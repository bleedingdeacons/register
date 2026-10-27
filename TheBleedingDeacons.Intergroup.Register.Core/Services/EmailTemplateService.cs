using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Support;
using Serilog;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// Fills the email templates from a model.
	///
	/// <para><b><c>{{Name}}</c> is text; <c>{{{Name}}}</c> is HTML.</b> A
	/// value in double braces is HTML-encoded, braces included, so whatever
	/// was typed at the tablet arrives as the words that were typed. Before
	/// this, a member's name went into the email as markup: anyone at a
	/// meeting could put a link in their name, give someone else's address,
	/// and have the intergroup's own mail server deliver it. Triple braces
	/// insert the value as it is, and are for HTML the app itself wrote or
	/// took from the site — the policy wording, nothing typed in.</para>
	///
	/// <para><b>A value is never read as template.</b> Loops and
	/// conditionals are worked out on the template first, and placeholders
	/// are then filled in a single pass, so a value containing
	/// <c>{{Email}}</c> or <c>{{/if}}</c> is inserted as those characters
	/// rather than expanded.</para>
	/// </summary>
	public class EmailTemplateService : IEmailTemplateService
	{
		private static readonly ILogger Logger = AppLogger.ForContext<EmailTemplateService>();

		// Guards the template regexes against pathological (ReDoS) input.
		private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

		// {{{Path}}} raw, or {{Path}} encoded; Path as in {{Member.Name}} or {{Name()}}.
		private const string PathPattern = @"[a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*(?:\(\))?";

		private static readonly Regex Placeholder = new(
			@"\{\{\{(?<raw>" + PathPattern + @")\}\}\}|\{\{(?<text>" + PathPattern + @")\}\}",
			RegexOptions.CultureInvariant,
			RegexTimeout);

		private readonly string _templateDirectory;
		private readonly Dictionary<string, string> _templateCache;
		private readonly Assembly _assembly;

		public EmailTemplateService(string templateDirectory = "Templates")
		{
			_templateDirectory = templateDirectory;
			_templateCache = new Dictionary<string, string>(StringComparer.Ordinal);
			_assembly = Assembly.GetExecutingAssembly();
		}

		public EmailTemplateService(Assembly assembly, string templateDirectory = "Templates")
		{
			_templateDirectory = templateDirectory;
			_templateCache = new Dictionary<string, string>(StringComparer.Ordinal);
			_assembly = assembly;
		}

		public async Task<string> RenderTemplateAsync<T>(string templateName, T model)
		{
			try
			{
				string? template;

				// Check cache first
				if (_templateCache.ContainsKey(templateName))
				{
					template = _templateCache[templateName];
				}
				else
				{
					// Try to load from embedded resource first
					template = await LoadEmbeddedTemplateAsync(templateName);

					if (template == null)
					{
						// Fallback to file system
						template = await LoadFileTemplateAsync(templateName);
					}

					if (template == null)
					{
						throw new TemplateNotFoundException(templateName);
					}


					_templateCache[templateName] = template;
				}

				return RenderTemplate(template, model);
			}
			catch (Exception ex) when (!(ex is TemplateNotFoundException))
			{
				throw new TemplateRenderingException(
					$"Error rendering template '{templateName}': {ex.Message}", templateName, ex);
			}
		}

		public async Task<string> RenderTemplateFromStringAsync<T>(string template, T model)
		{
			return await Task.FromResult(RenderTemplate(template, model));
		}

		public string RenderTemplate<T>(string template, T model)
		{
			try
			{
				if (string.IsNullOrEmpty(template))
					return string.Empty;

				// The template's shape first — loops, then {{#if}} — and only
				// then the values, in one pass, so nothing inserted is ever
				// read as template.
				var result = ProcessLoops(template, model);
				result = ProcessConditionals(result, model);
				return ReplacePlaceholders(result, model);
			}
			catch (Exception ex)
			{
				throw new TemplateRenderingException(
					$"Error rendering template: {ex.Message}", ex);
			}
		}

		private async Task<string?> LoadEmbeddedTemplateAsync(string templateName)
		{
			try
			{
				var extensions = new[] { ".html", ".cshtml", ".txt" };

				// The assembly the caller named first, then this one: the
				// shipping templates are embedded beside this service, in
				// Register.Core. Until register#55 the app passed its own
				// assembly, which has held no templates since the Core split,
				// and every acceptance and welcome email failed with
				// TemplateNotFoundException.
				var own = typeof(EmailTemplateService).Assembly;
				var assemblies = _assembly == own ? new[] { own } : new[] { _assembly, own };

				foreach (var assembly in assemblies)
				{
					foreach (var extension in extensions)
					{
						var resourceName = $"TheBleedingDeacons.Intergroup.Register.{_templateDirectory}.{templateName}{extension}";
						using var stream = assembly.GetManifestResourceStream(resourceName);

						if (stream != null)
						{
							using var reader = new StreamReader(stream);
							return await reader.ReadToEndAsync();
						}
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		private async Task<string?> LoadFileTemplateAsync(string templateName)
		{
			try
			{
				var extensions = new[] { ".html", ".cshtml", ".txt" };

				foreach (var ext in extensions)
				{
					var filePath = Path.Combine(_templateDirectory, $"{templateName}{ext}");

					if (File.Exists(filePath))
					{
						return await File.ReadAllTextAsync(filePath);
					}
				}

				return null;
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Fills every <c>{{Path}}</c> and <c>{{{Path}}}</c> from the model in
		/// one pass. A path the model does not have renders as nothing; one
		/// whose lookup throws is left as it was.
		/// </summary>
		private static string ReplacePlaceholders<T>(string template, T model) =>
			Placeholder.Replace(template, match =>
			{
				var raw = match.Groups["raw"].Success;
				var path = raw ? match.Groups["raw"].Value : match.Groups["text"].Value;
				try
				{
					var value = GetNestedPropertyValue(model, path)?.ToString() ?? string.Empty;
					return raw ? value : Encode(value);
				}
				catch
				{
					return match.Value;
				}
			});

		/// <summary>
		/// A value as text in HTML. Braces are encoded too, so that nothing
		/// inserted can form a placeholder for a later pass to fill.
		/// </summary>
		internal static string Encode(string value) =>
			WebUtility.HtmlEncode(value)
				.Replace("{", "&#123;", StringComparison.Ordinal)
				.Replace("}", "&#125;", StringComparison.Ordinal);

		private static object? GetNestedPropertyValue<T>(T obj, string propertyPath)
		{
			if (obj == null) return null;

			var parts = propertyPath.Split('.');
			object? current = obj;

			foreach (var part in parts)
			{
				if (current == null) return null;

				var cleanPart = part.Replace("()", ""); // Remove method call syntax
				var property = current.GetType().GetProperty(cleanPart);

				if (property != null)
				{
					current = property.GetValue(current);
				}
				else
				{
					return null;
				}
			}

			return current;
		}

		private static string ProcessConditionals<T>(string template, T model)
		{
			// Handle {{#if PropertyName}}...{{/if}} blocks
			var pattern = @"\{\{#if\s+([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*)\}\}(.*?)\{\{/if\}\}";
			var regex = new Regex(pattern, RegexOptions.Singleline, RegexTimeout);

			return regex.Replace(template, match =>
			{
				var propertyName = match.Groups[1].Value;
				var content = match.Groups[2].Value;

				try
				{
					var value = GetNestedPropertyValue(model, propertyName);

					// Truthiness for {{#if}} sections: null is false, bool is
					// itself, 0 is false, an empty string is false, and any
					// other non-null value counts as present.
					var isTrue = value switch
					{
						null => false,
						bool boolValue => boolValue,
						int intValue => intValue != 0,
						string stringValue => !string.IsNullOrEmpty(stringValue),
						_ => true,
					};

					return isTrue ? content : string.Empty;
				}
				catch
				{
					return string.Empty;
				}
			});
		}

		private static string ProcessLoops<T>(string template, T model)
		{
			var result = template;
			const int maxIterations = 100;
			var iteration = 0;

			while (iteration++ < maxIterations)
			{
				// Find {{#each PropertyName}}
				var eachMatch = Regex.Match(result, @"\{\{#each\s+([a-zA-Z_][a-zA-Z0-9_]*)\}\}", RegexOptions.None, RegexTimeout);
				if (!eachMatch.Success) break;

				var propertyName = eachMatch.Groups[1].Value;
				var loopStart = eachMatch.Index;
				var contentStart = eachMatch.Index + eachMatch.Length;

				// Find {{/each}} manually (not with regex to avoid bracket issues)
				var endEachIndex = result.IndexOf("{{/each}}", contentStart, StringComparison.Ordinal);
				if (endEachIndex == -1) break;

				// Extract the item template between {{#each}} and {{/each}}
				var itemTemplate = result.Substring(contentStart, endEachIndex - contentStart);

				Logger.Debug("Processing loop for property {PropertyName}", propertyName);

				try
				{
					// Get the collection property
					var collection = GetNestedPropertyValue(model, propertyName);
					var processedContent = string.Empty;

					if (collection is System.Collections.IEnumerable enumerable and not string)
					{
						var count = 0;
						foreach (var item in enumerable)
						{
							count++;


							var itemHtml = ProcessSingleItemTemplate(itemTemplate, item);
							processedContent += itemHtml;


						}
						Logger.Debug("Processed {Count} items for {PropertyName}", count, propertyName);
					}
					else
					{
						Logger.Debug("Property {PropertyName} is not enumerable", propertyName);
					}

					// Replace the entire {{#each}}...{{/each}} block
					var fullLoop = result.Substring(loopStart, endEachIndex - loopStart + 9); // +9 for "{{/each}}"
					result = result.Replace(fullLoop, processedContent);
				}
				catch (Exception ex)
				{
					Logger.Warning(ex, "Error processing loop for {PropertyName}", propertyName);
					break;
				}
			}

			if (iteration >= maxIterations)
			{
				Logger.Warning("ProcessLoops hit iteration limit ({MaxIterations}) — possible malformed template", maxIterations);
			}

			return result;
		}

		/// <summary>
		/// Fills one loop item's own placeholders, encoded or raw by the same
		/// rule as the model's, in one pass. Placeholders the item has no
		/// property for are left for the model's pass; an encoded value
		/// cannot be mistaken for one, since its braces are encoded.
		/// </summary>
		private static string ProcessSingleItemTemplate<T>(string itemTemplate, T item)
		{
			if (item == null) return itemTemplate;

			var itemType = item.GetType();

			return Placeholder.Replace(itemTemplate, match =>
			{
				var raw = match.Groups["raw"].Success;
				var name = raw ? match.Groups["raw"].Value : match.Groups["text"].Value;

				var prop = itemType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
				if (prop is null)
					return match.Value;

				try
				{
					var value = prop.GetValue(item)?.ToString() ?? string.Empty;
					return raw ? value : Encode(value);
				}
				catch (Exception ex)
				{
					Logger.Debug(ex, "Error processing property {PropertyName}", prop.Name);
					return match.Value;
				}
			});
		}
	}
}