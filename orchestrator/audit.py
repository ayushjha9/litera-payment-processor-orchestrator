"""In-memory audit log. Append-only within a process; no database, by design.

Audit records carry document *ids*, never document text — untrusted vendor
prose must not propagate into logs that humans and downstream tools read.
"""

from __future__ import annotations

import itertools
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Any


class AuditEventType(str, Enum):
    # Written when the workflow is invoked, before any evidence is read, so a
    # crash mid-run still leaves evidence that the run happened.
    WORKFLOW_RUN = "workflow_run"
    # Written before the risky action is attempted, allowed or blocked.
    ACTION_ATTEMPT = "action_attempt"
    # Written at the end of every run with the recommendation actually returned.
    DECISION = "decision"


@dataclass(frozen=True)
class AuditEvent:
    event_id: str
    timestamp: str
    event_type: AuditEventType
    tenant_id: str
    user_id: str
    role: str
    details: dict[str, Any] = field(default_factory=dict)

    def to_dict(self) -> dict:
        payload = asdict(self)
        payload["event_type"] = self.event_type.value
        return payload


_AUDIT_LOG: list[AuditEvent] = []
_event_counter = itertools.count(1)


def write_audit_event(
    *,
    event_type: AuditEventType,
    tenant_id: str,
    user_id: str,
    role: str,
    details: dict[str, Any] | None = None,
) -> str:
    """Append an event and return its id."""
    event = AuditEvent(
        event_id=f"evt-{next(_event_counter):06d}",
        timestamp=datetime.now(timezone.utc).isoformat(),
        event_type=event_type,
        tenant_id=tenant_id,
        user_id=user_id,
        role=role,
        details=dict(details or {}),
    )
    _AUDIT_LOG.append(event)
    return event.event_id


def read_audit_log(tenant_id: str | None = None) -> list[AuditEvent]:
    if tenant_id is None:
        return list(_AUDIT_LOG)
    return [e for e in _AUDIT_LOG if e.tenant_id == tenant_id]


def reset_audit_log() -> None:
    """Test helper — real audit storage is never truncated."""
    global _event_counter
    _AUDIT_LOG.clear()
    _event_counter = itertools.count(1)


writeAuditEvent = write_audit_event
