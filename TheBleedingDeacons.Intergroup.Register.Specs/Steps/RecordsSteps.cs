using Reqnroll;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>
/// What the tablet holds when the scenario begins: the records a sync from
/// Unity would have left, and the tablet's own switches.
/// </summary>
[Binding]
public sealed class RecordsSteps(World world)
{
	/// <summary>A phone number libphonenumber accepts: its own example GB mobile.</summary>
	private const string Phone = "07400 123456";

	[Given(@"^the group ""([^""]*)""$")]
	public void GroupNamed(string name) => AddGroup(name, registered: false);

	[Given(@"^the group ""([^""]*)"", already registered when the meeting started$")]
	public void GroupAlreadyRegistered(string name) => AddGroup(name, registered: true);

	[Given(@"^""([^""]*)"" is a GSR of ""([^""]*)""$")]
	public void Gsr(string name, string group) => AddMember(name, group, isGsr: true, email: null, world.NextId());

	[Given(@"^""([^""]*)"" is a GSR of ""([^""]*)"" with the email ""([^""]*)""$")]
	public void GsrWithEmail(string name, string group, string email) => AddMember(name, group, isGsr: true, email, world.NextId());

	[Given(@"^""([^""]*)"" is a member of ""([^""]*)""$")]
	public void MemberOf(string name, string group) => AddMember(name, group, isGsr: false, email: null, world.NextId());

	[Given(@"^""([^""]*)"" is a member of ""([^""]*)"" with the email ""([^""]*)""$")]
	public void MemberOfWithEmail(string name, string group, string email) => AddMember(name, group, isGsr: false, email, world.NextId());

	[Given(@"^""([^""]*)"" is added at the meeting as a GSR of ""([^""]*)""$")]
	public void AddedAtTheMeeting(string name, string group) =>
		AddMember(name, group, isGsr: true, email: null, TemporaryIdGenerator.Next());

	[Given(@"^""([^""]*)"" holds the position ""([^""]*)""$")]
	public void Holds(string name, string position)
	{
		if (!world.HasPosition(position))
		{
			var id = world.NextId();
			world.Change(db => db.Positions.Add(new Position { Id = id, ShortDescription = position }));
			world.NamePosition(position, id);
		}

		var positionId = world.PositionId(position);
		world.Change(db => db.Members.Find(world.MemberId(name))!.IntergroupPositionId = positionId);
	}

	[Given(@"^""([^""]*)"" has never accepted the privacy policy$")]
	public void NeverAccepted(string name) =>
		world.Change(db => db.Members.Find(world.MemberId(name))!.GdprAccepted = null);

	[Given(@"^""([^""]*)"" accepted version ""([^""]*)"" of the privacy policy$")]
	public void AcceptedVersion(string name, string version) =>
		world.Change(db =>
		{
			var member = db.Members.Find(world.MemberId(name))!;
			member.GdprAccepted = true;
			member.GdprAcceptedAt = DateTime.UtcNow.AddMonths(-6);
			member.GdprAcceptanceVersion = version;
		});

	[Given(@"^the cached privacy policy is version ""([^""]*)""$")]
	public void CachedVersion(string version) => world.Policy.Cached!.Version = version;

	[Given(@"^there is no cached privacy policy$")]
	public void NoPolicy() => world.Policy.Clear();

	[Given(@"^the cached privacy policy has no wording$")]
	public void EmptyPolicy() => world.Policy.Cached!.Policy = string.Empty;

	[Given(@"^positions are not registered with their group$")]
	public void NoCascade() => world.Settings.IsAutoRegisterPositionsOnGroupEnabled = false;

	[Given(@"^welcome emails are switched off$")]
	public void NoWelcome() => world.Settings.IsWelcomeEmailOnRegistrationEnabled = false;

	[Given(@"^acceptance emails are switched off$")]
	public void NoAcceptanceEmail() => world.Settings.IsComplianceAcceptanceEmailEnabled = false;

	[Given(@"^the registration log is switched off$")]
	public void NoRegistrationLog() => world.Settings.IsRegistrationEventLogEnabled = false;

	[Given(@"^the compliance log is switched off$")]
	public void NoComplianceLog() => world.Settings.IsComplianceEventLogEnabled = false;

	[Given(@"^there is no active intergroup meeting$")]
	public void NoActiveMeeting() => world.Settings.Unity.ActiveIntergroupMeetingId = null;

	[Given(@"^the tablet is offline$")]
	public void Offline() => world.Online = false;

	private void AddGroup(string name, bool registered)
	{
		var id = world.NextId();
		world.Change(db => db.Groups.Add(new Group { Id = id, Name = name, Registered = registered }));
		world.NameGroup(name, id);
	}

	private void AddMember(string name, string group, bool isGsr, string? email, int id)
	{
		// With no email, a phone number: everyone in these scenarios can be
		// reached unless a scenario says otherwise, because the gate would
		// not have let them be registered otherwise.
		var hasEmail = !string.IsNullOrEmpty(email);
		world.Change(db => db.Members.Add(new Member
		{
			Id = id,
			AnonymousName = name,
			HomeGroupId = world.GroupId(group),
			IsGsr = isGsr,
			PersonalEmail = hasEmail ? email : null,
			MobileNumber = hasEmail ? null : Phone,
		}));
		world.NameMember(name, id);
	}
}
