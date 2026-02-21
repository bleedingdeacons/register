using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Utilities;

public class RegisterData
{
    public List<Group> Groups { get; set; } = new();
    public List<Meeting> Meetings { get; set; } = new();
    public List<Position> Positions { get; set; } = new();
    public List<Member> Members { get; set; } = new();

    public int TotalGroups => Groups.Count;
    public int TotalMeetings => Meetings.Count;
    public int TotalPositions => Positions.Count;
    public int TotalMembers => Members.Count;

    public RegisterData() { }

    public RegisterData(List<Meeting> meetings, List<Position> positions)
        : this(new List<Group>(), meetings, positions, new List<Member>()) { }

    public RegisterData(List<Meeting> meetings, List<Position> positions, List<Member> members)
        : this(new List<Group>(), meetings, positions, members) { }

    public RegisterData(List<Group> groups, List<Meeting> meetings, List<Position> positions, List<Member> members)
    {
        Groups = groups ?? new List<Group>();
        Meetings = meetings ?? new List<Meeting>();
        Positions = positions ?? new List<Position>();
        Members = members ?? new List<Member>();
    }
}
