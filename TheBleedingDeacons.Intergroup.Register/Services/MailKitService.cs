using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using MimeKit.Utils;
using Serilog;
using System.Diagnostics;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	public class MailKitService : IMailService, IDisposable
	{
		private static readonly ILogger Logger = AppLogger.ForContext<MailKitService>();

		#region Private Fields        
		private readonly IDbContextFactory<MailDbContext> _dbContextFactory;
		private readonly SemaphoreSlim _queueSemaphore = new(1, 1);
		private readonly SemaphoreSlim _configSemaphore = new(1, 1);
		private readonly Timer _backgroundTimer;

		// Configuration
		private string _smtpHost;
		private int _smtpPort;
		private string _username;
		private string _password;
		private bool _enableSsl;
		private int _timeoutSeconds;
		private int _maxRetries;

		private bool _isOfflineMode;
		private bool _disposed;

		// Circuit breaker — pauses the background timer after repeated failures
		// so a persistent SMTP misconfiguration doesn't spin forever.
		private const int CircuitBreakerThreshold = 3;
		private int _consecutiveQueueFailures;
		private bool _circuitOpen;
		private string? _lastQueueError;
		private DateTime? _circuitOpenedAt;

		#endregion

		#region Public Properties

		public bool IsOfflineMode
		{
			get => _isOfflineMode;
			set => _isOfflineMode = value;
		}

		public int MaxRetries
		{
			get => _maxRetries;
			set => _maxRetries = Math.Max(1, value); // Ensure at least 1 retry
		}

		/// <summary>
		/// True when the background queue has been paused due to repeated failures.
		/// Check <see cref="LastQueueError"/> for the reason. Call
		/// <see cref="ResetCircuitBreaker"/> to resume processing.
		/// </summary>
		public bool IsCircuitOpen => _circuitOpen;

		/// <summary>
		/// Number of consecutive queue processing failures since the last success.
		/// </summary>
		public int ConsecutiveQueueFailures => _consecutiveQueueFailures;

		/// <summary>
		/// The error message from the most recent queue processing failure,
		/// or null if the last run succeeded.
		/// </summary>
		public string? LastQueueError => _lastQueueError;

		/// <summary>
		/// UTC timestamp when the circuit breaker opened, or null if it's closed.
		/// </summary>
		public DateTime? CircuitOpenedAt => _circuitOpenedAt;

		#endregion

		#region Events

		public event EventHandler<EmailSentEventArgs> EmailSent;
		public event EventHandler<EmailFailedEventArgs> EmailFailed;
		public event EventHandler<QueueProcessedEventArgs> QueueProcessed;

		#endregion

		#region Constructor

		public MailKitService(IDbContextFactory<MailDbContext> dbContextFactory,
			string smtpHost, int smtpPort, string username, string password, bool enableSsl = true,
			int timeoutSeconds = 30, int maxRetries = 10)
		{
			_dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));

			_smtpHost = smtpHost ?? throw new ArgumentNullException(nameof(smtpHost));
			_smtpPort = smtpPort;
			_username = username ?? throw new ArgumentNullException(nameof(username));
			_password = password ?? throw new ArgumentNullException(nameof(password));
			_enableSsl = enableSsl;
			_timeoutSeconds = timeoutSeconds;
			_maxRetries = Math.Max(1, maxRetries); // Ensure at least 1 retry

			// Start background processing timer
			_backgroundTimer = new Timer(async _ => await ProcessQueueInBackground(),
				null, ServiceConstants.EmailTimerInitialDelay, ServiceConstants.EmailTimerInterval);

			Logger.Information("MailKitService initialized with host: {Host}:{Port}, SSL: {EnableSsl}, MaxRetries: {MaxRetries}",
				_smtpHost, _smtpPort, _enableSsl, _maxRetries);
		}

		#endregion

		#region Configuration Methods

		public async Task UpdateConfigurationAsync(SmtpConfiguration config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config));

			await _configSemaphore.WaitAsync();
			try
			{
				_smtpHost = config.Host;
				_smtpPort = config.Port;
				_username = config.Username;
				_password = config.Password;
				_enableSsl = config.EnableSsl;
				_timeoutSeconds = config.TimeoutSeconds;
				_maxRetries = Math.Max(1, config.MaxRetries); // Ensure at least 1 retry

				Logger.Information("MailKit service configuration updated - MaxRetries: {MaxRetries}", _maxRetries);

				// New config deserves a fresh attempt — reset the circuit breaker.
				ResetCircuitBreaker();
			}
			finally
			{
				_configSemaphore.Release();
			}
		}

		#endregion

		#region Core Email Sending Methods

		public async Task<bool> SendEmailAsync(string to, string subject, string body,
			bool isHtml = false, string? cc = null, string? bcc = null)
		{
			if (string.IsNullOrWhiteSpace(to))
				throw new ArgumentException("Recipient email address is required", nameof(to));

			if (string.IsNullOrWhiteSpace(subject))
				throw new ArgumentException("Email subject is required", nameof(subject));

			if (string.IsNullOrWhiteSpace(body))
				throw new ArgumentException("Email body is required", nameof(body));

			// Check if we should queue instead of sending immediately
			if (_isOfflineMode || !await IsNetworkAvailableAsync())
			{
				await QueueEmailAsync(to, subject, body, isHtml, cc, bcc);
				Logger.Information("Email queued due to offline mode or network unavailability: {To}", to);
				return false;
			}

			var email = new QueuedEmail
			{
				To = to,
				Subject = subject,
				Body = body,
				From = _username,
				Cc = cc,
				Bcc = bcc,
				IsHtml = isHtml,
				CreatedAt = DateTime.UtcNow,
				Status = EmailStatus.Sending,
				MaxRetries = _maxRetries
			};

			return await TrySendEmailWithMailKitAsync(email);
		}

		public async Task QueueEmailAsync(string to, string subject, string body,
			bool isHtml = false, string? cc = null, string? bcc = null)
		{
			if (string.IsNullOrWhiteSpace(to))
				throw new ArgumentException("Recipient email address is required", nameof(to));

			if (string.IsNullOrWhiteSpace(subject))
				throw new ArgumentException("Email subject is required", nameof(subject));

			if (string.IsNullOrWhiteSpace(body))
				throw new ArgumentException("Email body is required", nameof(body));

			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();

				var email = new QueuedEmail
				{
					To = to,
					Subject = subject,
					Body = body,
					From = _username,
					Cc = cc,
					Bcc = bcc,
					IsHtml = isHtml,
					CreatedAt = DateTime.UtcNow,
					Status = EmailStatus.Pending,
					MaxRetries = _maxRetries
				};

				context.QueuedEmails.Add(email);
				await context.SaveChangesAsync();

				Logger.Information("Email queued successfully for {To}: {Subject} (MaxRetries: {MaxRetries})",
					to, subject, _maxRetries);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to queue email for {To}: {Subject}", to, subject);
				throw;
			}
		}

		private async Task<bool> TrySendEmailWithMailKitAsync(QueuedEmail queuedEmail)
		{
			if (queuedEmail == null)
				throw new ArgumentNullException(nameof(queuedEmail));

			var stopwatch = Stopwatch.StartNew();
			var operationId = Guid.NewGuid().ToString("N")[..8];

			try
			{
				Logger.Debug("[{OperationId}] Creating MIME message for {To}", operationId, queuedEmail.To);
				var message = await CreateMimeMessageAsync(queuedEmail);

				Logger.Debug("[{OperationId}] Creating SMTP client", operationId);
				using var client = new SmtpClient();

				// Configure client settings
				client.Timeout = _timeoutSeconds * 1000; // MailKit timeout in milliseconds
				client.CheckCertificateRevocation = true;

				// Create cancellation token for the entire operation
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds + 10));

				// Get current configuration safely
				var (host, port, username, password, enableSsl) = await GetCurrentConfigAsync();

				// Determine SSL options
				var secureSocketOptions = ResolveSecureSocketOptions(enableSsl, port);

				Logger.Debug("[{OperationId}] Connecting to {Host}:{Port} with SSL={EnableSsl} ({SSLOptions})",
					operationId, host, port, enableSsl, secureSocketOptions);

				// Connect to server
				await client.ConnectAsync(host, port, secureSocketOptions, cts.Token);

				Logger.Debug("[{OperationId}] Connected successfully, authenticating as {Username}",
					operationId, username);

				// Authenticate
				await client.AuthenticateAsync(username, password, cts.Token);

				Logger.Debug("[{OperationId}] Authenticated successfully, sending message", operationId);

				// Send the message
				await client.SendAsync(message, cts.Token);

				Logger.Debug("[{OperationId}] Message sent, disconnecting", operationId);

				// Disconnect gracefully
				await client.DisconnectAsync(true, cts.Token);

				stopwatch.Stop();

				// Update email status
				queuedEmail.Status = EmailStatus.Sent;
				queuedEmail.LastAttemptAt = DateTime.UtcNow;

				await UpdateEmailInDatabase(queuedEmail);

				// Raise success event
				EmailSent?.Invoke(this, new EmailSentEventArgs
				{
					Email = queuedEmail,
					SentAt = DateTime.UtcNow
				});

				Logger.Information("[{OperationId}] Email sent successfully to {To} in {ElapsedMs}ms: {Subject}",
					operationId, queuedEmail.To, stopwatch.ElapsedMilliseconds, queuedEmail.Subject);

				return true;
			}
			catch (OperationCanceledException)
			{
				stopwatch.Stop();
				var timeoutEx = new TimeoutException($"Email send operation timed out after {_timeoutSeconds + 10} seconds");
				Logger.Warning("[{OperationId}] Email send TIMED OUT after {ElapsedMs}ms to {To}",
					operationId, stopwatch.ElapsedMilliseconds, queuedEmail.To);

				return await HandleEmailFailure(queuedEmail, timeoutEx);
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				Logger.Error(ex, "[{OperationId}] Email send FAILED after {ElapsedMs}ms to {To}: {Error}",
					operationId, stopwatch.ElapsedMilliseconds, queuedEmail.To, ex.Message);

				return await HandleEmailFailure(queuedEmail, ex);
			}
		}

		private async Task<(string host, int port, string username, string password, bool enableSsl)> GetCurrentConfigAsync()
		{
			await _configSemaphore.WaitAsync();
			try
			{
				return (_smtpHost, _smtpPort, _username, _password, _enableSsl);
			}
			finally
			{
				_configSemaphore.Release();
			}
		}

		/// <summary>
		/// Resolves MailKit's SecureSocketOptions from the SSL flag and port number.
		/// Extracted from TrySendEmailWithMailKitAsync for reuse in batch connection setup.
		/// </summary>
		private static SecureSocketOptions ResolveSecureSocketOptions(bool enableSsl, int port)
		{
			if (!enableSsl) return SecureSocketOptions.None;
			if (port == 465) return SecureSocketOptions.SslOnConnect;
			if (port is 587 or 25) return SecureSocketOptions.StartTls;
			return SecureSocketOptions.Auto;
		}

		/// <summary>
		/// Creates, connects, and authenticates a new SmtpClient.
		/// Used by both single-send and batch-send paths.
		/// </summary>
		private async Task<SmtpClient> ConnectSmtpClientAsync(
			string host, int port, string username, string password,
			SecureSocketOptions secureSocketOptions)
		{
			var client = new SmtpClient
			{
				Timeout = _timeoutSeconds * 1000,
				CheckCertificateRevocation = true
			};

			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds + 10));
			await client.ConnectAsync(host, port, secureSocketOptions, cts.Token);
			await client.AuthenticateAsync(username, password, cts.Token);

			Logger.Debug("SMTP client connected and authenticated to {Host}:{Port}", host, port);
			return client;
		}

		/// <summary>
		/// Sends a single email using an already-connected SmtpClient (for batch processing).
		/// Falls back to the per-message TrySendEmailWithMailKitAsync on connection-level errors.
		/// </summary>
		private async Task<bool> TrySendEmailWithSharedClientAsync(SmtpClient client, QueuedEmail queuedEmail)
		{
			var operationId = Guid.NewGuid().ToString("N")[..8];
			var stopwatch = Stopwatch.StartNew();

			try
			{
				var message = await CreateMimeMessageAsync(queuedEmail);
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds + 10));
				await client.SendAsync(message, cts.Token);

				stopwatch.Stop();

				queuedEmail.Status = EmailStatus.Sent;
				queuedEmail.LastAttemptAt = DateTime.UtcNow;
				await UpdateEmailInDatabase(queuedEmail);

				EmailSent?.Invoke(this, new EmailSentEventArgs
				{
					Email = queuedEmail,
					SentAt = DateTime.UtcNow
				});

				Logger.Information("[{OperationId}] Email sent via shared connection in {ElapsedMs}ms to {To}",
					operationId, stopwatch.ElapsedMilliseconds, queuedEmail.To);

				return true;
			}
			catch (Exception ex)
			{
				stopwatch.Stop();
				Logger.Error(ex, "[{OperationId}] Email send FAILED via shared connection after {ElapsedMs}ms to {To}",
					operationId, stopwatch.ElapsedMilliseconds, queuedEmail.To);

				return await HandleEmailFailure(queuedEmail, ex);
			}
		}

		private async Task<MimeMessage> CreateMimeMessageAsync(QueuedEmail queuedEmail)
		{
			return await Task.Run(() =>
			{
				var message = new MimeMessage();

				// Set From address
				message.From.Add(new MailboxAddress("", queuedEmail.From));

				// Set To address
				message.To.Add(new MailboxAddress("", queuedEmail.To));

				// Set subject
				message.Subject = queuedEmail.Subject;

				// Add CC recipients if specified
				if (!string.IsNullOrWhiteSpace(queuedEmail.Cc))
				{
					var ccAddresses = queuedEmail.Cc.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(addr => addr.Trim())
						.Where(addr => !string.IsNullOrWhiteSpace(addr));

					foreach (var ccAddress in ccAddresses)
					{
						message.Cc.Add(new MailboxAddress("", ccAddress));
					}
				}

				// Add BCC recipients if specified
				if (!string.IsNullOrWhiteSpace(queuedEmail.Bcc))
				{
					var bccAddresses = queuedEmail.Bcc.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
						.Select(addr => addr.Trim())
						.Where(addr => !string.IsNullOrWhiteSpace(addr));

					foreach (var bccAddress in bccAddresses)
					{
						message.Bcc.Add(new MailboxAddress("", bccAddress));
					}
				}

				// Create body
				var bodyBuilder = new BodyBuilder();

				if (queuedEmail.IsHtml)
				{
					bodyBuilder.HtmlBody = queuedEmail.Body;
				}
				else
				{
					bodyBuilder.TextBody = queuedEmail.Body;
				}

				message.Body = bodyBuilder.ToMessageBody();

				// Set additional headers
				message.MessageId = MimeUtils.GenerateMessageId();
				message.Date = DateTimeOffset.UtcNow;

				return message;
			});
		}

		private async Task<bool> HandleEmailFailure(QueuedEmail queuedEmail, Exception ex)
		{
			queuedEmail.LastError = ex.Message;
			queuedEmail.LastAttemptAt = DateTime.UtcNow;
			queuedEmail.AttemptCount++;

			bool isRetryable = IsRetryableException(ex);

			if (queuedEmail.AttemptCount >= queuedEmail.MaxRetries)
			{
				queuedEmail.Status = EmailStatus.Failed;
				Logger.Error(ex, "Email failed permanently after {MaxRetries} attempts to {To}: {Subject}",
					queuedEmail.MaxRetries, queuedEmail.To, queuedEmail.Subject);
			}
			else
			{
				queuedEmail.Status = EmailStatus.Pending;
				Logger.Warning(ex, "Email attempt {AttemptCount}/{MaxRetries} failed to {To}: {Subject}. Will retry.",
					queuedEmail.AttemptCount, queuedEmail.MaxRetries, queuedEmail.To, queuedEmail.Subject);
			}

			await UpdateEmailInDatabase(queuedEmail);

			// Raise failure event
			EmailFailed?.Invoke(this, new EmailFailedEventArgs
			{
				Email = queuedEmail,
				Error = ex.Message,
				FailedAt = DateTime.UtcNow,
				IsRetryable = isRetryable
			});

			return false;
		}

		private static bool IsRetryableException(Exception ex)
		{
			// Simplified MailKit exception handling using only confirmed types
			return ex switch
			{
				// SMTP specific exceptions - only use confirmed status codes
				MailKit.Net.Smtp.SmtpCommandException smtp => smtp.StatusCode switch
				{
					MailKit.Net.Smtp.SmtpStatusCode.MailboxBusy => true,
					MailKit.Net.Smtp.SmtpStatusCode.TransactionFailed => true,
					MailKit.Net.Smtp.SmtpStatusCode.InsufficientStorage => true,
					MailKit.Net.Smtp.SmtpStatusCode.ExceededStorageAllocation => true,
					_ => false // Default to non-retryable for unknown SMTP errors
				},

				// Protocol exceptions (network/connection issues) - these are usually retryable
				MailKit.Net.Smtp.SmtpProtocolException => true,

				// Service state exceptions
				MailKit.ServiceNotConnectedException => true, // Connection lost - retryable
				MailKit.ServiceNotAuthenticatedException => false, // Auth failure - don't retry

				// General timeout and cancellation - retryable
				TimeoutException => true,
				OperationCanceledException => true,

				// I/O exceptions - usually network issues, retryable
				System.IO.IOException => true,

				// Socket exceptions - handle specific cases
				System.Net.Sockets.SocketException socket => socket.SocketErrorCode switch
				{
					System.Net.Sockets.SocketError.HostNotFound => false, // DNS failure - permanent
					System.Net.Sockets.SocketError.ConnectionRefused => false, // Server not accepting - permanent
					System.Net.Sockets.SocketError.TimedOut => true, // Timeout - retryable
					System.Net.Sockets.SocketError.NetworkUnreachable => true, // Network issue - retryable
					System.Net.Sockets.SocketError.HostUnreachable => true, // Routing issue - retryable
					_ => true // Default to retryable for other socket errors
				},

				// SSL/TLS authentication issues - usually permanent config problems
				System.Security.Authentication.AuthenticationException => false,

				// Any other exception - don't retry by default
				_ => false
			};
		}

		private async Task UpdateEmailInDatabase(QueuedEmail queuedEmail)
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();

				if (queuedEmail.Id == 0)
				{
					context.QueuedEmails.Add(queuedEmail);
				}
				else
				{
					context.QueuedEmails.Update(queuedEmail);
				}

				await context.SaveChangesAsync();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to update email in database: {EmailId}", queuedEmail.Id);
				throw;
			}
		}

		#endregion

		#region Queue Management Methods

		public async Task<bool> ProcessQueueAsync()
		{
			if (_isOfflineMode)
			{
				Logger.Debug("Skipping queue processing - offline mode enabled");
				return false;
			}

			if (!await IsNetworkAvailableAsync())
			{
				Logger.Debug("Skipping queue processing - no network connection");
				return false;
			}

			if (!await _queueSemaphore.WaitAsync(TimeSpan.FromSeconds(5)))
			{
				Logger.Debug("Skipping queue processing - another process is already running");
				return false;
			}

			var stopwatch = Stopwatch.StartNew();

			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();

				var pendingEmails = await context.QueuedEmails
					.Where(e => e.Status == EmailStatus.Pending)
					.OrderBy(e => e.AttemptCount)
					.ThenBy(e => e.CreatedAt)
					.Take(ServiceConstants.EmailBatchSize) // Process emails in controlled batches
					.ToListAsync();

				if (!pendingEmails.Any())
				{
					Logger.Debug("No pending emails to process");
					return true;
				}

				int processedCount = 0;
				int failedCount = 0;

				Logger.Information("Processing {EmailCount} pending emails with MailKit (MaxRetries: {MaxRetries})",
					pendingEmails.Count, _maxRetries);

				// ARCH-003: Reuse a single SMTP connection for the entire batch.
				// This avoids N connect/auth handshakes (each ~2s) for N emails.
				var (host, port, username, password, enableSsl) = await GetCurrentConfigAsync();
				var secureSocketOptions = ResolveSecureSocketOptions(enableSsl, port);
				SmtpClient? sharedClient = null;

				try
				{
					sharedClient = await ConnectSmtpClientAsync(host, port, username, password, secureSocketOptions);

					foreach (var email in pendingEmails)
					{
						try
						{
							// Reconnect if the shared client was disconnected by a previous failure
							if (sharedClient == null || !sharedClient.IsConnected)
							{
								sharedClient?.Dispose();
								sharedClient = await ConnectSmtpClientAsync(host, port, username, password, secureSocketOptions);
							}

							if (await TrySendEmailWithSharedClientAsync(sharedClient, email))
							{
								processedCount++;
							}
							else
							{
								failedCount++;
							}

							// Small delay between sends
							await Task.Delay(ServiceConstants.EmailInterSendDelayMs);

							// Check if we should stop processing
							if (_isOfflineMode || !await IsNetworkAvailableAsync())
							{
								Logger.Information("Queue processing interrupted - offline mode or network unavailable");
								break;
							}
						}
						catch (Exception ex) when (ex is MailKit.ServiceNotConnectedException or MailKit.ServiceNotAuthenticatedException)
						{
							Logger.Warning(ex, "SMTP connection lost during batch, reconnecting for email {EmailId}", email.Id);
							sharedClient?.Dispose();
							sharedClient = null;
							failedCount++;
						}
						catch (Exception ex)
						{
							Logger.Error(ex, "Unexpected error processing email {EmailId}", email.Id);
							failedCount++;
						}
					}
				}
				finally
				{
					if (sharedClient is { IsConnected: true })
					{
						try { await sharedClient.DisconnectAsync(true); }
						catch (Exception ex) { Logger.Debug(ex, "Error disconnecting shared SMTP client"); }
					}
					sharedClient?.Dispose();
				}

				stopwatch.Stop();

				var remainingCount = await context.QueuedEmails
					.CountAsync(e => e.Status == EmailStatus.Pending);

				QueueProcessed?.Invoke(this, new QueueProcessedEventArgs
				{
					ProcessedCount = processedCount,
					FailedCount = failedCount,
					RemainingCount = remainingCount,
					ProcessedAt = DateTime.UtcNow,
					ProcessingTime = stopwatch.Elapsed
				});

				Logger.Information("MailKit queue processing completed in {ElapsedMs}ms: {ProcessedCount} sent, {FailedCount} failed, {RemainingCount} remaining",
					stopwatch.ElapsedMilliseconds, processedCount, failedCount, remainingCount);

				return true;
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Error during MailKit queue processing");
				return false;
			}
			finally
			{
				_queueSemaphore.Release();
			}
		}

		private async Task ProcessQueueInBackground()
		{
			if (_circuitOpen)
			{
				Logger.Debug(
					"Skipping background queue processing — circuit breaker open since {OpenedAt}: {Error}",
					_circuitOpenedAt, _lastQueueError);
				return;
			}

			try
			{
				var success = await ProcessQueueAsync();

				if (success)
				{
					// Reset on any successful run (even if some individual emails failed —
					// ProcessQueueAsync returns true when the SMTP connection itself worked).
					if (_consecutiveQueueFailures > 0)
					{
						Logger.Information(
							"Queue processing succeeded — resetting failure counter (was {Count})",
							_consecutiveQueueFailures);
					}
					_consecutiveQueueFailures = 0;
					_lastQueueError = null;
				}
				else
				{
					RecordQueueFailure("Queue processing returned false (offline, no network, or semaphore contention)");
				}
			}
			catch (Exception ex)
			{
				RecordQueueFailure(ex.Message);
				Logger.Error(ex, "Error in background queue processing (failure {Count}/{Threshold})",
					_consecutiveQueueFailures, CircuitBreakerThreshold);
			}
		}

		private void RecordQueueFailure(string error)
		{
			_consecutiveQueueFailures++;
			_lastQueueError = error;

			if (_consecutiveQueueFailures >= CircuitBreakerThreshold && !_circuitOpen)
			{
				_circuitOpen = true;
				_circuitOpenedAt = DateTime.UtcNow;

				Logger.Warning(
					"Circuit breaker OPEN — background email processing paused after {Count} consecutive failures. " +
					"Last error: {Error}. Call ResetCircuitBreaker() or update SMTP settings to resume.",
					_consecutiveQueueFailures, _lastQueueError);
			}
		}

		/// <summary>
		/// Resets the circuit breaker, clears the failure counter, and resumes
		/// background queue processing on the next timer tick. Call this after
		/// updating SMTP configuration or resolving the underlying issue.
		/// </summary>
		public void ResetCircuitBreaker()
		{
			_circuitOpen = false;
			_circuitOpenedAt = null;
			_consecutiveQueueFailures = 0;
			_lastQueueError = null;

			Logger.Information("Circuit breaker reset — background email processing will resume");
		}

		// All other queue management methods remain the same as the original implementation
		public async Task<List<QueuedEmail>> GetQueuedEmailsAsync()
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();
				return await context.QueuedEmails
					.OrderByDescending(e => e.CreatedAt)
					.ToListAsync();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to get queued emails");
				throw;
			}
		}

		public async Task<List<QueuedEmail>> GetQueuedEmailsByStatusAsync(EmailStatus status)
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();
				return await context.QueuedEmails
					.Where(e => e.Status == status)
					.OrderByDescending(e => e.CreatedAt)
					.ToListAsync();
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to get queued emails by status {Status}", status);
				throw;
			}
		}

		public async Task<int> GetQueueCountAsync()
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();
				return await context.QueuedEmails
					.CountAsync(e => e.Status == EmailStatus.Pending || e.Status == EmailStatus.Failed);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to get queue count");
				throw;
			}
		}

		public async Task ClearQueueAsync()
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();
				var deletedCount = await context.QueuedEmails.ExecuteDeleteAsync();

				Logger.Information("Email queue cleared - {DeletedCount} emails removed", deletedCount);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to clear email queue");
				throw;
			}
		}

		public async Task ClearSentEmailsAsync()
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();
				var deletedCount = await context.QueuedEmails
					.Where(e => e.Status == EmailStatus.Sent)
					.ExecuteDeleteAsync();

				Logger.Information("Sent emails cleared from queue - {DeletedCount} emails removed", deletedCount);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to clear sent emails");
				throw;
			}
		}

		public async Task RetryFailedEmailsAsync()
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();

				var failedEmails = await context.QueuedEmails
					.Where(e => e.Status == EmailStatus.Failed)
					.ToListAsync();

				foreach (var email in failedEmails)
				{
					email.Status = EmailStatus.Pending;
					email.AttemptCount = 0;
					email.LastError = null;
					email.LastAttemptAt = null;
				}

				await context.SaveChangesAsync();

				Logger.Information("Reset {EmailCount} failed emails to pending status", failedEmails.Count);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to retry failed emails");
				throw;
			}
		}

		public async Task ResetRetryCountAsync()
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();

				var emailsWithRetries = await context.QueuedEmails
					.Where(e => e.AttemptCount > 0 && (e.Status == EmailStatus.Pending || e.Status == EmailStatus.Failed))
					.ToListAsync();

				foreach (var email in emailsWithRetries)
				{
					email.AttemptCount = 0;
					email.LastError = null;
					email.LastAttemptAt = null;

					// If it was failed, set it back to pending to give it another chance
					if (email.Status == EmailStatus.Failed)
					{
						email.Status = EmailStatus.Pending;
					}
				}

				await context.SaveChangesAsync();

				Logger.Information("Reset retry count for {EmailCount} emails (failed emails also set to pending)",
					emailsWithRetries.Count);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to reset retry counts");
				throw;
			}
		}

		public async Task ResetRetryCountAsync(int emailId)
		{
			try
			{
				using var context = await _dbContextFactory.CreateDbContextAsync();

				var email = await context.QueuedEmails
					.FirstOrDefaultAsync(e => e.Id == emailId);

				if (email == null)
				{
					Logger.Warning("Email with ID {EmailId} not found for retry count reset", emailId);
					return;
				}

				var oldAttemptCount = email.AttemptCount;
				var oldStatus = email.Status;

				email.AttemptCount = 0;
				email.LastError = null;
				email.LastAttemptAt = null;

				// If it was failed, set it back to pending to give it another chance
				if (email.Status == EmailStatus.Failed)
				{
					email.Status = EmailStatus.Pending;
				}

				await context.SaveChangesAsync();

				Logger.Information("Reset retry count for email {EmailId} (was {OldAttemptCount} attempts, status was {OldStatus})",
					emailId, oldAttemptCount, oldStatus);
			}
			catch (Exception ex)
			{
				Logger.Error(ex, "Failed to reset retry count for email {EmailId}", emailId);
				throw;
			}
		}

		#endregion

		#region Testing and Utility Methods

		public async Task<bool> TestSmtpConnectionAsync(SmtpConfiguration config)
		{
			if (config == null)
				throw new ArgumentNullException(nameof(config));

			try
			{
				using var client = new SmtpClient();
				client.Timeout = config.TimeoutSeconds * 1000;

				var secureSocketOptions = ResolveSecureSocketOptions(config.EnableSsl, config.Port);

				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds + 10));

				Logger.Debug("Testing connection to {Host}:{Port}", config.Host, config.Port);
				await client.ConnectAsync(config.Host, config.Port, secureSocketOptions, cts.Token);

				Logger.Debug("Testing authentication for {Username}", config.Username);
				await client.AuthenticateAsync(config.Username, config.Password, cts.Token);

				// Create test message
				var testMessage = new MimeMessage();
				testMessage.From.Add(new MailboxAddress("", config.Username));
				testMessage.To.Add(new MailboxAddress("", config.Username));
				testMessage.Subject = "SMTP Configuration Test - MailKit";
				testMessage.Body = new TextPart("plain")
				{
					Text = "This is a test email to verify SMTP settings using MailKit. If you receive this, your configuration is working correctly."
				};

				Logger.Debug("Sending test message");
				await client.SendAsync(testMessage, cts.Token);

				Logger.Debug("Disconnecting from server");
				await client.DisconnectAsync(true, cts.Token);

				Logger.Information("MailKit SMTP connection test successful for {Host}:{Port}", config.Host, config.Port);
				return true;
			}
			catch (OperationCanceledException)
			{
				Logger.Warning("MailKit SMTP connection test timed out for {Host}:{Port}", config.Host, config.Port);
				return false;
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "MailKit SMTP connection test failed for {Host}:{Port}: {Error}",
					config.Host, config.Port, ex.Message);
				return false;
			}
		}

		private Task<bool> IsNetworkAvailableAsync()
		{
			try
			{
				var isAvailable = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
				return Task.FromResult(isAvailable);
			}
			catch (Exception ex)
			{
				Logger.Debug(ex, "Network availability check failed");
				return Task.FromResult(false);
			}
		}

		#endregion

		#region Offline Mode Methods

		public void EnableOfflineMode()
		{
			_isOfflineMode = true;
			Logger.Information("MailKit offline mode enabled - all emails will be queued");
		}

		public void DisableOfflineMode()
		{
			_isOfflineMode = false;
			ResetCircuitBreaker();
			Logger.Information("MailKit offline mode disabled - resuming email sending");

			// Trigger immediate queue processing
			Task.Run(async () =>
			{
				try
				{
					await Task.Delay(1000);
					await ProcessQueueAsync();
				}
				catch (Exception ex)
				{
					Logger.Error(ex, "Error processing queue after disabling offline mode");
				}
			});
		}

		#endregion

		#region IDisposable Implementation

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (_disposed)
				return;

			if (disposing)
			{
				try
				{
					_backgroundTimer?.Dispose();
					_queueSemaphore?.Dispose();
					_configSemaphore?.Dispose();

					Logger.Information("MailKitService disposed successfully");
				}
				catch (Exception ex)
				{
					Logger.Error(ex, "Error during MailKitService disposal");
				}
			}

			_disposed = true;
		}

		~MailKitService()
		{
			Dispose(false);
		}

		#endregion
	}
}