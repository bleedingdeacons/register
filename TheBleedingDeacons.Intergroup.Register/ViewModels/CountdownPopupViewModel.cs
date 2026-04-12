using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System.Timers;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    public partial class CountdownPopupViewModel : ObservableObject, IDisposable
    {
        private static readonly ILogger Logger = AppLogger.ForContext<CountdownPopupViewModel>();

        private readonly System.Timers.Timer _timer;
        private readonly Popup _popup;
        private readonly Func<Task> _navigateAction;

        [ObservableProperty]
        private string _message = string.Empty;

        [ObservableProperty]
        private int _countdownSeconds = 3;

        [ObservableProperty]
        private string _nextButtonText = "Next Fellow 👆";

        [ObservableProperty]
        private string _title = string.Empty;

        public CountdownPopupViewModel(Popup popup, string title, string message, Func<Task> navigateAction)
        {
            _popup = popup;
            _title = title;
            _message = message;
            _navigateAction = navigateAction;

            _timer = new System.Timers.Timer(1000); // 1 second interval
            _timer.Elapsed += OnTimerElapsed;
            _timer.Start();
            
        }

        private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                CountdownSeconds--;                

                if (CountdownSeconds <= 0)
                {
                    _timer.Stop();
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await NavigateAndClose();
                    });
                }
            });
        }
        

        [RelayCommand]
        private async Task NavigateAndClose()
        {
            _timer?.Stop();
            await _popup?.CloseAsync();
            await _navigateAction.Invoke();
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
        }
    }
}