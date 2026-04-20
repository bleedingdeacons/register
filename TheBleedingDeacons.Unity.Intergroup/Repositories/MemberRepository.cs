using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories;

public class MemberRepository : IMemberRepository
{
	private readonly IDbContextFactory<UnityDbContext> _factory;

	public MemberRepository(IDbContextFactory<UnityDbContext> factory) => _factory = factory;

	public async Task<List<Member>> GetAllAsync(CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Members
			.Include(m => m.HomeGroup)
			.Include(m => m.IntergroupPosition)
			.OrderBy(m => m.AnonymousName)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<Member?> GetByIdAsync(int id, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Members
			.Include(m => m.HomeGroup)
			.Include(m => m.IntergroupPosition)
			.AsNoTracking()
			.FirstOrDefaultAsync(m => m.Id == id, ct);
	}

	public async Task<List<Member>> GetGsrsAsync(CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Members
			.Include(m => m.HomeGroup)
			.Where(m => m.IsGsr)
			.OrderBy(m => m.AnonymousName)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Member>> GetByHomeGroupIdAsync(int groupId, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Members
			.Include(m => m.IntergroupPosition)
			.Where(m => m.HomeGroupId == groupId)
			.OrderBy(m => m.AnonymousName)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Member>> GetByPositionIdAsync(int positionId, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Members
			.Include(m => m.HomeGroup)
			.Where(m => m.IntergroupPositionId == positionId)
			.OrderBy(m => m.AnonymousName)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Member>> SearchAsync(string searchTerm, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Members
			.Include(m => m.HomeGroup)
			.Include(m => m.IntergroupPosition)
			.Where(m => m.AnonymousName.Contains(searchTerm) ||
						(m.PrivateName ?? "").Contains(searchTerm) ||
						(m.Email ?? "").Contains(searchTerm) ||
						(m.PersonalEmail ?? "").Contains(searchTerm))
			.OrderBy(m => m.AnonymousName)
			.AsNoTracking()
			.ToListAsync(ct);
	}
}