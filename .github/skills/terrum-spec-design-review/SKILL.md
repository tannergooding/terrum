---
name: terrum-spec-design-review
description: Design, review, consolidate, or integrate Terrum specification proposals. Use for intentional changes to accepted source, runtime, library, target, or metadata contracts, including instruction reservations and numeric assignments; do not assume pre-v1 immutability or draft compatibility.
---

# Terrum specification design review

Follow `.github\copilot-instructions.md`. Use this workflow for choosing or
revising contracts, not as permission to implement or approve adjacent designs.
The accepted specification is the current baseline, not a frozen destination.

## Establish the decision and document owners

1. Read the original request, explicit decisions, relevant proposal, and the
   chapters reached through `docs\specs\README.md`. Identify which accepted
   text the proposal intentionally replaces and which rules it preserves.
2. Keep approval, implementation, and stability separate. Use
   `docs\implementation.md`, actual code, and active tests for support claims;
   a prototype does not approve a design, and missing support does not
   invalidate an accepted target contract.
3. Distinguish an accidental contradiction, an intentional amendment, a
   duplicate, an implementation gap, and a genuinely undecided contract.
   Do not report a proposed replacement as defective solely because the
   accepted spec currently says something else.
4. Preserve explicit user decisions. If a recommendation would revisit one,
   identify it and explain why; do not silently reopen it.

## Design the integrated result

- Prefer the simplest coherent design, not the smallest textual patch.
  Compare a minimal amendment with substantive revision of the affected area
  when the distinction matters. Recommend one and explain the tradeoff.
- Concrete proposed syntax, signatures, records, and numeric candidates are
  appropriate design outputs. Mark them unapproved and complete their
  validity, ownership, failure, and consumer obligations rather than hiding
  missing semantics behind an example or an implementation limit.
- Check interacting proposals together. Reuse one owner for shared semantics;
  consolidate duplicate recommendations while preserving distinct alternatives,
  dependencies, and useful rationale. Put implementation inventories and
  sequencing in their own documents, not in proposal chronology.
- For binary changes, check the whole allocation structure: namespaces,
  semantic families, operation/terminator bands, prefixes/escapes, operand
  schemas, width limits, capacity, and canonical writers. A currently occupied
  draft ID can be reassigned; an unused ID is not necessarily the best design.
  Do not impose additive-only changes or retain retired numbers as aliases.
- Follow `docs\specs\representation\format-versioning.md`: unpublished drafts
  have no automatic compatibility obligation, but exchange still selects an
  exact schema. Do not guess a decoder from equal version bytes or plausible
  operands. Released-schema stability is a separate scoped rule.
- Preserve generic compiler/library boundaries and required consumer checking.
  Mutable design is not permission for sample recognizers, implicit marshalling,
  trusted serialized authorization, or success-shaped loss of obligations.

## Prepare an integration-ready proposal

Make the following discoverable without adding empty template sections:

- The problem, recommendation, status, scope, prerequisites, and remaining
  decisions.
- Exact spec owners and the text, productions, records, or assignments to add,
  replace, remove, or reorganize. State intended supersession explicitly.
- Cross-layer consequences for source, runtime, libraries, targets, interchange,
  binding/replacement, verification, and lowering where affected.
- Positive, negative, boundary, and cross-feature conformance cases. For
  re-encoding, account for indexes, references, maps, and source data rather
  than proposing byte substitution.
- Current implementation gaps, with accepted-versus-proposed obligations and
  dependencies, in an explicitly requested maintained inventory such as
  `docs\implementation\conformance-gaps.md`; keep temporary tracking in
  session state.

Keep unapproved replacements in the relevant proposal. Incorporate only
already-approved decisions into normative owners unless the user explicitly
authorizes a different review workflow. Updating documents does not itself
approve syntax, allocate an ID, migrate a compiler, or stabilize the language.

## Check and hand off

Review the combined design, not just each proposal's local consistency.
Check final links, status boundaries, allocation interactions, and whether
another session could implement the recommendation without inventing policy.
Use `terrum-example-writing` for snippets and `terrum-native-qualification`
only for claims requiring compiler/native evidence.

Keep documentation-only checks proportional: use existing documentation
checks and source evidence, not unrelated builds or interactive execution.
When independent review is requested, give `terrum-contract-review` the task
kind, original concerns, intended amendments, exact snapshot, and evidence.
Report undecided choices and implementation gaps without calling them accepted
or describing the documentation pass as compiler conformance.
