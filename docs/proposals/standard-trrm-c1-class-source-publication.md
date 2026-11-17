# Standard TRRM C1 class source publication and provider closure

**Status: unapproved; provisional direction under the user-authorized
missing-contract workflow.** This completes a producer-input decision, not
normative adoption, compiler support or runtime qualification.

## Decision and boundary

Recommend an **exact-program, source-anchored class export mapping**, backed
by compiler-owned checked declaration facts, and retention of the complete
class/member TypeDef/TypeSpec/MethodDef/Body closure. Reuse the
[separate class declaration carrier](standard-trrm-c1-class-declaration-carrier.md),
ordinary Import/Export identities and existing complete-type comparison.
Do not obtain declarations by deleting offsets from `BoundClass`, treating
classes as structs, or accepting caller-supplied declaration facts as checked.
**Metadata-only describes the absence of computed object geometry, not
permission to omit constructor declarations, contracts or implementations.**

The [C1 publication rule](standard-trrm-exception-class-witness-binding.md#binding-inputs-and-publication)
already chooses source ordinal/full declaration span/kind/explicit key.
It does not specify the concrete class input or how to preserve source
declaration facts before executable layout. Those are the new decisions
here. Complete contextual type comparison is an existing binding obligation,
not a second equality relation or a new reason to approve C1.

Complete constructor/member source publication and binding require the
distinct exact [constructor successor](standard-trrm-c1-constructor-declarations.md#allocation-and-exact-agreement),
with the **same exact selection on the root and every supplied provider**.
Existing [C1M1](standard-trrm-c1-class-declaration-mutability.md#exact-schema-and-proposed-assignment)
remains exactly C1 plus class mutability; its bounded type-only class
references and inactive Transfer contract are unchanged. They do not acquire
constructor/member publication or automatically convert to the successor.
Original C1 may retain an opaque class identity with mutability unknown;
it cannot use this route to publish/bind a complete class, even one whose
source declaration is immutable. Preserve
[exact-schema agreement](../specs/representation/format-versioning.md);
no automatic promotion, schema alias or zero-bit default supplies the fact.

No accepted text is superseded. The [specification index](../specs/README.md)
leads to [declarations/constructors](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
[constructor outcomes](../specs/language-contracts.md#22-declared-outcome-relationships),
[construction/publication](../specs/language/ownership-and-cleanup.md#construction-and-publication),
[complete types](../specs/language/types-and-normalization.md),
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [binding/replacement](../specs/representation/binding-and-replacement.md).
Those contracts remain authoritative. C1, C1M1, the constructor successor and
the carrier remain **unapproved dependencies**, not accepted metadata contracts. This proposal
assigns no byte, flag, Feature, section, version or CLI/JSON grammar.

### Source evidence and alternatives

These are inspected-source observations, not newly executed test results:

| Evidence | Boundary |
|---|---|
| [`Binder.ManagedClasses`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs), [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | `DeclareClassFactories()` requires an explicit constructor (TRM3011); the host also rejects empty fields. A unit-result `IsClassConstructor` initializer and separate Boolean/out `TryCreate` are host representations, not the public source construction contract. |
| [`Emitter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`type-root projection`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs) | C1M1 has bounded type-only class projection/re-emission. That unchanged operation is distinct from complete constructor/member source publication under the new successor; removing publication guards alone supplies no complete source closure. |
| [`Class carrier`](../../sources/Terrum.CodeAnalysis/Semantics/BoundStandardClassDeclarations.cs), [`provider validator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs), [`carrier tests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Generics/BoundStandardClassDeclarationTests.cs) | Metadata class retention and provider binding do not by themselves establish complete constructor/member/Body publication. The declaration carrier stays distinct from executable layout, and these tests are not full source-producer acceptance. |

A field-only source producer is therefore not a usable boundary for currently
checked classes. The accepted grammar independently requires an ordinary
constructor body, or a partial primary with exactly one implementing
secondary; it does not impose the host's nonempty-field limit. Constructor
and member retention below is mandatory, not an optional expansion.

Use a separate source-anchored record, following
[`TrRmSourceGenericMethodExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceGenericMethodExport.cs)
rather than extending name/arity-oriented struct/enum/interface inputs or
redesigning executable class layout.

## Candidate source input and capture

These are unapproved C# API declarations in `Terrum.CodeAnalysis.Modules`,
not Terrum syntax:

```csharp
public sealed record TrRmSourceClassExport(
    BoundProgram Program,
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    string ExportKey,
    ImmutableArray<TrRmSourceClassMemberExport> MemberExports);

public sealed record TrRmSourceClassMemberExport(
    int SourceInputOrdinal,
    TextSpan DeclarationSpan,
    string ExportKey)
{
    public string? ConstructionProjectionExportKey { get; init; }
}

public static byte[] EmitStandard(
    BoundProgram program,
    string moduleIdentity,
    string schemaIdentity,
    uint moduleTargetRequirement,
    IReadOnlyList<TrRmSourceTypeExport> typeExports,
    IReadOnlyList<TrRmSourceGenericMethodExport> genericMethodExports,
    IReadOnlyList<TrRmSourceClassExport> classExports);
```

This required-seven-argument `TrRmExecutableCompiler` overload preserves
existing overloads and avoids same-arity null ambiguity. `TextSpan` is the
existing text type. Require the exact constructor successor even for empty
lists; siblings cannot import another schema's producer support. Empty sibling lists do not omit
class constructors/methods. Omitted/empty class mappings assign no keys and
never drop retained declarations, publications or aliases.
Check retained provider-origin schemas before emission as well; a C1M1
type-only reference cannot be restamped as successor constructor/member closure.

1. Capture all lists and member arrays before processing; reject null lists,
   entries, anchors, keys and default member arrays. `Program` must be
   reference-identical to the checked program with its compiler-owned source
   association. A hand-built/loaded/copy/rebind/rewrite cannot acquire source
   eligibility from coincident names/spans; changes require recapture.
2. Select an original input ordinal and exact full primary `ClassDeclaration`
   node `Span`: nonnegative start, positive length, checked end within the
   immutable UTF-16 snapshot. A name/body/overlapping span, secondary,
   imported proxy or generated factory is not a match. The record fixes kind.
3. Retain the whole composed identity in accepted input/member order.
   Distinct keys may alias one primary/TypeDef; repeated keys reject.
   Pool member assignments across aliases, checking coverage once. An empty
   member array supplies no keys, not evidence of no members.
4. Exported roots are public; internal closure declarations gain no access
   or Export. Project all source classes and required closure, including
   unused declarations. The map is not a trim list; unmapped classes remain
   unpublished, not incomplete.
5. Keys use metadata's nonempty, NUL-free scalar-text domain, ordinal decoded
   equality and String capacity, with Module-wide uniqueness across all kinds.
   No derived/default/normalized/truncated keys or rekeying of loaded/imported
   publications and aliases.

Member selectors inherit the exact program/input/span rules and select direct
primaries in that class's partial composition. Syntax fixes the existing kind:
constructor/method to MethodDef, getter-only property to PropertyDef, selected
field to FieldDef. Require keys for exposed public constructors/methods/properties;
reject cross-owner, secondary-only, inherited, private and synthetic-factory
selections. A property needs no invented getter key. Field exports retain their
layout/access and Feature 13 rules. Private/internal dependencies retain actual
definitions/Bodies without becoming exports.

Capture checked input/primary/partial anchors, exact name/access/mutability,
complete field types/roles, declared Layout and every required member,
contract, conformance and synthesized edge or checked absence. Retain exact
member owner/declaration/implementation associations through rewrites; do not
reselect changed source. Checking needs no executable object geometry and
cannot legalize invalid source. Local tokens map to the supplied output
Module's TypeDefs; artifact views use completed checked bytes/row maps, not
fabricated snapshots. Imported origins remain unchanged. `BoundClass.Size`,
offsets and private class layouts are not source facts; an associated
executable class and metadata capture remain one nominal declaration.

### Required constructor and member closure

Retain ordinary nongeneric classes, optional declaration `mut`, required
constructors and represented members. Partial composition retains the actual
implementation. Empty metadata is valid but not a currently checked source
success through the host's empty-field restriction. Unannotated source classes
retain default Auto, no Blittable bit, alignment/pack zero and complete ordered
instance fields under existing access rules;
[registered struct layout schemas](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
do not apply to classes.

| Retained fact | Required mapping using existing identities/records |
|---|---|
| Constructor declaration | Associate the exact checked constructor with its owning class TypeDef and actual initializer MethodDef; `ownerTypeDef` and Module ownership agree. Preserve source access, caller-unsafety, ordered logical parameters/modes/defaults, receiver-initialization role and every declared completion guarantee. A host unit-result initializer is not a new source result declaration or an ordinary live-receiver method. |
| Implementation | The local actual MethodDef has its unique nonzero `bodyRow` and real checked Body. For partial constructors, retain primary-to-implementing-secondary correspondence and the implementation's actual source ownership. Map bound receiver/formals, field selections, calls and contracts to their exact TypeSpec/Param/FieldDef/MethodMap/Body references; never substitute an empty Body or `TryCreate`. An imported expectation remains bodyless and binds to the actual bodyful provider. |
| Completion and phases | Retain the minimum initialized field/property identities promised on normal constructor completion, required validity/dependency facts and exact pre/postconditions. Use C1's existing graph/contract records only where they express those exact facts. Declaration initializers precede constructor clauses/body; caller initialization and final completion validation follow. Constructor return does not imply final publication or permission to call methods on a partial object. Failure publishes no invented normal guarantee. |
| Other members | Ordinary instance/static methods use owner TypeDef, exact MethodSig/Params, contracts and actual Bodies; receiver presence/access is not a new MethodDef flag. Getter-only properties use PropertyDef and its exact getter MethodDef/Body. Preserve generic method/constraint and interface/witness edges under their existing C1 owners, including uncalled members and required Features; no map erases them. |

Do not drop required initializers, constructors, members or conformances.
Where exact selected records suffice, implement their mapping rather than
inventing a codec or declaring source unsupported because today's emitter
rejects it. Unrepresented accessor, static-initialization, ownership/access
or witness facts remain contract gates. No field-only profile or narrower
accepted class/witness scope is selected.

The separate [class-source CLI transport proposal](standard-trrm-c1-class-source-cli-transport.md)
defines a candidate file input for these existing source records. It adds no
class/member/Body support and does not weaken the closure requirements above.

### Constructor representation decision

The separate, unapproved
[constructor declaration/projection proposal](standard-trrm-c1-constructor-declarations.md)
supplies the mandatory source-free role, receiver authority, minimum completion
and initializer-to-result correspondence. Its distinct exact successor
selection is required for this complete-publication route; C1M1 retains its
original composition and bounded type-only behavior without those records.

The member selector's `ExportKey` selects the source initializer declaration.
Its optional `ConstructionProjectionExportKey` explicitly selects that
constructor's genuine complete-result projection under the companion's rules;
no name-derived key, Boolean/out factory or arbitrary helper substitutes.
Body `0B` remains the unchanged selected exception-construction plan and
targets that complete projection, not the unit-result initializer.

Likewise, PropertyDef has a getter reference, not setter/init-accessor
correspondence; required such semantics need their own exact decision rather
than encoding unrelated methods and claiming the property complete. These
are specific missing contracts, distinct from host member emission, class
layout and body-remapping implementation gaps. Until required representations
are supplied, publication fails as **incomplete closure**, not successful
type-only producer support. Runtime/object ABI remains a separate gate.

## Expected/actual binding and complete comparison

Extend the applicability of the existing explicit
`CreateStandardReference(input, moduleIdentity, providers, typeExportKeys)`
selection to constructor-successor class roots, without changing C1M1's
bounded type-only class reference route or the other schema routes.
Do not reinterpret an A2 projection as classes. Require a nonempty distinct
key list selecting public provider-local kind-2 TypeDefs in the selected
Module. Type-root selection is not a member-pruning request: retain required
constructor/member declarations, explicit public member keys and actual
provider Bodies even when no callable is independently requested. Source
lookup uses original declaration names, not keys; aliases retain each selection
without new types. Closure-only declarations gain no lookup-root or private access.

Before returning a reference, loading or publishing:

* Own immutable input snapshots; preflight every supplied exact schema and
  selected Module's edition/Features, target/runtime and required closure.
  An unselected provider position is not a default and a small root list
  cannot bypass supplied-input admission.
* For each retained TypeDef Import, check its expected definition's Module
  and ModuleRef ownership, then resolve the exact Module/revision and key
  to one public, provider-local TypeDef. Match nominal kind before contract
  comparison; ModuleRef target/runtime requirements must match the provider.
  Reject missing/conflicting revisions, wrong kinds, proxy re-exports,
  duplicate Import triples and one expectation resolving to conflicting
  definitions; do not search another key/provider/name.
* Keep each expected typed declaration in its **original using snapshot**
  separately from the actual typed declaration in its provider snapshot.
  Compare namespace/name, visibility and inherited declaration mutability
  exactly, both Boolean directions. Correspond the complete ordered FieldDefs by declaration
  position, comparing names, access, static/mutable roles and complete types.
  This route retains full fields/Layout views, not a resilient or redacted
  class facade. No physical row equality or private field-name export is used.
* Bind each retained constructor/method Import to its exact public provider
  MethodDef and already bound class owner. Compare its original complete
  signature, access, effects, initialization/completion and dependency contracts
  under existing directed satisfaction. Preserve property/getter and witness
  target correspondence. A same-named method, compatible machine signature or
  old acceptance flag is not constructor/member identity.
* Check provider-owned Imports recursively in their own using Modules, not
  the root's provider context. Check every retained expectation/alias and
  equality relationship, even after an earlier resolution succeeded.
  Retain stronger facts actually relied on; actual facts never overwrite
  the original expected view.

### TypeSpec closure

Compare **normalized complete types** under each side's artifact, exact
schema, original using Module, declaration/formal and substitution context.
Apply this to fields, constructor/method signatures, properties and witness
terms. Structural rows have no implicit Module cursor. Never compare raw
TypeSpec references across snapshots or stop at a pointer/handle because its
width is known.

Nominal correspondence comes from checked bindings, retained origins or a
meaning-preserving remap, not recursive shape matching. Resolve self/proxy
edges through that correspondence. Across replacement, resolve retained keys
against the new snapshot and preserve alias equalities; snapshot-local row
changes are not identity changes. A private direct edge without a checked
correspondence stays tied to its retained provider closure or refuses
rebinding; no name-derived private selector is introduced.

| Form | Required comparison or refusal |
|---|---|
| Inline F2/F3 and row built-ins `01` | Compare the assigned atomic/family identity and complete argument/qualifications after allowed normalization. Equivalent inline/indexed encodings compare equally; equal widths and unassigned codes do not. |
| Nominal `02/03` | Resolve direct definitions and TypeDef Imports to the same actual nominal identity, retaining every expected-to-actual obligation. A direct reference naming an expected proxy still honors its binding. Class, struct and interface remain distinct; equal names/fields do not merge them. |
| Optional `06`, families `07..0C`, initialization reference `0E` | Compare the normalized constructor and every child, with exact inner/outer write, GC, optional and init positions. Validate qualifier applicability in its containing position. Pointer-cell initialization never becomes pointee initialization; pin/backing dependencies are not qualifier bits. |
| Native function pointer `0D` | Compare calling convention, complete anchor signature/modes/contracts and the noncallable bodyless-anchor role, not anchor name/row. Independently required native support remains gated. |
| Formal `04`, constructed `05` | In admitted generic signatures/witness terms, map owner/kind/ordinal and compare the selected constructor plus all ordered type/constant substitutions and retained constraints/defaults/evidence. A free formal in a nongeneric class field is invalid. No CG1 or generic-class storage codec is inferred. |
| Existential `0F` | Preserve constructor/hidden-slot context and visible arguments; a bare slot is neither a storable field nor `void`. Required per-value evidence remains separate. |
| G's captured-type `10` | Compare/remap exact graph/capture scope under [G's owner](general-predicate-graph-schema.md). An escaped field/signature binder is invalid; matching capture-row bytes is insufficient. |
| Other forms | Refuse forms absent the selected schema, including A2/A3 fixed-array form `11`; no sniffing, private decoding or fallback. |

Use a finite contextual comparison worklist, including every outgoing
nominal, field, layout, signature and required contract edge. Deduplicate
only the same complete expected/actual/context/relation pair. Recursive
class-handle edges can close a finite nominal graph; structural cycles,
invalid inline storage cycles and expanding substitutions do not become
valid by memoization. An in-progress pair is not success: the whole closure
must finish, and a mismatch behind a back-edge still fails. Resource
exhaustion declines explicitly, never counts an unvisited edge as satisfied.

### Declared layouts, not object geometry

Validate complete owned field/Layout/Contract ranges and schema flags before
use, with checked bounds and schema-owned row namespaces under the
[layout contract](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations).
Require exactly
one default Layout and at most one variant per exact TargetProfile identity
and rule revision. Each variant contains every instance field exactly once
in its complete ordered list; static fields are excluded. Compare the
**entire retained variant set**, matching default versus exact decoded
profile identity/revision rather than profile-row numbers. Compare kind,
Blittable commitment, declared alignment/pack and ordered corresponding
FieldDefs and their complete TypeSpecs. Reordered physical rows may agree;
missing/extra variants, sparse patches or changed membership do not.

Metadata comparison must not request class size/alignment/offsets or impose
Copy, rootlessness, Sequential/Blittable struct restrictions or A2 array
geometry merely because a class has instance fields. It also must not erase
an independently retained inline/fixed/coupled representation obligation.
Validate such an obligation under its actual type/target contract, or
refuse unsupported affected use. Reuse the validator's identity/worklist
mechanisms with this separation; a global inline-storage exemption or an
unchanged A2 physical-layout path is insufficient.

## Remapping, failure and acceptance

Preserve the carrier's expected/actual separation through references,
binding, shared traversal/rewriting, mandatory verification and lowering.
Re-emission rebuilds Module partitions, TypeDef/Import/Export, complete
FieldDef/Layout/LayoutField/Contract ranges, TypeSpecs/signatures/anchors,
constructor/member MethodDefs and actual Bodies, getter/witness/completion
correspondence, requirements and Body maps. Clone context-sensitive rows rather
than changing another Module's interpretation.
Preserve original obligations after Import-to-direct resolution and
replacement; old snapshots alone do not prove newly written references.

Private local children need no invented export keys. Across artifacts use
the already selected Import/key, or preserve provider-owned direct definitions
in a checked packed closure as C1 requires; otherwise refuse that publication.
Never write an external row as a local reference, choose an alias by row order
or export an imported proxy. Packing cannot relabel the provider Module,
drop private required facts or claim full replacement compatibility after
discarding published obligations. Loaded re-emission needs no source/spans
and cannot accept source mappings as replacement publication authority.

Follow existing API failure categories: invalid/null caller configuration
uses the corresponding argument exception, malformed serialized
structure/binding uses `InvalidDataException`, and unsupported complete
forms/use use `NotSupportedException`; scoped resource refusal remains
distinct. Do not return an empty successful reference or partial artifact.
Finish capture, closure checking and output verification before returning
bytes or replacing output; no worker is launched to discover compatibility.
Consumers need not semantically re-prove producer-authored Bodies.

Required focused tests through public compiler APIs, **not results**:

| Case | Expected outcome |
|---|---|
| Source input | Use currently checked ordinary classes with private initialized fields, an explicit bodyful constructor and ordinary methods, in immutable/mutable and supported partial variants. Map class and public member keys explicitly; retain the real constructor/body. Test same offsets across inputs, aliases, collisions and stale/wrong-owner/secondary selectors. A field-only capture or an unsupported result is not a passing producer test. |
| Constructor/member closure | Round-trip exact owner/signature/access/effects, completion guarantees and real constructor/method/getter Body mappings. Binding rejects missing constructor/key/actual Body, wrong owner/formals or weakened guarantees. Producer checks establish no empty-stub, stripped-initializer, arbitrary-factory or Boolean/out `TryCreate` substitution; consumers need not re-prove Body truth. Preserve partial implementation ownership and bodyless expectations versus bodyful actuals. |
| Provider closure | With source/debug absent and the selected provider non-first, bind nested class/struct types and provider-owned Imports across several Modules. Distinct aliases share an identity; every original expectation survives and must pass. Wrong Module/key/kind/proxy, missing dependencies and conflicting revisions fail. |
| Complete types | Equal TypeSpec row numbers containing different atoms or nominal origins fail; different rows and equivalent inline/indexed forms pass. Mutate a nested pointee, optionality, inner/outer authority, init/GC position or function-pointer contract without changing field names/layout: fail. An escaped formal/capture or unsupported array/generic/evidence closure refuses. |
| Recursion and layouts | Finite handle recursion passes without object sizing; a mismatch beyond its back-edge fails. Check genuine empty metadata membership separately from the host's source empty-field restriction, wrong owners/ranges/flags, omitted/duplicate defaults, static membership and exact profile/kind/flag/alignment/pack/order mismatches. Equal widths do not repair a mismatch. |
| Mutability and schema | Both declaration-mutability mismatches fail despite identical readonly fields. Exact successor selection is required even for empty publication maps and all supplied providers. C1M1 type-only behavior stays unchanged; C1M1, original C1, A2, G1 and private inputs never become successor publications through byte inspection, fallback or relabeling. Metadata-only success grants no executable class/struct ABI. |
| Transformations | Remap class/member/Import/field/Layout/TypeSpec/Contract/Body rows with valid ownership, range and dependency order; clone context-sensitive rows and reconsume without source. Check constructor/member body references, normal-completion subjects, getter/witness and `0B` plan correspondence where retained, original expectations and replacement failure after prior success; no snapshot-passthrough substitute. |

The source record/capture and metadata-only closure recommendation remain
unapproved. The companion owns the provisional constructor representation;
existing normalized contextual binding still governs type equality. Required
member semantics missing an exact selected codec remain separate decisions,
not permission to discard constructors. C1's complete class/witness and source
producer acceptance remains unmet until the real class/member closure round-trips.
CLI transport, physical class/runtime agreements and qualification remain
separate. No accepted specification or implementation inventory changes here.
