# Closed constructed nominal elements in standard fixed arrays

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This recommendation approves no source, schema,
API or runtime policy and establishes no compiler conformance.
[Proposal index](README.md).

## Decision and scope

Recommend **A3, a distinct exact schema candidate for bounded declaration-only
generic storage admission**:
reuse nominal constructed TypeSpec `0x05`, GenericParam/Constraint and
TypeDef/FieldDef/Layout/LayoutField records; substitute complete type actuals
before deriving storage; retain A2's universal positive Copy declaration and
check rootlessness on the resulting stored children. Its uniqueness-amended
successor adds positive nominal `DeclaredUnique` evidence as selected by the
[companion proposal](standard-trrm-a3-positive-unique-type-evidence.md); it
does not introduce a per-instantiation nominal definition or physical-layout
table.

The missing decision is **how to select this reusable generic declaration
and closed-storage subset without selecting general generic execution/export
or witness capabilities**. Recommend the exact identity
**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`**, not A2
plus agreement to another document. The flag-free A3 identity `...-3` does not
select this complete composition; there is no alias or fallback. Substitution,
nominal identity and target
placement themselves are already specified; rejecting generics in a layout
helper is not evidence of a missing layout algorithm.

When A3 is explicitly selected, this supplies the generic prerequisite in the
unapproved
[source array proposal](standard-native-fixed-array-source-contract.md#extent-and-identity)
for this bounded subset. A2 and the external-admission gate in
[A2's generic section](standard-trrm-rootless-copy-type-evidence.md#generic-and-constructed-subjects)
remain unchanged. A3 permits type-only generic declaration exchange and closed
inline storage,
not generic callable export, open Bodies, constructors/method specialization,
constant-formal recipes, existential values, interface dispatch or a generic
dictionary ABI. Actual array construction, values, places, calls and native
execution still need their independently admitted carriers.

## Existing authority and the remaining boundary

The [specification index](../specs/README.md) leads to these owners:

| Evidence | Determined contract; distinction from this decision |
|---|---|
| [Grammar §§3.1–3.2](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [complete types](../specs/language/types-and-normalization.md#forward-substitution-and-normalization) | Select the nominal declaration; preserve owner/ordinal, ordered complete actuals, defaults and qualifications; substitute forward without replacing a nominal instance by its carrier. No new generic syntax is needed. |
| [Generic logical records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending), [TypeSpec and declaration rows](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Form `05`, the 32-byte GenericParam and 12-byte Constraint already exist. Intrinsic property `3=Blittable` is identified; default Copy is a domain, not an explicit Copy constraint or a rootless guarantee. Their shapes do not supply generic admission. |
| [Metadata layout](../specs/il-metadata.md#semantic-contracts-and-stable-binding), [Windows layout §§4–5](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Layout carries declared inputs and ordered real fields, not completed size/offsets. Derive those under the target after field substitution; no per-closed-instance physical record is missing. Blittable, Copy and rootlessness are distinct obligations. |
| [A2 evidence](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule) | Already selects a universal declaration Copy promise and recursive closed-storage checking, explicitly including independently admitted constructed instances. It does not admit the generic representation on its own. |
| [Generic/witness admission](../specs/representation/requirements-and-admission.md#4-source-obligations-and-representation-completion), [metadata's generic gate](../specs/il-metadata.md#semantic-contracts-and-stable-binding) | General generic execution/export remains separately gated. Recognized TypeSpec/Constraint rows are not executable generic support. This bounded admission choice is not yet supplied. |
| [Exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts), [schema selection](trrm-draft-schema-profile-selection.md) | The selected schema includes its semantic catalog, not just row bytes. The sole out-of-band schema identity cannot distinguish A2 from A2 plus an additional proposal. A changed composition needs one distinct exact selector unless A2 is deliberately replaced as a whole. |
| [Associated-relation payload](constraint-associated-relation-payload.md), [associated-type identity](associated-type-definition-identity.md), [witness binding origin](witness-associated-type-binding-origin.md), [private interface/witness composition](source-interface-witness-private-persistence.md) | Resolve separate reference/origin or private-graph questions; they do not select Standard A2 generic-storage admission. The private composition explicitly supplies no Standard generic/witness Feature registration and gates wider generic value declarations. |

Existing prototypes and APIs do not select generic storage admission or alter
the cited contracts. Current implementation and qualification evidence belong
in the implementation inventory, not this design proposal.

## Alternatives

All choices below are unapproved.

| Direction | Assessment |
|---|---|
| Retain A2 semantics | Keep its independently admitted generic prerequisite and decline this new generic-storage route when that prerequisite is unavailable. Coherent, but does not supply the requested bounded admission. A helper fix or another document cannot implicitly extend the A2 selector. |
| **A3 with existing declaration templates and exact substitution — recommended** | Existing form `05`, formal/constraint rows and inherited A2 Copy evidence retain identity and formation. Existing Layout inputs suffice to derive each closed representation. The distinct successor selector also assigns the positive TypeDef `DeclaredUnique` bit from the companion proposal; no per-instance records or generic execution capability are added. |
| Explicit closed-instantiation records with per-instance capabilities/layout | A substantive alternative could retain the original generic definition and complete actuals while publishing separately keyed instance facts. It needs exact instance ownership, field correspondence, provider availability, replacement and capability semantics, and a new required encoding/composition. It must not disguise one favorable instance as a universal Copy promise. Enumeration or conditional recipes are materially wider than this closed-storage requirement. |
| Complete general generic/witness capability first | Appropriate for constrained behavior, associated projections, constant recipes and independently callable generic code. The existing proposals remain prerequisites there. Requiring that entire surface for type-only, property-bounded storage adds no necessary representation fact. |

### Exact schema decision

**Select the amended A3 only by
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`.** Its
complete meaning is A2's F+S+array+Copy composition plus the bounded
generic-storage admission rules in this proposal and the positive nominal
Unique evidence in the companion proposal. The flag-free `...-3` candidate
does not select this complete composition.

The [format-versioning rules](../specs/representation/format-versioning.md)
permit unpublished drafts to be replaced without compatibility duties or a
major/minor increment. They do not permit one selected identity to mean both
the existing A2 contract and an optional extra agreement. Deliberate wholesale
supersession of A2 could be another design, but would require replacing its
definition and all selection owners consistently; that is not this
recommendation. Keeping A2's meaning distinct here is a selection requirement,
not a claim that draft A2 must remain compatible or immutable.

Retain `(major,minor)=(1,0)` and the inherited codec. Add no on-wire schema
discriminator, Feature ID/revision, section, TypeSpec form or runtime policy.
The successor assigns only TypeDef flag `0x40` to positive `DeclaredUnique`,
with the exact A3-only applicability and exclusivity in the companion
proposal. The public selector and its typed/internal counterpart identify
this complete successor, never A2 plus a hidden flag, proposal list or
inferred capability. No selector implementation is performed by these
proposals.

## Proposed formation and declaration evidence

These are **new bounded admission rules**, preserving the linked source and
logical semantics:

1. E is a closed ordinary inline nominal struct instance `G<A0, ..., Ak>`.
   Its selected G has a nonempty ordered **type-formal-only** range.
   GenericParam ownership/nullability domains retain their assigned meanings
   and tags; constant-formal fields are zero. Type defaults retain their
   typed references and ordinary earlier-formal rules. Source selection
   completes defaults before publication; form `05` always supplies exactly
   one complete actual per formal, with no inference or missing-argument
   sentinel.
2. A constraint range is empty or contains only existing
   `kind=2, flags=0, payload=3` intrinsic **Blittable** requirements. Check
   each against the normalized complete actual and selected target (1,1),
   using its genuine representation commitment, not size, spelling or Copy
   alone. No PropertyWitness is created. Interface-instance constraints,
   associated relations, other property requirements, conditional capability
   clauses and constant formals remain outside this initial route. Required
   unsupported facts reject affected admission even on phantom formals,
   unused declarations or N=0; never omit them to fit the profile.
3. Every actual independently satisfies its full type formation, formal
   domain and qualification requirements. Preserve nominal actuals, nested
   constructed instances and pointer depth/access. Actuals need not all be
   rootless: only actual **stored inline children** participate in the
   rootless closure. A phantom actual still participates in identity and
   formation. Missing Copy/ownership or other necessary evidence is not
   repaired by a default domain tag.
4. G publishes inherited A2 `DeclaredCopy=1` and the applicable positive
   Blittable Layout commitment. These are **separate** declaration promises:
   Copy holds for **every well-formed instantiation admitted by the entire
   published formation contract**, across every formal's ownership and
   nullability domain, not only the closed actuals selected for this array
   or currently supported by a backend. A source producer must establish
   both promises from the declaration and formal guarantees; the domain
   rules below govern Copy independently of Blittable.
   A directly stored unconstrained T's default Copy domain cannot establish
   a Blittable promise; an explicit `[Blittable]` requirement or an already
   sufficient published formation guarantee is necessary. A pointer cell or
   phantom T does not require its pointee/unused actual to be Blittable.
   Do not silently add a constraint, condition a Layout flag, or infer the
   promise from one successful scalar instance.
5. Apply A2's existing complete RootlessCopy check to all substituted instance
   fields, including private fields and every stored alternative of an
   independently admitted layout. Each nominal stored child needs its own
   positive Copy evidence. A parent flag, Cleanup absence or zero extent is
   insufficient. Statics are not instance storage, but their retained
   requirements cannot be erased.

### Universal Copy across formal domains

Preserve the accepted
[source domains](../specs/language/types-and-normalization.md#generic-formal-domains-and-specialization)
and [ordinary-struct Copy contract](../specs/language.md#types-and-operations),
and A2's [universal bit meaning](standard-trrm-rootless-copy-type-evidence.md#exact-unapproved-field-and-capability-amendments).
The following applies to **each formal and the complete stored representation
under every admitted combination of actuals**, including nested inline
children:

| `ownershipDomain` | Published domain and Copy consequence |
|---|---|
| `1` default | Non-unique, Copy-capable actuals. This supplies a Copy guarantee for directly stored T under its full admitted domain; it supplies neither rootlessness nor Blittable representation. |
| `2` unique-only | No Copy guarantee for directly stored T. An ordinary Copy G cannot store T itself or an inline child that exposes T's unique responsibility. Blittable unique actuals do not change this result. |
| `3` default plus unique | May admit unique actuals and therefore supplies no universal Copy guarantee for directly stored T. `[Blittable]` alone does not exclude unique actuals; a Copy closed scalar instance cannot justify the declaration's bit. This initial constraint subset supplies no additional Copy restriction that repairs such direct storage. |

`nullabilityDomain=1/2` remains an independent non-null/nullable-allowed
modifier. Nullable admission does not change ownershipDomain, make a unique
actual Copy, or project an optional to its payload. Check the complete actual
and stored type, not only a present value.

Domains 2 and 3 are not blanket exclusions on G. A phantom T or an
independently Copy field representation such as a raw `ptr<T>` cell may leave
G universally Copy because it does not store or duplicate T's unique
responsibility. Its complete formation and all other fields must still meet
the published promises. This supplies no missing unique-actual representation
or ownership codec.

The accepted Blittable source schema also permits unique structs, so a
`[Blittable]` requirement, a Layout flag, trivial bytes or Cleanup absence is
never Copy evidence. Do not add a `[Copy]` constraint, narrow domain 3 to
domain 1, silently drop an admitted unique case, or set `DeclaredCopy` only
after a favorable specialization. In particular, an ordinary declaration
that directly stores T with domain 3 and only a Blittable requirement must
fail Copy publication even if its only requested instance uses int32.

Source producers and rewriters changing the declaration or its relied-on
facts owe this universal truth. Independent consumers require the published
positive universal promise and check the actual closed storage; they must
not manufacture that promise from an instance, but are not required to
semantically re-prove the producer's declaration truth from source/Bodies.
A structurally consistent false promise remains a producer violation under
[reader trust](../specs/representation/reader-trust.md), distinct from an
optional semantic audit.

Illustrative accepted generic/property spelling, with **proposed A3 storage
admission**, not a runnable or compiler-qualified example. Assume the existing
selected Core Layout/Blittable source schemas; the intrinsic property is not
an interface or user attribute with the same name:

```terrum
[Blittable]
[Layout(LayoutKind.Transparent)]
public struct Cell<T> where T : [Blittable]
{
    public T Value;
}
```

The provisional array type `[Cell<int32>; 2]` retains G and int32 in E.
`Cell<box<int32>>` cannot satisfy this Blittable requirement merely because
box handles are Copy-capable. `Cell<void>` retains distinct nominal identity
and the Transparent zero-payload layout. No constructor or initializer is
synthesized by these declarations.

Fixed-array recipes themselves remain closed under the existing source/A2
boundary. A generic definition may contain an already closed array field;
this decision does **not** admit exported `[T; N]` templates, symbolic counts
or a new array-formal substitution codec.

## Existing physical records and closed layout

Form `05` retains `u8 form, u8 qualifiers, u32 genericTypeSpecRow`, followed
by K ordinary TypeSpecRefs in formal order for this type-only subset:
exact row extent `6 + 4*K`. The generic reference selects an ordinary
in-artifact/imported nominal definition row, not a Body token or another
construction. Definition fields may use existing formal form `04` in the
exact TypeDef's scope. Preserve the existing reference, qualifier,
acyclicity and ordinary/canonical encoding rules. This does not replace the
formal-directed ConstantSource alternative in other supported profiles.

For each closed use:

* Resolve G's original import expectation and actual provider declaration.
  Correspond formals by owner, kind and ordinal; bind ordered actuals before
  resolving its fields. Field TypeSpecRefs/formals resolve in the defining
  Module, while supplied actuals retain their own resolved origins.
* Substitute and normalize each real field term, including nested generic
  owners, before storage checking. Enter a nested declaration with its own
  formal environment. Retain the real FieldDef and complete constructed
  owner; a derived closed struct carrier is not a new exported TypeDef.
* Select G's complete Layout under the accepted exact-profile/default rules
  and derive field offsets, size s, alignment a and transitive explicit
  alignment R using Windows x64 placement. No per-instance offsets/sizes are
  serialized. A2 array rows still require an explicit TargetProfile (1,1),
  not a default/zero profile. A retained array occurrence uses its own using
  Module's row; rebasing a recipe must preserve or rebuild that context.
* Keep A2 `A=max(1,a)`, `S=checkedRoundUp(s,A)`, `B=checked(N*S)` and
  inherited R. Submitted array geometry must match. Check every field
  addition, nested offset, rounding and product in target nuint before
  allocation/access. No int/u32 narrowing, wrap, saturation or 4096-byte cap
  follows. N=0 still validates E, formation, constraints, layout and cycles.
  Transparent-over-void retains s=a=0, R=1; its arrays have A=1, S=B=0
  without supplying native payload backing.

Memoization/visiting keys include the complete normalized instance and its
using/bound-provider context, selected target and governing schema/rules, not
just a TypeDef row. Finite nesting such as `Cell<Cell<int32>>` must not look
cyclic merely because G repeats. Reject genuine inline cycles, including
zero-count edges; growing nonterminating expansions cannot succeed because
no exact key repeats. Pointer edges do not expand pointee storage. Report
representable but unsupported resource capacity separately from malformed
types or arithmetic overflow; do not enumerate N elements.

## Provider identity, replacement and admission

Apply the existing [complete binding relation](../specs/representation/binding-and-replacement.md)
and [A2 positive-evidence rules](standard-trrm-rootless-copy-type-evidence.md#nominal-binding-replacement-and-re-encoding).
Bind the exact selected Module/key and generic declaration before comparing
the complete substituted fields/layout. Preserve every retained formal
domain, default, constraint, access and layout obligation; never tighten the
old caller domain or treat names, ordinals without owners, equal bytes or
another same-layout provider as identity. Expected Copy requires actual
positive Copy evidence, including transitive nominal children. Keep original
expectations separate from actual-provider facts and any stronger guarantees
relied on.

Packing remaps definitions, formal owners, Constraint owners/payload variants,
generic-definition rows, actual TypeSpecRefs, FieldDef/Layout ranges, imports,
Module partitions and A rows in their own namespaces. Clone shared recipes
when using contexts differ. Constructed-owner field operations retain their
complete FieldPath rather than a bare nongeneric-owner shorthand; Feature 13
remains conditional on those retained uses. Revalidate affected layouts and
admission after provider/formal/field/target/schema changes. This grants no
hot-replacement, live-place retargeting or owner-migration protocol.

Under the **distinct A3 schema**, the bounded native-value declaration
scope uses existing `2/1 -> 1/1`; array retention still requires
`20/1 -> 2/1 -> 1/1`. A type-only provider carrying Copy/generic storage
facts needs 2/1, and needs 20/1 only if it also retains arrays. Other retained
call, pointer, query, witness or runtime obligations remain independent.

Select A3 **before** indexing, production, binding or lowering of its required
uses. The unchanged A2 identity
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2` does not select A3,
even with another proposal named alongside it. Generic rows, Feature 2/20,
equal version bytes and plausible payloads do not select the new meaning.
There is no alias, fallback decoder, version sniff, private-interface bridge
or generic admission added to Standard/F/F+S/A1/A2 selections.

A3 inherits TypeSpec `11` `(E,u64 N)`, Module-owned `0012`/40-byte rows,
TypeDef `0x20` with A2's unchanged universal meaning, PlaceSelection
`targetRefKind=3`, and the companion proposal's A3-only TypeDef `0x40`
uniqueness evidence; the layout reserved word remains zero.

Each supplied provider retains its own explicit supported schema and
requirements. A dependency requiring this bounded generic admission must
select A3 or another independently specified, complete supported schema;
inspection of A2 generic rows is not an upgrade. A nongeneric dependency
can remain A2 when its actual records/admission supply every required fact.
Packing selects one destination schema preserving all retained obligations.
Any A2-to-A3 conversion selects both identities explicitly, validates the
complete declaration and universal Copy evidence, and re-encodes/remaps
where needed; changing the selector alone does not create missing guarantees
or admission. No conversion or historical-reader obligation is imposed.

Wrong arity/kind/scope, malformed references/ranges, failed domains/constraints,
missing actual provider evidence, incomplete fields, inconsistent layout or
overflow reject affected publication. Structurally valid but out-of-profile
generic/witness/value inputs report unsupported admission instead of becoming
invalid source syntax or a successful truncated contract. Failure publishes
no partial bound program/image and changes no selected binding. Existing
index, initialization, unsafe, backing and runtime failure behavior is
unchanged. No new Terrum exception class, exceptional edge or runtime policy
is introduced. Only operations explicitly selecting the exact A3 successor
use the host failure classes and bounded processing recipe in the
[A3 layout-failure companion proposal](standard-trrm-a3-layout-failure-classes.md),
including its single host-only resource-limit exception. A2's outward failure
behavior remains unchanged.

## Future conformance gates and ownership

These are required acceptance cases; the proposal does not claim that they
pass. Current support and qualification evidence belong in the implementation
inventory.

| Case | Required outcome |
|---|---|
| Exact substitutions | Independent local/provider `Cell<int32>`, `Cell<uint32>`, nested instances, multiple type-formal orders and defaults; preserve complete identities even with equal layout. |
| Universal Copy publication | Check domains 1/2/3 with both nullability tags. Source producers reject directly stored domain-2 T and domain-3 T with only Blittable, including when only a Copy scalar specialization is requested. Independently admitted phantom/pointer-cell uses can retain broader domains without storing unique responsibility. No instance, Layout flag or absent Cleanup invents the universal bit. |
| Evidence and domains | Independently require the positive universal Copy promise, actual complete field Copy and Blittable layout; Copy handles fail rootless/Blittable storage admission. An independently formed phantom nonrootless actual is not a stored child. Missing evidence, unique/Cleanup storage and unsupported required constraints fail even at N=0. Structurally consistent false producer promises are separate optional-audit cases. |
| Boundary layout | Unit versus nominal Transparent-over-unit, zero/one/max-u64 counts, closed array fields, nested explicit alignment and packing; exact derived geometry, logical initialization and unchanged zero-extent address rejection. |
| Invalid and unsupported input | Wrong formal owner/order/kind/default, malformed 6+4*K rows, row/range limits and missing fields are rejected. Constant formals, interface/associated/other constraints and open array recipes remain unsupported, never erased. |
| Termination and overflow | Finite repeated G succeeds; inline and growing cycles do not. Pointer recursion does not expand payload storage. Nested arithmetic overflow and resource exhaustion are distinct failures; count validation stays compact. See the [A3 layout-failure companion proposal](standard-trrm-a3-layout-failure-classes.md) for the provisional host failure channels and bounded-expansion recipe. |
| Independent exchange | Reorder all declaration/formal/type/layout/import rows; re-emit/reload without source; preserve original/actual identities and independent Module closures. Reject equal-layout no-Copy or tightened-formation replacements. |
| Exact schema isolation | Select the exact uniqueness-amended successor; A2 or flag-free `...-3` plus an extra proposal/flag does not admit this generic route. Equal `(1,0)` bytes or Feature 2/20 do not upgrade a selector. Test independent nongeneric A2 dependencies and reject generic dependencies lacking their own admitted schema/requirements. |
| Execution boundary | After independently completing the relevant value/place/backend route, qualify actual source-free read/copy/address behavior and retained backing/code origins. Binding or emitted rows alone are not runtime evidence. |

On approval, integration owners include exact schema selection, the bounded
generic admission paragraphs in IL metadata/logical records and native-value
requirements/manifests, plus A3/source-proposal cross-references. Keep the
existing A2 candidate distinct; its selector is not amended by this proposal.
The existing normalization, layout and binding algorithms need no replacement.
No normative chapter or inventory is changed here. Implementation must carry
the real
generic declaration, substitution and storage graph through production,
binding, verification, rewriting, persistence and lowering; a host-only
specialization cache cannot carry required facts.

No further choice remains inside this bounded recommendation. Broader
generic properties, behavioral/associated witnesses, constant formals, open
array templates and generic callable execution remain their existing separate
contract/admission prerequisites, not provisionally selected by this document.
