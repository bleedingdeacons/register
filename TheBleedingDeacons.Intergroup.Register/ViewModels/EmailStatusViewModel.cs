using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class EmailStatusViewModel : BaseViewModel
{
	private static readonly ILogger Logger = AppLogger.ForContext<EmailStatusViewModel>();

	private readonly IEmailService _emailService;
	private readonly IConfigurationService _configService;

	private readonly Timer _refreshTimer;

	private bool _disposed;

	[ObservableProperty]
	private ObservableCollection<EmailDisplayModel> _emails = new();

	[ObservableProperty]
	private bool _isOnline = true;

	[ObservableProperty]
	private bool _isLoading;

	[ObservableProperty]
	private bool _isRefreshing;

	[ObservableProperty]
	private string _statusMessage = "Ready";

	[ObservableProperty]
	private int _totalEmails;

	[ObservableProperty]
	private int _pendingEmails;

	[ObservableProperty]
	private int _sentEmails;

	[ObservableProperty]
	private int _failedEmails;

	[ObservableProperty]
	private EmailDisplayModel? _selectedEmail;

	// ── Circuit breaker surface ──────────────────────────────────────
	// Mirrors IEmailService.IsCircuitOpen/LastQueueError/CircuitOpenedAt so the
	// Email Status page can bind directly. Updated both proactively (in the
	// constructor so the UI reflects state on first render, even if the
	// breaker tripped before this VM was created) and reactively via the
	// CircuitStateChanged event so transitions appear live without polling.

	/// <summary>
	/// True when background email delivery is paused due to repeated failures.
	/// Bind to a warning banner or similar on the Email Status page.
	/// </summary>
	[ObservableProperty]
	private bool _isCircuitOpen;

	/// <summary>
	/// The SMTP error that tripped the breaker, surfaced to the user so they
	/// know what to fix (typically an auth failure or DNS miss).
	/// </summary>
	[ObservableProperty]
	private string? _circuitLastError;

	/// <summary>
	/// Human-readable timestamp for when the breaker opened. Empty when closed.
	/// </summary>
	[ObservableProperty]
	private string _circuitOpenedText = string.Empty;

	/// <summary>
	/// True while a reachability probe is in flight. Bind to a spinner /
	/// disable state on the Test Connection button to prevent double-taps.
	/// </summary>
	[ObservableProperty]
	private bool _isTestingConnection;

	public EmailStatusViewModel(IEmailService emailService, IConfigurationService configService)
	{
		_emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
		_configService = configService ?? throw new ArgumentNullException(nameof(configService));

		// Subscribe to mail service events
		_emailService.EmailSent += OnEmailSent;
		_emailService.EmailFailed += OnEmailFailed;
		_emailService.QueueProcessed += OnQueueProcessed;
		_emailService.CircuitStateChanged += OnCircuitStateChanged;

		// The breaker may already be open when we wire up — mirror current
		// state so the UI reflects reality on first render rather than only
		// after the next transition.
		ApplyCircuitState(
			_emailService.IsCircuitOpen,
			_emailService.ConsecutiveQueueFailures,
			_emailService.LastQueueError,
			_emailService.CircuitOpenedAt);

		// Setup auto-refresh timer (every 30 seconds)
		_refreshTimer = new Timer(async _ => await RefreshEmailsAsync(), null,
			TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

		// Initial load
		Task.Run(async () => await LoadEmailsAsync()).SafeFireAndForget("InitialEmailLoad");
	}

	[RelayCommand]
	private async Task LoadEmailsAsync()
	{
		try
		{
			IsLoading = true;
			StatusMessage = "Loading emails...";

			var emails = await _emailService.GetQueuedEmailsAsync();

			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				Emails.Clear();
				foreach (var email in emails)
				{
					Emails.Add(new EmailDisplayModel(email));
				}

				UpdateStatistics();
			});

			StatusMessage = $"Loaded {emails.Count} emails";
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error loading emails");
			StatusMessage = $"Error loading emails: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	[RelayCommand]
	private async Task RefreshEmailsAsync()
	{
		if (IsLoading || IsRefreshing) return;

		try
		{
			IsRefreshing = true;
			await LoadEmailsAsync();
		}
		finally
		{
			IsRefreshing = false;
		}
	}

	[RelayCommand]
	private async Task RetryAllEmailsAsync()
	{
		try
		{
			IsLoading = true;
			StatusMessage = "Resetting all email retry counts...";

			await _emailService.ResetRetryCountAsync();
			StatusMessage = "All email retry counts reset. Processing queue...";

			// Trigger queue processing
			await _emailService.ProcessQueueAsync();

			// Refresh the display
			await LoadEmailsAsync();

			StatusMessage = "All emails queued for retry";
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error retrying all emails");
			StatusMessage = $"Error retrying all emails: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	[RelayCommand]
	private async Task RetryFailedEmailsAsync()
	{
		try
		{
			IsLoading = true;
			StatusMessage = "Retrying failed emails...";

			await _emailService.RetryFailedEmailsAsync();
			StatusMessage = "Failed emails reset to pending. Processing queue...";

			// Trigger queue processing
			await _emailService.ProcessQueueAsync();

			// Refresh the display
			await LoadEmailsAsync();

			StatusMessage = "Failed emails queued for retry";
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error retrying failed emails");
			StatusMessage = $"Error retrying failed emails: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	[RelayCommand]
	private async Task RetryEmailAsync(EmailDisplayModel emailModel)
	{
		if (emailModel?.Id == null) return;

		try
		{
			StatusMessage = $"Retrying email to {emailModel.To}...";

			await _emailService.ResetRetryCountAsync(emailModel.Id.Value);

			// Trigger queue processing
			await _emailService.ProcessQueueAsync();

			// Refresh the display
			await LoadEmailsAsync();

			StatusMessage = $"Email to {emailModel.To} queued for retry";
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error retrying email {EmailId}", emailModel.Id);
			StatusMessage = $"Error retrying email: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task DeleteEmailAsync(EmailDisplayModel emailModel)
	{
		if (emailModel?.Id == null) return;

		try
		{
			// Note: You'll need to add a delete method to your mail service
			// For now, we'll just remove it from the display and log it
			Logger.Information("Delete email {EmailId} requested", emailModel.Id);

			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				Emails.Remove(emailModel);
				UpdateStatistics();
			});

			StatusMessage = $"Email to {emailModel.To} removed from display";
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error deleting email {EmailId}", emailModel.Id);
			StatusMessage = $"Error deleting email: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task ToggleOfflineModeAsync()
	{
		try
		{
			if (_emailService.IsOfflineMode)
			{
				_emailService.DisableOfflineMode();
				IsOnline = true;
				StatusMessage = "Online mode enabled";
			}
			else
			{
				_emailService.EnableOfflineMode();
				IsOnline = false;
				StatusMessage = "Offline mode enabled";
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error toggling offline mode");
			StatusMessage = $"Error toggling offline mode: {ex.Message}";
		}
	}

	[RelayCommand]
	private async Task ProcessQueueAsync()
	{
		try
		{
			IsLoading = true;
			StatusMessage = "Processing email queue...";

			var result = await _emailService.ProcessQueueAsync();

			if (result)
			{
				StatusMessage = "Queue processing completed";
			}
			else
			{
				StatusMessage = "Queue processing failed or skipped";
			}

			// Refresh the display
			await LoadEmailsAsync();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error processing queue");
			StatusMessage = $"Error processing queue: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	[RelayCommand]
	private async Task ClearSentEmailsAsync()
	{
		try
		{
			IsLoading = true;
			StatusMessage = "Clearing sent emails...";

			await _emailService.ClearSentEmailsAsync();

			// Refresh the display
			await LoadEmailsAsync();

			StatusMessage = "Sent emails cleared";
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error clearing sent emails");
			StatusMessage = $"Error clearing sent emails: {ex.Message}";
		}
		finally
		{
			IsLoading = false;
		}
	}

	private void UpdateStatistics()
	{
		TotalEmails = Emails.Count;
		PendingEmails = Emails.Count(e => e.Status == EmailStatus.Pending);
		SentEmails = Emails.Count(e => e.Status == EmailStatus.Sent);
		FailedEmails = Emails.Count(e => e.Status == EmailStatus.Failed);
		IsOnline = !_emailService.IsOfflineMode;
	}

	private void OnEmailSent(object? sender, EmailSentEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			var email = Emails.FirstOrDefault(em => em.Id == e.Email.Id);
			if (email != null)
			{
				email.UpdateFromQueuedEmail(e.Email);
				UpdateStatistics();
			}

			StatusMessage = $"Email sent to {e.Email.To}";
		});
	}

	private void OnEmailFailed(object? sender, EmailFailedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			var email = Emails.FirstOrDefault(em => em.Id == e.Email.Id);
			if (email != null)
			{
				email.UpdateFromQueuedEmail(e.Email);
				UpdateStatistics();
			}

			StatusMessage = $"Email failed to {e.Email.To}: {e.Error}";
		});
	}

	private void OnQueueProcessed(object? sender, QueueProcessedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			StatusMessage = $"Queue processed: {e.ProcessedCount} sent, {e.FailedCount} failed, {e.RemainingCount} remaining";
			await RefreshEmailsAsync();
		});
	}

	/// <summary>
	/// Fires from the mail service background thread when the breaker opens
	/// or closes. Hop to the UI thread before touching observable properties.
	/// </summary>
	private void OnCircuitStateChanged(object? sender, CircuitStateChangedEventArgs e)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			ApplyCircuitState(e.IsOpen, e.ConsecutiveFailures, e.LastError, e.OpenedAt);

			// Nudge the status line so the user gets an inline hint alongside
			// the banner. Don't overwrite a more-specific status they may be
			// looking at unless this is a transition we want to highlight.
			StatusMessage = e.IsOpen
				? $"Background delivery paused after {e.ConsecutiveFailures} failures. Tap Resume to retry."
				: "Background delivery resumed.";
		});
	}

	/// <summary>
	/// Writes the circuit state into observable properties. Safe to call from
	/// the UI thread only (caller is responsible for marshalling).
	/// </summary>
	private void ApplyCircuitState(bool isOpen, int consecutiveFailures, string? lastError, DateTime? openedAt)
	{
		IsCircuitOpen = isOpen;
		CircuitLastError = lastError;
		CircuitOpenedText = openedAt.HasValue
			? $"Paused at {openedAt.Value.ToLocalTime():HH:mm}"
			: string.Empty;
	}

	/// <summary>
	/// Manually resume background email delivery after the circuit breaker
	/// has tripped. Calls the mail service's reset and immediately attempts
	/// to drain the pending queue so the user can see whether the underlying
	/// issue has been resolved without waiting for the next 5-minute tick.
	/// </summary>
	[RelayCommand]
	private async Task ResetCircuitBreakerAsync()
	{
		try
		{
			StatusMessage = "Resuming background email delivery...";

			// Reset first — this flips _circuitOpen to false and fires
			// CircuitStateChanged, which updates our observable state.
			_emailService.ResetCircuitBreaker();

			// Now try to send. If the underlying issue is resolved, pending
			// emails drain; if not, the breaker will re-trip after
			// CircuitBreakerThreshold more failures from the background timer.
			// Either way the user gets immediate feedback rather than waiting
			// five minutes.
			await _emailService.ProcessQueueAsync();

			// Refresh the list so status changes for individual emails
			// (Pending → Sent / Failed) show up without another tap.
			await LoadEmailsAsync();
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error resetting circuit breaker");
			StatusMessage = $"Error resuming delivery: {ex.Message}";
		}
	}

	/// <summary>
	/// Runs a lightweight SMTP reachability probe using the current saved
	/// config — connects and authenticates without sending a test email.
	/// Lets the user distinguish "my phone is offline" from "my SMTP
	/// credentials are wrong" at a glance, before waiting out three breaker
	/// failures to find out the hard way.
	/// </summary>
	[RelayCommand]
	private async Task TestConnectionAsync()
	{
		if (IsTestingConnection) return;

		try
		{
			IsTestingConnection = true;
			StatusMessage = "Testing SMTP connection...";

			var config = await _configService.LoadSmtpConfigurationAsync();
			if (!config.IsValid())
			{
				StatusMessage = "SMTP is not configured — fill it in under Settings first.";
				return;
			}

			var result = await _emailService.TestSmtpReachabilityAsync(config);

			if (result.IsReachable)
			{
				StatusMessage = "✓ SMTP reachable and credentials valid.";
				Logger.Information("SMTP reachability probe OK for {Host}:{Port}", config.Host, config.Port);
			}
			else
			{
				// Prefix with the failure kind so the user sees an actionable
				// hint without having to parse the raw exception message.
				var hint = result.Kind switch
				{
					SmtpReachabilityKind.Auth => "Authentication failed",
					SmtpReachabilityKind.Network => "Network / DNS problem",
					SmtpReachabilityKind.Timeout => "Timed out",
					SmtpReachabilityKind.Tls => "TLS / certificate problem",
					_ => "SMTP unreachable",
				};
				StatusMessage = $"✗ {hint}: {result.Message}";
				Logger.Warning(result.Exception,
					"SMTP reachability probe failed for {Host}:{Port} — {Kind}: {Message}",
					config.Host, config.Port, result.Kind, result.Message);
			}
		}
		catch (Exception ex)
		{
			Logger.Error(ex, "Error during SMTP reachability probe");
			StatusMessage = $"Error testing connection: {ex.Message}";
		}
		finally
		{
			IsTestingConnection = false;
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (!_disposed && disposing)
		{
			_disposed = true;

			_refreshTimer?.Dispose();

			// Unsubscribe from events
			_emailService.EmailSent -= OnEmailSent;
			_emailService.EmailFailed -= OnEmailFailed;
			_emailService.QueueProcessed -= OnQueueProcessed;
			_emailService.CircuitStateChanged -= OnCircuitStateChanged;
		}
		base.Dispose(disposing);
	}
}

// Display model for emails in the UI
public partial class EmailDisplayModel : ObservableObject
{
	[ObservableProperty]
	private int? _id;

	[ObservableProperty]
	private string _to = string.Empty;

	[ObservableProperty]
	private string _subject = string.Empty;

	[ObservableProperty]
	private string _from = string.Empty;

	[ObservableProperty]
	private EmailStatus _status;

	[ObservableProperty]
	private DateTime _createdAt;

	[ObservableProperty]
	private DateTime? _lastAttemptAt;

	[ObservableProperty]
	private int _attemptCount;

	[ObservableProperty]
	private int _maxRetries;

	[ObservableProperty]
	private string? _lastError;

	[ObservableProperty]
	private bool _isHtml;

	public string StatusText => Status.ToString();

	public string StatusColor => Status switch
	{
		EmailStatus.Sent => "Green",
		EmailStatus.Failed => "Red",
		EmailStatus.Pending => "Orange",
		EmailStatus.Sending => "Blue",
		_ => "Gray"
	};

	public string AttemptText => $"{AttemptCount}/{MaxRetries}";

	public string CreatedAtText => CreatedAt.ToString("MM/dd/yyyy HH:mm");

	public string LastAttemptText => LastAttemptAt?.ToString("MM/dd/yyyy HH:mm") ?? "Never";

	public bool HasError => !string.IsNullOrWhiteSpace(LastError);

	public bool CanRetry => Status == EmailStatus.Failed || Status == EmailStatus.Pending;

	public string ShortSubject => Subject.Length > 50 ? Subject[..47] + "..." : Subject;

	public EmailDisplayModel(QueuedEmail queuedEmail)
	{
		UpdateFromQueuedEmail(queuedEmail);
	}

	public void UpdateFromQueuedEmail(QueuedEmail queuedEmail)
	{
		Id = queuedEmail.Id;
		To = queuedEmail.To;
		Subject = queuedEmail.Subject;
		From = queuedEmail.From;
		Status = queuedEmail.Status;
		CreatedAt = queuedEmail.CreatedAt;
		LastAttemptAt = queuedEmail.LastAttemptAt;
		AttemptCount = queuedEmail.AttemptCount;
		MaxRetries = queuedEmail.MaxRetries;
		LastError = queuedEmail.LastError;
		IsHtml = queuedEmail.IsHtml;
	}
}