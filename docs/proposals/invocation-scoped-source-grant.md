# Invocation-scoped source grant proposal

**Status: unapproved proposal.**

## Summary

The bounded `SourceLines`, scanner and scalar-frontend components read the
immutable UTF-16 `Host.Source` grant. Hosted compilation can bind that grant as
a read-only `char16` host buffer, but persisted producer modules currently
reject host-buffer dependencies. As a result, adding source-aware helpers to a
persisted compiler-primitives library is not just build wiring.

This proposal concerns only how persisted modules may declare and receive the
existing source grant. It does not change source decoding, UTF-16 coordinates,
line-break behavior, the scanner contract, or the bounded frontend's reliance
on hosted verification and emission.

## Decision question

Should a persisted module be allowed to require an immutable source snapshot
supplied by its caller for each invocation?

## Recommendation

Yes, add a narrow `Host.Source` module dependency, distinct from arbitrary
host buffers and from stored references. Persist only the dependency identity
and its required read-only `char16` shape. The caller supplies the actual
snapshot for each invocation; module artifacts must not contain source code
units or retain a source address beyond that invocation. Existing checks for
source ranges, malformed UTF-16 and line coordinates remain unchanged.

This is the smallest direction that lets existing source-aware components
participate in a persisted compiler library without inventing general buffer
capture, reference or span semantics.

## Alternative

Keep persisted modules host-neutral and defer source-aware exports until the
compiler can supply source-bound code per compilation. This avoids a new
module capability but leaves `SourceLines` and other source readers outside
the persisted bootstrap library.

General host-buffer dependencies and explicit span parameters are not proposed
as substitutes: they would widen the module or reference ABI beyond this
source-specific requirement.

## Acceptance boundary

If accepted, a future implementation should demonstrate that one persisted
source-aware module can be invoked with different source snapshots without
rebuilding or retaining either snapshot. Missing or incompatible source
grants must fail validation before execution; modules without this dependency
remain host-neutral, and arbitrary host buffers remain unsupported. AOT/JIT
results should preserve the existing `SourceText` parity fixtures.
