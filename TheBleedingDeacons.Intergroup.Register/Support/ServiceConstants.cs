namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Centralised configuration constants for queue processing, email handling,
/// and background service timers. Replaces magic numbers scattered across services.
/// 
/// Future improvement: load these values from appsettings.json via IConfiguration
/// so they can be tuned without recompilation.
/// </summary>
public static class ServiceConstants
{
    /// <summary>API queue: maximum retry attempts before marking a call as permanently failed.</summary>
    public const int ApiQueueMaxAttempts = 10;

    /// <summary>Email queue: maximum emails processed per batch run.</summary>
    public const int EmailBatchSize = 20;

    /// <summary>Email queue: delay in milliseconds between sending individual emails in a batch.</summary>
    public const int EmailInterSendDelayMs = 2000;

    /// <summary>Email queue: background timer initial delay before the first queue processing run.</summary>
    public static readonly TimeSpan EmailTimerInitialDelay = TimeSpan.FromMinutes(1);

    /// <summary>Email queue: background timer interval between queue processing runs.</summary>
    public static readonly TimeSpan EmailTimerInterval = TimeSpan.FromMinutes(5);

    /// <summary>Email queue: timeout in seconds for SMTP operations (connect + send).</summary>
    public const int SmtpDefaultTimeoutSeconds = 30;

    /// <summary>Email queue: default maximum retries per email.</summary>
    public const int SmtpDefaultMaxRetries = 10;
}
