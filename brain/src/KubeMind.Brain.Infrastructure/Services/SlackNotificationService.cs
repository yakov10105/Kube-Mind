using System.Text;
using System.Text.Json;
using KubeMind.Brain.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// Implements a notification service for sending messages to a Slack webhook.
/// </summary>
public class SlackNotificationService : INotificationService
{
    private readonly HttpClient _httpClient;
    private readonly string? _webhookUrl;
    private readonly ILogger<SlackNotificationService> _logger;

    public SlackNotificationService(HttpClient httpClient, IConfiguration configuration, ILogger<SlackNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _webhookUrl = configuration.GetSection("Slack")["WebhookUrl"];

        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _logger.LogWarning("Slack:WebhookUrl is not configured. Slack notifications will be disabled.");
        }
    }

    /// <inheritdoc/>
    public async Task SendNotificationAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _logger.LogDebug("Slack notifications are disabled. Message not sent: {Message}", message);
            return;
        }

        try
        {
            var payload = new { text = message };
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_webhookUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent notification to Slack.");
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to send Slack notification. Status: {StatusCode}, Response: {ResponseBody}", response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while sending a Slack notification.");
        }
    }
}
