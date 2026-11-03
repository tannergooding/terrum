# Core provider Module-origin carrier

**Status: unapproved candidate; provisional implementation direction only
under the user's explicit missing-contract workflow.** This is not normative
adoption, an implementation, or a production provider selection.

## Finding and selected decision

[Native Core provider configuration](native-core-provider-configuration.md#host-input-and-adapter-obligations)
already requires the right information: an exact using Module and binding
generation, authorized provider selections, revisions, export keys/kinds and
complete original expectations. The
[registration hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off)
already permits an authorized in-process producer with equivalent real
semantic origins. Neither leaves authority, discovery or fallback policy
undecided. Missing concrete production selections remain host inputs, not
permission to invent them.

The remaining carrier/API decision is how those Module binding facts stay
associated with the existing enum/schema handles through compilation and
publication. The current declaration descriptors alone cannot supply them:

| Inspected implementation | Relevant boundary |
|---|---|
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs) | `CoreDeclarationIdentity` contains provider text, key text and semantic kind. `CoreEnumDefinition` carries enum/member facts and a Blittable target commitment. `NativeCoreRegistrationSet` adds a using-Module label, not a checked Module binding snapshot, selected producer revision/schema or ModuleRef requirements. Member identities do not supply the explicit producer-member-key mapping required by [enum persistence](nominal-enum-persistence.md#logical-identities-and-binding). |
| [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | It **does** expose `BoundRuntimePolicy` for imported checked-failure behavior. It has no complete per-provider Module-origin directory tying enum expectations to producer identity/revision/schema, target/runtime requirements and exports. Its internal enum directory and registration slot do not establish that association. |
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), `CompilationOptions`, `SelectNativeCoreRegistration` | The explicit options hand-off already exists, including duplicate options/reference rejection. Another factory-wiring proposal is unnecessary; shape validation and a using-Module string do not establish the missing context. |
| [`TrRmModuleRefTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmModuleRefTableReader.cs) and [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmModuleRefTableWriter.cs) | ModuleRef carries producer identity and exact target/runtime requirements, owned by a using Module. Co-located producer requirements are checked; external selection still needs binding. Revision and exact schema selection are not ModuleRef fields. |
| [`TrRmImportTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableReader.cs)/[writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmImportTableWriter.cs), [`TrRmExportTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableReader.cs)/[writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExportTableWriter.cs) | Imports retain consumer-local expected definitions; exports name actual producer-local definitions. These structural codecs do not authenticate providers or perform the whole declared-contract comparison. |

Select **one dedicated immutable host binding projection attached to
`NativeCoreRegistrationSet`, reusing ModuleRef/Import/Export semantics**.
Do not require a general redesign of `CompilationReference`, a serialized Core
manifest, or a second resolver. This concretizes the existing proposals'
origin-delivery deferral; it does not replace their identity or comparison
rules.

## Proposed host input and adapter shape

The following are proposed in-process API names and logical fields, not
Terrum syntax or wire records.

Replace the string-only `usingModuleIdentity` input to
`NativeCoreRegistrationSet.Create` with a `CoreProviderModuleBindings` input,
keeping its existing enum/schema inputs. Derive `UsingModuleIdentity` from
that input. Keep `CompilationOptions.NativeCoreRegistration` as the sole
explicit configuration entry point; do not add a competing options property.
This intentionally replaces a provisional API shape: no legacy overload may
manufacture origins, revisions or default requirements.

Candidate public shapes:

```csharp
public readonly record struct CoreProviderSchemaSelection(
    string Identity, uint Revision);

public readonly record struct CoreProviderModuleOrigin(
    string ModuleIdentity,
    string RevisionIdentity,
    CoreProviderSchemaSelection Schema,
    uint GoverningSemanticEdition,
    uint TargetRequirement,
    uint RuntimePolicy);

public sealed class CoreProviderModuleBindings
{
    public string UsingModuleIdentity { get; }
    public ImmutableArray<CoreProviderModuleOrigin> Providers { get; }

    public static CoreProviderModuleBindings Create(
        string usingModuleIdentity,
        ImmutableArray<CoreProviderModuleOrigin> providers);
}
```

`RevisionIdentity` and `Schema.Identity` are nonempty ordinal host-selected
tokens, not ordered versions or decoder hints; `Schema.Revision` is nonzero.
Artifact-backed selections must name a supported exact whole artifact schema.
An in-process selection identifies the checked declaration-contract schema
actually supplied by its authorized adapter; it does not claim that a TRRM
artifact or private module exists. `GoverningSemanticEdition`,
`TargetRequirement` and `RuntimePolicy` use their existing Module domains.
There is no guessed zero/default in place of omitted input.

The existing `CoreEnumMemberDefinition` gains an explicit
`DeclarationKey` property for the producer-assigned EnumMemberDef key. The
Core member handle remains its exact `Identity`; admission validates the
key-to-handle mapping and owner instead of treating `Identity.ExportKey` as
an independent Module export.

| Carrier | Required contents and meaning |
|---|---|
| `CoreProviderModuleBindings` | One using logical Module identity and immutable admitted binding snapshot/generation; its selected dependency closure and applicable rule/target/runtime/evaluation context; selected provider entries and declaration bindings below. The compiler captures this context, not just its display string. Lexical tree/namespace/usings remain separate lookup inputs. |
| `CoreProviderModuleOrigin` entry | A checked selected producer snapshot/handle; its exact logical Module identity, unambiguous selected producer revision, exact schema identity/revision selection, governing semantic requirements, and actual Module `TargetRequirement`/`RuntimePolicy`; the producer's checked logical export map. The schema selection includes the exact supported declaration contract and applicable capability closure. |
| Declaration binding | The existing checked Core declaration handle/identity, one provider entry, the explicitly selected export key and required semantic declaration kind, the actual selected producer declaration, and the complete original consumer expectation graph. Each enum binding additionally maps every actual member handle to its producer-assigned member declaration key and declaration ordinal. |

`CoreProviderModuleBindings.Create` validates the using Module and provider
origin entries and rejects missing or conflicting module selections.
`NativeCoreRegistrationSet.Create` then validates the checked declaration
bindings as one closure, captures them with a compiler-assigned immutable
snapshot token, and requires one agreeing provider origin for every supplied
enum/schema and one member-key mapping for every supplied enum member. Missing,
dangling or competing mappings reject.

The binding snapshot is not a caller-chosen generation string or a serialized
success flag. New selections produce a new generation. Implementations may
share underlying checked objects with ordinary reference binding. Neither
factory authenticates providers or introduces another compatibility relation.

Producer revision, schema identity/revision, governing semantic edition,
Feature revisions and target/runtime requirements are separate selections.
A revision identifies the actual selected producer snapshot; it is not part
of a new nominal type key or an ordering-based compatibility test.
For artifact-backed providers, schema means the explicitly selected complete
artifact schema, including its catalog. For a provider with no artifact,
record the explicit in-process declaration-contract schema/revision instead;
do not claim a nonexistent TRRM header, private revision or metadata row.
Artifact emission separately selects its complete destination schema.
This proposal chooses no production values for these inputs.

`CoreEnumDefinition.BlittableTargetRequirement` is a declaration representation
commitment, not the producer Module's `TargetRequirement`. Preserve both;
neither is inferred from the other or from the enum's integer width.

The adapter supplies real checked declarations and exports from the selected
logical producer. `ProviderIdentity` must agree with that producer, and a
declaration's key/kind must select its actual checked definition before
comparison. Module identities and export keys use
[metadata's ordinal decoded-text rules](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics),
not filename casing, normalization or namespace lookup.

An enum's original expectation is the complete snapshot required by
[nominal enum persistence](nominal-enum-persistence.md#logical-identities-and-binding):
exact nominal owner, effective namespace/name, base, access, primary/generic
facts, Flags fact, representation commitments and the complete ordered
member key/name/ordinal/value list with agreeing owners. Retain all referenced
types, target selections and stronger relied-on facts, not just the members
used by the current expression. Expected declarations are proxies for the
selected producer definitions, not consumer-authored nominal definitions.

Member keys are scoped to the defining enum. The adapter explicitly maps
the current member identities to those keys; it must not derive them from
lookup names, numeric values, ordinals, or string-prefix stripping.
`CoreDeclarationIdentity.ExportKey` on a member is not evidence of an
independent Module export. Standard publication imports/exports the enum
TypeDef, with its enum-owned members; it adds no member Export or DefId kind.
Core semantic kinds likewise are not Standard DefId assignments: an enum
binding projects to an enum TypeDef only under an admitted enum schema.

Schema declarations retain their owning proposal's complete argument,
default, placement, repetition and referenced-enum expectations. A native
role label or the current compact `CoreNativeAttributeSchema` fields do not
stand in for an otherwise missing complete schema. This origin carrier does
not select that schema's source-independent codec.

## Admission and publication

1. The embedding host explicitly authorizes its adapter and provider
   selections. The adapter captures the using context and checked producer
   snapshots before affected source binding. Public descriptors, identities,
   hashes and export-key strings are inputs, **not authentication**.
   The environment establishes authorization; the compiler checks the
   structures, handle/context relationships, support and bindings available
   to it. An artifact cannot authorize its own adapter or Core designation.
2. Admit the complete required registration closure together. Resolve
   provider identity, export key and required kind before comparing complete
   original expectations. Publish no partial successful role or native
   artifact when admission fails. Ordinary lookup, typed constant evaluation
   and native normalization then use the selected declarations under their
   existing contracts.
3. Retain the origin projection with the checked compilation, bound enum
   declarations and every independently surviving declaration/value/type or
   evaluation dependency. Reference construction and bound-tree
   copying/rewriting must preserve it. Passing only `CoreDeclarationIdentity`
   strings or an enum's context-local type number is insufficient.
4. When a source Module publishes a retained imported enum under an explicitly
   selected schema supporting the enum candidate, emit its own expected
   TypeDef and complete enum/member expectation records, an Import selecting
   the real provider export, and a using-Module-owned ModuleRef with that
   provider's exact target/runtime requirements. Do not copy the consumer's
   runtime policy or fill unspecified fields with zero. Do not export the
   expected TypeDef as a locally owned enum. Also check the using Module's
   compatibility with its selected dependencies under explicitly supported
   target/runtime rules; matching the ModuleRef pair alone does not admit
   otherwise unsupported composition.
5. A separately supplied emission Module identity must agree with the
   retained using context. Renaming, packing or rebinding requires a checked
   transformation, not relabeling the program. Co-location keeps provider and
   consumer ownership distinct. Resolving to a direct provider TypeDef may
   remove redundant physical import rows only while preserving every original
   binding and retained obligation.

An authorized in-process adapter may itself produce complete declarations
under its host-selected real logical Module and explicit logical exports,
before source EnumDecl parsing or provider-artifact publication exists.
That is producer responsibility, not a claim that absent artifact rows exist.
It can support in-process binding and a truthful external import expectation;
it does not establish that another tool can independently load the provider.

Conversely, an artifact-backed provider must supply its real definitions and
exports through an independently admitted exact codec. Wrapping missing,
wrong-kind or unsupported serialized declarations in an adapter does not
repair them. A raw serialized consumer still needs an available admitted
provider delivery route; it cannot use the original host's process handles.
Producers owe truthful declarations; consumers do not semantically re-prove
producer bodies to authenticate them.

## Failure, duplicates and ambiguity

Reject invalid submitted configuration at admission; diagnose absent
registration when affected source use requires it. Unconfigured unaffected
source and ordinary inspection remain usable.

| Condition | Required outcome |
|---|---|
| Missing using context, selected producer/revision/schema, requirement/policy, export, original expectation or member-key mapping | Refuse affected binding/publication. Do not manufacture origins from qualified names or fill missing data from the consumer/current Core map. Explicit valid zero requirements remain distinguishable from missing input. |
| Repeated registration for a using Module/role, duplicate provider-selection entries even when identical, or both options/reference registration inputs | Reject configuration. Multiple declarations may reference one provider entry; that is not duplication. |
| Conflicting selected revisions for one producer identity, ambiguous key, wrong kind, wrong owner or one expected declaration mapped to different definitions | Reject without first/last/reference-order preference, overload search or same-shape substitution. |
| Distinct retained expectations for the same provider/key | Preserve and check each; do not overwrite or choose the weakest. Ordinary aliases/import paths to one checked definition may deduplicate identity without deleting those expectations. Distinct producer export aliases to one actual definition remain aliases, not new types. |
| Unauthorized adapter/provider claim or arbitrary public strings | The host refuses unauthorized designation. Descriptor validation cannot authenticate an arbitrary host assertion; artifact assertions and package names cannot activate the adapter path. |
| Stale snapshot, foreign-context handle, mismatched Module requirements, incomplete declaration graph or unsupported schema/capability | Refuse affected use under its actual category. A prior admission result does not repair it. |

No default provider, discovery, runtime lookup/retry, compatibility alias,
integer-to-enum retagging or apparently successful empty registration is added.
Required constant failure/exhaustion and unsupported target lowering remain
distinct from origin/binding failure. Unsafe, ownership and lifetime
obligations are unchanged.

## Replacement and cross-context transformation

Retain every using Module's original selections and expected declaration
graphs, including independent expectations for the same export. Replacement
checks the newly selected producer against those originals and all stronger
facts actually relied on. It does not import the replacement's expectations
as substitutes, rerun source lookup, replay frozen-value construction, or
reload current configuration to relabel old values.

A different producer revision can satisfy the originals through explicit
checked replacement; recording the old revision does not forbid replacement
or require identical body bytes. The bounded enum carrier still requires
its complete snapshot comparison: changed base, member keys/names/order/
values, Flags/access or retained representation facts fail. A same-shaped
declaration under another nominal origin is not a replacement identity.
Caches include the complete binding generation, selected producer snapshots,
schemas, rules, target/runtime/evaluation context and retained dependencies.

Transplanting handles between contexts is invalid. Explicit checked rebinding
may establish the same selected definitions in a new using context, producing
a new context-local mapping while preserving original expectations. Modules
A and B selecting distinct equal-named enums retain distinct raw types/values;
their common source spellings do not create a mapping.

Packing/remapping resolves identities first. Rebuild Module/ModuleRef cursors,
Import/Export references, TypeDef/EnumDef/member and Layout ownership ranges,
TypeSpec/signature/contract/value graphs, Body maps and any retained
SourceFile/SourceMap or registered optional reference slots. Preserve member
keys and declaration order across physical row moves. Clone context-sensitive
shared rows when meanings differ; refuse a destination unable to represent
the retained obligations. Private projections must preserve the same logical
origins and originals under their exact selected carrier; a stricter private
identity grammar cannot be satisfied by renaming the provider. Neither byte
substitution nor equal row/type numbers is a binding transformation.

## Source-free normalization boundary

Already normalized native conventions, resolution facts and declaration roles
use their existing admitted standard/private contracts. A source-free consumer
does not reacquire Core registration, reconstruct schemas/defaults or rerun
source evaluation solely to consume them. No synthetic runtime Core dependency
survives solely because normalization consulted a registered enum.

An independently surviving raw enum declaration, signature/value or evaluation
obligation still requires its complete admitted carrier, original bindings
and applicable dependencies. Removing source or annotations does not erase
that obligation. Serialized readers perform structural, schema-support and
binding checks; they do not deserialize host authorization or infer declarations
from normalized convention bytes. A process-local descriptor is not a codec.
A raw consumer uses ordinary selected Module/Import bindings, not reconstructed
Core registration. Loading an enum does not designate its provider as Core
for new source.

No ModuleRef revision/schema columns, new registry entries, wire version,
private revision or historical reader are proposed here.
[Format versioning](../specs/representation/format-versioning.md) continues to
require exact out-of-band draft agreement for each supplied artifact and an
explicit representable destination for transformations. Equal version bytes,
revision numbers, filenames or plausible common-subset bytes select no decoder.

## Alternatives

| Shape | Assessment |
|---|---|
| **Selected: dedicated host projection on `NativeCoreRegistrationSet`, using ordinary Module binding semantics** | Supplies the exact missing association without requiring executable bodies, a provider artifact, another configuration entry point or a general reference-API redesign. It must use the same checked origin/comparison machinery as references, not duplicate its rules. |
| Enrich `CompilationReference` with a complete general Module binding graph and require it for Core registration | Coherent and reusable for broader separate compilation. The present reference is a declaration projection, however, and Core designation remains a separate host act. Requiring that larger redesign or an artifact-backed reference is unnecessary for the authorized in-process producer. Future reference enrichment may share this carrier without granting Core authority. |
| Reuse only raw ModuleRef rows or current identity strings | Insufficient: no complete original enum/member expectations, selected revision/schema, checked export map or exact using snapshot. Strings and structural rows do not authenticate providers. |
| New serialized Core/provider manifest | Broader than this hand-off. It needs its own schema, authority establishment, references and admission rules, and still cannot replace the actual declaration codecs or authenticate itself. Not selected. |

## Exact owners and adoption boundaries

These are implementation/adoption destinations, not files amended here.

| Owner | Required integration; preserved boundary |
|---|---|
| [Provider configuration](native-core-provider-configuration.md#host-input-and-adapter-obligations), [Core hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off) | Resolve only their concrete origin-carrier/API deferral; preserve their required semantic information, explicit authority, single options input, role/schema contracts and no discovery/default. The proposed API replacement concerns the implemented string-only hand-off, not a competing semantic registration contract. |
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs), [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`BoundEnumTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs) | Replace `Create`'s using-label input with the immutable binding projection; validate/capture context in factories; retain actual origin and original expectations through bound/reference projections. Keep `CoreDeclarationIdentity` a selector, not authentication or a complete Module origin. |
| [`ModuleCompiler.CreateReference`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`TrRmExecutableCompiler.CreateReference` and emission](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`TrRmEnumTables.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmEnumTables.cs) | Preserve origin associations when projecting declarations and publishing imported enum expectations. Existing per-function `BoundModuleImport` facts do not supply enum origins. Enum structural rows do not alone enable source emission or independent loading. |
| [Modules](../specs/modules.md); [metadata, “Metadata logical tables and row semantics”](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Apply existing Module ownership and ModuleRef/Import/Export rules. No row width, target/runtime assignment, identity equality or imported-definition export rule changes. |
| [Binding/replacement §§1, 4, 5, 6](../specs/representation/binding-and-replacement.md#1-scope-and-selected-identities) | Apply identity-before-comparison, unambiguous revision selection, complete contextual graphs, original expectations, replacement/cache and remapping obligations. No second satisfaction relation. |
| [Nominal enum persistence](nominal-enum-persistence.md#logical-identities-and-binding) and its Standard/private candidates | Supply actual provider origins and explicit member-key mappings to their existing complete snapshots. Do not manufacture origins from names, select a provider, alter their schema compositions or claim either carrier implemented. |
| [Requirements/admission](../specs/representation/requirements-and-admission.md), [reader trust](../specs/representation/reader-trust.md), [format versioning](../specs/representation/format-versioning.md) | Preserve independent support, producer truth, mandatory safe-consumption checks and exact schema selection. No serialized authorization or body re-proof requirement. |
| [Binding conformance](../specs/conformance/il-binding-and-replacement.md), [persistence conformance](../specs/conformance/interchange-persistence.md), [host API inventory](../implementation/terrum-code-analysis.md) | On adoption/implementation, add the cases below and document actual supported APIs. Writing this proposal supplies no conformance or execution evidence. |

## Acceptance cases

These are future public-API and independently authored artifact cases, not
checks run by this documentation change.

| Case | Required outcome |
|---|---|
| Explicit authorized in-process producer with no serialized provider artifact; complete enum/member origins and logical exports | Bind exact declarations in the captured using context without pretending artifact rows exist. Publication can retain a truthful external expectation; independent provider loading remains a separate gate. |
| Explicit supported artifact-backed producer, including separately selected enum/schema providers | Decode the selected exact schemas, resolve actual exports/kinds and satisfy the complete coherent registration. No provider is inferred from namespace or role names. |
| Source-free raw enum consumer and independently encoded provider, without source, original registration or process handles | Ordinary admitted ModuleRef/Import binding reconstructs exact enum/member expectations under the selected enum schema, without designating Core. Missing provider codec/export rejects the route. |
| Raw imported enum retained by source Module M | Under the selected enum schema, emit M's expected TypeDef/member snapshot, Import and matching ModuleRef requirements. No consumer-owned enum Export; a co-located provider remains its actual owner. |
| Same enum/member names and values from another Module; integer carrier with matching bits | No origin substitution or retagging. A member-shaped export cannot substitute for the enum TypeDef or its owned member mapping. |
| Missing revision/schema/requirements/member mapping; mismatching target/runtime; partial member snapshot | Refuse affected use, including declaration-only/uncalled roots. Known base width and aggregate `CompilationReference.RuntimePolicy` do not fill the missing facts. |
| Duplicate equal configuration/provider entries, conflicting revisions, ambiguous export or wrong kind/owner | Reject without order preference. Ordinary aliases to one actual definition remain usable and preserve every independently retained expectation. |
| Two original expectations for one export; replacement satisfies only one | Reject. Compatible physical reindexing or unrelated body changes succeed only after all original obligations pass; changed enum/member/representation facts fail. |
| Reuse in another generation or emit under another Module label | Reject transplantation/relabeling. Explicit checked rebinding/remapping to the same definitions may succeed; changed selections do not rewrite old values or expectations. |
| Pack two using Modules with divergent imported meanings and shared structural rows | Preserve each context, clone/remap affected rows and complete ownership/reference graphs, or refuse an unsupported destination. No current-Core-map flattening. |
| Explicit selected schema differs despite equal header/private revision bytes | Refuse unsupported selection without speculative decoding or retry. A host descriptor cannot legitimize missing artifact exports or enum/schema codecs. |
| Remove source and registration after native normalization; then independently retain a raw enum value/signature | Normalized contracts remain consumable without synthetic Core dependency. The surviving raw obligation still needs its admitted carrier and complete bindings. |
| Public string descriptor or serialized claim asserts Core authority | No automatic designation/authentication. The explicitly selected host adapter boundary remains required; consumer structural checks still apply to authorized inputs. |
| A private projection cannot encode an otherwise valid selected origin | Decline that route; do not rename, add an alias, misstate local ownership or silently discard expectations. |

## Remaining inputs and independent gates

The host still must supply the actual authorized authority/adapter, using
Module and checked closure, provider Module identities and selected revisions,
exact supported schema selections, exports/kinds, producer-assigned member
keys, complete declaration expectations and truthful Module/representation
requirements. None is selected by this recommendation.

Implementing the carrier and its retention/binding paths remains work.
Raw Standard/private enum codecs and their consumers, source EnumDecl support,
complete source-independent attribute schemas, further required-evaluation
programs and native lowering retain their existing independent gates. This
proposal neither completes Core registration acceptance nor adds a runtime
provider, fallback or production identity.
