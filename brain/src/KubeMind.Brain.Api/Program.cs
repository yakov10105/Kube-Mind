using KubeMind.Brain.Api.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Redis;
using Microsoft.SemanticKernel.Memory;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var serviceName = "KubeMind.Brain";

// --- Logging Configuration ---
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()); // Structured JSON logging
});


// --- Service Registration ---

// Add OpenTelemetry for distributed tracing.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter()); // Exports traces to the console

// Add gRPC services to the container.
builder.Services.AddGrpc();

// Configure Redis memory store
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    // Fallback to the Redis section for non-GetConnectionString methods
    redisConnectionString = builder.Configuration.GetSection("Redis")["ConnectionString"];
}
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("Redis connection string is not configured in ConnectionStrings or Redis section.");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<IMemoryStore>(sp =>
{
    var database = sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
    return new RedisMemoryStore(database, vectorSize: 1536); // Adjust vectorSize as needed for your embedding model
});


// Add Semantic Kernel to the container.
var kernelBuilder = builder.Services.AddKernel();

// Add the diagnostic plugin to the DI container and the kernel
builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();

// Configure the Kernel with an AI connector based on appsettings.
var aiConfig = builder.Configuration.GetSection("AIService");
var serviceType = aiConfig["Type"];

if (string.IsNullOrWhiteSpace(serviceType))
{
    throw new InvalidOperationException("AIService:Type is not configured in appsettings.");
}

var modelId = aiConfig["ModelId"];
var apiKey = aiConfig["ApiKey"];

if (string.IsNullOrWhiteSpace(modelId)) throw new InvalidOperationException("AIService:ModelId is not configured.");
if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("AIService:ApiKey is not configured.");

switch (serviceType)
{
    case "OpenAI":
        var orgId = aiConfig["OrgId"];
        builder.Services.AddOpenAIChatCompletion(modelId, apiKey, orgId);
        break;

    case "AzureOpenAI":
        var endpoint = aiConfig["Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException("AIService:Endpoint is not configured for AzureOpenAI.");
        builder.Services.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
        break;
    
    default:
        throw new InvalidOperationException($"Unsupported AIService:Type '{serviceType}'.");
}


var app = builder.Build();

// --- HTTP Request Pipeline Configuration ---

// The gRPC service is the primary endpoint.
app.MapGrpcService<IncidentService>();

// A simple 'I am online' endpoint.
app.MapGet("/", () => "KubeMind.Brain is online.");

// A health check endpoint that verifies connectivity to the configured LLM.
app.MapGet("/healthz", async (Kernel kernel, ILogger<Program> logger) =>
{
    try
    {
        // A simple, low-token prompt to verify the connection.
        var result = await kernel.InvokePromptAsync("Respond with a single word: OK");
        
        if (result.GetValue<string>()?.Trim() == "OK")
        {
            return Results.Ok("LLM connection is healthy.");
        }

        logger.LogWarning("Health check failed: LLM responded unexpectedly: {Response}", result.GetValue<string>());
        return Results.StatusCode(503); // Service Unavailable
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed with an exception.");
        return Results.StatusCode(503); // Service Unavailable
    }
});


app.Run();
