using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Models;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	public interface IConfigurationService
	{

		SmtpConfiguration GetSmtpConfiguration();
		Task SaveSmtpConfigurationAsync(SmtpConfiguration config);
		Task<SmtpConfiguration> LoadSmtpConfigurationAsync();

		Task SaveUnityConfigurationAsync(UnityConfiguration config);
		Task<UnityConfiguration> LoadUnityConfigurationAsync();

		/// <summary>Persists only the active intergroup meeting ID, leaving all other settings untouched.</summary>
		Task SaveActiveIntergroupMeetingAsync(int? meetingId);

		BetterStackConfiguration GetBetterStackConfiguration();
		Task SaveBetterStackConfigurationAsync(BetterStackConfiguration config);
		Task<BetterStackConfiguration> LoadBetterStackConfigurationAsync();

		/// <summary>
		/// When true, each registration action is also appended to the
		/// crash-durable registration event log. When false, the log is
		/// not written — but any existing log file is still replayed on
		/// the next reconcile, and purge still runs on success.
		/// Defaults to true; callers should treat a missing value as true.
		/// </summary>
		bool IsRegistrationEventLogEnabled { get; }

		/// <summary>
		/// Persists the registration event log toggle. Takes effect on the
		/// next registration action; no restart required.
		/// </summary>
		void SetRegistrationEventLogEnabled(bool enabled);

		/// <summary>
		/// When true, registering a group also registers any intergroup position
		/// held by one of its members in the same operation. Useful when an
		/// officer also serves as GSR for their home group and would otherwise
		/// need to be registered twice.
		/// Defaults to true; callers should treat a missing value as true.
		/// </summary>
		bool IsAutoRegisterPositionsOnGroupEnabled { get; }

		/// <summary>
		/// Persists the auto-register-positions toggle. Takes effect on the
		/// next group registration; no restart required.
		/// </summary>
		void SetAutoRegisterPositionsOnGroupEnabled(bool enabled);

		/// <summary>
		/// When true, each compliance acceptance / revocation is also
		/// appended to the crash-durable compliance event log. When false,
		/// the log is not written — but any existing log file is still
		/// replayed on the next reconcile, and purge still runs on success.
		/// Defaults to true; callers should treat a missing value as true.
		/// </summary>
		bool IsComplianceEventLogEnabled { get; }

		/// <summary>
		/// Persists the compliance event log toggle. Takes effect on the
		/// next compliance action; no restart required.
		/// </summary>
		void SetComplianceEventLogEnabled(bool enabled);

		/// <summary>
		/// When true, the verify-group flow takes a shortcut whenever a group
		/// has exactly one GSR: tapping "No" opens that GSR's edit form
		/// directly (skipping the picker), and tapping "Finished" on that
		/// edit form auto-registers attendance on return. When false, the
		/// flow always lands on the GSR list and Finished returns to Verify
		/// without registering — the user must tap "Yes" themselves.
		/// Defaults to false; callers should treat a missing value as false.
		/// </summary>
		bool IsSingleGsrShortcutEnabled { get; }

		/// <summary>
		/// Persists the single-GSR shortcut toggle. Takes effect on the next
		/// verify-group entry; no restart required.
		/// </summary>
		void SetSingleGsrShortcutEnabled(bool enabled);

		/// <summary>
		/// When true, a welcome / confirmation email is queued for each
		/// recipient as part of every successful registration:
		/// <list type="bullet">
		/// <item>Group registrations email each active GSR (and any member
		/// whose held position is auto-registered as a cascade), deduped
		/// by member id so a person who fits both criteria is emailed once.</item>
		/// <item>Position registrations email each holder.</item>
		/// </list>
		/// Defaults to <c>false</c> — fresh installs do not send any
		/// registration-time email until an operator explicitly opts in
		/// from Settings. Members without a <c>PersonalEmail</c> on file
		/// are silently skipped regardless of the toggle.
		/// </summary>
		bool IsWelcomeEmailOnRegistrationEnabled { get; }

		/// <summary>
		/// Persists the welcome-email toggle. Takes effect on the next
		/// registration action; no restart required.
		/// </summary>
		void SetWelcomeEmailOnRegistrationEnabled(bool enabled);

		/// <summary>
		/// A short human-readable label that identifies this physical device
		/// in the Better Stack live tail (and any other log sink). Resolution
		/// order:
		/// <list type="number">
		/// <item>The user-set value persisted in Preferences, if any.</item>
		/// <item>Otherwise, an auto-generated default that combines manufacturer,
		///       model, and OS version so two physically-similar devices on
		///       different OS versions are still distinguishable.</item>
		/// </list>
		/// On desktop (Windows / macOS Catalyst) the auto-default is
		/// <c>Environment.MachineName</c>, which is meaningful there. On Android
		/// it's <c>"Pixel Tablet (Android 16)"</c>-style; on iOS it's the
		/// model-plus-OS-version. Never returns <c>"localhost"</c>.
		/// </summary>
		string DeviceLabel { get; }

		/// <summary>
		/// Persists a user-chosen device label and rebuilds the Serilog pipeline
		/// so the new value appears in subsequent log events without an app
		/// restart. Pass an empty / whitespace string to clear the override and
		/// fall back to the auto-default.
		/// </summary>
		void SetDeviceLabel(string? label);

		/// <summary>
		/// When true, a confirmation email is queued for each member who
		/// accepts the active privacy policy via
		/// <see cref="Services.ComplianceService.RecordAcceptance"/>. The
		/// email captures the audit trail (timestamp, capture method,
		/// policy version, policy contact details) and the exact statement
		/// the member accepted, giving them a copy independent of the
		/// local SQLite store.
		///
		/// <para>Defaults to <c>true</c> — fresh installs do not send
		/// any acceptance-confirmation email until an operator explicitly
		/// opts in from Settings. Members without a <c>PersonalEmail</c>
		/// on file are silently skipped regardless of the toggle.</para>
		///
		/// <para>Read per-call inside <c>ComplianceService</c> so flipping
		/// the value in Settings takes effect on the next acceptance
		/// without an app restart.</para>
		/// </summary>
		bool IsComplianceAcceptanceEmailEnabled { get; }

		/// <summary>
		/// Persists the compliance-acceptance-email toggle. Takes effect
		/// on the next acceptance action; no restart required.
		/// </summary>
		void SetComplianceAcceptanceEmailEnabled(bool enabled);

		/// <summary>
		/// The email address used by the compliance service — typically
		/// the data-protection / compliance contact who should receive
		/// audit-trail copies of acceptance and revocation events. Read
		/// per-call from Preferences so flipping the value in Settings
		/// takes effect on the next compliance action without an app
		/// restart. Returns an empty string when no value has been set,
		/// which callers should treat as "no compliance recipient
		/// configured" and skip any send that would otherwise target it.
		/// </summary>
		string ComplianceEmail { get; }

		/// <summary>
		/// Persists the compliance email address. Pass an empty / whitespace
		/// string to clear the value (no compliance recipient configured).
		/// The address is trimmed before storage; validity is the caller's
		/// responsibility — the Settings page validates with
		/// <see cref="System.ComponentModel.DataAnnotations.EmailAddressAttribute"/>
		/// before invoking this setter.
		/// </summary>
		void SetComplianceEmail(string? email);
	}
}