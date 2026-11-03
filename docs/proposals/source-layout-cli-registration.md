# Explicit CLI configuration for source layout registration

**Status: unapproved candidate; provisionally actionable under the user's
authorized missing-contract workflow.** This document changes no normative
owner, selects no production Core authority/provider and supplies no
implementation or conformance evidence.

## Decision and scope

Recommend one optional, nonrepeatable source-command option:

```text
--layout-core-manifest <path>
```

Its strict, versioned JSON input is **trusted host configuration for a bounded
in-process declaration producer**, not a provider artifact, source annotation
file or serialized authorization token. The CLI constructs actual checked
producer declarations and exports, captures their original expectations in
one using-Module binding snapshot, and passes the resulting
`LayoutCoreRegistrationSet` through
`CompilationOptions.LayoutCoreRegistration`.

This recommendation settles only the CLI delivery decision left by
[source layout registration](source-layout-core-registration.md). It does not
add layout slots to native registration, a general Core catalog, provider
discovery, dynamic adapter/plugin loading or an enum/schema artifact codec.
Concrete authority, producer identities/revisions, declaration-contract schema
selections, export/member keys and names remain explicit host inputs.

### Narrow supersession and unchanged owners

For **this layout-only path**, supersede the sentence in
[native Core provider configuration](native-core-provider-configuration.md#host-input-and-adapter-obligations)
that says the decision adds no CLI option or configuration-file grammar.
That deferral remains unchanged for native Core registrations and other
families. Preserve that proposal's explicit host authority, exact identity,
checked adapter, originating-context, original-expectation, no-default and
no-fallback rules. A layout manifest does not supply `NativeCoreRegistration`
or make NativeImport/NativeCallable recognition available.

Preserve without amendment:

- [Windows x64 layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas):
  the exact selected LayoutKind domain, Layout/Blittable schemas, constant
  types, placement, omission, packing/alignment, transitive eligibility,
  geometry, access and overflow rules.
- [Attribute grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
  [ordinary lookup](../specs/language/names-and-binding.md) and
  [Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity).
  The manifest neither changes Terrum syntax nor imports names implicitly.
- The candidate carrier and complete shape checks in
  [source layout registration](source-layout-core-registration.md#candidate-host-api-and-complete-schema-projection)
  and the checked origin/expectation obligations in
  [the Module-origin carrier](core-provider-module-origin-carrier.md#proposed-host-input-and-adapter-shape).
- Assigned TRRM `Layout` (`0x0015`) and `LayoutField` (`0x0023`) rows and
  their [existing payloads and semantics](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics).
  No new row, Feature, TargetProfile, source spelling, wire/private revision
  or compatibility reader is selected.

This document proposes an implementation CLI input, not an amendment to the
selected [`terrum.cli/1` machine envelope](../specs/cli-protocol.md). Existing
human/experimental `--json` behavior remains the response contract for this
bounded integration; its existence does not implement that machine protocol.

## Exact command-line contract

The option consumes exactly one following argv element. Its spelling is
case-sensitive. Allow it once, after the command's required positional
inputs, among that command's existing options and before a final `--json`.
No short alias, `--layout-core-manifest=...`, repeated occurrence, list value,
environment-based selection or automatic filename search is accepted.
An empty operand, `-`, or an operand beginning with `-` is invalid; a
dash-leading filename can be supplied through an explicit relative or absolute
path. Shell quoting/expansion remains the caller's responsibility.

| Commands | Version-1 behavior |
|---|---|
| `analyze`, `build`, `build-native-import`, `build-library`, `jit` | Accept the option and deliver the admitted set to the existing options-bearing source compilation factory. |
| `compile-trrm`, including `--library` | Accept it for source binding. Require ordinal equality between manifest `usingModuleIdentity` and the existing required `--identity`; retain the existing destination-schema and runtime selectors. |
| Every other command | Reject the option as usage error; never ignore it or consume it as a source/provider path. |

In particular, syntax-only `check`/`tokens`/`tree`, component prototypes,
`frontend-build`/`frontend-build-library`/`frontend-run`, private
`compile-module`/`compile-native`/`compile-native-set`/`compile-native-graph`
producers, and all source-free build/run/validation/catalog commands do not
accept it. This is deliberately the smallest direct-host plus Standard
source-producer path, not implicit support in every command that reads source.
Those excluded source paths have no layout-role selection until explicitly
extended; removing name recognition does not entitle them to a hidden default.
They remain usable for otherwise supported unconfigured source.

The option does not relax any existing command restriction. In particular,
`--corelib`, source grants, native-import mode, arithmetic/runtime policies
and native-lowering agreements retain their independent gates. `compile-trrm`
does not infer an output schema from the manifest or silently switch away
from its existing unselected emission route.

Resolve the manifest path once against the invocation's working directory,
not the source directory. No extension is required. Read a regular file, not
stdin, a URL, a directory, an include tree or a provider search location.
There are no nested file references in version 1. A source may still be
stdin; manifest reading/admission must finish before consuming that stdin.
`--source-name` remains a diagnostic source label, never a using-Module identity.

One invocation binds one using source Module, including all its `--source`
trees. The manifest's identity supplies that explicit registration context
for direct commands, which currently have no Module-identity option.
For `compile-trrm` it must agree with the publication identity rather than
relabeling a bound program at emission. A provider Module cannot be the
currently unbound source Module in this version-1 in-process route: the
manifest produces completed, separately owned provider snapshots, not the
source Module's still-unchecked declarations.

## Host authority and producer construction

Passing this option is the CLI host's explicit instruction to use the named
file as configuration from its designated authority and to authorize the
CLI's **closed layout-declaration producer** for that exact input. The host
must establish that authorization outside the source/package/artifact being
compiled. `coreAuthorityIdentity` records the host-supplied designation; its
text, a signature-looking field or a file hash cannot authenticate it.
No environment-independent production authority or provider is named here.

The closed producer executes no configuration-supplied code and loads no
assembly, library or adapter named by the file. It takes the complete
declaration records below as producer inputs, validates them, and creates real
in-process logical Module snapshots and their explicit logical exports.
It is not an adapter that pretends arbitrary already-existing artifact
exports have been checked. The host/producer remains responsible for the
truth and legitimacy of its claimed origins.

The provider's schema identity/revision is an explicit host selection naming
the **in-process declaration contract being produced**. The version-1
manifest grammar fixes the exact supported projection below; changing an
origin's schema token cannot change that grammar or admit a different
declaration shape. These tokens do not dispatch an arbitrary artifact decoder
or assert interoperability with external bytes. No production schema token,
revision, hash-derived revision or `"current"` default is supplied by the CLI.
An artifact-backed provider requires its independently admitted exact codec
and delivery route; version 1 has no artifact/plugin form and rejects fields
purporting to select one.

This bounded declarative producer avoids making this CLI path depend on
unavailable source EnumDecl/attribute-schema publication codecs. It is not
a second normative semantic interchange format. An ordinary dependency,
source annotation, Core compilation mode, colocated manifest or package
self-assertion cannot activate it. A build tool may pass the option on behalf
of its host only after making the same explicit authorization decision.

## Version-1 JSON shape

The following tables define the complete JSON grammar, not illustrative
subsets. Every object has exactly the listed properties, all required.
Object-property order is insignificant. Arrays retain order except for the
schema `targets` set specified below. There are no omitted-field defaults.
Only `Argument.enumType` and `Argument.defaultUInt32` admit null.

Use strict UTF-8, permitting one leading UTF-8 signature. Require one root
object followed only by JSON whitespace. Reject invalid UTF-8, unpaired
surrogates in decoded strings, comments, trailing commas, trailing JSON,
duplicate decoded property names at **every** depth, and unknown properties.
Escaped and literal forms of the same property name are duplicates.
Property names and tag values are ordinal and case-sensitive.

`u32` means an exact JSON integer token in `0..4294967295`, without sign,
fraction or exponent; `i32` permits an optional minus and has the signed
32-bit range. Numeric strings, Boolean-to-number conversions and rounded or
truncated numbers reject. `Text` means well-formed decoded Unicode text,
nonempty and not whitespace-only, with no NUL. Do not trim, case-fold,
normalize Unicode or reinterpret identity/key text as paths.
Qualified/member names additionally obey the corresponding ordinary source
naming rules, not a new CLI identifier grammar.

### Root and provider origins

| Root property | Value |
|---|---|
| `schemaVersion` | Integer token exactly `1`; this versions only this configuration grammar. |
| `coreAuthorityIdentity` | `Text`; the explicit host designation, not a provider identity inferred from a namespace. |
| `usingModuleIdentity` | `Text`; the one using logical Module. |
| `providers` | Array of one through three `Origin` objects, one for each distinct provider used by the selected declarations; no unused entry. |
| `layoutKinds` | One complete `EnumDefinition`. |
| `layout` | One complete `SchemaDefinition`. |
| `blittable` | One complete `SchemaDefinition`. |

| `Origin` property | Value |
|---|---|
| `moduleIdentity` | `Text`; exact provider logical Module identity. |
| `revisionIdentity` | `Text`; exact selected producer revision token, not an ordered version. |
| `schema` | `SchemaSelection`. |
| `governingSemanticEdition` | `u32`; explicit supported governing edition under the existing Module domain. |
| `targetRequirement` | `u32`; the actual provider Module requirement in the existing supported domain. |
| `runtimePolicy` | `u32`; the actual provider Module policy in the existing supported domain. |

`SchemaSelection` has exactly `identity: Text` and `revision: u32`, with
nonzero revision. Missing requirements/policy are not zero; an explicitly
supplied valid zero retains its existing meaning. Unsupported editions,
requirements or policies reject; this proposal assigns no additional values.
Producer revision, declaration-contract schema/revision, semantic edition,
Module requirements and output artifact schema are separate facts.

Repeated provider identities reject even when every field agrees; two
different revisions for one identity likewise reject. Multiple declarations
can refer to the one agreeing origin without duplicating that entry.

### Exact identities and enum definition

`Identity` has exactly:

| Property | Value |
|---|---|
| `providerIdentity` | `Text`; equal to exactly one `Origin.moduleIdentity`. |
| `exportKey` | `Text`; explicit producer-assigned declaration identity key. |
| `kind` | Exactly `"enum-type"`, `"enum-member"` or `"attribute-schema"`, mapping to the existing semantic kinds, not serialized C# ordinals or TRRM DefId kinds. |

On top-level enum/schema declarations the key is the actual logical export
key. On a member it is the existing member identity component, **not an
independent Module export**. Neither form is generated from source names.

| `EnumDefinition` property | Value |
|---|---|
| `identity` | `Identity` with kind `"enum-type"`. |
| `qualifiedName` | The actual checked source-qualified name, as `Text`. |
| `underlyingType` | Exactly `"int32"`. |
| `members` | Complete ordered array of four `Member` objects. |
| `genericArity` | `u32`, exactly `0`. |
| `isPublic` | Boolean, exactly `true`. |
| `isPrimary` | Boolean, exactly `true`. |
| `isFlags` | Boolean, exactly `false`. |
| `blittableTargetRequirement` | Explicit `u32` in the existing enum carrier's supported commitment domain; distinct from the provider Module requirement. |

| `Member` property | Value |
|---|---|
| `identity` | `Identity` with kind `"enum-member"` and the enum's provider. |
| `declaringEnum` | `Identity`, exactly equal to the containing enum's identity. |
| `declarationKey` | `Text`; actual producer-assigned enum-owned member key. |
| `name` | Exact checked source member name. |
| `value` | `i32`; checked underlying value. |
| `isPublic` | Boolean, exactly `true`. |

The array contains exactly Auto=0, Sequential=1, Union=2 and Transparent=3,
without aliases, extra cases or duplicate identities/keys/names/values.
Preserve its supplied producer declaration order; do not sort by these
names/values. The array position supplies the declaration ordinal, not a
derived member key. Member identity keys and `declarationKey` are independently
supplied and retained under their existing scopes; neither is derived from
the other, a prefix or the ordinal.

### Complete schema definitions

| `SchemaDefinition` property | Value |
|---|---|
| `identity` | `Identity` with kind `"attribute-schema"`. |
| `qualifiedName` | The actual checked source-qualified name, as `Text`. |
| `arguments` | Initialized JSON array of `Argument` objects; exact role shape below. |
| `targets` | Array representing a set of exact `"inline-struct"` and/or `"integer-backed-enum"` tags; no repeated/unknown tag. Order is insignificant. |
| `isPrimaryTargetOnly` | Boolean, exactly `true`. |
| `genericArity` | `u32`, exactly `0`. |
| `isPublic` | Boolean, exactly `true`. |
| `isPrimary` | Boolean, exactly `true`. |
| `isRepeatable` | Boolean, exactly `false`. |

`Argument` has exactly `name: Text`, `kind`, `enumType`,
`isRequired: Boolean`, `allowsPositional: Boolean` and `defaultUInt32`.
`kind` is exactly `"layout-kind"` or `"uint32"`. `enumType` is an `Identity`
or null; `defaultUInt32` is `u32` or null. Their complete allowed combinations
for `layout.arguments`, in this exact projection order, are:

| `name` | `kind` | `enumType` | `isRequired` | `allowsPositional` | `defaultUInt32` |
|---|---|---|---|---|---|
| `Kind` | `"layout-kind"` | Exactly `layoutKinds.identity` | `true` | `true` | null |
| `Packing` | `"uint32"` | null | `false` | `false` | `16` |
| `Alignment` | `"uint32"` | null | `false` | `false` | null |

`layout.targets` is exactly the singleton `"inline-struct"`.
`blittable.targets` is exactly the two-tag set and
`blittable.arguments` is exactly `[]`. These tags map only to the candidate
carrier's existing placement flags. They are not new Terrum targets.
The two schema identities are distinct. One provider/export key cannot
select both a top-level enum and a schema by changing `kind`; identity reuse
across declarations/roles rejects. Enum-owned member keys retain their
separate scope and are not compared as Module export keys.

These are complete actual declaration inputs, not role names from which the
adapter invents omitted schema facts. No mutable property/FieldDef,
constructor ABI, extra arguments, renamed labels, widened targets or
altered defaults are supported. The manifest's canonical argument order
does not restrict ordinary source named-argument ordering. Actual qualified
names and export keys need not use the semantic slot names.

## Admission, source binding and errors

Complete these steps before affected binding, emission or program launch:

1. Validate command eligibility, arity, duplication and existing options.
   Resolve input/output paths and capture the complete manifest bytes once.
   Refuse output destinations that alias the manifest or another input,
   including `--abi-output`; apply the target's path/file-identity relation,
   not only equal argument strings. Do not reopen configuration during binding.
2. Strictly decode and validate the entire manifest, even when the source
   would use none of its roles. Create each provider's checked in-process
   declarations and logical export map from the complete records.
   Provider/module/key/kind and member ownership are established before
   comparison; a role label or provider-membership check is insufficient.
3. Use ordinary checked Module/declaration binding, completing its reusable
   adapter/export path where absent, to capture actual exports and complete
   original expectation graphs in one
   `CoreProviderModuleBindings` snapshot. The initial expectations are immutable
   snapshots of these checked declarations, not references to mutable JSON
   objects or a current-configuration lookup. Preserve all enum/member,
   schema, requirement and representation facts. Do not add a competing resolver.
4. Call the proposed validating `LayoutCoreRegistrationSet.Create` with those
   checked inputs. Publish no partial set if any role, origin, expectation or
   shape fails. Pass the set once through the options property; do not create
   a host-configured reference as a second selection channel.
5. Admit the selected enum and schemas to ordinary source lookup in the using
   context. Keep lexical imports separate for each source tree. No injected
   `using`, terminal-name search, source rewrite, enum retagging or retry under
   the Core spelling is permitted. Preserve typed constant evaluation,
   required-evaluation dependencies, failure/exhaustion and layout-cycle rules.
6. Normalize checked source facts once, then use ordinary shared verification,
   rewriting, persistence and lowering. A successful configuration supplies
   neither missing target/codec support nor ownership, initialization,
   alignment, access, lifetime or unsafe authorization.

The existing options/reference duplicate-selection rule still applies if
another host-configured path is later composed. A future native and layout
selection must use the **same snapshot object**, not two equal lists or
using-Module strings. This option constructs only the layout set and cannot
silently obtain or replace a native registration.

| Failure | Required experimental CLI behavior after integration |
|---|---|
| Wrong command/spelling/arity, repeated option, invalid operand, or manifest/`--identity` mismatch | `TRM2000`, exit 2. |
| Manifest I/O/access/strict decoding/JSON/version/field/type/range error | `TRM2001`, exit 2. |
| Invalid/incomplete origin, identity, producer declaration, export binding, complete expectation or registration shape | `TRM2001`, exit 2 as invalid submitted host configuration, not a fabricated source error. |
| Manifest aliases an output destination | `TRM2002`, exit 2, before output acquisition/replacement. |
| Invalid source lookup, schema use, constant evaluation or layout semantics | Existing located source diagnostics and exit 1; configuration does not reclassify them as successful admission. |
| Later unsupported emission/codec/target or output failure | Preserve the selected command's existing failure category/code/status and staged-publication behavior; no fallback emitter/schema/provider. |

Human configuration errors name the manifest path and, when established,
the offending JSON property/index path. Experimental `--json` uses the
existing input-error response and no fabricated source span; do not add
response fields or allocate a diagnostic code for each manifest property.
Existing `jit` successful execution still returns the program's actual status
without a compiler-success envelope and requires supervision.

### Absence, isolation and output preservation

Without the option there is **no layout registration**. The CLI cannot apply
the selected Layout/Blittable source semantics by spelling, matching enum
bits, native mode, `--corelib` or an ordinary provider reference. An unresolved
layout-looking annotation diagnoses under ordinary lookup/support rules.
A same-named ordinary user schema has no layout role; it follows its own
supported behavior or diagnoses unsupported use. Do not silently drop it.
Likewise, configured but unimported, shadowed or ambiguous names receive the
ordinary lookup outcome, not a privileged Core retry.

Unaffected source remains usable. Omitted Layout still means Auto under the
accepted target rules; core scalar layout and independently normalized
dependency layouts do not become unavailable merely because source-role
registration is absent.

Keep all configuration per invocation and using context. No process-global
provider/role cache, environment singleton or path-only cache may reuse a
selection. Capture immutable binding generations and all relied-on context;
file/content equality alone does not establish interchangeable checked handles.
Changing the manifest starts a new selection, not relabeling or replacement of
an already-bound program. Rebinding/replacement checks the retained originals
under its [existing owner](../specs/representation/binding-and-replacement.md).

On every option, configuration, source or prepublication lowering failure,
leave existing executable, DLL, TRRM and ABI-manifest outputs byte-for-byte
unchanged and create no successful artifact. Do not open/truncate destinations
or start a JIT worker before configuration admission. Use the existing
staged-output helpers after successful compilation/emission; this proposal
does not invent a multi-file atomic transaction or conceal an actual
publication/rollback failure. Report output failures truthfully.

## Normalization and persistence

Only existing normalized layout inputs and independently surviving semantic
dependencies reach emission. The assigned payloads remain exactly:

- `Layout`: `u32 ownerTypeDef, u32 targetProfileRef?, u8 layoutKind, u8 flags,
  u16 reserved=0, u32 alignment, u32 pack, u32 layoutFieldFirst,
  u32 layoutFieldCount`.
- `LayoutField`: `u32 ownerLayout, u32 ordinal, u32 fieldDef`.

Preserve existing layout-kind values, Blittable bit, declared-input omission
normalization, complete ordered field variants and target-derived
size/alignment/offsets. In particular, omitted Packing normalizes to 16 only
where applicable, omitted Alignment is not supplied zero, and explicitly
supplied controls stay supplied during checked source binding.

Do not serialize the configuration file, authority, host selection/snapshot,
raw source annotations or schema argument records into TRRM Layout rows,
Attr/debug data or a synthetic runtime Core dependency. Already normalized
source-free layout consumers do not reacquire this manifest. Raw enum/schema,
typed-value or required-evaluation obligations that independently survive still
need their complete supported carriers and original bindings; configuration
is not their publication codec. Consumers perform required structural,
support and binding checks, not a new semantic re-proof of producer bodies.

Packing/remapping preserves those obligations and rebuilds actual Module,
Import/Export, type, Layout/LayoutField and source/map references as required,
not byte/name substitutions. Output schema selection remains independent and
requires [exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts).
Neither the manifest's version 1 nor its provider schema tokens select a
TRRM decoder.

## Alternatives

| Choice | Assessment |
|---|---|
| No CLI support; require embedding hosts | Validly removes name-based CLI recognition, but leaves standalone source layout semantics unavailable even when the caller can explicitly authorize complete in-process providers. It does not meet the authorized CLI integration objective. |
| Embedded default provider or built-in test registration | Rejected: silently selects authority, origin, revisions and keys. Renaming it a compatibility provider or deriving identities from layout spellings does not repair exact registration. |
| **Recommended: explicit versioned host-definition manifest** | Gives the existing source commands a reproducible input for the checked carrier without provider defaults, plugins or missing artifact codecs. The caller must explicitly trust and supply complete producer inputs; the strict bounded grammar keeps that obligation visible. |
| Artifact-selector manifest only | Useful once complete checked enum/schema publication and exact provider delivery are available. It cannot reconstruct missing declarations today and must not accept role strings as fabricated artifact exports. Not selected as a second version-1 route. |

## Current gaps and integration destinations

These are inspected implementation gaps and proposed obligations, not claims
that a documentation change implemented the feature.

| Evidence/owner | Required work |
|---|---|
| [`Program.cs`](../../sources/terrumc/Program.cs), `Usage` and dispatch | The current working tree advertises the proposed option on the selected source commands. Verify the exact eligible set and that every excluded command rejects it rather than consuming or ignoring it. |
| [`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs), option parsing and `CompileTrees` | The current working tree captures the manifest before reading source, checks it against source/output paths, and passes its registration through the selected compilation factories. Active CLI regressions and final branch-by-branch validation remain required. |
| [`Program.TrRmExecution.cs`](../../sources/terrumc/Program.TrRmExecution.cs), `compile-trrm` | The current working tree accepts the source-only option, requires exact agreement with `--identity`, and keeps output-schema selection independent. Verify mismatch/no-publication behavior and that artifact commands remain unconfigured. |
| [`LayoutCoreManifestReader.cs`](../../sources/terrumc/LayoutCoreManifestReader.cs) | The current working tree implements the closed version-1 JSON producer and exact declaration projection. Validate strict decoding, duplicate/unknown fields, escaped-name handling, all registration shapes and errors through active CLI tests; this is not an artifact-backed provider decoder. |
| [`NativeCoreRegistration.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeCoreRegistration.cs), [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs) and [source-layout carrier](source-layout-core-registration.md) | The layout-only set, options/reference/bound-program hand-off and complete shape checks are implemented in the current working tree. Preserve exact origins, immutable expectations and duplicate-selection constraints; no native registration or external provider resolver is supplied by this manifest. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), `IsNoArgumentBlittableAttribute` and `TryDecodeLayoutAttribute` | The current working tree consumes exact selected Layout/Blittable schemas and checked controls. Keep unregistered name lookup from granting these roles; verify ordinary constant evaluation and rejection cases remain covered. |
| [Layout support inventory](../implementation/layout-profiles.md), [hosted compiler reference](../implementation/terrum-code-analysis.md), [experimental CLI reference](../implementation/cli-protocol.md) | Update supported entry points and limitations after CLI tests pass. Accepted packing/alignment/layout support, native ABI and execution qualification remain separate gates. |

On adoption, the CLI option/manifest grammar belongs in the experimental CLI
reference/help, with the carrier API in the hosted compiler reference.
The target, grammar, Core inventory, Module binding and TRRM chapters remain
the semantic owners. Only the native-configuration deferral identified above
is intentionally superseded; no existing proposal or normative chapter is
edited here.

## Required CLI acceptance cases

These are required tests, **not yet run against the current patch**. The
working tree now contains active CLI regressions for configured `analyze`,
Standard `compile-trrm`, strict manifest failures, output alias preservation,
identity mismatch, syntax-only command rejection, and duplicate/invalid option
forms. Use the public CLI and
existing [CliTests runner](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.cs)
with explicit synthetic authorized producers and unrelated provider/key/name
choices. Confirm active test inclusion, matched counts and final compiler
inputs; parsing a manifest is not emission or execution evidence.

| Cases | Required observations |
|---|---|
| Each of the six eligible commands, file/stdin source and multiple `--source` trees; `compile-trrm --library` | Capture one explicit using context and exact layout set. Inspect normalized facts through API/Standard metadata where supported; no native registration prerequisite or hidden provider. Existing unsupported emitters still refuse rather than claim success. |
| One provider and split enum/schema providers; arbitrary actual qualified names, explicit member keys and non-value-sorted member order | Exact roles bind through ordinary qualification/imports. Preserve producer order, origins and keys; no namespace/key/ordinal inference. |
| Import present/absent, qualified names, independently supported aliases, local shadows, ambiguous imports and different tree import scopes | Ordinary resolution decides identity. Unimported/foreign/ambiguous declarations get no Core retry, identity retagging or merged lexical scope. |
| No manifest with unaffected source, then with layout-looking source; same-named user schema; native-only registration or `--corelib` | Unaffected supported source remains usable. No selected source layout/Blittable role is granted by spelling or another configuration family; ordinary user-schema behavior retains its independent support rules. |
| Unknown command use; every excluded command family; repeated option including the same path; missing/empty/dash operand; equals form, casing and extra operands | Usage error before source stdin consumption, output writes or program launch. |
| Missing/inaccessible/directory file; invalid UTF-8; one BOM versus repeated BOM; malformed/trailing JSON, comments, trailing comma or unpaired surrogate | Only the permitted single signature is accepted; failures use input/configuration diagnostics without fabricated source spans. |
| Missing/null/unknown properties at every object depth, escaped duplicate names, scalar/array type errors and unsupported `schemaVersion` | Reject the complete manifest, even on source without layout uses; no first/last-field wins, ignored extension fields or partial set. |
| Numeric boundaries, negative unsigned and signed overflow, fractions, exponent notation, numeric strings and missing versus explicit zero | Exact checked parsing/domain validation. Never round/truncate, fill zero requirements or infer a schema revision. |
| Duplicate provider entries/revisions, unused/missing origins, using/provider collision, wrong-context identities or `--identity` mismatch | Reject without selection-order preference or publication relabeling. Multiple roles referencing one valid provider remain permitted. |
| Wrong enum kind/base/access/primary/arity/Flags; missing/extra/duplicate/wrong-valued case; wrong member owner/provider/key | Reject whole registration before binding, including when the particular source argument would have used an apparently valid member. |
| Changed schema label/type/default/positional permission/targets/primary/repetition; reused Layout/Blittable identity; missing/null versus initialized empty arguments | Reject instead of manufacturing the expected schema or accepting a role label. |
| Kind positional/named and ordinary checked constant forwarding; integer/foreign-enum equal bits, unnamed selected value, failed/exhausted/cyclic required evaluation | Preserve exact nominal typing and ordinary evaluation dependencies/outcomes; no token-only evaluator, numeric fallback or declaration-order repair. |
| Packing 1/2/4/8/16 and Alignment power-of-two boundaries 1..8192; zero, nonpowers, overflow, weaker nested alignment, Auto/Transparent controls | Apply the unchanged target rules, including omitted versus explicitly supplied defaults, transitive alignment and no silent weakening. |
| `--corelib`, source-grant/ABI pair, native-import mode, arithmetic/runtime and lowering-agreement combinations | Registration does not widen existing option or target admission. Only supported combinations proceed; it registers no native schema or unsafe authorization. |
| Every prepublication failure with existing EXE/DLL/TRRM/ABI sentinel outputs; manifest/output equal path, relative/case/reparse/hard-link alias where supported | Outputs remain byte-identical and no program starts. Exercise actual file identity, not only textual path equality; preserve truthful output-error handling on publication failure. |
| Invalid configuration with unread stdin; repeated invocations/change of manifest contents; concurrent Modules with equal spellings but different origins | Reject before blocking on source; no stale path cache, cross-context handles, global role leakage or environment/default-provider selection. |
| Selected source normalization followed by source-free TRRM build/load without configuration; retained raw enum/evaluation dependencies; row remapping/replacement | Supported normalized layouts need no manifest. Preserve every independent original obligation, decline unavailable codecs and incompatible replacements, and never serialize host authorization or source annotations as missing semantics. |
| Positive native build and JIT cases after binding/metadata cases pass | Use headless supervised execution and existing cleanup/deadline patterns. Record emission separately from actual geometry/access/runtime results; no visible-window or hardware opt-ins. |

Implementation depends on the checked origin/producer adapter, the proposed
layout carrier, ordinary schema/enum binding and downstream accepted layout
support. No further discovery/default/authority policy is left undecided by
this recommendation. Actual authorized identities, revisions, keys and
contract-schema selections remain required deployment inputs, not constants
for an implementer to invent. Completing this CLI slice does not by itself
complete `source-layout-packing-alignment` or compiler adoption.
