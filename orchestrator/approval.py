"""Authorization and the human-in-the-loop approval gate."""

from __future__ import annotations

from .fixtures import APPROVERS
from .models import ActionStatus, ApprovalDecision, Role, RiskLevel

# Allow-list, not a deny-list: an action nobody wrote a rule for is refused.
RISKY_ACTIONS: dict[str, set[Role]] = {
    "markVendorApproved": {Role.APPROVER},
}


def is_valid_approver(tenant_id: str, approved_by: str | None, user_id: str) -> bool:
    """An approval counts only if the approver belongs to *this* tenant and is
    not the person making the request (no self-approval)."""
    if not approved_by:
        return False
    if approved_by == user_id:
        return False
    return approved_by in APPROVERS.get(tenant_id, set())


def request_or_verify_approval(
    *,
    tenant_id: str,
    user_id: str,
    role: Role,
    risk_level: RiskLevel,
    requested_action: str | None,
    approved_by: str | None,
) -> ApprovalDecision:
    """Decide whether the requested action may proceed.

    Only HIGH risk blocks. MEDIUM returns a caution in the recommendation but
    does not gate, so ``requiresApproval`` stays a truthful statement about the
    gate rather than an advisory flag that contradicts ``actionStatus``.

    Authorization is checked *before* approval, so an unauthorized role is
    refused even while holding a valid approval.
    """
    requires_approval = risk_level is RiskLevel.HIGH
    approved = is_valid_approver(tenant_id, approved_by, user_id)

    if not requested_action:
        return ApprovalDecision(
            requires_approval=requires_approval,
            approved=approved,
            action_status=ActionStatus.NOT_REQUESTED,
            reason="No action requested; advisory answer only.",
        )

    if requested_action not in RISKY_ACTIONS:
        return ApprovalDecision(
            requires_approval=requires_approval,
            approved=approved,
            action_status=ActionStatus.BLOCKED_UNKNOWN_ACTION,
            reason=f"Action {requested_action!r} is not on the allow-list of executable actions.",
        )

    if role not in RISKY_ACTIONS[requested_action]:
        return ApprovalDecision(
            requires_approval=requires_approval,
            approved=approved,
            action_status=ActionStatus.BLOCKED_UNAUTHORIZED,
            reason=f"Role {role.value!r} is not permitted to execute {requested_action!r}.",
        )

    if requires_approval and not approved:
        if approved_by and approved_by == user_id:
            detail = "self-approval is not permitted"
        elif approved_by:
            detail = f"{approved_by!r} is not a registered approver for {tenant_id!r}"
        else:
            detail = "no approval was supplied"
        return ApprovalDecision(
            requires_approval=True,
            approved=False,
            action_status=ActionStatus.BLOCKED_PENDING_APPROVAL,
            reason=f"High-risk action requires a recorded human approval: {detail}.",
        )

    return ApprovalDecision(
        requires_approval=requires_approval,
        approved=approved,
        action_status=ActionStatus.EXECUTED,
        reason=(
            f"Approved by {approved_by!r}."
            if approved
            else f"{risk_level.value.capitalize()}-risk action does not require approval."
        ),
    )


requestOrVerifyApproval = request_or_verify_approval
