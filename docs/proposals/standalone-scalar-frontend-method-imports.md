# Standalone scalar frontend method imports

**Status: unapproved decision candidate; provisional direction only under the
user-authorized missing-contract workflow, pending later review.** This
document approves no specification, compiler support, schema or runtime
behavior. No build, test or execution qualification is claimed.

## Finding and smallest decision

There is **no missing source `using` rule or logical TRRM Import contract**.
The missing decision is the standalone compiler's **raw dependency-input and
complete Terrum-owned call-node producer/carrier/consumer contract**. A target
tag alone does not supply a producer or replace the local-only v88 reader.
Existing C# reference objects and the v88 projection do not establish that
boundary.

Recommend one language-neutral input agreement,
**`terrum.experimental.standalone-method-imports/1`**, consisting of explicitly
schema-selected, owned `.trrm` artifacts and explicit Module/MethodDef export
selections. Each standalone compiler independently decodes and binds those
inputs. For the Terrum implementation, replace the successful standalone
source-call route with the [owned call-node pipeline](#replacement-terrum-owned-call-node-pipeline)
below: source binding produces typed calls, normalization preserves their
targets in explicit invocation records, and Terrum-owned decoding/lowering
consumes those records. Complete B+I `.trrm` is the only serialized carrier.
An imported expectation is never a local source frame.

The initial B+I-only operation selected the provisional
**`trrm/1-draft-init-reference-bodies-1`** identity, its complete catalog and
[initialization-call contract](standard-trrm-initialization-destination-callsite.md).
For complete predicate closure, that selection is superseded by the
[standalone graph-closure recommendation](standalone-published-predicate-graph-closure.md):
use the exact B+I+G identity on every artifact. The two identities are not
aliases and are not mixed or probed as fallbacks. This supersession changes
only schema selection for the complete graph operation; the raw-input
agreement, source domain, call-node pipeline and retained-call obligations
remain owned here. Both proposals remain unapproved.

The new agreement, its initial operation domain and every new record/policy
below remain **unapproved/provisional**. The initialization-Body prerequisite
is itself unapproved; this proposal does not adopt it.

### Evidence and terminology

These are inspected source facts, not executed results:

| Owner | Observation |
|---|---|
| [`ScalarFrontendProgram.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/ScalarFrontendProgram.terrum) | v88 contains 32 local frames with 64 per-node targets. `FindScalarProgramFunction` searches `Host.FrontendProgram[2]`; namespace inventory rejects token kind 5 (`using`). No dependency input or import inventory is defined. |
| [`ScalarFrontend.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/ScalarFrontend.terrum) | `ParseTreeSelfCall` obtains a local frame index through `FindScalarProgramFunction`, reads that frame's signature, and writes the index beside operation 105. `AddTreeNode` writes eight-word, 64-node `Host.Frontend` records; neither path can produce the proposed owned tagged call. |
| [`ScalarFrontendProgramReader.terrum`](../../sources/Terrum.CodeAnalysis.Terrum/Semantics/ScalarFrontendProgramReader.terrum) | `ScalarProgramReaderValidateFrame` bounds operation-105 targets by the local frame count. `ScalarProgramReaderValidateCall` reads that target's local header. The reader still consumes `Host.Source` and `Host.Tokens`. |
| [`ScalarFrontendHost.Program.cs`](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.Program.cs), [`ScalarFrontendHost.cs`](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.cs) | `ProgramReadContext.Callee` selects a local declaration; `BindLibraryCore` places `CallTargets` directly in `BoundCall`. These prototype entry points accept no dependency-reference context and still re-lex/audit original source. |
| [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs), [`CompilationReference`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs) | C# has exact-schema artifact inputs and source-reference factories. Their C# bound declarations are not a language-neutral standalone compiler input. |
| [Compiler boundary](../implementation/bootstrap-frontend-composition.md#compiler-boundary) | C# and Terrum implementations remain standalone. The transition fallback invokes a separate C# compiler on original source, not a Terrum AST or partial semantic projection. |

Here **source-free imports** means that provider contracts and, when needed,
provider Bodies are consumed without provider source. Consumer source remains
the source-binding input; after complete `.trrm` publication, decoding its
calls also requires neither consumer source nor its tokens. The test named
[`SourceFreeReaderAcceptsSiblingNamespaceAliasesAsync`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/Frontend/ScalarFrontendInvocationProfileTests.CallFrames.cs)
exercises namespace-scoped *type aliases*, not metadata imports or a
source-independent compiler. Extending that fixture is not standalone
acceptance.

The local-only v88 target representation and its missing `using` support are
implementation gaps. They do not justify another language import syntax, a
new Module encoding or a production C# projection adapter.

## Owners, preserved rules and alternatives

| Controlling owner | Preserved rule |
|---|---|
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [names and binding](../specs/language/names-and-binding.md#lexical-lookup-and-imports) | Lexical imports precede declarations in their own scope; namespace bodies/files do not share import scopes. Accessibility and ambiguity precede overload selection. |
| [Module linking](../specs/modules.md), [metadata row semantics](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Module ownership, ModuleRef/Import/Export identities, complete expectations and independent reference namespaces. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md) | Identity before directed comparison; every retained expectation, equality, using context and relied-on stronger fact survives replacement. |
| [Versioning](../specs/representation/format-versioning.md), [admission](../specs/representation/requirements-and-admission.md), [consumer verification](../specs/representation/reader-trust.md) | Exact selection before decoding; structural/support/binding checks before use; producer truth without mandatory consumer semantic re-proof. |
| [Calls](../specs/language/calls-and-inference.md), [references](../specs/language/references.md), [OutputState](../specs/representation/contract-clauses.md#5-outputstate) | Selected call identity, source-order evaluation, complete parameter types/modes, initialization reservations and per-invocation publication. |
| [Persistence](../specs/representation/persistence.md), [resources](../specs/representation/resource-accounting.md), [Windows x64](../specs/targets/windows-x64.md) | `.trrm` is semantic interchange; private compiler state is not another normative format; capacities and execution admission remain independent. |

No accepted specification text is superseded. This proposal supplies a
standalone input policy and internal preservation requirements, not a new
normative source or metadata rule. It does not amend sibling proposals.

| Alternative | Assessment |
|---|---|
| Add an import bank or fake bodyless frames to v88 | Smaller prototype patch, but perpetuates fixed banks, source spans and the forbidden production projection handoff. Not selected. |
| **Owned typed calls → normalized invocations → complete B+I `.trrm` → Terrum decoding/lowering** | **Recommended.** Raw artifacts/explicit roots supply imported contracts. Typed targets remain attached to the call throughout the replacement route; existing MethodMap/CallSite records carry serialized identity. No AST exchange, new metadata allocation or provider-source parsing. |
| Invent a compact scalar contract wire format or universal source-reference schema | Duplicates signatures, effects, output relations and remapping, or adds unrelated declaration-surface work. Neither is needed for this import decision. |

## Exact proposed input agreement

The enclosing compilation request retains its original ordered source inputs,
target/runtime/arithmetic selections, operation and independently selected Core
configuration. Add exactly these logical fields; they are **input values**,
not a C# object ABI or serialized semantic projection:

| Record | Required ordered fields |
|---|---|
| `StandaloneMethodImportInput` | `agreement: text`, `usingModuleIdentity: text`, `artifacts: ArtifactInput[]`, `methodRoots: MethodRoot[]` |
| `ArtifactInput` | `schemaIdentity: text`, `artifact: owned byte sequence` |
| `MethodRoot` | `artifactInputOrdinal: int32`, `moduleIdentity: text`, `exportKey: text` |

`agreement` is exactly the candidate literal above. `usingModuleIdentity` is
the explicit logical Module owning all original source inputs in this
operation, and must agree with any independently supplied output identity.
It is not a source path or provider selector. Arrays are required, nonnull and
ordered. Both may be empty for local-only compilation; a root requires an
artifact. A supplied artifact need not have a lookup root: it may provide only
transitive dependency closure. Capture the vector and immutable artifact bytes
before decoding; later caller mutation cannot change a result. Caller lists
and storage must not change during capture. Capture is not producer
authentication or a transaction across changing files.

Each root's zero-based ordinal selects **one whole artifact**, and its Module
identity selects one Module in that artifact. Resolve its key to one public,
provider-local module MethodDef, never an imported proxy, anchor, PropertyDef
or similarly named method. Reject repeated root triples. Distinct keys naming
one actual MethodDef are allowed as distinct callable views.

Build one unambiguous Module directory containing the source using Module
and all Modules in all supplied artifacts. Duplicate Module identities reject,
including a provider colliding with the source using identity or byte-identical
provider copies; a co-located Module needs no duplicate artifact. Resolve all
retained dependency edges against that directory. No filesystem/package search,
forwarding, first-provider preference or inferred dependency is added.
The source entry supplies ownership, not a metadata provider/export table:
pre-existing provider Imports must resolve to explicitly supplied artifact
Modules, not freshly authored source declarations.

Schema tokens compare ordinally, case-sensitively, without trimming or
component parsing. Module identities and keys use metadata's nonempty,
NUL-free, well-formed decoded-scalar text and ordinal equality, independent
of String encoding, path casing or Unicode normalization. Keys remain
opaque binding identities, not derived method names or native symbols.

The first operation admits target requirement **1** and runtime policy **0**
for participating Modules. A valid portable-target or other independently
defined runtime input is outside this bounded operation, not thereby
malformed. Preserve each Module's governing edition and complete Feature
closure. The request agreement does not select the artifact schema, Core
authority, source arithmetic defaults or a native ABI.

This is a language-neutral value contract. A host may supply the same raw
files/selections to either compiler; it may not replace them with C#-decoded
reference objects. No CLI option, JSON grammar, process ABI, invocation buffer
or `terrum.cli/1` revision is allocated here. Existing
[source-provider CLI transport](standard-trrm-source-provider-cli-transport.md)
and its [method/type version 3](standard-trrm-c1-method-type-source-provider-cli-transport.md)
retain their own exact schemas and dispatch. Their similar fields do not
authorize a new standalone command or B+I manifest pairing.

## Source exposure is not dependency identity

Keep these four concepts separate:

| Concept | Meaning in this recommendation |
|---|---|
| Source `using` | Existing lexical namespace-name lookup over the explicitly exposed declarations; it neither loads artifacts nor chooses a Module revision/key. |
| Namespace/type aliases | Namespace declarations compose containers. Accepted `alias` declarations are transparent type names, not namespace or Module renames. No namespace-alias syntax is added. |
| Logical Module import | A selected provider Module and complete retained expected declaration; internal or external placement does not change its meaning. |
| Metadata Import | Existing `moduleRef, exportKey, expectedLocalDefId` row, naming a consumer-owned bodyless expectation. It is not a source `using` or NativeImport. |

For nominated method roots, reuse the existing provisional
[method callable-view convention](standard-trrm-c1-source-reference-alias-publication.md#exact-sourceapi-and-publication-rules-for-b):
the exact selected export key is the source callable-view name. This is a
**scoped provisional exposure policy**, not reinterpretation of every
metadata key or replacement of its binding identity. Do not infer a namespace
from Module identity, provider filename or the actual MethodDef's private
implementation name.

Where the key is spellable as an accepted dotted identifier, its components
place the view in that source namespace and callable entry, retaining provider
ownership. Escaped keywords retain their decoded identifier identity. An
otherwise valid unspellable key grants no new call syntax; selecting it still
retains/checks its import. Qualification and `using` follow ordinary lookup.
Dependency-only declarations are not lookup roots.

Repeated paths to one view deduplicate by semantic view identity. Distinct
providers/views with colliding visible names retain ordinary ambiguity; array
order cannot choose them or combine unrelated imported overload groups.
Once selected, a call never retries another group after a type/effect error.

This first B+I operation has no ordinary parameter-label/default carrier.
It supports explicitly supplied positional inputs, not invented `argN` labels
or implicit defaults. A named-input/default-dependent source use is
unsupported unless its complete independently selected declaration-surface
agreement is available. Do not turn missing labels/defaults into an empty
success, infer them from provider source/Bodies/debug data, or broaden
[G1/C1 labels](standard-trrm-g1-interface-parameter-labels.md) or
[foreign surfaces](trrm-foreign-native-interface-parameter-surface-schema-selection.md)
to ordinary methods. This is not complete labeled source-reference support.

## Exact internal identities and retained facts

Construct these logical records **inside the compiler performing binding**.
They are not exchange records, private persistence assignments or additions
to v88:

| Record | Required fields |
|---|---|
| `LocalCallable` | `ownerModuleId: int32`, `callableId: int32`, `bodyId: int32` |
| `ImportedCallable` | `usingModuleId: int32`, `providerModuleId: int32`, `exportKey: owned text`, `expectedCallableId: int32`, `actualArtifactInputOrdinal: int32`, `actualModuleRow: u32`, `actualMethodDefRow: u32` |
| `CallTarget` | `kind: int32`, `index: int32` |

Module IDs index the admitted compilation-local Module directory, including
its source using Module. `expectedCallableId` indexes the compiler's common
callable-contract inventory, not its local source-frame array. Actual rows are
typed, one-based TRRM rows in the selected immutable artifact, within
`1..0xFF_FFFF` and their actual tables; they are not durable replacement
identities. The Import's Module/key plus its original expectation is retained
even after actual rows have been resolved. The expected callable belongs to
`usingModuleId`; the actual row triple must be the MethodDef selected by
`providerModuleId` and that exact Export key. Bounds, owner or snapshot
disagreement cannot be repaired by matching names.

`CallTarget.kind` is provisionally **0 absent, 1 local, 2 imported**.
Absent requires `index=-1` and is valid only for a noncall node. A call requires
kind 1 with `0 <= index < localFunctionCount`, or kind 2 with
`0 <= index < importedCallableCount`. Other tags/index combinations reject.
Local and import counts are independent; do not encode imports as
`localFunctionCount + ordinal`, negative local IDs or synthetic source frames.
Here the local inventory contains real implementations with an owner and Body,
not imported expectations. Source binding creates its entries from authored
local declarations. Source-free decoding creates real Body entries in their
actual metadata Module contexts; an admitted packed direct definition can
therefore retain its different owner without masquerading as a source frame.
An already imported target remains imported even if its provider Body is also
available to the same lowering operation.

The proposed shared `ResolveStandaloneCallTarget(program,target)` first
checks that target's own table bounds, then returns
`LocalCallable[index].callableId` for kind 1 or
`ImportedCallable[index].expectedCallableId` for kind 2. Kind 0/unknown tags
refuse a call. Resolve the returned contract in its retained owner/using
context. Source binding, both call validators, normalization and lowering use
this resolver; none dereferences an imported index as a local declaration.

Every expected callable retains the complete immutable declaration graph:
MethodDef kind/access/owner, MethodSig result/receiver/formals, ordered Params,
complete normalized TypeSpecs, all applicable Contract rows and their typed
subjects, governing rules/Features and every required dependency. New roots
capture that expectation from their selected provider view; provider-owned
pre-existing Imports retain their **original** expectations and using contexts.
An expected declaration has no authored Body or fabricated source span.

Use the existing metadata record shapes, not the projection's scalar nibbles
or `out`/init masks. In particular:

* Every logical parameter, including zero-data unit, keeps its ordinal.
  Param mode is **`00`**; historical `01` or other passing-mode bytes are
  invalid under the selected schema. Source output lifting is not an
  alternative metadata passing mode.
* Complete `ref<init T>` uses TypeSpec **`0E`**, with independently preserved
  outer write authority and closed scalar referent. It is neither a scalar
  by-value input nor an ordinary initialized reference/raw pointer. Unsupported
  qualifications, reference results, Span, aggregates, generics, receivers,
  native roles and richer contracts refuse this operation, without erasure.
* Effects clause **`0001`** retains mask bits **`02` DoesNotReturn** and
  **`04` caller-unsafe**; retired **`01` NoThrow** and unknown bits reject.
  Absence and explicit zero have their assigned meaning. Projection/source
  declaration flag bits are not metadata Effects bits.
* Initialization inputs retain complete OutputState **`0002`**, predicates,
  destinations and applicable Dependency **`0004`** relations under B+I.
  Default Boolean correlations and other-result completion use their owning
  rules, not a guessed success convention. Missing required support refuses.

Each nominated root creates a retained expectation, including an uncalled
root. Distinct keyed views have distinct expectations unless an already
retained relationship requires a shared expected declaration. Do not merge
views because their current actual MethodDef or signature is equal. Preserve
every pre-existing shared-expectation equality and exact owner correspondence.

## Replacement Terrum-owned call-node pipeline

The following are **proposed internal record/procedure names**, not existing
APIs, Terrum source syntax, a public object ABI or another persistence schema.
They replace the successful standalone call path; they are not an adapter
around `ParseTreeSelfCall`, `AddTreeNode`, operation 102/105 or a v88 target
bank. Those procedures/layouts remain isolated prototype fixtures.

### Owned records and lifetime

One immutable `StandaloneBoundProgram` owns the Module/binding snapshot,
complete callable/type contracts, `LocalCallable[]`, `ImportedCallable[]`,
and complete function Bodies. Common contract/type IDs are scoped to its
immutable binding catalog; local/imported indexes name this program's
corresponding callable inventories. Each source-bound function Body owns a
typed expression arena and argument ranges with Body-local IDs, not positions
in `Host.Frontend`. The call is a **typed DirectCall variant**, with this exact
payload:

| Record | Required ordered fields |
|---|---|
| `StandaloneDirectCall` | `target: CallTarget`, `expectedCallableId: int32`, `resultTypeId: int32`, `argumentFirst: int32`, `argumentCount: int32`, `locationId: int32` |
| `StandaloneCallArgument` | `expressionId: int32`, `parameterOrdinal: int32`, `sourceUse: u8` |

The enclosing Body supplies ownership; `expressionId` names an expression in
that same Body. The argument range is exclusively owned by that call and is
in **written source order**. Empty ranges use `(0,0)`; nonempty ranges have
checked nonnegative starts/counts and fit the actual argument inventory.
Every supplied formal occurs exactly once. This first call bridge admits fully
supplied calls: `argumentCount` equals the selected contract's logical
parameter count, including unit. Local named calls can carry a permutation
learned from authored local labels; imported calls remain positional under the
initial surface restriction. There is no packed four-argument map.
Each source invocation has its own expression occurrence and argument range.
Expression references are acyclic and cannot share an effectful call recipe
to erase or duplicate an invocation; function recursion is a target edge, not
an expression cycle.

`sourceUse` uses the existing CallSite input-use meanings, not Param passing
mode bytes: unit `0`, scalar copy/read `1`, and admitted initialization
destination `5` in this first domain. The expression retains its complete type
and actual value/descriptor formation; `5` does not invent vacancy, provenance
or authorization. Other source uses require their separately supported types.
`resultTypeId` names the selected declaration's complete result type, including
unit or a non-unit DoesNotReturn declaration. `expectedCallableId` must equal
the contract obtained by resolving `target`, not merely a matching signature.
`locationId=-1` means no diagnostic location; locations are nonsemantic and
cannot be used to recover target identity or argument mapping.

After call normalization/storage planning, an owned `StandaloneBodyProgram`
retains the same binding snapshot and complete contracts, explicit staged
instructions/CFG, and this **Invoke variant**. It retains the original immutable
contract catalog and local/imported index ordering, so target and expected
contract IDs remain unchanged. Its local Body references are rebuilt for the
new Body inventory; expression IDs are not reused as storage selectors:

| Record | Required ordered fields |
|---|---|
| `StandaloneInvoke` | `target: CallTarget`, `expectedCallableId: int32`, `inputFirst: int32`, `inputCount: int32`, `resultKind: u8`, `resultIndex: int32`, `preparationFirst: int32`, `preparationCount: int32`, `locationId: int32` |
| `StandaloneInvokeInput` | `selectorKind: u8`, `sourceUse: u8`, `selectorIndex: int32` |

Inputs are now **formal-order completed selectors**, not expression recipes.
Selector/result kinds and their register/Home meanings are exactly those of
[CallSite](../specs/il-instruction-set.md#typed-operand-records); None has index
zero. Registers obey their assigned roles, and Homes belong to this Body.
Preparation entries are the existing typed DestinationPreparation operations,
not arbitrary nodes. Fully supplied calls in this first bridge have no omitted
hidden outputs/defaults and use `(preparationFirst,preparationCount)=(0,0)`;
an input needing an unsupported preparation refuses rather than dropping it.
The count-sized internal representation does not widen Body-local wire limits.

Neither owned program contains borrowed token/Span backing as semantic state.
Freeze/publish it only after its complete selected source operation succeeds.
No source AST, either owned graph, partially bound declaration or intermediate
record buffer is handed to C#. These internal records have **no byte decoder
or file format**. The source-free byte carrier is complete `.trrm`, below.

### Source producer and normalization

1. **Inventory first.** A proposed Terrum-owned `BindStandaloneScalarSource`
   consumes original sources/tokens and the admitted import input. Its owned
   arena builder never writes `Host.Frontend`/`Host.FrontendProgram`, even as
   temporary call storage. Inventory local headers before Bodies, assigning
   local callable IDs in declaration order and complete contract IDs. Header
   inventory reserves Body IDs only for authored declarations with Bodies;
   all must be completed before publication. Imported roots get separate IDs
   and bodyless expected contracts from the Terrum metadata reader. All
   declarations are available for forward/self calls without fake frames.
2. **Resolve one typed candidate.** Its `BindStandaloneDirectCall` applies
   lexical/qualified lookup, access and applicability to local declarations
   and exposed imported views, returning **both** `CallTarget` and the exact
   expected callable ID. A local candidate is `(1,localId)`; an imported one
   is `(2,importId)`. No helper returns an untagged `callee` integer or reads a
   signature at `4 + callee * 780`. Self-recursion uses this same resolver.
3. **Bind every argument.** Bind/contextualize each written argument in source
   order against its mapped formal, retaining its typed expression, complete
   parameter ordinal and value/initialization use. Check authored source
   safety, initialization and dependencies inside this compiler. Append the
   entire argument vector, then construct `StandaloneDirectCall` atomically
   with its selected target, expected contract and result. A parse/type/budget
   failure constructs no successful call or partial program.
4. **Traverse and verify owned source IR.** Terrum-owned source verification
   checks the complete program and call obligations. It is not
   `ScalarFrontendHost.ReadProgram` or `ProgramReadContext.Callee`; it does not
   ask C# to reparse/audit consumer source. An imported contract is checked
   against its admitted provider binding, not by inspecting the provider Body
   to discover stronger source-call guarantees.
5. **Normalize once.** `NormalizeStandaloneCalls` emits argument
   evaluation/formation into explicit staged instructions on the appropriate
   CFG paths, once in written order. Preserve already staged values across
   later nested calls in registers or typed argument Homes. Only then permute
   those selectors into formal order and construct `StandaloneInvoke`,
   copying the target **and expected callable ID** unchanged. No later stage
   walks source spans to reconstruct argument order. A unit argument's
   expression is still evaluated; only its completed input uses None.
   A normal unit result uses None with normal continuation; DoesNotReturn
   uses None, ends its Block and has no normal successor. Required scalar
   output publication/loan facts remain part of the invocation contract.

The remaining expression/statement variants must likewise become complete
owned typed Bodies or supported normalized Body operations. A call-only
overlay on v88 is not a successful standalone program. Unsupported surrounding
syntax/control-flow/contract formation declines the whole operation; this
proposal does not claim that a new DirectCall type completes the compiler.

### Shared traversal and rewriting

`VisitStandaloneDirectCall` visits every argument expression in its stored
source order and the target/expected-contract dependency. The target is a
declaration edge, **not an expression child**: recursion does not recurse into
callee Bodies during ordinary expression traversal. Visit the complete
expected/import context separately under the dependency worklist rules.
`VisitStandaloneInvoke` visits target, expected contract, all completed input
selectors, result and every preparation, following explicit instruction/CFG
order rather than recreating source expressions.

`RewriteStandaloneDirectCall` and `RewriteStandaloneInvoke` require distinct
local-callable, imported-callable, common-contract, type and Body reference
maps. Dispatch on the target tag, remap its index in **that** table, remap the
expected callable ID consistently, rebuild complete argument/input ranges and
preserve owner/binding contexts. Never copy a call's other fields while
dropping/defaulting the target or map an imported ID through a local-function
array. Removing a node cannot drop retained import obligations.

An explicit packing rewrite may change imported to direct only after binding
to a real owner-qualified implementation and retaining every original
expectation/equality and stronger relied-on fact. Ordinary call normalization,
cloning or row renumbering has no such permission. The rewriter checks
target/expectation consistency before publishing its new immutable program.

### Standard carrier and source-free Terrum consumer

`WriteStandaloneScalarModule` consumes the complete owned normalized program
and writes one complete, schema-selected B+I `.trrm` artifact. It supplies
the Module/Feature/TypeSpec/MethodSig/Param/Contract graph, real local
MethodDefs/Bodies, consumer-owned expected MethodDefs and all retained
ModuleRefs/Imports. An expected MethodDef has `bodyRow=0`; it is not inserted
into the local Body inventory. Unsupported retained Body/contract encoding
refuses the whole artifact, never a call-only partial payload.
Bodies use the existing `implementationKind=01`, `instructionEncoding=01`,
actual present-map mask, checked counts/offset indexes and complete Block/code
streams. No v88 words, private call record tags or new schema prefix are
embedded in them.

The exact target mapping for each owning Body is:

| Owned target | MethodMap token written |
|---|---|
| `(1,localId)` | `0x1600_0000 \| methodDefRow`, for the real `LocalCallable` and its actual Module owner |
| `(2,importId)` | `0x1C00_0000 \| importRow`, for that exact retained expected view/key/using Module |

Allocate/remap global rows first. Both token rows are one-based low-24-bit
indexes in **different tables**. Canonical MethodMap ordering is first logical
use with identical exact tokens reused; aliases/contexts are not merged by
signature or actual Body. The call then uses existing **record `02=CallSite`**:
`targetKind=1` (MethodMap), `targetIndex` is that Body's zero-based MethodMap
index, `flags=0`, complete formal-order inputs and the mapped result/preparation
selectors. Its payload is exactly
`u8 targetKind, u8 resultKind, u16 flags, u16 targetIndex, u16 resultIndex,
u32 inputCount, u32 preparationCount`, followed by four-byte input rows
`u8 selectorKind, u8 sourceUse, u16 selectorIndex` and preparation indexes.
Use short **`90`** for record indexes `0..255`, otherwise long **`E0`** with
the `u16` record index and control zero. This first writer uses general calls,
not compact window calls. A serialized `targetKind=2` means **Register**,
never the internal Imported tag; copying the internal tag into CallSite is
an encoding bug.

`ReadStandaloneScalarModule` is a proposed **Terrum-owned** decoder accepting
only the exact artifact input, selected Module and admitted provider snapshots.
Its exact logical inputs are `input: ArtifactInput`,
`moduleIdentity: text`, `providers: ArtifactInput[]`; capture the effective
artifact vector as `[input, ...providers]` in that order. Select the root Module
only in `input`, with the same schema preflight and unambiguous directory rules
as source imports. Decoded actual-artifact ordinals refer to this captured
vector, not to stale ordinals from the producing compilation.
It reads the standard Module/declaration/contract/Body records, never
`Host.Source`, `Host.Tokens`, v88 frames or a C# source inventory. Reconstruct
real Body entries and common contract IDs, plus an Import-row-to-imported-ID
map preserving original expectations and shared-expected-definition groups.
The selected root owner comes from its Module row; artifact-only decoding adds
no fresh source Module with the same identity. The new owned program has its
own IDs, with explicit maps from the original artifact rows.
Decode direct calls in this order:

1. Resolve `90`/`E0` to the bounded CallSite `02` record. Validate its shape,
   target/result/selector namespaces, formal count and required support.
2. For `targetKind=1`, resolve its bounded **MethodMap** index and token kind.
   A real MethodDef token maps to `(1,realBodyEntryId)` in its metadata owner
   context. An Import token must expect a MethodDef in the Body's using Module
   and maps to `(2,importedId)` through that **Import row**, not the expected
   MethodDef row or actual provider's local index. Other target kinds require
   separately admitted callable-value support; this bridge refuses them.
3. A direct MethodDef token naming an imported expected declaration cannot
   bypass its Imports. Retain/check the whole shared-expected group, requiring
   every edge to select one identical actual definition. Use its lowest
   original Import-row ordinal solely as an internal representative and map
   to that imported ID; retain **all** group rows/keys. This chooses no
   provider, drops no alias and grants no new source-view naming policy.
   A bodyless declaration without an admitted import or callable role refuses.
4. Set `expectedCallableId` from that exact retained declaration/view; validate
   complete referenced input/result types and applicable contracts, then
   construct `StandaloneInvoke` with its formal-order completed selectors and
   explicit surrounding staged instructions/CFG. Source labels, original
   expression nesting and written argument order are **not reconstructed**:
   the standard code already carries the evaluation sequence.

Mandatory source-free checks establish safe structure, support and binding;
they do not re-prove authored source choices or arbitrary producer Body truth.
A same-signature token substitution can describe a different valid program.
The decoder must decode that different identity, not pretend it can detect
the old source target from debug names. Source producer/rewriter tests enforce
preservation of the original selected identity.

### Lowering and boundary

`LowerStandaloneBodyProgram` consumes normalized invocations from either
source normalization or this Terrum decoder. Build an explicit target plan:
local IDs map to emitted code for their real owner/Body; imported IDs map
through the admitted actual artifact/Module/MethodDef triple to separately
decoded provider Bodies and retained code/load origins. Check the invocation's
original expected contract before using that plan. Never lower an imported ID
as `Functions[index]`, look up a symbol by source spelling, or append a
bodyless local stub. Provider Bodies are implementation inputs, not inferred
callee-contract guarantees.

Successful standalone compilation uses the Terrum-owned binder, verification,
normalizer, writer/decoder when requested, and lowering path. C# is neither its
source auditor nor a consumer of either intermediate graph. Independent
consumption of a **complete normative `.trrm` artifact** remains ordinary
semantic interchange; it is not permission to route the transition compiler
through a C# partial-semantic frontend adapter.

The call-target bridge requires **no CLI/process transport decision**:
typed calls stay inside one Terrum compiler operation and `.trrm` supplies
the complete serialized carrier. Original-source fallback remains separate;
its inability to preserve the selected raw inputs is still an unsupported
operation, not authorization for a new intermediate handoff.

## Binding, verification, lowering and replacement

1. Preflight the request and **every artifact's exact schema** before
   schema-dependent indexing, including unused inputs. Validate framing,
   checked table/range products, Strings, ownership/reference namespaces,
   supported records and per-Module admission. Validate the complete retained
   supplied dependency closure, not only calls found in consumer source.
2. Resolve Module/key/kind before comparing each expected contract against
   its selected actual declaration. Compare complete result/parameter types,
   ordered modes, callable role, authority, output states, dependencies and
   all retained obligations under
   [directed satisfaction](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions).
   Safe-to-unsafe replacement fails; dropping caller-unsafety can satisfy an
   unsafe expectation. Removing expected DoesNotReturn fails; adding it is
   permitted only where other promised outcomes remain satisfied. Masks are
   not numerically ordered.
3. Use the owned source producer above to bind against the selected
   **expected view**, with source-order argument staging and explicit
   source-to-formal mapping.
   Check complete types and initialization/loan state before invocation.
   Caller-unsafe requires the source call's explicit unsafe authorization;
   an unsafe declaration does not authorize its own body. No input trust bit
   or successful binding authorizes unsafe source operations.
4. The owned call/Invoke traversals and rewrites preserve the target tag,
   expectation, using context, arguments, publication and cleanup. Consume
   published callee contracts, not facts inferred from callee Bodies. Check
   authored consumer source semantically; provider Body truth remains its
   producer's responsibility. Structurally checked metadata is not a semantic
   audit certificate.
5. Use the owned normalized target plan above to lower local/import IDs to
   admitted implementations. Metadata-only reference admission does not establish
   executable support: semantic joint lowering requires the selected real
   provider Bodies, their complete supported closure and independently
   selected target-lowering admission. Consumer source arithmetic selection
   never re-profiles stored provider operations. Missing code is not
   a zero-return stub, native symbol search or implicit JIT fallback.
   Precompiled native composition remains a separate ABI/deployment route.
6. Use the complete Standard carrier above to publish consumer-owned bodyless
   expected MethodDefs and existing **ModuleRef/Import** rows. Remap common callable IDs
   to typed metadata references, and calls to existing MethodMap
   MethodDef/Import tokens and CallSite records. Do not serialize `CallTarget`
   tags or internal IDs as metadata tokens. Imported expectations are not
   Export targets; required manifests and original contracts survive.
7. Replacement resolves the same retained Module/key against a newly selected
   snapshot and rechecks every obligation. Row relocation is compatible when
   complete mappings/contracts remain valid; equal row numbers, bodies,
   names, digests or a cached success do not establish compatibility.
   Re-emission/packing rebuild Module partitions, ModuleRefs, expected
   MethodDefs, MethodSig/Param/TypeSpec/Contract graphs, Imports/Exports,
   Body maps/records/indexes and affected SourceMaps together. Clone shared
   structural rows when using contexts differ. No byte substitution suffices.

Preserve stronger provider facts actually relied on as additional dependency
obligations; do not overwrite the caller's original expectation with them.
An Import-to-direct rewrite or elimination of a call cannot discard surviving
contract, identity, publication, target, code/load or cleanup obligations.
Declaration binding runs no provider initialization or code.

## Resources, failures and original-source fallback

In-memory counts and IDs use validated nonnegative **`int32`** domains, with
`-1` only for explicit absence. Allocate local frames, imported declarations,
call targets and argument/parameter maps from validated counts. Do not impose
32 functions, 64 nodes, four parameters, bitmask width or sample-name limits.
Source/token spans retain their own `nint`/source-coordinate contracts.

Before allocation, check count sums, products and extents without wrapping.
Keep metadata's independent one-based **24-bit per-table row** domain and
Body-local **65,536-entry/u16-index** bounds; in-memory capacity does not widen
either encoding. Reserve source, artifact backing, decoded contracts, graph
state, remapping tables and output staging under the operation's aggregate
work/live-state/depth envelope. Nested provider comparisons do not receive
fresh root budgets. Reuse only completed identical-context decisions; visiting
a cycle cannot mark an unchecked edge satisfied.

Use the existing
[resource-accounting recipes](../specs/representation/resource-accounting.md#5-deterministic-work-depth-and-audit-recipes)
for actual field/reference/list actions, text scans/copies and graph
transitions. Numerical portable minima remain unassigned. Declared lower
implementation capacity explicitly declines the operation; it cannot turn a
valid large artifact into malformed bytes, omit roots or approve partial
closure. No new universal parser/verifier budget is selected.

Malformed selection/artifacts, missing or incompatible binding, unsupported
schema/contract/target, deterministic capacity exhaustion and host I/O/memory
failure remain distinguishable. Publish no reference, program or executable
until its required operation succeeds; failure preserves existing output.

Unsupported consumer source may use the existing **whole-original-source,
separate-process C# fallback**, with the same ordered original sources, raw
dependency snapshots/selections, using Module and compilation configuration.
The C# compiler independently decodes and binds them. Never pass a Terrum
AST, partial bound program, resolved contract table or projected host buffers.
If the fallback route cannot preserve those inputs or support this agreement,
report unsupported rather than compile with imports dropped. Malformed input
or incompatible dependencies cannot be hidden by retrying source lookup,
another schema, provider or compiler.

Span/token views remain compilation-local. This agreement persists no Span
and makes no bootstrap, self-sustaining-compiler or production projection claim.

## Required acceptance cases

These are future gates, **not tests run for this draft**. Exercise the actual
standalone source-to-result route and public compiler/Standard interfaces;
prototype buffer tests alone do not meet them.

| Area | Positive / negative and boundary cases |
|---|---|
| Raw input and isolation | Bind without provider source, C# `CompilationReference` or Terrum projection handoff. Local-only empty inputs succeed. Reject unknown agreement, missing/null fields, ordinal overflow/out-of-range and incompatible schema selections before decoding, including an unused provider. |
| Identity and exposure | Use two unrelated synthetic provider Modules, explicit keys different from implementation names, co-located Modules, dependency-only Modules and multiple roots. Missing key/wrong kind/private or imported Export rejects; same-shaped methods elsewhere do not substitute. Duplicate Module snapshots reject. Case/normalization distinctions and alternate String encodings follow metadata equality. |
| Lexical imports and aliases | Compare qualified calls and `using` within file, file-scoped and sibling/nested block namespaces. Type aliases do not select provider Modules. Repeated paths deduplicate; colliding unrelated visible groups diagnose regardless of root/provider order. Unspellable keys grant no extra syntax. |
| Local versus imported targets | Mixed local/imported calls, forward local calls, recursion and imports at matching numeric indexes retain distinct targets. Reject absent targets on calls, imported IDs used as local frames, unknown tags and every out-of-range combination. |
| Signatures and modes | Exact scalar identity, unit positions and positional source-order mapping survive reload. Reject equal-width signed/unsigned substitution, dropped unit parameters, changed result/arity/receiver, legacy mode bytes and unsupported qualifications. Missing labels/defaults never expose invented `argN` calls. |
| Effects and publication | Caller-unsafe requires explicit authorization, including inside unsafe declarations. Exercise valid/invalid directed Effects changes and retired/unknown bits. Test scalar init outputs on each admitted normal result, saved result correlation, failure vacancy, forwarding/reborrow and destination invalidation; reject changed output ordinal/state/guard/dependency. |
| Closure and replacement | Chains, diamonds, finite cycles, uncalled retained Imports and shared expectations all check completely. Relocate Module/MethodDef/Param/Contract/Import/Export rows and Body maps; unchanged consumers bind by key. Incompatible signature/effects/state changes and split retained equality reject. Independent keyed views do not acquire an unencoded equality. |
| Transformation and execution | Source-free reload/re-emission/packing preserves original expectations, exact types, call targets, maps and source associations. Joint lowering executes real admitted provider Bodies under existing headless supervision; declaration binding and emission alone are not execution. Missing executable closure rejects without a stub. |
| Resources and fallback | Cross former 32/64/four-parameter prototype bounds; test exactly-at-limit versus exhausted declared capacity and independent metadata/Body limits. No truncated closure or partial output. Unsupported source falls back with original inputs intact; malformed bindings do not fall back. Private Span persistence still rejects. |

### Concrete call-bridge regression vectors

These required vectors distinguish source production, internal consistency,
metadata decoding and execution evidence. Use real admitted scalar provider
Bodies and the actual new Terrum stages, not fabricated v88 output or C#
source reconstruction.

| Vector | Positive case | Negative / identity-preservation case |
|---|---|---|
| SCT-01 source target production | Inventory a local zero-input `int32` helper first (`localId=0`, result 11) and one imported zero-input view (`importId=0`, provider result 29). A local consumer adds both calls. Assert producer records `(1,0)` and `(2,0)` with their distinct expected callable IDs; both normalized and decoded lowering return 40. | Change the imported owned call to `(1,0)` but leave its expected callable ID intact: owned validation rejects the target/expectation disagreement despite identical signatures. A buggy producer/rewriter that changes both fails the original-target regression and produces 22, not a compatible rewrite. |
| SCT-02 independent inventories | With two source functions and four selected imported views, bind a call to `(2,3)` and resolve only `ImportedCallable[3]`; it is valid although index 3 exceeds the local inventory. | Reject `(1,3)`, absent/unknown tags, negative call indexes and each table's exactly-one-past-end index. Imported index 3 must never read local frame 3 or a synthetic bodyless function. |
| SCT-03 complete traversal | A five-input local call contains nested calls in all five argument expressions, with the fifth targeting an import. Visit all six call occurrences, including the outer target, in the stored source-argument order; clone/remap local, import, contract and expression IDs independently. | Dropping the fifth argument/target, preserving only legacy Left/Right/Low/Fourth fields, using the local map for an imported index or creating an expression cycle rejects or fails the traversal/identity regression. A recursive callee does not cause expression traversal to enter its Body. |
| SCT-04 stage versus formal order | A local two-input function computes `first*10+second`. Bind a local named call with `second` supplied by the result-11 local helper before `first` supplied by the result-29 import. Assert staged Local then Imported invocations, formal inputs 29 then 11, and result 301. Also stage a unit-returning call used as a unit argument before its None input. | Formal-order normalization must not reverse evaluation, reevaluate an argument or drop the unit invocation. Truncating the formal vector, duplicating an ordinal or replacing an init descriptor with a scalar Home rejects. |
| SCT-05 source-free carrier | Write SCT-01 as complete B+I `.trrm`, discard consumer/provider sources and tokens, omit optional SourceMaps, and decode only artifact bytes/context. Assert MethodMap `16` versus `1C`, exact Module/key/expected-view identity and Invoke target reconstruction. Arrange CallSite record indexes 255 and 256 to exercise `90` and `E0`. | Reject wrong MethodMap/record indexes or token kinds, an Import expecting TypeDef/PropertyDef, wrong Import using Module, unknown required flags/records and unsupported target modes. Both internal target tags must serialize as CallSite `targetKind=1`; serializing Imported as `2=Register` is not an import encoding. |
| SCT-06 retained expected groups | Decode an Import-token call and a direct expected-MethodDef-token call to one expectation shared by two retained alias Imports. Check both keys bind to one actual MethodDef; the direct form uses the internal representative while retaining both edges. | Missing the nonrepresentative key, splitting the actual targets or discarding one group edge rejects. A bodyless direct MethodDef with no admitted import/callable role cannot become a LocalCallable. Reordering Import rows may change the internal representative, not the complete retained group or selected actual identity. |
| SCT-07 decoding is not source re-proof | Deliberately change SCT-01's serialized imported MethodMap token to the same-signature local MethodDef token, updating otherwise valid references. The source-free decoder reports the different local target; the resulting different program returns 22. | Do not claim the decoder can reject this structurally valid semantic change by guessing original source names. Original-source producer/rewriter assertions must detect the lost imported dependency; malformed target/owner/reference mutations still refuse safe consumption. |
| SCT-08 relocation and lowering | Permute MethodDef, Import, MethodMap and CallSite rows with complete remapping, reload source-free, and compare SCT-01's normalized and decoded native plans/results. Include a real owner-qualified Body in a second co-located Module and retain its actual owner when decoding an admitted packed direct reference. | Missing selected provider code refuses lowering without a stub or source-name lookup. Incompatible replacement rejects against the original expectation. An import whose provider Body is available must not silently become a direct local target during normalization. |
| SCT-09 compiler separation | Successful source binding uses only Terrum-owned typed arenas; successful artifact decoding/lowering needs neither consumer text nor the legacy projection. Test harness entry points for the C# projection/source-audit adapter are not invoked. | Reject successful standalone results that require v88 publication, `ProgramReadContext` or C# reparsing; absence of source must not trigger an AST/partial-semantic handoff. Unsupported source may use only the distinct original-source fallback, with inputs intact. |

## Integration and review decisions

Implementation owns the standalone dependency reader/directory, typed source
call producer/arenas, complete callable contracts, separate local/import IDs,
source lookup, both complete traversal/rewrite variants, call normalization,
the Terrum Standard carrier reader/writer and native target plan. Those stages
implement the existing expected-definition, Import, Body-map and remapping
obligations; a C# adapter is not their implementation substitute.
Keep the legacy v87/v88 prototype and its C# audit isolated; neither is the
production integration owner. No implementation inventory, plan or accepted
specification is changed by this draft.

Later review must decide whether to approve:

* This raw-input agreement and scoped export-key callable-view convention,
  versus an independently designed declaration-name exposure contract.
* B+I-only, target-1/runtime-0 initial admission, positional-only label-free
  uses, and retaining each explicitly selected root expectation.
* The complete owned DirectCall/Invoke replacement path, internal target tag
  values, Standard MethodMap/CallSite carrier and source-free expected-group
  representative rule, rather than an abstract tag added beside v88.

The concrete recommendation above is provisionally actionable; these are
approval choices, not permission for an implementer to guess alternatives.
Broader named/defaulted ordinary methods require a genuine complete surface
carrier; native imports, generic/nominal/Span contracts and precompiled
deployment retain their independently owned decisions. No unresolved
CLI/process choice is needed for this internal/Standard call-target bridge,
and no standalone CLI transport is selected. Approval of the concrete choices
above remains a user decision; implementation must not invent a different
carrier or use the prototype projection to fill a missing stage.
