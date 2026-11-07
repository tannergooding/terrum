# Source layout registration for private semantic-module compilation

| Dimension | Status |
|---|---|
| Approval | **Unapproved candidate**, provisionally actionable only under the user's authorized missing-contract workflow; later user review remains required. |
| Implementation | The existing host carrier and manifest are provisionally implemented for private producer/consumer source compilation; the exact candidate remains unapproved and has no stability promise. The Debug fast build and focused module/private-CLI selection passed 182/182 with zero skips, covering producer/consumer source binding, configuration-before-source failures, input/output alias rejection and native/source-free route rejection. |
| Stability | Unpublished CLI/configuration/private-persistence direction, not a released compatibility promise or normative adoption. |

## Missing decision and recommendation

Extend the existing **`--layout-core-manifest <path>` version-1 input only to
`compile-module producer` and `compile-module consumer`**. Reuse its closed,
host-authorized declaration producer and checked `LayoutCoreRegistrationSet`;
do not introduce another manifest grammar, registration channel, private
payload field or provider-selection mechanism.

The gap is command delivery, not source admission or layout semantics:

| Inspected evidence | Consequence |
|---|---|
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), `SelectLayoutCoreRegistration`, and [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `DeclareStruct` | Exact host selection is required. Unregistered Layout/Blittable uses diagnose, including `TRM3005`; source names and matching enum bits cannot grant these roles. |
| [`Program.Modules.cs`](../../sources/terrumc/Program.Modules.cs), `RunModuleCommand` | Both private producer and consumer commands bind source through options-bearing compilation factories, but their options currently carry no layout selection. Linking and running read artifacts only. |
| [`CliTests.LayoutCoreManifest.cs`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.LayoutCoreManifest.cs) and [`ModuleTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs) | The private-module extension is provisionally exercised with real source files for producer and consumer compilation, strict configuration errors, path aliases, and unchanged native/source-free exclusions. The former private producer rejection case is replaced by positive routing coverage; `compile-native` remains rejected. |
| [`ModuleTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs), `CliBuildsMultiSourceSelfHostLibraryAndConsumerAsync` | The producer compiles `SourceRange` plus `Token` or `CharacterFacts`. `SourceRange` and `Token` author Layout/Blittable annotations, so this source compilation needs an explicit selection. The consumer can use their normalized exported layouts without inheriting that selection. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `CreateReference`, `CreateProducer`, `CreateConsumer`, `ReadModuleHeader` | The private route has no publication-Module identity argument or persisted Core-registration directory. References expose checked exported functions/layouts. The current reader selects exact private semantic revision 104; surviving nominal enum declarations/values remain unsupported. |

### Alternatives and narrow supersession

| Choice | Assessment |
|---|---|
| **Recommended: minimal route amendment to the existing manifest proposal** | Both commands already invoke the same source binder and can deliver the same options property. One grammar, authority rule and snapshot mechanism suffice. Define private using-context and reference behavior below without changing bytes. |
| Separate private-module source-registration decision with a distinct CLI input/grammar | Could keep the earlier command table unchanged and independently define private admission, using-context and reference composition. With the same complete declarations and no new retained semantics, it duplicates configuration ownership without adding capability. Persisting raw schemas/enums would still need a separately selected codec; a second switch cannot supply it. |
| Infer registration from source names, `--corelib`, exported layouts or a colocated manifest | Rejected: weakens the accepted identity/authority boundary and silently selects missing host inputs. |

This candidate intentionally supersedes **only** the exclusion of private
`compile-module producer`/`consumer` in
[source-layout CLI registration, “Exact command-line contract”](source-layout-cli-registration.md#exact-command-line-contract),
and the corresponding private-module rejection expectation. That document
remains the sole owner of the version-1 JSON grammar and closed producer.
All its other exclusions and obligations remain unchanged.

Preserve the accepted owners:

- [Windows x64 layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
  owns schema shapes, constants, placement, omission, geometry, access and
  overflow. [Attribute grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations)
  and [ordinary lookup](../specs/language/names-and-binding.md#lexical-lookup-and-imports)
  remain source authorities.
- [Core identity/authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
  [Module semantics](../specs/modules.md),
  [binding/replacement](../specs/representation/binding-and-replacement.md),
  and [consumer verification](../specs/representation/reader-trust.md) retain
  their existing ownership and producer/consumer obligations.
- [Private persistence](../specs/representation/persistence.md) and
  [exact schema selection](../specs/representation/format-versioning.md)
  remain distinct from CLI configuration.
  [Source-layout registration](source-layout-core-registration.md) and the
  [Module-origin carrier](core-provider-module-origin-carrier.md) retain the
  host API, immutable snapshots and complete original expectations.

## Exact routes and option contract

| Command form | Manifest behavior under this candidate |
|---|---|
| `compile-module producer <source> <export> ... -o <artifact.trm>` | Newly accepts one manifest for this source library compilation, including every `--source` tree and the existing producer-only `--corelib` form. Existing export restrictions still apply. |
| `compile-module consumer <source> <producer.trm> ... -o <artifact.trm>` | Newly accepts one manifest for the consumer's own source compilation, alongside the independently selected producer reference. |
| `analyze`, `build`, `build-native-import`, `build-library`, `jit`, `compile-trrm` including `--library` | Existing eligibility is unchanged. `compile-trrm` still requires ordinal agreement with its existing `--identity` and independently selects its output schema. |
| `link-module`, `run-module` | Still reject the option, before artifact reading, output acquisition or execution. Normalized source-free consumption needs no manifest. |
| `compile-native` producer/consumer, `compile-native-set`, `compile-native-graph` producer/consumer | Still reject it, although these commands read source. Shared parser code is not authorization to extend native package/set/graph routes. |
| `scan-component` and `build-component` | Not part of this private-module extension; the separate unapproved [scanner-component extension](scanner-component-source-layout-registration.md) admits the manifest only for scanner-source compilation, not subject-source compilation. |
| Native artifact validation, resolution and execution commands/workers; `build-trrm`, `build-trrm-library`, `run-trrm`, `validate-trrm-*`; syntax-only and frontend commands other than the separately scoped `frontend-component` probe; every other command form | Still reject it. The [frontend-component extension](frontend-component-source-layout-registration.md) admits the manifest only for that probe's component-source compilation, not a private-module route. This candidate authorizes no source-free, native-package or additional Standard TRRM path. |

Retain the existing option's exact case-sensitive spelling and argv rules:
one occurrence, exactly one following nonempty operand, after required
positionals among existing options and before an optional final `--json`.
Reject `-`, a dash-leading operand, equals syntax, aliases, repeated options
even with the same path, and extra operands. An explicit relative/absolute
path can name a dash-leading file. Resolve it against the invocation working
directory, never a source/provider directory.

Private module source inputs remain bounded files, **not stdin**. Keep the
existing source/export count limits, combined source budget, runtime and
arithmetic selectors, producer-only Core mode and `.trm` destination rules.
The configuration is not another source tree or export. Do not add
`--identity`, `--source-name`, `--abi-output`, native-import mode or a source
grant to this command family as part of this extension.

### Using Module and publication identity

The manifest's `usingModuleIdentity` explicitly names **the one using source
Module of this invocation**, producer or consumer. All source trees share its
registration snapshot; their lexical namespace/import scopes remain separate.
The file name, output stem, first export, command role and selected `.trm`
producer path supply no identity.

Unlike `compile-trrm`, `compile-module` has no separate publication identity
selector. Therefore there is no second CLI identity to compare, infer or
persist, and no new `--identity` option is needed. This is the same explicit
using-context delivery already used by the direct source commands, not a new
serialized identity for the private artifact. If a later API supplies a
separate publication identity for retained source-role dependencies, it must
agree with their captured context or perform an admitted checked
transformation; this candidate supplies neither that API nor a relabeling.

The using Module cannot equal any selected layout-provider Module. For a
consumer, its using identity designates its source Module, not the referenced
producer. No comparison to an absent producer publication identity is defined.
Loading that producer neither recreates its former source-binding context nor
propagates its registration.

## Reused version-1 host input

This extension consumes the exact strict version-1 manifest and declaration
projection owned by [source layout CLI registration](source-layout-cli-registration.md#host-authority-and-producer-construction)
and [its JSON shape](source-layout-cli-registration.md#version-1-json-shape).
It adds no field, schema revision, parser, provider discovery, authorization
mechanism, artifact decoder or default. The base owner's complete UTF-8/JSON,
identity, origin, argument, target-set, member-order and immutable-snapshot
checks apply unchanged, including when source uses no layout roles.

The route-specific using-Module interpretation is defined above. Provider
origins still describe the explicitly selected Layout/Blittable providers,
not the source Module, `--runtime`, the referenced `.trm` function producer
or an inferred target default. A `.trm` reference is not a schema provider;
existing compatibility checks apply only to dependencies actually retained.

## Admission, paths and consumer references

Implement admission in this order:

1. Validate command eligibility and all option syntax before reading input.
   Resolve source, reference, manifest and output paths once. The manifest is
   a regular file, not stdin, a directory, URL, include tree or search root.
2. Refuse a manifest that aliases any source, the selected producer `.trm`
   input or the output. Retain output distinctness from every source and
   producer artifact. Use the existing shared
   [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs) relation,
   including full paths, platform casing, resolved links and available file
   identities/hard links, not argv-string equality. Failure to inspect
   required identity is an error, not evidence of distinctness.
3. Capture manifest bytes once and completely admit the shared closed
   producer, origins, declarations, expectations and layout set before
   reading/binding source or reading the selected producer artifact. Keep
   immutable per-invocation snapshots; do not reopen configuration mid-bind
   or cache selection by path/using-identity text alone.
4. Read the bounded source snapshots and, for consumers, capture and validate
   the producer bytes through the existing `ModuleCompiler.CreateReference`.
   Preserve that reference and every original imported expectation. Supply
   the manifest set **once through
   `CompilationOptions.LayoutCoreRegistration`**, alongside the existing
   runtime/arithmetic options, to the existing single/multi-tree factory.
5. Apply ordinary lookup, argument mapping, checked constant evaluation,
   layout derivation and shared verification. Then use the existing private
   producer/consumer writer and staged `WriteArtifact` publication. A valid
   manifest cannot enable a missing codec or lowerer.

The shared `TryReadModuleOptions` also serves `compile-native`. Its new
option branch must explicitly require `args[0] == "compile-module"`; passing
or accepting an ignored manifest output through another caller is not valid
routing. Build/test recipes may pass configuration only after the same
explicit host authorization. No repository-local manifest search, build-mode
default or particular source-name recognizer is introduced.

| Consumer/reference condition | Required behavior |
|---|---|
| An ordinary decoded producer reference already exposes selected exported layouts/signatures | Use those normalized facts and existing import checks unchanged. They grant no source schema role. No manifest is needed merely to construct/read/call those imported types. |
| Consumer source authors its own Layout/Blittable annotations | Require its own explicitly selected using-context registration. It may use the same authorized layout providers as the producer, but does not inherit the producer's authority or rewrite imported layout expectations. |
| A host-configured reference already supplies layout registration for this using context | Preserve the existing reference-only selection path when options supply none. Supplying options registration as well rejects, even for equal contents or the same object; do not merge, prefer one or manufacture an unconfigured reference to bypass the duplicate. The private byte decoder does not currently produce such a registration. |
| Another registration family is already host-selected | Preserve the existing same-`CoreProviderModuleBindings` **object** requirement for native/layout composition. Equal strings/lists are insufficient. This manifest constructs layout-only configuration and cannot silently acquire or replace native registration. |
| No selection, including with `--corelib` or same-named source declarations | Unaffected source and normalized imports remain usable. Affected source use diagnoses; no automatic role, retry, injected `using`, source rewrite or discarded annotation. |

This selects no `NativeCoreRegistration`, NativeImport/NativeCallable schema,
ABI convention, foreign provider, unsafe authority, ownership transfer or
lifetime extension. Existing native restrictions remain independent.

## Failures and observable results

Use the existing experimental CLI response paths, not a new machine protocol:

| Failure | Projection |
|---|---|
| Ineligible command, wrong option spelling/arity/operand, duplicate option or otherwise invalid command combination | `TRM2000`, exit 2, before input reading/publication/launch. |
| Manifest I/O/access/path-admission, strict UTF-8/JSON/version/type/range failure, or invalid origin/declaration/expectation/registration composition | `TRM2001`, exit 2. Isolate this admission from the module artifact catches; malformed JSON must not escape or become `TRM5003`. |
| Manifest aliases a source, producer artifact or output | `TRM2002`, exit 2, before output acquisition/replacement. |
| Existing invalid `.trm` destination or output alias with a source/producer, not involving the manifest | Preserve the module-output `TRM5001`, exit 1 category; use the same complete path-identity check. |
| Source lookup, attribute/constant/layout or other semantic failure | Existing located source diagnostics, exit 1; retain originating source snapshots and ordinary failure/exhaustion distinctions. |
| Malformed/unsupported private artifact or incompatible retained contract, including an unrepresentable surviving nominal enum | Preserve `TRM5001`/`TRM5002`, exit 1 according to the existing format/contract distinction. |
| Existing nonconfiguration module I/O/output/unsupported-operation failure | Preserve the command's existing `TRM5003`, exit 2 projection and truthful publication outcome. No alternate provider/schema/emitter. |

Configuration errors name the manifest and, when established, the offending
JSON property/index path. Under trailing `--json`, use the existing schema-1
input-error response with no fabricated source span. Source diagnostics retain
their existing single-source or multi-source response schema; successful
artifact responses gain no authority/registration fields. This is not an
implementation of [`terrum.cli/1`](../specs/cli-protocol.md).

On every prepublication failure, leave existing `.trm` outputs and all inputs
byte-for-byte unchanged and publish no successful artifact. Do not truncate
destinations, modify producer references, launch a program or repair an error
with another manifest/provider. Retain staged publication and report genuine
publication/cleanup failures; no multi-file atomic transaction is promised.

## Private payload, identity and replacement consequences

**No private wire/metadata revision changes are selected.** The inspected
writer/reader uses exact semantic revision **104**, including Packing,
optional declared Alignment, derived Alignment/RequiredAlignment, Blittable
commitments, ordered field contracts and wide inline geometry. This extension
continues that exact schema; it adds no revision 105, old-revision reader,
native outer-container change, profile ID, opcode, Feature or metadata row.
Manifest version 1 and provider declaration-contract revisions select neither
that payload nor a Standard TRRM schema.

Persist only the existing normalized layout inputs/facts and supported
independently retained dependencies. Do not persist the configuration bytes
or path, authority, host snapshot, using-context label, raw annotations or a
new Core dependency into the private payload, optional source/debug records
or assigned Standard `Layout`/`LayoutField` rows. Those
[Standard records](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
remain unchanged and outside this route amendment.

An annotation-only selected enum can disappear through the existing checked
normalization when no independent enum use survives. Do not blanket-delete
enum definitions, origin obligations or required-evaluation dependencies to
make private emission succeed. Raw enum/schema/typed-value/evaluation facts
that do survive require their own complete supported carriers. The current
private writer's nominal-enum refusal remains in force; this manifest is
not their publication codec and authorizes no guessed encoding.

After normalization and successful private emission, `CreateReference`,
`link-module` and `run-module` consume supported layouts without source or
configuration. They perform their existing structural, support and binding
checks; this adds no mandatory semantic re-proof of producer bodies.

There is **no new serialized nominal identity or replacement relation**.
Private symbolic function/type contracts retain their existing scope,
visibility, complete ordered layout expectations and remapping behavior
described by [the module implementation reference](../implementation/modules.md#current-artifact-and-loading-rules)
and [layout inventory](../implementation/layout-profiles.md). Compatible body
replacement does not require the producer's former manifest, provider revision
or source. Changed retained Packing/Alignment/Blittable/field/type contracts
still fail the existing exact comparison. No current Core map can substitute
for an old expectation, and equal geometry does not alias distinct retained
identities. Reordering local indexes requires checked remapping and
verification, not byte/name substitution.

Changing configuration starts a new source compilation and binding generation;
it never relabels an old consumer. Source-free commands reject an attempted
manifest override. `run-module` performs load-time semantic lowering;
`link-module` emits a fresh image, not replacement of an existing native PE.

## Required integration and acceptance

The route/help and parser/factory wiring in `Program.cs`/
`Program.Modules.cs`, shared manifest reader/path helpers, and active
CLI/module regressions are provisionally implemented. The current CLI
reference, module examples and layout support inventory distinguish this
extension from the base manifest candidate. Reuse the current carrier and
writer; do not add a competing resolver or public registration API. The
candidate remains unapproved, and completion of this route is not completion
of the broader conformance-adoption work.

The cases below are acceptance gates; the results above cover only the
identified focused selection. Use public compiler/CLI paths and unrelated synthetic authorized providers,
with actual bounded source files and non-`Main` producer exports. The old
private rejection case uses stdin and `Main`; merely removing its expected
usage error would not establish a valid producer test.

| Class | Cases and required observations |
|---|---|
| Positive source routes | Producer and consumer, single/multiple trees, no-entry libraries, producer `--corelib`, both existing runtime policies and independent arithmetic profiles where admitted. Inspect normalized layouts and persisted contracts, not only manifest parsing or exit zero. |
| Positive motivating regression | Both `CliBuildsMultiSourceSelfHostLibraryAndConsumerAsync` variants explicitly configure the annotated producer. The existing normalized-layout consumer works without a manifest. No SourceRange/Token/provider-name recognizer. |
| Positive consumer ownership | Consumer source declares an unrelated packed/over-aligned local type alongside imported producer types. Its explicit registration enables only its source role uses and preserves the selected producer's complete expectations. Split schema/enum origins and nondefault keys/names remain valid through ordinary lookup. |
| Negative routes | Replace only the `compile-module` rejection row with valid-file positives. Retain native rejection and cover both private source roles versus `link-module`/`run-module`; also retain native set/graph, source-free Standard, syntax-only and frontend exclusions. Nonexistent inputs must not be read before usage rejection. |
| Negative configuration | Reuse the complete shared strict-manifest matrix on both new roles: invalid UTF-8/signatures/surrogates, duplicate decoded/unknown/missing fields at depth, JSON/type/version errors, invalid origins, keys, owners and complete enum/schema shapes. Invalid configuration wins over malformed source or a missing producer file. No partial registration. |
| Negative source/authority | No manifest with annotated source, `--corelib` alone, same-named declarations, foreign enum with equal bits, unimported/shadowed/ambiguous names, bad controls and duplicate placements. Preserve source diagnostics and per-tree import isolation; normalized imports alone still succeed. |
| Negative reference composition | Ordinary producer references never grant source roles. Preserve options/reference duplicate rejection even for the same selection, native/layout snapshot-object agreement and stale/foreign-context rejection wherever host-configured reference delivery is exercised. Do not add a CLI channel to manufacture these references. |
| Boundary options and limits | Option order, final `--json`, duplicates, empty/missing/dash/equals/case variants; bounded file-only sources, source/export count and byte limits. Configuration must not alter existing counts, budgets, producer-only options or runtime compatibility. |
| Boundary values and layouts | Existing integer-token limits, missing versus explicit zero, null versus initialized empty arrays, member order and target-set ordering. Exercise Packing 1/2/4/8/16, Alignment 1/8192 and invalid powers/ranges, nested alignment, omitted versus supplied controls and unsupported layout/codecs. No truncation or inferred defaults. |
| Boundary paths/publication | Manifest versus every source/reference/output, output versus every input, relative/case/link/hard-link aliases where supported; inaccessible identity/configuration. Use sentinel outputs and verify every prepublication failure preserves bytes and creates no successful artifact. |
| Cross-route source binding | Compare supported normalized facts with options-bearing API and already-eligible CLI source paths, including separately selected Standard library production where admitted. Existing Standard identity-mismatch/decoder rules and all native-family exclusions remain unchanged. |
| Cross-route source-free use | Compile producer/consumer, remove authored sources and manifest, then link and supervise a headless `run-module` launch. Inspect the unchanged exact private revision and consumer bytes. Distinguish successful persistence, PE emission and actual runtime observations. Reject attempts to pass configuration to either source-free command. |
| Cross-route replacement/remapping | Compatible body change and admitted physical type-index relocation preserve the same consumer; incompatible retained layout/field/access/runtime changes fail before execution/publication. Preserve every expectation through remapping; no manifest replay or current-provider substitution. |
| Cross-route retained dependencies/isolation | Annotation-only normalization persists without a Core registration record; an independently surviving LayoutKind value/signature still hits the existing missing-codec gate. Repeated/concurrent invocations, changed contents at one path and different using contexts show no global role/cache leakage or borrowed snapshot handles. |

Use the existing supervised process/deadline and bounded cleanup helpers for
execution cases; do not enable visible-window or hardware integrations.
Record the final compiler/configuration inputs and matched active test counts.
The inspected NUnit project has default compile inclusion for these fixtures,
but their presence or an earlier manifest run is not passing evidence for the
new routes.

## Remaining decisions and independent gates

No further contract choice is required for this **normalized-layout-only**
private source-compilation extension. Actual authorized authority/provider
identities, revisions, declaration-schema selections, export/member keys and
qualified names remain explicit host/deployment inputs, not constants for the
implementer to invent.

Artifact-backed schema providers, persisted raw enum/schema dependencies,
native package/set/graph source registration and additional Standard TRRM
routes remain independent decisions/support gates. None is needed to route
the already-selected in-process layout manifest to these two private source
commands, and none is provisionally approved by this document. Completing
this slice is not completion of the broader conformance-adoption work.
