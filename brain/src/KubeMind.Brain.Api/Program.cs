using Azure.Identity;
using Google.Apis.Auth.OAuth2;
using KubeMind.Brain.Api.Extensions;
using KubeMind.Brain.Api.Filters;
using KubeMind.Brain.Api.Hubs;
using KubeMind.Brain.Api.Services;
using KubeMind.Brain.Api.Telemetry;
using KubeMind.Brain.Infrastructure.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", @"c:\Users\Me\Desktop\Kube-Mind\docs\kube-mind-c205678d57e9.json");

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(); 

builder.Services.AddGrpc();
builder.Services.AddSignalR();

var kernelBuilder = builder.Services.AddKernel();
kernelBuilder.Services.AddSingleton<IFunctionInvocationFilter, AgentStreamingFilter>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.K8sDiagnosticsPlugin>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.KubernetesPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.KubernetesPlugin>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.PolycheckPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.PolycheckPlugin>();


builder.Services.AddVertexAIEmbeddingGenerator(
    modelId: "text-embedding-004",
    location: "us-central1",
    projectId: builder.Configuration["GCP:ProjectId"] ?? throw new InvalidOperationException("GCP:ProjectId is not configured"),
    bearerTokenProvider: async () =>
    {
        var credential = await GoogleCredential.GetApplicationDefaultAsync();
        if (credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        }
        var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync();
        return token;
    }

);



builder.Services.AddQdrantVectorStore("localhost", 6334, https: false, apiKey: null, options: null);

builder.Services.AddScoped<KubeMind.Brain.Application.Services.IEnrichmentService, EnrichmentService>();

builder.Services.AddHostedService<VectorDbInitializer>();

builder.Services.AddSingleton<MemoryBufferChannel>();
builder.Services.AddSingleton<KubeMind.Brain.Application.Services.IMemoryBuffer>(sp => sp.GetRequiredService<MemoryBufferChannel>());
builder.Services.AddHostedService<MemoryConsolidationService>();

builder.Services.AddAiService(builder.Configuration);

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
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

var githubToken = builder.Configuration.GetSection("GitHub")["Token"];
if (string.IsNullOrWhiteSpace(githubToken))
{
    throw new InvalidOperationException("GitHub:Token is not configured in appsettings.");
}

builder.Services.AddSingleton(sp => new Octokit.GitHubClient(new Octokit.ProductHeaderValue("KubeMind-Brain"))
{
    Credentials = new Octokit.Credentials(githubToken)
});
builder.Services.AddSingleton<KubeMind.Brain.Application.Services.IGitHubService, GitHubService>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.GitOpsPlugin>();
kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.GitOpsPlugin>();



builder.Services.AddHttpClient<KubeMind.Brain.Application.Services.INotificationService, SlackNotificationService>();

builder.Services.AddSingleton<KubeMind.Brain.Application.Services.IIncidentDeduplicationService, RedisIncidentDeduplicationService>();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenAnyIP(50051, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
    });

    serverOptions.ListenAnyIP(5081, listenOptions =>
    {
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });

    serverOptions.ListenAnyIP(7067, listenOptions =>
    {
        listenOptions.UseHttps();
        listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2;
    });
});

var app = builder.Build();

app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

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
