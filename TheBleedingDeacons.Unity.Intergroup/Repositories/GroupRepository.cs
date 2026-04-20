using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories;

public class GroupRepository : IGroupRepository
{
	private readonly IDbContextFactory<UnityDbContext> _factory;

	public GroupRepository(IDbContextFactory<UnityDbContext> factory) => _factory = factory;

	public async Task<List<Group>> GetAllAsync(CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Groups
			.OrderBy(g => g.Name)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<Group?> GetByIdAsync(int id, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Groups
			.AsNoTracking()
			.FirstOrDefaultAsync(g => g.Id == id, ct);
	}

	public async Task<Group?> GetByIdWithMembersAsync(int id, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Groups
			.Include(g => g.Members)
			.Include(g => g.Contacts)
			.AsNoTracking()
			.FirstOrDefaultAsync(g => g.Id == id, ct);
	}

	public async Task<Group?> GetByIdWithMeetingsAsync(int id, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Groups
			.Include(g => g.Meetings)
			.AsNoTracking()
			.FirstOrDefaultAsync(g => g.Id == id, ct);
	}

	public async Task<List<Group>> SearchAsync(string searchTerm, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Groups
			.Where(g => g.Name.Contains(searchTerm) ||
						(g.Email ?? "").Contains(searchTerm) ||
						(g.Notes ?? "").Contains(searchTerm))
			.OrderBy(g => g.Name)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Group>> GetByDistrictAsync(int districtId, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Groups
			.Where(g => g.DistrictId == districtId)
			.OrderBy(g => g.Name)
			.AsNoTracking()
			.ToListAsync(ct);
	}
}