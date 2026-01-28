using Grpc.Core;
using KubeMind.Proto;
using Microsoft.SemanticKernel;
using System.Text.Json;
using KubeMind.Brain.Application.Plugins;
using static KubeMind.Proto.IncidentService;

namespace KubeMind.Brain.Api.Services;

/// <summary>
/// Implements the gRPC service for receiving incident data from Observers.
/// </summary>
public class IncidentService : IncidentServiceBase
{
    private readonly ILogger<IncidentService> _logger;
    private readonly Kernel _kernel;

    public IncidentService(ILogger<IncidentService> logger, Kernel kernel)
    {
        _logger = logger;
        _kernel = kernel;
    }

    /// <summary>
    /// Handles a client-side stream of IncidentContext messages.
    /// </summary>
    public override async Task<StreamIncidentResponse> StreamIncident(
        IAsyncStreamReader<IncidentContext> requestStream,
        ServerCallContext context)
    {
        _logger.LogInformation("Client stream started.");

        // The Kernel is already configured with our plugin, so we can invoke it.
        var diagnosticFunction = _kernel.Plugins.GetFunction(nameof(K8sDiagnosticsPlugin), "AnalyzeIncident");

        await foreach (var incident in requestStream.ReadAllAsync(context.CancellationToken))
        {
            _logger.LogInformation(
                "Received Incident '{IncidentId}' for Pod '{PodName}' in namespace '{Namespace}'. Reason: {Reason}",
                incident.IncidentId,
                incident.PodName,
                incident.PodNamespace,
                incident.FailureReason);

            // Trigger the Semantic Kernel cognitive loop.
            var incidentJson = JsonSerializer.Serialize(incident);
            var result = await _kernel.InvokeAsync(
                diagnosticFunction,
                new() { { "incidentContextJson", incidentJson } }
            );

            _logger.LogInformation("Diagnosis for Incident {IncidentId}: {Diagnosis}", incident.IncidentId, result.GetValue<string>());
        }

        _logger.LogInformation("Client stream finished.");

        return new StreamIncidentResponse
        {
            Status = "Incidents received and processed."
        };
    }
}
