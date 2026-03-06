namespace TheBleedingDeacons.Unity.Intergroup.Entities;

/// <summary>
/// Represents a contact associated with a group, synced from the Unity API.
/// </summary>
public class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // FK to the parent group
    public int GroupId { get; set; }
    public Group? Group { get; set; }
}
