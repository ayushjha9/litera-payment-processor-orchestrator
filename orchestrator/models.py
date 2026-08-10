"""Types and the validated output contract.

Internally everything is snake_case Python. The camelCase contract from the spec
(``riskLevel``, ``actionStatus``, ``missingEvidence``, ...) exists only at the
boundary: ``WorkflowResult.to_dict()`` on the way out, and the accepted input
keys of ``run_workflow`` on the way in.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum

SNIPPET_MAX_CHARS = 240


class RiskLevel(str, Enum):
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"


class ActionStatus(str, Enum):
    NOT_REQUESTED = "not_requested"
    EXECUTED = "executed"
    BLOCKED_PENDING_APPROVAL = "blocked_pending_approval"
    BLOCKED_UNAUTHORIZED = "blocked_unauthorized"
    BLOCKED_UNKNOWN_ACTION = "blocked_unknown_action"


class Role(str, Enum):
    VIEWER = "viewer"
    ANALYST = "analyst"
    APPROVER = "approver"


class UnknownTenantError(ValueError):
    """Raised when a workflow is invoked for a tenant we do not know about.

    Fail closed: an unrecognised tenant must never fall through to an unfiltered
    document set.
    """


class OutputContractError(AssertionError):
    """Raised when a WorkflowResult violates the output contract or a safety invariant."""


@dataclass(frozen=True)
class Document:
    """A piece of tenant-scoped evidence.

    ``text`` is vendor-supplied prose. It is UNTRUSTED: it is only ever read to
    build citation snippets and to *detect* (never obey) instruction-like
    content. Every risk decision is made from the structured ``has_*`` flags,
    which is what a real evidence-intake pipeline would extract and a human
    would attest to. That split is the trust boundary of this system.
    """

    document_id: str
    tenant_id: str
    vendor_id: str
    doc_type: str  # "policy" | "contract"
    title: str
    text: str
    has_soc2: bool = False
    has_encryption: bool = False
    has_breach_notification: bool = False
    has_retention_schedule: bool = False


@dataclass(frozen=True)
class Citation:
    document_id: str
    snippet: str

    def to_dict(self) -> dict:
        return {"documentId": self.document_id, "snippet": self.snippet}


@dataclass(frozen=True)
class RiskAssessment:
    risk_level: RiskLevel
    reasons: list[str]
    citations: list[Citation]
    missing_evidence: list[str]


@dataclass(frozen=True)
class ApprovalDecision:
    requires_approval: bool
    approved: bool
    action_status: ActionStatus
    reason: str


@dataclass(frozen=True)
class ActionResult:
    status: ActionStatus
    detail: str
    receipt: dict | None = None


@dataclass
class WorkflowResult:
    risk_level: RiskLevel
    recommendation: str
    reasons: list[str]
    citations: list[Citation]
    missing_evidence: list[str]
    requires_approval: bool
    action_status: ActionStatus
    audit_event_ids: list[str] = field(default_factory=list)

    def validate(self, allowed_document_ids: set[str], approval_recorded: bool) -> None:
        """Constrain the output shape and assert the safety invariants.

        ``allowed_document_ids`` is the requesting tenant's document set, so a
        citation can never point outside it.
        """
        if not isinstance(self.risk_level, RiskLevel):
            raise OutputContractError(f"riskLevel must be a RiskLevel, got {self.risk_level!r}")
        if not isinstance(self.action_status, ActionStatus):
            raise OutputContractError(f"actionStatus must be an ActionStatus, got {self.action_status!r}")
        if not self.recommendation:
            raise OutputContractError("recommendation must be non-empty")
        if not self.reasons:
            raise OutputContractError("reasons must be non-empty")
        if not all(isinstance(r, str) and r for r in self.reasons):
            raise OutputContractError("every reason must be a non-empty string")
        if not all(isinstance(m, str) and m for m in self.missing_evidence):
            raise OutputContractError("every missingEvidence entry must be a non-empty string")

        for citation in self.citations:
            if citation.document_id not in allowed_document_ids:
                raise OutputContractError(
                    f"citation {citation.document_id!r} is outside the requesting tenant's documents"
                )
            if len(citation.snippet) > SNIPPET_MAX_CHARS:
                raise OutputContractError(f"citation snippet exceeds {SNIPPET_MAX_CHARS} chars")

        # Safety invariants — the part that actually matters.
        if self.risk_level is RiskLevel.HIGH and not self.requires_approval:
            raise OutputContractError("high risk must require approval")
        if self.action_status is ActionStatus.EXECUTED and self.requires_approval and not approval_recorded:
            raise OutputContractError("action executed while approval was required but not recorded")

    def to_dict(self) -> dict:
        return {
            "riskLevel": self.risk_level.value,
            "recommendation": self.recommendation,
            "reasons": list(self.reasons),
            "citations": [c.to_dict() for c in self.citations],
            "missingEvidence": list(self.missing_evidence),
            "requiresApproval": self.requires_approval,
            "actionStatus": self.action_status.value,
            "auditEventIds": list(self.audit_event_ids),
        }
