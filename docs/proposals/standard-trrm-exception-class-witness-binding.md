# Standard TRRM exception-class witness binding

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This selects a profile/binding prerequisite,
not normative adoption, implementation support or runtime qualification.

## Decision and alternatives

Use the assigned TypeDef, InterfaceDef and InterfaceWitness families, ordinary
exact provider bindings, and an **interface-only accompanying Core anchor**.
For the complete Core exception contract, recommend the distinct exact
candidate identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1`**

Call this **C1** below: the [B+I+G composition](standard-trrm-initialization-predicate-composition.md)
plus the complete [G1 schema](standard-trrm-generic-method-call-body-encoding.md#exact-schema-and-capability),
with the catalog reconciliation below and bounded nominal class/witness
binding specified here. Keep both existing identities unchanged. Explicitly
extend the [failure handoff](standard-trrm-initialization-predicate-failure-identity.md)
and [phase-2 construction map](standard-trrm-initialization-predicate-postcondition-construction.md)
to C1; their existing B+I+G route does not acquire G1.

| Candidate | Assessment |
|---|---|
| Assigned class/interface/witness rows under existing exact B+I+G | Smallest identity-only integration: class kind and a closed owner-generic interface instance need no GenericCall record merely to identify them. However, generic rows alone do not authorize generic publication/execution under [metadata's admission boundary](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics). This is insufficient for the complete retained Core contract below; do not broaden B+I+G silently. |
| **C1 plus ordinary bindings and the interface-only anchor** | **Recommended.** Reuses the assigned declaration/witness rows and G1's required generic-method representation. Adds one explicit composition and required binding input, not a second witness encoding or generic ABI. |
| B+I+G plus out-of-band keys/origins instead of G1 | Keys can identify declarations, not authorize unsupported method formals or replace their published contracts. Carrying richer declaration/body semantics outside the artifact would require a separately defined required representation; a strings-only catalog is insufficient. |
| C1 with a new wire Core-role/witness registry | Unnecessary for this handoff. Ordinary exports and the implementing primary's exact witness graph already identify declarations and conformance. No new DefId, section or role bytes are selected. |

Explicitly replacing the unpublished B+I+G agreement is permissible. A narrower
generic-declaration-only amendment would need its own complete admission
contract; it saves no declaration bytes and duplicates G1's publication rules.
C1 instead reuses that owner while keeping opt-in and affected consumers clear.
Preserving draft numbers or providing old-schema compatibility is not the reason.

G1 is required here by **retained contracts, not interface names**.
[Local failures](../specs/language/local-failures.md#1-the-common-exception-contract)
requires the common formatter witness;
[standard text operations](../specs/language/construction-handlers.md#standard-text-operations)
requires `IFormattable.TryFormat<TWriter>` and its `ITextWriter` constraint,
whose declaration closure also includes `TryWriteFormatted<TValue>`.
G1 requires `23/1` for retained method formals, including bodyless declarations,
even when formatting is uncalled. One monomorphic specialization cannot
replace those contracts. Conversely, `IException<C>`'s owner argument alone
does not require 23 or select G1. G1 is necessary, **not sufficient**, for the
complete common-member/value/witness closure. C1 alone does not encode the
class declaration's `BoundClass.IsMutable` fact; complete class binding uses
the exact C1M1 amendment in
[class declaration mutability](standard-trrm-c1-class-declaration-mutability.md).
The C1M1 token is distinct, not an alias or fallback, and adds only its
assigned class mutability bit to C1.

The [G1 version-3 source map](standard-trrm-g1-interface-declaration-exports.md)
remains G1-only and declaration-only; it does not publish closed witnesses,
class constructors, properties, defaults or arbitrary provider graphs.
C1 does not reinterpret that map or select it from artifact contents.

## Exact schema and catalog

Select C1 ordinally and case-sensitively **before** schema-dependent indexing,
emission, reference construction or loading. Require C1 explicitly for the
root, plan-bearing inputs and every supplied provider in this initial route.
Differently selected artifacts decline; no automatic conversion, component
parsing, subset retry or compatibility alias is defined. Preserve `(1,0)`,
edition 1, Body codec/encoding `01` and existing API/CLI defaults under
[format versioning](../specs/representation/format-versioning.md).

C1 includes G1's whole schema, including its assigned LayoutQueries base,
complete SF1 scalar codec, CallSite target kind 4 and Body record `10`.
It also includes I and G, not just their recognizable records. Reconcile
the catalog **once for the whole artifact**:

| Requirement | C1 rule, independently in each using Module |
|---|---|
| Phase-2/general graphs | `17/1 -> [(10,1)]`; bounded `10/1 -> []`, as in B+I+G. |
| Retained fixed-scalar values/defaults | Full SF1 `11/1 -> []`; no integer-only replacement codec. |
| Required constant execution | G's pending `12/1 -> [(11,1),(17,1)]`; missing execution codecs still refuse use. These rules explicitly replace G1's historical 10/12 edges **only in C1**. |
| Retained generic MethodDefs/GenericCall targets | G1 `23/1 -> []`, including imported expectations and uncalled declarations. It supplies no missing witness or managed-value representation. |
| Layout/query/field binding uses | Existing conditional `13/1 -> []`; declaration-only FieldDef Imports/Exports also require it. Schema inclusion does not establish LQ source/provider/query-Body implementation support. |

No new Feature ID is proposed for already assigned class/interface rows.
Retain every independent substrate and incomplete-codec gate; the empty
Feature list does not assert universal support. Neither interface conformance
nor phase 2 implies native interface Feature 5, native unwind 7, NativeOutput 8
or CheckedExceptions 21. No E1 directory/RegionExit amendment is composed.
Nor are CG1, F/array, RootPin or constant-request profiles composed.
G's TypeSpec `10` and G1's Body record `10` remain different namespaces.

## Binding inputs and publication

Preserve the originating Module's existing
[`CoreContractFailureIdentitySelection`](core-contract-failure-identity-selection.md),
its checked class selection, and the construction owner's exact `0B` map.
Add one immutable candidate **`TrRmExceptionInterfaceBinding`** per originating
using Module requiring selected exception-class evidence:

| Field | Required meaning |
|---|---|
| `UsingModuleIdentity` | Original semantic Module, not current physical MethodDef placement or importing caller. |
| `ProviderIdentity`, `ExportKey` | Authorized exact public primary interface TypeDef for Core `IException<TSelf>`. |

Reuse an already checked interface handle only with the same pair, original
expectation and selected provider snapshot retained. This is the independent
interface context already required by the semantic selection owner, **not**
an extension of E1's `CoreExceptionIdentitySelection`: it selects no
Unreachable payload or E1 profile. Apply the existing ordinal, nonempty
scalar-text/NUL rules, authority checks and immutable snapshot discipline.
Reject missing, duplicate, malformed or conflicting rows; validate supplied
unused rows too. Different using Modules retain their own authorized choices.

Source-local publication needs explicit checked declaration-to-key inputs for
each independently exported class, interface and selected callable/member.
Use `(source input ordinal, full declaration span, definition kind, ExportKey)`
against the exact bound program and ordered source snapshots; require complete,
unambiguous mappings, allowing separately supplied aliases to the same
declaration. Do not derive keys from namespace/name, constructor spelling,
member ordinal or a generated stub. This is a candidate C1 host input contract,
not a new CLI/JSON grammar or adoption of G1's version-3 map. Existing checked
explicit mappings may supply these facts without adding another key namespace.
Imported declarations retain their checked reference's actual keys, original
expectations and provider artifacts; no provider source/span is required.
An imported proxy cannot be selected as a source-local export.

Every retained cross-artifact declaration edge uses its ordinary ModuleRef
and TypeDef/MethodDef/PropertyDef/FieldDef Import/Export contract where assigned.
The class key invents no constructor/member key. An interface export selects
its declaration closure, not interchangeable MethodDef exports. Property
getter correspondence uses its selected PropertyDef where supported; it need
not invent an independently exported getter. Private/default witness targets
remain provider-owned: retain their actual artifact/Module/typed-row origins.
If an edge cannot use an assigned Import, preserve direct definitions in a
packed provider closure or decline; never manufacture a private export or
write a cross-artifact row number into a local row field.

## Required class and witness closure

Before binding a mapped construction plan:

1. Resolve exact Module/revision, key and DefId kind first. Require the selected
   public provider-local nongeneric **class**, TypeDef kind 2, and independently
   selected **interface**, kind 3. Struct kind 1 cannot substitute. Reject proxy
   re-exports, same-name impostors and conflicting actual snapshots. Retain all
   original expectations, aliases, requirements, editions, target/runtime
   context and stronger facts used; producer authentication remains external.
2. Keep class handles, interfaces and structures distinct in the bound type
   system. Nominal TypeSpecs `02/03` identify the class or interface declaration,
   not a reconstructed struct. The interface instance uses `05` over its exact
   nominal `02/03` anchor with the complete class as self argument. Preserve all
   legal qualifiers and substitutions. Existential `0F` may identify common-view
   signature terms; it supplies no per-value existential evidence or runtime
   carrier. Body TypeMap tokens and metadata TypeSpecRefs retain their distinct
   grammars.
3. Validate TypeDef Module partitions, field/formal/Layout/Contract ranges,
   MethodDef owner agreement and the required default Layout, including for
   interfaces. Require exactly one InterfaceDef per interface TypeDef. The
   selected IException has exactly one owned type formal at ordinal zero and
   `selfGenericParam` names it; other interfaces keep their own formal/self
   contracts. Validate all base/member/associated ranges and direct
   InterfaceMethod ordinals; those ordinals are not keys or dispatch slots.
   Use checked row/range arithmetic, global rows
   `1..00FF_FFFF`, reserved-zero high bytes and empty ranges `(0,0)`.
4. Resolve the implementing primary authority's unambiguous semantic witness
   keyed by **complete `(C, selected IException<C>)`**. Both implementing type
   and substituted self must equal C after binding, not merely share a name.
   Consumer proxy evidence is an expectation, not authority to publish a new
   conformance. Bind all inherited declaring-interface/requirement identities
   and exactly one selected implementation/default per requirement. Validate
   exclusive WitnessMember ranges, exact MethodDefs, selected owners, formal
   correspondence, access, modes, effects and dependencies. Equivalent physical
   encodings do not create another nominal witness; conflicting mappings reject.
5. Retain the complete common contract: Message/DefaultMessage, creation paths,
   causes, formatter and their selected declarations, types and witnesses.
   Assigned rows do not excuse omitted properties, static roles, associated
   bindings or projection evidence. Decline any required member/reference codec
   not actually supported; do not relabel it as an ordinary instance method or
   erase it because the terminal path does not observe it.

These are mandatory structural/support/binding checks under
[reader trust](../specs/representation/reader-trust.md) and
[directed satisfaction](../specs/representation/binding-and-replacement.md),
not mandatory semantic re-proof of arbitrary provider Bodies. The assigned
[row/reference owner](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [witness contract](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain)
are unchanged; unsupported associated/projection codecs gain no meaning here.

## Source-free retention and transformations

Load/reference results retain class/interface declarations, complete witnesses
and every expected/actual origin as typed bound facts, not names or accepted
Booleans. Bodyless Imports retain full supported expectations and witness
correspondence without fake Bodies or zero-field class stand-ins. They still
need the construction map's explicit bodyful plan-bearing input and actual
provider implementation; bytes plus role/anchor tables alone are insufficient.

The C1 emitter returns the role, interface-anchor and construction contexts
alongside bytes and selected provider inputs. Re-emission rebuilds Module
partitions, TypeDef/Import/Export, Layout/field/Contract ranges, TypeSpecs and
binders, InterfaceDef/base/method/witness/member relationships, requirement and
target MethodDefs, Body maps and `0B` indexes, graph/phase correspondence and
Feature lists. Clone context-sensitive structural rows when necessary; remap
or discard affected optional debug data under its rules. Preserve original
expectations even after Import-to-direct resolution. Snapshot passthrough is
not remapping evidence.

Replacement checks every retained expectation and alias equality against the
new actual closure, including witness targets and relied-on stronger facts.
It never reruns overload resolution or selects another class/interface/key.
Live objects keep actual allocator/runtime/provider/code dependencies.
Keep the construction owner's zero-formal initial admission, genuine
constructed-result projection, once-only phase ordering and terminal
same-runtime `runtimePolicy=0` route unchanged. C1 supplies neither a handler
nor a stable managed/generic ABI. Existing artifact commands without explicit
C1/context transport refuse before output replacement or worker launch.

## Separate gaps and acceptance

This proposal completes only the profile and exact binding-input decision.
It deliberately leaves these independent prerequisites visible:

| Gap | Remaining action |
|---|---|
| Standard class/witness implementation | [`EmitStandardCore`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) rejects witnesses and executable classes; `FromNativeValueRow` reconstructs TypeDef/Import through structure indexes. [`CreateStandardReferenceCore`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) projects functions, not a class/witness closure. The separate [C1 metadata-only class declaration carrier](standard-trrm-c1-class-declaration-carrier.md) proposes retaining complete declaration/expectation facts without fabricating executable `BoundClass` layout. Implement typed publication, binding, traversal, verification and remapping rather than weakening those refusals. |
| Class mutability and layout | The unapproved [C1M1 amendment](standard-trrm-c1-class-declaration-mutability.md) assigns `0x80` to the class declaration fact; its producer/consumer implementation remains open. Field mutability or a TypeSpec `write` bit cannot supply that fact. Existing Layout rows encode declared inputs, not managed object headers, offsets or a runtime ABI. |
| Complete member/value/construction closure | G1's map does not complete interface accessor/static/default/projection codecs. Preserve any missing exact role/correspondence as a separate gate. Constructor projection/initializers/defaults, durable Text, common-view per-value evidence and partial cleanup remain the construction owner's independent prerequisites. |
| Managed runtime | [Backing/root/cleanup carriers](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) and actual completed-object event/origin support still need an explicit supported representation/runtime agreement. Joint relowering, Feature 23 or private managed-class tests cannot assign one. No guessed header, parent slot, root convention or GC/stable-handle alias is selected here. |

Future acceptance must cover:

- Explicit C1 publication and source-free class/interface/witness references
  using unrelated declarations, non-name-derived keys and non-first providers;
  bodyless expectations plus the mapped real plan, with all source/debug absent.
- Complete formatter method-formal closure and per-Module 23/17/10/11/13
  detection as applicable, including uncalled/imported declarations. A mere
  owner-generic interface does not select G1. Existing B+I+G, G1 and G1-v3
  mappings never auto-promote to C1.
- Wrong class/interface kind, wrong self/anchor/requirement/target, missing or
  duplicate member bindings, bad owners/ranges/reserved bits, absent private
  closure, missing context and incompatible replacement all refuse.
- Several originating Modules and alias keys, row/binder/Body-map permutations,
  source-free re-emission/reconsumption and all original-expectation checks;
  boundary rows and unsupported member/mutability/runtime carriers.
- Independently qualified phase-2 JIT/PE execution only after those separate
  gates: zero/one real construction, actual class/self-witness/object identity,
  complete Message, roots, cleanup and raise origin. Decoding, emission or a
  nonzero exit alone establishes none of these.

No source syntax, accepted specification, registry allocation, support inventory
or milestone status changes in this proposal.
