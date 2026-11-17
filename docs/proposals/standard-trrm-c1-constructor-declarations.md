# Standard TRRM constructor declarations and projections

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This selects a metadata/Body mapping, not
implementation support, source syntax or an object/runtime ABI.

## Decision, evidence and scope

Recommend one required **MethodDef-owned Construction Contract**, with two
roles: the source constructor's instance initializer **I**, and an explicitly
associated complete-construction projection **P**. I remains the constructor
declaration identity and owns its real implementation. P, when needed,
returns a completed class value; it is not another source constructor.
Retain ordered parameters and ordinary contracts in existing MethodSig/Param
and Contract rows, and implementations in existing Bodies.

This resolves the decision in
[class source publication](standard-trrm-c1-class-source-publication.md#constructor-representation-decision).
That proposal still requires complete class/member closure, not field-only
success. The existing [class carrier](standard-trrm-c1-class-declaration-carrier.md)
retains declarations without computed object geometry.

| Source evidence | Consequence |
|---|---|
| [`BoundFunction`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs): `IsClassConstructor`, `ClassConstructor`, `DeclaringStruct` | The host distinguishes an unpublished-receiver initializer, a factory's selected initializer and its nominal owner. Those flags/indexes/name strings are not serialized constructor identities. |
| [`Binder.ManagedClasses`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs), [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Checked classes require an explicit constructor. Its unit-result initializer and the separate Boolean/out `TryCreate` adapter cannot be published as interchangeable declarations. |
| [Metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [Body `0B`](../specs/il-instruction-set.md#typed-operand-records) | MethodDef identifies owner/signature/Body, but its flags encode visibility only. `0B` selects an already-bound exception-construction target returning the class; it is not a constructor declaration or initializer-to-result association. |

Preserve accepted [constructor grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
[minimum completion guarantees](../specs/language-contracts.md#22-declared-outcome-relationships)
and [construction/publication](../specs/language/ownership-and-cleanup.md#construction-and-publication).
Scope the physical candidate to C1's ordinary nongeneric class constructors,
not struct construction, static initialization, generic-class storage,
property accessor codecs or interface/witness redesign.

## Allocation and exact agreement

Recommend the distinct, explicitly **unapproved** exact successor identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1`**

Call it **the constructor successor** below. Its complete schema is exactly
[C1M1](standard-trrm-c1-class-declaration-mutability.md#exact-schema-and-proposed-assignment)
plus the Construction changes in this document. **C1M1 remains exactly C1
plus its class-mutability amendment**: its token, inactive Transfer semantics
and bounded type-only class reference behavior do not change.

[`TrRmStandardArtifactInput`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs)
carries only a schema-identity string and bytes, not a second agreement
revision. Select the new literal ordinally and case-sensitively for the root
and **every supplied provider**, including unused inputs, before
schema-dependent decoding/binding. Mixed selections, C1M1 inputs and attempts
to use successor inputs through a C1M1 operation refuse; no fallback, alias,
component parsing, subset retry or row/Feature sniffing is defined. Existing
API/CLI defaults remain unchanged. A missing successor selector never opts in.

Keep `(1,0)`, edition 1, Body codec/encoding `01` and existing catalog edges.
No new Feature is needed for this required declaration role; independently
retained graphs, generics, native contracts and runtime uses keep their gates.

Select **Contract kind `0003 = Construction` only in the constructor
successor**, replacing the inherited semantically inactive Transfer shape. The
[registry](../specs/representation/registry.md) assigns `0001..0005`,
`000A..000C`; `0003` currently has no admitted transfer semantics. Reclaiming
it makes the call/state family Effects–OutputState–Construction–Dependency–
Cleanup coherent without another identity/table. It does not activate or
preserve the old Transfer payload.

| Alternative | Assessment |
|---|---|
| **One tagged MethodDef Contract; selected** | Existing required Contract ranges support bodyless expectations and variable completion sets; source declaration and projection share one role family. |
| New unassigned clause, e.g. `0006`, or separate kinds for I and P | Viable, but no width/capacity benefit. Two kinds duplicate one lifecycle family; choosing unused space is not intrinsically preferable to retiring inactive Transfer. |
| MethodDef role bits/new columns or a ConstructorDef table | Bits alone cannot carry completion/correspondence. Columns widen all rows; another declaration table/DefId duplicates MethodDef identity. |
| OutputState, static InitializationRole, predicates or Body `0B` alone | Their subjects/roles differ. Graphs retain constructor clauses but do not classify I/P; `0B` remains a selected construction plan. Do not reinterpret a parameter as the object receiver or a static group as an instance. |

Only the new successor reallocates `0003`; C1M1, accepted specs and other
draft selections retain their existing meaning. Contract `0003`, Body record
`03`, Feature 3 and Body record `0B` are distinct namespaces. No opcode,
escape, flag bit, section or DefId
is allocated. [Exact draft agreement](../specs/representation/format-versioning.md)
is required despite unchanged version bytes. No conversion or relabeling of
C1M1 artifacts is defined; complete constructor facts cannot be recovered
from its type-only projection or inferred from a missing clause.

## Exact Construction clause

Use the existing header `u32 ownerRow, u16 clauseKind=0003, u16 flags=0`.
`ownerRow` is a typed MethodDef row, not a DefId. All integers are little-endian:

| Role | Entire payload after the eight-byte header | Total extent |
|---|---|---|
| `1 = InstanceInitializer` | `u8 role=1, u8 flags=0, u16 reserved=0, u32 initializedCount`, then `initializedCount` ordered `u32 memberDefId` | `16 + 4*initializedCount` bytes |
| `2 = CompleteConstructionProjection` | `u8 role=2, u8 flags=0, u16 reserved=0, u32 initializerMethodDef` | Exactly 16 bytes |

Require exactly one clause on each retained I or P, including imported
expectations; at most one on any MethodDef. Absence grants neither role nor
initialization authority. Unknown tags, nonzero flags/reserved values,
duplicate clauses, trailing bytes, bad ownership and unsupported required
facts refuse affected use. No clause may belong to a native function-pointer
contract anchor. All typed rows are `1..00FF_FFFF`, high byte zero.

For role 1, each `memberDefId` is FieldDef kind `14` or PropertyDef kind `1E`,
with a nonzero bounded row and the **same immediate class owner**. Subjects
are instance members, never static fields/properties or another object's
member. They form a duplicate-free set; order has no semantic meaning.
Canonical writers sort unsigned DefIds after remapping. Counts are full
`u32` counts, not 24-bit row references; check multiplication/extents and
owned-member capacity without imposing the host's 32-field/128-byte limits.

Each listed subject is guaranteed initialized as its exact complete declared
type on normal I completion. Include every direct-member guarantee of the
checked declaration; zero means **no additional guarantee**, not all vacant.
Keep full source pre/postconditions in C1's graph clauses, including runtime
checks and phase order. Conditional/nested/richer guarantees remain in their
exact supported records, never flattened into this unconditional set.
Property entries require their complete initialized-place/backing/accessor
contract; PropertyDef's getter reference alone does not supply that contract.
The unapproved [PI1 proposal](standard-trrm-c1-property-initialized-state.md)
offers a bounded Construction-1L1R1/Construction-2L1R1 candidate. Until that
contract is reviewed and implemented, missing required property codecs refuse
rather than substitute a FieldDef.

## Declaration, receiver and Body mapping

| I: source constructor initializer | Required representation |
|---|---|
| Identity/owner | The exact checked source constructor maps to one MethodDef I with `ownerTypeDef=C`, class kind 2, zero C generic formals and zero MethodSig generic formals. Preserve source access and explicit Export aliases. |
| Signature | Represent I with unit result and a nonzero receiver TypeSpec resolving to C, with its checked legal qualifications. This is a chosen initializer representation, not a new source constructor result declaration. Params contain only the source parameters, in exact order/modes/types, including logical unit parameters. |
| Authority | The role grants initialization-phase authority over the **unpublished object**, independently of ordinary `write` or declaration `mut`. It does not initialize/rebind a class-handle cell, permit reinitializing a live object or grant ordinary mutation after publication. `ref<init C>` retains its distinct cell meaning. |
| Implementation | A local actual I owns one nonzero Body row, kind/encoding `01`. Emit the real checked declaration-initializer prefix, constructor preconditions, authored body and normal postconditions in their required order. Retain all calls, fields, graphs, effects, dependencies and actual implementation origins. |
| Reference/partial | A bound expectation has `bodyRow=0` and the complete role/signature/guarantees. A source partial primary maps to the same I as its unique implementing secondary, whose real Body is retained. A missing secondary is a source error, not permission to publish a bodyless actual. |

Resolve `DeclaringStruct` through the source capture's checked nominal owner,
not name lookup. Map `IsClassConstructor` to role 1. Map source/body receiver
slot separately from explicit parameter ordinal zero: the receiver is not
a Param row. No imported index, host variable number or optional source path
is a durable declaration identity.

Use existing **general CallSite** records for I: logical input zero is its
receiver with `sourceUse=5`; remaining inputs follow Params. This explicitly
extends that source-use's receiver applicability only for role 1. Require a
fresh construction-owned, unpublished object receiver; ordinary live C,
handle-cell init storage or an arbitrary pointer cannot satisfy it.
Invoke one source constructor per fresh construction, not a second initializer
over an already initialized object. Compact calls cannot express this formation.

The existing receiver Home/TypeMap/BlockEntryState shapes remain: a receiver
Home has role 1 and zero flags, and a register advertises the nominal handle
as carrier kind 0, not a new type/carrier code. The handle cell is live;
**object/member completeness and unpublished authority are distinct retained
state**, established by the role and checked producer facts, not by carrier 0.
Preserve that state through moves, CFG joins, calls and reconstruction.
It authorizes eligible initialization operations, never whole-object escape
before final completion, live-object replacement or an unsafe operation.
Supporting only ordinary live-handle flow is insufficient for this Body use.

I completion publishes its minimum member facts to the construction context,
not a completed C result. Subsequent caller initializer entries keep ordinary
replacement/invalidation rules; do not rerun I's postconditions. Required
final validation remains a separate publication gate.

## Complete-construction projection and source keys

Role-2 P has the same owner C, no receiver or own formals, and Params
corresponding one-for-one to I's explicit parameters, using its own
MethodSig/Param rows rather than copying receiver or owner indexes. Its nonoptional result
is the checked complete C type, including legal access/backing qualifications.
Its `initializerMethodDef` is a typed local row naming role-1 I in the same
Module and class, not an Import token, Body index or another projection.
An expected P references its local expected I; I's ordinary binding supplies
the cross-artifact identity.

P is the checked ordinary **whole-construction** recipe for C and selected I:
preserve the once-staged source inputs, establish fresh unpublished backing,
invoke I once, perform final completion/validity checks, then publish the
complete result. Normalize this real program before row assignment; it need
not already exist as a host function. It has **no caller initializer entries**;
constructions needing those retain their actual call-site program. An
incomplete constructor still retains I, not a fabricated complete P.
Fresh-backing operations/helpers require their own complete selected
representation and execution agreement; P requests no default allocator
and cannot hide missing operations in an opaque intrinsic Body.

I and P have distinct real MethodDefs/Bodies. P may inline I only through
a checked transformation preserving I's identity, original clause origins,
phases and every relied-on fact. Preserve source access/caller obligations
including I's caller-unsafety; derive P's actual effects from the real program.
Translate receiver-capture obligations into P's result
dependencies with the same formal/source/backing identities. Failure-side
captures/cleanup remain, not just normal-result relations. No inferred Copy,
lifetime extension or source requirement weakening follows from projection.

The source member selector's `ExportKey` selects **I**. Add this optional,
unapproved input property to `TrRmSourceClassMemberExport`:

```csharp
public string? ConstructionProjectionExportKey { get; init; }
```

Only a constructor selector may supply it. It explicitly exports that
constructor's checked P; if the ordinary recipe cannot establish completeness
or lacks a required representation, refuse rather than generate a success
stub. Capture/validate it with the original exact
program/primary/span association and Module-wide ordinal key rules. Aliases
may select the same I/P with distinct keys. No key is derived from another,
and neither a class key nor I's key aliases P.

A public P requires a public I/public class and that explicit P key; without
one, P is a private closure-only implementation, not a source lookup member.
A provider-owned private P remains directly referenced in its retained or
packed provider closure; otherwise decline the cross-artifact publication.
This adds no private re-export or default projection-key convention.

`BoundFunction.ClassConstructor` supplies candidate association evidence,
not proof that its Boolean/out `TryCreate` is P. Normalize a genuinely used
adapter into its real checked program where supported; never relabel its
signature, false-result failure channel or empty intrinsic Body. P must
actually return complete C under the selected construction semantics.
The [construction handoff](standard-trrm-initialization-predicate-postcondition-construction.md#bind-the-selected-plan)
and unchanged `0B` target P, whose clause binds the selected I. Preserve
`0B`'s exact zero-supplied/default/empty-params recipe; other arguments are
ordinary checked construction, not a new `0B` variant.

## Binding, failure and transformations

Resolve exact Module/revision/key and class/MethodDef ownership first.
Compare roles, normalized receiver/result/ordered parameter types and modes,
formal correspondence, access, effects, graphs, dependencies and formation
obligations under [directed binding](../specs/representation/binding-and-replacement.md).
P must retain the **same selected I**, not another matching constructor.
Each expected initialized-member guarantee must occur in the actual set
under checked owner/member correspondence; additional guarantees may satisfy
this dimension only while all old caller-domain/phase obligations remain.
Unlisted members are not required vacant. Check all original expectations,
aliases and stronger facts relied on, including after an earlier resolution.

On failure before publication, retain actual argument/initializer/constructor/
validation failures and reverse actual acquisition cleanup where unwinding
is admitted; never run incomplete-parent Cleanup, fabricate a C/default or
roll back unrelated effects. Fail-fast supplies no unwind. An independently
admitted DoesNotReturn constructor keeps its declaration/signature but
publishes no normal guarantee/result on an absent normal edge.

Rebuild MethodDef/Contract/Body and class/member/Import maps together.
Remap role-2 typed I rows, role-1 kind-directed member DefIds, signatures,
graphs, clause origins, receiver/parameter Homes, every Body map and `0B`
target/record context. Clone context-dependent rows; preserve original
expectations after Import-to-direct packing. Keep partial implementation
origins and any required semantic source-unit authority; optional debug
SourceMaps cannot replace them. Use source-free typed facts, not snapshots
passed through without remapping.

Producers establish truthful completion/Body facts. Consumers perform
mandatory framing, extent, namespace, owner, required-support and binding
checks before use; they need not re-prove arbitrary Body truth. Resource
exhaustion explicitly declines. Invalid caller selectors/keys use argument
errors, malformed serialized roles/bindings use `InvalidDataException`, and
unsupported required representations use `NotSupportedException`. No failure
returns a partial artifact/reference or launches a worker to discover support.

## Integration and focused cases

On adoption, the [registry](../specs/representation/trrm-1.registry.json),
[clause owner](../specs/representation/contract-clauses.md) and
[metadata](../specs/il-metadata.md) would register the successor-only `0003`
reallocation and the above occurrence/reference/absence rules. The
[Body owner](../specs/il-instruction-set.md#calls) would add role-1 receiver
formation/state semantics without changing bytes. Grammar, source completion
and binding owners retain their semantics; schema selection and the source
input owner would route complete publication through the new exact identity.
C1M1's composition and type-only route remain unchanged. No accepted owner
is edited here.

These are future conformance cases, not test results:

| Case | Required outcome |
|---|---|
| Real source | Round-trip an ordinary immutable/mutable class with a private initialized field, explicit bodyful constructor and ordinary method. Retain I and real Body, exact public keys, ordered zero/unit/nonzero parameters and P only when genuinely complete. A field-only result is not producer acceptance. |
| Roles/authority | Readers reject wrong owner/kind, missing actual Body, duplicate/unknown roles, bad extents/reserved bits, count overflow, tag-2-as-count decoding, I-as-P and incompatible factory signatures. Source checking rejects live/cell receivers, repeated constructor invocation, compact initializer calls, premature escape and post-publication initialization authority. |
| Completion/partial | Preserve every minimum guarantee and normal graph; reject duplicate/static/foreign-owner subjects and unsupported property contracts. A partial primary needs its real secondary; imported bodyless expectations bind bodyful actuals. Added guarantees can satisfy old callers; removed ones fail. Missing normal completion publishes none. |
| Plans/failure | `0B` binds exact P→I with zero supplied arguments; defaults require their actual codecs. Declaration initializers precede I clauses/body; caller initialization/final validation retain ordering. No fake complete value on constructor/validation failure, replayed postconditions or incomplete-parent Cleanup. |
| Providers/remap | Non-first providers, several using Modules and aliases retain separate expectations. Permute class/member/MethodDef/Contract/Body rows, pack Import-to-direct references, reconsume without source, and reject a retargeted P or weakened completion after prior success. |
| Exact agreement | Exact successor root/providers pass selection; reject C1M1 or mixed providers, including unused ones, and successor data selected as C1M1. Existing C1M1 type-only references retain their old behavior. Its Transfer payload is not Construction; no same-token extension, fallback, byte/Feature sniffing or implicit conversion establishes successor support. |

This recommendation leaves no constructor-role/initializer/projection
association choice open. Formal approval, implementation and qualification
remain pending. Property initialization/accessor closure, richer defaults,
generic-class/static-constructor scope and complete witnesses retain their
own exact codec gates. Object allocation, headers, GC/root representation,
native calling convention and execution agreement remain **unselected**;
this metadata does not discharge those C1 prerequisites.
