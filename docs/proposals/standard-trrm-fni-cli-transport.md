# Standard TRRM FNI source producer and consumer CLI transport

**Status: unapproved; provisionally actionable under the user-authorized
missing-contract workflow, pending later review.** This selects development
CLI inputs, not normative adoption, approval of dependent schemas, compiler
support or qualification. Preparation is static source/document inspection.
The active no-overlap hold remains: no build, test, profiling or native/process
checks were performed.

## Missing decision and recommendation

Recommend two independently versioned extensions of existing options:

* **Version 2 of `--trrm-source-type-exports <path>`** supplies local FNI
  owner/member keys and eligible aggregate keys. It pairs explicitly with
  AC1, PS1 and the necessary A2 type-provider publication below.
* **Version 4 of `--trrm-source-providers <path>`** binds source destined
  for **AC1 only**, using either one whole FNI Module reference or one
  explicit A2 type reference. Its complete artifact vector preserves each
  artifact's own exact schema.

Both are `compile-trrm` inputs and require `--library`. Neither changes an
option spelling, registers source attributes, supplies semantic facts in
JSON or introduces an artifact codec. Keep consumer selections separate
from producer export assignments. Permit the two new versions together
only for AC1, after each independently passes its agreement.

This fills the AC1 proposal's unselected CLI boundary. Reuse the already
proposed [NativeCore manifest `/2`](terrumc-foreign-native-interface-core-manifest.md)
for authored FNI registration; do not redesign it. NativeOutput and Layout
registration remain independent where authored source needs them. Imported,
normalized FNI declarations do not require their source registration files
merely to reconstruct their contracts or bind calls.

### Proposal-time inspected boundary

At proposal preparation, static source inspection found the following
constraints; these were not executed evidence:

| Owner | Consequence |
|---|---|
| [`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs), [`TrRmSourceProvidersReader`](../../sources/terrumc/TrRmSourceProvidersReader.cs) | Provider files admitted versions 1/2/3 and required every provider identity to equal the output. The type-export option admitted only the nested FieldPath schema. No AC1 source-provider pairing existed. |
| [`TrRmSourceTypeExportsReader`](../../sources/terrumc/TrRmSourceTypeExportsReader.cs), [`TrRmSourceTypeExport`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | The closed version-1 file carried only namespace/name/type key and direct field keys. The API already had `InterfaceMethodExports`, whose member anchors are source-input ordinal and complete declaration `TextSpan`; FNI publication also requires owner arity zero. |
| [FNI reference route](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`CreateGenericMethodCallsDraftReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs) | The three-argument Standard factory projects the selected Module's FNI declarations and aggregate closure. It is not the C1 six-argument method/type/field operation. |
| [`GenericMethodReadContext`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodProviderBinding.cs), [FNI source producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.ForeignNativeInterface.cs) | Per-artifact provider selection required more than a CLI allowlist: the FNI source producer also rejected heterogeneous aggregate origins. |
| [Explicit A2 type factory](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardTypeReference.cs), [reference-set composer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs) | The four-argument factory selects A2 TypeDef roots. The composer rejects FNI inventories; it cannot combine independent FNI/type or several FNI roots. |
| [`BuildTrRmLibraryBindsAc1BodylessPs1ProviderAsync`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmExecution.cs), [FNI/AC1 API regressions](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs) | These requested artifact-command AC1/PS1 use and API production, not `compile-trrm` transport. Some PS1 assertions also requested positive Copy, although its schema owner and [`SupportsDeclaredCopyEvidence`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) exclude that carrier. Neither discrepancy approved a transport or counted as a pass. |

Preserve [source input and coordinates](../specs/language/source-input.md),
[exact draft agreement](../specs/representation/format-versioning.md),
[admission](../specs/representation/requirements-and-admission.md),
[directed binding/replacement](../specs/representation/binding-and-replacement.md)
and [consumer checking](../specs/representation/reader-trust.md). No normative
text, TRRM number, Feature, record, instruction, `(1,0)` pair, governing edition,
target ABI, source syntax or [`terrum.cli/1`](../specs/cli-protocol.md) envelope
is amended.

## Exact agreements and old-version isolation

The names in the first column are explanatory, **not accepted aliases**:

| Agreement | Complete case-sensitive schema identity |
|---|---|
| AC1 | `trrm/1-draft-foreign-native-interface-aggregate-output-consumption-1` |
| PS1 | `trrm/1-draft-foreign-native-interface-parameter-surface-1` |
| A2 | `trrm/1-draft-foreign-invocation-storage-fixed-arrays-2` |

Their complete meanings remain owned by [AC1](trrm-foreign-native-interface-aggregate-output-consumption.md),
[PS1](trrm-foreign-native-interface-parameter-surface-schema-selection.md),
the [FNI owner](native-output-foreign-interface-method-identity.md),
[A2 Copy evidence](standard-trrm-rootless-copy-type-evidence.md) and
[A2 source type identity](standard-trrm-source-copy-type-identity.md).
This transport selects those exact snapshots, not future linked amendments.

Version-4 source-provider pairings are exactly:

| Output | Reference `kind` | Selected root artifact | Every other artifact |
|---|---|---|---|
| AC1 | `foreign-native-interface` | AC1 or PS1 | Independently selected AC1, PS1 or A2 |
| AC1 | `aggregate-types` | A2 | A2 only |

Version-2 type-export pairings are exactly:

| Output | Permitted export entry kinds |
|---|---|
| AC1 | `foreign-native-interface`, `struct`, under AC1's existing declaration/storage scope |
| PS1 | `foreign-native-interface`, `struct`, without AC1 Copy/value-consumption additions |
| A2 | `struct` only; `fieldExports` must be empty |

Require explicit output selection even for empty export maps. A provider
file's version, reference kind or selected root supplies no output default.
PS1/A2 publication is included only to deliver the dependency declarations
this route needs; version 4 adds no PS1 or A2 consumer-output pairing.
Neither table automatically admits F, F+I, A1, A2 successors, A3, L1, C1,
Development, private formats or another schema supported by a wider API.

Preserve the existing agreements exactly:

* [Source-provider version 1](standard-trrm-source-provider-cli-transport.md)
  retains its nested-only pairing, string keys, always-five-argument factory
  dispatch and eligible type-reference composition.
* [Version 2](standard-trrm-c1-source-provider-cli-transport.md) retains its
  closed fifteen-literal C1 table, string keys, four-/five-argument dispatch
  and bounded composition.
* [Version 3](standard-trrm-c1-method-type-source-provider-cli-transport.md),
  including its [six-literal binding-view amendment](standard-trrm-c1-contextual-type-binding-views-cli-pairing.md),
  retains one nonempty method-root selection, Module-qualified type/field
  objects, identical output/provider identities and one six-argument call.
* [Type-export version 1](standard-trrm-source-type-cli-transport.md) retains
  its exact root/entry shapes, nested-only pairing and default interface/enum
  API properties. It gains neither `sources` nor FNI members.

New properties are unknown in old versions; old shapes are not alternate
version-4/2 decoders. Keep enum, generic-method and class publication agreements
independent. The new maps do not pair with them, even when empty.

## Version-2 local export file

The root has exactly `schemaVersion`, `sources`, `exports`. The following
illustrates shape only: the digest/spans are placeholders, not a checked
source fixture.

```json
{
  "schemaVersion": 2,
  "sources": [
    {
      "path": "local-provider.terrum",
      "snapshotSha256": "0000000000000000000000000000000000000000000000000000000000000000"
    }
  ],
  "exports": [
    {
      "kind": "struct",
      "namespace": "Example",
      "name": "Point",
      "exportKey": "data/Point",
      "fieldExports": [
        {"name": "X", "exportKey": "data/Point.X"},
        {"name": "Y", "exportKey": "data/Point.Y"}
      ]
    },
    {
      "kind": "foreign-native-interface",
      "namespace": "Example",
      "name": "IProbe",
      "exportKey": "foreign/IProbe",
      "interfaceMethodExports": [
        {
          "sourceInputOrdinal": 0,
          "declarationStart": 240,
          "declarationLength": 96,
          "exportKey": "foreign/IProbe.TryRead"
        }
      ]
    }
  ]
}
```

All shown properties are required and nonnull. `exports` may be empty.
`sources` has one through 256 entries and asserts the **entire already-selected**
source vector. Each source object has exactly `path`, `snapshotSha256`.
Use the existing [source-map assertion contract](standard-trrm-source-generic-method-export-inputs.md#source-order-and-stale-map-checks):

1. Match count/order to positional source followed by `--source` occurrence
   order. Paths assert those operands; they do not add or read inputs. Resolve
   against the invocation working directory and match with `PathsAlias`.
   `"-"` is permitted only for that ordinal's actual stdin source.
2. The digest is exactly 64 lowercase ASCII hexadecimal digits: SHA-256 of
   the complete captured UTF-16 snapshot, each unit little-endian, without
   BOM, terminator or normalization. Do not hash reread bytes, syntax/semantic
   nodes, concatenated sources or the asserted path.
3. Check count/path assertions before provider/source content acquisition;
   check digests after complete strict source decoding and before binding.
   Preserve CRLF, supplementary pairs and interior U+FEFF. A leading source
   signature is not hashed; its actual current byte-origin map is still
   captured independently.

### Exact entries and API association

`kind` selects one of two closed configuration shapes; it is not a metadata
kind, role certificate or override of the checked declaration:

| Kind | Exact properties beyond `kind` | Existing API handoff |
|---|---|---|
| `struct` | `namespace`, `name`, `exportKey`, `fieldExports` | `TrRmSourceTypeExport` with direct `TrRmSourceFieldExport` entries; leave generic arity at its existing unspecified default and interface/enum/associated properties default. |
| `foreign-native-interface` | `namespace`, `name`, `exportKey`, `interfaceMethodExports` | `TrRmSourceTypeExport` with `GenericArity=0` and a nondefault member array of `TrRmSourceInterfaceMethodExport`; leave field/enum/associated properties default. |

Namespace may be empty; name is a nonempty simple decoded declaration name,
without dots. Resolve its exact namespace/name and asserted kind against the
**one checked `Compilation.Program`**, not using aliases, dependency lookup,
ExportKey parsing or another equal/rebound program. Only public, local,
nongeneric, source-authored eligible declarations qualify. Imported proxies,
loaded/synthetic declarations, scalar/array/alias targets and wrong kinds
reject. Retain the compiler-owned source association before rewriting.

Each field object has exactly `name`, `exportKey`. Names select unique direct
public instance fields of that local owner, not dotted paths or another
owner's equal-named field; field names contain no dot. The list may be empty
or select a subset; layout and private storage closure remain complete
independently. A2's type-only publication permits no nonempty list and creates
no FieldDef export API extension.

Each FNI member object has exactly `sourceInputOrdinal`, `declarationStart`,
`declarationLength`, `exportKey`. The ordinal is zero-based in the captured
source vector; start/length are UTF-16 units. Require nonnegative start,
positive length, checked end within that snapshot, and an exact match to one
direct member's complete declaration `Span`, including the same retained
source ordinal. A name/name-token/body/trivia-inclusive, containing or
overlapping span is not a match. Identical offsets in two inputs are distinct.
The member must belong to this exact owner; source names, overload shapes,
MethodDef rows and slot numbers cannot repair a mismatch.

The FNI member list is nonempty and **complete in direct declaration order**:
exactly one key per member, including uncalled members and their complete
label/default/output/dependency contracts. Map every local FNI owner exactly
once. Map every local nominal type required across the published signature/
layout boundary, including necessary inline children. Required external
children retain their actual owner/key binding; an unexportable required
child refuses publication, not promotion or a generated key.

Fresh assignments select one key per type/field/member, not new aliases.
Reject repeated namespace/name selectors, field selectors, member anchors
and keys. Keys are unique across all these lists and all other exports of
the output Module, including ordinary callable exports. The writer remains
responsible for complete collision/coverage checking. Already retained
artifact aliases retain their owning contract; this file cannot rekey them.

An absent map supplies no fresh keys. An empty map supplies no keys or
implicit coverage. With no local declarations needing assignment, an AC1
consumer can omit the option or use an independently valid empty version-2
map. Neither omission nor emptiness erases retained publication or Imports.

## Version-4 provider file

The root has exactly `schemaVersion`, `providers`, `references`.
`providers` is nonempty; every entry has exactly `schemaIdentity`, `path`.
`references` has **exactly one** entry. This example assumes a valid PS1
foreign declaration whose retained aggregate Imports select the supplied
A2 Module; the JSON does not create that relationship.

```json
{
  "schemaVersion": 4,
  "providers": [
    {
      "schemaIdentity": "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
      "path": "data.trrm"
    },
    {
      "schemaIdentity": "trrm/1-draft-foreign-native-interface-parameter-surface-1",
      "path": "foreign.trrm"
    }
  ],
  "references": [
    {
      "kind": "foreign-native-interface",
      "providerInputOrdinal": 1,
      "moduleIdentity": "example.foreign"
    }
  ]
}
```

`foreign-native-interface` has exactly the three properties shown. It selects
the whole Module's supported exported FNI owners and retained foreign
expected views, with their required aggregate closure. Require at least one
such owner/view; it is not a successful empty/type-only reference.
Source lookup keeps declaration namespace/member names and ordinary overload
rules, not callable renaming by ExportKey. Producer keys and existing Import
keys remain explicit artifact facts.

For an AC1 source producer using separately published A2 types, the other
closed reference shape is:

```json
{
  "schemaVersion": 4,
  "providers": [
    {
      "schemaIdentity": "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
      "path": "data.trrm"
    }
  ],
  "references": [
    {
      "kind": "aggregate-types",
      "providerInputOrdinal": 0,
      "moduleIdentity": "example.data",
      "typeExportKeys": ["data/Coordinates", "data/Point"]
    }
  ]
}
```

`aggregate-types` has exactly `kind`, `providerInputOrdinal`,
`moduleIdentity`, `typeExportKeys`. Its nonempty ordered string list selects
public provider-local A2 Copy struct roots in that Module; repeated keys
reject. Preserve the A2 owner's explicit-alias and closure-only child rules,
including refusal of ambiguous unselected child aliases. No MethodDef,
FieldDef or cross-Module key-root list is added.

There are no `methodExportKeys`, `fieldExportKeys`, Module-qualified key
objects, provider-source recipes, source assertions or optional hybrid shapes
in version 4. A provider's presence cannot expose independent declarations
outside the chosen operation's projection.

### Identity, order and exact dispatch

The ordinal selects one **whole artifact**. The Module must occur in that
artifact, including a non-first co-located Module; absence cannot search
another vector element. Every supplied artifact has one schema for all its
Modules. Co-location cannot mix PS1 and A2 decoders or replace a separate
selection. Duplicate Module identities anywhere in the vector reject,
including equal bytes at distinct paths.

Capture all provider bytes once, in vector order, with their selected literals.
For root ordinal `r`, pass that input as API artifact zero and all other
inputs in original vector order, omitting `r` exactly once. Retain this
ordinal translation in provider/Import/origin associations.

* `foreign-native-interface`: call exactly
  `CreateStandardReference(input, moduleIdentity, providers)`. Its FNI route
  remains the owner of projection, complete identity and binding.
* `aggregate-types`: call exactly the four-argument `CreateStandardReference`
  with the complete submitted type-key list, in order.
* Use the single result directly. Never call the C1 six-argument operation,
  pass empty lists to another overload, compose through
  `CreateStandardReferenceSet`, retry after refusal or relabel the root as
  output AC1 to obtain admission.

One FNI Module can contain several owners and expose supported imported
owners through retained exact Imports. Supply all artifacts for its existing
chains, nested type dependencies, co-located Modules and shared dependencies.
Every reached original view/edge remains an obligation. This does **not**
permit several independent FNI reference roots, FNI plus an independent
A2 reference, C1 callable composition or promotion of disconnected provider
types into lookup roots. Those requests refuse. Cyclic FNI loading or other
closure the current API cannot represent also refuses; order is not a
conflict resolver. In particular, the current FNI reader's refusal of multiple
TypeDef keys for one definition remains a refusal, not first-key selection.
Supported MethodDef aliases and already retained alias equalities remain
obligations; the CLI neither sorts their keys nor invents a new alias policy.

The explicit A2 operation may select several roots in its one Module and
retain external children through their existing provider-owned Imports.
It adds no cross-Module child/root selector. Fields lacking an independently
admitted explicit binding gain no field operation from type selection.
An A2 array/Union/owner or other closure unsupported by AC1 cannot be trimmed
or converted to make source emission succeed.

## Strict input, preflight and publication

Both new files are local regular UTF-8 JSON files: at most one leading
signature, one root object and only trailing JSON whitespace; maximum depth
**128**. Reject malformed UTF-8, unpaired decoded surrogates, escaped-equivalent
duplicate properties at every depth, unknown/missing properties, nulls,
comments, trailing commas/content and wrong types. Object order is immaterial;
every array order is retained. Versions are exactly number tokens `2` and `4`
in their respective files, not fractions, exponents, strings or a shared
version namespace.

Ordinals/start/length are unsigned decimal integer tokens, without signs,
fractions or exponents, bounded by `Int32.MaxValue`; ordinals must be within
their actual vectors. Check cumulative counts, span ends and allocation
products without wrapping or truncating. Retain schema-owned metadata
`1..0xFF_FFFF` row limits and Body-map `u16` capacities, including usable
`FFFF`; configuration indexes are neither kind of serialized reference.
Add no arbitrary new provider-count or analysis budget.

Strings are scalar, NUL-free decoded ordinal text, without trimming,
normalization or case folding. Except empty namespaces, they are nonempty.
Module identities/names/keys must fit the applicable Standard String domain;
keys are **1..`0xFF_FFFF` strict UTF-8 bytes**, opaque and possibly
whitespace-only. They gain no new source call syntax. Digests and kind/schema
tags have their exact grammars above.

Keep both options nonrepeatable, with one operand after the required source
and before any final `--json`. Reject missing/empty/dash-leading operands,
`-`, alternate/equals/short spellings and other commands. Embedded provider
paths also reject dash-leading paths and `-`; use explicit relative/absolute
paths for such filenames. Resolve configuration/provider paths against the
invocation working directory, **not** the manifest directory. No stdin
configuration/provider, URL, environment expansion, glob, include, response
file, installed-provider search or discovery is added.

Required ordering:

1. Preserve argv identity, arithmetic, target requirement **1**, fail-fast,
   `.trrm` output and source-count gates. The outer option/schema gates admit
   only the union of their old tables and the new exact tables; the captured
   version selects the pairing. Check argv-known aliases before content reads.
2. Read selected configuration files once in argv option order. Strictly
   validate complete versions/shapes, library/map combinations, every artifact
   selection, ordinal and source-path assertion before any provider/source
   content, including stdin. No file is reread to select another version.
3. Preserve Core composition, authority and exact `usingModuleIdentity ==
   --identity` checks. NativeCore `/2`, NativeOutput and Layout declarations
   share the existing checked binding snapshot where required. TRRM providers
   grant no Core role; no manifest grants missing artifact/body support.
4. Before acquisition, use existing
   [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs) over every
   actual source/configuration/provider/output path. Require distinct inputs
   across roles and reject duplicate source/provider aliases. Source-path
   assertions are not additional file inputs. Preserve relative/case/link/
   Windows hard-link handling; inspection failure is not distinctness.
5. Check all provider paths, capture owned snapshots in vector order and
   admit **every artifact and every Module**, including disconnected,
   declaration-only and unselected inputs. Validate structural/support,
   edition/Features, target/runtime and complete retained binding closure.
   Root selection is not a filter that discards a failing supplied input.
   Complete the one checked reference before acquiring sources.
6. Capture the positional source and ordered `--source` inputs once under
   `source-input/1`. Retain actual source identities, byte origins and the same
   immutable syntax-tree vector for hashes, binding and all assignments.
   Bind once through `Compilation.CreateLibrary` with that reference and
   existing options, or without a reference when none is selected.
7. Emit the one successful program through the selected `EmitStandard`
   overload, using the checked local type/member mapping when supplied.
   Finish artifact construction and verification before output acquisition.
   Recheck all actual inputs against output immediately before existing
   same-directory staged
   [`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs) replacement.

Every prepublication failure preserves existing output bytes and creates no
absent destination. Retain staging cleanup and no success response on
publication failure. Capture is immutable after acquisition, not an atomic
multi-file transaction, authentication or hostile-filesystem/crash guarantee.

## Required semantic retention, not new JSON authority

PS1 retains its complete FNI/NativeCallable/PS1/SF1/output/dependency meaning.
**PS1 does not acquire `DeclaredCopy`.** A PS1 aggregate declaration alone
cannot authorize AC1 whole-value reading merely because source used an
ordinary struct, its layout matches or Cleanup is absent.

For a PS1 FNI member referring to an A2 type, its retained TypeDef Import must
already select that exact A2 Module/key. Supplying an equal-named/equal-layout
A2 type does not retarget a PS1-local nominal type. Resolve that original
expectation and every inline child first; acquire any Copy promise from an
actual, separately selected supported declaration. AC1 source use must retain
the additional Copy-positive expectation required by the
[AC1 binding owner](trrm-foreign-native-interface-aggregate-output-consumption.md#binding-replacement-and-re-encoding),
without overwriting a weaker PS1 view or dropping original imports.
If the API cannot preserve both, refuse. PS1 publication likewise cannot
erase a retained Copy-positive reference obligation to fit its smaller schema.
This proposal adds no PS1 consumer transport or weakening/projection policy.

A2 dependency publication through version-2 explicit aggregate exports must
carry the full supported Blittable TypeDef/FieldDef/Layout closure and
effective **2/1 and 13/1** in each required owning Module, plus A2's independent
requirements. The additional explicit 13 requirement makes those published
aggregate providers usable under AC1's existing provider-closure check; it is
a producer requirement of this route, not a change to A2's catalog or a claim
that every A2 artifact requires 13. Implement it in normal compiler/API
publication, not by CLI byte patching or fixture-only Feature injection.
An empty map adds no requirement merely by existing.

Retain all of the following through binding, shared traversal/rewriting,
mandatory verification, emission, source-free reconstruction and lowering:

| Retained fact | Required preservation |
|---|---|
| Actual origin and original views | Owned bytes, exact schema per artifact, artifact/Module/definition association, original using contexts, keys, expectations, dependency edges and stronger facts actually relied on. Returning only an actual interface or a bare `BoundModuleImport` is insufficient when original views would disappear. |
| FNI identity | TypeDef/InterfaceDef and complete InterfaceMethod/MethodDef ownership, every member key/Import, bodyless role, ordinal, effective convention, unsafe/no-return and exact receiver/explicit-formal/result correspondence. Slots follow resolved identity, never replace it. |
| Parameter/output surface | Complete PS1 labels and SF1 default bits, absence versus default, output conditions/formals, vacancy/publication and DependsOn/ForwardsBacking identities. Preserve differing original/provider source surfaces; fresh source may observe the selected provider's surface without rewriting completed old calls. |
| Aggregate and fields | Positive Copy where required, exact nominal children and complete field/Layout contracts, including private storage, declared pack/alignment, access and existing FieldDef bindings. A satisfied TypeDef does not manufacture a FieldDef key or private access. |
| Requirements | Per-Module least closure under its own selected schema. AC1 aggregate output consumption retains `{1,2,5,8,10,13,26}`; SF1 defaults add 11 and independent uses retain their own triggers. No requirement follows merely from an option/version, another Module's rows or equal format bytes. |
| Bodies and source data | Distinct Homes, TypeMap/MethodMap/CallSite/reservation indexes, receiver-first mapping and complete aggregate D5/read/return types. Rebuild affected rows/ranges/maps/sections and [`SourceMap`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceMapTableReader.cs) locations with their original UTF-16/source ownership, not matching-byte substitution. |

The producer establishes truthful source/body facts. Consumers perform required
safe decoding, supported-schema/closure and directed binding checks, not
mandatory re-proof of foreign behavior or arbitrary Body outcome dominance.
Optional audits remain separate. Diagnostic
[`SourceFile`](../../sources/Terrum.CodeAnalysis/Modules/TrRmSourceFileTableReader.cs)
paths neither open source files nor supply missing semantic identity.
Source-free consumers need no export/provider JSON, hashes, spans or Core
source manifests after the emitted required contracts are retained.

Keep raw receiver calls unsafe, stable backing and provider/code lifetime
obligations explicit. No IID acquisition, ownership transfer, marshalling,
default output, pin/root, private persistence fallback or sample-specific
compiler policy is supplied. Wider pointer-field, function-pointer-field,
array/Union/owner, default-argument Body, call/control-flow or native forms
still require their independently admitted complete support; otherwise refuse.

## Failures

Retain current human/experimental `--json` response shapes and source-input
diagnostics, not a new machine protocol:

| Failure | Projection and boundary |
|---|---|
| Argv/command/operand, missing/unlisted output schema, forbidden option combination | `TRM2000`, exit 2, before configuration content reads. |
| Known version with wrong output/library/kind pairing, unlisted embedded schema or invalid root/provider schema combination | `TRM2000`, exit 2, after necessary strict configuration decoding and before provider/source content. |
| File/read/strict UTF-8/JSON/version/shape/text/count/ordinal failure; duplicate mapping/selection; stale source assertion | `TRM2001`, exit 2, with file/JSON path where attributable. |
| Attributable explicit root/Module/key/kind/owner or checked source-anchor error in a supplied map | `TRM2001`, exit 2. Translate only identified selection/map failures, including the FNI `sourceTypeExports` handoff, not arbitrary compiler argument/host exceptions. |
| Newly checked configuration/provider/input aliases | `TRM2002`, exit 2; existing source/output argument guards retain `TRM2000`. |
| Syntax/binding/source-use error | Existing source diagnostics, exit 1, with actual source identity and coordinates; no fabricated source span for configuration/artifact failures. |
| Invalid provider metadata, missing serialized dependency, duplicate/conflicting Module snapshot, incompatible original view, unsupported complete declaration/Body/context/closure or existing capacity refusal | `TRM5001`, exit 1, preserving existing explicit resource distinctions. No provider discard, partial reference, weakened schema or retry. |
| Missing necessary publication input, or a valid request whose retained obligations the destination cannot represent | Existing `TRM5001`, exit 1, not implicit keys or success-shaped empty metadata. |
| Output I/O/access failure | Existing `TRM2001`, exit 2; no success response. |

Do not import A3-only failure categories into this route, turn unavailable
memory into malformed JSON, or classify invalid serialized ownership as a
mere user key-selection mistake.

## Integration and acceptance

This proposal remains unapproved and does not change normative contracts. The
user authorized provisional implementation before later formal review; the
active hold still prohibits builds and tests. Current support status is tracked
in [`docs/implementation.md`](../implementation.md). The following table
defines the implementation and acceptance obligations:

| Owner | Required change/gate |
|---|---|
| `sources\terrumc\TrRmSourceTypeExportsReader.cs` | Version 2 now retains source assertions and exact FNI/struct entry shapes. Acceptance must confirm ordinal/span/hash handoff and A2's empty `fieldExports` rule while preserving version 1. |
| `sources\terrumc\TrRmSourceProvidersReader.cs`; `Program.TrRmExecution.cs`; `Program.cs` | Version 4 parsing, AC1-only pairings, per-artifact schema selection and one-call dispatch are implemented. Acceptance must confirm full preflight/output safety and preserve versions 1-3 exactly. |
| `TrRmExecutableCompiler.StandardConsumption.cs`, `TrRmGenericMethodProviderBinding.cs`, `TrRmGenericMethodSourceReader.cs`, `TrRmExecutableCompiler.ForeignNativeInterfaceSourceReader.cs` in `sources\Terrum.CodeAnalysis\Modules` | Bind the selected PS1/A2/AC1 artifacts through the existing FNI factory, retaining complete actual/original owner, member, type views and every supplied Module/edge. No new public selector or reference-set API is selected. |
| `TrRmExecutableCompiler.ForeignNativeInterface.cs`, `TrRmGenericMethodSourceWriter.cs`, `TrRmExecutableCompiler.StandardTypeReference.cs`, `TrRmExecutableCompiler.FixedArrayCopyWriter.cs`, `TrRmExecutableCompiler.cs` in that directory; shared semantic/native owners | Cross-schema source emission now accepts only the selected FNI/A2 origin combinations and publishes A2 aggregate TypeDefs with the required Copy/LayoutQueries facts. Acceptance must verify original expectations, Imports, re-encoding, complete closures and refusal of unrepresentable PS1 Copy obligations. |
| Existing artifact-command handler and native Standard binding | The `build-trrm-library` AC1 root now admits separately selected AC1/PS1/A2 providers. Acceptance must preserve existing grammar/old routes; this transport does not admit AC1 `run-trrm` or `build-trrm`. Native support remains an independent gate. |
| `tests\Terrum.CodeAnalysis.Tests\Cli\CliTests.TrRmExecution.cs`, `CliTests.TrRmSourceProviders.cs`, `CliTests.TrRmGenericMethodExports.cs`; `Semantics\NativeCoreRegistrationTests.cs`; relevant `Modules\TrRm` fixtures | Public API/CLI regressions now cover the new routes and malformed inputs. Run them after the hold is released, retain old-route coverage, and keep PS1 Copy assertions owned by the correct schema. No friend assembly, dummy interface, manual output reflagging or weakened test gate. |
| Proposal/index and implementation owners | When integrated, cross-link this owner and update actual scope/dependencies/gates in the proposal index, CLI/module inventories, conformance gap index and roadmap. Do not report milestone completion from transport or focused tests alone. |

Required cases are future acceptance, **not results of this document**:

| Family | Required observation |
|---|---|
| Source production | CLI-produce a local AC1 FNI/aggregate library with version-2 owner/member/field keys and exact NativeCore `/2`, Output and Layout selection; inspect all keys/kinds/owners, Copy/2/13 and complete PS1 surface. CLI-produce a bodyless PS1 provider without a Copy bit, and an A2 type provider with truthful Copy/aggregate requirement closure. |
| End-to-end AC1 | Produce A2 types, bind one explicit A2 reference while authoring a local AC1 FNI provider, publish that provider, then bind another source consumer through one FNI reference and its full artifact vector. Separately exercise a PS1 method provider with valid pre-existing A2 TypeDef Imports, and AC1 consumption retaining weak PS1 plus positive actual Copy views. No name-based retargeting. |
| Owners and chains | Several local FNI owners, unrelated synthetic interfaces, same-name overloads with distinct full spans, non-first artifact/root Module, co-located Modules, FNI/type chains and shared-leaf diamonds. Check a later incompatible edge and uncalled owner/member. Unsupported independent multi-root/combined-reference requests explicitly refuse. |
| Source identity | Multiple inputs, stdin assertions, CRLF/supplementary/interior-U+FEFF text, identical spans in different inputs, changed unmapped input, reordered sources, stale hashes, cross-owner/body/name/partial spans. Preserve actual source identities and origin maps; never reread/rebind to rescue a map. |
| Contract refusal | Missing/wrong/private/proxy owner/member/type/field key, missing required local/child mapping, invalid alias, changed convention/receiver/slot/signature/output/dependency/layout/pack/alignment, missing Copy on a child or Copy plus Cleanup. Equal layout, PS1-local type plus unrelated A2 input, and producer presence cannot satisfy nominal identity. |
| Parameter surface | Complete uncalled and overloaded declarations, required/output positions, differing retained/provider labels/defaults, exact SF1 unit/scalar/floating/BID values and absence versus default. Unsupported defaults or omission/body lowering refuse rather than become required-only or zero defaults. |
| Supplied set and schema | Every permitted per-artifact pairing and both version-4 kinds; wrong-schema unused provider before content/stdin, corrupt/disconnected/co-located/declaration-only input, missing dependency and duplicate Module identity even for equal bytes. Wrong case, common `(1,0)` bytes, PS1 Copy content and wider profiles never select a decoder. |
| Strict/boundary input | Both exact version tokens versus coerced/unknown versions; zero/two references, empty providers/types, complete possibly empty maps, missing/null/unknown properties and old/new shape mixing. Escaped duplicate properties, invalid UTF-8/surrogates/signatures, depth 128 boundary, signed/exponent/overflowing ordinals/spans, UTF-8 key/count and metadata/Body capacity boundaries. |
| Old agreements | Unchanged provider versions 1/2/3 including all six current v3 pairings, type-export v1, enum/generic/class maps, Core using-Module checks, arithmetic and artifact options. New keys/kinds/source assertions in old files and cross-version/schema combinations reject without fallback. |
| File safety | Working-directory relative paths, case/link/hard-link aliases, duplicate inputs, identity-inspection failure, late output aliasing and publication failure. For each failure family assert code/status, existing-output bytes, absent-output noncreation, staging cleanup and no success response. Preflight must not consume blocked stdin. |
| Source-free transformation | Consume CLI output without source/maps/Core manifests; replace providers with compatible TypeDef/FieldDef/InterfaceDef/InterfaceMethod/MethodDef/Import/PS1/contract row relocation, then re-emit/reload. Preserve every original expectation, alias equality, stronger Copy view, full nominal/layout/Body maps and source locations; reject incompatible replacement including an unused edge. Recompiling source is not this evidence. |
| Native boundary | Separately establish admitted layout, PE/ABI emission and supervised headless execution of success/failure and aggregate-return paths, with actual provider/code lifetime preserved. Cover eligible small carriers, 16-byte and larger aggregates, packing/alignment and exact fallback/payload behavior. DLL load/export lookup or emission alone is not execution. No visible-window/hardware opt-in, new executable/JIT admission or weaker runtime policy is implied. |

### Alternatives and remaining decision

| Alternative | Disposition |
|---|---|
| **Distinct provider v4 plus type-export v2 — recommended** | Two existing options, closed operation shapes and existing public factories. Separates FNI Module projection/mixed artifacts from C1 method selection, and source-anchored publication from consumer binding. |
| Replace/extend v3 intentionally | Permissible before release, not forbidden compatibility work. Unlike its binding-view pairing amendment, FNI changes selector shape, factory/domain and per-artifact agreement. Preserving its exact old behavior would still require a second discriminated operation; v4 exposes that distinction directly. Not selected. |
| Extend type-export v1 in place | Also permissible, but required `sources`/member anchors change the closed grammar and stale-map contract. Version 2 preserves its existing exact behavior without optional-field fallback. |
| Universal mixed producer/consumer graph, new FNI key-root API or callable reference composer | Could enable independent roots and selective owner/member exposure, but needs additional identity/projection/composition decisions absent from current APIs. Unnecessary for the bounded AC1 route; explicitly not selected. |

The complete in-scope recommendation is above; no transport policy is
intentionally left for implementation to invent. **Formal adoption of these
two file agreements and pairings remains the review decision.** Existing
schema/semantic prerequisites, demonstrated API retention gaps and native
qualification remain separate gates, not approval by this document.
