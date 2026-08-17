# Production notes

What this exercise deliberately fakes, and what it would take to make each part real.

Now that the engine is an HTTP service rather than a library, several of these stopped being
hypothetical — the rate limiting, idempotency and validation sections below describe work
against endpoints that actually exist. Each is flagged where that changes the advice.

## Auth and identity
- **Now actionable.** `CallerContextMiddleware` resolves the principal from `X-Tenant-Id` /
  `X-User-Id` / `X-Role`. That is the correct *shape* — identity resolved once, at the edge,
  never read from a body — with the verification step missing. Replace the header read with a
  validated OIDC/JWT principal (claims checked against the IdP's JWKS) and the rest of the
  pipeline is unchanged. See risk 4 in `THREAT_NOTES.md`.
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
- `tenantId` derived from the authenticated session, never from the request body. **Done** —
  the request contract cannot carry it, and `GET /api/v1/audit` has no tenant parameter.
- Per-tenant encryption keys; scope caches, retrieval indexes and audit queries per tenant. A
  shared vector index is the usual leak.
- Keep the output-side citation validation as defence in depth — it is cheap and it catches
  regressions elsewhere. `InvariantBreachTests.cs` proves it still fires.

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
  **Now actionable**: add OpenTelemetry to `Program.cs` and correlate on the audit event ids
  already returned in every response.
- Metrics that matter operationally: block rate, approval latency, high-risk volume per tenant,
  rate of injection-pattern detections (a spike is an incident, not noise).
- Alert on invariant violations from `WorkflowResult.Validate()` — those are should-never-happen
  conditions and deserve a page, not a log line. The handler already logs these at Critical and
  returns an opaque `500`; wire that log level to a pager.

## Retries and idempotency
**Now actionable** — `POST /api/v1/workflow/run` is retryable by any HTTP client, so this is no
longer theoretical.
- Idempotency key per action attempt (an `Idempotency-Key` header), stored with the result, so a
  retried request returns the original outcome rather than executing twice.
- Retries only on the read path (evidence retrieval); the effect path must be exactly-once from
  the caller's perspective.
- Persist the gate decision before performing the effect, so a crash between the two is
  recoverable and visible.

## Rate limiting
**Now actionable** — there is a listening port and no limiter on it. ASP.NET Core's built-in
`AddRateLimiter` partitioned by tenant would cover most of this.
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
The original note here said hand-written validation was right-sized *because there was no API
edge parsing untrusted inbound JSON*, and that a schema library would earn its place once there
was. That condition no longer holds, so what the edge does now:

- **Inbound**: System.Text.Json with `JsonUnmappedMemberHandling.Disallow` on the request
  contract, so unknown fields are a `400` rather than a silent drop. This is the control that
  stops a caller smuggling `role` or `forceApprove` past the boundary, and it is enforced by the
  serializer rather than by hand-written checks.
- **Outbound**: `WorkflowResult.Validate()` stays hand-written. It asserts *safety invariants*
  ("high risk must require approval", "no execution without a recorded approval", "no citation
  outside the tenant's documents"), not shapes — a schema library would not express those, and
  they are the part worth keeping.
- **Published schema**: the OpenAPI document at `/openapi/v1.json` is generated from the
  contracts, so callers break loudly rather than silently. Serve it in all environments, not
  just Development, and version the path once external callers exist.
- The rule table in `RiskEvaluator` should become versioned data, with the version recorded in
  the audit event — so "why did this pass in March?" is answerable.
