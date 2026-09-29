using TheBleedingDeacons.Intergroup.Register.Services;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	/// <summary>
	/// Which of the Settings page's switches Freedom sets for this tablet. A
	/// switch the site sets is shown, greyed out, at the site's value: toggling
	/// it here would be overridden on the very next read.
	/// </summary>
	public partial class SettingsViewModel
	{
		public bool IsRegistrationEventLogEnabledEditable => !_configService.IsManaged(FreedomSettings.FeatureRegistrationLog);

		public bool IsAutoRegisterPositionsOnGroupEnabledEditable => !_configService.IsManaged(FreedomSettings.FeatureAutoRegisterPositions);

		public bool IsSingleGsrShortcutEnabledEditable => !_configService.IsManaged(FreedomSettings.FeatureSingleGsrShortcut);

		public bool IsWelcomeEmailOnRegistrationEnabledEditable => !_configService.IsManaged(FreedomSettings.FeatureWelcomeEmail);

		public bool IsAddPositionHolderEnabledEditable => !_configService.IsManaged(FreedomSettings.FeatureAddPositionHolder);

		public bool IsAnySwitchManaged =>
			!IsRegistrationEventLogEnabledEditable
			|| !IsAutoRegisterPositionsOnGroupEnabledEditable
			|| !IsSingleGsrShortcutEnabledEditable
			|| !IsWelcomeEmailOnRegistrationEnabledEditable
			|| !IsAddPositionHolderEnabledEditable;
	}
}
