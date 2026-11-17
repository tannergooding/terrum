# Aggregate constants for ordinary parameter defaults

> [!IMPORTANT]
> **Entire candidate unapproved; provisional direction only under the
> user-authorized missing-contract workflow.** Permission to continue
> implementation provisionally is not formal acceptance or stability.
> This document changes no accepted specification, existing proposal,
> registry, implementation or build policy. It assigns no instruction,
> metadata record, schema identity or runtime default-argument mechanism.

## Problem and recommendation

Ordinary aggregate defaults are already accepted, subject to the ordinary
constant-value requirements. The missing decision is not whether to admit
their syntax or replace their constant domain with the current scalar subset.
It is the concrete bridge from a successfully evaluated, complete aggregate
to a reusable frozen value and then to an ordinary caller argument.

Recommend **typed structural freezing followed by value materialization**:

* Retain a complete immutable aggregate value, with its exact nominal type,
  typed children and surviving dependencies. Do not retain an executable
  constructor/store program as though it were the completed value.
* Make the source/private constant-value contract concrete for finite inline
  Copy products with committed Sequential or Transparent representation,
  including nested products and zero-data children. The selected target must
  supply all leaf encodings and complete layout.
* Materialize that already formed value into private supporting storage using
  checked structural initialization and existing typed stores/loads and call
  staging. This is copying a value, not invoking its formation program.
* Add no aggregate opcode, runtime constructor/thunk, implicit zero aggregate
  or Standard TRRM aggregate codec. Existing operations suffice for supported
  nonempty product materialization; empty-value completion needs the explicit
  logical rule below, not an invented interpretation of an existing encoding.

An immutable structural value is a required **semantic fact**, not a mandate
for a particular public bound-node class. An implementation may use a private
typed value object or an equivalent checked normal form. It must preserve the
same distinction between formation and materialization throughout the compiler.

### Established contracts and inspected evidence

The [specification index](../specs/README.md) reaches these governing owners:

* [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments)
  admits `ParameterType Identifier [= ConstantExpression]` and explicitly
  includes eligible nominal aggregates among publishable compile-time Copy
  defaults. [Calls](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms)
  selects the declaration's published value for an omission.
  [Constant generics](../specs/language/constant-generics.md#formation-binding-and-forwarding)
  expressly preserves ordinary aggregate/parameter defaults' wider domain.
* [Constant execution](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain)
  already requires complete state, validity, an admitted target constant-value
  encoding and transitive Blittable commitment for an emitted aggregate.
  [Frozen results](../specs/language/proof-and-constant-evaluation.md#deterministic-nans-and-frozen-results)
  are not producer replay. Copy or layout equality alone supplies no codec.
* The existing [frozen-value family](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
  already defines typed nominal-aggregate/tuple/optional/union nodes, typed
  children and strict-tree ownership. There is **no missing record identity
  to allocate here**. Per-kind value codecs and executable admission remain
  separately gated.
* The [instruction model](../specs/il-instruction-set.md#names-values-and-storage)
  permits aggregate logical values and
  [structural stores](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement),
  but still leaves a dedicated aggregate-construction operation unassigned.
  The [aggregate-local layout proposal](standard-trrm-local-aggregate-layout-execution.md)
  covers ordinary checked construction, not general aggregate constants or
  default publication. Its empty-Sequential admission is not selected here.
* In the current source, [`BindParameterDefaultExpression` and
  `EvaluateParameterDefaultValue`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)
  gate through `IsConstEvalValueType` and return `BoundConstant`.
  [`Binder.ConstEval`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs)
  uses scalar/Transparent-carrier values and `BoundConstant` slots;
  [`BoundConstant` and `BoundInterfaceParameter.DefaultValue`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  likewise carry scalar payloads, not an aggregate constant value.
  [`TrRmFrozenValueNodeTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs)
  distinguishes general row framing from its selected scalar payload reader.
  Existing [parameter-default tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ParameterDefaultBindingTests.cs)
  exercise adjacent scalar formation, mapping and requirement ownership;
  they do not establish aggregate support. These are inspected facts, not
  newly executed qualification.

Adding aggregate evaluator storage, traversal and lowering is implementation
work. The proposed contract delta is the product-value admission,
reconstitution authority and result-accounting rule below. It completes a
conditional constant-value contract; it does not reapprove ordinary defaults.

## Scope and alternatives

The concrete candidate covers closed values of inline nominal Copy products,
with already admitted scalar/nominal leaves and recursively eligible products.
It is independent of member names, APIs, field count and constructor control
flow. It also supplies the frozen-result side of a dependent default once
formation becomes possible; it does not decide when an open default may form.

Tuples, optionals, tagged sums, raw overlapping Unions, arrays and
backing-bearing values keep their existing contracts and separate admission
gates. Their general accepted constant domain is not removed. In particular,
Windows x64's raw Union layout is not a tagged sum or active-member contract;
its overlapping fields cannot be serialized as independent product children.
This candidate supplies no missing tag, array constant codec, relocation or
published-backing contract.

| Route | Assessment |
|---|---|
| **Typed immutable value, then existing structural materialization** | **Recommended.** Fits the existing frozen-value semantics, preserves declaration ownership and works without a new runtime instruction. A recursive representation handles unrelated and nested products without constructor-pattern recognition. |
| Freeze directly to a checked field-store normal form | Equivalent only if this is immutable typed data with complete value/dependency validation and no arbitrary executable actions. A backend can use it, but treating an ordinary constructor Body as that normal form loses the formation/materialization boundary. |
| Dedicated aggregate-constant instruction or new frozen-value family | Potential later compact interchange/lowering design, not needed for source/private products. Existing frozen-node identities already cover the logical family; another instruction or record does not establish validity, backing or target admission. |
| Runtime constructor, default thunk or lazy static initialization | Not equivalent. Repeats computation, checks and possibly ordinary-form effects/failures; changes selection and runtime dependencies. Not selected. |
| Untyped object bytes, zero-fill or a scalar-sized surrogate | Loses nominal identity, logical initialization, padding distinctions and nested validity. Size, Copy and Blittable are not sufficient justification. Not selected. |

## Proposed value and formation contract

### Declaration checking and timing

Bind a default in its declaration's lexical, access, type and constant
environment. Bind its selected operations once, including conversions,
constructor/witness identities, source arithmetic policy and evaluation-form
requirements. An explicit argument suppresses that omission's use, not
checking an invalid declared default. Do not borrow the caller's locals,
receiver value, ordinary parameter values or ambient runtime state.

When all required generic and target inputs are fixed, execute required
constant work at the existing compile-time stage and freeze only after
successful completion, cleanup and final validation. Preserve constructor
phases, field initialization, branches, helpers, checks, pre/postconditions
and actual acquisition/retirement order during formation. Do not require a
syntactically simple constructor or infer eligibility from a returned shape.
Private evaluator slots may be vacant or partial; the published result may
not be. No incomplete parent's complete-value Cleanup is manufactured.

Fixed work forms during Terrum compilation. Genuinely target-dependent work
can remain required work for an admitted target JIT/AOT compilation stage,
but must finish before affected execution. A frozen value does not initialize
runtime static storage or discharge a runtime initialization dependency.
Neither `[Pure]` alone nor one successful ConstEval specialization supplies
an absent formation contract.

### Structural product values

Use the existing typed-tree meaning, without assigning another representation:

| Component | Required meaning |
|---|---|
| Root type | Exact normalized complete type: definition/Module identity, qualifications, ordered type/constant actuals and their domains. A Transparent nominal type remains nominal; it is not its carrier or an alias. |
| Product children | Exactly the stored instance-field vector, identified by resolved declaration identity in declaration order. Include implicit property backing where present and every logical zero-data field. Exclude statics, computed properties and indexers. Names, offsets and equal payload widths do not identify a child. |
| Child value | Complete, live, correctly typed and valid under the field's substituted complete type. Recurse for aggregate children. No omitted field, duplicate slot, extra slot, partial child, implicit default, cycle or borrowed evaluator region is permitted. |
| Copy and validity | Establish Copy from the actual published type contract for the complete type and children. No owning Cleanup/duplicated responsibility is permitted by Copy. Formation establishes nominal construction/validation obligations, not merely valid scalar bits. |
| Target representation | Require the selected target's transitive Blittable commitment, complete product layout and each leaf's actual constant-value encoding. Sequential field placement and Transparent carrier equivalence do not independently establish nominal validity. |
| Dependencies | Retain complete value/type/layout/target and publication/backing obligations that survive freezing. Keep formation-request dependencies distinct from the completed value's surviving requirements; a private helper need not remain solely for replay. |

Nested aggregates are children, not flattened equal-sized carriers. Physical
sharing or interning inside a compiler must not create source identity,
mutable sharing or a second lifetime. It also does not relax the existing
Standard frozen-value family's strict-tree rules if a future codec uses it.
No new source equality, hashing or aggregate proof principle follows.

An instance-field-free product has a valid empty child vector **only after
successful eligible formation and nominal validation**. That is a complete
typed value, not vacant storage. A product with void fields is not field-free:
each such field still has a logical initialized child. Padding, an allocated
minimum byte and all-zero bits establish neither case.

### Product target encoding and backing

For source/private product constants, propose a structural value encoding:
the typed tree is authoritative; its value payload consists of the existing
leaf payloads in recursive declaration order. Products add no payload bytes;
unit/empty products add none. Each leaf keeps its selected exact encoding.
The checked sum of leaf payload lengths is the encoded-result payload length
for constant-execution accounting. Type/field identities and transport
framing are structural data, not inferred from that payload or charged as
machine-code bytes. This rule specifies no Standard row or Blob encoding.

Target materialization places those leaves at the already derived field
locations. Object extent, alignment, tail padding and encoded-value length
are different quantities. Do not serialize host objects or uninitialized
padding. Padding remains unspecified; choosing to clear physical scratch
padding does not publish a zero-padding promise. Leaf copies preserve signed
zero, NaN payload/signaling bits, decimal cohorts and other promised
representation observations; constant arithmetic's NaN rules apply at
formation operations, not again when copying a child.

The concrete product admission carries inline values only, with no surviving
external backing or address identity. Internal evaluator references/views
can assist formation under their existing rules but cannot escape into the
result. Blittable pointer fields or Copy reference descriptors do not thereby
have admitted constant values. A static-data-backed value or symbolic address
needs its separately admitted immutable-backing/relocation/value contract;
this proposal supplies none and never fabricates a host/process address.
An Auto temporary can be an internal evaluator value where admitted; that
does not make it an eligible emitted Auto aggregate.

### Generic, requirement and proof ownership

[D1](standard-trrm-interface-parameter-defaults.md) continues to own the
declaration's ordinary-default surface; [GD1](standard-trrm-generic-dependent-parameter-defaults.md)
separately proposes open declaration-scoped terms and their formation timing.
Neither is an aggregate codec, and neither becomes approved here.

For an open default under GD1's provisional direction, preserve the checked
term, complete result type and unresolved obligations. Substitute declaring
owner actuals first, then method formals by kind/ordinal; do not rebind names
or substitute only a carrier. The closed frozen result must have the exact
normalized parameter type after that same substitution. An open declaration
must be valid under its declared constraints; one closed success cannot
repair invalid open typing or establish universal Copy/layout commitments.
An omission supplies no generic inference evidence and does not widen
constant-formal eligibility. Closure still checks the concrete value,
representation and dependency obligations.

An interface omission belongs to the exact selected requirement instance,
member and parameter ordinal, including inherited identity and qualified-self
adaptation. Preserve its default through proof stubs, witness selection,
accessor projection and specialization. Do not substitute the implementation's
default, even if its type or bytes happen to match. Distinct requirements
remain distinct; a diamond reaching the same declaring identity shares that
view. Indexer/accessor projection must use its separately admitted ownership
mapping, not a synthesized default on the setter's assigned-value input.

Proof programs retain a checked frozen value or a checked open term and its
original owner. Closed rebinding verifies the same substituted requirement;
it does not refresh the default from the selected Body. Exact scalar children
can feed existing scalar proof rules where those rules admit the read.
Neither one constant evaluation nor an aggregate's field bits prove an
arbitrary runtime predicate, witness or all-input property.

### Failure and resources

Apply the accepted
[required-constant accounting](../specs/language/proof-and-constant-evaluation.md#portable-resource-envelope),
including its inclusive ceilings, work/depth/storage exhaustion order and
separate final validation/encoding charges. Traverse and charge all logical
roots/fields, including empty and zero-data cells; private aggregate storage
uses the existing recursive slot model. The output encoding and source value
coexist while live. Sharing, cached formation, codegen shortcuts and nested
helper calls do not erase semantic work or grant a fresh nested budget.

Missing bodies/encodings, invalid values, unsupported reached effects,
arithmetic/requirement failure, exhaustion and host cancellation/resource
failure remain distinct outcomes. None becomes absence, another overload,
a zero value, a false proof or runtime fallback. Known runtime-only actions
on unselected paths are not executed; malformed structures or unfulfilled
required constant subexpressions do not hide on those paths. Failure during
required cleanup prevents publication too. An implementation's smaller
private evaluator limits are not the portable resource guarantee.

## Caller materialization and every-exit behavior

After normal declaration/generic/overload selection and compile-time default
formation, construct the complete argument plan:

1. Capture the callable/receiver once. Stage written arguments once,
   left-to-right in source order, preserving their value snapshots, loans and
   responsible holders. Named labels map positions; they never reorder effects.
2. Supply each omitted position from that declaration view's formed value,
   in logical parameter order, before invocation checks/preparation. Required
   positions still reject when unsupplied. Keep unit positions and accessor
   mappings; do not search for the next unfilled slot.
3. Materialize a Copy value into private supporting storage where needed.
   The frozen value authorizes reconstruction of **that already valid value**,
   not arbitrary source access to private fields or a fresh nominal
   construction. Use complete typed child initialization from vacant storage;
   preserve partial states until complete, then load/pass the whole value.
4. Perform the ordinary invocation Check, Prepare, Invoke and normal Publish
   phases. Pass every logical argument explicitly. There is no default
   sentinel, hidden thunk argument, dynamic lookup or default-specific ABI.

The materializer cannot call a constructor, property setter, validator,
literal parser or helper, or rerun declaration initializers. Those actions
belong to formation. Statically validate the materialization plan's geometry,
types and completion; do not add a runtime nominal-validation failure.
Existing checked operations are sufficient only when they preserve this
semantics and the selected backend supports the complete state transition.
Register aliasing alone is not Copy or initialization.

For a field-free frozen product, the internal value/materialization plan may
establish complete logical state without payload stores, retaining its type
and already established validity. Logical void children are initialized
explicitly. This is the proposed nullary **value-materialization** rule,
not permission to treat every empty destination as initialized.
It does not broaden Standard SemanticStore's None source, contextual zero,
or the aggregate-local proposal's exact empty-Sequential encoding.
Unsupported serialized lowering must decline rather than invent that meaning.

Each invocation gets its ordinary independent by-value binding. A callee's
permitted mutation of its parameter cannot alter the frozen value or another
call's argument. Supporting storage has the actual use's extent, alignment,
scope and nonescape obligations; no public reference, static backing, pin or
lifetime extension is created. Copy itself has no owning Cleanup, but that
does not remove dependencies or responsibilities from surrounding arguments.

Preserve [construction and retirement](../specs/language/ownership-and-cleanup.md)
and ordinary call failure semantics on every exit. Later written-argument
failure prevents invocation, without reviving transferred sources or
performing hidden-destination preparation early. After invocation transfer,
the callee's existing ownership/output obligations apply; exceptional exits
publish no normal result. Formation failure preserves actual partial
construction responsibilities and the applicable cleanup order.
Do not introduce a blanket exceptional destructor pass: automatic authored
Cleanup retains its normal-retirement rule, and exceptional cleanup still
requires the existing authored `finally`/failure protocol. Defaults add no
resource acquisition, hidden heap owner, failure suppression or rollback.
Physical storage/frame failures retain the selected target/runtime policy.

## Persistence, interchange and native execution

| Path | Required consequence |
|---|---|
| Source compilation | Preserve the typed value/term through declaration binding, shared traversal/rewriting, verification, substitution, interface adapters and lowering. A scalar `BoundConstant` bit payload cannot stand in for the aggregate root. |
| Private persistence and source-free binding | Under an explicit implementation-specific format agreement, retain the selected declaration's absence/value/term, exact complete types, child identities and required dependency closure. Remap semantic references, not payload bits. A reader must safely decode, resolve and support those facts; it must not reconstruct them from source text, runtime constructors or provider defaults. Unsupported retained declarations reject, never lose their default. |
| Already compiled calls | A complete argument/materialization plan can be retained without the originating default declaration if that declaration is genuinely unretained. A format capable of those typed operations need not grow a default-surface codec solely for that call. This does not establish reusable declaration publication. |
| Standard TRRM | Continue exact-profile rejection of retained aggregate defaults not admitted by the selected complete schema/codec. D1's SF1 surface and GD1's formal/template composition do not enable aggregate frozen payloads, native-width leaves or a new Body operation. Existing frozen-node kinds and table framing are not evidence of an executable aggregate codec. |
| Native JIT/AOT | Consume the same verified completed values and argument plans. Finish required target work before affected execution. Apply existing target layout, packed access, object alignment, by-value copies, result buffers, frame/probing/unwind and code-lifetime rules. Native or private-source-free success is not Standard interchange conformance. |

Private data must distinguish completed values from deferred required work;
a completed value needs no producer Body solely to replay it. A genuinely
deferred request needs the exact admitted program, substitutions, effects,
target and resource obligations. A source-free consumer lacking those cannot
execute a fallback. Private serialization cannot hide unsupported required
semantics in an embedded Standard payload.

Source checking establishes authored formation and nominal validity.
Consumers perform the
[safe-consumption and binding checks](../specs/representation/reader-trust.md)
needed for the selected representation, not arbitrary semantic re-proof or
reexecution of the producer's constructor/invariant. A published “verified”
flag is not authorization. Expected/provider binding and replacement preserve
the original caller-selected default and every relied-on contract. A changed
provider default affects fresh source compilation, not unchanged compiled
arguments; default differences alone do not invalidate otherwise compatible
witness/signature binding.

For Windows x64, retain the
[existing call classification](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults):
ordinary aggregate sizes 1/2/4/8 use integer carriers, other positive sizes
use independent indirect copies, and Transparent uses its carrier's rules.
An incoming indirect copy's ABI alignment is not arbitrary object
over-alignment. Preserve exact packed field extents and actual alignment;
do not overread padding/adjacent fields or widen a tail access. JIT and AOT
have the same obligations; no new independently replaceable ABI is proposed.

A later Standard aggregate codec should reuse the existing frozen family
where sufficient, while explicitly completing per-kind validity, child
coverage/type rules, references/remapping, target dependencies, accounting,
reader/writer rules and declaration-surface composition. It must name one
exact profile and its complete admission closure. No such profile is selected
here: there is no alias, automatic D1/GD1/layout mix-in, equal-version guess,
old-profile retry or private-format fallback.

## Integration owners and prerequisites

These are intended amendments **if adopted**, not edits made by this proposal:

| Owner | Exact disposition |
|---|---|
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [constant-generics formation](../specs/language/constant-generics.md#formation-binding-and-forwarding) | Preserve the productions, wider ordinary-default domain, Copy requirement and generic-formal distinction. Add only a cross-reference to the completed structural constant-value contract. |
| [Constant-execution domain, normalization and accounting](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain) | Add structural product-value admission, exact child coverage, empty/zero-data validity, freeze-versus-materialize authority and product payload-length accounting. Reuse staged formation, failure/resource rules and existing limits. |
| [Calls](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms), [witness ownership](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity) | Clarify selection of the typed frozen aggregate and its pre-invocation materialization without rebinding/replay. Preserve source-order staging, generic inference, exact requirement ownership and all existing call obligations. |
| [Data-layout constant boundary](../specs/data-layout-and-abi.md#scope), [Windows x64 layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Add the concrete source/private product materialization/value-encoding contract. Preserve Layout/Blittable identities, equations, padding, alignment and access rules unchanged; do not register optional/sum/backing representations by analogy. |
| [Instruction values/storage/aggregate semantics](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement) | Explain that checked materialization of a completed value can lower to existing operations. Keep aggregate construction unassigned and ConstantMap/per-record eligibility separately gated; add no opcode or operand meaning. |
| [Frozen-value records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms), [IL metadata](../specs/il-metadata.md#section-kinds-and-row-layouts) | Preserve existing node/child identities, strict-tree ownership and pending codec boundaries. No new row, kind, schema or registry amendment is required for this source/private slice. |
| D1, GD1 and the aggregate-local layout proposal | Preserve their distinct surface, dependent-term and executable-layout scope. This proposal supplies no Standard composition and supersedes none of their wire candidates. Their implementation observations do not override current source evidence. |

Implementation prerequisites are complete declaration/type/field identity,
positive Copy and layout evidence, admitted formation bodies/forms and leaf
encoders, structural evaluator storage, mandatory bound-tree verification,
and native support for the resulting ordinary values. A private source-free
path additionally needs its explicit persistence/rebinding contract. A
Standard path additionally needs its selected aggregate codec and complete
profile. Removing a scalar gate alone satisfies none of these prerequisites.

## Required conformance cases

These are acceptance cases for the candidate, not claims about current
support or completed test runs. Use public compiler APIs and the existing
headless, supervised execution patterns; keep syntax, binding, emission,
private/source-free preservation, Standard admission and runtime evidence
separate.

| Case | Required outcome |
|---|---|
| Closed product default | A valid multi-field Copy product formed by an admitted constructor/helper becomes an omitted argument; an explicit argument bypasses its use. A valid unused declaration still forms its fixed required constant. |
| Unrelated synthetic APIs | A telemetry calibration product and an unrelated ledger header, with different names, field counts and constructor branches/helper structure, use the same path. No API name, recognized constructor sequence or motivating sample is required. |
| Nominal/access validity | Formation may use a declaration-accessible private helper/constructor; callers receive the value, not helper access. Equal-sized distinct nominal types, wrong field identities/types, invalid wrapper/rune/bool leaves and failed nominal checks reject. |
| Nested products | Several levels of Sequential/Transparent children, including implicit stored backing, retain exact child types and values. Reordered/missing/extra/duplicate children, cycles, partial nested state and nominal/carrier substitution reject. |
| Empty and zero-data boundary | Successfully formed field-free and void-field products remain distinct complete types. Test their Copy/materialization and accounting; vacant storage, minimum padding and omitted logical unit initialization do not count as a value. |
| Layout/ABI boundaries | Exercise sizes around 1/2/4/8/16, a larger product, packed misaligned children and nested explicit over-alignment. Native JIT/AOT preserve field observations and independent argument copies without page/tail overread. Layout/accounting overflow diagnoses, not truncates or changes field order. |
| Representation bits | Nested signed zero, explicit NaN payload/signaling bits and decimal cohorts survive copies exactly. Arithmetic-created NaNs follow formation-operation rules; no serialization/materialization recanonicalization occurs. Padding is not compared as value data. |
| Invalid versus separately unsupported | Non-Copy/owning defaults, invalid complete values and escaped evaluator backing reject under existing obligations. Missing optional/sum/raw-Union/array/backing or leaf codecs diagnose the actual unsupported contract, never a new blanket scalar-only source rule. |
| Mapping and evaluation order | Interleaved defaults and reordered labels preserve written receiver/argument effects exactly once in source order; constants fill their own ordinals. Duplicate/unknown labels and missing required positions reject. Accessor tests preserve admitted index/default mappings and required assigned-value inputs. |
| Interface identity and replacement | Requirement and implementation publish different aggregate defaults. Interface calls, inherited same-identity diamonds and proof closure use the requirement; direct calls use the concrete declaration. Unrelated requirements remain distinct, and replacement cannot refresh an old call's value. |
| Generic substitution and proof | Closed generic-independent defaults retain complete nested nominal actuals. Under GD1's provisional timing, owner/method type and constant actuals form the selected dependent value after substitution; omissions infer nothing. Forged proof-stub ownership, changed domains or invalid open declarations reject despite one successful instance. |
| Runtime effects and copying | An eligible formation callable with a mode-guarded ordinary effect does not run that ordinary path at omitted calls. Repeated calls and callee mutation cannot change the frozen value. An unmarked reached formation call rejects; a known runtime-only unselected branch is not executed. |
| Failure and every exit | Constructor/precondition/final-validation/required-cleanup failure publishes no default. A later explicit argument failure, invocation-check failure and callee exception preserve existing staging, ownership, authored-finally and output rules, without runtime formation or an invented exceptional Cleanup pass. |
| Resource boundaries | At and one beyond each selected work/depth/live-storage/result ceiling, include recursive aggregates, empty roots, nested calls and encoding-buffer coexistence. Enforce checked product payload-length accounting; caching and nested defaults cannot reset the budget. Cancellation is not a constant failure value. |
| Private/source-free paths | Round-trip a retained declaration and a separately lowered caller without original source; remap nested type/field/provider references, preserve original requirement values and reject missing/unsupported closure atomically. Do not require a finished value's private producer solely for replay. |
| Standard negative boundary | D1/GD1/scalar or layout-only profiles reject retained aggregate defaults lacking a selected codec, including uncalled views. Reject relabeled private artifacts and guessed compositions. An independently admitted fully lowered caller is not evidence that declaration defaults can be published. |

## Remaining decisions

1. Formal adoption of this structural product admission, nullary
   materialization and payload-accounting rule remains outstanding. These
   are the recommended concrete decisions for provisional source/private
   implementation; they are not accepted merely by writing this document.
2. Open generic-dependent timing and source-free terms remain GD1's separate
   unapproved decision. Closed products do not depend on adopting that
   extension; dependent omissions do.
3. No Standard aggregate-default codec/profile or additional aggregate/backing
   family is selected. If one is needed, complete its own value, target,
   reference and admission contract explicitly. Do not invent a wire form or
   runtime constructor to continue past that boundary.
