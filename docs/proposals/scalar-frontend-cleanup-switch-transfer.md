# Scalar frontend cleanup-contained switch transfers

**Status: unapproved implementation proof strategy.** Statement-switch
transfers and finite cleanup are already
[source contracts](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization).
No grammar or source-semantics decision is pending here.

## Proof boundary

The bounded scalar frontend represents cross-arm transfers using a private
arm-ordinal dispatcher loop. Recommend a structural finite-progress proof in
the shared hosted verifier, not a frontend exemption from cleanup checking.
Use the [composition inventory](../implementation/bootstrap-frontend-composition.md)
for current support.

Source-audited transfers establish the intended switch, but reconstructed
bound trees must independently satisfy the dispatcher shape:

- A private ordinal state, complete arm selection and only recognized
  adjacent constant-ordinal assignments followed by the matching dispatcher
  continue establish edges. A marker or variable name is eligibility
  information, never a trusted termination certificate.
- Account for ordinary completion, every possible entry arm, nested
  dispatchers and cleanup, break/continue depth and all transfer paths.
  An admitted acyclic graph reaches a finite exit from every possible entry.
- Preserve initialization, vacancy/publication, `NoThrow`, ownership,
  exceptional cleanup and scope checks. Cross-arm transfer cannot escape
  its cleanup action or bypass required cleanup.
- Charge analysis to the existing verifier budget. A frontend-generated
  structure is not a reason to omit consumer verification.

The proof concerns the general dispatcher/flow facts, not exact sample names
or a whitelist of successful switch bodies.

## Bounded counted loops

Inside a proved dispatcher, a pre-tested counted `for` may establish finite
progress when:

1. A mutable local `int32` has a constant in-range initializer and bound.
   Initialization can declare the local or assign an existing one.
2. The condition is strict `counter < bound` with exact `counter + 1`, or
   strict `counter > bound` with exact `counter - 1`.
3. No body path writes, updates or aliases the counter, including through
   nested cleanup, output arguments or calls.
4. Every local continue reaches the proved iterator. Recognize both the
   appended-iterator and separate-iterator lowering shapes; reject an
   appended-iterator bypass.
5. All other body effects/exits pass the ordinary cleanup verifier.

A false initial condition is a zero-iteration exit. Strict bounds and a unit
step keep each update representable, including signed extrema. Prove progress
mathematically; do not subtract endpoints in `int32`, enumerate iterations or
execute a multi-billion-step extreme case to establish termination. Only
the proved iterator arithmetic gains the non-overflowing `NoThrow` fact;
that fact does not authorize unrelated body arithmetic.

This is a bounded proof-recognition strategy, not a restriction on accepted
source `for` syntax. Unknown starts, non-unit/data-dependent steps, inclusive
bounds, body mutation, other unproved loops and cleanup escapes remain
unsupported by this proof.

## Alternatives and remaining decisions

The minimal strategy proves acyclic arm graphs and the bounded counted form.
A general control-flow/ranking analysis could admit cycles and data-dependent
iteration, but needs a sound progress argument with valid scope and transfer
semantics. Runtime termination of examples is not such an argument. Recommend
the bounded strategy without weakening rejection of unproved paths.

This does not add ordinary parser support, standard TRRM dispatcher records
or new language constructs. If dispatcher IR later crosses another persistence
surface, retain and independently verify its complete structure there.

## Acceptance gates

- Multi-arm, nested and zero-iteration cases; ordinary completion and every
  transfer/entry shape; finite cleanup during normal return and checked unwind.
- Both iterator layouts, local and nested continue routing, nested cleanup and
  assignment/declaration initializers. Verify extrema structurally.
- Forged ordinal targets, missing adjacency, exposed/aliased state, cycles,
  invalid depth, body counter writes/aliases, iterator bypass and unknown or
  overflowing arithmetic. Reject before native emission.
- Preserve effects, initialization, cleanup and budget failures under both
  runtime policies; parser/source audit success alone is insufficient.

Evidence entry points:
[verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.SwitchDispatch.cs),
[counted-loop regressions](../../tests/Terrum.CodeAnalysis.Tests/ManagedScannerComponentTests.CleanupCountedFor.cs),
[switch regressions](../../tests/Terrum.CodeAnalysis.Tests/ManagedScannerComponentTests.CleanupSwitchTransfers.cs)
and [CLI integration](../../tests/Terrum.CodeAnalysis.Tests/CliTests.FrontendCleanupSwitchTransfers.cs).
These establish only the behavior actually exercised, not the general ranking
alternative or adoption of this strategy.
