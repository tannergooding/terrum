# Lexical grammar

This document and the [source grammar](grammar-spec.md#2-lexical-notation)
specify approved source rules. Open recovery and contextual-token details
are identified explicitly; [implementation](implementation.md) records which
forms the compiler supports.

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
trivia, not line breaks.

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
Keywords are the lower-case ASCII reserved spellings designated by the
source grammar; its context table identifies words recognized as keywords
only in designated syntactic positions. Primitive type names remain identifiers.
All other admissible spellings are identifiers. `_name` and `_123` remain identifiers.
A contiguous `@` followed by an exact reserved or contextual keyword spelling
forms an escaped identifier. Its name is the keyword spelling without `@`;
the escape suppresses keyword recognition but does not normalize the name
or grant primitive-definition authority. Thus `@await` is an identifier,
while `@ordinaryName`, `@_` and `@ await` are not valid keyword escapes.
The `@` escape does not admit arbitrary-string identifiers.
Future keyword reservations and contextual recognition must preserve the
interpretation of valid source within its specified source-language version;
breaking recognition changes require a version boundary.
The standalone `_ = expression;` statement evaluates the bound expression
once, with its effects and failures, and cleans up an owning result at statement
exit. It does not name a destination or infer a type for a context-free literal.
Void calls and named mutable-buffer-view `Set(index, value)` calls are separate
expression statements. Generic type holes, wildcard patterns and unnamed
lambda parameters are separate grammar constructs; the reserved token does not
grant them discard semantics.

## Numeric tokens

Numeric tokens begin with a decimal digit and are unsuffixed. The lexer
recognizes decimal and hexadecimal floating forms, decimal, hexadecimal and
binary integer forms, and `_` separators in their digit runs; a type is selected later by context or an
explicit type-call target. Decimal fractional/exponent forms have lexical
syntax here, but their value semantics are specified separately. An
underscore-starting spelling such as `_123` is an identifier, not a number.
Width tags are not part of the target grammar, so spellings such as `10i32`
are not typed numeric literals.

```text
number          := decimalNumber | hexFloat | hexInteger | binaryInteger
decimalNumber   := decimalDigits ('.' decimalDigits)? (('e' | 'E') ('+' | '-')? decimalDigits)?
hexFloat        := ('0x' | '0X') ('_'* hexDigits ('.' hexDigits?)? | '.' hexDigits) ('p' | 'P') ('+' | '-')? decimalDigits
hexInteger      := ('0x' | '0X') basedHexDigits
binaryInteger   := ('0b' | '0B') basedBinaryDigits
decimalDigits   := digit ('_'* digit)*
hexDigits       := hexDigit ('_'* hexDigit)*
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
fallback numeric type. Fractional/exponent value semantics, custom literal
parsers, registered string targets and `[ConstEval]` are specified outside
this lexical contract.

Decimal integer literals have numeric range semantics, including the direct
negative signed-minimum case. Hexadecimal and binary literals
are target-width bit patterns: a signed target interprets the selected bits
using its signed representation, while an unsigned target retains the
unsigned value. Nonzero bits beyond the target width are rejected; leading
zeroes and separators do not count as significant bits.

A hexadecimal float requires a `p`/`P` exponent with decimal digits and
hexadecimal digits on at least one side of the optional point. `0x1f` is an
integer; `0x1.fp+2` is floating. Its exact binary-rational value rounds
directly to the contextual target under the specified floating conversion
policy, without an intermediate host type. Exact malformed-exponent,
separator and identifier-tail recovery remains open.

A bare numeric literal must be grouped before member access, independent of
radix or whether the following name resembles fractional/exponent digits.
Use `(literal).Member` or an explicitly typed `T(literal).Member`; spacing
around the period does not make an ungrouped receiver legal. This also
applies to optional member access and witness selection. Grouping alone
does not infer a numeric target. Valid numeric fractions/exponents and range
bounds retain their existing rules, and already-delimited text/character
literals have no equivalent grouping requirement. Malformed numeric forms
do not become valid member accesses through an alternate tokenization.

## Punctuation boundaries

Symbolic binary arithmetic, shift, comparison, bitwise, Boolean and
coalescing expression operators require separating trivia on both sides.
Symbolic prefix operators attach directly to their following operand token;
symbolic postfix operators attach directly to their preceding operand token.
Member-access `.` and `?.` must adjoin the receiver's final token or the
following member's first token, or both. Whitespace, line breaks and comments
provide separation and break attachment. Thus `x + y`, `x + ++y`, `-x`,
`x++`, `x.y`, `x. y` and `x .y` are valid forms, while `x+y`, `- x`,
`x ++` and `x . y` reject. Operator roles come from grammar before type
checking, not from an operand's type or a spacing-driven reinterpretation.
Calls and indexing are delimited postfix operators, distinct from grouping
despite sharing delimiter characters. Call argument `(` and index `[`/`?[`
openers must directly adjoin the receiver's final token, including
type/constructor calls and generic or
parenthesized receivers. Thus `f(x)`, `f<T>(x)` and `items?[i]` satisfy
attachment; `f (x)` and `items [i]` reject. Grouping/control-flow parentheses
have no receiver and are not subject to call attachment. Interior trivia
in ordinary parentheses, brackets and braces is optional, including comments
and line breaks around enclosed tokens. Multiline calls keep the opening
`(` attached to the callee while permitting arguments and the closing `)`
on subsequent lines. Receiver/operator attachment remains independent of
interior padding, and literal/interpolation delimiters retain their own rules.
Commas and semicolons are fixed structural separators with flexible trivia
on either side, not postfix operators or parts of operator runs. Leading-comma
multiline alignment is valid. Spacing does not change where a separator,
trailing comma or empty statement is allowed.
Generic argument and declaration parameter lists require their opening `<`
to adjoin the preceding name. Interior trivia remains optional, including
comments and multiline arguments/closers. A constructed type remains a
sequence of lexical tokens; generic delimiters are recognized by grammatical
role, distinct from spaced binary comparisons.
Designated keyword-introduced angle lists likewise attach their `<` to
the introducer: `default<I>`, `.via<I>`, `policy<Domain.Case>` and
`ref<T>` (including `init ref<T>` and `implicit ref<T>`). Their interiors
retain ordinary trivia, but trivia before the opener rejects. Word
boundaries between separate keywords are unchanged.
Optional-type suffix `?` adjoins the type's final token, including a tuple
or generic closing delimiter. Type-context adjacency permits `List<T?>`,
`List<T>?` and `List<List<T?>>`; it does not define general expression
operators containing those glyph runs. `T ?` rejects, as does repeated
source optional marking `T??`. Spaced coalescing `a ?? b` is unchanged.
Keyword operators retain word boundaries. Other delimiter roles and
range syntax retain their own grammatical
rules; their exact remaining spacing requirements are separate design work.

Punctuation adjacency follows an allow list: one defined spelling or an
explicit grammatical boundary, not an enumeration of forbidden combinations.
A contiguous run of operator glyphs must be one defined operator or
punctuator, not implicitly concatenated operators: `x+++y` rejects,
whereas `x++ + y` and
`x + ++y` satisfy the operator-role spacing rules. Trivia can separate
operators; grouping delimiters, commas and semicolons are not part of such a
run. Nested generic
closing delimiters are recognized in type-argument context. Exact glyph
membership and contextual exceptions, including optional/type punctuation
and comment openers, remain to specify. Neither recovery nor a formatter may
guess a different expression from an invalid run.

`..^` is one defined composite punctuator for an end-relative upper slice
bound, as in `items[start..^1]`. The grammar also permits separated `..`
and `^`, as in `items[start.. ^1]`, with the same slicing semantics.
This does not allow arbitrary concatenation or assign the unlisted `...`
spelling. Lexical recognition does not admit `..^` outside its slice role.
The slice connector `..` allows flexible trivia on either side, including
omitted bounds where the grammar permits. From-end prefix `^` attaches
to its following offset token; the composite `..^` does likewise.
Thus `items[start .. end]`, `items[^1]`, `items[start.. ^1]` and
`items[start..^1]` are valid forms; `^ 1` and `..^ 1` reject.
Expression bitwise XOR remains a distinct spaced binary operator.

## Strings and character notation

Strings are delimited by ASCII double quotes. Escapes supported are `\n`,
`\r`, `\t`, `\\`, `\"`, `\0`, `\u` plus exactly four hexadecimal digits, and
`\U` plus exactly eight hexadecimal digits representing a Unicode scalar
(not a surrogate or an out-of-range value). The original raw text, including
invalid escapes, is preserved. A line break or EOF before a closing quote
diagnoses an unterminated string without swallowing the next line. Encoded text
targets such as `utf8z("Terrum")` are ordinary type/call forms, not lexer
prefixes or suffixed string tokens; the selected target parser validates
encoding and terminator requirements without itself granting pointer lifetime.

Single quotes delimit one decoded Unicode scalar, with the ordinary escapes
and `\'`; empty, multiple-scalar and surrogate values reject. The expected
type must represent that scalar, with no fallback character type. The
scalar/target-representability contract is in
[conversions](conversions.md#character-intent); it does not choose UTF-16 as
runtime text's default encoding. Checked numeric type-call conversions use
`T(expression)`, not a numeric prefix-cast production. Source functions and
local shadowing follow call precedence independent of literal tokenization.
Literal conversion remains distinct from raw tokenization.

`$"..."` interpolates expression-aware `{...}` holes; doubled braces in
literal segments denote literal braces. Decoded escapes never become hole
delimiters. Ordinary quoted text does not interpolate.
Raw text uses whole matching quote fences of at least three quotes.
Multiline and empty raw values use delimiter-line framing and exact
closing-indent prefix removal, not visual-column indentation. Unmarked raw
text preserves source line endings. An opening-line escaped newline
directive normalizes literal segments only to LF, CR, VT, FF, NEL, LS, PS or
CRLF; it does not reclassify source VT/FF as line breaks.

For raw interpolation, `N` leading dollar signs select `N`-brace delimiters.
In literal text, shorter runs are literal; opening runs of length `N` through
`2N - 1` put their literal excess before the hole, and closing runs put it
after the hole. Boundary runs of `2N` or more reject. Nested expression
delimiters are parsed as expressions, not split by the outer text scanner.
The [source grammar](grammar-spec.md#2-lexical-notation) defines complete
framing, whitespace-prefix, directive and brace-boundary rules.
Interpolation formats are an identifier's static spelling, ordinary/raw
quoted text, or a parenthesized runtime expression. Alignment is an ordinary
expression; each hole's value, width and format are captured once in source
order before draining. Target handlers and representation remain separate
checked contracts. Exact recovery for these text forms remains open.

## Trivia and diagnostics

Horizontal whitespace, line breaks, `//` comments up to but not including
the break, and non-nesting `/* ... */` comments are leading trivia.
Documentation adds one slash: `///` introduces a documentation line, and
`//* ... *//` is a non-nesting documentation block. Recognize `//*` before
ordinary `//`; within that block, only `*//` closes it, not `*/`.
Documentation trivia retains its source extent while participating in
declaration attachment and separate documentation extraction.
Generalized matching-length star fences and their exact closing-run rules
remain unspecified.
The final zero-width EOF token owns trailing trivia. Unterminated block
comments receive a diagnostic. Malformed surrogate units inside any comment form are
diagnosed without changing the trivia text. Lexical diagnostic codes are
`TRM0001` for bad characters or malformed surrogates, `TRM0002` for an
unterminated string, `TRM0003` for an unterminated block comment, `TRM0004`
for an incomplete exponent, `TRM0005` for an invalid escape, `TRM0006`
for an attached numeric width suffix, `TRM0007` for a hexadecimal prefix
without a digit, `TRM0008` for a binary prefix without a digit, and `TRM0009`
for malformed digit separators. Every UTF-16 unit belongs exactly once
to either token text or its leading trivia, including invalid input.
Diagnostics for malformed text do not replace or normalize source characters.
