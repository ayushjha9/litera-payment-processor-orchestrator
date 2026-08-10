import pytest

from orchestrator import run_workflow
from orchestrator.models import ActionStatus, Citation, OutputContractError, RiskLevel, WorkflowResult

from .conftest import QUESTION

EXPECTED_KEYS = {
    "riskLevel",
    "recommendation",
    "reasons",
    "citations",
    "missingEvidence",
    "requiresApproval",
    "actionStatus",
    "auditEventIds",
}


def test_result_matches_the_documented_json_shape():
    result = run_workflow(
        tenantId="tenant-b",
        userId="approver@tenant-b.example",
        role="approver",
        question=QUESTION,
        requestedAction="markVendorApproved",
    )

    assert set(result) == EXPECTED_KEYS
    assert result["riskLevel"] in {r.value for r in RiskLevel}
    assert result["actionStatus"] in {a.value for a in ActionStatus}
    assert isinstance(result["requiresApproval"], bool)
    for citation in result["citations"]:
        assert set(citation) == {"documentId", "snippet"}
    assert "SOC 2 report" in result["missingEvidence"]


def test_validation_rejects_a_citation_outside_the_tenants_documents():
    result = WorkflowResult(
        risk_level=RiskLevel.LOW,
        recommendation="ok",
        reasons=["fine"],
        citations=[Citation("contract-b-002", "leaked")],
        missing_evidence=[],
        requires_approval=False,
        action_status=ActionStatus.NOT_REQUESTED,
    )

    with pytest.raises(OutputContractError):
        result.validate(allowed_document_ids={"policy-a-001"}, approval_recorded=False)


def test_validation_rejects_high_risk_that_does_not_require_approval():
    result = WorkflowResult(
        risk_level=RiskLevel.HIGH,
        recommendation="ok",
        reasons=["missing everything"],
        citations=[],
        missing_evidence=["SOC 2 report"],
        requires_approval=False,
        action_status=ActionStatus.NOT_REQUESTED,
    )

    with pytest.raises(OutputContractError):
        result.validate(allowed_document_ids=set(), approval_recorded=False)


def test_validation_rejects_execution_without_a_recorded_approval():
    result = WorkflowResult(
        risk_level=RiskLevel.HIGH,
        recommendation="ok",
        reasons=["missing everything"],
        citations=[],
        missing_evidence=["SOC 2 report"],
        requires_approval=True,
        action_status=ActionStatus.EXECUTED,
    )

    with pytest.raises(OutputContractError):
        result.validate(allowed_document_ids=set(), approval_recorded=False)


def test_unknown_request_field_is_rejected():
    with pytest.raises(ValueError):
        run_workflow(
            tenantId="tenant-a",
            userId="a@tenant-a.example",
            role="analyst",
            question=QUESTION,
            forceApprove=True,
        )
