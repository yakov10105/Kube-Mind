import structlog
from langchain_core.tools import tool

from src.config import settings
from src.services.github_service import GitHubService
from src.services.slack_service import SlackNotificationService

log = structlog.get_logger()


@tool
async def create_fix_pull_request(
    repository_owner: str,
    repository_name: str,
    base_branch: str,
    new_branch_name: str,
    commit_message: str,
    file_path: str,
    file_content: str,
    pull_request_title: str,
    pull_request_body: str,
) -> str:
    """Creates a Pull Request in a GitHub repository with a proposed infrastructure fix.
    Returns the URL of the created Pull Request."""
    github = GitHubService(settings.github_token)
    slack = SlackNotificationService(settings.slack_webhook_url)

    await github.create_branch(
        repository_owner, repository_name, base_branch, new_branch_name
    )
    await github.create_or_update_file(
        repository_owner,
        repository_name,
        new_branch_name,
        file_path,
        file_content,
        commit_message,
    )
    pr_url = await github.create_pull_request(
        repository_owner,
        repository_name,
        base_branch,
        new_branch_name,
        pull_request_title,
        pull_request_body,
    )
    log.info("gitops.pr_created", pr_url=pr_url)
    if settings.slack_webhook_url:
        await slack.notify(f"Automated Fix Proposed! Review: {pr_url}")
    return pr_url
