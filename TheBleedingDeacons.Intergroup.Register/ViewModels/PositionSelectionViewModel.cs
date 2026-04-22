using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class PositionSelectionViewModel : BaseViewModel
    {
        private static readonly ILogger Logger = AppLogger.ForContext<PositionSelectionViewModel>();

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
            Header = "My Intergroup Position is";
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

                var allPositions = await _positionRepository.GetAllAsync();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    foreach (var position in allPositions)
                    {
                        Positions.Add(position);
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
        async Task SelectPosition(Position position)
        {
            if (position == null) return;

            var parameters = new Dictionary<string, object> {
        {"positionId", position.Id.ToString()} };

            await Shell.Current.GoToAsync(nameof(VerifyPositionPage), parameters);
        }
    }
}
