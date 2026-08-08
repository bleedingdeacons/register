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
		private readonly IPositionRepository _positionRepository;

		[ObservableProperty]
		private bool isLoading = false;

		[ObservableProperty]
		private bool isDataLoaded = false;

		public ObservableCollection<Position> Positions { get; } = new();

		public PositionSelectionViewModel(IPositionRepository positionRepository)
		{
			_positionRepository = positionRepository;
			Title = "My Intergroup Position is";
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
		private async Task SelectPosition(Position position)
		{
			if (position == null) return;

			var parameters = new Dictionary<string, object>(StringComparer.Ordinal) {
		{"positionId", position.Id.ToString()} };

			await Shell.Current.GoToAsync(nameof(VerifyPositionPage), parameters);
		}
	}
}
