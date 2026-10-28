using System.Net;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>
/// Starting and finishing the meeting: the snapshot taken at the start,
/// the reconcile at the end, and what Unity was told in between — read off
/// the requests the real client made.
/// </summary>
[Binding]
public sealed class MeetingSteps(World world)
{
	private static readonly Dictionary<string, string> Routes = new(StringComparer.Ordinal)
	{
		["registrations of groups"] = "/register-group",
		["registrations of officers"] = "/register-officer",
		["new members"] = "/members/create",
		["consent records"] = "/compliance",
		["member updates"] = "/update",
	};

	[Given(@"^the meeting has started$")]
	public Task Started() => world.Snapshots.CaptureAsync();

	[Given(@"^the tablet has no snapshot$")]
	public void NoSnapshot() => world.Change(db => db.EntitySnapshots.RemoveRange(db.EntitySnapshots));

	[When(@"^the meeting is finished$")]
	public async Task Finished()
	{
		try
		{
			world.Reconciled = await world.Reconciliation.ReconcileAsync();
		}
		catch (UnitySiteChangedException ex)
		{
			// The one refusal a scenario sets out to provoke; anything
			// else still fails the scenario where it is thrown.
			world.Failure = ex;
		}
	}

	[Given(@"^the tablet's records came from ""([^""]*)""$")]
	public void RecordsCameFrom(string site) => world.RecordsSite.Site = site;

	[Given(@"^Freedom now points Unity at ""([^""]*)""$")]
	public void FreedomPointsAt(string site) => world.Settings.Unity.BaseUrl = site;

	[Then(@"^finishing is refused because the Unity site changed$")]
	public void RefusedForSite()
	{
		world.Failure.ShouldBeOfType<UnitySiteChangedException>();
		world.Reconciled.ShouldBeNull();
	}

	[Given(@"^Unity (refuses|already has|cannot take) (registrations of groups|registrations of officers|new members|consent records|member updates)$")]
	public void Unity(string how, string what) =>
		world.Unity.Refusals[Routes[what]] = how switch
		{
			"refuses" => (HttpStatusCode.UnprocessableEntity, "rest_invalid_param"),
			"already has" => (HttpStatusCode.Conflict, "already_registered"),
			// A server in trouble, which a retry may get past.
			_ => (HttpStatusCode.ServiceUnavailable, "server_error"),
		};

	[When(@"^the meeting is finished anyway$")]
	public async Task FinishedAnyway() =>
		await world.Reconciliation.AcceptRefusedConsentAsync(world.Reconciled.ShouldNotBeNull());

	[Then(@"^""([^""]*)""'s (consent|mobile number) is named as refused$")]
	public void NamedAsRefused(string name, string what)
	{
		var refusal = world.Reconciled.ShouldNotBeNull().Refusals.ShouldHaveSingleItem();
		refusal.MemberName.ShouldBe(name);
		refusal.Reason.ShouldNotBeNullOrWhiteSpace();
		if (what == "consent")
		{
			refusal.Kind.ShouldBe(ReconciliationService.ReconcileRefusalKind.Consent);
		}
		else
		{
			refusal.Kind.ShouldBe(ReconciliationService.ReconcileRefusalKind.MemberUpdate);
			refusal.What.ShouldBe(what);
		}
	}

	[Then(@"^nothing is named as refused$")]
	public void NothingRefused() => world.Reconciled.ShouldNotBeNull().Refusals.ShouldBeEmpty();

	[Then(@"^the meeting (can|cannot) be finished anyway$")]
	public void CanFinishAnyway(string can) =>
		world.Reconciled.ShouldNotBeNull().OnlyRefusedConsentRemains.ShouldBe(can == "can");

	[Then(@"^the compliance log is kept$")]
	public void ComplianceLogKept() => world.ComplianceLog.HasPendingEntries().ShouldBeTrue();

	[Then(@"^the compliance log is archived on the tablet$")]
	public void ComplianceLogArchived()
	{
		world.ComplianceLog.HasPendingEntries().ShouldBeFalse();
		world.ArchivedComplianceLogs().ShouldHaveSingleItem();
	}

	[When(@"^Unity accepts (registrations of groups|registrations of officers|new members|consent records|member updates) again$")]
	public void UnityAcceptsAgain(string what) => world.Unity.Refusals.Remove(Routes[what]);

	[Then(@"^the last registration of ""([^""]*)"" was represented by member (\d+)$")]
	public void LastGroupRegistration(string group, int memberId)
	{
		var id = world.GroupId(group);
		world.Unity.Posts("/register-group").Last(c => c.Int("group_id") == id).Int("member_id").ShouldBe(memberId);
	}

	[Then(@"^the tablet holds ""([^""]*)"" as member (\d+)$")]
	public void HeldAs(string name, int memberId) => world.CurrentMemberId(name).ShouldBe(memberId);

	[When(@"^""([^""]*)""'s mobile number is changed to ""([^""]*)""$")]
	public void MobileChanged(string name, string mobile) =>
		world.Change(db => db.Members.Find(world.MemberId(name))!.MobileNumber = mobile);

	/// <summary>What the position editor's Done does to a holder it removes.</summary>
	[When(@"^""([^""]*)"" is removed as the holder of ""([^""]*)""$")]
	public void RemovedAsHolder(string name, string position) =>
		world.Change(db =>
		{
			var member = db.Members.Find(world.MemberId(name))!;
			member.IntergroupPositionId.ShouldBe(world.PositionId(position));
			member.IntergroupPositionId = null;
			member.IntergroupPositionRotation = null;
		});

	[When(@"^""([^""]*)"" is taken out of their home group$")]
	public void TakenOutOfHomeGroup(string name) =>
		world.Change(db => db.Members.Find(world.MemberId(name))!.HomeGroupId = null);

	[Then(@"^Unity is sent nothing$")]
	public void SentNothing() => world.Unity.Calls.Where(c => c.Method == "POST").ShouldBeEmpty();

	[Then(@"^Unity is told ""([^""]*)"" attended, represented by ""([^""]*)""$")]
	public void GroupAttended(string group, string gsr)
	{
		var call = GroupRegistration(group);
		call.Int("member_id").ShouldBe(world.MemberId(gsr));
		call.Str("gsr_name").ShouldBe(gsr);
		call.Bool("gsr_proxy").ShouldBe(false);
	}

	[Then(@"^Unity is told ""([^""]*)"" attended, represented by member (\d+)$")]
	public void GroupAttendedById(string group, int memberId) =>
		GroupRegistration(group).Int("member_id").ShouldBe(memberId);

	[Then(@"^Unity is told ""([^""]*)"" attended with ""([^""]*)"" standing in$")]
	public void GroupAttendedWithStandIn(string group, string standIn)
	{
		var call = GroupRegistration(group);
		call.Bool("gsr_proxy").ShouldBe(true);
		call.Str("gsr_proxy_name").ShouldBe(standIn);
	}

	[Then(@"^Unity is told ""([^""]*)"" is no longer attending$")]
	public void GroupAbsent(string group) =>
		world.Unity.Posts("/unregister-group").ShouldHaveSingleItem().Int("group_id").ShouldBe(world.GroupId(group));

	[Then(@"^Unity is told ""([^""]*)"" attended as ""([^""]*)""$")]
	public void OfficerAttended(string member, string position)
	{
		var call = world.Unity.Posts("/register-officer").ShouldHaveSingleItem();
		call.Int("officer_id").ShouldBe(world.MemberId(member));
		call.Str("position_name").ShouldBe(position);
		call.Str("officer_name").ShouldBe(member);
	}

	[Then(@"^Unity is asked to create ""([^""]*)""$")]
	public void AskedToCreate(string name) =>
		world.Unity.Posts("/members/create").ShouldHaveSingleItem().Str("anonymous_name").ShouldBe(name);

	[Then(@"^Unity is not told about ""([^""]*)""$")]
	public void NotTold(string group)
	{
		var id = world.GroupId(group);
		world.Unity.Calls
			.Where(c => c.Method == "POST" && c.Path.Contains("-group", StringComparison.Ordinal))
			.ShouldNotContain(c => c.Int("group_id") == id);
	}

	[Then(@"^Unity is told only ""([^""]*)""'s new mobile number$")]
	public void OnlyMobile(string name)
	{
		var call = world.Unity.Posts($"/members/{world.MemberId(name)}/update").ShouldHaveSingleItem();
		call.Str("mobile_number").ShouldBe(world.LoadMember(name).MobileNumber);
		call.Has("anonymous_name").ShouldBeFalse();
		call.Has("personal_email").ShouldBeFalse();
		call.Has("home_group_id").ShouldBeFalse();
	}

	[Then(@"^Unity is told ""([^""]*)"" no longer holds a position$")]
	public void NoLongerHolds(string name)
	{
		var call = world.Unity.Posts($"/members/{world.MemberId(name)}/update").ShouldHaveSingleItem();
		call.Int("intergroup_position_id").ShouldBe(0);
		call.Str("intergroup_position_rotation").ShouldBe(string.Empty);
		call.Has("home_group_id").ShouldBeFalse();
	}

	[Then(@"^Unity is told ""([^""]*)"" no longer has a home group$")]
	public void NoLongerHasHomeGroup(string name)
	{
		var call = world.Unity.Posts($"/members/{world.MemberId(name)}/update").ShouldHaveSingleItem();
		call.Int("home_group_id").ShouldBe(0);
		call.Has("intergroup_position_id").ShouldBeFalse();
	}

	[Then(@"^Unity is told ""([^""]*)"" accepted version ""([^""]*)"" of policy (\d+)$")]
	public void ConsentSent(string name, string version, int policyId)
	{
		var call = world.Unity.Posts($"/members/{world.CurrentMemberId(name)}/compliance").ShouldHaveSingleItem();
		call.Bool("accepted").ShouldBe(true);
		call.Str("version").ShouldBe(version);
		call.Int("policy_id").ShouldBe(policyId);
		call.Str("accepted_at").ShouldNotBeNullOrEmpty();
	}

	[Then(@"^the meeting finished with (\d+) errors? and (\d+) warnings?$")]
	public void Outcome(int errors, int warnings)
	{
		var result = world.Reconciled.ShouldNotBeNull();
		result.ApiErrors.ShouldBe(errors);
		result.ApiWarnings.ShouldBe(warnings);
	}

	[Then(@"^both logs are cleared$")]
	public void LogsCleared()
	{
		world.RegistrationLog.HasPendingEntries().ShouldBeFalse();
		world.ComplianceLog.HasPendingEntries().ShouldBeFalse();
	}

	[Then(@"^the logs are kept for next time$")]
	public void LogsKept() => world.RegistrationLog.HasPendingEntries().ShouldBeTrue();

	private FakeUnity.Call GroupRegistration(string group) =>
		world.Unity.Posts("/register-group").ShouldHaveSingleItem()
			.ShouldSatisfyAllConditions(c => c.Int("group_id").ShouldBe(world.GroupId(group)));
}

file static class Fluent
{
	/// <summary>Assert, then hand the value on.</summary>
	public static T ShouldSatisfyAllConditions<T>(this T value, Action<T> conditions)
	{
		conditions(value);
		return value;
	}
}
