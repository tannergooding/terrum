# Standard TRRM unit fixed-array initialization

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** This is a candidate wire-admission decision,
not normative adoption, implementation support or execution evidence.
[Proposal index](README.md).

## Finding and recommendation

Positive-count `[void; N]` construction has a **selected wire-contract gap
for the literal unit source**, alongside implementation gaps. Its source
cardinality, unit value and array geometry are already determined. The
existing SemanticStore can install a complete typed unit value, but current
A2/A2E provide no contextual constant-source meaning for `()` in that store.
Changing the producer/reader scalar whitelist alone would invent that meaning.

Recommend adding **exact `void` with value `()` to R14's admitted
context-typed source domain**, only in the exact **A2EU** successor below.
Reuse ordinary `sourceKind=1=Register`, `sourceIndex=14`,
`sourceUse=1=copy/read` and SemanticStore `05`; allocate no operation,
record, type, section, flag or Feature. R14 supplies the complete unit value,
not integer zero, an empty array or a general default.

A2EU composes A2's identity/layout/Copy rules with A2E's empty construction
and this unit-source admission. **A2 and A2E remain unchanged.** In
particular, A2E's `sourceKind=None/sourceUse=0` store still constructs only
the exact N=0 array; it is not the carrier for a positive-count unit array.

## Baseline and source evidence

The [specification index](../specs/README.md) reaches the governing owners.
Fixed-array source/representation directions remain provisional; the
accepted unit and execution contracts are not new choices here.

| Evidence | Consequence |
|---|---|
| [Unit values](../specs/language/expressions-and-statements.md#the-zero-data-void-value), [initialization](../specs/language/initialization-and-flow.md#states-reads-and-initialization), [unit IL cases](../specs/conformance/il-unit-values.md) | `void` is inhabited, Copy and has exactly `()`. Vacancy is not that value. Calls/checks/failures and logical value positions survive removal of physical payload. An unrelated zero-data nominal type is not unit. |
| [Grammar](../specs/grammar-spec.md#34-expressions-and-precedence), [existing fixed-array source direction](standard-native-fixed-array-source-contract.md#proposed-initialization-and-publication) | `()` is an ordinary expression; the already selected source path accepts exactly N expressions in an established `[void; N]` context. It captures them once left-to-right, installs after successful captures and publishes only a complete array. No new source syntax is needed. |
| [A identity/shape](standard-trrm-fixed-array-identity-shape.md#complete-identity-and-exact-layout-row), [A2 element admission](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule) | Unit E is admitted. Its element size/alignment are 0/0, while the array has `A=1, S=0, B=0` at **every** valid N. N remains an exact uint64 count; positive N is not empty. |
| [R14/R15](../specs/il-instruction-set.md#selected-special-operand-slots), [record 05](../specs/il-instruction-set.md#typed-operand-records), [stores](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement) | R14's admitted-type table omits `void`; R15 supplies no unit constant either. Ordinary stores require a complete typed source with its source-use contract. None is absence, not a value constructor. |
| [A2E](standard-trrm-empty-fixed-array-initialization.md#exact-record-and-state-semantics) | Its exceptional record-05 combination initializes exactly an N=0 array at a permitted vacant local-rooted place, including empty children. Its positive-count zero-extent section explicitly preserves all N initializers and leaves their value carriers independently gated. |
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.NativeSampleArray.cs), [source verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.NativeSampleArray.cs), [bound node](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | Binding checks exact count/type and retains all expressions in `BoundNativeFixedArrayInitialization.Elements`. Verification evaluates reachable elements in order and retains failure edges; an empty collection is allowed only at N=0. |
| [Active source tests](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayStorageTests.cs) | `ZeroPayloadArrayStillRequiresEveryElement` contains `[(), ()]`, asserts zero stride/extent, reads/replaces unit elements and rejects underfill. `ZeroPayloadArrayDoesNotProvideElementStorage` rejects a unit-element address. Nested positive unit arrays are also exercised. These tests are compiled by the current test project, not excluded prototypes. |
| [Private persistence test](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs) | `UnitFixedArrayOperationsRoundTripThroughRevision100Async` retains two unit elements and rejects a forged nonzero unit payload. Private revision 100 is not an A2/A2E Body codec. |
| [Current Body producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs), [source-free reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs) | `IsSupportedNativeFixedArrayInitialization` accepts contextual scalar constants or A2E's exact empty-array children. `TryGetContextualInitializationRegister`/`TryGetContextualConstant` require `DirectNativeImportContract.IsScalar`, which excludes `void`. Source-free positive initialization reconstructs those scalar constants; empty initialization separately checks N=0. |
| [Record writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyRecordWriter.cs), [Body/map validators](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs), [A2E Body test](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs) | Ordinary record-05 source-use remains one; `EncodeEmptyArrayInitialization` and `ValidateEmptyArrayInitializationStore` retain the exact None/zero-use exception and N=0 checks. `StandardA2EInitializesEmptyLocalAndRejectsTheRecordUnderA2` verifies the separate selection, nested empty events and re-emission. |

These are current source observations, **not tests run by this documentation
change**. Existing runtime assertions do not qualify Standard unit-array
production, reconstruction or execution. The
[implementation inventory](../implementation.md) remains the support owner.

### Why assigned carriers do not already complete this case

A live exact unit Register/Home can already be copied into an eligible unit
destination. That is not a source for an authored `()` in a method with no
such incoming value. HomeMap locals begin vacant; adding an unused unit
Home, BlockEntryState value or layout does not initialize it.

CallSite None supplies an exact unit formal or the signature-known normal
unit result. It preserves the evaluation of the actual call; it does not
publish an arbitrary Register/Home source for a later store. Unit return
forms terminate the method, not produce a store operand.
DestinationPreparation is an invocation preparation, not a nullary unit or
array constructor. A fabricated helper, extra formal or provider call changes
signature/binding/effects/failure obligations and is not a lowering alias.

`D4`/`16` ConstantLoad could carry unit through a fully selected frozen-value
codec, but that is **not part of A2/A2E**. Accepted Feature 11 remains
registration-pending; [scalar frozen-value registration](scalar-frozen-value-registration.md#candidate-registration-and-value-tables)
proposes an exact zero-Blob unit node in a different schema. The current
[schema predicates](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs)
do not select that scalar codec for A2/A2E. Composing it would be a concrete
new schema decision, not an already admitted fallback. No whole-array
frozen/default/aggregate construction codec is selected here.

## Alternatives and exact composition

All alternatives and allocations in this document are **unapproved**.

| Direction | Assessment |
|---|---|
| **Minimal R14 unit-domain amendment, selected by A2EU — recommended** | Keeps unit as a real complete source and ordinary copy/read stores unchanged. One contextual constant case suffices; no absent-source store rule or new wire extent is needed. The operation's exact type context, not zero byte size, supplies its meaning. |
| Extend A2E record 05's None/zero-use alternative to unit destinations | Also possible in a separately selected successor, with exact unit applicability. It adds a second absence-based construction meaning and bypasses the existing complete-source/copy-use route unnecessarily. It must not reinterpret the N=0 exception or default a whole positive array. |
| Distinct unit-construction instruction/record | Could materialize a general typed unit binding or combine destination selection/initialization. It needs an explicit allocation, operand/result type and state rules; a new opcode/record supplies no required capability beyond the selected constant plus store. Reserve no number for this unselected alternative. |
| Compose A2E with the complete scalar frozen-value schema | Reuses that proposal's unit node and ConstantLoad without amending R14, but adds Feature 11 admission, ConstantMap/source/node tables and broader scalar codec obligations for one singleton. Coherent when that value family is required; not the smallest unit-array decision. Its schema/requirements cannot be inferred from unit geometry. |
| Treat positive zero extent as empty, default storage or elide all initialization | Incorrect for retained value/state/effect observations. A proven removal of an unobservable effect-free local is an optimization, not the missing carrier; it cannot erase effectful/throwing captures or make a retained vacant Home live. |

Propose exactly
**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1`**,
candidate selector
**`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft`**.
The shorthand **A2EU** denotes **A2E = A2 + its exact empty-initialization
exception**, plus only this document's typed R14 unit-source rule.

**Intended supersession, only in A2EU:** extend the instruction chapter's
[special-source table](../specs/il-instruction-set.md#selected-special-operand-slots)
by `exact void: R14 -> (); R15 -> not admitted`. An assigned operation must
provide exact unit type context and admit that constant-source role. Its
existing type, access, source-use and transition rules still apply.
This is not a numeric conversion from zero, a typeless `mov` source, a
writable R14 binding or permission for unit arithmetic/bitwise/ordering.
For typed stores it is an actual complete Copy unit source.

Keep A2's TypeSpec `11`, section `0012`, TypeDef Copy bit `0x20`,
Field/Import/provider rules and A2E's **N=0-only None exception** unchanged.
DestinationPreparation's result-only zero-use rule and CallSite/return unit
roles remain unchanged. R14 cannot construct the complete type `[void; N]`,
`ptr<void>`, an optional or a zero-sized nominal value.

A2EU supersedes A2E only for the explicitly selected combined empty-and-unit
initialization route. It does not rename, mutate or alias the original A2
or A2E schemas. Standard, F, F+S, A1, A3 and scalar/G/enum/RootPin
compositions gain no admission; A3 composition requires its own decision.
The scalar frozen-value proposal's unit codec and Feature 11 remain
independent and unchanged.

Retain `(1,0)`, Body implementation/codec `01`, the mixed 1/2/4-byte
instruction bands, four-bit registers, u16 Body indexes, u32 inventory counts
through 65,536, all record extents and F/S allocations. The
[registry namespaces](../specs/representation/registry.md) gain no opcode,
record kind, prefix/escape, field, reserved-bit meaning or capacity change.
No new Feature, target or runtime policy is selected. Modules retaining array
types/layouts/operations keep `20/1 -> 2/1 -> 1/1`, Windows x64 TargetProfile
`(1,1)` and target requirement 1. Declaration-only Copy providers keep their
independent `2/1`; actual calls, fields and failures retain their own closure.

Select the exact schema before indexing, producing, binding, reconstructing,
rewriting or lowering. Each separately supplied provider retains its own
explicit supported schema. An A2/A2E type-only provider can supply unchanged
type/Copy/layout contracts, not an A2EU Body meaning. Co-location does not
select decoders per Module; packing retained unit-source operations requires
a destination schema carrying them. Apply
[exact draft agreement](../specs/representation/format-versioning.md), with no
Feature sniffing, version/name/extent inference, decoder retry, compatibility
alias, scalar-zero/None substitution or private fallback.

## Precise positive-array operation

The required case is a vacant **unit element** whose immediate parent type
is exactly `(FixedInlineArray, void, uint64 N)` with **N>0**, in private
whole-array construction rooted in a role-3 local Home of this activation.
An independently admitted real containing field/nested-array path is allowed;
nominal identity, containing authority and each parent dimension remain.
The unit referent is the normalized unqualified built-in `void`, not a
same-sized Transparent wrapper or a pointer to void.

Resolve the unique using-Module/complete-array/profile FixedArrayLayout,
its complete E and admitted target layout before affected use. Require
`N>0`, `S=0`, `B=0`, `A=1` and unit's existing required alignment; these are
validated geometry, not a byte-derived count or proof of live state.

Reuse PlaceSelection `04` (24 bytes), applied by `D8`/`91`:
`targetKind=3=Element`, `accessKind=3=Initialize`,
`destinationKind=1=Register`, `destinationIndex=P` in R0..R13,
`baseKind=2=Home` or `1=Register` holding an admitted captured parent
PlaceHandle, `indexKind=1=Register` or `2=Home`,
`targetRefKind=3=FixedArrayLayout`, and the exact unit
`referentTypeMapIndex`. Index/source fields, targetRef's one-based row domain,
flags and reserved values retain A's existing rules.

P captures the local-rooted destination, storage generation, parent, original
index and initialization authority. Validate original numeric `0 <= i < N`
before narrowing or multiplying, even though `i*S=0`. Equal offsets do not
merge different element places; register aliases do not duplicate rights.
Selection does not initialize anything or expose a reference/native address.

Apply ordinary SemanticStore `05` (20 bytes) at its actual program position:

```text
u8 recordKind=0x05, u8 flags=0, u16 reserved=0
u8 placeKind=1, u8 sourceKind=1, u8 transition=1, u8 sourceUse=1
u16 placeIndex=P, u16 sourceIndex=14
u32 dependencyContractRow=0
u8 hasFormal=0, u8 flags=0, u16 formalOrdinal=0
```

All multibyte fields are little-endian. The destination is a typed internal
PlaceHandle, not an array value, raw pointer or checked reference. Under
A2EU, the exact unit referent gives source R14 the complete value `()`;
no numeric register or bytes are read. The store performs one permitted
vacant-to-live **element** initialization, with ordinary copy/read use.
It does not itself publish the whole array. Initialize access neither
repairs missing containing authority nor permits replacement of live,
maybe-live, partial or reserved storage.

This operation writes no payload/padding, clears no storage, retires no old
value and invokes no initializer. It adds no failure of its own once its
place/state obligations hold; earlier place/check failures retain their
actual failure path. Bad/unsupported metadata or binding rejects affected
use, not as a new runtime unit-initialization exception.

Use `92` when the store record index fits u8, otherwise `D9` with its full
u16 index and zero control; selection uses `91` when its four-bit record
index fits, otherwise D8 with P in the low control nibble. Reuse records
where their complete meanings agree; logical executions, not distinct
record rows, distinguish element installations. Full inventory count 65,536
and index `0xFFFF` remain valid. No immediate-count repetition or bulk/default
constructor is introduced.

Other already admitted typed unit stores/value uses can consume this exact
R14 meaning under their own ordinary operation rules. This does not select a
missing general unit-local, nonlocal, optional, call-result or aggregate codec.
In particular, the record above is not a nullary live-replacement operation.

## Evaluation, failure and publication

Preserve the existing source construction contract:

1. Establish the exact target/type and cardinality before runtime evaluation.
   Capture/check an authored assignment destination before its RHS. Forming
   the private construction destination does not grant premature publication.
2. Evaluate each of the **N authored unit expressions once, left-to-right**,
   including each call's arguments, checks, side effects, failures and
   expression-boundary cleanup. A unit-returning call uses its actual
   CallSite/signature; its normal completion supplies `()`, not permission
   to omit the call. A nested initializer completes when reached, not in a
   breadth-first replay.
3. After all captures/checks succeed, install the N captured unit values in
   increasing element-index order and publish one complete exact array.
   Since all complete unit values equal `()`, the install sources can be
   contextual R14 without payload captures. This erases **only unit payload
   transport**, not the evaluations or logical cardinality.
4. If expression i fails or cannot normally complete, execute no later
   initializer, no installation/publication into that array's final
   destination, and no old-value retirement for a replacement RHS. Preserve
   earlier external effects and settle actual private acquisitions, including
   previously completed private children, under ordinary unwind. This is not
   rollback of those effects or a new exception/handler transport.
5. Private child completion must not expose a readable prefix or publish an
   enclosing array early. A nested positive `[void; N]` needs all its own N
   captures/installations. Empty child arrays still use A2E's exact N=0
   exception; a positive outer count still requires every authored child.

An effect-free `()` evaluation may need no instruction, but it remains one
typed captured element in the construction's logical meaning. Effectful or
throwing unit expressions must remain explicit ordered operations/CFG edges.
Optimization may remove private/no-payload operations only when all retained
state, value, failure and alias observations remain equivalent; it cannot
infer completion from zero extent.

Calls/failure/cleanup carriers outside an implementation's independently
admitted subset remain explicit support gates. This decision does not make
unsupported throwing Bodies executable or authorize a no-failure promise.

## Reconstruction, providers, remapping and lowering

**Producers** owe truthful exact type/count, source evaluation, place/state,
containing authority, failure and complete-publication facts. Carry them
through shared bound traversal/rewriting and mandatory verification; do not
recognize a function, sample, statement adjacency or fixed emitter sequence.
Retain unrelated synthetic effectful unit producers, not API-specific policy.

**Consumers** check the selected schema, closed record fields, reference/index
extents, exact unit source context, PlaceHandle carrier/access/origins,
using-Module/type/layout/profile relationships and actual provider bindings
before affected use. Retain operation-derived state across aliases/joins.
[Reader trust](../specs/representation/reader-trust.md) does not require
semantically re-proving arbitrary producer Body facts; false claims that
required evaluations occurred remain producer-truth violations.

**Source-free reconstruction** must materialize each retained R14 unit use
as exact typed `()` and preserve every logical element and ordered
call/check/failure node. For a `[void; N]` construction with N>0, the
`BoundNativeFixedArrayInitialization` has a present Elements collection with
exactly N unit expressions/captures, never `[]`, a missing/default collection
or N numeric-zero expressions.
Separately retained evaluation statements followed by typed unit installs
are valid when they preserve the same ordering/publication; do not replay
their calls as newly reconstructed initializers.
Array type/layout/Home presence alone cannot synthesize the construction.
Do not require adjacent D8/D9, a first scalar element or an int32-only index
pattern. Validate compact geometry without expanding N absent payload bytes;
materializing required Body elements uses bounded actual operations, and
unsupported/exhausted reconstruction fails without partial program publication.

**Binding and transformations** preserve complete normalized unit/array
identity, every dimension, original import expectations, selected actual
provider origins, field paths and Copy/layout obligations even at zero
extent. Remap Module/TypeSpec/TypeDef/Import/profile/layout rows,
TypeMap/HomeMap, captured general registers, BlockEntryState/records/blocks
and instruction references in their own namespaces. Element targetRef is a
FixedArrayLayout row, not a Home/type token. Source Register **14 remains
R14**, not a remappable general binding, Home 14 or value-map index.
A2E's unused `sourceIndex=0` also remains unused and zero; N/S/B/A are
quantities, not references.

Rebuild first-use maps/records, counts/mapMask, offsets and all affected
Body/block/code/section extents, short/long forms and instruction ordinals.
Update corresponding SourceMap slots or legitimately discard optional source
data. Re-emission to A2/A2E cannot silently substitute None, integer zero,
a helper or a missing initializer. A retained operation needs a destination
schema representing its actual meaning, not byte substitution.

**Lowering** may emit no unit payload load/store, while preserving actual
initializer effects, check/failure order, local state and parent publication.
The [source emitter](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs)
already evaluates each element before skipping a zero-stride payload store;
that source path is not Standard Body qualification. No memset, minimum
payload byte, hidden anchor, extra lifetime or native backing follows.
Unit references and [S zero-extent EC borrowing](standard-native-storage-address-formation.md#source-region-and-failure-semantics)
remain unavailable even for positive N and in-bounds i. Count/type/state
observations must distinguish `[void; 0]`, `[void; 1]` and `[void; 2]`.

## Required cases and exclusions

These are **future acceptance gates**, not passing evidence from this
documentation task.

| Gate | Required outcome |
|---|---|
| Positive unit/cardinality | N=1 and N=2 literals produce/reload exactly N typed unit captures/installations and complete only after all succeed. Underfill, overfill, spread and `[]` at positive N reject. Vacant declaration without an initializer remains vacant. |
| Effects/failures | Two unrelated unit-producing calls mutate an observable trace in source order, once each. Failure in the first prevents the second; failure in the second preserves the first's effects and publishes no array. Target failure precedes RHS effects; failed replacement construction retains the old destination until ordinary unwind. Preserve throwing argument/check/cleanup behavior without claiming unselected handler support. |
| Nesting/composition | Positive nested unit arrays retain each dimension and child completion order; positive arrays of empty children retain all child events through A2E. N=0 still uses its unchanged None exception, not an inferred positive-array default. |
| Type and place boundaries | Unit is not a zero-size nominal/Transparent value, pointer-to-void or complete array. Keep N=0 element admission/Copy/layout checks. Preserve aliases and containing field/init authority; reject wrong carrier/profile/provider, live second initialization and escaping partial construction. |
| Index and extent | Original negative, too-large and wide indexes reject even at S=B=0. Equal zero offsets do not merge element identities. Large compact type counts remain valid subject to target/query limits without requiring N expanded layout rows or imposing a new source count cap. |
| Schema and malformed inputs | A2/A2E do not admit a record-05 R14 unit source by this amendment. A2EU rejects R15 as unit, numeric/array/nominal reinterpretation, invalid source/use/transition/type combinations for the specified initialization, invalid reserved fields and truncated/trailing records. None/zero-use remains N=0-only. Existing scalar R14/R15 meanings are unchanged. |
| Source-free transformation/provider | Produce, load without source, rewrite/remap, re-emit and reload against independently selected compatible providers with reordered rows/maps/Homes/registers/records. Preserve exact unit Elements, original/actual origins and ordered effects. Exercise indexes 15/16, 255/256 and 65535 with counts through 65536 and correct source-map/short-long rebuilding. |
| Native qualification | Headless JIT/AOT from the final selected source-free compiler/configuration/input checks actual trace, failures and complete publication, not just image emission. No unit payload store, extra anchor or zero-extent native address is introduced. Unsupported failure/state/value carriers stay reported gaps. |

No normative specifications or compiler code are changed. The concrete
remaining decision is approval of **A2EU's exact typed R14 unit admission
and composition**, followed by separate producer/consumer/reconstruction/
remapping/lowering implementation and qualification.
General default/zero-sized nominal construction, positive-array nullary or
bulk construction, frozen arrays/scalar-schema composition, nonlocal/global/
static transport, new unit references/native backing, whole-array call/ABI
carriers, additional targets and A3 composition are excluded. Existing
independently admitted operations retain their own contracts; this proposal
does not complete the broader array adoption milestone.
