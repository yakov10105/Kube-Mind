using System.Diagnostics;

namespace KubeMind.Brain.Api.Telemetry;

/// <summary>
/// Provides a single, static source for creating telemetry activities (spans).
/// </summary>
public static class KubeMindActivitySource
{
    /// <summary>
    /// The name of the activity source, used for identifying the application in telemetry data.
    /// </summary>
    public const string Name = "KubeMind.Brain";

    /// <summary>
    /// A shared ActivitySource instance for creating and starting activities.
    /// </summary>
    public static readonly ActivitySource Source = new(Name);
}
