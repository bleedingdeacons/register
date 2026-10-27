using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Intergroup.Register.Support;

/// <summary>
/// Hides email addresses in every property of a log event, through
/// <see cref="EmailRedactor"/>, before any sink sees it: the local log files
/// and Better Stack alike. A message is rendered from its properties, so the
/// message text is covered by the same pass.
///
/// <para>Strings are redacted wherever they sit — at the top level or inside
/// a destructured object, list or dictionary. Any other value that would
/// print an address (a <c>MailboxAddress</c>, say) is replaced by its
/// redacted text. Numbers, dates and the like are left as they are.</para>
///
/// <para><b>What this cannot reach.</b> An exception travels beside the
/// properties, not in them, and Inventory writes its text out whole; so does
/// the summary Inventory's own enricher adds after this one runs. An address
/// in an exception message — a mail server refusing a recipient, typically —
/// still gets through. Closing that is Inventory's to do.</para>
/// </summary>
public sealed class EmailRedactingEnricher : ILogEventEnricher
{
	private readonly EmailRedactor _redactor;

	public EmailRedactingEnricher(EmailRedactor redactor)
	{
		ArgumentNullException.ThrowIfNull(redactor);
		_redactor = redactor;
	}

	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		// Copied first: the loop replaces properties as it goes.
		foreach (var (name, value) in logEvent.Properties.ToList())
		{
			var redacted = Redact(value);
			if (!ReferenceEquals(redacted, value))
				logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
		}
	}

	// Returns the same instance when nothing changed, so untouched values
	// are not copied.
	private LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
	{
		ScalarValue scalar => Redact(scalar),
		SequenceValue sequence => Redact(sequence),
		StructureValue structure => Redact(structure),
		DictionaryValue dictionary => Redact(dictionary),
		_ => value,
	};

	private ScalarValue Redact(ScalarValue scalar)
	{
		switch (scalar.Value)
		{
			case string text:
				var redacted = _redactor.Redact(text);
				return ReferenceEquals(redacted, text) ? scalar : new ScalarValue(redacted);

			case null or bool or char or IConvertible and not string or DateTimeOffset or TimeSpan or Guid:
				return scalar;

			default:
				var printed = Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty;
				var hidden = _redactor.Redact(printed);
				return ReferenceEquals(hidden, printed) ? scalar : new ScalarValue(hidden);
		}
	}

	private SequenceValue Redact(SequenceValue sequence)
	{
		var elements = sequence.Elements.Select(Redact).ToList();

		return elements.SequenceEqual(sequence.Elements, ReferenceComparer.Instance)
			? sequence
			: new SequenceValue(elements);
	}

	private StructureValue Redact(StructureValue structure)
	{
		var changed = false;
		var properties = new List<LogEventProperty>(structure.Properties.Count);

		foreach (var property in structure.Properties)
		{
			var redacted = Redact(property.Value);
			changed |= !ReferenceEquals(redacted, property.Value);
			properties.Add(ReferenceEquals(redacted, property.Value) ? property : new LogEventProperty(property.Name, redacted));
		}

		return changed ? new StructureValue(properties, structure.TypeTag) : structure;
	}

	private DictionaryValue Redact(DictionaryValue dictionary)
	{
		var changed = false;
		var elements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);

		foreach (var (key, value) in dictionary.Elements)
		{
			var redactedKey = Redact(key);
			var redactedValue = Redact(value);
			changed |= !ReferenceEquals(redactedKey, key) || !ReferenceEquals(redactedValue, value);
			elements.Add(new(redactedKey, redactedValue));
		}

		return changed ? new DictionaryValue(elements) : dictionary;
	}

	private sealed class ReferenceComparer : IEqualityComparer<LogEventPropertyValue>
	{
		public static readonly ReferenceComparer Instance = new();

		public bool Equals(LogEventPropertyValue? x, LogEventPropertyValue? y) => ReferenceEquals(x, y);

		public int GetHashCode(LogEventPropertyValue obj) =>
			System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
	}
}
