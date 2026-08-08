using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.UnitTests.Fakes;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// The cache feeds the wording recorded against a GDPR acceptance, so a
/// corrupt or unreadable blob has to degrade to "no cache" rather than throw
/// into the consent flow. Those recovery paths are the reason the class is
/// written the way it is, and until now nothing exercised them.
/// </summary>
public class PrivacyPolicyCacheTests
{
	private const string CacheKey = "scrutiny.cachedPolicy";

	private static PrivacyPolicy SamplePolicy() => new()
	{
		Id = 4211,
		Title = "Privacy Policy",
		Version = "2.1",
		Active = true,
		Policy = "We keep your **details** safe.",
		Modified = "2026-02-14T09:30:00",
	};

	[Fact]
	public void GetCached_ReturnsNullWhenNothingHasBeenSaved()
	{
		var cache = new PrivacyPolicyCache(new FakePreferences());

		Assert.Null(cache.GetCached());
	}

	[Fact]
	public void Save_ThenGetCached_RoundTripsEveryRecordedField()
	{
		var prefs = new FakePreferences();
		var cache = new PrivacyPolicyCache(prefs);
		var policy = SamplePolicy();

		var before = DateTime.UtcNow;
		cache.Save(policy);
		var cached = cache.GetCached();

		Assert.NotNull(cached);
		Assert.Equal(policy.Id, cached!.Id);
		Assert.Equal(policy.Title, cached.Title);
		Assert.Equal(policy.Version, cached.Version);
		Assert.Equal(policy.Policy, cached.Policy);
		Assert.Equal(policy.Modified, cached.Modified);
		Assert.InRange(cached.CachedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
	}

	[Fact]
	public void Save_WritesASingleBlobSoTheEntryCannotBeHalfUpdated()
	{
		// Atomicity is the stated reason for storing JSON rather than one key
		// per field: a version stamp that disagrees with the policy body would
		// silently corrupt the audit trail.
		var prefs = new FakePreferences();
		var cache = new PrivacyPolicyCache(prefs);

		cache.Save(SamplePolicy());

		Assert.Equal(1, prefs.SetCount);
	}

	[Theory]
	[InlineData("{ not json")]
	[InlineData("[]")]
	public void GetCached_TreatsACorruptBlobAsNoCache(string corrupt)
	{
		var prefs = new FakePreferences();
		prefs.Seed(CacheKey, corrupt);
		var cache = new PrivacyPolicyCache(prefs);

		Assert.Null(cache.GetCached());
	}

	[Fact]
	public void GetCached_ReturnsNullWhenPreferencesAreUnavailable()
	{
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs cleared") };
		var cache = new PrivacyPolicyCache(prefs);

		Assert.Null(cache.GetCached());
	}

	[Fact]
	public void Save_SwallowsAPreferencesWriteFailure()
	{
		// The caller already holds the policy in memory and the next sync
		// retries the write, so a failed cache write must not abort the sync.
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("disk full") };
		var cache = new PrivacyPolicyCache(prefs);

		cache.Save(SamplePolicy());
	}

	[Fact]
	public void Clear_RemovesTheCachedEntry()
	{
		var prefs = new FakePreferences();
		var cache = new PrivacyPolicyCache(prefs);
		cache.Save(SamplePolicy());

		cache.Clear();

		Assert.Null(cache.GetCached());
		Assert.False(prefs.ContainsKey(CacheKey));
	}

	[Fact]
	public void Clear_SwallowsAPreferencesFailure()
	{
		var prefs = new FakePreferences { FailWith = new InvalidOperationException("prefs broken") };
		var cache = new PrivacyPolicyCache(prefs);

		cache.Clear();
	}

	[Fact]
	public void Save_RejectsANullPolicy()
	{
		var cache = new PrivacyPolicyCache(new FakePreferences());

		Assert.Throws<ArgumentNullException>(() => cache.Save(null!));
	}

	[Fact]
	public void Constructor_RejectsNullPreferences()
	{
		Assert.Throws<ArgumentNullException>(() => new PrivacyPolicyCache(null!));
	}
}
