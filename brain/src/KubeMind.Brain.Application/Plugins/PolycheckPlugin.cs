using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace KubeMind.Brain.Application.Plugins;

/// <summary>
/// A Semantic Kernel plugin that acts as a safety guardrail ("Polycheck").
/// </summary>
public class PolycheckPlugin(IChatCompletionService chatCompletionService)
{
    private readonly IChatCompletionService _chatCompletionService = chatCompletionService;

    [KernelFunction]
    [Description("Validates a proposed code change for safety by asking a secondary LLM.")]
    public async Task<string> IsCodeChangeSafe(
        [Description("The proposed code or configuration change to validate.")] string codeChange)
    {
        var prompt = $"""
        You are a senior DevOps engineer responsible for infrastructure stability.
        Your sole task is to determine if the following configuration change is safe.
        A "safe" change only modifies values (e.g., changing memory limits, updating image tags, modifying environment variable values).
        An "unsafe" change is anything that alters the structure, deletes resources, or changes fundamental behavior (e.g., deleting a deployment, changing a port, removing a volume).

        Does this code look safe? Does it contain any destructive actions? Answer ONLY with "YES" or "NO".

        ---
        {codeChange}
        ---
        """;

        var result = await _chatCompletionService.GetChatMessageContentAsync(prompt);
        
        var response = result.Content?.Trim().ToUpperInvariant();

        return response == "YES" ? "YES" : "NO";
    }
}
