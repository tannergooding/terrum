# Terrum function-pointer same-runtime call ABI

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This proposal selects a candidate entry/call and
runtime-domain contract, not normative adoption, compiler support or released ABI
stability. It changes no other document, registry, source or implementation.
The authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Decision

Recommend a **Windows x64 same-runtime, receiver-free, fixed rootless Copy
entry ABI** for default-Terrum `fnptr` invocation. Explicitly adopt the
selected Windows scalar/aggregate carrier, position, preservation and
supporting-storage rules below as this bounded **Terrum-internal** contract.
Do not obtain that contract by treating Terrum as Unmanaged or by taking an
arbitrary ordinary body's first instruction.

The first execution boundary is one admitted runtime instance and jointly
lowered/regenerated code closure. Separately supplied semantic Modules can
participate after binding and joint lowering. Independently replaceable
precompiled providers, different runtime instances and foreign entry are
**not** admitted by this first agreement. They need additional complete
assumption/publication or adapter contracts.

This chooses a useful executable subset, including indirect by-value arguments
and results, without selecting a general managed ABI. It does not narrow the
source definition of `fnptr`. Checked references, initialization destinations,
unique owners, views, tasks and other excluded carriers remain valid source
contracts where their own representation/runtime support is admitted.
Implementation can progress in coherent subsets, but cannot claim this whole
candidate qualified until all its elected carriers and applicable semantics
are covered.

No new Feature, TargetProfile, runtime-policy value, clause, opcode, record,
flag or byte assignment is selected here. Companion assignments cited below
remain their owners' **unapproved candidates**. In particular, existing Windows
target identities do not already assert this new call contract.

## The missing contract and its owners

The [specification index](../specs/README.md) leads to these distinct owners:

| Owner | Established boundary; intended integration if this proposal is adopted |
|---|---|
| [Callable families](../specs/type-families.md#callable-values-and-tuples), [type grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments) | Default `fnptr` convention is Terrum, with no independent environment; `func` is distinct. Preserve those identities and connect eligible execution to an explicitly selected same-runtime entry ABI. |
| [ABI layers](../specs/data-layout-and-abi.md#contract-layers), [independently replaceable boundaries](../specs/data-layout-and-abi.md#private-implementation-versus-independently-replaceable-boundaries) | Source type, storage, target calls and runtime compatibility are different contracts. Add this bounded joint-execution agreement, not a universal or implicitly version-stable ABI. |
| [Windows call interface §2](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults) | Nonforeign defaults expressly do **not** standardize a Terrum-to-Terrum ABI. Add a separately scoped same-runtime subsection adopting the specified subset; preserve that disclaimer for unselected/private calls and independent crossings. |
| [Windows layout](../specs/targets/windows-x64-data-layout.md), [positions/copies](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack), [deployment](../specs/targets/windows-x64-deployment.md) | Reuse exact storage, classification, call-area, preservation, PE/unwind and lifetime obligations where explicitly incorporated below. Do not extend foreign primitive admission or native member-call rules. |
| [Native roles](../specs/native.md#native-declaration-roles), [native lifetime](../specs/native.md#runtime-and-code-lifetime), [JIT](../specs/jit.md) | Distinguish an ordinary Terrum pointer entry from NativeCallable/NativeImport/NativeExport. Preserve native caller-unsafety, foreign containment, code publication and retained lifetime. |
| [Calls and phases](../specs/il-instruction-set.md#calls), [exceptional exits](../specs/language-contracts.md#33-normal-and-exceptional-exits), [local failures](../specs/language/local-failures.md) | Direct and indirect calls have one logical invocation model. Preserve normal publication, actual failure dispatch and explicit cleanup; do not import a native callback's containment rule. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md), [binding](../specs/representation/binding-and-replacement.md) | Retain complete contracts and each Module's governing rules; add use-specific entry/domain compatibility checks. A type anchor or coarse target requirement is not an executable ABI assertion. |
| [Reader trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md), [versioning](../specs/representation/format-versioning.md) | Safely consume the selected exact schema and preserve typed references. Producer truth is not mandatory consumer body re-proof; unpublished draft equality does not imply compatibility. |

This proposal fills the target/runtime decision deliberately left open by
[ordinary Terrum addressability](terrum-function-pointer-addressability.md).
It composes with, rather than replaces:

* [Default-convention encoding](standard-function-pointer-default-convention.md):
  TypeSpec-only candidate `0=Terrum`, not zero in native-role convention domains.
* [Source contracts](standard-function-pointer-source-contracts.md):
  fixed bare defaults, complete inferred contracts and checked projection.
* [Anchor contracts](standard-function-pointer-anchor-contracts.md):
  applicability, normalization, structural identity, selectors and rebasing.
* [Address formation](standard-native-address-formation.md), as generalized by
  the addressability proposal: candidate `method.address`, long `E9`, Body
  record kind `0D`, actual target plus explicit pointer view.
* [Aggregate native outputs](aggregate-native-outputs.md): its candidate
  `DF`/record `0C` reservation protocol remains distinct. An ABI result buffer
  here is neither that operation nor a checked initialization-output formal.
* [Complete private pointer persistence](native-function-pointer-contract-private-persistence.md):
  a separate unapproved lossless-carrier decision, not an already usable codec.

Only the addressability proposal's open first-ABI decision is answered here.
Its broader source eligibility, initialization-trigger recommendation,
unsafe rule and Standard carrier remain provisional in their own right.
The anchor proposal's bounded predicate/capability gates remain gates.

## Alternatives

| Candidate | Assessment |
|---|---|
| **Bounded Windows x64 same-runtime ABI — recommended** | Reuses specified positional carriers and existing direct-call mechanisms while making the indirect-entry agreement explicit. Rootless Copy values avoid choosing stored reference, owner, collector or closure ABIs. Runtime-instance and joint-generation restrictions avoid an unsupported public replacement promise. |
| Only the prototype's narrow scalar/pointer whitelist, no indirect results | A useful first implementation slice, but not a reason to redefine Terrum signatures by C foreign admission or today's host enum. Excluding all aggregates also leaves ordinary aggregate-return entries unusable. The recommended contract supplies their existing target classification now; qualification can proceed incrementally. |
| Universal cross-target or independently replaceable Terrum ABI | Requires additional complete representations, runtime/failure agreement, version selection and native-artifact assumption carriers. Neither a source convention nor one Windows register table supplies them. Not selected. |
| Assume every ordinary direct entry already works | Incorrect: private direct lowering may use hidden Source inputs, result storage, receivers or specialization context. An addressable entry must implement the selected contract, even if an internal body uses another private convention. |
| Use NativeCallable/Unmanaged, add a native wrapper, or retag zero | A deliberate foreign wrapper can be useful, but it exposes the wrapper's foreign contract, not the ordinary target's default-Terrum entry. Coincident carriers do not equate convention identity, failure containment or caller requirements. |
| Keep default pointers type-only, require a foreign convention everywhere, or reject all default invocation | The first accurately describes an unsupported implementation, not completion. The others replace the accepted default/source direction and would need an explicit separate decision. None is the proposed implementation strategy. |

## Execution-domain agreement

“Same-runtime” means a **validated execution agreement**, not the same CPU,
OS, process, Module name, compiler brand or pointer width. Before code or an
address is exposed, the execution provider selects:

1. The exact Windows layout/platform rules and this candidate call mapping.
2. One actual runtime instance, its admitted failure policy and complete
   event/handler/helper protocol, thread-entry conditions and runtime data.
3. Each Module's governing edition, required capabilities and retained
   expected/provider bindings, with all required compatible interpretations.
4. The actual code/helper/unwind/load closure and its publication/lifetime
   obligations.

For the first boundary, the lowerer controls and can regenerate every Terrum
entry/caller in that closure, and binds them to the same runtime implementation
and instance. The runtime's internal event records and helper ABI may remain
private **within that jointly controlled closure**; their actual protocol must
be the one implemented on both sides, not an unspecified future wire format.
This proposal standardizes no independently exchanged exception-object layout.
It chooses ordinary same-runtime dispatch, as specified below, rather than a
new failure argument or result.

Selection is an explicit execution/lowering agreement, out of band from
TRRM type bytes. No schema field is invented to carry it. The implementation
must retain the selected assumptions with the generated closure and check
reuse against them; a producer-authored “accepted” bit or build name is not
proof. If an execution path cannot establish this agreement, it cannot yet
claim this ABI.

A pointer parameter or result transported within that domain must designate
an entry admitted for the same domain when its convention is Terrum. Code
origin and that use obligation survive forwarding. There is no domain cookie,
environment word, per-pointer registry or dynamic scan of machine code.
Known mismatches reject before use. Unsafe fabrication of address bits does
not validate an entry or waive the caller's code/provenance obligations.

The runtime thread must already satisfy the domain's entry conditions.
A raw call does not attach a foreign thread, select a runtime, establish a
collector context or grant cross-thread authority. Ordinary thread
eligibility, publication and handoff remain separately checked.

## Exact entry and signature eligibility

The addressability companion owns source selection: an accessible, concrete
ordinary top-level function or static method, or an exact semantic Import
resolved to one, with a checked implementation and complete closed signature.
Formation uses its proposed existing-prefix `&` rule, expected-type
projection or exact inference; no NativeCallable or new entry attribute is
required. This document adds the following first-runtime eligibility:

| Dimension | Candidate rule |
|---|---|
| Receiver and environment | No MethodSig receiver, bound receiver, interface/witness dispatch environment, closure or `func` environment. Explicit ordinary parameters remain ordinary inputs. |
| Generic callable context | Empty method-generic range and no unresolved declaring-owner formals or dictionary/witness/context requirement. An existing admitted exact closed specialization can qualify only with its closed representation and context fully discharged. A bare method token supplies no specialization recipe. A nongeneric authored wrapper is otherwise required. |
| Parameter modes | Ordinary by-value complete values only. Local parameter `mut` and labels/defaults do not add physical arguments or alter pointer identity. A target with a checked-reference parameter is excluded even when direct source calls offer implicit-reference adaptation. Pointer invocation receives the already selected complete signature, not omitted defaults or call-site adaptation recipes. |
| Admitted values | All core scalars, well-formed raw data/code pointers, explicitly fixed integer-backed enums, and the eligible fixed rootless Copy aggregates/Transparent forms defined below. Every supplied value is initialized and valid for its complete type. |
| Excluded values/modes | Checked `ref<T>`, `write ref<T>`, `ref<init T>`, initialization ranges, owned/unique transfers, managed/rooted handles, `own`/box/view/task/continuation descriptors, erased witnesses, optional/tagged/tuple layouts not assigned by this target, variadics, SIMD and unprototyped calls. A pointer-sized observation is not their ABI. |
| Required entry inputs | **No hidden Source grant, receiver, generic dictionary, witness list, captured environment, user context, runtime-context argument or exception-state argument.** The sole hidden signature-derived call input is result storage when the classification below requires it. |
| Declaration roles | No anchor, abstract/unbound bodyless declaration, constructor, accessor, operator, Cleanup entry, intrinsic or initialization-only helper as an ordinary address target. Native-role declarations keep their separate foreign route; no dual Terrum/native entry is silently added. |
| Body/runtime support | The selected implementation and retained dependency closure need actual runtime, helper, initialization and failure support. A rootless signature is not proof of a context-free entry or supported body. Ordinary bodies may call private helpers only under completely established internal contracts. |

Fixed aggregates are precisely the target's admitted `Blittable Copy`
Sequential/Union records, transitively containing admitted fixed field
representations without roots, owning cleanup or unknown descriptors.
The [layout schemas and placement rules](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
determine extent, alignment, packing and validity. A marker cannot validate
an unsupported field. Raw Union validity/access rules remain unchanged.
An ordinary empty record has the target's minimum storage extent; it is not
unit. An eligible Transparent type recursively inherits its one field's
carrier, including the zero-extent case, while preserving nominal identity
and validity. Frozen integer-backed enums likewise retain their complete
identity and use the underlying carrier. `Copy` does not follow from size.

Raw `ptr<T>` transport passes an address, not a `T` value; it does not demand
that a pointee be passed by value under this ABI. Complete pointer formation,
access, backing and lifetime obligations still apply. It neither flattens a
checked descriptor nor makes pointee storage initialized.

Entry admission must account for **transitive** private requirements.
A function that reads invocation-scoped Source, or calls a helper requiring
that grant without supplying it explicitly, cannot expose this entry.
Do not read Source from invented TLS/global state or capture it in a
trampoline. Conversely, an unrelated Source-dependent function elsewhere in
the program is not grounds to reject an otherwise independent entry.
A signature-derived result pointer is not such a Source/context grant.

Private direct-call specialization can continue. The address must name a
published conforming entry, which can enter a differently lowered private
body through a mechanically correct internal stub. Such a stub may rearrange
derivable carriers; it cannot invent an unavailable grant, capture state,
change the static contract or become a cross-domain adapter.

## Physical call mapping

This is an **explicit candidate adoption** of the
[Windows carrier rules](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults)
for the eligible Terrum entry subset, not an inference from Microsoft's
foreign primitive table.

### Classification and results

| Complete admitted form | One argument carrier | Normal result |
|---|---|---|
| Integer up to 64 bits, nint/nuint, bool, code unit or rune | Low payload bits of integer carrier | Low payload bits of RAX |
| Raw data pointer or plain function-pointer address | Eight-byte integer carrier | RAX |
| float16/bfloat16, float32, float64 | Low 16/32/64 bits of floating carrier | Low 16/32/64 bits of XMM0 |
| int128/uint128 | Pointer to independent 16-byte argument value | Hidden result storage; its address in RAX |
| float128 | Pointer to independent 16-byte argument value | All 128 payload bits of XMM0; **no** hidden result pointer |
| decimal32/decimal64 BID | Same-width integer bit carrier | Low 32/64 bits of RAX |
| decimal128 BID | Pointer to independent 16-byte argument value | Hidden result storage; its address in RAX |
| Ordinary fixed aggregate of extent 1/2/4/8 | Same-width integer bit carrier | Low same-width bits of RAX |
| Other nonzero ordinary fixed aggregate | Pointer to independent argument copy | Hidden result storage; its address in RAX |
| Exact unit or eligible zero-extent Transparent carrier | No payload carrier | No payload carrier; ordinary normal completion remains |
| Eligible enum/Transparent wrapper | Recursively classify its final carrier | Same inherited result classification |

No argument is split across registers. A one-float ordinary record uses an
integer carrier; a Transparent float wrapper uses the floating carrier.
Decimal is BID, not CLR decimal. Half/bfloat are not widened to binary32;
float128 is not Windows `long double`. Integer128 does not return through
RDX:RAX. The lack of formal foreign primitive equivalents for these types
does not exclude them from this explicitly selected **nonforeign** contract.
There is no C default-argument promotion.

Only the payload bits are meaningful. Upper register and stack-slot bits
are unspecified, not promised sign/zero extension or initialized padding.
Typed identity/copy transfer preserves required bits, including signed zero,
signaling NaNs, payloads and decimal cohorts/noncanonical containers.
Arithmetic and validation retain their own rules; transfer is not arithmetic.
Invalid bool/rune/nominal inputs are not normalized into valid values.

### Physical positions and call area

Construct a single physical sequence:

1. The hidden result pointer, exactly when the table requires it.
2. Each nonzero-payload ordinary formal in declaration order.

No receiver or other hidden entry input intervenes. Unit formals retain
their logical ordinals, argument evaluation, initialization and contract
subjects but consume **no** physical position. The hidden result pointer is
not a logical formal and changes none of those ordinals.

| Zero-based physical position | Integer/pointer carrier | Floating carrier |
|---|---|---|
| 0 | RCX | XMM0 |
| 1 | RDX | XMM1 |
| 2 | R8 | XMM2 |
| 3 | R9 | XMM3 |
| 4 and later | Eight-byte stack slot | Eight-byte stack slot |

Integer and floating registers share this ordinal; do not count them
independently. The unused member of a pair contains no additional argument.
At callee entry, position 4 is `[entryRSP + 40]`, then eight bytes per
position; at the caller immediately before CALL it is `[RSP + 32]`.
Narrow payload occupies the low-address bytes; an indirect argument occupies
one pointer slot.

For `N` physical arguments reserve at least
`32 + 8 * max(0, N - 4)` bytes of outgoing space, with checked extent and
additional frame/alignment storage as required. Even zero-argument calls
reserve the 32-byte shadow area. RSP is 16-byte aligned immediately before
CALL; entryRSP is eight modulo 16. The callee owns the home area during the
call; no caller value may remain there across a nested call. There is no
red zone. Guard/probe the complete new stack extent before access under
the platform rules. No prototype frame or register-window ceiling becomes
a language arity or ABI aggregate-size limit.

**Classification examples, not executable source evidence:** logical inputs
`(void, uint8, float64, void, int32, float32, uint64)` with an int32 result
use RCX/XMM1/R8/XMM3/stack. With an indirect record result they use hidden
RCX, then RDX/XMM2/R9/stack/stack. With a float128 result they use the
first sequence and return through all of XMM0. Unit omission is physical,
not signature equivalence.

Capture the target once before staging source arguments once in written
order. Preserve each prior value snapshot, raw-pointer/code origin and
supporting storage through later nested calls. Then map completed values to
formal order and physical positions; labels do not reorder evaluation.
Failure before Invoke prevents entry and performs only the caller's required
staging/cleanup actions. A backend register choice for the indirect target
does not change these rules.

### Copies, hidden results and publication

Indirect arguments use independent exact-extent by-value copies with at
least 16-byte ABI alignment. They are not loans of the caller's original
place, and the callee cannot retain an ABI temporary merely because it
received a pointer. An over-aligned object needs correctly aligned local
materialization before stronger alignment is used; incoming argument backing
does not promise alignment above 16. Supporting storage cannot duplicate
ownership, expose uninitialized padding or silently allocate a hidden heap
object.

An indirect result uses separate, initially unpublished caller-prepared
storage of the complete result's exact extent and **full object alignment**.
Its pointer is physical position zero and the callee returns that exact
pointer in RAX on normal completion. Caller and callee use no status bit,
second result register, result-reference descriptor or implicit output formal.
The caller reads/publishes the complete value only after normal completion,
including the required body cleanup and postcondition phases. Partial writes,
an unchanged RAX or a failure do not publish a result.

Result and argument supporting storage remain independent unless a checked
optimization preserves all ABI, alias, identity, lifetime and publication
observations. The callee cannot retain result storage as an undeclared source.
Recursion/reentry uses distinct backing; static scratch storage is not a
conforming substitute. A DoesNotReturn view retains classification from its
declared result type, including a required hidden result pointer, but has
no normal result publication or continuation.

### Preservation and frames

Adopt [Windows deployment §1](../specs/targets/windows-x64-deployment.md#1-register-and-control-state-preservation):
RBX, RBP, RDI, RSI, RSP and R12..R15 are nonvolatile;
XMM6..XMM15 preserve their low 128 bits. RAX, RCX, RDX, R8..R11 and
XMM0..XMM5 are volatile. Optional upper-vector/additional-register state
follows the exact imported ISA/OS contract, not an assumed Vectorcall ABI.
DF is clear on entry/exit and before runtime/platform calls.

The x87 control word and MXCSR bits 6..15 are nonvolatile; MXCSR status
bits 0..5 are volatile. Use the specified ordinary call environment:
nearest/ties-even, masked exceptions, DAZ/FTZ off. Restore modified
nonvolatile state on normal return and admitted unwind. No x87/MMX payload
return is introduced, and copied floating bits are not canonicalized to
satisfy environment requirements.

Use actual describable prologs/epilogs, allocated stack and accurate
RUNTIME_FUNCTION/UNWIND_INFO under
[Windows code/unwind publication](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support).
The leaf exception applies only to actual eligible leaves. A conforming
entry or internal stub does not get a private stack-walk exemption.

## Failure protocol and runtime policy

An indirect Terrum call uses the **same selected runtime failure path as
an ordinary direct Terrum call in that domain**. It is not a reverse foreign
boundary:

* Normal return alone carries the declared result and normal Publish.
  Preconditions, body operations, explicit cleanup/finally and postconditions
  retain their ordinary phases and ordering.
* An ordinary failure raises the runtime's actual event and dispatches through
  its shared language handler/personality machinery over Windows x64 frames.
  Windows unwind records recover frame/control state; they are not the
  language exception object's representation. No extra argument, normal
  status/result, exception-bit register or per-pointer catch thunk transports
  the event.
* Source handlers and structured propagation observe the same event identity,
  replacement/causal relations and explicit cleanup obligations as direct
  calls. Exceptional completion publishes no result or fabricated output,
  rolls back no source effects and adds no blanket destructor/owner cleanup
  pass beyond the selected language contracts.
* An unhandled failure reaches the admitted outer runtime/host boundary and
  fails fast under its contract. Explicit failfast/process termination gives
  no ordinary unwind or cleanup promise. Fatal faults and excluded forced
  unwinds are not ordinary normal-return failures.

Preserve Standard
[`runtimePolicy=0`](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics):
**fail fast for unhandled failures**, not “terminate every checked failure.”
Explicit unsafe source catches need no policy opt-in. This proposal assigns
no alternate Standard policy and does not make `DoesNotReturn`, unsafe,
NativeCallable or an Effects bit select an unwinder.

The runtime event/helper representation stays private only because the
first boundary has one actual runtime instance and jointly controlled
code. Admission must bind the actual creation/raise/dispatch/cleanup helpers
and their event/handler protocol consistently, including any runtime-owned
roots and data. A runtime lacking a retained exception-object, handler,
cause or cleanup capability cannot claim that program supported. It cannot
replace a catchable failure with failfast, invent an event or accept an
unimplemented protocol on the strength of the register mapping.

The present implementation's explicit
[`FailFast` and bounded `CheckedExceptions` experiments](../implementation/native.md#runtime-policy)
are narrower runtime selections, not two encodings of Standard policy zero.
An initial **experimental** pointer implementation can reuse either selected
private policy exactly as its direct calls do, while preserving the explicit
selection and reporting the narrower support. In bounded checked mode its
existing owned-event filter/unwind protocol must be shared, not confused
with arbitrary native SEH. Such qualification does not complete the accepted
general exception contract or Standard handler carriers. No new exception
code/packet layout is allocated by this proposal.

Internal Terrum propagation does not require the separate native-inbound
`NativeExceptionUnwind` feature merely because Windows unwinding is used.
A body that actually makes an admitted foreign call retains that call's
caller-unsafety and selected foreign exception support. Foreign faults use
the [native normalization contract](../specs/targets/windows-x64.md#6-native-exception-unwinding-and-normalization)
only when admitted; Terrum exceptions still cannot escape through a native
caller. NativeCallable containment and the
[paired foreign failure bridge](../specs/runtime/foreign-failure-bridge.md)
remain distinct routes, not automatic adapters for this ABI.

## Invocation facts, nesting and lifetime

| Fact | Required handling |
|---|---|
| Effects | Preserve the complete selected view, including DoesNotReturn and declared caller-unsafe. The addressability/source-contract proposals' raw unsafe gate is independent; do not insert Effects `04` into a safe ordinary declaration or make its direct calls unsafe. A declaration's unsafe requirement does not authorize its body. |
| No-return | Formation has normal pointer-valued continuation. Invocation retaining DoesNotReturn has no normal edge for any declared result type; it must not read/publish return storage. A returning implementation violates the published promise. An optional defensive terminal trap is not a fabricated unit/result or consumer body proof. Checked weakening of that guarantee changes the view, not convention or address. |
| OutputState | This first outer ABI has no checked init-reference/range formals, so it admits no such output contract. A well-formed richer signature is unsupported here, not converted to raw pointers. An OutputState attached to an ineligible raw-pointer formal is invalid under its owner, not a reason to drop the row. The hidden result buffer is not an OutputState destination. |
| Dependency | Preserve every well-formed applicable whole-source/result/retained-formal obligation and explicit empty set, including phase, source-storage borrowing versus backing forwarding, validity and invalidation. Phases requiring deferred completion, a receiver, checked output or an excluded representation are not activated by this subset. Required facts must have complete admitted carriers; no contract is erased because machine data is Copy. |
| Pointer cells and pointees | Returning/copying a raw pointer publishes only its pointer value under its actual backing/access obligations. It establishes neither pointee initialization nor an owning transfer. Initializing a code-pointer cell is not keeping its code alive. |

Use the source/anchor companions as the owners of defaults, exact identity and
directed projection. The backend must use the recorded static view, including
source-argument correspondence, not recover a convenient contract from a
recognized target body. Mutable cells remain invariant. A required invocation
fact outside the admitted anchor/codec remains a support gate, even for
DoesNotReturn.

A nested function pointer is one eight-byte code-address carrier; its
signature is not recursively flattened into the enclosing call. Retain its
own exact convention and complete contracts through aliases, fields,
substitution and results. An outer Terrum call transporting a Cdecl or
Vectorcall pointer does not invoke it, change its identity or provide its
later call mapping. Transport requires the value/contract support actually
retained; a retained nested invocation additionally needs that convention's
full target support. Structural signature/anchor cycles reject under the
anchor owner; nominal pointer recursion does not unfold pointee storage.

Terrum, Unmanaged, Cdecl, Stdcall and Vectorcall remain distinct. The admitted
Windows C-style Unmanaged/Cdecl/Stdcall scalar carriers can coincide with
this candidate; that coincidence grants no conversion, native role or
compatible replacement. NativeCallable instance/member `this` and hidden
result placement follow their separate target mapping, not this receiver-free
sequence. Vectorcall is not an alias for the positional table.

Code pointers occupy eight bytes with alignment eight in storage; an entry
need not be eight-byte instruction-aligned. Formation yields a usable
published code entry, never an anchor, row number, IAT/data cell or unresolved
placeholder. Complete fixups, write/execute separation, instruction-cache
publication and JIT function-table registration before exposure. No lazy
resolver/compilation failure protocol is introduced; preparation cannot
move source-visible initialization/failure effects across their specified
source position.

Preserve actual definition/load origin, runtime instance, entry/body/helper
and unwind dependencies through storage, copying, joins, forwarding Modules
and results. Equal structural anchors do not merge targets or load instances.
There is no implicit root, retain or unload lease attached to a raw pointer.
Code, stubs, helpers, runtime data and unwind backing remain available through
every permitted use. Failed unwind deregistration prevents release.
No live pointer is repaired by moving code or rebinding an Import.
Replacement does not retarget existing values; no hot-unload/patch protocol
is selected.

## Modules, metadata and implementation obligations

### Selection and compatibility

| Boundary | Candidate disposition |
|---|---|
| One Module or multiple Modules lowered together | Admit the exact runtime/entry agreement; same-Module membership alone is insufficient. Preserve conforming escaped entries despite private direct-call optimization. |
| Separately supplied semantic `.trrm` dependencies | Each retains its exact schema, governing edition, manifest, nominal identities and expected bindings. Resolve contracts in their defining contexts, then lower the compatible closed closure into one runtime domain. This is not an unchanged precompiled crossing. |
| Portable semantic input | A zero coarse target requirement does not itself select a machine ABI. The execution provider must explicitly choose and implement this mapping while preserving every retained observation; target-dependent input retains its actual exact layout requirements. |
| Windows-targeted input | Coarse `targetRequirement=1` and existing Windows layout TargetProfile `(1,1)` do not attest this new Terrum-entry ABI, runtime instance or entry eligibility. Use them only for their existing meanings; record the additional joint-execution agreement separately. |
| Different editions/rules | Neither require equality unnecessarily nor take the newest edition. Admit each exact interpretation or an explicitly supported directed compatibility mapping, and compare the actual boundary contracts. |
| Independently replaceable precompiled code | Excluded from this first boundary. A future complete contract must publish/version and bind exact ABI, layout, runtime/policy/helper, entry and retained lifetime assumptions. Same exports, equal source signatures, private revision or successful PE load are insufficient. Regenerate the whole eligible closure instead, or decline that crossing. |
| Foreign/different-runtime entry | Excluded. An explicitly authored native wrapper or separately selected complete adapter is required; default convention is not an implicit callback/export ABI. |

No complete public precompiled-assumption carrier is chosen here. That is a
remaining decision for expanding this boundary, not permission to hide it in
Attr rows, reserved bits, Feature lists or a TypeSpec convention byte.
Per-Module manifests and before-use retained dependency closure remain
mandatory even where every native image is generated together.

### Source to Standard and provider binding

1. Bind/verify an actual ordinary eligible target, lexical raw unsafe
   authorization and a complete selected static pointer view. Apply the
   addressability companion's static-initialization trigger and ordinary
   invocation initialization obligations; taking an address does not execute
   the target or its preconditions.
2. Produce the candidate seven-byte Standard TypeSpec `0D` with TypeSpec-only
   Terrum zero and the source/anchor companions' complete noncallable
   contract anchor. Emit no NativeCallable `000C` on the anchor or ordinary
   target and do not extend native-role convention domains.
3. Use the generalized candidate `E9`/Body-kind `0D` address record unchanged
   in shape: actual MethodMap target, explicit pointer TypeMap view, valid
   register/vacant-Home destination and zero reserved fields. Its Body
   record namespace is distinct from TypeSpec `0D`. It composes with the
   aggregate-output candidate's separate `DF`/record `0C`; no discriminator,
   collision, alias or new address opcode is needed.
4. Execute through existing signature-aware `call.value` forms, using a
   general CallSite where the logical staging/preparation requires it.
   Logical unit inputs stay in the signature/maps. Address formation is not
   a zero-argument call; a unit-returning target still produces a nonunit
   code pointer when addressed.
5. Bind an address-only Import's exact provider/key against **every retained
   expected declaration**, then additionally require its eligible conforming
   Terrum entry and domain support. A bodyless expectation is a proxy, not an
   entry; the structural anchor is neither. An ordinary provider cannot
   satisfy this use by supplying a foreign entry with equal registers.

The companion proposals recommend broadening existing Features `1/1`,
`2/1` and `4/1`, retaining their `4 -> 3 -> 2 -> 1` closure for identity,
values, formation and invocation. That composition remains their **candidate
scope amendment**, not an allocation or approval made here. Retained
selectors, layout queries, generics, failures and other semantics retain
their independently applicable complete capability/carrier gates. A
recognized opcode, inferred anchor or unused/discarded address does not
discharge any retained requirement.

Current-schema readers must not reinterpret zero/`E9`/record `0D` as this
combined candidate solely because format bytes or Feature revisions match.
Select the whole exact amended schema and semantic catalog. Development
operation `31`, six-byte pointer rows and private signature sections are not
Standard alternatives or fallback decoders.

### Shared rewriting, persistence and lowering

Carry entry eligibility/requirements, complete pointer contracts and actual
code origins through shared bound traversal/rewriting, generic substitution,
dependency materialization and mandatory verification. Recompute the
physical entry/call plan after valid substitution; do not reuse a plan whose
logical arity, payload classification or hidden-result decision changed.
Do not rematch an ordinary target through a NativeCallable-only matcher.

Packing/remapping must rebuild decoded references: MethodMap/Import targets;
TypeMap and nested TypeSpecs; anchor, MethodSig/Param and Contract ownership
and ranges; selector subjects; Home/record indexes; formal/result source
correspondence and all original expectations. Preserve defining Module
contexts, domain assumptions and address-only origin edges. Rebuild Body
map masks/counts, block/record/code offsets, instruction ordinals, section
extents and affected SourceMap/debug slots. Anchor deduplication is not
target deduplication. No byte substitution or equal-carrier interning can
perform this transformation.

Private persistence must be lossless for the retained contract and
addressability requirements under its explicitly selected schema.
Revision-92/Development rejection of nondefault invocation facts is currently
a necessary safeguard, not completion of their transport. Never serialize
process addresses as integer constants or quietly round-trip only a signature
triple. A changed private codec needs its own selected revision/design; this
proposal assigns none.

AOT emits real entry relocations/fixups and all required runtime/unwind
dependencies. JIT publishes the same admitted entry ABI and registers its
function tables before an address becomes usable. Both use the complete
static call plan, including unit omission, indirect inputs/results, failure
and dependency/publication obligations. Neither can replace an unsupported
entry with a foreign thunk, hidden context, heap box, pin, retain or success
default. Identifying a local target does not license devirtualization that
loses its view or retained expectations.

### Malformed and unsupported input

Consumers safely validate exact selected schema/framing, checked count/extent
arithmetic, controls/reserved fields, map/token namespaces and bounds,
result/Home roles, complete anchor/signature/contracts, owner/range/cycle
rules, selected bindings, capability closure and runtime/entry assumptions
before affected lowering or execution. Reject an anchor as an address target,
an unresolved proxy, forbidden receiver/context, inconsistent classification
or incompatible domain. Malformed structures, unsupported valid signatures,
resource exhaustion and provider incompatibility are distinct conditions.

Source producers prove authored eligibility, effects, flow and backing facts.
Consumers may rely on truthful published body facts after mandatory
safe-consumption/support/binding checks; they need not reconstruct unsafe
authorization from SourceMap, re-prove arbitrary provider bodies or detect
all forged semantic promises. A structurally consumable false contract
remains a producer violation and can cause undefined behavior.

## Current evidence and support gates

The checked implementation is a starting mechanism, not this contract:

* [NativeFunctionPointerContract](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs)
  recognizes internal `Native=0`, but its target matcher requires the
  native-pointer declaration path and caller-unsafety. Its value whitelist
  is narrower than the candidate; in-memory Effects/Dependencies do not
  establish lossless persistence or all OutputState support.
* [NativeEmitter](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
  admits internal Native/Unmanaged/Cdecl/Stdcall and stages indirect scalar
  calls with shared x64 positions. Its indirect path is not a general
  signature-derived aggregate/result plan. Direct entry/call lowering also
  computes hidden result inputs and two Source inputs; the
  [Source classifier](../../sources/Terrum.CodeAnalysis/Native/NativeSourceGrantClassifier.cs)
  follows transitive requirements. These facts prohibit assuming every
  ordinary BoundFunction address has the indirect caller's shape.
* [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) and
  [pointer tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs)
  exercise native-role/legacy `NativeAbi` formation and prototype failure
  restrictions. The [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
  does not exclude that suite; its native cases have Windows x64 gates.
  They do not establish this ordinary-entry/default-convention route.
* [ModuleCompiler](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
  private revision 92 and
  [Development executable production](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
  reject nondefault pointer invocation facts rather than lose them.
  Standard end-to-end entry/contract transport still needs its selected
  companion schemas and implementation.

The supplied Release fast pointer/no-return and active TRRM test results are
related evidence, not qualification of this new ABI. They neither supply a
clean analyzer-enabled Release checkpoint nor runtime evidence for the newly
added DoesNotReturn address path. This documentation change runs no build or
native program and makes no wider support claim.

Before claiming the candidate supported, complete these gates:

1. Select the combined provisional semantic/schema agreement and actual
   runtime domain; preserve Standard unhandled-failure policy separately from
   private experimental policies.
2. Implement ordinary entry admission, including transitive hidden-input
   exclusion, and complete static contract propagation/verification.
3. Implement lossless Standard and affected private/dependency transport,
   provider binding, rewriting and retained origins; do not leave the
   producer permanently Development-only.
4. Implement caller/callee plans for the full elected carrier table,
   supporting storage, unit mapping, preservation and actual runtime failure
   paths. Track unimplemented wide/floating/layout/helper cases explicitly.
5. Qualify those final inputs with independent structural, binding and native
   execution cases below. A bounded scalar implementation is progress, not
   full-candidate or general callable acceptance.

## Proposed acceptance cases

These are required tests, **not executed results**. Use public compiler APIs,
unrelated synthetic functions/providers and independently constructed
source-free artifacts. Separate syntax, binding/verification, Standard
serialization, PE inspection and supervised execution evidence.

| Case | Required observations |
|---|---|
| TCA-01 Ordinary producer/consumer | Unannotated top-level/static methods form default pointers, store/pass/return them and execute under the selected domain. No NativeCallable, NativeImport or OS export is manufactured. Different functions sharing one anchor execute their actual selected targets. |
| TCA-02 Logical/physical positions | Zero arguments, unit-only and interspersed unit inputs/results; mixed integer/floating inputs at positions 0..5 and beyond compact windows. Preserve logical arity, source-order effects and once-captured target while observing the exact shared registers/stack slots and zero-argument shadow space. |
| TCA-03 Scalar bits/results | Exercise every elected scalar carrier, dirty unused upper bits, narrow signed values, nint/nuint, signed zero, signaling/quiet NaN payloads, half/bfloat bits and BID cohorts. Test bool/rune validity at checked production/adaptation, not by requiring detection of every false unsafe promise. Float128 returns all XMM0 bits without sret; int128/decimal128 use sret, never a guessed foreign primitive ABI. |
| TCA-04 Aggregates/alignment | Extents 1/2/4/8 versus 3/5/16 and larger; empty records versus unit/zero-extent Transparent; one-float record versus Transparent float; frozen enums, packed fields and over-alignment including 32 bytes. Assert exact-extent independent copies, 16-byte incoming ABI backing and correctly aligned object/result materialization without overreads. |
| TCA-05 Hidden result/order | Combine sret with unit and mixed inputs past four physical positions. Verify hidden RCX, shifted payload positions, exact RAX pointer and complete-only publication after cleanup/postconditions. Reentry/recursion must not share result backing. |
| TCA-06 Hidden requirements | Reject direct and transitive Source-dependent entries, unresolved dictionaries, receivers and contexts. An unrelated Source user must not block an independent eligible entry. Reject constructor/accessor/cleanup/anchor/unbound targets; admit an independently eligible nongeneric static wrapper with explicit ordinary inputs. |
| TCA-07 Effects/no-return | Safe direct calls remain safe, raw formation/invocation require their separate authorization, declared Effects survive projection. Forming a non-unit DoesNotReturn address continues normally; invoking it reaches the actual failure/nonreturning path with no Publish. Include indirect-result classification and a weakened nonterminal view. Do not count emission alone as runtime proof. |
| TCA-08 Failure/cleanup | Compare direct and indirect checked failure, outer unsafe recovery, finally, postcondition failure and staged-argument failure with the same actual runtime. Observe event/cleanup/order and no result publication. Exercise private FailFast/CheckedExceptions only as explicitly selected narrower experiments; reject policy/protocol mismatches and never reinterpret them as Standard zero. |
| TCA-09 Excluded outputs/owners | Preserve a well-formed richer checked-output signature through supported identity transport, but decline its execution under this subset. Reject OutputState on a raw-pointer formal. Never erase rows, treat sret as a formal init output, fabricate ownership or let DoesNotReturn bypass required carriers. |
| TCA-10 Dependencies/nesting | Round-trip exact/empty/backing-forwarding dependencies and retained origins through locals, fields, joins, calls and results. Reject lost retention or leaked ABI temporary backing. Nested mixed conventions retain their identities; outer transport does not flatten or admit an unsupported nested invocation. |
| TCA-11 Native distinction | Equal-carrier Terrum/Unmanaged/Cdecl/Stdcall types stay unequal and cannot retag/replace each other. Preserve native-import/callback unsafe and containment rules. Member-receiver and Vectorcall mappings are never accidentally routed through this table. |
| TCA-12 Preservation/unwind | Independent caller/callee probes inspect nonvolatile registers, low XMM6..15, RSP/call alignment, DF and nonvolatile floating controls on normal and admitted unwind paths. Validate PE and dynamic function tables, prolog/epilog points, stack probing and deregistration lifetime. A native harness observes physical mapping, not source convention equality. |
| TCA-13 Module/domain binding | Source-free separately supplied Modules lower into one admitted domain with retained editions/manifests/expectations. Address-only Imports retain providers. Same-shaped different-runtime, incompatible-policy/helper or unchanged independently precompiled crossings decline before use; a PE load is not admission. |
| TCA-14 Remapping/lifetime | Permute/deduplicate anchors/maps and pack Modules without merging targets or losing contracts, selectors, source slots or code origins. Forward through an intermediate Module. Keep old escaped entries/helpers/unwind backing valid across permitted servicing; no silent pointer patching or early unload. |
| TCA-15 Structure/capacity | Independently reject wrong token/map kinds, record extents/controls, reserved bits, result/Home states, anchor roles/cycles, invalid contract subjects and missing required references/closure. Exercise local indexes 0/65,535 and counts 65,536, one-past limits, typed global-row high bytes and checked stack/layout extents; distinguish exhaustion from malformed input. |
| TCA-16 Exact persistence | Exercise source-to-selected-Standard, independent loading, dependency binding, private transport and actual `call.value` lowering. Reject current-schema zero/`E9` and Development/private shapes under the wrong selected schema. Required nondefault facts cannot disappear into revision-92 or signature-only success. |
| TCA-17 AOT/JIT and support claims | Execute the admitted route in both strategies with the same expected behavior, including the new no-return address path. Record final compiler/input identity, configuration, selected runtime, assertions and actual test counts/skips. An analyzer failure, gated/empty selection, timeout, emitted image or passing structural suite is not runtime/candidate acceptance. |

Full typed-failure/handler/owner/root features retain their own acceptance
gates; this proposal does not claim to finish them through these tests.
When required capability support is absent, decline the specific crossing
with the missing requirement. Implement and qualify supported ordinary
default-pointer formation and invocation rather than making a blanket
diagnostic, foreign retagging or permanent Standard exclusion the completion
strategy.

Approval of this candidate, completion of its implementation and qualification,
and release of an independently stable ABI are three separate decisions.
The remaining design expansion is an explicit precompiled/runtime-assumption
carrier or additional signature/runtime families, not a guessed register
convention needed to begin this bounded same-runtime route.
