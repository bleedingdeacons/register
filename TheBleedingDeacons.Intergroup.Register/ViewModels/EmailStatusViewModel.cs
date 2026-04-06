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

	private readonly IMailService _mailService;

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

	public EmailStatusViewModel(IMailService mailService)
	{
		_mailService = mailService ?? throw new ArgumentNullException(nameof(mailService));

		// Subscribe to mail service events
		_mailService.EmailSent += OnEmailSent;
		_mailService.EmailFailed += OnEmailFailed;
		_mailService.QueueProcessed += OnQueueProcessed;

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

			var emails = await _mailService.GetQueuedEmailsAsync();

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

			await _mailService.ResetRetryCountAsync();
			StatusMessage = "All email retry counts reset. Processing queue...";

			// Trigger queue processing
			await _mailService.ProcessQueueAsync();

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

			await _mailService.RetryFailedEmailsAsync();
			StatusMessage = "Failed emails reset to pending. Processing queue...";

			// Trigger queue processing
			await _mailService.ProcessQueueAsync();

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

			await _mailService.ResetRetryCountAsync(emailModel.Id.Value);

			// Trigger queue processing
			await _mailService.ProcessQueueAsync();

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
			if (_mailService.IsOfflineMode)
			{
				_mailService.DisableOfflineMode();
				IsOnline = true;
				StatusMessage = "Online mode enabled";
			}
			else
			{
				_mailService.EnableOfflineMode();
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

			var result = await _mailService.ProcessQueueAsync();

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

			await _mailService.ClearSentEmailsAsync();

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
		IsOnline = !_mailService.IsOfflineMode;
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

	protected override void Dispose(bool disposing)
	{
		if (!_disposed && disposing)
		{
			_disposed = true;

			_refreshTimer?.Dispose();

			// Unsubscribe from events
			_mailService.EmailSent -= OnEmailSent;
			_mailService.EmailFailed -= OnEmailFailed;
			_mailService.QueueProcessed -= OnQueueProcessed;
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