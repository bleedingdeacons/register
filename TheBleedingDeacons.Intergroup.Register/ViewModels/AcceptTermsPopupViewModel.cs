using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.AcceptTermsPopup"/>. Mirrors
    /// <see cref="CountdownPopupViewModel"/> in shape (popup reference + bound
    /// Title/Message), but instead of an auto-advancing timer it exposes
    /// Accept/Decline commands. The user's choice is forwarded through the
    /// supplied <see cref="TaskCompletionSource{Boolean}"/> so the calling
    /// service can <c>await</c> a <see cref="Task{Boolean}"/> result.
    /// </summary>
    public partial class AcceptTermsPopupViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AcceptTermsPopupViewModel>();

        private readonly Popup _popup;
        private readonly TaskCompletionSource<bool> _resultTcs;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _message = string.Empty;

        public AcceptTermsPopupViewModel(
            Popup popup,
            string title,
            string message,
            TaskCompletionSource<bool> resultTcs)
        {
            _popup = popup;
            _resultTcs = resultTcs;
            _title = title;
            _message = message;
        }

        [RelayCommand]
        private async Task Accept()
        {
            Logger.Information("Compliance popup: user accepted");
            _resultTcs.TrySetResult(true);
            await _popup.CloseAsync();
        }

        [RelayCommand]
        private async Task Decline()
        {
            Logger.Information("Compliance popup: user declined");
            _resultTcs.TrySetResult(false);
            await _popup.CloseAsync();
        }
    }
}
