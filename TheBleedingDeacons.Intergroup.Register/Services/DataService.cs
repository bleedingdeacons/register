using OfficeOpenXml;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;
using TheBleedingDeacons.Unity.Intergroup.Services;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Handles Unity sync, Excel export, and search operations.
/// All domain data access goes through Unity.Data repositories.
/// </summary>
public class DataService
{
    private static readonly ILogger Logger = AppLogger.ForContext<DataService>();

    private readonly UnitySyncService _syncService;
    private readonly SnapshotService _snapshotService;
    private readonly ReconciliationService _reconciliationService;
    private readonly IMeetingRepository _meetingRepository;
    private readonly IPositionRepository _positionRepository;
    private readonly IScrutinyClient _scrutinyClient;
    private readonly IPrivacyPolicyCache _privacyPolicyCache;

    public DataService(
        UnitySyncService syncService,
        SnapshotService snapshotService,
        ReconciliationService reconciliationService,
        IMeetingRepository meetingRepository,
        IPositionRepository positionRepository,
        IScrutinyClient scrutinyClient,
        IPrivacyPolicyCache privacyPolicyCache)
    {
        _syncService = syncService;
        _snapshotService = snapshotService;
        _reconciliationService = reconciliationService;
        _meetingRepository = meetingRepository;
        _positionRepository = positionRepository;
        _scrutinyClient = scrutinyClient;
        _privacyPolicyCache = privacyPolicyCache;
    }

    // ====================================================================
    // Import (Sync) Methods
    // ====================================================================

    /// <summary>
    /// Performs the initial Unity sync and captures a baseline snapshot.
    /// Call this at the start of a session (e.g. app launch or before an
    /// intergroup meeting) to establish the "clean" state.
    ///
    /// Flow: Sync all data from Unity → Snapshot the result.
    ///
    /// <para>
    /// Pass <paramref name="progress"/> to receive granular UI updates
    /// (per-page fetch status, snapshot capture). The final
    /// <see cref="SyncStage.Complete"/> report is fired here so the
    /// calling view-model can clear its busy state on a single signal.
    /// </para>
    /// </summary>
    public async Task<(int Meetings, int Positions, int Members, int Groups, int Contacts, int IntergroupMeetings)> ImportWithSnapshotAsync(
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null)
    {
        try
        {
            Logger.Information("Starting Unity sync with snapshot capture");

            // Privacy-policy gate — runs before the main data sync so
            // the meeting can't be started against a missing policy.
            // On 404 this throws NoActivePrivacyPolicyException, which
            // the calling view-model surfaces as a sync failure and
            // stops the meeting starting. On network failure we keep
            // any previous cache and let the data sync attempt to
            // proceed — if the data sync also fails the user will see
            // that error instead.
            await RefreshPrivacyPolicyAsync(cancellationToken);

            var sync = await _syncService.SyncAsync(cancellationToken, progress);
            var snap = await _snapshotService.CaptureAsync(cancellationToken, progress);

            progress?.Report(new SyncProgress(SyncStage.Complete, "Done"));

            Logger.Information(
                "Unity sync + snapshot complete: {Groups} groups, {Meetings} meetings, {Members} members, {Positions} positions, {Contacts} contacts, {IntergroupMeetings} IG meetings. " +
                "Snapshot: {SnapGroups} groups, {SnapMembers} members, {SnapPositions} positions",
                sync.Groups, sync.Meetings, sync.Members, sync.Positions, sync.Contacts, sync.IntergroupMeetings,
                snap.Groups, snap.Members, snap.Positions);

            return (sync.Meetings, sync.Positions, sync.Members, sync.Groups, sync.Contacts, sync.IntergroupMeetings);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unity sync with snapshot failed");
            throw;
        }
    }

    /// <summary>
    /// Detects local changes, pushes them to the Unity API in the correct
    /// dependency order, then re-syncs and re-snapshots.
    ///
    /// Flow: Detect → Push creates → Push updates → Push registrations → Re-sync → Re-snapshot.
    ///
    /// <para>
    /// Pass <paramref name="progress"/> to receive per-phase reconciliation
    /// updates. Forwarded as-is to
    /// <see cref="ReconciliationService.ReconcileAsync"/>.
    /// </para>
    /// </summary>
    public async Task<(int Meetings, int Positions, int Members, int Groups, int Contacts, int IntergroupMeetings,
                        int Created, int Modified, int Registered, int ApiErrors, int ApiWarnings)> ImportWithReconciliationAsync(
        CancellationToken cancellationToken = default,
        IProgress<SyncProgress>? progress = null)
    {
        try
        {
            Logger.Information("Starting Unity reconciliation");

            // Same privacy-policy gate as ImportWithSnapshotAsync.
            // Reconciliation includes a re-sync internally, so any
            // policy update on the server should be reflected on the
            // device by the time this method returns. Running the
            // refresh first means a "no active policy" state aborts
            // before we push any pending creates/updates — which is
            // the desired behaviour: if the upstream policy is
            // missing we want the operator to fix that before
            // committing anything else through reconciliation.
            await RefreshPrivacyPolicyAsync(cancellationToken);

            var result = await _reconciliationService.ReconcileAsync(cancellationToken, progress);
            var sync = result.Resync;

            Logger.Information(
                "Reconciliation complete: {Created} members created, {Modified} modified, " +
                "{RegGroups} groups registered, {RegPos} positions registered, {Errors} errors, {Warnings} warnings. " +
                "Re-synced {Groups} groups, {Meetings} meetings, {Members} members, {Positions} positions",
                result.CreatedMembers, result.ModifiedMembers,
                result.RegisteredGroups, result.RegisteredPositions, result.ApiErrors, result.ApiWarnings,
                sync.Groups, sync.Meetings, sync.Members, sync.Positions);

            return (sync.Meetings, sync.Positions, sync.Members, sync.Groups,
                    sync.Contacts, sync.IntergroupMeetings,
                    result.CreatedMembers, result.ModifiedMembers,
                    result.RegisteredGroups + result.RegisteredPositions,
                    result.ApiErrors, result.ApiWarnings);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unity reconciliation failed");
            throw;
        }
    }

    // ====================================================================
    // Privacy-policy cache refresh (sync-stage gate)
    // ====================================================================

    /// <summary>
    /// Refreshes the on-device privacy-policy cache from Scrutiny. This
    /// is the *only* point in the codebase that calls Scrutiny — every
    /// other consumer reads from <see cref="IPrivacyPolicyCache"/>, so
    /// registration flows that happen mid-meeting (potentially offline)
    /// don't depend on connectivity to record consent.
    ///
    /// <para>Three terminal states:</para>
    /// <list type="bullet">
    /// <item><b>Active policy returned</b> — write it to the cache,
    ///       overwriting any previous value. Sync proceeds.</item>
    /// <item><b>404 / no active policy</b> — clear the cache and
    ///       throw <see cref="NoActivePrivacyPolicyException"/>. The
    ///       calling view-model's existing sync-failed catch surfaces
    ///       the message to the operator and the meeting cannot
    ///       proceed. Clearing the cache (rather than leaving the
    ///       last-known-good in place) is deliberate: the operator
    ///       has retracted the policy upstream, and silently letting
    ///       acceptances be recorded against an old, no-longer-active
    ///       version would corrupt the audit trail.</item>
    /// <item><b>Network failure</b> — log a warning and return. We do
    ///       NOT throw and we do NOT touch the cache. The data sync
    ///       below will likely fail too and surface its own error,
    ///       but if it somehow succeeds (e.g. against a local cache),
    ///       the previous policy cache remains valid so registrations
    ///       can still be recorded against the last-known-good
    ///       version. This is the offline / partial-connectivity
    ///       case the user described.</item>
    /// </list>
    /// </summary>
    private async Task RefreshPrivacyPolicyAsync(CancellationToken cancellationToken)
    {
        Models.PrivacyPolicy? policy;
        try
        {
            policy = await _scrutinyClient.GetActivePrivacyPolicyAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // Transport failure — the device may have gone offline
            // between the connectivity check and now, or Scrutiny may
            // be temporarily unreachable. Either way, leaving the
            // existing cache in place is strictly better than wiping
            // it: a meeting that synced yesterday should still be
            // able to record acceptances today against yesterday's
            // policy. The data sync below will raise its own error
            // if needed.
            Logger.Warning(ex, "Could not refresh privacy policy cache from Scrutiny; keeping existing cache");
            return;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient timeout (genuine cancellation re-throws below).
            Logger.Warning("Timed out refreshing privacy policy cache from Scrutiny; keeping existing cache");
            return;
        }
        catch (InvalidOperationException ex)
        {
            // Thrown by ScrutinyClient when Unity base URL isn't
            // configured. Treat the same as a network failure for
            // this method's purposes — fix-up is a config issue, not
            // an upstream-policy issue, and the data sync below will
            // also fail with a clearer "configure Unity first" error.
            Logger.Warning(ex, "Cannot refresh privacy policy cache: {Reason}", ex.Message);
            return;
        }

        if (policy is null)
        {
            // Server returned 404 — no active policy is published.
            // This is the gate condition: the meeting must not
            // proceed. Clear the on-device cache so the next attempt
            // to record consent finds no version to record against,
            // and throw to abort the sync.
            _privacyPolicyCache.Clear();
            Logger.Warning("Scrutiny reports no active privacy policy; aborting sync");
            throw new NoActivePrivacyPolicyException();
        }

        _privacyPolicyCache.Save(policy);
        Logger.Information(
            "Privacy policy cache refreshed: id={Id} version={Version}",
            policy.Id, policy.Version);
    }

    // ====================================================================
    // Export Methods
    // ====================================================================

    public async Task<byte[]?> ExportToExcel()
    {
        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("AABristol");
            using var package = new ExcelPackage();

            var meetings = await _meetingRepository.GetAllAsync();

            // Sort by Day, start on Monday
            meetings = meetings.OrderBy(m =>
            {
                if (Enum.TryParse<DayOfWeek>(m.DayOfWeek, out var dayOfWeek))
                    return dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
                else
                    return int.MaxValue;
            }).ThenBy(m => m.Name).ToList();

            if (meetings.Count > 0)
            {
                var ws = package.Workbook.Worksheets.Add("Meetings");

                ws.Cells[1, 1].Value = "ID";
                ws.Cells[1, 2].Value = "Day";
                ws.Cells[1, 3].Value = "Time";
                ws.Cells[1, 4].Value = "End Time";
                ws.Cells[1, 5].Value = "Name";
                ws.Cells[1, 6].Value = "GSR Name";
                ws.Cells[1, 7].Value = "GSR Email";
                ws.Cells[1, 8].Value = "GSR Phone";
                ws.Cells[1, 9].Value = "Location";
                ws.Cells[1, 10].Value = "Address";
                ws.Cells[1, 11].Value = "Types";
                ws.Cells[1, 12].Value = "Online";

                for (int i = 0; i < meetings.Count; i++)
                {
                    int row = i + 2;
                    var m = meetings[i];
                    var gsrs = m.Group?.Members.Where(mb => mb.IsGsr).ToList() ?? [];

                    ws.Cells[row, 1].Value = m.Id;
                    ws.Cells[row, 2].Value = m.DayOfWeek;
                    ws.Cells[row, 3].Value = m.Time;
                    ws.Cells[row, 4].Value = m.EndTime;
                    ws.Cells[row, 5].Value = m.Name;
                    ws.Cells[row, 6].Value = string.Join("; ", gsrs.Select(g => g.AnonymousName));
                    ws.Cells[row, 7].Value = string.Join("; ", gsrs.Select(g => g.PersonalEmail ?? ""));
                    ws.Cells[row, 8].Value = string.Join("; ", gsrs.Select(g => g.MobileNumber ?? ""));
                    ws.Cells[row, 9].Value = m.LocationName;
                    ws.Cells[row, 10].Value = m.Address;
                    ws.Cells[row, 11].Value = m.Types;
                    ws.Cells[row, 12].Value = m.IsOnline;
                }

                ws.Cells.AutoFitColumns();
            }

            var positions = await _positionRepository.GetAllAsync();
            if (positions.Count > 0)
            {
                var ws = package.Workbook.Worksheets.Add("Positions");

                ws.Cells[1, 1].Value = "ID";
                ws.Cells[1, 2].Value = "Position Name";
                ws.Cells[1, 3].Value = "Long Name";
                ws.Cells[1, 4].Value = "Email";
                ws.Cells[1, 5].Value = "Holder";
                ws.Cells[1, 6].Value = "Holder Email";
                ws.Cells[1, 7].Value = "Holder Mobile";
                ws.Cells[1, 8].Value = "Term Years";

                for (int i = 0; i < positions.Count; i++)
                {
                    int row = i + 2;
                    var p = positions[i];
                    var holder = p.Holders.FirstOrDefault();

                    ws.Cells[row, 1].Value = p.Id;
                    ws.Cells[row, 2].Value = p.ShortDescription;
                    ws.Cells[row, 3].Value = p.LongName;
                    ws.Cells[row, 4].Value = p.Email;
                    ws.Cells[row, 5].Value = holder?.AnonymousName;
                    ws.Cells[row, 6].Value = holder?.PersonalEmail;
                    ws.Cells[row, 7].Value = holder?.MobileNumber;
                    ws.Cells[row, 8].Value = p.TermYears;
                }

                ws.Cells.AutoFitColumns();
            }

            return await package.GetAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Export Failed!");
            return null;
        }
    }

    // ====================================================================
    // Search Methods
    // ====================================================================

    public async Task<List<Meeting>> SearchMeetings(string searchTerm) =>
        await _meetingRepository.SearchAsync(searchTerm);

    public async Task<List<Position>> SearchPositions(string searchTerm) =>
        await _positionRepository.SearchAsync(searchTerm);
}