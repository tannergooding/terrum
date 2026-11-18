# Standard TRRM class constructor and method parameter defaults

> [!IMPORTANT]
> **Unapproved; provisional direction under the user-authorized
> missing-contract workflow, pending later review.** This document changes
> no accepted specification, registry or implementation. Provisional use is
> not formal adoption, stability or evidence of compiler support.

## Finding and decision

**The source rule exists; the ordinary-class declaration carrier does not.**
Recommend **CM1**: extend D1's existing MethodDef-owned parameter surface to
the retained ordinary-class callable closure, including constructor initializer
**I** and its complete-construction projection **P**. Reuse D1's closed SF1
value path and require P to project I's same parameter view. Add no default
expression language, runtime thunk, Param flag or constructor-plan operand.

The [specification index](../specs/README.md) reaches these owners. Code
references are inspected evidence, not newly executed validation:

| Owner/evidence | Existing contract and precise boundary |
|---|---|
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [calls](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms) | Ordinary parameters generally admit checked, publishable compile-time Copy defaults, including eligible aggregates. Constructors and ordinary instance/static methods are not exceptions. Required/defaulted parameters may interleave; omission selects the statically visible declaration's value at caller source compilation. |
| [Metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [logical constant sources](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) | Param is structural and MethodSig-owned. GenericParam defaults belong to generic formals. ConstantSource supplies values/required work but has no ordinary-class parameter ownership edge. |
| [Constructor declarations](standard-trrm-c1-constructor-declarations.md#complete-construction-projection-and-source-keys), [class publication](standard-trrm-c1-class-source-publication.md#required-constructor-and-member-closure) | Ordered parameters/defaults and complete member closure must survive; I/P roles, signatures and their association are specified. Neither proposal assigns the missing default edge. An already completed call is not a reusable declaration surface. |
| [D1](standard-trrm-interface-parameter-defaults.md#complete-parameter-surface) | The exact 16-byte `0066 MethodParameterSurface` carries requirement defaults and existing L1 exported/expected generic-method views. It explicitly excludes ordinary nongeneric exports and constructor I/P, including in its Construction-2 successor. |
| [GD1](standard-trrm-generic-dependent-parameter-defaults.md#decision-and-scope) | Adds scoped open terms to D1, explicitly **without adding owners**. Its Construction-2 identity therefore does not fill this gap. |
| [IO1](standard-trrm-c1-indexer-parameter-default-ownership.md#one-declaration-vector-projected-d1gd1-surfaces) | Supplies indexer/accessor correspondence, explicitly without expanding D1 to concrete nongeneric methods or constructors. It is not an ordinary class-method carrier. |
| [Aggregate defaults](aggregate-parameter-default-contract.md#persistence-interchange-and-native-execution) | Specifies structural freezing/materialization direction, explicitly assigning no Standard aggregate codec or declaration surface. Its wider source value domain does not fill the owner gap. |
| [Param reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmParamTableReader.cs), [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmParamTableWriter.cs), [label reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmMethodParameterLabelTableReader.cs) | Current Param rows are 16 bytes: owner/ordinal/type/mode/flags/reserved, with no default payload. The current label reader consumes 12-byte owner/ordinal/name rows, not D1's proposed surface. |
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [ordinary-default fixture](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ParameterDefaultBindingTests.cs), [schema facts](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | Source binding already has typed default expression/value paths and focused assertions; historical parser/binder observations in older proposals are not current support claims. Those facts and existing Construction-2/L1/R1 selections do not implement or select CM1. |

Thus implementing existing source rules is implementation work, but inventing
an ordinary-class serialization edge would cross a genuine contract gap.
This proposal supplies that edge only. For this route it supersedes L1/D1's
class/I/P owner exclusion; their existing identities remain unchanged.
MethodSig/Param still own the structural spine, not declaration defaults.

Scope is Construction-2's admitted ordinary nongeneric class closure and its
already admitted members. Method-generic members keep their existing generic
rules; a **closed** scalar default is not necessarily generic-dependent.
Generic classes, open default terms, richer value codecs, indexer ownership,
static initialization and new callable roles are not admitted here.

## One exact candidate agreement

Recommend this single **unapproved** whole-schema identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1`**

Its complete meaning is exactly
[Construction-2L1R1D1](standard-trrm-interface-parameter-defaults.md#whole-agreements-not-mix-ins)
plus CM1's owner/coverage and I/P projection rules below. Inherit D1's
interface/generic-export semantics unchanged, the reconciled **C1** catalog,
R1 receiver access, Construction `0003`, Feature 25, ED/00 and record 11.
Do not compose a G1-only decoder or CG1/GD1 term delta.

Keep `(1,0)`, governing edition 1 and Body encoding 01. Select the literal
ordinally and case-sensitively before schema-dependent decoding for the root,
**every supplied provider including unused inputs**, and retained
Clause/Origin/Plan inputs. Preserve the separate phase-1/phase-2 maps and
Construction-2's independent host/runtime admission. API defaults do not
select CM1.

No parent, sibling, D1/GD1/IO1, PI1/EN1, private or generic-constant agreement
is an alias. No additional composition is selected by this document.
Under [format versioning](../specs/representation/format-versioning.md),
equal version bytes, zero defaults, plausible strides or Features do not
select a decoder. Conversion must select both schemas and preserve decoded
meaning; changing a selector or appending zero words is not conversion.

## Declaration surface and exact wire contract

Reuse D1's candidate section **`0066=MethodParameterSurface`**, required
directory policy **`0000`**, fixed stride **16**, with no offset index/trailer:

```text
offset  0: u32 ownerMethodDef
offset  4: u32 ordinal
offset  8: u32 name
offset 12: u32 defaultValue
```

All words are little-endian. `ownerMethodDef` is a local typed MethodDef
row, `name` a String row, and nonzero `defaultValue` a ConstantSource row in
`0030`. These are one-based rows `1..00FF_FFFF`, high byte zero, bounded by
their actual tables; they are not DefIds, Imports, TypeSpecRefs, Blob rows
or Body-map indexes. The MethodDef partition supplies the logical Module.
`ordinal` is the full-width, zero-based **explicit logical Param ordinal**,
not a receiver, GenericParam, field or physical ABI position.

In addition to D1's existing owners, require the surface for every retained
MethodDef owned by an admitted ordinary nongeneric class TypeDef (kind 2)
in the parent's class declaration closure, excluding function-pointer
signature-only anchors. This structurally defined extension includes:

- Every ordinary instance/static member method and role-1 I, irrespective
  of visibility, export mapping, actual use or Body presence.
- Every retained role-2 P, private or explicitly exported.
- Their consumer-local expected copies. The bound provider is a separate
  view; its MethodDef row is not the expected owner.

This does not admit a previously unsupported callable/member role. Existing
admitted getter-only properties have no index Params and an empty accessor
vector; indexers/setter/init ownership still needs its separate agreement.
Synthetic helpers without authored defaults carry absence, except P's
explicit projection below. A surface grants no source lookup, access or
Export to a helper/accessor. No name-based role classifier is introduced.

For each owner, require exactly one row for every ordinal
`0..paramCount-1`, including required, unused and zero-data parameters.
Zero-parameter owners have no rows. The section is absent exactly when
there are no surface rows. Pairs `(ownerMethodDef, ordinal)` are unique;
extra/ineligible owners, missing coverage and even identical duplicate pairs
are invalid, not positional-only degradation. Export aliases share one
vector; distinct expected views never merge because their provider/key or
structural MethodSig agrees. Nonempty method-generic ranges remain exclusive.

Names retain [L1's decoded identifier and declaration-space rules](standard-trrm-g1-interface-parameter-labels.md#record-inputs-and-closure),
including distinct names within a view and ordinary formal-name collision
checks. Capture the primary's checked parameter names/defaults before
rewriting; partial secondaries cannot author another vector. Structural
nongeneric MethodSigs may still be shared across unrelated declarations.
Canonical surface order is MethodDef row then ordinal; ordinary readers
accept valid permutations with complete references.

`defaultValue=0` means **no ordinary default**, never an unknown or
unrepresentable value. Nonzero admits exactly D1's
**ConstantSource kind 1 → FrozenValueNode kind 1 → SF1 scalar Blob** path,
with [SF1's complete 24-scalar codec](scalar-frozen-value-registration.md#rows-and-blob-payloads).
The normalized complete root type must equal the Param type in that
declaration's context. Preserve nominal/qualification distinctions; do not
substitute one successful generic instance for an open declaration contract.
Unit has a nonzero source/node reference with zero scalar Blob reference;
it is different from absence.

Only an eligible ordinary value parameter may have a nonzero default:
ordinary mode `00`, no implicit-reference adaptation, and the complete
publishable Copy type/value contract. Reference/init loans, synthesized
assigned-value parameters and `params` initializers acquire no defaults.
ConstantSource 2/3/4/5, open formals/templates, native-width values,
enums/wrappers, aggregates and backing/object/address identities are not
enabled at this edge. Otherwise valid source defaults outside this selected
codec refuse retained publication; they do not become zero. An uncalled
declaration or explicit arguments at every observed call do not waive this.

Check `16*recordCount`, all ranges and the checked sum of complete owner
vectors; the surface table itself is limited to `00FF_FFFF` rows even when
many owners share one structural Param range. Do not narrow ordinals to
Body `u16`: global row limits and each Body's 65,536-entry map/Home/record
capacities are separate. No all-ones absent-index sentinel is introduced.

## Preserved source evaluation and failure semantics

CM1 proposes no source-rule replacement:

1. Bind/check a default in its primary declaration's lexical, access,
   type/constant and source-arithmetic environment. Runtime receiver or
   ordinary parameter values, caller locals and ambient mutable state
   are not captures. Invalid declared defaults diagnose even when unused.
2. Resolve declaration/access, map supplied positions/labels and perform
   ordinary overload/generic selection under
   [calls and inference](../specs/language/calls-and-inference.md).
   Defaults supply no inference evidence or new ranking rule. An omitted
   required position rejects; nontrailing omissions require the existing
   named-argument forms. Do not skip required positions or retry a constructor
   because its chosen default cannot form.
3. Perform required constant work under
   [constant execution](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation),
   including selected operations, checks, cleanup, validity and the actual
   resource envelope. Fixed work forms during Terrum compilation;
   admitted target-dependent work must finish before execution. This
   carrier publishes only completed SF1 values and their surviving
   obligations, not a deferred request. Generic-dependent timing/terms
   remain owned by GD1, not newly enabled by CM1.
4. An omission uses that selected view's formed value. An explicit argument
   suppresses that omission, not declaration checking. Materialize/copy
   the frozen value; never replay a constructor, parser, helper or validator.
   Preserve exact scalar bits, including signed zeros, NaNs and BID containers.
5. Capture the target/receiver once, then stage written arguments once in
   source order. Supply omitted constants in Param order before invocation
   Check/Prepare/Invoke/Publish. All logical arguments are explicit in the
   lowered call; default rows introduce no sentinel, hidden input or ABI.

Failed formation, unsupported required representation/effect, resource/tool
failure, invalid source and incompatible binding remain distinct refusals,
not absence, zero, another overload/provider or runtime fallback. Preserve
ordinary every-exit staging/ownership and construction failure ordering.
No new unwind/rollback guarantee, incomplete-parent Cleanup, output state,
caller-unsafety exemption, rooting or lifetime extension follows.

## I/P correspondence and existing ConstructorBinding

Within **each declaration view**, P's surface is a projection of its exact
role-2-selected I, not an independently authored default source:

- I and P have separate MethodDefs and their parent's separate
  MethodSig/Param rows. Map explicit ordinal q to q, with identical ordered
  complete types, modes/adaptation flags, labels and default presence/value.
  Neither I's receiver nor a Boolean/out `TryCreate` parameter participates.
- Compare values by normalized complete type, codec and exact payload bits,
  not row equality, numerical equality, source text or reevaluation.
  Equivalent shared/duplicate immutable value rows are permitted.
- Require consistency for bodyful actual pairs and bodyless expected pairs,
  including private P and partial-constructor implementations. P adds no
  independent default formation occurrence. If no valid P exists, retain I;
  defaults cannot justify fabricating P, omitting final validation or
  publishing an incomplete object.
- P receives already staged full arguments and forwards them once to I.
  Do not fill defaults again at P→I or after BeginNormal. All construction,
  access, unsafe/effect, failure and completion obligations remain inherited.

Keep assigned [Body record `0B=ConstructorBinding`](../specs/il-instruction-set.md#typed-operand-records)
**byte-for-byte in shape and meaning**: zero **supplied** arguments is not
zero formal parameters. Its target is real P associated with selected I;
kind-1 rows name the already selected defaults through that Plan Body's
ConstantMap in formal order. Require complete formal coverage and exact
typed value agreement with the **original selected expected P/I view**.
Unit retains its binding row and assigned source-use rule. Empty `params`
remains kind 2 with its independently admitted codec, not an ordinary default.

No new `0B` field, kind, runtime constructor lookup or allocator is needed.
Existing zero-formal-only execution limits remain limits until independently
implemented; CM1 does not turn metadata or a binding round trip into
Construction-2 runtime support.

## Binding, replacement and source-free preservation

Resolve Module/revision/key/kind and class/member correspondence before
[directed satisfaction](../specs/representation/binding-and-replacement.md).
Keep each original expected surface separately from its selected actual.
Differences in labels/default presence/value alone do not reject otherwise
compatible binary binding: old calls pass complete arguments already chosen
from their expected view. Fresh source compilation can see the new provider
default; removing it can invalidate a new omission. Other caller-domain,
declared behavior, I/P role/association and dependency obligations still check.

Thus expected I/P can consistently carry 7 while actual I/P consistently
carry 9 or absence. An actual I carrying 7 paired with its own P carrying 9
is invalid. A retained `0B` using expected 7 must not be rechecked against
actual 9 as though it were a new source omission. Interface calls still use
the requirement's D1 surface, not this concrete implementation surface.

Packing must preserve the expected P→expected I graph and original surface
when resolving execution to actual P/I. If the selected records cannot
retain an unequal source view after an Import-to-direct rewrite, keep the
existing expected MethodDef proxies/Imports and their MethodMap selection;
do not erase them or invent a new context tag. Eliminating a source-view
proxy requires complete source-view equivalence, including equal labels,
presence and exact typed values, plus every other retained obligation.
Check every alias/expectation and stronger fact relied on, not first-view wins.
Private direct I/P edges retain their parent's provider-owned closure/origin
rules. If no admitted binding can preserve that view through replacement or
packing, refuse the operation; do not manufacture private export keys.

Producers retain truthful checked defaults, complete class/member/Body
closure, original access principals and surviving type/value/layout/target/
provider dependencies. A frozen value needs no private formation Body solely
for replay; independently retained evaluation contracts/programs still
survive. Unsupported closure refuses atomically, never a partial artifact.

Consumers check schema/support, extents, namespaces, owners, total coverage,
ordinals/names, eligibility, scalar node/Blob validity, complete type agreement,
I/P consistency and required bindings before affected exposure/use.
[Producer truth and consumer checks](../specs/representation/reader-trust.md)
remain distinct: consumers need not reexecute defaults or re-prove arbitrary
producer Bodies/formation facts.

Source-free re-encoding remaps MethodDef/TypeDef/Module origins and partitions,
MethodSig/Param ranges, Strings, `0066` source references, ConstantSource/
FrozenValueNode/TypeSpec/Blob graphs, Imports/Exports and all retained
contracts/dependencies. Remap P's typed I reference, member guarantees,
Body maps, `0B` ConstantMap indexes and original phase-1/phase-2 plan contexts
together. Clone context-dependent rows/views where sharing would change
meaning. Rebuild directories, ranges, Body/record/code offsets and affected
SourceMaps; scalar payload bits are data, never reference-like octets.
Source/debug stripping must still allow an unseen admitted omission.
An earlier schema that lost a default cannot reconstruct it without source.

## Allocation alternatives, requirements and integration

CM1 adds **no numeric assignment**: retain candidate `0066`/stride 16 and
Feature `26/1` from D1/L1, existing `0030`/`003F`/Blob namespaces and all
instruction/record bands. Its changed owner set is mandatory whole-schema
meaning, not a Feature-26 decoder switch.

The accepted [registry](../specs/representation/registry.md) assigns semantic
sections through `004B`; related candidates reach `0066`. Reuse is not
required by draft compatibility: unpublished layouts/IDs may be replaced.
Section kinds are `u16`, semantic space is below `8000`, and DefId kinds
are a separate `u8` namespace. This non-DefId auxiliary table needs no new
definition kind, token, prefix/escape or reduced index domain.

| Complete carrier alternative | Capacity/size and disposition |
|---|---|
| **Extend D1's complete vector to class closure; selected** | Four words per logical position; adds four bytes to each L1 label row and uses the same typed references/24-bit row limits. I/P duplicate the vector intentionally with checked correspondence. No additional association or allocation. |
| Keep 12-byte L1 labels and add sparse candidate `0067 MethodParameterDefault`, required policy `0000`, rows `u32 ownerMethodDef, u32 ordinal, u32 constantSource` | Viable with the same class label-owner extension, unique eligible pairs, nonzero value references and schema-defined missing-pair=absence. A new required section/validator/index is needed, but no DefId. For N total surface positions and D defaults, costs `12*N + 12*D` versus `16*N`, excluding common value graphs/directory overhead; sparse saves row bytes when `D < N/3`. Both tables retain independent 24-bit row limits; no universal size win is claimed for the selected route. `0067` is an unselected candidate, not a reservation or accepted allocation. |
| Append `u32 defaultValue` to Param, stride 20, retaining separate labels | Representable only after splitting any shared nongeneric MethodSig/Param vectors whose declaration defaults differ, with complete owner/context remapping and increased signature/Param consumption. Defaults become structural storage; labels still need the class owner extension. This adds no capability over the existing declaration-owned field. |
| Replace the family with declaration-owned Params carrying names/defaults, e.g. stride 24, and redesign signature sharing | Coherent substantive redesign: replace `ownerMethodSig` by MethodDef ownership and append name/default words, then rebuild MethodSig ranges, all anchor/signature uses and comparison rules. Can avoid a side table, but changes unrelated structural sharing and every Param consumer. Not justified for this owner gap. |
| Runtime thunk, Body-only constants or optional/debug sidecar | Not equivalent. A thunk changes timing/effects/ABI; completed Body arguments cannot supply a reusable declaration default; skippable data cannot own required source semantics. |

In each retaining Module extend `26/1`'s label-owner trigger to the new class
owners, **including zero-parameter/bodyless/uncalled owners**. Nonzero frozen
defaults additionally require `11/1`; absence alone does not. Keep
`26 -> []`, `11 -> []`, independent method-generic `23/1`, Construction-2
`25/1` and every inherited C1 catalog/conditional requirement. No new
`26 -> 11/23/25` prerequisite, evaluation-form grant or artifact-wide Feature
union is introduced. Bodyless I/P alone still do not trigger 25.

On integration, amend metadata/registry surface ownership and the L1/D1
owner scope **for this successor**, constructor/class publication's
parameter-fact mapping, and binding/verification/re-encoding consumers.
Carry the selected view and typed default/absence through bound declaration
capture, traversal/rewriting, verification, import reconstruction and lowering.
No new source grammar, runtime/library API or public C# input record is needed.

For the existing [class-source CLI transport](standard-trrm-c1-class-source-cli-transport.md#exact-cli-and-schema-admission),
pair its unchanged **version-1** `--trrm-source-class-exports` file explicitly
with this literal on integration, using existing `--trrm-schema` selection.
Keep all source anchors, three mapping lists and explicit I/P keys; defaults
come from checked declarations, never JSON overrides. Add no map version,
automatic promotion, G1 map pairing, PI1/EN1 composition or machine-protocol
field. Operations not explicitly routed to CM1 refuse it.

## Focused acceptance and remaining boundary

These are required future cases, **not results**; no builds/tests are run
for this proposal:

| Cases | Required outcome |
|---|---|
| Unrelated ordinary immutable/mutable classes; constructor plus instance/static/private methods; partial primary/implementation; omitted and explicit scalar arguments | Real class/member closure, once-only source-order staging, correct selected defaults, exact access/effects and unchanged construction phases. Explicit arguments do not excuse invalid declared defaults. |
| Required/defaulted positions interleaved, named reordering, zero parameters, unit required versus unit default, shared structural signatures and export aliases | Total declaration-owned vectors; receiver excluded; no shifted/skipped required positions, duplicate rows or absence/unit collision. Defaults do not distinguish overload signatures. |
| I/P defaults 7/7; malformed 7/9 or present/absent pair; no P; private/exported P; `0B` zero supplied with nonzero formal count | Preserve q→q projection and original plan values; reject inconsistent pairs/coverage and I-as-P/TryCreate substitution. No second default evaluation at P→I, fabricated P or empty-params reinterpretation. |
| Expected I/P 7/7, provider 9/9 or no defaults; interface requirement 5 implemented by concrete member 9; later provider replacement | Old omissions/plans retain their original values, new source sees its selected view, witness calls use 5, and other incompatible contracts still reject. Unequal expected proxies cannot be erased. |
| Every SF1 scalar, unit, endpoints, signed zeros/NaNs/BID containers; exact bit differences between I/P; equivalent row/F2 types and duplicate immutable value rows | Exact typed bits survive; equivalent encodings pass, unequal representations fail same-view projection consistency. No numerical-equality shortcut or producer replay. |
| Wrong namespace/high byte/zero required reference, bad mode/type/Blob/boolean/rune, duplicate/partial/foreign-owner surface, bad stride/count multiplication | Refuse before affected exposure/output. Explicit calls and unused declarations do not excuse malformed retained metadata. |
| Last 24-bit rows, ordinal 0/65535/65536 where complete ranges fit, oversized total owner coverage, Body index 0/FFFF and overflow | Check each independent capacity without narrowing, wrapping or reserving FFFF. Global metadata capacity does not promise a corresponding Body encoding/execution route. |
| Closed SF1 default on an admitted method-generic member versus dependent, aggregate/native-width/nominal/backing defaults | Admit only the exact closed value path; retain otherwise valid source contracts as unsupported publication gates, never erase or freeze one specialization. No generic/interface/indexer redesign is inferred. |
| Source/debug removed; non-first provider; several Modules; permuted/reindexed MethodDef/Param/String/value/Blob rows; packing and re-emission | An unseen admitted omission and old `0B` plan reconstruct correctly; retain original views/dependencies and remap both phase contexts. Scalar opcode-looking bytes remain unchanged. |
| Formation/check/resource failure, later written-argument failure, constructor/validation failure, incompatible replacement or unsupported target | Preserve actual failure/cleanup/publication policy; no runtime default, retry, partial output, incomplete-parent Cleanup or implicit runtime admission. |
| Exact CM1 versus every parent/sibling/private input, empty maps and unused mismatched providers; missing per-Module 11/26 or independent 23/25 | Explicit whole agreement and complete retained requirement closure; content never chooses another decoder. |

CM1 leaves no owner/ordinal/closed-SF1/I/P correspondence choice for the
implementer. Formal adoption, implementation and qualification remain open.
GD1 terms, aggregate/nominal/native-width codecs, IO1 accessors, PI1/EN1
composition and independently unsupported member/runtime closure retain
their own gates. Settling this declaration gap is not acceptance of the
whole Construction-2 producer or the active conformance milestone.
