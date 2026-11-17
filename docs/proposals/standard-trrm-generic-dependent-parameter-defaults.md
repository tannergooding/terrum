# Standard TRRM generic-dependent ordinary parameter defaults

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user-authorized
> missing-contract workflow, pending later review.** Provisional implementation
> may follow this direction; that permission is not formal approval, adoption
> or stability. This document changes no accepted specification, registry or
> implementation. **No registry assignment is made here.**

## Decision and scope

Recommend **GD1**, an explicit composition of the
[D1 ordinary-default surface](standard-trrm-interface-parameter-defaults.md#candidate-standard-carrier-and-exact-selection)
with [CG1's shared constant terms](standard-trrm-constant-generic-methods.md#wire-contract).
Keep D1's declaration-owned field; admit a scoped constant-formal source
or a checked expression template when the default cannot yet be frozen.
Do not add an ordinary-default expression language, runtime thunk or
specialization cache masquerading as the declaration.

The accompanying **unapproved source-semantic amendment** is:

> Bind an ordinary default in its declaring environment. All declaring-owner
> and method generic formals are available, independently of the ordinary
> parameter's ordinal. After ordinary generic selection, an omission selects
> that bound term at caller source compilation. Perform required constant
> formation when its necessary generic and target inputs are established,
> before execution. Preserve the selected term across open callers; never
> reselect an implementation's or replacement provider's default.

This supplies D1's explicitly deferred dependent-default contract, not a
replacement for its closed-value slice. D1 continues to own eligible ordinary
positions, absence, complete parameter surfaces, labels, requirement ownership,
and ordinary expected/provider differences. GD1 adds the open term's scope,
formation boundary and exact schema composition. It adds no publication
owners, source syntax, inference source, value codec, witness ABI or runtime
default-argument mechanism.

## Accepted owners and inspected implementation

The [specification index](../specs/README.md) leads to these actual owners:

| Owner | Established fact and boundary |
|---|---|
| [Grammar §3.1/§3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments) | `ParameterBinding = ParameterType, Identifier, [ "=", ConstantExpression ]` applies generally, not only to interfaces or nongeneric methods. `ConstantExpression = Expression`; interface functions reuse the ordinary function/parameter productions. This is syntax subject to semantic checks, not permission for every expression. |
| [Generic scope](../specs/language/names-and-binding.md#generic-formal-names-and-scope) | Type/constant formals are in scope throughout the declaration's signature. Constant formals are declared constant values, not borrowable runtime storage. Scope alone does not establish an open ordinary default's publishability. |
| [Ordinary-default rule in §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [calls](../specs/language/calls-and-inference.md#argument-mapping-and-call-forms), [CARG-10/11](../specs/conformance/source-calls-and-inference.md) | Ordinary defaults are checked, publishable compile-time Copy values, including eligible nominal aggregates; the caller's source compilation selects the omitted value. No reference/initialization-loan default follows from this rule. |
| [Generic completion](../specs/language/calls-and-inference.md#expected-result-defaults-and-contextual-completion), [constant generics](../specs/language/constant-generics.md#formation-binding-and-forwarding) | **Generic-formal** defaults have earlier-formal dependencies and participate in the defined generic completion process. Ordinary parameter defaults have a wider value domain; these are not the same default facility. |
| [Substitution](../specs/language/types-and-normalization.md#forward-substitution-and-normalization), [witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity), [WIT-20](../specs/conformance/source-interface-witnesses.md) | Preserve complete terms and exact requirement identity. Substitute owner arguments before corresponding method formals. Interface callers use the selected requirement's labels/defaults, not implementation names/defaults. |
| [Constant execution](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation) | Fixed work freezes during Terrum compilation; required target-dependent work can remain for target JIT/AOT compilation, never runtime. ConstEval is not all-input success or termination. This does not itself specify ordinary-default deferral over unbound generic inputs. |
| [Metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [logical generic records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending), [constant-domain records](../specs/representation/logical-records.md#constant-generic-domains-encoding-pending) | Param is structural; GenericParam defaults are kind-directed. Assigned ConstantSource tags 1/2/3 are value/request/target-binding sources, not an open ordinary-default term contract. Physical eligibility and evaluation gates remain real. |
| [Schema selection](../specs/representation/format-versioning.md), [admission](../specs/representation/requirements-and-admission.md), [reader trust](../specs/representation/reader-trust.md) | Select one exact artifact schema out of band. Check structure, references, support and bindings before affected use. Producer truth does not require consumers to semantically re-prove arbitrary bodies or invariants. |

**The accepted owners do not explicitly settle whether an ordinary default
depending on type/constant formals may remain open until substitution.**
The signature-scope and `ConstantExpression` productions permit writing the
relevant names; they do not decide that timing boundary. Conversely, the
earlier-formal rule for generic-formal defaults is not an ordinary-Param
prefix rule. The amendment above is proposed, not reported as already accepted.
Read D1's provisional dependent direction and its explicit encoding gate
together; proposal prose supplies no independent normative acceptance.

The following are current source observations, not newly executed tests:

| Inspected code | Actual retained information or rejection |
|---|---|
| [`Parser.ParseParameter` / `ParseGenericFormal`](../../sources/Terrum.CodeAnalysis/Syntax/Parser.cs) | Ordinary parsing stops after the parameter identifier, without consuming `=` or an initializer. Generic formals separately parse type/constant defaults. Lossless tokens do not constitute a bound ordinary default. |
| [`BoundInterfaceParameter` / `BoundInterfaceFormal`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Ordinary interface parameters retain name, type, mode and attributes only. Generic formals separately retain `TypeDefault` / `ConstantDefault`. |
| [`Binder.Parameter` / `BindArguments`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Ordinary signature parameters have no default value/term. Binding requires the full supplied count and reports every unsupplied named position as required. |
| [`BindInterfaceParameter`, invocation and proof stubs](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) | Parameter binding retains no ordinary initializer. Invocation overlays requirement names on the target's parameters, not a separate default term. These paths need the declaration view before target adaptation, not another provider-signature overwrite. |
| [`GenericParameter`, `GenericTypeParameter`, `GenericMethodConstantFormal`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs) | Generic ordinary parameters retain type terms, not defaults. Type/constant **generic formals** separately retain default syntax and formal-dependency facts; that machinery is not an ordinary-default carrier. |
| [ConstantSource codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceTable.cs), [template codec](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantTemplateTable.cs), [schema facts](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | Current tags 4/5 and the `16 + 8*A` template codec are gated to exact `GenericConstantMethodsDraft`. The table checks include reference bounds and helper structural shape/arity; they do not establish this ordinary-default use context, its full formation or a G1/C1 composition. The source codec now rejects 2/3 under that exact CG1 selection. |
| [Frozen nodes](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs), [scalar builder](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenScalarValueBuilder.cs), [value validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) | There are scalar and selected enum/CG1 wrapper leaf paths. `GetFrozenScalarPayload` requires source kind 1; it is not an interpreter for 4/5. These codecs cannot freeze an unbound formal or expression merely because its eventual result is scalar. |

Adjacent [interface tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/InterfaceDeclarationBindingTests.cs)
exercise interface generic-formal defaults/forwarding;
[generic-owner tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Generics/GenericNominalTypeTests.cs)
exercise owner constants feeding **method generic-formal** defaults.
Neither establishes ordinary omitted-parameter support. The
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
excludes neither fixture. The [implementation inventory](../implementation.md#bounded-generic-functions)
is context, not a substitute for these code distinctions.

## Alternatives

| Route | Assessment |
|---|---|
| Require every default to be closed before publication | A coherent bounded interchange restriction, as D1 explicitly applies; reject an unrepresentable retained declaration atomically. It does not represent an independently reusable open default. Making it a source prohibition would require an intentional source amendment, not an inference from absent metadata. Publishing only observed closed specializations is not equivalent to publishing the generic declaration. |
| **Compose declaration-scoped ConstantSource 4/5 with the complete G1/C1 successors and CG1 delta** | **Recommended.** One existing D1 field selects a typed value, formal or bound program. Reuse the already specified helper/program, nominal-domain and reference machinery, while defining the new ordinary-default context explicitly. No source reconstruction or second expression evaluator is needed. |
| Replace ConstantSource with a unified declaration-owned ConstantTerm AST/table | Materially coherent, and unpublished IDs may be reassigned. It could put owner/result/operation operands directly on terms, but would also replace frozen/default/TypeSpec/ConstantMap/GenericCall edges, operation identities, dependency rules and remapping. It provides no missing scope or evaluation capability that the explicit shared-source composition cannot express. Not justified for this gap. |
| Put a thunk, ordinary arguments or a result from one specialization in the default field | A runtime thunk changes constant selection/effects/ABI. Capturing ordinary arguments changes the source constant domain. One cached result loses unobserved generic instances. None preserves the required declaration contract. |

Reusing 0030/0064/0065/0066 is a design choice, not an add-only allocation
constraint. Renumbering or replacing unpublished families is permissible.
No retired layout, tag spelling or schema name is kept as an alias.

## Declaration context and candidate source rule

### Exact ownership and formal coordinates

For a surface entry, let **V** be its selected declaration view MethodDef,
**q** its ordinary Param ordinal, **O** the declaring TypeDef's complete
mixed formal vector (empty for a module-level function), and **M** V's
complete mixed method-formal vector.

* The default belongs to **(V, q)** in V's logical Module. Its structural
  parameter is `MethodSig(V.signature).paramFirst + q`, with the existing
  exclusive Param range and matching ordinal. Shared nongeneric MethodSigs do not
  merge declaration defaults. Nonempty method-formal ranges retain their
  MethodDef exclusivity.
* Each generic formal is identified by its resolved declaring TypeDef or
  MethodDef, kind and zero-based ordinal. Owner ordinal 0, method ordinal 0
  and ordinary Param ordinal 0 are three different coordinates. A Param row
  or q never selects a GenericParam.
* The ordinary-default environment is **all O, then all M**, each block in
  its original mixed declaration order, including unused/defaulted formals.
  It is not the first q generic formals, all types followed by all constants,
  or only formals inferable from written ordinary arguments.
* An ordinary omission supplies no inference evidence. Finish owner binding,
  then method inference/explicit selection/generic-formal defaults under
  their existing rules. Every method slot must be established before applying
  an ordinary default; an established term may still refer to an open
  caller's legitimate formals.
* No receiver **value**, ordinary parameter value, caller local, later
  ordinary default, ambient mutable state or missing generic actual can be
  captured. Even a written constant argument does not turn its ordinary
  parameter into a constant formal. A self **type formal** is not a receiver
  value.

For an interface invocation, V is the **exact selected requirement view**,
including its closed or symbolically substituted declaring-interface
instance, inherited identity and original expected contract. It is not the
implementation MethodDef. First substitute that interface owner's arguments;
map corresponding method formals by kind/ordinal and matching arity. Preserve
qualified-self substitutions only where the parent admits their complete
evidence. Do not rewrite unrelated occurrences of a concrete type.
Implementation/provider views have their own O/M and default surfaces;
adapting a Body does not transplant either view's term into the other.

### Declaration validation versus new formation

At declaration checking, bind names, operations, conversions and calls in
the original lexical/access environment. Check the complete parameter type,
Copy eligibility, effects/forms, constraints, required published value/layout
contracts and absence of forbidden captures. Check the open term against the
full declared generic domain, not successful samples. Closed required-constant
obligations still form normally, even in an unused declaration or when all
observed calls supply the parameter.

An open term need not be proven terminating or successful for every generic
value: ConstEval does not promise that. A structurally/type-correct checked
addition can fail for a particular substituted input. That selected omission
then fails required formation. An explicit argument suppresses that occurrence's
formation, not declaration checking or retention of its published default.

Illustrative declaration, using accepted initializer spelling but the
**proposed dependent timing rule**; the current ordinary-parameter parser
does not support it:

```terrum
public int32 Adjust<T, const int32 N>(int32 value = N + 1)
{
    return value;
}
```

Here q is 0 and N's method-formal ordinal is 1. Once T and N are selected,
an omission forms the checked expression; it cannot infer either formal.
In Development/Debug, N = int32 maximum makes that omission fail; an explicit
value does not run the default. A Release-bound wrapping operation retains
its selected modulo semantics, not the consumer's current build profile.

### Dependent result type and publishability

Let **S** be the established, capture-avoiding owner-then-method substitution.
The required result type is **D = Normalize(S(Param.typeSpec))**. Apply the
same substitution to the source's declared result/domain; require exact
complete normalized equality with D, not assignability, equal carrier width,
unqualified spelling or host numeric equality. Any authored conversion is
already an operation in the bound program.

Before closure, retain the complete dependent result tree. For example, an
eligible fixed-int32-carrier `Tagged<T>` keeps T in its nominal identity;
T is not erased because the payload width is fixed. The declaration must
establish its open Copy/validity/layout/codec commitments from published
contracts. Copy or Blittable alone, or a few successful T substitutions,
does not establish the missing value codec or a constant-formal domain.

GD1 adds no frozen-value shapes: formed results use D1's SF1 scalars or the
enum/Transparent-wrapper leaves **explicitly selected by the CG1 composition**.
The latter are not retroactively D1 values. Retain all CG1 nominal eligibility
and validity obligations. A floating ordinary result can use SF1 even though
floating values cannot be constant formals. General aggregate/native-width
or backing-bearing results still need their own admitted codecs; otherwise
publication/use is unsupported, not a zero default or a source prohibition.

## Shared term representation

Keep D1's required **0066 MethodParameterSurface**, stride 16, and its
`defaultValue` field at offset 12 unchanged. Zero still means absence.
Nonzero is R(ConstantSource), **not** an ordinary Param, GenericParam,
FrozenValueNode, Blob, TypeSpecRef or Body-map index.
Keep D1's owner set, total vectors and other validity rules.

Section/DefId codes are hexadecimal; Feature IDs, lengths, counts and
ordinals are decimal. All multibyte fields remain little-endian.
R(T) is a one-based u32 row in
table T, high byte zero, bounded by the actual table count and
`1..00FF_FFFF`. Preserve complete TypeSpecRefs' separate row/F2/F3 grammar.

| Default source | GD1 interpretation |
|---|---|
| 0030 kind 1 → R(FrozenValueNode), 003F | Already formed typed value, under the explicitly selected SF1/CG1 value codec and complete dependencies. No producer-body replay. |
| 0030 kind 4 → R(GenericParam), 0019 | Direct reference to an in-scope **constant** formal in O or M. Its substituted `constantTypeSpec` is the domain; read its established actual. Never recursively consult `defaultArgumentRef`. |
| 0030 kind 5 → R(ConstantTemplate), 0064 | Required bound expression program, with its dependent result type, exact/deferred target and complete captured actual vector as below. Not an executed request. |
| 0030 kinds 2/3 | **Not admitted directly in the ordinary declaration-default field by GD1.** Their request/completed-target meanings elsewhere remain schema-owned; neither is an open term or alias for 5. |

These are an explicit new **use** of CG1's 4/5, not a claim that linking its
proposal enables them in D1. Retain the eight-byte 0030 row
`u32 kind, u32 payloadRef`; add no context discriminator there. The retaining
edge supplies the context:

| Retaining position | Formal scope |
|---|---|
| GenericParam's generic-formal default at mixed ordinal r | Existing CG1 earlier-formal scope; method defaults have O plus method ordinals `< r`, TypeDef defaults only earlier owner formals. |
| **MethodParameterSurface ordinary default at Param ordinal q** | **Entire O followed by entire M; q does not truncate it.** |
| Constructed-TypeSpec constant slot, GenericCall constant actual, Body ConstantMap | CG1's corresponding using/caller/Body scope and complete kind/domain checks, not either default-prefix rule. |

The same row can be shared only if its resolved meaning and every contextual
scope obligation agree at all uses; otherwise clone/remap it. Global placement
does not supply a Module or authorize free captures.
Otherwise well-formed unreferenced 1/4/5 rows remain permitted under CG1;
do not invent their using Module or derive Feature 24 from orphan placement.

### Expression lifting and helper boundary

Reuse CG1's **exact** offset-indexed 0064 row:

```text
u32 selectedMethodReference     // MethodDef DefId 16 or MethodDef Import 1C
u32 resultTypeSpec              // complete type in this use's environment
u32 targetProfileRef            // R(TargetProfile), or zero to defer selection
u32 argumentCount = A
A * { u8 kind, u8 flags=0, u16 reserved=0, u32 actualRef }
```

Extent is exactly **16 + 8*A**. Kind 1 selects a complete TypeSpecRef;
kind 2 selects R(ConstantSource). No envelope, ordinary argument, owner word,
budget prefix or trailer is added. A MethodDef token in the first field
does not change row-only references in the other tables.

For this **ordinary-default** use, lift the checked expression into a
module-level static ConstEval helper with **no receiver, no ordinary
parameters**, and no defaults on its generic formals:

1. Capture all O followed by all M as the helper's method formals. Copy their
   kinds, domains and required constraints/contracts with explicit
   capture-avoiding mappings. Owner formal i maps to helper ordinal i;
   method formal j maps to helper ordinal `O.Count + j`.
2. Rewrite the already-bound result type and Body references into that helper
   environment. Its result, after applying the template actuals, must equal
   the template result and D. Preserve constant loads through tag-4
   ConstantMap entries and selected operator/call/witness identities.
3. On the declaration's template, supply one identity actual for every
   captured slot, including unused slots. A later use substitutes those
   actuals; it does not bind source again. A helper-local formal must never
   be read in V's scope, or V's formal directly in the helper's Body.
4. Preserve the expression's original semantic access principal and checked
   effects. Lifting grants no new file/friend/private access. Retain required
   access records if the selected expression needs them; missing records
   refuse the route. The generated helper is not automatically public or
   independently source-exported.

CG1's helper for a **generic-formal default** still captures only its allowed
earlier prefix. Changing it globally to this full vector would admit forward
dependencies and is not proposed. Existing CG1 expression actuals keep their
own caller environment.

The helper is a real checked program, not a throwing stub, syntax Blob,
signature anchor, arbitrary Pure-only body or evaluator recognized by name.
An expected MethodDef stays bodyless; retain a separate real helper definition
or bind its exact admitted implementation dependency. Private program closure
can be packed with preserved Module identity; do not invent a private export
key or serialize cross-artifact row numbers to obtain it.

## Formation phase, failures, resources and staging

After mapping supplied arguments and ordinary generic completion, unique
selection chooses V and its omitted positions. A default's value, ability to
evaluate cheaply, failure or range does not rank overloads or select a
different witness. Perform complete default validation after unique selection
under the [existing call phases](../specs/language/calls-and-inference.md#applicability-and-best-candidate).
A formation failure diagnoses that selection; it does not retry another
overload, default, provider, source tag or runtime path.

* **Open caller:** substitute into the caller's checked formal environment,
  retain that occurrence's selected V/term/actuals and required closure.
  When more inputs become available, continue substitution of that term.
  Closing a role-2 witness never refreshes the default from its target.
* **Concrete generic and fixed target inputs:** a new expression occurrence
  performs required constant execution during Terrum compilation. Formal/
  previously frozen value forwarding performs no new constructor.
* **Remaining target input:** retain required typed work with exact query,
  layout, value ABI and binding dependencies until the admitted target
  JIT/AOT stage. Zero template target means deferred selection, never host,
  wildcard or permission to substitute a different target. A nonzero target
  is an exact retained constraint. Formation must finish before execution;
  shared generic execution cannot turn it into a runtime dictionary query.

Apply the accepted
[formation domain and executed-path rules](../specs/language/proof-and-constant-evaluation.md#constant-execution-domain):
new construction checks its actual program, pre/postconditions, initialization,
cleanup and value validity. Unsupported reached effects, absent programs,
failed checks, arithmetic failures, invalid results, unavailable targets and
resource/tool failures remain distinct refusals. Unselected known runtime-only
operations are not executed; malformed instructions/types/references do not
become valid by lying on an unselected edge.

Use CG1's [budget-free template rule](standard-trrm-constant-generic-methods.md#concrete-request-envelope).
For each genuinely new root formation occurrence, select and record the
consumer's actual envelope E **before** execution. No provider cap or saved
successful instance selects E. Explicit smaller/larger/zero ceilings keep
their actual meaning; no automatic raised-limit retry hides exhaustion.
Use the accepted inclusive work/frame/storage/result limits, exhaustion
priority and encoding/cleanup charges. Source and imported dependent-default
routes use the same lifted program semantics: count its entered helper
frame/slots and semantic actions even if physical lowering inlines it.
Do not make the synthetic helper free or reset accounting at nested calls.
Inside an enclosing constant request, nested default/template evaluation
shares that root's cumulative ledger rather than obtaining a free new root.

Evaluate distinct omissions in ordinary Param ordinal order for formation
occurrence ordering; sharing an immutable term does not merge occurrences
or independently authored constructions. Retained concrete requests preserve
their recorded E; only a genuinely new occurrence selects a new envelope.

There is **no runtime evaluation of a default expression**. The runtime
argument plan captures the receiver once, stages written expressions once
left-to-right in source order, then supplies the formed Copy constants to
their omitted logical positions before invocation checks/preparation.
Named mapping does not reorder written effects. Preserve D1's cleanup,
authority, precondition and initialization/output rules; no default creates
vacant storage or caller authorization. Every lowered call passes its
complete logical argument list explicitly.

Completed values keep their typed bits and surviving formation/target/provider
obligations. A genuinely completed target-independent value can use kind 1
where its selected codec admits it. This does not erase the open declaration
or let one instance replace its term. Persisting a concrete request or completed
target binding elsewhere requires that schema's complete records. Template
actual rows are **not** ConstantExecution substitution rows: do not copy them
into an incompatible 005E layout. The
[mixed-substitution successor](constant-execution-mixed-substitutions.md)
is not selected here. If the parent's exact concrete-request carrier cannot
represent all surviving inputs/obligations, refuse that output operation;
retaining an open template does not invent the missing request codec.

## Exact complete candidate schemas

GD1 is a document shorthand, not an on-wire selector or independently
selectable mix-in. The following **unapproved exact identities** select one
complete agreement each:

| Candidate | Exact candidate identity |
|---|---|
| G1L1R1GD1 | `trrm/1-draft-generic-method-calls-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1` |
| C1L1R1GD1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1` |
| C1M1L1R1GD1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1` |
| Construction-1L1R1GD1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1` |
| Construction-2L1R1GD1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-generic-dependent-parameter-defaults-1` |

Each recipe is **its corresponding complete
[D1 successor](standard-trrm-interface-parameter-defaults.md#whole-agreements-not-mix-ins)
+ CG1's Standard enum/domain/constant-term delta + this ordinary-default
context**, with the following explicit precedence:

1. Inherit the corresponding
   [L1 labels](standard-trrm-g1-interface-parameter-labels.md#exact-selection-allocations-and-alternatives)
   and [R1 receiver](standard-trrm-method-receiver-access.md#exact-profile-consequence)
   successors through D1, not a label-free G1/C1 decoder. Keep D1's 16-byte
   0066, not L1's old 12-byte label row. Preserve each class/construction
   stage, separate phase-1/phase-2 maps and original expected/provider contexts.
2. Select **all** CG1 Standard enum replacements, 0065 wrapper contracts,
   0030 kinds 4/5, exact 0064 templates, nominal leaves, contextual rules and
   Feature 18/24 rules. Do not import CG1's G1 base a second time or any
   private enum/generic encoding.
3. Apply CG1's GenericCall override **throughout the artifact**: constant
   actuals use `argumentKind=2` and R(ConstantSource), including forwarding.
   G1's `argumentKind=3` is invalid, not a second alias. Generic-formal
   defaults and TypeSpec constant slots retain their CG1 semantics; only
   the new surface edge receives the full O/M ordinary-default scope.
4. For **G1L1R1GD1**, retain the
   [CG1 admission amendment](standard-trrm-cg1-constant-source-admission.md#cg1-constantsource-and-section-rules):
   every 0030 row is kind 1/4/5. Exclude its listed request/target/graph/static
   sections, including 0042/0049, even when unreferenced. Retain that owner's
   explicit exceptions for shared 0045/0048/004A/004B. No general-predicate
   or concrete-request composition is inferred.
5. For the **four C1-family candidates**, the corresponding C1 parent owns
   its graph/section admission and catalog; do **not** import CG1's G1-only
   exclusion of parent-selected graph/request sections. Kinds 2/3 remain
   available only at their independently admitted parent uses, not in the
   ordinary-default field. No additional request, intrinsic, immutable-data,
   static, access or mixed-substitution successor joins by a link.

G1's bounded interface publication exclusions remain, apart from the
ordinary-default gate intentionally replaced here. CG1 supplies its mixed
constant-method route for already admitted generic-method publications; it
does not silently admit every method-generic/inherited/bodyful interface
requirement under bounded G1. The corresponding C1 route is required for
its independently admitted richer requirement views. Construction identities
do not enlarge D1's ordinary-default owner set to arbitrary constructors,
properties, callable values or nongeneric exports.

### Catalog and operation selection

Keep `(1,0)`, governing edition 1 and register codec 01. No new section,
Feature, DefId, Contract kind, type discriminator, opcode, prefix, escape,
Body record or width is allocated by GD1. The selected candidate meanings of
0030/0064/0065/0066 come from the stated whole agreement, not the registry.

| Candidate scope | Catalog consequence |
|---|---|
| All five | Keep independent `23/1 -> []`, `24/1 -> []`, `26/1 -> []` and SF1 `11/1 -> []`; amended enum `18/1 -> [(11,1)]`. |
| G1L1R1GD1 | Retain G1/CG1's incomplete `10 -> 11`, `12 -> {10,11}` registrations and gates. A template is not permission to execute an unsupported Feature-12 program. |
| Four C1-family candidates | Retain the reconciled C1 catalog: `10 -> []`, `17 -> {10}`, pending `12 -> {11,17}`. These edges replace the G1 ones once for this whole artifact. Preserve the corresponding class/construction requirements, including 25 where selected. |

In **each using Module**, a retained 4/5 source requires 24, including an
uncalled or bodyless default view; all other CG1 triggers remain. A type-only
template can therefore require 24. A retained ConstEval program additionally
requires its complete evaluation-form/program/read-bound/target support,
including 12 and that candidate's closure. A helper with method formals or a
GenericCall independently triggers 23; owner formals alone do not. Frozen
leaves trigger 11 and their nominal/layout requirements; bare formal forwarding
does not trigger 11 by itself. Retain L1's 26 triggers even for zero-parameter
owners. No artifact-wide Feature union, new `24 -> 23` edge or label-only
Feature 26 reinterpretation supplies missing support.

Select the exact literal before schema-dependent indexing for root, supplied
providers and captured Clause/Origin/Plan inputs under the corresponding
parent operation. Empty inputs and unused providers do not select a parent
or sibling decoder. Existing G1 source-map versions 1/3 and CG1 version 2
are **not automatically paired** with these new identities. Integration must
explicitly route the parent's checked declaration inputs and the CG1 nominal
inputs needed by retained terms to the same selected agreement/snapshots;
this representation proposal selects no new JSON grammar or implicit mapping
fallback. Helpers need no fabricated source export keys.

Original D1, CG1, G1L1/R1, C1 siblings and private persistence remain distinct.
Neither equal version bytes, zero default fields, compatible-looking
discriminators, Features nor a schema-name component parser selects GD1.
Unpublished assignments can be replaced without compatibility readers;
schema-key/version-byte substitution is not conversion.

## Replacement, remapping and source-free re-emission

Follow [binding and replacement](../specs/representation/binding-and-replacement.md#5-packing-replacement-and-relied-on-facts):
retain every original declaration view and all stronger dependencies actually
relied on. Parameter-default differences alone do not become an interface
witness-signature or binary-satisfaction mismatch. A provider with another
ordinary default, or none, can still receive the complete arguments selected
by the expectation. Fresh source compilation uses its separately selected
view. Removing/replacing a required formation helper or changing promised
constant behavior incompatibly is a different retained-dependency failure.

An already selected **open** default is as fixed in origin as a frozen one.
Keep its original V, captured mapping, program and expectations through
implementation/default-body replacement and later specialization. Renaming
corresponding formals is harmless; changing their kind/domain/order or losing
required contracts is not. Do not import CG1's generic-formal-default comparison
as a rule requiring implementation ordinary defaults to equal a requirement's.

A source-rebinding-equivalence claim, or elimination of a source-view proxy,
requires equal default presence and the same normalized bound value/term
after complete formal/reference remapping, plus the other D1 contracts.
Alpha-equivalent programs/mappings can establish this; equal results at
observed substitutions cannot. No arbitrary program-equivalence solver is
required. If equality is not established, retain both valid views rather than
overwriting one or using the first provider/key/MethodSig encountered.

| Decoded graph | Required transformation |
|---|---|
| MethodDef / MethodSig / **Param** / 0066 | Remap MethodDef owners and signatures; rebuild exclusive Param ranges/ordinals and the surface association to each logical Param. Remap names and ConstantSource rows. Clone context-dependent structural rows; do not attach the surface to a provider's same-numbered Param. |
| GenericParam / constraints | Remap TypeDef/MethodDef owner DefIds, complete mixed formal ranges and helper capture correspondences. Preserve ordinary q separately from generic ordinals and retain each generic-formal default's earlier-prefix scope. |
| **ConstantSource** | Remap 1 to FrozenValueNode, 4 to GenericParam and 5 to ConstantTemplate. Remap parent-admitted 2/3 in their own request/binding namespaces. Matching numeric words never select a table. |
| **TypeSpec** / frozen node / **Blob** | Substitute/remap complete result/domain/nominal argument trees and kind-directed constant slots. Rebuild node/child/carrier graphs and Blob references, preserving scalar/nominal payload bits; scalar payload bytes are not references. |
| ConstantTemplate / program | Remap MethodDef/Import token, exact/deferred TargetProfile, result TypeSpecRef, tagged actuals and the full helper/body/constraint/effect/dependency graph. Rebuild variable offsets and exact `16 + 8*A` extents. A helper's actuals do not become ordinary Params or saved execution substitutions. |
| **Module** / imports / exports / witnesses | Preserve logical originating Module and using context while remapping physical partitions/cursors, ModuleRefs, expected proxies, provider identities/keys, owner instances and evidence. Recompute each retaining Module's manifests, not an artifact-wide union. |
| Bodies / target and occurrence records | Rebuild maps, GenericCall actuals/evidence, instructions, records, offsets and SourceMap locations; preserve selected target/layout bindings and recorded concrete-request envelopes where representable. Remap both C1 phase-map contexts against their actual Clause/Origin/Plan artifacts. |

Removing source/debug material must leave a consumer able to bind a previously
unseen admitted substitution from this semantic closure alone. A required
program body is not debug data. Canonical writers retain D1's surface ordering
and CG1's contextual term traversal; valid reordered/duplicate immutable
term rows remain acceptable under
[ordinary canonicality rules](../specs/representation/encoding-canonicality.md).
Duplicate surface ownership or incomplete coverage remains malformed.

Closing one use may substitute its formal sources and form its templates;
keep the open declaration whenever independent future instantiation is
promised. Retain occurrence-specific target/resource/provider obligations.
If the destination cannot express the surviving closure, refuse output
atomically. A parent artifact that lost a default cannot be upgraded by
inventing one without source. Converting G1 argument kind 3 to CG1 kind 2
requires decoded formal-source construction; enum i32 fields likewise require
decoded typed re-encoding, not byte replacement.

Caches include selected declaration/expected view, complete owner/method
actuals and domains, bound term/program, provider revisions, exact schema/rules,
target and actual request envelope where relevant. A cached success under a
different target, binding or larger envelope is not this formation's evidence.
Reusing implementation work must preserve each occurrence's required checks
and accounted outcome under E, not waive charges or change the request scope.

## Required consumer checks

Before exposing/using the affected retained declaration or forming its default:

1. Check exact schema/catalog/support, section policies/extents/count products,
   variable indexes and all reserved fields. Enforce the selected all-row
   discriminator/section rules, not merely in-range payloads.
2. Check D1's surface ownership/coverage/eligibility and the exact Param range.
   Validate MethodDef, GenericParam, ConstantSource, TypeSpec, Blob, Module,
   target and Import references in their schema-owned namespaces.
3. For each use, resolve its own Module/declaration view. Check formal kind,
   owner/ordinal scope, complete O/M-to-helper mapping, result/domain equality,
   helper shape and selected form/program/provider closure. Reject dangling
   captures, substitution/type/value-source cycles and infinitely expanding
   transparent dependencies. Ordinary recursive program calls are instead
   governed by constant-execution resources.
4. Validate required records/support for published open formation/codec/domain
   facts and check substituted binding obligations. Do not infer missing
   facts from carrier bytes, one specialization, provider layout or ordinary
   arguments. The producer owns declaration-wide semantic truth; consumers
   need not re-prove it from the Body. After closure, validate the actual
   typed result, encoded scalar/nominal structure and exact target bindings.
5. For a genuinely new required execution, admit and run that program under
   the selected E, with its actual checks/dependencies. For a published value,
   rely on producer truth without replaying constructors or semantically
   re-proving arbitrary invariants/bodies. Optional audits remain separate.

Unavailable support, malformed structure, incompatible binding, failed
formation and resource exhaustion are not absence, an unknown-but-usable
default or interchangeable failure categories. Retained uncalled/defaulted
declarations still require valid structure and support closure; an explicit
argument does not excuse malformed metadata. An inspector can recognize a
template while declining semantic binding/execution support.

## Focused acceptance cases

These are required cases for later implementation, **not claimed passes**.
D1 owns its closed-default/label/scalar matrix; the cases here test the new
dependent boundary.

| Case | Required result |
|---|---|
| Direct constant-formal ordinary default; defaulted q=0 refers to the last method formal | Kind 4 reads the established actual with exact domain. All method formals, not only a same-numbered prefix, are available; generic inference does not use the omission. |
| Owner-constant-only forwarding on an admitted nongeneric requirement; repeat with a type-only expression helper and a bodyless expected view | The first needs 24/26 but not 23/11 absent another retained trigger. The helper adds 24/23 and its evaluation closure. Each using Module retains its own requirements, including uncalled views. |
| Expression `N + 1` versus direct N forwarding | Expression uses a real kind-5 program; forwarding needs no constructor replay. New inputs chosen by an independent consumer form the right value without producer source. |
| Owner vector `(type, constant, type)`, method vector `(constant, type)`, with unused entries | Capture tags are `1,2,1,2,1` in owner-then-method order. Owner ordinal 0, method ordinal 0 and ordinary q=0 never alias. Preserve all slots through packing and closure. |
| Type-dependent result `Tagged<T>` with published fixed primitive carrier; same carrier under different nominal T arguments | Preserve the complete domain and mapped result type. Equal carrier bits never merge nominal instances or replace missing declaration-wide eligibility. |
| Ordinary floating result depending on an integer/Boolean constant formal | Use the admitted SF1 result codec, retaining signed-zero/NaN operation observations. Do not admit floating constant formals or canonicalize copied bits merely to serialize. |
| Interface requirement has default term N; implementation/provider uses N+1, another label or no default | Omission selects the requirement term in its exact declaring view. Concrete/direct calls select their own view. Binary/witness satisfaction does not compare these ordinary defaults for equality. |
| Inherited same-identity diamond; unrelated requirements; method-formal renaming; open role-2 caller later closed with a replacement target | Preserve declaring instances, original expectation and alpha-mapped method formals. Do not refresh the chosen term or merge distinct views by signature/value; use the independently admitted C1 witness closure. |
| Constant formal with a generic-formal default, used by an ordinary default | Finish the generic default first. Kind 4 reads the actual; it never consults that formal's default itself. A self/later dependency in the **generic-formal** default still rejects. |
| Required ordinary argument would be inferable only from its ordinary default | Diagnose unresolved generic selection; no inverse arithmetic, default-value evidence or implementation-body search. |
| Default refers to an ordinary parameter, receiver value, caller local or out-of-scope helper formal | Reject capture, even when all observed ordinary arguments are constants. Wrong-owner equal ordinals do not repair it. |
| Malformed/ill-typed default in an unused declaration or with every call explicit | Reject declaration/publication. A well-typed open checked-add default that fails only for one substituted maximum value instead fails that omission's formation; explicit supply does not create that occurrence. |
| Same open arithmetic term under checked and already-bound wrapping policies | Preserve the declaration's selected policy across source-free import/JIT; no consumer-configuration rebinding. |
| Missing ConstEval program, Pure-only helper, helper with receiver/ordinary Params, invalid result or reached failed requirement | Refuse required formation; no stub, raw nominal carrier, runtime thunk, alternate default or another overload. |
| Admitted type-layout-dependent default, deferred versus fixed template target | Resolve against the selected target before execution and retain layout/binding dependencies. Target mismatch or unsupported value codec refuses; host layout and a target-independent tag-1 shortcut are not substitutes. |
| Default and explicit smaller/larger/zero envelopes; exact inclusive limits and one-unit excess | Record actual E and preserve accepted accounting/failure priority. No provider cap, nested reset, automatic larger retry or stale larger-envelope success. Retain the lifted helper's semantic charges. |
| Two omissions share a template; nested formation; previously frozen/forwarded value | Keep distinct formation occurrences and their ordering; nested execution shares the enclosing root ledger. Reading a frozen value does not replay its producer. |
| Reordered named supplied expressions with side effects and a later failing supplied argument | Receiver and written arguments run once in source order; formed constants fill the right omitted ordinals. Preserve staged-owner cleanup and every call/initialization obligation without runtime default calls. |
| Zero owner/method blocks, unit result/ordinary position, 0/1 template actuals, maximum row/ordinal and Body-map boundaries | Empty blocks are empty, not missing actuals; zero/one-actual templates have exact 16/24-byte extents when their helper/context permits them. Preserve logical unit positions and full-width q; enforce global 24-bit rows and separate Body u16 capacities without narrowing one namespace into another. |
| GP DefId where R(GenericParam) is required, type formal behind kind 4, missing/extra/reordered captures, wrong result TypeSpec, nonzero reserved bytes, old budget-bearing template extent, cycles | Refuse before affected use. Plausible lengths, matching widths or unused placement cannot select a different decoder or formal scope. |
| Source/debug stripped; alias/non-first provider; permuted MethodDef/Param/GP/source/type/Blob/Module rows; re-emit and import again | Preserve original views, complete helper/evidence closure, exact typed values and every schema-owned reference. New admitted substitutions still work; no first-view-wins or cross-artifact raw row leakage. |
| Replacement changes only provider ordinary defaults versus removes the original required helper/program promise | Preserve old selected defaults in the first case; reject or retain a satisfiable original program closure in the second. New source views may see the new default; old calls/terms do not. |
| GD1 versus D1/CG1/label-only/receiver-only/private/other C1 stages, including empty and unused inputs | Require the exact complete agreement and per-Module closure. In particular reject G1 argument kind 3 in GD1 and CG1-only 4/5 under D1; no content-driven schema guessing. |

## Integration owners and remaining decisions

On later adoption, the changes are:

| Owning document/layer | Exact integration |
|---|---|
| Grammar §3.2 and calls/inference | Add the proposed ordinary-dependent selection/formation timing, full O/M scope, declaration checking and no-totality distinction. Preserve initializer syntax, generic-formal earlier-only rules and existing inference/ranking. |
| Logical records and metadata | Add the ordinary-default use context for D1's 0066 → 0030 edge, its result/helper mapping and closure. Do not move defaults onto shared Param or reinterpret GenericParam defaults. |
| D1, CG1/admission, L1/R1 and stage-selection owners | Add the exact GD1 family and precedence above. D1 remains the separate closed-value agreement; CG1 alone acquires neither labels/receiver surfaces nor this ordinary-default context. Source input routing needs explicit integration, not inferred version pairing. |
| Registry, manifests, trust, canonicality and binding/replacement | Integrate the selected candidate schemas/contexts, independent 11/18/23/24/26 requirements, safe-consumption checks and typed transformations. Allocate no extra identity merely for the retaining edge; any adopted reassignment must update the whole agreement consistently. |
| Compiler | General ordinary-default parsing/binding precedes this Standard path. Carry checked defaults through bound declarations, generic proofs, traversal/substitution, interface adapters, verification, dependency binding, publication, re-emission and lowering. A codec-only change cannot supply the source feature. |

**Review decision still required:** adopt or reject the proposed ordinary
dependent-evaluation rule and this exact shared-source composition. The
recommendation leaves no ordinary/default ordinal, term tag, helper capture,
result-type, failure or schema-selection choice to implementation.

**Separate support gates:** general ordinary-default implementation, complete
source-free helper/witness/access closure, Feature-12 evaluator/target support,
explicit producer-input routing and transformation support remain necessary.
Richer value codecs, concrete mixed-request persistence and publication owners
outside D1 are not selected here. These are not implicit approvals or reasons
to erase a retained default.

Later integration must preserve the unapproved-versus-adopted boundary.
Creation of this document alone establishes neither compiler conformance
nor execution evidence.
