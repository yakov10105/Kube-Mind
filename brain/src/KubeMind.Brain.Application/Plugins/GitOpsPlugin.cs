using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using KubeMind.Brain.Application.Services; // Add this using directive

namespace KubeMind.Brain.Application.Plugins;

/// <summary>
/// A Semantic Kernel plugin for GitOps operations, specifically creating Pull Requests.
/// </summary>
public class GitOpsPlugin(ILogger<GitOpsPlugin> logger, IGitHubService gitHubService)
{
    [KernelFunction]
    [Description("Creates a Pull Request in a specified GitHub repository with a proposed fix.")]
    public async Task<string> CreateFixPullRequest(
        [Description("The owner of the GitHub repository (e.g., 'octocat').")] string repositoryOwner,
        [Description("The name of the GitHub repository (e.g., 'Spoon-Knife').")] string repositoryName,
        [Description("The base branch to create the pull request against (e.g., 'main').")] string baseBranch,
        [Description("The name of the new branch to create for the fix.")] string newBranchName,
        [Description("The commit message for the proposed changes.")] string commitMessage,
        [Description("The file path within the repository to modify or create (e.g., 'deploy/helm/values.yaml').")] string filePath,
        [Description("The new content for the specified file.")] string fileContent,
        [Description("The title of the Pull Request.")] string pullRequestTitle,
        [Description("The body/description of the Pull Request.")] string pullRequestBody)
    {
        logger.LogInformation("Attempting to create PR for {RepoOwner}/{RepoName} on branch {NewBranchName}", repositoryOwner, repositoryName, newBranchName);

        await gitHubService.CreateBranchAsync(repositoryOwner, repositoryName, baseBranch, newBranchName);
        await gitHubService.CreateOrUpdateFileAsync(repositoryOwner, repositoryName, newBranchName, filePath, fileContent, commitMessage);
        var prUrl = await gitHubService.CreatePullRequestAsync(repositoryOwner, repositoryName, baseBranch, newBranchName, pullRequestTitle, pullRequestBody);

        logger.LogInformation("Pull Request created: {PullRequestUrl}", prUrl);

        return prUrl;
    }
}
