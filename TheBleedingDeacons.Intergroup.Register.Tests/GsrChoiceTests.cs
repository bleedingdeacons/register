using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// Which of a group's GSRs its registration goes to Unity under: the same
/// one every time, whatever order the database hands them back in.
/// </summary>
public sealed class GsrChoiceTests
{
	private static readonly HashSet<int> NoneFailed = [];

	private static Member Gsr(int id) => new() { Id = id, AnonymousName = $"GSR {id}", IsGsr = true };

	[Fact]
	public void NoGsrIsNone() =>
		Assert.Null(ReconciliationService.ChooseGsr([], NoneFailed));

	[Fact]
	public void TheLowestIdWhicheverOrderTheyComeIn()
	{
		Assert.Equal(3, ReconciliationService.ChooseGsr([Gsr(9), Gsr(3), Gsr(5)], NoneFailed)!.Id);
		Assert.Equal(3, ReconciliationService.ChooseGsr([Gsr(5), Gsr(9), Gsr(3)], NoneFailed)!.Id);
	}

	[Fact]
	public void OneAlreadyOnUnityBeforeOneStillToBeCreated() =>
		Assert.Equal(12, ReconciliationService.ChooseGsr([Gsr(-1), Gsr(12)], NoneFailed)!.Id);

	[Fact]
	public void AmongThoseStillToBeCreatedTheFirstMade() =>
		Assert.Equal(-1, ReconciliationService.ChooseGsr([Gsr(-4), Gsr(-1)], NoneFailed)!.Id);

	[Fact]
	public void OneWhoseCreateFailedIsPassedOverForOneThatCanStand() =>
		Assert.Equal(-2, ReconciliationService.ChooseGsr([Gsr(-1), Gsr(-2)], new HashSet<int> { -1 })!.Id);

	[Fact]
	public void IfEveryCreateFailedTheCallerStillHearsOfOne() =>
		Assert.Equal(-1, ReconciliationService.ChooseGsr([Gsr(-1)], new HashSet<int> { -1 })!.Id);
}
