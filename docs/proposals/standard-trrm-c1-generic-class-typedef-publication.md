# Standard TRRM generic class TypeDef reference publication

> [!IMPORTANT]
> **Unapproved proposal; provisional direction under the user-authorized
> missing-contract workflow.** This proposes a distinct source/reference
> operation. Existing C1M1 support does not approve generic class publication,
> and this document establishes no implementation or runtime qualification.

## Finding and bounded decision

There is a real **publication-promise decision**, not a missing TypeSpec
encoding: may checked generic class source produce an explicitly type-only
reference artifact, rather than a complete class implementation/publication?

**Recommend yes, through a separate opt-in operation.** Publish the complete
generic **TypeDef-declaration closure** needed for nominal identity, type
formation, forward substitution and metadata comparison. Do not promise
constructor/member lookup, construction, invocation or executable instances.
Represent unavailable member surfaces explicitly in the bound reference;
they are not known-empty class member lists.

This is not successful complete C1 class publication with its constructors
removed. It is a reference-artifact role with a smaller, explicit
independent-use promise. Source remains fully checked, and every fact
required by that promise remains complete. The distinction permits a bounded
decision without designing generic constructor roles or an object ABI.

### Authority and the necessary amendment

The [specification index](../specs/README.md) leads to the accepted
[class/generic grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
[complete types](../specs/language/types-and-normalization.md),
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
[binding/replacement](../specs/representation/binding-and-replacement.md)
and [format lifecycle](../specs/representation/format-versioning.md).
No accepted text is replaced. In particular:

* Generic class declarations already have source grammar and owner-scoped
  type/constant formals. Missing hosted support is not an undecided language
  syntax or substitution rule.
* An ordinary authored constructor still requires its checked body; a partial
  primary still requires its actual implementing secondary. A reference
  artifact does not legalize bodyless authored constructors.
* Binding preserves every retained validity, ownership, representation and
  public-operation obligation. The accepted reference-facade allowance does
  not permit omitting an implementation's fields or promised constant Bodies.
  This candidate retains all fields and narrows only the explicit artifact
  promise; it does not select resilient layout.

The interacting **unapproved** owners require these precise dispositions:

| Owner | Proposed disposition |
|---|---|
| [C1](standard-trrm-exception-class-witness-binding.md) | Preserve full exception class/interface/witness closure and its selected contexts. A type-only generic declaration cannot stand in for that complete contract. |
| [C1M1](standard-trrm-c1-class-declaration-mutability.md) | Explicitly extend candidate type-only source/reference admission to generic class TypeDefs under this document. Keep `DeclaredClassMutable=0x80`, exact Boolean comparison, the complete catalog and every existing wire meaning unchanged. The extension is not already implied by C1M1. |
| [Class carrier](standard-trrm-c1-class-declaration-carrier.md) | Reuse separate declaration payloads and existing nominal identities; permit nonempty formal ranges for this role. Retain the distinction between a TypeDef-only view and a complete member/witness publication, without executable `BoundClass` stand-ins. |
| [Class source publication](standard-trrm-c1-class-source-publication.md) | Keep its existing full-publication operation, member keys, real implementations and constructor-successor requirement unchanged. Qualify its rejection of field-only source success only for the **new, explicitly requested reference-artifact role** below; that result is never full-publication acceptance. |
| [Constructor declarations/projections](standard-trrm-c1-constructor-declarations.md) | Preserve the distinct successor, I/P correspondence, real Bodies and zero owner/method formal restrictions. This route publishes no constructor declaration, projection, `0003` Construction clause or `0B` plan. |
| [Generic TypeDef source closure](standard-trrm-generic-typedef-source-closure.md) | Leave its G1 generic struct mapping and class-publication exclusion unchanged. Reuse shared capture/remapping mechanisms, not its schema or producer applicability. |

This is an explicit, in-place **candidate C1M1 admission amendment**, not a
new binary schema or a claim that the old agreement already selected it.
Exact exchange agreement must include this role and its restrictions.
No new schema literal, row, flag, Feature, codec, version, compatibility
reader or alias is proposed. Unpublished compatibility is not required.

### Evidence and alternatives

These are source observations, not test results:

| Evidence | What remains |
|---|---|
| [`Binder.ManagedClasses`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs), [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | `DeclareClassName` rejects generic class source. The existing source-class association anchors layout-bearing `BoundClass` snapshots. Implement checked open declarations without treating an observed closed class layout as the definition. |
| [`Class carrier`](../../sources/Terrum.CodeAnalysis/Semantics/BoundStandardClassDeclarations.cs) | `IsValidTable` currently requires an empty nominal formal list. Its `TryGetDeclaredMutability` already distinguishes an unavailable fact from a known Boolean; the out-value alone is not authority. |
| [Current class source summary](../implementation.md), [class publication proposal](standard-trrm-c1-class-source-publication.md#required-constructor-and-member-closure) | Bounded nongeneric type-only metadata paths do not select generic class source publication. Conversely, complete publication's required constructor/member implementation is not a prerequisite for every possible reference-artifact promise. |

| Alternative | Assessment |
|---|---|
| Remove generic guards and call existing class emission complete | Rejected: loses the source/member/publication distinction and treats missing mutability or member facts as defaults. |
| **Separate TypeDef-only reference operation** | **Selected.** Existing rows carry the promised facts; unavailable operations remain unavailable. Requires an explicit C1 admission decision, supplied here, not constructor or runtime invention. |
| Extend complete C1 publication to generic constructor/member declarations and implementations | Coherent larger direction, but requires extending the constructor owner's zero-formal contract and preserving its complete open implementation closure. Not needed for the TypeDef-only promise and not selected. |

## Exact operation and source authority

Require exactly the existing ordinal, case-sensitive C1M1 selection:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1`**

Keep its `(1,0)`, edition 1, TypeDef mask and inherited catalog. Require that
selection for the root and every supplied provider before schema-dependent
indexing, including unused supplied inputs. C1, G1/G1L1, CG1, constructor
successors and private inputs neither alias nor automatically promote to it.
In particular, this does not compose G1's separate Feature 27 amendment into
C1M1 or allocate a generic-class Feature.

Add a distinct candidate library operation,
**`EmitStandardGenericClassTypeDeclarations`**, taking the existing emitter
inputs `program`, `moduleIdentity`, `schemaIdentity`,
`moduleTargetRequirement`, plus a nonempty immutable capture of
`classTypeExports`. Each candidate **`TrRmSourceGenericClassTypeExport`**
mapping carries:

| Input | Meaning |
|---|---|
| `Program` | Reference-identical checked program, with compiler-owned declaration/source association. |
| `SourceInputOrdinal` | Original zero-based ordinal in that program's ordered immutable source snapshots. |
| `DeclarationSpan` | Exact full primary ClassDeclaration node span; checked nonnegative start, positive length and nonoverflowing end within the snapshot. |
| `ExportKey` | Explicit producer-selected public TypeDef key. |

There is no member-key array or construction projection selector. Do not
reinterpret an empty `TrRmSourceClassExport.MemberExports` as this role.
Existing `EmitStandard` overloads and defaults remain unchanged; no CLI file,
option or default schema is selected.

Select public, namespace-level, locally authored ordinary generic classes:
nonempty ordered formal range, optional nominal `mut`, not static/unique
classes or class unions. Preserve private/internal required type dependencies
where representable; they gain no lookup-root or export access. Unencoded
file/friend, static-class and other ownership roles are refusal gates, not
erased modifiers. The ordinary static FieldDef bit does not publish a static
initialization policy or execution route.

Capture and check the whole composed source declaration, including real
constructor/member implementations and their actual partial-source ownership,
over the declared formal domains. Checking needs no physical object layout.
Syntax-only checking, an unverified declaration, a generated factory or one
closed specialization is insufficient. Unsupported source checking remains
an implementation failure; this proposal does not claim it exists.

Freeze mappings and resolve exact source anchors before internal rewrites.
Copies, rebinding, loading or rewriting cannot acquire source authority from
coincident names/spans. Supported transformations carry explicit compiler-owned
correspondences or recapture. Distinct keys may alias one primary; repeated
keys reject. Apply existing nonempty, NUL-free scalar-text/String-capacity and
Module-wide ordinal uniqueness rules, without deriving or normalizing keys.

This operation explicitly projects selected type roots and required closure
from checked source; it is not the full-publication map over every class.
Omitted unrelated source members/declarations are not advertised exports.
It cannot take loaded/full publications and silently erase their existing
retained promises or rekey imported definitions.

## Published declaration and unavailable surfaces

The output is a reference artifact with no entry point or executable class
Body. It promises the following complete TypeDef-declaration information:

* Existing nominal definition identity, exact namespace/name/access and class
  kind, with source tokens assigned to the supplied output Module only.
  Imported definitions retain their original checked Module/provider origins.
* Every ordered GenericParam, its TypeDef owner and total ordinal, kind,
  ownership/nullability domains, constant domain, default term and complete
  supported Constraint range. Reuse existing row meanings and earlier-only
  default scope; formal names do not establish correspondence.
* Every FieldDef, including private fields, with exact owner/name/access,
  static/mutable roles and complete open type; all complete Layout variants
  and ordered instance-field membership. Require exactly one default, at most
  one variant per exact profile/revision, and every instance field exactly once
  in each variant, excluding statics. Fresh unannotated source classes use
  the existing default Auto/no-Blittable/alignment-zero/pack-zero declaration
  rule. No struct Layout attribute applicability or class geometry is added.
* All identity/formation/validity/ownership/representation dependencies
  required to interpret those facts. The view is not allowed to omit an
  authored conformance, Cleanup role or type-level obligation and then
  manufacture negative evidence from its absence. Required facts outside
  supported C1M1 closure refuse publication.

Type formals use existing kind 1/domains; constant formals use kind 2, zero
type-domain tags, an exact constant-domain TypeSpec and no type constraints.
C1M1's inherited bounded G1 constant path remains fixed-integer/Boolean with
tag-1 SF1 values; CG1 nominal/open constant terms do not join. Preserve every
default and typed constraint dependency, including on phantom formals.
Unencodable defaults, unsupported interface/associated obligations and Write
requirements lacking a selected C1M1 admission contract refuse rather than
becoming absent. Derive per-Module requirements from the unchanged catalog;
owner formals alone do not require method-call Feature 23/1.

Constructor/member declarations, field-initializer execution and their Bodies
remain checked source facts but are **not promised operations of this view**.
The source capture records that their surface is unselected/unavailable, not
that the class has no members. If a retained type-level contract needs one
of those declarations or a Body, it enters required closure and must be
represented by the selected supported contract or publication refuses.
No fake Body, Boolean/out factory or completed closed instance substitutes.
This operation selects no class-member Body carrier; a promise requiring one
therefore refuses rather than silently extending this role.

Consequently, a type-only class reference:

* Supports only its promised nominal/type-term binding, substitution and
  metadata comparison; it supplies no member lookup, constructor selection,
  witness dispatch, storage admission, field operation, Copy/cleanup
  inference from missing members, or runtime specialization.
* Must report an unavailable operation as unsupported, not as an authoritative
  empty overload/member set or evidence that Cleanup/conformance is absent.
* Cannot satisfy a retained full-class/member/witness expectation merely
  because its TypeDef matches. Additional provider operations are acceptable
  only under ordinary binding while every expected obligation still holds.

Absence of MethodDefs in this artifact is not a serialized claim of an empty
class. The explicit producer/reference operation establishes the consumption
scope; no wire `TypeOnly` bit or alternate interpretation of a row is needed.
Mandatory consumers still check required facts before affected use.

## Mutability, binding and source-free transformations

Keep **unknown**, **known immutable** and **known mutable** distinct in bound
origins and every expected/actual view. These are fact-availability states,
not new nominal identities or a three-valued wire field:

| Origin | Required handling |
|---|---|
| Checked source under this operation | Declaration modifier supplies a known Boolean; emit C1M1 `0x80` set/clear truthfully. Mutable fields or writable uses cannot supply it. |
| Selected C1M1 TypeDef/expectation | Clear means known immutable, set means known mutable. Compare exactly in both directions, independently for every original expectation. |
| Original C1/G1 or another schema without the fact | Preserve unknown where an opaque identity was already admitted. It cannot enter this complete C1M1 declaration view or be emitted there as immutable. Obtain a genuine admitted fact through a separately defined conversion, or refuse; none is selected here. |

Extend the explicit C1M1 type-root
`CreateStandardReference(input, moduleIdentity, providers, typeExportKeys)`
operation to these generic declaration views. Require selected public local
class exports, preserve each alias/key and original expected snapshot, and
retain provider-only definitions as closure rather than source lookup roots.
An output reference must retain its TypeDef-only/unavailable-member scope
through binding, shared traversal, verification and re-emission.

Bind exact Module/revision/key/kind and owner before comparison. Correspond
formals by owner/kind/total ordinal; compare supported formation domains,
default presence and normalized terms/typed values, constraints, declaration
mutability, access, complete fields and every declared Layout variant under
each side's original using/formal/substitution context. A callable-constraint
relaxation is not a nominal formation rule. Preserve each original expectation,
alias equality and stronger relied-on fact separately from actual facts.

Follow provider-owned Imports in their own using Modules. Required external
edges use checked selected keys; otherwise preserve actual definitions in a
checked packed Module closure or refuse. No private-key invention, proxy
re-export, foreign-as-local row or name/row-based identity matching is allowed.
Contextual comparison uses a finite worklist; mismatches beyond nominal
back-edges, malformed structural cycles and growing substitutions cannot
succeed by memoization or unvisited-edge cutoffs.

Rebuild Module/TypeDef/Import/Export partitions, exclusive GenericParam and
Constraint ranges, typed defaults/payloads, TypeSpecs `02/03/04/05` and nested
families, fields, all Layout/LayoutField ranges and supported Contract edges.
Constructed TypeSpecs retain the earlier nominal anchor and one kind-directed
argument per formal; no omitted argument or Body ConstantFormal tag fits a
constant slot. Clone context-dependent structural rows, remap typed
constant/value references and section/index offsets, and preserve original
obligations after Import-to-direct packing. Loaded re-emission requires no
source/debug data or source mapping; snapshot passthrough is not reconstruction.

Check schema support, framing, reserved values, bounds/ranges/owners,
references and complete selected bindings before returning bytes or references.
Consumers need not semantically re-prove producer-authored Bodies. Invalid
caller mappings, malformed serialized/binding input, unsupported complete
use and scoped resource refusal retain their existing distinct failure
paths; no partial successful reference/output is returned.

## Focused acceptance and remaining decisions

These are required future cases, not test results:

| Case | Required outcome |
|---|---|
| Source authority | Two unrelated mutable/immutable generic classes with checked bodyful constructors and ordinary methods publish TypeDef-only views without fabricated layouts/Bodies. Exercise partial composition, unused formals/defaults, aliases and stale/wrong-primary selectors. Generic source-checking rejection is not a passing publication test. |
| Promise boundary | Type signatures and substitution retain exact class/formal identities. Constructor/member/field execution, missing-member inference, constant execution without its Body and full-class/witness consumption refuse; the ordinary complete-publication operation still requires its real closure. |
| Facts and binding | Both mutability mismatches fail despite identical readonly fields. Unknown remains unknown and cannot promote to C1M1. Changed kind/domain/default/constraint, nested type origin/qualification, private field or Layout membership fails; corresponding formal renaming/row movement may agree. |
| Source-free closure | Bind a non-first selected provider across several using Modules/aliases with all source/debug absent; retain provider-owned Imports and every original expected view. Unsupported required conformance/ownership/member closure fails rather than disappearing. |
| Reconstruction | Permute definition/formal/constraint/field/Layout/TypeSpec/import rows, pack direct definitions and reconsume re-emitted bytes. Preserve mutability availability, TypeDef-only scope, complete ranges and all alias/original-expectation obligations. |
| Exact scope | Unamended C1M1 support alone, C1/G1/sibling inputs, unknown-bit defaulting, unencoded static/unique roles and accidental constructor-successor decoding do not opt in. No class size/header, allocation/GC ABI or generic-instance lowering is inferred. |

The explicit reference-artifact promise and C1M1 admission amendment are the
only new choices. They give a bounded TypeDef publication route; there is no
remaining higher-level C1 decision **for that route**. They do not discharge
an acceptance gate requiring complete generic class publication: that larger
operation still needs an explicit generic extension of the constructor/
member-publication contract and its real implementation closure.

Use from a differently selected G1 artifact still needs independently supported
exact-schema binding and representable retained facts, or refusal; this
proposal supplies no cross-schema conversion or G1/C1M1 composition.
Constructor/member runtime, object ABI, generic-class instance lowering,
broader codecs, CLI transport and normative adoption remain unselected.
No implementation, inventory/status or test changes are made here.
