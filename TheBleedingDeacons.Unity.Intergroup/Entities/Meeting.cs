namespace TheBleedingDeacons.Unity.Intergroup.Entities;

/// <summary>
/// Represents a meeting synced from the Unity API.
/// </summary>
public class Meeting
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Day { get; set; }
    public string? DayOfWeek { get; set; }
    public string? Time { get; set; }
    public string? EndTime { get; set; }
    public string? LocationName { get; set; }
    public string? Address { get; set; }
    public bool IsOnline { get; set; }
    public string? OnlineLink { get; set; }
    public string? Types { get; set; }

    // FK to the parent group
    public int? GroupId { get; set; }
    public Group? Group { get; set; }
}
