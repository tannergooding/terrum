# Standard register-Body initialization-destination inputs

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This is a design decision, not normative
integration, compiler support, ABI admission or qualification evidence.

## Problem and recommendation

The [assigned CallSite row](../specs/il-instruction-set.md#typed-operand-records)
only labels `sourceUse=5` as an initialization destination. The existing
[function-pointer output proposal](function-pointer-checked-output-parameters.md#initialization-capability-formation)
selects a complete descriptor input for indirect calls, but does not define
whether ordinary MethodDef CallSites share that carrier, how whole-Span
initialization formals use it, or how an omitted hidden output binds to its
formal during preparation. The [OutputState clause](../specs/representation/contract-clauses.md#5-outputstate)
publishes normal vacant/complete states; it supplies neither a call input nor
per-call destination binding.

Recommend one shared source-use-5 contract: `ref<init T>` uses its exact live
checked descriptor, `Span<init T>` uses its exact whole-range descriptor, and
an omitted output's prepared descriptor is bound to the same formal and hidden
destination. Reuse CallSite `02`, BlockEntryState `01`, PlaceSelection `04`
and DestinationPreparation `03`; a Home containing T is not a descriptor input.

The existing
[checked function-pointer output proposal](function-pointer-checked-output-parameters.md)
selects a descriptor input for indirect calls. This proposal generalizes the
same `sourceUse=5` meaning to ordinary MethodDef CallSites, covers whole-Span
formals, and binds explicitly prepared omitted outputs. It does not change the
DD candidate, function-pointer identity, selector or machine-ABI decisions;
both proposals remain unapproved.
The [runtime-publication proposal](initialization-state-runtime-publication.md)
remains the owner of its provisional caller-local witness/one-address lowering
protocol; no wire witness or additional ABI word is selected here.

### Scope and source evidence

* The call-side contract covers `ref<init T>` and `Span<init T>`, preserving
  admitted outer and inner qualifications. DD forms one-place initialization
  references, **not ranges**. A Span input must already come from an admitted
  typed descriptor producer or formal. No slice/range-formation opcode, stored
  init-capability layout, generic ABI, deferred-completion transport, root or
  pin operation is added.
* [Reference formation and reborrow](../specs/language/references.md#initialization-references),
  [whole-range state](../specs/language/initialization-and-flow.md#states-reads-and-initialization),
  [source output defaults](../specs/lifetimes.md#declaration-defaults-exceptions-and-published-compatibility)
  and [hidden-destination timing](../specs/language/calls-and-inference.md#hidden-destination-instances-and-retirement)
  are preserved, not new syntax.
* Implementation remains provisional under this exact profile. Source
  emission now includes default Boolean `OutputState` rows and bounded
  same-profile provider binding/re-emission. Current support and open gates
  are tracked in the [implementation summary](../implementation.md) and
  [conformance index](../implementation/conformance-gaps.md#cg-001); this
  evidence does not approve the proposal.

## Exact owning amendments

All changes in this table are **proposed**, not made by this document.

| Owner | Amendment |
|---|---|
| [Instruction set: typed records](../specs/il-instruction-set.md#typed-operand-records) | Add the shared input-carrier/type matrix and source-use-5 preparation specialization below. Clarify that BlockEntryState types do not assert referent vacancy. Admit an initialization-only checked descriptor as a SemanticStore destination while its exact target is vacant, without manufacturing ordinary write authority. |
| [Instruction set: general calls and phases](../specs/il-instruction-set.md#general-call-site-record) | Replace “already completed values/descriptors” only for explicitly preparation-bound omitted-output entries. Other inputs remain completed before the call. Specify captured destination/result correlation, child-loan suspension/resumption and Check → Prepare → Invoke → normal Publish. |
| [Metadata: TypeSpec/Param and Body](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Cross-reference this carrier mapping. Preserve TypeSpec `0E`, indexed Span `0C/8A`, immediate-element init qualifier `04`, Param mode `00`, complete inner qualification and existing row/token namespaces. Change no metadata payload. |
| [Contract clauses §5](../specs/representation/contract-clauses.md#5-outputstate) and [metadata example](../specs/il-metadata.md#example-predicate-correlated-output-contract) | Make the linkage from formal ordinal to the particular CallSite input, captured destination and saved normal result explicit. Preserve clause `0002`, its 12-byte payload, predicate ownership, vacant `0`/complete `1`, absence meaning and exceptional-state exclusion. |
| [Lifetimes: declaration defaults](../specs/lifetimes.md#declaration-defaults-exceptions-and-published-compatibility) | Link normalized source defaults to the Body publication contract; preserve Boolean success and other-result unconditional completion. Do not introduce an unspecified normal-output mode. |
| [Registry](../specs/representation/registry.md), specifically `instruction_encoding.record_kinds` and `instruction_encoding.record_constraints` in [the machine registry](../specs/representation/trrm-1.registry.json) | Reuse the DD flags proposed by the function-pointer output owner. Add the source-use-5 input/preparation constraints and init-store rules; retain record-header flags, record kinds, primary opcodes, widths and namespaces. |

The recommendation changes an assigned operand's validity and completes Body
semantics; it is not merely permission to decode formerly reserved bytes.
Select the implementation profile with the exact out-of-band identity
`trrm/1-draft-init-reference-bodies-1`. It does not add a serialized schema
field, change TRRM version bytes or authorize decoding from equal `(1,0)`
bytes. The artifact remains Standard TRRM and carries its ordinary per-Module
Feature closure. Checked descriptor formation and unconditional outputs add
no Feature; the six default Boolean selectors require provisional `10/1`.
General phase-3 graphs require their separate provisional `17/1` proposal
and are not admitted by this profile until that schema composition is selected.
The loader and every provider/re-emission operation must preserve this exact
profile identity; no fallback is permitted.

## Carrier, formation and preparation

### CallSite input

Keep the four-byte row `u8 selectorKind, u8 sourceUse, u16 selectorIndex`.
For `sourceUse=5`, the resolved formal uses Param mode `00` and this matrix:

| Complete formal | Selected carrier at Invoke | Authority/state |
|---|---|---|
| `ref<init T>`, with admitted outer write qualification | Live descriptor of that complete type in R0..R13 or a typed descriptor Home | One fixed exact T destination, vacant before entry; scoped child reborrow of its initialization reservation, not descriptor Copy or ownership transfer of T |
| `Span<init T>`, with admitted outer/inner qualification | Live Span descriptor of that complete type in R0..R13 or a typed descriptor Home | Pass the descriptor by value under the Span contract; every element in its captured range is vacant before entry; no independent initialization right is created by copying descriptor bits |

Both require exact normalized/substituted type agreement and supporting
authority. None, R14/R15, a Home containing T, an internal PlaceHandle,
ordinary `ref<T>`/Span<T>, a raw pointer and a NativeOutput reservation are
not these inputs. Conversely an initialization-view formal cannot be invoked
using source-use 1/2/3/4 as an alternative protocol. No implicit-reference
adaptation or legacy parameter mode supplies initialization.

Logical inputs are receiver, if present, then parameters. For formal ordinal
`i`, the row is `receiverCount+i`, where receiverCount is 0 or 1; receivers
are not OutputState parameter ordinals. Multiple outputs are independent
formal bindings, not extra ordinary result registers. Even `ref<init void>`
is not admitted by current checked-reference type validation, so this profile
does not add an addressless destination. None remains invalid for every
admitted initialization formal.

An ordinary source-use-5 input is already a live descriptor before Check.
Forwarding preserves its original destination and reservation; Invoke lends
the child view and suspends conflicting parent access. Normal child completion
updates the same destination before the parent resumes. Passing the formal's
descriptor does not initialize its descriptor slot or reborrow a nested
referent inside T. A completed/attenuated capability cannot supply a second
initialization of an already-live destination.

### One-place DD formation

Use the exact candidate DD encoding and PlaceSelection flags owned by
[checked function-pointer output](function-pointer-checked-output-parameters.md#initialization-capability-formation).
The selected place must be vacant, have `accessKind=3=Initialize` and referent
type T; outer write survives completion only when the descriptor type retains
it. Non-DD flag use, unknown bits and nonzero record-header flags reject.
Exact-schema selection remains required. DD forms one-place references only;
Span inputs must already be admitted whole-range descriptors.

### Explicit preparation-bound omitted output

The source's omitted-output contract requires old hidden values to survive
Stage and Check. Therefore do not form a supposedly vacant explicit descriptor
to a still-live hidden T, or retire T early to make DD admissible.

Specialize a CallSite-listed DestinationPreparation `03` as follows:

* `sourceUse=5`, `hasFormal=1`, `formalOrdinal=i` identifies exactly one
  source-use-5 input for an omitted **initialization-reference** formal.
  `destinationKind=2=Home`, `destinationIndex=H` selects its actual hidden
  T local: Home role 3, rebindable, exact complete referent type T. It is
  deliberately distinct from the descriptor selector in that input row,
  other output targets and the hidden ordinary-result Home.
* `priorState=1=Vacant` uses `transition=1=Initialize`;
  `priorState=2=Live` or `3=MaybeLive` uses `transition=2=Replace`.
  Prepare conditionally retires the actual old live T, in the recorded
  dependency-valid order, and leaves H vacant. “Replace” does not publish the
  new T before Invoke. Partial `4`, pending Reserved `5`, Publish `3` and
  source-use-5 Register preparation destinations reject in this specialization.
* The input's R0..R13 binding or role-3 descriptor Home is an available,
  unbound/vacant staging destination, not a value read before preparation.
  It must not overwrite a live responsibility or overlap another staged
  input/target/result binding. Once H is vacant, Prepare forms the exact
  signature-selected descriptor into that input slot, checking required
  outer write authority. This is the record's sole typed formation action,
  not an arbitrary helper program or a hidden T-to-descriptor alias.
* Exactly one preparation entry binds that input per invocation. A nonzero
  `dependencyContractRow` must be a selected-callable-owned Dependency `0004`
  row for destination kind 2, ordinal i, phase 1, applicable to that
  transition; zero does not erase other declaration dependencies.
  Other preparation flag/reserved fields stay zero.
  `formalOrdinal` remains u16: an unrepresentable required preparation cannot
  be truncated or bypassed.

The descriptor is call-protocol-owned, not retained as an uncompleted capability.
Release or transfer its temporary responsibility only under the existing
completed-view/dependency rules; clearing its staging slot cannot invalidate
a surviving ordinary loan. Published ordinary views retain their actual
backing and can prevent later retirement of H. T remains under the hidden
local's state/holder/cleanup contract. Skipping the call performs none of this
preparation and creates no new publication. An explicit destination cannot use
this form to destroy a live value. Span lifting/range creation is not implied:
the existing omitted-reference-output source contract supplies one T Home,
whereas a range producer needs its own admitted extent/formation contract.

## State, provenance, normal publication and failure

Reuse the exact caller-local witness and normal-publication protocol in
[Initialization-state runtime publication](initialization-state-runtime-publication.md),
the source relation/coverage rules in
[checked-output selectors](checked-output-outcome-selectors.md), and the
formation/forwarding meaning of `ref<init T>` in
[References](../specs/language/references.md#initialization-references).
This proposal adds only the Standard Body mapping:

* BlockEntryState records the complete descriptor type; it does not assert
  referent vacancy or restore consumed authority. Preserve the selected
  destination, actual result, backing and loan relationship at the CallSite.
* Each source-use-5 input must bind to exactly one initialization formal. Its
  descriptor is live before Check; forwarding preserves the same destination
  and reservation. A descriptor slot is distinct from the T referent and from
  a whole Span range.
* Preparation for an omitted output binds the formal to the distinct hidden T
  Home, retires only the selected prior hidden value in dependency order, forms
  the descriptor after Stage/Check, and does not Invoke if preparation fails.
  It cannot retire an explicit actual or turn a Home containing T into a
  descriptor input.
* Only normal Publish applies the declared relation to that exact destination
  and saved result. Check/Stage/Prepare failure, exceptional completion,
  postcondition failure and nonreturn have no OutputState normal guarantee;
  no rollback, fabricated false result or replayed cleanup is implied.

## Cross-layer obligations and separation

| Surface | Required obligation |
|---|---|
| Source producer / shared verifier | Check accepted formation, vacancy, nonconflicting reservations, partial settlement and declared normal exits. Carry complete types, normalized defaults, formal bindings, loan lineage and result/state relations through shared bound traversal, rewrites, generic substitution and lowering; no sample or method-name policy. |
| Standard reader / consumer | Before affected use, validate exact schema/capability support, extents/counts, selector/type/carrier combinations, DD flag ownership, preparation binding/state tuples, register availability, typed references and supported provenance/CFG reconstruction. Reject missing or inconsistent required state/loan representation and unsupported merges. Check OutputState owner/formal/predicate/state domains, duplicate keys and codec-defined structural contradictions. |
| Trust boundary | Semantic truth remains the producer's obligation under [reader trust](../specs/representation/reader-trust.md). Mandatory safe-consumption/reconstruction checks do not require re-proving a callee's authored body, arbitrary general-predicate coverage or foreign promises. False source/body facts are producer defects; a separate semantic audit may reject them. |
| Binding / provider replacement | Retain every original expected complete signature, output map and dependency/source view in its owning Module. Check the actual provider's directed satisfaction; false/complete is not interchangeable with expected false/vacant. Resolved layout or an implementation refinement cannot erase the original expectation. |
| Remapping / provider re-emission | Decode, remap and rebuild TypeSpec/Import/Method/Contract/predicate references, Body Type/Home/Method maps, formal-order inputs, preparation correlations, record/Block indexes, entry states, instruction widths/offsets/extents and affected source/debug data. Preserve distinct Home identities, captured place lineage and per-Module capability closure. Regenerate state witnesses in the destination context; never substitute matching bytes or serialize host-private flags. |
| Runtime / targets | Implement the admitted saved-result publication and actual destination writes through both JIT and AOT. Preserve descriptor/value distinctions and loan/cleanup ordering. This Body carrier assigns no general reference/Span ABI, implicit pin/root/marshalling or independently replaceable entry shape. |
| Persistence profiles | Standard register Bodies, private semantic/native persistence and Development ScalarInitOutput keep exact independent schemas and gates. ScalarInitOutput, legacy IsInit/IsOut fields, private tags and candidate NativeOutput `sourceUse=6`/DF/record `0C` are **not Standard source-use-5 fallbacks**, even for scalar T or equal machine addresses. |

### Alternatives and remaining decisions

| Alternative | Assessment |
|---|---|
| CallSite directly accepts a T PlaceHandle or payload Home, deriving a descriptor from the formal | Saves explicit DD for some fresh actuals, but needs a different interpretation for forwarded descriptors and cannot represent a whole Span range from an element place. It also risks delaying source-position reservation/checks. Not selected as a second ordinary input encoding. |
| Dedicated initialization-destination record, or replacement of record `03`/`04` with explicit call/formal/carrier/target links | Could make preparation links wider and support a separately designed range producer. It still needs guarded dynamic provenance and publication. Adds overlapping ownership/mapping machinery that the selected one-place preparation and already-typed Span inputs do not need. If selected later, reassign draft records/operands on their merits; occupied IDs are not protected and old encodings need no aliases. |
| **Existing descriptors/records plus scoped DD flags and formal-linked preparation** | Recommended: one ordinary input interpretation for fresh and forwarded checked views, explicit preparation-bound input, no new record/opcode/state namespace. Existing u16 preparation limits remain honest before-use boundaries, not a new source arity rule. |

The open approval choice is whether to adopt this recommended integrated
carrier/formation/preparation contract or replace it with the explicit-record
alternative. No operand, state value or forwarding policy is left to guess
within the recommended scope. Predicate-codec compositions, production of new
initialization ranges, deferred transport and physical ABI/target admission
remain independent prerequisites, not implicitly approved extensions.

## Required positive, negative and cross-feature cases

These are proposed acceptance cases, **not test results**.

| Family | Cases |
|---|---|
| Formation/carrier | DD `0001/0003`, descriptor Register/Home inputs and two-helper reborrow; nested descriptor/pointer T and generic qualified substitutions. Reject a T Home or PlaceHandle as ordinary input 5, None/constants, wrong mode/type, implicit adaptation, flag `0002` alone, unknown/header flags and non-DD flag use. |
| State/provenance | Whole-vacant versus live/maybe/partial/pending targets; outer-write versus inner-write attenuation; duplicate/overlapping destinations, descriptor rebinding, selected fields, expired owner/guard and later-argument invalidation. Reject double initialization, target-slot confusion, Copy/escape of uncompleted refs and rights recreated by a BlockEntryState row. |
| Defaults/CFG | Boolean true/false, non-Boolean/void unconditional outputs and explicit false/complete maps; several outputs sharing predicates, saved/copied/discarded results, negated/short-circuit branches, joins and loop reuse. Detect stale-result revival and cross-paired destination/result merges. Reject OutputState `state=2`, duplicate predicate/output keys and codec-defined conflicting or incomplete maps. Source/producer verification rejects wrong normal states; readers reject malformed rows and structurally unsupported representations without claiming arbitrary body re-proof. |
| Preparation | First, live and maybe-live repeated hidden output; skipped site; dependent old view blocking retirement; Stage/Check/retirement failure. Inspect H-to-descriptor formal mapping and actual retirement order. Reject invalid priorState/transition pairs, duplicate/wrong-formal/foreign-owner preparations, occupied staging slots, partial/pending states, early retirement of an explicit actual and use of the call-owned temporary after release. |
| Span distinction | Forward exact typed whole ranges, including admitted empty ranges and outer/inner write variants. Check descriptor-state versus whole-range state and whole completion. Reject mixed/live elements, prefix publication, overlapping active ranges, lost extent/backing and DD or a one-element Home masquerading as range formation. No test presumes an unassigned Span ABI or slice codec. |
| Failure/ownership | Later-argument failure, partial callee construction, body/postcondition failure and DoesNotReturn; no normal-state update, false result, rollback or repeated cleanup. Preserve required settlement and live optional-null/descriptor payload dependencies. Exercise only independently admitted ownership/failure routes. |
| Interchange/providers | Independent Standard producer/consumer, bodyless expectations, source-free provider loading, reordered/deduplicated maps/records, provider Body re-emission and re-consumption. Preserve all original contracts, predicates, formal/input/preparation links and source offsets. Reject lost dependencies, wrong namespaces/owners, incompatible provider states and mixed Standard/private/Development decoding. |
| Widths/qualification | Boundary R13 versus R14/R15; record/map/Home index 0 and 65,535; u32 counts/extents and u16 preparation ordinals, rejecting overflow/truncation. Qualify admitted direct/generic/indirect paths with actual headless supervised JIT and AOT writes/state observations, matched test counts and final compiler/configuration/schema/input identity. Parsing, structural reads, static proposals or emission alone establish no runtime support. |
