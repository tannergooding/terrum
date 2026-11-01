# Experimental CLI responses

This is an informative reference for the development CLI, not the selected
[`terrum.cli/1` machine contract](../specs/cli-protocol.md).
The latter has one supervisor envelope, separate program status and dedicated
program-output destinations. No implementation migration or conformance claim
follows from that selection.

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

The experimental `compile-trrm` command accepts
`--trrm-schema <exact-schema-identity>` for Standard source production. The
current exact selections are `trrm/1-draft-layout-queries-1` and
`trrm/1-draft-nominal-enums-1`; omission preserves the existing Development
`Emit` behavior. Unknown, malformed, repeated and ineligible options are
usage errors, and a failed selected emission does not replace the output.
The public `EmitStandard` API also selects the unapproved
`trrm/1-draft-foreign-invocation-contracts-1` (F) and
`trrm/1-draft-foreign-invocation-storage-addresses-1` (F+S) and
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2` (A2),
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1`
(A2E), and
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1`
(A2EU) identities.
The CLI does not accept an explicit `NativeCoreRegistration` input, so it
cannot author the NativeImport source required by these bounded producers.
Artifact commands accept exact per-artifact F or F+S schema identities and a
root Module identity, plus repeatable explicitly selected provider
schema/path pairs including A2, A2E and A2EU; they preflight selections
before reading artifacts, and the Development default remains unchanged.
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
inverting the existing read/call branch. The focused A2EU
producer/reader/JIT/AOT selection passed 76/76 with zero skips at
`artifacts\\tst\\Debug\\a2eu-boolean-literal-comparisons-final3\\a2eu-boolean-literal-comparisons-final3.trx`.
Numeric literal comparison operands, non-source predicate masks, and call
forms outside the direct NativeImport capture profile remain unsupported. A
co-located selected-provider regression shifts the conditional caller to Body row 2,
remaps its maps, preserves body-local
branches/records, and checks compatible and incompatible provider replacement.
Supervised source-free A2EU JIT/AOT exercises these conditions under FailFast,
including selected comparison-call failure; CheckedExceptions and
cleanup/loan transport remain incomplete.
The bounded F route runs/builds direct NativeImport executables and emits
direct-import libraries. A provisional
NativeModuleCompiler path verifies one retained Import against one selected
F/F+S provider and carries the checked origin/expectation plan through JIT
and PE lowering. The source-free loader selects exact non-first root/provider
Module identities, and one root Import can resolve to a distinct leaf provider
co-located in the root artifact or supplied separately. Other populated
Modules cannot retain ModuleRefs/Imports; provider chains, additional imports,
raw-output contracts and wider remapping remain unsupported.

The scalar-local F+S Body path explicitly initializes typed Homes using the
assigned R14/R15 contextual values, forms `EC`/record `0E` addresses, and
stages those pointer Homes in NativeParameter CallSites. The selected
`GetSystemTimeAdjustment` case is supervised through API JIT, CLI JIT and CLI
AOT; malformed Home write authority and extent reject, and failed AOT
preflight preserves an existing output. RegisterPlace sources, arrays,
aggregates, field/index selection, multiple indirect operations, staged
side-effects and provider NativeParameter runtime qualification remain open.
The source and consumer surfaces follow the unapproved
[Standard schema-selection proposal](../proposals/standard-trrm-source-schema-selection.md)
and [selected-provider consumption proposal](../proposals/standard-trrm-selected-provider-consumption.md);
the provisional native handoff and F+S delta follow
[provider-native lowering](../proposals/standard-trrm-provider-native-lowering.md).

## Provisional normal-backing agreement selection

The source `analyze`, `build`, `build-native-import` and `jit` commands accept
the exact, case-sensitive
`--native-lowering-agreement terrum.experimental.windows-x64.normal-backing`
selector. Other commands, repeated or malformed selections, and library/Core
output combinations reject it as a usage error. The choice is a host lowering
selection, not a TargetProfile, semantic schema or persisted authorization.
Analysis responses publish `"profile": null` and identify the selected key
separately as `nativeLoweringAgreement`; they do not label it as one of the
existing private native profiles. Native `build` and `jit` now execute the
selected agreement for source-bound classes with scalar/native-address,
inline-copy and ordinary class-handle fields. They also support local ordinary
references with the candidate 16-byte/8-aligned carrier and a zero support word
for stack/inline places: formation, Copy/rebind, reborrow, and scalar or
inline-Copy reads/writes. References to normal-backed class/box subplaces
capture the selected control and field address, retain one support hold per
descriptor residence, and release it on rebind, lexical exit and checked
unwind. References to class-handle slots retain the containing allocation;
writable slot stores preserve local roots and typed class-field retirement.
Direct class fields, nested child fields and inline-Copy subplaces are covered
under both runtime policies. Class creation, handle copies,
handle-field assignment/replacement, borrowed receiver calls, scope retirement,
iterative child-handle retirement and checked construction rollback remain
supported. Optional-reference operations, reference-bearing fields/aggregates,
reference parameters/results, by-value handle parameters, Cleanup, foreign
calls and source-independent indexed bodies remain unsupported and reject
before emission.
`build-native-import` accepts the selector but still rejects at its
native-import lowering boundary. No route falls back to the address-only or
tracing representation. This provisional implementation follows the unapproved
[normal-backing proposal](../proposals/reference-normal-backing-support.md);
it does not adopt or amend the selected `terrum.cli/1` protocol.

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
