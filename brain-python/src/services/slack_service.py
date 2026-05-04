import httpx
import structlog

log = structlog.get_logger()


class SlackNotificationService:
    def __init__(self, webhook_url: str) -> None:
        self._webhook_url = webhook_url

    async def notify(self, message: str) -> None:
        if not self._webhook_url:
            log.warning("slack.webhook_not_configured")
            return
        async with httpx.AsyncClient() as client:
            response = await client.post(
                self._webhook_url,
                json={"text": message},
                timeout=10.0,
            )
            response.raise_for_status()
            log.info("slack.notification_sent", message=message[:100])


_service: SlackNotificationService | None = None


def get_slack_service() -> SlackNotificationService:
    global _service
    if _service is None:
        from src.config import settings
        _service = SlackNotificationService(settings.slack_webhook_url)
    return _service
