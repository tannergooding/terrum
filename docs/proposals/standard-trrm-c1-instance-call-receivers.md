# Standard TRRM construction-2 ordinary instance receivers

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow, pending later review.** This completes one
class-receiver mapping, not source admission, implementation support or a new
runtime ABI. It changes no normative document.

## Finding and owners

There is a **narrow interchange integration gap**, not an undecided source
ownership model. Ordinary receivers already have readonly/writable access,
borrow rather than transfer ownership, and precede explicit formals. What
is missing is the concrete ordinary class receiver's MethodSig/access and
CallSite source-use mapping alongside construction-2's special I receiver.

The [specification index](../specs/README.md) leads to these existing owners:

| Evidence | Already specified; remaining boundary |
|---|---|
| [Source receiver defaults](../specs/language.md#types-and-operations), lines 173–186; [receiver loans](../specs/language.md), lines 1420–1425 | Readonly by default; `mut` requires writable access, not ownership transfer. A mutable binding cannot upgrade readonly object access. |
| [MethodSig](../specs/il-metadata.md#section-kinds-and-row-layouts), lines 813–829, 866 and 1220–1222 | Receiver presence is separate from Param order; type-owned static methods have none. Receiver access still needs a specified contract-field mapping, not name inference. |
| [CallSite rows](../specs/il-instruction-set.md#typed-operand-records), lines 399–413; [calls](../specs/il-instruction-set.md#calls), lines 2131–2147 and 2182–2219 | Existing source-use domain and receiver-first logical order, once-only staging and invocation phases. These do not select the ordinary class receiver's concrete source-use/access projection. |
| [Dependencies](../specs/language/dependencies-and-views.md#dependency-identities-and-obligations), lines 30–45; [Dependency clause](../specs/representation/contract-clauses.md#6-dependency) | Receiver is not formal zero; a class receiver selects object backing, not the caller/callee handle slot. Existing rows already distinguish receiver sources and retained destinations. |
| [Class/member publication](standard-trrm-c1-class-source-publication.md#required-constructor-and-member-closure), lines 166–169 | Requires exact ordinary MethodSig/receiver/contracts/Bodies; does not assign that missing mapping. |
| [Constructor declarations](standard-trrm-c1-constructor-declarations.md#declaration-receiver-and-body-mapping), lines 120–149; [construction-2](standard-trrm-c1-construction-runtime-boundary.md#checked-construction-program), lines 162–184 | Explicitly assign receiver input zero with source-use 5 to unpublished I, not an ordinary live receiver. |
| [Normal-backing calls](reference-normal-backing-support.md#sources-staging-and-direct-calls); [construction native projection](standard-trrm-c1-construction-runtime-boundary.md#extension-to-ns-native-preparation) | Supply staged holds and a borrowed ordinary C control-pointer projection. They do not assign its serialized CallSite row or make it I. |

Inspected worktree source confirms a separate implementation defect; it is
not authority to change those source rules:

| Source line evidence | Consequence |
|---|---|
| [Binder receiver declaration](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L1504-L1517), [call binding](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L17061-L17098); [verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs#L22289-L22329) | Class `mut` access is carried by writable class type; the bound receiver is first and remains distinct from constructor invocation and caller-unsafety. Host argument slot zero is not a serialized Param ordinal. |
| [Call effects](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L2611-L2631), [Home/type validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L6397-L6419) | Both compute `hasConstructionReceiver` from schema plus **any nonzero receiver TypeSpec**, then call the construction-only validator. Ordinary calls cannot satisfy that classification. |
| [Construction-only validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L2666-L2701) | Requires MethodMap, selector Home role 4, source-use 5, caller role-2 P and its exact bodyful role-1 I, with matching owner/Module/type. This specifies/checks P→I, not ordinary instance borrowing. |
| [Compact MethodMap checks](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L2421-L2475), [I exclusion](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L6766-L6783) | Compact calls reject I and no-return targets but bypass that general-record receiver path. Their acceptance cannot substitute for receiver/window type, loan and lifetime checking. |
| [Source publication gate](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L816-L834); [native admission](../../sources/Terrum.CodeAnalysis/Native/NativeManagedClassRuntime.cs#L142-L205) | Construction-2 class closure is still refused. Native source admission recognizes bounded borrowed-receiver forms while rejecting ordinary by-value handle handoff; neither is Standard class-call round-trip support. |

The active [construction fixture](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmObjectConstructionBodyTests.cs#L124-L133)
exercises P→I, and [compact negatives](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmObjectConstructionBodyTests.cs#L240-L248)
reject I. Its [types/signatures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmObjectConstructionBodyTests.cs#L615-L645)
use a `0C/8A` Span-of-class stand-in for the receiver/context, not the
constructor proposal's nominal C. [Receiver Home tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmRegisterBodyReaderTests.cs#L1028-L1061)
check Home/signature agreement; [normal-runtime source tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeManagedClassModuleTests.NormalBacking.cs#L12-L43)
use the source factory/backend, not Standard ordinary-call reconstruction.
These files are included by the current [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj);
they are intended coverage, **not results from this review**.

## Proposed exact mapping

Amend **construction-2 only**, retaining its exact out-of-band identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2`**

Exact agreement now includes this mapping on the root and every supplied
provider, including unused inputs, before schema-dependent decoding. Do not
infer agreement from equal bytes/tokens or silently accept earlier artifacts
under a different interpretation; regenerate under the agreed complete schema.
No new selector, alias, Feature, opcode, record, field, bit or width is needed.
C1, C1M1, construction-1, Construction `0003` roles, class mutability and
`0B` meanings remain unchanged. Preserve
[exact draft agreement](../specs/representation/format-versioning.md).

On integration, add the following class-specific mapping to
[MethodSig/receiver access](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [CallSite/calls](../specs/il-instruction-set.md#calls), and link it from
the three class publication/construction proposals above:

1. **Classify the selected declaration before interpreting its receiver.**
   A role-1 I retains its existing initialization contract. Role-2 P has no
   receiver. An ordinary instance method has no Construction role and has a
   nonzero receiver TypeSpec resolving to its exact class owner C.
   Receiver presence alone never grants construction authority.
2. **Publish ordinary class access in the existing receiver TypeSpec.**
   A readonly method uses nonoptional normal `C`; a `mut` method uses
   nonoptional normal `write C`, using the existing write qualification and
   requiring C's declared mutability. No MethodDef flag, Param, name,
   writable handle binding or body inspection supplies that requirement.
   This is the class-specific contract-field mapping missing above.
   I's own receiver type/initialization authority remains its owner's rule.
3. **Count and order every logical input.** For `n` explicit Params,
   `inputCount = n + (receiverTypeSpec != 0 ? 1 : 0)`, with checked arithmetic.
   A present receiver is row zero; formal `i` is row `receiverCount+i`.
   Include unit formals. Param, OutputState, preparation and dependency
   formal ordinals do not shift. An unused callee receiver still counts,
   whether or not its Body materializes a receiver Home.
4. **Ordinary receiver row:** use `sourceUse=3` for readonly C and
   `sourceUse=4` for writable C. Select one already captured, live, complete,
   published handle value in R0..R13 or an ordinary typed Home (role 1, 2
   or 3). The row borrows object access; it does not read/borrow a handle
   slot as `ref<C>`, copy a handle or transfer its responsibility.
   None, R14/R15, a place handle, raw pointer, vacant/partial value,
   construction context or unpublished I receiver is invalid here.
5. **Type/access agreement:** resolve the selected target, nominal owner,
   using Module and admitted substitutions before checking the receiver.
   Require the same complete nominal/backing type and sufficient access;
   permit only the ordinary checked weakening from `write C` to readonly C.
   A readonly receiver cannot satisfy `write C`. No nominal conversion,
   optional unwrapping, GC/normal change or authority upgrade is inferred.
   In the callee, a materialized role-1 Home has zero flags and the exact
   receiver type; it is a nonrebindable borrowed view, not an owned formal.
6. **Keep source-use 5 distinct.** In receiver position it remains only I's
   existing P→selected-I, role-4-context initialization input, with all
   fresh-generation/lifecycle restrictions. Ordinary receiver uses 1, 2
   or 5 reject; I does not accept 3/4 instead. Explicit initialization
   formals still use their inherited source-use-5 descriptor contract,
   including in an ordinary instance call.
7. **Compact MethodMap calls follow the same receiver plan.** Existing
   `80`, `82` and `E1` window/result restrictions remain. The consecutive
   window includes receiver first and all logical formals; its signature
   implies the same ordinary receiver borrow mode. Check identical type,
   publication, authority, dependency and lifetime facts. Never use a
   compact call for I, a no-return target or an invocation whose complete
   preparation protocol cannot fit that form; use general `90`/`E0` as
   appropriate. Existing full `u16` map/Home indexes, including FFFF, remain.

Apply this to ordinary calls wherever the class/member is otherwise admitted:
from top-level/static code, other instance methods and published construction
results, including represented getter MethodDefs. Do not restrict it to P,
a source expression shape, constructor helper, sample or API. This decision
adds no struct, unique/generic/GC class, dispatch, bound-callable or foreign
ABI support; independently missing representations still refuse before use.

## Loans, effects and lowering

Capture the receiver once before written arguments. Its staged holder must
support Check → Prepare → Invoke and invocation cleanup under the admitted
failure policy. Later handle-slot rebinding cannot redirect that capture;
permitted writes through other aliases may remain observable. Readonly is
not a snapshot, Pure, NoAlias or exclusive-loan promise; writable borrowing
does not grant new field mutability or whole-object initialization.

Neither receiver input mode creates a second owning parameter, moves the
caller handle, releases its hold at entry or silently extends its lifetime.
Any required handle Copy/temporary staging is a distinct checked earlier
operation. Preserve actual allocation generation, owner/guard, access and
code origins through calls, joins, rewrites and replacement. Results,
outputs and captures retain their complete declared receiver/formal bounds
for their own extents, including captures surviving failure; one physical
backing hold does not erase the other declared sources. Reuse the existing
Dependency receiver identity, not formal zero.

Preserve Effects, unsafe-caller obligations, clauses, output phases,
no-return terminality and directed expected/actual binding. Borrow modes
authorize neither unsafe operations nor unsupported unwind. Consumers check
framing, references, selected support and bindings, without being required
to re-prove arbitrary producer-published body facts.

Under [N's separately selected normal agreement](reference-normal-backing-support.md#sources-staging-and-direct-calls),
the ordinary receiver borrows the staged handle's `NormalBacking*`, not a
payload address or handle-cell pointer. Keep its separate call-extent holder,
existing hidden-result/receiver/formal physical order and every-exit handoff;
add no hidden runtime argument, callee-entry retain or receiver-ownership transfer.
Source-free Standard preparation must derive this plan from checked facts,
not the tracing prototype or a serialized authorization.

## Alternatives and integration gates

**Select the existing CallSite amendment.** Receiver TypeSpec plus existing
3/4 modes and Construction roles distinguish every case required here.
A substantive alternative is a tagged receiver descriptor/contract with an
explicit mode, changing MethodSig or adding a record. That could organize
future consuming/dispatch receivers, but those are not this gap; it duplicates
the present distinctions and requires schema/remapping changes without
adding required information. No ID reallocation or new call opcode is justified.

Implementation must replace both blanket `hasConstructionReceiver` paths
with role-aware ordinary/I handling, and use the same signature plan for
compact validation, emission and source-free reconstruction. Carry the
borrow/access/backing facts through shared bound traversal, mandatory
verification and native lowering. Preserve original expected signatures and
all aliases while remapping receiver Homes, Type/Method maps, Imports,
contracts and Bodies together. Complete class/member publication and real
nominal I/context support remain prerequisites; do not remove their guards,
adopt the fixture's Span stand-in or publish field-only closure.

Required future checks, **not executed results**:

| Cases | Required outcome |
|---|---|
| Positive and boundaries | Two unrelated immutable/mutable class APIs; readonly and writable methods/getters; static controls; zero, unit and several explicit formals; Register/Home general inputs and all eligible compact forms. Preserve exact count/order and readonly weakening. Check last-fitting windows, spill-to-Home cases, checked counts and Home/MethodMap index FFFF. |
| Negative | Reject missing/extra/reordered receiver rows, wrong nominal/access/carrier/state, ordinary modes 1/2/5, wrong 3/4 requirement, context/unpublished receiver escape and receiver-as-formal substitutions. Retain all I lifecycle/5-only and compact-I negatives; use actual nominal C, not Span stand-ins. |
| Cross-feature | Ordinary receiver plus unit and initialization-output formals preserves formal ordinals, source-use 5 and output correlation. Retain receiver-dependent results/captures, later-argument rebinding/failure, allowed referent mutation, unsafe/effect clauses and general terminal no-return calls. Reject lost support or weakened obligations after joins/adaptation. |
| Separate compilation and execution | Through public Standard APIs, publish complete members, bind non-first/multi-Module providers and aliases, permute/pack/re-emit and reconsume without source. Reject receiver-contract changes and mixed exact schemas, including unused providers. With explicit N selection, headless JIT/AOT must exercise actual reconstructed ordinary calls, nested calls, receiver support and hidden-result/fifth-position placement under supported failure paths. Source-backend tests alone do not satisfy this gate. |

The ordinary receiver mapping has no remaining design choice in this
recommendation. Formal approval, implementation and qualification remain
open; existing class/runtime/ownership/accessor/witness support gates are
not discharged by this document.
