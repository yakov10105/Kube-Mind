using System.Text.Json.Serialization;

namespace KubeMind.Brain.Application.Models;

/// <summary>
/// Represents the structured diagnosis of a Kubernetes incident,
/// as determined by the AI agent.
/// </summary>
/// <param name="RootCause">A concise summary of the most likely root cause.</param>
/// <param name="Confidence">The AI's confidence in its diagnosis (Low, Medium, High).</param>
/// <param name="RecommendedAction">A specific, actionable recommendation for remediation.</param>
/// <param name="SupportingEvidence">Key log lines or manifest snippets that support the diagnosis.</param>
public record IncidentDiagnosis(
    [property: JsonPropertyName("rootCause")] string RootCause,
    [property: JsonPropertyName("confidence")] string Confidence,
    [property: JsonPropertyName("recommendedAction")] string RecommendedAction,
    [property: JsonPropertyName("supportingEvidence")] string SupportingEvidence
);
