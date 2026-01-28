using KubeMind.Brain.Api.Services;
using Microsoft.SemanticKernel;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using KubeMind.Brain.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var serviceName = "KubeMind.Brain";

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()); 
});



builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter()); 

builder.Services.AddGrpc();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    redisConnectionString = builder.Configuration.GetSection("Redis")["ConnectionString"];
}
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("Redis connection string is not configured in ConnectionStrings or Redis section.");
}
builder.Services.AddRedisVectorStore(redisConnectionString);

var kernelBuilder = builder.Services.AddKernel();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.KubernetesPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.KubernetesPlugin>();

var githubToken = builder.Configuration.GetSection("GitHub")["Token"];
if (string.IsNullOrWhiteSpace(githubToken))
{
    throw new InvalidOperationException("GitHub:Token is not configured in appsettings.");
}

builder.Services.AddSingleton<Octokit.GitHubClient>(sp => new Octokit.GitHubClient(new Octokit.ProductHeaderValue("KubeMind-Brain"))
{
    Credentials = new Octokit.Credentials(githubToken)
});
builder.Services.AddSingleton<KubeMind.Brain.Application.Services.IGitHubService, GitHubService>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.GitOpsPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.GitOpsPlugin>();

// Register the Enrichment Service
builder.Services.AddSingleton<KubeMind.Brain.Application.Services.IEnrichmentService, EnrichmentService>();

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

app.MapGrpcService<IncidentService>();

app.MapGet("/", () => "KubeMind.Brain is online.");

app.MapGet("/healthz", async (Kernel kernel, ILogger<Program> logger) =>
{
    try
    {
        var result = await kernel.InvokePromptAsync("Respond with a single word: OK");
        
        if (result.GetValue<string>()?.Trim() == "OK")
        {
            return Results.Ok("LLM connection is healthy.");
        }

        logger.LogWarning("Health check failed: LLM responded unexpectedly: {Response}", result.GetValue<string>());
        return Results.StatusCode(503); 
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Health check failed with an exception.");
        return Results.StatusCode(503); 
    }
});


app.Run();
