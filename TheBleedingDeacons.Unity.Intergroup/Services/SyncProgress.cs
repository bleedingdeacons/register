namespace TheBleedingDeacons.Unity.Intergroup.Services;

/// <summary>
/// Progress payload reported by <see cref="UnitySyncService"/> and the
/// Register-app's reconciliation service so that the UI can surface what
/// the sync is currently doing.
///
/// <para>
/// Reporting uses the standard <see cref="IProgress{T}"/> contract: the
/// service produces values, the consumer (e.g. an admin view-model wiring
/// up a <see cref="Progress{T}"/>) marshals them onto the UI thread.
/// </para>
///
/// <para>
/// <b>Determinate vs indeterminate</b>: when <see cref="Total"/> is
/// <c>null</c> the stage has no known unit count (e.g. database write,
/// snapshot capture) and the UI should show a busy indicator. When
/// <see cref="Total"/> is set, <see cref="Current"/> is in <c>[0, Total]</c>
/// and the UI can render a determinate progress bar.
/// </para>
/// </summary>
/// <param name="Stage">
/// Coarse-grained pipeline phase. Stable enum values so consumers can
/// switch on them (e.g. swap between determinate / indeterminate UI)
/// without depending on the wording of <see cref="Message"/>.
/// </param>
/// <param name="Message">
/// Human-readable description of the current step. Safe to bind directly
/// to a label in the UI; already past-tense / present-progressive as
/// appropriate (e.g. "Fetching groups…", "Re-syncing from Unity…").
/// </param>
/// <param name="Current">
/// Items processed so far in this stage. Always 0 for indeterminate
/// stages. For paginated fetches this is the page or item index.
/// </param>
/// <param name="Total">
/// Total items expected in this stage, or <c>null</c> when the total is
/// not yet known (e.g. before the first page response arrives) or when
/// the stage is conceptually indeterminate.
/// </param>
public sealed record SyncProgress(
	SyncStage Stage,
	string Message,
	int Current = 0,
	int? Total = null);

/// <summary>
/// Pipeline stages reported during sync and reconciliation. Ordered
/// roughly chronologically inside each pipeline; the consumer should
/// not assume every stage fires on every call (e.g. reconciliation
/// only emits <see cref="PushCreates"/> when there are new members).
/// </summary>
public enum SyncStage
{
	/// <summary>Initial state, before any work has started.</summary>
	Starting,

	/// <summary>Fetching paginated data from the Unity API.</summary>
	Fetching,

	/// <summary>Replacing local SQLite data with fresh Unity data.</summary>
	WritingDatabase,

	/// <summary>Capturing a baseline snapshot for change detection.</summary>
	CapturingSnapshot,

	/// <summary>Replaying durable event-log entries before diffing.</summary>
	ReplayingLog,

	/// <summary>Diffing local state against the snapshot.</summary>
	DetectingChanges,

	/// <summary>Pushing newly-created members to Unity.</summary>
	PushCreates,

	/// <summary>Pushing modified members to Unity.</summary>
	PushUpdates,

	/// <summary>Pushing GDPR compliance changes to Unity.</summary>
	PushCompliance,

	/// <summary>Pushing group / officer registrations to Unity.</summary>
	PushRegistrations,

	/// <summary>Re-syncing from Unity after pushing changes.</summary>
	Resyncing,

	/// <summary>Pipeline finished — final values flushed to the UI.</summary>
	Complete
}
