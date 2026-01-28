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
        // Replaced GitHubClient mock with IGitHubService mock
        var mockGitHubService = new Mock<IGitHubService>();

        // Updated plugin instantiation to use IGitHubService mock
        var plugin = new GitOpsPlugin(mockLogger.Object, mockGitHubService.Object);

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

        // Set up IGitHubService mocks
        mockGitHubService.Setup(x => x.CreateBranchAsync(repositoryOwner, repositoryName, baseBranch, newBranchName))
            .ReturnsAsync("mock-new-branch-sha");

        mockGitHubService.Setup(x => x.CreateOrUpdateFileAsync(repositoryOwner, repositoryName, newBranchName, filePath, fileContent, commitMessage))
            .ReturnsAsync("mock-file-sha");

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
        mockGitHubService.Verify(x => x.CreateBranchAsync(repositoryOwner, repositoryName, baseBranch, newBranchName), Times.Once);
        mockGitHubService.Verify(x => x.CreateOrUpdateFileAsync(repositoryOwner, repositoryName, newBranchName, filePath, fileContent, commitMessage), Times.Once);
        mockGitHubService.Verify(x => x.CreatePullRequestAsync(repositoryOwner, repositoryName, baseBranch, newBranchName, pullRequestTitle, pullRequestBody), Times.Once);
    }
}
