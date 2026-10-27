# Private persistence of scalar frozen values

**Status: superseded, unapproved historical proposal.** The original
recommendation supplied a private scalar codec candidate, not approval of
assignments, standard registration, compiler support or conformance. Its
source observations below reflect the earlier worktree, not the current
implementation inventory. No code, sample or normative owner was amended by
the original recommendation.

> Superseded as the candidate owner by
> [Wide scalar value carriers](scalar-wide-value-carriers.md). This document
> remains historical rationale only; it is not an alternative revision-94
> codec or implementation target.

## Decision, scope and prerequisites

Recommend **candidate private semantic-module revision 94**, exactly the
composed revision-93 candidate plus the scalar identity and constant-payload
changes below. A constant's complete scalar type determines its exact payload
width; replace the current unconditional eight-byte value with that many raw
little-endian bytes. Use one lossless in-memory scalar carrier through
materialization, verification, rewriting, persistence and lowering.

The prerequisite [scalar frozen-value registration](scalar-frozen-value-registration.md)
owns the proposed Feature 11/1 domain and binary/BID representations. Reuse its
**24 fixed-width built-in scalars**, including unit and all narrower forms.
Do not silently widen its scope to the source catalog's native-width
`nint/nuint`, nominal wrappers/enums, aggregates, addresses or TypeIdentity.
This private proposal does not register Feature 11, revise its standard rows,
add a standard Blob codec or change any standard/development `.trrm` schema.

Prerequisites are that exact scalar proposal, the arithmetic, NoThrow,
native-output and declaration-dependency composition specified below, and
complete typed-bit preservation in the compiler. Implementing the declaration
codec from revision 93 does not require inventing its separately gated general
reference/root/pin runtime. Unsupported types, operations, dependencies and
target routes retain their rejection gates. No source spelling, constant
instruction, exported constant family or general type-table redesign is
selected here.

## Source evidence and owners

These are source observations, not test or execution evidence.

| Owner | Relevant fact |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundConstant` | Its only value field is `long`. It also serves non-scalar native literal indexes and null pointers; those are not the Feature 11 scalar domain. The current `PrimitiveType` catalog represents 16 of the proposed 24 scalars, not int128/uint128, float16/bfloat16/float128 or decimal32/64/128. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `PutExpression`, `GetExpression` | Existing constant tag 1 has the common `u8 kind, i32 type, i32 spanStart, i32 spanLength` envelope, then `writer.Write(constant.Value)` / `ReadInt64`. `GetType` reads an implementation-private i32 identity, not a standard TypeSpecRef. The current final writer and header reader select exact revision 92; tag 41 and the output collection are already present. |
| [`TrRmFrozenValueNodeTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs) | The provisional structural scalar path validates widths through 16 bytes, zero-width unit, Boolean/rune validity and exact Blob containers without using host floating values. |
| [`TrRmConstantSourceValueValidator.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs), `MaterializeFrozenScalar` | Checks the using Module, complete scalar TypeSpec agreement and Windows x64 selection, then maps only the existing bound scalar subset into `long`. Unmapped formats currently decline; accepting their Blob is not full materialization support. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`Graph`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), main-code remappers | Bodies and declarations pass through structure/class, native-pointer and function-pointer remapping, symbolic resolution and source-free restoration. `ReadGraphLibrary` is a separate entry sharing the header/expression codec. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | Current outer TNPK/TNSP/TNGP versions are 3, not the version 2 described by the older arithmetic/output/provenance proposals. They retain structured Contract bytes separately from their images and checksums. |

The [persistence taxonomy](../specs/representation/persistence.md),
[format policy](../specs/representation/format-versioning.md),
[exact-bit canonicality](../specs/representation/encoding-canonicality.md#5-constant-values-and-exact-bits),
[consumer boundary](../specs/representation/reader-trust.md) and
[binding relation](../specs/representation/binding-and-replacement.md)
remain authoritative and unchanged. The [implementation inventory](../implementation/modules.md)
records support separately; older proposals' source-baseline tables are not
claims about the current worktree.

## Exact composition and selection

The following deltas are cumulative only as explicitly stated, not by numeric
comparison. Keep every occupied candidate's meaning:

| Private revision | Exact composition |
|---|---|
| 89 | [Arithmetic persistence](source-arithmetic-private-persistence.md): its revision-88 baseline plus resolved checked/wrapping mode bytes; retains the old NoThrow and output carriers. |
| 90 | [Output persistence](aggregate-native-output-private-persistence.md): exactly 89 plus its normalized declaration output collection, legacy-vector/suffix removals and reservation tag 41; does not retire NoThrow. |
| 91 | [NoThrow persistence retirement](no-throw-private-persistence.md): exactly 89 minus its specified declaration Boolean and NoThrow/MayThrow contract tokens; no new output collection. |
| 92 | Exactly 91 plus the complete output delta from 90. For this composition select the explicitly amended [scalar/pointer output-cell eligibility](native-pointer-output-ownership.md#exact-candidate-representation-delta), not an inference from equal 92 bytes. |
| 93 | [Reference provenance carriers](reference-provenance-carriers.md#private-exchange-candidate-revision-93): exactly that 92 plus its mandatory general declaration-dependency collection, disjoint from output-specific dependencies; no persisted runtime-instance ledger. |
| **94** | Exactly that 93 plus the eight missing private scalar identities and tag-1 scalar payload replacement specified below. No other field, tag, collection, operation meaning or rule version changes. |

Revision 94 is unoccupied in the inspected implementation and proposals.
The [root/pin proposal](reference-root-pin-runtime.md) expressly allocates no
private 94; it supplies neither an alternate 94 layout nor permission to
serialize transient provenance. This proposal assigns no root/pin carrier.

At this integration boundary all new structured writes and the current
reader select **exact 94**, even without constants or with empty collections.
Replace the current exact-92 selection; do not emit a constant-only variant
on 89, 91, 92 or 93. The complete prerequisite fields may be integrated
directly into 94 without first producing intermediate artifacts, but none
may be omitted. Revision 93's declaration-codec integration remains a real
prerequisite, not an empty-count substitute for required relationships.

Reject other revisions, including 89–93 and unknown higher values, before
record decoding. Add no historical reader, compatibility alias, automatic
converter, alternate-decoder retry or header-patching facility. The older
arithmetic proposal's retained historical readers are not selected at
this later exact-schema boundary. Update header/structure-role/producer
allowlists and fixed-layout/span/pointer/callable/arithmetic/output gates
together, including graph-library and snapshot reads; a higher version
constant or `>=` test alone is not admission.

The private header remains `TRRM` magic plus little-endian **i32 94**.
It is not the standard u16 major/minor pair, a Feature revision or a native
profile. Equal 94 bytes still require agreement on this complete draft,
including the selected amendments and scalar catalog. Filename, payload
plausibility and outer-container version cannot select another schema.
Some unchanged 64-bit constant records naturally have the same bytes in
93 and 94; that overlap neither selects a source schema nor authorizes an
old decoder. Validation cannot infer producer provenance from bytes valid
under both schemas.

## Lossless scalar carrier and exact expression schema

### Typed bits, not a wider host number

Use an immutable scalar payload with two unsigned 64-bit storage words,
low word first logically, and the complete scalar type. The type determines
the meaningful width; unused high storage bits are zero, and unit has both
words zero. Reject invalid in-memory carriers rather than masking away
unexpected bits. This is a compiler representation, not a new source type
or an instruction to serialize 16 bytes for every value.

Identity is **normalized complete type, selected scalar codec and every
meaningful payload bit**. Signed and unsigned types, float16 and bfloat16,
equal-valued decimal cohorts, distinct NaN containers and positive/negative
zero remain distinct. No host float, double, decimal, numeric equality,
formatting/parsing round trip or arithmetic conversion may mediate copying,
hashing, interning, loading or writing.

Migrate the old `long` construction/use sites with their documented per-type
interpretation: signed integer values, uint32's signed 32-bit bit-pattern
uses, uint64's unchecked 64-bit container, and float32/64 raw bits are not one
uniform signed-to-128 conversion. Validate the old caller's representation
before constructing the exact-width bits. Remove or explicitly restrict
64-bit convenience accessors; a wide value must never yield a truncated
`Value`, default zero or a numerically converted approximation. Operations
that require numeric interpretation must deliberately decode their exact
type under their own already-admitted semantics.

Keep native literal indexes and pointer nulls distinct in the in-memory
model. Their independently admitted existing private representations remain
unchanged; this is not permission to persist a missing native literal table,
freeze an arbitrary address or acquire pointer provenance.

### Complete scalar identities

Keep the envelope's existing private i32 type field and all existing type
identities unchanged. It must distinguish every scalar in the width table,
not just its width or signedness. The following **unapproved private**
identities complete the eight missing forms:

| Private i32 identity | Scalar |
|---:|---|
| 39 | int128 |
| 40 | uint128 |
| 41 | float16 |
| 42 | bfloat16 |
| 43 | float128 |
| 44 | decimal32 |
| 45 | decimal64 |
| 46 | decimal128 |

These assignments are necessary to make the proposed 24-type private schema
exact without inventing a second type discriminator or a type escape.
The inspected `PrimitiveType` declarations end at uint16=38; these values
do not collide with the structure range starting at 100, managed-class
ranges, native-pointer/function-pointer identities or retained private
ordinals. They are **not** standard atomic codes, TypeSpec row numbers or
expression tags; the independent expression tag 41 retains its output
reservation meaning. No normative numeric assignment is proposed.

Translate between the complete TypeSpec identity and this private catalog
explicitly, not by casting standard codes. Accept equivalent standard
TypeSpec-row and inline atomic spellings only after resolving them to the
same unqualified built-in scalar. A nominal/enum/Transparent wrapper,
qualification, family construction or unresolved formal does not become
that scalar because its layout matches. Preserve the using Module,
substitution and target/binding context of every retained use.

### Tag-1 payload replacement

For each of the 24 scalar types the exact record is:

```text
u8  kind = 1                     // existing constant expression tag
i32 privateType                  // exact scalar identity
i32 spanStart
i32 spanLength
u8  payload[N]                   // N derived only from privateType
```

All multibyte fields are little-endian. There is **no** payload length,
scalar-local type/tag/codec field, sign extension, padding, alignment,
reserved tail, Blob reference or optional high-word presence bit. The
record extent is exactly **13 + N bytes**. Unit is a real 13-byte expression,
not an absent expression or a void return's missing value.

| Complete scalar type | N bytes | Container/validity |
|---|---:|---|
| void | 0 | Singleton unit, no payload bytes. |
| bool; char8; int8; uint8 | 1 | Boolean exactly 0/1; raw unsigned code unit; two's-complement signed or unsigned integer respectively. |
| char16; int16; uint16; float16; bfloat16 | 2 | Raw code unit including surrogates; exact integer or declared binary container. |
| char32; rune; int32; uint32; float32; decimal32 | 4 | All char32 containers; rune only Unicode scalars; exact integer/binary/BID container. |
| int64; uint64; float64; decimal64 | 8 | Exact integer/binary/BID container. |
| int128; uint128; float128; decimal128 | 16 | Complete integer/binary128/BID128 container, including the high 64 bits. |

Byte zero contains bits 0–7. A 16-byte payload is the low u64 in little-endian
order followed by the high u64 in little-endian order. Narrow negative
integers occupy their N-byte two's-complement container, not an eight- or
16-byte sign extension. Unit consumes zero bytes; every other width is exact.
The binary/BID field interpretations are those selected by the scalar
registration proposal and [Windows x64 layout](../specs/targets/windows-x64-data-layout.md#2-binary-floating-representation).
All declared binary and BID containers are preserved, including signaling
NaNs, infinity trailing bits and noncanonical finite/NaN decimal containers.
Serialization canonicality does not numerically canonicalize them.

The non-scalar tag-1 null-constant cases retaining the old eight-byte i64
payload are exactly those admitted by
[`SemanticVerifier.Expression`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs):

* `PrimitiveType.NativeRawPointer`, with payload exactly zero.
* A `NativePointerTypes` identity validated by
  `NativePointerTypeContract.IsValidType` against the program's native-pointer
  table, with payload exactly zero.

`PrimitiveType.NativeUtf16ZPointer` and `PrimitiveType.NativeUtf8ZPointer`
constants instead carry i64 **literal indexes**, checked against their
respective `NativeUtf16ZLiterals`/`NativeUtf8ZLiterals` collections. Index zero
selects the first literal; it is not a null-pointer constant. Preserve their
existing index semantics and literal ownership/bounds checks wherever that
representation is independently admitted; missing private literal-table
support still rejects.

`NativeFunctionPointerTypes` identities are **not admitted `BoundConstant`
forms**, including payload zero. They do not enter the native-pointer null
branch; native function addresses retain their separate expression form.
This clarification authorizes no new null form. Unsupported non-scalar
constants still reject. Decode the complete type before selecting a payload;
unknown types never select a guessed width or generic eight-byte fallback.

No other expression changes. In particular, arithmetic mode bytes, source
argument order, unit-typed reservation tag 41 and both declaration collections
retain their composed definitions.

### Alternatives

* **Selected: type-derived fixed payload.** The mandatory complete type
  already determines 0/1/2/4/8/16 bytes. There is one exact representation
  and no redundant length to disagree with that type.
* **Length-prefixed byte string or Blob-backed constant.** Both preserve
  bits, but add a count/reference and validation/remapping state to a bounded,
  reference-free leaf. Importing the standard source/node/Blob chain into
  private IR duplicates its table ownership without retaining additional
  scalar meaning. Neither is selected.
* **Two unconditional u64 wire words.** Simple fixed framing, but spends
  16 bytes even on unit/Boolean and needs unused-bit/padding rules in every
  ordinary input. Keep two words as an implementation carrier, not this codec.

Keeping an eight-byte low word with a new wide tag or optional high word
would create parallel scalar forms and still need exact schema selection.
No historical compatibility requirement justifies that split.

## Producer, consumer and target obligations

1. **Producers establish the value.** Source/public-bound-program construction
   validates complete type, exact meaningful bits and source validity before
   emission. Preserve all declared scalar observations and retained
   requirements; never truncate, infer a missing high word, replay a producer
   program, or substitute runtime evaluation for a required frozen value.
2. **Readers check selected input.** Validate exact revision, type domain,
   remaining N-byte extent, source spans, enclosing counts/depth/artifact
   bounds, Boolean and rune domains, declaration/body type consistency and
   actual required support before affected use. Validate the full private
   type catalog in every type-bearing field, not only constant leaves.
   Unsupported is not successful decoding as int64 or a zero-sized value.
   Consumers need not semantically re-prove arbitrary producer bodies.
3. **Materialization checks the target.** Retain the using Module's exact
   selected target and required representation, size/alignment and core
   Blittable facts. Unit needs neither payload placement nor an address;
   do not apply a positive-width alignment rule to it. Serialized byte order
   does not select native endianness or a foreign call classification.
   A target lacking an admitted bit-preserving mapping rejects affected
   materialization, operations or calls; storage support alone is not an ABI.
4. **Native-width exclusion is deliberate.** Neither Windows x64's eight-byte
   nint/nuint layout, host pointer size, Blob length nor a private integer
   identity registers these types. Reject them on this frozen path; do not
   substitute int64/uint64. Their target/profile/value-identity and Feature 12
   binding decision remains with the scalar registration owner.
5. **Canonical writes preserve bits.** Emit N bytes and no unused carrier
   bits. Any interning uses exact typed-bit identity, not host object/numeric
   equality. Valid standard duplicate/reordered value rows remain ordinary
   inputs under their owner; canonical output deduplicates only identical
   typed values. No whole-artifact byte uniqueness is implied.
6. **Resources remain bounded.** Preserve current private size/node/depth/
   slot limits, with checked record/stream/container extent arithmetic.
   Width lookup is closed and at most 16, not an attacker-controlled
   allocation length. Larger values can reach an existing artifact limit
   sooner; do not silently relax it or invent ConstantExecution charges.

## References, maps, binding and replacement

The standard-to-bound bridge resolves ConstantMap type, ConstantSource,
FrozenValueNode and Blob in their **actual separate namespaces**. Retain
Feature 11/1 and using-Module checks. For unit require the scalar proposal's
zero Blob reference, not an empty Blob alias. For other types require the
exact selected Blob extent. Copy validated bytes into the owned scalar
carrier; do not retain a mutable borrowed Blob span.

Standard rewriting remaps complete TypeSpecRefs, source/node/Blob rows and
every owning declaration/use context independently. Rebuild variable Blob
offsets, section extents and directories. Preserve constant-use/type/node
agreement after substitutions. **Never rewrite scalar payload bytes as
references**, even when they resemble a token, row, private type or address.
Rebuild affected ConstantMap/TypeMap and other Body-map indexes, map masks
and count words, instruction forms/extents, Block/code/record offsets and
SourceMap Body/step slots under the existing schema. Preserve private text
spans; optional debug/source maps may be regenerated or dropped only under
their existing rules, never used to carry missing scalar semantics.

Private IR has no new Blob or scalar-value table to remap. Its fixed scalar
identities survive structure pruning/reordering unchanged; all enclosing
structure/class, native-pointer and function-pointer table references still
need their normal complete remapping, including declaration collections,
reservation pointees, signatures and native expectations. Preserve the full
payload when copying a constant or rebuilding a parent. Apply reference
remapping only to actual typed reference/index fields, including separately
admitted native literal indexes, not to scalar bits. Do not identify types
or constants by coincident numeric table IDs or equal storage layout.

Module linking, sets and diamond graphs must preserve selected provider/type
origins and each retained expectation through declaration projection,
symbolic resolution, source-free restoration and body cloning. Keep both
dependency collections and reconstruct the provenance ledger; scalar
independence does not erase a surrounding contract, executed/query/layout
dependency or code-lifetime obligation.

Do not add arbitrary private body constants to import-contract equality.
A compatible ordinary function replacement may change an unpromised result.
Where an already-admitted published or retained exact-value dependency
requires particular bits, compare its complete type and payload after
substitution/remapping, retain that expectation and revalidate affected
bindings/optimizations. A changed high word, NaN state/payload or decimal
cohort cannot satisfy an exact-bit promise merely by numerical equality.
This proposal creates no missing exported-constant or proof-dependency codec;
unsupported required contracts still reject rather than being discarded.

## Native containers and execution

Use the [direct-import provider-binding proposal's](native-output-direct-import-provider-binding.md)
**outer version 3** for TNPK/TNSP/TNGP, with its expectation table and all
field meanings unchanged, embedding exact **94** Contract bytes. This
explicitly advances that integration's inner selection from 92 through the
complete 93/94 composition; it does not reinterpret a 92 Contract as 94.
The older owners' unchanged-version-2 recommendations do not select the
current provider-binding framing. No version 4 or new `native-N` label is
needed for this inner-only change.

Admit outer and inner schemas independently throughout producer, consumer,
set, graph-root/intermediate/leaf and snapshot paths. Outer 3 alone is not
agreement on the amended complete draft. Preserve the expectation table's
private-type references in the importing Contract's universe. Recompute
Contract lengths, affected offsets and SHA-256 checksums after re-encoding;
the new scalar bytes do not enter reference remapping or the image-length
field. Regenerate old semantic/native artifacts through the selected
producer, not by editing headers or hashes.

Images and retained bodies must derive from the same checked typed values
and target contracts. Stored-image AOT and root-only graph JIT are distinct:
ordinary native execution uses the existing EXE/DLL; graph JIT restores and
re-lowers only the root, not provider DLLs. Reject unsupported inner schemas,
types, bindings or target materialization before affected image use, even
with a valid image/checksum. No image-only, source-rebinding, silent JIT or
legacy-decoder fallback is selected. Pair lossless literal emission with
separate actual execution evidence where the selected backend supports it;
byte persistence alone proves neither wide arithmetic nor native calls.

## Acceptance cases and remaining gates

These are future tests, not qualification claims. Use public compiler APIs,
independent byte fixtures and unrelated synthetic providers rather than
only matching writer/reader round trips.

| Area | Required positive, negative and boundary cases |
|---|---|
| Exact records — [`ModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/ModuleTests.cs) | All 24 types and every width; unit exactly 13 bytes, no placeholder; wide records exactly 29. Independent 94 fixtures include empty/nonempty output and general-dependency collections, no retired NoThrow field/tokens, arithmetic modes and tag 41. Check every new private identity independently of host enum casts. |
| Integer/type boundaries | Every signed minimum/maximum and unsigned maximum, especially 128-bit sign and bit 127; values differing only above bit 63; narrow negative containers and uint32/uint64 high-bit patterns. Same bytes under different scalar types remain distinct; equivalent complete atomic TypeSpec spellings remain equivalent. Nominal/qualified/formal lookalikes and nint/nuint reject. |
| Binary/BID observations — [`TrRmFrozenScalarMaterializationTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFrozenScalarMaterializationTests.cs), [`FrozenValueNode tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFrozenValueNodeTableTests.cs) | All binary formats: signed zeros, subnormals, finite extremes, infinities and both NaN states/signs/payloads. All BID formats: distinct equal-valued cohorts, signed/quantized zero, noncanonical finite/NaN containers and infinity trailing bits. Preserve bits that differ in either half of 128-bit values, including through hashing/interning; do not quiet or normalize. |
| Malformed/type/target inputs | Reject truncated 1/2/4/8/16-byte payloads, trailing bytes in complete fixtures, old eight-byte narrow/unit records under a 94 header that leave invalid fields/trailing bytes, unknown types/revisions, invalid Boolean/rune, nonzero unused in-memory bits, declaration/body/map/node type mismatch, bad typed references or Blob extents, missing Feature, absent/wrong using-Module target and unsupported layout/materializer. A same-width but genuinely different valid typed value is not intrinsically malformed; an incompatible expected type/value is. No decoder retry or claim to detect provenance in overlapping valid bytes. |
| Remapping and resources | Reorder/deduplicate standard TypeSpec/source/node/Blob rows and Body maps, using indexes 0 and 65535 and typed-row boundary/high-byte cases; rebuild source-step slots and extents. Reorder private structures, pointer/function-pointer tables, functions and diamond providers, including outer native expectations. Payloads containing token-/index-looking words remain byte-identical. Exercise private size/node/depth limits and checked arithmetic after record growth. |
| Linking/replacement | Source-free module/set/graph restoration preserves full bits and selected origins. Compatible ordinary body changes remain allowed; changed retained exact-bit promises reject before affected use. Exercise changed high words, signed zero and decimal cohort, and retain each importing expectation rather than copying one from a replacement. Keep unsupported published-value routes rejected. |
| Native packages and support gates | Independently test outer-3/inner-94 TNPK/TNSP/TNGP producer, consumer, graph root/intermediate/leaf and snapshots, including declaration-only imports. Old inner 92/93 and outer 2 inputs reject under this selection. Recompute checksums on corrupt-inner fixtures so tests reach inner admission. Separate AOT stored-image tests from root-only JIT; compatible provider replacement leaves consumer artifacts unchanged. Preserve non-scalar null/literal restrictions and all unsupported operation/ABI/indexed-body gates. |

Implementation must migrate the shared scalar carrier and all construction,
traversal, rewriting, verification, standard materialization, private
read/write, dependency/restoration and lowering paths together. Audit every
old `Value`/long-sized assumption, including constant folding, optimization
keys and literal emission; merely adding a high word to `BoundConstant`
does not satisfy this gate. Finish the composed revision-93 declaration
carrier before enabling 94 and use the existing focused module/scalar/native
tests at that coherent integration boundary. Update support inventories only
with evidence for the final relevant patch and selected configuration.

Formal approval of this private 94 schema and its prerequisite candidates
remains pending. There is no undecided width, payload, type-ID or revision
choice within this bounded recommendation. Native-width frozen registration,
richer frozen values, general reference runtime support, wide-scalar
operation/call routes and standard producer/rewriter/execution completion
retain their independent decision/implementation gates. Provisional adoption
of this carrier is not standard Feature 11 approval, completion of the
26-scalar source floor or acceptance of the compiler-adoption milestone.
