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
spellings are identifiers.

Numeric tokens use ASCII digits only:

```text
number   := digit+ ('.' digit+)? (('e' | 'E') ('+' | '-')? digit+)?
digit    := '0'..'9'
```

A period starts a fraction only when followed by a digit. A sign is its own
operator except within an exponent. An exponent without digits after a sign,
or followed by a non-identifier character such as `;`, whitespace, or `.`
without digits, is malformed; an `e` beginning another identifier (for
example `1efoo` or `1else`) remains outside the numeric token. The
defined primitive width tags `i8`, `i16`, `i32`, `i64`, `i128`, `u8`, `u16`,
`u32`, `u64`, `u128`, `f16`, `f32`, `f64`, and `f128` may immediately follow
the digits. They are recognized only when the following scalar cannot
continue an identifier: `10i32` is one token, but `10i32extra` is `10` and
`i32extra`. Other adjacent identifiers remain separate. The lexer preserves
raw spelling; typing and numeric range/target-format validation happen later.

Strings are delimited by ASCII double quotes. Escapes supported are `\n`,
`\r`, `\t`, `\\`, `\"`, `\0`, `\u` plus exactly four hexadecimal digits, and
`\U` plus exactly eight hexadecimal digits representing a Unicode scalar
(not a surrogate or an out-of-range value). The original raw text, including
invalid escapes, is preserved. A line break or EOF before a closing quote
diagnoses an unterminated string without swallowing the next line. No
interpolation is defined; `$` outside a string is a bad token.

Horizontal whitespace, line breaks, `//` comments up to but not including
the break, and non-nesting `/* ... */` comments are leading trivia. The final
zero-width EOF token owns trailing trivia. Unterminated block comments receive
a diagnostic. Malformed surrogate units inside either comment form are
diagnosed without changing the trivia text. Lexical diagnostic codes are
`TRM0001` for bad characters or malformed surrogates, `TRM0002` for an
unterminated string, `TRM0003` for an unterminated block comment, `TRM0004`
for an incomplete exponent and `TRM0005` for an invalid escape. Every UTF-16
unit belongs exactly once to either token text or
its leading trivia, including invalid input. Diagnostics for malformed text
do not replace or normalize source characters.

The 2021 TerraFX lexer gist by Tanner Gooding and Contributors was reviewed
as MIT-licensed prior art for property selection and source preservation.
Its rune-indexed spans, host Unicode categories, and `IEnumerator` token
model are intentionally not used here.
