using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.UnitTests.Fakes;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// Temp IDs must be negative and must never be reissued while a row still
/// references them — a repeat hands SQLite a duplicate primary key. Both
/// properties depend on the counter surviving a restart, which is the
/// behaviour these tests are really about.
/// </summary>
public class TemporaryIdGeneratorTests
{
	private const string CounterKey = "temp_id_counter";

	[Fact]
	public void Next_CountsDownFromMinusOne()
	{
		var generator = new TemporaryIdGenerator(new FakePreferences());

		Assert.Equal(-1, generator.Next());
		Assert.Equal(-2, generator.Next());
		Assert.Equal(-3, generator.Next());
	}

	[Fact]
	public void Next_PersistsAfterEveryCall()
	{
		var prefs = new FakePreferences();
		var generator = new TemporaryIdGenerator(prefs);

		generator.Next();
		generator.Next();

		// Persisted per call, not batched: a crash between generating an ID
		// and saving the member row must not let the next launch reissue it.
		Assert.Equal(2, prefs.SetCount);
		Assert.Equal(-2, prefs.Get(CounterKey, 0));
	}

	[Fact]
	public void Next_ResumesFromThePersistedCounterOnARestart()
	{
		var prefs = new FakePreferences();

		var firstRun = new TemporaryIdGenerator(prefs);
		firstRun.Next();
		firstRun.Next();

		// Same prefs store, new instance — as after an app restart.
		var secondRun = new TemporaryIdGenerator(prefs);

		Assert.Equal(-3, secondRun.Next());
	}

	[Theory]
	[InlineData(1)]
	[InlineData(500)]
	public void Next_IgnoresAPositiveStoredCounter(int corruptValue)
	{
		// A positive seed would hand out positive IDs, which collide silently
		// with real Unity post IDs. The guard resets to zero instead.
		var prefs = new FakePreferences();
		prefs.Seed(CounterKey, corruptValue);

		var generator = new TemporaryIdGenerator(prefs);

		Assert.Equal(-1, generator.Next());
	}

	[Fact]
	public void ResetCounter_SendsTheSequenceBackToTheStart()
	{
		var prefs = new FakePreferences();
		var generator = new TemporaryIdGenerator(prefs);
		generator.Next();
		generator.Next();

		generator.ResetCounter();

		Assert.Equal(0, prefs.Get(CounterKey, -99));
		Assert.Equal(-1, generator.Next());
	}

	[Fact]
	public void Next_StillIssuesIdsWhenPreferencesAreUnavailable()
	{
		// The counter degrades to in-memory only. Losing the persistence is
		// survivable; throwing mid-registration is not.
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };
		var generator = new TemporaryIdGenerator(prefs);

		Assert.Equal(-1, generator.Next());
		Assert.Equal(-2, generator.Next());
	}

	[Fact]
	public void Constructor_SurvivesAPreferencesReadFailure()
	{
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };

		var generator = new TemporaryIdGenerator(prefs);

		Assert.Equal(-1, generator.Next());
	}

	[Fact]
	public void Constructor_RejectsNullPreferences()
	{
		Assert.Throws<ArgumentNullException>(() => new TemporaryIdGenerator(null!));
	}

	[Theory]
	[InlineData(-1, true)]
	[InlineData(-9999, true)]
	[InlineData(0, false)]
	[InlineData(42, false)]
	public void IsTemporary_TreatsOnlyNegativeIdsAsLocal(int id, bool expected)
	{
		Assert.Equal(expected, TemporaryIdGenerator.IsTemporary(id));
	}
}
