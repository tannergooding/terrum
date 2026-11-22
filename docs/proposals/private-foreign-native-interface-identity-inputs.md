# Private foreign-interface identity inputs

**Status: unapproved; provisionally actionable only under the user's authorized
missing-contract workflow.** This recommendation does not formally approve an
API, manifest version or private schema, amend normative specifications, or
establish implementation or execution support. No build or test accompanies it.

## Missing decision, recommendation and owners

The current identity inputs cannot supply
[PF111's owner/member Bindings](private-foreign-native-interface-persistence.md#exact-private-records).
Recommend two complete foreign-interface assignment collections on the existing
snapshot-bound `PrivateModuleIdentityInputs`, transported by the exact new
**private-identity manifest version 2**. Explicitly permit that version on
`compile-native producer|consumer` only when paired with PF111's exact
`--private-schema` selection. Version 1 remains its independently selected
module-only grammar; do not add optional foreign fields to it.

This supplements PF111's checked-interface-ordinal input requirement without
changing its bytes or exact agreement
`terrum/private-semantic-111-foreign-native-interfaces-1`. It intentionally
amends the [v1 CLI proposal](private-module-cli-identity-inputs.md)'s collection
and command boundary **only for v2/PF111**, and PF111's statement that schema
selection supplies no identity-manifest scope extension: the extension comes
from this separate joint direction, not from the schema selector itself.
The [nominal-input owner](private-105-nominal-origin-inputs.md) continues to own
the existing four collections, identity/key domains and shared namespaces.
The [foreign-interface owner](native-output-foreign-interface-method-identity.md)
continues to own declaration eligibility, complete slots and semantic versus
executing providers. Preserve
[private persistence](../specs/representation/persistence.md),
[exact draft agreement](../specs/representation/format-versioning.md) and
[binding/replacement](../specs/representation/binding-and-replacement.md);
no accepted text or Standard TRRM record is replaced.

### Source evidence

These are inspected source boundaries, not validation results:

| Owner | Current boundary |
|---|---|
| [`PrivateModuleIdentityManifestReader.cs`](../../sources/terrumc/PrivateModuleIdentityManifestReader.cs) | Reads the exact v1 eight-property grammar and v2 ten-property grammar; v2 carries complete foreign owner/member assignments while v1 rejects those properties. |
| [`PrivateModuleIdentityInputs.cs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs), [`selectors`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentitySelectors.cs) | Snapshot-bound owner/member assignments and exact syntax/member association now complement the nominal collections. Rebinding retains declaration identity and rebases selected interface ordinals. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs) | Internal interface/member tables carry source provenance, ordered members, foreign layout and native facts. Source member `DeclarationKey` comes from `InterfaceMemberKey`; it is not an explicit private export-key assignment. Owner `ModuleImport` does not supply a complete private using-context/member Binding map. |
| [`Program.Modules.cs`](../../sources/terrumc/Program.Modules.cs) | Reads and hashes the selected v1/v2 source and producer snapshots, enforces v2/PF111 pairing, composes selected Core manifests, and captures the public selector-factory inputs before publication. |
| [`Program.NativeModules.cs`](../../sources/terrumc/Program.NativeModules.cs), [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs) | Native producer/consumer routes now admit v2 only with exact PF111, guard the complete `.trmn` package, pass snapshot-bound identity inputs, and preserve the producer outer identity distinction. |

Implementation of the recommended assignments, v2 CLI transport and Core
registration routes is in progress. The current worktree has not been built or
tested under the active validation hold; this does not change this proposal's
unapproved status.

PF111's schema selector supplies **no Module identity, owner key or member key**.
Adding `"interface"` to the existing nominal kind enum or encoding an interface
as a fake `PrimitiveType` would not complete the missing contract.

## Public snapshot-bound assignments

Recommend these public sealed positional records in
`Terrum.CodeAnalysis.Modules`. These are proposed C# host inputs, not Terrum
syntax, Core identities, wire rows or public bound-table accessors:

```csharp
public sealed record PrivateForeignInterfaceMemberDefinitionAssignment(
    int MemberOrdinal, string LocalDeclarationKey);

public sealed record PrivateForeignInterfaceMemberImportAssignment(
    int MemberOrdinal, string LocalDeclarationKey, string SelectedExportKey);

public sealed record PrivateForeignInterfaceDefinitionAssignment(
    int InterfaceOrdinal, string LocalDeclarationKey, bool IsExported,
    ImmutableArray<PrivateForeignInterfaceMemberDefinitionAssignment> Members);

public sealed record PrivateForeignInterfaceImportAssignment(
    int InterfaceOrdinal, string LocalDeclarationKey,
    string SelectedModuleIdentity, string SelectedExportKey,
    ImmutableArray<PrivateForeignInterfaceMemberImportAssignment> Members);
```

Add immutable `ForeignInterfaceDefinitions` and `ForeignInterfaceImports`
properties, with the corresponding assignment array types, and these
overloads; retain the existing overload signatures:

```csharp
public static PrivateModuleIdentityInputs Create(
    BoundProgram program,
    string usingModuleIdentity,
    IReadOnlyList<PrivateNominalDefinition> definitions,
    IReadOnlyList<PrivateNominalImport> imports,
    IReadOnlyList<PrivateEnumMemberAssignment> enumMembers,
    IReadOnlyList<PrivateSourceEnumDefinitionAssignment> sourceEnumDefinitions,
    IReadOnlyList<PrivateForeignInterfaceDefinitionAssignment> foreignInterfaceDefinitions,
    IReadOnlyList<PrivateForeignInterfaceImportAssignment> foreignInterfaceImports);

public static PrivateModuleIdentityInputs CreateFromSelectors(
    BoundProgram program,
    string usingModuleIdentity,
    IReadOnlyList<PrivateNominalDefinitionSelection> definitions,
    IReadOnlyList<PrivateNominalImportSelection> imports,
    IReadOnlyList<PrivateEnumMemberSelection> enumMembers,
    IReadOnlyList<PrivateSourceEnumDefinitionAssignment> sourceEnumDefinitions,
    IReadOnlyList<PrivateForeignInterfaceDefinitionAssignment> foreignInterfaceDefinitions,
    IReadOnlyList<PrivateForeignInterfaceImportAssignment> foreignInterfaceImports);
```

The selector overload resolves the existing nominal selectors internally and
uses the same foreign capture/validation as `Create`. The CLI submits ordinals
unchanged; it does not inspect internal interface tables. No friend grant,
reflection, filesystem/JSON library API or independent identity registry is
selected. Existing overloads forward empty foreign collections, not inferred
keys or permission to discard a required interface.

### Exact input ordinals and capture

Here an **input/source ordinal** selects the successfully checked input
snapshot, not a persisted directory position:

* `InterfaceOrdinal` is the zero-based index in that exact
  `program.Interfaces`, **including ordinary and imported interfaces**, before
  private canonicalization, closure pruning or provider substitution. It is
  not the index in a foreign-only filter, a source-file ordinal, a source span,
  a TypeDef/ModuleFunction index or an index in another program/reference.
  Current source binding prepends retained reference interfaces, then local
  interface groups in first-source-occurrence order. Capture the actual table
  association, not a separately sorted name catalog.
* Resolve `D = program.Interfaces[InterfaceOrdinal]`. Each member entry n
  requires `MemberOrdinal == n` and `D.Members[n].Ordinal == n`. Its identity
  is that exact member of D; require `Members.Length == D.Members.Length`
  and a positive member count.
  Member arrays must be in complete declaration order. Owner assignment-array
  order is immaterial. Overloads and uncalled/no-output members all count.
* Require PF111 eligibility and positive checked ownership/reference
  provenance. Neither a plausible name, `ModuleImport == null`, nor a copied
  `SourceInputOrdinal` alone proves local ownership. Capture D, every actual
  member, their source/reference associations and complete original expected
  views immutably. The source binder must retain any missing association.
  Source lookup text and the binder's signature-shaped `DeclarationKey`
  remain locators/checking facts, never defaults for submitted private keys.
* Reject negative/out-of-range/wrong-kind ordinals, null/default collections,
  null entries/strings, duplicate owners including definition/import overlap,
  duplicate/sparse/extra/reordered member rows and conflicting retained maps.
  Validate every supplied group even if later pruned. A newly required owner
  needs one complete assignment; an already complete compiler-retained Binding
  graph in the same using context needs no new assignment. Any supplied
  assertion of retained data must itself be complete and agree exactly.
* Associate inputs by the existing exact-program reference check. A second
  compilation of identical bytes is still another snapshot. Compiler-owned
  `Rebind` must map captured declaration/member associations and rebase
  ordinals explicitly while preserving keys and original expectations; it
  must not replay integers against a changed table or search names on failure.

### Binding construction and provider checks

| Assignment | PF111 Binding |
|---|---|
| Owned owner | Kind 1; supplied using Module/local owner key and `IsExported`. Require an eligible locally owned declaration; exported owners must be public. |
| Owned member n | Kind 1; same using Module/export status as owner; member n's supplied local key. Exported owners export **every** declared member. |
| Imported owner | Kind 2, not exported; supplied using Module/local proxy key plus exact `SelectedModuleIdentity`/`SelectedExportKey`. |
| Imported member n | Kind 2, not exported; its supplied local proxy key; provider Module inherited from the owner and its own explicit `SelectedExportKey`. No separate member-provider override. |

Every owned export publishes its supplied local key as its **one canonical
export key**. There is no member `IsExported` override, independent member
retention root, export alias or re-export. These are semantic declarations,
not ordinary name-selected code exports, IAT entries or abstract PE thunks.

Use PF111's ordinal, case-sensitive 1–64-character ASCII Module domain
(initial letter, then letters/digits/underscore) and explicit nonempty
strict-UTF-8 keys of at most 256 bytes, without trimming, normalization or
case folding. Owner/member local keys share the using Module's
**Binding-bearing declaration namespace** with existing nominal assignments;
export keys also cannot collide with its ordinary semantic exports.
Foreign member keys are Module-wide, unlike enum members' separate per-enum
namespace. Apply existing enum-specific rules only to enum assignments.
Never concatenate owner/name/ordinal, use signature hashes, copy Core tokens
or derive keys from source paths, `--export`, layout or digests.

Resolve an imported owner's selected pair to one actual **owned exported**
foreign owner in the supplied checked provider snapshot. Resolve each selected
member export key independently in that same Module; require a distinct owned
exported member of exactly that selected owner at ordinal n. Matching names,
slots or signatures cannot repair a wrong key, owner, provider or ordinal.
Require the complete ordered member correspondence and all PF111 target,
receiver, signature, convention, effect, output and nominal-type-closure
checks, including uncalled declarations.

The factory captures/checks host assertions against the checked snapshot;
publication/binding still owes actual provider resolution and contract
satisfaction. Retained original expectations cannot be overwritten by this
input or a replacement's declarations. Preserve distinct using-context views.
An imported proxy is never an owned export, and unrelated Standard/Core
origins cannot be relabelled private without a separately selected conversion.

## Exact manifest version 2

Keep the existing option spelling and v1 file/JSON guards: one nonempty
file-backed path, no `-`, dash-leading operand, equals form, alias, repeat,
discovery, include, URL or provider path. Capture at most 4 MiB of original
manifest bytes; strict UTF-8 with at most one leading signature, one root
object/trailing whitespace only, `MaxDepth=128`. Reject comments, trailing
commas/data, invalid Unicode, duplicate **decoded** property names at every
depth, unknown/missing properties and wrong JSON types. Comparisons are
ordinal and case-sensitive. Counts share the actual declaration/member and
PF111 budgets; no new per-collection allowance is introduced.

The root has **exactly** these ten required properties:

```text
schemaVersion                 exact unsigned decimal integer token 2
usingModuleIdentity           string
sourceSha256                  ordered array of digest strings
referenceProducerSha256       null for producer; digest string for consumer
definitions                   existing v1 Definition array
imports                       existing v1 Import array
enumMembers                   existing v1 EnumMember array
sourceEnumDefinitions         existing v1 source-enum array
foreignInterfaceDefinitions   ForeignDefinition array
foreignInterfaceImports       ForeignImport array
```

The four existing arrays retain their **exact v1 field sets, kind tags and
producer/consumer rules**, not a new `"interface"` kind. In particular,
consumer `enumMembers` is empty; producer nominal imports remain limited to
actual checked Core enums from independently selected registration.

| Object | Exact required properties |
|---|---|
| `ForeignDefinition` | `interfaceOrdinal`, `localDeclarationKey`, `isExported`, `members` |
| Its member | `memberOrdinal`, `localDeclarationKey` |
| `ForeignImport` | `interfaceOrdinal`, `localDeclarationKey`, `selectedModuleIdentity`, `selectedExportKey`, `members` |
| Its member | `memberOrdinal`, `localDeclarationKey`, `selectedExportKey` |

Ordinals are unsigned decimal JSON integer tokens in `0..2147483647`, further
bounded by the actual checked tables; reject strings, negative tokens including
`-0`, fractions and exponents. Keys/identities are strings and `isExported`
is a JSON Boolean. Only `referenceProducerSha256` admits null. Root arrays may
be empty; each supplied foreign owner requires its nonempty complete member
array. No names, slot/layout facts, source spans or serialized type integers
are accepted as additional foreign fields.

Select the exact property set by `schemaVersion`, never field presence.
An upgraded reader supports the independently defined v1 and v2 schemas;
the v1 grammar rejects version 2. V1 rejects the two foreign properties, even
when empty; v2 requires them both. Reject missing/unknown versions,
mixed shapes and malformed selected input without retry, sparse overlay,
implicit upgrade/downgrade or a compatibility alias.

Changing unpublished v1 in place would be permissible under the
[pre-v1 policy](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
with explicit agreement on that replacement. Prefer v2 because the currently
implemented option selects one closed v1 grammar, and both its input shape
and one command boundary change here. Preserving that explicitly selected
route is a scoped recommendation, not automatic draft compatibility.
No private artifact revision, digest field or PF111 selector is added.

## CLI scope, snapshots and native composition

| Command | V1 identity option | V2 identity option |
|---|---|---|
| `compile-module` producer and consumer | Existing grammar/scope, including independent exact-schema selection. Missing newly required foreign keys still fail. | Requires explicit exact PF111 `--private-schema`; no selection inferred from version or assignments. |
| `compile-native` producer and consumer | Rejected, preserving the existing v1 option boundary. | Explicit new permission only with exact PF111 `--private-schema`. |
| Artifact-only and all other commands | Rejected. | Rejected. |

The native option is admitted only after the ordinary option set establishes
that explicit PF111 selection; option order remains unrestricted within the
existing positional/final-`--json` boundary. A module v2 manifest with absent
or other schema selection is invalid configuration, not an implicit PF111
request. This does **not** widen `--layout-core-manifest`,
`--native-core-manifest` or `--native-output-core-manifest` command scopes.
The manifest grants no registered foreign role, output schema or unsafe
authority. Existing private source commands still lack the native/output
Core-configuration route for owned annotated foreign declarations; the
separate, unapproved [private compilation Core-registration proposal](private-compilation-native-core-registration.md)
addresses that independent gate. This identity manifest cannot manufacture
Core authority or replace those selections.

Producer foreign imports must be empty: neither producer CLI has a positional
private provider. Existing eligible Core enum imports remain as above.
Consumer foreign imports must resolve through the **positional producer**
to its actual owned owner/member exports. No Core-registration fallback,
additional provider discovery, private proxy re-export or implicit set/graph
route is introduced. Richer embedding routes keep their explicitly supplied
checked references and independent scope.

Reuse v1's 64-uppercase-hexadecimal SHA-256 guards. Capture each source once
in positional-then-`--source` argv order, hash **original file bytes including
any UTF-8 signature**, and require exactly one digest per source operand.
Then decode/bind those same immutable bytes. Preserve
file-only sources, the 256-source cap and existing per-source/combined budgets.
Consumer producer guards cover the entire captured positional artifact:
`.trm` for module compilation, **the whole `.trmn` package**, not just its
inner Contract or PE image, for native compilation. Retain the existing
4 MiB semantic and 16 MiB native-package read limits. Compare before exposing
provider declarations, using the same bytes for reference creation and
publication. Digests guard snapshots, not identities, authentication,
compiler/configuration selection or future replacement equality.

The **same v2 transport** serves both commands; the same unchanged file can
serve two producer invocations only when all source guards, actual ordinal
associations and using identity agree. Capture a new API carrier for each
program. Native producer positional `<identity>` must equal the manifest's
using Module identity ordinally; do not relabel either. A native consumer's
outer identity remains its selected producer's identity; its manifest selects
its own distinct semantic using Module. Each foreign import's provider Module
must match the actual inner owner and native producer identity.
Switching a consumer from `.trm` to `.trmn` requires the corresponding complete
producer digest, so identical semantic contents do not generally permit reuse
of an unchanged consumer manifest.

Pass the captured carrier to the existing inputs-bearing
`ModuleCompiler.CreateProducerWithSchema`/`CreateConsumerWithSchema` or
`NativeModuleCompiler.CreateProducerWithSchema`/`CreateConsumerWithSchema`;
create consumer references with the matching `CreateReferenceWithSchema`
before source binding. Preserve independent runtime policy, source/Core
configuration, code-export/image admission and outer-v3 framing. No fictitious
code export satisfies a foreign-only native producer's real-image requirements.
The semantic provider still need not own the code reached by the raw receiver.

Reuse `PathsAlias` checks against every source, producer, output and other
admitted manifest, including failure to establish distinctness. Retain
`TRM2000`/exit 2 for option/scope errors; `TRM2001`/exit 2 for manifest/version,
digest and invalid host-assignment assertions; `TRM2002`/exit 2 for manifest
aliases. Preserve source diagnostics and artifact format/contract/I/O
distinctions (`TRM5001`/`TRM5002`/`TRM5003`). Produce complete bytes before staged
`WriteArtifact`; all prepublication failures preserve the existing destination.
Source-free binding/re-emission retains Bindings, not manifests, source files
or digest checks.

## Remaining decision, implementation gaps and future acceptance

Formal adoption of this joint v2/API/native-option recommendation remains
unapproved. The authorized workflow makes it provisionally actionable; no
in-scope key-generation, ordinal, version or option-scope choice is delegated
to implementation. Core authority remains independently supplied by the
separate NativeCore/NativeOutput manifest options; the identity manifest does
not supply registration. This input amendment neither completes PF111 nor
approves its other dependencies.

The public assignments and rebinding, exact v1/v2 parser, PF111 pairing,
source and package digests, path-identity checks, and private module/native
Core-manifest routes are implemented. An end-to-end CLI regression covering
local/imported FNI identities, NativeOutput, source-free provider binding,
receiver-aware call persistence and both package types is added but remains
unverified under the active hold. Malformed binding cases, remaining set/graph
and runtime integration, and final acceptance remain.

Future focused acceptance, **not checks run here**, must cover:

* Unrelated multi-source interfaces, ordinary-interface ordinal prefixes,
  overloads and complete uncalled members with keys unlike names/signatures;
  owned exported/internal owners and consumer-local definitions.
* Imported owner/member proxies with distinct local/provider keys; wrong
  owner/provider/ordinal, swapped or missing keys, sparse/repeated groups,
  cross-kind/ordinary-export collisions and 256-byte key boundaries.
* Snapshot rejection, guarded source order/signatures and full native-package
  hashes; compatible physical remapping/replacement preserving every original
  view without source files or manifest digest requirements.
* Exact v1/v2 JSON, escaped duplicate properties, integer/UTF-8/depth/byte
  boundaries; both modes of both compile routes, explicit PF111/ordinary-only
  v2, unchanged v1 native refusal, absent/wrong schema and all other scopes.
* Public CLI/API use without friend access, native producer identity mismatch,
  independent missing Core/provider/image refusals, path aliases and unchanged
  outputs on failure. Native execution evidence remains PF111's separate
  supervised qualification, not a consequence of accepting this manifest.
