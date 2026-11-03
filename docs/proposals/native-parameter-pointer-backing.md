# NativeParameter pointer-value backing at calls

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** Every new decision below is an
unapproved, provisional recommendation. This document changes no normative
specification or implementation and supplies no executed-test, emission or
runtime evidence.

## Decision and scope

Recommend **unsafe responsibility for genuinely opaque raw backing, with
mandatory preservation and checking of known facts**. A positive
NativeParameter extent is not restricted to direct address expressions, but
copying an address into a typed pointer cannot bypass its known size. An
explicit unsafe invocation may supply the developer's proof for backing that
the raw boundary does not describe. It cannot turn a known violation, a lost
required fact or an unsupported representation into an opaque valid buffer.

This completes the call-site meaning of the bounded typed
Read/Write/ReadWrite, NoEscape, minimum-element-count descriptor in
[NativeImport function-pointer contracts](standard-native-import-function-pointer-contracts.md).
Apply the same rule wherever a call form already admits that descriptor,
including its supported indirect projection; this grants no new call-form
eligibility. Preserve NativeParameter as access to initialized backing,
distinct from NativeOutput's vacant-cell publication protocol.

No source syntax, view type, attribute registration, adapter, trust flag,
wire field or ID is proposed. Standard NativeParameter schema adoption,
private revision 97, raw outputs, provider replacement and function-pointer
calling conventions remain with their existing owners and gates. There is
no implicit marshalling, pinning, rooting or lifetime extension.

## Why a decision is still needed

The gap is **not** the integer domain, minimum-count unit or existence of
unsafe obligations. It is whether a positive extent requires independently
proved backing for every non-null pointer value, and how genuine opacity is
distinguished from losing an established fact.

| Evidence owner | Existing decision or inspected limitation |
|---|---|
| [Safe and unsafe boundaries](../specs/language.md#safe-and-unsafe-boundaries), [pointer dereference](../specs/language/references.md#dereference-and-member-access), [lifetimes](../specs/lifetimes.md#passing-and-storage-access) | Unsafe transfers proof responsibility, not write authority or a known invalid lifetime. Address formation and descriptor copies preserve established backing facts. These rules do not define NativeParameter's quantitative admission policy. |
| [Native boundaries](../specs/native.md#native-declaration-roles), [NBP-01 through NBP-04](../specs/conformance/native-boundaries.md#4-pointer-dependency-and-lifetime-boundaries) | Pointer spelling supplies no extent, initialization or retention promise. Accepted Standard input still rejects NativeParameter clauses; prototype support is not accepted Standard semantics. |
| [Foreign invocation staging](standard-native-import-function-pointer-contracts.md#one-invocation-and-its-outcomes), [candidate parameter meaning](standard-native-import-function-pointer-contracts.md#candidate-nativeparameter-row) | Require actual access/extent/validity checks; positive extent counts complete pointee elements, while zero supplies no static minimum. They do not choose proof-only versus unsafe-trusted opaque backing or specify loss/merge behavior for that size fact. |
| [Operation-derived provenance](reference-provenance-carriers.md#identity-and-availability), [field-copy summaries](reference-field-value-provenance.md#failure-cleanup-and-opaque-calls) | Preserve established origins and distinguish unavailable facts from known-empty dependencies. Exact-fact-dependent uses without supported facts refuse. Neither proposal chooses when this raw foreign extent obligation can instead be an authored unsafe responsibility. |
| [`DirectNativeParameter` and address nodes](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Extent is `uint`. Call-only BoundNativeStorageAddress retains ElementCount, ElementStride, requested ByteExtent and index; ordinary BoundNativePointerAddress instead identifies its local, and the fixed-array pointer node retains layout and index. The direct storage-address node proves the declared local backing and requested suffix; it is not a quantitative fact for arbitrary pointer values. |
| [`Binder.DirectNativeImport.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.DirectNativeImport.cs) | TryDecodeNativeParameterAttribute admits `0..uint.MaxValue`. Direct `&values[i]` NativeParameter formation over initialized local fixed arrays retains the typed index and Q*S requested extent; constant indexes check Q against the suffix statically, while native lowering evaluates dynamic indexes once and checks the signed original index plus suffix before the call. Typed pointer values remain a distinct path: literal null rejects for positive extent, while non-null capacity depends on provenance analysis. |
| [`DirectNativeImportContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs), [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), [`address verification`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.DirectNativeImport.cs) | MatchesAddress checks storage geometry; MatchesNativeParameterArgumentType checks formal/pointee/access and known constant null, not a non-null value's capacity. Direct/vtable call verification and VerifyIndirectCall use these paths. Existing lifetime verification is not a size comparison. |
| [`SemanticVerifier.TokenSpans.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.TokenSpans.cs) | BackingDependencies tracks NativePointerSources and NativePointerMayBeUnknown; projected NativePointerFieldValue stores sources and MayBeUnknown. Neither carries a quantitative element/byte capacity. The unknown bit alone cannot distinguish the policies considered here. |
| [`DirectNativeImportTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs), [`NativePointerModuleTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativePointerModuleTests.cs) | Source cases cover array capacities 2/3/5 against minimum 3, the unsigned source boundary and positive-extent typed-null rejection, including a forged bound-program check. Pointer tests cover selected fields, containing-value copies and copy-origin calls for lifetime provenance. Those assertions do not settle non-null size admission. |

These are source observations, not results from running those tests. Merely
adding an extent member to the ledger would leave its treatment of an opaque
incoming pointer undecided. Conversely, treating every non-null typed pointer
as sufficient would discard the already enforced single-local/array bounds.

## Alternatives

All three alternatives are unapproved.

| Candidate | Assessment |
|---|---|
| **1. Trust opaque backing only at an explicit unsafe boundary; check known facts — recommended provisionally** | Fits the accepted authored-unsafe trust boundary and non-owning raw pointer representation. Completes the existing minimum-count direction without requiring a new public carrier. Requires a real distinction between opaque backing and failed preservation of required facts. |
| **2. Require an independently typed/view-like length or complete provenance contract before any positive extent** | Offers a stronger mechanically composable caller proof. It needs a separately designed carrier/extraction or published backing contract; a second scalar argument or raw pointer type is not such evidence by itself. Useful future API design, but a substantive prerequisite not selected by current NativeParameter direction. No automatic adapter is implied. |
| **3. Reject every non-null pointer value with positive extent unless exact backing is proved** | A coherent conservative restriction while support is missing. As the completed rule it rejects genuinely opaque foreign buffers even in authored unsafe code, and demands more than a sufficient lower bound. Existing unsafe and minimum-count direction does not justify that general restriction. |

## Provisional call-site rules

The remainder of this section is the **unapproved candidate**, not current
Standard conformance.

### Units and selected region

Preserve the full unsigned `0..4,294,967,295` extent domain. A positive count
requires at least that many contiguous, complete, initialized elements of
the formal's exact pointee type at the supplied address. It is not bytes, a
maximum access count, an allocation request or a promise to initialize those
elements. Other provider preconditions may require more backing.

Derive element stride and required alignment from that complete type and the
selected directly interoperable target layout, including aggregate tail
padding. An extent on a pointer to pointer cells counts those cells, not
allocations reached through them. Byte capacity alone does not establish
typed initialization, access or provenance. The existing opaque-void
restriction remains Read/NoEscape/extent-zero; no positive byte-count
interpretation is introduced for it.

An admitted address of one local or one standalone subobject supplies one
element, not adjacent fields or unrelated allocations. For an admitted
element address into an established contiguous array, the available region
starts at the selected element and ends at that array's end, not at the
start of its whole allocation. Derive remaining capacity from the captured
index and actual region. This selects no new address operation, pointer
arithmetic or one-past-end formation rule.

Check count-times-stride, selected offsets and required address ranges with
nonwrapping arithmetic under the selected target limits. A finite known
region with an unresolved offset must establish the minimum using supported
bounds/refinements or an independently admitted explicit check; it cannot be
relabeled externally opaque because the analysis lacks that precision.
No hidden runtime bounds check is added by this proposal.

### Known, opaque and unavailable are different

For each possible actual value at Invoke, apply this admission table:

| Available facts | Provisional outcome |
|---|---|
| Known live typed region with a sufficient lower bound and required authority/alignment | Admit under the ordinary explicit unsafe call obligation. Exact allocation identity/size beyond the needed proof is unnecessary. |
| Known undersized, invalidated, uninitialized, under-aligned or unauthorized region | Reject the violating call. Unsafe does not repair it. A larger alternative, identical pointer bits or an unknown alternative cannot mask it. |
| Genuinely opaque raw input/result boundary, with no established quantitative region | Permit the unresolved extent/validity responsibility only in the explicit unsafe invocation. The developer must establish the actual backing, access, alignment and lifetime externally. The value remains opaque; acceptance does not publish a capacity or checked-reference guarantee. |
| Lost fact from a fact-preserving operation, missing required dependency/support carrier, unsupported required derivation, or exhausted analysis | Refuse the affected extent-dependent use. Do not convert this failure into opaque external backing, zero capacity, an empty dependency set or a successful proof. |

Opacity describes a raw boundary's deliberately unproved backing, not a
source annotation, serialized authorization bit or a synonym for
NativePointerMayBeUnknown. Known restrictions and every independently
required supporting dependency still apply when some other dimension is
opaque. An ordinary safe wrapper may contain this trust boundary in its
explicit unsafe implementation; it must meet its own published safe
contract. It does not silently transfer that proof to safe callers.

Extent zero means **no static minimum**, not a known zero-length region,
unbounded permission, no access, a terminator guarantee or absence of
dependencies. Preserve all known access, initialization, stability and
cleanup obligations. Where null is otherwise admitted by the selected
source/type contract, zero adds no non-null requirement; positive extent
cannot be satisfied by a known null alternative. Copies and supported flow
refinements preserve that fact. This does not make non-null `ptr<T>`
nullable, replace required presence checks or validate a null dereference.

### Access, retention, layout and call timing

Read needs readable initialized backing; Write/ReadWrite also needs
established immediate write authority and already initialized elements.
Write is not NativeOutput and cannot admit vacant or partial storage.
Retain nominal/member restrictions, required alias separation and every
owner/guard/root/pin/cleanup dependency. A numeric size proof supplies none
of those independently.

Known alignment must satisfy the formal's typed access for every required
element at its actual stride. A packed member address cannot acquire
stronger alignment merely from a pointer type or a sufficient byte count.
Unsupported layouts decline use; no copy buffer, unaligned adapter or pin is
synthesized.

NoEscape remains the provider's no-retention promise for the supplied
address/region through the complete invocation, including applicable
exceptional exits. It grants no ownership or backing extension. Stage each
pointer value once in ordinary written argument order, bind the actual
formal, and check its required backing at Invoke after later-argument
effects. Later rebinding of the source pointer slot does not redirect that
snapshot; retirement or mutation invalidating its backing still matters.

### Preservation through ordinary value flow

These are semantic preservation requirements, not a new public provenance
or mutation-effect contract.

| Operation or boundary | Provisional requirement |
|---|---|
| Address formation | Derive the selected region, geometry and supporting storage generation from the admitted operation. Do not count the pointer descriptor's own slot as pointee backing. |
| Copies, aliases, moves and access attenuation | Preserve the pointer value's established region facts and dependencies. Moving a descriptor does not move its referent; reducing access cannot increase capacity or extend validity. Rebinding installs the new value's facts without rewriting surviving copies. |
| Joins and loops | Retain every feasible known region/null alternative and independent opaque alternatives. All known alternatives must meet the minimum; retain a common lower bound only when valid for all. Supported guards may eliminate alternatives. Widening cannot erase a known restriction into externally opaque backing. |
| Fields and pointer-cell reads/writes | Track the selected field/current stored value, including nested fields and eligible containing-value copies. Slot aliases observe permitted later writes; already captured pointer values do not change. Invalidate affected facts after overlapping or insufficiently modeled writes, rather than reusing a stale extent. |
| Calls/returns with a supported value-copy/origin guarantee | Instantiate the region facts from that invocation's staged sources, including selected fields, using the existing summary contract. Retain the original declared dependency bounds independently. Caller/callee parameter copies and hidden ABI result storage are not new backing regions. |
| Opaque call results or incoming raw parameters | Where the complete boundary admits a raw pointer but supplies no region guarantee, use the opaque rule above. Do not infer a fresh allocation, count or precise forwarding from a name, private body or matching type. A dependency union is a support bound, not proof that the result selects each argument's region. |
| Erasure, adapters, rewriting and persistence | Preserve every fact of a value the operation promises to preserve, or decline the required use. A preserved value cannot deliberately lose its small bound to gain unsafe admission. A genuinely new opaque result is not a promised copy; missing required source/lifetime information is not mere numeric opacity. |

Whole-source DependsOn/ForwardsBacking does not itself publish an element
count or exact result-region mapping. Use the existing
[copy-origin summary](reference-field-value-provenance.md) only within its
admitted scope; this proposal adds no summary attachment for foreign results
or NativeOutput. Unknown mutation and failed calls do not restore previous
facts by assuming rollback.

## Declaration, metadata and failure obligations

The declaration owns its exact pointee/access/retention/minimum contract and
the provider owns the truth of its foreign promises. A positive declaration
extent does not allocate backing or prove every call possible. The call site
owns satisfaction against its actual staged argument, including an authored
unsafe proof for genuinely opaque backing. Retaining the declaration's
extent in a function-pointer type is necessary but does not check the
argument's size.

Source binding and mandatory bound-program producer verification must apply
the same call-site rule to direct addresses and pointer values, not just
literal null or syntactic address arguments. Shared traversal/rewriting and
lowering must not lose relied-on regions. This calls for general quantitative
facts alongside existing provenance, not API recognizers or a second
ownership system.

Source-free use keeps the [producer/consumer boundary](../specs/representation/reader-trust.md):
producers publish truthful facts; consumers perform required exact-schema,
shape/domain, bounds/reference, support and binding checks before affected
use, without semantically re-proving arbitrary published Body facts or
foreign promises. Consumers can reconstruct local facts from admitted
operations and consume supported published summaries. Required facts lost
during transformation are the transformer's responsibility, not permission
to trust an empty ledger.

Preserve complete types, unsigned extents, their owning declaration/formal
and original dependency expectations through remapping and dependency
binding. Any stronger published region guarantee actually relied on remains
a retained obligation under [binding and replacement](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts).
This adds no replacement relation, schema or permission to serialize
transient verifier state. A required summary without an admitted carrier
remains unsupported; unsafe cannot authorize dropping it. Existing selected
schemas and companion proposal gates remain necessary.

Known violations diagnose/refuse before the affected call is lowered or
executed. Unsupported required facts also decline use; no truncation,
implicit repair, success-shaped null/default result or new runtime failure
category is selected. If a developer/provider's opaque unsafe promise is
false, execution relying on it has no safety guarantee; this proposal
promises neither a runtime size probe nor detection of that semantic lie.
Existing explicit checks and their specified failures remain unchanged.

## Adoption owners and minimum regression matrix

If later adopted, add this quantitative admission/preservation rule to the
native call contract and its conformance cases, and complete the companion
foreign-invocation proposal's extent-checking text. Reuse the reference,
lifetime, data-layout, dependency and reader-trust owners without replacing
their general rules. Standard NativeParameter schema selection and every
unrelated protocol remain separate decisions. No normative owner is edited
by this proposal.

These are **future regression requirements, not checks run here**. Exercise
public compiler APIs and unrelated synthetic providers. Combine direct,
already-admitted vtable and contract-carrying indirect call variants where
applicable; keep checking, decoding, emission and execution evidence distinct.

| Case | Minimum required outcome |
|---|---|
| Known capacity | Minimum 3 against arrays of 2/3/5: reject/admit/admit both as direct addresses and copied typed pointers. A single-local pointer cannot satisfy minimum 2. A sufficient lower bound need not reveal the full allocation. |
| Selected geometry | First/interior/last array elements use remaining capacity, not the whole array length. Include dynamic indices with sufficient/insufficient admitted bounds, standalone subobjects, padded aggregates, packed/over-aligned storage and nested pointer cells. Counts are elements; checked byte/offset/target overflow rejects. |
| Integer boundaries | Zero, one and full `uint.MaxValue` survive supported declaration/type/dependency transport; one above the source domain rejects. Large valid declarations allocate nothing; impossible target call ranges reject rather than narrow. |
| Null and zero | Where null is otherwise admitted, literal/copied/field/joined known null cannot satisfy a positive extent. Zero adds no minimum and no lifetime/alignment/initialization waiver. Preserve ordinary optionality/presence rules. |
| Opaque unsafe | Raw incoming/foreign-result pointers may satisfy the unresolved responsibility at an explicit unsafe invocation without inventing a capacity fact. Missing lexical unsafe fails; an unsafe declaration alone does not authorize its body. A false external promise is not a promised runtime diagnostic. |
| Lost versus opaque | Dropped known-small bounds, missing required backing/source carriers, unsupported preservation and exhausted analysis refuse the required use. Neither an empty source set nor the current unknown bit counts as authorization. |
| Copies, fields and joins | Preserve a small old copy after its slot is rebound to a larger region; distinguish small/large sibling fields and containing-value copies. Join 3/5 for minimum 3 succeeds; 2/5 and 2/opaque fail unless a supported guard removes the short branch. Cover loop-carried and null alternatives. |
| Calls and returns | Supported whole/field copy-origin summaries preserve capacity through staged arguments and results without body inspection. Opaque results invent no selected region. Whole dependency unions are not exact extent maps; ABI temporaries never become backing. |
| Access and lifetime | Read versus Write/ReadWrite, readonly backing, vacant/partial elements, ended owners/guards/pins, known alias conflicts and nested pointer cells keep their existing obligations. Write never grants NativeOutput publication or implicit support acquisition. |
| Staging and invalidation | Later arguments rebind a pointer slot, change a referenced pointer field, retire backing, or fail. Preserve the captured value, recheck required backing at Invoke, invalidate affected current facts and never assume rollback. |
| Producer verification | Mutate a bound call's minimum or known region so type/access still match but capacity is short: producer verification rejects every admitted call form. Preserve existing forged typed-null checks. Do not impose semantic Body re-proof on ordinary metadata consumers. |
| Source-free consumers | Reconstruct admitted address/copy/field flow after supported serialization/remapping; retain supported callable summaries, complete types and original dependencies without source. Malformed structures, unsupported required contracts and incompatible bindings reject. Structurally valid false producer facts remain a separate trust violation, not guaranteed consumer detection. |

**Remaining user decision:** formally select or reject alternative 1 and
its distinction between opaque backing and unavailable required facts.
It is the provisional direction only under the authorized workflow.
Standard schema/summary-carrier adoption and implementation/qualification
remain independent gates, not decisions silently made here.
