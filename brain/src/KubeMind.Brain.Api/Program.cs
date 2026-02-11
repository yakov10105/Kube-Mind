using KubeMind.Brain.Api.Extensions;
using KubeMind.Brain.Api.Telemetry;
using Azure.Identity;
using KubeMind.Brain.Api.Logging;
using KubeMind.Brain.Api.Filters;
using KubeMind.Brain.Api.Hubs;
using KubeMind.Brain.Api.Services;
using Microsoft.SemanticKernel;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using KubeMind.Brain.Infrastructure.Services;
using Microsoft.SemanticKernel.Connectors.Redis;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var serviceName = "KubeMind.Brain";

var keyVaultUri = builder.Configuration.GetValue<string>("KeyVaultUri");
if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(new CompactJsonFormatter()); 
});



builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithTracing(tracing => tracing
        .AddSource(KubeMindActivitySource.Name)
        .AddAspNetCoreInstrumentation()
        .AddConsoleExporter()); 

builder.Services.AddGrpc();
builder.Services.AddSignalR();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    redisConnectionString = builder.Configuration.GetSection("Redis")["ConnectionString"];
}
if (string.IsNullOrWhiteSpace(redisConnectionString))
{
    throw new InvalidOperationException("Redis connection string is not configured in ConnectionStrings or Redis section.");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));
builder.Services.AddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());
builder.Services.AddRedisVectorStore(redisConnectionString);

var kernelBuilder = builder.Services.AddKernel();
kernelBuilder.Services.AddSingleton<IFunctionInvocationFilter, AgentStreamingFilter>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.KubernetesPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.KubernetesPlugin>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.PolycheckPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.PolycheckPlugin>();

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

// Register the Notification Service
builder.Services.AddHttpClient<KubeMind.Brain.Application.Services.INotificationService, SlackNotificationService>();

// Register the Deduplication Service
builder.Services.AddSingleton<KubeMind.Brain.Application.Services.IIncidentDeduplicationService, RedisIncidentDeduplicationService>();

builder.Services.AddAiService(builder.Configuration);

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(50051, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });
});

var app = builder.Build();

app.UseStaticFiles();

app.MapGrpcService<IncidentService>();
app.MapHub<AgentHub>("/agenthub");

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


app.MapPost("/test-kernel", async (Kernel kernel) =>
{
    var testIncidentDescription = "Pod 'my-app-xyz' in namespace 'default' is in CrashLoopBackOff. Relevant logs: 'Error connecting to database', 'Connection refused'.";
    var result = await kernel.InvokePromptAsync(testIncidentDescription);
    return Results.Ok(result.GetValue<string>());
});

app.Run();
