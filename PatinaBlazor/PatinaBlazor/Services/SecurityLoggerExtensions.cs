using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace PatinaBlazor.Services
{
    // Tags a log call with EventCategory="Security" via Serilog's ambient LogContext (picked
    // up by Program.cs's .Enrich.FromLogContext()) rather than introducing a new log level -
    // Serilog's LogEventLevel is a fixed, non-extensible enum (Verbose/Debug/Information/
    // Warning/Error/Fatal), so a literal level between Information and Error isn't something
    // the library supports. Program.cs's Filter.ByExcluding checks this property to let
    // Security-tagged events through regardless of their own Information/Warning level, even
    // though the app otherwise only logs Error and above - use these for account/security
    // events (logins, registrations, password resets, email confirmations), not general
    // application logging.
    public static class SecurityLoggerExtensions
    {
        public static void LogSecurityInformation(this ILogger logger, string message, params object?[] args)
        {
            using (LogContext.PushProperty("EventCategory", "Security"))
            {
                logger.LogInformation(message, args);
            }
        }

        public static void LogSecurityWarning(this ILogger logger, string message, params object?[] args)
        {
            using (LogContext.PushProperty("EventCategory", "Security"))
            {
                logger.LogWarning(message, args);
            }
        }
    }
}
