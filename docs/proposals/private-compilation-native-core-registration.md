# Native Core configuration for private compilation

**Status: unapproved; provisionally actionable only under the user's authorized
missing-contract workflow.** This recommends source-configuration delivery,
not normative adoption, a stabilized CLI/private schema or compiler conformance.
No build, test or native qualification accompanies this proposal.

## Missing decision and recommendation

Extend the existing **`--native-core-manifest <path>`** and
**`--native-output-core-manifest <path>`** inputs to exactly
**`compile-module producer|consumer` and `compile-native producer|consumer`**.
Reuse their exact readers and the existing shared registration composer.
Do not introduce a private/FNI manifest grammar, version, schema field or
registration API.

The gap is delivery to source binding, not PF111 encoding or identity inputs:

| Inspected source | Current boundary |
|---|---|
| [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs), `BindForeignNativeInterfaceLayout`; [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `TryBindNativeOutputAttribute` | Authored FNI requires the selected NativeCore FNI schema and effective NativeCallable contracts; authored NativeOutput independently requires its selected Core schema. Missing selection diagnoses `TRM3030`. |
| [`NativeOutputContractValidator.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputContractValidator.cs), `IsValidInterfaceTable` | Local source output contracts require FNI/output registrations. Admitted normalized foreign origins have a distinct consumption path; they are not permission to bypass authored-source registration. |
| [`Program.Modules.cs`](../../sources/terrumc/Program.Modules.cs), `TryReadModuleOptions`, `RunModuleCommand` | The private parser now admits NativeCore and NativeOutput alongside Layout, reads the existing strict definitions in option order, and composes registrations through one shared provider-binding snapshot before source binding. |
| [`Program.NativeModules.cs`](../../sources/terrumc/Program.NativeModules.cs), `RunNativeModuleCommand` | Native producer/consumer source compilation now admits NativeCore and NativeOutput, pairs them with identity/PF111 selection, and passes the composed registrations to the existing compilation options. Layout remains excluded from `compile-native`. |
| [`NativeCoreManifestReader.cs`](../../sources/terrumc/NativeCoreManifestReader.cs); [`NativeOutputCoreManifestReader.cs`](../../sources/terrumc/NativeOutputCoreManifestReader.cs) | NativeCore explicitly reads `/1` and FNI-capable `/2`; NativeOutput reads its independent `/1`. Both return complete unbound definitions, not private declaration-key assignments. |
| [`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs), `ReadCoreManifestDefinitions`; [`CoreManifestRegistrationComposer.cs`](../../sources/terrumc/CoreManifestRegistrationComposer.cs) | The private commands now reuse the existing Core-option-order reader and composer, preserving one provider-binding snapshot and independent role selection. |

The recommended CLI route is implemented but remains unverified under the
active build/test hold. This does not change the proposal's unapproved status,
extend Layout registration to native package commands, or make Core manifests
a substitute for private identity assignments.

No existing proposal resolves these four routes.
[NativeCore CLI registration](native-core-cli-registration.md#exact-option-and-command-contract)
and [NativeOutput CLI registration](native-output-cli-registration.md#exact-option-and-command-contract)
explicitly exclude them.
[NativeCore FNI `/2`](terrumc-foreign-native-interface-core-manifest.md)
changes grammar but not command eligibility.
[PF111](private-foreign-native-interface-persistence.md#cli-option-boundary)
selects private persistence, and
[foreign identity inputs](private-foreign-native-interface-identity-inputs.md#cli-scope-snapshots-and-native-composition)
explicitly leave Core delivery separate.

This candidate supersedes **only those four command exclusions for the two
Core options**. The linked owners retain their exact grammars, authority,
declaration, source and binding contracts. A PF111-only switch or combined
identity/Core file would unnecessarily couple independent selections and
duplicate working readers; hosted-only configuration leaves this CLI gap.
No accepted specification text is replaced. Preserve
[Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
[native roles](../specs/native.md#native-declaration-roles),
[binding/replacement](../specs/representation/binding-and-replacement.md),
[reader trust](../specs/representation/reader-trust.md) and
[private persistence](../specs/representation/persistence.md).

## Exact options and routes

Both options are independent, optional and nonrepeatable, even when source
uses no selected role. Each consumes exactly one following nonempty argv
element, after required positionals among existing options and before an
optional final `--json`. Spelling is case-sensitive. Reject aliases, equals
syntax, repetitions even with equal paths, extra/missing operands, `-` and
dash-leading operands. An explicit relative/absolute path can name a
dash-leading file. Resolve against the invocation working directory once.
Require a regular file; no extension is required. No stdin, URL, directory,
include, plugin, environment selection or discovery is admitted.

| Existing command form | Newly delivered configuration |
|---|---|
| `compile-module producer <source> <export> ... -o <artifact.trm>` | The producer's one source library compilation, including every `--source` and the existing producer-only `--corelib` form. |
| `compile-module consumer <source> <producer.trm> ... -o <artifact.trm>` | The consumer's own source compilation alongside its independently checked positional producer reference. |
| `compile-native producer <source> <identity> --export <name> ... -o <producer.trmn> --runtime <policy>` | The native producer's source library compilation, including its existing multi-source/Core-library forms; retain real code-export/image admission. |
| `compile-native consumer <source> <producer.trmn> ... -o <consumer.trmn> --runtime <policy>` | The native consumer's own source compilation alongside the checked positional native reference. |

Existing eligible direct source commands and `compile-trrm` remain unchanged.
Every otherwise excluded form remains excluded, including syntax-only,
scanner/component/frontend, `compile-native-set`, `compile-native-graph`,
and source-free link/run/build/validate/catalog/resolution routes. Shared
parsers cannot accept and discard these inputs on another caller.
Preserve all existing source/export budgets, file-only sources, destinations,
runtime/arithmetic requirements, import restrictions and factory selection.
Add no `--identity`, source-name, ABI-output or native-lowering option here.

Consume exactly **`terrumc.native-core-manifest/1` or
`terrumc.native-core-manifest/2`** through the NativeCore reader and
**`terrumc.native-output-core-manifest/1`** through the NativeOutput reader.
Their strict UTF-8/JSON, complete-record, provider and
declaration checks apply unchanged, including unused records. `/1` selects
I/C/P only; authored FNI needs `/2` and its complete FNI schema. `/2` selects
no NativeOutput; authored output needs the independent E/O file. Neither file
selects NativeExport, ownership, an executing receiver provider or a codec.
Do not infer a grammar from fields, cross-feed readers or retry another format.

### Layout and schema boundaries

Keep [private source Layout registration](private-module-source-layout-registration.md)
on `compile-module` producer/consumer and compose any such explicit Layout
input with the newly selected families. **This amendment does not extend
`--layout-core-manifest` to `compile-native`.** Native source that needs newly
authored Layout/Blittable annotations still has that separate delivery gate;
these two files cannot supply or bypass it. Already admitted normalized
provider layouts remain consumable under their existing contracts.

Core options are **not gated on PF111**: they configure the existing source
binder, independently of destination capability. They neither imply nor
replace `--private-schema`. PF111 still requires exactly
`terrum/private-semantic-111-foreign-native-interfaces-1` on its selected input
and destination routes. A valid source registration cannot make legacy
104/106 retain foreign facts; the independently unsupported output must refuse.

Likewise, keep `--private-identity-manifest` v1/v2 and the
[v2/PF111 command gate](private-foreign-native-interface-identity-inputs.md#cli-scope-snapshots-and-native-composition)
unchanged. That file supplies explicit snapshot-bound keys, not Core authority.
These Core files supply no private using/key assignments or source/producer
digests. Do not synthesize missing identity inputs from Core names, keys,
using labels or provider snapshots, and do not add fields to any manifest.

## Using identity, providers and consumers

All explicitly selected Core files must have ordinal-equal
`coreAuthorityIdentity` and `usingModuleIdentity`. Their using identity names
the invocation's one source Module across all trees, not a namespace, source
path, output stem, command role or native library. Passing an explicit file
remains host authorization of its closed declaration producer; its authority
string, digests and source/package assertions do not authenticate that authority.

* When a private identity file is supplied, require its using identity to
  equal every selected Core file after each file's independent validation.
  The common value must satisfy both domains; do not relax private
  1–64-character ASCII Module identities to Core's broader Text domain.
* For a native producer, require every selected Core file's using identity
  to equal positional `<identity>` ordinally, even without an identity file.
  For a module producer there is no positional publication identity.
* A consumer's configuration selects its own source using Module. A native
  consumer's outer identity denotes its selected producer, not another
  using-identity selector. Preserve the identity-input owner's distinct
  using/provider views and actual inner-owner/native-producer correspondence.

No Core provider may equal the still-unbound using Module. Shared providers
coalesce only when **every Origin field** agrees; duplicate/conflicting
declarations or exports reject even when equal-shaped. Compose distinct
complete definitions with the existing composer into **one actual
`CoreProviderModuleBindings` object**, then create each explicitly selected
family against that object. Equal separately created snapshots do not suffice.
Publish no partial selection and grant no file/declaration-order precedence.

Core providers are closed host-produced schema/enum providers, not alternate
positional private providers. Preserve the existing actual-registration
checks and complete inputs for eligible producer Core-enum imports. Consumer
private imports, including every foreign owner/member, still resolve through
the positional producer to actual owned exports; no Core fallback, discovery
or proxy re-export. Retain every original expectation rather than replacing it
with current configuration or a replacement's declarations.

Decoded normalized FNI/convention/output/layout facts do not require the
producer's former manifests merely for consumption. They grant no Core role
to the consumer's newly authored annotations. Keep source registration and
`NativeOutputContractValidator` checks: never mark authored owners imported or
invent normalized private origins to bypass them. Preserve ordinary lookup,
typed enum constants, complete callable/output contracts, unsafe authorization,
known backing, reservation/publication, dependencies and cleanup. Registration
supplies neither missing ABI support nor semantic re-proof of producer bodies.

## Admission, snapshots and failure behavior

1. Validate command eligibility and the whole option set before reading any
   configuration, source or artifact. Resolve paths and check destination
   requirements without acquiring outputs.
2. Use shared [`PathsAlias`](../../sources/terrumc/Program.PathIdentity.cs)
   for every selected manifest against all sources, the consumer's positional
   producer, output and other selected manifests, including Layout and private
   identity. Preserve output distinctness from sources/producer using the
   same full-path, casing, link and available physical-file identity relation.
   Inability to establish required distinctness is failure, not a fallback to
   argv equality.
3. Read each selected Core definition once, in Core-option order, through
   `ReadCoreManifestDefinitions` or the same readers; admit the identity file
   under its separate contract before source/provider reads. Check configuration
   using/authority/origin consistency and create the shared registrations before
   binding. No reopen mid-bind or process-global, path-only or content-only
   registration cache.
4. Capture bounded source/producer inputs once. Preserve any identity-file
   guards over original source bytes in argv order and the whole positional
   `.trm` or `.trmn`, not its inner Contract/image alone. Use those same bytes
   for matching default or exact-schema `CreateReference` APIs, preserving
   complete original provider expectations.
5. Pass registrations once through `CompilationOptions.NativeCoreRegistration`
   and `.NativeOutputCoreRegistration`, plus the existing module Layout slot,
   to the existing options-bearing source factories. Preserve options/reference
   duplicate refusal even for the same object and the shared-snapshot invariant.
   Capture any private identity inputs for that exact successful program, then
   use the independently selected default/WithSchema creation API.
   Produce complete semantic/native bytes before staged `WriteArtifact`.

| Failure | Existing experimental CLI projection to retain |
|---|---|
| Bad option/scope/arity/repetition/operand/combination, or native producer positional identity disagreement with selected Core configuration | `TRM2000`, exit 2. |
| Core file I/O/decoding/JSON/grammar/record/origin/binding/registration failure; unequal Core authority/using identity or Core/private using identity | `TRM2001`, exit 2; identify configuration/property paths, not fabricated source spans. Isolate configuration admission from artifact I/O catches. |
| Manifest aliases an input/configuration/output, or required manifest path distinctness cannot be established | `TRM2002`, exit 2, before output acquisition. |
| Source lookup/schema/argument/evaluation/semantic failure, including absent required FNI/NativeCallable/NativeOutput registration | Existing located source diagnostics, exit 1, including missing-schema `TRM3030`. |
| Invalid artifact/destination or unsupported/incompatible retained contract | Preserve `TRM5001`/`TRM5002`, exit 1 and independent schema/identity checks. |
| Nonconfiguration artifact/output I/O or unsupported operation | Preserve `TRM5003`, exit 2 and actual publication outcome. |

Prepublication failures leave existing destinations and inputs byte-for-byte
unchanged, publish no successful artifact and start no program. Retain staged
publication and report actual publication/cleanup failures; promise no new
multi-file transaction. Human/trailing-`--json` responses gain no fields or
configuration chatter. This adds no configuration-input kind to
[`terrum.cli/1`](../specs/cli-protocol.md); reject composition with its machine
selector under the existing selection-failure rule.

**Absent options:** preserve legacy source/reference/default-schema behavior;
no selection from PF111, identity-manifest v2, `--corelib`, artifact headers,
names or exports. Independently unsupported surviving raw enum/schema/value
obligations still refuse; do not erase them to serialize. Serialize only
supported normalized facts and independently retained dependencies, never
manifest bytes/paths, host authorization or registration objects.

## Implementation gaps and future acceptance

The parser, both private source-command handlers, shared Layout composition,
identity/alias admission and options delivery still need this route amendment.
Reuse existing readers/composer; update command help and the experimental CLI
reference with implementation. PF111 codec/projection/native edges and identity
v2 transport remain separately owned work, not evidence supplied by this doc.

Required cases, **not tests run here**, use existing public CLI/API test areas
and unrelated synthetic providers:

| Case | Required observation |
|---|---|
| All four routes, single/multiple source files, both Core versions, independent E/O and every admitted option order; module Layout pairs/triples | Exact selected sets reach the correct source factory through one shared snapshot; preserve Core-library/runtime/export restrictions. |
| Owned PF111 FNI without output, then with output; absent NativeCore, `/1` on FNI, E/O-only, missing E/O, same-shaped shadows and invalid source contracts | Independent exact source registration and output validation; no inferred role, unsafe permission or fabricated normalized origin. |
| Shared/split providers with unrelated keys; any Origin conflict, duplicate export, authority/using disagreement, Core/private domain mismatch or native positional mismatch | Complete combined admission or the specified refusal; no precedence, key inference or provider substitution. |
| Strict malformed/unused files, wrong/cross-fed grammar, BOM/Unicode/JSON/property/numeric boundaries and bad argv/excluded routes | Reuse exact reader guards and scope errors before source/provider reads or output acquisition. |
| Private v1/v2 and default/PF111 combinations, missing required owner/member maps, wrong producer schema and stale source/full-package guards | Preserve independent schema, identity and snapshot failures; Core configuration does not select or repair either. |
| Relative/case/link/hard-link aliases with every input/manifest/output; sentinel outputs; changed/concurrent invocations | Complete distinctness, prepublication preservation and fresh per-invocation snapshots without context leakage. |
| Normalized producer consumption without its source/manifests; consumer-local annotated FNI; incompatible provider replacement | Consume retained facts without source-role lookup, but require local selections and preserve every original owner/member/output expectation. |
| Native production with real unrelated code exports versus foreign-only metadata; locally authored native Layout/Blittable use | Preserve image/export refusal and the still-separate native Layout delivery gate; no dummy foreign thunk or layout bypass. |

**Remaining decision:** formal adoption of this two-option/four-route amendment.
It is provisionally actionable, with no further in-scope grammar or authority
choice delegated to implementation. Native Layout delivery, production host
authority/provider inputs and broader route/codec/runtime qualification remain
separate boundaries; this proposal does not complete the PF111 milestone.
