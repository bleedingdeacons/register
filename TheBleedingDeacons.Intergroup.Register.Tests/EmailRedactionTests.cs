using Serilog.Events;
using Serilog.Parsing;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Tests;

/// <summary>
/// Email addresses in the logs become per-tablet tokens: hidden, but the
/// same address still lines up with itself.
/// </summary>
public sealed class EmailRedactionTests
{
	private static readonly byte[] KeyA = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
	private static readonly byte[] KeyB = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

	private readonly EmailRedactor _redactor = new(KeyA);

	[Fact]
	public void An_address_becomes_a_token()
	{
		var redacted = _redactor.Redact("Email sent to jo.bloggs@example.org");

		Assert.DoesNotContain("jo.bloggs", redacted, StringComparison.Ordinal);
		Assert.DoesNotContain("example.org", redacted, StringComparison.Ordinal);
		Assert.Matches("^Email sent to email:[0-9a-f]{8}$", redacted);
	}

	[Fact]
	public void The_same_address_gives_the_same_token_every_time()
	{
		Assert.Equal(_redactor.Token("jo@example.org"), _redactor.Token("jo@example.org"));
		Assert.Equal(_redactor.Token("jo@example.org"), new EmailRedactor(KeyA).Token("jo@example.org"));
	}

	[Fact]
	public void Case_and_surrounding_space_do_not_change_the_token()
	{
		Assert.Equal(_redactor.Token("jo@example.org"), _redactor.Token("  Jo@Example.ORG "));
	}

	[Fact]
	public void Different_addresses_give_different_tokens()
	{
		Assert.NotEqual(_redactor.Token("jo@example.org"), _redactor.Token("sam@example.org"));
	}

	[Fact]
	public void Another_tablet_gives_the_same_address_a_different_token()
	{
		Assert.NotEqual(_redactor.Token("jo@example.org"), new EmailRedactor(KeyB).Token("jo@example.org"));
	}

	[Fact]
	public void Every_address_in_the_text_is_replaced()
	{
		var redacted = _redactor.Redact("From a@x.org, to b@y.co.uk; cc <c+tag@z.net>");

		Assert.DoesNotContain("@", redacted, StringComparison.Ordinal);
		Assert.Equal(3, redacted.Split("email:").Length - 1);
	}

	[Theory]
	[InlineData("No addresses here")]
	[InlineData("Member 42 registered for group 7")]
	[InlineData("Reply @ the meeting")]
	[InlineData("")]
	public void Text_without_an_address_is_returned_as_it_was(string text)
	{
		Assert.Same(text, _redactor.Redact(text));
	}

	[Fact]
	public void A_short_key_is_refused()
	{
		Assert.Throws<ArgumentException>(() => new EmailRedactor(new byte[15]));
	}

	[Fact]
	public void A_new_key_is_random()
	{
		Assert.NotEqual(EmailRedactor.NewKey(), EmailRedactor.NewKey());
	}

	[Fact]
	public void The_enricher_redacts_a_top_level_property_and_the_rendered_message()
	{
		var logEvent = Event("Email sent to {Recipient}", ("Recipient", new ScalarValue("jo@example.org")));

		Enrich(logEvent);

		var rendered = logEvent.RenderMessage();
		Assert.DoesNotContain("jo@example.org", rendered, StringComparison.Ordinal);
		Assert.Equal("Email sent to \"" + _redactor.Token("jo@example.org") + "\"", rendered);
	}

	[Fact]
	public void The_enricher_redacts_inside_lists_objects_and_dictionaries()
	{
		var logEvent = Event(
			"Batch {Recipients} {Message} {ByGroup}",
			("Recipients", new SequenceValue([new ScalarValue("a@x.org"), new ScalarValue(3)])),
			("Message", new StructureValue([new LogEventProperty("To", new ScalarValue("b@y.org"))], "QueuedEmail")),
			("ByGroup", new DictionaryValue([new(new ScalarValue("c@z.org"), new ScalarValue("d@z.org"))])));

		Enrich(logEvent);

		var rendered = logEvent.RenderMessage();
		Assert.DoesNotContain("@", rendered, StringComparison.Ordinal);
		Assert.Contains(_redactor.Token("b@y.org"), rendered, StringComparison.Ordinal);
		Assert.Equal("QueuedEmail", ((StructureValue)logEvent.Properties["Message"]).TypeTag);
	}

	[Fact]
	public void The_enricher_redacts_an_object_that_prints_an_address()
	{
		var logEvent = Event("To {Mailbox}", ("Mailbox", new ScalarValue(new Printed("Jo <jo@example.org>"))));

		Enrich(logEvent);

		Assert.Equal("To \"Jo <" + _redactor.Token("jo@example.org") + ">\"", logEvent.RenderMessage());
	}

	[Fact]
	public void The_enricher_leaves_other_values_alone()
	{
		var number = new ScalarValue(42);
		var name = new ScalarValue("Jo B");
		var logEvent = Event("{Id} {Name}", ("Id", number), ("Name", name));

		Enrich(logEvent);

		Assert.Same(number, logEvent.Properties["Id"]);
		Assert.Same(name, logEvent.Properties["Name"]);
	}

	private void Enrich(LogEvent logEvent) =>
		new EmailRedactingEnricher(_redactor).Enrich(logEvent, null!);

	private static LogEvent Event(string template, params (string Name, LogEventPropertyValue Value)[] properties) =>
		new(
			DateTimeOffset.UnixEpoch,
			LogEventLevel.Information,
			exception: null,
			new MessageTemplateParser().Parse(template),
			properties.Select(p => new LogEventProperty(p.Name, p.Value)));

	private sealed class Printed(string text)
	{
		public override string ToString() => text;
	}
}
