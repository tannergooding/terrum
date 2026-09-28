# Lexical grammar

## Definitions and notation

A **UTF-16 code unit** is one 16-bit element of the source snapshot. A
**Unicode scalar** is a Unicode code point excluding the surrogate range.
**Token text** is the source extent recognized as a token; **leading trivia**
is the whitespace or comment text attached to that token. Spans, line columns,
diagnostics and token lengths are measured in UTF-16 code units.

## Source text and identifiers

Lexing operates on an immutable UTF-16 string snapshot.
Valid surrogate pairs advance together during identifier recognition; unpaired
surrogates remain in the snapshot and produce diagnostics, including within
comments and strings. Lines end at CRLF
(one break), CR, LF, NEL (U+0085), LS (U+2028), or PS (U+2029).
The character position inside a line includes its break when queried by offset;
the next line starts immediately after the break. Tabs, VT, FF, ASCII space,
NBSP, U+1680, U+2000–U+200A, U+202F, U+205F, and U+3000 are whitespace
trivia, not line breaks. Current lexer and parser support is documented in
[implementation](implementation.md).

Identifiers start with `_` or Unicode 17.0.0 `XID_Start` and continue with
`_` or `XID_Continue` (UAX #31), except that `_` alone is the reserved
`Underscore` token, never an identifier. Spelling is compared ordinally; the lexer
neither normalizes nor folds case. The exact derived properties are generated
from the pinned Unicode 17.0.0 `DerivedCoreProperties.txt`, independent of
the host runtime's Unicode categories. `Join_Control` U+200C and U+200D
are in Unicode 17 `XID_Continue`, so they are allowed after an identifier
start but not as the first character. This profile makes no additional
contextual or default-ignorable exclusion: such characters remain significant
in ordinal spelling, even when visually invisible. A disallowed
scalar is an error token, including Unicode digits at identifier start.
Keywords are the lower-case ASCII spellings represented in `TokenKind`,
including reserved future grammar such as `struct`, `unique`, `move`, `async`,
and `try`; primitive type names remain identifiers. All other admissible
spellings are identifiers. `_name` and `_123` remain identifiers.
The standalone `_ = expression;` statement evaluates the bound expression
once, with its effects and failures, and cleans up an owning result at statement
exit. It does not name a destination or infer a type for a context-free literal.
Void calls and named mutable-buffer-view `Set(index, value)` calls are separate
expression statements. Generic type holes, wildcard patterns and unnamed
lambda parameters are separate grammar constructs; the reserved token does not
grant them discard semantics. Current executable coverage is documented in
[implementation](implementation.md).

## Numeric tokens

Numeric tokens begin with a decimal digit and are unsuffixed. The lexer
recognizes decimal, hexadecimal and binary integer forms plus `_`
separators in their digit runs; a type is selected later by context or an
explicit type-call target. Decimal fractional/exponent forms have lexical
syntax here, but their value semantics are specified separately. An
underscore-starting spelling such as `_123` is an identifier, not a number.
Width tags are not part of the target grammar, so spellings such as `10i32`
are not typed numeric literals.

```text
number          := decimalNumber | hexInteger | binaryInteger
decimalNumber   := decimalDigits ('.' decimalDigits)? (('e' | 'E') ('+' | '-')? decimalDigits)?
hexInteger      := ('0x' | '0X') basedHexDigits
binaryInteger   := ('0b' | '0B') basedBinaryDigits
decimalDigits   := digit ('_'* digit)*
basedHexDigits  := '_'* hexDigit ('_'* hexDigit)*
basedBinaryDigits := '_'* binaryDigit ('_'* binaryDigit)*
digit           := '0'..'9'
hexDigit        := digit | 'a'..'f' | 'A'..'F'
binaryDigit     := '0' | '1'
```

A period starts a fraction only when followed by a digit. A sign is its own
operator except within an exponent. An exponent without digits after a sign,
or followed by a non-identifier character such as `;`, whitespace, or `.`
without digits, is malformed; an `e` beginning another identifier (for
example `1efoo` or `1else`) remains outside the numeric token. Numeric width is
never encoded in a suffix. The lexer preserves the complete raw spelling;
target selection, constant parsing and numeric range/target-format validation
happen later.

An attached width suffix is invalid and diagnoses as `TRM0006`; a `0x`/`0X`
prefix lacking an ASCII hex digit diagnoses as `TRM0007`, and a `0b`/`0B`
prefix lacking an ASCII binary digit diagnoses as `TRM0008`. Malformed digit
separators in a numeric digit run diagnose as `TRM0009`. Runs
of underscores are allowed between digits and immediately after a based
prefix, but not at the end of a digit run or adjacent to its decimal point,
exponent marker or sign. If an exponent sign has no following digit, the
existing incomplete-exponent diagnostic `TRM0004` applies.
An underscore-starting name such as `_123` remains an identifier, not a number.
Hex digits include `a` through `f`, so `0x1f32` is one integer token whose full
value is hexadecimal; its `f32` tail is not a width suffix. A non-hex suffix
such as `0x1u32` is an invalid attached width suffix. Contextual target
selection constructs decimal, hexadecimal and binary integers without a
fallback numeric type. The executable boundary for fractional/exponent forms,
custom literal parsers, registered string targets and `[ConstEval]` is
documented in [implementation](implementation.md).

Decimal integer literals retain numeric range semantics, including the
existing direct negative signed-minimum case. Hexadecimal and binary literals
are target-width bit patterns: a signed target interprets the selected bits
using its signed representation, while an unsigned target retains the
unsigned value. Nonzero bits beyond the target width are rejected; leading
zeroes and separators do not count as significant bits.

## Strings and character notation

Strings are delimited by ASCII double quotes. Escapes supported are `\n`,
`\r`, `\t`, `\\`, `\"`, `\0`, `\u` plus exactly four hexadecimal digits, and
`\U` plus exactly eight hexadecimal digits representing a Unicode scalar
(not a surrogate or an out-of-range value). The original raw text, including
invalid escapes, is preserved. A line break or EOF before a closing quote
diagnoses an unterminated string without swallowing the next line. No
interpolation is defined; `$` outside a string is a bad token. Encoded text
targets such as `utf8z("Terrum")` are ordinary type/call forms, not lexer
prefixes or suffixed string tokens; the selected target parser validates
encoding and terminator requirements without itself granting pointer lifetime.

Single quotes do not delimit a token in this grammar. Their proposed
scalar/target-representability contract is in
[conversions](conversions.md#character-intent); it does not choose UTF-16 as
runtime text's default encoding. Checked numeric type-call conversions use
`T(expression)`, not a numeric prefix-cast production. Source functions and
local shadowing retain existing call precedence. Literal conversion remains
distinct from raw tokenization; execution support is documented in
[implementation](implementation.md).

## Trivia and diagnostics

Horizontal whitespace, line breaks, `//` comments up to but not including
the break, and non-nesting `/* ... */` comments are leading trivia. The final
zero-width EOF token owns trailing trivia. Unterminated block comments receive
a diagnostic. Malformed surrogate units inside either comment form are
diagnosed without changing the trivia text. Lexical diagnostic codes are
`TRM0001` for bad characters or malformed surrogates, `TRM0002` for an
unterminated string, `TRM0003` for an unterminated block comment, `TRM0004`
for an incomplete exponent, `TRM0005` for an invalid escape, `TRM0006`
for an attached numeric width suffix, `TRM0007` for a hexadecimal prefix
without a digit, `TRM0008` for a binary prefix without a digit, and `TRM0009`
for malformed digit separators. Every UTF-16 unit belongs exactly once
to either token text or its leading trivia, including invalid input.
Diagnostics for malformed text do not replace or normalize source characters.

## Prior art

The 2021 TerraFX lexer gist by Tanner Gooding and Contributors (MIT) is prior
art for property selection and source preservation. Its rune-indexed spans,
host Unicode categories and `IEnumerator` token model are not part of this
contract.
