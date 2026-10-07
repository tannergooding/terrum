# Compare-exchange order-pair proposal

**Status: unapproved.**

## Summary

The accepted memory model treats a failed compare-exchange as a read, not a
release, and requires both success and failure orderings. Failure ordering
cannot be `Release` or `AcquireRelease`. The complete legal order-pair table
remains open in the [memory model](../specs/memory-model.md) and
[core-library proposal](core-library-reference.md).

## Recommendation

Require a compile-time-known success/failure pair from the finite table below.
The failure order must be a load order and no stronger than the success order:

| Success order | Valid failure orders |
|---|---|
| `Relaxed` | `Relaxed` |
| `Acquire` | `Relaxed`, `Acquire` |
| `Release` | `Relaxed` |
| `AcquireRelease` | `Relaxed`, `Acquire` |
| `Sequential` | `Relaxed`, `Acquire`, `Sequential` |

This admits nine of the 25 enum-value pairs. Reject all others at compile
time; in particular, no failure order releases, and a failure cannot be
stronger than its success order. Keep strong/non-spurious CAS behavior
independent from ordering: choosing an order pair does not select a weak
operation or permit spurious failure.

Preserve the accepted default of strong sequentially consistent CAS for both
outcomes, the defined-bitwise comparison rule, and the rule that failed CAS
does not publish a value it did not store. Do not add runtime order dispatch,
managed-reference eligibility, or a target-specific fallback here.

## Required validation if adopted

Test every success/failure pair, accepting exactly the nine listed pairs.
For accepted pairs, verify the success and failure outcomes retain their
respective read/write effects and that failure cannot create a release
communication edge. Include selected litmus cases for acquire failure and
sequential failure. Keep the strong-CAS outcome and defined-representation
checks separate from order-pair validation.

The diagnostic identity for a nonconstant or invalid pair, source argument
spelling, target capability table, and implementation strategy remain open.
