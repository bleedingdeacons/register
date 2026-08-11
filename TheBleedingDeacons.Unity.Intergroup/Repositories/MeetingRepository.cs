using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Unity.Intergroup.Repositories;

public class MeetingRepository : IMeetingRepository
{
	private readonly IDbContextFactory<UnityDbContext> _factory;

	public MeetingRepository(IDbContextFactory<UnityDbContext> factory) => _factory = factory;

	public async Task<List<Meeting>> GetAllAsync(CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Meetings
			.Include(m => m.Group)
			.OrderBy(m => m.Day)
			.ThenBy(m => m.Time)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<Meeting?> GetByIdAsync(int id, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Meetings
			.Include(m => m.Group)
			.AsNoTracking()
			.FirstOrDefaultAsync(m => m.Id == id, ct);
	}

	public async Task<List<Meeting>> GetByGroupIdAsync(int groupId, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Meetings
			.Where(m => m.GroupId == groupId)
			.OrderBy(m => m.Day)
			.ThenBy(m => m.Time)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Meeting>> GetByDayAsync(int day, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Meetings
			.Include(m => m.Group)
			.Where(m => m.Day == day)
			.OrderBy(m => m.Time)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Meeting>> GetOnlineMeetingsAsync(CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Meetings
			.Include(m => m.Group)
			.Where(m => m.IsOnline)
			.OrderBy(m => m.Day)
			.ThenBy(m => m.Time)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	public async Task<List<Meeting>> SearchAsync(string searchTerm, CancellationToken ct = default)
	{
		await using var db = await _factory.CreateDbContextAsync(ct);
		return await db.Meetings
			.Include(m => m.Group)
			.Where(m => m.Name.Contains(searchTerm) ||
						(m.LocationName ?? "").Contains(searchTerm) ||
						(m.Address ?? "").Contains(searchTerm) ||
						(m.DayOfWeek ?? "").Contains(searchTerm))
			.OrderBy(m => m.Day)
			.ThenBy(m => m.Time)
			.AsNoTracking()
			.ToListAsync(ct);
	}
}
