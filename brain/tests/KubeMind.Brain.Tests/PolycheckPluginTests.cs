using KubeMind.Brain.Application.Plugins;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Runtime.CompilerServices; // Required for [EnumeratorCancellation]

namespace KubeMind.Brain.Tests;

public class PolycheckPluginTests
{
    private class DummyChatCompletionService : IChatCompletionService
    {
        private readonly string _llmResponse;

        public DummyChatCompletionService(string llmResponse)
        {
            _llmResponse = llmResponse;
        }

        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ChatMessageContent>>(
                new List<ChatMessageContent> { new ChatMessageContent(AuthorRole.Assistant, _llmResponse) });
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, _llmResponse);
        }
    }

    [Theory]
    [InlineData("YES", "YES")]
    [InlineData("NO", "NO")]
    [InlineData("  yes  ", "YES")]
    [InlineData("Something else", "NO")]
    public async Task IsCodeChangeSafe_ReturnsCorrectSafetyAssessment(string llmResponse, string expectedResult)
    {
        // Arrange
        var dummyChatCompletionService = new DummyChatCompletionService(llmResponse);
        
        var plugin = new PolycheckPlugin(dummyChatCompletionService);
        var codeChange = "some code";

        // Act
        var result = await plugin.IsCodeChangeSafe(codeChange);

        // Assert
        Assert.Equal(expectedResult, result);
    }
}