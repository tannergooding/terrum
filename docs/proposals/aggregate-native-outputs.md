# Result-correlated aggregate native outputs

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This document recommends a focused source and
logical contract. It does not amend accepted specifications, establish compiler
support, or change the normative registry. The standard representation below
is a concrete **unapproved candidate**, provisionally actionable as a whole.
Its adoption, implementation and qualification are prerequisites for the
whole-plan standard native path, not optional work after that path succeeds.
No private revision or compatibility encoding is proposed.

The [predicate Feature composition decision](predicate-feature-composition.md)
owns the joint provisional registration with
[function-pointer anchors](standard-function-pointer-anchor-contracts.md).
It confirms this proposal's bounded `10/1` with no prerequisites and
`8/1 -> {1/1,2/1,10/1}`, superseding the anchor proposal's former broad
`10 -> 11 -> 9` interpretation. Anchor predicates are limited to the same
phase-3 selectors; general pre/post graphs remain gated. The selector codec
and output semantics below remain owned here.

## Decision and scope

Recommend an explicit, call-extent output contract for a foreign callable to
initialize **one wholly vacant, caller-owned `Blittable Copy` aggregate**.
Publish the complete value only on its declared native-result condition;
otherwise leave the destination logically vacant, irrespective of bytes
written. Use a raw **`write ptr<T>`** formal and an explicit proposed
`&init destination` argument, not a checked reference or an ordinary pointer
to an already-live value.

Apply the same logical contract to a direct callable import and to an exact
foreign native-interface method when its complete vtable/receiver ABI is
independently admitted. No API, symbol name, sample, IID or slot number selects
the behavior. This proposal does not generalize all native pointer contracts:
arrays/prefix outputs, live in/out replacement, ownership acquisition, retained
buffers, async completion and general failure-output values are outside scope.

Preserve the selected
[NoThrow retirement direction](no-throw-retirement-and-proof-boundaries.md#proposed-source-and-proof-rules).
An output contract is not a no-failure promise, and call sites must not inspect
an ordinary authored constructor/helper body to acquire such a promise.

## Problem and current authority

The reported Release failure in
`BuildNativeImportCompilesGenericDx11EntryPointAsync` is `TRM3030` at
[`main.terrum:205`](../../samples/Win32Dx11Triangle/main.terrum#L205):
`new WindowMessage()` inside the native-callable `WindowProcedure`.
That expression invokes an ordinary authored constructor with no published
no-failure bound. The callback needs storage for `PeekMessageW`, not a
constructor-created input value. Passing that storage while it is still
vacant requires a real native-output contract; observing that today's
constructor assigns constants supplies neither that contract nor a published
no-failure bound.

The current prototype does not supply that contract:

* [`DirectNativeImportContract.IsOutputPointee`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs)
  admits scalars and pointer/function-pointer forms, not aggregates.
* [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) builds
  prototype `NativeOutput` facts only for its `NativeStorageAddress`
  representation of `write ptr<T>`, with explicit result conditions.
* [`VerifyDirectNativeParameterAddress`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.DirectNativeImport.cs)
  requires initialized backing, including complete aggregate fields.
  `NativeParameter(Write, ...)` on the prototype's `ptr<T>` path is not
  permission to pass an `init` local.

These are implementation observations, not accepted schemas or reports of
qualification performed by this documentation change. The
[support inventory](../implementation.md#hosted-compiler) likewise separates
private aggregate pointer inputs from bounded native outputs.

| Accepted owner | Boundary preserved or intentionally proposed for amendment |
|---|---|
| [NBP-01 through NBP-04](../specs/conformance/native-boundaries.md#4-pointer-dependency-and-lifetime-boundaries) | Pointer spelling supplies no initialization, extent or retention promise; the selected schema currently rejects NativeOutput/NativeParameter clauses. Admitting this narrow output contract would intentionally amend that rejection only after its schema is selected. |
| [Logical dependency records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending) | Required output/region facts outside assigned shared carriers remain unsupported. This proposal does not bypass that gate. |
| [Contract clauses](../specs/representation/contract-clauses.md#5-outputstate) | Assigned OutputState selects `ref<init T>` or `Span<init T>`, not a raw pointer. This proposal explicitly extends its applicability and the direct-formal domain of [Dependency destination 2](../specs/representation/contract-clauses.md#6-dependency); neither resolves through a NativeOutput row. |
| [References](../specs/language/references.md#descriptors-referents-and-bindings) and [initialization](../specs/language/initialization-and-flow.md#states-reads-and-initialization) | `ptr<T>` and `write ptr<T>` have different access authority; neither alone is initialization authority. Vacancy, partial state, live values and reserved storage remain distinct. |
| [Native roles](../specs/native.md#native-declaration-roles) and [target calls](../specs/targets/windows-x64-call-interface.md) | Raw crossings remain caller-unsafe and follow the exact selected ABI, without marshalling, rooting, implicit pinning, ownership or lifetime extension. |

## Proposed source/API forms

The new `NativeOutput` schema and `&init` argument below are **candidate
syntax, not accepted or implemented examples**. Short attribute/enum names
denote the proposed resolved Core registrations, not same-named user
attributes or the existing `Terrum.Interop` prototype schema. Ordinary
layout/type syntax follows the accepted
[layout schemas](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
and [grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments).

```terrum
[Blittable]
[Layout(LayoutKind.Sequential)]
public struct Packet
{
    public uint32 Kind;
    public uint32 Count;
}

[NativeImport("sensor.dll", "TryReadPacket")]
extern unsafe int32 TryReadPacket(
    [NativeOutput(When: NativeOutputCondition.NonZero)]
    write ptr<Packet> packet);
```

Candidate call-site fragment, inside an ordinary statement scope:

```terrum
unsafe {
    init Packet packet;
    int32 status = TryReadPacket(&init packet);
    if (status != 0) {
        uint32 count = packet.Count;
    }
}
```

The caller reserves object storage without invoking `Packet` construction,
zeroing it, or claiming that zero bits form a valid `Packet`. The native
provider promises the complete value on the selected result. The example is
not a no-failure helper or an independently complete executable.

### Exact candidate schema and formation

* `NativeOutput` is nonrepeatable per formal, parameter-targeted, and authored
  on the callable's primary contract. Inline placement or an exact
  `param(name)` selector uses the ordinary attribute grammar. Its sole required
  argument is `When: NativeOutputCondition`; there is no default, parameter
  ordinal overload, extent argument or implicit combination with
  `NativeParameter`. Duplicate/unknown arguments and incompatible placements
  diagnose. This is not adoption of the prototype `(ordinal, condition)` API
  or its numeric result/failure tags.
* Admit only a synchronous bodyless callable import, or a bodyless method
  declaration in an independently admitted foreign native interface. Its
  selected formal must be exactly `write ptr<T>`, with a closed, directly
  interoperable, positive-extent `Blittable Copy` aggregate `T`. No implicit
  conversion from `ptr<T>`, nullable pointer, `ref<T>` or `ref<init T>` applies.
  Ordinary authored helpers, native-callable callback bodies, receivers,
  fields, results and arbitrary function-pointer type uses are not new
  attachment targets.
* The proposed closed source conditions are `NonZero` for `int32`/`uint32`
  results and `NonNegative` for `int32` results. They mean exactly `result != 0`
  and `result >= 0` in that result type. Nonzero does not mean exactly one;
  signed negative values satisfy NonZero. Source enum numeric values are not
  wire codes. Normalize these templates to the shared result-predicate nodes
  specified below, not a native-only condition field, a second predicate
  language or a symbol/status-name heuristic.
* Add a direct argument form `"&", "init", Expression` to
  [Arguments](../specs/grammar-spec.md#32-types-parameters-and-arguments).
  Semantically its expression must identify one whole initialization-capable
  local of exact type `T`, proven wholly vacant, with stable caller-owned
  backing and the required extent/alignment. This form explicitly reserves
  initialization authority for the annotated call only. It cannot be stored,
  returned, captured, used as a standalone pointer expression, or forwarded
  through an unannotated formal.
* Ordinary `&place` is not this projection and is not changed to accept vacant
  storage. `write ptr<T>` still grants referent write access, not an
  initialization promise; `ptr<T>` remains a different, non-writing access
  contract. The live pointer descriptor passed to native code is not the
  output value. No checked reference, `pin ref<T>` alias, pin acquisition or
  lifetime extension is introduced.

These restrictions select whole local storage rather than silently defining
field paths, arbitrary raw-address provenance, overlapping spans or
initialization-reference extraction. Extending those destinations is a
separate decision.

### Vtable and motivating API applicability

The same parameter contract can appear on the following **member fragment**
of an already admitted foreign native interface:

```terrum
int32 TryReadPacket(
    [NativeOutput(When: NativeOutputCondition.NonNegative)]
    write ptr<Packet> packet);
```

The containing interface's layout, declaring method identity,
`NativeCallable` convention, live receiver, inherited slots, adjustments and
provider/code lifetime must be supplied independently under
[interface ABI rules](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations).
This fragment adds no interface-layout syntax or `NativeVTableMethod` schema.
The receiver is not an output formal. Inherited slots retain their declaring
method's contract; a NativeCallable default does not invent output facts.
Terrum-authored implementations of such slots need separately supported
body verification/publication; this foreign-declaration slice does not enable
them by trusting their bodies.

An authored `PeekMessageW` declaration could use the NonZero form on
`write ptr<WindowMessage>` with an independently established native record
layout and provider promise. A callback could then reserve a vacant local
instead of constructing an unused input value. No fact follows merely from
the API name or the sample's record shape. APIs with different status/output
behavior require their actual contracts, not a copied annotation.

Avoiding that ordinary constructor call does **not** prove the whole callback
admissible: import resolution, initialization triggers, remaining calls,
checks, cleanup and supported native-failure containment retain their own
obligations. NativeOutput is not a declaration that those operations cannot
fail, and native exceptions are not ordinary false results.

## Destination states, order and failed writes

The contract is output-only, not transactional replacement:

| Point or outcome | Required state and behavior |
|---|---|
| Before output formation | The whole destination is vacant, with no live child or retained partial acquisition. `init T` and `init mut T` can supply eligible storage; a live, partial, maybe-live or already-reserved place cannot. |
| A caller has a live `T` | Use new vacant storage, or explicitly end the old value under ordinary dependency/retirement rules before forming the output. Copy eligibility does not make in-place overwrite implicitly transactional. No saving/restoring of the old value is supplied. |
| During the call reservation | The caller and its aliases cannot read, state-test, initialize, move, retire or reuse the destination, including through a reentrant callback. Native code may write within the exact object region and use bytes it establishes itself, but cannot depend on incoming `T` contents or retain the address beyond this invocation. |
| Normal result satisfies When | All semantic fields/representation invariants of a complete `T` must be established. Publish one live value in the caller's holder only at normal invocation completion. No constructor call or default value is synthesized. |
| Normal result does not satisfy When | Publish no `T`; the destination is vacant. Partial or even complete bytes may have been written. They are not readable fields, a fallback value, evidence of preservation, or an initialized partial owner. No zeroing, rollback or complete-`T` Cleanup is implied. |
| Failure before native invocation | No provider writes occurred. Release the unentered output reservation; its destination remains vacant. Preserve any other already-executed argument effects. |
| Admitted exception after invocation begins | No normal result or output relation is fabricated. Preserve the existing exceptional indeterminate-state rule; do not read or perform complete-`T` cleanup based on this annotation. Reuse after recovery needs independently admitted state evidence, not the normal false/vacant rule. |

Multiple annotated formals may share one captured scalar result while retaining
their own conditions and disjoint whole destinations. There is no inferred
all-outputs success rule. Foreign reentry is permitted only while these
reservations and all other boundary obligations remain respected.

Evaluate the receiver and all arguments once in their ordinary source order.
Evaluate and reserve each output place at its argument position; preserve
its identity, generation, backing and authority while later arguments run.
Later arguments cannot inspect the reservation or create conflicting output
access. Failed argument evaluation prevents invocation; target register
placement does not reorder source effects.

The native result is captured once. Complete the actual invocation's
postconditions and phase cleanup before publishing normal output facts.
The declared integral predicate relates that result to that exact destination
generation; it is not a runtime check that validates arbitrary aggregate
bytes. Before a result test, joins may leave the output maybe-live. Saved
discriminator copies and invalidation follow
[outcome correlation rules](../specs/language/initialization-and-flow.md#outcome-correlations-and-invalidation);
reassignment, replacement, retirement and relevant aliasing writes cannot
revive an old relation. Ignoring a result grants no unguarded read.

The bounded rootless Copy payload acquires no automatic owning-child
responsibility from partial writes. A provider contract that can acquire
resources requiring settlement, retain a pointer, or leave a separately
responsible partial value needs another complete contract and is not admitted
by this one. Ordinary external side effects are not rolled back on failure.

## Layout, ABI and trust obligations

The provider receives the address of the actual reserved caller storage, not
a by-value aggregate copy, a hidden result buffer or a marshalling temporary.
The logical formal is one pointer even when `T` is large. Its extent,
alignment, nested fields, packing and validity follow the exact selected
[target layout](../specs/targets/windows-x64-data-layout.md) and
[foreign call mapping](../specs/targets/windows-x64-call-interface.md#1-foreign-primitive-admission).
Equal size, current field shape or `[Blittable]` alone is insufficient.
There is no proposal-specific aggregate-size cap; unsupported complete
representations reject rather than acquire a new ABI.

Success requires valid Boolean, enum, pointer-descriptor and other nominal
field representations, not merely writes to every byte. Padding need not be
initialized unless the external protocol independently requires it; exposing
raw bytes retains that protocol's obligations. Raw pointer fields do not
establish pointee initialization, lifetime, backing, ownership or permission
to dereference. Copy is not sharing, atomicity or permission for conflicting
access. Known overlap/conflicting reservations and underaligned backing
diagnose; residual foreign alias/provenance promises remain caller-unsafe
responsibilities, not facts granted by `unsafe`.

For direct foreign imports the authored declaration is a trust assertion about
the exact library/symbol, resolution and calling convention. Successful DLL
loading does not verify its truth. For interface dispatch, retain the exact
interface/method contract and receiver/provider lifetime; an arbitrary pointer,
caller-selected IID or equal slot number is insufficient.

Through Terrum dependency binding and replacement retain the complete expected
declaration and apply
[directed satisfaction](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions).
Preserve exact formal/type identity, raw write authority, vacant formation
domain, object footprint/alignment, condition and state mapping, no-input-read
and no-retention guarantees, publication phase, independent dependencies,
caller-unsafety and ABI. Changing NonZero to NonNegative, adding input
requirements/retention, or publishing a live failure output is not automatically
a stronger compatible contract. Already compiled consumers need their retained
facts preserved or explicit regeneration/invalidation, not callee-body inference.
Converting to a function-pointer surface that cannot retain the complete
contract cannot preserve these output privileges merely because pointer bits
or ABI carriers match.

[Producer truth and consumer verification](../specs/representation/reader-trust.md#1-valid-artifacts-and-producer-obligations)
remain separate. Source/public-bound-program producers check caller states,
authority and all facts they establish; foreign declarations cannot prove
arbitrary provider behavior. Consumers check selected schema/framing,
ownership of records, types, formal/predicate references, ranges, multiplicity,
supported destination/condition shapes, capability closure and complete
binding before use. They need not re-prove foreign behavior or producer body
facts. False output metadata or unsafe provider promises can invalidate
execution; neither a self-declared trust bit nor an optional audit repairs them.

## Concrete shared logical and standard representation

**Recommend extending shared OutputState, not allocating a NativeOutput
clause/table.** Its logical destination is an initialization output identified
by the exact callable/formal, complete output type, formation contract and
publication phase. A checked `ref<init T>` destination, `Span<init T>` range
and the proposed whole-object raw destination remain distinct formation
contracts; sharing a state relation does not make their source types aliases.
The raw variant fixes vacancy, one-object extent, no incoming-value read,
call-extent address use and no address retention. These are required
guarantees, not optional annotations.

### Accepted allocation and actual missing pieces

The following baseline comes from the
[machine registry](../specs/representation/trrm-1.registry.json),
[predicate records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms),
[Body records](../specs/il-instruction-set.md#typed-operand-records) and
[manifest](../specs/representation/semantic-manifests.md), not prototype enums:

| Surface | Existing assignment and remaining gap |
|---|---|
| OutputState / Dependency | Clauses `0x0002` / `0x0004`, each 20 bytes total. Raw output applicability is not assigned. Dependency destination `2` currently means checked initialization output. |
| Published predicates | Clause `0x000B`, 20 bytes; sections `0x0037..0x003E` already hold graphs, footprints, roots/dependencies, nodes, operands, edges and captures. General node/operand kinds and their roles/arities remain open. |
| Predicate attachment | `0x000B.phaseTag` currently has `1=Precondition`, `2=NormalPostcondition`. A success selector and its complement cannot both be asserted postconditions. No output-selector phase is assigned. |
| Feature closure | `10/1=PublishedPredicates` is assigned but registration-pending, with `10 -> 11 -> 9`. Recognizable predicate tables alone do not complete that closure. |
| Calls and places | CallSite record `0x02`, DestinationPreparation `0x03`, PlaceSelection `0x04`, SemanticStore `0x05`; input source-use values stop at `5`. A selected place is neither a native address nor a reservation tied to one invocation. |
| Free capacity selected below | Feature `8`, long primary `0xDF`, Body record kind `0x0C`, input source-use `6` and predicate phase `3` are unassigned. Predicate node/operand numeric domains are still open. No new section, DefId, TypeSpec form, prefix or instruction codec is needed. |

The clause chapter still says `0x000B` and the predicate section/Feature
assignment remain open, while
[ICOW-16/18](../specs/conformance/il-contract-clauses.md#owner-ranges-and-occurrence-keys),
the registry and logical records state the clause/section assignments.
This owner discrepancy remains normatively unresolved. Provisionally
preserve those explicit assignments; adoption must reconcile the owners,
not allocate a second attachment or infer missing node semantics.
The proposed additions below are not already accepted merely because their
containers are assigned.

### Candidate assignment summary

All additions and applicability/registration changes in this table are
**unapproved recommendations**; existing allocations are identified as such.
Unmentioned flags, reserved fields, Transfer, NoThrow and other unassigned
values retain their current rejection rules.

| Namespace | Candidate |
|---|---|
| Contract `0x0002` OutputState | Keep its exact 20-byte shape; extend the admitted destination domain to the selected `write ptr<T>` aggregate formal. Make `predicateRef` explicitly a typed `PredicateGraph` row in section `0x0037`. |
| Contract `0x0004` Dependency | Keep its exact 20-byte shape; broaden destination `2` to **initialization output**, selected directly by formal ordinal, including this raw output. No NativeOutput-row reference or new destination tag. |
| Contract `0x000B.phaseTag` | Add `3=NormalOutcomeSelector`; retain the existing 20-byte payload/owner/ordinal rules. It observes a predicate, not an assertion that the predicate is true. |
| `PredicateNode.nodeKind` | Assign the six closed shared normal-result test forms in the next table, `0x00000001..0x00000006`. |
| `PredicateNodeOperand.operandKind` | Assign `1=ReadRoot`, selecting a typed row of section `0x0039` in that graph's own footprint. |
| Feature `10/1` | Complete a deliberately bounded `PublishedPredicates` registration for these closed normal-outcome selectors, governing edition `1`, prerequisites `[]`. Remove the draft unconditional `10 -> 11` edge; the [composition decision](predicate-feature-composition.md#recommended-registration-and-closure) governs the shared registration and bounded anchor applicability. |
| Feature `8/1` | Register `NativeAggregateOutputs`, governing edition `1`, prerequisites `[(1,1),(2,1),(10,1)]`, covering raw-output declarations, shared dependency applicability, reservation/call records, binding, rewriting and lowering below. |
| Body record `0x0C` | `OutputReservation`, 12 bytes total: ordinary four-byte record header, then `u16 callSiteRecordIndex, u16 destinationHomeIndex, u32 formalOrdinal`. |
| Long primary `0xDF` | Four bytes: `u8 primary=DF, u8 action, u16 outputReservationRecordIndex`; action `0=Reserve`, `1=CancelBeforeInvoke`. No result register, short form, prefix or escape. |
| CallSite input source-use `6` | `ReservedNativeOutput`; only with `selectorKind=2` (Home), naming the reserved **T destination**, not a pointer-valued argument home. Other source-use contexts do not acquire value `6`. |

DF completes the typed storage/reference neighborhood `D8..DD`, beside DE's
layout/type observations, without consuming the exception/control band or
introducing a width escape. Its action values `02..FF` remain invalid.
Record `0C` follows the assigned typed-record inventory; its small number
does not shorten the fixed record/header or index widths.

**Feature 8 versus 14: select 8.** The complete assigned namespace has native
capabilities `1..7`, then `9=NominalUnionMetadata`, `10=PublishedPredicates`,
`11=TypedFrozenValues`, `12=ConstantExecution` and `13=LayoutQueries`.
Both 8 and 14 are free. Feature 8 keeps this specifically native capability
beside NativeBoundaryContracts and its related family; 14 would append it
after the shared proof/value/query group without a compensating advantage.
Neither choice changes the full-width `u32` encoding cost.

The [earlier Generic 6–8 candidates](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending)
are explicitly outside the current allocation, not a reservation of 8.
Keep prerequisites `1/1`, `2/1`, `10/1`: numeric order does not constrain
dependency edges, so `8 -> 10` is valid. Direct calls and interface dispatch
still add 3 or 5 respectively; 7's native-unwind support is not made mandatory.
Feature 14 stays unassigned, not an alias or compatibility spelling for 8.

Feature 10's narrowed registration is an intentional amendment to an
incomplete unpublished capability, not a claim to implement arbitrary
published predicates. It enables the shared selectors required here without
pretending that general expression graphs, scalar literals, frozen aggregates
or nominal unions have complete registrations. The separate
[scalar frozen-value proposal](scalar-frozen-value-registration.md) addresses
Feature 11; neither proposal silently approves the other. Retaining an actual
ConstantMap/frozen value still requires its own capability, but a fixed
zero/sign result test has no literal Blob and no Feature 11 dependency.

### Exact shared predicate codec

Each of these nodes is a closed structural **normal-result test**, not a
general selected operator/call or an embedded Body opcode. Its test semantics
are fixed by the selected schema; a same-named user operator cannot change
them. Other node/operand forms remain unassigned by this proposal.

| `nodeKind` | Test | Owner's ordinary result type |
|---|---|---|
| `1` | Boolean result is true | `bool` |
| `2` | Boolean result is false | `bool` |
| `3` | Integral result is nonzero | `int32` or `uint32` |
| `4` | Integral result is zero | `int32` or `uint32` |
| `5` | Signed result is nonnegative | `int32` |
| `6` | Signed result is negative | `int32` |

The Boolean pair also serves the existing checked-output success pattern;
it does not extend the NativeOutput source API to Boolean foreign signatures.
NativeOutput NonZero normalizes to nodes `3/4`, NonNegative to `5/6`.
Comparing the complete owner result type is mandatory; Win32 BOOL remains an
int32 status, not the one-byte Terrum Boolean. No type-name heuristic,
caller-chosen comparison, general expression evaluator or implicit conversion
is involved.

All multi-byte fields are little-endian. Global typed references are one-based
`u32` rows in `1..0xFF_FFFF`, with reserved-zero high bytes; Body indexes below
remain a separate zero-based namespace.

| Existing record | Candidate complete selector shape |
|---|---|
| `0x000B` Contract | Header `u32 ownerRow, u16 clauseKind=000B, u16 flags=0`; payload `u8 phaseTag=3, u8 flags=0, u16 reserved=0, u32 phaseLocalOrdinal, u32 predicateGraphRef`. Owner is the exact MethodDef; phase-3 ordinals are contiguous and unique. |
| `0x0037` PredicateGraph, 8 bytes | `u32 rootNodeRef, u32 readFootprintRef`, both required. Exactly one owning `0x000B` clause; OutputState may additionally reference the graph, including from several outputs of that same owner. |
| `0x0038` PredicateReadFootprint, 16 bytes | `u32 rootFirst, u32 rootCount=1, u32 dependencyFirst=0, u32 dependencyCount=0`. Its exclusive root range contains exactly the ordinary result. |
| `0x0039` PredicateReadRoot, 8 bytes | `u8 rootKind=5, u8 flags=0, u16 reserved=0, u32 rootIdentity=0`. Meaning is the attached owner's captured ordinary result, not an output or a formal ordinal. |
| `0x003B` PredicateNode, 16 bytes | `u32 nodeKind` from the table, `u32 resultTypeSpec, u32 operandFirst, u32 operandCount=1`. `resultTypeSpec` is a complete TypeSpecRef resolving exactly to built-in bool under the metadata complete-type rule, not an untyped integer or a forced row-only reference. |
| `0x003C` PredicateNodeOperand, 8 bytes | `u8 operandKind=1, u8 flags=0, u16 reserved=0, u32 operandRef`; the reference names the matching `0x0039` root in this graph's own footprint. |
| Edges/captures | No child edges, captures, selected-call dependencies, extra roots or literal operands. Each graph owns its one node, operand and footprint/root occurrence; no cross-graph sharing of those rows. |

The result-test leaf is the graph root and has no incoming edge. Its only
observed subject is the owner's result. Full node, operand and footprint
ranges must be well-framed and consistently owned. The shared strict graph
ownership rules survive; only immutable complete type identities may be
shared here.

Selector observation does not assert success, invoke user code, run a getter,
allocate, read an output, or raise a false-postcondition failure. At normal
Publish it classifies the saved result. Phase tags `1/2` keep their distinct
assertion semantics; this bounded Feature 10 registration does not activate
their general predicate programs. General predicates, effects/read roots,
literal/call/capture nodes and their broader registration remain separate
work, not prerequisites hidden inside this closed selector path.

Canonical writers reuse the same selector for an equal template and complete
owner context, order phase-3 selectors by unsigned nodeKind, and number them
contiguously. Ordinary readers may accept equivalent duplicate graphs and
reordered physical tables with consistent references; duplicate clause
occurrence keys, cross-owner references and ambiguous output relations still
reject. Binding compares the normalized owner/result-type/test identity, not
the graph row, source enum number or phase ordinal.

### OutputState and Dependency application

An OutputState row remains exactly:

```text
u32 ownerRow, u16 clauseKind=0002, u16 flags=0,
u32 parameterOrdinal, u32 predicateRef, u8 state, u8 reserved[3]=0
```

For a raw formal require exactly two applicable rows: success `3` or `5`
with `state=1`, and its complement `4` or `6` with `state=0`. Both graphs are
phase-3 selectors attached to the same MethodDef. The pair is exhaustive and
disjoint by the closed test/type rules, not a theorem about provider bodies.
Different outputs can select different pairs. No missing-row, unconditional
native-output, live-failure or partial-state default is admitted.

The Param remains mode `00`; its complete type is `write ptr<T>`. The clause
pair plus Feature 8 and an eligible native declaration role selects the
fixed raw-output formation/footprint contract. It does not add an unsafe
storage-address pseudo-type or alter `ptr<T>` access. NativeImport must name
that bodyless static MethodDef, or an InterfaceMethod must name the exact
bodyless native slot with its BlittableInterface layout and effective
NativeCallable contract. `bodyRow=0`, a native address or NativeCallable alone
is not sufficient role evidence.
Publish the assigned caller-unsafe Effects bit `04` in the normalized
declaration contract, with `02` retained independently when DoesNotReturn
actually applies. No `01` NoThrow bit is restored. This call-site obligation
does not authorize unsafe operations within a caller or callee body.

For Dependency, the existing payload stays:

```text
u8 destinationKind, u8 sourceKind, u8 relationship, u8 phase,
u32 destinationOrdinal, u32 sourceOrdinal
```

Extend `destinationKind=2` to the same directly identified initialization
formal. For this synchronous native variant only `phase=1` applies, and its
relations become obligations of the published live value on success; failure
publishes no value/dependency holder. Require an explicit source set: either
the existing all-zero source/relationship/ordinal row for an empty set, or
the existing distinct formal/receiver source rows and relationship meanings.
NativeOutput alone supplies no formal/receiver backing sources and normalizes
to the explicit empty set; independently declared required dependencies must
be retained instead, never overwritten by that default.

Nonempty sources retain their existing checked permissions and whole-source
interpretations; this amendment does not make raw-pointer pointees into
checked source storage or assign field/subregion paths. A required relation
outside those shared rules needs its own supported contract. Empty formal/
receiver sources do not erase independent type, provider, code, target or
external backing obligations and do not make a pointer field safe to use.
Phase 2/3, retention of the output address, ownership transfer and partial
resource acquisition are not introduced by this raw variant.

### Body staging, call-site and lowering

Keep instruction encoding `01`, the masked prefix, HomeMap/MethodMap/TypeMap
formats and CallSite record `0x02` unchanged in extent. Do not pass a
PlaceHandle as a pointer or misuse checked initialization source-use `5`.
The candidate adds one effectful staging operation, not a general address or
initialization escape.

1. Allocate a role-`3` local Home of exact complete type `T`, initially
   vacant, with actual target-required stable object backing. Home identity
   is distinct even when types/bytes match. No default constructor or memset
   is represented.
2. At the source `&init` argument position execute `DF 00 recordIndex:u16`.
   Its `OutputReservation` record identifies one general CallSite record,
   the exact destination Home and a zero-based **formal** ordinal; the
   ordinal does not count an implicit receiver. This captures and reserves
   that Home's current invocation instance/generation before later arguments.
   It produces no ordinary value, reference, pointer or register carrier.
3. Other arguments stage normally, in source order. The reservation cannot
   be inspected, reused or duplicated through another output. Every normal
   path that reaches the selected call consumes its matching reservations.
   A path abandoning the unentered invocation releases each through
   `DF 01 recordIndex:u16`, or the same staging-cancellation semantics on an
   admitted exceptional edge. Cancellation is permitted only before Invoke;
   it cannot turn an invoked/indeterminate or live output back into vacancy.
4. The general CallSite uses `targetKind=1` (MethodMap), resolving the exact
   import or interface declaration. Input rows retain four bytes:
   `u8 selectorKind, u8 sourceUse, u16 selectorIndex`. For an annotated formal
   require `(selectorKind,sourceUse)=(2,6)` and the reservation's Home index.
   This is an explicit reserved destination selector, not a read of Home T.
   Ordinary receiver/value inputs remain in their existing modes and formal
   order. Raw callable-value targets and compact window calls do not encode
   this new protocol.
5. Check the target/receiver, then perform ordinary dependency-valid
   DestinationPreparation for other hidden homes, then Invoke. The output
   reservation is not a preparation recipe to retire a live destination;
   neither DestinationPreparation nor SemanticStore accepts new source-use
   `6`. At Invoke project the actual reserved Home address into the native
   pointer argument, retaining its backing and generation through the call.
6. For a normally returning target retain the int32/uint32 result in a general
   register R0..R13 or a result Home, including a hidden one when source
   discards it. None, R14/R15 and an output Home are not result destinations
   for that call. Complete invocation postconditions/cleanup, then apply the
   selector pair at normal Publish and release the reservation into live/
   vacant state. Native unwind has no normal Publish; the ordinary exceptional
   rule remains indeterminate.

A verified DoesNotReturn target instead keeps the existing terminal general
CallSite rule: None result, full declared signature, no normal successor or
published output. Its declarations still require valid output rows; absent
normal completion does not waive structural/type/support checking or become
a no-failure proof. DoesNotReturn does not justify cancelling an already-entered
invocation as if no native writes had happened.

`OutputReservation`'s exact 12-byte record is:

```text
u8 recordKind=0C, u8 flags=0, u16 reserved=0,
u16 callSiteRecordIndex, u16 destinationHomeIndex, u32 formalOrdinal
```

The two indexes address this Body's typed records and HomeMap, respectively;
`0xFFFF` remains usable and counts retain the existing 65,536 limit.
The formal ordinal is a full-width `u32` bounded by the selected MethodSig.
The target record must be kind `02`, and its selected input at that ordinal
(after any receiver) must agree exactly. No global Contract row, runtime
instance ID, pointer bits or TypeMap index is hidden in these fields.

Reservations are dynamic executions of their staging operation, not record
row identities. Reusing a CallSite/OutputReservation record in a loop requires
a fresh proven-vacant generation each time; a previous result cannot revive
it. CFG joins preserve pending reservations with their actual call, Home,
generation and backing. No BlockEntryState flag, new stored descriptor or
per-expression-ID table is introduced. Unsupported state merges reject
affected use rather than guess. More than fourteen simultaneous outputs can
use distinct Homes; this protocol introduces no register-window arity cap.

For example, these are candidate **fragments**, not a complete artifact:

```text
Body record 7, reserving Home 3 for CallSite record 5, formal 0:
0C 00 00 00  05 00 03 00  00 00 00 00
Reserve:             DF 00 07 00
CallSite 5 input:    02 06 03 00
General call:        90 05
Pre-invoke cancel:  DF 01 07 00
```

The call and cancellation are alternative control-flow paths, not a sequence
that cancels an already invoked call. For MethodDef 1 with NonZero output
formal 0, its two 20-byte OutputState rows reference separate graphs rooted
in node kinds `3` and `4`, with states `1` and `0`; each graph has the exact
result-only rows above. No constructor call, output load or pointer constant
is needed.

Lowering maps the reserved Home directly to its native address with checked
frame extent/alignment, not to an aggregate by-value temporary. Preserve
earlier argument snapshots, the selected vtable receiver/adjustment, native
register/stack placement and all provider lifetimes. The internal publication
test can use ordinary integer comparison/control flow, but must retain its
association with the exact result and Home generation. An `is init` lowering,
where otherwise admitted, observes that state/discriminator, never output
bytes. DF cancellation changes reservation state without touching or cleaning
a T. None of these instructions grants an outward failure path from a
NativeCallable body or removes a required scoped no-escape proof.

### Complete admission, trust, binding and remapping

The proposed Feature registrations are coherent completion requirements,
not implementation badges:

* A using Module requires `8/1` for every retained raw output declaration,
  expected import, dependency application or reservation/call use, even if
  uncalled. Require `10/1` for retained closed selector graphs, including
  checked-output uses. Derive the complete catalog closure per Module.
  These are effective requirements, not mandatory explicit prerequisite
  rows: `{3/1,8/1}` already derives `10/1` for a direct output call.
  [Exact composition cases](predicate-feature-composition.md#proposed-manifest-and-cross-feature-cases)
  cover anchors, outputs and independent value requirements in one Module.
* A direct-call use additionally needs `3/1`; an interface-call use needs
  `5/1`. Thus the basic closures are `{1,2,3,8,10}` and
  `{1,2,5,8,10}` respectively. A native export/address/entry use also retains
  its actual Feature 4 obligations. Feature 7 remains conditional on admitted
  native exception unwinding; Feature 13 on actual query/FieldMap/path uses.
  Features 9/11/12 are not smuggled in by an implicit zero test.
* Require the complete selected target and actual Copy/Blittable, type,
  layout, input, code-lifetime and failure contracts. This does not invent
  a Copy/Unique TypeDef flag or infer those facts from a struct/native role.
  Unavailable independent type/ownership/handler/interface representations
  still gate their affected uses; they are not an unresolved output-clause
  assignment or permission to discard obligations.
* Producers establish annotation truth within their responsibility, exact
  formation/state/source-order facts, disjoint reservations, exhaustive
  selector pairs, actual output validity and retained requirements. Public
  bound programs and source projections must carry those same facts through
  shared visitors/rewriters and mandatory producer verification, not only
  through one direct-import binder path.
* Ordinary consumers validate selected framing/extents, owner/range/reference
  namespaces, zero reserved fields, exact result/formal/type eligibility,
  graph/footprint/node arities, phase ownership, complementary state pairs,
  dependency sets, reservation-to-call/input agreement, selected capabilities,
  native roles, target mapping and complete binding before use. They may rely
  on producer-published Body flow and foreign semantic promises without
  proving provider bodies or performing a general state/alias theorem proof.
  A deeper source-order/flow audit remains separately scoped.
* Binding/replacement compares exact declaration/role and complete types,
  native output formation domain and fixed no-read/no-retain footprint,
  normalized predicate pair, state/phase, and dependency source identities/
  permissions. Formal names and rows may change; logical order and exact
  actual-source/receiver correspondence may not. Adding a success output
  dependency, changing the status partition, removing vacancy, increasing
  alignment/extent requirements or weakening no-retention cannot service an
  unchanged consumer. Preserve any stronger facts actually relied on, or
  invalidate/recompile the dependent code explicitly.
* Rewriters remap MethodDef owners and Contract ranges, formal ordinals and
  MethodSig context, complete TypeSpecs/Layout/TargetProfile/native identities,
  graph/root/node/operand/edge/footprint references and Dependency sources.
  Rebuild phase ordinals and graph-owned ranges consistently; no graph can
  acquire another owner's result context through row interning.
* Within each Body remap MethodMap/TypeMap and distinct Home indexes,
  CallSite input/result selectors, OutputReservation's two indexes and formal,
  DF record operands and preparation references. Canonical typed records
  follow first logical use, including DF's referenced CallSite; forward record
  references are valid. Ordinary consistent duplicate/reordered records are
  allowed but must not create another logical reservation or merge Homes.
  Rebuild record offsets, code/Block extents and affected SourceMap instruction
  slots/ordinals. DF uses existing SourceMap role `0=whole-instruction`, not
  an arithmetic substep or new source-role tag. It has one four-byte form;
  no unused short alias is assigned.

This bounded amendment leaves the current broad Feature 10 interpretation
behind an exact draft boundary; it cannot relabel arbitrary old predicate
programs as the new closed registration. Do not implement two meanings for
the same bytes under one selected schema. Integrating the candidate requires
the registry, clause/logical-record, metadata, instruction, manifest, binding
and conformance owners to agree as a whole. Their currently accepted text is
unchanged by this proposal.

### Standard-path prerequisite and private persistence

There is **no undecided physical assignment within this recommended bounded
output direction**: existing OutputState/Dependency and predicate sections,
the closed Feature 10 registration, new Feature 8, selector phase/node/
operand domains and DF/record-0C/source-use-6 form one candidate schema.
Approval, implementation and independent qualification are still required
before relying on that standard path. Broader predicate/type/runtime work
retains its separately identified scope, not an unspecified future output
codec that an implementer must guess.

An explicitly selected private schema may experiment with the same logical
contract, preserving states, verification, binding and ABI. This proposal
chooses no private revision/encoding. `.trm`, `.trmn`, `.tnset` and `.trng`
remain [private persistence](../specs/representation/persistence.md); their
success is not an alternative acceptance gate for the whole-plan standard
path. An independently read embedded `.trrm` must select the complete amended
standard draft and its requirements. Until then standard production/use
must reject the required unrepresentable/unsupported contract, not omit it.

[Exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
still governs unchanged `(1,0)` bytes. No optional Attr, reserved field,
private fallback, retired native-output table or historical decoder activates
this candidate. Conversion selects source and destination schemas and
reconstructs all affected records, indexes and proofs; byte substitution or
clearing a requirement is not conversion.

## Alternatives and owner integration

| Representation option | Tradeoff |
|---|---|
| **Extend shared OutputState, recommended** | Retains one state/predicate/dependency model and existing exact row widths. Requires explicit raw-domain, selector and Body-staging amendments; the bounded shared predicate registration closes their actual dependency rather than assuming the general graph codec exists. |
| Distinct native-output clause/record | Could store formal, condition and failure disposition directly, but would duplicate state partitions, dependencies, binding and call publication. Keeping its own condition enum would recreate a parallel native-only predicate codec; referencing the shared graphs would still need their completion. No such clause/table number is selected. |
| Extend OutputState but require general predicate completion first | Preserves the broad draft Feature 10 scope, but cannot deliver this standard path while node/operand forms and the `10 -> 11 -> 9` closure remain incomplete. The recommended explicit bounded registration is a substantive draft amendment, not silent partial support for that broad capability. |

**General no-failure promise/helper, not recommended.** Reintroducing a checked
`[NoThrow]` constructor or a published ordinary-form no-failure helper could
authorize that call in a callback proof and produce an initialized buffer.
It would need its own source applicability, failure/phase domain, complete
mandatory carrier and replacement rules. It directly reverses the selected
[retirement recommendation](no-throw-retirement-and-proof-boundaries.md#alternatives-and-acceptance-cases).
A helper's current constant body, Pure selection or retired Effects bit
cannot supply the promise. It also would not approve the current
NativeParameter prototype or express vacant, result-correlated aggregate
outputs; it solves a different, larger callable-contract problem.

An intrinsic zero-fill/default helper is not a generic substitute: zero bits
need not satisfy `T`'s validity, and an ordinary authored wrapper remains an
ordinary potentially failing call. Using checked `ref<init T>` as an alias
for the foreign pointer likewise erases distinct source contracts; an
authored checked wrapper would still need an admitted raw-output publication
operation. A full native region/ownership/in-out redesign may be useful
later, but is unnecessary for this bounded whole-aggregate decision.

Adoption would require focused edits to these owners; none is amended here:

| Owner | Intended integration |
|---|---|
| [Grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments), [reference formation](../specs/language/references.md#formation), [flow](../specs/language/initialization-and-flow.md) | Register the exact candidate attribute/condition schema and direct `&init` argument; specify its vacancy-only reservation and result correlation without changing ordinary pointer/reference formation. |
| [Native roles](../specs/native.md#native-declaration-roles), [layout/ABI](../specs/data-layout-and-abi.md), [Windows call interface](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | Admit the selected foreign declaration roles and actual pointer projection while preserving unsafe, target, backing and exception boundaries. Do not rename the checked-init ABI into a raw-pointer contract. |
| [Logical records](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending), [clauses](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness), [registry](../specs/representation/registry.md), [manifest](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Adopt the concrete OutputState/Dependency applicability, result-selector codec and bounded Feature 10/new Feature 8 registrations together. Reconcile stale assignment projections and retain before-use rejection until the amended schema is selected and supported. |
| [Body records](../specs/il-instruction-set.md#typed-operand-records), [calls](../specs/il-instruction-set.md#invocation-phases), [metadata](../specs/il-metadata.md#body-table-and-coded-instruction-boundary), [canonicality](../specs/representation/encoding-canonicality.md) | Add DF/OutputReservation and CallSite source-use 6 with exact staging, cancellation, field widths, bounds and rewrite rules. Do not reinterpret checked-init source-use 5, PlaceSelection or a private storage-address type. |
| [Binding](../specs/representation/binding-and-replacement.md), [consumer checking](../specs/representation/reader-trust.md), [NBP-04 and NBM-08](../specs/conformance/native-boundaries.md) | Retain complete expectations and before-use checks; replace only the applicable native-output rejection after adoption, leaving NativeParameter and legacy/equal-version aliases unapproved. |

## Proposed acceptance cases

These are design requirements, not passing compiler or runtime evidence.
Qualify future implementation with unrelated synthetic foreign providers,
not only the motivating graphics sample.

| Case | Required outcome |
|---|---|
| Vacant local, nested `Blittable Copy` aggregate, valid successful writes | Publish the complete value under the exact saved result condition without invoking any source constructor. Exercise direct import and independently admitted vtable dispatch. |
| NonZero receives `2` or signed `-1`; NonNegative receives `0` | Apply the exact integral tests, not Boolean-one, HRESULT-name or sample heuristics. |
| False result after no writes, one field written, or all bytes written | Keep vacant; reject value reads/default inference and perform no complete-`T` cleanup or rollback. |
| Live, maybe-live, partial or already-reserved output place | Reject formation until ordinary state/dependency rules establish whole vacancy. No implicit retirement of an old value. |
| `ptr<T>`, nullable pointer, ordinary `&place`, checked ref or stored raw pointer supplied instead | Do not upgrade access or fabricate the proposed initialization reservation. |
| Missing/duplicate/unknown condition, wrong result type, nonaggregate/non-Copy/rooted/unsupported layout | Reject the selected source contract or affected use, not a silent fallback. |
| Later argument fails, reads the output or aliases a second output to it | Preserve source evaluation order; no invocation on failure, and no competing access/reservation. |
| Reentrant access conflicts with the reserved destination; native code retains it, or failed writes acquire an owner | Violates this provider contract; require a distinct admitted protocol rather than relaxing the reservation. Unrelated callbacks do not themselves violate it. |
| Native unwind or postcondition failure before normal publication | No normal result/output; retain exceptional initialization and containment rules. Unsafe or NativeOutput is not NoThrow. |
| DoesNotReturn native target with a non-unit declared status | Preserve the complete signature and terminal None-result CallSite; publish no default status/output, and still validate the complete declaration and reservations. |
| Saved result overwritten, output retired/replaced, or relevant aliasing write intervenes | Invalidate affected correlations; an old result cannot authorize reading a new/vacant generation. |
| Packed/over-aligned nested object, large extent or target/profile mismatch | Require actual derived extent/alignment and exact foreign mapping; no size-based alias, buffer overrun or alignment fabrication. |
| Provider promises success but leaves an invalid Boolean/enum/nominal field | False semantic/unsafe promise, not valid initialization from byte coverage. Mandatory consumer structural checks are not semantic re-proof of that provider. |
| Row/type remapping, renamed formals or replacement provider | Preserve logical formal order/identities, condition/state, footprint, phases and dependencies. Reject weakening or a plain-fnptr erasure of required facts. |
| Standard artifact omits effective Feature 8/10, a selector complement, dependency set, reservation, or matching native role | Reject the missing required representation/support before affected use. Derived prerequisites need no explicit duplicate rows; private success, optional attributes and equal-version prototype bytes do not satisfy standard admission. |
| Selector graph is phase 2, reads an output, has an extra root/call/literal/edge, or pairs an unsigned status with a signed-sign test | Reject this malformed/unsupported selector use. False status is not a postcondition failure; no general graph or frozen-value support is inferred. |
| DF names the wrong record kind, Home, formal or call; input mode 5 is substituted for 6; cancellation occurs after Invoke | Reject structural mismatches; producers/auditors also reject invalid reservation flow. No arbitrary address formation, implicit retirement or state reset. |
| Body/global index endpoints, forward/duplicate/reordered records, multiple outputs and loops | Exercise zero and `0xFFFF` Body indexes, 24-bit global bounds, more than fourteen Homes, exact reuse/generation rules and complete source/record/Block remapping. |

The provisional direction is therefore **explicit vacant aggregate output,
not a resurrected general no-failure API**. Its complete candidate standard
representation is a prerequisite of implementation, not an optional later
assignment; adoption and passing qualification remain distinct from this
unapproved/provisional recommendation.
