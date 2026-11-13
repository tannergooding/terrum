# Experimental CLI responses

This is an informative reference for the development CLI, not the selected
[`terrum.cli/1` machine contract](../specs/cli-protocol.md).
The latter has one supervisor envelope, separate program status and dedicated
program-output destinations. The development responses below do not implement
that supervisor contract.

## Existing schemas and worker behavior

The experimental check, tokens, tree, analyze, build and build-native-import
commands distinguish inspection/build diagnostics from execution.
Build-native-import opts into source-authored Windows x64 native imports;
ordinary build does not enable those imports.
These nonexecution commands return 0 on success, 1 for source/compilation
diagnostics and 2 for usage/input/output failures. Execution workers instead
return raw program status. Run-native currently supervises its native child
with a 30-second deadline and reports TRM5004 on deadline failure.

## Provisional source arithmetic selection

Source-compiling commands accept the provisional
`--arithmetic-profile development|release` selector. Values are exact and
case-sensitive; omission selects Development. It applies to eligible source
compilation commands, including hosted builds and analysis, module/TRRM/native
artifact producers, and frontend build/library/run commands. It is rejected by
syntax-only, scanner-only, source-free and artifact-only commands. For
frontend build/library/run commands, the selected profile is forwarded to the
original-source C# compiler process. In the explicit test-only
`frontend-component` probe, it applies to the subject source while the
component itself is compiled with Development. Arithmetic selection is
independent of `--runtime`, and invalid or ineligible uses follow the existing
usage-error path before output replacement. This implementation follows the unapproved
[source arithmetic selection proposal](../proposals/source-arithmetic-profile-selection.md);
it does not adopt or amend the selected `terrum.cli/1` protocol.

## Provisional TRRM source schema selection

The experimental `compile-trrm` command forwards
`--trrm-schema <exact-schema-identity>` to the public `EmitStandard` API with
Windows x64 target requirement 1. Omission preserves Development `Emit`.
Standard source candidates include `trrm/1-draft-layout-queries-1`,
`trrm/1-draft-nominal-enums-1` and the bounded foreign-invocation/local-layout
selections below. Exact schema selection does not widen their producer gates.
Unknown, malformed, repeated and ineligible options are usage errors, and
failed emission does not replace the output. Foreign/layout candidates include
the unapproved
`trrm/1-draft-foreign-invocation-contracts-1` (F) and
`trrm/1-draft-foreign-invocation-storage-addresses-1` (F+S) and
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2` (A2),
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1`
(A2E), and
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1`
(A2EU),
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1`
(A2EU+SF1),
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1`
(A2R1),
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1`
(A2R1+Repeat),
and `trrm/1-draft-local-aggregate-layout-execution-1` (L1) identities.
A3 uses the exact identity described below. Eligible source commands can
configure NativeImport roles through the provisional NativeCore manifest.
The CLI does not expose the API's explicit source TypeDef export-key map.
Artifact commands accept exact per-artifact schema identities and a root
Module identity, plus repeatable explicitly selected provider schema/path
pairs. The current whitelist includes F/F+S, A2/A2E/A2EU/A2EU+SF1,
A2R1/A2R1+Repeat, A3, B+E1 and L1; provider schema selection remains exact
and does not default or fall back. The L1 `run-trrm`/`build-trrm`/
`build-trrm-library` route uses these existing options; it adds no CLI option
or default selection. A focused Windows x64 CLI regression covers `run-trrm`
and `build-trrm` for a bodyless MethodDef provider whose exact Param
implicit-readonly-reference contract is exercised by the caller. A mismatched
by-value provider rejects before publication and preserves the existing PE
output. Generic,
receiver-bearing and MethodDef-contract-bearing provider cases remain open.
The Development default remains unchanged.
Artifact commands also accept repeatable
`--trrm-core-exception-anchor <using-module> <role> <producer-module> <export-key>`
options. The role is exactly `exception-interface` or `unreachable-payload`;
each selected using Module requires each role exactly once. Role/duplicate/
partial/schema errors are usage errors, and B+E1 loading checks selected
using/provider Module identities and public TypeDef ExportKeys before
verification. The current B+E1 route still rejects multiple artifacts, so
anchors in separate provider artifacts remain open. Hosted source compilation
carries selections through `CompilationOptions` and `CompilationReference`;
`compile-trrm` does not accept this artifact-command option.
Exact A3 selection
uses `trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`; the
flag-free `...-3` identity is not an alias. For an A3 root only, marked
admission failures map to experimental `TRM5001`/1 (invalid metadata),
`TRM5005`/1 (target-layout overflow), `TRM5006`/1 (unsupported admission),
and `TRM5007`/2 (deterministic resource refusal). A2 retains its existing
`TRM5001`/1 behavior, and unmarked failures are not promoted to admission
diagnostics. These A3-only codes and the host resource envelope are
provisional candidates under the unapproved
[A3 failure-class proposal](../proposals/standard-trrm-a3-layout-failure-classes.md),
not `terrum.cli/1` assignments. Source-free loading reconstructs the exercised
A2 positive-count scalar initialization, A2E direct/nested empty-array
initialization, and provisional A2EU positive-count Unit stores using typed
R14 `()`. A2E and A2EU artifacts pass their CLI PE-build selectors. These
checks establish reconstruction and emission, not native execution or
zero-extent backing. A2EU also captures checked `int32`/`uint32` division and
remainder over Home reads in direct NativeImport arguments, reconstructing
checked expressions and preserving operation-before-call/store order through
canonical re-emission. It also retains checked fixed-array element-address
arguments as PlaceSelection plus StorageAddress operations before the direct
call, preserving the dynamic index Home through source-free reconstruction.
These checks establish encoded ordering and re-emission, not runtime failure
behavior. Provisional source support admits exact Unit/Unit conditional
expressions in return and expression-statement contexts; both selected arms
pass native JIT under both runtime policies. Explicit/inferred Unit locals now
preserve zero-data initialization, liveness, reads, assignments and lifetime
ends in hosted and standalone source paths. Focused regressions cover
Unit-call initialization, expression-bodied Unit returns, both runtime
policies, JIT and native PE execution. The conditional proposal remains
unapproved. The Standard A2EU
Body path now encodes nested Unit-valued array-initializer conditionals as
multi-block `E4`/`ConditionalEdge` branches and `E3` joins. Source-free loading
reconstructs structured `BoundIf` control flow and canonically re-emits the
Body, including nested checked scalar and storage-address arguments. Its
condition subset now includes Boolean Home reads, direct Boolean NativeImport
results, constant selection, logical-not, short-circuit `&&`/`||`, nested
Boolean conditional expressions, and non-Boolean scalar comparisons (`==`,
`!=`, `<`, `<=`, `>`, `>=`) using assigned `0x40`/`0x41` instructions.
Boolean equality/inequality uses assigned `0x32` XOR and `0x33` NOT. Comparison
operands may be Home reads or admitted direct NativeImport calls; a left Home
read is snapshotted before a right-side call, and nested checked argument
failure prevents the branch. Boolean `==`/`!=` with one literal lowers by
inverting the existing read/call branch.
Numeric comparison literals are supported only by the distinct, provisional
A2EU+SF1 selector, which adds typed Feature 11/1 frozen-value tables for the
existing scalar codec while leaving A2EU unchanged; this comparison path
admits literals only for the ten numeric types. Feature 11 is required only
when an emitted Body retains a ConstantMap value. Source-free loading
validates the Body/source/node/blob references, reconstructs the comparison,
and canonically re-emits the typed value. Regressions cover all ten types and
six predicates, both literal operand positions, malformed table/map references,
legacy A2EU rejection, selected and co-located provider remapping, and
compatible/incompatible provider replacement. The A2EU+SF1 selector is
available to the CLI artifact selection path; its contract remains provisional
under the unapproved
[A2EU scalar-literal comparison proposal](../proposals/standard-trrm-a2eu-scalar-literal-comparisons.md).
Non-source predicate masks and call forms outside the direct NativeImport
capture profile remain unsupported. A co-located selected-provider regression
shifts the conditional caller to Body row 2, remaps its maps, preserves
body-local branches/records, and checks compatible and incompatible provider
replacement.
Supervised source-free A2EU JIT/AOT exercises these conditions under FailFast,
including selected comparison-call failure; CheckedExceptions and
cleanup/loan transport remain incomplete.
The bounded F route runs/builds direct NativeImport executables and emits
direct-import libraries. Its provisional native plan validates every retained
MethodDef Import in the supplied exact-F Module closure, including disconnected
Modules, and preserves each original expectation and selected provider origin.
Separate and co-located providers, multiple Imports, chains, diamonds,
self-edges and cycles have bounded coverage. Only root imports are realized
for lowering; provider Bodies are not merged or executed. F+S and NativeOutput
compositions retain their separate admission gates. The
[native integration inventory](conformance-gaps.md#cg-004) records those
compositions and remaining production, remapping and runtime limits.

The scalar-local F+S Body path explicitly initializes typed Homes using the
assigned R14/R15 contextual values, forms `EC`/record `0E` addresses, and
stages those pointer Homes in NativeParameter CallSites. The selected
`GetSystemTimeAdjustment` case is supervised through API JIT, CLI JIT and CLI
AOT; malformed Home write authority and extent reject, and failed AOT
preflight preserves an existing output. This scalar-local F+S slice does not
include RegisterPlace sources, arrays, aggregates or field/index selection;
the A2 successors have separate support in the
[native integration inventory](conformance-gaps.md#cg-004). Multiple indirect
operations, staged side-effects and broader provider NativeParameter runtime
qualification remain open.
The source and consumer surfaces follow the unapproved
[Standard schema-selection proposal](../proposals/standard-trrm-source-schema-selection.md)
and [selected-provider consumption proposal](../proposals/standard-trrm-selected-provider-consumption.md);
the provisional native handoff and F+S delta follow
[provider-native lowering](../proposals/standard-trrm-provider-native-lowering.md).

## Provisional native Core registration

The source `analyze`, `build`, `build-native-import`, `build-library`, `jit`
and `compile-trrm` commands accept one `--native-core-manifest <path>`.
The exact version-1 JSON input constructs an explicit in-process registration
for NativeImport, NativeCallable and NativeCallConv roles with complete
provider selections and original expectations. It supplies no built-in
authority, provider, schema or key, decodes no provider artifacts, and does not
persist host authorization. NativeExport is outside this manifest version.
`compile-trrm` requires the manifest's using-Module identity to match
`--identity`; schema and runtime selection remain independent. Other command
forms reject the option before consuming input. Shared admission with a
Layout manifest preserves their separate grammars and checks the combined
binding snapshot; it does not extend either option's eligibility or bypass
native-import/lowering gates.
This follows the unapproved
[NativeCore CLI proposal](../proposals/native-core-cli-registration.md).
[CG-023](conformance-gaps.md#cg-023) records malformed-input, provider-closure,
composition and output-preservation coverage; this is not `terrum.cli/1`
approval or a source-free Core provider protocol.

The same source commands also accept one
`--native-output-core-manifest <path>` for the independent E/O registration.
Its exact version-1 input composes NativeOutputCondition and NativeOutput
declarations with selected NativeCore and Layout declarations in one checked
provider-binding snapshot; it does not extend NativeCore manifest v1 or add
foreign-interface CLI admission. `compile-trrm` requires every selected Core
manifest's using-Module identity to match `--identity`. Other command forms
reject this option before consuming input. This follows the unapproved
[NativeOutput CLI proposal](../proposals/native-output-cli-registration.md);
it does not amend `terrum.cli/1`.

## Provisional source layout registration

The source `analyze`, `build`, `build-native-import`, `build-library`, `jit`,
`compile-trrm`, and private `compile-module producer`/`consumer` commands
accept one `--layout-core-manifest <path>`. `compile-trrm` requires the
manifest's `usingModuleIdentity` to exactly match its required `--identity`;
private-module commands use that identity only for their source-binding
context and do not persist it. The versioned JSON input is decoded strictly
and constructs only the explicitly supplied in-process Layout/Blittable
provider set; it does not select a default, configure native Core schemas,
decode provider artifacts or choose a TRRM output schema. Manifest admission
precedes source and consumer-artifact reading, and manifest aliases with
sources, producer input or output reject before publication. Syntax-only,
source-free, `compile-native`, native set/graph and other artifact commands
reject the option. The `frontend-build`, `frontend-build-library` and
`frontend-run` fallback wrappers also reject it. The separate test-only
`frontend-component` probe accepts it only for its fixed component-source
compilation; it does not configure subject input or extend the fallback
commands. A separate scanner extension admits it to
`scan-component`/`build-component` only for scanner-source compilation;
`build-component` does not pass it to subject-source compilation. Private
module source inputs remain bounded file paths, not stdin. The manifest and
component/private-route candidates remain unapproved:
[Layout CLI configuration proposal](../proposals/source-layout-cli-registration.md)
and its separate
[private-module extension](../proposals/private-module-source-layout-registration.md)
and the separate unapproved
[scanner-component extension](../proposals/scanner-component-source-layout-registration.md);
the frontend probe follows the separate unapproved
[frontend-component extension](../proposals/frontend-component-source-layout-registration.md).
These implementation paths do not change the selected `terrum.cli/1` protocol.

The private `compile-module producer`/`consumer` commands also accept one
`--private-identity-manifest <path>` under the separate unapproved
[private identity CLI proposal](../proposals/private-module-cli-identity-inputs.md).
This bounded, strict file input supplies explicit nominal selectors and keys,
Core enum-member identities, source-enum maps, and hashes of the exact source
and positional producer bytes; it does not add fields to `.trm`. If both
manifests are supplied, their using-Module identities must match and satisfy
both domains. Producer imports may select only checked Core enums from an
explicit Core registration; consumer imports must resolve to owned exports in
the positional producer artifact. The option is rejected by other commands
and does not extend `terrum.cli/1`.

## Provisional normal-backing agreement selection

The source `analyze`, `build`, `build-native-import` and `jit` commands accept
the exact, case-sensitive
`--native-lowering-agreement terrum.experimental.windows-x64.normal-backing`
selector. Other commands, repeated or malformed selections, and library/Core
output combinations reject it as a usage error. The choice is a host lowering
selection, not a TargetProfile, semantic schema or persisted authorization.
Analysis responses publish `"profile": null` and identify the selected key
separately as `nativeLoweringAgreement`; they do not label it as one of the
existing private native profiles. Native `build` and `jit` execute a bounded
source-bound class/reference closure under both runtime policies. It includes
scalar/native-address and inline-Copy payloads, ordinary/optional class-handle
fields, local required/optional ordinary references, and stored reference
descriptors in class fields and inline Copy aggregates. The candidate carrier
is 16 bytes with alignment 8; stack/inline references have zero support, while
normal-backed subplace descriptors retain one control hold per residence.
Generated direct nonforeign calls also carry required/optional ordinary
reference parameters/results and descriptor-bearing aggregate inputs, with
source-order staging and result publication after cleanup. The
[normal-backing inventory](conformance-gaps.md#cg-027) details retention,
replacement, rollback, handoff and evidence boundaries.
Optional GC/init references, reference output parameters, by-value handle
parameters/results, Cleanup, foreign/native-callable calls and
source-independent persistence remain unsupported and reject before emission.
`build-native-import` accepts the selector but still rejects at its
native-import lowering boundary. No route falls back to the address-only or
tracing representation. This provisional implementation follows the unapproved
[normal-backing proposal](../proposals/reference-normal-backing-support.md);
it does not adopt or amend the selected `terrum.cli/1` protocol.

## Provisional rootless stored-reference agreement

The source `analyze`, `build`, `build-native-import` and `jit` commands also
accept the exact, case-sensitive
`--native-lowering-agreement terrum.experimental.windows-x64.rootless-references`
selector. This is a separate, unapproved host selection; it is not the null
default, normal-backing's 16/8 descriptor, an integrated-GC runtime, a
TargetProfile or a serialized schema. Analysis publishes the selected key
separately from `"profile": null`.

The current source-bound Windows x64 path selects 8/8 ordinary and optional
reference values, direct reference arguments/results, and scalar/raw-pointer
native import boundaries without normal-backing runtime data. Debug
regressions exercise source-bound JIT lowering, PE build and
supervised image execution, direct scalar System32 import execution, and the
separate `build-native-import` producer, including optional presence testing.
Checked GC/init descriptors, managed-class runtime state, checked-reference
foreign/function-pointer
crossings, persisted module/TRRM/native-package transport and independently
precompiled provider closure remain unsupported; they reject rather than
fall back to another agreement. This implementation follows the unapproved
[host-selection amendment](../proposals/reference-stored-descriptor-host-selection.md)
and [stored-descriptor owner](../proposals/reference-stored-descriptor-layout.md);
it does not change `terrum.cli/1` or establish general reference conformance.

## Response payloads and source input

Use the built terrumc.dll/executable for stdout isolation; dotnet run may print
build messages. The `-` input path reads stdin. Human diagnostics use stderr;
--json writes the implemented response on diagnostic/inspection paths.

Schema 1 publishes language version, coordinate system, path, source length,
success state and diagnostics. Parsed commands publish a content ID.
Diagnostics carry code/severity/message and source span/range.
Usage/input failures have no fabricated source position.
Multi-source compiler errors use schema 2 and identify the owning input.
These names/versions do not select the new protocol despite their similar
numeric version labels.

Token output preserves exact text, ordinary/full spans, missing state and
structured trivia. Tree output is a flat node table with root index and
node/token child references. Messages are human-facing, not stable dispatch
keys. Existing code bands cover lexical, syntax, CLI/I/O, semantic,
native-host and bounded-artifact diagnostics.

Target file/stdin input is subject to the selected
[source-input boundary](../specs/language/source-input.md).
An invalid-byte file cannot be made conforming by filling schema-1 UTF-16
fields from a partially decoded prefix. The implemented schema does not
gain byte-coordinate fields from prose or from the newly selected protocol.

## Execution distinction

The development [JIT worker](jit.md) returns raw int32 program status,
or zero for a void entry. Its --json option formats compiler diagnostics;
successful execution does not emit a supervisor success envelope.
The current successful worker output therefore cannot be consumed as a
conforming terrum.cli/1 execution response.
A caller should explicitly supervise possibly nonterminating native work;
the selected protocol imposes no mandatory default deadline.

Private [module](modules.md), [set](module-sets.md) and [graph](native-graph.md)
command/output examples retain their own implementation meanings.
Their private schemas, containers and limits are not normative interchange,
new machine-mode status rules or automatic adoption of compiler APIs.
