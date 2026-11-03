# Standard TRRM rootless Copy type evidence

**Status: unapproved candidate; provisionally actionable only under the
user's explicit missing-contract workflow.** Every allocation and new
checking rule below is an unapproved recommendation, not an accepted
contract, implemented capability or validation result.
[Proposal index](README.md).

## Decision and scope

Recommend **one positive nominal Copy declaration bit**, together with
**compositional rootless-storage checking of the complete closed element
type**. Do not serialize a second, target-independent rootless bit.
This supplies the missing type evidence for
[the fixed-array candidate](standard-trrm-fixed-array-identity-shape.md)'s
closed, rootless Copy element E, including aggregate and nested-array E.
It does not replace that candidate's independent type, layout, initialization,
place, backing, native-call or execution gates.

The unapproved allocation is `TypeDef.flags` **bit 5, `0x0000_0020 =
DeclaredCopy`**, in the existing 44-byte row. Its positive meaning belongs
to the nominal inline struct declaration, not a using layout row or a
particular value. Rootlessness instead follows the actual complete
representation and recursively selected storage children. Copy and
rootlessness remain separate: a Copy struct containing a shared handle is
not admitted as a rootless array element.

Select the amended composition as **A2**:
**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`**, candidate
selector **`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft`**.
A2 inherits the exact F+S+A candidate
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-1` except for the
Copy-evidence and admission amendments specified here. The fixed-array
proposal remains the owner of A1's structural identity/layout/place design
and is cross-linked to A2; A1 is not an alias or fallback for integrated
nominal-array use. If selected, A2 supersedes A1 for this composition;
neither is normative.
A further changed composition requires its own exact agreement.

No source attribute, unique-owner codec, root descriptor, new layout field,
type-capability table, instruction, Body record, runtime policy or target
identity is selected. General Copy execution, managed elements, ownership
transfer/cleanup, generic export and source-free aggregate construction
retain their independent prerequisites.

## Accepted baseline and the actual gap

The [specification index](../specs/README.md) leads to these accepted
contracts. The proposed physical completion is distinct from them.

| Evidence | Existing rule or implementation boundary |
|---|---|
| [Language: copying](../specs/language.md#values-copying-and-aliases), [types](../specs/language.md#types-and-operations) | Copy is a checked complete-type contract, not synonymous with bitwise copying. Ordinary structs promise Copy; adding an incompatible unique field or Cleanup invalidates that promise. Unique structs and unique struct unions remain noncopyable even with trivial bytes. Eligible handle copies can require intrinsic retention. |
| [Complete types](../specs/language/types-and-normalization.md#complete-type-terms), [generic domains](../specs/language.md#interfaces-access-and-generic-properties) | Constructor, nominal origin, ordered arguments, constant domains and every qualifier position matter. Default type formals are Copy-capable, not necessarily rootless; unique-only and default-plus-unique domains differ. |
| [Metadata](../specs/il-metadata.md#section-kinds-and-row-layouts) | TypeDef assigns visibility and nominal kind only; bits 4..31 are reserved. TypeSpec qualifiers mean write/gc/init, not Copy or uniqueness. Layout's Blittable bit is a representation commitment, not Copy evidence. |
| [Cleanup clause](../specs/il-metadata.md#semantic-contracts-and-stable-binding) | Cleanup is a type-owned clause on an admitted unique type; its absence is not a declaration of Copy. Complete ownership representation remains independently required. |
| [`TrRmTypeDefTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs), [`TrRmContractTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs), [`TrRmTypeSpecTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs) | The Standard TypeDef mask is `0x0F`; the separately selected enum candidate expands the kind mask to `0x1F`, not ownership. Contract reads Cleanup, and TypeSpec validates its schema-selected forms and qualifiers, but neither supplies a positive nominal Copy declaration. Recognizing candidate array form `11` does not supply this missing fact. |
| [`FixedLayoutStorage`](../../sources/Terrum.CodeAnalysis/Semantics/FixedLayoutStorage.cs), [`NativeFixedArrayContract`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs) | Layout/array validation inspects `BoundStruct.IsUnique`; the array helper rejects unique elements even without Cleanup. A Standard consumer cannot truthfully reconstruct that eligibility from nominal kind and field bytes alone. |
| [`ModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [persistence taxonomy](../specs/representation/persistence.md) | Private `.trm` persists `IsUnique`; that is not Standard TRRM evidence or an alternate decoder for this proposal. |
| [Reader trust](../specs/representation/reader-trust.md), [binding/replacement](../specs/representation/binding-and-replacement.md) | Producers publish truthful facts; consumers must check structural safe consumption, supported requirements and actual bindings, without being required to re-prove source or Body truth. Expected stubs are proxies for selected declarations, not new nominal types. |

These are source observations, not newly run tests. Current support remains
owned by [the implementation inventory](../implementation.md). This
proposal completes neither that inventory nor the whole array milestone.

## Alternatives and recommendation

All alternatives and the selection in this section are unapproved.

| Alternative | Assessment |
|---|---|
| **Positive TypeDef Copy bit; derive rootlessness from complete storage — recommended** | The missing declaration promise is nominal and shared by its uses. One existing word carries it, while substituted field/array types determine rootlessness. This supports ordinary generic Copy promises without falsely promising that every instantiation is rootless. No per-use or per-closed-instantiation fact registry is necessary. |
| TypeDef Unique bit, with clear meaning Copy | Could encode the ordinary/unique distinction under an exact new schema. A positive Copy bit makes the required guarantee explicit and prevents an absent guarantee from being used as implicit authorization. Neither design may infer Copy from an older schema's zero reserved bit. |
| Two declaration bits, Copy and Rootless | Cheap physically, but a universal Rootless promise excludes otherwise valid closed instantiations of a generic Copy container unless conditional rules/constraints are added. Treating the second bit as merely an observation of one instantiation is unsound on a shared TypeDef. Closed storage already supplies the required rootless structure. |
| Dedicated complete-type capability record | A coherent alternative would key positive Copy/rootless facts by using Module, complete TypeSpec and target profile, with original expectations and actual-provider evidence. It requires a new required section, uniqueness/reference/binding rules, and a policy for generic instantiations not enumerated by the provider. Conditional recipes would be a wider capability system. That is not needed to carry this nominal promise and check this bounded closed storage graph. |
| Reuse `FixedArrayLayout.reserved`, Layout's Blittable bit, Cleanup absence, names or field bytes | Rejected. A using Module's geometry is not the declaration's Copy authority. Equal representation, zero count/extent and no Cleanup do not make a unique declaration Copy. |

Bit 4 is also a possible draft allocation, but
[the enum candidate](nominal-enum-persistence.md#allocation-and-representation)
uses bits 2..4 for nominal kinds. Recommend bit 5 to keep a three-bit
nominal-kind band and capability flags orthogonal, not because unpublished
numbers are immutable. Repacking either draft would be legal. There is no
width saving from choosing bit 4, and A2 does **not** thereby admit enums or
select an enum/array composition.

## Exact unapproved field and capability amendments

All multibyte words retain little-endian encoding.

| Item | A2 candidate rule |
|---|---|
| TypeDef row | Unchanged 44-byte stride; the `u32 flags` field remains at byte offset 8. |
| `flags & 0x03` | Unchanged visibility, including invalid value 3. |
| `(flags >> 2) & 0x03` | Unchanged A1 kind domain: 1 struct, 2 class, 3 interface. |
| `flags & 0x20` | Positive `DeclaredCopy` promise; permitted only on kind-1 TypeDefs, including expected imported struct stubs. |
| Reserved bits | Bit 4 and bits 6..31 remain zero in A2; permitted mask is exactly `0x0000_002F`. Class/interface rows must additionally have bit 5 clear. |
| Positive example | Public Copy struct flags are `0x0000_0026`; public struct flags `0x0000_0006` do not provide the required Copy guarantee, even with identical fields/layout. |
| Feature scope | Under A2, the flag is the bounded nominal Copy declaration carrier for existing `2/1=RootlessNativeValues`'s Blittable Copy admission. No new Feature ID or revision is allocated. |
| Array closure | Candidate `20/1=FixedInlineArrayStorage` retains immediate prerequisite `2/1`, deriving `1/1`. It now requires this explicit evidence wherever its element closure reaches a nominal struct. |
| Preserved A records | TypeSpec `11` remains 14 bytes; section `0012` remains 40 bytes; its offset-12 `reserved` word remains **zero**, not a Copy flag or row reference. PlaceSelection remains 24 bytes. No other reference discriminator, instruction, map, clause, prefix or escape changes. |

`DeclaredCopy=1` asserts that the declaration's ordinary inline value form
has the accepted Copy contract for **every well-formed instantiation admitted
by its published formation contract**. The producer must set it when
publishing that retained ordinary-struct promise, and must not set it for
an authored unique declaration. It cannot replace missing qualification,
representation, generic or ownership records.

The bit is a retained contract dimension, not a new type constructor,
generic argument or nominal identity. Array identity remains exact E/N;
changing a selected declaration's Copy promise must pass replacement
checking, not produce an equal-layout substitute for E.

Clear means **no positive Copy guarantee in this carrier**. It is not
proof that a type is unique, has Cleanup, is byte-trivial or can be safely
materialized with `IsUnique=false`. Other non-Copy uses still need their
actual admitted ownership representation. The bounded array route declines
such a nominal element rather than assigning a default ownership model.
Copy remains a contract of the complete form: this bit does not make
`own<T>`, an initialization capability or another non-Copy wrapper Copy.

A set bit and a type-owned Cleanup clause on that struct are structurally
incompatible. Every nominal inline child required by the element check
must separately provide its Copy promise; a parent's bit cannot hide a
missing child fact. The producer also owes all semantic obligations of
Copy, including the prohibition on duplicating unique responsibility and
on user-authored copy code.

Every Module owning a retained set bit, retaining an expected Copy stub,
or relying on this fact must have effective **2/1**, even a declaration-only
provider with no array or Body. Every Module retaining an A type/layout/
intrinsic selection still needs **20/1**, including nested, unused and
declaration-only uses. Module ownership follows the TypeDef partition;
fact use also retains the using Module's obligations. Packing/shared rows
never union those manifests into one artifact-wide requirement.

This is an unapproved physical completion of the already bounded native
Copy scope, not a new general Copy/managed-runtime capability. Features
3/4/16, queries/FieldMap/FieldPath/FieldDef binding under 13, generic/
witness/constant support and actual runtime dependencies remain conditional
on their own retained uses. A2
does not complete pending registrations merely by preserving their numbers.

## Unapproved complete-element checking rule

Check `RootlessCopy(T, using Module, selected target profile)` only after
complete identity resolution, normalization and independently supported
type formation. This denotes a storage/type admission check, not a fact
about an initialized value. For A2 the selected layout remains Windows x64
TargetProfile **(1,1)** and target requirement 1 from A1.

Here, rootless means that holding/copying this inline representation
requires no contained managed/normal-backed handle retention, reference
descriptor support, root, pin or owning retirement. It does **not** mean
that a raw pointer has no pointee/backing obligation or that a code address
has no retained provider/code-lifetime dependency.

| Complete T | Required evidence and result |
|---|---|
| Assigned atomic scalar, including unit | Its admitted built-in contract supplies Copy/rootless identity; no fabricated TypeDef/flag. Actual target representation and required operations still need support. |
| Raw data pointer or admitted plain function-pointer cell | Its complete pointer contract supplies Copy/rootless **cell** representation. Preserve qualifications/depth, signature/anchor, backing and code origins. Do not recurse into pointee storage to classify the cell; validate the pointee/signature's complete closed type and other required contracts normally. |
| Nominal inline struct or supported complete constructed instance thereof | Require `DeclaredCopy=1` on the exact selected declaration and a retained positive expectation for every imported Copy reliance; reject Cleanup. Use complete actual instance fields and the applicable independently admitted layout, including private fields. Recursively require RootlessCopy for every stored inline child after exact substitution. Statics are not instance children. |
| Structural fixed array `(E,N)` | Recursively require RootlessCopy(E) and its complete A layout/identity obligations. The array's Copy follows this rule, not a nominal bit. N=0 still checks E and all inline cycles; a zero-count unique-element array is not an escape. |
| Another already admitted inline constructor | Apply only its independently defined complete Copy and storage-child rules. For example, an optional needs its actual supported validity/tag representation; a representation projection must normalize under its declared rules, not be stripped by name. This proposal assigns no missing optional, union, SIMD or projection codec. |
| Class/box handles, checked references, Span/views, owners, GC/support-bearing forms, open formals/existentials or an unsupported constructor | Not eligible in this bounded route. A nominal Copy flag on an enclosing type cannot erase these children or authorize their missing support. |

For an independently admitted union representation, every possible stored
case/payload must pass, not merely the currently observed active case.
An overlapping native `LayoutKind.Union` does not supply tagged-union
validity or a missing union codec. Incomplete/reference-only facades that
omit fields cannot establish this fixed rootless storage closure.

Check a finite complete type/storage graph, not N expanded array elements.
Distinguish completed memo entries from in-progress inline edges; reject
inline cycles even through zero-count arrays or zero-size fields. Pointer
edges do not expand pointee storage. Generic expansion keys include complete
substitution, not just the generic TypeDef; infinitely expanding or
unsupported graphs decline, and resource exhaustion is not successful
admission.

After this type check, independently perform A1's complete target layout,
stride/extent/alignment and Blittable checks. Unit and a distinct Copy
Transparent-over-unit nominal element retain their own identities and
0/0 element layouts; the latter needs its own positive flag. The positive
array-container floor neither supplies Copy nor native payload backing.
Initialization, padding, access, stable backing, zero-extent address
rejection and whole-counted-array state remain unchanged. Rootless Copy
does not authorize an arbitrary byte blit, implicit zeroing, new foreign
mapping, unsafe call, root acquisition or lifetime extension.

### Generic and constructed subjects

The unapproved Copy bit is a universal declaration promise, not a
specialization observation. Default `ownershipDomain=1` guarantees the
Copy-capable domain but does not guarantee rootless storage. Domains 2
(unique-only) and 3 (default plus unique) do not by themselves establish
Copy for a stored formal. Producers must establish their ordinary-struct
promise under the actual domains and declared constraints; one favorable
closed argument or machine-code specialization cannot invent that promise.
A phantom formal need not be a storage child, but remains part of complete
identity and formation checking.

For a closed constructed E, preserve the selected generic declaration,
owner/ordinal correspondence, every ordered complete type/constant argument,
domain/value, qualifier and required constraint/witness. Substitute before
rootless checking; the same Copy declaration can have an eligible scalar
instantiation and an ineligible handle-bearing instantiation. Do not reuse
the first instantiation's result for the second.

A2 assigns **no new rootless constraint, Constraint payload, witness or
generic execution/export capability**. Existing inspectable GenericParam/
Constraint rows alone do not admit constructed E. If the required generic,
constant-argument, witness or complete storage representation is not
independently specified and supported under the selected schema, decline
affected use. Closedness or the Copy bit cannot repair that gap. Do not
replace a generic instance by a fabricated nongeneric TypeDef that loses
its selected origin/arguments.

The separate unapproved
[A3 constructed-element proposal](standard-trrm-closed-constructed-array-elements.md)
recommends a bounded type-only generic storage profile under the exact
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1` identity. It leaves A2
unchanged and does not admit generic execution, witnesses or open storage
recipes.

## Producers, independent consumers and trust

**Unapproved producer obligations:** source producers establish the actual
ordinary/unique declaration distinction and the universal Copy promise;
they publish complete truthful instance-type/layout and formation facts.
Rewriters assume responsibility for changed facts. A source unique struct
with only an integer field must never acquire the flag because its bytes
look Copy-compatible. A Copy struct with a handle may truthfully carry
the flag but cannot pass the rootless array check.

**Unapproved mandatory consumer checks:** before affected use, validate the
selected A2 schema, unchanged row extents, closed flag mask/applicability,
TypeDef owner/ranges, TypeSpec/reference namespaces and bounds, actual
Copy/Cleanup incompatibility, complete closed element relationships,
structural storage closure, supported target/layout and Module closure.
Resolve and compare original expectations with the actual selected
provider as below. Unsupported/missing evidence fails affected admission;
there is no partial program/image publication or mutation of a selected
binding on failure.

These checks use declared type structure and published guarantees; they
do not reconstruct source or prove that the producer's Copy assertion is
semantically true. A malicious producer can mark an otherwise structurally
consistent unique declaration Copy while omitting evidence of its source
uniqueness. That violates producer truth; ordinary consumers are not required
to discover it by Body analysis. An optional source/semantic audit must
identify its actual scope. The flag is not an authentication, trust,
verified, initialized or unsafe-authorization bit.

## Nominal binding, replacement and re-encoding

The following comparisons and preservation rules are unapproved additions
for this fact; the accepted binding relation otherwise remains unchanged.

1. Resolve the using Module's exact ModuleRef identity/export key and
   expected declaration kind first. A local imported TypeDef is its proxy,
   not a second nominal E. Preserve the original expected Copy bit, complete
   types, layout and other ownership commitments separately from the actual
   provider declaration/revision. Another same-name or equal-layout type
   cannot satisfy this expectation.
2. Expected `DeclaredCopy=1` requires actual `DeclaredCopy=1` from a
   separately admitted provider schema carrying this fact. A consumer's
   expected bit is a requirement, not evidence that the actual provider
   publishes it. A clear expected bit retains no Copy guarantee; an extra
   provider promise can strengthen only that dimension, never override
   independently retained unique/consumption/formation obligations.
   Array use always retains the required positive promise.
3. Check every retained expectation and every transitively required
   nominal child under the exact substituted storage graph. Replacing a
   Copy declaration with a no-Copy/unique declaration fails even if the
   key, fields, size and Cleanup absence are unchanged. Changing field
   types to support-bearing storage fails retained rootless/layout
   obligations. An optimization relying on a stronger actual provider
   promise must retain that stronger dependency. If the original stub
   has no Copy promise, retain an additional Copy-positive expected
   TypeDef/Import under the existing multiple-expectation rules, resolving
   to the same actual nominal declaration. Do not overwrite the original
   stub or keep the relied-on bit only in an unserialized binding cache.
4. Packing a satisfied Import to a direct TypeDef reference preserves the
   original expectation, selected actual identity and relied-on fact.
   Remap Module partitions, TypeDefs, Imports/ModuleRefs, TypeSpec children/
   constructed arguments, field types, Layout/LayoutField rows and A
   layout/type references in their schema-owned namespaces. The flag is a
   bit value, not a reference; N/stride/extent/alignment remain quantities.
5. A shared TypeSpec is shareable only if its resolved meaning agrees in
   every using context. Clone/remap a recipe when changing one binding
   would change another use; do not merge nominal definitions because
   their flags or geometry match. Rebuild dependent Body maps/records,
   extents, indexes and applicable optional source locations under A1's
   rewrite rules rather than substituting matching bytes.
6. Relevant provider, complete type, flag, field/layout, schema/catalog,
   target or rule-context changes invalidate cached eligibility, binding,
   layout and derived native data. Revalidate/re-lower affected retained
   uses; successful replacement comparison does not retarget a live place,
   migrate owners or establish safe unload/hot replacement.

## Exact schema isolation

This unapproved choice keeps `(major,minor)=(1,0)`, codec `01`, the ordinary
header and one exact schema per artifact. Select A2 **before** indexing,
writing, binding or lowering. No wire schema discriminator is added.

Unchanged Standard, F, F+S, A1, enum, CopyOrigin, general-predicate and
RootPin selections gain no flag meaning or admission rule. In particular,
Feature 2 or 20, a `0x20` bit, equal version bytes or plausible fields cannot
select A2. Old-schema nominal metadata lacking this evidence cannot satisfy
an A2 nominal Copy requirement through a guessed default or private
`IsUnique` fallback. No compatibility alias or automatic retry is defined.

Separately supplied providers may still use different explicitly supported
schemas: equal schemas are not a general binding requirement. A provider
must actually carry each required logical fact; a scalar-only dependency
need not acquire a nominal Copy fact it does not use. This proposal defines
no mapping that manufactures this fact from an older nominal encoding.
Packing requires a single destination schema preserving all retained
facts and requirements, never one decoder per Module. An A1-to-A2
conversion needs genuine declaration evidence and complete re-encoding;
changing a selector or setting every struct's flag is not conversion.

## Approval-time owners and implementation prerequisites

No accepted owner is edited by this proposal. The unapproved integration
plan is:

| Owner | Amendment only after approval |
|---|---|
| [IL metadata](../specs/il-metadata.md#section-kinds-and-row-layouts), [registry](../specs/representation/registry.md) and [machine-readable allocations](../specs/representation/trrm-1.registry.json) | Add exactly the bit, applicability, absence meaning, Copy/Cleanup exclusion and bounded complete-element rules; replace the blanket reserved-bit/visibility-and-kind-only statements for the selected adopted schema. Keep A geometry reserved words zero. |
| [Semantic manifests](../specs/representation/semantic-manifests.md), [requirements/admission](../specs/representation/requirements-and-admission.md) | Register the schema-specific declaration carrier within 2/1 and the complete 20/1 dependency rules together, including declaration-only providers/imports and unused retained content. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | State the positive guarantee comparison, actual-provider evidence, storage-closure structural checks and preserved original expectations without requiring semantic source/Body re-proof. |
| [Complete types](../specs/language/types-and-normalization.md), [language ownership](../specs/language.md#types-and-operations), [Windows layout](../specs/targets/windows-x64-data-layout.md) | Preserve current source Copy/unique, normalization, complete generic domains and target layout behavior. Add only necessary cross-references; do not create a source Copy attribute or change representations/ABI. |
| [Versioning](../specs/representation/format-versioning.md), the unapproved [array proposal](standard-trrm-fixed-array-identity-shape.md) and [schema-composition proposal](trrm-draft-schema-profile-selection.md) | Apply existing exact-draft agreement; reconcile A1/A2 identities and candidate adoption text explicitly. No format-version policy change, historical-reader duty or automatic composite is proposed. |

Implementation prerequisites, not work performed here:

* Carry the explicit fact and its original/actual Module origins through
  source production, selected Standard readers/writers, public schema
  selection, dependency references, binding, bound reconstruction/shared
  traversal, mandatory verification, rewriting and native handoff. Do not
  materialize a required unknown fact as `!IsUnique`.
* Complete the independently admitted aggregate storage/type graph and A
  identity/layout/element path before claiming aggregate or nested-array
  execution. Existing private IsUnique persistence is not this gate.
* Keep unsupported generic/constraint/witness/constant and other constructor
  carriers gated. This proposal supplies no missing codecs for them.
* Qualify the final selected compiler/configuration and source-free inputs
  under the ordinary developer workflow. A type reader test or emitted
  array row is not executable array support.

## Required future tests and remaining decisions

All cases below are unapproved acceptance requirements, **not tests run**.
Use independent producers/consumers and unrelated synthetic types.

| Gate | Required outcome |
|---|---|
| Positive/type closure | Scalars, unit, raw-pointer and admitted function-pointer cells, including pointers whose closed pointees are unique or managed; explicit Copy Sequential/Transparent aggregates, aggregate-in-aggregate, array-of-aggregate, aggregate-with-array and array-of-array. Every nominal stored child supplies its own bit; arrays derive Copy recursively. Preserve complete nominal/depth/qualification identity without expanding pointee storage. |
| Negative/declaration | Source producer rejects assigning the bit to a unique struct, including trivial unique/no-Cleanup forms; consumer rejects missing positive evidence, set-bit class/interface, invalid reserved bits, Copy plus Cleanup and a nominal child lacking Copy. Copy with class/box/ref/owner support fails rootless checking despite plausible layout. A semantically false but structurally consistent producer assertion is an optional-audit case, not a mandatory consumer source-proof test. |
| Boundaries | All visibility values and kind combinations; `0x06` versus `0x26`; truncated/oversized 44-byte rows; row 1/FF_FFFF and invalid reference/high-byte boundaries. N=0/1/max-u64 still checks E, nested zero extent, unit versus distinct Copy Transparent-over-unit, zero-count unique E and inline cycles. Pointer recursion does not expand inline storage. Never expand N elements. |
| Generic/constructed | Independently admitted closed scalar versus handle instantiations of the same Copy declaration; domains 1/2/3, ordered substitutions, phantom formals, constant domains, nested qualifiers and witness constraints. No inference from one specialization. Missing generic codec/support, open formal/existential or infinite expansion declines rather than erases identity. |
| Binding/replacement | Import expected Copy selects actual Copy from the exact provider/key; no-Copy actual, same-layout unique replacement, wrong owner/provider and support-bearing substituted child fail. Preserve an original no-Copy-guarantee stub alongside any additional positive expectation, all independent expectations, stronger relied-on guarantees and transitive target/layout dependencies after direct binding. |
| Transformation | Permute Module/TypeDef/Import/TypeSpec/Layout/A rows, clone shared recipes across using contexts, reconstruct/re-emit/reload without source. Preserve the bit, original expectations and per-Module closure; rebuild maps/extents/source slots and recompute invalidated derived eligibility. Byte equality is not the criterion. |
| Cross-feature/schema | Set bit under Standard/F/F+S/A1/enum/CopyOrigin/RootPin does not select A2; Feature 2/20 alone cannot enable it. A2 no-array providers still need 2/1 when carrying the fact; array consumers need 20/1 transitively. Missing unused/imported/nested requirements fail. Distinct supported dependency schemas bind only when their actual records provide required facts; one packed artifact never mixes decoders. |
| Native boundary | With independently completed array execution, source-free aggregate/nested-array read/copy/address use retains pointer backing/code origins, initialization, access/alignment and actual storage mutation. Zero extent, unsupported children and unsafe obligation failures publish no address/call. No hidden retain/root/pin, byte-copy authorization or relaxed EC suffix rule follows from the flag. |

The provisional recommendation resolves the in-scope choice: **positive
TypeDef bit 5, compositional rootlessness, A2 exact identity, unchanged A
record allocations and Feature closure 20/1 -> 2/1 -> 1/1**.
Approval and the implementation/acceptance gates remain outstanding.

No further in-scope encoding policy is left to implementation. A complete
generic/witness/constant or additional representation schema remains a
consequential **external prerequisite** when such E is requested; this
proposal deliberately does not choose those missing codecs or a rootless
generic constraint. Managed/non-Copy arrays, a general conditional type-
capability system, enum/G/CopyOrigin/RootPin composites and other targets
remain separate unapproved decisions, not fallback interpretations.
