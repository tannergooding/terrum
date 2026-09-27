# Lexical grammar

Lexing operates on an immutable .NET string snapshot. All spans, line columns,
diagnostics and token lengths use UTF-16 code units, not Unicode scalar counts.
Valid surrogate pairs advance together during identifier recognition; unpaired
surrogates remain in the snapshot and produce diagnostics, including within
comments and strings. Lines end at CRLF
(one break), CR, LF, NEL (U+0085), LS (U+2028), or PS (U+2029).
The character position inside a line includes its break when queried by offset;
the next line starts immediately after the break. Tabs, VT, FF, ASCII space,
NBSP, U+1680, U+2000–U+200A, U+202F, U+205F, and U+3000 are whitespace
trivia, not line breaks.

Identifiers start with `_` or Unicode 17.0.0 `XID_Start` and continue with
`_` or `XID_Continue` (UAX #31). Spelling is compared ordinally; the lexer
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
spellings are identifiers. In particular, the lexer does not reserve `_` as a
global discard or type-hole token; any special role is contextual grammar. The
current binder treats `_` as an ordinary identifier and assignment; it has no
special discard handling. General discard, wildcard-pattern and generic-hole
forms are unsupported. Expression statements or unused-local cleanup do not
implement the distinct discard semantics.

Target numeric literals are unsuffixed source tokens. Target grammar preserves
radix, exponent and signed-minimum behavior; a type is selected later by context
or an explicit type-call target. Digit separators are not supported by the
current lexer/scanner and their grammar is separate scheduled literal-frontend
work. Width tags are not part of the target grammar, so spellings such as
`10i32` are not typed numeric literals.

```text
number          := decimalNumber | hexInteger
decimalNumber   := digits ('.' digits)? (('e' | 'E') ('+' | '-')? digits)?
hexInteger      := ('0x' | '0X') hexDigits
digits          := digit+
hexDigits       := hexDigit+
digit           := '0'..'9'
hexDigit        := digit | 'a'..'f' | 'A'..'F'
```

A period starts a fraction only when followed by a digit. A sign is its own
operator except within an exponent. An exponent without digits after a sign,
or followed by a non-identifier character such as `;`, whitespace, or `.`
without digits, is malformed; an `e` beginning another identifier (for
example `1efoo` or `1else`) remains outside the numeric token. Numeric width is
never encoded in a suffix. The lexer preserves the complete raw spelling;
target selection, constant parsing and numeric range/target-format validation
happen later.

The syntax-15 parser rejects a recognized attached width suffix with `TRM0006`
and diagnoses a `0x`/`0X` prefix lacking an ASCII hex digit with `TRM0007`.
Hex digits include `a` through `f`, so `0x1f32` is one integer token whose full
value is hexadecimal; its `f32` tail is not a width suffix. A non-hex suffix
such as `0x1u32` remains rejected as an attached width suffix. The unchanged
scanner-4 five-word integer record uses tag 0 for valid decimal and
hexadecimal literals; recognized suffix tags remain only as metadata on
rejected suffix tokens. The executable binder constructs context-typed decimal
and hexadecimal integer literals without a fallback numeric type. Fractional
and exponent forms remain outside the executable numeric binder. Digit
separators, custom literal parsers, registered string-target execution and
`[ConstEval]` also remain unsupported.

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

Single-quoted character literals in design samples are not implemented tokens.
Their proposed scalar/target-representability contract is in
[conversions](conversions.md#character-intent); it does not choose UTF-16 as
runtime text's default encoding. The bounded parser now uses `T(expression)`
for checked numeric conversions to `int32`, `uint32`, `int64`, `uint64` and
`char16`; it has no numeric prefix-cast production. Source functions and local
shadowing retain existing call precedence. Literal conversion remains distinct
from raw tokenization; floating and custom literal parsing are not executable
support.

Horizontal whitespace, line breaks, `//` comments up to but not including
the break, and non-nesting `/* ... */` comments are leading trivia. The final
zero-width EOF token owns trailing trivia. Unterminated block comments receive
a diagnostic. Malformed surrogate units inside either comment form are
diagnosed without changing the trivia text. Lexical diagnostic codes are
`TRM0001` for bad characters or malformed surrogates, `TRM0002` for an
unterminated string, `TRM0003` for an unterminated block comment, `TRM0004`
for an incomplete exponent, `TRM0005` for an invalid escape, `TRM0006`
for an attached numeric width suffix, and `TRM0007` for a hexadecimal prefix
without a digit. Every UTF-16 unit belongs exactly once
to either token text or its leading trivia, including invalid input.
Diagnostics for malformed text do not replace or normalize source characters.

The 2021 TerraFX lexer gist by Tanner Gooding and Contributors was reviewed
as MIT-licensed prior art for property selection and source preservation.
Its rune-indexed spans, host Unicode categories, and `IEnumerator` token
model are intentionally not used here.
