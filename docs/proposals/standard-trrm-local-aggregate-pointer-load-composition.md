# Standard TRRM L1 aggregate pointer-load composition

**Status: unapproved; provisionally actionable under the user's explicit
missing-contract direction.** This recommends one exact composition and its
aggregate-read integration, not normative adoption, implemented support,
qualification or stability. No existing proposal or specification is changed.

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.” This document supplies that provisional direction;
formal approval and implementation remain separate.

## Finding and evidence

The missing decision is **L1's joint selection with P**, not another source
dereference syntax, layout rule or aggregate opcode. Once selected, preserving
the complete read at its ordered position is an implementation obligation
already required by P. These are inspected working-tree facts, not execution
results:

| Evidence | Finding |
|---|---|
| [P selection](standard-native-pointer-load.md#exact-schema-selection-and-requirements), [L1 selection](standard-trrm-local-aggregate-layout-execution.md#exact-schema-and-catalog-selection) | P names B+I+P and B+I+G+P successors and explicitly excludes aggregate-local layout composition. L1 separately selects its A2R1+Repeat successor and excludes general predicates. Neither defines L1+P. |
| [Source identity dispatch](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1250-L1302), [L1 dispatch](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1472-L1474), [P identity predicate](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L307-L316) | P and L1 enter different routes. The additional NativeParameter/P identities also do not select L1. A pointer-load Boolean or an understood constituent cannot supply the missing exact composition. |
| [`IsCopyableRegisterValueType`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L8479-L8510), [P register typing](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs#L5349-L5383) | This validator admits kind-1 nominal struct value forms with positive declared Copy only under `LocalAggregateLayoutDraft`; enum handling is separate. P derives its result from the pointer's complete pointee TypeSpec and uses that schema-dependent admission. |
| [`NativePointerTypeContract`](../../sources/Terrum.CodeAnalysis/Semantics/NativePointerTypes.cs#L44-L79), [underlying Copy/pointee checks](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs#L269-L283), [source read binding](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L19537-L19563) | Source raw reads already use nonconsuming pointee eligibility and explicit unsafe authorization; independently eligible Blittable Copy Sequential/Transparent pointees are not intrinsically scalar-only. This does not admit every aggregate or unsupported pointer representation. |
| [Standard source type checks](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs#L1569-L1578), [P reconstruction](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.InitializationReferenceBodies.cs#L4313-L4345) | The current Standard P path is scalar/pointer bounded. Reconstruction rejects unsupported pointees and records a `BoundNativePointerRead`; recognizing `1B` alone cannot integrate aggregate values or their snapshot lifetime. |
| [`FromNativeValueRow`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L11626-L11769) | Existing recursive pointer decoding has local TypeDef and Import-to-expected-TypeDef paths, requiring represented structure and import context. Those paths are building blocks, not proof of aggregate P admission, actual-provider origin retention or execution. |
| [`EmitNativePointerRead`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeStorage.cs#L248-L257), [`IsIndirect`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs#L6782-L6796) | Lowering evaluates the address and loads only non-indirect values. For an indirect aggregate it leaves the source address; it does not itself capture independent payload storage. A later copy cannot substitute for the required earlier observation. |

The [current support inventory](../implementation.md#L83-L100) likewise keeps
aggregate P support open. Existing [L1 source-free tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmLocalAggregateLayoutConsumerTests.cs#L26-L103)
and [P selector/scalar cases](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmStandardNestedPointerLoadTests.cs#L21-L101)
exercise separate selections, not this composition; no pass result is claimed.

## One exact recommendation

Select the new, case-sensitive out-of-band candidate identity
**`trrm/1-draft-local-aggregate-layout-execution-pointer-loads-1`**.
Call it **L1P** here, as notation only, not flags or a profile algebra.

| Component | Fixed selected meaning |
|---|---|
| L1 | Exactly `trrm/1-draft-local-aggregate-layout-execution-1`, including its complete A2R1+Repeat schema/catalog, constructor normalization, empty-Sequential completion, Copy/layout/value rules and exclusions. |
| P | Only [P's typed raw-read operation and obligations](standard-native-pointer-load.md#exact-operation-contract): candidate short `0x1B`, destination/source nibbles, exact pointee result and retained-use requirements, applied to independently admitted L1 values. Neither of P's whole B+I successor profiles is imported as a component. |

Revision 1 denotes this closed agreement, not future moving contents of linked
proposals. **No G-containing L1/P combination is selected**: L1+G+P,
L1+I+P and L1+I+G+P are outside this decision. Existing B+I+P,
B+I+G+P and their separately selected NativeParameter successors retain their
own meanings; none becomes an alias for L1P. L1's inherited F/S contracts do
not import I or G. Do not compose additional exception, root/pin, generic,
optional-value or native-boundary protocols from another profile.

Keep `(1,0)`, governing edition 1, kind-`01` register Bodies, codec `01`,
mapMask framing, all row/map/record widths and L1's catalog unchanged.
Reuse P's candidate `1B` exactly; no new numeric ID, Feature, record,
TypeSpec form, discriminator, prefix or escape is proposed. In L1P the
inherited short gap `1B..1F` becomes `1C..1F`; checked-reference `19` remains
checked-only. L1's Body records through `0E` retain their meanings and `0F`
remains unassigned. An opcode, Contract/section kind and TypeSpec form with
equal numeric bytes are different namespaces. Compact-indexed kind `08`
is not a P fallback.

Every Module retaining P requires **2/1 RootlessNativeValues -> 1/1
NativeBoundaryContracts**, including uncalled/unreachable retained Bodies.
Preserve L1's complete per-Module least closure, including arrays, CopyOrigin,
ReturnCoverage and layout dependencies when actually retained. Reading alone
adds no call/address Feature; actual address formation,
native calls and exports retain their independent requirements.
Keep L1's Windows x64 target/profile `(1,1)`, target requirement 1 and
Standard `runtimePolicy=0` with its existing fail-fast selection. This adds
no catchable invalid-pointer failure, checked-unwind route or new policy.

Select L1P ordinally before schema-dependent indexing, writing, loading,
binding or lowering; carry the immutable complete selection throughout.
Default entry points and `LocalAggregateLayoutDraft` keep their old meanings.
For this bounded operation, **the root and every supplied provider artifact,
including type-only and unused providers, must explicitly select L1P**.
This preserves the existing L1/P uniform-selection boundary, not a global
ban on other operations' directed interoperability.

No mixed-schema loader or converter is assigned. Any separately authorized
conversion must select supported sources and destination, decode and rebuild
all retained meaning. Equal `(1,0)` bytes, Features, filenames or plausible
operands cannot select the route; relabeling is not conversion. Common-subset
bytes may be valid under separately agreed schemas, but cannot prove agreement
or originating provenance.

## Aggregate read contract

P still reads one available nonoptional raw-pointer **value binding**,
not a checked reference, PlaceHandle, function-pointer source, integer address
or Home identity. Its result is exactly the complete pointee T, with no
reinterpretation operand. Both registers are R0..R13; `x == y` reads the old
pointer before publishing T. R14/R15 are not additional defaults/discards.

For the aggregate slice, T must be a closed, positive-extent nominal inline
value admitted by **both L1's rootless Copy fixed-layout rules and the existing
nonconsuming raw-read contract**. Require positive Copy on each applicable
nominal declaration/expected import, the complete stored-child closure,
Blittable/validity evidence and the selected target layout. Size, absence of
Cleanup, a layout marker or an enclosing Copy bit is not sufficient.
Preserve all inner/outer qualifications, field access and independent
pointee admission. This does not assign top-level structural-array
pointee/call admission, missing stored-reference/owner/optional representations
or general authored copying. L1's independently admitted fixed-array children
and raw-Union contracts remain in force.

1. **Capture and order.** Evaluate the source pointer expression once, capture
   that pointer value, then evaluate the complete typed read at P's ordered
   position before subsequent observable operations. Publish a computed
   **value snapshot**, never a loan of the source aggregate. Later writes,
   pointer rebinding or calls cannot change fields subsequently obtained from
   that value. Copies/aliases of the result retain the same captured value.
   This applies to nested calls, argument staging, CFG edges and register/Home
   transport, not just assignment immediately following P. Equivalent
   optimization must preserve these observations and failure/effect order.
2. **Physical materialization.** A direct carrier captures the complete
   admitted payload bits. An indirect representation must capture independent
   payload storage at that same read position; an internal address may refer
   to this snapshot, not the original pointee. Plan sufficient correctly
   aligned private value/supporting storage and keep it live through every
   retained use, including spills and calls. Reuse L1's materialization/frame
   and target call rules; add no hidden heap policy or new ABI. Copying only
   when a later consumer requests a field/argument/result is too late.
3. **Complete representation.** Preserve promised field and scalar bits,
   including floating signed zero/NaN payloads, nested aggregate/array values
   and carried pointer/code identities. Copying pointers does not deep-copy
   pointees or rebase them into snapshot storage. Retain justified guarded
   backing/owner/provider dependencies; unknown external backing stays
   unknown. Capturing payload neither acquires ownership nor extends a source
   backing, guard, root, pin or provider-code lifetime.
4. **Layout and extent.** Use the exact selected declared Packing/Alignment,
   ordered fields, nested explicit requirements and derived extent/stride.
   Source access owes T's actual readable extent/alignment and validity;
   snapshot storage owes its actual uses' alignment. Packed interior accesses
   use their weaker actual alignment and exact authorized spans. No widened
   tail/page read, neighboring-field access, host layout substitution or
   aligned-member capability is inferred. Geometry/slot arithmetic remains
   checked under L1; unsupported capacity declines rather than truncates.
5. **Padding and partial representation.** Typed Copy preserves promised
   payload, not unspecified padding. Empty positive-extent Sequential values
   retain their nominal validity and logical state; their minimum padding
   byte is not a hidden payload/default. Where L1's raw-Union rules apply,
   preserve actual initializedness, derived coverage and typed dependencies
   as an opaque whole-value Copy, without selecting/evaluating every
   alternative, clearing unproven bytes or upgrading coverage. Required facts
   that cannot be represented remain an unsupported use.
6. **Authority and failure.** Authored source still needs explicit lexical
   unsafe authorization; an unsafe callable declaration alone is insufficient.
   Preserve known access, provenance, live-generation, alignment and backing
   obligations through shared binding/verification/rewrites. Preserve the
   grammar's unsafe definite-initialization exception without manufacturing
   checked initialization or completion facts. Pointer-cell initialization
   does not establish a valid aggregate pointee. Failed operand evaluation
   publishes no snapshot; earlier effects and existing settlement obligations
   remain. Invalid raw access gains no zero result, rollback, normalization or
   new cleanup guarantee. The read consumes/retires neither source nor owner.
7. **Concurrency.** “Snapshot” means the captured value from this ordered
   read's permitted observations, not a newly atomic multi-field observation.
   Existing coherent-memory, race, non-tearing and synchronization contracts
   apply; no stronger atomic, volatile, unaligned-atomic or MMIO mode is added.

## Retention, replacement and checking

Source production and source-free reconstruction must share complete
pointee/value admission, not widen a scalar helper indiscriminately or copy an
unsafe bit as permission. Carry an ordered raw-read/value capture through
shared traversal, substitution, mandatory compiler verification and lowering.
Apply the same contract to generic compiler paths, unrelated aggregate names
and all supported Body shapes; do not recognize a sample's statement sequence.

Resolve local TypeDefs and imported expectations in their owning Module and
complete TypeSpec context before applying layout or Copy checks.
`FromNativeValueRow`'s represented local/import paths do not replace actual
provider resolution. Preserve each original expectation separately from the
selected actual provider declaration, including nested pointer and field
origins. Use the existing directed replacement relation: retain nominal
identity, complete types/access, Copy/validity, field order, declared
packing/alignment, derived geometry, coverage and every stronger fact used.
Equal size, exports, carrier shape or type spelling is not compatibility.
Compatible private Body/row changes need not be byte-identical.

Re-emission explicitly selects L1P and rebuilds TypeSpecs, TypeDef/FieldDef/
Layout/LayoutField and fixed-array rows, Module/Import/Export correspondences,
signatures/call targets, distinct Homes, maps, typed records, Block entry
states/edges, instruction/record offsets and extents. Preserve or legitimately
omit optional SourceMap/native origins, remapping retained references.
P embeds no row index, but its source/result types and snapshot dependencies
still require remapping. Never deduplicate storage identities, patch matching
octets, reconstruct from source names or drop facts to fit an older profile.
Where executable providers are used, retain the existing
[L1 semantic MethodDef closure direction](standard-trrm-l1-method-provider-native-closure.md);
carry L1P through that checked handoff, without replacing it with F's native
symbol plan, clearing Imports or inventing another provider ABI. Its bounded
callable/contract admission remains a separate gate; selection cannot erase
an unsupported unsafe/effect contract to make a provider usable.

Consumers must complete selected-schema/framing, checked extent/count,
instruction-boundary/register-role, map/record/reference namespace and owner,
supported complete-type/layout/Feature and required binding checks before
affected use. Reject P under unamended L1, unsupported selectors/requirements,
wrong carriers/types and unrepresentable required layouts; never retry a
private or old decoder. Producers/rewriters owe truthful Copy, initializedness,
unsafe, validity, provenance and ordering facts. Mandatory structural checking
does **not** prove arbitrary source authorization, foreign addresses, external
bytes or provider Body truth. False structurally consumable facts remain
producer violations with no execution guarantee; deeper audits are separate.
The compiler's existing verifier remains enabled, not a serialized trust flag.

## Required regressions and open gates

These are implementation acceptance requirements, **not results of this
documentation task**. Use existing public compiler/Standard APIs and active
L1/P fixture patterns.

| Area | Required cases |
|---|---|
| Exact selection | L1P producer/index/reader/re-emitter agreement; aggregate-free common-subset input still uses explicit L1P. Reject unsupported/altered identities, P under old L1 and every supplied non-L1P provider, including unused ones. Preserve old L1, B+I+P and B+I+G+P behavior; reject fabricated L1/G/P combinations and kind `08` fallback. |
| Local/imported types | Unrelated local and imported Copy aggregates, direct TypeDef/Import Body-map tokens and row-backed pointer/nested TypeSpecs; resolve local expectations to actual provider origins. Include nested aggregates, fixed-array children and independently admitted carried pointer values. No name-, size- or declaration-order admission. |
| Ordered snapshots | Pointer-producing call executes once; capture before later aggregate mutation and descriptor rebinding. Read multiple fields after an intervening write/call, stage a read before a mutating later argument, pass/return through non-inlined helpers, retain across CFG merges, spill and register reuse. Two reads around mutation capture distinct values. Failed operand evaluation performs no read/publication. |
| Source-free remapping | Exclude source/private artifacts; load, rewrite, re-emit the root/providers or retained Module closure, reload and rebind. Consistently permute TypeSpec/TypeDef/Import/Layout/Field rows, Homes/maps/records/Blocks and cross short/long operand boundaries. Preserve types, layout inputs, payload observations and pointer/coverage dependencies rather than relying on byte equality. |
| Native lowering | Observe snapshot independence through reconstructed Standard JIT and separately emitted AOT bytes, not pointer equality or emission alone. Cover direct 1/2/4/8 carriers and indirect extents, including above sixteen bytes, nested calls/spills and existing over-aligned result/frame cases. Supply backing through an independently admitted L1/S route; P is not an address producer. |
| Layout/bits | Packed fields, nested explicit alignment and Alignment 32/8192; exact authorized page-tail reads and neighboring-byte preservation. Preserve floating bit patterns and carried pointers. Include empty positive-extent values and applicable opaque raw-Union coverage; never normalize padding, synthesize defaults or promote a shorter valid alternative to wider typed validity. |
| Rejection/truth separation | Malformed/truncated instructions, invalid R14/R15 operands/bindings, wrong map/owner/import/type context, nonpointer/optional/checked/PlaceHandle sources, void/zero-extent or unsupported pointees, missing positive Copy/child layout/Feature closure and incompatible providers. Source/shared-verifier negatives cover missing lexical unsafe, known invalid backing/alignment/access, local escape and lost coverage. External invalid bytes/promises and arbitrary false Body facts are producer/audit cases, not claimed universal mandatory-reader detections. |
| Replacement | Compatible private provider Body/row rearrangement succeeds after rebinding/regeneration. Changed nominal origin, nested pointer access/type, Copy, field/layout/stride, retained coverage, effects or lifetime/dependency guarantees reject when incompatible; retain every original edge, not only the called one. |

Native runs must retain compiler/configuration, final patch, exact schema and
root/provider/input identity with evidence and use existing headless
supervision. Loading, emission and runtime observations remain distinct.
**The no-overlap hold remains active: no builds, tests or process inspection
were performed for this proposal.**

Remaining decisions/risks are formal approval of this one L1P agreement and
qualification of complete aggregate/value-provenance capture, source-free
provider remapping and shared scratch/frame lowering. Broader G/exception,
top-level array and unsupported representation admission require separate
decisions; they are not implicit implementation choices here. An address-only
indirect path or unsupported retained dependency cannot be counted as completion.

## Alternatives and owning integration

| Alternative | Material difference |
|---|---|
| **New exact L1P composition — recommended** | Reuses P and L1 without another value opcode/state model. One selection closes the genuine composition gap; shared snapshot implementation remains necessary. |
| Widen/replace old L1 or a B+I+P identity | Draft replacement is legally possible, but deliberately changes an existing agreement and scope. It is unnecessary here and contrary to preserving those profile meanings; a capability Boolean is not a replacement agreement. |
| Compose L1 with I/G/exception companions, or add a raw aggregate-place/load record | Requires additional complete catalog, applicability or place/state decisions not needed to capture an existing eligible value. Neither is selected. |

On approval, the [draft selection](trrm-draft-schema-profile-selection.md) and
[source selection](standard-trrm-source-schema-selection.md) owners would
record this exact entry; L1/P would cross-reference their joint applicability.
The [registry](../specs/representation/registry.md) and
[machine registry](../specs/representation/trrm-1.registry.json) would project
P's existing candidate assignment for the selected composition; the
[Body](../specs/il-instruction-set.md#names-values-and-storage) and
[TypeMap metadata](../specs/il-metadata.md#L325-L343) owners would describe its
exact complete-value applicability and ordered capture.
No source production, layout equation, ABI, runtime policy or new allocation
is replaced. [Format versioning](../specs/representation/format-versioning.md),
[reader trust](../specs/representation/reader-trust.md),
[binding/replacement](../specs/representation/binding-and-replacement.md) and
[Windows layout/access](../specs/targets/windows-x64-data-layout.md) remain
controlling. No existing identity or distinct owner is superseded by this
document, and none of those integrations is performed now.
