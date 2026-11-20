# Standard TRRM Construction-2 source-reference alias publication

**Status: unapproved decision candidate.** This supplements the provisional
[method/type dependency-selection proposal](standard-trrm-c1-method-type-dependency-selection.md);
drafting it approves neither the amendment nor implementation. It changes no
accepted specification and claims no build, test or runtime qualification.

## Problem and recommendation

The combined source-reference proposal requires every selected alias of one
actual MethodDef, TypeDef or FieldDef to remain equal under replacement,
without specifying how new consumer Imports retain that equality. Export
aliases describe the **selected provider snapshot**; they do not themselves
record which relationships a separately published consumer retained.

There is an existing logical carrier: several Imports sharing **one expected
local DefId**, together with the accepted rule that one expectation cannot
resolve to conflicting declarations. That can express equality without a new
record. However, the current Construction-2 paths do not admit or preserve
that construction uniformly for all three definition kinds. Distinct expected
DefIds with independently satisfying contracts do not, by themselves, express
the missing equality.

Recommend **B: source-snapshot alias equality, representative-only new
dependencies**. Validate all explicit selections when creating the reference,
then select one explicit type/field key per actual declaration for newly
created external uses. A method use retains its selected callable-view key.
Publish those dependencies, not an additional promise that other selected
keys will remain aliases in later provider revisions.

**This intentionally supersedes the original proposal's blanket alias-split
replacement rejection for newly selected source aliases.** It does not weaken
an equality, owner correspondence or stronger fact already retained in an
input artifact or required by an emitted contract. Preserving that distinction
is a condition of this recommendation, not an optional optimization.

Scope is only the proposed six-argument `CreateStandardReference` overload,
its classless nongeneric method/Copy-struct/field domain, and the **three
non-EN1 Construction-2 Copy-evidence agreements** listed in the original
proposal's [exact scope](standard-trrm-c1-method-type-dependency-selection.md#authority-exact-scope-and-alternatives).
Their full literals, identical-schema preflight for every supplied input and
all inherited requirements remain unchanged. Other factories and A2,
LayoutQueries, class/member, EN1 and generic alias policies are not amended.

## Evidence and the existing representation

The following are inspected source/specification facts, not executed results:

| Owner/path | Relevant fact |
|---|---|
| [Binding and replacement §1](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities), [IBID-06/08/11](../specs/conformance/il-binding-and-replacement.md#1-selected-definitions-and-expectations) | Export aliases denote one resolved declaration; retained equality must survive replacement; one expected declaration cannot resolve to conflicting identities. These are logical obligations, not a general alias-group table. |
| [Metadata row semantics and constraints](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Export is `u32 localDefId, str exportKey`; Import is `u32 moduleRef, str exportKey, u32 expectedLocalDefId`. Keys are unique within a provider Module; Import triples are unique. Neither row has an independent equivalence-class field. |
| [`TrRmExportTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs) | Key uniqueness is `(Module, decoded key)`, not local DefId uniqueness: several keys can map to one definition. `TryGetExportKeyForDefinition` refuses a nonunique choice; it is not a representative selector. |
| [`TrRmImportTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs) | `tracksImportAliases` stores Import-row bindings and marks DefId-wide lookup ambiguous. Its separate `allowsAlias` condition admits repeated expected **MethodDef `0x16`** in the three scoped C2 agreements, but not repeated expected **TypeDef `0x13` or FieldDef `0x14`**. Standard plus LayoutQueries admits `13/14`; A2-family TypeDef handling is another schema-specific branch. Those branches do not confer C2 support. |
| [`TrRmImportTableWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableWriter.cs) | Encoding validates through that reader under the selected schema. An array of candidate rows is not evidence that the rows can be published. |
| [`TrRmFixedArrayProviderBindingValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs) | `ValidateFieldDefImports` requires one owner Import per expected TypeDef. Type resolution is cached by artifact/Import row. Conversely, `ValidateC1M1MethodDefImports` checks a common expected top-level MethodDef against one actual provider target. Row-scoped storage alone is not that equality check. |
| [`StandardMethodTypeReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardMethodTypeReference.cs), [`BoundProgram` origins](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | The combined factory selects methods from one classless root Module and Module-qualified type/field roots across its validated provider set. Construction-2 provider Module Imports are admitted only when the root has MethodDef closure work; validated TypeDef/FieldDef edges are retained. The multi-Module path and source-free remapping remain unverified. |
| [`StandardClassTypeReferenceWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs) | Fresh imported-struct/class planning can iterate all selected keys into Imports sharing an expected TypeDef/FieldDef; that does not overcome the C2 reader restriction. Source-free `CreateC1M1ClassImports` remaps existing Import rows and their expected definitions, including common expected MethodDefs, rather than inventing a missing alias relation. |
| [`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`LocalAggregateLayoutProviderClosure`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutProviderClosure.cs) | Ordinary pending method Imports use the function's expected MethodDef, so separate callable views need not share an expected row. The provider-closure path separately checks conflicting targets for a shared expected MethodDef and retains method/type edges. Neither establishes equality between arbitrary distinct proxies merely because their original actual targets coincided. |

For example, schematic Imports `(P, a, E)` and `(P, b, E)` express one
expectation selecting one declaration through both keys **if the exact schema
admits that sharing and the consumer enforces the no-conflict rule**.
`(P, a, E1)` and `(P, b, E2)` instead retain two contracts; equal layout,
equal signatures or original resolution to one provider row does not encode
an otherwise unrepresented equality between them. Original expected views and
their using contexts cannot be merged merely to obtain the first encoding.

Thus the records can carry the relationship in an applicable shared-expectation
agreement; the claim that Import has only one key is not proof of impossibility.
But the current C2 operation has no completed all-kind publication/replacement
contract for it. Export-table success or source-reference interning does not
close that gap.

## Alternatives and allocation decision

| Alternative | Concrete contract and tradeoff |
|---|---|
| **A: persist selected alias equality** | For the newly captured groups with the same complete expectation, use one consumer-local expected definition and one existing Import per alias. Define/enforce same-actual resolution for the whole group, including uncalled retained rows. Admit shared TypeDef/FieldDef expectations in the exact C2 agreement and update field-owner selection, all binders, direct uses and transformations together. Preserve separately authored expectations and contexts rather than replacing them with a weaker common facade. This retains the original stronger promise but expands the current C2 alias-consumption work. |
| **B: representative-only new dependencies — recommended** | All selections are checked in the captured source-reference snapshot. New type/field uses select the first explicit alias of that actual declaration; method uses retain their callable-view keys. Downstream contracts retain only the keys actually selected for those uses, plus all pre-existing obligations. This needs no all-kind shared-expectation extension and avoids exporting equality beyond the emitted per-use contracts. |

Under A, group by original using snapshot/Module and expected DefId. Resolve
and compare every row, then require one identical actual `(selected Module,
definition in its selected snapshot)` for the group before affected use.
Missing keys, wrong kinds, an unsatisfied expectation or split targets reject,
including on replacement and when a group member is not used by a Body. Compatible row
relocation preserves that relationship; numeric rows across revisions do not
establish it. Independent successful row comparisons alone are insufficient.

A does **not** require an `AliasGroup` section, Contract or new DefId for this
bounded case: the accepted shared-expected-definition rule supplies its
schema-owned representation. Extending C2 admission/checking would have to be
an explicit part of its exact draft agreement, not a schema-name inference or
an assumption that A2's row-scoped machinery already implements it. A relation
between distinct nonmergeable expectations would need a further representation
decision; that larger problem is not selected here.

For B allocate **nothing**: retain the 12-byte Import and 8-byte Export rows,
DefId kinds `13/14/16`, `(1,0)`, governing edition and existing conditional
Features. FieldDef binding still requires 13/1; alias selection adds no
Feature 25 or other prerequisite. No successor identity, Feature, section,
Contract, Body record, opcode or reserved-value interpretation is proposed.
There is therefore no numeric allocation collision or capacity change to
resolve in the [registry](../specs/representation/registry.md).
[Exact draft agreement](../specs/representation/format-versioning.md) remains
required; neither a suffix nor equal version bytes selects an amended policy.

## Exact source/API and publication rules for B

Keep the original API signature and `TrRmStandardExportKeySelection` unchanged.
No new public selector, alias-group option or resolver is added.

1. Capture and validate **every** submitted key and artifact before returning
   the immutable reference, including keys that will not be representatives.
   Preserve the original null/text/duplicate/kind/access/ownership checks.
   Resolve aliases by the exact provider snapshot, Module and local
   definition, not equal source names, layout, key text or row values across
   snapshots. Equal keys in different Modules are unrelated selections.
2. Within each actual TypeDef or FieldDef group, the representative is the
   **first occurrence in its captured explicit selection list**. Type and
   field lists choose independently. Store that exact Module/key with the
   original expectation. Remaining selected aliases are checked snapshot
   facts, not additional downstream replacement dependencies. No Export-row
   order, lexical sorting, unique-key fallback or unselected key chooses it.
   All newly created uses of that grouped type/field in this reference use
   the same representative. Changing list order can intentionally change
   the published dependency.
3. Keep one source type/field declaration identity for aliases of one actual
   declaration and retain original declaration names for lookup. Method
   keys remain callable-view names. A call through method alias `b` retains
   `b`, not the first alias `a`; separate retained callable views use
   separate expected MethodDefs unless an **already retained** relationship
   requires otherwise. Do not coalesce them into a shared proxy or redirect
   calls merely because the captured actual implementation was the same.
   Whether an unused callable view is retained remains the existing
   retention policy, not an excuse to drop a retained Import.
4. For a **new** consumer expectation emit one Import for its chosen
   type/field representative, or the method use's exact key, with the
   original ModuleRef target/runtime requirements. TypeSpec, MethodSig,
   FieldMap/FieldPath and other uses refer to that checked expectation.
   A selected field still requires its separately bound exact owner
   TypeDef, complete layout/field correspondence and ordinary access.
   Complete layout does not select or authorize external field access.
5. Do not rewrite provider Export tables to remove aliases: they describe
   provider-owned publication, not this consumer's choices. Imported
   expectations remain non-exportable. Every **pre-existing retained**
   Import keeps its original key, expectation, equality/owner relationships
   and using context. Do not replace those with a representative, reselect
   a provider-owned dependency, or apply this policy globally through a
   shared A2/class/query helper.
6. Source interning cannot justify a fact lost by publication. Preserve all
   retained type/signature/owner correspondences and stronger relied-on
   facts; refuse publication if they cannot be represented in the selected
   agreement. This policy adds no authorization to erase a required equality.
   It authorizes no specialization that assumes two separately keyed methods
   remain the same implementation after replacement.

There is no Terrum grammar, source alias declaration, library API, Core
identity, target representation or runtime ownership change. Creating a new
reference against a later snapshot rechecks its selections; it is not
retargeting an immutable old reference or a source-rebinding compatibility
claim.

## Consumers, replacement and transformations

Consumers bind each retained Module/key to the required actual declaration
and check its complete original contract under
[binding compatibility](../specs/representation/binding-and-replacement.md).
They do not reconstruct omitted source-selection lists from available Export
aliases or provider debug data.

Binding this consumer to a new provider revision may succeed after removal or
splitting of an omitted source-only alias **only when no other retained edge
or relied-on fact requires it**. This is consumer-relative satisfaction, not
a claim of compatible replacement for the provider's entire published
surface. Every retained representative and callable key must still exist;
every original expectation and complete nominal, field-owner, Copy, layout,
unsafe/effect and applicable parameter-surface contract must still hold.
Two independently keyed method views may resolve to distinct satisfying
methods; a serialized shared-expected-MethodDef group may not. A method
signature now using a different nominal type from the consumer's retained
type binding still fails, even if both types have identical layout. A field's
separately selected owner and ordered layout correspondence still have to agree.

Keep [producer truth and consumer checking](../specs/representation/reader-trust.md)
separate: producers must not publish bodies or folds relying on erased facts;
consumers check safe decoding, supported schema/Features and required binding,
not arbitrary Body truth.

Carry the chosen dependency and original expectation through source binding,
bound traversal/rewriting, verification and lowering. On re-emission or packing,
rebuild Module partitions/ModuleRefs, expected definitions, Imports/Exports,
TypeSpecs/signatures/Params, fields/Layout, Body maps/FieldPaths and affected
source locations/indexes in their owning contexts. Preserve common expected
definitions where they already encode equality; do not create or dissolve
groups based on new row numbers or coincident actual providers. Reordering
the provider's Export rows never changes a retained representative.

Import-to-direct packing and pruning preserve all surviving original
expectations and stronger facts. Source-free reconstruction must not resurrect
discarded source-only alias checks or lose an existing group. Lowering uses
the checked per-use bindings and recomputed target call/layout plans, with
provider code/load lifetimes intact. This is not a live replacement protocol
or permission to reuse machine code after its dependencies change.

## Integration and current gap

| Owner | Intended integration on approval |
|---|---|
| Original proposal, **Lookup, aliases and closure** | Replace “retaining every key … same-definition obligation” for newly selected aliases with the snapshot/representative rules above. Preserve per-method-view keys and every pre-existing Import. |
| Original proposal, **Binding, substitution and transformations** and **Integration, CLI boundary and acceptance** | Replace unconditional new-selection alias-split rejection and its corresponding alias test expectations with the replacement cases below. “Every Import/Export alias” continues to mean retained serialized relationships and provider publication, not every discarded host selection. Other contract/remapping gates remain. |
| [Binding §§1, 4–5](../specs/representation/binding-and-replacement.md), [metadata](../specs/il-metadata.md), [verification](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | No normative amendment: equality actually retained remains obligatory; this host policy decides which additional dependencies new source publication creates. IBID-08 is not retired. |
| [Class publication](standard-trrm-c1-class-source-publication.md), [A2 type identity](standard-trrm-source-copy-type-identity.md), [Copy-evidence composition](standard-trrm-c1-initializer-copy-evidence.md) | Preserve their independent operations and exact schema meanings. Cross-link the combined overload's narrower alias-publication policy without importing it into those operations. |

The combined factory now validates Module-qualified selections and projects
the first explicit type/field key as the representative, while retaining each
method view's own key. Public regressions now cover the initial supplied-
provider chain, source-free remapping, and a type/field alias selection whose
nonrepresentative keys split under replacement while the selected keys and
method views remain intact. They remain unverified under the active hold.
Alias removal, invalid nonrepresentative selections, pre-existing shared
expectation groups, co-located non-first Modules, deeper chains/diamonds, and
multiple using-Module contexts for one expected TypeDef remain open. Emitting
all selected type/field aliases into one expected DefId is not a valid
workaround for current C2 admission.
The [implementation boundary](../implementation/modules.md) and
[gap index](../implementation/conformance-gaps.md) record unverified combined
reference/remapping/replacement work. Neither they nor adjacent method-alias
tests establish this proposal's coverage. No validation was run for this draft.

Keep the existing host failure families: invalid requested selections use
`ArgumentException`/`ArgumentNullException`; malformed or incompatible retained
metadata/bindings use `InvalidDataException`; unsupported required
schema/closure/representation uses `NotSupportedException`, preserving existing
capacity/resource distinctions. Fail atomically, never by dropping an
unrepresentable equality, choosing another alias or retrying another schema.

The [source-provider CLI transport](standard-trrm-source-provider-cli-transport.md)
and [C1 extension](standard-trrm-c1-source-provider-cli-transport.md) still do
not expose the combined method selection or these Copy-evidence pairings.
[`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs)
dispatches existing type/type-field factories and type-reference composition.
Add no command, manifest version/field, schema pairing, diagnostic family or
machine-protocol change here. A future explicit transport must preserve
selection-list order; artifact consumers need no new option to honor their
already serialized Imports.

## Required acceptance cases and remaining decisions

These are future checks through public APIs, not results from this draft:

| Case | Required outcome |
|---|---|
| Positive selection/publication | Select type/field aliases in an order different from provider Export rows. Produce one source identity and one exact representative Import per new grouped declaration; inspect actual emitted triples. Method calls through two views keep their respective keys and independent expected methods. |
| Negative capture | Missing, wrong-kind/case/Module, duplicate or inaccessible selections reject even for a nonrepresentative alias. No missing child/field key is inferred; public layout grants no field selection. |
| Replacement: source-only alias | A public regression now source-free re-emits the consumer and accepts a provider where its nonrepresentative type/field keys split while retained representative and method keys still satisfy their contracts. It remains unverified. Also cover removal rather than splitting, and require fresh source-reference requests to validate every submitted key. |
| Replacement: used dependencies | Remove a retained callable/representative key, lose Copy, change nested nominal signature/layout/pack/alignment, unsafe contract or field owner/ordinal: reject without substituting a surviving alias. Independently keyed method aliases may split only while every retained contract and type correspondence passes. |
| Retained equality | Supply an admitted common-expected-MethodDef alias group; re-emit/remap it, then split its provider targets. Reject before affected use. Separate expected rows alone do not add a group. Unsupported shared TypeDef/FieldDef input still refuses rather than being “repaired” by this policy. |
| Re-encoding | Permute consumer/provider definitions, Export/Import rows and Body maps; source-free re-emit/reload and pack. Retain chosen keys, original expectations, existing shared proxies, FieldPath/owner contexts and source locations. Never choose a representative again from output row order. |
| Cross-provider | Exercise the implemented supplied-provider chain and nested FieldMap loading, then add chains/diamonds and co-located non-first Modules. New selections use their exact declaring Module; provider-owned Imports retain their own keys and all old obligations. The current regression is unverified under the active hold. |
| Cross-feature/boundary | Cover all three exact agreements, scalar-only methods, unit/nested Copy-struct fields, CM1 labels/defaults and caller-unsafety where admitted. Preserve conditional Features and CM1AR1 closure; reject wrong/mixed schemas and unsupported required facts. Exercise existing checked row/map capacity boundaries without adding a limit or truncation. |
| Lowering | Separately qualify headless JIT and supervised AOT with final compiler/configuration/input identity. Emission or source-free loading alone establishes neither execution nor safe native replacement. |

Approval must choose **B and the explicit supersession above**, or instead
retain the original stronger promise and complete A's shared-expectation
contract across producers/consumers. No representative-ranking or hidden
wire design remains delegated by B: explicit first type/field selection and
the called method-view key are the choices. Implementation and qualification
remain open. CLI transport, broader callable composition and any independent
equivalence record for distinct nonmergeable expectations are separate
decisions, not defaults.
