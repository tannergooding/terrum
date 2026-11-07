# Private nominal-class references under revision 105

**Status: unapproved candidate.** This proposes a private declaration/reference
role, not public constructor semantics, compiler support or runtime
qualification. Implementing its recommendation remains provisional and does
not approve or stabilize the contract.

## Decision and contract owners

Recommend **reference-only class declarations**: retain the checked class
contract and nominal origin without importing, synthesizing or identifying
a constructor/allocation factory. Classes whose implementations are owned by
the program still require the existing local construction pair. A reference
declaration is not an incomplete local implementation.

The missing decision is how that distinction survives source binding and
source-free reconstruction. The existing logical identity rules answer
*which class*, but the current private carriers and verification paths do not
provide an end-to-end imported-class route.

The candidate supplies the class-reference role left unspecified by
[nominal-origin inputs](private-105-nominal-origin-inputs.md#retained-closure-and-identity-contexts)
and [105 type closure](checked-reference-private-persistence.md#every-type-occurrence-and-textual-expectations).
It makes their timed definition-to-expectation projection concrete for classes,
preserving C's [Binding](native-output-foreign-interface-private-persistence.md#binding-definition-export-or-expected-import)
and R's canonical `1024 + classIndex` origin selector, with at most 256
classes. Qualified uses still resolve through that one canonical origin.
Preserve the exact **outer 3 / inner 105 / B+C+R** selection, directories,
8-byte complete-reference rows, class i32 geometry and all existing fields.
No new wire flag, constructor key, table, tag, textual type atom or schema
selection is proposed.

No accepted text is replaced. The governing owners remain
[Modules](../specs/modules.md),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[complete types](../specs/language/types-and-normalization.md),
[recorded source binding](../specs/language/names-and-binding.md#calls-conversions-and-recorded-binding),
[consumer trust](../specs/representation/reader-trust.md) and
[private persistence](../specs/representation/persistence.md).
The accepted [TypeDef/Import model](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
allows declaration expectations without locally authored implementations;
it does not supply an adapter from Standard records to private `BoundClass`.
Equal version bytes do not select such an adapter under
[format versioning](../specs/representation/format-versioning.md).

Scope is the existing bounded, closed, nongeneric `BoundClass` contract and
its reference/origin closure. This adds no class forms, resilient layout,
member publication, general managed runtime, GC, allocation/handle ABI,
cross-image value mode, public factory API or standalone type-only package.
Unsupported field/type/runtime facts remain unsupported.

## Verified boundary

These are source observations, not newly executed tests:

| Evidence | What it establishes |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) (`ManagedClassTypes`, `BoundClass`, `BoundProgram`, `BoundPrivateNominalTypeOrigin`) | Canonical/qualified class identities, fields/size/access/mutability and nominal Binding rows exist. `BoundClass` has no declaration-versus-implementation role or constructor identity. |
| [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`ModuleCompiler.CreateReference/CreateProjectedReference`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | References now carry a bounded class table and `PrivateClassReferences` expected snapshots; explicit exported class roots are projected while non-exported layouts stay outside source lookup. The snapshots remain the source of the complete original field/type graph. |
| [`Binder.Bind`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.ManagedClasses.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs) | Binder registers classes only when a retained reference marks the origin as an eligible owned export. It does not derive constructor/factory stubs. Nested private field-type closure is not yet materialized or rebased into the consumer's bound type universe. |
| [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) (class counts, `IsClassConstructor`, `VerifyClassFactory`) | Reference-only descriptors carry checked expected snapshots and do not require a local constructor/factory pair; source-authored classes retain exactly-one local constructor/factory checks. Both class-constructor and factory checks still reject external declarations. External managed-value signatures retain their independent restrictions. |
| [`ModuleCompiler.PrivateNominalOrigins.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateNominalOrigins.cs), [`SameClass`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | The writer/reader support class origin rows, snapshot references and selected class-export comparisons. `SameClass` currently uses class-aware type names, not a complete context-sensitive nominal comparison graph; recursive/private field-type closure remains incomplete. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Sets/graphs now reindex distinct class tables and retain expected snapshots. Ambiguous duplicate class names are rejected rather than merged by owner/snapshot, so class diamonds and divergent physical closures remain open. |
| [`ModuleCompiler.Link`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`NativeManagedClassModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeManagedClassModuleTests.cs) | Source-free linking of owned image-private class implementations already exists, with class offsets/renaming and scalar/Copy boundaries. It does not expose an imported nominal class to source. Concatenating such implementations is not proxy-to-definition binding. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [sets](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [graphs](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.Graph.cs) | Native paths forward identity inputs and semantic references, but function import bindings and graph structure-name/cleanup origins do not supply a class type export or reference role. `HasPrivateManagedState`/`RetainedNativeProfile` also conflate a retained class table with managed implementation use. |

Thus no inspected route completes **explicit imported class reference
binding** without a further private role/validation decision. Copying a
local constructor/factory into a consumer, marking it external, or making a
same-layout local class is not a solution. The existing
[`ClassDescriptorWithoutConstructionPairCannotEnterLibraryOrProducer`](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeManagedClassVerifierTests.cs)
regression must continue rejecting an owned descriptor stripped of its pair.

## Candidate carriers and source/reference binding

Keep `BoundClass` as the existing immutable layout record; do not attach
constructor/factory keys to it. Keep the existing `Classes` and
`PrivateNominalTypeOrigins` collections on program/reference/module.

Add compiler-owned internal
`PrivateClassReferences : ImmutableArray<BoundPrivateClassReference>` to
`BoundProgram` and `CompilationReference`. Each candidate immutable entry
has exactly:

| Field | Candidate meaning |
|---|---|
| `ClassType : PrimitiveType` | The canonical class selector in this exact containing snapshot. |
| `Origin : BoundPrivateNominalTypeOrigin` | Its checked, remapped occurrence Binding. A provider-scoped reference may retain the provider's owned Binding; a new consumer proxy uses an imported Binding. Neither means implementation ownership in the consumer. |
| `ExpectedSnapshot` | The immutable private identity snapshot required by the nominal-input owner: original using context, exact schema, complete original class/field/type/origin graph, and separately retained selected-provider resolution/additionally relied-on facts. This reuses that snapshot contract, not a second identity system. |
| `ExpectedClassType : PrimitiveType` | The canonical root selector in `ExpectedSnapshot`, related to `ClassType` by a checked map, not integer equality. |

Retain an entry for each independent original occurrence/context, even if
several eventually share one actual class row. Only checked projection,
decoding and compiler rebasing create these associations. Validate the
association against the exact captured declaration and complete type graph;
a stale `with` copy or replaced class table cannot inherit an exemption.
A host-supplied Boolean, absent body or missing pair is not reference evidence.
An owned implementation's obligations remain independent of any reference
occurrences selecting it.

Use the existing `Compilation.Create`/`CreateLibrary` reference input; add no
source syntax. Project explicit public owned class exports and their needed
contract closure, not every private class in the provider image. Only
eligible published roots enter ordinary source type lookup; retaining a
private dependency's layout does not publish its name, fields or members.
Register class identities before remapping fields so finite nominal recursion
works. Preserve existing duplicate-name/unsupported-ambiguity rejection
rather than choosing a provider by order.

`CreateReference(byte[])` remains provider-scoped. It preserves the original
class contract and provider requirements without choosing the consumer's
Module identity or proxy key. Binder incorporates its reference-only class
associations and remaps all dependent type families; it must not run
`DeclareClassFactories` for them.

The existing `PrivateModuleIdentityInputs.Create` and producer/consumer
overloads supply the authoring input:

* An owned class assignment uses the actual program's canonical class
  selector and explicit local declaration/export key. Export requires public
  visibility and positive ownership, not merely a missing import marker.
* An imported assignment uses that program's canonical reference-class
  selector, explicit using-Module proxy key and exact selected provider
  Module/class export. It must agree with the retained reference origin.
  At this boundary form the new imported occurrence and capture its complete
  original expectation; do not substitute a replacement's contract.
* An imported class cannot become owned or exported by resubmitting its
  layout. Missing required inputs/exports refuse transport. A closure needing
  an unavailable nominal export remains gated under C+R; this adds no private
  type forwarder, export alias or name-derived key.

At serialization, project the selected provider-owned reference root into
the explicit using Module's **imported** Binding. Do not publish that
reference copy as another owned provider definition. Its original class
facts remain in the complete expected class record, and its selected
owner/export remain in the imported Binding; preserve independent inherited
using-context expectations too. If the actual provider implementation is
also packed, retain its owned Binding and real pair as an implementation,
not as a reference-copy export. This specifies the class case of the input
owner's timed definition-to-expectation projection.

The existing class table and `privateType + Binding` rows therefore retain
the required expectations. Source-free decoding reconstructs imported
occurrences from those Bindings and contexts, separately from any actual
owned implementation. An owned class in an implementation artifact still
requires its pair; an origin-free local class retains that rule. A reference
factory can project an owned producer definition as reference-only because
the checked projection, not absence of a pair, establishes that role.
No new serialized authorization bit is needed. No required fact may live
only in `PrivateClassReferences` or a host cache: refuse a route whose existing
selected records cannot reconstruct its required associations.

## Construction and provider replacement

For a reference-only occurrence, import **no** `IsClassConstructor` or
`ClassTryCreate` implementation, local provisional-object slot or
`ClassConstructor` function index. Do not synthesize empty bodies or infer
callable identity from `C.#ctor`, `C.TryCreate`, field names or class keys.
The class export alone exposes no callable members. Independently admitted
ordinary function exports keep their own contracts.

For an owned implementation, preserve the current exactly-one local
constructor/factory invariant, receiver qualification, initialization/
publication behavior and checked factory-to-constructor relationship.
Retain the implementation pair and its dependencies when retaining that
owned class. A reference occurrence neither supplies nor excuses the pair.
External class constructors/factories and construction/member uses requiring
an unpublished class operation remain rejected; this proposal creates no
importable construction operation.

During semantic linking, resolve a proxy to the selected actual owned class,
remap uses to that implementation and retain the original expectation.
Do not append a second pseudo-owned class or copy the pair into each using
Module. If one physical row serves owned and reference occurrences, validate
the owned implementation and every reference expectation independently.

Resolve `(selected Module, selected export key, class kind)` to exactly one
actual public exported owned class before comparison. Check **every original**
expectation in its original using context, including uncalled/type-only
roots and diamonds. For this bounded complete-snapshot candidate, compare
the existing published class lookup name, size, access/mutability and entire
ordered field list: names, offsets/sizes, access/mutability and complete
qualified types with resolved nominal owners. Names are retained contract
data, never a resolution mechanism. Retain all other required admitted facts;
an unsupported field fact must not default away. This selects no layout
resilience or private-field omission.

Compare recursive graphs by complete expected/actual/context pairs; nominal
anchors do not make an in-progress pair successful. Same-named/layout classes
from another owner fail. Physical reindexing succeeds with agreeing maps.
Keep original expectations separate from stronger actual facts used later.

Private constructor/factory signatures, bodies and function positions are
**not** part of this reference-only contract. They may change on replacement
when the actual owned implementation remains valid and every independently
retained callable/other obligation still holds. No constructor/factory
export key or provider member-selection policy is therefore needed here.

## Propagation, native gates and failure

Projection, `Function=null` erasure, restoration and selection helpers must
carry the associations and original graphs, not reconstruct them from a
replacement. A reference-only class table alone does not request allocation,
root maps or the image-private managed runtime. Separate that metadata from
actual owned class implementations and class-bearing signatures/operations
when computing managed-state/profile requirements. Do not weaken existing
managed external-call, lifetime or per-use support gates.

Sets and graphs merge class tables, origins, original snapshots and all
complete type families together. Preserve each intermediate using context;
deduplicate only after exact owner resolution and complete comparison.
Conflicting selections for one logical Module reject. Existing name-based
graph type/cleanup provenance is additional deployment evidence, not the
nominal class key. Keep justified class-only semantic dependency edges even
without a machine call, without manufacturing PE exports or IAT entries.
An unbound reference may retain unresolved requirements; operations claiming
resolved binding must supply the entire required selected closure.

Use checked per-snapshot/context class maps, preserving all four existing
qualified class bands. Remap class/struct fields, complete reference/optional,
pointer, signature and array closure, declaration-only contracts, annotations,
and typed-place owner/field paths even when the final leaf is scalar.
Preserve original snapshot roots and identity/key strings while rebuilding
type indexes and parsed existing type atoms. Re-encode counts and affected
outer lengths/offsets/hashes; do not substitute matching bytes or ordinary
text. Retain existing aggregate byte/node/work/depth budgets.

For native producers, the authored using identity must agree with the
explicit producer identity. Consumer identity is separately supplied, never
the provider's identity or graph `@`. TNPK/TNSP/TNGP snapshots retain the
same expectations and semantic-only edges. This changes neither outer-3
fields nor image-shape/cardinality requirements. Independently stored images,
root-only JIT and joint semantic lowering keep their existing separate
admission/regeneration rules. The accepted
[set](../specs/module-sets.md) and [graph](../specs/native-graph.md) scopes
grant no managed crossing, heap/domain or cleanup/code-lifetime permission.

Invalid host shape/snapshot association uses existing argument failures;
source attempts to use unavailable operations produce diagnostics.
Malformed framing, selectors, Binding data or references use
`ModuleFormatException`; unavailable required bindings, unsupported closure
or incompatible providers use `ModuleContractException` at module boundaries.
Complete required checks before affected lowering/image admission, publish
no partially resolved success and do not fall back to another provider,
proof-only verifier mode, invented pair or older schema. Producer body truth
and optional semantic audits retain their existing trust boundary.

## Alternatives, implementation gates and approvals

| Candidate | Assessment |
|---|---|
| **Reference-only class contract — recommended** | Reuses the selected class/origin records and explicit inputs. Requires checked host associations, declaration-aware verification and complete class maps; needs no callable construction identity. |
| Import constructors/factories as public callable members | Substantive alternative: select actual member keys/owners, receiver/allocation/output/failure contracts, private-versus-public factory meaning and replacement/target rules. Existing names/local indexes do not supply them. This is unnecessary for metadata references and requires a separate scoped decision/encoding analysis, not an assumed 105 amendment. |
| Universal nominal TypeDef/member/context IR | Could unify wider class/generic/member projection and eliminate more name-based limits. It migrates substantially more ownership and callable machinery than this missing role needs. Standard records are design input, not a decoder fallback. |
| Keep imported class transport unsupported | An honest implementation gate, but not a reference-binding solution or completion of the class-origin slice. |

Current implementation work remains in complete snapshot type-family
rebasing, nested private field-type closure, source-free proxy replacement,
context-sensitive nominal comparison, reference-only versus owned runtime
classification in every native/package profile, and duplicate/diamond set and
graph resolution. The exercised scalar-field class route is partial
infrastructure, not acceptance. Implement each route coherently before
removing its refusal; adjacent enum C/R codecs and runtime gates remain
independent.

Required focused cases, not claimed results:

| Area | Positive, negative and boundary obligations |
|---|---|
| Source/reference | Export an otherwise unused public class under an arbitrary key beside a real scalar function export; bind a separately authored consumer with a different using/proxy key, no imported construction pair and exact original class closure. Unrelated private classes stay out of source lookup. Missing export/origin, imported-as-owned, wrong key/kind/Module and stale association fail. |
| Construction distinction | Preserve the owned-descriptor-without-pair regression. Reject forged reference exemptions, external constructors/factories and construction/member use with no separately admitted operation. Owned classes and imported reference classes can coexist without one satisfying the other's count. |
| Replacement | Reorder class/type/function tables and change private constructor/factory implementation contracts while preserving the published class and independently retained obligations. Reject equal layouts with different nominal owners, changed field/order/access/mutability/size/type/qualification, identity redirection and missing uncalled expectations. |
| Closure and limits | Exercise recursive class fields and scalar-leaf object places, nested complete type constructors and missing inner origins. Cover class indexes 0/255, count 256/257 including cloning, noncanonical origin selectors, dangling references, key/string limits, existing class field/payload bounds and shared resource exhaustion. Unsupported recursive structural or adjacent type forms still refuse. |
| Source-free preservation | Project, erase Functions, decode, restore/link and re-emit without source or original host registration. Preserve every original context/expectation separately from actual facts; a proxy must not publish the provider's owned export. Independent fixtures retain exact 105 directories, class geometry, mandatory counts and reference-row widths; reject missing/truncated/conflicting data. |
| Sets/graphs/native | Reverse provider order and physical maps; check compatible and incompatible diamond expectations, including class-only edges with no IAT use. Preserve owner versus code/cleanup origin and explicit producer/consumer identities. Missing/ambiguous closure fails before image use; class allocation/handle crossings remain unsupported unless independently admitted. |

Approval is still required for (1) the reference-only carrier/projection and
ownership-aware verification rule, and (2) this bounded complete-class
comparison contract, including constructor/factory independence. Actual
host Module/key assignments remain explicit invocation decisions under the
input owner; CLI delivery is separately unselected. Importable construction
operations, general class/runtime support and any future wire amendment are
outside this decision, not implicit prerequisites or approvals. Documentation
approval would not implement or qualify any route.
