using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Intergroup.Data;
using UnityModels = TheBleedingDeacons.Unity.Models;
using Entities = TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Unity.Intergroup.Services;

/// <summary>
/// Fetches Groups, Meetings, Positions, Members and Intergroup Meetings
/// from the Unity API and replaces the local SQLite data with a fresh snapshot.
/// </summary>
public class UnitySyncService
{
	private readonly UnityDbContext _db;
	private readonly Func<Task<UnityRestSharp>> _clientFactory;

	public UnitySyncService(UnityDbContext db, Func<Task<UnityRestSharp>> clientFactory)
	{
		_db = db;
		_clientFactory = clientFactory;
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

		// ── Replace local data inside a transaction ────────────────────
		// If the app crashes between delete and insert, the transaction
		// rolls back and the previous data is preserved.

		await using var transaction = await _db.Database.BeginTransactionAsync(ct);
		try
		{
			// Delete dependents first, then principals
			await _db.Meetings.ExecuteDeleteAsync(ct);
			await _db.Contacts.ExecuteDeleteAsync(ct);
			await _db.IntergroupMeetings.ExecuteDeleteAsync(ct);
			await _db.Positions.ExecuteDeleteAsync(ct);
			await _db.Members.ExecuteDeleteAsync(ct);
			await _db.Groups.ExecuteDeleteAsync(ct);

			// Also clear snapshot bookkeeping table so stale data
			// doesn't confuse a subsequent reconciliation cycle.
			await _db.EntitySnapshots.ExecuteDeleteAsync(ct);

			_db.ChangeTracker.Clear();

			// Sync data comes directly from Unity — don't stamp Updated timestamps.
			_db.SuppressUpdatedStamp = true;

			await _db.Groups.AddRangeAsync(groups, ct);
			await _db.Members.AddRangeAsync(members, ct);
			await _db.Meetings.AddRangeAsync(meetings, ct);
			await _db.Contacts.AddRangeAsync(contacts, ct);
			await _db.Positions.AddRangeAsync(positions, ct);
			await _db.IntergroupMeetings.AddRangeAsync(intergroupMeetings, ct);

			await _db.SaveChangesAsync(ct);
			await transaction.CommitAsync(ct);
		}
		catch
		{
			// Transaction rolls back automatically on dispose if not committed.
			// Re-throw so the caller knows the sync failed.
			throw;
		}
		finally
		{
			_db.SuppressUpdatedStamp = false;
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