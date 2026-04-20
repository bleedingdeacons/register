using Microsoft.EntityFrameworkCore;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Services;
using TheBleedingDeacons.Unity.Models;
using Group = TheBleedingDeacons.Unity.Intergroup.Entities.Group;
using Member = TheBleedingDeacons.Unity.Intergroup.Entities.Member;
using Position = TheBleedingDeacons.Unity.Intergroup.Entities.Position;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Orchestrates the full local-replica lifecycle:
///
///   1. <b>Start of session</b> — caller invokes
///      <see cref="DataService.ImportWithSnapshotAsync"/>:
///      Pull everything from Unity → capture a baseline snapshot.
///
///   2. <b>During session</b> — the Register app edits entities freely.
///      Every <see cref="UnityDbContext.SaveChangesAsync"/> call stamps
///      <c>Updated = DateTime.UtcNow</c> on touched entities.
///      Registrations are additionally written to
///      <see cref="RegistrationEventLog"/> for crash durability.
///
///   3. <b>End of session / refresh</b> — <see cref="ReconcileAsync"/>:
///      Replay the event log (if any pending entries) → detect what changed
///      locally (by diffing current state against the snapshot) → push those
///      changes to the Unity API in the correct dependency order → re-sync
///      and re-snapshot → purge the event log.
///
/// <b>Dependency ordering</b>: locally-created members (negative temp IDs)
/// must be created on Unity first so a real ID is returned. That real ID
/// is then used for any subsequent registration calls that reference
/// the member.
///
/// <b>Durability ordering</b>: the event log is purged only AFTER a clean
/// reconcile (no API errors) and a fresh snapshot. If anything goes wrong
/// the log is preserved so the next attempt can replay it.
///
/// <b>Context lifetime</b>: <see cref="ReconcileAsync"/> owns a single
/// DbContext for the duration of the call — reconciliation is a unit of
/// work with a consistent view of the DB. The detect helpers receive
/// that context as a parameter rather than opening their own.
/// </summary>
public class ReconciliationService
{
	private static readonly ILogger Logger = AppLogger.ForContext<ReconciliationService>();

	private readonly SnapshotService _snapshotService;
	private readonly UnitySyncService _syncService;
	private readonly IConfigurationService _configService;
	private readonly Func<Task<UnityRestSharp>> _clientFactory;
	private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
	private readonly RegistrationEventLog _eventLog;

	public ReconciliationService(
		SnapshotService snapshotService,
		UnitySyncService syncService,
		IConfigurationService configService,
		Func<Task<UnityRestSharp>> clientFactory,
		IDbContextFactory<UnityDbContext> dbContextFactory,
		RegistrationEventLog eventLog)
	{
		_snapshotService = snapshotService;
		_syncService = syncService;
		_configService = configService;
		_clientFactory = clientFactory;
		_dbContextFactory = dbContextFactory;
		_eventLog = eventLog;
	}

	// =====================================================================
	// Result records
	// =====================================================================

	public record ReconcileResult(
		int CreatedMembers,
		int ModifiedMembers,
		int RegisteredGroups,
		int UnregisteredGroups,
		int RegisteredPositions,
		int UnregisteredPositions,
		int ApiErrors,
		int ApiWarnings,
		UnitySyncService.SyncResult Resync,
		SnapshotService.SnapshotResult Snapshot);

	// =====================================================================
	// Reconcile — detect, push, re-sync
	// =====================================================================

	/// <summary>
	/// Detects all local changes since the last snapshot, pushes them to
	/// the Unity API in dependency order, then performs a fresh sync and
	/// captures a new snapshot.
	/// </summary>
	public async Task<ReconcileResult> ReconcileAsync(CancellationToken ct = default)
	{
		// One context for the whole reconcile — detect / push / stamp
		// all share a consistent view of local state.
		await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

		// ── Pre-phase: replay event log ──────────────────────────────
		// If the DB was lost or corrupted between a registration and now,
		// this rebuilds the Registered flags so the snapshot diff below
		// will detect them exactly as if the user had just made the
		// changes in the current session. No-op when the log is empty.
		if (_eventLog.HasPendingEntries())
		{
			Logger.Information("ReconcileAsync: registration log has pending entries — replaying before diff");
			var replay = await _eventLog.ReplayIntoDatabaseAsync(db, ct);
			Logger.Information(
				"Replay applied {Groups} group(s), {Positions} position(s); {Missing} skipped (entity not in DB)",
				replay.GroupsApplied, replay.PositionsApplied, replay.MissingEntities);
		}

		var hasSnapshot = await _snapshotService.HasSnapshotAsync(ct);
		if (!hasSnapshot)
		{
			Logger.Warning("ReconcileAsync: no snapshot exists — performing plain sync + snapshot");
			var sync = await _syncService.SyncAsync(ct);
			var snap = await _snapshotService.CaptureAsync(ct);
			return new ReconcileResult(0, 0, 0, 0, 0, 0, 0, 0, sync, snap);
		}

		using var client = await _clientFactory();

		int createdMembers = 0, modifiedMembers = 0;
		int registeredGroups = 0, unregisteredGroups = 0;
		int registeredPositions = 0, unregisteredPositions = 0;
		int apiErrors = 0;
		int apiWarnings = 0;

		// Maps old temporary (negative) member ID → real Unity ID
		var tempIdToRealId = new Dictionary<int, int>();

		// ── Phase 1: Create new members on Unity ─────────────────────
		// These have negative temporary IDs assigned by TemporaryIdGenerator.
		var newMembers = await db.Members
			.Where(m => m.Id < 0)
			.Include(m => m.HomeGroup)
			.Include(m => m.IntergroupPosition)
			.AsNoTracking()
			.ToListAsync(ct);

		foreach (var member in newMembers)
		{
			try
			{
				var request = new CreateMemberRequest
				{
					AnonymousName = member.AnonymousName,
					PersonalEmail = member.PersonalEmail,
					MobileNumber = member.MobileNumber,
					HomeGroupId = member.HomeGroupId,
					IsGsr = member.IsGsr,
					IntergroupPositionId = member.IntergroupPositionId,
					IntergroupPositionRotation = member.IntergroupPositionRotation,
				};

				var response = await client.CreateMemberAsync(request, ct);
				if (response.Success && response.Data != null)
				{
					tempIdToRealId[member.Id] = response.Data.Id;
					createdMembers++;
					Logger.Information(
						"Created member {Name} on Unity: temp {TempId} → real {RealId}",
						member.AnonymousName, member.Id, response.Data.Id);
				}
				else
				{
					apiWarnings++;
					Logger.Warning(
						"Failed to create member {Name} on Unity: {Error}",
						member.AnonymousName, response.Error?.Message);
				}
			}
			catch (Exception ex)
			{
				apiErrors++;
				Logger.Error(ex, "Exception creating member {Name} on Unity", member.AnonymousName);
			}
		}

		// ── Phase 2: Push modified members to Unity ──────────────────
		var modifiedMemberChanges = await DetectModifiedMembersAsync(db, ct);
		foreach (var (member, changedProps) in modifiedMemberChanges)
		{
			// Skip temp members — they were just created above
			if (member.Id < 0) continue;

			try
			{
				var request = new UpdateMemberRequest
				{
					AnonymousName = changedProps.Contains(nameof(Member.AnonymousName)) ? member.AnonymousName : null,
					PersonalEmail = changedProps.Contains(nameof(Member.PersonalEmail)) ? member.PersonalEmail : null,
					MobileNumber = changedProps.Contains(nameof(Member.MobileNumber)) ? member.MobileNumber : null,
					HomeGroupId = changedProps.Contains(nameof(Member.HomeGroupId)) ? member.HomeGroupId : null,
					IsGsr = changedProps.Contains(nameof(Member.IsGsr)) ? member.IsGsr : null,
					IntergroupPositionId = changedProps.Contains(nameof(Member.IntergroupPositionId))
						? member.IntergroupPositionId : null,
					IntergroupPositionRotation = changedProps.Contains(nameof(Member.IntergroupPositionRotation))
						? member.IntergroupPositionRotation : null,
				};

				var response = await client.UpdateMemberAsync(member.Id, request, ct);
				if (response.Success)
				{
					modifiedMembers++;
					Logger.Information("Updated member {Name} (ID={Id}) on Unity", member.AnonymousName, member.Id);
				}
				else
				{
					apiWarnings++;
					Logger.Warning("Failed to update member {Id} on Unity: {Error}", member.Id, response.Error?.Message);
				}
			}
			catch (Exception ex)
			{
				apiErrors++;
				Logger.Error(ex, "Exception updating member {Id} on Unity", member.Id);
			}
		}

		// ── Phase 3: Push registration changes to Unity ──────────────
		// Now that all members have real IDs, we can register/unregister.

		var config = await _configService.LoadUnityConfigurationAsync();
		if (config.ActiveIntergroupMeetingId.HasValue)
		{
			var meetingId = config.ActiveIntergroupMeetingId.Value;

			// Group registrations
			var groupChanges = await DetectGroupRegistrationChangesAsync(db, ct);
			foreach (var (group, registered) in groupChanges)
			{
				try
				{
					if (registered)
					{
						// Find the GSR for this group — resolve temp IDs
						var gsr = await db.Members
							.Where(m => m.HomeGroupId == group.Id && m.IsGsr)
							.FirstOrDefaultAsync(ct);

						var gsrName = gsr?.AnonymousName ?? string.Empty;
						var memberId = gsr != null
							? (tempIdToRealId.TryGetValue(gsr.Id, out var realId) ? realId : gsr.Id)
							: 0;

						var response = await client.RegisterGroupAsync(
							meetingId, group.Id, memberId, gsrName,
							gsrProxy: group.GsrProxy, gsrProxyName: group.GsrProxyName, ct);

						if (response.Success)
						{
							registeredGroups++;
							Logger.Information("Registered group {Name} (ID={Id}) on Unity", group.Name, group.Id);
						}
						else if (IsAlreadyRegisteredError(response.Error))
						{
							registeredGroups++;
							Logger.Information("Group {Name} (ID={Id}) was already registered on Unity — treating as success",
								group.Name, group.Id);
						}
						else
						{
							apiErrors++;
							Logger.Error("Failed to register group {Id}: {Error}", group.Id, response.Error?.Message);
						}
					}
					else
					{
						var response = await client.UnregisterGroupAsync(meetingId, group.Id, ct);
						if (response.Success)
						{
							unregisteredGroups++;
							Logger.Information("Unregistered group {Name} (ID={Id}) on Unity", group.Name, group.Id);
						}
						else
						{
							apiErrors++;
							Logger.Error("Failed to unregister group {Id}: {Error}", group.Id, response.Error?.Message);
						}
					}
				}
				catch (Exception ex)
				{
					apiErrors++;
					Logger.Error(ex, "Exception processing group registration for {Id}", group.Id);
				}
			}

			// Position / officer registrations
			var positionChanges = await DetectPositionRegistrationChangesAsync(db, ct);
			foreach (var (position, registered) in positionChanges)
			{
				try
				{
					// Find the holder for this position — resolve temp IDs
					var holder = await db.Members
						.Where(m => m.IntergroupPositionId == position.Id)
						.FirstOrDefaultAsync(ct);

					if (registered)
					{
						if (holder == null)
						{
							Logger.Warning("Position {Name} registered but has no holder — skipping API call",
								position.ShortDescription);
							continue;
						}

						var officerId = tempIdToRealId.TryGetValue(holder.Id, out var realId) ? realId : holder.Id;
						var officerName = holder.AnonymousName;
						var positionName = position.ShortDescription ?? position.LongName ?? string.Empty;

						var response = await client.RegisterOfficerAsync(
							meetingId, officerId, positionName, officerName, ct);

						if (response.Success)
						{
							registeredPositions++;
							Logger.Information("Registered officer {Name} for position {Position} on Unity",
								officerName, positionName);
						}
						else if (IsAlreadyRegisteredError(response.Error))
						{
							registeredPositions++;
							Logger.Information("Officer {Name} for position {Position} was already registered on Unity — treating as success",
								officerName, positionName);
						}
						else
						{
							apiErrors++;
							Logger.Error("Failed to register officer for position {Id}: {Error}",
								position.Id, response.Error?.Message);
						}
					}
					else
					{
						if (holder == null) continue;

						var officerId = tempIdToRealId.TryGetValue(holder.Id, out var realId) ? realId : holder.Id;

						var response = await client.UnregisterOfficerAsync(meetingId, officerId, ct);
						if (response.Success)
						{
							unregisteredPositions++;
							Logger.Information("Unregistered officer {Id} on Unity", officerId);
						}
						else
						{
							apiErrors++;
							Logger.Error("Failed to unregister officer {Id}: {Error}",
								officerId, response.Error?.Message);
						}
					}
				}
				catch (Exception ex)
				{
					apiErrors++;
					Logger.Error(ex, "Exception processing position registration for {Id}", position.Id);
				}
			}
		}
		else
		{
			Logger.Information("No active intergroup meeting set — skipping registration push");
		}

		// ── Phase 4: Re-sync from Unity to get authoritative state ───
		Logger.Information("Reconciliation API calls complete — re-syncing from Unity");
		var syncResult = await _syncService.SyncAsync(ct);

		// ── Phase 5: Capture fresh snapshot ──────────────────────────
		var snapshotResult = await _snapshotService.CaptureAsync(ct);

		// ── Phase 6: Purge the durability log ────────────────────────
		if (apiErrors == 0)
		{
			try
			{
				await _eventLog.PurgeAsync(ct);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Reconciliation succeeded but failed to purge registration log");
			}
		}
		else
		{
			Logger.Warning(
				"Reconciliation had {Errors} API error(s) — keeping registration log for next reconcile attempt",
				apiErrors);
		}

		Logger.Information(
			"Reconciliation complete: {Created} created, {Modified} modified, " +
			"{RegGroups} groups registered, {UnregGroups} unregistered, " +
			"{RegPos} positions registered, {UnregPos} unregistered, " +
			"{Errors} API errors, {Warnings} API warnings",
			createdMembers, modifiedMembers,
			registeredGroups, unregisteredGroups,
			registeredPositions, unregisteredPositions,
			apiErrors, apiWarnings);

		return new ReconcileResult(
			createdMembers, modifiedMembers,
			registeredGroups, unregisteredGroups,
			registeredPositions, unregisteredPositions,
			apiErrors, apiWarnings, syncResult, snapshotResult);
	}

	// =====================================================================
	// Change detection — diff current entities against snapshot
	//
	// These receive the DbContext as a parameter so they share
	// ReconcileAsync's unit-of-work context.
	// =====================================================================

	/// <summary>
	/// Returns members that existed in the snapshot but have been modified
	/// locally, along with the names of the properties that changed.
	/// </summary>
	private async Task<List<(Member Member, HashSet<string> ChangedProperties)>>
		DetectModifiedMembersAsync(UnityDbContext db, CancellationToken ct)
	{
		var snapshots = await _snapshotService.GetSnapshotsAsync("Member", ct);
		var snapshotMap = snapshots.ToDictionary(
			s => s.EntityKey,
			s => SnapshotService.Deserialise<Member>(s));

		var currentMembers = await db.Members.AsNoTracking().ToListAsync(ct);
		var result = new List<(Member, HashSet<string>)>();

		foreach (var member in currentMembers)
		{
			if (!snapshotMap.TryGetValue(member.Id, out var original) || original == null)
				continue;

			var changed = new HashSet<string>();

			if (original.AnonymousName != member.AnonymousName) changed.Add(nameof(Member.AnonymousName));
			if (original.PrivateName != member.PrivateName) changed.Add(nameof(Member.PrivateName));
			if (original.Email != member.Email) changed.Add(nameof(Member.Email));
			if (original.PersonalEmail != member.PersonalEmail) changed.Add(nameof(Member.PersonalEmail));
			if (original.MobileNumber != member.MobileNumber) changed.Add(nameof(Member.MobileNumber));
			if (original.IsGsr != member.IsGsr) changed.Add(nameof(Member.IsGsr));
			if (original.HomeGroupId != member.HomeGroupId) changed.Add(nameof(Member.HomeGroupId));
			if (original.IntergroupPositionId != member.IntergroupPositionId) changed.Add(nameof(Member.IntergroupPositionId));
			if (original.IntergroupPositionRotation != member.IntergroupPositionRotation) changed.Add(nameof(Member.IntergroupPositionRotation));

			if (changed.Count > 0)
				result.Add((member, changed));
		}

		return result;
	}

	/// <summary>
	/// Returns groups whose <c>Registered</c> flag changed since the snapshot.
	/// </summary>
	private async Task<List<(Group Group, bool Registered)>>
		DetectGroupRegistrationChangesAsync(UnityDbContext db, CancellationToken ct)
	{
		var snapshots = await _snapshotService.GetSnapshotsAsync("Group", ct);
		var snapshotMap = snapshots.ToDictionary(
			s => s.EntityKey,
			s => SnapshotService.Deserialise<Group>(s));

		var currentGroups = await db.Groups.AsNoTracking().ToListAsync(ct);
		var result = new List<(Group, bool)>();

		foreach (var group in currentGroups)
		{
			if (!snapshotMap.TryGetValue(group.Id, out var original) || original == null)
				continue;

			if (original.Registered != group.Registered)
				result.Add((group, group.Registered));
		}

		return result;
	}

	/// <summary>
	/// Returns positions whose <c>Registered</c> flag changed since the snapshot.
	/// </summary>
	private async Task<List<(Position Position, bool Registered)>>
		DetectPositionRegistrationChangesAsync(UnityDbContext db, CancellationToken ct)
	{
		var snapshots = await _snapshotService.GetSnapshotsAsync("Position", ct);
		var snapshotMap = snapshots.ToDictionary(
			s => s.EntityKey,
			s => SnapshotService.Deserialise<Position>(s));

		var currentPositions = await db.Positions.AsNoTracking().ToListAsync(ct);
		var result = new List<(Position, bool)>();

		foreach (var position in currentPositions)
		{
			if (!snapshotMap.TryGetValue(position.Id, out var original) || original == null)
				continue;

			if (original.Registered != position.Registered)
				result.Add((position, position.Registered));
		}

		return result;
	}

	/// <summary>
	/// Returns true when the API error indicates the group/position is already
	/// registered for this meeting. Treated as success on register-calls so the
	/// user can still proceed to Completed and purge the database.
	/// </summary>
	private static bool IsAlreadyRegisteredError(ApiError? error)
	{
		if (error is null) return false;

		var code = error.Code?.ToLowerInvariant() ?? string.Empty;
		var msg = error.Message?.ToLowerInvariant() ?? string.Empty;

		return code.Contains("already") || code.Contains("duplicate") || code.Contains("exists")
			|| msg.Contains("already registered") || msg.Contains("already exists");
	}
}