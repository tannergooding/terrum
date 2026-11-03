# Wide scalar value carriers

**Status: unapproved; provisionally actionable only under the user's
instruction to supply a concrete missing-contract proposal and continue
using it as provisional direction.** This document approves no standard
schema, Feature, source form, ABI, implementation claim or release. It changes
no normative owner; its proposal-level change is superseding the earlier
private scalar draft as candidate owner, as described below.

## Decision and actual remaining gap

Retain the existing two-word `ScalarBits` payload and complete
`BoundConstant.Type`. Complete private persistence using the **already
proposed exact revision 94**: its complete revision-93 prerequisite plus
type-derived, little-endian scalar payloads of 0/1/2/4/8/16 bytes. Do not
introduce another wide-constant tag, host numeric carrier or revision.

The inspected source does **not** have eight missing in-memory scalar
representations. It maps all **24** scalars in the provisionally selected
[Feature 11/1 domain](scalar-frozen-value-registration.md#candidate-registration-and-value-tables).
The remaining private codec gate names **eight** scalar identities, but only
**four** have 16-byte payloads: `int128`, `uint128`, `float128` and
`decimal128`. The other gated types are `float16`, `bfloat16`, `decimal32`
and `decimal64`; widening a host integer is not their missing contract.

The [existing private constant-value proposal](scalar-frozen-value-private-persistence.md)
already recommends this carrier and revision-94 codec. This document
**supersedes that draft as the candidate owner of the scalar carrier,
private scalar catalog, tag-1 payload and revision-94 integration described
here**, correcting its obsolete long-only source baseline. It preserves
that draft's exact proposed bytes and prerequisite composition; it does not
create a second interpretation of 94. The older document remains a reference
for its rationale, not an alternative decoder.

The [scalar registration proposal](scalar-frozen-value-registration.md)
remains the candidate owner of the standard scalar value path, Feature
closure and scalar Blob codec. Its proposed removal of `11 -> 9` is not
performed here. The accepted
[registry](../specs/representation/trrm-1.registry.json) still marks Feature
11 registration pending and retains that edge. Private persistence cannot
approve or silently widen that standard candidate.

## Evidence and contract boundaries

These are observations of the inspected worktree, not results from running
the compiler or tests.

| Source or owner | Established fact |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `PrimitiveType` and `BoundConstant` | All 24 identities exist. `BoundConstant` contains `ScalarBits`, not only a long. Its cached nullable legacy value and `Value` accessor reject complete representations wider than eight bytes; its long constructor also rejects those types. |
| [`ScalarBits.cs`](../../sources/Terrum.CodeAnalysis/Semantics/ScalarBits.cs), [`PrimitiveTypeFacts.cs`](../../sources/Terrum.CodeAnalysis/Semantics/PrimitiveTypeFacts.cs) | The immutable payload is `(ulong Low, ulong High)`. Closed scalar widths reach 16 bytes, with little-endian payload import/export. Width canonicality and Boolean/rune value validity are separate checks. Legacy construction currently masks narrow bits, so producer validation must not be bypassed when serializing them. |
| [`TrRmFrozenValueNodeTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs), [`TrRmConstantSourceValueValidator.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) | The provisional scalar wire domain is atomic codes `00..0F` and `12..19`. Selected materialization maps every member to the bound catalog after checking Feature, complete type, using Module, selected Windows x64 target and payload/layout agreement. |
| [`ValueLayout.cs`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs), [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) | Scalar sizes include all eight privately gated identities. Constant verification checks typed bits and value domains. Native constant lowering writes both words of the four 16-byte types into spill storage; other paths still use the guarded legacy accessor. This is not evidence that every operation, placement or call route is complete. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `PutExpression`, `GetExpression`, `WritePrivateType`, `GetType`, `ReadModuleHeader` | The current structured writer/reader select exact **103**, composing the NoThrow/output carrier from the revision-92 candidate with later storage-address, array, field-path and layout changes. Constant tag 1 still writes/reads one i64 after its type/span envelope; the type helpers reject the eight candidate-94 identities, including declarations without constant leaves. Revision 94 remains a separate unapproved exact candidate, not an alias or the current writer selection. |
| [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs) | Graph-library reads share the header/function codecs; graph, set and ordinary linking have distinct restoration/remapping routes. Migrating only an ordinary root reader is insufficient. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | TNPK/TNSP/TNGP currently select outer **3**, retain structured Contract bytes separately from images and include checksums. Older proposals' outer-2 recommendations are not the current framing. |
| [Implementation inventory](../implementation/modules.md), [conformance handoff](../implementation/conformance-gaps.md#cg-006) | The former's limited-materializer paragraph is stale relative to source and the latter's all-24 carrier account. Neither inventory prose nor a test name supplies missing codec or execution evidence. |

Preserve the accepted [26-scalar source floor](../specs/overview/conformance.md#mandatory-scalar-catalog),
[unit semantics](../specs/language/expressions-and-statements.md#the-zero-data-void-value),
[exact-bit constant identity](../specs/representation/encoding-canonicality.md#5-constant-values-and-exact-bits),
[target storage](../specs/targets/windows-x64-data-layout.md),
[consumer checks](../specs/representation/reader-trust.md) and
[binding relation](../specs/representation/binding-and-replacement.md).
The proposed private leaf codec intentionally replaces the current i64
record, not these contracts. Under the
[persistence taxonomy](../specs/representation/persistence.md), neither it
nor a native container becomes standard `.trrm` interchange.

## Verified closed catalog and widths

The following table derives from the atomic registry, scalar reader,
materializer, `PrimitiveType` and `PrimitiveTypeFacts.ScalarWidth`, not from
the number of types mentioned in an earlier draft. `N` is the proposed
scalar payload width. Windows x64 object size/alignment is also `N`,
including zero for unit; internal object layout and ABI slots are not `N`.
The private identities are i32 ordinals, **not casts of atomic codes**.

| Standard atomic code | Complete scalar | Host/private identity | N bytes |
|---|---|---:|---:|
| `00` | void | 0 | 0 |
| `01` | bool | 1 | 1 |
| `02` | char8 | 32 | 1 |
| `03` | char16 | 4 | 2 |
| `04` | char32 | 33 | 4 |
| `05` | rune | 34 | 4 |
| `06` | int8 | 35 | 1 |
| `07` | int16 | 36 | 2 |
| `08` | int32 | 2 | 4 |
| `09` | int64 | 5 | 8 |
| `0A` | int128 | 39 | 16 |
| `0B` | uint8 | 37 | 1 |
| `0C` | uint16 | 38 | 2 |
| `0D` | uint32 | 3 | 4 |
| `0E` | uint64 | 6 | 8 |
| `0F` | uint128 | 40 | 16 |
| `12` | float16 | 41 | 2 |
| `13` | bfloat16 | 42 | 2 |
| `14` | float32 | 14 | 4 |
| `15` | float64 | 15 | 8 |
| `16` | float128 | 43 | 16 |
| `17` | decimal32 | 44 | 4 |
| `18` | decimal64 | 45 | 8 |
| `19` | decimal128 | 46 | 16 |

Identities **39..46** already exist in the host and are exactly the eight
rejected by the revision-92 private gate. Retain their proposed revision-94
assignments. They are below the inline-structure range starting at 100 and
disjoint from managed-class, native-function-pointer and native-pointer
ranges. No standard number is allocated. Private type 41 and expression
tag 41 belong to different namespaces; the latter remains NativeOutput
reservation, not a floating-constant escape.

Atomic `10/11` (`nint/nuint`) are deliberately outside this 24-type codec.
Their eight-byte Windows layout does not select frozen-value identity,
target ownership or a Feature-12 binding. Do not substitute int64/uint64.
Nominal enums/Transparent wrappers, aggregates, optionals, TypeIdentity,
addresses and family/qualified/formal types likewise do not become these
scalars by equal size. Equivalent atomic TypeSpec rows and F2 references
resolve to the same complete scalar; nominal identity is not erased.

## In-memory typed-bit invariant

Use the existing immutable two-u64 carrier, with bits 0..63 in `Low` and
64..127 in `High`. The complete type remains in the containing bound
constant; add no second discriminator or mutable byte-array ownership.
The carrier has 16 bytes of bit capacity, not a promised CLR record layout
or a 16-byte serialized representation for every value.

At checked construction/consumption boundaries require:

* `N=0`: both words are zero. Unit is one initialized logical value, not
  missing input, vacancy, null, bottom or an addressable zero-byte payload.
* `N=1/2/4`: `High=0` and every bit of `Low` above the N-byte container is
  zero. `N=8` requires `High=0`; `N=16` uses both words completely.
  Reject nonzero unused bits rather than treating them as padding.
* Signed integers use the exact N-byte two's-complement container;
  unsigned integers use the exact unsigned container. Narrow negatives
  are not sign-extended into unused storage. Numeric interpretation,
  widening and arithmetic are explicit operations, not copying.
* Boolean admits only byte 0 or 1. Rune admits `0..10FFFF` excluding
  `D800..DFFF`. Code units admit every unsigned container, including
  char16 surrogates and char32 values above Unicode's range.
* All five binary formats preserve their entire declared sign/exponent/
  fraction container. All three decimal formats preserve the exact
  [BID container](../specs/targets/windows-x64-data-layout.md#3-ieee-decimal-bid),
  not DPD, CLR decimal or a host numeric serialization. Preserve signed
  zeros, subnormals, infinities, NaN sign/state/payload, decimal quantum/
  cohort, noncanonical finite/NaN encodings and infinity trailing bits.
  These permitted noncanonical decimal containers are not invalid scalars.

Copying, hashing, interning, private writing, loading and materialization
perform no floating arithmetic, NaN quieting, numerical normalization or
format/parse round trip. The
[constant-operation NaN recipe](../specs/language/floating-environment.md#4-deterministic-constant-nans)
applies at its semantic operation, not at serialization. Typed-value
identity is normalized complete type, selected codec and every meaningful
payload bit. Span, private row number and the cached legacy value are not
part of that identity; use an explicit value key rather than bound-record
equality with incidental fields.

Retain the guarded long convenience API only for its existing exact
interpretations. Signed integer values, uint32's signed-i32 container,
uint64's unchecked i64 container, and floating raw bits are not one signed
conversion to 128 bits. Validate legacy input in its original type domain
**before** projecting bits: masking an out-of-range int8/uint8 or invalid
Boolean into a valid container must not legalize it. An N=16 value never
yields a long, even when its high word is zero; keep explicit failure, not
truncation, approximation or a zero fallback. Operations needing a number
decode the exact type under their own admitted semantics.

Keep non-scalar native literal indexes and pointer nulls logically distinct.
The scalar width predicate is closed; its unknown/non-scalar result is not
permission to serialize an arbitrary address as eight scalar bytes.

## Exact private schema and composition

All records below are **candidate private persistence**, not a standard
TypeSpecRef, ConstantSource, Feature revision or header major/minor pair.
For scalar tag 1 replace the unconditional i64 with:

```text
u8  expressionKind = 1
i32 privateType
i32 spanStart
i32 spanLength
u8  payload[N]
```

Every multibyte field is little-endian. `N` comes only from the complete
private scalar identity in the table. Byte zero holds bits 0..7; a 16-byte
payload contains low u64 then high u64, each little-endian. There is no
length prefix, local codec/tag, optional high word, Blob reference, padding
or reserved tail. The expression is exactly **13 + N bytes**: unit 13,
Boolean 14, a 128-bit value 29. An eight-byte scalar remains 21 bytes;
that overlap with old records does not select the old schema.

Unknown types do not select a guessed width or generic i64 decoder. Keep
the separately admitted non-scalar tag-1 cases unchanged:

* `NativeRawPointer` and a validated `NativePointerTypes` identity: one i64
  payload, exactly zero.
* `NativeUtf16ZPointer`/`NativeUtf8ZPointer`: one i64 literal index, validated
  against its matching literal collection. Index zero is its first literal,
  not null. This does not supply their missing private literal-table codec.
* Native-function-pointer identities, managed handles and other types are
  not new BoundConstant cases. Function addresses retain their distinct
  admitted expression and lifetime/binding obligations.

Producers establish the original typed value and emit truthful facts.
Readers check exact remaining payload extent, source spans, enclosing
counts/depth/stream bounds, complete type agreement and Boolean/rune
validity before affected use. These structural/admission duties do not
require replaying or semantically re-proving arbitrary producer bodies.
Retain unsupported-route rejection. Validate every type-bearing declaration,
field, signature, variable, reservation and native expectation, not only
the constant envelope. No other expression schema changes.

The revision composition is exact, not inferred from numeric ordering:

| Candidate | Complete selection |
|---|---|
| 89 | [Arithmetic persistence](source-arithmetic-private-persistence.md): revision 88 plus resolved checked/wrapping operation bytes; old output/NoThrow carriers remain. |
| 90 | [Output persistence](aggregate-native-output-private-persistence.md): exactly 89 plus the normalized output collection, its old-vector/suffix removals and reservation tag 41. |
| 91 | [NoThrow retirement](no-throw-private-persistence.md): exactly 89 minus its declaration Boolean and NoThrow/MayThrow contract tokens; no new output collection. |
| 92 | Exactly 91 plus the complete output delta from 90, using the explicitly amended [NativeOutput cell eligibility](aggregate-native-outputs.md#standard-path-prerequisite-and-private-persistence). This is a candidate composition within the current revision-103 writer chain, not the scalar codec or an independently selected current header. |
| 93 | [Declaration provenance persistence](reference-provenance-carriers.md#private-exchange-candidate-revision-93): exactly 92 plus its mandatory general-dependency collection, disjoint from output-specific dependencies. |
| **94** | Exactly that 93 plus the scalar catalog admission and tag-1 payload above. This retains the existing scalar candidate's meaning, not another allocation. |
| 95, only when separately selected | [Field-copy provenance](reference-field-value-provenance.md#necessary-private-transport-candidate-95): the complete 94 plus that owner's declaration summary field. Do not drop that field when integrating scalar persistence into a destination already selecting 95. This proposal changes neither its schema nor its applicability. |

Revision 93's collection is present on every ModuleFunction, including
bodyless expectations: i32 count and exactly 12 bytes per row, after the
output collection and before the expected-vtable presence Boolean. Preserve
its exact tag/ordinal/default/phase rules. A zero count cannot stand in for
required relationships. Persist declarations, not the runtime-instance
ledger. The [root/pin runtime candidate](reference-root-pin-runtime.md)
allocates no competing 94 and is not an implicit runtime prerequisite for
independent scalar bits.

At the 94 integration boundary select `TRRM` plus little-endian **i32 94**
for every structured artifact, including those without constants. Update
all writer/header, nested-reader, role/type/layout and producer-validation
allowlists together. Ordinary, graph-library, set and snapshot paths must
select the same complete schema. Reject 92/93, older and unknown revisions
before record decoding; do not retain historical readers, add aliases,
retry alternate decoders or promote headers. Where 95 is separately
selected, use its exact complete schema instead, not a permissive `>=94`
layout or a constant-only variant.

The [format policy](../specs/representation/format-versioning.md) requires
exact draft agreement independently of equal version bytes. No compatibility
obligation justifies keeping the old scalar record. Conversely, bytes valid
under both schemas do not reveal provenance: a consumer cannot promise to
detect every old artifact whose header was dishonestly changed.

## Source-free materialization and native persistence

The standard-to-bound bridge resolves the using Module's ConstantMap,
ConstantSource tag 1, FrozenValueNode kind 1 and Blob in their actual
namespaces. Check provisional Feature 11/1, complete scalar type agreement,
node zero flags/reserved/child/case fields, exact Blob extent and value
validity before use. Unit requires **zero Blob reference**, not an empty
Blob alias. Own the validated bits after loading; do not retain a mutable
borrow of the input bytes.

Materialize without source text, constructors, parsers or replaying the
producer. Preserve the selected Module/target and required Blittable,
representation, size/alignment and binding facts. The current materializer
explicitly selects Windows x64; another target needs an admitted
bit-preserving mapping or rejects affected use. Unit has no positive-width
alignment/placement requirement. Serialized byte order does not select a
target or a foreign ABI.

Shared construction, traversal/rewriting, verification, folding/value keys,
dependency restoration and lowering must carry the complete payload.
Audit every `Value`, deconstruction and long-sized assumption; an added high
word alone is not integration. Existing supported 16-byte lowering must
preserve both words through staging, locals/fields, calls and results.
The [Windows call profile](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults)
distinguishes hidden-buffer integer/decimal128 results from float128's XMM0
result. Do not infer either from storage width or reinterpret a compiler
temporary as a source loan. Unsupported placements, operations and call
routes remain explicit gates; this is no universal scalar runtime.

All eight privately gated identities still lack a direct foreign primitive
equivalent in that profile. Lossless persistence does not admit those foreign
signatures, implicit conversion, marshalling or helper dependencies. A frozen
result also does not erase a separately retained evaluation-form, query,
layout or executed dependency. ConstantSource tags 2/3 and their Feature-12
closure remain separately gated, not runtime fallbacks for missing values.

Keep the [provider-binding candidate's](native-output-direct-import-provider-binding.md)
**outer version 3** TNPK/TNSP/TNGP framing and expectation table unchanged.
Embed the exact selected inner 94, or complete 95 when separately selected.
This intentionally supersedes older outer-2 recommendations and that
provider-binding draft's standalone inner-92 selection for this composition.
Admit outer/inner schemas independently for producers, consumers, graph
roots/intermediates/leaves and snapshots; a valid PE/checksum cannot bypass
unsupported Contract semantics.

Recompute Contract lengths, affected offsets and SHA-256 checksums after
encoding. Contract growth is not image length. Preserve expectation-table
type references in the importing Contract's universe. Images and retained
IR must derive from the same checked values, target and binding; regenerate
affected images rather than patching their semantic headers. Stored-image
AOT and graph root-only JIT remain distinct routes. No new outer version,
native-profile label, image-only admission, silent JIT or source-rebinding
fallback is selected.

## Remapping, replacement and resources

Standard transformation remaps complete TypeSpecRefs, ConstantSource rows,
FrozenValueNode rows and Blob references separately, preserving each using
Module/substitution/target context. Rebuild Blob indexes, section extents
and directories; reorder/deduplicate only while retaining typed values.
Remap every affected Body ConstantMap/TypeMap and other actual map reference,
including instruction/typed-record indexes, masks/count words, instruction
forms/extents, Blocks, record offsets and SourceMap Body/step slots.
Preserve optional source steps only when their semantic meaning survives;
otherwise regenerate or drop them under their existing owner, never use
them to carry required scalar meaning.
Opcode-, token- or address-looking scalar bytes are **never references**.
Ordinary valid duplicate/reordered rows remain accepted under their owner;
wrong types/widths/reserved fields are not noncanonical alternatives.

Private IR adds no Blob/value table. Primitive identities and scalar bits
survive structure pruning/reordering unchanged. Preserve complete enclosing
structure/class, field/function, native-pointer/function-pointer and actual
literal-index remapping, including declaration/output/general-dependency
collections, optional revision-95 summaries and outer native expectations.
Cloning a constant or rebuilding its parent preserves both words; do not
rewrite scalar payload words because they coincide with another table's ID.

Linking and replacement retain original selected provider/type origins,
caller expectations and actually relied-on value/layout/code dependencies.
Where an admitted promise requires an exact scalar, compare its complete
type and every payload bit after substitution/remapping. A changed high word,
zero sign, NaN state/payload or decimal cohort cannot satisfy that promise by
numerical equality. Do **not** add every private function-body constant to
import equality: an ordinary compatible replacement may change an unpromised
result. This proposal supplies no missing exported-constant or dependency
codec; unsupported required promises reject rather than disappearing.

Preserve existing checked size/node/depth/slot/container limits and scoped
resource accounting. Width lookup is closed and at most 16, never an input
allocation length. Check record/count/offset arithmetic after size changes.
Standard typed rows retain their 24-bit limits and zero reserved high byte;
Body indexes remain zero-based u16 with counts through 65,536. Loading frozen
bits is not a ConstantExecution request and invents no execution-resource
charge or portable budget.

## Required tests and integration gate

These are acceptance requirements, not claims that this documentation pass
ran them. Use existing public compiler/module/native entry points,
independently authored bytes and unrelated synthetic providers. Do not add
InternalsVisibleTo or rely only on matching writer/reader round trips.

| Area | Positive, negative, boundary and cross-feature cases |
|---|---|
| Catalog and exact records | Cover all 24 scalars and every N; check each standard/private mapping independently. Unit occupies 13 bytes and no payload; 16-byte scalars occupy 29. Test constants adjacent to other expressions so wrong extents cannot hide at EOF. Unknown identities, family/qualified/formal/nominal lookalikes and nint/nuint reject on this path. |
| Integer and legacy boundaries | Every signed minimum/maximum and unsigned maximum; bits 63, 64 and 127; same low word with different high words; N=16 values with zero high word still reject legacy access. Preserve signed narrow containers and uint32/uint64 high bits. Invalid original long inputs must not become valid by masking. Equal bytes under different complete types remain distinct; changing a span/cache does not change value identity. |
| Binary and BID observations | All binary formats' signed zeros, subnormals, finite extremes, infinities and both NaN states/signs/payloads. All BID formats' equal-valued cohorts, quantized/signed zeros, finite steering/noncanonical coefficients, noncanonical NaN payload/reserved bits and infinity trailing bits. Exact copy/load/write/interning preserves them; separately admitted sign/processing operations retain their different contracts. |
| Validity and malformed framing | Boolean 0/1 versus 2; maximal char32 versus maximal rune; char16 surrogates versus rune rejection; nonzero unused carrier bits; every truncated payload width. Reject missing/zero/wrong-table/high-byte/out-of-range required references, wrong Blob extents, unit with a Blob, bad node fields, map/node/type disagreement, missing Feature and absent/wrong target. Preserve valid noncanonical map forms. |
| Revision composition | Independent exact-94 fixtures include arithmetic modes, no retired NoThrow fields/tokens, reservation tag 41, empty/nonempty output collections and required general-dependency rows, including bodyless declarations. Separately test complete 95 summaries when that candidate is selected. Reject other inner revisions with no decoder retry. Header-edited old bytes that remain valid under the selected schema cannot be rejected merely for guessed provenance. |
| Rewriting and capacities | Reorder/deduplicate standard type/source/node/Blob rows and maps; exercise Body indexes 0 and 65535, count 65536, typed-row boundary/high-byte checks and changed SourceMap slots. Reorder private structures, fields, functions, pointer tables and diamond providers. Token/index-looking payload words remain identical. Test checked extents and resource exhaustion without success-shaped partial results. |
| Source-free binding/replacement | Restore producer/reference/consumer, sets and graph root/intermediate/leaf without source. Compare every retained payload byte after rewriting. Compatible unpromised body-result changes remain allowed; changed retained exact-bit promises or target/layout/provider obligations reject. Preserve original output/general-dependency and, when selected, copy-origin expectations together. |
| Native execution and containers | Outer-3/inner-94 and separately selected complete-95 package/set/graph/snapshot routes, both existing runtime policies where admitted. Recompute corrupt-inner fixture checksums to reach inner admission. Distinguish PE emission, stored-image execution and root-only graph JIT. For admitted identity/copy call routes, observe both words/all returned bytes under the selected call ABI; invoking and discarding a result does not prove its bits survived. Keep unsupported direct foreign signatures and operation routes rejected. |

The existing
[`TrRmFrozenScalarMaterializationTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFrozenScalarMaterializationTests.cs)
contains all-24 source-free materialization cases and guarded-wide-access
checks. Its wide identity-call execution case discards the result; it is not
an exact returned-bit oracle or private-94 round trip. Retain those tests
and add the missing byte/codec/binding observations rather than treating
their names as completed acceptance.

Complete the revision-93 declaration carrier and shared typed-bit migration
before enabling 94, or integrate both directly into the complete destination
schema. Do not emit a reduced 94 with required relationships omitted.
Update implementation inventories only after validation covers the final
relevant patch, selected compiler/configuration and actual admitted routes.
Passing this bounded gate does not close
[CG-001](../implementation/conformance-gaps.md#cg-001),
[CG-006](../implementation/conformance-gaps.md#cg-006),
[CG-007](../implementation/conformance-gaps.md#cg-007) or the
[constant-execution floor](../implementation/conformance-gaps.md#cg-010).

## Remaining decisions and exclusions

No unresolved width, host-carrier, scalar identity, payload-framing or
revision choice prevents this bounded recommendation. The exact carrier
policy already exists as a draft; persistence integration and qualification
are remaining work, not permission to invent another policy.

Formal approval of the private composition and the separate standard
Feature-11 registration remains pending. Native-width frozen identity/
target binding, nominal/composite/immutable values, ownerless standard
constant contexts, general reference/root/pin runtime support, broader
standard producers/rewriters/register execution and unimplemented scalar
operation/call routes retain their own decisions or implementation gates.
No compatibility alias, fallback decoder, universal runtime, standard
schema approval or stability promise follows from this proposal.

## Alternatives

**Selected minimal amendment:** reuse the current two-word host carrier and
the existing type-derived revision-94 leaf. It preserves one typed-bit rule
through all widths and avoids another scalar form. Preserving the old
eight-byte narrow/unit payload while adding a 16-byte wide variant is a
smaller textual codec change, but retains legacy signed-container conventions
on the wire, contradicts the selected 94 draft and still requires exact
schema migration. No unpublished compatibility obligation warrants it.

A substantive redesign could use a length-prefixed byte string, Blob-backed
private value pool or universal numeric object. A Blob pool may help future
aggregate sharing but adds reference/remapping/ownership state without
additional scalar meaning; length prefixes duplicate the complete type's
width. Arbitrary-precision numbers cannot themselves preserve floating/BID
containers, while a universal tagged runtime exceeds this value-transport
decision. Unconditional two-u64 wire records avoid variable leaf extents but
spend 16 bytes on unit/Boolean and add unused-bit rules to every input.
None is selected. Future richer values require an explicit design, not
speculative generalization of this bounded carrier.
