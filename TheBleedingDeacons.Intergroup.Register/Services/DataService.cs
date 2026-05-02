using OfficeOpenXml;
using Serilog;
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

    public DataService(
        UnitySyncService syncService,
        SnapshotService snapshotService,
        ReconciliationService reconciliationService,
        IMeetingRepository meetingRepository,
        IPositionRepository positionRepository)
    {
        _syncService = syncService;
        _snapshotService = snapshotService;
        _reconciliationService = reconciliationService;
        _meetingRepository = meetingRepository;
        _positionRepository = positionRepository;
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