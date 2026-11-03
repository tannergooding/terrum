# Standard TRRM provider-native lowering

**Status: unapproved candidate; provisionally actionable only under the
user's authorized missing-contract workflow.** This is a proposed host
handoff, not formal approval or normative adoption. The bounded
direct-import, separately supplied-provider and co-located leaf-provider
routes are implemented and focused-tested; they do not qualify broader F
content or the candidate as a stable contract.

## Decision and scope

Add three explicitly selected Standard operations to `NativeModuleCompiler`:
emit an executable, emit a library, and execute by in-process JIT. Each
accepts the original artifact selections, performs one shared checked
preparation, and retains an internal resolved-binding plan through lowering.
Return PE bytes or the existing JIT result, **not native packages**.

This completes the native-handoff direction left open by
[selected-provider consumption](standard-trrm-selected-provider-consumption.md).
It consumes that proposal's exact per-artifact selections, bounded Module
closure, every-edge Module/key resolution and original-expectation checks.
This document adds the native preparation/realization contract, not another
provider topology or identity resolver. The candidate remains limited to
exact `trrm/1-draft-foreign-invocation-contracts-1` (F), Windows x64 and
fail-fast, and does not imply support for unrelated F schemas/content.

The consumption mode is **semantic relowering of explicitly selected
`.trrm` inputs**. No precompiled provider image is supplied or selected.
Provider chains, general semantic Body merging, additional imports, raw-output
reconstruction and other currently unsupported F content remain rejected.
This decision adds no source syntax, ABI, schema identity, record, numeric
assignment, private revision or conversion.

Keep `LoadStandard` and `CreateStandardReference` as semantic operations.
Keep existing `NativeEmitter`/`NativeJit` public operations unchanged,
including their rejection of provider-bound imports on direct-import routes.

### Decision classification and ownership

The [selected-provider proposal](standard-trrm-selected-provider-consumption.md#bounded-f-module-closure)
owns exact Module shapes, multiple root Imports, finite dependency topology,
every-edge validation and the distinct-name admission bound. This proposal
owns how the checked immutable binding plan is consumed by JIT/PE lowering,
the native import/entry realization, code/load origins and runtime lifetime.
Co-located Modules and provider-owned dependency edges do not create a
precompiled provider image or alter the selected consumption mode.

No accepted specification text is replaced. Both the shared admission
proposal and this native handoff remain unapproved; this separation prevents
semantic relowering from being treated as unchanged precompiled-provider
deployment.

## Owners and inspected boundary

No accepted text is superseded. These owners continue to apply:

| Owner | Preserved contract |
|---|---|
| [Exact schemas](../specs/representation/format-versioning.md), [admission](../specs/representation/requirements-and-admission.md), [consumer checking](../specs/representation/reader-trust.md) | Select every artifact before decoding; admit complete retained requirements and necessary structure/bindings before generation. Producer semantic truth is not a mandatory consumer re-proof. |
| [Module/Import/Export metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [binding and replacement](../specs/representation/binding-and-replacement.md) | Exact identity/key before contract comparison; retain each original expectation and any stronger relied-on fact in its owning context. |
| [Modules](../specs/modules.md), [JIT](../specs/jit.md), [persistence taxonomy](../specs/representation/persistence.md) | Semantic regeneration and unchanged precompiled composition are different modes; `.trrm`, private packages, target images and process status are different objects. |
| [Native boundaries](../specs/native.md), [Windows x64 deployment](../specs/targets/windows-x64-deployment.md) | Unsafe obligations, actual native roles/mappings, selected entry, fixups, publication, unwind and code/load lifetime remain required. |
| [CLI protocol](../specs/cli-protocol.md) | Existing experimental responses/raw worker status are not adoption of `terrum.cli/1` or a new supervisor. |

The F schema and
[NativeImport pointer-contract candidate](standard-native-import-function-pointer-contracts.md)
remain unapproved prerequisites. This proposal supplies only their bounded
checked-native consumer handoff, including the distinction between semantic
relowering and their separately specified precompiled-provider entry route.

Direct source inspection establishes the following, not execution evidence:

| Source | Why it does not already provide the complete handoff |
|---|---|
| [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`F reconstruction`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs), [`binding plan`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs) | Loading compares the selected provider and verifies the reconstructed root, preserving its entry and `BoundModuleImport`. `LoadStandardNativeBindingPlan` now captures owned artifact snapshots, every Module's exact identity/requirements/ranges, the original root expectation and selected provider MethodDef origin. Its internal lowering view is created only after the selected contract comparison. |
| [`NativeEmitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), [`NativeJit`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs), [`Standard native operations`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.StandardTrRm.cs) | Public direct-import APIs still reject provider-bound module references. The new high-level Standard operations consume the checked plan, then use existing internal import-binding, PE and JIT machinery; raw caller-supplied DLL/export bindings are not accepted by those operations. |
| [`Single-provider compiler`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs) | `CreateProducer`/`CreateConsumer` consume or produce private semantic/native packages containing PE images. Binding has additional private-route restrictions, including export key equal to the local function name, and consumers call the selected provider DLL/export. `RunAsync` stages those images with a caller-supplied deadline. |
| [`Sets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`graphs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.Graph.cs), [`graph readers`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs), [`graph JIT`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | Sets require 2–16 private producer packages and return a private set. Graphs accept 1–16 native leaf/graph packages, not selected Standard artifacts. Graph JIT restores the private root while retaining validated provider DLLs; its trusted worker, staging and explicit deadline are a different execution contract. |
| [`TRRM commands`](../../sources/terrumc/Program.TrRmExecution.cs) | Explicit F selection reaches `EmitStandardExecutable`, `EmitStandardLibrary` or `ExecuteStandard`. Bounded direct-import, separately supplied-provider and co-located leaf-provider run/build routes are wired; no default changes. |

The source-free consumer now resolves providers from the captured Module
directory, excluding only the selected root itself. The checked lowering plan
accepts provider artifact index `0` only when its Module row differs from the
root. The selected-root ModuleRef remains the sole dependency edge; every
other populated Module, whether co-located or supplied, must be a supported
leaf with no retained ModuleRefs/Imports.

**Recorded bounded evidence:** the latest Debug fast build passed and the
focused F producer/loader/provider/CLI selection passed 27/27 with zero skips
at `artifacts\\tst\\Debug\\fnptr-fprofile-multimodule-colocated-final-v2\\fnptr-fprofile-multimodule-colocated-final-v2.trx`.
It covers non-first root/provider Modules, a co-located leaf provider through
the public Standard loader/JIT/PE-emission APIs, and CLI JIT plus supervised
AOT executable execution without `--trrm-provider`. The separate-provider
CLI test also exercises provider-bound DLL emission, mismatch rejection
before sentinel replacement, and refusal to export the imported expectation
as a root-owned library function. Input hashes are retained at
`artifacts\\log\\fnptr-fprofile-multimodule-colocated-final-v2-inputs.txt`.
This does not exercise co-located library CLI emission/runtime invocation,
pointer NativeParameter runtime access/extent, dynamic resolution, provider
chains or provider library execution; it is not formal approval or milestone
acceptance.

Consequently, the checked single/set/graph APIs cannot complete this route
unchanged. Creating native packages first would require additional private
codec/image handoffs and change the selected consumption mode. Their private
identity/filename rules must not become Standard Module identity restrictions.
The current internal plan is the selected F semantic-to-native handoff,
without creating `.trmn`, set or graph artifacts.

### Material alternatives

| Alternative | Assessment |
|---|---|
| Convert F to private producer/consumer/set/graph packages | Valid only as a separately designed conversion/deployment facility. It introduces private persistence dependencies, provider images and entry/worker contracts, and does not directly return the CLI's requested PE or existing in-process JIT result. Not selected. |
| Clear `ModuleImport`, copy a descriptor, or pass matching DLL/export strings to a raw emitter | Not a checked handoff. It loses the selected actual declaration and original obligations, including address-only edges. Rejected even after a prior successful `LoadStandard`. |
| Add a reusable public native-ready handle and emitter/JIT overloads | Coherent, but adds a public result, reuse/invalidation contract and more entry points than this one-operation CLI/host use needs. An internal immutable plan provides the necessary preservation without that public lifetime surface. |
| **Three Standard operations with one internal checked plan — recommended** | Captures selections once, keeps semantic admission adjacent to actual-provider resolution, and reuses native machinery without exposing an arbitrary binding override or changing direct-import defaults. |

## Concrete public API candidate

Add the following **unapproved C# signatures** to
`Terrum.CodeAnalysis.Modules.NativeModuleCompiler`, using the existing
`TrRmStandardArtifactInput`:

```csharp
public static byte[] EmitStandardExecutable(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    NativeRuntimePolicy runtimePolicy,
    CancellationToken cancellationToken = default);

public static byte[] EmitStandardLibrary(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    IReadOnlyList<string> exports,
    NativeRuntimePolicy runtimePolicy,
    CancellationToken cancellationToken = default);

public static int ExecuteStandard(
    TrRmStandardArtifactInput input,
    string moduleIdentity,
    IReadOnlyList<TrRmStandardArtifactInput> providers,
    NativeRuntimePolicy runtimePolicy,
    string? registrarAssembly = null);
```

`input`, `moduleIdentity` and `providers` have exactly the selected-consumer
meaning, including an explicit empty provider list when no additional artifact
is needed, whether the target is owned-direct or in a distinct co-located
Module. There is no provider DLL/path argument, resolver, native-binding
override, precompiled input, schema default or native-lowering-agreement selector.
Initially only `FailFast` and the bounded Windows x64 F input are supported;
an unsupported policy is not implemented by changing the input's policy.

The emit methods return a complete caller-owned PE32+ EXE or DLL array.
They do not return `.trmn`, `.tnset`, `.trng` or rewritten `.trrm` bytes.
They write no files, load no libraries and execute no code. Cancellation
retains the existing emission meaning, not execution interruption.
`ExecuteStandard` uses the existing in-process Windows x64 JIT and returns
its `int32` entry result. `registrarAssembly` has the existing trusted JIT
registrar meaning; it is not a worker/deadline setting. There is deliberately
no execution timeout, cancellation, isolation or scheduling API here.

Executable emission/execution require the selected root's retained
`Module.EntryMethodDef` and the existing supported entry shape, including
the current Main-name, parameterless `void`/`int32` restrictions. Do not
replace entry selection with a name search or use a provider's entry.
Library emission requires a no-entry root. `exports` retains the existing
library emitter's exact bound-function-name, visibility, layout, uniqueness,
capacity and native-callable-name rules, with default export containment.
It is not a list of provider keys or a new `NativeExport` contract.
Imported expectations remain ineligible DLL exports; resolving one does not
make it root-owned. Eligible root-owned direct imports retain their existing
explicit export-thunk behavior.

## Checked preparation and retained plan

All three methods share the following preparation; they must not implement
three different binding relations:

1. Validate arguments and preflight **all** exact artifact schema selections
   before any artifact index/reader is constructed. Follow the selected
   consumer's ordinal token rules and supported-schema boundary. Capture the
   input/provider/export lists and owned byte snapshots once. Callers must
   not mutate inputs concurrently; later mutation cannot change the operation
   or returned image. Do not decode caller bytes again after checking a copy.
2. Share/factor the selected loader's complete structural/admission and
   Module-directory checks. Select the root by identity in its own artifact;
   duplicate identities reject regardless of equal bytes or argument order.
   Admit every supplied artifact/Module, not just the first matching provider.
   Preserve each schema, governing edition, requirements and target/runtime.
3. For the retained root Import, resolve its exact ModuleRef identity and
   Export key to a public provider-local callable NativeImport. Preserve
   legal keys different from MethodDef names. Reject an imported proxy,
   anchor, data import or equal-shaped authored method as the actual target.
   Compare complete original signatures, caller-unsafety, native descriptor,
   convention, resolution and normalized `000E` collections under the existing
   supported relation. Preserve full unsigned extents and all type contexts.
   Nonempty raw-output collections remain unsupported in this bounded loader,
   not silently empty.
4. Preserve/check the pointer view against the **original expectation**, not
   stronger current-provider facts. Keep anchors noncallable. Reuse root
   verification on the same reconstructed snapshot and existing supported
   call/provenance checks. This adds no mandatory semantic re-proof of
   provider Bodies or foreign behavior, and no Core/source authorization.
5. Before machine generation, reject unsupported closure/topology, native
   role, signature, convention, resolution and target/runtime mapping.
   Apply the bounded closure below per logical Module, not by classifying
   an entire artifact as root-only or provider-only. Chains/cycles, extra
   dependency edges and unsupported replacement relations do not become
   direct foreign imports. Check uncalled, address-only and discarded uses,
   not just eventual machine-import usage.

### Checked native-plan handoff and origin

The complete Module shape, root cardinality, name bound, provider graph,
every-edge validation and original/actual binding relation are owned by
[selected-provider consumption](standard-trrm-selected-provider-consumption.md#bounded-f-module-closure).
This proposal adds no second topology or alternate admission path. It owns
the native handoff after that semantic preparation succeeds.

The native operation receives one internal immutable plan retaining:

| Retained part | Native realization needs |
|---|---|
| Selection/context | Owned input snapshots, exact schema/catalog and Module selections, root entry/no-entry state, operation and target/runtime context. |
| Every original edge | Using artifact/Module, Import and ModuleRef/expected MethodDef identities, selected provider/key/requirements and complete original callable/native/pointer-view expectations. Include uncalled dependencies. |
| Selected actual | Exact provider artifact/Module/local MethodDef, public Export key, admitted NativeImport descriptor/contracts and checked satisfaction result. |
| Lowering correspondence | Root declaration/address use to checked actual definition and its native import/thunk, semantic origin and actual code/load origin. Preserve separate semantic origins even if physical machinery is shared. |

Keep the root `ModuleImport` and original declaration intact. Only the
checked internal lowering view may realize the bound actual; a bare
`BoundProgram`, copied descriptor or previous success flag is insufficient
for direct native APIs. Do not clear imports or derive a DLL/export mapping
from caller-supplied strings.

The same correspondence must survive remapping, interning and bound-tree
rewriting, with affected typed references/maps/call plans rebuilt. Revalidate
changed inputs; reject missing/stale relationships before generation.
Precompiled provider deployment remains a separate route and is not
regenerated or validated by semantic relowering.

## Native realization and lifetime

For this **semantic** route, lower the selected actual provider's NativeImport
to the existing supported Windows x64 foreign call binding. Derive library
and resolved entry exclusively from that checked provider-local declaration.
The original expectation remains the caller's view and binding obligation;
its strings are not independently authorized machine bindings.

The declaration origin remains the selected provider Module/MethodDef.
Its machine realization belongs to the regenerated closure: an IAT call,
or the existing ABI-transparent leaf jump for NativeAddress, tied to the
admitted foreign library/export and actual loaded code. NativeAddress exposes
a usable entry, never an IAT cell address, anchor, serialized address or
fabricated null. No NativeCallable, extra invocation/output publication or
failure bridge is synthesized.

There is no `terrum-<provider>.dll` to manufacture or bypass: no native provider
image was selected. Conversely, an explicitly selected precompiled provider
on the existing native package/graph routes still requires its validated
provider entry. Those routes must never be redirected here or silently
regenerated. This is the consumption-mode distinction already required by
the [pointer candidate](standard-native-import-function-pointer-contracts.md#resolution-provider-binding-and-replacement).

Use the same checked correspondence for JIT and both AOT outputs. Each
external declaration needs its owned-direct or checked-provider mapping;
every emitted foreign call/address fixup must belong to it. Equal foreign
symbols may share physical machinery only while their distinct semantic
origins and all original obligations remain retained and checked.
Optimizations or an empty IAT do not discharge declaration-only obligations.

Preserve the current supported Link mapping, calling conventions, DLL/name
limits and absolute Windows system-directory qualification. Unsupported
Dynamic/Vectorcall or platform mappings decline; do not add application-
directory search, suffix inference or compatibility fallback. Preparation
checks support but performs no foreign-symbol lookup. JIT resolves required
symbols and completes fixups, write/execute separation, instruction-cache
publication and unwind registration before entry/address exposure. AOT emits
the corresponding PE imports/relocations; successful emission is not native
symbol resolution or execution.

Retain generated stubs/code, actual foreign-library loads, helpers and unwind
backing through every admitted use. Reuse existing JIT retirement ordering
and retain backing if deregistration fails. No new escaped-pointer lease,
early unload or lifetime extension is supplied. DLL consumers retain their
image while its exported/returned addresses remain usable under the existing
native lifetime contract. Provider entries/Bodies/initializers are not
automatically invoked or exported; required unsupported activation declines.

Changing a provider for a **new operation** requires fresh selection and
complete checks, then new code. A changed unrelated private provider Body
need not make an otherwise satisfying declaration incompatible. A changed
foreign protocol, identity, retained requirement or unsupported relation
rejects before generation. There is no operation to replace a provider inside
a prepared plan, service an unchanged emitted PE, patch an escaped address
or hot-swap a loaded library. A PE is the fixed realization, not a new
serialized provider-binding proof. Any internal cache keys the complete
unchanged selections, expectations, origins and rule/target/runtime context.

## CLI handoff without a new command contract

Keep the selected-consumer options and their validation unchanged. After
option/schema preflight and one capture of each file, use:

| Explicit Standard command | Proposed native operation/result |
|---|---|
| `run-trrm` | `ExecuteStandard`, passing the existing registrar selection; return the program's raw status. |
| `build-trrm` | `EmitStandardExecutable`; publish only the complete `.exe` result. |
| `build-trrm-library` | `EmitStandardLibrary` with the existing explicit root export list; publish only the complete `.dll` result. |

Pass the captured `TrRmStandardArtifactInput` selections directly to the new
operation. Do not first call public `LoadStandard`, discard its provider
context, then try a direct emitter; factor shared preparation instead.
Providers do not acquire an executable-entry or automatic export role.

Omitted selection retains the fixed Development route. `compile-trrm`
retains its destination-schema/source-Core boundary and `.trrm` output.
Native package/set/graph commands keep their own inputs/results. This adds
no options, provider resolver, private conversion, implicit artifact detection
or mixed-format list. Artifact `-` remains invalid. Keep `.exe`/`.dll` output
validation and Windows input/output collision checks against **every** input,
including providers, plus the existing complete-image `WriteArtifact`
publication/cleanup behavior.

Require the existing `--runtime fail-fast`; preserve checked-exceptions
rejection. `run-trrm` remains in-process, without staging provider images,
launching a graph worker or borrowing its deadline. Its caller supplies
supervision. Preserve program streams, raw nonzero results and diagnostic-only
`--json` behavior; do not emit a success envelope over program output or map a
normal nonzero program return to compilation failure. Existing nonexecution
success responses/status remain unchanged. No `terrum.cli/1` migration is
selected.

## Failure and side-effect contract

| Condition | API behavior; existing CLI mapping |
|---|---|
| Null required input/list/element/identity or exports | `ArgumentNullException`; malformed/missing CLI option values remain usage errors. |
| Empty/invalid identity or unselected/altered schema token | `ArgumentException`; option/schema preflight remains exit `2`, `TRM2000`, before artifact reads. A decoded missing root identity remains admission exit `1`, `TRM5001`. |
| Malformed selected bytes/references, duplicate Module identities, missing provider/key, wrong actual role or incompatible original expectation | `InvalidDataException`; exit `1`, `TRM5001`, without generation, output replacement or execution. |
| Valid but unsupported selected-F content outside the bounded Module closure, replacement relation, native mapping or runtime policy | `NotSupportedException` before generation; exit `1`, `TRM5001`. Unselected schema tokens follow argument preflight above; invalid encoded values remain invalid-data errors, not unsupported aliases. |
| Wrong executable/library entry state, invalid verified root or invalid export arguments | Existing invalid-data/argument/verifier failure categories; exit `1`, `TRM5001`. No fallback entry, provider export or partial image. |
| Observed emission cancellation | `OperationCanceledException`, no image result; no new CLI cancellation/deadline protocol. |
| CLI input/output I/O, access or decoding failure | Existing exit `2`, `TRM2001`, and output-publication cleanup. |
| Actual native loader/symbol, publication or execution failure | Existing native JIT/platform or program failure behavior; never success/null-pointer substitution, provider retry or guessed normal program return. |

Preparation performs no file writes, native loads, initialization or Body
execution. Emission allocates/builds an image but does not activate it;
backend capacity/internal failure returns no image. JIT may load foreign
libraries, including their platform initialization effects, only after complete
admission/binding/support checks. It is not a sandbox and cannot undo program
or loader effects after execution starts.

Rejections never retry a different provider/schema/runtime, source compilation,
private package or caller-authored DLL binding. Diagnostic text identifies the
input, and established Module/key/obligation where applicable; no new code or
response field is needed. Apart from selector preflight and the stated
before-generation/publication boundaries, precedence between independently
invalid inputs is not newly prescribed.

## Required acceptance cases

These are future checks, not results of this documentation change. Use public
APIs, independently assembled F fixtures and unrelated headless providers;
separate binding, image inspection and supervised execution evidence.

| Case | Required outcome |
|---|---|
| Selected provider across all routes | A root selecting an eligible provider NativeImport executes by JIT and emitted EXE; a no-entry root emits a DLL whose selected wrapper export is actually invoked. Exercise direct calls, staged direct/indirect calls, saved register/Home targets and NativeAddress-only reachability within the supported Body subset. Observe real foreign behavior, not merely import-name text. |
| Exact selection and entry | Use a legal key different from the MethodDef/local expected name, reorder methods/maps and place empty or supported populated Modules before selected Modules. Preserve root `EntryMethodDef` or no-entry state and exact provider ownership. Never run/export the provider's caller or choose the first/same-spelled entry. Unsupported entry shapes still decline. |
| Co-located selection and origins | Use independently assembled or schema-aware repacked F fixtures with valid ranges, references and maps, containing distinct root/provider Modules in both orders and supported distractor Modules sharing names/keys. `LoadStandard` retains the original root Import; `CreateStandardReference` preserves the selected root's export ownership and contracts rather than automatically projecting the provider. Native operations accept provider origin index `0` and a provider MethodDef outside the root's range, with an explicitly empty `providers` list. Reordering cannot change the selected Module/key or admit an Export into another Module's range. This requires no general merger API. |
| Co-located cross-route behavior | Compare the same root/provider contracts supplied separately versus co-located through public APIs and all three CLI commands, with no `--trrm-provider` for the co-located-only case. Exercise supervised JIT and emitted EXE execution, plus an explicitly produced no-entry root whose emitted DLL wrapper is actually invoked. Preserve entry/export ownership, foreign behavior, pointer origins, raw program status and streams; do not automatically invoke/export another Module's entry or caller. Bare loaded roots still fail direct native APIs with `ModuleImport` retained. |
| Co-located rejection and atomicity | Reject duplicate identities within one artifact or across inputs (including resubmitting the root artifact), a root present only in an additional artifact, missing/wrong-case producer or key, wrong-kind/owner Export, imported proxy, anchor or equal-shaped authored target, and incompatible original contracts. Reject a self-edge unless its distinct expected stub resolves to a public owned NativeImport; reject every unresolved, incompatible or invalid incoming edge, including uncalled edges. Malformed or unsupported nonselected Modules still fail admission; co-location cannot hide them. Preserve sentinel outputs and observe no execution marker on each preparation failure. |
| Original contracts | Independently alter library/entry, convention/resolution, caller obligations and each NativeParameter type/access/retention/extent dimension. Reject incompatible provider/key/target/runtime before generation, including unused/discarded/address-only expectations. Preserve high-bit/maximum unsigned extents without truncation. No claim that these checks prove foreign behavior. |
| Admission and closure | Reject unknown/altered or unsupported composite selectors before decoding even malformed other inputs; then reject malformed rows/maps, duplicate identities, missing providers and proxy/anchor/data targets. Exercise owned-direct, imported, mixed, chain, diamond, valid finite-cycle/self-edge and invalid-edge cases from the shared selected-provider contract. Multiple populated Modules alone are not a rejection reason; unsupported retained content or edges in any Module still decline. Valid unsupported raw outputs or richer F forms decline rather than dropping facts. |
| Replacement and snapshot isolation | Accept a freshly selected same-identity provider whose unrelated private Body changes while the supported contract remains satisfied; independently reject each incompatible replacement. Interleave equal identities with different schemas/bytes/contracts without stale cached acceptance. Caller buffers changed after a completed operation cannot change its image; never reread files during JIT/emission. |
| Binding authority | Bare `LoadStandard` results still fail direct native APIs with `ModuleImport` retained. Matching raw DLL/export strings, copied descriptors, provider order or same foreign symbol in another Module do not authorize this route. Check the actual target/origin correspondence after any internal remapping; no fixture-specific exemption. |
| JIT/AOT agreement and lifetime | Use identical selected closure snapshots for JIT versus EXE, and an explicitly produced no-entry library variant retaining the same expectations for DLL. Exercise mixed modules, multiple root Imports and a finite provider graph while realizing only checked provider-owned NativeImports. Never clear an executable entry implicitly to obtain the library variant. Compare deterministic observations or API-specific invariants, not process-dependent IDs for equality. Inspect PE/import/entry/export structure separately. Execute without source or generated provider packages; retain address thunks/foreign loads/unwind data through actual calls, then retire only under existing lifetime rules. |
| CLI/output isolation | Exercise all three commands, existing entry/export failures, option/schema errors, file failures and collision with root/provider inputs. Preserve sentinel destinations on admission/emission failure and observe no execution marker. Keep `.trrm` inputs unchanged, PE outputs distinct from package bytes, build status/JSON unchanged and raw program status/streams untouched. |
| Preserved routes | No-provider F retains direct-import behavior. Development/default and historical schema routes keep their interpretations. Native single/set/graph and root-only graph JIT continue to use their selected precompiled provider entries and existing supervision; none is silently routed through semantic regeneration. |

Qualification must name the final relevant patch, compiler configuration,
exact input selections and executed selectors, distinguishing passed, skipped,
rejected, emitted and actually executed cases. Existing semantic binding and
no-provider tests are not selected-provider native acceptance. This proposal
does not close the larger implementation or CLI milestone.

## Remaining decision

The recommendation is the three-operation checked **semantic-to-native**
facade above, with an internal origin/expectation-preserving plan and no
native-package conversion. The selected-provider owner defines the bounded
root/provider graph; this proposal owns native realization and lifetime for
that admitted closure. No unresolved native handoff or origin-retention choice
remains, but implementation and final-closure JIT/AOT qualification are
required. Formal approval of this proposal and its prerequisites remains
outstanding. Unchanged precompiled Standard providers retain their existing
deployment contract and are not regenerated by this semantic route.
