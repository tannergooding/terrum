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
claim support; readers must not silently lower the permissions to existing
visibility or infer them from filenames.

## Nominal and qualified-self witnesses: encoding pending

The [source witness contract](../language/interface-witnesses.md) requires
records beyond the current bounded TypeDef, MethodDef, and Constraint rows.
A complete logical witness identifies its closed implementing type,
interface instance, self formal, inherited declaring-member identities,
selected targets/defaults, associated bindings, and published contracts.
Source names, physical slots, and matching signatures cannot replace it.

A retained qualified-self projection additionally identifies its original
witness, complete requested self/interface, rule version, and checked
argument/receiver/result adaptations. Its inherited views preserve their
substituted identities and selected mappings. It cannot silently retarget
ordinary arguments, drop unit positions, change an initialization destination,
upgrade result authority, or choose a new associated/default binding.
Dependencies, effects, transfers, roots, and compatibility requirements
remain part of the projected obligation.

An independent consumer validates those complete requirements and supported
rules before using the projection. Trust in producer checking does not
waive bounded decoding, references, contract compatibility, or feature
admission. A replacement must preserve retained projected contracts, not
only the original unqualified witness. Linkers/JITs do not recreate source
binding from private implementation bodies.

No existing reserved table kind, Constraint witness word, contract clause,
flag, or opcode is assigned this meaning. The physical encoding and
dispatch ABI remain separate work; an artifact needing these semantics
must use a complete supported required representation, not optional data
that an unaware reader could ignore.

## Static initialization and load scope: encoding pending

The [source static-storage contract](../grammar-spec.md#static-initialization-and-load-scoped-storage)
requires semantic records beyond the current `(1, 0)` design. Existing
FieldDef, MethodDef and PropertyDef flags do not encode static storage,
initialization units, constructor trigger policy or lazy state. Source
approval assigns no additional bits, table kinds, operation tags or revision.
A static field has a distinct logical definition identity from an instance
`FieldDef`; it is not a member of instance layout. Its physical table or
section mapping remains open.

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
is not a byte flag granting safe initialized-state facts. An effect-bounded
consumer cannot ignore a producer's initialization obligations merely because
the requested member is a scalar field or an otherwise simple static method.
The [failed-unit contract](../runtime/standard-failures.md#5-static-and-lazy-initialization-failure)
retains the original escaping event in the initializing flow, not in shared
unit storage. Waiting/later demands construct fresh InitializationFailedException
events; cyclic demand uses InitializationCycleException. State/operation
records must preserve that distinction, actual acquired-state cleanup and
load identity without an implicit shared captured-event cache.

Unknown required initialization, sharing or lifetime semantics reject before
binding/execution; readers must not reinterpret these declarations as ordinary
instance fields/methods, silently skip policy attributes, or substitute the
host's static-constructor timing and unload behavior. Concrete schemas and
runtime state/ABI remain separate specification work.

## Local failure eligibility and catch inputs: encoding pending

[Local failure contracts](../language/local-failures.md) require the resolved
Exception schema and primary declaration or exact closed category-witness
route, complete payload/subject forms and property constraints. Same-named
attributes, source/debug names, equal layout and guessed inheritance are not
those facts. They also require explicit reference/value/discard catch modes,
checked Copy only where a value binding uses it, initialized input scopes,
source-order filters and exact active event/owner/provenance/dependency states.

The propagation owner remains separate from a borrowed descriptor or payload
copy. False/filter/body/rethrow exits preserve the input's actual retirement,
causal and surviving backing obligations. Bare interfaces can be category
test subjects without becoming storable handler values; no implicit box or
reference covariance supplies missing representation.

Complete physical attribute/category/witness, payload/value, operation-site,
handler/input/cleanup and captured-state/cause records remain unassigned.
This logical selection allocates no Feature ID, Attr tag, TypeSpec shape,
opcode, exception region or target selector. runtimePolicy and inspectable
interface/constraint rows alone do not enable source-free local failure use.

## Runtime requirement construction and defaults: encoding pending

[Requirement failures](../language/requirement-failures.md) require resolved
primary RequireException/type recipes, exact Core fallback or explicit site
selection and the original semantic check owner/phase. Their ordinary `new E()`
binding retains complete types, constructor identity/signature/constraints,
actually selected defaults/params, access principals, effects and successful
initialization versus actual construction failure.

False-path construction and fresh propagation cannot be inferred from a
source name, current body, default scalar bits or an unqualified runtimePolicy.
Preserve predicate once-only execution, actual output states, supporting-scope
owners, partial cleanup, per-site payload/event/dependency responsibility and
async/producer failure channels through lowering, binding and replacement.
Source owner survives relocation even when a check executes in generated code.

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
catch-input extents, source/destination owner vacancy, same-event consuming
throw, causal replacement, phase, effects/resources, nonthrowing/nonsuspending
retirement and all source/root/runtime/domain/code dependencies.
Independent use cannot infer those programs from equal owner bits, a private
Capture body, ordinary Exception eligibility or inspectable table rows.
Missing required support declines before affected transfer or execution,
without broadening trusted selective readers into whole-Body scanners.

Complete registration/context/owner/value/operation/handler/cause/phase/
cleanup records remain unassigned. This logical selection allocates no
Feature ID, Attr tag, TypeSpec, intrinsic wire ID, opcode, exception region
or native export/ABI. The bounded profile does not execute this contract
merely because runtimePolicy or a reserved exception band exists.

## Scalar failure catalog: encoding pending

[Core scalar failures](../language/scalar-failures.md) require exact selected
payload/constructor identities, complete numeric/count domain and source
failure site, target width, no-result state, policy/effects/phase, event
publication and complete propagation/cause/cleanup dependencies.
Zero-data payloads still require nominal value identity and one responsible
event; they are not void or an integer failure code. A native helper address,
debug source span, scalar opcode or raw runtimePolicy cannot supply those
records or silently remap shift-count failure to conversion overflow.

Complete registration/value/operation-state/event/handler/phase/transport
codecs remain unassigned. This logical catalog changes no Feature ID, Attr
tag, TypeSpec, intrinsic wire ID, opcode or exception-region layout and does
not activate checked propagation in the bounded fail-fast physical profile.

## Numeric selection and bounds failures: encoding pending

[Numeric selection](../language/numeric-selection.md) requires exact owning
Min/Max/Clamp identities, same-type value formals/results and target width;
complete NaN/zero/cohort selection observations; Clamp's published min <= max
predicate, explicit Core InvalidBoundsException and ordinary constructor;
semantic precondition origin, argument/result state, effects/phases and the
full actual value/program/helper/runtime/propagation closure.

Machine comparisons/selects, equal zero-data payload layouts, a helper address
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
Core interface/self/formal/member identities, Copy constraints, complete
primitive instances, inherited member deduplication, selected implementations
and published predicate/effect/phase/constant dependencies.
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
zero-input constructors and nominal zero-data value identities.
Retain each failed semantic operation/domain, policy, state/output facts,
source owner/phase, actual construction/transport and complete event/cause/
cleanup/load/runtime dependencies.
Recoverable allocation needs actual recoverable-publication support;
a runtimePolicy flag or zero-data payload cannot establish it.

Static/lazy records distinguish running/complete/failed state, cycle demand,
original initialization escape and fresh failed-unit-access construction.
They neither copy an affine original event nor infer safe retry/suppression.
False ensure remains distinct from actual predicate failure and static proof.
Invalid original indices are checked before narrowing/address formation.
No Feature ID, Attr tag, exception region, opcode, payload ABI or state/handler
codec is assigned by these logical selections.

## Pattern evidence and handler selection: encoding pending

The [source pattern and catch contracts](../grammar-spec.md#33-statements-and-local-declarations)
use uniform concrete/generic type matching and incremental post-unwind handler
selection. They require semantic records beyond the current bounded encoding;
no new operation tags, flags, row layouts or artifact revision are assigned.

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
representation. An explicitly normalized `ref<E>` catch input instead borrows
the runtime-owned E payload under its own input contract; it is not a pattern
that silently forms an extra reference.

Handler records must preserve candidate scopes, source-order catch tests,
filter bodies, live failure payload/dependencies and inner cleanup boundaries,
including the local chapter's explicit borrowed versus Copy input obligations.
Transport preserves pending failure and causal replacement while scopes retire.
An imported contract cannot substitute host pre-unwind filter timing, swallow
filter failures or replay cleanup. Source consumers check these requirements;
unsupported retained semantics reject rather than selecting another pattern
or silently accepting a different exception ABI. Concrete witness/transport
encoding and target runtime integration remain specification work.

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

The consumer checks the record's supported rule/schema versions, authority,
signature, input and value recipes, and required constant body or admitted
intrinsic before using it. `[ConstEval]` alone does not supply an encodable
body, target codec or resource recipe. Unsupported required semantics reject;
optional attribute data or a matching runtime parser is not a fallback.
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
capture types/modes and fragment graph, including selected formatter witnesses
and required writer/member identities. Equal closed shapes within that module
share one logical generated type; captured values and literal text remain
instance data. Linking preserves this key and does not merge types across
modules or by layout/output. Actual copy/transfer/backing/retention obligations
remain independent of shared type identity. Independent consumers do not
reconstruct these from a parameter name, source coordinate or private body.
The parameter annotation is not itself a value codec or a new type carrier.
Lowering to ordinary explicit operations must preserve the same order and
dependencies. This is not a deferred source program, resumable enumerator,
new ITextContent identity or construction-policy carrier.

Consumers reject unsupported required recipe/protocol semantics before
affected use. Optional attribute data, an existing TypeSpec name or a held
slot cannot silently carry mandatory behavior. Physical recipe/construction/
TextTarget records, physical operation/capture/lending representations and
generated producer representations remain separately gated; these logical approvals
activate no allocation.

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

Consumers check supported rules/operations, complete relationships,
availability, access, output and backing/cleanup contracts before affected
use. Semantic validity follows explicit producer/audit trust, not a declaration
name or artifact flag. Missing, malformed, ambiguous or incompatible requirements
reject without Current discovery, a default item, hidden adapter or fallback.
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

Consumers require supported completion/output/reservation and source/close
relationships before affected use. Remapping preserves exact nominal, member,
operation, destination/formal and phase identities independently of physical
rows/task addresses. Replacement cannot shift publication to invocation-return,
weaken capture/cleanup/close promises or substitute unrelated completion.
Semantic truth still follows explicit producer/audit trust, not a record flag.
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

Consumers validate supported complete relationships and explicit semantic
producer/audit trust before use. Remapping preserves role/member/formal/
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
Async NoThrow covers callee establishment and operation completion on the
valid domain; an invocation-only non-async promise does not cover returned
work without an explicit completion contract.

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
version. Source Pure/ConstEval/NoThrow/NoAlias does not imply corresponding
later producer operation behavior. Additional generated slot guarantees are
checked; they cannot narrow the inherited caller domain.

Linking/remapping preserves type/callable/formal/member/source/phase/cleanup
identities and outstanding obligations independently of rows/debug names.
Replacement preserves relied-on capture, lifecycle, effect, output, failure
and complete frame-layout/ABI contracts. Equal bodies/layout or generated
origin cannot make a named cursor representation-independent.
Consumers reject unsupported required semantics before affected use and apply
explicit semantic producer/audit trust to body facts.
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

Required capability/condition/protocol/profile facts cannot be omitted under
producer trust or substituted by optional Attr data. Source checking or an
explicit semantic producer/audit trust boundary supplies implementation truth,
not the annotation. Untrusted unsupported algorithms do not acquire admission.
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

Consumers reject missing required field/cleanup facts or unsupported
partial-state/call/root profiles before use. Arbitrary implementation truth
needs checked evidence or explicit semantic producer/audit trust.
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
or automatic error handling. Broader observer/awaiter/supervisor APIs remain
separately admitted.

Published records retain the exact Core family/code owner and normalized T,
ownership and wait roles, status/observation/payload contracts, failure/effect
phases, every-exit settlement/transfer and complete source/capture/output
requirements. Invocation binds those declarations to actual operation/owner/
value/output versions. Remapping row or debug-name identities cannot duplicate
responsibility, erase a payload or republish an output after reuse.

Every owner extent ends only by valid settlement or explicit responsibility
transfer, including exceptional call/staging/holder transitions. Residual
destruction is nonsuspending/nonthrowing; hidden blocking, async joining,
cancellation or supervision cannot stand in for missing obligations.
Replacement preserves those choices and relied-on capture, failure, output,
affinity and complete task/wait/root representation contracts.

Consumers reject unsupported required rules, missing/malformed owner or
observation/output facts and unsupported task/wait profiles before use.
Body truth needs checked evidence or explicit semantic producer/audit trust.
The logical contract allocates no task/awaiter/continuation/root record,
table, feature bit, attribute field or opcode. Physical codecs, callback and
scheduling substrates and broader task/cancellation catalogs remain gated;
optional Attr data and held ranges do not acquire mandatory task meanings.

## General dependency contracts: encoding pending

[Dependencies and views](../language/dependencies-and-views.md) selects logical
`dependencies-and-views/1` and `dependency-source-schemas/1`.
DependsOn, Captures and ForwardsBacking retain whole receiver/formal identities,
result/initialized-output or capture destinations, broad/backing-only source
permissions and checked published member/witness conditions. This allocates
no physical Dependency kind, table, Attr field, type form, feature bit or opcode.

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
Backing-only excludes source-storage borrowing but does not prove advance/
close survival, immutable contents, NoAlias or sharing/exclusive authority.

Witness records preserve the broad requirement and separately checked
published implementation fact. Member-contract conditions resolve an exact
unambiguous requirement and its mapped source/target identities; generic use
must establish them. Bodies, specialization, equal addresses, pinning and
devirtualization cannot silently strengthen retained contracts.
Compatible replacement cannot add dependencies/capture/invalidation, weaken
backing-only permission or change relied-on target phases, authority,
ownership, layout/ABI or capability bounds.

Consumers reject unknown required rules, malformed/missing relationships,
unavailable source/capability facts and unsupported stored/erased/crossing
representations before affected use. Body truth follows explicit semantic
producer/audit trust or checked evidence, not the attribute itself.
Physical source/capture/refinement records, view/anchor/root programs and
codecs remain separately admitted. The candidate 01/02/03 relationship tags
in the [reference-call proposal](../../proposals/reference-call-metadata.md#1-which-dependency-relations-belong-in-the-first-callable-profile)
remain unassigned; logical approval cannot activate held ranges or optional
attribute data as mandatory semantics.

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

Admission validates required shapes, references, domain/carrier compatibility
and valid value representations before using the argument. Semantic validity
facts follow the existing explicit producer/audit trust policy, not an
artifact flag or implicit permission to skip malformed data.
Unknown required semantics or missing/incompatible obligations reject;
there is no erasure to a scalar argument, raw-bit wrapper fabrication or
runtime-evaluation fallback. Linking/remapping preserves nominal identities,
canonical values, target and validity dependencies independently of physical
row reassignment.

Physical generic/value/validity records remain separately gated. This logical
approval cannot reinterpret optional Attr data or an unassigned slot as
mandatory constant-generic semantics.

## Registered query contracts: encoding pending

The [registered-query contract](../language/registered-queries.md) selects
logical `registered-queries/1`, including Core SizeOf/AlignmentOf value-layout
APIs, without assigning an intrinsic ID, table/section, TypeSpec/value form,
attribute, clause, feature bit or opcode. Source names and existing scalar
constant bits are not query/dependency records.

Retain the exact designated Core type/member identities, type-formal ordinal,
checked Sized requirement, empty value-input list, exact nuint result,
runtime/pure/constant forms and intrinsic action/storage recipe. A use retains
its complete normalized type/representation/access and available type evidence,
selected target ABI/width, coherent layout facts and their transitive
commitment or explicit layout-coupling identities/revisions.
A retained result/derived observation records its typed scalar value and
fixed-versus-rebuild/rebind/invalidation basis; the result codec does not
freeze its queried subject.

Consumers validate supported required semantics, shapes/references, target
and contract compatibility, representable size and positive alignment before
affected use. Existing producer/audit trust policy governs semantic facts,
not a same-named library declaration or artifact flag. Malformed/missing
records, unresolved evidence and unknown required rules reject; there is no
host-layout, default-value or runtime-constant fallback.

Linking, trimming and remapping preserve exact member/type/target/dependency
identities independently of row indexes, source/debug names or physical
packing. Representation replacement cannot retain stale coupled observations
without regeneration/rebinding/invalidation, nor violate published fixed
observations. Runtime descriptor use and code must agree on the selected
value representation. Queries do not invent field access, object-payload
reflection or arbitrary value codecs.

Physical query/layout/dependency records remain separately gated. Optional
Attr data, a held slot or name-based call cannot silently carry these required
semantics. Logical selection alone makes no physical interoperability claim.

## Published predicates and evaluation forms: encoding pending

The [published-predicate and effect contract](../language/published-predicates-and-effects.md)
defines logical `pure-evaluation/1`, `effect-footprints/1` and
`published-predicates/1`, plus `noalias-parameters/1` requirements, not new tables, clauses, effect-mask
bits, opcodes or a format revision.
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
and replacement preserve the required forms and bounds.

The resolved Core Pure/ConstEval schemas and evaluation-mode properties
retain their exact identities, argument/placement/multiplicity policy and
mode recipes. Constant mode implies pure mode; annotations do not turn
every ordinary invocation into either mode. A required call additionally
needs its actual admitted body/intrinsic, value recipe and dependencies.
No source query gains an opcode or bypasses an unavailable persistent
representation merely because a private compiler can evaluate it.

Input-state reads remain alias-sensitive. Records preserve relevant storage/
view extents, backing, lifetime and any checked disjointness obligation;
distinct root addresses or readonly modes do not establish separation.
Initialization outputs are logical result states/values, not a write to an
assumed live input, and preserve once-only initialization, overlap,
publication, copying/transfer and cleanup requirements.

The existing bounded Effects clause is not a carrier for these forms.
Its unconditional NoThrow bit cannot encode valid-input pure-form support,
and adding Pure/ConstEval promises to a generic optional attribute row does
not make mandatory semantics ignorable.
Consumers validate supported records/versions, exact references and
compatibility before use; absent mandatory semantics reject.
The NoAlias record retains its exact annotated parameter ordinal, selected
form's accessed mutable data/state regions, invocation extent and immutable
exemptions. Named mapping, generic substitution, accessor/qualified-self
adaptation and parameter forwarding preserve those identities/regions.
It is not a TypeSpec qualifier, unused Param flag, new Effects mask bit or
private-body assumption. Unsupported required NoAlias semantics reject before
affected use. Physical predicate/effect/NoAlias/program records and value codecs
remain separately gated.
