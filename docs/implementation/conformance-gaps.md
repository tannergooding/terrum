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
development-schema fixtures. The C# Module row reader/writer and artifact
framer use the assigned 48-byte row and validate governing edition 1. The
standard Feature reader/writer now use exact assigned pairs, accept unordered
ordinary lists with identical duplicates, canonicalize writer output, and
derive prerequisite closure per Module. The executable compiler still routes
through separate development Feature assignments; Body codecs and
private/native/output experiments retain other assignments. Registry-owned
TypeDef/FieldDef/Layout/LayoutField/TargetProfile/GenericParam/Constraint
shapes are now used by the C# readers/writers, artifact index and structural
validator. The bounded Blittable path emits complete default and Windows x64
Layout variants with ordered instance-field lists, then reconstructs supported
Sequential/Transparent layouts without serializing physical size or offsets.
GenericParam domains and assigned Constraint kinds are structurally checked;
the current TypeSpecRef slice validates row/F2/F3 forms, nested operands,
existential and ref-init rows, and variable form-0x05 payload framing. It
applies complete references to FieldDef, MethodSig, Param, PropertyDef,
GenericParam defaults/constant types and Constraint kind 1, and retires the
TypeArg table and section `0x0012` from standard parsing. Generic-aware
argument arity/kind/ConstantSource bounds, remaining registered fields,
dependency/remapping and Body routes, and the provisional associated-relation
payload remain unfinished. The selected TypeSpecRef behavior does not establish
current-schema conformance. See
[ModuleCompiler](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs),
[Feature admission](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs),
[Body fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmBodyExampleTests.cs)
and the [module inventory](modules.md). Existing test success would not establish
current-registry decoding.
A separate internal `CopyOriginDraft` profile now admits candidate Feature
15/1 and contract-only nonpublic TypeDef/FieldDef identities; FieldDef
Import/Export additionally requires Feature 13/1. Standard validation does
not select this profile, and Contract 000D decoding/writing, summary binding,
producer routes and current-schema conformance remain unfinished.

Selected Body access now decodes register kind-01 framing, optional-map
extents, fixed Blocks, assigned typed-record payload shapes, instruction
boundaries and block terminators; compact descriptor shapes are also checked.
This remains selective access rather than artifact-wide Body-content
preflight. The C# structural validator indexes Body table ranges, and callers
must use `TrRmBodyTableReader` to validate a selected implementation. A
method-aware selected-Body map validator now checks HomeMap structure and
signature ordinal bounds, TypeMap references, and provisionally rejects F0/F1
at selected use under the unapproved
[TypeMap pseudo-retirement proposal](../proposals/body-typemap-pseudo-type-retirement.md).
It also validates MethodMap/FieldMap row kinds and bounds, top-level
direct/Import module ownership, selected FieldPath step ownership, and
ConstantMap TypeSpecRef/ConstantSource row bounds. Map-reachable TypeSpec
references are traversed under the selected Module, including constructed
type arguments, MethodSig result/receiver/parameter types, FieldDef types and
FieldPath roots/steps.
The active reader tests also consume a hand-encoded kind-01 Body vector with a
selected TypeMap token; mutating its local selector out of range rejects
through selected Body validation. Broader registry-linked Body vectors remain
part of the integration gate.
Receiver and formal HomeMap types are compared with their MethodSig types
across direct/Import TypeDefs, TypeSpec rows, F2/F3 spellings, and indexed
family terms; nested wrappers and form-0x05 type arguments are compared
recursively while preserving exact TypeDef, Import and GenericParam identities.
Owner-argument substitution and callee/provider contract equality remain
incomplete. Selected ConstantMap entries, Body TypeMap arguments and
constructed constant arguments reached through declaration-owned TypeSpecRefs
currently require exact scalar type/value binding under provisional Feature
11/1; ownerless contexts and other ConstantSource tags remain unsupported.
Selected E1 MethodMap calls also allow an R15 result only for an exact unit
target. Full record and instruction semantics,
descriptor eligibility, canonical writing, remapping, re-emission and
execution remain unfinished. The selected Body reader now validates short
control-flow targets and selected conditional-edge, switch, requirement and
Throw records, plus zero operands for fail-fast/unreachable and the short Home
return index. Tiny/short Home, Type, Constant and Method map indexes and the
`61`/`90`/`91`/`92` typed-record references are also checked for local bounds
and expected record kind. Long `C0..C2` selectors and `C3..C5` predicate
controls are checked against their assigned domains. `C0..C2` common-family
payloads validate one/two-result and unary-source fillers, distinct dual
outputs and R14 output rejection; `C3..C6`/`CB..CC` reject R14 destinations,
and assigned `CC` is admitted as a four-byte operation. Long
`C7..CA` controls and payloads enforce the domain-specific absent unary
operands and the 182 valid patterns, including outer-negation order. Input/
result type relationships and recipe arithmetic/failure semantics remain
unverified. Long
`D0..DE`/`E0..E8`/`EB` forms validate map, Block and record references,
selected record kinds, reserved controls and query-kind routing. `B4`/`E8`
bind Throw record kind `09`; `B6`/`EA` remain rejected without the unassigned
region carrier.
The reader checks the fixed fields for short Return, UnitReturn and PlaceRetire
and now applies the assigned D0..D3 conversion control order: low-nibble source,
high-nibble destination. R14/R15 remain structurally available as source
constants, and R14 is rejected as the destination. Other per-operation source
roles remain incomplete. E1 bound calls now permit R15 only for an exact unit
result; other per-operation R15 discard eligibility remains unverified.
Place/window registers are restricted to R0..R13 and paired-formal windows
are bounded. Register/type/effect semantics and remaining instruction-record
relationships are not established.

Artifact framing now takes an explicit standard or internal development
schema before table indexing and writing. Standard `TrRmArtifactIndex` uses
the registry-owned section assignments through `0x004B`; the development
executable route retains its old `0x0030..0x0033` meanings through explicit
writer, loader and compact-validation selection. No section-layout fallback
or byte-based schema detection is used. Standard SourceMap selector-1 slots
now use target section kind zero and zero-based `u16` Body blocks, with their
reserved word checked; the Terrum-authored directory and SourceMap validators
match those rules. Standard interface/constant/witness/native semantic readers,
all registry field binding, and executable current-schema production remain
incomplete, so correct framing is not a conformance claim. Standard
FieldPath and retained target-layout readers/writers now validate complete
root/subject TypeSpecRefs, direct FieldDef or expected-FieldDef Import
references, exact TargetProfile row bounds, and exclusive offset-field ranges.
They do not bind path steps through owner substitutions or compute/compare
target layouts.
Standard UnionCaseDef/UnionCaseField rows now validate their TypeDef and case
owners, exclusive payload-field ranges, ordered payload ordinals and complete
UnionCaseField TypeSpecRefs. Feature 9 remains registration-pending, so these
structural checks do not establish nominal-union source or runtime support.
PredicateGraph rows validate required node/footprint row bounds, and
PredicateReadFootprint rows validate empty/nonempty range shape, target bounds
and exclusive nonoverlap across footprints. PredicateCapture rows validate
graph-local contiguous ordinals, role-specific flags and complete captured
TypeSpecRefs. PredicateChildEdge rows validate typed references, graph-local
tree ownership, contiguous child ordinals independently of physical row order,
and arbitrary node/edge row permutations; the writer sorts edges canonically.
The general-predicate candidate also validates direct captured-TypeSpec scope
and requires each capture to have one positive defining pattern site. Other
root/dependency semantics, complete node typing/calls, canonical graph
identity ordering/remapping, and predicate evaluation remain incomplete.

**Routes/gate:** migrate the complete producer/reader/rewriter and executable
consumer set to the accepted register Body: implementation kinds `01..05`,
the `17 + 4*popcount(mapMask)` prefix and five optional maps,
`instructionEncoding=01`, the fixed Block table, typed-record offsets/data
and one-/two-/four-byte instruction partitions. Body-local indexes are
zero-based `u16` with `u32` counts through 65,536; global tokens remain
fixed-width. Implement `DE` kinds 1..5; FieldMap tokens; global FieldPath
section `004B`; target-layout field references in `004A`; TypeIdentity
carrier/comparison/conditional flow; and Feature 13 admission. Preserve exact
TypeSpec/Import/MethodDef identity, the TypeSpecRef row/F2/F3 union in every
complete type-valued field, and the source operation/type, effect, failure and
ownership contracts. The accepted registry still assigns F0/F1; their
provisional rejection is an explicitly unapproved schema divergence, not
current-schema conformance.

Independently decode the revised vectors; exercise every tiny tuple, short
and long control domain, register-role/reserved-field rejection, `0xFFFF`/
65,536/65,537 boundaries, Block/record extents, SourceMap recipe steps,
compact descriptors, the provisional F0/F1 selected-use rejection and its
unapproved schema boundary, place-handle BlockEntryState
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
The source verifier also derives provisional in-memory CopyOrigin summaries
for supported Copy results and fresh inline construction. It composes selected
declaration summaries without callee-body inspection. Native-pointer field
backing sets and unknown-state now follow those summaries: EntryValue is
snapshotted at argument evaluation, while EntryReferentValue is resolved at
Invoke. This preserves exact selected pointer-field provenance through fresh
construction, by-value forwarding and borrowed receivers, without narrowing
whole-source DependsOn/Captures bounds. Source summary candidates now use a
callee-first mandatory verification pass; each body is checked before its
summary can be consumed. If summary inference exceeds its supported budget,
ordinary full verification remains available with source summaries unavailable.
The [unapproved candidate-95 amendment](../proposals/reference-unique-constructor-field-provenance.md)
now provisionally extends fresh construction to unique inline roots while
keeping each mapped subtree Copy.
The source/JIT regression exercises that narrow direction; unique normal
results, private candidate-95 transport and Standard TRRM support remain
separate implementation gaps.
The current codecs do not persist summaries; bodyless providers and standard
transport remain unsupported under the unapproved
[field-provenance proposal](../proposals/reference-field-value-provenance.md).
Mutable receiver/reference and known pointer-write call effects invalidate
affected local origin facts conservatively; general field backing/capability
transfer beyond native-pointer field backing, other capability facts and
alias-place refinements remain unfinished.

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
switch and private `NoThrow` facts. Source `NoThrow` uses are now rejected
provisionally. The structured semantic-module reader/writer now selects only
provisional revision 100, which inherits revision 92's omission of the general
NoThrow field/tokens and persists direct-import aggregate outputs with their supported output-specific
dependency rows. It rejects unsupported general Dependency contracts rather
than dropping them; candidate revision 93 remains required for general
Captures/provenance persistence. Native graph packages and consumer sets now
select outer schema version 3, which removes `NoThrow`/`MayThrow` from
recorded import contracts while retaining directed DoesNotReturn and
caller-unsafe checks; exact readers reject older v2 artifacts instead of
normalizing away those promises. Focused NoThrow module/graph/set regression
selection passed 88/88 with zero skips. CheckedExceptions may unwind through
private `NativeModuleCompiler` producer/graph images into a runtime-policy-
matched generated caller; public `NativeEmitter.EmitLibrary` exports still
require an ephemeral verified `NoFailureProof` under `CheckedExceptions`.
The managed-class/graph plus public-export regression selection passed
114/114 with zero skips, including cross-image recovery and public-export
rejection. Remaining private semantic/native binding and proof boundaries
stay open. The unapproved
[direct-import provider-binding proposal](../proposals/native-output-direct-import-provider-binding.md)
is provisionally implemented with version-3 TNPK/TNSP/TNGP expectation tables
and provider-owned leaf thunks; package, set and graph AOT plus root-only graph
JIT exercise selected-provider output binding and foreign-descriptor
replacement rejection. These slices do not complete general revision-93
dependencies, standard register-Body output persistence, field projections or
the broader NoThrow migration.
The standard Effects
reader/writer now rejects retired bit `0x01`, and standard Cleanup no longer
depends on that bit. The unapproved
[`no-throw-retirement-and-proof-boundaries.md`](../proposals/no-throw-retirement-and-proof-boundaries.md)
proposal provisionally recommends retiring the general source/published
promise; this is distinct from the accepted standard wire-mask correction.
The checked-exceptions path remains a separate runtime-policy gap.
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
These do not yet implement the selected schema. A separate, unapproved F+S
prototype currently exercises initialized scalar Home storage addresses and
one direct NativeParameter call through source-free reconstruction, API JIT
and CLI AOT; it does not close this accepted-contract gap or approve the
provisional schema. Active
[function-pointer](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs)
and [vtable](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeVTableCallTests.cs)
cases are bounded implementation evidence, not approval.

The separate unapproved F selected-provider, multiple-Import and provider-
closure candidates are provisionally implemented. Exact-F preparation checks
every supplied Module, including disconnected Modules, and resolves every
MethodDef Import by its exact ModuleRef identity/requirements and key to a
public provider-owned NativeImport. It retains each edge's original Import,
ModuleRef, expectation and actual origin; root imports alone are substituted
for lowering. Mixed one-owned/imported Modules, three-edge chains, hybrid
provider co-location, diamonds, repeated ModuleRefs, multiple public keys for
one actual definition, 17-provider fan-in, self-edges and mutual cycles are
covered by public API tests;
provider Bodies are not merged or invoked. The checked plan revalidates the
complete retained closure before lowering. The Debug fast build and combined
NativeAddress/provider-CLI selection passed 144/144 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_08_52_36_net11.0.trx`,
including deep-provider rejection, duplicate/empty/non-first Modules, caller
buffer mutation after plan creation, duplicate mixed names, non-first address
targets, 17-provider fan-in, UTF-16 identity and ordinal case/normalization
checks, exact alternate ExportKey selection, compatible deep-body replacement,
chain/diamond/mutual-cycle CLI JIT/AOT, no-entry DLL export invocation, PE
import/export inspection and sentinel output preservation. Native input/
compiler hashes and execution results are recorded in
`artifacts\\log\\compiler-adoption-f-provider-closure-native-qualification.log`.
The source-free NativeAddress reader now finalizes and verifies its native
pointer-type table after reconstructing all direct NativeImports and the caller
Body. This preserves typed pointer formals introduced by an uncalled imported
declaration in a mixed owned/imported Module. A deep-provider regression
preserves `NativeParameter` extent `0xffffffff` and rejects a provider with an
incompatible extent. A four-Module acyclic Root→P→Q→R chain now also loads from
one artifact with Root last. A second ordering Q,Root,R,P re-encodes the
ModuleRef, Import, MethodDef, TypeSpec and Body-map references and preserves
the selected root and checked lowering plan. Unicode identity coverage now
also binds a supplementary scalar (`U+1F680`) exactly and rejects normalization
of `U+212B` to `U+00C5`. Corrupting the co-located chain's ModuleRef identity,
Import ExportKey, cross-module Body MethodDef token or function-pointer
TypeSpec anchor rejects during selected loading. Exact-F loading rejects
one-over 24-bit record counts for Module, ModuleRef, TypeSpec, MethodDef,
Import and Body sections. The shared Body reader/writer selection also covers
the 65,536-entry map boundary and one-over rejection; the provider matrix
retains its 17-Module fan-in case. The combined NativeAddress/provider CLI and
Body reader/writer selection passed 243/243 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_09_43_53_net11.0.trx`.
The normal analyzer build remains at its existing baseline: 709 errors and
353 normalized identities, with zero additions/removals against
`artifacts\\log\\compiler-adoption-final-debug-analyzers.log`; output:
`artifacts\\log\\compiler-adoption-f-provider-closure-analyzers-final13.log`.
This remains provisional, not formal approval or full Standard conformance.
The selected proposal introduces no separate closure work/depth cap; finite
graph validation remains subject to existing row/map and operation/backend
capacity and must fail explicitly on exhaustion. Broader source
production/re-emission, same-name expanded lowering,
general source production/
re-emission of non-target Imports and broader schema compositions remain
unsupported.

The original unapproved A1 fixed-array identity candidate was superseded for
integrated nominal-array use by an independently drafted A2 Copy-evidence
composition. Current A2 prototype code carries TypeSpec form `0x11`,
Module-owned `0x0012`, Feature `20/1`, TypeDef flag `0x20`/Feature `2/1`,
local scalar and Copy Sequential/Transparent Windows x64 geometry checks,
and structural Home/Register PlaceSelection plus counted EC RegisterPlace
validation. It rejects duplicate per-Module array layouts with structurally
equivalent TypeSpec identities. The direct selected-provider path resolves an
imported Copy TypeDef by its ModuleRef and ExportKey, compares its fields and
all Layout variants, derives target geometry, and retains/revalidates the
provider origin in the Standard native plan. The Debug fast A2/F+S/API/CLI/
provider-binding selection passed 127/127 with zero skips. Provider-owned
import chains now resolve each exact ModuleRef and ExportKey and validate
every intermediate Copy expectation against the selected provider; matching
and same-size/different-field chain cases, plus nested-array field identity
and same-layout nested replacement rejection, passed in the 27-test A2
fixed-array layout/provider selection. The follow-up selection passed
29/29, adding function-pointer anchor equivalence across relocated MethodDef
rows and rejection of a different anchor signature. These are metadata/provider checks,
not array runtime or full Body evidence. A2, its allocations and Copy bit
remain unapproved. The A2 `EmitStandard` path emits declaration-only local
Blittable Sequential/Transparent structs with `DeclaredCopy` `0x20` and
Feature `2/1` for checked ordinary non-unique/no-Cleanup declarations; unique
rows remain unmarked. It also emits TypeSpec `0x11` and Module-owned
FixedArrayLayout rows for scalar, unit and nested array fields in these
declarations with exact Feature `20/1` and `2/1` closure. Array Body rows and
operations are not produced. The type-only source-free branch reconstructs
local non-generic Copy TypeDefs and these field types, including acyclic arrays
whose elements are local nominal Copy structs. It resolves array/TypeDef layout
dependencies in order and revalidates the completed geometry. The executable
NativeAddress path still rejects nonempty TypeDef tables. The focused
`TrRmForeignInvocationAddressEmitterTests` class passed 36/36 with zero skips,
including the local nominal-element round trip. The public source-free
NativeAddress reader now consumes
A2 Section `0x0012` and TargetProfile rows for selected-Module scalar, unit,
direct-pointer, function-pointer and nested-array types; it rebuilds bound
array identities, checks target geometry and preserves wide counts. Its
synthetic consumer and combined TRRM array suites passed 151/151 with zero
skips. The executable NativeAddress path still does not bind external/imported
Copy provider origins. The exact A2 executable NativeAddress route now emits
positive-count scalar-array TypeMap/Layout and PlaceSelection/SemanticStore/EC
Body records from checked source. Its exact A2E successor also binds
`init [E; 0] value; value = [];` and `init mut` forms, verifies the vacant
whole-array transition, emits the Home PlaceSelection and nullary store with
canonical short `91`/`92` references when representable, and reconstructs and
byte-identically re-emits the operation. A2 still rejects the A2E record.
The focused A2E and related Body/array/vacancy selection passed 312/312 with
zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_12_50_47_net11.0.trx`. These
results are provisional implementation evidence, not approval of the A2E
proposal, provider Body remapping, or native execution. Provider replacement/
re-encoding and supervised JIT/AOT qualification remain open.
The [A2 source Copy identity proposal](../proposals/standard-trrm-source-copy-type-identity.md)
remains unapproved. Its local A2 `EmitStandard` export-map slice is
implemented provisionally: source-bound public ordinary Copy declarations
require explicit namespace/name selection and an exact caller-supplied key;
exported roots also require explicit keys for every local nominal child in
their field, fixed-array, or typed-pointer storage closure. There is no
name-derived default or alias. The full
`CreateStandardReference` type-key projection now selects nonempty explicit
TypeDef ExportKeys from one local type-only A2 Module, then materializes only
the selected roots and their required public nominal/fixed-array/typed-pointer
closure. Closure-
only declarations are not exposed through source name lookup. Projected types
retain the schema, artifact snapshot, Module requirements, TypeDef identity,
and selected/closure ExportKeys; Standard and private module producers reject
the projection rather than losing that origin. Separately supplied A2
providers now bind through the checked Copy/layout contract, and imported
closure types retain both actual provider identity and the original using-
Module Import/ModuleRef expectation. Direct field and fixed-array imports,
compatible TypeDef relocation, incompatible packing, source-name lookup and
snapshot immutability are covered. Provider-owned TypeDef Import chains now
project through separately supplied or co-located A2 type-only Modules,
retaining both provider and consumer expectations. Distinct A2 TypeDef Import
triples for the same expected TypeDef retain each ExportKey and Import
expectation; they bind only when all keys resolve to the same actual provider
TypeDef, while duplicate triples and split aliases reject. Distinct expected
views now remain separate expected stubs while their established alias keys are
cross-linked to the same actual provider definition. Type-only structural
re-encoding now rebinds against compatible providers whose TypeDef/FieldDef/
TypeSpec/Layout/LayoutField/FixedArrayLayout rows move. Independent regressions
reorder FieldDef owner blocks, Layout variants, FixedArrayLayout rows and
topologically valid TypeSpec leaf rows with their child references, then
exercise CreateStandardReference and LoadStandard/EmitStandard/rebind.
Separate and co-located provider Module graphs are covered. The
combined A2 provider/reference/layout selection passed 165/165 with zero skips
at `artifacts\\tst\\Debug\\tagoo_GOODING-ZEN4_2026-10-31_17_37_24_net11.0.trx`.
PlaceSelection/TypeMap/Body remapping and source-free Body execution remain
open. Type-only source
consumers now re-emit origin-backed TypeDef stubs, ModuleRefs, Imports,
imported TypeSpec uses and fixed-array recipes without losing them when no
local ExportKey map is supplied. Explicit local exports preserve the same
imports. Loading and re-emitting a type-only provider retains its local
ExportKey aliases, unpublished declarations and imported origin graph; a
replacement export map is rejected. Provider-owned alias keys survive two
consumer re-projection generations; compatible provider TypeDef relocation
succeeds; changed imported declarations, split alias replacements and
incompatible layouts reject. The writer checks retained declaration,
fixed-array and pointer-table snapshots;
local TypeDef and Import TypeSpec forms compare after resolving to the same
actual TypeDef origin. The combined A2 provider/reference/layout selection
passed 164/164 with zero
skips, including distinct-view and re-encoding cases.
General provider replacement,
complete remapping, initialization, and A2 indexed native/runtime lowering
remain open. Closed constructed array elements remain incomplete. The exact
uniqueness-amended A3 identity is
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1`; the prior
flag-free `...-3` identity is not accepted as an alias. Its provisional,
unapproved proposal adds A3-only TypeDef `DeclaredUnique=0x40`, exclusive
with Copy `0x20`, requires Feature `2/1` for either positive fact, and
requires Unique evidence for A3 Cleanup declarations. A2 remains unchanged.
The A3 consumer now performs closed actual substitution, domain and
qualification checks, nested field geometry/RootlessCopy validation, and
generic provider binding across exact A3 selections. Local layout now keys
recursive visits by normalized constructed identity: finite repeated generic
definitions succeed while direct same-instance inline cycles reject. Tests
also cover nested closed arrays, Transparent-over-void, pack/alignment,
zero extents and aggregate overflow. Provider-geometry cases bind matching
packed declarations and reject a selected provider with a different derived
alignment. A compatible replacement with a relocated provider TypeDef row
binds through the preserved export key. The validator compares formal
domains/defaults/constraints, Copy/Unique and Layout commitments, ordered
field templates and nested constructed identities; generic and Unique
dependencies cannot bind to A2 providers. Direct generic values and
unconstructed generic definitions/imports in Body TypeMaps reject. A
plan-level test confirms retained A3 bindings are revalidated before the
current no-`NativeImport` lowering boundary; it is not public source-free
provider execution evidence. The
combined A3 layout/provider/Body-map/TypeSpec/emission/CLI matrix passed
159/159 with zero skips at
`artifacts\\tst\\Debug\\tagoo_GOODING-ZEN4_2026-10-31_04_31_06_net11.0.trx`.
Its selected-schema CLI cases still exercise inherited nongeneric
NativeParameter operations only. A provisional implementation of the
[A3 failure-class companion proposal](../proposals/standard-trrm-a3-layout-failure-classes.md)
now adds the host exception/classifier boundary, exact-root exception markers,
post-handoff marker removal, selected-module resource context, and A3-only
CLI mapping. CLI regressions exercise invalid metadata, unsupported admission,
target overflow and work exhaustion through both build and run, including
status/code checks and retained-output preservation; A2 still reports
`TRM5001`/1. The root ledger is shared through Standard structural
loading, TypeSpec/Feature/Body validation, fixed-array table decoding, layout,
A3/A2 selected-provider checks and binding-plan revalidation. Inclusive
work/depth reservation, iterative child-state insertion, provider depth
context and constructed-identity payload charges are covered. A3 aggregate
depth sums now use nonwrapping `BigInteger` arithmetic before comparing against
the 128-frame limit; nested frames plus `ulong.MaxValue` through
`EnterDepthContext`, `CheckDepthFromCurrent` and `ReserveWorkAtDepth` reject with
the exact required depth without consuming work. The focused A3 layout, TypeSpec, witness/text and CLI resource selection passed
185/185 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_10_22_59_net11.0.trx`. A3
`LoadStandard`, `CreateStandardReference` and native-plan paths now select one
root ledger before input snapshots, reserve selected input/schema/artifact
state and copied bytes before allocation, and carry the same ledger through
later validation. Oversized artifact bytes and an `int.MaxValue` provider-list
count reject before touching the artifact span or indexing/allocating the list.
`TrRmArtifactIndex` now receives that ledger before A3 header and directory
preflight. It charges fixed-header and directory fields, Module feature ranges,
Feature validation, zero-padding bytes, reference-slot reads, section lookups,
and the state/work for deterministic out-of-order extent sorting. Standard
loading, reference creation, provider binding, native-address reconstruction
and retained-plan revalidation keep using the same ledger for reconstructed
indexes. Exact/one-short constructor and lookup boundaries, padding-byte cost,
out-of-order payloads and an oversized directory count are covered.
The shared `TrRmVariableTableIndex` now accepts the same ledger and charges
offset-field access, checked row boundaries and each selected row lookup.
Blob, Body, Contract, FieldPath, String and TypeSpec readers pass the root
ledger into it; duplicate caller-side offset charges were removed so each
index read is accounted once. The combined focused A3 index/layout/provider/
TypeSpec/witness/text/CLI plus variable-table and Body-reader selection passed
505/505 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_10_56_38_net11.0.trx`.
Repeated retained-plan work shares the same budget: after an earlier
no-`NativeImport` attempt, changed-provider revalidation can correctly exhaust
the remaining work before establishing incompatibility. Separate within-budget
provider mismatch and exact-limit tests remain active.
The selected A3 structural table/Body consumer route is reconciled, but the
full deterministic field/action/payload recipe remains unfinished. The current
producer gap is the exact-A3 Standard dispatch from `EmitStandardCore` to
`EmitForeignInvocationDraftNativeAddressLibrary`: it constructs the artifact
and performs final index/structural validation. That route now creates one A3
root ledger, reserves final framed-artifact bytes before allocating the output,
and passes the same ledger through `TrRmArtifactWriter`, the final
`TrRmArtifactIndex` and structural validation. The combined focused A3
consumer/provider/table/Body and producer writer/emitter selection passed
658/658 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_11_19_07_net11.0.trx`. Its
section/Body encoders now reserve output bytes and row state before allocating
their encoded buffers; the final frame separately reserves its output bytes.
The NativeAddress producer now charges its source scan, TypeSpec/signature/
parameter staging, strings and indexes, Module/contract/method/import/export/
NativeImport rows, Body maps/records/code, and section descriptors before
staging or allocation. `TrRmRegisterBodyMapBuilder` uses the same root ledger
for token collection, deduplication, index maps and encoded map bytes; storage-
address analysis and Body emission preserve that ledger through their nested
builders and record writers. Exact and one-short work-boundary checks cover
both the simple NativeAddress and storage-address producer paths. Their
focused emitter/Body-writer selection passed 157/157 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-11-01_12_13_16_net11.0.trx`.
The normal analyzer-enabled build remains baseline-red; comparison against
the saved pre-staging A3 producer-section output retained 351/351 unique
diagnostic identities, with no additions/removals and no diagnostics in the
touched files. The NativeAddress emitter's fixed-array TypeSpec path still
accepts only A2/A2E/A2EU, so A3 source array production remains unfinished and
must account for its TypeSpec/Layout/Body state before allocation.
A broader `Modules.TrRm` selection later reported 2,378
passes but was aborted when the test host crashed; it is incomplete evidence,
not a passing suite.
The nested constructed-provider checks now retain a one-entry validated
provider-table cache within the binding request and defer generic formal
storage origins to their constructed owner/actual comparison. The A2
PlaceSelection/EC fixtures use the assigned B5 fail-fast terminator rather
than B7 Unreachable, which requires the separate exception carrier. The full
`TrRmFixedArrayLayoutTableTests` class now passes 122/122; the combined
NativeAddress, fixed-array and selected provider-CLI selection passes 253/253
with zero skips at
`artifacts\\tst\\Debug\\compiler-adoption-integrated-final3\\compiler-adoption-integrated-final3.trx`.
The latest normal Debug analyzer/style build still fails on the repository
baseline; the normalized comparison in
`artifacts\\log\\compiler-adoption-a3-producer-section-analyzers.log` has 351
unique diagnostic identities versus the 353-identity baseline, with no
additions and two removed diagnostics.

Source production, source-free generic reconstruction and
remapping/replacement, generic-array CLI JIT/AOT and supervised
constructed-array runtime behavior remain open. The failure-class proposal
remains unapproved and only provisionally actionable; this candidate does
not admit general generic execution/export or establish conformance.

The separate unapproved source-contract proposal's `[E; N]` form is now
provisionally parsed and bound for initialized local storage. The former
`InlineArray` name recognizer, positive-only count rule and 4096-byte cap are
removed; current tests cover exact initialization, zero extent, local `uint64`
constants, scalar/pointer/committed-aggregate elements, zero-payload unit
elements, nested scalar/unit local initialization and row replacement/copy,
checked nested scalar/unit selection and positive-payload scalar addresses,
references, and page-probed JIT/AOT frames. Unit
arrays retain exact N-expression initialization and execute indexed reads and
writes with a dynamic bounds-checked index; zero-extent element addresses are
rejected. This does not approve the source contract or establish all required
cases. Source array identities now resolve through canonical table rows that
carry complete element identity, exact count and checked geometry; locals no
longer duplicate that layout in a sidecar. Pointer-bearing nested-array
provenance, distinct Copy Transparent-over-void nominal elements, ConstEval
extents in whole-array function signatures, field initialization/access, A2
PlaceSelection index
transport/remapping, full-width local-frame offsets, provenance/transformation,
and other declaration contexts remain open. Local and struct-field extents
require exact `uint64` and transparent aliases evaluate eligible scalar
`ConstEval` calls through the existing bounded required-constant path; struct
field layout derives from the array row. Whole-array function signatures
continue to reject because no call ABI is selected.
Private semantic revision 100 is a separate unapproved provisional schema
that inherits revision 99's u64 tag-35 storage-address extents and adds
structural array rows and flat direct-local operations. Nested and
ptr-to-array runtime forms, and managed-class fields whose pointer or
nested-class graph reaches array metadata, remain excluded as runtime carriers.
This private path does not select or substitute for Standard A2. Full
private artifacts preserve metadata-only nested/pointer/fnptr recipes and
descriptive nominal array fields through ordinary, set and graph remapping;
bodyless references prune unrooted recipes. The separate unapproved
[artifact-local metadata boundary companion](../proposals/native-fixed-array-private-metadata-boundary.md)
records that provisional boundary without adding type-only exports or
relaxing runtime exclusions.

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

The standard C# and Terrum-authored Import/Export validators now recognize
FieldDef DefIds and Import expectations. They check the consumer's owner/field
stub rows, explicit owner imports, same-consumer ModuleRef ownership, public
provider fields/owners, expected-stub non-reexport and unique keys. They do
not compare complete field types, static/mutable access, owner substitutions
or retained Layout/TargetProfile membership.

Standard structural FieldPath and target-layout readers/writers validate
complete root/subject TypeSpecRefs, FieldDef/Import FieldReferences, concrete
TargetProfile rows and exclusive `004A` observation ranges. The active TRRM
tests cover direct/imported references, malformed paths, query-specific ranges,
profile bounds and overlapping/orphaned ranges. Exact owner-substitution and
layout membership/computation remain in this gate.

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
conformance/constraint/generic-method forms. The assigned 32-byte
GenericParam and 12-byte Constraint rows now have C# readers/writers and
Terrum-authored structural validators for ownership, formal domains, ranges,
flags and currently assigned payload tags. Complete TypeSpecRef shapes and
ConstantSource row bounds are structurally checked, but payload/value
semantics, the Constraint kind-3 associated-relation link,
generic substitution, proof, witness and execution semantics remain open; row
framing alone does not execute a generic contract. The standard structural
validator also checks InterfaceDef/AssociatedTypeDef ownership, self-formal
identity, range extents, associated-row partitioning, names and optional
default TypeSpecRefs. InterfaceBase/InterfaceMethod ownership, ordinals,
complete base TypeSpecRefs and direct instance MethodDef ownership are now
checked; InterfaceConstDef/AssociatedConstDef ownership, names, complete type
references and bounded ConstantSource row references are also checked.
The hosted source declaration binder represents generic interface arguments
as an ordered type/constant union, applies trailing defaults, substitutes
earlier type formals into constant domains, preserves forwarded interface and
member constant-formal identities, and remaps values and domains together.
Active regressions cover literal, default, forwarded and incompatible-domain
arguments; this is not witness conformance or Standard interface-member
provider/consumer support.
InterfaceWitness/WitnessMember rows now validate complete type references,
exclusive member/associated ranges and direct MethodDef bounds. A provisional
WitnessAssociatedType reader/writer now validates 20-byte extents, witness
range ownership, direct/import AssociatedTypeDef identity and owner agreement,
complete declaring/value TypeSpecRefs, nominal declaring-interface membership
in the witness interface's inherited closure, and only binding-origin tags 1
or 2. Base rows must identify interfaces. Tag 2 requires a nonzero default on
the identified local/expected declaration.
Those tags and the effective-origin contract remain unapproved in
[`witness-associated-type-binding-origin.md`](../proposals/witness-associated-type-binding-origin.md);
exact constructed-interface membership/substitution, selected binding,
default equality, requirement compatibility, dependency remapping and runtime
use remain incomplete.
Standard AssociatedRelation rows now validate complete declaring/required
TypeSpecRefs and direct AssociatedTypeDef or Import references expected to
denote that definition kind; the declaring-interface reference must be a
TypeSpec row, not an inline F2/F3 form. Constraint kind-3 still accepts only a
nonzero payload; the candidate row bound, owning-formal domain and exact
relation binding remain unfinished. Base-instance binding, other non-Body
ConstantSource consumers, provider/default comparison, relation/witness
binding and execution remain incomplete. The provisional 24-scalar
FrozenValueNode reader/writer validates fixed row references and selected
kind-1 payload widths, unit, Boolean, rune and raw-bit rules. Selected
register-Body ConstantMap use now requires Feature 11/1 and binds tag-1
ConstantSource to kind-1 FrozenValueNode with exact unqualified scalar type
identity and Blob payload validation. Initialized InterfaceConstDef tag-1
values use the same checks with Feature 11/1 from the owning Module. This path
also binds GenericParam constant defaults through their owning TypeDef or
MethodDef Module, and selected Body TypeMap form-0x05 constant arguments bind
against their constant formal. Declaration-owned TypeSpecRefs now traverse
nested constant arguments for method signatures/parameters, fields, generic
defaults and constant types, kind-1 constraints, interface bases/constants,
associated-type defaults, union fields, builder registrations and text
targets/captures under their owning Module. Ownerless witness, predicate and
query contexts remain unresolved. The Windows x64 materializer now maps all 24
provisional fixed-scalar carriers with exact extents, alignment and raw bits;
wide values use a two-word BoundConstant payload, and legacy 64-bit access
rejects values that would truncate. The hosted emitter supports joint
nonforeign identity/copy calls for the eight wider carriers under the selected
Windows x64 call profile, while direct foreign signatures remain rejected.
Source-free materialization and native call tests cover the wider payloads,
including distinct high words. Standard register-Body execution remains a
single int32 ConstantMap return; wider standard CallSite execution, other
non-Body consumers, generic substitution, producer/interning/remapping and
general execution remain unfinished. The proposal remains unapproved and does
not change normative registration.
AssociatedTypeDef Import/Export identity is implemented only as the unapproved
provisional candidate.
[associated-relation payload proposal](../proposals/constraint-associated-relation-payload.md)
is unapproved, provisionally guides implementation only under explicit user
authorization, and does not itself approve or implement the separate
AssociatedTypeDef identity candidate. Its C# Import/Export identity path is
also provisional; exact provider-owner/default comparison remains open.

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
well-known and referenced-type argument forms. The accepted registry also
assigns Body TypeMap pseudo-type forms; the unapproved retirement proposal is
provisionally rejecting them at selected use because their complete qualifier
and role elaboration is unsettled. The registry remains unchanged. These
assignments replace the TypeArg
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
builders/protocols. Standard structural BuilderRegistration and TextTarget
readers/writers now validate target TypeDef/MethodDef rows, complete
schema/recipe TypeSpecRefs, parameter ordinal bounds and duplicate targets.
TextContentShape/TextCaptureShape readers now validate generated TypeDef
ownership, exclusive capture/fragment ranges, capture order/modes and complete
capture TypeSpecRefs. The unapproved
[TextFragmentShape reference-form proposal](../proposals/text-fragment-reference-forms.md)
is provisionally implemented by structural checks for literal/formatted field
presence, shape-local capture rows, MethodDef row/module bounds and
generated-type-owned kind-1 Constraints. Selected-use validation binds methods
to InterfaceMethod rows, checks formatter/writer interface separation and
per-shape consistency, matches closed witness implementing types to captured
types and exact WitnessMember requirements, and checks open type-formal capture
and interface-constraint closure. The provisional public
`ValidateTextConstructionProfile` path accepts exact per-Module role rows and
checks direct interface membership, public declarations, Boolean results,
receiver access, ordered parameter types/modes, generic domains/constraints,
TextSpan identity and absence of no-normal-return/caller-unsafe effects. The
structural-only `Validate` API does not imply this profile check, and no
current CLI/source-free consumer supplies the context. Provider Import/Export
resolution, replacement/remapping, source recipe meaning, runtime
construction and retained constant programs remain open. A self-hosted method
or candidate API sketch supplies no selected profile identity, retained
constant program or frozen value codec.

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
separate with their own owners. Canonical duplicate-free flat footprint ranges,
16-byte PredicateNode and 8-byte operand-row shapes, and exclusive operand
ownership are integrated. The unapproved `general-predicate-graph-schema`
proposal is provisionally implemented through an internal
`GeneralPredicateGraphsDraft` profile: Feature 17/1 and its 10/11/12 closure,
candidate 004D-0052 table codecs, TypeSpec form 10, ReadRoot kind 8, 000A
EvaluationForm clauses/read bounds, ordinary row-order-independent edge
validation, one-positive-binding-site capture validation, and a source-free
phase-1 TypeIdentityEqual consumer path. The focused graph/contract/codec
selection, including ImmutableData row decoding and Feature-19 checks for
actual/allowed roots and associated-constant identity/Import/Export cases,
passes 123/123 with zero skips at
`artifacts\\tst\\Debug\\predicate-immutable-associated-integration-final15\\tagoo_GOODING-ZEN4_2026-10-28_12_09_14_net11.0.trx`.
Standard remains unchanged and phase-3 selector-only. Complete node type/
operation/call/witness/failure checks, exact ImmutableData value/witness/
provider binding, constructed declaring-interface closure/origin and
expected-binding, complete ConstantExecution source/value binding, source
binding, proof/ConstEval, canonical graph rewriting/remapping and ordinary
lowering remain unfinished. Candidate ConstantExecution/LogicalInputView
structural codecs now check retained dependency-kind-6 and backing-kind-2
Feature-19 requirements through explicit using contexts in the selected
GeneralPredicateGraphsDraft profile. The closure now also follows retained
ConstantSource references embedded in PredicateCall owner/type actuals,
PredicateTypeUse operands and type patterns. Exact cross-Module context
reconstruction, provider replacement, other retained contexts and remapping
remain; see the focused evidence below.
The proposal remains unapproved and no normative
registry/specification owner changed. Optional attributes and names are not
a fallback.

The separate unapproved `immutable-data-feature-admission` candidate now
registers Feature 19/1 only in `GeneralPredicateGraphsDraft`, with no
prerequisites. Its isolated `{19/1}` closure, independent of Features 10/11/17,
is covered by that combined selection. The candidate reader checks 0041's closed kind, target family/bounds and
complete owner TypeSpecRef, and graph actual/allowed root-6 reads require 19
in the using Module. Owner checks require zero exactly for nongeneric target
owners and the corresponding nominal definition/formal shape otherwise;
generic WAC-backed owner TypeSpecs match the selected witness implementer.
The associated-constant candidate adds DefId `0x25`
Import/Export resolution and a `0033` reader/writer accepting direct or
Import-form declarations, validating witness-range ownership and the
definition-level inherited-interface closure. A Feature-19 root-6 case now
selects such a WAC row and rejects a missing using-module feature. Exact
constructed declaration-instance, value/witness/provider binding, other
retained execution contexts and remapping remain incomplete; Standard and
normative registries are unchanged.
Separate unapproved proposals now recommend `0x25=AssociatedConstDef` with a
direct/Import reference for witness requirements, and static sections
`0055..005C`/clauses `0013/0014` with a whole-inline publication carrier.
New unapproved proposals provisionally select `005D`/DefId `26` for
IntrinsicRegistration identity and `005E` for ConstantExecution's ordered
substitution range; the mixed-substitution proposal recommends replacing the
four-byte TypeSpecRef-only candidate with eight-byte tagged type/constant slots
for checked-Body formals. The provisional candidate profile now uses that
eight-byte row shape and checks TypeSpecRef/ConstantSource namespaces, formal
kind/order, exclusive ranges and direct constant-source request cycles.
Exact source value/domain and dependent-type binding remain incomplete; none
of these proposals changes the normative registry.
Candidate `005D/26` role-2 Imports now have a cross-artifact validator that
selects the exact provider Module/export key, checks target/runtime
requirements and requires exact registrationId/ruleRevision identity.
Mismatches, missing exports and ambiguous providers reject; this is identity
binding only, not intrinsic catalog admission, packaging/rewrite integration
or runtime execution.
These are provisional design directions, not normative changes. The static
proposal now has candidate-profile structural readers/writers, DefId `24`
Import/Export resolution, StaticDependency, StaticAccess, publication and
ImmutableData owner checks, and MethodDef initialization-role/demand
validation. The focused Debug build and related TRRM, import/export,
ImmutableData, static-table, Contract and intrinsic provider-binding selection
passed 75/75 with zero
skips at
`artifacts\\tst\\Debug\\constant-execution-provider-binding-final\\tagoo_GOODING-ZEN4_2026-10-28_14_38_53_net11.0.trx`.
That candidate selection exercises request-owned ConstantExecution ranges,
the provisional 005D registration/Import identity, 005E tagged
TypeSpecRef/ConstantSource slots and checked-Body mixed-formal matching,
direct nested-request cycle rejection, Feature 19 for dependency kind 6 and an
ImmutableData-backed input view,
missing-feature rejection through predicate TypeUse/type-pattern ConstantSource
references, exact 005D/26 provider identity success/failure, and an
unreferenced-view case that does not activate 19. This is structural candidate
evidence only; exact substituted actual/value/provider binding, all retained
using-context sources, type-dependent ConstantSource cycles, packaging/
re-encoding, Body/runtime,
witness-origin, richer access and indirect-backing gates remain open.

<a id="cg-008"></a>
## CG-008: Static initialization

**Accepted logical behavior is distinct from the provisional static-storage
candidate; source production and runtime integration remain incomplete.**
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
ranges. The selected logical `InitializationRole` contract is one 20-byte clause
with tags `1=eager initializer`, `2=lazy initializer`,
`3=authored static constructor`, `4=eager-phase helper`,
`5=lazy-phase helper`; each method has at most one role. The selected logical
16-byte `InitializationDemand` contract carries one closed eager-group
demand, with at most one per resolved group. Approved candidate records also
include `StaticField` flags `0=not writable after initialization`,
`1=writable after initialization`, a 12-byte `StaticAccess` with target tags
`1=local StaticField`, `2=imported StaticDependency` and authority tags
`1=ordinary completion demand`, `2=initialization-phase authority`. Exact
physical row schemas, section/DefId/Contract clause IDs, reference encodings
and remapping remain unassigned in the accepted registry; in particular, the
superseded 8-byte `StaticDependency` candidate is not an assigned schema. A
separate unapproved [static persistence proposal](../proposals/immutable-static-field-persistence.md)
recommends sections `0055..005C`, clauses `0013/0014` and one whole-inline
publication carrier. This does not change normative assignments or establish
static compiler/runtime support. Its candidate-profile structural
implementation currently validates StaticField/Contract, EagerGroup,
EagerInitializer, LazyUnit, StaticAccess, StaticDependency and publication
rows; it resolves candidate DefId `24` through role-checked Imports/Exports,
checks method-owned initialization clauses against initializer/constructor/
lazy identities, and requires a matching publication/dependency for retained
ImmutableData kind 2. The focused Debug selection passed 68/68 with zero skips
at `artifacts\\tst\\Debug\\static-contract-clause-integration-final7\\tagoo_GOODING-ZEN4_2026-10-28_12_58_52_net11.0.trx`.
It does not establish exact provider replacement, source-authored output,
Body StaticPlace/recovery, runtime state/failure/cleanup, AOT/JIT execution or
final immutable-value truth.
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

**Bounded implementation; remaining evidence and coverage required.**
[Owning-field contracts](../specs/language/ownership-and-cleanup.md) distinguish
complete values, structural partial move/repair, authored cleanup and
reverse-declaration versus reverse-acquisition retirement.

**Current/evidence:** source binding, mandatory verification and Windows x64
lowering now support direct top-level field moves and vacant-field repair for
structurally tracked unique local owners. Per-field runtime flags skip moved
children during partial retirement and whole-owner replacement; constructor
acquisition flags remain distinct. The focused JIT/emitted-image selection
passed 102/102 with zero skips at
`artifacts\\tst\\Debug\\partial-owner-field-move-final-v5\\partial-owner-field-move-final-v5.trx`.
Direct top-level live unique-field replacement is also provisionally
implemented for derived and authored-cleanup parents, including constructors
and maybe-live field states. It stages the replacement before cleanup and
requires a closed same-program proof of nonthrowing transitive retirement;
unknown, fallible, external or reentrant Cleanup is rejected. This proof is a
conservative implementation subset, does not rely on a general NoThrow
declaration, and is authorized only by the still-unapproved
[replacement-retirement proposal](../proposals/live-field-replacement-retirement.md).
The Debug fast build passed and the active checked-reference, partial-owner
lifecycle and live-replacement selection passed 42/42 with zero skips at
`artifacts\\tst\\Debug\\partial-owner-live-replacement-final-v2\\partial-owner-live-replacement-final-v2.trx`.
Nested unique-field moves, vacant repair, maybe-live replacement and
hierarchical partial-retirement flags are now implemented for structurally
tracked local owners with eligible mutable ancestor paths. The selected private
revision-92 module route provisionally persists and remaps `BoundFieldMove`
using tag 42 under the unapproved
[private-persistence proposal](../proposals/owning-field-move-private-persistence.md);
it does not add Standard TRRM support.

**Routes/gate:** per-field generations are carried through local state copies,
conditional/recovery joins, and loop fixed points. Whole-owner replacement
invalidates descendant generations; a direct field move and vacant-field
repair advance only that field subtree. A sibling checked reference remains
valid across a conditional partial move. The Debug fast build passed, and the
active checked-reference plus partial-owner lifecycle selection passed 29/29
with zero skips at
`artifacts\\tst\\Debug\\field-generation-final\\field-generation-final.trx`.
The analyzer-enabled Debug build remains blocked by 721 repository-wide source
analyzer/style errors (1,442 repeated diagnostic lines); it did not reach test
project compilation. There are no diagnostics in the changed nested-owner
verifier, binder, emitter or module-codec ranges after local style fixes.

The current tests exercise both runtime policies, branch-joined nested
replacement, checked repair failure, a repairing loop, sibling-reference
survival and ancestor invalidation, nested construction failure without
incomplete Cleanup, checked failure with a live nested owner field,
nested partial retirement in reverse field order, constructor failures before,
during and after field acquisition, malformed private field-move records,
provider replacement, and native graph/set exchange with a remapped consumer
type table. The Debug fast build and combined active selection passed 63/63
with zero skips at
`artifacts\\tst\\Debug\\nested-owner-complete-final-v5\\nested-owner-complete-final-v5.trx`.
Other acquisition layouts and all remaining unwind joins remain unfinished.
The current route does not establish Standard TRRM, CLI or standalone-compiler
support, generalized checked-reference provenance or proofs for called
Cleanup bodies. Preserve exact live fields, original responsibility,
repair/retirement order and complete-receiver restrictions through binding,
traversal/remapping, verification, persistence and lowering. Never invoke
incomplete-parent authored Cleanup, replay a retired child, or pass partial T
as a whole value. Independent publication depends on CG-002/003 and complete
ownership/property codecs.

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
union-case identities. The internal predicate candidate now structurally
validates graph/node/operand ownership, the assigned pattern/call/helper row
extents, phase-specific node families and direct footprint references.
Complete per-kind type/policy and selected call/provider semantics, typed
capture evidence, immutable-data identity, and replacement/remapping remain
gates. Graph-local capture ordinals and value, checked-reference and generic-
type-argument roles remain distinct; capture-dependent TypeSpec form 10 is
profile-scoped and does not permit a source/body type escape. Rewriters must
preserve graph/capture identity rather than substitute matching row bytes.
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
limits and admits the returnable fixed scalar set except `nint`/`nuint`; wide
integer source operators now bind, verify, fold and execute through Windows
x64 JIT/AOT, and hosted floating/BID operations fold in their target formats.
Native BID decimal32/64/128 operators now execute through PE and CLI JIT under
both runtime policies; native float128 operator execution remains incomplete.
`ConstEvalTests`, `WideScalarLiteralTests`,
`JitTests.BidDecimalOperatorsExecuteAcrossAotAndJitUnderBothRuntimePoliciesAsync`,
and supervised wide integer/literal PE/JIT coverage exercise the current
boundaries. The
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

**In progress.** The
[source no-return rule](../specs/language/expressions-and-statements.md#no-return-contracts)
and [clause contract](../specs/representation/contract-clauses.md#4-doesnotreturn-and-actual-completion)
permit any admitted declared result type, retaining ordinary typing.

**Current/evidence:** The hosted binder/verifier and scalar frontend accept
non-unit declarations without erasing their signature, reject reachable
normal return/fallthrough, and preserve ordinary type checking. No-normal
flow is carried through direct calls, return expressions, initializers,
arguments, conditionals and pre-test conditions. Private module
replacement, native module execution and both scalar frontend routes have
active regressions. The Release fast build and focused no-return/effect/
control-flow/frontend selection passed 131 tests with zero skipped; this is
not current-schema TRRM CallSite evidence.

The selected standard-body method validator now resolves direct/import
MethodMap targets for general CallSites and compact bound calls. It enforces
the unit/non-unit result-selector shape, requires a `DoesNotReturn` CallSite
to use `None` and end its Block, and rejects compact bound calls to those
targets. Short `0x90` is admitted as a potential terminator. Release fast
build passed; the focused register Body reader selection passed 57 tests and
the combined TRRM/metadata/CLI/no-return selection passed 1,763 tests with
zero skipped. This is selected-validation evidence, not current-schema
execution evidence.

**Routes/gate:** complete the accepted register CallSite rule through
indirect Register/Home targets, compact callable-value windows, full argument
mode/type/ownership checks, result-home type/vacancy and discard
responsibility, canonical writing, dependency binding/remapping and JIT/AOT
execution. A verified no-return target must use result selector `None` and
terminate its Block without publishing a normal result or successor; the
complete target signature remains in metadata. A normal non-unit call retains
its typed result selector, and compact calls remain normal-returning forms.
The development indexed Body currently round-trips non-unit no-return
declaration/body facts, not terminal non-unit callsites.
The source/private/native/frontend slice now preserves evaluation order and
no-normal state across supported expression-bearing statements, including
assignments, coalescing, native buffers and later arguments. The remaining
accepted register CallSite gate above is independent of that source slice.
The constructor applicability recommendation in
[`constructor-no-return-applicability.md`](../proposals/constructor-no-return-applicability.md)
remains unapproved and provisional; its source/JIT slice is tracked
separately. Struct and managed-class constructor source tests cover truthful
no-return declarations with vacant receivers; emitted-native and JIT class
factory tests cover failure under both runtime policies and checked-path
allocation cleanup. Private semantic-module linking and native-module
consumer execution retain the class constructor effect. Static constructors,
imported constructor replacement compatibility, broader nested construction
and standard register CallSites remain gated by their actual codecs and
initialization contracts.

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

**In progress: prototype limits and projected records are not the selected
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
implementation slice. `frontend-build`, `frontend-build-library`, and
`frontend-run` now invoke the C# compiler process on original source for every
input. Only the explicit `frontend-component` test command retains the
projection path; those buffers are not consumed by executable build/library/
run.

**Routes/gate:** keep fixed v87/v88 projection buffers confined to the
test-only `frontend-component` command or replace them with the selected
count-sized representation; never route build/library/run through them.
Preserve direct argument identities, source ordering, initialization/effects,
cleanup and whole-source process compilation. Move verifier budgets to cached per-function work
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

**Current/evidence:** `frontend-build`, `frontend-build-library`, and
`frontend-run` start the C# compiler as a separate process for every input,
send original source text on stdin, preserve source identity and runtime/
arithmetic options, and do not pass v87/v88 records. Library compilation
selects all public top-level functions. Focused CLI coverage checks executable
build, JIT, library parity, diagnostics and output preservation. The
projection-based success path remains only in the explicit test-only
`frontend-component` command. The Terrum-authored implementation remains
bounded and is not yet a self-sustaining compiler. Direct-source bootstrap
tests are implementation evidence, not proof of a general Terrum compiler.

**Routes/gate:** keep `frontend-build`, `frontend-build-library`, and
`frontend-run` on the original-source process boundary until the Terrum
compiler can compile those inputs independently. Keep the projection-based
component command test-only; do not use its result as a build artifact or
handoff to the C# compiler. Keep observable language support in sync across
both implementations and the direct-source prototype's limitations out of
language/spec support claims.
Use a previous stable compiler to bootstrap future Terrum-only features after
the implementation reaches the required milestones.

<a id="cg-017"></a>
## CG-017: Direct token spans and deferred returned views

**General source dependency contracts remain accepted.** Use
`Span<Token>` directly over a validated in-memory snapshot with `nint`
length/index semantics and full bounds checks. The user declined a
`TokenCursor` wrapper, a separate 64-token fast profile, and private
`.trm` Span<Token> persistence. Same-compilation returned-view forwarding
has a bounded TokenSpan prototype; it does not establish standard metadata,
general dependency proof, or persisted view support.

**Current/evidence:** `TokenCursor` remains a component prototype. The private
module writer now rejects compilation-local span roles, types and operations;
the reader rejects span payloads in retained revisions and refuses revisions
77/81 before decoding. Source binding now resolves flat `DependsOn`,
`Captures` and `ForwardsBacking` recipes into normalized in-memory
receiver/formal and destination identities, including getter and initialized
output targets; it distinguishes direct `this` from `nameof(@this)`/literal
`"this"`, accepts explicit empty sets, and rejects malformed, unknown,
duplicate and incompatible recipes. Explicit `ForwardsBacking` sources are
checked against a matching explicit dependency/capture set. The focused
source-recipe selection also confirms a valid strict subset and rejects extra
or unrelated sources; 32 tests passed with zero skips. The legacy
TokenSpan call/capture flow now consumes normalized source contracts, and the
legacy projection is checked for consistency. This remains compilation-local
and uses live slot identities. In the TokenSnapshot/TokenSpan profile, per-state
backing maps remap across owner transfer, keep a later reuse of the old slot
distinct, union conditional sources, and retire descriptors before owner
cleanup. Active coverage exercises transfer, retirement, reinitialization,
conditional-source union, loop-scope cleanup and staged call arguments.
TokenSpan result defaults now consider only TokenSpan formals and
TokenSpan/TokenCursor/validated TokenSnapshot receivers; unrelated owners and
receivers do not widen the inferred source set.
Separately, a bounded source-verifier slice admits `Captures` on writable
pointer holders whose pointee carries pointer provenance. The body must satisfy
the declared capture set; call sites use the published declaration, stage
actual source origins, and preserve unknown writable effects as unknown. The
Release fast output/provenance/register-map selection passed 531 tests with no
skips. General `Captures` persistence is not yet implemented: private revision
92 now rejects a NativeCallable capture as unrepresented rather than dropping
its Dependency declaration; candidate revision 93 must carry and revalidate it.
General aliasable-reference generations, owner/guard/root/pin obligations,
inherited/provider binding, body verification, private dependency
persistence/remapping and general lowering remain incomplete. No standard
`.trrm`, public span ABI or persisted span capture follows from this slice.

**Routes/gate:** keep span backing, access, owner/guard, and retirement facts
through direct calls, copies, joins and cleanup. Keep the cursor prototype
test-only and never reintroduce private or standard span persistence without a
new contract and concrete consumer. Continue the accepted general dependency
contract work through defaults, body verification, provenance, persistence,
remapping and lowering; do not treat the bounded returned-view prototype as
completion of those obligations.

<a id="cg-018"></a>
## CG-018: Unselected source-grant prototypes

**Selected boundary:** compiler frontends consume original source text at the
compiler input boundary; JIT consumes semantic metadata. A source-file buffer
is not a selected runtime capability of persisted semantic modules. Isolated
AST/CST testing APIs do not imply a private semantic-type format or a source grant.
Compile-time text capture, if separately designed, produces a string value;
it does not retain a reference to the original source file. No `Host.Source`
semantic-module requirement, persisted scanner/frontend invocation profile,
native source-grant ABI, or standard `.trrm` source-grant record is selected.

**Current/evidence:** `NativeJit.ExecuteWithSourceGrant`,
`ExecuteWithScannerProfile` and `ExecuteWithScalarFrontendProfile` are
explicitly documented test-only prototypes. The private semantic-module
writer may retain only the existing tagged source-grant profiles used by
those tests; native-module packages and standard TRRM reject them. The
ordinary JIT entry point rejects unbound source grants. `frontend-build`,
`frontend-build-library` and `frontend-run` always invoke the C# compiler
process on original source; `frontend-component` alone retains a test-only
projection. `NativeSourceGrantLibrary` is test-only and adds no native calling
convention.

**Disposition:** these profiles are not selected compiler/JIT contracts or
conformance requirements. Keep retained implementations explicitly
test-only; do not infer a whole-source runtime reference, hidden native
parameter, source-dependent native package, or persisted-AOT feature. Any
distinct future need requires a new concrete consumer and contract review
rather than promotion of these profiles.

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
The checked Standard TRRM source-reference route now retains generic nominal
TypeDef/provider identity and exact selected Core Task<T> signature identity;
selected Task-returning declarations bind the operation-capture default
separately from completed-payload dependencies. Private semantic/native
persistence still rejects those nominal carriers; task source defaults do not
yet flow through call-result provenance or provider replacement. This source
binding does not admit Task values in bodies or execute consuming await.

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
An internal, unapproved `RootPinRuntimeDraft` now structurally isolates
candidate Feature 14/1, TargetProfile 2/1 and Body F0/record-0D from the
foreign-invocation schemas. It does not bind source operations, provide the
runtime DLL, implement relocation/pinning in JIT/AOT, or support private and
Development inputs; this is not runtime evidence.
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
decisions integrated. Selected Body framing and partial map-reference
validation are implemented, but complete producer/consumer routes and
qualification remain blocked on CG-001. Numerical portable resource minima
and exception-region physical carriers remain unassigned.**
[Canonicality](../specs/representation/encoding-canonicality.md),
[reader trust](../specs/representation/reader-trust.md) and
[resource accounting](../specs/representation/resource-accounting.md),
[Body metadata](../specs/il-metadata.md) and the
[instruction contract](../specs/il-instruction-set.md) define the accepted
schema. It assigns the `17 + 4*popcount(mapMask)` register-stream prefix,
five optional Body maps including four-byte FieldMap tokens, global FieldPath
and target-layout field-reference sections, zero-based `u16` local indexes
with `u32` counts, `DE` query/TypeIdentity controls, BlockEntryState carrier
2, instruction/record layouts, F2/F3 TypeMap tokens and TypeSpecRef fields;
the accepted registry still assigns F0/F1, which the provisional selected
schema rejects. Also cover the accepted integer, binary-floating and decimal
type matrices.

**Current/evidence:** development fixture/reader behavior is not independent
proof of the selected schema. The selected-Body validator checks map token
kinds, row bounds and direct/Import ownership; its provisional path rejects
examined F0/F1 tokens under the unapproved retirement proposal while accepted
registry assignments remain unchanged. It checks receiver/formal HomeMap
types against MethodSig types for direct/Import TypeDefs and TypeSpec/F2/F3
forms, including inline/indexed family equivalence, and traverses map-reachable
TypeSpec references under the using Module. It does not establish normalized
constructed-type substitution, callee/provider contract equality,
ConstantSource value-type equality, canonical writing or runtime execution,
and is not current-schema conformance.
The bounded NativeAddress producer now preserves TypeMap/MethodMap
first-logical-use order and deduplicates repeated tokens, but this is not
canonical emission for every selected Body form or an A2 array Body route.
Numerical portable resource minima remain unassigned, not invented defaults.
A lower fixed-width ID is not smaller on disk.

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
The hosted lexer/decoder now covers noninterpolated raw-text fence selection,
single-line and framed forms, indentation, newline directives and Unicode
scalars; source binding consumes decoded ASCII through `utf8z`. Interpolated
raw text now tokenizes and parses raw segments, nested ordinary expression
holes, alignment, and static/quoted/raw/parenthesized formats; typed formatter
construction, literal-segment framing/newline decoding, and runtime
qualification remain open. Constant generic-argument binding also remains
open. The switch parser accepts the shared pattern grammar, while hosted switch
execution currently covers scalar constants, wildcard/inferred/exact-type
bindings and short-circuit scalar `not`/`and`/`or` composition. Non-scalar
pattern binding, the broader `is` expression forms and switch expressions
remain open.

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
incomplete.** The unapproved RootPin proposals provisionally select a Windows
x64 stable-cell runtime, Feature 14/1, TargetProfile 2/1, F0 and Body record
0D; a separate exact `RootPinRuntimeDraft` profile now dispatches its
`ReferenceSupport` record without composing the foreign-invocation schemas.
The separate unapproved loaded-type proposal specifies same-provider
descriptor admission and retained layout/code/Cleanup origins. Its
supplementary native admission export now validates and interns typed
descriptor graphs transactionally under a test-only domain path. The provider
now exports the exact 104-byte V1 operation table inside the 112-byte
`RootPinRuntimeInfoV1`, with the same-provider V2 loaded-type admission
and V1 loaded-method admission exports. Method admission validates
same-domain types, support-bearing receiver/formal slot maps and origin
closures, interns exact maps, rejects conflicts, and publishes method
descriptors and new origin holds transactionally. Its probe exercises
profile/map rejection, canonical origin ordering/kinds, duplicate slots,
duplicate identity, distinct keys for equivalent maps, borrowed receiver
mapping and origin-retain rollback. The same supervised test builds a
production-mode provider without test hooks and checks exact info/table sizes,
readable info/domain storage, executable table/export pointers in the same
mapped image, and the V2/V1 admission exports. AOT provider resolution and
complete end-to-end consumer checks remain open. The JIT consumer
now resolves internal RootPin runtime fixups through the selected absolute path,
validates the exact ABI, provider-image pointers, immutable info/domain and
mapped-file identity, then pins and retains the process provider. A supervised
synthetic JIT import passes with the production DLL and rejects a second path.
An isolated child-process probe rejects a provider missing a required
admission export, verifies that subsequent requests remain failed, and runs
four concurrent same-path imports against a delayed getter successfully.
These qualify bootstrap for the synthetic internal fixup; source/compiler-
produced fixups and AOT resolution remain open. A second unapproved deployment
proposal now recommends one host-selected absolute ASCII path P embedded in
affected AOT PE imports and reused unchanged by JIT; it remains unapproved and
has no AOT consumer implementation evidence. A separate unapproved
fixed-array amendment extends that descriptor handoff and selects its
V2/96-byte-node form. The standalone native admission implementation and
supervised test use that V2 shape, including checked count/stride, nested
arrays, packed placement and zero/unit cases. A test-only provider path now
registers host-owned initialized objects and exercises opaque ObjectCell,
RootCell and PinCell identities, exact field/array roots, stable resolution,
pin overlap/retirement and acquisition rollback. It also traverses copied
support through fields, optionals and fixed arrays; registers frame slots;
transfers staged call holds into parameters and results; and marks test-host
object/support cycles without treating descriptor holds as global roots.
Retain-before-release replacement, exceptional handoff cleanup and invalid
support-cell fast-fail are covered; support release does not invoke the GC
retirement thunk, and type/code/cleanup origins remain retained by the domain.
The test-only provider now registers per-thread frame chains, cooperatively
parks mutators at `Poll`, and traces frames from multiple parked threads.
Its separate test relocation path copies reachable host-registered payloads
from typed parked mutator frames when unpinned, preserves ObjectCell/RootCell
identity and offsets, skips active pins, and discards every staged copy when
an injected destination allocation fails.
The bounded native probe covers support cycles, repeated moves, overlapping
and final-pin behavior, concurrent polling and allocation rollback. The older
payload-substitution helper remains only a test address-change probe. This
does not establish source operations, compiler-produced JIT/AOT fixups,
production collector integration or general runtime execution. It is
provisional test evidence, not normative adoption or complete runtime support.
Canonical source contracts are in
[language](../specs/language.md),
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
GC-triggered object Cleanup. The internal RootPin draft selection now checks
Feature 14/1, TargetProfile 2/1 and the F0/record-0D schema boundary, including
the distinct foreign-invocation NativeAddress record. The test-only native
probe exercises typed support-cell holds, per-thread frame registration,
cooperative `Poll`, cross-thread frame tracing, call/result transfer, exception
rollback, typed object-graph tracing and transactional test-only payload
relocation. The provider ABI is exported and probe-tested. The synthetic JIT
resolver's exact selected path/file identity, same-provider ABI, process-
lifetime retention, missing-export failure latch and concurrent same-path
bootstrap are supervised; compiler-produced fixups and AOT provider
resolution remain open. Source operations, standard producer/consumer
execution, production collector integration, compiler lowering and complete
qualification remain open. The supervised JIT provider selection passed 1/1
with zero skips at
`artifacts\tst\tagoo_GOODING-ZEN4_2026-10-30_05_04_25_net11.0.trx`. These are
scoped implementation facts, not a reason to weaken the accepted contracts
or infer conformance from the private collector.

The separate unapproved
[normal-backing candidate](../proposals/reference-normal-backing-support.md)
now has an exact case-sensitive host agreement key threaded through
`CompilationOptions`, bound programs, source Binder, `ValueLayout`, mandatory
verification, native emission/JIT entry points and the four selected CLI
commands. Under that explicit key, ordinary checked-reference fields have the
candidate 16/8 size/alignment and nested inline layout is recomputed; regression
coverage checks the changed field offset/aggregate size and confirms that
omitting the key cannot verify or emit the candidate-bound program. Emission,
direct native imports and unsupported JIT/AOT routes fail closed rather than
selecting today's 8-byte layout or a tracing profile. A bounded source-bound
runtime slice is now implemented: each allocation has a compiler-private
32-byte control with exact runtime identity, nonreused generation, checked
strong-hold count, class identity and payload size; a separate per-image/JIT
24-byte runtime state supplies generation and live-allocation accounting.
Normal class handles are retained/released across local copies, borrowed
receiver calls, scope exit and checked construction failure, without joining
the tracing allocation list. Both JIT and AOT execute scalar/inline-copy class
payloads under FailFast and CheckedExceptions; checked constructor failure
releases the unpublished provisional control before catch recovery.

The selected agreement now executes local ordinary-reference descriptors in
the candidate 16-byte/8-aligned form with a zero support word for stack/inline
places. JIT/AOT regressions cover formation, Copy/rebind, reborrow, and
scalar/inline-Copy reads and writes without extending referent lifetime.
Support-bearing references to normal-backed class/box subplaces now capture
the selected control and exact field address. Each local descriptor residence
retains that control through Copy/rebind and releases it on lexical or checked
exception exit. Readonly and writable references to class-handle slots retain
the containing class, and writable stores preserve local roots and typed field
retirement. JIT/AOT regressions cover direct class fields, child-handle
selection versus the parent slot, class-handle slots and inline-Copy subplaces
under both runtime policies.

Stored ordinary reference descriptors are also implemented for normal-backed
class fields and inline Copy aggregates. Class-object provenance is distinct
from local handle-slot identity and follows copies, aliases, field updates and
constructor publication. Each typed descriptor leaf has its own support hold;
replacement stages new holds before releasing old values, construction
rollback drains unpublished fields in reverse acquisition order, and published
retirement releases descriptor supports iteratively. JIT/AOT tests cover direct
and nested class fields, multi-leaf aggregate Copy/rebind/replacement,
constructor self-reference failure, checked unwind and rejection of local
backing escapes under both runtime policies. Optional ordinary references now
support local values and typed leaves in normal-backed class/inline-Copy
storage under the selected agreement. Absence is both words zero; present
values retain/release their support through Copy, replacement, constructor
rollback and retirement. The selected JIT/AOT optional integration passes
15/15 with zero skips under both policies where applicable at
`artifacts\\tst\\Debug\\normal-backing-optional-integration-v6\\normal-backing-optional-integration-v6.trx`.
The source-bound direct nonforeign call ABI now stages required and optional
ordinary reference inputs by value and returns support-bearing values through hidden
16-byte storage. Reentrant frame-owned plans match generated entries, transfer
input holds without count changes, publish result support only after cleanup,
and settle pre-entry and checked-unwind failures. JIT/AOT qualification covers
source-order staging and rebinding, nested results, multi-leaf aggregate
inputs, backing retention after the source handle ends, later-argument failure,
checked unwind and plan reuse. The complete Debug `NormalBacking` selection
passes 65/65 with zero skips, including inline call-result reads through
`BoundReferenceRead` descriptors. Spill-root discovery now traverses
reference formation, reborrow and read descriptors to register nested call
results. The combined normal-backing, lowering-agreement,
dependency-source and checked-reference selection passes 117/117 with zero
skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-10-30_04_16_55_net11.0.trx`.
The latest focused Debug fast selection, including the complete NormalBacking
suite and related TRRM identity/anchor tests, passed 100/100 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-10-30_05_41_24_net11.0.trx`.
Optional ordinary references are included in direct formals/results and
descriptor-bearing aggregate inputs, including absent results. Optional
GC/init references remain unsupported.

Normal class and optional-class handle fields support retaining assignment,
replacement and typed iterative child retirement. The runtime tracks
constructor field acquisitions separately from publication and rolls partially
initialized objects back in reverse acquisition order. A 65-object nested
handle chain retires iteratively in both JIT and AOT tests, and a shared child
remains live after replacing one field. Cleanup, tasks, foreign/native-callable
routes, source grants, indexed semantic bodies, handle results, reference
output parameters and by-value handle arguments remain rejected;
`build-native-import` is also fail-closed. Arbitrary result/owner provenance,
module relowering and cross-instance exchange remain unimplemented; this is
not full normal-backing conformance.
The agreement is not persisted or accepted by module/graph/TRRM routes.

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
reused for GC. The unapproved proposal supplies provisional type/runtime
carriers and the RootPin draft profile supplies exact schema dispatch. The
test-only provider now covers stable cells, typed support residences,
cooperative mutator rendezvous, cross-thread object-graph marking and
transactional payload relocation, and exports the exact candidate provider
ABI. The synthetic JIT runtime-import path is partially implemented and
qualified, but source binding, independent producer/consumer execution, AOT
provider resolution, production collector integration, lowering and complete
qualification remain open. Never infer support from private formats or equal
version bytes.
Use unrelated synthetic classes/boxes and root/pin operations,
including bounded references that outlive a handle local but not a slot or
owner/guard endpoint, optional-root presence checks, failed acquisition with
no published root, early-release cleanup boundaries, and negative
sibling-place, stale-pointer and resource-lifetime cases. Include strong
cycles crossing normal and GC backing without assuming they are collectible;
weak handles, ownership domains and any joint collection policy remain
separate design work. Verify exact source and metadata identities through
separate compilation and both JIT/AOT paths; distinguish source binding,
emission, runtime behavior and profile admission.

<a id="cg-028"></a>
## CG-028: Nominal enum values and persistence

**Unapproved candidate; selected Standard persistence slices and a bounded
Windows x64 AOT ABI case.**
[Nominal enum persistence](../proposals/nominal-enum-persistence.md)
provisionally selects an exact `NominalEnumsDraft` profile with TypeDef kind
4, required `EnumDef`/`EnumMemberDef` sections `0053/0054`, Feature 18/1
closure over the scalar 11/1 carrier, int32 value bits and the existing
target-specific Layout commitment. It does not change default Standard or
normative registry owners.

**Current/evidence:** structural row codecs enforce complete TypeDef/member
ownership, ordered member snapshots, public access, base/Flags shape, default
Auto Layout and the exact supported Blittable target. A source-free provider
validator binds an imported enum TypeDef to its selected Export and compares
the exact enum/member snapshot and target Layout facts; missing providers,
changed names/keys/values/Flags, target mismatches and ambiguity reject. The
focused enum metadata/provider selection passed 73/73 with zero skips at
`artifacts\\tst\\Debug\\nominal-enum-provider-binding-final\\tagoo_GOODING-ZEN4_2026-10-28_15_00_08_net11.0.trx`.
A separate in-memory host-supplied `Sample.Palette` test emits and loads a
Windows x64 DLL for `Echo(Palette) -> Palette`; the Winapi int32 carrier
preserves `0x13572468` and `int32` minimum through both argument and result.
That supervised `NativeEmitter` ABI case passes 1/1 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-10-30_05_16_01_net11.0.trx`; the
complete `NativeCoreRegistrationTests` selection passed 63/63 with zero skips
at `artifacts\\tst\\tagoo_GOODING-ZEN4_2026-10-30_05_16_19_net11.0.trx`. This
does not validate source enum declaration parsing, general Standard producer
emission, replacement/packing, private-semantic revision 97, JIT, imported
enum calls, checked policy or other targets.

The public Standard source producer now selects only the exact existing
baseline or `NominalEnumsDraft` identity through `EmitStandard`; the CLI's
`compile-trrm --trrm-schema` option forwards the selected identity without
changing the default Development `Emit` route. The candidate producer covers
complete enum declaration libraries and bounded parameterless scalar/enum
constant-return libraries, including enum-free output with no Feature 18/1.
The current Standard baseline executable producer requires target
requirement 1.
Blittable enum declarations require target requirement 1; declaration and
constant-return output otherwise preserves explicit 0/1. The source-free
scalar-return execution helper remains Windows x64-only.
The Debug fast build passed; the related enum/API selection passed 21/21 with
zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-10-30_06_11_04_net11.0.trx`.
The selected CLI schema/default/error cases passed 24/24 with zero skips at
`artifacts\\tst\\tagoo_GOODING-ZEN4_2026-10-30_06_13_17_net11.0.trx`.
The analyzer-enabled Debug build remains blocked at 704 repository-wide
errors before test-project analysis; no diagnostic maps to the new public API,
CLI selector/parser or their test lines.
General nonconstant enum Bodies, source enum declaration production,
cross-provider replacement/remapping and private P+E remain open.

**Routes/gate:** carry `(complete enum identity, int32 value)` through
ConstantSource/FrozenValueNode, ConstantMap and TypeSpec use without
materializing the underlying int32 and retagging it. Preserve member
declaration keys and every Module's original provider expectation through
Standard imports, private P+E, sets/graphs, replacement and decoded
remapping. Qualify target representation and native calls separately from
table validation or PE emission. No member DefId, source annotation schema,
private revision fallback or general runtime ABI is implied.
