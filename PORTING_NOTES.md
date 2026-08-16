# Porting notes: Python library → .NET REST service

What changed moving from `run_workflow(dict) -> dict` to an HTTP service, and why. The
behaviour of the engine is unchanged; everything below is either a consequence of putting it
behind a network edge, or a language difference that would have silently altered behaviour if
transcribed literally.

## What did not change

The rule table, the `0 / 1–2 / ≥3` thresholds, the injection patterns, the action allow-list,
the fixture documents (including the injected addendum, character for character), and every
recommendation and reason string. The four scenarios produce byte-identical JSON to the Python
implementation, down to the audit event ids.

## Structural: Core / Api split

`Orchestrator.Core` has no ASP.NET reference. The four trust boundaries are domain properties
and must be provable without a web server — that is what keeps the 34 ported tests a faithful
equivalent of the pytest suite rather than a rewrite that happens to also exercise HTTP. The
API project holds only edge concerns: identity, serialization, status codes.

## Identity moved from the body to headers

The single most consequential change.

In Python, `tenantId`, `userId` and `role` were function arguments supplied by trusted
in-process code. Over HTTP they arrive from the caller, and a request body field named `role`
would mean anyone can POST `"role": "approver"` and walk through the authorization boundary.
`PRODUCTION_NOTES.md` had already named this: *"the client never asserts its own role"* and
*"`tenant_id` derived from the authenticated session, never from the request body."*

So identity is resolved by `CallerContextMiddleware` from `X-Tenant-Id` / `X-User-Id` /
`X-Role` before any endpoint runs, and `RunWorkflowRequest` does not declare those fields at
all. Because the contract is marked `JsonUnmappedMemberHandling.Disallow`, sending them is a
`400` rather than a silent no-op.

`approvedBy` stayed in the body. It is not the caller's identity but an artefact presented
about a third party, so the body is the right place for it; it is verified against the
tenant's approver registry and against the header-derived user id to refuse self-approval.

The middleware deliberately does **not** validate the tenant. Tenant filtering stays a single
choke point in the evidence store — duplicating it at the edge would create a second place to
forget it, and a second place for the two to disagree.

## Concurrency, which the original never faced

`_AUDIT_LOG`, `_event_counter` and `VENDOR_APPROVAL_STATUS` were process-global mutables, safe
only because pytest and `demo.py` are single-threaded. ASP.NET Core serves requests in
parallel, so:

- `InMemoryAuditLog` takes a lock for both append and read-snapshot, keeping event ids
  gap-free and monotonic and preventing a reader from observing a torn list.
- `InMemoryVendorStateStore` uses a `ConcurrentDictionary`.
- `auditEventIds` is collected in a local list inside `WorkflowEngine`, so concurrent runs
  never mix ids into each other's responses.

The last one is the mistake this refactor invites, so it is pinned by a test that fires 25
concurrent requests across two tenants and asserts no id is claimed twice.

The Python `reset_audit_log()` / `reset_vendor_status()` test helpers are **gone**, not ported.
Tests construct fresh instances instead, so production code has no method that can erase an
audit trail — and xUnit's parallel test-class execution stays safe.

## Two serialization details that carry real behaviour

**Property names are camelCase; enum values are snake_case.** `"riskLevel"` but
`"blocked_pending_approval"`. One naming policy cannot produce both, so
`PropertyNamingPolicy = CamelCase` is paired with
`JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)`.

**Unknown fields must be rejected, not ignored.** The Python `_normalise_request` raised on
unrecognised keys; System.Text.Json drops them by default, which would have quietly deleted
that control. `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` restores it.
Minimal APIs wrap the resulting `JsonException` in a `BadHttpRequestException`, so the
exception handler unwraps the inner exception to produce a useful `400`.

## Language differences that would have changed behaviour

**Record equality over collections.** `test_risk_evaluation_is_pure_and_deterministic` asserts
`evaluate_risk(docs) == evaluate_risk(docs)`. Python dataclasses compare lists by value; C#
records compare `IReadOnlyList` members by *reference*, so the ported test would have failed
against a correct implementation. `RiskAssessment` therefore implements structural equality
explicitly.

**Reflection over the control table.** Python's `getattr(d, flag)` became a
`Func<Document, bool>` selector on each `Control` record — the same table, statically checked.

**Exceptions.** `UnknownTenantError(ValueError)` → `UnknownTenantException`;
`OutputContractError(AssertionError)` → `OutputContractException`. The latter matters: an
`AssertionError` can be compiled away with `python -O`, whereas the C# exception cannot be
disabled. A safety invariant that evaporates under an optimisation flag is not an invariant.

**Snippet length.** `SnippetMaxChars = 240` counts UTF-16 units in C# versus code points in
Python. Identical for the ASCII fixture corpus; documented at the constant.

## A latent quirk preserved, not fixed

`Excerpt(text, around:)` prepends `"..."` to a window of up to `SnippetMaxChars`, so it can in
principle return 243 characters — three over the cap that `WorkflowResult.Validate` enforces.
It does not for the current corpus (the window hits end-of-text at 200 chars), and the
validator would reject it loudly rather than leak an oversized snippet if a longer document
ever changed that.

This was left as-is rather than silently corrected: changing the truncation would change
citation text, and the behaviour is bounded by a control that already exists. It is called out
at the method and worth fixing deliberately if the corpus grows.

## Status codes

A blocked action is `200`, not `4xx` — see the README for the reasoning. Errors are reserved
for requests the service refused to *evaluate*. `UnknownTenantException` maps to `403` without
echoing the tenant id, since fail-closed that names the rejected tenant is a tenant-enumeration
oracle. `OutputContractException` maps to an opaque `500` and logs critical.

## Test mapping

| Python file | C# file | Count |
|---|---|---|
| `test_tenant_isolation.py` | `TenantIsolationTests.cs` | 5 |
| `test_authorization.py` | `AuthorizationTests.cs` | 4 |
| `test_approval_gate.py` | `ApprovalGateTests.cs` | 8 |
| `test_prompt_injection.py` | `PromptInjectionTests.cs` | 6 |
| `test_audit.py` | `AuditTests.cs` | 6 |
| `test_output_contract.py` | `OutputContractTests.cs` | 5 |

Two tests changed shape because their subject moved:

- `test_unknown_role_is_rejected_at_the_input_boundary` asserted on a role *string*. By the
  time the engine sees a role it is a parsed enum, so the string rejection moved to the edge
  and is proven over HTTP in `IdentityBoundaryTests`. The domain test now pins that no invalid
  enum value exists.
- `test_unknown_request_field_is_rejected` likewise tested JSON-shaped input that the domain no
  longer parses; it moved to `IdentityBoundaryTests.Unknown_body_field_is_rejected`. Its slot
  in `OutputContractTests` is taken by a snippet-length test that the Python suite lacked.

`demo.py`'s four scenarios survive as the README `curl` examples and as
`WorkflowEndpointTests`.

The 24 API tests cover ground the library version had no equivalent for: header authority and
body-based privilege escalation, missing/invalid identity, tenant-scoped audit reads, the
concurrency properties above, and that an invariant breach surfaces as an opaque `500`.
