# Explicit CLI inputs for private nominal identity

**Status: unapproved candidate; provisional direction only under the user's
authorized missing-contract workflow.** This documentation change implements
and approves nothing, including the separately unapproved source-enum design.

## Missing decision and recommendation

Recommend **`--private-identity-manifest <path>`**, supplying one strict,
versioned host-input file to `compile-module producer` or `compile-module
consumer`. This settles only the CLI-transport deferral in
[nominal-origin inputs](private-105-nominal-origin-inputs.md#alternatives-and-remaining-decisions).
It includes the minimum public selector-factory boundary needed to deliver
that input without granting the CLI access to compiler internals.
That owner retains identity/key domains, ownership, exports, closure,
original expectations, reference-projection timing and replacement rules.
The [source-enum input proposal](source-nominal-enum-identity-inputs.md)
separately owns source handles and complete producer member-key projection;
transporting its inputs does not approve or broaden that candidate.

The initially reported `CliBuildsMultiSourceSelfHostLibraryAndConsumerAsync`
failure reached `TRM5002` for missing explicit Module/declaration keys.
This candidate specifies their delivery through
[`Program.Modules.cs`](../../sources/terrumc/Program.Modules.cs) and
[`PrivateModuleIdentityInputs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs).
It adds no `.trm` field or support claim; no newly executed test result is
claimed here.

## Command and version-1 grammar

Accept the case-sensitive option once, after required positionals among
existing options and before a final optional `--json`. It consumes exactly
one nonempty path operand; reject `-`, dash-leading operands, equals syntax,
aliases and repetitions. Resolve a regular file against the invocation's
working directory. No environment selection, automatic discovery, includes,
URLs or provider paths inside the manifest. The manifest and source inputs
remain file-backed: preserve `ReadModuleSource`'s rejection of `-` for both
positional and `--source` operands. This changes transport, not source-input
modes.

Every other command, including shared-parser `compile-native` routes,
`link-module` and `run-module`, rejects the option. Existing source/export
limits, producer-only `--corelib`, arithmetic/runtime selectors and output
restrictions remain unchanged.

The option selects this grammar; `schemaVersion` versions only host input,
not an artifact decoder or semantic edition. Use strict UTF-8 with at most
one leading signature, one JSON root object and trailing whitespace only.
Reject comments, trailing commas/data, invalid Unicode, duplicate decoded
property names at every depth, unknown properties and wrong JSON types.
Properties/tags compare ordinally and case-sensitively. Every listed property
is required; arrays may be empty unless stated otherwise. Only
`referenceProducerSha256` admits null.

Use these existing resource precedents, not a new independent count policy:

| Bound | Basis |
|---|---|
| Manifest capture: 4 MiB of original bytes | Reuse `Program.Modules.ReadModuleFile`'s input cap. This newly proposed manifest cap is not an existing layout-manifest limit. |
| JSON `MaxDepth = 128` and strict structural checks | Align with [`LayoutCoreManifestReader.ReadDefinition`](../../sources/terrumc/LayoutCoreManifestReader.cs). |
| Sources: at most 256; ordinary exports: at most 1024; source byte budgets | Preserve `TryReadModuleOptions`, `ReadModuleFile` and `ReadModuleSources`: 4 MiB captured file bytes per source and 4 MiB combined UTF-8 bytes of decoded, signature-stripped source. |
| Assignment/member counts | Use the nominal-input owner's actual declaration/member cardinalities and existing closure/table budgets, plus the manifest byte cap; no additional arbitrary row-count limit. |

| Root property | Exact value |
|---|---|
| `schemaVersion` | Integer token `1`, not a string, fraction or exponent. |
| `usingModuleIdentity` | Explicit logical identity under the nominal-input owner's domain. |
| `sourceSha256` | Ordered array of digest strings, exactly one per file-backed source operand: positional source first, then each `--source` in argv order. |
| `referenceProducerSha256` | Null for producer, which has no positional private artifact; required digest string for consumer's positional producer artifact. |
| `definitions` | Array of owned `Definition` objects. |
| `imports` | Array of `Import` objects under the command-specific provider rules below: producer admits explicitly registered Core enums; consumer selects its positional producer artifact. |
| `enumMembers` | Array of exact actual-Core `EnumMember` assignments described below. |
| `sourceEnumDefinitions` | Array of source-enum assignments described below. |

`Definition` has exactly `kind`, `qualifiedName`, `localDeclarationKey` and
`isExported`; `kind` is `"struct"`, `"class"` or `"enum"`, names/keys are
strings and `isExported` is a JSON Boolean. `Import` has exactly `kind`,
`qualifiedName`, `localDeclarationKey`, `selectedModuleIdentity` and
`selectedExportKey`; it uses the same kind tags and all other fields are strings.
Source-enum assignments have exactly `qualifiedName`, `localDeclarationKey`,
`isExported` and `memberDeclarationKeys`, mapping to the existing
`PrivateSourceEnumDefinitionAssignment`; the last property is an explicit,
complete string array in checked declaration order, including `[]` for an
empty enum. Apply the linked owners' exact key domains and collision rules,
without trimming, normalization or case folding.

`EnumMember` has exactly `owner`, `enumIdentity`, `memberOrdinal`,
`memberIdentity` and `memberDeclarationKey`. `owner` has exactly
`kind: "enum"` and `qualifiedName: string`, selecting a checked enum in the
same program. Each identity object has exactly `providerIdentity: string`,
`exportKey: string` and `kind`; the enum's kind is `"enum-type"` and the
member's is `"enum-member"`. These tags map to `CoreDeclarationKind`,
using [`LayoutCoreManifestReader.ReadIdentity`](../../sources/terrumc/LayoutCoreManifestReader.cs)'s
existing identity spelling, not numeric tokens.
Decode these objects as `CoreDeclarationIdentity`, applying its validity
checks and the nominal owner's additional association rules.
`memberOrdinal` is an unsigned decimal JSON integer token in `0..2147483647`
(no fraction or exponent), further bounded by the actual member count;
`memberDeclarationKey` is an explicit string under the nominal owner's rules.

This carries all four `PrivateModuleIdentityInputs` collections.
Owned `"enum"` definitions select actual-Core-backed
declarations with positive local ownership; source enums use the separate
`sourceEnumDefinitions` path. Apply the nominal owner's
[exact owner/ordinal/identity checks](private-105-nominal-origin-inputs.md#exact-owner-ordinal-and-identity-association)
and complete-group rules. Core identities are explicit assertions of existing
checked declarations, not new declarations, provider registration or authority.
If the CLI lacks an independently supported route to the required checked
Core declarations/bridge/provider snapshot, publication remains gated by
`TRM5002`; the manifest cannot manufacture that prerequisite. Decoded private
enums reuse complete retained member maps without fabricated Core identities.

## Public host API boundary — unapproved

[`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
keeps source declarations, enums and private origins internal;
[`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs)
does not expose those maps either. Public layout rows or Core provider
definitions do not establish their association to this program's nominal
types. The raw public `Create` overloads need `PrimitiveType` inputs for
definitions/imports/Core members; source-enum assignments have their own
qualified-name route. The
[friend declaration](../../sources/Terrum.CodeAnalysis/Properties/AssemblyInfo.cs)
is for tests, not `terrumc`; preserve that boundary.

Recommend one public `PrivateModuleIdentityInputs.CreateFromSelectors`
factory in `Terrum.CodeAnalysis.Modules`, not public bound-table accessors
or a CLI-side resolver. Add these public sealed positional records;
`S` abbreviates `PrivateNominalDeclarationSelector` in this table:

| Record | Constructor fields |
|---|---|
| `PrivateNominalDeclarationSelector` | `PrivateNominalDeclarationKind Kind, string QualifiedName` |
| `PrivateNominalDefinitionSelection` | `S Declaration, string LocalDeclarationKey, bool IsExported` |
| `PrivateNominalImportSelection` | `S Declaration, string LocalDeclarationKey, string SelectedModuleIdentity, string SelectedExportKey` |
| `PrivateEnumMemberSelection` | `S Owner, CoreDeclarationIdentity EnumIdentity, int MemberOrdinal, CoreDeclarationIdentity MemberIdentity, string MemberDeclarationKey` |

`PrivateNominalDeclarationKind` is a public enum with `Struct`, `Class`,
`Enum`, corresponding to the existing JSON tags, not serialized numeric IDs.
Add this factory signature; retain existing records/overloads unchanged:

```csharp
public static PrivateModuleIdentityInputs CreateFromSelectors(
    BoundProgram program,
    string usingModuleIdentity,
    IReadOnlyList<PrivateNominalDefinitionSelection> definitions,
    IReadOnlyList<PrivateNominalImportSelection> imports,
    IReadOnlyList<PrivateEnumMemberSelection> enumMembers,
    IReadOnlyList<PrivateSourceEnumDefinitionAssignment> sourceEnumDefinitions);
```

The CLI validates file/JSON guards, constructs these public selector records
from the manifest, and passes its exact successful `compilation.Program`.
The library resolves all selectors and checks provenance internally, then
constructs the existing definition/import/Core-member records and invokes
the full `Create` factory with all four collections. Source-enum records
reuse its existing checked projection. Copy inputs immutably, reject
null/invalid selectors and conflicting or stale associations, and return no
partial carrier. No Core identity/key is filled from private tables.

This keeps ownership/member/snapshot checks together instead of publishing
raw-index lookup or declaration-enumeration APIs. Do not add
`InternalsVisibleTo`, reflection, guessed type ranges or duplicated CLI
binding logic. It adds no JSON parsing or filesystem API to the library.

## Exact snapshot and declaration binding

A digest is exactly 64 uppercase hexadecimal SHA-256 characters covering
the **captured original file bytes**, including any UTF-8 signature.
Capture each input file once, compare its digest, then decode/bind those
same immutable bytes under
[source-input/1](../specs/language/source-input.md); never reopen or reread
inputs or hash a re-encoding of decoded source. Digests are snapshot guards,
not Module/declaration keys, authentication or byte-identical replacement
requirements. Equal contents do not merge source units or checked handles.

The library factory resolves selectors only after successful ordinary binding
of that invocation's complete source set and selected registration/reference:

1. `qualifiedName` is the complete namespace-qualified declaration name,
   using decoded identifier spellings joined by dots; a global declaration
   uses its name alone. It is a locator in this exact checked program, never
   an identity. No lexical import/alias search, suffix match or short-name
   retry is allowed. Partial parts select one checked composed declaration.
2. Each owned selector must match exactly one positively locally owned
   declaration of the specified kind, using compiler-owned source/checked
   adapter provenance rather than reconstructing ownership from names,
   layouts or submitted Core tokens. Source-enum selection and eligibility
   follow its separate owner. Reject file-local, constructed/qualified-type,
   stale, wrong-kind, absent or ambiguous selections; absence of an import
   marker alone does not establish ownership.
3. Each import must select exactly one checked occurrence with the stated
   name/kind **and** selected Module/export provenance. For producer Core
   imports, require its checked association to the explicitly selected
   registration's actual declaration and provider-binding snapshot, not
   matching identity strings alone. For consumers, check retained reference
   provenance and validate the actual owned export in the positional
   producer bytes. Apply the command-specific scope below; paths, argument
   order, equal layout or similar names cannot substitute. Do not discover
   additional providers or silently switch to a set/graph route.
4. Resolve every `enumMembers.owner` in that same snapshot; require the
   supplied enum identity, selected ordinal's actual member identity,
   declaring owner, provider and explicit member key to agree exactly.
   Never fill identities from the selected declaration, names, ordinals or
   keys. Require a matching nominal type assignment or eligible retained
   origin and the owner's complete member group; reject duplicates, orphan
   rows, wrong owners and sparse groups. Source/private-only identities
   cannot accept Core rows without the owner's independently checked bridge.
5. Map those actual declarations to their canonical `PrimitiveType` values
   internally and capture inputs for this exact program through the existing
   full factory. Each Core row becomes a
   `PrivateEnumMemberAssignment` with the supplied identities, ordinal and
   key, not inferred tokens. Do not accept serialized type-table integers.
   Capture all inputs immutably and preserve checked source/member
   associations through explicit compiler rebasing.

Validate every row, even if later pruned. Reject duplicate type assignments,
owned/imported overlap, repeated owner/ordinal or owner/member identities,
duplicate per-enum member keys, incomplete groups and conflicting retained
origins under the owners' rules. Different members repeat their owner's
selector legitimately. No first/last wins, sparse overlay or rekeying.

## Producer and consumer consequences

The producer explicitly supplies its using Module and local keys; public
visibility does not imply export. `isExported` publishes the same supplied
key, not an alias. Ordinary positional/`--export` function selection and
artifact cardinality remain separate. Required nominal exports and complete
closure must exist before a consumer can import them.

**Producer imports are permitted only for actual checked Core enums from an
explicitly selected Core registration**, currently
`--layout-core-manifest` /
[`LayoutCoreRegistrationSet`](../../sources/Terrum.CodeAnalysis/Semantics/LayoutCoreRegistration.cs).
Require `kind: "enum"`, an exact registered enum provider/export selection
and complete explicit `enumMembers` under the owner's rules. The identity
manifest alone supplies no Core provider. Private imported declarations
still need an explicit checked provider-snapshot route; this CLI producer
has none and must refuse them. Core imports remain imported expectations,
not owned declarations or re-exports. These CLI restrictions do not redefine
embedding APIs supplied with other checked references.

[`PrivateNominalEnumTests.PrivateEnumDirectoryRoundTripsAndBindsWithoutCoreTokens`](../../tests/Terrum.CodeAnalysis.Tests/Modules/PrivateNominalEnumTests.cs)
constructs producer inputs with Core-enum imports and complete member
assignments. That API evidence motivates this route; it is not CLI
qualification or permission to admit arbitrary private imports.

The consumer supplies **its own** using Module, owned-source assignments and
local proxy keys, plus exact selected provider Module/export keys. It cannot
mint missing producer/type/member keys, relabel imports as owned or impersonate
a distinct provider Module. **Every consumer import must resolve through its
positional producer artifact to an actual owned export**; a layout Core
registration is not an alternate provider or fallback for that command.
An imported proxy in the artifact is not an owned export.
`CreateReference` remains provider-scoped: preserve its original expectations
through source binding, then establish new consumer proxy occurrences using
these inputs. Selecting a replacement never overwrites those originals.

If `--layout-core-manifest` is also present, require ordinal equality of its
`usingModuleIdentity` with this manifest's identity **after independent
validation against both domains**. The common value must lie in their
intersection: the nominal owner still requires 1–64 ASCII characters, an
initial letter, then letters/digits/underscore. Equality does not widen that
domain; an equal but nominal-invalid value is `TRM2001`, exit 2, not permission
to normalize punctuation or relax `IsValidModuleIdentity`. Layout-only
identity rules and all declaration/proxy/member-key domains remain unchanged.

Every `Import.selectedModuleIdentity`, including a Core provider, must also
satisfy the nominal owner's same Module-identity domain. For a Core enum it
must equal the actual checked Core identity's provider identity. Thus the
fixture's `sample.layout-core` cannot be a private nominal selected Module;
the broader Core/layout identity domain does not widen private inputs.
Choosing a different label only in the import cannot relabel that provider.
Core providers not selected as private nominal imports keep their existing
registration-domain rules.

[`CliTests.CreateLayoutCoreManifest`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.LayoutCoreManifest.cs)
writes this actual registration identity, not a diagnostic label. The
[self-host fixture](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs)'s
current `sample.self-host` is therefore invalid for combined manifests.
For a combined fixture, explicitly use a valid provider identity such as
`LayoutCoreProvider` consistently in the actual provider origin and Core
identities referring to it, separately from valid using identities such as
`SelfHostProducer` and `SelfHostConsumer`. These are fixture examples, not
compiler defaults. If producer and consumer identities
differ and both invocations select layout configuration, the fixture must
use distinct layout manifest files, each matching its invocation's nominal
manifest. Each layout manifest binds one using Module: provider declarations
may be identical, but one unchanged using-context manifest cannot serve both.

Neither manifest supplies missing fields in the other. Without the new
option, preserve only the
nominal owner's supported origin-free/complete-retained-input routes;
newly required identities still fail. Never derive nominal keys or Module
identity from declaration names, source/output/provider paths, exports,
hashes, ordinals or layout; there are no defaults, aliases, fallback
encodings or retry without configuration.

## Admission, failures and publication

Validate option syntax and paths first. Use the existing `PathsAlias`
relation, including resolved links/available file identities: the manifest
must be distinct from every file source, producer artifact, output and
layout manifest. Failure to establish required file distinctness is an
error. Admit configuration structure before reading
source/provider snapshots; check their guards, bind, call
`CreateFromSelectors`, then invoke the existing inputs-bearing
`CreateProducer`/`CreateConsumer` overload. Retain all independent verifier,
provider, codec and lowering refusals.

| Failure | Existing CLI projection to use |
|---|---|
| Ineligible command, option spelling/arity/repetition | `TRM2000`, exit 2, before input reading. |
| Manifest I/O, UTF-8/JSON/version/limit failure, snapshot mismatch or invalid selector/assignment shape | `TRM2001`, exit 2; isolate configuration parsing/argument failures from artifact catches. |
| Manifest input/output alias or uncheckable path identity | `TRM2002`, exit 2. |
| Source binding failure | Existing located diagnostics, exit 1. |
| Source-file byte-budget exhaustion | Existing module input-limit failure, `TRM5001`, exit 1. |
| Malformed producer bytes; missing required identity/provider, incompatible contract or unsupported publication | Respect existing format/contract distinction: `TRM5001` / `TRM5002`, exit 1. |
| Existing nonconfiguration input/I/O or output publication failure | Existing `TRM5003`, exit 2; report the actual outcome. |

All prepublication failures leave an existing destination byte-for-byte
unchanged and create no artifact when none existed. Generate complete bytes
before the existing staged single-file `WriteArtifact` replacement; never
truncate output during admission or report success after a publication
failure. Keep the existing experimental human/`--json` response contracts;
this is not a `terrum.cli/1` amendment.

There is **no `.trm` wire change**, revision, embedded manifest, digest,
registration cache or extra Binding field. Existing exact private schema
selection and [binding/replacement](../specs/representation/binding-and-replacement.md)
remain authoritative; source-free linking/running/re-emission needs retained
semantic identities, not the original manifest or source files.

## Alternatives, acceptance and remaining decision

Repeated flags for Module identity and individual declaration/import/member
assignments would suffice for a struct-only case, but arbitrary UTF-8 keys,
snapshot guards and complete enum-member arrays require an additional
escaping/grouping grammar. Recommend the single closed manifest instead,
not both transports; a general provider catalog or interchange redesign is
unnecessary.

Future acceptance must cover unrelated multi-source producers/consumers,
unchanged rejection of positional/`--source` `-`, exact captured file bytes
including signature/multibyte bytes, arbitrary keys, explicit unused type
exports, exact Core enum/member identity and ordinal assertions,
wrong/orphan/sparse member groups, and source-enum maps under their separate
candidate.
Add a positive producer Core import/member case through an explicitly
selected layout registration and the public selector factory, using
distinct valid provider/using identities and preserving imported Binding
and the complete member map. Reject missing registration, unchecked or
wrong providers, nominal-invalid Core provider identities, private imports
without a provider route, and consumer attempts to substitute Core
registration for the positional artifact.
Exercise wrong ownership/provider/kind, stale/reordered snapshots,
duplicate/unknown fields, inherited byte/depth/count boundaries,
layout-context disagreement or equal-but-invalid identities, path aliases
and unchanged outputs on every prepublication failure.
Build the real CLI against this public factory without a new friend grant,
and exercise all four collections through that boundary, not only
friend-assembly tests.
Compatible replacement and source-free re-emission must preserve original
expectations without retaining digest requirements. These are future cases,
not results claimed here.

The remaining review decision is adoption of this option, exact version-1
transport and public checked-selector factory. Actual identities, keys and
snapshots remain explicit per-invocation host inputs, not unresolved defaults.
No subsidiary transport choice is deferred; implementation and qualification
remain work.
