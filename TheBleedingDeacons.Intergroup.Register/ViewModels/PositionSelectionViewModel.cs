using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class PositionSelectionViewModel : BaseViewModel
    {
        private static readonly ILogger Logger = AppLogger.ForContext<PositionEditViewModel>();

        private readonly IPositionRepository _positionRepository;

        [ObservableProperty]
        bool isLoading = false;

        [ObservableProperty]
        bool isDataLoaded = false;

        [ObservableProperty]
        string header = string.Empty;

        public ObservableCollection<Position> Positions { get; } = new();

        public PositionSelectionViewModel(IPositionRepository positionRepository)
        {
            _positionRepository = positionRepository;
        }

        [RelayCommand]
        public async Task LoadDataAsync()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;
                IsLoading = true;
                IsDataLoaded = false;


                await Task.Yield();

                Positions.Clear();

                var allPositions = await _positionRepository.GetAllPositionsAsync();

                MainThread.BeginInvokeOnMainThread(() =>
                {

                    foreach (var Position in allPositions)
                    {
                        Positions.Add(Position);
                    }

                    IsDataLoaded = true;
                    IsLoading = false;
                });

            }
            finally
            {
                IsBusy = false;
                IsLoading = false;
            }
        }

        [RelayCommand]
        async Task SelectPosition(Position Position)
        {
            if (Position == null) return;

            await ShowFeedback();

            var parameters = new Dictionary<string, object> {
                {"positionId", Position.ID.ToString()} };

            await Shell.Current.GoToAsync(nameof(PositionEditPage), parameters);

        }

        private async Task ShowFeedback()
        {
            await Task.Delay(100);
            await Toast.Make("Loading Position...", ToastDuration.Short).Show();
        }
    }
}