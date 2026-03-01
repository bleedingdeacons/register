using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Data.Data;
using TheBleedingDeacons.Unity.Data.Entities;
using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;

namespace TheBleedingDeacons.Unity.Data.Repositories;

public class PositionRepository : IPositionRepository
{
    private readonly UnityDbContext _db;

    public PositionRepository(UnityDbContext db) => _db = db;

    public async Task<List<Position>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Positions
            .Include(p => p.Holders)
            .OrderBy(p => p.ShortDescription)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<Position?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _db.Positions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Position?> GetByIdWithHoldersAsync(int id, CancellationToken ct = default) =>
        await _db.Positions
            .Include(p => p.Holders)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<List<Position>> GetFilledPositionsAsync(CancellationToken ct = default) =>
        await _db.Positions
            .Include(p => p.Holders)
            .Where(p => p.Holders.Any())
            .OrderBy(p => p.ShortDescription)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<List<Position>> GetVacantPositionsAsync(CancellationToken ct = default) =>
        await _db.Positions
            .Where(p => !p.Holders.Any())
            .OrderBy(p => p.ShortDescription)
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<List<Position>> SearchAsync(string searchTerm, CancellationToken ct = default) =>
        await _db.Positions
            .Include(p => p.Holders)
            .Where(p => p.ShortDescription.Contains(searchTerm) ||
                        (p.LongName ?? "").Contains(searchTerm) ||
                        (p.Email ?? "").Contains(searchTerm))
            .OrderBy(p => p.ShortDescription)
            .AsNoTracking()
            .ToListAsync(ct);
}
