using TheBleedingDeacons.Intergroup.Register.Utilities;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// This converter turns the privacy policy — markdown fetched from the
/// Unity/Scrutiny server — into the HTML embedded in compliance acceptance
/// emails. Untrusted input rendered into HTML makes the escaping behaviour
/// security-relevant rather than cosmetic, so that gets the most attention
/// here.
/// </summary>
public class BasicMarkdownConverterTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   \n  \t ")]
	public void Convert_ReturnsEmptyForNothingWorthRendering(string? input)
	{
		Assert.Equal(string.Empty, BasicMarkdownConverter.Convert(input!));
	}

	// ── Block elements ────────────────────────────────────────────────

	[Theory]
	[InlineData("# One", "<h1>One</h1>")]
	[InlineData("## Two", "<h2>Two</h2>")]
	[InlineData("###### Six", "<h6>Six</h6>")]
	public void Convert_RendersAtxHeadings(string markdown, string expected)
	{
		Assert.Equal(expected, BasicMarkdownConverter.Convert(markdown));
	}

	[Fact]
	public void Convert_TreatsSevenHashesAsAParagraphNotAHeading()
	{
		// There is no <h7>; the heading regex caps at six.
		Assert.Equal("<p>####### Seven</p>", BasicMarkdownConverter.Convert("####### Seven"));
	}

	[Theory]
	[InlineData("---")]
	[InlineData("-----")]
	public void Convert_RendersHorizontalRules(string markdown)
	{
		Assert.Equal("<hr>", BasicMarkdownConverter.Convert(markdown));
	}

	[Theory]
	[InlineData('-')]
	[InlineData('*')]
	[InlineData('+')]
	public void Convert_AcceptsEveryBulletMarker(char marker)
	{
		var result = BasicMarkdownConverter.Convert($"{marker} first\n{marker} second");

		Assert.Equal("<ul>\n  <li>first</li>\n  <li>second</li>\n</ul>", Normalise(result));
	}

	[Fact]
	public void Convert_GroupsConsecutiveBulletsIntoOneList()
	{
		var result = BasicMarkdownConverter.Convert("- a\n- b\n\ntext\n\n- c");

		// Two separate <ul> blocks, not one spanning the paragraph.
		Assert.Equal(2, CountOccurrences(result, "<ul>"));
		Assert.Equal(2, CountOccurrences(result, "</ul>"));
	}

	[Fact]
	public void Convert_JoinsConsecutiveParagraphLinesWithASpace()
	{
		var result = BasicMarkdownConverter.Convert("first line\nsecond line");

		Assert.Equal("<p>first line second line</p>", Normalise(result));
	}

	[Fact]
	public void Convert_SplitsParagraphsOnABlankLine()
	{
		var result = Normalise(BasicMarkdownConverter.Convert("one\n\ntwo"));

		Assert.Contains("<p>one</p>", result, StringComparison.Ordinal);
		Assert.Contains("<p>two</p>", result, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("a\r\nb")]   // Windows
	[InlineData("a\rb")]     // classic Mac
	[InlineData("a\nb")]     // Unix
	public void Convert_NormalisesEveryLineEndingStyle(string markdown)
	{
		Assert.Equal("<p>a b</p>", Normalise(BasicMarkdownConverter.Convert(markdown)));
	}

	// ── Inline formatting ─────────────────────────────────────────────

	[Fact]
	public void Convert_RendersBoldAndItalic()
	{
		Assert.Equal("<p><strong>bold</strong> and <em>italic</em></p>",
			Normalise(BasicMarkdownConverter.Convert("**bold** and *italic*")));
	}

	[Fact]
	public void Convert_PrefersBoldOverItalicOnADoubleAsterisk()
	{
		// The bold regex runs first, so ** never decays into two <em>.
		Assert.Equal("<p><strong>x</strong></p>", Normalise(BasicMarkdownConverter.Convert("**x**")));
	}

	[Fact]
	public void Convert_RendersMarkdownLinks()
	{
		Assert.Equal("<p><a href=\"https://example.org\">the policy</a></p>",
			Normalise(BasicMarkdownConverter.Convert("[the policy](https://example.org)")));
	}

	[Theory]
	[InlineData("https://example.org")]
	[InlineData("http://example.org")]
	public void Convert_AutolinksBareUrls(string url)
	{
		Assert.Equal($"<p><a href=\"{url}\">{url}</a></p>",
			Normalise(BasicMarkdownConverter.Convert(url)));
	}

	[Fact]
	public void Convert_DoesNotDoubleWrapAUrlAlreadyInsideAMarkdownLink()
	{
		// The bare-URL pass uses a (?<!href=") guard so it skips the href it
		// has just written. Without it the output nests an <a> inside an <a>.
		var result = Normalise(BasicMarkdownConverter.Convert("[here](https://example.org)"));

		Assert.Equal("<p><a href=\"https://example.org\">here</a></p>", result);
		Assert.Equal(1, CountOccurrences(result, "<a href"));
	}

	[Fact]
	public void Convert_FormatsInlineMarkupInsideHeadingsAndListItems()
	{
		Assert.Equal("<h2>a <strong>b</strong></h2>", Normalise(BasicMarkdownConverter.Convert("## a **b**")));
		Assert.Contains("<li>a <em>b</em></li>", Normalise(BasicMarkdownConverter.Convert("- a *b*")), StringComparison.Ordinal);
	}

	// ── Escaping — the security-relevant part ─────────────────────────

	[Fact]
	public void Convert_EscapesHtmlInTheSource()
	{
		// Policy text arrives from the server. A raw <script> must not survive
		// into an outgoing email.
		var result = Normalise(BasicMarkdownConverter.Convert("<script>alert('x')</script>"));

		Assert.DoesNotContain("<script>", result, StringComparison.Ordinal);
		Assert.Contains("&lt;script&gt;", result, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("a & b", "&amp;")]
	[InlineData("a < b", "&lt;")]
	[InlineData("a > b", "&gt;")]
	[InlineData("say \"hi\"", "&quot;")]
	public void Convert_EscapesEachHtmlSpecialCharacter(string markdown, string expectedEntity)
	{
		Assert.Contains(expectedEntity, BasicMarkdownConverter.Convert(markdown), StringComparison.Ordinal);
	}

	[Fact]
	public void Convert_EscapesBeforeBuildingLinksSoAmpersandsInUrlsAreEncoded()
	{
		// EscapeHtml runs first, so a query string's & is already an entity by
		// the time the href is written. Pinning the ordering: reversing it
		// would emit a raw & inside an attribute.
		var result = BasicMarkdownConverter.Convert("[x](https://example.org/?a=1&b=2)");

		Assert.Contains("href=\"https://example.org/?a=1&amp;b=2\"", result, StringComparison.Ordinal);
	}

	[Fact]
	public void Convert_EscapesAngleBracketsInsideAHeading()
	{
		Assert.Equal("<h1>a &lt;b&gt; c</h1>", Normalise(BasicMarkdownConverter.Convert("# a <b> c")));
	}

	// ── A realistic document ──────────────────────────────────────────

	[Fact]
	public void Convert_HandlesARepresentativePolicyDocument()
	{
		const string markdown = """
			# Privacy Policy

			We hold your details so the intergroup can contact you.

			---

			## What we keep
			- Your **anonymous name**
			- Your email, if you give one

			See [our site](https://example.org) for more.
			""";

		var html = Normalise(BasicMarkdownConverter.Convert(markdown));

		Assert.Contains("<h1>Privacy Policy</h1>", html, StringComparison.Ordinal);
		Assert.Contains("<hr>", html, StringComparison.Ordinal);
		Assert.Contains("<h2>What we keep</h2>", html, StringComparison.Ordinal);
		Assert.Contains("<li>Your <strong>anonymous name</strong></li>", html, StringComparison.Ordinal);
		Assert.Contains("<a href=\"https://example.org\">our site</a>", html, StringComparison.Ordinal);
		Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Convert_DoesNotLeaveTrailingWhitespace()
	{
		var result = BasicMarkdownConverter.Convert("# Title\n\ntext\n\n\n");

		Assert.Equal(result.TrimEnd(), result);
	}

	/// <summary>
	/// The converter builds output with <c>AppendLine</c>, so its line endings
	/// are whatever the platform uses. Normalise to "\n" so the assertions
	/// describe the HTML rather than the runner's OS.
	/// </summary>
	private static string Normalise(string s) =>
		s.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

	private static int CountOccurrences(string haystack, string needle)
	{
		int count = 0, i = 0;
		while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) != -1)
		{
			count++;
			i += needle.Length;
		}

		return count;
	}
}
