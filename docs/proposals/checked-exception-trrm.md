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

Should a new, explicitly identified TRRM profile admit checked-exception
Bodies, and what serialized contract establishes their exceptional control
flow, state and cleanup?

**Summary:** The accepted source semantics include handler selection after
inner cleanup, normal and exceptional `finally` composition, propagation of
unhandled failures, and preservation of pending failure/cause state. Hosted
binding, verification and native emission support a bounded subset, but TRRM
(1, 0) admits only fail-fast execution. Its Effect metadata and the scalar
frontend's hosted projection do not encode or validate exceptional Body edges.

**Recommendation:** Keep TRRM (1, 0) fail-fast and keep current rejection of
`checked-exceptions`; a runtime-policy flag alone is insufficient. Before
implementation, assign a separate required feature/profile and specify its
exceptional-edge contract, including failure-reason transport, handler input
state, cleanup ordering and the NoThrow cleanup restriction. Prefer serialized
exception regions with audited operation-site exceptional-state mappings over
synthetic normal CFG successors, unless an alternative is shown to preserve
the hosted rules and can be independently verified without producer source.

### Recommended first profile (unapproved)

Admit only root-free scalar values, module-local direct calls, nested bare
catches and a `finally` body that is NoThrow and normally completing. Keep
this cleanup restriction as an implementation-profile boundary, not a change
to the accepted `finally` contract. A failed operation has no result on its
exceptional edge; completed preceding effects remain. Inner cleanup completes
before handler entry. An attached `finally` runs exactly once on normal exits
and escaping failures, and a return value is evaluated before cleanup.
`DoesNotReturn` removes normal continuation only; it does not erase exceptional
propagation. Catching and recovery retain their accepted explicit-`unsafe`
authorization requirement; `finally` cleanup does not acquire it.

The profile must explicitly identify which checked failure reasons can be
transported and caught. It must not treat arbitrary Windows faults as Terrum
checked failures or rely on Windows first-pass handler search where it would
make selection observable before required cleanup. Audit every Body, including
uncalled methods and inactive paths, before execution or output replacement.
Preserve the current staged artifact-write behavior.

Exclude typed or multiple catches, filters, explicit throw/rethrow/capture,
faulting cleanup and causal-chain transport, `defer`, output parameters,
ownership, loans, GC roots, imported calls, cross-module linking and native
DLL exports. Reject unsupported forms rather than weakening their source
semantics. This profile does not expand the scalar frontend beyond its hosted
verifier/emitter boundary.

### Required acceptance evidence

- Independent-byte fixtures round-trip through a producer and load without
  producer source; malformed, missing, overlapping, foreign and mismatched
  exception records or operation-state mappings reject.
- AOT and JIT execution match hosted behavior for normal completion, caught
  failure, nested cleanup order, retained pre-cleanup return values,
  `DoesNotReturn` propagation and escaping failures. Failures expose no result
  and do not erase effects completed before the failing operation.
- Uncalled Bodies and inactive paths receive the same audit as executed code;
  late invalid Bodies leave existing artifacts unchanged.
- Policy and required-feature mismatches reject before execution. The current
  TRRM (1, 0) rejection path remains unchanged, and no command falls back to
  success-shaped behavior after unsupported input.
