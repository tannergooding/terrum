# Standard TRRM FNI enum section coexistence

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow, pending review.** This completes the section
interpretation of the [PS1+EN1 candidate](standard-trrm-fni-enum-closure-composition.md).
It changes no accepted specification, code, test or support claim. Evidence
below is source inspection only; builds, tests, JIT/AOT and process inspection
remain held.

## Finding and smallest decision

**Use Standard `0021=NativeImport` and `0030=ConstantSource` together. No new
numeric allocation is needed.** The collision is in shared producer routing,
not between the inherited Standard registry assignments. In particular,
calling a helper `EmitDevelopment` does not select Development's schema.
The missing integration decision is to exclude that helper's Development
NativeImport route from this Standard composition, not to relocate EN1 values.

| Inspected owner | Evidence and consequence |
|---|---|
| [Registry](../specs/representation/registry.md#1-authority-and-schema-identity), [machine section registry](../specs/representation/trrm-1.registry.json), [metadata directory](../specs/il-metadata.md#header-and-directory) | Standard assigns `0021` to 16-byte NativeImport and `0030` to eight-byte ConstantSource. There is one artifact-wide section per kind, not one directory per Module. |
| [F selection](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities), [F+I](trrm-foreign-native-interface-schema-selection.md#exact-proposed-selection), [PS1](trrm-foreign-native-interface-parameter-surface-schema-selection.md#one-exact-provisional-agreement), [EN1](standard-trrm-c1-enum-closure-composition.md#enum-records-catalog-and-values) | F selects B's Standard-facing schema, not private Development rows. PS1 adds SF1 sources/defaults; EN1 adds typed member sources. Neither selects a replacement for Standard `0021/0030`. The FNI enum composition does not resolve its shared emitter's contrary routing explicitly. |
| [`EmitDevelopment`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), `nativeImportSectionId` and final section emission | The initial NativeImport kind is `0030`, changed to `0021` only by the `standardNativePointerLoads` branch. Otherwise the producer uses a Development NativeImport writer. Final frozen-value emission also uses `0030` and has an explicit coexistence refusal. The conflict already arises with scalar defaults and becomes unavoidable when imports coexist with a nonempty enum, even without Body constants/defaults. |
| [`TrRmFrozenScalarValueBuilder.EncodeEN1EnumClosure`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenScalarValueBuilder.cs), [typed enum writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.C1EnumClosureWriter.cs) | The builder returns source/node/Blob payloads and counts, not a section-kind assignment. Its sources are eight-byte typed rows; their callers place them in Standard `0030`. A producer cannot solve coexistence by discarding member values or publishing two source row spaces. |
| [`TrRmArtifactIndex`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), `IsDevelopmentProfile`, `FixedStride`, directory loop and reference-slot validation | FNI profiles are not Development profiles. The Standard branch already fixes `0021/16` and `0030/8`; the Development branch fixes `0030/16`. Duplicate directory kinds and wrong fixed extents reject. Standard row slots can target either `0021` or `0030`, with independent table counts. Neither rows nor ReferenceSlots provide an overloaded-table discriminator. |
| [Structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs), [Standard NativeImport codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeImportTable.cs) | Non-Development validation consumes ConstantSource at `0030` and Standard NativeImport at `0021`; the Development reader is separately gated. Equal 16-byte extents do not make the two NativeImport payload layouts equivalent. |
| [Index tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmArtifactIndexTests.cs), [NativeImport tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmNativeImportTableTests.cs), [PS1 tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInterfaceParameterSurfaceTests.cs), [FNI enum test](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFniEnumClosureTests.cs) | Test source distinguishes Standard/Development strides, checks Standard import field offsets and PS1 scalar sources, and adds an enum-only provider case. These are not executed evidence of combined NativeImport/PS1/EN1 support. |

Thus there is no genuinely unassigned numeric contract to invent. This
proposal makes the inherited allocation explicit for the new composition
and records the required implementation correction. It does not characterize
the current conflicting output as another valid draft encoding.

## Exact scope and wire interpretation

Apply this clarification only to the exact, case-sensitive agreement
**`trrm/1-draft-foreign-native-interface-parameter-surface-1-enum-closure-1`**,
internal selector `ForeignNativeInterfaceParameterSurfaceEnumClosureDraft`,
format `(1,0)`, governing edition 1. Its revision-1 snapshot includes this
clarification. No new selector, suffix, header bit or Feature is needed:
`0021/0030` already follows from its inherited Standard baseline.

Select the complete agreement before indexing every supplied artifact,
including unused providers, retained origins and the destination. Preserve
the composition owner's same-schema provider restriction. Equal version
bytes, Feature lists, row counts, strides or plausible payloads never select
a decoder. There is no `0030` NativeImport alias in this agreement and no
fallback decoder for conflicting prototype output. Common-subset bytes may
still be valid under independently selected agreements; bytes cannot prove
their origin.

| Kind | Required policy / fixed stride | Exact meaning in this agreement |
|---|---|---|
| `0021` | `0000` / 16 | Standard NativeImport: offsets 0..3 are `u8 ownerKind, resolutionMode, resolutionPolicy, callConv`; offsets 4, 8, 12 are `u32 ownerRef, libraryName, entryName?`. |
| `0030` | `0000` / 8 | ConstantSource: `u32 kind, payloadRef`. Kind 1 selects a required FrozenValueNode row in `003F`, not a NativeImport, Blob, DefId or Body-map index. |

NativeImport retains its exact owner domains, static/bodyless callable or
data eligibility, resolution pairs, conventions, ASCII names, optional
entry-name meaning, owner uniqueness, caller-unsafety and independent
Feature/binding/target checks. A Development four-word
method/library/export/policy row cannot become Standard by changing its
directory kind: construct the Standard fields from checked facts.
Foreign-interface members remain their separate `004C`/InterfaceMethod
role, never synthesized NativeImports.

All SF1 defaults, EN1 member values and admitted Body constants share **one
artifact-wide ConstantSource table**, one FrozenValueNode table and one Blob
table. Their counts are independent of NativeImport's count. Preserve
`0003` Blob's offset index, `003F`'s 24-byte node, `0053/0054`'s 20-byte enum
rows and `0066`'s 16-byte PS1 surface. Required empty tables are omitted;
present tables have positive counts and exactly checked extents, with no
new index, tag, trailer or ReferenceSlots prelude.

Table-specific references remain one-based `u32`, `1..00FF_FFFF`, high byte
zero. `0054.valueSourceRef`, `0066.defaultValue` when nonzero, and each
Body ConstantMap source word target **0030**. Source kind 1's payload targets
**003F**, whose scalar payload targets **0003** except SF1 unit's required
zero Blob reference. Complete types remain TypeSpecRefs. Preserve EN1's
exact nominal type/base/target and value validity; PS1 defaults remain
SF1-only, so this does not admit enum defaults. No DefId or Body token
namespace changes: MethodMap still selects MethodDef/Import, not NativeImport
rows. Assigned richer source kinds keep their unsupported capability gates.
Preserve EN1's legal back-edge from a member to its owning type, not cyclic
frozen child graphs.

For retained optional policy-`0002` data, row-selector 0 slots distinguish
`targetKind=0021` and `targetKind=0030`; validate the actual target table,
row count, reserved fields and zero block/ordinal independently. A slot to
row 1 of one table is not row 1 of the other. Standard instruction-selector
1 still requires target kind zero and valid Body coordinates. Core semantic
rows keep direct references, not slots. No enlarged kind/count array or new
row-slot eligibility is needed for these two existing kinds. Rewriters may
drop optional data when no meaning-preserving mapping exists.

Producers must publish valid rows and truthful retained facts. Consumers
check the selected framing, references, value validity, requirements,
support and bindings before affected exposure/use, without re-proving
published Body facts or foreign behavior. Preserve the parent's existing
argument/malformed/unsupported failure channels; publish no partial result.

### Feature closure and preserved boundaries

Keep the complete PS1+EN1 catalog unchanged: `11 -> []`,
`18 -> [11]`, bounded `10 -> []`, incomplete `12 -> [10,11]`, and PS1's
26 applicability. A basic enum-bearing FNI declaration has
`{1,2,5,11,18,26}`; an independently retained callable NativeImport adds
3 with prerequisites `{1,2}`. Outputs, NativeParameter, native data,
addresses, queries and other retained contracts keep their separate triggers.
Physical coexistence adds no capability or artifact-wide manifest union;
each using Module needs its own complete closure, including uncalled uses.

This changes no source syntax, library API, enum/Core identity, ABI, unsafe
authorization, foreign resolution policy, output protocol or lowering
semantics. Existing F, F+I, PS1, C1/EN1, private and Development agreements
keep their meanings; this document neither repairs nor widens their
implementations. In particular it selects no Development `0031..0033`
native-family rows and no broader FNI/enum redesign.

## Alternatives and allocation review

| Candidate | Assessment |
|---|---|
| **Use inherited `0021` NativeImport and `0030` ConstantSource — recommended** | Already coherent with B/F, SF1, EN1, Standard strides, reference slots and structural readers. Correct shared helper routing without creating another table identity or changing value references. |
| Move ConstantSource to a fresh candidate such as `0069`, make `0030` exclusively Standard-layout NativeImport, and remove `0021` NativeImport in that new schema | Possible deliberate draft redesign, not prohibited by compatibility. It changes both inherited assignments, all source-table lookups/cloners and slot targets/count coverage, plus `FixedStride(0030)`. Direct typed source words still need their enclosing schemas rebound to the new table. It preserves no useful semantic distinction and retains no Development payload compatibility merely by keeping `0030`. Not selected. |
| Put Standard-layout NativeImport at a fresh candidate such as `0069`, retain ConstantSource `0030`, and retire NativeImport `0021` in that new schema | Also coherent with a separately agreed schema and complete reader/writer/slot remapping, but duplicates the purpose already served by `0021`. More allocation and dispatch work for no coexistence benefit. Not selected. |
| Forbid simultaneous records | A temporary unsupported-operation refusal is honest, but not the completed schema. Forbidding imports drops retained F contracts; forbidding sources loses nonempty enum declarations/defaults/constants. A per-Module prohibition cannot resolve artifact-wide tables when packing different Modules. No support contract justifies that scope reduction. |

This is not additive-only reasoning. The accepted draft numbers are mutable,
but their existing separation already solves the problem. The inspected
allocation set uses Standard sections through `004B`, candidate `004C` for
FNI, `004D..0052` for graphs, `0053/0054` for enums, `0055..005E` for
static/evaluation families, `005F..0063` for access, `0064/0065` for CG1,
`0066` for parameter surfaces, `0067` for binding equality and `0068` for
witness projections. `0012` has a separate fixed-array candidate. `0069`
above is only an alternative, **not an allocation or reservation**.
Numeric gaps below `8000` are not extensions; nonsemantic extension kinds
cannot carry either required table. No occupied number is retained as an
alias, and duplicate `0030` entries or content-dependent interpretation
are not alternatives.

## Owners, implementation gaps and preservation

The [FNI enum composition](standard-trrm-fni-enum-closure-composition.md)
owns this exact profile. On integration, add this explicit `0021/0030`
coexistence rule there. This supersedes only an inference that its use of a
Development-named helper imports Development's allocation/payload; no such
exception to Standard was approved or selected by the parent proposals.

The numeric/shape owners are [registry prose](../specs/representation/registry.md)
and `metadata.sections` in the [machine registry](../specs/representation/trrm-1.registry.json);
[metadata framing, rows and slots](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
owns their meaning. Their existing `0021/0030` assignments are preserved,
not replaced. [Format versioning](../specs/representation/format-versioning.md),
[manifests](../specs/representation/semantic-manifests.md),
[reader obligations](../specs/representation/reader-trust.md) and
[canonicality](../specs/representation/encoding-canonicality.md) remain
unchanged. SF1/PS1/EN1 remain the value/surface owners; this document owns
only their coexistence clarification.

| Required integration area | Current gap / obligation |
|---|---|
| [Index and schema dispatch](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), [artifact writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactWriter.cs), [optional slots](../../sources/Terrum.CodeAnalysis/Modules/TrRmOptionalSectionSlots.cs) | Finish new-selector admission throughout; retain the Standard strides and slot meanings, sorted unique directory kinds, independent counts and checked bounds. Do not select the Development branch or widen a stride to tolerate both payloads. |
| [Main producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [FNI producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.ForeignNativeInterface.cs), [frozen builder](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenScalarValueBuilder.cs) | Select Standard NativeImport independently of the pointer-load operation flag. Integrate enum/default/Body value builders into the single source/node/Blob row space, remapping if merged. Emit one section of each kind; removing the coexistence guard alone is insufficient. |
| [Structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs), [import codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeImportTable.cs), [source codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceTable.cs), [enum tables](../../sources/Terrum.CodeAnalysis/Modules/TrRmEnumTables.cs), [Body-map validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs) | Use the separate validated tables in every contextual check. Existing Standard import decoding is the baseline, not a new codec. Preserve complete enum/value/default validity and producer-truth versus structural-check boundaries; no missing table becomes empty success. |
| [Standard consumption](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [FNI source reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.ForeignNativeInterfaceSourceReader.cs), shared executable readers, [native binding plan](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs) | Carry the exact selection into all allowlists, declaration/Body loading, binding and native reconstruction. Shared loading currently gates `0021` on pointer-load selection; that operation gate must not decide this profile's table meaning. Keep original import descriptors/expectations and actual provider obligations. |
| Source-free writers/remappers, including [method source writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs) and [nominal row cloner](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodNominalTypeWriter.cs) where reused | Preserve separate NativeImport and ConstantSource row maps. Do not copy Development rows, infer source identities from integer bits, or discard uncalled imports/defaults/member sources to get an encodable result. Audit every reached schema dispatch and table lookup, not just final section emission. |

Meaning-preserving source-free packing/re-emission rebuilds Module partitions
and per-Module requirements, NativeImport owner/String references, binding
descriptors and original expectations; enum TypeDef/TypeSpec/Import origins
and member ranges; FNI owner/member/profile, Layout and Contract links;
PS1 owner/name/default references; source/node/Blob rows; and affected Body
maps, records, code/block offsets and SourceMaps. Rebuild variable-table
indexes and directory extents.
Optional row slots remap using their **target kind and original context**.
Preserve exact nominal types and payload bits; never substitute matching
`0030` octets inside values.

Canonical writers use the existing first-logical-use Body maps, reuse equal
typed constants, omit unused map entries, and order PS1 rows by owner/ordinal.
Global immutable value interning remains optional. Ordinary valid reordered
or duplicate immutable rows/maps remain readable. Preserve distinct original
expectations and clone contextual structural rows when needed. A destination
unable to retain the complete closure refuses publication. No conversion
from older/private schemas, source replay, selector restamping or debug-data
repair is supplied or required.

## Future acceptance and remaining decisions

These are required cases, **not results from this documentation task**:

| Family | Required result |
|---|---|
| Positive coexistence | One Module retains a Standard callable NativeImport, an FNI owner, nonempty enum/member sources, an enum Body constant and an eligible scalar PS1 default. Emit/read/source-free re-emit/reconsume with exactly one `0021/16`, `0030/8`, `003F/24` and Blob section as needed. Use unrelated synthetic declarations, not a recognized API. Preserve all original contracts and exact values. |
| Absence and independent Modules | Cover imports without sources, sources without imports, enum-only providers and empty enums with no value uses. Omit empty tables. Pack multiple Modules with separate import/value uses and manifests; neither a dummy FNI owner nor another Module's requirements repairs a missing obligation. |
| Negative exact selection | Never reinterpret `0030` as NativeImport, retry Development, or accept an explicitly wrong schema/provider context. Relabeling a Development payload is not conversion. Where coincident bytes are valid Standard source rows, interpret them only as sources; do not claim byte provenance detection. Enum defaults and unsupported foreign enum crossings remain excluded. |
| Malformed rows/framing | Reject duplicate kinds, wrong policy/stride, mixed row payloads, zero present counts, truncated/overflowing extents, invalid Standard import owner/mode/policy/convention/name/role, wrong-table/high-byte/zero required references, missing source/node/Blob tables, wrong enum T or width and missing using-Module closure before affected use. |
| Boundary and slots | Exercise independent counts and row endpoints `1/00FF_FFFF`, invalid `01000000`, checked `16*N` versus `8*N`, and Body-local `0000/FFFF` indexes under their separate limits. Row slots to both tables preserve kind and meaning; missing/out-of-range targets, nonzero row-slot block/ordinal/reserved fields, and Standard instruction slots with nonzero target kind reject. |
| Remapping and canonicality | Shift/reorder Module, MethodDef, String, enum/TypeSpec/Import, source/node/Blob and Body map rows so equal old row numbers need different mappings. Preserve distinct enum types with equal bits, shared equal typed values, original expected views and optional slots to both tables. Accept valid noncanonical map/value permutations; canonical output rebuilds all references/indexes without modifying opcode/ID-looking payload bytes. |

No allocation, row shape or discriminator decision remains open within this
recommendation. Formal approval of this clarification and the parent
PS1+EN1 bundle remains open; implementation and final-input qualification
are separate gates under the active hold. Wider compositions and historical
conversion are excluded, not prerequisites. Completing this decision does
not complete the full conformance plan.
