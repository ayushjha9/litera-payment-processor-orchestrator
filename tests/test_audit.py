from orchestrator import AuditEventType, read_audit_log, run_workflow
from orchestrator.fixtures import injected_text

from .conftest import QUESTION

ACTION_REQUEST = {
    "tenantId": "tenant-b",
    "userId": "approver@tenant-b.example",
    "role": "approver",
    "question": QUESTION,
    "requestedAction": "markVendorApproved",
}


def _types():
    return [e.event_type for e in read_audit_log()]


def test_every_workflow_run_writes_an_audit_event():
    run_workflow(
        tenantId="tenant-a",
        userId="analyst@tenant-a.example",
        role="analyst",
        question=QUESTION,
    )

    assert AuditEventType.WORKFLOW_RUN in _types()
    assert AuditEventType.DECISION in _types()


def test_blocked_action_still_writes_an_action_attempt_event():
    run_workflow(ACTION_REQUEST)

    attempts = [e for e in read_audit_log() if e.event_type is AuditEventType.ACTION_ATTEMPT]
    assert len(attempts) == 1
    assert attempts[0].details["action"] == "markVendorApproved"
    assert attempts[0].details["gateVerdict"] == "blocked_pending_approval"
    assert attempts[0].details["approvalValid"] is False


def test_executed_action_writes_an_action_attempt_event():
    run_workflow({**ACTION_REQUEST, "approvedBy": "compliance@tenant-b.example"})

    attempts = [e for e in read_audit_log() if e.event_type is AuditEventType.ACTION_ATTEMPT]
    assert len(attempts) == 1
    assert attempts[0].details["approvalValid"] is True

    decision = [e for e in read_audit_log() if e.event_type is AuditEventType.DECISION][-1]
    assert decision.details["actionStatus"] == "executed"


def test_returned_audit_ids_match_the_log():
    result = run_workflow(ACTION_REQUEST)

    logged = [e.event_id for e in read_audit_log()]
    assert result["auditEventIds"] == logged


def test_audit_events_carry_document_ids_not_untrusted_text():
    run_workflow(ACTION_REQUEST)

    for event in read_audit_log():
        assert injected_text() not in str(event.details)

    decision = [e for e in read_audit_log() if e.event_type is AuditEventType.DECISION][-1]
    assert "contract-b-002" in decision.details["citationDocumentIds"]


def test_audit_events_are_scoped_to_the_requesting_tenant():
    run_workflow(ACTION_REQUEST)
    run_workflow(
        tenantId="tenant-a",
        userId="analyst@tenant-a.example",
        role="analyst",
        question=QUESTION,
    )

    assert {e.tenant_id for e in read_audit_log("tenant-a")} == {"tenant-a"}
    for event in read_audit_log("tenant-a"):
        assert "tenant-b" not in str(event.details)
