using System.Diagnostics;

namespace TheBleedingDeacons.Intergroup.Register.Models;

[DebuggerDisplay("ID={ID}, Name={Name}")]
public class Member
{
    public int ID { get; set; }
    public string? Name { get; set; }
    public string? EmailPersonal { get; set; }
    public string? Phone { get; set; }

    // One-to-one FK back to Group
    public int GroupId { get; set; }
    public Group? Group { get; set; }
}
