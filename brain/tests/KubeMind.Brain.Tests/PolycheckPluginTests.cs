using KubeMind.Brain.Application.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Xunit;

namespace KubeMind.Brain.Tests;

public class PolycheckPluginTests
{
    [Theory]
    [InlineData("YES", "YES")]
    [InlineData("NO", "NO")]
    [InlineData("  yes  ", "YES")]
    [InlineData("Something else", "NO")]
    public async Task IsCodeChangeSafe_ReturnsCorrectSafetyAssessment(string llmResponse, string expectedResult)
    {
        // Arrange
        var mockChatCompletionService = new Mock<IChatCompletionService>();
        var mockKernel = new Mock<Kernel>();
        
        mockChatCompletionService.Setup(c => c.GetChatMessageContentAsync(It.IsAny<string>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatMessageContent(AuthorRole.Assistant, llmResponse));
        
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(s => s.GetService(typeof(IChatCompletionService))).Returns(mockChatCompletionService.Object);
        mockKernel.Setup(k => k.Services).Returns(serviceProvider.Object);
        
        var plugin = new PolycheckPlugin(mockKernel.Object);
        var codeChange = "some code";

        // Act
        var result = await plugin.IsCodeChangeSafe(codeChange);

        // Assert
        Assert.Equal(expectedResult, result);
    }
}
