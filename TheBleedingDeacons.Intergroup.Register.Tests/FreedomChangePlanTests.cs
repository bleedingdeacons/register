using System.Reflection;
using TheBleedingDeacons.Intergroup.Register.Services;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// The edges of working out what a Freedom change sets off: keys the map
/// does not know, the same key twice, nothing at all — and the guard that
/// no key Register reads is missing from the map.
/// </summary>
public sealed class FreedomChangePlanTests
{
	/// <summary>Every key FreedomSettings names, by reflection, so a new one cannot be missed.</summary>
	private static List<string> Keys() =>
		typeof(FreedomSettings).GetFields(BindingFlags.Public | BindingFlags.Static)
			// The callback URI is a constant too, but not a key Freedom holds.
			.Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name != nameof(FreedomSettings.FreedomCallbackUri))
			.Select(f => (string)f.GetRawConstantValue()!)
			.ToList();

	public static TheoryData<string> EveryKey() => new(Keys());

	[Theory]
	[MemberData(nameof(EveryKey))]
	public void EveryKeyRegisterReadsSaysWhatAChangeToItMeans(string key) =>
		Assert.True(FreedomChangePlan.KeyEffects.ContainsKey(key), $"{key} is read by Register but has no entry in FreedomChangePlan.KeyEffects");

	[Fact]
	public void TheMapNamesNoKeyRegisterDoesNotRead()
	{
		var keys = Keys();

		Assert.All(FreedomChangePlan.KeyEffects.Keys, key => Assert.Contains(key, keys));
	}

	[Fact]
	public void ThereAreKeysToCheck() => Assert.Equal(12, Keys().Count);

	[Fact]
	public void NothingChangedSetsOffNothing()
	{
		var plan = FreedomChangePlan.For([], []);

		Assert.Equal(FreedomEffects.None, plan.Effects);
		Assert.Empty(plan.Unused);
	}

	[Fact]
	public void EffectsFromSeveralKeysAreCombined()
	{
		var plan = FreedomChangePlan.For(
			[FreedomSettings.SmtpHost, FreedomSettings.UnityBaseUrl],
			[FreedomSettings.BetterStackEndpoint]);

		Assert.True(plan.Has(FreedomEffects.Smtp));
		Assert.True(plan.Has(FreedomEffects.LogShipping));
		Assert.True(plan.Has(FreedomEffects.UnitySite));
	}

	[Fact]
	public void NoneIsNeverSomethingAPlanHas() =>
		Assert.False(FreedomChangePlan.For([FreedomSettings.SmtpHost], []).Has(FreedomEffects.None));

	[Fact]
	public void AKeyBothFetchedAndRemovedIsReportedOnce()
	{
		var plan = FreedomChangePlan.For(["scrutiny.base_url"], ["scrutiny.base_url"]);

		Assert.Equal(["scrutiny.base_url"], plan.Unused);
	}

	/// <summary>Exact keys, not prefixes: a key nothing reads sets nothing off, whatever it starts with.</summary>
	[Fact]
	public void AnUnknownKeyInAKnownGroupIsUnusedNotSmtp()
	{
		var plan = FreedomChangePlan.For(["smtp.reply_to"], []);

		Assert.Equal(FreedomEffects.None, plan.Effects);
		Assert.Equal(["smtp.reply_to"], plan.Unused);
	}

	[Fact]
	public void KeysAreCaseSensitiveAsFreedomsAre() =>
		Assert.Equal(["SMTP.HOST"], FreedomChangePlan.For(["SMTP.HOST"], []).Unused);
}
