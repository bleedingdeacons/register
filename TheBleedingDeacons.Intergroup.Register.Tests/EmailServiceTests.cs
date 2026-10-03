using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The email service against an SMTP server that misbehaves, and a tablet
/// with no network. The background timer is left alone — its first run is
/// a minute away and every test disposes the service long before then.
///
/// <para>The circuit breaker lives only in the timer's callback, so its
/// tests run that callback directly rather than waiting for the timer.</para>
/// </summary>
public sealed class EmailServiceTests : IDisposable
{
	private readonly InMemoryDb<MailDbContext> _db = Databases.Mail();
	private readonly FakeSmtpClient _smtp = new();
	private bool _online = true;
	private readonly EmailService _service;
	private readonly List<EmailFailedEventArgs> _failures = [];
	private readonly List<CircuitStateChangedEventArgs> _breaker = [];

	public EmailServiceTests()
	{
		_service = new EmailService(
			_db,
			() => _online,
			"smtp.example.org", 587, "register@example.org", "secret",
			maxRetries: 3,
			smtpClientFactory: () => _smtp);
		_service.EmailFailed += (_, e) => _failures.Add(e);
		_service.CircuitStateChanged += (_, e) => _breaker.Add(e);
	}

	public void Dispose()
	{
		_service.Dispose();
		_db.Dispose();
	}

	[Theory]
	[InlineData("", "Subject", "Body")]
	[InlineData("dave@example.org", " ", "Body")]
	[InlineData("dave@example.org", "Subject", "")]
	public async Task AnIncompleteEmailIsRefusedBeforeAnythingElse(string to, string subject, string body)
	{
		await Assert.ThrowsAsync<ArgumentException>(() => _service.SendEmailAsync(to, subject, body));
		await Assert.ThrowsAsync<ArgumentException>(() => _service.QueueEmailAsync(to, subject, body));
		Assert.Empty(_smtp.Connections);
	}

	[Fact]
	public void TheNetworkProbeIsRequired() =>
		Assert.Throws<ArgumentNullException>(() => new EmailService(_db, null!, "h", 25, "u", "p"));

	[Fact]
	public async Task OfflineAnEmailIsQueuedNotSent()
	{
		_online = false;

		var sent = await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello");

		Assert.False(sent);
		Assert.Empty(_smtp.Connections);
		var queued = Assert.Single(await _service.GetQueuedEmailsAsync());
		Assert.Equal(EmailStatus.Pending, queued.Status);
	}

	[Fact]
	public async Task OfflineModeQueuesEvenWithANetwork()
	{
		_service.EnableOfflineMode();

		Assert.False(await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello"));
		Assert.Empty(_smtp.Connections);
	}

	[Fact]
	public async Task AProbeThatThrowsCountsAsOffline()
	{
		using var service = new EmailService(_db, () => throw new InvalidOperationException("no connectivity API"),
			"smtp.example.org", 587, "u", "p", smtpClientFactory: () => _smtp);

		Assert.False(await service.SendEmailAsync("dave@example.org", "Welcome", "Hello"));
		Assert.Empty(_smtp.Connections);
	}

	[Fact]
	public async Task ASentEmailIsStoredAsSentAndTheConnectionClosed()
	{
		Assert.True(await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello"));

		Assert.Single(_smtp.Sent);
		Assert.Equal(1, _smtp.Disconnects);
		Assert.Equal(EmailStatus.Sent, Assert.Single(await _service.GetQueuedEmailsAsync()).Status);
	}

	[Theory]
	[InlineData(587, SecureSocketOptions.StartTls)]
	[InlineData(465, SecureSocketOptions.SslOnConnect)]
	public async Task ThePortChoosesTheTls(int port, SecureSocketOptions expected)
	{
		await _service.UpdateConfigurationAsync(new SmtpConfiguration { Host = "smtp.example.org", Port = port, Username = "u", Password = "p", EnableSsl = true });

		await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello");

		Assert.Equal(expected, Assert.Single(_smtp.Connections).Options);
	}

	[Fact]
	public async Task AnUnreachableServerLeavesTheEmailPendingAndRetryable()
	{
		_smtp.FailOnConnect = new SocketException((int)SocketError.TimedOut);

		Assert.False(await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello"));

		var email = Assert.Single(await _service.GetQueuedEmailsAsync());
		Assert.Equal(EmailStatus.Pending, email.Status);
		Assert.Equal(1, email.AttemptCount);
		Assert.True(Assert.Single(_failures).IsRetryable);
	}

	[Theory]
	[InlineData(SocketError.HostNotFound)]
	[InlineData(SocketError.ConnectionRefused)]
	public async Task AServerThatDoesNotExistIsNotRetryable(SocketError error)
	{
		_smtp.FailOnConnect = new SocketException((int)error);

		await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello");

		Assert.False(Assert.Single(_failures).IsRetryable);
	}

	[Fact]
	public async Task ARefusedPasswordIsNotRetryable()
	{
		_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");

		await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello");

		Assert.False(Assert.Single(_failures).IsRetryable);
		Assert.Empty(_smtp.Sent);
	}

	[Fact]
	public async Task AFullMailboxIsRetryable()
	{
		_smtp.FailOnSend = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "452 try later");

		await _service.SendEmailAsync("dave@example.org", "Welcome", "Hello");

		Assert.True(Assert.Single(_failures).IsRetryable);
	}

	[Fact]
	public async Task TheLastAllowedAttemptMarksTheEmailFailed()
	{
		await using (var context = _db.CreateDbContext())
		{
			context.QueuedEmails.Add(new QueuedEmail
			{
				To = "dave@example.org", Subject = "Welcome", Body = "Hello", From = "register@example.org",
				CreatedAt = DateTime.UtcNow, Status = EmailStatus.Pending, AttemptCount = 2, MaxRetries = 3,
			});
			await context.SaveChangesAsync();
		}

		_smtp.FailOnSend = new IOException("connection reset");

		await _service.ProcessQueueAsync();

		await using var check = _db.CreateDbContext();
		var email = await check.QueuedEmails.SingleAsync();
		Assert.Equal(EmailStatus.Failed, email.Status);
		Assert.Equal(3, email.AttemptCount);
		Assert.Equal("connection reset", email.LastError);
	}

	[Fact]
	public async Task ProcessingSkipsEntirelyWhileOffline()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_online = false;

		Assert.False(await _service.ProcessQueueAsync());
		Assert.Empty(_smtp.Connections);
	}

	[Fact]
	public async Task AnEmptyQueueConnectsToNothing()
	{
		Assert.True(await _service.ProcessQueueAsync());
		Assert.Empty(_smtp.Connections);
	}

	[Fact]
	public async Task ARefusedPasswordIsReportedAsNotRunWithoutTryingAnEmail()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");

		Assert.False(await _service.ProcessQueueAsync());

		var email = Assert.Single(await _service.GetQueuedEmailsAsync());
		Assert.Equal(EmailStatus.Pending, email.Status);
		Assert.Equal(0, email.AttemptCount);
	}

	[Fact]
	public async Task ARefusedPasswordClosesTheConnectionItOpened()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");

		await _service.ProcessQueueAsync();

		Assert.Single(_smtp.Connections);
		Assert.False(_smtp.IsConnected);
		Assert.Equal(1, _smtp.Disposals);
	}

	[Fact]
	public async Task ThreeRefusedPasswordsInARowOpenTheBreaker()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");

		await Tick(2);

		Assert.False(_service.IsCircuitOpen);
		Assert.Equal(2, _service.ConsecutiveQueueFailures);

		await Tick();

		Assert.True(_service.IsCircuitOpen);
		Assert.Equal("535 bad credentials", _service.LastQueueError);
		var opened = Assert.Single(_breaker);
		Assert.True(opened.IsOpen);
		Assert.Equal(3, opened.ConsecutiveFailures);
		Assert.Equal(0, Assert.Single(await _service.GetQueuedEmailsAsync()).AttemptCount);
	}

	[Fact]
	public async Task AnOpenBreakerStopsTheBackgroundConnecting()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");
		await Tick(3);
		var connections = _smtp.Connections.Count;

		await Tick();

		Assert.Equal(connections, _smtp.Connections.Count);
	}

	[Fact]
	public async Task ATlsFailureCountsTowardTheBreaker()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnConnect = new System.Security.Authentication.AuthenticationException("bad certificate");

		await Tick();

		Assert.Equal(1, _service.ConsecutiveQueueFailures);
	}

	public static TheoryData<Exception> NetworkFailures => new()
	{
		new SocketException((int)SocketError.HostUnreachable),
		new IOException("connection reset"),
	};

	[Theory]
	[MemberData(nameof(NetworkFailures))]
	public async Task AnUnreachableServerNeverCountsTowardTheBreaker(Exception failure)
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnConnect = failure;

		await Tick(3);

		Assert.False(_service.IsCircuitOpen);
		Assert.Equal(0, _service.ConsecutiveQueueFailures);
		Assert.Empty(_breaker);
	}

	[Fact]
	public async Task OfflineTheBackgroundNeitherConnectsNorCounts()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_online = false;

		await Tick(3);

		Assert.Equal(0, _service.ConsecutiveQueueFailures);
		Assert.Empty(_smtp.Connections);
	}

	[Fact]
	public async Task ARunThatConnectsClearsTheCount()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");
		await Tick(2);

		_smtp.FailOnAuthenticate = null;
		await Tick();

		Assert.Equal(0, _service.ConsecutiveQueueFailures);
		Assert.Null(_service.LastQueueError);
		Assert.Equal(EmailStatus.Sent, Assert.Single(await _service.GetQueuedEmailsAsync()).Status);
	}

	[Fact]
	public async Task APasswordRefusedOnReconnectingCountsToo()
	{
		await _service.QueueEmailAsync("dave@example.org", "Welcome", "Hello");
		await _service.QueueEmailAsync("ann@example.org", "Welcome", "Hello");

		// The first send drops the connection; by the time the batch
		// reconnects for the second email, the server refuses the password.
		_smtp.FailOnSend = new IOException("connection reset");
		_service.EmailFailed += (_, _) =>
		{
			_smtp.FailOnSend = null;
			_smtp.FailOnAuthenticate = new MailKit.Security.AuthenticationException("535 bad credentials");
			_smtp.DisconnectAsync(true).GetAwaiter().GetResult();
		};

		await Tick();

		Assert.Equal(1, _service.ConsecutiveQueueFailures);
		Assert.Equal([0, 1], (await _service.GetQueuedEmailsAsync()).Select(e => e.AttemptCount).Order());
	}

	private async Task Tick(int times = 1)
	{
		for (var i = 0; i < times; i++)
		{
			await _service.ProcessQueueInBackground();
		}
	}

	[Fact]
	public async Task AnIncompleteConfigurationIsNotProbed()
	{
		var result = await _service.TestSmtpReachabilityAsync(new SmtpConfiguration());

		Assert.False(result.IsReachable);
		Assert.Equal(SmtpReachabilityKind.Other, result.Kind);
		Assert.Empty(_smtp.Connections);
	}

	public static TheoryData<Exception, SmtpReachabilityKind> ProbeFailures => new()
	{
		{ new MailKit.Security.AuthenticationException("535"), SmtpReachabilityKind.Auth },
		{ new System.Security.Authentication.AuthenticationException("bad certificate"), SmtpReachabilityKind.Tls },
		{ new SocketException((int)SocketError.HostNotFound), SmtpReachabilityKind.Network },
		{ new IOException("reset"), SmtpReachabilityKind.Network },
	};

	[Theory]
	[MemberData(nameof(ProbeFailures))]
	public async Task TheProbeNamesWhatWentWrong(Exception failure, SmtpReachabilityKind kind)
	{
		if (failure is MailKit.Security.AuthenticationException)
		{
			_smtp.FailOnAuthenticate = failure;
		}
		else
		{
			_smtp.FailOnConnect = failure;
		}

		var result = await _service.TestSmtpReachabilityAsync(Valid());

		Assert.False(result.IsReachable);
		Assert.Equal(kind, result.Kind);
	}

	[Fact]
	public async Task TheProbeSendsNothing()
	{
		var result = await _service.TestSmtpReachabilityAsync(Valid());

		Assert.True(result.IsReachable);
		Assert.Empty(_smtp.Sent);
	}

	private static SmtpConfiguration Valid() =>
		new() { Host = "smtp.example.org", Port = 587, Username = "u", Password = "p", TimeoutSeconds = 5 };
}
