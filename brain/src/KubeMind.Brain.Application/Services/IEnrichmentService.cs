using KubeMind.Proto;

namespace KubeMind.Brain.Application.Services;

/// <summary>
/// Defines the contract for enriching an incident context with historical data.
/// </summary>
public interface IEnrichmentService
{
    /// <summary>
    /// Enriches a goal with context from similar past incidents or runbooks.
    /// </summary>
    /// <param name="incident">The incoming incident context.</param>
    /// <param name="originalGoal">The original high-level goal for the AI planner.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An enriched goal string containing relevant historical context.</returns>
    Task<string> EnrichGoalWithCognitiveMemoryAsync(IncidentContext incident, string originalGoal, CancellationToken cancellationToken = default);
}
