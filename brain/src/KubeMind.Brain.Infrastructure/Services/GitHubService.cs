using KubeMind.Brain.Application.Services;
using Octokit;

namespace KubeMind.Brain.Infrastructure.Services;

/// <summary>
/// Provides an implementation of IGitHubService using the Octokit library.
/// </summary>
public class GitHubService(GitHubClient gitHubClient) : IGitHubService
{

    /// <inheritdoc/>
    public async Task<string> CreateBranchAsync(string repositoryOwner, string repositoryName, string baseBranch, string newBranchName)
    {
        var latestCommit = await gitHubClient.Repository.Commit.Get(repositoryOwner, repositoryName, baseBranch);
        var baseSha = latestCommit.Sha;

        var newRef = new NewReference($"refs/heads/{newBranchName}", baseSha);
        var reference = await gitHubClient.Git.Reference.Create(repositoryOwner, repositoryName, newRef);

        return reference.Object.Sha; 
    }

    /// <inheritdoc/>
    public async Task<string> CreateOrUpdateFileAsync(string repositoryOwner, string repositoryName, string branchName, string filePath, string fileContent, string commitMessage)
    {
        string? fileSha = null;
        try
        {
            var existingFile = await gitHubClient.Repository.Content.GetAllContentsByRef(repositoryOwner, repositoryName, filePath, branchName);
            if (existingFile != null && existingFile.Count > 0)
            {
                fileSha = existingFile[0].Sha;
            }
        }
        catch (NotFoundException) { /* File does not exist, proceed with creation */ }

        if (fileSha != null)
        {
            var updateRequest = new UpdateFileRequest(commitMessage, fileContent, fileSha, branchName);
            var updateResult = await gitHubClient.Repository.Content.UpdateFile(repositoryOwner, repositoryName, filePath, updateRequest);
            return updateResult.Commit.Sha;
        }
        else
        {
            var createRequest = new CreateFileRequest(commitMessage, fileContent, branchName);
            var createResult = await gitHubClient.Repository.Content.CreateFile(repositoryOwner, repositoryName, filePath, createRequest);
            return createResult.Commit.Sha;
        }
    }

    /// <inheritdoc/>
    public async Task<string> CreatePullRequestAsync(string repositoryOwner, string repositoryName, string baseBranch, string headBranch, string title, string body)
    {
        var newPullRequest = new NewPullRequest(title, headBranch, baseBranch) { Body = body };
        var pullRequest = await gitHubClient.Repository.PullRequest.Create(repositoryOwner, repositoryName, newPullRequest);

        return pullRequest.HtmlUrl.ToString(); 
    }
}
