using System;

namespace TheBleedingDeacons.Intergroup.Register.Models;

/// <summary>
/// Represents a REST API call that could not be sent immediately (offline or transient failure)
/// and should be retried when connectivity is restored.
/// </summary>
public class QueuedApiCall
{
    public int Id { get; set; }

    /// <summary>Discriminator so callers know what to do with the response (e.g. "RegisterAttendee").</summary>
    public required string OperationType { get; set; }

    /// <summary>Fully-qualified endpoint URL.</summary>
    public required string Url { get; set; }

    /// <summary>HTTP method, e.g. "POST".</summary>
    public required string HttpMethod { get; set; }

    /// <summary>JSON-serialised request body (null for GET).</summary>
    public string? JsonPayload { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptUtc { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }

    /// <summary>Whether this entry has been permanently abandoned after too many retries.</summary>
    public bool IsFailed { get; set; }
}