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
	}
}