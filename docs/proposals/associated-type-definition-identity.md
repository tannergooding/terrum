# Associated-type declaration identity and binding

> [!IMPORTANT]
> **Unapproved proposal.** Every allocation and rule addition below is a
> candidate, not an accepted assignment. The user's compiler-conformance
> execution instruction permits provisional implementation direction; it
> does not approve this design. This document changes no specification,
> registry, compiler or conformance status.

## Decision, scope and dependencies

**Recommend candidate DefId kind `0x20` for AssociatedTypeDef, with an
explicit four-byte AssociatedTypeReference union of that DefId and an
Import token expecting that kind.** Apply the union to
`AssociatedRelation.associatedTypeDefId` and
`WitnessAssociatedType.associatedTypeDefId`; admit the candidate definition
kind in Import/Export. Retain the existing declaration, relation, witness,
InterfaceDef and Import/Export row layouts.

The [logical witness contract](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain)
already requires first-class associated-type declaration identities bound
through Import/Export. It explicitly calls `0x20` an **unassigned candidate**.
The accepted [DefId list](../specs/il-metadata.md#table-stream-and-token-encoding)
and [registry](../specs/representation/trrm-1.registry.json) omit that kind,
and both Import/Export target sets exclude AssociatedTypeDef. Assigning
section `0x0027` and its 12-byte row did not assign a definition kind.
This is a physical-contract gap, not an accepted alternative encoding or
merely a missing implementation.

This candidate fills the identity/Import/Export decision deliberately left
open by the [Constraint kind-3 payload proposal](constraint-associated-relation-payload.md).
That proposal separately recommends a required AssociatedRelation row
reference in `Constraint.payload`; adopting either candidate alone does not
complete the other. The relation payload is not this declaration-reference
word, and the declaration reference is not an AssociatedRelation row.

The current [TypeSpecRef work](../implementation/conformance-gaps.md#cg-001)
is another prerequisite: `defaultTypeSpec`, the relation's declaring and
required types, and witness type fields use the accepted row/F2/F3 union,
including nested complete-type arguments. Formal-aware arity/kind/domain
validation, dependency binding and remapping must cover these uses; an
earlier row-only reader is insufficient. This proposal neither resurrects
TypeArg nor assigns an associated-projection TypeSpec form.

Independent generic/witness exchange still requires its complete
[capability, evidence and carrier contracts](../implementation/conformance-gaps.md#cg-006).
This candidate allocates no Feature ID and does not complete those gates,
WitnessProjection, associated-constant identity or witness `bindingOrigin`
tags. Unsupported required semantics continue to block affected use.

## Candidate physical contract

All requirements in this section are **unapproved additions**. Words are
little-endian; table rows are artifact-wide, not per-interface ordinals.

| Position | Candidate word and exact namespace |
|---|---|
| Local associated-type DefId | `(0x20 << 24) \| r`, where `1 <= r <= AssociatedTypeDef.count <= 0x00FF_FFFF`; the low 24 bits select section **`0x0027`**, not Body section `0x0020` |
| `Import.expectedLocalDefId` for this kind | The candidate local `0x20` DefId of a consumer-owned expected AssociatedTypeDef |
| `Export.localDefId` for this kind | The candidate local `0x20` DefId of an eligible provider-owned AssociatedTypeDef |
| AssociatedTypeReference: direct form | The candidate `0x20` DefId above |
| AssociatedTypeReference: imported form | `(0x1C << 24) \| i`, where `1 <= i <= Import.count <= 0x00FF_FFFF` and Import row `i` has an expected `0x20` AssociatedTypeDef |

Thus candidate `0x2000_0001` selects AssociatedTypeDef row 1
(`01 00 00 20`), while `0x1C00_0001` selects Import row 1
(`01 00 00 1C`) with the required expectation. Import/Export's definition
fields themselves do **not** accept Import tokens or chained expectations.
Check the discriminator before interpreting the low bits.

The complete grammar of this candidate reference is those two forms only.
Zero, a zero row with either discriminator, raw `0x0000_0001`,
section-derived `0x2700_0001`, TypeDef/GenericParam/UnionCaseDef words,
TypeSpecRef F2/F3 words and `0xFF` reject in an AssociatedTypeReference
position. A same-numbered row in another table cannot rescue an invalid
reference. `0x20` becomes a permitted definition kind **only if this
candidate is adopted**; it does not become a general Body-map token,
complete type, GenericParam owner, constant identity or native address.
Do not widen any other field's target set by implication.

Use the union in exactly the two existing fields named
`associatedTypeDefId`. Keeping that column name does not make its imported
form a DefId. Their enclosing rows remain:

| Existing section | Unchanged ordered payload | Candidate interpretation |
|---|---|---|
| AssociatedTypeDef `0x0027`, 12 bytes | `u32 ownerInterfaceDef, str name, u32 defaultTypeSpec?` | A directly declared associated-type requirement; the owner is a typed InterfaceDef row, not a TypeDef/DefId |
| AssociatedRelation `0x0036`, 12 bytes | `u32 declaringInterfaceTypeSpec, u32 associatedTypeDefId, u32 requiredTypeSpec` | Required complete interface TypeSpecRef, required AssociatedTypeReference, required complete TypeSpecRef |
| WitnessAssociatedType `0x002A`, 20 bytes | `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 associatedTypeDefId, u32 valueTypeSpec, u32 bindingOrigin` | Preserve witness/origin rules; use the same required AssociatedTypeReference, never a second declaration codec |

The existing required section policy `0x0000`, fixed strides, section
framing and checked extent/count rules remain. AssociatedTypeDef's owner
and name use ordinary required typed rows with a reserved-zero high byte;
the name is nonempty well-formed decoded text. Its default is zero for
absence or a valid complete TypeSpecRef, not an executable initializer.
An associated declaration has no separate generic-formal list: its default
is interpreted in its declaring interface's formal scope, including the
explicit self formal when present, not an arbitrary using method's scope.

Canonical writers use the imported form for a retained Import dependency
and the direct form for an authored or already satisfied direct-bound
declaration. A direct DefId naming a registered expected stub still follows
that stub's Import; it is not a bypass. Ordinary consumption need not reject
that equivalent noncanonical reference solely for spelling. This adds no
fallback codec, raw-row alias or global row-deduplication rule.

## Declaration ownership and publication

The following owner/stub/export rules are **unapproved candidate additions**.
They reuse InterfaceDef's existing
`associatedTypeFirst/associatedTypeCount` rather than introducing another
owner, member table, ordinal or per-module cursor.

* Each modeled interface TypeDef has one InterfaceDef whose `ownerTypeDef`
  identifies that exact nominal interface. Two InterfaceDef rows cannot
  describe competing owners for the same TypeDef.
* Its associated-type range contains exactly its directly declared
  AssociatedTypeDef rows. Nonempty ranges are checked contiguous ranges;
  an empty range is `(0, 0)`. Each row belongs to exactly one such range,
  its `ownerInterfaceDef` agrees with that range, and no associated row is
  orphaned or shared between owners. A stub retains its complete expected
  direct-associated declaration contract, not a sparse lookup cache.
* Module ownership is inherited through
  `AssociatedTypeDef -> InterfaceDef -> TypeDef -> Module`. It is not
  inferred from String text, export-key spelling or physical adjacency.
  Base-interface members stay in their original declaring interface's
  range; a derived interface does not copy/re-own them.
* An imported associated declaration is an expected stub **inside the
  consumer's expected InterfaceDef**. Its owner TypeDef, its Import and
  the Import's ModuleRef belong to that same consumer Module. It is not
  an AssociatedTypeDef attached directly to an external provider's
  InterfaceDef. Every retained expected associated declaration has an
  explicit Import binding; importing the interface alone does not
  identify its member by range position or name.
* The stub may retain the expected `defaultTypeSpec`. That is published
  declaration contract, not a locally authored implementation or a chosen
  witness binding. Forcing it to zero would erase an obligation.
* An Export selects a directly declared provider-local requirement of a
  public, non-expected interface TypeDef. Neither an imported associated
  stub nor a member of an imported owner can be re-exported as local.
  AssociatedTypeDef has no independent visibility word: this bounded
  codec represents the interface-contract requirement with its containing
  interface's effective access. It cannot erase an additional source
  access restriction. Forms requiring unrepresented access metadata remain
  gated under the existing [access-record rules](../specs/representation/logical-records.md#source-unit-access-and-definition-identity-encoding-pending).

Export keys retain the existing nonempty ordinal decoded-text comparison,
Module-wide uniqueness and stable-publication obligations. A member key is
not implicitly an interface key plus a name, and keys are not scoped to an
InterfaceDef. Different owner ranges do not permit duplicate keys within
one Module identity. Existing permission for several keys to select one
local definition does not create several declaration identities; no new
alias mechanism is proposed.

An exported member does not implicitly export its owner or assign an owner
key. A cross-artifact consumer separately binds the expected interface
TypeDef through its explicit owner Import. A merged use may instead name
previously satisfied direct-bound provider declarations. A retained member
expectation still keeps its consumer-owned owner stub and owner-binding
obligation; packing cannot reparent the stub into the provider. Both paths
must identify the same selected owner. Public visibility alone supplies
neither an owner binding nor conformance authority.

## Consumer/provider binding and defaults

The following is the **candidate associated-declaration specialization**
of [binding and replacement](../specs/representation/binding-and-replacement.md).
It is not a new selector or a relaxed complete-contract comparison.

1. Resolve the member Import's exact selected Module identity/revision and
   export key to one eligible AssociatedTypeDef. Require the candidate kind;
   do not retry another key, overload, provider or same-named declaration.
   Existing ModuleRef target/runtime matching and every retained
   expectation's independent checking still apply.
2. Resolve the expected interface TypeDef's own binding independently.
   Require the selected associated declaration's `ownerInterfaceDef` to be
   the InterfaceDef of **that exact selected provider TypeDef**, in that
   provider Module/revision. Equal owner names or layouts do not suffice.
   Satisfy the complete retained interface contract, including its formal
   domains, self identity, bases and other required dependencies; success
   on the associated row alone is not success on the owner.
3. Correspond expected/provider owner formals by kind and ordinal.
   Preserve arity, ordered type/constant arguments and applicable formation
   constraints. Compare defaults in that correspondence using forward
   substitution and exact normalized complete type identity.
4. **Candidate exact-default rule:** both defaults absent succeeds; exactly
   one absent fails; both present must normalize to the same complete type
   under the applicable owner substitution. At an open owner compare
   corresponding symbolic terms over the admitted domain; equality in one
   closed instance does not establish an open declaration comparison.
   TypeSpec row versus eligible F2/F3 spelling may differ. Layout,
   assignability, a nominal wrapper or lost inner/outer qualification cannot
   establish equality. Unsupported comparison cannot count as satisfaction.
5. Complete the finite retained comparison graph before publishing success.
   Keys include resolved declarations, using Modules, formal/substitution
   contexts and governing rules, not just pairs of row numbers. Recursion
   through a nominal anchor may be finite; cyclic transparent associated
   defaults or growing substitutions are not rescued by memoization.

The exact-default rule is deliberately conservative and **unapproved**.
It does not invent a strengthening order for absent/present/default types.
“Replaceable default” means an explicit checked conformance binding may
override the declaration default; it does not mean a provider revision may
silently change a retained default contract. A directed relaxation permitting
new defaults would need a separate proof that all retained formation and
selection observations survive; it is not this candidate.

The name is validated as a source/declaration label, not used to select or
pair binary members. It is not an additional binary identity-equality
condition in this candidate; separately retained source/publication
obligations are not waived. One expected declaration cannot bind to
conflicting provider identities. Different expectations selecting the same
provider declaration remain distinct obligations and must all pass; no
“weakest expectation wins” merge is introduced.

## Exact interface instances, relations and witnesses

Declaration identity and an instantiated member identity are different:
one AssociatedTypeDef can be used under several constructed interface
instances without creating one definition row per instantiation.
The **candidate instantiated key** is the exact normalized declaring
interface instance plus its resolved AssociatedTypeDef identity.
The declaration must be directly owned by that interface's nominal
definition. Validate local owner/range consistency and then selected-owner
equality after all required imports resolve. A direct provider reference and
a still-imported owner can coexist only after their bindings establish that
equality and retain the original expectations. References retaining an
expected stub, including the direct spelling, must use that expectation's
consumer Module context. Sharing a structural relation does not let another
Module borrow its Import or formal scope; direct cross-Module references
instead name satisfied provider definitions with the retained obligations.

For AssociatedRelation, retain the existing triple:
`(complete declaring interface instance, resolved associated declaration,
complete required type)`. Constraint.ownerGenericParam supplies the subject
and using context under the separate kind-3 payload candidate. Resolve the
subject's selected witness, then its binding for that exact instantiated
member key, substitute and compare against the required type by exact
normalized identity. Neither the default nor a declaration reference
creates a witness, infers the subject backward or proves a relation.
An open constraint remains a symbolic obligation, not a fabricated closed
InterfaceWitness row.

A complete witness retains one selected associated binding per exact
declaring-member key in its required inherited closure. Missing, duplicate
or mismatched entries cannot be repaired from a same-named declaration or
the presence of a default. A producer may select the checked default when
forming the witness; the consumer does not synthesize a missing witness
binding. Explicit bindings may differ from the default. Preserve the
selected value, explicit/default origin and any inherited resolution using
their separately admitted codecs; equal value/default types do not identify
the origin.

Close base-interface instances before selection. Repeated diamond paths to
the same instantiated declaring identity share one requirement; different
constructed instances or unrelated declarations remain distinct. An
inherited use names its actual base declaration/interface instance, not
the most-derived interface or its physical member position. Conflicting
defaults require the existing explicit resolution, never path order.

Substitute declaring-owner arguments first, including constant values;
map enclosing/selected method formals by kind and ordinal where the using
constraint requires them. Declaration IDs do not themselves become new
IDs under substitution. Bind only InterfaceDef's explicit self formal as
self; an ordinary argument equal to self in one instance remains ordinary.
Qualified-self projection preserves the selected concrete associated binding
and checks the newly substituted relations; it cannot reselect a default or
retarget an ordinary argument. Existential property/relation obligations
remain their own typed facts, not additional interface-witness slots.

## Replacement, remapping and compiler propagation

These **candidate preservation obligations** apply to the identity slice;
they do not authorize a private encoding as normative interchange.

| Boundary | Required preservation |
|---|---|
| Replacement | Preserve the selected Module/key/declaration and exact owner, every expected default contract, instantiated member key, relation and selected witness binding. Same names, row numbers, keys or layouts alone prove nothing. A provider's extra fact used by specialization remains an additional retained dependency, not a substituted weaker expectation. |
| Packing and trimming | Decode under explicitly selected source/destination schemas. Map the `0x20` payload through the AssociatedTypeDef map; map a `0x1C` payload through the Import map and its expected `0x20` definition through the declaration map. Never map either through TypeSpec, Body, AssociatedRelation or InterfaceDef merely because numbers coincide. |
| Owners and ranges | Remap AssociatedTypeDef.ownerInterfaceDef through the InterfaceDef map and InterfaceDef.ownerTypeDef through the TypeDef map; preserve Module ownership and every exclusive associated range. Rebuild range starts/counts and dependent interface/witness ranges after reordering, deletion or cloning. |
| Types and using contexts | Remap defaults and relation/witness complete TypeSpecRefs, nested type/constant references, formal owners and ModuleRefs using their own namespaces and substitution contexts. Only eligible complete-type fields admit F2/F3. Do not reinterpret an inline payload as a row fixup. |
| Relation linkage | Under the kind-3 candidate, remap Constraint.ownerGenericParam through GenericParam and Constraint.payload through AssociatedRelation; then remap the relation's declaration word separately. Preserve the logical dependency comparator's complete identity tuples, not raw DefId-bit ordering. |
| Direct binding | Replace an Import use with the selected provider's candidate DefId only after the owner/member and complete contract checks succeed. Retain all original expectations and stronger relied-on facts that survive the rewrite. Do not delete an owner/member stub or Import while another retained context still requires it. |
| Shared structures | Clone/remap a shared relation or TypeSpec when resolving one consumer changes its meaning for another. Equal bytes/names and two imports reaching one provider do not erase independently retained contracts. Rebuild counts, extents, variable indexes and any affected typed source/debug/reference slots, not matching byte patterns. |
| Bound trees and lowering | Carry typed declaration identity, exact interface/substitution, selected binding/origin and retained dependencies through shared traversal, rewriting, persistence and specialization. Lower only from admitted selected facts; retain symbolic obligations where required rather than erasing unsupported ones or repeating source lookup. |

Cached binding/verification decisions remain valid only for unchanged
relied-on identities, complete contracts, contexts, schema/rules and
target/runtime dependencies. An in-progress graph pair or resource cutoff
is not successful comparison. Rewriting imports does not repair native
code that already depends on old types, defaults, layouts or witnesses.
No live-replacement, unloading or owner-migration protocol is added.

## Safe consumption, authority and non-goals

The following checks are **candidate required safe-consumption additions**,
under the existing [reader-trust boundary](../specs/representation/reader-trust.md).
Before affected binding, lowering or execution, validate:

* Exact selected schema agreement, required section policies/shapes,
  checked counts/extents and support for the retained semantic closure.
* Discriminator, nonzero row and correct table bounds for every declaration
  reference; expected kind for every Import token; candidate kind alone
  does not admit an otherwise unsupported generic/witness use.
* Unique interface descriptor, owner nominal-interface kind, contiguous
  exclusive ranges, owner words and inherited Module ownership. Validate
  Import/ModuleRef ownership, eligibility and Module-wide Export-key
  uniqueness, and reject proxy/owner re-export.
* Complete TypeSpecRef structure, allowed field-position qualification,
  nested reference scope, formal kinds/arity and supported dependencies;
  zero is absent only for `defaultTypeSpec`, not relation/witness types.
* Required owner/member bindings, exact instantiated owner correspondence,
  default comparison and the complete retained compatibility obligations.

Producers check authored source, truthful selected defaults/witnesses and
relation satisfaction. Consumers validate safe structure, supported
references and required binding compatibility; they need not re-prove
producer-published body facts or audit arbitrary provider behavior.
Consumers forming a new instantiation/binding must establish its constraints
from supported published evidence. Missing evidence, incompatible contracts,
unsupported semantics or exhausted comparison are not successful results.
An inspector may report a recognized numeric shape with unsupported
dependencies; that is not admission to use it.

| Layer | Consequence and boundary |
|---|---|
| Grammar/source | No production, lookup rule, default-selection rule or inference source changes. Existing associated declarations and exact identity clauses retain their meaning; this is their candidate declaration codec. |
| Libraries/authority | Any admitted interface can use the generic mechanism. Conformance remains with the implementing declaration's authority, with privileged Core publication for primitive witnesses. Importing/exporting an associated declaration creates no conformance or library privilege. |
| Runtime | No new value, reflection service, dynamic name/witness search, dictionary, type-equality call or implicit execution is introduced. Admission never invokes a getter/provider body to discover identity. |
| Target/ABI | No field/storage layout, vtable slot, calling convention, dispatch ABI, marshalling or native-symbol rule changes. Declaration identity is not an address or target carrier. |
| Metadata/binding | Add only the candidate kind, the two-field reference union, Import/Export targets and the owner/default/preservation rules above. Full capability registration and unrelated mandatory record gaps remain separate. |
| Verification/lowering | Preserve the typed facts and check before-use structure/support/binding. Identity/equality supplies no Copy, write, initialization, ownership, cleanup, pinning or caller-unsafe authorization. This document implements none of that propagation. |

## Alternatives and exact adoption boundary

The recommendation is the smallest coherent addition to the existing
first-class identity requirement, not a claim that current draft numbers
are immutable.

| Alternative | Material tradeoff |
|---|---|
| Candidate definition kind `0x27` matching section `0x0027` | Equally expressible and the same width, but section and definition namespaces already differ (for example UnionCaseDef). Recommend the explicitly identified `0x20` candidate alongside definition kind `0x21`, with an explicit section mapping; do not admit both numbers as aliases. Body section `0x0020` is not a collision in the definition namespace. |
| Raw AssociatedTypeDef row plus implicit owner binding | Saves no reference bytes and either loses first-class Import identity or adds implicit member matching. Reject name/range-position matching and Import-to-raw-row reinterpretation. |
| Owner-relative stable member key without independent member Exports | Could remove a member Import/Export edge, but needs a new key namespace, owner-relative lookup/reference schema and replacement rules. It substantively replaces the accepted first-class Import/Export direction; there is no evidence here that the redesign's cost is justified. |
| New AssociatedTypeRef table or separate reference-kind tag | Adds a table/indirection or row width without resolving an additional semantic distinction. Existing `0x20`/`0x1C` discriminators suffice for this slice. |
| Redesign InterfaceDef/AssociatedTypeDef to carry independent access/identity data | Appropriate if broader access or declaration families need it, but not required for the owner-derived interface-contract slice. Do not silently erase restrictions to avoid that future design. |

If approved, integrate the following amendments together; **none are made
by this document**:

| Governing owner | Candidate amendment |
|---|---|
| [Logical witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain) | Replace only the statement that AssociatedTypeDef's `0x20`/binding codec remains unassigned with the adopted reference/owner rules; preserve other codec and admission gates. |
| [Physical metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Extend the DefId kind list and only the Import/Export target sets, specify the two AssociatedTypeReference fields, ownership/stubs/export eligibility and complete-default contract without changing row strides. |
| [Machine registry](../specs/representation/trrm-1.registry.json) | Add candidate `["20","AssociatedTypeDef"]` to `metadata.definition_kinds`, `20:AssociatedTypeDef` to both `metadata.import_export_target_sets` arrays, and explicit two-form associated-reference and ownership rules for the two fields. Preserve section kinds, Body-map target sets and unrelated definition positions. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md) | Add the selected-owner/member binding and exact-default specialization, including instantiated identity and retained rewrite obligations. |
| [Reader trust](../specs/representation/reader-trust.md#4-schema-owned-reference-namespaces) and [conformance](../specs/conformance/il-binding-and-replacement.md) | Extend namespace/validation rules and exercise the candidate cases below, including the existing IBGR-17 first-class declaration requirement. |
| [Kind-3 payload candidate](constraint-associated-relation-payload.md) | Replace its explicit identity-codec deferral only after this decision is adopted; keep payload-to-relation targeting separate. |

The [format lifecycle](../specs/representation/format-versioning.md) permits
this draft amendment without promising old-draft compatibility or a new
version pair. Exchange still requires exact schema agreement even if both
artifacts say `(1, 0)`. Do not guess this codec from kind bytes, table
presence, filename or manifest, or fall back to an older meaning after a
check fails. Conversion, if offered, selects both schemas and rebuilds
decoded references; no legacy alias/reader or byte-only conversion is
required. No remaining choice inside this candidate identity slice is
delegated to a future implementer; review must still approve, reject or
amend these unapproved decisions. Adjacent representation gates above are
not presumed solved.

## Candidate acceptance cases

These are proposed conformance obligations, **not tests run or current
support claims**. Use independent small artifact vectors and unrelated
synthetic providers, not only one writer/reader round trip.

| Class | Case | Candidate outcome |
|---|---|---|
| Positive | Local relation and witness use `0x2000_0001` with correct owned AssociatedTypeDef row 1 | Resolve the declaration in section `0x0027`; retain its exact complete interface instance. |
| Positive | Consumer-local owner/member stubs bind through separate explicit keys to the same selected provider interface/member | Validate both contracts and owner equality; member names and physical rows are not selectors. |
| Positive | Different expected names/formal names, String encodings and row order; equal selected identities and corresponding default terms | Same binary identity/comparison, subject to independently retained source/publication obligations. |
| Positive | Default has equivalent TypeSpec-row/F2 or eligible row/F3 forms; both defaults absent | Exact default comparison succeeds; spelling does not replace semantic identity. |
| Positive | Two retained imports reach one provider definition with separately compatible expectations | Check both; do not coalesce away either expected contract. |
| Negative: namespace | Raw row, `0x2700_0001`, F2/F3, wrong kind, zero row, reserved `0xFF`, absent table or out-of-range row | Reject before affected use, without masking/fallback or probing another table. |
| Negative: Import kind | Imported form names an Import expecting TypeDef, MethodDef, FieldDef or an associated constant | Reject; the general Import token does not erase this field's expected kind. |
| Negative: owner structure | Wrong/zero owner, non-interface TypeDef, duplicate InterfaceDefs, orphaned/overlapping range, wrong row owner or truncated section | Reject the selected malformed structure with checked extent/range arithmetic. |
| Negative: stub/publication | Stub is attached to the provider instead of its consumer owner; ModuleRef is owned by another consumer; imported member/owner is re-exported | Reject; neither a key nor a public flag repairs ownership. |
| Negative: key/selection | Empty/duplicate Module key, unresolved selected key, wrong provider owner or equal names/layout under another interface | Reject that binding; do not search names, other keys or another revision. |
| Negative: default | One default absent, different signedness/qualification/nominal type, out-of-scope formal or wrong argument kind | Fail the exact default/formation comparison; equal layout or one successful closed substitution is insufficient. |
| Negative: witness | Missing/duplicate binding for an exact member key, conflicting inherited defaults or a required type differing from the selected value | Reject new source/witness/instantiation formation as applicable; do not synthesize evidence from the declaration/default. |
| Boundary | Declaration/import row 1 and `0x00FF_FFFF` under their respective valid table bounds, versus row 0, count+1 or oversized counts/extents | Accept only bounded correct-kind words; exercise max-row owner ranges without overflow or a portable-capacity claim. |
| Boundary | AssociatedTypeDef, Import, InterfaceDef, Body and AssociatedRelation all contain the same numeric row | Each word uses only its schema-selected table; no numeric coincidence establishes ownership or identity. |
| Cross-feature: generic | Same declaration under two interface type/constant substitutions; defaults depend on owner/self formals; relation also references method formals | Preserve distinct instantiated member keys and exact substitution order; closed coincidence never proves open equivalence. |
| Cross-feature: inheritance/projection | Diamond repeats one closed base versus two distinct constructed bases; qualified-self projection with an ordinary equal-to-self argument | Deduplicate only the same instantiated identity; preserve the selected binding and ordinary argument, without default reselection. |
| Cross-feature: normalization | Finite nominal anchor versus direct/indirect transparent default expansion, including growing arguments | Permit the nominal anchor; source formation rejects nonfinite expansion and comparison never reports a cycle/cutoff as success. |
| Cross-feature: direct proxy | Direct `0x20` reference names an expected stub whose Import is incompatible | Follow the Import and refuse affected use; direct spelling cannot manufacture local authority. |
| Cross-feature: remapping | Independently permute declaration/Import/interface/type/formal/relation/witness rows, then direct-bind only one consumer of a shared structure | Rebuild every typed edge/range/index and clone changed contexts; preserve all retained expectations and dependency ordering. |
| Cross-feature: replacement | Same keys now select a different owner/member, change a retained default or relied-on witness binding, or preserve only a broad old expectation | Compatibility fails for affected retained obligations; no automatic reselection or stale acceptance cache. |
| Cross-feature: trust/support | Structurally valid false published equality versus malformed owner/reference versus an unsupported witness/access capability | Distinguish producer truth violation, mandatory structural rejection and unsupported affected use; no body re-proof requirement or success-shaped fallback. |
| Cross-feature: schema | Equal `(1, 0)` without agreement on this candidate, or section `0x0027` under the unchanged accepted schema | Do not infer candidate `0x20` or extend target sets; recognize the existing gate without claiming binding/execution support. |

## Current implementation boundary

The C# [definition resolver](../../sources/Terrum.CodeAnalysis/Modules/TrRmDefinitionResolver.cs)
now has a **provisional** AssociatedTypeDef kind `0x20` path through
AssociatedTypeDef -> InterfaceDef -> TypeDef. The
[Import reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs)
and [Export reader/writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs)
receive those validated tables in standard structural validation. They
validate explicit member expectations, the consumer-owned owner stub and
same-consumer ModuleRef ownership, public provider owners, expected-stub
non-reexport and Module-wide export-key uniqueness. This implements only the
candidate row identity and Import/Export gates; it does not bind the selected
provider owner/member or compare defaults.

The [structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs)
composes InterfaceDef, AssociatedTypeDef, base/method/constant declaration
tables and Import/Export readers. Relation and witness declaration references
are not yet admitted. The [Constraint reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstraintTableReader.cs)
validates kind 1 through TypeSpecRef, but kind 3 still only receives a
nonzero-payload check, not an AssociatedRelation table/count.

Exact selected-owner/default comparison, AssociatedRelation and witness
binding, dependency/replacement propagation and remapping remain unimplemented.
They must be added with the separate payload, TypeSpecRef and runtime
prerequisites before affected use is admitted. [CG-001](../implementation/conformance-gaps.md#cg-001)
and [CG-006](../implementation/conformance-gaps.md#cg-006) retain their
existing acceptance gates. This proposal remains unapproved: the provisional
implementation does not approve its allocation or any source, library,
runtime or ABI policy, and establishes no milestone completion.
