#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.
#pragma warning disable SKEXP0052 // Type is for evaluation purposes only and is subject to change or removal in future updates.

using System.Text;
using KubeMind.Brain.Application.Services;
using Microsoft.Extensions.Logging;
using KubeMind.Proto;
using Microsoft.SemanticKernel.Memory;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// Implements the logic for enriching incident context using a vector store.
/// </summary>
public class EnrichmentService(ISemanticTextMemory semanticTextMemory, ILogger<EnrichmentService> logger) : IEnrichmentService
{
    private const string MemoryCollectionName = "incidents-and-runbooks";

    /// <inheritdoc/>
    public async Task<string> EnrichGoalWithCognitiveMemoryAsync(IncidentContext incident, string originalGoal, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting cognitive enrichment for Incident {IncidentId}...", incident.IncidentId);

        var searchQuery = $"{incident.FailureReason}: {incident.Logs}";

        var searchResults = semanticTextMemory.SearchAsync(
            MemoryCollectionName,
            searchQuery,
            limit: 3,
            minRelevanceScore: 0.75,
            cancellationToken: cancellationToken
        );
        
        var enrichedContextBuilder = new StringBuilder();
        var memoriesFound = 0;

        await foreach (var memory in searchResults)
        {
            if (memoriesFound == 0)
            {
                enrichedContextBuilder.AppendLine("\n\n--- Relevant Historical Context ---");
            }
            enrichedContextBuilder.AppendLine($"- Past Incident/Runbook: {memory.Metadata.Text}");
            memoriesFound++;
        }

        if (memoriesFound == 0)
        {
            logger.LogInformation("No relevant memories found for Incident {IncidentId}", incident.IncidentId);
            return originalGoal;
        }

        logger.LogInformation("Found {MemoriesCount} relevant memories for Incident {IncidentId}", memoriesFound, incident.IncidentId);
        
        enrichedContextBuilder.AppendLine("--- End of Context ---");

        return originalGoal + enrichedContextBuilder.ToString();
    }
}
