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

				var allPositions = await _positionRepository.GetAllAsync(Token);

				// Clear and repopulate in a single synchronous block: no await
				// between them, so the bound CollectionView never observes the
				// collection half-updated. Both run on the UI thread because
				// the page invokes this from OnAppearing without Task.Run.
				Positions.Clear();

				foreach (var position in allPositions)
				{
					Positions.Add(position);
				}

				IsDataLoaded = true;
			}
			catch (OperationCanceledException)
			{
				// Navigated away mid-load — the view-model has been disposed
				// and there is nothing left to publish.
			}
			finally
			{
				// Released only once the collection is fully rebuilt, so the
				// IsBusy guard above genuinely serialises overlapping loads.
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
