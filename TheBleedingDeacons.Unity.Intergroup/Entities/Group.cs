namespace TheBleedingDeacons.Unity.Intergroup.Entities;

/// <summary>
/// Represents a group synced from the Unity API.
/// </summary>
public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }
    public int? DistrictId { get; set; }

    /// <summary>
    /// Flag indicating whether this group has registered attendance
    /// for the active intergroup meeting. Persisted locally.
    /// </summary>
    public bool Registered { get; set; }

    // Navigation: a group can have multiple GSR members
    public List<Member> Members { get; set; } = [];

    // Navigation: a group can have multiple meetings
    public List<Meeting> Meetings { get; set; } = [];

    // Navigation: a group can have multiple contacts
    public List<Contact> Contacts { get; set; } = [];
}
