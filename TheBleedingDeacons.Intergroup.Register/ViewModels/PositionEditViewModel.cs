using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class PositionEditViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<PositionEditViewModel>();

    private readonly IPositionRepository _positionRepository;

    [ObservableProperty]
    private Position? _position;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string? _editMemberAnonymousName;

    [ObservableProperty]
    private string? _editMemberPersonalEmail;

    [ObservableProperty]
    private string? _editMemberMobile;

    [ObservableProperty]
    private bool _canConfirm;

    // Display properties that will notify when changed
    [ObservableProperty]
    private string? _displayMemberAnonymousName;

    [ObservableProperty]
    private string? _displayMemberPersonalEmail;

    [ObservableProperty]
    private string? _displayMemberMobile;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private bool _canSave;

    private readonly IPopupNotification _popupService;
    private readonly IAttendanceRegistration<Position> _attendanceRegistration;

    public PositionEditViewModel(IPositionRepository positionRepository, IPopupNotification popupService, IAttendanceRegistration<Position> attendanceRegistration)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _attendanceRegistration = attendanceRegistration ?? throw new ArgumentNullException(nameof(attendanceRegistration));
        _popupService = popupService;
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("position", out var positionObj) && positionObj is Position position)
        {
            Initialize(position);
        }
        else if (query.TryGetValue("positionId", out var positionIdObj) &&
                 positionIdObj is string positionIdStr &&
                 int.TryParse(positionIdStr, out var positionId))
        {
            // Load position by ID if only ID was passed
            _ = LoadPositionByIdAsync(positionId);
        }
    }

    private async Task LoadPositionByIdAsync(int positionId)
    {
        try
        {
            var position = await _positionRepository.GetPositionByIdAsync(positionId);
            if (position != null)
            {
                Initialize(position);
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", "Position not found.", "OK");
                await Shell.Current.GoToAsync("//MainPage");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to load position: {ex.Message}", "OK");
            await Shell.Current.GoToAsync("//MainPage");
        }
    }

    public void Initialize(Position position)
    {
        Position = position;
        UpdateDisplayProperties();
        UpdateCanConfirm();
    }

    partial void OnPositionChanged(Position? value)
    {
        UpdateDisplayProperties();
        UpdateCanConfirm();
    }

    private void UpdateDisplayProperties()
    {
        if (Position == null) return;
        DisplayMemberAnonymousName = Position.MemberAnonymousName;
        DisplayMemberPersonalEmail = Position.MemberPersonalEmail;
        DisplayMemberMobile = Position.MemberMobile;
        Title = Position.PositionName ?? "Position";
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value)
        {
            StartEditing();
        }
        UpdateCanSave();
    }

    partial void OnEditMemberAnonymousNameChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditMemberPersonalEmailChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditMemberMobileChanged(string? value)
    {
        UpdateCanSave();
    }

    private void StartEditing()
    {
        if (Position == null) return;

        EditMemberAnonymousName = DisplayMemberAnonymousName;
        EditMemberPersonalEmail = DisplayMemberPersonalEmail;
        EditMemberMobile = DisplayMemberMobile;

        UpdateCanSave();
    }

    private void UpdateCanConfirm()
    {
        // Member Anonymous Name is required
        bool hasMemberName = !string.IsNullOrWhiteSpace(DisplayMemberAnonymousName);

        // Either Member Mobile or Member Email is required (but not necessarily both)
        bool hasMemberContact = !string.IsNullOrWhiteSpace(DisplayMemberMobile) ||
                               !string.IsNullOrWhiteSpace(DisplayMemberPersonalEmail);

        CanConfirm = hasMemberName && hasMemberContact;

        // Update validation message
        UpdateValidationMessage(hasMemberName, hasMemberContact);
    }

    private void UpdateValidationMessage(bool hasMemberName, bool hasMemberContact)
    {
        var errors = new List<string>();

        if (!hasMemberName)
            errors.Add("Member Name is required");

        if (!hasMemberContact)
            errors.Add("Either Member Email or Member Mobile is required");

        HasValidationErrors = errors.Count > 0;
        ValidationMessage = errors.Count > 0 ? string.Join(", ", errors) : null;
    }

    private void UpdateCanSave()
    {
        if (!IsEditing)
        {
            CanSave = false;
            return;
        }

        bool hasMemberName = !string.IsNullOrWhiteSpace(EditMemberAnonymousName);
        bool hasMemberContact = !string.IsNullOrWhiteSpace(EditMemberMobile) ||
                               !string.IsNullOrWhiteSpace(EditMemberPersonalEmail);

        CanSave = hasMemberName && hasMemberContact;
    }

    [RelayCommand]
    private void StartEdit()
    {
        IsEditing = true;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Position == null) return;

        await ShowSaveFeedback();

        // Validate required fields before saving
        bool hasMemberName = !string.IsNullOrWhiteSpace(EditMemberAnonymousName);
        bool hasMemberContact = !string.IsNullOrWhiteSpace(EditMemberMobile) ||
                               !string.IsNullOrWhiteSpace(EditMemberPersonalEmail);

        if (!hasMemberName || !hasMemberContact)
        {
            var errors = new List<string>();

            if (!hasMemberName)
                errors.Add("Member Name is required");

            if (!hasMemberContact)
                errors.Add("Either Member Email or Member Mobile is required");

            string errorMessage = string.Join("\n", errors);
            await Shell.Current.DisplayAlert("Validation Error", $"Please fix the following errors:\n\n{errorMessage}", "OK");
            return;
        }

        try
        {
            // Copy edited values back to the position
            Position.MemberAnonymousName = EditMemberAnonymousName;
            Position.MemberPersonalEmail = EditMemberPersonalEmail;
            Position.MemberMobile = EditMemberMobile;

            // Save to repository
            var savedPosition = await _positionRepository.SavePositionAsync(Position);

            // Update the Position property to trigger UI refresh
            Position = savedPosition;

            // Exit editing mode
            IsEditing = false;

            // Show success message
            //await Shell.Current.DisplayAlert("Success", "Position information has been saved.", "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to save position: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (IsEditing)
        {
            // Check if there are unsaved changes
            bool hasChanges = HasUnsavedChanges();

            if (hasChanges)
            {
                bool shouldDiscard = await Shell.Current.DisplayAlert(
                    "Discard Changes?",
                    "You have unsaved changes. Are you sure you want to discard them?",
                    "Discard",
                    "Keep Editing");

                if (!shouldDiscard)
                    return;
            }

            IsEditing = false;
        }
    }

    [RelayCommand]
    private async Task Confirm()
    {
        if (!CanConfirm) return;

        try
        {
            // Mark as attended and save
            if (Position != null)
            {
                await _attendanceRegistration.Register(Position);

                string memberName = Position.MemberAnonymousName ?? "Member";

                // Show success popup
                await _popupService.ShowCountdownPopupAsync(
                    "Finished",
                    $"Thanks for registering {memberName}.",
                    async () => await Shell.Current.GoToAsync("//MainPage")
                );
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to confirm position: {ex.Message}", "OK");
        }
    }

    private bool HasUnsavedChanges()
    {
        return EditMemberAnonymousName != DisplayMemberAnonymousName ||
               EditMemberPersonalEmail != DisplayMemberPersonalEmail ||
               EditMemberMobile != DisplayMemberMobile;
    }

    private async Task ShowSaveFeedback()
    {
        //await Task.Delay(100);
        //await Toast.Make("Updating Position...", ToastDuration.Short).Show();
    }
}