import asyncio

import structlog
from github import Github, GithubException

log = structlog.get_logger()


class GitHubService:
    def __init__(self, token: str) -> None:
        self._client = Github(token)

    async def create_branch(
        self, owner: str, repo: str, base: str, new_branch: str
    ) -> None:
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(
            None, self._create_branch_sync, owner, repo, base, new_branch
        )

    def _create_branch_sync(
        self, owner: str, repo: str, base: str, new_branch: str
    ) -> None:
        repository = self._client.get_repo(f"{owner}/{repo}")
        base_sha = repository.get_branch(base).commit.sha
        repository.create_git_ref(ref=f"refs/heads/{new_branch}", sha=base_sha)
        log.info("github.branch_created", branch=new_branch)

    async def create_or_update_file(
        self,
        owner: str,
        repo: str,
        branch: str,
        path: str,
        content: str,
        message: str,
    ) -> None:
        loop = asyncio.get_event_loop()
        await loop.run_in_executor(
            None,
            self._create_or_update_file_sync,
            owner,
            repo,
            branch,
            path,
            content,
            message,
        )

    def _create_or_update_file_sync(
        self,
        owner: str,
        repo: str,
        branch: str,
        path: str,
        content: str,
        message: str,
    ) -> None:
        repository = self._client.get_repo(f"{owner}/{repo}")
        try:
            existing = repository.get_contents(path, ref=branch)
            repository.update_file(path, message, content, existing.sha, branch=branch)  # type: ignore[union-attr]
        except GithubException:
            repository.create_file(path, message, content, branch=branch)
        log.info("github.file_committed", path=path)

    async def create_pull_request(
        self,
        owner: str,
        repo: str,
        base: str,
        head: str,
        title: str,
        body: str,
    ) -> str:
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(
            None, self._create_pr_sync, owner, repo, base, head, title, body
        )

    def _create_pr_sync(
        self, owner: str, repo: str, base: str, head: str, title: str, body: str
    ) -> str:
        repository = self._client.get_repo(f"{owner}/{repo}")
        pr = repository.create_pull(title=title, body=body, base=base, head=head)
        log.info("github.pr_created", url=pr.html_url)
        return pr.html_url
