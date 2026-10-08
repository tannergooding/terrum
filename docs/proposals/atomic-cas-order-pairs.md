# Compare-exchange order-pair proposal

**Status: accepted contract: compare-exchange requires a compile-time-known
success/failure pair from the complete table in the
[memory model](../specs/memory-model.md#explicit-order-semantics). Diagnostic
identity, source enum/argument spelling, target capability details and
implementation strategy remain open.**

## Summary

The canonical order-pair table and its relationship to strong CAS, failed-read
semantics and bitwise comparison are in the
[memory model](../specs/memory-model.md#explicit-order-semantics). This
proposal specifies no atomic-operation support or additional API surface.

## Required conformance validation

When implemented, test all 25 enum-value pairs against the canonical table,
accepting exactly the nine listed there. Check success/failure read-write
effects and that failure cannot create a release communication edge; include
acquire-failure and sequential-failure litmus cases. Keep strong-CAS outcomes
and defined-representation checks separate from order-pair validation.

The diagnostic identity for a nonconstant or invalid pair, source enum and
argument spelling, target capability details, and implementation strategy
remain open.
