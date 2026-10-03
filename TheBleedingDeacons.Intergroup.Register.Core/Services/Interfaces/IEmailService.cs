using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	public interface IEmailService
	{
		// Properties
		bool IsOfflineMode { get; set; }
		int MaxRetries { get; set; }

		// Circuit breaker state — observable by the UI to show email health
		/// <summary>True when background processing is paused due to repeated failures.</summary>
		bool IsCircuitOpen { get; }
		/// <summary>Number of consecutive queue processing failures.</summary>
		int ConsecutiveQueueFailures { get; }
		/// <summary>Error message from the most recent failure, or null.</summary>
		string? LastQueueError { get; }
		/// <summary>UTC timestamp when the circuit breaker opened, or null.</summary>
		DateTime? CircuitOpenedAt { get; }

		// Events
		event EventHandler<EmailSentEventArgs>? EmailSent;
		event EventHandler<EmailFailedEventArgs>? EmailFailed;
		event EventHandler<QueueProcessedEventArgs>? QueueProcessed;

		/// <summary>
		/// Fires when the background-queue circuit breaker transitions between
		/// open (paused) and closed (running). Raised from background threads —
		/// marshal to the UI thread in handlers if you touch bindable state.
		/// </summary>
		event EventHandler<CircuitStateChangedEventArgs>? CircuitStateChanged;

		// Core email sending methods
		/// <summary>
		/// Sends an email immediately (or queues it if offline). When <paramref name="from"/>
		/// is supplied (non-null, non-whitespace), it overrides the default From address
		/// — which is otherwise the SMTP account username from the active
		/// <see cref="SmtpConfiguration"/>. Use this when a feature has its own
		/// sender identity (e.g. a compliance recipient sending audit-trail copies)
		/// and shouldn't be attributed to the generic SMTP account. The override
		/// is per-call: it does not mutate the service-wide default.
		///
		/// <para><paramref name="replyTo"/> sets the message's Reply-To header
		/// independently of the From address. Use this to keep From aligned with
		/// the SMTP login (so SPF/DMARC checks pass and the provider doesn't
		/// rewrite the header) while still routing replies to a different
		/// mailbox — the typical pattern for compliance/audit-trail emails.
		/// Null or whitespace means "no Reply-To header".</para>
		/// </summary>
		Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = false, string? from = null, string? cc = null, string? bcc = null, string? replyTo = null);

		/// <summary>
		/// Queues an email for background sending. <paramref name="from"/> behaves
		/// the same way as on <see cref="SendEmailAsync"/>: a non-empty value
		/// overrides the default From address for this email only and is persisted
		/// onto the <c>QueuedEmail</c> row, so it survives an app restart and is
		/// applied when the queue processor eventually sends the message.
		///
		/// <para><paramref name="replyTo"/> behaves the same way: a non-empty
		/// value is persisted on the <c>QueuedEmail</c> row and applied as the
		/// Reply-To header when the message is eventually sent, surviving an
		/// app restart in between.</para>
		/// </summary>
		Task QueueEmailAsync(string to, string subject, string body, bool isHtml = false, string? from = null, string? cc = null, string? bcc = null, string? replyTo = null);

		// Configuration methods
		Task UpdateConfigurationAsync(SmtpConfiguration config);

		// Queue management methods
		Task<bool> ProcessQueueAsync();
		Task<List<QueuedEmail>> GetQueuedEmailsAsync();
		Task<List<QueuedEmail>> GetQueuedEmailsByStatusAsync(EmailStatus status);
		Task<int> GetQueueCountAsync();
		Task ClearQueueAsync();
		Task ClearSentEmailsAsync();
		Task RetryFailedEmailsAsync();
		Task ResetRetryCountAsync();
		Task ResetRetryCountAsync(int emailId);

		// Testing and utility methods
		Task<bool> TestSmtpConnectionAsync(SmtpConfiguration config);

		/// <summary>
		/// Lightweight probe: connects and authenticates against the SMTP server
		/// using the given configuration, without sending any email. Use to
		/// distinguish "network is reachable but SMTP broken" from "no network"
		/// without dropping a test email into the user's inbox.
		/// Returns a structured result so the UI can show the precise failure
		/// reason (auth, timeout, DNS, TLS, etc.).
		/// </summary>
		Task<SmtpReachabilityResult> TestSmtpReachabilityAsync(SmtpConfiguration config, CancellationToken cancellationToken = default);

		// Offline mode methods
		void EnableOfflineMode();
		void DisableOfflineMode();

		/// <summary>
		/// Resets the circuit breaker and resumes background queue processing.
		/// Call after updating SMTP settings or resolving the underlying issue.
		/// </summary>
		void ResetCircuitBreaker();
	}

	#region Event Argument Classes

	/// <summary>
	/// Event arguments for successful email sending.
	/// </summary>
	public class EmailSentEventArgs : EventArgs
	{
		/// <summary>
		/// The email that was successfully sent.
		/// </summary>
		public QueuedEmail Email { get; set; } = null!;

		/// <summary>
		/// Timestamp when the email was sent.
		/// </summary>
		public DateTime SentAt { get; set; } = DateTime.UtcNow;

		/// <summary>
		/// Time taken to send the email (if available).
		/// </summary>
		public TimeSpan? SendDuration { get; set; }

		/// <summary>
		/// Additional metadata about the send operation.
		/// </summary>
		public Dictionary<string, object> Metadata { get; set; } = new(StringComparer.Ordinal);
	}

	/// <summary>
	/// Event arguments for failed email sending.
	/// </summary>
	public class EmailFailedEventArgs : EventArgs
	{
		/// <summary>
		/// The email that failed to send.
		/// </summary>
		public QueuedEmail Email { get; set; } = null!;

		/// <summary>
		/// Error message describing the failure.
		/// </summary>
		public string Error { get; set; } = string.Empty;

		/// <summary>
		/// Timestamp when the failure occurred.
		/// </summary>
		public DateTime FailedAt { get; set; } = DateTime.UtcNow;

		/// <summary>
		/// Whether this failure is considered retryable.
		/// </summary>
		public bool IsRetryable { get; set; }

		/// <summary>
		/// The underlying exception that caused the failure (if available).
		/// </summary>
		public Exception? Exception { get; set; }

		/// <summary>
		/// Current attempt number when this failure occurred.
		/// </summary>
		public int AttemptNumber { get; set; }

		/// <summary>
		/// Whether this was the final attempt (max retries reached).
		/// </summary>
		public bool IsFinalAttempt { get; set; }
	}

	/// <summary>
	/// Event arguments for queue processing completion.
	/// </summary>
	public class QueueProcessedEventArgs : EventArgs
	{
		/// <summary>
		/// Number of emails successfully processed and sent.
		/// </summary>
		public int ProcessedCount { get; set; }

		/// <summary>
		/// Number of emails that failed during processing.
		/// </summary>
		public int FailedCount { get; set; }

		/// <summary>
		/// Number of emails remaining in the queue after processing.
		/// </summary>
		public int RemainingCount { get; set; }

		/// <summary>
		/// Timestamp when processing completed.
		/// </summary>
		public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

		/// <summary>
		/// Total time taken to process the queue.
		/// </summary>
		public TimeSpan ProcessingTime { get; set; }

		/// <summary>
		/// Whether processing was interrupted (due to offline mode or network issues).
		/// </summary>
		public bool WasInterrupted { get; set; }

		/// <summary>
		/// Reason for interruption (if any).
		/// </summary>
		public string? InterruptionReason { get; set; }

		/// <summary>
		/// Average time per email sent during this processing session.
		/// </summary>
		public TimeSpan AverageTimePerEmail => ProcessedCount > 0
			? TimeSpan.FromMilliseconds(ProcessingTime.TotalMilliseconds / ProcessedCount)
			: TimeSpan.Zero;

		/// <summary>
		/// Success rate for this processing session (0.0 to 1.0).
		/// </summary>
		public double SuccessRate => ProcessedCount + FailedCount > 0
			? (double)ProcessedCount / (ProcessedCount + FailedCount)
			: 0.0;
	}

	/// <summary>
	/// Event arguments for circuit-breaker state transitions.
	/// Fired when the background queue pauses due to repeated failures
	/// (<see cref="IsOpen"/> = true) and when it resumes after a reset
	/// or SMTP config update (<see cref="IsOpen"/> = false).
	/// </summary>
	public class CircuitStateChangedEventArgs : EventArgs
	{
		/// <summary>
		/// True when the breaker is now open (background processing paused);
		/// false when it has just been reset (processing will resume on the
		/// next timer tick).
		/// </summary>
		public bool IsOpen { get; set; }

		/// <summary>
		/// Number of consecutive queue-processing failures recorded at the
		/// moment of the transition. Zero for close transitions.
		/// </summary>
		public int ConsecutiveFailures { get; set; }

		/// <summary>
		/// The most recent error message observed by the background queue
		/// processor, or <c>null</c> if none. Useful for surfacing the
		/// underlying cause (SMTP auth failure, DNS failure, etc.) in the UI.
		/// </summary>
		public string? LastError { get; set; }

		/// <summary>
		/// UTC timestamp when the breaker opened, or <c>null</c> for close
		/// transitions.
		/// </summary>
		public DateTime? OpenedAt { get; set; }
	}

	/// <summary>
	/// Outcome of a lightweight SMTP reachability probe. The kind field lets
	/// the UI choose the right message ("check your password" vs "check your
	/// connection") rather than showing a raw exception string.
	/// </summary>
	public sealed class SmtpReachabilityResult
	{
		/// <summary>True when connect + authenticate both succeeded.</summary>
		public bool IsReachable { get; init; }

		/// <summary>Categorised failure kind (or <see cref="SmtpReachabilityKind.Success"/>).</summary>
		public SmtpReachabilityKind Kind { get; init; }

		/// <summary>Human-readable detail: the exception message, or a success confirmation.</summary>
		public string Message { get; init; } = string.Empty;

		/// <summary>Underlying exception on failure, for logging.</summary>
		public Exception? Exception { get; init; }

		public static SmtpReachabilityResult Success() =>
			new() { IsReachable = true, Kind = SmtpReachabilityKind.Success, Message = "SMTP server is reachable and credentials are valid." };

		public static SmtpReachabilityResult Failure(SmtpReachabilityKind kind, string message, Exception? ex = null) =>
			new() { IsReachable = false, Kind = kind, Message = message, Exception = ex };
	}

	/// <summary>
	/// Coarse failure categories from a reachability probe. Used by callers
	/// to decide whether to count a failure toward the circuit breaker
	/// (Auth/Config = yes, Network/Timeout = no) and to show the user a
	/// meaningful hint rather than a raw stack trace.
	/// </summary>
	public enum SmtpReachabilityKind
	{
		/// <summary>Connect + authenticate both succeeded.</summary>
		Success,

		/// <summary>No network route, DNS resolution failed, or TCP connect refused.</summary>
		Network,

		/// <summary>Connection opened but the operation timed out.</summary>
		Timeout,

		/// <summary>TCP connection refused, TLS handshake failed, or certificate invalid.</summary>
		Tls,

		/// <summary>Server reachable but credentials were rejected.</summary>
		Auth,

		/// <summary>Everything else (protocol errors, unexpected exceptions).</summary>
		Other
	}

	#endregion

	#region Extended Interface (Optional - for advanced implementations)

	/// <summary>
	/// Extended mail service interface with additional advanced features.
	/// Implement this if you need features like templates, attachments, or bulk operations.
	/// </summary>
	public interface IAdvancedMailService : IEmailService
	{
		#region Template Support

		/// <summary>
		/// Sends an email using a predefined template with variable substitution.
		/// </summary>
		/// <param name="to">Recipient email address</param>
		/// <param name="templateName">Name of the email template</param>
		/// <param name="templateData">Data for template variable substitution</param>
		/// <param name="cc">Carbon copy recipients (optional)</param>
		/// <param name="bcc">Blind carbon copy recipients (optional)</param>
		Task<bool> SendTemplatedEmailAsync(string to, string templateName,
			Dictionary<string, object> templateData, string? cc = null, string? bcc = null);

		/// <summary>
		/// Registers a new email template.
		/// </summary>
		/// <param name="templateName">Unique template identifier</param>
		/// <param name="subject">Email subject template (supports variables)</param>
		/// <param name="bodyTemplate">Email body template (supports variables)</param>
		/// <param name="isHtml">Whether the template is HTML</param>
		Task RegisterTemplateAsync(string templateName, string subject, string bodyTemplate, bool isHtml = true);

		#endregion

		#region Attachment Support

		/// <summary>
		/// Sends an email with file attachments.
		/// </summary>
		/// <param name="to">Recipient email address</param>
		/// <param name="subject">Email subject</param>
		/// <param name="body">Email body</param>
		/// <param name="attachments">List of file paths to attach</param>
		/// <param name="isHtml">Whether body is HTML</param>
		/// <param name="cc">Carbon copy recipients (optional)</param>
		/// <param name="bcc">Blind carbon copy recipients (optional)</param>
		Task<bool> SendEmailWithAttachmentsAsync(string to, string subject, string body,
			List<string> attachments, bool isHtml = false, string? cc = null, string? bcc = null);

		#endregion

		#region Bulk Operations

		/// <summary>
		/// Sends the same email to multiple recipients efficiently.
		/// </summary>
		/// <param name="recipients">List of recipient email addresses</param>
		/// <param name="subject">Email subject</param>
		/// <param name="body">Email body</param>
		/// <param name="isHtml">Whether body is HTML</param>
		/// <param name="batchSize">Number of emails to process in each batch</param>
		Task<BulkEmailResult> SendBulkEmailAsync(List<string> recipients, string subject,
			string body, bool isHtml = false, int batchSize = 50);

		#endregion

		#region Analytics and Reporting

		/// <summary>
		/// Gets email sending statistics for the specified date range.
		/// </summary>
		/// <param name="from">Start date for statistics</param>
		/// <param name="to">End date for statistics</param>
		Task<EmailStatistics> GetEmailStatisticsAsync(DateTime from, DateTime to);

		/// <summary>
		/// Gets the current health status of the email service.
		/// </summary>
		Task<ServiceHealthStatus> GetHealthStatusAsync();

		#endregion
	}

	#endregion

	#region Supporting Data Classes for Advanced Features

	/// <summary>
	/// Result of bulk email operations.
	/// </summary>
	public class BulkEmailResult
	{
		/// <summary>
		/// Total number of emails attempted.
		/// </summary>
		public int TotalAttempted { get; set; }

		/// <summary>
		/// Number of emails sent successfully.
		/// </summary>
		public int Successful { get; set; }

		/// <summary>
		/// Number of emails that failed.
		/// </summary>
		public int Failed { get; set; }

		/// <summary>
		/// Number of emails queued for later sending.
		/// </summary>
		public int Queued { get; set; }

		/// <summary>
		/// Time taken for the bulk operation.
		/// </summary>
		public TimeSpan Duration { get; set; }

		/// <summary>
		/// List of failed email addresses with error messages.
		/// </summary>
		public Dictionary<string, string> FailedEmails { get; set; } = new(StringComparer.Ordinal);

		/// <summary>
		/// Success rate (0.0 to 1.0).
		/// </summary>
		public double SuccessRate => TotalAttempted > 0 ? (double)Successful / TotalAttempted : 0.0;
	}

	/// <summary>
	/// Email service statistics.
	/// </summary>
	public class EmailStatistics
	{
		/// <summary>
		/// Date range for these statistics.
		/// </summary>
		public DateTime FromDate { get; set; }
		public DateTime ToDate { get; set; }

		/// <summary>
		/// Total emails sent in the period.
		/// </summary>
		public int TotalSent { get; set; }

		/// <summary>
		/// Total emails failed in the period.
		/// </summary>
		public int TotalFailed { get; set; }

		/// <summary>
		/// Total emails currently queued.
		/// </summary>
		public int TotalQueued { get; set; }

		/// <summary>
		/// Average sending time per email.
		/// </summary>
		public TimeSpan AverageSendTime { get; set; }

		/// <summary>
		/// Peak sending rate (emails per hour).
		/// </summary>
		public double PeakSendingRate { get; set; }

		/// <summary>
		/// Most common failure reasons.
		/// </summary>
		public Dictionary<string, int> FailureReasons { get; set; } = new(StringComparer.Ordinal);

		/// <summary>
		/// Daily breakdown of email counts.
		/// </summary>
		public Dictionary<DateTime, DailyEmailStats> DailyStats { get; set; } = new();
	}

	/// <summary>
	/// Daily email statistics.
	/// </summary>
	public class DailyEmailStats
	{
		public DateTime Date { get; set; }
		public int Sent { get; set; }
		public int Failed { get; set; }
		public int Queued { get; set; }
		public TimeSpan AverageSendTime { get; set; }
	}

	/// <summary>
	/// Service health status.
	/// </summary>
	public class ServiceHealthStatus
	{
		/// <summary>
		/// Overall health status.
		/// </summary>
		public HealthLevel Status { get; set; }

		/// <summary>
		/// Last time the service successfully sent an email.
		/// </summary>
		public DateTime? LastSuccessfulSend { get; set; }

		/// <summary>
		/// Current queue size.
		/// </summary>
		public int QueueSize { get; set; }

		/// <summary>
		/// Number of failed emails in queue.
		/// </summary>
		public int FailedEmailsCount { get; set; }

		/// <summary>
		/// Whether SMTP connectivity is working.
		/// </summary>
		public bool SmtpConnectivity { get; set; }

		/// <summary>
		/// Whether network connectivity is available.
		/// </summary>
		public bool NetworkConnectivity { get; set; }

		/// <summary>
		/// Whether background processing is running.
		/// </summary>
		public bool BackgroundProcessingActive { get; set; }

		/// <summary>
		/// List of current issues or warnings.
		/// </summary>
		public List<string> Issues { get; set; } = new();

		/// <summary>
		/// Additional diagnostic information.
		/// </summary>
		public Dictionary<string, object> DiagnosticInfo { get; set; } = new(StringComparer.Ordinal);
	}

	/// <summary>
	/// Health status levels.
	/// </summary>
	public enum HealthLevel
	{
		/// <summary>
		/// Service is healthy and operating normally.
		/// </summary>
		Healthy = 0,

		/// <summary>
		/// Service has minor issues but is still functional.
		/// </summary>
		Warning = 1,

		/// <summary>
		/// Service has significant issues affecting functionality.
		/// </summary>
		Degraded = 2,

		/// <summary>
		/// Service is not functional.
		/// </summary>
		Unhealthy = 3
	}

	#endregion

	#region Utility Extensions (Optional)

	/// <summary>
	/// Extension methods for IMailService to provide convenience methods.
	/// </summary>
	public static class MailServiceExtensions
	{
		/// <summary>
		/// Sends a simple text email. <paramref name="from"/> behaves the same
		/// way as on <see cref="IEmailService.SendEmailAsync"/>: a non-empty
		/// value overrides the default From address (the SMTP account
		/// username) for this email only.
		/// </summary>
		public static Task<bool> SendTextEmailAsync(this IEmailService mailService,
			string to, string subject, string body, string? from = null)
		{
			return mailService.SendEmailAsync(to, subject, body, isHtml: false, from: from);
		}

		/// <summary>
		/// Sends a simple HTML email. <paramref name="from"/> behaves the same
		/// way as on <see cref="IEmailService.SendEmailAsync"/>: a non-empty
		/// value overrides the default From address (the SMTP account
		/// username) for this email only.
		/// </summary>
		public static Task<bool> SendHtmlEmailAsync(this IEmailService mailService,
			string to, string subject, string htmlBody, string? from = null)
		{
			return mailService.SendEmailAsync(to, subject, htmlBody, isHtml: true, from: from);
		}

		/// <summary>
		/// Queues a simple text email. <paramref name="from"/> behaves the same
		/// way as on <see cref="IEmailService.QueueEmailAsync"/>: a non-empty
		/// value overrides the default From address for this email only and
		/// is persisted onto the queue row, surviving an app restart.
		/// </summary>
		public static Task QueueTextEmailAsync(this IEmailService mailService,
			string to, string subject, string body, string? from = null)
		{
			return mailService.QueueEmailAsync(to, subject, body, isHtml: false, from: from);
		}

		/// <summary>
		/// Queues a simple HTML email. <paramref name="from"/> behaves the same
		/// way as on <see cref="IEmailService.QueueEmailAsync"/>: a non-empty
		/// value overrides the default From address for this email only and
		/// is persisted onto the queue row, surviving an app restart.
		/// </summary>
		public static Task QueueHtmlEmailAsync(this IEmailService mailService,
			string to, string subject, string htmlBody, string? from = null)
		{
			return mailService.QueueEmailAsync(to, subject, htmlBody, isHtml: true, from: from);
		}

		/// <summary>
		/// Gets count of emails by status.
		/// </summary>
		public static async Task<Dictionary<EmailStatus, int>> GetEmailCountsByStatusAsync(this IEmailService mailService)
		{
			var counts = new Dictionary<EmailStatus, int>();

			foreach (EmailStatus status in Enum.GetValues<EmailStatus>())
			{
				var emails = await mailService.GetQueuedEmailsByStatusAsync(status);
				counts[status] = emails.Count;
			}

			return counts;
		}

		/// <summary>
		/// Checks if the service is healthy (can connect and authenticate).
		/// </summary>
		public static async Task<bool> IsHealthyAsync(this IEmailService mailService, SmtpConfiguration config)
		{
			try
			{
				return await mailService.TestSmtpConnectionAsync(config);
			}
			catch
			{
				return false;
			}
		}
	}

	#endregion
}