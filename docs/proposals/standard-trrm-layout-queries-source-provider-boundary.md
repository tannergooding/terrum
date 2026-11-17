# Standard TRRM LayoutQueries source/provider boundary

**Status: UNAPPROVED proposal; provisional direction only under the user's
authorized missing-contract workflow.** This is a recommended host-level
contract, not normative adoption, implementation evidence or milestone
acceptance. [Proposal index](README.md).

## Decision and scope

Extend the existing source TypeDef export mapping with **explicit field
export mappings**, and extend selected Standard reference construction with
**explicit type and field ExportKey selections**. Carry their checked
origins, original expectations and selected provider snapshots through source
binding, query emission and source-free consumption.

The initial boundary selects exactly
**`trrm/1-draft-layout-queries-1`**, including Feature **`13/1`** where required.
It admits declaration-only providers and providers retaining already-supported
Standard scalar/query Bodies; a provider need not contain a generic TypeDef,
NativeImport, address anchor or callable import. A consumer can bind a
nongeneric provider type and compile an already-supported safe query Body
whose TypeMap/FieldMap subjects retain that provider binding.

This closes missing **host publication/projection policy**, together with
the implementation handoff needed to honor it. It does not repair or replace
the accepted query wire contract. Keep the existing Windows x64, fail-fast,
closed nongeneric query execution boundary; adding imported subjects does not
admit arbitrary Standard Bodies. Broader supported metadata inspection remains
distinct from this source projection and execution scope.

### Preserved owners and intended integration

No accepted text, source production, wire assignment or schema identity is
superseded. The following owners remain authoritative:

| Owner | Preserved contract |
|---|---|
| [Registered queries](../specs/language/registered-queries.md), [logical records: registered query representation](../specs/representation/logical-records.md#registered-query-contracts-and-representation) | Complete subjects, source authorization, exact `nint` results, TypeIdentity and retained fixed/coupled observations. |
| [Instruction set](../specs/il-instruction-set.md#registered-query-lowering), [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Existing `DE` kinds, TypeMap/FieldMap, global FieldPath, FieldDef Import/Export, Layout and dependency records; no new query record. |
| [Module manifests](../specs/representation/semantic-manifests.md#2-governing-baseline-and-capability-registration) | Feature 13/1 has no unconditional Feature prerequisites. Declaration-only FieldDef Imports/Exports require it; map framing alone does not. Other retained facts keep their independent requirements. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md#fielddef-importexport-and-layout-path-obligations) | Resolve Module/key/kind and the owner before comparing the complete field/layout contract; retain every original expectation and stronger relied-on fact. |
| [Consumer verification](../specs/representation/reader-trust.md), [admission](../specs/representation/requirements-and-admission.md), [format versioning](../specs/representation/format-versioning.md) | Producer truth, mandatory safe decoding/support/binding checks, exact out-of-band schema selection and no compatibility fallback. |
| [Implementation support](../implementation.md#executable-trrm-scalar-interchange), [CG-005](../implementation/conformance-gaps.md#cg-005-accepted-layout-and-windows-x64-abi) | Actual support and remaining qualification, not approval of this candidate. |

This proposal adds LayoutQueries applicability to the unapproved
[source-schema API](standard-trrm-source-schema-selection.md#exact-public-library-contract)
and [type-reference projection](standard-trrm-source-copy-type-identity.md#proposed-api-and-source-selection),
reusing the [selected-provider input contract](standard-trrm-selected-provider-consumption.md#exact-proposed-public-api).
It does not supersede their other schema-specific scopes. A2 Copy evidence,
NominalEnumsDraft member keys, and
[G1/CG1 source export inputs](standard-trrm-source-generic-method-export-inputs.md)
remain separate decisions; sharing an API record does not compose schemas.

## Current implementation boundary

The following worktree implementation is provisional under the user's
authorized missing-contract workflow; it does not change this proposal's
unapproved status.

| Evidence | Boundary |
|---|---|
| [`TrRmSourceTypeExport` and `EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | `TrRmSourceFieldExport` and nested `FieldExports` now provide explicit direct-field keys. Exact LayoutQueries emission supports type-only providers and providers with admitted local query Bodies; it emits TypeDef/FieldDef Export rows and Feature 13/1 for FieldDef publication. |
| [`CreateStandardReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | The exact Standard five-argument overload selects TypeDef and FieldDef keys. Its current source projection preserves provider-owned nested TypeDef Imports through an explicit provider set and scalar/inline-struct layout closure; source values and unselected fields remain unavailable. A separate [reference-composition proposal](standard-trrm-layout-queries-reference-composition.md) now owns merging separately selected Module views. |
| [`LoadStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) | Provider TypeDef/FieldDef bindings are checked before the imported-query path loads the Body. Query-only Modules with type/field Imports no longer fall through the ForeignInvocation NativeAddress caller/anchor loader. The current source-free imported Body path is exercised through NativeJit. |
| [`RegisteredQueryBody`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.RegisteredQueryBody.cs) | The producer emits imported FieldPath steps as FieldDef Import references and imported root TypeSpecRefs through TypeDef Imports. The consumer reconstructs checked query facts from local stubs after provider validation. A `PairProvider -> LeafProvider` TypeDef Import chain is exercised for a direct Pair field query; a FieldPath through a separately selected Leaf field still needs reference composition. |
| [`FieldDef Import/Export tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFieldDefImportExportTests.cs) and [`TrRmExecutableCompilerTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmExecutableCompilerTests.cs) | The existing provider/FieldPath tests remain active. `StandardLayoutQueryBodyUsesImportedProviderLayout` now emits explicit source keys, creates the exact Standard reference, emits and loads the imported OffsetOf Body, and executes it through Windows x64 NativeJit. |

The current bounded slice does not establish nested FieldDef selection through
a separately selected Module, reference composition, broader provider graphs,
compatible/incompatible replacement, row remapping or retained dependency
re-emission; these remain acceptance work.

## Minimal alternatives

| Candidate | Assessment |
|---|---|
| **Nested field assignments on `TrRmSourceTypeExport` — recommended** | One owner selection, one existing emitter argument, atomic Module-wide key checking. The type mapping supplies the public owner key needed by this boundary; each named direct field supplies its independent explicit key. No new overload competes with the generic-method mapping overload. |
| Separate owner-qualified field-export list | Coherent if each entry carries the exact local type selector, field selector and key, with joint type/field validation. It permits independently managed lists, but duplicates owner selection and needs another overload/options container and cross-list agreement. There is no independent field-publication requirement here to justify it. |

An ordered array of keys alone, analogous to enum-member keys, is not selected:
this boundary can publish a subset of public fields, so each assignment names
its actual direct declaration. Neither alternative permits inferred keys,
source attributes, re-exporting expectations or resolving fields by layout.

## Recommended producer API and exact mapping

These are **UNAPPROVED C# API candidates**, not Terrum syntax. Preserve the
existing record's three positional parameters/deconstruction and its enum
member property; add one record and one member in
`Terrum.CodeAnalysis.Modules`:

```csharp
public sealed record TrRmSourceFieldExport(string Name, string ExportKey);

public sealed record TrRmSourceTypeExport(
    string Namespace, string Name, string ExportKey)
{
    public ImmutableArray<string> EnumMemberDeclarationKeys { get; init; }
    public ImmutableArray<TrRmSourceFieldExport> FieldExports { get; init; }
}
```

Reuse the existing five-argument `TrRmExecutableCompiler` overload:

```csharp
public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmSourceTypeExport> typeExports);
```

For exact LayoutQueries, apply these rules:

1. Resolve `(Namespace, Name)` once against the supplied checked program's
   original local declaration catalog. Use the complete decoded namespace
   (empty is valid) and simple decoded type name, ordinally without trimming,
   normalization, alias lookup or dependency search. Require exactly one
   positively source-authored, public, nongeneric inline struct with a complete
   Standard-representable declaration/layout closure. The initial query route
   retains its safe Blittable Sequential/Transparent target-layout restriction.
   Imported expectations, loaded publications, aliases and generated closed
   specializations cannot be relabeled as local source declarations.
2. Each `FieldExports` entry selects exactly one direct, public instance field
   by its decoded simple `Name` within that resolved owner. Retain its actual
   declaration handle and composed field ordinal before any rewriting.
   Inherited/name-search candidates, properties, static fields, private fields,
   wrong owners and missing/ambiguous declarations reject. This is a local
   selector, not cross-artifact identity.
3. Default or empty `FieldExports` means no new field-key assignments. Partial
   field publication is permitted; retain the **complete ordered field and
   Layout graph**, including private storage facts, independently of exports.
   Each type assignment supplies one TypeDef key; each field assignment supplies
   one FieldDef key. Empty/default maps never request implicit keys.
4. Keys are explicit, nonempty, NUL-free, well-formed scalar text in the existing
   Standard String domain. Compare decoded text ordinally without normalization
   or identifier interpretation; do not derive keys from namespace, name,
   ordinal, offset or another key. Reject repeated type/field selectors and
   repeated keys, even identical assignments. Keys must be unique across **all**
   output Module exports, including existing method exports. This bounded fresh
   source surface creates no aliases; already legal loaded aliases survive.
5. Capture submitted lists, records and arrays before processing, resolve them
   against that exact checked source snapshot, and carry the resolved handles
   through internal rewrites. Do not mutate caller inputs or the program.
   `EnumMemberDeclarationKeys` must remain default under LayoutQueries.
   If the separately proposed `GenericArity` member is present, require zero;
   this candidate neither introduces it nor enables CG1.
6. Emit the following existing rows, with no additional payload:

   | Assignment | Exact output association |
   |---|---|
   | Type mapping | `Export.localDefId = 0x1300_0000 \| localTypeDefRow`; `exportKey` names the supplied type key. |
   | Nested field mapping | `Export.localDefId = 0x1400_0000 \| localFieldDefRow`; that FieldDef lies in the mapped owner's checked field range; `exportKey` names the supplied field key. |

   Both declarations must be provider-local and public. Field publication adds
   Feature 13/1 to that Module even without a Body. Preserve every complete
   retained contract and all independently required Features, not just the
   mapped declarations or query-reachable fields.

Permit type-only libraries and libraries retaining already-admitted local
query/scalar Bodies through this same path. Do not impose the unrelated
fixed-array overload's no-functions condition or require a dummy query method.
Retain the program's admitted library/entry shape; mappings do not choose or
invent an entry.
Omission of mappings on fresh source requests no new type/field exports.
Omission on a loaded program cannot erase its retained publication map, and
new source mappings cannot rekey that map. Other schemas retain their existing
behavior and reject **nonempty** field assignments until separately selected;
the new property is not silently ignored.

This mapping supplies no Copy/Unique evidence. In particular, do not import
A2's `DeclaredCopy` bit into LayoutQueries or infer ownership/value-operation
permissions from Blittable layout, field membership or absent cleanup.

## Recommended consumer input and API

Reuse `TrRmStandardArtifactInput(SchemaIdentity, Artifact)` and its owned-byte,
exact-selection contract. Add this required-argument overload:

```csharp
public static CompilationReference CreateStandardReference(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    IReadOnlyList<string> typeExportKeys,
    IReadOnlyList<string> fieldExportKeys);
```

Initially the new overload accepts **only exact LayoutQueries selections for
every supplied artifact**. Extend the existing four-argument type-key overload
to LayoutQueries as the same operation with an empty field-key list; keep its
other schema behavior unchanged. The byte-array `CreateReference` default,
generic-nominal entry point and three-argument MethodDef-rooted reference
operation do not change defaults or become schema-probing fallbacks.

* `moduleIdentity` selects exactly one Module in `input`, not a decoder,
  filename, source namespace or default provider. Require at least one type
  root. Every type key selects a public provider-local TypeDef in that Module;
  every field key selects a public provider-local FieldDef whose exact owner
  is among those selected type roots. Reject missing/wrong-kind keys,
  expectations presented as exports and duplicate requested keys.
* Expose original declaration names for ordinary source lookup. Keys do not
  rename types or fields. Attach a selected field key to its checked actual
  FieldDef identity, never to a same-named or same-offset field in another
  owner. Full field metadata needed for layout does not itself select a field
  ExportKey or grant source access.
* A type-only selection supports admitted identity/size/alignment subjects
  without field keys. Every external field step used by an offset query needs
  its explicit selected field binding, or an already-retained exact FieldDef
  Import translated from the provider's context. A missing binding declines
  the affected compilation/emission; do not search names or synthesize a key.
* Reuse the type-projection owner's nominal-closure rule: provider-owned
  Imports retain their exact keys; a local nominal child crossing into the
  consumer needs a real public type export. Use an explicitly selected child
  key, otherwise its unique published key; missing/ambiguous publication
  declines rather than selecting by name, row or alias order. Closure-only
  declarations remain distinct from lookup roots and grant no private access.
* The key lists select exports in `moduleIdentity` only. To select a field
  declared by another Module in a nested path, construct an explicit reference
  to that Module with its owner/field keys and the same admitted provider
  closure. Provider presence alone does not expose all its fields. Combining
  references reconciles checked origins, not names or local type numbers;
  conflicting snapshots for one selected Module reject without an order winner.
* Distinct explicitly selected aliases to one actual declaration produce one
  source identity while retaining every key/view and their same-definition
  obligation. As in the type projection, the first explicitly selected alias
  can represent new uses only after equality is established; all other
  expectations remain checked. Equal-layout declarations in different Modules
  stay distinct, even with equal names and keys.

The projection contains the supported declaration facts necessary for these
queries, not fabricated construction, Copy, cleanup, value-call or field-access
capabilities. If the current host IR cannot preserve that distinction, fixing
its in-memory association is a prerequisite; layout-shaped value stubs are not
a successful reference.

### Selected-provider closure and retained handoff

`providers` is the complete explicit additional artifact set, not a resolver
catalog. Preflight every exact selector before decoding, snapshot every input,
and validate/admit supplied artifacts and their retained Module requirements.
Callers must not mutate collections or byte backing during capture; later
mutation cannot affect the completed result.
Keep one unambiguous Module directory: duplicate selected Module identities
reject even for equal bytes; co-located Modules need no resubmitted artifact.
No installed provider, private container, native DLL or default decoder fills
a missing edge.

Bind every retained TypeDef/FieldDef Import in the finite closure, including
provider-owned edges and unqueried expectations. Repeated graph visits reuse
the same snapshot, not a weakened union of expectations; each edge must pass
its own original contract. Dependency cycles do not authorize inline storage
cycles. This adds no special provider-count/depth limit; existing checked
capacity/resource failures remain failures.
This candidate adds no MethodDef/PropertyDef import admission: independently
admitted forms keep their own contracts, and unsupported retained imports
decline rather than selecting the foreign loader.

Carry an immutable compiler-owned association through `CompilationReference`,
source binding, BoundProgram, shared traversal/rewriting, verification and
lowering:

| Retained component | Required contents |
|---|---|
| Context and actual origin | Checked source/using context, context-local type/field handles, exact owned artifact/schema, Module and snapshot-local definition rows, selected export aliases, manifests and target/runtime requirements. Rows/digests identify locations or revisions, not a new nominal type constructor. |
| Original expected views | Original owner/field stubs and complete types/layouts, ModuleRef tuple, Module/key/kind, alias/equality relationships and provider-owned dependency edges. Keep these separate from current actual-provider facts. |
| Query and observation | Complete normalized subject/root, ordered exact owner/field steps, query kind/result domain/authorization, selected target, fixed or rebuild/rebind/invalidation basis and stronger facts actually relied on. |

Source emission obtains its provider closure from those checked associations;
it must not recover one from names, `CompilationReference.RuntimePolicy` or
coincident local indexes. Reusing a reference in another compilation remaps
context-local handles while preserving its owned snapshots and original views.
Host/Core registration remains separate; exports designate no Core authority
or authenticated/trusted producer.

Use the existing
`LoadStandard(input, moduleIdentity, providers)` API for source-free consumers.
Its exact LayoutQueries path must consume this admitted closure and pass both
type and field edges into query reconstruction, including provider-owned paths.
Do not route a query-only Module through F's NativeAddress caller/anchor
admission. Loading, comparing or projecting declarations does not execute
provider Bodies, initialize Modules or resolve native symbols. Independently
required unsupported facilities still decline.

## Import, replacement and remapping invariants

1. A consumer emits its own TypeDef expectations and FieldDef stubs, in the
   expected owner's contiguous field range, with separate TypeDef and FieldDef
   Imports selecting the original Module/keys. Preserve the captured ModuleRef
   target/runtime expectation; do not replace it with consumer defaults or a
   replacement provider's tuple. Expected declarations are not local exports
   or independently initialized storage.
2. TypeMap subjects resolve complete TypeSpecs through the selected TypeDef
   binding; a TypeSpec-row Body token uses kind `0x11`, not an invented raw
   Import/TypeSpecRef interchange. FieldMap uses the existing direct FieldDef
   or Import shorthand only for an exact one-field nongeneric owner; otherwise
   use `0x1D` FieldPath with a complete root and ordered global FieldReferences.
   Every imported owner/step needs its satisfied binding, including a direct
   DefId naming an expected stub. Kinds 3/4 acquire no extra TypeMap operand.
3. Resolve each path's immediate owner from the previous complete field type.
   Do not copy a provider's row into the consumer's numeric namespace. Preserve
   cumulative type-path versus terminal immediate-owner Place offsets, and
   reject static/non-inline transitions or unsupported generic substitutions.
   Preserve existing `0045` observations and exclusive ordered `004A` field
   ranges wherever retained, including after folding away a query instruction.
4. Compare a replacement against **all original views and stronger relied-on
   facts**: exact selected owner, field type/role/access, retained declaration
   facts, every complete Layout variant and ordered membership, target/runtime
   and surviving observation dependencies. Compatible row relocation can pass;
   same Module/key, equal layout or unchanged query bytes alone cannot. Do not
   overwrite old expectations with the replacement's declarations or split
   retained aliases. Invalidate affected cached bindings/layouts/derived code
   when their complete context changes.
5. Re-encoding resolves before interning and rebuilds Module partitions,
   Strings, Import/Export/ModuleRef, TypeDef/FieldDef owner ranges, TypeSpecs,
   Layout/TargetProfile references, FieldPath roots/steps and retained dependency
   ranges. Rebuild Body maps, `mapMask`, conditional counts, instruction/record
   indexes, extents and affected optional source/reference-slot data according
   to their schemas; never substitute matching bytes. Clone context-dependent
   shared rows where necessary.
6. A packed direct provider reference may replace an Import use only while all
   original expectations, equality and relied-on dependencies survive. Keep
   Import/stub records when no other admitted carrier preserves them. An
   unchanged-root byte replay is valid only for the unchanged root and binding
   context; it is not implementation of changed-program remapping.

Producers must publish truthful source/query facts. Consumers perform required
structural, bounds, reference, exact-schema/support and binding checks before
affected use; this candidate adds no mandatory semantic re-proof of provider
Bodies or external behavior. A key, snapshot or host association is not a
semantic trust certificate or unsafe authorization.

### Failure boundary

| Condition | Candidate host result |
|---|---|
| Null required object/list/entry | `ArgumentNullException` for a null top-level argument; `ArgumentException` naming its list for a null entry. |
| Invalid selector/key or ineligible mapping target, repeated assignment/selection, key collision, missing/wrong-kind requested export | `ArgumentException` naming the affected mapping/selection argument. |
| Malformed artifact/reference graph, ambiguous provider revision or incompatible binding | `InvalidDataException`. |
| Known but unsupported schema operation, required contract of an otherwise eligible declaration, Body/ownership/target obligation or unavailable required closure publication | `NotSupportedException`; do not drop the obligation to succeed. |

Keep the existing identity-selection and unassigned-target argument failures,
checked overflow/resource failures and explicit target requirement. Unknown,
altered-case or whitespace-padded schema tokens are not aliases. Return no
partial artifact/reference/program and never retry another schema or provider.
No diagnostic number or Terrum runtime failure category is allocated.

## Prerequisites and required acceptance cases

The accepted Feature 13 records and comparison rules are prerequisites already
assigned, not decisions reopened here. Implementation still needs checked
local field-publication associations, the nongeneric Standard reference
projection, retained type/field origins across source composition, and the
import-capable query loader/emitter handoff. Reuse existing layout registration
and complete provider comparison; do not replace them with a query-specific
catalog. Any retained unsupported ownership, constant, witness or target
contract remains an independent gate.

These are **future acceptance cases**, not tests run or passed by this
documentation change:

| Class | Required result |
|---|---|
| Source-to-source positive | Two unrelated synthetic Sequential/Transparent providers with keys unlike source names; both declaration-only and local-query-Body libraries. Emit explicit TypeDef/FieldDef exports, create the selected reference, bind consumer source, emit and `LoadStandard` the imported query without any generic type, NativeImport or anchor. |
| Query subjects | Imported size/alignment and existing TypeIdentity comparison/conditional shapes; exact one-field and nested nongeneric offset paths. Reconstruct complete identities/owners, preserve `nint`, zero-data distinctions and cumulative versus terminal offsets. No extra offset TypeMap association. |
| Provider closure | External and co-located Modules, repeated/diamond provider-owned imports, nested paths whose child field is explicitly selected from its declaring Module, and unqueried retained expectations. Every edge retains its original Module/key/owner and requirement tuple. |
| Mapping/selection negatives | Missing/ambiguous/imported/nonpublic/generic owners; wrong/private/static fields; invalid Unicode/NUL/empty keys; duplicate selectors/keys and type/field/method collisions; missing/wrong-kind field key or unselected owner. No implicit publication, field discovery or partial success. |
| Binding negatives | Same-named/equal-layout different providers, wrong owner/field ordinal, changed field type/flags/access, missing Layout variant/member or provider, direct-stub import bypass and discontinuous/non-inline paths all fail affected use. |
| Replacement/originals | Compatible provider row relocation succeeds. Lost packing/alignment/Blittable commitments, changed fixed observations, alias splitting or a provider satisfying only a weakened replacement-authored view fail. Recheck every original view, including stronger facts used by folded results. |
| Remapping and bounds | Source-free re-emission and nontrivial row/map permutations preserve imports, FieldPaths, `0045`/`004A`, aliases and source slots; truncated/wrong-kind references, map/index limits and checked capacity failures reject. Later input-list/byte mutation cannot change retained results. |
| Exact schema and cross-feature | Keep Development defaults and their mismatch distinct from malformed Standard. Reject F/A2/L1/NominalEnums/G1/CG1 compositions on this new route rather than guessing; preserve their existing export APIs. Reject enum-member payload/nonzero generic arity here. Feature 13 is present for field publication with no Body, but common map framing or ordinary fixed-count constants alone do not force it or native/A2 Features. Existing generic metadata/path inspection does not become generic Body support. |
| Trust and execution | Source checking establishes authored authorization; structural/provider tests do not claim semantic re-proof. A separate final-input, headless supervised Windows x64 query execution case must consume the loaded provider-bound Body before runtime support is claimed. Emission or the recorded failing regression is not that evidence. |

No further in-scope policy is delegated to implementation by this candidate.
Actual Module identities, keys and selected snapshots remain explicit host
inputs. Formal review/approval, implementation and the applicable acceptance
evidence remain outstanding; inventories should change only with that evidence.

## Explicit non-approvals

This proposal does **not** approve generic/open Standard Bodies, broader query
control flow, unsafe/non-Blittable query encoding, execution-dependent layout,
Count-witness publication, new constant/value codecs, general field
load/store/address/initialization execution, ownership/value ABI admission,
native data/symbol binding, AOT or other targets. It adds no wire schema,
Feature, ID, opcode, alias, source syntax, Core/provider default, trust flag,
compatibility reader, private persistence, CLI mapping/selection transport or
live replacement/unload protocol. All remain separate contracts or gates.
