# Standard TRRM register-Body exception regions and failure transport

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract direction.** This proposal selects a candidate, not normative
adoption, compiler support, runtime qualification or a released schema.
[Proposal index](README.md).

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction. I will go and appropriately review/approve those proposals
at a future point; this allows you to continue work without being blocked.
Do not invent compatibility aliases, fallbacks or encodings to avoid the
decision.”

## Decision and scope

Recommend **E1**, one generic register-Body exception-region contract:

- Complete existing `B6`/`EA` RegionExit, amending record `0A` with an explicit
  completion-value selector and the four exit kinds below.
- Add **Body record `0F=ExceptionDirectory`**, containing Body-local region,
  ordered handler and Block-owner tables. No global exception section, event-ID
  table, exceptional successor edge or per-site state snapshot is added.
- Select **Feature `21/1=RegisterExceptionRegions`**, governing edition `1`,
  with **no unconditional Feature prerequisites**. Its complete scope includes
  fresh/active event transport, exception-specific catch evidence, incremental
  dispatch, structured finally continuations and preservation of actual state.
  Required payload, operation, cleanup, target and runtime support remains
  use-dependent.
- Select exactly **`trrm/1-draft-register-exception-regions-1`**, composition
  **B + E1**, as defined below. This is the generic baseline register-Body
  exception profile; it does not select F, S, fixed arrays, unit-array
  initialization, or any other nonbaseline feature. Add no other implicitly
  selectable composition and do not widen any existing selector.
- Preserve **Standard `runtimePolicy=0`**, fail-fast for *unhandled* failures.
  Explicit catches already require no new runtime-policy opt-in. Add no
  `CheckedExceptions` wire value and make native unwind Feature 7 independent.

This decision supplies routing and event transport, not new source syntax,
normal Cleanup semantics, a general managed-object ABI, arbitrary existential
operations, captured-owner creation, asynchronous frames, generic type-pattern
capture, foreign exception interchange or private persistence. A composition
with another profile requires its own exact schema identity and complete
selection; no powerset or feature-sniffing rule is implied. No compiler
implementation or normative document is changed by authoring it.

## Verified baseline and preserved owners

The [specification index](../specs/README.md) reaches the following owners.
The amendments later in this document are explicitly candidate amendments.

| Owner | Verified baseline and preserved contract |
|---|---|
| [Instruction records](../specs/il-instruction-set.md#typed-operand-records), [short forms](../specs/il-instruction-set.md#assigned-short-forms), [long families](../specs/il-instruction-set.md#assigned-long-families) | `B4`/`E8` reference Throw `09`; `B6`/`EA` reference RegionExit `0A`. Current `0A` is `u8 exitKind, u8 hasTarget, u16 targetBlock, u32 regionCount`, then ordered inner-to-outer `u16` region indexes. No exit-kind values, ExceptionRegion inventory, handler/filter/finally rows or event carrier are assigned. Active-handler rethrow and handler/cleanup execution remain gated. |
| [Machine registry](../specs/representation/trrm-1.registry.json), [registry authority](../specs/representation/registry.md#1-authority-and-schema-identity) and [instruction allocation](../specs/representation/registry.md#2-instruction-allocation) | B6/EA are shape-only, `RegionExitKind` is empty, and Throw/event/handler/cleanup carriers are explicitly unsupported. Recognized bytes do not complete their prerequisites. |
| [Local failures](../specs/language/local-failures.md#2-object-identity-storage-and-lifetime), [throw](../specs/language/local-failures.md#3-fresh-throw-and-propagation-responsibility), [catches](../specs/language/local-failures.md#4-catch-subjects-and-bindings), [selection](../specs/language/local-failures.md#5-selection-recovery-and-propagation) | An ordinary exception object is the event. Preserve exact normalized self/witness identity, object-handle bindings, same-object origin/parent preservation, distinct-event replacement and separate authored InnerException. Exact Core UnreachableException is the sole selected Copy-value payload. |
| [Normal phases](../specs/language-contracts.md#31-the-normal-sequence), [saved results](../specs/language-contracts.md#32-saved-results-and-cleanup), [normal and exceptional exits](../specs/language-contracts.md#33-normal-and-exceptional-exits), [incremental selection](../specs/language.md#safe-and-unsafe-boundaries) | Stage/evaluate once in source order. Complete body/finally/local and untransferred owned-parameter cleanup before postconditions; an escaping failure runs no ensure. Unwind exited inner scopes before an outer scope's type tests/filters. Catch fallthrough propagates; explicit non-failure transfer leaving the catch swallows. Recovery is unsafe and certifies no restored state. |
| [Ownership and cleanup](../specs/language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), [failed construction](../specs/language/ownership-and-cleanup.md#construction-and-publication) | Normal complete retirement runs authored Cleanup then reverse-declaration field retirement. Failed unpublished construction retires outstanding acquisitions in reverse actual acquisition order, without incomplete-parent Cleanup. Registered compiler-invoked Cleanup alone creates no exceptional-unwind cleanup. Preserve actual holders, partial/vacant/live state and no cleanup replay. |
| [Standard failures](../specs/runtime/standard-failures.md#1-exact-core-identities-and-construction), [postconditions](../specs/runtime/standard-failures.md#2-runtime-postconditions), [allocation](../specs/runtime/standard-failures.md#4-recoverable-allocation-exhaustion), [unreachable](../specs/runtime/standard-failures.md#6-executed-unreachable) | Preserve exact categories, actual preparation failure, no normally published result on failure, ordinary versus private/reserved output state, and the operation-specific recoverable-publication admission. Neither this Feature nor OutOfMemoryException promises universal recoverable OOM. Unreachable is a fresh checked event, not failfast or undefined behavior. |
| [Logical failure records](../specs/representation/logical-records.md#local-failure-eligibility-and-catch-inputs-encoding-pending), [handler records](../specs/representation/logical-records.md#pattern-evidence-and-handler-selection-encoding-pending), [owning records](../specs/representation/logical-records.md#owning-fields-and-cleanup-encoding-pending) | Required logical identities, ordinary code regions, handler order, state and responsibility survive separate compilation. Their current physical gates cannot be filled by debug data or a guessed private codec. E1 fills the region/event gate, not every adjacent declaration/value/owner gate. |
| [Consumer verification](../specs/representation/reader-trust.md), [binding/replacement](../specs/representation/binding-and-replacement.md), [canonicality](../specs/representation/encoding-canonicality.md), [admission](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md) | Producers publish truthful facts. Consumers perform structural safe-consumption, support and directed binding checks before use, not mandatory arbitrary Body truth proofs. Preserve Module-owned requirements and original expectations. |
| [Format identity](../specs/representation/format-versioning.md), [capacity](../specs/representation/resource-accounting.md) | One artifact selects one exact schema/catalog out of band. Equal `(1,0)` bytes select nothing. Unpublished schemas need no aliases/readers/converters; numeric draft assignments are mutable. Encoded capacities are not portable resource minima. |

The generic [IL-extension proposal](il-extensions.md#remaining-body-and-runtime-questions)
already names this region/event gap. The
[unit-conditional proposal](unit-valued-conditional-expressions.md#fixed-array-capture-and-body-consequences)
preserves selected-branch failures and cleanup and explicitly leaves
CheckedExceptions/general failure transport open. Its A2EU observations are
not a contradictory conformance claim.

Direct inspection of
[`TrRmExecutableCompiler.NativeAddress.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs)
shows the current executable producer requires `BoundRuntimePolicy.FailFast`.
The [reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs)
reconstructs that policy. The active
[`TrRmForeignInvocationAddressEmitterTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs)
select FailFast for the A2EU source-free JIT/AOT cases. These observations and
the [implementation inventory](../implementation.md) establish a current
support boundary, not E1 implementation or newly run validation. The A2EU
profile is not part of this candidate and remains unchanged; its composition
with E1 is not selected here.

## Alternatives and allocation selection

| Direction | Assessment |
|---|---|
| **Amend RegionExit and add one Body-owned directory — selected** | Retains the assigned terminal families and ordinary code blocks. One typed inventory supplies lexical membership, ordered dispatch and continuation ownership without new operation bytes or a second CFG value model. |
| Keep the eight-byte RegionExit payload and dedicate R0 to filter results | Possible in a new exact schema, but adds an implicit Boolean role unrelated to the existing signature-aware return. Select the extra four-byte typed selector instead: both register and Home results are explicit and no unrelated R0 binding is read. |
| Extend the Body prefix/map mask and insert separate region/handler tables | Could avoid an empty directory in handler-free methods. It changes common prefix arithmetic, mask/count interpretation and table positioning. Select a fixed record-zero anchor instead; the cost is a 34-byte minimum directory plus its ordinary offset-index entry in a stream Body. |
| Global ExceptionRegion/Handler sections, for example `005F/0060` | Noncolliding candidates could be chosen, or draft sections could be repacked. They add artifact-wide ownership/remapping and still need a Body membership carrier. There is no need for wider cross-Body region identities or shared handlers here; select no global section. |
| Explicit failure CFG edges and an event register/result tuple at every fallible operation | Coherent only as a larger revision of calls, register carriers, merges and publication. It risks treating exceptional completion as a normal successor/result and is unnecessary for instruction-address-driven dispatch. |
| Error/status substitution, a native hidden failure argument, or a private-format fallback | Not the selected contract. These alter source outcomes or boundary ABI, or lose independently consumable semantics. A backend's internal implementation is not an alternate Standard encoding. |
| Repack `0C..0F` or allocate an earlier Feature by moving other candidates | Permitted before release, but gives no width/capacity advantage or cleaner semantic family. Select the next coherent Body kind and independent Feature, not preservation of unpublished numbers as an obligation. |

The allocation review considers the registry and proposals as one namespace:

| Namespace | Candidate choice and interactions |
|---|---|
| Primary byte | **No new primary/control.** Preserve tiny `00..0F`, short `10..BF`, long `C0..FF`, and assigned controls. Complete B6/EA in E1 only; B4/E8 and B7 keep their identities. F's `DF` OutputReservation / `E9` NativeAddress and S's `EC` StorageAddress remain distinct. |
| Body record `u8` | Existing `01..0B`; select **`0F` ExceptionDirectory** and amend **`0A`**. `0C..0E` belong to separately selected F/S candidates and are not composed here; `00` and `10..FF` remain unassigned in this composition. No prefix/escape or private tag is reused. |
| RootPin | [B + RootPin](trrm-root-pin-schema-selection.md) selects ReferenceSupport `0D`, separately from F. S mentions a *future, unselected* move to `0F`; that suggestion is not an allocation. E1 selects 0F for the directory. Any future composite must concretely reassign ReferenceSupport or repack the family; no collision/dual interpretation is admitted here. |
| Required global sections | Registry runs through `004B`; candidates use [foreign-interface `004C`](native-output-foreign-interface-method-identity.md#proposed-standard-metadata-delta), [predicates `004D..0052`](general-predicate-graph-schema.md), [enums `0053/0054`](nominal-enum-persistence.md), [static records `0055..005C`](immutable-static-field-persistence.md), [intrinsics `005D`](intrinsic-registration-identity.md) and [substitutions `005E`](constant-execution-mixed-substitutions.md). E1 adds **none**. `8000..FFFF` remains nonsemantic, not an exception extension. |
| Feature `u32` | Preserve assigned 1..7/9..13 and candidates 8/14..20 in their selected schemas. In particular 16 is foreign invocation, 17 general predicates, 18 enums, 19 ImmutableData, 20 fixed arrays. Select **21/1**, not NativeExceptionUnwind 7 or an Effects bit. |
| Other identities | No new section kind, DefId/token/TypeSpec form, Contract kind, SourceMap step role, target/profile ID, runtimePolicy value, Body implementation kind or codec selector. Feature 21 is unrelated to DefId `21=UnionCaseDef`. |

## Exact schema composition and prerequisites

The sole new case-sensitive out-of-band identity is
**`trrm/1-draft-register-exception-regions-1`**. The proposed internal selector
is **`TrRmArtifactSchema.RegisterExceptionRegionsDraft`**, not a flag set.
Its entire meaning is the accepted baseline **B** composition plus this
document's **E1** delta:

| Component | Complete selected contents |
|---|---|
| B | Accepted `trrm/1-draft-layout-queries-1`, registry, semantic catalog and governing chapters. |
| E1 | Directory 0F, amended 0A/domain, exception-specific event/view representation, dispatch and continuation rules below, and exact Feature 21/1. |

This is one closed selection, not a powerset algorithm or a promise to
incorporate later edits at linked paths. Exchange agrees on this complete
composition/catalog. F, S, fixed arrays, CopyOrigin, general predicates,
enums, static storage, scalar frozen-value registration, A3, RootPin,
captured-owner codecs and private amendments are **not** composed. Their
unassigned or unselected uses still decline. In particular, A2EU+E1 is not
selected; a future exact composite must name and validate its complete
composition rather than inherit E1 from this identity.

Keep `(1,0)`, `implementationKind=01`, `instructionEncoding=01`, map-mask bits
0..4/count order and `17 + 4*popcount(mapMask)` prefix unchanged. Compact
descriptors `02..05` retain their existing exact eligibility; they cannot
encode handlers/finally or acquire new hidden cleanup. Their ordinary operation
failure, when admitted, still needs the runtime's caller-delivery contract.

Every retained stream Body in this schema contains its E1 frame directory,
even if it has only Root. Its owning Module therefore requires effective
**21/1**. Retained exception-role/witness/common-view declarations and
fresh/active Throw or executed-unreachable use likewise require 21/1, including
unused-but-retained records and imported expectations. Edition is 1;
unconditional Feature prerequisites are `[]`. Operations whose requirements
are supported by B retain their independent least closure; a Module
requirement cannot compose another profile. Feature 7 is required only for
admitted *inward native* unwinding in a separately selected composition.
Another Module's requirements cannot satisfy the using Module.

Before affected use, require:

1. The explicitly selected schema for each supplied artifact and supported
   complete Module/Feature, binding, target and runtime closure.
2. Exact selected Core identities and complete ordinary type/witness records.
   Exception eligibility is never derived from a name, class flag, layout or
   serialized “trusted” assertion. A source-free Core identity that the actual
   selected input contract cannot establish is unsupported.
3. Real event/object preparation, roots, parent/cause retention and same-runtime
   delivery; supported representations for every reached payload and view.
4. Complete ordinary operations and specifically required exceptional-cleanup
   programs for actual holders, partial construction and publication protocols.
   The directory supplies routing, not missing state tests or owner codecs.

Known missing prerequisites decline before code generation/execution. Host
`FailFast`/`CheckedExceptions` options are implementation selections, not a
reinterpretation of Standard Module policy zero. A backend advertising E1
cannot silently terminate a reached checked event in place of its admitted
local dispatch. Explicit `failfast` and unrecoverable preparation still have
their separate terminal outcomes.

## Exact candidate Body records

All integers are little-endian. The existing typed-record header is
`u8 kind, u8 flags=0, u16 reserved=0`. Counts/extents use nonwrapping checked
arithmetic within the containing Body/record/section. No padding or trailing
bytes is permitted. Opcode, record, section, type-form and bit-mask codes are
hexadecimal; Feature IDs, counts and ordinals are decimal.

### Record 0F: ExceptionDirectory

**Every stream Body has exactly one directory, at typed-record index 0.**
Record zero must have kind 0F; no other record may have that kind. This is a
schema-owned anchor, not a first-record heuristic or an old-Body fallback.
`recordCount` is now **2..65,536**: the directory and at least one separate
kind-01 BlockEntryState. Body framing otherwise remains unchanged.

Payload, in this exact order:

```text
u32 regionCount
u32 handlerCount
u32 blockOwnerCount
ExceptionRegion[regionCount]       // fixed 16-byte rows below
ExceptionHandler[handlerCount]     // fixed 20-byte rows below
u16 blockRegion[blockOwnerCount]   // in Block-index order
```

Exact record extent including header is
`16 + 16*regionCount + 20*handlerCount + 2*blockOwnerCount`.
Region count is 1..65,536; handler count 0..65,536, further constrained by
exclusive region ownership; `blockOwnerCount == Body.blockCount`, 1..65,536.
These are distinct zero-based `u16` index namespaces. Root consumes region 0;
all remaining indexes, including FFFF when present, are ordinary indexes.
There is no absent-index sentinel and no portable capacity guarantee.

The directory and all nested rows belong exclusively to this Body's owning
MethodDef. Body sharing between MethodDefs is not newly admitted. Region and
handler identities are activation-local, not exported definitions. Two rows
with equal code/types do not become one scope or one handler occurrence.

### ExceptionRegion rows

Each row is exactly 16 bytes:

```text
u8  regionKind
u8  regionFlags
u16 parentRegionIndex
u16 entryBlock
u16 finallyRegionIndex
u32 handlerFirst
u32 handlerCount
```

| Kind | Meaning and exact field applicability |
|---:|---|
| `0=Root` | Exactly row 0. `parentRegionIndex=0`, `entryBlock=0`; all remaining fields zero. The kind, not a sentinel index, denotes no parent. Root is the frame's outer executable region. |
| `1=Protected` | Parent is a different existing region. Entry belongs directly to this region. Handler range and optional finally use the rules below. Every ordinary continuation target is explicit in RegionExit; no duplicate per-region normal target or dummy successor is required. |
| `2=Filter` | Parent is its owning Protected. Entry belongs directly to this Filter. Region flags, handler-range and finally fields are all zero. Exactly one Handler row owns this region as its optional filter. |
| `3=Handler` | Parent is its owning Protected. Entry belongs directly to this Handler; other fields as for Filter. Exactly one Handler row owns this body region. |
| `4=Finally` | Parent is its owning Protected. Entry belongs directly to this Finally; other fields as for Filter. Exactly one Protected owns it through `hasFinally=1`. |

Kinds 5..255 are unassigned. Every nonroot parent chain reaches Root without
cycles. A Protected can be nested in any executable region; Filter, Handler
and Finally membership identifies association with the Protected, **not**
eligibility for that Protected's sibling handlers when their own code fails.
Every region has at least its directly owned entry Block.

A Protected's `handlerFirst/handlerCount` is an exclusively owned contiguous
range in the directory's **Handler table**, not a typed-record range.
Empty is `(0,0)`. Nonempty first is zero-based; first plus count must fit the
actual Handler count. Rows occur in lexical catch order, each occurs in
exactly one range, and each names that same Protected. A Protected with no
handlers or finally can represent a lexical transfer boundary; its presence
creates no implicit cleanup or handler.

`regionFlags` bit 0 (`01=HasFinally`) applies only to Protected; bits 1..7
are zero. A clear bit requires `finallyRegionIndex=0`; a set bit selects an
existing kind-4 region with this parent. Other kinds have flags zero. The
Protected's finally runs after its protected/selected-handler activation
leaves, not before testing its own catches. An enclosing scope's finally
runs only when that scope is crossed.

### ExceptionHandler rows

Each row is exactly 20 bytes:

```text
u16 protectedRegionIndex
u8  catchKind
u8  hasBinding
u16 catchTypeMapIndex
u16 bindingHomeIndex
u8  hasFilter
u8  reserved=0
u16 filterRegionIndex
u16 handlerRegionIndex
u16 reserved=0
u32 exceptionWitnessRow
```

| catchKind | Match, type and witness |
|---:|---|
| `0=AllLocal` | Any admitted contract-catchable local event. `catchTypeMapIndex=0`, `exceptionWitnessRow=0`, `hasBinding=0`. No source-visible universal event value is introduced. |
| `1=ExactObject` | `catchTypeMapIndex` is the exact complete exception class after normalization. `exceptionWitnessRow` is a required ordinary InterfaceWitness row for that class's exact registered `IException<Class>` instance. |
| `2=CommonObject` | TypeMap names exact registered `IException<_>`, not `IException<void>` or another interface. `exceptionWitnessRow=0`; each actual object supplies its complete self/type/witness evidence. Does not match UnreachableException. |
| `3=UnreachableValue` | TypeMap names exact selected Core UnreachableException. `exceptionWitnessRow=0`. Matches its built-in event and binds only the zero-data Copy payload when requested. No other struct/enum/union exception is admitted. |

Kinds 4..255 are unassigned. Witness references use the ordinary required
one-based 24-bit row in `u32`, high byte zero, section `0028`; they are neither
DefIds nor Body-local indexes. Validate exact implementing type, self argument,
registered interface identity, substitutions and retained binding. Positive
witness records are producer assertions with mandatory structural/binding
checks, not a new authority flag or consumer proof of arbitrary bodies.

`hasBinding/hasFilter` are exactly 0/1. `hasBinding=0` requires
`bindingHomeIndex=0`.
A binding selects a distinct role-3 local Home of the complete catch type;
it cannot be a receiver/formal, share another catch binding's identity or
designate a payload `ref<E>`. Runtime initializes this Home after match and
before the filter. Its ordinary scope is that candidate's filter/body and
their nested regions, not siblings. A false/failed candidate ends this binding;
a selected handler retains it until handler exit. These are nonowning object
handles/views, not event transfers. Unreachable binds its Copy value.

`hasFilter=0` requires `filterRegionIndex=0`; a present filter selects kind 2
with the same parent. The required `handlerRegionIndex` selects kind 3 with
that parent. Filters and bodies cannot share region identities between
handler occurrences.

### Block ownership, entries and ordinary edges

`blockRegion[b]` is the Block's single directly owning region. Every block has
one owner; parents supply its lexical ancestors. Instructions cannot straddle
regions: split a Block at a boundary. Block ranges still cover all code
exactly, and each ends in one explicit terminal instruction.

Filter, Handler and Finally entries are **dispatch-only**. They have no
ordinary incoming edge and their BlockEntryState has `registerCount=0`.
Selection initializes only the declared catch Home, not arbitrary registers
or destination snapshots. Other needed survivors must have explicit, legally
live Home storage and complete dependencies. Ordinary code within those
regions uses ordinary checked operations/edges.

An ordinary branch may stay within its directly owning region, or enter the
entry Block of an immediate child Protected. The latter establishes one new
activation. It cannot exit a region, enter another point in a child, or enter
Filter/Handler/Finally. A same-region back edge does not rearm a finally.
Reentry after a completed exit establishes a new activation and new ordinary
local lifetimes. Crossed exits use RegionExit; dispatch is not an extra normal
CFG successor.

Ordinary return forms are valid only from Root with no entered child region.
A source return inside protected/handler code first stages its result, leaves
the crossed regions, and reaches ordinary Root completion code. No normal
return is added to DoesNotReturn, Filter or Finally.

### Amended record 0A and closed exit-kind domain

**Replace the current eight-byte payload prefix only in E1** with:

```text
u8  exitKind
u8  hasTarget
u16 targetBlock
u8  valueKind       // ValueSelector: 0=None, 1=Register, 2=Home
u8  valueUse
u16 valueIndex
u32 regionCount
u16 region[regionCount]
```

Exact extent including header is `16 + 2*regionCount`. Regions are an explicit
inner-to-outer lexical path, **not sorted numeric IDs**, with no duplicates
or Root. Their referenced indexes and the execution context must agree.
Unused target/value fields are zero; presence is never encoded by FFFF.

| exitKind | Exact operands and continuation |
|---:|---|
| `1=Leave` | `hasTarget=1`; target directly belongs to a strict ancestor of the current Block owner. `valueKind=None`, `valueUse=0`, `valueIndex=0`. Region list is the complete parent path from that owner up to, but excluding, the target owner; count 1..65,535. Run every applicable crossed finally once, then enter target. Leaving an active Handler by this non-failure transfer is explicit recovery; normal handler fallthrough cannot use Leave. |
| `2=HandlerFallthrough` | `hasTarget=0`, no value. Current Block directly belongs to Handler H. List is exactly `[H, H.parentProtected]`. End this activation's catch binding and propagate its **same** event, crossing the Protected's finally and continuing outward without restarting its catches. |
| `3=FilterComplete` | `hasTarget=0`; list exactly `[current Filter]`. Value selector is Register/Home, `valueUse=1=CopyRead`, with exact initialized primitive bool. Read it once. True selects this candidate's Handler; false ends its binding and advances to the next catch. No method return or implicit R0/None value is used. |
| `4=FinallyComplete` | `hasTarget=0`, no value; list exactly `[current Finally]`. Complete the running action and resume its suspended runtime continuation. It has no ordinary target edge. |

Zero and 5..255 are unassigned. Leave may not cross a Filter or Finally;
source control cannot escape a cleanup action. Transfers *contained* inside
a Finally, including leaving a nested Protected for a Block still inside that
Finally, remain ordinary structured transfers and do not replace the suspended
continuation. A nested loop transfer staying inside a Handler does not swallow
its event.

A Leave target has empty incoming register state. Spill any required saved
values to lawful Homes before beginning the transfer and reload where needed;
cleanup does not promise register preservation. Saved scalar returns retain
their computed value, while saved handles/references still observe their real
backing. Normal local/parameter retirement and postconditions are explicit
ordinary code after the appropriate authored finally actions, before final
ret/publication. No separate Return exit kind or fabricated unit result is
needed.

B6 remains `B6, recordIndex:u8`; EA remains `EA, control:u8=0,
recordIndex:u16`. Both are terminal and reference only amended kind 0A.
Use B6 at indexes 0..255 when equivalent and eligible, otherwise EA.
Longer equivalent EA is ordinary valid noncanonical input. Wrong kinds,
nonzero EA control or an old eight-byte-prefix 0A extent reject under E1.

## Runtime event representation and Throw

Events are **runtime state**, not serialized artifact rows or source-visible
IDs. E1 selects a runtime-owned propagation context with the following exact
logical fields; it does not prescribe an independent machine object layout:

| Field | Representation/obligation |
|---|---|
| Event discriminator | `Object` or `UnreachableValue`, no other local payload form. |
| Identity/subject | Object: the exact existing managed object is the event; no separately allocated public wrapper or independent event identity. Built-in: one fresh opaque runtime identity with exact zero-data Unreachable payload. Copying its payload never copies the event. |
| Complete evidence | Normalized actual subject type and, for objects, exact checked self/IException witness. The exception-only common view carries that same handle plus the actual closed self type and selected witness. This supplies its per-value evidence, not general existential storage, boxing, inheritance or virtual dispatch. |
| Origin | Terrum event: original selected Module/MethodDef and logical instruction/suboperation, actual code/load instance and minimal originating instruction address. Relocation supplies a checked logical-to-address mapping and retains it. A separately admitted normalized native event retains its Feature-7/profile-owned native origin instead; no foreign PC is invented as a Terrum MethodDef or relabeled to a catch/call site. Ordinary debug SourceMap text is optional and cannot provide required identity. |
| Propagation cause | Optional exact predecessor event and its complete closure. Object-to-object replacement attaches the exact old object as ParentException. InnerException remains authored and unchanged. Same-event propagation adds no self-parent and preserves the existing chain. |
| Responsibility | One live responsible runtime propagation/handling extent, with checked handles/views being nonowning. Suspended/nested handling contexts preserve the proper event association without minting a second event or captured owner. |
| Resources | Real payload/cause roots, backing/guard/storage-generation dependencies, type/witness/cleanup providers, runtime/allocator and code/load lifetime. A root retains an object, not invalid external resources or an exited borrow. |

For mixed object/value causal replacement, retain the predecessor as a typed
runtime event link, without boxing Unreachable. A class ParentException names
the immediate predecessor only when it is an object; otherwise it is null
and the complete predecessor remains in runtime event context. This is the
selected physical completion of the built-in value-event exception to the
class-only view, not loss/flattening of history or a general value exception.
Synthesizing a class wrapper would change the selected payload/event identity;
skipping the value predecessor would lose the actual cause. The typed runtime
link preserves both without broadening the source-visible class view.
Cause/dependency traversal preserves sharing and rejects invalid owning
cycles/duplicated responsibility under the existing ownership contract.

Runtime context may be embedded or side-stored, but object event identity
always remains object identity. No per-site event-ID catalog, public current
exception API, serialized live address, foreign packet or scalar reason-code
substitute is selected.

Retain Throw record 09 and B4/E8 widths/controls:

```text
u8  payloadKind
u8  selectorKind
u16 selectorIndex
u16 lexicalRegionIndex
u16 flags=0
```

Its extent including the common header remains 12 bytes. B4 has a full-byte
record index; E8 has zero control and a `u16` record index. Both reference
only kind 09. Register/Home indexes use their ordinary distinct namespaces;
special-register source eligibility is not broadened to exception types.

- `payloadKind=1` selects an already evaluated complete eligible object/value
  in Register/Home, with unused `lexicalRegionIndex=0`. Apply the local owner's
  fresh-versus-same-active-object rule: the same active object preserves origin
  and parent history even when reached through another checked handle; the
  record name cannot force it to become a distinct event. A distinct object
  becomes primary at this throw; a thrown Unreachable value always creates a
  fresh built-in event. Operand/preparation failure is its actual event.
- `payloadKind=2` uses None/index zero and selects an actual **Handler region
  index in this Body**, not a Protected, handler-table index or thread-global
  exception. That live lexical activation must still own its exact event.
  The named Handler must be the current Block's owner or an ancestor in its
  region chain; a merely existing Handler elsewhere in the directory rejects.
  Nested source blocks/body-owned cleanup may refer to their enclosing Handler;
  a separately invoked/inlined helper cannot acquire that source context.
  This enables compiler-selected same-event propagation, **not source bare
  `throw;`**, which remains inadmissible.
- `payloadKind=3` keeps its existing captured-owner meaning but **remains
  unsupported in this selected composition**. E1 adds neither Capture's exact
  registration/context operation nor its opaque affine value/retirement codec.
  Reject affected use rather than reducing it to fresh or active Throw.

Throw has no normal successor/result. Checked arithmetic, requirement
construction, ordinary call failure and executed unreachable enter the same
dispatcher at their actual failed operation. Their exact factories/categories,
phases and input/output state are governed by their own selected contracts;
E1 does not complete missing constructor, catalog or checked-operation codecs.
Events that cannot actually be prepared/transported take their admitted
preparation outcome or fail fast; no null/default/requested outer event is
fabricated.

## Incremental dispatch, cleanup and continuations

The runtime derives the current region from the active instruction's Block
and directory in each active Terrum frame. This dispatch location is separate
from the event's retained origin: a caller receives exceptional completion at
its actual invocation instruction. Admitted native normalization supplies the
Terrum import-call dispatch location without relabeling its native origin.
It maintains dynamic entered-region activations and suspended
continuations. These are runtime bookkeeping, not additional wire fields:
pending event or normal Leave target, remaining lexical exit path, running
finally and actual Home/owner/publication state. No missing normal result is
written into a destination to represent failure.

### Selection and exceptional traversal

1. At a failure, retain its complete event and actual state. Complete any
   specifically required cleanup of the already exited inner regions before
   reaching a candidate Protected's still-live scope.
2. At that Protected, test handlers in their stored lexical order. Do not run
   a nonmatching filter. Initialize a matching binding, then enter its filter
   once if present. FilterComplete false advances; true or absence selects
   the Handler body. The Protected's own finally stays armed while its catch
   selection/body remains inside that construct.
3. If no catch matches, leave that Protected, run its optional finally, then
   continue outward. Thus an inner finally's mutation or replacement failure
   is visible to an outer filter. **No pre-unwind first-pass filter search**
   is allowed.
4. Failure escaping a Filter or Handler bypasses the associated Protected's
   sibling catches; its finally is crossed before outer selection. A nested
   Protected inside that code can handle the new failure first. A false filter
   is not a failure, and a failed filter is never silently false or a restart
   of completed sibling work.
5. HandlerFallthrough continues the same event; Leave crossing the Handler
   requests swallow. Keep that event as the transfer's causal context until
   all crossed finally actions complete, so a replacement failure still
   retains it. Successful transfer ends this propagation responsibility; a
   catch handle does not become a saved owner.
6. A new failure from a reached filter/handler/finally becomes primary and
   retains the pending event under the event rules. Search starts from that
   actual instruction, with already exited activations and started cleanup
   recorded. Completed work for the old event is not replayed. A genuinely
   new event can reach the same still-live outer scope under a new search;
   this is not retesting the old event or retrying a failed filter.
7. After the outermost admitted Terrum frame/main finds no handler, terminate
   under fail-fast-unhandled. Already crossed explicit/required cleanup remains
   executed; the final abort adds no normal frame/owner/output cleanup or ensure.

### Finally execution and state

Starting a finally marks that activation's action **started** before entering
its dispatch-only Block. It cannot be restarted for the same exit. Normal
FinallyComplete resumes the suspended transfer. Nested Leave/filter/finally
execution uses a LIFO stack of continuations and cannot overwrite an enclosing
action's pending target/event. If a failure escapes the
action, abandon its remaining instructions and that pending normal target,
retain causal responsibility, skip the action's associated Protected catches,
and continue through outer applicable actions/handlers. Never resume a partially
executed action as if it had completed.

Nested recovery contained within the Finally can complete normally and resume
the rest of its action; its enclosing suspended transfer remains in force.
Each separate outer finally remains eligible when actually crossed. Failfast,
process/thread/platform abort and unadmitted host faults may terminate without
finishing any of these actions.

Use ordinary code for **authored or specifically selected** exceptional-cleanup
programs. Required argument-staging, failed-construction, saved-result and
private-publication cleanup must be represented explicitly with actual
acquisition/transfer markers and supported operations. A region directory
cannot infer cleanup from a TypeDef, invoke incomplete-parent Cleanup, restore
moved sources, retire vacant storage or invent state-query semantics.

Normal compiler-invoked Cleanup remains normal-retirement-only. An ordinary
retirement operation in an explicitly selected cleanup program has its own
contract; merely retaining Cleanup clause 0005 does not add that operation.
If its authored Cleanup throws, remaining generated owner/field retirement
does not resume during the new unwind. Required exceptional protocols must
retain/settle their actual residual responsibility under their own program.
If that program/state/value support is unavailable, decline the affected use.

### Ordering and publication boundaries

Preserve the exact CallSite Capture/Stage/Check/Prepare/Invoke/Publish order
from the [instruction owner](../specs/il-instruction-set.md#invocation-phases).
The dispatcher does not execute later arguments, replay preparation or publish
a normal result. Before Invoke, staged owners remain caller responsibilities;
after transfer, actual callee-owned inputs do not revive in the caller.

Preconditions remain outside the body's protected scopes; an unentered body
finally cannot run for their failure. Body cleanup precedes ensure. An ensure
failure starts at its clause, outside the exited body try, without replaying
body/finally/field cleanup. Its normal false result and predicate failure
retain their distinct categories and original semantic owners.

Ordinary explicitly initialized outputs retain their real state on later
failure, including false ensure; the dispatcher neither vacates nor publishes
them. Reserved/private outputs follow their selected exceptional cancellation,
partial cleanup and publication protocol. F's result-correlated NativeOutput
reservations remain tied to the actual call and Home generation: pre-Invoke
cancellation is not cleanup of T, and abnormal Invoke completion provides no
normal status/output relation. Do not recover such a relation from output bytes.

E1 adds no automatic all-output transaction or general “recovered” state.
Continuation after a catch remains unsafe; cleanup accesses require their own
valid actual state, backing and surviving dependencies.

## Producer, consumer and compiler obligations

### Producer truth and mandatory structural consumption

Producers, including transforming producers, establish correct lexical/source
order, actual entry/exit/activation behavior, scope authorization, event
eligibility, complete state, roots, ownership, cleanup and all retained
requirements. Source compilation checks authored source; optional deeper
audits may inspect emitted Body truth.

Before using a selected Body, consumers must check:

- Exact schema/catalog and supported Module/Feature/target/runtime closure;
  map/Block/record framing, anchor uniqueness, exact extents/counts, reserved
  fields and every required reference namespace.
- Region kinds, rooted acyclic parent graph, directly owned entries,
  exclusively owned handler ranges/filter/body/finally regions, Block-owner
  bounds, and legal structural branch/terminal/dispatch-only entry shapes.
- Handler type/witness/ordinary Home shape, all 0/1 presence fields and required
  unused zeros, filter Boolean selector shape, RegionExit context/path/target
  agreement and Throw's lexical Handler references.
- Complete selected type/Core/witness binding and original provider
  expectations; actual runtime/event/cleanup support for the retained use.

These are finite structural and binding checks needed for safe consumption,
not a requirement to prove that a filter really computes the producer's claimed
state, a cleanup program settles every resource, every authorization is true,
or foreign code honors its promises. False producer facts can make execution
undefined/not guaranteed under the [reader owner](../specs/representation/reader-trust.md);
do not call such a structurally valid semantic defect a mandatory rejection
test. Conversely an unsafe-to-decode, wrong-namespace, malformed or unsupported
input must not be used.

A consumer may process only the selected Bodies/dependencies, performing their
required checks before use. E1 requires no preliminary scan of all artifact
Bodies, source reconstruction, global semantic audit or trusted signature bit.
Resource exhaustion is explicit refusal of the selected operation/claim, not
a recoverable Terrum program exception or successful empty directory.

### Binding, replacement and retained origins

Resolve each original Module/key/type/witness expectation before comparing the
actual provider. Preserve exact complete catch/self identities, common-view
registration, representation/root/backing contracts, failure categories and
phases, ownership/publication/cleanup obligations and every stronger provider
fact actually used. No raw row, method name or identical native signature
substitutes for those dimensions.

Compatible replacement need not have the same private region/Block/handler
indexes or implementation body. If its Body is consumed, it must have a
structurally valid selected directory and preserve every retained behavioral
promise. Catch elimination based on positive *or negative* type/witness facts
retains those facts and checks them again. Incompatible replacement declines
or triggers an explicitly authorized regeneration, not a weakened event/state
contract or search for another provider.

Keep original expectations distinct from selected actual origins through
transitive providers, direct-reference packing and source-free lowering.
Live events retain the actual originating code/load, type/witness, cause and
cleanup providers until their last use. Servicing later bindings cannot relabel
an existing event to a replacement image or release its old instruction-address
mapping.

### Traversal, remapping and re-encoding

Shared bound-tree traversal/rewriting must visit protected code, filters,
handler bodies and finally programs as semantic regions, even though their
entries are not normal successors. Preserve source order, Handler activation
identity, dependencies, loans/owners and original/actual origins. A visitor
following only normal CFG edges is incomplete. E1 does not approve general
cross-method inlining/Body sharing; such transformations need their complete
source-owner/phase/origin contract or must decline.

Meaning-preserving re-encoding selects both schemas, decodes typed structures
and rebuilds:

1. All Module/MethodDef/Body ownership, Import/Export/type/witness/Contract
   references and retained requirements under their ordinary namespaces.
2. TypeMap/MethodMap/FieldMap/ConstantMap and distinct Homes, including catch
   types/bindings/witness rows and all E1-used ordinary operations.
3. Region and Handler indexes, parent links, lexical handler ranges, optional
   filter/finally references, Block-owner entries and every Throw/RegionExit
   reference. Remap list elements without sorting their lexical order.
4. Block indexes/entry references, ordinary edges and all records,
   record-zero anchor, record offsets, selected B4/E8/B6/EA form widths,
   code offsets/lengths and containing extents.
5. Logical failure origins and instruction-to-generated-code mappings;
   affected SourceMap instruction slots/ordinals/substeps under the existing
   role domain when debug data is retained. Any separately selected native
   address records retain their own provider-owned identities.

Packing uses one supported destination schema for the whole artifact. Separate
providers may use other explicitly supported schemas for unchanged contracts;
their Bodies retain their own selectors. Retained E1 programs cannot be packed
into A2EU by erasing directories, converting Throw to FailFast, or copying
same-pair bytes. Changing a version word or substituting matching octets in
literals/counts/indexes is not conversion. Unsupported required meaning refuses
the transformation; no converter or old reader is required for drafts.

Canonical writers keep directory at record 0, Root at region 0, order other
regions by a parent-before-child depth-first traversal with immediate children
ordered by their entry Block index, and group Handler rows by owning Protected
in that order while preserving lexical catch order. Emit Block owners in Block
order. Visit Home types, then directory catch types in that traversal, then
ordinary BlockEntryState/instruction references for first-use TypeMap order.
Other records/maps follow existing first-use/no-unused rules after the anchor.
Do not intern distinct regions, catch occurrences or Homes.

Ordinary readers accept consistently remapped noncanonical nonroot region
order, handler-range placement, maps and equivalent long instruction forms.
The record-zero anchor, Root identity and lexical handler-list order are
structural/semantic obligations, not optional canonicality. Other ordinary
duplicate typed records remain allowed when they preserve the same referenced
meaning; duplicating the directory or scope identities does not.

### JIT/AOT, runtime and native boundaries

JIT and AOT preserve the same logical dispatch/continuations and instruction
origins. Before publishing code, provide real event/type/witness/root/cause
services, precise active-instruction-to-Block/region mapping, supported Home
storage, complete cleanup programs and admitted internal Terrum frame delivery.
Registers may be spilled, but live roots/holders and pending continuations
cannot disappear on a failure path.

The runtime carries failure separately from normal return/output publication.
This logical runtime-internal transport is not a new native procedure-call
signature, integer return code, public object layout or foreign packet ABI.
Any independently replaceable boundary still needs its actually selected
complete call and failure contract. OS unwind records recover frames; they
do not supply object identity, E1 filter timing or cleanup truth.

Preserve [Windows deployment](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support):
accurate prolog/epilog/stack and nonvolatile-state recovery, code fixups and
publication, unwind registration and retention through all live events/frames.
No pre-unwind OS filter search may replace Terrum's incremental semantics.

Ordinary raw native calls stay direct and caller-unsafe, without implicit
marshalling/rooting/pinning, an automatic catch thunk or hidden failure argument.
Feature 21 does not enable inbound native faults. Such faults require independent
Feature 7 and its exact normalization/runtime support; fatal/unadmitted host
faults remain distinct. Terrum failures **never propagate outward through native
frames**. Exports/callbacks contain them under an already admitted boundary or
terminate. A paired [foreign bridge](../specs/runtime/foreign-failure-bridge.md)
returns normally before local re-raise and remains separately gated.

## Candidate qualification and acceptance gates

The cases below are **requirements for implementing/qualifying the candidate**,
not results of this documentation change. No parser, hosted/private execution,
image emission or existing A2EU test establishes them. Supply independent
well-formed artifact fixtures and actual supported payload/runtime/cleanup
dependencies; do not silently replace unavailable classes/owners with integers.

| Area | Cases and required observations |
|---|---|
| Positive directory/flow | Root-only Body; protected code with exact-object, common-object and bare handlers; handler-free finally; nested Protected inside filter/handler/finally; repeated normal loop reentry. Validate distinct activations, explicit entries/terminators and exactly-once crossed actions. |
| Catch evidence | Aliases normalize; distinct Modules/closed arguments do not match; positive self witness and same-object handle/view survive. No Copy requirement, boxing or `ref<E>` binding. Unreachable matches its exact/bare catch, not the common class view. |
| Filter ordering | Type mismatch performs no filter/binding work; false then true filters run once in stored source order. Inner finally mutates an outer-filter input before it is tested. An inner finally replacement event, not the superseded event, reaches that filter. |
| Filter failure | Matching binding exists before filter execution. Escaping filter failure becomes primary with original event retained; skip sibling selection. Nested recovery within a filter can continue its remaining code without replaying earlier evaluation. |
| Handler exits | Normal fallthrough propagates same event/origin; same-object explicit Throw via both payload 1 and proven payload 2 preserves history. Explicit return/break/continue leaving Handler swallows after crossed actions; a contained loop transfer does not. |
| Finally continuations | Normal Leave, exceptional propagation and recovery run inner-to-outer actions once. Faulting action stops its remainder and replaces the pending transfer/event. Nested contained recovery resumes the action; escaping failure does not replay it. An outer action remains eligible when crossed. |
| Event identity/causes | Distinct object replacement uses exact ParentException, never InnerException mutation or aggregate copy. Unreachable throw/instruction creates fresh identities even with equal zero payloads. Mixed object/value cause chains retain every exact predecessor; class-only views do not box value events. |
| Terminal distinctions | Explicit FailFast performs no checked dispatch/cleanup; reached Unreachable uses admitted event transport. Unhandled propagation performs already crossed explicit actions, then aborts with no added normal cleanup/ensure. Event-preparation failure fabricates neither selected nor null/default events. |
| Phase/state | Failed precondition cannot enter body finally. Saved scalar and saved-reference results observe their different backing semantics. Body cleanup precedes ensure; ensure failure does not replay body cleanup or return-expression evaluation. No affected-state recovery certificate. |
| Ownership/cleanup | Later argument failure settles actual staged owners under selected programs; transferred sources stay vacant. Failed unpublished construction reverses actual outstanding acquisitions, including nested partial children, without incomplete-parent Cleanup. Normal complete cleanup keeps declaration order. A throwing Cleanup does not resume remaining generated retirement on unwind. |
| Output/publication | An ordinary explicit output initialized before false ensure stays actually live. Result publication occurs only on normal completion; failure does not infer a status from output bytes or publish an unavailable result. Reserved/private output protocols remain outside this composition. |
| Other profile composition | B+E1 does not compose F, S, A2EU, RootPin, general predicates, enums or private amendments. Reject artifacts whose retained use requires those schemas before affected use; never infer a composite from Feature rows, matching bytes or provider co-location. A future composite needs a separate exact schema decision and qualification. |
| Mandatory malformed rejection | Missing/wrong/duplicate record-zero directory; truncated/extra extents; unknown kinds/flags; mismatched Block-owner count; cycles/wrong parents; overlapping/orphan Handler ownership; wrong filter/finally association or ordinary edge into dispatch entry; illegal return/RegionExit path/target; wrong Boolean selector, None-use fields, type/Home/witness namespaces or lexical Throw index. |
| Unsupported combinations | Missing 21/support/closure, unknown schema, wrong Core/witness identity, unsupported payload/owner/state/cleanup program, captured Throw 3, unselected RootPin/general predicates/enum/frozen/A3 composition. Decline before affected use, with no aliases, guessed decoders or FailFast substitution. |
| Boundary encodings | Root index 0; valid local FFFF and count 65,536 in each independent namespace where ownership permits; first/last exclusive ranges; B4/B6 index 255 versus E8/EA 256/FFFF; empty maps/lists; absent index zero versus present ordinary zero. Reject count 65,537, count/extent overflow, stale old 0A payload and every unassigned exit/control. Encoded maxima imply no portable runtime capacity. |
| Producer truth/audit | Structurally valid false authorization, false initialized-Home/read facts, wrong semantic acquisition order or omitted required resource settlement violate producer truth. A scoped semantic audit should find its claimed cases; do not demand mandatory arbitrary consumer re-proof or claim external provider behavior was proven. |
| Remapping/re-encoding | Permute TypeMap, distinct Homes, Regions, Handler-range placement, Blocks, records and global witnesses/provider rows simultaneously. Rebuild entry/exit lists, Throw lexical indexes, short-to-long forms, offsets and debug/origin mappings. Include literal/count bytes equal to opcodes/indexes to expose octet substitution. Independent decode/runtime observations stay identical. |
| Provider replacement | Compatible row/body reorganization with exact selected Module/key/self/witness/output/cleanup promises succeeds. Removed self witness, changed complete catch type, weakened output/cleanup/cause dependency, stronger caller-unsafety or invalidated positive/negative catch-elimination fact fails. Transitive selected origins and original expectations survive packed direct references. |
| Code lifetime | An event retaining an old origin/cause/cleanup provider prevents that actual code/mapping from retiring while later bindings select a compatible replacement. Native address identity is not rewritten to the replacement. |
| Native boundaries | Feature 7 and native/foreign exception transport are not selected by B+E1. Reject retained uses requiring inbound native normalization, export/callback containment, a hidden native argument or foreign packet mapping; no native fault is inferred as a Terrum event. |
| Recoverable resources | Exercise recoverable allocation only with actual admitted event-publication reserve/resources and partial-acquisition cleanup. Unrecoverable OOM, stack/loader budgets and forced/platform termination remain separate; Feature 21 alone supplies no recovery promise. |

For runtime qualification, use the selected final compiler/configuration and
explicit schema for **every** input/provider. Execute source-free JIT and AOT
fixtures in owned headless workers with deadlines, captured outputs and actual
normal/terminal outcomes; terminate only the owned process/tree and wait before
bounded file cleanup. Inspect an independently specified order/identity/state
trace, not only a numeric exit status. Keep fatal and checked-recovery cases
separate, and preserve visible/hardware opt-in gates.

Report selected/passed/failed/skipped/incomplete/crashed cases and input,
provider-binding and compiler identities. Emission proves emission; successful
structural loading proves neither truth nor runtime behavior. Existing hosted
CheckedExceptions or A2EU FailFast observations do not qualify E1. Documentation
authoring requires no build or native run.

## Intended integration and remaining gates

If reviewed/adopted, update the
[registry](../specs/representation/registry.md) and machine registry together:
add Feature 21/1 and record 0F, change 0A's shape/domain, complete B6/EA status
and replace only the affected unsupported-region/event entries. Update the
[Body/instruction owner](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations),
[metadata Body boundary](../specs/il-metadata.md#body-table-and-coded-instruction-boundary),
logical region/event records, manifests/admission, reference/canonicality and
capacity projections with these same exact rules. Add the candidate cases to
their [representation](../specs/conformance/il-registry.md) and
[local-failure](../specs/conformance/source-local-failures.md) owners after
approval. The source productions and preserved runtime/ownership owners are
not intentionally replaced.

The physical mixed-cause link and exception-only common-view evidence, the
record-zero anchor, amended RegionExit payload/domain and exact B+E1
composition are the selected **unapproved** decisions; none is left for an
implementation to guess. All unrelated allocations remain untouched.

Independent managed construction/member execution, captured owners, general
owning/partial-state codecs, runtime failure factories/defaults/phase origins,
async/generic-pattern execution and native/foreign transport retain their
separate support/contract gates. Implement E1 only for actual complete admitted
uses and qualify them; do not describe that slice as full TRRM conformance or
completion of [CG-003](../implementation/conformance-gaps.md#cg-003).
