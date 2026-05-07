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

    // ── GDPR compliance ──────────────────────────────────────────────
    //
    // Mirrors the five fields the Unity API serialises under the
    // `gdpr_compliance` JSON sub-object on member responses, but stored
    // flat here for simpler change-tracking and snapshot diffs in
    // reconciliation. Nullable because pre-compliance-feature snapshots
    // and members from older Unity servers carry no value.

    /// <summary>
    /// Whether the member has currently recorded acceptance of the
    /// privacy policy. Null when no state has ever been recorded;
    /// false records an explicit revocation.
    /// </summary>
    public bool? GdprAccepted { get; set; }

    /// <summary>
    /// UTC timestamp at which the current state was recorded (acceptance
    /// or revocation). Null when never recorded.
    /// </summary>
    public DateTime? GdprAcceptedAt { get; set; }

    /// <summary>
    /// The privacy policy version that was accepted. Null after a
    /// revocation, or when no acceptance has been recorded.
    /// </summary>
    public string? GdprAcceptanceVersion { get; set; }

    /// <summary>
    /// How the acceptance was captured. Set to <c>"register-app"</c>
    /// by <see cref="Services.ComplianceService"/> for offline-recorded
    /// acceptances; <c>"web-form"</c>, <c>"api"</c>, etc. when set
    /// elsewhere. Null after a revocation.
    /// </summary>
    public string? GdprAcceptanceMethod { get; set; }

    /// <summary>
    /// The exact statement the member accepted. Null after a revocation.
    /// </summary>
    public string? GdprAcceptanceStatement { get; set; }

    /// <summary>
    /// WordPress post ID of the privacy policy that the member accepted.
    /// Sent to the Unity server during reconciliation in place of the
    /// statement body — the server resolves the body itself via
    /// Scrutiny's <c>PrivacyPolicyRepository</c>, so the wire payload
    /// only carries the identifier. Null after a revocation, or when
    /// no acceptance has been recorded.
    /// </summary>
    public int? GdprAcceptancePolicyId { get; set; }

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