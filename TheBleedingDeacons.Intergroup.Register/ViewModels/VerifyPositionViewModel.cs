using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Intergroup.Register.Views;
using TheBleedingDeacons.Unity.Intergroup.Entities;
using TheBleedingDeacons.Unity.Intergroup.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

/// <summary>
/// Handles the read-only verification and registration flow for a position.
/// The user confirms the position holder details are correct (Yes) or navigates to edit them (No).
///
/// Displays ALL active holders for the position as a member-centric list.
///
/// Receives a positionId from navigation and loads the Position (with Holders)
/// from <see cref="IPositionRepository"/>, so verify/edit always operate on the position.
/// </summary>
[QueryProperty(nameof(PositionId), "positionId")]
[QueryProperty(nameof(Edited), "edited")]
public partial class VerifyPositionViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<VerifyPositionViewModel>();

    private readonly IAttendanceRegistration<Position> _attendanceRegistration;
    private readonly IPositionRepository _positionRepository;
    private readonly IPopupNotification _popupService;

    [ObservableProperty]
    private Position? position;

    [ObservableProperty]
    private int positionId;

    [ObservableProperty]
    private string attendedStatusText = string.Empty;

    [ObservableProperty]
    private bool edited;

    [ObservableProperty]
    private bool standingIn;

    [ObservableProperty]
    private string? standinEmail;

    [ObservableProperty]
    private string? standinName;

    [ObservableProperty]
    private bool canRegister;

    [ObservableProperty]
    private bool isLoading;

    /// <summary>
    /// Active holders for the position, displayed as a list.
    /// </summary>
    public ObservableCollection<Member> ActiveHolders { get; } = new();

    /// <summary>
    /// True when the position has at least one active holder to display.
    /// </summary>
    [ObservableProperty]
    private bool hasActiveHolders;

    /// <summary>
    /// Descriptive text showing how many holders are registered for this position.
    /// </summary>
    [ObservableProperty]
    private string holderCountText = string.Empty;

    public VerifyPositionViewModel(
        IAttendanceRegistration<Position> attendanceRegistration,
        IPositionRepository positionRepository,
        IPopupNotification popupService)
    {
        _attendanceRegistration = attendanceRegistration;
        _positionRepository = positionRepository;
        _popupService = popupService;
    }

    #region Query Attributes Handling

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Logger.Information("VerifyPositionViewModel.ApplyQueryAttributes called with {Count} parameters", query.Count);

        // Handle edited flag returning from Edit flow — reload from DB so updated
        // holder values are reflected rather than the stale in-memory Position object.
        if (query.TryGetValue("edited", out var editedObj) &&
            editedObj?.ToString() == "true")
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (PositionId > 0)
                    await LoadPositionAsync(PositionId);
            });
        }

        // Handle positionId passed from navigation
        if (query.TryGetValue("positionId", out var positionIdObj))
        {
            int parsedPositionId = 0;

            if (positionIdObj is string positionIdStr)
                int.TryParse(positionIdStr, out parsedPositionId);
            else if (positionIdObj is int intValue)
                parsedPositionId = intValue;

            if (parsedPositionId > 0)
                PositionId = parsedPositionId;
        }
    }

    #endregion

    #region Property Change Handlers

    partial void OnPositionIdChanged(int value)
    {
        Logger.Information("OnPositionIdChanged triggered with value: {Value}", value);

        if (value > 0)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await LoadPositionAsync(value);
            });
        }
    }

    partial void OnPositionChanged(Position? value)
    {
        if (value != null)
        {
            UpdateTitle();
            RefreshActiveHolders();
            UpdateCanRegister();
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// User indicates their details are NOT correct — navigate to edit page.
    /// </summary>
    [RelayCommand]
    public async Task No()
    {
        if (Position == null)
        {
            Logger.Warning("Cannot navigate to edit - Position is null");
            return;
        }

        var parameters = new Dictionary<string, object>
        {
            ["position"] = Position
        };

        await Shell.Current.GoToAsync(nameof(PositionEditPage), parameters);
    }

    /// <summary>
    /// User confirms details are correct — register attendance for the position.
    /// </summary>
    [RelayCommand]
    public async Task Yes()
    {
        if (Position == null)
        {
            Logger.Warning("Cannot register - Position is null");
            return;
        }

        try
        {
            await _attendanceRegistration.Register(Position);

            await _popupService.ShowCountdownPopupAsync(
                "Finished",
                $"Thanks for registering {Position.ShortDescription}.",
                async () => await Shell.Current.GoToAsync("//MainPage")
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to register attendance");

            var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (mainPage != null)
            {
                await mainPage.DisplayAlert("Error", $"Failed to register: {ex.Message}", "OK");
            }
        }
    }

    #endregion

    #region Private Methods

    private async Task LoadPositionAsync(int positionId)
    {
        Logger.Information("LoadPositionAsync called with positionId: {PositionId}", positionId);

        if (IsLoading) return;

        try
        {
            IsLoading = true;

            var loadedPosition = await _positionRepository.GetByIdWithHoldersAsync(positionId);

            if (loadedPosition != null)
            {
                Position = loadedPosition;
            }
            else
            {
                Logger.Warning("Position not found for ID: {PositionId}", positionId);
                var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Not Found", $"Position with ID {positionId} was not found.", "OK");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load position {PositionId}", positionId);

            try
            {
                var mainPage = Application.Current?.Windows?.FirstOrDefault()?.Page;
                if (mainPage != null)
                {
                    await mainPage.DisplayAlert("Error", $"Failed to load position: {ex.Message}", "OK");
                }
            }
            catch (Exception alertEx)
            {
                Logger.Error(alertEx, "Failed to show error alert");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Rebuild the observable list of active holder members from the loaded Position.
    /// Mirrors <see cref="VerifyGroupViewModel.RefreshActiveGsrs"/> for consistency.
    /// </summary>
    private void RefreshActiveHolders()
    {
        ActiveHolders.Clear();

        if (Position?.Holders != null)
        {
            foreach (var holder in Position.Holders)
            {
                ActiveHolders.Add(holder);
            }
        }

        HasActiveHolders = ActiveHolders.Count > 0;

        var count = ActiveHolders.Count;
        HolderCountText = count switch
        {
            0 => "No Position Holders",
            1 => "1 Position Holder",
            _ => $"{count} Position Holders"
        };
    }

    private void UpdateTitle()
    {
        Title = !string.IsNullOrEmpty(Position?.ShortDescription)
            ? Position!.ShortDescription
            : "Position Verification";
    }

    private void UpdateCanRegister()
    {
        // At least one active holder must have required contact fields
        CanRegister = ActiveHolders.Any(h =>
            !string.IsNullOrEmpty(h.AnonymousName) &&
            (!string.IsNullOrEmpty(h.MobileNumber) || !string.IsNullOrEmpty(h.PersonalEmail)));
    }

    #endregion
}