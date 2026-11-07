# Private revision-105 nominal-origin inputs

**Status: unapproved; provisional implementation direction only under the
authorized missing-contract workflow.** This fills an authoring/carrier gap,
not normative approval, completed compiler support or runtime qualification.
The governing direction is to prepare the smallest concrete missing-contract
proposal using `gpt-6.1-sol` at maximum reasoning, then continue provisionally.

## Decision and owners

Recommend one immutable **`PrivateModuleIdentityInputs`** input for an exact
bound-program snapshot: an explicitly supplied logical using Module identity,
owned nominal declaration/key/export assignments, and imported nominal
occurrence/key/provider selections, plus a **separate enum-member assignment
collection** for both ownership cases. Each assignment explicitly relates an
actual bound enum/member identity and declaration ordinal to its
producer-assigned member key. Preserve complete original enum/member
expectations and resolved origins with the resulting program/reference/module.
Do not introduce a key-generation convention, independent enum-member exports
or a second binding relation.

This completes the input requirement in
[C's Binding record](native-output-foreign-interface-private-persistence.md#binding-definition-export-or-expected-import)
and [C's enum directory](nominal-enum-persistence.md#exact-directory-and-typevalue-references)
for the exact
[revision-105 B+C+R composition](checked-reference-private-persistence.md#exact-schema-composition).
It completes this proposal's earlier type-only input recommendation; it
changes no wire field: retain the reconciled directory order, C's
`privateType + Binding` origin rows and exact enum/member entries, R's
**8-byte** complete-reference rows, and **outer 3 / inner 105 / B+C+R**
selection. In particular, `completeReferenceTypes`, `nominalTypeOrigins`,
`EnumDefinitions`, then `foreignInterfaces` remain in that order; do not
adopt C's historical pre-array placement or revision number.

Preserve [complete types](../specs/language/types-and-normalization.md),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[Module ownership](../specs/modules.md),
[exact-schema selection](../specs/representation/format-versioning.md) and
[consumer trust](../specs/representation/reader-trust.md). No accepted text is
replaced. The proposal-level amendment supplies C's already-required enum
member keys and their checked association, as well as the reference-projection
timing below. It does not amend the enum owner's logical identity, complete
snapshot or replacement rules. Standard TRRM source publication, Core
registration, source enum collection, open generics, foreign-interface/member
authoring APIs and class runtime admission remain separate. This recommendation
approves and stabilizes none of those contracts or host APIs.

### Verified implementation gap

These are source observations, not newly executed tests:

| Inspected owner | Current boundary |
|---|---|
| [`PrivateModuleIdentityInputs.cs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs) | The provisional carrier has two type-level records and a four-argument factory, but no member-assignment input. Accepting an in-range enum selector supplies neither an explicit member mapping nor an enum codec. |
| [`BoundEnumTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs) | Internal `BoundEnumDefinition` carries `Identity`, `Name`, `UnderlyingType`, ordered `Members`, public/primary/Flags facts, `BlittableTargetRequirement` and optional `ModuleImport`. Each internal member carries its own `Identity`, `DeclaringEnum`, `Name`, int32 `Value`, public fact and separate `DeclarationKey`. `FromCore` copies that explicit key; it does not derive it from `Identity.ExportKey`. |
| [`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | Both carry `EnumDefinitions` and `PrivateNominalTypeOrigins`. The reference constructor validates enum tables, including owner/member identities. These collections do not by themselves define the missing host assignments or preserve every original using-context expectation through projection and cloning. |
| [`ModuleCompiler.PrivateNominalOrigins.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateNominalOrigins.cs), [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | Nominal-origin construction exists, but enum remapping explicitly requires an enum-definition directory and refuses. The partial 105 writer still writes `0 // enumDefinitionCount`; this is not complete B+C+R support. |

The current Core-shaped bound identity and its validation also are not the
private logical `(enum, member key)` codec. The adapter distinction below is
necessary even where an enum table already validates. Existing
[checked-reference fixtures](../../tests/Terrum.CodeAnalysis.Tests/Semantics/CheckedReferenceTypeTests.cs)
do not establish this amendment's acceptance cases merely by exercising a
partial 105 path. No new test or implementation result is claimed here.

## Concrete input amendment

The following are proposed C# host records, not Terrum syntax or wire tags:

```csharp
public sealed record PrivateNominalDefinition(
    PrimitiveType Type, string LocalDeclarationKey, bool IsExported);

public sealed record PrivateNominalImport(
    PrimitiveType Type, string LocalDeclarationKey,
    string SelectedModuleIdentity, string SelectedExportKey);

public sealed record PrivateEnumMemberAssignment(
    PrimitiveType EnumType,
    CoreDeclarationIdentity EnumIdentity,
    int MemberOrdinal,
    CoreDeclarationIdentity MemberIdentity,
    string MemberDeclarationKey);
```

Extend the sealed immutable `PrivateModuleIdentityInputs`. Retain its
read-only `UsingModuleIdentity`, `Definitions` and `Imports` properties; add
the following property and five-argument factory (C# API signatures;
implementation omitted):

```csharp
public ImmutableArray<PrivateEnumMemberAssignment> EnumMembers { get; }

public static PrivateModuleIdentityInputs Create(
    BoundProgram program,
    string usingModuleIdentity,
    IReadOnlyList<PrivateNominalDefinition> definitions,
    IReadOnlyList<PrivateNominalImport> imports,
    IReadOnlyList<PrivateEnumMemberAssignment> enumMembers);
```

The four-argument factory forwards an empty `enumMembers` collection. It
does not opt into missing-key inference: only enums with no members or an
already complete compiler-owned retained mapping can need no new assignments.
The new records expose public selector/identity types, not the internal
`BoundEnumDefinition` or `BoundEnumMemberDefinition` types.

`Type` selects an actual nominal declaration in **that exact program
snapshot**, not a cross-Module identity or necessarily its final wire index.
Use the program's canonical inline-struct, enum or readonly nonoptional class
type identity. Reject scalars, structural constructors, qualified class
selectors and absent/out-of-range declarations. Physical selection by a local
type number does not generate any key. Require nonnull program, lists,
entries and strings; reject uninitialized collection inputs. Capture the
program association, all three submitted lists and complete original
declaration/type/member graphs immutably, without retaining caller-mutable
list backing. Input enumeration order is not enum declaration order.

The factory checks shape, ownership associations and conflicts with retained
origins. It does not claim that a provider has been resolved merely because
its identity/key strings are valid. Resolution uses checked selected provider
snapshots at the applicable binding boundary.

Add these overloads:

```text
ModuleCompiler.CreateProducer(program, exports, inputs)
ModuleCompiler.CreateConsumer(program, referenceProducer, inputs)
```

Thread the same required input through internal `CreateConsumerSet`,
`CreateConsumerAgainstReference` and `CreateGraphLibrary`; add a required
final `inputs` argument to new overloads of native `CreateProducer`,
`CreateConsumer`, `CreateConsumerSet`, `CreateGraphProducer` and
`CreateGraphConsumer`. Existing runtime-policy/export/dependency inputs keep
their meanings. Compiler transformations rebase this association explicitly;
passing inputs captured for a different or host-modified program rejects.

Existing overloads may continue an explicitly supported origin-free route,
or forward an already complete retained identity snapshot without inventing
new assignments. They must refuse when a newly required identity/proxy key
is missing. An empty input is not permission to erase a retained origin.
This amendment supplies no default Module identity and does not make input
presence, a native profile string or a version comparison a schema selector.

### Member collection placement and totality

**Select the separate `EnumMembers` collection**, not member fields on
`PrivateNominalDefinition`. Both owned definitions and imported expectations
need the same complete enum/member mapping. Putting it only on definitions
would omit imports or encourage relabelling them owned; duplicating an
enum-only payload on both type records would split the validation contract.
The separate collection leaves C's type Binding inputs unchanged and joins
members to their actual owner explicitly. It is not a second enum directory,
member import/export table or retention-root collection.

For each supplied enum definition/import, require a complete assignment for
every member of its selected bound declaration, unless a complete
compiler-owned mapping for that exact original snapshot is already retained.
An explicit group for an enum must itself be complete, including when it
asserts agreement with retained data: no sparse overlay or per-member
fallback. A zero-member enum has no member assignments but still needs its
type Binding and enum entry. An absent mapping for a nonempty required enum
is missing input, never a request to choose keys. Merely finding strings in
a bound enum table is not a checked retained identity/origin association.

Every supplied member group must match either a supplied enum type assignment
or an already retained origin in the input's `UsingModuleIdentity` context.
Reject unmatched groups, non-enum owners, absent declarations, missing or
extra members and repeated assignments, including identical repetitions.
Validate complete groups even for unexported definitions later pruned from
the output. Pruning may remove an unneeded enum and its map together, never
members from a retained enum. Closure discovery also rejects a required enum
without a complete type origin and member mapping; a group cannot manufacture
the missing origin.

### Exact owner, ordinal and identity association

The Core-shaped fields below are the **Core-backed source adapter form**,
not identities a private-byte reader can reconstruct. The separate
[private enum bound-identity candidate](private-105-enum-bound-identity.md#concrete-internal-carrier-recommendation)
specifies the missing disjoint host carrier and checked bridge. Under that
candidate, token equality below unwraps actual Core identities or uses a
checked bridge for the exact original source snapshot; a decoded private-only
enum instead uses its complete retained logical member map without fresh
Core assignments. A Core-only assertion cannot manufacture that bridge.

For each `PrivateEnumMemberAssignment`:

1. Resolve `EnumType` in the captured program's enum table to the actual
   `BoundEnumDefinition D`. Require `EnumIdentity == D.Identity`, a valid
   `CoreDeclarationKind.EnumType` identity and the ownership/provider
   association required by the corresponding type Binding. A matching name
   or local integer in another program is not an owner match.
2. Require `0 <= MemberOrdinal < D.Members.Length`; select exactly
   `M = D.Members[MemberOrdinal]`. Require `MemberIdentity == M.Identity`,
   its valid `CoreDeclarationKind.EnumMember` kind,
   `M.DeclaringEnum == D.Identity`, and
   `M.Identity.ProviderIdentity == D.Identity.ProviderIdentity`.
   Do not search another ordinal, owner, name or value to make an identity
   match.
3. Relate that actual member identity to the explicitly supplied
   `MemberDeclarationKey`. Under the current checked bound-table invariant,
   require ordinal equality with `M.DeclarationKey`; a conflicting,
   empty or invalid bound key is not repaired by this factory. The authoring
   adapter must supply its real producer-assigned key before creating the
   checked bound declaration. The factory neither mutates that declaration
   nor assigns a key from `M.Identity.ExportKey`.
4. Require exactly one assignment per owner/ordinal and owner/member identity,
   and unique member keys within that enum. Duplicate values are valid;
   duplicate names, keys, ordinals or member identities are not. A member
   identity belonging to another enum cannot be reassigned to this one.

Member keys use C's nonempty strict-UTF-8 **at-most-256-byte** key capacity
and the enum owner's additional prohibition on zero scalars. Compare them
ordinally without normalization or case folding. Keys are unique within
the resolved T, **not across a Module**: two different enums may use the same
member key. An enum member key may also equal a type/ordinary export key
without a namespace collision. It has no `IsExported`, local proxy key or
independently selected Module/export. C's Binding-bearing foreign methods
remain in their existing, separate declaration/export namespace.

`EnumIdentity` and `MemberIdentity` assert the actual in-process declarations;
they are not extra wire identities or Core authority selections. In
particular, `MemberIdentity.ExportKey` is an adapter token, not an instruction
to export this member or a default for `MemberDeclarationKey`. In-process
validation alone proves neither positive local ownership nor a selected
provider/export. For a current Core-backed enum, the checked type
`D.Identity.ProviderIdentity`/`D.Identity.ExportKey` must agree with its owned
Module/declaration pair or selected provider/type-export pair. An import's
using-Module proxy key need not equal that provider key. A retained
`ModuleImport` must agree too; neither token strings nor a missing import
marker authorize restamping an external owner as local. Any stricter current
adapter/table representability limit remains an explicit implementation
gate, not permission to rename a key.

### Enum entry construction and eligibility

Retain the enum owner's bounded **complete, module-level, public, nongeneric,
primary, int32-backed** domain. Members inherit public access. Check those
facts from the actual checked declaration/provenance, not input defaults or
the absence of unsupported fields. An unexported owned enum is still public
under this carrier's bound; public does not imply exported. An imported enum
can never be made owned by supplying member assignments.

The complete `D` and its checked map supply every existing enum field:

| Existing C field | Input or retained checked fact |
|---|---|
| `nominalTypeOriginRef` | The one-based, nonzero reference to this enum occurrence's exact owned or imported type Binding, with agreeing using context and final `privateType`. |
| `lookupName` | `D.Name`, preserving its effective namespace/qualified source lookup name, not deriving identity from it. |
| `underlyingPrivateType` | Exactly `2`, after checking `D.UnderlyingType == PrimitiveType.Int32`. |
| `flags`, reserved fields | Bit `01` exactly when `D.IsFlags`; every other flag/reserved bit zero. Flags grants no new operation or annotation schema. |
| `blittableTargetRequirement` | The actual `D.BlittableTargetRequirement`: `0` or exact Windows x64 requirement `1`. No inference from base, size or Copy. |
| `memberCount` | The entire `D.Members.Length`, including zero. Public/complete primary status is required, not serialized as optional flags. |
| Per-member key, name, value | The checked `MemberDeclarationKey`, `M.Name`, `M.Value` at each declaration ordinal. The ordinal is the list index, not another field; keys never come from the name/value/ordinal. |

Each retained enum origin has exactly one enum entry and conversely. Canonical
enum entry order is the canonical origin order restricted to enum origins,
not host assignment order or member key order. Remap its source selector to
the final fixed private code **`356 + i`** for
`0 <= i < enumDefinitionCount <= 668`; code **1023** is the last enum,
class codes begin at **1024**, and
inline structures remain limited to **256** entries. These are the selected
105 domains, not formulas derived from future verifier constants.

Keep the exact enum entry's **20 fixed bytes excluding the lookup String
and members**, existing i32-length strict-UTF-8 Strings and each member's
two Strings plus i32 value. No host ordinal/adapter identity, extra presence
bit, per-member Binding or extra count is serialized. Preserve the mandatory
zero counts when empty and the existing four-byte enum constant payload.
Bound all enum/member counts and checked extents by the shared **4 MiB**,
**100,000-node**, string and depth budgets, including all retained contexts;
members do not acquire a fresh budget per enum or per input collection.

### Assignment and validation rules

* **Owned:** map the selected declaration to Binding kind 1 with the supplied
  using identity/key and exact `IsExported`. That Boolean explicitly publishes
  the **same key**; there is no separate export alias or public-implies-export
  rule. Positive local ownership must come from checked source provenance or
  the explicit authored-bound-program producer input. An imported declaration
  cannot be relabelled owned; absence of today's import marker is not proof.
* **Imported:** map to kind 2, `isExported=0`, with the explicitly supplied
  local proxy key and selected Module/export key. Capture the original expected
  declaration and its full context before Function erasure, provider
  substitution or table merging. If source binding already retained a selected
  origin/expectation, these inputs must agree; they cannot replace it with a
  newly supplied provider's contract.
* Use C's exact domains: identities are ordinal case-sensitive ASCII,
  **1–64 characters**, initial letter then letters/digits/underscore; keys are
  explicit nonempty strict-UTF-8 text of **at most 256 bytes**, ordinal without
  case folding or normalization. Apply no PE-symbol or path grammar to keys.
  Never derive identities/keys from names, slots, hashes, paths or layouts.
  One identity denotes one logical Module throughout the retained selection;
  a new using context cannot impersonate a distinct selected provider.
* Reject repeated type assignments, including identical repetitions and an
  owned/imported assignment to the same occurrence. Local keys are unique
  across C's **Binding-bearing** nominal/interface/foreign-method declaration
  kinds in each using Module. Export keys also cannot collide with that
  Module's ordinary semantic exports. Enum member keys use their per-T
  namespace above, not that shared namespace. Different using Modules may
  retain different expectations selecting the same actual declaration.
* Validate every supplied assignment. Explicit exported definitions are
  retention roots even when no function mentions them; require the applicable
  public declaration and supported complete contract. Internal/unexported
  assignments do not create exports and may be pruned only when unneeded by
  any retained root. Existing artifact/code/image cardinality gates remain;
  this does not add a standalone type-only package shape or dummy code export.
* Invalid host argument shape uses existing argument exceptions; malformed
  wire data uses `ModuleFormatException`; unavailable required bindings,
  unsupported closure or contract mismatch use `ModuleContractException`.
  Publish no partially admitted artifact/reference as successful binding.

## Retained closure and identity contexts

Compute the origin closure specified by
[R's expansion of C](checked-reference-private-persistence.md#every-type-occurrence-and-textual-expectations):
retained reference types, **every nominal typed-place owner even for a scalar
leaf**, and recursively required field/type contracts, plus explicit nominal
export roots and C's foreign-signature roots. Traverse pointer/signature,
array, optional and reference constructors; do not stop at an outer structural
type or a bodyless/uncalled declaration. C independently requires an origin
and complete enum entry for **every retained raw enum**, including values,
fields and bodyless/uncalled signatures with no checked reference. Retain all
members, not only those mentioned in a body. Normalized native facts do not
retain an otherwise unused raw enum solely because source normalization used
one.

Struct/enum origins retain C's domains. For a needed class use only
**`1024 + classIndex`**, `0 <= classIndex < classCount <= 256`, in an origin
row. Resolve writable/optional class qualifications through that canonical
origin while preserving their complete type qualification. Require one origin
per canonical class per using context. This adds no class layout, allocation,
GC, cross-image handle or native-call support.

Retain an immutable compiler-owned private identity snapshot with
`BoundProgram`, `CompilationReference`, decoded `Module` and
consumer/provider/graph snapshots. Each occurrence records:

* using Module, local declaration key, exact nominal kind and local type map;
* the original expected declaration/type/field graph and its original context;
* for an enum, its original complete ordered member key/name/ordinal/value
  snapshot, public/primary/Flags/base/Blittable facts, and checked association
  to the original bound owner/member identities;
* selected Module/export and checked actual owned declaration/provider snapshot
  when resolved, separately from the expectation and any stronger relied-on fact.

For an imported enum, member assignments describe the **original expected**
`BoundEnumDefinition`, never whichever replacement is currently selected.
The enum-level import supplies the using Module's local proxy and the
provider's type export; its members retain the producer's keys without
consumer-local rekeying. A checked retained `ModuleImport` or Core provider
origin must agree with that selection. Supplying strings cannot authorize a
different owner, publish an imported enum or overwrite an original snapshot.

The snapshot captures the complete graphs and assignment records, not just
program object identity. Mutating a submitted list cannot affect it; a
host-created `with` copy, substituted enum table or host-modified nested
declaration is not the captured program. Compiler-owned transformations
must explicitly rebase the association with checked owner/type/member maps.
Clones and selection helpers on `BoundProgram`/`CompilationReference` must
forward this association, including helpers selecting unrelated Core roles;
reconstruction with default arrays is not preservation.

This is semantic data, not a serialized cache/authorization Boolean.
Original Binding rows and retained expected type records provide the wire
facts; no required expectation or relied-on fact may survive only in a host
cache. Refuse re-emission if the selected codecs cannot retain it. No new
top-level Module-identity field is added. An origin-free stream
need not manufacture a using identity. A decoded packed stream retains every
row's own context rather than guessing one root from table order or an outer
consumer identity.

### Adapter tokens versus persisted member identity

After enum-level binding resolves the actual T, the member's logical identity
is exactly **`(T, memberDeclarationKey)`**. For an owned enum, T is anchored by
its owned nominal Binding; for an expectation, T resolves through its selected
Module/type export. The input's Core-shaped tokens are checked selectors for
the source snapshot, not an additional cross-artifact identity relation.

Source-free decoding reconstructs that typed owner/key pair and the complete
ordered declaration from C's entries. It must not synthesize a flat
`CoreDeclarationIdentity.ExportKey` by concatenating an enum key with a member
key, copy a lookup name or ordinal into it, or require the original host
registration to recover a missing semantic identity. The current bound
validator's globally unique Core type/member tokens and direct token
comparisons do not replace context-sensitive enum occurrences or owner-scoped
member identity. Implementation must preserve the checked
adapter-token-to-owner/key map where those tokens exist and use the logical
pair for private identity/comparison/remapping. If a route cannot represent
that pair without fabricating a token or losing an original expectation, it
remains unsupported.

Re-encoding needs the retained logical pair and complete original declaration,
not the original adapter token's spelling. No required member mapping survives
only in an external host cache or a new wire field.

The logical reconstruction target here does not itself specify a replacement
for `BoundEnumDefinition`'s required Core identity fields.
[Private revision-105 enum bound identities](private-105-enum-bound-identity.md)
supplies that separately unapproved host-internal representation,
projection/source-binding and provider-resolution recommendation; it changes
neither this input collection nor the selected bytes.

### Reference projection and propagation

The context-free public `CreateReference(byte[])` and private
`CreateReference(Module)` remain **provider-scoped projections**. They retain
owned export keys, complete expected closure, inherited using contexts and
provider requirements, including entire enum/member snapshots; they do not
mint a new using Module, local proxy keys or new enum member keys.
When source binding incorporates this reference, preserve that provenance
through its local type remap. The explicit consumer inputs then establish the
new using context/proxy occurrence and capture its original expectation.
This fixes the API timing of C's definition-to-expectation projection: the
conversion requires an explicit using context, not one invented by the
context-free reference factory.

| Path | Required propagation |
|---|---|
| Producer selection and `CreateReference(Module)` | Retain needed owned/internal origins, complete enum entries/member maps and explicit nominal exports with their full closure. Projection retains exported roots and required contract-only facts, not private implementation bodies. A needed external type without its real public type export cannot be exposed by guessing a key. |
| Consumer creation, `Link`, restoration | Retain consumer-authored and imported occurrences separately; capture original enum/member and other expectations before `Function=null`, provider substitution or merging. New consumer Bindings change the enum proxy context, not producer member keys. Restored/linked programs carry the same obligations after removing a physical import. |
| `SetReference`, `CreateReferenceSetFromReferences`, set consumers | Merge origin-bearing references, complete enum/member snapshots and all complete type families together. Provider order selects no winner. Same-named distinct origins and same member keys under different enum owners remain distinct, or explicitly unsupported where current lookup/table limits cannot represent them. |
| `MergeGraphReferences`, graph-library projection/validation | Preserve every intermediate/leaf using context and full enum/member expectation in diamonds. Deduplicate only proven identical occurrences; two original expectations selecting one provider are both checked, not weakened into one. Keep actual type and cleanup/code origins in their separate roles. |
| Re-emission/snapshots | Existing origins and member assignments are authoritative; omission cannot remove them, and a new supplied root identity cannot restamp/rekey them. Cloning, projection and caches preserve exact schema, original expectations and selected snapshot/context identity. |

An unbound reference projection can retain external provider requirements;
it is not a claim that those requirements have been discharged. Operations
claiming resolved binding, native admission or execution must resolve the
complete required selected closure. If the single-provider or flat-set API
cannot supply that closure within its existing topology, refuse that operation
rather than search, drop the requirement or silently switch to a graph.

## Exact binding, replacement and remapping

Build the typed semantic export directory from **owned exported** Bindings.
An import selects exactly `(selectedModuleIdentity, selectedExportKey, kind)`;
require one unambiguous selected provider snapshot and its actual owned
declaration. Do not select by argument order, name/layout equality, a newer
revision, an imported proxy or a similarly named provider. Conflicting
selected provider snapshots of one logical Module reject; repeated graph
paths to the same selected snapshot need not create new nominal types.
Authentication remains the host's responsibility, not the result of a hash
or Binding.
The selected snapshot identifies a binding/cache input, not another nominal
key or a byte-identical-replacement requirement. Retained original snapshots
may differ from the selected replacement snapshot.

Resolve identities before type equality/deduplication, then compare every
original complete expectation under C+R and accepted directed satisfaction.
Preserve exact nominal/field owner correspondence, complete qualifications,
ordered fields and retained representation/ownership/state contracts.
Changing physical rows is allowed; matching field offsets or replacing an
expectation with actual provider facts is not a compatibility check.
Keep additional actual facts used by lowering as extra obligations. Uncalled
imports and dependency-only providers remain required on replacement.

For each enum expectation, resolve T first, then map each expected member key
to exactly one actual member of **that same T**. Compare the entire original
key/name/ordinal/value list, count, qualified lookup name, int32 base, public/
primary/Flags and retained Blittable commitment exactly under the enum owner.
Adding, removing, rekeying, renaming, reordering or changing a member is not
compatible with this complete-snapshot carrier, even when values are equal
or the member is uncalled. A physical enum/origin table reorder with agreeing
maps is allowed; a declaration-order change is not such a reorder.

For each retained `(source snapshot, using context, type)` build checked maps
for structs/classes/enums, references/optionals, pointers, signatures and
arrays. Nominal declarations anchor finite graphs; do not unfold nominal
recursion as structural equality or treat an in-progress pair as equal.
Clone a physical closure when its distinct using contexts/expectations cannot
share one complete meaning. Preserve all original occurrence rows; equal
private integers never merge origins.

Rewrite every binary type occurrence and parsed `ctype#i`/`fnptr#i`/`enum#i`
atom, including bodyless/native annotation roots. Rebuild typed-place
owner/field paths, scalar-leaf paths, summaries and class qualifications with
the correct maps. Rebuild the enum origin reference, context-local enum code
and owner/member associations together, preserving declaration ordinals and
all producer member keys. Enum constants retain their exact T and int32
payload; those four bytes contain no references even when they resemble an
index. Keys/Module identities do **not** change during index rebasing;
ordinary strings/source/debug data are not type atoms. Re-encode counts,
strings, indexes and affected outer lengths/hashes, not byte substitution.
Keep C's origin ordering and all existing aggregate byte/node/work/depth
budgets; validate forward references after the complete 105 type/origin/enum
universe is available.

## Native-package outers

For TNPK/TNGP producers, require ordinal agreement between explicit producer
`identity` and the input using identity for the newly authored producer
context, including owned definitions and imported proxies.
Retained packed logical Modules keep their identities. Consumer inputs supply
their own using identity: never use TNPK's **provider** identity, a TNSP
dependency, null TNGP root identity or graph `@` as a substitute.

Keep semantic nominal exports/imports separate from PE/code-symbol exports,
machine imports and the outer direct-import expectation table. Enum type
exports stay semantic-only, and member keys are not exports in either
namespace. Arbitrary semantic keys are not DLL symbols. Retain justified
semantic-only dependency edges even when no call/IAT entry survives, under
[C's unchanged outer-3 field layout and admission amendment](native-output-foreign-interface-private-persistence.md#native-containers-sets-graphs-and-snapshots).
Cross-check existing graph type/cleanup origins; they cannot replace the
nominal export key. Resolve outer direct-expectation type fields in the
**original importing inner Contract's** final universe, not the provider's.
Propagate the complete original enum/member snapshot and selected owner
through native producer/consumer/set/graph snapshots as through semantic
paths. This needs no new outer identity, member table, image field or hash
interpretation; reconstruct and re-encode inner directories and all affected
outer lengths/offsets/checksums under the unchanged exact schema.

AOT validates then uses stored images; root JIT does not rebuild providers.
Changed lowering/ABI facts require regeneration through the existing admitted
route, not repaired hashes or hidden joint regeneration. This input carrier
admits no new value crossing, runtime agreement or native provider shape.

## Focused acceptance cases

Use public compiler APIs and the existing NUnit module/reference fixtures;
no friend assembly, sample recognizer or visible-window opt-in. The following
are required tests, **not results claimed by this documentation change**:

| Case | Required observation |
|---|---|
| Owned/exported/internal roots | Round-trip explicit arbitrary keys, internal body-only origins, an otherwise unused explicit type export and recursive field closure. Public visibility alone creates no export. Missing required assignment/export declines, never produces a misleading zero table. |
| Enum/member positive inputs | Publish two unrelated eligible enums with explicit owner/member identities, arbitrary keys unrelated to names/ordinals/values, shuffled assignment input and duplicate-valued members. Retain a zero-member enum and an unused explicit enum export. Read member order from declaration ordinal, not input/key order; preserve Flags and actual Blittable 0/1. Same member key in different enums or equal to an ordinary/type export key is valid. |
| Enum assignment negatives | Reject duplicate assignments even when identical, duplicate ordinal/member identity or per-enum key/name, missing one member, extra/unmatched group, non-enum owner, ordinal -1/count, stale `EnumType`, wrong enum identity, correct ordinal with another member identity, wrong `DeclaringEnum`, conflicting `DeclarationKey`, NUL key and imported-as-owned. Validate invalid groups even when their unexported owner would be pruned. Missing keys cannot be repaired from identity export tokens, names, values, ordinals or a selected replacement. |
| Enum ownership and replacement | Import A's enum into B with a distinct B proxy key and original member keys. Preserve the full original snapshot through provider-scoped reference projection, source binding, Function erasure, source-free restoration and re-emission. Equal-shaped C, wrong type export/kind, redirected owner, or changed/added/removed/reordered/rekeyed member fails; physical table reindexing succeeds. Both compatible and incompatible independent diamond expectations are checked. |
| Snapshot association and omission | Mutate/clear submitted member lists after factory creation; output is unchanged. Passing a `with` copy or changed enum/member table rejects. Omit a complete retained map and preserve it; supply an incomplete or conflicting assertion and reject. Four-argument construction with a newly required nonempty enum and no retained map fails; an empty enum needs no member entries. Clone/select/reference/set/graph helpers cannot reset the association. |
| Enum exact schema and capacity | Independent B+C+R fixtures keep the complete-reference/origin/enum/interface order, mandatory empty counts, exact enum field extents and four-byte enum payload. Indices 0/667 yield codes 356/1023; count 669, structure count 257, missing/duplicate/dangling/context-wrong origin/enum entries and unsupported base/scope/access/primary/target fail. Exercise final legal shared byte/node/depth extents and one-over, cumulative members across enums/contexts, and checked allocation arithmetic. |
| Consumer and replacement | Producer A/key X and using Module B/proxy Y survive reference binding, Function erasure, source-free restoration and re-emission. A compatible reordered provider succeeds; equal-name/layout C, missing X, wrong kind, unexported definition or identity redirection fails. |
| Scalar-leaf and constructor closure | Require owner origins for a scalar field place; independently exercise nested reference/optional, pointer, complete signature and array closure. Removing any required nominal origin fails even when the final referent is scalar. Unavailable adjacent C/R codecs still refuse. |
| Class canonicalization | All supported qualifications use the same canonical class origin per using context. Index 255/count 256 works for semantic identity; count 257, qualified origin codes, duplicate canonical origin and absent class reject. No native class-execution result is inferred. |
| Inputs and ownership | Nulls, invalid/stale type selectors, duplicate keys/types, cross-kind/export collisions, imported-as-owned and conflicting retained assignments reject appropriately. Mutation of submitted lists after capture cannot change output. A genuinely origin-free route may use a zero origin count; omitted retained origins never do. |
| Exact text and extents | Identity lengths 1/64 succeed; 0/65, invalid initial/subsequent ASCII and non-ASCII identities reject. Declaration and enum member key byte lengths 1/256 succeed, 0/257 reject; include multibyte UTF-8, invalid Unicode/UTF-8, member zero scalars and canonically equivalent but ordinally distinct keys. Native case-insensitive path collisions remain separate from logical case-sensitive identity. |
| Reader/resource boundaries | Reject unknown Binding kind, invalid Boolean/reserved fields, malformed/truncated UTF-8/lengths, dangling type/export references and conflicting rows. Exercise count/byte/work/depth limits and forward references without resetting budgets or allocating overflowing extents. |
| Sets, graphs and native outers | Reverse provider/table order; retain two diamond expectations, including one incompatible uncalled expectation. Preserve semantic-only enum dependency edges and cleanup origins. Test outer producer/input mismatch and a consumer identity distinct from its provider; no nominal or enum member key appears as a PE export/IAT entry. |
| Original versus actual and re-encoding | Replacement strengthening never overwrites the original expectation; a later replacement satisfying only the substituted actual view is checked against every original and additionally relied-on obligation. Reorder physical enum/origin/reference/pointer/signature/array tables and rebuild all paths/atoms, member-owner maps and outer lengths/hashes. Same-looking ordinary text and enum payload bytes remain untouched; source-free re-encoding needs no original host registration or fabricated Core token. |

The first implementation slice must add the carrier, actual origin/enum
codecs, checked adapter/logical member maps and shared closure/remapping/
retention together on each admitted route before removing its nominal/enum
refusal. Remaining C/R features stay explicit support gates. Passing focused
nominal cases would not establish complete 105, Standard interchange, the
broader conformance milestone or native execution.

## Alternatives and remaining decisions

The **carrier amendment with a separate member collection is recommended**:
it uses the selected type/enum tables and Binding rows, adds explicit host
input for both owned and imported enums, and preserves original graphs.
A universal Module/TypeDef/Import/Export/context IR could remove more private
name-based limitations and serve open generic/alias/witness use. It would
migrate every owner, complete type and ordinary function/export path; it is
not necessary to supply this missing input. Reusing Standard/Core internals
may share checked helpers, but does not make those schemas/roles private
origin evidence.

Remaining unapproved decisions are exactly:

1. Adopt the proposed input records/five-argument factory, separate complete
   enum-member collection, checked adapter-to-logical mapping, overload
   behavior and provider-scoped reference/proxy-formation timing as the
   provisional private API direction. These are concrete recommendations,
   not accepted or stabilized APIs; this document does not approve them.
2. Select the real logical Module identities and declaration/proxy/export
   assignments and producer member keys/actual identity associations for each
   embedding host invocation, with positive ownership/provider provenance.
   This document supplies no production values, Core authority selection or
   generated-key convention.
3. If CLI delivery is required, approve a separate explicit transport for
   these inputs; existing commands must not invent them. Export aliases,
   standalone type-only package cardinality and a general nominal/context IR
   require separate decisions if requested, not implementation guesses.

Within this recommendation, member collection placement, totality, owner/
ordinal/identity matching and original-versus-actual handling are specified,
not deferred to implementation. The missing host-internal bound identity
carrier is separately specified in the unapproved
[enum bound-identity candidate](private-105-enum-bound-identity.md);
implementing and qualifying the admitted routes remain work rather than new
wire decisions. No choice about 105's selected
composition, per-Module Binding meaning, enum/member encoding, directory
order, reference-row width or class-origin domain is reopened. This document
and its index entry approve none of those proposals and change no normative
spec.
