using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Utilities;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Handles Excel import/export and search operations.
/// For standard CRUD, use IMeetingRepository and IPositionRepository directly.
/// </summary>
public class DataService
{
    private static readonly ILogger Logger = AppLogger.ForContext<DataService>();

    private readonly RegisterContext _context;
    private readonly IMeetingRepository _meetingRepository;
    private readonly IPositionRepository _positionRepository;

    public DataService(RegisterContext context, IMeetingRepository meetingRepository, IPositionRepository positionRepository)
    {
        _context = context;
        _meetingRepository = meetingRepository;
        _positionRepository = positionRepository;
    }

    // ====================================================================
    // Import Methods
    // ====================================================================

    public async Task<(int Meetings, int Positions)> ImportFromUnityAsync(IUnityApiService unityApiService, CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Information("Starting import from Unity API");

            var data = await unityApiService.GetRegisterDataAsync(cancellationToken);

            // Delete in FK order: dependents first, then principals
            await _context.Members.ExecuteDeleteAsync(cancellationToken);
            await _context.Meetings.ExecuteDeleteAsync(cancellationToken);
            await _context.Groups.ExecuteDeleteAsync(cancellationToken);
            await _context.Positions.ExecuteDeleteAsync(cancellationToken);

            // Insert Groups only — Meetings and Members are attached as nav properties
            // so EF resolves all FKs and inserts them in the correct order automatically
            await _context.Groups.AddRangeAsync(data.Groups, cancellationToken);
            await _context.Positions.AddRangeAsync(data.Positions, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            await _meetingRepository.InvalidateAllMeetingsCacheAsync();
            await _positionRepository.InvalidateAllPositionsCacheAsync();

            Logger.Information(
                "Unity import complete: {Groups} groups, {Meetings} meetings, {Members} GSR members, {Positions} positions",
                data.Groups.Count, data.Meetings.Count, data.Members.Count, data.Positions.Count);

            return (data.Meetings.Count, data.Positions.Count);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Import from Unity API failed");
            throw;
        }
    }

    // ====================================================================
    // Import/Export Methods
    // ====================================================================

    public async Task<byte[]?> ExportToExcel()
    {
        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("AABristol");
            using var package = new ExcelPackage();

            // Sort By Day Start on Monday
            var meetings = await _meetingRepository.GetAllMeetingsAsync();

            meetings = meetings.OrderBy(m =>
            {
                if (Enum.TryParse<DayOfWeek>(m.Day, out var dayOfWeek))
                    return dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
                else
                    return int.MaxValue;
            }).ThenBy(m => m.Name).ToList();

            if (meetings.Count > 0)
            {
                var meetingsWorksheet = package.Workbook.Worksheets.Add("Meetings");

                // Add headers for Meetings
                meetingsWorksheet.Cells[1, 1].Value = "ID";
                meetingsWorksheet.Cells[1, 2].Value = "Day";
                meetingsWorksheet.Cells[1, 3].Value = "Time";
                meetingsWorksheet.Cells[1, 4].Value = "End Time";
                meetingsWorksheet.Cells[1, 5].Value = "Name";
                meetingsWorksheet.Cells[1, 6].Value = "Gsr Name";
                meetingsWorksheet.Cells[1, 7].Value = "Gsr Email Personal";
                meetingsWorksheet.Cells[1, 8].Value = "Gsr Phone";
                meetingsWorksheet.Cells[1, 9].Value = "Meeting Generic Email";
                meetingsWorksheet.Cells[1, 10].Value = "Using Generic";
                meetingsWorksheet.Cells[1, 11].Value = "Location";
                meetingsWorksheet.Cells[1, 12].Value = "Address";
                meetingsWorksheet.Cells[1, 13].Value = "Contact 1 Name";
                meetingsWorksheet.Cells[1, 14].Value = "Contact 1 Email";
                meetingsWorksheet.Cells[1, 15].Value = "Contact 1 Phone";
                meetingsWorksheet.Cells[1, 16].Value = "Contact 2 Name";
                meetingsWorksheet.Cells[1, 17].Value = "Contact 2 Email";
                meetingsWorksheet.Cells[1, 18].Value = "Contact 2 Phone";
                meetingsWorksheet.Cells[1, 19].Value = "Contact 3 Name";
                meetingsWorksheet.Cells[1, 20].Value = "Contact 3 Email";
                meetingsWorksheet.Cells[1, 21].Value = "Contact 3 Phone";
                meetingsWorksheet.Cells[1, 22].Value = "Updated";
                meetingsWorksheet.Cells[1, 23].Value = "Attended";
                meetingsWorksheet.Cells[1, 24].Value = "Proxy Attended";
                meetingsWorksheet.Cells[1, 25].Value = "Proxy Name";
                meetingsWorksheet.Cells[1, 26].Value = "Proxy Email";

                for (int i = 0; i < meetings.Count; i++)
                {
                    int row = i + 2;
                    var meeting = meetings[i];

                    meetingsWorksheet.Cells[row, 1].Value = meeting.ID;
                    meetingsWorksheet.Cells[row, 2].Value = meeting.Day;
                    meetingsWorksheet.Cells[row, 3].Value = meeting.Time;
                    meetingsWorksheet.Cells[row, 4].Value = meeting.EndTime;
                    meetingsWorksheet.Cells[row, 5].Value = meeting.Name;
                    meetingsWorksheet.Cells[row, 6].Value = meeting.Group?.Gsr?.Name;
                    meetingsWorksheet.Cells[row, 7].Value = meeting.Group?.Gsr?.EmailPersonal;
                    meetingsWorksheet.Cells[row, 8].Value = meeting.Group?.Gsr?.Phone;
                    meetingsWorksheet.Cells[row, 9].Value = meeting.MeetingGenericEmail;
                    meetingsWorksheet.Cells[row, 10].Value = meeting.UsingGeneric;
                    meetingsWorksheet.Cells[row, 11].Value = meeting.Location;
                    meetingsWorksheet.Cells[row, 12].Value = meeting.Address;
                    meetingsWorksheet.Cells[row, 13].Value = meeting.Contact1Name;
                    meetingsWorksheet.Cells[row, 14].Value = meeting.Contact1Email;
                    meetingsWorksheet.Cells[row, 15].Value = meeting.Contact1Phone;
                    meetingsWorksheet.Cells[row, 16].Value = meeting.Contact2Name;
                    meetingsWorksheet.Cells[row, 17].Value = meeting.Contact2Email;
                    meetingsWorksheet.Cells[row, 18].Value = meeting.Contact2Phone;
                    meetingsWorksheet.Cells[row, 19].Value = meeting.Contact3Name;
                    meetingsWorksheet.Cells[row, 20].Value = meeting.Contact3Email;
                    meetingsWorksheet.Cells[row, 21].Value = meeting.Contact3Phone;
                    meetingsWorksheet.Cells[row, 22].Value = meeting.Updated?.ToString("yyyy-MM-dd HH:mm:ss");
                    meetingsWorksheet.Cells[row, 23].Value = meeting.Attended;
                    meetingsWorksheet.Cells[row, 24].Value = meeting.ProxyAttendance;
                    meetingsWorksheet.Cells[row, 25].Value = meeting.ProxyName;
                    meetingsWorksheet.Cells[row, 26].Value = meeting.ProxyEmail;
                }

                meetingsWorksheet.Cells.AutoFitColumns();
            }

            // Export Positions
            var positions = await _positionRepository.GetAllPositionsAsync();
            if (positions.Count > 0)
            {
                var positionsWorksheet = package.Workbook.Worksheets.Add("Positions");

                positionsWorksheet.Cells[1, 1].Value = "ID";
                positionsWorksheet.Cells[1, 2].Value = "Position Name";
                positionsWorksheet.Cells[1, 3].Value = "Position Long Name";
                positionsWorksheet.Cells[1, 4].Value = "Position Generic Email";
                positionsWorksheet.Cells[1, 5].Value = "Member Anonymous Name";
                positionsWorksheet.Cells[1, 6].Value = "Member Personal Email";
                positionsWorksheet.Cells[1, 7].Value = "Member Mobile";
                positionsWorksheet.Cells[1, 8].Value = "Position Duration";
                positionsWorksheet.Cells[1, 9].Value = "Started Service";
                positionsWorksheet.Cells[1, 10].Value = "Attended";

                for (int i = 0; i < positions.Count; i++)
                {
                    int row = i + 2;
                    var position = positions[i];

                    positionsWorksheet.Cells[row, 1].Value = position.ID;
                    positionsWorksheet.Cells[row, 2].Value = position.PositionName;
                    positionsWorksheet.Cells[row, 3].Value = position.PositionLongName;
                    positionsWorksheet.Cells[row, 4].Value = position.PositionGenericEmail;
                    positionsWorksheet.Cells[row, 5].Value = position.MemberAnonymousName;
                    positionsWorksheet.Cells[row, 6].Value = position.MemberPersonalEmail;
                    positionsWorksheet.Cells[row, 7].Value = position.MemberMobile;
                    positionsWorksheet.Cells[row, 8].Value = position.PositionDuration;
                    positionsWorksheet.Cells[row, 9].Value = position.StartedService?.ToString("yyyy-MM-dd");
                    positionsWorksheet.Cells[row, 10].Value = position.Attended;
                }

                positionsWorksheet.Cells.AutoFitColumns();
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
    // Search Methods (kept here as they span the DbContext directly)
    // ====================================================================

    public async Task<List<Meeting>> SearchMeetings(string searchTerm)
    {
        return await _context.Meetings
            .Where(m => (m.Name ?? "").Contains(searchTerm) ||
                       (m.Day ?? "").Contains(searchTerm) ||
                       (m.Contact1Name ?? "").Contains(searchTerm) ||
                       (m.Contact2Name ?? "").Contains(searchTerm) ||
                       (m.Contact3Name ?? "").Contains(searchTerm))
            .OrderBy(m => m.Day)
            .ThenBy(m => m.Name)
            .ToListAsync();
    }

    public async Task<List<Position>> SearchPositions(string searchTerm)
    {
        return await _context.Positions
            .Where(p => (p.PositionName ?? "").Contains(searchTerm) ||
                       (p.PositionLongName ?? "").Contains(searchTerm) ||
                       (p.MemberAnonymousName ?? "").Contains(searchTerm))
            .OrderBy(p => p.PositionName)
            .ToListAsync();
    }
}
