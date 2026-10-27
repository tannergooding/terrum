# Nominal enum values for ordinary constant forwarding

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow, not normative adoption:**

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This proposal resolves the shared value-integration prerequisite for
`native-callconv-typed-constant-forwarding`. It changes no compiler code,
normative specification or wire schema. Missing compiler enum support is
not evidence that the accepted enum language semantics are undecided.

## Decision and scope

Recommend **shared nominal enum value integration in the ordinary bound
type/value model and evaluator**, not a native attribute evaluator.
Represent a value logically as its exact normalized enum type T and a
checked value in T's declared underlying integer type U. Reuse the existing
integer payload and target storage machinery without replacing T by U.

The bounded admission needed here is nongeneric **int32-backed enums from
checked declarations**, regardless of whether they have a native role.
It covers the source-schema candidate's convention enum E and its distinct
resolution-mode/policy enums, plus unrelated enums supplied through the same
general declaration/value mechanism. Other accepted enum bases retain their
own support gates; int32 is this integration boundary, not a new language
restriction or permission to narrow other already admitted domains.

The [Core registration adapter](native-calling-convention-core-registration.md#proposed-registration-hand-off)
may supply those checked declarations before general EnumDecl parsing exists.
It must connect them to shared type/member lookup and typed values, rather
than supply a parallel native-only type system. General source enum parsing
is a separate implementation prerequisite for authored enum declarations,
not for consuming complete declarations from that admitted in-process
producer. A host registration handle alone is not a usable bound enum value.

Preserve the [source-schema candidate](native-calling-convention-source-schema.md)'s
exact E requirement, ordinary constant forwarding and final closed-value
mapping. This recommendation makes its shared-carrier prerequisite concrete;
it supersedes neither that candidate's public names/assignments/defaults nor
the Core-registration hand-off. A native-specific evaluator, direct-member
fallback or int32-to-E surrogate remains excluded by the settled direction.

NativeImport's schema-owned LibraryName/EntryName literal forms stay
unchanged. The [LibraryName candidate](native-import-library-name-source-contract.md)
and [resolution-persistence candidate](native-import-resolution-private-persistence.md)
retain their exact names, mode/policy omission rules and separate loader/
serialization gates; enum forwarding does not admit string-valued expressions.

## Accepted baseline and inspected gap

| Authority | Existing obligation, not a new choice here |
|---|---|
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [language enum values](../specs/language.md) | EnumDecl and integral member constants already exist in the accepted grammar. Members are checked in the exact eligible base; successor overflow rejects, duplicate-valued names are permitted, and unnamed underlying values remain valid. The selected profile supplies its eligible base catalog. |
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [normalization](../specs/language/types-and-normalization.md#transparent-expansion-and-termination) | Exact selected nominal definitions remain distinct from their carriers. Transparent aliases preserve the target identity; finite expansion, access and ordinary lookup still apply. |
| [Constant execution](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain) | Admitted nominal enums are already in the minimum value domain, subject to complete validity, encoding and emitted-result Blittable commitments. Actual eligible bodies/intrinsics, ordinary effects, failures, dependencies and resource accounting remain required. |
| [Constant-generic identity](../specs/language/constant-generics.md#canonical-value-identity) | Same-domain enum values compare canonically by underlying value, not member spelling; this does not supply a source equality operator or enlarge ordinary/attribute constant eligibility. |
| [Native roles](../specs/native.md#native-declaration-roles), [Windows enum storage](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas), [call classification](../specs/targets/windows-x64-call-interface.md#1-foreign-primitive-admission) | Native convention roles remain separate from ordinary enum validity. An enum uses its declared integer storage, with nominal validity retained through carrier classification; equal carriers do not establish foreign signature compatibility. |

Current source inspection establishes the implementation boundary, not
passing qualification:

* [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
  has struct/class and pointer directories but no nominal enum declaration
  model. BoundExpression/BoundConstant carry PrimitiveType; ScalarBits alone
  does not add an enum definition.
* [`Binder.ConstEval.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs)'s
  ConstEvalScalar admits only fixed primitive scalars. Required evaluation,
  local values and constant caches consequently have no exact enum domain.
* [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)'s
  TryBindNativeConventionValue reaches TryBindCoreEnumMember, which decodes
  qualified member syntax against
  [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs)'s
  separate in-process descriptors. That is not ordinary E-typed evaluation.
* [`NativeCoreRegistrationTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs)
  exercise direct members/identity rejection and explicitly declined
  persistence; [`ConstEvalTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ConstEvalTests.cs)
  exercise primitive forwarding, retained runtime calls and failure/limit
  cases. Neither establishes nominal enum forwarding. The
  [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
  has no explicit Compile exclusions.

The [implementation summary](../implementation.md) identifies direct-member
support and incomplete E-typed forwarding. [CG-010](../implementation/conformance-gaps.md#cg-010)
separately records the incomplete ordinary value/resource floor. The current
private depth/work limits are implementation evidence, not replacements for
the accepted constant-execution contract.

## Alternatives

| Direction | Tradeoff and disposition |
|---|---|
| Narrow native typed-value carrier/evaluator, including a special interpreter for E-returning helpers | Could accept more expressions than a token recognizer, but duplicates type, copy, call, failure and resource semantics. Even a carrier tagged with E is insufficient if ordinary values cannot use it. **Not selected:** conflicts with the settled prohibition on native-specific evaluation/fallback. |
| Shared nominal enum value integration, initially admitting int32-backed checked enums | Extends the ordinary model once and lets native schemas consume its result. Requires coherent bound typing, verification and runtime copying, but no new enum operations, source production or wire encoding. **Recommended.** |
| Defer the dependent native feature until general source enum support is complete | Coherent if affected use remains explicitly unsupported, not direct-member-only “complete.” It unnecessarily couples value forwarding to parsing and broader enum operations; independent publication still needs its own codec decision. Not the recommended prerequisite. |

## Exact value and operation boundary

An admitted enum declaration supplies its selected definition identity,
underlying type, checked member definitions/values, access and actual
copy/validity/representation commitments. For this bounded admission U is
int32. Value identity is **(exact T, mathematical int32 value)**:

* The existing int32/ScalarBits payload can hold the value. A shared type
  reference or checked context-local type identifier must separately retain
  T and resolve its U. A monomorphic runtime value needs no extra runtime
  tag merely to carry a statically known T.
* Do not hard-code a primitive kind per native enum, use a member ordinal
  as T, or annotate only the eventual attribute argument. Constants, reads,
  variables, fields in already supported storage, callable signatures and
  conditional results retain T throughout their admitted bound paths.
* Type aliases normalize to T; they neither create another enum nor convert
  U to T. Constant bindings and ordinary Copy values retain T/value and
  their applicable dependencies. Equal-valued members of one ordinary enum
  denote one value; their member declarations still have their own lookup
  identities. No member-origin provenance tag follows every copy.
* A conditional has a bool condition and two ordinarily compatible results
  of the same normalized enum type. It evaluates its condition once and
  only its selected result. Equal payloads do not make E, another enum or
  int32 compatible; an unselected ill-typed arm still rejects.

The added enum-value paths are member-constant reads, already formed typed
constant reads, value copies, initialized locals and permitted assignment,
same-type conditional selection, parameter passing and results/returns.
Use these paths both in eligible ConstEval bodies and ordinary runtime
functions. Existing initialization, mutability, source order, call mapping,
scopes, replacement and result-publication rules apply unchanged.

This does **not** grant enum arithmetic, ordering, source equality, bitwise
or Flags operations, increment, numeric promotion, literal construction,
casts, U-to-T construction or representation conversion. Such an operation
requires its own already admitted source/operation contract and, for required
evaluation, eligible body/intrinsic. Canonical compiler value comparison is
not an authored equality operation. An existing independently admitted
operation is not revoked; an unavailable operation cannot be implemented by
silently invoking U's primitive operator. Bool and primitive control/work
inside a forwarding body retain their ordinary operation contracts.

General enum validity includes every in-range underlying value, not just
members. This proposal selects no new syntax to manufacture unnamed values:
tests can supply already checked values through the admitted declaration/
value producer or an independently admitted formation operation. Out-of-range
formation fails before forwarding. E's final native schema rejects values
outside its registered mapping, including zero, without making those values
globally invalid E values. The registered E declaration's exact member-set
restriction does not prohibit duplicate-valued names on unrelated enums.

Runtime storage/moves may use U's existing representation **after** exact
typing and representation checks. Required constant results still need their
actual frozen validity and selected target value-encoding support; emitted
results need the accepted per-target Blittable commitment. Neither Copy,
the host handle nor this proposal synthesizes those commitments. There is
no automatic native ABI admission, marshalling or unsafe authorization.

The following is a **proposal-dependent source fragment**, not current
executable support. It assumes the selected Core/schema and representation
prerequisites above; it adds no enum constructor or operator:

```terrum
internal alias ABI = Terrum.Interop.NativeCallingConvention;

[ConstEval]
internal ABI Forward(ABI value, bool choose)
{
    ABI copy = value;
    return choose ? copy : ABI.Stdcall;
}

[Terrum.Interop.NativeCallable(CallConv: Forward(ABI.Cdecl, true))]
public int32 Probe() { return 0; }
```

The result is exact E with underlying value 2 and therefore Cdecl. A runtime
call to Forward remains an ordinary runtime call, not definition-time
constant work merely because its declaration is ConstEval.

## Evaluation and diagnostics

Bind schema arguments and expressions through ordinary lookup and exact
typed constant work, then check exact E and its registered mapping once.
The same applies independently to the two resolution-enum domains. Raw
integers, strings, wrappers and other enums reject, even with matching bits;
no contextual implicit conversion is added.

Keep the accepted evaluator's reached-path eligibility, argument/default
evaluation, pre/postconditions, cleanup and failure ordering. A helper that
would return E.Cdecl after a reached failure does not yield a convention.
Unselected, structurally valid runtime-only work follows the ordinary
executed-path rule, including the enclosing pure-form requirements; malformed
source/types/operations do not become valid by being unselected.

Reading an already frozen constant does not replay its original construction.
Executing another call, copying values and traversing conditionals still incur
their ordinary semantic work, frames, live storage and encoding charges.
Use the selected ordinary envelope and its boundary/failure priorities,
including on cache hits. Native attributes receive no free work, separate
fuel, syntax-only shortcut or runtime fallback.

Keep lookup/type errors, unavailable enum/operation/encoding support,
required-evaluation failure or exhaustion, malformed persisted input and
unsupported target lowering distinguishable. Diagnose the affected source
argument/use or refuse affected artifact consumption; no diagnostic number
is assigned here. Cycles, missing bodies/dependencies, runtime-only values
and failed checks cannot become Unmanaged, zero, a successful partial result,
another provider or a retry through direct-member recognition.

## Modules, verification and persistence

Carry the enum definition and exact value through shared traversal/rewriting,
constant substitution, folding, signature interning and verification.
Enum-aware caches must distinguish complete T and the applicable binding,
target/rule and evaluation context, not just U/bits or qualified name.
Source/bound verification checks exact domains at copies/calls/joins,
initialization and valid underlying payloads; a rewrite cannot erase T to
make an otherwise invalid assignment pass.

Retain every applicable original binding expectation: exact enum origin,
underlying type, relied-on members/values, representation, callable forms
and evaluation dependencies. Compare each expectation on replacement, not
the replacement's own description or one merged weakest expectation.
Already frozen results stay frozen; a replacement does not replay them.
Further required evaluation needs its actual retained eligible program and
dependencies, not a runtime stub. No equality of unrelated private bodies
or new constant-result promise is introduced.

Keep the following consumption boundaries separate:

| Boundary | Obligation and support gate |
|---|---|
| Fully normalized native contract | Consume its existing effective convention/resolution facts under the independently selected standard/private route. Do not reconstruct E or acquire a new Core registration merely to read those facts. Normalization alone does not erase any separately retained raw-E signature/value or evaluation obligation. |
| Raw enum declaration/member/schema, E-typed frozen value, or E-bearing program/signature | Needs a complete admitted declaration/value/program representation, exact selected bindings and capability closure. The in-process Core descriptor is not that representation. |
| Standard TRRM | [FrozenValueNode kind 1](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) already includes scalar/enum values with a type reference. It does not complete enum declaration/member/schema publication: [TypeDef flags](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) select struct/class/interface, and the current [scalar materializer](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) admits primitive type codes. **The exact enum publication route remains a separate gap**, not permission to encode E as int32 or a FieldDef. |
| Private semantic/native persistence | The [convention](native-calling-convention-private-persistence.md) and [resolution](native-import-resolution-private-persistence.md) candidates transport normalized native facts, not a general enum directory/value codec. **An exact raw-enum persistence route remains separately unassigned here.** Decline affected use rather than serialize process handles, add a token or relabel primitive payloads. |

Producers publish schema-valid, truthful facts. Consumers perform the
[structural, support and binding checks](../specs/representation/reader-trust.md)
needed before affected use, without a new obligation to re-prove producer
body semantics. Transformations preserve actual enum/type/value references,
Module ownership and dependencies, rebuilding affected maps, ranges, graphs
and optional source references under their selected schemas; row renumbering
or equal bytes cannot merge enum identities.

No new wire record, tag, ID, Feature, private revision, compatibility alias
or alternate decoder is selected. [Persistence taxonomy](../specs/representation/persistence.md)
and [exact draft agreement](../specs/representation/format-versioning.md)
remain unchanged. Existing integer payload reuse is an internal/value-layout
choice, not a claim that either serialized enum route is complete.

## Required conformance cases

These are future acceptance cases, not tests run by this documentation task.
Use public compiler APIs, explicit checked declarations and unrelated enums;
distinguish source/value support, persistence, emission and execution.

| Family | Required result |
|---|---|
| Positive forwarding | Each E member directly and through type-alias chains, accessible constant-binding chains, local copies, nested eligible ConstEval parameters/results and both selected conditional arms gives the same exact E/value and native mapping. Exercise I, C and P, plus each separate resolution-enum argument. |
| Generality/runtime | An unrelated int32 enum uses the same ordinary forwarding paths, including a runtime invocation left unfrozen. It still cannot supply E. No native role/name or member spelling is required to use the carrier. |
| Nominal negatives | Reject integer 2, same-shaped enum/wrapper, another Module's same-named E, mixed-domain conditional arms and wrong-domain call arguments. Aliases preserve identity but cannot repair inaccessible, ambiguous or cyclic bindings. |
| Value boundaries | Preserve ordinary E values 0, -1, 5 and int32 minimum/maximum through copies/results, then reject them at the native mapping. Reject out-of-range formation/successor overflow; permit ordinary duplicate-valued members while rejecting the registered Core shape's forbidden aliases. |
| Operation boundary | No enum arithmetic, equality, flags, cast or integer construction becomes valid solely because U has that operation. Independently admitted operations retain their actual eligibility and failure checks. |
| Evaluation/failure | Reached overflow/division/requirement failure, missing or unmarked bodies, nonconstant inputs and constant cycles fail required work. Do not select Cdecl by inspecting an eventual return or run native code. Exercise ordinary selected/unselected-path distinctions and malformed unselected arms. |
| Resource boundaries | Exact/one-over selected work, frame, live-storage and result-size bounds behave as ordinary required work; copies, repeated calls and cached execution retain charges. Frozen reads do not replay construction. |
| Verification/rewrite | Preserve E through shared rewrites and interning. Refuse mismatched enum references, invalid underlying payloads or unsupported required forms; equal U/bits must not merge E and another enum. |
| Separate compilation | Without an admitted raw-enum codec, decline raw-E values/signatures/programs even if primitive bits or a kind-1 node exist. Fully normalized native contracts do not require source attributes or otherwise unused Core registration. |
| Replacement/remapping | Reordered physical rows preserve original enum origins and every retained expectation. A changed base/member mapping, schema domain or lost ConstEval form cannot silently satisfy it; replacing a helper does not recompute a previously frozen value. |
| Cross-feature | Preserve independently admitted constant-generic exact-domain forwarding without enlarging its catalog. NativeOutput, unsafe/backing obligations and target support remain unchanged; a successfully evaluated Vectorcall argument does not supply its unsupported lowering. |

## Integration boundary and remaining gaps

This selects the value-integration direction, not an implementation roadmap.
No accepted enum rule or production needs replacement. The source-schema and
Core-registration candidates remain the owners of the unapproved native
surface; this document supplies their shared nominal-value prerequisite.
Ordinary type/evaluation/target owners above continue to govern semantics.

There is no remaining choice between a native-specific evaluator and ordinary
forwarding in this recommendation. Complete checked declaration delivery,
actual selected representation/value support, shared binder/verifier/runtime
implementation and qualification remain prerequisites. Production Core
provider selections remain explicit profile inputs, not guessed defaults.
Source-authored EnumDecl support and broader enum operations are independent
support work. Exact standard and private raw-enum declaration/value/program
publication remain genuine separate representation gaps; this proposal does
not decide their codecs or enable source-free enum consumption.
