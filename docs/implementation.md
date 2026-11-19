# Implementation support

Terrum's accepted target contracts and its implemented subset are tracked
separately. The [specifications](specs/README.md) define intended behavior;
the [roadmap](roadmap.md) lists pending contracts and dependencies; and
[proposals](proposals/README.md) contain unapproved choices.

The [compiler conformance inventory](implementation/conformance-gaps.md)
separates accepted-contract implementation gaps from work conditional on
proposal approval, with evidence boundaries and acceptance gates.

The [library support inventory](implementation/libraries.md) keeps bounded
examples and API sketches separate from the selected library/protocol
inventory and its provider/consumer contracts.

The [development JIT proof](implementation/jit.md) records the implemented
process-start worker and private backend/memory behavior.
[Bounded layout directions](implementation/layout-profiles.md) retain the
initial public-layout slice and module/replacement observations without
narrowing the selected target's transitive eligibility or assigning a
standard target/Feature identity.

The [bounded conversion inventory](implementation/conversion-profiles.md)
keeps scalar-front-end projections and legacy output support outside the
source literal/conversion rules.

## Status at a glance

| Surface | Representative support | Open boundary |
|---|---|---|
| Source and semantics | Hosted C# compiler with bounded language, verifier, ownership and lifetime support; Terrum-authored compiler components remain bounded. | Remaining source, proof, ownership, dependent-runtime and standalone-compiler routes. |
| Standard TRRM | Exact schema-selected profiles include scalar interchange, initialization Bodies, query Bodies, native contracts, and bounded generic/class records. | Phase-1/2 clause persistence and failure context, full class/witness/provider closure, G1/G1L1 Write-feature re-emission/replacement, and broader remapping. |
| Private and native packages | Private semantic modules, native packages, Standard TRRM and their consumers remain distinct routes. | Remaining dependency, replacement, remapping and runtime paths in each route. |
| Native execution | Windows x64 PE/JIT and selected supervised AOT paths are implemented; emission alone is not execution evidence. | Broader cleanup, exception, ABI and runtime-policy qualification. |
| CLI and validation | Selected manifests and source routes are implemented; normal repository builds and tests remain the validation path. | Source parity, full-route qualification and the analyzer/style checkpoint. |

This is a navigation-level support summary, not a conformance claim. The
[conformance inventory](implementation/conformance-gaps.md) is the current
gate index; per-run results and input identities belong in qualification
artifacts.

## Hosted compiler

The compiler host is written in C#. It exposes a Unicode-aware, lossless
syntax API and a bounded Windows x64 native profile with direct PE emission
and a JIT worker. The implemented language subset includes checked scalar
operations and provisional Release wrapping selection for currently admitted
integer arithmetic, compact values and unique owners, conditional outputs, bounded
generics, managed roots/classes, and deterministic cleanup. Hosted
`CompilationOptions` currently defaults arithmetic selection to Development,
offers Release explicitly, and keeps runtime-failure policy independent. The
arithmetic selection currently applies to hosted source binding, direct
native/JIT lowering, eligible CLI source compilation and scalar frontend
subject binding; compiler/scanner component compilation remains Development.
Noninterpolated raw text is lexed and decoded with matched fences, multiline
framing/indentation, newline directives and Unicode-scalar validation.
Target-directed `utf8z` binding consumes the decoded value. Raw interpolated
text now tokenizes and parses dollar-counted holes with alignment and format
syntax; typed formatter construction, segment framing/newline decoding, and
runtime execution remain unfinished. Source generic method constant binding
now covers mixed-formal scalar, Boolean, enum and local transparent-wrapper
actuals, defaults and reached ConstEval construction. Imported generic
nominal type arguments additionally materialize validated frozen scalar
values and supply forward evidence through a pinned type default; expected
result evidence, conflicting values and explicit `_` without evidence are
covered. Imported enum/wrapper/ConstExecution nominal values, symbolic
constant forwarding in constructed type positions, source-free remapping,
persistence and native value lowering remain open.

An unapproved Feature 27/1 direction now has provisional G1/G1L1
interface-TypeDef and generic-MethodDef Write-constraint emission, reading and
per-Module admission; broader generic TypeDef producers, re-emission, provider
replacement and qualification remain open.

Source `ref<init T>` postconditions map through direct calls, saved bool state,
copies and short-circuit chains. Status reassignment, writable-alias calls,
descriptor rebinding and destination lifetime end invalidate the relationship.
Native JIT/PE publishes verified unconditional postconditions; Standard B+I
accepts redundant completion under its unconditional default. Scalar
`write ref<init T>` stores lower as `Initialize` then `Replace`. Outcome-
dependent predicates, general declaration-clause execution and explicit
contract-failure transport remain open.

Pointer-cell `ref<init ptr<T>>` initializes only the pointer cell, not its
pointee. Private module and Standard B+I/B+I+G routes retain the pointer/
pointee distinction; Standard Bodies require Feature 1/2. The unapproved
[typed raw-pointer-load proposal](proposals/standard-native-pointer-load.md)
has provisional B+I+P and B+I+G+P producers: kind `01` encodes typed loads
with `0x1B`; earlier identities and compact-indexed kind `08` remain
unsupported. Source production is limited to a source-ordered declaration/
assignment prefix, value-formal calls, a single-return Body and one raw load
with at most one total conversion. General CFG/effect order, nested
backing/provenance, optional/aggregate types, provider replacement and broader
runtime paths remain open.
The separate unapproved [F16 composition proposal](proposals/standard-native-pointer-load-native-parameter-composition.md)
selects exact B+I+P+F16 and B+I+G+P+F16 successors. Their distinct selectors,
`000E` producer path and Feature 16/1 closure are implemented without
admitting the rest of F. Source-free provider re-emission and native execution
cover both successors. Broader nominal type remapping, pointer provenance/CFG,
and remaining dependency/runtime routes remain open.
The provisional C1 and C1M1 identities now route the same bounded init-Body
subset through exact producer, structural validation, loading and re-emission.
Their combined Feature catalog accepts the G and G1 requirements together;
GenericCall Bodies remain open. The C1M1 type-root projection now binds
selected metadata-only class declarations and class TypeDef Imports through
their exact ModuleRef/ExportKey provider snapshots. It compares class flags
(including mutability), complete field TypeSpecs and declared Layout variants,
without constructing executable `BoundClass` layouts; provider-only class
declarations remain dependency closure rather than source lookup roots. Direct
non-generic struct dependencies are structurally bound and retained without
promoting them to standalone source declarations. C1 is not promoted from
unknown mutability. Type-only source-free re-emission now
rebuilds the selected class TypeDefs, TypeSpec/FieldDef/Layout maps,
ModuleRef/Import/Export rows and revalidates the provider closure. It also
reconstructs reachable local nongeneric class TypeDefs as unexported closure
through supported direct and wrapped TypeSpec forms, and preserves directly
referenced local or imported nongeneric struct TypeDefs as dependencies.
Generic TypeDef dependencies and interface/witness edges remain unsupported.
Source class capture now retains exact compiler-owned declaration anchors and
applies member-key completeness by exact profile: C1M1 type-only emission
requires no callable-member keys, while Construction-1 requires keys for
retained public constructors, methods and properties. The bounded C1M1 source
emitter includes every local source class TypeDef, but exports only explicitly
selected class roots and mapped fields. It publishes captured class TypeDefs,
fields, and explicitly mapped public FieldDef
ExportKeys with scalar, local normal class-handle, local plain auto-layout
struct and selected imported non-generic auto-layout struct stubs, including
root-owned TypeDef Import expectations.
Class field TypeSpecs preserve source write/optional
qualifications without synthesizing GC backing; focused C1M1 and EN1
regressions remain unverified under the active hold. It rejects mapped
constructor, method, and property keys rather than
silently dropping them; class MethodDefs, Bodies and construction records
remain unsupported in this type-only route.
The Construction-2 source-class producer also emits direct selected Standard
class TypeDef/FieldDef/layout stubs and remaps readonly class-handle TypeSpecs
in method signatures, including root-owned imported TypeDef expectations. A
readonly method can also read one selected public field through an imported
class-handle parameter; source binding, mandatory verification, Body-01 D8/DA
encoding and exact FieldDef/owner TypeDef Import checks are implemented.
Producer/consumer/provider-replacement and unselected-field rejection
regressions are added but remain unverified under the active hold. Multi-hop
class provider chains, optional or writable wrappers, nested imported-class
dereferences, mutable imported field stores, aggregate stores and source-free
class member re-emission remain open.
The exact C1M1 construction successor has a
class-free emission route and a Construction `0003` reader/writer that checks
I/P signatures, owner/mutability agreement, direct instance FieldDef
guarantees, bodyful actuals and bodyless imported expectations. Construction-2
Body validation now uses direct nominal normal-class TypeSpecs for I receivers,
P results/contexts and ordinary class receivers; updated fixture regressions
remain unverified under the active hold. The exact C2 source-class route now
emits local class TypeDef/FieldDef closure and real role-1 initializer I and
role-2 projection P MethodDefs, MethodSigs, Params, Construction clauses,
Feature 25 and Bodies for multiple non-partial constructors per class whose
fields are initialized directly from Copy scalar parameters, bounded
canonical scalar ConstantMap/SF1 values, or recursively safe local or selected
direct imported non-generic auto-layout struct parameters. The mixed
parameter/constant initializer regression remains unverified under the hold.
Partial-constructor pairs retain the primary
I/P export identity and use the implementing secondary Body. It preserves
explicit I/P ExportKeys and does not substitute `ClassTryCreate`; constructor
producer regressions are added but unverified under the hold.
Construction-2 class methods emit assigned register Body kind `01` for
direct-formal returns, fixed-width integer unary/binary operations (bitwise,
checked/wrapping negation and add/subtract/multiply, checked divide/remainder),
and strict floating/decimal negation and add/subtract/multiply/divide/remainder
for float16, bfloat16, float32, float64, float128, decimal32, decimal64 and
decimal128. Static and readonly-instance numeric comparisons use C2/C3;
Boolean equality/inequality uses C2 controls. Target-native integer Body
arithmetic remains outside the register validator's current type matrix.

Static and readonly-instance numeric arithmetic Bodies can load SF1 scalar
operands through ConstantMap; fixed scalar returns use the provisional Feature
`11/1` path and Blob section `0003`. Same-class static and readonly-instance
fixed-integer or floating CallSite bodies can feed a following same-typed
binary/comparison with a direct formal or SF1 ConstantMap operand.
Static and instance bodies support one-formal numeric local-copy returns and
ordered numeric copy chains feeding numeric binary/comparison operations.
Readonly instance bodies also return an explicit scalar parameter through
ReturnHome, emit direct scalar FieldRead/FieldWrite descriptors, and use
receiver/formal Homes in declaration order. Exact same-class static and
readonly-instance scalar/unit identity forwarders use assigned Body kind `05`;
empty unit instance bodies
are emitted. These producer and imported-caller regressions remain unverified
under the hold.

Other static and instance operations, local expression forms and control flow
remain open.
Source binding now admits ordinary by-value Unit parameters and `()` defaults; the CM1
producer/reader preserves their logical signature
positions and zero-width SF1 values, while Windows x64 lowering omits only
their payload positions. Focused source, metadata and JIT regressions are
added but remain unverified under the active hold. The provisional CM1 route carries checked defaults for its
bounded class MethodDefs, including constructor I/P, and composes the inherited
D1 generic MethodDef and interface owners under the exact 16-byte
MethodParameterSurface schema. Structural validation enforces complete owner
coverage, Feature 11/26 closure and same-view I/P typed-payload agreement;
generic and interface imports materialize the selected default view. Focused
producer, malformed I/P and composition regressions remain unverified under
the active hold. Class MethodDef Imports now bind row-scoped through their
owner TypeDef Import to the same selected provider, comparing signatures,
effects, modes and construction roles after TypeSpec remapping while retaining
separate expected/provider defaults and Import aliases. Provider-source-free
caller and compatible/incompatible provider regressions are added but remain
unverified under the active hold. Remaining gaps include non-identity
instance calls, broader nested field paths and aggregate stores, locals and
control flow, properties, constructor failure/cleanup behavior, class-backed
field initialization, source-free class method re-emission and its
replacement/remapping paths, multi-hop imported class provider chains,
optional or writable class-handle wrappers, nested imported-class dereferences,
transitive imported-struct provider chains, CLI transport, independent carrier TypeSpec
verification, generic TypeDef dependencies, interfaces/witnesses outside D1,
and runtime execution.
The user-authorized provisional
[CM1 class parameter-default proposal](proposals/standard-trrm-c1-class-parameter-defaults.md)
defines the exact candidate owner/schema and remains pending later review.
The checked development and Standard TRRM type-reference
paths now carry exact generic nominal TypeDef/provider identities, formal
domains/defaults, ordered substitutions and TypeSpec qualifications into
source binding. A host
may select one exact `Terrum.Threading.Tasks.Task<T = void>` definition for
signature identity; selected Task-returning declarations now retain the
registered operation-capture default over eligible receiver/formal sources,
separately from completed-payload dependencies. Explicit method-targeted
captures can narrow that set, including to empty. These contracts pass through
selected Standard TRRM source references and are structurally verified.
Source-level async bodies, operation-result provenance at call sites, private
generic nominal persistence, Task ownership transfer/consuming await and
native execution remain unsupported. Standard executable persistence and
private semantic/native modules still reject these carriers.
The
[unapproved private-persistence proposal](proposals/source-arithmetic-private-persistence.md) is
provisionally implemented for structured semantic modules and their native
package, set, and graph embeddings. The selected fail-fast development
executable profile also preserves resolved arithmetic through its existing
linear, indexed and ScalarInitOutput controls. The bounded ScalarInitOutput
route also carries `ref<init T>` TypeSpec `0x0E` and selected scalar writes,
local formations, compatible reborrow forwarding, and conditional live/vacant
states; it remains separate from Standard OutputState contracts. A bounded standard register-Body
producer/consumer profile handles assigned scalar arithmetic opcodes `0x20..0x2B`
in a map-free, single-method library: source emission covers direct unary or
binary integer and float32/float64 operations, while standard loading rebuilds
and verifies the corresponding BoundProgram. Native execution covers int32
wrapping/division/remainder edges and float32/float64 remainder. General
standard Body persistence and execution remain unfinished.
Hosted calls also
support non-unique, handle-free `Copy`-struct `out` values on non-Boolean and
`void` functions across semantic modules, native libraries and native graphs;
compilation-local `[Copy]` templates support the bounded generic `out T` and
`init ref<T>` profiles with fixed `int32`, `uint32`, `int64`, `uint64`,
`char16`, or `void` results, plus the mixed all-generic-output profile.
Single-mode generic outputs also support fixed `float32` and `float64` results
without fixed side outputs. Generic initialization outputs retain
compilation-private managed-handle support. Fixed scalar side outputs are
supported with the non-floating result set and one generic output mode. The
bounded floating-result implementation also combines mixed generic output
modes with fixed scalar side outputs. Compilation-local `[Copy]` templates
also accept concrete `float32` and `float64` generic arguments, forwarding and
publishing their substituted values under existing rules. This grants no
symbolic floating operations. Generic proofs hydrate floating leaves only for
reached non-unique `Copy`-struct descriptors and concrete non-class callable
signatures; managed-class dependency closures remain separate. Public generic
exports and executable TRRM support remain separate. The native backend emits
Terrum-owned code; CIL is neither a dependency nor the default.

Source reference type identity now carries nested `ref<T>`, outer `write`,
`gc`, `init`, and optional qualifications through source binding and mandatory
verification. A bounded ordinary-reference path forms references to direct
local places, safely reads referents, and supports nested references and
TokenSpan result-source forwarding. Its ordinary descriptor uses the
unapproved 8/8 layout proposal provisionally; the candidate is not normative
and adds no hidden retain, root, pin, or lifetime extension. Scalar/registered-
enum referent stores, local descriptor-slot rebinding, bounded non-unique
cleanup-free Copy-aggregate stores, managed-handle slot stores, and unique-owner
replacement through writable references are implemented in that route. Unique
stores stage the new value, retire the old value only when transitive cleanup
is proven nonthrowing, preserve exact-place aliases, and reject overlapping
references to the replaced value. A failed right-hand side leaves the old
owner live. The scalar, aggregate, and owner cases have JIT and emitted-image
coverage. Mutable `Span<Token>` locals can rebind their
descriptor; `write Span<Token>` element access remains unsupported. A
`Captures`-declared writable `ref<Span<Token>>` formal can replace a local
view slot: the verifier checks the body's stored backing against declared
sources, and callers stage the target identity and conservatively preserve
old/new backing possibilities. The verifier tracks local reference targets
and storage generations through formation, copies, rebinding, and conservative
flow joins, rejecting references that escape a local scope or outlive their
target. Formation also supports fields in non-unique cleanup-free inline
structs, scalar/Copy fields of complete unique inline owners, already-live
fields of a partially initialized constructor receiver, local fixed-array
elements, and fields within an element with a combined array/field projection.
Union-field validity remains attached to the selected element. Root generations
and field/element projections remain distinct from backing. GC roots/pinning,
managed/class/box backing retention,
initialization attenuation, reference-valued aggregate fields, private
revision-97 checked-reference persistence, and Standard TRRM executable
support remain incomplete. Direct and nested unique-field moves, vacant-field
repair, partial cleanup, field-place generations, and live unique-field
replacement are implemented for structurally tracked local owners with
eligible mutable paths. Nested moves update every affected ancestor; hierarchical
liveness flags preserve independently live fields during normal retirement and
checked repair failure. Checked references to live nested scalar fields survive
independent moves and repair, while moving their ancestor invalidates them.
Failed nested construction retires acquired fields without invoking incomplete
authored Cleanup. The private revision-92 module route provisionally
round-trips and remaps nested `BoundFieldMove` paths under the unapproved
[private persistence proposal](proposals/owning-field-move-private-persistence.md);
this does not establish Standard TRRM support. Replacement stages the new
value, retires the old value once on live paths, and requires a conservative
same-program proof of nonthrowing transitive cleanup; unknown or reentrant
cleanup rejects. This is provisional support under the unapproved
[replacement-retirement proposal](proposals/live-field-replacement-retirement.md),
not a normative change or a general no-failure promise. Whole-owner replacement
invalidates descendant references, while field moves, repair, and live
replacement preserve unrelated live siblings. Authored Cleanup receivers
cannot be left partial. Constructor-acquisition unwind combinations beyond
the checked before/after-acquisition cases, broader proof coverage, checked-reference
provenance, Standard TRRM, CLI and
standalone-compiler routes remain unfinished.
See the
[stored-layout proposal](proposals/reference-stored-descriptor-layout.md)
and [private-schema proposal](proposals/checked-reference-private-persistence.md).

The unapproved [normal-backing agreement](proposals/reference-normal-backing-support.md)
uses the exact host key `terrum.experimental.windows-x64.normal-backing`,
carried through source binding, `ValueLayout`, mandatory verification and
lowering. Its bounded source-bound JIT/AOT closure supports scalar/inline-Copy
class payloads, ordinary/optional class-handle fields, local ordinary
references and reference descriptors stored in class fields or inline Copy
aggregates. Required and optional ordinary references use the candidate
16-byte/8-aligned carrier; absence is both words zero. Each support-bearing
descriptor residence retains its selected normal backing independently of
the object containing the slot. Stack/inline descriptors have zero support and cannot
extend local backing lifetime.
Generated direct nonforeign calls also carry these reference values and
descriptor-bearing aggregate inputs. Inputs are staged in source order;
hidden reference results transfer support only after callee cleanup.
Replacement, construction rollback, iterative retirement and reentrant
call-plan cleanup preserve typed holds under both runtime policies, including
source-slot rebinding, source-handle retirement, checked unwind and inline
call-result reads.
The [normal-backing inventory](implementation/conformance-gaps.md#normal-backing-agreement)
retains the detailed runtime, provenance and qualification boundaries.
Foreign/native-callable boundaries, reference output parameters, by-value
handle arguments/results, optional GC/init references, Cleanup and
source-independent persistence remain gated. Private module/graph and TRRM
routes reject candidate-bound programs. The
[CLI reference](implementation/cli-protocol.md#provisional-normal-backing-agreement-selection)
records the four eligible commands and their fail-closed boundaries; the key
is not a TargetProfile, persistence schema or serialized authorization, and
no tracing profile selects it.

Hosted statement switches evaluate the selector once and bind ordered scalar
constant/wildcard arms, guards, cleanup-aware cross-arm transfers, inferred
`var`/`mut var` and exact scalar type bindings, and short-circuit
`not`/`and`/`or` compositions. Pattern bindings are visible to guards and
bodies, with flow checks for composed alternatives. The parser also accepts
the shared `is`/switch pattern grammar for null, tuple/grouped,
typed/reference, generic, qualified union-case, negated, conjunction and
disjunction forms. Non-scalar typed/reference/generic/tuple/union binding and
execution, output-lifting patterns, and value-producing switch expressions
remain unfinished; existing optional-handle `is` forms remain separately
supported.

Required constants and `[ConstEval]` bodies admit fixed scalar values:
`bool`, `char8`/`char16`/`char32`/`rune`, signed and unsigned 8/16/32/64/128-bit
integers, binary16/`bfloat16`/binary32/64/128, and decimal32/64/128. Numeric
literals construct directly in their selected target format, including exact
wide integer bits, binary rounding and subnormals, and BID decimal cohorts.
`nint`/`nuint` remain outside the fixed-scalar value-registration candidate.
Eligible `[ConstEval]` bodies support scalar locals, assignments, branches,
loops, returns, and calls to other `[ConstEval]` functions, subject to the
currently implemented operation matrix. Hosted `int128`/`uint128` binding, verification, checked/wrapping folding,
ConstEval and Windows x64 JIT/AOT execution cover the accepted integer
operators. Hosted binding, verification, exact folding and ConstEval also
cover the binary and BID floating operator matrix, including truncating
remainder and decimal preferred cohorts. Windows x64 lowering covers BID
decimal32/64/128 arithmetic, comparisons, sign operations, updates, compound
assignments and truncating remainder through JIT and PE under both runtime
policies. Native `float128` lowering remains incomplete. Windows x64 lowering
also covers the `float16`, `bfloat16`, `float32` and `float64` operator
matrices, including truncating remainder, updates and compound assignments.
Standard register-Body execution remains a separate gap. The evaluator limits
are 4,096 interpreted statement steps and a call-depth limit of 64; these are
not portable guarantees. Source `ConstEval` recognition now requires an exact
host-selected per-Module schema; matching spelling alone grants no
authorization. Local catalogs and checked external provider snapshots are
retained in the bound program and in-memory references. The CLI manifest path
is provisionally implemented. `[Pure]` binding requires the exact selected
schema; mandatory source verification currently accepts scalar and recursively
Copy-struct values, plus readonly nonoptional managed-class inputs for scalar
and Copy-struct field reads. It also covers local/field assignments,
local scalar and Copy-field updates, checked/implicit-reference reads, scalar
`out`/`init` destinations, declaration-condition expressions, comparisons,
release-wrapping integer operations, and calls with selected Pure/ConstEval
contracts. Each callee body is verified independently; call sites reject
unverified callees without inspecting their bodies. Locally declared generic
methods retain selected forms in their proof definitions and are checked
within the default Copy domain. Closed source-generic Copy-struct
specializations and default-Copy generic method result terms are accepted when
their substituted fields satisfy the same recursive value checks. Pure generic
methods can construct fresh source-generic Copy results through selected,
verified constructors; unsafe, impure and unverified constructors are rejected.
A closed local specialization executes in NativeJit. Observable writes,
potentially throwing operations and unverified callees are rejected. Complete
read/effect/output/transfer contracts, broader precondition/failure domains,
other accessor/constructor forms, and schema-dependent interchange remain
unfinished under the unapproved proposals. Wide scalar static-proof facts now
preserve 128-bit integer payloads and target-format floating values/classes;
the broader built-in predicate-refinement catalog for runtime values remains
incomplete, so the accepted v1 proof floor is not yet fulfilled.

Current profile markers are syntax language version 46, semantic-module base
revision 76, the provisionally implemented but unapproved private arithmetic
candidate revision 89, and native profiles native-10 through native-20.
Readers retain exact revisions 76–88 as checked-only arithmetic inputs.
These markers describe supported implementations, not approval of the
remaining language contracts. The exact source, layout, metadata, ABI, and
runtime contracts are in the
[specification index](specs/README.md).
Source `Layout`/`Blittable` roles require an explicitly selected
`LayoutCoreRegistrationSet`; names alone grant no authority. The provisional
target implementation covers `Auto`, `Sequential`, `Union`, and
`Transparent` layouts, packing/alignment, and fixed-array geometry; see the
[layout profile inventory](implementation/layout-profiles.md) for precise
route support. Private revision 104 and Standard L1 preserve wide aggregate
and array geometry, but these schemas and the L1 execution route remain
unapproved candidates. Semantic geometry is not capped at 16 bytes or 8192:
native materialization has separate target and host capacity limits, and the
8192-byte bound applies only to byte-wise payload analysis. The verifier now
tracks selected native-pointer origins through local inline-field reads,
writes, copies and replacement, but this is not a published callable summary
or a serialized standard/private contract. Raw pointer fields still carry
only address bits in their declared representation and do not establish
pointee validity, access, ownership, or lifetime.
The verifier also carries provisional local CopyOrigin facts for currently
represented native-pointer, function-pointer, TokenSpan and stored checked-
reference values, separating source kind/view/formal/path from backing
dependencies. A generic reference-bearing aggregate preserves its selected
target through fresh construction and by-value result forwarding under both
runtime policies. Missing facts remain unavailable; calls without a published
summary do not inspect callee bodies, and whole-source DependsOn/Captures
bounds remain independent. Source
verification now derives in-memory `NormalResult` and
`FreshInlineConstruction` summaries for supported Copy values, stages
by-value inputs at argument evaluation, and resolves checked referents at
Invoke using the selected declaration's summary. Known mutable receiver,
checked-reference, output and native write effects invalidate affected field
facts instead of preserving stale origins. Recursive or exhausted inference
remains unavailable. Writable checked-reference aliases update initialized scalar and
stored-reference fields in inline Copy aggregates; later copies observe the
replacement under both runtime policies. Generic stored-reference field
origins also cover fresh
construction and by-value forwarding. Bodyless cross-module expectations
still have no private or standard transport, and these facts are not consumed
by lowering. Private revision 106/-2 Body round-trip now composes a scalar
inline-field place with an ordinary checked-reference referent through the
existing records. Stored-reference-field persistence still depends on the
selected dependency-contract carrier.
Direct `NativeParameter` inputs can point at committed aggregates in source,
private semantic modules, and native execution. The canonical standard TRRM
`NativeParameter` encoding remains unapproved. The direct-import
`000E`/Feature 16 carrier is provisionally exercised only by the exact
P+F16 successors described above; it does not select NativeOutput or the rest
of the ForeignInvocation bundle. The old scalar/opaque-pointer `NativeOutput`
prototype has been retired from source admission. The replacement aggregate
source path is provisional under
[`aggregate-native-outputs.md`](proposals/aggregate-native-outputs.md);
private persistence and standard register-Body support remain incomplete.
The detailed [hosted output profiles](implementation/output-profiles.md)
are informative development inventories. Their historical `out` modes do not
add parameter forms to the canonical reference model.
The [hosted view and owner profiles](implementation/view-and-owner-profiles.md)
record the current TokenCursor prototype and bounded owner/field loans and
cleanup projections; their restrictions do not limit the general source
dependency contracts.
Source binding now resolves `DependsOn`, `Captures`, and `ForwardsBacking`
flat recipes into in-memory receiver/formal and destination identities for
callable results, initialized output values, retained destinations, and
getters. It distinguishes direct `this` from an escaped `@this` formal,
supports explicit empty sets, and rejects unknown, duplicate, non-source, and
incompatible recipes. `ForwardsBacking` is checked against an explicit
same-destination dependency/capture set when one is present. The legacy
TokenSpan call/capture flow now consumes normalized contracts; the legacy
projection remains limited to Span results and TokenCursor constructor
captures and is checked for consistency. That flow is compilation-local and
uses live slot identities. In the TokenSnapshot/TokenSpan profile, owner moves
remap backing dependencies to the transfer destination; per-state copies and
joins distinguish that moved owner from a later value reusing its old slot.
Transfer, retirement, reinitialization, conditional-source union, loop-scope
cleanup and staged-call cases have active coverage. General
source-instance/version tracking, aliasable reference provenance, body proof,
inherited relationships, private/TRRM persistence and remapping, and general
native lowering remain incomplete. This slice is not end-to-end dependency
support.
The [sample catalog](samples.md) distinguishes runnable programs from design
sketches.
The [experimental CLI response reference](implementation/cli-protocol.md)
preserves current schema-1/schema-2 and raw execution-worker behavior.
It does not claim implementation of the independently selected
[terrum.cli/1 supervisor protocol](specs/cli-protocol.md).

## Persistent and self-hosted components

Only [`.trrm` semantic interchange](specs/representation/persistence.md) has
Terrum-standard serialized producer/reader conformance. Current saved
structured IR and native package/set/graph serializations are private.
Their detailed [native profile](implementation/native.md),
[module](implementation/modules.md), [set](implementation/module-sets.md)
and [graph](implementation/native-graph.md) references preserve current
revisions, reader limits and commands. The
[native execution specification](specs/native.md) separately retains ABI,
binding, ownership and code-lifetime obligations.

The [executable TRRM inventory](#executable-trrm-scalar-interchange) separates
Development compact Bodies from selected Standard execution candidates.
Standard metadata readers/writers use the assigned 48-byte Module row and
governing edition 1, exact Feature pairs, duplicate-tolerant ordinary lists,
canonical writer output and per-Module prerequisite closure. Artifact
indexing/writing selects the schema explicitly, with no fallback between
Standard and Development section meanings or SourceMap layouts. This is
metadata integration, not general current-schema execution.

The Standard TypeSpecRef slice handles row/F2/F3 references, nested wrappers,
existential/ref-init forms, inline constructed arguments, formal-kind/arity
checks, local/imported nominal identity, row bounds and acyclicity. Complete
references are integrated in selected fields, signatures, parameters,
properties, generic formals and constraints. Remaining complete-type uses,
owner substitution, ConstantSource semantics, dependency/remapping and
execution still require integration.
The Standard Contract reader/writer accepts Effects bits `0x02` and `0x04`,
rejects retired bit `0x01`, and permits an explicit zero mask; Cleanup no
longer requires the retired bit. The provisionally selected source NoThrow
retirement does not by itself migrate every private contract/replacement path.

Selected register-Body validation checks framing, maps, Blocks, typed-record
shapes, instruction boundaries, control selectors and reference bounds.
Method-aware checks cover selected signature/map type identities and Module
ownership, including reached constructed types. This is selective Body access,
not artifact-wide Body preflight or complete instruction semantics. F0/F1
selected-use rejection remains provisional under the unapproved
[pseudo-retirement proposal](proposals/body-typemap-pseudo-type-retirement.md);
their normative assignments are unchanged. `DoesNotReturn` CallSites must use
`None` and terminate their Block; compact bound calls to those targets reject.
Generic substitution, complete callee/replacement equality, operation
type/effect checks, R15 discard eligibility, general indirect calls,
canonical writing/remapping and exceptional execution remain open.
[CG-001](implementation/conformance-gaps.md#cg-001) retains the exact row,
map, register-role and opcode coverage and the full producer/consumer gate.
The Standard Param reader/writer now require mode `00`, accept only flag `01`
for an ordinary readonly outer `ref<T>`, and reject all other flags/modes.
Development artifacts explicitly retain legacy modes `01..04` with zero flags.
This is structural row support; Standard source production and all selected
Body call/adaptation consumers do not yet preserve the adaptation flag.
The standard structural validator now consumes `NativeImport` section
`0x0021` using the assigned 16-byte row, checking tagged MethodDef/FieldDef
owners, staticness, conventions, resolution pairs, String references, duplicate
owners, callable signature constraints, and admitted data-field representations.
The Standard Contract reader also consumes the MethodDef-only `0x000C`
NativeCallable clause with exact convention/reserved-field validation and
Feature 1 ownership. Standard `NativeExport` section `0x0022` has a 16-byte
reader/writer that validates static MethodDef/FieldDef owners, the exact
MethodDef-owned `0x000C` reference, field representation, duplicate owners and
symbol names, and Feature 4 ownership.
Callable checks cover only forms admitted by the current Standard TypeSpec
reader. Standard TypeSpec `0x0D` now has its exact seven-byte convention plus
MethodDef-anchor form; the Development six-byte signature-row form remains
separate. Basic anchor checks require a bodyless, non-receiver, non-method-
generic MethodDef with no NativeCallable, import, export, entry-point,
accessor or cleanup role. The unapproved, provisionally actionable
[anchor-contract proposal](proposals/standard-function-pointer-anchor-contracts.md)
defines the complete applicable clause set and normalized identity.
The reader/writer also provisionally accept TypeSpec-only convention byte
`0x00=Terrum`; this follows an unapproved proposal and does not widen
NativeImport, NativeCallable, NativeExport or source `NativeCallConv` domains
from `1..4`. No normative registry change or default-convention lowering
support is implied.
The provisional MethodDef Contract `0x000B` row now validates its phase,
flags, reserved field, phase-local ordinals, typed PredicateGraph reference,
exclusive attachment and Feature 10 closure; the complete predicate-node
schema, general predicate identity and execution remain open. The bounded
Standard anchor comparator now normalizes admitted signature types and modes,
Effects, OutputState, phase-3 selector identity and Dependency rows; it rejects
structural anchor/type cycles. Selected Body-map type matching and
full-artifact PropertyDef getter/result validation use this comparator; the
latter waits for the anchor table and accepts distinct equivalent anchors
while rejecting changed conventions, signatures or contracts. Selected
`GeneralPredicateGraphsDraft` StaticImmutablePublication field/extent
validation also uses it after anchor construction. The full-artifact
WitnessAssociatedConst duplicate-binding check likewise waits for validated
anchors, rejecting distinct constructed interface instances whose nested
function-pointer anchors have equivalent signatures and contracts.
Producer/replacement identity, cross-module provider binding, other consumer
routes and re-encoding/remapping remain open.
The Standard Contract reader also consumes the assigned 20-byte OutputState
row, requiring Param mode `00`, a complete `ref<init T>` or `Span<init T>`
type, and a phase-3 PredicateGraph attached to the same MethodDef. Its bounded
phase-3 selector profile validates one bool-result node, one ordinary-result
read root, the six assigned selector kinds and owner result type, with no child
edges, captures or dependencies. It preserves those clauses on
DoesNotReturn methods and rejects duplicate or conflicting overlapping
normal-outcome states for one output; it does not execute the selector.
Within the separate unapproved `GeneralPredicateGraphsDraft` candidate, the
structural consumer now also admits general phase-3 graphs under Feature 17/1
for `OutputState` rows targeting checked initialization destinations. It
requires each such graph to be referenced, and rejects output-root reads,
raw `NativeOutput` cells and function-pointer anchors. Source projection and
emission cover only the bounded source subset described below; full source
`ensure` projection, semantic re-proof, runtime graph evaluation and
Standard-profile support remain open.
The distinct provisional composition
`trrm/1-draft-init-reference-bodies-general-predicate-graphs-1` now emits,
loads and re-emits the bounded initialization-Body forms against the
GeneralPredicateGraphsDraft schema, preserving Feature 10/1 default
OutputState rows. Source projection and the phase-3 graph codec now preserve
built-in scalar roots, comparisons, checked/wrapping arithmetic, unary
operations, total conversions, conditionals, FrozenValueNode literals
(Feature 11/1) and saved-result root 5. The selected profile emits and reads
TypeSpec rows for the full built-in scalar domain; source-free provider
re-emission covers the full built-in scalar domain. Native lowering does not
evaluate phase-3 selector graphs.
The source projection preserves per-clause OutputState consequences and adds
bounded conjunction-derived relations when the normal-postcondition set is
within the supported scalar subset, correlating repeated guard expressions
across clauses.
The Standard producer and source-free re-emitter preserve rootless scalar
predicates with an empty read footprint for either OutputState value. Loading
checks the graph and relation shape without re-proving the producer's body.
Nontrivial source declaration-clause emission remains gated until the separate
phase-2 clause path is implemented; the current graph-codec tests isolate
phase-3 metadata from that clause payload.
This composition does not select the LayoutQueries source/provider/query-Body
profile. Phase-2 ensure persistence, checked conversions, bit reinterpretation,
calls, captures, places, broader cross-clause value coverage, complete
dependency/provider replacement remapping and graph execution remain open.
Native JIT and PE execution also cover
imported default-Boolean OutputState calls after same-profile
provider/consumer re-emission; they do not establish phase-3 graph execution.
The provisional
[failure-identity handoff](proposals/standard-trrm-initialization-predicate-failure-identity.md)
does not yet bind or preserve the selected PostconditionException.
The code provisionally uses the unapproved
[predicate Feature composition](proposals/predicate-feature-composition.md)
direction: Feature 10/1 has no unconditional Feature 11/9 prerequisite, and
the separate
[checked-output selector proposal](proposals/checked-output-outcome-selectors.md)
limits general phase-3 selectors to that exact candidate profile. Standard
phase-1/2 declaration-predicate persistence and execution remain unsupported.
The normative registry still records the pending `10 -> 11 -> 9` chain; this
divergence is not a conformance claim or a compatibility mapping.
The separate, unapproved `trrm/1-draft-init-reference-bodies-1` profile
currently emits and consumes scalar `ref<init T>` stores (including multiple
destinations), direct local-Home DD and one-level FieldDef-backed DD for a
local Blittable structure field, and source-use-5 calls, plus a bounded
three-block Boolean provider/caller shape. The field route carries its local
TypeDef/FieldDef/layout closure without adding a public type export; exact
field/referent mismatches reject, and source-free same-profile re-emission
preserves the place. A loaded FailFast library returns the value written
through the field descriptor. The distinct, unapproved
`trrm/1-draft-init-reference-bodies-local-field-layout-1` composition
provisionally adds explicit TypeDef publication/import and provider binding
for rootless Copy Sequential/Transparent Blittable layouts. It preserves
provider origins through source-free re-emission, rejects changed Copy/type/
layout contracts, and binds separate and co-located provider Modules. This
does not add schema aliases or L1 Bodies.
Source production also emits Feature `10/1` `OutputState` rows and
bounded true/complete, false/vacant PredicateGraphs; same-profile imports
compare and preserve that mapping through source-free re-emission. The loaded
same-module branch path executes under FailFast. Non-Blittable and nested
field paths, element/static places, authored/nondefault OutputState
predicates, Span, hidden preparation, general CFG joins, provider dependency
graphs, provider-linked execution and CheckedExceptions remain open. This
provisional implementation does not approve the profile.
Standard NativeImport/Export field validation recognizes these function-
pointer values. Feature 1 is enforced for anchors; Feature 2 closure now
traverses wrappers and type arguments of constructed values in MethodSig
results/receivers/parameters, FieldDefs, StaticField definitions in the
selected GeneralPredicateGraphsDraft profile, UnionCaseField payloads and
TextCaptureShape captures, plus PredicateCapture roles 1 and 2. Union and text
capture rows use their owning TypeDef's Module; predicate captures use their
graph owner's Module. Selected Standard Body TypeMap entries containing
function-pointer values also require Feature 2.
Selected Body-map validation also rejects MethodMap targets resolving to local
anchor MethodDefs. E9/record-0D map validation now requires a form-0D,
zero-qualified TypeMap anchor, checks the local NativeCallable/NativeImport or
eligible ordinary target's convention, complete signature and supported
invocation-contract projection, and validates a role-3 Home result's exact
pointer type. It also checks Feature 4 and uses the Standard NativeImport
table; imported targets retain their expected MethodDef identity, but selected
provider binding remains open. Targets with raw NativeOutput/NativeParameter
obligations reject until the separate unapproved function-pointer contract is
implemented. General predicate semantics, other registered type-use routes,
and standard production/remapping remain in
`native-callconv-standard-trrm-integration`.
The selected `CopyOriginDraft` consumer also rejects Contract `0x000D` on a
MethodDef used by a Standard TypeSpec `0x0D` anchor. This role check does not
complete cross-module provider binding or remapping.
Blittable aggregate fields require the owning Module's Windows x64 target
requirement and its exact `(profileId=1, ruleRevision=1)` Blittable Layout;
another profile's Blittable variant is insufficient. Explicit EntryName rows
require nonempty, NUL-free ASCII, while zero retains the exact declaration name.
It applies the task-authorized provisional `IsValidSourceLibraryName` rule
(still unapproved as a proposal) and enforces the owning Module's standard
`DirectNativeCalls` or `NativeDataImports` feature closure. The four-word
`0x0030` import prototype remains development-schema-only, separate from
standard `0x0030` ConstantSource. This metadata slice does not implement target
resolution, NativeExport image publication, or runtime lowering, and does not
establish source-level data-import support.
The hosted source binder now preserves nominal interface type arguments as
tagged complete type or constant terms. Literal scalar actuals,
forwarded interface/member constant formals, trailing type/constant defaults,
type-domain substitution and cross-tree rewriting are preserved by binding.
Interface signature terms also preserve existential hidden type-argument
slots independently from constant arguments. This is not
general source `G<_>` value support: per-value hidden argument/evidence
retention, generic pattern recovery, storage and runtime lowering remain
unimplemented. This source declaration slice does not select interface
witnesses, publish Standard interface members, or implement member-call
lowering; provider binding and private witness persistence remain separate.
Standard structural validation now reads `InterfaceDef` and
`AssociatedTypeDef`, checking interface ownership, optional self-formal
ownership, declaration-range extents, exclusive associated-type ownership,
nonempty names and optional complete default TypeSpecRefs. InterfaceBase and
InterfaceMethod rows additionally validate range ownership and ordinals,
complete base TypeSpecRefs, and directly declared instance MethodDefs owned
by the interface TypeDef. The C# resolver and standard Import/Export readers
provisionally admit the proposed AssociatedTypeDef `0x20` identity, require
explicit owner imports, and enforce public-owner and non-reexport rules.
Provider-owner equality, exact default comparison, base-instance binding,
ConstantSource payload/value semantics, relation/witness semantics and
standard executable production remain incomplete. Standard AssociatedRelation
rows now validate both complete TypeSpecRefs and direct AssociatedTypeDef or
Import references expected to denote that definition kind. Exact
declaring-interface references must use a TypeSpec row rather than inline
F2/F3 forms. Under the unapproved
[Constraint payload proposal](proposals/constraint-associated-relation-payload.md),
Constraint kind 3 provisionally selects a bounded one-based AssociatedRelation
row with a type-formal owner; relation satisfaction, exact instance binding,
AssociatedTypeDef provider binding and witness execution remain incomplete.
InterfaceConstDef and AssociatedConstDef rows validate their
InterfaceDef ranges, owners, names and complete type references; initialized
interface constants also require a bounded ConstantSource row reference. The
standard InterfaceWitness/WitnessMember readers additionally validate
complete witness type pairs, exclusive member/associated-binding ranges and
WitnessMember method/type-reference bounds. A provisional
`WitnessAssociatedType` codec also validates row ownership, nominal declaring
interface ownership and membership in the witness interface's nominal
inheritance closure, complete selected types, direct/import declaration
references and the unapproved binding-origin tags; declaration-default rows
must identify a nonzero published default. Exact constructed-interface
membership and substitution, selected associated-type binding truth, default
equality, requirement compatibility and remapping remain incomplete. The
standard UnionCaseDef/UnionCaseField readers validate TypeDef/UnionCaseDef
owners, exclusive payload ranges, ordered payload ordinals and complete
UnionCaseField TypeSpecRefs. Feature 9 remains registration-pending, so this
row validation does not admit nominal-union source or runtime support.
BuilderRegistration and TextTarget rows now validate TypeDef/MethodDef
references, complete schema/recipe TypeSpecRefs, parameter ordinal bounds and
duplicate targets. TextContentShape/TextCaptureShape rows validate generated
TypeDef ownership, exclusive capture/fragment ranges, capture order/modes and
complete capture TypeSpecRefs. `TextFragmentShape` typed formatter/writer
reference forms remain an unapproved
[design candidate](proposals/text-fragment-reference-forms.md), provisionally
implemented by a standard structural reader/writer that checks
literal/formatted field presence, shape-local captures, MethodDef row/module
bounds and open kind-1 constraints owned by the generated type. Selected-use
validation also requires both methods to be InterfaceMethod rows, keeps the
writer interface and formatter requirement consistent per shape, matches
closed witness implementing types to value captures and their exact
WitnessMember requirement, and checks open formal captures against the
constraint's formatter-interface closure. A provisional profile-aware
`ValidateTextConstructionProfile` overload accepts exact per-Module role rows
and checks exact row selection, direct interface membership, public role
declarations, Boolean results, receiver access, ordered parameter types/modes,
generic formal domains/constraints, resolved TextSpan identity and absence of
DoesNotReturn/caller-unsafe effects. The structural-only `Validate` entry
remains profile-agnostic; no current CLI or source-free consumer supplies the
profile context. Selected-provider Import/Export comparison, replacement,
remapping/packing, source recipe checking, generated text operations and
runtime construction remain unimplemented. PredicateGraph rows validate
required PredicateNode and PredicateReadFootprint row bounds;
PredicateReadFootprint rows validate empty/nonempty range shape, target bounds
and exclusive nonoverlap across footprints. PredicateChildEdge writes sort by
parent and child ordinal; the reader validates edge references, contiguous
ordinals independent of physical row order, exclusive rooted trees, and the
root-first depth-first node-row order. PredicateCapture validates graph-local
ordinals, assigned roles/flags and complete captured TypeSpecRefs.
Root/dependency payloads and semantic ordering, canonical graph identity
ordering/remapping, predicate node/operand semantics and evaluation remain
incomplete. The associated-type identity and relation-payload proposals remain
unapproved; their implementation directions are provisional only.

Terrum-authored scanner and scalar-frontend components are implementation
experiments, not a bootstrapped compiler or compiler interchange. The scalar
frontend projection is not the selected compiler architecture: the C#-authored
and Terrum-authored compilers remain standalone, with whole-source process
fallback on the original source. Source text belongs at the compiler input
boundary; JIT input is semantic metadata.

Private `Host.Source` grants, scanner invocation profiles, scalar-frontend
projections, and native source-grant companions remain prototype evidence only.
They are not selected compiler interfaces, source-dependent native ABIs, or
standard TRRM features. Their implementation does not change the accepted
source-free ABI or establish general source-dependent DLL, graph, module-set,
or persisted-AOT support. See the [Terrum-authored compiler component
inventory](implementation/terrum-code-analysis.md), [bootstrap/frontend
composition inventory](implementation/bootstrap-frontend-composition.md),
and [scanner implementation notes](scanner.md).

Native declaration roles and their standard metadata are accepted in the
[native boundary contract](specs/native.md), [semantic metadata](specs/il-metadata.md)
and [native conformance cases](specs/conformance/native-boundaries.md).
Compiler implementation remains prototype-only and does not yet conform to
the complete accepted source and metadata contract. The prototype's source
declarations name a library and export directly and include experimental
pointer access, retention and output-validity annotations; those annotations
are not part of the accepted native schema. The declarations do not select a
compiler-owned Win32, DX11 or native-import catalog.
`build-native-import` explicitly selects the Windows x64 native-import emitter;
ordinary `build` does not emit source imports. Fixed-array local storage is
provisionally admitted by ordinary source compilation as structural `[E; N]`,
independent of native-import mode. Local initializers require exactly `N`
ordered elements; `[]` is valid only for `N = 0`. The bound type table retains
normalized element identity and a `uint64` count, and layout checks use
Windows x64 stride, extent, alignment and target-`nuint` overflow rules rather
than the retired `InlineArray` identifier and 4096-byte limit. Current element
support is limited to scalar, pointer/code-pointer and committed Copy aggregate
storage plus zero-payload unit elements with exact N-expression initialization;
nested scalar/unit local initialization preserves dimensions and source order,
and nested rows support replacement/copy. Scalar/unit nested selection,
read/write, and positive-payload scalar element addresses check each dimension
in order. Each array's `PrimitiveType` identity resolves to a canonical row
carrying normalized element identity, exact count and checked geometry; locals
no longer carry a duplicate layout sidecar. Function-local and struct-field
fixed-array extents and transparent aliases now evaluate eligible scalar
`ConstEval` calls under the existing bounded required-constant evaluator
without persisting call identities. Struct fields also derive width and
alignment from their structural array rows. Pointer-bearing nested arrays
remain rejected until nested storage provenance is represented; whole-array
function signatures remain rejected because no call ABI is selected, and
field initialization/access, full-width local frame offsets and several
source-type contexts remain unfinished.
The uniqueness-amended A3 selector
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1` is wired
through Standard emission, loading, reference creation and CLI selection;
the flag-free `...-3` identity is not an alias or fallback. Its unapproved,
provisionally actionable [proposal](proposals/standard-trrm-a3-positive-unique-type-evidence.md)
adds A3-only TypeDef `DeclaredUnique=0x40`,
exclusive with A2's positive `DeclaredCopy=0x20`; either fact requires
Feature `2/1`, and A3 Cleanup clauses require positive Unique evidence. A2
semantics remain unchanged.

The A3 structural validator admits type-only generic TypeDef metadata,
checks earlier-formal defaults and only the assigned Blittable constraint,
and rejects constant formals, unsupported constraints, existential values
and generic method specialization. Closed actual substitution now checks
ownership/nullability domains, full qualifier positions and Blittable
requirements through nested generic fields before deriving local Sequential
geometry and RootlessCopy closure. The admitted subset rejects Cleanup,
Copy/Unique conflicts, unsupported pointer-cell/phantom/nullable/Span actuals,
writes, open recipes and A2 providers. A3 provider binding compares generic formals/defaults/constraints,
flags and layouts, ordered field templates, nested constructed identities and
selected Unique actual origins; it requires A3 for generic or Unique
dependencies and retains A2 for supported nongeneric providers. Direct
generic values and generic TypeDefs/imports in Body TypeMaps are rejected;
the selected fixed-array TypeSpec route remains separate. Constructed layout
cycle detection compares normalized closed identities, so finite repeated
generic definitions are accepted while a direct inline instance cycle rejects.
The layout matrix also covers nested closed arrays, Transparent-over-void,
packing, explicit alignment, zero extents and checked aggregate overflow.
Provider geometry cases bind matching packed A3 declarations and reject a
selected provider whose layout produces a different array alignment. A
compatible replacement provider with its TypeDef relocated to another row
also binds through the same export key. The binding plan
preserves the retained type binding but does not yet support NativeImport
lowering through this route.
The unapproved
[A3 failure-class proposal](proposals/standard-trrm-a3-layout-failure-classes.md)
has a provisional root-scoped resource ledger and dedicated exception/CLI
classification for invalid metadata, unsupported admission, target overflow
and work exhaustion. A2's existing failure behavior remains unchanged.
The full field/action/payload charging recipe and A3 array production remain
unfinished; [CG-004](implementation/conformance-gaps.md#cg-004) records the
covered consumer/provider/producer paths and remaining resource gates.
The selected A3 CLI execution cases exercise inherited nongeneric
run/build/PE operations,
not constructed generic array execution. Generic source production,
source-free reconstruction/CLI JIT/AOT, compatible replacement/remapping and
end-to-end array runtime evidence remain open; the A3 proposal is not accepted
conformance or general generic execution/export.
Element indexing preserves signed and unsigned 8/16/32/64/128-bit types through
JIT access and pointer formation, with checked bounds and unsafe obligations.
Zero-extent arrays do not provide borrowable element storage.
Native JIT/AOT lowering now probes large stack frames and has
supervised coverage above the former ceiling. Provisional private semantic
revision 103 composes the prior fixed-array, storage-address and field-path
records, and additionally persists Packing, optional declared Alignment,
derived Alignment and RequiredAlignment. Its separate
[storage-address-path proposal](proposals/native-storage-address-path-private-persistence.md)
remains unapproved. The separate
[address-only backing companion](proposals/native-storage-address-address-only-backing.md)
remains an unapproved candidate for the revision-101 path; the current private
revision-103 implementation carries the bounded behavior: recursively
initialized, positive nested local backing is admitted only when every
retained use is a direct NoEscape `NativeParameter` address argument. Private
and Standard paths preserve provenance through one- and two-ancestor
address chains, including native package-set and graph routes.
Ordinary nested array values, vacant backing, escapes and ptr-to-array runtime
carriers remain excluded; nominal zero-payload operations and remaining
replacement/body fixtures stay gated. Standard A2 declaration production
remains type-only for local Blittable Copy types; it emits TypeSpec form
`0x11` and FixedArrayLayout rows for scalar, unit, and nested array fields.
Raw `LayoutKind.Union` is now carried through source binding, fixed-array
geometry, A2 type-only metadata and private-module round-tripping; all
alternatives must satisfy the rootless Copy closure, and typed alternative
access requires `unsafe`.
The source-free type-only route reconstructs local non-generic DeclaredCopy
TypeDefs and those array fields, including acyclic arrays whose elements are
local nominal Copy structs and raw Union layouts. Whole-Union initialization
and executable union-array values remain under the unapproved, provisionally
actionable
[raw Union initialization proposal](proposals/standard-trrm-raw-union-initialization.md);
current runtime evidence does not include them.
Separately, the bounded F+S/A2 executable path emits positive-count local
arrays initialized by source-ordered scalar stores and indexed EC/RegisterPlace
addresses. Nested PlaceSelection chains now reach scalar-leaf PlaceLoad, and
the selected Standard LoadStandard route reconstructs/re-emits that Body;
source-free JIT/AOT reads initialized nested leaves. Direct-source JIT/AOT
also exercise nested NativeParameter addresses with an actual four-byte native
copy. Provisional A2E adds explicit N=0 Home initialization and nested empty
children without zeroing or creating backing. These are bounded profile
results, not general array-runtime or private nested-value admission. The
private address-only backing exception does not select or substitute for
Standard A2 contracts.
The exact Standard A2 route additionally supports direct NativeImport scalar
result calls in recursively nested initializers for scalar Copy elements.
Calls are emitted once in source order into typed result Homes; the source-free
reader reconstructs typed reads and canonical D8/D9 stores. The bounded route
checks the address fault before an invalid initializer call, but does not
select ordinary unwind/Cleanup or general effectful initialization.
Zero-payload non-array element values, nested pointer element
backing/provenance, broader provider replacement/remapping and native-array
contracts remain open.
Under the separate unapproved A2 selector, mandatory Body-map validation now
propagates typed PlaceHandle origins and declared value types across blocks,
branch joins and loop back-edges. It rejects missing or incompatible
predecessor states, including a register index whose incoming value type
differs from its BlockEntryState declaration. Body-map validation rejects
incompatible place-authority merges and Q values beyond the array extent.
This verifies selected metadata flow only;
dynamic `0 <= i < N` and `Q <= N-i`,
initialization/publication, actual address/alignment execution and source-free
reconstruction/remapping remain open outside the selected nested scalar-read
and NativeParameter cases. Successful recursive leaf calls execute exactly
once in source order through
the private reader, JIT and AOT. The private type-closure boundary accepts 64
nested single-element dimensions (p=63), and the closed 65-dimensional case
(p=64). Type closure counts one unit per array dimension. The 128/129
effective-depth boundary remains open, while
source syntax rejects 127/128 dimensions with `TRM1004`. A source-free reader
rejects index trees beyond the shared expression-work limit.
These bounded private address-only backing cases do not discharge the wider A2
or overall source/runtime gates.
The `MessageBoxW` sample uses the same direct-import form as other native
functions; metadata inspection does not invoke the modal API. This prototype
does not establish conformance to the accepted metadata schema or support on
other targets. Source-level typed
and nested `ptr<T>` signatures and unsafe pointer address/read/write nodes
persist through private semantic
module revision 83. Typed pointer formation can take the address of an element
in an initialized fixed-array local; native lowering retains its checked
element bounds. Revision 83 does not represent fixed-array expressions;
selected later private revisions support bounded flat-array and nested-address
operations described in the [module inventory](implementation/modules.md).
General pointer memory operations remain outside Standard execution support;
typed native-pointer declarations and selected F/F+S/A2 address/call Bodies
have separate bounded routes.

The source compiler retains the legacy two-string
`Terrum.Interop.NativeImport` prototype and implements the unapproved,
explicit-Core callable schema candidate for LibraryName/EntryName, CallConv,
ResolutionMode and ResolutionPolicy. With an explicit Core registration,
these enum-valued arguments now use ordinary exact-E lookup and constant
evaluation, including aliases, copies, conditionals, and eligible `ConstEval`
calls. `NativeCallConv` type-use values preserve canonical nested
function-pointer and pointer identities through bound-tree remapping. The
| Layer | Current support and boundary |
|---|---|
| Source binding | Supports partial and file-local enums, exact wide values, duplicate-valued members and source identities distinct from Core/private identities. |
| Private persistence | The unapproved source-enum route is limited to public namespace-level int32 enums with explicit ordered member keys; internal, file-local and wide enums are not persisted by it. |
| Standard enums | NominalEnumsDraft remains a separate unapproved candidate. Its API and CLI require explicit declaration/member keys, generate none, and support only bounded enum declarations, constant-return and enum-identity Bodies. The baseline emitter is Windows x64 target-1-only; the candidate preserves target requirements 0/1 for non-Blittable enums and requires 1 for Blittable. See the [CLI inventory](implementation/cli-protocol.md#provisional-trrm-source-schema-selection). |
| EN1 | The separate [C1 enum-closure proposal](proposals/standard-trrm-c1-enum-closure-composition.md) leaves NominalEnumsDraft and CG1 unchanged. Partial enum source/reference and direct-import re-emission paths exist; bounded local non-generic struct FieldDef/Layout emission, fresh source emission using selected imported enum roots, mixed type-only loading with direct imported enum fields, direct non-generic struct TypeDef Imports, and FieldDef Imports inside imported struct stubs are implemented but unverified under the active hold. Imported enum FieldDef TypeSpecs now have a provider TypeDef-row relocation and source-free re-emission/reload regression; it remains unverified. A transitive Leaf-to-Wrapper/WrapperTwin-to-Packet provider diamond now checks the shared FieldDef Import and compatible TypeDef/FieldDef row relocation through source-free re-emission/reload, alongside incompatible Leaf replacement; it remains unverified. Source emission now includes local non-generic class and inline-struct fields with class/struct/enum TypeSpecs, raw `ptr<void>`, native data-pointer storage with scalar, integer-backed enum, non-generic struct or non-generic class-handle pointees, and scalar-signature native function-pointer fields; source-free loading/re-emission handles selected class roots, class TypeDef Imports, and nested local class dependencies. Pointer-to-enum FieldDef pointees are remapped by Standard provider identity across enum-row relocation. Non-generic class-handle pointer storage now preserves class identity through source-free re-emission and compatible TypeDef-row relocation, with an added class-layout/JIT/AOT-emission regression; all remain unverified under the hold. Under the unapproved exact class-layout successors, a bounded class-root OffsetOfType Body now carries ordered FieldPath/0045 dependencies through source-free load/re-emission and lowers supported Auto/Sequential field layouts, including nested inline-struct paths and data/function-pointer storage without expanding pointees; direct/nested JIT and round-trip regressions are added but unverified. Pointer-to-generic-class pointees and function-pointer signatures with non-scalar types or additional contracts, broader FieldMap/FieldPath provider remapping, optional folding, PI1 property carriers, class members/Bodies and dependent runtime/CLI remain open; EN1 is not end-to-end support. See the [conformance gap index](implementation/conformance-gaps.md). |
`NativeParameter` still adds
prototype raw pointer annotations; without it, a raw pointer remains
caller-unsafe and carries no checked access, retention, extent, or alignment
promise. The former ordinal `NativeOutput` scalar and opaque-pointer forms are
no longer admitted.

The replacement output source path is provisional under the unapproved
[aggregate native-output proposal](proposals/aggregate-native-outputs.md).
Direct imports may annotate a formal with
`NativeOutput(When: NativeOutputCondition.NonZero)` or
`NativeOutput(When: NativeOutputCondition.NonNegative)` and pass one vacant
`Blittable Copy` aggregate through `&init local`. The result selector is
limited to `int32`/`uint32` NonZero or `int32` NonNegative. The current
direct-import path also admits scalar and raw-pointer cells. The verifier
tracks reservations through argument evaluation and publishes complete values
only on matching normal results; it does not infer provider truth, ownership,
pointee backing, retention or failure cleanup. Selected private revision-106
semantic/native routes keep these NativeOutput contracts separate from checked
init-output publication: each ModuleFunction owns its selector/output-state
vectors, and phase-2 Captures must correlate with a complete normal outcome.
Standard F/F+S register Bodies have bounded scalar/raw-pointer output and
provider-binding support, including `if` and pretest `while` conditions formed
from supported Boolean-Home predicates and integer comparisons involving
zero, matching Homes or admitted call results, with logical negation and
short-circuit `&&`/`||` edges. Loops emit explicit backedges and depth-checked
nearest- or enclosing-loop `break` and `continue` transfers. The source-free
loader reconstructs bounded reducible nested loops whose exits are a normal
parent continuation or an enclosing loop transfer, retaining the reconstructed
transfer depth. Irreducible or ambiguous loop exits remain unsupported.
Direct NativeOutput reservations inside a loop remain unsupported so one
reservation cannot be repeated; loop, comparison, short-circuit, named outer
transfer and rejection regressions remain unverified under the active hold.
General aggregate output Bodies and native-interface/vtable outputs remain
incomplete.
[CG-004](implementation/conformance-gaps.md#cg-004) owns
the detailed composition and qualification gates. Earlier TRRM NativeOutput
clauses and ScalarInitOutput encoding are prototype artifacts, not a fallback
or the selected general representation.

`TrRmExecutableCompiler.Emit`/`Load` still use the explicitly selected
development profile: its four-word `0x0030` NativeImport prototype, NativeAbi
clause and SystemDirectory import path are not standard TRRM assignments.
Standard section `0x0021` now has exact assigned-row structural validation,
and Standard TypeSpec `0x0D` anchors now have structural validation, bounded
identity comparison and selected Body-map target screening. The explicitly
selected F/F+S/A2 producers and native consumers below are bounded unapproved
compositions, not general Standard native integration. The accepted
records and assignments are defined in [semantic metadata](specs/il-metadata.md) and
the [registry](specs/representation/trrm-1.registry.json). The development
profile also persists
`NativeCallable` entries
through a provisional NativeCallable table and ABI role; source-free references
expose those entries as caller-unsafe external declarations. Provisional
function-pointer signature anchors in experimental section `0x0033` and TypeSpec form `0x0D`
preserve recursively nested signatures over scalar, raw-pointer, and
function-pointer values. Duplicate normalized signatures reject, and each
module using a callable or function-pointer signature must declare the
provisional `RootlessNativeValues` feature. Compact Body type keys are limited
to 65,024 signature rows. This provisional `.trrm` path supports function-address formation
and indirect calls to local `NativeCallable` entries and bodyless imported
callable declarations in compact-indexed Bodies. Ordinary ModuleRef/Import rows
retain the selected provider identity and export key through loading; the writer
and consumer validate the local expected signature and preserve lexical unsafe
authorization with indexed region kind `0x06`. The provisional feature closure
also requires `DirectNativeCalls` and `NativeCallableEntries` for these entries
and operations. Native single-provider and graph consumers enforce retained
provider identity for native-callable imports; their selected semantic
contracts are checked before native emission. Address operations retain the
selected MethodDef/import identity in the artifact, including address-only
dependency use. A loaded indexed TRRM consumer cannot yet be repackaged through
the private semantic-module path. The runtime function pointer does not own or
extend its provider image's lifetime; deployment must keep that image live.
Generic numeric-slot vtable declarations now round-trip through
provisional section `0x0032` and ABI role 4, including the explicit receiver,
aligned vptr byte offset, slot, scalar/raw-pointer signatures, annotated
pointer inputs, and result-correlated output clauses. Compact-indexed Bodies
preserve admitted typed-pointer formals and arguments and support scalar
output destinations through the restricted ScalarInitOutput rule below;
general pointer address/read/write operations remain unsupported. Public
declarations are exposed for source-reference binding. A TRRM reference
projects role-4 declarations into the consumer as local vtable-dispatch stubs.
ModuleRef/Import rows preserve the selected provider identity and export key,
while the consumer retains a copy of the role-4 binding row needed for
source-free verification and lowering. The loader does not resolve that
provider or replace the copied slot/offset.
Native-package single-provider, set, and graph consumers validate the retained
provider identity and exact vtable binding, then rebind the local dispatch stub
to the selected provider export. Private semantic revision 87 persists that
expected binding and its output contracts for consumer-side revalidation; ordinary private-module
replacement still permits a provider with a different slot. Private semantic
modules retain vptr offsets in revision 85. Same-artifact
compact-indexed bodies can call role-4 declarations
through ordinary `Call`, transporting raw pointers only through
signature-matched call results/arguments or zero-valued constants; ordinary
pointer/address operations remain unsupported.
Vtable-bearing prototype artifacts provisionally require experimental Features `0x0101`
(`RootlessNativeValues`) and `0x0104` (`NativeVTableCalls`) in addition to
`0x0100` (`NativeBoundaryContracts`); these implementation assignments are
not the accepted registry assignments and do not establish standard TRRM
conformance.
The writer can emit this body from source, and the loader reconstructs and
verifies it; a loaded indexed body is not yet reserializable by the standard
writer. Native-package binding and consumer-side revalidation are covered for
TRRM-derived imports. Runtime dispatch is also demonstrated for a non-null
receiver through a TRRM-derived declaration, including a scalar output pointer:
the consumer artifact is loaded, emitted as a native library, and dispatched
through a one-slot vtable. This does not establish arbitrary pointer operations
or reserialization of loaded indexed bodies. The
source prototype also accepts
`NativeParameter` for raw pointer inputs with explicit `Read`, `Write`, or
`ReadWrite` access, `NoEscape` retention, and extent zero or one for supported
scalar, raw-pointer, and fixed-layout Blittable aggregate pointees. Extent zero
does not prove a buffer length, string termination, or any other property of
the foreign access. Aggregate addresses require an initialized local; writable
access additionally requires mutable backing. Windows `HWND`/`HINSTANCE`
aliases do not receive generic direct-import parameter admission; spell those
ABI values as explicitly contracted pointers. `NativeOutput` uses the separate
vacant-cell profile above; `ptr<void>` input values are limited to
readonly, zero-extent metadata. Direct pointer arguments also accept `null`;
matching `utf8z`/`utf16z` literals require read-only, no-escape metadata and do
not require a selected API catalog. A `utf16z` literal passed to a `ptr<char16>`
parameter may satisfy a positive extent up to its stored UTF-16 element count,
including the terminator; `utf8z` and `ptr<void>` literal inputs remain limited
to extent zero. When a call forms an address with `&local` or an indexed
fixed-array element, the backing must be initialized and match the pointee
type; `Write` and `ReadWrite` require mutable backing. Under the provisional
`[E; N]` source route, direct NativeParameter addresses also admit a typed
dynamic array index. The address uses the positive requested count Q (or one
when the declaration gives no static minimum) and checks the original index
once, then checks the original index and `Q <= N-i` before pointer formation.
Three Windows x64 NativeJit cases exercise a successful indexed read plus
negative-index and insufficient-suffix failures before `RtlCompareMemory`.
Standard A2 now emits typed nested pointer-cell stores and reconstructs them
source-free. JIT/AOT coverage exercises the nested NativeParameter route,
fail-fast bounds suppression, provider pointer-TypeSpec remapping, and exact
whole-array local Copy/assignment through typed Home transfers. Source JIT
separately verifies that copied pointer cells remain independent and retain
their backing. The A2 profile is fail-fast-only; whole-array signatures,
reference parameters/results, CLI execution, and provider-bound whole-array
Copy remain open.
Raw pointer values remain caller-authored unsafe inputs. Caller-unsafe source
functions may return opaque `ptr<void>` values through private semantic
modules. This preserves caller-unsafety and address bits only; it conveys no
provenance, backing, ownership, or lifetime, and source-body pointer results
from ordinary Terrum functions remain outside the current TRRM executable
prototype. This input metadata
round-trips through private semantic-module revision 80, native packages,
consumer sets, and graph-JIT roots. The experimental TRRM direct-import path
also round-trips scalar, opaque-pointer, and `ptr<void>` pointees, plus
supported scalar and `ptr<void>` results, using provisional clause kind 10.
Positive `NativeParameter` extents carried by typed pointer values are checked
against known local or fixed-array backing through local copies; array capacity
is measured from the selected element. Opaque incoming or native-result
pointers remain usable only at unsafe calls and do not mask known undersized
alternatives. Selected pointer-bearing fields and whole-object pointer cells
preserve the fact through reads, writes, copies, and supported `CopyOrigin`
summaries, including entry-value and entry-referent views. The value fact is
snapshotted at each argument position and rechecked at invocation after later
arguments; unsupported projections or missing summaries remain unavailable
and reject rather than inferring size from lifetime dependency unions. This is
in-memory source verification only; it adds no private or Standard schema.
Fixed-array locals also support typed pointer elements and pointer-bearing
Blittable elements. Per-cell pointer sources and extents flow through
initialization, constant and dynamic reads/writes, branch joins, and
pointer-to-pointer dereferences; dynamic reads join all possible cells, and a
pointer to the array measures the pointer cells rather than their targets.
Writable native calls invalidate the potentially affected cell facts, while a
later exact element write restores its fact.
It includes extent-zero scalar and character-pointer inputs and explicitly
committed `ptr<TypeDef>` aggregate pointees using provisional feature ID 4 and
contract kind 12. The loader recomputes supported scalar/nested-struct layouts,
and `CreateReference` reconstructs their identities for consumer binding.
This path is limited to declaration-only import libraries; pointer fields,
function-pointer fields, aggregate bodies, by-value aggregate ABI, and
aggregate outputs remain unsupported. These assignments remain unapproved
and do not establish canonical TRRM conformance.
The provisional ScalarInitOutput rule revision 8 additionally round-trips
exact typed addresses of initialized scalar locals for read-only `NoEscape`
`NativeParameter` and writable `NativeOutput` formals on direct imports or
generic vtable calls. The Body retains addressable-place identity, typed
pointer TypeSpecs, and an explicit unsafe scope; loading reconstructs the
address fact for mandatory semantic verification. Its bounded three-block
conditional form can keep a terminal result check and both return arms inside
that scope. A consumer can re-emit a vtable call using this address form; this
does not add general pointer locals, fields, or memory operations. It requires
the candidate `RootlessNativeValues` feature alongside
`NativeBoundaryContracts`. Fixed-array or aggregate addresses, pointer
reads/writes, and broader control flow remain unsupported. The candidate
`0x2E` unsafe marker remains unapproved and is documented in the
[output inventory](implementation/output-profiles.md#provisional-trrm-output-bodies). The
verifier now ties each output destination to the producing call and its result
condition, and admits reads only on paths that prove availability. It supports
direct comparisons against zero for the declared `NonZero` and signed
`NonNegative` conditions, plus body-verified, single-parameter Boolean
predicates that reduce to those comparisons, including scalar temporaries
reconstructed from TRRM. Overwriting the status or destination, another output
call, or an unmodeled pointer write invalidates the relevant correlation;
unguarded, mismatched, and stale-result observations are rejected.

Direct imports with writable scalar outputs and `NativeParameter` inputs also
round-trip through private semantic producer/consumer and native package
paths. Their PE imports are checked against retained calls and kept separate
from selected provider dependencies. The bounded validator supports
single-provider, consumer-set and graph consumer/provider bindings.
Standalone and graph NativeJit resolve direct imports from the consumer root
only by absolute System32 path; graph and consumer-set execution do not stage
arbitrary raw-import DLLs. The bounded runtime subset is
`CompareStringOrdinal` with readonly local `char16` inputs and
`GetCurrentThreadId` in standalone and graph JIT paths. Arbitrary
non-System32 imports and COM runtime behavior remain unqualified.

An additional, provisional prototype supports foreign
`[Terrum.Interop.NativeCallable]` entries and explicit or inferred
`fnptr<Args..., R>` values on Windows x64. The in-memory and private revision-92
routes also address eligible ordinary top-level/static scalar functions under
the provisional Terrum convention; a local `var` retains the target's
complete signature, while an expected pointer type is checked as a directed
view. The binder accepts the bare form with the
Terrum default convention, but still accepts the prototype `NativeAbi` prefix
pending a source-form decision; it is not the selected source contract.
Under the unapproved F profile, the exact
`EmitStandard` selector emits one direct NativeImport address and bounded
`E0` CallSite shapes: scalar or typed-pointer signatures, `000E`
NativeParameter facts, caller-formal Home inputs, and scalar register or
result-Home returns. Direct NativeImport calls used as arguments are emitted
as earlier E0 CallSites in source order; all but the last staged scalar result
are preserved in typed Homes, and the last is consumed from R0. The
source-free loader reconstructs these limited direct-import shapes, runs
mandatory semantic verification, and supports byte-identical re-emission.
Public `LoadStandard` and `CreateStandardReference` take exact per-artifact
schema selections and an explicit provider set. The bounded loader binds one
F Module's retained MethodDef Import to one F provider NativeImport by exact
Module identity, export key, requirements, signature, NativeImport descriptor
and complete `000E` facts; it preserves `ModuleImport` and reruns the mandatory
consumer verifier. The checked native route resolves the selected root's sole
Import set across the complete exact-F ModuleRef/MethodDef closure in all
supplied artifacts, including disconnected Modules. Every original edge binds
by exact Export key to a provider-owned NativeImport; mixed Modules with one
owned NativeImport and imported expectations, chains, diamonds, self-edges and
cycles are provisionally admitted. The immutable native plan retains all edge
origins and revalidates the closure before lowering; only root imports are
realized, while provider Bodies are not merged or executed. This implements the
unapproved
[provider-closure candidate](proposals/standard-trrm-provider-dependency-closure.md)
provisionally, not as accepted conformance. Other schemas, source production/
re-emission of non-target Imports and same-name expanded lowering remain
unsupported.

The separate unapproved F+S identity
`trrm/1-draft-foreign-invocation-storage-addresses-1` now adds explicit
R14/R15 scalar-local initialization, `EC`/Body record `0E`, and ordered
NativeParameter CallSite inputs. Its producer and source-free loader preserve
the initialized local, source Home authority, complete scalar pointee type,
and pointer result Home; loading re-verifies the bound operation and
byte-identically re-emits the canonical artifact. Effective Feature closure
for the exercised NativeAddress/NativeParameter path is
`{1,2,3,4,16}/1`; F+S does not select Feature 11 or arbitrary frozen-value
decoding. A Windows x64 `GetSystemTimeAdjustment` case executes through
`LoadStandard`/JIT and selected-schema CLI JIT/AOT, including a fresh read of
the written BOOL local. Malformed source authority and extent are rejected,
and a failed AOT preflight preserves the requested output. A separately
selected, unapproved F+S+A2 candidate now routes through the public Standard
API and artifact CLI commands. Its experimental metadata path reads/writes
TypeSpec form `0x11` and the Module-owned `0x0012` layout row, validates
Feature `20/1`, TypeDef `DeclaredCopy` bit `0x20`/Feature `2/1`,
Copy/Cleanup exclusion, scalar and local non-generic Sequential/Transparent
Windows x64 geometry, and Home/Register PlaceSelection plus counted EC
RegisterPlace maps. The selected A2 `EmitStandard` declaration path
emits local Blittable
Sequential/Transparent structs with positive Copy evidence for checked
ordinary types, and TypeSpec `0x11`/`0x0012` metadata for scalar, unit, and
nested array fields. This declaration producer is type-only; the separate
bounded F+S/A2/A2E executable producer carries the Body operations described
above. `LoadStandard` now
reconstructs the selected Module's local array type table for scalar, unit,
direct-pointer, function-pointer, and nested-array elements; it recomputes
target geometry and preserves full `uint64` counts, including zero-payload
arrays at `ulong.MaxValue`. A separate type-only path reconstructs local
non-generic DeclaredCopy TypeDefs and fields with scalar/array types, including
arrays of local nominal Copy elements after their layout dependencies are
reconstructed. Selected A2/A2E references now bind retained provider origins;
the executable NativeAddress path consumes the exercised array TypeMap,
PlaceSelection, SemanticStore and EC Body records. Standard A2 also produces
nested index-side-effect sequences using the assigned `C0` controls: checked
add/subtract are `0x01`/`0x02`, and wrapping add/subtract are `0x05`/`0x06`.
The source-free reader reconstructs the corresponding `BoundUpdate` path
indices and overflow policy; register-map validation preserves unrelated
PlaceHandles and invalidates those rooted at an overwritten Home. Checked
unsigned indices cover `uint8`/`uint16`/`uint32`/`uint64`/`uint128`; signed
checked and wrapping prefix/postfix updates are also available. The selected
supervised worker is the bounded route for all five unsigned widths. The
Standard A2 producer and source-free reader reuse R0/R1
PlaceHandles across nested paths while retaining immutable selection ancestry
for PlaceLoad, storage-address reconstruction and initialization replay.
Nested initializers use general-register counters through depth 10 and
validated Home-backed counters beyond that window; sibling initialization
replays the parent selection path without losing counter values. The general
fixed-array path contract accepts 128 projections and rejects 129; Body
semantic depth remains a separate bound. Canonical
provider Body-row remapping, additional initializer/value carriers,
failure/cleanup/loan provenance and broader address/runtime semantics remain
open.
The unapproved [A2 source Copy identity proposal](proposals/standard-trrm-source-copy-type-identity.md)
has bounded type-only source production, reference projection and re-emission.
Exports require explicit keys for each selected public Copy root and its
public nominal closure; closure-only types stay outside ordinary name lookup.
References retain exact schemas, snapshots, provider origins and original
Import/ModuleRef expectations through provider chains. Selected aliases and
distinct expected views may resolve to one actual provider identity without
merging their expectations. Type-only Standard re-emission preserves those
origins, alias keys and imported recipes; private writers still reject
unsupported projected origins. Compatible provider row relocation preserves
bindings; incompatible declaration/layout replacements reject.
The [native integration inventory](implementation/conformance-gaps.md#cg-004)
retains the exact alias, re-encoding and provider gates. General replacement
and complete Body remapping remain open.
The separate unapproved
[A2R1+Repeat source composition proposal](proposals/standard-trrm-a2r1-source-copy-type-composition.md)
has a provisional implementation for exact A2R1+Repeat type-only
source export/reference and mixed A2/A2R1 provider chains. Imported
FieldPath Bodies retain TypeDef/FieldDef and consumer expectations through
source-free remapping, including compatible row relocation and co-located
providers. Changed layouts/extents, malformed imports and split expectations
reject. The proposal remains unapproved; broader imported-type closure and
replacement/topology rules remain open.
The original A1 array candidate is superseded for integrated nominal arrays
by the unapproved
[A2 rootless Copy evidence proposal](proposals/standard-trrm-rootless-copy-type-evidence.md);
the [A1 identity/shape proposal](proposals/standard-trrm-fixed-array-identity-shape.md)
remains its structural design owner. The standalone Development profile
still has no ordinary-address support. See
the unapproved [Standard native-address](proposals/standard-native-address-formation.md),
[Standard native storage-address](proposals/standard-native-storage-address-formation.md),
[NativeImport function-pointer](proposals/standard-native-import-function-pointer-contracts.md),
[selected-provider consumption](proposals/standard-trrm-selected-provider-consumption.md),
and [provider-native lowering](proposals/standard-trrm-provider-native-lowering.md)
proposals for the provisional carriers and remaining contracts. Eligible CLI
source commands accept the provisional
[NativeCore manifest](implementation/cli-protocol.md#provisional-native-core-registration);
source production and artifact consumption still require their exact schema
and producer/consumer gates.
With an explicit Core registration, source/bound-tree binding now forms an
inferred pointer type for a callable NativeImport and preserves its complete
raw NativeOutput contracts in the existing selector, OutputState and
Dependency identity, and its ordered NativeParameter descriptors, including
the effective foreign convention. NativeParameter-only pointers can now be
invoked indirectly in same-compilation source: binding retains the declared
address/access/extent, mandatory verification checks stable initialized
storage, and the existing foreign pointer ABI path is exercised through
Windows x64 `CompareStringOrdinal` under both runtime policies. Calls carrying
raw NativeOutput states now bind exact `&init local` reservations, correlate
NonZero status with publication, preserve declared output dependency sources
and lower through the foreign indirect-call path. Windows x64 `QueryPerformanceCounter` success and invalid-handle
`ReadFile` complement execution cover scalar outputs under both runtime
policies; the latter also exercises a fifth stack argument, and AOT emission
checks the selected imports. Aggregate pointer-cell dependency and
complement/discard/duplicate-reservation cases are also covered. Other
selector/dependency call contracts remain gated. The bounded Standard F/F+S
`000E`/Feature `16/1` source-free and CLI routes are implemented, but general
provider NativeParameter runtime coverage, multi-edge NativeParameter contract
cases, private revision 97, and exceptional reentrancy/cleanup qualification
remain open; no normative spec is changed.
The prototype retains
concrete signatures through address formation, local/field transport,
indirect calls, private semantic modules (revision 82), native module sets and
graph consumers. Address formation and indirect invocation require an explicit
unsafe scope; native-callable entries and callers remain caller-unsafe, and
defined bodies satisfy scoped no-failure verification. In-memory function-
pointer identities preserve declared Effects separately from the native
family's effective caller-unsafety, plus normalized whole-source Dependency
rows. A `DoesNotReturn` pointer view suppresses normal flow and lowering emits
a trap after an impossible normal return; address formation can project that
target to a returning view without changing convention or signature. Checked
pointer projection preserves the destination and phase while allowing an
actual source subset and backing-only refinement of a broader expected source
set; absence and an explicit empty-source contract remain distinct.
Contract-bearing types are not silently serialized beyond the selected
private revision-103 array records or their selected Development TRRM schema.
The default Development and Effects-only selections still reject normalized
conventions and resolution facts they cannot represent; the explicitly
selected, unapproved conventions/resolution candidate persists those bounded
facts without changing Standard or private schemas. Other unrepresented
function-pointer contracts remain rejected until their private/Standard
carriers are implemented. Checked OutputState is present in the in-memory model but
remains unsupported for this ABI signature subset, which has no checked
initialization-reference parameter representation. The role does not publish
a general `NoThrow` promise. Function pointers carry code addresses only:
they do not root or extend the lifetime of
the image containing the target. Provider callback transport is bounded to the
selected JIT/module routes. Bounded experimental `.trrm` support is described above. The
accepted schema defines native-callable roles and function-pointer signatures
with canonical records and encodings; the prototype's source forms, private
revisions and wire assignments are implementation evidence, not definitions
of normative grammar or interchange.

The source-only prototype also supports a generic numeric-slot vtable call
declaration with an explicit receiver and ordinary Windows x64 argument
positions. It forwards scalar, pointer, function-pointer and output arguments
without inferring interface identity, ownership, or provider behavior.
Supervised native execution covers mixed register/stack arguments, floating
arguments and results, and output publication. The provisional
`NativeVTableMethod` spelling is not normative. Private semantic-module
revision 84 persists and revalidates the slot and parameter contract; revision
85 additionally persists the aligned vptr byte offset. Exported methods are
exposed to consumers as ordinary caller-unsafe external functions.
Native module packages use the same producer/consumer path. Provisional
`.trrm` declarations and bounded calls are described above; they are not
admitted by the accepted standard schema.

The earlier COM-specific source metadata path has been retired. COM-shaped
attributes have no compiler-owned meaning; unsupported qualified interop
attributes use the generic unsupported-attribute diagnostic. Compiler-owned
interface identity, owner/acquisition, reference-count, hidden-parameter,
argument-adaptation, and mapped-buffer behavior is not supported. The
provisional TRRM COM feature and contract kinds 7-9 and 11 are not part of the
accepted format. No canonical COM metadata records have been assigned.

`NativeParameter` remains a generic pointer access/retention/extent annotation
for native declarations; its minimum extent uses the full unsigned 32-bit
domain. A known null pointer satisfies only zero extent. `NativeOutput` can
describe result-correlated output validity. These annotations do not prove
foreign-provider behavior or confer ownership.
Writable `ptr<T>` values remain ABI-shaped pointer values; without an output
contract the compiler does not infer that the pointee becomes initialized.

The maintained DX11 sample authors its Win32 imports, ABI layouts, callbacks,
message loop, and resource cleanup in Terrum source. The compiler's remaining
native mechanisms are generic direct imports, pointers, function pointers,
fixed layouts, and numeric-slot vtable calls; vtable calls do not infer an
interface identity, ownership, or provider behavior. The active suite covers
both maintained entrypoint builds and the generic import, pointer, callable,
and vtable mechanisms. Retired compiler-owned DX11/COM contracts are not
acceptance evidence. Both maintained entrypoints have supervised WARP and
hardware runtime evidence for window startup, client resize and normal
shutdown, plus an explicit WARP provider-output failure case. These observations
do not establish rendering correctness or every provider/runtime path. DX11
generalization is accepted with those evidence limits; they are not reasons to
restore sample-specific compiler policy.

## Syntax and CLI

The public C# syntax APIs are experimental. Syntax trees preserve source text
and spans; edits are content-bound to the snapshot they were prepared against.
The parser can consume a validated compiled-scanner snapshot, but the scanner
component remains responsible for correct token classification. Current CLI
result shapes are in the [experimental response reference](implementation/cli-protocol.md);
the selected [machine protocol](specs/cli-protocol.md) is a separate target contract.

## Bounded generic functions

The provisional exact G1 reference path also reconstructs bounded module-level
direct-return generic forwarding through expected MethodDef Imports.
It retains both expected and provider declarations and binds only the selected
Module identity, ExportKey, requirements and provider snapshot through the
existing explicit Standard artifact inputs; see the
[module support boundary](implementation/modules.md#definitions-and-scope).
C1 composition, role-2 evidence and broader generic persistence/lowering remain
separate.

The hosted compiler supports selected compilation-local constrained generic
functions and static operator protocols. Templates are checked against their
declared constraints and produce closed specializations for supported concrete
types. Internal generic structs have a separate closed-specialization path for
type-only formals, fields, constructors, ordinary methods and generic methods
bound per closed owner instance; source-authored struct constant formals and
the source definition itself are not exported or imported. Generic method
owners retain their source-generic definition and
ordered owner arguments separately from method type/constant formals. Owner
defaults and constraints are established before method inference, and method
formals may shadow same-named owner formals. Public generic function
templates can now be source-bound into a separate checked proof program
retained by `BoundProgram`; unmarked type formals use the default non-unique,
Copy-capable, non-null domain, and explicit `[Copy]` constraints reject.
Type-formal defaults now apply after typed inference or a supplied formal-order
prefix, retain syntax-free concrete/formal/reference/optional/local-generic
type terms and earlier-formal dependencies, and reject self/forward
dependencies. Explicit `<_>` call slots require inference and cannot fall back
to defaults; `default` selects the declared type default. Method declarations
also retain mixed type/constant formal order, exact constant-domain terms,
earlier-constant default proof functions and immutable proof inputs. Source
constant domains currently cover fixed-width integers, Boolean, enums,
transparent primitive wrappers and open local transparent wrappers with a
fixed primitive carrier. Closed method calls now bind mixed type/constant
actuals for fixed-width integers, Boolean, enums and local one-field
Transparent wrappers, preserving exact domains and canonical values. They
accept direct literals, compile-time scalar constants, qualified enum members
and parenthesized scalar or wrapper `ConstEval` expressions. Wrapper formation
executes eligible constructor/factory bodies, initializes the sole primitive
carrier and checks reached preconditions and normal postconditions under the
existing constant-work budget. The bound value retains both nominal domain and carrier. Earlier-constant defaults
support explicit `default`, omitted defaulted suffixes and scalar expression
evaluation. Wrapper-valued defaults retain the proof-selected source ConstEval
factory call and evaluate it only after substituting validated earlier actuals;
reached construction preconditions and failures are enforced. Direct
earlier-formal forwarding remains symbolic in proof calls and resolves in
closed specializations. Equal canonical values share a closed specialization.
constant-value inference also covers a pinned imported nominal type default:
typed arguments and expected results expose the exact declared integer/Boolean
constant slot, while conflicts and explicit `_` without evidence reject.
Source `StructDecl`s support mixed type/constant formals and closed actuals in
fixed-width integer, Boolean, enum and local one-field Transparent-wrapper
domains. Exact nominal domains and canonical values determine specialization
identity; equal values share a specialization. Closed defaults and substitution
reach fields, fixed-array extents, constructors and ordinary owner methods.
Type defaults consume earlier resolved constants and resolve nested generic
targets independently of declaration order. Closed source-owner
constants flow through generic method proofs, defaults and dot-member calls;
arrow and optional-member generic-call postfixes remain unimplemented.
Generic constant domains can themselves be source-generic Transparent
wrappers. Closed source instances substitute type actuals before validating
wrapper values or caller-formal identity; open proof terms preserve nested
actuals, direct method-formal forwarding and trailing type defaults that
depend on earlier type formals. Defaults that depend on earlier constant
formals remain unsupported in open proof terms.
Unresolved/cyclic default-domain
dependencies, symbolic expression inference, imported enum/wrapper/ConstEval
values, source-free generic-method persistence, general aggregate construction
and cleanup, and native lowering for declaration-contract-bearing functions
remain unsupported. Physical constant-generic metadata remains gated by its
specification. Standard and selected A2 source emitters reject source-generic
aggregate rows rather than flattening their generic identity; compile-time
queries folded to scalar values do not require a persisted type row.
Imported CG1 generic nominal constants retain exact provider/TypeDef identity,
provider expectations, substituted type formals and caller-formal domains
through explicit generic Transparent-wrapper provider closure. Same-provider
frozen wrapper values decode after substitution with exact nominal identity
and carrier. Typed inputs and expected imported results infer closed values
or forward exact caller-formal identity; conflicting evidence, mismatched
type/domain terms, incompatible replacement providers, missing dependencies
and malformed wrapper shapes reject. Imported wrappers remain limited to
type-only formals without constraints or defaults; nested imported generic
TypeDef arguments admit supported scalar constants with exact domain/scope.
Direct imported wrapper construction/defaults, ConstExecution values and
source-free remapping/re-emission remain open.
The provisional G1 source producer now accepts exact source declaration
selectors and explicit export keys, emits open MethodDef/MethodSig/GenericParam
and supported Constraint/default rows under exact schema selection, and
serializes the checked proof Body without closing its type formals for the
bounded compact-linear scalar subset. It also emits direct-return forwarding
calls as G1 Register Bodies with CallSite target kind 4 and record `0x10`.
That call subset targets a local module-level MethodDef with type-only
formals, explicit scalar/caller-formal TypeActuals, parameter Homes and a
return Home. Copy/read inputs are supported; a unique transfer is admitted
only for a direct parameter move-return target, with no intervening failing
operation. The source producer matches the synthesized cleanup wrapper to the
transferred Home, and the Body-map validator requires the transfer call to be
terminal before ReturnHome. It reconstructs supported ownership/nullability
domains and Write/Blittable obligations, requiring caller-formal constraints
where the actual remains open; Blittable declarations require Feature 13/1.
The source-free reader validates the target Body before accepting ownership
transfer, reconstructs the hidden local MethodDef dependency without provider
source, and specializes it for a separately compiled consumer; NativeJIT
execution covers private forwarding targets with Write, Blittable and unique
transfer obligations. It currently rejects owner methods, constant formals,
operator/interface requirements, nested type-default terms, dependency-bearing
contracts and Bodies outside those subsets. The strict
G1-only version-1 `--trrm-source-generic-method-exports` CLI transport checks
the ordered source paths and UTF-16 snapshots before binding and preserves
existing outputs on mapping failures. An unapproved, provisionally actionable
[G1 interface declaration export-input proposal](proposals/standard-trrm-g1-interface-declaration-exports.md)
adds a G1-only version-3 map that now publishes selected public source
interfaces with type-only unconstrained owner formals, a default Layout,
direct public bodyless Callable requirement MethodDefs/MethodSigs/Params, and
explicit TypeDef and MethodDef ExportKeys for scalar or owner-formal
signatures. The producer rejects bases, defaults, interface constraints,
associated members, member generics, non-value modes, contracts and foreign
dependencies. Version 1 remains G1 method-only and CG1 version 2 is unchanged;
no GenericCall wire identity, Feature or interface ABI changed. The exact
G1L1/G1L1R1 routes now emit and source-free bind bounded role-2 open-Constraint
calls against retained interface/requirement views; their regressions remain
unverified under the active hold. Base G1 remains method-only and rejects
interface declarations, constraints and role-2 calls. Closed-witness and
projection persistence remain open.
`CreateStandardReference` with exact G1 provider artifacts also binds direct
role-1 GenericCall MethodDef Imports by the selected Import row, ModuleRef,
ExportKey and provider snapshot; aliases for
one expected MethodDef retain separate views, and ambiguous direct-stub
selection rejects. `CreateGenericMethodCallsDraftReference`
now consumes an exact G1L1 artifact by Module identity, either all generic
exports from that Module or one explicit ExportKey. It decodes compact-linear
Bodies and bounded G1L1 role-1 GenericCall forwarding Bodies, including
direct and linear-call forms plus a single Boolean-parameter branch with a
unit call and unit join; it then binds closed scalar calls without provider
source. The conditional Body has source emission, source-free
decoding/re-emission and JIT regressions, all unverified under the active hold.
GenericCall substitution supports direct formal/scalar actuals plus nested
Standard `ref`, `ref<init>` and Span TypeSpec wrappers for role-1 targets.
Generic source MethodSig/Body maps preserve outer and inner Write
distinctions, and the source-free reader reconstructs scoped `ref<init T>`
reborrows. The bounded routes reject base-schema and mixed-provider-schema
substitution. G1 result
Dependency rows preserve storage-only relationship 1 separately from
backing-only 2 and union 3 through source-free decoding and per-source provider
comparison. Expected imported MethodDefs now retain those ordinary-result
Dependency rows when re-emitted; source-free ref-result forwarding coverage is
added but remains unverified. Source-authored syntax still does not declare
relationship 1, and independent GenericMethodImport/reference-set re-emission
remains open. Binding rejects invalid broad-to-storage narrowing, mixed-source
exclusion and provider-only source widening. Constructed nominal TypeSpecs for local or exactly imported public
TypeDefs now decode in generic MethodSig/GenericCall signatures. Imported
TypeDefs bind through exact ModuleRef/Import provider selections and compare
the current generic formal/default/constraint subset, fields and layouts;
source binding and source-free reading retain method-formal owner identity
through reference-set and graph remapping. The selected G1 source writer now
emits constructed nominal MethodSig/default types and bounded move/forwarding
Body type maps using checked TypeDef artifact graphs, preserving local
declarations or exact imported expectations, ordered scalar constant actuals
and admitted nested reference/Span actuals. Added source emission,
source re-emission, compatible TypeDef-row relocation and incompatible
replacement regressions are unverified under the active validation hold.
Expected imported MethodSigs also preserve constructed nominal parameter and
result types through checked TypeDef/Import closure.
Provider-to-method-importer-to-source-free-consumer coverage includes TypeDef
relocation and incompatible replacement, but remains unverified under the
active hold.
Expected imported MethodDefs also retain constructed nominal type defaults
through checked TypeDef/Import closure; provider-chain, relocation and
incompatible replacement coverage remains unverified under the active hold.
G1 type-default emission now also composes an indexed Span element with a
non-optional, non-GC checked-reference wrapper, including nested
`Span<ref<Span<T>>>`; source-free import binding, forwarded-formal and
incompatible-provider regressions are added but remain unverified under the
hold.
The writer now copies all retained TypeDef Import expectation views; a
reference-set emission regression is added but remains unverified. Fresh
generic TypeDef source declarations, source-free generic MethodDef re-emission
has a provisional explicit selector/emission path for the current G1 writer
subset under the unapproved
[selection proposal](proposals/standard-trrm-source-free-generic-method-export-selection.md);
public alias/helper/import, constructed-nominal, open-interface-call,
result-dependency, provider-replacement, source-free binding and JIT
regressions are added but remain unverified under the active hold. The bounded
path preserves direct same-root MethodDef helper Bodies without exporting
helper keys and retains external bodyless MethodDef Imports; broader
provider/nominal remapping, declaration copying,
associated-relation and non-frozen constant-default comparison, and
qualification remain open; see the
[module implementation boundary](implementation/modules.md#definitions-and-scope).
G1 source TypeSpecs now encode and reconstruct GC-backed `ref<T>` and
optional `ref<T>?` signatures/defaults, without selecting their runtime
execution; a provider replacement with weakened GC default is rejected.
Optional class handles in generic MethodSigs, type defaults and
nullability-permitted TypeActuals now use the existing TypeSpec `0x06`
wrapper, including selected source-free re-emission and provider-replacement
checks. Focused producer/import/forwarding and replacement regressions remain
unverified under the active hold. Optional wrappers nested as constructed
nominal type arguments, optional value types, writable/GC-qualified nominal
wrappers, GC execution, owner-type and role-2 nested substitution,
interface/witness or associated-relation obligations and arbitrary Body
control flow remain open.
Full replacement closure, generic-import re-emission, private persistence and
Standard closed-instance lowering remain open.
An unapproved [G1/C1 parameter-label proposal](proposals/standard-trrm-g1-interface-parameter-labels.md)
recommends exact G1L1 for version-1/version-3 G1 sources and distinct
C1L1/C1M1L1/Construction-1L1/Construction-2L1 whole schemas for their
label-preserving routes. G1L1 and the bounded C1L1R1, C1M1L1R1, Construction-1L1R1, and
Construction-2L1R1 generic/interface routes have writer/reader paths for
MethodDef labels, and source-free binding preserves expected labels when
provider names differ. These routes retain Feature 23/26 closure, reject
base-schema and mixed-provider-schema substitution, and preserve labels through
source-free calls and re-emission.
The separate provisional CM1 exact schema composes Construction-2 class
I/P/member surfaces with those inherited D1 owners, using 16-byte
MethodParameterSurface rows and closed SF1 scalar defaults. Its bounded source
producer, structural validation, generic/interface materialization and
same-view I/P checks are implemented but unverified; source-free class method
binding/re-emission and provider replacement remain open.
The `--trrm-source-generic-method-exports` CLI admits version-1 generic maps
and version-3 interface/generic maps only under exact G1L1. The bounded C1
generic/interface routes for C1L1R1, C1M1L1R1, Construction-1L1R1 and
Construction-2L1R1 use typed APIs; the Construction-1L1R1 route rejects
class declarations. A separate version-1
[class-source CLI manifest](proposals/standard-trrm-c1-class-source-cli-transport.md)
is provisionally proposed for Construction-1/2 L1R1 and PI1 successors, but is
not implemented and depends on complete class/member/Body closure; the G1 map
remains G1L1-only. Class-source closure, provider replacement/remapping and
lowering remain open. Ordinary nongeneric and CG1 labels remain outside that
proposal.
The [C1 enum-closure proposal](proposals/standard-trrm-c1-enum-closure-composition.md)
adds a separate EN1 candidate and class-source manifest version 2 for
enum-bearing Construction successors and a provisional Standard enum
occurrence carrier. Schema/table admission, classless source-only emission and
local/direct-import enum-only reference/re-emission are partial. Direct enum
TypeDef imports compare complete expected/provider contracts and retain
separate Standard expectations. Source emission also supports a bounded local
non-generic struct closure with FieldDefs, layouts, and enum TypeSpec
references; source-free type-only loading reconstructs that local mixed
closure. Fresh source emission can carry selected imported enum roots into
local struct FieldDefs. Mixed type-only loading also accepts direct imported
enum fields and non-generic struct TypeDef Imports; source-free re-emission
preserves TypeDef/FieldDef ExportKeys, Import edges and FieldDef TypeSpec
references. A two-provider Leaf-to-Wrapper-to-Packet regression now exercises
transitive imported struct closure and source-free re-emission/reload; it
remains unverified under the active hold.

EN1 source emission now also supports local non-generic class TypeDefs and
FieldDefs with class, struct and enum TypeSpecs, raw `ptr<void>` fields,
scalar, enum or non-generic struct-pointee `ptr<T>` fields and scalar-signature
`fnptr` fields in classes and inline structs, default layouts
and explicit class/field ExportKeys. Source-free references retain selected
class roots, class TypeDef/FieldDef Imports, and transitive local class
TypeDef dependencies; re-emission remaps nested class/struct/enum TypeSpecs
and exact root Import rows. Standard struct references also retain direct
local/imported class-handle FieldDef TypeSpecs with their exact root
TypeSpec/provider identities. Nested structs preserve unknown class ownership
and are rejected by Copy-sensitive verification. These class and struct routes
are unverified under the active hold and do not cover PI1 property carriers.
Provisional EN1 type+field-key reference selection now retains selected local
class fields and fields in the selected inline-struct closure, preserving
caller key order so new query imports use the first explicit alias while
existing Imports retain their keys. Source binding and mandatory verification
admit class-root offsetof FieldPaths through one
selected public instance field and subsequent inline-struct fields. Under the
unapproved exact class-layout successors, the bounded producer emits a
single parameterless `nint` method whose return expression may combine
class-root OffsetOfType operations, with one ordered FieldPath/0045 dependency
per query occurrence. Source-free load/re-emission preserves the selected
class provider and expected FieldDef imports while preserving co-located
direct FieldDef references, and native lowering derives supported
Auto/Sequential field offsets from complete class/inline-struct layouts,
including raw `ptr<void>` storage, selecting the exact target-profile variant
when present and refusing an unsupported exact variant instead of falling
back to the default.
Direct/nested load/JIT, multiple-query, exact-successor re-emission/reload and
native executable-image emission through both the direct emitter and Standard
native binding plan are added but unverified under the active hold; no image
execution is established.
Broader Body composition, provider
replacement and nontrivial dependency remapping, optional folding, additional
class representations, runtime/AOT and CLI support remain open.
No end-to-end EN1 support is implemented.
These source-bound definitions are not generally interoperable: source-authored
generic classes and complete nominal conformance rules remain unsupported.
Generic method formals retain verified nominal interface constraints with
mixed type/constant formal scopes. Interface constant domains can retain an
exact imported generic Transparent-wrapper identity such as `Wrapper<T>` in
an open method constraint; proof calls and caller-formal forwarding preserve
the type-formal scope. Imported generic TypeDef arguments can mix type and
constant formals when nested constants have supported scalar domains; exact
provider/TypeDef identity, typed values and caller-formal scope survive
constraints, proof calls and verifier reconstruction. Mismatched domains,
provider identities and unsupported TypeDef constraints reject. Local
source-generic Transparent wrappers such as `Tagged<T>` also retain exact
source-definition identity through interface constraints and proof
forwarding. Nested source-generic type arguments such as `Cell<7>` preserve
their concrete scalar constant actuals, and malformed source identities or
changed nested values fail mandatory verification. An earlier method
constant formal can also be forwarded through a wrapper's concrete scalar
constant formal, and source wrapper formal domains substitute earlier type
formals and earlier source constant formals. Dependent domains retain exact
source owner/ordinal identity through interface constraints and proof
forwarding; self/forward references and mismatched caller domains reject.
Source terms remain bounded to default-domain type formals, explicit arguments
and concrete scalar nested-constant domains. Generic aliases in generic method
constant domains now expand to exact target terms with type-actual substitution;
proof forwarding and verification preserve the resulting source-generic identity
and caller-formal scope, and finite alias cycles reject. Other generic default
substitution and source-free persistence remain unsupported. Source conformance now
retains exact interface instances, maps supported plain callable targets
(implicit or explicitly qualified), same-identity callable/property-getter
defaults, plain property getters, explicit associated-type bindings/defaults,
and typed associated-constant bindings without defaults. Calls require an exact
declared witness or a checked caller-formal constraint, including inherited
interface views; matching members alone do not infer conformance. Concrete
`.via<I>.Member(...)` calls and property-getter reads bind directly to the
selected witness member, retaining its exact requirement identity. Generic
method proofs use a private requirement stub checked against the declared
interface constraint; closed specializations rebind to the concrete witness,
without introducing a dynamic interface-object ABI. The verifier rejects
forged call identities and requirement stubs outside generic proof programs.
This dispatch subset still lacks ordinary overload ranking for same-name
requirements, interface default-body invocation, associated-type/value
projection, checked self projections, most generic interface members, property
setters, indexer init calls and operators. Indexer getter reads and direct or
interface-witness setter calls have bounded comma-argument source routes.
Default-body execution, init dispatch and
persistence remain open. Full published
ownership/effect/output/dependency compatibility and inherited replacement/
ambiguity rules also remain open. Witness identity is not persisted: private
and selected Standard source-module emitters reject nonempty witness tables,
and the G1 writer rejects interface constraints rather than omitting them.
Executable generic metadata remains incomplete.

Ordinary parameter defaults now have a bounded source route for non-generic
callable parameters, closed generic-method parameters of fixed scalar type,
and scalar interface requirements, including positional/named caller
materialization and source-order maps. Direct indexer getter/setter suffixes
and interface getter suffixes are included.
Generic-formal-dependent method defaults, aggregates, ConstEval-valued
interface defaults, interface indexer setter suffixes, and source-free
persistence/replacement remain open.

Imported
generic nominal types can participate in semantic binding, but native layout,
GC ABI, and execution are not implemented. Generic owner methods require a
closed compilation-local source generic struct instance. Neither specialization
path establishes runtime generic captures or support for every accepted
generic contract. The
[development native profile](implementation/native.md) and
[source grammar](specs/grammar-spec.md) define the relevant boundaries.
Within the compilation-local function-template path, exact `ref<T>` formal and
result patterns plus result `DependsOn` source ordinals now specialize for
closed default-domain arguments. The bounded proof carrier also retains ordered
GenericMethodSignature identities, formal-to-placeholder mappings, visibility,
operator requirements linked by ordinal to source-scoped protocol records,
dependency contracts, and exact generic call/closed specialization identities.
The protocol records preserve the qualified declaration name, source
input/span, operator shape, self role and unsafe obligation; verification
checks the links and proof context. Generic method type defaults also retain
imported generic nominal constructions as recursive type-term trees with the
exact provider/module/TypeDef identity, ordered type/constant arguments and
method constant-formal leaves, plus native-pointer, fixed-array and
function-pointer constructors. Pinned imported nominal defaults can infer a
primitive fixed-width integer or Boolean constant from a typed matching
nominal argument or expected result; the match uses exact definition identity,
ordered arguments, declared domain and canonical bits. Direct source uses of
imported generic nominal types bind closed scalar constants and frozen scalar
defaults. An internal, exact `NominalEnumsDraft` reference route also retains
enum-valued constant formals/defaults with their enum domain and int32 carrier,
supports direct enum actuals, and infers enum method constants from typed
nominal inputs or expected-result evidence. The default Standard reader does
not select this draft profile. These routes do not infer from layout, invert
arithmetic or inspect private provider bodies. Imported wrapper and
ConstExecution value domains, symbolic caller-formal forwarding in constructed
type positions, general nominal TypeSpec defaults and source-free remapping
remain open.
Type-formal constraints on imported nominal terms are still rejected.
Source semantic verification accepts checked generic nominal values in bodies,
but their native value layout/GC ABI and execution are not implemented.
Open pointer and fixed-array terms require earlier formals with
selected `Blittable` evidence; function-pointer terms retain the calling
convention and admit only ABI-supported scalar, pointer and nested
function-pointer parameters/results. Unsupported constraints and qualifier
forms reject rather than being discarded or projected onto placeholder
layouts. Method signatures preserve ownership/nullability domain tags and
selected `unique`, `unique?`, `null?`, `write` and registered `Blittable`
requirements. Unique-only formal values can be transferred with `move`;
direct `if (T is unique)` true/false branches refine `unique?` ownership,
including calls to matching unique-only and default-domain helpers, and the
branches produce an ordinary Boolean. Closed specializations select the known
branch. Saved correlations through immutable Boolean locals, copies and `!`
are retained. Compound Boolean paths and false-edge refinement after an `if`
without `else` remain incomplete. These terms remain
compilation-local and do not provide general source-free substitution. This
bounded hosted proof carrier does not establish
a complete open generic method route. The unapproved provisional G1 producer
and source-free reader now implement a bounded subset of the proposed Standard
MethodDef/Body carrier, including direct-return forwarding through a local
GenericCall target. This does not establish general type/constant substitution,
constraint or witness evidence, arbitrary Body composition, provider
replacement, generic re-emission or closed-instance persistence. The proposal
remains the design owner for those remaining consumers and call paths.
The unapproved private
[G108S proposal](proposals/private-span-type-term-persistence.md) supersedes
G107, adding type-only Span actuals/defaults and the formal Write fact without
admitting Span value positions. Current revision-106 writers, readers, sets
and graphs still do not preserve open generic definitions or calls.
Source generic lookup still selects one template per qualified name; accepted
complete overload-set ranking and disjoint constraint-only overload
validation remain unimplemented.
Bounded output combinations remain provisional only where they extend the
accepted profiles: fixed sides alongside mixed generic modes, or floating
results with mixed modes or fixed side outputs. Remaining decisions are in
the [proposal index](proposals/README.md); the accepted
floating-result contracts are in
[language contracts](specs/language-contracts.md#22-declared-outcome-relationships).
They do not establish general reference metadata or executable TRRM support.

## Executable TRRM scalar interchange

`compile-trrm` emits development `(1, 0)` metadata and compact-linear or indexed
Bodies for a bounded source-free, fail-fast scalar subset, separately from
private semantic-module encoding. its semantics overlap accepted contracts, but Development encoding is not the
selected [`trrm/1-draft-layout-queries-1` schema](specs/representation/trrm-1.registry.json).
Common TRRM metadata readers, writers and structural validation now use the
assigned 48-byte Module and exact Feature manifests, TypeDef/FieldDef rows of
44/16 bytes, Layout/LayoutField rows of 28/12 bytes, 8-byte TargetProfile
rows, and GenericParam/Constraint rows of 32/12 bytes. The bounded
source-free loader reconstructs supported Windows x64 Blittable
Sequential/Transparent layouts from their complete ordered field lists; size,
offset and final alignment remain target-derived, not serialized. This
does not migrate the general compact-linear/indexed Body formats or establish
whole-schema conformance. Under the exact Standard LayoutQueries selection,
`EmitStandard` and `LoadStandard` now round-trip bounded single-return
register Bodies with Feature 13, including NativeInt/int32/bool locals
initialized from the supported query expression subset, scalar-local
assignments and NativeInt compound updates: DE kinds 1..5, NativeInt
equality/inequality between query
results, C2/C3 ordered NativeInt comparisons, relation-driven C4/C5 selection,
C0 NativeInt arithmetic, D0 NativeInt-to-int32 conversion, and TypeIdentity
equality/inequality over TypeOf results. A source-free query-local
compound/reassignment Body and a comparison-selected SizeOf execute through
NativeJit and return 20 and 4. Kinds 1/2/5 bind TypeMap TypeSpecs;
kinds 3/4 bind FieldMap
DefId/Import tokens or complete-root FieldPath rows in section `004B`.
Consumers reconstruct verified query facts and check result types, complete
FieldPath roots and immediate owners.
The source-free FieldDef provider validator also admits the exact Standard
schema selected by LayoutQueries.

For exact Standard LayoutQueries, the source emitter also accepts explicit
TypeDef and direct FieldDef ExportKey mappings from the checked source
declaration snapshot. It emits type-only providers without requiring a dummy
MethodDef, preserves the complete Blittable layout closure, and requires
Feature 13/1 when publishing FieldDefs. Distinct explicit keys may alias one
actual TypeDef or FieldDef; composed references preserve selection order and
use the first explicit alias for new emitted imports. The selected Standard
reference overload projects explicit TypeDef/FieldDef keys as query-only
source types,
preserves their provider origins and rejects unselected field paths and
ordinary value use. Query emission writes provider-backed TypeDef/FieldDef
Imports and TypeSpec Import references; `LoadStandard` validates the provider
bindings before using the Standard query Body loader rather than the
NativeAddress admission path. Imported `offsetof(Pair, Tag)` is available
through Feature 13. Field-provider validation checks exact Import bindings for
FieldPath rows, register FieldMaps and compact field-access descriptors; this
does not add compact-body execution. The provisional
`CreateStandardReferenceSet` host composition is described below; broader
provider graphs, replacement and re-emission remain open. None of these
mappings grants Copy or value-use capability.

The bounded Body producer emits TypeIdentity TypeOf, short `40` E/U
comparison, C2 Boolean comparison, C4 selection, and a single Boolean-formal
diamond whose join carries TypeIdentity through BlockEntryState carrier 2.
Source-free consumption additionally supports `mov`, C2 control-00 relation
results and C3/C5 selection. It checks both incoming TypeOf values, the exact
join carrier and register availability; C2 E/U results may feed C3/C4/C5
predicates, ordered NativeInt relations may feed C3/C4/C5, and C5 preserves
its prior destination on the nonselected path.
Other Homes, calls and signatures cannot carry TypeIdentity. Control-flow
support includes a four-block Boolean-formal TypeIdentity comparison with a
carrier-2 join, and bounded acyclic NativeInt layout-query trees over Boolean
formal reads. The query trees support up to 13 conditionals, with a
carrier-0 TypeMap join at each merge and SizeOf/AlignOf/OffsetOfTypePath/
OffsetOfPlace leaves, including nested branches and multi-step FieldPaths.
A bounded nested-loop case compares TypeIdentity queries in a condition with
an inner Break and outer Continue; the source-free consumer reconstructs the
query registers without creating source locals. This is not general Standard
Body support.
The provisional `CreateStandardReferenceSet` path
composes exact query-only LayoutQueries references by selected provider Module
and TypeDef identity. Nested `Pair.Inner.Value` queries require selected Pair
and Leaf path fields. Unselected fields, ambiguous equal-name types and
conflicting provider snapshots reject. This is not broad
provider-graph, replacement or re-emission support. The source projection
remains nongeneric and requires Blittable scalar/inline-struct layout metadata.

For source generic methods, mandatory verification now substitutes each
closed local GenericCall's exact type and constant actuals into the owning
proof's TypeIdentity terms and compares them with specialized query subjects.
The comparison uses complete SourceGeneric and imported nominal identities,
including constant actuals, and rejects a forged specialized subject. An open
generic caller's nested proof call preserves its imported nominal type and
constant-formal actual identities.

NativeEmitter lowers fixed selected-target layouts/offsets and closed
TypeIdentity comparisons. Feature 13 loop Bodies carry NativeInt query values
through carrier-0 state; TypeIdentity comparison loops use carrier 2. The
bounded producer/consumer profiles include:

| Shape | Supported form |
|---|---|
| Single loop | Pretest or post-test; one structured `BoundIf` Break or Continue, or one Break plus one Continue in the bounded CFG form |
| Nested loops | Bounded two-level pretest/post-test combinations and selected mixed-depth transfers through depth five preserve query state. |
| Sibling loops | Two sequential loops, each independently pre/post; one direct Break or Continue in either loop, or one transfer per sibling for both Continue/Break orderings |
| For iterators | Bounded Boolean-condition loops with a Bool/NativeInt assignment iterator; body fallthrough and Continue execute the iterator, while Break skips it. |
| Query state | One NativeInt update in either loop; Break, Continue and mixed sibling transfers preserve carried state; carrier 2 for single-loop TypeIdentity; one nested TypeIdentity condition remains a transient query value |

Consumers validate bounded reachability, liveness, dominators, natural-loop
regions and exact entry-state types. Standard-loaded Bodies execute through
Windows x64 NativeJit caller wrappers. Additional depth-four/five transfer
combinations, six-or-more nested levels, three or more sibling loops, and
nested/sibling combinations beyond one-transfer-per-sibling and
Break-plus-Continue profiles remain unsupported.
Iterator forms outside the bounded assignment profile,
exception/cleanup exits, arbitrary Body composition, unsafe/non-Blittable
queries, generic query subjects, AOT and other targets remain unsupported or
unqualified.
Standard FieldPath and retained target-layout
tables also have structural readers/writers that validate complete
TypeSpecRefs, FieldDef/Import FieldReferences, TargetProfile rows and exclusive
`004A` ranges; the separate provider path validates constructed generic owner
substitutions and imported-stub obligations.
FieldDef DefId/Import/Export rows also have structural C# support for owner
imports and public local publication; exact field-contract and retained-layout
comparison remain incomplete. The hosted structural validator now checks
Feature 13/1 against the using Module for FieldDef Import/Export, selected
Bodies containing `DE`, TypeIdentity or a nonempty FieldMap, CopyOrigin
FieldPath contracts, general-graph layout/path operands, and retained
target-layout dependencies in ConstantExecution. A Terrum-authored
Feature-manifest consumer is not currently present in the maintained component
tree. The out-of-band `CopyOriginDraft` profile still recognizes provisional
Feature 15/1 and contract-only nonpublic TypeDef/FieldDef identities; it does
not read or write Contract 000D and is not the selected current schema. The
`compile-trrm` source route still emits its separate Development profile and
does not claim whole Standard schema conformance.
Consumers load artifact bytes, validate metadata and Bodies, reconstruct typed
inputs, and invoke the shared mandatory verifier and backend. General
imported-call linking and standard
exception-region Body support are not implemented.
The unapproved exact `B+E1` candidate now routes scalar compact Bodies through
Standard emission/loading, source-reference creation, and CLI run/build. Its
structural `0F`/`0A` readers validate directory ownership, and the mandatory
Body-map validator checks exact Boolean typing for `FilterComplete` values,
including register-state liveness, and matches catch-binding Home types to
CatchType maps by complete type semantics rather than raw TypeMap tokens. Home
initialization remains a producer/verifier obligation. The nonserialized
`CoreExceptionIdentitySelection` carrier is validated and preserved through
`CompilationReference`, `CompilationOptions` and `BoundProgram`. Standard
`LoadStandard`/`CreateStandardReference` now preflight exact B+E1 using and
provider Module identities and public TypeDef ExportKeys before verification.
The B+E1 loader accepts separately supplied provider artifacts only when every
artifact selects exact B+E1; its root artifact still cannot contain ModuleRef
or Import rows. The selected interface/payload contracts and witness binding
remain open. Core identities are not serialized. Exception dispatch is not
implemented. Bounded registered-query loops preserve Continue, Break and
fallthrough through Protected/Finally, including Home-carried state and
iterator ordering through Standard-loaded NativeJit. General Standard
stream-Body production/loading, provider imports/remapping, source catch/filter
forms, and JIT/AOT continuations remain incomplete. This candidate does not
change current conformance. Body-local optional scalar values use independent
presence and payload state, with assigned operations limited to the supported
linear/indexed Body forms.
Optional floating types remain rejected, and optional `char16` is limited to
its assigned optional operations and equality. `MakeOptional`, `None`,
`IsPresent`, `Copy` and eager `Select` retain their exact presence/payload
rules. This does not add optional source types, optional call signatures,
native ABI support, optional managed values or broader optional operators
to this Body path. This path does
not move source verification or emission into Terrum-authored components.
Static initialization is not encoded or executed by the current TRRM path.
The accepted first implementation gate is a self-contained, single-module
profile for non-partial nominal types with ordinary eager scalar static fields,
get-only auto-property backing, and supported closed source-generic struct
instances. Authored static constructors use only the accepted trigger choices.
The hosted source binder also pairs one
bodyless primary
`static partial` declaration with one implementation, preserving the primary's
trigger and clauses and the implementation's body/source span. Static
field declarations are now kept outside instance layouts with owner, access,
mutability, type and source-order facts; eager declaration initializers bind
through checked hidden functions, and direct/unqualified reads plus mutable
writes use their exact field identities. Fields without declaration
initializers require an authored static constructor. Mandatory verification
requires every non-lazy field to be initialized on every normal return and
rejects repeated initialization of immutable fields; conditional, loop and
checked-catch joins preserve those initialization facts. The source binder
also binds get-only static auto-properties into their owner's group at the
property's declaration position, using an internal backing-field identity.
Their declaration initializers use checked hidden functions; a property
without an initializer requires static-constructor initialization. Computed
properties add no backing storage. Additional setter/init or explicit
property-local backing forms remain incomplete. The source binder also
recognizes LazyInitialization only through the
separate exact Core registration and its direct, field-selector or alias
paths; this provisional source support does not execute a lazy unit.
File-visible static fields retain their source-input ordinal; reads, writes
and initialization references require that same source unit.
Same-module friend access is also bound for static fields, methods and
get-only static auto-property getters. It preserves exact nominal principals
and closed generic arguments, supports a single `_` generic wildcard, and
rejects unrelated or cross-module callers. Interface default bodies are not
represented as callable bound functions, so interface friend access remains
incomplete. File/friend access facts are source/bound-only and are not yet
representable by the selected static persistence candidate.
Static-constructor and field facts are not yet persisted or executed;
native lowering, private module production and Standard TRRM emission reject
them. Execution requires enforced nonconcurrent access and fail-fast
termination. The gate excludes lazy runtime execution, setter/init or
explicit-field property backing, foreign storage, source-generic class owners,
partial nominal-type composition, interface friend bodies, and
cleanup-bearing static values. Imported or packed definitions cannot
participate in an initialization dependency, though unrelated imports may
remain in the module. This is an implementation boundary, not a source
restriction or a complete TRRM 1.0 GA profile. Remaining static schema and
runtime integration gates are tracked in
[CG-008](implementation/conformance-gaps.md#cg-008).
The development reader/compiler also supports same-format binary32/binary64
comparisons, raw-bit constants, Copy/Select, format conversion, fixed-width
integer conversion, char16-to-floating conversion, sign-bit negation and
same-format addition/subtraction/multiplication/division. Float-to-integer
conversion covers the eight accepted checked pairs. Target rounding, code-unit
semantics and the processing-result NaN contract remain independent of their
encoding. These operations remain Development-only; they do not establish
general Standard register-Body execution.

The compiler must migrate the complete selected schema, references, indexes,
manifests and independent fixtures before claiming current standard
interchange; see [CG-001](implementation/conformance-gaps.md#cg-001).
Unsupported additional formats or operations remain explicit boundaries, not
reasons to freeze the accepted design.

## Platform and validation

Native execution is Windows x64-only, although PE emission does not
invoke an installed platform toolchain. Use the [build guide](building.md)
for repository commands and the [native exception probe](native-seh.md) for
the bounded unwind evidence.
