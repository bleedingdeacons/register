using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Services;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Append-only, crash-durable log of registration state for the current meeting.
///
/// Each line is a JSON record representing the <i>current</i> registered state
/// of a group or position (upsert semantics — the latest line for a given
/// entity wins). This makes the log self-healing: a torn final write only
/// loses the most recent state change, not the whole meeting.
///
/// The log is an independent durability layer, parallel to the SQLite
/// database. If SQLite is corrupted or lost between a registration and the
/// end-of-meeting reconcile, <see cref="ReplayIntoDatabaseAsync"/> rebuilds
/// the <c>Registered</c> flags on the local entities so reconciliation can
/// push them to Unity normally.
///
/// File lives at <see cref="GetDefaultLogPath"/> — the user's Documents folder
/// on desktop platforms, or a sensible platform-appropriate equivalent elsewhere.
/// Callers must invoke <see cref="PurgeAsync"/> only AFTER a successful
/// reconciliation — never before.
/// </summary>
public sealed class RegistrationEventLog : IAsyncDisposable
{
	private static readonly ILogger Logger = AppLogger.ForContext<RegistrationEventLog>();

	// JSON options match SnapshotService for consistency.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	/// <summary>
	/// Kinds of entities the log tracks. Keep values stable — they're
	/// persisted on disk and compared as strings during replay.
	/// </summary>
	public enum EntityKind { Group, Position }

	private readonly string _logPath;

	// Serialises appends across threads. Two GSRs can't physically tap at
	// the same time, but ViewModel commands can fire concurrently on the
	// thread pool so the lock is not paranoia.
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	public RegistrationEventLog()
		: this(GetDefaultLogPath()) { }

	// Constructor overload for tests / non-MAUI hosts.
	internal RegistrationEventLog(string logPath)
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
	/// the directory if it doesn't yet exist. Placing the log in Documents
	/// (rather than <see cref="FileSystem.AppDataDirectory"/>) makes it
	/// visible to the user for inspection and to IT support for collection,
	/// and keeps it outside the app's sandbox-scoped data directory so
	/// uninstalling the app doesn't take the crash log with it.
	/// </summary>
	private static string GetDefaultLogPath()
	{
		// Environment.SpecialFolder.MyDocuments resolves to:
		//   • Windows  → %USERPROFILE%\Documents
		//   • macOS    → ~/Documents
		//   • iOS      → the app's Documents directory (sandbox; still the
		//                right place — it's user-visible via the Files app)
		//   • Android  → the app's private files dir; the public Documents
		//                folder is not directly available through
		//                Environment.SpecialFolder, so we fall back below.
		var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		if (string.IsNullOrEmpty(documents))
		{
			// Last-resort fallback for platforms where MyDocuments isn't
			// mapped. Keeps the app functional rather than crashing at
			// first registration.
			documents = FileSystem.AppDataDirectory;
		}

		try
		{
			Directory.CreateDirectory(documents);
		}
		catch (Exception ex)
		{
			// If we can't create or access the Documents folder for any
			// reason (permissions, read-only volume), fall back to the
			// app data directory rather than fail hard — the log is a
			// durability aid, not a feature the app can't start without.
			Logger.Warning(ex, "Could not prepare Documents folder {Path}; falling back to AppDataDirectory", documents);
			documents = FileSystem.AppDataDirectory;
		}

		return Path.Combine(documents, "registrations.log");
	}

	// ────────────────────────────────────────────────────────────────
	// Record shape
	// ────────────────────────────────────────────────────────────────

	/// <summary>
	/// One line in the log. Upsert semantics — for any given
	/// (<see cref="Kind"/>, <see cref="EntityId"/>) pair, the last record
	/// written wins. Older records for the same entity are superseded but
	/// not removed from the file.
	/// </summary>
	public sealed record Entry(
		DateTime TimestampUtc,
		EntityKind Kind,
		int EntityId,
		bool Registered,
		// Group-only fields. Null for Position entries.
		bool? GsrProxy = null,
		string? GsrProxyName = null);

	// ────────────────────────────────────────────────────────────────
	// Write path
	// ────────────────────────────────────────────────────────────────

	public Task AppendGroupAsync(int groupId, bool registered, bool gsrProxy, string? gsrProxyName, CancellationToken ct = default)
		=> AppendAsync(new Entry(DateTime.UtcNow, EntityKind.Group, groupId, registered, gsrProxy, gsrProxyName), ct);

	public Task AppendPositionAsync(int positionId, bool registered, CancellationToken ct = default)
		=> AppendAsync(new Entry(DateTime.UtcNow, EntityKind.Position, positionId, registered), ct);

	private async Task AppendAsync(Entry entry, CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(entry, JsonOptions);
		// Newline-delimited JSON: one complete record per line. A torn write
		// can leave a trailing partial line but cannot corrupt earlier lines.
		var line = json + "\n";
		var bytes = Encoding.UTF8.GetBytes(line);

		await _writeLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			// FileOptions.WriteThrough asks the OS to bypass its write cache
			// and write straight to the storage device. Combined with the
			// explicit FlushAsync(true) below this is our durability guarantee:
			// by the time this method returns, the bytes are on stable storage.
			//
			// Opening per-append rather than keeping a handle open is a
			// deliberate choice — it makes every registration independently
			// durable even if the process is killed before the next append.
			await using var fs = new FileStream(
				_logPath,
				FileMode.Append,
				FileAccess.Write,
				FileShare.Read,
				bufferSize: 4096,
				options: FileOptions.WriteThrough);

			await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
			// flushToDisk: true → fsync. Required on Android; WriteThrough
			// alone is not honoured by every filesystem/driver combination.
			await fs.FlushAsync(ct).ConfigureAwait(false);
			fs.Flush(flushToDisk: true);
		}
		catch (Exception ex)
		{
			// Never throw from the write path — we don't want a log failure
			// to mask a successful DB write. The DB is still the primary
			// record; the log is defence in depth.
			Logger.Error(ex, "Failed to append registration log entry {Kind} {Id}", entry.Kind, entry.EntityId);
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
	/// latest known state per (Kind, EntityId). Torn final lines that
	/// fail to parse are skipped and logged.
	/// </summary>
	public async Task<IReadOnlyDictionary<(EntityKind Kind, int EntityId), Entry>>
		ReadLatestStatesAsync(CancellationToken ct = default)
	{
		var latest = new Dictionary<(EntityKind, int), Entry>();

		if (!File.Exists(_logPath)) return latest;

		await _writeLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			// FileShare.ReadWrite so a concurrent append doesn't block us.
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
					// A torn last line is expected after a crash — count it
					// and move on. Earlier lines were fsync'd so they parse.
					torn++;
					continue;
				}

				if (entry is null) continue;
				latest[(entry.Kind, entry.EntityId)] = entry;
			}

			if (torn > 0)
				Logger.Warning("Registration log had {Torn} unparseable line(s) out of {Total}", torn, lineNum);
		}
		finally
		{
			_writeLock.Release();
		}

		return latest;
	}

	/// <summary>
	/// Rebuilds the <c>Registered</c> state on groups and positions in the
	/// local database from the log. Call at startup <b>after</b>
	/// <see cref="UnitySyncService.SyncAsync"/> has populated the entities
	/// from Unity but <b>before</b> reconciliation runs — so the flags
	/// represent "local changes since the last sync", ready to be diffed.
	///
	/// Skips entities whose IDs are not present in the DB (the sync may
	/// have removed a group since the last meeting).
	/// </summary>
	public async Task<ReplayResult> ReplayIntoDatabaseAsync(UnityDbContext db, CancellationToken ct = default)
	{
		var states = await ReadLatestStatesAsync(ct).ConfigureAwait(false);
		if (states.Count == 0)
			return new ReplayResult(0, 0, 0);

		int groupsApplied = 0, positionsApplied = 0, missing = 0;

		// Partition by kind so we can issue two bulk lookups instead of
		// one DB round-trip per entry.
		var groupEntries = states.Values.Where(e => e.Kind == EntityKind.Group).ToList();
		var positionEntries = states.Values.Where(e => e.Kind == EntityKind.Position).ToList();

		if (groupEntries.Count > 0)
		{
			var ids = groupEntries.Select(e => e.EntityId).ToHashSet();
			var groups = await db.Groups
				.Where(g => ids.Contains(g.Id))
				.ToDictionaryAsync(g => g.Id, ct)
				.ConfigureAwait(false);

			foreach (var entry in groupEntries)
			{
				if (!groups.TryGetValue(entry.EntityId, out var group))
				{
					missing++;
					continue;
				}

				group.Registered = entry.Registered;
				group.GsrProxy = entry.GsrProxy ?? false;
				group.GsrProxyName = (entry.GsrProxy ?? false) ? entry.GsrProxyName : null;
				groupsApplied++;
			}
		}

		if (positionEntries.Count > 0)
		{
			var ids = positionEntries.Select(e => e.EntityId).ToHashSet();
			var positions = await db.Positions
				.Where(p => ids.Contains(p.Id))
				.ToDictionaryAsync(p => p.Id, ct)
				.ConfigureAwait(false);

			foreach (var entry in positionEntries)
			{
				if (!positions.TryGetValue(entry.EntityId, out var position))
				{
					missing++;
					continue;
				}
				position.Registered = entry.Registered;
				positionsApplied++;
			}
		}

		if (groupsApplied + positionsApplied > 0)
		{
			// Suppress the Updated stamp — replay is reconstructing prior
			// user actions, not performing new ones. Keeping the original
			// timestamps (or at least not advancing them) is closer to truth.
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
			"Registration log replay applied {Groups} group(s), {Positions} position(s); {Missing} skipped (entity not in DB)",
			groupsApplied, positionsApplied, missing);

		return new ReplayResult(groupsApplied, positionsApplied, missing);
	}

	public record ReplayResult(int GroupsApplied, int PositionsApplied, int MissingEntities);

	// ────────────────────────────────────────────────────────────────
	// Purge
	// ────────────────────────────────────────────────────────────────

	/// <summary>
	/// Deletes the log file. Call ONLY after a successful reconciliation has
	/// confirmed the Unity server holds everything the log described.
	/// Safe to call when the log does not exist.
	/// </summary>
	/// <remarks>
	/// We delete rather than truncate because truncation races with any
	/// in-flight append on another thread. File deletion is atomic at the
	/// filesystem level; the next append will recreate the file.
	/// </remarks>
	public async Task PurgeAsync(CancellationToken ct = default)
	{
		await _writeLock.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (File.Exists(_logPath))
			{
				File.Delete(_logPath);
				Logger.Information("Registration log purged");
			}
		}
		catch (Exception ex)
		{
			// A failed purge is a real problem — next meeting could start
			// with stale state that replay would wrongly resurrect. Log
			// loudly and rethrow so the caller can surface it.
			Logger.Error(ex, "Failed to purge registration log at {Path}", _logPath);
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