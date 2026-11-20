# Standard TRRM Construction-2 method/type source-provider CLI transport

**Status: unapproved; provisionally actionable under user-authorized missing-contract workflow**.
This is a development CLI transport candidate, not approval of syntax, schemas,
its dependent proposals or normative specifications. No build, test or runtime
qualification is claimed.

## Missing decision and recommendation

Add **version 3 of `compile-trrm --trrm-source-providers <path>`**, selecting
exactly one combined reference: explicit MethodDef keys from one classless
root Module, plus ordered Module-qualified TypeDef/FieldDef keys across its
complete supplied provider set. Dispatch once to the existing six-argument
[`TrRmExecutableCompiler.CreateStandardReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardMethodTypeReference.cs).
Do not compose callable references through `CreateStandardReferenceSet`.

The [combined API proposal](standard-trrm-c1-method-type-dependency-selection.md)
deliberately leaves this transport undecided. Its provisional implementation,
[`TrRmStandardExportKeySelection`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardExportKeySelection.cs)
and provider-closure work do not select a JSON grammar or CLI/schema pairing.
This document fills only that gap. The separate
[alias-publication proposal's recommendation B](standard-trrm-c1-source-reference-alias-publication.md#exact-sourceapi-and-publication-rules-for-b)
governs new dependencies; this transport neither reinstates blanket
selected-alias equality nor weakens already serialized obligations.

| Inspected owner | Current source boundary, not executed evidence |
|---|---|
| [`TrRmSourceProvidersReader`](../../sources/terrumc/TrRmSourceProvidersReader.cs) | Accepts exact number tokens `1` and `2`; both have closed `providers`/`references` shapes, nonempty string type-key lists and required, possibly empty string field-key lists. No method-root or Module-qualified key entry exists. |
| [`Program.TrRmExecution`](../../sources/terrumc/Program.TrRmExecution.cs) | Version 1 pairs only with local-field-layout-2. Version 2 uses the closed fifteen-literal C1 table. Neither table includes the three Copy-evidence literals below. Version 2 with empty fields calls the four-argument factory; other admitted selections call the five-argument factory, followed by type-reference composition when needed. |
| Combined factory above; [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs) | The six-argument operation captures selections and artifact bytes, requires identical exact schemas, validates the supplied Module directory, and preserves method/type/field provider closure. Type/field selectors can name co-located or external Modules. |
| [`StandardReferenceComposition`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardReferenceComposition.cs) | The classless composer rejects nonempty `Functions`; removing that guard is not a combined-reference contract. |
| Active [source-provider CLI fixture](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.TrRmSourceProviders.cs) | Contains nested version-1 and bounded version-2 class/provider cases, canonical-version/schema preflight and output-preservation checks; no version-3 combined transport. |
| [Public combined-reference regressions](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardClassSourceEmissionTests.cs) | Exercise nested providers, non-root co-located type/field selections, first-list aliases, retained shared MethodDef expectations and compatible type/field row relocation. The [module inventory](../implementation/modules.md) and [gap index](../implementation/conformance-gaps.md) mark this work unverified under the active hold. |

Preserve the [version-1 transport](standard-trrm-source-provider-cli-transport.md)
and [version-2 C1 extension](standard-trrm-c1-source-provider-cli-transport.md)
as separate exact agreements: unchanged shapes, pairings, factory dispatch,
composition and failure behavior. Neither accepts the new properties or key
objects, even with empty arrays. Version 3 is not their successor decoder or
a fallback for them; it adds one explicitly selected alternative.

## Exact pairing and impact boundary

Version 3 requires explicit `--trrm-schema` selection of **exactly one** of
these existing non-EN1 agreements:

| Existing agreement | Complete selected schema identity |
|---|---|
| Construction-2 + Copy evidence | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1` |
| CM1 + Copy evidence | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1` |
| CM1AR1 + Copy evidence | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-associated-relation-constraints-1-initializer-copy-evidence-1` |

These match `IsC1M1MethodTypeSchemaIdentity` and the three corresponding
`StandardGenericMethodSchema` constants in
[`BoundProgram`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs).
Their meanings remain owned by
[Copy evidence](standard-trrm-c1-initializer-copy-evidence.md#exact-schema-scope-and-reused-declaration-facts),
[Construction-2](standard-trrm-c1-construction-runtime-boundary.md),
[CM1](standard-trrm-c1-class-parameter-defaults.md) and
[CM1AR1](standard-trrm-cm1-associated-relation-constraints.md).
This file versions the CLI grammar, not those schemas.

Every artifact, including unused/declaration-only inputs and retained origins,
must explicitly select the **identical full literal** chosen for output before
schema-dependent decoding. Compare ordinally and case-sensitively. Reject
omitted/Development, unamended parents, label/receiver-only siblings, PI1,
EN1/class-layout (including their Copy successors), G1, A2/A3, LayoutQueries,
private formats and every unlisted selection. Do not infer this pairing from
a parent's admission, a version number, suffix, Feature, row, filename or
successful decoder; no promotion, conversion or retry is selected.

Retain the combined operation's bounded domain: public local nongeneric
ordinary top-level methods with supported scalar/unit results and ordinary
by-value scalar/unit or admitted struct parameters; public nongeneric Copy
struct roots with complete rootless Blittable Sequential/Transparent Windows
x64 closure and scalar/unit or nested admitted struct fields. Every supplied
Module must satisfy the factory's classless target-1/fail-fast restrictions.
Unsupported complete declaration/Body/contract closure still refuses. No
dummy class, constructor, native anchor or provider execution is required.

Allocate no TRRM ID, Feature, record, opcode or artifact-schema identity;
change neither `(1,0)`, governing editions nor conditional requirements.
In particular, explicit field bindings keep Feature 13/1 where required;
selecting methods does not itself require construction Feature 25.
No language syntax, lookup rule, library API, Core authority, transfer or
runtime policy is authorized. This adds neither multiple method-root Modules,
callable-set composition, aggregate results/defaults, generics nor another
type/ABI domain. Existing source and emitter eligibility remains independent.

## Complete version-3 manifest

Keep the existing three root property names and artifact vector. Version 3's
`references` array has **exactly one** element, with the five properties shown.
The example assumes the named Modules/exports exist; it is not a qualified
artifact fixture.

```json
{
  "schemaVersion": 3,
  "providers": [
    {
      "schemaIdentity": "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1",
      "path": "leaf.trrm"
    },
    {
      "schemaIdentity": "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-initializer-copy-evidence-1",
      "path": "methods.trrm"
    }
  ],
  "references": [
    {
      "providerInputOrdinal": 1,
      "moduleIdentity": "example.methods",
      "methodExportKeys": ["ReadAlias", "Read"],
      "typeExportKeys": [
        {"moduleIdentity": "example.methods", "exportKey": "type.record.alias"},
        {"moduleIdentity": "example.methods", "exportKey": "type.record"},
        {"moduleIdentity": "example.leaf", "exportKey": "type.leaf"}
      ],
      "fieldExportKeys": [
        {"moduleIdentity": "example.methods", "exportKey": "field.record.data"},
        {"moduleIdentity": "example.leaf", "exportKey": "field.leaf.value"}
      ]
    }
  ]
}
```

All properties are required and nonnull; reject unknown properties at every
level. `providers` is nonempty and each entry has exactly `schemaIdentity`,
`path`. The sole reference has exactly `providerInputOrdinal`,
`moduleIdentity`, `methodExportKeys`, `typeExportKeys`, `fieldExportKeys`.
The method list contains one or more strings; type/field lists may be empty
and contain objects with exactly `moduleIdentity`, `exportKey`. Bare type/field
strings, per-key ordinals, additional roots and type-only version-3 references reject.
Object property order is immaterial; **all array order is preserved**.

* `providerInputOrdinal` selects the whole artifact containing the method
  root; `moduleIdentity` must identify that Module **in that artifact**.
  Do not search another vector element on failure. Method keys select only
  that Module's eligible local MethodDefs, never imported proxies or anchors.
* Type/field entries select their exact declaring Module in the unique
  directory of **all** Modules in the root artifact and every other provider
  artifact. They need no artifact ordinal: duplicate Module identities reject,
  including byte-identical copies at distinct paths. A co-located Module
  requires no second copy of its file. Module/key pairs, not paths, source
  names, equal layouts or numeric rows, select declarations.
* Reject repeated method keys and repeated `(moduleIdentity, exportKey)` pairs
  within or across the type/field lists. A key has one kind in its Module;
  submitting it for another kind rejects, not overload-resolves. Equal text
  in different Modules is not a collision. Distinct keys selecting one actual
  definition remain valid aliases.
* Each selected field must be a public local instance field whose exact
  declaring owner is explicitly selected in the type list in that same
  Module. An imported owner proxy, outer struct root, hidden layout node or
  provider's mere presence supplies no owner/field selection. New nominal
  dependencies needing Imports, including nested children, require explicit
  type keys; existing retained provider-owned Imports keep their own bindings.
  The factory establishes these correspondences, not CLI-side name lookup.

Retain the strict reader rules: one local regular UTF-8 JSON file, at most
one leading signature, one root object and trailing JSON whitespace only;
maximum nesting **128**. Reject malformed UTF-8, unpaired decoded surrogates,
duplicate decoded properties (including escape-equivalent names), comments,
trailing commas/content, missing/null/wrongly typed fields and unsupported
versions. `schemaVersion` is exactly number token `3`, not `3.0`, `3e0`,
`"3"` or another version. Ordinals are unsigned decimal integer tokens
without signs, fractions or exponents, fit `Int32` and are below provider
count. Check counts, cumulative allocations and schema-owned capacities
without wrapping, truncation or partial admission; add no new capacity budget.

Strings are nonempty NUL-free scalar text, compared decoded ordinally without
trimming, normalization or case folding. ExportKeys are opaque, including
whitespace-only keys, and fit the existing Standard String domain,
**1..`0xFF_FFFF` UTF-8 bytes**. They gain no identifier interpretation.
Method keys are callable-view names under the existing reference convention;
a key not spellable by accepted source syntax gains no new call syntax.
Type/field source lookup retains original declaration names and ordinary
access/ambiguity diagnostics.

### Ordered aliases and retained obligations

Pass every selection, in its submitted order, to the combined API; do not
sort, collapse aliases, regroup by provider or select by Export-row order.
Validate **every** key, including nonrepresentative aliases and unused
callable views, before exposing a reference. Under the separate alias owner,
the first explicit type key for each actual TypeDef and independently the
first explicit field key for each actual FieldDef choose **new** dependencies.
All selected method views keep their own exact keys; a call through
`ReadAlias` is not redirected to `Read`. Retention of unused callable views
remains existing policy, not permission to drop a retained Import.

Other validated source-only type/field aliases do not become additional
replacement dependencies merely by appearing here. Conversely, every
pre-existing retained Import preserves its key, original expectation,
using/owner/substitution context, shared-expected-DefId equality and stronger
relied-on facts. Never apply representative selection to those old edges,
merge independent expected methods because their original targets coincide,
or weaken method-signature/type/field-owner correspondence. Refuse an
unrepresentable obligation. Manifest order does not resolve conflicting
revisions or source-name ambiguity, and need not dictate serialized row order.

## CLI admission, capture and dispatch

Retain the nonrepeatable option's spelling and one operand after the required
source and before optional final `--json`:

```text
compile-trrm <source|-> --identity <using-module> --trrm-schema <exact-listed-literal> --trrm-source-providers <path> -o <output.trrm> --runtime fail-fast [--library] [--source <path> ...] [--json]
```

This is illustrative argv shape, not a new parser. Preserve the existing
arithmetic/Core options, identity/output-extension/fail-fast gates and maximum
**256** ordered source inputs. Reject repetition, missing/empty/dash-leading
option operands, `-`, alternate spellings and other commands. `--identity`
names the consumer/output Module, not the method root. Artifact-only
`--trrm-provider`, `--trrm-module` and anchor options remain unavailable here.
Their commands' schema whitelists, including current exclusion of these
Copy-evidence routes, are unchanged.

No local export-map pairing is added. Existing enum/type/generic publication
options keep their exact gates and remain incompatible with version 3,
including empty maps. The separately proposed class-publication option gains
no Copy-evidence pairing or composition here. This input cannot assign local
exports or replace a missing publication contract.

1. **Preflight.** The outer argv schema gate admits only the exact union of
   the three versions' output-schema tables; it cannot choose the manifest
   version. Check argv-known aliases before content reads. Capture selected
   configuration files once in argv option order.
   Strictly decode the selected version's entire shape, then require its exact
   output/provider pairing before any provider or source content, including
   stdin. Never try another version's shape or infer version 3 from an
   otherwise recognizable schema. Preserve Core composition and exact
   using-Module checks; provider presence grants no Core role or missing
   evaluation/phase codec.
2. **Paths.** Resolve the option operand and embedded provider paths against
   the invocation working directory, **not the manifest directory**. Embedded
   paths are local regular-file paths, not `-`, dash-leading paths, stdin,
   URLs, globs, includes or response files; no environment expansion or search.
   Before provider/source reads, use existing
   [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs) checks across
   source, configuration, provider and output paths. Require distinct file
   inputs across all roles, including duplicate provider/source aliases.
   Preserve relative/case/link/Windows hard-link recognition and existing
   source/output argument checks; failed identity inspection is not evidence
   of distinctness.
3. **Provider capture/admission.** Check all provider paths, then capture bytes
   once in vector order into owned snapshots with their explicit schema
   selections. Supply the entire set, including disconnected/unselected
   Modules and declaration-only artifacts. Validate structural/support,
   edition/Features, target/runtime and every retained binding/closure edge,
   not only selected source uses. Duplicate Module identities, missing
   dependencies, unsupported complete closure or conflicting expectations
   reject; never discard an input. Producer truth remains distinct from
   required consumer checking, not mandatory semantic re-proof of Bodies.
4. **Exactly one combined call.** Use the sole reference's selected vector
   element as `input` and its Module as `moduleIdentity`. Supply **all other
   vector elements in original order**, omitting that element exactly once,
   as `providers`. Convert type/field entries one-for-one, in order, to
   `TrRmStandardExportKeySelection`; pass the ordered method strings and both
   lists to the six-argument factory, including explicit empty lists. Use its
   one result directly. Do not loop per declaring Module, call the
   three-/four-/five-argument factories, invoke the reference-set composer,
   retry after failure or infer a missing ExportKey, even if uniquely exported.
5. **Source/emission.** Complete reference admission before acquiring source.
   Capture the positional source followed by repeatable `--source` inputs
   once under `source-input/1`; keep source identities, order, UTF-16 spans
   and the same immutable syntax-tree vector. Bind once through existing
   `Compilation.Create` or `CreateLibrary` with that reference and the
   existing options. Emit the successful checked program with the selected
   exact-schema `EmitStandard` route and existing target. Required origin,
   expectation, field/Layout/FieldPath and Body-map preservation belongs to
   compiler verification/remapping, not JSON reconstruction. Imported
   expectations are not implicitly local exports. Refuse unsupported context
   retention rather than erase it or use another emitter.
6. **Publication.** Construct and verify the complete artifact before output
   acquisition. Recheck every source/configuration/provider versus output
   alias immediately before existing same-directory staged
   [`WriteArtifact`](../../sources/terrumc/Program.Compilation.cs) replacement.
   Every prepublication failure preserves existing destination bytes and
   creates no absent destination. Keep staged-file cleanup and no success
   response on publication failure.

Captures are immutable after acquisition, not an atomic transaction across
changing files, authentication or a hostile-filesystem/crash-durability
protocol. A later invocation captures again and performs normal full checks;
same path, Module/key text or hash does not retarget an old reference.

## Failure contract

Use the existing human/experimental `--json` projections, not a new
[`terrum.cli/1`](../specs/cli-protocol.md) envelope or runtime failure category.
The following defines version 3 without reclassifying version-1/2 failures:

| Failure | Projection and boundary |
|---|---|
| Command/option/operand, missing/unlisted output schema or forbidden map combination | `TRM2000`, exit 2, before configuration reads. |
| Known configuration version with the wrong exact output pairing, or any embedded provider schema differing from output | `TRM2000`, exit 2, after necessary strict configuration decoding, before provider/source content reads. |
| Configuration/provider/source read or strict-decoding failure; malformed/unsupported JSON version, shape, text, count or ordinal; duplicate selection | `TRM2001`, exit 2; identify file and JSON path where attributable. |
| Input-attributable factory argument failure: absent explicitly selected root/type/field Module, missing/wrong-kind key, omitted required new type selection or explicit field-owner selection violation | `TRM2001`, exit 2, identifying the selection/list. Translate only attributable selection arguments, not arbitrary compiler `ArgumentException`s. |
| Newly checked configuration/provider/input path aliases | `TRM2002`, exit 2. Existing source/output argument failures retain `TRM2000`. |
| Source syntax/binding/use failure | Existing source diagnostics, exit 1, with actual source identities/spans. |
| Malformed provider metadata, missing serialized dependency, duplicate/conflicting Module identities, incompatible retained expectation, unsupported schema-owned declaration/Body/context/closure or existing capacity refusal | Existing `TRM5001`, exit 1, preserving existing explicit capacity/resource distinctions. A valid requested selection is not permission to repair invalid metadata. |
| Output I/O/access failure | Existing `TRM2001`, exit 2; no success response. |

Do not fabricate source spans for configuration/artifact failures, classify an
unexpected host failure as malformed input, import A3-only codes, return an
empty reference or report partial success. Unsupported shared TypeDef/FieldDef
equality must refuse, not be repaired by erasing Imports.

## Prerequisites, integration owners and remaining work

The combined API and alias-publication B directions, their exact Copy-evidence
dependencies and complete retained-closure guarantees are prerequisites.
Transport does not approve them. Provisional code already exists; qualification
of that code remains separate. The version-3 reader, pairing and combined CLI
dispatch are unimplemented; API regressions do not close that gate.
Current inventories record co-located provider Imports, deeper chains/diamonds,
multiple using-Module contexts for one expected TypeDef, broader
shared-expectation transformations and full row/map
permutations as open. The loader still requires the whole validated Module-set
TypeDef/method closure to fit its bounded Blittable-struct path. Unsupported
cases must retain the direct API's refusal, not become selected-root filtering
or CLI workarounds. Runtime qualification and broader milestone completion
remain open independently of this transport.

After review/adoption, amend the owners below; **this draft changes none of
them**:

| Exact owner | Integration |
|---|---|
| `sources\terrumc\TrRmSourceProvidersReader.cs` | Add version-discriminated strict version-3 shape/selection storage; leave version-1/2 records and validation behavior intact. No public selector/API addition is needed. |
| `sources\terrumc\Program.TrRmExecution.cs` | Add the explicit three-literal version-3 pairing and one-call six-argument handoff; reuse capture, paths, source binding, diagnostics and staged publication. Preserve old dispatch/composition and artifact-command whitelists. |
| `sources\terrumc\Program.cs` | Document the existing option's new exact version, not a new option spelling or default. |
| `tests\Terrum.CodeAnalysis.Tests\Cli\CliTests.TrRmSourceProviders.cs`; `CliTests.TrRmGenericMethodExports.cs` in that directory | Add the cases below and retain old-route/map-isolation regressions. Use public compiler APIs to construct providers; do not require new provider-publication CLI transport. |
| `sources\Terrum.CodeAnalysis\Modules\TrRmExecutableCompiler.StandardMethodTypeReference.cs`; `TrRmExecutableCompiler.StandardConsumption.cs` and `TrRmExecutableCompiler.StandardClassTypeReferenceWriter.cs` in that directory; `tests\Terrum.CodeAnalysis.Tests\Modules\TrRm\TrRmStandardClassSourceEmissionTests.cs` | Owning combined-selection, closure and remapping prerequisites/regressions. Repair only demonstrated handoff/retention gaps; add no alternate resolver or callable-set policy. |
| [Version 1](standard-trrm-source-provider-cli-transport.md), [version 2](standard-trrm-c1-source-provider-cli-transport.md), [combined API](standard-trrm-c1-method-type-dependency-selection.md), [alias publication](standard-trrm-c1-source-reference-alias-publication.md) | Cross-link this separate transport owner and replace only statements that its decision is still absent. Do not widen their old versions/pairings or duplicate alias semantics. |
| `docs\proposals\README.md`; `docs\implementation.md`; `docs\implementation\cli-protocol.md`; `docs\implementation\modules.md`; `docs\implementation\conformance-gaps.md`; `docs\roadmap.md` | Index the new provisional choice and update actual scope, remaining dependencies and gates when integrated; do not mark broader work complete from a schema whitelist or a focused pass. |
| [Source input](../specs/language/source-input.md); [metadata](../specs/il-metadata.md); [format versioning](../specs/representation/format-versioning.md); [admission](../specs/representation/requirements-and-admission.md); [binding/replacement](../specs/representation/binding-and-replacement.md); [consumer checking](../specs/representation/reader-trust.md); [Windows x64 layout](../specs/targets/windows-x64-data-layout.md) and [calls](../specs/targets/windows-x64-call-interface.md); [CLI protocol](../specs/cli-protocol.md) | Preserve unchanged; no normative amendment, binary allocation, source/runtime rule or machine-envelope change is selected. |

### Required acceptance

These are future implementation checks, not tests run for this document:

| Family | Required cases/outcomes |
|---|---|
| Positive handoff | Each of the three exact agreements; scalar-only methods with empty type/field arrays; nested Copy structs and explicit field access; single/multiple source snapshots; library and already-admitted executable emission. Inspect exact Module/key/kind and nominal signature/owner correspondences, not only exit status or file existence. |
| Module resolution | Method root in a non-first vector element and non-first co-located Module; type/field roots in its own, another co-located and an external Module. Root identity in the wrong artifact rejects without search. Equal key text in different Modules stays distinct; equal source names diagnose ordinary ambiguity. |
| Negative selection/closure | Empty method selection or multiple reference entries; missing/wrong-kind/private/proxy/anchor/static-field selections; wrong declaring owner; absent new child/field key even if uniquely exported. Required retained MethodDef/TypeDef/FieldDef edges, owner binding, Copy, layout/pack/alignment, unsafe/effect/default/label and CM1AR1 context obligations cannot be skipped by an unused selection or earlier successful comparison. |
| Alias order/replacement | Reverse explicit alias order relative to Export rows and inspect the first type/field representative Imports. Calls through two method views retain both own keys and independent expectations. Reject invalid nonrepresentative keys during capture. Source-free replacement may remove/split an omitted source-only alias only if no retained fact needs it; loss of a representative/called key, nominal correspondence or already-shared expected-MethodDef equality rejects. Retain all old provider-owned keys, including uncalled rows. |
| Complete supplied set | Wrong-schema unused provider rejects before content/source/stdin; corrupt, unsupported or dependency-incomplete disconnected/co-located/declaration-only Module rejects after capture. Duplicate Module identity rejects even for identical bytes at distinct paths. Exercise chain/shared-Leaf diamond and a later incompatible edge; unsupported current closure refuses without provider discard. |
| Strict/boundary input | Exact `3` versus noncanonical/unknown version; zero/two references, empty providers/methods, required possibly empty type/field lists, old string versus new object shapes, escaped duplicate properties, nulls/unknown fields, malformed UTF-8/surrogates/signatures, depth 128 boundary, signed/fractional/exponent/out-of-range ordinals, opaque/whitespace keys and checked text/count/map capacities. No truncation or inferred selection. |
| Isolation/cross-feature | Unchanged version-1 nested and version-2 type/class dispatch and compositions; reject all cross-version/schema pairings and new fields in old versions. Keep G1 version-1/version-3 publication maps, enum/type/class publication gates, Core using-Module/context refusals, arithmetic and artifact-command options separate. Source-provider version 3 is unrelated to a publication file's version 3. |
| Snapshots/publication | Relative paths resolve from working directory; relative/case/link/hard-link input/output aliases, duplicate inputs, late output aliasing and identity-inspection failures preserve the required diagnostic/status. Check existing-output bytes, absent-output preservation, staged cleanup and no success response for each failure family; preflight must not consume stdin. Mutations after capture cannot change retained providers or ordered source identities, CRLF/supplementary text or UTF-16 spans. |
| Source-free preservation | Load/re-emit/reload CLI-produced consumers through already-admitted public APIs, without source/manifests; preserve original views, selected representative/method keys, shared expectations, owner/Layout/FieldPath/Body maps and source locations under compatible row relocation, and reject incompatible replacement. Recompiling source is not replacement evidence. Emission/loading establishes neither JIT/AOT execution nor safe live replacement. |

### Alternatives and remaining decision

| Alternative | Disposition |
|---|---|
| **Existing option, explicit version 3, one `references` entry mirroring the combined API** | **Recommended.** Reuses capture/preflight and provider vectors; explicit version separates new key shapes and method selection without a second option, extra per-type ordinals or a callable composition contract. |
| Separate `--trrm-source-method-providers` option with this shape and exact version `1` | Viable stronger option isolation, but duplicates an externally visible consumer-input mechanism and all path/preflight rules for the same provider vector. Not selected. |
| Version 3 with a singular `reference` object or a new operation discriminator | Removes a one-element array or enables multiple operation kinds, but adds another root shape/discriminator without a capability needed for this bounded handoff. Not selected. |
| Widen version 1/2, accept optional method fields or infer Copy pairings from parents | Possible intentional draft redesign, not prohibited by unpublished compatibility; rejected here because it conflates different selector shapes, schema tables and dispatch. The older type-root contracts are deliberately preserved. |
| Several callable roots composed after projection, per-key artifact ordinals, or embedding selections in export maps | Adds unresolved callable composition or redundant revision selection, or conflates consumer dependencies with local publication. Not required and not selected. |

**Remaining formal decision:** accept or reject the explicit version-3 shape,
three-schema pairing and one-call dispatch, contingent on the combined API
and alias-publication B directions. Under the supplied missing-contract
authorization this recommendation is provisional direction, not approval.
No transport policy is intentionally left for implementation to invent.
Multiple method-root Modules, wider schema/map pairings, additional publication
transport and runtime/native admission remain separate decisions, not defaults.
