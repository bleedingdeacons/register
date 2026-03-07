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
    private readonly UnityRestSharp _client;

    public UnitySyncService(UnityDbContext db, UnityRestSharp client)
    {
        _db = db;
        _client = client;
    }

    public record SyncResult(int Groups, int Meetings, int Positions, int Members, int Contacts, int IntergroupMeetings);

    /// <summary>
    /// Pulls all data from Unity and replaces the local database.
    /// </summary>
    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        // ── Fetch from Unity API ──────────────────────────────────────

        var groupsResponse = await _client.GetGroupsAsync(perPage: 500, expandMeetings: true, cancellationToken: ct);
        if (!groupsResponse.Success || groupsResponse.Data is null)
            throw new InvalidOperationException($"Failed to fetch groups: {groupsResponse.Error?.Message}");

        var positionsResponse = await _client.GetPositionsAsync(perPage: 500, cancellationToken: ct);
        if (!positionsResponse.Success || positionsResponse.Data is null)
            throw new InvalidOperationException($"Failed to fetch positions: {positionsResponse.Error?.Message}");

        var membersResponse = await _client.GetMembersAsync(perPage: 500, cancellationToken: ct);
        if (!membersResponse.Success || membersResponse.Data is null)
            throw new InvalidOperationException($"Failed to fetch members: {membersResponse.Error?.Message}");

        var intergroupResponse = await _client.GetIntergroupMeetingsAsync(perPage: 500, cancellationToken: ct);
        if (!intergroupResponse.Success || intergroupResponse.Data is null)
            throw new InvalidOperationException($"Failed to fetch intergroup meetings: {intergroupResponse.Error?.Message}");

        // ── Map to EF entities ────────────────────────────────────────

        var groups = MapGroups(groupsResponse.Data);
        var meetings = MapMeetings(groupsResponse.Data);
        var contacts = MapContacts(groupsResponse.Data);
        var members = MapMembers(membersResponse.Data);
        var positions = MapPositions(positionsResponse.Data);
        var intergroupMeetings = MapIntergroupMeetings(intergroupResponse.Data);

        // ── Replace local data (delete dependents first, then principals) ──

        await _db.Meetings.ExecuteDeleteAsync(ct);
        await _db.Contacts.ExecuteDeleteAsync(ct);
        await _db.IntergroupMeetings.ExecuteDeleteAsync(ct);
        await _db.Positions.ExecuteDeleteAsync(ct);
        await _db.Members.ExecuteDeleteAsync(ct);
        await _db.Groups.ExecuteDeleteAsync(ct);

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

        _db.SuppressUpdatedStamp = false;

        return new SyncResult(groups.Count, meetings.Count, positions.Count, members.Count, contacts.Count, intergroupMeetings.Count);
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