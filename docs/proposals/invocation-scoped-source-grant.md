# Invocation-scoped source grant proposal

**Status: Deferred. Accepted semantic-module execution remains
source-independent; `Host.Source` JIT support is provisional
implementation evidence only.**

## Summary

The bounded `SourceLines`, scanner and scalar-frontend components read the
immutable UTF-16 `Host.Source` grant. Hosted compilation can bind that grant as
a read-only `char16` host buffer. The current semantic-module JIT adapter
provisionally supports the invocation-scoped facade described below.
The maintained source-free compiler-primitives DLL remains separate from the
[provisional native source-grant companion](native-source-grant-library.md).

This proposal concerns only how persisted modules may declare and receive the
existing source grant. It does not change source decoding, UTF-16 coordinates,
line-break behavior, the scanner contract, or the bounded frontend's reliance
on hosted verification and emission.

## Decision question

Should a persisted module be allowed to require an immutable source snapshot
supplied by its caller for each invocation?

## Deferred decision

Do not accept `Host.Source` as a semantic-module execution dependency. The
current tests establish provisional implementation support, not a requirement
for executing compiled modules. Source is needed while compiler or tooling
components process an input file; keep that input in the host/source-processing
layer unless a concrete persisted tooling API demonstrates a need for this
module boundary. This deferral does not approve arbitrary buffers or change
source decoding, UTF-16 coordinates, line-break behavior, scanner semantics,
or the bounded frontend's reliance on hosted verification and emission.

## Provisional first implementation slice

**Status: implemented provisionally; contract deferred.**

**Summary:** The broad recommendation does not define invocation extent,
dependency propagation, source-length binding, or the initial execution
backend. The existing `SourceText` and `SourceLines` facades need only four
read operations; the scanner and scalar frontend additionally depend on
writable interchange buffers and other contracts outside this proposal.

**Provisional implementation shape:** The prototype persists only the
four-operation source facade as one development semantic producer module, with
`SourceRange` and `CharacterFacts` as host-neutral dependencies. It invokes
that facade through an independently persisted consumer using an explicit
source-binding JIT adapter. Source length and reads stay symbolic in the
module; each invocation binds the supplied snapshot into fresh
execution-owned storage, lowers those operations to the existing checked
host-buffer representation, verifies the linked program, then uses the hosted
JIT emitter. Reusing module bytes does not require reusing generated machine
code. This implementation shape is not an accepted execution contract.

**Implemented provisionally:** The current semantic-module format stores the
requirement bit and symbolic length/read operations. References propagate the
requirement; linking checks it; `NativeJit.ExecuteWithSourceGrant` validates
and copies one eligible source snapshot before hosted verification/emission.
The native emitter, graph and module-set paths reject unbound or unsupported
source requirements. This profile defines neither a general buffer dependency
nor the separate native companion ABI.

The same capability also runs the bounded Terrum-authored `Utf16Scalar`,
`CharacterLiteral` and `StringLiteral` leaf decoders and the provisional `StringRow` name and
namespace encoders for ASCII, UTF-8, UTF-16LE and UTF-32LE as persisted
semantic-module providers. This reuses the existing requirement and grant
validation; it does not make `StringRow` a TRRM section writer or make the
scanner or scalar frontend source-independent, independently
verified, or executable outside the hosted verifier/emitter path.

The provisional contract for this slice is:

- A module may require only the reserved identity `Host.Source`, with readonly
  `char16` elements and invocation lifetime. The requirement is module-level,
  exposed through references and propagated through retained producer
  dependencies. Replacement must preserve the recorded requirement.
- One synchronous entry invocation, including nested calls and defined
  unwinding/cleanup, observes one immutable snapshot. Different invocations
  bind independently; there is no process-global current source, capture,
  suspension, or rebinding during execution.
- The caller explicitly supplies exact UTF-16 code units, copied into
  invocation-owned backing. Empty source is valid and distinct from a missing
  grant. This first profile accepts at most 16,384 code units, preserving
  malformed surrogates, NUL and line endings. Missing, duplicate, writable,
  wrong-element-type, invalid-code-unit or oversized grants reject before
  entry execution.
- Artifacts retain only the requirement and symbolic source operations, never
  invocation contents, concrete length or address. Source-dependent operations
  cannot use a compile-time sample length for constant evaluation or persisted
  bounds/effect proofs. Checked indexing and conditional-output publication
  retain their existing behavior.
- The capability cannot become a source value, field, pointer, returned view
  or retained handle. Copied code units, scalar coordinates and `SourceRange`
  values may escape; they carry no source backing or implicit snapshot
  identity.
- This profile covers single-producer semantic-module JIT execution only.
  Native packages, graphs and persisted AOT images with this dependency
  reject explicitly. The native source-grant companion is a separate
  provisional implementation, not execution of this module profile.
  A reusable AOT contract requires a
  separate invocation adapter; embedding a snapshot in an executable does not
  satisfy this profile.

## Regression evidence

[Module tests](../../tests/Terrum.CodeAnalysis.Tests/ModuleTests.cs) reuse
identical producer/consumer artifacts across fresh empty, differently sized,
malformed-surrogate and mixed-line-break snapshots. They compare the facade
with hosted `SourceText`, check output vacancy and invocation-relative
end-index reads, reject invalid grants and preserve host-neutral behavior.

The [self-host inventory](../implementation/self-host.md) records decoder, line,
numeric and string-row support with their component boundaries. The associated
module regressions compare observable results with independent host oracles
under both runtime policies. That coverage does not accept the proposal,
make a compiler bootstrap, or move verification/emission out of the host.
Use [module history](https://github.com/tannergooding/terrum/commits/main/sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
for implementation provenance rather than treating past test totals as
current qualification evidence.

## Alternative

Keep persisted modules host-neutral and defer source-aware exports until the
compiler can supply source-bound code per compilation. This avoids a new
module capability but leaves `SourceLines` and other source readers outside
the persisted bootstrap library.

General host-buffer dependencies and explicit span parameters are not proposed
as substitutes: they would widen the module or reference ABI beyond this
source-specific requirement.

## Reconsideration boundary

If reconsidered, acceptance should require a concrete persisted
source-processing consumer and demonstrate that it can be invoked with
different source snapshots without rebuilding or retaining either snapshot.
Missing or incompatible source grants must fail validation before execution;
modules without this dependency remain host-neutral, and arbitrary host
buffers remain unsupported. AOT/JIT results should preserve the existing
`SourceText` parity fixtures.
