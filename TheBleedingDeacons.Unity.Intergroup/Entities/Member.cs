namespace TheBleedingDeacons.Unity.Intergroup.Entities;

/// <summary>
/// Represents a member synced from the Unity API.
/// </summary>
public class Member
{
    public int Id { get; set; }
    public string AnonymousName { get; set; } = string.Empty;
    public string? PrivateName { get; set; }
    public string? Email { get; set; }
    public string? PersonalEmail { get; set; }
    public string? MobileNumber { get; set; }
    public bool IsGsr { get; set; }

    /// <summary>
    /// UTC timestamp of the last local persistence of changes to this entity.
    /// </summary>
    public DateTime? Updated { get; set; }

    // FK to the member's home group (nullable — some members may not have a home group)
    public int? HomeGroupId { get; set; }
    public Group? HomeGroup { get; set; }

    // FK to the intergroup position held by this member (nullable)
    public int? IntergroupPositionId { get; set; }
    public Position? IntergroupPosition { get; set; }
}
