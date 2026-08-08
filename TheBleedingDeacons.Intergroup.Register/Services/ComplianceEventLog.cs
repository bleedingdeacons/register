using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Append-only, crash-durable log of GDPR compliance state changes.
///
/// Mirrors <see cref="RegistrationEventLog"/> in shape and durability
/// guarantees — see that file for the rationale around fsync, torn
/// last-line tolerance, FileShare semantics, and the upsert read model.
///
/// The log captures one entity kind only (Member) so the entry shape
/// is flatter than the registration log: each line records the latest
/// known compliance state for one member.
///
/// The file lives next to <c>registrations.log</c> in the user's
/// Documents folder so IT support collecting one will collect the other.
///
/// Callers must invoke <see cref="PurgeAsync"/> only AFTER a successful
/// reconciliation has confirmed the Unity server holds everything the
/// log described — never before.
/// </summary>
public sealed class ComplianceEventLog : IAsyncDisposable
{
	private static readonly ILogger Logger = AppLogger.ForContext<ComplianceEventLog>();

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private readonly string _logPath;
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	public ComplianceEventLog()
		: this(GetDefaultLogPath()) { }

	// Constructor overload for tests / non-MAUI hosts.
	internal ComplianceEventLog(string logPath)
	{
		_logPath = logPath;
	}

	/// <summary>
	/// Absolute path of the log file on disk. Exposed so callers — e.g. the
	/// Settings "Reset Device" command — can check for existence and delete
	/// the file without duplicating the path logic.
	/// </summary>
	public string LogPath => _logPath;

	/// <summary>
	/// Resolves the log file path on the user's Documents folder, creating
	/// the directory if it doesn't yet exist. See
	/// <see cref="RegistrationEventLog"/> for the platform-by-platform
	/// rationale; this method follows the identical fallback ladder so
	/// both logs always end up in the same directory.
	/// </summary>
	private static string GetDefaultLogPath()
	{
		var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		if (string.IsNullOrEmpty(documents))
			documents = FileSystem.AppDataDirectory;

		try
		{
			Directory.CreateDirectory(documents);
		}
		catch (Exception ex)
		{
			Logger.Warning(ex, "Could not prepare Documents folder {Path}; falling back to AppDataDirectory", documents);
			documents = FileSystem.AppDataDirectory;
		}

		return Path.Combine(documents, "compliance.log");
	}

	// ────────────────────────────────────────────────────────────────
	// Record shape
	// ────────────────────────────────────────────────────────────────

	/// <summary>
	/// One line in the log. Upsert semantics — for any given
	/// <see cref="MemberId"/>, the last record written wins. Earlier
	/// records for the same member are superseded but not removed
	/// from the file; that's how a torn final write costs only the
	/// most recent state change rather than the whole session.
	///
	/// <para>
	/// Mirrors the fields the Unity API's compliance endpoint accepts
	/// and stores. Field names are JSON-serialised camelCase (matching
	/// the JSON options); they're independent of the Unity server's
	/// snake_case wire format because this log is read back by this
	/// same process, not by the server.
	/// </para>
	///
	/// <para>
	/// <see cref="PolicyId"/> is the WordPress post ID of the privacy
	/// policy the member accepted. Sent to Unity on reconcile in place
	/// of the statement body — Unity resolves the body itself via the
	/// Scrutiny repository — so the entry needs to carry it across a
	/// process restart. Older log lines written before this field
	/// existed deserialise with <c>PolicyId = null</c>, which the
	/// reconcile push treats the same way as "no id known": send the
	/// other compliance fields, omit policy_id, and the server falls
	/// back to recording an empty statement (the same fallback used
	/// for fresh devices that haven't synced a policy yet).
	/// </para>
	/// </summary>
	public sealed record Entry(
		DateTime TimestampUtc,
		int MemberId,
		bool Accepted,
		DateTime AcceptedAt,
		string? Version,
		string? Method,
		string? Statement,
		int? PolicyId = null);

	// ────────────────────────────────────────────────────────────────
	// Write path
	// ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Appends an acceptance entry. <paramref name="acceptedAt"/> is the
	/// timestamp the user accepted at — passed through to the Unity
	/// server verbatim during reconciliation, so callers should pass
	/// UTC values (use <c>DateTime.UtcNow</c> when in doubt).
	/// <paramref name="policyId"/> is the WordPress post ID of the
	/// accepted policy, sent to the server on reconcile in place of
	/// the statement body; null when the device has never synced a
	/// policy and the acceptance is being recorded "wording unknown".
	/// </summary>
	public Task AppendAcceptanceAsync(
		int memberId,
		DateTime acceptedAt,
		string? version,
		string? method,
		string? statement,
		int? policyId,
		CancellationToken ct = default)
		=> AppendAsync(
			new Entry(DateTime.UtcNow, memberId, Accepted: true, acceptedAt, version, method, statement, policyId),
			ct);

	/// <summary>
	/// Appends a revocation entry. The server clears version, method,
	/// and statement on revocation so we don't carry them in the entry —
	/// keeping them null also makes a torn write that lands on this
	/// type of entry indistinguishable from a "no metadata available"
	/// acceptance, which is the safest fallback if a partial parse ever
	/// occurs.
	/// </summary>
	public Task AppendRevocationAsync(int memberId, DateTime revokedAt, CancellationToken ct = default)
		=> AppendAsync(
			new Entry(DateTime.UtcNow, memberId, Accepted: false, revokedAt, Version: null, Method: null, Statement: null),
			ct);

	private async Task AppendAsync(Entry entry, CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(entry, JsonOptions);
		var line = json + "\n";
		var bytes = Encoding.UTF8.GetBytes(line);

		await _writeLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			// Same durability dance as RegistrationEventLog: WriteThrough
			// asks the OS to skip its cache, and Flush(flushToDisk: true)
			// issues an explicit fsync. By the time this method returns,
			// the bytes are on stable storage on every supported platform,
			// including Android (which doesn't always honour WriteThrough
			// without an explicit fsync).
			//
			// Per-append open-and-close trades a little throughput for
			// strong independent durability of each entry.
			await using var fs = new FileStream(
				_logPath,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read,
				bufferSize: 4096,
				options: FileOptions.WriteThrough);

			await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
			await fs.FlushAsync(ct).ConfigureAwait(false);
			fs.Flush(flushToDisk: true);
		}
		catch (Exception ex)
		{
			// Never throw from the write path — the DB is the primary
			// record and has already succeeded by the time we're here.
			// A log failure must not mask that.
			Logger.Error(ex, "Failed to append compliance log entry for member {MemberId}", entry.MemberId);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	// ────────────────────────────────────────────────────────────────
	// Read / replay path
	// ────────────────────────────────────────────────────────────────

	/// <summary>
	/// True when the log file exists and contains at least one byte.
	/// Used at startup to decide whether a replay is warranted.
	/// </summary>
	public bool HasPendingEntries()
	{
		try
		{
			var info = new FileInfo(_logPath);
			return info.Exists && info.Length > 0;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Reads every complete line in the log and collapses it into the
	/// latest known state per member. Torn final lines that fail to parse
	/// are skipped and counted; well-formed earlier lines are unaffected.
	/// </summary>
	public async Task<IReadOnlyDictionary<int, Entry>> ReadLatestStatesAsync(CancellationToken ct = default)
	{
		var latest = new Dictionary<int, Entry>();

		if (!File.Exists(_logPath)) return latest;

		await _writeLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			await using var fs = new FileStream(
				_logPath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.ReadWrite);
			using var reader = new StreamReader(fs, Encoding.UTF8);

			int lineNum = 0;
			int torn = 0;
			// Loop on ReadLineAsync's null rather than on EndOfStream: the
			// latter has to peek at the underlying stream to answer, and does
			// so synchronously — a blocking read on every iteration of an
			// otherwise async loop. .NET 10's CA2024 flags exactly this.
			string? line;
			while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
			{
				ct.ThrowIfCancellationRequested();
				lineNum++;
				if (string.IsNullOrWhiteSpace(line)) continue;

				Entry? entry;
				try
				{
					entry = JsonSerializer.Deserialize<Entry>(line, JsonOptions);
				}
				catch (JsonException)
				{
					torn++;
					continue;
				}

				if (entry is null) continue;
				latest[entry.MemberId] = entry;
			}

			if (torn > 0)
				Logger.Warning("Compliance log had {Torn} unparseable line(s) out of {Total}", torn, lineNum);
		}
		finally
		{
			_writeLock.Release();
		}

		return latest;
	}

	/// <summary>
	/// Rebuilds the GDPR compliance fields on members in the local
	/// database from the log. Call at startup <b>after</b>
	/// <c>UnitySyncService.SyncAsync</c> has populated the entities
	/// from Unity but <b>before</b> reconciliation runs — so the
	/// fields represent "local changes since the last sync", ready
	/// to be diffed.
	///
	/// Skips entries whose member IDs are not present in the DB —
	/// the most recent sync may have removed or renumbered the
	/// member.
	/// </summary>
	public async Task<ReplayResult> ReplayIntoDatabaseAsync(UnityDbContext db, CancellationToken ct = default)
	{
		var states = await ReadLatestStatesAsync(ct).ConfigureAwait(false);
		if (states.Count == 0)
			return new ReplayResult(0, 0);

		int applied = 0, missing = 0;

		// Bulk-load all targeted members in a single query rather than
		// one round-trip per entry.
		var ids = states.Keys.ToHashSet();
		var members = await db.Members
			.Where(m => ids.Contains(m.Id))
			.ToDictionaryAsync(m => m.Id, ct)
			.ConfigureAwait(false);

		foreach (var (memberId, entry) in states)
		{
			if (!members.TryGetValue(memberId, out var member))
			{
				missing++;
				continue;
			}

			ApplyEntryToMember(member, entry);
			applied++;
		}

		if (applied > 0)
		{
			// Suppress the Updated stamp — replay is reconstructing prior
			// user actions, not performing new ones, so we don't want the
			// reconcile snapshot diff to wrongly attribute the timestamp
			// shift to a "modification" that wasn't really new.
			db.SuppressUpdatedStamp = true;
			try
			{
				await db.SaveChangesAsync(ct).ConfigureAwait(false);
			}
			finally
			{
				db.SuppressUpdatedStamp = false;
			}
		}

		Logger.Information(
			"Compliance log replay applied {Applied} member(s); {Missing} skipped (not in DB)",
			applied, missing);

		return new ReplayResult(applied, missing);
	}

	/// <summary>
	/// Applies a single log entry's compliance state to a Member entity.
	/// Public-static so reconciliation and replay share the same field-
	/// to-field mapping rules — there's no risk of replay setting one
	/// shape and reconcile-push reading another.
	/// </summary>
	internal static void ApplyEntryToMember(
		TheBleedingDeacons.Unity.Intergroup.Entities.Member member,
		Entry entry)
	{
		member.GdprAccepted = entry.Accepted;
		member.GdprAcceptedAt = entry.AcceptedAt;

		if (entry.Accepted)
		{
			member.GdprAcceptanceVersion = entry.Version;
			member.GdprAcceptanceMethod = entry.Method;
			member.GdprAcceptanceStatement = entry.Statement;
			member.GdprAcceptancePolicyId = entry.PolicyId;
		}
		else
		{
			// Revocation — clear metadata that belonged to the prior
			// acceptance. Same rule the Unity server applies on its side,
			// so reconcile push and offline state stay congruent.
			member.GdprAcceptanceVersion = null;
			member.GdprAcceptanceMethod = null;
			member.GdprAcceptanceStatement = null;
			member.GdprAcceptancePolicyId = null;
		}
	}

	public record ReplayResult(int Applied, int MissingEntities);

	// ────────────────────────────────────────────────────────────────
	// Purge
	// ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Deletes the log file. Call ONLY after a successful reconciliation
	/// has confirmed the Unity server holds everything the log described.
	/// Safe to call when the log does not exist.
	/// </summary>
	public async Task PurgeAsync(CancellationToken ct = default)
	{
		await _writeLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (File.Exists(_logPath))
			{
				File.Delete(_logPath);
				Logger.Information("Compliance log purged");
			}
		}
		catch (Exception ex)
		{
			// Same loud-failure rationale as RegistrationEventLog: a
			// failed purge could see stale state resurrected on next
			// startup.
			Logger.Error(ex, "Failed to purge compliance log at {Path}", _logPath);
			throw;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public ValueTask DisposeAsync()
	{
		_writeLock.Dispose();
		return ValueTask.CompletedTask;
	}
}
