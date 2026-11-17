# Explicit CLI evaluation-form schema selection

> [!IMPORTANT]
> **Status: UNAPPROVED; provisional direction only; non-normative.**
> The parent task explicitly authorizes provisional implementation pending
> review. That direction does not accept this proposal or its prerequisite.

## Gap, scope and owners

Select one host-only input, **`--evaluation-core-manifest <path>`**, delivering
the [provisional Pure/ConstEval identity selection](core-pure-schema-identity-selection.md)
without changing its semantics or authority boundary.

The reported Debug build compiles the hosted C# compiler, then fails on
`[ConstEval]` in the Terrum-authored primitives. The identity-based binder now
requires explicit selection, but [`Program.cs`](../../sources/terrumc/Program.cs)
and [`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs)
expose only Layout, NativeCore and NativeOutput manifests.
[`terrumc.csproj`](../../sources/terrumc/terrumc.csproj)'s two native primitive
targets invoke `build-library` with Layout configuration only.
The hosted catalog/selection types exist; restoring spelling recognition is
not the missing implementation.

| Owner | Preserved authority |
|---|---|
| [Checked annotations and forms](../specs/language/published-predicates-and-effects.md#checked-annotations-and-forms), [LCI-24 and Core identity](../specs/libraries/adopted-inventory.md#1-status-and-identity) | Exact designated schemas, independent Pure/ConstEval slots, primary authorship, targets, multiplicity and checked evaluation semantics. |
| [Names and binding](../specs/language/names-and-binding.md), prerequisite proposal | Ordinary lookup of actual declarations; exact local catalog or checked ordinary-reference exports, not matching names or consumer expectations. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md), [logical evaluation records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms), [reader trust](../specs/representation/reader-trust.md) | Retained original contexts/expectations, producer truth, safe consumer checking and independent codec gates. |
| [Tooling protocol](../specs/cli-protocol.md), [format versioning](../specs/representation/format-versioning.md) | No response-envelope amendment, artifact schema selection or serialized authority grant. |

This fills only the prerequisite's separately deferred CLI/manifest decision.
It supersedes no accepted rule and adds no source syntax, runtime promise,
metadata record, registry assignment or general provider loader. If approved,
document the host option/build configuration in the existing CLI/building
references; normative semantics remain with these owners.

## CLI and build-target contract

The case-sensitive option consumes exactly one following argv element, once,
among existing options before an optional final `--json`. Reject repetition
even with the same path, equals syntax, missing/empty operands, `-` and
dash-leading operands. An explicit relative/absolute path can name a
dash-leading file. Resolve against the invocation working directory; require
a regular file; no extension is required. No search, include, URL, environment
or stdin form.

| Command | Applicability |
|---|---|
| `analyze`, `build`, `build-native-import`, `build-library`, `jit` | One selection for the complete source compilation, including every `--source`; retain existing `--corelib`, source-grant/ABI, runtime, arithmetic, import and lowering restrictions. |
| `compile-trrm`, including `--library` | Source binding only; `usingModuleIdentity` must equal the required `--identity` ordinally before binding. `--trrm-schema` and output support remain independent. |
| All other forms | Usage error before source/artifact reads or execution. Includes syntax commands, component/frontend commands, private module/native/set/graph commands and source-free TRRM routes. |

Reject combination with internal `--frontend-command` or selected
`--machine terrum.cli/1`; this proposal adopts no configuration input in that
protocol. Human and experimental `--json` retain existing writers/fields.

All trees, including stdin, belong to the one explicitly supplied using
logical Module, while preserving their ordinary lexical scopes. Capture
configuration before consuming source stdin and bind it with the immutable
source/options/reference snapshot. `--source-name` is only a diagnostic label.
Where another input/publication selects the Module identity, require exact
agreement; never relabel a bound program. Separate Modules require separate
invocations/selections, not per-file overrides or an order-dependent winner.

Each native primitive target must explicitly supply its manifest path from
project configuration and pass this switch. Track that file and the selected
path in incremental input identity; changed/removed configuration cannot reuse
a previously successful DLL. Validate required configuration even when an
output exists; do not skip the target to hide missing input. The targets may
share a file only for an intentionally shared using identity with compatible
origins; each invocation captures its own source binding snapshot.
A project-selected development fixture is configuration, not a compiler or
`--corelib` default. No new MSBuild identity-inference rule is introduced.

Omission supplies no evaluation selection. Otherwise supported unconfigured
paths remain usable, but required authored/retained schema dependencies still
diagnose or decline. Neither omission nor a null role makes a required
annotation inert; same-named user attributes grant no evaluation capability.

## Exact host manifest

Select the whole grammar with **`schema: "terrumc.evaluation-core-manifest/1"`**.
This token identifies host configuration, not a Terrum artifact/catalog codec.
Every object has exactly the properties below, all required; only the three
root fields explicitly marked nullable permit null. Property order is
irrelevant. Require strict UTF-8 with at most one leading signature, one JSON
root and trailing whitespace only. Reject comments, trailing commas, invalid
scalar text, unknown properties and duplicate decoded properties at any depth.
Reuse [Core-reader validation helpers](../../sources/terrumc/NativeCoreManifestReader.cs)
without extending existing manifest grammars.

`Text` is nonempty, valid strict-UTF-8 decoded text without NUL; compare
ordinally without trimming, case folding, normalization or path interpretation.
`u32` is an unsigned integer token in `0..4294967295`, without sign, fraction,
exponent, numeric-string conversion, overflow or truncation.

| Root field | Exact shape |
|---|---|
| `schema` | Exact token above. |
| `coreAuthorityIdentity` | `Text`; host designation, not authentication. |
| `usingModuleIdentity` | `Text`; exact using logical Module. |
| `pureSchema` | Selector or null. |
| `constEvalSchema` | Selector or null; at least one role must be nonnull. |
| `localSchemaDeclarations` | LocalCatalog or null; nonnull exactly when some selected provider equals the using Module. |

**Selector** has exactly `providerIdentity: Text` and `exportKey: Text`.
The required declaration family is semantic AttributeSchema, not a
constructor, TypeDef, MethodDef or physical DefId.

**LocalCatalog** has exactly `origin: Origin`, `declarations: Declaration[]`
and `exports: Export[]`; both arrays are nonempty.

**Origin** has exactly `moduleIdentity: Text`, `revisionIdentity: Text`,
`schema: { identity: Text, revision: u32 }`,
`governingSemanticEdition: u32`, `targetRequirement: u32` and
`runtimePolicy: u32`. Require nonzero schema revision, governing edition 1,
target requirement in `0..1` and runtime policy in `0..1`, with their existing
Module meanings and compatibility/admission checks. Require
`moduleIdentity == usingModuleIdentity`. Supply every value explicitly.
The catalog schema identity/revision names the actual in-process declaration
contract; it cannot change this grammar or dispatch an artifact decoder.

**Declaration** has exactly the following complete bounded schema projection:

| Field | Required value |
|---|---|
| `qualifiedName` | Explicit source-qualified name under ordinary naming rules, not derived from a role/key. |
| `genericArity`, `argumentCount` | Each `u32` exactly 0; optional/defaulted arguments are not zero arguments. |
| `targets` | Array containing each of `"ordinary-named-callable"`, `"individual-accessor"`, `"eligible-initialization-constructor"` exactly once, in any order. Maps to the corresponding existing target enum categories; adds no placement. |
| `isPublic`, `isPrimary`, `isPrimaryTargetOnly` | Each Boolean true. |
| `isRepeatable` | Boolean false. |

**Export** has exactly `exportKey: Text` and `declarationIndex: u32`, a
zero-based in-range index into this catalog's `declarations`.
Create each actual declaration object once, then resolve exports to those
objects before calling `CoreEvaluationFormSchemaDeclarations.Create`.
Indexes are file-local construction references, not cross-process identities.
Reject duplicate export keys even with identical indexes, declaration-space
collisions, dangling indexes and unexported declarations. Multiple keys may
alias one object; never merge distinct declarations by shape or manufacture
one object per alias. Reject both roles resolving to one definition, including
through different keys. Preserve the prerequisite's complete checks,
including unused declarations, exports and selected roles.

## Admission, references and other manifests

The designated host supplies configuration from outside the compiled
source/package/artifact. Passing this option designates the input; neither
`coreAuthorityIdentity` text nor a file authenticates authority. Execute no
file-supplied code and load no provider assembly. Select no production
authority, Module, revision, qualified name, export key or catalog default.
Prohibit spelling, filename/path, namespace, output-name and `--corelib`
inference.

* **Local:** the manifest is the actual host declaration source for its using
  Module's bounded schema catalog. Construct the prerequisite's typed catalog
  and selection, install checked declarations in ordinary lookup and pass
  `CompilationOptions.CoreEvaluationFormIdentitySelection` to every eligible
  factory. No fake source declaration, Import or reference is needed.
* **External:** resolve only from the same compilation's already supplied
  ordinary checked `CompilationReference` schema-provider/export projection.
  Require one unambiguous admitted provider snapshot, its real public primary
  schema definition/export correspondence and the complete original
  expectation/context. The manifest supplies no external declarations,
  provider paths, reference factory or alternate resolver. A local catalog,
  Native/Layout manifest snapshot or provider Module string is not that
  reference. If the route cannot supply these facts, reject the supplied
  external selection even when unused. Current eligible CLI routes expose no
  such ordinary schema-reference input; accepting the manifest must not
  pretend external delivery is implemented.

Mixed local/external roles and distinct external providers need no additional
manifest shape; they still require those actual reference capabilities.
Reject options/reference duplicate supply, ambiguity, lost alias equality and
conflicting snapshots under the prerequisite. Reuse checked reference handles,
not equal-looking reconstructed provider objects or stale admissions.

Keep Layout, NativeCore and NativeOutput manifests independent and unchanged.
Read selected configuration files in option order; validate all before exposing
any selection to binding. Require ordinal-equal authority/using identities
across files, and agreement of every Origin field for a shared provider with
ordinary references or other registrations. Compose atomically with the
[existing composer checks](../../sources/terrumc/CoreManifestRegistrationComposer.cs);
no manifest wins by order. Keep the local evaluation
catalog outside `CoreProviderModuleBindings`, whose external-only invariant
and declaration payloads are not changed by this proposal.
Reject aliases between a manifest and another configuration, source, artifact,
ABI output or destination using existing path/file-identity checks, before
output acquisition.

Retain local/external correspondence, original keys/schema expectations and
using/provider binding contexts through binding, rewriting, verification,
reference projection, replacement and lowering. A selection does not prove a
callable's form. Source-free consumers use admitted published contracts, not
replayed source spelling or provider-body re-proof.
Serialize neither this manifest nor host authorization into Terrum metadata,
private payloads, debug attachments or ABI manifests. Direct PE/JIT lowering
may consume checked source context without exporting an authority grant.
Any output route, including `compile-trrm`, that must retain a schema/form
dependency but lacks its supported declarations/maps/accompanying context
must fail before publication, not omit the obligation, substitute a schema
or silently switch routes. Already normalized outputs with no surviving
schema dependency need no host manifest on consumption.

## Failures and focused conformance

| Condition | Required result |
|---|---|
| Bad/ineligible/repeated option or operand; `compile-trrm --identity` disagreement | Existing usage error `TRM2000`, exit 2. |
| Unreadable/invalid manifest, incomplete/unsupported catalog, unresolved supplied selector or cross-input conflict | `TRM2001`, exit 2; configuration path/property/index where known, no invented source span. |
| Forbidden path/file alias | `TRM2002`, exit 2. |
| Missing required role or source lookup/placement/form/evaluation failure | Ordinary located compiler diagnostics, exit 1; never inert-attribute success or runtime fallback. |
| Later unsupported output/codec or publication failure | Preserve the route's actual failure category and staged-publication behavior; never claim partial success or retry another route. |

Prepublication failures preserve existing DLL/EXE/TRRM/ABI bytes and start no
program. Use existing staged publication, not a lossy success-shaped retry.

Required cases, not evidence of executed qualification:

| Case | Expected boundary |
|---|---|
| Local ConstEval-only; Pure-only; both distinct definitions; alias exports | Correct exact binding and ConstEval implication without requiring Pure selection; normal form verification still required. |
| Several source trees/stdin, qualified names and transparent aliases | One explicit Module with ordinary lexical lookup; source order/path/name changes never select identities. |
| Missing catalog/role, wrong owner/schema/target/arity, duplicate keys/properties, bad indexes/text/numbers | Reject complete invalid input, including unused entries; same-name/shape decoys grant nothing. |
| Two roles alias one definition; case/normalization-distinct keys | Reject role collision; preserve ordinal distinctions. |
| External-only/mixed roles with a real checked schema reference | Resolve its actual exports and retain all contexts; absent/unsupported reference, fictional manifest provider or conflicting revision rejects. |
| Evaluation plus Layout/Native/NativeOutput, in either option order | Compatible selections compose; authority/Module/origin/ownership conflicts and file aliases reject atomically. |
| Each eligible source command and both build targets; missing/changed build configuration with existing outputs | Forward selection exactly once; no stale-success target skip, spelling fallback or mode-derived selection. |
| Omitted option on unconfigured source; ineligible/component/source-free commands given the option | Preserve supported unconfigured behavior; missing required dependencies still fail; ineligible option is never ignored. |
| Successful source binding followed by an unsupported TRRM/private context codec | Decline publication, preserve old output and all obligations; direct normalized native output does not serialize authority. |

## Alternatives and remaining decisions

Separate scalar flags cannot describe actual local declarations/aliases;
extending existing manifests changes unrelated closed grammars. A unified
local/external Core catalog could replace their shared carrier, but requires a
larger ownership/payload/admission redesign. The independent bounded manifest
reuses the existing typed handoff without that redesign.

No CLI-shape decision remains deferred here. Adoption of both proposals remains
open. Concrete project/production identity and catalog values remain explicit
host configuration; ordinary external schema-reference delivery and retained
declaration/context codecs remain independent implementation/approval gates,
not permission to invent a loader or serialize authority.
