using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
	public interface IPopupNotification
	{
		Task ShowCountdownPopupAsync(string title, string message, Func<Task> navigationAction);

		/// <summary>
		/// Shows a modal error dialog with a single dismiss button.
		/// Centralised here so error presentation (styling, icons, or a
		/// future migration from DisplayAlert to a custom popup) can be
		/// changed in one place.
		/// </summary>
		Task ShowErrorAsync(string title, string message);
	}
}