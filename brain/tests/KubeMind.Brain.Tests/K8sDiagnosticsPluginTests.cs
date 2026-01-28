using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using KubeMind.Brain.Application.Models;
using KubeMind.Brain.Application.Plugins;
using KubeMind.Proto;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;

namespace KubeMind.Brain.Tests;

public class K8sDiagnosticsPluginTests
{
    [Fact]
    public async Task AnalyzeIncident_WithValidContext_ReturnsStructuredDiagnosis()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<K8sDiagnosticsPlugin>>();
        var plugin = new K8sDiagnosticsPlugin(mockLogger.Object);

        var incidentContext = new IncidentContext
        {
            IncidentId = "test-incident-123",
            PodName = "test-pod",
            PodNamespace = "default",
            FailureReason = "OOMKilled",
            Logs = "Error: Out of Memory",
            PodManifestJson = "{\"spec\":{\"containers\":[{\"name\":\"test\",\"resources\":{\"limits\":{\"memory\":\"64Mi\"}}}]}}",
            DeploymentManifestJson = "{\"spec\":{\"template\":{\"spec\":{\"containers\":[{\"name\":\"test\",\"resources\":{\"limits\":{\"memory\":\"64Mi\"}}}]}}}}",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        
        var incidentContextJson = JsonSerializer.Serialize(incidentContext);
        
        // Act
        var resultJson = await plugin.AnalyzeIncident(incidentContextJson);
        var result = JsonSerializer.Deserialize<IncidentDiagnosis>(resultJson);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Placeholder: Analysis not implemented.", result.RootCause);
    }
}
