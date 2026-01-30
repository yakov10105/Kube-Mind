using System.Text.Json;
using KubeMind.Brain.Application.Plugins;
using KubeMind.Brain.Application.Services; // Added this
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Moq;
// Removed using Octokit; as it's no longer directly used in the test
using Xunit;

namespace KubeMind.Brain.Tests;

public class GitOpsPluginTests
{
    [Fact]
    public async Task CreateFixPullRequest_WithValidParameters_ReturnsPullRequestUrl()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<GitOpsPlugin>>();
        var mockGitHubService = new Mock<IGitHubService>();
        var mockNotificationService = new Mock<INotificationService>();

        var plugin = new GitOpsPlugin(mockLogger.Object, mockGitHubService.Object, mockNotificationService.Object);

        var repositoryOwner = "test-owner";
        var repositoryName = "test-repo";
        var baseBranch = "main";
        var newBranchName = "fix/test-incident";
        var commitMessage = "Fix: Test incident";
        var filePath = "config/values.yaml";
        var fileContent = "new-content: true";
        var pullRequestTitle = "Automated Fix for Test Incident";
        var pullRequestBody = "This is an automated PR.";
        var expectedPullRequestUrl = $"https://github.com/{repositoryOwner}/{repositoryName}/pull/123-mock";

        mockGitHubService.Setup(x => x.CreatePullRequestAsync(repositoryOwner, repositoryName, baseBranch, newBranchName, pullRequestTitle, pullRequestBody))
            .ReturnsAsync(expectedPullRequestUrl);

        // Act
        var result = await plugin.CreateFixPullRequest(
            repositoryOwner,
            repositoryName,
            baseBranch,
            newBranchName,
            commitMessage,
            filePath,
            fileContent,
            pullRequestTitle,
            pullRequestBody);

        // Assert
        Assert.Equal(expectedPullRequestUrl, result);
        mockGitHubService.Verify(x => x.CreatePullRequestAsync(repositoryOwner, repositoryName, baseBranch, newBranchName, pullRequestTitle, pullRequestBody), Times.Once);
        mockNotificationService.Verify(x => x.SendNotificationAsync(It.Is<string>(s => s.Contains(expectedPullRequestUrl)), default), Times.Once);
    }
}
