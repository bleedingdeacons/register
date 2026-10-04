using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Inventory;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Tests.Support;

/// <summary>
/// An SMTP server, scripted. MailKit's own client with the four network
/// calls overridden, so EmailService drives it exactly as it drives the real
/// one — the factory seam hands this out instead.
///
/// <para>Recording rather than mocking: a test asks afterwards what was
/// connected to and what was sent.</para>
/// </summary>
public sealed class FakeSmtpClient : SmtpClient
{
	private bool _connected;

	public Exception? FailOnConnect { get; set; }

	public Exception? FailOnAuthenticate { get; set; }

	public Exception? FailOnSend { get; set; }

	public List<(string Host, int Port, SecureSocketOptions Options)> Connections { get; } = [];

	public List<string> Authentications { get; } = [];

	public List<MimeMessage> Sent { get; } = [];

	public int Disconnects { get; private set; }

	public override bool IsConnected => _connected;

	public override Task ConnectAsync(string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default)
	{
		Connections.Add((host, port, options));
		if (FailOnConnect is not null)
		{
			throw FailOnConnect;
		}

		_connected = true;
		return Task.CompletedTask;
	}

	public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default)
	{
		Authentications.Add(credentials.GetCredential(new Uri("smtp://test"), "PLAIN")?.UserName ?? string.Empty);
		return FailOnAuthenticate is null ? Task.CompletedTask : throw FailOnAuthenticate;
	}

	public override Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, MailKit.ITransferProgress? progress = null)
	{
		if (FailOnSend is not null)
		{
			throw FailOnSend;
		}

		Sent.Add(message);
		return Task.FromResult("250 OK");
	}

	public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
	{
		Disconnects++;
		_connected = false;
		return Task.CompletedTask;
	}

	public int Disposals { get; private set; }

	// MailKit's client closes its socket on Dispose. This one is handed out
	// again for the next connection, so it has to forget the last one too.
	protected override void Dispose(bool disposing)
	{
		Disposals++;
		_connected = false;
		base.Dispose(disposing);
	}
}

/// <summary>A database factory over one in-memory SQLite connection, kept open for the test.</summary>
public sealed class InMemoryDb<TContext> : IDbContextFactory<TContext>, IDisposable
	where TContext : DbContext
{
	private readonly SqliteConnection _connection = new("DataSource=:memory:");
	private readonly DbContextOptions<TContext> _options;
	private readonly Func<DbContextOptions<TContext>, TContext> _create;

	public InMemoryDb(Func<DbContextOptions<TContext>, TContext> create)
	{
		_create = create;
		_connection.Open();
		_options = new DbContextOptionsBuilder<TContext>().UseSqlite(_connection).Options;
		using var context = _create(_options);
		context.Database.EnsureCreated();
	}

	public TContext CreateDbContext() => _create(_options);

	public void Dispose() => _connection.Dispose();
}

/// <summary>Shorthands for the two databases Core uses.</summary>
public static class Databases
{
	public static InMemoryDb<MailDbContext> Mail() => new(o => new MailDbContext(o));

	public static InMemoryDb<UnityDbContext> Unity() => new(o => new UnityDbContext(o));
}

/// <summary>A directory under the system temp folder, removed afterwards.</summary>
public sealed class TempDirectory : IDisposable
{
	public TempDirectory()
	{
		Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "register-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path);
	}

	public string Path { get; }

	public string File(string name) => System.IO.Path.Combine(Path, name);

	public void Dispose()
	{
		try
		{
			Directory.Delete(Path, recursive: true);
		}
		catch (IOException)
		{
			// Best effort; the OS clears temp eventually.
		}
	}
}

/// <summary>
/// Configuration with every switch a field. The real one reads
/// credentials from Freedom and switches from Preferences, and stays in the app.
/// </summary>
public sealed class FakeConfigurationService : IConfigurationService
{
	public SmtpConfiguration Smtp { get; set; } = new();

	public UnityConfiguration Unity { get; set; } = new() { BaseUrl = "https://aa-bristol.org" };

	public BetterStackConfiguration BetterStack { get; set; } = new();

	public bool IsRegistrationEventLogEnabled { get; set; } = true;

	public bool IsAutoRegisterPositionsOnGroupEnabled { get; set; } = true;

	public bool IsComplianceEventLogEnabled { get; set; } = true;

	public bool IsSingleGsrShortcutEnabled { get; set; } = true;

	public bool IsAddPositionHolderEnabled { get; set; } = true;

	public bool IsWelcomeEmailOnRegistrationEnabled { get; set; } = true;

	public string DeviceLabel { get; set; } = "Test tablet";

	public bool IsComplianceAcceptanceEmailEnabled { get; set; } = true;

	public bool IsButtonSoundEnabled { get; set; } = true;

	public string ComplianceEmail { get; set; } = string.Empty;

	public SmtpConfiguration GetSmtpConfiguration() => Smtp;

	public Task<SmtpConfiguration> LoadSmtpConfigurationAsync() => Task.FromResult(Smtp);

	public Task<UnityConfiguration> LoadUnityConfigurationAsync() => Task.FromResult(Unity);

	public Task SaveActiveIntergroupMeetingAsync(int? meetingId)
	{
		Unity.ActiveIntergroupMeetingId = meetingId;
		return Task.CompletedTask;
	}

	public BetterStackConfiguration GetBetterStackConfiguration() => BetterStack;

	public Task<BetterStackConfiguration> LoadBetterStackConfigurationAsync() => Task.FromResult(BetterStack);

	public void SetRegistrationEventLogEnabled(bool enabled) => IsRegistrationEventLogEnabled = enabled;

	public void SetAutoRegisterPositionsOnGroupEnabled(bool enabled) => IsAutoRegisterPositionsOnGroupEnabled = enabled;

	public void SetComplianceEventLogEnabled(bool enabled) => IsComplianceEventLogEnabled = enabled;

	public void SetSingleGsrShortcutEnabled(bool enabled) => IsSingleGsrShortcutEnabled = enabled;

	public void SetAddPositionHolderEnabled(bool enabled) => IsAddPositionHolderEnabled = enabled;

	public void SetWelcomeEmailOnRegistrationEnabled(bool enabled) => IsWelcomeEmailOnRegistrationEnabled = enabled;

	public void SetDeviceLabel(string? label) => DeviceLabel = label ?? string.Empty;

	public void SetComplianceAcceptanceEmailEnabled(bool enabled) => IsComplianceAcceptanceEmailEnabled = enabled;

	public void SetButtonSoundEnabled(bool enabled) => IsButtonSoundEnabled = enabled;

	public void InvalidateCache()
	{
	}
}

/// <summary>The privacy-policy cache, in memory. The real one is a Preferences blob.</summary>
public sealed class FakePrivacyPolicyCache : IPrivacyPolicyCache
{
	public CachedPrivacyPolicy? Cached { get; set; }

	public CachedPrivacyPolicy? GetCached() => Cached;

	public void Save(PrivacyPolicy policy) => Cached = new CachedPrivacyPolicy
	{
		Id = policy.Id,
		Title = policy.Title,
		Version = policy.Version,
		Policy = policy.Policy,
		Modified = policy.Modified,
		CachedAt = DateTime.UtcNow,
	};

	public void Clear() => Cached = null;

	/// <summary>A policy worth agreeing to.</summary>
	public static FakePrivacyPolicyCache With(string version = "2.1", string body = "We keep your name and number.") =>
		new() { Cached = new CachedPrivacyPolicy { Id = 42, Title = "Privacy policy", Version = version, Policy = body } };
}

/// <summary>Records every acceptance it is given; can be told to throw on one member.</summary>
public sealed class RecordingComplianceRegistration : IComplianceRegistration
{
	public List<(Member Member, string Version, DateTime? At)> Accepted { get; } = [];

	public int? ThrowFor { get; set; }

	public Task RecordAcceptance(Member member, string version, string statement, string method = "register-app", DateTime? acceptedAtUtc = null, CancellationToken ct = default)
	{
		if (member.Id == ThrowFor)
		{
			throw new InvalidOperationException("database is locked");
		}

		Accepted.Add((member, version, acceptedAtUtc));
		return Task.CompletedTask;
	}

	public Task RecordRevocation(Member member, DateTime? revokedAtUtc = null, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>An HTTP server, scripted: one answer for every request, every request recorded.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
	public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
		_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };

	public List<Uri> Requests { get; } = [];

	public static StubHttpHandler Answering(HttpStatusCode status, string body, string mediaType = "application/json") =>
		new() { Respond = _ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) } };

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(request.RequestUri!);
		return Task.FromResult(Respond(request));
	}
}

/// <summary>Records what it was reconfigured with.</summary>
public sealed class RecordingLogShipper : ILogShipper
{
	public List<BetterStackConfiguration?> Reconfigured { get; } = [];

	public ShippingState State => ShippingState.NotStarted;

	public void Reconfigure(BetterStackConfiguration? configuration) => Reconfigured.Add(configuration);

	public void Flush()
	{
	}
}

/// <summary>A counter store that can be told to fail.</summary>
public sealed class FakeTemporaryIdStore : ITemporaryIdStore
{
	public int Value { get; set; }

	public bool FailOnGet { get; set; }

	public bool FailOnSet { get; set; }

	public int Get() => FailOnGet ? throw new IOException("prefs unreadable") : Value;

	public void Set(int value)
	{
		if (FailOnSet)
		{
			throw new IOException("disk full");
		}

		Value = value;
	}
}
