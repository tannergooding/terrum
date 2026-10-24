# Compiler conformance handoff

This inventory separates accepted obligations from work conditional on proposal
approval. It is a handoff, not an exhaustive compiler audit or a conformance
certificate. Source/test observations below are static evidence; naming a test
does not mean it ran or covers the complete contract.

The [accepted specification](../specs/README.md) records current acceptance,
not immutable pre-v1 design. An implementation session must select its exact
accepted or explicitly authorized experimental contract. Pending assignments
remain in their proposals; do not implement them merely because this inventory
links them, preserve unpublished encodings automatically, or reinterpret old
bytes under a new schema.

**Statuses:** **Ready** means accepted work can be qualified and implemented;
**Blocked: design** needs the named choice or approval; **Blocked: dependency**
needs its prerequisite mechanism. **Bounded implementation; evidence required**
is a qualification gate, not an assertion of a newly reproduced defect.

| ID | Area | Principal prerequisites |
|---|---|---|
| [CG-001](#cg-001) | Current-registry TRRM producers and consumers | Exact selected schema |
| [CG-002](#cg-002) | Shared references, dependencies and output state | CG-001 for standard persistence; reviewed codecs |
| [CG-003](#cg-003) | Failure, cleanup and publication composition | CG-002; reviewed exceptional Body representation |
| [CG-004](#cg-004) | Native declaration and Body integration | CG-001/002/003; native schema approval |
| [CG-005](#cg-005) | Accepted layout and Windows x64 ABI | Selected target; CG-001/004 for standard persistence |
| [CG-006](#cg-006) | Generic nominal identity, witnesses and properties | CG-001/002/003; coordinated TypeSpec, Constraint and witness-reference allocation |
| [CG-007](#cg-007) | Construction, literals and constant programs | CG-002/003/006/010; reviewed value/program codecs |
| [CG-008](#cg-008) | Static initialization | Reviewed storage/group/unit codec |
| [CG-009](#cg-009) | Owning fields and partial retirement | CG-002/003; complete ownership records |
| [CG-010](#cg-010) | Portable proof and constant-evaluation floor | Accepted source rules; codecs for independent exchange |
| [CG-011](#cg-011) | Numeric and selected library contracts | Registered identities, witnesses and actual programs |
| [CG-012](#cg-012) | Quiet arithmetic NaN qualification | Existing floating paths; no fixed-runtime-payload approval |
| [CG-013](#cg-013) | Non-unit DoesNotReturn | Accepted source and clause applicability |
| [CG-014](#cg-014) | Signed-minimum bootstrap remainder | Accepted integer arithmetic |
| [CG-015](#cg-015) | Scalar frontend record migration | Int32/nint records, source-sized storage, and standalone compiler fallback |
| [CG-016](#cg-016) | Bootstrap/frontend profile integration | Coherent envelope and invocation decisions |
| [CG-017](#cg-017) | Direct span views and deferred persistence/results | CG-002; current views remain in-memory and returned-view work needs a consumer |
| [CG-018](#cg-018) | Source grants and native promotion | Private JIT source profiles; no native source ABI selected |
| [CG-020](#cg-020) | CAS and concurrent execution | Selected APIs, representation and physical operations |
| [CG-021](#cg-021) | Tasks, iteration and generated producers | CG-002/003/006/009; completion/phase codecs |
| [CG-022](#cg-022) | Roots, handoff, code and failure-boundary qualification | Complete selected runtime/target profiles |
| [CG-023](#cg-023) | CLI and source-input conformance | Accepted machine/source-input contracts |
| [CG-024](#cg-024) | Representation extensions and resource accounting | Encoding decisions and calibrated recipes |
| [CG-025](#cg-025) | Generic native regression and qualification evidence | Final selected compiler inputs and active cases |
| [CG-026](#cg-026) | Source vocabulary, grammar and binding convergence | CG-023 input identity; accepted source owners |
| [CG-027](#cg-027) | Normal/GC backing, roots, pin reborrows and Cleanup | CG-001/002/003/004/006/009/022/026; selected GC type/runtime carriers |

## Evidence and closure rules

Read the [test project exclusions](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
and each selected test's gates. Retained prototype tests and
`BoundTreeTraversalTests.cs` are excluded; they are not active coverage. Keep
ordinary tests headless. Window/hardware qualification requires its documented
opt-in and cannot be substituted for generic regression cases.

Implement general facts through binding, shared nested traversal/rewriting,
mandatory verification, persistence, dependency binding/replacement and lowering.
Test public compiler APIs rather than adding internal-access grants. An
unsupported complete contract must decline before affected use, not erase facts
or produce success-shaped defaults.

Close an entry only with evidence for its final relevant patch/configuration
and exact compiler/dependency inputs. Distinguish parser, semantic, emitted-image,
native ABI and supervised runtime evidence; record passed, failed, skipped and
incomplete cases separately. Use the normal [build/test workflow](../building.md).
Qualification tooling consumes those outputs and does not silently build or
select another compiler. Existing valid evidence can be reused when its relevant
inputs remain unchanged.

<a id="cg-001"></a>
## CG-001: Current-registry TRRM producers and consumers

**Blocked: accepted TRRM producer/consumer implementation and qualification.**
The exact draft schema is integrated across the
[registry](../specs/representation/registry.md),
[manifest](../specs/representation/semantic-manifests.md),
[reference/admission rules](../specs/representation/requirements-and-admission.md)
and [independent vectors](../specs/conformance/il-registry.md), which together
define the target.
The full accepted TRRM 1.0 requirements define GA conformance; there is no
separate reduced scalar execution floor. The draft registry still lacks
required capability implementation and complete runtime/target carriers, so
schema assignment does not establish executable support for every approved
contract. Consumers require exact agreement on
`trrm/1-draft-layout-queries-1`; equal format bytes do not select an
alternate schema.

**Current/evidence:** `TrRmBodyExampleTests.ReadExample` explicitly embeds
development-schema fixtures; private/native and output experiments retain
other assignments. See
[ModuleCompiler](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs),
[Feature admission](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs),
[Body fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmBodyExampleTests.cs)
and the [module inventory](modules.md). Existing test success would not establish
current-registry decoding.

**Routes/gate:** migrate the complete producer/reader/rewriter and executable
consumer set to the accepted register Body: implementation kinds `01..05`,
the `17 + 4*popcount(mapMask)` prefix and five optional maps,
`instructionEncoding=01`, the fixed Block table, typed-record offsets/data
and one-/two-/four-byte instruction partitions. Body-local indexes are
zero-based `u16` with `u32` counts through 65,536; global tokens remain
fixed-width. Implement `DE` kinds 1..5; FieldMap tokens; global FieldPath
section `004B`; target-layout field references in `004A`; TypeIdentity
carrier/comparison/conditional flow; and Feature 13 admission. Preserve exact
TypeSpec/Import/MethodDef identity, F0/F1/F2/F3 TypeMap tokens, the
TypeSpecRef row/F2/F3 union in every complete type-valued field, and the
source operation/type, effect, failure and ownership contracts.

Independently decode the revised vectors; exercise every tiny tuple, short
and long control domain, register-role/reserved-field rejection, `0xFFFF`/
65,536/65,537 boundaries, Block/record extents, SourceMap recipe steps,
compact descriptors, pseudo-type cycles/modifiers, place-handle BlockEntryState
carrier/access rows and their verified edge flow, RequirementFailure Boolean/
uint8-mask condition selectors and constructor bindings, terminal
DoesNotReturn CallSites, all map masks/empty-present framing, every `DE`
control/map selector, TypeIdentity evidence/joins/escapes, FieldPath table
rows, FieldDef Import/Export binding, unsupported capabilities, same-version
incompatible drafts and packed/import contexts. Check scoped
selective access separately from whole-artifact audits. Rebuild decoded
operations, maps, records, references, code offsets and SourceMap data rather
than byte-substituting old fixtures. The earlier SSA/value-ID Body draft is
not a compatibility target; no legacy decoder or new format pair is required.

<a id="cg-002"></a>
## CG-002: Shared references, dependencies and output state

**Accepted source, logical-row and TypeSpec assignments; end-to-end
implementation remains blocked on dependent carriers.**
[References](../specs/language/references.md),
[dependencies](../specs/language/dependencies-and-views.md),
[initialization](../specs/language/initialization-and-flow.md),
[calls](../specs/language/calls-and-inference.md),
[contract clauses](../specs/representation/contract-clauses.md) and the
[shared logical records](../specs/representation/logical-records.md) own the
accepted contracts. TypeSpec form `0x0E` assigns `ref<init T>`; qualifier
bits assign inner/outer `write` and `gc` modifiers and inner `init`.
`Span<init T>` uses whole-range initialization state in OutputState, not
per-element state. OutputState predicate references still depend on the
shared published-predicate codec, and required Feature implementation and
complete prerequisite closure remain outstanding. These are coordinated
physical and implementation dependencies; the linked owners define the
source semantics.

**Current/evidence:** the [output inventory](output-profiles.md) distinguishes
bounded hosted/generic paths from ScalarInitOutput's experimental mode, Feature
and Body rules. This is implementation evidence, not a scalar-only semantic
floor. [Scalar output reader cases](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmScalarInitOutputBodyReaderTests.cs)
are not the complete accepted reference/output model.

**Routes/gate:** preserve complete descriptor types, receiver versus formal
identity, eligible storage borrow versus backing forwarding, explicit/default
source normalization, actual output/destination versions and capture phases.
Derive per-call outcome correlation from the selected callable contract and
verified Body call/value/place facts rather than an opaque success flag;
producer correctness does not waive required schema, structural, binding or
exposed-consistency checks.
Preserve exact annotation targets across ordinary Boolean returns, async
completion payloads and non-async Task-returning declarations; a return target
does not select an initialized descriptor or its responsible operation.
Exercise `write ref<init T>` vacant/read/write transitions and whole-span
`Span<init T>` complete/vacant outcomes, without partial-range state. Exercise
empty source sets without losing definition-owned dependencies;
readonly/pinned/writable distinctions; optional-null versus vacancy; unit
outputs; lifting and later completion; named arguments, staged mutation,
aliases, branches/loops and separate providers. Reject unauthorized reads,
publication or authority/lifetime upgrades. Standard persistence depends on
CG-001 and completion of the identified predicate and checked-output carriers,
not on retaining experimental `out` codes or UnsafeTail. Native output
semantics are not carried by a native-specific clause or Dependency
destination; use the ordinary shared relations where sufficient, otherwise
remain unsupported.

<a id="cg-003"></a>
## CG-003: Failure, cleanup and publication composition

**Accepted exception/output behavior; standard Body execution blocked on
physical region and capability carriers.**
[Failure/exit contracts](../specs/language-contracts.md),
[local failures](../specs/language/local-failures.md) and
[captured events](../specs/language/captured-exceptions.md) govern source
behavior. The accepted Body exception model is owned by the
[instruction contract](../specs/il-instruction-set.md) and
[logical-record inventory](../specs/representation/logical-records.md);
the ExceptionRegion directory and handler-row physical layouts, explicit
exception-object throwing/transport carrier, ParentException runtime storage,
and general Body-handler capability remain unassigned. Native exception
unwinding is separately registered as Feature 7; registration does not supply
the missing Body-handler codecs. The prototype's reason-only `Fail`
terminator does not encode general exception-object propagation or limit
operation/call failure semantics. No reduced fail-fast-only TRRM 1.0 floor
is selected.

Runtime `require` false paths use their exact ordinary zero-supplied
ConstructorBinding, not the `IException` common factory; constructor
evaluation failure propagates unchanged. This source construction rule does
not replace the class-specific common creation paths assigned to other
scalar/runtime failure categories.

The register codec assigns `Throw` and `RegionExit` instruction/record
shapes, but those shapes do not define the missing ExceptionRegion inventory,
ordered handler rows, filter/finally programs or runtime event carrier.
Bodies requiring those semantics remain unsupported until the dependent
physical contracts and capability closure are assigned and implemented.

**Current/evidence:** [native support](native.md) and
[output support](output-profiles.md) describe implementation behavior, not
the complete accepted typed-handler, filter, ParentException and output/
ownership contract.
The current prototype still exposes a `checked-exceptions` runtime-policy
switch and the retired `NoThrow` effect; those paths do not conform to the
accepted explicit-unsafe-catch and no-`NoThrow` contract.
[Provider output/failure cases](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeProviderOutputFailureTests.cs)
cover a bounded separate-compilation surface.

**Routes/gate:** route failures from the active instruction through lexical
ExceptionRegion ancestry and ordered typed/filter handlers using each
operation/call's declared failure semantics. Exercise `require` true paths
without construction, false paths with the retained ordinary constructor
binding, and constructor failures without fallback or fabricated events.
Do not serialize per-site
exceptional-state snapshots or treat failure as a normal CFG edge. Preserve
the exception object and origin; a handler/filter/finally failure replaces
the pending event and retains it through the runtime ParentException chain.
Catch and post-catch continuation are unsafe with indeterminate general state;
fallthrough propagates the same event, while explicit non-failure exits
swallow it and run each crossed `finally` under structured transfer order.
Test filter order/failure, same-event propagation, replacement-parent
identity, explicit swallow, finally routing without replay, output
publication only on declared normal outcomes, unpublished partial
responsibility and every-exit moved-input ownership. Do not infer automatic
owner cleanup, rollback or recovered output state. CG-002 supplies shared
places/dependencies/output relations; the ExceptionRegion directory, ordered
handler rows, explicit exception-object transport and runtime parent storage
remain physical blockers.

<a id="cg-004"></a>
## CG-004: Native declaration and Body integration

**Accepted contract; implementation and qualification gap.** The
[native boundary rules](../specs/native.md),
[metadata schema](../specs/il-metadata.md),
[registry](../specs/representation/trrm-1.registry.json), and
[target ABI contract](../specs/data-layout-and-abi.md) own the approved source
roles, records, capabilities and target-derived behavior. Features 1-7 are
registered; registration does not establish implementation support.

**Current/evidence:** [modules](modules.md#persisted-caller-unsafe-operations)
and [native support](native.md) inventory current experimental native
sections/clauses/Features, copied vtable bindings and private revisions.
These do not yet implement the selected schema. Active
[function-pointer](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs)
and [vtable](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeVTableCallTests.cs)
cases are bounded implementation evidence, not approval.

**Routes/gate:** migrate declaration producers/readers, structural signature
identity, unsafe regions, pointer/dependency facts, dependency materialization
and all emitters together. Cover imported addresses without proxy-body offsets;
address-only import accounting; callback/code-origin lifetime; local dispatch
retaining its distinct imported expectation; provider replacement and
re-emission; nested pointer cells versus pointees; actual backing/alignment/
retention and absent optional annotations. Exercise callable/data imports,
explicit method/data exports, `NativeCallable` contracts, interface defaults
and inherited slots, function-pointer identity, Dynamic provider retention,
field mutability, target-profile layout variants and native exception handling.
Native call/member layout, stack walking and unwind records follow the selected
target ABI; unsupported mappings reject. Do not add API-specific policy or
native pointer/output clauses. Use unrelated synthetic APIs and independently
constructed malformed artifacts. Depend on CG-001/002/003 as applicable.

<a id="cg-005"></a>
## CG-005: Accepted layout and Windows x64 ABI

**Ready for accepted target/source rules.** The
[layout contract](../specs/data-layout-and-abi.md) and
[Windows x64 target](../specs/targets/windows-x64.md) select packing,
explicit nested alignment, Union, scalar storage/validity, call
classification and non-unwinding ordinary boundaries. These choices do not
need reopening because support is incomplete.

**Current/evidence:** [layout support](layout-profiles.md) describes natural
Sequential/bounded Transparent source support and pending wider controls,
Union, standard commitment persistence and qualification.
[PublicFixedLayoutStorageTests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/PublicFixedLayoutStorageTests.cs)
and `Binder`'s layout-attribute path exercise that bounded support.
Neither compact ValueLayout limits nor native-pointer staging limits are
Windows ABI size ceilings.

**Routes/gate:** implement/qualify the
[target cases](../specs/conformance/target-windows-x64.md), including all
scalar representations, combined packing/alignment, nested propagation,
zero data, malformed layouts and exact-extent packed accesses. Check complete
producer/consumer exchange with target identity and full field membership;
consumers recompute offsets, extent, alignment and stride. Reject missing
transitive commitments, forged layout facts, storage cycles, unsupported
targets, invalid attribute arguments and incompatible replacement. Preserve
nominal identity, field access/mutation, caller-unsafety and stronger physical
facts through replacement.
Implement and qualify the accepted `DE` SizeOf/AlignOf/OffsetOf path for
selected targets, including direct non-generic fields, generic/nested
FieldPath rows, cumulative versus terminal offsets, retained `0045`/`004A`
dependencies, FieldDef Import/Export layout membership and fixed/coupled
replacement. This codec assignment does not provide a dynamic layout model
or close the underlying target/layout implementation gate.
Select the actual supported language version and diagnostic behavior rather
than inferring support from attribute parsing.

Independently compare storage and call classification with a Windows x64 C
fixture. Probe direct 1/2/4/8-byte aggregates, indirect 3/5/12/16/24-byte and
larger records, hidden results, mixed physical ordinals, over-aligned
supporting copies and original-place borrows. Separate storage, emission and
execution claims. Standard persistence requires CG-001 and the accepted
complete layout/field-variant carrier in the selected registry schema.

<a id="cg-006"></a>
## CG-006: Generic nominal identity, witnesses and properties

**Source, logical and TypeSpec directions are integrated; independent generic
exchange remains blocked on incomplete capability registration, reference
codecs and the existential-value carrier.**
[Types](../specs/language/types-and-normalization.md),
[witnesses](../specs/language/interface-witnesses.md),
[constant generics](../specs/language/constant-generics.md) and selected
library contracts are accepted. Logical records are owned by
[TRRM logical records](../specs/representation/logical-records.md), physical
assignments by [metadata](../specs/il-metadata.md) and the
[registry](../specs/representation/trrm-1.registry.json).

**Current/evidence:** [library support](libraries.md) describes
compilation-local closed internal generic structs and bounded function
witnesses. `Binder`/`Binder.ManagedClasses` and
`GenericNominalTypeTests` deliberately reject broader public/class/
conformance/constraint/generic-method forms. Constraint-row framing alone
does not execute a generic contract.

**Routes/gate:** preserve complete declaring/implementing/selected instances,
type and constant arguments, associated/generated identities, method formals,
constraints, witness origins and actual selected members through substitution,
verification, persistence and replacement. Map selected generic method
formals by ordinal after owner substitution; reject inferred permutations or
adaptations. Unmarked type formals use the
non-unique Copy-capable non-null domain; `unique?` and `null?` are independent
domain modifiers, and no explicit `[Copy]` constraint exists. Verify
constraint-only overloads are pairwise disjoint, generic bodies across their
full declared domains, and compile-time property branches with true-edge
proofs and complements only for closed exhaustive domains. JIT/AOT may resolve
such branches during closed-instance specialization, not as runtime reflection.
Feature 13 now assigns the internal TypeIdentity carrier and exact E/U
comparison flow, but open generic TypeOf still needs exact execution-model
identity evidence; the entry carrier or formal declaration ID cannot supply
it, and no reflection/dictionary ABI is selected.
Use the approved 32-byte `GenericParam` shape for formal kind, type-only
ownership/nullability domains and typed defaults; use the approved 12-byte
`Constraint` shape with closed kinds `1=interface instance`,
`2=intrinsic property`, `3=associated relation`, and property codes
`1=Unique`, `2=Write`, `3=Blittable`. Preserve constructed arguments in the
existing variable-length TypeSpec prefix/payload scheme, using the assigned
well-known and referenced-type argument forms; the Body TypeMap also has
well-known and pseudo-type token forms. These assignments replace the TypeArg
child range and separate existential-marker TypeSpec; section `0x0012`
remains unassigned. Do not revive the older fixed 8-byte tagged-slot
candidate. A first-class existential-value owner is keyed
to the owning semantic value occurrence, including Body values and
parameter/result/storage boundaries; Body value descriptors are not the sole
carrier. Its evidence range has one entry per hidden formal ordinal, including
an evidence shape whose nested typed range has exactly one slot for each
applicable interface-instance `Constraint`. Statically known formations retain
the concrete hidden TypeSpec and exact direct `InterfaceWitness` or closed
`WitnessProjection`; parameter/result/storage owners define the evidence shape
while each runtime value supplies its own concrete type and witness references.
Projected witnesses map every normalized requirement exactly once, including
unchanged/direct mappings. Property and associated-relation constraints retain
their own typed facts rather than becoming witness entries. Owner keys, row
fields, runtime carrier, typed references and registry allocations remain
gates.
Treat associated type declarations as first-class DefId/Import/Export
identities, with witness bindings keyed by the exact complete interface
instance. Keep associated constants in separate first-class definition and
witness-binding records, reusing the typed constant-value/evaluation
contracts rather than static-storage or callable records; every complete
witness supplies a binding, with no declaration default in this profile.
Requirement `GenericCall` records retain the exact open `Constraint`, closed
`InterfaceWitness` or closed `WitnessProjection` evidence; do not derive it by
name or private-body inspection. An open formal `Constraint` carries no
closed witness reference. Its payload is typed by the constraint kind as an
interface-instance TypeSpec, intrinsic property code or associated-relation
reference; validate and remap by variant.
`write` constrains immediate access on the complete normalized type argument
without synthesizing it. GC backing and root identities are accepted source
distinctions but have no assigned TypeSpec encoding; pinning is a separate
backing dependency, not a type qualifier or generic property. GenericCall
evidence kinds are
closed as `0=none`, `1=Constraint`, `2=InterfaceWitness`,
`3=WitnessProjection`; target roles are `1=direct`, `2=interface
requirement`. No runtime generic dictionary is implied.
Include general nominal-generic existential views such as `Task<_>` with
per-value hidden-type/witness correlation, operation filtering under declared
constraints, selected typed recovery, ownership/settlement and a
representation-agnostic carrier. Persist one evidence entry per hidden formal
ordinal and value under the first-class existential-value owner; each hidden
formal's nested range specifies one evidence slot per applicable
interface-instance constraint. Preserve concrete type/witness facts at known
formations and carry each value's own facts across parameter/result and storage
boundaries. Do not substitute Body-only ownership, value-global evidence or
consumer-side reconstruction.
Test unrelated providers, inherited
instances, nominally distinct equal layouts, wrong argument/default domains,
qualified arguments and malformed remaps. Define one coherent owner for
Unique/Sized/property facts and the native scoped representation commitment;
use one `InterfaceWitness` family keyed by implementing `TypeSpec` for nominal
and primitive identities, with primitive membership restricted to privileged
Core declarations and no fabricated nominal rows. Sections `0x0025..0x0036`
and `UnionCaseDef`
DefId kind `0x21` are assigned. Complete witness/call/value/existential
reference codecs, authority checks, typed remapping, remaining required DefId
kinds, the existential runtime carrier and complete generic Feature
registration remain gates. Feature IDs `9..12` are assigned to the
proof/value/execution capabilities, but their registrations are incomplete;
Feature 13 now registers the TypeIdentity and layout-query representation,
but exact open-generic identity evidence remains an implementation dependency;
generic candidate Feature IDs `6..8` remain unassigned. Assigned rows do not
establish independent exchange support.

<a id="cg-007"></a>
## CG-007: Construction, literals and constant programs

**Accepted source and logical record directions are integrated; independent
execution remains blocked on complete typed codecs and capability closure.**
[Literal construction](../specs/language/literal-construction.md),
[handlers/builders](../specs/language/construction-handlers.md) and
[published forms](../specs/language/published-predicates-and-effects.md)
own the selected behavior. The corresponding record families are in
[logical records](../specs/representation/logical-records.md); assigned
sections are in [metadata](../specs/il-metadata.md).

**Current/evidence:** [library support](libraries.md) distinguishes parsed
spread syntax and private generic specializations from executable registered
builders/protocols. A self-hosted method or candidate API sketch supplies no
registered identity, retained constant program or frozen value codec.

**Routes/gate:** retain exact role/member/witness identities and contextual
result, once-only source-ordered captures, after-capture acquisition, ordered
formatting/spread drain, refusal and accepted-prefix effects, output/owner
publication and abandonment cleanup. Qualify factory versus direct-cursor
capture and never-reached cleanup. Capture modes encode copy versus ownership
transfer only; complete capture types and dependency/lifetime facts separately
preserve reference/access placement and backing/provenance. Cross-check
ordinary/pure/constant forms,
typed Unicode/format data, nominal value validity and inclusive resource
boundaries. `BuilderRegistration`, `TextTarget`, and the
`TextContentShape`/`TextCaptureShape`/`TextFragmentShape` candidate rows and
module-local shape key are approved; typed-reference encodings, registry
allocation and generated-producer support remain gates. Source-free execution
needs CG-002/003/006/010 and the approved separate
evaluation-form, PredicateGraph, per-observation footprint,
per-request `ConstantExecution`, and typed frozen-value record families.
`countof(Type)`/known-shape Count produces ordinary typed `nint` constants;
live Place Count remains a witnessed `ICountable.Count` call. Reached layout
queries retain one `TargetLayoutDependency` and ordered `004A` field range
per observation; TypeOf retains identity evidence without a layout row.
The `PredicateGraph` references one separate actual-footprint record with flat
typed ranges for the complete actual read-root set and direct call/witness
dependencies; 8-byte tagged candidate schemas now define both root entries
and direct-dependency entries, with disjoint per-footprint range ownership;
imported MethodDef dependencies follow the expected declaration's Import
binding. Consumers recursively validate
dependencies' own records, and phase and an explicit zero-based, contiguous
phase-local declaration ordinal remain on the MethodDef Contract clause;
physical row order does not encode declaration order.
Use one Contract clause kind across predicate phases, with `u8` phase tags
`1=Precondition` and `2=NormalPostcondition`; body-level static obligations
remain separate. The approved candidate row is 20 bytes total: the 8-byte
Contract header followed by `u8 phaseTag, u8 flags=0, u16 reserved=0,
u32 phaseLocalOrdinal, u32 predicateGraphRef`. The clause kind and
PredicateGraph section are assigned; typed-reference resolution/remapping
and complete Feature registration remain open.
`ConstantExecution` owns its selected checked Body/intrinsic,
substitution, transformed inputs, executed dependencies, per-query layout
dependencies and the actual four-axis request envelope inline. A checked Body
is charged directly under common evaluator rules; an opaque bulk intrinsic
without an eligible checked Body requires a compact deterministic resource
recipe owned by the exact versioned intrinsic registration. The execution
retains the intrinsic identity and request inputs rather than duplicating the
recipe. The consumer computes exact work and peak incremental intrinsic
scratch/value storage before execution; common rules account for ordinary
slots, entered frames and result encoding. Its materialized result is a
separate frozen value. The approved
24-byte `FrozenValueNode` and 12-byte
child row retain typed tree structure, including nominal union case identity.
Predicate occurrences form ordered trees, and dependency families remain
separate with their own owners. Canonical duplicate-free flat footprint ranges
and recursive dependency validation are approved; 16-byte PredicateNode and
8-byte operand-row shapes and exclusive operand-range ownership are approved;
node/operand-kind values, typed target schemas and per-kind operand roles and
arities, remaining PredicateNode operand target identities and operation
identity encoding, resource-recipe schema and complete Feature registration
remain gates.
Optional attributes and names are not a fallback.

<a id="cg-008"></a>
## CG-008: Static initialization

**Accepted logical behavior and assigned shared proof/value sections are
integrated; static-storage metadata and runtime integration remain blocked on
their exact row schemas, typed references and runtime ABI.**
[Static source rules](../specs/grammar-spec.md#31-source-file-structure-and-declarations)
and the [logical records](../specs/representation/logical-records.md)
govern storage and initialization. The
[required static-initialization cases](../specs/conformance/source-static-initialization.md)
exercise the accepted source, failure and lifetime behavior.
[Metadata](../specs/il-metadata.md) and the registry define assigned shared
sections and capability identities; they do not yet assign static
storage sections or a static-initialization Feature. Preserve typed
field/group/lazy-unit identities and a dedicated imported `StaticDependency`
for the exact imported `StaticField` plus complete owner substitution, shared
across use sites. D152 requires it to retain the shared Import/provider
binding, exact provider field and group, applicable substitution, and trigger
and effect contract identities. It must not duplicate provider resolution or
copy contract snapshots. Its physical fields and reference encoding remain
open. Static-field provider selection extends the shared Import/Export
identity path through a static-field `DefId` kind. Recognized attributes bind
exact registered Core definitions and the closed trigger enum/cases; names
and underlying values alone do not select behavior. Static section/DefId/
Contract-clause assignments, remaining row schemas, manifest closure,
compatibility checks, remapping and runtime ABI remain gates.
Method-owned initialization roles and demands use the existing `Contract`
ranges. The approved `InitializationRole` candidate is one 20-byte clause
with tags `1=eager initializer`, `2=lazy initializer`,
`3=authored static constructor`, `4=eager-phase helper`,
`5=lazy-phase helper`; each method has at most one role. The approved
16-byte `InitializationDemand` clause carries one closed eager-group
demand, with at most one per resolved group. Approved candidate records also
include `StaticField` flags `0=not writable after initialization`,
`1=writable after initialization`, a 12-byte `StaticAccess` with target tags
`1=local StaticField`, `2=imported StaticDependency` and authority tags
`1=ordinary completion demand`, `2=initialization-phase authority`. Exact
physical row schemas, section/DefId/Contract clause IDs, reference encodings
and remapping remain open; in particular, the superseded 8-byte
`StaticDependency` candidate is not an assigned schema.
Unsafe single-unit recovery uses one registered runtime-owned call, not a
dedicated Body operation. Its logical operand is a closed tagged exact-unit
reference distinguishing eager groups from lazy fields; exact callable and
signature identity, physical reference fields/encoding and runtime ABI remain
open.

**Current/evidence:** the [module inventory](modules.md) does not establish
complete retained closed-type static groups, phase authority, recovery and
unload support. Their exact implementation coverage is an audit question,
not a reproduced universal failure or permission to weaken accepted rules.

**Routes/gate:** establish actual current support first, then preserve
closed-type/group identity, partial-declaration order, eager and independently
demanded lazy units, primary constructor/ownership choice, effects, managed
and cleanup-bearing values, sharing, reentrancy/cycles,
failed-initialization state/recovery and cleanup/code lifetime. Imported
dependencies bind the exact provider field/group and preserve trigger,
access, effect, compatibility and remapping obligations; they are not
excluded by a single-module profile. Competing permitted callers wait for the
unit outcome, while reentrant/cyclic demand fails without exposing unfinished
state. Exercise independent consumers, generic closed types, partial order,
import/replacement and malformed group/field references. Distinguish the
initializer's actual escaping event from fresh waiting/later
InitializationFailedException events: do not copy the original payload, add
an implicit shared cause or retry automatically. A lazy failure after eager
group completion fails only that lazy unit. An explicit unsafe retry is
permitted only for a failed unit after cleanup, with exclusive authority and
valid dependencies; catching a failure does not reset it. No separate
suppression or mark-complete operation is selected. Do not overload FieldDef
instance offsets or infer static semantics from a method name. No new draft
version selector is automatically required; a bounded StaticInitProfile is
not part of the accepted design.

<a id="cg-009"></a>
## CG-009: Owning fields and partial retirement

**Ready; wider coverage must be established.**
[Owning-field contracts](../specs/language/ownership-and-cleanup.md) distinguish
complete values, structural partial move/repair, authored cleanup and
reverse-declaration versus reverse-acquisition retirement.

**Current/evidence:** [native support](native.md), [owner/view support](view-and-owner-profiles.md),
[OwnershipTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/OwnershipTests.cs)
and [native origin cases](../../tests/Terrum.CodeAnalysis.Tests/Native/Graphs/NativeGraphOriginTests.cs)
cover bounded owners/cleanup/origin. They do not by themselves establish all
accepted partial-field, failure and crossing combinations; no new universal
runtime defect is asserted.

**Routes/gate:** audit and preserve exact live fields, original responsibility,
repair/retirement order and complete-receiver restrictions in binding, shared
rewrites, verification and lowering. Test failed construction at every
acquisition, moved children, guarded joins, replacement failure and cleanup
that diverges. Never invoke incomplete-parent authored Cleanup, replay a
retired child, or pass partial T as a whole value. Independent publication
depends on CG-002/003 and complete ownership/property codecs.

<a id="cg-010"></a>
## CG-010: Portable proof and constant-evaluation floor

**Ready for the accepted source floor.**
[Proof/evaluation](../specs/language/proof-and-constant-evaluation.md),
[published effects](../specs/language/published-predicates-and-effects.md) and
their [required cases](../specs/conformance/source-proof-and-constant-evaluation.md)
govern semantics and reproducible accounting. The
[logical record direction](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
selects typed evaluation-form clauses, predicate graphs plus separate
read-footprint dependencies, constant-execution records and typed frozen values;
remaining predicate/resource fields and typed-reference encodings remain open.
Sections `0x0037..0x004B`, `Contract` clauses `0x000A..0x000B`, Feature IDs
`9..13` at revision 1, DefId kind `0x21`, and TargetProfile `(1,1)` are
assigned; their complete registrations and independent exchange support are
not complete. Feature prerequisites are `11 -> 9`, `10 -> 11`, and
`12 -> 10,11`; Feature 13 has no unconditional Feature prerequisite.
The approved direction additionally gives each predicate one typed node row
per evaluated expression occurrence with explicit ordered child-edge rows and
one separate actual read-footprint record referenced by its graph, whose
approved 8-byte row contains root and footprint references. Each footprint
has an approved 16-byte row with two first/count ranges; it assigns
each required request one `ConstantExecution` owner
for its selected checked Body/intrinsic, substitution, inputs, executed and
per-query layout dependencies, and four inline request ceilings. Charge a
checked Body directly; require a registration-owned compact deterministic
recipe only for an opaque bulk intrinsic without an eligible checked Body.
Keep the materialized result separate. `FrozenValueNode`
and its child row have approved 24-byte and
12-byte candidate shapes; values are strict typed trees, including nominal
union-case identities. Node/operand-kind values, typed target schemas and
per-kind operand roles/arities, remaining PredicateNode operand target
schemas/identities,
PredicateCapture row fields, tag values, exact typed-fact references and any
role-specific field schemas remain gates; graph-local capture ordinals, the
PredicateCapture family, separation of capture identity from typed facts, and
the value, checked-reference and generic-type-argument capture roles are
approved. Canonical capture-row ordering by owning graph identity and capture
ordinal is approved; re-sort after remapping. Reuse existing typed records
when they fully represent required facts; encode only a missing role-specific
fact in role-selected PredicateCapture row fields, without a separate
capture-payload record family or duplicate semantic payloads. Exact
typed-reference remapping details remain gates.
Predicate literal operands reuse the existing `FrozenValueNode` root and its
typed-value/Blob contracts; exact operand-kind assignments and the remaining
PredicateNode target schemas and operation identity encoding remain gates.
An operand denoting a read root
references the matching root-entry row in its graph's read footprint; exact
operand-kind assignments and remaining typed-reference details remain gates.
Selected-call operands reuse same-footprint direct dependency entries; generic
call-site substitutions remain separate typed facts. Field/property operands
use selected declaration identities, preserve complete generic owner
substitutions, and leave placement to `Layout`; property getters remain calls.
Generic union-case operands preserve both `UnionCaseDef` and owner TypeSpec.
Other typed-reference encodings, resource fields and complete Feature
registration remain gates; canonical PredicateNode
row ordering, the MethodDef identity tuple (including complete generic
domains/constraints and imported producer/export identity), and one
`ImmutableData` declaration-identity family are approved. Its 12-byte
candidate row contains a symbolic kind, zero flags/reserved fields, a typed
declaration reference selected by kind and an optional complete owner
TypeSpec reference; semantic identity uses kind, declaration identity and
owner substitution rather than row numbers. Closed kind tags are
`1=ResolvedConstant` and `2=PublishedImmutableRegion`; sections
`0x0037..0x004B`, Contract clauses `0x000A..0x000B` and Features `9..13`
are assigned. Features 9..12 retain incomplete registration/support gates;
Feature 13 is registered for layout queries and TypeIdentity, but still
depends on exact identity, target, value and witness support where used.
Typed-reference mappings remain open. The family references existing value and static-initialization records rather than copying
snapshots;
one-clause-per-graph ownership, structural-kind versus selected-identity
separation, and the coarse value/subject, access/projection,
operation, call, test/pattern, Boolean-composition and construction families
with behavior-specific variants are approved; semantic tuple comparison and
the absence of serialized sort-key fields are approved.

**Current/evidence:** `Binder.ConstEval` retains private depth 64/work 4096
limits and admits only bool/char16/int32/uint32/int64/uint64 evaluator values.
`ConstEvalTests` tests those private boundaries. The
[implementation inventory](../implementation.md) explicitly does not claim
the complete portable floor.

**Routes/gate:** implement selected proof facts, executed-path ordinary/pure/
constant forms, actual failure/effects and the minimum admitted value domain.
Qualify exact and one-over work/depth/live-state/payload boundaries, repeated
root operations, early branch refusal, invalid values and absence of runtime
fallback. Separate source evaluation budgets from decoder/audit budgets.
Independent program/value exchange needs CG-001/006/007 and reviewed codecs.
Reference/public-contract artifacts must also retain required `ConstEval`
programs and resource/dependency facts rather than stubs; public `Blittable`
commitments require field/layout facts for consumer validation. These physical
closures remain gates, not reasons to weaken the accepted logical floor.
Typed frozen values derive ordinary field placement from validated target
`Layout` records; only genuine address relocations need explicit fixups, and
target materialization does not replay completed source evaluation. The
selected value carrier is one recursive typed-node family for the admitted
acyclic value domain, not opaque host bytes or a managed identity graph.
Keep callable allowed-root contracts separate from per-observation footprints,
layout-query dependencies owned by each `ConstantExecution`, ordinary
backing/retention `Dependency`, and static-provider `StaticDependency`; do not
globally intern target-layout facts or combine them into a general dependency
graph.

<a id="cg-011"></a>
## CG-011: Numeric and selected library contracts

**Ready for accepted members; candidate APIs remain blocked on approval.**
The [adopted inventory](../specs/libraries/adopted-inventory.md),
[numeric groups](../specs/language/numeric-capabilities.md),
[selection](../specs/language/numeric-selection.md) and
[primitive operators](../specs/language/primitive-operators.md) are the owners.
The broad [Core candidate inventory](../proposals/core-library-reference.md)
does not approve every listed API.

**Current/evidence:** [library](libraries.md), [conversion](conversion-profiles.md)
and [native](native.md) inventories cover bounded scalar/witness paths, not
all 26 scalars, mathematical programs, library families or independently
registered identities.

**Routes/gate:** map each selected member to its exact Core declaration,
complete signature, constraints, effects, failure, validity and actual
runtime/pure/constant implementation. Cover widths/signedness, checked
intermediate failure, shift-count domains, heterogeneous results,
signed zero, NaNs, infinity and nominal witnesses. The accepted instruction
matrix assigns all twelve integer primitives, five binary floating formats,
and three decimal formats to their applicable operation families; this does
not establish implementation or authorize conversions beyond their explicit
contracts. Integer `Convert`/`CheckedConvert` covers all twelve integer
identities; `IntToFloat`, `FloatConvert`, `Char16ToFloat` and `FloatToInteger`
remain limited to the exact pairs in the instruction contract. Other
source-level conversions need an exact selected callable or another assigned
encoding; a type code does not supply one. Do not infer semantic laws from a
name or layout, erase fallback identities, or claim all fixed-width integers
from a four-integer implementation. Migrate the source compiler, constant
evaluation, diagnostics and serialization to the approved Development/Debug
checked and Release wrapping defaults for ordinary integer add/subtract/
multiply/negate. Resolve the profile before checking/evaluation/serialization;
preserve explicit operation policies in imported IL and test all twelve
integer identities, including signed minima and unsigned negation. This is
not current implementation support. Additional mathematical, container, I/O
and application APIs need their explicit candidate decisions.

<a id="cg-012"></a>
## CG-012: Quiet arithmetic NaN qualification

**Bounded implementation; evidence required.** Accepted
[floating arithmetic](../specs/language/primitive-operators.md#floating-arithmetic-and-comparisons)
requires quiet processing NaNs, while
[constant execution](../specs/language/floating-environment.md) selects its
deterministic canonical recipe. Runtime arithmetic has no fixed sign/payload
requirement.

**Current/evidence:** `FloatingArithmeticModuleTests`' NaN oracle accepts
any NaN class, including signaling patterns; inspection established an
oracle gap, not an observed runtime violation.

**Routes/gate:** strengthen representation-sensitive runtime checks for the
required quiet bit without demanding a fixed runtime sign/payload. Separately
check canonical constant results, exact copy/selection and sign-only negation
of signaling/quiet payloads. Cover source, private dependency and selected
standard paths at the final relevant compiler inputs. A numeric IsNaN check
alone does not establish this requirement.

<a id="cg-013"></a>
## CG-013: Non-unit DoesNotReturn

**Ready; accepted applicability mismatch.** The
[source no-return rule](../specs/language/expressions-and-statements.md#no-return-contracts)
and [clause contract](../specs/representation/contract-clauses.md#4-doesnotreturn-and-actual-completion)
permit any admitted declared result type, retaining ordinary typing.

**Current/evidence:** `Binder` reports TRM3018 for non-void DoesNotReturn;
`SemanticVerifier` independently rejects the same combination.
[NoThrowTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoThrowTests.cs)
and [module cases](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NoThrowModuleTests.cs)
exercise the bounded declaration/flow machinery, not the complete rule.

**Routes/gate:** remove the void-only restriction consistently in binding,
reconstructed verification, signatures/contracts, imports/replacement and
source/TRRM/native flow. Test typed non-unit divergence/escaping failure,
false claims with a normal return, independent NoThrow, unreachable ill-typed
source and absence of a bottom/discard conversion. For a normal-returning
non-unit call, retain the typed result selector. A verified DoesNotReturn
CallSite instead uses result selector None for any declared result type and
terminates its Block without publishing a normal result or successor; its
complete signature remains in metadata. Compact call windows remain
normal-returning forms. Unsupported result representations remain unsupported.

<a id="cg-014"></a>
## CG-014: Signed-minimum bootstrap remainder

**Ready; accepted arithmetic mismatch.**
[Primitive arithmetic](../specs/language/primitive-operators.md) requires
signed minimum `% -1` to yield zero, distinct from minimum `/ -1` failure.

**Current/evidence:** `BootstrapInt32MainSource.terrum` and rejection cases in
`BootstrapInt32MainReturnComponentTests` retain the older remainder rejection.
This is static source/test evidence, not an executed reproduction.

**Routes/gate:** reconcile shared folding across direct Main, helpers, locals
and call arguments. Check all supported signed minima, zero divisors,
minimum division and intermediate overflow under both runtime policies.
Keep source failure identity/order and emitted results consistent; do not fix
only one literal or route.

<a id="cg-015"></a>
## CG-015: Scalar frontend record migration

**Blocked: prototype limits and projected records are not the selected
compiler architecture.** The current C#-hosted `ScalarFrontendHost` and
Terrum-authored producer use fixed v87/v88 buffers, a 16,384-unit prototype
grant bound, 32-function/64-node banks, and packed call metadata. The
user-selected direction is standalone C# and Terrum compilers with
whole-source process fallback, not a Terrum AST handed to the C# compiler.
In-memory counts/IDs use `int32` with nonnegative valid values (`-1` only for
absence); span lengths/offsets follow the `nint` contract. Size buffers from
validated counts. A metadata row bound applies only to an actual table row.

**Current/evidence:** `ScalarFrontendHost` still rejects fourth-child
index-plus-one values above 32 even though the prototype field accepts a
larger value; the current 64-node and other fixed banks are prototype
ceilings. These are not source, ABI, or adopted profile limits. The C#
`SemanticVerifier` also retains implementation ceilings of 1,024 functions,
256 variables, 64 arguments, and depth 128. Its `IndexedBody.Verify` path
shares one 100,000-visit counter across functions, unlike the selected
per-function verifier budget. None of these values is an accepted source
limit. The Terrum-authored source parser/emitter remains a bounded
implementation slice; the general C# process fallback is not yet
demonstrated by this component.

**Routes/gate:** replace fixed v87/v88 projection banks with the selected
count-sized representation or remove the projection path; preserve direct
argument identities, source ordering, initialization/effects, cleanup and
whole-source fallback. Move verifier budgets to cached per-function work
using published callee contracts; keep module-level facts transient and
separate from those budgets. Measure or remove fixed verifier ceilings;
retained safeguards must be justified resource budgets, not language or
profile limits. Track actual buffer/PE resource failures separately from
unsupported fast-path shapes. Test count/ID boundary values, metadata row
references, one-over-capacity and malformed projections, and ensure a
profile miss never makes otherwise valid source invalid. CG-014 remains the
separate signed-minimum remainder mismatch.

<a id="cg-016"></a>
## CG-016: Standalone compiler transition

**Architecture selected; implementation and synchronization remain
incomplete.** The C#-authored and Terrum-authored compilers are standalone
implementations maintained side-by-side while the language evolves. When the
Terrum compiler lacks a feature, it must invoke the C# compiler as a separate
process on original source. Do not pass a Terrum-produced AST or partial
semantic projection to the C# compiler. This fallback is transitional; the
long-term goal is a self-sustaining Terrum compiler, with the C# compiler
removed after GA.

**Current/evidence:** the current scalar frontend prototype emits v87/v88
projections that the C# host audits/verifies/emits; that path is not the
selected inter-compiler contract. The direct-source bootstrap tests are a
bounded Terrum-authored implementation slice, not evidence that a general
Terrum compiler or process fallback exists.

**Routes/gate:** implement source-level subprocess fallback without sharing
compiler-internal ASTs; keep observable language support in sync across both
implementations; test feature misses as a whole-source C# invocation, not a
partial handoff. Keep the direct-source prototype's limitations out of
language/spec support claims. Use a previous stable compiler to bootstrap
future Terrum-only features after the implementation reaches the required
milestones.

<a id="cg-017"></a>
## CG-017: Direct token spans and deferred returned views

**General source dependency contracts remain accepted.** Use
`Span<Token>` directly over a validated in-memory snapshot with `nint`
length/index semantics and full bounds checks. The user declined a
`TokenCursor` wrapper, a separate 64-token fast profile, and private
`.trm` Span<Token> persistence. Returned-view forwarding remains deferred
until a concrete same-compilation consumer exists.

**Current/evidence:** the repository still contains `TokenCursor` and private
`ModuleCompiler.TokenSpans` prototypes/tests. They are implementation evidence
only, not selected APIs or persistence schemas. No standard `.trrm`, public
span ABI, persisted capture or returned-span contract follows.

**Routes/gate:** keep span backing, access, owner/guard, and retirement facts
through direct calls, copies, joins and cleanup. Remove or isolate the
unselected cursor/persistence prototypes. Reopen returned-view work only for
a concrete consumer, preserving ordinary dependency and ownership contracts.

<a id="cg-018"></a>
## CG-018: Unselected source-grant prototypes

**Selected boundary:** compiler frontends consume original source text at the
compiler input boundary; JIT consumes semantic metadata. A source-file buffer
is not a runtime capability of persisted semantic modules. Isolated AST/CST
testing APIs do not imply a private semantic-type format or a source grant.
Compile-time text capture, if separately designed, produces a string value;
it does not retain a reference to the original source file. No `Host.Source`
semantic-module requirement, persisted scanner/frontend invocation profile,
native source-grant ABI, or standard `.trrm` source-grant record is selected.

**Current/evidence:** `NativeJit.ExecuteWithSourceGrant`,
`ExecuteWithScannerProfile` and `ExecuteWithScalarFrontendProfile` remain
private prototype paths. Their tests and current code are implementation
evidence only, not compiler input/JIT contracts. The scalar projection is not
the C# fallback; that fallback invokes the C# compiler process on original
source. `NativeSourceGrantLibrary` remains experimental and adds no native
calling convention.

**Disposition:** these profiles are superseded by the standalone compiler
boundary and are not conformance requirements. Keep any retained implementation
clearly isolated as prototype/test support; do not expose or infer a whole-source
runtime reference, hidden native parameter, source-dependent package, or
persisted-AOT feature. Any distinct future need requires a new concrete
consumer and contract review rather than promotion of these profiles.

<a id="cg-020"></a>
## CG-020: CAS and concurrent execution

**Accepted semantics; ordinary method calls are the selected representation,
while target/runtime implementation remains unverified.**
[Memory ordering](../specs/memory-model.md) and
[concurrent executions](../specs/runtime/concurrent-executions.md) own the
accepted order matrix, read sources, failure and full-boundary effects.
Compare-exchange uses the selected method identity through the ordinary
MethodMap/Call path; no dedicated CAS opcode or Feature is assigned. Source
API signatures remain within the separate Core-library review.

**Current/evidence:** the current registry assigns no CAS operation or
capability. Implementation coverage for the selected methods/order contracts
and broader concurrent execution remains unverified.

**Routes/gate:** retain both compile-time-known orders,
actual location/generation and defined bitwise comparison representation.
Check all 25 success/failure pairs, nonconstant orders, failed-read acquire,
SC/full effects with discarded results, checked-RMW overflow and exact
representation/width support. Qualify method binding and runtime/helper
refinement, publication, quiescence/reuse and grounded causality; equal size,
padding bytes, numeric equality or one x64 instruction do not establish the
contract.

<a id="cg-021"></a>
## CG-021: Tasks, iteration and generated producers

**Ready for accepted logical rules; physical/runtime completion blocked.**
[Task responsibility](../specs/language/task-responsibility.md),
[value iteration](../specs/language/iteration.md),
[async iteration](../specs/language/async-iteration.md),
[lending](../specs/language/lending-iteration.md),
[async execution](../specs/language/async-execution.md) and
[generated producers](../specs/language/generated-producers.md) are the owners.

**Current/evidence:** the [library inventory](libraries.md) and
[logical representation inventory](../specs/representation/logical-records.md)
do not establish complete compiler/runtime or persistent task/frame/
phase/loan support. A candidate `Faulted(Error)`/Canceled catalog is not a
replacement for selected events, causes, reservations and responsibility.

**Routes/gate:** first map actual support, then preserve complete nominal
roles, creation/invocation/completion/observation identities, actual
destination versions, capture and frame/code origin, one outstanding advance,
pending reservation and every-exit settlement. Preserve `Task<_>` existential
views per value, including common-only operations and completion-only
consumption that settles hidden payload cleanup without erasure to
`Task<void>`. Cover never-advanced/failed close, value versus loan outputs,
cancellation without settlement, forwarding, early abandonment and
cleanup/failure ordering. CG-002/003/006/009 supply shared mechanisms;
pointer-sized handles and optional attributes do not provide missing
independent codecs or hidden blocking.
Failed acquisition leaves no caller cursor to Close; any complete cursor
formed privately still has its actual holder's close/retirement obligation.

<a id="cg-022"></a>
## CG-022: Roots, handoff, code and failure-boundary qualification

**Bounded implementation; wider profiles require evidence/design.**
[Sharing/handoff](../specs/runtime/sharing-and-handoff.md),
[deployment](../specs/targets/windows-x64-deployment.md) and the separately
selected [failure bridge](../specs/runtime/foreign-failure-bridge.md)
preserve actual root, authority, allocator, code and runtime-domain lifetimes.

**Current/evidence:** [JIT](jit.md), [graphs](native-graph.md) and
[native support](native.md) describe bounded private heaps, roots, unwind/
registration and provider origins. They do not certify arbitrary shared
managed/view/task crossings or actual Terrum failure-packet transport.
The [bridge cases](../specs/conformance/target-windows-x64-failure-bridge.md)
explicitly separate native wire fixtures from Terrum execution.

**Routes/gate:** qualify actual roots through calls/cleanup, code/helper/
allocator edges absent from the IAT, full-point unwind/probing, control-state
restoration, registration and failed deregistration, quiescence/unload and
cross-image responsible release. Wider handoff needs real capability/token/
generation/return support. Bridge implementation additionally needs local
typed mapping/dispatch/capture, affine packet ownership, nested callback
containment and complete retained codecs; no shared foreign unwinder,
pointer-header probing or success-shaped capture failure.

<a id="cg-023"></a>
## CG-023: CLI and source-input conformance

**Ready for the selected contracts.** The
[`terrum.cli/1` protocol](../specs/cli-protocol.md) and
[source-input boundary](../specs/language/source-input.md) are accepted.

**Current/evidence:** the [CLI inventory](cli-protocol.md) distinguishes
experimental schemas and raw execution-worker statuses from the selected
supervisor envelope. `Program.Compilation` opts into source-authored native
imports; it is not a compiler-owned API catalog.

**Routes/gate:** establish supported operation/option/input/output/error
shapes, stable machine identities and separate program result/output from
supervisor status. Qualify actual malformed-byte/encoding boundaries,
snapshot/coordinate identity, no fabricated decoded prefix, multi-source
diagnostics, explicit cancellation/deadlines and output atomicity. Invoke
the built CLI rather than mixing `dotnet run` messages into protocol data.
A worker timeout or raw program status cannot be silently relabeled as a
successful supervisor response.

<a id="cg-024"></a>
## CG-024: Representation extensions and resource accounting

**Metadata/type-reference, masked Body-map and register query-schema
decisions integrated; producer/consumer implementation and qualification
remain blocked on CG-001. Numerical portable resource minima and
exception-region physical carriers remain unassigned.**
[Canonicality](../specs/representation/encoding-canonicality.md),
[reader trust](../specs/representation/reader-trust.md) and
[resource accounting](../specs/representation/resource-accounting.md),
[Body metadata](../specs/il-metadata.md) and the
[instruction contract](../specs/il-instruction-set.md) define the accepted
schema. It assigns the `17 + 4*popcount(mapMask)` register-stream prefix,
five optional Body maps including four-byte FieldMap tokens, global FieldPath
and target-layout field-reference sections, zero-based `u16` local indexes
with `u32` counts, `DE` query/TypeIdentity controls, BlockEntryState carrier
2, instruction/record layouts, F0/F1/F2/F3 TypeMap tokens and TypeSpecRef
fields, plus the accepted integer, binary-floating and decimal type matrices.

**Current/evidence:** development fixture/reader behavior is not independent
proof of the selected schema. The revised vectors are specification evidence,
not compiler implementation or runtime qualification. Numerical portable
resource minima remain unassigned, not invented defaults. A lower fixed-width
ID is not smaller on disk.

**Routes/gate:** CG-001 must preserve ordinary equivalent noncanonical
admission versus strict writers/scoped audits, retained root budgets,
checked preflight, structural versus semantic work, depth, payload/state
lifetime and explicit exhaustion. Calibrate rather than guess minima. Migrate
every producer/consumer to rebuild maps, records, code offsets, indexes and
SourceMap step roles; validate token nesting/modifiers and exact type/value
identity. Rebuild masks/counts for every stream Body; traverse FieldPath rows
under artifact/root resource budgets and preserve ordered `004A` ranges.
Exercise `0xFFFF` and 65,536/65,537 boundaries, all map masks,
empty-present maps, FieldPath rows, noncanonical maps,
malformed controls/records and operation/type combinations with independent
vectors. No decoder alias or new pre-release format pair is automatic.

<a id="cg-025"></a>
## CG-025: Generic native regression and qualification evidence

**Bounded implementation; evidence required.** The
[native contract](../specs/native.md) owns the generic/authored-library
boundary; this gate tracks implementation and qualification evidence.

**Current/evidence:** active generic native tests are distinct from excluded
legacy prototypes. Absence of API-specific recognizers does not establish
every producer/consumer, backing, adaptation or lowering case.

**Routes/gate:** after CG-004 or other relevant native changes, exercise
unrelated synthetic vtables, function pointers, layouts, outputs and failure
paths through source, persisted dependencies, reconstruction and lowering.
Include nested traversal, address-only imports, aggregate scratch sizing,
callback/code origin, status/version correlation and malformed metadata.

<a id="cg-026"></a>
## CG-026: Source vocabulary, grammar and binding convergence

**Ready for accepted source rules; route coverage must be established.**
The [lexical contract](../specs/lexical-grammar.md),
[source grammar](../specs/grammar-spec.md),
[names](../specs/language/names-and-binding.md),
[types](../specs/language/types-and-normalization.md) and
[calls](../specs/language/calls-and-inference.md) own admission. Proposal-only
families, annotations and APIs do not become source forms through examples.

**Current/evidence:** the [implementation inventory](../implementation.md)
distinguishes lossless syntax APIs, bounded semantic/native support and
experimental authoring. The [scanner](../scanner.md) and
[frontend inventory](bootstrap-frontend-composition.md) describe narrower
component protocols, not complete source conformance. This is a coverage
handoff, not a claim that every rule below currently fails.

**Routes/gate:** map accepted positive/negative cases to actual lexer, parser,
binder and public compiler entry points after CG-023 establishes exact source
snapshots. Cover keyword/escape and Unicode recognition; signed numeric
boundaries and raw/comment/documentation fences; declaration visibility and
modifier order; exact attribute target/argument schemas; complete qualified
type/alias normalization; finite call inference, source-order named argument
staging, defaults and params; selected pattern/refinement forms; and unit versus
explicit non-unit result discard. Compose the relevant CG-002/006/007/010/013/021
gates rather than adding a second semantic rule set.

For registered queries, connect the accepted query productions to their
public binder/compiler entry points and the assigned representation cases.
Preserve the distinctions between source query acceptance, TypeIdentity
evidence, target-layout support and Count witness availability; a parser or
display path alone does not establish `DE` emission or runtime support.

Exercise hosted and Terrum-authored routes only for their declared scope,
with explicit unsupported diagnostics outside it. Parser acceptance is not
semantic validity or emission support, and parity with a bounded older
frontend is not proof of the accepted grammar. Do not weaken the language,
retain legacy source forms automatically or silently reinterpret an
unpublished profile to make a subset appear conforming.

<a id="cg-027"></a>
## CG-027: Normal/GC backing, roots, pin reborrows and Cleanup

**Accepted source direction; implementation and independent exchange remain
blocked on complete type, runtime and carrier selections.** Canonical source
contracts are in [language](../specs/language.md),
[complete types](../specs/language/types-and-normalization.md),
[references](../specs/language/references.md), [lifetimes](../specs/lifetimes.md),
[conversions](../specs/conversions.md) and
[ownership/Cleanup](../specs/language/ownership-and-cleanup.md). Focused
positive, negative and cross-feature cases are in
[storage and GC conformance](../specs/conformance/source-storage-and-gc.md).

**Current/evidence:** [native implementation profiles](native.md) document
private `native-18` through `native-20` managed-node/class tracing, optional
handles, cycles and explicit collection. Those bounded profiles use
image-private managed storage and do not implement the accepted normal-versus-
GC distinction, source `gc` types/construction, `gc ref<T>` exact-place roots,
stable `ref *root` pin reborrows, or a public managed-handle ABI. The profiles
explicitly exclude interior pointers and pinning; source-defined classes use
compiler-owned `TryCreate` factories, while `new C(...)` remains unsupported.
The existing Cleanup prototype is for unique values and does not implement
GC-triggered object Cleanup. These are scoped implementation facts, not a
reason to weaken the accepted contracts or infer conformance from the private
collector.

**Routes/gate:** preserve normal `C`/`box<T>` stable shared backing separately
from GC-backed `gc C`, `gc box<T>` and `gc ref<T>`. Carry exact backing
identity, retains, roots, pin support, resource/guard dependencies and the
single backing-specific Cleanup obligation through binding, shared bound-tree
traversal/rewriting, mandatory verification, persistence/dependency binding,
JIT/AOT lowering and runtime retirement. A GC root must identify an exact
initialized place; `ref *root` stabilizes only that place, while explicit
`C(gcObject)` pins the whole object and copies share support. Raw pointers
remain non-owning/non-rooting and cannot outlive their supporting dependency.
Normal strong cycles remain uncollected; GC Cleanup is once-only,
non-resurrecting, before reclamation and on an unspecified cleanup thread,
with thread-neutral/transferable transitive obligations.

The former `pin` TypeSpec qualifier bit is retired/reserved and must not be
reused for GC. No GC TypeSpec form/code, root/retain/pin/retirement record,
Feature ID, collector/provider profile or ABI/carrier layout is assigned by
this integration. Select those physical/runtime contracts explicitly before
producer/consumer support; never infer them from private formats or equal
version bytes. Use unrelated synthetic classes/boxes and root/pin operations,
including bounded references that outlive a handle local but not a slot or
owner/guard endpoint, optional-root presence checks, failed acquisition with
no published root, early-release cleanup boundaries, and negative
sibling-place, stale-pointer and resource-lifetime cases. Include strong
cycles crossing normal and GC backing without assuming they are collectible;
weak handles, ownership domains and any joint collection policy remain
separate design work. Verify exact source and metadata identities through
separate compilation and both JIT/AOT paths; distinguish source binding,
emission, runtime behavior and profile admission.
