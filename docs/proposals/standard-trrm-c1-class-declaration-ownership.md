# Standard TRRM C1 class declaration ownership

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This proposes a declaration fact and an exact
type-only publication composition, not implementation support or a runtime ABI.

## Finding and smallest decision

**Source semantics are settled; general source-free class ownership evidence
is missing.** [Language ownership](../specs/language.md#types-and-operations)
distinguishes ordinary aliasable class handles from unique owning handles.
[Complete types and generic domains](../specs/language/types-and-normalization.md#generic-formal-domains-and-specialization)
require that distinction when admitting actuals. It is not a new constraint,
an inference from fields, or an implication of nominal class kind.

The [metadata flag table](../specs/il-metadata.md#section-kinds-and-row-layouts),
[registry owner](../specs/representation/registry.md) and its
[machine-readable assignments](../specs/representation/trrm-1.registry.json)
assign TypeDef only visibility/kind: mask `0x0F`, class kind 2.
[C1M1](standard-trrm-c1-class-declaration-mutability.md) adds only declaration
mutability `0x80`. Neither says whether a class is unique.

The source-free
[`BoundGenericNominalTypeContract.IsAdmittedByDomains`](../../sources/Terrum.CodeAnalysis/Semantics/BoundGenericNominalTypes.cs)
now refuses non-Core kind-2 actuals for ownership-dependent domains instead
of treating class kind as Copy evidence. The generic nominal definition still
has no ownership fact; the C1M1 class TypeDef view likewise retains none.
[`CreateGenericNominalTypeDefinitions`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
retains kind but no such fact.
[`TrRmTypeDefTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs)
recognizes `0x20/0x40` only in separately selected struct-evidence schemas.
These are source observations, not validation results.

Blanket kind-to-Copy admission was an **implementation error** and now fails
closed in the source-free generic nominal checks; this correction needs no
language proposal. Checked ordinary-class source carries a known aliasable
fact, and the current C1M1 writer accepts only that fact. But supporting
ordinary **and unique** class declarations
as source-free generic actuals needs a published discriminator. The existing
[generic class reference proposal](standard-trrm-c1-generic-class-typedef-publication.md)
excludes unique source roots; it does not supply that general discriminator.

**Recommend one total class-declaration Boolean, `0x40 =
DeclaredClassUnique`, under the exact C1M1 successor below.** Preserve class
kind 2 and existing nominal identities. Known-clear means ordinary aliasable
handle ownership; known-set means unique handle ownership. Availability is
separate: another schema's clear reserved bit is unknown, not Copy.

| Alternative | Assessment |
|---|---|
| **Class-only `0x40` Boolean — selected** | One bit, outside both the current and enum draft kind bands; mirrors accepted `unique` and aligns numerically with the struct Unique candidate without composing it. Complete class declarations require a known value. |
| Class-only `0x20` Boolean | Equally small; could encode positive ordinary-handle Copy with clear meaning unique under an exact schema. Prefer `0x40` to keep the draft Copy/Unique positions intelligible, not because either allocation is frozen. |
| Positive Copy `0x20` plus Unique `0x40` | Coherent three-state carrier: neither unknown, exactly one known, both invalid. Useful if incomplete ownership must be representable within this new schema. Not selected: this route promises complete class declarations, and schema-aware availability already preserves unknown older origins. Unlike struct inline Copy, ordinary class-handle Copy does not depend on every payload field being Copy. |
| Dedicated TypeDef contract and required Feature | Could require exactly one class-owned ownership clause with tags `1=aliasable`, `2=unique`, zero flags/reserved bytes, absence unknown, and per-owning/using-Module admission including bodyless expectations. Adds clause/Feature allocations, cardinality, references and retention rules for a mandatory fact; independent optional admission is unnecessary here. No such allocation is selected. |
| Repack flags or give unique/static/union classes distinct kind codes | Permissible unpublished redesign, including moving current draft assignments. A larger kind band/role field could encode more declaration families, but changes every kind consumer and adds no value for this independent Boolean. No width saving or identity benefit justifies it here. |
| Infer Copy from class kind, no Cleanup, layout, fields or producer operation names | Rejected for general consumption. A producer's source restriction is not a replacement for required declaration facts in arbitrary retained providers/expectations. |

## Exact successor and flag rules

Select ordinally and case-sensitively:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-class-uniqueness-1`**

Call this **C1M1U1** locally. Its complete agreement is C1M1, including
[C1's reconciled catalog](standard-trrm-exception-class-witness-binding.md#exact-schema-and-catalog),
plus the explicit type-only generic-reference role from the
[generic publication owner](standard-trrm-c1-generic-class-typedef-publication.md),
with exactly the ownership and admission amendments here.
The C1M1 agreement and its ordinary-only producer route keep their meanings;
neither becomes an alias. No constructor, label, Write, CG1, array or private
schema joins merely through inheritance links.

Keep `(1,0)`, edition 1 and Body codec/encoding `01`; add no section, row,
TypeSpec form, Feature or API/CLI default. Require this exact selection for
the root and every supplied provider before schema-dependent indexing,
publication or reference construction. No sniffing, subset retry or automatic
conversion is selected. [Exact draft agreement](../specs/representation/format-versioning.md)
allows replacing unpublished assignments, not guessing meanings from equal
version bytes. Broader cross-schema binding remains separately defined work.

| TypeDef item | Exact C1M1U1 candidate |
|---|---|
| Framing | Existing 44-byte row; little-endian `u32 flags` at offset 8. |
| Visibility/kind | `flags & 3`: private 0, internal 1, public 2; 3 invalid. `(flags >> 2) & 3`: struct 1, class 2, interface 3; zero invalid. |
| Ownership | `0x40 = DeclaredClassUnique`, only on kind 2. Every retained class definition and expected proxy supplies the total set/clear fact. |
| Mutability | C1M1's unchanged class-only `0x80 = DeclaredClassMutable`; independent of ownership. |
| Mask | Exactly `0x0000_00CF`; bits 4, 5 and 8..31 reserved zero. Struct/interface rows additionally clear `0x40/0x80`. |
| Public class examples | Immutable aliasable `0x0A`; immutable unique `0x4A`; mutable aliasable `0x8A`; mutable unique `0xCA`. |
| Admission | Supporting this class declaration schema includes ownership semantics, even without Bodies or generic methods. Retain all independent C1 per-Module requirements; TypeDef formals alone do not require method-call Feature 23/1. |

This intentionally amends the successor's TypeDef reserved-bit and
visibility/kind-only rules; it does **not** amend the accepted registry.
[A2](standard-trrm-rootless-copy-type-evidence.md)'s `0x20` remains a positive
universal **struct inline Copy** promise.
[A3](standard-trrm-closed-constructed-array-elements.md#positive-nominal-uniqueness-evidence)'s
`0x40` remains positive **struct** Unique, with neither bit meaning unknown
and both invalid. C1M1U1 imports neither carrier: `0x20` is invalid here,
and `0x40` on a struct is invalid. No shared reader may widen applicability
or reuse A3's clear-bit interpretation merely because the number matches.
A future composition must explicitly reconcile masks and kind-specific
absence semantics; no compatibility aliases or combined schema are selected.
The [G1 generic-struct source-closure proposal](standard-trrm-generic-typedef-source-closure.md)
and its schema/admission remain separate and unchanged.

## Declaration scope, availability and complete types

The Boolean describes the declaration's class-handle ownership contract for
**every well-formed instantiation admitted by the declaration**, not a
favorable specialization. Ordinary handles alias one object; copying them
does not copy payload fields or duplicate object-level Cleanup. A unique
class remains unique with trivial/empty fields and without Cleanup.
Neither declaration mutability nor a formal's domain determines the owner's
uniqueness.

Under [the distinct source kind contracts](../specs/grammar-spec.md#31-source-file-structure-and-declarations):

* **Include non-static, non-union instantiable classes**, generic or nongeneric
  in retained closure, with independent optional nominal `mut` and `unique`.
* **Exclude static classes from this class-value route.** They are
  non-instantiable containers, cannot combine with nominal `mut`/`unique`,
  and supply no handle actual for an ownership domain. Publishing their
  container identity/initialization role needs a separate explicit contract;
  clear ownership is not that role.
* **Do not extend publication to class unions in this composition.** Their
  accepted ordinary/unique handles obey the same ownership distinction, but
  their cases, active-payload validity and evolvable/closed case-set contracts
  are distinct required facts, with separate incomplete union admission.
  This is an explicit bounded candidate choice, not a source-language ban.
  A future union composition must preserve those facts and distinguish that
  declaration role; it cannot encode a union as an ordinary class with fields.

In C1M1U1, kind-2 rows assert the included instantiable, non-union declaration
role. This adds no static/union role discriminator. Producers must not erase
those roles to fit the schema; consumers check supported required structure,
not reconstruct omitted source modifiers.

Keep **unknown / known aliasable / known unique** distinct in bound
declarations, source captures, references, expected and actual provider views:

* Checked composed source supplies a known fact; preserve it before rewrites.
* Selected C1M1U1 class rows supply a known Boolean; there is no wire unknown
  state in this complete declaration view.
* Standard, C1, C1M1, G1 and the struct-only profiles supply no class fact by
  a clear reserved bit. An otherwise admitted opaque identity may remain
  unknown; it cannot be emitted as known-clear C1M1U1 or satisfy a use needing
  ownership evidence. No conversion is defined here.

After exact forward substitution and normalization, check the **complete
actual**:

| Complete class-handle actual | Domain 1: Copy | Domain 2: unique-only | Domain 3: Copy or unique |
|---|---|---|---|
| Known aliasable | Admit | Refuse | Admit |
| Known unique | Refuse | Admit | Admit |
| Unknown ownership | Refuse | Refuse | Refuse |

These are ownership checks, not sufficient overall admission. Independently
check nullability tags 1/2, every argument kind/domain/default and supported
constraint, even for phantom formals. Domain 3 does not mean arbitrary
unknown or unsupported types. For open forwarded formals, require the
source's proven domain to be contained in the destination domain; refinements
use existing exhaustive-property rules. Intrinsic Constraint property
`1=Unique` uses the same canonical evidence; add no Copy constraint or witness.

Preserve optional and inner/outer access/representation qualifications.
`write` does not change duplication rights; GC does not erase uniqueness,
but needs its independently admitted representation. `ref`, `ptr`, `own`,
`box` and `val` retain their own complete-form rules: do not classify a pointer
cell as unique merely because its pointee is unique, or infer inline-payload
Copy from aliasable class handles. Core Task retains its independently selected
affine contract, not a general kind/name heuristic or fallback for unknown
class facts. Contradictory declaration/family ownership facts refuse.

## Publication, verification and replacement

Extend the candidate `EmitStandardGenericClassTypeDeclarations` operation
and explicit type-root reference operation to C1M1U1, permitting checked
ordinary **or unique** generic class roots only under this selection. Reuse
their exact-program/source-anchor/key inputs, declaration capture and
unavailable-member scope; do not reinterpret an empty member map as this
operation. Check the whole composed source over its formal domains, including
real constructor/member implementations, without requiring physical object
layout. Unsupported checking refuses, never yields a successful empty capture.

Retain the complete promised formal/default/constraint, access, field,
Layout and required contract closure, including private dependencies and
bodyless expectations. Populate ownership from the checked declaration,
not fields, Cleanup absence, source spelling caches or one specialization.
Type-only references can use the fact for supported type formation/domain
checking; they still promise no constructor/member/field execution, storage,
Copy execution or cleanup operations. Missing member surfaces remain
unavailable, not known empty. Required unrepresentable closure refuses.

This fact completes neither the narrow `0005` Cleanup clause's
role/ownership prerequisites nor the
[accepted object-level cleanup carriers](../specs/representation/logical-records.md#general-dependency-contracts-carrier-and-capability-integration-pending).
It does not widen that clause's current unique-only applicability to aliasable
objects or import the struct Copy/Cleanup exclusion as a class-handle rule.
All source cleanup obligations remain checked; unavailable serialized
obligations cannot be dropped.

Apply [consumer verification](../specs/representation/reader-trust.md):
check exact schema/support, extents, closed masks and applicability,
references, owners/ranges and retained bindings before affected use.
Producers owe truthful declarations; ordinary consumers do not infer
ownership from fields or semantically re-prove source/Bodies. A structurally
consistent false flag is producer misconduct, not a required Body audit.

Under [binding/replacement](../specs/representation/binding-and-replacement.md),
resolve exact Module/revision/key/kind and formal owners first. Compare
class ownership **exactly in both directions**, independently of mutability;
neither aliasable-to-unique nor unique-to-aliasable is strengthening.
Check every original expectation, alias equality, complete substituted
contract and additionally relied-on fact separately from actual-provider
facts. Unknown is not a wildcard for an ownership-dependent binding.

Source-free reconstruction preserves the Boolean and its availability
alongside the existing nominal identity; allocate no second identity or
per-instantiation TypeDef. Rebuild Module partitions, TypeDef/Import/Export,
GenericParam/Constraint owners and kind-directed payloads/defaults,
TypeSpecs `02/03/04/05` and nested terms, FieldDef/Layout/Contract ranges and
affected signatures/Body maps. Remap in each original using/formal context,
clone context-dependent shared rows and retain original expectations after
Import-to-direct packing. The flag is a scalar, not a row reference.
Use finite contextual worklists, closing nominal back-edges only after their
outgoing obligations succeed. Malformed structural cycles, growing
substitutions and resource refusal cannot succeed as equality.
Changed schemas, declarations, substitutions or providers invalidate affected
cached checks. Failure returns no partial successful reference/output.

## Implementation gaps and acceptance

Implementation must add schema-aware fact availability to nominal/class
declaration capture and expected/actual views, selected readers/writers,
domain/property checking, dependency binding, shared traversal/verification
and re-emission. The current kind-only nominal definition, struct-only
ownership reader and
[`mutability-only class view`](../../sources/Terrum.CodeAnalysis/Semantics/BoundStandardClassDeclarations.cs)
cannot substitute. Generic class source/declaration support and complete
required closure remain implementation gates; removing guards alone does
not meet them. These are requirements, not code changes made here.

Required future acceptance cases, **not results**:

| Case | Required outcome |
|---|---|
| Source and declaration | Unrelated ordinary/unique generic classes, each mutable/immutable, retain all four flag combinations, exact identities and complete fields/layouts; include empty/trivial unique-without-Cleanup and aliasable handles whose payload is not Copy. Fully checked constructors remain real source implementations but unavailable operations in the type-only view. |
| Domains/substitution | Domains 1/2/3 crossed with both nullability tags, nested constructed/default/optional actuals, phantom formals and forwarded open domains follow the table; unknown refuses even domain 3. Pointer/reference/owner forms use their own contracts. No favorable specialization establishes a declaration fact. |
| Malformed/support | Refuse reserved `0x10/0x20` or high bits, `0x40/0x80` on struct/interface, invalid kind/visibility, truncated rows, wrong owners/ranges/arity and unsupported required closure. Static/union source cannot silently become ordinary class publication. |
| Replacement | Same-name/key/fields/Layout but opposite ownership fails in both directions; mutability mismatches still fail. Check all using Modules/aliases after earlier successful bindings; corresponding formal renaming/row movement can agree, changed domains/defaults/constraints cannot disappear. |
| Source-free remapping | Select a non-first provider with source/debug absent; permute valid definition/formal/constraint/TypeSpec/import/field/Layout rows, pack Imports to direct definitions, re-emit and reconsume. Preserve both ownership values, unavailable-member scope and every original expected/actual context. |
| Isolation/trust | Old C1M1 clear is unknown, not ordinary; A3 struct bits do not select C1M1U1. Reject mismatched supplied selections before output. False but structurally consistent producer facts are separate optional-audit cases. |
| No runtime promotion | Type/domain success supplies no class size/header, constructor/member execution, roots/GC/cleanup ABI, instance lowering or native execution evidence; unsupported affected uses still refuse. |

The in-scope candidate choices are explicit: total class-only `0x40`,
C1M1U1 exact selection, unique type-only roots, no static/union expansion.
They remain **unapproved/provisional**. Complete generic constructor/member
publication requires a separately selected extension of its owner contract;
object layout, GC/ownership ABI, generic-class instance lowering and union/
static publication remain independent decisions/gates. No normative owner,
implementation inventory, compiler code or tests are changed.
