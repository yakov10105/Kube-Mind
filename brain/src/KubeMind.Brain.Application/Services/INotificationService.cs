namespace KubeMind.Brain.Application.Services;

/// <summary>
/// Defines a contract for sending notifications to external systems like Slack or Microsoft Teams.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends a notification message.
    /// </summary>
    /// <param name="message">The plain text message to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SendNotificationAsync(string message, CancellationToken cancellationToken = default);
}
