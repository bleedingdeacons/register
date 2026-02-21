using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, Name={Name}")]
public class Group
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public List<Meeting> Meetings { get; set; } = new();

    // One-to-one: the GSR for this group
    public Member? Gsr { get; set; }
}
