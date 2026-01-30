using Microsoft.AspNetCore.SignalR;

namespace KubeMind.Brain.Api.Hubs;

/// <summary>
/// Represents a SignalR hub for streaming real-time updates about the AI agent's thought process.
/// </summary>
public class AgentHub : Hub
{
    /// <summary>
    /// Called when a new client connects to the hub.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // Here you could, for example, log the connection or send a welcome message.
        await Clients.Client(Context.ConnectionId).SendAsync("ReceiveMessage", "Agent is connected and awaiting incident data...");
        await base.OnConnectedAsync();
    }
}
