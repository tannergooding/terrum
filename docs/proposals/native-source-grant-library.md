# Native source-grant companion

**Status: deferred until a concrete native source-processing caller justifies
an invocation ABI.** The opt-in companion, scalar queries, generated-binding
experiments and owned-workspace probes are provisional evidence only.
The maintained source-free compiler-primitives DLL remains separate.

## Decision and alternatives

A source-aware native helper cannot receive invocation input by embedding a
build-time sample. A promoted path needs an explicit source extent, lifetime,
resource and result contract chosen for its consumer.

| Alternative | Consequence |
|---|---|
| Keep native source readers unsupported | Use the compilation-local host or separately deferred semantic-module source-grant adapter; no native ABI is added. Recommended until a real native consumer exists. |
| Minimal internal companion | Generate a hidden source pair and scalar results from the exact verified export set. This is rebuild-coupled, not a stable C ABI. |
| Explicit source input and typed results | Author an ordinary target-compatible source extent and status/output API. This avoids call-graph-inferred signatures and packed/sentinel workarounds, but needs reviewed public layouts, output validity, failure and lifetime contracts. |

Do not promote the minimal design solely because it is implemented, or preserve
its isolated classifiers and scalar encodings as desired permanent API policy.
A real consumer may justify the substantive explicit-input design instead.
The [self-host inventory](../implementation/self-host.md) owns the current
export list and component coverage.

## Spec integration

This would add an explicitly selected native source-context convention.
The ordinary [Windows x64 call interface](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack)
does **not** infer this pair from source bodies; its ordinary hidden result,
receiver and authored-parameter rules remain unchanged. If selected, specify
the specialized convention rather than silently treating it as the ordinary
foreign ABI.

[Native boundaries](../specs/native.md) still require directly interoperable
carriers, caller-unsafety, correct backing and code lifetime, and no implicit
marshalling, pinning, rooting or retention. Ordinary foreign boundaries are
non-unwinding. Prototype checked-policy unwind observations do not approve
cross-language exception transport. Promotion must choose a compatible
non-unwinding boundary or explicit authored status translation, not expose
hosted failure transport accidentally.

The [source-input contract](../specs/language/source-input.md) remains unchanged:
the caller supplies already-decoded UTF-16, preserving malformed surrogates,
NUL, BOM code units and line endings. Strict UTF-8 compilation-file validation
is a separate boundary. No original-byte mapping is invented.

This proposal assigns no standard TRRM metadata/opcodes and does not change
semantic-module JIT. The [source-only grant](invocation-scoped-source-grant.md),
[scanner profile](scanner-invocation-profile.md) and
[frontend profile](scalar-frontend-invocation-profile.md) are independent
decisions, not ways to infer native companion acceptance.

## Candidate source extent and calls

For the minimal experiment:

- Add a raw source address and `uint32` UTF-16 code-unit count **before**
  authored scalar arguments, only on source-dependent functions. The selected
  Windows x64 argument/stack rules then apply to the complete effective list.
- Compute dependence to a fixed point through the local call graph, including
  cleanup helpers. A source read in a private callee can change an export's
  effective ABI without changing its authored parameters or name.
- Use exactly reserved readonly `Host.Source`, one extent for the complete
  synchronous call and its cleanup. No process/thread global source,
  mid-call rebinding, retention, suspension or embedded invocation contents.
  Source-free exports keep their existing ABI.
- Require a self-contained library. Source-dependent imports, cross-DLL
  context propagation, arbitrary host buffers, scanner/frontend output
  profiles, source-dependent executables/packages/graphs/module sets and
  persisted-AOT invocation are outside this candidate.

The raw internal ABI's proposed caller preconditions are
`count == 0 || source != null` and `count <= 16384`, plus two-byte alignment,
readability of the complete extent, immutability and validity until all
synchronous cleanup completes. Null is allowed only for empty input.
The ceiling is a prototype resource envelope, not an accepted whole-file
limit.

A supported managed adapter must validate null/count shape and ceiling
**before pinning and dispatch**. It cannot establish arbitrary pointer
readability, alignment provenance or lifetime by probing memory. The raw
prototype consumes the count for bounds but does not enforce those shape/cap
preconditions itself; adapter tests are not raw-export validation evidence.
Invalid storage is outside a query's ordinary false/`-1` result contract.

## Export identity and publication

Generate descriptors and managed stubs from the **same verified selected
export set** used for emission. Include the hidden pair exactly where the
transitive source requirement demands it. Do not ask callers to inspect
bodies, infer signatures from names or maintain independent delegate lists.
Direct unmanaged calls remain an internal rebuild-coupled obligation.

If a descriptor/manifest is published, bind it to the exact emitted image
hash and export selection. Replacing an image and its manifest/stubs must
not leave a valid-looking mismatched pair after failure. A manifest derived
from a different compilation is not ABI evidence. This is an emission/binding
artifact requirement, not justification for unrelated module metadata.

## Candidate scalar result contracts

These are provisional ways to exercise existing component semantics without
exporting aggregates. They are not approved APIs or general marshalling.
Use the inventory for exact current export spellings.

### Source, ranges and lines

| Query family | Candidate result and constraints |
|---|---|
| Source length/line count | Scalar counts for the supplied immutable extent; empty-source behavior matches the source facade. |
| Code unit | `0..65535`, or `-1` for an invalid index; NUL, unpaired surrogates and `0xFFFF` remain distinct valid units. |
| Range validity | Boolean for `0 <= start <= sourceLength` and `0 <= length <= sourceLength - start`, checking subtraction before any end addition. Empty EOF ranges are valid. |
| Offset from line/character | Nonnegative UTF-16 offset or `-1`, using the source facade's accepted coordinate and line-end bounds. |
| Line/character from offset | Separate nonnegative queries or `-1`; offset/EOF validity and all line-break cases match the source facade. |
| Line ends | Separate content-end and including-break-end queries, or `-1` for invalid lines. Do not merge their meanings. |

Treat CRLF as one break; both code units belong to the preceding line, and the
following offset starts the next line. Preserve the final empty line after
a trailing break and exact EOF behavior. Scalar paired queries must use
unchanged arguments and the same snapshot. These queries may rescan; they
do not promise an indexed line table, atomic tuple or single-pass performance.

### Character and escape results

The character-analyzer candidate returns a packed unsigned 32-bit result:

| Low tag | Remaining fields |
|---|---|
| `00` | False/unrecognized slice; canonical whole word zero |
| `01` | Decoded scalar in bits 2–22; higher bits zero |
| `10` / `11` | Error 1 / error 5 respectively; 15-bit relative error start in bits 2–16 and 15-bit length in bits 17–31 |

Unsigned interpretation matters when an error sets bit 31. False, successful
NUL and a zero-length diagnostic are distinct. Relative diagnostic ranges
must fit the slice; packing relies on the selected bounded source envelope.
A separate scalar-value convenience query returns `0..0x10FFFF` on success
or `-1`, not the packed word interpreted as a signed scalar.

The string-escape candidate uses zero for false. Otherwise scalar-plus-one
occupies bits 5–25, consumed width bits 1–4, and bit 0 marks error 5; higher
bits are zero. The admitted scalar field represents `-1..0x10FFFF`, consumed
width is 1–10, and the error width equals consumed width. Keep false,
successful NUL and replacement/error cases distinguishable.

Validate exact caller slices and preserve the analyzer's own boundary
behavior. The lexer may pre-handle a line break before invoking a decoder;
direct analyzer results must not be “corrected” to match that different call
site. Packed fields are a workaround, not a reason to freeze a general
diagnostic ABI.

### Strings and row measurement

String scalar count and scalar-at-index return nonnegative values or `-1`.
Both validate the **complete exact quoted slice**, including malformed
suffixes after the requested scalar. Reject raw interior quotes, hosted line
breaks, invalid escapes and unpaired raw UTF-16. A raw surrogate pair is one
scalar; adjacent escaped surrogate values do not combine. Scalar lookup may
rescan and does not export a cursor, string aggregate or retained storage.
That bounded rule is not the accepted
[Text literal input recipe](../specs/language/literal-construction.md#text-and-character-input),
which combines UTF-16 escape units before scalar validation. A consumer
implementing that recipe must preserve its decoded-text semantics rather than
use the narrower helper as a complete source-literal oracle.

The encoded-length selector candidate is authored API data, not an enum
ordinal or the ordering of a test helper:

| Selector | Measurement |
|---|---|
| 0 | Strict ASCII bytes |
| 1 | Strict Latin-1 bytes |
| 2 | UTF-8 bytes |
| 3 | UTF-16 code units |
| 4 | UTF-16 bytes |
| 5 | UTF-32 bytes |

Return `-1` for invalid spelling/selector or an unrepresentable decoded scalar.
No replacement, normalization, BOM or terminator is added; byte order does
not change length. Under the prototype 16,384-unit grant, a quoted literal
has maxima of 16,382 UTF-16 units, 32,764 UTF-16 bytes, 49,146 UTF-8 bytes and
65,528 UTF-32 bytes. These bounds are not language encoding limits.

UTF-8 name-row measurement rejects empty decoded text and embedded zero;
namespace-row measurement permits empty text but still rejects zero.
Both validate the complete literal, enforce the 24-bit content-byte count,
then include a four-byte header, one-byte terminator and four-byte alignment.
An empty namespace row measures eight bytes. Measuring does not allocate or
emit a row and does not implement
[registered string-target binding](registered-string-target-binding.md).

### Integer and floating conversion

Full-domain integer results cannot use a failure sentinel. The candidate
`CanParseTBase`/`ParseTBase` pairs cover the five existing parser types:
identical slice/radix/snapshot, exact radices 2–36, ASCII digits, permitted
signed leading minus, radix-specific separators and destination overflow.
`Parse` revalidates and raises the existing requirement failure on invalid
input; it must not manufacture zero. A successful pair scans twice and is
not an atomic try-with-output API. Promotion must reconcile that failure
with the native non-unwinding boundary.

Hexadecimal-float, based-integer-to-float and decimal-literal queries return
target `float32`/`float64` nonnegative raw bit patterns through `int64`, or `-1` for
invalid spelling/bounds. These isolated helpers consume nonnegative magnitude
slices, keeping valid nonnegative bit patterns disjoint from the sentinel.
The [source lexer](../specs/lexical-grammar.md#numeric-tokens) still retains an
attached leading sign in its number token. A consumer must preserve that
original token/context and apply its sign, including negative zero; exponent
signs remain inside the magnitude input. Preserve positive zero and
target-defined infinity/overflow, subnormals and ties-to-even.
Round directly to the selected target; float32 must not double-round through
float64.

The input families remain distinct:

- Hexadecimal floats accept their exact mantissa/exponent and separator
  grammar; reject separators adjacent to point, exponent marker/sign or end.
- Based-integer conversion accepts exact binary/hexadecimal integer tokens,
  including admitted repeated/prefix separators, not decimal or hex-float
  spellings. Use exact significant bits, guard/sticky and target rounding.
- Decimal conversion accepts a caller-delimited mantissa with optional
  fraction/exponent, separators only between digits and no unary sign.
  A broader hosted floating parser is not itself a spelling oracle.

These are isolated conversions, not token construction, syntax parsing or
frontend verification/emission.

### Private decimal workspace

The decimal prototype uses a private 183-word owned `int32` workspace for
exact midpoint comparison. Keep exported signatures scalar-only. Internal
ordinary helpers may transfer the owner by value, use scoped local views
and the canonical allocation/count/read/set/append operations, and must
preserve normal ownership, bounds, loans and cleanup through nested calls.

Do not infer public owner/view parameters or results, owner aggregates,
arbitrary output helpers, external/nominal owner calls or cross-DLL transfers.
Workspace size and a particular converter's control-flow shape are not
general compiler admission policy. Any broader compiler implementation must
use generic verified ownership/effect facts, not a decimal-function whitelist.

Allocation failure is the selected runtime failure, **not** malformed-input
`-1`. Failed allocation has no live owner; after a successful move, the
responsible callee cleans up exactly once on every outcome. Checked native
probe evidence for that behavior is not approval to unwind through an
ordinary foreign caller.

## Acceptance gates

Promotion first needs a concrete consumer, selected minimal versus explicit
API, whole-input/resource policy and compatible failure transport. Then require:

1. Source-free ABI/artifact unchanged; transitive source classification
   independently derived, including an unchanged export whose private callee
   gains/loses a source read. Generated stubs/descriptors must match both
   effective signatures and the exact image.
2. Fresh/repeated calls with empty and maximum grants, malformed UTF-16, NUL,
   every code-unit boundary, mixed breaks, trailing breaks and EOF. Managed
   adapters reject null/nonzero and excessive counts before dispatch; do not
   dereference invalid external pointers as a validation test.
3. No embedded or retained invocation address/content, including failure and
   cleanup. Verify source context across register/stack argument transitions
   and nested helpers; source-free calls receive no hidden context.
4. Exact slices, negative/extreme bounds and all result sentinels/tags.
   Independently test character diagnostic ranges, string malformed suffixes,
   scalar index/escape consumption, NUL versus false and canonical unused bits.
5. Every encoding selector and Unicode width/representability threshold:
   U+007F/0080, 00FF/0100, 07FF/0800, D7FF/E000, FFFF/10000 and 10FFFF;
   empty/NUL name versus namespace rows, maximum grants and framing bounds.
6. Integer full domains and invalid radix/separator/overflow cases against an
   independent exact oracle. Floating ties at both precisions, subnormals,
   overflow, long mantissas and malformed suffixes against target-format
   oracles, not another invocation of the same converter. Qualify adapters
   between signed source tokens and magnitude slices, and between accepted
   decoded Text input and the narrower quoted-slice helpers.
7. Decimal allocation failure without a live owner, source-context forwarding
   after owner move, scoped-view retirement and exactly-once cleanup on
   normal/failure paths. Use unrelated synthetic helpers for generic claims.
8. Separately identify binding, verification, emission, actual native ABI
   calls and execution. Use supervised fail-fast cases and both policies
   only where their boundaries permit; prototype unwind tests are not foreign
   transport acceptance. Check failed image/manifest replacement cannot
   publish a mismatched pair.

Evidence entry points are the
[native classifier](../../sources/Terrum.CodeAnalysis/Native/NativeSourceGrantClassifier.cs),
[companion builder](../../sources/Terrum.CodeAnalysis/Native/NativeSourceGrantLibrary.cs),
[native tests](../../tests/Terrum.CodeAnalysis.Tests/NativeSourceGrantLibraryTests.cs),
[CLI tests](../../tests/Terrum.CodeAnalysis.Tests/CliTests.SourceGrantLibrary.cs)
and [decimal ABI probe](../../tests/native/decimal-float-source-grant-abi.terrum).
They preserve provisional evidence, not a claim that every gate has been
exercised, that a stable ABI exists or that a compiler is bootstrapped.
