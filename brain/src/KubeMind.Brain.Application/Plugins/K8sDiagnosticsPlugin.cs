using System.ComponentModel;
using System.Text.Json;
using KubeMind.Brain.Application.Models;
using KubeMind.Proto;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Logging;

namespace KubeMind.Brain.Application.Plugins;

/// <summary>
/// A Semantic Kernel plugin for diagnosing Kubernetes-related incidents.
/// </summary>
public class K8sDiagnosticsPlugin
{
    private readonly ILogger<K8sDiagnosticsPlugin> _logger;

    public K8sDiagnosticsPlugin(ILogger<K8sDiagnosticsPlugin> logger)
    {
        _logger = logger;
    }

    [KernelFunction]
    [Description("Analyzes Kubernetes pod logs and manifests to diagnose the root cause of a failure.")]
    public async Task<string> AnalyzeIncident(
        [Description("The full incident context, serialized as a JSON string.")] string incidentContextJson)
    {
        _logger.LogInformation("Starting incident analysis...");
        
        var incident = JsonSerializer.Deserialize<IncidentContext>(incidentContextJson);
        if (incident == null)
        {
            _logger.LogError("Failed to deserialize IncidentContext JSON.");
            return "{}";
        }
        
        // This is where the prompt engineering happens.
        // We create a detailed prompt that guides the LLM to act as an SRE.
        var prompt = $$$"""
        You are an expert Kubernetes Site Reliability Engineer (SRE).
        Your task is to analyze the following incident data and provide a structured JSON diagnosis.

        **Incident Data:**
        - **Incident ID:** {incident.IncidentId}
        - **Pod:** {incident.PodNamespace}/{incident.PodName}
        - **Failure Reason:** {incident.FailureReason}
        - **Timestamp:** {incident.Timestamp.ToDateTime().ToString("o")}

        **Pod Manifest:**
        ```json
        {incident.PodManifestJson}
        ```

        **Deployment Manifest:**
        ```json
        {incident.DeploymentManifestJson}
        ```

        **Recent Pod Logs:**
        ```
        {incident.Logs}
        ```

        **Analysis Instructions:**
        1.  **Examine the `Failure Reason`:** Is it `OOMKilled`, `CrashLoopBackOff`, `ImagePullBackOff`, or something else? This is your primary clue.
        2.  **Correlate with Logs:** Scan the logs for keywords related to the failure reason. For `OOMKilled`, look for memory warnings. For `CrashLoopBackOff`, look for stack traces, exceptions, or fatal errors.
        3.  **Check Manifests:** Review the pod and deployment manifests for potential misconfigurations (e.g., incorrect image tags, low memory limits, missing config maps).
        4.  **Formulate Diagnosis:** Based on your analysis, provide a concise root cause, a confidence level, a specific recommended action, and the single most important log line or manifest snippet as supporting evidence.

        **Output Format:**
        Provide your response ONLY as a single, minified JSON object that conforms to the `IncidentDiagnosis` C# record schema. Do NOT include any markdown formatting like ```json.

        Example output for an OOMKilled incident:
        {{"rootCause":"The container was terminated because it exceeded its memory limit of 64Mi.","confidence":"High","recommendedAction":"Increase the memory limit for the container in the deployment manifest to 128Mi.","supportingEvidence":"(Final log line showing memory usage)"}}
        """;
        
        // In a real implementation, we would invoke the kernel here:
        // var result = await kernel.InvokePromptAsync(prompt);
        // For now, we return a placeholder to satisfy the interface.
        
        _logger.LogInformation("Analysis prompt generated. (Kernel invocation is mocked for now).");

        var placeholderDiagnosis = new IncidentDiagnosis(
            RootCause: "Placeholder: Analysis not implemented.",
            Confidence: "N/A",
            RecommendedAction: "N/A",
            SupportingEvidence: "N/A"
        );
        
        return JsonSerializer.Serialize(placeholderDiagnosis);
    }
}
