using Serilog;
using Serilog.Events;

namespace Palmap.Collector.Logging;

internal readonly record struct CollectorLogLevel(
    string Name,
    LogEventLevel MinimumLevel,
    bool Disabled)
{
    private const string SupportedValues =
        "Trace, Debug, Information, Warning, Error, Critical, or None";

    public static CollectorLogLevel Parse(string? configuredValue)
    {
        var value = string.IsNullOrWhiteSpace(configuredValue)
            ? nameof(Microsoft.Extensions.Logging.LogLevel.Information)
            : configuredValue.Trim();

        return value.ToUpperInvariant() switch
        {
            "TRACE" => new("Trace", LogEventLevel.Verbose, false),
            "DEBUG" => new("Debug", LogEventLevel.Debug, false),
            "INFORMATION" => new("Information", LogEventLevel.Information, false),
            "WARNING" => new("Warning", LogEventLevel.Warning, false),
            "ERROR" => new("Error", LogEventLevel.Error, false),
            "CRITICAL" => new("Critical", LogEventLevel.Fatal, false),
            "NONE" => new("None", LogEventLevel.Fatal, true),
            _ => throw new InvalidOperationException(
                $"LogLevel must be one of: {SupportedValues}.")
        };
    }

    public LoggerConfiguration Apply(LoggerConfiguration configuration)
    {
        configuration.MinimumLevel.Is(MinimumLevel);
        if (Disabled)
        {
            configuration.Filter.ByExcluding(static _ => true);
        }

        return configuration;
    }
}
