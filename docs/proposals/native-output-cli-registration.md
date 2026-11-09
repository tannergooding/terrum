# Explicit CLI configuration for NativeOutput Core registration

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This is an implementation-ready delivery
recommendation, not normative adoption, implemented support or qualification
evidence. Later authorized implementation may use it provisionally.

## Decision, scope and owners

Recommend one independent, optional, nonrepeatable option:

```text
--native-output-core-manifest <path>
```

Deliver the already-defined E/O closure through
`CompilationOptions.NativeOutputCoreRegistration`. E is the exact
NativeOutputCondition enum; O is its complete NativeOutput schema.
[NativeOutput source/Core registration](native-output-core-registration.md)
owns their declarations, admission and source meaning. No hosted API changes
or I/C/P placeholders are required.

For the commands below, supersede only that proposal's **CLI delivery
deferral**, and extend the
[NativeCore CLI composition algorithm](native-core-cli-registration.md#admission-and-coexistence-with-layout)
to include this separately selected family. **NativeCore manifest v1 remains
exactly I/C/P-only**: unchanged schema token, properties, roles and defaults.
Neither it nor the [Layout manifest](source-layout-cli-registration.md)
selects O, and the new file selects neither family.

[NativeOutput cells](aggregate-native-outputs.md) remain owner of eligibility,
conditions, publication, dependencies and unsafe/backing obligations.
[Foreign-interface identity](native-output-foreign-interface-method-identity.md)
remains independently required. Preserve accepted
[native roles](../specs/native.md#native-declaration-roles),
[Core authority](../specs/libraries/adopted-inventory.md#1-status-and-identity)
and [binding/replacement](../specs/representation/binding-and-replacement.md).
Select no ABI, runtime behavior, source syntax, artifact schema, metadata
allocation, resource adoption or foreign-interface delivery.

The missing delivery is visible in the existing
[`NativeOutputCoreRegistrationSet`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputCoreRegistration.cs)
and options slot versus the Layout/I/C/P-only
[`CoreManifestRegistrationComposer`](../../sources/terrumc/CoreManifestRegistrationComposer.cs).
The four
[`BuildNativeImportCompilesGenericDx11EntryPointAsync`](../../tests/Terrum.CodeAnalysis.Tests/Cli/CliTests.NativeImports.cs)
cases supply Layout and I/C/P files but no E/O selection. Their reported
`TRM3030` missing-Core-schema failures motivate this delivery; no tests were
run for this document, and supplying O does not promise that later gates pass.

## Exact option and command contract

Use the NativeCore option's existing argv/path conventions: case-sensitive
spelling, exactly one following argv element, once, after required positional
inputs among existing options and before an optional final `--json`.
Reject aliases, equals/list forms, repetition even with the same path,
missing/extra/empty operands, `-`, and operands beginning with `-`. An explicit
relative or absolute path can name a dash-leading file.

| Commands | Eligibility |
|---|---|
| `analyze`, `build`, `build-native-import`, `build-library`, `jit` | Accept for their single source compilation, including every `--source` tree and supported Core-library form. |
| `compile-trrm`, including `--library` | Accept for source binding; require ordinal equality between this file's `usingModuleIdentity` and the required `--identity`, just as for each other selected Core manifest. |
| Every other command/form | Reject as usage error before reading configuration, source or artifacts, modifying outputs or starting execution. |

Explicit exclusions are syntax-only `check`/`tokens`/`tree`; private
`compile-module`, `compile-native`, `compile-native-set` and
`compile-native-graph` forms; scanner/component/frontend routes; and every
source-free build/run/validation/catalog/resolution route. Reject combination
with internal `--frontend-command`, rather than forwarding or ignoring the
selection. Layout's separately extended routes do not extend this option.

Preserve existing destination, runtime/arithmetic, native-import,
native-lowering-agreement, Core-library and source-grant/ABI restrictions.
Registration does not enable imports in a factory that forbids them.
`compile-trrm --trrm-schema` and runtime selection remain independent; do not
relabel a bound Module or select an output schema from this file.

Resolve the path once against invocation working directory. Require a regular
file; no extension is required. There is no stdin/URL/directory/include/plugin
form, environment selection or automatic search. Capture each selected
configuration once in argv option order and finish admission before source stdin.
All source trees belong to the one explicit using logical Module; lexical
scopes remain separate. A filename, output stem, namespace or `--source-name`
diagnostic label supplies neither that identity nor Core authority.

## Exact version-1 JSON grammar

Reuse NativeCore v1's lexical and declaration-record conventions, not its
root schema or native-role carrier. The tables below are the complete grammar.
Every object has exactly the listed required properties. Object order is
insignificant; no omitted field is defaulted. Only Argument `default` admits
null. Preserve array order except `targets` and `contexts`, which are
duplicate-free sets with insignificant order.

Require strict UTF-8, permitting one leading UTF-8 signature, and one root
object followed only by JSON whitespace. Reject repeated signatures, invalid
UTF-8, unpaired decoded surrogates, comments, trailing commas/JSON, unknown
properties and duplicate decoded property names at every depth, including
escaped/literal duplicates. Names/tags compare ordinally, case-sensitively.
`Text` is well-formed decoded Unicode, nonempty, not whitespace-only and
without NUL; preserve it without trimming, case folding, Unicode normalization
or path interpretation. Source names additionally obey ordinary naming rules.
`u32` is an unsigned JSON integer token in `0..4294967295`, without sign,
fraction or exponent; `i32` permits minus and the exact signed 32-bit range.
Reject numeric strings, conversions, rounding, overflow and truncation.

### Root, origins and exports

| Root property | Exact value |
|---|---|
| `schema` | String exactly `"terrumc.native-output-core-manifest/1"`. |
| `coreAuthorityIdentity` | `Text`, the explicit host designation. |
| `usingModuleIdentity` | `Text`, the one using logical Module. |
| `providers` | Array of one or two Origin objects, exactly the distinct providers of E/O, with no unused entry. |
| `outputConditions` | One complete EnumDefinition for E below. |
| `nativeOutput` | One complete OutputSchemaDefinition for O below. |

Origin has exactly `moduleIdentity: Text`, `revisionIdentity: Text`,
`schema: SchemaSelection`, `governingSemanticEdition: u32`,
`targetRequirement: u32` and `runtimePolicy: u32`. SchemaSelection has exactly
`identity: Text` and `revision: u32`, with nonzero revision. As in NativeCore
v1, require governing edition 1 and target requirement/runtime policy each
in the existing `0..1` domains. Provider revision, declaration-contract
schema/revision, governing edition, requirements and destination artifact
schema are distinct explicit facts, never inferred or `"current"` defaults.
No provider may equal the still-unbound using Module. Duplicate provider
identities within a file reject even if all fields agree.

Identity has exactly `providerIdentity: Text`, `exportKey: Text` and `kind`,
where `kind` is exactly `"enum-type"`, `"enum-member"` or
`"attribute-schema"`. The provider must equal exactly one Origin's
`moduleIdentity`. E/O top-level keys select the actual logical exports of
their required kinds. A provider/export key cannot denote both declarations
by changing kind. No key is derived from a name, value, ordinal or prefix.

### Complete E declaration

EnumDefinition has exactly `identity: Identity`, `qualifiedName: Text`,
`underlyingType: "int32"`, `members: Member[]`, `genericArity: u32`,
`isPublic: Boolean`, `isPrimary: Boolean`, `isFlags: Boolean` and
`blittableTargetRequirement: u32`.

Require enum-type identity, qualified name
`"Terrum.Interop.NativeOutputCondition"`, arity 0, public/primary true, Flags
false, and exactly two public members: **NonZero=0, NonNegative=1**, with no
extra case, alias or missing definition. `blittableTargetRequirement` is
explicitly 0 or the existing Windows x64 commitment 1, independent of the
provider Module requirement; no commitment is inferred.

Member has exactly `identity: Identity`, `declaringEnum: Identity`,
`declarationKey: Text`, `name: Text`, `value: i32` and `isPublic: Boolean`.
Require enum-member identity, E's provider, `declaringEnum` exactly E.identity
and public true. Member identities, declaration keys, names and values are
distinct within E. Preserve supplied member array order as actual declaration
ordinal; numeric order is not required. Member `exportKey` is an identity
component, **not an independent Module export**. `declarationKey` is the
separately supplied enum-owned producer key; derive neither key from the other.

### Complete O declaration

OutputSchemaDefinition projects the existing
`CoreNativeOutputAttributeSchema`, reusing the NativeCore SchemaDefinition
shape **without `role`**. O's set slot selects its role; add no
`CoreNativeAttributeRole` member or redundant JSON role selector.

| Property | Exact value |
|---|---|
| `identity` | Identity of kind `"attribute-schema"`, distinct from E's export. |
| `qualifiedName` | String exactly `"Terrum.Interop.NativeOutput"`. |
| `arguments` | Array containing exactly the one Argument below. |
| `placement` | String exactly `"primary-callable-contract"`. |
| `targets` | Exactly `["direct-import-parameter", "foreign-interface-method-parameter"]` as a set. |
| `genericArity` | `u32`, exactly 0. |
| `isPublic`, `isPrimary`, `isRepeatable` | Three required Boolean properties, respectively true, true, false. |

Argument has exactly `name: Text`, `kind`, `enumType: Identity`,
`isRequired: Boolean`, `allowsPositional: Boolean`, `allowsNamed: Boolean`,
`contexts: Text[]` and `default`.
Require `name="When"`, `kind="enum"`, `enumType` exactly E.identity,
`isRequired=true`, `allowsPositional=false`, `allowsNamed=true`, `contexts`
exactly O's target set and `default=null`. No Default object, null name/type,
literal domain, positional overload, omitted argument or zero default exists.
Validate every record rather than filling facts from O's name or set slot.

No NativeImport/NativeCallable/NativeCallConv, NativeExport, NativeParameter,
NativeVTableMethod, ForeignNativeInterface, Layout or ownership record is
admitted here. Conversely, NativeCore v1 rejects `outputConditions`,
`nativeOutput` and every attempted output-role extension. Each reader requires
its own exact root token and grammar; do not autodetect or retry another format.

## Authority, admission and composition

Passing the option explicitly authorizes the CLI's **closed E/O declaration
producer** using host configuration designated outside the compiled
source/package/artifact. `coreAuthorityIdentity` records, but does not
authenticate, that decision. Source names, file hashes, package self-assertions
and signature-looking fields grant no authority. Execute no file-supplied code
and load no provider assembly, DLL or adapter.

The producer creates actual checked in-process provider declarations and
explicit export maps from the complete records, not fictional artifact rows.
Origin schema tokens name the in-process declaration contract; they cannot
change this grammar or dispatch an artifact decoder. The host/producer remains
responsible for legitimate origins and truthful declarations. No production
authority, revision, provider or contract-schema token is chosen by this doc.

Extend the existing composer, not the resolver or registration APIs:

1. Validate command/options and paths. Refuse aliases between any selected
   manifest and any source, output, ABI output or other manifest using the
   shared target path/file-identity relation, including hard links/reparse
   paths, not argv spelling alone. Failure to establish required identity is
   an error. Read/capture files in option order; do not acquire outputs.
2. Strictly validate each whole selected file, even unused roles. Keep parsed
   declaration inputs unbound until composition. On `compile-trrm`, compare
   **every** selected manifest's using identity with `--identity` before
   composition; any disagreement is the existing usage failure.
3. Across NativeOutput, NativeCore and Layout files require ordinal-equal
   authority and using identities. A provider shared across files coalesces
   only when **every Origin field** agrees. Conflicting revisions, schema
   selections, editions, requirements or policies reject the entire selection.
   Each file's internal duplicate/unused-provider checks remain unchanged.
4. Combine distinct complete declarations/export maps. Duplicate top-level
   provider/export keys or declaration identities reject, even if equal;
   conflicting kinds, owners or definitions likewise reject. Member identities
   must remain unambiguous in the combined graph; enum-owned declaration keys
   retain their defining-enum scope and are not compared as Module export keys.
   Provider order grants no lookup precedence.
5. Construct the checked combined providers and **one
   `CoreProviderModuleBindings` object** with complete original expectations.
   Against that exact object, call
   `NativeOutputCoreRegistrationSet.Create(bindings, outputConditions, nativeOutput)`
   and create each other explicitly selected family's set. Do not construct
   separate equal snapshots and transplant their handles. Publish none if any
   admission fails.
6. Pass each set once through its existing options property to every eligible
   options-bearing source factory. NativeOutput-only needs no I/C/P or Layout;
   the other routes without this option supply a null output selection.
   Declarations present in the shared directory do not select additional roles.
   Options/reference duplicate supply still rejects even for the same object;
   native/layout/static/lazy/output combinations retain the hosted same-object
   invariant without gaining new CLI delivery routes.

No process-global/path/content-only registration cache or retained stale
generation is permitted. Preserve original enum/member/schema and evaluation
expectations through compilation copies, rewriting and checked replacement;
changing a file rebinds new work rather than relabelling an old program.

## Source, failures and independent gates

Use the hosted owner's ordinary lexical/module lookup, accessibility,
transparent aliases, schema argument mapping and typed constant evaluator.
Lookup must select exact O/E definitions; matching spellings, same-shaped
declarations, other providers' equal-valued enums and local shadows confer no
role. Qualified names and aliases to the selected definitions remain valid.
Require an evaluated constant of exact E type, including eligible
constant/ConstEval forwarding. Explicit E.NonZero=0 is valid; omitted `When`,
integer 0/1, unrelated enum, runtime value or unknown E value is not.
Failed/exhausted required evaluation retains its actual failure category.

Normalize admitted facts once: NonZero for int32/uint32 results, NonNegative
only for int32, under the cell owner's exact `write ptr<T>` and formal-target
rules. Preserve nonrepeatability, inline/`param(name)` identity, primary-only
authorship, NativeParameter overlap rejection, unsafe authorization, known
backing, dependencies and ownership/cleanup obligations. Add no implicit
using, name-based retry, enum retagging, default registration or fallback.

O's foreign target tag is mandatory schema completeness, **not CLI admission
of a foreign interface**. This option plus NativeCore v1 still supplies no
ForeignNativeInterface schema/identity. Positive foreign output use needs its
independently admitted exact interface, declaring MethodDef, effective
NativeCallable contract and target ABI through a separately selected route.
NativeVTableMethod slots, NativeCallable alone, interface/pointer values or
caller-selected IID/void** acquisition cannot substitute. Unsupported foreign
source, dispatch, codec or target support must continue to decline.

| Failure | Existing experimental CLI projection |
|---|---|
| Ineligible command, bad option/operand/combination, repetition, or `compile-trrm --identity` disagreement | `TRM2000`, exit 2, before source/artifact reads or launch. |
| File I/O/access/decoding/JSON/shape/origin/export/registration error, including cross-file composition conflicts | `TRM2001`, exit 2; identify configuration path and established JSON property/index path, without fabricated source spans. |
| Manifest aliases another selected input/configuration/output | `TRM2002`, exit 2, before output acquisition. |
| Missing source registration, lookup/argument/evaluation/semantic failure | Existing located source diagnostics, exit 1; retain missing-O `TRM3030`, not a successful empty registration. |
| Later unsupported emission/target/codec, artifact or publication failure | Preserve the command's actual category/status and staged-publication behavior; no alternate schema/provider/emitter. |

Human and experimental `--json` use existing result/error writers, with no new
response fields or producer chatter. This does not amend
[`terrum.cli/1`](../specs/cli-protocol.md#1-selection-and-framing):
reject combination with `--machine terrum.cli/1` under its selection-failure
rule before affected input processing, publication or execution. No configuration
input kind is invented or disguised as source/artifact data.

Prepublication failures preserve existing EXE/DLL/TRRM/ABI bytes and start no
program; retain existing staged publication and truthful rollback failures.
Serialize no host authorization, file, registration set or inferred runtime
Core dependency. Supported normalized source-free output contracts need no
manifest. Surviving raw enum/schema/value/evaluation obligations retain their
own exact codec and binding gates under
[reader trust](../specs/representation/reader-trust.md) and
[format versioning](../specs/representation/format-versioning.md); this file
neither supplies codecs nor requires consumer re-proof of producer bodies.

## Required acceptance cases

These are future implementation requirements, **not tests run**. Use public
CLI entry points and public hosted APIs, existing active NUnit areas and
explicit synthetic authorized providers unrelated to DX11. Keep binding,
emission and supervised headless execution evidence separate.

| Case group | Required observation |
|---|---|
| Output-only, NativeCore-only, Layout-only, every pair and all three; each eligible command including `compile-trrm --library`, file/stdin/multitree input and both option orders/all three-file permutations | Independent selections reach the right factory; successful composition exercises the hosted same-object invariant. Unsupported import/target/schema modes still decline. |
| E/O on one provider and split providers; shared provider with distinct NativeCore/Layout exports; arbitrary keys and reversed enum member order | Exact declarations/owners/order retained; complete agreeing origins coalesce without inferred keys or lookup priority. |
| Synthetic eligible scalar, pointer and aggregate output imports through CLI versus the same explicit hosted configuration | Same normalized formal/condition/dependency facts; aliases, qualified names, inline/selector targets and eligible typed constant forwarding agree. |
| No output option or I/C/P-only on NativeOutput source; O-only on source requiring unselected I/C/P; malformed unused output input | Missing independent roles diagnose; invalid configuration always rejects. Preserve the existing I/C/P-only negative CLI test rather than weakening it. |
| The four reported DX11 build cases supplied explicit E/O as well as Layout and I/C/P | Exercise delivery and headless emission only; no sample recognizer, implicit output selection, window creation or hardware execution. Other failures remain real failures. |
| Wrong/missing/extra enum member, owner/key/base/arity/access/representation; altered O target/placement/argument/default/repetition; ordinary source misuse | Reject complete host shape or actual source error, including omitted When versus explicit zero, foreign/integer values, uint32 NonNegative, overlap, duplicates and unsafe/backing failures. |
| Malformed UTF-8/BOM/surrogates/JSON; unknown/missing/null/escaped-duplicate properties; wrong token and cross-fed files; numeric type/range/sign/fraction/exponent cases | Exact reader rejects without repair, alternate decoding or partial selection. |
| Duplicate/unused/missing provider; using/provider collision; unequal authority/using or any Origin field; duplicate/conflicting combined exports/member owners; stale/equal-but-separate snapshots | Reject whole selection with the defined configuration/host category. Include `compile-trrm` identity mismatch even with unused roles. |
| Excluded commands/frontend/machine composition; casing/equals/arity/repetition/dash/extra operand cases; missing/inaccessible/directory files | Fail before blocking on source stdin, artifact reads, outputs or workers; preserve protocol isolation. |
| Manifest/source/output/ABI/other-manifest physical aliases and sentinel outputs; repeated/concurrent/changed-file invocations | Identity checks and prepublication preservation hold; no context/configuration leakage or stale-generation reuse. |
| Output plus I/C/P applied to vtable/foreign source without independent interface admission; attempted foreign/NativeParameter/ownership fields in either file | No foreign authority or extra role. Hosted foreign positives remain gated by the separate complete registration/ABI route, not counted as new CLI support. |
| Supported normalized dependency consumption with source/manifests removed; retained obligations, reordered definitions and incompatible provider replacement | No source-registration requirement for normalized facts; preserve complete original binding/evaluation expectations and refuse missing codecs or incompatible replacement. |

## Alternatives and integration

| Alternative | Assessment |
|---|---|
| **Separate exact E/O manifest and shared composer** | Smallest complete delivery: reuse existing hosted set, record shapes, CLI diagnostics and checked binding generation without requiring unrelated I/C/P. |
| Extend NativeCore v1 | Contradicts its exact I/C/P-only contract and couples role selection; rejected for this bounded decision. |
| NativeCore v2 or general multi-family catalog | Could explicitly model independently optional complete closures, but redesigns the envelope/admission surface unnecessarily. No additional capability is needed for two existing declarations. |
| Hosted API only; artifact-selector/plugin route; built-in provider or name fallback | Respectively leaves CLI delivery missing, requires separately admitted codecs/executable authority, or violates explicit identity/authority. |

Implementation belongs in a bounded E/O reader, the existing composer,
`Program.Compilation.cs` and `Program.TrRmExecution.cs` routing, `Program.cs`
help, and related CLI tests. On adoption after implementation/qualification,
document the option in the experimental CLI reference; the hosted registration
and cell proposals remain semantic owners. No normative spec or existing
manifest grammar changes.

No delivery-policy decision remains within this recommendation. Formal
adoption is pending; actual authorized origins, revisions, schema selections,
exports and member keys remain mandatory host inputs. Foreign-interface
delivery, unsupported persistence and native realization remain separate gates,
not provisional approval of adjacent work.
