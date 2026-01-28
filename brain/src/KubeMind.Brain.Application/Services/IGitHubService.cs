namespace KubeMind.Brain.Application.Services;

/// <summary>
/// Defines the contract for interacting with GitHub for GitOps operations.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Creates a new branch from a base branch.
    /// </summary>
    /// <param name="repositoryOwner">The owner of the repository.</param>
    /// <param name="repositoryName">The name of the repository.</param>
    /// <param name="baseBranch">The branch to create the new branch from.</param>
    /// <param name="newBranchName">The name of the new branch.</param>
    /// <returns>The SHA of the new branch.</returns>
    Task<string> CreateBranchAsync(string repositoryOwner, string repositoryName, string baseBranch, string newBranchName);

    /// <summary>
    /// Creates or updates a file in a specified branch.
    /// </summary>
    /// <param name="repositoryOwner">The owner of the repository.</param>
    /// <param name="repositoryName">The name of the repository.</param>
    /// <param name="branchName">The branch where the file will be updated or created.</param>
    /// <param name="filePath">The path to the file.</param>
    /// <param name="fileContent">The content of the file.</param>
    /// <param name="commitMessage">The commit message.</param>
    /// <returns>The SHA of the updated or created file.</returns>
    Task<string> CreateOrUpdateFileAsync(string repositoryOwner, string repositoryName, string branchName, string filePath, string fileContent, string commitMessage);

    /// <summary>
    /// Creates a Pull Request.
    /// </summary>
    /// <param name="repositoryOwner">The owner of the repository.</param>
    /// <param name="repositoryName">The name of the repository.</param>
    /// <param name="baseBranch">The base branch for the PR.</param>
    /// <param name="headBranch">The head branch for the PR.</param>
    /// <param name="title">The title of the PR.</param>
    /// <param name="body">The body of the PR.</param>
    /// <returns>The URL of the created Pull Request.</returns>
    Task<string> CreatePullRequestAsync(string repositoryOwner, string repositoryName, string baseBranch, string headBranch, string title, string body);
}
