#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

using System.Text;
using KubeMind.Brain.Application.Services;
using Microsoft.Extensions.Logging;
using KubeMind.Proto;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Embeddings; 

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// Implements the logic for enriching incident context using a vector store.
/// </summary>
public class EnrichmentService : IEnrichmentService
{
    private readonly IMemoryStore _memoryStore;
    private readonly ITextEmbeddingGenerationService _textEmbeddingGeneration;
    private readonly ILogger<EnrichmentService> _logger;
    private const string MemoryCollectionName = "incidents-and-runbooks";

    public EnrichmentService(IMemoryStore memoryStore, ITextEmbeddingGenerationService textEmbeddingGeneration, ILogger<EnrichmentService> logger)
    {
        _memoryStore = memoryStore;
        _textEmbeddingGeneration = textEmbeddingGeneration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> EnrichGoalWithCognitiveMemoryAsync(IncidentContext incident, string originalGoal, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Starting cognitive enrichment for Incident {incident.IncidentId}...");

        var searchQuery = $"{incident.FailureReason}: {incident.Logs}";
        var queryEmbedding = await _textEmbeddingGeneration.GenerateEmbeddingAsync(searchQuery, cancellationToken);

        var searchResults = await _memoryStore.GetNearestMatchesAsync(
            collectionName: MemoryCollectionName,
            embedding: queryEmbedding,
            limit: 3,
            minRelevanceScore: 0.75
        ).ToListAsync(cancellationToken);
        
        if (searchResults.Count() == 0)
        {
            _logger.LogInformation($"No relevant memories found for Incident {incident.IncidentId}");
            return originalGoal;
        }

        _logger.LogInformation($"Found {searchResults.Count()} relevant memories for Incident {incident.IncidentId}");
        
        var enrichedContext = new StringBuilder();
        enrichedContext.AppendLine("\n\n--- Relevant Historical Context ---");
        foreach (var memory in searchResults)
        {
            enrichedContext.AppendLine($"- Past Incident/Runbook: {memory.Value.Metadata.Text}");
        }
        enrichedContext.AppendLine("--- End of Context ---");

        return originalGoal + enrichedContext.ToString();
    }
}
