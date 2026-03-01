namespace TheBleedingDeacons.Unity.Data.Entities;

/// <summary>
/// Represents an intergroup position synced from the Unity API.
/// </summary>
public class Position
{
    public int Id { get; set; }
    public string ShortDescription { get; set; } = string.Empty;
    public string? LongName { get; set; }
    public string? Email { get; set; }
    public int MinimumSobriety { get; set; }
    public int TermYears { get; set; }

    // Navigation: members currently holding this position
    public List<Member> Holders { get; set; } = [];
}
