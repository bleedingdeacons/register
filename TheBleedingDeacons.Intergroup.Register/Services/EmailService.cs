using MailKit;
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
	public class EmailService : IEmailService, IDisposable
	{
		private static readonly ILogger Logger = AppLogger.ForContext<EmailService>();

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

		private volatile bool _isOfflineMode;
		private bool _disposed;

		// Circuit breaker — pauses the background timer after repeated failures
		// so a persistent SMTP misconfiguration doesn't spin forever.
		private const int CircuitBreakerThreshold = 3;
		private int _consecutiveQueueFailures;
		private volatile bool _circuitOpen;
		private volatile string? _lastQueueError;
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
		public event EventHandler<CircuitStateChangedEventArgs>? CircuitStateChanged;

		#endregion

		#region Constructor

		public EmailService(IDbContextFactory<MailDbContext> dbContextFactory,
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

			Logger.Information("EmailService initialized with host: {Host}:{Port}, SSL: {EnableSsl}, MaxRetries: {MaxRetries}",
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
			bool isHtml = false, string? from = null, string? cc = null, string? bcc = null, string? replyTo = null)
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
				await QueueEmailAsync(to, subject, body, isHtml, from, cc, bcc, replyTo);
				Logger.Information("Email queued due to offline mode or network unavailability: {To}", to);
				return false;
			}

			// Resolve the From address: caller override wins when supplied
			// (non-null and non-whitespace), otherwise fall back to the
			// SMTP account username — same default the service has always
			// used. Trim so a stray space in the override doesn't break
			// the MailboxAddress parser downstream.
			var resolvedFrom = string.IsNullOrWhiteSpace(from) ? _username : from!.Trim();

			// Reply-To is independent of From: NULL means "no header at
			// all", a non-empty value is trimmed and persisted on the row
			// so the sender path picks it up unchanged.
			var resolvedReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo!.Trim();

			var email = new QueuedEmail
			{
				To = to,
				Subject = subject,
				Body = body,
				From = resolvedFrom,
				ReplyTo = resolvedReplyTo,
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
			bool isHtml = false, string? from = null, string? cc = null, string? bcc = null, string? replyTo = null)
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

				// Same resolution as SendEmailAsync: caller override wins
				// when supplied, otherwise the SMTP account username. The
				// resolved value is persisted on the QueuedEmail row so
				// the override survives an app restart and is applied
				// when the background processor eventually sends.
				var resolvedFrom = string.IsNullOrWhiteSpace(from) ? _username : from!.Trim();

				// Reply-To has no fallback — null means no Reply-To header.
				var resolvedReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo!.Trim();

				var email = new QueuedEmail
				{
					To = to,
					Subject = subject,
					Body = body,
					From = resolvedFrom,
					ReplyTo = resolvedReplyTo,
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
				var message = CreateMimeMessage(queuedEmail);

				Logger.Debug("[{OperationId}] Creating SMTP client", operationId);
				using var client = new SmtpClient();

				// Configure client settings
				client.Timeout = _timeoutSeconds * 1000; // MailKit timeout in milliseconds
				client.CheckCertificateRevocation = false;

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
			if (!enableSsl)
				return SecureSocketOptions.None;

			return port switch
			{
				465 => SecureSocketOptions.SslOnConnect,
				587 => SecureSocketOptions.StartTls,
				25 => SecureSocketOptions.StartTls,
				_ => SecureSocketOptions.Auto
			};
		}
		//private static SecureSocketOptions ResolveSecureSocketOptions(bool enableSsl, int port)
		//{
		//	if (!enableSsl) return SecureSocketOptions.None;
		//	if (port == 465) return SecureSocketOptions.SslOnConnect;
		//	// Use StartTlsWhenAvailable instead of StartTls — mandatory StartTls throws
		//	// SslHandshakeException if the server doesn't advertise it in the EHLO banner.
		//	if (port is 587 or 25) return SecureSocketOptions.StartTlsWhenAvailable;
		//	return SecureSocketOptions.Auto;
		//}

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
				CheckCertificateRevocation = false // Set to false to avoid issues with certain servers; handle TLS errors in IsRetryableException instead
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
				var message = CreateMimeMessage(queuedEmail);
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

		private MimeMessage CreateMimeMessage(QueuedEmail queuedEmail)
		{
			var message = new MimeMessage();

			// Set From address
			message.From.Add(new MailboxAddress("", queuedEmail.From));

			// Set Reply-To when the queueing caller asked for one. NULL is
			// the common case (welcome emails and any caller that didn't
			// override) — no Reply-To header is added in that case, which
			// matches RFC 5322 default behaviour where replies go to From.
			if (!string.IsNullOrWhiteSpace(queuedEmail.ReplyTo))
			{
				message.ReplyTo.Add(new MailboxAddress("", queuedEmail.ReplyTo));
			}

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
			// Guard against timer callbacks firing after Dispose() has been called.
			// The Timer can enqueue one final callback between _backgroundTimer.Dispose()
			// and the actual cancellation — without this check, the callback would run
			// against disposed semaphores and throw ObjectDisposedException.
			if (_disposed)
				return;

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
					var previousFailures = Interlocked.Exchange(ref _consecutiveQueueFailures, 0);
					if (previousFailures > 0)
					{
						Logger.Information(
							"Queue processing succeeded — resetting failure counter (was {Count})",
							previousFailures);
					}
					_lastQueueError = null;
				}
				else
				{
					// ProcessQueueAsync returned false. The three documented causes
					// (offline mode, no network, semaphore contention) are all
					// transient and NOT our SMTP config's fault — they should not
					// count toward tripping the breaker. Log at Debug so we still
					// have the trail if something weird happens.
					Logger.Debug("Background queue processing skipped (offline, no network, or concurrent run)");
				}
			}
			catch (ObjectDisposedException)
			{
				// Service was disposed while the timer callback was in-flight — expected, not an error.
				Logger.Debug("Background queue processing aborted — service disposed");
			}
			catch (Exception ex)
			{
				// ProcessQueueAsync threw after passing the offline/network guards.
				// Probe reachability to decide whether this is "the network died
				// mid-send" (transient — don't trip the breaker) vs "SMTP is
				// broken" (persistent — do trip it). The probe is cheap — one
				// connect + auth, no email sent.
				var countsTowardBreaker = await ClassifyFailureAsync(ex);

				if (countsTowardBreaker)
				{
					RecordQueueFailure(ex.Message);
					Logger.Error(ex, "Error in background queue processing (failure {Count}/{Threshold})",
						_consecutiveQueueFailures, CircuitBreakerThreshold);
				}
				else
				{
					Logger.Warning(ex,
						"Background queue processing failed, but SMTP is unreachable — treating as transient, not counting toward breaker");
				}
			}
		}

		/// <summary>
		/// Decides whether a ProcessQueueAsync exception should count toward
		/// tripping the circuit breaker. Returns true for failures that indicate
		/// the user's SMTP configuration is actually broken (auth, TLS, etc.)
		/// and false for transient network issues (captive portal, intermittent
		/// connectivity) — those resolve on their own and shouldn't cause the
		/// breaker to pause legitimate delivery.
		/// </summary>
		private async Task<bool> ClassifyFailureAsync(Exception originalException)
		{
			// Fast-path classifications based on the exception type alone. For
			// clear-cut auth failures we don't need to probe — the server told
			// us the credentials are bad.
			if (originalException is ServiceNotAuthenticatedException
								  or MailKit.Security.AuthenticationException)
			{
				return true;
			}

			// For ambiguous exceptions (SocketException, IOException, timeouts)
			// run a short reachability probe to decide. If we can connect and
			// authenticate right now, the earlier failure was a transient hiccup
			// during the batch — still worth counting (SMTP may be flaky). If
			// we can't even reach the server, it's a network-level problem and
			// pausing our background would punish the user for poor connectivity.
			try
			{
				var (host, port, username, password, enableSsl) = await GetCurrentConfigAsync();
				var probeConfig = new SmtpConfiguration
				{
					Host = host,
					Port = port,
					Username = username,
					Password = password,
					EnableSsl = enableSsl,
					// Shorter timeout for the probe than for real sends — we want
					// a fast "can I reach this?" answer, not a thorough test.
					TimeoutSeconds = 10,
				};

				using var probeCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
				var probe = await TestSmtpReachabilityAsync(probeConfig, probeCts.Token);

				return probe.Kind switch
				{
					// SMTP is up → the earlier send failure was either spurious
					// or a server-side problem. Count toward breaker so a broken
					// configuration is eventually caught.
					SmtpReachabilityKind.Success => true,

					// Credentials bad — definitely count.
					SmtpReachabilityKind.Auth => true,

					// TLS problems typically indicate a configuration error
					// (wrong port, SSL mismatch). Count.
					SmtpReachabilityKind.Tls => true,

					// Network problems (DNS, refused, I/O) — don't count. The
					// phone is probably offline or on a captive portal. It will
					// self-resolve.
					SmtpReachabilityKind.Network => false,

					// Timeouts are ambiguous. Lean toward "don't count" to avoid
					// false positives on slow networks.
					SmtpReachabilityKind.Timeout => false,

					// Unknown — conservatively count so a genuinely broken
					// config doesn't hide forever behind the probe.
					_ => true,
				};
			}
			catch (Exception probeEx)
			{
				// If even the probe can't run, treat the outer failure as real
				// (conservative). We still log the probe failure for diagnosis.
				Logger.Debug(probeEx, "Reachability probe itself failed — treating original error as breaker-worthy");
				return true;
			}
		}

		private void RecordQueueFailure(string error)
		{
			var count = Interlocked.Increment(ref _consecutiveQueueFailures);
			_lastQueueError = error;

			if (count >= CircuitBreakerThreshold && !_circuitOpen)
			{
				_circuitOpen = true;
				_circuitOpenedAt = DateTime.UtcNow;

				Logger.Warning(
					"Circuit breaker OPEN — background email processing paused after {Count} consecutive failures. " +
					"Last error: {Error}. Call ResetCircuitBreaker() or update SMTP settings to resume.",
					count, _lastQueueError);

				RaiseCircuitStateChanged(isOpen: true, count, _lastQueueError, _circuitOpenedAt);
			}
		}

		/// <summary>
		/// Resets the circuit breaker, clears the failure counter, and resumes
		/// background queue processing on the next timer tick. Call this after
		/// updating SMTP configuration or resolving the underlying issue.
		/// Idempotent — safe to call when the breaker is already closed.
		/// </summary>
		public void ResetCircuitBreaker()
		{
			// Capture before clearing so we only fire the event on a real transition.
			var wasOpen = _circuitOpen;

			_circuitOpen = false;
			_circuitOpenedAt = null;
			Interlocked.Exchange(ref _consecutiveQueueFailures, 0);
			_lastQueueError = null;

			Logger.Information("Circuit breaker reset — background email processing will resume");

			if (wasOpen)
			{
				RaiseCircuitStateChanged(isOpen: false, 0, null, null);
			}
		}

		/// <summary>
		/// Invokes CircuitStateChanged on a background thread so subscriber work
		/// (typically UI updates) runs off the thread that tripped the breaker.
		/// Any exception thrown by a subscriber is logged and swallowed — a buggy
		/// handler must never bring the email service down.
		/// </summary>
		private void RaiseCircuitStateChanged(bool isOpen, int consecutiveFailures, string? lastError, DateTime? openedAt)
		{
			var handler = CircuitStateChanged;
			if (handler is null) return;

			var args = new CircuitStateChangedEventArgs
			{
				IsOpen = isOpen,
				ConsecutiveFailures = consecutiveFailures,
				LastError = lastError,
				OpenedAt = openedAt
			};

			try
			{
				handler.Invoke(this, args);
			}
			catch (Exception ex)
			{
				Logger.Warning(ex, "CircuitStateChanged subscriber threw");
			}
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
				client.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
				client.CheckCertificateRevocation = false;

				var port = config.Port;

				port = 587; // Force StartTLS for testing, even if the user put 465. This is because many servers misconfigure their SSL ports and don't actually support SSL-on-connect, but do support StartTLS. For a connection test, we want to give the user the best chance of succeeding and diagnosing their config, rather than failing due to a common server misconfiguration.

				var secureSocketOptions = ResolveSecureSocketOptions(config.EnableSsl, port);

				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(config.TimeoutSeconds + 10));

				//client.ServerCertificateValidationCallback = (s, c, h, e) => true;
				Logger.Debug("Testing connection to {Host}:{Port}", config.Host, port);
				await client.ConnectAsync(config.Host, port, secureSocketOptions, cts.Token);

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

		/// <summary>
		/// Lightweight SMTP probe — connects and authenticates but does NOT
		/// send a test email. Used by:
		///   • The Email Status page "Test Connection" button, so users can
		///     check credentials without spamming their own inbox.
		///   • ProcessQueueInBackground, to distinguish network-level failures
		///     (not the user's fault → don't count toward the breaker) from
		///     SMTP-level failures (the user's config is broken → count).
		///
		/// Classifies the failure into a small set of actionable kinds rather
		/// than dumping raw exception text — see <see cref="SmtpReachabilityKind"/>.
		/// </summary>
		public async Task<SmtpReachabilityResult> TestSmtpReachabilityAsync(
			SmtpConfiguration config, CancellationToken cancellationToken = default)
		{
			if (config is null)
				throw new ArgumentNullException(nameof(config));

			if (!config.IsValid())
				return SmtpReachabilityResult.Failure(
					SmtpReachabilityKind.Other,
					"SMTP configuration is incomplete.");

			using var client = new SmtpClient { Timeout = config.TimeoutSeconds * 1000 };

			// Hard ceiling on total probe time. +5s over the SmtpClient timeout
			// to allow the inner timeout to surface its own diagnostic error.
			using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			cts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds + 5));

			var secureSocketOptions = ResolveSecureSocketOptions(config.EnableSsl, config.Port);

			try
			{
				await client.ConnectAsync(config.Host, config.Port, secureSocketOptions, cts.Token);
				await client.AuthenticateAsync(config.Username, config.Password, cts.Token);
				await client.DisconnectAsync(quit: true, cts.Token);
				return SmtpReachabilityResult.Success();
			}
			catch (ServiceNotAuthenticatedException ex)
			{
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Auth,
					"Authentication failed — check username and password.", ex);
			}
			catch (AuthenticationException ex)
			{
				// MailKit's AuthenticationException wraps auth failures from the server.
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Auth,
					ex.Message, ex);
			}
			catch (System.Security.Authentication.AuthenticationException ex)
			{
				// Distinct from MailKit's — this is TLS cert / handshake.
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Tls,
					$"TLS handshake failed: {ex.Message}", ex);
			}
			catch (OperationCanceledException ex) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
			{
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Timeout,
					$"Probe timed out after {config.TimeoutSeconds + 5}s.", ex);
			}
			catch (System.Net.Sockets.SocketException ex)
			{
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Network,
					$"Could not reach {config.Host}:{config.Port} — {ex.SocketErrorCode}.", ex);
			}
			catch (System.IO.IOException ex)
			{
				// Usually TLS negotiation over a blocked port, or mid-stream disconnect.
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Network,
					$"Network I/O error: {ex.Message}", ex);
			}
			catch (SmtpProtocolException ex)
			{
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Other,
					$"SMTP protocol error: {ex.Message}", ex);
			}
			catch (Exception ex)
			{
				return SmtpReachabilityResult.Failure(SmtpReachabilityKind.Other,
					ex.Message, ex);
			}
			finally
			{
				// SmtpClient is IDisposable; we created it above.
				client.Dispose();
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

			// Trigger immediate queue processing (unless already disposed)
			if (!_disposed)
			{
				Task.Run(async () =>
				{
					await Task.Delay(1000);
					await ProcessQueueAsync();
				}).SafeFireAndForget("DisableOfflineMode queue flush");
			}
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

			// Set early so in-flight timer callbacks can bail out.
			_disposed = true;

			if (disposing)
			{
				try
				{
					// Stop the timer first. Change(Infinite, Infinite) prevents new
					// callbacks from being enqueued, and the ManualResetEvent signals
					// when any currently-executing callback has finished — so we don't
					// dispose the semaphores out from under a running ProcessQueueAsync.
					using var timerStopped = new ManualResetEvent(false);
					if (_backgroundTimer.Dispose(timerStopped))
					{
						// Wait up to 30 seconds for in-flight callback to drain.
						timerStopped.WaitOne(TimeSpan.FromSeconds(30));
					}

					_queueSemaphore?.Dispose();
					_configSemaphore?.Dispose();

					Logger.Information("EmailService disposed successfully");
				}
				catch (Exception ex)
				{
					Logger.Error(ex, "Error during EmailService disposal");
				}
			}
		}

		~EmailService()
		{
			Dispose(false);
		}

		#endregion
	}
}