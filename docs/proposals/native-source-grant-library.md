# Native source-grant library proposal

**Status: unapproved proposal.**

## Summary

The invocation-scoped source-grant proposal permits bounded source-dependent
semantic-module JIT execution. Ordinary native-library emission still rejects
programs that require `Host.Source`; an explicit opt-in CLI path now emits a
separate provisional Windows x64 companion for a checked scalar adapter and
a scalar source-length, source-code-unit, source-offset, source-line-count
and source-position queries.
This proposal addresses promotion of that native-library boundary; it does
not extend the scanner profile or move scalar frontend verification or
emission out of the hosted compiler.

## Decision question

Should an internal Windows x64 native-library export be able to read a
caller-supplied `Host.Source` snapshot for the duration of one synchronous call?

**Summary:** source-aware helpers cannot be added to the maintained
compiler-primitives DLL by binding a build-time sample. A native entry point
needs an explicit per-call source ABI, with bounded length, read-only access,
and no retained address.

**Recommendation:** permit a narrowly scoped generated ABI for
source-dependent exports. Prefix only those exported signatures with a
`const char16*` source address and a `uint32` code-unit count; propagate the
same pair through their internal source-dependent call graph. Keep
source-independent exports unchanged. The caller must provide readable,
two-byte-aligned storage and keep it immutable and alive until the synchronous
call returns. This is an internal Windows x64 implementation ABI, not a
language-level pointer or span feature, public API, or stable C ABI.

## Candidate first-slice boundary

- Require only the existing reserved `Host.Source` identity with read-only
  `char16` units. Reuse the current 16,384-code-unit maximum; preserve NULs,
  line endings, and malformed surrogate units without normalization.
- Permit a null source address only when the count is zero. For a nonempty
  source, the caller is responsible for supplying an address that is aligned
  and readable for the entire extent. The generated code reads but never
  writes through it.
- Bind one source extent for the complete synchronous exported call, including
  nested local calls. The compiler must not put the address or source contents
  in the DLL, retain them after return, or use process-global or thread-local
  current-source state.
- Propagate source dependence through the local call graph. The first slice
  accepts a self-contained library only; source-dependent imports and
  cross-DLL propagation remain rejected. Calls to exports that do not require
  `Host.Source` retain their current ABI.
- Do not change the semantic-module format or the JIT adapter. Do not enable
  source-dependent executables, packages, graphs, module sets, AOT images,
  arbitrary host buffers, or scanner invocation profiles.

The unmanaged caller's extent, alignment, lifetime, and immutability
obligations are preconditions: native code cannot safely probe an arbitrary
address to establish that the full range is readable. The existing managed
source-grant adapter continues to validate and copy managed grants before
execution.

## Required validation

Before implementation, verify that native lowering can pass one explicit
source context through every source-dependent local call without changing
source-free function signatures or retaining invocation state. The first
implementation slice should compile only a self-contained library and
exercise a source-dependent export against pinned C# UTF-16 buffers.

Tests should cover empty and maximum-size inputs, fresh and repeated calls,
malformed surrogate units, NUL and line endings, and checked source boundaries.
Compare results with the hosted source facade under the applicable runtime
policies. Inspect the image and call paths to confirm that no supplied source
address or code unit is embedded or retained. Invalid external pointers cannot
be safely tested by dereferencing them; validate null/count shape and document
the caller precondition instead.

## Alternative

Keep source-dependent native exports unsupported and continue using the
semantic-module JIT adapter. This avoids adding a native call ABI; the
provisional companion remains explicitly opt-in and separate from the
source-free artifact unless the proposal is promoted.

## Implementation boundary

This proposal remains unapproved and does not make the candidate ABI an
accepted target contract. The bounded maintained-library companion described
below is provisional implementation evidence, not promotion. Exact export
naming, interaction with any future cross-DLL source dependency, and whether
a general managed invocation API is needed remain open. No compiler bootstrap
or public native ABI is supported.

### Provisional maintained-library slice

**Question:** Should the proposed source-context ABI be wired into the
maintained Windows x64 compiler-primitives DLL for one checked scalar
`Utf16Scalar` export, the existing scalar source-length query and a scalar
source-line-count export, with its managed caller generated from the exact
export selection?

**Summary:** The separate test-only DLL has exercised hidden source context,
generated bindings, transitive dependency changes, loop-based `SourceText`
line count/position/offset/extent queries and exhaustive UTF-16 line-break
parity under both runtime policies. The maintained build now emits a separate
companion from `CharacterFacts`, `Utf16CodeUnits`, `Utf16Scalar`,
`SourceLineCount` and the isolated scalar `SourceTextLength` component. It
exports the source-free decoder, checked source-dependent adapter, existing
source-length query and allocation-free source-line count with a hash-bound
manifest. `GetSourceTextLength` returns `Host.Source.Length`; including the
complete `SourceText` module is rejected because whole-program source-grant
classification also sees its unselected source-dependent output/aggregate
functions. The isolated component retains the existing scalar contract
without widening classifier support. The line-count loop uses shared
line-break classification, collapses CRLF to one break and counts the final
empty line after a trailing break; it avoids `SourceLines.TryReadSourceLine`
because source-dependent aggregate/output call closures remain unsupported.
Hosted `SourceText.Length` and `SourceText.Lines.Length` comparisons cover
empty and maximum-size grants, line-break forms, malformed UTF-16 and NUL.
The maintained companion itself uses the fail-fast policy. Export descriptors
admit only scalar value signatures; unsupported output/loan and aggregate
signatures and managed-runtime use reject. End-relative source reads derive
their base from the captured invocation count, not the empty build-time buffer
length. This establishes a narrow provisional implementation path, not a
promoted ABI.

**Recommendation:** Keep the opt-in companion separate from the source-free
artifact and the ABI unapproved. Accept only self-contained local dependency
closures and scalar source-dependent exports; generate the descriptor
manifest from the exact selected export set for synchronous callers. The
checked scalar adapter, source-length query and allocation-free line-count
query remain provisional implementation evidence. Raw pointer/count shape,
16,384-unit maximum, readable aligned storage, lifetime and immutability
remain caller preconditions. The managed caller validates the bound and pins
UTF-16 storage only for the call. Keep source-dependent imports, cross-DLL
propagation, aggregate/output export and call closures, the complete source
facade, scanner profiles, executables, packages, graphs, module sets and AOT
unsupported. This implementation does not approve the proposal or promise
ABI stability.

### Provisional scalar source-line-count export

**Question:** Should the maintained companion expose one source-line-count
operation before supporting the complete `SourceLines` facade?

**Summary:** The maintained companion can implement the count as a scalar-only
scan of `Host.Source`, reusing `CharacterFacts.IsLineBreak` and retaining no
source pointer. It counts CRLF once and includes the final empty line after a
trailing break. Calling the general `SourceLines.TryReadSourceLine` helper
would pull aggregate `SourceRange` outputs into the dependent call closure,
which the current classifier rejects.

**Recommendation:** Include only the scalar-only `GetSourceLineCount` export
as provisional coverage of the existing per-call source ABI. Verify it
against hosted `SourceText.Lines.Length`, including maximum-size grants, but
do not infer support for line positions/extents, general `SourceText` APIs,
internal aggregate/output source-dependent call closures, or a stable ABI.

### Provisional scalar source-text-length export

**Question:** Should the maintained companion expose the existing source
length query before supporting the complete `SourceText` facade?

**Summary:** `SourceText.GetSourceTextLength` directly returns
`Host.Source.Length` as a scalar. The source-grant classifier validates all
source-dependent functions in the supplied program before export selection, so
including the full `SourceText` component also reaches unrelated
output/aggregate functions. A separate scalar-only component isolates the
existing length behavior without widening that boundary.

**Recommendation:** Include only the isolated `GetSourceTextLength` scalar
export as provisional evidence. Compare it with hosted `SourceText.Length`
for empty, edge and maximum-size grants; do not infer code-unit reads, ranges,
line operations, the complete source facade, classifier support for
unselected aggregate functions, or a stable ABI.

### Provisional scalar source-code-unit export

**Question:** Should the maintained companion expose one isolated raw
source-code-unit query before supporting the complete `SourceText` facade?

**Summary:** `SourceText.TryReadSourceCodeUnit` distinguishes a valid code unit
from an out-of-range position with a Boolean and output value. For an isolated
scalar export, returning `0..65535` for every UTF-16 code unit and `-1` only
for positions outside `[0, Host.Source.Length)` preserves that information
without output/aggregate ABI. Bounds must be checked before indexing; NUL,
surrogates and `0xFFFF` remain unchanged. The source-grant classifier still
requires an isolated component rather than the complete `SourceText` module.

**Recommendation:** Include only
`GetSourceCodeUnitOrMinusOne(int32 position) -> int32` as provisional
implementation evidence. Compare every code-unit value and hosted
`SourceText` indexing under both runtime policies, including empty and
maximum grants and the full `int32` boundary cases. Callers must check `-1`
before converting to `char16`; it is an ordinary position sentinel, not a
validation result for invalid pointers, oversized grants or other raw ABI
preconditions. Keep the source-free DLL and classifier boundary unchanged,
and do not infer support for the complete source facade or a stable ABI.

### Provisional scalar source-range validation export

**Question:** Should the maintained companion expose source-range validity as
a scalar predicate without returning an aggregate `SourceRange`?

**Summary:** `SourceText.TryGetSourceRange` accepts nonnegative `start` and
`length` when `start <= Host.Source.Length` and `length <=
Host.Source.Length - start`, then constructs the range. The subtraction form
avoids signed overflow, and the accepted source bound makes every resulting
end representable. A scalar Boolean can preserve that success contract
without the aggregate output closure that the source-grant classifier
rejects. Hosted C# `SourceText.GetSubText(TextSpan)` provides an executable
oracle for the same negative, overflowing and out-of-snapshot boundaries.

**Recommendation:** Include only
`CanGetSourceRange(int32 start, int32 length) -> bool` as provisional
evidence. Return true exactly when the hosted `TextSpan` can be extracted
from the same source snapshot, including a zero-length range at EOF; return
false for negative, overflowing or out-of-snapshot ranges. Check the start
before subtracting it from source length. Keep aggregate `SourceRange`
exports and output closures, the source-free DLL, classifier boundary and ABI
promotion decision unchanged.

### Provisional packed scalar string-escape export

**Question:** Should the maintained companion expose the result of
`StringEscapeDecodeResult.TryAnalyze` through one packed scalar export rather
than an aggregate output or several rescanning field queries?

**Summary:** `TryAnalyze` returns false for an invalid/empty range or one not
starting with a backslash. Otherwise it publishes either a valid escape
(`ErrorCode = 0`, scalar and consumed width) or a malformed escape
(`ErrorCode = 5`, consumed width and an equal error width); directly escaped
unpaired UTF-16 uses scalar `-1`. The source-grant classifier validates the
whole source-dependent program, so export selection cannot hide the
aggregate result type or output closure. One `uint32` can retain the current
field domain in one source scan: zero means not analyzable; bits 5..25 store
`Scalar + 1`; bits 1..4 store consumed width; bit 0 marks error code 5.
`ErrorLength` is derived as consumed width for an error and zero on success.
This relies on the current decoder producing only error codes 0/5, scalar
values from -1 through U+10FFFF and positive consumed widths no greater than
10. Future changes to those values require revisiting the encoding. Separate
field exports would repeatedly scan and require identical arguments and the
same unchanged source snapshot across calls. The isolated implementation uses
the shared source-free `HexDigitValue` helper, leaving the aggregate decoder
and source-free `CharacterFacts` component unchanged.

**Recommendation:** Include only the isolated
`AnalyzeStringEscapePacked(int32 start, int32 length) -> uint32` query as
provisional evidence, preserving `TryAnalyze`'s direct range semantics rather
than the surrounding lexer’s string-boundary behavior. Return zero for
`TryAnalyze == false`; otherwise use the encoding above. Check slice bounds
before computing the end or reading source, use a scalar hexadecimal-digit
helper, and do not call the aggregate `TryAnalyze` closure. Compare
reconstructed fields with hosted C# escape diagnostics, including truncation,
non-hex stopping positions, Unicode boundaries, unpaired surrogates, trailing
text, maximum grants and invalid arguments. For a backslash immediately
followed by a line break, test `TryAnalyze` semantics directly: the hosted
lexer handles that boundary before invoking its escape analyzer. Keep the
classifier, source-free DLL, general string-literal/scanner support and ABI
promotion decision unchanged. Direct native tests under both runtime policies
cover simple and Unicode escapes, malformed/truncated forms, scalar limits,
paired and unpaired surrogates, trailing text, direct line-break spellings,
invalid arguments and maximum grants. Semantic-module regressions compare the
packed fields with the aggregate analyzer across the hosted diagnostic corpus
and exhaustive BMP/supplementary escape cases; the maintained manifest binds
the selected export without changing the source-free export set.

### Provisional packed character-literal analysis export

**Question:** Should the maintained companion expose one scalar result for
`CharacterLiteralDecodeResult.TryAnalyze` without returning its aggregate
diagnostic span?

**Summary:** The existing analyzer returns false only for an invalid source
range; valid ranges may instead publish either one scalar or an error code and
an absolute UTF-16 error span. Its aggregate span prevents direct selection by
the current source-grant ABI. A tagged `uint32` preserves the result without
sentinels or collisions: low two bits `00` mean false and the canonical
result is zero; `01` means success and stores the scalar in bits 2..22;
`10` and `11` mean error codes 1 and 5, respectively, with the spelling-
relative error start in bits 2..16 and error length in bits 17..31. Both
diagnostic coordinates fit 15 bits under the current 16,384-unit maximum.
Errors use unsigned packing because a full-spelling error may set bit 31.
Successful NUL (`1`), false (`0`) and zero-length diagnostics (`2` or `3`)
remain distinct. The encoding must be revisited if the source maximum or
diagnostic/result domain changes. The component scans the quoted spelling
directly rather than calling the aggregate analyzer or changing the
source-grant classifier.

**Recommendation:** Include only
`AnalyzeCharacterLiteralPacked(int32 start, int32 length) -> uint32` as
provisional evidence. Preserve the analyzer's exact quote-boundary and
UTF-16 behavior: valid empty/short ranges publish error 1, invalid ranges
return zero, raw unpaired surrogates publish error 1, malformed escapes and
surrogate-valued Unicode escapes publish error 5, and a backslash before a
surrogate consumes only one code unit. Error coordinates remain relative to
the complete quoted spelling, including the opening quote; closing quotes
are excluded from escape spans, and invalid second escapes take precedence
over the multiple-scalar error. Verify both policies against aggregate
`CharacterLiteralDecodeResult.TryAnalyze` and hosted lexer diagnostics across
nonzero source offsets, NUL, quote/newline, escaped apostrophe, truncated and
non-hex escapes, surrogate cases, second-scalar precedence, extreme
arguments and maximum grants. Use the isolated scalar hex helper; leave the
classifier, source-free DLL and hosted scanner/frontend boundaries unchanged.
The encoding remains provisional and does not establish a stable ABI.

### Provisional scalar character-literal value export

**Question:** Should the maintained companion expose the decoded character
value without requiring callers to interpret the packed diagnostic layout?

**Summary:** `AnalyzeCharacterLiteralPacked` already validates the exact
quoted slice and packs either a scalar success or a diagnostic result into
`uint32`. A separate
`GetCharacterLiteralScalarOrMinusOne(int32 start, int32 length) -> int32`
can return the Unicode scalar on success and `-1` for an invalid range,
malformed spelling or analyzer error. Every valid scalar is in
`0..0x10FFFF`, so the sentinel is collision-free and successful NUL remains
zero. The wrapper can reuse the packed analyzer's low two-bit success tag
instead of duplicating literal parsing.

**Recommendation:** Include only this scalar value query as provisional
evidence. Return the packed scalar bits only when the tag is `01`; return
`-1` for false and both error tags. Compare the result with aggregate
`CharacterLiteralDecodeResult.TryAnalyze` and hosted lexer validity under
both runtime policies, including NUL, raw and escaped supplementary scalars,
maximum scalar, malformed/truncated escapes, surrogate cases, multiple
scalars, invalid bounds and maximum grants. Keep detailed error codes/spans
on the existing packed query. Do not change the source-grant classifier,
source-free DLL or hosted scanner/frontend boundary, and do not approve a
stable ABI.

### Provisional scalar string-literal validation/count export

**Question:** Should the maintained companion expose the existing
`StringLiteral.TryCreate` validation and scalar count without exporting the
literal aggregate, cursor or owned-string operations?

**Summary:** `StringLiteral.TryCreate` returns false for malformed spelling or
an invalid source range and otherwise publishes a `StringLiteral` containing
the original range and decoded scalar count. An isolated
`GetStringLiteralScalarCountOrMinusOne(int32 start, int32 length) -> int32`
can preserve the validity/count result: `-1` means false and every successful
count is nonnegative, including zero for an empty literal and one for a
literal containing NUL. The source-grant bound limits the count to 16,382.
For escaped units, the component can reuse `AnalyzeStringEscapePacked` and
must reject both zero (not analyzable) and an error marker before advancing
by the packed consumed width. It must exclude the closing quote from the
escape range. This avoids including `StringLiteral`, `StringEscape` aggregate
outputs or source-dependent output call closures in the maintained program.

**Recommendation:** Include only the isolated scalar validation/count export
as provisional evidence. Match the exact quoted-slice contract: require both
quotes, reject raw interior quotes and the five hosted line breaks, accept
escaped quotes and escaped linebreak scalars, count raw surrogate pairs once,
and reject unpaired raw units and malformed escapes. Do not combine escaped
surrogate pairs across escape spellings. Check bounds before computing the
closing-quote position; preserve exact slice termination, including a
backslash immediately before the closing quote. Compare both validity and
count with aggregate `StringLiteral.TryCreate` and hosted lexer diagnostics
under both policies, covering empty/NUL/mixed literals, multiple scalars,
all line breaks versus VT/FF, quote/backslash boundaries, malformed and
truncated escapes, surrogate distinctions, nonzero offsets, extreme
arguments and maximum grants. This query does not expose a `StringLiteral`,
cursor, decoder/encoder, scanner or frontend support, widen the classifier,
change the source-free DLL, or approve a stable ABI.

### Provisional scalar string-literal scalar lookup export

**Question:** Should the maintained companion expose one decoded scalar from a
validated quoted spelling without exporting the `StringLiteral` aggregate or
its cursor?

**Summary:** A bootstrap consumer needs literal scalar values as well as
validity and count, but the aggregate literal and cursor are not admitted by
the current source-grant classifier. An isolated
`GetStringLiteralScalarOrMinusOne(int32 start, int32 length, int32 scalarIndex)
-> int32` can return the decoded Unicode scalar at a zero-based scalar index,
or `-1` for invalid bounds, spelling or index. Every valid scalar is
nonnegative, including NUL and U+FFFF, so the sentinel is unambiguous. The
source-grant maximum bounds a successful spelling to 16,382 scalars.

**Recommendation:** Include only this scalar lookup as provisional evidence.
First call `GetStringLiteralScalarCountOrMinusOne` so the complete exact
quoted slice has passed `StringLiteral.TryCreate` validation, even when the
requested scalar occurs before a malformed suffix; reject a negative or
out-of-range scalar index, including every index for an empty literal. Then
rescan only to the selected decoded scalar, counting raw surrogate pairs
once and using packed escape decoding without combining adjacent escaped
surrogate values. The two bounded scans are allocation-free and linear in the
slice length; repeatedly enumerating every index is quadratic, so this is
occasional indexed access rather than a cursor replacement. Compare scalar
values and failure with the maintained `StringLiteral` semantic module and
hosted lexical validity under both runtime policies, including exhaustive
single-scalar inputs, malformed suffixes and multi-scalar boundary cases. Do
not expose source positions, cursors, literal values, encoders, scanner or
frontend support, widen the classifier, change the source-free DLL, or
approve a stable ABI.

### Provisional scalar string-literal encoded-length export

**Question:** Should the maintained companion expose the existing string
literal encoding-length contracts without exporting owned encoded buffers or
the literal aggregate?

**Summary:** `StringLiteral` measures strict ASCII and Latin-1 bytes, UTF-8
bytes, UTF-16 code units and bytes, and UTF-32 bytes. A scalar query can
preserve those results without returning an aggregate or output buffer. The
candidate `GetStringLiteralEncodedLengthOrMinusOne(int32 start, int32 length,
int32 encoding) -> int32` returns a nonnegative encoded length, or `-1` for
invalid spelling, an invalid selector, or a scalar that strict ASCII or
Latin-1 cannot represent. The authored selector mapping is `0` ASCII bytes,
`1` Latin-1 bytes, `2` UTF-8 bytes, `3` UTF-16 code units, `4` UTF-16 bytes,
and `5` UTF-32 bytes. This mapping is a provisional API choice, not an enum
ordinal; the existing encoding-test helper uses a different ordering.
Endianness does not change byte length, and no encoding adds a byte-order
mark, terminator, normalization or replacement. Invalid raw source-grant
storage remains outside the query's `-1` result contract.

**Recommendation:** Consider this bounded query as provisional only. Validate
the entire exact quoted slice, reject raw interior quotes, hosted line breaks,
unpaired UTF-16 and malformed escapes, and preserve strict ASCII/Latin-1
rejection rather than substituting; evaluate representability on decoded
scalars, not spelling units. The UTF-8 width is 1/2/3/4 at U+0080, U+0800 and
U+10000; UTF-16 units are one for BMP scalars and two for supplementary
scalars; UTF-16 bytes are twice the unit count; UTF-32 bytes are four per
scalar. The 16,384-unit source bound limits lengths to 16,382 UTF-16 units,
32,764 UTF-16 bytes, 49,146 UTF-8 bytes and 65,528 UTF-32 bytes. Cover every
selector explicitly, including the thresholds U+007F/U+0080, U+00FF/U+0100,
U+07FF/U+0800, U+D7FF/U+E000, U+FFFF/U+10000 and U+10FFFF. Compare against
the existing `StringLiteral.TryGet*Length` methods and strict hosted encoders
under both policies, including empty literals, every valid scalar in bounded
batches, unrepresentable single-byte values and maximum grants. Do not expose
encoded buffers, literal aggregates, cursors, scanner or frontend support,
widen the classifier, change the source-free DLL, or approve a stable ABI.

### Provisional scalar source-offset export

**Question:** Should the maintained companion expose the hosted line/character
to source-offset query before supporting the complete `SourceLines` facade?

**Summary:** Hosted `SourceText.TryGetOffset` accepts UTF-16 character
positions within a line's including-break extent, except that the end of a
non-final line belongs to the next line; the final line accepts EOF. A scalar
export can preserve its success/value contract as an offset in
`0..Host.Source.Length`, or `-1` for invalid pairs. This mapping does not
preserve the host method's incidental zero-valued `out` storage on failure.
The source scan must use the existing line-break set, collapse only CRLF,
retain both CRLF code-unit positions, and include the final empty line after a
trailing break. The query can be isolated from aggregate `SourceRange` outputs
without compiling the full `SourceLines` facade.

**Recommendation:** Include only
`GetSourceOffsetOrMinusOne(int32 line, int32 character) -> int32` as
provisional evidence, mechanically matching the hosted `TryGetOffset`
contract. Check both arguments before scanning, compare the bounded character
position before adding it to the line start, and return `-1` for invalid
line/character pairs only. Verify every short-fixture line and nearby
character boundary, recognized and excluded line breaks, empty/trailing-break
sources, maximum-size grants and extreme `int32` arguments. Keep the
source-free DLL and classifier boundary unchanged; do not infer general
`SourceLines` support or a stable ABI.

### Provisional scalar source-position exports

**Question:** Should the maintained companion expose the hosted
line/character result as two scalar source-position exports rather than a
packed pair?

**Summary:** `SourceText.GetLinePosition` returns two coordinates for offsets
in `[0, SourceText.Length]`; both fit in `0..16,384` under the current grant
bound. Two scalar exports can return the exact line number and UTF-16
character position, or `-1` for invalid offsets, without defining a packed
representation or output/aggregate ABI. Each export scans independently, so
callers combining the values must use the same unchanged source snapshot; the
bounded scan is not a performance-parity claim with the hosted line index.
Line advancement must wait for the complete line break, keeping both code
units of CRLF on the preceding line.

**Recommendation:** Include only the isolated
`GetSourceLineNumberOrMinusOne(int32 position)` and
`GetSourceCharacterPositionOrMinusOne(int32 position)` scalar exports as
provisional evidence. Preserve hosted `GetLinePosition` behavior for every
valid offset, including EOF, an empty source, trailing empty lines and
positions inside each recognized break; round-trip those coordinates through
the scalar source-offset query on the same grant. Return `-1` only for offsets
outside the source extent. Reuse shared line-break classification, keep the
source-free DLL and classifier boundary unchanged, and do not infer aggregate
`LinePosition`, complete `SourceLines` or stable ABI support.

### Provisional scalar source-line-end exports

**Question:** Should the maintained companion expose both exclusive ends of a
source line's content and including-break extents without returning aggregate
`SourceRange` values?

**Summary:** Hosted `SourceLines.TryReadSourceLine` returns both a content
range and an including-break range, each half-open and UTF-16 based. Their
exclusive ends are scalar offsets; the existing
`GetSourceOffsetOrMinusOne(line, 0)` provides their shared start. The content
end is the first line-break unit offset, while the including-break end is
after the complete break. For CRLF, those ends differ by two; for other
recognized breaks, they differ by one. A trailing break creates a final
empty line whose start and both ends equal source length. The content end on
a non-final line is a valid character coordinate at the first break unit;
the including-break end belongs to the next line and is not valid for the
preceding line. Combining the three scalar results reconstructs both hosted
ranges only when calls use the same unchanged source snapshot. Each query
scans independently, so enumerating every line this way is quadratic in
source length.

**Recommendation:** Include only
`GetSourceLineContentEndOrMinusOne(int32 line) -> int32` and
`GetSourceLineEndOrMinusOne(int32 line) -> int32` as provisional evidence.
Return the hosted content and including-break exclusive ends for
`0 <= line < SourceText.Lines.Length`, including EOF for empty final lines,
and `-1` for invalid line numbers only. Reuse `CharacterFacts.IsLineBreak`,
collapse only CRLF, and check CRLF lookahead before reading. Verify both
reconstructed ranges and offset-query behavior at content and including-break
boundaries for empty, mixed/consecutive breaks, NUL and malformed UTF-16,
every UTF-16 successor after CR, maximum grants and extreme line numbers
under both policies. Do not claim indexed-lookup performance or support
aggregate `SourceRange` call closures, the complete `SourceLines` facade, raw
pointer validation or a stable ABI.

### Provisional scalar integer-parsing exports

**Question:** Should the maintained source-grant companion expose the
source-dependent integer parsers as paired scalar validity and value queries?

**Summary:** The existing `IntegerParsing` APIs use `bool` plus
`init ref<T>` output, which the source-grant ABI does not admit. No sentinel
value can represent failure for the full `int64` or `uint64` domains. An
isolated scalar-only component can instead expose `CanParseTBase(start,
length, radix) -> bool` and `ParseTBase(start, length, radix) -> T` for each
existing parser type. The value query repeats validation and fails through
`require` when input is invalid; it does not fabricate a zero or sentinel.
Callers must use identical arguments and the same immutable source snapshot
for both calls. A successful pair scans twice, and is not an atomic try-style
operation.

**Recommendation:** Keep the pair provisional and isolated from
`IntegerParsing.terrum`, preserving its hosted semantics for exact UTF-16
slices, radices 2–36, ASCII digits, signed leading minus, radix-specific
separators and destination-specific overflow. Compare all five types against
the existing independent `BigInteger` oracle under both runtime policies,
including invalid values, extreme slice/radix arguments and maximum grants;
verify checked failure directly and fail-fast failure in a subprocess.
Include only the scalar pairs in the maintained manifest. Do not widen the
source-grant classifier, change the source-free companion, imply single-pass
performance or connect these exports to syntax parsing or the scalar
frontend. This does not approve the ABI.

### Provisional scalar hexadecimal-float bit exports

**Question:** Should the maintained source-grant companion expose target-typed
hexadecimal-float conversion as two scalar raw-bit queries?

**Summary:** `HexFloatLiteral.terrum` converts a caller-delimited,
grammar-shaped hexadecimal-float slice directly to `float32` or `float64`
bits. A source-grant caller can receive those bits through
`GetHexFloat32LiteralBitsOrMinusOne(start, length) -> int64` and
`GetHexFloat64LiteralBitsOrMinusOne(start, length) -> int64`, with `-1` for
invalid slices. The sign remains a separate source operator, so successful
literal values are positive bit patterns; even positive NaN has a nonnegative
bit pattern and cannot collide with the sentinel. The functions are isolated
scalar queries, not lexer-token constructors or frontend integration.

**Recommendation:** Include only these two exports as provisional
source-dependent evidence. Preserve the hosted target-format rounding
semantics, including subnormals, ties-to-even and overflow; accept only exact
grammar-shaped slices, including permitted repeated separators, and reject
separators adjacent to the point, exponent marker/sign or slice end. Compare
direct fail-fast companion calls with hosted bound constants, and compare
persisted semantic-module execution under both runtime policies, including
invalid bounds/spellings, rounding ties, deterministic valid spellings and
very long mantissas. Do not widen the classifier, change the source-free DLL,
or connect the converter to frontend verification or emission. This does not
approve the ABI.

### Provisional scalar based-integer floating-bit exports

**Question:** Should the maintained source-grant companion expose direct
target-format conversion of hexadecimal and binary integer slices?

**Summary:** The hosted floating-literal binder accepts exact based integer
tokens as positive arbitrary-precision values and rounds directly to the
contextual `float32` or `float64` format. The candidate
`GetBasedIntegerFloat32BitsOrMinusOne(start, length) -> int64` and
`GetBasedIntegerFloat64BitsOrMinusOne(start, length) -> int64` queries preserve
those raw result bits without exposing a floating ABI. Integer literal signs
remain separate unary operators, so valid results are nonnegative bit
patterns and `-1` remains disjoint as the invalid-slice sentinel. The queries
accept only hexadecimal or binary integer spellings, including the existing
repeated-separator and prefix-separator forms; they do not parse hex-float
exponents or decimal integers.

**Recommendation:** Keep these two source-dependent exports provisional.
Validate the complete token slice and exact radix digits before constructing
the value; round the significant-bit stream directly with guard/sticky and
ties-to-even behavior, preserving positive zero and overflowing to positive
infinity as hosted target-format conversion does. Compare against hosted
bound constants under both runtime policies for semantic-module execution and
through the fail-fast companion, including ties at both significand
precisions, long inputs, overflow, leading zeroes, repeated separators,
prefix separators and malformed radix/suffix/bound cases. Do not widen the
source-grant classifier, alter source-free exports or imply frontend
verification/emission support. This does not approve the ABI.

## Promotion decisions still required

### Where source-grant shape is validated

**Question:** Should each raw native export validate the source pointer/count
shape and the maximum extent, or should those be mandatory caller
preconditions?

**Summary:** The feasibility experiment's private managed helper rejects a
null pointer with a nonzero count and counts above 16,384 before invocation.
Generated code consumes the supplied count for length and bounds checks, but
does not validate the raw pointer or enforce the maximum. Native code cannot
establish that an arbitrary address is readable without dereferencing it.

**Recommendation:** Make `(count == 0 || source != null)` and
`count <= 16,384` caller preconditions on the raw internal ABI. Require any
supported managed invocation adapter to validate both before pinning and
dispatch. Keep readable extent, two-byte alignment, lifetime and immutability
as caller obligations; do not probe arbitrary pointers. The raw export must
not acquire a new failure convention solely for invalid external pointers.
The test helper's validation establishes adapter behavior only, not raw-export
validation.

### How callers learn the effective export signature

**Question:** How does a caller discover that a source-dependent export has
the hidden source pair when dependence is inferred from its transitive local
call graph?

**Summary:** Adding a source read in a private callee can change an export's
effective native signature without changing its authored parameters or export
name. A test-only follow-up now derives selected-export descriptors from the
same emission selection and generates matching cdecl delegate types, including
the hidden pair only for source-dependent exports. Recompiling an unchanged
export after changing only its private callee verifies both signature shapes.
This is feasibility evidence, not a production managed caller-binding
mechanism.

**Recommendation:** For any promoted managed path, generate its invocation
stub from the same verified export set used for DLL emission, including the
hidden source pair only for source-dependent exports. Do not require callers
to infer the ABI from source bodies or hand-maintain delegate signatures.
Direct unmanaged calls remain an internal rebuild-coupled ABI obligation, not
a stable C ABI; no new semantic-module metadata is needed solely to generate
the stub.

## Provisional test-only experiment

The test-only feasibility experiment below remains distinct from the bounded
maintained source-grant companion described above. That companion is available
only through an explicit opt-in path and leaves the source-free
compiler-primitives DLL unchanged. Neither implementation approves the
proposal; general source-dependent native-library support and a production
managed invocation stub remain unsupported unless a later decision promotes
them.

The experiment uses a separate test DLL containing `Utf16CodeUnits`,
`Utf16Scalar`, and a test-authored export named
`Terrum.CodeAnalysis.ReadUtf16ScalarForSourceAbiTest`. Its source-dependent
signature prefixes the source address and `uint32` code-unit count, in that
order, before the authored scalar parameters. The test adapter takes `at`,
`end`, and an expected scalar so the ABI exercises a fifth argument on
Windows x64 while returning the decoder result only when it matches. Internal
source-dependent calls receive the same hidden source pair; source-independent
calls and their signatures remain unchanged. Separate signed and unsigned
64-bit exports read a source code unit through local callees, and a source-free
64-bit export checks byte-identical emission against ordinary native code.
No source-dependent imports, cross-DLL propagation, aggregate results, or
general wrapper are admitted.

The test-only generated binding validates the 16,384-unit limit, pins the
provided UTF-16 array for the synchronous call, adds the hidden pair only when
the selected export descriptor requires it, and unpins after return. The
caller remains responsible for readable storage and immutability during the
call; pinned arrays provide the required two-byte alignment. Lower-level
adapter tests still check null/count and alignment rejection before dispatch.
Tests do not dereference invalid pointers. This experiment establishes implementation feasibility only; extending the
maintained companion beyond its documented scalar slice, generalizing the
ABI, or claiming language/public ABI support requires a separate review and
decision.

### Experiment outcome

The test-only path is implemented as internal
`NativeEmitter.EmitSourceGrantLibraryForTest`; the public `EmitLibrary` path
continues to reject source grants. Tests compile the two UTF-16 components
and test adapters through hosted binding, then mark that test IR as a library
without adding a library-buffer compilation API or changing semantic metadata.
Admission is deliberately narrower than the candidate contract: only
self-contained structured `bool`, `char16`, `int32`, `uint32`, `int64`,
`uint64`, and `void` functions are accepted. Blocks, initialized locals,
assignments, expression statements, conditionals, requirements, returns,
scalar expressions, and local calls are explicitly visited. It also admits
pre-tested structured loops whose `BoundWhile` has no separate iterator;
post-tested loops, loops with a separate iterator, and `break`/`continue`
remain rejected. Source forms lowered to this same bound shape are
necessarily admitted as well.
Cleanup regions, indexed graphs, aggregates, output loans, intrinsic
functions, and external declarations are not admitted.

Direct source length/read expressions and the transitive local call graph
determine which functions receive the hidden pair. The pair is saved in
frame-local slots before volatile-register use. Authored argument positions,
outgoing stack space, and local calls account for those two positions only
on source-dependent functions. Length reads load the invocation count;
buffer reads compare their index against that count and use zero-extending
two-byte loads from the invocation pointer. No source buffer data or address
fixups are emitted.

Focused Windows x64 tests load one separate temporary DLL per runtime policy
and reuse it for fresh pinned UTF-16 storage. The scalar adapter and its
three-argument local callee exercise the fifth argument, including reordered
named arguments. Empty/null-zero calls use the length export and scalar
adapter guard. Valid decoder results match hosted C# `Rune.DecodeFromUtf16`
for maximum-size input, BMP/supplementary scalars, NUL, CR/LF and other line
breaks, malformed surrogates, final units, and bounded lookahead.
The signed and unsigned code-unit exports are also compared against every BMP
code unit across four fresh maximum-size pinned grants under both policies;
the scalar adapter is checked at the same one-unit ends and at every
supplementary high-surrogate boundary, so no following code unit can complete
a surrogate pair outside the requested extent. The full native scalar adapter
also decodes all 1,048,576 valid supplementary scalars across fresh
maximum-size grants under both policies. This is direct-read and
bounded-decoding evidence only, not support for the `SourceText` facade or
output loans.
One pinned allocation is reused with different counts, and two
simultaneously pinned allocations with equal counts produce distinct results.
The test adapter returns `-2` for invalid ranges without calling the decoder,
and `-3` for an expected-value mismatch; neither changes the real decoder's
precondition or `-1` malformed-surrogate result. Managed test helpers reject
null/nonzero, oversized, misaligned, and storage/count-inconsistent calls
before native invocation; tests never dereference invalid pointers. A separate
boundary export indexes a still-readable one-unit allocation with a zero-unit
grant, and the checked-exceptions DLL must raise `SEHException` before the
read. Both runtime-policy images assert the dynamic-count comparison,
conditional branch, and failure call; the fail-fast failure path is not invoked
because it terminates the process.

Artifact assertions establish empty host data, buffer offsets, and data
fixups; absent image data sections, TLS, and base relocations; identical DLL
bytes after changing the sample source units and length; frame-local context
stores before body code; and dynamic-count comparisons followed by
`movzx eax,word ptr [rdx+rax*2]` in the emitted PE text. A five-authored-argument
source-free export remains callable without the hidden pair and its function
bytes match ordinary source-free emission exactly.

The test-only managed binder consumes the selected-export descriptors emitted
alongside the image. A fixture keeps the public `int32(int32)` export
unchanged while switching only its private callee from source-free to a
`Host.Source` read; the generated delegate changes from one authored argument
to the hidden source pair plus that argument, and execution matches the
expected source unit under both runtime policies. Oversized managed grants are
rejected before invocation, source-free calls retain their authored signature,
and ordinary `EmitLibrary` continues to reject both fixtures. The separate
opt-in maintained companion path now emits its own hash-bound manifest; it
does not change the source-free compiler-primitives DLL boundary or provide a
general production invocation stub.

The test-only fixture also verifies source-dependence fixed-point propagation
through a terminating two-function mutual-recursion cycle under both runtime
policies, with the native result matching the hosted source-grant JIT result.
This adds feasibility evidence only; the general source-grant DLL proposal
remains unapproved. Only the separately bounded maintained companion is
provisionally implemented.

The reject-by-default experiment admits only self-contained structured
`bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`, and `void` functions.
These results establish feasibility of the provisional generated ABI only.
They do not establish native support for the existing four-operation
`SourceText`/`SourceLines` facade, whose current components require output
parameters and structures outside the experiment. The scalar line-scan
fixture does not change that boundary; adopting the source facade would
require separate implementation and validation evidence.

### Test-only increment: structured line scans

**Question:** Should the feasibility classifier admit structured pre-tested
loops without a separate iterator for scalar-returning source-dependent
helpers, without admitting the output parameters or aggregate results needed
by the full source facade?

**Summary:** The test-only classifier now admits `BoundWhile` only when
`Iterator` is null and `IsPostTested` is false, recursively visiting its
condition and body for source-dependence. The line-count fixture starts at one
(including empty input), treats CRLF as one break, and counts CR, LF, NEL, LS
and PS as line breaks. Under both runtime policies, generated-binding results
match `SourceText.From(text).Lines.Length` across empty/null grants, each
supported line ending, mixed and trailing endings, malformed UTF-16, NUL,
excluded VT/FF, and maximum-length inputs. Explicit maximum-bound checks cover
16,384 plain units, 16,384 LF units, 8,192 CRLF pairs and a final CR; the
16,385-unit grant remains rejected before dispatch. The line-count export
also matches hosted `SourceText` for each of the 65,536 possible one-unit
UTF-16 sources under both runtime policies, including every surrogate and
control code unit. A second exhaustive pass appends each possible UTF-16 unit
after CR, checking CRLF pairing and adjacent breaks against the hosted line
count under both policies. At all three offsets of every two-unit `CR +
code-unit` input, native line/character positions also match
`SourceText.GetLinePosition`, and each valid result round-trips through the
native inverse query and hosted `SourceText.TryGetOffset`. For every
single-unit source, the same parity holds at both offsets.
The same test DLL also returns a test-local packed line/character pair and
matches hosted `SourceText.GetLinePosition` for every offset in the short
edge-case fixtures and selected boundary offsets in maximum-length inputs.
Both coordinates fit unsigned 16-bit fields under the 16,384-unit source
bound. The packing is only a scalar test carrier; it does not define a
production return ABI or remove the full facade's output-parameter and
aggregate requirements. For those same valid positions, a scalar inverse
query maps line/character back to the original offset and agrees with hosted
`SourceText.TryGetOffset`. Invalid line/character pairs and any production
failure representation remain outside the experiment. Two further test-only
scalar carriers pack each line's content extent and including-break extent,
matching `SourceText.Lines` for every line in short fixtures and selected
lines in maximum-length sources. Start and length each fit within 15 bits
under the source bound; the packed coordinates remain private to the fixture
and do not add aggregate returns or output loans.

**Recommendation:** Keep this admission confined to the test-only
feasibility path. Continue rejecting post-tested loops, separate iterators,
loop transfers, output loans, aggregate types, the four-operation source
facade, maintained DLL exports and production caller binding. These parity
tests add implementation evidence only and do not approve the proposal.
Public library emission, native packages, production CLI paths, maintained
compiler-primitives exports, JIT execution, scanner profiles, executables,
graphs, module sets, AOT, and cross-DLL source propagation remain unchanged
and unsupported for this experiment. Readable aligned memory, lifetime, and
immutability remain caller preconditions; there is no general native pointer
validation or public wrapper. **Status remains unapproved.**

### Decimal floating-literal owned workspace

**Question:** Should the maintained source-grant companion admit the decimal
converter's private owned workspace while retaining scalar-only exported
signatures?

**Summary:** The hosted decimal path delegates to invariant-culture
`float.TryParse`/`double.TryParse`, whose accepted spellings are broader than
the language's numeric tokens. The Terrum helper instead accepts a
caller-delimited decimal mantissa with optional fractional and exponent
digits, digit separators only between digits, and no sign because the sign is
a separate operator. It constructs target-format bits directly and uses a
183-word `OwnedInt32Buffer` workspace for exact midpoint comparison. A narrow
provisional classifier allowance now permits the internal by-value owner
transfers, local scoped views, compiler-verified cleanup, and only the
canonical allocation/count/read/set/append intrinsics needed by this helper.
The maintained companion exports only the two scalar `int64(int32, int32)`
entry points; the source-free compiler-primitives DLL and export descriptors
are unchanged. Direct native DLL parity covers the existing rounding corpus,
invalid bounds and the maximum-size grant. The tracked native ABI probe covers
source-context forwarding, successful decimal calls and owner cleanup under
both runtime policies, plus checked unwinding after an owner moves into a
source-dependent helper and checked allocation failure with no live owner.
Allocation failure remains a native checked/fail-fast failure and is not
represented as malformed input. These pieces are implementation evidence, not
promotion of the ABI or ownership contract.

**Recommendation:** Keep the extension provisional and the proposal
unapproved. Retain the decimal helper's grammar-shaped input contract and
separate unary-sign boundary. Keep `IsScalar` and the public export gate
unchanged; allow owner results/parameters only on internal ordinary helpers,
never view parameters/results, owner aggregates, arbitrary output helpers,
external or nominal owner calls, or cross-DLL transfers. Admit only the
canonical buffer intrinsics required above and continue relying on the normal
semantic verifier for ownership, loan, bounds, and cleanup invariants. Do not
infer general source-dependent owned-buffer support, alter the source-free
artifact, or move scalar-frontend verification/emission out of the hosted
implementation. This does not change lexer recovery, the hosted floating
parser, or any accepted target contract.
