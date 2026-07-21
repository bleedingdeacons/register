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
///      changes to the Unity API in the correct dependency order → purge
///      the event log.
///
/// <b>Dependency ordering</b>: locally-created members (negative temp IDs)
/// must be created on Unity first so a real ID is returned. That real ID
/// is then used for any subsequent registration calls that reference
/// the member.
///
/// <b>Durability ordering</b>: the event log is purged only AFTER a clean
/// reconcile (no API errors). If anything goes wrong the log is preserved
/// so the next attempt can replay it.
///
/// <b>Context lifetime</b>: <see cref="ReconcileAsync"/> owns a single
/// DbContext for the duration of the call — reconciliation is a unit of
/// work with a consistent view of the DB. The detect helpers receive
/// that context as a parameter rather than opening their own.
/// </summary>
public class ReconciliationService
{
	private static readonly ILogger Logger = AppLogger.ForContext<ReconciliationService>();

	/// <summary>
	/// Sentinel key written into the modified-members "changed properties"
	/// set to indicate that one or more of the five GDPR compliance fields
	/// differs from the snapshot. Not a real property name on
	/// <see cref="Member"/>; chosen so it can never collide with a
	/// <c>nameof(Member.X)</c> result.
	/// </summary>
	private const string GdprComplianceKey = "__gdpr_compliance__";

	private readonly SnapshotService _snapshotService;
	private readonly UnitySyncService _syncService;
	private readonly IConfigurationService _configService;
	private readonly Func<Task<UnityRestSharp>> _clientFactory;
	private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
	private readonly RegistrationEventLog _eventLog;
	private readonly ComplianceEventLog _complianceLog;

	public ReconciliationService(
		SnapshotService snapshotService,
		UnitySyncService syncService,
		IConfigurationService configService,
		Func<Task<UnityRestSharp>> clientFactory,
		IDbContextFactory<UnityDbContext> dbContextFactory,
		RegistrationEventLog eventLog,
		ComplianceEventLog complianceLog)
	{
		_snapshotService = snapshotService;
		_syncService = syncService;
		_configService = configService;
		_clientFactory = clientFactory;
		_dbContextFactory = dbContextFactory;
		_eventLog = eventLog;
		_complianceLog = complianceLog;
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
		int RecordedCompliance,
		int ApiErrors,
		int ApiWarnings);

	// =====================================================================
	// Reconcile — detect and push
	// =====================================================================

	/// <summary>
	/// Detects all local changes since the last snapshot and pushes them
	/// to the Unity API in dependency order. Does not re-sync or
	/// re-snapshot afterwards: callers (Finish Meeting) are expected to
	/// either purge the local DB or treat the local state as terminal.
	///
	/// <para>
	/// When <paramref name="progress"/> is supplied, emits
	/// <see cref="SyncProgress"/> updates at every phase boundary and on a
	/// per-item basis during the API push loops. Pass <c>null</c> to run
	/// without reporting (e.g. during a headless retry).
	/// </para>
	/// </summary>
	public async Task<ReconcileResult> ReconcileAsync(
		CancellationToken ct = default,
		IProgress<SyncProgress>? progress = null)
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
			progress?.Report(new SyncProgress(
				SyncStage.ReplayingLog,
				"Replaying registration log…"));

			Logger.Information("ReconcileAsync: registration log has pending entries — replaying before diff");
			var replay = await _eventLog.ReplayIntoDatabaseAsync(db, ct);
			Logger.Information(
				"Replay applied {Groups} group(s), {Positions} position(s); {Missing} skipped (entity not in DB)",
				replay.GroupsApplied, replay.PositionsApplied, replay.MissingEntities);
		}

		// Same dance for the compliance log — independent of attendance,
		// so a reconcile that finds only one of the two log files will
		// still replay correctly. The order between the two doesn't
		// matter (different fields, no shared keys), but doing
		// registration first matches the order the data was originally
		// written if both logs received entries in the same session.
		if (_complianceLog.HasPendingEntries())
		{
			progress?.Report(new SyncProgress(
				SyncStage.ReplayingLog,
				"Replaying compliance log…"));

			Logger.Information("ReconcileAsync: compliance log has pending entries — replaying before diff");
			var replay = await _complianceLog.ReplayIntoDatabaseAsync(db, ct);
			Logger.Information(
				"Compliance replay applied {Applied} member(s); {Missing} skipped (not in DB)",
				replay.Applied, replay.MissingEntities);
		}

		var hasSnapshot = await _snapshotService.HasSnapshotAsync(ct);
		if (!hasSnapshot)
		{
			Logger.Warning("ReconcileAsync: no snapshot exists — performing plain sync + snapshot");
			await _syncService.SyncAsync(ct, progress);
			await _snapshotService.CaptureAsync(ct, progress);
			return new ReconcileResult(0, 0, 0, 0, 0, 0, 0, 0, 0);
		}

		using var client = await _clientFactory();

		int createdMembers = 0, modifiedMembers = 0;
		int registeredGroups = 0, unregisteredGroups = 0;
		int registeredPositions = 0, unregisteredPositions = 0;
		int recordedCompliance = 0;
		int apiErrors = 0;
		int apiWarnings = 0;

		// Maps old temporary (negative) member ID → real Unity ID
		var tempIdToRealId = new Dictionary<int, int>();

		// Temporary (negative) member IDs whose CreateMemberAsync call failed
		// during Phase 1. Phase 3 (group/officer registrations) consults this
		// set to skip any registration that depends on a member whose creation
		// failed, rather than blindly sending the negative temp ID to Unity
		// (which the API rejects with HTTP 400). The temp member is preserved
		// in the local DB so the next ReconcileAsync run will retry the
		// create automatically — and the dependent registration along with it.
		var failedCreates = new HashSet<int>();

		// ── Phase 1: Create new members on Unity ─────────────────────
		// These have negative temporary IDs assigned by TemporaryIdGenerator.
		var newMembers = await db.Members
			.Where(m => m.Id < 0)
			.Include(m => m.HomeGroup)
			.Include(m => m.IntergroupPosition)
			.AsNoTracking()
			.ToListAsync(ct);

		// Only report this phase when there's something to do — an empty
		// loop would flash through the UI for no reason and break the
		// determinate progress bar's "x of N" continuity.
		if (newMembers.Count > 0)
		{
			progress?.Report(new SyncProgress(
				SyncStage.PushCreates,
				newMembers.Count == 1
					? "Creating 1 new member…"
					: $"Creating {newMembers.Count} new members…",
				Current: 0,
				Total: newMembers.Count));
		}

		int createIndex = 0;
		foreach (var member in newMembers)
		{
			createIndex++;
			progress?.Report(new SyncProgress(
				SyncStage.PushCreates,
				$"Creating member: {member.AnonymousName}",
				Current: createIndex,
				Total: newMembers.Count));

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
					// Create-failure is an error, not a warning: it both fails
					// to push the new member AND blocks every dependent
					// registration in Phase 3. Bumping apiErrors keeps the
					// registration event log alive so the next ReconcileAsync
					// run can retry the create (member is still in the local
					// DB with a negative ID) plus its dependent registrations.
					apiErrors++;
					failedCreates.Add(member.Id);
					Logger.Error(
						"Failed to create member {Name} on Unity (temp ID {TempId}): {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}. Dependent registrations in Phase 3 will be skipped and retried on the next sync.",
						member.AnonymousName,
						member.Id,
						FormatApiFailure(response),
						response.StatusCode,
						response.Error?.Code,
						response.Error?.Message);
				}
			}
			catch (Exception ex)
			{
				// Same reasoning as above: an exception here also leaves the
				// member uncreated, so register it as a failed-create so
				// Phase 3 doesn't try to use the negative ID.
				apiErrors++;
				failedCreates.Add(member.Id);
				Logger.Error(ex, "Exception creating member {Name} on Unity (temp ID {TempId}). Dependent registrations in Phase 3 will be skipped and retried on the next sync.", member.AnonymousName, member.Id);
			}
		}

		// ── Phase 2: Push modified members to Unity ──────────────────
		progress?.Report(new SyncProgress(
			SyncStage.DetectingChanges,
			"Detecting local changes…"));

		var modifiedMemberChanges = await DetectModifiedMembersAsync(db, ct);

		// Updates and compliance pushes both walk this list, so count up
		// front how many fall into each bucket. Without these counts the
		// determinate progress bar would jerk between the two phases.
		// Skipping temp members keeps the count honest — they're handled
		// in Phase 1 and the loop body below also skips them.
		int updateTotal = modifiedMemberChanges.Count(c =>
			c.Member.Id >= 0 &&
			c.ChangedProperties.Any(p => p != GdprComplianceKey));

		if (updateTotal > 0)
		{
			progress?.Report(new SyncProgress(
				SyncStage.PushUpdates,
				updateTotal == 1
					? "Updating 1 member…"
					: $"Updating {updateTotal} members…",
				Current: 0,
				Total: updateTotal));
		}

		int updateIndex = 0;
		foreach (var (member, changedProps) in modifiedMemberChanges)
		{
			// Skip temp members — they were just created above
			if (member.Id < 0) continue;

			// Skip rows where the only thing that changed is GDPR
			// compliance — that's pushed separately in Phase 2.5 and
			// shouldn't be counted against the "updating member" total.
			if (!changedProps.Any(p => p != GdprComplianceKey)) continue;

			updateIndex++;
			progress?.Report(new SyncProgress(
				SyncStage.PushUpdates,
				$"Updating member: {member.AnonymousName}",
				Current: updateIndex,
				Total: updateTotal));

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
					Logger.Warning(
						"Failed to update member {Id} on Unity: {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}",
						member.Id,
						FormatApiFailure(response),
						response.StatusCode,
						response.Error?.Code,
						response.Error?.Message);
				}
			}
			catch (Exception ex)
			{
				apiErrors++;
				Logger.Error(ex, "Exception updating member {Id} on Unity", member.Id);
			}
		}

		// ── Phase 2.5: Push GDPR compliance changes to Unity ─────────
		//
		// Compliance changes ride on a dedicated endpoint
		// (POST /members/{id}/compliance) rather than the general
		// member-update path because the server applies special rules
		// there: it normalises accepted_at, defaults method to "api"
		// when missing, and clears version/method/statement on a
		// revocation. Going through UpdateMemberAsync would silently
		// drop the GDPR fields (UpdateMemberRequest carries none) and
		// even if it carried them, it wouldn't trigger the compliance
		// audit-log entries on the server side.
		//
		// We re-iterate the same modifiedMemberChanges list — anything
		// flagged with the GdprComplianceKey sentinel goes through this
		// loop in addition to (not instead of) the Phase 2 update.
		// Members may legitimately have both kinds of change in the
		// same session (e.g. an officer corrects a phone number AND
		// records an acceptance); each pushes to its own endpoint.

		// New members with compliance recorded this session: not in the
		// snapshot so DetectModifiedMembersAsync never returns them, and
		// the loop below guards on Id < 0. Collect them separately now
		// that tempIdToRealId is fully populated; only include those
		// whose create succeeded (i.e. have a resolved real ID).
		var newMembersWithCompliance = newMembers
			.Where(m => m.GdprAccepted is not null && tempIdToRealId.ContainsKey(m.Id))
			.ToList();

		// Pre-count compliance pushes for the same reason as Phase 2:
		// the determinate progress bar needs a stable total, and a
		// member with only-GDPR changes on a temp ID (Id < 0) is a
		// no-op down in the loop body, so it shouldn't be counted.
		int complianceTotal = modifiedMemberChanges.Count(c =>
			c.Member.Id >= 0 &&
			c.ChangedProperties.Contains(GdprComplianceKey) &&
			c.Member.GdprAccepted is not null)
			+ newMembersWithCompliance.Count;

		if (complianceTotal > 0)
		{
			progress?.Report(new SyncProgress(
				SyncStage.PushCompliance,
				complianceTotal == 1
					? "Recording 1 compliance update…"
					: $"Recording {complianceTotal} compliance updates…",
				Current: 0,
				Total: complianceTotal));
		}

		int complianceIndex = 0;
		foreach (var (member, changedProps) in modifiedMemberChanges)
		{
			if (member.Id < 0) continue; // temp members handled below
			if (!changedProps.Contains(GdprComplianceKey)) continue;

			// Skip "noise" changes where the snapshot had no value
			// recorded and the local value is also effectively unset.
			// This shouldn't happen via ComplianceService — it always
			// stamps GdprAccepted — but defends against a third party
			// nulling fields back out without going through the service.
			if (member.GdprAccepted is null)
			{
				Logger.Debug(
					"Skipping compliance push for member {Id}: GdprAccepted is null (no recorded state)",
					member.Id);
				continue;
			}

			complianceIndex++;
			progress?.Report(new SyncProgress(
				SyncStage.PushCompliance,
				$"Recording compliance: {member.AnonymousName}",
				Current: complianceIndex,
				Total: complianceTotal));

			try
			{
				// AcceptedAt is sent as ISO 8601 round-trip ("o") so
				// the wire payload is timezone-explicit. The server
				// accepts any DateTime-parseable string and normalises
				// to its own UTC Y-m-d H:i:s storage; we send "o" to
				// avoid relying on the server's parser to guess UTC
				// from a naïve local-style timestamp.
				var acceptedAt = member.GdprAcceptedAt?
					.ToUniversalTime()
					.ToString("o");

				var request = new RecordComplianceRequest
				{
					Accepted = member.GdprAccepted.Value,
					AcceptedAt = acceptedAt,
					// On revocations these will be null on the entity
					// (ComplianceService and replay both clear them);
					// JsonIgnore-when-null on RecordComplianceRequest
					// drops them from the wire payload, which is what
					// the server expects.
					Version = member.GdprAcceptanceVersion,
					Method = member.GdprAcceptanceMethod,
					// PolicyId replaces the statement body on the wire —
					// the server resolves the body itself via Scrutiny's
					// PrivacyPolicyRepository. Null when a pre-Scrutiny
					// log line replayed without an id, or when the device
					// accepted before ever syncing a policy; the server
					// treats the omission as "wording unknown" and stores
					// an empty statement, which is the same fallback
					// ComplianceService applied locally.
					PolicyId = member.GdprAcceptancePolicyId,
				};

				var response = await client.RecordComplianceAsync(member.Id, request, ct);
				if (response.Success)
				{
					recordedCompliance++;
					Logger.Information(
						"Recorded compliance for member {Name} (ID={Id}, accepted={Accepted}) on Unity",
						member.AnonymousName, member.Id, member.GdprAccepted);
				}
				else
				{
					apiWarnings++;
					Logger.Warning(
						"Failed to record compliance for member {Id} on Unity: {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}",
						member.Id,
						FormatApiFailure(response),
						response.StatusCode,
						response.Error?.Code,
						response.Error?.Message);
				}
			}
			catch (Exception ex)
			{
				apiErrors++;
				Logger.Error(ex, "Exception recording compliance for member {Id} on Unity", member.Id);
			}
		}

		foreach (var member in newMembersWithCompliance)
		{
			var realId = tempIdToRealId[member.Id];

			complianceIndex++;
			progress?.Report(new SyncProgress(
				SyncStage.PushCompliance,
				$"Recording compliance: {member.AnonymousName}",
				Current: complianceIndex,
				Total: complianceTotal));

			try
			{
				var request = new RecordComplianceRequest
				{
					Accepted = member.GdprAccepted!.Value,
					AcceptedAt = member.GdprAcceptedAt?.ToUniversalTime().ToString("o"),
					Version = member.GdprAcceptanceVersion,
					Method = member.GdprAcceptanceMethod,
					PolicyId = member.GdprAcceptancePolicyId,
				};

				var response = await client.RecordComplianceAsync(realId, request, ct);
				if (response.Success)
				{
					recordedCompliance++;
					Logger.Information(
						"Recorded compliance for new member {Name} (ID={Id}, accepted={Accepted}) on Unity",
						member.AnonymousName, realId, member.GdprAccepted);
				}
				else
				{
					apiWarnings++;
					Logger.Warning(
						"Failed to record compliance for new member {Id} on Unity: {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}",
						realId,
						FormatApiFailure(response),
						response.StatusCode,
						response.Error?.Code,
						response.Error?.Message);
				}
			}
			catch (Exception ex)
			{
				apiErrors++;
				Logger.Error(ex, "Exception recording compliance for new member {Id} on Unity", realId);
			}
		}

		// ── Phase 3: Push registration changes to Unity ──────────────
		// Now that all members have real IDs, we can register/unregister.

		var config = await _configService.LoadUnityConfigurationAsync();
		if (config.ActiveIntergroupMeetingId.HasValue)
		{
			var meetingId = config.ActiveIntergroupMeetingId.Value;

			// Detect both kinds of registration change up-front so we
			// can quote a single combined total to the UI. From the
			// user's point of view "registering attendance" is one
			// step regardless of whether each item is a group or an
			// officer, and a single bar that fills smoothly across the
			// whole phase reads more naturally than two short bars.
			var groupChanges = await DetectGroupRegistrationChangesAsync(db, ct);
			var positionChanges = await DetectPositionRegistrationChangesAsync(db, ct);
			int registrationTotal = groupChanges.Count + positionChanges.Count;
			int registrationIndex = 0;

			if (registrationTotal > 0)
			{
				progress?.Report(new SyncProgress(
					SyncStage.PushRegistrations,
					registrationTotal == 1
						? "Pushing 1 registration…"
						: $"Pushing {registrationTotal} registrations…",
					Current: 0,
					Total: registrationTotal));
			}

			// Group registrations
			foreach (var (group, registered) in groupChanges)
			{
				registrationIndex++;
				progress?.Report(new SyncProgress(
					SyncStage.PushRegistrations,
					registered
						? $"Registering group: {group.Name}"
						: $"Unregistering group: {group.Name}",
					Current: registrationIndex,
					Total: registrationTotal));

				try
				{
					if (registered)
					{
						// Find the GSR for this group — resolve temp IDs
						var gsr = await db.Members
							.Where(m => m.HomeGroupId == group.Id && m.IsGsr)
							.FirstOrDefaultAsync(ct);

						var gsrName = gsr?.AnonymousName ?? string.Empty;
						var gsrFound = gsr != null;
						var gsrTempId = gsr?.Id ?? 0;
						var memberIdResolvedFromTempId = false;
						var memberId = 0;
						if (gsr != null)
						{
							if (tempIdToRealId.TryGetValue(gsr.Id, out var realId))
							{
								memberId = realId;
								memberIdResolvedFromTempId = true;
							}
							else
							{
								memberId = gsr.Id;
							}
						}

						// If this GSR is a still-uncreated temp member whose
						// CreateMemberAsync failed in Phase 1, sending the
						// negative temp ID to Unity would just produce a
						// follow-on HTTP 400. Skip the registration and
						// preserve apiErrors from the create failure so the
						// event log survives — both the create AND this
						// registration will be retried on the next sync.
						// We do NOT increment apiErrors here: the underlying
						// error was already counted in Phase 1, and counting
						// it twice would inflate the failure metric.
						if (gsr != null && failedCreates.Contains(gsr.Id))
						{
							Logger.Information(
								"Skipping group registration for {Name} (ID={Id}): GSR {GsrName} (temp ID {GsrTempId}) failed to create on Unity in Phase 1. Will retry on next sync.",
								group.Name, group.Id, gsrName, gsrTempId);
							continue;
						}

						Logger.Debug(
							"Registering group on Unity — POST params: " +
							"MeetingId={MeetingId}, GroupId={GroupId}, GroupName={GroupName}, " +
							"MemberId={MemberId}, GsrName={GsrName}, GsrProxy={GsrProxy}, GsrProxyName={GsrProxyName}, " +
							"GsrFound={GsrFound}, GsrTempId={GsrTempId}, MemberIdResolvedFromTempId={MemberIdResolvedFromTempId}",
							meetingId, group.Id, group.Name,
							memberId, gsrName, group.GsrProxy, group.GsrProxyName ?? string.Empty,
							gsrFound, gsrTempId, memberIdResolvedFromTempId);

						var response = await client.RegisterGroupAsync(
							meetingId, group.Id, memberId, gsrName,
							gsrProxy: group.GsrProxy, gsrProxyName: group.GsrProxyName, ct);

						if (response.Success)
						{
							registeredGroups++;
							Logger.Information("Registered group {Name} (ID={Id}) on Unity", group.Name, group.Id);
						}
						else if (IsAlreadyRegisteredError(response))
						{
							registeredGroups++;
							Logger.Information("Group {Name} (ID={Id}) was already registered on Unity — treating as success",
								group.Name, group.Id);
						}
						else
						{
							apiErrors++;
							Logger.Error(
								"Failed to register group {Id} ({Name}): {ApiFailure}. " +
								"StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}. " +
								"POST params: MeetingId={MeetingId}, GroupId={GroupId}, MemberId={MemberId}, " +
								"GsrName={GsrName}, GsrProxy={GsrProxy}, GsrProxyName={GsrProxyName}, " +
								"GsrFound={GsrFound}, GsrTempId={GsrTempId}, MemberIdResolvedFromTempId={MemberIdResolvedFromTempId}",
								group.Id, group.Name,
								FormatApiFailure(response),
								response.StatusCode,
								response.Error?.Code,
								response.Error?.Message,
								meetingId, group.Id, memberId,
								gsrName, group.GsrProxy, group.GsrProxyName ?? string.Empty,
								gsrFound, gsrTempId, memberIdResolvedFromTempId);
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
							Logger.Error(
								"Failed to unregister group {Id}: {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}",
								group.Id,
								FormatApiFailure(response),
								response.StatusCode,
								response.Error?.Code,
								response.Error?.Message);
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
			foreach (var (position, registered) in positionChanges)
			{
				registrationIndex++;
				var positionLabel = position.ShortDescription ?? position.LongName ?? $"#{position.Id}";
				progress?.Report(new SyncProgress(
					SyncStage.PushRegistrations,
					registered
						? $"Registering officer: {positionLabel}"
						: $"Unregistering officer: {positionLabel}",
					Current: registrationIndex,
					Total: registrationTotal));

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

						// If the holder is a still-uncreated temp member whose
						// CreateMemberAsync failed in Phase 1, sending the
						// negative temp ID to Unity would just produce a
						// follow-on HTTP 400. Skip the registration; both
						// the create AND this registration will be retried
						// on the next sync. Same rationale as the group
						// case — apiErrors is NOT incremented here.
						if (failedCreates.Contains(holder.Id))
						{
							Logger.Information(
								"Skipping officer registration for position {Position}: holder {HolderName} (temp ID {HolderTempId}) failed to create on Unity in Phase 1. Will retry on next sync.",
								positionLabel, holder.AnonymousName, holder.Id);
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
						else if (IsAlreadyRegisteredError(response))
						{
							registeredPositions++;
							Logger.Information("Officer {Name} for position {Position} was already registered on Unity — treating as success",
								officerName, positionName);
						}
						else
						{
							apiErrors++;
							Logger.Error(
								"Failed to register officer for position {Id}: {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}",
								position.Id,
								FormatApiFailure(response),
								response.StatusCode,
								response.Error?.Code,
								response.Error?.Message);
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
							Logger.Error(
								"Failed to unregister officer {Id}: {ApiFailure}. StatusCode={StatusCode}, ErrorCode={ErrorCode}, Error={Error}",
								officerId,
								FormatApiFailure(response),
								response.StatusCode,
								response.Error?.Code,
								response.Error?.Message);
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

		// Phase 4 (re-sync from Unity) and Phase 5 (snapshot recapture)
		// have been removed: Finish Meeting is followed by Purge, which
		// wipes the local DB anyway, so pulling fresh state down from
		// Unity and snapshotting it would only be discarded seconds
		// later. The push counts collected above are the only numbers
		// the caller needs, and the event-log purge below depends only
		// on apiErrors, not on a successful re-sync.

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

			// Compliance log purges independently — a registration-log
			// purge failure shouldn't keep the compliance log alive
			// (and vice versa). Each is loud-failed via its own catch.
			try
			{
				await _complianceLog.PurgeAsync(ct);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Reconciliation succeeded but failed to purge compliance log");
			}
		}
		else
		{
			Logger.Warning(
				"Reconciliation had {Errors} API error(s) — keeping event logs for next reconcile attempt",
				apiErrors);
		}

		Logger.Information(
			"Reconciliation complete: {Created} created, {Modified} modified, " +
			"{RegGroups} groups registered, {UnregGroups} unregistered, " +
			"{RegPos} positions registered, {UnregPos} unregistered, " +
			"{Compliance} compliance recorded, " +
			"{Errors} API errors, {Warnings} API warnings",
			createdMembers, modifiedMembers,
			registeredGroups, unregisteredGroups,
			registeredPositions, unregisteredPositions,
			recordedCompliance,
			apiErrors, apiWarnings);

		progress?.Report(new SyncProgress(SyncStage.Complete, "Done"));

		return new ReconcileResult(
			createdMembers, modifiedMembers,
			registeredGroups, unregisteredGroups,
			registeredPositions, unregisteredPositions,
			recordedCompliance,
			apiErrors, apiWarnings);
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

			var changed = new HashSet<string>(StringComparer.Ordinal);

			if (original.AnonymousName != member.AnonymousName) changed.Add(nameof(Member.AnonymousName));
			if (original.PrivateName != member.PrivateName) changed.Add(nameof(Member.PrivateName));
			if (original.Email != member.Email) changed.Add(nameof(Member.Email));
			if (original.PersonalEmail != member.PersonalEmail) changed.Add(nameof(Member.PersonalEmail));
			if (original.MobileNumber != member.MobileNumber) changed.Add(nameof(Member.MobileNumber));
			if (original.IsGsr != member.IsGsr) changed.Add(nameof(Member.IsGsr));
			if (original.HomeGroupId != member.HomeGroupId) changed.Add(nameof(Member.HomeGroupId));
			if (original.IntergroupPositionId != member.IntergroupPositionId) changed.Add(nameof(Member.IntergroupPositionId));
			if (original.IntergroupPositionRotation != member.IntergroupPositionRotation) changed.Add(nameof(Member.IntergroupPositionRotation));

			// Treat the GDPR fields as a single unit: any field
			// difference flags the synthetic key "GdprCompliance",
			// because they're pushed via the dedicated compliance
			// endpoint as one atomic action rather than via the general
			// member-update endpoint. If we mapped each field to a
			// nameof key the push phase would have to OR them together
			// anyway, and getting that wrong would silently drop
			// changes — better to centralise the union here.
			//
			// GdprAcceptancePolicyId is included even though it isn't
			// returned in server responses (the server only echoes the
			// resolved statement back, not the id it resolved). It can
			// still differ between snapshot and current state when a
			// local acceptance has just been recorded against a freshly
			// synced policy id, and that delta is exactly what triggers
			// the push that informs the server.
			if (original.GdprAccepted != member.GdprAccepted
				|| original.GdprAcceptedAt != member.GdprAcceptedAt
				|| original.GdprAcceptanceVersion != member.GdprAcceptanceVersion
				|| original.GdprAcceptanceMethod != member.GdprAcceptanceMethod
				|| original.GdprAcceptanceStatement != member.GdprAcceptanceStatement
				|| original.GdprAcceptancePolicyId != member.GdprAcceptancePolicyId)
			{
				changed.Add(GdprComplianceKey);
			}

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
	/// Returns true when the API response indicates the group/position is
	/// already registered for this meeting. Treated as success on register-calls
	/// so the user can still proceed to Completed and purge the database.
	///
	/// <para>
	/// Two recognition paths, either of which is sufficient:
	/// </para>
	/// <list type="number">
	/// <item><b>HTTP 409 Conflict</b> — the REST-correct status for a duplicate
	///       resource. The WordPress plugin returns this for repeat register-*
	///       calls regardless of the JSON body shape, so we treat 409 from a
	///       register endpoint as "already registered" without inspecting the
	///       body. This handles bodies that say "conflict" / "duplicate entry"
	///       / arrive non-JSON / arrive empty.</item>
	/// <item><b>Keyword match</b> — kept as a backstop for any non-409 path
	///       that surfaces the same condition (e.g. a 200 with success=false,
	///       or a 400 with a "duplicate" code).</item>
	/// </list>
	/// </summary>
	private static bool IsAlreadyRegisteredError<T>(ApiResponse<T> response) where T : class
	{
		// Status-based: a register-* POST returning 409 is "already registered".
		if (response.StatusCode == 409) return true;

		var error = response.Error;
		if (error is null) return false;

		var code = error.Code?.ToLowerInvariant() ?? string.Empty;
		var msg = error.Message?.ToLowerInvariant() ?? string.Empty;

		return code.Contains("already", StringComparison.Ordinal) || code.Contains("duplicate", StringComparison.Ordinal) || code.Contains("exists", StringComparison.Ordinal)
			|| msg.Contains("already registered", StringComparison.Ordinal) || msg.Contains("already exists", StringComparison.Ordinal);
	}

	/// <summary>
	/// Formats every diagnostic field on an <see cref="ApiResponse{T}"/> into a
	/// single string for inclusion in a failure log line. Surfaces the HTTP
	/// status, error code, error message, and rate-limit headers — everything
	/// the client returned. Pair this with the Warning-level dump that
	/// <see cref="UnityRestSharp"/> writes for 400/401/403 (which includes
	/// request/response headers and bodies) to get full forensic detail.
	/// </summary>
	private static string FormatApiFailure<T>(ApiResponse<T> response) where T : class
	{
		var status = response.StatusCode;
		var code = response.Error?.Code ?? "(no code)";
		var message = response.Error?.Message ?? "(no message)";

		var rl = response.RateLimit;
		var rateLimit = rl is null
			? "(no rate-limit headers)"
			: $"limit={rl.Limit}, remaining={rl.Remaining}, reset={rl.Reset} ({rl.ResetDateTime:O})";

		return $"HTTP {status}, code={code}, message=\"{message}\", rate-limit: {rateLimit}";
	}
}