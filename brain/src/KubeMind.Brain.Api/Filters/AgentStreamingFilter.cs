using KubeMind.Brain.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;

namespace KubeMind.Brain.Api.Filters;

public class AgentStreamingFilter(IHubContext<AgentHub> hubContext, ILogger<AgentStreamingFilter> logger) : IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        var message = $"🏃 Executing function {context.Function.Name}...";
        await hubContext.Clients.All.SendAsync("ReceiveMessage", message);
        logger.LogInformation(message);

        await next(context);

        message = $"✅ Function {context.Function.Name} finished. Result: {context.Result}";
        await hubContext.Clients.All.SendAsync("ReceiveMessage", message);
        logger.LogInformation(message);
    }
}
