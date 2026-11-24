# Standard TRRM FNI nominal enum composition

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow, pending later review.** This selects a candidate
agreement, not normative adoption, implementation support or qualification.
Existing specifications and proposal agreements remain unchanged. No build,
test, JIT/AOT or external-process inspection accompanies this decision.
The [section-coexistence clarification](standard-trrm-fni-enum-section-coexistence.md)
completes this same candidate's `NativeImport`/`ConstantSource` allocation;
read both proposals as one unapproved revision-1 agreement.

## Finding and recommendation

**The missing decision is whole-schema composition, not whether ordinary
nominal enums are inherently forbidden at an FNI boundary.** The
[FNI source contract](native-output-foreign-interface-method-identity.md#candidate-source-contract)
uses independently admitted complete native types. The accepted
[Windows x64 mapping](../specs/targets/windows-x64-call-interface.md#1-foreign-primitive-admission)
explicitly admits integer-backed frozen enums using their final carrier's
classification while preserving nominal validity. This does not admit every
enum base or crossing merely because the binder accepts it.

Inspected evidence, not executed validation:

| Evidence | Consequence |
|---|---|
| [`DirectNativeImportContract.IsScalar`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs#L77-L90), [`ForeignNativeInterfaceContract.TryGetNativeValueType`](../../sources/Terrum.CodeAnalysis/Semantics/ForeignNativeInterfaceContract.cs#L416-L463) | FNI signature admission delegates to a value predicate containing nominal enum types. That corroborates, but does not replace, the target contract. |
| [EN1 Standard/Core boundary](standard-trrm-c1-enum-closure-composition.md#standard-bound-enum-carrier), [Core origin publication](core-provider-module-origin-carrier.md#admission-and-publication), [`BoundEnumDefinition.FromCore`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs#L185-L196) | Core enums are bound nominal types too. EN1's Standard occurrence is disjoint from Core, while the Core origin carrier supplies bindings but no enum codec. Their applicability needs the explicit bridge below; neither a Source mapping nor display-name reconstruction supplies it. |
| [PS1 agreement and defaults](trrm-foreign-native-interface-parameter-surface-schema-selection.md#one-exact-provisional-agreement), [EN1 selections](standard-trrm-c1-enum-closure-composition.md#exact-whole-schema-selection) | PS1 selects F+I+SF1+PS1, not enum closure. Its explicit nominal-value exclusion concerns **defaults**, not all formal types. EN1 names only four C1 successors, none an FNI parent. |
| [FNI producer preflight](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.ForeignNativeInterface.cs#L20-L48), [native type mapping](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L11927-L11930), [FNI ConstantMap production](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs#L5724-L5752) | The Standard path cannot retain enum declarations/types and sends direct constants to the built-in scalar builder. Binder admission is not interchange support. |
| [Frozen-value builder](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenScalarValueBuilder.cs#L55-L63), [EN1 builder gate](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenScalarValueBuilder.cs#L115-L135), [schema predicates](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs#L97-L134) | An enum is neither an SF1 primitive root nor automatically an EN1 use. Existing enum helpers are selected by different complete agreements. |
| [FNI structural validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs#L4133-L4161), [native scalar decoder](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeImportTable.cs#L13-L44), [Body ConstantMap validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L1887-L1920) | Consumers also need contextual enum type/value and native-admission paths; producer-only acceptance or clearing a preflight guard is insufficient. |

Recommend **one exact PS1 successor with EN1's enum-only contract** below.
The substantive alternative is to require authored integer/record wire
wrappers and exclude nominal enums from FNI signatures. That is a viable
different API design, but changes the logical boundary and requires explicit
conversion/validation adapters; it does not preserve the already selected
nominal signature. A new FNI-specific integer-plus-enum side codec would
duplicate the general enum carrier without reducing an obligation.
Neither alternative is selected. Widening an unpublished parent in place is
permitted, but is not this distinct agreement.

## One exact provisional agreement

| Dimension | Candidate |
|---|---|
| Case-sensitive out-of-band identity | `trrm/1-draft-foreign-native-interface-parameter-surface-1-enum-closure-1` |
| Distinct internal selector | `ForeignNativeInterfaceParameterSurfaceEnumClosureDraft` |
| Complete composition | Complete PS1 F+I+SF1+PS1, plus the EN1 enum records, values, nonpublic closure, native-width binding, Standard bound enum carrier and preservation rules, including the checked Core-provider bridge below |
| Format / governing edition / stream encoding | `(1,0)` / `1` / `01`; existing eligible Body implementation forms unchanged |
| Foreign target | `windows-x64-native/1`, TargetProfile `(1,1)` |

EN1 means the explicitly reused enum delta, **not** any C1 schema literal,
independent flag or import of C1's catalog. Preserve PS1's complete owner,
receiver, convention, dispatch, label/default, output/dependency and bounded
predicate contracts. Select no C1/class/construction, PI1, general graph,
constant-generic, AC1/DeclaredCopy, array, storage-address or private composite.
Revision 1 fixes this complete agreed snapshot, not future linked text.

Select the literal for the destination, root, every supplied provider
including unused/declaration-only inputs, and captured origins before
schema-dependent access. This bounded route selects **no heterogeneous
provider set or conversion**; the general specification's permission to bind
separately supported schemas does not supply such an implementation here.
Old PS1, AC1, NominalEnumsDraft, CG1 and the four C1 EN1 literals are not
aliases or providers for this route, even for common-subset bytes.
[Exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
forbids feature-based selection, suffix parsing, sniffing, retries and
selector-only conversion. Existing identities/defaults keep their meanings.

**Enum-only providers are valid under this same new agreement.** Require
`004C` exactly when foreign-interface declarations are retained, and `0066`
according to PS1's actual explicit-formal coverage. An enum-only artifact
needs neither a dummy interface nor foreign-member surface rows. This
changes the schema-implies-nonempty-foreign-table predicates only for the new
agreement; every retained foreign owner still satisfies the complete FNI/PS1
rules. Modules with FNI retain the parent's target requirements; enum-only
Modules retain EN1's fixed/native-base target rules.

## Enum records, values and Feature closure

Reuse EN1's [enum records and values](standard-trrm-c1-enum-closure-composition.md#enum-records-catalog-and-values),
[nonpublic closure](standard-trrm-c1-enum-closure-composition.md#nonpublic-closure-and-nominal-origins)
and [Standard carrier](standard-trrm-c1-enum-closure-composition.md#standard-bound-enum-carrier)
for this one additional exact agreement. Their complete enum validity and
identity obligations apply, not their class/CLI publication routes.

* TypeDef remains 44 bytes. Extend kind extraction to bits 2..4 with enum
  kind **4**; preserve the parent's other kinds and visibility. This
  composition's permitted flags mask is **`0x1F`**, not C1's `0x9F` or AC1's
  `0x2F`. Enum public/internal/closed-private flags are `0x12/0x11/0x10`;
  visibility 3 and bits 5..31 reject. No class-mutability or DeclaredCopy bit
  is imported.
* Reuse required-policy `0000` sections **`0053` EnumDef** and **`0054`
  EnumMemberDef**, each stride **20**. Their exact fields are respectively
  `(ownerTypeDef, underlyingTypeSpecRef, flags, memberFirst, memberCount)` and
  `(ownerTypeDef, ordinal, declarationKeyString, nameString, valueSourceRef)`,
  all little-endian u32 words. The last member word is a **typed
  ConstantSource reference**, never NominalEnumsDraft's inline i32.
* Require one EnumDef per retained kind-4 TypeDef, including expected proxies,
  complete exclusive member ranges, agreeing ordinals/owners, per-owner
  unique names/keys and EN1's empty field/formal/Contract ranges and Auto
  Layout rules. Empty enums and duplicate-valued members are valid. Members
  acquire no DefId, FieldMap target or independent Import/Export.

For a complete unqualified enum T, both a member's `valueSourceRef` and an
ordinary Body constant use this existing typed chain:

```text
ConstantMap:    TypeSpecRef(T), ConstantSource row
ConstantSource kind 1 -> FrozenValueNode kind 1, typeSpec = TypeSpecRef(T)
FrozenValueNode: flags/reserved/childFirst/childCount/activeUnionCaseDef = 0
                nonzero scalarBlobRef -> exact underlying integer bytes
```

The root and use must resolve to **the same nominal T in their actual using
context**, not merely equal bases or widths. Typed row references are
one-based `1..00FF_FFFF` with zero high bytes, distinct from Body-local
zero-based u16 indexes. Preserve EN1's reference/extent checks and legal
member-to-owning-type back-edge; it does not permit frozen child cycles.

| EN1 base | Exact little-endian Blob | Direct by-value FNI crossing |
|---|---|---|
| int8/16/32/64; uint8/16/32/64 | 1/2/4/8 bytes, signed two's-complement or unsigned respectively | Only with the target admission below |
| int128/uint128 | 16 bytes, signed/unsigned respectively | **Not admitted** by Windows x64's formal primitive mapping |
| nint/nuint | Exactly 8 bytes, signed/unsigned respectively | Only with EN1's exact target-1/profile-(1,1) binding |

Range-check mathematical values before encoding; never truncate, wrap or use
host pointer width. Ordinary unnamed values are valid; Flags does not impose
a named-member whitelist. Equal-valued members remain distinct declarations.
Intern typed values only by complete T and value; equal Blob bytes may be
shared across distinct typed roots. Native-width owners, all using Modules
and relevant ModuleRefs must meet EN1's target binding; Blob length cannot
choose it. Preserve actual target-specific Blittable commitments rather than
deriving them from the base. This native-width codec is enum-only; it does
not register standalone SF1 nint/nuint roots.

Add **NominalEnums `18/1 -> [11/1]`**, edition 1. Every Module retaining an
enum declaration, expectation, nested type, member or value requires this
closure, including uncalled uses. Preserve **all of PS1's catalog**:
`11 -> []` remains SF1's ordinary scalar registration; 18 supplies nominal
leaves. Keep bounded `10 -> []` and incomplete `12 -> [10,11]`, not C1's
graph edges. No 9, 12, 17 or 24 is implied by an enum constant.

A basic enum-bearing FNI declaration/call has effective closure
**`{1,2,5,11,18,26}`**; NativeOutput adds **8 and 10**. Enum-only providers
need no 5/26 absent foreign owners. Actual Blittable commitments, queries,
FieldDef bindings, direct calls, addresses, NativeParameter and unwinding
retain their independent requirements. Enum metadata/ConstantMap alone adds
no 13. Requirements belong to each using Module, not an artifact-wide union.

## Signatures, Bodies and native representation

Carry T unchanged through MethodSig results, ordinary Param `mode/flags=00/00`,
nested types, HomeMap/BlockEntryState, CallSite inputs/results and ordinary
copy/read/store/return operations. Metadata uses nominal TypeSpec forms
**02/03** through TypeSpecRef. Body TypeMaps use exact TypeDef **13**, Import
**1C**, or TypeSpec-row **11** tokens under the
[existing namespaces](../specs/il-metadata.md#table-stream-and-token-encoding);
F2 of the integer base never means T.

Keep the exact selected foreign MethodMap target, receiver input 0 and
completed explicit inputs in formal order. Receiver capture and written
argument evaluation remain once-only in source order. A constant, formal,
local snapshot, nested call result or returned enum retains nominal typing,
exact bits and its base representation throughout shared traversal,
verification, reconstruction and lowering. A bound constant's logical type
is T and its storage/representation type is the checked base; the existing
`RepresentationType == Type` primitive-only shortcut cannot reject or erase
that pair. Do not fabricate a zero-field struct or require a struct Copy bit
for an enum.

Direct native enum formals/results require frozen Copy validity, the actual
Blittable commitment for `(1,1)`, and an exactly matching foreign
representation/validity contract. Use the admitted base's integer-carrier
classification, payload width and signedness, including register/stack
positions after the receiver. Do not assume upper-bit extension, integer
promotion, marshalling or equal-width nominal conversion. In particular,
int128/uint128 declarations and frozen values can survive enum closure but
cannot become direct foreign primitives.

Nested enum occurrences in already-admitted pointer/function-pointer or
aggregate declaration closure preserve T and those forms' independent
admission gates. Pointer size does not establish pointee validity. NativeOutput
retains its existing complete cell eligibility, extent/alignment, reservation,
dependency and publication rules; this adds no enum result discriminator.
Output-bearing members still have exactly int32/uint32 results, not enums
with those bases. Caller-unsafety and actual receiver/native-provider code
lifetime remain mandatory.

**PS1 defaults remain SF1-only.** An enum Param may be required, but a retained
enum default still refuses publication rather than becoming absent or an
integer default, even if observed calls supply it. This selects no enum
arithmetic, comparison, update, conversion, special-register constant,
predicate-selector or general constant-execution amendment. Unsupported
operations/crossings remain unsupported, not integer-erased fallbacks.

**Core enum identity is not inferred by EN1.** A Core enum FNI type is
supported only when the separately selected Core-provider binding maps its
actual Core handle and member keys to an owned EN1 TypeDef/EnumDef under this
exact agreement. Preserve `BoundEnumDeclarationIdentity.Core`; do not rekey
it as Source/Standard or derive an owner from its display name. A logical
Core registration without that owned provider definition is unsupported for
Standard FNI publication/consumption. This reuses the Core-provider origin
candidate without making its host authority, provider selection or
registration rules an EN1 feature.

## Source publication and imported closure

Reuse the existing `TrRmSourceEnumDeclarationExport` record and EN1's
[source-anchored capture rules](standard-trrm-c1-enum-closure-composition.md#source-anchored-api-and-joint-capture).
Add one candidate six-argument `EmitStandard` overload whose arguments are
`program`, `moduleIdentity`, `schemaIdentity`, `moduleTargetRequirement`,
`IReadOnlyList<TrRmSourceTypeExport> typeExports`, and
`IReadOnlyList<TrRmSourceEnumDeclarationExport> enumExports`.
It selects only the new literal. The existing generic-method overload is
unchanged. FNI owner/member and eligible struct mappings retain PS1's rules;
`typeExports.EnumMemberDeclarationKeys` remains default.

Snapshot both lists jointly. Require one source-anchored record per fresh
retained source enum, including uncalled declarations: exact checked program,
input ordinal, full primary declaration Span, complete partial composition
and original member order, explicit nondefault alias/member-key arrays.
Empty member arrays are valid for empty enums; empty aliases mean unpublished.
Check ExportKey uniqueness jointly with all type/field/method publications,
but enum member keys only within their owner. No name-derived keys, imported
rekeying, copied/rebound program, secondary-part selector or fabricated Core/
private identity qualifies. Source accessibility remains producer-checked.

The existing three-argument whole-Module `CreateStandardReference` selects
that Module's owned public enum exports alongside its FNI declarations.
The existing four-argument TypeDef-key factory also accepts owned public enum
roots under the new literal, including enum-only selections and aliases.
Support composing the parent's FNI reference with these selected enum roots,
preserving checked nominal correspondence and all direct expectations;
this is not new independent multi-FNI-root admission. Unselected/public
closure, internal and file-local enums stay out of source-name lookup.
Source-free re-emission uses retained occurrences, not fresh source maps.
The existing source-free emitter route may select the new literal but cannot
invent missing fresh-source keys. No CLI grammar/whitelist, private revision
or C1 class-export transport is extended here.

Extend EN1's Standard enum occurrence applicability explicitly to this
literal. Preserve immutable selected-schema/provider snapshots, the owned
actual declaration, every original expected view, aliases, and direct
Import/ModuleRef edges. Reconstruct the bound enum type table and member
owners from those checked origins, not artifact enum ordinals or Core-shaped
tokens. Nonpublic closure remains retained/packed with its owning provider;
it gains no export or source access.

For each retained enum Import, resolve its own using-Module expectation to
the selected public **owned**, non-proxy provider TypeDef before comparing
namespace/name, visibility, base/native binding, Flags, complete Layouts and
ordered member keys/names/ordinals/exact values. Reject proxy re-exports,
including unselected ones. Check every diamond expectation in its own row
space before interning aliases/repeated paths by the checked actual origin.
Preserve all original views across replacement; equal names, bits, slots or
integer ABI do not establish correspondence. Closed-private replacement
requires EN1's checked meaning-preserving old-site/new-site rewrite or
retention of the original closure; otherwise refuse.

### Core enum FNI signatures

**Compose the Core origin carrier with EN1 in this same exact agreement.**
The existing nominal TypeSpec forms `02/03` suffice; add no Core TypeSpec
family, wire identity directory or Feature. This is an unapproved
applicability amendment, not evidence that the bridge is implemented.

For each retained raw Core enum declaration/type/value, require the
[Core provider declaration binding](core-provider-module-origin-carrier.md#proposed-host-input-and-adapter-shape)
to retain a checked correspondence from its exact Core handle and captured
using-Module binding generation to the EN1 occurrence of a public **owned**
provider TypeDef. Resolve the selected Module/revision/export key/kind before
comparing the complete original expectation and explicit member-handle-to-key/
ordinal mapping. The actual provider snapshot must select this new literal's
revision-1 agreement, not a C1 EN1 literal or another Core declaration schema.
Apply the same complete enum, Layout/Blittable, target/runtime, alias,
replacement and direct-edge checks as for Standard imports.

Keep `BoundEnumDeclarationIdentity.Core` and its member handles unchanged in
the checked source program; retain the correspondence alongside them through
references, traversal/rewriting and emission. Do not rekey them as Source or
Standard, use `enumExports` for them, or fabricate an owned TypeDef from
qualified names. Core and Standard views denote one T only after this checked
binding; preserve every original view and expectation when unifying uses.
Neither integer bits nor registration alone supplies that correspondence.

Serialize the using Module's complete expected TypeDef/EnumDef/member
snapshots and its TypeDef Import/ModuleRef, carrying the provider's actual
target/runtime requirements, not the consumer's. Do not export the expected
enum as consumer-owned. Packing may use a direct owned TypeDef only after
preserving the original binding obligations. Source-free loading
reconstructs EN1 Standard occurrences of that same selected T without Core
registration; it does not deserialize host authority or designate Core for
new source. Core's independent registration validity and all FNI target,
default and NativeOutput exclusions above remain unchanged.

Missing/stale bindings, incomplete member mappings/expectations, proxy exports
or absent/wrong-schema owned provider definitions refuse the affected raw
Standard publication/consumption. An authorized logical-only in-process Core
provider does not by itself meet this bounded route's owned EN1 definition
gate: it needs actual provider publication under this agreement, not invented
artifact rows. This selects no production provider or Core attribute-schema
codec. Conversely, normalization of a role/convention alone creates no raw
enum dependency; an independently retained FNI signature/value cannot use
normalization to erase its enum obligation.

## Consumption, re-emission and acceptance

Carry one immutable new selection through indexing/framing, TypeDef/enum and
Feature tables, frozen-value writers/materializers, native type checking,
FNI source/Body readers, provider binding and all caches. Reuse shared
enum-aware logic without making this selector pass C1-specific predicates.
The current public-only enum-table checks, primitive-only native decoders,
C1-only value writer/provider gates and ordinal-based materialization need
the corresponding complete selected-contract handling, not blanket removal.

Before affected exposure/use, consumers check exact schema, checked extents/
counts, typed references, kinds/visibility/owners/ranges, names/keys, source
and node kinds, complete T agreement, exact payload width, target binding,
per-Module Feature support and original provider expectations. Wrong enum T
with equal bits is malformed; a valid but unsupported target/operation
refuses use. Source mapping errors use the existing argument families,
malformed serialized input `InvalidDataException`, and unsupported complete
use `NotSupportedException`; publish no partial reference/artifact.
[Producer truth](../specs/representation/reader-trust.md) remains distinct:
consumers need not rerun enum construction, re-prove Body facts or inspect
arbitrary foreign implementations.

Meaning-preserving packing/re-emission rebuilds Module partitions/manifests,
TypeDef/EnumDef/member ranges, Imports/Exports and original expected stubs,
Strings, Layout/profile references, TypeSpecs/signatures/Params, FNI
owner/member links, PS1 surfaces and contracts, ConstantSource/node/Blob
references, and every affected Body map, Home/CallSite/record/CFG/code index
and SourceMap. Remap the member's source reference, not matching payload
octets. Preserve exact values and nominal back-edges; clone shared structural
rows when contexts diverge. Apply existing
[canonical writer/ordinary reader rules](../specs/representation/encoding-canonicality.md).
A destination unable to retain the complete closure refuses; restamping
older enum words or dropping dependencies is not conversion.

Required future acceptance cases, **not results from this document**:

| Family | Required gate |
|---|---|
| Nominal FNI values | Source-owned and imported enum formals/results; direct named-member and unnamed ConstantMap inputs, forwarded formal/local values, nested call results and returns. Preserve exact T, receiver-first/formal mapping and source-order snapshots. Reject another enum or base integer with equal bits. |
| Core bridge | Bind a registered Core enum used in FNI formals/results and member constants to an independently supplied owned enum-only provider under this same agreement. Preserve Core/member handles and all original expectations, then re-emit/reconsume source-free as the same nominal T without registration or new Core designation. Reject missing/stale context, logical-only providers without EN1 publication, absent member-key mappings, wrong owner/export/kind, proxy exports, wrong schema and same-name/same-value substitutes. |
| Boundaries | All twelve EN1 bases in declaration/member/value round trips; direct foreign crossings only for the admitted eight fixed and two native bases. Cover signed endpoints, uint64 above int32, 128-bit extrema, native target/width mismatches, missing Blittable evidence, empty enums and duplicate-valued members. |
| Source and visibility | Joint FNI/struct/enum publication, aliases, enum-only providers, explicit enum roots and source-free reconsumption without debug/source/Core maps. Preserve distinct equal-named private enums; reject stale anchors, missing/extra member keys, nonpublic exports and lookup leakage. |
| Dependencies/replacement | Non-first providers, multiple Modules, owned-dependency chains/diamonds and declaration-only members. Relocate all relevant rows, pack/re-emit/reconsume, then accept compatible replacement while rejecting changed base/member/order/value/Flags/layout, alias splits, proxy exports, lost originals or unsupported private correspondence. |
| Malformed/exact selection | Wrong-table/high-byte/zero references, overflow/truncation, bad kinds/flags/ranges, missing enum or Feature closure, wrong source/node/root T or Blob width. Wrong-case/old/C1/private selections, including an unused provider, refuse without retry. Valid reordered/duplicate Body map entries remain acceptable. |
| Preserved exclusions | Required enum parameters work without defaults; retained enum defaults fail publication. Enum-based output discriminators, direct 128-bit foreign values and unselected enum operators/graphs do not acquire support. Existing scalar defaults and output state contracts remain intact. |
| Native evidence | Later, independently qualify emitted ABI and supervised headless execution against an unrelated native provider for register/stack enum inputs/results and enum inputs on output-bearing calls. Keep schema round trips, image emission and runtime evidence separate and tied to the final inputs/configuration. Validation remains held now. |

Formal approval remains open for the exact identity/composition, enum-only
provider and Core-bridge applicability, source API/reference applicability
and reused candidate enum/Feature assignments. No enum codec or binding policy
is left implicit in this recommendation. Adoption would integrate the
[metadata](../specs/il-metadata.md), [Body](../specs/il-instruction-set.md),
[registry](../specs/representation/registry.md) and
[manifest](../specs/representation/semantic-manifests.md) owners together;
the accepted native representation and exact-schema rules are preserved.
Implementation and qualification are separate, still-open gates.
