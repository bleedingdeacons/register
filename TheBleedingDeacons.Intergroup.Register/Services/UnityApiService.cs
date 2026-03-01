using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Utilities;
using TheBleedingDeacons.Unity.Client;
using UnityModels = TheBleedingDeacons.Unity.Models;
using LocalModels = TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services;

/// <summary>
/// Fetches data from the Unity WordPress API via <see cref="UnityRestSharp"/>
/// and maps it into a <see cref="RegisterData"/> for the register app.
/// </summary>
public class UnityApiService : IUnityApiService
{
    private static readonly ILogger Logger = AppLogger.ForContext<UnityApiService>();

    private readonly IConfigurationService _configService;

    public UnityApiService(IConfigurationService configService)
    {
        _configService = configService;
    }

    /// <inheritdoc />
    public async Task<RegisterData> GetRegisterDataAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configService.LoadUnityConfigurationAsync();
        if (!config.IsValid())
        {
            throw new InvalidOperationException("Unity API is not configured. Please set the Base URL and API Key in Settings.");
        }

        using var client = new UnityRestSharp(config.BaseUrl, config.ApiKey);

        Logger.Information("Fetching register data from Unity API at {BaseUrl}", config.BaseUrl);

        // Fetch groups with expanded meetings so we get day/time/location per meeting
        var groupsResponse = await client.GetGroupsAsync(
            perPage: 500,
            expandMeetings: true,
            cancellationToken: cancellationToken);

        if (!groupsResponse.Success || groupsResponse.Data is null)
        {
            Logger.Error("Failed to fetch groups from Unity API: {Error}",
                groupsResponse.Error?.Message ?? "Unknown error");
            throw new InvalidOperationException(
                $"Failed to fetch groups: {groupsResponse.Error?.Message ?? "Unknown error"}");
        }

        // Fetch positions
        var positionsResponse = await client.GetPositionsAsync(
            perPage: 500,
            cancellationToken: cancellationToken);

        if (!positionsResponse.Success || positionsResponse.Data is null)
        {
            Logger.Error("Failed to fetch positions from Unity API: {Error}",
                positionsResponse.Error?.Message ?? "Unknown error");
            throw new InvalidOperationException(
                $"Failed to fetch positions: {positionsResponse.Error?.Message ?? "Unknown error"}");
        }

        // Fetch members so we can resolve GSR info and position holders
        var membersResponse = await client.GetMembersAsync(
            perPage: 500,
            cancellationToken: cancellationToken);

        if (!membersResponse.Success || membersResponse.Data is null)
        {
            Logger.Error("Failed to fetch members from Unity API: {Error}",
                membersResponse.Error?.Message ?? "Unknown error");
            throw new InvalidOperationException(
                $"Failed to fetch members: {membersResponse.Error?.Message ?? "Unknown error"}");
        }

        // Fetch intergroup meetings
        var intergroupMeetingsResponse = await client.GetIntergroupMeetingsAsync(
            perPage: 500,
            cancellationToken: cancellationToken);

        if (!intergroupMeetingsResponse.Success || intergroupMeetingsResponse.Data is null)
        {
            Logger.Error("Failed to fetch intergroup meetings from Unity API: {Error}",
                intergroupMeetingsResponse.Error?.Message ?? "Unknown error");
            throw new InvalidOperationException(
                $"Failed to fetch intergroup meetings: {intergroupMeetingsResponse.Error?.Message ?? "Unknown error"}");
        }

        // Build a complete object graph: Group owns its Meetings + Gsr (Member).
        // EF inserts Groups first and resolves all FKs automatically via nav properties.
        var groups = MapGroups(groupsResponse.Data, membersResponse.Data);

        // Flatten for counts and convenience — the graph is the source of truth for saving
        var meetings = groups.SelectMany(g => g.Meetings).ToList();
        var members = groups.SelectMany(g => g.Gsrs).ToList();

        var positions = MapPositions(positionsResponse.Data, membersResponse.Data);

        var intergroupMeetings = MapIntergroupMeetings(intergroupMeetingsResponse.Data);

        Logger.Information(
            "Fetched {GroupCount} groups, {MeetingCount} meetings, {PositionCount} positions, {MemberCount} GSR members, {IntergroupCount} intergroup meetings from Unity API",
            groups.Count, meetings.Count, positions.Count, members.Count, intergroupMeetings.Count);

        return new RegisterData(groups, meetings, positions, members, intergroupMeetings);
    }

    // ====================================================================
    // Mapping: Unity Groups → Local Groups (with Meetings + Gsrs attached)
    // ====================================================================

    private static List<LocalModels.Group> MapGroups(
        List<UnityModels.Group> unityGroups,
        List<UnityModels.Member> unityMembers)
    {
        // Build GSR lookup by home group ID
        // All GSRs per group (Unity allows multiple GSRs for one group)
        var gsrsByGroupId = unityMembers
            .Where(m => m.IsGsr && m.HomeGroupId.HasValue)
            .GroupBy(m => m.HomeGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var groups = new List<LocalModels.Group>();

        foreach (var unityGroup in unityGroups)
        {
            gsrsByGroupId.TryGetValue(unityGroup.Id, out var gsrs);

            var localGroup = new LocalModels.Group
            {
                ID = unityGroup.Id,
                Name = unityGroup.Title,
            };

            // Attach GSR members — GroupId resolved by EF via the Group nav property
            if (gsrs != null)
            {
                foreach (var gsr in gsrs)
                {
                    localGroup.Gsrs.Add(new LocalModels.Member
                    {
                        ID = gsr.Id,
                        Name = gsr.AnonymousName,
                        EmailPersonal = gsr.PersonalEmail,
                        Phone = gsr.MobileNumber,
                    });
                }
            }

            // Attach meetings — GroupId resolved by EF via the Group nav property
            if (unityGroup.HasExpandedMeetings && unityGroup.Meetings.Count > 0)
            {
                foreach (var meeting in unityGroup.Meetings)
                {
                    localGroup.Meetings.Add(MapUnityMeetingToLocal(unityGroup, meeting));
                }
            }
            else
            {
                localGroup.Meetings.Add(MapGroupOnlyToLocal(unityGroup));
            }

            groups.Add(localGroup);
        }

        return groups;
    }

    private static LocalModels.Meeting MapUnityMeetingToLocal(
        UnityModels.Group unityGroup,
        UnityModels.Meeting meeting)
    {
        // Prefer meeting-level contacts, fall back to group-level contacts
        var contacts = meeting.Contacts.Count > 0
            ? meeting.Contacts
            : unityGroup.Contacts;

        return new LocalModels.Meeting
        {
            ID = meeting.Id,
            Day = meeting.DayOfWeek,
            Time = meeting.Time,
            EndTime = meeting.EndTime,
            Name = !string.IsNullOrEmpty(meeting.Name) ? meeting.Name : unityGroup.Title,
            MeetingGenericEmail = unityGroup.Email,
            UsingGeneric = !string.IsNullOrEmpty(unityGroup.Email) ? true : null,
            Location = meeting.Location?.Name,
            Address = meeting.Location?.FormattedAddress,
            Contact1Name = contacts.ElementAtOrDefault(0)?.Name,
            Contact1Email = contacts.ElementAtOrDefault(0)?.Email,
            Contact1Phone = contacts.ElementAtOrDefault(0)?.Phone,
            Contact2Name = contacts.ElementAtOrDefault(1)?.Name,
            Contact2Email = contacts.ElementAtOrDefault(1)?.Email,
            Contact2Phone = contacts.ElementAtOrDefault(1)?.Phone,
            Contact3Name = contacts.ElementAtOrDefault(2)?.Name,
            Contact3Email = contacts.ElementAtOrDefault(2)?.Email,
            Contact3Phone = contacts.ElementAtOrDefault(2)?.Phone,
            Types = meeting.Types.Count > 0 ? string.Join(", ", meeting.Types) : null
        };
    }

    private static LocalModels.Meeting MapGroupOnlyToLocal(UnityModels.Group unityGroup)
    {
        var contacts = unityGroup.Contacts;

        return new LocalModels.Meeting
        {
            ID = unityGroup.Id,
            Name = unityGroup.Title,
            MeetingGenericEmail = unityGroup.Email,
            UsingGeneric = !string.IsNullOrEmpty(unityGroup.Email) ? true : null,
            Contact1Name = contacts.ElementAtOrDefault(0)?.Name,
            Contact1Email = contacts.ElementAtOrDefault(0)?.Email,
            Contact1Phone = contacts.ElementAtOrDefault(0)?.Phone,
            Contact2Name = contacts.ElementAtOrDefault(1)?.Name,
            Contact2Email = contacts.ElementAtOrDefault(1)?.Email,
            Contact2Phone = contacts.ElementAtOrDefault(1)?.Phone,
            Contact3Name = contacts.ElementAtOrDefault(2)?.Name,
            Contact3Email = contacts.ElementAtOrDefault(2)?.Email,
            Contact3Phone = contacts.ElementAtOrDefault(2)?.Phone,
        };
    }

    // ====================================================================
    // Mapping: Unity IntergroupMeetings → Local IntergroupMeetings
    // ====================================================================

    private static List<LocalModels.IntergroupMeeting> MapIntergroupMeetings(
        List<UnityModels.IntergroupMeeting> unityMeetings)
    {
        return unityMeetings.Select(m => new LocalModels.IntergroupMeeting
        {
            ID = m.Id,
            Title = string.IsNullOrWhiteSpace(m.Title) ? null : m.Title,
            Date = m.Date,
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
    }


    private static List<LocalModels.Position> MapPositions(
        List<UnityModels.Position> unityPositions,
        List<UnityModels.Member> members)
    {
        // Build a lookup of members by their intergroup position ID
        var membersByPositionId = members
            .Where(m => m.IntergroupPositionId.HasValue)
            .GroupBy(m => m.IntergroupPositionId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        return unityPositions.Select(p =>
        {
            membersByPositionId.TryGetValue(p.Id, out var holder);

            return new LocalModels.Position
            {
                ID = p.Id,
                UnityPositionId = p.Id,
                MemberId = holder?.Id,
                PositionName = p.ShortDescription,
                PositionLongName = p.LongName,
                PositionGenericEmail = p.Email,
                MemberAnonymousName = holder?.AnonymousName,
                MemberPersonalEmail = holder?.PersonalEmail,
                MemberMobile = holder?.MobileNumber,
                PositionDuration = p.TermYears > 0 ? $"{p.TermYears} year{(p.TermYears != 1 ? "s" : "")}" : null,
            };
        }).ToList();
    }
}