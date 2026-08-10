import pytest

from orchestrator.actions import reset_vendor_status
from orchestrator.audit import reset_audit_log

QUESTION = "Can we approve Vendor X to process customer payment data?"


@pytest.fixture(autouse=True)
def clean_state():
    """Every test starts with an empty audit log and an unapproved vendor."""
    reset_audit_log()
    reset_vendor_status()
    yield
    reset_audit_log()
    reset_vendor_status()
