using Microsoft.EntityFrameworkCore;
using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;
using EntityKind = TheBleedingDeacons.Intergroup.Register.Services.RegistrationEventLog.EntityKind;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>
/// Registering and unregistering, and the log written behind them.
///
/// <para>The group is loaded with its members, as the Verify page loads it,
/// and handed to the real AttendanceService — so the cascade, the event log
/// and the welcome emails all run as they do on a tablet.</para>
/// </summary>
[Binding]
public sealed class AttendanceSteps(World world)
{
	private Services.RegistrationEventLog.ReplayResult? _replay;

	[Given(@"^""([^""]*)"" has been registered$")]
	[When(@"^""([^""]*)"" is registered$")]
	public Task Register(string name) => RegisterAsync(name, standIn: null);

	[Given(@"^""([^""]*)"" has been registered with ""([^""]*)"" standing in$")]
	[When(@"^""([^""]*)"" is registered with ""([^""]*)"" standing in$")]
	public Task RegisterWithStandIn(string name, string standIn) => RegisterAsync(name, standIn);

	[Given(@"^""([^""]*)"" has been unregistered$")]
	[When(@"^""([^""]*)"" is unregistered$")]
	public async Task Unregister(string name)
	{
		if (world.HasGroup(name))
		{
			await world.Attendance.Unregister(world.LoadGroup(name));
		}
		else
		{
			await world.Attendance.Unregister(world.LoadPosition(name));
		}
	}

	[Then(@"^""([^""]*)"" is attending$")]
	public void Attending(string name) => IsRegistered(name).ShouldBeTrue($"{name} should be attending");

	[Then(@"^""([^""]*)"" is not attending$")]
	public void NotAttending(string name) => IsRegistered(name).ShouldBeFalse($"{name} should not be attending");

	[Then(@"^""([^""]*)"" is attending with ""([^""]*)"" standing in$")]
	public void AttendingWithStandIn(string name, string standIn)
	{
		var group = world.LoadGroup(name);
		group.Registered.ShouldBeTrue();
		group.GsrProxy.ShouldBeTrue();
		group.GsrProxyName.ShouldBe(standIn);
	}

	[Then(@"^the registration log says ""([^""]*)"" is (attending|not attending)$")]
	public async Task LogSays(string name, string state)
	{
		var key = world.HasGroup(name) ? (EntityKind.Group, world.GroupId(name)) : (EntityKind.Position, world.PositionId(name));
		var states = await world.RegistrationLog.ReadLatestStatesAsync();

		states.ShouldContainKey(key);
		states[key].Registered.ShouldBe(state == "attending");
	}

	[Then(@"^the registration log is empty$")]
	public async Task LogEmpty() => (await world.RegistrationLog.ReadLatestStatesAsync()).ShouldBeEmpty();

	[Given(@"^the tablet's database loses today's registrations$")]
	public void LoseRegistrations() =>
		world.Change(db =>
		{
			foreach (var group in db.Groups)
			{
				group.Registered = false;
				group.GsrProxy = false;
				group.GsrProxyName = null;
			}

			foreach (var position in db.Positions)
			{
				position.Registered = false;
			}
		});

	[Given(@"^Unity no longer lists ""([^""]*)""$")]
	public void NoLongerListed(string name) =>
		world.Change(db =>
		{
			var id = world.GroupId(name);
			foreach (var member in db.Members.Where(m => m.HomeGroupId == id))
			{
				member.HomeGroupId = null;
			}

			db.Groups.Remove(db.Groups.Find(id)!);
		});

	[When(@"^the registration log is replayed$")]
	public async Task Replay()
	{
		await using var db = world.Db();
		_replay = await world.RegistrationLog.ReplayIntoDatabaseAsync(db);
	}

	[Then(@"^the replay skipped (\d+) entr(?:y|ies)$")]
	public void Skipped(int count) => _replay.ShouldNotBeNull().MissingEntities.ShouldBe(count);

	private async Task RegisterAsync(string name, string? standIn)
	{
		if (world.HasGroup(name))
		{
			// As the Verify page does: proxy state set on the entity, then registered.
			var group = world.LoadGroup(name);
			group.GsrProxy = standIn is not null;
			group.GsrProxyName = standIn;
			await world.Attendance.Register(group);
		}
		else
		{
			await world.Attendance.Register(world.LoadPosition(name));
		}
	}

	private bool IsRegistered(string name)
	{
		using var db = world.Db();
		return world.HasGroup(name)
			? db.Groups.AsNoTracking().Single(g => g.Id == world.GroupId(name)).Registered
			: db.Positions.AsNoTracking().Single(p => p.Id == world.PositionId(name)).Registered;
	}
}
