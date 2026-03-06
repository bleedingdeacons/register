namespace TheBleedingDeacons.Unity.Intergroup.Entities;

/// <summary>
/// Represents a dated intergroup meeting occurrence synced from the Unity API.
/// </summary>
public class IntergroupMeeting
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Date { get; set; }

    /// <summary>
    /// Comma-separated group IDs that attended.
    /// </summary>
    public string? GroupAttendeeIds { get; set; }

    /// <summary>
    /// Comma-separated display names of group attendees.
    /// </summary>
    public string? GroupAttendeeNames { get; set; }

    /// <summary>
    /// Comma-separated officer (member) IDs that attended.
    /// </summary>
    public string? OfficerAttendeeIds { get; set; }

    /// <summary>
    /// Comma-separated display names of officer attendees.
    /// </summary>
    public string? OfficerAttendeeNames { get; set; }

    /// <summary>
    /// UTC timestamp of the last local persistence of changes to this entity.
    /// </summary>
    public DateTime? Updated { get; set; }
}
