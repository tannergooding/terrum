# Standard TRRM A2R1+Repeat source Copy type composition

**Status: unapproved candidate; provisionally actionable only under the
user's explicit missing-contract workflow.** This selects a bounded
operation-support extension, not normative adoption, new wire assignments,
implementation completion or execution qualification. [Proposal index](README.md).

## Decision and scope

Recommend extending the explicit export-key, source-root and retained
origin/expectation policy in [A2 source Copy type identity](standard-trrm-source-copy-type-identity.md)
to the **existing exact A2R1+Repeat selection** below. Permit those imported
types in the **already admitted F NativeParameter/NativeAddress Body paths**.
Keep publication type-only: a method-bearing consumer can use an imported
type without exporting it or giving it a new local identity.

The missing decision is this source/API operation's exact-schema coverage,
not a new nominal equality, TypeDef Import encoding, native pointer protocol
or Repeat composition. The A2 source proposal explicitly limits nonempty
export/root arguments to A2 and says other exact schemas decline.
The [Repeat owner](standard-native-fixed-array-repeat-initialization.md#exact-standard-successor)
already permits unchanged type-only providers, but does not itself extend
that source API policy or complete its imported-type Body preservation.
Partial implementation branches do not approve that extension.

This proposal intentionally replaces **only that A2-only operation limit
for this one additional exact selection**, reusing the existing public
overloads and all their key/origin rules. It does not amend the original
proposal's text or approve its other candidates. The newly admitted
publication/root types remain public, nongeneric, ordinary Copy inline
structs with complete A2-admitted Blittable Sequential/Transparent storage
closure. No new raw-Union export policy, by-value struct/array formal or
result ABI, constructor/member API, arbitrary Body operation, private
persistence, runtime policy or CLI transport is selected.

## Authority and observed boundary

The [specification index](../specs/README.md) leads to the preserved owners:

| Owner | Preserved obligation |
|---|---|
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [complete types](../specs/language/types-and-normalization.md), [binding/replacement](../specs/representation/binding-and-replacement.md) | Resolve Module/key/kind before comparison; retain complete using contexts, original expectations, alias equality and stronger relied-on facts. Equal names, rows and layouts do not establish identity. |
| [Versioning](../specs/representation/format-versioning.md), [admission](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md) | One exact schema/catalog per artifact; separate explicit dependency selections; each Module's complete before-use requirements. |
| [Consumer verification](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Mandatory structural/support/binding checks, producer truth, schema-owned references and semantic re-encoding rather than byte substitution. |
| [Native boundaries](../specs/native.md), [Windows layout](../specs/targets/windows-x64-data-layout.md), [Windows calls](../specs/targets/windows-x64-call-interface.md) | Exact interoperable layout and pointer identity; caller-unsafety, actual backing/access/extent and code lifetime remain independent obligations. |

These are source observations, **not newly executed tests**:

| Inspected source | Relevant boundary |
|---|---|
| [`TrRmArtifactIndex`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | Already has the exact Repeat selector and its Copy, array, F/storage, scalar, CopyOrigin and ReturnCoverage schema facts. No new selector is needed. |
| [`StandardTypeReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | Its public root-selection preflight lists Repeat, but the inner type-reference schema gate and parts of `RetainLoadedA2SourceTypeFacts` omit it. Recognizing the token is not complete projection/preservation support. |
| [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) | `LoadStandardProgramCore` does not dispatch Repeat to the type-provider validator. Its bounded-F Import gate consequently cannot consume those TypeDef Imports. |
| [`TrRmFixedArrayProviderBindingValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs) | Already recognizes the exact Repeat schema and compares TypeDef/import chains, complete fields/layouts and pointer anchors. Reuse its checked relation; do not bypass the missing dispatch by changing schema. |
| [`FixedArrayCopyWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.FixedArrayCopyWriter.cs) | `CreateSourceTypeImportPlan` maps the Repeat destination to an A2-only provider/reference-root expectation. That shortcut does not preserve independently selected Repeat origins. |
| [`NativeAddress`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs) | Already selects Repeat, constructs imported TypeSpecs from an import plan, and emits F contracts/anchors/Bodies. Local-type support does not establish the complete imported-type route. |
| [`A2 provider-chain tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmA2SourceTypeProviderChainTests.cs), [`NativeAddress emitter tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs) | Contain A2 alias/original/provider-chain and source-free type tests, Repeat native Body tests, and an imported-pointer test intent. Their source is not evidence that the combined route passes; the schema/origin gates above still require integration. |

The [support inventory](../implementation.md) separately distinguishes the
bounded A2 type-only route from complete Body/provider execution. This
proposal changes no support inventory or qualification claim.

## Alternatives

| Direction | Assessment |
|---|---|
| Keep the source identity route A2-only | Coherent if Repeat consumers continue to reject imported Copy TypeDefs. It preserves a small support surface but leaves the requested independently supplied type/provider plus Repeat/F Body route unavailable. Re-emitting it as A2 would lose retained meanings, not solve the boundary. |
| **Extend exactly A2R1+Repeat — recommended** | Reuses the existing exact schema, source-key API and comparison/carrier rules. Requires end-to-end admission, origin retention and Body remapping, but no new serialization or call ABI. |
| Enable every A2-derived selector, or introduce a new composition/Feature | Not selected. An inheritance test or common row shape does not establish complete catalogs or operation support. The required Repeat composition already exists; another schema/ID would duplicate rather than complete this decision. Other separately authorized operations remain unchanged. |

## Exact selection and complete catalog

Use exactly the existing case-sensitive out-of-band identity:

**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1`**

Its existing internal selector is
**`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraft`**.
“A2R1+Repeat” below is document notation, not an API alias or string-composition
algorithm. In particular, the distinct token ending in
`copy-origin-return-coverage-1` **without Repeat** is not this selection.

Preserve the complete closed composition already specified by these owners:

| Component | Exact selected content |
|---|---|
| B and F | [Foreign-invocation schema composition](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities): `trrm/1-draft-layout-queries-1` plus the complete F delta, its bounded predicate catalog, NativeImport/anchor applicability, NativeParameter and raw-output protocols. |
| S and A2 | [Storage addresses](standard-native-storage-address-formation.md#exact-schema-and-capability-selection), [array identity/shape](standard-trrm-fixed-array-identity-shape.md#exact-schema-allocations-and-capability), [positive Copy evidence](standard-trrm-rootless-copy-type-evidence.md): EC/0E and assigned initialization, structural arrays/layout/places and TypeDef `DeclaredCopy=0x20`. |
| E, U and SF1 | [Empty initialization](standard-trrm-empty-fixed-array-initialization.md), [unit initialization](standard-trrm-unit-fixed-array-initialization.md), [A2EU scalar composition](standard-trrm-a2eu-scalar-literal-comparisons.md#exact-combined-selection-and-manifest): their bounded Body meanings and the complete 24-scalar frozen-value codec/catalog, not a ten-type replacement codec. |
| C and R1 | [Raw-Union composition](standard-trrm-raw-union-initialization.md#exact-successor-and-allocation), including its full [CopyOrigin](reference-copy-origin-trrm.md) delta/applicability amendment, P/G/D semantics and ReturnCoverage. |
| Repeat | [Repeat transport and semantics](standard-native-fixed-array-repeat-initialization.md#exact-standard-successor), including its same-schema field/path refinements and linked [resource recipe](fixed-array-repeat-resource-accounting.md) and [root-prefix completion](fixed-array-repeat-root-prefix-selection.md). |

This adds no later unrelated proposal through a transitive link and does not
reconcile different schemas by their common rows. The complete agreed
schema and governing chapters, not just this token or a registry digest,
remain the exchange identity. No wire/catalog amendment is made here.

Governing edition remains `1`. The selected catalog is:

| Capability, exact revision 1 | Immediate prerequisites and preserved scope |
|---|---|
| `1 NativeBoundaryContracts` | `[]`; declaration/binding contracts, not Body execution by itself. |
| `2 RootlessNativeValues` | `{1}`; includes A2's positive Copy and S's pointer/storage obligations. |
| `3 DirectNativeCalls` | `{1,2}`. |
| `4 NativeExportEntries` | `{3}`; actual native function addresses, indirect calls and code origins retain this closure. |
| `5 NativeInterfaceCalls`, `6 NativeDataImports`, `7 NativeExceptionUnwind` | Respectively `{1,2}`, `{1,2}`, `{1}`; only their independently retained uses trigger them. This decision adds none of those execution paths. |
| `8 NativeOutputCells` | `{1,2,10}` under F's fixed cell protocol. |
| `10 PublishedPredicates` | `[]`; F's six bounded phase-3 result selectors, not general predicate graphs. |
| `11 TypedFrozenValues` | `[]`; SF1's complete closed 24-scalar registration. No ConstantExecution or aggregate/pointer/enum frozen codec follows. |
| `13 LayoutQueries` | `[]`; its own field/path/query and conditional target obligations remain. |
| `15 CopyOriginContracts` | `[]`; retained CopyOrigin facts/expectations require it, with 13 when their paths/binding use it. Preserve R1's explicitly amended applicability. |
| `16 ForeignInvocationContracts` | `{1}`; every retained NativeParameter and the complete F invocation projection. |
| `20 FixedInlineArrayStorage` | `{2}`; every retained structural array, layout or intrinsic selection, including nested/imported declaration facts. Repeat uses this existing capability. |
| `22 RawUnionValueCoverage` | `{2}`; R1's coverage/value uses and initialized-backing uses that rely on its raw-Union model, including retained expectations. A type-only Copy declaration alone does not trigger it. |
| `9`, `12` | Still registration-pending, not made executable. Preserve 12's inherited `{10,11}`, not G's `{11,17}`. |
| Other candidate capabilities | No RootPin 14, general graphs 17, enums 18, ImmutableData 19 or exception-region 21 composition is selected. |

Compute each Module's least exact closure from **all retained content and
dependencies**, including unused declarations/records and uncalled Bodies.
Selecting this schema does not require every Module to list every available
Feature. An ordinary imported Copy pointer plus NativeParameter, E9 and
indirect call needs effective `{1,2,3,4,16}`; retained array fields add 20.
Actual frozen values, CopyOrigin, coverage, fields/queries and outputs add
their own requirements. Another Module's manifest cannot supply them.

Keep `(1,0)`, codec `01`, all framing/widths, TypeSpec `11`, section `0012`,
Contracts `000D/000E/000F`, E9/Body `0D`, EC/Body `0E`, DF/Body `0C` and
record `05`'s selected RepeatCopy source-use 6 unchanged. Those equal numbers
in different namespaces are not interchangeable. No allocation, alias,
fallback, on-wire discriminator or per-Module decoder is introduced.

## Source roots, provider closure and original expectations

1. Extend the existing `EmitStandard(..., typeExports)` and
   `CreateStandardReference(..., typeExportKeys)` operations for this exact
   selection. Keep the bounded type-only publication/reference route;
   this is not permission to export arbitrary mixed method/type libraries.
   The existing MethodDef APIs and their operation restrictions do not change.
   An imported type may subsequently be used in an independently admitted
   method-bearing A2R1+Repeat consumer without a new export map.
2. Apply the A2 owner's exact producer-chosen keys, declaration selections,
   visibility/kind/Copy checks, explicit nominal closure and lookup-root
   policy unchanged. No key from a source name, implicit export, new alias
   authoring surface, FieldDef export or imported-stub re-export is added.
   Closure-only declarations supply type/layout facts, not source lookup or
   private field access.
3. Snapshot and preflight **every supplied artifact under its own exact
   selection**, then admit its required Module/provider closure. Required
   cases for this addition include A2R1+Repeat type providers and independently
   selected A2 type-only providers carrying unchanged supported Copy/layout
   facts, including chains mixing those selections. Neither is decoded as
   the other. This does not grant new operation support to other profiles
   or withdraw their separately supported operations. Unsupported retained
   contracts decline; no automatic A2-family wildcard or requirement that
   dependencies select the root's schema is allowed. Preserve existing
   duplicate-input rejection.
4. Bind each TypeDef Import in its owning/using Module to the selected
   provider Module/key and actual public local TypeDef. Check actual
   positive Copy evidence and complete ordered names/fields/types, every
   retained Layout variant, target/profile and ModuleRef requirements.
   An expected Copy bit, equal layout or absence of Cleanup is insufficient.
   Provider-owned imports remain in that provider's context; do not rebind
   them in the consumer by name.
5. Retain the immutable original expected declaration/type/layout graph,
   original ModuleRef target/runtime tuple and every selected key, separately
   from the actual provider snapshot and stronger facts actually relied upon.
   Carry the A2 projection through references, binding/type-table composition,
   the BoundProgram, shared traversal/rewriting, verification and lowering.
   Loading an executable with imports must reconstruct these associations,
   not omit them because the program has functions or the schema has Repeat.
6. Preserve all original views and alias equivalence classes. Multiple keys
   for one actual definition yield one nominal source type, not one type per
   key. Distinct views retain distinct expected stubs; every retained key
   must still select that same actual definition. Check Import **triple**
   uniqueness, not expected-definition uniqueness. Retain the A2 owner's
   transport representative and explicit child-alias selection rules; no
   first-provider, row-order or arbitrary child-key choice is added.
7. Imported uses in the consumer encode through their TypeDef Imports and
   original expectations, not fresh local declarations or exports. Re-encoding
   cannot overwrite originals with the replacement's current graph/tuple.
   If a stronger actual Copy fact was additionally relied upon, preserve
   the A2 owner's additional positive expectation rather than changing the
   original view.

The same Module text cannot select conflicting revisions through separate
references. Cache keys and graph comparison contexts retain exact schemas,
owned snapshots, catalog/rule/target/runtime context and binding generation.
Snapshot-local rows are not new nominal constructors or stable identity keys.
The existing API failure categories apply: invalid arguments, malformed or
incompatible bindings and unsupported operations remain failures, with no
partial reference/artifact, silent omission or retry against another schema.

## Pointer and Body integration

An illustrative **proposal-only integration shape**, not a qualified runnable
example, is a public `Container` exported by a type-only provider and explicitly
selected as a source root. Its `Pair`/leaf/array closure can remain lookup-hidden.
A consumer can name `ptr<Container>` in an already admitted pointer-input path,
form the address of a correctly declared raw native observer, and pass that
pointer under the observer's exact Read/NoEscape/minimum-element contract.
It cannot instead name a closure-only `Pair` as a new root, pass `Container`
by value, or treat a same-shaped `fnptr` as having the observer's protocol.

Preserve these independent mappings:

| Layer | Required integration |
|---|---|
| Complete types | Remap imported nominal uses inside raw pointer/pointee types, nested arrays, MethodSig/Param and every anchor signature. Preserve each constructor, pointer depth, access qualification, count, actual origin and original expected view. Derive geometry from the selected exact layout, not local type numbers. |
| NativeParameter `000E` | Remap its MethodDef owner and owning Contract range. Its zero-based formal ordinal still selects the corresponding complete signature formal; it is not a global row. The row contains **no independent pointee TypeSpecRef**. Derive/cross-check the pointee through that formal, including imported identity. Access, NoEscape, extent kind and element-count quantity do not become remappable indexes. |
| Function-pointer view | Preserve the complete F normalized signature/protocol, actual foreign convention, effects and any independently retained selectors/states/dependencies. Apply F's exact foreign-protocol equality to target/view formation and transport. A layout-compatible pointee is not an equal contract. |
| E9/Body `0D` | The MethodMap selects the actual eligible NativeImport or its checked imported MethodDef expectation. The TypeMap selects the complete pointer view and its noncallable signature anchor. A TypeDef Import supplies pointee identity, never the callable target or native symbol. Keep code/provider origins separate from structural pointer identity. |
| Body maps and records | Remap every affected HomeMap/TypeMap, MethodMap, FieldMap/FieldPath, ConstantMap and entry-state type/reference in its own namespace. Preserve captured target/argument order, storage generations, backing facts and existing CallSite preparation/publication. No new Body form is admitted merely because its types can now bind. |
| EC/storage and Repeat/coverage | Where already admitted, preserve exact local/subplace authority, array element identity/layout, requested suffix checks, seed snapshots, RepeatCopy completion and retained CopyOrigin/ReturnCoverage. Importing a type neither initializes backing nor grants new construction or raw-Union operations. |

NativeParameter Write/ReadWrite still needs initialized, appropriately writable
backing, not a vacant output cell. Minimum extent counts **elements**, not
bytes; no-static-minimum proves no region. Reject known insufficient extent,
bad alignment, invalidation, readonly writes or missing required provenance.
Zero-size types/zero-count arrays gain no positive-extent native address.
Preserve explicit unsafe formation/invocation and any published caller-unsafe
requirement; an unsafe declaration still does not authorize its body's
operations. Do not invent marshalling, pinning, rooting, ownership, retention,
default backing or lifetime/code-origin extension.

## Mandatory checks, replacement and trust

Before affected use, validate selected framing/counts/extents, required
sections/manifests, namespaces/ranges, owners and exclusive ranges, actual
export kinds/visibility, TypeSpec/anchor structure and all required provider
bindings. Reject wrong-context imports, exported expectation stubs, conflicting
Module selections, absent/ambiguous closure keys, malformed type/layout cycles,
missing Copy evidence and unsupported retained contracts. Validate the F
anchor/NativeParameter owner, formal, mode, pointer/access/extent and complete
target/view relationship. Neither a known schema enum nor a successful local
Body load is a certificate for this imported route.

A replacement must satisfy **every original expectation and stronger relied-on
fact**. Compatible TypeDef/Import/layout row relocation may succeed. Lost Copy,
changed retained declaration/field names or order, complete pointee types,
layout variants/packing/alignment, ModuleRef target/runtime requirements,
foreign protocol or alias splitting must not pass through equal geometry.
In particular, changing an intermediate provider's import to an equal-looking
definition in another Module changes identity. Invalidate affected binding,
layout and native-derived caches; this adds no live replacement/unload policy.

Producers establish truthful source ownership, Copy/publication, Body state,
unsafe authorization and provider facts. Consumers perform the structural,
support and complete binding checks required for safe selected consumption;
they are **not required to semantically re-prove producer-published Body facts**.
A structurally consumable false semantic assertion remains a producer violation,
not a promised consumer diagnosis. Optional audits and host authentication
remain separate. No trusted origin flag or re-verification of source text is
added to repair missing carriers.

## Source-free re-encoding

Reconstruct checked semantic/type graphs from the selected artifacts and
providers without original source/debug text, producer BoundProgram instances
or a same-process reference object. Preserve the required original views and
actual-origin associations in the reconstructed program and output.

Rebuild Module partitions and requirement ranges; Strings; TypeDef/FieldDef,
Export/Import/ModuleRef and expected stubs; every TypeSpec child; MethodSig/Param,
MethodDef/anchor and Contract owner/range references; Layout/LayoutField/
TargetProfile/FixedArrayLayout; and retained CopyOrigin paths, ReturnCoverage
profiles/ranges and scalar ConstantSource/node/Blob references. Preserve
quantities and exact value bits as data. Resolve identities before interning;
clone shared structural rows when their meaning differs by using context.

Rebuild Body-local maps and all actual uses, including TypeSpec-kind-`11`
TypeMap tokens rather than non-Body TypeSpecRefs, pseudo bases, Home indexes,
entry states, records, native address target/view indexes, CallSites and
field/array paths. Recompute mapMask/count words/prefixes, record indexes,
instruction forms/ordinals, Block offsets/extents, Body/section offsets and
optional source/reference-slot coordinates. Regenerate or legitimately drop
optional source data; never use it to carry required semantics.

Packing/co-location still requires one explicitly selected destination schema
preserving all retained Modules and meanings. A direct reference may replace
an Import use only while its original expectations, alias equality and stronger
dependencies remain represented; otherwise retain the expected/import records
or decline. A2 cannot retain Repeat or ReturnCoverage meanings by relabeling
or dropping Features; shared foreign/type rows do not prove a conversion.
Canonical writers rebuild first-use maps; ordinary readers retain acceptance
of equivalent valid noncanonical orders/duplicates. Equal re-emitted bytes
are not the only required evidence.

## Acceptance and integration gates

These gates define the complete acceptance scope. Current provisionally
implemented cases and remaining evidence gaps are tracked in the
[conformance inventory](../implementation/conformance-gaps.md#cg-001); that
status does not change this proposal's unapproved state.

| Gate | Required result |
|---|---|
| Explicit type-only publication/reference | A2R1+Repeat public Sequential/Transparent Copy roots with arbitrary producer-chosen keys and required nominal/array closure emit, project and re-emit. No methods are required in the provider and no implicit keys/exports appear. |
| Imported pointer Body | A genuinely visible imported root is used by a supported NativeParameter/NativeAddress Body. Inspect the nominal Import inside signature/anchor/TypeMap reconstruction and its exact original/provider association, not just a nonempty map. No by-value struct/array ABI is used to make the test pass. |
| Independent providers | Exercise exact A2 and exact A2R1+Repeat type-only providers, multi-edge chains, private field facts, closure-only lookup and unrelated synthetic types. Missing/ambiguous children and same-shaped foreign identities do not bind. |
| Aliases and original views | Preserve multiple selected keys, distinct expectations, same-definition relationships and original ModuleRef tuples through source binding, loading and re-emission. Replacement satisfies all views, never just a newly captured actual provider. |
| Structural/contract negatives | Reject wrong namespaces/owners/ordinals/kinds, out-of-range references, malformed layouts/anchors/clauses, missing required Features/Copy, incompatible pointer qualifiers/protocols and unsupported retained semantics before affected use. Distinguish these from unproved producer truth. |
| Schema isolation | Reject unsupported requested operations rather than select A2, A2R1-without-Repeat, L1, A3, G, enum, RootPin, E1 or private decoding. A common-subset image is judged under its explicit selection, not rejected merely because another schema could encode those bytes. |
| Semantic re-encoding | Use non-byte-identical, consistently permuted global declarations/imports/layouts/anchors and Body maps/records, colliding context-local type numbers and shared recipes. Load/re-emit/load without source and preserve all originals, quantities, bits and Body/native meaning. Include separately supplied and co-located providers. |
| Replacement | Compatible provider relocation succeeds after remapping. Lost Copy, changed fields/layouts/target/runtime, intermediate nominal substitution, alias splitting and changed NativeParameter protocol fail against retained originals. |
| Existing behavior | Preserve A2 type-only source routes, local-type Repeat Bodies, existing F target selection and explicit rejection of unsupported value/formal/Body operations. No defaults, publication maps or other exact selectors silently widen. |
| Execution | Qualify actual imported-type native accesses after source-free loading and non-byte-identical rewriting, before and after compatible provider relocation, under the existing Windows x64 FailFast policy. Require both JIT and supervised headless AOT observations, not emission alone. |

Runtime fixtures must use a correctly declared real native target and stable,
initialized, target-correct backing. Assert actual values/mutations and
untouched neighboring storage, access/extent behavior and preserved target
selection through an independently admitted pointer-input or storage-address
harness. An unavailable harness is a separate execution prerequisite, not
permission to invent a by-value ABI or misdeclare an OS symbol. No visible
window/hardware opt-in, new runtime policy or CLI route is required here.

Attribute qualification to the final relevant patch, selected configuration,
compiler/companion input hashes, exact root/provider selections, artifacts
and actual assertions/results. Confirm nonzero intended test selections;
skips, crashes and incomplete runs are gaps. Metadata/binding, PE emission,
JIT and supervised AOT establish distinct claims.

Integration belongs in the existing Standard producer/reference/consumer
dispatch, origin carriers, shared type/Body remappers, provider comparator
and native binding plan. All relevant exact-schema gates must agree; adding
only one enum case is insufficient. The decision above supplies provisional
direction now; approval, implementation and these qualification gates remain
outstanding. No further schema/ID, key-default, ABI or CLI decision is needed
for this bounded addition.
