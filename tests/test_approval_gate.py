from orchestrator import run_workflow, vendor_status

from .conftest import QUESTION

HIGH_RISK_REQUEST = {
    "tenantId": "tenant-b",
    "userId": "approver@tenant-b.example",
    "role": "approver",
    "question": QUESTION,
    "requestedAction": "markVendorApproved",
}


def test_high_risk_action_is_blocked_without_approval():
    result = run_workflow(HIGH_RISK_REQUEST)

    assert result["riskLevel"] == "high"
    assert result["requiresApproval"] is True
    assert result["actionStatus"] == "blocked_pending_approval"
    assert result["recommendation"] == "Do not approve yet."
    assert vendor_status("tenant-b", "vendor-x") == "pending"


def test_high_risk_action_executes_with_a_registered_approver():
    result = run_workflow({**HIGH_RISK_REQUEST, "approvedBy": "compliance@tenant-b.example"})

    assert result["actionStatus"] == "executed"
    assert vendor_status("tenant-b", "vendor-x") == "approved"


def test_approver_from_another_tenant_is_rejected():
    result = run_workflow({**HIGH_RISK_REQUEST, "approvedBy": "alice@tenant-a.example"})

    assert result["actionStatus"] == "blocked_pending_approval"
    assert vendor_status("tenant-b", "vendor-x") == "pending"


def test_self_approval_is_rejected():
    result = run_workflow({**HIGH_RISK_REQUEST, "approvedBy": HIGH_RISK_REQUEST["userId"]})

    assert result["actionStatus"] == "blocked_pending_approval"
    assert vendor_status("tenant-b", "vendor-x") == "pending"


def test_unrecognised_approver_string_is_rejected():
    result = run_workflow({**HIGH_RISK_REQUEST, "approvedBy": "totally-made-up@evil.example"})

    assert result["actionStatus"] == "blocked_pending_approval"
    assert vendor_status("tenant-b", "vendor-x") == "pending"


def test_medium_risk_proceeds_without_approval():
    result = run_workflow(
        tenantId="tenant-a",
        userId="approver@tenant-a.example",
        role="approver",
        question=QUESTION,
        requestedAction="markVendorApproved",
    )

    assert result["riskLevel"] == "medium"
    assert result["requiresApproval"] is False
    assert result["actionStatus"] == "executed"
    assert vendor_status("tenant-a", "vendor-x") == "approved"


def test_action_outside_the_allow_list_is_refused():
    result = run_workflow({**HIGH_RISK_REQUEST, "requestedAction": "deleteAllVendors"})

    assert result["actionStatus"] == "blocked_unknown_action"
    assert vendor_status("tenant-b", "vendor-x") == "pending"


def test_advisory_run_reports_not_requested():
    result = run_workflow(
        tenantId="tenant-b",
        userId="analyst@tenant-b.example",
        role="analyst",
        question=QUESTION,
    )

    assert result["actionStatus"] == "not_requested"
    assert result["requiresApproval"] is True  # high risk still needs sign-off
