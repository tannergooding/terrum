# NativeOutput source/Core registration

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

Later authorized implementation may use this recommendation provisionally.
This document selects only the missing hosted registration boundary; it
does not implement it or constitute normative adoption.

## Decision and scope

Recommend a separate **`NativeOutputCoreRegistrationSet`**, supplied through
`CompilationOptions.NativeOutputCoreRegistration`, sharing the existing exact
`CoreProviderModuleBindings` snapshot with other selected Core registrations.
Its complete closure is one condition enum E and one parameter schema O.
Selecting another native role does not select O.

[NativeOutput cells](aggregate-native-outputs.md#proposed-sourceapi-forms)
remain owner of source syntax, cell eligibility, result correlation,
reservation/publication and dependencies. This proposal fills only that
document's unspecified host API/registration delivery. The
[foreign-interface identity proposal](native-output-foreign-interface-method-identity.md#candidate-source-contract)
continues to require its independently registered ForeignNativeInterface,
exact declaring interface/MethodDef and complete NativeCallable/target ABI.
O supplies none of those facts.

Do not change `docs/specs`, registry IDs, standard/private encodings,
NativeParameter, NativeVTableMethod or runtime/resource ownership. Keep
`NativeCoreRegistrationSet`'s I/C/P and optional NativeExport/
ForeignNativeInterface selections unchanged. The
[CLI manifest v1](native-core-cli-registration.md#exact-version-1-json-grammar)
remains deliberately **I/C/P-only**: no new field, accepted role, default,
version reinterpretation or CLI option is selected here.

## Gap and alternatives

These are current source observations, not qualification results.

| Evidence | Boundary |
|---|---|
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `TryDecodeDirectNativeOutputAttribute` and `IsNativeOutputAttribute` | Recognizes NativeOutput spellings and condition-member text without a distinct registration; it cannot establish O/E identity or ordinary constant forwarding. |
| [`NativeOutputContractValidator.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputContractValidator.cs) | Checks normalized direct-import output facts; it supplies no source schema authority or foreign-interface admission. |
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | Requires three native enums and I/C/P, with optional export and foreign-interface schemas; shared provider snapshots already retain checked declarations and exact exports. |
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), `ValidateCoreRegistrationComposition` | Native, Layout, static-initialization and LazyInitialization selections have independent slots but must share the same snapshot object. |
| [`NativeCoreManifestReader.cs`](../../sources/terrumc/NativeCoreManifestReader.cs) | Reads the exact v1 I/C/P closure, not an output schema or condition enum. |

| Candidate | Assessment |
|---|---|
| Minimal optional extension to `NativeCoreRegistrationSet` | Add optional E/O inputs and properties, requiring both present or both absent. This can preserve manifest v1 and exact identity, but selecting O still requires the unrelated complete I/C/P closure and expands that carrier's authority. |
| **Separate `NativeOutputCoreRegistrationSet` and options slot** | Adds one independently admitted closure and forwarding slot, reusing the provider directory, nominal enums and argument/evaluation machinery. It avoids dummy I/C/P selections and keeps output authority explicit. |

**Choose the separate set**, following the independent
[Layout](source-layout-core-registration.md#candidate-host-api-and-complete-schema-projection)
and [LazyInitialization](lazy-initialization-core-registration.md#chosen-hosted-registration-and-source-selection)
pattern. Separation is of role selection, not a second provider resolver or
constant evaluator.

## Candidate hosted API

These are **unapproved in-process API signatures** in
`Terrum.CodeAnalysis.Semantics`, not source declarations or wire tags.
Reuse `CoreDeclarationIdentity`, `CoreEnumDefinition`,
`CoreNativeAttributeArgument` and `CoreProviderModuleBindings`.

```csharp
public sealed record CoreNativeOutputAttributeSchema(
    CoreDeclarationIdentity Identity,
    string QualifiedName,
    ImmutableArray<CoreNativeAttributeArgument> Arguments,
    string Placement,
    ImmutableHashSet<string> Targets,
    int GenericArity,
    bool IsPublic,
    bool IsPrimary,
    bool IsRepeatable);

public sealed class NativeOutputCoreRegistrationSet
{
    public CoreProviderModuleBindings ModuleBindings { get; }
    public string UsingModuleIdentity { get; }
    public CoreEnumDefinition OutputConditions { get; }
    public CoreNativeOutputAttributeSchema NativeOutput { get; }

    public static NativeOutputCoreRegistrationSet Create(
        CoreProviderModuleBindings moduleBindings,
        CoreEnumDefinition outputConditions,
        CoreNativeOutputAttributeSchema nativeOutput);

    public bool IsValid();
}
```

Construction is private except through `Create`; `UsingModuleIdentity`
derives from `ModuleBindings`. Add the nullable options property
`NativeOutputCoreRegistrationSet? NativeOutputCoreRegistration { get; init; }`.
Forward it through the existing compilation factories, optional reference
configuration and bound-program preservation paths under the same name and
exposure convention as other Core selections. No public reference constructor
or general Core catalog is required.

Add a nullable `CoreNativeOutputAttributeSchema` arm named
`NativeOutputAttributeSchema` and a matching `From` overload to
`CoreProviderDeclarationSnapshot`. Exactly one declaration arm must be
present. The `NativeOutput` set slot selects O's role; introduce no second
configurable role flag or entry in `CoreNativeAttributeRole`.

### Exact selected declarations

| Selection | Complete required shape |
|---|---|
| E: `OutputConditions` | Exact selected `EnumType`, candidate qualified name `Terrum.Interop.NativeOutputCondition`; public, primary, nongeneric, non-Flags, exactly `int32`-backed. Exactly two distinct public members: **`NonZero=0`, `NonNegative=1`**, with no extra case or duplicate-valued alias. |
| O: `NativeOutput` | Exact selected `AttributeSchema`, candidate qualified name `Terrum.Interop.NativeOutput`; `GenericArity=0`, `IsPublic=true`, `IsPrimary=true`, `IsRepeatable=false`; exact `Placement="primary-callable-contract"`. |
| O targets | Set exactly `{"direct-import-parameter", "foreign-interface-method-parameter"}`. These mean explicit eligible formals of the cell proposal's synchronous bodyless direct import or independently admitted foreign-interface method, not arbitrary native-callable methods. |
| O arguments | Initialized array containing exactly one `CoreNativeAttributeArgument`, with the values below. |

| `Name` | `Kind` | `EnumType` | `IsRequired` | `AllowsPositional` | `AllowsNamed` | `Contexts` | `Default` |
|---|---|---|---|---|---|---|---|
| `When` | `Enum` | Exactly E's identity | `true` | `false` | `true` | Exactly O's target set | `null` |

These labels/tags compare ordinally and case-sensitively. Placement restricts
authorship to the callable's primary contract; `IsPrimary` separately describes
the schema definition. Inline annotation and exact `param(name)` selection
target the same explicit formal and share multiplicity. No receiver, result,
field, type-use, secondary contract or untargeted callable placement is added.

This repeats the existing **candidate**, not runnable support:
`[NativeOutput(When: NativeOutputCondition.NonZero)]` on `write ptr<T>`.
There is no default, positional/ordinal overload, ownership/extent argument
or implicit NativeParameter form. Source values `0/1` are not selector or
serialization codes. Preserve the enum's actual representation commitment
under the existing descriptor's supported domain; registration adds no
Blittable commitment or runtime enum ABI.

## Admission, ownership and composition

1. The host authorizes the Core producer adapter and exact selections.
   `Create` validates the **whole** E/O closure against its checked provider
   snapshot before publishing the immutable set. Null required inputs throw
   `ArgumentNullException`; other malformed inputs throw `ArgumentException`.
   `IsValid` applies the same checks. A submitted invalid set cannot be ignored
   merely because output source is unused.
2. Resolve each provider/export key/required kind before comparing the complete
   declaration expectation. E and O may belong to different explicitly selected
   providers in the same snapshot. Require their matching complete exported
   declaration snapshots, unambiguous provider revisions/schema selections and
   exact O.When-to-E binding. Identity components and member keys are nonempty,
   well-formed ordinal text without NUL; qualified names obey ordinary source
   naming rules. Names and equal shapes do not manufacture handles.
3. Each E member is an actual `EnumMember` owned by E, from E's provider,
   with a distinct identity and producer-assigned `DeclarationKey`. Preserve
   declaration order; the value list does not prescribe it. Member keys are
   enum-owned, not independent Module exports. Reject missing/duplicate keys,
   foreign owners, reused identities or conflicting provider/key kinds.
4. Select the output slot once from options or explicit reference configuration.
   Both nonnull inputs reject even when they are the same object; there is no
   precedence or merge. Null selects no output registration. All selected
   native/layout/static/lazy/output sets for the using compilation must share
   the **same `CoreProviderModuleBindings` object**, not merely equal Module
   text or copied origins. Mixed options/reference roles may compose on that
   snapshot.
5. To combine independently assembled contexts, the host must explicitly create
   one checked snapshot and revalidate registrations against it, retaining
   every original expectation. Stale or foreign-generation handles reject.
   Copies/rewrites retain the originating selection; replacement checks those
   original enum/member/schema expectations rather than relabelling old work.
6. O alone registers neither I/C/P, ForeignNativeInterface, Layout,
   NativeParameter nor NativeVTableMethod. Having other declarations in the
   shared directory is not selecting their roles. Conversely, those selections
   do not register O. Unaffected source may omit this set entirely.

Reuse the existing
[host authority and origin rules](native-core-provider-configuration.md#host-input-and-adapter-obligations).
Descriptors do not authenticate a provider; package/source self-assertion is
not authorization. An in-process producer supplies real checked declarations;
artifact-backed declarations still require their independently admitted codec.

## Source binding and failure

Resolve O, E and member expressions through ordinary lexical/module lookup,
accessibility and finite transparent alias expansion. Only the exact registered
definition selects the role. Qualified names and aliases to O/E preserve
identity; local shadows, ambiguous lookup, equal-shaped declarations and
another provider's same-named enum/schema do not. Add no suffix removal,
compatibility spelling, namespace bypass or provider-order fallback.

Map arguments through O's schema and use the ordinary checked constant
evaluator. Require a completed constant of **exact E type**, including
eligible constant/ConstEval forwarding; do not require a direct member token
or member-origin tag on the resulting value. E's value 0 selects NonZero,
and 1 selects NonNegative. Integers, strings, unrelated enums and nominal
wrappers with matching bits do not convert implicitly. Other otherwise legal
E values reject at this schema use, not throughout ordinary enum semantics.
Zero is valid when explicitly supplied, never an omitted `When` default.

Missing O/E selection or checked definition diagnoses affected source use;
malformed submitted registration fails at host construction/admission as above.
Unknown/duplicate/missing source arguments, positional legacy forms, wrong
targets and secondary authorship diagnose without forming output facts.
Lookup ambiguity, failed/exhausted required evaluation and unsupported
representation retain their actual failure categories; none yields a default,
name-based retry or partial successful role. In particular, v1 CLI
configuration alone cannot authorize source NativeOutput.

After exact source admission, normalize once under the cell proposal:
NonZero applies to `int32`/`uint32` results and NonNegative only to `int32`.
Preserve its exact `write ptr<T>` eligibility and known backing/dependency/
unsafe obligations. Foreign outputs still require exact interface/method
identity; a slot, NativeVTableMethod, NativeCallable or interface value is
insufficient. NativeParameter/NativeOutput overlap on one formal remains
invalid. Registration grants no resource adoption, pointee validity or cleanup.

Already normalized output contracts from a supported dependency need no O/E
source registration or reconstructed attributes merely to consume them.
Keep their existing structural/support/binding checks and producer-truth
boundary; do not require consumers to semantically re-prove provider bodies.
Any independently retained raw enum/schema obligation still needs its own
admitted representation. This proposal changes no persistence gate.

## Required tests and remaining inputs

These are future acceptance cases through public compiler APIs and explicit
synthetic authorized providers, not tests run or compiler-support claims.
Use the existing NUnit areas; add no friend assembly or special evaluator.

| Case | Required outcome |
|---|---|
| Complete E/O, including split providers; output-only configuration on unaffected source | Admit the set without I/C/P placeholders; retain exact selected declarations/context. |
| Null, missing snapshot/export, wrong kind/owner/base/value, extra enum member, duplicate member key, altered schema label/default/targets/repetition | Reject complete registration, including malformed unused inputs. |
| Inline and `param(name)` placements; short/qualified names; aliases to O and E; eligible constant/ConstEval forwarding | Produce the same formal-owned facts for `int32` NonZero, `uint32` NonZero and `int32` NonNegative, preserving E identity. |
| Integer 0/1, same-shaped other enum, unknown E value, runtime value, failed/exhausted evaluation; omitted/duplicate/unknown/positional arguments | Refuse use; specifically accept explicit E.NonZero=0 but reject omitted When and `uint32` NonNegative. |
| Local shadows, unresolved/ambiguous names, aliases to another provider; repeated inline/selector annotations and secondary authorship | No output authority, fallback or duplicate target contract. |
| Options/reference duplication; native/Layout/static/lazy/output permutations on one snapshot; equal-text copied or stale snapshots | Reject duplicate supply and mismatched generations; accept independent roles sharing the exact snapshot and retain selections through factories/copies/rewrites. |
| Separately admitted ForeignNativeInterface method with exact owner/member/effective convention, plus O; missing either registration or foreign ABI support | Admit output formation only with the whole independent foreign contract. NativeVTableMethod slot-only and ordinary NativeCallable outputs remain rejected. |
| NativeParameter input on another formal, then overlap on the output formal; unrelated scalar/pointer/aggregate cell types | Preserve independent input contracts and existing cell eligibility; reject overlap, unsafe/backing violations and unique-owner cells. |
| Supported source-free output dependency, reordered definitions and provider replacement; forged normalized role/formal/condition facts | Consume without source Core selection; preserve original identities/expectations and reject incompatible or malformed facts. |
| NativeCore manifest v1 unchanged; attempted output/condition/foreign-interface/NativeParameter/vtable fields | I/C/P behavior remains unchanged; unknown fields reject, and I/C/P-only source NativeOutput reports missing registration. |

Relevant existing coverage is in
[NativeCoreRegistrationTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs),
[DirectNativeImportTests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs),
[NativeVTableCallTests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeVTableCallTests.cs),
[NativeOutputModulePersistenceTests](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs)
and [CLI manifest tests](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.NativeCoreManifest.cs).
Foreign positive cases require that proposal's independent admission gate;
registration alone must not claim foreign dispatch or execution support.

No further design choice remains within this bounded recommendation.
Actual authorized providers, export/member keys, revisions/schema selections
and complete expectations remain mandatory host inputs, not guessed defaults.
CLI output-registration delivery and foreign dispatch/persistence remain
separate scopes; writing this proposal does not approve or implement them.
