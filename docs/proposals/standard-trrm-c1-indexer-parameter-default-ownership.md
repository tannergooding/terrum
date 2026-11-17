# Standard TRRM C1 indexer parameter-default ownership

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user-authorized
> missing-contract workflow, pending later review.** This document changes no
> accepted specification, registry, API or implementation. Provisional use is
> not formal adoption or a conformance claim. No numeric identity is allocated.

## Finding and recommendation

**The complete owner mapping is genuinely missing, but indexer defaults are
not missing source syntax.** Recommend **IO1**, a successor of the existing
PropertyDef record with explicit nullable setter/init MethodDef links. Reuse
the mandatory getter's signature as the structural index vector; project one
declaration-owned label/default vector onto the existing D1/GD1 accessor
surfaces. No independent IndexerDef, parameter table or arbitrary ordinal-map
language is needed for the accepted getter-required grammar.

The [specification index](../specs/README.md) leads to these owners. Code and
test references below are inspected evidence, not newly executed checks.

| Evidence | Established rule or actual boundary |
|---|---|
| [Grammar §3.1/§3.2](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | IndexParameters is a nonempty Parameter sequence, and ParameterBinding admits ordinary constant defaults. Every indexer has a getter and at most one setter **or** init accessor. Neither indexers nor their accessors declare method generic formals. |
| [Accessor contracts](../specs/grammar-spec.md#510-properties-and-accessor-local-effects), [declaration spaces](../specs/language/names-and-binding.md#namespace-and-member-declaration-spaces), [implicit names](../specs/language/local-declarations.md#implicit-names-and-generated-storage) | Indexers have designated operation identities, not ordinary members named `this`. Accessors have checked identities/effects. Indices are in scope in each accessor; implicit setter/init `value` is a distinct parameter and cannot collide with an index name. |
| [Interface ownership](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity), [binding](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | The selected requirement owns caller-visible labels/defaults. PropertyDef binding currently specifies its referenced getter; other accessor kinds require admitted schemas. |
| [Metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [registry](../specs/representation/trrm-1.registry.json) | PropertyDef `001E` is a 20-byte named-getter record. MethodSig owns structural Params, not declaration labels/defaults. InterfaceMethod links a MethodDef; WitnessMember selects an exact requirement/target MethodDef. None groups getter/setter/init or identifies the setter's assigned-value position. |
| [PropertyDef reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmPropertyDefTableReader.cs), [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmPropertyDefTableWriter.cs), [structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs), [definition resolver](../../sources/Terrum.CodeAnalysis/Modules/TrRmDefinitionResolver.cs) | The codec consumes exactly five words, checks getter owner/result and visibility, and has no setter/init association. The shared validator uses that codec for C1 too; PropertyDef resolution reports the getter's Body, not an all-accessor expectation. |
| [MethodDef](../../sources/Terrum.CodeAnalysis/Modules/TrRmMethodDefTableReader.cs), [MethodSig](../../sources/Terrum.CodeAnalysis/Modules/TrRmMethodSigTableReader.cs), [Param](../../sources/Terrum.CodeAnalysis/Modules/TrRmParamTableReader.cs), [InterfaceMethod](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceMethodTable.cs), [witness codecs](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceWitnessTables.cs) | Existing ownership, exclusive ranges, logical ordinals and MethodDef references are reusable. They do not encode source member kind or accessor role; matching signatures cannot recover those facts. |
| [Bound declarations](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [requirement identities](../../sources/Terrum.CodeAnalysis/Semantics/BoundInterfaceContract.cs), [interface binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) | Source facts already distinguish Indexer, a common parameter vector and Get/Set/Init accessors. Requirement identities retain declaring interface/member plus AccessorOrdinal. Direct/witness getter and setter calls now have in-progress, unverified source paths; init dispatch remains open. |
| [Parser](../../sources/Terrum.CodeAnalysis/Syntax/Parser.cs), [ordinary binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [C1 emitter](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [G1 writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs)/[reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs) | Parameter defaults now parse; direct non-generic scalar defaults and scalar BoundInterfaceParameter values are being bound/materialized, with regressions unverified. Aggregate/generic terms, interface setter suffix defaults, init binding, C1 accessor publication and the G1 Callable-only boundary remain open. These are implementation gaps, not completed owner codecs. |
| [Active interface fixture](../../tests/Terrum.CodeAnalysis.Tests/Semantics/InterfaceDeclarationBindingTests.cs), [PropertyDef fixture](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmPropertyDefTableReaderTests.cs), [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj) | Added source tests cover comma-separated direct/witness getter and setter calls, receiver authority and scalar default selection; all are unverified. Existing PropertyDef tests cover getter owner/result validation, not persisted index defaults or accessor ownership. Neither fixture is excluded by the project. |

[D1](standard-trrm-interface-parameter-defaults.md#indexer-parameters-and-accessor-owner-gate)
explicitly defers this association. [GD1](standard-trrm-generic-dependent-parameter-defaults.md)
adds a term context, not an indexer owner. [C1](standard-trrm-exception-class-witness-binding.md#separate-gaps-and-acceptance)
likewise leaves complete member/accessor codecs gated. This is neither a
contradiction of accepted default ownership nor merely an absent serializer.

## Alternatives

| Candidate | Disposition |
|---|---|
| Unchanged PropertyDef + getter MethodSig + D1/GD1 surface | Sufficient storage for a **getter-only** index vector once its indexer interpretation is specified. Insufficient for the requested complete contract: there is no setter/init role link, grouping or assigned-value correspondence. Neither InterfaceMethod ordinals nor names supply them. |
| **Extend PropertyDef by two explicit role links; fixed prefix projection** | **Recommended IO1.** Reuses existing definition/Import/Export, signature, Param and surface namespaces. The accepted required getter supplies the complete structural vector. Costs eight bytes per PropertyDef and duplicated surface facts, checked for consistency. |
| Independent IndexerDef/member-kind record, declaration-owned parameters and explicit accessor/ordinal maps | Coherent substantive redesign; avoids projecting labels/defaults and supports getter-less or independently shaped accessors. It requires new owner/reference schemas and revising D1's surface ownership, binding and transformations. Those additional capabilities are not required here; future grammar changes could justify it. |
| Generated accessor names, key suffixes, adjacency or Callable-shaped substitutes | Not an owner contract. Reject rather than infer roles, merge declarations or expose a new callable API. |

Unpublished assignments may be redesigned. Reuse here is justified by the
required getter and fixed parameter correspondence, not draft compatibility.

## Proposed PropertyDef successor

**IO1 intentionally replaces the 20-byte PropertyDef layout only in the exact
successors below.** Section `001E`, required policy `0000`, and DefId kind
`1E` retain their namespaces. The candidate row is seven little-endian words,
stride **28**, with no offset index or trailer:

```text
offset  0: u32 ownerTypeDef
offset  4: u32 name
offset  8: u32 typeSpec
offset 12: u32 getterMethodDef
offset 16: u32 flags
offset 20: u32 setterMethodDef       // zero when absent
offset 24: u32 initMethodDef         // zero when absent
```

The first five fields retain their namespaces: TypeDef row, String row,
complete TypeSpecRef, MethodDef row, visibility flags. The two added fields
are nullable **local MethodDef rows**, not DefIds, Imports, dispatch slots or
Body indexes. Nonzero rows have zero high byte and lie in `1..00FF_FFFF`
within their actual table. Check `28 * recordCount` and all range arithmetic.
PropertyDef flags gain no bit; no accessor-role numeric tag is introduced.

The new set/init links are admitted here **only for instance indexers**.
Existing zero-index getter-only properties retain their parent's contract
with both added words zero. General named-property setters/init, including
static initialization-group roles, are not selected by this focused route.

For each retained declaration P:

1. The getter is required. At most one of setter/init is nonzero. All linked
   MethodDefs are distinct, have P's owner TypeDef and logical Module, and
   belong to exactly this one declaration/role. Export aliases share P;
   they do not create more role links. Multiple requirements may still select
   the same implementation accessor through separate WitnessMembers.
2. Let **n** be the getter MethodSig's logical Param count and **T** P's complete
   value type. Under IO1, n=0 denotes a named property; n>0 denotes an indexer.
   For an indexer, `name` stores the grammar's decoded `this` spelling for
   presentation only. It creates no ordinary name entry. A named property
   spelled with escaped `this` remains a zero-index property, not an indexer.
   This is an explicit successor interpretation, not a heuristic for old rows.
3. Every accessor has an empty method-formal range: the accepted productions
   provide no accessor/indexer method-generic list. Declaring-TypeDef formals
   remain available in their complete original mixed order. Generic helpers
   used to represent defaults are separate declarations.
4. For n>0, the following correspondence is mandatory; no
   permutation/adaptation map is implicit:

   | Role | Result | Explicit logical Params |
   |---|---|---|
   | Getter | T | Index vector at ordinals `0..n-1` |
   | Setter | Unit `void` | Same index vector at `0..n-1`, then assigned `value` at n |
   | Init | Unit `void` | Same index vector at `0..n-1`, then assigned `value` at n |

   Compare complete normalized index types, modes and applicable Param flags
   in the same declaring-owner environment, not raw TypeSpec words. The
   assigned value is an ordinary by-value Param of exactly T, without implicit
   reference adaptation. It is required and never has an ordinary default.
   Getter/result and setter/init-value equality is complete type equality,
   not equal width or assignability.
5. Receiver presence is separate from Param: these indexer accessors are
   instance MethodDefs with the proper owner receiver. No receiver shifts j
   to j+1. Unit indices/value retain logical positions. Checked `n+1` and
   complete exclusive Param ranges must fit the selected table capacities;
   sharing a structural nongeneric MethodSig never shares declaration facts.
6. Preserve each accessor's independent access, effects, contracts, parameter
   annotations and dependencies. Common index annotations map index j to
   Param j on each receiving accessor; accessor-local targets remain local.
   Implicit `value` has identity `(P, setter-or-init, n)` and label `value`
   where a surface is required. Apply accepted name/generic-formal collision
   rules; do not rename a colliding index to manufacture a valid declaration.
7. Indexer get/set receiver access follows [R1](standard-trrm-method-receiver-access.md#exact-receiver-semantics)
   independently. For init, `receiverTypeSpec` is the normal outer-unqualified
   declaring nominal instance with every owner argument retained; an interface
   uses R1's abstract implementing-receiver position. It is the typed subject
   of P's exact unpublished instance initialization context, not a live-value
   borrow. The R1 writable-receiver bit is clear, **not an ordinary readonly
   authorization**. Classify that role before interpreting receiver access.
   The init link records the accepted phase requirement; it adds no executable
   phase carrier, constructor I/P role, backing guarantee or right to initialize
   a live object. Missing construction/evidence/lowering support still refuses
   affected use.

An admitted interface's nonprivate accessor requirements each have exactly
one InterfaceMethod entry for their linked MethodDef. Expand a declaration
in getter-then-setter-or-init order, omitting private helpers from requirement
enumeration. Preserve the existing direct declaration order. InterfaceMethod
ordinal is the flattened requirement order, **not** the source member ordinal,
AccessorOrdinal, index ordinal or dispatch slot. Source-free reconstruction
groups entries using P's explicit links and reconstructs the accessor array
in grammar order; it never classifies a method from its spelling.

The semantic indexer identity is its exact bound declaring-owner instance
and selected declaration P; an accessor additionally selects its role.
For interfaces this retains the actual declaring-interface instance, including
inherited identity. The corresponding requirement MethodDef remains the
existing WitnessMember reference. Same-identity diamonds share that
requirement; unrelated P identities or different closed owner instances do not.

## One declaration vector, projected D1/GD1 surfaces

Let **I(P)[j]** contain index j's checked name, complete parameter contract and
ordinary default value/term or absence. This vector belongs to P, **not to a
chosen getter Body or implementation**. Its structural spine is the getter's
Param range; its source-view facts are the consistent projected surfaces:

- For these index projections, D1/GD1's `(ownerMethodDef, ordinal)` is the
  storage/use coordinate of the declaration fact `(P, j)`, not an independently
  authored accessor default. Retain the selected expected P/owner instance
  and operation role. Other D1/GD1 publication owners keep their existing rules.
- Every accessor MethodDef independently eligible under D1 has its total
  16-byte `0066 MethodParameterSurface` vector. The first n rows project
  I(P) at unchanged ordinals. Setter/init additionally has its required
  `value` row at n, with `defaultValue=0`. There is no receiver surface row.
- Across one P's eligible accessors, names, default presence and normalized
  bound values/terms must agree at each index. D1 compares exact typed frozen
  values/bits; GD1 compares the retained bound term, source/helper identities
  and complete captured/formal mappings. Equivalent duplicate immutable rows
  need not have equal physical row numbers. Sample evaluations or host
  numeric equality do not establish agreement. Writers project the one
  checked fact; readers reject inconsistent copies rather than pick a winner.
- A source-free reader can recover I(P) from any eligible accessor prefix,
  checking all the others. A private getter need not become an InterfaceMethod
  or a new surface owner when another accessor supplies the requirement view.
  Missing eligible coverage is invalid, not absence of defaults. If no
  eligible surface exists, this route promises no independently callable
  default source view.
- D1's owner set does **not** expand. Concrete nongeneric targets, private
  helpers, constructors and callable values gain no default-publication
  surface merely by receiving an owner link. Retain their necessary role
  contracts, but refuse operations requiring an additional concrete-call
  source view that the parent cannot publish; do not silently discard it.
- For an index default, GD1's declaration context is the entire declaring
  owner vector O and an **empty method vector M**. Index j and assigned-value
  ordinal n never restrict generic scope or name a GenericParam. Projected
  uses have the same owner environment; no accessor receiver/value is a
  constant capture. D1 still permits only its closed SF1 path. GD1 alone
  admits its explicit ConstantSource 1/4/5 composition, helper captures and
  formation/dependency closure; no new term tag or evaluator is selected here.

Accessor selection follows the source operation and its authority/phase,
not default availability, an implementation's labels or a compatible
MethodSig. Map the written indices in source order, then fill expressible
omissions from I(P). The accepted [bracket production](../specs/grammar-spec.md#34-expressions-and-precedence)
has neither empty nor named argument slots: only a missing suffix reachable
after a nonempty supplied prefix can use defaults. An all-default one-index
declaration still does not admit empty brackets. Required/defaulted indices
may interleave without making skipped required positions legal.

`params` keeps its separate contract, final within the **index vector**, not
after the synthesized value Param. IO1 supplies no missing `params`, reference,
result-loan or value codec. Independently unsupported retained contracts refuse.

Perform D1/GD1 checking and required formation at their existing compilation
boundary; default values/terms add no inference or ranking rule. Lower a complete
argument list: n indices for a get, n indices plus the supplied RHS for set/init.
Projected rows do not create extra formation occurrences: preserve GD1's
source-occurrence accounting, not one execution per accessor copy or witness
step. This does not add a compound operation or change its existing expansion.
Preserve accepted receiver/place capture, written-expression order, once-only
evaluation, checks, every-exit cleanup and publication. There is no runtime
default thunk, sentinel, hidden argument, witness search or new assignment rule.

## Expected/provider binding and replacement

Resolve the exact provider/revision, PropertyDef key/kind and bound owner
before comparison under [binding and replacement](../specs/representation/binding-and-replacement.md).
Reuse C1's checked declaration-to-key inputs; a declaration export is not
derived from `this`, an accessor name or an ordinal. IO1 adds no API/JSON
grammar or independently required accessor key.

A PropertyDef Import retains a consumer-local P and local accessor MethodDefs.
**Every linked expected accessor is bodyless**, not just the getter. Bind each
expected role to the corresponding field of the selected actual P, with the
same owner correspondence. Property-selected accessors need no independent
MethodDef exports. An independently retained MethodDef Import must agree with
that selection too. Private actual closure stays provider-owned or is packed
as real definitions; unsupported cross-artifact edges refuse, not fabricated
private keys or foreign row numbers.

For every expected accessor role, the provider must supply that **same role**
with the exact index/value/result correspondence and the parent's directed
receiver, safety, effect, ownership, dependency and phase satisfaction.
Unrequested additional provider roles are permitted only when they leave
all retained obligations intact. A setter is not an init implementation,
nor the reverse. WitnessMembers select those exact role MethodDefs, including
explicit/private implementation targets and admitted default Bodies; a
same-shaped ordinary Callable is not a substitute.

Retain expected and actual I(P) separately. Differences in labels/defaults
alone do not break binary/witness satisfaction: the old caller already chose
its expected requirement's values/terms and passes all arguments explicitly.
Fresh source binding may observe the new provider surface. Replacing an
accessor/default Body never refreshes an already selected default, including
an open GD1 term. Its original helper/program and relied-on dependencies
must remain satisfiable. Removing a required role or changing its phase/type
is a different, rejecting incompatibility.

Aliases of one declaration share its vector; distinct expected views retain
their own P/accessor/surface associations even after binding to one actual.
Eliminating a source-view proxy requires the parent's full source-equivalence
test, including equal labels/default presence and normalized values/terms
after complete formal/reference remapping. If equality is not established,
keep the views; do not merge by provider/key, MethodSig or observed values.

## Exact schema and feature closure

IO1 is shorthand, not a Feature or on-wire selector. Recommend these **eight
unapproved whole agreements**, each exactly its corresponding D1 or GD1 C1
parent plus the PropertyDef/ownership delta above:

| Candidate | Exact candidate identity |
|---|---|
| C1L1R1D1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1-indexer-accessor-owners-1` |
| C1M1L1R1D1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1-indexer-accessor-owners-1` |
| Construction-1L1R1D1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1-indexer-accessor-owners-1` |
| Construction-2L1R1D1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-interface-parameter-defaults-1-indexer-accessor-owners-1` |
| C1L1R1GD1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1-indexer-accessor-owners-1` |
| C1M1L1R1GD1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1-indexer-accessor-owners-1` |
| Construction-1L1R1GD1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1-indexer-accessor-owners-1` |
| Construction-2L1R1GD1IO1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1-indexer-accessor-owners-1` |

Keep `(1,0)`, edition 1, Body codec 01, D1's 16-byte 0066, R1 receiver
semantics, and every matching class/construction stage. GD1IO1 retains GD1's
whole CG1 delta, argument-kind override and **C1** catalog/section precedence,
not a G1/CG1 sub-decoder. Original phase-1/phase-2 maps and expected/provider
Clause/Origin/Plan contexts remain distinct.

There is **no new section, DefId, Feature, Contract kind, flag, opcode,
prefix/escape, Body record or map namespace**. The proposed change to an
existing row's stride needs later explicit registry integration, not an
allocation made by this document. The owner map is mandatory schema meaning;
Feature 26 does not select it.

In each retaining Module preserve L1's `26/1` triggers, D1's `11/1` for
nonzero frozen defaults, and GD1's independent `24/1`, nominal-domain and
evaluation/program requirements when selected. Owner formals alone do not
trigger `23/1`; independently retained method-generic helpers can. Preserve
the parent's reconciled `10 -> []`, `17 -> {10}`, pending `12 -> {11,17}`,
class/construction and other conditional requirements. A private helper or
unrelated Module does not acquire label requirements by table co-location.

Select one exact literal before schema-dependent decoding for the root,
all supplied providers and the stage's captured inputs, including unused or
empty inputs. G1's Callable-only interface route is unchanged. Original C1,
D1/GD1 parents, sibling stages, CG1 and private persistence are not aliases.
Equal versions, zero role words, Features or plausible lengths do not choose
a decoder. Conversion explicitly decodes both selected schemas and preserves
meaning; changing a key/version or appending zero words is not conversion.

## Cross-layer integration, remapping and refusal

| Layer/owner | Required consequence |
|---|---|
| Source binding and bound declarations | Implement ordinary defaults separately; capture the checked index declaration once, exact accessor identities and value position. Preserve them through proofs, substitution, traversal/rewriting and adapters. Do not copy implementation signatures over requirement views or serialize current internal accessor enum values as new Standard tags. |
| Standard production and reference reconstruction | Retain P, every role link, ordered index contract and every eligible surface, including uncalled/bodyless expectations. Use immutable checked source anchors/keys for source publication, or loaded typed facts for source-free re-emission. Existing input routes unable to express that closure must refuse; no new public transport is selected here. |
| Metadata/registry and logical records | Replace PropertyDef's row and getter-only description **for IO1**; specify exact role exclusivity, index prefix and assigned-value identity. Extend the logical witness/declaration requirements with P/role correspondence. Keep Param structural and D1's surface MethodDef-owned. |
| Binding and verification | Extend getter correspondence to each retained role and all-accessor bodyless expectations. Check exact schema/support, extents, references, owner/Module, signatures, role uniqueness, interface membership, surfaces, term context and selected dependencies before affected use. Producer source checking establishes semantic truth; consumers need not re-prove arbitrary Bodies or constant programs. |
| Lowering | Select P/role and the expected default plan before target adaptation; pass every logical argument explicitly. Preserve R1 access and actual init-phase evidence without treating init as an ordinary setter or constructor I. Unsupported runtime/backing/phase representations remain gates. |

Source-free transformation must remap PropertyDef references/DefIds and its
three role links together with TypeDef/Module origins, MethodDefs, MethodSigs,
exclusive Param ranges, Interfaces/InterfaceMethods/WitnessMembers, Imports,
Exports, contracts and receiver/parameter identities. Rebuild source-view
associations using declaration/role, not coincident old ordinals.

Remap 0066 MethodDef/String/ConstantSource references, then the complete
kind-directed value/TypeSpec/Blob graph and, for GD1, GenericParams, templates,
helper captures/programs, targets and retained evidence/dependencies. Preserve
scalar payload bits as data. Clone context-dependent signatures, terms and
expected views when necessary; shared storage does not erase their contexts.
Rebuild ranges, indexes, directories, Body maps/offsets and affected source
locations, both C1 phase-map contexts and per-Module manifests. D1's canonical
surface order remains owner MethodDef then ordinal; valid reordered rows and
equivalent immutable duplicates remain distinct from malformed ownership.
Only genuinely unretained views may be trimmed.

Stripping source/debug must still permit reconstruction of an unseen admitted
indexer use and GD1 substitution from semantic closure alone. A required
helper Body is not debug data. A predecessor that lost accessor/default facts
cannot be upgraded by guessing them. Refuse an unrepresentable destination
atomically, preserving the existing output.

Keep malformed ownership/shape, unsupported required representation,
incompatible binding, failed required formation and resource exhaustion
distinct. Never turn one into a missing default, getter-only partial success,
implementation-default fallback, alternate accessor/overload or runtime
evaluation. Explicit arguments and uncalled declarations do not excuse invalid
retained surfaces.

## Focused conformance cases

These are required future cases, **not test results**. Run source, structural,
binding/remapping and any later execution qualification at their own layers.

| Case | Required outcome |
|---|---|
| Two-index requirement, second default 7; getter/setter variant and getter/init variant; target labels differ and its default is 9 or absent | Each requirement accessor projects the same second-index default at Param 1. Setter/init RHS remains required at Param 2. Calls retain the requirement's 7, not the target's surface. |
| Get-only, get+set, get+init; n=1 and several indices; zero-data index/value where independently admitted | Recover exact role sets/counts; retain every logical position. A zero-index named-property control does not become an indexer. |
| One supplied index versus all indices; interleaved required/defaulted indices; all-default single index | Fill only legal omitted suffixes. Required omissions, empty brackets, named/empty bracket slots reject without inventing syntax. |
| Owner mixed formals with a default using the last owner constant; GD1 direct formal and expression forms | All O is available and M is empty in every projection. Source-free unseen substitution works with the original term/closure. D1 refuses the open term; a forged accessor method-formal capture rejects. |
| One default term projected onto get/set, one source omission versus two distinct omissions | Keep GD1's occurrence and resource accounting; metadata projection creates no extra evaluation and does not merge separate source omissions. |
| Unused/bodyless expected indexer; private getter plus a nonprivate receiving accessor | Preserve total eligible surfaces and reconstruct one declaration vector without promoting private accessors to requirements. No-default and typed unit-default remain distinct. |
| Index named `value`, with getter only versus setter/init added | Getter-only collision-free case can retain the label. Setter/init case rejects under accepted implicit-name rules; no `$value` rename or omitted value surface repairs it. |
| Same-identity inherited diamond, unrelated identical indexers, distinct closed generic instances and explicit implementation | Deduplicate only the same declaring requirement identity. Preserve P/role and original expected vector; no signature/name/default-value merging. |
| Independent source-free consumer, non-first provider and export aliases; provider replacement changes labels/defaults only | Old expectation/compiled arguments survive; fresh views can see the new defaults. Aliases share one actual P, while unequal expected proxies stay separate. |
| Expected getter-only versus provider with an additional role; expected set versus provider init, or removed required role | Additional role is not automatically breaking; every retained obligation still checks. Missing/switching a required role rejects even with identical Params/results. |
| Readonly/mutable get/set differences; init on live receiver, absent phase evidence, missing target/backing support | Apply R1 independently. Init role is never ordinary readonly/mutable authority; unsupported or unauthorized use refuses without invoking a setter/constructor substitute. |
| Missing getter, both set/init links, receiverless indexer, added role on a zero-index property, repeated MethodDef in two roles/declarations, foreign owner/Module, wrong result/value/mode, permuted index prefix, extra accessor method formals | Reject malformed grouping/shape before exposure/use. Shared nongeneric structural signatures alone remain valid when all contexts agree. |
| Different projected labels, default absence, exact scalar bits, term owner/helper/capture mapping; duplicate/partial surface | Reject inconsistent one-declaration projections even when observed values match. Distinct expected/provider surfaces are not this error. |
| Stripped source/debug; permute PropertyDef/MethodDef/MethodSig/Param/GP/source/String/type/Blob rows, pack Import-to-direct, re-emit and import again | Reconstruct the same P/roles/vector, preserve all original expectations, exact bits and GD1 helper closure; remap both phase contexts. No snapshot passthrough or byte substitution. |
| Zero/last row, nonzero high byte, wrong DefId in a role field, count/stride overflow, unit and full-width ordinal versus Body-u16 boundaries | Enforce each namespace and checked capacity separately; no narrowing, sentinel collision or foreign row escape. |
| Later written-expression failure, invalid/failing default formation, changed/removed required GD1 helper | Preserve source staging/cleanup and existing formation/resource failure. A successful specialization or explicit supply cannot legitimize malformed retained metadata. |
| Every IO1 successor versus its parent, other C1 stages, G1/CG1/private input, including empty/unused inputs | Exact complete agreement required before decoding. Neither a 20-byte parent nor a 28-byte IO1 row selects the other decoder by content. |

## Integration boundary

On later adoption, the exact owning edits are the PropertyDef table/semantics
and flags explanation in [metadata](../specs/il-metadata.md#section-kinds-and-row-layouts),
its registry row, [logical records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain),
PropertyDef correspondence in binding/replacement, and the admission,
reader-trust and transformation rules. D1's owner gate is discharged **only
for an admitted IO1 closure**; GD1 keeps its existing term semantics with the
indexer's empty method-formal block. L1/R1 and C1 stage-selection owners need
the explicit whole agreements above. None of their existing keys is redefined.

No source grammar, ordinary-default value domain, library API, dynamic
interface ABI, runtime policy, automatic backing or construction-completion
contract changes. General default implementation, executable accessor/witness
support, init-phase/backing evidence, richer value codecs and host input
transport remain separate support gates, not unanswered ordinal choices.
Formal review must still adopt or reject IO1 and its whole-schema selections.
This draft alone establishes no compiler support or runtime evidence.
