# Checked-exception TRRM proposal

**Status: accepted 1.0 GA requirement; serialization and conformance details
remain open. The current beta executable subset is fail-fast-only and is not
the final TRRM 1.0 contract.**

## Current boundary

The current beta executable subset is fail-fast-only and rejects
`checked-exceptions`; this implementation boundary is tracked in the
[implementation reference](../implementation.md#executable-trrm-scalar-interchange).
It is not the GA contract. The candidate profile below also excludes output
parameters, so it cannot by itself satisfy both accepted TRRM 1.0 GA
requirements for exceptions and output modes.
The [joint output/failure/ownership proposal](trrm-output-failure-ownership.md)
owns that composition gate; this document retains the distinct candidate
first exception-execution slice.

## Remaining design

After approval, integrate the selected region, operation-site state, handler
and cleanup codecs into the instruction/metadata owners, registry and
conformance cases. Prefer the shared joint model over freezing this
root-free slice as an independent permanent exception system. Its physical
records may replace draft shapes; no old-reader compatibility is required.

The accepted language semantics and TRRM 1.0 GA requirement are recorded in
the [language contracts](../specs/language-contracts.md#33-normal-and-exceptional-exits)
and [instruction-set specification](../specs/il-instruction-set.md#executable-subset-versus-assigned-decode-shapes).
Still open are beta artifact identification (including whether a required
feature marker is needed) and a serialized, source-independent contract for
exceptional control flow, operation-site state, handler inputs and cleanup.

**Recommendation:** A runtime-policy flag alone is insufficient. Specify
failure-reason transport, handler input state, cleanup ordering and the
`NoThrow` cleanup restriction before claiming support. Prefer serialized
exception regions with audited operation-site exceptional-state mappings over
synthetic normal CFG successors unless an alternative preserves hosted
semantics and is independently verifiable without producer source.

### Recommended first profile (unapproved)

This is a possible implementation gate, not the complete GA contract. Its
exclusions must be checked against the core 1.0 language feature set; no core
exception semantics may remain unsupported at GA merely because they are
outside this initial profile.

Admit only root-free scalar values, module-local direct calls, nested bare
catches and a `finally` body that is NoThrow and normally completing. Keep
this cleanup restriction as an implementation-profile boundary, not a change
to the accepted source contract. See the [language contracts](../specs/language-contracts.md#33-normal-and-exceptional-exits)
for cleanup ordering, failure propagation, `DoesNotReturn`, and unsafe
authorization semantics.

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
