# AI usage

This exercise was built with AI assistance. This document records where that help was real,
where it was wrong, and which decisions were not delegated.

## Tools used

- **Claude Code (Opus 5)** — planning, scaffolding the `orchestrator` package, the test suite,
  and first drafts of the written notes.

## Prompts that were actually useful

- Pasting the brief verbatim and asking for a **plan before code** — file layout, function
  signatures, fixture data, risk rules, test list — then reviewing that plan rather than
  reviewing a finished implementation. Reviewing a plan is cheap; reviewing 400 lines of
  plausible-looking code is not.
- Asking specifically *"where is the trust boundary, and can evidence text reach the decision?"*
  This produced the structured-flag design — risk reads `has_*` booleans, prose is only quoted
  and scanned — rather than the regex-over-prose approach that a determined injection could game.
- Asking for the **failure cases** the tests should assert, before writing any tests:
  cross-tenant approver, self-approval, an unauthorized role holding a valid approval, and
  injection placed in the *question* rather than in the evidence.
- Asking it to justify a dependency — *"Pydantic or stdlib here, and why?"* — instead of
  accepting the default. The reasoning is recorded in `PRODUCTION_NOTES.md`: stdlib is
  right-sized for one flat output object, and Pydantic earns its place at an API edge parsing
  untrusted inbound JSON, which this exercise does not have.

## What the AI got wrong, and had to be corrected

- **A contradictory output state.** The first design had medium risk emit `requiresApproval:
  true` as an "advisory" while also returning `actionStatus: "executed"`. In a compliance record
  that reads as a bug — the same document asserts that approval was required and that the action
  ran without one. Corrected so `requiresApproval` describes the gate truthfully: only high risk
  sets it, and `WorkflowResult.validate()` now refuses to emit the contradictory pair at all.
- **Regex-based injection defence presented as sufficient.** Detecting *"ignore previous
  instructions"* in prose is a signal, not a control — it fails against any rephrasing. The fix
  was structural: make prose incapable of reaching the score, and demote detection to evidence
  that can only ever raise risk.

Both corrections point the same way. The model is willing to produce a design that looks
defensible and contains a contradiction, and to describe a pattern match as a security control.
Neither error is visible without asking what the system must never do.

## What was not delegated

The judgment calls are the substance of the exercise:

- what counts as a trust boundary — and therefore what the four rows of the README table are;
- that **approval does not lower risk**: an approved high-risk vendor still reports `"high"`
  with every reason intact, because the record should show that a human accepted a documented
  risk rather than that the risk disappeared;
- that `execute_mock_action` re-checks authorization and the gate even though `run_workflow`
  already did, so the effect is not reachable by a future caller that forgets;
- the rule weights and the `0 / 1–2 / ≥3` thresholds — arbitrary in that any small integer
  scheme would serve, deliberate in that only **high** gates the action.

## The .NET port

The Python implementation was later converted into this ASP.NET Core service, again with
Claude Code. The pattern that worked the first time worked again: plan first, review the plan,
then let it write the code.

**Useful.** Mechanical translation is exactly the shape of task AI is good at — enums, records,
DI registration, and turning six pytest files into six xUnit files. Asking *"what does putting
this behind HTTP change that the library never had to answer?"* produced the three real
issues — caller-asserted identity, concurrent access to process-global state, and exceptions
becoming status codes — before any code was written, which is what made them design decisions
rather than bugs found later.

**What needed correcting.** Two defects were the kind a transcription introduces silently:

- **Record equality over collections.** C# records compare `IReadOnlyList` members by
  *reference*, where Python dataclasses compare by value. The ported determinism test would
  have failed against a perfectly correct implementation — or, worse, been "fixed" by weakening
  the assertion. `RiskAssessment` needed explicit structural equality.
- **Unknown fields silently dropped.** System.Text.Json ignores unrecognised properties by
  default. A literal port would have deleted the control that stops a caller sending
  `role: "approver"` or `forceApprove: true` — the test would have gone green while the
  boundary was gone.

Both are cases where the target language's default behaviour differs from the source's, and
neither shows up as a compile error. The lesson is narrower than last time but the same in
kind: the risk in a port is not the code that fails to compile, it is the control that
quietly stops being enforced.

**Not delegated.** That identity moves to headers rather than staying in the body; that a
blocked action is `200` rather than `403`; that the unknown-tenant `403` must not echo the
tenant id; and that the latent excerpt-length quirk was preserved and documented rather than
silently "fixed". Each is a judgment about what the system should mean, not what it should do.

## The UI

A Blazor component library and review console were added on top of the API, again with Claude
Code, again plan-first.

**Useful.** Component scaffolding, the DI wiring for a typed client with a `DelegatingHandler`,
and turning a component inventory into a bUnit suite are all well-trodden ground. The question
worth asking up front was *"what does putting a browser in front of this change?"*, and it
produced the two real answers before any code existed: untrusted vendor prose now reaches a
renderer, and a role dropdown is a privilege-escalation control. Both became design constraints
rather than review findings.

**What needed correcting.** The first version of the escaping test asserted that the rendered
**markup string** did not contain `onerror`. That test failed — against components that were
behaving perfectly. Correctly escaped output still contains those characters, as the inert text
`&lt;img src=x onerror=…&gt;`. The failure was in the assertion, not the component, and the
tempting "fix" was to strip the payload in `CitationList` so the string check would pass — which
would have destroyed the demonstration and replaced a real control with a cosmetic one. The
correct assertion queries the parsed DOM: did an element get built, did a handler get bound.

That is the most instructive error in this whole record, because a green test would have been
available in either direction. It is worth stating plainly: a test that fails against correct
code invites you to break the code.

**Not delegated.** That the UI references neither `Orchestrator.Core` nor `Orchestrator.Api`,
so it cannot reach past the API to the fixtures; that the injected addendum is rendered rather
than filtered, because hiding it would hide the demonstration; that a blocked action is a full
result and not an error state; that the `403` keeps the API's wording so the UI does not rebuild
the tenant-enumeration oracle at the last step; and that the identity picker carries its own
"not authentication" warning on screen rather than relying on a notes file nobody opens.

## Overall assessment

AI was fastest on the parts with a known shape: package scaffolding, turning a list of failure
modes into a test suite, and getting a first draft of the documentation onto the page. It was
least trustworthy exactly where this problem is interesting — the output contract and the
strength of a control. Keeping it on breadth (what could fail, what should be tested, what a
production version would need) while keeping the decisions about what the system must never do
under human control was the pattern that worked.
