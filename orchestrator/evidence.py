"""Tenant-scoped evidence retrieval — the single tenant-filtering choke point."""

from __future__ import annotations

from .fixtures import TENANTS, all_documents
from .models import Document, UnknownTenantError


def search_evidence(tenant_id: str, vendor_id: str, question: str | None = None) -> list[Document]:
    """Return the documents this tenant holds for this vendor.

    Tenant filtering happens here and nowhere else, so it can be reasoned about
    and tested in one place. There is no caller-supplied document list and no
    fallback path: an unknown tenant raises rather than returning anything.

    ``question`` is accepted for signature fidelity and audit context; retrieval
    in this mock is exhaustive for the vendor rather than ranked, since a real
    retriever (BM25/embeddings) is out of scope.
    """
    if tenant_id not in TENANTS:
        raise UnknownTenantError(f"unknown tenant: {tenant_id!r}")

    return [d for d in all_documents() if d.tenant_id == tenant_id and d.vendor_id == vendor_id]


# Spec-name alias, so the exercise's camelCase names are directly importable.
searchEvidence = search_evidence
