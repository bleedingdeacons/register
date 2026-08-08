using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Register.UnitTests;

/// <summary>
/// The template engine behind welcome and compliance-acceptance emails. It is a
/// small hand-rolled Handlebars subset — placeholders, nested properties,
/// <c>{{#each}}</c> and <c>{{#if}}</c> — so its substitution rules and its
/// truthiness rules are worth pinning exactly.
///
/// <para>The <c>(Assembly, string)</c> constructor overload exists so the
/// resource-loading path can be aimed at a chosen assembly, which is what makes
/// the embedded-template tests below possible.</para>
/// </summary>
public sealed class EmailTemplateServiceTests : IDisposable
{
	private readonly List<string> _tempDirs = new();

	public void Dispose()
	{
		foreach (var d in _tempDirs)
		{
			try { Directory.Delete(d, recursive: true); } catch (IOException) { }
		}
	}

	private string NewTempDir()
	{
		var dir = Path.Combine(Path.GetTempPath(), "register-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		_tempDirs.Add(dir);
		return dir;
	}

	// The assembly that actually carries the embedded Templates/*.html resources.
	private static System.Reflection.Assembly AppAssembly => typeof(EmailTemplateService).Assembly;

	private sealed class Person
	{
		public string Name { get; set; } = "Alex";
		public string? Nickname { get; set; }
		public int Count { get; set; }
		public bool Flag { get; set; }
		public Address? Home { get; set; }
		public List<Item> Items { get; set; } = new();
		public string NotACollection { get; set; } = "text";
	}

	private sealed class Address
	{
		public string City { get; set; } = "Bristol";
	}

	private sealed class Item
	{
		public string Label { get; set; } = string.Empty;
	}

	private sealed class Exploding
	{
		public string Boom => throw new InvalidOperationException("getter blew up");
	}

	// ── Plain substitution ────────────────────────────────────────────

	[Fact]
	public void RenderTemplate_SubstitutesSimplePlaceholders()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("Hello Alex", svc.RenderTemplate("Hello {{Name}}", new Person { Name = "Alex" }));
	}

	[Fact]
	public void RenderTemplate_RendersANullPropertyAsEmpty()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("[]", svc.RenderTemplate("[{{Nickname}}]", new Person { Nickname = null }));
	}

	[Fact]
	public void RenderTemplate_ResolvesNestedProperties()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("Bristol", svc.RenderTemplate("{{Home.City}}", new Person { Home = new Address() }));
	}

	[Fact]
	public void RenderTemplate_RendersAMissingNestedPathAsEmpty()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("[]", svc.RenderTemplate("[{{Home.City}}]", new Person { Home = null }));
	}

	[Fact]
	public void RenderTemplate_StripsPlaceholdersTheModelDoesNotHave()
	{
		// Worth knowing: an unknown placeholder silently disappears rather than
		// being left in the email for someone to notice.
		var svc = new EmailTemplateService();

		Assert.Equal("[]", svc.RenderTemplate("[{{NoSuchProperty}}]", new Person()));
	}

	[Fact]
	public void RenderTemplate_ReturnsEmptyForAnEmptyTemplate()
	{
		var svc = new EmailTemplateService();

		Assert.Equal(string.Empty, svc.RenderTemplate(string.Empty, new Person()));
	}

	// ── Conditionals ──────────────────────────────────────────────────

	[Theory]
	[InlineData(true, "yes")]
	[InlineData(false, "")]
	public void RenderTemplate_TreatsABoolAsItself(bool flag, string expected)
	{
		var svc = new EmailTemplateService();

		Assert.Equal(expected, svc.RenderTemplate("{{#if Flag}}yes{{/if}}", new Person { Flag = flag }));
	}

	[Theory]
	[InlineData(0, "")]
	[InlineData(3, "yes")]
	[InlineData(-1, "yes")]
	public void RenderTemplate_TreatsZeroAsFalseForInts(int count, string expected)
	{
		var svc = new EmailTemplateService();

		Assert.Equal(expected, svc.RenderTemplate("{{#if Count}}yes{{/if}}", new Person { Count = count }));
	}

	[Theory]
	[InlineData(null, "")]
	[InlineData("", "")]
	[InlineData("x", "yes")]
	public void RenderTemplate_TreatsAnEmptyStringAsFalse(string? nickname, string expected)
	{
		var svc = new EmailTemplateService();

		Assert.Equal(expected, svc.RenderTemplate("{{#if Nickname}}yes{{/if}}", new Person { Nickname = nickname }));
	}

	[Fact]
	public void RenderTemplate_TreatsAnyOtherNonNullValueAsTrue()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("yes", svc.RenderTemplate("{{#if Home}}yes{{/if}}", new Person { Home = new Address() }));
		Assert.Equal("", svc.RenderTemplate("{{#if Home}}yes{{/if}}", new Person { Home = null }));
	}

	[Fact]
	public void RenderTemplate_SubstitutesInsideAConditionalThatIsKept()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("Hi Alex", svc.RenderTemplate(
			"{{#if Flag}}Hi {{Name}}{{/if}}", new Person { Flag = true, Name = "Alex" }));
	}

	// ── Loops ─────────────────────────────────────────────────────────

	[Fact]
	public void RenderTemplate_RepeatsTheBlockOncePerItem()
	{
		var svc = new EmailTemplateService();
		var model = new Person { Items = { new Item { Label = "a" }, new Item { Label = "b" } } };

		Assert.Equal("[a][b]", svc.RenderTemplate("{{#each Items}}[{{Label}}]{{/each}}", model));
	}

	[Fact]
	public void RenderTemplate_DropsTheBlockForAnEmptyCollection()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("|", svc.RenderTemplate("|{{#each Items}}[{{Label}}]{{/each}}", new Person()));
	}

	[Fact]
	public void RenderTemplate_DropsTheBlockWhenThePropertyIsNotACollection()
	{
		var svc = new EmailTemplateService();

		Assert.Equal("|", svc.RenderTemplate("|{{#each NotACollection}}x{{/each}}", new Person()));
	}

	[Fact]
	public void RenderTemplate_LeavesAnUnclosedLoopAlone()
	{
		// No {{/each}} means the loop scanner bails rather than looping forever.
		var svc = new EmailTemplateService();
		var result = svc.RenderTemplate("{{#each Items}}[{{Label}}]", new Person());

		Assert.Contains("{{#each Items}}", result, StringComparison.Ordinal);
	}

	// ── Failure modes ─────────────────────────────────────────────────

	[Fact]
	public void RenderTemplate_WrapsAnUnexpectedFailure()
	{
		var svc = new EmailTemplateService();

		var ex = Assert.Throws<TemplateRenderingException>(
			() => svc.RenderTemplate("{{Boom}}", new Exploding()));
		Assert.NotNull(ex.InnerException);
	}

	[Fact]
	public async Task RenderTemplateAsync_ThrowsWhenTheTemplateDoesNotExist()
	{
		var svc = new EmailTemplateService(AppAssembly, NewTempDir());

		var ex = await Assert.ThrowsAsync<TemplateNotFoundException>(
			() => svc.RenderTemplateAsync("NoSuchTemplate", new Person()));
		Assert.Equal("NoSuchTemplate", ex.TemplateName);
	}

	[Fact]
	public async Task RenderTemplateAsync_DoesNotWrapTemplateNotFound()
	{
		// The exception filter deliberately lets TemplateNotFoundException past
		// so callers can distinguish "missing" from "broken". Easy to lose.
		var svc = new EmailTemplateService(AppAssembly, NewTempDir());

		await Assert.ThrowsAsync<TemplateNotFoundException>(
			() => svc.RenderTemplateAsync("NoSuchTemplate", new Person()));
	}

	[Fact]
	public async Task RenderTemplateAsync_WrapsARenderFailureAndKeepsTheTemplateName()
	{
		var dir = NewTempDir();
		await File.WriteAllTextAsync(Path.Combine(dir, "Boom.html"), "{{Boom}}");
		var svc = new EmailTemplateService(AppAssembly, dir);

		var ex = await Assert.ThrowsAsync<TemplateRenderingException>(
			() => svc.RenderTemplateAsync("Boom", new Exploding()));
		Assert.Equal("Boom", ex.TemplateName);
	}

	// ── Loading and caching ───────────────────────────────────────────

	[Fact]
	public async Task RenderTemplateAsync_LoadsATemplateFromDisk()
	{
		var dir = NewTempDir();
		await File.WriteAllTextAsync(Path.Combine(dir, "Greet.html"), "Hello {{Name}}");
		var svc = new EmailTemplateService(AppAssembly, dir);

		Assert.Equal("Hello Alex", await svc.RenderTemplateAsync("Greet", new Person { Name = "Alex" }));
	}

	[Fact]
	public async Task RenderTemplateAsync_CachesTheTemplateAfterTheFirstLoad()
	{
		var dir = NewTempDir();
		var file = Path.Combine(dir, "Greet.html");
		await File.WriteAllTextAsync(file, "Hello {{Name}}");
		var svc = new EmailTemplateService(AppAssembly, dir);

		await svc.RenderTemplateAsync("Greet", new Person());

		// Delete the source: a second render can only succeed from the cache.
		File.Delete(file);

		Assert.Equal("Hello Alex", await svc.RenderTemplateAsync("Greet", new Person { Name = "Alex" }));
	}

	[Fact]
	public async Task RenderTemplateAsync_RendersTheRealWelcomeEmailResource()
	{
		// Guards the resource logical name as much as the rendering: renaming or
		// unembedding Templates/WelcomeEmail.html breaks registration emails.
		var svc = new EmailTemplateService(AppAssembly);

		var html = await svc.RenderTemplateAsync("WelcomeEmail", new WelcomeEmail
		{
			FirstName = "Alex",
			Location = "Bristol",
			Address = "1 Example Street",
			StartTime = "19:30",
			Email = "alex@example.org",
			Mobile = "07700900123",
			MeetingName = "Tuesday Steps",
			MeetingContacts = new List<MeetingContact>(),
			Policy = "We keep your details safe.",
		});

		Assert.Contains("Alex", html, StringComparison.Ordinal);
		Assert.Contains("Tuesday Steps", html, StringComparison.Ordinal);
		Assert.DoesNotContain("{{FirstName}}", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RenderTemplateAsync_RendersTheRealComplianceEmailResource()
	{
		var svc = new EmailTemplateService(AppAssembly);

		var html = await svc.RenderTemplateAsync("ComplianceAcceptanceEmail", new ComplianceEmail
		{
			AnonymousName = "Alex",
			PolicyVersion = "2.1",
			PolicyTitle = "Privacy Policy",
		});

		Assert.Contains("Alex", html, StringComparison.Ordinal);
		Assert.Contains("2.1", html, StringComparison.Ordinal);
		Assert.DoesNotContain("{{AnonymousName}}", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RenderTemplateFromStringAsync_MatchesTheSynchronousRender()
	{
		var svc = new EmailTemplateService();
		var model = new Person { Name = "Alex" };

		Assert.Equal(
			svc.RenderTemplate("Hello {{Name}}", model),
			await svc.RenderTemplateFromStringAsync("Hello {{Name}}", model));
	}
}
