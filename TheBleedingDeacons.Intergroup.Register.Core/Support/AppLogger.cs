using Serilog;
using TheBleedingDeacons.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TheBleedingDeacons.Intergroup.Register.Support
{
    public static class AppLogger
    {
        public static void Debug(string message, params object[] args) => Log.Debug(message, args);
        public static void Info(string message, params object[] args) => Log.Information(message, args);
        public static void Warning(string message, params object[] args) => Log.Warning(message, args);
        public static void Error(string message, params object[] args) => Log.Error(message, args);
        public static void Error(Exception ex, string message, params object[] args) => Log.Error(ex, message, args);

        // Through CurrentLogger, not Log.ForContext. Every caller keeps the
        // result in a static field, and Log.ForContext binds to the pipeline of
        // the moment: Inventory rebuilds and disposes the pipeline whenever the
        // Better Stack settings change and after every error, so those fields
        // would go on writing into a disposed one — silently, for everything
        // logged through them, from the first rebuild on. CurrentLogger writes
        // to whatever Log.Logger is at the time of each call.
        public static ILogger ForContext<T>() => CurrentLogger.Instance.ForContext<T>();
        public static ILogger ForContext(string sourceContext) => CurrentLogger.Instance.ForContext("SourceContext", sourceContext);
    }
}
