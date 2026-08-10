import pytest

from orchestrator import UnknownTenantError, run_workflow, search_evidence
from orchestrator.fixtures import VENDOR_ID, document_ids_for_tenant

from .conftest import QUESTION


def test_search_returns_only_the_requesting_tenants_documents():
    ids = {d.document_id for d in search_evidence("tenant-a", VENDOR_ID, QUESTION)}
    assert ids == {"policy-a-001", "contract-a-002"}
    assert ids.isdisjoint(document_ids_for_tenant("tenant-b"))


def test_each_tenant_sees_its_own_evidence_for_the_same_vendor():
    a = {d.document_id for d in search_evidence("tenant-a", VENDOR_ID)}
    b = {d.document_id for d in search_evidence("tenant-b", VENDOR_ID)}
    assert a.isdisjoint(b)
    assert b == {"policy-b-001", "contract-b-002"}


def test_workflow_never_cites_another_tenants_document():
    for tenant in ("tenant-a", "tenant-b"):
        result = run_workflow(
            tenantId=tenant,
            userId=f"analyst@{tenant}.example",
            role="analyst",
            question=QUESTION,
        )
        cited = {c["documentId"] for c in result["citations"]}
        assert cited, "expected at least one citation"
        assert cited <= document_ids_for_tenant(tenant)


def test_same_question_yields_different_answers_per_tenant():
    kwargs = {"role": "analyst", "question": QUESTION}
    a = run_workflow(tenantId="tenant-a", userId="analyst@tenant-a.example", **kwargs)
    b = run_workflow(tenantId="tenant-b", userId="analyst@tenant-b.example", **kwargs)
    assert a["riskLevel"] == "medium"
    assert b["riskLevel"] == "high"


def test_unknown_tenant_fails_closed():
    with pytest.raises(UnknownTenantError):
        search_evidence("tenant-zzz", VENDOR_ID)

    with pytest.raises(UnknownTenantError):
        run_workflow(
            tenantId="tenant-zzz",
            userId="attacker@example.com",
            role="approver",
            question=QUESTION,
        )
