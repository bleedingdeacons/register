using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Intergroup.Register.Support
{
    public class ExceptionEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent.Exception != null)
            {
                var exceptionDetails = new
                {
                    Type = logEvent.Exception.GetType().Name,
                    Message = logEvent.Exception.Message,
                    StackTrace = logEvent.Exception.StackTrace?.Split('\n').Take(5), // First 5 lines
                    InnerExceptionMessage = logEvent.Exception.InnerException?.Message
                };

                logEvent.AddPropertyIfAbsent(
                    propertyFactory.CreateProperty("ExceptionDetails", exceptionDetails, true));
            }
        }
    }
}
