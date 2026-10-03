using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBleedingDeacons.Intergroup.Register.Data;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Client;
using TheBleedingDeacons.Unity.Intergroup.Data;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories;
using TheBleedingDeacons.Unity.Intergroup.Services;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Support;

/// <summary>
/// One tablet, and everything around it, for the length of a scenario.
///
/// <para>Reqnroll gives each scenario its own instance through its
/// container, so a step class takes this in its constructor and every step
/// in the scenario sees the same tablet. The container disposes it
/// afterwards.</para>
///
/// <para><b>The services are the real ones.</b> Attendance, compliance,
/// both event logs, the email queue, the snapshot, the sync and the
/// reconcile all run as shipped, over the tablet's own two databases (in
/// memory) and a temporary directory for the logs. What is stood in for is
/// only what a tablet has and a test host does not: Preferences, the mail
/// server, the network probe, and Unity itself — which is faked under the
/// real client, not in place of it. Link and Hand chose the same line for
/// the same reason: a stubbed service would let a scenario pass while the
/// shipping one did nothing.</para>
///
/// <para><b>Services are built on first use</b>, so a Given step can change
/// a setting before anything reads it.</para>
/// </summary>
public sealed class World : IDisposable
{
	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), "register-specs", Guid.NewGuid().ToString("N"));

	private readonly Dictionary<string, int> _groups = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _members = new(StringComparer.Ordinal);
	private int _nextId = 1;

	private RegistrationEventLog? _registrationLog;
	private ComplianceEventLog? _complianceLog;
	private EmailService? _email;
	private AttendanceService? _attendance;

	public World()
	{
		Directory.CreateDirectory(_directory);

		// Temporary ids come from a static; give it a store that starts
		// empty for every scenario. See Parallelism.cs.
		TemporaryIdGenerator.Use(new InMemoryTemporaryIdStore());
	}

	/// <summary>The tablet's copy of the intergroup's records.</summary>
	public InMemoryDb<UnityDbContext> Tablet { get; } = new(o => new UnityDbContext(o));

	/// <summary>The tablet's outgoing mail queue.</summary>
	public InMemoryDb<MailDbContext> Mail { get; } = new(o => new MailDbContext(o));

	public FakeConfigurationService Settings { get; } = new();

	public FakePrivacyPolicyCache Policy { get; } = new();

	public FakeSmtpServer Smtp { get; } = new();

	public FakeUnity Unity { get; } = new();

	public bool Online { get; set; } = true;

	public RegistrationEventLog RegistrationLog =>
		_registrationLog ??= new RegistrationEventLog(Path.Combine(_directory, RegistrationEventLog.FileName));

	public ComplianceEventLog ComplianceLog =>
		_complianceLog ??= new ComplianceEventLog(Path.Combine(_directory, ComplianceEventLog.FileName));

	public EmailService Email => _email ??= new EmailService(
		Mail,
		() => Online,
		Settings.Smtp.Host, Settings.Smtp.Port, Settings.Smtp.Username, Settings.Smtp.Password,
		maxRetries: 3,
		smtpClientFactory: Smtp.NewClient);

	public EmailTemplateService Templates { get; } = new(typeof(EmailTemplateService).Assembly);

	public AttendanceService Attendance =>
		_attendance ??= new AttendanceService(Templates, Email, Tablet, RegistrationLog, Settings);

	public ComplianceService Compliance => new(Tablet, ComplianceLog, Settings, Email, Templates, Policy);

	public ConsentRound Consent => new(Policy, Compliance);

	public PositionRepository Positions => new(Tablet);

	public SnapshotService Snapshots => new(Tablet);

	public UnitySyncService Sync => new(Tablet, UnityClient, NullLogger<UnitySyncService>.Instance);

	public ReconciliationService Reconciliation =>
		new(Snapshots, Sync, Settings, UnityClient, Tablet, RegistrationLog, ComplianceLog);

	/// <summary>The real Unity client, talking to <see cref="Unity"/>.</summary>
	public Func<Task<UnityRestSharp>> UnityClient => () => Task.FromResult(
		new UnityRestSharp(Settings.Unity.BaseUrl, Settings.Unity.ApiKey, new HttpClient(Unity, disposeHandler: false)));

	// ── What the last action produced, for the Then steps ─────────────

	public ReconciliationService.ReconcileResult? Reconciled { get; set; }

	public ConsentOutcome? ConsentOutcome { get; set; }

	public List<string> AskedForConsent { get; } = [];

	public Exception? Failure { get; set; }

	// ── Names → ids ───────────────────────────────────────────────────

	public int GroupId(string name) => _groups.TryGetValue(name, out var id) ? id : throw new KeyNotFoundException($"No group called \"{name}\" in this scenario.");

	public int PositionId(string name) => _positions.TryGetValue(name, out var id) ? id : throw new KeyNotFoundException($"No position called \"{name}\" in this scenario.");

	public int MemberId(string name) => _members.TryGetValue(name, out var id) ? id : throw new KeyNotFoundException($"No member called \"{name}\" in this scenario.");

	public bool HasGroup(string name) => _groups.ContainsKey(name);

	public bool HasPosition(string name) => _positions.ContainsKey(name);

	public int NextId() => _nextId++;

	public void NameGroup(string name, int id) => _groups[name] = id;

	public void NamePosition(string name, int id) => _positions[name] = id;

	public void NameMember(string name, int id) => _members[name] = id;

	// ── The tablet's database ─────────────────────────────────────────

	public UnityDbContext Db() => Tablet.CreateDbContext();

	public void Change(Action<UnityDbContext> change)
	{
		using var db = Db();
		change(db);
		db.SaveChanges();
	}

	public Group LoadGroup(string name)
	{
		using var db = Db();
		return db.Groups.Include(g => g.Members).AsNoTracking().Single(g => g.Id == GroupId(name));
	}

	public Position LoadPosition(string name)
	{
		using var db = Db();
		return db.Positions.Include(p => p.Holders).AsNoTracking().Single(p => p.Id == PositionId(name));
	}

	public Member LoadMember(string name)
	{
		using var db = Db();
		return db.Members.AsNoTracking().Single(m => m.Id == MemberId(name));
	}

	public void Dispose()
	{
		_attendance?.Dispose();
		_email?.Dispose();
		_registrationLog?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		_complianceLog?.DisposeAsync().AsTask().GetAwaiter().GetResult();
		Tablet.Dispose();
		Mail.Dispose();
		Unity.Dispose();

		try
		{
			Directory.Delete(_directory, recursive: true);
		}
		catch (IOException)
		{
			// Best effort; the OS clears temp eventually.
		}
	}
}
