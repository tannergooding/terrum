# Optional scalar TRRM execution boundary

**Status: unapproved implementation-boundary proposal.** This records a
bounded path for executing the optional scalar forms already described by the
approved Body contract. It does not add source-language scalar optional
support or change optional semantics.

## Current boundary

The approved instruction set assigns optional `TypeSpec` forms and the
`MakeOptional`, `None`, `IsPresent`, `Copy` and eager `Select` operations.
Compact Body readers structurally validate a bounded optional scalar subset.
The hosted `PrimitiveType` model and source binder do not represent scalar
optionals, and the native scalar ABI explicitly excludes them. The source-free
`TrRmExecutableCompiler` consequently rejects optional TypeSpecs rather than
claiming execution from structural validation alone.

## Decision

Should source-free TRRM execution implement optional scalar values inside
method Bodies while keeping them out of callable signatures?

**Summary:** The approved Body subset assigns optional scalar values and
operations, but executable `Call` signatures are restricted to primitive
scalar parameters and primitive-scalar or `void` results. Source binding and
the native ABI also do not support scalar optional types. Therefore optional
values can be implemented as Body-local values without defining optional
parameters or results.

**Recommendation:** Implement the assigned optional scalar operations for
Body-local values, with presence represented independently from payload bits.
Keep MethodSig, Param and Call signatures within their existing primitive
scalar/`void` subset; do not add optional arithmetic, infer a payload from
absence, or treat payload bits as presence.

This advances the assigned Body subset without changing source syntax,
source binding, or native calling conventions.

## Scope after approval

The first implementation slice would cover optional forms of supported
root-free scalar payloads through `MakeOptional`, `None`, `IsPresent`, `Copy`,
and eager `Select`, with exact TypeSpec identity checked throughout the Body.
`char16` retains only its assigned optional operations and equality rules.
The implementation must preserve absence independently of payload bits and
pass both JIT and AOT execution tests. It must not weaken structural Body
validation or the mandatory semantic verifier.

Optional parameters/results and calls, a source spelling/type-system mapping,
optional managed handles, checked-exception execution and broader optional
operators remain outside this slice.
