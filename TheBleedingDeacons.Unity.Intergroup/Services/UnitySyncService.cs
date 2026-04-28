using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Intergroup.Data;
using UnityModels = TheBleedingDeacons.Unity.Models;
using Entities = TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Services;

/// <summary>
/// Fetches Groups, Meetings, Positions, Members and Intergroup Meetings
/// from the Unity API and replaces the local SQLite data with a fresh snapshot.
///
/// <b>Context lifetime</b>: <see cref="SyncAsync"/> owns a single DbContext
/// for the duration of the call. All the replace-local-data work happens
/// inside one transaction on one context. This avoids sharing a change
/// tracker with AttendanceService / ViewModels.
/// </summary>
public class UnitySyncService
{
	private readonly IDbContextFactory<UnityDbContext> _dbContextFactory;
	private readonly Func<Task<UnityRestSharp>> _clientFactory;
	private readonly ILogger<UnitySyncService> _logger;

	public UnitySyncService(
		IDbContextFactory<UnityDbContext> dbContextFactory,
		Func<Task<UnityRestSharp>> clientFactory,
		ILogger<UnitySyncService> logger)
	{
		_dbContextFactory = dbContextFactory;
		_clientFactory = clientFactory;
		_logger = logger;
	}

	public record SyncResult(int Groups, int Meetings, int Positions, int Members, int Contacts, int IntergroupMeetings);

	private const int PageSize = 500;

	/// <summary>
	/// Pulls all data from Unity and replaces the local database.
	/// </summary>
	public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
	{
		// Create a fresh client each sync so we always use the latest credentials.
		using var client = await _clientFactory();

		// ── Fetch from Unity API (paginated) ─────────────────────────

		var allGroups = await FetchAllPagesAsync(
			(page, token) => client.GetGroupsAsync(page: page, perPage: PageSize, expandMeetings: true, cancellationToken: token),
			"groups", ct);

		var allPositions = await FetchAllPagesAsync(
			(page, token) => client.GetPositionsAsync(page: page, perPage: PageSize, cancellationToken: token),
			"positions", ct);

		var allMembers = await FetchAllPagesAsync(
			(page, token) => client.GetMembersAsync(page: page, perPage: PageSize, cancellationToken: token),
			"members", ct);

		var allIntergroupMeetings = await FetchAllPagesAsync(
			(page, token) => client.GetIntergroupMeetingsAsync(page: page, perPage: PageSize, cancellationToken: token),
			"intergroup meetings", ct);

		// ── Map to EF entities ────────────────────────────────────────

		var groups = MapGroups(allGroups);
		var meetings = MapMeetings(allGroups);
		var contacts = MapContacts(allGroups);
		var members = MapMembers(allMembers);
		var positions = MapPositions(allPositions);
		var intergroupMeetings = MapIntergroupMeetings(allIntergroupMeetings);

		// ── Sanitise FK references ───────────────────────────────────
		// The Unity API may return members whose HomeGroupId or
		// IntergroupPositionId points to an entity outside the
		// fetched dataset (e.g. a group in another district).
		// Null-out any dangling references so SQLite FK checks pass.

		var groupIds = new HashSet<int>(groups.Select(g => g.Id));
		var positionIds = new HashSet<int>(positions.Select(p => p.Id));

		foreach (var m in members)
		{
			if (m.HomeGroupId.HasValue && !groupIds.Contains(m.HomeGroupId.Value))
				m.HomeGroupId = null;

			if (m.IntergroupPositionId.HasValue && !positionIds.Contains(m.IntergroupPositionId.Value))
				m.IntergroupPositionId = null;
		}

		foreach (var mtg in meetings)
		{
			if (mtg.GroupId.HasValue && !groupIds.Contains(mtg.GroupId.Value))
				mtg.GroupId = null;
		}

		// ── Replace local data inside a transaction ────────────────────
		// Open a fresh context for this sync — no shared change tracker
		// with other services or ViewModels. If the app crashes between
		// delete and insert, the transaction rolls back and the previous
		// data is preserved.

		await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		try
		{
			// Delete dependents first, then principals
			await db.Meetings.ExecuteDeleteAsync(ct);
			await db.Contacts.ExecuteDeleteAsync(ct);
			await db.IntergroupMeetings.ExecuteDeleteAsync(ct);
			await db.Positions.ExecuteDeleteAsync(ct);
			await db.Members.ExecuteDeleteAsync(ct);
			await db.Groups.ExecuteDeleteAsync(ct);

			// Also clear snapshot bookkeeping table so stale data
			// doesn't confuse a subsequent reconciliation cycle.
			await db.EntitySnapshots.ExecuteDeleteAsync(ct);

			db.ChangeTracker.Clear();

			// Sync data comes directly from Unity — don't stamp Updated timestamps.
			db.SuppressUpdatedStamp = true;

			// Insert principals before dependents to satisfy FK constraints:
			//   Groups   ← referenced by Members (HomeGroupId), Meetings (GroupId), Contacts (GroupId)
			//   Positions ← referenced by Members (IntergroupPositionId)
			await db.Groups.AddRangeAsync(groups, ct);
			await db.Positions.AddRangeAsync(positions, ct);
			await db.Members.AddRangeAsync(members, ct);
			await db.Meetings.AddRangeAsync(meetings, ct);
			await db.Contacts.AddRangeAsync(contacts, ct);
			await db.IntergroupMeetings.AddRangeAsync(intergroupMeetings, ct);

			await db.SaveChangesAsync(ct);
			await transaction.CommitAsync(ct);
		}
		catch (DbUpdateException ex)
		{
			// Log every member whose HomeGroupId or IntergroupPositionId
			// doesn't match a group/position we're about to insert —
			// these are the most likely cause of the FK violation.
			var danglingHome = members
				.Where(m => m.HomeGroupId.HasValue && !groupIds.Contains(m.HomeGroupId.Value))
				.ToList();

			var danglingPosition = members
				.Where(m => m.IntergroupPositionId.HasValue && !positionIds.Contains(m.IntergroupPositionId.Value))
				.ToList();

			foreach (var m in danglingHome)
			{
				_logger.LogError(
					"Member {MemberId} ({MemberName}) has invalid HomeGroupId {HomeGroupId} — no matching group in sync data",
					m.Id, m.AnonymousName, m.HomeGroupId);
			}

			foreach (var m in danglingPosition)
			{
				_logger.LogError(
					"Member {MemberId} ({MemberName}) has invalid IntergroupPositionId {PositionId} — no matching position in sync data",
					m.Id, m.AnonymousName, m.IntergroupPositionId);
			}

			if (danglingHome.Count == 0 && danglingPosition.Count == 0)
			{
				_logger.LogError(ex,
					"FK constraint failed during sync but no dangling member references were detected — check meeting/contact GroupIds");
			}

			throw;
		}
		finally
		{
			db.SuppressUpdatedStamp = false;
		}

		return new SyncResult(groups.Count, meetings.Count, positions.Count, members.Count, contacts.Count, intergroupMeetings.Count);
	}

	// ── Pagination ──────────────────────────────────────────────────

	/// <summary>
	/// Fetches every page of a paginated Unity endpoint and returns the
	/// combined results as a single list.
	/// </summary>
	private static async Task<List<T>> FetchAllPagesAsync<T>(
		Func<int, CancellationToken, Task<ApiResponse<List<T>>>> fetchPage,
		string entityName,
		CancellationToken ct) where T : class
	{
		var all = new List<T>();
		int page = 1;

		while (true)
		{
			var response = await fetchPage(page, ct);

			if (!response.Success || response.Data is null)
				throw new InvalidOperationException(
					$"Failed to fetch {entityName} (page {page}): {response.Error?.Message}");

			all.AddRange(response.Data);

			if (response.Meta is null || page >= response.Meta.TotalPages)
				break;

			page++;
		}

		return all;
	}

	// ── Mapping ──────────────────────────────────────────────────────

	private static List<Entities.Group> MapGroups(List<UnityModels.Group> source) =>
		source.Select(g => new Entities.Group
		{
			Id = g.Id,
			Name = g.Title,
			Email = NullIfEmpty(g.Email),
			Phone = NullIfEmpty(g.Phone),
			Website = NullIfEmpty(g.Website),
			Notes = NullIfEmpty(g.Notes),
			DistrictId = g.DistrictId,
		}).ToList();

	private static List<Entities.Meeting> MapMeetings(List<UnityModels.Group> groups) =>
		groups
			.Where(g => g.HasExpandedMeetings)
			.SelectMany(g => g.Meetings.Select(m => new Entities.Meeting
			{
				Id = m.Id,
				Name = !string.IsNullOrEmpty(m.Name) ? m.Name : g.Title,
				Day = m.Day,
				DayOfWeek = NullIfEmpty(m.DayOfWeek),
				Time = NullIfEmpty(m.Time),
				EndTime = NullIfEmpty(m.EndTime),
				LocationName = m.Location?.Name,
				Address = m.Location?.FormattedAddress,
				IsOnline = m.IsOnline,
				OnlineLink = NullIfEmpty(m.OnlineLink),
				Types = m.Types.Count > 0 ? string.Join(", ", m.Types) : null,
				GroupId = g.Id,
			}))
			.ToList();

	private static List<Entities.Contact> MapContacts(List<UnityModels.Group> groups) =>
		groups
			.Where(g => g.Contacts.Count > 0)
			.SelectMany(g => g.Contacts.Select(c => new Entities.Contact
			{
				Name = !string.IsNullOrWhiteSpace(c.Name) ? c.Name : string.Empty,
				Email = NullIfEmpty(c.Email),
				Phone = NullIfEmpty(c.Phone),
				GroupId = g.Id,
			}))
			.ToList();

	private static List<Entities.Member> MapMembers(List<UnityModels.Member> source) =>
		source.Select(m => new Entities.Member
		{
			Id = m.Id,
			AnonymousName = m.AnonymousName,
			PrivateName = NullIfEmpty(m.PrivateName),
			Email = NullIfEmpty(m.Email),
			PersonalEmail = NullIfEmpty(m.PersonalEmail),
			MobileNumber = NullIfEmpty(m.MobileNumber),
			IsGsr = m.IsGsr,
			HomeGroupId = m.HomeGroupId,
			IntergroupPositionId = m.IntergroupPositionId,
			IntergroupPositionRotation = string.IsNullOrWhiteSpace(m.IntergroupPositionRotation) ? null : m.IntergroupPositionRotation,
			// Flatten the GdprCompliance sub-object into the entity's
			// individual columns. Older Unity servers omit the sub-object
			// entirely (api Member.GdprCompliance is null) — leave the
			// fields null in that case rather than synthesising a
			// "never accepted" record that would be indistinguishable
			// from an actively-recorded revocation.
			GdprAccepted = m.GdprCompliance?.Accepted,
			GdprAcceptedAt = m.GdprCompliance?.AcceptedAt,
			GdprAcceptanceVersion = NullIfEmpty(m.GdprCompliance?.Version),
			GdprAcceptanceMethod = NullIfEmpty(m.GdprCompliance?.Method),
			GdprAcceptanceStatement = NullIfEmpty(m.GdprCompliance?.Statement),
		}).ToList();

	private static List<Entities.Position> MapPositions(
		List<UnityModels.Position> source) =>
		source.Select(p => new Entities.Position
		{
			Id = p.Id,
			ShortDescription = p.ShortDescription,
			LongName = NullIfEmpty(p.LongName),
			Email = NullIfEmpty(p.Email),
			MinimumSobriety = p.MinimumSobriety,
			TermYears = p.TermYears,
		}).ToList();

	private static List<Entities.IntergroupMeeting> MapIntergroupMeetings(
		List<UnityModels.IntergroupMeeting> source) =>
		source.Select(m => new Entities.IntergroupMeeting
		{
			Id = m.Id,
			Title = NullIfEmpty(m.Title),
			Date = NullIfEmpty(m.Date),
			GroupAttendeeIds = m.GroupAttendeeIds.Count > 0
				? string.Join(",", m.GroupAttendeeIds)
				: null,
			GroupAttendeeNames = m.GroupAttendees.Count > 0
				? string.Join(", ", m.GroupAttendees.Select(a => a.Name))
				: null,
			OfficerAttendeeIds = m.OfficersAttendingIds.Count > 0
				? string.Join(",", m.OfficersAttendingIds)
				: null,
			OfficerAttendeeNames = m.OfficersAttending.Count > 0
				? string.Join(", ", m.OfficersAttending.Select(a => a.Name))
				: null,
		}).ToList();

	private static string? NullIfEmpty(string? value) =>
		string.IsNullOrWhiteSpace(value) ? null : value;
}