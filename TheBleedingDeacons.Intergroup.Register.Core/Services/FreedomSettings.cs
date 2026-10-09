using System.Globalization;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Inventory;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// The settings Freedom manages for Register: every credential and
	/// endpoint the tablet uses.
	///
	/// <para><b>Freedom is the only source.</b> Nothing is built into the app
	/// and nothing can be typed in on the tablet. A key Freedom does not hold
	/// — because the site has not set it, or the tablet has not signed in, or
	/// the build names no Freedom site — is simply absent: an empty host, an
	/// empty key. Only the non-secret shape of a connection has a default
	/// (port 587, TLS on, a 30-second timeout), from the models themselves,
	/// so a site that sets just a host and a password still works. The
	/// <see cref="Smtp"/>, <see cref="BetterStack"/>, <see cref="Unity"/> and
	/// <see cref="Compliance"/> methods are the whole of how the tablet
	/// reads them.</para>
	///
	/// <para>Until 2026-10-04 a managed value was laid over one the tablet
	/// stored, or one a dev build embedded from devsettings.json. Both are
	/// gone: a dev build that silently pointed at the live site was exactly
	/// the kind of mistake this rules out.</para>
	///
	/// <para>The keys are the contract with the Freedom admin: an
	/// application called <c>register</c> with these names. Secret ones —
	/// <c>smtp.password</c>, <c>unity.api_key</c>,
	/// <c>betterstack.source_token</c> — should be ticked Secret there, so
	/// they arrive sealed to the tablet's own key.</para>
	/// </summary>
	public static class FreedomSettings
	{
		public const string SmtpHost = "smtp.host";
		public const string SmtpPort = "smtp.port";
		public const string SmtpUsername = "smtp.username";
		public const string SmtpPassword = "smtp.password";
		public const string SmtpEnableSsl = "smtp.enable_ssl";
		public const string SmtpFromDisplayName = "smtp.from_display_name";
		public const string SmtpTimeoutSeconds = "smtp.timeout_seconds";
		public const string UnityBaseUrl = "unity.base_url";
		public const string UnityApiKey = "unity.api_key";
		public const string BetterStackEndpoint = "betterstack.endpoint";
		public const string BetterStackSourceToken = "betterstack.source_token";
		public const string ComplianceEmail = "compliance.email";

		/// <summary>
		/// The site, application and callback, from the "Freedom" section of
		/// appsettings.json — none of them secret. Null when the build names no
		/// site, which leaves Freedom switched off.
		/// </summary>
		public static FreedomOptions? OptionsFrom(Microsoft.Extensions.Configuration.IConfiguration configuration)
		{
			var baseUrl = configuration["Freedom:BaseUrl"];
			if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var site))
				return null;

			return new FreedomOptions
			{
				BaseUrl = site,
				Application = configuration["Freedom:Application"] is { Length: > 0 } application ? application : "register",
				// Must match FreedomCallbackActivity's intent filter and the
				// application's Callback URI in the Freedom admin.
				CallbackUri = new Uri(configuration["Freedom:CallbackUri"] is { Length: > 0 } callback
					? callback
					: FreedomCallbackUri),
				AllowInsecureBaseUrl = string.Equals(configuration["Freedom:AllowInsecure"], "true", StringComparison.OrdinalIgnoreCase),
			};
		}

		/// <summary>The default callback: the app id, a <c>.freedom</c> suffix, host <c>auth</c>.</summary>
		public const string FreedomCallbackUri = "org.thebleedingdeacons.intergroup.register.freedom://auth";

		/// <summary>The SMTP settings Freedom holds, over the model's defaults.</summary>
		public static SmtpConfiguration Smtp(Func<string, string?> managed) =>
			Apply(new SmtpConfiguration(), managed);

		/// <summary>The Better Stack settings Freedom holds; empty, and so not shipping, without them.</summary>
		public static BetterStackConfiguration BetterStack(Func<string, string?> managed) =>
			Apply(new BetterStackConfiguration(), managed);

		/// <summary>The Unity site and API key Freedom holds; empty strings without them.</summary>
		public static (string BaseUrl, string ApiKey) Unity(Func<string, string?> managed)
		{
			ArgumentNullException.ThrowIfNull(managed);

			return (managed(UnityBaseUrl) ?? string.Empty, managed(UnityApiKey) ?? string.Empty);
		}

		/// <summary>The compliance contact Freedom holds; empty without one.</summary>
		public static string Compliance(Func<string, string?> managed)
		{
			ArgumentNullException.ThrowIfNull(managed);

			return managed(ComplianceEmail) ?? string.Empty;
		}

		/// <summary>
		/// Lays what Freedom holds over <paramref name="config"/>, in place.
		/// <see cref="Smtp"/> is how the app calls it; this stays public for
		/// the edge tests that pin how a value that will not parse is treated.
		/// </summary>
		public static SmtpConfiguration Apply(SmtpConfiguration config, Func<string, string?> managed)
		{
			config.Host = managed(SmtpHost) ?? config.Host;
			config.Username = managed(SmtpUsername) ?? config.Username;
			config.Password = managed(SmtpPassword) ?? config.Password;
			config.FromDisplayName = managed(SmtpFromDisplayName) ?? config.FromDisplayName;

			if (int.TryParse(managed(SmtpPort), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
				config.Port = port;
			if (int.TryParse(managed(SmtpTimeoutSeconds), NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeout))
				config.TimeoutSeconds = timeout;
			if (bool.TryParse(managed(SmtpEnableSsl), out var ssl))
				config.EnableSsl = ssl;

			return config;
		}

		/// <summary>Lays what Freedom holds over <paramref name="config"/>, in place.</summary>
		public static BetterStackConfiguration Apply(BetterStackConfiguration config, Func<string, string?> managed)
		{
			config.Endpoint = managed(BetterStackEndpoint) ?? config.Endpoint;
			config.SourceToken = managed(BetterStackSourceToken) ?? config.SourceToken;

			return config;
		}
	}
}
