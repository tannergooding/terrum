# Standard TRRM empty fixed-array initialization

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This selects a concrete candidate for subsequent
implementation, not normative adoption, current compiler support or runtime
evidence. [Proposal index](README.md).

## Finding and recommendation

The exact experimental **A2** schema,
`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`, has a **semantic/wire
gap** for explicit initialization of an empty zero-length local. Its valid
array type and zero-extent layout do not establish a live value.

Recommend one narrowly scoped **nullary initialization alternative in the
existing SemanticStore record `05`**: `sourceKind=0=None`, `sourceIndex=0`,
`sourceUse=0`, `transition=1=Initialize`, into an initialization-authorized
local-rooted PlaceHandle whose exact fixed-array referent has **N=0**.
Reuse assigned PlaceSelection `04`, `D8`/`91` and `D9`/`92`; allocate no
instruction, record kind, type form, section, flag or Feature.

Select the amendment through the exact **A2E** successor below. Preserve
A2 unchanged rather than treating its existing selector as permission to
decode the new combination.

## Baseline and alternatives

The [specification index](../specs/README.md) reaches these owners:

| Evidence | Consequence |
|---|---|
| [HomeMap and state flow](../specs/il-instruction-set.md#body-row-layout-and-compact-implementations), [source initialization](../specs/language/initialization-and-flow.md#states-reads-and-initialization) | Role-3 Homes start vacant, including zero-data storage. TypeMap, layout, a register entry state and native stack materialization do not initialize a Home. |
| [Typed records](../specs/il-instruction-set.md#typed-operand-records), [places](../specs/il-instruction-set.md#fields-elements-and-ranges) | Home PlaceSelection with Initialize access is assigned, but selecting a place does not initialize it. None is explicit absence, not a default value. SemanticStore consumes a complete source value; its inherited source-use domain permits zero only for a result-only destination. A standalone store is not such a destination. |
| [Special operands](../specs/il-instruction-set.md#selected-special-operand-slots), [store meanings](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement) | R14/R15 do not construct an aggregate. Ordinary Home/place stores require a completed source; neither a zero bit pattern nor absence supplies one. |
| [A identity/layout](standard-trrm-fixed-array-identity-shape.md#complete-identity-and-exact-layout-row), [A2 Copy](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule), [source construction](standard-native-fixed-array-source-contract.md#proposed-initialization-and-publication) | N=0 is valid, still checks E, and needs explicit empty construction. Positive N needs exactly N expressions even at zero stride/extent. Neither an array row nor EC initializes it. |
| [`AllowsEmptyInitialization`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs), [source verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.NativeStorage.cs) | The source node admits an empty Elements collection exactly for N=0 and evaluates no elements. This is not a Standard Body encoding. |
| [Body reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyReader.cs), [record writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyRecordWriter.cs), [map validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs) | Record 05 currently rejects source kind/use zero. The array place-flow helper recognizes only `targetRefKind=3` element selections and clears facts for Home selections. Adding Home place flow is an implementation gap; assigning nullary store semantics is a separate contract decision. |
| [A2 producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs), [source-free reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs) | Positive-count scalar initialization uses explicit element D8/D9 operations. The producer rejects N=0 and empty Elements; the reader publishes an array only after its supported positive-count element sequence. There is no first element at N=0. |

An existing admitted call returning the exact array could supply a completed
value, but an authored empty construction supplies no such call. Inventing
a helper/provider call changes binding, effects and failure obligations;
DestinationPreparation cannot manufacture a call result. ConstantMap and
the abstract aggregate-family inventory supply no A2 fixed-array constant
or nullary construction codec.

| Alternative | Assessment |
|---|---|
| **Exact record-05 None/source-use exception — recommended** | Reuses explicit typed place/state carriers; gives absence one defined, type-bounded operation, not a universal default. No new wire allocation is needed. |
| New empty-construction instruction/record | Can combine type/destination selection into fewer bytes, but needs a new operation, operands, allocation and equivalent state rules. The byte saving does not supply a necessary semantic capability beyond the existing typed storage protocol. |
| General default/zero-size aggregate construction or frozen-value codec | Must define validity, constructors, dependencies and source effects for other types. Byte extent zero is not proof of nullary construction; this is unnecessary scope. |
| Elide a proven unobservable, effect-free local | A legal optimization only when all value/state/alias observations and retained dependencies permit removal. It can handle an unused local, not represent a retained initialization or justify making an omitted initializer live. Lowering an explicit logical operation to no machine bytes is different from omitting its semantics. |

## Exact schema and intended amendments

Propose exactly
**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1`**
with candidate selector
**`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft`**.
Its meaning is the complete original A2 composition plus only this document's
record-05 exception and associated semantics.

Keep `(1,0)`, stream implementation/codec `01`, all widths, maps, framing,
TypeSpec `11`, section `0012`, TypeDef Copy bit `0x20`, and F/S allocations.
Every Module retaining or using the exception or its array type/layout
requires `20/1 -> 2/1 -> 1/1`, selected Windows x64 TargetProfile `(1,1)`
and target requirement 1. A Copy-only declaration provider keeps its
independent `2/1` requirement; it does not acquire `20/1` from another Module.
Fields, queries, native calls and other retained uses keep their independent
requirements.
No new Feature revision, runtime policy or on-wire schema discriminator is
introduced. Check the [registry namespaces](../specs/representation/registry.md):
this changes one record's permitted combination, not its size or the
allocation/capacity of any instruction or record band.

**Intended supersession, only in A2E:** amend the instruction chapter's
SemanticStore source/value rule and inherited source-use restriction by the
exception below. None remains an absent selector; it does not select a value.
DestinationPreparation record `03` retains its result-only zero-use rule,
and all ordinary stores retain their existing source and transition domains.
The source proposal's exact-cardinality/publication contract and A2's
type/layout/Copy rules are preserved, not replaced by default construction.

Original A2, A1, Standard, F, F+S, A3 and other compositions are not widened.
An A3 combination would need its own explicit composition decision; it does
not inherit a later change to its A2 prerequisite text. Select A2E before
indexing, writing, binding, reconstruction or lowering. Each separately
supplied provider retains its independently selected supported schema; an
A2 type-only provider may satisfy A2E's unchanged type expectations.
Packing retained nullary stores requires a destination schema carrying this
exception. No feature sniffing, equal-version/name inference, decoder retry,
private fallback or obligation-erasing conversion is permitted. This uses
[exact draft agreement](../specs/representation/format-versioning.md), not
a compatibility obligation for unpublished A2 artifacts.

## Exact record and state semantics

All multibyte fields are little-endian. Record `05` retains its **20-byte**
extent, including the four-byte header:

```text
u8 recordKind=0x05, u8 flags=0, u16 reserved=0
u8 placeKind=1, u8 sourceKind=0, u8 transition=1, u8 sourceUse=0
u16 placeIndex=P, u16 sourceIndex=0
u32 dependencyContractRow=0
u8 hasFormal=0, u8 flags=0, u16 formalOrdinal=0
```

P must name R0..R13 holding an internal PlaceHandle, not a raw pointer,
array value or checked reference. Its complete referent is exactly
`(FixedInlineArray, complete normalized E, uint64 0)`. It has Initialize
access and a captured origin rooted in a role-3 Home of this activation,
either that Home itself or an independently admitted real field/array
subplace. A Home selector containing an array or reference is not an
alternative destination for this exception.

Resolve and validate the unique FixedArrayLayout for that referent in the
Body-owning Module/profile, including E's complete identity, positive Copy
evidence where required, rootlessness, independent layout, inline cycles,
stride/alignment and required alignment. Require N=0 and the valid derived
B=0; do not admit N>0 merely because S or B is zero. Nested E is checked
even though no E value is constructed. No stored initialized/unsafe bit or
serialized backing claim is added.

Executing this record performs **one explicit vacant-to-live initialization
of the exact empty array**. The destination must actually be vacant and have
the required initialization authority. A fixed Home need not be rebindable
to initialize once; rebindability does not itself establish vacancy or grant
additional containing/subplace authority. Live, maybe-live, partial or
reserved destinations cannot use this exception. All aliases observe the
same captured place/version and resulting state; register aliases do not
duplicate initialization rights.

There is no source read, element evaluation/acquisition, call, retirement,
payload/padding write, clearing, sentinel, allocation or lifetime extension.
The checked nullary operation has no initializer failure of its own.
Earlier target formation/checks and surrounding staging keep their actual
order, effects and failure edges; a failed selection never reaches this
initialization. Unsupported or malformed type/layout/binding input rejects
affected use, not as a new runtime initialization exception.

For a direct local, use the assigned **24-byte Home PlaceSelection `04`**:

```text
u8 recordKind=0x04, u8 flags=0, u16 reserved=0
u8 targetKind=1, u8 accessKind=3, u8 destinationKind=1, u8 baseKind=0
u16 destinationIndex=P, u16 baseIndex=0
u8 indexKind=0, u8 targetRefKind=0, u16 indexIndex=0
u32 targetRef=H
u16 referentTypeMapIndex=T, u16 flags=0
```

H is the zero-based HomeMap index, encoded in u32 with upper bits zero;
the Home has role 3 and its complete type matches TypeMap[T]. T uses the
existing Body kind-11 TypeSpec-row token, not a new pseudo-type. Apply this
selection with D8, then apply the store with D9 at its actual program
position. A source-free consumer needs no first element, fabricated index,
R14 aggregate value or initializer call. Preserve captured-place flow;
adjacent instructions or a particular local/statement name are not semantic
requirements.

Canonical writers use record-indexed short `91` when the selection index
fits its four-bit field and `92` when the store index fits u8; otherwise use
D8/D9 with full u16 record indexes and their assigned controls. D8's
destination must match P; D9 control is zero. Record/map index `0xFFFF`
remains usable with inventory count 65,536. Short CopyRead stores and the
implicit Home-place form are not new nullary aliases.

### Nested and positive-count zero-extent storage

Include **N=0 referents at permitted local-rooted subplaces**, not only root
Homes: a positive-count outer array can have empty arrays as its elements.
Each authored empty child initialization uses the same exception for that
child's exact N=0 type. Preserve nested capture order and the enclosing
intrinsic's private whole-construction authority. A vacant child inside
private construction is not an escaping readable prefix; completing one
child does not publish its ancestor. The parent becomes complete only after
all its required captures/installations succeed under the existing source
contract. Do not add an ordinary per-element initialization route into a
vacant source-visible parent.

The exception is **not** a constructor for a positive-count zero-extent
array. Such an array still requires N explicit expressions and their actual
effects/failures. For empty-array elements, initialize all N logical children;
for unit or other zero-sized elements, retain independently admitted typed
value/store operations. Missing support for those value carriers remains a
separate gate, never a reason to default the whole array.
Standalone empty values can use an explicitly initialized private Home and
independently admitted typed value flow; this proposes no new whole-array
register, call/result or frozen-value codec.

## Consumer, reconstruction, remapping and lowering obligations

Before affected use, consumers check the exact exception's closed fields,
record/instruction extents and indexes, typed PlaceHandle/origin domain,
complete referent/Module/layout relationships, supported requirements and
actual provider bindings. Source producers owe truthful vacancy, authority,
order and publication facts. Reconstruction retains those operation-derived
facts; [reader trust](../specs/representation/reader-trust.md) does not become
a requirement to re-prove arbitrary producer Body truth. Resource accounting
uses the finite type/place graph and actual records, never N expanded
elements or a success-shaped exhaustion fallback.

Reconstruct the explicit nullary initialization at the store position,
using the existing empty `BoundNativeFixedArrayInitialization` where
appropriate, with exact type/layout, selected place and enclosing authority.
An empty Elements collection is present and length zero, not a default/
missing collection. Preserve declaration versus later initialization,
captured aliases, state joins and private parent publication. A reconstructor
must not invent an initializer from an unused Home/layout row or recognize
only the positive-count emitter's statement sequence.

Rewriters remap Module/TypeSpec/TypeDef/Import/profile/layout rows, Body
TypeMap/HomeMap and field paths, captured registers, BlockEntryState and
record references in their own namespaces. Home PlaceSelection.targetRef
maps as a Home index; intrinsic element selections retain their parent
FixedArrayLayout row reference. Preserve original expectations and actual
provider origins even at N=0. **sourceIndex=0 is unused and never remapped**;
N/S/B/A remain quantities. Rebuild first-use maps/records, mapMask/counts,
record/block/code/section extents and instruction widths/ordinals, plus
affected SourceMap slots or legitimately discard optional source data.
Re-encoding to original A2 cannot silently erase the operation.

Lowering may emit no payload instruction for this initialized zero-extent
object while retaining its logical state, order, type and dependency facts.
Do not allocate a hidden native anchor or reinterpret a stack address as
eligible payload. [S's zero-extent backing exclusion](standard-native-storage-address-formation.md#source-region-and-failure-semantics)
remains: neither this live Home nor a live zero-sized element permits EC,
native payload borrowing, array decay, rooting or pinning.

## Required acceptance cases and exclusions

These are **future gates, not checks run by this documentation change**.
Existing [source zero/nested tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayStorageTests.cs)
and [positive A2 Body tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs)
do not establish this successor's support.

| Gate | Required outcome |
|---|---|
| Positive/state | Explicit N=0 initialization of ordinary, fixed-init and mutable-init locals preserves vacant-before/live-after state and rejects a second vacant-only initialization of live storage. Source-free reconstruction contains the actual empty initialization, not just a type/layout. Both short/long record forms preserve the same meaning. |
| Type/layout boundaries | Distinct scalar, pointer, unit, Copy nominal and nested E remain distinct at N=0. Still reject unsupported/unique/missing-Copy E, wrong actual provider, inline cycles and false geometry. Validate maximum child count/target arithmetic without expanding absent elements. Zero stride/extent with N>0 cannot use the exception. |
| Malformed/schema | Reject wrong selector/use/transition pairs, nonzero unused source index/formal/dependency/flags, truncated/trailing bytes, R14/R15 destinations, wrong carrier/origin/type/owner/profile or out-of-range maps. None remains invalid for ordinary stores and under original A2/other schemas; no fallback decoder. |
| Nested/cardinality/failure | Nested empty children require every positive outer element and private complete publication; underfill/overfill reject. Positive zero-data elements retain effects and throwing captures in source order. Failed earlier capture/target checks publish no child/parent and preserve earlier external effects; failure of a replacement RHS does not retire the old destination. This exception itself introduces no throwing initializer. |
| Transformation/binding | Permute Homes, type/layout/provider rows, registers and records across 15/16, 255/256 and 65535, with full counts through 65536. Source-free load/rewrite/re-emit/reload preserves the event, nested state, original/actual origins and unused zero fields. Changing E or required Copy/layout evidence cannot be hidden by equal zero extent. |
| Lowering | Headless source-free JIT/AOT checks use the final selected compiler/configuration/input and distinguish retained logical initialization from no machine payload. Reject zero-extent EC before address publication; create no default bytes, native backing, foreign call or hidden anchor. Unsupported state/value/failure carriers remain explicit qualification gaps. |

No normative specification or compiler code is changed by this proposal.
Approval of the exception and exact successor remains required outside the
authorized provisional workflow. General zero-sized nominal/unit construction,
live replacement through an absent source, checked-reference/nonlocal
destinations, global/static initialization, whole-array ABI/frozen values,
managed/non-Copy elements, additional targets and A3 composition are excluded.
Existing operations may cover some of these independently; this decision
neither activates them nor completes the broader array adoption milestone.
