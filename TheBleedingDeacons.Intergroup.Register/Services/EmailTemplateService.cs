using System.Reflection;
using System.Text.RegularExpressions;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Exceptions;
using TheBleedingDeacons.Intergroup.Register.Support;
using Serilog;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	public class EmailTemplateService : IEmailTemplateService
	{
		private static readonly ILogger Logger = AppLogger.ForContext<EmailTemplateService>();

		// Guards the template regexes against pathological (ReDoS) input.
		private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

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
				string template;

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

				// Handle loops: {{#each CollectionProperty}}...{{/each}}
				var result = template;
				result = ProcessLoops(result, model);

				var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

				// Handle simple property replacements: {{PropertyName}}
				foreach (var prop in properties)
				{
					var placeholder = $"{{{{{prop.Name}}}}}";
					var value = prop.GetValue(model)?.ToString() ?? string.Empty;
					result = result.Replace(placeholder, value);
				}

				// Handle nested property access: {{Property.SubProperty}}
				result = ReplaceNestedProperties(result, model);

				// Handle simple conditionals: {{#if PropertyName}}...{{/if}}
				result = ProcessConditionals(result, model);



				return result;
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

				foreach (var extension in extensions)
				{
					var resourceName = $"TheBleedingDeacons.Intergroup.Register.{_templateDirectory}.{templateName}{extension}";
					using var stream = _assembly.GetManifestResourceStream(resourceName);

					if (stream != null)
					{
						using var reader = new StreamReader(stream);
						return await reader.ReadToEndAsync();
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

		private string ReplaceNestedProperties<T>(string template, T model)
		{
			// Match patterns like {{Property.SubProperty}} or {{Property.Method()}}
			var pattern = @"\{\{([a-zA-Z_][a-zA-Z0-9_]*(?:\.[a-zA-Z_][a-zA-Z0-9_]*)*(?:\(\))?)\}\}";
			var regex = new Regex(pattern, RegexOptions.None, RegexTimeout);

			return regex.Replace(template, match =>
			{
				var propertyPath = match.Groups[1].Value;
				try
				{
					var value = GetNestedPropertyValue(model, propertyPath);
					return value?.ToString() ?? string.Empty;
				}
				catch
				{
					return match.Value; // Return original if can't resolve
				}
			});
		}

		private object? GetNestedPropertyValue<T>(T obj, string propertyPath)
		{
			if (obj == null) return null;

			var parts = propertyPath.Split('.');
			object current = obj;

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

		private string ProcessConditionals<T>(string template, T model)
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
					var isTrue = value != null &&
								(value is bool boolValue ? boolValue :
								 value is int intValue ? intValue != 0 :
								 value is string stringValue ? !string.IsNullOrEmpty(stringValue) :
								 true);

					return isTrue ? content : string.Empty;
				}
				catch
				{
					return string.Empty;
				}
			});
		}

		private string ProcessLoops<T>(string template, T model)
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

		// Complete item processing method
		private string ProcessSingleItemTemplate<T>(string itemTemplate, T item)
		{
			if (item == null) return itemTemplate;

			var result = itemTemplate;
			var itemType = item.GetType();
			var properties = itemType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);



			foreach (var prop in properties)
			{
				try
				{
					var placeholder = $"{{{{{prop.Name}}}}}";
					var value = prop.GetValue(item)?.ToString() ?? string.Empty;

					if (result.Contains(placeholder, StringComparison.Ordinal))
					{
						result = result.Replace(placeholder, value);

					}
				}
				catch (Exception ex)
				{
					Logger.Debug(ex, "Error processing property {PropertyName}", prop.Name);
				}
			}

			return result;
		}
	}
}