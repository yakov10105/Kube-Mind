using System.Diagnostics;
using KubeMind.Brain.Api.Hubs;
using Grpc.Core;
using KubeMind.Proto;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;
using KubeMind.Brain.Application.Services;
using KubeMind.Brain.Api.Telemetry;
// Planner removed to avoid runtime planner/package mismatches. We invoke the Kernel directly.
using static KubeMind.Proto.IncidentService;

namespace KubeMind.Brain.Api.Services;

/// <summary>
/// Implements the gRPC service for receiving incident data from Observers.
/// </summary>
public class IncidentService(ILogger<IncidentService> logger, Kernel kernel, IEnrichmentService enrichmentService, IHubContext<AgentHub> hubContext, IIncidentDeduplicationService deduplicationService, IMemoryBuffer memoryBuffer) : IncidentServiceBase

{

    /// <summary>
    /// Handles a client-side stream of IncidentContext messages.
    /// </summary>
    public override async Task<StreamIncidentResponse> StreamIncident(
        IAsyncStreamReader<IncidentContext> requestStream,
        ServerCallContext context)
    {
        logger.LogInformation("Client stream started.");

        // Planner/Handlebars is disabled in this build to avoid Semantic Kernel
        // planner package mismatches. Planning is skipped and a no-op result is
        // returned so the service can continue processing incidents.

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
            
            var incidentJson = System.Text.Json.JsonSerializer.Serialize(incident);
            
            var originalGoal = $"""
            You are Kube-Mind, an autonomous Site Reliability Engineer (SRE).
            Your mission is to diagnose and fix the reported Kubernetes incident.

            Follow this STANDARD OPERATING PROCEDURE (SOP) strictly:

            1. **Gather Context**:
               - Call `KubernetesPlugin.GetPodStatus` to get the current state of the pod.

            2. **Diagnose Root Cause**:
               - Call `K8sDiagnosticsPlugin.AnalyzeIncident` with the incident context JSON provided below.
               - Analyze the findings.

            3. **Formulate Fix**:
               - Based on the diagnosis, determine the necessary fix (e.g., update deployment, change config).
               - Draft the specific code/configuration changes required.

            4. **Safety Validation (CRITICAL)**:
               - You MUST validate your proposed fix before applying it.
               - Call `PolycheckPlugin.IsCodeChangeSafe` with your proposed code changes.
               - If the result is "NO", STOP immediately and report the safety violation. DO NOT proceed to Step 5.

            5. **Apply Fix**:
               - IF AND ONLY IF the safety check was "YES":
               - Call `GitOpsPlugin.CreateFixPullRequest` to submit the fix.
               - Use a clear and descriptive PR title and body.

            6. **Report**:
               - Summarize your actions and the outcome (PR link or safety failure reason).

            ---
            **INCIDENT CONTEXT (Pass this JSON string to AnalyzeIncident if needed or use for reasoning):**
            {incidentJson}
            ---
            """;

            var enrichedGoal = await enrichmentService.EnrichGoalWithCognitiveMemoryAsync(incident, originalGoal, context.CancellationToken);

            string resultString = string.Empty;

            try
            {
                // Execute the enriched goal with Auto-Invocation enabled.
                // This allows the Kernel to automatically select and call the plugins 
                // defined in the SOP (GetPodStatus -> Analyze -> Polycheck -> GitOps).
                
                // We use the generic PromptExecutionSettings with FunctionChoiceBehavior.Auto()
                // which is the modern, unified way to enable tool calling across connectors (OpenAI, Gemini, etc.)
                PromptExecutionSettings settings = new PromptExecutionSettings
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
                };

                var kernelResult = await kernel.InvokePromptAsync(enrichedGoal, new(settings));
                resultString = kernelResult.GetValue<string>() ?? kernelResult.ToString();
                await hubContext.Clients.All.SendAsync("ReceiveMessage", $"🤖 Kernel executed goal for Incident {incident.IncidentId}", context.CancellationToken);
                logger.LogInformation("Kernel executed goal for Incident {IncidentId}: {Result}", incident.IncidentId, resultString);


                var resolution = new KubeMind.Brain.Application.Models.IncidentResolution(
                    Guid.NewGuid(),
                    "default-cluster", // In a real scenario, this would come from IncidentContext metadata
                    incident.PodNamespace,
                    incident.Logs,
                    resultString
                );

                await memoryBuffer.WriteAsync(resolution, context.CancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Kernel execution failed for Incident {IncidentId}. Falling back to no-op.", incident.IncidentId);
                resultString = $"Kernel execution failed: {ex.GetType().Name}: {ex.Message}";
                await hubContext.Clients.All.SendAsync("ReceiveMessage", $"⚠️ Kernel execution failed for Incident {incident.IncidentId}: {ex.Message}", context.CancellationToken);
            }

            stopwatch.Stop();
            logger.LogInformation("Incident {IncidentId} processed in {ElapsedMilliseconds}ms. Final result: {Result}", incident.IncidentId, stopwatch.ElapsedMilliseconds, resultString);
            await hubContext.Clients.All.SendAsync("ReceiveMessage", $"🏁 Incident {incident.IncidentId} processing finished in {stopwatch.ElapsedMilliseconds}ms.", context.CancellationToken);
        }

        logger.LogInformation("Client stream finished.");

        return new StreamIncidentResponse
        {
            Status = "Incidents received and processed."
        };
    }
}
