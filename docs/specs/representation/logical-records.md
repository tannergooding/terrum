# Logical source and runtime records

This chapter owns the semantic representation requirements for selected
source/runtime contracts. Their linked source/runtime chapters remain the
canonical owners of behavior. These requirements do not assign a table,
flag, Feature ID, opcode or physical codec; affected independent use remains
gated until its complete encoding and support are admitted.

[Physical metadata](../il-metadata.md) owns framing and assigned row layouts.
Its [definitions](../il-metadata.md#definitions-and-conventions), the
[registry](registry.md), [requirements and admission](requirements-and-admission.md)
and [binding contract](binding-and-replacement.md) retain their separate
authority. An inspectable shape, source name or private implementation cannot
stand in for an unassigned mandatory record.

These records define producer obligations and the meaning of valid
representations. Source compilation checks authored source; reconstructed-
metadata, JIT and AOT consumers perform structural safe-consumption checks
before affected use. Producers are responsible for semantic truth, and
consumers may rely on published facts without re-proving represented bodies
or obligations. Optional audits do not prove arbitrary external provider
behavior or authored unsafe promises; those remain explicit trust contracts.
Runtime checks required by an operation's own contract remain distinct from
metadata validation.

## Source-unit access and definition identity: encoding pending

The [source declaration contracts](../grammar-spec.md#31-source-file-structure-and-declarations)
define strict `file` access and explicit member-scoped `friend<...>` grants.
Their identities and permissions require mandatory semantic metadata, not
optional diagnostic SourceFile paths. The requirements below do not assign
new tables, flags, row layouts or a format revision.

File-local definitions preserve their source namespace/name and carry an
explicit module-owned semantic file-scope discriminator. For example:

| Definition reference | Module | File scope | Namespace | Name |
|---|---|---|---|---|
| TypeDef row 17 | M | Unit A | N | Foo |
| TypeDef row 18 | M | Unit B | N | Foo |

References select row 17 or 18, not an ambiguous `N.Foo` string. Name-based
resolution also accounts for the file-scope identity; equal text does not
coalesce nominal identities. Unique name mangling is not the semantic
identity scheme. Unit identities are compiler input identities, not path
or filename equality. The existing `DefId` representation already distinguishes
definition rows, but the current row layouts do not encode these semantic
scope/access records.

File-visible members retain their declaration's access scope. Partial
implementation bodies retain their actual source-unit origin rather than
inheriting the primary declaration's file permissions. Friend records bind
resolved same-module nominal type identities and exact/wildcard generic-slot
patterns; they do not grant by textual names, interface implementation,
extension receiver or transitive friendship. Lowering/inlining preserves
the body's declared access principal rather than deriving it from a synthetic
runtime type or the caller. Effective containing-type,
accessor and interface-conformance restrictions remain part of the contract.

Linking, packing and trimming must preserve/remap the definition, module,
semantic scope and grant references without merging them merely because
names or paths agree. Stripping debug locations cannot strip required access
semantics. Reflection or tooling that searches only by name must retain the
distinct matches or report ambiguity; a precise scope/definition reference
selects the intended type. Private-state reflection authorization remains
a separate API contract, not an identity-disambiguation mechanism.

The assigned `(1, 0)` visibility flags in
[physical metadata](../il-metadata.md#section-kinds-and-row-layouts) encode only private, internal
and public. Neither reserved visibility `0x03` nor optional SourceFile rows
are a file/friend encoding. A complete compatible required representation
and its reader/binding rules must be specified before such artifacts can
claim support; lowering the permissions to existing visibility or inferring
them from filenames does not establish a support claim.

## Nominal and qualified-self witnesses: assigned families, codec gates remain

The [source witness contract](../language/interface-witnesses.md) requires
records beyond the current bounded TypeDef, MethodDef, and Constraint rows.
A complete logical witness identifies its closed implementing type,
interface instance, self formal, inherited declaring-member identities,
selected targets/defaults, associated bindings, and published contracts.
One shared typed `InterfaceWitness` family is keyed by the exact complete
implementing `TypeSpec` and interface-instance `TypeSpec`, covering both
nominal and built-in primitive implementers. Nominal witnesses are published
by the implementing type's primary authority; primitive membership is
published only by privileged Core declarations. Primitive identities do not
gain fabricated nominal TypeDef or DefId rows.
Each associated type declaration has a first-class typed definition identity
bound through Import/Export; a witness selects that declaration's binding for
the exact complete interface instance. Names, ordinals alone, physical slots,
and matching signatures cannot replace these identities. The registry assigns
sections `0x0025..0x0036` for generic declarations, witnesses, construction,
constant sources and union records; DefId kind `0x21` is `UnionCaseDef`.
Kind `0x20` remains an unassigned `AssociatedTypeDef` candidate, and numeric
kinds for other first-class generic/static definitions remain unassigned.
Associated constants likewise have a separate first-class definition
identity and typed witness-binding record. Every complete implementing
witness supplies the binding; the declaration has no default value in this
profile. Retain the exact declaration, complete interface instance, constant
domain/value and dependencies. These records reuse the typed
constant-value/evaluation contract; they are neither runtime static fields
nor callable methods. Their numeric identity and row/reference schemas remain
open.

A retained qualified-self projection additionally identifies its original
witness, complete requested self/interface, rule version, and checked
argument/receiver/result adaptations. Its inherited views preserve their
substituted identities and selected mappings. It cannot silently retarget
ordinary arguments, drop unit positions, change an initialization destination,
upgrade result authority, or choose a new associated/default binding.
Dependencies, effects, transfers, roots, and compatibility requirements
remain part of the projected obligation.

An implementation claiming support for the projection preserves its
complete requirements, supported rules and contract compatibility. It
verifies the reconstructed witness, projection and selected body facts before
use; it does not reconstruct source binding from private implementation
bodies. A replacement preserves retained projected contracts, not only the
original unqualified witness. Linkers/JITs do not recreate source binding
from private implementation bodies.

The `InterfaceWitness`, `WitnessMember` and associated-binding section
identities are assigned in the registry, but `WitnessProjection`,
some DefId kinds and required reference/mapping codecs remain unassigned.
The dispatch ABI remains separate work; an artifact needing these semantics
must use a complete supported required representation, not optional data
that an unaware reader could ignore.

## Existential generic views

The source contract admits `G<_>` as an existential view of a nominal generic
type in type positions. Each value retains its own hidden complete type
arguments and the corresponding witness/evidence needed for operations valid
for every hidden argument admitted by the constructor's constraints. This
correlation is value-specific; it cannot be replaced with one artifact-wide
inferred type, `void`, a name lookup or a default witness. The Task view
additionally preserves affine ownership, payload settlement, failure,
dependencies and cleanup. Its selected common operations are defined by
[task responsibility](../language/task-responsibility.md#existential-task-view);
the general view does not grant operations whose signatures or contracts
depend on hidden arguments.

Physical storage is replaceable: a box, tagged representation, specialized
code or another admitted form is valid only if it preserves the per-value
correlation and all ownership/failure/publication obligations. A
first-class existential-value owner is keyed to the owning semantic value
occurrence, which may be a Body value, parameter/result, stored field or
formation/use site; Body value descriptors are not the sole carrier. Each
owner defines a typed evidence shape with one outer entry for each hidden
formal ordinal. Each outer entry's nested range specifies exactly one evidence
slot for each applicable interface-instance `Constraint`, keyed by that exact
constraint. At a formation where the hidden type is statically known, the value
evidence retains its concrete hidden `TypeSpec` and each slot references its
direct `InterfaceWitness` or closed `WitnessProjection`. At a
parameter/result/storage boundary, the owner defines the required evidence
shape, while each runtime value supplies its own concrete hidden `TypeSpec` and
witness references; a static slot record does not freeze one hidden type for
all values. A projection has exactly one mapping for every normalized
requirement in the requested interface closure, including unchanged/direct
mappings; missing or duplicate mappings are invalid. These facts are
per-value, not a type-global witness set or a runtime dictionary. Property and
associated-relation constraints retain their own typed facts and are not
converted into interface-witness entries. The owner key, evidence-row fields,
runtime carrier and reference encoding remain open. An implementation
claiming support verifies the representation and materializes it under the
published compatibility contract before use; verification does not prescribe
a physical runtime carrier.

TypeSpec form `0x0F` identifies an existential generic argument/view and
replaces the separate `TypeArg` child range; it is not a missing-argument
sentinel or a synonym for `void`. Constructed TypeSpec form `0x05` carries
one fixed reference per formal, in formal order: TypeSpec for a type formal,
ConstantSource for a constant formal. Visible arguments remain exact in
static identity, constant arguments retain their distinct semantics, and
each existential value retains its own hidden complete TypeSpecs and witness
evidence. The Body TypeMap's `0xF2` well-known code `0x1A` denotes this same
existential identity; it does not supply the per-value evidence carrier.
Runtime carrier representation and independently executable persistence
remain governed by the corresponding generic/witness capability closure.

## Generic domains and specialization branches: encoding pending

The [generic source contract](../language/types-and-normalization.md#generic-formal-domains-and-specialization)
gives an unmarked type formal the non-unique, Copy-capable, non-null domain.
`unique` and `unique?` distinguish unique-only from Copy-or-unique actuals;
`null?` independently admits nullable complete types. There is no explicit
`[Copy]` generic constraint. Constraint-only same-signature overloads are
valid only when their applicability domains are proven pairwise disjoint
under closed, exhaustive properties.

The selected `if (T is unique)` form tests a type property, not a value's
runtime type or general reflection. Both arms are checked against the generic
definition's declared domain and their refinements. A true edge retains its
proof/witness; a false edge establishes a complement only when the tested
property is exhaustive for that formal, including the Copy arm for
`T : unique?`. GenericCall selects exact checked methods/witnesses; a broad
formal uses the explicit branch when it must select among domain-specific
overloads. The property may be resolved during Terrum compilation or
closed-instance JIT/AOT specialization, or evaluated in shared/canonicalized
generic code using compact implementation-provided type metadata. In value
context it produces an ordinary Boolean with supported flow correlation.
Shared execution is not general reflection.

The assigned `GenericParam` row is 32 bytes:
`u32 ownerDefId, u32 ordinal, str name, u32 constraintFirst,
u32 constraintCount, u8 formalKind, u8 ownershipDomain,
u8 nullabilityDomain, u8 reserved=0, u32 constantTypeSpec?,
u32 defaultArgumentRef?`. Formal kinds are `1=type` and `2=constant`.
Type formals use ownership-domain tags `1=default non-unique Copy-capable`,
`2=unique-only`, `3=default plus unique`, and nullability tags
`1=non-null`, `2=nullable allowed`; their constant type reference is zero.
Constant formals use zero domain tags, a nonzero value-domain TypeSpec, and
no type Constraint rows. A nonzero default reference is a TypeSpec for a type
formal or a `ConstantSource` for a constant formal; zero means no default.
Reserved bytes and inapplicable fields are zero.

The assigned 12-byte `Constraint` row is
`u32 ownerGenericParam, u16 kind, u16 flags=0, u32 payload`. Its closed kinds
are `1=interface instance`, `2=intrinsic property`, and
`3=associated relation`; each selects one nonzero, kind-correct payload.
Intrinsic property codes are `1=Unique`, `2=Write`, and `3=Blittable`;
unknown and zero codes do not satisfy constraints. `Copy` remains the default
formal domain, `Sized` is not a property, and there is no `PropertyWitness`.
`Unique` and `Write` derive from canonical closed-type facts; `Blittable`
derives from the selected target's published representation and `Layout`.
The `write` property tests immediate access on the normalized complete type
argument. Pinning is a backing dependency established by explicit reborrow,
not a generic property or TypeSpec qualifier. The former pin TypeSpec bit is
retired/reserved; no GC encoding is assigned here.

The structural tuple TypeSpec form is approved without a form ID: its
variable payload is an ordered sequence of `u32 elementTypeSpec` references,
with arity at least two and count derived from extent. Labels are source-view
only; tuple frozen children use zero-based ordinal slots. It introduces no
nominal `ValueTuple` identity or implied ABI. The assigned 8-byte
`ConstantSource` row has `u32 kind, u32 payloadRef`, with tags `1=frozen
typed value`, `2=required target evaluation` and `3=completed
target-dependent value`; they reference a `FrozenValueNode`,
`ConstantExecution` or `TargetValueBinding`, respectively. The selected
record supplies the result TypeSpec, which the owning declaration validates
after exact substitution. Zero, mismatched reference kinds and unknown tags
are invalid.

An exchange format preserves the exact formal/property identity,
domain modifiers, branch edges, applicable proof/witness evidence and
substitution. An implementation claiming support for the form verifies these
identities, body facts and compatibility relations before use. No consumer may infer a complementary guarantee from
an open constraint or one successful instance when it performs its own
analysis. The conformance floor names required mixed type/constant arguments
and nested substitution, associated constants, positive and closed-negative
property branches including saved-Boolean propagation, qualified-self
projection, and per-value `Task<_>` evidence. Implementations may diagnose
excessive complexity beyond that floor; no portable numeric charge units or
fixed resource budget is selected. The `DE` TypeOf/C2 E/U flow supports exact
TypeIdentity comparison and the existing successful-edge refinement; generic
branch proof/witness/recovery codecs, captured evidence acquisition, other
branch operands, proof references, form IDs and typed-reference encodings
remain open. Sections `0x0025..0x004B`, `Contract` clauses
`0x000A..0x000B`, Feature IDs 9-13 and DefId kind `0x21` are assigned by the
registry; Feature 13 is registered, while incomplete registrations remain
unsupported. Generic candidate Features 6-8 and all `GenericCall`
opcode/display assignments remain outside this allocation. A `GenericCall`
requirement call retains a typed reference to the exact open formal
`Constraint`, closed `InterfaceWitness`, or closed `WitnessProjection`;
direct calls carry neither. Its target-role tags are `1=direct` and
`2=interface requirement`; evidence-kind tags are `0=none`, `1=Constraint`,
`2=InterfaceWitness`, and `3=WitnessProjection`. The evidence kind and
reference are both zero only for a direct call. `Constraint` is the open
formal requirement and has no witness reference; closed evidence lives in
the applicable witness record.
After declaring-owner substitution, selected generic method formals map by
ordinal with matching arity; names do not affect identity, and reordered or
otherwise adapted arguments reject without a separately admitted mapping
carrier. Preserve each formal's constraints, defaults and contracts. One
semantic generic-call operation uses a direct-versus-interface-requirement
target role; there are not separate direct-generic and requirement-generic
operations. Its display name, opcode assignment and byte grammar remain
unassigned.
Use one ordered tagged `Constraint` family/range for interface-instance,
type-property and associated-relation requirements. Its selected 12-byte row
shape is `u32 ownerGenericParam, u16 kind, u16 flags=0, u32 payload`. Each kind
selects one explicit typed payload or reference; related facts may have their
own typed records, but per-kind parallel requirement ranges are not used.
This replaces the optional witness-reference payload direction. Exact typed
payload encodings and registry assignments remain under review. User-defined
behavior uses interface-instance constraints and witnesses, not
library-defined property identities. A property variant is not encoded as
`targetTypeSpec=0`, and its code does not replace evidence for the property.

## Static initialization and load scope: encoding pending

The [source static-storage contract](../grammar-spec.md#static-initialization-and-load-scoped-storage)
requires semantic records beyond the current `(1, 0)` design. Existing
FieldDef, MethodDef and PropertyDef flags do not encode static storage,
initialization units, constructor trigger policy or lazy state. No section,
DefId kind, opcode number or format revision is allocated here. The approved
candidate row shapes and closed tags below do not themselves assign those
physical identities.
A static field has a distinct logical definition identity from an instance
`FieldDef`; it is not a member of instance layout. Its physical table or
section mapping remains open; its approved candidate row shape is specified
below. The static-field definition is a kind in the
shared `DefId` namespace, allowing Import/Export to bind it as a typed
provider definition; its numeric kind assignment remains open.

The approved logical model uses distinct `StaticField`, `EagerGroup`,
`EagerInitializer`, `LazyUnit`, `StaticAccess`, `InitializationRole` and
`InitializationDemand` records. Eager and lazy initializer bodies are private,
unspeakable generated MethodDefs with a required role, not ordinary
source-callable or imported methods. A `StaticPlace` Body operation forms an
ordinary place and carries a typed access reference to the exact static field
and group. Static method invocation reuses ordinary `Call`; its selected
MethodDef carries the demand contract that determines required group
completion. Each imported static-field definition has one separate typed
`StaticDependency` record, shared across its uses. It binds the exact
provider-owned field/group and preserves the provider trigger/effect,
compatibility and remapping requirements. Provider selection reuses the
shared Import/Export identity path, with static fields as a `DefId` kind;
`StaticDependency` supplements rather than replaces that provider binding.
Use-specific access kind and demand/phase authority remain on `StaticAccess`.
An ordinary dependency or call record does not replace either contract.
Static-initialization attributes resolve through exact registered Core
definition identities. `StaticInitializer` uses its registered closed trigger
enum and cases; `LazyInitialization` has its exact no-argument identity.
Names or enum underlying values alone do not select policy. Their catalog and
metadata identifiers remain unassigned.
`InitializationRole` and `InitializationDemand` are MethodDef-owned clause
kinds in the existing `Contract` ranges, not separate method-record tables.
Use one `InitializationRole` clause kind with a closed role tag; each method
has at most one role, bound to one exact group, and a role-bearing method must
carry it. Its approved 20-byte candidate row is the 8-byte Contract header
followed by `u8 roleTag, u8 flags=0, u16 reserved=0, u32 eagerGroupRow,
u32 ownerTypeSpec`. Tags are `1=eager initializer`, `2=lazy initializer`,
`3=authored static constructor`, `4=eager-phase helper`, and
`5=lazy-phase helper`; zero and unknown tags are invalid. Generated eager
and lazy initializer methods require this role. A method may have multiple
`InitializationDemand` clauses, one per exact closed eager group, with
duplicate group demands rejected. Its approved 16-byte candidate row is the
8-byte Contract header followed by `u32 eagerGroupRow, u32 ownerTypeSpec`.
Missing demand means no published eager-group completion requirement.
Clause-kind IDs and typed-reference encodings remain unassigned.
Unsafe recovery of one failed group or lazy field uses one registered
runtime-owned callable through ordinary `Call`, not a dedicated Body
operation. Its target is a closed tagged exact-unit reference distinguishing
an eager group from a lazy field and resolving to the exact loaded-unit
identity. Runtime validates the failed state and recovery preconditions. The
callable identity/signature and physical tagged-reference fields and encoding
remain open.

The approved candidate shapes also include a 20-byte `StaticField` row
(`u32 ownerTypeDef, str name, u32 typeSpec, u32 flags, u32 eagerGroupRef`),
where flags `0=not writable after initialization` and `1=writable after
initialization`; all other values are invalid. `StaticAccess` is a 12-byte
candidate row (`u8 targetKind, u8 authorityKind, u16 reserved=0, u32 targetRow,
u32 ownerTypeSpec`) with target tags `1=local StaticField`,
`2=imported StaticDependency` and authority tags `1=ordinary completion
demand`, `2=validated initialization-phase authority`. `StaticDependency` has one identity per exact imported `StaticField` and
complete owner substitution, shared across its uses. It carries typed
references to the shared Import/provider binding, exact provider field and
group, applicable substitution, and trigger/effect contract identities. It
does not duplicate the module/export-key resolver or copy contract snapshots.
Its physical fields, reference encodings and section assignment remain for
the coordinated static-initialization schema review. Section/DefId/clause IDs,
remaining static-row schemas, opcode numbers and runtime handles remain open.

The runtime owns initialization lifecycle coordination and enforcement;
artifacts supply typed unit identities and initializer bodies. The runtime
may keep state in its own table or artifact-provided cells. This does not
select a physical state layout, handle representation or operation spelling;
either state-storage mechanism is an implementation choice, not an artifact
contract requirement.

A future encoding must identify owning closed types and load scopes, ordinary
group members and separately lazy fields, ordered initializer operations, the
sole authored constructor policy/contract and its implementation. It preserves
compiler-input/source execution order rather than reconstructing it from
packed row numbers, filename sorting or primary-part priority. Linking and
generic substitution preserve exact owning identities and dependencies.
Every source-free runtime static access must also carry an explicit typed
obligation identifying its owning group and access kind. The obligation
distinguishes an ordinary completion demand from validated, group-scoped
initialization-phase authority and exposes the applicable trigger effect.
Consumers must not infer the obligation from names, row order or ordinary
member effects.

Published records also carry explicit sharing/access and initialization-phase
authority, trigger effects, completion guarantees, failure/recovery policy,
ownership, roots, cleanup and unload dependencies. Runtime waiting/publication
state is distinct from those producer contracts. Unsafe retry or suppression
is not a byte flag granting safe initialized-state facts. Retry is an explicit
unsafe request carrying one closed tagged exact-unit reference for a failed
group or lazy field; runtime validation of identity, cleanup, exclusive
authority and dependencies is required, and ordinary access never retries
implicitly. An effect-bounded consumer cannot ignore a producer's
initialization obligations merely because the requested member is a scalar
field or an otherwise simple static method.
The [failed-unit contract](../runtime/standard-failures.md#5-static-and-lazy-initialization-failure)
retains the original escaping event in the initializing flow, not in shared
unit storage. Waiting/later demands create fresh
`InitializationFailedException` events; cyclic demand uses
`InitializationCycleException`. State/operation records must preserve that
distinction, actual acquired-state cleanup and load identity without an
implicit shared captured-event cache.

An implementation cannot claim binding/execution support for unknown
required initialization, sharing or lifetime semantics, or substitute the
host's static-constructor timing and unload behavior. This does not prescribe
how unsupported declarations are detected or reported. Concrete schemas and
runtime state/ABI remain separate specification work.

## Local failure eligibility and catch inputs: encoding pending

[Local failure contracts](../language/local-failures.md) select class-only
managed exception objects implementing `IException<TSelf>`. The object is the
propagation event; catch binds that object or its common `IException<_>`
existential view, not a copied payload or a `ref<E>` into payload storage.
`InnerException` is the authored cause, distinct from the runtime-owned
`ParentException` chain. Same-named attributes, source/debug names, equal
layout and guessed inheritance do not establish exception eligibility or
witness identity.

Approved source behavior uses lexical ordered handlers and filters, with
ordinary Body regions for protected code, handlers, filters and `finally`.
The active instruction address and operation/call failure semantics determine
dispatch; there is no event-ID table or per-site exceptional-state snapshot.
Normal catch fallthrough propagates the same event. Explicit non-failure exits
swallow it; a distinct failure from a handler, filter or crossed `finally`
becomes primary and retains the prior object through `ParentException`.
Handling is unsafe, general state after a catch is indeterminate, and neither
implicit owner cleanup nor rollback is promised.

The logical region/handler and event-object contracts are approved, but their
Body directory layout, capability registrations, and runtime storage for the
parent link remain physical integration gates. Existing runtimePolicy and
interface/constraint rows alone do not enable source-free local failure use.

## Runtime requirement construction and defaults: encoding pending

[Requirement failures](../language/requirement-failures.md) require resolved
primary RequireException/type recipes, exact Core fallback or explicit site
selection and the original semantic check owner/phase. Their constructor
binding retains the exact exception class, selected type recipe, zero-supplied
ordinary constructor/optional-default/empty-params binding, effects and
successful construction versus actual construction failure.

False-path construction and fresh propagation cannot be inferred from a
source name, current body, default scalar bits or an unqualified runtimePolicy.
Preserve predicate once-only execution, actual output states, supporting-scope
owners, partial cleanup, the exact exception object and ParentException
propagation, dependency responsibility and async/producer failure channels
through lowering, binding and replacement. Source owner survives relocation
even when a check executes in generated code.

Complete schema/default/constructor/value/operation/phase/cleanup records
remain unassigned. This logical Core/source selection allocates no Feature ID,
Attr tag, opcode, TypeSpec or exception-region/constructor ABI. Existing
generic/constraint rows and Effects alone do not enable source-free execution.

## Captured exception ownership and lexical context: encoding pending

[Captured exceptions](../language/captured-exceptions.md) require exact Core
type/member/module registration and zero-parameter signature, lexical source
callable/handler activation, owning/transferred state and the result's complete
event/payload/provenance/cause closure. The implicit source is semantic
handler context, not a supplied parameter, thread-global object or debug name.

Retain creation preparation/publication and pre/post-transfer failure states,
catch-input extents, source/destination owner vacancy, same-object propagation,
ParentException replacement, phase, effects/resources,
nonthrowing/nonsuspending retirement and all source/root/runtime/domain/code
dependencies.
Independent use cannot infer those programs from equal owner bits, a private
Capture body, ordinary Exception eligibility or inspectable table rows.
Missing required support declines before affected transfer or execution,
without broadening selective readers into whole-Body scanners.

Complete registration/context/owner/value/operation/handler/cause/phase/
cleanup records remain unassigned. This logical selection allocates no
Feature ID, Attr tag, TypeSpec, intrinsic wire ID, opcode, exception region
or native export/ABI. runtimePolicy or a reserved exception band alone does
not authorize source-free execution while these required carriers are absent.

## Scalar failure catalog: encoding pending

[Core scalar failures](../language/scalar-failures.md) require exact selected
exception-class/factory identities, complete numeric/count domain and source
failure site, target width, no-result state, policy/effects/phase, event
publication and complete propagation/cause/cleanup dependencies.
The exception object is the event, not a detached payload, void value or
integer failure code. A native helper address,
debug source span, scalar opcode or raw runtimePolicy cannot supply those
records or silently remap shift-count failure to conversion overflow.

Complete registration/value/operation-state/handler/phase/transport codecs
remain unassigned. This logical catalog changes no Feature ID, Attr tag,
TypeSpec, intrinsic wire ID, opcode or exception-region layout. Until those
carriers are registered, affected inputs reject; this is not a separate
reduced TRRM 1.0 GA conformance floor.

## Numeric selection and bounds failures: encoding pending

[Numeric selection](../language/numeric-selection.md) requires exact owning
Min/Max/Clamp identities, same-type value formals/results and target width;
complete NaN/zero/cohort selection observations; Clamp's published min <= max
predicate, explicit Core InvalidBoundsException class and ordinary
zero-supplied constructor binding;
semantic precondition origin, argument/result state, effects/phases and the
full actual value/program/helper/runtime/propagation closure.

Machine comparisons/selects, equal exception-object layouts, a helper address
or raw runtimePolicy do not supply those records. A comparison rewrite must
preserve unordered-bound rejection and in-range signed-zero/cohort identity.
Registration, predicate/value/program/member/event/handler/cause codecs
remain unassigned. No Feature ID, Attr tag, TypeSpec, intrinsic wire ID,
opcode or exception region is allocated here.

## Floating representation and numeric witnesses: encoding pending

[Floating contracts](../language/floating-environment.md) retain the exact
format/representation and operation: sign-only Abs/negation versus
NaN-processing arithmetic/conversion/selection, quiet-NaN runtime latitude,
the canonical constant recipe and actual target control obligations.
Copies/identity retain their stronger bits. No language ambient status record
is required; that omission does not erase foreign ABI duties or other effects.
Missing operation/program/value/target support declines affected use.

[Numeric capability groups](../language/numeric-capabilities.md) require exact
Core interface/self/formal/member identities, the default Copy-capable domain,
complete primitive instances, inherited member deduplication, selected
implementations and published predicate/effect/phase/constant dependencies.
The INumericSelection binding supplies Min/Max/Clamp together; its inherited
comparison evidence retains the exact Core bounds construction.
An optional composite is not a universal number marker, and names/primitive
codes cannot substitute for these witnesses.

The existing bounded FloatNegate/FloatBinary/FloatConvert families preserve
their selected result rules, but do not encode Abs/selection member
registration, generic witness dispatch or constant programs.
No new Feature ID, intrinsic, Attr tag, opcode, witness-table layout or general
numeric value ABI is allocated here.

## Standard runtime categories: encoding pending

[Standard categories](../runtime/standard-failures.md) require exact Core
PostconditionException, IndexOutOfRangeException, OutOfMemoryException,
InitializationCycleException and InitializationFailedException declarations,
their self-typed common creation contracts and exact class identities.
Retain each failed semantic operation/domain, policy, state/output facts,
source owner/phase, actual factory/transport and complete event/cause/
cleanup/load/runtime dependencies.
Recoverable allocation needs actual recoverable-publication support;
a runtimePolicy flag or common exception class cannot establish it.

Static/lazy records distinguish running/complete/failed state, cycle demand,
original initialization escape and fresh failed-unit-access exception creation.
They neither copy an affine original event nor infer safe retry/suppression.
False ensure remains distinct from actual predicate failure and static proof.
Invalid original indices are checked before narrowing/address formation.
No Feature ID, Attr tag, exception region, opcode, payload ABI or state/handler
codec is assigned by these logical selections.

## Pattern evidence and handler selection: encoding pending

The [source pattern and catch contracts](../grammar-spec.md#33-statements-and-local-declarations)
use uniform concrete/generic type matching and ordered lexical exception
dispatch. `IException<_>` is the common existential view of an already
constructed exception object; a catch binding preserves that same object
identity. The general existential representation and persistence codec remain
unassigned. Their first-class owner must define the per-hidden-formal evidence
shape and nested constraint-keyed witness slots. Statically known formations
may retain concrete hidden-type and witness references; a boundary owner does
not fix one type for all values, which supply their own concrete evidence.
Owner fields and codecs remain open. The Task-specific operations on `Task<_>`
are described in
[existential generic views](#existential-generic-views);
the shared per-value evidence is not a Task-specific carrier.

Runtime-only captured type arguments preserve complete type identity,
qualification and declared constraints/witnesses. Their names have the source
pattern's supporting scope, while use requires retained checked evidence under the
[structural-flow rules](../language/initialization-and-flow.md#binding-pattern-declarations-and-scopes).
Execution requires compatible retained specialization, shared generic code or
another explicitly supported path, with its ordinary access, ownership,
backing, root and load-lifetime contracts. A witness is not an implicit box,
permission for arbitrary runtime instantiation or authority to generate code.
Pattern reference/pointer matching preserves existing forms rather than
manufacturing indirection or deriving facts from a dependency's private
representation. Catch bindings are initialized managed-object handles or
common existential views, not borrowed payloads or implicit owners.

Body-local exception directories reference the protected lexical region,
ordered handler rows and optional `finally` region. Handler rows select a
concrete catch type or catch-all and may reference filter and handler regions.
These code regions are ordinary Body regions/blocks, not normal-CFG
successors: dispatch-only entries have no ordinary incoming edge, while
structured exits route through each crossed `finally`. Filters are evaluated
once in source order after a type match. A filter failure becomes primary and
retains the pending object through `ParentException`; normal catch fallthrough
propagates the same object, and explicit non-failure exits swallow it.
Try/catch/finally and continuation after a catch require `unsafe`; general
state after a catch is indeterminate. No implicit owner cleanup is encoded.

Exact Body directory widths, opcodes/exit records, capability registrations
and runtime storage for `ParentException` remain physical integration work.
An implementation cannot claim affected execution support until the complete
region, dispatch, cleanup-routing and runtime prerequisites are assigned; an
unassigned shape does not acquire host exception timing or successor-edge
semantics. A consumer rejects affected use when these required contracts are
unavailable.

## Foreign failure bridge: encoding pending

[Paired boundary containment](../runtime/foreign-failure-bridge.md) and its
[Windows x64 wire](../targets/windows-x64-failure-bridge.md) require retained
origin/receiving definitions, exact special entry/callback signatures,
Boolean result/failure output correlation, supported runtime domains and
generations, eligible local failure/mapping identities, affine transfers,
capture/provenance/causes, transitive retirement and actual root/allocator/code
dependencies. Raw entries and domain release are nonthrowing; receiving
proxies retain their actual source failure effect.

Domain header/state pointers are native machine data, not serialized type,
Module, definition or runtime identities. Packing/replacement must preserve
the logical relationships rather than persisting addresses, flattening
causes or reassigning an intermediate image as failure origin.
The bridge's packet, mappings, local failure state and callbacks need complete
physical value/operation/contract codecs before source-free independent use.

No Feature ID, Attr schema, reserved bit, TypeSpec extension, opcode or
exception region is assigned by the logical bridge revision. Existing
OutputState framing does not by itself supply packet types, complete
failure/callback operations or ownership. `runtimePolicy=1`, target `1`
and a matching export name are not bridge admission.

## Literal construction and registrations: encoding pending

The [literal construction contract](../language/literal-construction.md) defines
the logical `literal-construction/1` records, not new metadata table kinds,
contract clauses, feature bits, operation tags or a format revision.
An independently consumed record preserves its target-defining module,
normalized target pattern, exact resolved kind/schema identities, parser
identity and substituted signature, access, Boolean initialization relation,
input recipe, constant-execution prerequisites and frozen-value admission.
The four kinds are distinct from parser names, token radix and expression
operators.

Import deduplication preserves record identity. Distinct claims for one
target/kind key conflict even when they name the same callable; aliases,
packing and trimming cannot hide a conflict or transfer registration authority
to another module. Generic substitution follows the owning target's declared
formals and constraints, without inverse inference or conditional priority.
Linking preserves/remaps exact definition and dependency references, not
textual parser names.

Numeric recipes preserve the original signed or signless ASCII token.
Text/character recipes preserve the selected decoding, raw transformations,
scalar validation and actual UTF-16 view contract. The input view and backing
cannot escape as an unsupported frozen result. Target validity, direct
floating rounding, decimal cohorts, value codecs and deterministic execution
costs remain identified requirements, not host-parser conventions.

An implementation claiming support for the record preserves its rule/schema
versions, authority, signature, input and value recipes, and required
constant body or admitted intrinsic. `[ConstEval]` alone does not supply an
encodable body, target codec or resource recipe. Unsupported required
semantics do not establish a support claim; optional attribute data or a
matching runtime parser is not a substitute.
The physical records, retained constant programs and value codecs remain
separate specification work. No existing reserved slot is assigned by this
logical source approval.

## Builder type recipes and construction handlers: encoding pending

The [construction registration contract](../language/construction-handlers.md)
defines logical `builder-type-recipes/1`, `text-target-parameters/1` and
`eager-construction/1`, not a new metadata type, table, Attr argument tag,
clause, feature bit or opcode.
The selected consuming completion additionally retains `builder-completion/1`
and the exact complete Core IBuilder witness/member and result relationship.
Source acquisition retains `builder-acquisition/1`, the exact Core
IBuilderFactory self witness/static member and its init-builder relation,
including no ordinary runtime input positions. No allocator, capacity hint
or captured caller-local environment is inferred from the registration.
The record preserves its resolved TextBuilder/CollectionBuilder schema,
target-defining module, exact target definition/pattern, kind and primary
authorship. Distinct claims conflict; repeated imports deduplicate exact
record identities rather than matching builder names.

Retain the bound builder type tree and its selected definition references,
the target's type/constant formal ordinals and domains, constructed arguments,
defaults, constraints, normalization rules and associated/witness dependencies.
This is forward substitution of ordinary complete types, not serialization
of `typeof(ListBuilder<>)`, a runtime reflection object or an unapplied
generic-constructor value. Source names and import order cannot rebind a
retained recipe. Linking preserves/remaps exact identities and checks the
actual construction and load dependencies.

The complete construction record must additionally supply exact operation/
result/capture roles, access, effects, failure/refusal, ownership, backing,
initialization/publication, cleanup and supported fragment/spread contracts.
Operations retain checked nominal witness/member identities, not a role
inferred from names or a new per-method role-attribute catalog.
Completion records retain the by-value builder transfer, fixed true/live
and false/vacant init-output relation, exceptional vacancy/partial cleanup,
and actual ownership/backing transferred to the result. They do not encode
a source reset/reuse/abandonment method or infer one from an instance TryBuild.
Factory records retain true/live, false/vacant and exceptional-vacancy output,
partial/residual cleanup and selected initialization/provider dependencies.
Parameterless factory invocation does not imply purity or constant eligibility.
`construction-operations/1` retains exact Core IFormattable/ITextWriter/
ICollectionBuilder instances and member identities, effective typed argument
roles, int32 Unicode-scalar alignment, TextSpan format data and the exact
normalized contextual completion result. Type/literal data is not a method
name or an inference recipe for another result representation.
Retain ordered literal/value graph roles, once-only formatting and permitted
staging effects/resources/cleanup. Spreads retain their independently admitted
sequence/advance/item/cleanup identities, item transfer modes and dependency
extent; no preflight count pass or body reconstruction is supplied.
A builder type reference alone does not supply these fields or authorize
ordinary private member access. Required constant use also needs admitted
program/intrinsic, target value-codec and exact resource recipes.

Where a retained record/program represents construction shorthand, preserve
its source-order typed captures, after-capture acquisition and ordered drain.
Formatting and spread enumeration remain drain operations; refusal cannot
suppress earlier capture effects or roll back transfers/output.
Retain ordinary require failure for Boolean refusal in plain value
construction and the selected operations' own exceptional/output contracts.
Explicit Try/writer consumers keep their separately declared outcomes; no
registration-specific plain-refusal failure role is supplied.
TextTarget records retain the resolved no-argument parameter schema,
declaring callable/requirement, exact formal ordinal and eligible input mode.
Generated content retains its originating module and normalized complete
capture types and acquisition modes and fragment graph, including selected
formatter witnesses and required writer/member identities. Capture mode
distinguishes copy from ownership transfer only; complete `TypeSpec` identity
retains value/reference/pointer structure and access placement, while ordinary
dependency/lifetime records retain backing and provenance. Equal closed shapes
within that module share one logical generated type; captured values and
literal text remain instance data. Linking preserves this key and does not
merge types across modules or by layout/output. Actual
copy/transfer/backing/retention obligations remain independent of shared type
identity. Independent consumers do not reconstruct these from a parameter
name, source coordinate or private body.
The approved candidate rows are 12-byte `BuilderRegistration`
(`u32 targetTypeDef, u32 schemaTypeSpec, u32 recipeTypeSpec`) and 12-byte
`TextTarget` (`u32 ownerMethodDef, u32 schemaTypeSpec, u32 parameterOrdinal`).
Generated content uses a 20-byte `TextContentShape`
(`u32 generatedTypeDef, u32 captureFirst, u32 captureCount,
u32 fragmentFirst, u32 fragmentCount`), a 16-byte `TextCaptureShape`
(`u32 shapeRow, u32 ordinal, u32 typeSpec, u8 mode, u8 flags=0,
u16 reserved=0`; modes `1=copy`, `2=ownership transfer`), and a 40-byte
`TextFragmentShape`
(`u32 shapeRow, u32 ordinal, u8 kind, u8 flags=0, u16 reserved=0,
u32 valueCapture?, u32 alignmentCapture?, u32 formatCapture?,
u32 formatterWitness?, u32 formatterConstraint?, u32 formatterMethodDef?,
u32 writerRequirementMethodDef`). Fragment kinds are `0=literal segment`
and `1=formatted value`; literal segments have no capture/formatter
references and retain the selected `TryWrite`, while formatted segments
retain the three one-based captures and exact formatter/writer requirements.
Keep literal nodes, including empty segments, in the shape.

The complete shape key includes the originating module, ordered complete
capture TypeSpecs and acquisition modes, ordered fragment-kind structure, and
exact formatter/writer requirements. It excludes literal bytes,
alignment/format values, parameter ordinal, consumer instance, source
coordinates and generated names. Equal keys share one logical generated type
only within the originating module; no physical interning table or shared
captured value is implied.
The parameter annotation is not itself a value codec or a new type carrier.
Lowering to ordinary explicit operations must preserve the same order and
dependencies. This is not a deferred source program, resumable enumerator,
new ITextContent identity or construction-policy carrier.

An implementation cannot claim support for unsupported required
recipe/protocol semantics. These approved candidate row shapes and tag values
do not allocate sections or typed-reference encodings. Exact registry
assignments, physical operation/capture/lending representations and generated
producer representations remain separately gated; optional attribute data,
an existing TypeSpec name or a held slot cannot silently carry mandatory
behavior.

## Value-iteration contracts: encoding pending

The [value-iteration contract](../language/iteration.md) selects logical
`value-iteration/1`, not a new table, feature bit, attribute/value/TypeSpec
form, operation ID or opcode. Retain exact Core ICursor/ISequence and inherited
member/witness identities, normalized item/cursor/source terms and one selected
factory or direct-cursor source relationship.
The factory retains readonly acquisition and its actual result/backing
contract; direct adoption retains ordinary copy/transfer and existing traversal
state, not an inferred restart or second GetEnumerator call.

TryMoveNext records its mutable receiver, exact init output, true/live,
false/vacant and checked exceptional publication/partial-cleanup relation.
Close records required valid-use state, effects/failure and traversal/residual
cleanup obligations, including never-advanced and failed-advance state.
These do not imply purity, no-throw, NoAlias, Copy, element lending or async
completion. Published source/cursor/item roots, backing, guards, leases,
loans and refinements remain exact; copying a descriptor does not erase them.

Retained shorthand preserves once-only source evaluation/acquisition, body/
item retirement before later advance or close and one required close on exit.
For eager spreads, retain factory acquisition only at reached drain and
direct-cursor adoption/close responsibility at capture. An unreached captured
direct cursor closes on abandonment without advancement; an unreached
factory is not acquired. Preserve item transfer to acceptance, stop/failure
order, residual cleanup and ordinary failure chaining.

An implementation claiming support for these operations preserves the
supported rules, complete relationships, availability, access, output and
backing/cleanup contracts. The producer is responsible for published
semantic truth; consumers validate selected record shapes, ranges, references
and support needed for safe use, without semantic re-proof (see
[consumer verification](reader-trust.md)). A declaration name or artifact
flag supplies no missing obligation. Missing, malformed, ambiguous or incompatible
requirements reject affected use and do not authorize Current discovery, a
default item, hidden adapter or fallback.
Linking/remapping keeps these semantic identities and schedules independently
of row/name changes. Replacement cannot weaken output, source ownership,
close timing or dependency/refinement promises used by retained callers.

Physical witness/state/output/capture/program records and generated producer
frame/phase representations remain gated. Optional
Attr data or held slots cannot carry these mandatory semantics.

## Asynchronous value iteration: encoding pending

The [async-value-iteration contract](../language/async-iteration.md) selects
logical `async-value-iteration/1`, not a table, Task/frame layout, feature bit,
attribute or opcode. Retain exact Core IAsyncCursor/IAsyncSequence requirements,
complete item/cursor/source relationships, selected nominal witnesses and
synchronous readonly GetAsyncEnumerator or direct copy/transfer acquisition.
No Task<TCursor> factory or implicit source-preparation await is inserted.

TryMoveNextAsync retains its exact init formal, operation/destination identity
and version, completion-Boolean predicate, true/live false/vacant and checked
failure/vacant relation. Record invocation failure cleanup and the distinction
between invocation returning a task, physical completion, required cleanup
and checked caller observation. Pending output is reserved against state/value
inspection, competing initialization, retirement/reuse and overlapping cursor
advance/close through its actual aliases. Task shape, status, mutable storage
or a same-named attribute cannot supply this relationship.

Preserve actual storage/backing, source/cursor/output roots, loans, guards,
leases, affinity and responsible completion/cleanup ownership. Completion
observation maps its exact operation/output version to a saved Boolean without
duplicating output ownership or reviving old versions after storage reuse.
Forwarding either maps the original attachment/formals or records checked
outer/inner init delegation, with outer publication after its own checks/cleanup.
An independent caller cannot recover missing phase/capture semantics from
a private forwarding body.

Retained loop order includes synchronous acquisition once, one outstanding
advance, observed publication before body, item retirement before later advance
or awaited close, and header cleanup only after required completion.
CloseAsync records cleanup-safe never-advanced/failed-advance state, actual
effects/failure and traversal/residual obligations; its Task<void> completion
does not permit generated destructors to suspend.
Cancellation request does not end reservation, publish exhaustion or erase
failure/cleanup responsibility. Preserve ordinary causal failure chaining.

An implementation claiming support preserves the completion/output/
reservation and source/close relationships. Remapping preserves exact nominal, member,
operation, destination/formal and phase identities independently of physical
rows/task addresses. Replacement cannot shift publication to invocation-return,
weaken capture/cleanup/close promises or substitute unrelated completion.
The producer is responsible for published semantic truth; consumers validate
selected record shapes, ranges, references and support needed for safe use,
without semantic re-proof (see [consumer verification](reader-trust.md)). A
record flag does not replace producer correctness. Optional audits do not
prove arbitrary external provider behavior or authored unsafe promises.
Physical task/operation/output/phase/capture/witness programs and codecs require
their own complete schema, admission and conformance vectors. Optional Attr
data or held opcode/table space cannot encode mandatory phase semantics.
General task/cancellation/supervisor APIs and physical producer phase records
remain separately selected; ordinary output lifting and
invocation-phase Pure/NoAlias are unchanged.

## Lending iteration: encoding pending

The [lending contract](../language/lending-iteration.md) selects logical
`lending-iteration/1`, not a physical reference/loan representation, table,
attribute, feature bit or opcode. Retain exact readonly/writable sync/async
Core cursor/sequence requirements, selected witnesses, complete T/TCursor
and descriptor types, factory/direct source mode and bound acquisition.
Readonly factories retain readonly access; writable factories require mut
receivers with actual backing authority. Both async factories are synchronous.
Mode filtering precedes source ambiguity; reference-valued value items and
ordinary reference attenuation do not provide another lending witness.

The init-output selector identifies the initialized inner ref<T>/write ref<T>
value of an exact formal, not the Boolean result, formal descriptor slot or
hidden caller output storage. Preserve destination/operation/version and
invocation/completion phase, true/valid-loan false/vacant failure/vacant
publication and private/partial cleanup. Async roles retain the selected
completion reservation, observation, guarded joins and responsible ownership.

Retain permitted existing element-place origins, exact stored T/access,
receiver/cursor storage versus forwarded backing and every source/path,
root, lease, guard, validity, affinity and capture dependency.
The standard bound permits cursor storage and its checked backing set.
Published refinements retain exact member/witness identity, condition/evidence,
narrower source set and affected-operation invalidation, including advance/
close faults and residual cleanup. Removing cursor-storage dependence alone
does not preserve a lease released by close or backing invalidated by a source.
Generic callers use only exposed/proven refinements, not private bodies.

Forwarding maps exact output/source/phase/refinement relationships without
covariant init storage, authority upgrade, early publication or new lifetime.
Loop names alias T places; ending the binding/descriptor releases its loan,
not T ownership. Copies/reborrows keep their invalidation obligations.
Retain once-only acquisition, publication/observation before body, retirement
before invalidating advance/close and awaited close before header cleanup.
Eager spreads still select value iteration; no automatic lending dereference,
ref spread or generated producer registration is introduced.

An implementation claiming support preserves supported complete
relationships. The producer is responsible for published body facts;
consumers validate selected record shapes, ranges, references and support
needed for safe use, without semantic re-proof (see
[consumer verification](reader-trust.md)). Remapping preserves
role/member/formal/
output/source/condition identities independently of row numbers/debug names.
Replacement cannot weaken a relied-on source/refinement, change element
authority, add capture or change publication/invalidation/cleanup.
General source/target annotation syntax is supplied by
[dependencies and views](../language/dependencies-and-views.md);
associated lifetime items, view/
capture/loan/task/frame layouts and physical programs/codecs remain separately
gated. No optional Attr field, held type/table slot or opcode acquires these
mandatory meanings, and no allocation is activated by the logical rule set.

## Async execution and generated producers: encoding pending

[Async execution](../language/async-execution.md) and
[generated producers](../language/generated-producers.md) select logical
`async-execution/1` and `generated-producers/1`. They activate no physical
table, feature bit, attribute field, task/frame layout or opcode.

Retain the ordinary async versus non-async task-factory identity, complete
task-like result/payload, logical operation establishment and synchronous
argument/failed-establishment versus established failed-completion boundary.
Preserve user precondition, body, completion check, cleanup and failure
attribution irrespective of suspension or physical task materialization.
Synchronous establishment failures and later task-completion failures retain
their distinct operation extents. An invocation that returns a Task does not
promise that the later operation completes successfully.

Generation records retain exact defining module, primary cursor definition,
sole producer callable and original code owner/source unit. IteratorCursor
and Iterator are resolved no-argument primary schemas, not mutable runtime
attribute objects or same-spelled user definitions. The result selects its
exact owned named affine cursor and one Core direct role, with matched timing,
complete T and T/ref<T>/write ref<T> output. It is not a sequence factory,
Task<Cursor>, erased interface carrier or body-inferred return type.

Retain the checked bijection/permutation of enclosing/own type and constant
formals into shell arguments, equivalent domains/constraints and complete
definition/forward-substitution evidence. All choices affecting private
capture/frame representation remain part of the complete type.
Physical generated fields/methods preserve logical original code ownership;
debug names or relocation cannot confer private/file access.

The retained lifecycle includes these relationships:

| Relationship | Required content |
|---|---|
| Creation | Eager source-ordered input/default/params staging, ordinary copy/transfer, captured receiver/formals, synchronous require/ensure and private cursor publication |
| Captures | All backing/source/root/guard/lease/affinity/responsibility bounds, input vacancy/ownership and valid creation-failure cleanup |
| Body | One lazy activation, exact resume/yield/completion state and active lexical scope/handler/defer identities |
| Output | Exact selected member/formal/output version, typed value or loan, private publication checks/cleanup and true/live false/vacant failure/vacant relation |
| Clauses/effects | Creator versus generated member identity/phase, completed payload selection, actual captured accesses and inherited caller domain/guarantees |
| End/close | Exhausted/failed state, body completion once, entered-scope-only early unwind, no replay/retry, transferred close responsibility and nonthrowing/nonsuspending residual destruction |
| Async | Establishment/failure boundary, exact reservation/observation, pending advance settlement, surviving item/loan and awaited close before header cleanup |
| Representation | Complete generated frame/capability/layout and code dependencies, distinct nominal identities and explicit admission/trust |

False never substitutes for failure. Task status or a retained completion
does not release a pending reservation or republish output into another
version. Source Pure/ConstEval/NoAlias does not imply corresponding
later producer operation behavior. Additional generated slot guarantees are
checked; they cannot narrow the inherited caller domain.

Linking/remapping preserves type/callable/formal/member/source/phase/cleanup
identities and outstanding obligations independently of rows/debug names.
Replacement preserves relied-on capture, lifecycle, effect, output, failure
and complete frame-layout/ABI contracts. Equal bodies/layout or generated
origin cannot make a named cursor representation-independent.
An implementation cannot claim support for unsupported required semantics.
The producer is responsible for published body facts; consumers validate
selected record shapes, ranges, references and support needed for safe use,
without semantic re-proof (see [consumer verification](reader-trust.md)).
Task/awaiter/cancellation APIs, physical generation/phase/frame/root/loan
programs and independent codecs/vectors remain separately gated; optional
Attr data or held opcode/table space cannot carry these mandatory meanings.

## Thread capabilities and sharing: encoding pending

[Sharing and handoff](../runtime/sharing-and-handoff.md) select logical
`thread-capabilities/1` and `sharing-and-handoff/1`. Retained declarations
preserve exact capability identities, primary opt-in, conditional generic
premises and complete forms. Invocation binds actual extent/generation,
alias/token authority, holders, publication/return/release endpoints, effects,
code/backing/root lifetime and use/cleanup affinity.

Required capability/condition/protocol/profile facts cannot be omitted or
substituted by optional Attr data. The producer is responsible for the truth
of its published implementation contracts; capability opt-in or an annotation
alone does not prove them. Consumers validate selected record shapes, ranges,
references and support needed for safe use, without semantic re-proof (see
[consumer verification](reader-trust.md)). Unsupported algorithms do not acquire
admission.
Replacement cannot remove relied-on eligibility, strengthen conditions/caller
obligations or weaken ordering, return, cleanup and failure guarantees.
Logical approval assigns no capability flag, guard/reservation table, proof
certificate, required-feature number or opcode. Physical records and target
profiles remain independently admitted.

## Concurrent executions: encoding pending

[Concurrent executions](../runtime/concurrent-executions.md) selects logical
`concurrent-executions/1`: exact live locations and actual read sources,
matched access/fence communication, per-generation coherence, required access
order, ordinary-hb-consistent SC and full boundaries, grounded source-path
causality and initialization/quiescence/reuse.

Retained operations preserve valid compile-time-known orders, exact
representation/width/protocol support, successful updates and read-only
CAS/checked-arithmetic failure effects. Failure may acquire through its actual
source but has no write/release entry; Sequential attempted failure retains
SC/full ordering. Result omission changes none of these promises.
Replacement cannot weaken relied-on ordering, publication, failure or
lifetime requirements. Missing required facts and unsupported target/helper/
collector profiles are rejected before use.

These logical rules allocate no event record, causal certificate, order
encoding, feature number, table, Attr field or opcode. Dynamic identities
are not metadata row numbers or pointer bits. Optional attributes and held
ranges cannot substitute for mandatory semantics; physical codecs and
independent target-refinement evidence still need admission.

## Owning fields and cleanup: encoding pending

[Owning-field contracts](../language/ownership-and-cleanup.md) select logical
`owning-fields-and-cleanup/1`: complete type/field and storage identities,
structural partial move/repair, checked replacement, and exact retirement.
Complete authored Cleanup precedes automatic reverse-declaration fields;
failed unpublished construction unwinds reverse acquisition without invoking
incomplete-parent Cleanup. A partial owner is not whole vacant/complete T.

Retained contracts preserve selected authored/derived cleanup identity/effects,
declaration order, complete-receiver boundaries, guarded field state and
source/root/protocol obligations. Runtime invocation binds those declarations
to actual acquired values and storage generations; row/name remapping cannot
revive a moved child or reorder observable cleanup. Adding authored Cleanup
or weakening a relied-on partial-move/retirement guarantee is incompatible.

An implementation cannot claim support without the required field/cleanup
facts or for unsupported partial-state/call/root profiles. The producer is
responsible for published implementation truth; consumers validate selected
record shapes, ranges, references and support needed for safe use, without
semantic re-proof (see [consumer verification](reader-trust.md)).
The existing narrow Cleanup node does not encode this general contract.
Logical approval allocates no field-state mask, acquisition journal, cleanup
program, feature bit, table or opcode; physical codecs remain independently
admitted. Optional Attr data cannot substitute for mandatory semantics.

## Task responsibility: encoding pending

[Task responsibility](../language/task-responsibility.md) selects logical
`task-responsibility/1` and `consuming-task-await/1`.
Core Task<T> is an affine completion owner; its compiler-owned wait consumes
that owner, supplies exact T or terminal failure and preserves all attached
output/reservation relationships. IsCompleted is status, not output discharge
or automatic error handling. The selected `Task<_>` existential view retains
each value's hidden complete T and corresponding checked evidence, exposes
only its family-wide operations, and adds completion-only consumption without
payload erasure. Broader observer/awaiter/supervisor APIs remain separately
admitted.

Published records retain the exact Core family/code owner and normalized T,
ownership and wait roles, status/observation/payload contracts, failure/effect
phases, every-exit settlement/transfer and complete source/capture/output
requirements. Each existential formation/use record owns a typed evidence
shape with one entry per hidden formal ordinal and nested constraint-keyed
witness slots. Where a formation has statically known hidden arguments, retain
their concrete TypeSpecs and exact direct/projected witness references;
parameter/result/storage owners specify the required shape while each runtime
Task value supplies its own hidden arguments and witnesses. Invocation binds
those declarations to actual operation/owner/value/output versions. Remapping
row or debug-name identities cannot duplicate responsibility, erase a payload
or republish an output after reuse.

Every owner extent ends only by valid settlement or explicit responsibility
transfer, including exceptional call/staging/holder transitions. Residual
destruction is nonsuspending/nonthrowing; hidden blocking, async joining,
cancellation or supervision cannot stand in for missing obligations.
Replacement preserves those choices and relied-on capture, failure, output,
affinity and complete task/wait/root representation contracts.

An implementation cannot claim support for unsupported required rules or
task/wait profiles. Producers supply truthful owner and observation/output
facts; consumers validate selected record shapes, ranges, references and
support needed for safe use, without semantic re-proof (see
[consumer verification](reader-trust.md)).
The logical contract allocates no task/awaiter/continuation/root record,
table, feature bit, attribute field or opcode. Physical codecs, callback and
scheduling substrates and broader task/cancellation catalogs remain gated;
optional Attr data and held ranges do not acquire mandatory task meanings.

## General dependency contracts: carrier and capability integration pending

[Dependencies and views](../language/dependencies-and-views.md) selects logical
`dependencies-and-views/1` and `dependency-source-schemas/1`.
DependsOn, Captures and ForwardsBacking retain whole receiver/formal
identities, result/initialized-output or capture destinations,
borrow-source-storage versus backing-only source permissions and checked
published member/witness conditions. The shared MethodDef-owned Dependency
clause has a 12-byte payload:

```text
u8 destinationKind, u8 sourceKind, u8 relationship, u8 phase,
u32 destinationOrdinal, u32 sourceOrdinal
```

Destination kinds are `1=result`, `2=checked init-output`,
`3=retained formal holder` and `4=retained receiver`. Source kinds are
`1=formal` and `2=receiver`. Relationship kinds
are `1=borrow source storage`, `2=forward existing backing` and `3=declared
union`. Phases are `1=normal publication`, `2=retention/capture into an
existing holder on every applicable exit` and `3=deferred-completion
publication`. Rows are repeatable and keyed by destination kind/ordinal,
phase and source kind/ordinal. An explicit empty-source row uses zero
source/relationship/ordinal fields and cannot coexist with source rows for
that destination and phase. The 20-byte total Contract row includes its
eight-byte header.

The shared [definitions](../language.md#definitions-and-notation) explain
reference/view validity through lifetime and backing. DependsOn, Captures
and ForwardsBacking remain precise logical relationship names, not synonyms
for owning or keeping data alive and not newly assigned wire variants.

**Backing and GC-root obligations (source accepted; carrier pending).** Normal
class handles `C` and `box<T>` designate stable, non-moving shared backing;
copying an eligible handle retains that same object and its single Cleanup
obligation. The distinct `gc C`, `gc box<T>` and `gc ref<T>` complete types
select collector-managed backing, which may move. Their roots retain
reachability but not independent native resources, guards or write
authority. `gc ref<T>` identifies one initialized exact place, not adjacent
storage. The explicit `ref *root` reborrow carries the pin/backing dependency
that makes that place stable for the bounded result; the explicit `C(gcObject)`
conversion pins the whole object, with copied stable handles sharing support
until the last converted handle retires. Raw pointers remain non-owning and
non-rooting, and any pointer derived from a checked reference retains a
dependency on the required stable backing.

These facts must remain correlated with complete type identity and actual
runtime values through copying, fields, calls/results, verification,
dependency binding, replacement and lowering. An ordinary type/Dependency
reference alone does not prove that a root, retain or pin is present, and a
GC root does not satisfy a separate cleanup/owner/guard obligation. Cleanup
is one object-level obligation for eligible unique values, aliasable class
objects and boxed payloads: normal shared backing triggers it after final
strong-hold/dependency retirement; GC backing triggers it once after
collector-established unreachability and before reclamation. GC Cleanup
cannot resurrect the receiver and has an unspecified runtime cleanup thread,
so transitive obligations must be thread-neutral or transferable.

The prior Pin TypeSpec qualifier is no longer a source type contract and its
former registry bit is reserved, not repurposed. The complete TypeSpec form,
retention/root/pin/cleanup carriers, capability registration, runtime profile
and ABI/carrier layouts for the accepted GC forms remain unassigned. Do not
emit or accept guessed encodings, and do not treat private `native-18`–`20`
managed-node/class paths as standard TRRM support. Producers and consumers
must preserve and verify these obligations before affected use. This gate
depends on the current-registry codecs, shared references, failure/cleanup,
native declaration/Body, generic TypeSpec and root/target-profile work in
[conformance gaps](../../implementation/conformance-gaps.md#cg-027).

Receiver identity is distinct from a formal ordinal. Formal names and direct
`this` are source recipes, not persisted runtime selectors or row numbers.
Publish their resolved declaration identities, target kind/phase/extent,
source modes and checked propagation permissions with the exact declaring
code owner, complete normalized types and default/inherited rule version.
Invocation and storage bind those contracts to actual source/destination
instances and versions; a declaration record does not serialize future runtime
instances. Preserve the resulting data/shape backing, roots, owner/lease/guard,
publication, load and affinity obligations.
An empty receiver/formal source set does not erase independent definition-owned
or layout/code dependencies. Copying, erasure, joins and transfer retain the
applicable obligations, without acquiring ownership or extending storage.

Captures is a possible retained-state contract, including mutation surviving
failure. Returned/output dependencies do not authorize unrelated receiver
storage. Completion operations, generated creation cursors and their payloads/
outputs retain their independently declared phases and responsible cleanup.
Backing-only excludes references to the source's own eligible storage but
does not prove advance/close survival, immutable contents, NoAlias or
sharing/exclusive authority.

Witness records preserve the broad requirement and separately checked
published implementation fact. Member-contract conditions resolve an exact
unambiguous requirement and its mapped source/target identities; generic use
must establish them. Bodies, specialization, equal addresses, pinning and
devirtualization cannot silently strengthen retained contracts.
Compatible replacement cannot add dependencies/capture/invalidation, weaken
backing-only permission or change relied-on target phases, authority,
ownership, layout/ABI or capability bounds.

An implementation cannot claim support for unknown required rules,
unavailable source/capability facts or unsupported stored/erased/crossing
representations. Producers supply truthful relationships; consumers validate
selected record shapes, ranges, references and support needed for safe use,
without semantic re-proof (see [consumer verification](reader-trust.md)). An
attribute alone supplies no missing contract.
Dependency destinations do not include native-output values or resolve
through a NativeOutput row. A required output/region fact outside the assigned
shared carriers remains unsupported until its complete representation is
admitted. The shared row does not
encode a general field/subregion path. Exact registry projection and
capability closure remain integration work; logical relation codes are
approved and do not activate optional attribute data as mandatory semantics.

## Constant-generic domains: encoding pending

The [constant-generic contract](../language/constant-generics.md) defines logical
`constant-generics/1`, not a new table, TypeSpec form, attribute/value tag,
contract clause, feature bit or opcode. Existing scalar constant bits do not
encode a nominal constant domain or authorize arbitrary generic arguments.

Retain each formal's declaring owner/ordinal, exact normalized domain and
earlier-formal default dependencies. Primitive integer/Boolean values retain
their exact mathematical/logical value; enum values additionally retain the
nominal enum and exact fixed-width base. An eligible wrapper retains its
complete nominal type, resolved Transparent single-instance-field/carrier
relationship, Copy/frozen validity, per-target Blittable and typed value-codec
obligations. No second marker or carrier-exposure interface is required.
Field shape, same-spelled attributes or user equality bodies cannot supply
these records.

Canonical value identity preserves the complete domain and carrier value,
not a host object blob, padding, source radix or row number. Constructor/
literal APIs remain separately bound operations; a retained checked constant
does not require rerunning a constructor or invoking Equals/GetHashCode.
New construction still requires its admitted constant program/intrinsic,
validity checks and resource/dependency contracts.

Valid argument representations retain required shapes, references,
domain/carrier compatibility and typed value facts. The producer is
responsible for published semantic validity; consumers validate record
shapes, ranges, references and support needed for safe use, without semantic
re-proof (see [consumer verification](reader-trust.md)). An artifact flag
does not replace producer correctness. Unknown required semantics or
missing/incompatible obligations reject affected use and do not authorize
erasure to a scalar argument, raw-bit wrapper fabrication or runtime-evaluation
fallback. Linking/remapping preserves nominal identities,
canonical values, target and validity dependencies independently of physical
row reassignment.

Physical generic/value/validity records remain separately gated. This logical
approval cannot reinterpret optional Attr data or an unassigned slot as
mandatory constant-generic semantics.

## Registered query contracts and representation

The [registered-query contract](../language/registered-queries.md) selects
`typeof`, `sizeof`, `alignof`, `offsetof` and `countof`, without assigning a
query opcode or source API. The physical query assignment is the `DE`
family in the [instruction set](../il-instruction-set.md#assigned-long-families)
and the machine registry. Kinds 1/2/5 use complete TypeMap subjects; kinds
3/4 use FieldMap tokens. The Body-local FieldMap selects a direct one-field
FieldDef/Import token or a FieldPath token into global section `0x004B`.
FieldPath rows carry a complete root TypeSpecRef and ordered FieldDef/Import
steps. Direct field tokens are valid only for an exact unconstructed
non-generic declaring owner; complete constructed, qualified, generic or
nested paths use FieldPath. No generic `LayoutQuery` Body record is assigned.

`typeof` materializes internal TypeIdentity only when a runtime comparison
needs a value. Its exact comparison reuses binary `C2` controls `00..02`
and short `40`: identical complete identities produce E=`2`, distinct
identities produce U=`8`; boolean equality/inequality remain ordinary
bool. `BlockEntryState` carrier 2 carries a TypeIdentity with unused
`typeMapIndex=0` and `accessKind=0`. It is not a public type, reflection
object, TypeMap code or serialized constant. Metadata-only type recipes bind
their complete type without executing TypeOf. Open generic uses require exact
identity/substitution evidence from the selected execution model; unsupported
uses reject rather than use a formal ID, layout equality or reflection
fallback.

The first four are dedicated source queries; `countof(Place)` binds a nominal
synchronous Pure Count witness with a readable instance `Count : nint`
property getter from the shared Core `ICountable` requirement. Same-shaped
properties from unrelated interfaces, same-named methods and optional
attributes do not supply these facts. Type and known-shape `countof` lower to
ordinary exact typed `nint` constants; live Place Count uses the selected
getter through ordinary call machinery. There is no universal Count/Length
opcode or `length` alias.

Retain the exact operation, complete normalized type or bound Place/field
path, selected target ABI/width, result domain, Blittable/unsafe authority,
and coherent fixed or execution-dependent layout facts. Preserve the exact
shared `ICountable.Count` requirement, property/getter and typed implementing
witness identity for live Place observations; fixed type/known-shape counts do
not dispatch it. A retained layout observation records its value
and fixed-versus-rebuild/rebind/invalidation basis; the scalar result codec
does not freeze its queried subject. `TargetLayoutDependency` (`0x0045`)
keeps its 20-byte row and four kinds; size/alignment use empty field ranges,
while offset paths own ordered `TargetLayoutDependencyField` (`0x004A`)
rows containing FieldDef/Import references. Each observation exclusively
owns its range. If a required structured constant embeds
a target-dependent query, retain its recursive typed value and field
identities; target materialization uses validated `Layout` records, with
explicit fixups only for genuine address relocations. JIT/AOT resolves only
unresolved query work and does not replay completed source evaluation.
Semantic `SizeOf`, `AlignOf` and `OffsetOf` operations remain represented
until a selected target can legitimately fold a fixed result.

An implementation claiming support preserves the query semantics,
target/contract compatibility, `nint` result domain, authorization and
surviving dependencies. The producer is responsible for published semantic
facts; consumers validate selected record shapes, ranges, references and
support needed for safe use, without semantic re-proof (see
[consumer verification](reader-trust.md)). A same-named library declaration or artifact flag
supplies no missing contract. Malformed/missing records, unresolved evidence
and unknown required rules reject affected use. Host-layout inference or a
success-shaped fallback cannot be claimed as the specified query behavior.

Linking, trimming and remapping preserve exact operation/type/field/target/
witness/dependency identities independently of row indexes, source/debug
names or physical packing. Representation replacement cannot retain stale
coupled observations without regeneration/rebinding/invalidation, nor violate
published fixed observations. Queries do not invent field access,
object-payload reflection or arbitrary value codecs.

Feature 13 `LayoutQueries` registers the complete query/identity/path and
FieldDef binding capability. Common map-mask framing alone does not require
it. A required Feature number does not supply exact open generic identity
evidence, Count witnesses, dynamic target models, frozen-value codecs or
layout coupling; those retain their separate gates. Optional Attr data, a
held slot or name-based call cannot silently carry required semantics.
Logical selection alone makes no physical interoperability claim.

## Published predicates and evaluation forms

The [published-predicate and effect contract](../language/published-predicates-and-effects.md)
defines logical `pure-evaluation/1`, `effect-footprints/1` and
`published-predicates/1`, plus `noalias-parameters/1` requirements. The
assigned record identities below do not assign operation names or opcodes,
release a schema, or imply implementation support.

The following labels are explanatory names for the four selected semantic
record families. They are not aliases for sections or clauses, and they do
not add tables, IDs, flags or codecs:

| Explanatory family label | Existing physical records | Boundary |
|---|---|---|
| `EvaluationForms` | MethodDef-owned `EvaluationForm` Contract clauses (`0x000A`) | Plural family label; each clause retains its exact form and callable contract |
| `Predicate`, `ReadFootprint` | `PredicateGraph` (`0x0037`) and its separate `PredicateReadFootprint`, `PredicateReadRoot` and `PredicateReadDependency` records (`0x0038..0x003A`) | The graph and actual observation footprint remain separate typed records |
| `ConstantExecution` | `ConstantExecution` (`0x0042`), its argument/dependency rows and `TargetLayoutDependency` | Resource ceilings remain in the execution row; the request references an ordinary checked Body or exact admitted intrinsic, not a second instruction stream |
| `FrozenValue` | `FrozenValueNode`/`FrozenValueChild` (`0x003F..0x0040`) and their typed dependencies | The validated result remains distinct from the execution request and its producer |

An admitted record preserves the exact declaring callable/accessor identity,
closed signature and receiver/parameter/result modes, selected form, domain,
finite read roots, writes, fresh outputs, transfers/retention, activity and
failure bounds, initialization/publication obligations and required rule
versions. Ordinary and pure-form records remain distinct.

Bound predicates retain typed expression graphs over staged input, receiver,
saved result and initialization-output identities, selected operation/member/
witness identities, Boolean/pattern structure and complete dependencies.
They do not rely on parameter names, private imported fields, retired
phase-local bindings or an unstated entry snapshot. Getter identity is the
declared accessor, not a fabricated name. Variants, witnesses, default bodies
and replacement preserve the required forms and bounds. Serialized or
reconstructed records remain subject to mandatory structural safe-consumption
checks and support admission; consumer verification does not re-prove their
semantic truth.

### Assigned pre-v1 identities

These pre-v1 assignments are coordinated with the
[registry](registry.md) and [metadata schema](../il-metadata.md). They have no
unpublished-artifact compatibility promise.

| Namespace | Assignment |
|---|---|
| Required sections | `0x0025=InterfaceDef`, `0x0026=InterfaceBase`, `0x0027=AssociatedTypeDef`, `0x0028=InterfaceWitness`, `0x0029=WitnessMember`, `0x002A=WitnessAssociatedType`, `0x002B=BuilderRegistration`, `0x002C=TextTarget`, `0x002D=TextContentShape`, `0x002E=TextCaptureShape`, `0x002F=TextFragmentShape`, `0x0030=ConstantSource`, `0x0031=InterfaceConstDef`, `0x0032=AssociatedConstDef`, `0x0033=WitnessAssociatedConst`, `0x0034=UnionCaseDef`, `0x0035=UnionCaseField`, `0x0036=AssociatedRelation` |
| Required sections | `0x0037..0x0049`, in order: `PredicateGraph`, `PredicateReadFootprint`, `PredicateReadRoot`, `PredicateReadDependency`, `PredicateNode`, `PredicateNodeOperand`, `PredicateChildEdge`, `PredicateCapture`, `FrozenValueNode`, `FrozenValueChild`, `ImmutableData`, `ConstantExecution`, `ConstantExecutionArgument`, `ConstantExecutionDependency`, `TargetLayoutDependency`, `LogicalInputView`, `LogicalViewPathEntry`, `TargetProfile`, `TargetValueBinding`; then `0x004A=TargetLayoutDependencyField`, `0x004B=FieldPath` |
| `Contract` clauses | `0x000A=EvaluationForm`; `0x000B=PredicateGraph` |
| `DefId` kind | `0x0021=UnionCaseDef` |
| Feature revision 1 | `9=NominalUnionMetadata`, `10=PublishedPredicates`, `11=TypedFrozenValues`, `12=ConstantExecution`, `13=LayoutQueries`; IDs 1-8 remain separate Native/Generic candidates, not assignments made by this batch |
| Feature prerequisites | Feature 11 requires 9; 10 requires 11; 12 requires 10 and 11; 13 has no unconditional Feature prerequisite. Generic `ConstantSource` tags 2 and 3 additionally require Feature 12. The complete registrations remain gates even though their numeric identities are assigned. |
| Target profile | `profileId=1`, revision `1` is `windows-x64-native/1`; `portable-semantic` remains a broad `targetRequirement`, not a concrete layout/ABI profile. |

The [semantic operation display and layer map](../il-instruction-set.md#operation-identity-encoding-and-presentation)
is owned by the coded instruction chapter. It changes neither these physical
record names nor their assignments. Feature assignments do not complete a
capability whose required payload schema or external registration remains
open.

The resolved Core Pure/ConstEval schemas and evaluation-mode properties
retain their exact identities, argument/placement/multiplicity policy and
mode recipes. Constant mode implies pure mode; annotations do not turn
every ordinary invocation into either mode. A required call additionally
needs its actual admitted body/intrinsic, value recipe and dependencies.
No source query gains an opcode or bypasses an unavailable persistent
representation merely because a private compiler can evaluate it.

A public-contract artifact used for independent required constant execution
must retain the checked Body or an equivalent complete admitted program,
selected form, transformed inputs, executed dependencies and deterministic
resource recipe. A null, empty or throwing reference stub cannot stand in
for that program. A published `Blittable` value commitment likewise retains
the field and representation facts needed to validate it. A non-Blittable
reference facade may omit private implementation fields where its contract
allows; the implementation artifact must still retain its actual fields.
These requirements follow explicit public contracts and do not publish
unrelated private implementation details.

The approved direction keeps four typed record families distinct:

* Evaluation-form clauses are exact-method `Contract` rows of kind
  `0x000A`. The fixed 12-byte row is the 8-byte header followed by
  `u8 formTag, u8 flags=0, u16 reserved=0`, with tags `1=Pure` and
  `2=ConstEval`; the latter implies pure-form support.
* Each published condition uses a separate typed `PredicateGraph`, not the
  callable's ordinary `Body`. Encode each evaluated expression occurrence in
  its own typed node row, with explicit ordered child-edge rows and one graph
  root. The approved edge candidate is a 12-byte row:
  `u32 parentNodeRef, u32 childOrdinal, u32 childNodeRef`. Both node fields
  are nonzero standard typed row references; `childOrdinal` is zero-based and
  contiguous per parent. The root has no incoming edge, every non-root node
  has exactly one parent, and edges cannot cross graphs or form cycles.
  Each node uses the approved 16-byte candidate row
  `u32 nodeKind, u32 resultTypeSpec, u32 operandFirst, u32 operandCount`.
  `resultTypeSpec` is a nonzero standard typed row reference. The operand
  range uses the standard contiguous-range encoding, with `(0,0)` when empty
  and a nonzero in-bounds range otherwise. Each operand uses the approved
  8-byte candidate row
  `u8 operandKind, u8 flags=0, u16 reserved=0, u32 operandRef`; its nonzero
  standard typed row reference targets the table selected by `operandKind`.
  `nodeKind` identifies structural expression form, not the selected
  operation identity or an executable Body opcode. The logical families are
  value/subject, access/projection, operation application, selected call,
  test/pattern, Boolean composition and construction; use distinct structural
  variants where evaluation behavior differs, including short-circuit versus
  eager Boolean composition. Pattern-test variants distinguish initialization
  and presence tests, type and generic-type tests, qualified union-case tests,
  and structural tuple patterns. Pattern `not`, `and` and `or` are pattern
  composition forms, distinct from Boolean `!`, `&&` and `||`. Selected
  TypeSpecs and union-case   identities use typed operands; ordered child edges
  preserve nested payload-pattern order. Pattern-local capture identities
  are graph-local zero-based ordinals assigned in declaration order; resolve
  capture references only within their owning graph, never by source spelling,
  physical row number or pattern-path reconstruction. Captures use one typed
  `PredicateCapture` record family; each record refers to its owning graph and
  ordinal, and capture operands refer to that row. The row is not the capture
  identity, and `PredicateGraph` keeps its assigned 8-byte shape without a
  capture range. `PredicateCapture` is a fixed 16-byte row
  `u32 graphRef, u32 ordinal, u8 role, u8 flags, u16 reserved=0,
  u32 capturedTypeSpec`; roles are `1=Value`, `2=CheckedReference`, and
  `3=GenericTypeArgument`. Every role retains its complete captured TypeSpec.
  Value captures may set flag `0x01` for mutability; all other bits and flags
  for the other roles are zero. Existing typed facts carry ownership/presence,
  access, backing/provenance and branch evidence; do not duplicate those
  payloads or infer unsupported facts.
  Canonical `PredicateCapture` rows follow the resolved identity of their
  owning graph: the owning MethodDef identity, phase tag and phase-local
  ordinal, then the capture ordinal. Re-sort after typed-reference remapping;
  physical references and row numbers are not order or identity keys.
  Capture roles distinguish value captures with their exact TypeSpec and
  ordinary ownership/presence
  facts, explicit checked-reference captures with their existing `ref<T>`
  referent, access authority and backing/provenance, and generic-type-argument
  captures with the complete hidden TypeSpec and checked evidence available
  in that branch. A checked-reference capture does not add indirection; a
  `ptr<T>` capture remains a pointer-typed value, not a checked reference.
  Exact typed-fact references and imported remapping follow their owning
  schemas. Ordered child edges represent
  expression-valued subexpressions and nested pattern-test occurrences;
  operand rows represent typed
  non-expression identities such as subjects, literals, selected
  operations/members/methods or witnesses. Each operand row belongs to exactly
  one node; operand ranges are disjoint across nodes, with repeated semantic
  operands serialized as separate rows even when their typed target is
  shared. A literal operand references the root `FrozenValueNode` of the
  existing typed-value tree, which supplies its complete TypeSpec and scalar
  or structured value payload. Do not inline literal bytes, introduce a
  predicate-specific literal representation, or execute code to recover the
  value. An operand denoting a read root references the matching root-entry
  row in that graph's `PredicateReadFootprint`; validate that the entry belongs
  to the graph's own footprint. Compare the root identity by its kind and
  identity payload, not by the row reference. A selected-call operand
  references the matching direct dependency-entry row in that graph's
  footprint for its MethodDef, Constraint, InterfaceWitness or
  WitnessProjection target. A constructed generic call preserves its complete
  call-site substitution as a separate typed fact, not as part of the
  declaration/dependency identity. Field and property access operands
  reference the selected `FieldDef` or `PropertyDef`; an evaluated property
  getter is separately represented as a selected call. For a constructed
  generic owner, preserve the complete owner TypeSpec substitution; derive
  field placement from validated `Layout`, not stored offsets. A qualified
  union-case pattern references its `UnionCaseDef` and complete constructed
  owner TypeSpec. Exact node/operand-kind values, target schemas and per-kind
  roles/arities remain open. This carrier assigns no predicate operation
  display names or Body opcodes.
  Each `PredicateGraph` has the approved 8-byte candidate row
  `u32 rootNodeRef, u32 readFootprintRef`. Both are nonzero standard typed row
  references in `1..0xFF_FFFF` with reserved-zero high bytes; each field fixes
  its target table. The graph row has no separate subject range: subject
  identities are typed operands in graph nodes.
  Expression nodes are not shared, while immutable typed identities may be
  referenced by multiple nodes. Each `PredicateGraph` is referenced by
  exactly one MethodDef-owned typed `Contract` clause. Canonical node-row
  order groups graphs by the stable logical identity of that clause's
  MethodDef, then phase tag and phase-local ordinal; within each graph, nodes
  are ordered root-first in depth-first traversal, visiting children by
  `childOrdinal`. Remapping typed references may change physical node order
  and requires re-sorting; table position is not semantic. Compare the
  resolved MethodDef identity tuple component-by-component; no separate
  sort-key bytes are serialized. Its key includes module semantic identity,
  owner TypeDef identity or module-level scope, declaration/file scope where
  applicable, name, complete MethodSig and generic formal domains/constraints.
  For imported expectations, also include the selected producer identity and
  export key. PredicateGraph/node/edge section assignments are in the registry;
  complete Feature registration, node/operand-kind values, and per-kind operand
  roles and arities remain open. The physical child-edge table is
  sorted by unsigned `parentNodeRef`, then `childOrdinal`; this order is not
  semantic, and readers validate child ordinals independently. Remapping node
  references may change physical edge order.
  Each clause retains the predicate phase, an explicit zero-based, contiguous
  phase-local `u32` declaration ordinal and exact graph reference. One clause
  kind serves all predicate phases. The approved candidate row is 20 bytes
  total: the 8-byte Contract header followed by
  `u8 phaseTag, u8 flags=0, u16 reserved=0, u32 phaseLocalOrdinal,
  u32 predicateGraphRef`. The `u8` phase tags are `1=Precondition` and
  `2=NormalPostcondition`; zero and unknown tags are invalid. The graph
  reference is a nonzero standard typed row reference: a little-endian `u32`
  row index in `1..0xFF_FFFF` with reserved-zero high byte, resolved by the
  clause's target table and remapped as a typed reference; it is not a
  polymorphic `DefId`. The clause kind and PredicateGraph section are
  assigned; complete Feature registration remains
  open. The graph owns its
  root/subjects/nodes, not a duplicate callable, phase or order identity. Gaps
  and duplicate ordinals are invalid.
  Each graph references exactly one separate typed read-footprint
  record for its observation. Each footprint has the approved 16-byte
  candidate row `u32 rootFirst, u32 rootCount, u32 dependencyFirst,
  u32 dependencyCount`, carrying two flat typed ranges: its
  complete logical read roots and its direct call/witness dependencies; it
  does not nest dependencies under each root or duplicate graph, phase or
  order identity. Each range uses the standard contiguous-range encoding:
  empty is exactly `(0, 0)`; otherwise the first row is nonzero, has a
  reserved-zero high byte, and the checked range is in-bounds for its typed
  table. Each root entry has the approved 8-byte candidate row
  `u8 rootKind, u8 flags=0, u16 reserved=0, u32 rootIdentity`. Closed tags
  are `1=receiver value/state`, `2=input value`, `3=input state`,
  `4=logical output`, `5=ordinary result`, `6=immutable data`, and
  `7=evaluation mode`; zero/unknown tags and nonzero flags/reserved fields are
  invalid. Tags 2-4 carry a zero-based parameter/destination ordinal; tag 6
  carries a nonzero standard typed row reference to one `ImmutableData`
  identity family. Its compact tagged identity references the exact
  declaration, either a resolved constant or a published immutable region,
  including the complete owner substitution where applicable. Its approved
  12-byte candidate row is `u8 kind, u8 flags=0, u16 reserved=0,
  u32 declarationDefId, u32 ownerTypeSpec`. Closed `kind` tags are
  `1=ResolvedConstant` and `2=PublishedImmutableRegion`; zero and unknown
  values are invalid. `declarationDefId` is a nonzero shared `DefId`, and
  `kind` restricts its permitted definition kinds. `ownerTypeSpec` is zero
  only when no owner substitution applies; otherwise it is a nonzero standard
  typed TypeSpec reference to the complete
  owner construction. Reject unknown kinds, nonzero flags/reserved fields,
  invalid references and owner substitutions inconsistent with the target
  declaration. Compare `ImmutableData` semantic identities by kind, resolved
  declaration identity and complete owner TypeSpec identity (or absence of
  substitution), not by row references. It does not inline value bytes or
  copy publication/lifetime contract snapshots; those facts remain owned by
  the existing constant-value and static-initialization records and are
  followed through typed dependencies. Section `0x0041` and Feature
  `TypedFrozenValues` (`11/1`) own this family. A resolved constant must
  retain exact value identity, including witness binding where applicable;
  a `StaticField` alone does not establish a published immutable region.
  Tags 1, 5 and 7 require
  `rootIdentity=0`. Validate ordinals against the attached MethodDef signature
  and output destinations. Each dependency entry has the approved 8-byte
  candidate row `u8 dependencyKind, u8 flags=0, u16 reserved=0,
  u32 dependencyRef`. Closed kinds are `1=MethodDef` direct call,
  `2=Constraint` open generic dispatch, `3=InterfaceWitness` closed witness,
  and `4=WitnessProjection` projected witness. The nonzero standard typed row
  reference is in `1..0xFF_FFFF` with a reserved-zero high byte and targets
  the table selected by the kind; for an imported call it names the
  consumer-local expected MethodDef, whose Import binding is followed during
  dependency validation. Unknown kinds and nonzero flags/reserved fields are
  invalid. Each footprint contains unique direct dependencies only; a
  consumer recursively validates their own dependency records instead of
  copying transitive closure. Sections `0x0038..0x003A` and Feature
  `PublishedPredicates` (`10/1`) own these families. The
  read-root range
  remains the complete actual root set. Order each range by unsigned kind tag,
  then by the target's complete stable logical identity; compare root ordinals
  numerically for tags 2-4 and immutable data by its `ImmutableData` semantic
  identity. Compare
  MethodDef dependencies by module semantic identity, owner TypeDef identity
  or module-level scope, declaration/file scope where applicable, name,
  complete MethodSig and generic formal domains/constraints; imported
  expectations additionally include the selected producer identity and
  export key. Constraints sort by owning formal and exact
  constraint identity: owning generic declaration identity, formal ordinal,
  semantic kind, then canonical typed payload identity. For a MethodDef owner,
  use its complete declaration identity; its embedded constraint list is
  represented as kind-and-payload tuples, not recursively owner-qualified
  Constraint identities. Resolve interface-instance payloads as complete
  TypeSpecs, intrinsic-property payloads by registered property code, and
  associated-relation payloads by the complete `AssociatedRelation` identity
  (`declaringInterfaceTypeSpec`, associated declaration identity and
  `requiredTypeSpec`). Sort `InterfaceWitness` dependencies by complete
  implementing and interface TypeSpecs, and `WitnessProjection` dependencies
  by source witness identity, requested complete interface TypeSpec and
  projection-rule version. Use the same semantic identities for duplicate
  detection. Never order by physical row references or raw DefId bits.
  Resolve targets and compare family-specific semantic identity tuples
  component-by-component in the approved field order, using complete TypeSpec
  identities for nested type components; do not serialize a separate sort-key
  field. Root
  and dependency ranges are exclusively owned by one footprint each and are
  disjoint across footprints; do not share identical or partially overlapping
  ranges or individual entry rows. Equal semantic entries in different
  footprints are serialized as separate rows, while duplicates within one
  range remain invalid. The callable's
  allowed-root contract remains independently owned, and distinct graphs do
  not share actual footprints.
* Each required request owns one `ConstantExecution` record selecting an
  exact checked `MethodDef`/ordinary `Body` or admitted intrinsic, result
  `TypeSpec`, complete substitution, ordered transformed inputs,
  executed-dependency range, per-query layout-dependency range, its actual
  resource envelope as four inline ceilings (semantic steps, active frames,
  live abstract storage bytes and encoded result bytes). A checked Body is
  charged directly under the common evaluator rules. An opaque bulk intrinsic
  without an eligible checked Body requires a compact deterministic resource
  recipe owned and versioned by the intrinsic registration. The execution
  retains the exact intrinsic identity and transformed logical inputs, not a
  duplicate recipe; the consumer computes exact semantic work and peak
  incremental live scratch/value storage attributable to the intrinsic.
  Common rules account for ordinary input/result slots, entered frames and
  result encoding. A serialized action expansion and producer-reported totals
  are not substitutes. Scalar proof-work limits remain callable-scoped, not
  part of this request envelope. Its materialized result is a separate frozen
  value. The row grouping, inline ceilings, conditional compact-recipe
  direction and intrinsic-registration ownership are approved; exact recipe
  schema, field widths, reference kinds and allocations remain open.
  Its assigned `0x0042` row is 80 bytes:
  `u8 sourceKind, u8 flags=0, u16 reserved=0, u32 selectedSourceRef,
  u32 resultTypeSpec, u32 targetProfileRef`, followed by four standard
  `u32 first, u32 count` ranges for substitution, ordered arguments, executed
  dependencies and target-layout dependencies, then four `u64` ceilings in
  the order above. Source tags are `1=CheckedBody` and `2=Intrinsic`; the
  selected-source reference targets the exact Body row or registered
  IntrinsicRegistration. Each row owns its ranges. The substitution range
  contains TypeSpec references in owner-formal then method-formal order, or
  in intrinsic registration order. The argument range is receiver-first,
  then signature parameter order; tags are `1=FrozenValueNode`,
  `2=LogicalInputView`, `3=VacantInitDestination`, and
  `4=ConstantExecutionResult`. Tag 3 has a zero reference; the others have a
  nonzero tag-selected reference. Tag 4 refers to a distinct nested request
  with matching return TypeSpec/profile, by-value use only, and an acyclic
  request graph. A target-dependent persisted request names its concrete
  `TargetProfile`.
* `ConstantExecutionDependency` at section `0x0044` uses an 8-byte row
  `u8 kind, u8 flags=0, u16 reserved=0, u32 targetRef`. Closed tags are
  `1=MethodDef`, `2=Constraint`, `3=InterfaceWitness`,
  `4=WitnessProjection`, `5=IntrinsicRegistration`, `6=ImmutableData`,
  `7=FieldDef`, `8=PropertyDef`, and `9=UnionCaseDef`; each nonzero typed
  reference targets the table selected by its tag. The request owns a
  canonically sorted, duplicate-free set of direct dependencies actually
  reached. It omits its selected source, nested executions referenced by
  argument rows and target-layout dependencies, which have separate owners.
* `TargetLayoutDependency` at section `0x0045` uses a fixed 20-byte row
  `u8 queryKind, u8 flags=0, u16 reserved=0, u32 subjectTypeSpec,
  u32 selectedTargetProfileRef, u32 fieldFirst, u32 fieldCount`. Query tags
  are `1=SizeOf`, `2=AlignOf`, `3=OffsetOfTypePath`, and
  `4=OffsetOfPlace`. The row retains the complete normalized subject type,
  exact TargetProfile and an ordered field path only for offset queries;
  SizeOf/AlignOf have an empty path. Offset ranges index section `0x004A`,
  `TargetLayoutDependencyField`, whose fixed four-byte rows are FieldDef
  DefIds (`0x14`) or FieldDef Imports (`0x1C`). Each occurrence exclusively
  owns its nonempty ordered range. Expand FieldMap/FieldPath paths to these
  global references; do not persist Body-map or FieldPath-row indexes here.
  It stores no computed result, byte offset or duplicated Layout rows.
* `TargetProfile` at section `0x0048` is the 8-byte identity row
  `u32 profileId, u32 ruleRevision`. The assigned pair `(1, 1)` identifies
  the exact `windows-x64-native/1` layout, value-representation and applicable
  ABI contract; it does not duplicate computed Layout results. The broad
  `portable-semantic` target requirement is not a concrete target profile.
* `LogicalInputView` at section `0x0046` uses a fixed 20-byte row
  `u8 backingKind, u8 flags=0, u16 reserved=0, u32 backingRef,
  u32 referentTypeSpec, u32 pathFirst, u32 pathCount`. Backing tags are
  `1=FrozenValueNode`, `2=ImmutableData`, and
  `3=ConstantExecutionResult`; each view exclusively owns its ordered path.
  `LogicalViewPathEntry` at `0x0047` uses a fixed 24-byte row
  `u8 selectorKind, u8 flags=0, u16 reserved=0, u32 selectorRef,
  u64 indexOrRangeStart, u64 rangeCount`. Selector tags are `1=FieldDef`,
  `2=UnionCaseField`, `3=TupleOrdinal`, `4=ElementIndex`, and
  `5=ElementRange`. Definition selectors use a typed reference and zero
  numeric fields; ordinal/index selectors use zero reference and zero
  `rangeCount`; a range selector has zero reference and is terminal. Paths
  preserve effective access at each reference indirection. Physical
  placement is derived from validated target Layout.
* `TargetValueBinding` at section `0x0049` uses a fixed 16-byte row
  `u32 frozenValueRootRef, u32 targetProfileRef, u32 layoutDependencyFirst,
  u32 layoutDependencyCount`. The root supplies the result TypeSpec. The
  dependency range is exclusively owned; every entry names the same target
  profile. An empty range is valid for a target-dependent value that required
  no layout query. A profile/dependency mismatch rejects use unless a
  separately selected ConstantExecution exists for the exact requested
  profile; this binding contains no fallback recipe.
* Frozen values use one recursive typed-value family. The approved
  24-byte `FrozenValueNode` is
  `u32 typeSpec, u8 kind, u8 flags=0, u16 reserved=0, u32 scalarBlobRef?,
  u32 childFirst, u32 childCount, u32 activeUnionCaseDef?`; closed kinds are
  `1=scalar/enum`, `2=nominal aggregate`, `3=tuple`,
  `4=optional absent`, `5=optional present`, and `6=nominal union`.
  Its approved 12-byte `FrozenValueChild` is
  `u32 slotKind, u32 slotRef, u32 childNodeRef`; slot kinds are
  `1=FieldDef`, `2=UnionCaseField`, and `3=positional ordinal`.
  Values are strict trees: a non-root node has one parent, and cycles or
  shared child nodes are invalid. Union definitions use separate first-class
  `UnionCaseDef` and `UnionCaseField` identities, with approved 20-byte and
  16-byte row shapes respectively; frozen union values name the exact active
  case and carry its typed payload slots.

Keep executed dependencies, per-observation read-footprint dependencies,
per-query layout dependencies, ordinary backing/retention dependencies, and
static-provider `StaticDependency` records separate, each with its own owner.
Layout dependencies belong to the `ConstantExecution` occurrence that
performs the query; do not globally intern by TypeSpec and target or combine
these families in a general cross-proposal dependency graph. The callable's
allowed-root contract and each observation's actual footprint are distinct,
and an observation may conservatively use the complete allowed set but may
not omit a dependency. Persist stable typed identities, not analysis revision
tokens. Ordinary field placement derives from validated target `Layout`;
explicit fixups are reserved for genuine address relocations, not duplicated
field offsets. The value domain is acyclic and excludes managed identity
graphs and native resources. Required forms are not flags, and constant
execution does not introduce a second instruction language.
Required source-independent target evaluation retains the typed operation
that could not yet resolve, its exact subject/target dependency and any
admitted `ConstEval` program needed at JIT/AOT compile time. Work already
completed and frozen during Terrum compilation is not replayed, and its body
need not be retained solely for that value. Consumers validate these records
and the selected checked Body structurally and for the support needed for
safe use; the producer is responsible for semantic truth, which consumers
need not re-prove. They do not prove external provider behavior or authored
unsafe promises.
Node/operand-kind values, remaining per-kind operand schemas, exact
typed-reference encodings, intrinsic recipe schema, open Generic/static
definition identities and their reference mappings remain coordinated
schema gates. Assigned section/Feature identities do not make incomplete
registrations executable.

Input-state reads remain alias-sensitive. Records preserve relevant storage/
view extents, backing, lifetime and any checked disjointness obligation;
distinct root addresses or readonly modes do not establish separation.
Initialization outputs are logical result states/values, not a write to an
assumed live input, and preserve once-only initialization, overlap,
publication, copying/transfer and cleanup requirements.

The existing bounded Effects clause is not a carrier for these forms.
It carries no no-failure promise and cannot encode valid-input pure-form
support; adding Pure/ConstEval promises to a generic optional attribute row
does not make mandatory semantics ignorable.
An implementation claiming support preserves supported record/version
semantics, exact references and compatibility. Absent or invalid mandatory
semantics are rejected before affected use.
The NoAlias record retains its exact annotated parameter ordinal, selected
form's accessed mutable data/state regions, invocation extent and immutable
exemptions. Named mapping, generic substitution, accessor/qualified-self
adaptation and parameter forwarding preserve those identities/regions.
It is not a TypeSpec qualifier, unused Param flag, new Effects mask bit or
private-body assumption. Unsupported required NoAlias semantics reject before
affected use. Physical predicate/effect/NoAlias/program records and value codecs
remain separately gated.
