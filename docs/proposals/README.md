# Terrum specification proposals

The [source grammar](../grammar-spec.md), [metadata design](../il-metadata.md)
and bounded [coded instruction set](../il-instruction-set.md) are approved
target designs, not claims of implementation or released formats. The
proposal documents in this directory contain unapproved choices. The source
grammar records its own open choices and scope boundaries. An **accepted
direction** settles a semantic
boundary only; adjacent grammar and APIs remain open unless explicitly
approved.

- [Core-library proposal](core-library-reference.md) records candidate
  declarations, dependency layers and unresolved API contracts.
- [Static-proof and constant-evaluation proposal](static-proof-and-constant-evaluation.md)
  collects candidate minimum proof rules, effects and evaluator budgets;
  the portable-floor policy itself is accepted.
- [Semantic IL extensions](il-extensions.md) collect the unapproved inline
  built-in type word and compact-profile follow-up, separate from the
  approved `(1, 0)` designs.
- [Self-host frontend verification budget](self-host-frontend-verification-budget.md)
  records a deferred, unapproved architecture option if memoization no longer
  suffices to grow the authored scalar frontend within its shared limit.
- [Reference-call metadata](reference-call-metadata.md) records the proposed
  first dependency and caller-safety metadata slice for general cross-call
  references; it does not change the accepted source contracts.
- [Span-backed token cursor](span-backed-token-cursor.md) records the open
  backing, capture, indexing and construction contracts needed for the
  existing zero-copy self-host cursor candidate.
- [Checked-exception TRRM](checked-exception-trrm.md) records the unapproved
  exceptional-control-flow contract needed before source-free checked-exception
  Bodies can be considered.
- [Static initialization metadata and attribute schema](static-initialization-metadata.md)
  recommends how to persist the accepted static-storage contract without
  changing its initialization or failure semantics.

Use these proposals for candidate APIs, source choices and format extensions,
not as a second policy or implementation checklist. Settled call, output,
cleanup and proof contracts are in [language contracts](../language-contracts.md);
other accepted direction lives in the relevant [language](../language.md),
[lifetime](../lifetimes.md), [conversion](../conversions.md),
[layout](../data-layout-and-abi.md), [type-family](../type-families.md),
[memory](../memory-model.md) and [library](../libraries.md) specifications.
Current support and sequencing are in the [implementation reference](../implementation.md)
and [roadmap](../roadmap.md); candidate compiler components are in
[self-host](../../self-host/README.md).
