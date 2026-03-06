namespace TheBleedingDeacons.Unity.Intergroup.Entities;

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

    /// <summary>
    /// Flag indicating whether this position's holder has registered
    /// attendance for the active intergroup meeting. Persisted locally.
    /// </summary>
    public bool Registered { get; set; }

    /// <summary>
    /// UTC timestamp of the last local persistence of changes to this entity.
    /// </summary>
    public DateTime? Updated { get; set; }

    // Navigation: members currently holding this position
    public List<Member> Holders { get; set; } = [];
}
