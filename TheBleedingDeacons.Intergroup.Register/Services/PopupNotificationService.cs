using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Views;
using CommunityToolkit.Maui.Extensions;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{

	public class PopupNotificationService : IPopupNotification
	{
		private static readonly ILogger Logger = AppLogger.ForContext<PopupNotificationService>();

		public async Task<bool> ShowTerms(string title, string text)
		{
			var popup = new AcceptTermsPopup(title, text);

			// Resolve the active page the same way ShowCountdownPopupAsync
			// does — Application.Current.Windows is the supported path on
			// MAUI now that MainPage is deprecated.
			var currentPage = Application.Current?.Windows?.FirstOrDefault()?.Page;

			if (currentPage is not Page page)
			{
				// No host page to attach to. Treat as "did not consent" so
				// callers default to the safe branch (don't proceed as if
				// the user agreed). Mirrors ShowErrorAsync's defensive
				// handling of the same condition.
				Logger.Warning(
					"ShowTerms called with no active page. Title={Title}", title);
				return false;
			}

			// Same Shape=null trick used for the countdown popup: stops the
			// CommunityToolkit wrapper from drawing its own bordered card
			// around our M3CardStyle Border.
			await page.ShowPopupAsync(popup, new PopupOptions { Shape = null });

			// AcceptTermsPopup completes Result on Accept / Decline / close.
			return await popup.Result;
		}

		public async Task ShowCountdownPopupAsync(string title, string message, Func<Task> navigationAction)
		{
			var popup = new CountdownPopup(title, message, navigationAction);

			// Use the Windows collection to obtain the active Page instead of the deprecated MainPage property
			var currentPage = Application.Current?.Windows?.FirstOrDefault()?.Page;

			if (currentPage is Page page)
			{
				// CommunityToolkit.Maui v2 wraps every popup in its own
				// rounded Border (with a visible stroke) on top of whatever
				// the popup XAML defines. That stroke shows up as the white
				// outline around our M3 card. Setting PopupOptions.Shape to
				// null disables the toolkit's wrapping Border entirely so
				// only the inner M3CardStyle Border is rendered.
				await page.ShowPopupAsync(popup, new PopupOptions { Shape = null });
			}
		}

		public async Task ShowErrorAsync(string title, string message)
		{
			// Resolve the currently-active Page the same way
			// ShowCountdownPopupAsync does, rather than going through
			// Shell.Current — works on pages that aren't inside Shell
			// (popups, modals) and avoids a NullReferenceException if
			// Shell hasn't initialised yet.
			var currentPage = Application.Current?.Windows?.FirstOrDefault()?.Page;

			if (currentPage is null)
			{
				// Nothing to attach a dialog to — log and swallow. Throwing
				// from an error-reporting path just turns one failure into two.
				Logger.Warning(
					"ShowErrorAsync called with no active page. Title={Title}, Message={Message}",
					title, message);
				return;
			}

			try
			{
				await currentPage.DisplayAlertAsync(title, message, "OK");
			}
			catch (Exception ex)
			{
				// DisplayAlertAsync can throw if the page is torn down between
				// resolution and the call. Log, don't propagate.
				Logger.Warning(ex, "Failed to display error dialog: {Title} / {Message}", title, message);
			}
		}
	}
}