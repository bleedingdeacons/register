using Serilog;
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

        public static ILogger ForContext<T>() => Log.ForContext<T>();
        public static ILogger ForContext(string sourceContext) => Log.ForContext("SourceContext", sourceContext);
    }
}
