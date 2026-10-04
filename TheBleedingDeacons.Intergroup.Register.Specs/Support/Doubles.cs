using System.Net;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Support;

// The platform pieces a tablet has and a test host does not.
//
// These are deliberately a second copy of what Register.Tests carries, not
// a shared package: nothing can reference a test project, and the copies are
// kept lean on purpose. Link made the same choice with Sealing.cs.

/// <summary>
/// The mail server, scripted. Each SMTP client EmailService asks for is a
/// fresh <see cref="ScriptedSmtpClient"/> reporting here, so what was sent
/// is a question asked of one list however many connections it took.
/// </summary>
public sealed class FakeSmtpServer
{
	public Exception? RefuseConnections { get; set; }

	public Exception? RefusePassword { get; set; }

	public Exception? RefuseMessages { get; set; }

	public List<MimeMessage> Delivered { get; } = [];

	public int Connections { get; set; }

	public SmtpClient NewClient() => new ScriptedSmtpClient(this);

	/// <summary>MailKit's own client with the four network calls answered from the script.</summary>
	private sealed class ScriptedSmtpClient(FakeSmtpServer server) : SmtpClient
	{
		private bool _connected;

		public override bool IsConnected => _connected;

		public override Task ConnectAsync(string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default)
		{
			server.Connections++;
			if (server.RefuseConnections is { } refusal)
			{
				throw refusal;
			}

			_connected = true;
			return Task.CompletedTask;
		}

		public override Task AuthenticateAsync(Encoding encoding, ICredentials credentials, CancellationToken cancellationToken = default) =>
			server.RefusePassword is { } refusal ? throw refusal : Task.CompletedTask;

		public override Task<string> SendAsync(FormatOptions options, MimeMessage message, CancellationToken cancellationToken = default, MailKit.ITransferProgress? progress = null)
		{
			if (server.RefuseMessages is { } refusal)
			{
				throw refusal;
			}

			server.Delivered.Add(message);
			return Task.FromResult("250 OK");
		}

		public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
		{
			_connected = false;
			return Task.CompletedTask;
		}
	}
}

/// <summary>A database factory over one in-memory SQLite connection, kept open for the scenario.</summary>
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

/// <summary>
/// The tablet's settings. The real ConfigurationService is Preferences and
/// SecureStorage, and stays in the app; every switch here is a field.
/// </summary>
public sealed class FakeConfigurationService : IConfigurationService
{
	public SmtpConfiguration Smtp { get; set; } = new() { Host = "smtp.example.org", Port = 587, Username = "register@example.org", Password = "secret" };

	public UnityConfiguration Unity { get; set; } = new() { BaseUrl = "https://aa-bristol.org", ApiKey = "unity-key", ActiveIntergroupMeetingId = 77 };

	public BetterStackConfiguration BetterStack { get; set; } = new();

	public bool IsRegistrationEventLogEnabled { get; set; } = true;

	public bool IsAutoRegisterPositionsOnGroupEnabled { get; set; } = true;

	public bool IsComplianceEventLogEnabled { get; set; } = true;

	public bool IsSingleGsrShortcutEnabled { get; set; } = true;

	public bool IsAddPositionHolderEnabled { get; set; } = true;

	public bool IsWelcomeEmailOnRegistrationEnabled { get; set; } = true;

	public string DeviceLabel { get; set; } = "Front desk";

	public bool IsComplianceAcceptanceEmailEnabled { get; set; } = true;

	public bool IsButtonSoundEnabled { get; set; } = true;

	public string ComplianceEmail { get; set; } = "privacy@aa-bristol.org";

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

/// <summary>The cached privacy policy, in memory. The real one is a Preferences blob.</summary>
public sealed class FakePrivacyPolicyCache : IPrivacyPolicyCache
{
	public CachedPrivacyPolicy? Cached { get; set; } = new()
	{
		Id = 42,
		Title = "Privacy policy",
		Version = "2.1",
		Policy = "We keep your anonymous name and how to reach you, for intergroup business only.",
		Modified = "2026-09-01",
		CachedAt = DateTime.UtcNow,
	};

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
}

/// <summary>Records what the log shipper was reconfigured with.</summary>
public sealed class RecordingLogShipper : ILogShipper
{
	public List<BetterStackConfiguration?> Reconfigured { get; } = [];

	public ShippingState State => ShippingState.NotStarted;

	public void Reconfigure(BetterStackConfiguration? configuration) => Reconfigured.Add(configuration);

	public void Flush()
	{
	}
}

/// <summary>The temporary id counter's store, in memory.</summary>
public sealed class InMemoryTemporaryIdStore : ITemporaryIdStore
{
	public int Value { get; set; }

	public int Get() => Value;

	public void Set(int value) => Value = value;
}
