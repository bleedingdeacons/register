using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Extensions;
using TheBleedingDeacons.Intergroup.Register.Models;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class GroupEditViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<GroupEditViewModel>();

    private readonly IGroupRepository _groupRepository;

    [ObservableProperty]
    private Group? _group;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string? _editGsrName;

    [ObservableProperty]
    private string? _editGsrEmailPersonal;

    [ObservableProperty]
    private string? _editGsrPhone;

    [ObservableProperty]
    private string? _editGroupGenericEmail;

    [ObservableProperty]
    private bool _editUsingGeneric;

    [ObservableProperty]
    private bool _canConfirm;

    // Display properties that will notify when changed
    [ObservableProperty]
    private string? _displayGsrName;

    [ObservableProperty]
    private string? _displayGsrEmailPersonal;

    [ObservableProperty]
    private string? _displayGsrPhone;

    [ObservableProperty]
    private string? _displayGroupGenericEmail;

    [ObservableProperty]
    private bool _displayUsingGeneric;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private bool _canSave;

    private readonly IPopupNotification _popupService;
    private readonly IAttendanceRegistration<Group> _attendanceRegistration;

    public GroupEditViewModel(IGroupRepository groupRepository, IPopupNotification popupService, IAttendanceRegistration<Group> attendanceRegistration)
    {
        _groupRepository = groupRepository ?? throw new ArgumentNullException(nameof(groupRepository));
        _attendanceRegistration = attendanceRegistration ?? throw new ArgumentNullException(nameof(attendanceRegistration));
        _popupService = popupService;
    }

    public override void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("group", out var groupObj) && groupObj is Group group)
        {
            Initialize(group);
        }
        else if (query.TryGetValue("groupId", out var groupIdObj) &&
                 groupIdObj is string groupIdStr &&
                 int.TryParse(groupIdStr, out var groupId))
        {
            // Load group by ID if only ID was passed
            _ = LoadGroupByIdAsync(groupId);
        }
    }

    private async Task LoadGroupByIdAsync(int groupId)
    {
        try
        {
            var group = await _groupRepository.GetGroupByIdAsync(groupId);
            if (group != null)
            {
                Initialize(group);
            }
            else
            {
                await Shell.Current.DisplayAlert("Error", "Group not found.", "OK");
                await Shell.Current.GoToAsync("//MainPage");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to load group: {ex.Message}", "OK");
            await Shell.Current.GoToAsync("//MainPage");
        }
    }

    public void Initialize(Group group)
    {
        Group = group;        
        UpdateDisplayProperties();
        UpdateCanConfirm();
    }

    partial void OnGroupChanged(Group? value)
    {
        UpdateDisplayProperties();
        UpdateCanConfirm();
    }

    private void UpdateDisplayProperties()
    {
        if (Group == null) return;        
        DisplayGsrName = Group.GsrName;
        DisplayGsrEmailPersonal = Group.GsrEmailPersonal;
        DisplayGsrPhone = Group.GsrPhone;
        DisplayGroupGenericEmail = Group.GroupGenericEmail;
        DisplayUsingGeneric = Group.UsingGeneric ?? false;
        Title = Group.Name;
    }

    partial void OnIsEditingChanged(bool value)
    {
        if (value)
        {
            StartEditing();
        }
        UpdateCanSave();
    }

    partial void OnEditGsrNameChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditGsrEmailPersonalChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditGsrPhoneChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditGroupGenericEmailChanged(string? value)
    {
        UpdateCanSave();
    }

    partial void OnEditUsingGenericChanged(bool value)
    {
        UpdateCanSave();
    }

    

    private void StartEditing()
    {
        if (Group == null) return;

        EditGsrName = DisplayGsrName;
        EditGsrEmailPersonal = DisplayGsrEmailPersonal;
        EditGsrPhone = DisplayGsrPhone;
        EditGroupGenericEmail = DisplayGroupGenericEmail;
        EditUsingGeneric = DisplayUsingGeneric;

        UpdateCanSave();
    }

    private void UpdateCanConfirm()
    {
        // GSR Name is required
        bool hasGsrName = !string.IsNullOrWhiteSpace(DisplayGsrName);

        // Either GSR Phone or GSR Email is required (but not necessarily both)
        bool hasGsrContact = !string.IsNullOrWhiteSpace(DisplayGsrPhone) ||
                            !string.IsNullOrWhiteSpace(DisplayGsrEmailPersonal);

        CanConfirm = hasGsrName && hasGsrContact;

        // Update validation message
        UpdateValidationMessage(hasGsrName, hasGsrContact);
    }

    private void UpdateValidationMessage(bool hasGsrName, bool hasGsrContact)
    {
        var errors = new List<string>();

        if (!hasGsrName)
            errors.Add("GSR Name is required");

        if (!hasGsrContact)
            errors.Add("Either GSR Email or GSR Phone is required");

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

        bool hasGsrName = !string.IsNullOrWhiteSpace(EditGsrName);
        bool hasGsrContact = !string.IsNullOrWhiteSpace(EditGsrPhone) ||
                            !string.IsNullOrWhiteSpace(EditGsrEmailPersonal);

        CanSave = hasGsrName && hasGsrContact;
    }

    [RelayCommand]
    private void StartEdit()
    {
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Group == null) return;

        await ShowSaveFeedback();

        // Validate required fields before saving
        bool hasGsrName = !string.IsNullOrWhiteSpace(EditGsrName);
        bool hasGsrContact = !string.IsNullOrWhiteSpace(EditGsrPhone) ||
                            !string.IsNullOrWhiteSpace(EditGsrEmailPersonal);

        if (!hasGsrName || !hasGsrContact)
        {
            var errors = new List<string>();

            if (!hasGsrName)
                errors.Add("GSR Name is required");

            if (!hasGsrContact)
                errors.Add("Either GSR Email or GSR Phone is required");

            string errorMessage = string.Join("\n", errors);
            await Shell.Current.DisplayAlert("Validation Error", $"Please fix the following errors:\n\n{errorMessage}", "OK");
            return;
        }

        try
        {
            // Copy edited values back to the group
            Group.GsrName = EditGsrName;
            Group.GsrEmailPersonal = EditGsrEmailPersonal;
            Group.GsrPhone = EditGsrPhone;
            Group.GroupGenericEmail = EditGroupGenericEmail;
            Group.UsingGeneric = EditUsingGeneric;

            // Save to repository
            var savedGroup = await _groupRepository.SaveGroupAsync(Group);

            // Update the Group property to trigger UI refresh
            Group = savedGroup;

            // Exit editing mode
            IsEditing = false;

            // Show success message
            //await Shell.Current.DisplayAlert("Success", "Group information has been saved.", "OK");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to save group: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task CancelAsync()
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
    private async Task ConfirmAsync()
    {
        if (!CanConfirm) return;

        try
        {
            // Mark as attended and save
            if (Group != null)
            {
                await _attendanceRegistration.Register(Group);

                string personalName = Group.GetGsrFirstName();

                // Show success popup
                await _popupService.ShowCountdownPopupAsync(
                    "Finished",
                    $"Thanks for registering {personalName}.",
                    async () => await Shell.Current.GoToAsync("//MainPage")
                );

            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Error", $"Failed to confirm group: {ex.Message}", "OK");
        }
    }

    private bool HasUnsavedChanges()
    {
        return EditGsrName != DisplayGsrName ||
               EditGsrEmailPersonal != DisplayGsrEmailPersonal ||
               EditGsrPhone != DisplayGsrPhone ||
               EditGroupGenericEmail != DisplayGroupGenericEmail ||
               EditUsingGeneric != DisplayUsingGeneric;
    }

    private async Task ShowSaveFeedback()
    {
        await Task.Delay(100);
        await Toast.Make("Updating Group...", ToastDuration.Short).Show();
    }

}