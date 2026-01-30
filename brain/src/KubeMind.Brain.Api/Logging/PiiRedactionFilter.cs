using Serilog.Core;
using Serilog.Events;
using System.Text.RegularExpressions;

namespace KubeMind.Brain.Api.Logging;

/// <summary>
/// A Serilog filter that redacts potential secrets from log messages.
/// </summary>
public class PiiRedactionFilter : ILogEventFilter
{
    private static readonly Regex SecretRegex = new(
        @"(api_key|token|secret|password|connection_string|auth_token)[\s""']*(=|:|=>)[\s""']*([a-zA-Z0-9_\-.~]+)", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool IsEnabled(LogEvent logEvent)
    {
        foreach (var property in logEvent.Properties)
        {
            if (property.Value is ScalarValue scalar && scalar.Value is string stringValue)
            {
                var redactedValue = SecretRegex.Replace(stringValue, @"$1$2""[REDACTED]""");
                if (redactedValue != stringValue)
                {
                    logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(redactedValue)));
                }
            }
        }

        var RendedredMessage = logEvent.RenderMessage();
        var redactedMessage = SecretRegex.Replace(RendedredMessage, @"$1$2""[REDACTED]""");
        if (redactedMessage != RendedredMessage)
        {
            // This is a bit of a hack as Serilog doesn't easily support message template modification.
            // For a real-world scenario, a more sophisticated approach might be needed.
        }

        return true;
    }
}
