# Standard TRRM aggregate-local layout integration

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract direction.** This is an integration amendment, not accepted
source syntax, a normative schema allocation, compiler support or runtime
qualification. No implementation or existing specification/proposal is changed.

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.” Formal review and approval remain outstanding.

## Decision

Recommend **L1**, one explicitly selected successor to the existing
**A2R1+Repeat** profile, adding only the aggregate-local integration below:

* Normalize a locally implemented source constructor into an ordinary checked
  value-returning MethodDef with private construction storage. Retain its real
  operations, field states, calls, checks and normal-result publication.
* Use existing typed Homes, field/element places, semantic stores, calls and
  returns, including values larger than sixteen bytes. Add no aggregate-value
  opcode or second constructor/body model.
* Extend the existing nullary initialization store to an **instance-field-free
  Sequential Copy aggregate**, explicitly and only in this new schema.
  Declaration, padding allocation and zeroing still do not initialize a value.
* Preserve declared Layout inputs and target-derived geometry through those
  Bodies, source-free reconstruction, rewriting and native lowering.
  Materialize every addressable object and result at its required alignment;
  preserve exact packed accesses, array stride, probing and recoverable frames.

The new exact candidate identity is
**`trrm/1-draft-local-aggregate-layout-execution-1`**; the proposed internal
selector is **`TrRmArtifactSchema.LocalAggregateLayoutDraft`**.
Both are unapproved. No existing selector changes meaning.

This makes a standalone Standard program with an ordinary aggregate
constructor, helpers and local arrays a first-class producer/consumer case.
It requires **neither a NativeImport nor a function-pointer signature nor a
NativeAddress operation**. Retain the current route's fail-fast execution
policy; a failing checked access remains terminal under its admitted policy.
The [E1 proposal](standard-trrm-register-body-exception-regions.md) already
owns register-Body exception transport. Do not duplicate its contract or
implicitly compose it merely to execute successful packed/aligned programs.

## Evidence and classification

The [specification index](../specs/README.md) reaches the accepted owners.
These are inspected source/document facts, not newly executed tests.

| Evidence | Consequence |
|---|---|
| [Windows layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Exact registered schemas, normalized Packing, declared Alignment, transitive explicit alignment, nested placement/stride and safe packed access are already accepted. Missing executable transport does not authorize different layout rules. |
| [Body values and storage](../specs/il-instruction-set.md#names-values-and-storage), [typed records](../specs/il-instruction-set.md#typed-operand-records), [storage](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement) | One logical register may contain a large aggregate. Homes start in their specified states; records 04/05 already distinguish construction, replacement and captured places. |
| [Aggregate inventory](../specs/il-instruction-set.md#semantic-family-inventory) and [aggregate semantics](../specs/il-instruction-set.md#optional-values-aggregates-and-queries) | A dedicated aggregate-construction operation is unassigned. This does not forbid ordinary checked calls and structural construction stores; it does forbid inventing an aggregate opcode, constant or R14 default. Empty Sequential completion needs the explicit applicability decision below. |
| [`EmitStandardCore` and `EmitDevelopment`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L170-L399) | B and E1 source production enter the shared writer. Its admission permits executable scalar bodies or declaration-only Blittable libraries. The [later declaration check](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L662-L686) independently rejects constructors, struct members, non-scalar results and non-scalar storage. Removing only the first guard is incomplete. |
| [`EmitForeignInvocationDraftNativeAddressLibrary`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs#L100-L209) | The executable A2R1+Repeat route requires FailFast, exactly one pointer signature/formation, one caller and direct NativeImport declarations. Another local constructor Body is rejected. These are route restrictions, not a general Standard Body contract. |
| [`BoundStruct`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs#L593-L644), [`BoundFunction`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs#L804-L805), [`ModuleCompiler`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [Standard layout reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmLayoutTableReader.cs)/[writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmLayoutTableWriter.cs) | The working tree has Pack/declared Alignment and completed/required alignment facts; private fixed-array revision 103 persists declared inputs. Private persistence is not Standard interchange or executable support. |
| [`ValueLayout`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs), [`aligned prolog`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs#L5841-L5893), [`NativeUnwind`](../../sources/Terrum.CodeAnalysis/Native/NativeUnwind.cs) | Existing 16-byte value, 128-byte address-storage and bounded struct/frame representations are implementation boundaries, not target limits. There is already aligned-frame/probe/unwind machinery to integrate rather than replace with a sample path. |
| [`NativeFrameTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Unwind/NativeFrameTests.cs#L154-L270), [`TrRmFixedArrayLayoutTableTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs#L157-L210) | The working tree contains source-native packed/aligned frame cases and Standard type/layout round-trip cases. Neither establishes the missing source-free executable route; no pass result is claimed here. |
| [`NativeModuleCompiler.StandardTrRm`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.StandardTrRm.cs#L13-L53), [provider-native proposal](standard-trrm-provider-native-lowering.md#decision-and-scope) | The native facade consumes an F resolved-binding plan. It is not a general aggregate-Body loader or an authorization to discard a provider marker. |

Most of the gap is **implementation integration**, not undecided layout.
The decisions here are the exact successor profile, the explicit constructor
normalization/publication contract and the empty-Sequential store admission.
The existing [E1 proposal](standard-trrm-register-body-exception-regions.md)
already owns the general register-Body exception contract; duplicating it
would be incorrect.

## Ownership and preserved clauses

| Owner | Disposition |
|---|---|
| [Windows data layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Preserve without amendment: exact schema identity; Packing 1/2/4/8/16, omitted 16; supplied power-of-two Alignment 1..8192 without weakening derived alignment; nested explicit requirements; empty-storage, Transparent and Union rules; checked target-nuint geometry; padding, safe unaligned accesses and address obligations. The 8192 bound is the accepted **declared alignment** bound, not a new object/frame-size cap. |
| [Call interface §§2–4](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults), [deployment §3](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support) | Preserve aggregate classification, independent ABI copies, object-aligned result buffers, incoming-copy alignment distinction, shadow space, call alignment, stack probing, unwind and code-lifetime requirements. No new independently replaceable Terrum ABI. |
| [Initialization](../specs/language/initialization-and-flow.md#states-reads-and-initialization), [construction/ownership](../specs/language/ownership-and-cleanup.md#construction-and-publication) | Preserve vacancy, partial construction, actual field state, validation, staging, failed-RHS effects and complete-only publication. No source permission is inferred from a normalized Body. |
| [Fixed-array source contract](standard-native-fixed-array-source-contract.md#proposed-initialization-and-publication), [A2 identity/geometry](standard-trrm-fixed-array-identity-shape.md), [Copy evidence](standard-trrm-rootless-copy-type-evidence.md), [repeat owner](standard-native-fixed-array-repeat-initialization.md#exact-standard-successor) | Reuse their provisional type, source, initialization, Copy and place contracts, including field-rooted/interleaved paths and RepeatCopy. No second array syntax, codec or array-call ABI. |
| [Raw Union/ReturnCoverage](standard-trrm-raw-union-initialization.md), [CopyOrigin](reference-copy-origin-trrm.md) | Retain their existing selected meanings and original/provider obligations. Ordinary aggregate construction supplies no stronger raw-Union coverage promise. |
| [E1](standard-trrm-register-body-exception-regions.md#exact-schema-composition-and-prerequisites) | Remains the owner of the unapproved register-Body exception contract, not an aggregate-construction codec. Preserve its B+E1 selection and noncomposition rules. L1 adds no directory, event transport, catch/finally or exception policy. |
| [Profile selection](trrm-draft-schema-profile-selection.md), [versioning](../specs/representation/format-versioning.md), [registry](../specs/representation/registry.md) | One exact schema/catalog per artifact, selected out of band before decoding. Draft IDs may be reassigned; no compatibility reader, alias, numeric roll-forward or equal-version inference. |
| [Consumer verification](../specs/representation/reader-trust.md), [replacement](../specs/representation/binding-and-replacement.md) | Mandatory safe-consumption/support/binding checks and producer truth remain distinct. Preserve original expectations and every stronger relied-on fact; no serialized “verified” authorization. |

On approval, the instruction owner would add the bounded construction
normalization and record-05 applicability below, qualifying—not assigning—a
dedicated aggregate-construction instruction. Its map/state projections and
registry applicability must agree. Metadata Layout/FieldDef and target layout
equations need **no replacement**. Reconcile this exact composition with the
profile owners; do not broaden their old identities or move their semantics
into this document. No such normative or proposal edits are made now.

## Material alternatives

| Choice | Assessment |
|---|---|
| **Minimal successor: A2R1+Repeat plus aggregate-local stores/calls — recommended** | Makes the required successful source-free packed/aligned JIT/AOT runtime gate achievable under the existing fail-fast policy. Terminal negative execution and independent OS frame-recovery probes remain testable. It needs no new exception payload/runtime/identity contract. |
| Integrated revision composing E1 as well | Materially different: permits caught checked failures and actual language unwinding across aligned frames. It requires a new complete composition, E1's runtime/payload support and explicit interoperability for the [Core identity owner's E1-only operation](standard-trrm-core-exception-identity.md#explicit-provider-schema-boundary). That is not necessary for this layout runtime gate; do not silently select it. |
| New general aggregate opcode, constructor-specific bytecode or trusted preinitialized Home | Unnecessary for nonempty structural construction; adds an independent state/publication model or bypasses verification. Not selected. |
| Relax the NativeAddress route, add a dummy import/signature, or use the F native plan unchanged | Does not define general local MethodDef/Body ownership and reconstruction. A dummy native operation changes the program and retains source-pattern restrictions. Not selected. |

Only the recommended profile is proposed. It does not qualify source-free
catches or close E1's independent runtime gate. Accurate Windows unwind data
is nevertheless required by the accepted target; fail-fast is not permission
to omit it. There is no integrated-profile alias, profile algebra or upgrade.

## Exact schema and catalog selection

L1 selects the following **closed composition**, not future contents of linked
files and not per-Module decoder flags:

| Component | Exact selected meaning |
|---|---|
| R | A2R1+Repeat: `trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1`, with its complete B/F/S/A2/empty/unit/scalar/CopyOrigin/ReturnCoverage/repeat amendments and catalog. |
| L | Only the aggregate-local normalization, empty-Sequential record-05 admission and layout/frame integration in this document. |

Keep `(1,0)`, `implementationKind=01`, codec `01`, the map mask/count order,
`17 + 4*popcount(mapMask)` prefix, row widths, index/count domains,
BlockEntryState/CFG rules and instruction-length bands.

There is **no new numeric allocation**: retain Body records 01..0B, F's
0C/0D and S's 0E. Body record 0F remains unassigned in L1; E1's directory and
ReturnCoverage Contract 000F are different namespaces, not aliases.
L uses existing record 05 and opcodes. E1's amended 0A/domain, RootPin's
conflicting ReferenceSupport record and every unlisted composition remain
unselected. Inherited unusable RegionExit shapes gain no exit-kind semantics.
Reallocating draft numbers would be legal, but would not reduce width or
remove an integration obligation here.

Use governing edition 1 and **R's complete catalog unchanged**. In particular:

* Preserve `20 -> 2 -> 1`, R's positive nominal Copy carrier within 2/1,
  `22 -> 2`, and CopyOrigin 15 with its conditional layout dependencies.
  Retained nominal Copy/fixed-aggregate value uses require the existing
  rootless-value 2/1 scope; retained arrays require 20/1, including nested
  children. L adds no new Feature.
* Preserve R's scalar `11/1 -> {}` registration and F's bounded
  `10 -> {}`, `8 -> {1,2,10}`, `16 -> {1}` reconciliation. General predicates,
  A3, enums, E1 and RootPin are not added; incomplete registrations remain
  incomplete.
* Arrays, raw-Union values, constants, fields/queries/imports and native
  operations retain their independent use-dependent requirements. Selecting
  L1 does not require a foreign call, 16, CopyOrigin or raw-Union values.
  Another Module cannot supply a missing using-Module requirement.
* Select Windows x64 TargetProfile `(1,1)` and Module target requirement 1
  for this execution slice. Preserve Standard `runtimePolicy=0` and the
  producer's existing fail-fast implementation selection. No E1/Feature 21,
  native unwind Feature 7 or CheckedExceptions wire value is added.

Carry the immutable exact selection through public `EmitStandard` destination
selection, `TrRmStandardArtifactInput`, indexing, tables, manifest validation,
loading, reconstruction, rewriting and lowering. A default overload stays on
its existing profile. Unknown/unsupported selectors decline before
schema-dependent indexing or output writing. Every dependency has its own
explicit supported schema; a root's choice, Feature list, filename or `(1,0)`
bytes select nothing for it. No private fallback or old-profile retry.

For the initial L1 load/re-emission operation, require **L1 for the root and
every supplied provider artifact**, including type-only and otherwise unused
providers. Each remains explicitly selected before indexing. A known R, B or
E1-only provider is unsupported by this operation, not implicitly upgraded;
other operations' existing directed interoperability remains unchanged.
Co-located Modules necessarily share the artifact's L1 selection. Typed
re-encoding to an explicitly selected L1 destination can preserve unchanged
type contracts; relabeling provider bytes is not a conversion.

## Body representation and construction

The bounded value scope is closed fixed-layout rootless Copy nominal
aggregates and their independently admitted fixed-array children. Positive
Copy, complete layout/validity and provider evidence remain necessary;
layout equality, absent Cleanup or a small size is not evidence.

### Ordinary constructor lowering

A checked locally implemented constructor becomes a private ordinary
value-returning MethodDef, with the selected complete nominal result type,
ordinary input signature and a role-3 local construction Home. It has no
logical input receiver containing an already-live result. Calls bind its
actual MethodMap identity, not a generated-name convention.
Admission depends on complete signatures and operations, not a prescribed
number, naming pattern or ordering of locally implemented methods.

Remap the source constructor's unpublished receiver to that private Home and
its inputs to their real formal Homes. Preserve declaration initialization,
argument captures, constructor statements, effects, nested calls, checks and
final validation in their actual order. This is normalization after source
checking, not permission to call an arbitrary factory a constructor or to
perform inaccessible source field writes. Exporting/discovering source
constructor roles is not assigned here; independently retained/exported
declaration identities cannot be erased by this normalization.

The construction Home starts **vacant**, never as zero/default/live.
Initialize complete children under the actual private construction authority;
retain partial state and all surviving dependencies between stores. A
whole-value read/return requires every required child and nominal validity
obligation complete. Only normal call completion publishes the result.
Hidden ABI result storage is physical supporting storage, not a logical
formal, reference, live receiver or permission to expose partial construction.

The producer and reconstructed indexed-Body verifier must support this
structural construction directly. Do not infer completion from a constructor
name, an observed sequence length, a native pointer formation, layout bytes
or an unchecked `IsConstructor` assertion. An implementation unable to
represent the required state must decline, not skip verification.

### Reused operations

Opcode, record, section and TypeSpec-form codes in this table are hexadecimal;
widths, counts and selector/domain values are decimal.

| Need | Selected representation and obligation |
|---|---|
| Local storage and value snapshots | Existing HomeMap roles and complete TypeMap types; role 3 begins vacant. D5/short 13 loads only an eligible complete value; D6/short 14 copies to an eligible Home under its actual transition. One aggregate remains one logical value, regardless of physical register count or backing size. |
| Construction/field place | Existing 24-byte record 04, D8/short 91. Home target uses `targetKind=1`, its exact Home index, `targetRefKind=0`. Field target uses `targetKind=2`, `targetRefKind=1`, the exact FieldDef row and complete referent type; base is the local Home or an already captured Field/Element PlaceHandle. Index fields are unused/zero for fields. A value register is not a writable parent place. |
| Initialization/replacement | Existing 20-byte record 05, D9/control zero or short 92; `placeKind=1`, `sourceKind=1|2` meaning either Register or Home, `sourceUse=1=CopyRead`; transition 1 initializes a vacant child and transition 2 performs permitted replacement. Unused formal/dependency fields are zero. The field's declared mutability, containing authority and partial/live state still govern. Constructor authority is not ordinary postpublication mutability. |
| Read/extraction | Existing DA/short 63 reads a selected field place; complete Copy extraction uses its assigned field-extraction form where eligible. Neither operation turns a copied aggregate into original storage. Safe packed access uses the selected place's actual alignment and authorized extent. |
| Calls/returns | Existing record 02 and E0/short 90, typed ordinary inputs and Register/Home results; retain destination preparation and failure phases. Aggregate results may use a Home. EB/B8 returns a complete non-unit Home under the existing source-use contract. A large result does not become multiple logical returns or a raw address. |
| Arrays | Existing TypeSpec 11, FixedArrayLayout 0012 and record-04 `targetRefKind=3` retain exact E/N, using Module/profile and captured index. Reuse complete-list/private construction and record-05 RepeatCopy (`sourceUse=6`) from R. Whole Copy and field/element stores preserve snapshots, dimensions, coverage and pointer origins; no expansion of a uint64 count for layout/fact analysis. |
| Native address | Reuse S's EC/record 0E only when requested, after complete initialized backing and real alignment/access/suffix checks. E9/NativeAddress is unrelated and optional. A packed field may be safely loaded/stored yet ineligible for an aligned typed native borrow. |
| Failure/regions | Retain R's supported terminal failure behavior and explicit FailFast. E1 remains a separately selected contract; its record-zero 0F, handlers and amended 0A are not admitted. No aggregate exception, implicit cleanup region or failure-status result. |

Field references above are **FieldDef rows**, not FieldMap indexes, tokens,
ordinals or byte offsets. Imported fields use their checked local expectation
and bound provider correspondence; retaining a local stub row does not
discharge its original Import/layout obligation. Every field step preserves
complete nominal owner, actual generation, containing path and authority.
Mixed field/array paths are not flattened or reconstructed from spelling.

### Exact empty-Sequential completion amendment

Nonempty Sequential construction needs no new operation. For the
instance-field-free boundary, extend R's existing None/zero-use initialization
as follows, **only in L1**. The complete record remains exactly twenty
little-endian bytes:

```text
u8 recordKind=0x05, u8 flags=0, u16 reserved=0
u8 placeKind=1, u8 sourceKind=0, u8 transition=1, u8 sourceUse=0
u16 placeIndex=P, u16 sourceIndex=0
u32 dependencyContractRow=0
u8 hasFormal=0, u8 flags=0, u16 formalOrdinal=0
```

P is R0..R13 holding an Initialize PlaceHandle for wholly vacant private
construction storage rooted in a role-3 Home of this activation. Its exact
referent is an admitted Copy Sequential nominal type whose
**selected complete LayoutField instance inventory is empty**. Ordinary
construction code, side effects and required checks must already have run;
this store performs only the explicit logical completion/publication
transition. It neither evaluates them nor supplies missing nominal validity.
It completes only its exact referent, not an incomplete ancestor or an
escaping partial array. Apply it through D9/control zero or eligible short 92.

No payload bytes are read or initialized. The target's minimum empty extent
and final alignment rounding remain padding, not a value or a default.
Static fields do not count as instance fields. An actual void field is still
a logical child: it requires its independently admitted explicit unit
initialization, not this nullary shortcut. Nonempty, live, maybe-live,
partial or reserved destinations reject this combination; it cannot replace
a live value or complete an incompletely initialized parent.

R's existing empty-array, unit and empty-Union admissions stay exactly scoped
by their owners. None does not become an aggregate source, R14 a universal
default, or sourceUse zero a generic initialize operation. The same bytes do
not gain the new meaning under R, B or the old E1 selector.

## Layout, physical values and frames

Retain source-normalized Packing and optional declared Alignment in Layout,
with exact ordered LayoutField/FieldDef membership and complete field types.
Omitted declared alignment is the metadata owner's zero representation;
source Alignment zero is still invalid. Derive offsets, extent, completed
object alignment and transitive required explicit alignment under the exact
target. Do not serialize derived offsets as authority, reacquire Core schemas
from names during source-free loading, or substitute private revision 103.

Keep all layout, array `count * stride`, nested offset and alignment-rounding
arithmetic checked in target nuint. Derive array stride from the completed E
extent/alignment, including tail padding and nested explicit requirements.
N=0 still validates E and its dependencies; zero extent supplies no payload
address. Respect the separate physical codec ranges. Unrepresentable layout,
valid unsupported encoding/mapping and representable resource exhaustion are
different failures, not wrap, sentinels or a new layout cap.

Lower large values to as many registers or exact-extent private slots/copies
as needed. Preserve typed representation observations and actual initialized
payload; copying does not initialize unspecified padding or raw-Union bytes.
Do not widen a tail access, touch a neighboring field, or overread a page.
Known backing/provenance/validity is not erased by Copy, spilling or unsafe
authorization.

Every addressable local, array base, private construction/result Home,
materialized value and supporting slot must satisfy its **actual use's**
extent/alignment. Interior field accesses retain the weaker actual alignment
caused by packing; no aligned reference is manufactured from a type name.
Array bases and strides preserve every element's alignment, including after
spilling and nested placement.

For joint lowering, select the existing Windows x64 aggregate call
classification: ordinary sizes 1/2/4/8 use integer carriers, other positive
sizes use independent indirect copies and hidden result storage. This
proposal standardizes no additional Terrum ABI. Incoming indirect copies
promise at least sixteen-byte ABI alignment, **not** arbitrary object
over-alignment. Materialize a correctly aligned local before an operation
requires it; hidden result storage already needs the complete result object
alignment. Preserve source input snapshots and no-alias/publication
obligations. Whole structural-array parameters/results still have their
separate unselected signature/ABI gate.

Frame planning accounts for slot sizes, each slot's alignment, call/shadow
space, spills, result/capture temporaries and alignment headroom with checked
arithmetic. Over-aligned frames require a recoverable stable prealignment
anchor; reserving a multiple of sixteen alone is insufficient. Probe the
complete worst-case allocation/alignment range before accessing new pages,
without destroying incoming arguments or nonvolatile state.

Prologs, epilogs, frame-relative addressing, generated helper access and
UNWIND_INFO must describe the actual state before, during and after dynamic
alignment at every supported instruction point. Restore stack/nonvolatile
state on normal exits and provide accurate platform frame recovery, including
nested calls. Use valid platform encodings or decline an unrepresentable
frame; do not truncate a displacement or advertise a successful smaller frame. Existing
16/128/8192-byte storage checks, signed-int offsets or probe thresholds do not
become source/Standard layout limits. No hidden heap allocation is selected.

JIT and AOT share these obligations. JIT publishes fixups, executable code,
instruction-cache state and registered unwind tables before exposure and
retains their backing through all live frames/addresses; AOT emits the
corresponding PE data. Accurate unwind data alone is not E1 event transport
or inward native-unwind Feature 7.

## Verification, reconstruction and replacement

Keep source checking and the existing mandatory per-function compiler
verification enabled before production, after reconstruction and after
meaning-changing rewrites. Extend their typed state/place/call handling;
do not bypass `SemanticVerifier`, clear facts to fit the scalar writer,
fabricate live Homes or accept a previously verified flag. Finite analysis
must preserve complete/partial state, actual field/array paths, captured
indices, loans, guarded pointer origins and normal-result availability.
Failed or exhausted analysis declines explicitly.

Consumers still owe selected-schema framing, checked extents, exact record
shapes, reserved fields, namespace/range/ownership checks, complete supported
requirements, target geometry and directed binding before use.
[Producer truth](../specs/representation/reader-trust.md) remains the owner:
this integration does **not** require arbitrary consumer re-proof of authored
constructor behavior, nominal validity, source authorization or provider
Bodies. The compiler's chosen verification is not a serialized trust policy.
Keep malformed/unsupported rejection tests distinct from source-verifier
regressions and optional semantic-audit tests for false producer facts.

Source-free loading reconstructs actual indexed instructions, field and
array selections, distinct Homes, typed snapshots, ordinary local callees
under their MethodDef/using-Module context. It needs no source, source Layout
registration input, constructor-name lookup or private package. Preserve
source-checked construction effects and applicable validity/publication
obligations rather than reconstructing only a layout and scalar caller.

Re-emission must support the retained indexed representation; the existing
“indexed Body cannot be reserialized as structured source” guard is not
permission to erase it. Select the destination separately and rebuild all:

* Module/TypeDef/FieldDef/Layout/LayoutField/profile and Import/Export
  references, complete TypeSpecs and Module-owned FixedArrayLayout rows;
* MethodDefs/signatures/call targets and original-versus-actual provider
  correspondences, including constructor-normalization remaps;
* distinct Homes; Type/Method/Field/Constant maps; typed records and selectors;
  Block states, edges, offsets, short/long forms and code/section extents;
* scalar value references, CopyOrigin/ReturnCoverage and per-Module least
  closure, without adding or erasing an unsupported failure contract;
* retained SourceMap instruction ordinals/substeps and native origin/address
  mappings, or legitimately omit optional debug data.

Never deduplicate storage identities, remap counts/geometry/payload bits as
references, byte-patch opcodes, concatenate differently selected Bodies, or
drop a requirement to fit an older destination. Shared recipes may need
cloning when using-Module bindings differ.

Canonical writers use the existing first-logical-use/no-unused map and record
rules and shortest eligible instruction forms. Distinct Homes are not
interned; there is no new unique Home or whole-artifact ordering. Ordinary
readers retain valid consistently remapped noncanonical forms.

Binding resolves exact original Module/key/type/field identities before
comparing selected actual providers. Recheck declared packing/alignment,
complete geometry, Copy/validity/access, array identity/stride and every
retained fixed/coupled observation or stronger fact actually used. Compatible
private Body/row rearrangement need not be byte-identical. An incompatible
replacement declines or uses separately authorized regeneration; equal
exports or size do not repair a changed layout/alignment/coverage promise.
Relowering cannot relabel a still-live old code/address origin.

## Runtime and failure boundary

The successful path constructs before publication, captures/checks the
destination before its RHS and commits exact typed stores to the original
selected storage. Replacement stages a complete RHS before ordinary
retirement/commitment. Earlier permitted effects survive failure; no rollback,
atomic whole-array update, default initialization or observable partial
result is promised.

Dynamic index/suffix failure retains its existing operation-specific Core
category under the selected terminal policy, before later indices/RHS/arguments
or an access/call. As the [runtime owner](../specs/runtime/standard-failures.md#1-exact-core-identities-and-construction)
permits, a terminal policy need not prepare an unobservable local event;
it must not claim checked recovery or invent another normal outcome.
It publishes no failed selection/pointer/result. Constructor/check failure
publishes no aggregate result. Explicit FailFast and the selected terminal
policy promise no checked unwind, catch, normal Cleanup or ensure after
termination. Stack exhaustion, loader failure and unrecoverable resource
preparation are not automatically recoverable
OutOfMemoryException or a guessed program return.

Host `FailFast`/`CheckedExceptions` enums are implementation selections, not
schema selectors or new Standard policy values. Source/loaded Bodies requiring
catch/finally or checked-failure delivery decline before affected use under
L1; do not drop those constructs and claim this profile preserves them.

Malformed input, missing requirements/bindings, unsupported representation,
failed verification and compiler capacity failures publish no partial
artifact/image/code entry and do not replace an existing requested output.
Native activation occurs only after complete admission for the selected use.
No new diagnostic code, success-shaped fallback or diagnostic-only substitute
for an executable gate is selected.

Raw native calls/addresses retain explicit unsafe, stability, alignment,
extent, validity and lifetime obligations. There is no marshalling, pinning,
rooting, implicit lifetime extension, inward native unwind or outward Terrum
exception propagation supplied by L1.

## Required acceptance cases

These are **future gates**, not results of authoring this proposal. Use
unrelated synthetic types and independently assembled artifacts as well as
source-produced inputs. No application/API/type name may control admission.

| Area | Positive, negative and boundary obligations |
|---|---|
| Actual Standard executable | Produce L1 with a nonexported ordinary constructor, multiple local helpers and no imports, function-pointer signatures or NativeAddress operations. Delete/exclude source/private artifacts; load, verify, rewrite, re-emit, reload, then execute JIT and AOT from those bytes. Observe fresh reads after scalar-field, whole-value and array-element/field mutation. A constructor merely present in rejected bytes is not coverage. |
| Packed/over-aligned geometry | A uint8/uint64/uint8 Sequential with Packing=1 and Alignment=32 or 8192 has offsets 0/1/9 and extent/stride equal to the selected alignment. Verify exact Middle writes preserve both adjacent bytes and neighboring elements. Include every accepted Pack, derived versus supplied alignment, nested explicit alignment under Packing=1, and array fields/nested arrays. Preserve declared inputs through Standard reconstruction and non-byte-identical re-emission. |
| Construction/state | Ordered side-effecting inputs/field initializers/constructor calls run once; incomplete children, invalid final checks, readonly replacement, lost backing and failure before return cannot publish a value. Include conditional field initialization, Copy snapshots before/after mutation and aliasing RHS effects. Partial joins do not become vacant/complete. Source verification must still reject illegal source authority. |
| Empty/zero boundaries | Explicit empty Sequential construction uses the new nullary store and retains padding-only minimum extent/alignment. Declaration alone remains vacant. Reject nullary nonempty/all-void/partial/live targets and its use under old profiles. Preserve R's independent empty-array/empty-Union/unit cases; N=0 validates E, and zero-extent elements provide no native address. |
| Calls and materialization | Return nominal aggregates above sixteen bytes through real local helper calls, not mandatory inlining. Test 1/2/4/8 versus other positive extents, complete aligned result buffers, incoming sixteen-byte-aligned ABI copies materialized before stronger address use, multiple staged arguments and nested calls. Direct whole-array signatures remain a deliberate unsupported gate, not an implicit pointer ABI. |
| Address/access safety | Safe unaligned scalar field read/write uses exact extent across cache/page boundaries with owned guarded backing. An aligned native member borrow rejects when actual alignment is too weak; a correctly aligned packed enclosing-record pointer is distinct. Array first/middle/last and nested suffixes retain immediate bounds, generation and original backing; negative/N/too-wide indices fail before RHS/call. No padding or one-past backing. |
| Frames and failure paths | Exercise Alignment=32 and 8192, frames below/above actual platform probing boundaries, large unwind allocation encodings and live values across nested calls. Inspect real platform prolog/unwind recovery at prealignment/prolog/body/epilog points, call alignment and nonvolatile restoration, using independent OS/ABI probes rather than only emitted bytes. Supervised terminal index/suffix negatives must show no element access, later RHS marker or native call. Caught language failure is not claimed. |
| Malformed/support isolation | Wrong selector/context; bad map/record/owner/field/type/profile references; wrong widths/reserved fields; unsupported or missing Module closure; forged array geometry versus derived E; illegal Pack/alignment or inline cycles reject before affected use. Initial L1 operations reject every supplied non-L1 provider before indexing, even with apparently unchanged bytes. E1 directories, handlers and amended exits reject under L1, not fall back to plausible old shapes. No Feature sniffing, scalar/declaration fallback or selection of F's one-caller plan. Structurally valid false source/Body facts are producer/audit cases, not mandatory arbitrary consumer rejection claims. |
| Resources and widths | Checked layout round-up/add/product overflow and nested offset overflow remain distinct from capacity refusal. Test representable layout above old value/address/struct-size gates, compact uint64 counts without element expansion, zero-stride huge counts, full u16 map/record indexes and u32 extents at supported boundaries. No arbitrary new layout cap; impractically large positive storage need not actually be allocated to establish layout validity. |
| Remapping/replacement | Consistently permute global/profile/layout/field rows, Homes, maps, Blocks and records; cross short/long index boundaries; include payload bytes equal to opcodes/indexes. Rebuilt source-free behavior and declared layout inputs survive. Compatible private constructor/helper changes succeed; changed type origin, Copy, field type/order/access, Pack, alignment, array E/N/stride or retained coverage/layout observations reject when incompatible. Check original expectations and actual provider facts separately. |
| Failure/output/lifetime | Preparation/verification/emission failures preserve sentinel output files and perform no entry execution. Runtime failure publishes no aggregate/default result or pointer. Retain JIT tables/code/load backing through real frames/addresses and deregister safely. Do not reinterpret stack/platform termination as a normal result or a caught local event. |

Use existing public compiler/Standard APIs and explicit input selections.
Supervise headless JIT and emitted executables in owned workers with deadlines,
actual exit/trace capture, owned-process termination when needed, wait-for-exit
and bounded `TestFileCleanup`. Keep visible/hardware opt-ins untouched.
Record final compiler/configuration, patch/input/provider identities and
selected/passed/failed/skipped/incomplete outcomes. Layout, loading, emission,
execution and OS unwind evidence are separate claims.

## Dependencies, exclusions and remaining approval

Implementation depends on the R owners, generic indexed-Body production
and re-emission, source/private/Standard layout-fact retention, and shared
large-value/frame lowering. E1's exception codec/runtime and Core identity
handoff are not prerequisites for this fail-fast runtime gate. Any future
caught-failure composition must explicitly select them and their complete
directed interoperability; names, integer stand-ins or L1's passing success
cases cannot fill that separate gate.

For a root with only locally implemented executable methods, use the
selected semantic loader and the ordinary checked native emitter/JIT
handoff. Preserve any type-provider/layout binding context. Executable
MethodDef-provider closure still needs the provider-native owner's checked
plan; clearing imports or broadening its F-only acceptance is not this
decision. An unsupported provider lowering remains an open gate.

Deliberately excluded: new Layout schemas/formulas; public/exported constructor
role discovery; general aggregate constants/defaults; direct structural-array
call ABI; managed/owning/non-Copy storage, checked exception composition,
async frames, RootPin, A3/general generics, general predicates or another
target; new foreign/native mappings, provider resolver/graph ABI,
private revision/container changes or CLI
defaults; sample/DX11 policy, marshalling or source-specific fallbacks.
R's already selected raw-Union/coverage semantics are preserved, not redesigned.

**Remaining approval decision:** approve or replace the single L1 composition,
its ordinary private constructor/storage normalization and narrowly scoped
empty-Sequential completion amendment. The recommendation is concrete; no
in-scope encoding, layout, failure-routing or frame policy is left to a
sample recognizer or an implementation limit. Approval, incorporation into
the canonical owners, implementation and qualification remain separate.
