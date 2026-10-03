using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The edges of the registration gate: what counts as missing, what counts
/// as a different version, and the inputs nobody meant to send.
/// </summary>
public sealed class RegistrationGateTests
{
	private static Member Gsr(string name = "Dave B", string? mobile = "07700900123", string? email = null) =>
		new() { Id = 1, AnonymousName = name, MobileNumber = mobile, PersonalEmail = email };

	[Fact]
	public void NoHoldersAtAllIsNotContactable() =>
		Assert.False(RegistrationGate.HasContactableHolder([]));

	[Fact]
	public void PhoneOnlyIsEnough() =>
		Assert.True(RegistrationGate.HasContactableHolder([Gsr(mobile: "07700900123", email: null)]));

	[Fact]
	public void EmailOnlyIsEnough() =>
		Assert.True(RegistrationGate.HasContactableHolder([Gsr(mobile: null, email: "dave@example.org")]));

	[Fact]
	public void ANameWithNoWayToReachThemIsNot() =>
		Assert.False(RegistrationGate.HasContactableHolder([Gsr(mobile: null, email: null)]));

	[Fact]
	public void AContactWithNoNameIsNot() =>
		Assert.False(RegistrationGate.HasContactableHolder([Gsr(name: string.Empty)]));

	[Fact]
	public void OneContactableHolderAmongSeveralIsEnough() =>
		Assert.True(RegistrationGate.HasContactableHolder([Gsr(mobile: null), Gsr(name: string.Empty), Gsr()]));

	/// <summary>
	/// Found, not chosen: the check is IsNullOrEmpty, so whitespace passes.
	/// It is pinned here so that changing it is a decision with a red test
	/// attached, not a side effect.
	/// </summary>
	[Fact]
	public void WhitespaceCountsAsPresent()
	{
		Assert.True(RegistrationGate.HasContactableHolder([Gsr(name: " ")]));
		Assert.True(RegistrationGate.HasContactableHolder([Gsr(mobile: " ")]));
	}

	[Theory]
	[InlineData(false, null, true)]
	[InlineData(false, "", true)]
	[InlineData(true, "Sam", true)]
	[InlineData(true, null, false)]
	[InlineData(true, "", false)]
	[InlineData(true, "   ", false)]
	public void AStandInMustBeNamed(bool standingIn, string? name, bool satisfied) =>
		Assert.Equal(satisfied, RegistrationGate.StandInSatisfied(standingIn, name));

	[Fact]
	public void AGroupNeedsBoth()
	{
		Assert.True(RegistrationGate.CanRegisterGroup([Gsr()], standingIn: true, standinName: "Sam"));
		Assert.False(RegistrationGate.CanRegisterGroup([Gsr()], standingIn: true, standinName: null));
		Assert.False(RegistrationGate.CanRegisterGroup([Gsr(mobile: null)], standingIn: false, standinName: null));
	}

	[Fact]
	public void APositionNeedsOnlyAContactableHolder() =>
		Assert.True(RegistrationGate.CanRegisterPosition([Gsr()]));

	[Fact]
	public void NullHoldersAreRefusedLoudly()
	{
		Assert.Throws<ArgumentNullException>(() => RegistrationGate.HasContactableHolder(null!));
		Assert.Throws<ArgumentNullException>(() => RegistrationGate.NeedingConsent(null!, "2.1"));
	}

	[Fact]
	public void NeverAcceptedNeedsConsentWhateverTheCache()
	{
		var member = Gsr();
		member.GdprAccepted = null;

		Assert.Single(RegistrationGate.NeedingConsent([member], "2.1"));
		Assert.Single(RegistrationGate.NeedingConsent([member], null));
	}

	[Fact]
	public void AcceptedFalseNeedsConsent()
	{
		var member = Gsr();
		member.GdprAccepted = false;
		member.GdprAcceptanceVersion = "2.1";

		Assert.Single(RegistrationGate.NeedingConsent([member], "2.1"));
	}

	[Fact]
	public void AcceptedWithNoRecordedVersionNeedsConsentOnceThereIsOne()
	{
		var member = Accepted(version: null);

		Assert.Single(RegistrationGate.NeedingConsent([member], "2.1"));
	}

	[Fact]
	public void WithNoCachedVersionAnyAcceptanceIsEnough()
	{
		Assert.Empty(RegistrationGate.NeedingConsent([Accepted("1.0")], null));
		Assert.Empty(RegistrationGate.NeedingConsent([Accepted("1.0")], string.Empty));
		Assert.Empty(RegistrationGate.NeedingConsent([Accepted("1.0")], "   "));
	}

	[Theory]
	[InlineData("2.1", "2.1", false)]
	[InlineData("2.0", "2.1", true)]
	[InlineData("3.0", "2.1", true)]   // newer is still different
	[InlineData("v2.1", "2.1", true)]
	[InlineData("2.1 ", "2.1", true)]  // no trimming
	[InlineData("Draft", "draft", true)] // ordinal: case matters
	public void DifferentNotOlder(string recorded, string cached, bool needsConsent) =>
		Assert.Equal(needsConsent, RegistrationGate.NeedingConsent([Accepted(recorded)], cached).Count == 1);

	[Fact]
	public async Task CascadeSkipsGsrsWithNoPositionAndPositionsThatAreGone()
	{
		var positions = new FakePositions();
		positions.Add(new Position { Id = 9, Holders = [Gsr()] });

		var gsrs = new[]
		{
			Gsr(),                                              // holds nothing
			WithPosition(Gsr(), 404),                           // position no longer exists
			WithPosition(Gsr(), 9),
			WithPosition(Gsr(), 9),                             // same position twice: asked once
		};

		var needing = await RegistrationGate.CascadedHoldersNeedingConsentAsync(gsrs, positions, "2.1");

		Assert.Single(needing);
		Assert.Equal([404, 9], positions.Asked);
	}

	[Fact]
	public async Task CascadeTreatsAPositionWithNoHolderListAsEmpty()
	{
		var positions = new FakePositions();
		positions.Add(new Position { Id = 9, Holders = null! });

		var needing = await RegistrationGate.CascadedHoldersNeedingConsentAsync([WithPosition(Gsr(), 9)], positions, "2.1");

		Assert.Empty(needing);
	}

	private static Member Accepted(string? version)
	{
		var member = Gsr();
		member.GdprAccepted = true;
		member.GdprAcceptanceVersion = version;
		return member;
	}

	private static Member WithPosition(Member member, int positionId)
	{
		member.IntergroupPositionId = positionId;
		return member;
	}

	/// <summary>Only the lookup the cascade uses; everything else is not this test's business.</summary>
	private sealed class FakePositions : IPositionRepository
	{
		private readonly Dictionary<int, Position> _positions = [];

		public List<int> Asked { get; } = [];

		public void Add(Position position) => _positions[position.Id] = position;

		public Task<Position?> GetByIdWithHoldersAsync(int id, CancellationToken ct = default)
		{
			Asked.Add(id);
			return Task.FromResult(_positions.GetValueOrDefault(id));
		}

		public Task<List<Position>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();

		public Task<Position?> GetByIdAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();

		public Task<List<Position>> GetFilledPositionsAsync(CancellationToken ct = default) => throw new NotSupportedException();

		public Task<List<Position>> GetVacantPositionsAsync(CancellationToken ct = default) => throw new NotSupportedException();

		public Task<List<Position>> SearchAsync(string searchTerm, CancellationToken ct = default) => throw new NotSupportedException();
	}
}
