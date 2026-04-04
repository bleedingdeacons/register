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
    /// The rotation date for this member's intergroup position (e.g. "2025-09-01").
    /// Stored as a plain string to match the Unity API format.
    /// Only meaningful when <see cref="IntergroupPositionId"/> is set.
    /// </summary>
    public string? IntergroupPositionRotation { get; set; }

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

    /// <summary>
    /// Returns <c>true</c> when this member was created locally and has not yet
    /// been assigned a real Unity API ID. Locally-created members are given
    /// negative temporary IDs by <c>TemporaryIdGenerator</c> so they cannot
    /// conflict with Unity's positive WordPress post IDs, even when multiple
    /// Register apps are running simultaneously.
    /// </summary>
    public bool IsTemporary => Id < 0;
}