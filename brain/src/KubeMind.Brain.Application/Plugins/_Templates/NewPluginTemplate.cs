using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace KubeMind.Brain.Application.Plugins._Templates;

/// <summary>
/// TEMPLATE: A new Semantic Kernel plugin for Kube-Mind Brain.
/// </summary>
/// <remarks>
/// Follow this template to create new plugins. Remember to update the namespace
/// and remove the `_Templates` suffix from the directory and class name.
/// </remarks>
public class NewPluginTemplate(ILogger<NewPluginTemplate> logger)
{
    /// <summary>
    /// TEMPLATE: A sample Kernel function within the plugin.
    /// </summary>
    /// <param name="input">A descriptive input parameter.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A descriptive output string.</returns>
    [KernelFunction]
    [Description("A short, descriptive summary of what this function does.")]
    public async Task<string> SampleFunction(
        [Description("A descriptive name for this input parameter.")] string input,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("SampleFunction called with input: {Input}", input);

        // TODO: Implement your plugin's business logic here.
        // Ensure all I/O operations are asynchronous.
        await Task.Delay(100, cancellationToken); // Simulate async work

        return $"Processed input: {input} successfully!";
    }
}
