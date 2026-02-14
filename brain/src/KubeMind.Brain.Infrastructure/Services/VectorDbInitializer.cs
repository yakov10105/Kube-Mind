using KubeMind.Brain.Infrastructure.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// A background service responsible for ensuring the vector database is initialized on application startup.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="VectorDbInitializer"/> class.
/// </remarks>
/// <param name="vectorStore">The vector store instance.</param>
/// <param name="logger">The logger instance.</param>
public class VectorDbInitializer(VectorStore vectorStore, ILogger<VectorDbInitializer> logger) : IHostedService
{

    /// <summary>
    /// Checks for the existence of the required vector collection and creates it if it does not exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var collection = vectorStore.GetCollection<Guid, IncidentMemory>("k8s_incidents");
            await collection.EnsureCollectionExistsAsync(cancellationToken);
            logger.LogInformation("Vector database collection 'k8s_incidents' initialized.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize vector database collection.");
        }
    }

    /// <summary>
    /// Performs any necessary cleanup when the service stops.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
