using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Register.Specs.Support;
using UnityModels = TheBleedingDeacons.Unity.Models;

namespace TheBleedingDeacons.Intergroup.Register.Specs.Steps;

/// <summary>What Unity holds, and what the tablet holds after it syncs.</summary>
[Binding]
public sealed class SyncSteps(World world)
{
	[Given(@"^Unity holds the group ""([^""]*)""$")]
	public void UnityGroup(string name)
	{
		var id = 500 + world.NextId();
		world.Unity.Groups.Add(new UnityModels.Group { Id = id, Title = name });
		world.NameGroup(name, id);
	}

	[Given(@"^Unity holds (\d+) members of ""([^""]*)""$")]
	public void UnityMembers(int count, string group)
	{
		for (var i = 0; i < count; i++)
		{
			world.Unity.Members.Add(new UnityModels.Member
			{
				Id = 1000 + world.NextId(),
				AnonymousName = $"Member {i + 1}",
				HomeGroupId = world.GroupId(group),
			});
		}
	}

	[Given(@"^Unity holds the member ""([^""]*)"" whose home group is not in the district$")]
	public void UnityMemberElsewhere(string name)
	{
		var id = 1000 + world.NextId();
		world.Unity.Members.Add(new UnityModels.Member { Id = id, AnonymousName = name, HomeGroupId = 9999 });
		world.NameMember(name, id);
	}

	[Given(@"^Unity serves (\d+) to a page$")]
	public void PageSize(int size) => world.Unity.PageSize = size;

	[Given(@"^Unity refuses page (\d+)$")]
	public void RefusePage(int page) => world.Unity.RefusePage = page;

	[When(@"^the tablet syncs$")]
	public async Task Syncs()
	{
		try
		{
			await world.Sync.SyncAsync();
		}
		catch (Exception ex)
		{
			world.Failure = ex;
		}
	}

	[Then(@"^the tablet holds (\d+) members?$")]
	public void HoldsMembers(int count)
	{
		using var db = world.Db();
		db.Members.Count().ShouldBe(count);
	}

	[Then(@"^the tablet does not hold the group ""([^""]*)""$")]
	public void DoesNotHold(string name)
	{
		using var db = world.Db();
		db.Groups.Any(g => g.Name == name).ShouldBeFalse();
	}

	[Then(@"^the tablet still holds the group ""([^""]*)""$")]
	public void StillHolds(string name)
	{
		using var db = world.Db();
		db.Groups.Any(g => g.Name == name).ShouldBeTrue();
	}

	[Then(@"^""([^""]*)"" has no home group$")]
	public void NoHomeGroup(string name) => world.LoadMember(name).HomeGroupId.ShouldBeNull();

	[Then(@"^the sync failed$")]
	public void Failed() => world.Failure.ShouldNotBeNull();

	[Then(@"^Unity was asked for (\d+) pages of members$")]
	public void PagesAsked(int pages) =>
		world.Unity.Calls.Count(c => c.Method == "GET" && c.Path.EndsWith("/members", StringComparison.Ordinal)).ShouldBe(pages);
}
