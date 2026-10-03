using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;
using TheBleedingDeacons.Unity.Intergroup.Entities;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The consent round's refusals and its failure paths. That a round asks
/// each member and registers them when all agree is the specification's to
/// say; this is what happens when something is missing or goes wrong.
/// </summary>
public sealed class ConsentRoundTests
{
	private readonly RecordingComplianceRegistration _compliance = new();
	private readonly List<string> _titlesAsked = [];

	private static Member Member(int id, string name = "Dave B") => new() { Id = id, AnonymousName = name };

	private Func<string, string, Task<bool>> Answer(params bool[] answers)
	{
		var queue = new Queue<bool>(answers);
		return (title, _) =>
		{
			_titlesAsked.Add(title);
			return Task.FromResult(queue.Dequeue());
		};
	}

	[Fact]
	public async Task NoCachedPolicyRefusesWithoutAskingAnyone()
	{
		var round = new ConsentRound(new FakePrivacyPolicyCache(), _compliance);

		var outcome = await round.RunAsync([Member(1)], Answer(), "this GSR");

		Assert.Equal(ConsentOutcome.NoPolicy, outcome);
		Assert.Empty(_titlesAsked);
		Assert.Empty(_compliance.Accepted);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\n\t")]
	public async Task APolicyWithNoBodyRefusesWithoutAskingAnyone(string body)
	{
		var round = new ConsentRound(FakePrivacyPolicyCache.With(body: body), _compliance);

		var outcome = await round.RunAsync([Member(1)], Answer(), "this GSR");

		Assert.Equal(ConsentOutcome.EmptyPolicy, outcome);
		Assert.Empty(_titlesAsked);
	}

	[Fact]
	public async Task NobodyToAskIsConsent()
	{
		var round = new ConsentRound(FakePrivacyPolicyCache.With(), _compliance);

		Assert.Equal(ConsentOutcome.Consented, await round.RunAsync([], Answer(), "this GSR"));
	}

	[Fact]
	public async Task TheRoundStopsAtTheFirstDecline()
	{
		var round = new ConsentRound(FakePrivacyPolicyCache.With(), _compliance);

		var outcome = await round.RunAsync([Member(1, "Ann"), Member(2, "Bob"), Member(3, "Cat")], Answer(true, false, true), "this GSR");

		Assert.Equal(ConsentOutcome.Declined, outcome);
		Assert.Equal(2, _titlesAsked.Count);
		Assert.Equal([1], _compliance.Accepted.Select(a => a.Member.Id));
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	public async Task ABlankNameUsesTheFallback(string name)
	{
		var round = new ConsentRound(FakePrivacyPolicyCache.With(), _compliance);

		await round.RunAsync([Member(1, name)], Answer(true), "this position holder");

		Assert.Equal(["Privacy policy — this position holder"], _titlesAsked);
	}

	[Fact]
	public async Task AFailedRecordDoesNotStopTheRound()
	{
		_compliance.ThrowFor = 1;
		var round = new ConsentRound(FakePrivacyPolicyCache.With(), _compliance);
		var first = Member(1);

		var outcome = await round.RunAsync([first, Member(2)], Answer(true, true), "this GSR");

		Assert.Equal(ConsentOutcome.Consented, outcome);
		Assert.Equal(2, _titlesAsked.Count);
		Assert.Equal([2], _compliance.Accepted.Select(a => a.Member.Id));

		// Not mirrored onto the entity either: it was never recorded.
		Assert.Null(first.GdprAccepted);
	}

	[Fact]
	public async Task EachAcceptanceIsRecordedAtTheCachedVersionWithItsOwnTime()
	{
		var round = new ConsentRound(FakePrivacyPolicyCache.With(version: "3.0"), _compliance);
		var member = Member(1);

		await round.RunAsync([member], Answer(true), "this GSR");

		var (_, version, at) = Assert.Single(_compliance.Accepted);
		Assert.Equal("3.0", version);
		Assert.NotNull(at);
		Assert.Equal(DateTimeKind.Utc, at!.Value.Kind);
		Assert.True(member.GdprAccepted);
		Assert.Equal(at, member.GdprAcceptedAt);
	}

	/// <summary>
	/// The entity's version is not touched: ComplianceService writes it to
	/// the row, and the cascade reads rows. Pinned because it is easy to
	/// "fix" without noticing the cascade relies on the row instead.
	/// </summary>
	[Fact]
	public async Task TheEntitysVersionIsLeftToTheDatabase()
	{
		var round = new ConsentRound(FakePrivacyPolicyCache.With(version: "3.0"), _compliance);
		var member = Member(1);
		member.GdprAcceptanceVersion = "2.0";

		await round.RunAsync([member], Answer(true), "this GSR");

		Assert.Equal("2.0", member.GdprAcceptanceVersion);
	}

	[Fact]
	public void MissingCollaboratorsAreRefused()
	{
		Assert.Throws<ArgumentNullException>(() => new ConsentRound(null!, _compliance));
		Assert.Throws<ArgumentNullException>(() => new ConsentRound(new FakePrivacyPolicyCache(), null!));
	}
}
