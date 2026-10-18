# Compiler conformance handoff

This inventory separates accepted obligations from work conditional on proposal
approval. It is a handoff, not an exhaustive compiler audit or a conformance
certificate. Source/test observations below are static evidence; naming a test
does not mean it ran or covers the complete contract.

The [accepted specification](../specs/README.md) records current acceptance,
not immutable pre-v1 design. An implementation session must select its exact
accepted or explicitly authorized experimental contract. Pending assignments
remain in their proposals; do not implement them merely because this inventory
links them, preserve unpublished encodings automatically, or reinterpret old
bytes under a new schema.

**Statuses:** **Ready** means accepted work can be qualified and implemented;
**Blocked: design** needs the named choice or approval; **Blocked: dependency**
needs its prerequisite mechanism. **Bounded implementation; evidence required**
is a qualification gate, not an assertion of a newly reproduced defect.

| ID | Area | Principal prerequisites |
|---|---|---|
| [CG-001](#cg-001) | Current-registry TRRM producers and consumers | Exact selected schema |
| [CG-002](#cg-002) | Shared references, dependencies and output state | CG-001 for standard persistence; reviewed codecs |
| [CG-003](#cg-003) | Failure, cleanup and publication composition | CG-002; reviewed exceptional Body representation |
| [CG-004](#cg-004) | Native declaration and Body integration | CG-001/002/003; native schema approval |
| [CG-005](#cg-005) | Accepted layout and Windows x64 ABI | Selected target; CG-001/004 for standard persistence |
| [CG-006](#cg-006) | Generic nominal identity, witnesses and properties | Shared property/instance codec decision |
| [CG-007](#cg-007) | Construction, literals and constant programs | CG-002/003/006/010; reviewed value/program codecs |
| [CG-008](#cg-008) | Static initialization | Reviewed storage/group/unit codec |
| [CG-009](#cg-009) | Owning fields and partial retirement | CG-002/003; complete ownership records |
| [CG-010](#cg-010) | Portable proof and constant-evaluation floor | Accepted source rules; codecs for independent exchange |
| [CG-011](#cg-011) | Numeric and selected library contracts | Registered identities, witnesses and actual programs |
| [CG-012](#cg-012) | Quiet arithmetic NaN qualification | Existing floating paths; no fixed-runtime-payload approval |
| [CG-013](#cg-013) | Non-unit DoesNotReturn | Accepted source and clause applicability |
| [CG-014](#cg-014) | Signed-minimum bootstrap remainder | Accepted integer arithmetic |
| [CG-015](#cg-015) | Fourth-child scalar projection ceiling | Selected 64-node/four-parameter experimental envelope |
| [CG-016](#cg-016) | Bootstrap/frontend profile integration | Coherent envelope and invocation decisions |
| [CG-017](#cg-017) | Private spans and returned-view promotion | CG-002; concrete private consumer/schema selection |
| [CG-018](#cg-018) | Source grants and native promotion | Concrete consumer and source-context ABI decision |
| [CG-019](#cg-019) | Registered string target | Frozen target/parser/value/resource decisions |
| [CG-020](#cg-020) | CAS and concurrent execution | Selected APIs, representation and physical operations |
| [CG-021](#cg-021) | Tasks, iteration and generated producers | CG-002/003/006/009; completion/phase codecs |
| [CG-022](#cg-022) | Roots, handoff, code and failure-boundary qualification | Complete selected runtime/target profiles |
| [CG-023](#cg-023) | CLI and source-input conformance | Accepted machine/source-input contracts |
| [CG-024](#cg-024) | Representation extensions and resource accounting | Encoding decisions and calibrated recipes |
| [CG-025](#cg-025) | Generic native regression and sample evidence | Final selected compiler inputs and active cases |
| [CG-026](#cg-026) | Source vocabulary, grammar and binding convergence | CG-023 input identity; accepted source owners |

## Evidence and closure rules

Read the [test project exclusions](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
and each selected test's gates. In particular, the retained DX11/Win32
prototypes and `BoundTreeTraversalTests.cs` are excluded; they are not active
coverage. Keep ordinary tests headless. Window/hardware qualification requires
its documented opt-in and cannot be substituted for generic regression cases.

Implement general facts through binding, shared nested traversal/rewriting,
mandatory verification, persistence, dependency binding/replacement and lowering.
Test public compiler APIs rather than adding internal-access grants. An
unsupported complete contract must decline before affected use, not erase facts
or produce success-shaped defaults.

Close an entry only with evidence for its final relevant patch/configuration
and exact compiler/dependency inputs. Distinguish parser, semantic, emitted-image,
native ABI and supervised runtime evidence; record passed, failed, skipped and
incomplete cases separately. Use the normal [build/test workflow](../building.md).
Qualification tooling consumes those outputs and does not silently build or
select another compiler. Existing valid evidence can be reused when its relevant
inputs remain unchanged.

<a id="cg-001"></a>
## CG-001: Current-registry TRRM producers and consumers

**Ready for the accepted bounded schema.** The
[registry](../specs/representation/registry.md),
[manifest](../specs/representation/semantic-manifests.md),
[reference/admission rules](../specs/representation/requirements-and-admission.md)
and [independent vectors](../specs/conformance/il-registry.md) own the target.
The [encoding inventory](trrm-encoding-history.md) records development
assignments, not conformance to `trrm/1-draft-unified-3`.

**Current/evidence:** `TrRmBodyExampleTests.ReadExample` explicitly embeds
development-schema fixtures; private/native and output experiments retain
other assignments. See
[ModuleCompiler](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs),
[Feature admission](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs),
[Body fixtures](../../tests/Terrum.CodeAnalysis.Tests/TrRmBodyExampleTests.cs)
and the [module inventory](modules.md). Existing test success would not establish
current-registry decoding.

**Routes/gate:** reconcile the complete producer/reader/rewriter and executable
consumer set: 48-byte Module edition/ranges, exact Feature admission, typed rows
versus DefIds, canonical Param modes, type/operation assignments and Body indexes.
Independently decode current vectors; exercise all forms/width boundaries,
malformed and retired assignments, unsupported required capabilities,
same-version incompatible drafts and packed/import contexts. Check scoped
trusted access separately from whole-artifact audits. Rebuild decoded structures
and references rather than byte-substituting old fixtures. Check SourceMap's
reserved-zero Body slot kind, target remapping and dropping separately from
opaque skipping. No legacy decoder or new version pair is required solely to
retain unpublished artifacts.

<a id="cg-002"></a>
## CG-002: Shared references, dependencies and output state

**Ready for accepted source semantics; standard codecs blocked on design.**
[References](../specs/language/references.md),
[dependencies](../specs/language/dependencies-and-views.md),
[initialization](../specs/language/initialization-and-flow.md) and
[calls](../specs/language/calls-and-inference.md) are accepted. The
[reference-call](../proposals/reference-call-metadata.md) and
[executable-output](../proposals/trrm-output-parameters.md) recommendations
remain unapproved physical integration.

**Current/evidence:** the [output inventory](output-profiles.md) distinguishes
bounded hosted/generic paths from ScalarInitOutput's experimental mode, Feature
and Body rules. [Scalar output reader cases](../../tests/Terrum.CodeAnalysis.Tests/TrRmScalarInitOutputBodyReaderTests.cs)
are not the complete accepted reference/output model.

**Routes/gate:** preserve complete descriptor types, receiver versus formal
identity, eligible storage borrow versus backing forwarding, explicit/default
source normalization, actual output/destination versions and capture phases.
Preserve exact annotation targets across ordinary Boolean returns, async
completion payloads and non-async Task-returning declarations; a return target
does not select an initialized descriptor or its responsible operation.
Exercise empty source sets without losing definition-owned dependencies;
readonly/pinned/writable distinctions; optional-null versus vacancy; unit
outputs; lifting and later completion; named arguments, staged mutation,
aliases, branches/loops and separate providers. Reject unauthorized reads,
publication or authority/lifetime upgrades. Standard persistence depends on
CG-001 and approved complete dependency/place carriers, not on retaining
experimental `out` codes or UnsafeTail.

<a id="cg-003"></a>
## CG-003: Failure, cleanup and publication composition

**Ready for accepted behavior; richer standard execution blocked on design.**
[Failure/exit contracts](../specs/language-contracts.md),
[local failures](../specs/language/local-failures.md) and
[captured events](../specs/language/captured-exceptions.md) govern source
behavior. The [first exception slice](../proposals/checked-exception-trrm.md)
and [joint composition](../proposals/trrm-output-failure-ownership.md) must
agree before broader standard Body execution is claimed.

**Current/evidence:** [native support](native.md) and
[output support](output-profiles.md) describe bounded checked failure,
recovery and cleanup, not all typed payload/filter/capture or standard
exceptional-region semantics.
[Provider output/failure cases](../../tests/Terrum.CodeAnalysis.Tests/NativeProviderOutputFailureTests.cs)
cover a bounded separate-compilation surface.

**Routes/gate:** carry protected operation identity, pending failure/cause,
saved result and partial-output state through traversal, verification,
binding and lowering. Test cleanup before each live handler/filter,
false-filter effects, cleanup/ensure/filter replacement, no replay of exited
cleanup, live normal-failure outputs, unpublished partial retirement and
every-exit moved-input ownership. Keep already initialized ordinary explicit
outputs separate from still-private/reserved protocol publication; failure
does not impose a universal rollback. Distinguish fail-fast and actual process
termination from DoesNotReturn calls that can propagate with checked cleanup.
CG-002 supplies shared state; a catch-only codec must not become a permanent
incompatible output/owner system.

<a id="cg-004"></a>
## CG-004: Native declaration and Body integration

**Blocked: design approval.** The exact candidate allocation/rewrite and
capability owners are in
[native boundary metadata](../proposals/trrm-native-interop-metadata.md).
Do not copy its numeric candidates into code or the registry without selection.
Raw caller-unsafety, authored wrappers and generic rather than API-specific
compiler behavior are already settled.

**Current/evidence:** [modules](modules.md#persisted-caller-unsafe-operations)
and [native support](native.md) inventory current experimental native
sections/clauses/Features, copied vtable bindings and private revisions.
These are not the candidate next-draft schema. Active
[function-pointer](../../tests/Terrum.CodeAnalysis.Tests/NativeFunctionPointerTests.cs)
and [vtable](../../tests/Terrum.CodeAnalysis.Tests/NativeVTableCallTests.cs)
cases are bounded implementation evidence, not approval.

**Routes/gate after selection:** migrate declaration producers/readers,
structural signature identity, unsafe regions, pointer/output/dependency
facts, dependency materialization and all emitters together. Cover imported
addresses without proxy-body offsets; address-only import accounting;
callback/code-origin lifetime; local dispatch retaining its distinct imported
expectation; provider replacement and re-emission; nested pointer cells versus
pointees; signed-status and exact Boolean correlation; status/destination reuse;
actual backing/alignment/retention and absent optional annotations.
Use unrelated synthetic APIs and independently constructed malformed artifacts.
Depend on CG-001/002/003 as applicable. Loading-policy enforcement and the shared
declaration-property carrier remain explicit review choices.

<a id="cg-005"></a>
## CG-005: Accepted layout and Windows x64 ABI

**Ready for accepted target/source rules.** The
[layout contract](../specs/data-layout-and-abi.md) and
[Windows x64 target](../specs/targets/windows-x64.md) select packing,
explicit nested alignment, Union, scalar storage/validity, call
classification and non-unwinding ordinary boundaries. These choices do not
need reopening because support is incomplete.

**Current/evidence:** [layout support](layout-profiles.md) describes natural
Sequential/bounded Transparent source support and pending wider controls,
Union, standard commitment persistence and qualification.
[PublicFixedLayoutStorageTests](../../tests/Terrum.CodeAnalysis.Tests/PublicFixedLayoutStorageTests.cs)
and `Binder`'s layout-attribute path exercise that bounded support.
Neither compact ValueLayout limits nor native-pointer staging limits are
Windows ABI size ceilings.

**Routes/gate:** implement/qualify the
[target cases](../specs/conformance/target-windows-x64.md), including all
scalar representations, combined packing/alignment, nested propagation,
zero data, malformed layouts and exact-extent packed accesses. Independently
probe direct 1/2/4/8-byte aggregates, indirect 3/5/12/16/24-byte and larger
records, hidden results, mixed physical ordinals, over-aligned supporting
copies and original-place borrows. Separate storage, emission and execution
claims. Standard persistence requires CG-001 and the approved commitment
carrier from CG-004 or its reviewed shared replacement.

<a id="cg-006"></a>
## CG-006: Generic nominal identity, witnesses and properties

**Ready for accepted source contracts; physical integration blocked on design.**
[Types](../specs/language/types-and-normalization.md),
[witnesses](../specs/language/interface-witnesses.md),
[constant generics](../specs/language/constant-generics.md) and selected
library contracts are accepted. The
[generic/nominal metadata proposal](../proposals/generic-nominal-construction-metadata.md)
owns its candidate records, not another registry.

**Current/evidence:** [library support](libraries.md) describes
compilation-local closed internal generic structs and bounded function
witnesses. `Binder`/`Binder.ManagedClasses` and
`GenericNominalTypeTests` deliberately reject broader public/class/
conformance/constraint/generic-method forms. Constraint-row framing alone
does not execute a generic contract.

**Routes/gate:** preserve complete declaring/implementing/selected instances,
type and constant arguments, associated/generated identities, method formals,
constraints, witness origins and actual selected members through substitution,
verification, persistence and replacement. Test unrelated providers, inherited
instances, nominally distinct equal layouts, wrong argument/default domains,
qualified arguments and malformed remaps. Define one coherent owner for
Copy/Unique/Sized/property facts, reconciling the native scoped Copy commitment;
primitive identities cannot fabricate nominal declaration rows.
Approve complete instance/witness/call/value codecs before independent exchange.

<a id="cg-007"></a>
## CG-007: Construction, literals and constant programs

**Ready for accepted source rules; independent physical execution blocked.**
[Literal construction](../specs/language/literal-construction.md),
[handlers/builders](../specs/language/construction-handlers.md) and
[published forms](../specs/language/published-predicates-and-effects.md)
own the selected behavior. The
[proof](../proposals/static-proof-and-constant-evaluation.md) and
[generic/construction](../proposals/generic-nominal-construction-metadata.md)
proposals own pending carriers.

**Current/evidence:** [library support](libraries.md) distinguishes parsed
spread syntax and private generic specializations from executable registered
builders/protocols. A self-hosted method or candidate API sketch supplies no
registered identity, retained constant program or frozen value codec.

**Routes/gate:** retain exact role/member/witness identities and contextual
result, once-only source-ordered captures, after-capture acquisition, ordered
formatting/spread drain, refusal and accepted-prefix effects, output/owner
publication and abandonment cleanup. Qualify factory versus direct-cursor
capture and never-reached cleanup. Cross-check ordinary/pure/constant forms,
typed Unicode/format data, nominal value validity and inclusive resource
boundaries. Source-free execution needs CG-002/003/006/010 and approved
value/program/layout-dependency records; optional attributes and names are
not a fallback.

<a id="cg-008"></a>
## CG-008: Static initialization

**Ready for accepted logical behavior; metadata blocked on design.**
[Static source rules](../specs/grammar-spec.md#31-source-file-structure-and-declarations)
and the [logical records](../specs/representation/logical-records.md)
govern storage and initialization. The
[static metadata proposal](../proposals/static-initialization-metadata.md)
owns pending typed field/group/lazy-unit identities and executable integration.

**Current/evidence:** the [module inventory](modules.md) does not establish
complete retained closed-type static groups, phase authority, recovery and
unload support. Their exact implementation coverage is an audit question,
not a reproduced universal failure or permission to weaken accepted rules.

**Routes/gate:** establish actual current support first, then preserve
closed-type/group identity, ordered eager/lazy initialization, primary
constructor/ownership choice, effects, sharing, reentrancy/cycles,
failed-initialization state/recovery and cleanup/code lifetime. Exercise
independent consumers, generic closed types, replacement and malformed
group/field references. Distinguish the initializer's actual escaping event
from fresh waiting/later InitializationFailedException events: do not copy
the original payload, add an implicit shared cause or retry automatically.
Explicit recovery retains its separately selected contract.
Do not overload FieldDef instance offsets or infer
static semantics from a method name. No new draft version selector is
automatically required.

<a id="cg-009"></a>
## CG-009: Owning fields and partial retirement

**Ready; wider coverage must be established.**
[Owning-field contracts](../specs/language/ownership-and-cleanup.md) distinguish
complete values, structural partial move/repair, authored cleanup and
reverse-declaration versus reverse-acquisition retirement.

**Current/evidence:** [native support](native.md), [owner/view support](view-and-owner-profiles.md),
[OwnershipTests](../../tests/Terrum.CodeAnalysis.Tests/OwnershipTests.cs)
and [native origin cases](../../tests/Terrum.CodeAnalysis.Tests/NativeGraphOriginTests.cs)
cover bounded owners/cleanup/origin. They do not by themselves establish all
accepted partial-field, failure and crossing combinations; no new universal
runtime defect is asserted.

**Routes/gate:** audit and preserve exact live fields, original responsibility,
repair/retirement order and complete-receiver restrictions in binding, shared
rewrites, verification and lowering. Test failed construction at every
acquisition, moved children, guarded joins, replacement failure and cleanup
that diverges. Never invoke incomplete-parent authored Cleanup, replay a
retired child, or pass partial T as a whole value. Independent publication
depends on CG-002/003 and complete ownership/property codecs.

<a id="cg-010"></a>
## CG-010: Portable proof and constant-evaluation floor

**Ready for the accepted source floor.**
[Proof/evaluation](../specs/language/proof-and-constant-evaluation.md),
[published effects](../specs/language/published-predicates-and-effects.md) and
their [required cases](../specs/conformance/source-proof-and-constant-evaluation.md)
govern semantics and reproducible accounting.

**Current/evidence:** `Binder.ConstEval` retains private depth 64/work 4096
limits and admits only bool/char16/int32/uint32/int64/uint64 evaluator values.
`ConstEvalTests` tests those private boundaries. The
[implementation inventory](../implementation.md) explicitly does not claim
the complete portable floor.

**Routes/gate:** implement selected proof facts, executed-path ordinary/pure/
constant forms, actual failure/effects and the minimum admitted value domain.
Qualify exact and one-over work/depth/live-state/payload boundaries, repeated
root operations, early branch refusal, invalid values and absence of runtime
fallback. Separate source evaluation budgets from decoder/audit budgets.
Independent program/value exchange needs CG-001/006/007 and reviewed codecs;
the logical floor is not blocked merely because those codecs are pending.

<a id="cg-011"></a>
## CG-011: Numeric and selected library contracts

**Ready for accepted members; candidate APIs remain blocked on approval.**
The [adopted inventory](../specs/libraries/adopted-inventory.md),
[numeric groups](../specs/language/numeric-capabilities.md),
[selection](../specs/language/numeric-selection.md) and
[primitive operators](../specs/language/primitive-operators.md) are the owners.
The broad [Core candidate inventory](../proposals/core-library-reference.md)
does not approve every listed API.

**Current/evidence:** [library](libraries.md), [conversion](conversion-profiles.md)
and [native](native.md) inventories cover bounded scalar/witness paths, not
all 26 scalars, mathematical programs, library families or independently
registered identities.

**Routes/gate:** map each selected member to its exact Core declaration,
complete signature, constraints, effects, failure, validity and actual
runtime/pure/constant implementation. Cover widths/signedness, checked
intermediate failure, shift-count domains, heterogeneous results,
signed zero, NaNs, infinity and nominal witnesses. Do not infer semantic laws
from a name or layout, erase fallback identities, or claim all fixed-width
integers from a four-integer implementation. Additional mathematical,
container, I/O and application APIs need their explicit candidate decisions.

<a id="cg-012"></a>
## CG-012: Quiet arithmetic NaN qualification

**Bounded implementation; evidence required.** Accepted
[floating arithmetic](../specs/language/primitive-operators.md#floating-arithmetic-and-comparisons)
requires quiet processing NaNs, while
[constant execution](../specs/language/floating-environment.md) selects its
deterministic canonical recipe. The
[fixed-runtime encoding alternative](../proposals/floating-arithmetic-nan-encoding.md)
remains unapproved.

**Current/evidence:** `FloatingArithmeticModuleTests`' NaN oracle accepts
any NaN class, including signaling patterns; inspection established an
oracle gap, not an observed runtime violation.

**Routes/gate:** strengthen representation-sensitive runtime checks for the
required quiet bit without demanding a fixed runtime sign/payload. Separately
check canonical constant results, exact copy/selection and sign-only negation
of signaling/quiet payloads. Cover source, private dependency and selected
standard paths at the final relevant compiler inputs. A numeric IsNaN check
alone does not establish this requirement.

<a id="cg-013"></a>
## CG-013: Non-unit DoesNotReturn

**Ready; accepted applicability mismatch.** The
[source no-return rule](../specs/language/expressions-and-statements.md#no-return-contracts)
and [clause contract](../specs/representation/contract-clauses.md#4-doesnotreturn-and-actual-completion)
permit any admitted declared result type, retaining ordinary typing.

**Current/evidence:** `Binder` reports TRM3018 for non-void DoesNotReturn;
`SemanticVerifier` independently rejects the same combination.
[NoThrowTests](../../tests/Terrum.CodeAnalysis.Tests/NoThrowTests.cs)
and [module cases](../../tests/Terrum.CodeAnalysis.Tests/NoThrowModuleTests.cs)
exercise the bounded declaration/flow machinery, not the complete rule.

**Routes/gate:** remove the void-only restriction consistently in binding,
reconstructed verification, signatures/contracts, imports/replacement and
source/TRRM/native flow. Test typed non-unit divergence/escaping failure,
false claims with a normal return, independent NoThrow, unreachable ill-typed
source and absence of a bottom/discard conversion. A non-unit Call still
requires its typed result operand; do not fabricate a normal result.
Unsupported result representations remain unsupported.

<a id="cg-014"></a>
## CG-014: Signed-minimum bootstrap remainder

**Ready; accepted arithmetic mismatch.**
[Primitive arithmetic](../specs/language/primitive-operators.md) requires
signed minimum `% -1` to yield zero, distinct from minimum `/ -1` failure.

**Current/evidence:** `BootstrapInt32MainSource.terrum` and rejection cases in
`BootstrapInt32MainReturnComponentTests` retain the older remainder rejection.
This is static source/test evidence, not an executed reproduction.

**Routes/gate:** reconcile shared folding across direct Main, helpers, locals
and call arguments. Check all supported signed minima, zero divisors,
minimum division and intermediate overflow under both runtime policies.
Keep source failure identity/order and emitted results consistent; do not fix
only one literal or route.

<a id="cg-015"></a>
## CG-015: Fourth-child scalar projection ceiling

**Blocked: experimental envelope selection.** The
[consolidated frontend proposal](../proposals/bootstrap-frontend-profiles.md#four-parameter-calls)
recommends a coherent 64-node/four-parameter profile; that is not a language
arity limit or automatic approval.

**Current/evidence:** `ScalarFrontendHost` rejects
`fourthRootPlusOne > 32`, while the Terrum producer/reader uses the six-bit
field alongside 64 nodes. This is an inconsistency in the candidate
implementation, separate from general source-language call support.

**Routes/gate after selection:** reconcile host projection audit,
complete-program decoding and summary invocation. Qualify fourth roots in
nodes 32 through 62 when preceding the call, plus duplicate/forward roots,
missing fields, reserved bits and node-count boundaries. Preserve complete
callee/argument identities rather than masking an out-of-range value.

<a id="cg-016"></a>
## CG-016: Bootstrap/frontend profile integration

**Blocked: design selection.** The
[bootstrap/scalar proposal](../proposals/bootstrap-frontend-profiles.md),
[invocation proposal](../proposals/scalar-frontend-invocation-profile.md),
[cleanup proof strategy](../proposals/scalar-frontend-cleanup-switch-transfer.md)
and [verification budget](../proposals/self-host-frontend-verification-budget.md)
are distinct decisions.

**Current/evidence:** [self-host support](self-host.md) and
[composition](bootstrap-frontend-composition.md) describe bounded hosted
verification/emission and private projection routes. Route-specific parsing,
local and capacity restrictions are implementation facts, not accepted
language policy or evidence of a bootstrapped compiler.

**Routes/gate after selection:** integrate one coherent envelope, source
namespace/alias rules, expression/folding/local behavior, workspace planning,
publication and invocation identity. Cover zero/one/two/many locals, unused
locals, exact/one-short workspaces, all argument routes, malformed inputs and
checked failure. General ranking proofs or verifier splitting require their
own selected scope. CG-014 is independently ready; CG-015 is tied to this
experimental envelope.

<a id="cg-017"></a>
## CG-017: Private spans and returned-view promotion

**Accepted relationships; private realization/promotion partly blocked.**
[Source dependency semantics](../specs/language/dependencies-and-views.md)
are already accepted. The
[returned-span](../proposals/token-span-result-forwarding.md),
[private persistence](../proposals/token-span-semantic-module.md) and
[cursor](../proposals/span-backed-token-cursor.md) proposals separate those
semantics from their bounded consumer/representation choices.

**Current/evidence:** `TokenCursorTests` contains forwarding cases;
`ModuleCompiler.TokenSpans` and the private-span/complete-program invocation
tests exercise feature-selected private artifacts, including revision 81.
A historical revision-77 fixture or revision-76 reader is not a compatibility
requirement.

**Routes/gate:** normalize explicit, untargeted and sole-source-default
declarations consistently; accept empty dependency syntax but reject an
incompatible body. Preserve every source through named calls, staging,
joins, cleanup, remapping and replacement. Qualify forged private roles,
layouts, ordinals, index carriers and drop order, invalidated sources and
source-free rejection. General returned-view promotion still needs its
concrete consumer and CG-002; no missing universal implementation defect is
asserted solely from that deferral.

<a id="cg-018"></a>
## CG-018: Source grants and native promotion

**Blocked: consumer and ABI decisions.** The
[invocation grant](../proposals/invocation-scoped-source-grant.md),
[persisted scanner](../proposals/scanner-invocation-profile.md) and
[native companion](../proposals/native-source-grant-library.md)
own separate authority, identity, whole-input and deployment choices.

**Current/evidence:** `NativeSourceGrantClassifier`,
`NativeSourceGrantLibraryTests`, `CliTests.SourceGrantLibrary` and the
decimal-float native fixture describe bounded experiments. Hosted source
capability, matching native companion and standard raw native ABI are not
interchangeable.

**Routes/gate after selection:** establish source extent/access/retirement,
image-bound effective stub signatures, context-stack transitions, private
callee adaptation and result publication. Test nested calls, malformed
adapters without arbitrary pointer probing, exact workspaces, allocation/
capture failure and exactly-once moved-owner cleanup. Every external crossing
needs compatible non-unwinding containment; hidden source context must not
silently alter an ordinary foreign signature. Do not infer a permanent ABI
from a working sample. Consumers retain accepted signed-token and decoded-text
semantics when adapting magnitude-only or narrower quoted-slice helpers;
those helpers are not complete source-literal oracles.

<a id="cg-019"></a>
## CG-019: Registered string target

**Blocked: design; existing helper is not a defect.** The
[string-target proposal](../proposals/registered-string-target-binding.md)
still needs the exact frozen target representation, capacity domain, parser
identity, value codec and resource recipe.

**Current/evidence:** `StringRow.terrum` provides an ordinary source-grant
helper. `StringLiteralComponentTests` covers owned-row publication/refusal
and capacity retry, not registered literal-target construction.

**Routes/gate after selection:** use bounded source-free constant execution
and frozen typed publication with CG-007/010. Qualify grammar/UTF validity,
capacity boundaries, deterministic cost, parser refusal/failure, dependency
identity and independent consumers. Do not implement the deferred
source-reading JIT route, retag an owned mutable row as an emitted constant,
or infer registration from the helper's name.

<a id="cg-020"></a>
## CG-020: CAS and concurrent execution

**Accepted semantics; API/physical integration blocked.**
[Memory ordering](../specs/memory-model.md) and
[concurrent executions](../specs/runtime/concurrent-executions.md) own the
accepted order matrix, read sources, failure and full-boundary effects.
The [CAS proposal](../proposals/atomic-cas-order-pairs.md) does not assign an
API/opcode/Feature by repeating that matrix.

**Current/evidence:** the current registry assigns no CAS operation or
capability. Implementation coverage for CAS/order APIs and broader concurrent
execution remains unverified.

**Routes/gate after selection:** retain both compile-time-known orders,
actual location/generation and defined bitwise comparison representation.
Check all 25 success/failure pairs, nonconstant orders, failed-read acquire,
SC/full effects with discarded results, checked-RMW overflow and exact
representation/width support. Independently qualify target/helper refinement,
publication, quiescence/reuse and grounded causality; equal size, padding
bytes, numeric equality or one x64 instruction do not establish the contract.

<a id="cg-021"></a>
## CG-021: Tasks, iteration and generated producers

**Ready for accepted logical rules; physical/runtime completion blocked.**
[Task responsibility](../specs/language/task-responsibility.md),
[value iteration](../specs/language/iteration.md),
[async iteration](../specs/language/async-iteration.md),
[lending](../specs/language/lending-iteration.md),
[async execution](../specs/language/async-execution.md) and
[generated producers](../specs/language/generated-producers.md) are the owners.

**Current/evidence:** the [library inventory](libraries.md) and
[logical representation inventory](../specs/representation/logical-records.md)
do not establish complete compiler/runtime or persistent task/frame/
phase/loan support. A candidate `Faulted(Error)`/Canceled catalog is not a
replacement for selected events, causes, reservations and responsibility.

**Routes/gate:** first map actual support, then preserve complete nominal
roles, creation/invocation/completion/observation identities, actual
destination versions, capture and frame/code origin, one outstanding advance,
pending reservation and every-exit settlement. Cover never-advanced/failed
close, value versus loan outputs, cancellation without settlement, forwarding,
early abandonment and cleanup/failure ordering. CG-002/003/006/009 supply
shared mechanisms; pointer-sized handles and optional attributes do not
provide missing independent codecs or hidden blocking.
Failed acquisition leaves no caller cursor to Close; any complete cursor
formed privately still has its actual holder's close/retirement obligation.

<a id="cg-022"></a>
## CG-022: Roots, handoff, code and failure-boundary qualification

**Bounded implementation; wider profiles require evidence/design.**
[Sharing/handoff](../specs/runtime/sharing-and-handoff.md),
[deployment](../specs/targets/windows-x64-deployment.md) and the separately
selected [failure bridge](../specs/runtime/foreign-failure-bridge.md)
preserve actual root, authority, allocator, code and runtime-domain lifetimes.

**Current/evidence:** [JIT](jit.md), [graphs](native-graph.md) and
[native support](native.md) describe bounded private heaps, roots, unwind/
registration and provider origins. They do not certify arbitrary shared
managed/view/task crossings or actual Terrum failure-packet transport.
The [bridge cases](../specs/conformance/target-windows-x64-failure-bridge.md)
explicitly separate native wire fixtures from Terrum execution.

**Routes/gate:** qualify actual roots through calls/cleanup, code/helper/
allocator edges absent from the IAT, full-point unwind/probing, control-state
restoration, registration and failed deregistration, quiescence/unload and
cross-image responsible release. Wider handoff needs real capability/token/
generation/return support. Bridge implementation additionally needs local
typed mapping/dispatch/capture, affine packet ownership, nested callback
containment and complete retained codecs; no shared foreign unwinder,
pointer-header probing or success-shaped capture failure.

<a id="cg-023"></a>
## CG-023: CLI and source-input conformance

**Ready for the selected contracts.** The
[`terrum.cli/1` protocol](../specs/cli-protocol.md) and
[source-input boundary](../specs/language/source-input.md) are accepted.

**Current/evidence:** the [CLI inventory](cli-protocol.md) distinguishes
experimental schemas and raw execution-worker statuses from the selected
supervisor envelope. `Program.Compilation` opts into source-authored native
imports; it is not a compiler-owned API catalog.

**Routes/gate:** establish supported operation/option/input/output/error
shapes, stable machine identities and separate program result/output from
supervisor status. Qualify actual malformed-byte/encoding boundaries,
snapshot/coordinate identity, no fabricated decoded prefix, multi-source
diagnostics, explicit cancellation/deadlines and output atomicity. Invoke
the built CLI rather than mixing `dotnet run` messages into protocol data.
A worker timeout or raw program status cannot be silently relabeled as a
successful supervisor response.

<a id="cg-024"></a>
## CG-024: Representation extensions and resource accounting

**Accepted existing rules; optional extensions blocked on selection.**
[Canonicality](../specs/representation/encoding-canonicality.md),
[reader trust](../specs/representation/reader-trust.md) and
[resource accounting](../specs/representation/resource-accounting.md)
already govern the current schema. The [inline/compact extensions](../proposals/il-extensions.md)
and [operation-family review](../proposals/trrm-operation-encoding-families.md)
do not activate reserved fields or new primary bytes.

**Current/evidence:** development fixture/reader behavior is not independent
proof of every selected scope/recipe. Numerical portable minima remain
unassigned, not invented defaults. A lower fixed-width ID is not smaller
on disk.

**Routes/gate:** CG-001 must preserve ordinary equivalent noncanonical
admission versus strict writers/scoped audits, retained root budgets,
checked preflight, structural versus semantic work, depth, payload/state
lifetime and explicit exhaustion. Calibrate rather than guess minima.
If an extension is selected, update its complete field matrix, indexed/
inline type identity, canonical writer, remapping, all Body forms and
independent vectors. Measure the claimed size/import benefit against actual
method shapes; no decoder alias or new pre-release format pair is automatic.

<a id="cg-025"></a>
## CG-025: Generic native regression and sample evidence

**Bounded implementation; evidence required.** The
[native generalization boundary](dx11-generalization-check-in.md) describes
the settled generic/authored-library architecture.
[Pipeline](../proposals/dx11-direct-pipeline.md) and
[resize](../proposals/dx11-event-driven-resize.md) documents are application
and qualification scenarios, not compiler policy.

**Current/evidence:** active generic native tests are distinct from excluded
DX11/Win32 prototypes. Static removal of known API/catalog recognizers does
not establish every producer/consumer, backing, adaptation or lowering case.
No historical defect list is presumed current without inspection.

**Routes/gate:** after CG-004 or other relevant native changes, exercise
unrelated synthetic vtables, function pointers, layouts, outputs and failure
paths through source, persisted dependencies, reconstruction and lowering.
Include nested traversal, address-only imports, aggregate scratch sizing,
callback/code origin, status/version correlation and malformed metadata.
Separately qualify the maintained sample's authored lifecycle and exact
image-domain/RowPitch/Unmap assertions. Visible-window and hardware evidence
requires opt-in; one WARP coverage mask or successful emission does not
establish a universal pixel oracle or application runtime success.

<a id="cg-026"></a>
## CG-026: Source vocabulary, grammar and binding convergence

**Ready for accepted source rules; route coverage must be established.**
The [lexical contract](../specs/lexical-grammar.md),
[source grammar](../specs/grammar-spec.md),
[names](../specs/language/names-and-binding.md),
[types](../specs/language/types-and-normalization.md) and
[calls](../specs/language/calls-and-inference.md) own admission. Proposal-only
families, annotations and APIs do not become source forms through examples.

**Current/evidence:** the [implementation inventory](../implementation.md)
distinguishes lossless syntax APIs, bounded semantic/native support and
experimental authoring. The [scanner](../scanner.md) and
[frontend inventory](bootstrap-frontend-composition.md) describe narrower
component protocols, not complete source conformance. This is a coverage
handoff, not a claim that every rule below currently fails.

**Routes/gate:** map accepted positive/negative cases to actual lexer, parser,
binder and public compiler entry points after CG-023 establishes exact source
snapshots. Cover keyword/escape and Unicode recognition; signed numeric
boundaries and raw/comment/documentation fences; declaration visibility and
modifier order; exact attribute target/argument schemas; complete qualified
type/alias normalization; finite call inference, source-order named argument
staging, defaults and params; selected pattern/refinement forms; and unit versus
explicit non-unit result discard. Compose the relevant CG-002/006/007/010/013/021
gates rather than adding a second semantic rule set.

Exercise hosted and Terrum-authored routes only for their declared scope,
with explicit unsupported diagnostics outside it. Parser acceptance is not
semantic validity or emission support, and parity with a bounded older
frontend is not proof of the accepted grammar. Do not weaken the language,
retain legacy source forms automatically or silently reinterpret an
unpublished profile to make a subset appear conforming.
