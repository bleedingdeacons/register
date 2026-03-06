using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    [QueryProperty(nameof(Day), "day")]
    public partial class TypeSelectionViewModel : BaseViewModel
    {
        private static readonly ILogger Logger = AppLogger.ForContext<TypeSelectionViewModel>();

        [ObservableProperty]
        private string day;

        public TypeSelectionViewModel()
        {
            Title = "Select Meeting Type";
            day = string.Empty;
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

        async Task SelectType(bool isOnline)
        {

            await ShowFeedback();

            var criteria = new MeetingCriteria() { MeetingType = isOnline ? "Online" : "Face 2 Face", Day = Day };

            var parameters = new Dictionary<string, object> { { "criteria", criteria } };            

            // Navigate to the next page
            await Shell.Current.GoToAsync(nameof(GroupSelectionPage), parameters);
        }

        [RelayCommand]
        async Task GoBack()
        {
            // Navigate back to the previous page
            await Shell.Current.GoToAsync("..");
        }
        private async Task ShowFeedback()
        {
            await Task.Delay(100);
        }
    }
}
