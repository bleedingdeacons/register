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
/// For standard CRUD, use IGroupRepository and IPositionRepository directly.
/// </summary>
public class DataService
{
    private static readonly ILogger Logger = AppLogger.ForContext<DataService>();

    private readonly RegisterContext _context;
    private readonly IGroupRepository _groupRepository;
    private readonly IPositionRepository _positionRepository;

    public DataService(RegisterContext context, IGroupRepository groupRepository, IPositionRepository positionRepository)
    {
        _context = context;
        _groupRepository = groupRepository;
        _positionRepository = positionRepository;
    }

    // ====================================================================
    // Import/Export Methods
    // ====================================================================

    public async Task ImportFromExcel(Stream excelStream)
    {
        try
        {
            RegisterData data = ExcelSerializer.DeserializeFromExcel(excelStream);

            await _context.Groups.ExecuteDeleteAsync();
            await _context.Positions.ExecuteDeleteAsync();

            await _context.Groups.AddRangeAsync(data.Groups);
            await _context.Positions.AddRangeAsync(data.Positions);

            await _context.SaveChangesAsync();

            // Invalidate all caches after bulk import
            await _groupRepository.InvalidateAllGroupsCacheAsync();
            await _positionRepository.InvalidateAllPositionsCacheAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Import from Excel Failed!");
            throw;
        }
    }

    public async Task<byte[]?> ExportToExcel()
    {
        try
        {
            ExcelPackage.License.SetNonCommercialOrganization("AABristol");
            using var package = new ExcelPackage();

            // Sort By Day Start on Monday
            var groups = await _groupRepository.GetAllGroupsAsync();

            groups = groups.OrderBy(g =>
            {
                if (Enum.TryParse<DayOfWeek>(g.Day, out var dayOfWeek))
                    return dayOfWeek == DayOfWeek.Sunday ? 6 : (int)dayOfWeek - 1;
                else
                    return int.MaxValue;
            }).ThenBy(g => g.Name).ToList();

            if (groups.Count > 0)
            {
                var groupsWorksheet = package.Workbook.Worksheets.Add("Groups");

                // Add headers for Groups
                groupsWorksheet.Cells[1, 1].Value = "ID";
                groupsWorksheet.Cells[1, 2].Value = "Day";
                groupsWorksheet.Cells[1, 3].Value = "Time";
                groupsWorksheet.Cells[1, 4].Value = "End Time";
                groupsWorksheet.Cells[1, 5].Value = "Name";
                groupsWorksheet.Cells[1, 6].Value = "Gsr Name";
                groupsWorksheet.Cells[1, 7].Value = "Gsr Email Personal";
                groupsWorksheet.Cells[1, 8].Value = "Gsr Phone";
                groupsWorksheet.Cells[1, 9].Value = "Group Generic Email";
                groupsWorksheet.Cells[1, 10].Value = "Using Generic";
                groupsWorksheet.Cells[1, 11].Value = "Location";
                groupsWorksheet.Cells[1, 12].Value = "Address";
                groupsWorksheet.Cells[1, 13].Value = "Contact 1 Name";
                groupsWorksheet.Cells[1, 14].Value = "Contact 1 Email";
                groupsWorksheet.Cells[1, 15].Value = "Contact 1 Phone";
                groupsWorksheet.Cells[1, 16].Value = "Contact 2 Name";
                groupsWorksheet.Cells[1, 17].Value = "Contact 2 Email";
                groupsWorksheet.Cells[1, 18].Value = "Contact 2 Phone";
                groupsWorksheet.Cells[1, 19].Value = "Contact 3 Name";
                groupsWorksheet.Cells[1, 20].Value = "Contact 3 Email";
                groupsWorksheet.Cells[1, 21].Value = "Contact 3 Phone";
                groupsWorksheet.Cells[1, 22].Value = "Updated";
                groupsWorksheet.Cells[1, 23].Value = "Attended";
                groupsWorksheet.Cells[1, 24].Value = "Proxy Attended";
                groupsWorksheet.Cells[1, 25].Value = "Proxy Name";
                groupsWorksheet.Cells[1, 26].Value = "Proxy Email";

                for (int i = 0; i < groups.Count; i++)
                {
                    int row = i + 2;
                    var group = groups[i];

                    groupsWorksheet.Cells[row, 1].Value = group.ID;
                    groupsWorksheet.Cells[row, 2].Value = group.Day;
                    groupsWorksheet.Cells[row, 3].Value = group.Time;
                    groupsWorksheet.Cells[row, 4].Value = group.EndTime;
                    groupsWorksheet.Cells[row, 5].Value = group.Name;
                    groupsWorksheet.Cells[row, 6].Value = group.GsrName;
                    groupsWorksheet.Cells[row, 7].Value = group.GsrEmailPersonal;
                    groupsWorksheet.Cells[row, 8].Value = group.GsrPhone;
                    groupsWorksheet.Cells[row, 9].Value = group.GroupGenericEmail;
                    groupsWorksheet.Cells[row, 10].Value = group.UsingGeneric;
                    groupsWorksheet.Cells[row, 11].Value = group.Location;
                    groupsWorksheet.Cells[row, 12].Value = group.Address;
                    groupsWorksheet.Cells[row, 13].Value = group.Contact1Name;
                    groupsWorksheet.Cells[row, 14].Value = group.Contact1Email;
                    groupsWorksheet.Cells[row, 15].Value = group.Contact1Phone;
                    groupsWorksheet.Cells[row, 16].Value = group.Contact2Name;
                    groupsWorksheet.Cells[row, 17].Value = group.Contact2Email;
                    groupsWorksheet.Cells[row, 18].Value = group.Contact2Phone;
                    groupsWorksheet.Cells[row, 19].Value = group.Contact3Name;
                    groupsWorksheet.Cells[row, 20].Value = group.Contact3Email;
                    groupsWorksheet.Cells[row, 21].Value = group.Contact3Phone;
                    groupsWorksheet.Cells[row, 22].Value = group.Updated?.ToString("yyyy-MM-dd HH:mm:ss");
                    groupsWorksheet.Cells[row, 23].Value = group.Attended;
                    groupsWorksheet.Cells[row, 24].Value = group.ProxyAttendance;
                    groupsWorksheet.Cells[row, 25].Value = group.ProxyName;
                    groupsWorksheet.Cells[row, 26].Value = group.ProxyEmail;
                }

                groupsWorksheet.Cells.AutoFitColumns();
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

    public async Task<List<Group>> SearchGroups(string searchTerm)
    {
        return await _context.Groups
            .Where(g => (g.Name ?? "").Contains(searchTerm) ||
                       (g.Day ?? "").Contains(searchTerm) ||
                       (g.Contact1Name ?? "").Contains(searchTerm) ||
                       (g.Contact2Name ?? "").Contains(searchTerm) ||
                       (g.Contact3Name ?? "").Contains(searchTerm))
            .OrderBy(g => g.Day)
            .ThenBy(g => g.Name)
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