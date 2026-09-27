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

Target numeric literals are unsuffixed source tokens. Existing radix, exponent
and signed-minimum behavior is preserved; a type is selected later by context
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

The bounded current scanner still exposes legacy integer width-tag metadata
for its existing P0 component. That implementation boundary is scheduled for
migration and does not define target source syntax.

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
runtime text's default encoding. Numeric literal construction and explicit
`T(expression)` conversion are binder/evaluator work, distinct from the raw
numeric token grammar above. The bounded current binder's prefix-cast spelling
is legacy pending migration and is not the target form.

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
