# Checked function-pointer output parameters

**Status: unapproved; provisional implementation direction under the authorized
missing-contract workflow.** This proposal does not approve syntax, amend
normative owners, establish compiler support or report executed evidence.
It defines the checked-output portion of a function-pointer contract and
recommends a bounded addition to the proposed same-runtime ABI. The private
checked-reference wire decision identified below remains an explicit gate.

## Recommendation and scope

Represent an output formal by its **complete checked-reference type**:
`ref<init T>` or `write ref<init T>`, with ordinary parameter mode `00`.
Carry its exact normal-output state relation and dependencies in the existing
function-pointer invocation contract. At an admitted synchronous same-runtime
call, pass one address of the actual reserved destination, not a copied `T`,
an untyped pointer argument or a native-output reservation.

Recommend extending the
[same-runtime ABI candidate](terrum-function-pointer-call-abi.md) only for
these checked output formals with positive-extent, fixed rootless Copy payloads.
Keep its Windows x64, receiver-free, jointly regenerated, single-runtime-domain
agreement and all other entry restrictions. This intentionally supersedes that
proposal's blanket checked-output exclusion for this subset, not its exclusions
of general checked inputs/results, ranges, owners, roots, tasks or environments.
The source type contract is not restricted to this machine subset.

| Alternative | Assessment |
|---|---|
| Retain all checked references outside the first ABI | Coherent as a temporary unsupported execution boundary. It does not resolve the stated output gap; even a Boolean scalar output remains unusable indirectly. Keep it as the rejection behavior until the selected addition is implemented, not the completion criterion. |
| **Encode complete parameter types and output states in the pointer signature — recommended** | Reuse accepted `ref<init T>` identity, Param `00`, shared selectors, OutputState and Dependency. Add checked destination operands and one bounded address carrier. No new source parameter-mode vocabulary or general descriptor ABI is necessary. |
| Dedicated callable-contract owners, a new parameter-mode/state language, or a universal reference/owner ABI | Could unify more callable families, stored descriptors, ownership and independently replaceable entries. Requires new owners, references, runtime representations, failure and remapping contracts across unrelated features. Not needed for this output-only addition. |

Do not solve the gap by treating `ptr<T>`, `write ptr<T>`, `ref<T>` or the
legacy parameter spelling `init ref<T>` as aliases. An `init ref<T>` **local
declaration** instead reserves storage for a descriptor; it is not a descriptor
to vacant `T`. No compatibility syntax, mode-only reinterpretation or target-body
inference is selected.

## Owners, composition and inspected boundary

| Owner | Preserved contract or proposed amendment |
|---|---|
| [References](../specs/language/references.md#initialization-references), [signature identity](../specs/language/references.md#signature-identity), [grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments) | Reuse complete initialization-reference types, explicit `ref init place`, fixed destination identity, non-Copy capability and flow attenuation. Add no source production or legacy mode. |
| [Calls and outputs](../specs/language-contracts.md#2-outcome-initialization-and-nullability-are-separate), [flow](../specs/language/initialization-and-flow.md#outcome-correlations-and-invalidation) | Apply existing defaults, checked publication and discriminator invalidation to indirect calls. No separate checked-output transaction. |
| [Ownership and cleanup](../specs/language/ownership-and-cleanup.md), [dependencies](../specs/language/dependencies-and-views.md#destination-and-phase) | Preserve private acquisitions, actual holders, complete versus partial retirement, source/storage distinctions and every applicable exit obligation. |
| [Native boundaries](../specs/native.md), [ABI layers](../specs/data-layout-and-abi.md#contract-layers), [stable outputs](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | A checked destination is not foreign pointer authorization. Add the scoped Terrum entry mapping below, not a universal or independently replaceable ABI. |
| [Source-contract proposal](standard-function-pointer-source-contracts.md), [anchor proposal](standard-function-pointer-anchor-contracts.md) | Complete their checked-output parameter applicability. Preserve fixed bare defaults, inferred exact contracts, directed projection, structural anchors and their bounded predicate gates. Do not select an independently authored nondefault type spelling. |
| [Selector codec](aggregate-native-outputs.md#exact-shared-predicate-codec), [Feature composition](predicate-feature-composition.md) | Reuse the six closed normal-result selectors and proposed bounded `10/1`. Do not reuse the foreign NativeOutput formation/publication protocol, Feature 8, DF, Body record `0C` or source-use `6`. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [clauses](../specs/representation/contract-clauses.md), [registry](../specs/representation/registry.md) | Retain TypeSpec `0E`, Param `00`, existing clause shapes and owner rules. The only new Standard numeric candidates here are the narrowly scoped PlaceSelection flags below. |
| [Complete private pointer carrier](native-function-pointer-contract-private-persistence.md) | Remains the owner of the unimplemented candidate-97 signature payload and `fnptr#i` references. Its assumption of complete private type identities needs the separate explicit checked-reference decision below before affected private transport. |

The inspected
[`NativeFunctionPointerSignatureDefinition`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs)
carries result/parameter type identities, declared Effects, selectors, OutputStates
and dependencies. Its `IsValidSignature` nevertheless rejects every nonempty
OutputStates array; its admitted parameter identities are value types.
`ValidDependencyRows` does not admit checked-output destination `2`, and
`MatchesFunction` excludes `IsOut`, `IsInit` and implicit-reference formals.
Those are actual implementation gates, not evidence that the checked-output
contract is inherently a raw pointer.

[`BoundIndirectCall`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
currently carries a target and value-expression arguments. The legacy
`BoundOutArgument` describes private scratch outputs, not the complete
checked-reference capability required here. Adding rows without a destination
operand, exact formal type and verifier/lowering support would remain incomplete.

The active
[`NativeFunctionPointerTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs)
inspect effects/dependency identity and assert rejection of an output row;
that fixture is not a well-formed checked-output signature with a selector pair.
The separate
[`TrRmContractTableReaderTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmContractTableReaderTests.cs)
check Standard initialization types, mode `00` and attached output predicates.
[`PublicInitReferenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/PublicInitReferenceTests.cs)
exercise the legacy `init ref<T>`/`init argument` prototype, not this source form
or indirect ABI. The [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
does not exclude these suites; native execution has its own platform gates.
These are source observations only. The
[support inventory](../implementation.md#persistent-and-self-hosted-components)
does not establish this proposal's end-to-end route.

## Exact source and structural identity

Use the existing result-last spelling, for example
`fnptr<bool, ref<init int32>, bool>` or
`fnptr<write ref<init int32>, void>`. A checked descriptor is passed as an
ordinary typed argument; `ref init destination` explicitly forms its capability.
There is no `out` argument, `&init` checked argument, implicit vacant borrow,
default reference argument, type-local ensure clause or new output attribute.

This **proposed composition**, not an implemented/runnable sample, uses accepted
reference grammar and the unapproved ordinary-address/default-ABI directions:

```terrum
bool TryFill(bool ready, ref<init int32> output)
{
    if (!ready) { return false; }
    *output = int32(7);
    return true;
}

int32 Main()
{
    init int32 value;
    unsafe {
        fnptr<bool, ref<init int32>, bool> fill = &TryFill;
        bool available = fill(true, ref init value);
        if (available) { return value; }
    }
    return int32(0);
}
```

The Boolean output default supplies true/complete and false/vacant; the name
`TryFill` supplies nothing. Direct calls to this ordinary declaration are not
made unsafe by taking its address. Function-pointer formation/invocation retains the
companion proposals' independent unsafe gate; unsafe does not authorize an
invalid destination or operations inside the callee.

The normalized pointer identity contains the family, original convention,
complete result and ordered formal types/modes, declared Effects, selector
attachments, OutputStates and Dependency records. In particular:

* `T`, `ref<T>`, `write ref<T>`, `ref<init T>` and
  `write ref<init T>` are distinct parameter types. Inner qualification stays
  at its own node. For `T = ptr<U>`, the destination holds a pointer, not `U`.
* All these signature parameters use ordinary logical mode `00`; do not add a
  second, independently mutable IsInit/IsOut mode to stand in for the type.
  An implementation may classify a complete type for call planning, but that
  classification is derived, not additional identity.
* Labels, local binding `mut`, defaults and implicit-reference adaptation do
  not change identity. An initialization formal itself cannot be rebound.
  Writable reference cells remain invariant; pointer-value projection is not
  conversion of aliased storage.
* Bare source types acquire their fixed defaults in the defining edition and
  Module without consulting an initializer. Inferred addresses retain the
  selected declaration's normalized published contract. Nondefault contracts
  can travel by that inference and exact imported types; independently spelling
  arbitrary nondefault callback parameter/field types remains the source
  proposal's separate decision. Do not invent a type-query workaround.

For the first proposed machine addition, retain existing by-value inputs/results
and permit these two checked output types only when `T` has the same-runtime
candidate's admitted fixed rootless Copy representation, positive extent and
actual object alignment. No implicit root, pin or owning cleanup belongs to
that payload. Exclude initialization ranges, deferred outputs, GC initialization
references, reference/owner/task payloads and addressless zero-data destinations
from this ABI. Preserve well-formed richer types where identity transport is
supported; unsupported execution is not permission to erase their facts.

## Output selectors and the complete state relation

For each output, the contract denotes a normal-result-to-state relation:
`0=wholly vacant`, `1=complete and publishable`. It never denotes a partial
prefix, incoming initialization requirement, nullable-presence test or failure
channel. Use only the companion codec's selectors:

| nodeKind | Exact test | Complete ordinary result |
|---:|---|---|
| `1`, `2` | True, false | Built-in bool |
| `3`, `4` | Nonzero, zero | Built-in int32 or uint32 |
| `5`, `6` | Nonnegative, negative | Built-in int32 |

Each attachment is phase `3=NormalOutcomeSelector`, with its existing contiguous
phase-local ordinal and one ordinary-result read root. It observes the saved
result; it is not an asserted postcondition that must always be true.
`OutputState(formalOrdinal, selectorOrdinal, state)` selects that attachment in
the same logical signature. Formal ordinals include all ordinary parameters,
including unit parameters, and exclude hidden result storage.

For example, bare `fnptr<ref<init int32>, bool>` normalizes to attachments
`(phase=3, ordinal=0, node=1)` and `(3,1,2)`, with output rows
`(formal=0, selector=0, state=1)` and `(0,1,0)`. This tuple notation is semantic,
not another wire encoding. Preserve attachment ordinals when importing an
already published contract; source canonicalization is not permission to
renumber or merge independent attachments.

Require the supported normalized relation to be total and unambiguous over
the result domain: bool's two cells, int32's negative/zero/positive cells, or
uint32's zero/positive cells. Nonempty conditional rows must cover every cell;
overlapping applicable rows must agree. Duplicate output/selector keys reject
even when equal. Distinct outputs can share selectors but have different
state maps. Missing complements cannot be guessed from method names or bodies.

An output with no conditional rows has the existing unconditional-complete
normal-return meaning. Non-Boolean bare types use that default; Boolean bare
types must publish their default pair. An explicitly declared unconditional
Boolean output can therefore differ from a bare Boolean type. Do not delete
retained attachments/rows merely because another encoding has the same state
table. Non-Boolean conditional declarations use checked published ensure
relationships over `return` and `*output is init`; only relationships exactly
representable by this bounded codec are admitted here. Broader predicates
remain gated, not reduced to a guessed test number.

## State, authority and outcomes

Distinguish the live descriptor, its reserved destination generation and the
private initialization state of `T`. Passing the descriptor does not transfer
ownership of the caller's storage or create another responsible holder.

| Point/outcome | Required behavior |
|---|---|
| Formation/staging | Evaluate the exact eligible destination once at the argument's source position. Prove whole vacancy, type, access, extent/alignment and stable backing. No live child or outstanding partial acquisition may remain. An expected type or lexical unsafe block cannot manufacture that proof. |
| Active reservation | Preserve the fixed place/generation and its supporting dependencies through later arguments, invocation and completion. Conflicting caller aliases/reentry cannot read, initialize, retire, move or invalidate it. Initialization is not an ordinary exclusive-reference rule applied to all references. |
| Callee initialization | `*output` selects the exact destination without reading T. Initialize only eligible vacant storage. No whole-T read, ordinary reference to whole T or live-value replacement is allowed before complete initialization. Independently live children retain their ordinary partial-state rules; every stored child/value must satisfy its own validity and backing contract. |
| Completion inside the callee | Consume only the init capability: `ref<init T>` attenuates to `ref<T>`; outer `write` is preserved as `write ref<T>`. It permits later whole-place replacement only when that authority was originally present. Inner qualification is unchanged. No second initialization, descriptor rebinding or access upgrade follows. |
| Normal state `1` | After the ordinary body, explicit finally/cleanup, postcondition and epilogue obligations, publish one complete valid T with its actual dependencies under the caller's existing holder. There is no constructor/default synthesis. |
| Normal state `0` | No live T or retained partial acquisition remains. A checked implementation that initialized T must establish vacancy by an ordinary permitted retirement/transfer operation before this outcome; it cannot merely relabel written bytes as vacant. Otherwise its body fails checking. |
| Invocation never entered | Release only the unentered staged loan/reborrow; a fresh destination remains vacant. An existing parent capability resumes without new writes from this call. Preserve earlier argument effects and still-owned staged values; do not restore moved inputs. |
| Exceptional completion after entry, including a failed postcondition | There is no ordinary result or normal state relation. Output state is indeterminate to the caller, not the false/vacant arm. Preserve actual acquired/retained obligations; recovery cannot read, clean as complete T, or reuse the destination without independently admitted state evidence. |
| DoesNotReturn | Invocation has no normal continuation, result or output publication. Formation still returns a pointer value normally. Validate all types, selectors, loans, required carriers and signature-derived physical inputs even when normal outcomes are unreachable. |
| Divergence/failfast/process termination | Do not pretend the call completed or release backing still required by active execution. Failfast/termination adds no unwind, rollback or cleanup guarantee. |

Where cleanup is required, retain the accepted distinction between unpublished
partial acquisitions and complete values, their actual acquisition/field order,
and their responsible holders. This proposal adds no exceptional destructor
pass: automatic Cleanup is normal retirement; crossed explicit finally blocks
retain their existing exceptional role. Excluding owning payloads from the
first ABI does not erase cleanup of callee locals, staged inputs, supporting
backing or resources already acquired elsewhere. Neither output-state promises
nor rootless Copy payloads prove absence of failure; no NoThrow promise or
retired Effects bit is introduced.

Normal publication is the existing call boundary, not a multi-output commit/
rollback transaction. Body writes and other side effects remain observable
under their existing rules. Capture the ordinary result once, associate each
output with that invocation/destination generation, and retain guarded
maybe-live facts until the saved discriminator is tested. Ignoring the result
does not grant a read. Status reassignment, relevant writes, retirement,
replacement or owner movement invalidate correlations; an old discriminator
cannot revive a reused destination.

Forwarding `Helper(ref init *output)` is a scoped reborrow of the same
reservation. Suspend the parent capability and resume it with the child's
actual state; do not copy the non-Copy capability or reference its descriptor
slot. Reborrowing after initialization needs the actual eligible vacant-state
transition, not restoration of the consumed init permission.

## Dependencies, provenance and escape

Instantiate existing whole-source contracts against captured inputs and exact
output places. Checked outputs use Dependency destination `2`, their logical
formal ordinal and normal-publication phase `1`. The destination of that
relation is the **initialized inner T**, not the capability, descriptor slot
or caller output-storage address. Preserve explicit empty sets versus absence,
borrow-source-storage versus forward-existing-backing, and defining contexts.
Apply the source-contract proposal's dependency-default normalization; do not
borrow NativeOutput's empty-set default or silently overwrite authored sources.

An init formal is not a default input source. An explicitly declared inter-output
source is usable only on outcomes that publish that source complete, with its
actual backing already established before the dependent value is published.
Reject a dependent live output/result whose source is vacant on that outcome,
or an unsupported cyclic provenance justification. Raw pointer inputs do not
expose their pointees as checked source storage. `ForwardsBacking` never turns
the reserved output slot itself into independently surviving backing.

The descriptor's storage loan and the dependencies carried by the published T
are separate. Preserve actual storage generations, owner/code/load origins,
access, alignment and invalidation through forwarding, adapters and copies.
Initializing a pointer cell proves neither pointee initialization nor resource
ownership; initializing a code-pointer cell grants neither a runtime domain
nor code lifetime.

This first machine addition admits call-bounded use of the output address,
including checked synchronous helper reborrows, not escape of that address or
an uncompleted capability. It does not implement retaining an attenuated checked
descriptor in a result, field, capture or suspended operation. Such uses retain
their ordinary source eligibility and require their own complete representation/
dependency admission; do not reinterpret a source relation on inner T as that
missing escape permission. Existing independent phase-2 retention into an
otherwise admitted holder remains every-applicable-exit, not rolled back by
output failure. Dependency phase `3` deferred publication is not selector
attachment phase `3` and is not activated here.

## Same-runtime ABI addition

Use exactly the companion's selected Windows x64 layout/runtime-domain agreement.
Admit actual ordinary entries and bound semantic providers only after their
closed signatures, transitive hidden-input restrictions, runtime helpers,
initialization behavior, code publication and lifetime are established.
Neither same-process membership nor equal pointer width establishes admission.
Foreign conventions and independently replaceable precompiled entries remain
outside this addition.

This mapping is the entry ABI for the selected function, not an indirect-call
shim over a second private scratch/copy-back entry. Direct and indirect calls
to that function use the same checked destination and publication semantics.
If the body or its callers cannot be regenerated under this mapping, its
address is not admitted; the compiler does not expose a second code address or
infer a wrapper.

Each admitted checked output contributes **one eight-byte integer/pointer
argument carrier**, the address of the actual reserved T storage, regardless
of T's scalar/aggregate size. It is not a by-value indirect argument copy,
hidden result buffer, public stored-reference descriptor ABI or raw source ptr.
No status bitmap, generation word, environment, retain, pin, runtime-context
argument or second output register is added.

Retain the companion's physical sequence: hidden ordinary-result pointer when
required, then nonzero-payload formals in declaration order. A checked output
always consumes one position; a by-value unit formal does not. Positions 0..3
share RCX/RDX/R8/R9 with XMM0..3 for other floating inputs; later positions
use the existing stack slots, shadow space and call alignment. Hidden result
storage changes physical positions but no logical output/source ordinal.
RAX/XMM0 and indirect-result publication remain the ordinary result rules.

Caller backing must satisfy T's full object extent/alignment and remain stable
through the reservation. Do not substitute the companion's 16-byte-aligned
by-value argument-copy rule for a stronger destination alignment. Underaligned
fields reject an aligned reference; a checked optimization may remove storage
only while preserving exact identity, alias, validity, lifetime and publication
observations. No heap temporary, copy-back transaction or static scratch cell
repairs an unsupported place.

Callee access uses its fixed checked destination, not a writable address to its
parameter descriptor slot. Runtime/private state bookkeeping may be needed for
dynamic state tests, but it is not a public payload/live-mask protocol.
Use the same direct/indirect Terrum failure dispatch and actual runtime event;
do not add a foreign containment thunk, normal false result or NativeOutput
publication on unwind. Preserve platform nonvolatiles, frame/unwind information,
stack probing and all backing/helper lifetimes on admitted paths.

## Standard representation and exact new candidates

All additions here are **unapproved**. Reuse these existing or separately
proposed owners rather than allocate a new function-pointer output table:

| Surface | Required encoding/meaning |
|---|---|
| Function-pointer type | Standard seven-byte `0D` anchor form; proposed Terrum convention `0` remains owned by the [default-convention proposal](standard-function-pointer-default-convention.md). The anchor is not an actual target. |
| Formal type | Existing `0E`: `u8 form=0E, u8 qualifiers, TypeSpecRef innerType`. Qualifiers are `0` or `01` for outer write; gc/init-on-this-node and other bits reject. Preserve complete inner qualification and type identity. |
| Param | Existing row, mode `00`, flags `0` for an init formal, reserved zero. Neither reserved mode `01` nor the ordinary-ref implicit-adaptation flag encodes initialization. |
| Invocation clauses | Existing MethodDef-owned Effects `0001`, OutputState `0002`, Dependency `0004` and proposed bounded `000B` attachments, with their exact extents, occurrence keys and exclusive owner ranges. OutputState's typed PredicateGraph reference selects the same anchor's phase-3 attachment; private selector ordinals are not graph row numbers. |
| Capability closure | Retain the anchor/value/address/call companions' Features 1/2/4 and their full prerequisites; add the proposed bounded Feature `10/1` for retained selectors. Checked outputs alone do not require NativeOutput Feature 8. Required type/layout/failure semantics retain their own gates. |
| Address formation | Companion `E9`/Body `0D` actual-target plus explicit pointer-view carrier, not an address of the anchor. No address-operation allocation changes here. |
| Call | Existing general CallSite `02`, invoked by `90`/`E0`; captured register/Home pointer target, formal-order inputs and source-use `5=initialization destination` for each checked descriptor. Never use native-output source-use `6`. |

### Initialization capability formation

The registry currently reserves all PlaceSelection payload flags as zero.
Its ordinary DD/short-67 reference formation does not explicitly distinguish
vacant initialization formation and its requested outer write authority.
Recommend this exact **candidate amendment**, not an implicit interpretation
of current bytes:

| PlaceSelection payload `u16 flags` | DD result |
|---:|---|
| `0001` | `ref<init T>` |
| `0003` | `write ref<init T>` |

Assign bit `0001=InitializationReference`, bit
`0002=WritableAfterInitialization`; `0002` alone and bits `FFFC` reject.
These nonzero flags are valid **only for DD reference formation** with
`accessKind=3=Initialize`, an eligible selected place of exact
`referentTypeMapIndex=T`, proven vacancy and stable backing. Bit `0002`
additionally requires ordinary whole-place write authority after initialization;
initialization authority alone is insufficient. All other PlaceSelection uses
retain flags zero. The record's four-byte header flags/reserved fields remain
zero and are not this payload flag field.

DD remains four bytes: `u8 primary=DD, u8 control=destination register,
u16 PlaceSelectionRecordIndex`, with control high nibble zero and destination
R0..R13; R14/R15 reject for these new forms. Record `04` remains 24 bytes
including its four-byte header; the payload's final `u16` now has only the above scoped
candidate meanings. No new primary, prefix, short form, record kind, map
namespace or width escape is needed. Flagless short `67` remains ordinary
initialized-reference formation, not an initialization alias.

At the source argument position, DD captures/forms the capability and its
reservation before later arguments. A CallSite source-use-5 input selects a
live descriptor of exact `0E` type in a register or descriptor Home, **not
the Home containing T**. It supplies a scoped child reborrow, not a Copy of
that descriptor. Forwarding an existing init formal uses that same input
meaning without reading T or forming a reference to the parameter slot.

Typed stores through that checked descriptor use existing SemanticStore `05`
and `transition=Initialize`, then the ordinary replacement rules only when
outer write survived. This explicitly admits initialization-only checked
authority as a store destination while vacant; it does not require or fabricate
ordinary writable-ref authority. Block-entry value types/maps retain complete
reference types; operation/edge reconstruction carries actual places,
reservations and state. No serialized “verified” authorization bit is added.
Ending a loan does not retire T or reset its state.

Use the general call form for this addition so its non-Copy descriptor uses,
selected result, correlations and any hidden-destination preparation are
explicit. Do not retire a live explicit destination as preparation for
`ref init place`; it must already be vacant. The callee uses the same canonical
formal type and checked stores, not private scratch plus guessed copy-back.

Body-local indexes remain zero-based `u16` with counts through 65,536;
metadata typed rows retain their one-based 24-bit row domain and zero high
byte. Signature/output/dependency formal ordinals remain full-width `u32`.
An existing record with an optional `u16 formalOrdinal` cannot represent a
larger required correlation by truncation: that affected operation needs a
selected wider carrier or explicit unsupported-use rejection. There is no new
source arity limit or sentinel hidden in `FFFF`.

The [source-contract](standard-function-pointer-source-contracts.md),
[anchor](standard-function-pointer-anchor-contracts.md) and
[selector composition](predicate-feature-composition.md) proposals remain
prerequisites, including reconciliation of the published-predicate owners'
assignment discrepancy. The two flag candidates fill a distinct Body
formation gap; they do not activate unassigned general exception regions,
root/pin operations or arbitrary predicate programs. Select the entire exact
combined draft schema under
[format versioning](../specs/representation/format-versioning.md).
Equal `(1,0)` bytes do not authorize a flag-guessing alternate decoder.

## Private persistence: owner and explicit next decision

The unimplemented
[complete private pointer carrier](native-function-pointer-contract-private-persistence.md#exact-candidate-row-and-reference-layout)
owns candidate 97's inline Effects/selectors/checked-output/dependency payload,
signature-local selector ordinals, complete `fnptr#i` references and bodyless
address expectations. Keep its explicit convention translation and its twelve-byte
checked-output rows. Do not use Standard convention bytes, graph rows or host
legacy passing flags in those fields. Preserve exact selected types/contracts
in references, native sets/graphs and retained provider expectations.

**Implemented revision 104 does not persist the host's complete
checked-reference types or checked Body operations.** The unapproved
[shared private companion](checked-reference-private-persistence.md) now
selects one exact **candidate-105 composition over 104**, replacing its earlier
reference-97 candidate; implementation remains a gate.
Merely allowing nonempty OutputStates or
writing payload T into `parameterType` would still lose the formal's identity,
address and authority. The implemented revision 104 must continue to
reject facts it cannot transport; neither its legacy tag-21 output expression
nor NativeOutput tag 41 is the encoding of this proposal.

The private decision, owned by that candidate-105 companion, supplies a
lossless complete initialization-reference type-use and checked formation/
reborrow/store representation across:

1. Signature parameters, actual Function formals/variables and expression types,
   including outer write and complete inner qualification.
2. Fresh place formation and scoped forwarding, with destination identity and
   backing reconstructed from operations rather than persisted runtime tokens.
3. Nested types, bodyless declaration expectations, normal state correlations,
   traversal/remapping, source-free binding and set/graph closure admission.

Use the companion's shared private complete-type mechanism, not
function-pointer-only IsInit bits or a second native-output tuple. **This
checked-output proposal itself assigns no private revision, type code,
escape, expression tag or optional trailer**; the companion is their sole
owner. Select exactly its whole **104 plus explicitly reconciled deltas / 105**
before affected private writing/reading is enabled. The old reference tags
42–45 cannot replace 104's existing allocations; the companion uses 49–54.
A revision number alone is not agreement, and 104 or earlier-97 drafts are
not alternate decoders or aliases.

This is a blocked transport path, not permission to claim lossless private
support, silently retain only Standard metadata, strip outputs for native
packages or permanently exclude the source-to-Standard producer. Standard and
direct bound lowering can follow the concrete contract above; any route that
requires private persistence remains gated on exact selection and implementation.

## Verification, provider binding and transformation

* **Source and bound producers:** check authored bodies against the complete
  published state/access/dependency contract on all applicable normal paths,
  plus existing failure/cleanup rules. Generalize the bound parameter type and
  introduce/reuse a canonical checked initialization-reference operand carrying
  the exact place or scoped parent reservation. Keep descriptor state separate
  from referent state. Preserve source-order capture/staging and explicit
  argument-to-formal correspondence; do not attach target-body promises to a
  call site.
* **Mandatory verification:** validate exact formal kinds, descriptor formation,
  vacancy, stable backing, aliases, scoped reborrows, state transitions,
  selector/result domains and dependency applicability on rewritten programs.
  Admitting destination `2` must also check that it selects an init formal and
  the right publication phase, not just relax its numeric range. Reject an
  output attached to ordinary ref/value/raw-pointer formals.
* **Consumers:** check selected schema/framing, checked count/extent arithmetic,
  reserved fields, owner/range/cycle rules, complete TypeSpec/Param references,
  selector locality and domains, output keys/coverage/conflicts, dependency
  endpoints/phases, Body input/type/place correspondence, capability closure,
  provider binding and selected runtime/ABI support before affected use.
  Producers remain responsible for truthful body facts; mandatory safe
  consumption does not require semantic re-proof of arbitrary provider bodies.
  Optional audits are separate. A false published promise can cause undefined
  behavior; a “trusted” bit or runtime byte check does not repair it.
* **Formation/projection/replacement:** preserve exact complete ordered
  types/modes, selector attachments, output destinations, phases and states.
  Apply only the source proposal's supported directed Effects/dependency
  refinements. A false/complete output is not stronger than false/vacant:
  the old caller may initialize that presumed-vacant slot. Do not erase a
  conditional relation to obtain an unconditional-complete default. Bind each
  retained expected declaration independently; target/provider equality of
  machine addresses or carriers is not satisfaction.
* **Substitution/traversal:** visit the complete inner T, outer qualification,
  nested pointer signatures, target and every argument/place/parent reservation,
  Effects, selectors and all output/dependency subjects in shared visitors,
  rewriters and imported materialization. Substitute enclosing formals in the
  defining context, normalize and revalidate layout, source eligibility and
  state/dependency relations, then recompute call plans. No method-generic
  environment or receiver is invented.
* **Remapping:** rebuild TypeSpec references, anchors and MethodSig/Param ranges,
  clause owners and predicate attachments/footprints, Body TypeMap/MethodMap/
  Home/record references, actual target Imports and formal/source correspondence.
  Preserve logical formal order and published attachment ordinals. If a valid
  transformation explicitly renumbers attachments, update every referring
  OutputState in that same transformation. Rebuild code/block/record offsets,
  map counts, section extents and affected SourceMap/debug data. Do not substitute
  matching bytes or equate anchor deduplication with target deduplication.
* **Lowering/runtime:** emit matching entry and indirect call plans, actual
  destination addresses, normal-only publication, guarded result correlations
  and the admitted failure path. Retain owners, backing, code/load/runtime and
  unwind helpers through all uses. AOT/JIT optimization cannot replace the
  checked contract with an unsupported copy-back, pin or foreign adapter.
  Replacement does not retarget already escaped pointers or establish a
  hot-unload protocol; the same-runtime closure must still be compatible and
  jointly regenerable under the companion agreement.

## Proposed acceptance cases and gates

These are required tests, **not test results**. Use public compiler APIs,
unrelated synthetic providers and independently constructed source-free
artifacts. Keep syntax/semantic, representation, emission and supervised runtime
evidence distinct.

| Area | Positive, negative and boundary cases |
|---|---|
| Source/type identity | Form/copy/store/pass/return pointers with checked output signatures; compare readonly versus outer-write init refs and ordinary/raw forms. Preserve exact inferred nondefault contracts and fixed bare defaults. Reject legacy parameter aliases, implicit vacant borrowing, default init-ref arguments and descriptor rebinding. |
| Defaults/selectors | Boolean true/complete-false/vacant, explicitly complete outputs on both Boolean outcomes, non-Boolean/unit unconditional completion, explicit zero/nonzero and negative/nonnegative relations, and independently mapped multiple outputs. Check int32 minimum/zero/maximum and uint32 zero/maximum. Reject holes, conflicting overlaps, duplicate keys, wrong result types, foreign-owner selectors and unsupported general graphs. Preserve same-state overlaps and retained attachment identities without guessing a new type. |
| Authority/state | Whole-vacant scalar and aggregate outputs, partial field initialization followed by complete validity, and outer-write replacement only after initialization. Reject live/partial/maybe-live/reserved actuals, duplicate/overlapping destinations, pre-init reads, readonly-to-write upgrades, double initialization and false/vacant returns with retained initialized state. Distinguish descriptor/referent state and initialized optional-null/vacancy without asserting an unsupported optional ABI. |
| Provenance/forwarding | Helper reborrows preserve exact places/generations and resume the parent state. Exercise explicit empty and backing-only dependencies on a published pointer/code-pointer cell, plus admitted inter-output sources. Reject copied/escaped uncompleted capabilities, dead local backing, extra retention, vacant sibling sources and output-slot escape disguised as an inner-value dependency. |
| Failure/cleanup | Compare direct and indirect later-argument failure, invocation/body failure and postcondition failure under the same runtime; no fabricated result, rollback or false/vacant recovery. Observe explicit finally and still-owned staging duties without an invented unwind Cleanup pass. Reject unsupported owning/task outputs rather than accepting a pointer-sized approximation. |
| No-return | Preserve all contract rows, argument checks and physical inputs for unit/non-unit DoesNotReturn targets, including hidden ordinary results. Formation continues; invocation does not publish. Test checked weakening separately and never treat emission of a terminal trap as execution evidence. |
| ABI/backing | Scalar and aggregates of extents 1/2/4/8 and larger, packed/underaligned versus full/over-aligned storage, mixed floating/value/unit/output inputs past four machine positions and fourteen logical registers. Combine hidden ordinary results with several outputs; verify actual addresses, no by-value copies, correct stack slots and distinct recursive/reentrant backing. Decline addressless destinations and unsupported carrier families. |
| Native separation | Reject OutputState on a raw-pointer fnptr formal and NativeOutput erasure through pointer formation. DD/source-use `5` never becomes DF/source-use `6`; pointer-cell completion does not initialize its pointee. Equal Terrum/foreign carriers do not permit retagging or foreign callbacks into this domain. |
| Standard structure | Independently round-trip `0D` plus `0E`/Param `00`, exact clauses/selectors and DD flags `0001/0003`. Reject mode `01`, init-ref implicit flag, gc/unknown qualifiers, flag `0002` alone, non-DD flag uses, wrong record header flags, live-place formation and a Home T substituted for a descriptor input. |
| Widths/remapping | Exercise map/record/Home indexes 0 and 65,535 and counts 65,536; reject one-past limits and arithmetic overflow. Preserve full-width formal ordinals; decline unrepresentable optional u16 correlations. Permute/pack/deduplicate structural rows and nested signatures, remap selectors and debug/source offsets, and retain actual address-only provider edges. |
| Correlation/lifetimes | Copy the saved discriminator, ignore it, reassign it, mutate aliases and reuse a destination in a loop. Permit reads only with current valid state evidence; reject stale-result revival, dangling backing, conflicting later arguments and reentrant access to an active reservation. |
| Providers/replacement | Source-free consumer preserves exact expected output types/states/dependencies after intermediate forwarding. Reject changed failure-output state, lost backing, wrong convention/domain, anchor-as-target and unchanged incompatible precompiled entries. Equal signature shape cannot erase a retained expectation. |
| Private transport | Until the explicit private decision is selected, reject affected writes/reads rather than emitting payload T plus legacy IsInit or dropping rows. After that gate, require exact type/Body/expectation round-trips, independent malformed inputs, remapping and set/graph binding tests under the selected complete schema. |
| Qualification | Run actual admitted JIT and AOT calls with normal/failure/non-return observations under explicit supervision and headless gates. Record final compiler/input identity, configuration, selected schema/runtime and actual matched/passed/skipped cases. Parsing, a structural reader test, an emitted image or legacy prototype execution does not qualify this addition. |

Implementation acceptance requires complete checked parameter/operand/state
propagation, the selected Standard source producer and consumer path, matching
entry/call lowering, dependency/replacement/remapping coverage, and the explicit
private decision wherever that transport is required. Broader source contract
spelling, general stored-reference/owner/deferred ABI support and independently
replaceable runtime boundaries remain separate expansions, not silently chosen
prerequisites for this bounded addition. Provisional direction, normative
adoption, implemented support and released stability remain distinct.
