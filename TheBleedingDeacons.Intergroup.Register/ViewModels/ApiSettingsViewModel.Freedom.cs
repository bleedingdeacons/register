using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Freedom.Client;
using TheBleedingDeacons.Intergroup.Register.Services;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	/// <summary>
	/// The Freedom section of the API settings page: sign the tablet in,
	/// check for changes now, or sign it out. Every credential and endpoint
	/// the tablet uses comes through here; without it the tablet has none.
	/// Absent — the section hidden — when the build names no Freedom site.
	/// </summary>
	public partial class ApiSettingsViewModel
	{
		private readonly FreedomClient? _freedom;

		public bool IsFreedomAvailable => _freedom is not null;

		[ObservableProperty]
		[NotifyPropertyChangedFor(nameof(IsFreedomIdle))]
		private bool isFreedomBusy;

		public bool IsFreedomIdle => !IsFreedomBusy;

		[ObservableProperty]
		private string freedomStatus = string.Empty;

		[RelayCommand]
		private async Task SignInToFreedomAsync()
		{
			if (_freedom is null)
				return;

			IsFreedomBusy = true;
			try
			{
				var result = await _freedom.EnrolAsync();

				// A cancel is not a failure and has nothing to say; a refusal
				// carries the site's own words.
				FreedomStatus = result.Status switch
				{
					EnrolmentStatus.Cancelled => FreedomStatus,
					_ when result.Succeeded => Describe(result.Sync) ?? result.Message,
					_ => result.Message,
				};

				if (result.Succeeded)
				{
					_configService.InvalidateCache();
					await LoadConfigurationAsync();
				}
			}
			finally
			{
				IsFreedomBusy = false;
			}
		}

		[RelayCommand]
		private async Task SyncFreedomAsync()
		{
			if (_freedom is null)
				return;

			IsFreedomBusy = true;
			try
			{
				FreedomStatus = Describe(await FreedomStartup.SyncAsync(_freedom)) ?? FreedomStatus;
				_configService.InvalidateCache();
				await LoadConfigurationAsync();
			}
			finally
			{
				IsFreedomBusy = false;
			}
		}

		[RelayCommand]
		private async Task SignOutOfFreedomAsync()
		{
			if (_freedom is null)
				return;

			IsFreedomBusy = true;
			try
			{
				await _freedom.SignOutAsync();
				FreedomStatus = "Signed out. This tablet has no mail, Unity or logging settings until it signs in again.";
				_configService.InvalidateCache();
				await LoadConfigurationAsync();
			}
			finally
			{
				IsFreedomBusy = false;
			}
		}

		private void RefreshFreedomStatus()
		{
			if (_freedom is null)
				return;

			var current = _freedom.Current;
			FreedomStatus = current.Values.Count == 0
				? "Not signed in, or nothing set on the site yet."
				: $"{current.Values.Count} setting(s) from the site; last checked {Checked(current.VerifiedAt)}.";
		}

		private static string? Describe(SyncResult? sync) => sync?.Status switch
		{
			null => null,
			SyncStatus.UpToDate => $"Up to date; checked {Checked(sync.VerifiedAt)}.",
			SyncStatus.Updated => $"Updated {sync.Updated.Count} setting(s), removed {sync.Removed.Count}.",
			SyncStatus.Offline => $"The site could not be reached; using settings from {Checked(sync.VerifiedAt)}.",
			SyncStatus.KeyFault => "Some settings could not be opened. Sign in again.",
			_ => sync.Message,
		};

		private static string Checked(DateTimeOffset? at) =>
			at is null ? "never" : at.Value.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
	}
}
