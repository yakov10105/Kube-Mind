using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using System;
using System.IO;
using System.Threading.Tasks;

#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates.

public class Program
{
    private const string MemoryCollectionName = "incidents-and-runbooks";
    
    public static async Task Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var redisConnectionString = configuration.GetSection("Redis")["ConnectionString"];
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            Console.WriteLine("Redis connection string not found in appsettings.json.");
            return;
        }

        var aiConfig = configuration.GetSection("AIService");
        var modelId = aiConfig["ModelId"];
        var apiKey = aiConfig["ApiKey"];

        if (string.IsNullOrWhiteSpace(modelId))
        {
            Console.WriteLine("AIService:ModelId is not configured in appsettings.");
            return;
        }
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("AIService:ApiKey is not configured in appsettings.");
            return;
        }
        
        // Build a Kernel with the embedding generation service
        var kernel = Kernel.CreateBuilder()
            .AddOpenAIEmbeddingGenerator(modelId, apiKey)
            .Build();
        var textEmbeddingGeneration = kernel.GetRequiredService<ITextEmbeddingGenerationService>();

        Console.WriteLine("Connecting to Redis...");
        var redis = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
        var db = redis.GetDatabase();
        var memoryStore = new RedisVectorStore(db);
        var collection = memoryStore.GetCollection<string, MemoryRecord>(MemoryCollectionName);

        Console.WriteLine("Successfully connected to Redis. Seeding data...");

        // Sample data to seed
        var memories = new[]
        {
            ("runbook-oom-1", "Runbook: For OOMKilled errors, the primary remediation is to increase the container's memory limit in the deployment manifest. A typical starting point is to double the existing limit and monitor."),
            ("incident-similar-1", "Past Incident: Pod 'auth-service-78c9f4f4f-4j4j4' in 'production' was OOMKilled. Resolution was to increase memory limit from 128Mi to 256Mi."),
            ("runbook-crashloop-1", "Runbook: CrashLoopBackOff errors are often caused by application-level exceptions. Examine the logs for stack traces, database connection errors, or missing configuration files."),
            ("incident-similar-2", "Past Incident: Pod 'cart-service-5f7b8f8f8-f8f8f' in 'staging' went into CrashLoopBackOff. The logs showed a 'System.NullReferenceException' at startup. A missing environment variable was the root cause.")
        };

        for(int i=0; i<memories.Length; i++)
        {
            var (id, text) = memories[i];
            var embedding = await textEmbeddingGeneration.GenerateEmbeddingAsync(text);
            var memoryRecord = new MemoryRecord(
                metadata: new MemoryRecordMetadata(
                    isReference: false,
                    id: id,
                    text: text,
                    description: null,
                    externalSourceName: "SeedScript",
                    additionalMetadata: null
                ),
                embedding: embedding,
                key: id
            );

            await collection.UpsertAsync(memoryRecord);
            Console.WriteLine($"  - Saved record '{id}'");
        }
        
        Console.WriteLine("\nSeeding script finished.");
    }
}
