"""Deterministic, rule-based risk evaluation. No model call, no free-text parsing.

The trust boundary lives here. Risk is computed purely from the structured
``has_*`` flags on each Document. Document prose is read for exactly two
purposes, neither of which can influence a decision in the attacker's favour:

  1. to quote a snippet back as a citation, and
  2. to *detect* instruction-like content, which can only ever raise risk.

There is no code path where document text lowers a risk score, opens the
approval gate, or reaches an interpreter.
"""

from __future__ import annotations

import re

from .models import Citation, Document, RiskAssessment, RiskLevel, SNIPPET_MAX_CHARS

# Phrases that look like an instruction aimed at an automated reviewer. Matching
# one is a tampering signal, not a command.
INJECTION_PATTERNS: tuple[re.Pattern[str], ...] = (
    re.compile(r"ignore (all |any )?(previous|prior|above) instructions", re.I),
    re.compile(r"disregard (the )?(risk|policy|policies|instructions)", re.I),
    re.compile(r"mark (this|the) vendor (as )?approved", re.I),
    re.compile(r"regardless of risk", re.I),
    re.compile(r"report risk level (low|medium)", re.I),
)

# (flag name, weight, reason, missing-evidence label)
_CONTROLS: tuple[tuple[str, int, str, str], ...] = (
    ("has_soc2", 2, "No SOC 2 evidence found.", "SOC 2 report"),
    ("has_breach_notification", 1, "Contract lacks breach notification language.", "breach notification clause"),
    ("has_encryption", 1, "No evidence of encryption controls for payment data.", "encryption controls"),
    ("has_retention_schedule", 1, "No documented data retention schedule on file.", "data retention schedule"),
)

_HIGH_THRESHOLD = 3
_MEDIUM_THRESHOLD = 1


def _excerpt(text: str, around: str | None = None) -> str:
    """A citation-sized excerpt, centred on ``around`` when it occurs in ``text``."""
    if around and around in text:
        start = max(0, text.index(around) - 40)
        excerpt = text[start : start + SNIPPET_MAX_CHARS]
        return ("..." + excerpt) if start > 0 else excerpt
    if len(text) <= SNIPPET_MAX_CHARS:
        return text
    return text[: SNIPPET_MAX_CHARS - 3] + "..."


def _find_injection(documents: list[Document]) -> tuple[Document, str] | None:
    for document in documents:
        for pattern in INJECTION_PATTERNS:
            match = pattern.search(document.text)
            if match:
                return document, match.group(0)
    return None


def evaluate_risk(documents: list[Document], question: str | None = None) -> RiskAssessment:
    """Score the evidence and explain the score with citations.

    Pure and deterministic: the same documents always yield the same assessment.
    """
    policies = [d for d in documents if d.doc_type == "policy"]
    contracts = [d for d in documents if d.doc_type != "policy"]
    requirement_source = policies[0] if policies else (documents[0] if documents else None)

    score = 0
    reasons: list[str] = []
    citations: list[Citation] = []
    missing_evidence: list[str] = []

    if not documents:
        return RiskAssessment(
            risk_level=RiskLevel.HIGH,
            reasons=["No evidence on file for this vendor."],
            citations=[],
            missing_evidence=[label for _, _, _, label in _CONTROLS],
        )

    for flag, weight, reason, label in _CONTROLS:
        satisfied = any(getattr(d, flag) for d in documents)
        if satisfied:
            continue
        score += weight
        reasons.append(reason)
        missing_evidence.append(label)
        # Cite the policy that requires the control, not the document that lacks
        # it — that is what a reviewer needs to see.
        if requirement_source is not None:
            citations.append(
                Citation(document_id=requirement_source.document_id, snippet=_excerpt(requirement_source.text))
            )

    injection = _find_injection(documents)
    if injection is not None:
        document, matched = injection
        score += 1
        reasons.append(
            "Evidence contains instruction-like text addressed to an automated reviewer. "
            "Treated as untrusted content and as a tampering signal; it does not affect the decision."
        )
        citations.append(Citation(document_id=document.document_id, snippet=_excerpt(document.text, around=matched)))

    if score >= _HIGH_THRESHOLD:
        risk_level = RiskLevel.HIGH
    elif score >= _MEDIUM_THRESHOLD:
        risk_level = RiskLevel.MEDIUM
    else:
        risk_level = RiskLevel.LOW
        reasons.append("All required security evidence is present for this vendor.")
        source = contracts[0] if contracts else documents[0]
        citations.append(Citation(document_id=source.document_id, snippet=_excerpt(source.text)))

    # Stable, de-duplicated citations (several missing controls cite one policy).
    seen: set[tuple[str, str]] = set()
    unique_citations = []
    for citation in citations:
        key = (citation.document_id, citation.snippet)
        if key not in seen:
            seen.add(key)
            unique_citations.append(citation)

    return RiskAssessment(
        risk_level=risk_level,
        reasons=reasons,
        citations=unique_citations,
        missing_evidence=missing_evidence,
    )


evaluateRisk = evaluate_risk
