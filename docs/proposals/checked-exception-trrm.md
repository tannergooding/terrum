# Checked-exception TRRM proposal

**Status: unapproved design question.** This records a boundary for future
source-free checked-exception execution; it does not change the TRRM format,
runtime policy, or language semantics.

## Current boundary

TRRM (1, 0) is fail-fast only. The `terrumc` TRRM commands reject
`checked-exceptions` because executable Bodies have no exceptional-edge
contract. The hosted compiler can bind, verify and emit its bounded checked
`try`/`catch` subset, and the Terrum-authored scalar frontend now projects a
bounded bare catch with optional `finally`. Neither fact establishes that a
source-free TRRM artifact preserves that behavior.

## Decision

Should a future TRRM revision admit checked-exception Bodies, and what
executable representation should carry their exceptional control flow?

**Recommendation:** keep TRRM (1, 0) fail-fast and do not treat a runtime-policy
flag as sufficient. Before implementation, specify how the source-free Body
represents the existing hosted rules: handler selection after inner cleanup,
normal and exceptional `finally` composition, propagation of unhandled
failures, and the verifier's NoThrow cleanup requirement. Compare explicit
exceptional CFG successors with a serialized exception-region table against
the hosted verifier and emitter; prefer explicit successors only if they
preserve those rules and can be audited without producer source.

Until that contract is reviewed, retain the current rejection path and keep
native checked-exception support distinct from source-free TRRM support.
