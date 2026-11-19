# CM1 associated-relation generic constraints

> [!IMPORTANT]
> **Unapproved; provisional direction under the user's missing-contract
> authorization, pending later review.** This selects a bounded composition,
> not either prerequisite proposal's wider design. Accepted specifications,
> the registry and implementation/support status remain unchanged.

## Missing decision

The [source contract](../specs/language/interface-witnesses.md#associated-types-and-constants)
already defines exact associated equality. The
[logical records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending),
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [registry](../specs/representation/trrm-1.registry.json) assign the rows
but leave kind-3's typed payload and AssociatedTypeDef's first-class
definition/binding codec incomplete.

The existing unapproved [payload](constraint-associated-relation-payload.md)
and [identity](associated-type-definition-identity.md) proposals already
make those physical choices concrete. Do not redesign them. They do **not**
select their use in CM1: [G1 expressly excludes their composition](standard-trrm-generic-method-call-body-encoding.md#plural-generic-argument-obligations),
and [CM1 inherits that generic contract unchanged](standard-trrm-c1-class-parameter-defaults.md#one-exact-candidate-agreement).
The remaining decision is an exact agreement and bounded obligation-
reconstruction scope, not a third relation encoding or new source meaning.

Current code already bounds kind-3 against AssociatedRelation in
[TrRmConstraintTableReader](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstraintTableReader.cs)
and structurally decodes the candidate declaration union in
[TrRmAssociatedRelationTable](../../sources/Terrum.CodeAnalysis/Modules/TrRmAssociatedRelationTable.cs).
The earlier proposals' implementation paragraphs are not current evidence.
[The generic reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs)
still rejects kind 3 and associated interface members; the
[class re-emitter](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs)
also rejects kind 3. Typed bound facts, owner/member provider comparison,
source satisfaction and closure/remapping are implementation gaps once this
composition is selected.

## Provisional choice: CM1AR1

Select one new, ordinal, case-sensitive out-of-band whole-schema identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1`**

Its meaning is exactly CM1 plus:

* The payload proposal's kind-3 row reference, subject/context and preservation
  rules: `Constraint.payload` names AssociatedRelation section `0x0036`, never
  TypeSpec, a token or a witness. Both rows remain 12 bytes.
* The identity proposal's candidate `0x20` AssociatedTypeDef DefId, explicit
  owner/member Import/Export, declaration-default comparison and
  AssociatedRelation declaration-reference union. A direct `0x20` word selects
  section `0x0027`; an imported `0x1C` word selects an Import expecting that kind.
  Its witness-reference use is not admitted by this composition.
* The open-formal satisfaction boundary below. This supersedes G1's blanket
  associated-relation refusal **only in this successor and scope**.

Keep `(1,0)`, edition 1, Body/record layouts and existing numeric candidates.
No new Feature, opcode, section, qualifier, operand or evidence list is added.
In this successor's catalog, existing `23/1` also covers these retained
declaration/relation contracts: require it in each Module retaining an
AssociatedTypeDef or AssociatedRelation, including expected and interface-only
provider declarations. Its existing triggers and unconditional prerequisites
`[]` remain. Existing conditional `11/1`, `26/1` and other CM1 requirements
remain independent; an associated *type* default is not an SF1 value.
This registers no closed-witness/projection capability.

Explicitly select CM1AR1 before decoding the root and every supplied provider,
including unused inputs, as CM1 requires. Parent/sibling/private/CG1 selectors
and equal version bytes are not aliases. Neither Feature 23 nor table presence
selects this codec. No compatibility reader or automatic promotion is needed;
conversion must select both schemas and rebuild decoded references.
Both referenced codec subsets and the complete owner/member binding and
remapping below are prerequisites to exposing these methods; partial
structural support is not admission.

## Bounded source meaning and satisfaction

Scope is import, source-free re-emission and direct source calls for CM1's
public static/readonly-instance generic MethodDefs on nongeneric classes,
with type-only method formals. Supporting interfaces are public, type-formal-
only, without bases or associated constants; retain their complete directly
declared associated-type contract, not a sparse referenced-member cache.
Other callable/type/default restrictions remain CM1's. Valid but unsupported
closure is refused, not erased.

The accepted-target fragment `where T.via<I>.Item is U` retains its existing
[grammar/source meaning](../specs/grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding):
the subject is the Constraint's formal; the payload triple is the complete
declaring interface instance, resolved associated declaration and complete
required type. Equality is normalized identity, not assignability, layout
equality or inference backward from `U`. Substitute owner arguments before
method formals by kind/ordinal. Bind only an explicitly identified interface
self formal as self; equal ordinary arguments remain ordinary.

For this slice, discharge a callee relation only when its subject actual is
an in-scope caller type formal with a matching normalized kind-3 obligation
after substitution. Retain the exact caller/target Constraint identities and
their subject correspondence. Both retained callee and caller formal views
must independently supply the same subject's kind-1 conformance obligation
for that exact interface instance. Neither kind 3 nor an associated
declaration/default supplies a witness. No transitive equality solver or
stronger implication is required; unsupported implication is not success.
Check authored declarations/calls over their admitted domains.

Closed-actual associated satisfaction and bodies requiring associated
projection/dispatch remain unsupported here. Do not manufacture a witness,
select a default as evidence or invoke a provider to discover equality.
Open forwarding preserves symbolic obligations without a closed witness row.
The existing plural obligation environment reconstructs them from declarations;
direct GenericCall retains role 1 and zero dispatch evidence. Kind 3 is never
interface-dispatch evidence. No runtime input, check or dictionary follows.

## Identity, providers and replacement

Apply the identity proposal's complete owner/range, stub, export-key and
exact-default rules, not name/ordinal matching. Each retained associated
expectation has an explicit member Import inside its consumer-owned expected
InterfaceDef and a separately bound owner TypeDef Import. A member must resolve
to that exact selected provider interface in the selected Module/revision.
Direct references to expected stubs still follow their Imports. Public local
members may be exported; expected members/imported owners may not be re-exported.
Preserve complete associated ranges, aliases and independently retained views.

The class MethodDef and its class owner must still select the same provider.
The relation's interface/member pair may select a **different** dependency;
do not force its ModuleRef to equal the class provider's. Validate that pair's
complete expected interface contract independently.

Under [directed satisfaction](../specs/representation/binding-and-replacement.md),
each provider-required relation must match an expected relation after formal
correspondence and normalized identity comparison. Dropping a caller
requirement may relax the provider contract but never deletes the retained
expected obligation or a stronger fact actually relied upon. An added or
changed unmatched requirement fails this supported comparison.
Associated declaration defaults compare absent/absent or exact normalized
types under owner substitution; present/absent fails. This does **not** change
CM1 ordinary-parameter defaults: calls retain their selected expected view,
not the provider's current default. Keep generic type-default rules unchanged.
Replacements recheck all retained identities, contexts and contracts; equal
names/layouts, one closed coincidence or an unfinished comparison cannot pass.

## Safe consumption and re-encoding

Before affected exposure/use, validate exact schema/support, checked section
extents/counts, zero reserved fields, constraint ownership/ranges and kind-
correct bounds; then validate both complete TypeSpecRefs, formal scope,
associated-reference discriminator/expected kind, interface ownership and
required bindings. Only optional declaration defaults admit zero. A declaring
interface must actually resolve to the matching nominal interface; structural
row decoding alone is insufficient. Unsupported required closure blocks use.
Producers establish semantic truth; consumers need not re-prove represented
bodies. New source calls still establish their own declared obligations under
the [consumer trust contract](../specs/representation/reader-trust.md).

Carry typed relations and retained expected/provider contexts through binding,
shared traversal/rewriting and mandatory verification. Re-encoding decodes
first and rebuilds these schema-owned edges:

| Record | Required remapping |
|---|---|
| GenericParam / MethodSig | MethodDef/TypeDef owners and formal rows by their maps; retain ordinal/kind/domain/defaults and rebuild exclusive generic/constraint ranges. Method formals are never MethodSig-owned. |
| Constraint | Owner through GenericParam; kind 1 through complete TypeSpecRef; kind 2 retains its property code; kind 3 through AssociatedRelation, never TypeSpec. |
| AssociatedRelation | Map both complete types and separately map its declaration union through AssociatedTypeDef or Import according to its discriminator. |
| AssociatedTypeDef / InterfaceDef | Map InterfaceDef/TypeDef owners, explicit self formal, names, scoped type defaults and complete directly owned ranges. A declaration's default uses interface scope, not the using method's. |
| Import / Export | Map expected/local definition by its actual kind, plus ModuleRef/String and owner/member dependencies. Preserve independent expectations and aliases; direct-bind only after complete compatibility checking. |
| TypeSpec / Body dependencies | Map nominal/import/formal references and constructed terms recursively, including admitted nested ConstantSource arguments through their own map. Preserve F2/F3 complete-type meanings, qualifications and role-1 call obligations; never treat inline words as row fixups. |

Clone shared relations/TypeSpecs when using contexts differ. Rebuild ranges,
counts, indexes, manifests, extents and affected typed source/debug references.
No raw-byte substitution, row-coincidence identity or success-shaped fallback.

## Required cases and deferred work

These are proposed tests, not executed results. Use public compiler APIs and
independent artifact vectors, not only writer/reader round trips.

| Cases | Required result |
|---|---|
| Static and readonly-instance imports; matching open-formal direct calls; renamed formals; multiple relations | Preserve exact owner/ordinal/subject and every typed obligation through binding and source-free reload. |
| Class provider, consumer and separate interface provider; aliases; compatible provider row relocation | Bind each owner/member pair independently; retain all expected views and replacement obligations. |
| Wrong namespace/high byte/zero/out-of-range/truncated payload; wrong formal scope or overlapping ranges | Mandatory structural rejection even when another table has the same numeric row. Exercise checked 24-bit limits and extent arithmetic. |
| Missing/wrong associated key, wrong selected interface owner, conflicting aliases, changed default or strengthened provider relation | Reject binding/replacement without name lookup, fallback provider or default reselection. |
| No caller relation/conformance, different required type or qualification, closed actual, default offered as proof | Refuse unsatisfied/unsupported source use; do not infer equality or synthesize evidence. |
| Independent GenericParam/Constraint/AssociatedRelation/AssociatedTypeDef/InterfaceDef/TypeSpec/Import permutations; context-dependent shared rows | Remap each namespace/range and split sharing where needed; equivalent row/F2/F3 required types compare equally. |
| Relation beside Unique/Write/Blittable, generic defaults and unequal CM1 expected/provider ordinary defaults | Keep independent permissions/domains/dependencies and selected default views; associated equality grants no authority. |
| Unchanged CM1/G1 or unused mismatched provider; missing per-Module requirements; unsupported inherited/witness closure | Refuse affected admission; recognition or equal `(1,0)` is not this agreement. |

No wider witness/binding-origin codec, closed witness publication, inherited
associated resolution implementation, qualified-self/existential carrier,
associated constants, generic-class storage, shared-generic/native ABI,
runtime/JIT/AOT acceptance, fresh source export-key API or CLI transport is
selected. Later integration would amend the linked metadata/logical-record/
registry owners and the G1/CM1 composition/admission clauses together; this
proposal makes none of those edits or support claims.
