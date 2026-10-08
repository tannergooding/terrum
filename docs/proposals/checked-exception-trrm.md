# Checked-exception TRRM proposal

**Status: accepted 1.0 GA requirement; serialization and conformance details
remain open. The current beta executable subset is fail-fast-only and is not
the final TRRM 1.0 contract.**

## Current boundary

The current TRRM (1, 0) beta executable subset is fail-fast only. The
`terrumc` TRRM commands reject `checked-exceptions` because executable Bodies
do not yet have an exceptional-edge contract. This is an incomplete beta
implementation, not a final compatibility boundary: core language exception
semantics must be representable and executable in TRRM 1.0 before GA. The
hosted compiler's bounded checked `try`/`catch` subset and the scalar
frontend's bounded bare catch with optional `finally` are implementation
evidence, not a complete source-free contract.

## Accepted requirement and remaining design

TRRM 1.0 must support the core checked-exception semantics required by the
language before GA. This is not an optional post-1.0 feature; whether beta
artifacts use a new required-feature marker or another explicit identification
mechanism remains open, but no such marker may make core support optional at
GA. The serialized contract must establish exceptional control flow, state
and cleanup without relying on producer source.

**Summary:** The accepted source semantics include handler selection after
inner cleanup, normal and exceptional `finally` composition, propagation of
unhandled failures, and preservation of pending failure/cause state. Hosted
binding, verification and native emission support a bounded subset, but the
current TRRM beta executable subset supports only fail-fast execution. Its
Effect metadata and the scalar frontend's hosted projection do not encode or
validate exceptional Body edges.

**Recommendation:** Treat checked-exception TRRM support as required 1.0 GA
work, not as a reason to preserve the current beta rejection. A runtime-policy
flag alone is insufficient. Specify failure-reason transport, handler input
state, cleanup ordering and the `NoThrow` cleanup restriction before claiming
support. Prefer serialized exception regions with audited operation-site
exceptional-state mappings over synthetic normal CFG successors unless an
alternative preserves hosted semantics and is independently verifiable
without producer source.

### Recommended first profile (unapproved)

This is a possible implementation gate, not the complete GA contract. Its
exclusions must be checked against the core 1.0 language feature set; no core
exception semantics may remain unsupported at GA merely because they are
outside this initial profile.

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
  beta rejection may remain until the beta format and implementation are
  updated; GA TRRM 1.0 must not reject core checked-exception programs or fall
  back to success-shaped behavior after unsupported input.
