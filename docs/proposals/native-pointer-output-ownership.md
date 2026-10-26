# Scalar and pointer native-output cells

**Status: unapproved; provisional direction only under the user's authorized
missing-contract workflow.** That authorization permits subsequent work to
use this recommendation provisionally. It does not approve its source
registration, extend normative specifications, establish implementation
support, or stabilize an encoding. This document changes no code, sample or
normative owner.

## Decision and scope

Extend [aggregate NativeOutput](aggregate-native-outputs.md) from aggregates
to **one wholly vacant, caller-owned cell of any closed, positive-extent,
fixed rootless, directly interoperable `Blittable Copy` value type T**.
Keep its exact `write ptr<T>` formal, `&init` argument, normal-result
conditions, reservations, publication and complementary vacancy rules.
This admits scalar cells such as `write ptr<uint32>` and address cells such
as `write ptr<ptr<void>>` through the same contract, not separate scalar,
handle, COM or API-specific output mechanisms.

**NativeOutput publishes T, not ownership of a resource represented by T.**
Publishing a raw pointer initializes the pointer cell, not its pointee.
Publishing an integer initializes that integer, not an operating-system
object or a release obligation. An authored unsafe boundary can additionally
assert a foreign resource-acquisition contract; a checked owner must be
established by an explicit authored wrapper under the rules below.
Do not add an ownership flag, automatic adoption, synthesized release or
compiler-recognized owner-constructor name.

This intentionally supersedes only:

* The aggregate-only T eligibility and corresponding general scalar/pointer
  output exclusion in the aggregate proposal and its
  [private-persistence candidate](aggregate-native-output-private-persistence.md).
* The aggregate-only **destination** eligibility in
  [NativeOutput source dependencies](aggregate-native-output-source-dependencies.md).
  Its input/receiver source recipes, relationship meanings and target-only
  restriction otherwise remain unchanged.

It preserves their exclusion of implicit pointer/COM ownership acquisition,
live in/out replacement, output-address retention, arrays/prefix publication,
deferred completion and general failure-output values. It does not revive
the retired ordinal-based NativeOutput prototype or NoThrow promise.

### Why one amendment, not a resource-output role

The smallest coherent amendment is to remove the aggregate-only restriction
while retaining the complete cell protocol. Scalars, raw addresses and
aggregates need the same vacancy, exact extent/alignment, result correlation,
reservation and publication mechanisms. An integer is not distinguishable
as a resource handle by its type width or spelling.

A separate role such as `NativeOwnedOutput` would be materially different,
not merely a second name for NativeOutput. It would need an affine
acquisition holder before typed publication, exact release/provider origin,
transfer and adoption points, settlement on every failure, release-failure
states, dependencies and new declaration/Body persistence. A release symbol
or an `Owned` Boolean on a Copy pointer cannot supply that contract.
**Recommend the cell amendment and explicit authored ownership boundary.**
Do not implement a partial resource-output role to make a sample compile.
First-class checked acquisition directly from raw output cells is excluded
from this decision, rather than silently represented by pointer liveness.

## Candidate source registration and eligibility

All additions here are **unapproved source/API choices**. Resolve
`Terrum.Interop.NativeOutput` and `Terrum.Interop.NativeOutputCondition`
through the selected Core registration, including their exact nominal
identities and members. Fully qualified text, a same-named user attribute,
matching enum bits or an `extern` modifier alone supplies no registration.
Aliases may denote those exact resolved identities.

Keep one required named argument, `When: NativeOutputCondition`, and no
positional-ordinal overload, ownership argument, extent or implicit
NativeParameter combination. Complete the candidate source enum as
`int32`, with `NonZero = 0` and `NonNegative = 1`; these are **proposed
source enum values**, not prototype conditions or serialized selectors.
Unknown values, flags, wrong nominal enum types and duplicate/unknown
arguments reject. A selected Core library must match this exact schema;
its binary Module/type/member identities come from that registration,
not invented global DefId numbers.

The schema is primary-contract-only and nonrepeatable per explicit formal.
An inline attribute and `[param(name): ...]` select the same formal and
cannot both supply its occurrence. The named target is resolved against
that declaration, not a caller variable or a parameter ordinal. Inheritance
retains the declaring method's contract and checked formal mapping.

Admit only a synchronous **bodyless direct callable NativeImport**, or an
exact **bodyless foreign native-interface method** with independently
admitted interface layout, effective native convention, live receiver and
dispatch/provider contract. Both remain caller-unsafe. Ordinary helper or
callback bodies, authored interface implementations, fields, receivers,
results, secondary partial declarations and arbitrary function-pointer type
uses acquire no new attachment permission. A prototype vtable marker or
equal slot number is not an admitted foreign-interface identity.

For each selected formal:

1. Require ordinary parameter mode and the exact complete type
   `write ptr<T>`. There is no nullable destination address, checked-reference
   substitution, access upgrade or `out`/`init ref<T>` alias.
2. Require T to satisfy the decision's complete value/layout predicates.
   These are general target/type facts, not a list of recognized handle
   names. Eligible leaves include admitted integer and other scalar
   primitives, raw data pointers, plain native function pointers, and
   independently admitted enum/Transparent carriers. Existing eligible
   aggregates remain eligible.
3. For a raw pointer T, preserve its complete pointee identity and immediate
   access qualification. The **cell's** size/alignment is the pointer
   descriptor's; no positive extent, initialized contents or accessible
   allocation for its pointee follows. `ptr<void>` can be opaque.
4. Preserve nominal scalar/enum/wrapper validity. Equal integer width or a
   one-field record is not a Transparent commitment or valid native handle.
   A unique owner is not eligible merely because its representation is one
   pointer: its responsibility prevents the required Copy contract.
5. Require any optional or richer descriptor T to have its own complete
   directly interoperable stored representation. Do not infer an optional
   layout from null-as-absence, raw-pointer size or the source `?` spelling.
   This proposal assigns no optional, checked-reference, class, root, view,
   task or owning-carrier representation. Unsupported T rejects.

The result remains exactly `int32` or `uint32`. `NonZero` means `result != 0`
for either type, including negative signed results; `NonNegative` means
`result >= 0` for `int32` only. Neither means `result == 1`, non-null output,
successful ownership acquisition or validity of some other output.
There is no new Always/Negative/failure-live condition. Several outputs may
select different existing conditions over the one captured result.

### Source fragments

These are **proposal-only fragments**, not runnable or qualified examples.
The synthetic provider promises a complete uint32 and a non-null opaque
pointer on nonnegative return, with no borrowed receiver/formal sources and
no native resource transferred by the cookie. No API name selects this
behavior.

```terrum
[Terrum.Interop.NativeImport("cellprobe.dll", "TryReadCookie")]
[param(count): Terrum.Interop.NativeOutput(
    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
[param(cookie): Terrum.Interop.NativeOutput(
    When: Terrum.Interop.NativeOutputCondition.NonNegative)]
extern unsafe int32 TryReadCookie(
    write ptr<uint32> count,
    write ptr<ptr<void>> cookie);
```

Inside an ordinary statement scope:

```terrum
unsafe {
    init uint32 count;
    init ptr<void> cookie;
    int32 status = TryReadCookie(&init count, &init cookie);
    if (status >= 0) {
        uint32 observedCount = count;
        ptr<void> observedCookie = cookie;
    }
}
```

These copies grant no dereference, release or typed interface authority.
The non-null complete type is a provider obligation on success; this is
not the prototype spelling `ptr<void> value = null`.
Preinitializing either local and passing ordinary `&local` is not equivalent
to the proposed vacant-cell reservation.

## Reservation, outcomes and dependencies

Reuse the prerequisite's whole-local `&init` protocol, including once-only
source-order evaluation and formal mapping for named arguments. Reserve the
exact T local at its argument position. Preserve its actual backing,
alignment, generation and authority while later arguments execute.
The address passed to native code is the actual cell address, not its
incoming bits, a pointer-to-pointee projection or a copy-back temporary.
The cell address is usable only during this invocation.

The caller and all aliases, including reentrant callbacks, cannot read,
state-test, initialize, move, retire, reserve again or otherwise reuse that
cell while reserved. Known overlap or backing invalidation rejects;
residual foreign alias/access promises remain unsafe obligations.
Rooting or pinning is not acquired implicitly.

| Outcome | Cell state and obligations |
|---|---|
| Failure while staging, before Invoke | Cancel only unentered reservations; their cells remain vacant. Preserve earlier argument effects and still-owned acquisitions. No foreign resource was acquired by an invocation that never occurred. |
| Normal result satisfies When | Establish one complete valid T and its required dependencies at the exact normal Publish point, after required invocation checks/phase cleanup. Do not construct, zero, normalize or validate arbitrary foreign bits on the provider's behalf. |
| Normal complement result | Publish no T; the cell is vacant even after partial or complete writes. Do not read, adopt or release those bytes through NativeOutput. The provider must settle any unpublished acquisitions for this output before returning this outcome. Independent external side effects are not rolled back. |
| Normal status not yet discriminated | Keep possible live state and dependencies. An ignored result grants no unguarded read; pointer/integer Copy does not erase the relation. |
| Admitted failure after Invoke begins, including phase work before Publish | Retain the governing exceptional indeterminate state. No normal result, success/complement relation, vacancy, usable raw ticket or completed release is fabricated. Recovery/reuse needs independent state evidence. |
| DoesNotReturn invocation | No normal publication; retain the full declaration and terminal-call rules. This neither cancels an entered reservation nor proves absence of failure. |

The saved result, invocation identity and destination generation own the
correlation. Apply the existing discriminator-copy, join and invalidation
rules; a stale result cannot authorize a read after replacement, retirement,
storage reuse or relevant aliasing mutation. Multiple outputs are independent:
success for one is not success for all, and the same destination cannot
supply overlapping output reservations.

Extend the source-dependency proposal's destination applicability to the
published **inner T**, including pointer/scalar T. DependsOn/ForwardsBacking
still name only admitted staged inputs or the exact receiver. Preserve
actual storage/backing, owner/guard, root/pin, code/provider and affinity
obligations through copying and adapters. Neither annotation invents a
checked pointee, extent or backing from an integer/raw pointer source.

Keep the explicit empty receiver/formal source default, without applying a
single-input borrowed-result default or erasing independent required
obligations. A nonempty effective source set replaces only that default.
ForwardsBacking refines already selected sources; it cannot create them.
No raw NativeOutput formal becomes a source: self/inter-output/result
projection of its future T and borrowing its reserved destination address
remain rejected, even if both outputs would succeed.

For example, an independently admitted raw input with established backing
can support a pointer-valued output using `[param(address):
DependsOn(nameof(source))]` and the corresponding ForwardsBacking refinement.
The published descriptor retains that input's actual backing, not the
source-pointer variable's slot or a callee parameter copy. Ending all copies
of the descriptor does not release a native resource unless an independently
established owner contract says so.

## Explicit resource and typed-interface boundary

Distinguish three contracts; NativeOutput supplies only the first:

| Contract | Required evidence |
|---|---|
| Complete cell value | Exact T, native representation, result condition, publication and dependencies under this proposal. |
| Foreign resource right/responsibility | The authored unsafe provider/wrapper contract identifies what was acquired, whether it is borrowed or a transferred reference, actual resource/provider origin, validity/retention endpoint, permitted operations and exact release behavior. Copying raw bits acquires no additional right or ticket. |
| Checked Terrum owner or borrowed interface | An explicit authored operation establishes its complete type, validity, dependencies, unique responsible holder where applicable, selected cleanup/origin and failure contract. The ordinary owner/dependency representations must support those facts. |

### Minimal resource-producing provider scope

A raw acquisition API may use this cell contract, but its external resource
promise is an **additional caller-unsafe assertion**, not serialized checked
ownership on T. The minimum supported owned-wrapper scenario requires:

* On the selected normal success, the provider establishes exactly the
  documented resource/reference responsibility, in addition to valid T.
  The raw caller is responsible from the provider's actual successful
  normal return, **not only after a later constructor happens to succeed**.
  Multiple acquired resources remain separately responsible until explicitly
  transferred or settled.
* On every complementary normal return, the provider leaves no acquisition
  for this output that the caller must inspect or release. A provider that
  returns a release-worthy diagnostic/error handle on failure cannot use
  this success-only annotation for that channel. An independently admitted
  adapter must settle or publish it under its own complete contract.
* On an admitted escaping foreign exception, no caller acquisition is
  transferred through this output; the provider settles its unpublished
  acquisitions. The cell itself still has indeterminate exceptional state.
  APIs transferring responsibility on exceptional/partial exits are outside
  this bounded resource-wrapper scenario.
* There must be no escaping failure between successful machine return and
  normal cell Publish that could strand an acquisition hidden in
  unpublished bytes. **For the initial resource-wrapper slice, admit only
  crossings without fallible postconditions, consumed-owner phase cleanup
  or other fallible work in that interval.** Selector classification and
  publication themselves are non-failing bookkeeping. Do not move Publish
  before required checks to bypass this gate.

The last restriction is a publication-path requirement, not a general
NoThrow promise on the native call, release function or wrapper. Argument
checks/import resolution may fail before Invoke; the admitted foreign call
may fail as above. A wider resource-aware contained adapter could settle an
acquisition before exceptional propagation, but this proposal neither
defines that protocol nor infers it from a helper body. Without the stated
provider and publication guarantees, raw unsafe use has no new
failure-safe ownership guarantee and must not be exposed as a checked
owning acquisition.

### Authored adoption, retention and release

The wrapper must cover every observable interval from raw acquisition to
typed owner publication. Establish its release context, provider/code
dependencies and exceptional cleanup structure before any operation that
can acquire/strand the resource. After successful raw publication, use
explicit `try`/`finally` or equivalent already-admitted ownership control
flow while validation/adoption can fail. A failed adoption leaves the raw
responsibility with the wrapper unless that operation's exact contract
already consumed or settled it; a successful adoption transfers it once
to the unique owner. Do not give both paths permission to release.

No method called Adopt, owner constructor, cast or pointer equality proves
this handoff. Such operations are ordinary authored APIs, with their own
caller-unsafety and checked body/publication obligations. This proposal
selects no universal adoption/release signature. A wrapper that can fail
after acquiring several resources must settle the still-held acquisitions
in its actual required order, preserving dependencies between them.

Retirement follows the existing complete-owner Cleanup and explicit
exceptional-finally rules. A raw Copy cell has no synthesized Cleanup;
dropping or copying it is not a compiler-enforced native-resource leak or
double-release check. NativeOutput must not be advertised as such a check.
Do not assume generated Cleanup runs during exceptional unwind.
Release must use the exact compatible provider, allocator, interface and
required context. If release can fail, preserve its actual remaining
responsibility and observable failure; do not silently mark it settled,
blindly retry a possibly reused numeric handle, or invent a fail-fast,
swallowing or retry policy. The bounded qualification scenario uses a
provider whose release completely consumes its obligation on normal return.

A borrowed pointer/handle instead requires its actual backing/lease/guard
and retention endpoint; it must not be adopted as a transferred reference
or released by guessing. NativeOutput's no-retention guarantee concerns the
**output cell address**, not permission for the provider to retain arbitrary
inputs or a lifetime extension for the returned resource.

For COM or another foreign interface, non-null pointer bits do not identify
an interface, establish its vtable/slot contract or acquire a reference.
Typed acquisition must fix the requested interface identity in authored
library code or validate the actual identity under an independently
admitted protocol before publishing the typed owner. A caller-selected IID
plus `void**`, equal slot, common release prefix or cast is insufficient.
Code pointers likewise require an exact callable signature/convention and
live code/provider obligation; an address does not establish them.

## ABI, persistence, binding and consumer checks

The ABI formal is still one native pointer, independent of the size of T.
For Windows x64, a uint32 output cell has its admitted four-byte extent and
alignment; a raw-pointer output cell has eight-byte extent/alignment.
The eight-byte argument carrier does not authorize an eight-byte write to
the uint32 cell. Keep ordinary physical argument positions, receiver and
hidden-result rules. There is no implicit marshalling, managed rooting,
pinning, temporary allocation, owner conversion or ABI based on a handle
name. All crossings and unsafe adoption/release operations retain their
actual caller-unsafety; an unsafe declaration does not authorize its body.

### Exact candidate representation delta

Reuse the prerequisite proposals' **complete** representations, with the
following eligibility/registration amendment; do not introduce an alternate
pointer-output codec.

| Surface | Recommendation |
|---|---|
| Standard shared contracts | Keep OutputState `0x0002` and Dependency `0x0004`, their 20-byte rows, direct explicit-formal destinations, success/complement states and normal-publication phase. Extend their raw-destination applicability from aggregates to eligible T. Complete pointer/scalar type identity remains in the formal's TypeSpec, not an untyped bits field. |
| Result selectors | Keep proposed `0x000B` phase 3 and Feature `10/1` closed predicate registration. Normalize NonZero to `3/4` and NonNegative to `5/6` with the existing exact result-type rules. Source enum numbers above are not these node kinds. |
| Native feature | Amend the unapproved Feature `8/1` candidate's name/scope from `NativeAggregateOutputs` to **NativeOutputCells**, retaining governing edition 1 and prerequisites `[(1,1),(2,1),(10,1)]`. Do not allocate a second scalar/pointer Feature, retain the old name as an alias or consume Feature 14, which has a separate root/pin candidate. |
| Body staging | Keep proposed long primary `DF`, OutputReservation record `0C`, CallSite Home source-use `6`, exact T Home and reserve/cancel/Invoke/Publish rules. A pointer-valued T Home stores the **inner pointer value**, not the caller cell address. No new opcode, pseudo-type, record field, prefix or source-use value. |
| Private structured persistence | Keep the combined candidate revision-92 field layout: one declaration-owned output collection, formation `1`, phase `1`, explicit dependencies and reservation expression tag `41`. Admit existing scalar, native-pointer and function-pointer type identities in `pointeeType`, with full validation/remapping. Do not encode them as `100 + structIndex` or a newly invented handle identity. |
| Composition and native containers | Select the **amended exact revision-92 draft** when using that codec; the separate provenance revision-93 candidate composes the same amended output collection when its additional carrier is selected. This eligibility extension needs no new revision/field. Keep the provider-binding candidate's outer version `3` and foreign-expectation table unchanged; native images still need regeneration and validation for changed inputs. |
| Ownership | No new ownership carrier/ID is assigned. Transfer clause `0x0003` remains inactive; no flag, legacy tuple or Attr row becomes resource authorization. A checked wrapper uses independently admitted ordinary owner/dependency/cleanup contracts, not an unrepresented raw acquisition promise. |

Revision numbers and version bytes alone do not establish agreement on the
amended draft. Regenerate inputs where exact agreement is absent; do not
header-patch, retry an older layout or revive the scalar/pointer prototype
tuples. Existing aggregate records have unchanged meanings under this
amendment, not a guessed compatibility interpretation. An older validator
must reject a required unsupported leaf output, not ignore its collection.
Private persistence does not establish standard TRRM support.

Retain the selected semantic provider/export and exact direct-import
library/symbol/resolution/convention, or exact interface/method/receiver/
dispatch contract. Preserve the
[provider-bound direct-import routing](native-output-direct-import-provider-binding.md);
do not bypass the chosen provider because the underlying foreign DLL is
equal. An ABI-transparent thunk creates no second reservation or publication.

Binding compares the original complete expectation: types/access,
formation, extent/alignment, selectors/states, sources/refinements, phase,
no-input-read/no-output-address-retention, caller-unsafety and ABI.
Changing NonZero to NonNegative, pointer nullability, integer signedness,
resource/provider origin or a retained cleanup contract is not automatically
strengthening. Preserve normal semantic directed satisfaction and the
private output candidate's stricter exact normalized comparison.
Reordering tables/formal names requires typed correspondence/remapping, not
equal private numeric type IDs. Copying an output **whose value is a
function pointer** does not enable this output protocol on indirect
callable-value targets or erase their unsupported contract surface.

Raw resource assertions are not checked ownership guarantees in the output
collection. Do not export a stronger safe acquisition claim solely from it.
Consumers of an authored checked wrapper retain that wrapper's actual
owner/type, cleanup, dependency and code/provider contracts. Foreign provider
truth, including its resource promises, remains the unsafe declarer's
responsibility; successful loading is not proof.

Producers check authored caller states, native role, resolved source schema,
complete T eligibility, correlated flow, dependencies and supported
publication paths. Preserve facts through shared traversal/rewriting,
verification, adapters, persistence, linking and lowering. Consumers check
framing, bounds, owner/role, complete type/reference resolution, explicit
formal ordinals, condition/result compatibility, uniqueness, dependency
sets/phases, feature closure and retained binding before affected use,
including declaration-only requirements. They need not semantically re-prove
foreign behavior or producer-published body facts. Unsupported optional,
resource, backing, owner or code representations reject instead of becoming
empty dependencies or a success-shaped default.

## Exact integration owners

The following are proposed **future amendments**, not edits made here.
Apply them together with the prerequisite candidates; keep their accepted
versus proposed status explicit.

| Normative owner | Proposed amendment or preserved boundary |
|---|---|
| [Grammar §3.2 and §5.1](../specs/grammar-spec.md#32-types-parameters-and-arguments) | Carry the prerequisite `&init` argument production/semantics through all eligible T; keep ordinary pointer/ref formation distinct. No additional production. |
| [Native roles and publication](../specs/native.md#native-declaration-roles), [references](../specs/language/references.md#descriptors-referents-and-bindings), [initialization/flow](../specs/language/initialization-and-flow.md) | Register the exact source schema/enum, native role and cell protocol; distinguish pointer-cell publication from pointee state, result tests, resource acquisition and exceptional indeterminacy. |
| [Dependencies — source schemas and targets](../specs/language/dependencies-and-views.md#source-schemas-and-targets) | Widen the prerequisite inner-output destination to eligible T, preserving its exact flat sources, explicit empty set, refinement and no raw-output-source rule. |
| [Ownership and cleanup](../specs/language/ownership-and-cleanup.md), [lifetimes](../specs/lifetimes.md), [adopted library inventory](../specs/libraries/adopted-inventory.md) | Record the raw-cell/explicit-wrapper boundary and source registration. Preserve single responsibility, Copy restrictions, complete-owner publication and explicit exceptional cleanup; add no universal owner API or release runtime. |
| [Windows x64 call interface §§1, 5, 6](../specs/targets/windows-x64-call-interface.md), [data layout §1](../specs/targets/windows-x64-data-layout.md#1-scalar-storage-and-validity) | Apply the cell protocol to already admitted scalar/address representations. Preserve exact foreign primitive/nominal admission and owner ABI; assign no optional/managed layout. |
| [Contract clauses §§2, 5, 6](../specs/representation/contract-clauses.md), [logical dependencies](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending), [registry](../specs/representation/registry.md) and [machine registry](../specs/representation/trrm-1.registry.json) | Adopt the shared raw-cell applicability and candidate NativeOutputCells Feature name/domain; preserve exact rows and bounded selectors. Do not activate Transfer or a native-only output table. |
| [Body records](../specs/il-instruction-set.md#typed-operand-records), [requirements/admission](../specs/representation/requirements-and-admission.md), [semantic manifests](../specs/representation/semantic-manifests.md) | Complete the prerequisite reservation/selector admission for scalar/pointer T and required capability closure, without a second codec. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions), [reader trust](../specs/representation/reader-trust.md), [native-boundary conformance](../specs/conformance/native-boundaries.md#4-pointer-dependency-and-lifetime-boundaries) | Include raw-cell exact expectations and tests. Amend NBP-04's rejection only for the selected shared schema; preserve rejection of legacy clauses and unsupported required contracts. |

Private revisions/container mappings remain proposal/implementation owners,
not additional normative persistence formats. The accepted
[persistence taxonomy](../specs/representation/persistence.md) and
[exact-draft agreement](../specs/representation/format-versioning.md)
continue to apply.

## Required acceptance cases

These are **future design/implementation tests**, not tests run for this
documentation change. Use public compiler APIs and unrelated synthetic
headless providers; select the final compiler/configuration/input identity.
Distinguish source/metadata rejection, provider-truth violations and observed
native execution.

| Case | Required result |
|---|---|
| `cellprobe.dll/TryReadCookie`: uint32 counter and opaque raw-pointer cookie outputs on one call | Both reserve exact independent cells and publish only under their own declared condition. No handle/type-name policy. |
| Every independently admitted scalar/address leaf, enum/Transparent carrier and existing aggregate | Apply the same eligibility predicate; preserve exact nominal/access/type identity and representation. Unsupported primitive, zero extent, unique owner, managed/view/task or unassigned optional layout rejects. |
| `NonZero`: signed minimum, -1, 0, 1, signed maximum; unsigned 0, 1, maximum | Only zero is complement. For NonNegative, negative values including signed minimum are complement and zero succeeds; uint32 NonNegative rejects. |
| Non-null pointer T, null on claimed success; invalid Boolean/enum/wrapper bits | Valid output type remains a provider obligation. Test shim/harness detects the lying-provider case; do not promise a runtime validity check or source diagnosis of arbitrary foreign code. |
| Wrong registered identity/type, unknown enum case, old ordinal spelling, repeated inline/named occurrence or wrong target/native role | Diagnose; same spelling, same bits, bodylessness or slot number supplies no authority. |
| Vacant local versus live/null-preseeded, partial, maybe-live, already-reserved, field/array/property or ordinary `&local` argument | Admit only the prerequisite whole vacant-local `&init` destination. No implicit overwrite, rollback or checked-reference conversion. |
| Reordered named arguments, later nested call, staging failure and reentrant alias access | Evaluate once in source order; preserve earlier reservations. Cancel only before Invoke; reject conflicting access/backing invalidation. |
| No, partial and complete writes on complementary return | No live cell, read, adoption or guessed release. A provider leaving a caller-owned resource here violates this annotation and cannot be used as the bounded acquisition provider. |
| Saved/copied discriminator, ignored result, joins, loops and destination reuse | Preserve exact invocation/generation correlation and conservative maybe-live dependencies; no stale-result authorization. |
| `sliceprobe.dll/TryFindByte`: nonempty pointer-output DependsOn/ForwardsBacking, copied descriptors, ended descriptor slot versus ended backing/guard | Retain actual backing through all surviving copies. No inferred pointee validity, extent, source-slot borrow or pin acquisition. |
| Empty sources, missing required backing, self/inter-output source and output-address escape | Preserve independent required obligations; unsupported/missing facts reject. No raw-output source projection or retention by accident. |
| Borrowed ticket versus transferred native reference | No automatic release/adoption for either. Checked wrapper must establish the actual acquisition/borrow contract; duplicate raw bits are not another reference. |
| `ticketprobe.dll/TryAcquireTicket`, with a uint64 output and separately authored `ReleaseTicket(uint64)` contract, plus fallible wrapper validation/construction | One release for every still-held acquisition on each failed wrapper path; exactly one unique owner on publication and one later retirement release. Include several outputs and a dependency-constrained cleanup order. These fixture names select no compiler behavior. |
| Foreign exception after partial writes; fallible post-return phase before Publish | No normal output facts. The bounded owning-wrapper scenario requires the provider's no-exceptional-transfer promise and the stated non-failing publication interval; do not expose checked ownership when either is absent. |
| Release failure or reused numeric handle | Preserve actual unsettled state and reported failure; no unconditional settled flag, blind retry or new runtime failure policy. |
| Caller-chosen interface identity plus void pointer, wrong fixed identity, or `catalogprobe.dll/FindOperation` returning an exact native fnptr followed by contract erasure/provider unload | No typed interface/owner/call privilege from bits. Reject unsupported checked acquisition/code lifetime; unsafe lying assertions remain distinguishable from compiler-checked facts. |
| Private/standard round trip and replacement with reordered scalar/pointer/fnptr/type/owner tables | Preserve exact output T, selectors, dependencies, reservation home and provider/cleanup origin. Reject changed roles, states, type/access, sources or unsupported required capabilities before use. |
| Malformed metadata, uncalled declaration, old header substitution or nested-container disagreement | Reject bad bounds/references/ordinals/ownership, duplicate/missing state pairs and unknown codes; never clear output facts or retry a guessed schema. |
| Selected direct-import provider, alternate export/thunk, graph/set and process-start JIT paths | Preserve the original provider expectation and actual call route; qualify stored provider images separately from re-lowered consumers. Publication occurs once at the caller, not at each thunk. |

## Implementation boundary and remaining decisions

The inspected binder and
[`NativeOutputContractValidator`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputContractValidator.cs)
currently require the aggregate eligibility helper in
[`DirectNativeImportContract`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs).
The declaration-owned collection/reservation and
[`NativeOutputModulePersistenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs)
provide relevant in-progress infrastructure; their aggregate cases do not
qualify scalar/pointer outputs, resource adoption or standard exchange.
No passing compilation, ABI probe or execution is claimed by this document.

There is **no remaining design choice inside the recommended cell
amendment**: use the single widened contract, source registration and shared
carriers above, with explicit ownership outside that raw cell contract.
Approval, complete independent backing/owner/code representations,
implementation and the acceptance cases remain gates. First-class checked
raw resource tickets, failure-live/exceptional resource transfers, optional
descriptor layouts and raw-output-as-source projections are explicitly
excluded; this document neither approves them nor permits inventing them
while implementing this direction.
