# Scope, authority, and conformance

## Scope

Terrum specifies source programs, their abstract execution, published
declaration contracts, semantic interchange, and explicitly selected target
and library profiles. A compiler's implementation language, backend, current
support inventory, or optimization strategy does not define these contracts.

This is the development specification for Terrum 1. It does not require
compatibility with unpublished alpha source, implementation projections, or
retired artifact schemas. A retained artifact must be identified and admitted
under its actual schema; draft status is not permission to guess its meaning.

## Normative status and authority

An unqualified requirement in a specification chapter is normative within
that chapter's stated feature and profile scope. "Must" and "shall" express
requirements; "may" expresses permission; "should" expresses a recommendation
whose omission does not by itself violate conformance.

A passage explicitly marked informative, rationale, example, or comparison
does not add requirements. Pseudocode illustrates the rule it cites and does
not implicitly approve another source form, library identity, or encoding.
An external reference is informative unless a target or interchange chapter
imports a precisely identified part as normative.

An explicitly unassigned or open design is not an admitted semantic feature.
An implementation may expose it only as a separately identified extension,
not as part of an unchanged standard profile. A proposal, same-named API,
attribute, private enum, or implementation milestone cannot activate it.

Each observable rule has one canonical owner:

| Partition | Canonical responsibility |
|---|---|
| Overview | Scope, terms, status, conformance claims, and profile dependencies |
| Source language | Source transformation, lexical and grammatical forms, names, complete types, checking, binding, and evaluation |
| Abstract runtime | Storage, lifetimes, ownership, roots, cleanup, failures, completion, and shared-memory observations |
| Semantic representation | Published identities/contracts, logical and physical records, instructions, admission, linking, and rewriting |
| Targets | Exact representation, call-interface, foreign-boundary, and target support contracts |
| Libraries | Adopted protocol/API identities, signatures, behavior, dependencies, and provider requirements |
| Tooling | Machine schemas, statuses, diagnostics, coordinates, and nonsemantic artifacts |

Other chapters summarize or apply the canonical rule; they do not define a
different default. A contradiction is a specification defect to be corrected,
not an implementation choice. Implementation documentation reports support;
it cannot weaken normative behavior. Unapproved proposals are outside this
authority hierarchy.

### Source-equivalent shorthand

A source construct defined as shorthand must have an expansion into ordinary
source operations that makes its existing scopes, evaluation points, control
transfers, initialization, dependencies, effects, and cleanup explicit.
The expansion must not invent a shorter lifetime, silently retain an old
owner, fabricate a default, or acquire authority to make invalid source valid.
Compiler-owned primitives have their own explicit contracts; pseudocode cannot
introduce one merely to fill a missing rule.

This is a semantic requirement, not a demand to emit the expanded text or
machine code literally. Physical lowering may use different representations
and optimizations only when it preserves the specified observations and
validity. In particular, storage declaration is not value initialization,
and a reusable local is not an unbounded collection of retained instances.

## Conformance objects

Conformance claims name the object, specification edition, supported features,
target where applicable, and any permitted implementation-defined choices.

| Object | Required obligations |
|---|---|
| Source implementation | Recognize and check the selected source language, diagnose invalid or unsupported requirements, and preserve its abstract execution |
| Source program | Satisfy the selected syntax, static rules, declared requirements, and runtime preconditions |
| Semantic producer | Emit valid identities, complete published contracts, required features, and records satisfying the selected schema and producer obligations |
| Semantic consumer or linker | Perform required admission and compatibility checks and preserve contracts when resolving, transforming, or replacing artifacts |
| Runtime | Implement the selected execution, lifetime, cleanup, failure, memory, and completion contracts |
| Target provider | Supply the specified representations, procedure-call interface, and required support guarantees |
| Library provider | Implement adopted signatures and behavior without weakening effects, authority, output state, or dependencies |
| Tooling provider | Produce and consume the selected machine schemas, diagnostic categories, and coordinate units |

Supporting source syntax alone is not semantic conformance. Recognizing a
record or opcode is not execution support. An AOT and a JIT implementation of
the same selected features preserve the same observable language contracts.
Private optimizations can change representation, not caller validity.

[Interchange and private persistence](../representation/persistence.md)
selects `.trrm` as normative serialized semantic interchange. Development
`.trm` and native `.trmn`/`.tnset`/`.trng` serializations are implementation
contracts. Their extensions, common magic and current revisions do not select
a standard schema. Native ABI, binding, ownership, deployment and code lifetime
remain separate obligations; CLI protocol conformance does not standardize
its artifact payloads. Private serialization details belong in implementation
references, without an obligation to retain unpublished alpha readers.

[Windows x64 native](../targets/windows-x64.md) specifies its selected fixed
rootless storage, call/ownership and deployment boundary. Direct foreign
primitives require formal platform equivalents; every core scalar still has
its Terrum storage and required source semantics. Default foreign calls are
direct and non-unwinding, with exception interchange a separately selected
special [boundary-contained bridge](../targets/windows-x64-failure-bridge.md).
Its paired adapters return an owned failure packet before resuming local
propagation, without admitting a foreign unwinder through Terrum frames.
Target/rule agreement does not activate missing physical
records, managed/view/task crossings or private package standards.

[Local failures](../language/local-failures.md) define the nominal Exception
capability and explicit readonly reference versus Copy-gated catch bindings.
The propagation extent retains its owner through selection and active rethrow;
an input copy is not a captured event. Category witnesses do not manufacture
interface storage, a universal base or boxing. Other compiler/runtime failure
catalogs and physical payload/handler/cause support remain separately admitted.

[Runtime requirement failures](../language/requirement-failures.md) select
ordinary `new E()` with zero supplied arguments on the failed path, primary callable
RequireException recipes and exact Core RequirementException fallback.
Explicit selectors win; helper/lambda/generation/async execution retains
each check's semantic owner. Constructor failure is its actual failure,
not a fabricated E or permission to erase initialized outputs. Other failure
catalogs and complete physical constructor/default/handler records remain open.

[Captured exceptions](../language/captured-exceptions.md) select one opaque
affine Core CapturedException and direct no-argument Capture in the active
source handler body. Publication transfers the existing event, not its payload
copy; catch borrows keep their original extent. Named consuming throw requires
move and preserves origin/causes/dependencies. Transferred handlers cannot
reuse or retire that responsibility. No shared/typed family, thread capability
or physical capture/cause codec follows.

[Binding and replacement](../representation/binding-and-replacement.md)
uses checked directional satisfaction of every retained expectation, not
equality of physical rows or automatic semantic-revision roll-forward.
Exact state/phase/representation promises survive stronger effects, and
optimizations retain any stronger provider facts on which they rely.

[Contract clause applicability](../representation/contract-clauses.md)
separates required row/owner/subject checks from body truth and feature support.
An inspectable output/cleanup shape or all-zero reserved variant does not
enable its missing publication, ownership or execution contracts.

[Reader trust](../representation/reader-trust.md) separates complete producer
validity from access-scoped consumer checks. Trusted indexes permit selective
reads without a preliminary Body scan; bounds and exposed contradictions
still reject. Whole-artifact validation, semantic audit and canonicality
are distinct claims. Table-specific references have schema-owned namespaces.

[Encoding canonicality](../representation/encoding-canonicality.md) requires
minimal writer widths and logical-first-use maps while ordinary readers
accept equivalent, otherwise valid noncanonical encodings. Explicit audits
reject deviations in their stated scope; no universal general/compact form
or preliminary trusted Body scan is required. Invalid fields, values,
references and unsupported affected semantics retain their rejection rules.

[Resource accounting](../representation/resource-accounting.md) separates
representational validity from scoped supported capacity, deterministic
structural/audit work and live processing state. Portable minimum supported
envelopes are intended, with declared higher limits; numerical minima remain
unassigned rather than inferred from format maxima or fixture limits.
Exhaustion declines the requested use/claim, not otherwise valid input.
Host failures and the source proof/ConstEval envelope remain distinct.

## Language floor

The `language` floor is independent of the Core library's general-purpose
APIs. It includes the mandatory scalar identities, source and checking rules
for the selected freestanding language, required literal construction, and
the portable proof and constant-evaluation substrate those rules require.
An ordinary package cannot replace primitive identities or redefine built-in
arithmetic.

The [library inventory](../libraries/adopted-inventory.md) distinguishes
selected logical roles, incomplete families, design direction and deferred
APIs. It indexes canonical contracts rather than extending this floor or
assigning physical records. A provider's profile supplies each claimed
surface's complete identity/signature/phase/responsibility/backing and actual
source/runtime/target/physical closure. A candidate sketch, matching name or
dependency tier is not that evidence.
[Provider/consumer scenarios](../libraries/design-scenarios.md) and their
[library cases](../conformance/source-library-contracts.md) retain the same
admission boundaries without claiming implemented support.

[Source input](../language/source-input.md) defines strict UTF-8 compilation
files and immutable UTF-16 lexical snapshots. Byte-input failures and source
code-unit diagnostics are distinct; a host decoder cannot replace invalid
bytes or normalize source spelling.

[Literal construction](../language/literal-construction.md) defines
target-owned kind/signature records, contiguous signed numbers, raw numeric
versus validated decoded text input, and direct primitive range/rounding/
cohort semantics. Selection precedes value acceptance, and a registration
cannot activate an unassigned physical carrier.

Compiler-time parser or evaluator dependencies are not automatically linked
runtime dependencies. Conversely, an unavailable compiler-time requirement
cannot be silently replaced by runtime parsing or a fabricated constant.

### Mandatory scalar catalog

| Category | Reserved identities |
|---|---|
| Zero-data and Boolean | `void`, `bool` |
| Code units and scalar | `char8`, `char16`, `char32`, `rune` |
| Signed fixed-width integer | `int8`, `int16`, `int32`, `int64`, `int128` |
| Unsigned fixed-width integer | `uint8`, `uint16`, `uint32`, `uint64`, `uint128` |
| Target-width integer | `nint`, `nuint` |
| Binary floating point | `float16`, `bfloat16`, `float32`, `float64`, `float128` |
| Decimal floating point | `decimal32`, `decimal64`, `decimal128` |

All 26 identities are required by the language floor. The target fixes native
integer width and physical representation through its target contract.
Missing direct hardware support does not remove a scalar identity: software
operations are permitted when they preserve the specified values, rounding,
failures, effects, and interoperability. A runtime helper remains an explicit
dependency where the target implementation needs one.

[Built-in scalar operators](../language/primitive-operators.md) defines
the required signatures and arithmetic/count/observation domains across
that catalog. Being a scalar does not grant every operation: Boolean,
character and unit availability remains explicit, and typed numbers do
not implicitly promote.

[Absolute-value members](../language/absolute-value.md) separately fix the
adopted numeric API's same-type static Abs shape. Signed integer minimum
fails with checked overflow and no result; unsigned values are unchanged.
The floating magnitude classes preserve their selected format.
This API contract does not redefine the primitive operator floor or
approve a generic-math hierarchy, unchecked variant, failure catalog or
missing physical member/call representation.

[Numeric selection members](../language/numeric-selection.md) fix the adopted
same-type Min/Max/Clamp signatures on all 20 numeric scalars. Min/Max preserve
the selected NaN-propagating and signed-zero rules. Clamp checks its published
min <= max precondition, explicitly using exact Core InvalidBoundsException
for reversed or unordered bounds; an out-of-range value instead clamps
normally. These calls preserve ordinary argument order, selected non-NaN
input representations, failure/result state and actual effects.
[Numeric capabilities](../language/numeric-capabilities.md) separately select
the grouped Core witnesses and optional scalar composition.
[Floating environment rules](../language/floating-environment.md) require
sign-only Abs/negation, quiet processing-result NaNs and a deterministic
admitted constant recipe without an ambient language status facility.
These logical contracts do not supply missing physical execution support.

[Core scalar checked failures](../language/scalar-failures.md) separately
select zero-data nominal OverflowException, DivideByZeroException and
ArgumentOutOfRangeException payloads. Their exact arithmetic/conversion/count
mapping preserves no-result failure and complete source event/phase state.
It does not turn floating IEEE outcomes, diagnostics, expected API results,
termination or hardware faults into local exceptions, nor activate missing
payload/event/handler codecs.

[Standard runtime categories](../runtime/standard-failures.md) additionally
select fixed postcondition, checked-index, recoverable-allocation, cycle and
failed-unit-access payloads. Initialization delivers its actual escaping
event to its initializing flow; waiting/later demands construct fresh
InitializationFailedException without sharing the original event or retrying.
Actual recoverable-publication and cleanup/resource support remain required.

The [portable proof and constant-execution floor](../language/proof-and-constant-evaluation.md)
defines required scalar fact/transfer rules, concrete target execution,
executed-path eligibility and a minimum guaranteed resource envelope.
Structural initialization safety is not optional numeric analysis.
Private implementation limits cannot replace that guarantee; missing body,
registration or value-ABI support is an explicit unsupported requirement.

[Constant-generic domains](../language/constant-generics.md) are narrower than
ordinary constant values: fixed integers, Boolean, admitted nominal enums and
checked Transparent integer/Boolean wrappers with existing construction,
validity and frozen Copy/Blittable/value-codec commitments.
No additional attribute or carrier protocol is required. Canonical argument
identity retains the complete nominal domain and carrier value, without
executing user equality or erasing validity. Required construction and
physical generic/value records keep their independent admission obligations.

[Registered Core queries](../language/registered-queries.md) bind exact
compiler-known identities, not arbitrary same-spelled helpers.
SizeOf/AlignmentOf are ordinary nuint-returning generic value-layout APIs;
the complete subject and selected target determine the observation.
Sized supports runtime evidence without a universal compile-time number.
Retained layout-dependent results keep published fixed promises or explicit
coupled rebuild/rebind/invalidation obligations through derived uses.
The result's scalar codec does not freeze a replaceable queried layout.
No new query keyword, reflection object or physical intrinsic ID is assigned.

The [published-predicate and effect contract](../language/published-predicates-and-effects.md)
defines logical input-state purity, checked form authorization, inherited
evaluation modes, complete finite reads/effects and initialization results.
Its optional NoAlias parameter schema requires checked accessed-region
separation for the invocation, not exclusive ownership or new access authority.
`ConstEval` opts into required Terrum execution and implies pure-form
support; `Pure` alone does not opt into that interpreter.
Resolved mode queries are compiler-time semantic inputs, not dependencies
on mutable runtime state or an allocating runtime.
Physical persistence of these contracts still requires separately admitted
records and value codecs.

[General dependencies and views](../language/dependencies-and-views.md) use
whole receiver/formal sources, distinct result and retained destinations and
per-source backing-only refinements. DependsOn, Captures and ForwardsBacking
are checked Core schemas, not implicit owner acquisition or lifetime extension.
Stored values, erasure, copies and joins preserve every applicable source,
backing, shape, owner/guard/lease, root, load and affinity obligation.
An explicit member-contract constraint can require a published witness fact;
private body inspection cannot narrow a generic exported relationship.
This logical selection supplies no Span anchor, collector, task/closure
representation or physical source/capture codec.

[Task responsibility](../language/task-responsibility.md) selects affine Core
Task<T>, compiler-owned consuming await with explicit move of named owners,
exact payload/output/terminal observation and every-exit settlement or transfer.
Status, cancellation, a discarded result or a collector root cannot discharge
those obligations. No hidden blocking, async scope join, suppression or
supervision is supplied. General observer/awaiter/supervisor/cancellation APIs
and physical task/wait/root codecs remain separately admitted.

[Thread capabilities and sharing](../runtime/sharing-and-handoff.md) select
explicit user-type opt-in and capability-owned ordinary where conditions.
An unmet condition does not invalidate a confined instantiation. Actual
handoff, immutable publication, scoped exclusion and scalar access have their
own checked authority, alias, endpoint and lifetime contracts. Custom/native
algorithms retain existing scoped-unsafe and semantic producer/audit trust.
These logical rules alone do not admit physical tokens or a target profile.
[Concurrent executions](../runtime/concurrent-executions.md) separately
defines exact location/source generations, communication, coherence,
required access order, mixed SC/full boundaries, failed-attempt effects and
finite grounded causality. Its [required cases](../conformance/runtime-concurrent-executions.md)
distinguish finite supplied-fact/observation assessments from arbitrary
source/native proofs, compiler/runtime behavior, collectors and target
refinement. No physical allocation or primitive catalog follows.

[Owning fields and cleanup](../language/ownership-and-cleanup.md) permit
structural partial moves under ordinary write/init authority while preserving
remaining live fields. Authored Cleanup requires a complete receiver and runs
before automatic reverse-declaration fields; failed construction instead
unwinds reverse acquisition without incomplete-parent Cleanup. Field repair,
replacement, exceptional transfer and retained dependencies remain checked.
Partial state is not a new type argument, ordinary call value or physical codec.

[Construction handler registration](../language/construction-handlers.md)
uses target-owned constructed builder recipes over the target's declared
formals. Binding a recipe does not admit an open-generic type value,
an operation protocol, default allocating text/collection, or a constant
value codec; each affected use needs its complete construction dependencies.
Selected source completion uses the exact Core IBuilder consuming requirement,
with its complete result relationship. It transfers the private builder,
publishes a live result only on true and leaves the output vacant on false
or checked exceptional completion; residual/partial owned state is cleaned
without source revival. Explicit reusable builders need a checked adapter.
Acquisition uses the exact parameterless Core IBuilderFactory requirement
after eager captures. Runtime context requires ordinary explicit APIs;
type-only registration is not a closure or implicit provider selection.
Standard text drain uses exact Core writer/value members and int32 minimum
alignment measured in Unicode scalars, with no formatter replay. Collection
acceptance retains exact item and contextual result identities, composed
factory/completion roles and separately admitted spread item/cleanup contracts.
Supported construction uses eager source-order capture before acquiring its
builder and draining. Refusal does not suppress capture effects; formatting
and spread enumeration remain reached drain operations.
Plain value construction maps Boolean refusal to ordinary require failure,
not a successful prefix/default target; explicit consumers retain their
declared outcomes.
Resolved parameter-local TextTarget admits checked eager typed IFormattable
content through the selected callable's input contract. It is not a
callable-wide name argument, generated deferred producer, boxing permission
or new overload-ranking preference. Captured backing/ownership and consumer
retention obligations remain explicit. Equivalent complete content shapes
share one generated nominal type within the originating module, not across
modules; captured values/literal text remain separate instance data.
Incremental/deferred source evaluation and construction policies are not
admitted by this rule set.

[Standard value iteration](../language/iteration.md) selects exact Core
ICursor<T>/ISequence<T, TCursor> contracts for synchronous by-value loops
and collection spreads. TryMoveNext publishes one complete item on true and
leaves the destination vacant on false; checked failure preserves unpublished
output and cleanup-safe cursor state. Sources are readonly sequence factories
or supplied direct cursors, with ordinary explicit moves for named affine
values. Required Close runs on traversal exit, not as disposal of the source
collection. Eagerly captured direct cursors close even if drain never reaches
them; unreached factories are not acquired.
No universal Copy bound, Current discovery, implicit borrowing/consumption,
producer frame, async/lending contract or physical operation is supplied.

[Async value iteration](../language/async-iteration.md) separately selects
exact IAsyncSequence<T, TCursor>/IAsyncCursor<T> with synchronous factory or
direct acquisition, completion-published TryMoveNextAsync(init ref<T>) and
awaited CloseAsync. Pending destination/cursor access is reserved until checked
completion observation and cleanup establish the exact live/vacant relation.
Task status, cancellation request and same-shaped Task<bool> alone supply
no output proof; forwarding preserves operation/formal/version relationships.
This logical contract does not select general task/cancellation/producer
registration, lifting, lending, or physical phase/output/task representations.

[Lending iteration](../language/lending-iteration.md) selects distinct
readonly/writable synchronous/asynchronous nominal roles. A single init
output publishes a descriptor to an existing live element, and the loop name
aliases its exact T place. Writable factories require mut receivers; mutable
descriptor storage does not grant writable backing. Published loans retain
the cursor-bound default unless an available checked backing/invalidation
refinement applies. Every source/root/guard and surviving copy/reborrow remains
an obligation, including through async reservation/completion and close.
No element ownership, general annotation syntax, associated lifetime item,
automatic Span/List witness or physical relationship layout is selected.

[Async execution](../language/async-execution.md) starts ordinary async work
eagerly after logical operation establishment. Argument/failed-establishment
failures are synchronous; later escaping checks/body/cleanup fail completion
regardless of suspension. Completion and responsibility are not task status
or cancellation requests.
[Generated producers](../language/generated-producers.md) instead synchronously
create owned named affine IteratorCursor results. Creator clauses/effects
govern capture/creation; selected cursor operations govern lazy body/yield,
completion and entered-scope-only close. The body keeps one activation, and
cursor generation does not imply Copy, allocation, erasure or a new lifetime.
Physical task/frame/phase/root/loan programs remain separately required.

`void` has the single zero-data value `()` and is an ordinary Copy value
type, not a result-only marker. Logical initialization, parameter/tuple
positions, and type identity remain even when there is no payload storage.
It does not convert arbitrary values into ignored results.
[Values and execution](../language/expressions-and-statements.md)
defines its distinction from explicit `_` discard.

Floating and decimal formats are distinct identities; equal storage size does
not make them interchangeable. A primitive name is an ordinary identifier
token with a reserved binding, except `void`, which is a keyword. Primitive
member definitions in the designated Core bootstrap do not create new
primitive identities or redefine their built-in operations.

## Feature and substrate dependencies

Language conformance alone does not require a tracing collector, allocating
heap, scheduler, operating system, or every target's optional hardware.
Features using those facilities retain explicit dependencies:

| Facility-dependent feature | Dependency that cannot be silently omitted |
|---|---|
| Managed storage and managed handles | The selected allocation, tracing, root, barrier, and code-lifetime contracts |
| Concurrent sharing and atomics | The shared-memory model, authority protocol, supported representation, and progress/coordination requirements |
| Async and producers | Completion, captured-lifetime, cleanup, affinity, and selected scheduling/close contracts |
| Fixed SIMD | Element/mask semantics and the admitted fixed-width target representation; semantics-preserving software lowering is permitted |
| Scalable SIMD | The target's genuinely scalable, sizeless representation and call/storage restrictions; a fixed preferred width is not a substitute |
| Foreign calls and callbacks | The exact foreign ABI, validity, authority, lifetime, and unwind-containment contracts |
| Independently consumed artifacts | Required schema/rule/feature identities and complete published contracts with before-use admission |

The corresponding canonical chapter closes each feature's inventory and
admission rules. An implementation declares its supported feature set. A
program or artifact requiring an excluded feature receives an explicit
unsupported-requirement result before affected execution. Merely observing
that code is uncalled does not waive required structural or unknown-form
checks.

The [TRRM registry](../representation/registry.md) supplies one exact-schema
allocation catalog, with separate metadata recognition, inspectable Body
shapes and bounded executable status. It integrates the selected binary32/
binary64 floating semantics without retaining overlapping draft byte aliases.
Family reserves and type codes do not activate pending operations or a
feature/revision protocol. A source implementation of the complete language
floor is distinct from a consumer of this bounded physical profile.

The [interchange admission contract](../representation/requirements-and-admission.md)
selects one encoding schema per artifact and exact governing semantic editions
and feature revisions per Module. Consumers admit the complete retained
dependency closure, preserving each Module's requirements through packing.
The [format-version contract](../representation/format-versioning.md) makes
released major/minor assignments stable exact schema identities. Beta/preview
drafts keep `(1, 0)` without pair-update or compatibility duties and still
require exact out-of-band agreement; no released schema is assigned yet.
Numeric revision order, the reader's default edition and producer trust do
not establish support or compatibility. Header/directory framing and natural
checks on accessed forms remain mandatory. Trusted selective access need not
scan uncalled code or all indexes; a broader validation claim checks its
stated scope under [reader trust](../representation/reader-trust.md).
The [Module manifest](../representation/semantic-manifests.md) encodes exact
governing editions and Feature requirements. Edition `1` preserves the bounded
interchange baseline; no extra capability IDs or richer codecs are registered.

A retained feature must have complete source/runtime contracts, records where
persisted, and applicable compatibility and transformation rules. Neither an
optional nonsemantic sidecar nor a private body replaces a required public
obligation. Hardware acceleration may strengthen implementation performance,
not weaken source checks or substitute a different value/ABI identity.

## Behavior and diagnostic categories

An invalid source construct violates a static rule and requires a diagnostic.
An unsupported requirement is valid only under a feature/profile the
implementation does not provide and requires an explicit rejection.
Malformed interchange violates the selected physical/logical schema and is
rejected before it can justify execution or transformation.

Implementation-defined behavior is a choice explicitly permitted by a
canonical contract and documented by the implementation. Unspecified behavior
is variation explicitly permitted by that contract without requiring a
single documented choice. Neither category is a default for a missing rule.
Checked runtime failure follows the selected failure policy and never
produces a success-shaped default.

Unsafe authorization transfers specified proof obligations to the author; it
does not remove explicit validation, imply unchecked arithmetic, or grant
arbitrary whole-program behavior after a violation. The affected unsafe
operation's canonical contract defines its obligations and guarantees.
