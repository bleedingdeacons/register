using TheBleedingDeacons.Unity.Data.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

/// <summary>
/// Container for the full set of data held in the local Unity database.
/// All types are from <see cref="TheBleedingDeacons.Unity.Data.Entities"/>.
/// </summary>
public class RegisterData
{
    public List<Group> Groups { get; set; } = new();
    public List<Meeting> Meetings { get; set; } = new();
    public List<Position> Positions { get; set; } = new();
    public List<Member> Members { get; set; } = new();
    public List<IntergroupMeeting> IntergroupMeetings { get; set; } = new();

    public int TotalGroups => Groups.Count;
    public int TotalMeetings => Meetings.Count;
    public int TotalPositions => Positions.Count;
    public int TotalMembers => Members.Count;
    public int TotalIntergroupMeetings => IntergroupMeetings.Count;

    public RegisterData() { }

    public RegisterData(List<Group> groups, List<Meeting> meetings, List<Position> positions,
        List<Member> members, List<IntergroupMeeting> intergroupMeetings)
    {
        Groups = groups ?? new();
        Meetings = meetings ?? new();
        Positions = positions ?? new();
        Members = members ?? new();
        IntergroupMeetings = intergroupMeetings ?? new();
    }
}
