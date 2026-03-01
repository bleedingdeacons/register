using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Data.Data;
using TheBleedingDeacons.Unity.Data.Entities;
using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;

namespace TheBleedingDeacons.Unity.Data.Repositories;

public class GroupRepository : IGroupRepository
{
    private readonly UnityDbContext _db;

    public GroupRepository(UnityDbContext db) => _db = db;

    public async Task<List<Group>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Groups
            .OrderBy(g => g.Name)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<Group?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _db.Groups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<Group?> GetByIdWithMembersAsync(int id, CancellationToken ct = default) =>
        await _db.Groups
            .Include(g => g.Members)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<Group?> GetByIdWithMeetingsAsync(int id, CancellationToken ct = default) =>
        await _db.Groups
            .Include(g => g.Meetings)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<List<Group>> SearchAsync(string searchTerm, CancellationToken ct = default) =>
        await _db.Groups
            .Where(g => g.Name.Contains(searchTerm) ||
                        (g.Email ?? "").Contains(searchTerm) ||
                        (g.Notes ?? "").Contains(searchTerm))
            .OrderBy(g => g.Name)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<List<Group>> GetByDistrictAsync(int districtId, CancellationToken ct = default) =>
        await _db.Groups
            .Where(g => g.DistrictId == districtId)
            .OrderBy(g => g.Name)
            .AsNoTracking()
            .ToListAsync(ct);
}
