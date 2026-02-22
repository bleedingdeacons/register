using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, Name={Name}")]
public class Group
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public List<Meeting> Meetings { get; set; } = new();

    // One-to-many: a group can have more than one GSR
    public List<Member> Gsrs { get; set; } = new();
}