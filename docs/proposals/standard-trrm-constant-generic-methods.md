# Standard TRRM constant-generic method terms

> [!IMPORTANT]
> **Unapproved proposal; provisional direction under the user's authorized
> missing-contract workflow.** This document assigns candidate bytes, not
> normative approval, compiler support or execution evidence. Accepted
> `constant-generics/1` source semantics are unchanged.

## Decision and missing contract

Recommend **extending the existing shared ConstantSource family**, not adding
constant values to MethodSig or inventing a parallel method-only value table:

* Keep MethodSig/GenericParam at 24/32 bytes and GenericCallTarget at
  `20 + 8*A` bytes, including its four-byte record header.
* Add ConstantSource tags **4 = scoped constant formal** and
  **5 = bound constant-expression template**. Use that same source reference
  in constant defaults, constructed TypeSpec arguments, ConstantMap and
  GenericCall actuals.
* Add candidate required sections **`0064=ConstantTemplate`** and
  **`0065=TransparentConstantDomain`**. Reuse typed frozen leaves for values;
  publish nominal wrapper obligations separately from their carrier bytes.
* Compose and amend the Standard part of
  [nominal enum persistence](nominal-enum-persistence.md) for all ten accepted
  fixed-integer bases, with typed member-value references instead of i32
  member payloads.
* In this successor, retire G1's method-argument tag 3. All constant actuals,
  including forwarding, use tag 2 and a ConstantSource. Do not retain both
  spellings as compatibility aliases.

There is **no accepted exact physical contract for this whole route**.
[Constant generics](../specs/language/constant-generics.md#retained-contracts-and-compatibility)
and the [logical constant records](../specs/representation/logical-records.md#constant-generic-domains-encoding-pending)
explicitly leave physical generic/value/validity records gated.
Assigned GenericParam fields and ConstantSource tags 1..3 provide part of the
graph, not nominal eligibility or open expression terms.
The [G1 proposal](standard-trrm-generic-method-call-body-encoding.md#tagged-method-arguments)
is itself unapproved: it selects fixed-integer/Boolean actuals and a
Body-only formal-forwarding tag, explicitly excluding symbolic defaults,
open constant arguments inside TypeSpecs, enums and wrappers.
[Private G107](private-generic-method-persistence.md) excludes constant
formals and supplies no Standard encoding.

This decision completes the **constant-term carrier and binding rules**.
It does not complete the independently gated evaluator, witness, general
predicate, target-value or generic-class execution contracts. A representable
template is not proof that its required program can execute. No new source
syntax, implicit conversion, inference, operator, export-key convention,
shared-generic ABI or diagnostic-only substitute is selected.
The [source type and generic-method export-input proposal](standard-trrm-source-generic-method-export-inputs.md#cg1-combined-source-producer-inputs)
owns CG1's joint producer path: explicit source TypeDef/member keys and
generic-method span mappings, transported in one version-2 file against one
ordered snapshot set. G1's version-1 method-only file and the independently
gated NominalEnumsDraft enum option cannot supply that combined input.
This document owns the wire/domain contract, not another key transport.

## Alternatives

| Route | Assessment |
|---|---|
| G1-local extension: additional GenericParam default variants and GenericCall argument tags for values, formals and expressions | Small for literal-only methods, but defaults, nested TypeSpecs and ordinary loads need corresponding carriers. Repeating nominal/domain checks and expression variants at each use is not a complete shared solution. Enlarging those rows would be permissible, not prohibited by draft compatibility. |
| New shared ConstantValue/ConstantTerm table, referenced uniformly from every constant position | Coherent if it replaces ConstantSource for this entire graph and has typed literal, formal and template variants. It still needs nominal contracts, bound programs and target/dependency retention. Keeping both value families requires adapters and another reference namespace; replacing the existing family migrates unrelated constants without a present benefit. |
| **Extend shared ConstantSource; retain existing frozen values and add only formal/template sources** | **Selected.** One typed source reference already occupies each relevant four-byte slot. No GenericParam/TypeSpec/CallSite enlargement, duplicate scalar codec or independently indexed argument table is needed. A template is distinct from a concrete execution occurrence. |
| Put an open default into ConstantSource tag 2 with invented execution dependencies | Not selected. ConstantExecution describes a particular request, including its substitution, envelope and reached-dependency ownership. An expression over unbound formals cannot publish the dependency trace of its future executions. |

This is the smallest complete shared route, not a claim of measured byte,
allocation or throughput superiority. Existing unpublished assignments can
be redesigned; preserving their bytes is not a decision criterion.

## Exact selection and owner integration

Candidate out-of-band identity:
**`trrm/1-draft-constant-generic-methods-1`**, abbreviated **CG1**.
CG1 is exactly G1's complete
`trrm/1-draft-generic-method-calls-1` selection, the **Standard delta only**
of nominal enum persistence, and this document's amendments. In particular:

* Retain the complete SF1 24-scalar registration and G1 call/evidence rules.
  The constant-formal catalog remains much narrower than SF1.
* Replace only the enum delta's int32 restriction and `0054.value:i32`
  field as specified below. Preserve its TypeDef kind 4, complete declaration
  snapshots, member keys, Layout rules and Import/Export ownership.
* Replace G1's constant-argument domain restriction and tag-3 argument branch
  with this shared route. Its source-free substitution, plural obligations,
  original expectations and invocation phases remain authoritative.
* Do not compose private encodings, F/S/array, general-predicate, static
  storage, source-unit/friend, RootPin, intrinsic or ConstantExecution
  substitution successors merely because they are linked here.

Keep `(1,0)`, governing edition 1 and register codec 01. There is no on-wire
CG1 selector. Exact schema agreement precedes decoding; neither Feature
lists, matching version bytes nor plausible rows choose it. G1 and CG1 are
separate exact schemas, not permissive decoders for each other.

Candidate **Feature `24/1=ConstantGenericTerms`**, governing editions `[1]`,
immediate prerequisites **`[]`**, covers these formal/domain/value/
template representations, binding and source-free transformations.
**Recommend independent admission** because accepted GenericParam ownership
and constructed-TypeSpec argument rules distinguish TypeDef constant terms
from MethodDef method formals and generic calls. A TypeDef-only constant
formal/value must not require GenericCall capability. This is a proposed
capability boundary derived from that accepted separation, not an accepted
Feature registration.

| Admission alternative | Assessment |
|---|---|
| **Independent `24/1`, prerequisites `[]`; require 23 for retained G1 uses** | **Selected.** Preserves the accepted TypeDef-versus-MethodDef separation while using one shared constant-term family. TypeDef constant formals/defaults/TypeSpec actuals need no generic-method capability unless their retained closure independently contains a G1 use. A template's generic helper or GenericCall Body supplies such a use; no prerequisite edge is needed to detect it. |
| Bundle `24/1 -> 23/1` | Coherent but unnecessarily coarse. It would force generic-method capability on a TypeDef-only constant graph despite that graph containing no method-owned formals or GenericCallTarget. The exact CG1 composition alone does not justify this admission dependency. Not selected. |

Keep the **same CG1 out-of-band identity and exact G1 + Standard enum delta +
CG1 wire composition** above. The whole-artifact schema specifies how all
forms are encoded; each Module's retained uses independently determine its
required capabilities. Selecting CG1 alone activates neither 24 nor 23,
and 24 does not imply 23. This one proposed catalog requires exact agreement;
Feature lists do not select a different decoder or authorize either an
implicit dependency edge or an alternate wire layout.

Require 24 in **each using Module** whose retained graph contains any of:

* A GenericParam with formalKind 2 owned by a TypeDef **or** MethodDef,
  including an uncalled definition or bodyless expected/imported declaration.
* A constructed TypeSpec form 05 constant slot or a GenericCallTarget
  constant method-argument slot, even when its actual is already frozen.
  Traverse nested types, signatures, constraints, defaults and Bodies.
* A ConstantSource tag 4 or 5 in any retained position, including an ordinary
  ConstantMap load, forwarding or a template actual.
* A TransparentConstantDomain contract or a wrapper carrier leaf that
  requires it, including expected/provider contract views.

Require **23 independently** whenever G1's own retained-use triggers apply:
a MethodDef with method formals, including expected/declaration-only views,
or a GenericCallTarget, including owner-only instantiation and forwarded
generic-call actuals. This includes a ConstantTemplate helper with generic
method formals or a helper Body retaining a GenericCallTarget. A nongeneric
helper with no G1 call does not trigger 23 merely by being a template program.

CG1 forwards a constant through tag 4 instead of G1's argumentKind 3.
Tag 4 in a TypeDef default/TypeSpec slot does not itself require 23; using it
as a GenericCall actual does, because the GenericCallTarget independently
triggers G1. Method constant formals require both 24 for the constant terms
and 23 for the generic MethodDef. An otherwise type-only generic
method/helper requires 23 but not 24 unless its closure also contains a
constant-term trigger. TypeDef-only constant uses require 24 without 23
when no independent G1 trigger survives in that Module's retained closure.

Requirements cover unused retained declarations/defaults and each using
Module's entire retained closure, not just reachable calls. Unreferenced
global source rows or common framing alone do not activate 24. SF1 values,
enum declarations/values and type-only TypeDef formals do not by themselves
trigger 24 or 23. Missing actual semantic G1 capability cannot be replaced
with recognition of its low-level record framing when a retained use
requires 23; it is not an admission prerequisite for TypeDef-only terms.

Feature 24 does not grant evaluator, native-boundary or witness support:

| Retained use | Additional requirement |
|---|---|
| Generic MethodDef or GenericCallTarget in the retained closure, including a template's generic helper or GenericCall Body | `23/1` independently under G1; not derived from 24. |
| Primitive frozen value or a nominal carrier leaf | `11/1`; merely declaring or forwarding a formal does not require a frozen value. |
| Enum declaration/type/value | The amended `18/1 -> 11/1`, including declaration-only uses. |
| A published Blittable Layout | Existing native-boundary/target requirements, including `1/1`; neither 18 nor 24 replaces them. |
| ConstantTemplate's ConstEval declaration/program or an existing tag-2/3 source | Complete required evaluation-form, program, target and execution support, including the existing Feature-12 closure. Feature 24 does not register incomplete 10/12 capabilities. |

G1's catalog still has `10 -> 11` and `12 -> {10,11}`; the independently
proposed general-predicate catalog is not selected. A future composition
that admits its read-bound/program records needs its own exact selection.
CG1 makes a dependent default's carrier concrete without pretending those
independent prerequisites have become available. Preserving/inspecting a
template graph and executing a new instance remain different support claims.

| Future integration owner | Exact amendment |
|---|---|
| [Constant logical records](../specs/representation/logical-records.md#constant-generic-domains-encoding-pending) | Replace the physical-carrier deferral with these source/domain/template records; retain the accepted eligibility, validity and producer-truth rules. |
| [Generic logical records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending), [metadata rows](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [typed Body records](../specs/il-instruction-set.md#typed-operand-records) | Add source tags 4/5 and their exact namespaces, contextual scope/default checks, nominal carrier leaves and the two tables. Preserve formal owners/ranges and kind-directed TypeSpec/default slots. Apply the CG1 override to G1's argument tags. |
| [Registry](../specs/representation/trrm-1.registry.json), [admission](../specs/representation/requirements-and-admission.md), [manifests](../specs/representation/semantic-manifests.md) | Add the two required sections, source variants, amended enum fields and Feature 24 registration with prerequisites `[]` and the retained-use triggers above; retain G1's independent Feature-23 triggers and mirror catalog projections. Do not mark unrelated pending registrations complete. |
| [Source type/method producer inputs](standard-trrm-source-generic-method-export-inputs.md#cg1-combined-source-producer-inputs) | Supply explicit nominal definition/member keys and method declaration-span mappings through the shared six-argument API and CG1-only version-2 CLI file. Preserve exact-program capture, one ordered snapshot set, all aliases/key-collision checks and atomic output preservation; G1's version-1 and NominalEnumsDraft-only routes remain unchanged. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Add the contextual edges, nominal obligations, template/request distinction and transformations below. |
| [Versioning](../specs/representation/format-versioning.md), [representation cases](../specs/conformance/il-binding-and-replacement.md), [source cases](../specs/conformance/source-constant-generics.md) | Select the whole CG1 draft and add independent cases; source acceptance itself is unchanged. |

### Allocation review

Numbers here are hexadecimal except decimal Feature IDs. Accepted sections
run through `004B`, plus `7F00` SourceMap. Companion candidates occupy
`004C..0052`, enum `0053/0054`, static `0055..005C`, intrinsic `005D`,
mixed execution substitutions `005E`, and
[source-unit/friend access](static-field-source-unit-and-friend-access.md)
**`005F..0063`**. Select **0064/0065**, required policy `0000`, not a
second interpretation of those sidecars. Feature candidates through 23
leave 24 available; DefId `24=StaticField` is a different namespace.

No new TypeSpec form/discriminator, DefId, Import/Export kind, Contract kind,
Body record, primary/control opcode, prefix or escape is needed. Preserve
G1's record `10`, target kind 4 and `90`/`E0` instructions. The two new
sections use the existing global 24-bit row capacity; Body indexes stay
zero-based u16 with FFFF usable. No lower candidate number saves field bytes.

## Wire contract

All multibyte fields are little-endian. **R(T)** is a required one-based u32
row in T, high byte zero, in `1..00FF_FFFF` and within its actual count.
It is not a token, DefId, byte offset or cross-artifact reference.
Complete TypeSpecRefs retain the existing row/F2/F3 grammar. All counts,
products, ranges and offset-index extents require checked preflight.

### ConstantSource and constant positions

Keep section `0030`'s eight-byte row:
`u32 kind, u32 payloadRef`.

| Kind | Exact payload namespace and result domain |
|---:|---|
| 1 | R(FrozenValueNode), `003F`; its complete root type is the domain. |
| 2 | R(ConstantExecution), `0042`; preserve its separately admitted request/result/target obligations. |
| 3 | R(TargetValueBinding), `0049`; preserve its frozen root and exact target/layout dependencies. |
| **4** | **R(GenericParam), `0019`**, whose formalKind is 2. Its substituted constantTypeSpec supplies the domain; its current instance binding supplies the checked value. Never follow its default to obtain a missing actual. |
| **5** | **R(ConstantTemplate), `0064`**; its substituted resultTypeSpec supplies the domain and its bound program supplies required, not optional, formation. |

Zero/unknown kinds, zero required references and wrong namespaces reject.
Tag 4 is newly proposed Standard data, not the similarly numbered private
tag or G1's argumentKind 3. A GenericParam DefId such as `1900_0001` is
not R(GenericParam).

Use the same R(ConstantSource) in:

* A constant GenericParam's nonzero `defaultArgumentRef`.
* Every constant slot of constructed TypeSpec form 05; type slots remain
  complete TypeSpecRefs. The referenced declaration's full formal range
  determines each slot's namespace.
* A GenericCallTarget argument row with `argumentKind=2`; kind 1 remains
  a complete TypeSpecRef. Both use `u8 kind, u8 flags=0, u16 reserved=0,
  u32 argumentRef`. Kind 3 is invalid in CG1.
* ConstantMap's existing source field, including an open Body loading its
  constant formal. The map type must equal the source's substituted domain.
  Existing load.constant instructions suffice; this creates no runtime
  dictionary or arbitrary constant-load opcode.

All actual lists are complete, in **mixed declaration order**, including
unused/defaulted positions. No inferred slots, `_`, `default` marker,
trailing omission or type-before-constant sorting is serialized.
MethodSig's generic range is exclusively owned by its MethodDef.
Owner ordinal 0, method ordinal 0 and a helper's ordinal 0 remain distinct.

### Domains and frozen values

The ten fixed-width integers and bool need no domain row. Reuse SF1's
exact-width two's-complement/unsigned little-endian containers and Boolean
byte 00/01. Preserve mathematical/logical identity, including signed
128-bit minimum and unsigned 128-bit maximum. Integer signed-zero spellings
produce the same zero. No host-width conversion, truncation or wrapping
occurs while decoding a value.

**Enums:** keep the enum proposal's 20-byte `0053` EnumDef and 20-byte
`0054` EnumMemberDef, with these exact replacements:

```text
EnumDef.underlyingType:
    TypeSpecRef of one unqualified fixed integer, codes 06..0F
EnumMemberDef:
    u32 ownerTypeDef, u32 ordinal, str declarationKey, str name,
    u32 valueSourceRef                    // R(ConstantSource), tag 1 only
```

The member source's frozen root type is that exact enum, not its base.
Its Blob width/interpretation comes from EnumDef.underlyingType.
No field is an inline i32 in CG1. Every kind-4 TypeDef, including an
expected proxy, has exactly one EnumDef and its complete exclusive ordered
member range. Keep public, module-level, primary, nongeneric enum ownership,
name/key uniqueness, Flags and target-specific Layout commitments from the
enum proposal. A wider base is not a native-width or Boolean base.
Duplicate member values remain allowed; unnamed underlying values remain
valid. Neither member key nor chosen spelling is part of value identity.
This intentionally broadens that candidate's physical base catalog, not the
accepted source catalog or the existing int32-only implementation.

**Transparent wrappers:** required section **0065** concatenates fixed
**12-byte** rows:

```text
u32 ownerTypeDef                         // R(TypeDef), actual or expected
u32 carrierTypeSpec                      // complete term in the owner's scope
u32 carrierField                        // R(FieldDef) in that owner's range
```

There are no flags, padding, count prefix or implicit fields. A row is
owned by its TypeDef and inherits its Module. At most one row per TypeDef;
exactly one is required for every retained wrapper-domain use of that
definition. Primitive and enum definitions have no such row. Imported
TypeSpec form 03 resolves to its own expected TypeDef/field/contract view,
then binds that view to the selected provider.

This row publishes the source-derived commitment, **not a second source
opt-in**: an instance satisfying its substituted primitive-carrier and
original formation requirements is Copy, has frozen validity and the
`constant-generic-carrier/1` typed value codec, and retains its required
per-target Blittable commitments. The selected
Transparent Layout has exactly one instance field, carrierField, with
carrierTypeSpec one of the ten unqualified fixed integers or bool after
substitution.
Check the ordinary Layout and complete field type; static fields do not
count, but an extra zero-data instance field does. A field of enum, wrapper,
optional or floating type is not flattened. Equal size, Blittable alone or
an arbitrary same-named attribute supplies no commitment.

A generic wrapper's row publishes definition-owned carrier/contract terms,
not caller-specific added eligibility. Distinguish two checks:

* **Concrete closed-instance resolution:** when all owner actuals are already
  fixed, forward-substitute them into the published terms and validate that
  complete instance's primitive carrier and every other eligibility
  obligation. A declared field-type formal may resolve to int32 here. This
  checks the requested closed domain; it does not repair an open declaration
  that was invalid when checked.
* **Declaration-time open-domain proof:** before admitting a symbolic
  constant formal, its declared terms and published guarantees must establish
  catalog membership and formation for **every** admitted substitution,
  without waiting for callers. An unused symbolic tag may remain in nominal
  identity when the carrier is already int32 and the remaining commitments
  are established. An unresolved carrier formal with only Copy, Blittable or
  operator evidence does not establish membership in the closed catalog.
  Sampling or collecting eventual valid closed instantiations cannot supply
  the missing declaration proof, and no inverse carrier inference is added.

Preserve all ordered complete arguments, even unused tags. The row is not a
general Copy TypeDef flag or a promise that every carrier bit pattern is a
valid wrapper. It exposes no private field API, parser, conversion,
constructor or operator.

For both enums and wrappers use a **kind-1 FrozenValueNode leaf** with exact
complete nominal domain, zero flags/reserved/child/case fields, and a
required Blob containing exactly its carrier's SF1 integer/Boolean bytes.
For wrappers this is an explicit new interpretation of kind 1, authorized
by 0065 and Feature 24, not an inference from struct shape or kind 2.
No host object, aggregate padding, field child or validity-authorizing bit
is stored. Normal complete-type formation rules still apply; qualifications
cannot be silently discarded to fit a codec.

Producer truth establishes that a published typed wrapper value is complete
and valid. Consumers check its structural/representation obligations, not
arbitrary invariants by constructor replay. New construction must still run
its selected eligible program and checks. A symbolic nominal root may retain
caller type arguments only when its published value is valid throughout
that declared context and no required construction dependency is missing;
otherwise retain a template, not invented carrier bits.

Value interning/equality uses exact normalized complete domain plus
mathematical/logical carrier value, retaining target/validity dependencies
as obligations. Different nominal domains never merge. User equality,
hashing, formatting and constructors do not participate. Shared Blob bytes
do not make typed roots interchangeable.

### Bound constant-expression templates

#### Resource ownership alternatives

| Route | Assessment |
|---|---|
| **Omit template budgets; select and record each concrete request's actual envelope** | **Recommended.** The accepted resource contract belongs to ConstantExecution, not a generic default. A template retains the bound expression and its dependencies; the consumer's default/explicit request policy supplies its actual envelope when a new occurrence is instantiated. No provider cap or duplicate budget owner is needed. |
| Publish four per-template upper ceilings U and cap each consumer request by them | Mechanically coherent if an explicit source/contract rule assigns that restriction to the default. No such rule is selected here. The same default expression could acquire a provider-chosen refusal threshold unrelated to its declared semantics, and the 32-byte cap duplicates policy beside the request's actual envelope. Not selected. |

Select **no U fields**, inferred caps or reserved budget words. This replaces
the provider-capped candidate rather than retaining two template layouts.
An independently adopted explicit resource contract would need its own
owner, representation and admission rules; this proposal creates none and
does not erase actual envelopes on already retained concrete requests.

#### Template row and expression binding

Section **0064** is an ordinary offset-indexed variable table. Each row is:

| Offset | Bytes | Field |
|---:|---:|---|
| 0 | 4 | `u32 selectedMethodReference`: MethodDef DefId `16_rrrrrr` or Import token `1C_rrrrrr` expecting MethodDef |
| 4 | 4 | Required complete `resultTypeSpec` in the using context |
| 8 | 4 | `u32 targetProfileRef`: R(TargetProfile), or zero to defer exact profile selection to the instantiating context |
| 12 | 4 | `u32 argumentCount=A` |
| 16 | `8*A` | Inline ordered actual rows: `u8 kind`, `u8 flags=0`, `u16 reserved=0`, `u32 actualRef` |

Exact extent is **`16 + 8*A`**, no budget prefix, trailer or padding.
Zero actuals require exactly 16 bytes; one actual requires 24. The retired
budget-bearing `48 + 8*A` extent is not an alias or a layout to probe.
Kinds are exactly 1 = complete TypeSpecRef and 2 = R(ConstantSource).
Every actual matches the selected helper's method formal by kind/ordinal;
argumentCount equals its entire formal count. There is no Body-local
map/index or ordinary argument list here.

The selected program is an ordinary checked **module-level static helper**
with no receiver or ordinary parameters, the exact substituted result and
the ConstEval evaluation form. An arbitrary Body, Pure-only function,
signature anchor, runtime-only function, private placeholder or reference
stub is not a program. Resolve an Import against its retained expected
declaration, then obtain the actual admitted Body/implementation closure.
Original bound operations, arithmetic policy and calls remain selected.

A producer lifts a required generic expression into this helper, rather
than serializing syntax or introducing another expression language.
For a default, the helper's ordered formals are the declaring owner's
formals followed by all **earlier** method formals; for a TypeDef default,
only its earlier owner formals. Copy their kinds/domains and the complete
requirements needed by the lifted body with explicit capture-avoiding
mappings, but give helper formals no defaults. Required evidence must have
its entire dependency closure within that prefix. Do not pull a later formal
into a default merely to copy an unrelated constraint from an earlier formal.
Such an unused constraint stays on the original declaration, not the helper;
a default actually requiring it cannot claim earlier-only dependencies.
The declaration's template supplies the corresponding identity actuals.
A call selecting that default substitutes its already established earlier
actuals into this graph. All captured positions, including unused ones,
remain explicit. An expression actual in an open caller uses the same
lifting rule for its in-scope caller formal environment.

The helper's Body loads constant formals through tag-4 ConstantMap sources.
Factory calls or operations on nominal values remain ordinary already-bound
calls with their exact contracts; they are not replaced with raw carrier
creation. The producer retains the helper and all required implementation
dependencies, without automatically exporting it or assigning a source key.

A template is **not an executed ConstantExecution**. Before execution,
close every required type/constant/evidence/target input, bind the program,
and form a fresh concrete request under the accepted
[constant-execution rules](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation).
Zero targetProfileRef means deferred selection, not host/default/wildcard
authority; a nonzero profile is an exact retained constraint. Evaluation
computes that occurrence's reached dependencies and target observations.

#### Concrete request envelope

A template publishes **no budget**. For each newly instantiated root
ConstantExecution occurrence:

1. Before execution, select the actual envelope E from the consumer's
   default or explicitly selected request policy under the accepted
   [portable-envelope rules](../specs/language/proof-and-constant-evaluation.md#portable-resource-envelope).
   Preserve the guaranteed supported defaults/minima and explicit
   smaller/larger selections; do not infer a provider ceiling from the
   template, its helper, its generic default or an earlier occurrence.
   The same immutable template can instantiate under different selected
   envelopes without changing its declaration/value identity.
2. Check that the selected envelope is supported. An explicitly constrained
   E is reported as such, not silently clamped, raised to the default or
   reported as a larger envelope. A zero component is a zero limit for that
   request, never absence, inheritance or infinity. Metadata cannot select
   a larger policy or waive a missing recipe.
3. Before execution, record **only E**, including every constrained or zero
   component, as the concrete request's actual envelope. If persisted, its
   four values occupy the existing ConstantExecution u64 fields at offsets
   **48/56/64/72**, in the order semantic steps, active frames, live abstract
   storage bytes and encoded result bytes. These are actual request ceilings,
   not consumption totals or remaining-balance estimates. Keep the same
   actual-envelope evidence for in-memory and failed evaluations; nothing
   is copied back into ConstantTemplate.
4. Execute against E using the accepted
   [constant-request accounting](../specs/language/proof-and-constant-evaluation.md#portable-resource-envelope).
   Preserve its inclusive limits, exact work/frame/storage charges,
   work-before-depth-before-storage exhaustion priority and post-cleanup
   value/result-length checks. Encoding still consumes work and live storage.

Nested helper calls, template applications and dependency evaluation do not
select a fresh default envelope or receive a free new root budget. Retain
cumulative work and simultaneously live frames/storage under the enclosing
request's unchanged envelope and ledger. A separately represented nested
ConstantExecution retains its own actual selected envelope as well; neither
record replaces the other's scope or resets enclosing accounting. Releasing
storage does not refund work. Result-byte limits retain their accepted
request scope, not a new sum-of-all-intermediate-encodings rule.
Decoding/binding budgets remain separate from constant execution.

No automatic raised-limit retry may convert exhaustion under E into success.
A separately requested evaluation needs its own explicit selection and
recorded actual envelope; it cannot change the failed request's E or turn
its selected zero into a default. An earlier success under a larger envelope
is not evidence that the same accounted evaluation fits a smaller E.
Re-emitting an existing concrete request preserves its recorded E rather
than substituting the current consumer's default; selection applies to a
genuinely new template occurrence, not a reinterpretation of saved requests.

If a concrete occurrence is persisted, use its separately admitted
ConstantExecution/TargetValueBinding records and exact schema, including the
[mixed substitution successor](constant-execution-mixed-substitutions.md)
where selected. Do not embed its eight-byte rows into a schema that selects
the old four-byte execution range. A frozen target-independent completed
value may instead use tag 1 when no other retained obligation survives.
Target-dependent results retain their exact binding/observations; changing
tag 5 to tag 1 cannot erase them.

Failure, unavailable program/support/target, invalid result or exhausted
required evaluation refuses the formation. There is no runtime fallback,
another overload, default carrier value or retry as a different source tag.
Metadata sharing of equal templates does not merge evaluation occurrences
or authorize eliding separately authored constructions.

## Context, defaults and source-free binding

Value/source/template tables have no new Module cursor. Each owning
declaration, TypeSpec use or Body supplies the using Module and lexical
formal environment. Resolve actual terms there, declaration terms in their
own expected/provider context. Share a row only when it has the same
resolved meaning in every using context; otherwise clone/remap it.

For a MethodDef default at ordinal q, allowed free formals are its declaring
owner's formals and method ordinals `< q`. A TypeDef default admits only its
own ordinals `< q`. Traverse nested type constant slots, nominal domains,
template result/actual terms and forwarded sources to establish these
dependencies; helper-local formals use their explicit mapping, not a free
capture. Self/later/out-of-scope dependencies and value/type-source cycles
reject. A formal reference reads its actual binding, never recursively
consults that formal's default. Ordinary recursive program calls are not
metadata-value cycles; they remain subject to constant-execution resources.

At a generic call, resolve the exact declaration/Import, bind owner first,
then all method slots in mixed ordinal order with separate caller/callee
environments. Compute each formal's normalized substituted domain D.
Its actual source must have **exactly D**, including nominal identity and
ordered complete arguments. No numeric promotion, carrier extraction,
wrapping, type inference from bits or inverse equation solving is added.
Prove open eligibility from published declared-domain facts, not successful
closed samples or body-layout placeholders.

Source selection resolves inference/default markers before serialization.
A forwarded already checked formal/value reuses its validity and dependencies
without constructor replay. Selecting an expression default retains and,
when concrete, executes that published bound expression. An unused or
currently overridden default still belongs to the exported declaration.
Unencodable/unsupported defaults cannot become `defaultArgumentRef=0`.

Source-free references preserve the entire expected declaration and the
default/template/domain closure needed by independent new consumers.
An expected MethodDef stays bodyless; its required expression program is a
separate retained helper definition or selected implementation dependency,
not a throwing stub or a closed specialization. Remove source/debug data
without removing semantic owner/ordinal, provider/key or program identity.
No parser, source snapshot, sample name or source overload search is needed
to bind a represented constant term.

## Consumer checking, replacement and re-encoding

Mandatory checks cover exact selected schema/capabilities; directory/offset
framing and arithmetic; row kinds/namespaces/bounds; exclusive formal/member
ranges and ordinals; source/template graph scope and cycles; complete
type/domain/carrier agreement; enum/wrapper/Layout ownership; template
signature/form/actual agreement; and all selected provider/target bindings.
Reject malformed or unsupported affected input before use. Structural
validity does not replace missing schema support or published obligations.

The defining producer owns eligibility and value truth. A consumer does
not re-prove arbitrary wrapper invariants or all generic Body facts.
It does check the new instance's substituted domains and binding obligations,
and execute a genuinely new required formation when admitted. Optional
semantic audits remain separate; no serialized trust flag is introduced.

Replacement preserves every original view and stronger relied-on fact.
Match formals by owner/kind/ordinal, not names/rows. Changing arity/kind,
constant domain/carrier, validity of previously promised values, required
eligibility, target commitments or an incompatible published default breaks
the affected contract. Enum snapshots retain their complete member keys/
names/ordinals/typed values. A private wrapper field rename alone does not
change its domain/value identity: compare the sole carrier relationship,
not the field spelling, while preserving any independently retained field
contract. Preserve constructor/value-codec behavior actually promised.

Default programs compare after context/formal/reference remapping, including
their already-bound operations and retained promises. Physical helper names,
rows and formal spellings are not default identity. Already selected call
actuals and original default programs do not silently become the replacement's
defaults; retain their closure or reject an unsatisfied replacement.
Provider changes invalidate dependent bindings/specialization caches.

| Decoded edge | Required transformation |
|---|---|
| GenericParam / MethodSig / TypeSpec 05 | Rebuild exclusive ranges, owner DefIds and ordinals; remap domain TypeSpecRefs, type defaults and constant source slots by formal kind. Preserve mixed order. |
| ConstantSource 1/2/3/4/5 | Map FrozenValueNode / ConstantExecution / TargetValueBinding / GenericParam / ConstantTemplate respectively. A shared numeric word never chooses another table. |
| ConstantTemplate | Rebind/remap MethodDef/Import, complete result type, exact/deferred target, all tagged actuals and helper/formal/program closure. Rebuild offsets for exact `16 + 8*A` rows; there is no template envelope to copy. Separately retained concrete ConstantExecution rows preserve their actual envelopes. |
| Enum / wrapper contracts and leaves | Remap TypeDefs, exclusive enum ranges/member source refs, carrier FieldDefs, complete nominal argument trees, Layouts and frozen node/Blob refs. Scalar Blob bytes are never reference slots. |
| GenericCall / ConstantMap / Body | Remap target/record/map references in their separate namespaces, all constant/type actuals, offsets, Block/code extents, instruction widths and optional SourceMap slots. Retain G1's evidence/obligation fixups. |

When closing a separately emitted Body, replace tag-4 sources with their
bound admitted value sources and instantiate tag-5 templates as required.
Substitute nominal domains without erasing unused tags; preserve any remaining
target/validity/provider obligations. No caller formal may dangle outside
its lexical environment. Keep the original open declaration when new
independent instances are promised. If the destination schema cannot retain
the surviving graph, refuse that output operation.

Converting a G1 argumentKind 3 requires decoding its caller formal, creating
the corresponding tag-4 source and writing argumentKind 2 under CG1. Enum
i32 member words likewise require decoded typed values, not header changes.
Canonical writers preserve semantic argument order, reuse equal typed
value roots within a context, visit sources/templates in logical-use order
and rebuild indexes. Ordinary valid duplicate/reordered rows remain valid;
template sharing is not value equality or permission for construction CSE.
Keep every using Module's complete manifest and original expected binding.

Closed method-instance lowering uses the resulting typed values and ordinary
G1 calls, preserving all state, provenance, caller-unsafety and cleanup
obligations. TypeDef-only domain/value substitution uses the shared terms
without introducing a GenericCall or Feature-23 requirement; independent
layout/runtime gates still apply.
Cache identity includes exact domains/canonical values, open template/context
where unresolved, provider revisions/bindings, original expectations, schemas,
rules and target/runtime. There is no new machine constant-generic argument
or dictionary ABI.

## Evidence and acceptance gates

These are inspected source facts and explicitly attributed prior results,
**not tests newly executed for this documentation revision**:

| Source | Current boundary |
|---|---|
| [GenericParam reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericParamTableReader.cs) and [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericParamTableWriter.cs) | Already use the 32-byte kind-2 shape and check domain/default reference bounds. These checks do not publish wrapper eligibility or implement method constant binding. |
| [ConstantSource reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceTable.cs) | Tags 4/5 now have exact-CG1-only GenericParam/ConstantTemplate row bounds; G1 retains tags 1..3. The low-level [ConstantTemplate table codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantTemplateTable.cs) checks exact `16 + 8*A` row extents, reserved fields, TypeSpec/ConstantSource/target-profile references, and local or imported MethodDef-view arity and formal kinds. It does not check tag-4 use-site scope, imported provider compatibility, ConstEval declaration/program closure, expression formation, or source-free remapping. |
| [Feature manifest and structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs), [formal tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericParamTableWriterTests.cs) and [multi-Module matrix](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericConstantFeatureMatrixTests.cs) | Candidate structural-core gates cover Feature 24/1 for kind-2 GenericParams, `TransparentConstantDomain` owners and retained constructed-TypeSpec/ConstantSource tag-4/5 uses; Feature 18/1 for CG1 enum declarations (with its Feature 11/1 prerequisite); and Feature 1/1 for published Blittable Layouts. Feature 23/1 remains independent for generic MethodDefs/GenericCallTargets. Exact CG1 schema dispatch is not yet wired, so these checks do not establish consumer conformance. Nominal frozen-leaf identity/value binding and tag-4 use-site scope remain open. |
| [Frozen nodes](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs), [value binding](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) | Registered scalar and separately selected int32 enum paths are not a complete fixed-integer enum/wrapper or symbolic constant codec. |
| [GenericParam writer tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericParamTableWriterTests.cs), [constant default tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmConstantSourceValueBindingTests.cs), [constructed binding tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmConstructedTypeSpecBindingTests.cs), [CG1 ConstantSource/template tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmConstantSourceTableTests.cs) and [template table tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmConstantTemplateTableTests.cs) | Exercise the low-level CG1 tags and template framing/structural references. They do not establish mandatory artifact dispatch, source binding, value/domain semantics, or independent generic method consumption. |
| [Body writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyRecordWriter.cs), [reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs) and [tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmRegisterBodyReaderTests.cs) | Candidate G1 low-level framing is exactly `20 + 8*A` total bytes, including the header, with no extra gap. Commit `a6de9404` corrected the writer/parser and tests; 86 focused reader tests were reported passing. Semantic generic-call binding and source emission remain absent. That framing result establishes neither G1 semantic capability nor CG1 support. |
| [Resource-envelope owner](../specs/language/proof-and-constant-evaluation.md#portable-resource-envelope) and [logical execution records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | The actual envelope belongs to a concrete request and its four u64 ConstantExecution fields. No accepted resource budget is assigned to a generic default. CG1's `16 + 8*A` template codec contains no budget fields; no template evaluation or qualification is claimed. |
| [Bounded-generic inventory](../implementation.md#bounded-generic-functions), [private G107](private-generic-method-persistence.md) | Source type-formal proof/default carriers and private candidate records do not supply constant formals or independent Standard generic method interchange. |

The [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
has no Compile exclusions. The following are required independent
conformance/implementation cases, not claimed passes:

| Group | Required outcome |
|---|---|
| Values and domains | Every integer endpoint including both 128-bit extremes; Boolean 0/1 and rejection of 2; enum aliases/unnamed values; two unrelated equal-carrier enums/wrappers remain distinct. Reject native-width, character, floating, optional, reference and general aggregate domains. |
| Wrapper eligibility | Checked primitive/Boolean Transparent carrier; extra zero-data field, enum/wrapper carrier, missing Copy/frozen/Blittable/codec facts and arbitrary valid-bit claims reject. Preserve private field renames without changing nominal value identity. |
| Symbolic nominal domains | Resolve a concrete closed instance's carrier by forward substitution of published terms; separately admit an int32-carrier wrapper with an unused earlier symbolic tag only when its whole-domain obligations are established at declaration time. An unresolved open carrier is not repaired by any set of successful closed instances. Preserve complete identity through forwarding, closure and rewriting. |
| Feature admission: TypeDef and values | A TypeDef-only constant formal, default, constructed constant slot or tag-4 forwarding requires 24 but not 23 when its closure has no G1 trigger; a frozen value additionally requires 11. Wrapper contracts/leaves trigger 24 with their own representation prerequisites, not an implicit 23 edge. SF1-only and enum-only Modules acquire neither 24 nor 23 merely by selecting CG1. |
| Feature admission: methods and templates | Method constant formals require both 24 and 23; a type-only generic MethodDef requires 23 without 24 unless its closure has constant terms. A template helper with generic formals or a GenericCall Body independently triggers 23; a nongeneric helper without a G1 call does not. Check every Module's full retained closure, including uncalled views/defaults. |
| CG1 source production | One version-2 source-export file maps enum/wrapper TypeDefs and complete enum members plus public generic method spans against the same source snapshots. Preserve type/method aliases and reject cross-kind Module export-key collisions, stale snapshots and missing nominal inputs before output replacement; no composition of the old G1/enum CLI options is inferred. |
| Defaults and expressions | Literal, earlier-formal identity, arithmetic and checked factory defaults; interleaved required/defaulted type/constant slots; `_` cannot use defaults. Preserve helpers, already-bound calls and explicit arithmetic policy. Self/forward/captured-out-of-scope defaults and value graph cycles reject. |
| Independent methods | Uncalled public constant-only and mixed methods, three-module forwarding and recursion, source removed, consumer choosing actuals absent from the producer. Retain every original expectation, default and program dependency. |
| Malformed data | Wrong source/argument kinds, G1 tag 3 in CG1, GP DefId where a row is required, mismatched enum/member/wrapper owner, helper with ordinary parameters, bad widths/extents/offsets/ranges and unrepresentable counts reject without another decoder. Independently authored templates have exact `16 + 8*A` extents, including 16/24-byte zero/one-actual endpoints; budget-prefix/trailing bytes are invalid, not alternate framing. |
| Evaluation boundary | Previously checked forwarding runs no constructor; a new template occurrence performs required checks under the same root envelope. Unavailable Feature-12/program/target support, invalid construction, failure and exhaustion refuse formation, never runtime fallback or fabricated success. |
| Envelope ownership/selection | Instantiate one unchanged budget-free template under the consumer's default and explicit smaller/larger request envelopes; persist only each actual E. Explicit zero cannot become a default or infinity, equal inclusive limits remain valid, and no provider cap is inferred. Preserve existing concrete request envelopes on re-emission and require new selection only for a new occurrence. |
| Resource outcomes | Nested work does not replenish/reset the root; no automatic raised-limit retry or prior larger-envelope success may hide exhaustion under selected E. Preserve exact work/frame/storage charges, failure priority, cleanup/validity/result-length checks and encoding charges. A genuinely new explicitly selected request does not rewrite a prior failed request's envelope. |
| Transformation/replacement | Permute all global/Body indexes, duplicate immutable rows, strip SourceMap, pack multiple Modules, close and re-emit; preserve domain/value and every obligation. Reject changed domain/validity/default/target contracts and stale cached bindings. |

Adoption must connect these terms through source binding, the shared bound
tree/traversal, mandatory verification, Standard declaration/Body codecs,
independent references/providers, source-free remapping/replacement and
closed lowering. The accepted source contract is not reduced to whatever
subset first runs. General ConstEval/evaluation-read, concrete request,
intrinsic, witness, target materialization and runtime support remain named
independent prerequisites; their pending physical contracts must not be
filled from private tags or companion schemas by resemblance.

**No constant-term field, tag, ownership, ordering or reference choice is
left to implementation by this proposal.** Integrating this carrier does
not itself establish complete generic or constant-execution conformance.
