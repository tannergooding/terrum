# Standard TRRM A2 source Copy type identity

**Status: unapproved candidate; provisionally actionable only under the
user's explicit missing-contract workflow.** This recommendation does not
approve a source/API contract, change an accepted specification, establish
implementation support or qualify execution. [Proposal index](README.md).

## Finding and bounded decision

The missing contract is **source publication and its in-memory hand-off**,
not the meaning of nominal identity or the expected/provider comparison.
Recommend explicit source-declaration-to-export-key mappings, explicit type
export selections for source references, and a compiler-owned immutable
origin/expectation projection carried with those references and bound types.
Do not derive an export key or an imported origin from a qualified name.

Scope this completion to public, nongeneric, ordinary Copy inline structs
with complete A2-admitted Blittable Sequential/Transparent declaration and
storage contracts, including their required nominal and fixed-array closure.
It is a type-only publication route, not a new value, constructor, field
instruction or call ABI. Unsupported retained contracts still decline.
Structural arrays have no nominal TypeDef of their own and acquire no
independent type export through this proposal.

Preserve exactly the provisional A2 selection
**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`** and
**`DeclaredCopy=0x20`** from
[rootless Copy evidence](standard-trrm-rootless-copy-type-evidence.md).
This fills producer/reference API policy within existing permitted records;
it changes no A2 wire shape, semantic flag meaning, schema identity, Feature,
row allocation or version. A1 is not an alias or fallback. The separately
selected [A3 generic-storage](standard-trrm-closed-constructed-array-elements.md)
and [positive-Unique](standard-trrm-a3-positive-unique-type-evidence.md)
candidates retain their own scope and exact selection.

## Authority and verified gap

The [specification index](../specs/README.md) leads to these accepted owners:

| Accepted owner | Preserved contract; what it does not decide |
|---|---|
| [Names and binding](../specs/language/names-and-binding.md), [complete types](../specs/language/types-and-normalization.md#complete-type-terms) | Referenced declarations retain their Module ownership. Source spelling, rows and equal layout do not establish nominal identity or export keys. |
| [IL metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [identity invariants](../specs/il-metadata.md#identity-and-contract-invariants) | Export names a public local definition, not an expectation. Module/key equality is ordinal decoded-scalar text without normalization. Producers choose keys; stable-key generation is explicitly open. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md) | Identity precedes complete comparison. Retain every original expectation, equality/ownership relationship and stronger relied-on fact. Export aliases do not create types. No source authoring API or ordinary-struct origin carrier is assigned. |
| [Layout and ABI](../specs/data-layout-and-abi.md), [Windows layout](../specs/targets/windows-x64-data-layout.md) | Copy, Blittable, layout and access are independent. Complete inline representation includes required private field facts, without granting private access. |
| [Admission](../specs/representation/requirements-and-admission.md), [consumer trust](../specs/representation/reader-trust.md) | Producer truth; mandatory structural/support/binding checks; no mandatory semantic source/Body re-proof. |
| [Versioning](../specs/representation/format-versioning.md), [persistence](../specs/representation/persistence.md), [Modules](../specs/modules.md) | One exact schema per artifact, independently selected dependencies; private formats/images are not Standard identity carriers. |

Accepted TypeDef flags contain visibility/kind only. A2's Copy bit and
array forms remain unapproved physical candidates, not accepted metadata.

Related **unapproved** proposals supply adjacent provisional directions:

* [A2 Copy evidence](standard-trrm-rootless-copy-type-evidence.md#nominal-binding-replacement-and-re-encoding)
  supplies Copy and original/actual preservation, not source TypeDef keys
  or a concrete reference association.
* [Source schema selection](standard-trrm-source-schema-selection.md#exact-public-library-contract)
  selects the destination and requires complete origins/publication, not
  declaration-key formation.
  [Selected-provider consumption](standard-trrm-selected-provider-consumption.md#exact-proposed-public-api)
  specifies its initial MethodDef/F surface, not this A2 type projection.
* [Core provider Module-origin carrier](core-provider-module-origin-carrier.md#finding-and-selected-decision)
  attaches its host projection to `NativeCoreRegistrationSet` for enum/schema
  designation, not ordinary struct references or source exports. Shared
  machinery is appropriate; ordinary imports neither require nor grant Core.
* [Private fixed-array metadata](native-fixed-array-private-metadata-boundary.md#decision-and-owner)
  defers type-only publication in revision 100; this proposal preserves that
  private boundary.

Current source observations, **not newly run tests or approval**:

| Inspected implementation | Relevant boundary |
|---|---|
| [`EmitStandard`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`A2 declaration writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.FixedArrayCopyWriter.cs) | Emits local Blittable Sequential/Transparent TypeDefs, Copy and field/type/layout metadata, including local struct/nested-array relationships. No `0x001D` Export section or type export-map input. |
| [`StandardConsumption.CreateStandardReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) | Projects MethodDef exports, returns empty Structures and rejects without a supported function export. No type-only reference projection. |
| [`A2 type-library reconstruction`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs) | `LoadForeignInvocationFixedArrayTypeLibrary` rejects Imports/ModuleRefs; reconstructed structs/arrays retain no Module/export/expectation directory. |
| [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`BoundStruct`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Name/local-number binding of fields, geometry, visibility and ownership lacks the ordinary-struct association to selected provider Module/key/original graph. Aggregate RuntimePolicy is not per-provider origin. |
| [`BoundGenericNominalTypes`](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericNominalTypes.cs) | Has provider/Module/TypeDef identities, including nongeneric arguments, but no association from ordinary BoundStruct to export binding, original A2 expectation or publication map. Reuse those checked primitives; the ordinary-struct association remains missing. |
| [`TrRmFixedArrayProviderBindingValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs) | Already checks exact providers/TypeDefs, Copy, names, ordered fields, all Layout variants/geometry, provider-owned import chains and function-pointer anchors. Its binding records are not source exports or the missing reference projection. |
| [`Import`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs)/[`Export`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs) readers | The one-binding-per-expected-definition dictionary and unique-key-per-definition helper cannot implement all retained aliases unchanged. These are implementation restrictions, not new identity rules. |

Thus there is a genuine undecided authoring/carrier completion, alongside
implementation gaps in already selected preservation obligations. This does
not propose a second nominal equality or provider satisfaction relation.

## Alternatives

All selections in this section are unapproved.

| Direction | Assessment |
|---|---|
| Default every public type to its qualified source name | Not selected: comparing this alternative does not authorize a name-derived default. It offers the smallest API change, but requires a new composition/collision convention across all export kinds, couples keys to source names, misses arbitrary provider-assigned keys and still needs origins. This proposal supplies no such convention. |
| **Explicit mapping/reference keys — recommended** | Select real local declarations and published keys independently of source lookup. More API input, but no new grammar, Core schema, default key convention or fallback. |
| Source export attribute/syntax | Requires syntax/schema identity, placement, repetition, constant evaluation and library ownership. Unnecessary here; native export annotations are not semantic type exports. |
| Universal reference/Core graph redesign | Shared checked origin machinery is useful; general generic/witness/owner/Core registration and private persistence are not prerequisites. Raw identity strings remain insufficient. |

## Proposed API and source selection

These are **unapproved C# API candidates**, not Terrum source syntax.
In `Terrum.CodeAnalysis.Modules`, add:

```csharp
public sealed record TrRmSourceTypeExport(
    string Namespace, string Name, string ExportKey);

public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmSourceTypeExport> typeExports);

public static CompilationReference CreateStandardReference(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    IReadOnlyList<string> typeExportKeys);
```

The methods extend the existing `TrRmExecutableCompiler`; they are not new
resolver services. Nonempty type maps/selections initially require exact A2
operation support. Other schemas with a nonempty new argument decline as
unsupported, not by trying A2. Empty arguments preserve the corresponding
existing operation; they never erase a retained required type contract.
Lists, entries and strings are required nonnull; capture submitted lists
before processing so later mutation cannot change the result.

### Source exports

1. `Namespace` and `Name` select exactly one checked, locally owned,
   nongeneric nominal declaration in the supplied program's declaration
   catalog, minted by source binding or retained by Standard loading.
   Namespace is the full declared namespace, possibly
   empty; Name is the simple decoded source identifier. Compare ordinally
   after ordinary lexical decoding, without normalization. Do not perform
   using/alias lookup, split ExportKey, search dependencies or interpret
   these names as cross-artifact identity.
2. Require a public ordinary Copy struct and its complete supported
   declaration/storage closure. Reject a mapping to an imported expectation,
   private/internal/file-local declaration, unique struct, generic, alias,
   array, scalar or wrong declaration kind. Absence of an import marker on
   today's BoundStruct is not positive local-authorship evidence.
3. ExportKey is the exact producer-selected nonempty scalar text, without
   embedded zero, under metadata's String/key domain. It need not be an
   identifier or equal either source name. No trimming, case folding,
   Unicode normalization, prefix or default key is applied. Keys are unique
   across **all** exports of the output Module, including other kinds.
   Select exactly one key per mapped declaration; repeated declarations or
   keys reject even if identical. This bounded authoring surface creates
   no export aliases. Already legal metadata aliases remain legal and can
   be explicitly selected on import.
4. A fresh source program has no implicit type exports: public visibility
   alone is not a request to publish a key. The four-argument overload adds
   no source type mappings. A loaded program's already-retained publication
   map must instead survive re-encoding; omission is not permission to erase
   it. For loaded data the retained map is authoritative, including any
   existing aliases; a nonempty new source map cannot replace it.
   Removing keys or renaming an already identified Module requires a
   separate checked transformation, not relabeling this operation's inputs.
   Fresh source emission assigns the supplied Module identity in the output
   only to local declaration tokens; imported origins are never restamped.

Explicit key control does not relax retained declaration-name obligations:
a source rename can still be an incompatible replacement under A2.

This selects no source attribute, Core identity or CLI mapping syntax.
Existing `compile-trrm` invocations can continue metadata-only production;
they cannot claim this exported type route without a future explicit
transport for these inputs. No command derives keys to fill that absence.

### Source references

The added key list selects **type roots** in the explicitly selected Module,
alongside the existing supported MethodDef roots. It does not change those
methods' selection/naming policy. The three-argument operation retains its
MethodDef-rooted scope; required type closure is never optional. This
intentionally refines the consumer proposal's broad “admitted exports”
wording for the new A2 surface rather than silently treating every public
TypeDef as a selected root.

Each requested key must resolve to a public provider-local TypeDef of the
in-scope Copy kind. A type-only selection succeeds without a function export;
an empty root set is not a successful empty reference. Missing keys,
wrong-kind exports and expected definitions masquerading as local exports
fail. Repeated keys reject; distinct selected aliases to one actual TypeDef
produce one nominal source type while retaining each key's expectation.

Expose the TypeDef's original namespace/name for ordinary source lookup,
not the export key as a renamed type. Repeated paths to the same checked
definition deduplicate; distinct equal-named types remain distinct and
ambiguous where existing lookup says so. Reference order selects no winner.
Required closure-only declarations remain available for type/layout checking
without becoming additional lookup roots or gaining private access.

For a provider-local nominal child, use the caller's selected key when that
exact child was also explicitly selected. Otherwise require its one
unambiguous existing public TypeDef export key and retain that binding as
closure-only. A missing export declines the projection; multiple aliases
without an explicit selection decline rather than choosing by row/name/order.
The caller can explicitly select a child key when it is in the selected Module;
this API does not add a separate cross-Module child-alias selector. A
provider-owned Import already has its original selected key and is not
reselected by this rule.

Retain the complete field/layout/type contract, including private storage
facts and provider-owned dependency edges. Do not export fields, invent
member keys, grant operations or substitute field-only stubs for unsupported
public operations/formation promises. Any independently required unsupported contract declines the projection.
Capture the initial selected declaration graph as the immutable original
expectation here; a subsequent provider cannot replace that view.

Apply the existing selected-provider operation to **all supplied inputs**:
exact schema preflight, owned byte snapshots, structural/support admission,
unambiguous Module directory and complete dependency binding. A smaller root
list is not a way to bypass input admission. No resolver, installed provider,
same-name type or private-format fallback is added.

## In-memory origin and expectation projection

Retain one immutable compiler-owned association from every affected bound
nominal type handle to the following logical data. These are required
contents, not new wire records or caller-authored trust descriptors.

| Component | Required contents |
|---|---|
| Context/local ownership | Exact compilation/binding snapshot and type-handle map; local declaration token, namespace/name/visibility or imported/closure-only role. Fresh source has a compilation token, not a fabricated provider Module. |
| Actual origin | For imported/loaded definitions: owned artifact snapshot, exact schema, selected Module and snapshot-local TypeDef; governing edition/Features, Module target/runtime requirements, checked exports, dependency closure and binding generation. Fresh local source retains its token until publication. |
| Original expected views | Each original using/owner context, Module/key/kind and ModuleRef target/runtime expectation, Copy promise and complete declaration/type/ordered-field/Layout graph, provider-owned imports and correspondence/equality relationships. Separately retain stronger actually relied-on facts. |
| Publication/lookup | Explicit retained local export map, selected source roots versus closure-only declarations, and declaration/field/array/pointer/other supported type-handle mappings to their contextual origins/views. |

A snapshot identifies a selected provider revision; it is **not part of a
new nominal type constructor**. TypeDef row numbers are meaningful only in
that owned snapshot. Keys select actual declarations; two keys selecting
one actual definition remain aliases. Across replacement, resolve the old
keys against the new snapshot and check the retained graphs and equality
relationships. Neither unchanged row numbers nor an unchanged digest is
that proof.

`CreateStandardReference` mints imported associations only after successful
admission/binding. Source binding mints local declaration tokens and carries
the associations into the resulting BoundProgram. Attach the projection to
CompilationReference and BoundProgram/type tables; preserve it through
reference construction, type-table composition, bound-tree traversal,
copying/rewriting, verification and lowering. A bare hand-built BoundStruct
with no checked association cannot use this export-enabled route by claiming
a provider string or by defaulting `IsUnique=false`.

Reusing a reference in another compilation creates a checked context-local
mapping, not a transplanted type number. Repeated origins can share one
selected snapshot only after agreement on exact artifact/schema, selected
Module, dependency closure and rule/target/runtime context; Module text or
names alone do not reconcile revisions. Conflicting selections fail without
first/last-reference preference. Preserve the consumer API's existing
duplicate-input rejection when building the selected artifact directory.

Share checked origin/comparison machinery with generic/Core projections
where appropriate, but do not require NativeCoreRegistration or infer Core
designation. Per-provider Module requirements remain separate from
CompilationReference.RuntimePolicy and a declaration's Blittable commitment.
No origin, schema or original expectation may be recovered from those fields.

## Publication, imports and re-encoding

1. **Provider P:** each explicit local mapping emits an ordinary Export
   with `localDefId=0x1300_0000 | typeDefRow` and its exact key. Publish the
   complete Copy TypeDef, ordered fields, complete Layout variants and
   supported type/array closure. Preserve declared layout inputs; derive
   physical size/alignment/offsets under the selected target, not in new
   Layout fields. Scalar/unit identities need no fabricated TypeDef.
2. **Nominal closure:** every nominal child needed across this boundary
   must have an admitted exact export binding at its actual owning Module.
   Local children requiring publication need their own explicit mappings;
   imported children retain their original bindings. Do not automatically
   export a private child, synthesize a child key from a field/name, or copy
   it into the consumer as a new nominal declaration. An unexportable
   required nominal closure declines this bounded route. Private *fields*
   remain legal metadata facts without becoming public members.
3. **Consumer M:** reconstruct its expected TypeDef graph from the retained
   original view, emit M-owned ModuleRef/Import rows selecting P/key and
   expected TypeDef DefIds, and encode affected external nominal uses
   through those Imports. Emit the original captured ModuleRef target/runtime
   expectation, not M's policy, a guessed zero or a replacement's current
   tuple. Compare that original with the selected actual provider's tuple;
   never rewrite it to make replacement pass. M gains no local TypeDef Export
   for that expectation. Co-location changes none of this logical ownership.
4. **Contextual graphs:** translate every field/type/array reference from
   its original owner context. A dependency imported by P remains bound to
   that provider's selection, not resolved in M by name. Re-encoding into M
   rebuilds the required local expectations/ModuleRefs/Imports with those
   exact origins; it cannot reuse P's numeric import rows. Preserve every
   original expectation; an additional relied-on positive Copy guarantee
   gets the additional positive stub required by the A2 owner, never an
   overwritten original.
5. **Aliases/multiple views:** retain every selected key and established
   same-definition relationship. Identical original views may share a stub;
   distinct views keep distinct stubs. Link every stub to all retained keys
   in its alias equivalence class within the using Module. Multiple Import
   triples then preserve independent contracts and require the aliases to
   select one actual definition; no new key/equality record is invented.
   Check triple uniqueness, not expected DefId uniqueness. For new source
   uses, the first explicitly requested alias is the transport representative
   after equality is established; retain/check the others. Order changes
   neither nominal identity nor replacement obligations.
6. **Remapping:** resolve identities before interning. Rebuild Module
   partitions/cursors, Strings, Export/Import/ModuleRef and TypeDef/FieldDef
   references, owner ranges, TypeSpecs and their children, Layout/
   LayoutField/TargetProfile and FixedArrayLayout references. Preserve
   counts, stride, extent and alignment as quantities, not row references.
   Rebuild any retained signature/contract/anchor/Body maps, indexes,
   extents and optional source/reference-slot data under their own schemas;
   never substitute matching bytes. Clone shared structural rows whose
   resolved meaning differs between using contexts.
7. **Preservation boundary:** a packed direct TypeDef reference may replace
   an import use only after all original expectations, equality and
   relied-on obligations remain represented. Keep the Import/expected
   records when the destination has no other admitted carrier for them.
   A destination unable to preserve the closure declines. Existing private
   references/modules do not gain this carrier merely because they contain
   BoundStructs; their type-only exclusions remain in force.

Each declaring/expecting/relying Module retains effective `2/1 -> 1/1`;
retained arrays additionally require `20/1` and its existing closure.
Other retained facts keep their independent requirements. Separately
selected provider schemas may differ only where supported and actually
carrying the required facts; old A1 metadata, A3 uniqueness or generic rows
cannot manufacture A2 Copy evidence. Packing still selects one representable
destination schema for the whole artifact.

## Verification, replacement and execution boundaries

Require actual positive provider Copy evidence before reconstructing an
ordinary imported Copy struct. An expected bit is a demand, not that
evidence; clear is unknown, not ordinary or unique by default. Apply A2's
complete rootless/layout checking and the existing full expected/provider
comparison, including unused required fields, all retained Layout variants,
target/runtime requirements and provider-owned import chains/anchors.
The existing validator is the comparison owner to reuse, not a source
export policy or a reason to compare only the expected stub.

Replacement checks the newly selected actual provider against **all original
views and stronger relied-on facts**, not a snapshot synthesized from the
replacement. Same Module/key with relocated TypeDefs can succeed when every
obligation still holds. Different Module identity, missing/wrong-kind key,
lost Copy, changed retained names/field order/types/layouts or alias splitting
fails; equal geometry is insufficient. Relevant changes invalidate binding,
layout and derived native caches and require affected revalidation/lowering.
This adds no live replacement, owner migration or unload policy.

Producers establish truthful source ownership and Copy/publication facts.
Consumers check structures, supported schemas, origins/context consistency
and complete binding before affected use, without mandatory source/Body
semantic re-proof. Host/provider authentication remains external. An origin
handle or serialized flag is not an authorization or verification certificate.

No runtime/library/ABI addition follows from a type export. Layout metadata
does not initialize values, authorize field access/unsafe calls, marshal,
retain, pin or extend backing/code lifetime. Libraries choose their Module,
keys and layout/Copy commitments explicitly; no Core array API, provider
default, native symbol or OS type export is selected. Native lowering
consumes the retained checked type/layout dependencies, not DLL symbols for
type-only imports. Value/place/call execution and FieldDef publication, when
needed, retain their separate admitted contracts and explicit keys.

Null lists use ArgumentNullException; invalid entries/duplicates use
ArgumentException identifying the new list argument. Malformed artifacts or
incompatible selected keys/bindings are invalid-data failures; valid but
unsupported contracts/schema operations decline as unsupported. Existing
resource/overflow failures remain failures. Return no partial reference/
artifact and never retry another schema/provider. No runtime failure or
diagnostic-number allocation is added.

## Integration owners and acceptance gates

No normative owner or implementation file is amended here.

| Owner | Integration after approval |
|---|---|
| [IL metadata](../specs/il-metadata.md#identity-and-contract-invariants), [binding/replacement](../specs/representation/binding-and-replacement.md) | Add the scoped authoring/carrier completion; preserve key freedom, nominal equality, aliases, Import triples and satisfaction. No registry amendment. |
| [Producer API](standard-trrm-source-schema-selection.md#exact-public-library-contract), [consumer API](standard-trrm-selected-provider-consumption.md#exact-proposed-public-api) | Add the overloads/type-root semantics; replace only the missing A2 publication/projection policy, not existing defaults or MethodDef behavior. |
| [Source/complete types](../specs/language/types-and-normalization.md), [target layout](../specs/targets/windows-x64-data-layout.md) | Preserve names/access/ownership, Copy and representation; no syntax, conversion or ABI amendment. |
| [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), binder, Standard readers/writers and shared verifiers/remappers | Carry checked origins/originals, explicit maps and lookup/closure roles through type-only projection and source-free re-encoding; replace name-only identity adaptation on this route. |
| [A2 owner](standard-trrm-rootless-copy-type-evidence.md), [admission](../specs/representation/requirements-and-admission.md), [trust](../specs/representation/reader-trust.md), [versioning](../specs/representation/format-versioning.md) | Preserve existing schema/flag/closure/checking duties; no A2/A3 allocation or compatibility amendment. |
| [Support inventory](../implementation.md), [API inventory](../implementation/terrum-code-analysis.md), [binding cases](../specs/conformance/il-binding-and-replacement.md) | Record actual implementation/qualification and add the cases below when performed; no completion claim here. |

Required future tests, **not checks run by this documentation change**:

| Class | Required cases and outcome |
|---|---|
| Positive source/API | Unrelated Copy Sequential/Transparent types, including zero-payload, keys unlike names, no methods. Emit/read/project, bind consumer source and re-emit expectations without original source/debug/producer handles. Empty maps add no exports. |
| Positive closure | Explicit local child; exact imported child; private scalar fields; nested scalar/unit/Copy arrays. Preserve all ordered fields/Layout variants/counts and Module requirements. Closure-only types remain hidden from lookup. |
| Negative authoring | Missing/ambiguous declaration; imported/nonpublic type or unique type (including no-Cleanup) mapped as local Copy; generic/alias/array/scalar mapping; invalid scalar-text/empty key; duplicate declaration/key; cross-kind key collision. No invented key/attribute/origin. |
| Negative import | Missing/wrong-kind key, exported expectation, missing snapshot/schema/original view, unknown Copy, unexported child or unsupported closure. Equal spelling/geometry supplies no fallback or partial reference. |
| Lookup/aliases | Nonidentifier key does not rename a type. Same-definition paths/explicit aliases deduplicate; equal-named distinct providers stay distinct/ambiguous. No reference-order winner, partial merge or silent closure-child alias selection. |
| Boundaries | Empty namespace, keyword escapes, ordinal case/Unicode differences, independent String encodings, aliases/multiple views/Import triples, row/count limits and overflow. N=0/1/max-u64 zero-payload arrays still check full nominal/element identity. |
| Cross-provider/replacement | Equal-named/equal-layout P and Q with the same key stay distinct. P row relocation succeeds; lost Copy, changed fields/layout variants/packing/target/runtime or split aliases fail. Preserve P-to-R chains despite equal-looking S; replacement must satisfy both original views. |
| Remapping/source-free | Permuted Module/export/import/type/field/layout rows, colliding local type numbers, context-sensitive shared recipes, consumer expected stubs and co-located direct bindings. Rebuild maps/extents/source slots; retain originals/equality. Input byte/list mutation cannot affect results. |
| Schema/private | A1/F/Standard/altered tokens do not select A2; supported independent providers must carry real facts. Unsupported A3 generic/Unique closure declines, not erases. Private-100 type-only projections stay excluded. |
| Execution | Metadata/binding round trips are not runtime evidence. After separate execution prerequisites, qualify actual field/array/place/call and backing/code-origin behavior with the final selected compiler/configuration. |

The in-scope provisional choice is explicit mappings/selections plus the
retained origin/expectation projection above. Approval, implementation and
these acceptance gates remain outstanding. Actual Module identities, keys
and provider deliveries are caller inputs, not unresolved defaults.
Generic/value/member execution, a CLI transport, private type-only exchange
and production Core designation remain separate decisions or prerequisites;
none may be filled by an implementation fallback.
