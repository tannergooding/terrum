# Exact Core registration for source layout schemas

**Status: unapproved candidate; provisionally actionable under the user's
authorized missing-contract workflow.** This does not constitute normative
adoption, implementation evidence, a production Core selection or a stable
public API.

## Decision and preserved contracts

Recommend an immutable, host-selected **`LayoutCoreRegistrationSet` for one
using Module**, delivered through
`CompilationOptions.LayoutCoreRegistration`. Reuse the existing
`CoreDeclarationIdentity`, `CoreEnumDefinition` and
`CoreProviderModuleBindings` mechanism. Add a bounded layout-schema descriptor,
not layout roles to `CoreNativeAttributeRole` or a general Core catalog.

This settles only the missing host carrier and source-binding hand-off for
the exact `LayoutKind`, `Layout` and `Blittable` selections already required by
[Windows x64 data layout §4](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas).
It replaces **no accepted source or target rule**. In particular, preserve:

- The required nominal `LayoutKind` domain and its existing
  `Auto=0`, `Sequential=1`, `Union=2`, `Transparent=3` cases.
- The accepted argument types, labels, omission rules, placement and
  multiplicity; packing/alignment limits; transitive explicit alignment;
  layout eligibility, padding, access and overflow rules in
  [§§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas).
- The assigned TRRM `Layout` and `LayoutField` payloads and their declared-input,
  complete-variant and target-derived geometry rules in
  [metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics).

No Terrum grammar, layout formula, target identity, Feature, record, wire
revision, private revision, compatibility reader or CLI configuration syntax
is proposed. Interface vtable registration is not this storage-layout role.

## Evidence and the missing boundary

| Inspected owner or implementation | Established fact |
|---|---|
| [Windows layout §4](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas); [Core inventory §1 and LCI-35](../specs/libraries/adopted-inventory.md#1-status-and-identity) | Only the exact selected Core schemas/enum select these semantics. Core is an authority, not a guessed namespace or package binary. The complete storage-layout schema is already accepted. |
| [Attribute grammar and semantic rules](../specs/grammar-spec.md#31-source-file-structure-and-declarations); [recorded binding](../specs/language/names-and-binding.md#calls-conversions-and-recorded-binding) | Ordinary schema-resolved argument mapping, checked constants and declaration identity apply; subsequent phases do not rerun source lookup. |
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | Shared identity/enum descriptors, provider-origin entries and an immutable binding-snapshot token exist. Origin entries alone are not a checked producer export map. `NativeCoreRegistrationSet` requires three native enums and four native schemas; it has no layout selections. `CoreNativeAttributeSchema` describes native companion-enum relationships, not the layout schema. |
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), `CompilationOptions` and `SelectNativeCoreRegistration`; [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | Explicit options delivery and internal reference-carried selection exist for native registration, with duplicate-input rejection. No corresponding layout input exists. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `DeclareStruct`, `IsNoArgumentBlittableAttribute`, `TryDecodeLayoutAttribute` | Layout recognition uses syntax names/token shapes without a selected schema or nominal enum. The decoder accepts terminal kind spellings and rejects packing/alignment controls before ordinary constant evaluation. |
| [`BoundEnumTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs); [`Binder.ConstEval.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs) | Shared nominal enum definitions and checked enum constant evaluation are available. Layout must use that machinery rather than acquire a separate string/value evaluator. |
| [`PublicFixedLayoutStorageTests.RegisteredSourceLayoutAttributesSelectTheFixedLayoutCommitment`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/PublicFixedLayoutStorageTests.cs) | The fixture currently supplies no registration while asserting a fixed commitment. Its name is not evidence that exact registration is implemented. |

Implementing the accepted argument/layout rules is ordinary conformance work.
The undecided part is the concrete delivery and validation of the three
selected declarations. The existing
[native configuration](native-core-provider-configuration.md) and
[Module-origin carrier](core-provider-module-origin-carrier.md) proposals
provide the reusable authority/context policy, but do not designate layout
roles or their host carrier. This proposal extends that mechanism only.

## Candidate host API and complete schema projection

All names and enum ordinals below are **unapproved in-process API candidates**,
not Terrum declarations, registry assignments or serialized tags. Place them
in `Terrum.CodeAnalysis.Semantics`.

```csharp
public enum CoreLayoutAttributeArgumentKind
{
    None = 0,
    LayoutKind = 1,
    UInt32 = 2,
}

[Flags]
public enum CoreLayoutAttributeTargets
{
    None = 0,
    InlineStruct = 1,
    IntegerBackedEnum = 2,
}

public readonly record struct CoreLayoutAttributeArgument(
    string Name,
    CoreLayoutAttributeArgumentKind Kind,
    CoreDeclarationIdentity? EnumType,
    bool IsRequired,
    bool AllowsPositional,
    uint? DefaultUInt32);

public sealed record CoreLayoutAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    ImmutableArray<CoreLayoutAttributeArgument> Arguments,
    CoreLayoutAttributeTargets Targets,
    bool IsPrimaryTargetOnly,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable);

public sealed class LayoutCoreRegistrationSet
{
    public CoreProviderModuleBindings ModuleBindings { get; }
    public string UsingModuleIdentity { get; }
    public CoreEnumDefinition LayoutKinds { get; }
    public CoreLayoutAttributeSchema Layout { get; }
    public CoreLayoutAttributeSchema Blittable { get; }

    public static LayoutCoreRegistrationSet Create(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition layoutKinds,
        CoreLayoutAttributeSchema layout,
        CoreLayoutAttributeSchema blittable);

    public bool IsValid();
}
```

`UsingModuleIdentity` derives from `ModuleBindings`; construction is private
except through the validating factory. These are immutable projections of
complete checked producer declarations, not user-source assertions.
The schema descriptor includes its argument and placement facts so admission
can reject a changed schema instead of trusting a layout-role label.
`Layout` and `Blittable` slots select their respective roles; there is no
additional independently configurable role flag.

### Identity and enum validation

`Create` validates the whole set before publishing it. Null required
reference inputs throw `ArgumentNullException`; all other malformed
registration inputs throw `ArgumentException`. `IsValid` applies the same
structural checks. A submitted invalid set cannot be ignored because its role
is unused.

1. Require valid existing `CoreProviderModuleBindings`. Every selected
   declaration resolves to its exact checked provider/export/kind in that
   snapshot, with the original complete expectation retained under the
   [Module-origin contract](core-provider-module-origin-carrier.md#proposed-host-input-and-adapter-shape).
   Each provider identity has one agreeing selected origin. Schemas and enum
   may come from different explicitly selected provider Modules.
2. The enum has `Kind=EnumType`; each member has `Kind=EnumMember`;
   each schema has `Kind=AttributeSchema`. Identities and qualified names are
   nonempty, well-formed ordinal text without NUL. Qualified names come from
   the actual checked declarations and obey ordinary source naming rules;
   export keys are not inferred from them. No production namespace, provider,
   revision or key spelling is selected here.
3. `LayoutKinds` is public, primary, nongeneric, non-Flags and exactly
   `int32`-backed. It has exactly four distinct public members with the
   accepted names/values listed above, no extra aliases or cases. Validate the
   complete set, not just the value used by a particular annotation. Preserve
   the producer's member declaration order; the case list does not prescribe
   a new source declaration order.
4. Every member's `DeclaringEnum` equals `LayoutKinds.Identity`, its provider
   equals that enum's provider, and its actual identity and
   producer-assigned `DeclarationKey` are present and unique in their existing
   scopes. Member keys are enum-owned keys, not independent Module exports.
   Do not generate them from names, values, ordinals or key prefixes.
5. Selected declaration/member identities cannot be reused for different
   declarations or roles. `Layout` and `Blittable` must resolve to distinct
   actual schemas; one provider/export key cannot select both an enum and a
   schema by changing the requested kind. Retain the enum's actual
   `BlittableTargetRequirement` under the existing carrier's supported domain;
   source-constant use neither adds that commitment nor substitutes the
   provider Module's target requirement for it.

The host authorizes its producer adapter and selections; public descriptor
construction does not authenticate them. An authorized in-process producer
can supply these complete declarations under real semantic origins.
Artifact-backed declarations require their independently admitted exact codec;
an adapter cannot invent missing exports or reinterpret unsupported rows.

### Exact schema shapes

Both schemas require `GenericArity=0`, `IsPublic=true`, `IsPrimary=true`,
`IsRepeatable=false`, and `IsPrimaryTargetOnly=true`.
`IsPrimary` describes the selected schema declaration; the separate
`IsPrimaryTargetOnly` restricts attachment to the annotated type's primary
contract.

| Slot | Exact `Targets` | Complete arguments |
|---|---|---|
| `Layout` | `InlineStruct` | Exactly `Kind`, `Packing`, `Alignment`, projected in that order below |
| `Blittable` | `InlineStruct \| IntegerBackedEnum` | An initialized empty argument array |

`InlineStruct` includes ordinary inline Copy and unique structs, not classes,
interfaces or tagged source unions. These flags assign no new source targets.
Unknown target bits, a default/uninitialized argument array, missing/extra
arguments, altered labels/types/defaults or widened placement/repetition
reject.

| Argument | `Kind` | `EnumType` | `IsRequired` | `AllowsPositional` | `DefaultUInt32` |
|---|---|---|---|---|---|
| `Kind` | `LayoutKind` | Exactly `LayoutKinds.Identity` | `true` | `true` | `null` |
| `Packing` | `UInt32` | `null` | `false` | `false` | `16` |
| `Alignment` | `UInt32` | `null` | `false` | `false` | `null` |

Labels are ordinal and case-sensitive. `None` and unknown argument-kind
values reject. This array is the canonical host projection, not the order
required for source named arguments or an attribute constructor ABI.
Only `Kind` admits the first positional argument or its named form.
No argument is a mutable property/FieldDef and no enum-to-integer retagging
is permitted.

Keep supplied-versus-omitted state during argument mapping. `Packing`'s
semantic default applies to Sequential/Union; it does not manufacture a
supplied control on Auto/Transparent. Omitted `Alignment` remains absent
until target layout derives alignment; `null` is not a numeric zero default.
Explicitly supplied controls remain supplied even when equal to a default.
All value restrictions and kind-dependent refusal remain the accepted
[§4 rules](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas).

## Selection, context and binding

Add exactly this public options property:

```csharp
public LayoutCoreRegistrationSet? LayoutCoreRegistration { get; init; }
```

All existing options-bearing source factories (`Create`, `CreateLibrary`,
`CreateWithNativeImports`, `CreateCoreLibrary`, including their tree/reference/
buffer forms) forward it. Overloads without that options input select no
layout registration unless an explicitly host-configured reference supplies
one for the same using context. `default` options have no selection; Core
compilation mode, an ordinary dependency, a native registration or a namespace
does not supply one implicitly.

Mirror the existing native hand-off with internal
`CompilationReference.LayoutCoreRegistration`,
`BoundProgram.LayoutCoreRegistration` and a captured binder input.
An ordinary reference decoded from normalized provider declarations has no
layout registration merely because its producer used one. A reference-carried
selection is host configuration for the **using** context, not propagation of
the exporting Module's authority.

Select once before affected declaration binding and constant evaluation.
Supplying the layout selection through both options and a reference throws
`ArgumentException` naming `LayoutCoreRegistration`, even for the same object.
If native and layout selections coexist, require the same
`CoreProviderModuleBindings` object: its identity is the existing immutable
binding-snapshot token. Equal using-Module strings or equal origin lists do
not suffice. The host can construct both groups against one snapshot containing
their provider union; conflicting snapshots reject before binding. This
does not require a native registration for a layout-only compilation.
The selection describes the existing Windows x64 schemas; it does not itself
select a target, authorize an unsupported target combination or add a
representation commitment to the schema provider.

1. Admit the selected enum into the shared nominal enum directory, preserving
   its definition, actual member keys and originating context. An existing
   proxy for that exact definition must agree with the complete expectation;
   do not allocate another nominal type or overwrite a conflicting definition.
   Expose the two checked schemas to ordinary attribute lookup in that context.
2. Resolve each annotation through ordinary lexical namespace/import and
   declaration lookup. Compare the resolved schema identity to the selected
   slot, not the input spelling. Qualification and independently admitted
   aliases preserve identity; they do not establish it.
3. For an exact selected schema, validate target, primary-only authorship,
   multiplicity and ordinary argument mapping. Evaluate arguments through the
   shared checked constant machinery. `Kind` must finish with exact selected
   enum type and value 0–3. Integers and other enums with equal bits do not
   qualify; an unnamed value of the correct enum rejects at this schema use,
   not globally. A checked value need not originate in a member-access token.
4. Bind Packing/Alignment as exact `uint32` arguments under ordinary contextual
   literal/constant rules. Preserve eligible constant forwarding and required
   evaluation dependencies, failure and exhaustion. There is no token-only
   layout evaluator, implicit integer narrowing or runtime fallback.
5. Normalize once to the accepted layout inputs and Blittable commitment.
   Preserve actual field types, ownership, initialization, alignment and
   target dependencies through shared rewriting, mandatory verification and
   lowering. Registration alone establishes none of those facts.

Resolve required layout constants before deriving the layouts that depend on
them, using the ordinary staged evaluator and dependency/cycle rules.
Layout-query authorization and retained dependencies still apply inside an
argument. A layout/constant cycle is not repaired by declaration order,
temporary Auto layout, zero or default controls.

| Selection or lookup condition | Required outcome |
|---|---|
| No layout selection, no affected annotation | Ordinary source remains usable; omitted Layout remains Auto under its existing rules. |
| No selection, unresolved layout-looking annotation | Diagnose unresolved/unsupported annotation or missing required selection; never grant fixed layout by its spelling. |
| A same-named ordinary user schema resolves, with or without a Core selection | It receives no compiler layout role. Apply its independently supported ordinary attribute behavior, or diagnose unsupported user schemas. Do not ignore a malformed/unsupported annotation to produce successful fixed layout. |
| Valid selection, but an unimported or ambiguous name | Ordinary lookup fails; no terminal-name search, provider search or selection-order preference. |
| A local type/schema or enum shadows the registered spelling | Use ordinary lookup's selected declaration or ambiguity. A foreign schema gets no layout role; a foreign enum fails the selected Layout argument type check. Never retry Core after that failure. |
| Qualified or otherwise ordinarily resolved exact selected declarations | Apply the role and typed argument rules regardless of textual path. No new alias-to-attribute syntax is introduced. |
| Duplicate placement through different spellings or direct/type-targeted attachment | Count occurrences on the same resolved target/schema and reject repetition; aliases do not create another role. |
| Invalid, incomplete, stale or foreign-context selection | Reject host admission or affected checked rebinding, without an empty/default successful set or fallback to name recognition. |

The current terminal-kind shorthand has no compatibility exception. It works
only if the ordinary expression actually resolves to a constant of the
selected enum type. Unknown labels, extra positional controls, secondary
authorship and unsupported placement retain their accepted refusals.

## Retention and interchange boundaries

Keep the admitted selection with source binding and any surviving raw enum,
schema or required-evaluation dependencies. Copies/rewrites must retain that
context; caches distinguish its snapshot and all relied-on declarations,
rules and target/evaluation inputs. A new host selection does not relabel an
old value. Checked rebinding/replacement preserves every original expectation,
under [binding and replacement](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities),
not a replacement's current Core map.

The verifier checks any retained registration and its nominal-directory
relationships as well as the existing normalized layout obligations. It must
not require source annotations or Core selection to validate independently
loaded normalized layout declarations. Producers owe truthful schema/layout
facts; consumers retain mandatory structural, support and binding checks,
not a new obligation to semantically re-prove producer bodies.

The standard payloads remain exactly:

- `Layout`: `u32 ownerTypeDef, u32 targetProfileRef?, u8 layoutKind, u8 flags,
  u16 reserved=0, u32 alignment, u32 pack, u32 layoutFieldFirst,
  u32 layoutFieldCount`.
- `LayoutField`: `u32 ownerLayout, u32 ordinal, u32 fieldDef`.

Keep existing kind/Blittable values, omission normalization, complete field
lists, target variants and derived size/offset/alignment semantics unchanged.
No host registration, authorization bit, descriptor argument array or raw
Core identity is serialized into these records or optional Attr/debug data.
Already normalized source-free consumption needs no Core registration solely
because source layout normalization used it. Independently surviving raw
enum/schema/value/evaluation obligations still need their complete admitted
representation; this proposal supplies no schema-publication codec.
Packing/remapping preserves those obligations and rebuilds actual references,
not source names. [Exact draft-schema agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
remains required; no schema selection changes here.

## Alternatives and adoption destinations

| Approach | Assessment |
|---|---|
| **Selected: layout-only set using the existing identity/origin mechanism** | Supplies three exact declarations without unrelated native prerequisites. The bounded schema projection makes the required shape checkable. Existing native API and semantics stay unchanged. |
| Add an optional layout group to `NativeCoreRegistrationSet` | A small-looking field addition still requires three native enums and four native schemas for layout-only users. Removing that coupling requires changing its mandatory native fields/factory contract; it is not the smaller coherent hand-off. |
| Replace the native set with a general optional-family Core catalog | Could consolidate future roles, but requires a broader public API, role-composition and schema model. Sharing identities and one binding snapshot already resolves this task's cross-family consistency; defer that redesign. |
| Add layout cases to `CoreNativeAttributeRole`, or retain name/token decoding | The native schema's argument fields cannot represent this contract. Name/token decoding violates the already accepted identity and constant rules. |

On adoption, the target owner §4 would reference this explicit host-delivery
application; [grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
Core inventory LCI-35 and names/binding remain the semantic owners, with no
production source identity or syntax amendment. The native configuration and
Module-origin proposals retain their authority, snapshot and original-binding
rules; this proposal neither supersedes their native API nor registers other
Core families. Hosted API documentation belongs in
[the compiler reference](../implementation/terrum-code-analysis.md) only after
implementation. No normative owner or support inventory is edited here.

## Dependencies and required conformance cases

The implementation depends on completing the checked host adapter/origin
hand-off required by the existing proposals, reusing shared nominal enum
lookup/evaluation, and integrating ordinary attribute argument mapping.
Current origin descriptors and provider-membership checks do not by themselves
establish actual export resolution or complete original expectation binding.
Do not silently treat that prerequisite as implemented or invent another
resolver here. General source schema declaration/publication is a separate
gate, not permission to guess a schema codec. Full accepted packing/alignment,
partial-declaration placement and downstream layout support remain
`source-layout-packing-alignment` work; this carrier does not establish that
milestone's completion.

The following are acceptance tests to add through public compiler APIs, not
tests run or claims of current support:

| Cases | Required observations |
|---|---|
| Layout-only explicit synthetic provider; multiple schema/enum provider Modules; arbitrary actual provider/export/qualified names | Admit only the selected checked identities, without native configuration, hard-coded namespace or name-derived keys. Inspect the captured enum/selection and normalized layout facts. |
| Native-only configuration; native plus layout against one snapshot; equally populated but distinct snapshots | Existing native behavior remains unchanged; shared snapshot succeeds; conflicting snapshot composition rejects. |
| Options and reference both supply layout selection, including the same object; absent or conflicting using/provider snapshots | Reject duplicate/conflicting configuration before source binding, without first/last precedence. Ordinary normalized references do not implicitly supply registration. |
| Every enum shape fault: wrong kind/base/access/primary/arity/Flags, missing/extra/duplicate-valued case, wrong value/owner/provider/member key | Factory rejects the complete malformed set, even if the currently used enum value appears valid. Also test default arrays, null entries, empty/NUL identities, duplicate keys and missing provider origins. |
| Schema faults: wrong kind/access/primary/arity, repeated placement permission, widened/secondary targets, absent/extra/renamed argument, wrong enum reference/type/default/positional permission | Reject before successful source normalization. Include Layout/Blittable schema identity reuse and default versus initialized-empty argument arrays. |
| No configuration on unaffected source; layout-looking annotation without configuration; same-named user declaration; exact schema with wrong-origin equal-valued enum | Unaffected source stays usable. The others cannot acquire the selected layout/Blittable semantics; unsupported or invalid source diagnoses without fallback. |
| Namespace imports, qualification, ordinary aliases where admitted, local shadows, ambiguous imports and different per-Module enum selections | Preserve ordinary lookup and exact nominal identity. Test multiple trees with different lexical scopes and no process-global name cache. |
| Kind positional/named; reordered named controls; eligible constant forwarding/ConstEval; exact enum values 0–3 and unnamed values | Accepted forms normalize identically; wrong types, unnamed values, unavailable/failed/exhausted required evaluation and token-only shorthand fail under their proper category. |
| Defaults and boundaries: Packing 1/2/4/8/16, Alignment 1 through 8192 at valid powers of two | Exercise omitted versus explicitly supplied controls, zero, nonpowers, out-of-range values and too-weak alignment. Auto/Transparent reject supplied controls even when numerically equivalent to defaults. These are the existing target cases, not new limits. |
| Registered packed/over-aligned nested structs; Union/Transparent; enum Blittable; duplicate/secondary/misplaced attributes | Preserve accepted transitive alignment, eligibility, ownership, primary-only and multiplicity rules; Layout on an enum and these storage schemas on an interface reject. |
| Normalized producer/reference/consumer without source or registration; replacement and row remapping; independently surviving raw enum dependencies | Supported normalized layout consumption succeeds without reacquiring Core, while malformed layout/binding data rejects. Original obligations remain checked; unavailable raw schema codecs decline that separate route. |
| Existing fixed-storage source fixture and declaration/emission/runtime coverage | Supply explicit registrations to positive source fixtures and add absent/shadowed negatives; retain all geometry and safety assertions. Qualify emission and execution separately for changed downstream inputs. |

No additional language-policy choice is left open by this recommendation.
Formal adoption remains unapproved. Concrete authorized provider identities,
revisions, exports and adapter schema selections are required host inputs,
not defaults to invent and not a reason to block provisional implementation
with explicitly configured synthetic producers.
