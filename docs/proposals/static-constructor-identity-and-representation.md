# Static-constructor identity and logical representation

**Status: unapproved; explicitly provisional implementation direction.**
The authorized missing-contract workflow permits using this recommendation
provisionally. It does not amend accepted specifications, establish support,
select a runtime ABI or allocate physical metadata identifiers.

## Problem and recommendation

**Give an authored static constructor a distinct declaration identity, keyed
by its exact enclosing nominal definition and the symbolic category
`AuthoredStaticConstructor`. Preserve its receiver-free, parameterless,
no-source-result shape, and connect its implementation to the already
accepted authored-static-constructor `InitializationRole`.** Do not encode
this distinction by an ordinary method name or instance-constructor flags.

The missing decision is the explicit source-to-logical constructor identity
and shape, not permission to accept `static` on an instance constructor.
Physical constructor representation remains a separate integration gate.

| Directly inspected owner | Established contract or implementation boundary |
|---|---|
| [Grammar §3.1, static initialization](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage) | An authored static constructor has no parameters, result, visibility, caller-unsafe modifier or instance receiver; it is not ordinarily callable. One composed owner has at most one, with sole-primary policy/contracts and a separate implementation for a partial declaration. |
| [Names and binding](../specs/language/names-and-binding.md#namespace-and-member-declaration-spaces) | Constructors use designated operation identities rather than ordinary enclosing-type-name entries. The explicit static-versus-instance identity normalization below is not supplied there. |
| [Logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending), [clause ownership](../specs/representation/contract-clauses.md#1-scope-and-row-admission), and [MethodSig rules](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | The authored role tag `3` is already selected; no new role is needed. Its physical clause/reference integration remains pending. MethodSig requires `resultTypeSpec`, so absence of a source result is not permission to write zero or infer a standard constructor mapping. |
| [Static-persistence proposal](immutable-static-field-persistence.md#group-trigger-initializer-and-lazy-contracts) | Already recommends a private, unspeakable, receiver-free, parameterless, unit-result MethodDef candidate with the required role. That is a separate unapproved physical recommendation, not an ordinary source-result declaration or the current binder's constructor identity. This document neither replaces nor independently adopts its codec. |
| [Binder.Declare](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L3786) and [BoundFunction](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs#L841) | Declare reads `static` but rejects `constructor && (isStatic || isMutable)`, names all constructors `#ctor`, and selects instance-construction return/slot semantics. `IsClassConstructor` denotes a managed-class **instance** constructor. There is no authored-static-constructor fact on BoundFunction. Merely removing the rejection retains the name collision and wrong construction classification. |

The [trigger-identity proposal](static-initializer-trigger-identities.md)
continues to own the exact host-provided A/E/F/M registration and normalization.
Do not reopen its catalog, infer policy from names/bits, or include the selected
trigger in the constructor's identity key. The
[constructor no-return proposal](constructor-no-return-applicability.md)
remains independently provisional; this decision does not approve its
applicability rule.

## Proposed identity and logical shape

Add the following source rule to the grammar's static-initialization owner,
and apply it in the constructor paragraph of names and binding:

> An authored static constructor has the designated operation identity
> `(enclosing nominal declaration identity, AuthoredStaticConstructor)`.
> Its empty signature is not an instance-constructor overload and does not
> enter an ordinary member-name or callable group. Its written enclosing-type
> name identifies its owner, not an independently named method. Trigger,
> clauses, body, primary-part position and implementation spelling do not
> distinguish additional static constructors. Checked partial declarations
> contribute to this one identity; declarations in different Modules do not
> compose.

The exact owner includes its owning logical Module, nominal declaration
space and any required file-local source-unit identity. Transparent aliases
preserve the selected owner; equal names, layout or packed rows do not.

Add this **logical declaration fact**, not a new metadata table, to the
logical static-record requirements:

| Fact | Required meaning |
|---|---|
| Identity | Exact owning nominal definition plus symbolic `AuthoredStaticConstructor`; at most one per composed definition. No numeric category assignment is proposed. |
| Source signature | No receiver, no value parameters, no callable-owned generic formals and no declared source result. Enclosing type/constant formals remain available in their original context. |
| Primary contract | The sole primary's normalized trigger and checked conditions/other applicable contracts; a partial secondary cannot replace or repeat primary-only authorship. |
| Implementation | Exactly the implementation required by the accepted nonpartial/partial rules, belonging to this identity. A reference-only expected contract can omit executable Body data, not required contract facts. |
| Closed use | The declaration identity plus the complete enclosing-owner substitution. Different closed constructions remain distinct; this is not a new overload or method-formal list. |
| Initialization association | The exact owning eager group and its authored-constructor step. When represented by a MethodDef, that method has the existing required `InitializationRole` for the same group/owner substitution with authored role tag `3`. |

The group's authored-constructor edge and the method's role must agree in
both directions. An implementation has one declaration-level constructor/group
association: closing that owner or loading it again specializes the association,
not a second constructor declaration. It cannot supply another declaration's
constructor or double as a generated eager/lazy initializer. This constrains
logical edges, not native code sharing. Generated initializers and phase helpers
retain their separate identities and roles. No source constructor means **no
authored constructor identity**, not a synthesized role-3 method; the accepted
synthesized group/field-access trigger remains unchanged.

### Coexistence and non-callability

- An instance-constructor signature group and this singleton static identity
  coexist, including an empty instance signature. Ordinary constructor
  overloads, optional/default argument mapping and caller obligations remain
  governed by [calls and inference](../specs/language/calls-and-inference.md).
  Static construction contributes no candidate to instance creation,
  zero-supplied exception construction or type-call conversion.
- Reject multiple static primaries or implementations even with identical
  triggers/bodies. A valid partial primary and secondary denote one identity,
  not two overloads. Initializer execution order remains compiler-input/source
  order; identity composition gives the primary no priority.
- Do not expose the constructor through ordinary lookup, direct source calls,
  callable values, function-pointer/native-callable/native-export paths,
  instance factories or ordinary callable Imports/Exports. An unsafe context
  does not make it callable.
- An internal invocation belongs only to the owning group's validated
  authored-constructor phase. A MethodDef reference, role annotation or
  completion demand alone does not grant phase authority. This selects no
  new Body operation or calling convention.

## Binding, replacement and source-free consumption

Bind the exact provider owner/group through the existing selected identity
path, then correspond its authored-constructor edge and role. An expected
constructor reached through a retained group is not a new public method
Import or a lookup by private name. Preserve the originating using Module,
complete owner substitution, selected provider and retained expectations
through packing, aliases, linking and inlining. Partial composition cannot
cross a Module boundary.

For each retained group expectation, compare the presence of its
authored-constructor slot, constructor category, exact corresponding owner,
substitution and role/group association. Compare its clauses and every retained
trigger, initialization, effect, completion, ownership, failure and lifetime
obligation under [directed satisfaction](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions).
Body bytes and private names need not match. Replacing the constructor with an
ordinary static method, an instance constructor or a generated initializer is
not strengthening. Removing a required slot or changing its trigger cannot
be hidden by an unchanged Module/key or empty machine signature.

Source-free consumers check the selected schema/support, bounded typed
references, owner/substitution correspondence, signature/category consistency,
unique constructor edge, required role/cardinality and complete retained
binding graph before affected use. Reject dangling/wrong-kind owners or group
references, orphaned/multiply used authored roles, receiver/parameter shape
mismatches and unauthorized ordinary-callable exposure under the admitted
schema. Apply that schema's explicit MethodSig mapping; neither a `void`
result nor an `IsStatic` bit establishes constructor identity.

These are structural, support and binding obligations, not mandatory semantic
re-proof of the producer's body or initialization facts. Producers, including
public bound-IR producers and rewriters, establish their truth under
[reader trust](../specs/representation/reader-trust.md). Consumers need not
reconstruct source annotations or A/E/F/M references from a normalized group
contract. A missing required carrier declines the affected route rather than
fabricating a constructor or initialized group.

The declaration identity is distinct from runtime state: independently loaded
closed owner groups have separate load-scoped storage and initialization state.
No process-wide constructor-name cache or source identity tuple is a runtime
handle.

## Compiler integration and current gaps

These are required implementation consequences, **not support claims**.

| Layer / inspected implementation owner | Required change or gate |
|---|---|
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Classify static versus instance constructors before modifier validation, primary composition, callable lookup and return/receiver/dependency binding. Use a separate typed singleton key; validate the accepted static prefix without inventing default visibility. Resolve the trigger through the separate registration. Never select class-instance construction solely because the owner is a class. |
| [Signature, BoundFunction and BoundProgram](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Carry an explicit authored-static-constructor discriminator with exact owner/substitution, primary/implementation and group/contract links. Reuse checked nominal-origin machinery. Current instance flags `IsConstructor` and `IsClassConstructor` stay false for this category; `IsStatic` stays true, `ClassConstructor` and `ConstructionHomeIndex` stay absent. Parameter slots contain no receiver/formals. An internal unit completion carrier, if used, is not a declared source result or standard MethodSig decision. |
| [SemanticVerifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs#L1105) | Validate identity/shape/flag consistency, singleton ownership, role/group correspondence and permitted invocation paths on source/public IR. Keep static group initialization/completion separate from receiver completion, instance field rollback and class-factory publication. A discriminator alone cannot authorize unimplemented static operations. |
| [BoundTreeTraversal](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs#L331) and program/type remappers | Preserve the fact on function copies; traverse/remap owner, group, implementation and retained contract references as well as Body calls. Inlining/outlining preserves original phase, ownership and dependency obligations; it cannot turn the constructor into an ordinary callable helper. |
| [ModuleCompiler persistence/contracts](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs#L9738) | Current write/read paths and fingerprints carry instance flags, names and staticness, not this identity. Any supported private schema must preserve the complete fact and dependencies or refuse affected persistence. Do not silently erase it, reinterpret existing booleans or reconstruct it from `#ctor`. Private format changes remain separately specified, not standard TRRM support. |
| Dependency binding / [static expected-group candidate](immutable-static-field-persistence.md#binding-source-free-use-and-replacement) | Preserve constructor contracts through the exact provider/group correspondence, including reference-only stubs and remapping; retain original expectations and stronger relied-on facts. Required source-free declaration, contract and group codecs remain prerequisites. |
| [NativeEmitter](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs#L2100) and [local aggregate normalization](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayout.cs#L180) | Exclude this category from instance receiver allocation, constructed-value returns, class factories and local instance-constructor normalization. Execute only through supported owning-group orchestration with existing timing, phase, failure, cleanup and load obligations; otherwise refuse lowering. Do not substitute CLR static-constructor timing or a standalone ordinary unit call. |

The [implementation inventory](../implementation.md) and
[CG-008](../implementation/conformance-gaps.md#cg-008) retain broader static
integration gates. This document edits neither inventory and does not close
that milestone.

## Integration owners, alternatives and deferred gates

After adoption, add the proposed source paragraph/identity rule to
[grammar §3.1](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage)
and [names and binding](../specs/language/names-and-binding.md#namespace-and-member-declaration-spaces).
Add the logical fact and bidirectional role association to
[logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending).
[Metadata](../specs/il-metadata.md#semantic-contracts-and-stable-binding) and
[clause ownership](../specs/representation/contract-clauses.md#1-scope-and-row-admission)
reference that distinction without new flags or roles. Apply the comparison
and consumption rules above in
[binding §§2, 4–5](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions)
and [reader trust §§1–5](../specs/representation/reader-trust.md).
Integrate the cases below into
[static conformance's SIID/SIREP families](../specs/conformance/source-static-initialization.md).

These are additions clarifying the logical normalization, not replacements
of accepted source productions or execution rules. Only this bounded logical
identity/shape decision is supplied; physical deferrals are not superseded.

| Alternative | Tradeoff |
|---|---|
| Recommended: distinct typed declaration fact plus the existing authored initialization role | Small coherent change across consumers; separates identity from host return/slot mechanics and reuses the accepted group/role model. |
| Remove the rejection and choose another private name | Avoids one dictionary collision but leaves receiver/result, factory, verification, persistence and callability ambiguities. A name is not a contract. |
| Redesign all constructors as ordinary `T`/`void` functions or introduce a universal new callable-kind/MethodSig codec | Could unify representation, but changes broader construction/publication and exchange contracts. Unnecessary for this bounded source/logical decision. |

No section, DefId, clause ID, Feature, TypeSpec form, opcode, runtime handle,
reserved name or format pair is allocated or reassigned. Existing role tag
`3` is reused, not newly assigned. The registry's
[separate namespaces](../specs/representation/registry.md#4-admission-status-and-namespace-boundaries)
remain unchanged.

Separate gates are: the exact standard constructor MethodSig mapping for
absent source results; full group/owner/reference and contract codecs; schema
and capability admission; and static storage, phase authority, orchestration,
failure, sharing, cleanup and native/load-lifetime support. The existing
static-persistence unit-result recommendation remains its own provisional
candidate, not an implicit amendment here. Exchange requires
[exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts);
equal version bytes or private successful persistence supply none of these
missing agreements. Accepted execution semantics and the separate trigger
and no-return decisions are preserved.

## Acceptance cases

These are proposal-local requirements, not assigned metadata IDs, support
claims or reports of tests run. Fixtures supply independently admitted
contracts; all existing SIEG/SISTA/SILIFE cases remain required.

| Case | Required outcome |
|---|---|
| Empty instance constructor and valid authored static constructor on one type | Distinct identities; instance selection never chooses the static constructor. Preserve any initialization demand required by the actual operation. |
| Multiple instance overloads/defaults and one static constructor | Preserve ordinary overload and zero-supplied-argument binding; static trigger/body/clauses add no candidate or overload discriminator. |
| Nonpartial and valid partial static constructors, including distributed source parts | One primary/implementation identity; keep primary contract ownership and input/source initializer order. Duplicate primary, implementation or primary-only secondary policy rejects. |
| Struct, managed class, static class and generic owners | No instance receiver or constructed result; complete owner substitutions and independently loaded groups remain distinct. Invalid owner categories still reject under the grammar. |
| Missing trigger; parameters/result; visibility, unsafe, extern, mut or async prefix | Reject under existing source/schema rules; neither a new identity nor an unsafe context repairs invalid declarations. |
| Direct call, callable/address acquisition, native export, ordinary method Import or class-factory target | Reject constructor exposure/selection; same spelling or empty signature grants no callability. |
| No authored constructor, but generated eager/lazy initializers exist | No authored identity or role-3 substitution; retain the existing synthesized group trigger and generated roles. |
| Public bound IR has missing owner/group, instance flags, receiver/parameters, construction Home or factory linkage on this category | Producer verification rejects the inconsistent representation, rather than treating it as ordinary receiver construction. |
| Role-3 method is orphaned, reused by different declaration-level constructor edges, mapped to another owner/group, or lacks required role data | Reject structurally inconsistent or unsupported affected input; role tags alone do not grant authority. Closing or reloading one valid declaration does not create that duplication. |
| Supported rewrite/persistence/dependency binding with row, type, function and source-coordinate remapping | Preserve exact category, owner/substitution, constructor/group edge and retained contracts without name/row inference. Reference-only stubs remain nonexecutable. |
| Selected schema lacks a required constructor carrier; method merely resembles `#ctor` or an ordinary static unit function | Decline affected use; do not guess a schema or fabricate identity from names, flags, result type or optional source data. |
| Replacement changes constructor category/presence, owning group or retained trigger/phase contract despite stable Module/key | Reject affected compatibility. Reordered rows or changed private Body/name are permitted only when the complete retained contract still passes. |
| Authorized constructor phase versus ordinary or different-group invocation | Producer checking preserves exact authority and initialization state; consumer structural/authorization checks do not become mandatory body semantic re-proof. |
| Ordered eager initializers followed by constructor clauses/body; failure or truthful no-return under its separate proposal | Preserve existing order, actual cleanup/failure delivery and group publication rules. No normal receiver/result/group fact is fabricated; no-return is not copied to unrelated members. |
