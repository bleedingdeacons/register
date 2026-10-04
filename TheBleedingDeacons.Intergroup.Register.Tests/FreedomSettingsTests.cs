using Microsoft.Extensions.Configuration;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// What happens when Freedom hands back something the tablet's own setting
/// cannot be replaced with — and the config sections that switch it on.
/// </summary>
public sealed class FreedomSettingsTests
{
	private static SmtpConfiguration Tablet() => new()
	{
		Host = "smtp.tablet.example",
		Port = 587,
		Username = "tablet@example.org",
		Password = "tablet-secret",
		FromDisplayName = "Intergroup",
		EnableSsl = true,
		TimeoutSeconds = 30,
	};

	private static Func<string, string?> Managed(params (string Key, string Value)[] values)
	{
		var map = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
		return key => map.GetValueOrDefault(key);
	}

	[Fact]
	public void WithNothingFromFreedomThereAreNoCredentials()
	{
		var smtp = FreedomSettings.Smtp(Managed());

		Assert.Equal(string.Empty, smtp.Host);
		Assert.Equal(string.Empty, smtp.Password);
		Assert.False(smtp.IsValid());
		Assert.Equal((string.Empty, string.Empty), FreedomSettings.Unity(Managed()));
		Assert.False(FreedomSettings.BetterStack(Managed()).IsValid());
		Assert.Equal(string.Empty, FreedomSettings.Compliance(Managed()));
	}

	[Fact]
	public void OnlyTheShapeOfAConnectionHasADefault()
	{
		var smtp = FreedomSettings.Smtp(Managed((FreedomSettings.SmtpHost, "smtp.site.example")));

		Assert.Equal("smtp.site.example", smtp.Host);
		Assert.Equal(587, smtp.Port);
		Assert.True(smtp.EnableSsl);
		Assert.Equal(30, smtp.TimeoutSeconds);
	}

	[Fact]
	public void EachCallStartsFromNothing()
	{
		// No caching or carry-over between reads: a value Freedom stops
		// holding is gone on the next read, not remembered.
		FreedomSettings.Smtp(Managed((FreedomSettings.SmtpHost, "smtp.site.example")));

		Assert.Equal(string.Empty, FreedomSettings.Smtp(Managed()).Host);
	}

	[Fact]
	public void TheUnitySiteAndKeyAreFreedoms()
	{
		var (baseUrl, apiKey) = FreedomSettings.Unity(Managed(
			(FreedomSettings.UnityBaseUrl, "https://aa-bristol.org/amber"),
			(FreedomSettings.UnityApiKey, "key")));

		Assert.Equal("https://aa-bristol.org/amber", baseUrl);
		Assert.Equal("key", apiKey);
	}

	[Fact]
	public void NothingManagedChangesNothing()
	{
		var config = FreedomSettings.Apply(Tablet(), Managed());

		Assert.Equal("smtp.tablet.example", config.Host);
		Assert.Equal(587, config.Port);
		Assert.True(config.EnableSsl);
	}

	[Theory]
	[InlineData("twenty-five")]
	[InlineData("587.0")]
	[InlineData("")]
	[InlineData("99999999999")] // overflows int
	public void APortThatIsNotAnIntegerFallsThrough(string port) =>
		Assert.Equal(587, FreedomSettings.Apply(Tablet(), Managed((FreedomSettings.SmtpPort, port))).Port);

	[Fact]
	public void APortWithSurroundingSpaceIsStillAPort() =>
		Assert.Equal(465, FreedomSettings.Apply(Tablet(), Managed((FreedomSettings.SmtpPort, " 465 "))).Port);

	[Theory]
	[InlineData("yes")]
	[InlineData("1")]
	[InlineData("")]
	public void AnSslFlagThatIsNotABooleanFallsThrough(string flag) =>
		Assert.True(FreedomSettings.Apply(Tablet(), Managed((FreedomSettings.SmtpEnableSsl, flag))).EnableSsl);

	[Theory]
	[InlineData("false")]
	[InlineData("False")]
	[InlineData("FALSE")]
	public void AnSslFlagIsReadWhateverItsCase(string flag) =>
		Assert.False(FreedomSettings.Apply(Tablet(), Managed((FreedomSettings.SmtpEnableSsl, flag))).EnableSsl);

	[Fact]
	public void ATimeoutThatIsNotANumberFallsThrough() =>
		Assert.Equal(30, FreedomSettings.Apply(Tablet(), Managed((FreedomSettings.SmtpTimeoutSeconds, "a while"))).TimeoutSeconds);

	/// <summary>
	/// Found, not chosen: the string settings use <c>??</c>, so an empty
	/// string held by Freedom replaces the tablet's value rather than
	/// falling through. Whether Freedom can ever hand one back is the
	/// client's business; pinned so a change is deliberate.
	/// </summary>
	[Fact]
	public void AnEmptyStringHeldByFreedomStillWins()
	{
		var config = FreedomSettings.Apply(Tablet(), Managed((FreedomSettings.SmtpHost, string.Empty)));

		Assert.Equal(string.Empty, config.Host);
	}

	[Fact]
	public void TheConfigurationIsChangedInPlaceAndReturned()
	{
		var tablet = Tablet();

		Assert.Same(tablet, FreedomSettings.Apply(tablet, Managed((FreedomSettings.SmtpHost, "smtp.site.example"))));
		Assert.Equal("smtp.site.example", tablet.Host);
	}

	[Fact]
	public void BetterStackTakesOnlyWhatFreedomHolds()
	{
		var config = FreedomSettings.Apply(
			new BetterStackConfiguration { Endpoint = "https://in.logs.example", SourceToken = "tablet-token" },
			Managed((FreedomSettings.BetterStackSourceToken, "site-token")));

		Assert.Equal("site-token", config.SourceToken);
		Assert.Equal("https://in.logs.example", config.Endpoint);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("not a url")]
	public void NoUsableSiteLeavesFreedomOff(string? baseUrl) =>
		Assert.Null(FreedomSettings.OptionsFrom(Config(("Freedom:BaseUrl", baseUrl))));

	/// <summary>
	/// Found, not chosen, and platform-dependent: on Windows a rooted path
	/// is not an absolute URI, but on Linux — and so on Android — it parses
	/// as <c>file:///amber</c>, and OptionsFrom hands back options for it.
	/// Freedom's own IsConfigured refuses a file URI, so the tablet never
	/// talks to it; what this pins is that nothing usable comes out on
	/// either platform. OptionsFrom checking the scheme would make the
	/// first branch the only one.
	/// </summary>
	[Fact]
	public void ARootedPathIsNeverAUsableSite()
	{
		var options = FreedomSettings.OptionsFrom(Config(("Freedom:BaseUrl", "/amber")));

		Assert.True(options is null || !options.IsConfigured);
	}

	[Fact]
	public void ASiteAloneGetsTheDefaults()
	{
		var options = FreedomSettings.OptionsFrom(Config(("Freedom:BaseUrl", "https://aa-bristol.org/amber")));

		Assert.NotNull(options);
		Assert.Equal("register", options.Application);
		Assert.Equal(new Uri(FreedomSettings.FreedomCallbackUri), options.CallbackUri);
		Assert.False(options.AllowInsecureBaseUrl);
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("TRUE", true)]
	[InlineData("yes", false)]
	[InlineData("1", false)]
	public void OnlyTheWordTrueAllowsAnInsecureSite(string flag, bool allowed)
	{
		var options = FreedomSettings.OptionsFrom(Config(
			("Freedom:BaseUrl", "http://localhost:8080"),
			("Freedom:AllowInsecure", flag)));

		Assert.Equal(allowed, options!.AllowInsecureBaseUrl);
	}

	[Fact]
	public void AnEmptyApplicationOrCallbackFallsBackToTheDefault()
	{
		var options = FreedomSettings.OptionsFrom(Config(
			("Freedom:BaseUrl", "https://aa-bristol.org"),
			("Freedom:Application", string.Empty),
			("Freedom:CallbackUri", string.Empty)));

		Assert.Equal("register", options!.Application);
		Assert.Equal(new Uri(FreedomSettings.FreedomCallbackUri), options.CallbackUri);
	}

	private static IConfiguration Config(params (string Key, string? Value)[] values) =>
		new ConfigurationBuilder()
			.AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
			.Build();
}
