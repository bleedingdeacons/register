using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>
/// The two gates in front of Yes, as the Verify pages run them: the
/// contact check, and the consent round over whoever needs asking.
///
/// <para>The popup is MAUI, so the asking is scripted: everyone accepts
/// unless the scenario said they will decline. Everything else — who needs
/// asking, the refusals, the recording — is the shipping code, recording
/// into the scenario's database through the real ComplianceService.</para>
/// </summary>
[Binding]
public sealed class GateSteps(World world)
{
	private readonly List<Member> _holders = [];
	private readonly HashSet<string> _declines = new(StringComparer.Ordinal);
	private bool _standingIn;
	private string? _standInName;

	[Given(@"^a holder named ""([^""]*)"" with phone ""([^""]*)"" and email ""([^""]*)""$")]
	public void Holder(string name, string phone, string email) => _holders.Add(new Member
	{
		Id = _holders.Count + 1,
		AnonymousName = name,
		MobileNumber = phone.Length == 0 ? null : phone,
		PersonalEmail = email.Length == 0 ? null : email,
		IsGsr = true,
	});

	[Given(@"^someone is standing in without giving a name$")]
	public void StandInUnnamed() => _standingIn = true;

	[Given(@"^someone is standing in as ""([^""]*)""$")]
	public void StandIn(string name)
	{
		_standingIn = true;
		_standInName = name;
	}

	[Then(@"^the group (can|cannot) be registered$")]
	public void Verdict(string verdict) =>
		RegistrationGate.CanRegisterGroup(_holders, _standingIn, _standInName).ShouldBe(verdict == "can");

	[Given(@"^""([^""]*)"" will decline$")]
	public void WillDecline(string name) => _declines.Add(name);

	[When(@"^consent is sought from the GSRs of ""([^""]*)""$")]
	public Task FromGsrs(string group)
	{
		var needing = RegistrationGate.NeedingConsent(Gsrs(group), world.Policy.Cached?.Version);
		return RunRound(needing);
	}

	[When(@"^consent is sought from the officers ""([^""]*)"" brings with it$")]
	public async Task FromCascade(string group)
	{
		var needing = await RegistrationGate.CascadedHoldersNeedingConsentAsync(Gsrs(group), world.Positions, world.Policy.Cached?.Version);
		await RunRound(needing);
	}

	[Then(@"^""([^""]*)"" was asked$")]
	public void WasAsked(string name) => world.AskedForConsent.ShouldContain(name);

	[Then(@"^""([^""]*)"" was not asked$")]
	public void WasNotAsked(string name) => world.AskedForConsent.ShouldNotContain(name);

	[Then(@"^nobody was asked$")]
	public void NobodyAsked() => world.AskedForConsent.ShouldBeEmpty();

	[Then(@"^consent was given$")]
	public void Given() => world.ConsentOutcome.ShouldBe(ConsentOutcome.Consented);

	[Then(@"^consent was refused$")]
	public void Refused() => world.ConsentOutcome.ShouldBe(ConsentOutcome.Declined);

	[Then(@"^consent could not be sought because there is no policy$")]
	public void NoPolicy() => world.ConsentOutcome.ShouldBe(ConsentOutcome.NoPolicy);

	[Then(@"^consent could not be sought because the policy is empty$")]
	public void EmptyPolicy() => world.ConsentOutcome.ShouldBe(ConsentOutcome.EmptyPolicy);

	private List<Member> Gsrs(string group) =>
		world.LoadGroup(group).Members.Where(m => m.IsGsr).OrderBy(m => m.Id).ToList();

	/// <summary>As the Verify page does: the round only runs when someone needs asking.</summary>
	private async Task RunRound(IReadOnlyList<Member> needing)
	{
		if (needing.Count == 0)
		{
			world.ConsentOutcome = ConsentOutcome.Consented;
			return;
		}

		world.ConsentOutcome = await world.Consent.RunAsync(needing, Ask, "this GSR");
	}

	private Task<bool> Ask(string title, string body)
	{
		body.ShouldNotBeNullOrWhiteSpace();

		var name = title[(title.LastIndexOf(" — ", StringComparison.Ordinal) + 3)..];
		world.AskedForConsent.Add(name);
		return Task.FromResult(!_declines.Contains(name));
	}
}
