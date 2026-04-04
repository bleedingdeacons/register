namespace TheBleedingDeacons.Unity.Intergroup.Entities;

/// <summary>
/// Stores a serialised copy of an entity's state at the point the snapshot was
/// taken (i.e. immediately after the last Unity sync and before the Register
/// app starts making local modifications).
///
/// During reconciliation the snapshot is compared with the entity's current
/// state to determine which fields were changed locally and therefore need to
/// be preserved when the next Unity sync overwrites the database.
/// </summary>
public class EntitySnapshot
{
    public int Id { get; set; }

    /// <summary>
    /// Discriminator that identifies the entity type (e.g. "Group", "Member",
    /// "Position", "Meeting", "Contact", "IntergroupMeeting").
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// The primary-key value of the snapshotted entity.
    /// For entities with auto-increment keys (Contact) the value may change
    /// across syncs, so <see cref="EntityKey"/> holds whatever the ID was at
    /// snapshot time.
    /// </summary>
    public int EntityKey { get; set; }

    /// <summary>
    /// JSON-serialised representation of the entity at snapshot time.
    /// </summary>
    public string JsonData { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp when the snapshot was captured.
    /// </summary>
    public DateTime SnapshotUtc { get; set; }
}