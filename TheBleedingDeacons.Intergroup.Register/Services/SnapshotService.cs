using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Services;

/// <summary>
/// Captures a point-in-time snapshot of every entity in the local Unity
/// database.  This snapshot represents the "clean" state received from the
/// Unity API before the Register app starts making local modifications.
///
/// The snapshot is later used by <see cref="ReconciliationService"/> to
/// detect which entities were created, modified, or deleted locally so
/// that those changes can be preserved across a full re-sync.
///
/// <b>Context lifetime</b>: each public method opens its own DbContext
/// via <see cref="IDbContextFactory{TContext}"/> and disposes it at the
/// end. This prevents sharing a change-tracker with AttendanceService /
/// ViewModels (which would otherwise let one's stale tracked entities
/// overwrite another's writes).
/// </summary>
public class SnapshotService
{
	private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		// Prevent navigation-property cycles from causing infinite recursion
		ReferenceHandler = ReferenceHandler.IgnoreCycles,
	};

	public SnapshotService(IDbContextFactory<UnityDbContext> dbContextFactory)
	{
		_dbContextFactory = dbContextFactory;
	}

	public record SnapshotResult(
		int Groups,
		int Members,
		int Positions,
		int Meetings,
		int Contacts,
		int IntergroupMeetings);

	/// <summary>
	/// Deletes any existing snapshot and captures a fresh one from the current
	/// database state.  Should be called immediately after
	/// <see cref="UnitySyncService.SyncAsync"/> completes.
	/// </summary>
	public async Task<SnapshotResult> CaptureAsync(CancellationToken ct = default)
	{
		await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

		// Wipe any previous snapshot
		await db.EntitySnapshots.ExecuteDeleteAsync(ct);
		db.ChangeTracker.Clear();

		var now = DateTime.UtcNow;
		var snapshots = new List<EntitySnapshot>();

		// ── Groups ───────────────────────────────────────────────────
		var groups = await db.Groups.AsNoTracking().ToListAsync(ct);
		foreach (var g in groups)
			snapshots.Add(CreateSnapshot("Group", g.Id, g, now));

		// ── Members ──────────────────────────────────────────────────
		var members = await db.Members.AsNoTracking().ToListAsync(ct);
		foreach (var m in members)
			snapshots.Add(CreateSnapshot("Member", m.Id, m, now));

		// ── Positions ────────────────────────────────────────────────
		var positions = await db.Positions.AsNoTracking().ToListAsync(ct);
		foreach (var p in positions)
			snapshots.Add(CreateSnapshot("Position", p.Id, p, now));

		// ── Meetings ─────────────────────────────────────────────────
		var meetings = await db.Meetings.AsNoTracking().ToListAsync(ct);
		foreach (var m in meetings)
			snapshots.Add(CreateSnapshot("Meeting", m.Id, m, now));

		// ── Contacts ─────────────────────────────────────────────────
		var contacts = await db.Contacts.AsNoTracking().ToListAsync(ct);
		foreach (var c in contacts)
			snapshots.Add(CreateSnapshot("Contact", c.Id, c, now));

		// ── IntergroupMeetings ───────────────────────────────────────
		var igMeetings = await db.IntergroupMeetings.AsNoTracking().ToListAsync(ct);
		foreach (var ig in igMeetings)
			snapshots.Add(CreateSnapshot("IntergroupMeeting", ig.Id, ig, now));

		// Suppress Updated stamp — snapshot writes are bookkeeping, not user edits.
		// This flag is instance state on the context, so setting it here affects
		// only this short-lived context — no risk of leaking into another caller.
		db.SuppressUpdatedStamp = true;
		await db.EntitySnapshots.AddRangeAsync(snapshots, ct);
		await db.SaveChangesAsync(ct);
		db.SuppressUpdatedStamp = false;

		return new SnapshotResult(
			groups.Count,
			members.Count,
			positions.Count,
			meetings.Count,
			contacts.Count,
			igMeetings.Count);
	}

	/// <summary>
	/// Returns <c>true</c> when a snapshot exists (i.e. <see cref="CaptureAsync"/>
	/// has been called and not yet cleared by a reconciliation).
	/// </summary>
	public async Task<bool> HasSnapshotAsync(CancellationToken ct = default)
	{
		await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
		return await db.EntitySnapshots.AnyAsync(ct);
	}

	/// <summary>
	/// Retrieves all snapshot records for a given entity type.
	/// </summary>
	public async Task<List<EntitySnapshot>> GetSnapshotsAsync(
		string entityType, CancellationToken ct = default)
	{
		await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
		return await db.EntitySnapshots
			.Where(s => s.EntityType == entityType)
			.AsNoTracking()
			.ToListAsync(ct);
	}

	/// <summary>
	/// Deserialises a snapshot's JSON payload back into the specified entity type.
	/// Returns <c>null</c> if deserialisation fails.
	/// </summary>
	public static T? Deserialise<T>(EntitySnapshot snapshot) where T : class
	{
		try
		{
			return JsonSerializer.Deserialize<T>(snapshot.JsonData, JsonOptions);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Serialises an entity to JSON using the same options as snapshot capture.
	/// </summary>
	public static string Serialise<T>(T entity)
	{
		return JsonSerializer.Serialize(entity, JsonOptions);
	}

	// ── Private Helpers ──────────────────────────────────────────────

	private static EntitySnapshot CreateSnapshot<T>(
		string entityType, int entityKey, T entity, DateTime now)
	{
		return new EntitySnapshot
		{
			EntityType = entityType,
			EntityKey = entityKey,
			JsonData = JsonSerializer.Serialize(entity, JsonOptions),
			SnapshotUtc = now,
		};
	}
}