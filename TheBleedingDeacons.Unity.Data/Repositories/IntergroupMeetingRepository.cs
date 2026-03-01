using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Data.Data;
using TheBleedingDeacons.Unity.Data.Entities;
using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;

namespace TheBleedingDeacons.Unity.Data.Repositories;

public class IntergroupMeetingRepository : IIntergroupMeetingRepository
{
    private readonly UnityDbContext _db;

    public IntergroupMeetingRepository(UnityDbContext db) => _db = db;

    public async Task<List<IntergroupMeeting>> GetAllAsync(CancellationToken ct = default) =>
        await _db.IntergroupMeetings
            .OrderByDescending(m => m.Date)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IntergroupMeeting?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _db.IntergroupMeetings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<List<IntergroupMeeting>> GetByDateRangeAsync(
        string dateFrom, string dateTo, CancellationToken ct = default) =>
        await _db.IntergroupMeetings
            .Where(m => m.Date != null &&
                        string.Compare(m.Date, dateFrom) >= 0 &&
                        string.Compare(m.Date, dateTo) <= 0)
            .OrderByDescending(m => m.Date)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<List<IntergroupMeeting>> SearchAsync(string searchTerm, CancellationToken ct = default) =>
        await _db.IntergroupMeetings
            .Where(m => (m.Title ?? "").Contains(searchTerm) ||
                        (m.GroupAttendeeNames ?? "").Contains(searchTerm) ||
                        (m.OfficerAttendeeNames ?? "").Contains(searchTerm))
            .OrderByDescending(m => m.Date)
            .AsNoTracking()
            .ToListAsync(ct);
}
