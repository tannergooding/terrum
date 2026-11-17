# Interface requirement-owned ordinary parameter defaults

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user-authorized
> missing-contract workflow, pending later review.** This document approves
> no syntax, schema, API or allocation and changes no accepted specification,
> registry or implementation. Provisional direction is not formal adoption.

## Decision and actual gap

**Preserve the general accepted source contract.** Ordinary parameters,
including generic-method inputs and indexer indices, can declare checked,
publishable compile-time Copy defaults. An omitted argument uses the statically selected
declaration's default. For an interface call that owner is the requirement,
not its selected implementation.

**Provisional source direction:** permit an ordinary default to depend on
in-scope generic formals, keeping it as a checked declaration-scoped term
until the call's generic inputs allow required constant formation. The
accepted compile-time-value rule does not explicitly settle whether such a
default must already be closed at declaration binding.

Recommend retaining an optional **typed default-value fact on each requirement
parameter**, and extending the label proposal's complete MethodDef-owned
parameter vector by one nullable ConstantSource reference under the exact
successors below. Reuse that field for representable ordinary defaults on
L1's already included exported/expected generic MethodDefs rather than reserve
it zero there. This is an explicit shared-carrier consequence, not another
source rule or an expansion to new publication owners. Initially admit only
the parents' existing SF1 **closed** scalar values.

There are two independent gaps: **source implementation is partial**—a
non-generic scalar direct/interface-call path is in progress and unverified,
while generic method defaults, aggregates and several accessor routes remain
open; **Standard interchange** has no declaration-owned ordinary-default
carrier even once source binding is complete. Interface support is one slice
of the former, not its entire scope. This proposal focuses on requirement
ownership and the bounded shared interchange field; it does not complete
general default implementation or select an open dependent-constant codec.

The [specification index](../specs/README.md) leads to these owners and
implementation boundaries:

| Evidence | Established contract or actual loss |
|---|---|
| [Grammar §3.1/§3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [calls](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms) | ParameterBinding permits `= ConstantExpression` for ordinary parameters generally. InterfaceFunction reuses FunctionDefinition/ParameterList; InterfaceIndexer uses IndexParameters, also composed of Parameter. Thus index defaults are accepted too, not a Callable-only facility. Defaults include eligible nominal aggregates; required/defaulted positions may interleave. Generic-dependent timing is the separate provisional direction above. |
| [Witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity), [WIT-20](../specs/conformance/source-interface-witnesses.md) | The selected interface owns caller-visible labels/defaults; implementation differences do not rewrite compiled arguments. |
| **Source implementation:** [`Parser.ParseParameter`](../../sources/Terrum.CodeAnalysis/Syntax/Parser.cs), [`BoundInterfaceParameter`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder.Parameter` and `BindArguments`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | The parser ends a parameter at its identifier without consuming an initializer. Both the interface parameter record and ordinary Parameter lack a default-value/term fact. BindArguments requires the full supplied count and diagnoses missing parameters instead of filling defaults. These losses/rejections precede and are independent of serialization. BoundInterfaceFormal/GenericParam defaults are different facts. |
| **Interface source view:** [`Binder.Interfaces`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs), `BindInterfaceParameter`, witness matching, invocation and proof stubs | BindInterfaceParameter retains type/mode/name/attributes only. Matching uses substituted types/modes and supported contracts, not ordinary defaults. Invocation requires exact written argument count and overlays requirement labels on the target signature; proof stubs have no default carrier. Adding a Standard field alone fixes none of this. |
| **Indexer path:** that binder, [`BoundInterfaceContract`](../../sources/Terrum.CodeAnalysis/Semantics/BoundInterfaceContract.cs), active `BindsCallablePropertyIndexerAndAssociatedRequirements` in the source fixture below | ParseIndexParameters calls the same ParseParameter, so it has the same initializer gap. BindInterfaceMembers maps IndexerDeclaration to Indexer and binds IndexerParameterList through BindInterfaceParameter. Requirement enumeration retains each indexer accessor's AccessorOrdinal, but TryBindInterfaceWitness supports only Callable/plain Property getters and rejects Indexer; interface invocation likewise has no indexer branch. The parser's IndexExpression currently accepts one IndexArgument although the accepted Postfix production admits comma-separated indices. The active case checks declarations without defaults, not indexer witness/default calls. |
| [G1/C1 labels](standard-trrm-g1-interface-parameter-labels.md#record-inputs-and-closure), current [G1 writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs)/[reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs) | The working-tree G1L1 path now writes/reads requirement labels, unlike the label proposal's older `argN` support observation. Its 12-byte label row has no default. Standard [Param](../specs/il-metadata.md#section-kinds-and-row-layouts) is still structural and 16 bytes, with neither label nor default. |
| Active [source tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/InterfaceDeclarationBindingTests.cs), [G1 tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericMethodSourceWriterTests.cs), [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj) | The project excludes neither fixture. `BindsInterfaceWitnessNamedArgumentsUsingRequirementLabels`, `BindsGenericCallableWitnessByFormalOrdinal`, inherited-identity and default-Body cases exercise adjacent mechanisms; `BindsExpectedG1L1InterfaceProviderAndRetainsExpectedLabels` concerns labels. None is ordinary omitted-parameter-default evidence. These are inspected assertions, not newly executed qualification. |

## Source baseline and binding rules

The accepted baseline makes defaults checked, publishable compile-time Copy
values selected at caller source compilation; interface calls use the selected
requirement's view. The generic-dependent timing rule in item 4 below is the
unapproved candidate direction, not a normative interpretation.

1. **One declaration view.** Resolve the interface/member and its access first.
   The ordinary default belongs to that requirement declaration and parameter
   ordinal. An implementation may require that argument, publish a different
   default, or use another label and still satisfy this dimension of
   conformance. All logical positions/types/modes and the other witness
   contracts must still satisfy the requirement. Defaults do not distinguish
   overload signatures or create a witness.
2. **Inheritance is identity-based.** Use the actual closed declaring-interface
   instance plus member identity under the
   [inherited-requirement rules](../specs/language/interface-witnesses.md#inherited-requirements-and-defaults).
   A diamond reaching that same identity shares its parameter view; unrelated
   requirements or different closed instances remain distinct. A selected
   implementation/replacement default **body** does not replace the original
   requirement's argument defaults. A genuinely new requirement has its own
   view and ordinary lookup/ambiguity rules. No default-value comparison,
   inheritance-list order or import order merges requirements.
3. **Map supplied arguments, then fill omissions.** Explicit arguments,
   including values equal to a default, bind normally and never consult or
   evaluate the default. Every unsupplied eligible position uses that view's
   default; an unsupplied required position rejects. Use requirement labels
   and declaration ordinals, not implementation names or the next unfilled
   slot. Preserve in-position labels followed by positionals, reject positionals
   after an out-of-position label, and reject duplicate/unknown labels.
   `params` expansion and contextual output lifting keep their separate rules;
   neither is an ordinary default.
4. **Provisional dependent-default direction; substitute without rebinding.**
   If a default depends on in-scope generic formals, bind the term in its declaration's
   lexical/type/constant environment. Substitute declaring-owner arguments
   first, then corresponding method formals by kind/ordinal and matching arity.
   Preserve complete qualification, nominal identity, constant domain/value
   and dependencies, including through inherited and qualified-self views.
   A dependent default remains a checked term until its generic inputs permit
   required compile-time formation; it is not a declaration-time SF1 value
   or a runtime expression merely because its result type is scalar.
   Do not look up the expression in the caller, use its value to guess generic
   arguments, transplant implementation defaults, or certify an invalid open
   declaration from one successful specialization. Ordinary defaults are
   distinct from GenericParam defaults and do not change generic inference.
5. **No runtime recipe.** Apply the existing
   [constant-formation rules](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain)
   and required constant failure/resource rules. A failed formation diagnoses;
   it does not defer a failing expression to invocation. A completed frozen
   value is copied/materialized, not reconstructed by replaying a parser,
   constructor or helper. Capture the receiver once, then evaluate written
   arguments once left-to-right in source order; named mapping does not reorder
   them. Stage selected constants into the omitted logical positions before
   invocation checks/preparation. Preserve ordinary every-exit cleanup,
   failure, initialization and output publication; an omitted argument
   manufactures neither vacant storage nor an initialization guarantee.
6. **Access and effects remain real obligations.** Normal declaration access
   governs formation; importing a frozen result grants no access to private
   formation helpers. Retain any required type, validity, layout, target or
   value dependency; freezing is not permission to leak an unpublishable
   identity or erase a retained contract. A private formation body need not
   survive solely to replay an already published value. Calls still honor the
   selected requirement's caller-unsafety, receiver access, effects, contracts,
   backing and cleanup obligations. Neither Copy nor a scalar default makes
   the whole call Pure, ConstEval, nonfailing or caller-safe.

| Generic-dependent source rule | Assessment |
|---|---|
| Require every ordinary default to be a closed value at declaration binding | Simpler to freeze and publish, but the accepted `ConstantExpression` grammar and compile-time Copy rule do not explicitly impose this restriction. It would reject otherwise valid generic declarations without an accepted prohibition. |
| **Retain a typed declaration-scoped term until generic inputs are fixed** | **Recommended provisional direction.** It preserves the general parameter-default contract for generic declarations, but requires exact formal substitution, required-constant failure/resource handling and a separate source-free term codec; it cannot be confused with GenericParam defaults. |

Reject defaults that are not valid publishable constants of the complete
parameter type, including failed/range-invalid formation, non-Copy or owning
values, object/resource/address identity and escaping evaluator backing.
Reference/implicit-reference and initialization loans have no default under
this rule; `params` has no initializer production. An invalid declared default
is not excused by an explicit argument at every observed call. Accepted
aggregate defaults remain accepted source contracts, not silently reduced to
the scalar interchange subset selected here.

[RequireException recipes](../specs/language/requirement-failures.md#3-callable-owned-defaults-and-the-core-exception-class)
remain owned by their originating source checks. Ordinary argument defaults
select neither a failure class nor a constructor/default-body replacement.

## Candidate Standard carrier and exact selection

### Complete parameter surface

In the successors below, intentionally replace **only** the L1 candidate
section `0066=MethodParameterLabel`, stride 12, by candidate
**`0066=MethodParameterSurface`, stride 16**, required policy `0000`:

```text
u32 ownerMethodDef
u32 ordinal
u32 name
u32 defaultValue
```

Offsets are `0,4,8,12`, little-endian. The first three fields retain L1's exact
MethodDef ownership, full-width explicit logical Param ordinal and decoded
String rules. `defaultValue=0` means no ordinary default for that admitted
declaration parameter; otherwise it is a required typed row reference in
ConstantSource section `0030`, not a DefId, Blob, TypeSpecRef or Body-map index.
References have zero high byte and rows `1..00FF_FFFF`; ordinals remain `u32`.
Check `16*recordCount`, all extents/counts and the inherited row capacities.

Retain L1's complete owner set and exactly one row per logical Param,
including unit positions, unused requirements and bodyless expectations;
zero-parameter owners have no rows. Pairs `(ownerMethodDef, ordinal)` are
unique, vectors total, and the section absent exactly when no rows exist.
Missing/truncated coverage is invalid, not “all missing parameters required.”
The owner supplies the Module and declaration/formal context. Shared MethodSigs
do not merge surfaces. Export aliases share one declaration vector; distinct
expected views keep their own vectors.

A nonzero default is admitted on an eligible ordinary value parameter of
either an admitted InterfaceMethod requirement **or an existing L1
exported/expected generic MethodDef**. This includes mapped index parameters
on an independently admitted indexer accessor requirement MethodDef, subject
to the owner gate below. A direct generic declaration uses its own selected
declaration's default, not an interface implementation's view. Reserving zero
there would reproduce the same ordinary-default loss despite an identical
owner/ordinal/value carrier; being generic does not make every default
generic-dependent. Synthesized accessor value inputs and other ineligible
positions require zero. Unrepresentable defaults reject the retained
declaration, never become zero.

This reuse does not enlarge L1's owner set: ordinary nongeneric exports,
constructors/I/P, callable values and private helpers gain no publication
surface. Interface requirement defaults remain the design focus; general
source support and all other ordinary-default publication routes remain
separate implementation work. Generic **formal** defaults acquire no field
or rule here.

The initial nonzero payload is exactly
**ConstantSource tag 1 → SF1 FrozenValueNode kind 1**, with the complete
[24-scalar codec](scalar-frozen-value-registration.md#rows-and-blob-payloads),
including unit's zero Blob reference and exact floating/BID bits.
Its normalized result type must equal the parameter type under the recorded
owner-then-method substitution. No scalar-width coercion or host equality
substitutes for that check. A generic-dependent type/default not representable
by this closed value path rejects the retained declaration, even if current
calls are explicit. Native-width values, nominal/aggregate constants, open
constant terms, ConstantSource tags 2/3 and CG1 tags 4/5 are **not enabled**
as defaults here. Their missing codecs/compositions remain gates, not zero
defaults or a new symbolic-expression interpretation.

For example, an ordinary `int32` parameter defaulted to the closed constant 7
is representable even when its method has generic formals. A default naming
an in-scope constant formal N, or computing N + 1, is not a frozen value while
N remains open. This distinction applies equally to interface requirements
and exported generic methods; accepted source validity does not imply D1
publication support.

### Indexer parameters and accessor-owner gate

The accepted [indexer contract](../specs/grammar-spec.md#510-properties-and-accessor-local-effects)
is a parameterized accessor contract, not an ordinary Callable declaration.
Its written index defaults belong to the selected indexer requirement. When
an accessor requirement MethodDef is independently admitted, D1 carries each
representable index default at that accessor's **mapped Param ordinal**.
Project the same declaration-owned index vector onto every admitted
getter/setter/init accessor that receives those indices, preserving the
parent's exact index-to-Param correspondence and checking consistency across
those views. The receiver is not a Param; the setter/init assigned `value`
input remains separately mapped, required and default-zero. Neither an
implementation accessor nor its parameter spelling replaces this index view.

**Separate owner/schema gate:** the selected schema must retain the exact
declaring-interface/indexer/accessor identities, accessor role, MethodDef
correspondence and index-parameter/assigned-value ordinal mapping, plus its
access, contracts and any initialization phase. G1L1R1D1 retains G1's
Callable-only interface gate and therefore still refuses indexers. C1-family
D1 uses admitted accessor owners but does not complete C1's
[missing member/accessor codecs](standard-trrm-exception-class-witness-binding.md#separate-gaps-and-acceptance).
The current getter-only PropertyDef link does not supply the complete
indexer/setter/init mapping; a magic accessor name or a relabeled Callable
does not supply it either. The separate unapproved
[IO1 proposal](standard-trrm-c1-indexer-parameter-default-ownership.md)
recommends explicit nullable setter/init MethodDef links and a fixed
index-parameter projection on an exact C1 PropertyDef successor. Until that
owner contract is selected, refuse the retained indexer, not its defaults
alone. D1 assigns no accessor owner, role ID or mapping encoding.

Default eligibility also adds no bracket syntax:
[PostfixPart/IndexArgument](../specs/grammar-spec.md#34-expressions-and-precedence)
requires a nonempty index list and has no named-argument label form. Only
omissions expressible by that grammar use defaults; callable named-argument
rules do not invent empty `[]` or named index arguments.

### Explicit dependent-default gate

The remaining Standard contract is a **declaration-scoped open constant
source/program for an ordinary parameter default**, with exact owner/method
formal references, substituted result type, dependency/evidence closure,
required formation/failure/resource/target rules and source-free
remapping/re-emission. The companion
[generic-dependent-default proposal](standard-trrm-generic-dependent-parameter-defaults.md)
recommends that contract provisionally. Its using context is the selected
MethodDef and declaring owner. An ordinary Param ordinal is not a GenericParam ordinal:
method generic arguments are established before ordinary defaults are
applied, so their scope cannot be truncated to a same-numbered generic prefix.
Runtime ordinary arguments do not become constant-formal inputs.

[CG1](standard-trrm-constant-generic-methods.md#constantsource-and-constant-positions)
proposes formal-source tag 4 and expression-template tag 5, but is a separate
whole agreement. Its
[earlier-formal default rules](standard-trrm-constant-generic-methods.md#context-defaults-and-source-free-binding)
concern generic-formal defaults, not this ordinary-parameter scope.
A later composition must explicitly admit the ordinary-default use/context
and reconcile its complete schema, templates, value codecs and required
evaluation support; this proposal selects none of that. G1's GenericCall
argument-kind-3 forwarding is likewise not a ConstantSource carrier.
Until that contract is selected, D1 refuses retained open dependent defaults,
even on a scalar parameter or a currently uncalled/explicitly supplied view.
This is a named interchange gate, not undecided source syntax or a prohibition
on generic-dependent ordinary defaults.

### Allocation and carrier alternatives

This adds no section number, Feature, Contract kind, DefId, flag, opcode,
prefix/escape, operand tag or Body-map namespace. It reuses L1's **unapproved**
0066 candidate in a different exact schema; it does not reserve a new ID or
reinterpret the 12-byte L1 table. Feature `26/1` keeps its label-only scope and
retained-owner triggers; defaults are a mandatory schema dimension, not an
extension inferred from 26. Every using Module retaining a nonzero SF1 default
also requires `11/1`, including declaration-only/uncalled views. Absence alone
does not trigger 11. Preserve independent Feature 23 and every parent
requirement; an owner type formal alone does not trigger 23.

The material alternative is a separate sparse MethodDef/ordinal/ConstantSource
table (12 bytes per default). It is sound with exact-schema absence rules,
but needs another section assignment and a second source-view association.
**Prefer the four-byte extension of the already complete vector**, accepting
four bytes per label row rather than claiming a universal size win. Putting
the value on shared Param would instead require declaration-specific signature
splitting. Runtime default thunks would change the settled source contract.

### Whole agreements, not mix-ins

`D1` is document shorthand only. Select the corresponding complete
[receiver-bearing parent](standard-trrm-method-receiver-access.md#exact-profile-consequence)
plus exactly the surface delta above:

| Unapproved successor | Exact candidate identity |
|---|---|
| G1L1R1D1 | `trrm/1-draft-generic-method-calls-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1` |
| C1L1R1D1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1` |
| C1M1L1R1D1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1` |
| Construction-1L1R1D1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1` |
| Construction-2L1R1D1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1` |

Keep `(1,0)`, edition 1, Body encoding 01 and all other parent rows/catalogs.
G1's bounded interface path admits this closed ordinary-default exception to
its previous gate; the same field serves its existing exported generic
MethodDefs as specified above. Interface bases, member generics, default
bodies and other exclusions remain. C1's requirement set includes its
independently admitted inherited, bodyful and method-generic requirements;
the G1 exclusions are not its decoder.
Preserve C1's reconciled predicate catalog, each class/construction stage,
original expected/provider contexts and separate phase-1/phase-2 maps.

For this route, unchanged G1 mapping versions 1 and 3 pair explicitly with
G1L1R1D1, including empty inputs; no default override field is added. Each C1
stage selects its matching complete D1 literal, and complete failure
construction requires Construction-2L1R1D1 and independent runtime admission.
Preflight root, all supplied providers and captured Clause/Origin/Plan inputs
under that stage's one exact agreement before schema-dependent decoding.
No sibling/parent or G1 sub-decoder supplies missing C1 closure.

Original G1, G1L1, the L1/R1 C1 families, CG1/version 2 and
[private ParameterSurface](source-interface-witness-private-persistence.md#requirements-defaults-and-callable-instances)
remain distinct. Generic formal defaults keep GenericParam's existing fields
and rules. No CG1 symbolic constant or private tag joins by reference.
Under [format versioning](../specs/representation/format-versioning.md),
equal version bytes, zero default words, Features or plausible strides do not
select a decoder. No compatibility fallback/alias, old-reader obligation or
schema-key/version-byte patch is a conversion.

## Preservation, verification and lowering

- **Source implementation:** shared parsing, ordinary Parameter/signature
  binding and BindArguments need the general accepted default behavior;
  interface parameters, indexer-to-accessor projections and proof stubs must
  preserve the requirement's own typed value or dependent term/absence.
  Carry those facts through traversal,
  substitution, verification and adapters, not only syntax tokens, HasBody,
  declaration keys or an implementation signature. This source work remains
  necessary even without Standard publication.
- **Standard producer:** capture the checked declaration/vector using the
  existing source anchors and keys; publish
  complete representable defaults/dependencies for every admitted requirement
  and existing generic-export view, including uncalled views. Refuse
  unsupported retained defaults atomically, never encode them as absent.
- **Consumer/verifier:** before declaration exposure or affected use, validate
  the exact schema/support, Module closure, row framing, owner/ordinal/label
  coverage, eligible mode, typed source/node/Blob references, scalar validity
  and complete substituted type. Source checking proves authored formation;
  consumers perform required structural/binding checks, not semantic re-proof
  of producer-published evaluation or arbitrary bodies
  ([trust boundary](../specs/representation/reader-trust.md)).
- **Expected/provider binding and replacement:** a fresh import captures the
  selected provider declaration's surface. A retained expectation keeps its
  original labels, default presence and typed values when attaching a provider Body,
  rebinding a witness or replacing a provider. Parameter-default differences
  alone are not a witness-signature/binary-satisfaction mismatch: old calls
  pass their already selected values. Other caller-domain, promised behavior
  and dependency obligations must still hold. Fresh compilation sees the new
  provider's view; default removal can make a new omission invalid. A
  source-rebinding-equivalence claim or discharge of a source-view proxy
  requires equal labels/default presence and exact typed values, plus all
  other contracts. Preserve unequal proxies/Imports rather than overwriting
  either vector or coalescing by provider/key/MethodSig
  ([replacement owner](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts)).
- **Source-free transformation:** remap decoded MethodDef, String and
  ConstantSource references, then the source/node TypeSpec/Blob graph and
  every required Import/Export, owner/formal, witness, contract and dependency
  edge. Preserve scalar payload bits rather than treating matching bytes as
  references. Clone context-dependent rows/views as needed; rebuild affected
  ranges, indexes, directories, Body maps and offsets, SourceMap locations,
  manifests and C1 phase-map contexts. Canonical surface order is owner row
  then ordinal; ordinary readers accept reordered valid rows, not duplicates
  or incomplete vectors. Trim only genuinely unretained views. A nameless or
  default-less parent cannot reconstruct a lost default without source;
  re-emission must explicitly select both schemas and preserve all meaning.
- **Call lowering:** complete the ordinary argument plan from the selected
  requirement view before choosing/adapting the witness target. Preserve
  source-order staging and ordinal mapping, then pass every logical argument
  explicitly, with existing ConstantMap loads/materialization where admitted.
  Bindings inside an open generic caller retain that caller's chosen default;
  closing its witness does not refresh it. Lowering adds no default sentinel,
  hidden parameter, dynamic interface ABI, witness search, initialization
  output or failure-construction policy. Unsupported value/target/cleanup
  paths refuse rather than fall back to the implementation.

## Focused acceptance and integration

These are required cases, **not claimed passes**:

| Cases | Required outcome |
|---|---|
| Requirement default 7; implementation default 9 or no default, with different labels; omitted versus explicit interface and concrete calls | Interface omission uses 7; explicit values remain unchanged; concrete calls use their separately selected view. Conformance does not require equal defaults. |
| Required/defaulted parameters interleaved; reordered names and side-effecting supplied arguments; unknown/duplicate names and illegal positional mixtures | Correct requirement ordinals and once-only receiver/source-order evaluation; required omissions reject. No implementation-label fallback. |
| Same-identity diamond; unrelated same-shaped requirements with different defaults; explicit implementation and inherited/replacement default body | Preserve each declaring identity/surface and existing ambiguity rules. Body selection never selects an argument default. |
| Inherited interface indexer with two indices, the second defaulted to 7, and differently named getter/setter implementations; one supplied index and both comma-separated indices; provider replacement and source-free re-emission | When the accessor owner/schema is admitted, preserve the requirement's default on each mapped index Param and the original expected view; keep receiver/accessor authority and required setter value separate. G1 or a C1 route lacking the exact accessor mapping refuses the indexer, not a Callable-shaped substitute. No empty/named bracket form is introduced. |
| Generic owner then renamed method-formal substitution; open role-2 call later closed with another implementation; qualified-self views where independently admitted | Retain exact domains, caller-chosen values and formal correspondence; do not infer from a default or refresh during specialization. Unsupported generic/symbolic default terms refuse. |
| Existing G1 generic export with a closed ordinary default; repeat with an ordinary default depending on an owner/method constant formal or its expression | Preserve the closed default through fresh import, expected/provider replacement and re-emission. Refuse the open dependent default under D1 rather than freeze one specialization, write zero or reuse a generic-formal-default prefix rule. |
| Unit default versus no default; zero parameters; unused/bodyless expected views; full `u32` ordinal versus Body `u16` limits; all SF1 scalar endpoints, signed zeros/NaNs/BID containers | Preserve presence, logical arity, exact type/bits and per-Module 11/23/26 triggers. No narrowing, unit sentinel collision or host numeric equality. |
| Wrong owner/mode/type/source kind, duplicate/partial vector, bad reference/high byte/count/stride/reserved value, invalid bool/rune/width | Refuse before affected use/output; unchanged destination on producer failure. Explicit calls do not excuse malformed/invalid retained defaults. |
| Nonconstant/failing formation, reference/init/owning/object-identity defaults; valid aggregate/native-width/open constant defaults without a selected codec | Reject invalid source; diagnose unsupported interchange for otherwise valid accepted defaults, not absence or runtime evaluation. |
| Later supplied-argument failure with an earlier staged owner, unsafe/mutable requirement, init destination and declaration preconditions | Preserve cleanup, caller authority, checks and output state; defaults supply no extra authority or early publication. |
| Independent provider/consumer with source/debug removed; non-first provider, aliases, changed default or labels, then replacement | Original expected surfaces and compiled arguments survive; fresh views observe the replacement; incompatible other contracts still reject. |
| Pack/reindex/shared signatures, source-free re-emit and import again; repeat C1 with original phase contexts | Remap complete typed closures, retain unequal proxies and exact bits; no first-view-wins merging or byte patching. |
| Every exact successor versus its parent, sibling, CG1/private inputs, unused providers and empty artifacts | Explicit whole-schema agreement, no decoder guessing or lost parent gates. No declaration/emission-only result is execution evidence. |

On later integration, [metadata](../specs/il-metadata.md#section-kinds-and-row-layouts)
and the [machine registry](../specs/representation/trrm-1.registry.json) own the
successor's surface row; logical records own its requirement/value context;
manifest, reader-trust, canonicality and binding/replacement chapters own its
support, checking and preservation rules. The
[L1 proposal](standard-trrm-g1-interface-parameter-labels.md), R1,
[G1 method inputs](standard-trrm-source-generic-method-export-inputs.md),
[G1 interface inputs](standard-trrm-g1-interface-declaration-exports.md) and
C1 stage-selection proposals need the explicit successor pairings and shared
ordinary-default field applicability above. Any future CG1 composition needs
the separately stated dependent-default context, not a transitive link.
Source grammar/call/witness semantics need no replacement rule.

The [IO1 proposal](standard-trrm-c1-indexer-parameter-default-ownership.md)
separately describes the indexer/accessor owner gate. Its pending status is
distinct from this proposal's bounded D1 carrier and the general source
implementation gap. The compact gap index tracks each gate under
[CG-001/006/010/026 and approval closure](../implementation/conformance-gaps.md).

**Remaining decisions:** formal adoption of this bounded carrier/whole-schema
selection and the provisional generic-dependent source rule remain unapproved.
The accepted source syntax, requirement ownership and caller-selection rule are
settled; general source implementation remains open. Standard persistence for
open generic-dependent values, richer aggregate/native-width codecs, ordinary
declarations outside the L1 owner set, and complete C1/witness/runtime support
remain separate gates; see the linked generic-dependent-default proposal for
its candidate term carrier.
