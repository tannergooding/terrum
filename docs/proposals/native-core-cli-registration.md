# Explicit CLI configuration for native Core registration

**Status: unapproved candidate; provisionally actionable only under the user's
authorized missing-contract workflow.** This selects a proposed CLI input,
not a production provider, normative amendment, implementation or conformance
result. Later user review remains required.

## Finding and recommendation

The missing decision is **CLI delivery**, not a replacement identity,
registration authority or enum contract.
[Native Core provider configuration](native-core-provider-configuration.md#host-input-and-adapter-obligations)
selects an explicit hosted input and authorized in-process adapter, but
expressly adds no CLI option or configuration-file grammar.
[`CompilationOptions.NativeCoreRegistration`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs)
is the available hosted hand-off;
[`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs)
and [`Program.TrRmExecution.cs`](../../sources/terrumc/Program.TrRmExecution.cs)
currently deliver Layout configuration, not a native selection.
Keeping name-based NativeImport/NativeCallable recognition or a NativeAbi
function-pointer prefix cannot satisfy the selected source contracts.

Recommend one optional, nonrepeatable option:

```text
--native-core-manifest <path>
```

Select the exact JSON grammar below as **explicit trusted host input to a
closed in-process native declaration producer**. Produce three complete enums
and the NativeImport, NativeCallable and NativeCallConv schemas, bind their
actual exports and complete original expectations in one using-Module
snapshot, and pass the admitted set through
`CompilationOptions.NativeCoreRegistration`. No declaration, schema fact,
authority, provider, revision or key is supplied by a built-in default.

### Scope and narrow supersession

For these three native schemas and the eligible commands below, intentionally
supersede only the prior provider-configuration proposal's **CLI delivery
deferral** and its decision not to select a serialized CLI input. Its hosted
API, authorization, checked-producer, exact-origin, original-expectation,
replacement and source-free-consumption rules remain unchanged. Embedding
hosts need not use this file.

Do **not** extend or reinterpret the
[Layout manifest](source-layout-cli-registration.md). Its version-1 grammar
and separately proposed command extensions remain Layout-only. Shared lexical
helpers and one shared binding snapshot do not combine the file formats.

**NativeExport is outside this version-1 input.** Its separate
[source/Core schema candidate](native-export-source-schema.md) does not
extend the CLI manifest grammar. The
[registration hand-off](native-calling-convention-core-registration.md#proposed-registration-hand-off)
requires its independently complete schema *where used*; the
[source-schema candidate](native-calling-convention-source-schema.md#candidate-core-registration-and-source-arguments)
does not select its complete source arguments. The current
`NativeCoreRegistrationSet.Create` requires an Export descriptor and its
compact native schema carrier lacks complete argument/placement/default
records. Those implementation shapes cannot justify manufacturing an Export
marker or inferring the other schemas. Implement the already-selected
role-required hand-off with a complete I/C/P closure and an absent Export
role; refuse affected source NativeExport use. No compatibility constructor,
dummy export, partial admitted schema or new Export argument is selected here.
Existing CLI `--export`/`--export-all-public` controls keep their independent
host-emission meaning; they are not source NativeExport registration.

## Exact option and command contract

The spelling is case-sensitive. Consume exactly one following argv element,
once, after the command's required positional inputs among its existing
options and before an optional final `--json`. Reject aliases, equals syntax,
repetition even with the same path, extra operands, an empty operand, `-`,
and an operand beginning with `-`. An explicit relative/absolute path can
name a dash-leading file.

| Command | Proposed eligibility |
|---|---|
| `analyze`, `build`, `build-native-import`, `build-library`, `jit` | Accept for their one source compilation, including every `--source` tree. |
| `compile-trrm`, including `--library` | Accept for source binding; require ordinal equality of `usingModuleIdentity` and the existing required `--identity`. Keep `--trrm-schema` and runtime selection independent. |
| Every other command/form | Reject as usage error, before reading source/artifacts or starting execution. |

Explicit exclusions include syntax-only `check`/`tokens`/`tree`;
`compile-module` producer/consumer; `compile-native` producer/consumer,
`compile-native-set` and `compile-native-graph` producer/consumer;
scanner/component and frontend commands; and all source-free
build/run/validation/catalog/resolution commands. Existing Layout eligibility
does not extend native eligibility. Excluded source hosts remain usable for
otherwise supported unconfigured source, but gain no native source roles.
Extending one of them requires an explicit route amendment, not a shared
parser accidentally accepting an ignored option.

Preserve every existing option restriction, including required destinations,
runtime/arithmetic policies, Core-library mode, source-grant/ABI pairing,
native-import mode and native-lowering agreements. Reject composition with
the internal `--frontend-command` route rather than forwarding configuration
into a differently scoped frontend invocation. Configuration authorizes
neither a native import in a factory that disallows it nor missing emission,
dispatch, layout, value, codec or ABI support.

Resolve the path once against the invocation working directory, not a
source/provider directory. Require a regular file; no extension is required.
There is no stdin, URL, directory, include, artifact, package, plugin or
environment form and no automatic file search. Capture and admit configuration
before consuming source stdin. `--source-name` remains a diagnostic label.

All source trees share the explicitly supplied one using logical Module;
their lexical scopes remain separate. No source filename, output stem,
namespace, native library name or command mode supplies that identity.
Provider identities cannot equal this still-unbound source Module.
`compile-trrm` identity disagreement rejects before publication, not by
relabeling an already-bound program.

## Authority and closed producer

Passing the option is the CLI host's explicit designation of this file as
configuration from its authority and authorization of the **closed native
declaration producer** for these records. The host must establish that
decision outside the source/package/artifact being compiled.
`coreAuthorityIdentity` records it; text, file hashes, signature-looking
fields and provider self-assertions do not authenticate it.

Execute no file-supplied code and load no provider assembly, DLL or adapter.
The closed producer creates actual checked logical provider snapshots and
their explicit logical export maps from the complete definitions below.
It does not claim that absent artifact definitions/exports exist. The
host/producer remains responsible for truthful declarations and legitimate
origins; compiler shape and binding checks do not authenticate arbitrary
host assertions.

Each provider's schema token explicitly names the in-process declaration
contract being produced. This manifest grammar fixes its supported projection;
a different token cannot change that grammar, infer a schema from a source
name or dispatch an artifact decoder. No production token/revision is supplied
here. Artifact-backed providers require a separately admitted exact delivery
route and complete declaration codec; this version has neither.

## Exact version-1 JSON grammar

Every object has exactly its listed properties, all required; no field is
filled from an omitted value. Object order is insignificant. Only Argument
`name`, `enumType` and `default` admit null under the combinations below.
Set arrays have no duplicates and insignificant order; all other arrays
preserve supplied declaration/projection order.

Require strict UTF-8, allowing one leading UTF-8 signature, and one root
object followed only by JSON whitespace. Reject invalid UTF-8, repeated
signatures, unpaired decoded surrogates, comments, trailing commas/JSON,
unknown properties and duplicate decoded property names at every depth,
including escaped/literal duplicates. Names and tags compare ordinally,
case-sensitively.

`Text` is well-formed decoded Unicode, nonempty, not whitespace-only and
without NUL. Preserve it without trimming, case folding, Unicode normalization
or path interpretation. Source names additionally obey ordinary naming rules.
`u32` is an unsigned JSON integer token in `0..4294967295`, without sign,
fraction or exponent; `i32` permits minus and the exact signed 32-bit range.
Reject numeric strings, conversions, overflow, rounding and truncation.

### Root, origins and identities

| Root property | Exact value |
|---|---|
| `schema` | String exactly `"terrumc.native-core-manifest/1"`, the whole configuration grammar selection. |
| `coreAuthorityIdentity` | `Text`, explicitly designated by the host. |
| `usingModuleIdentity` | `Text`, the one using logical Module. |
| `providers` | Array of one through six Origin objects; exactly the distinct providers used below, with no unused entry. |
| `callingConventions` | One complete EnumDefinition for E below. |
| `resolutionModes` | One complete EnumDefinition for M below. |
| `resolutionPolicies` | One complete EnumDefinition for R below. |
| `nativeImport` | One complete SchemaDefinition for I below. |
| `nativeCallable` | One complete SchemaDefinition for C below. |
| `nativeCallConv` | One complete SchemaDefinition for P below. |

Origin has exactly `moduleIdentity: Text`, `revisionIdentity: Text`,
`schema: SchemaSelection`, `governingSemanticEdition: u32`,
`targetRequirement: u32` and `runtimePolicy: u32`. SchemaSelection has exactly
`identity: Text` and `revision: u32`, with nonzero revision.
Use the Module-origin carrier's admitted edition/requirement/policy domains;
for this version require governing edition 1 and target requirement/runtime
policy each in their existing `0..1` domains. Unsupported selections reject.
Producer revision, declaration-contract
schema/revision, governing edition, Module requirements and output artifact
schema are separate facts. Missing input is not zero or `"current"`.
Repeated provider identities reject even when equal, as do competing revisions.

Identity has exactly `providerIdentity: Text`, `exportKey: Text` and `kind`,
where `kind` is `"enum-type"`, `"enum-member"` or `"attribute-schema"`.
The provider must equal exactly one Origin's Module identity. Top-level keys
select actual logical exports of the required kind; changing kind cannot
reuse one provider/key for different declarations.

On a member, `exportKey` is the existing member identity component, not an
independent Module export. Its actual defining-enum relationship and explicit
producer member key are retained below. Do not derive any key from a name,
numeric value, ordinal, another key or string prefix.

### Complete enum snapshots

EnumDefinition has exactly `identity: Identity`, `qualifiedName: Text`,
`underlyingType: "int32"`, `members: Member[]`, `genericArity: u32`,
`isPublic: Boolean`, `isPrimary: Boolean`, `isFlags: Boolean` and
`blittableTargetRequirement: u32`.
Require enum-type identity, arity 0, public/primary true and Flags false.
The last field is an explicit declaration representation commitment, 0 for
absent or the existing Windows x64 requirement 1, not inferred from the
provider Module requirement.

Member has exactly `identity: Identity`, `declaringEnum: Identity`,
`declarationKey: Text`, `name: Text`, `value: i32` and `isPublic: Boolean`.
Require enum-member identity, the containing enum's provider and owner,
public true, and unique member identities across the required closure.
Member declaration keys, names and values are unique within each defining
enum, not across distinct enums. Preserve array order as actual declaration ordinal.
Member declaration keys are enum-owned, not Module export keys.

| Slot | Exact proposed source-qualified name | Complete required public members |
|---|---|---|
| E | `Terrum.Interop.NativeCallingConvention` | Unmanaged=1, Cdecl=2, Stdcall=3, Vectorcall=4 |
| M | `Terrum.Interop.NativeImportResolutionMode` | Link=1, Dynamic=2 |
| R | `Terrum.Interop.NativeImportResolutionPolicy` | Default=1, CompatibilitySearch=2 |

These public shapes are the existing **unapproved source-schema direction**,
not new standard wire assignments. Require each whole member set with no
extra case, alias or missing definition; declaration order need not be
numeric order. Qualified names are complete checked declaration inputs, not
authorization or substitutes for selected identity.

### Complete schema snapshots

SchemaDefinition has exactly `identity: Identity`, `qualifiedName: Text`,
`role`, `arguments: Argument[]`, `placement`, `targets: Text[]`,
`genericArity: u32`, `isPublic: Boolean`, `isPrimary: Boolean` and
`isRepeatable: Boolean`. Require attribute-schema identity, arity 0,
public/primary true and repeatable false; distinct top-level identities/keys.
`targets` is a set of the exact tags below.

| Slot | Exact qualified name / `role` | Exact `placement` / `targets` |
|---|---|---|
| I | `Terrum.Interop.NativeImport` / `"native-import"` | `"primary-declaration"` / `"static-extern-callable"`, `"static-extern-data"` |
| C | `Terrum.Interop.NativeCallable` / `"native-callable"` | `"primary-declaration"` / `"ordinary-method"`, `"interface"` |
| P | `Terrum.Interop.NativeCallConv` / `"native-callconv"` | `"immediate-fnptr-type-use"` / `"fnptr-type-use"` |

These tags are configuration projection tags, not new source targets.
They carry the source-schema owner's complete applicability restrictions:
ordinary functions count as methods; I remains static/bodyless and C excludes
constructors/operators/accessors and other nonmethod declarations. C interface
defaults, primary/partial placement, inherited-member identity, explicit
method overrides, role conflicts and target admission remain unchanged.

Argument has exactly `name: Text|null`, `kind`, `enumType: Identity|null`,
`isRequired: Boolean`, `allowsPositional: Boolean`, `allowsNamed: Boolean`,
`contexts: Text[]` and `default: Default|null`.
`kind` is `"library-name-literal"`, `"entry-name-literal"` or `"enum"`.
Literal kinds denote the existing compile-time literal domains, not runtime
text or constructor arguments; only enum kind has a nonnull enumType.
`contexts` is a set selecting exactly where supplying/defaulting that argument
is permitted.

Default is exactly either `{ "kind": "declaration-name" }`, or an object with
exactly `kind: "enum-member"` and `member: Identity` selecting the actual
member of the argument's exact enum. Null means no default, not integer zero.
No literal default, expression/program or name-based member selector is admitted.

In the next table, A abbreviates the four context tags `"callable-link"`,
`"callable-dynamic"`, `"data-link"`, `"data-dynamic"`; D abbreviates the two
dynamic tags; F abbreviates the two callable tags; Cx is `"method"`,
`"interface"`; Px is `"fnptr-type-use"`. Actual JSON supplies those complete
tag arrays, not these abbreviations.

| Slot/order / `name` | `kind` / `enumType` | Required / positional / named | `contexts` / `default` |
|---|---|---|---|
| I/0 `LibraryName` | library-name-literal / null | true / true / true | A / null |
| I/1 `EntryName` | entry-name-literal / null | false / true / true | A / declaration-name |
| I/2 `ResolutionMode` | enum / M.identity | false / false / true | A / exact M.Link member identity |
| I/3 `ResolutionPolicy` | enum / R.identity | false / false / true | D / exact R.Default member identity |
| I/4 `CallConv` | enum / E.identity | false / false / true | F / exact E.Unmanaged member identity |
| C/0 `CallConv` | enum / E.identity | false / false / true | Cx / exact E.Unmanaged member identity |
| P/0 null | enum / E.identity | true / true / false | Px / null |

Each argument array contains exactly these records in this projection order.
The I positional prefix is LibraryName/EntryName; ordinary named-source
ordering remains unrestricted. P has the already-selected one explicit
positional argument: this carrier invents no source argument label or named
overload. Validate every submitted record, rather than constructing missing
records from a role tag or qualified name.

Import contexts are determined by the actual declaration kind and checked
M selection. On Link, ResolutionPolicy is forbidden, including explicit
Default; on data, CallConv is forbidden, including explicit Unmanaged.
Apply defaults only within their allowed context. Preserve
[LibraryName's selected literal/value direction](native-import-library-name-source-contract.md#exact-candidate-validation),
exact EntryName/declaration-name and metadata String rules, ordinary argument
mapping, nominal typed constants and required evaluation. This file contains
no per-import library/symbol values, loader search paths or executable policy.

## Admission and coexistence with Layout

1. Validate eligibility, all options and destination constraints. Resolve
   paths once. Refuse manifest aliases with any source, output, ABI output
   or the other manifest using the shared target path/file-identity relation,
   not merely argv text; inability to establish required identity is an error.
   Capture configuration bytes once, without opening/truncating outputs.
2. Strictly validate the whole supplied native file and its complete
   definition inputs even if source uses none of its roles. Check every
   enum/member/default relationship; do not publish context-local handles
   before composing any explicitly supplied Layout input.
3. If Layout is also explicitly supplied, fully admit its unchanged file
   grammar. Require ordinal-equal authority and using-Module identities.
   Each file must have no internal duplicate/unused provider; a provider shared
   across files must agree in **every** Origin field. Combine agreeing origins
   and distinct complete definition inputs. Conflicting revisions/schema,
   declaration/key/owner claims or requirements reject both selections.
4. Construct each actual checked logical provider snapshot and export map
   once from the combined inputs. Resolve provider/key/kind before contract
   comparison and capture the immutable complete original expectation graphs
   through the existing checked Module binding path, creating **one
   `CoreProviderModuleBindings` object**. Do not add a second resolver or
   transplant handles from independently created equal snapshots.
   Validate the complete required native I/C/P set, and Layout set if supplied,
   against that same object. Publish neither on any admission failure.
   Pass each family once through its options property; options/reference
   duplicate selection still rejects even when equal. Native-only supplies
   no Layout role; Layout-only supplies no native role. Neither file may
   silently load, replace or manufacture the other's configuration.
5. Bind source with ordinary scoped lookup and typed constant evaluation,
   then normalize once and preserve facts through shared verification,
   rewriting, dependency binding, persistence and lowering. No injected
   using, token rewrite, privileged terminal-name retry, enum retagging,
   schema inference or relaxed unsafe/backing/ownership/lifetime obligation.

Capture both files in option order before source stdin; no process-global,
environment, path-only or content-only registration cache. Changing a file
creates a new checked generation, not replacement/relabeling of old work.
Keep complete enum/member order/keys, argument/context/default/placement
expectations and every independently retained value/evaluation dependency.
Replacement checks each original graph; packing/remapping resolves actual
references and owners before rebuilding affected records/maps.

Without the native option there is **no native registration**. Neither
`build-native-import`, `--corelib`, an ordinary reference, matching source
names nor equal enum integers selects one. Affected source binding diagnoses;
unaffected source, unannotated Terrum-convention function pointers and
supported independently normalized imports remain usable.
A two-string NativeImport prefix and bare NativeCallable remain legitimate
*only when ordinary lookup selects their admitted schemas*; their source
omission rules cannot bypass missing configuration. NativeAbi/Native prefix
aliases remain rejected even with configuration.

## Failures, publication and protocol isolation

| Failure | Proposed existing experimental CLI projection |
|---|---|
| Ineligible command, option spelling/arity/duplication/operand/combination, or `compile-trrm --identity` mismatch | `TRM2000`, exit 2. |
| Manifest I/O/access/strict decoding/schema/field/type/range error; invalid origin, declaration, binding, complete expectation or registration composition | `TRM2001`, exit 2; invalid host configuration, not a fabricated source error. |
| Manifest aliases another input/configuration/output | `TRM2002`, exit 2, before output acquisition. |
| Invalid source lookup/schema use/typed constant/semantic contract, including absent required registration | Existing located source diagnostics, exit 1; preserve ordinary failure/exhaustion distinctions. |
| Later unsupported target/emitter/codec, artifact admission or output failure | Preserve that command's existing category, status and staged-publication behavior; no alternate provider/schema/emitter. |

Configuration errors identify the manifest and, when established, JSON
property/index path without fabricated source spans. Human and experimental
`--json` responses use existing input-error/result writers; do not add response
fields or echo configuration/declaration records to stdout. Parsing or
producer admission emits no progress, source input, program bytes or success
envelope. Invoke the built CLI, not `dotnet run` mixed build output.
JIT remains a supervised worker returning the actual program status under
its existing experimental contract.

This proposal does **not** amend
[`terrum.cli/1`](../specs/cli-protocol.md). Its input catalog has no host
configuration kind; do not disguise this file as source or a TRRM artifact,
infer machine selection from `--json`, or claim new machine support.
Until a separately selected configuration-input protocol contract exists,
decline this option composed with `--machine terrum.cli/1` before affected
input processing, publication or execution, under the protocol's selection
failure rule. Preserve protocol-output isolation in any future admitted route.

On option/configuration/source/prepublication lowering failure leave existing
EXE/DLL/TRRM/ABI outputs byte-for-byte unchanged and launch no program.
Use existing staged publication only after successful admission/emission;
report actual publication/rollback failures rather than promising a new
multi-file atomic transaction.

Serialize only supported normalized conventions, resolution facts, roles and
independently surviving semantic dependencies under the independently selected
destination schema. Serialize no host authorization, configuration file,
registration snapshot or inferred runtime Core dependency. Source-free
consumers need no manifest merely to consume normalized native contracts.
Retained raw enum/schema/value/evaluation obligations still require their own
complete admitted codecs and original bindings; this JSON grammar supplies none.

## Alternatives and owner integration

| Alternative | Assessment |
|---|---|
| Hosted API only | Validly rejects unconfigured source, but does not supply the required standalone CLI delivery. |
| **Recommended: separate explicit native host-definition file** | Reuses the selected checked-producer hand-off without unavailable artifact schema codecs; makes every authority/origin/declaration input explicit and preserves Layout independence. |
| Artifact-selector-only file or dynamically loaded adapter | Needs separately admitted complete provider delivery/codecs or executable authorization. Not a second version-1 route. |
| Built-in/test provider, package discovery, Layout-file extension or spelling/value fallback | Rejected; selects unsupplied authority/identity/schema or crosses the existing family boundary. |

On adoption, option/JSON/help belong in the experimental CLI reference and
the complete carrier projection in the hosted compiler reference, **after
implementation and qualification**. Preserve these owners without normative
edits in this proposal:

- [Native declaration roles](../specs/native.md#native-declaration-roles),
  [attribute/type grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations),
  [source schemas](native-calling-convention-source-schema.md) and
  [registration hand-off](native-calling-convention-core-registration.md):
  exact roles, nominal domains, source forms/defaults, caller-unsafety and
  normalization; no new convention, symbol schema or allocation.
- [Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity),
  [Module origins](core-provider-module-origin-carrier.md#proposed-host-input-and-adapter-shape)
  and [binding/replacement](../specs/representation/binding-and-replacement.md):
  actual selections, one originating snapshot and complete retained originals.
- [Reader trust](../specs/representation/reader-trust.md),
  [persistence](../specs/representation/persistence.md) and
  [exact draft schemas](../specs/representation/format-versioning.md):
  structural/support/binding checks and producer truth, not re-proof of
  producer bodies, reconstructed declarations or compatibility decoding.
- [Windows call interface](../specs/targets/windows-x64-call-interface.md):
  independent physical support and refusal of unsupported crossings.

## Required acceptance cases and remaining inputs

These are acceptance requirements, not a claim that the full matrix is
implemented or qualified. Partial current evidence is recorded in the
[conformance-gap inventory](../implementation/conformance-gaps.md#cg-023-cli-and-source-input-conformance);
it does not approve this candidate or close its remaining gates. Use the
public CLI and existing
[CliTests runner](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.cs),
explicit synthetic authorized producers with unrelated Module/key choices,
and active test inclusion. Qualify binding, metadata, emission and execution
separately against final compiler/configuration inputs.

| Case group | Required observation |
|---|---|
| All six eligible commands, `compile-trrm --library`, file/stdin/multitree source | Same exact using-context selection reaches the correct options-bearing factory. Supported binding succeeds; unsupported native mode/target/schema still declines truthfully. |
| One provider and split enum/schema providers; nonnumeric member order, arbitrary actual export/member keys | Bind real selected identities and preserve complete snapshots/order; no inferred keys, provider search or privileged source-name identity. |
| I positional/named prefix, C bare/explicit CallConv, P explicit value; ordinary nominal ConstEval forwarding | Apply complete schemas and typed evaluator. Binding distinguishes all four conventions even where machine carriers coincide; no runtime literal/string conversion. |
| No option, Layout-only, `--corelib`, same-named declarations/foreign enums, unimported/shadowed/ambiguous names | Unaffected source works; no native role/default/provider retry. Bare C and two-string I without registration fail; NativeAbi prefixes never become aliases. |
| I missing/extra/duplicate arguments, third positional value, positional C convention, P missing/named/extra argument or wrong placement | Source diagnostics, not ignored arguments or schema repair. Dynamic/Link and callable/data context restrictions remain exact. |
| Exact LibraryName/EntryName rules; field CallConv, Link ResolutionPolicy, zero/unknown/foreign enum values; failed/exhausted required evaluation | Refuse the actual invalid use; preserve literal decoding, nominal type, omission and failure boundaries. |
| Complete interface defaults, explicit method overrides, inherited identity, role conflicts and witnesses where supported | Registration neither changes effective-contract rules nor supplies missing interface/export/ABI support. |
| Any `nativeExport` JSON field or source NativeExport use; compact/dummy Export factory accommodation | Unknown-field/configuration or affected source failure; no manufactured Export schema. Existing CLI host export controls remain independently usable. |
| Excluded command families and frontend composition; casing/equals/repeat/empty/dash/extra operands | Usage failure before stdin blocking, artifact reading, output modification or launch, even when Layout is independently eligible there. |
| Missing/inaccessible/directory file; UTF-8/BOM/surrogate/JSON failures; unknown/missing/null/escaped-duplicate fields at every depth | Whole configuration rejects, including source with no native use; no partial set or first/last field wins. |
| Numeric bounds/sign/fraction/exponent/string cases; unsupported schema/edition/requirements; missing versus explicit valid zero | Exact decoding/admission, without defaults, rounding, revision inference or alternate decoder. |
| Duplicate/unused/missing provider, competing revision, using/provider collision, wrong kind/key/member owner, incomplete enum/schema/default/context records | Reject complete closure before binding; do not validate only the currently used member or infer missing schema records. |
| Native-only, Layout-only and both; equal/unequal authority/using/origin fields, colliding exports and different snapshots | Independently valid families share one real combined snapshot only when selections agree; either failure preserves all outputs. |
| Manifest/source/output/ABI/other-manifest aliases, including case/reparse/hard-link cases; existing sentinel outputs | Actual file-identity checks; every prepublication failure preserves bytes and starts no worker. Truthfully report publication failure. |
| Repeated/concurrent invocations and changed file contents | No process/path cache, cross-context handles, stale original expectations or configuration leakage. |
| Human/`--json` success and every failure; unread stdin; unsupported machine composition | No producer/build chatter or extra stdout payload; existing experimental responses/statuses remain distinct from the selected machine protocol. |
| Normalization followed by source-free loading without files/configuration; surviving enum/evaluation dependencies; checked remapping/replacement | Normalized supported consumers need no source registration; preserve every independent original obligation and decline missing codecs/incompatible replacements. |
| Supported headless native-import AOT and JIT probes after binding cases | Record actual supervised result separately from emission, with existing deadlines/cleanup. No incidental UI/hardware opt-ins or sample-specific producer policy. |

No delivery-format, discovery, default-provider or authority-selection policy
remains undecided within this scope. The host must still supply its actual
authority designation, authorized provider origins/revisions/contract-schema
selections, export/member keys and complete declaration inputs. None is a
compiler constant or production provider selected by this document.

Complete native schema snapshots, role-required carrier admission, ordinary
binding/evaluation and CLI routing remain implementation work.
NativeExport's complete source schema, excluded source-host extensions and
raw enum/schema publication remain separate gates, not hidden defaults or
claims that this proposal completes native conformance.
