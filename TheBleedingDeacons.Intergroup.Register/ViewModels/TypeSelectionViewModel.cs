using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
	public partial class TypeSelectionViewModel : BaseViewModel
	{
		public TypeSelectionViewModel()
		{
			Title = "Meeting Type";
		}

		[RelayCommand]
		private async Task Online()
		{
			await SelectType(true);
		}

		[RelayCommand]
		private async Task Face2Face()
		{
			await SelectType(false);
		}

		private async Task SelectType(bool isOnline)
		{

			await ShowFeedback();

			var meetingType = isOnline ? "Online" : "Face 2 Face";

			var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { { "meetingType", meetingType } };

			// Navigate to the day selection page
			await Shell.Current.GoToAsync(nameof(DaySelectionPage), parameters);
		}

		[RelayCommand]
		private async Task GoBack()
		{
			// Navigate back to the previous page
			await Shell.Current.GoToAsync("..");
		}
		
	}
}
