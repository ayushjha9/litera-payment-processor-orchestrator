import pytest

from orchestrator import run_workflow, vendor_status

from .conftest import QUESTION

BASE = {
    "tenantId": "tenant-b",
    "question": QUESTION,
    "requestedAction": "markVendorApproved",
    "approvedBy": "compliance@tenant-b.example",
}


@pytest.mark.parametrize("role", ["viewer", "analyst"])
def test_unauthorized_role_cannot_execute_even_with_a_valid_approval(role):
    result = run_workflow({**BASE, "userId": f"{role}@tenant-b.example", "role": role})

    assert result["actionStatus"] == "blocked_unauthorized"
    assert vendor_status("tenant-b", "vendor-x") == "pending"


def test_approver_role_can_execute_with_a_valid_approval():
    result = run_workflow({**BASE, "userId": "approver@tenant-b.example", "role": "approver"})

    assert result["actionStatus"] == "executed"
    assert vendor_status("tenant-b", "vendor-x") == "approved"


def test_unknown_role_is_rejected_at_the_input_boundary():
    with pytest.raises(ValueError):
        run_workflow({**BASE, "userId": "x@tenant-b.example", "role": "superadmin"})
