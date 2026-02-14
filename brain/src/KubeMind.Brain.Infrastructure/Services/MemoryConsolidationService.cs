using KubeMind.Brain.Application.Models;
using KubeMind.Brain.Infrastructure.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// A background service that consumes resolved incidents from the memory buffer,
/// generates embeddings, and persists them to the vector database.
/// Incorporates semantic deduplication to avoid redundant memories.
/// </summary>
public class MemoryConsolidationService(
    MemoryBufferChannel channel,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    VectorStore vectorStore,
    ILogger<MemoryConsolidationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Memory Consolidation Service started. Listening for resolved incidents...");

        // Obtain likely IVectorStoreRecordCollection<Guid, IncidentMemory>
        // We use 'var' because the return type interface seems elusive in this version.
        var collection = vectorStore.GetCollection<Guid, IncidentMemory>("k8s_incidents");

        try
        {
            await foreach (var resolution in channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(resolution.RawLog))
                    {
                        logger.LogWarning("Skipping empty log for Incident {IncidentId}", resolution.IncidentId);
                        continue;
                    }

                    // 1. Generate Embedding
                    var embeddingResult = await embeddingGenerator.GenerateAsync([resolution.RawLog], cancellationToken: stoppingToken);
                    var vector = embeddingResult[0].Vector;

                    // 2. Semantic Deduplication
                    var searchOptions = new VectorSearchOptions<IncidentMemory>
                    {
                        VectorProperty = x => x.Embedding
                    };

                    var searchResults = collection.SearchAsync(vector, 1, searchOptions, stoppingToken);
                    bool duplicateFound = false;

                    await foreach (var result in searchResults)
                    {
                        if (result.Score >= 0.95)
                        {
                            logger.LogInformation(
                                "Duplicate memory detected for Incident {IncidentId} (Score: {Score:F2}). Skipping insertion.", 
                                resolution.IncidentId, 
                                result.Score);
                            duplicateFound = true;
                            break;
                        }
                    }

                    if (duplicateFound) continue;

                    // 3. Persist New Memory
                    var memory = new IncidentMemory
                    {
                        Id = resolution.IncidentId,
                        ClusterId = resolution.ClusterId,
                        Namespace = resolution.Namespace,
                        RawLog = resolution.RawLog,
                        ResolutionAction = resolution.Resolution,
                        Embedding = vector,
                        CreatedAt = DateTimeOffset.UtcNow
                    };

                    await collection.UpsertAsync(memory, cancellationToken: stoppingToken);
                    
                    logger.LogInformation("Successfully consolidated memory for Incident {IncidentId}.", resolution.IncidentId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to consolidate memory for Incident {IncidentId}", resolution.IncidentId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Memory Consolidation Service stopping.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error in Memory Consolidation Service loop.");
        }
    }
}
