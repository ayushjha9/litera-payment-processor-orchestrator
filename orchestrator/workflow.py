"""The orchestrator: retrieve → assess → gate → act → audit."""

from __future__ import annotations

from .actions import execute_mock_action
from .approval import request_or_verify_approval
from .audit import AuditEventType, write_audit_event
from .evidence import search_evidence
from .fixtures import VENDOR_ID, document_ids_for_tenant
from .models import (
    ActionStatus,
    ApprovalDecision,
    Role,
    RiskLevel,
    UnknownTenantError,
    WorkflowResult,
)
from .risk import evaluate_risk

_RECOMMENDATIONS = {
    RiskLevel.LOW: "Approve. Evidence supports processing customer payment data.",
    RiskLevel.MEDIUM: "Approve with conditions. Close the gaps below before renewal.",
    RiskLevel.HIGH: "Do not approve yet.",
}

# Accept the spec's camelCase input keys as well as snake_case.
_INPUT_ALIASES = {
    "tenantId": "tenant_id",
    "userId": "user_id",
    "requestedAction": "requested_action",
    "approvedBy": "approved_by",
}


def _normalise_request(request: dict) -> dict:
    normalised = {_INPUT_ALIASES.get(k, k): v for k, v in request.items()}
    unknown = set(normalised) - {"tenant_id", "user_id", "role", "question", "requested_action", "approved_by"}
    if unknown:
        raise ValueError(f"unknown request field(s): {sorted(unknown)}")
    for required in ("tenant_id", "user_id", "role", "question"):
        if not normalised.get(required):
            raise ValueError(f"missing required field: {required}")
    try:
        normalised["role"] = Role(normalised["role"])
    except ValueError:
        raise ValueError(f"unknown role: {normalised['role']!r}") from None
    normalised.setdefault("requested_action", None)
    normalised.setdefault("approved_by", None)
    return normalised


def _recommendation(risk_level: RiskLevel, decision: ApprovalDecision) -> str:
    base = _RECOMMENDATIONS[risk_level]
    if decision.action_status is ActionStatus.BLOCKED_UNAUTHORIZED:
        return f"{base} The requesting role is not permitted to execute this action."
    if risk_level is RiskLevel.HIGH and decision.approved:
        return "Proceed. A registered approver has accepted the documented risk."
    return base


def run_workflow(request: dict | None = None, **kwargs) -> dict:
    """Answer a vendor question, and gate any action it asks for.

    Accepts either ``run_workflow({"tenantId": ..., ...})`` or keyword
    arguments. Returns the validated JSON-ready result dict.
    """
    payload = _normalise_request({**(request or {}), **kwargs})
    tenant_id = payload["tenant_id"]
    user_id = payload["user_id"]
    role: Role = payload["role"]
    question = payload["question"]
    requested_action = payload["requested_action"]
    approved_by = payload["approved_by"]

    audit_ids = [
        write_audit_event(
            event_type=AuditEventType.WORKFLOW_RUN,
            tenant_id=tenant_id,
            user_id=user_id,
            role=role.value,
            details={"question": question, "requestedAction": requested_action, "approvalSupplied": bool(approved_by)},
        )
    ]

    documents = search_evidence(tenant_id, VENDOR_ID, question)
    assessment = evaluate_risk(documents, question)

    decision = request_or_verify_approval(
        tenant_id=tenant_id,
        user_id=user_id,
        role=role,
        risk_level=assessment.risk_level,
        requested_action=requested_action,
        approved_by=approved_by,
    )

    action_status = decision.action_status
    if requested_action:
        audit_ids.append(
            write_audit_event(
                event_type=AuditEventType.ACTION_ATTEMPT,
                tenant_id=tenant_id,
                user_id=user_id,
                role=role.value,
                details={
                    "action": requested_action,
                    "riskLevel": assessment.risk_level.value,
                    "requiresApproval": decision.requires_approval,
                    "approvalValid": decision.approved,
                    "approvedBy": approved_by,
                    "gateVerdict": decision.action_status.value,
                    "gateReason": decision.reason,
                },
            )
        )
        result = execute_mock_action(
            action=requested_action,
            tenant_id=tenant_id,
            vendor_id=VENDOR_ID,
            role=role,
            decision=decision,
        )
        action_status = result.status

    workflow_result = WorkflowResult(
        risk_level=assessment.risk_level,
        recommendation=_recommendation(assessment.risk_level, decision),
        reasons=assessment.reasons,
        citations=assessment.citations,
        missing_evidence=assessment.missing_evidence,
        requires_approval=decision.requires_approval,
        action_status=action_status,
    )

    audit_ids.append(
        write_audit_event(
            event_type=AuditEventType.DECISION,
            tenant_id=tenant_id,
            user_id=user_id,
            role=role.value,
            details={
                "riskLevel": assessment.risk_level.value,
                "recommendation": workflow_result.recommendation,
                "actionStatus": action_status.value,
                # Ids only — untrusted document text never enters the audit log.
                "citationDocumentIds": [c.document_id for c in assessment.citations],
                "missingEvidence": list(assessment.missing_evidence),
            },
        )
    )
    workflow_result.audit_event_ids = audit_ids

    workflow_result.validate(
        allowed_document_ids=document_ids_for_tenant(tenant_id),
        approval_recorded=decision.approved,
    )
    return workflow_result.to_dict()


runWorkflow = run_workflow

__all__ = ["run_workflow", "runWorkflow", "UnknownTenantError"]
