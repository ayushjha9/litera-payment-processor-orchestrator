"""The one risky action, mocked. Nothing here leaves the process."""

from __future__ import annotations

from .approval import RISKY_ACTIONS
from .models import ActionResult, ActionStatus, ApprovalDecision, Role

# (tenant_id, vendor_id) -> "approved"
VENDOR_APPROVAL_STATUS: dict[tuple[str, str], str] = {}


def vendor_status(tenant_id: str, vendor_id: str) -> str:
    return VENDOR_APPROVAL_STATUS.get((tenant_id, vendor_id), "pending")


def reset_vendor_status() -> None:
    """Test helper."""
    VENDOR_APPROVAL_STATUS.clear()


def execute_mock_action(
    *,
    action: str,
    tenant_id: str,
    vendor_id: str,
    role: Role,
    decision: ApprovalDecision,
) -> ActionResult:
    """Perform the action, re-checking the gate rather than trusting the caller.

    A second check here is deliberate: the gate and the effect are separate
    modules, and the effect should not be reachable by a future caller that
    forgets to consult the gate.
    """
    if action not in RISKY_ACTIONS:
        return ActionResult(ActionStatus.BLOCKED_UNKNOWN_ACTION, f"Unknown action {action!r}.")
    if role not in RISKY_ACTIONS[action]:
        return ActionResult(ActionStatus.BLOCKED_UNAUTHORIZED, f"Role {role.value!r} may not execute {action!r}.")
    if decision.action_status is not ActionStatus.EXECUTED:
        return ActionResult(decision.action_status, decision.reason)
    if decision.requires_approval and not decision.approved:
        return ActionResult(ActionStatus.BLOCKED_PENDING_APPROVAL, "Approval required but not recorded.")

    VENDOR_APPROVAL_STATUS[(tenant_id, vendor_id)] = "approved"
    return ActionResult(
        status=ActionStatus.EXECUTED,
        detail=f"{action} recorded for {vendor_id}.",
        receipt={"action": action, "tenantId": tenant_id, "vendorId": vendor_id, "status": "approved"},
    )


executeMockAction = execute_mock_action
