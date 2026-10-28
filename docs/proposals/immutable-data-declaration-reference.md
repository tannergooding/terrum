# Immutable-data declaration references

> [!IMPORTANT]
> **Unapproved proposal.** This recommendation is provisional implementation
> direction under the current task authorization, not normative approval.
> It allocates no IDs, changes no accepted schema and establishes no current
> compiler support or conformance.

## Decision and scope

**Recommend direct DefIds only**, with these closed target sets:

| `ImmutableData.kind` | Proposed permitted definition kinds |
|---|---|
| `1=ResolvedConstant` | `InterfaceConstDef`, or `WitnessAssociatedConst` acting as the selected associated-constant binding definition |
| `2=PublishedImmutableRegion` | `StaticField`, with a separately admitted immutable-publication contract |

This deliberately proposes a definition-reference role for the existing
`WitnessAssociatedConst` row family. It is not currently a registered DefId
kind. Neither `InterfaceConstDef` nor `StaticField` has an assigned DefId kind
in the current registry. Their symbolic names here are **not numeric
assignments**, section-to-DefId conversions or reservations.

Keep the assigned `0x0041` section and 12-byte row:
`u8 kind, u8 flags=0, u16 reserved=0, u32 declarationDefId,
TypeSpecRef ownerTypeSpec?`. Do not add an Import-token alternative, a new
constant/region table, inline value bytes or publication snapshots.
Interpret “declaration” for kind 1 as either a fixed constant declaration or
its exact selected associated-constant binding definition.

The [logical owner](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
currently requires a shared DefId, kind-restricted targets and complete owner
substitution without enumerating the targets. The
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [registry](../specs/representation/trrm-1.registry.json) assign the row,
but their closed DefId and Import/Export sets do not supply these kinds.
The [current resolver](../../sources/Terrum.CodeAnalysis/Modules/TrRmDefinitionResolver.cs)
handles TypeDef, FieldDef, MethodDef, PropertyDef and the separate unapproved
AssociatedTypeDef candidate; none is a substitute for these targets.
There is no ImmutableData reader in that path.

This closes the declaration-reference decision, not the surrounding value,
witness, static-storage or publication codecs. The unapproved
[scalar registration proposal](scalar-frozen-value-registration.md)
registers a different path: `ConstantSource(kind=1) -> FrozenValueNode(kind=1)
-> Blob`. Those tags select a frozen value and its representation, not an
`ImmutableData(kind=1)` declaration. Its proposed scalar-only `11/1` does
not activate ImmutableData. Any richer registration must make its exact
capability/revision and prerequisite closure explicit.

## Target eligibility and identity

### Resolved constants

An `InterfaceConstDef` target denotes the fixed, initialized constant owned
by its declaring interface. Its `constantTypeSpec` and `constantSourceRef`
supply the exact substituted domain and value, or separately admitted required
target work. It is not runtime static storage and has no initializer to replay.
“Resolved” selects the declaration/binding; deferred value work must still
complete before any use requiring its materialized value.

A `WitnessAssociatedConst` target denotes one binding in one selected
`InterfaceWitness`. Resolve that witness first. Its
`declaringInterfaceTypeSpec` and associated declaration must select exactly
one requirement in the witness's closed inherited interface closure; the
typed source must satisfy that requirement's substituted constant domain.
Its binding identity is the resolved witness origin, complete implementing
type, complete witness interface instance, complete declaring interface
instance and selected AssociatedConstDef identity, not a binding-row number.
Retain applicable projection/origin dependencies under the
[associated-member contract](../specs/language/interface-witnesses.md#associated-types-and-constants).
An expected witness is not permission to invent a conformance.

An unbound `AssociatedConstDef` is **not** an allowed target. Pairing it with
only its declaring interface loses the implementing witness; pairing it with
only an implementing owner can lose which constructed interface was selected.
For example, bindings of the same requirement declaration through
`I<int32>` and `I<uint32>` remain distinct even for one implementer and equal
values. The existing bound row retains both identities without enlarging
ImmutableData. A projected use retains its selected origin binding and the
checked projection dependency; it does not fabricate a new binding or erase
the requested interface substitution. Unsupported projection representations
remain unsupported.

Also exclude TypeDef, FieldDef, MethodDef, PropertyDef, GenericParam,
AssociatedTypeDef, UnionCaseDef, InterfaceWitness itself, ConstantSource,
FrozenValueNode and Blob. A pure getter, constant-returning method, immutable
instance field or equal scalar payload is not a constant declaration.
Constant formals are substituted arguments, not newly resolved declarations.
Body-local named constants use their formed frozen values and retained
dependencies; they acquire neither DefIds nor addressable backing through
this proposal. Enum-member and other declaration families without an admitted
constant-definition representation remain outside this closed set.

Two different declarations/bindings remain different ImmutableData identities
even when their values agree. Conversely, row duplication does not create a
new declaration. Compare kind, resolved declaration/binding identity and
normalized complete owner construction or its prescribed absence.
Exact value facts remain obligations attached to that identity, not permission
to merge identities by numerical equality.

### Published immutable regions

Use the distinct logical `StaticField` definition required by
[static-storage records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending),
not an instance FieldDef, a FieldDef with its existing static bit, a property
getter or a generated initializer MethodDef. This proposes no StaticField
section, numeric DefId, flags, runtime handle or new region codec.

The target must additionally have an admitted contract identifying its
immutable observed extent, valid publication, backing and load lifetime.
A fixed slot can contain a handle to mutable state; readonly access, RVA
placement, Copy, Blittable or an eager-group completion fact alone does not
establish transitive immutability. Preserve the
[pure-read conditions](../specs/language/published-predicates-and-effects.md#aliasing-memory-state-and-sharing)
and [constant-input restrictions](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain).

This candidate covers a field-anchored published extent only when the
separate contract selects one unambiguous extent per closed owner and loaded
unit. LogicalInputView may select a permitted subregion through its existing
typed path; an offset, address or view row is not another declaration DefId.
Several independently published regions behind one field, arbitrary foreign
storage or non-field-backed RVA data require their own reviewed identity
carrier. Do not manufacture a hidden StaticField or guess a region codec.
Until the static/publication representation is complete, kind 2 remains
gated, not satisfied by the presence of a StaticField-shaped row.
Separate unapproved candidates now address the direct DefId assignments in
[ImmutableData definition kinds](immutable-data-definition-kinds.md),
the [StaticField/publication physical schema](immutable-static-field-persistence.md),
and [AssociatedConstDef identity](associated-constant-definition-identity.md).
They remain proposals: this document still selects no numeric kind or
StaticField section, and implementation must preserve its direct-only
ImmutableData reference.

## Exact owner substitution

`ownerTypeSpec` is the accepted four-byte complete TypeSpecRef, not a TypeDef
row, Body TypeMap token, witness reference or substitution-list index.

| Target | Owner from which substitution is checked |
|---|---|
| `InterfaceConstDef` | Its InterfaceDef's owning TypeDef; use the complete **declaring interface instance**, including its self argument where present |
| `WitnessAssociatedConst` | The origin InterfaceWitness's complete implementing owner; the witness/binding records retain the interface instances, while a projection dependency retains any requested view |
| `StaticField` | Its exact declaring TypeDef construction, not the accessing receiver's type |

Require zero exactly when this owner has no applicable type/constant formals.
An owner exists even in that case; zero means absence of substitution, not
absence of ownership. A nongeneric implementer's binding through a generic
interface still retains the full interface construction in its witness rows.
This makes the existing conditional wording an explicit presence rule, not
merely a canonical-writer preference.

Otherwise require a nonzero complete construction with the exact resolved
owner constructor and one ordered argument per formal, including unused or
representation-irrelevant arguments. This also applies when a bound witness
row already records a closed generic implementer: its owner construction
must agree, not introduce a second substitution. Apply that one substitution
consistently to the constant domain/source, witness and inherited declaring
interface terms, static field type and retained contracts.

Map corresponding provider/expected formals by kind and ordinal. Preserve
nominal/module/source-unit identity, argument representation, every admitted
qualification, constant domain and canonical value; do not strip them to
obtain matching carriers. An alias may normalize under the
[complete-type rules](../specs/language/types-and-normalization.md);
an unrelated equal-layout owner may not. A reference/pointer wrapper, missing
argument, wrong constructor or unrelated interface instance does not become
an owner by discarding its outer form.

Symbolic arguments are permitted only where the surrounding admitted generic
representation retains their exact formal owners/ordinals and checked
substitution context. Before a use requiring concrete evaluation or storage,
resolve all required actuals, values and witnesses. A bare existential,
unresolved witness or inferred default is not supplied by this field.
It adds no method-substitution list and cannot encode a method constant
formal as a declaration. Constant arguments retain their separately admitted
[domain/value identity](../specs/language/constant-generics.md#canonical-value-identity);
floating/decimal constants do not thereby become constant-generic arguments.

Reject both a missing required construction and a redundant nonzero owner
where this rule requires zero. Equivalent valid TypeSpecRef encodings compare
by normalized complete identity, not by word equality.

## Module and Import resolution

The four-byte `declarationDefId` has a permitted definition-kind high byte
and one-based low-24-bit row in `1..0xFF_FFFF`. Zero, unknown/unassigned kinds,
wrong-table rows and the reserved indirection kind are invalid. Never derive
a definition kind from a section number: section `0x0031`, for example, is
not an allocation of DefId kind `0x31`.

An Import token (`0x1C`) is not an admitted alternative in this field.
Instead, directly reference the consumer-local expected definition, then
follow its required Import binding, as the logical read-dependency family
already does for expected MethodDefs. A direct reference cannot bypass an
expected definition's Import.

The proposed **additions** to Import/Export target sets are
`InterfaceConstDef` and `StaticField` only. Their owners bind separately to
the exact selected provider owners. Expected constant definitions retain
expected domain/value contracts rather than locally authored initializers;
expected static definitions allocate no independent storage or initializer.
An export names a provider-local public definition with an accessible public
owner, never an imported expectation. Complete access/ownership and static
stub encodings remain prerequisites; this document adds no missing flags.

Do not add `WitnessAssociatedConst` to Import/Export. Its expected/provider
correspondence follows the exact selected conformance and binding in the
retained owner/witness contract graph. Its definition context is the selected
witness origin, not automatically the declaring interface's module. Nominal
witness origins follow their implementing declaration; built-in witnesses
require their exact admitted registration. The witness schema must represent
that origin and any expected binding unambiguously before this form is used.
Map each expected binding to the provider's selected binding for the exact
implementing/interface/declaring-member identities, not just an equal value.
An equal-valued binding from another implementer is not a replacement.
This adds no re-export, independent witness lookup or conformance synthesis.

ImmutableData has no Module cursor. Resolve it in every retained using
Module/formal context: the MethodDef owning a predicate footprint, the
owning constant request, or the request using a LogicalInputView. Resolve
ModuleRef identity and export key first, then perform the complete
[directed binding comparison](../specs/representation/binding-and-replacement.md).
Equal names, rows or values do not redirect a failed binding to another key,
owner or provider. Co-locating modules does not discharge access or import
obligations. Share a row only when its resolved meaning agrees in every
using context; otherwise clone it and remap its uses.

## Provider, replacement and lifetime obligations

- **Constants:** preserve the exact substituted domain and promised typed
  value, including nominal identity, signed zeros, NaN representation and
  decimal observations where the admitted codec promises them. Retain
  selected witness/binding and actual value/code/target dependencies.
  Completed frozen work needs no Body solely for replay; retained required
  evaluation needs its admitted program, inputs, limits and closure.
  Purity or unchanged names cannot authorize changed documented results.
- **Regions:** retain the exact closed field/group or lazy unit, immutable
  extent, publication authority, sharing conditions, backing, cleanup and
  code/data load dependencies. An identity row grants no read, initialization
  or borrowing authority. Runtime initialization can throw, allocate, block
  or publish; preserve its ordinary point and effects unless independently
  discharged. Neither binding nor constant execution runs it or marks it
  complete.
- **Loaded state:** declaration identity is not an immortal address.
  Independently loaded closed types have distinct storage/state. Cache/reuse
  and view validity additionally depend on their actual load/publication
  identity and any storage generation. Preserve the
  [static lifetime rules](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage):
  failed units remain unpublished, recovery is not a reset of live state,
  and unload requires quiescence and discharge of loans, roots, pins,
  ownership and cleanup-code dependencies.
- **Replacement:** compare the retained expectation and any stronger facts
  used by folding, embedding or view formation, including exact owner,
  witness, value, target/ABI and publication/lifetime obligations. Replacing
  an Import with a direct provider DefId does not remove those obligations
  or update previously emitted code/data. This proposes no hot replacement
  or owner migration protocol.

## Structural checking, producer truth and rewriting

Before affected use, mandatory consumers validate exact selected schema and
capability support, section stride/extents, closed kind and zero
flags/reserved fields, required tables and DefId bounds, target-family
eligibility, complete owner references/substitution, declared typed
value/witness/publication relationships and required binding compatibility.
Missing required representations or unsupported forms refuse affected use;
recognized row shapes and producer assertions are not support.
Do not execute getters, equality, initializers or user code to discover a
binding or value.

Producers must publish truthful frozen values, immutable extents, effects,
publication and lifetime facts, including facts changed by a rewriter.
Under [reader trust](../specs/representation/reader-trust.md), ordinary
consumers need not semantically re-prove those facts against Bodies or
foreign behavior. An optional audit may examine actual writes, reachability,
value computations or publication proofs, but must state its scope.
A structurally present but false immutable-region assertion is producer
misconduct, not a mandatory whole-program consumer proof obligation;
execution relying on false metadata is not guaranteed.

A meaning-preserving rewriter must:

1. Decode each DefId by its selected target namespace; remap its row without
   losing its kind. Remap the bound witness, associated declaration/source,
   static and Import/Export graphs through their own schemas.
2. Remap owner TypeSpecRefs and all nested type/constant arguments, formal
   correspondences and retained target/value dependencies. Clone records
   whose meaning differs between using contexts.
3. Remap ImmutableData row references in PredicateReadRoot,
   ConstantExecutionDependency and LogicalInputView, then rebuild affected
   ranges, canonical ordering, duplicate checks, section indexes/extents
   and source mappings where applicable. Compare roots by semantic identity,
   not their new row numbers.
4. Preserve original expectations and stronger relied-on facts after
   internalizing imports, folding constants or removing now-unused rows.
   Never substitute matching octets inside Blob values or treat a raw row
   number as an untagged alternative.

Ordinary duplicate/reordered ImmutableData rows do not create new identities
or justify dropping dependencies. Reject duplicate semantic roots within
a footprint under its existing uniqueness rule; do not confuse that rule
with an unproposed global uniqueness constraint on ImmutableData.

## Alternatives

| Option | Assessment |
|---|---|
| Direct DefIds with expected-definition binding; promote the existing selected binding row | **Recommended.** Preserves 12 bytes and the shared definition model while retaining associated witness identity without a new value or region codec |
| Only InterfaceConstDef and StaticField, deferring all associated-constant identities | Coherent smaller implementation slice, but does not complete the witness-capable resolved-constant identity; an implementation may remain this bounded without claiming the broader form |
| Explicit table-specific union of direct DefId and Import token | Possible intentional amendment, not an existing interpretation. Requires a registered union and exact expected-kind rules; adds a second binding spelling while expected local definitions are still needed. Not recommended here |
| New ConstantDef/RegionDef carrier, or an enlarged ImmutableData with a witness selector | Could cover more declaration/region families, but adds ownership and codecs not required for the existing fixed constant and selected binding records. Revisit only for a demonstrated missing identity |
| Raw row interpreted from `kind`, guessed table or plausible bounds | Reject. Kind 1 has multiple target namespaces, numeric rows overlap, and section IDs are not DefId tags. An ambiguous row must never select a fallback decoder |

## Candidate conformance cases

These are proposed obligations for future complete-schema fixtures, not
claims that current tests or compiler outputs implement them. Type spellings
below describe metadata subjects, not standalone runnable source.

| Cases | Required result under the candidate |
|---|---|
| Nongeneric fixed InterfaceConstDef with a formed int32 value; owner zero | Resolve its declaration and exact value; no runtime storage, initialization or replay |
| Same constant declaration under `I<int32>` and `I<uint32>`; under two different valid integer constant arguments; under nominal wrapper domains with equal carriers | Require complete nonzero owners; distinguish constructions/domains even when the constant's result bits or layout agree |
| Two bindings of one associated declaration through `I<int32>` and `I<uint32>` for one nongeneric implementer; same interface for a second implementer | Distinct WitnessAssociatedConst identities with zero implementing-owner substitution; retain both interface and implementer identities even for equal values |
| Generic implementing owner, inherited requirement, checked qualified-self projection | Apply one exact implementing-owner substitution, validate declaring-instance membership and retain origin/projection dependencies; missing/ambiguous witness, wrong requirement/domain or an unbound AssociatedConstDef rejects |
| Kind 1 naming StaticField; kind 2 naming InterfaceConstDef; either kind naming a valid FieldDef, PropertyDef, GenericParam, node or Blob | Reject the forbidden target family even when its row exists and its value appears suitable |
| Missing generic owner, extra nongeneric owner, wrong constructor, wrong formal kind/arity, ref/ptr owner, erased argument qualification, unresolved existential | Reject the inconsistent/incomplete owner; equivalent normalized TypeSpecRef spellings remain equivalent |
| Valid field-anchored published immutable region and a permitted LogicalInputView subrange | Retain exact backing/extent, access, publication and load dependencies; no invented value bytes, offsets or lifetime extension |
| Immutable claim covering mutable elements reached through a fixed static handle; externally varying readonly data; region lacking a publication contract | Source producer cannot establish that pure/immutable claim; missing declared representation refuses consumer use. A false published fact is separately an audit/truth case, not mandatory Body re-proof |
| Two independently published extents behind one static declaration with no admitted selector | Refuse that ambiguous identity; do not infer an extent from address, view offset or packed row order |
| Static fields for two closed generic owners and two independent loads; lazy failure/recovery; unload with outstanding views | Keep separate state/publication dependencies; no accidental group completion, reuse across loads or premature backing/cleanup-code retirement |
| Zero/unknown kind, nonzero flags/reserved, zero DefId, row zero or out of bounds, missing target table, truncated/wrong-stride row | Reject before affected use using checked extents/arithmetic |
| First row and `0xFF_FFFF` row when the actual table admits them; an over-limit table; reserved/unknown high byte; section-shaped or raw row word; Import token `0x1C` | Accept only in-bounds assigned DefIds of the permitted kind; reject capacity overflow and every unassigned/wrong-namespace spelling without guessing |
| Expected fixed/static definition and exact owner bound to the selected module/key; alias keys for the same provider definition; same rows/values in another provider; standalone WitnessAssociatedConst Import/Export | Resolve aliases to the same selected identity while checking each retained expectation; reject wrong provider, owner, target kind, re-export, standalone binding import/export or incompatible contract |
| Replacement changes a promised constant bit pattern, nominal domain, witness binding, published extent, static trigger or backing lifetime | Do not claim compatibility for retained affected uses; unchanged values/names/purity flags cannot erase other changed obligations |
| Pack/reorder definitions, witnesses, imports, TypeSpecs, constant sources and ImmutableData rows; one row used by modules with distinct expected contracts | Remap every typed edge, clone context-sensitive rows, rebuild ranges/order/indexes and preserve original/stronger obligations; no raw byte replacement or cross-context deduplication |
| Scalar ConstantSource/Node/Blob row 1 beside ImmutableData row 1; scalar-only proposed `11/1`; predicate roots, execution dependencies and views sharing an identity | Keep namespaces and feature closure distinct. Scalar registration alone admits no ImmutableData use; richer admitted users preserve exact value, witness, publication and lifetime dependencies |

## Integration owners and remaining gates

These are proposed future edits, not changes made by this document:

| Owner | Required integration |
|---|---|
| [Logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Replace the unspecified target restriction with the closed sets, bound-definition meaning, direct-only rule and owner requirements above; preserve the distinct constant/static dependency owners |
| [Registry](../specs/representation/trrm-1.registry.json) and [registry prose](../specs/representation/registry.md#4-admission-status-and-namespace-boundaries) | Review and assign the three symbolic DefId kinds coherently; add only InterfaceConstDef/StaticField to Import/Export; register this field's exact per-kind sets. Do not obtain tags by truncating section IDs |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) and [reference checking](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) | Keep the 12-byte row and TypeSpecRef grammar; add target namespaces, using-context/owner checks, definition ownership and expected-binding rules |
| [Binding/replacement](../specs/representation/binding-and-replacement.md) | Add fixed-constant, selected binding and published-region obligations, exact owner correspondence and retained value/publication/load facts |
| [Admission](../specs/representation/requirements-and-admission.md), [format identity](../specs/representation/format-versioning.md) and conformance fixtures | Select one exact amended draft and complete richer capability closure; equal `(1,0)` bytes or a known section do not admit old/guessed encodings |

The target-set/direct-versus-Import decision is concrete; these remaining
decisions and implementation dependencies are **not** silently resolved:

- Numeric DefId assignments and exact-schema/catalog integration require
  coordinated registry review. No number, Feature revision or compatibility
  reader is allocated here.
- Constant/witness registration must complete associated-declaration reference
  encodings, selected-origin/expected-witness ownership, substitutions,
  projections, value sources and binding checks. Promoting the existing bound
  row is not completion of those codecs.
- StaticField/imported StaticDependency, group/lazy/access/authority and
  publication/extent/backing representations remain under their static
  owners. Select the precise immutable-region publication carrier before
  enabling kind 2; no currently assigned field flag supplies it.
- Effective access, source-unit identity and provider-local declaration
  ownership must be representable for newly importable/exportable constants
  and static fields; public-owner checks cannot stand in for member access.
- Resolve the richer Feature registration against the scalar proposal,
  including predicate, execution and view prerequisites. Do not silently
  widen its scalar-only `11/1`, or infer full support from a producer/reader
  recognizing a row.
- Implement and qualify producer, structural reader, binding/replacement,
  remapping and lowering paths against the complete selected representations.
  Namespace validation alone is not value admission, pure evaluation,
  immutable publication or execution support.

No source/runtime rule is weakened, and no new constant declaration syntax,
implicit marshalling, static initialization trigger or lifetime is introduced.
Additional constant or region target families require an explicit amendment,
not an implementer-selected extension of these closed sets.
