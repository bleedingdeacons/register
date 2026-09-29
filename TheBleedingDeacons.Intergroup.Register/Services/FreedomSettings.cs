using System.Globalization;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
	/// <summary>
	/// The settings Freedom can manage for Register, and how a managed value
	/// is laid over the one stored on the tablet.
	///
	/// <para><b>Freedom wins where it holds a value, and nothing else
	/// changes.</b> A key Freedom does not hold — because the site has not
	/// set it, the tablet has not signed in, or the build has no Freedom
	/// settings at all — falls through to exactly what the tablet stored
	/// before, so a tablet set up by hand keeps working and one moved over to
	/// Freedom stops needing anything typed in. That is what lets this ship
	/// ahead of the site being configured.</para>
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

		// The feature switches. Each is "true" or "false" on the site; a key
		// the site does not set leaves the tablet's own switch in charge.
		public const string FeatureRegistrationLog = "features.registration_log";
		public const string FeatureAutoRegisterPositions = "features.auto_register_positions";
		public const string FeatureComplianceLog = "features.compliance_log";
		public const string FeatureSingleGsrShortcut = "features.single_gsr_shortcut";
		public const string FeatureAddPositionHolder = "features.add_position_holder";
		public const string FeatureWelcomeEmail = "features.welcome_email";
		public const string FeatureComplianceAcceptanceEmail = "features.compliance_acceptance_email";

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
		public const string FreedomCallbackUri = "com.thebleedingdeacons.intergroup.register.freedom://auth";

		/// <summary>
		/// A switch's value as the site set it: true/false, also yes/no, on/off
		/// and 1/0, since an admin types it. Null for unset or unrecognised,
		/// which leaves the tablet's own switch in charge rather than guessing.
		/// </summary>
		public static bool? Flag(string? value) => value?.Trim().ToLowerInvariant() switch
		{
			"true" or "yes" or "on" or "1" => true,
			"false" or "no" or "off" or "0" => false,
			_ => null,
		};

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

		public static BetterStackConfiguration Apply(BetterStackConfiguration config, Func<string, string?> managed)
		{
			config.Endpoint = managed(BetterStackEndpoint) ?? config.Endpoint;
			config.SourceToken = managed(BetterStackSourceToken) ?? config.SourceToken;

			return config;
		}
	}
}
