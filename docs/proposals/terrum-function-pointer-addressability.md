# Terrum function-pointer addressability

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This proposal changes no normative owner, source
or metadata contract, registry, compiler or stability promise. Every syntax,
operation name, assignment, encoding and Feature-scope recommendation below
is a candidate, not an approval. The authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Decision and genuine gap

Recommend **A: a default-Terrum raw `fnptr` may address an eligible ordinary
Terrum static function**, with its complete invocation contract, under an
explicitly admitted same-runtime call ABI and code-lifetime contract.
Address-taking requires no NativeCallable, NativeImport, NativeExport or new
authored entry marker. It creates neither a closure nor a foreign callback.
This selects a formation rule, not a universal or frozen Terrum machine ABI.

The [specification index](../specs/README.md) leads to these owners:

| Verified owner | Established contract and remaining gap |
|---|---|
| [Callable families](../specs/type-families.md#callable-values-and-tuples), [lifetimes](../specs/lifetimes.md) | `fnptr<Args..., R>` defaults to Terrum, has no independent environment, and requires live code. Foreign conventions are distinct identities selected by NativeCallConv. Neither rule specifies which ordinary declarations supply addresses. |
| [Native declaration roles](../specs/native.md#native-declaration-roles) | NativeCallable stabilizes a selected **native** signature/convention; callable NativeImport is addressable without that marker; NativeExport publishes an OS symbol. These are not ordinary Terrum-entry roles. Native calls remain caller-unsafe. |
| [ABI layers](../specs/data-layout-and-abi.md#contract-layers), [Windows nonforeign defaults](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults) | Nonforeign scalar defaults explicitly do not standardize a Terrum-to-Terrum ABI. Joint regeneration permits private agreement, not inference from an OS, register sequence or equal carrier sizes. |
| [Standard TypeSpec](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | `0x0D` describes a complete pointer contract through a bodyless, noncallable MethodDef anchor; its accepted convention domain is foreign `1..4`. The anchor is never an address target. |
| [Calls](../specs/il-instruction-set.md#calls), [native-address candidate](standard-native-address-formation.md) | `call.method`/`call.value` invoke. The unapproved `E9`/Body-record `0D` candidate forms addresses of actual NativeCallable/callable NativeImport targets, expressly excluding ordinary methods and adapters. Neither fills this gap. |
| [Native source-schema candidate](native-calling-convention-source-schema.md) | Omitted CallConv means Unmanaged; the proposed enum domain is `1..4`. Adding that argument does not provide a default-Terrum target. |

The separate unapproved [default-convention proposal](standard-function-pointer-default-convention.md)
provides a TypeSpec-only `0=Terrum` codec candidate, not an entry. The
[source-contract](standard-function-pointer-source-contracts.md) and
[anchor-contract](standard-function-pointer-anchor-contracts.md) proposals
provide complete contracts, defaults, projection and rebasing, not ordinary
addressability. This proposal composes with them; it does not approve them.

### Repository evidence is narrower

In the current [signature model and matcher](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs),
`NativeCallingConvention.Native=0` is recognized, but `MatchesFunction`
requires the declaration's `NativeFunctionPointerType`, effective caller
unsafety and the bounded signature restrictions. [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)
populates the native-callable path, parses the legacy `fnptr<NativeAbi, ...>`
form for unattributed pointers, and binds unary `&function` only with an
expected pointer type and a target possessing that native pointer contract.
An ordinary static declaration does not become eligible through enum zero.

The in-memory signature now carries declared Effects and whole-source
Dependencies. OutputState is structurally modeled but rejected for the
current scalar/raw-pointer signature subset, which lacks checked
initialization-reference parameters. Those are implementation limits, not
the definition of Terrum signatures.

[NativeEmitter](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
uses actual function fixups for addresses and bounded Windows x64 staging
for indirect calls. [NativeFunctionPointerTests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs)
exercise the native-callable/legacy path; they are not excluded by the
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj),
but native execution cases have platform gates. Their presence is not a
test result or evidence for ordinary default-Terrum addresses. The
[implementation inventory](../implementation.md) likewise distinguishes
that prototype from accepted source and Standard interchange.

## Alternatives

| Candidate | Assessment |
|---|---|
| **A. Ordinary eligible static functions supply Terrum entries — recommended** | Gives the accepted default type an ordinary producer without foreign roles. Reuses complete callable contracts and the candidate address carrier. Requires a real same-runtime ABI agreement; taking an implementation address is insufficient. |
| **B. Default Terrum is logical only until an authored entry/adapter role is declared** | A concrete viable variant introduces a new **TerrumEntry role** on an authored static wrapper: its complete Terrum signature is an explicit pointer-entry commitment, and address formation accepts only such wrappers. The role would need source/Core identity, declaration applicability, persistence, binding and the same complete ABI/lifetime rules as A. A wrapper with only NativeCallable instead supplies a foreign entry, not this role. This can make publication intentional, but adds a role and wrapper ceremony without removing the ABI problem. |
| **C. Remove/reject the default or redefine it as foreign behavior** | Requiring NativeCallConv everywhere, or changing omitted convention to Unmanaged, is a substantive replacement of accepted source identity and the companion direction, not implementation of it. Pre-v1 replacement is allowed on its merits, but the user has not selected it. It would require explicit source/type/interchange and role/default amendments; matching Windows carriers do not justify an alias. |

Under B, a new admitted Terrum-entry wrapper could actually produce the
default type and an ordinary `call.value` could consume it. Without that
new role or an equally complete adapter, merely accepting pointer-shaped
parameters with no valid producer does not close the gap. Rejecting every
default-Terrum formation/use indefinitely would be C in substance, not a
bounded implementation of A or a complete B.

## Candidate source and declaration rule

### Eligibility

An eligible target is one selected, accessible, concrete **ordinary named
function** with a complete checked implementation and a receiver-free,
closed invocation signature. Addressability is a use-specific obligation;
it does not freeze all direct calls to that function.

| Declaration or signature | Candidate disposition |
|---|---|
| Ordinary top-level function or ordinary static method | Eligible, including a completed partial declaration. Visibility and ordinary name binding still apply; OS export is unnecessary. A semantic Import may resolve to such a provider. |
| Instance method, bound receiver, interface/witness dispatch requirement | Not directly eligible in this first rule. No `&object.Method` capture, open-instance receiver marker or hidden dispatch is invented. An authored ordinary static wrapper may take the receiver as an explicit ordinary parameter with its exact access/loan contract. |
| Method-generic declaration or generic declaring-owner context | No new instantiation recipe is added. The candidate carrier requires method-generic range `(0,0)` and no remaining owner formals. A closed implementation is eligible only if it already has an admitted exact closed MethodDef/Import representation; a bare method token cannot imply specialization. Otherwise use a nongeneric static wrapper calling the desired closed specialization. |
| Closed constructed types in parameters/results, including nested pointers | Permitted under their own complete type, formation and representation contracts. They do not make the callable itself universally generic or provide a dictionary. |
| Checked `ref<T>`, `write ref<T>`, `ref<init T>` or admitted initialization-range parameters/results | Not rejected merely for being nonforeign. They require their actual admissible positions, exact descriptors, backing, access, initialization/output, ownership and runtime carriers. The uncompleted initialization capability's existing non-escape rule still applies. No checked-reference-to-raw-pointer conversion is added. |
| NativeCallable, callable NativeImport or method NativeExport | Their existing **foreign** address route remains separate. This candidate does not expose a second Terrum entry on the same role-bearing declaration or retag its native entry. An ordinary authored wrapper can invoke it under the native contract. Data imports are not functions. |
| Anchor, abstract/unbound bodyless declaration, constructor, accessor, operator, Cleanup, initialization-only helper or compiler intrinsic | Not an ordinary address target under this rule. An existing special invocation role does not grant ordinary entry authority. No manufactured body, vtable slot or initialization authority is allowed. |
| Ordinary async/generated-producer function | Only its complete callable entry can qualify, with its operation/cursor result and all phase/ownership requirements admitted. Not its lowered resume/close implementation details. This proposal activates no missing task, producer or deferred-output carrier. |

Local binding `mut`, source labels/defaults and implicit-reference adaptation
do not change complete signature identity. Pointer invocation receives the
explicit descriptor/value inputs of that signature; address-taking neither
omits defaulted parameters nor synthesizes call-site argument adaptation,
variadic behavior or arbitrary signature conversion.

### Syntax and expected-type inference

Recommend extending the **meaning of existing prefix `&`**, not adding a
keyword or attribute: `&F` or `&Type.Method` may select an eligible ordinary
static function. The [expression grammar](../specs/grammar-spec.md#34-expressions-and-precedence)
already parses these forms, but its
[storage-address rule](../specs/grammar-spec.md#513-operators-assignments-and-expression-gaps)
does not establish this function-designator meaning. `&place` remains the
distinct storage operation. Ordinary lexical lookup, including shadowing,
happens first; failed method matching does not retry lookup as another role.

* With an expected pointer type, select the declaration by its complete
  ordered signature and convention, then check the companion proposal's
  directed contract projection. Bare `fnptr` always has its fixed defaults;
  the initializer cannot silently strengthen or redefine it.
* Without an expected type, one unambiguous eligible, fully selected target
  yields its exact normalized pointer contract, with Terrum convention.
  An unresolved overload group needs disambiguating context. Contracts alone
  do not rank overloads, and no free generic argument is inferred from an
  address, result type, body or ABI.
* An expected foreign convention does not turn an ordinary target into
  NativeCallable. Foreign formation still uses its separate native-entry
  rules. No conversion to `func`, convention cast or compatibility alias
  follows from this rule.

**Proposal-only binding example**, not current executable evidence and not a
selection of a target ABI:

```terrum
int32 Identity(int32 value) { return value; }

int32 Use()
{
    unsafe
    {
        fnptr<int32, int32> typed = &Identity;
        var inferred = &Identity;
        return typed(inferred(42));
    }
}
```

Both pointers have Terrum identity. No `NativeAbi` prefix, NativeCallConv,
NativeCallable or NativeExport is implied.

## Complete invocation contract

Use the source/anchor companions as the single owners of normalization,
applicable clauses and checked projection; extend their formation rules to
this ordinary route, rather than inventing a signature-only exception.

* **Effects and authorization:** retain declared Effects, including
  DoesNotReturn and authored caller-unsafe `04`, exactly. Recommend explicit
  lexical unsafe authorization for both raw Terrum address formation and
  raw invocation, independently of those declared bits, as for the companion
  raw-pointer direction. Do not insert `04` into a safe target's contract or
  make its ordinary direct calls unsafe merely because its address is taken.
  An unsafe declaration does not authorize its body's operations. This is
  not a safe `func` or an indirect Pure/ConstEval authorization.
* **Outputs:** preserve complete types, formal ordinals, exact normal-result
  selectors, vacant/complete states and publication phases. Bare-type
  defaults and inferred nondefault facts remain distinct. A raw pointer
  parameter is not a checked output; `ref<init ptr<T>>` initializes a pointer
  cell, not a pointee. No exceptional result or initialized output is
  manufactured when the ordinary call has no normal Publish.
* **Dependencies and ownership:** preserve whole-source relationships,
  explicit empty sets, backing-only forwarding versus borrowing source
  storage, retained destinations, phases, roots, invalidation and cleanup.
  Invocation establishes the actual argument/result relationships; forming
  a code pointer captures no future argument. Code origin is a value
  dependency separate from signature-contract identity.
* **Projection:** retain exact family, convention, complete signature/modes
  and every caller/guarantee obligation. Use only the companion's admitted
  directed relations; unsupported implication is not success. Storage cells
  remain invariant even when a value view may weaken a guarantee.
* **No-return:** address formation has normal continuation even when the
  target never returns. Only invocation through a view retaining
  DoesNotReturn removes the normal edge, for any declared result type.
  This does not erase argument, output, dependency or cleanup obligations.

Required invocation facts outside the selected anchor applicability or an
incomplete codec remain prerequisites. Neither bare defaults nor an unsafe
scope authorizes dropping them. The current Effects/Dependency prototype
and unsupported OutputState subset do not redefine this contract.

## Same-runtime ABI and execution

“Same-runtime” means an **admitted call-ABI domain**, not merely the same OS,
processor, compiler name, Module or equal source types. Every possible entry
supplied to a pointer call must agree with that call's selected representations,
runtime instance/context, helpers and failure protocol. This agreement is
an execution/binding obligation, not a new pointer type argument or an
unvalidated per-method assertion.

The selected target/runtime contract must explicitly determine:

1. Code-pointer representation/alignment and callable-entry rules; complete
   scalar, aggregate, reference, owner/root and result representations.
2. Argument/result classification, unit logical positions, supporting
   copies, result buffers, register/stack placement, alignment, preservation,
   resource checks and evaluation/publication ordering.
3. Root/relocation, loan and cleanup protocols, helper identities, runtime
   entry/thread requirements, stack walking and complete failure/unwind
   transport, including exceptional ownership and partial initialization.
4. Which ABI-generated hidden inputs exist and how an indirect caller
   derives them from the complete signature and admitted domain.

A hidden result buffer or mechanically derived runtime bookkeeping is
possible only under that explicit contract. There is **no implicit receiver,
captured environment, method dictionary, witness list or user context** in
this candidate. A per-target requirement not derivable at the indirect call
must be supplied by an explicit ordinary parameter or discharged by a
conforming receiver-free entry; it cannot be smuggled beside the address.
A backend entry stub may implement the selected entry contract, but cannot
change convention, erase contracts or become an implicit cross-domain adapter.
Foreign primitive/Blittable admission is not the definition of a Terrum
signature. No new logical arity or aggregate-size cap follows from this rule;
compact register windows are not the callable-arity limit.

Private direct-call specialization may continue. Once an address escapes
into an indirect call, its entry must obey the agreed indirect-call ABI even
if the private body uses another internal arrangement. Joint generation can
choose that agreement; it must choose and validate it, not assume all
ordinary body entry points already obey it.

The [Windows x64 tables](../specs/targets/windows-x64-call-interface.md)
and current emitter are not that missing agreement. This proposal assigns
no Windows register mapping, hidden failure bit, generic dictionary ABI or
universal Terrum stack convention. A first backend implementation needs an
explicit complete target/runtime contract for its admitted subset before
claiming formation plus execution support.

### Failure, initialization and reentrancy

Terrum invocation keeps [local failure](../specs/language/local-failures.md)
and [call-phase](../specs/il-instruction-set.md#invocation-phases) semantics:
capture the pointer once, stage written arguments once in source order,
perform the required checks/preparation, invoke, then publish only on normal
completion. Staged owners, transfers, partial outputs and exceptional cleanup
behave as for an ordinary Terrum call; there is no rollback or success-shaped
default. Fail-fast and explicitly admitted recovery retain their ordinary
rules. No NativeCallable-style boundary containment or prototype no-failure
restriction is imposed merely because this is an indirect call.

Formation does not invoke the target or its preconditions. For the
[static-initialization trigger](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage),
recommend classifying runtime `&Type.Method` formation as runtime
static-member access: the runtime-member trigger completes the group before
publishing the pointer; the grouped-field-only trigger is not activated by
address-taking alone. Compile-time binding is not a trigger. Initialization
effects/failures occur at that source expression, with the ordinary cycle,
failed-unit, publication and cleanup rules; they cannot be hidden behind
“address formation is pure.” Later invocation retains the ordinary static
access obligations. No initialization-only helper can leak group authority
through this address rule.

Code resolution/publication must succeed before a pointer is exposed.
AOT/JIT preparation may occur earlier only without changing source-visible
effects, initialization timing or failure ordering. This proposal creates no
lazy compilation/resolver failure protocol, implicit source allocation or
first-call fallback; a backend needing one must admit its complete existing
or separately selected contract.

Recursion and synchronous reentry use independent call frames and supporting
storage; no single static scratch result/output slot is permitted. Mutable
static state keeps its normal access and initialization rules. A pointer
grants neither synchronization nor thread-transfer permission, runtime-thread
attachment, cross-runtime entry or native-callback capability. Foreign entry
needs the existing native role/contained adapter contract, not tag-zero
retagging.

## Code lifetime, relocation and boundary scope

Apply the [native lifetime](../specs/native.md#runtime-and-code-lifetime)
and [JIT replacement](../specs/jit.md#replacement-and-code-lifetime) owners:

* Formation yields the real published callable entry, never a signature
  anchor, metadata row, data/IAT cell or unresolved placeholder. Persisted
  semantics describe the selected declaration and operation, not a process
  address encoded as a scalar constant.
* Relocate/fix up code before publication under target write/execute, cache
  and unwind-registration rules. The exposed entry remains valid and at a
  usable address through every permitted use; arbitrary escaped pointer
  copies cannot be repaired by moving code or rebinding an Import.
* Preserve the actual definition/load origin, entry stubs, helpers, runtime
  and unwind dependencies through copying, storage, forwarding, joins,
  results and optimization. Equal anchors do not merge two targets or their
  loaded instances. A forwarding Module is not automatically the code owner.
* A raw pointer acquires no owner, GC root, per-pointer retain or unload
  lease. The holder must satisfy code/runtime/load lifetime through each
  invocation and any later obligation that can use the entry. Unload,
  replacement or failed unwind deregistration cannot free still-needed code.
  Updating a provider does not retarget old pointer values; no hot-unload or
  live-patching protocol is selected.

| Boundary | Required agreement |
|---|---|
| Same Module or multiple Modules lowered/regenerated together | The selected runtime/target defines one complete indirect-call ABI for their actual calls. Same-Module membership alone is not proof; direct-call optimization cannot change an escaped entry. |
| Separately supplied semantic `.trrm` providers lowered together | Preserve each Module's governing rules, expected declarations and retained requirements, then establish compatible execution assumptions during joint lowering. Separate compilation is not necessarily a precompiled ABI crossing. |
| Independently replaceable native code or an unchanged precompiled consumer | Apply the [explicit/versioned boundary rule](../specs/data-layout-and-abi.md#private-implementation-versus-independently-replaceable-boundaries). Retain and validate exact compiler/runtime/target/ABI assumptions or use an explicitly selected adapter/regeneration strategy before use. Source signatures and tag zero alone do not admit this crossing. |
| Foreign caller or different runtime domain | Not admitted by default-Terrum identity. An authored native-callable wrapper or separately designed adapter must establish the other boundary; no automatic thunk, marshalling or conversion is added. |

This proposal does not add a serialized ABI-ID field. A complete independently
replaceable native-artifact assumption carrier remains a prerequisite where
that boundary is claimed, not information to infer from a `.trrm` anchor.

## Standard formation, references and capability closure

Recommend **generalizing the unapproved address carrier**, not allocating a
second opcode: replace its candidate presentation names `native.address` /
NativeAddress with **`method.address` / MethodAddress** and extend its target
eligibility by the selected pointer convention. This supersedes only that
companion's ordinary-Terrum non-goal and the corresponding native-only
eligibility/name; its foreign branch, result placement, ordering, bounds and
validation remain. The old proposed name is not a compatibility alias.

The candidate retains its sole four-byte `E9`, zero-control instruction and
exact 12-byte kind-`0D` record: result selector/index, actual MethodMap index,
and explicit pointer TypeMap index, with all reserved fields zero. No new
flag, discriminator, width, reference namespace or allocation is proposed:

* TypeSpec-only tag `0`, under the default-convention companion, selects
  this ordinary Terrum rule. Tags `1..4` select the unchanged native-entry
  rule. Neither branch may retag the other's target.
* The MethodMap still contains an actual MethodDef token or an Import
  selecting a MethodDef expectation. A local target needs a real admitted
  implementation; an Import resolves the exact provider/key and checks
  ordinary binding **plus this addressability/use obligation**, even if no
  direct CallSite survives. Bodyless expectations are proxies, not entries.
* The TypeMap still selects Standard `0x0D` with its distinct bodyless
  signature anchor. Require zero receiver, empty method-generic range and
  the admitted closed context; this record carries no instantiation recipe.
  Rebase complete contracts to the anchor from the selected static view.
* No NativeCallable `000C` is manufactured on the ordinary target or anchor.
  NativeImport/NativeCallable/source NativeCallConv retain their closed
  foreign domains and omission rules. NativeExport still references its
  actual method's unique native-callable contract.

This is a candidate amendment to the same draft allocation, not permission
to interpret current-schema `E9`, Body kind `0D` or TypeSpec convention zero.
The [registry](../specs/representation/registry.md) and
[machine registry](../specs/representation/trrm-1.registry.json) need that
combined exact agreement. The address companion's allocation/capacity review
still applies because the byte shapes and occupied cells do not change.
Development operation `31`, private signature rows and `NativeAbi` are not
Standard alternatives or historical decoders.

Compose the default-convention candidate's **unapproved scope broadening**:
`1/1 NativeBoundaryContracts` for pointer/anchor identity and declaration
binding; `2/1 RootlessNativeValues` for the code-pointer value/places;
`4/1 NativeExportEntries` for formation and indirect invocation, retaining
closure `4/1 -> 3/1 -> 2/1 -> 1/1`. Feature names do not require a foreign
role or OS export. This does not remove Feature 3 merely because the actual
target is ordinary, nor allocate a new Feature.

These gates admit the code pointer, not every signature payload. Checked
references, roots, owners, cleanup, selectors, generics, async and failures
also require their complete independently applicable carriers and closure.
The anchor companion's bounded selector dependency, including its
[predicate-composition candidate](predicate-feature-composition.md), remains
unapproved and gated; no missing registration is activated here.
`13/1 LayoutQueries` is additional only for retained query uses. Retained
unreachable/address-only operations, discarded results and unused retained
records do not erase requirements. Each dependency Module keeps its own
manifest; reachability and a recognized tag are not admission.

## Producer, consumer, rewriting and lowering obligations

**Produce truthful facts.** Source binding and mandatory bound-program
verification establish eligibility, lexical authorization, selected complete
contracts, valid flow, backing, ownership and code lifetime. Shared traversal,
rewriting, dependency materialization and every persistence route preserve
them. Ordinary addresses must not reuse a validator that requires the target's
NativeFunctionPointerType/NativeCallable marker. This does not relax validation
of the foreign branch.

**Safely consume selected input.** Under
[reader trust](../specs/representation/reader-trust.md), check exact schema,
framing/count arithmetic, record extents/controls, result/Home roles,
independent map bounds and token kinds, actual versus expected declaration
roles, anchor validity, complete contract/reference structures, directed
bindings, capability closure and runtime/ABI compatibility before affected
use. Reject an anchor as a target, a foreign entry for tag zero, an unbound
proxy, wrong receiver/generic context or incompatible provider. An
address-only Import still needs these binding checks.

Consumers need not reconstruct lexical unsafe scopes from SourceMap,
re-prove arbitrary producer-published body facts, prove a provider's behavior,
or identify an arbitrary code address by scanning bytes. False but
structurally consumable semantic facts remain producer violations and can
lead to undefined behavior; a self-declared ABI/trust bit cannot validate
them. Optional deeper audits are separate.

**Remap typed references, not bytes.** Rebuild record/MethodMap/TypeMap/Home
indexes; actual MethodDef/Import references; TypeSpec and anchor references;
MethodSig/Param/Contract ownership/ranges, selectors and substitutions in
their defining contexts. Preserve selected Module identities, every original
expectation and actual code origins. Anchor deduplication cannot merge
targets, discard address-only providers or reinterpret owner context.
Rebuild Body map masks/counts, record/block offsets, instruction ordinals,
section extents and affected source/debug slots. Preserve required Import
edges where direct cross-Module method tokens are not permitted.

**Lower the complete entry and call.** AOT emits real target-entry
relocations; JIT publishes an admitted usable entry before exposing its
address. Apply the selected Terrum ABI to argument/result staging, roots,
failure/cleanup and output/dependency publication. Neither equal native
carriers nor recognizing a known body permits foreign retagging, extra hidden
arguments, contract erasure or silent fallback to Development/private data.

Under [format versioning](../specs/representation/format-versioning.md),
draft IDs may be intentionally replaced without compatibility shims.
Exchange still needs exact schema/catalog agreement, even if `(1,0)` or
Feature revisions do not change. Equal header bytes do not select this
candidate; no alias, speculative reader or compatibility promise is added.

## Owner integration if adopted

Only this proposal is added now. The following identify future amendments,
not changes already made:

| Owner | Exact intended integration |
|---|---|
| [Callable families](../specs/type-families.md#callable-values-and-tuples), [grammar types](../specs/grammar-spec.md#32-types-parameters-and-arguments), [expression semantics](../specs/grammar-spec.md#513-operators-assignments-and-expression-gaps) | Add the ordinary static function-designator branch of `&`, eligibility, expected-type/inferred contract rules and raw unsafe obligation. Preserve existing productions, storage addresses, Terrum default, foreign identity and absence of an environment. |
| [Native roles](../specs/native.md#native-declaration-roles) | Distinguish ordinary Terrum entry formation from NativeAddress's native declaration roles; preserve callable-import addressability and native export/caller-unsafe rules. Do not add zero to native role schemas. |
| [ABI layers/boundaries](../specs/data-layout-and-abi.md), [target profiles](../specs/targets/README.md), [JIT](../specs/jit.md), [code lifetime](../specs/native.md#runtime-and-code-lifetime) | Add explicit same-runtime indirect-entry admission and escaped-entry/lifetime obligations. Supply the actual target contract before execution claims; do not amend Windows foreign carriers into a general Terrum ABI by implication. |
| [Static initialization](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage), [call protocol](../specs/il-instruction-set.md#calls) | Classify runtime static-method address access as above; preserve initialization effects/order and the existing invocation phases, output/failure and reentry semantics. Calls remain invocation, not formation. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [instruction records](../specs/il-instruction-set.md#typed-operand-records), registry | Compose TypeSpec-only zero and the generalization of candidate `E9`/record `0D`; preserve actual-target/anchor separation and the complete existing operand shapes. No other numeric allocation or reference kind. |
| [Manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md), [binding](../specs/representation/binding-and-replacement.md), [trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Broaden the selected candidate Feature scopes explicitly; add use-specific ordinary entry/ABI checks and typed preservation rules without demanding consumer semantic body re-proof. |
| Source/anchor/default-convention/address companions | Supersede only their missing ordinary-formation boundary and candidate native-only operation naming/eligibility. Preserve their contract defaults, applicability, projection, byte-domain proposal, foreign rules and independent pending gates. |

## Proposed acceptance tests

These are required evidence for implementation, **not executed results**.
Use public compiler APIs and independent source-free consumers with unrelated
synthetic functions/providers. Keep parsing, binding, Standard round-trip,
emission and supervised headless execution separate.

| Case | Required outcome |
|---|---|
| TFA-01 Ordinary producer | The example's unannotated top-level function and an ordinary static method form/store/pass/return/invoke default pointers. Require neither native role nor OS symbol. Exercise inferred exact and bare-default projected views. |
| TFA-02 Actual target | Two behaviorally different functions share a structural anchor. Round-trip and execute the selected function; reject the anchor directly and through an Import as an address target. |
| TFA-03 Eligibility | Reject implicit bound/open receivers, abstract/unbound declarations, special-role authority and unresolved generics. Admit an ordinary static wrapper with explicit receiver inputs and a nongeneric wrapper calling a closed generic specialization. |
| TFA-04 Source selection | Exercise shadowing, qualified static names, overload disambiguation and absent expected types. Ambiguous groups fail without contract-only ranking; existing `&place` behavior is preserved. |
| TFA-05 Convention/role separation | Keep default Terrum distinct from all four foreign conventions, including nested aliases/parameters/results. Reject foreign/default retagging and native-role zero; preserve callable NativeImport addressability without NativeCallable on the foreign branch. |
| TFA-06 Complete contracts | Preserve declared Effects, no-return formation/terminal invocation, dependencies including explicit empty/whole-source/backing distinctions, checked references and outcome-correlated outputs. Reject false projection upgrades, lost cleanup/retention and invalid initialization-capability escape. Confirm pointer-cell initialization is not pointee initialization. |
| TFA-07 ABI execution | Under an explicitly selected target/runtime contract, test zero/unit positions, mixed scalar/register/stack arguments, aggregate/hidden results and every admitted reference/owner mode. Reject mismatched domains even when native carriers coincide. Include source-free AOT and JIT rather than emitted-byte inspection alone. |
| TFA-08 Failure/reentry/order | Compare direct and indirect ordinary failure/cleanup behavior; test nested argument calls, once-captured target, recursive/reentrant calls and separate supporting storage. Address formation does not invoke the target. Preserve successful static-trigger effects/order; an escaping initialization failure publishes no pointer, with cycle/failed-unit behavior unchanged. |
| TFA-09 Lifetime/replacement | Transport through forwarding Modules, fields, joins and results without losing actual origins. Reject known invalid uses; retain old entry/helpers/unwind data while usable pointers exist. Validate compatible semantic relowering separately from unchanged precompiled replacement; an Import update cannot patch an escaped address. |
| TFA-10 Structural/trust | Independently reject malformed extents/controls, wrong map/token namespaces, invalid anchors/results/Homes, missing references and incompatible bindings. Separate source-verifier authorization/flow tests from structural consumer tests; do not claim mandatory detection of all false body facts. |
| TFA-11 Boundaries/closure | Exercise local index zero/65,535 and counts 65,536, one-past-end and checked-extent failures, typed global-row limits and reserved bytes. Require full selected Feature/dependency closure for address-only, discarded and retained unreachable uses; unknown/incomplete required semantics cannot be erased. |
| TFA-12 Remapping/persistence | Independently permute/deduplicate maps/anchors and pack multiple Modules; rebuild contracts, selectors, Body/source maps and import expectations without merging targets. Round-trip complete types and address-formation targets through Standard dependency loading and every affected private/native persistence path without serializing process addresses or using a lossy fallback. |
| TFA-13 Exact schema | Current-schema zero/`E9` and legacy Development/private shapes do not become candidate input by matching version bytes. Explicitly selected candidate producers/consumers agree on the whole schema and catalog; no compatibility decoder is inferred. |

## Remaining decisions and implementation gates

The recommendation settles a provisional **ordinary static entry rule**,
not normative adoption. The remaining consequential gates are:

1. **Approve or replace the semantic choice and scope.** A, the proposed
   raw unsafe rule, source inference and static-access classification remain
   unapproved. Neither B's new role nor C's breaking redesign is silently
   selected. Open-instance/generic-entry recipes and dual native/Terrum
   entries are outside this first rule, not inferred permissions.
2. **Select the complete first target/runtime ABI.** Record the concrete
   mappings and supported signature/failure/root/cleanup subset against the
   obligations above. Independently replaceable native artifacts additionally
   need a complete assumption/publication contract. The prototype's Windows
   register usage is not an answer to either decision.
3. **Select the combined source/anchor/Standard schema and capability
   composition.** TypeSpec zero, generalized `E9`/record `0D`, complete
   contracts and their exact retained references/scopes remain candidate
   prerequisites; richer selector/output/runtime carriers keep their own
   pending gates. No new ID, Feature, alias or reader is approved here.
4. **Complete and qualify the admitted route end to end.** The current
   prototype binds bare default pointer types and now forms inferred or
   explicitly viewed addresses of eligible ordinary scalar functions. Focused
   Release-fast tests exercise same-module NativeJit invocation and a private
   revision-92 producer/consumer round trip. This does not select the final
   ABI or satisfy the Standard, Development, broader signature, hidden-input,
   persistence, packing, CLI or dependent-runtime gates. Continue those
   routes through binding, shared rewriting, mandatory verification and
   lowering; do not claim broader callable or roadmap completion.

The implementation reference records current bounded support separately from
this unapproved proposal. The proposal preserves the accepted default's
identity while making its remaining addressability decisions concrete enough
for provisional integration.
