# Standard FNI aggregate NativeOutput value consumption

**Status: unapproved; provisionally actionable only under the user's authorized
missing-contract workflow.** This recommends an exact schema-composition
amendment, not normative approval, an allocation, implementation support or
qualification. Existing documents and compiler inputs are unchanged.

## Finding and decision

The logical output and value operations are **not missing contracts**.
[NativeOutput cells](aggregate-native-outputs.md#decision-and-scope) already
include eligible aggregate T, and the
[foreign-interface owner](native-output-foreign-interface-method-identity.md#decision-and-scope)
applies that same contract to the selected member. The accepted
[Body value model](../specs/il-instruction-set.md#names-values-and-storage),
[storage reads](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement)
and [returns](../specs/il-instruction-set.md#one-return-operation) already
permit one complete aggregate value in a logical register. No new aggregate
load, output condition, initialization rule or return operation is needed.

The distinct gap is **positive nominal Copy evidence in the exact FNI
parameter-surface schema, and its consumption by ordinary aggregate Bodies**.
[PS1](trrm-foreign-native-interface-parameter-surface-schema-selection.md#one-exact-provisional-agreement)
selects F+I+SF1+PS1, not the
[rootless Copy carrier](standard-trrm-rootless-copy-type-evidence.md#exact-unapproved-field-and-capability-amendments)
selected by A2. That carrier's owner explicitly withholds its meaning from
unchanged F schemas. Layout/Blittable, nominal kind and absent Cleanup cannot
supply the missing positive promise.

Recommend **one explicitly distinct aggregate-consumption schema, AC1**:
the complete PS1 baseline plus that existing nominal Copy carrier, its
applicable closed-storage checks and the aggregate Feature-13 requirement
below. Consume eligible aggregate values through existing Homes, D5 and
signature-aware returns. Leave PS1's identity, selector and selected meaning
unchanged, as well as F, F+I, A2 and L1. The new identity and selector are
unapproved candidates, not numeric allocations or compatibility aliases.
Formal adoption and implementation remain separate.

### Inspected evidence

These are source/document observations, not newly executed validation:

| Evidence | Consequence |
|---|---|
| [PS1 exact agreement](trrm-foreign-native-interface-parameter-surface-schema-selection.md#one-exact-provisional-agreement), [A2 isolation](standard-trrm-rootless-copy-type-evidence.md#exact-schema-isolation) | Existing proposals settle both logical contracts and the Copy carrier, but do not select their joint FNI consumption schema. |
| [Format-versioning §2](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts), [`TrRmStandardArtifactInput`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs#L15-L19), [schema resolver](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs#L4310-L4323) | Unchanged pre-release version bytes are permitted, but no external agreement-revision component is defined for this input. The input carries one schema spelling and the resolver maps it to a selector; reusing PS1's spelling would not distinguish the changed composition. |
| [Current FNI aggregate inventory](../implementation.md#L1322-L1327), [FNI producer handoff](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.ForeignNativeInterface.cs#L227-L245), [FieldDef-export Feature trigger](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L6657-L6666) | The producer carries aggregate TypeDef/FieldDef keys and Blittable Layout with Feature 13. Its FieldDef-export trigger adds 13 independently of a query instruction; a query-free aggregate Body cannot justify dropping that retained requirement. |
| [`ValidateHomeMap`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L586-L678), [`ValidateTypeMap`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L1694-L1746) | A role-3 local Home may name an aggregate through TypeMap. Local TypeDef and TypeDef Import tokens are structurally admitted; that is not value-read eligibility. |
| [D5 type update](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L5581-L5602), [`IsCopyableRegisterValueType`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L8592-L8623) | D5 checks the exact complete Home type. Nominal struct reads require `HasDeclaredCopy` and either `LocalAggregateLayoutDraft` or `ForeignInvocationStorageAddressDraft`; `ForeignNativeInterfaceParameterSurfaceDraft` is absent. |
| [`SupportsDeclaredCopyEvidence`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs#L115-L134), [TypeDef flag checking](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs#L87-L110), [`HasDeclaredCopy`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs#L226-L228) | PS1 cannot publish/read the positive carrier. The bare StorageAddress profile also lacks this flag support, so its nominal branch alone does not establish aggregate admission. L1 has both gates. The predicate's enum branch is not FNI aggregate support. |
| [D5 framing](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs#L866-L873) and [register roles](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs#L1089-L1092) | The reader checks control and Home references; it does not replace the map validator's type/Copy admission. Recognizing D5's bytes is not supporting an aggregate read. |

Broadening only the D5 predicate would therefore be incomplete. Conversely,
these bounded implementation gates do not overturn the proposals' aggregate
eligibility or impose a scalar-only design.

## Exact proposed selection

Select this whole composition explicitly before schema-dependent use,
under [exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts):

| Dimension | Recommendation |
|---|---|
| Distinct case-sensitive out-of-band identity | `trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1` |
| Distinct internal selector | `ForeignNativeInterfaceAggregateOutputConsumptionDraft` |
| Preserved baseline | The complete PS1 F+I+SF1+PS1 records, prerequisite catalog, declaration surface, defaults, receiver/dispatch, output/dependency and binding rules |
| Added delta | Only the nominal DeclaredCopy carrier, absence/applicability and closed rootless-storage rules, aggregate value consumption through existing Body operations, and explicit Feature-13 aggregate-closure applicability below |
| Format / governing edition / target | `(1,0)` / `1` / `windows-x64-native/1`, TargetProfile `(1,1)` |

AC1 denotes this complete baseline-plus-delta snapshot, not future moving
linked text. The format owner permits pre-release changes without changing
`(1,0)`, but does not establish a separately exchanged agreement revision as
part of `TrRmStandardArtifactInput`'s selected identity. Do not reinterpret
`trrm/1-draft-foreign-native-interface-parameter-surface-1` or
`ForeignNativeInterfaceParameterSurfaceDraft` to select AC1.

Carry AC1's new immutable identity/selector through every input and destination
selection, indexing, table/Body reader and writer, catalog, admission cache,
binding, reconstruction and lowering context. Select each dependency
separately before decoding; its bytes, Feature rows or container do not choose
AC1. Unknown, wrong-case or unsupported selections refuse affected use
without decoder trials or partial output. Common-subset bytes may be valid
under either explicitly selected schema; they do not identify their origin.
Default and separately selected PS1/F/F+I operations stay unchanged.

Reuse the rootless-Copy owner's already proposed `DeclaredCopy=0x20`:

* TypeDef remains 44 bytes, flags at offset 8. Visibility and kind remain
  unchanged; the admitted mask is exactly `0x2F`, with bit 4 and bits 6..31
  zero. Only kind-1 struct declarations may set the bit, including expected
  imported stubs. Copy plus Cleanup is structurally incompatible.
* A set bit is the declaration's positive Copy promise, not initialization,
  uniqueness, rootlessness, byte-triviality or a verifier certificate. Clear
  supplies no Copy guarantee. Producers may not infer/set it from layout or
  missing Cleanup. Preserve the carrier owner's producer-truth boundary.
* Require the exact selected nominal declaration's promise and complete
  instance storage closure, recursively including every inline nominal child
  and private field. Derive rootlessness and Blittable target geometry from
  independently admitted complete types/layouts. Statics are not children.
  Pointer cells do not recursively copy/classify their pointee storage, but
  retain complete pointer identity and independent backing/code obligations.
  Apply the carrier owner's
  [closed-storage rule](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule)
  to the types this composition actually admits. Check a finite complete
  storage graph with using-Module/type/target context; reject inline cycles
  and decline unsupported or exhausted analysis, never infer rootlessness.
* This adds no type-formation codec. AC1 reuses PS1's independently admitted
  closed nominal aggregate types; unsupported constructed, array,
  optional, enum, owner, reference/support-bearing or other representations
  remain gated. An independently required Union validity/coverage contract
  cannot be inferred from overlapping Layout fields or the Copy bit.

Do **not** import A2's fixed-array type/geometry/place delta, L1's constructor
normalization or empty-Sequential completion, CopyOrigin, ReturnCoverage,
general predicates, RootPin or exception-region compositions. This document
introduces no source syntax, ordinary-method parameter surface, constructor
role, new flag number, section, clause, Feature, opcode or private revision.

## Feature closure

The reused nominal carrier belongs to existing **2/1 RootlessNativeValues**,
with prerequisite 1/1. Every Module publishing a retained set bit, retaining
a Copy-positive expectation or relying on that promise requires effective
2/1, including declaration-only and nested-type uses. Preserve PS1's
prerequisite catalog, including **13/1 LayoutQueries with prerequisites `[]`**.

For AC1, retaining the admitted **Blittable aggregate Layout/TypeDef/FieldDef
closure requires effective 13/1** in each owning/using Module, including
declaration-only, expected/imported and nested aggregate views. This applies
even without DE, FieldMap/FieldPath or separately selected field access. The
[current inventory](../implementation.md#L1322-L1327) records this FNI producer
requirement; the [producer handoff](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.ForeignNativeInterface.cs#L227-L245)
and [FieldDef-export trigger](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L6657-L6666)
show why absence of a query does not remove 13. AC1 expressly selects this
aggregate-closure applicability; it does not assert that every metadata-only
type recipe in the accepted baseline requires 13, or manufacture a query.

Basic aggregate FNI output/read/return consumption therefore requires
**`{1,2,5,8,10,13,26}`**. Scalar/pointer-only output use with no retained
aggregate closure or other Feature-13 use remains **`{1,2,5,8,10,26}`**.
SF1 constants/defaults add 11. Actual queries, FieldMap/FieldPath and
FieldDef Import/Export retain their existing 13 triggers; count the same
requirement once. Direct calls, addresses, NativeParameter and unwinding
retain their independent triggers. Schema selection alone requires none
of these conditional rows, and another Module's rows cannot satisfy the
owner's or user's closure. No array, CopyOrigin or general predicate/value
prerequisite is inferred.

## Producer and consumer integration

### Types, Homes and ordinary values

Retain T's complete identity in MethodSig results, Param and nested TypeSpecs,
and each Body Home/TypeMap. Use the
[existing namespaces](../specs/il-metadata.md#table-stream-and-token-encoding):
Body TypeMap TypeDef `13` or Import `1C` selects a complete nongeneric nominal
type; TypeSpec-row token `11` selects a complete structural type. Metadata
TypeSpecRefs instead use row form `00` or eligible F2/F3 forms. A raw
`write ptr<T>` over nominal T uses the existing indexed pointer TypeSpec and
outer write qualification, not a checked reference or bare TypeDef DefId.
Validate nominal/import references under their using Module contexts.

Preserve T's exact default/profile Layout, Blittable commitment, declared
pack/alignment and complete ordered LayoutField/FieldDef inventory. Derive
extent, final alignment and offsets under the selected target; serialized
sizes/offsets, names and equal geometry are not authority. Output T retains
the cell owner's positive-extent requirement and complete validity promise.

The producer reserves a distinct vacant role-3 T Home, invokes the exact
member and publishes its state only under the existing saved-result relation.
Only a proven-live complete T may be read through D5/short Home load and
returned through existing register/Home return forms. Register aliases,
snapshots, spills and block merges preserve complete type, generation and
dependencies. Home-return or field-by-field lowering cannot bypass the same
eligibility. Failure/ignored/stale outcomes grant no aggregate read or default.
Copying T preserves its published DependsOn/ForwardsBacking source identities
and all independently established backing/code obligations; neither a raw
pointer field nor the nominal Copy bit makes those facts empty or supplies
CopyOrigin precision.
The FNI member still returns its declared int32/uint32 discriminator; an
ordinary caller returning T is a different MethodDef/result, not another
foreign output or an aggregate status conversion.

Extend source/public-bound-program production, shared traversal/rewriting and
the compiler's existing verification after reconstruction accordingly.
Preserve positive evidence explicitly; do not reconstruct a required unknown
promise as `!IsUnique`.
Ordinary consumers owe before-use framing, bounded maps/records, complete
type and Copy/storage admission, the complete per-Module closure including
aggregate 13/1, target/support and binding checks. They may
rely on producer-published flow/validity facts without re-proving Body
dominance, outcome correlation or arbitrary native behavior. A deeper audit
is separate under [reader trust](../specs/representation/reader-trust.md).

### Binding, replacement and re-encoding

Resolve the original retained TypeDef and foreign owner/member expectations
through exact Module identities/export keys before comparison. An expected
Copy bit requires an actual positive promise from a separately selected,
supported provider schema carrying it; the expectation is not provider
evidence. Check every transitively retained nominal child and original
layout/type/formation/output obligation. Preserve stronger actual facts
relied upon without overwriting weaker original views. If an original stub
has no Copy guarantee, retain an additional Copy-positive TypeDef/Import
expectation selecting that same actual declaration, under the carrier owner's
multiple-expectation rules; an unserialized cache is insufficient. Equal names,
size, slots or a no-Cleanup replacement do not satisfy these contracts.

Retain existing public Import/Export and owner rules. This selects no
CopyOrigin contract-only visibility extension; missing required nested
identity/export evidence remains an admission gate. Separately supplied
schemas can bind only with complete actual evidence and supported checking;
one packed destination must represent every retained obligation.

Rebuild Module partitions and Feature ranges; TypeDef/FieldDef/Layout/
LayoutField/TargetProfile references; TypeSpec children and TypeDef Imports;
MethodDef/InterfaceDef/InterfaceMethod/Import/Export links; signatures,
PS1 surfaces and output/selector/dependency records. Within Bodies remap
distinct Homes, all maps, block-entry types, CallSite receiver/formal/result
selectors, OutputReservation indexes/formals and DF/D5/return operands.
Rebuild record indexes, block/code/section extents and retained SourceMap
coordinates. Never deduplicate Homes, substitute row bytes, reinterpret
quantities as references or clear requirements. Clone shared structural
rows when using contexts diverge; invalidate affected admission/layout/native
caches on changed schema, facts, providers, target or rules.
Cache keys retain AC1's distinct identity and full checking context; PS1
checks or equal format bytes cannot authorize AC1 use.

### Lowering

Project the actual reserved T Home address into the member's pointer formal,
with exact extent/alignment and the existing receiver-first mapping.
An explicit output formal is not the receiver; formal zero follows it.
Keep staging/reservations and all receiver/backing/code dependencies intact.

After normal publication, lower the aggregate value to exact-extent copies
or multiple physical locations as needed. No logical register implies a
machine-word or sixteen-byte limit. Preserve payload validity and padding/
packed-access obligations. Ordinary caller T returns use the selected
[aggregate/Transparent classification](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults)
and correctly aligned result supporting storage. An ABI hidden result buffer
is not the NativeOutput cell. Preserve probing/unwind and provider lifetimes;
unsupported mapping or capacity refuses use, not truncated successful output.

## Alternatives and integration owners

| Candidate | Assessment |
|---|---|
| **Distinct AC1: PS1 plus the existing nominal Copy delta and aggregate-closure requirement — recommended** | Supplies the missing declaration evidence and aggregate read/result integration without a numeric allocation or unrelated executable profile. Peers select the new identity; old PS1 keeps its exact meaning. |
| Compose full L1 with PS1 | Materially larger: imports arrays, constructor normalization, nullary completion, repeat/raw-Union/CopyOrigin and their catalog/operation gates. Those are unnecessary when foreign success supplies the complete T; such a composition would need its own complete agreement. |

On adoption, integrate AC1's distinct selection, the rootless-Copy carrier's
AC1 applicability and the aggregate Feature-13 trigger; metadata/registry/
manifest projections must agree. Leave old PS1's composition unchanged.
Preserve the accepted Body, complete-type,
layout, target, reader-trust and directed-binding owners. This proposal changes
none of those files and assigns no number. Compiler integration is not just
a reader allowlist: it includes truthful source emission, actual provider
evidence, aggregate reconstruction/verification, remapping and native values.

## Required future cases and remaining decision

These are acceptance requirements, **not tests run**. Use unrelated synthetic
headless interfaces/providers and source-free independently assembled inputs.

| Case | Required outcome |
|---|---|
| Scalar, pointer and nested nominal aggregate cells | Preserve one shared output protocol; require positive Copy on every stored nominal child, not scalar/pointer TypeDefs or recursively classified pointee storage. |
| Aggregate success read and ordinary T return | Exercise D5, register alias/merge, Home return and non-inlined helper return, with sizes 1/2/4/8 and larger than 16, packed and over-aligned layouts. No constructor, field-only workaround or default aggregate is required. |
| Complementary/ignored/stale result, partial native writes, reuse/reentry | No unguarded read/publication, stale-generation revival, second reservation or complete-T cleanup. Separate source/audit regressions from mandatory structural consumer checks. |
| Missing bit/child promise, Copy plus Cleanup, invalid mask or support-bearing child | Refuse affected type/read/return use, including uncalled retained expectations; a structurally consistent lying producer remains a truth/audit case. |
| Separate aggregate and interface/member providers | Preserve exact TypeSpec/Layout/Import closure and original versus actual Copy promises; reject no-Copy, changed origin/packing/alignment/field/condition or dependency replacements despite equal carriers. |
| Malformed maps/records and boundary indexes | Reject wrong namespaces/high bytes, Home/result type mismatch, incomplete layout ownership, wrong receiver/formal mapping and reservation-to-call mismatch; retain usable u16 `FFFF` and checked extents. |
| Query-free aggregate declarations/Bodies and missing 13 | Retain 13 for the Blittable Layout/TypeDef/FieldDef closure even without DE or FieldMap; reject its omission from any owning/using or required provider Module, including uncalled and nested expectations. |
| Defaults/constants, aggregate closure and actual field/query use together | Preserve PS1 labels/SF1 bits; aggregate use already requires 13, while constants/defaults add 11. Scalar/pointer-only uses without aggregate or query/binding requirements need no 13. Preserve each Module's closure; no automatic 15, 20, general predicates or new codec. |
| Source-free packing/re-emission | Permute global rows, Homes, maps, blocks and records; cross compact/full-width boundaries and retain original expectations, source coordinates, payloads and behavior. |
| Schema isolation | AC1's new identity and selector select only its complete composition; old PS1/F/F+I keep their meanings and reject AC1-specific required content. Test common-subset bytes under either explicit selection, wrong-case/unknown/unsupported selectors and mismatched component contexts. Feature 2/13 or a header edit cannot select AC1; unsupported wider combinations refuse without decoder trials or private fallback. |
| Native evidence | Independently check layout, emitted ABI and supervised JIT/AOT execution of success/failure branches and aggregate returns; actual native provider remains live. Emission is not execution evidence. |

The in-scope recommendation is concrete: **distinct AC1, preserving old PS1
and adding the existing positive nominal Copy carrier, closed-storage checks
and aggregate 13/1 requirement, consumed through existing value operations**.
No further output semantics, numeric assignment or consumer policy is left
undecided within that scope. Formal approval, exact whole-composition
agreement, implementation and qualification remain outstanding. Wider
type/constructor/array/Union-coverage/failure compositions remain their owners'
independent gates, not implicit decisions here.
