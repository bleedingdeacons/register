using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Services;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Tests.Support;
using TheBleedingDeacons.Intergroup.Register.Utilities;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>The markdown the privacy policy arrives in, and what it must not let through.</summary>
public sealed class BasicMarkdownConverterTests
{
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("\n\n")]
	public void NothingBecomesNothing(string markdown) =>
		Assert.Equal(string.Empty, BasicMarkdownConverter.Convert(markdown));

	[Fact]
	public void HtmlInTheSourceIsEscaped()
	{
		var html = BasicMarkdownConverter.Convert("<script>alert(\"x\")</script> & more");

		Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
		Assert.Contains("&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; more", html, StringComparison.Ordinal);
	}

	[Fact]
	public void AQuoteInALinkCannotBreakOutOfTheAttribute()
	{
		var html = BasicMarkdownConverter.Convert("[click](https://example.org\" onmouseover=\"x)");

		Assert.DoesNotContain("\" onmouseover", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("[click](javascript:alert(1))")]
	[InlineData("[click](JavaScript:alert(1))")]
	[InlineData("[click](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
	[InlineData("[click](vbscript:msgbox(1))")]
	[InlineData("[click]( javascript:alert(1))")]
	public void ALinkToAnythingButTheWebOrMailIsOnlyItsText(string markdown)
	{
		var html = BasicMarkdownConverter.Convert(markdown);

		Assert.DoesNotContain("<a", html, StringComparison.Ordinal);
		Assert.DoesNotContain("script:", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("data:", html, StringComparison.OrdinalIgnoreCase);
		Assert.StartsWith("<p>click", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("[policy](javascript:void)")]
	[InlineData("[policy](/privacy)")]
	public void TheTextOfARefusedLinkIsKept(string markdown) =>
		Assert.Equal("<p>policy</p>", BasicMarkdownConverter.Convert(markdown));

	[Theory]
	[InlineData("https://example.org/privacy")]
	[InlineData("http://example.org")]
	[InlineData("HTTPS://example.org")]
	[InlineData("mailto:privacy@example.org")]
	public void ALinkToTheWebOrToMailStillWorks(string url) =>
		Assert.Equal($"<p><a href=\"{url}\">click</a></p>", BasicMarkdownConverter.Convert($"[click]({url})"));

	[Theory]
	[InlineData("**bold without an end")]
	[InlineData("*italic without an end")]
	[InlineData("[text without a target]")]
	public void UnbalancedMarkersAreLeftAsTheyAre(string markdown) =>
		Assert.Equal($"<p>{markdown}</p>", BasicMarkdownConverter.Convert(markdown));

	[Fact]
	public void LineEndingsFromAnyPlatformAreTheSame() =>
		Assert.Equal(
			BasicMarkdownConverter.Convert("# Title\nBody"),
			BasicMarkdownConverter.Convert("# Title\r\nBody"));

	[Fact]
	public void SevenHashesIsNotAHeading() =>
		Assert.StartsWith("<p>", BasicMarkdownConverter.Convert("####### too deep"), StringComparison.Ordinal);
}

/// <summary>The Better Stack token goes nowhere it should not (register#44, #47).</summary>
public sealed class LogShippingTests
{
	private readonly RecordingLogShipper _shipper = new();

	[Theory]
	[InlineData("http://in.logs.example")]
	[InlineData("ftp://in.logs.example")]
	[InlineData("")]
	public void AnEndpointThatIsNotHttpsStopsShippingRatherThanSendingTheToken(string endpoint)
	{
		_shipper.Ship(new BetterStackConfiguration { Endpoint = endpoint, SourceToken = "secret" });

		Assert.Null(Assert.Single(_shipper.Reconfigured));
	}

	[Fact]
	public void NoTokenStopsShipping()
	{
		_shipper.Ship(new BetterStackConfiguration { Endpoint = "https://in.logs.example", SourceToken = string.Empty });

		Assert.Null(Assert.Single(_shipper.Reconfigured));
	}

	[Fact]
	public void ASchemeLessEndpointIsGivenHttpsAndShips()
	{
		var config = new BetterStackConfiguration { Endpoint = "in.logs.example", SourceToken = "secret" };

		_shipper.Ship(config);

		Assert.Same(config, Assert.Single(_shipper.Reconfigured));
	}

	[Fact]
	public void NullsAreRefused()
	{
		Assert.Throws<ArgumentNullException>(() => LogShipping.Ship(null!, new BetterStackConfiguration()));
		Assert.Throws<ArgumentNullException>(() => _shipper.Ship(null!));
	}
}

/// <summary>
/// The temporary id counter when its store misbehaves. Static state, so
/// every test starts by handing it a fresh store.
/// </summary>
public sealed class TemporaryIdGeneratorTests
{
	[Fact]
	public void ItResumesFromTheStore()
	{
		var store = new FakeTemporaryIdStore { Value = -7 };
		TemporaryIdGenerator.Use(store);

		Assert.Equal(-8, TemporaryIdGenerator.Next());
		Assert.Equal(-8, store.Value);
	}

	[Fact]
	public void APositiveStoredValueRestartsAtZeroRatherThanHandingOutRealIds()
	{
		TemporaryIdGenerator.Use(new FakeTemporaryIdStore { Value = 12 });

		Assert.Equal(-1, TemporaryIdGenerator.Next());
	}

	[Fact]
	public void AnUnreadableStoreRestartsAtZero()
	{
		TemporaryIdGenerator.Use(new FakeTemporaryIdStore { Value = -7, FailOnGet = true });

		Assert.Equal(-1, TemporaryIdGenerator.Next());
	}

	[Fact]
	public void AFailedWriteDoesNotStopTheCount()
	{
		var store = new FakeTemporaryIdStore { FailOnSet = true };
		TemporaryIdGenerator.Use(store);

		Assert.Equal(-1, TemporaryIdGenerator.Next());
		Assert.Equal(-2, TemporaryIdGenerator.Next());
		Assert.Equal(0, store.Value);
	}

	[Fact]
	public void ResetGoesBackToZeroEvenIfItCannotSaveIt()
	{
		TemporaryIdGenerator.Use(new FakeTemporaryIdStore { Value = -3, FailOnSet = true });

		TemporaryIdGenerator.ResetCounter();

		Assert.Equal(-1, TemporaryIdGenerator.Next());
	}

	[Theory]
	[InlineData(-1, true)]
	[InlineData(0, false)]
	[InlineData(1, false)]
	public void OnlyNegativeIdsAreTemporary(int id, bool temporary) =>
		Assert.Equal(temporary, TemporaryIdGenerator.IsTemporary(id));

	[Fact]
	public void ANullStoreIsRefused() =>
		Assert.Throws<ArgumentNullException>(() => TemporaryIdGenerator.Use(null!));
}

/// <summary>The email templates: the two that ship, and the ways a render goes wrong.</summary>
public sealed class EmailTemplateServiceTests
{
	private readonly EmailTemplateService _templates = new(typeof(EmailTemplateService).Assembly);

	[Theory]
	[InlineData("WelcomeEmail")]
	[InlineData("ComplianceAcceptanceEmail")]
	public async Task BothShippingTemplatesAreStillEmbeddedInCore(string name)
	{
		var html = await _templates.RenderTemplateAsync(name, new { });

		Assert.False(string.IsNullOrWhiteSpace(html));
	}

	[Fact]
	public async Task AnUnknownTemplateIsNotFoundRatherThanABlankEmail()
	{
		var ex = await Assert.ThrowsAsync<TemplateNotFoundException>(() => _templates.RenderTemplateAsync("NoSuchTemplate", new { }));

		Assert.Equal("NoSuchTemplate", ex.TemplateName);
	}

	/// <summary>
	/// Found, not chosen: a placeholder the model has no property for
	/// renders as nothing, so a renamed property sends an email with a hole
	/// in it rather than failing. The "return original" fallback in
	/// ReplaceNestedProperties only applies when the lookup throws.
	/// </summary>
	[Fact]
	public void APlaceholderTheModelDoesNotHaveRendersAsNothing() =>
		Assert.Equal("Hello ", _templates.RenderTemplate("Hello {{Missing}}", new { Name = "Dave" }));

	[Fact]
	public void ANullPropertyRendersAsNothing() =>
		Assert.Equal("Hello !", _templates.RenderTemplate("Hello {{Name}}!", new Model(null)));

	[Fact]
	public void AnEmptyTemplateRendersAsNothing() =>
		Assert.Equal(string.Empty, _templates.RenderTemplate(string.Empty, new { }));

	private sealed record Model(string? Name);
}
