# Standard TRRM Construction-2 method/type dependency selection

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This document approves
neither an API nor its dependencies, changes no accepted specification, and
claims no implementation or execution qualification.

The separate unapproved
[alias-publication proposal](standard-trrm-c1-source-reference-alias-publication.md)
recommends representative-only new TypeDef/FieldDef dependencies and
supersedes the original alias-preservation candidate below. The provisional
implementation follows that recommendation; it does not change this proposal's
unapproved status or preserve source-only aliases as replacement obligations.

## Finding and recommendation

The wire-level composition is already defined: a MethodDef expectation can
refer to exact nominal types, with separately bound TypeDef and FieldDef
Imports. What is undecided is the **host selection and source-reference
handoff** when those declarations must be exposed together. Implementing
type substitution, provider comparison and struct remapping is implementation
work; deciding which previously unselected ExportKey to use is not.

Recommend **one explicit `CreateStandardReference` overload**, with method
keys for one selected Module and Module-qualified type/field keys across its
supplied dependency set. Construct one checked reference atomically. Do not
generalize `CreateStandardReferenceSet` to callable references for this slice.
An existing serialized Import already selects a key; a new cross-Module
reference without such a binding requires an explicit selection, even when
the provider currently exports only one possible key.

### Verified source boundary

These are source observations, not results from running tests:

| Evidence | Finding |
|---|---|
| [`StandardConsumption.CreateStandardReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`StandardTypeReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | The three-argument route projects functions and has a Construction-2 full-class route with `includeModuleFunctions`. Separate four-/five-argument routes select type/field keys. None accepts an explicit combined method/type/field selection. The C1M1 class-root projector requires public kind-2 roots; it is not a classless struct-method projector. |
| [`CreateStandardReferenceSet`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs) | The classless branch requires type-only retained associations and rejects nonempty `Functions`; the class branch is also explicitly type-only. Passing method references through the existing composer is not an available public composition contract. |
| [`ValidateC1M1ClassImports`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs), [`LoadLocalAggregateLayoutProviderClosure`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutProviderClosure.cs) | C1M1-family validation returns TypeDef, FieldDef and top-level MethodDef bindings. The method closure checks all three kinds and records TypeDef `TypeEdges`; fields are separately validated, not FieldDef entries in `TypeEdges`. [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) has separate method/type and field closure carriers. Their existence does not complete the combined handoff. |
| `CreateConstruction2ClassTypeContext` in that loader | Its additional nominal TypeSpec map reconstructs public class handles, not a general value-struct signature substitution map. With no class types it returns an empty class context. |
| [`LocalAggregateLayoutNativeProgramBuilder`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.LocalAggregateLayout.cs) | The earlier blanket rejection of value structs is **not the current worktree boundary**: provisional exact-origin struct mapping is already being added there. That work does not supply the missing public key-selection policy or establish end-to-end support. |
| [`TrRmStandardClassSourceEmissionTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassSourceEmissionTests.cs) | `LinksTopLevelConstruction2MethodImportsWithValueStructTypes` attempts explicit struct references followed by reference-set composition; adjacent tests exercise class-handle method aliases. These inspected callers explain the required integration, not passing coverage. |
| [`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs) | Source-provider input dispatches type-only or type/field factories, then the type-reference composer. It carries no method-key selection. |

[Implementation support](../implementation.md),
[module support](../implementation/modules.md) and the
[gap index](../implementation/conformance-gaps.md) distinguish provisional
class/member, provider, remapping and CLI slices from completion. Their
existence neither decides a new selection policy nor approves a proposal.

## Authority, exact scope and alternatives

Preserve the accepted [source productions](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
[lookup](../specs/language/names-and-binding.md),
[complete types](../specs/language/types-and-normalization.md),
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[admission](../specs/representation/requirements-and-admission.md) and
[consumer checking](../specs/representation/reader-trust.md), together with
the [Windows x64 layout](../specs/targets/windows-x64-data-layout.md) and
[call](../specs/targets/windows-x64-call-interface.md) contracts.
None assigns this public host API or a default export-key convention.
No accepted text or grammar production is superseded.

The [L1 method-provider closure](standard-trrm-l1-method-provider-native-closure.md#prerequisites-and-remaining-decisions)
explicitly leaves source method-key reference APIs separate. Reuse its
contextual origin/remapping mechanisms, not its schema applicability or the
F consumer's NativeImport-only Module shapes.

The initial candidate applicability is exactly the three **existing**
non-EN1 Construction-2 Copy-evidence agreements in
[initializer Copy evidence](standard-trrm-c1-initializer-copy-evidence.md#exact-schema-scope-and-reused-declaration-facts):

| Existing agreement | Exact selected identity |
|---|---|
| Construction-2 + Copy evidence | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1` |
| CM1 + Copy evidence | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1` |
| CM1AR1 + Copy evidence | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1` |

These retain the existing producer choices without forcing labels, defaults
or associated-relation facts into a weaker parent. All remain unapproved
dependencies, including [Construction-2](standard-trrm-c1-construction-runtime-boundary.md),
[CM1](standard-trrm-c1-class-parameter-defaults.md) and
[CM1AR1](standard-trrm-cm1-associated-relation-constraints.md).
Root, every supplied provider, and retained origins must select the
**identical full literal** ordinally and case-sensitively before
schema-dependent decoding. Do not parse suffixes or compose decoders.
Unamended parents, C1M1 type-only, EN1/class-layout, PI1, G1, A2/A3,
private and other unlisted inputs do not enter this new operation.
Existing operations retain their existing applicability.

Initially admit ordinary public nongeneric top-level functions with
already-supported scalar/unit results and ordinary by-value scalar/unit or
nongeneric struct parameters. Structs must have positive retained Copy
evidence and complete rootless Blittable Sequential/Transparent Windows x64
layout closure, with scalar/unit or nested admitted struct fields.
No class declaration, constructor, NativeImport or dummy function is required
merely to publish/reference these structs. Retain every required contract;
unsupported public operations or closure refuse the operation, not a
field-only replacement facade. Using the selected declaration's Copy fact
for an already-admitted value call adds no transfer operation and does not
broaden the Copy-evidence proposal's class-initializer Body amendment.

| Alternative | Assessment |
|---|---|
| **Explicit factory overload and one key record — recommended** | Captures one provider directory and all choices before projection, supports a classless method provider and dependencies in several Modules, and extends the existing source-reference boundary without an independent merge policy. |
| Callable-capable `CreateStandardReferenceSet` | A coherent larger design would first add explicit method-root selection to standalone references, then reconcile function signatures, actual/expected origins, alias views, all type arenas and overlapping snapshot contexts across completed references. It cannot repair a standalone method reference that could not preserve its signature without the other reference. Useful for independent method-root Modules, but not needed here. Removing the `Functions` guard or exposing private name-based set machinery is insufficient. |

The new overload deliberately replaces neither the existing all-method
projection's naming/selection policy nor type-only composition. In
particular, the A2/LayoutQueries proposal's unique-published-child-key fallback
is **not inherited by this overload**. Supporting several method-root Modules,
mixed schemas or compatible-but-different snapshots in a reference set needs
a separate decision.

## Proposed host API

These are **unapproved C# API candidates**, not Terrum declarations:

```csharp
public sealed record TrRmStandardExportKeySelection(
    string ModuleIdentity, string ExportKey);

public static CompilationReference CreateStandardReference(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    IReadOnlyList<string> methodExportKeys,
    IReadOnlyList<TrRmStandardExportKeySelection> typeExportKeys,
    IReadOnlyList<TrRmStandardExportKeySelection> fieldExportKeys);
```

The overload belongs to `TrRmExecutableCompiler`; the record belongs to
`Terrum.CodeAnalysis.Modules`. Reuse `TrRmStandardArtifactInput`, with no new
resolver, artifact wrapper or public checked-fact descriptor.

1. `moduleIdentity` selects exactly one Module in `input`.
   `methodExportKeys` is nonempty and selects public, provider-local,
   ordinary top-level MethodDefs in that Module, with no receiver or generic
   formals. Source-facing callable views are bodyless expectations; actual
   implementations retain their real Bodies/dependencies in the provider
   snapshots, not fabricated stubs. Imported proxies and noncallable anchors
   are not export targets.
   No selection runs a Body, chooses an entry or activates a provider.
2. Type/field lists may be empty. Each record selects its exact Module from
   the unambiguous directory of `input` and `providers`, then the key and
   required definition kind. This includes co-located Modules without
   resubmitting their artifact. A type key selects a public, provider-local
   nongeneric kind-1 struct in the admitted domain, not an expected proxy.
   A field must be public, local and instance; its exact public owner must
   be among the explicitly selected actual TypeDefs in that same declaring
   Module. An imported owner proxy in a different Module is not that selection.
3. Lists/entries/strings are required nonnull. Module identities and keys are
   nonempty, NUL-free well-formed scalar text in the existing metadata String
   domain. Compare decoded text ordinally without trimming, normalization,
   case folding or identifier interpretation. Reject repeated method keys
   and repeated `(ModuleIdentity, ExportKey)` selections, including repeats
   across the type/field lists. A key has one kind in its Module; equal text
   in different Modules is not a collision.
4. Capture collections and owned immutable artifact bytes once. Callers must
   not mutate during capture; later mutation cannot change the result.
   Preflight every exact selector, including unused inputs, before indexing.
   Validate all supplied Modules and retained requirements/edges. Duplicate
   Module identities within this input set reject even for equal bytes;
   neither traversal order nor a key record selects a revision winner.
   Missing dependencies are not searched for or supplied by another format.
5. Return one immutable `CompilationReference` only after complete admission,
   binding and remapping. Do not implement this as a method-only projection
   followed by today's type-only composer, or retry another factory/schema
   after a failure. The three-argument all-declarations factory cannot fill
   omitted key selections. New type roots do not excuse any unrelated
   unsupported Body, entry, reachability or closure restriction of the
   selected consumer.

### Lookup, aliases and closure

Preserve the existing method-reference convention: each explicitly selected
method key is its source callable-view name and retains that exact binding.
Selecting two aliases can expose two callable views of one actual MethodDef,
not two implementations or an invented overload. Keys outside ordinary
source identifier spelling gain no new source syntax.
Type/field lookup uses original declaration names, not renamed keys.
Explicit type selections are lookup roots; other required nodes stay
closure-only. Distinct actual equal-named types remain distinct and diagnose
ordinary ambiguity; no dictionary insertion or selection order chooses one.

Validate every explicitly selected alias against its captured provider
snapshot. For each actual type or field, a newly created use selects the first
explicit key in that selection list; remaining selected aliases do not become
replacement dependencies. Existing Imports retain their exact keys,
expectations and any shared-expected-DefId equality they already encode.
Method calls through an alias keep that callable-view key. Export-row order,
declaration names, equal layout and a uniquely available but unselected key
are never selectors. See the separate alias-publication proposal for the
unapproved representative-only policy and its replacement cases.

Walk the complete finite method signature/Body/contract and type/field/Layout
dependency graph, including uncalled retained edges:

* An existing provider-owned Import already carries its selected Module/key.
  Translate and retain that exact binding and original using context; do not
  demand a new key to replace it, redirect it to a downstream leaf, or treat
  its expected declaration as a public local export.
* A provider-local nominal dependency that needs a **new** Import in the
  consumer must have an explicit type-key selection for that exact actual
  definition, including nested inline children. Absence refuses even with
  one published key. A chain through another declaring Module uses that
  Module's explicit record. Selecting an outer type never selects its
  child's keys.
* Retain complete private field/layout facts and any supported private
  implementation closure in their original Module context without granting
  source access. A private nominal dependency cannot be given a fabricated
  export; if its required meaning cannot survive the selected representation
  and ownership, refuse publication.
* Complete layout metadata does not select field keys. A new external field
  use needs its explicit selected field binding. An already-retained exact
  FieldDef Import remains valid only in its checked original context;
  carrying a provider Body does not expose its private fields to callers.

Keys supply boundary identity, not operation authorization. Source access,
initialization, loans/backing, field-operation and target/ABI gates still apply.

## Binding, substitution and transformations

Resolve Module/key/kind and actual ownership **before** contract comparison.
Keep each consumer-local expectation separate from the actual provider
declaration, with original artifact/schema, using Module, Import/ModuleRef,
owner/formal/substitution context, requirements and selected provider snapshot.
Intern actual types by checked provider Module and snapshot-local definition,
not names, keys alone, field positions or `PrimitiveType` numbers.
Snapshots identify revisions, not a new nominal type constructor.

For each retained method compare complete ordered logical parameter/result
types, modes, caller-unsafety, effects, dependencies and applicable parameter
surfaces under the existing directed relation. CM1 defaults/labels remain
view-owned; a replacement's source defaults do not rewrite an old call.
Substitute expected nominal proxies with their checked actual correspondence
through signatures, variables, call inputs/results and nested field types.
Preserve complete constructor/qualification nodes and original declaration
contexts; shared structural rows require contextual cloning where meanings
diverge. This nongeneric slice introduces no inference, covariance, generic
instantiation or new formal-substitution rule.

Bind a FieldDef stub in its expected owner's checked contiguous range to the
exact selected provider field **and** separately bound owner TypeDef.
Compare complete type, visibility/access, mutable/static role, all retained
default/exact-target Layout variants, alignment/packing, Blittable commitment
and complete ordered LayoutField correspondence. Compare nested nominal
edges recursively; widths or offsets do not establish correspondence.
Preserve positive Copy on both expected and actual declarations and nested
Copy obligations. Never infer Copy from layout, rootlessness, absent Cleanup,
or a clear reserved bit in another schema.

Consumers must safely decode framing, counts, bounds, namespaces,
owner/range relationships, schema/Feature support and required bindings
before affected use. Producer-published Body facts remain producer truth
obligations, not mandatory consumer semantic re-proofs. A supplied artifact
or host key is not trusted authorization.

Carry the combined method/type/field association through source binding,
shared bound traversal/rewriting, verification, persistence and lowering.
Preserve field edges as well as `TypeEdges`; an in-memory carrier extension
is implementation work, not a reason to invent a wire record. Rebuild:

* Module partitions, entries, ModuleRefs, expected definitions and every
  Import/Export alias, retaining provider Module ownership;
* TypeSpec/MethodSig/Param, field/Layout, applicable parameter/contract/value
  references and each original expected-to-actual correspondence;
* Body-local Type/Method/Field/Constant/Home maps, FieldPath roots/steps,
  typed operands, instruction/source locations and section extents.

Packing can use direct bound definitions only while every surviving original
expectation and stronger relied-on fact remains represented. Clone/remap
schema-owned references, never matching octets. Source-free reconstruction
must produce the same nominal signature/field identities before joint
lowering; provider functions are not restamped as consumer-owned declarations
or implicitly re-exported. Recompute target call/layout plans and preserve
actual code/load lifetimes; metadata success is not a new aggregate foreign
ABI or independently precompiled-provider agreement.

Replacement resolves retained keys against the new selected snapshot and
checks the complete graph, including pre-existing alias equalities and
stronger facts used by specialization. A source-only alias omitted by
representative selection may be removed or split when no retained edge or
relied-on fact requires it; a fresh reference request still validates every
selected key. Compatible row relocation is permitted after those checks;
identical Module/key text or bytes are not proof. Changed retained nominal
origins, shared-expected-DefId targets, method/field contracts, Copy facts or
inline layout reject before affected use. Source is not rebound to a new
overload/default set. Updating metadata does not patch already generated
native code; live replacement remains outside scope.

## Wire preservation, failures and unsupported work

Allocate **no new wire ID, Feature, schema identity/alias, binary encoding or
version**. Existing Import rows still contain ModuleRef/key/expected DefId;
Export rows still contain local DefId/key. TypeDef, FieldDef and MethodDef
DefId kinds remain `13`, `14` and `16` hexadecimal respectively. Field stubs,
TypeSpec forms, MethodSig/Param, Layout/FieldPath and Body maps keep their
selected schema-owned shapes. Struct `DeclaredCopy=0x20` is inherited only
from the explicitly selected Copy-evidence agreement, not allocated here.
Retain `(1,0)`, governing edition and each Module's exact conditional Feature,
target and runtime obligations. In particular, FieldDef Import/Export retains
Feature 13/1; this selector alone does not force Feature 25, imply native
invocation, or activate constructor/GC support.
[Exact draft agreement](../specs/representation/format-versioning.md) remains
out of band; no old-schema relabeling or compatibility decoder is proposed.
No physical assignment is needed, so no allocation/reallocation candidate
is warranted.

Use the existing host failure families: null arguments as
`ArgumentNullException`; malformed/duplicate selections or missing/wrong-kind
requested keys as `ArgumentException`; malformed metadata, conflicting
identities/owners or incompatible retained bindings as `InvalidDataException`;
unsupported schema/contract/type/Body/lowering as `NotSupportedException`.
Keep existing checked capacity/resource distinctions. Fail atomically without
an empty reference, dropped edge or alternate-schema retry. Ordinary source
ambiguity/access/use failures remain source diagnostics.

Still unsupported by this extension: several independently selected method-root
Modules, callable reference-set composition, generic structs/methods,
aggregate results/defaults, new reference/pointer/array/enum/handle field
domains, unique or Cleanup-bearing values, new class construction/member
semantics, mixed schemas, private persistence, general initialization
scheduling, independent native images and live replacement. Existing
Construction-2 runtime selection, fail-fast and native lifetime gates remain;
this is not full Construction-2 or roadmap acceptance.

## Integration, CLI boundary and acceptance

On approval, integrate this narrow host policy with the owners below; no
owner is amended or approved by this document:

| Owner | Intended integration |
|---|---|
| [Selected-provider consumption](standard-trrm-selected-provider-consumption.md), [type identity](standard-trrm-source-copy-type-identity.md), [LayoutQueries projection](standard-trrm-layout-queries-source-provider-boundary.md) | Cross-link this separate combined overload/applicability and explicit dependency-key rule; preserve their existing F/A2/query-only operations and fallback policies outside this overload. |
| [Class publication](standard-trrm-c1-class-source-publication.md), [Construction-2](standard-trrm-c1-construction-runtime-boundary.md), [Copy evidence](standard-trrm-c1-initializer-copy-evidence.md) | Add this bounded classless method/struct projection without weakening complete class/member closure, copying evidence or execution admission. |
| Accepted metadata, binding, admission and target chapters linked above | Preserve unchanged; no new source production or representation allocation. |

An **independent source-provider CLI manifest needs a separate follow-on
decision** to expose this API. The existing
[source-provider transport](standard-trrm-source-provider-cli-transport.md)
and [C1 version-2 extension](standard-trrm-c1-source-provider-cli-transport.md)
select types/fields, not method roots, and do not select these Copy-evidence
pairings merely because their parents are listed. Do not add a method array,
configuration version, schema pairing or dispatch change here. A follow-on
must choose those explicitly and retain strict input/path/snapshot and atomic
publication rules. Output export maps cannot supply provider selections.
Artifact consumption of already serialized Imports is distinct and needs no
new source-manifest option from this proposal.

Implementation prerequisites are complete combined origin retention, struct
signature substitution, exact field-owner binding, source-free remapping and
the remaining value-struct runtime-merger integration. These are not new
semantic decisions. Required future checks through public APIs:

| Class | Required outcome |
|---|---|
| Positive | Classless provider with opaque explicit keys (source-spellable for called method views), nested Copy structs and a real method Body; compile a caller that exercises a nested scalar field, emit, source-free reload and retain exact method/type/field origins. No dummy class/native anchor. |
| Positive: aliases | Validate all selected type/field aliases in non-export-row order; publish the first explicit type/field key as the new dependency. Method aliases retain their callable-view keys, and pre-existing shared-expected-DefId relationships remain intact. |
| Cross-provider | Public regressions now cover a consumer method Module with a method provider importing a nested struct/field from another Module, and explicit TypeDef/FieldDef selection from a non-first co-located Module. They exercise nested FieldMap loading and unchanged source-free re-emission/reload and remain unverified under the active hold. Add co-located provider Imports, deeper chains/diamonds, and distinct expected-TypeDef contexts; preserve each incoming expectation. |
| Negative: selection | Omit a required newly imported type/child/field key even when uniquely exported; wrong case/kind/Module/owner, proxy re-export and private/static field selections reject. Provider presence, public layout and adding a type root alone select no field. |
| Negative: retained edges | Invalid uncalled MethodDef/TypeDef/FieldDef Import, missing owner binding or mismatch behind a shared/cyclic dependency rejects before use. A prior comparison cannot hide a later obligation. |
| Replacement | Accept compatible TypeDef/FieldDef/MethodDef and Import/Export row relocation with preserved retained keys/contexts. Accept removal or splitting of an omitted source-only type/field alias when no other retained obligation depends on it. Reject changed nested field type, layout/pack/alignment/variant, Copy, method mode/unsafe contract, selected nominal origin or an already-retained shared-expectation split, including changes in C under unchanged A/B bytes. |
| Transformation | Permute all affected consumer/provider rows and Body maps, pack/re-emit/reload without source. Preserve Module ownership, source maps, FieldPath steps, every original expectation and method call argument/result identity. Never compare raw row numbers across snapshots. |
| Boundary | Empty field selection and scalar-only methods; empty/unit-field structs retain nominal identity and logical unit positions. Null/empty/duplicate inputs, multiple aliases, zero/maximum schema-owned map indexes, checked row/count/resource limits and invalid inline cycles fail or succeed under their exact existing domains without truncation. |
| Isolation | Wrong/unsupported schema even on an unused provider, unamended parent Copy bits, duplicate/conflicting Module snapshots, unsupported retained contracts and caller mutation cannot yield partial success. Existing type-only set routes and CLI files keep their old admission/selection. |
| Native gate | Separately qualify headless JIT and supervised AOT using final compiler/configuration/input identities and the existing call ABI. Emission or source-free loading alone is not runtime evidence. |

These are acceptance requirements, not claimed test results.
Formal approval of the recommended API, its initial applicability and strict
explicit-key policy remains outstanding. The optional CLI transport and
broader callable composition remain separate decisions, not implementation
defaults.
