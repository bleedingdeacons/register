using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Services.Interfaces
{
    public interface IPopupNotification
    {
        Task ShowCountdownPopupAsync(string title, string message, Func<Task> navigationAction);
    }
}
