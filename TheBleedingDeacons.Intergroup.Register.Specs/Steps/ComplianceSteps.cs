using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>Consent recorded, withdrawn, logged and replayed — through the real ComplianceService.</summary>
[Binding]
public sealed class ComplianceSteps(World world)
{
	[Given(@"^""([^""]*)"" has accepted the privacy policy$")]
	[When(@"^""([^""]*)"" accepts the privacy policy$")]
	public Task Accepts(string name)
	{
		var policy = world.Policy.Cached.ShouldNotBeNull();
		return world.Compliance.RecordAcceptance(world.LoadMember(name), policy.Version, policy.Policy);
	}

	[When(@"^""([^""]*)"" withdraws their consent$")]
	public Task Withdraws(string name) => world.Compliance.RecordRevocation(world.LoadMember(name));

	[Then(@"^""([^""]*)"" has accepted version ""([^""]*)""$")]
	public void HasAccepted(string name, string version)
	{
		var member = world.LoadMember(name);
		member.GdprAccepted.ShouldBe(true);
		member.GdprAcceptanceVersion.ShouldBe(version);
		member.GdprAcceptedAt.ShouldNotBeNull();
	}

	[Then(@"^""([^""]*)"" has no acceptance on record$")]
	public void NoAcceptance(string name)
	{
		var member = world.LoadMember(name);
		member.GdprAccepted.ShouldBe(false);
		member.GdprAcceptanceVersion.ShouldBeNull();
		member.GdprAcceptanceStatement.ShouldBeNull();
		member.GdprAcceptancePolicyId.ShouldBeNull();
	}

	[Then(@"^the acceptance records the policy's id and wording$")]
	public void RecordsPolicy()
	{
		using var db = world.Db();
		var member = db.Members.Single(m => m.GdprAccepted == true);
		member.GdprAcceptancePolicyId.ShouldBe(world.Policy.Cached!.Id);
		member.GdprAcceptanceStatement.ShouldBe(world.Policy.Cached.Policy);
		member.GdprAcceptanceMethod.ShouldBe("register-app");
	}

	[Then(@"^the compliance log says ""([^""]*)"" accepted version ""([^""]*)""$")]
	public async Task LogSays(string name, string version)
	{
		var states = await world.ComplianceLog.ReadLatestStatesAsync();
		var entry = states[world.MemberId(name)];
		entry.Accepted.ShouldBeTrue();
		entry.Version.ShouldBe(version);
	}

	[Then(@"^the compliance log is empty$")]
	public async Task LogEmpty() => (await world.ComplianceLog.ReadLatestStatesAsync()).ShouldBeEmpty();

	[Given(@"^the tablet's database loses today's acceptances$")]
	public void LoseAcceptances() =>
		world.Change(db =>
		{
			foreach (var member in db.Members)
			{
				member.GdprAccepted = null;
				member.GdprAcceptedAt = null;
				member.GdprAcceptanceVersion = null;
				member.GdprAcceptanceMethod = null;
				member.GdprAcceptanceStatement = null;
				member.GdprAcceptancePolicyId = null;
			}
		});

	[When(@"^the compliance log is replayed$")]
	public async Task Replay()
	{
		await using var db = world.Db();
		await world.ComplianceLog.ReplayIntoDatabaseAsync(db);
	}
}
