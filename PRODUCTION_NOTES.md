# Production notes

What this exercise deliberately fakes, and what it would take to make each part real.

## Auth and identity
- Replace the `role` string with an authenticated principal: OIDC/JWT from the IdP, verified
  server-side. The client never asserts its own role.
- RBAC on the action allow-list, sourced from the directory, not a dict in code.
- `approvedBy` becomes a **signed approval artefact**, not a caller-supplied string: approver
  identity from the IdP, a hash of the exact risk assessment it approves, timestamp, expiry.
  Verified out-of-band from the request that consumes it.
- Segregation of duties: no self-approval (already enforced), plus four-eyes on payment-data
  vendors; approvals expire and must be re-obtained when evidence changes.

## Tenant isolation
- Move the filter from application code to storage: row-level security, or per-tenant
  schemas/indexes. A forgotten `WHERE tenant_id = ?` should fail, not over-return.
- `tenant_id` derived from the authenticated session, never from the request body.
- Per-tenant encryption keys; scope caches, retrieval indexes and audit queries per tenant. A
  shared vector index is the usual leak.
- Keep the output-side citation validation as defence in depth — it is cheap and it catches
  regressions elsewhere.

## Audit storage
- Append-only and tamper-evident: WORM bucket or an append-only table with a per-tenant hash
  chain, so an event cannot be edited or removed without detection.
- Retention aligned to the regulatory clock (PCI-DSS: 12 months readily available), with legal
  hold support.
- Keep the current discipline of logging document **ids**, not document text — untrusted content
  should not propagate into records humans and tools read.
- Emit an event for the *decision inputs* too (evidence version ids, rule-set version), so a past
  decision can be reconstructed after the rules change.

## Observability
- Structured logs and one trace per workflow run, spanning retrieve → assess → gate → act.
- Metrics that matter operationally: block rate, approval latency, high-risk volume per tenant,
  rate of injection-pattern detections (a spike is an incident, not noise).
- Alert on invariant violations from `WorkflowResult.validate()` — those are should-never-happen
  conditions and deserve a page, not a log line.

## Retries and idempotency
- Idempotency key per action attempt, stored with the result, so a retried request returns the
  original outcome rather than executing twice.
- Retries only on the read path (evidence retrieval); the effect path must be exactly-once from
  the caller's perspective.
- Persist the gate decision before performing the effect, so a crash between the two is
  recoverable and visible.

## Rate limiting
- Per-tenant and per-user quotas on workflow invocations, with tighter limits on action-bearing
  requests than advisory questions.
- Throttle repeated approval attempts against a single vendor — that pattern is approval
  brute-forcing, and it should alert as well as throttle.

## Legal and compliance
- PCI-DSS scope: this decides who may touch cardholder data, so decision records are themselves
  in scope for audit.
- Evidence lifecycle: SOC 2 reports and DPAs expire; store issue/expiry dates and re-run
  assessments on expiry rather than trusting a stale attestation.
- Records retention and legal hold on the audit log; data residency per tenant.
- Automated decisions affecting a counterparty need an explanation the reviewer can defend —
  which is why reasons and citations are part of the contract, not a debug field.

## Structure and validation
- The stdlib validation here is right-sized for one flat output object. At an API edge parsing
  untrusted inbound JSON, switch to Pydantic or a JSON Schema on both request and response, and
  publish the schema so callers break loudly rather than silently.
- The rule table in `risk.py` should become versioned data, with the version recorded in the
  audit event — so "why did this pass in March?" is answerable.
