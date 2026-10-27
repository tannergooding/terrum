# Standard function-pointer source contracts

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This proposal supplies a source/semantic decision,
not normative adoption, implementation evidence or released stability. It
changes no accepted owner and approves no syntax, Feature, row, flag, encoding,
alias, compatibility reader or target policy.

## Decision and boundary

The genuine gap is which invocation contract a source pointer promises.
Accepted source rules require target-contract preservation, and Standard
`0x0D` identity already includes applicable contracts, but bare syntax does not
state whether those are fixed defaults or target-derived facts, nor which may
be forgotten. A signature-only implementation cannot decide that policy.
The missing decision is not another anchor encoding.

Recommend the smallest semantic amendment: **every function-pointer type carries
an exact normalized invocation contract**. Bare `fnptr<Args..., R>` spells the
language-versioned default contract for that signature, not an unspecified
contract or a wildcard. An inferred `NativeAddress` value retains its selected
target's applicable invocation contract. A typed boundary may project that value
to another function-pointer contract only by checked directed satisfaction.

Thus a target's stronger no-return or backing guarantee need not make an
ordinary callback unusable, but a conditional output or retention obligation
cannot disappear because its machine signature matches. Address formation and
value conversion select a contract view; they do not change equality.

This proposal depends on the unapproved
[anchor-contract proposal](standard-function-pointer-anchor-contracts.md).
That document owns applicable clauses, anchor equality/rebasing and exact
metadata validity; this document supplies the missing source meaning. Its
bounded predicate/capability prerequisites remain gates. No broader predicate,
receiver, generic-callable or native-output protocol is admitted here.

| Unapproved alternative | Consequence |
|---|---|
| **Fixed bare-type defaults, exact inferred address types, checked projection — recommended** | No new grammar. Preserves complete facts in inference and interchange, while keeping existing explicit signatures deterministic. Nondefault types are not all independently source-nameable. |
| Explicit contract-carrying function-pointer type form | Would name ordered formals, effects, output/outcome relationships and dependency destinations/sources in a type-local scope. It would permit independent authorship of nondefault parameter/result/field types, but needs a complete source grammar, annotation applicability, predicate binding/default rules and source-view contract. No spelling or registration is selected here. |
| Signature/convention-only erasure, or copying target facts into a contextual bare type | Reject as a solution: the former loses obligations; the latter makes one source type mean different contracts depending on an initializer, assignment or selected provider. |

The recommendation deliberately defers independent source spelling of richer
callback APIs. Inferred values and imported complete types remain usable under
their established contracts; a new source declaration using bare `fnptr` exposes
only its defaults. Use an ordinary authored wrapper with a compatible declared
contract when necessary. Do not invent a `typeof(NativeAddress(...))` type form:
the accepted query is type/Place inspection, not an arbitrary-expression type
constructor.

## Exact owners and intended amendments

These are proposed additions/clarifications in the existing owners, not a new
normative authority or edits made by this document.

| Owner | Intended amendment; preserved rule |
|---|---|
| [Callable families](../specs/type-families.md#callable-values-and-tuples), [grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments) | Specify the fixed meaning of bare `fnptr` below. Preserve result-last syntax, ordinary by-value parameters, transparent aliases, the default Terrum convention and the distinct NativeCallConv type use. |
| [Complete-type identity](../specs/language/types-and-normalization.md#identity-versus-checked-adaptation) | Make the normalized invocation contract identity-bearing for `fnptr`; define projection as adaptation, not equality or contract-only overload discrimination. No corresponding `func` redesign is proposed. |
| [Native declaration roles](../specs/native.md#native-declaration-roles), [code lifetime](../specs/native.md#runtime-and-code-lifetime) | Clarify NativeCallable/NativeAddress preservation and view selection below. Preserve import addressability without NativeCallable, caller-unsafety and actual provider-code lifetime. |
| [Standard default convention](standard-function-pointer-default-convention.md), [Standard address formation](standard-native-address-formation.md) | These separate unapproved proposals own the `0x0D` byte-domain candidate and the Body carrier for NativeAddress. This document selects neither assignment. |
| [Output defaults](../specs/language-contracts.md#22-declared-outcome-relationships), [dependency defaults](../specs/language/dependencies-and-views.md#broad-sources-and-backing-only-forwarding), [Effects](../specs/representation/contract-clauses.md#3-effects) | Apply existing defaults and distinctions to the logical signature; do not substitute missing rows for required normalized source meaning. |
| [Directed binding](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions), [bounds](../specs/representation/binding-and-replacement.md#3-bounds-predicates-and-retained-observations) | Reuse caller-domain/guarantee checking for value-view projection, with the explicit native-unsafe specialization below. Preserve exact state, ownership and phase dimensions. |
| [Standard TypeSpec](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [anchor proposal](standard-function-pointer-anchor-contracts.md#proposed-normalization-and-equality) | Construct the anchor from the selected complete static type, not from signature alone or whichever target body is nearby. Preserve the existing `0x0D` schema and separate actual declarations. |

## Proposed source-type meaning

Represent a complete pointer type semantically as family, complete logical
signature, original convention and normalized applicable contract. Its contract
contains the anchor proposal's admitted Effects, OutputState/selector and
Dependency facts, including required source defaults. Labels remain source
views. Neither a target MethodDef identity nor its NativeCallable/export/import
role becomes structural function-pointer identity.

For a **bare source type**, compute defaults in its defining language-edition,
Module and generic context, without consulting an initializer or provider body:

| Dimension | Fixed default |
|---|---|
| Effects | No declared DoesNotReturn or explicit caller-unsafe effect: normalized mask zero. Native invocation is independently caller-unsafe. |
| Checked initialization outputs | Apply the ordinary output defaults: Boolean result means complete on true/vacant on false; other results require complete outputs on every normal return. Normalize required conditional defaults into the published contract. A raw pointer parameter is not an initialization output. |
| Dependencies/capture | Apply the ordinary whole-source rules. A dependency-bearing result with one eligible input uses its broad relationship; with none it needs independently valid state; multiple eligible sources require a declared set. Ordinary borrowed inputs do not permit undeclared retention. |

Formal ordinals exist even without names in the type spelling. Bare syntax does
not invent a receiver or a callee-parameter-storage source. Where the signature
and governing generic/default rules cannot establish a valid complete default,
the bare type cannot be formed; it is not a deferred wildcard to be completed
from one target. In particular, do not choose one of several possible backing
sources. An inferred address may instead retain the target's explicitly
declared set.

Absent versus explicit-zero Effects normalization, explicit empty dependencies,
source-default publication and declared-record identity follow their existing
owners and the anchor proposal. Do not collapse all logically redundant facts
into equality. Different complete contracts can nevertheless satisfy a directed
projection.

The source default convention remains **Terrum**, not Unmanaged. Under the
current accepted schema, Standard `0x0D` represents only its foreign
conventions. The separate unapproved
[default-convention proposal](standard-function-pointer-default-convention.md)
recommends a TypeSpec-only tag `0` for Terrum; this document neither adopts
that candidate nor maps Terrum to a foreign alias merely to emit a row.

## Address formation and NativeCallable

1. Select the actual declaration and any admitted closed substitution through
   ordinary binding. Do not use contract-only overload ranking. Check its
   existing native-address eligibility, convention, complete signature,
   representation and lifetime obligations. A signature anchor is never a
   target. No abstract code address, captured environment or implicit receiver
   adapter is manufactured.
2. Obtain the target's complete normalized applicable invocation contract,
   including authored facts and adopted defaults. NativeCallable stabilizes
   that selected signature/convention **and those invocation promises** for
   native pointer use; it does not create a contract-free alternate entry.
   Callable NativeImport formation uses the same rule without requiring a
   NativeCallable marker.
3. Without an expected pointer type, NativeAddress has that complete contract
   type. With an expected type, first establish the same exact target contract,
   then check projection to the expected view. Context cannot mutate the target
   or fill extra facts into a bare type.
4. Project only applicable invocation facts. NativeCallable, NativeExport,
   import identity and evaluation-form authorization remain actual-declaration
   facts, not anchor clauses. A target requirement outside the admitted
   invocation carriers cannot be silently erased to make formation succeed.
   A Pure/ConstEval role does not authorize indirect pure/constant execution.

Keep declared Effects separate from **effective** caller-unsafety caused by the
native role/family. A source `unsafe` declaration contributes its declared
caller requirement; a decoded published Effects `04` remains a declared fact.
Do not inject `04` solely because NativeCallable, NativeImport or native
function-pointer use is unsafe. Conversely, do not delete an existing `04`
while claiming exact type preservation. Unsafe formation/invocation remains
required independently, and authorizes no operations inside an authored body.

## Checked projection, storage and invocation

Projection from contract `C` to expected contract `E` requires the same family,
original convention and complete ordered signature/modes. Check that every
caller admitted by `E` satisfies `C`, and `C` supplies every guarantee of `E`,
using only supported relations:

| Dimension | Permitted projection; prohibited forgetting |
|---|---|
| DoesNotReturn | May forget the absent-normal-edge guarantee if no independent promised normal completion or other obligation is violated. Never infer it from result type or manufacture it for a returning target. |
| Caller-unsafe | Include the family's immutable native-unsafe gate in the caller-domain comparison. Within the foreign raw-pointer family it already satisfies `04`, so a view may omit that redundant requirement without making invocation safe. This is not equality, an Effects-mask ordering, or a conversion to a safe callable family. |
| OutputState | Preserve exact destination, complete type, selector meaning, phase and complete/vacant state. Dropping conditional rows to obtain an unconditional-complete default is strengthening, not weakening. A live failure output is not interchangeable with a vacant one. |
| Dependency | Use the existing source-bound refinement relation: an actual source subset/backing-only refinement may satisfy a broader permitted bound. `C` must not borrow/capture a source, borrow source storage or retain a source longer than `E` permits; it must supply every validity/invalidation guarantee of `E`. Preserve destination and phase. Empty sources and absence are not interchangeable by row deletion. |
| Other obligations | Preserve ownership, access, invalidation, cleanup, provenance, provider/code lifetime and all required capability support. DoesNotReturn does not make these checks vacuous. Unsupported implication is not success. |

Perform this check at address formation and ordinary pointer-value
initialization, assignment, argument and result boundaries. No new cast syntax
or runtime adapter is needed for an admitted projection: it changes the static
view, not the code address. It creates no thunk, marshalling or lifetime
extension. It is not a way to validate arbitrary foreign address bits.

An inferred local keeps its complete inferred type; later assignments do not
re-infer it. A bare-typed variable, field, parameter or result always has its
fixed default view. Imported declarations retain their exact published types,
including nondefault contracts that cannot yet be independently spelled.
Writable cells and references to cells remain invariant; converting a value
does not convert aliased storage. With a contextual join type, check each input
against it; without one, require equal complete pointer types rather than
inventing a merged contract. Generic inference retains complete type evidence,
not merely shape/convention.

Indirect invocation uses only the value's selected static contract, even when a
local optimizer recognizes its target. It enforces unsafe authorization, stages
arguments, retains all actual source/owner dependencies, and applies the saved
result's output correlations and dependency publication/retention phases.
DoesNotReturn in that view removes normal continuation for any declared result;
a weakened view cannot claim that proof. Relying on a stronger known provider
fact requires retaining the corresponding dependency, not secretly changing the
pointer parameter's contract.

## Cross-layer implementation obligations

* **Semantic model and verification:** intern/compare complete contracts, not
  signature triples. Keep declared effects distinct from native-role unsafety.
  Bound address/projection/indirect-call nodes and shared visitors/rewriters
  carry the selected contract and argument/source correspondence. Mandatory
  verification repeats eligibility/projection checks on rewritten programs;
  inspecting the original target body is not a substitute.
* **Generic substitution:** substitute complete signature types and every
  contract subject/type/dependency in the same declaring context, then
  normalize/validate. Preserve source permissions and exact external
  identities; never reconstruct defaults using a consuming edition or
  specialize from a sample body. Enclosing formals use existing admitted
  substitution rules; there is no new method-generic pointer environment.
  Native use requires the admitted closed signature. Inferring an opaque `T`
  does not itself authorize calling it as a function pointer.
* **Standard production:** emit the selected static view's bodyless anchor,
  complete MethodSig and applicable normalized clauses, including source
  defaults that require records. Rebase target-local formal/result/selector
  subjects to the anchor under the companion proposal; do not copy declaration
  roles or use the real callable as the anchor. A projected value can use a
  weaker anchor while its address operation still references the actual target.
  Required capabilities follow retained semantics, not reachability.
* **Persistence and provider binding:** retain complete types in references,
  module materialization and private persistence as well as Standard loading.
  A shape-only Development/private record is not a lossless carrier for this
  model. The separate
  [private carrier proposal](native-function-pointer-contract-private-persistence.md)
  amends the unimplemented candidate 97 with exact rows and type references;
  it remains unapproved and this document assigns no private revision or
  alternate decoder. Bind the
  actual selected provider against every retained expected declaration, and
  separately compare the structural pointer contract. Provider extra facts do
  not silently strengthen an imported pointer type or erase an expectation.
* **Rewriting/remapping and lowering:** follow the companion anchor remapping
  rules for contract/selector owners and typed references, nested types,
  substitutions and Body maps. Keep actual address targets distinct from
  anchors and preserve code-origin/import dependencies even for address-only
  uses. Lower the selected view's no-return/result selectors, output states,
  argument provenance and retention obligations. Equal machine carriers never
  justify losing a contract or changing a convention.

Consumers still perform required structural, support and binding checks, not
semantic re-proof of a provider's body; producers remain responsible for truth
under [consumer verification](../specs/representation/reader-trust.md).
[Exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
applies. Implement the admitted source-to-Standard route end to end; a blanket
diagnostic in place of its producer is not completion. An unsupported required
carrier still cannot be bypassed during that work.

## Candidate conformance cases

These are proposed acceptance tests, not executed results. Keep target/native
execution qualification separate from semantic and serialization evidence.

| Case | Required outcome |
|---|---|
| Positive default | Bare foreign scalar signature, ordinary NativeCallable address and callable NativeImport address without NativeCallable: normalize compatible default types, emit Standard anchors, preserve unsafe calls and code lifetime without mandatory synthetic Effects `04`. |
| Exact identity | Different actual methods with the same resolved signature/convention/contracts yield equal inferred pointer types; changed Effects, selector, dependency or nested complete type distinguishes identity. No contract-only overloads. |
| No-return/projection | A non-unit no-return target inferred into a local retains terminal indirect flow. Projection to a default returning view is allowed only after independent checks; it retains the result type but loses that flow proof. The reverse projection fails. |
| Output defaults/negative | Normalize Boolean true/complete and false/vacant defaults, and non-Boolean unconditional completion. A status-conditional target cannot satisfy that unconditional view; a live false output cannot satisfy the Boolean vacant-false view. No integer-success heuristic. |
| Dependencies | One eligible input uses its broad default. Explicit empty/backing-only target contracts may satisfy an appropriate broader view; extra retention, an additional possible source, wrong destination or changed phase fails. Multiple eligible inputs do not acquire a guessed bare default. |
| Storage and joins | Copy/infer/assign/pass/return pointers through locals, fields and parameters. Check value projection, keep mutable storage invariant, reject a reverse guarantee upgrade, and require equal types at an uncontextualized join. |
| Cross-feature unsafe | Copy/project a type with explicit `04` to a native-unsafe default view, then remove lexical unsafe authorization: formation/invocation still fails. No safe `func` conversion or authorization inside a callee follows. |
| Generic/nested | Substitute enclosing type/constant arguments through nested pointers, output types and contracts; preserve complete inferred `T`. Reject unavailable required substitutions, an unbound method-generic environment and invocation of an unconstrained opaque `T`. |
| Separate compilation/replacement | Source producer emits Standard, source-free consumer reloads exact contracts and forms/invokes a selected provider address. Check every retained expectation; reject a replacement losing relied-on no-return/output/backing facts or changing convention despite equal ABI carriers. |
| Remapping/lowering | Reorder/deduplicate anchors and rebuild selectors, exclusive ranges, substitutions and Body maps; retain actual target identity, address-only imports and source provenance. Round-trip and lower the same indirect-call contracts rather than recovering them from target names/bodies. |
| Unsupported/invalid | Reject invalid selected structures and decline unsupported required semantics without erasing clauses, including on no-return paths. Do not address an anchor, abstract declaration or unsupported receiver adaptation. Default Terrum pointers never become foreign `0x0D` rows. |

## Current gaps and remaining decisions

The current
[semantic signature record](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs)
contains only convention, result and ordered parameter types; its target
matcher excludes DoesNotReturn. Invocation facts instead live separately on
[BoundFunction and its parameters](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs).
[TrRmExecutableCompiler](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
still emits Development signature references and a Development artifact, not
this complete Standard source producer. The
[support inventory](../implementation.md#persistent-and-self-hosted-components)
records bounded Standard anchor consumption/comparison, with producer,
cross-module binding and remapping gaps. Existing
[source convention tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs)
establish narrower binding cases, not these proposed contracts or end-to-end
Standard production.

The rules above define the bounded provisional policy.
**Normative adoption remains unapproved.** A future
explicit contract-carrying source form remains a separate decision if
independently authored nondefault callback APIs are required; neither a type
query nor a new attribute is implicitly approved to supply it. The companion
anchor/predicate/default-convention/address-carrier proposals' outstanding
approvals and complete carrier support, and the implementation
migrations/tests above, remain outstanding. None establishes released-schema
stability or justifies a compatibility reader.
