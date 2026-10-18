# Invocation-scoped source grant

**Status: deferred.** Accepting a source requirement on persisted semantic
modules needs a concrete persisted source-processing consumer. Existing
`Host.Source` JIT support is provisional, not approval of that boundary.

## Decision and integration

The candidate lets a persisted component require one immutable source snapshot
per synchronous invocation. The [Terrum-authored component inventory](../implementation/terrum-code-analysis.md)
owns the current source/line/decoder/provider coverage; the
[module inventory](../implementation/modules.md) owns its private artifact
support. Reusing provider bytes may still involve fresh hosted verification
and machine-code emission.

This is a module binding/execution capability, not a new language source
primitive or a change to [source input](../specs/language/source-input.md).
It would add an explicitly selected invocation profile to private
reconstruction/linking and JIT admission. It makes no standard TRRM allocation
or native calling-convention assignment. Graphs, module sets, persisted AOT
and native packages remain outside this candidate.

The alternatives are to keep persisted modules source-independent, pass
source only in a compilation-local host layer, or design explicit general
view parameters and their module ABI. Recommend keeping the first boundary
until a real consumer justifies this narrower capability. Do not infer
approval of the broader view ABI from the convenience of a source grant.

## Candidate contract

- The reserved requirement is exactly readonly `char16` `Host.Source`,
  module-level and propagated through retained dependencies and references.
  Linking and replacement compare the recorded requirement; host-neutral
  modules acquire none merely because their caller has a grant.
- The caller explicitly supplies a snapshot. Empty is valid and is not a
  missing grant. The provisional envelope is 16,384 UTF-16 code units;
  missing, duplicate, writable, wrong-element, non-code-unit and oversized
  bindings reject before entry.
- One synchronous entry, nested calls and defined cleanup observe that same
  immutable snapshot. Bind fresh invocation-owned backing; no process/thread
  current source, rebinding, suspension, retention or capture.
- Persist only the symbolic requirement, length-query and read operations.
  Never persist source contents, length/address values or sample-derived
  bounds, effect or initialization proofs. Reconstruct and verify after
  reading/linking and again with the selected invocation binding.
- The capability does not become a source value, pointer, field, returned
  view or retained handle. Copied code units, scalar coordinates and
  `SourceRange` values can escape, but contain neither backing nor implicit
  snapshot identity. A range from one invocation cannot authorize reads of
  another.
- Preserve checked indexing and conditional-output initialization. A failed
  read leaves its output vacant under the existing operation contract;
  missing source is not a successful empty read.

The grant receives **already-decoded** UTF-16: preserve NUL, malformed
surrogates, BOM code units and original line endings without normalization.
Compilation **file-byte input** still requires complete strict UTF-8
validation before snapshot publication. A grant invents neither an
original-byte mapping nor permission to bypass that file-input boundary.

The prototype lowers host `char16` reads through checked host-buffer storage;
that storage choice is not a public two-byte/four-byte ABI commitment.
The four-operation source facade, source-neutral helpers and source-aware
leaf decoders share the requirement without giving arbitrary caller buffers
the same privilege.

## Independent profiles

The [scanner invocation](scanner-invocation-profile.md) additionally needs
canonical readonly tables, writable outputs and owned record publication.
The [frontend invocation](scalar-frontend-invocation-profile.md) additionally
needs private span IR, complete program publication and source/semantic audit.
Neither is implied by successful source-only execution.

The [native companion](native-source-grant-library.md) is a separate deferred
ABI experiment, not execution of this private semantic-module profile.
Embedding a snapshot into an AOT image or retaining an invocation address
would not satisfy the grant contract.

## Reconsideration and acceptance

Require a concrete consumer and a resource policy appropriate for its whole
input, rather than adopting 16,384 because it fits existing fixtures. Before
promotion:

1. Invoke identical producer/consumer artifacts with empty, changed-length,
   maximum, malformed-surrogate and mixed-line-break snapshots under both
   runtime policies. Confirm no seed contents or concrete length survives.
2. Reject each invalid binding and unsupported execution surface before
   entry; revalidate requirements through dependency replacement.
3. Preserve source/line and decoder observable behavior, including EOF,
   vacant outputs, cleanup and successive invocation isolation.
4. Prove no grant-backed value survives backing retirement and that copied
   scalar results remain independent. Distinguish artifact reuse from
   generated-code reuse.
5. Keep source-neutral modules and source-free library exports unchanged.
   Do not use a scanner fallback or embedded sample to report success.

Evidence entry points are
[`NativeJit.ExecuteWithSourceGrant`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs)
and [module regressions](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs).
They document implementation evidence, not acceptance or an independently
verified/self-hosted compiler.
