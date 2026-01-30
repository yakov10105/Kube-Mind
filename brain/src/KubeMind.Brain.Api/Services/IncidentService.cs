using System.Diagnostics;
using KubeMind.Brain.Api.Hubs;
using Grpc.Core;
using KubeMind.Proto;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;
using System.Text.Json;
using KubeMind.Brain.Application.Plugins;
using KubeMind.Brain.Application.Services;
using KubeMind.Brain.Api.Telemetry;
using Microsoft.SemanticKernel.Planning.Handlebars;
using static KubeMind.Proto.IncidentService;

namespace KubeMind.Brain.Api.Services;

/// <summary>
/// Implements the gRPC service for receiving incident data from Observers.
/// </summary>
public class IncidentService(ILogger<IncidentService> logger, Kernel kernel, IEnrichmentService enrichmentService, IHubContext<AgentHub> hubContext, IIncidentDeduplicationService deduplicationService) : IncidentServiceBase
{

    /// <summary>
    /// Handles a client-side stream of IncidentContext messages.
    /// </summary>
    public override async Task<StreamIncidentResponse> StreamIncident(
        IAsyncStreamReader<IncidentContext> requestStream,
        ServerCallContext context)
    {
        logger.LogInformation("Client stream started.");

        var planner = new HandlebarsPlanner(new HandlebarsPlannerOptions() { AllowLoops = true });

        await foreach (var incident in requestStream.ReadAllAsync(context.CancellationToken))
        {
            using var activity = KubeMindActivitySource.Source.StartActivity("ProcessIncident", ActivityKind.Server);
            activity?.AddTag("kubemind.incident.id", incident.IncidentId);
            activity?.AddTag("kubemind.pod.name", incident.PodName);
            activity?.AddTag("kubemind.pod.namespace", incident.PodNamespace);

            if (await deduplicationService.IsDuplicateAsync(incident.IncidentId, context.CancellationToken))
            {
                activity?.AddTag("kubemind.incident.duplicate", true);
                continue;
            }

            logger.LogInformation(
                "Received Incident '{IncidentId}' for Pod '{PodName}' in namespace '{Namespace}'. Reason: {Reason}",
                incident.IncidentId,
                incident.PodName,
                incident.PodNamespace,
                incident.FailureReason);

            var stopwatch = Stopwatch.StartNew();
            
            var originalGoal = """
            Analyze the provided Kubernetes incident and get the pod's current status.
            Then, provide a structured JSON diagnosis using the K8sDiagnosticsPlugin.AnalyzeIncident function.
            Based on the diagnosis, propose a fix.
            Before creating a pull request, you MUST validate the proposed file content using the PolycheckPlugin.IsCodeChangeSafe function.
            If and only if the safety check returns "YES", create a pull request using the GitOpsPlugin.CreateFixPullRequest function.
            If the safety check returns "NO", stop execution and report the failure.
            """;

            var enrichedGoal = await enrichmentService.EnrichGoalWithCognitiveMemoryAsync(incident, originalGoal, context.CancellationToken);

            var plan = await planner.CreatePlanAsync(kernel, enrichedGoal);

            await hubContext.Clients.All.SendAsync("ReceiveMessage", $"🤖 Plan created for Incident {incident.IncidentId}", context.CancellationToken);
            logger.LogInformation("Plan created for Incident {IncidentId}: {Plan}", incident.IncidentId, plan.ToString());

            var kernelArgs = new KernelArguments { ["incident"] = incident };
            
            var result = await plan.InvokeAsync(kernel, kernelArgs);

            stopwatch.Stop();
            logger.LogInformation("Incident {IncidentId} processed in {ElapsedMilliseconds}ms. Final result: {Result}", incident.IncidentId, stopwatch.ElapsedMilliseconds, result);
            await hubContext.Clients.All.SendAsync("ReceiveMessage", $"🏁 Incident {incident.IncidentId} processing finished in {stopwatch.ElapsedMilliseconds}ms.", context.CancellationToken);
        }

        logger.LogInformation("Client stream finished.");

        return new StreamIncidentResponse
        {
            Status = "Incidents received and processed."
        };
    }
}
