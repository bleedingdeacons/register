using CommunityToolkit.Maui.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheBleedingDeacons.Intergroup.Register.Services.Interfaces;
using TheBleedingDeacons.Intergroup.Register.Views;
using CommunityToolkit.Maui.Extensions;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.Services
{
    
    public class PopupNotificationService : IPopupNotification
    {
        private static readonly ILogger Logger = AppLogger.ForContext<PopupNotificationService>();

        public async Task ShowCountdownPopupAsync(string title, string message, Func<Task> navigationAction)
        {
            var popup = new CountdownPopup(title, message, navigationAction);

            if (Application.Current?.MainPage is Page currentPage)
            {
                await currentPage.ShowPopupAsync(popup);
            }
        }
    }
}
