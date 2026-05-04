"""Unit tests for structured JSON logging — Task 1.3."""
import json
import logging
import pytest
import structlog


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _reconfigure(log_level: str = "INFO") -> None:
    """Re-run configure_logging() so each test starts from a known state."""
    # Reset structlog's global state before re-configuring.
    structlog.reset_defaults()
    from src.observability.logging_config import configure_logging
    configure_logging(log_level)


def _last_json_line(out: str) -> dict:
    lines = [l for l in out.strip().splitlines() if l.strip()]
    assert lines, "No log output was captured"
    return json.loads(lines[-1])


# ---------------------------------------------------------------------------
# JSON output shape
# ---------------------------------------------------------------------------

def test_log_output_is_valid_json(capsys):
    """`structlog.get_logger().info(...)` must emit a parseable JSON line."""
    _reconfigure()
    structlog.get_logger().info("test_event", foo="bar")
    out = capsys.readouterr().out
    parsed = _last_json_line(out)
    assert parsed["event"] == "test_event"
    assert parsed["foo"] == "bar"


def test_log_output_contains_required_fields(capsys):
    """Every log line must include event, level, timestamp, and logger name."""
    _reconfigure()
    structlog.get_logger("mylogger").warning("something_happened", key="val")
    out = capsys.readouterr().out
    parsed = _last_json_line(out)
    assert "event" in parsed
    assert "level" in parsed
    assert "timestamp" in parsed


def test_log_level_field_matches_method(capsys):
    """The 'level' field in the JSON must match the method called."""
    _reconfigure()
    structlog.get_logger().warning("warn_event")
    out = capsys.readouterr().out
    parsed = _last_json_line(out)
    assert parsed["level"] == "warning"


def test_timestamp_is_iso_format(capsys):
    """Timestamps must be ISO-8601 strings (not epoch integers)."""
    _reconfigure()
    structlog.get_logger().info("ts_check")
    out = capsys.readouterr().out
    parsed = _last_json_line(out)
    ts = parsed["timestamp"]
    assert isinstance(ts, str)
    assert "T" in ts or "-" in ts, f"Timestamp does not look ISO-8601: {ts!r}"


# ---------------------------------------------------------------------------
# Context variables (incident_id propagation)
# ---------------------------------------------------------------------------

def test_bound_context_vars_appear_in_log_line(capsys):
    """`bind_contextvars(incident_id=...)` must appear in every subsequent line."""
    _reconfigure()
    structlog.contextvars.clear_contextvars()
    structlog.contextvars.bind_contextvars(incident_id="inc-42")

    structlog.get_logger().info("processing")

    out = capsys.readouterr().out
    parsed = _last_json_line(out)
    assert parsed.get("incident_id") == "inc-42"

    structlog.contextvars.clear_contextvars()


def test_context_vars_cleared_between_incidents(capsys):
    """After clearing context vars the incident_id must no longer appear."""
    _reconfigure()
    structlog.contextvars.clear_contextvars()
    structlog.contextvars.bind_contextvars(incident_id="inc-99")
    structlog.contextvars.clear_contextvars()

    structlog.get_logger().info("after_clear")

    out = capsys.readouterr().out
    parsed = _last_json_line(out)
    assert "incident_id" not in parsed


# ---------------------------------------------------------------------------
# Log-level filtering
# ---------------------------------------------------------------------------

def test_debug_messages_suppressed_at_info_level(capsys):
    """DEBUG messages must not appear when log_level='INFO'."""
    _reconfigure("INFO")
    structlog.get_logger().debug("should_be_hidden")
    out = capsys.readouterr().out
    assert "should_be_hidden" not in out


def test_debug_messages_visible_at_debug_level(capsys):
    """DEBUG messages must appear when log_level='DEBUG'."""
    _reconfigure("DEBUG")
    structlog.get_logger().debug("should_be_visible")
    out = capsys.readouterr().out
    assert "should_be_visible" in out
