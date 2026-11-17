# Standard TRRM C1 metadata-only class declarations

**Status: unapproved; provisional carrier recommendation under the
user-authorized missing-contract workflow.** This proposes a bound
representation, not implementation support or a class runtime agreement.

## Finding and alternatives

[C1](standard-trrm-exception-class-witness-binding.md#source-free-retention-and-transformations)
requires typed class/interface declarations, complete retained expectations
and source-free transformations without fake Bodies or zero-field class
stand-ins. [C1M1](standard-trrm-c1-class-declaration-mutability.md) adds the
class declaration's exact mutability fact, not physical layout.

An identity-only foundation **already exists**:
[`BoundGenericNominalTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericNominalTypes.cs)
defines provider/Module/TypeDef identities, definitions admitting class kind
2 and empty formal lists, and nominal uses with qualifications and original
TypeSpec context. `BoundProgram` and `CompilationReference` retain these
tables. The missing contract is a complete metadata class declaration and
its expected/actual views, not another nominal identity number. Those
definitions do not carry class visibility, declaration mutability, fields,
Layout variants or the complete member/witness closure. Their
`ProviderExpectations` retain binding origins/snapshots, not that typed
declaration payload.

| Candidate | Assessment |
|---|---|
| **Separate metadata-only class declaration payload, reusing existing nominal identities/uses** | **Recommended.** Retains declarations without requiring physical storage. Keeps the current executable `BoundClass` invariant and avoids a second identity namespace. |
| Change `BoundClass` into a declaration plus discriminated, optional runtime layout | Coherent alternative, but requires separating semantic fields from physical fields and revising every class-table, verifier, reference and lowering consumer. Merely making `Size` nullable or adding an unchecked metadata-only flag is insufficient. Prefer this only as an explicitly scoped class-IR redesign. |
| Existing `BoundClass`, private reference snapshot, or nominal identity alone | Insufficient. The first two retain physical fields/size; the last retains too little declaration information. Zero size, dummy fields, empty implementation Bodies or snapshot passthrough cannot complete the contract. |

## Proposed carrier boundary

Introduce an immutable candidate **`BoundStandardClassDeclaration`** payload
associated with the existing nominal definition, not an entry fabricated in
`BoundProgram.Classes`. Reuse nominal instances, including nongeneric uses
with empty arguments; do not allocate another `PrimitiveType` range or
reinterpret `ManagedClassTypes` indexes. Its required information is:

1. **Declaration facts:** exact snapshot-scoped TypeDef origin and class kind,
   namespace/name, full metadata visibility and C1M1 declaration mutability.
   Preserve typed member, formal, contract and interface/witness relationships
   under C1's complete-closure rules; missing codecs still refuse admission.
   A source-backed executable layout may be explicitly associated with the
   same declaration, never treated as another nominal class.
2. **Declared layout, not computed layout:** complete owned FieldDefs with
   their identities, types, access and static/mutable roles; every complete
   Layout variant, its exact target/default selection, kind, Blittable
   commitment, declared alignment/packing and ordered field membership.
   Do not use `BoundStructField` as the declaration schema: its required
   `Offset` and `Size` are physical facts. The payload has no object size,
   computed offsets/alignment, header, allocation or collector descriptor.
   An actually empty field range is valid only when the checked declaration
   is genuinely empty, never as a replacement for unavailable fields.
3. **Separate expected and actual views:** reuse
   `BoundSourceTypeProviderSnapshot`/`BoundSourceTypeExpectation` context for
   exact schemas, immutable provider artifacts, original using Modules and
   Imports, expected TypeDefs, authorized provider/key selections, aliases,
   requirements and actual definitions. Retain each typed expected
   declaration independently after resolution; merging actual identities
   must not replace or weaken original expectations. Names and row numbers
   alone establish neither identity nor authority.

Complete mutability-bearing declarations require explicitly selected C1M1.
C1's missing fact is not `false`; an opaque nominal identity retained under
C1 does not authorize complete class binding or automatic schema promotion.
Source publication obtains declaration facts and explicit keys from checked
source inputs under C1's mapping rules, not from hosted physical offsets.
Imported publication needs no provider source/debug data.

Declaration verification checks schema support, kinds, flags, bounded
references, ownership/ranges, complete layout membership and every required
binding before affected use. It does not derive runtime layout or re-prove
producer-authored Body facts. Preserve class/interface/struct distinctions,
complete TypeSpec qualifications, class self arguments and witness owners.
Bodyless expectations remain bodyless.

Shared traversal and rewriting must visit the declaration graph and all
expected views as well as type uses. Rebuild Module partitions,
TypeDef/Import/Export, FieldDef/Layout/Contract ranges, TypeSpecs/signatures,
interface/witness/member references and affected Body maps together,
including Import-to-direct resolution. Retain original binding obligations
after row remapping; keeping an old artifact snapshot alone is not evidence
that newly emitted references are correct.

Metadata retention never promotes a class to executable `Classes`.
Construction, object field access or lowering that needs an unsupported
object/handle/runtime agreement must refuse affected use. No implicit
conversion to a struct, image-private class layout, `gc` handle or native
ABI is selected.

## Exact implementation and specification owners

These are integration requirements, not source edits in this proposal.

| Source owner | Current boundary and required integration |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`BoundGenericNominalTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericNominalTypes.cs) | `ManagedClassTypes` indexes the layout-bearing `Classes` table; `BoundClass` requires physical fields and size. Associate the separate declaration payload with existing nominal identities, preserving distinct expected views and typed interface/witness edges. |
| [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.ManagedClasses.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs) | References carry the same `BoundClass` shape. Private class references retain `BoundPrivateClassSnapshot` layouts, including size comparisons; they are not Standard identity-only declarations. Project/import the new declaration facts without invoking that private layout path. |
| [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | Class verification requires nonempty fields and an aligned size of 8..128 bytes, then checks physical widths/offsets. Private reference-only exemptions occur after the common shape checks. Add declaration/binding validation separately; do not bypass or weaken executable-layout verification. |
| [`BoundTreeTraversal.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs) | `RewriteTypes(BoundClass)` rewrites physical field types; generic nominal rewrites do not supply a class declaration graph or contextual row remap. Extend shared traversal/remapping to the payload, origins and every expectation. |
| [`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`TrRmExecutableCompiler.StandardConsumption.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`TrRmExecutableCompiler.StandardTypeReference.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | `EmitC1InitializationReferenceBodies` rejects class/interface/witness and nominal tables; `CreateStandardReferenceCore` projects functions; explicit type-root projection is struct-only. `FromNativeValueRow` maps nominal TypeDef/Import uses through structure indexes. The separate generic-nominal reference API is not C1/C1M1 class publication. Add typed declaration publication, provider binding, projection and re-emission rather than removing these guards alone. |
| [`ValueLayout.cs`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs), [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), [`NativeManagedClassRuntime.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeManagedClassRuntime.cs) | Existing executable class consumers use physical sizes/offsets and image-private allocation conventions. Keep metadata-only declarations outside those paths until a separately supported target/runtime agreement exists. |

No accepted text or wire assignment is replaced. Exact governing owners are:

- [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics):
  TypeDef/FieldDef/Layout/LayoutField ownership, Import/Export expectations,
  nominal TypeSpecs `02/03` and constructed `05`. Keep C1M1's proposed
  mutability amendment in its own owner; add no section, Feature or selector.
- [Binding and replacement](../specs/representation/binding-and-replacement.md):
  selected identity before comparison, every retained expectation/alias,
  complete declaration and representation obligations; C1M1 owns exact
  Boolean comparison.
- [Consumer verification](../specs/representation/reader-trust.md) and
  [logical witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain):
  before-use structural/support checks and retained typed witness closure,
  without mandatory semantic re-proof.
- [Data layout and ABI](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations):
  declared Layout inputs remain distinct from target-derived physical
  representation. This carrier chooses no source grammar, library API,
  object header, roots/GC, allocation/cleanup ABI or native calling convention.

## Approval and focused acceptance

The separate-payload recommendation and its cross-layer integration remain
unapproved. C1/C1M1 adoption, complete class/interface/member/witness codecs,
construction/value closure, API/CLI transport and all runtime qualification
remain their existing owners' separate gates. This document changes no
support inventory or milestone status.

Required future cases, not test results:

| Case | Required outcome |
|---|---|
| Source-free declarations | Publish unrelated immutable/mutable classes; reference a non-first selected provider with all source/debug absent, bodyless expectations and real field/Layout data. Recover complete typed identities and contracts without constructing `BoundClass` stand-ins. |
| Binding and replacement | Refuse wrong nominal kind, declaration mutability, member/witness owner, access or retained layout commitment. Check several using Modules and aliases independently, even after an earlier resolution succeeded. |
| Transformations | Permute/remap declaration, Import and dependent rows, pack Import-to-direct definitions and reconsume emitted bytes. Preserve original expected views, actual origins, qualifications and class-self witness equality. |
| Missing versus empty | Retain a genuinely empty metadata declaration without fabricating size. Reject omitted required fields/layouts, malformed ownership/ranges, C1-to-C1M1 inference and unsupported closure before publication. |
| Runtime boundary | Identity retention and declaration comparison need no invented object facts. A required executable use lacking a supported target/runtime agreement refuses; private managed-class tests or successful metadata loading do not discharge that gate. |
