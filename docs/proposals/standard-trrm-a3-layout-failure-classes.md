# A3 layout failure classes and bounded expansion

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This document selects no accepted API, diagnostic
allocation, schema or compiler conformance. [Proposal index](README.md).

## Decision and evidence

The missing contract is the **host failure mechanism and bounded layout
processing recipe**, not a new layout algorithm or Terrum runtime failure.
Recommend retaining `InvalidDataException` for invalid selected metadata,
propagating `OverflowException` for target-layout arithmetic, retaining
`NotSupportedException` for unsupported admission, and introducing one
host-only `TrRmResourceLimitException` for deterministic capacity refusal.
Give these failures distinct producer diagnostics.

This recommendation applies **only to an operation explicitly selecting
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`**.
It preserves [A3's A2-isolation decision](standard-trrm-closed-constructed-array-elements.md#L292-L295):
A2's API exceptions, resource behavior and CLI diagnostics remain unchanged.
Neither another selector, equal version bytes nor shared implementation
helpers activate the new exception, envelope or diagnostic mappings.

| Repository evidence | Existing obligation or implementation boundary |
|---|---|
| [A3:254–263](standard-trrm-closed-constructed-array-elements.md#L254-L263), [315–331](standard-trrm-closed-constructed-array-elements.md#L315-L331), [342](standard-trrm-closed-constructed-array-elements.md#L342) | Complete constructed-instance identity, finite repeated definitions, compact count checking and separate malformed/overflow/resource failures are already proposed. The original host-exception sentence required an explicit A3-only amendment while preserving A2. |
| [Resource accounting:18–36](../specs/representation/resource-accounting.md#L18-L36), [41–60](../specs/representation/resource-accounting.md#L41-L60), [205–245](../specs/representation/resource-accounting.md#L205-L245) | Capacity is not artifact validity; nested work shares a root ledger; limits are inclusive and checked before work. Depth exhaustion proves neither validity nor a cycle. Numerical portable minima remain unassigned; physical-memory/host failure is not deterministic exhaustion. |
| [Windows layout:208–209](../specs/targets/windows-x64-data-layout.md#L208-L209), [253–258](../specs/targets/windows-x64-data-layout.md#L253-L258) | Inline storage cycles reject, pointer edges do not expand pointee storage, and layout arithmetic is checked in target `nuint`. |
| [CLI:19–32](../specs/cli-protocol.md#L19-L32), [59–77](../specs/cli-protocol.md#L59-L77), [137–152](../specs/cli-protocol.md#L137-L152) | `terrum.cli/1` distinguishes admission/resource outcomes and stable categories. Experimental `--json` is not that protocol, and TRRM command adoption requires its own command contract. |
| [Layout validator:94–126](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs#L94-L126), [203–205](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs#L203-L205), [254–256](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs#L254-L256), [1344–1346](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs#L1344-L1346) | The visit guard throws invalid-layout data errors; checked layout overflow is wrapped into that same class. The guard borrows the source verifier's visit limit. |
| [Standard APIs:26–30](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs#L26-L30), [73–77](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs#L73-L77), [TRRM CLI:289–299](../../sources/terrumc/Program.TrRmExecution.cs#L289-L299), [error writer:434–456](../../sources/terrumc/Program.cs#L434-L456) | Public loading/reference APIs document invalid versus unsupported input. The execution-command catch collapses both into `TRM5001`, status 1. Experimental errors contain code/severity/message, not machine categories. |
| [Run dispatch:264–269](../../sources/terrumc/Program.TrRmExecution.cs#L264-L269), [combined API:96–114](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.StandardTrRm.cs#L96-L114), [JIT preparation/handoff:12–56](../../sources/Terrum.CodeAnalysis/Native/NativeJit.StandardTrRm.cs#L12-L56), [entry/cleanup:772–794](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs#L772-L794) | `ExecuteStandard` performs admission, lowering and JIT in one call; the CLI catch also encloses entry invocation and cleanup. Neither a base exception type nor the enclosing API name proves that failure preceded execution. No post-start exception of a particular type is claimed to have been reproduced here. |

Thus the broad distinctions are already determined, but treating every guard
or checked overflow as malformed is an implementation gap. The exception
mechanism, diagnostic assignments and actual layout envelope remain a genuine
decision. Correcting a TypeDef-only visiting key does not settle them.

### Explicit amendment of A3's exception sentence

Replace only this sentence in A3's provider/admission section:

> Existing index, initialization, unsafe, backing and runtime failure behavior is
> unchanged; this introduces no new exception or runtime policy.

with:

> Existing index, initialization, unsafe, backing and runtime failure behavior
> is unchanged. No new Terrum exception class, exceptional edge or runtime
> policy is introduced. Only operations explicitly selecting the exact A3
> successor use the host failure classes and bounded processing recipe in the
> A3 layout-failure companion proposal, including its single host-only
> resource-limit exception. A2's outward failure behavior remains unchanged.

This **intentionally supersedes the literal prohibition on a new host
exception**, rather than claiming the original wording already authorized it.
The source/runtime clauses are preserved. No `Core` failure, runtime unwind
path, failure bridge or persisted exception identity is added.

Keeping one undifferentiated exception/code cannot satisfy A3's distinction.
Using a physical-memory or execution-stack exception for a logical work limit
would conflate host failure with deterministic capacity. A result-union
migration of the existing compiler APIs is unnecessary for this bounded
decision; one typed host exception preserves their throwing API shape.

## Exact failure channels

The following mapping applies only to fixed-array production, validation,
binding and pre-execution lowering under the **exact A3 successor selected
above**. It is not a replacement for A2's API/CLI failure contract.

| Established failure | Host API result carrying the pre-execution marker below | Experimental diagnostic / CLI status | `terrum.cli/1` outcome / category / status, when the command is separately admitted |
|---|---|---|---|
| Malformed rows/references, wrong arity/owner/domain, missing required positive evidence, inconsistent representable geometry, incompatible selected binding, or a proven inline-storage cycle | `InvalidDataException` | `TRM5001` / 1 | `admission-error` / `artifact.invalid` / 3 |
| Mathematical field placement, rounding, nested extent or count-times-stride exceeds target `nuint` | `OverflowException` | **`TRM5005`** / 1 | `admission-error` / `artifact.invalid` / 3 |
| Structurally understood required form, constraint, feature, target or value route is outside supported admission | `NotSupportedException` | **`TRM5006`** / 1 | `admission-error` / `admission.unsupported` / 3 |
| Required processing exceeds the selected deterministic layout envelope, without a completed invalidity proof | `TrRmResourceLimitException` | **`TRM5007`** / 2 | `resource-error` / `resource.exhausted` / 3 |

`TRM5005`–`TRM5007` are **unapproved A3-only producer-code candidates**, not
TRRM registry allocations. Existing private-module codes
[5001–5004](../../sources/terrumc/Program.NativeModules.cs#L121-L140)
are not reassigned. Status 2 denotes incomplete tool-capacity processing in
the experimental CLI; it is not a substitute for the selected protocol's
status 3.

### A3-scoped boundary and A2 preservation

For `trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`, preserve existing
`InvalidDataException` failures, including target-overflow wrapping and
existing guard failures, and existing `NotSupportedException` failures.
Both retain experimental `TRM5001`, status 1. No new resource exception,
escaping layout `OverflowException`, A3 envelope or `TRM5005`–`TRM5007`
mapping may leak through an A2 request.

Shared helpers may use one internal failure representation, but must retain
an explicitly selected **A3-scoped boundary** for the new outward channels.
The A2 boundary preserves its existing projections and resource behavior.
Applying the new A3 limits to A2 and merely translating their failures into
legacy exceptions is not preservation: those limits must not govern A2.

For a root-plus-providers API, the explicit root operation selects the public
boundary. An A3 request may charge its required provider accesses to its A3
ledger, while each provider retains its own selected schema and admission
rules. An A2 request is not upgraded by supplying an A3 provider or reusing a
shared helper/cache; independent A2 provider validation remains A2. Do not
unwrap an established A2 `InvalidDataException` to manufacture an A3 overflow
or resource-limit failure.

### A3 host exception and propagation

The new public sealed host type is
`Terrum.CodeAnalysis.Modules.TrRmResourceLimitException : NotSupportedException`.
It exposes immutable `Operation` and `Scope` strings, respectively
`"trrm.a3-fixed-array-layout/1"` and `"root-request"`; `Dimension` is exactly
`"work"` or `"depth"`. `Limit` is an inclusive `ulong`; `Required` is a
nonnegative `System.Numerics.BigInteger` giving the proposed cumulative work
or active depth that failed reservation, with `Required > Limit`.
Construction is internal to the host implementation. Its message identifies
these facts and the affected input/Module when established.

At the selected A3 boundary, catch this subtype before `NotSupportedException`.
Preserve its type and facts through A3 structural validation, provider
comparison and public compiler APIs; do not wrap A3 resource failure or layout
overflow into `InvalidDataException`, dispatch on message text, or recover
classification by inspecting an inner exception.
Only an origin-marked `OverflowException` denotes A3 target-geometry
arithmetic: invalid encoded extents/reference arithmetic remain data errors,
and unrepresentable resource reservations remain resource-limit failures.
Neither `OverflowException` nor `NotSupportedException` alone proves the A3
failure class or execution phase.

Map these channels only at the explicitly selected A3 compiler/admission
boundary, not in an unconditional shared CLI catch or around option parsing,
file I/O or a started native program. Usage/input, authored source diagnostics
and execution results retain their own contracts.
Unavailable physical memory, cancellation and other host failures remain
distinct; never synthesize a deterministic limit receipt from them. Fatal
host failure may prevent diagnostic delivery under the existing CLI contract.

For experimental human output, emit `code: message` on stderr; for `--json`,
retain the existing `schemaVersion: 1`, `success: false` and diagnostic
code/severity/message shape. Add no category, fabricated source span, partial
success artifact or program return status. The last table column specifies
the already accepted machine-category mapping, not a machine-mode
implementation, envelope extension or adoption of TRRM command identifiers.

### Pre-execution host marker and `run-trrm` translation

The combined API needs **origin evidence**, not a broader catch of the public
base exceptions. Recommend one private, nonserialized
`A3AdmissionFailureMarker`, attached by exact exception-object identity using
a private `System.Runtime.CompilerServices.ConditionalWeakTable<Exception,
A3AdmissionFailureMarker>` in `Terrum.CodeAnalysis`. This is not another
exception subtype, an `Exception.Data` string key, an artifact fact or
execution authorization.

Expose the read-only classifier
`TrRmExecutableCompiler.TryGetA3AdmissionFailure(Exception exception,
out TrRmA3AdmissionFailureKind kind)`. Reject null with `ArgumentNullException`;
for an unmarked exception return false and `None`. The host-only enum is
`None=0`, `InvalidArtifact=1`, `TargetLayoutOverflow=2`,
`UnsupportedAdmission=3`, `ResourceLimit=4`, corresponding to the table's
four rows. These are public host API values, not wire/Feature allocations.
The classifier recognizes only the host's private marker on that exact
exception; it does not infer from base types, messages, `HResult`, `Data`,
inner exceptions or aggregate contents. No CLI friend assembly is required.

Create a marker only at the established metadata, binding, target-layout,
unsupported-admission or deterministic-limit failure origin under the exact
A3 root scope. Record the kind, exact root schema, root processing-scope
identity and pre-execution origin. Use an opaque per-root identity token that
does not retain the ledger, input buffers or binding caches.
Retain the existing `InvalidDataException`, `OverflowException` or
`NotSupportedException` instance and stack, or the selected
`TrRmResourceLimitException` and its facts. Thus public base-type compatibility
is preserved without new arithmetic/unsupported exception subclasses.
An unrelated host overflow or unsupported operation stays unmarked, even if
it occurs before execution. A catch enclosing arbitrary preparation work
must not stamp every base exception as admission.

For the combined call, reuse the A3 root processing scope to track a monotonic
**preparation → execution-handoff** phase. Admission and required lowering
may produce markers during preparation. In
[`NativeJit.ExecuteStandard`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.StandardTrRm.cs#L51-L56),
advance the phase immediately before calling `ExecuteCode`, after code
emission, buffer validation and import-module construction. No A3 layout
charge, marker creation or retagging for that root is permitted beyond this
handoff.
Do not wait for `entryPoint()`: `ExecuteCode` also performs
[host setup and library loading](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs#L514-L526)
[before entry](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs#L639-L665).
The handoff is conservative; it does not assert that the program actually
started or returned if setup fails.

The A3 `NativeModuleCompiler.ExecuteStandard` exit guard must observe this
scope across the entire JIT call, including cleanup. If an exception escapes
after handoff, remove any marker on that exact exception and rethrow the same
exception with its original stack. This also prevents a previously marked
exception rethrown by a nested operation during invocation/cleanup from
masquerading as this root's admission failure. Before handoff, preserve only
an already established marker from the same root scope; discard an unrelated
marker rather than promoting it into this root's admission result. Never
manufacture a marker from the base type.
Unmarked host failures remain unmarked in either phase. The A2 branch does
not acquire this new scope, marker or translation.

The CLI maps 5001/5005/5006/5007 for a combined A3 call **only when the
classifier succeeds**. Put that A3 invocation in a marker-filtered catch
scope outside the existing legacy broad catch region; its unmarked failures
must not fall through to the old artifact/input catches. Preserve those
legacy catches for A2 and for their existing input/option scopes. An unmatched
combined-call failure propagates as its original execution/host failure to
the existing handler, or propagates unhandled if none applies; it is never
relabelled as admission or a normal program return. This recommendation adds
no execution-failure diagnostic or machine-protocol implementation.

## Bounded constructed processing

Recommend the A3-only implementation recipe **`trrm.a3-fixed-array-layout/1`**,
activated only for the exact A3 operation before its first resource-consuming
action. A2 retains its existing accounting and guards:

* The initial declared host envelope is **100,000 work units and 128 active
  logical frames**, both inclusive. These numerical candidates match existing
  [host guard values](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs#L32-L33),
  but have this recipe's own meaning and ownership; they do not import source
  proof/constant-execution policy. They are neither source/schema maxima nor
  portable minima. No new public limit-selection API is required here.
* One selected A3 root operation shares one layout ledger across all selected
  array rows, nested types, expected/actual providers and required layout
  revalidation. A direct public A3 validation request starts its own ledger.
  Internal helper/context creation, another Module or a provider lookup
  cannot reset it. Independent root requests start fresh ledgers.
* Use explicit logical work frames throughout substitution, complete-identity
  formation/comparison, actual qualification/evidence checking and layout
  expansion. Do not depend on unbounded host recursion, including recursive
  key stringification. A frame is one incomplete type-term normalization,
  actual-admission check, storage derivation or provider comparison. The first
  obligation has depth one; each dependent child adds one while its parent
  remains active. Scalar helpers and counter checks do not add frames.
  Changing physical stack organization does not erase logical depth. Reserve
  before pushing or constructing child state.
* Follow [`resource-accounting/1`](../specs/representation/resource-accounting.md#5-deterministic-work-depth-and-audit-recipes):
  each fixed-width field access, reference/range check, bounded scalar
  comparison/arithmetic operation, actual/formal mapping, type-term
  transition, field/array edge, key-node/edge comparison and frame transition
  costs one work unit, plus one per payload byte examined, copied or
  generated. List elements and nested actions are charged individually.
  Each new logical state entry reserves one additional work unit; nested
  actual slots, key nodes/edges, frame records and cached decisions count
  individually. Reserve the complete byte charge before constructing variable
  payload. This bounds constructed entries and payload by cumulative work;
  captured input is not a newly copied layout payload.
* Traverse array rows in ascending row order, ordered actuals/constraints and
  stored fields in ordinal order, and child terms depth-first left-to-right.
  Provider checks retain the selected input order. Physical caching cannot
  waive charges; completed-decision reuse requires the exact unchanged
  input/schema/target/binding context and charges its lookup, key comparison
  and reuse checks. An in-progress decision is never successful evidence.

Keep complete normalized constructed-instance keys, including ordered
qualified actuals, using/defining/actual-provider origins, selected target and
schema/rules. Cycle detection concerns the active **inline-storage ancestry**,
not repetition of a TypeDef or an unrelated formation/fact request.
Normalization and key comparison consume the same ledger before constructing
an unbounded key. Existing source
[generic/field grammar](../specs/grammar-spec.md#L657-L659) and
[default formal domains](../specs/language/types-and-normalization.md#L200-L214)
are not amended.

Under A3, finite `Outer<Cell<int32>>` and `Cell<Cell<int32>>` must succeed when
their complete obligations fit the envelope. A repeat of the exact active
inline instance is invalid, including through zero-count arrays. A source-free
metadata fixture whose field template is `A<A<T>>` inside `A<T>` can instead
keep producing new instances. Exhausting the envelope on that expansion
reports capacity, **not a fabricated cycle or malformed-type proof**. This
does not approve such a source declaration or certify its producer promises.
A stronger invalidity proof is not required merely to decline unfinished work.

There is no omniscient failure priority: reserve before each action, report
an invalidity or overflow only when established, and otherwise report the
first failed reservation. With simultaneous resource failures, report work
before depth. Do not inspect unvisited input to change that outcome.
Count `N` is one fixed-width input, not `N` expansion steps: do not enumerate
elements or allocate `N * stride` bytes to validate geometry. `N=0` still
checks element formation, evidence, storage cycles and nested arithmetic.
No 4096-byte cap, narrowing, saturation, fallback provider or automatic
larger-envelope retry is introduced.

## Publication and integration owners

These A3 failures publish no partial bound program, reference, binding plan or
native image, and commit no changed selected binding. Discard unfinished memoized
facts; do not retain them as successful validation evidence. Complete the
affected admission/layout work before starting execution or replacing output.
Keep existing output byte-for-byte on such failure.

The existing
[staged writer](../../sources/terrumc/Program.Compilation.cs#L421-L446)
already replaces only after complete output is written. Preserve
[per-artifact atomicity](../specs/cli-protocol.md#L216-L222), not an invented
multi-output transaction: unrelated or earlier committed outputs are not
rolled back; a later machine-mode failure reports them with `complete: false`.

Integration changes the quoted A3 sentence and links its failure/termination
gates to this companion. Resource accounting remains the capacity owner;
Windows layout/IL metadata retain validity and target arithmetic ownership;
CLI diagnostics retain tooling ownership. The
[schema selector](trrm-draft-schema-profile-selection.md) and exact A3
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1` composition
are unchanged: no TypeSpec, row, flag, Feature or wire revision is added.

Host implementation owners are `TrRmFixedArrayLayoutValidator`,
`TrRmFixedArrayGenericProfileValidator`, structural metadata validation,
provider binding and all public APIs exposing their results. The current
[structural dispatch](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs#L1661-L1676)
and [provider layout calls](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayProviderBindingValidator.cs#L256-L275)
must activate/share the ledger and preserve these failure classes only at the
selected A3 boundary, retaining A2's existing behavior through shared helpers.
`terrumc` TRRM validation and execution-command handlers own the A3-only
marker-based diagnostic mapping. `NativeModuleCompiler.ExecuteStandard` and
`NativeJit.ExecuteStandard` own the preparation/handoff scope and post-handoff
exit guard; the compiler owns the private marker and public classifier.
API exception documentation and the experimental CLI support inventory must
distinguish A3 from unchanged A2 behavior. This document changes neither those
files nor the accepted specification.

## Focused acceptance gates

These are future required tests, not passing evidence. Except for the explicit
A2 non-regression/isolation gates, the new outcomes below require the exact
A3 successor. Use public compiler APIs and independent metadata fixtures,
plus the existing supervised CLI runner; do not add `InternalsVisibleTo` or a
runtime test opt-in.

| Case | Required result |
|---|---|
| Finite repeated definition | Local and selected-provider `Outer<Cell<int32>>`, `Cell<Cell<int32>>`, equal-layout distinct actuals, and equivalent inline/indexed actual encodings succeed with correct identity/geometry. Extend the [existing finite/cycle fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs#L557-L597), without claiming they were run here. |
| Invalid versus unsupported | Bad arity/owner/reference, absent Copy/Unique evidence and forged representable geometry give data errors/5001; structurally understood but unsupported constant-formal, constraint or value requirements give unsupported/5006, including at zero extent. Reserved or malformed encoding is not “unsupported.” |
| Proven cycles versus capacity | Direct/mutual exact inline cycles give data errors; pointer recursion does not expand pointee storage. Growing `A<A<T>>` storage terminates with the typed resource failure/5007, not stack overflow, timeout classification or malformed input. Supervise the hostile fixture to detect crashes, not to define its resource charge. |
| Target arithmetic | For A3, independently isolate count multiplication, aggregate addition, alignment rounding and nested overflow, including an outer zero count. Assert escaping overflow/5005 rather than an invalid-layout wrapper. Add A3 counterparts of the [existing A2 invalid-geometry cases](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs#L222-L233); preserve those A2 expectations. |
| Compact numeric boundaries | Zero/one/max-u64 counts; Transparent-over-void yields zero stride/extent, and a one-byte element can have representable max-u64 extent without proportional work. Layout success establishes no backing allocation or executable support. |
| Inclusive envelope | Fixtures with independently derived recipe charges at each limit succeed if otherwise valid; one unit/frame beyond fails with exact operation/scope/dimension/limit/required facts. Exercise shallow work exhaustion and deep depth exhaustion separately; no silent reset or wrapped reservation. |
| Root aggregation and reuse | Nested helpers and expected/actual providers share charges. Completed context-exact reuse is charged; incomplete or differently bound/qualified/schema-selected facts are not reusable. Independent root requests start fresh. |
| End-to-end admission-failure publication | Public validation/loading/reference and available emission paths preserve each marked admission class. Human/JSON CLI codes and statuses match the table. Every marked admission failure preserves existing output and starts no program; a subsequent valid request succeeds without contaminated memo/binding state. |
| Marker and base compatibility | Real A3 admission failures preserve their public base types, instances/stacks and resource facts, and classify to the exact kind/code. Bare exceptions with identical types/messages/`HResult`, fabricated `Data` keys and wrapped/aggregate inner markers do not classify. No public API can attach a marker. |
| Combined-call phase separation | Controlled headless host-boundary tests cover preparation failure, execution-side setup failure, entry/invocation failure and cleanup failure, including rethrow of a previously marked exception. Only marked preparation failures produce admission diagnostics; post-handoff exceptions lose their marker and preserve their original exception, with no legacy artifact/input fallback or fabricated return. Do not add source/CLI test switches or visible/hardware opt-ins. |
| A2 non-regression | Under exact A2, malformed/overflow/guard fixtures retain their existing `InvalidDataException` behavior and unsupported fixtures retain `NotSupportedException`; human/JSON CLI failures remain `TRM5001`, status 1, with output preservation. Established A2 results remain unchanged even for inputs crossing the new A3 envelope thresholds. Assert no new marker, resource exception, escaping layout overflow, envelope refusal or 5005–5007 diagnostic through shared helpers and public APIs. |
| A2/A3 boundary isolation | Run A2 requests before and after A3 success/resource failure and reuse shared implementation caches. An A3 provider must not upgrade an A2 root. A3 may consume independently admitted nongeneric A2 providers without changing their schema or standalone A2 validation behavior. The root's explicit selector, never provider presence or cache state, controls the new outward channels. |

No further choice remains within this recommendation. Numerical portable
minima, a public configurable-envelope API, general generic termination
proofs and full `terrum.cli/1` command migration remain independent work,
not implicitly approved prerequisites for this bounded host mechanism.
