using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Support;
using TheBleedingDeacons.Unity.Data.Entities;
using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels;

public partial class PositionEditViewModel : BaseViewModel
{
    private static readonly ILogger Logger = AppLogger.ForContext<PositionEditViewModel>();

    private readonly IPositionRepository _positionRepository;

    [ObservableProperty]
    private Position? _position;

    [ObservableProperty]
    private bool _canConfirm;

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

    private readonly IPopupNotification _popupService;
    private readonly IAttendanceRegistration<Position> _attendanceRegistration;

    public PositionEditViewModel(
        IPositionRepository positionRepository,
        IPopupNotification popupService,
        IAttendanceRegistration<Position> attendanceRegistration)
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
            LoadPositionByIdAsync(positionId).SafeFireAndForget("LoadPositionById");
        }
    }

    private async Task LoadPositionByIdAsync(int positionId)
    {
        try
        {
            var position = await _positionRepository.GetByIdWithHoldersAsync(positionId);
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
        var holder = Position.Holders.FirstOrDefault();
        DisplayMemberAnonymousName = holder?.AnonymousName;
        DisplayMemberPersonalEmail = holder?.PersonalEmail;
        DisplayMemberMobile = holder?.MobileNumber;
        Title = Position.ShortDescription ?? "Position";
    }

    private void UpdateCanConfirm()
    {
        bool hasMemberName = !string.IsNullOrWhiteSpace(DisplayMemberAnonymousName);
        bool hasMemberContact = !string.IsNullOrWhiteSpace(DisplayMemberMobile) ||
                               !string.IsNullOrWhiteSpace(DisplayMemberPersonalEmail);

        CanConfirm = hasMemberName && hasMemberContact;

        var errors = new List<string>();
        if (!hasMemberName) errors.Add("Member Name is required");
        if (!hasMemberContact) errors.Add("Either Member Email or Member Mobile is required");

        HasValidationErrors = errors.Count > 0;
        ValidationMessage = errors.Count > 0 ? string.Join(", ", errors) : null;
    }

    [RelayCommand]
    private async Task Confirm()
    {
        if (!CanConfirm) return;

        try
        {
            if (Position != null)
            {
                await _attendanceRegistration.Register(Position);

                string memberName = Position.Holders.FirstOrDefault()?.AnonymousName ?? "Officer";

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
}
