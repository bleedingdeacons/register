namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces;

public interface IApiQueueService
{
    /// <summary>
    /// When true, all write calls are treated as if the device were offline
    /// regardless of actual connectivity — they are queued immediately.
    /// The value is persisted across app restarts via Preferences.
    /// </summary>
    bool IsOfflineModeEnabled { get; set; }

    Task EnqueueAsync(
        string operationType,
        string url,
        string httpMethod,
        string? jsonPayload = null,
        CancellationToken cancellationToken = default);

    Task FlushAsync(CancellationToken cancellationToken = default);

    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    event EventHandler<int> PendingCountChanged;
}