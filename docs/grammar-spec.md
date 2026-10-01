<a id="terrum-grammar-proposal"></a>

# Terrum source grammar

> [!IMPORTANT]
> The settled source forms and semantic constraints in this document are
> approved language specification, not a claim of compiler support.
> Explicitly open or deferred choices remain unresolved; informative examples
> and library names do not establish additional contracts.

This document specifies source notation and selected semantic constraints.
It is a bounded specification, not a complete grammar for every language
feature. The [implementation reference](implementation.md) records supported
subsets independently.

The [language](language.md), [memory model](memory-model.md),
[conversions](conversions.md), [lifetimes](lifetimes.md), and
[libraries](libraries.md) documents define the related accepted semantic
contracts. The [approved current semantic IL/metadata design](il-metadata.md),
[core-library proposal](proposals/core-library-reference.md) and
[IL extension proposal](proposals/il-extensions.md) are separate documents;
source approval does not adopt proposed APIs or metadata extensions.

The [remaining source-question proposal](proposals/grammar-open-questions.md)
collects unresolved decisions, comparisons and recommendations. Those
recommendations are not normative; the explicit unspecified boundaries here
remain in force until a decision is adopted.

## 1. Scope, status, and notation

The notation is EBNF-like: `{ X }` means zero or more, `[ X ]` means
optional, `|` means alternative, terminals are in quotes, and named lexical
tokens such as `Identifier` are supplied by the lexer. Productions and their
stated static-semantic restrictions define the approved source forms.
**ACCEPTED DIRECTION** identifies a settled semantic constraint.
**OPEN**, **proposed**, and **deferred** boundaries explicitly identify matters
not settled here; permissive placeholders do not resolve them. Examples are informative and
conformance cannot be inferred from matching an example.

Source forms, semantic constraints, and unresolved choices have distinct
roles. This specification makes no implementation or pre-v1 stability claim.

This is a bounded source-language specification. A reserved spelling does not by itself establish a
production; recovery syntax is not a conforming source form.

**Documentation-comment direction (accepted source form, detailed grammar
open):** attach `///` documentation to declarations, with `//* ... *//` as an
equivalent block form. Use GFM-compatible prose, implicit leading summary and
structured top-level headings such as `# param name` and `# safety`; ordinary
lower-level headings remain prose. Declaration-scoped mentions such as
`@value`, `@T`, and `@Type.Member` resolve to structured references. Ordinary
Markdown links remain for section/document navigation, while explicit
`xref:` links may target custom labels or qualified references. Backticks
denote code, not implicit references. Comments compile to a separate XML
documentation artifact and do not create executable contracts; effects,
ownership, lifetime, output and failure guarantees come from checked
declaration metadata. Exact Markdown tokenization/escaping, complex generic
and overload reference grammar, XML schema, member IDs, inheritance and
packaging remain work. Normal extraction does not execute embedded code or fetch remote includes.
The documentation marker adds one slash to the ordinary comment form:
ordinary line comments use `//`, documentation lines use `///`, ordinary
block comments use `/* ... */`, and documentation blocks use `//* ... *//`.
Recognize the documentation-block opener before ordinary line comments.
Both block forms select their star-fence length from the whole opening star
run. Closers require the same whole star run and respectively one or two
slashes; longer closing-looking star runs reject. Contents are opaque and
do not nest. Opening and closing fences cannot overlap, so `/**/` opens a
two-star comment rather than representing an empty one. See the
[lexical trivia contract](lexical-grammar.md#trivia-and-diagnostics).

## 2. Lexical notation

The lexical contract is specified in the canonical
[lexical grammar](lexical-grammar.md). The following summarizes relevant
syntax boundaries:

- Source positions and token lengths count UTF-16 code units. Identifier starts/continues follow the pinned Unicode 17.0.0 XID properties plus `_`; spelling is ordinal, case-sensitive, and not normalized. Keywords are lower-case ASCII token spellings. Primitive names such as `int32` are ordinary identifiers.
- Whitespace and comments are leading trivia; line breaks include CRLF, CR, LF, NEL, LS, PS. Line/block comments do not nest. EOF owns trailing trivia.
- Unsuffixed decimal, hexadecimal (`0x`/`0X`), binary (`0b`/`0B`) and hexadecimal floating-point forms are described below. Separators may occur between digits and, for based values, immediately after the prefix. Signs are separate operators except an exponent sign. Width suffixes are invalid; `10i32` is not a typed literal. `_123` is an identifier.
- Strings use `"..."` with the documented escapes. Interpolated text uses `$"..."` with `{expression}` holes; ordinary strings do not interpolate. Single-quoted character literals denote one decoded Unicode scalar; numeric width suffixes are invalid.
- Lexical diagnostics preserve malformed source spelling; bad characters/surrogates, invalid escapes, unterminated literals/comments, bad prefixes, separators, and attached width suffixes have distinct diagnostic codes in the lexical reference.

The schematic lexical terminals used below are:

```ebnf
Identifier       = lexer-identifier-token | EscapedKeyword ;
EscapedKeyword   = "@", KeywordSpelling ;
Number           = DecimalNumber | HexFloat | HexInteger | BinaryInteger ;
DecimalNumber    = DecimalDigits, [ ".", DecimalDigits ],
                   [ ( "e" | "E" ), [ "+" | "-" ], DecimalDigits ] ;
HexFloat         = ( "0x" | "0X" ), ( { "_" }, HexDigits, [ ".", [ HexDigits ] ]
                 | ".", HexDigits ), ( "p" | "P" ), [ "+" | "-" ], DecimalDigits ;
HexInteger       = ( "0x" | "0X" ), { "_" }, HexDigit, { { "_" }, HexDigit } ;
BinaryInteger    = ( "0b" | "0B" ), { "_" }, BinaryDigit, { { "_" }, BinaryDigit } ;
DecimalDigits    = Digit, { { "_" }, Digit } ;
HexDigits        = HexDigit, { { "_" }, HexDigit } ;
Digit            = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
HexDigit         = Digit | "a".."f" | "A".."F" ;
BinaryDigit      = "0" | "1" ;
String           = '"', { StringCharacter | Escape }, '"' ;
RawText          = RawDelimiter, RawContent, RawDelimiter ;
RawDelimiter     = '"""', { '"' } ;
RawContent       = lexer-raw-text-content ;
InterpolatedRawText = DollarPrefix, RawDelimiter,
                      RawInterpolatedContent, RawDelimiter ;
DollarPrefix     = "$", { "$" } ;
RawInterpolatedContent = lexer-raw-interpolated-content ;
InterpolatedText = "$", '"',
                   { InterpolatedTextCharacter | Escape | LiteralBrace
                   | InterpolationHole }, '"' ;
LiteralBrace     = "{{" | "}}" ;
InterpolationHole = "{", InterpolationContents, "}" ;
InterpolationContents = Expression, [ ",", Expression ], [ ":", FormatSpecifier ] ;
FormatSpecifier = Identifier | String | RawText | "(", Expression, ")" ;
Character        = "'", ( CharacterScalar | Escape | "\'" ), "'" ;
Escape           = "\n" | "\r" | "\t" | "\\" | '\"' | "\0"
                 | "\u", HexDigit, HexDigit, HexDigit, HexDigit
                 | "\U", HexDigit, HexDigit, HexDigit, HexDigit,
                   HexDigit, HexDigit, HexDigit, HexDigit ;
RawNewlineDirective = Escape, [ Escape ] ;
Boolean          = "true" | "false" ;
Null             = "null" ;
Trivia           = whitespace | line-comment | non-nesting-block-comment ;
```

`lexer-identifier-token` denotes the single lexical Identifier token, excluding
reserved keywords and the standalone reserved `_`; it is not three grammar
symbols. `EscapedKeyword` is one contiguous token: `KeywordSpelling` is an
exact reserved or contextual spelling in the vocabulary below. Its identifier
identity excludes `@`, and escaping suppresses keyword recognition at that
occurrence. It does not admit arbitrary text, whitespace after `@`, a
non-keyword spelling, or the standalone `_`.
`StringCharacter` excludes an unescaped quote, backslash, or line break; `\U` must decode to a Unicode scalar. Separators may repeat between digits and after a based prefix, but not at the end of a run or beside a decimal point/exponent marker/sign. The lexical reference defines malformed-token recovery and exact diagnostics.

The `$"` interpolation opener is contiguous. `InterpolatedTextCharacter`
excludes an unescaped quote, backslash, line break or brace. In a text segment,
`{{` emits `{` and `}}` emits `}`; unmatched single braces reject.
Existing Unicode escapes remain literal text even when they decode to a
brace, never becoming interpolation delimiters after decoding.
Hole expressions use ordinary expression
parsing, including nested delimiters and their own quoted literals, not a
scan that terminates at the first `}` regardless of nesting.
Brace doubling applies only in text segments, not while parsing a hole's
expression and its nested delimiters or literals.
Ordinary strings retain literal braces. A hole may specify alignment, format,
or both: `{value}`, `{value:X8}`, `{value,12}`, and `{value,-12:X8}`.
Parse a full ordinary expression before recognizing a suffix; conditionals
need no parentheses merely to protect their colon. Thus
`{flag ? left : right}` is a conditional hole, and
`{flag ? left : right:X8}` formats its result. The alignment expression uses
the same expression-aware parsing.
After `:`, quoted text supplies a format literal, a parenthesized expression
supplies a runtime format, and an identifier supplies its spelling as static
format text, not a symbol reference. Selection is syntactic, not a test of
format validity or expression binding. Examples are `{value:X8}`, `{value:"X8"}`, and
`{value:(formatSpec)}`. A raw text literal may supply the quoted format;
other computed/interpolated formats can appear in the parenthesized
expression. Identifier shorthand accommodates standard numeric format codes
such as `X8` or `G17`. Quoted text can provide standard or custom formats;
the expression form can provide any eligible format, including a nonconstant
one. Punctuation, spaces and reserved-word spellings require a quoted format
or the expression form. Ordinary source trivia may separate suffix tokens;
quoted content retains its literal whitespace/escape rules. There is no
separate bare-format escape language or lexer mode. Exact interpolation
recovery remains separate lexical work.

Raw text uses matching opening and closing quote fences of length three or
greater. Their lengths must agree, and content quote runs must be shorter
than the selected fence. Backslashes and braces are literal content, without
escape decoding, interpolation or brace doubling. Increasing the fence
length accommodates quote runs in the content, as in
`""""He wrote """ literally.""""`. Raw text retains ordinary target-directed
text typing; rawness does not select an allocating string type.
`lexer-raw-text-content` denotes content bounded by that selected fence, not
an identifier token. The entire contiguous opening quote run selects the
fence length; a closing fence must be a whole matching run. Longer closing
runs reject rather than splitting into content quotes and a shorter fence.
Single-line raw payloads cannot begin or end with a quote adjoining the
fence; use multiline framing for quote-edge content. A contiguous all-quote
run does not split into an opening and closing fence to represent empty raw
text; use ordinary `""` or the empty framed form.

Multiline raw text uses delimiter lines. After the opening fence, optional
horizontal whitespace and an optional newline directive precede the framing
newline; horizontal whitespace may surround the directive. The closing
line contains only optional indentation before its fence; ordinary source
tokens may follow that fence. The newline after the opening delimiter and
the newline before the closing delimiter are framing, not payload. Additional
blank content lines supply leading/trailing payload newlines. Adjacent
opening and closing lines are a valid empty raw value; their shared framing
newline is excluded once, without requiring a dummy content line.
The nonframed single-line form does not permit literal text line breaks.
For a multiline form, the exact horizontal whitespace before the closing
fence is the indentation prefix. Each nonblank literal content line must
begin with that prefix; remove it and preserve all remaining content.
A whitespace-only line must either begin with the prefix or itself be a
prefix of it. Remove as much of the prefix as present, preserving any excess
whitespace; incompatible whitespace sequences reject. Tabs and spaces are
compared as characters, not visual columns, with no normalization.
An empty closing indentation preserves all content indentation.
In interpolated raw text, this transformation applies only to literal text
line prefixes, not hole-expression trivia or the formatted values of holes.
Without a directive, interior source line-ending sequences are preserved,
including CRLF versus LF and other recognized line breaks; compilation does
not substitute a host-platform newline or normalize to LF. Framing removal
treats CRLF as one line-break sequence and excludes both code units when it
is framing.
Source line-ending conversion can therefore change an unmarked raw value.
An opening-line `\n` directive replaces each interior source line-break
sequence with LF; `\r\n` replaces each with CRLF. These are literal source
escape spellings consumed as header metadata, followed by the physical
framing newline, not emitted text. They apply only to framed multiline
raw forms, including interpolated raw text. Single-line `"""\n"""` remains
literal backslash-plus-`n`. Normalization affects literal source segments,
not hole-expression trivia or formatted interpolation values.
The directive uses existing escape spellings whose decoded value is exactly
one of LF (U+000A), CR (U+000D), VT (U+000B), FF (U+000C), NEL (U+0085),
LS (U+2028), PS (U+2029), or the CRLF pair. Equivalent `\u`/`\U` spellings
are allowed; other characters, reversed pairs and arbitrary separator
strings reject. Thus `\u2028` selects LS, while `\u000D\u000A` is equivalent
to `\r\n`. A CRLF source break is replaced once, not as separate CR and LF
breaks. Every recognized source break is replaced independently; normalization
does not collapse consecutive breaks.
These are output-target choices, not source-lexer classification changes:
VT and FF in raw content remain their existing whitespace/content characters,
not additional source line breaks to normalize. The chosen text target must
represent the selected separator under its ordinary checked text contract.
Whole-output normalization operations and handler metadata remain separate
library design.

Interpolated raw text prefixes its quote fence with one or more contiguous
`$` characters. With `N` dollar signs, a hole uses `N` opening and `N` closing
braces around the same `InterpolationContents`, including optional alignment
and format; text brace runs shorter than `N` are
literal. For example, `$$"""{ "count": {{count}} }"""` preserves the single
JSON braces and interpolates `count`. Quote-fence length and brace-delimiter
length are independent. Raw text segments perform neither backslash escape
decoding nor brace doubling. Hole expressions retain ordinary lexical and
parsing rules; their nested literals and delimiters are not outer text
segments. In text mode, an opening brace run of length `R`, with
`N <= R < 2N`, emits its first `R - N` braces literally and uses its final
`N` braces to open a hole. After parsing the hole contents, a closing
run of that range uses its first `N` braces to close the hole and emits the
remaining braces literally. Expression-owned nested delimiters are parsed
as expression syntax before identifying the hole closer. A closing run
requiring a hole when no hole is open rejects.
Boundary runs of `2N` or more reject; increase the dollar count for longer
literal runs. Thus `$$"""{{{value}}}"""` emits literal braces around the
formatted value. The split is fixed by lexical position and delimiter
count, never by testing alternative expression interpretations.
`lexer-raw-interpolated-content` denotes these alternating segments and
holes under the selected delimiter lengths. Interpolated raw text uses the
same multiline framing; line breaks inside hole expressions follow ordinary
expression trivia rules, not literal text framing. Exact recovery remains
separate lexical design work.
Both interpolated forms use the same checked target-directed handler and
capture/drain contracts in §3.4.

Symbolic expression operators have role-specific spacing requirements.
Binary arithmetic, shift, comparison, bitwise, Boolean and coalescing
operators require trivia separating the operator from each operand:
`x + y` is valid, but `x+y`, `x+ y` and `x +y` reject. Symbolic prefix
operators attach directly to the following operand token, and symbolic
postfix operators attach directly to the preceding operand token:
`-x`, `-(expr)`, `++x` and `x++` are valid; `- x`, `++ x` and `x ++` reject.
Member-access `.` and `?.` attach directly to the receiver's final token
or the following member's first token, or both: `Foo.Bar`, `Foo. Bar` and
`Foo .Bar` are valid, but `Foo . Bar` rejects. Whitespace, line breaks and
comments supply separation and break attachment on that side.
Operator roles are determined by grammar before type checking; an invalid
spacing or type does not trigger another interpretation. Keyword operators
retain their word-boundary requirements. Other punctuation, including
grouping/list delimiters, generic delimiters and range syntax, follows its
own grammatical rules; it is not automatically classified as an expression
operator.
These are source validity requirements, not merely formatter preferences.
Assignment and binding operators also require separating trivia on both
sides, including `=`, complete compound-assignment spellings, declaration
initializers, parameter/generic defaults, enum values, aliases and associated
type/constant bindings. Use `x = value`, `x += value`, `int32 x = 1`,
`T = void` and `alias A<T> = T`, not their squashed counterparts.
Each context retains its type, constant and ownership checks; spacing does
not make assignment an expression.
The `=>` connector requires separating trivia on both sides in expression
bodies, lambdas and switch arms. Use `int32 F() => 1;`, `x => x + 1`
and `0 => 1`, not their squashed counterparts. `=>` is one indivisible
spelling; `= >` does not form an arrow.
Conditional-expression `?` and `:` each require separating trivia on both
sides: use `flag ? left : right`, not `flag?left:right` or partially
squashed variants. Their grammatical role is distinct from optional-type
suffixes, optional access and other colon roles. Interpolation still permits
`{flag ? left : right:X8}`: the conditional colon is separated, while the
format colon follows its own grammar. Grouping the full conditional remains
optional.
Declaration-relationship `:` requires separating trivia on both sides
in conformance/interface-base lists, enum underlying types and generic
constraints: `struct S : I`, `enum E : int32` and `where T : I`.
These colons connect declared relationships; they are not attached type
suffixes. Label, named-argument, tuple-name, attribute-target and format
colons follow their own grammatical roles.
Label, named-argument, tuple-name and attribute-target colons attach to the
preceding name or selector's closing delimiter. Ordinary trivia after a
non-label colon is optional: `count: value` and `count:value` are valid, while
`count : value` rejects in those roles. Use `outer: while (ready)` and
`[param(count): Limit(16)]`. A loop label's colon and loop introducer must
remain on the same physical source line; stacked labels are invalid.
Label scope is specified in §3.3, independently of attribute schemas.
Interpolation format colons retain their separate grammar.
Trivia does not permit splitting or merging a token spelling after lexing.
Receiver/name-attached delimited constructs share a boundary rule:
the opener attaches to its receiver or introducing name, the interior
follows its ordinary grammar/trivia rules, and a matching delimiter closes
the construct. Calls `x(...)`, indexing `x[...]` and generic application
`x<...>` follow this pattern. Calls/indexing may attach to the final token
of a compound receiver, not only an identifier; generic lists attach to
their introducing name. Interior trivia does not relax operator spacing,
nesting, argument or other content requirements.
Callable declaration parameter lists attach `(` to the final name or
operator token; indexer declarations attach `[` to `this`, and attribute
argument lists attach `(` to the attribute name. Use `F(int32 x)`,
`Box(int32 x)`, `operator +(T x)`, `this[nuint index]` and `[Limit(16)]`,
not `F (int32 x)`, `operator + (T x)`, `this [nuint index]` or
`[Limit (16)]`. Declaration operator tokens do not acquire binary-expression
spacing requirements.
Qualified-name dots in types, namespaces, imports and attribute names attach
on both sides: `Terrum.Collections.List<T>`, not
`Terrum .Collections.List<T>` or `Terrum. Collections.List<T>`. Expression
member access retains its either-side attachment rule, even when the receiver
binds to a type; syntax determines the role before lookup.
Structural control-flow parentheses and external block braces allow flexible
outer trivia: both `if(ready){ ... }` and `if (ready) { ... }` are valid.
On a single line, the canonical formatter separates a structural keyword
from `(`, a header from `{`, and `}` from a following structural keyword
with one space: `if (...) { ... }`, `else { ... }` and `} while (...);`.
Multiline layout may use line breaks at those boundaries.
This formatting convention is not an additional source-validity rule and
does not relax receiver/name attachment or interior operator spacing.
Punctuation adjacency follows an allow list, not a list of forbidden runs.
A contiguous run from the ASCII glyph set `!%&*+-./:<=>?^|~`
must be one defined operator or punctuator, not an implicit concatenation
of separate operators.
Thus `x+++y` rejects, while
`x++ + y` and `x + ++y` separate and attach operators according to their
roles. Whitespace or a comment can separate operators; ordinary grouping
delimiters, commas and
semicolons are not part of the run. Nested generic closing delimiters are
recognized in type-argument context, so `List<List<T>>` does not require
spaces. Literal tokens, selected comment forms and narrow keyword escapes
take lexical priority over operator-run validation. `$`, `@`, grouping/list
delimiters and Unicode lookalikes are not operator glyphs. Grammar-authorized
type-context boundaries, including optional suffixes and nested generic
closers, are separate explicit rules, not permission to reinterpret a run
by guessing user intent.
Unlisted combinations require separation; adding an operator spelling or a
contextual adjacency exception requires an explicit language rule.

`CharacterScalar` excludes an unescaped quote, backslash or line break.
Escape decoding must yield exactly one Unicode scalar, never a surrogate.
The expected character type must represent it: `char32`/`rune` admits any
scalar, `char16` one nonsurrogate code unit, and `char8` ASCII. There is no
fallback character type.

`Number` is one token: unary `-` is an operator, not part of the number.
Numeric target selection/range and the lack of a default type are semantic
rules (see [conversions](conversions.md)). A hex float requires a binary
exponent (`p`/`P`), so `0x1f` is an integer while `0x1.fp+2` is a floating
token. It has digits on at least one side of the point; the exponent has
decimal digits, and `_` follows the existing between-digits rules. A leading
sign is separate from the token, unlike the exponent sign.

The `HexFloat` production above specifies its spelling and is
included in `Number` and `Primary` just like other numeric literals.
Its token-boundary and diagnostic rules (including incomplete `p` exponents,
separators and identifier tails) still need to be specified.
The selected floating target supplies the format; a hex float's exact
binary-rational value is rounded directly to that format using the selected
IEEE-754 nearest/ties-even default and the established overflow/underflow
rules, not through an intermediate host floating type. Both
`float32 x = 0x1.fp+2;` and `var x = float32(0x1.fp+2);` select `float32`;
`var x = 0x1.fp+2;` has no target and diagnoses.
The [lexical grammar](lexical-grammar.md) shares these source rules; open
recovery details do not permit alternative target-format rounding.

Bare numeric literals require grouping before member access, for every
numeric radix and format. `(literal).Member` and `T(literal).Member` have an
explicit receiver boundary; `literal.Member` is not a source form. The same
restriction applies to optional member access and `.via<I>` witness selection,
and trivia between the literal and member punctuator does not remove it.
Grouping does not supply a missing numeric target; an explicit type call can.
A period belonging to a valid numeric fraction remains part of that literal,
not member access. Numeric range bounds such as `items[0..2]` remain legal;
strings, characters and other already-delimited literals do not require this
numeric-only grouping. Malformed numeric syntax is not recovered as a valid
ungrouped member access.

Primitive spellings (`int32`, `float32`, `bool`, `char16`, etc.) remain
ordinary identifier **tokens**, not lexical keywords. Their exact type
bindings are reserved for compiler/runtime-owned primitives: an ordinary
package cannot declare a type with a reserved primitive name, including in
another namespace. This does not reserve all lowercase type names or prevent
ordinary code from using primitive types. The proposed width-explicit integer,
floating, character, vector and decimal families are listed in the
[language design](language.md#primitives-strict-arithmetic-and-text);
the set of reserved bindings follows the primitives actually selected by the
language, not every candidate in this grammar. A reserved or contextual
keyword can be used as an identifier through the narrow `@` escape, as in
`alias @await = Task<int32>;`. The escape changes token interpretation, not
name identity or binding authority. Backtick-delimited and arbitrary-string
identifiers are not source forms. Core does not need an escape to declare
`int32`: primitive names are ordinary identifiers, and escaping a keyword
does not confer the right to define a compiler-owned binding.

**Core bootstrap contract:** A designated Core-library compilation has no
implicit prebuilt standard-library reference and has explicit permission to
declare the reserved primitive types using the ordinary `struct int32`
spelling (and analogously for each supported primitive). Omitting the
standard-library reference alone does not grant that permission. A primitive
declaration attaches source-defined members, conformances and APIs such as
`TryParse` and `TryFormat` to the compiler's existing primitive identity;
calls resolve to their selected library implementations. It does not create
a second type or redefine built-in arithmetic. Its minimal shape requires
exactly one instance backing field of its own primitive type, with no other
instance fields and no source-defined primitive operators. That recursive
field is a privileged intrinsic representation: the compiler/runtime give
the primitive its fixed storage and do not recursively lay out the field as
an ordinary nested struct. Outside the designated Core build, the same
declaration is an error, regardless of spelling or namespace. The Core
artifact identity, full permitted member forms and enforcement across
separate compilation remain open design work.
Each selected language profile must publish a closed reserved-primitive
binding catalog independently of optional Core APIs. Privileged source
members must satisfy ordinary checking and cannot redefine primitive
representation or built-in operations. Candidate width families are not
automatically added to that catalog.

`void` is a distinct keyword; primitive type spellings are not keyword
tokens. The following vocabulary is grouped by source role. Words in the
context table below are contextual; the remaining words in this vocabulary
are reserved. Productions for some words remain open. Listing a word does not
permit it in an arbitrary context.

| Concept | Intended words | Source role |
|---|---|---|
| File imports | `using` | Bring names into scope. |
| Namespace | `namespace` | Declare a file-scoped namespace or namespace blocks, without mixing them in a file. |
| Visibility | `public`, `internal`, `private` | Restrict declaration access. |
| Nominal declarations | `struct`, `class`, `interface`, `enum`, `union` | Name types; union declaration syntax remains open. |
| Type aliases | `alias` | Name a namespace-level transparent type synonym with declaration visibility. |
| Associated types | `type` | Declare an interface requirement or bind it in a conformance. |
| Declaration composition | `partial` | Merge parts of one identity. |
| Extension declarations | `extension` | Declare scoped extensions; exact syntax remains open. |
| External declarations | `extern` | Bind external definitions; exact syntax remains open. |
| Member kinds | `static`, `operator` | Declare static members and operators. |
| Property accessors | `get`, `set`, `init` | Read, write or initialize properties. |
| Property backing | `field` | Access only the enclosing property's backing place. |
| Receiver and self binder | `this` | Refer to the member receiver and mark an interface's implementing-type formal as `<this T>`. |
| Generic constraints | `where` | State formal parameter constraints. |
| Compile-time values | `const` | Require static evaluation or declare a compile-time value formal. |
| Type forms | `void`, `var` | Name a zero-data result or infer a local type. |
| Ownership | `unique`, `own`, `move` | Express ownership and explicit transfer. |
| Reference access | `mut`, `ref`, `write`, `pin` | Qualify places, loans and references; pin qualifies `ref<T>` only. |
| Parameter and argument modes | `init`, `implicit`, `params` | Express vacant initialization, implicit loans or collection arguments; not every combination is legal. |
| Branching | `if`, `else`, `switch` | Choose conditional paths or pattern arms. |
| Default selection | `default` | Select a declared generic default or an interface default body. |
| Witness selection | contextual `via` | Explicitly select an existing interface witness after `.`. |
| Patterns | `when`, `is`, `some`, `and`, `or`, `not` | Match values and guard arms; full pattern grammar remains open. |
| Loops and iteration | `while`, `do`, `for`, `in`, `break`, `continue` | Iterate, including `await for`, and transfer to the nearest loop. |
| Results | `return` | Complete a call. |
| Producers | `yield` | Emit an element from a producer; ordinary `return;` finishes it. |
| Asynchrony | `async`, `await` | Mark an async callable implementation and await a checked completion. |
| Contracts | `require`, `ensure` | Check preconditions or postconditions; `return` names the result within a declaration postcondition. |
| Exception flow | `try`, `catch`, `finally`, `throw` | Handle or propagate failures under their checked transport contracts. |
| Deferred cleanup | `defer` | Register lexical-block-exit actions. |
| Unsafe authorization | `unsafe` | Scope operations that require caller authorization. |
| Policy selection | `policy` | Prefer or require a method variant. |
| Literal values | `true`, `false`, `null` | Form Boolean and optional values. |
| Construction | `new` | Begin ordinary construction. |

Contextual words are identifiers outside their designated grammatical
positions. Recognition is syntactic, not dependent on a library declaration
or the outcome of overload resolution. Escaping a contextual word suppresses
that recognition even in its designated position.

| Contextual spelling | Keyword position |
|---|---|
| `type` | Associated-type requirement or binding introducer; the `type` attribute target. |
| `field` | Property-local backing-place expression; the `field` attribute target. |
| `const` | Compile-time value declaration or generic value formal introducer; required-constant expression prefix. |
| `policy` | Policy-selection scope after its optional `require` marker. |
| `via` | Witness selection after `.` with its interface argument list. |
| `some` | Optional-presence pattern introducer. |
| `when` | Pattern-arm guard introducer. |
| `and`, `or`, `not` | Pattern combinators, not ordinary Boolean expression operators. |

Attribute-target spellings such as `module`, `assembly`, `property`,
`method`, `param` and `typeparam` likewise have meaning only in their target
positions; they are not additional reserved keywords. Reserved `return`
also has the designated result-reference meaning in a postcondition; that
does not make it an ordinary identifier elsewhere.

Future additions must consider both ordinary name use and ambiguities in
real source. A new reservation or contextual recognition that changes the
meaning of previously valid source requires a source-language version
boundary, not silent reclassification within that source version. Exact
source-version selection is a separate tooling contract.

`_` is a distinct nonbinding token, not an identifier. Vacant initialization
uses `init ref<T>`; arithmetic selection uses policy cases rather than a
separate arithmetic-toggle source form.

<a id="3-candidate-grammar-productions-proposed"></a>

## 3. Grammar productions

The productions below are subject to their stated semantic restrictions and
explicit open boundaries. `Identifier`
means one lexical identifier token; dotted names are written explicitly.
Semantic constraints are stated separately.
The prefix names below are metasyntactic placeholders for only the
attributes/modifiers valid in each declaration context. Legal prefix
modifiers may be permuted; duplicate or conflicting modifiers reject.

### 3.1 Source-file structure and declarations

```ebnf
CompilationUnit  = { FileAttribute }, { UsingDirective },
                   ( FileNamespace, { UsingDirective }, { TopDeclaration }
                   | { TopDeclaration | NamespaceDecl } ), EOF ;
FileAttribute    = "[", ( "module" | "assembly" ), ":", AttributeName,
                   [ AttributeArguments ], "]" ;
UsingDirective  = "using", DottedIdentifier, ";" ;
FileNamespace   = "namespace", DottedIdentifier, ";" ;
NamespaceDecl   = "namespace", DottedIdentifier, "{", NamespaceBody, "}" ;
NamespaceBody   = { UsingDirective }, { TopDeclaration | NamespaceDecl } ;
DottedIdentifier = Identifier, { ".", Identifier } ;

TopDeclaration = TopPrefix, ( StructDecl | ClassDecl | InterfaceDecl
                              | EnumDecl | FunctionDefinition )
               | AliasPrefix, AliasDecl ;
MemberDeclaration = MemberPrefix,
                    ( ConstructorDecl | FunctionDefinition | OperatorDefinition
                    | ConversionDefinition
                    | FieldDecl | PropertyDefinition | IndexerDefinition
                    | AssociatedTypeBinding
                    | AssociatedConstantBinding ) ;
InterfaceMemberDeclaration = InterfaceMemberPrefix,
                             ( InterfaceFunction | InterfaceOperator
                             | InterfaceProperty | InterfaceIndexer
                             | AssociatedTypeRequirement
                             | InterfaceConstant ) ;
TopPrefix       = ContextualPrefix ;
MemberPrefix    = ContextualPrefix ;
InterfaceMemberPrefix = ContextualPrefix ;
AliasPrefix     = { Attribute | NamespaceVisibilityModifier } ;
Attribute       = "[", [ AttributeTarget, ":" ], AttributeName,
                  [ AttributeArguments ], "]" ;
AttributeTarget = SingletonTarget
                | NamedTarget, [ "(", Identifier, ")" ] ;
SingletonTarget = "type" | "get" | "set" | "init" | "return" ;
NamedTarget     = "field" | "property" | "method" | "param" | "typeparam" ;
AttributeName   = DottedIdentifier ;
AttributeArguments = "(", [ ConstantExpression,
                     { ",", ConstantExpression } ], ")" ;
VisibilityModifier = "public" | "internal" | "private" ;
NamespaceVisibilityModifier = "public" | "internal" ;
DeclarationModifier = "public" | "internal" | "private" | "static"
                    | "mut" | "unique" | "unsafe" | "partial" | "async" ;

StructDecl      = "struct", Identifier, [ TypeParameterList ],
                  [ ConformanceList ], { ConstraintClause },
                  "{", { MemberDeclaration }, "}" ;
ClassDecl       = "class", Identifier, [ TypeParameterList ],
                  [ ConformanceList ], { ConstraintClause },
                  "{", { MemberDeclaration }, "}" ;
InterfaceDecl   = "interface", Identifier, [ InterfaceTypeParameters ],
                  [ InterfaceBaseList ], { ConstraintClause },
                  "{", { InterfaceMemberDeclaration }, "}" ;
EnumDecl        = "enum", Identifier, ":", IntegralType,
                  "{", [ EnumMember, { ",", EnumMember }, [ "," ] ], "}" ;
EnumMember      = Identifier, [ "=", ConstantExpression ] ;
AliasDecl       = "alias", Identifier, [ TypeParameterList ], "=", Type, ";" ;
AssociatedTypeRequirement = "type", Identifier, [ "=", Type ], ";" ;
AssociatedTypeBinding = "type", MemberName, "=", Type, ";" ;
InterfaceConstant = "const", Type, Identifier,
                    [ "=", ConstantExpression ], ";" ;
AssociatedConstantBinding = "const", Type, MemberName, "=",
                            ConstantExpression, ";" ;
ConformanceList = ":", Type, { ",", Type } ;
InterfaceBaseList = ":", Type, { ",", Type } ;
IntegralType    = DottedIdentifier ;
ConstantExpression = Expression ;
MemberName      = Identifier | Type, ".", Identifier ;
ConstructorDecl = Identifier, ParameterList, ContractClauses, CallableBody ;
ContractClauses = { PreconditionClause }, { PostconditionClause } ;
PreconditionClause = "require", ContractPredicate ;
PostconditionClause = "ensure", ContractPredicate ;
ContractPredicate = Expression ;
ReturnValueReference = "return" ;
FunctionDefinition = Type, MemberName, [ TypeParameterList ], ParameterList,
                     { ConstraintClause }, ContractClauses,
                     ( CallableBody | ";" ) ;
InterfaceFunction = FunctionDefinition ;
OperatorDefinition = Type, "operator", OperatorSymbol, ParameterList,
                     ContractClauses, CallableBody ;
ConversionDefinition = "operator", Type, "(", Type, Identifier, ")",
                       ContractClauses, CallableBody ;
InterfaceOperator = Type, "operator", OperatorSymbol, ParameterList,
                    ContractClauses, ( ";" | CallableBody ) ;
CallableBody    = Block | ExpressionBody ;
ExpressionBody  = "=>", ( ExpressionStatement | AssignmentStatement
                       | DiscardStatement | RequireStatement | ThrowStatement ) ;
FieldDecl       = Type, Identifier, [ "=", Expression ], ";" ;
PropertyDefinition = Type, MemberName,
                     ( "{", GetterDefinition,
                       [ SetterDefinition | InitDefinition ], "}"
                     | ContractClauses, ExpressionBody ) ;
AccessorPrefix  = { Attribute | "mut" } ;
GetterDefinition = AccessorPrefix, "get", ContractClauses, ( CallableBody | ";" ) ;
SetterDefinition = AccessorPrefix, "set", ContractClauses, ( CallableBody | ";" ) ;
InitDefinition   = { Attribute }, "init", ContractClauses, ( CallableBody | ";" ) ;
InterfaceProperty = Type, Identifier, "{", InterfaceGetter,
                    [ InterfaceSetter | InterfaceInit ], "}" ;
InterfaceGetter = GetterDefinition ;
InterfaceSetter = SetterDefinition ;
InterfaceInit   = InitDefinition ;
IndexerDefinition = Type, [ Type, "." ], "this", IndexParameters,
                    ( "{", IndexGetter, [ IndexSetter | IndexInit ], "}"
                    | ContractClauses, ExpressionBody ) ;
IndexParameters = "[", Parameter, { ",", Parameter }, "]" ;
IndexGetter     = AccessorPrefix, "get", ContractClauses, CallableBody ;
IndexSetter     = AccessorPrefix, "set", ContractClauses, CallableBody ;
IndexInit       = { Attribute }, "init", ContractClauses, CallableBody ;
InterfaceIndexer = Type, "this", IndexParameters,
                   "{", InterfaceGetter, [ InterfaceSetter | InterfaceInit ], "}" ;

TypeParameterList = "<", GenericFormal, { ",", GenericFormal }, ">" ;
GenericFormal   = { Attribute }, ( Identifier, [ "=", Type ]
                | "const", Type, Identifier, [ "=", ConstantExpression ] ) ;
InterfaceTypeParameters = "<", ( { Attribute }, "this", Identifier
                                | GenericFormal ),
                          { ",", GenericFormal }, ">" ;
ConstraintClause  = ConformanceConstraint | AssociatedIdentityConstraint ;
ConformanceConstraint = "where", Identifier, ":",
                        ( Type | "[", Identifier, "]" ),
                        { ",", ( Type | "[", Identifier, "]" ) } ;
AssociatedIdentityConstraint = "where", AssociatedProjection, "is", Type ;
AssociatedProjection = ( "void" | DottedIdentifier ), [ TypeArguments ],
                       ".", "via", "<", Type, ">", ".", Identifier ;
OperatorSymbol  = "+" | "-" | "*" | "/" | "%" | "&" | "|" | "^"
                | "<<" | ">>" | ">>>"
                | "==" | "!=" | "<" | "<=" | ">" | ">=" | "!" | "~"
                | "++" | "--" | "true" | "false" ;
```

The source file permits one file-scoped namespace or namespace blocks, without
mixing those forms in a file. Namespace blocks may coexist with root
declarations and contain nested namespace blocks. A nested qualified name
extends its enclosing namespace: `namespace A { namespace B.C { ... } }`
declares `A.B.C`. A file-scoped namespace cannot contain namespace blocks.
Imports precede declarations in their own scope. File imports apply to that
file; imports after a file-scoped namespace apply to its remaining body.
An import in a namespace block applies to that lexical block and its nested
blocks, not other blocks of the same namespace or other files. Namespace
identity does not merge import scopes. Declarations retain source order within
compiler file order.

Namespace/file-level declarations permit `public` and `internal`, not
`private`. `internal` grants access within the declaring module; `private`
is a declaring-type member access domain within that module. Another module
declaring the same namespace does not extend either access domain, and
linking or unsafe authorization does not grant friend-module access.
File-limited visibility is not a selected source form.

Fields and constructors occur in class/struct bodies; interface members do
not include fields or constructors. An interface
constant with an initializer is fixed by its declaration; one without an
initializer requires an implementer-supplied `const` binding of that selected
member identity. An unrelated same-named constant does not satisfy it.
The `ContextualPrefix` placeholder still requires context-specific
validation; permutation does not make every modifier legal on every
declaration. Repeated visibility and conflicting effects reject. Callable
contract clauses follow the parameter list and any generic constraints;
return types remain before the callable name, consistently for functions
and operators.
`<this T>` marks the single implementing-type formal of an interface;
ordinary interfaces need not declare one. A conformance binds it to the
implementing nominal type, not an arbitrary argument. Other formals follow
it; duplicate self formals reject.
`AliasDecl` occurs only at namespace/file scope with the same visibility
choices as other top-level declarations, not as a nominal member.
File attributes precede imports. A `module` target binds module metadata;
an `assembly` target is legal only if the build defines an assembly identity.
Targeted declaration attributes must refer to an existing applicable target;
in particular `[get: Name]` on a property targets its getter, not the whole
property. Neither unknown nor misplaced targets silently fall back to the
declaration. Attribute arguments here are a syntactic approximation;
constant-value eligibility and multiplicity require semantic validation.
Each resolved attribute identity declares its argument schema, applicable
targets and repetition policy. Arguments use the ordinary checked, encodable
compile-time constant evaluator, not a separate attribute evaluator or
arbitrary runtime objects. A constant must also match its declared argument
type and encoding. Type-reference arguments require an explicitly supported
schema; this does not introduce `typeof`, open-generic type literals or a
first-class type-object API. Repeated placements merge in source order only
where that identity permits repetition; otherwise they reject. Registration
syntax, concrete schema catalogs and metadata encoding remain separate work.
Compiler-recognized semantic attributes obey the same resolved-identity and
schema rules. Their library definitions provide argument names, types,
defaults and applicable targets; the compiler checks the registered schema
and maps its checked values to the designated semantic metadata shape.
Neither arbitrary same-named attributes nor a changed library schema can
redefine that mapping. Layout controls distinguish layout kind, member-packing
limit and aggregate alignment. Their schema may expose these separate
controls together without adding a layout-specific argument grammar or
choosing a universal positional overload here. Unsupported target
combinations diagnose; custom controls on `Transparent` remain invalid.
Packing or alignment alone does not grant `Blittable` or freeze a layout.
Attributes may attach directly to their target or identify a named target on
the enclosing declaration: `[param(count): Attr]` and `[typeparam(T): Attr]`
bind the same formal identities as inline attributes. A selector must resolve
exactly one applicable target, and placements share multiplicity and partial
merge rules. An untargeted callable attribute applies to the callable, not
its result; result attributes use `[return: Attr]`. No selector performs
overload guessing.
`[Iterator]` is a compiler-recognized semantic attribute selecting a producer
implementation on a callable with a body and an eligible declared producer
result contract. Recognition follows the resolved attribute identity, not
arbitrary same-named library attributes. It introduces no `iterator` keyword
and does not infer a result type, item type or stronger lifetime contract
from the body. Producer statements and remaining protocol boundaries are
described in §5.12.
Core's privileged primitive declaration uses `StructDecl` and `FieldDecl`
without a separate grammar production; its binding and representation
restrictions are semantic checks, not lexical exceptions.

Declaration contracts group zero or more `require` clauses before zero or
more `ensure` clauses, with no statement semicolon between clauses and the
body. Preconditions and postconditions execute in declaration order within
their own established phases, not interleaved source phases.
A predicate ends at an unparenthesized top-level `=>` that introduces the
callable's expression body. Lambdas within a contract must be grouped or
nested inside another expression delimiter, such as a call argument list;
the parser does not choose between a lambda and a body by testing type
validity. Ordinary parentheses around the whole predicate are permitted,
not required.
Functions, constructors, operators and conversions share this clause form.
Accessor clauses follow `get`, `set` or `init`; a getter-only expression-bodied
property/indexer places its getter clauses before `=>`. A full accessor list
puts contracts on the individual accessors, not an ambiguous property-wide
call phase. Bodyless interface requirements end in their ordinary `;` after
the clauses. Automatic property accessors still perform their declared
checks around their generated operation; contracts do not create implicit
indexer storage. Lambdas use the same grouping after their parameters and
before their body arrow, subject to the target contract rules in §3.4.
Body-level `require expression;` is an authored check, not a public
precondition inferred from the body. Published clauses obey ordinary
effective-visibility, access, ownership, lifetime and effect restrictions.

Top-level and concrete function/operator definitions have bodies unless
explicitly marked `partial`; a bodyless partial function must have exactly
one matching implementation in the compilation. A concrete property accessor
ending in `;` uses checked compiler-generated backing storage; an explicit
accessor needs a body. Interface members may end in `;` to require a
conformance implementation. A body on an
interface member supplies a default. For each inherited interface member, a
checked nominal conformance maps its declaring-interface member identity to a
compatible explicit implementation when present, otherwise to its default
body; if neither exists, the conformance is incomplete. An explicit
implementation may replace a default without changing the member identity or
weakening its published contract. Same-named extensions do not satisfy or
replace a witness.
These are interface contract and witness rules, not concrete inheritance (see
[§5.12](#512-modifiers-cleanup-and-finalization)).

Operators belong to the declaring nominal type or an interface contract,
**not** a global declaration. Retroactive behavior for another type needs an
explicit adapter or wrapper witness, not a global extension operator. A
conversion declaration `operator T(U source)` is a separate nominal member
form with result `T` and one by-value `U` input, invoked explicitly as
`T(source)`. It requires `static` and has no separate return-type prefix or
conversion keyword. `new T(...)` selects construction separately from
conversion type-calls. Destination-first conversion eligibility and explicit
owner selection are specified in §5.13; declaration order cannot break ties.
A bodyless static interface operator is a required compile-time witness; a
bodyful one supplies a default. Static operator witness selection needs no
runtime vtable. The generic type parameters and conformance lists
apply to structs **and** classes; interface base lists express interface
composition, not implementation of an interface by itself.
The integral enum base and constant-valued members preserve unnamed
underlying values; `[Flags]` is an optional declaration contract, not a
different closed enum grammar. Enum members are compile-time constants checked
in the exact eligible underlying integer type. An implicit successor that
overflows that type rejects; deliberate duplicate-valued names are permitted.
`[Flags]` annotation checks remain separate from runtime value validity and
do not forbid unnamed underlying values. String/type-valued cases are not an
alternative enum base selected by this rule. Exact eligible integer catalogs
and flags annotation schemas must be declared by the selected profile.
`AliasDecl`
names the target type without defining a new nominal identity or adding
conformance; generic aliases substitute their type formals into the target.
Its namespace-level placement and visibility are deliberate; aliases are
not nested in nominal types. Associated
`type Name;` declares an interface requirement; `type Name = Type;` in an
implementing nominal type binds a selected requirement rather than declaring
an arbitrary nested nominal type. Witness mapping and metadata remain separate
checked contracts; finite alias expansion is specified in §5.4.
Named properties and authored `this[...]` indexers use accessor-local effects.
A `set` or `init` body receives a typed `value` parameter. `mut get` and
`mut set` declare writable receiver requirements when necessary; plain
accessors may write through an independently writable referent without
mutating the receiver. `init` alone grants initialization-phase mutability;
there is no `mut init` form. An auto-setter that writes receiver backing
storage requires `mut set`. An indexer has no implicit per-index backing
`field`; concrete indexer accessors require bodies. Interface accessor
semicolons instead declare implementation requirements.
An expression body returns its expression for value-producing members and
executes one legal statement for void methods, setters, init accessors and
constructors. It is shorthand for the corresponding checked block, not a
general assignment-expression feature. Constructors name their enclosing
type and must still establish their published completion guarantees.
Nested type declarations are deliberately deferred from this
grammar; this is a scope boundary, not a claim that nested type
arguments in `Type` are excluded. See [§5.11](#511-nested-types-and-scoped-operators).

```terrum
// Informative interface example.
interface IAdd<T>
{
    static T operator +(T left, T right);
    int32 Size();
    int32 DefaultSize() { return 0; }
}
```

### 3.2 Types, parameters, and arguments

```ebnf
Type            = [ "write" ], [ "pin" ], ( NamedType | TupleType ), [ "?" ] ;
NamedType       = ( "void" | DottedIdentifier ), [ TypeArguments ],
                  [ ".", "via", "<", Type, ">", ".", Identifier ] ;
TupleType       = "(", TupleTypeElement, ",", TupleTypeElement,
                  { ",", TupleTypeElement }, ")" ;
TupleTypeElement = Type, [ Identifier ] ;
ParameterList   = "(", [ Parameter, { ",", Parameter } ], ")" ;
Parameter       = { Attribute }, ParameterBinding ;
ParameterBinding = [ PassingMode ], Type, Identifier, [ "=", ConstantExpression ]
                 | "init", "ref", "<", Type, ">", Identifier
                 | "implicit", "ref", "<", Type, ">", Identifier
                 | "params", Type, Identifier ;
PassingMode     = "ref" | "mut", "ref" ;

Arguments       = "(", [ Argument, { ",", Argument } ], ")" ;
Argument        = [ Identifier, ":" ],
                  ( [ PassingMode ], Expression | "init", Expression ) ;
```

Generic argument lists and declaration parameter lists attach their opening
`<` directly to the preceding name. Use `List<T>` and `F<T>(x)`, not
`List <T>` or `F <T>(x)`. A constructed generic type is one type expression,
not one lexical token. Interior trivia remains optional, including comments
and line breaks before/after arguments and before the closing delimiter:

```terrum
List<
    T,
    U
>
```

Argument count and formal kinds still have to match the selected declaration.
Nested generic closing delimiters retain their type-context recognition, so
`List<List<T>>` does not require separating its closers. Comparison `<` and
`>` remain spaced binary expression operators, not generic delimiters.

Direct constant generic arguments are literals or qualified constant names;
other constant expressions require grouping. Use `InlineArray<T, N>`,
`InlineArray<T, (N >> 1)>` and
`InlineArray<T, (N >= Minimum ? N : Minimum)>`.
A negative argument currently requires `(-1)`, because the sign is an
operator rather than part of the literal; an ungrouped signed-argument
exception is not adopted. This boundary does not restrict ordinary default
expressions or interpolation conditionals. The selected formal still checks
its eligible constant domain: syntax for a literal does not admit strings,
nulls or other values into the initial integer/Boolean/nominal-enum domains.

Designated keyword-introduced angle lists use the same opener attachment
and flexible interior trivia: `default<I>`, `.via<I>`,
`policy<Domain.Case>` and `init ref<T>` require `<` to adjoin its
introducer. `default <I>`, `.via <I>`, `policy <Domain.Case>` and
`init ref <T>` reject. This applies to the reference-type/loan constructor
`ref<T>`, including its separately worded `init` and `implicit` forms.
Separate words retain their normal boundaries: `init ref<T>` is not
`initref<T>`. Attachment does not change witness selection, policy
selection, initialization or reference authority.

The optional-type suffix `?` attaches directly to the type's final token:
`T?`, `(T, U)?` and `List<T>?` are valid forms, but `T ?` and
`List<T> ?` reject. In type context, optional suffixes may adjoin generic
closing delimiters on either side, as in `List<T?>`, `List<T>?` and
`List<List<T?>>`. These are explicit grammatical type boundaries, not
general expression-operator spellings or permission to split arbitrary
operator runs. Generic interior trivia remains optional, but may not
separate a suffix from the type it qualifies.
Repeated optional markers such as `T??` are not a valid source form.
Optional normalization such as `T?` with an already-optional substitution is a
separate semantic rule. In these productions, `mut` in a parameter is a
passing mode (`mut ref`), not a type argument. Grammar does not itself grant a
particular lifetime, write authority, representation conversion, or generic
variance.

Generic type and `const Domain P` value formals may have declared defaults,
and defaulted and required formals may interleave. Explicit arguments pin
their positions, typed arguments establish types, expected-result context
fills unresolved positions, then declared defaults fill omitted unresolved
positions before contextual literals/lambdas are checked. An explicit `_`
requires inference and cannot fall back to a default. `default` explicitly
selects one formal's declared default and rejects if none exists.
Omitting a suffix or the whole list may use defaults; an empty comma slot
such as `Make<, int32>()` is invalid. Defaults must satisfy the formal's
constraints; they do not rescue contradictory or ambiguous inference.
The initial constant-formal domains are fixed-width integers, Boolean and
nominal enums, plus explicitly opted-in eligible one-field wrappers over an
already eligible integer/Boolean carrier.
Floating values, arbitrary aggregates, references and allocated object
identities do not become eligible merely by being constructible.
Transparent aliases expand to their qualified target before eligibility
checking; an alias does not create a new value domain. A nominal wrapper
retains its distinct identity and invariants. Its checked constant-domain
contract requires constant construction, complete initialization and
canonical equality/value encoding, not just equal layout or a
`Transparent`/`Blittable` annotation.

Wrapper eligibility is a published capability under the existing
[contract-versioning rules](language-contracts.md#45-versioning-and-independent-implementations),
not an observation of today's private fields or constructor body.
Its validity, canonical value identity and any separately promised interop
representation must remain compatible for independently compiled consumers.
Removing eligibility or incompatibly changing those promises is breaking;
an unpromised private implementation change is not automatically breaking.
Neither an adapter nor a nominal wrapper becomes a transparent alias.
Required construction checks still execute in constant evaluation; a
negative input to a nonnegative wrapper rejects rather than manufacturing
a value from equal-width bits. Ordinary binary consumers rely on the
published contract, while evaluating a new constant still requires the
eligible body/dependencies under the separate constant-evaluation contract.
Exact opt-in spelling and value encoding remain open. Attribute-encoded
constructed data, such as a constructor recipe for a decimal-like value,
is a separate undecided facility, not implied by this wrapper subset.
A generic default may depend only on earlier formals. Self, forward and
cyclic dependencies reject; interleaving required/defaulted formals does
not change that direction. Defaults still satisfy the selected domain,
constraints and complete qualification.

Ordinary defaults are checked, publishable compile-time `Copy` values,
including eligible nominal aggregates, not just primitive/enum values.
An omitted value is selected at caller source compilation; changing a
provider's default does not rewrite unchanged compiled calls. Reference and
initialization loans have no defaults from this rule; owning and object-
identity defaults need separate contracts. Required and defaulted parameters
may interleave, but `params` must be final and is an ordinary collection
parameter, not a new by-reference mode or type pack.

Named arguments may reorder binding but supplied expressions always evaluate
left-to-right in source order. Unknown/duplicate labels and unsupplied
required parameters reject. A positional argument may follow an in-position
named argument; after an out-of-position label, subsequent arguments must be
named. There is no search for the next unfilled parameter and no empty slot.
Overload selection and output behavior remain checked semantic contracts. The
`init ref<T>` is a distinct initialization-loan parameter form, with the
accepted call-site spelling `Create(init value)`. `write` qualifies access
through a reference and `pin` qualifies only `ref<T>` (for example,
`write pin ref<T>`); other uses of `pin` in this broad `Type` production
are invalid. `pin ref<T>` keeps its backing rooted and address-stable for the
pin-loan lifetime. The initialized local binding `pin ref<T> name = ref place;`
explicitly and checkably acquires a pin from eligible initialized, addressable
backing, preserving the source's access and root/owner dependencies. This is
not a qualifier-only conversion or retyping of an ordinary reference. The pin
lasts for the binding's promised local lifetime; release or rebinding must
satisfy its dependency checks, and a derived address, copy, capture or loan
that still depends on the pin prevents premature release. An ordinary
`ref<T>` reborrow does not retain the pin guarantee; it retains only its
ordinary backing/root lifetime dependency. A raw `ptr<T>` is a
non-relocatable address and does not root or pin GC storage; forming one into
GC storage requires a live pin with enforced provenance/lifetime or
independently stable storage.
Concrete release/null-reference spelling, safe pointer conversion and
dereference/member syntax remain in
[Q26.1](proposals/grammar-open-questions.md#q261-pointer-formation-and-dereferencemember-boundaries).
Pinning a slot containing a class handle does not also pin the separately
allocated object reached through that handle. `mut` qualifies
a binding/receiver or a `mut ref` mode.
Neither `write` nor `init` is an unrestricted declaration modifier. Exact
`write ref<T>` legality in `Parameter` and the other target parameter forms
remain open, and this bounded production must not be used to reject the
accepted forms.

### 3.3 Statements and local declarations

```ebnf
Block           = "{", { Statement }, "}" ;
Statement       = Block | IfStatement | LoopStatement | SwitchStatement
                | RequireStatement | TryStatement | UnsafeStatement
                | DeferStatement | PolicyStatement
                | ReturnStatement | YieldStatement | ThrowStatement | LoopTransfer
                | SwitchTransfer | LocalDecl | DeconstructionDecl
                | DiscardStatement | AssignmentStatement
                | ExpressionStatement | ";" ;
IfStatement     = "if", "(", Expression, ")", Block,
                  [ "else", ( Block | IfStatement ) ] ;
WhileStatement  = "while", "(", Expression, ")", Block ;
DoStatement     = "do", Block, "while", "(", Expression, ")", ";" ;
LoopStatement   = [ Identifier, ":" ],
                  ( WhileStatement | DoStatement | ForStatement | ForEachStatement
                  | AsyncForEachStatement ) ;
ForStatement    = "for", "(", [ ForInit ], ";", [ Expression ], ";",
                  [ ForStep ], ")", Block ;
ForInit         = LocalBinding | StepExpression ;
ForStep         = StepExpression ;
StepExpression  = Expression | Assignment ;
ForEachStatement = "for", "(", [ "mut" | "ref" | "mut", "ref" ],
                   ( "var" | Type ), Identifier,
                   "in", Expression, ")", Block ;
AsyncForEachStatement = "await", ForEachStatement ;
RequireStatement = "require", Expression, ";" ;
TryStatement    = "try", Block, ( "finally", Block
                  | CatchClause, { CatchClause }, [ "finally", Block ] ) ;
CatchClause     = "catch", [ "(", Type, ( Identifier | "_" ), ")",
                            [ "when", "(", Expression, ")" ] ], Block ;
UnsafeStatement = "unsafe", Block ;
DeferStatement  = "defer", ( ExpressionStatement | Block ) ;
PolicyStatement = [ "require" ], "policy", PolicyCases, Block ;
ReturnStatement = "return", [ Expression ], ";" ;
YieldStatement  = "yield", Expression, ";" ;
ThrowStatement  = "throw", [ Expression ], ";" ;
LoopTransfer    = ( "break" | "continue" ), [ Identifier ], ";" ;
SwitchTransfer  = "continue",
                  ( CaseLiteral | "_" | "(", ConstantExpression, ")" ), ";" ;
CaseLiteral     = [ "+" | "-" ], ( DecimalDigits | HexInteger | BinaryInteger ) ;
LocalDecl       = LocalBinding, ";" | VacantLocal ;
LocalBinding    = [ "mut" ], ( "var" | Type ), Identifier, "=", Expression ;
VacantLocal     = "init", ( "var" | Type ), Identifier, ";" ;
DiscardStatement = "_", "=", Expression, ";" ;
AssignmentStatement = Assignment, ";" ;
Assignment      = Expression, AssignmentOperator, Expression
                | DeconstructionPlaces, "=", Expression ;
AssignmentOperator = "=" | "+=" | "-=" | "*=" | "/=" | "%="
                   | "&=" | "|=" | "^=" | "<<=" | ">>=" | ">>>=" | "??=" ;
ExpressionStatement = Expression, ";" ;
DeconstructionDecl = "var", DeconstructionBindings, "=", Expression, ";"
                    | TypedDeconstruction, "=", Expression, ";" ;
DeconstructionBindings = "(", BindingTarget, ",", BindingTarget,
                         { ",", BindingTarget }, ")" ;
BindingTarget   = Identifier | "_" | DeconstructionBindings ;
TypedDeconstruction = "(", TypedBinding, ",", TypedBinding,
                      { ",", TypedBinding }, ")" ;
TypedBinding    = Type, Identifier | "_" | TypedDeconstruction ;
DeconstructionPlaces = "(", PlaceTarget, ",", PlaceTarget,
                       { ",", PlaceTarget }, ")" ;
PlaceTarget     = Expression | "_" | DeconstructionPlaces ;

SwitchStatement = "switch", "(", Expression, ")",
                  "{", [ StatementArm, { ",", StatementArm }, [ "," ] ], "}" ;
StatementArm    = ArmHeader, "=>", Block ;
SwitchExpression = "switch", "(", Expression, ")",
                   "{", [ ValueArm, { ",", ValueArm }, [ "," ] ], "}" ;
ValueArm        = ArmHeader, "=>", Expression ;
ArmHeader       = { Attribute }, Pattern, [ "when", "(", Expression, ")" ] ;
Pattern         = PatternOperand, { PatternOperator, PatternOperand } ;
PatternOperand  = { "not" }, PatternAtom ;
PatternOperator = "and" | "or" ;
PatternAtom     = "_" | "null" | "some", ( Identifier | "_" )
                | ConstantExpression | "(", Pattern, ")" ;
```

`for` initializers and iterators omit their internal semicolon; an iterator
may be an expression or one `=` assignment. Ordinary local bindings require an
initializer. `init var name;` instead declares vacant storage whose type is
established by a checked initialization; `init T name;` makes its
type explicit. Neither form is a default value, and neither permits reads
before complete publication. Bare `T name;` is not a declaration here.
By-value iteration is the default. `ref` iteration borrows an element place
read-only and `mut ref` requires a writable element loan from the iterator;
neither silently copies a value or upgrades a readonly source. The declared
iteration protocol determines element access, loan extent,
backing dependencies and advance invalidation. Consumption or writable
borrowing requires an explicit eligible contract; an iteration-local view
does not become an independently owning value. Outstanding loans constrain
advancement, capture, suspension and close. Concrete operation identities and
consuming-binding spelling remain specification work; these spellings do not
grant escaping loans or overlapping mutation.
`await for (... in ...)` selects an eligible checked async iteration protocol
inside an async implementation. The source expression evaluates once, and
each advancement uses that protocol's awaitable completion. It does not
implicitly await yielded elements: an async sequence of task values still
yields task values. Awaiting each task in an ordinary synchronous sequence
instead uses a normal `for (... in ...)` with an explicit `await` in its body.
`await` is not an element-binding modifier, and `for (await ... in ...)` is
not an alternate form. Reference modes retain their ordinary access checks,
with suspension preserving backing, loan and affinity obligations. Iterator
acquisition, close and cancellation protocol identities remain library design;
the spelling introduces neither implicit blocking nor executor capture.
An iteration loop closes its successfully acquired iterator according to
the declared protocol on exhaustion, `break`, `return`, or failure, including
an advancement failure. Acquisition failure instead uses ordinary partial
initialization cleanup. Closing the iterator does not by itself dispose the
source collection. The loop performs its required close operation once;
completed producer cleanup cannot be rerun by close.
`await for` awaits a required asynchronous close before completing the exit.
This is a checked protocol operation, not permission for generated owner
destruction to suspend. Backing, ownership, loans and affinity remain valid
through close completion; a cancellation request alone does not discharge
them. Close failures use ordinary cleanup/failure chaining without silent
suppression.
`do { ... } while (condition);` executes its body before the first condition
check. Normal body completion and `continue` proceed to the condition;
`break` exits the loop. Named-loop transfers and ordinary scope cleanup
apply. The condition uses the ordinary checked Boolean contract, and body
locals do not escape their block into the condition.
Loop labels occupy a namespace separate from value declarations. Duplicate
normalized labels on simultaneously enclosing loops reject; disjoint loop
scopes may reuse a label. A loop has at most one label, whose colon attaches
to its name and whose loop introducer remains on the same physical source
line. `retry: while (...) { ... }` is valid; a line break after `retry:`
and `a: b: while (...) { ... }` are invalid. Labels name enclosing loops
only, not arbitrary statements or future `goto` destinations. For async
iteration, the label attaches to the loop's `await for` introducer.

Blocks are required after `if`, `while`, `do`, `for`, `try`, `catch`,
`finally`, and `unsafe`; a single unbraced statement is not part of this
grammar. Assignment is a
statement-level form, not a general right-associative expression; its left
expression must pass a writable-place check, which this syntactic
approximation does not define. `DiscardStatement` is only the standalone
discard form: `_` here is not an identifier or a pattern wildcard. In a
pattern it is a separate nonbinding wildcard; it is not an absence literal.
Recovery forms are not alternate accepted syntax.

Tuple deconstruction is structural, including nested tuples and `_`
discards; it does not discover a nominal `Deconstruct` method by name.
Declarations evaluate the right-hand side once. Assignment evaluates
destination places once left-to-right, then the right-hand side before any
write, then writes left-to-right. Ordinary access, alias, copy/move, and
cleanup checks apply; a later failure does not roll back completed writes.

Switch statements and expressions share pattern arms. The scrutinee evaluates
once and arms are considered in order; a guard runs only after its pattern
matches. Guards are ordinary checked Boolean code with preserved effects
and failures, not purity/NoThrow-only expressions. Both switch forms require
coverage or an unguarded wildcard fallback. Normal completion of an arm exits
the switch, with no implicit fallthrough or switch-specific `break`.
`continue N;` enters an existing unguarded constant arm in the same statement
switch directly without rematching; the target must have no pattern bindings.
Named or compound constant targets use `continue (N);`, while
`continue label;` denotes a loop label and bare `continue;` remains loop-only.
`continue _;` targets the unguarded wildcard. Forward and backward targets
are legal, cleanup runs when leaving an arm, and these transfers cannot
escape a cleanup action. Expression switches have no cross-arm transfer.
A source switch does not guarantee a jump table; a target-checked request
remains a possible separate design.

Pattern combinators use precedence `not`, then `and`, then `or`.
An `or` pattern may expose bindings only when all alternatives establish
the same names with the same complete type, ownership, access and lifetime
contracts. Full nominal-case, relational and destructuring patterns remain
to specify beyond the bounded forms above.
An ordinary wildcard suppresses missing-known-case warnings.
`[Exhaustive] _ => ...` retains a fallback while requesting those warnings;
guarded arms do not establish coverage merely by naming a case. The compiler
also warns when a known case reaches a fallback proved to have no normal
completion using a verified `DoesNotReturn` contract, not a helper-name guess.
Nonreturning does not imply always throwing. Authored fallback code, including
a throwing `unreachable()` helper, remains executable unless soundly proved
unreachable under all relevant inputs and replacement boundaries. Neither
undefined behavior nor a diagnostic authorizes changing source intent.

Typed catches bind a declared failure type and a name or `_`; bare `catch`
handles contract-catchable failures without a universal payload object.
`when` filters are checked Boolean code: false continues handler selection,
and a filter failure follows ordinary failure/chaining rules rather than
silently becoming false. Handlers test declared catchable contracts in source
order. A matched filter has checked access to the failure payload at its
declared filter phase; it cannot depend on invalid references into already
unwound frames. Catchability identity/registration and exact filter/unwind
timing remain in
[Q32.1](proposals/grammar-open-questions.md#q321-catchability-registration-and-filter-phase);
failure transport remains separate design work.
Bare `throw;` rethrows the
active handled failure; it is invalid outside a handler or in a separately
invoked lambda merely declared there. `throw expression` evaluates its
operand once and has no normal result; the operand must satisfy an eligible
failure contract.

Explicit checked capture is required to retain provenance for propagation
after the catch. `throw captured;` uses that captured-failure contract,
without special `throw<preserve>` syntax. Captured state cannot retain
invalid references into unwound frames or rerun prior cleanup. Propagation
is repeatable only under a checked sharing contract; unique state is
consumed instead. Original provenance is preserved without destructive
updates shared by multiple observers. Capture APIs, representation,
ownership metadata, and cause/transport rules remain open.

### 3.4 Expressions and precedence

The grammar uses the precedence table to parse an operand, then
repeated binary operators and operands. For example, `a && b || c` is
`(a && b) || c`; there is no need to read a chain of one production per
operator level. Ordinary binary operators at the same level associate left;
`??` and `?:` associate right. Prefix
operators nest right; postfix calls, member access and indexing bind more
tightly than prefix operators. Parentheses group a full `Expression` and
override the table, so they are not a binary precedence level. `is null`
and `is some name` have equality precedence. The table is highest first:

| Level | Syntax | Associativity |
|---:|---|---|
| 14 | postfix call, member access, index, `?.`, `?[]`, `++`, `--` | left |
| 13 | prefix `+ - ! ~ * & move ++ -- await`; contextual index-bound `^` | prefix/right-nesting |
| 12 | `* / %` | left |
| 11 | `+ -` | left |
| 10 | `<< >> >>>` | left |
| 9 | `< <= > >=` | left |
| 8 | `== !=`, `is null`, `is some name`, `is init` | left |
| 7 | `&` | left |
| 6 | `^` | left |
| 5 | `|` | left |
| 4 | `&&` | left |
| 3 | `||` | left |
| 2 | `??` | right |
| 1 | `?:` | right |

`<<` and `>>` are left and arithmetic right shift respectively; `>>>` is
zero-filling right shift. Counts are checked and left shift truncates to the
operand width as required by the language arithmetic contract. Built-in
operator availability is distinct from whether a nominal type may declare an
overload or a generic protocol may require it. Comparisons may chain
syntactically (`a < b < c`) and require semantic checking; the grammar does
not imply chained-comparison meaning. In
`is some`, `some` is contextual and is followed by an identifier naming the
bound value, e.g. `maybe is some value`.

```ebnf
Expression      = LambdaExpression | ThrowExpression | ConditionalExpression ;
ConditionalExpression = OperatorExpression,
                        [ "?", Expression, ":", Expression ] ;
OperatorExpression = Operand, { ( BinaryOperator, Operand | IsTest ) } ;
Operand         = { PrefixOperator }, ( Postfix | ThrowExpression ) ;
PrefixOperator  = "+" | "-" | "!" | "~" | "*" | "&"
                | "move" | "++" | "--" | "await" ;
BinaryOperator  = "*" | "/" | "%" | "+" | "-"
                | "<<" | ">>" | ">>>"
                | "<" | "<=" | ">" | ">=" | "==" | "!="
                | "&" | "^" | "|" | "&&" | "||" | "??" ;
IsTest          = "is", ( "null" | "some", Identifier | "init" ) ;
Postfix         = Primary, { PostfixPart } ;
PostfixPart     = ".", Identifier, [ TypeArguments ]
                | ".", "via", "<", Type, ">"
                | Arguments | "[", IndexArgument, { ",", IndexArgument }, "]"
                | "?.", Identifier, [ TypeArguments ]
                | "?[", IndexArgument, { ",", IndexArgument }, "]"
                | "++" | "--" ;
IndexArgument   = IndexBound | SliceRange ;
IndexBound      = Expression | FromEndBound | "(", IndexBound, ")" ;
FromEndBound    = "^", Operand ;
SliceRange      = [ IndexBound ], "..", [ IndexBound ]
                | [ IndexBound ], "..^", Operand ;
Primary         = "(", [ Expression ], ")" | TupleExpression
                | SwitchExpression | CollectionExpression
                | "new", Type, Arguments,
                  [ ObjectInitializer ]
                | Number | String | RawText | InterpolatedText | InterpolatedRawText
                | Character | Boolean | Null | "field" | "this" | ReturnValueReference
                | Identifier, [ TypeArguments, Arguments ]
                | "unsafe", "(", Expression, ")"
                | [ "require" ], "policy", PolicyCases, "(", Expression, ")"
                | "default", "<", Type, ">" ;
TypeArguments   = "<", GenericArgument, { ",", GenericArgument }, ">" ;
GenericArgument = Type | ConstantGenericArgument | "_" | "default" ;
ConstantGenericArgument = ConstantLiteral | DottedIdentifier
                        | "(", ConstantExpression, ")" ;
ConstantLiteral = Number | String | RawText | Character | Boolean | Null ;
PolicyCases     = "<", PolicyCase, { ",", PolicyCase }, ">" ;
PolicyCase      = DottedIdentifier ;
ObjectInitializer = "{", { Identifier, "=", Expression, ";" }, "}" ;
TupleExpression = "(", TupleElement, ",", TupleElement,
                  { ",", TupleElement }, ")" ;
TupleElement    = [ Identifier, ":" ], Expression ;
LambdaExpression = LambdaPrefix, LambdaParameters, ContractClauses,
                   "=>", ( Expression | Block ) ;
LambdaPrefix    = { "static" | "async" } ;
LambdaParameters = Identifier | "_"
                 | "(", [ LambdaParameter, { ",", LambdaParameter } ], ")" ;
LambdaParameter = Identifier | "_"
                | { Attribute }, [ PassingMode ], Type, ( Identifier | "_" ) ;
ThrowExpression = "throw", Expression ;
CollectionExpression = "[", [ CollectionElement,
                       { ",", CollectionElement }, [ "," ] ], "]" ;
CollectionElement = Expression | "..", Expression ;
```

`IsTest` is a postfix-like binary test at equality precedence: it consumes
the preceding expression as its left operand and its `null` or `some name`
tail as the right form. Unlike an ordinary binary operator, it may bind
`name` for the appropriate checked scope. The table, not the flat
`BinaryOperator` alternatives, resolves any mixed expression. `HexFloat`
participates in `Number` exactly like the other numeric alternatives; its
value semantics and lexical boundaries are described in §2.
When `Primary` is a bare `Number`, its first `PostfixPart` cannot be member
access (`.`, `?.`, or `.via<I>`); parenthesized or explicitly typed receivers
use the ordinary postfix grammar.

Calls and indexing are delimited postfix operators, not grouping constructs:
`f(...)` and `f[...]` take the preceding expression as their receiver.
Their grammatical role, not the delimiter characters alone, determines
attachment. Call argument openers attach directly to the callee's final
token, including type and constructor calls. Index openers `[` and `?[` likewise attach to
the receiver's final token. Use `f(x)`, `int32(value)`, `new Widget(value)`,
`items[i]` and `items?[i]`, not `f (x)` or `items [i]`.
Whitespace, comments and line breaks between the receiver and opener reject.
A generic call attaches after its type-argument closing delimiter, as in
`f<T>(x)`; a parenthesized receiver attaches after its closing parenthesis.
Grouping and control-flow parentheses have no receiver and do not acquire
this requirement: `(x + y)` and `if (ready)` remain valid.
Interior trivia in ordinary parentheses, brackets and braces is optional:
`f(x)` and `f( x )`, `(x + y)` and `( x + y )`, and `items[i]` and
`items[ i ]` are valid forms. Whitespace, comments and line breaks may
separate the enclosed tokens from the opening/closing delimiters, including
multiline argument lists:

```terrum
F(
    x,
    y,
    z
)
```

Receiver attachment and operator spacing still apply independently:
`f (x)` and `f(- x)` reject. Literal/interpolation delimiters retain their
own rules; interior trivia is not permission to alter literal contents.

Commas and semicolons are fixed structural separators, not postfix operators.
They impose no spacing or attachment requirement of their own: `f(x,y)`,
`f(x, y)` and `f(x , y)` are valid forms, as are `return x;` and
`return x ;`. Whitespace, comments and line breaks around separators remain
ordinary trivia. Leading-comma alignment is therefore also valid:

```terrum
F(
    x
  , y
  , z
)
```

This does not change where separators, trailing commas or empty statements
are allowed. Commas and semicolons delimit operator runs rather than becoming
part of future operator spellings.

Generic argument parsing uses the selected declaration's formal kinds to
distinguish type arguments from constant-value expressions; the broad
alternatives above do not permit arbitrary runtime expressions as constants.
The grammar includes explicit `_` inference holes and `default` selectors,
not source type packs or unresolved exported holes. Constructor-specific
inference, first-class range values, full type-test and prefix-cast forms remain
separate work; assignment remains a statement. `T(expression)` is an
ordinary type-call-shaped expression; its checked-conversion interpretation
is semantic.
`unsafe(expr)` scopes caller-unsafe obligations to the expression; it is
not a cast and does not switch arithmetic policy. Policy blocks and
expressions select declared variants under §5.12; there is no separate
`unchecked` expression or block. `default<I>.Member(...)`
directly selects a named interface's available default body under a checked
conformance; ordinary interface calls still use the selected witness.
Direct default-body selection is restricted to the current implementing
`this` inside an interface default body or its conforming type/adapter
implementation. It cannot bypass another receiver's implementation from
outside; authored forwarding APIs may expose that behavior explicitly.

`condition ? a : b` evaluates its condition once and only the selected arm;
both arms are checked under the expected result contract. `value ?? fallback`
reads a live initialized optional, using its contained value when present
and evaluating the fallback only for null. Neither reads vacant storage or
implicitly moves a unique value out of a borrowed optional.
`place ??= fallback;` evaluates a writable initialized optional place once
and assigns only when null; it is not vacancy initialization.
`?.` and `?[]` skip the remainder of their continuous access/call chain,
including arguments and indices, when that receiver is null. Parentheses
end the chain, and a separately optional member requires its own handling.
Results are lifted to normalized optionals; bounds errors and invoked
member failures remain ordinary failures. Optional-access chains are not
assignment destinations, but permitted method calls still obey their
ordinary receiver and effect contracts.

Inside an index argument, `start..end` selects a half-open slice, including
the start and excluding the end. Either endpoint may be omitted:
`items[..end]`, `items[start..]`, and `items[..]`. A standard zero-based
sequence uses zero and length for omitted bounds and checks
`0 <= start <= end <= length`; empty/end slices retain their backing
dependencies. Bounds are not silently clamped.
The receiver evaluates once, then supplied endpoint expressions evaluate
once in source order under the selected index-domain contract. Optional
indexing skips those expressions when its receiver is null.
Slicing requires a declared checked contract, not a method discovered by
the name `Slice`. The contract determines the result's type, copy/view
behavior, access and ownership/lifetime dependencies. Text slices must
preserve scalar boundaries; slicing does not silently project validated
text into unchecked raw units. Syntax does not grant heterogeneous tuple
slicing, tensor rank changes or arbitrary indexer range support without
their own eligible contracts. First-class range values remain separate work.

The slice connector `..` imposes no spacing or attachment requirement
of its own: `items[start..end]`, `items[start .. end]`, `items[..end]`
and `items[start..]` are valid forms. It connects slice bounds rather than
acting as a binary arithmetic operator.

An index or slice bound may use contextual prefix `^offset` for an
end-relative position. It has ordinary prefix binding; use `^(offset + 1)`
for a computed offset. The prefix attaches directly to its following
operand token: `^1` and `^(offset + 1)` are valid, but `^ 1` rejects.
Infix `^` retains bitwise XOR meaning. Standard
zero-based sequences check `0 <= offset <= length` before computing
`length - offset`, without unsigned wrapping. `^1` selects the last element;
`^0` denotes the end boundary and is legal as a slice endpoint, not an
element access. Element accesses must still resolve below length.
The offset evaluates once and is skipped by null optional indexing.
The defined composite punctuator `..^` introduces a slice with an
end-relative upper bound: `items[start..^1]` and `items[..^1]`.
The composite likewise attaches to its offset: `..^1` or
`..^(offset + 1)`, not `..^ 1`.
The separated `items[start.. ^1]` form remains valid with the same semantics.
Recognize `..^` as one assigned spelling, not as permission to concatenate
arbitrary range/spread and prefix operators. It is admitted only in this
slice-bound role; unassigned runs such as `...` remain invalid.
Standalone index/range values require an explicitly registered value domain
and consuming contract before their syntax is admitted outside indexing.
No allocating representation or universal `Index`/`Range` type is implied.
This contextual bound syntax does not infer a universal first-class `Index`
type or authorize unrelated unary `^` overloads.

Prefix/postfix `++` and `--` initially apply to writable `Copy` places.
The place evaluates once, a checked operator produces its replacement,
and prefix returns the new value while postfix returns the saved old one.
Operator failure does not commit the replacement, but does not roll back
other already-performed effects. Unique/owning update contracts remain open.

Tuples have two or more elements with optional names, not a new nominal
identity or implied foreign/public ABI. `(x)` remains grouping and `()` is
the void value. Zero-/one-arity tuple extensions, including any unified
grouping/decomposition model, are deferred with eventual type-pack design;
there is no `(x,)` form. An unnamed element uses `pair[0]` with a required
compile-time constant, zero-based in-range index, producing that element's
exact type. Named elements use their declared names without synthesized
`Item1` aliases. An arbitrary runtime index cannot pick a heterogeneous
result type from this rule.
End-relative tuple element access likewise requires a compile-time constant
offset resolving to one valid position; `^0` is not an element.

Lambdas require an explicit or contextual callable contract. An explicitly
typed parameter list does not make `var f = (int32 x) => x + 1` infer a
canonical `func` or `fnptr` representation. Expression/block bodies obey
ordinary result, effect, capture, ownership and lifetime checks.
`static` forbids capturing enclosing locals, parameters or `this`, but does
not itself select a representation, allocation strategy or inlining.
Contextually generic lambdas retain their definition-site constraint contract.
Lambdas may place grouped `require` then `ensure` clauses after their
parameters and before `=>`, using the same predicate/body-arrow boundary
as named callables. Clauses are checked against the explicit/contextual
callable target's published contract; they cannot silently strengthen its
caller obligations, weaken its guarantees, or bypass its effect/access/
lifetime bounds. Stronger requirements need an eligible advertised target
contract or explicit declared adaptation, not a hidden target narrowing.
Their presence does not infer a canonical callable representation.
A lambda's own `ensure` binds contextual `return` to that lambda's result;
an ordinary nested lambda inside another postcondition does not acquire a
new result binding merely by existing there. Body-level checks remain
ordinary authored checks, not inferred public contracts.

`async` marks a function implementation or lambda, including expression-bodied
forms; it is not part of the callable's result type. Declaration modifiers
retain their ordinary legal permutation rules. Lambda `static` and `async`
may occur in either order, at most once each. An async implementation must
have a declared or contextual task-like completion result, or the eligible
async-producer result contract described in §5.12 when marked `[Iterator]`.
Arbitrary result types are not implicitly wrapped, and `async void` rejects.
In an ordinary async body,
successful return or expression-body evaluation supplies the completion's
result, not a second task handle. Task-like and awaitable eligibility require
checked contracts, not similarly named methods.

`await expression` is a prefix expression within an async implementation.
Postfix access/calls bind first; `await Read() + offset` means
`(await Read()) + offset`, while `await (ChooseTask())` awaits the grouped
operand. The operand evaluates once and its checked completion supplies the
await expression's result or propagates failure under the ordinary failure
rules. Awaiting a completed operation may continue inline; an incomplete
operation suspends under its checked scheduling contract. Ordinary async
invocation starts eagerly, without implicit executor capture or a mandatory
worker hop.
Postconditions are checked before successful completion publication, and
suspension preserves ownership, loan, cleanup and affinity obligations.
A separately declared non-async lambda is not an async body merely because
it occurs inside one. Awaiter registration, executor APIs, affinity annotations
and completion representation remain separate specification work.

Interpolated text is target-directed through a registered checked text
handler, not an implicit allocating string type or arbitrary same-named
builder methods. Hole expressions evaluate once, left-to-right into typed
captures before segments drain. An alignment expression may be an ordinary
checked runtime expression, not just a compile-time constant. Evaluate the
value first, then its supplied alignment once, then its supplied runtime
format once, then proceed to the next hole. Width and format expressions
are not deferred until output draining and obey ordinary ownership,
lifetime, effect and failure checks.
Alignment is checked in the handler's declared signed integral width domain,
without inventing an independent numeric literal type. Positive width means
right alignment, negative means left, and zero requires no padding. Width is
a minimum, not a truncation limit; longer formatted content remains intact.
Width bounds, magnitude computation and text measurement obey the checked
handler contract, not unchecked negation or an implicit bytes/code-units/
terminal-columns choice. Format text and the value are passed to that
contract without obligatory `ToString` conversion or temporary strings.
Quoted and computed format arguments are checked under the handler's
published format-argument contract; neither their syntax nor a similarly
named method grants a formatter registration or an implicit representation.
Named unique transfers require explicit
`move`; borrowed captures retain their backing and lifetime dependencies.
Formatting forwards the same writer for nested values without obligatory
temporary strings or boxing. Handler refusal stops future writes, not prior
hole effects or output; it provides no rollback or replay guarantee.
Owning text publishes only after full validation/freeze, with partial-state
and refused-capture cleanup under the declared contracts. Handler APIs and
resumable async formatting remain separate library design.

Collection expressions are target-typed and use only a registered, checked
construction contract; `[]` is empty and no default array/List or numeric
width is invented. `..sequence` spreads can interleave with ordinary elements
under eligible iteration and construction contracts, without reinterpretation
or implicit ownership transfer. Conditional inclusion composes existing forms:
`..(condition ? [element] : [])`; no collection-specific `if` form is added.
Nested fragment typing and any guaranteed direct-fragment construction must
come from the selected target registration. Without that support, nesting
does not acquire a type or a no-intermediate-construction guarantee by syntax
alone. An implementation may eliminate intermediates only when permitted by
the contract or proved unobservable, preserving identity/escape, allocation
refusal, allocator hooks, cleanup, memory observations, effects and failures.
Equivalent final elements are insufficient to justify substituting builder
operations with different contracts. The accepted eager-capture and drain
ordering must be preserved. Mapping/filtering initially compose
sequence APIs and spreads. Dedicated comprehensions and repetition are
deferred for deeper library design, including tensor rank/shape, broadcasting,
traversal, allocation, and copy-versus-per-element-generation contracts.

`defer expr;` and `defer { ... }` register an action when execution reaches
the statement. The expression or block runs when the enclosing lexical block
exits, including normal transfers and checked unwinding; operands are
evaluated then, not when registered. Registered actions run in reverse order.
References used by a deferred action must remain valid until it executes;
control transfers cannot leave the action, just as they cannot leave a
`finally`. Deferred actions have the same ordinary failure and effect rules
as authored `finally`, not a special `NoThrow` requirement. See the
[language exception contract](language.md#safe-and-unsafe-boundaries).

`PolicyCase` binds a compile-time constant named enum case, not a type
argument or a runtime enum value. Exact policy selection, strictness and
generic forwarding are described in [§5.12](#512-modifiers-cleanup-and-finalization).

## 4. Accepted language direction

These semantic constraints accompany the source forms above; they do not
claim an implementation or settle the explicitly open boundaries.

- **Access, borrowing, and output state:** canonical forms include `ref<T>`,
  `ptr<T>`, `box<T>`, `val<T>`, `own<T>` and `write`. `mut` qualifies a place,
  nominal declaration or receiver; here `mut ref` is a passing mode, not a
  generic type qualifier. Access and receiver rules follow [language types and
  operations](language.md#types-and-operations); readonly access does not
  upgrade to writable access. `implicit ref<T>` follows the [parameter-only
  loan contract](lifetimes.md#parameter-only-implicit-readonly-loans); it
  adds no noescape rule or permission to widen declared dependencies.
  Conditional outputs follow [declaration postconditions](language-contracts.md#2-outcome-initialization-and-nullability-are-separate):
  `ensure` is an ordinary Boolean postcondition, live/nullable-live/vacant
  states remain distinct, and vacant storage is never read. Named callable
  clauses use §3.1 and target-checked lambdas use §3.4; concrete producer-phase
  registration and correlated-output metadata remain specification work
  (see [§5.8](#58-postcondition-syntax-for-conditional-output-state)).
  `init ref<T>` means
  vacant-storage initialization, distinct from ordinary `ref`/`write ref`;
  `Create(init value)` is the call-site spelling; complete passing-mode/place
  rules and output metadata remain open.
- **Generic type checking:** explicit parameters, constraints and static
  operator protocols are accepted direction; associated types and family
  membership follow [type-family contracts](type-families.md#explicit-families)
  and [associated relationships](type-families.md#nominal-witnesses-and-associated-relationships).
  Optional projection is non-injective and semantically idempotent, but
  repeated source `T??` is not valid syntax. This specification gives `_` holes,
  generic defaults and selected associated-type spellings; full inference,
  constructor and witness contracts remain separate work. Type packs are
  deferred, not ruled out as a future language capability.
- **Functions and overloads:** overload selection is source-time and fixes the
  selected declaration and closed type arguments; runtime virtual/interface
  dispatch is separate. Applicability and ranking follow [callable and
  `params` rules](language.md#callable-values-params-and-tuples): only
  declared projections/attenuations apply, no implicit numeric/conversion-chain
  search is performed, constraints do not create overloads, return type is not
  a tie-breaker, and applicable members outrank extensions. By-value `T` beats
  `implicit ref<T>` when both apply; fixed arity beats an equally good expanded
  `params` candidate. Explicit extension-provider selection is specified in
  §5.2; extension declaration/import eligibility, complete applicability and
  diagnostics remain open.
- **Types and declarations:** type/declaration semantics follow [language
  interfaces and generic properties](language.md#interfaces-access-and-generic-properties).
  An intended reserved word does not establish a declaration or statement
  production; the bounded async, matching and failure forms above do not
  settle union declaration syntax. Nominal tagged sums declare explicit cases
  and an explicit closedness contract; closed case sets support exhaustive
  dispatch, while evolvable sums require an unknown-case path preserving opaque
  payload validity, roots and cleanup. A live optional payload such as
  `Found(null)` is distinct from a no-payload `Missing`. Declaration and
  construction spelling remain open; no `case` keyword is introduced.
  New pattern syntax must compose clearly with Terrum's existing types,
  access, places and ownership rather than importing C# property-pattern
  punctuation. Per-arm member observation/reuse and extractor binding remain
  open; once-only scrutinee evaluation does not settle them.
  Owner withers require an explicit consuming or borrowing update contract
  with checked old/new ownership and failure cleanup. No nominal declaration
  automatically synthesizes a copy-based wither or record machinery.
  Author-selected source generators may provide common operations, including
  withers, under the ordinary checked partial-declaration contract.
- **Adapters, families, and protocols:** conformance, adapters, associated
  projections and family erasure follow [type-family contracts](type-families.md#nominal-witnesses-and-associated-relationships).
  Conformance and projections are explicit and checked; imports, matching
  members, or equal representation do not create them. Adapters do not change
  the original witness set; ABI-transparent wrappers are distinct from
  adapters. No universal `Type<T>`-to-`Type<void>` erasure, inferred covariance,
  or equality/hash/formatting root is implied. Exact source forms, forwarding,
  metadata, projection eligibility and readonly-view backing/lifetime/cost
  remain open.
- **Layout and interop:** `Auto` leaves fields private/unfixed; `Sequential`
  follows declaration order; `Union` overlays at offset zero and requires
  `Blittable`. `Transparent` is exactly one carrier field; `Blittable` fixes
  per-profile size/alignment/offsets; `Transparent` commits to its carrier's
  representation and direct call classification. Public fields require fixed `Blittable`
  representation. Declared overlap is available only with `Union`; alternative
  access is unsafe, with no active-member tracking or safe typed-overlap form.
  Unsafe pointer aliasing is separate. Safe tagged sums require tag checks
  that preserve validity, ownership, roots and cleanup; they do not authorize
  raw reinterpretation. Padding is uninitialized. Published
  language/layout/access/ownership/effect contracts are binary-replacement
  commitments, distinct from foreign ABI and serialization; incompatible
  changes break, while compatible extensions or stronger guarantees are not
  inherently breaking. No global bool/reference/SIMD wire format is selected.
  Unfrozen layout-dependent values cross only layout-coupled
  compile/rebuild/invalidation boundaries; JIT/AOT labels alone do not
  establish coupling. Power-of-two member packing and aggregate over-alignment
  are separate and may combine. Raw object-byte blits and unmediated foreign
  transfers are unsafe; serializers account for emitted bytes, and verified/
  trusted raw copying requires a covering contract. See [layout
  definitions](data-layout-and-abi.md#definitions), [fixed
  representations](data-layout-and-abi.md#fixed-directly-interoperable-representations),
  [packing/alignment](data-layout-and-abi.md#packing-and-aggregate-alignment),
  and [overlap safety](memory-model.md#overlap-representation-access-and-unions).
  Exact syntax and profile limits remain open.
- **Text and numeric forms:** [conversion policies](conversions.md#conversion-policies)
  define numeric behavior and [character intent](conversions.md#character-intent)
  defines character forms. There are no width suffixes or default numeric type;
  `T(expression)` is a checked conversion, literals are context-typed, and
  numeric operators are checked by default. Floating APIs are target-specific;
  character literals have the scalar/target rules in §2, while custom literal
  registration and floating token-boundary details remain work. `utf8z("...")` is an
  ordinary call rather than a lexer prefix.

<a id="5-open-grammar-and-semantic-choices"></a>

## 5. Source contracts and open choices

Examples in this section are informative. Settled source and semantic
contracts apply as stated; explicitly **OPEN**, proposed or deferred choices
are unresolved.
See the canonical [language direction](language.md),
[library layering](libraries.md#dependency-layers-and-package-boundaries),
and [specification roadmap](roadmap.md#language-semantic-il-and-metadata-specification)
for authoritative organization and semantic IL/reference boundaries.

### 5.1 Initialization loans at call sites

**Accepted target spelling:** use `init` as the call-site argument mode for an
`init ref<T>` parameter, e.g. `Create(init value)`. It designates vacant
storage and is distinct from ordinary `ref`/`write ref` access; it does not
make an existing initialized value writable or readable
through the initialization loan.

```terrum
// Initialization-loan argument.
Create(init value);
```

Argument labels are specified in §3.2; complete place, overload-binding and
conditional-output metadata rules remain open. This does not
introduce an extra transactional publication protocol.

### 5.2 Exact overload grammar, ranking, and disambiguation

**Accepted semantic direction:** ordinary same-name declarations form
source-time candidates; binding selects one concrete declaration and closed
generic arguments. That target is recorded for downstream validation and code
generation. Linkers/JITs do not rerun overload resolution; runtime
virtual/interface dispatch is a separate mechanism. A by-value `T` wins over
`implicit ref<T>` for ordinary `F(value)` when both apply. Exact matches
precede previously permitted, declared projections/erasures; there is no
implicit numeric or general type-conversion search. Permitted
access/qualification weakening may participate in matching (for example,
`write Span<T>` to a readonly `Span<T>` parameter), but no new or reversed
projection/erasure is inferred. Constraints filter applicability, not form
constraint-only overloads. Return type alone does not distinguish overloads.
Applicable members outrank extensions. These decisions are authoritative in
[language](language.md#callable-values-params-and-tuples),
[conversions](conversions.md#conversion-policies), and
[type families](type-families.md); complete overload resolution remains open.

The declarations below illustrate accepted overload semantics; complete
applicability rules and diagnostics remain open. No new overload keyword is
required by this direction.

```terrum
// Declarations illustrating overload semantics.
int32 Parse(int32 value) { return value; }
uint32 Parse(uint32 value) { return value; }

void ParseExample()
{
    // Parse(42) is ambiguous; explicit target construction selects int32.
    int32 parsed = Parse(int32(42));
}
```

The accepted `implicit ref<T>` form also participates, but a by-value match
wins for ordinary `Inspect(item)` when both candidates apply:

```terrum
// Parameter forms illustrating overload semantics.
int32 Inspect(Item value) { return 0; }
int32 Inspect(implicit ref<Item> value) { return 1; }
void InspectExample(Item item) { int32 selected = Inspect(item); }
```

**Accepted ranking direction:** retain ordinary same-name declarations as
source-time candidates. Determine applicability first, then compare argument
quality independently. Identity is better than permitted
access/qualification weakening (for example, `write Span<T>` to `Span<T>`).
A candidate is better only if it is no worse for every argument and better
for at least one; crossed better/worse arguments are ambiguous, not summed
into a score. Apply declared specificity and accepted by-value/
`implicit ref<T>`/`params` priorities, including fixed-arity non-expanded over
an otherwise equal expanded `params` candidate. There are no return-type,
declaration-order, or literal-numeric-fit tie-breakers. Do not search implicit
numeric or arbitrary type-conversion chains. Constraints filter applicability
but do not form constraint-only overloads. Source binding records the selected
declaration and closed generic arguments; downstream tools do not rerun source
overload resolution.

**Accepted generic specificity:** after argument quality and the existing
binding/`params` priorities tie, compare declared parameter structure rather
than counting inferred formals. An applicable `Span<U>` parameter pattern is
more specific than bare `T` at a `Span<int32>` argument: both infer an exact
parameter, but only the former declares the span structure. A pattern that
strictly specializes another through consistent substitution is more
specific; unrelated/crossed structures remain ambiguous. An applicable
nongeneric exact declaration wins a remaining tie with a generic declaration,
not regardless of argument quality. Explicit generic arguments select only
eligible generic candidates.

```terrum
int32 Select(int32 value) => value;
T Select<T>(T value) where T : [Copy] => value;
int32 OrdinarySelection() => Select(int32(7));
int32 ExplicitGenericSelection() => Select<int32>(int32(7));
```

Both candidates give an exact `int32` argument match in `OrdinarySelection`;
the nongeneric declaration wins the remaining tie. The explicit type argument
in `ExplicitGenericSelection` selects the generic declaration.

Constructors use the same staged inference: explicit arguments pin positions,
typed value arguments contribute types, expected-result context fills
unresolved positions, and declared defaults fill omitted unresolved positions
before contextual literals/lambdas are checked. Do not infer generic arguments
from field capture, private constructor implementation or arbitrary witness
search. Construction remains separate from the conversion type-call under
§5.13.
Complete applicability details, exact diagnostics, concrete extension
declaration/import schemas and serialized binding metadata remain specification
work.

#### Extension-provider selection

**Accepted target syntax:** `value.via<Provider>.Member(arguments)` selects
an explicitly eligible extension provider. Initially `Provider` is a nominal
non-interface container, distinct from the interface selector in
`value.via<I>.Member(arguments)`. The latter still selects an actual declared
witness; neither form creates conformance, bypasses access or loan checks, or
makes every static/private member of a named container eligible as an extension.
Extension eligibility is lexical and explicit. A provider declaration and
lexical import must grant eligibility; naming an arbitrary public static
container does not. Ordinary access, loan checks and member-over-extension
ranking remain intact. Concrete declaration/import schemas remain specification
work, not implicit provider discovery.

For eligible providers with these ordinary static helpers:

```terrum
struct MetricsA
{
    public static int32 Measure(int32 value) => value;
}
struct MetricsB
{
    public static int32 Measure(int32 value) => value + 1;
}
int32 ChooseA(int32 value) => value.via<MetricsA>.Measure();
int32 ChooseB(int32 value) => value.via<MetricsB>.Measure();
```

The selected calls have the behavior of `MetricsA.Measure(value)` and
`MetricsB.Measure(value)`. The receiver is evaluated once before explicit
arguments and occupies the extension receiver parameter. Thus
`value.via<MetricsA>.Measure(value)` supplies two values and rejects for the
one-parameter helper above; it needs an eligible two-parameter overload.
Receiver access, ownership and lifetime obligations remain those of the
selected declaration; this evaluation rule does not select a new loan
reservation/activation policy.

An applicable ordinary member outranks extensions in unqualified lookup.
Explicit provider selection can choose that provider even when an ordinary
same-named member would win `value.Member()`. Overloads within the selected
provider use the ordinary applicability/ranking rules, not declaration/import
order. Equally good providers remain ambiguous without selection; ordinary
qualified static calls remain available. Approval of this selector is not
approval of extension-block/registration syntax, runtime provider discovery or
current compiler support.

### 5.3 Caller-unsafe declarations and explicit unsafe scopes

**Accepted target syntax:** `unsafe` on a callable declaration exposes its
caller obligations, e.g. `unsafe void M()`. An ordinary `void M()` does not
propagate a caller-unsafe obligation. The declaration modifier does not make
its body an unsafe context: unsafe operations inside either form still require
an explicit `unsafe { ... }` block or `unsafe(expr)` expression. A caller invoking the unsafe declaration
must likewise satisfy the contract in an explicit unsafe context. The
`unsafe(expr)` form scopes the same caller obligations to one expression.

```terrum
// Caller-unsafe declaration.
unsafe uint32 LoadAligned(uint64 address)
{
    unsafe { return 0; }
}

uint32 Read(uint64 address)
{
    unsafe { return LoadAligned(address); }
}

// This ordinary declaration does not pass caller obligations onward.
uint32 LoadValidated(uint64 address)
{
    unsafe { return 0; } // Unsafe implementation work remains explicitly scoped.
}
```

The declaration contract and explicit unsafe blocks carry the machine-checked
obligations; an internal unsafe block alone does not make an ordinary
declaration caller-unsafe. Encourage a `# safety` documentation section to
explain caller responsibilities in human terms, but prose is not a compiler
proof or a substitute for contract metadata. An ordinary safe declaration
may contain internal unsafe work in explicit blocks when its declared safe
contract is checked; no extra trusted-implementation source marker is required. The
default binary-trust model does not require downstream consumers to re-prove
dependency bodies.

The contract metadata representation remains open.

<a id="54-aliases-and-associated-types-candidate-declarations-open-binding"></a>

### 5.4 Aliases and associated types: declarations and open binding

**Accepted semantic direction:** families opt in explicitly and declare
checked associated projections and constraints. Existing explicit
declarations such as `TextSpan<void>` are not reopened; this is not universal
`Type<T>`-to-`Type<void>` erasure. Associated types are named type members of
an interface witness. The §3.1 forms are `type Wider;` in the
interface and `type Wider = MyWideInteger;` in a conforming type.
`T.Wider` resolves through the selected `IInteger<T>` witness. An associated
identity clause uses `where T.via<ISource>.Item is U` to equate a selected
projection with a resolved type. `is` here means exact complete type identity,
not runtime pattern matching, assignability or conformance. Ordinary
`where T : I` still requires the named witness.
Do not infer the original `T` backward
from an associated result, choose an arbitrary implementer from a constraint,
or silently resolve collisions; ambiguous witness/projection uses require
explicit qualification.

**Alias form:** `alias Option<T> = T?;` names the same resolved type,
not a distinct wrapper or new witness. Aliases are namespace-level
declarations, with `public` or `internal` visibility like other
top-level declarations; they are not nominal members. Exporting or
qualifying an alias cannot create a new nominal identity or change the
target's access, ownership, layout or compatibility contract. A public alias
is exported as a named transparent source declaration; executable signatures
use its resolved target identity. Its public name and target mapping are source
contracts, not a new nominal runtime type. `Option<T>` is informative, not a
decision that Core or another library exports it. Alias expansion must be
finite; direct and indirect recursive synonym cycles reject with their
declaration chain. Substitute complete qualified arguments without dropping
access/ownership qualifiers, and validate effective accessibility of the
expanded target. Recursive data uses nominal types, not an infinitely expanded
alias. Exact alias metadata encoding remains open.
Associated-type bindings are distinct: they satisfy a selected interface
member and have witness identity, rather than providing a general synonym.

**Self-binding spelling:** `interface IInteger<this T>` declares
one explicit implementing-type formal rather than an implicit `Self` name.
It is the first formal when present; a conformance binds it to its
implementing type. Interfaces with no self dependency omit it. Ordinary
type formals follow it, and the checked witness retains the selected
interface/member identity. This does not alter the accepted associated-type
relations; metadata encoding remains open. An interface such as
`interface ISource { type Item; }` need not declare a self formal merely to
have associated members.

An associated requirement may have a replaceable checked default,
`type Item = uint8;`; bodyless `type Item;` requires an implementation binding.
Conflicting inherited defaults require explicit resolution. A type can
inherit a member through several paths without creating several copies of
one declaring-interface identity. Selection requires one unambiguous
inherited identity/default or an explicit checked resolution; unrelated
competing defaults cannot be ranked by inheritance/import order. This applies
to associated-type defaults as well as callable defaults, so an unrelated
new base cannot silently retarget a previously resolved member. A type can
implement two interfaces with same-named members explicitly, implicitly, or
one each. One implicit member can satisfy both only if it fulfills both
contracts; the witness identities remain distinct. Explicit declarations use
interface-qualified names, for example `type IReadable.Item = uint8;` and
`uint8 IReadable.Read() { ... }`.

`T.Item` resolves when exactly one applicable selected witness supplies it.
`T.via<ISource>.Item` and `x.via<ISource>.Member()` select an existing witness
explicitly; `via` is contextual after `.`. They do not create conformance,
implicitly box/convert the receiver, expose unrelated private members, or
bypass the selected implementation. Conformance visibility follows the
effective visibility of its type and interface, with no independent witness
visibility. Access is equivalent to what a caller could obtain through a
valid constrained helper, not broader. Conflicting claims for the same
type/interface pair reject rather than being resolved with `via`.
Extension-interface behavior still needs an explicit adapter and does not
change the original type's witnesses.

Associated identity resolves the selected witness and ordinary type arguments
first, then checks equality. Transparent aliases normalize to their targets;
nominal wrappers do not. Full access/ownership qualification remains part of
the comparison. The clause is an applicability check, not a new inference
source or a search for an otherwise unknown implementing type.
Complete checking diagnostics and metadata representation remain open.
For a selected `Item = int32`, `where T.via<ISource>.Item is U` succeeds
when the otherwise resolved `U` is `int32` or its transparent alias, not
`int64` or a nominal wrapper over `int32`.
Preserve the selected inherited-identity/default resolution rules. Keep family
opt-in and declared checked projections; do not introduce universal erasure
or inferred covariance.

**Accepted family projection direction:** identity preserves the complete
qualified type and its ownership obligations; it does not duplicate an affine
value. Within an explicitly opted-in family, a declared typed-to-erased
projection can hide static information while retaining the actual
representation, validity, access and backing/lifetime dependencies. It does
not imply an unconditional reverse projection from the erased member.
For example, `TextSpan<char16>` can expose declared erasure to
`TextSpan<void>`, but erased text cannot manufacture a `char16` view from the
requested result type. A separately declared checked refinement may succeed
when retained encoding information proves that relationship; mismatch
publishes no typed view.

This is a relationship between declared family members, not a universal
conversion from arbitrary `T` to `void` and back. The language's no-data value
is `()` of type `void`; `void` in an encoding-erased family argument is not a
default encoding or a new primitive named `unit`. Exact family opt-in,
conditional-member/conformance and projection declaration syntax remain open.

Family membership remains separate from extension lookup. Conditional members
publish their eligibility and full contract for definition-site generic
checking; a favorable later instantiation cannot supply a missing guarantee.
An ordinary `Box<T>` gains neither conditional conformance nor `Box<void>`
erasure by name/arity matching. Projection must be declared and checked.

**Nominal adapters and forwarding:** begin with explicitly declared nominal
wrappers and checked forwarding members; no wrapped type gains a conformance
or exposes members automatically. Aliases and eligible extensions serve
non-nominal source reuse, not implicit nominal relationships.
Opt-in generators may select only named members or all eligible members except
an explicit exclusion set, producing inspectable ordinary declarations in
compatible partial parts. An unchanged compiled adapter does not acquire new
members when a wrapped dependency gains them. Forward nothing by default.
Resolve complete overload/accessor signatures and check access, effects,
ownership, validity and backing dependencies; no name-only guessing or raw
representation equivalence. Operators, conversions, whole-interface
conformance and one-way container projections require their own explicit
contracts rather than blanket forwarding.
Trivial forwarding should not duplicate bodies, signatures or contract data
needlessly in metadata. Compact checked target references and shared contract
information may represent it while preserving each declared member's identity,
mapping and replacement guarantees; exact encoding remains metadata work.
This efficiency requirement does not introduce a live wildcard or implicit
conformance. Concrete adapter/forwarding attribute schemas remain specification
work.

Interface constructors are deferred: a future requirement
needs a checked construction and initialization-completion contract. Fixed
`const T Name = value;` interface constants are owned by the interface
definition; a bodyless `const T Name;` instead requires a checked
implementer-supplied binding. They have distinct identity/versioning rules
and neither follows automatically from a `type Name;` requirement.

**Source type packs are deferred.** No production for variadic type parameters
is made here; generic defaults and collection spreads are not type packs.

### 5.5 Accepted temporary scope for `implicit ref<T>`

For `Foo(5).Bar(); Baz();`, materialization is shorthand for initializing an ordinary hidden local at the argument's original evaluation point in the current enclosing lexical scope, then passing its readonly reference. The local is not hoisted to scope entry and does not create a shorter lifetime-only scope. If conditional or short-circuit control flow skips the argument, no local is initialized and there is no owner to clean up. Scope exit follows ordinary initialized/moved-state cleanup. A valid returned reference may be used by later statements in that scope, subject to ordinary declared dependencies; existing place/backing lifetimes are not widened and capture/suspension/escape permissions do not change. See the canonical [reference contract](language-contracts.md#1-one-call-and-reference-model) and [implicit-loan lifetime rules](lifetimes.md#parameter-only-implicit-readonly-loans). General source/loan metadata remain open.

Ordinary writable reference access is aliasable, not an exclusive loan.
`target.Update(target.Read())` with an independent scalar result from `Read`
does not conflict merely because both calls address the same storage.
Evaluate the outer receiver once, evaluate arguments in source order, then
invoke the outer call, preserving formation and invocation checks at their
respective phases. Readonly attenuation and address commoning do not invent a
different lifetime or purity guarantee.
Exclusive owner/guard loans retain their separately declared exclusion and
invalidation rules. Implicit borrowing follows the same explicit expansion;
see [ordinary call timing](language-contracts.md#13-evaluation-and-loan-timing).
The [passing/storage matrix](lifetimes.md#passing-and-storage-access)
distinguishes place authority, object authority, initialization and pinning.

<a id="result-dependency-declarations"></a>
**Accepted result dependency declaration:** a borrowed result can publish
its possible parameter dependencies with
`[return: DependsOn(nameof(left), nameof(right))]`. Prefer `nameof` over a
literal parameter name; the named parameters belong to the annotated callable,
including when the annotation precedes its parameter list.

```terrum
[return: DependsOn(nameof(left), nameof(right))]
ref<T> Select<T>(ref<T> left, ref<T> right, bool useLeft)
{
    return useLeft ? left : right;
}
```

Arguments are checked constant names of eligible parameter declarations,
not runtime lookups. Unknown names or incompatible dependency kinds are
errors. The body must satisfy the published relationship. Since either
input may supply the result, callers retain both possible backing obligations;
this is not the intersection of the inputs' backing sets, and neither input's
permitted lifetime is extended. An independently compiled caller relies on
that published relationship, not today's branch implementation.

This result annotation does not authorize capturing an input into receiver
storage, erase a lease/root obligation, or identify a receiver's own storage
with the backing forwarded through its descriptor. Receiver-storage,
forwarded-backing, capture/storage and suspension relationships use separate
targeted contracts. Unannotated results follow the
[language-versioned declaration defaults](lifetimes.md#declaration-defaults-exceptions-and-published-compatibility),
not a dependency-bearing example or private body inference. Concrete selector
schemas remain specification work.
Concrete attribute registration and metadata encoding are implementation work;
this approval does not claim current compiler support or settle the full
general compile-time-query catalog.

### 5.6 Reserved underscore and discard statement

**Accepted target semantics:** `_` is reserved and is never an identifier.
`_ = expression;` is the standalone discard statement: evaluate once, preserve
effects and failures, and apply ordinary ownership/cleanup. It is not assignment
and has no name-dependent fallback. Positional wildcard patterns and omitted
lambda parameter names are separate nonbinding contexts; they do not consume
the matched value or alter argument evaluation/parameter mode. A possible
generic type hole is another distinct nonbinding grammar role defined in §3.2,
not a named identifier or a consequence of the discard rule. In a match over `T?`,
`some` covers the present case and `_` can cover the remaining absent case
when those are the only possibilities. The wildcard still means "any
remaining value", not `none`: it also matches other unhandled cases if the
matched type has them. `null` remains the optional absence value. The
bounded switch/pattern productions and coverage rules are in §3.3; complete
nominal-case and destructuring patterns remain to specify.

```terrum
// Standalone discard statements.
_ = MakeOwner(); // Evaluate once; clean the returned owner at statement end.
_ = int32(42);   // The type call supplies the literal's target.
```

Discard expressions are evaluated once. Any owner/result cleanup follows the
ordinary expression and statement cleanup rules; this does not force
collection. Further statement-context details are grammar/specification work,
not permission to alter the accepted nonbinding semantics.

### 5.7 Unary operator contract direction (accepted; protocol mapping remains open)

**Accepted direction:** ordinary unary `+`, `-`, `!` and `~` have
one operand of the containing type `T` (or `T?`) and separate operand/result
types; `!` is not inherently bool-result. Return type alone does not
distinguish overloads. Static `++` and `--` have operand-shaped results rather
than arbitrary independent result types. No derived-result relation is adopted.
`true` and `false` are another explicit exception: both must be declared
together and return `bool`. These rules describe the accepted unary contract shape, not the exact
protocol names or source declarations.

```terrum
// Conceptual signatures only; no interface names or source declarations selected:
R operator +(T operand);  // T is the containing type (or nullable T?).
R operator -(T operand);
R operator !(T operand);  // R need not be bool.
R operator ~(T operand);
T operator ++(T operand); // Static; operand-shaped result.
T operator --(T operand);
bool operator true(T operand);
bool operator false(T operand);
```

Callable result types precede their names; these examples do not add
operator witness or implementation contracts.
No return-only overload is permitted: result type is part of the selected
operation contract, not a mechanism for distinguishing declarations. The
`true`/`false` pairing is the explicit pair requirement here; do not infer
other pair constraints from C#. Exact Terrum witness/interface names,
constraints, conformance shape, dispatch metadata and source syntax remain
specification work. Existing binary contracts are unchanged.

C# 14 separately permits instance `++`/`--` and instance compound-assignment
operators with `void` return to mutate a receiver. That form is not part of
this specification and must not be inferred from the static operator direction.

**Official comparison sources:** [C# language specification, §15.10.2 Unary
operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#15102-unary-operators)
for unary result relationships and the `true`/`false` pair; [C# operator
overloading reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading)
for the separate C# 14 instance increment/decrement and compound-assignment
forms. These links are comparisons only; they do not specify Terrum
inheritance, interface names, or runtime behavior.

### 5.8 Postcondition syntax for conditional output state

**Accepted semantics:** declaration postconditions are the normal authoring
route for conditional output initialization facts, with an `ensure <expr>`
clause whose expression is an ordinary Boolean expression. Ordinary patterns
and short-circuit expressions compose status and value facts. A legal
predicate such as `param > 0` implies that `param` is initialized; there is no
redundant `param is init` test. Source-body/path analysis must establish that
each value read is legal and must never read vacant storage to discover a false
predicate. A successful value predicate implies initialization. A live
complete value, an initialized nullable value containing null, and an
explicitly uninitialized destination remain distinct states. Callers retain
checked correlation with a discriminator and cannot access or clean up a
vacant arm. Do not add a special `ensure-if` statement or equivalent routine
output attributes as a parallel authoring mechanism.

Within a declaration postcondition, contextual `return` denotes the result
of the declaration owning that clause, not a symbol named `result`.
For example, `int32 Clamp(int32 value) ensure return >= 0 { ... }`
states a guarantee over its returned value. Ordinary `return` statements
retain their control-flow meaning; the result reference is not available
in ordinary body expressions or preconditions. A nested lambda does not
rebind the reference to its own result, and any capture must obey ordinary
ownership and lifetime rules. The result is observed through its live
destination, not a consumed body source, with no implicit move or entry
snapshot. It must remain valid for publication.
For an ordinary async callable, the reference denotes the completed payload,
not the returned task handle, at the established completion postcondition
phase. Constructors use their current receiver and initialization guarantees,
not an invented result reference. Producer creation/advance/completion
postcondition mapping still requires a declared protocol; `return` does
not implicitly make an `ensure` clause a per-yield check.

The language-versioned default initializes a boolean-success output on `true`
and leaves it uninitialized on `false`; for other result types, an output is
initialized on every normal return unless an explicit declaration postcondition
specifies a conditional relationship. These defaults are not inferred from
method names. Postconditions may state observable boundary facts, including
relations to unchanged value inputs; such relations are behavioral guarantees,
not optimizer-only hints. A relation to prior state could state a genuine
behavioral obligation, but is deferred with state-history support; this does
not imply that such contracts lack value. A clause may refer only to declarations visible at
the method's effective accessibility and permitted by ordinary contract
expression rules. Private representation invariants cannot become hidden
caller preconditions. Caller facts derive from published guarantees checked or
proved for the callee and are invalidated by relevant mutation, alias or
lifetime changes; vacant storage is never read to test initialization.

The accepted contract order is prologue, preconditions, method body,
postconditions, epilogue. Preconditions and postconditions belong to the public
declaration/signature outside the body; the body includes initialization of
local and output values, authored execution and `finally`, local cleanup, and
cleanup of untransferred owned parameters before postconditions. Exceptional
departure has no normal-return postcondition. Entry snapshots and arbitrary
state history are deliberately deferred from the initial contract model, with
no promise to add them later. Postconditions are ordinary code at their
declared phase: false predicates and thrown predicate evaluation follow
ordinary failure/unwinding; completed body `finally` blocks are not rerun and
body side effects are not rolled back. This does not add a separate output
publication transaction.

§3.1 gives grouped declaration `require`/`ensure` placement across named
callable forms and the contextual result reference. Lambda clauses follow the target-checked form in §3.4.
Producer phase mapping and source-to-metadata mapping for
correlated initialization facts remain open. There is no
special conditional postcondition block grammar. See the canonical
[conditional output contract](language-contracts.md#2-outcome-initialization-and-nullability-are-separate).

### 5.9 Static-proof floor and constant evaluation

Declaration-level `static require` and `static ensure` are deferred pending
a concrete API need. Ordinary declaration contracts and required static
body proofs retain their established roles; no caller-proof declaration
qualifier is introduced.

**Accepted policy:** required static obligations use
a versioned portable minimum of proof rules/complexity and guaranteed
resources, available independently of optimization level. Sound
beyond-floor analysis may prove harder cases, use larger budgets and accept
additional programs; identical acceptance and a fixed compiler maximum are
not required. Complexity limits are structural/work-based, never wall-clock,
and should avoid exponential or excessive linear compilation work while
keeping common cases cheap and simple. A bounded tree depth is one possible
limit; a value such as 64 is illustrative only, not selected. Explicit ways
to simplify hard cases may be provided where runtime behavior is legal; exact
escape syntax is still work. A required obligation that is disproved or not
established by the compiler's supported analysis is a compile error, with the
outcomes distinguished and no runtime fallback. Ordinary executable
pre/postconditions remain checked at runtime unless soundly proven redundant,
preserving required evaluation and effects.

Required constant evaluation is separate from proof extensions: successful
results have exact target semantics and never fall back to runtime. Exact
source productions, minimum proof-rule catalog/complexity, structural/work
accounting and numeric limits remain to specify. Versioning the portable floor
does not choose a metadata/wire encoding, proof-certificate format or ABI.

Where runtime values/checks are permitted, an author may explicitly use an
ordinary expression/`require` instead of requiring static evaluation. This is
not a compiler fallback, and ordinary `require` remains enforced unless
soundly redundant. A runtime alternative is unavailable for compile-time-only
layout/type/metadata requirements and does not weaken structural lifetime or
initialization safety. No example fixes the exact source spelling.

### 5.10 Properties and accessor-local effects

**Accepted direction:** by-value properties on compact `Copy` structs and
source classes. **Source forms:** `get`, `set`, and construction-only `init`
accessors, including backed auto-properties and authored indexers (§3.1).
Ref-returning properties and indexers still need the general result-loan contract.

Interface properties are separate contract members: a bodyless interface
getter requires a conformance implementation, while a getter body supplies a
default under §5.12. Instance `this[...]` indexers are parameterized accessor
contracts; concrete accessors require authored bodies, not automatic
per-index backing storage.

An explicit concrete getter body makes the getter an ordinary checked
callable member with its declared readonly or `mut` receiver; `{ get; }` generates a backed auto-getter under
the backing rules below. Accessor attributes such as `[NoThrow]` attach to
the getter:

```terrum
// Informative property example.
struct Point
{
    int32 _x;

    public int32 X
    {
        [NoThrow] get { return _x; }
    }
}

class Counter
{
    int32 _count;

    public int32 Count
    {
        [NoThrow] get { return _count; }
    }
}
```

Accessor effects attach to the accessor: `[NoThrow] get { ... }`.
`[get: NoThrow] public int32 Count { get { ... } }` applies to the
getter from the enclosing property declaration. An expression-bodied
getter-only property such as `int32 Size => width;` has the same checked
getter semantics. File attributes
use `[module: Name]`, or `[assembly: Name]` when an assembly identity is
declared by the build. Other explicit target labels are in §3.1;
placement, supported attribute classes, arguments and multiplicity must
be checked against the selected target rather than inferred by name.

An ordinary `[NoThrow] public int32 GetCount() { ... }` method is an
alternative callable form; it is not a property-effect placement option.

Treat the accessor as a named, source-bound
callable member with its checked receiver requirement and result. The property
declaration and accessor must carry checked member identity and effect metadata;
do not infer a hidden `get_Name` function from a property name. A module may
encode the accessor as a callable internally, but consumers bind the declared
property/accessor identity and contract rather than a magic naming convention.

Auto accessors and explicit bodies may mix. Any auto accessor, or any use of
property-local `field`, creates exactly one implicit backing field shared by
that property's accessors at its declaration position. A body may omit
`field` even when another accessor needs it. A property with only authored
bodies and no `field` use has no implicit storage. Indexers do not acquire a
per-index backing field from this rule. Implicit backing begins vacant, not
as an invented zero/default value. A property initializer such as
`int32 X { get; } = 0;` explicitly initializes it. A declaring constructor
may instead initialize a backed getter-only property with `X = value;`,
including a property whose authored accessor uses `field`. This is a
construction privilege to initialize its backing, not a generated public
setter/init accessor or permission to assign an arbitrary computed getter.
A getter-only property without a public `init` cannot leave this obligation
to a caller initializer; every successful constructor path must establish
its live backing before publication.

```terrum
struct Point
{
    public int32 X { get; } = 0;
    public int32 Y { get; }

    public Point(int32 y)
    {
        Y = y;
    }
}
```

Other properties may defer initialization only through the declared
construction obligations below. Accessor applicability and metadata
representation remain work.
`mut get` and `mut set` follow ordinary method receiver contracts; writing
through an independently writable referent does not by itself mutate `this`.
`init` provides mutability within its initialization scope without a redundant
`mut init` modifier.

**Construction rule:** a constructor initializes all private state
it does not leave to declared `init` properties. Its published completion
contract guarantees a minimum set of `init` properties through checked,
published clauses such as `S(int32 value) ensure P is init { P = value; }`.
Each guarantee must hold on every successful constructor return and be
available to downstream source and artifact consumers, not inferred from
today's body or duplicated by a routine attribute. The caller must initialize
each remaining property before the object is fully constructed. In
`new T(...) { P = value; }`, initializer entries execute in source order
after the constructor, within the same unpublished construction phase.
Construction publishes the object only after all storage and property
obligations are fulfilled and the final invariant/postcondition checks
succeed. Failures clean partial state without publishing a live object.
This is not a general right to invoke `init` on an already constructed value.
An init property is writable during the unpublished construction phase,
not restricted to one assignment. Constructor and caller initializers may
both assign it in source order, with ordinary replacement/cleanup checks;
the last assignment determines the final value. Adding a guaranteed
property can serve old callers that still initialize it, while removing a
guarantee cannot serve callers that relied on it. A new consumer requiring
the stronger guarantee cannot bind an older weaker provider. Other value,
effect and invariant promises remain independently checked.
Direct `new T(...).with(P: value)` may use an explicitly declared
initializer-completion operation with the same gate; the qualifying
annotation, update syntax and ordinary wither contract remain open. An
explicit completion-validation operation runs after all
required constructor/caller initialization and before publication. Its
checked failure leaves no published live object and retains ordinary
partial-state cleanup. It is not inferred from a method name or a
constructor's current body, and an ordinary live-value wither cannot enter
this construction phase.
The hook has an explicit registered operation identity and checked published
contract, not name-discovered `Validate` behavior. Concrete attribute/schema
encoding remains specification work.
Full validation and a declared changed-state validation contract may coexist.
For an update from already validated state, the latter may reuse unchanged
facts only when its checked dependencies still hold, and must revalidate every
invariant affected by changed values, including cross-field relationships.
Mutable aliases, changed backing, versioned invariants or unknown dependencies
cannot inherit a stale validation fact. Copying a value alone is not proof.
The completion gate must establish the same published guarantees before the
updated value is exposed; no generated wither silently skips validation.
A normal `.with(...)` on an already live value instead produces
a separately validated updated value; it cannot mutate its source through
`init`. No `required` modifier is needed solely to express constructor
completion obligations.

### 5.11 Nested types and scoped operators

**Deferred boundary:** defer nested nominal types; begin with namespace-level
types and their members. Private nesting is genuinely useful for hiding
implementation details, tying a helper's type parameters to its container,
and grouping tightly related types. On the other hand, nested public types
couple API naming and accessibility to their outer type, complicate
qualification, generic substitution, metadata identity, source lookup and
binary compatibility, and often read better as separate namespace types.
Module-internal namespace types already hide a helper from external
consumers, though not from the rest of its module. Core-library naming such as `List<T>.Enumerator` versus `ListEnumerator<T>`
is a reason to revisit the boundary before fixing public library identities.
Nesting, if added, does not imply an implicit outer instance or capture.

**Operator placement:** do not allow operator declarations in
`TopDeclaration`. A user operator is selected from the containing type's
declared operation or an applicable checked interface witness. Imports and
extension lookup must not silently add an operator or a witness to a foreign
type. A distinct explicit adapter/wrapper can implement an extension
interface without retroactively changing the original type's witness set;
its operator is a member of that adapter or its interface contract. Define
any later extension-operator syntax, operand eligibility and selection
separately rather than interpreting a global `operator` as an extension.
These choices preserve the [accepted adapter and witness
semantics](language.md#interfaces-access-and-generic-properties).

### 5.12 Modifiers, cleanup and finalization

**Accepted semantic separation; legal combinations need checking:** `public`/`internal`/
`private` are visibility; `static` is member placement; `mut` controls a
place/receiver/nominal declaration; `write` qualifies referent access in a
type; `ref` and `init ref<T>` distinguish ordinary access from vacant-storage
initialization.
`unique` is an ownership form, not an access synonym. `unsafe` on a callable
declares caller obligations while an `unsafe { ... }` block scopes operations
inside a body. A bodyless interface declaration requires a witness
implementation, while a body supplies
a default that a conforming type may explicitly replace. Both have the
interface's declared member identity and contract. Each selected nominal
conformance maps that identity to its checked explicit implementation, or to
the default when no explicit implementation is supplied. Conforming implementations must
preserve the published signature, effects, access, ownership and lifetime
guarantees; an unrelated same-named extension is not an override. Calls
through an interface receiver use the selected witness, including calls from
a default body to another interface member; a default is not a statically
hardwired bypass around an implementation. Runtime dispatch may use a slot,
table entry or equivalent representation, but neither a default nor a
bodyless requirement mandates a particular physical vtable layout. Static
operator requirements use checked compile-time witness selection and need no
runtime vtable. Conflicting inherited defaults need an explicit resolution;
no inheritance-order tie-break or implicit linearization selects one.
Adding a default to an existing interface remains subject to compatible
binding/dispatch for older implementations, not an assumption that an older
binary has a newly appended slot. Exact witness-mapping syntax and metadata
representation remain open. Do not infer legality from the permissive `ContextualPrefix` placeholder.
Legal prefix modifiers may be permuted without changing the contract;
duplicates, conflicting visibility and illegal modifier/declaration pairs
reject. Named callable contract clauses follow the parameter list and generic
constraints as specified in §3.1; result types remain in prefix position.

**External-declaration direction:** an external callable explicitly declares
its import identity, calling convention and complete Terrum callable contract.
An ABI annotation cannot suppress caller-unsafe obligations, failure
containment, rooting, access, ownership or loans. Exact `extern` productions,
registered annotation identities and target ABI mappings remain specification
work; no P/Invoke or marshalling model is implied.
`extension` remains proposed vocabulary for a scoped extension declaration.
Its productions, restrictions, effects and lowering remain open.
Accessors and `when` guards have bounded
productions above without implying complete effect/representation contracts.
Producer statements use `yield expression;` to emit an element and suspend
the producer, and `return;` to finish it; there is no `yield return`,
`yield break`, or producer `return expression;` form. The yield operand
evaluates once under the selected element contract. Emission does not imply
an ownership transfer or extend a borrowed element's lifetime beyond its
checked dependencies. A nested callable does not inherit producer eligibility
from its enclosing body. This `yield` is not the scheduling operation
`Yield()` and does not promise a thread switch. Producer
execution starts lazily: invocation evaluates arguments and establishes
captured ownership/loan obligations, but neither creating the producer nor
obtaining its iterator runs its body. The first advance starts the body and
runs until an element is yielded, the producer completes, or a failure
propagates. Subsequent advances resume after the previous yield. Argument
evaluation and producer construction can fail at invocation; body effects
and failures occur during advancement. Captured loans retain their permitted
backing lifetimes even if advancement is delayed or never occurs. Element
binding follows the declared access/consumption contract in §3.3;
representation remains library design. `[Iterator]` marks the
implementation; it is not a traversal-direction or element-access flag.
`[Iterator]` composes with `async` for an async producer. Its declared result
must satisfy a checked async-producer contract; `Task<Sequence<T>>` by itself
only represents completion of sequence creation, not asynchronous advancement.
Async producer creation remains lazy, unlike ordinary eager async invocation.
Advancement may suspend at an `await` and resumes under the ordinary checked
scheduling, failure and affinity rules until yield, completion or failure.
`yield expression;` still emits an element and `return;` finishes production;
neither introduces an implicit result wrapper. Cancellation integration
remains library design.
Closing a suspended producer unwinds entered scopes, executes applicable
`finally` bodies and registered `defer` actions, and releases owned state under
the checked cleanup contract; it does not run the remaining production body or enter previously
unentered scopes. Already-completed cleanup is not repeated. Captured owners
still require cleanup when a producer is closed without ever being advanced,
even though its body has not entered any scopes. Async close may suspend only
under its declared contract, retaining the required state and dependencies
until completion. Loop closing is structured as specified in §3.3; manual
consumers retain the corresponding declared close/ownership obligations.
Creation and completion obligations are distinct from per-advance/item
obligations, as selected by the declared producer protocol. A plain callable
clause does not become a per-yield assertion, and body inspection does not
extend an element or capture loan. The protocol publishes dependencies
retained during suspension and through close/completion. Creation checks run
at invocation; advance/item checks belong to the registered advancement
protocol; successful final-completion checks are not per-yield checks. Early
close discharges its cleanup contract without claiming normal completion.
Operation identities, checked phase selectors and clause encodings belong to
the producer registration, not body-inferred timing or similarly named APIs.
Their concrete catalog remains library/metadata specification work.
Reverse traversal and readonly/writable items belong to the selected
sequence/item contracts. Whether variations are exposed as adapters or
explicitly declared policies remains library design. Any selection must
publish the relevant ordering, access, ownership and lifetime guarantees;
it cannot upgrade readonly backing or make readonly item access imply
deep immutability of referenced objects.
`unsafe` has block and
parenthesized-expression forms; default arithmetic remains checked.
`defer` registers an
explicit lexical-block-exit action (not automatic `[Cleanup]`, function-exit
deferral, or delayed live-value destruction). One scope's registered actions
can share a cleanup region, but conditional registration and reverse order
must remain observable. Failures obey the same chaining and propagation
rules as an authored `finally`; `defer` does not introduce unsafe recovery.

`default` selects a generic formal's declared default and introduces `default<I>.Member(...)`,
which selects the named interface's default body directly without inventing a
default value for `I`, stateful base-class inheritance or implicit
linearization. Direct default-body invocation is restricted to the current
implementing `this` inside interface default bodies or a conforming
type/adapter implementation, not arbitrary receivers from outside.
It may apply to an explicit adapter implementing an extension interface, but cannot reach an
unrelated extension or fabricate conformance. Calls made within a default
body still dispatch through the selected witness. Access, effects and diamond
resolution are checked. No `override` keyword is proposed: an explicit
conformance implementation replaces an available default by member identity,
not by a same-named declaration. `pin` qualifies `ref<T>` in `Type`, not
`ptr<T>` or a pinning statement; `pinned` need not also be a keyword.
`field` is available only within its property accessors, as the checked
backing place of that property, not through ordinary member lookup; its write
authority depends on the property's backing-storage contract. Backing fields
occupy the property's position in field order, as do auto-properties: an
explicit field `a`, auto-property `b`, computed property `c`, field-backed
property `d`, and explicit field `e` have storage order `a, b, d, e`.
This order also applies to layouts where field order is observable.
`partial` merges explicitly marked type/member declarations across files
in one module, including generated parts, in the order the compiler receives
the files and then source order within each file. Every merged part must
declare `partial`, have matching kind and generic identity, and yield one
resulting identity without duplicate members or implementations. A partial
member may have a bodyless defining part, but exactly one implementation
body must be provided in the compilation; the implementing part cannot
change its signature, published contract or accessor shape. Explicitly
repeated visibility, modifiers, constraints and contract-affecting attributes
must agree, but need not be repeated on every part. Compatible noncontract
attributes combine in input/source order subject to their multiplicity rules.
A source generator running within the compilation may supply new parts and
implementations, but must not add or alter the published contract of an
existing declaration, including making a method unsafe or changing a
type's layout. Independently pre-generated files have ordinary source-file
status, not the compiler-known provenance of an in-compilation generator.
Partial parts match by declared identity and complete signature, including
generic formal names/order, callable parameters and accessor shape.
Explicitly repeated defaults and contracts must agree after their required
type/constant normalization rather than accumulate stronger obligations;
omitting a repetition does not erase the existing promise. Attribute
multiplicity remains a separate declared schema. A generator cannot silently
remove an API by failing to provide its required implementation; any future
API-absence mechanism needs explicit source permission and a separate
contract. None is introduced here. Ordinary mixed backing is defined in §5.10.

**Policy selection:** policy domains may be Core-defined or
user-defined enums. A policy case is a compile-time preference for a variant
of the method being called, not a hook or interception mechanism. Methods may
declare variants of their own unannotated base declaration. Resolve that
base by ordinary overload rules first. Variants
have the same parameter/result types and ownership/loan shape; effects may
differ by policy. No imported extension can attach a variant to an unrelated
declaration. A `policy<Domain.Case>` scope is a preference, not a guarantee:
for each call it tries the variant of the already selected base declaration
matching the innermost scope's case tuple, then each enclosing scope's tuple,
then the unannotated base. No callee inherits the caller's source policy.
The compiler records the selected variant and its contract; linking and JIT
do not repeat source policy selection. Adding a variant can change selection
on source recompilation, not silently retarget an existing compiled binding.

`policy<A.X, B.Y>` names one atomic tuple with at most one case per enum
domain. It tries the exact combined variant, not automatically A-only and
B-only or any subset. Separate nested scopes do not synthesize combinations:
`policy<A.X> { policy<B.Y> { ... } }` tries B-only, A-only, then base.
Authors can nest an explicit combined scope inside individual scopes to
specify their own fallback order. Lookup is linear in lexical scope depth,
not exponential in the number of active domains. A `require policy<...>`
scope is a strict boundary: calls within it must select a variant satisfying
its complete case tuple, rather than silently using an incompatible variant
or the base. All other policy scopes preserve ordinary fallback.
Variant declarations use the callable's own identity and complete signature
to identify one base; a registered semantic attribute supplies only the exact
policy tuple. Match scope/name, generic formals and complete parameter identity,
then check the full result/access/effect/lifetime contract and policy domains.
An attributed variant is not an ordinary duplicate overload. Ambiguous bases
and duplicate tuples reject; parameter names, hand-authored signature strings
and source order do not select the base. The attribute's registered identity
and wire encoding remain specification work. No policy may
manufacture access, lifetime, initialization, unsafe authorization or a
runtime GC exclusion lease.

Core-defined arithmetic policy cases may have compiler-known overflow
contracts, allowing sound arithmetic optimization for their selected
variants. User-defined policy cases select declared variants by the same
rules, without automatically acquiring Core's intrinsic semantics. A
preference such as `policy<Arithmetic.Wrap>` may fall back to the unannotated
checked operation when no variant matches; use
`require policy<Arithmetic.Wrap>` when wrapping selection is required. The
names of the Core cases remain to be specified.

The `policy<...>` arguments above are compile-time enum values. A generic
`const Arithmetic P = Arithmetic.Strict` formal can forward such a value
into `policy<P>` under definition-site checking. Ordinary runtime enum
parameters require explicit dispatch and
cannot select different source overloads depending on inlining. A general
policy query is deferred until its subject explicitly distinguishes an active
request from a particular call's selected variant. A policy case names the
requested variant; its name does
not by itself impose additional semantic obligations. `require policy<...>`
guarantees selection of a matching variant, not properties beyond that
variant's declared contract.

Type-call conversions use `T(expression)`. The user
conversion declaration `operator T(U source)` fixes its destination type
at declaration; it does not grant raw reinterpretation or implicit
conversion. Conversion ownership, ambiguity, effects and safety constraints
remain open. `operator` already has a token; `cast` and `explicit` need not
be reserved solely to declare or invoke a checked conversion.
`in` marks the iteration form; `and`, `or` and `not` combine
patterns, distinct from Boolean `&&`, `||` and `!`. Full pattern syntax remains
open. Allocation forms and pinning retain their
separate semantic and representation boundaries; `new` already covers the
ordinary construction form.

**Constants and registered queries:** required constant formation uses a
bounded, checked, encodable value domain. Constants in declarations,
attributes and generic arguments must evaluate successfully or diagnose;
they do not silently fall back to runtime execution. Optional runtime folding
is separate and must preserve source behavior.
Compiler-known query identities are explicitly registered contracts, not
arbitrary library names. A query declares its result domain, whether runtime
use is available and exactly which fact it supplies. Target-layout queries
use the selected target ABI, not the evaluator's host, and distinguish inline
value/handle size from object storage size. No universal reflection object,
`SizeOf`/`AlignOf` spelling or constructed-object constant encoding is chosen.
Eligible guaranteed constant domains and frozen/published layouts can supply
stable constants. Today's private layout or constructor implementation is not
a promise: an unfrozen, replaceable representation cannot be baked into an
independently persistent layout-dependent constant. Layout-coupled build facts
require explicit rebuild/invalidation dependencies, not a published constant
guarantee. Reuse the checked primitive-wrapper and nominal contract rules in
§3.2; exact query catalogs and constructed-data encodings remain separate work.

There is no `~Type()` finalizer production. Deterministic
`[Cleanup]` for an owning value and scope/unwind cleanup are not GC
finalization. GC finalizers remain absent unless a concrete API need survives
explicit ownership and supervision alternatives. Any future proposal must
review lifecycle, resurrection, affinity, failure/effects and resource safety
together, apart from `[Cleanup]`. Do not synthesize finalizers from owner
cleanup or class declarations.

### 5.13 Operators, assignments and expression gaps

**Accepted compound-operation contract:** user `operator +=` is separate from
`operator +`; it cannot be desugared to read/copy/write or inferred from
addition. A static, void-returning declaration uses `write ref<S>` to update
struct storage, or `write C` to mutate reference-object contents:

```terrum
public static void operator +=(write ref<S> destination, int32 increment) { ... }
public static void operator +=(write C destination, int32 increment) { ... }
```

These are declaration-shape sketches with omitted bodies, not new body syntax.
The same distinct-contract rule
applies to nominal `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=` and
`>>>=`. Their statement spellings are in §3.3; nominal declaration metadata
still needs completion. A struct destination must satisfy ordinary
writable-place checks, not merely parse as an expression. Built-in
integer shift assignments evaluate the place once and check shift counts.
`??=` is the built-in initialized-optional operation described in §3.4,
not a grant of arbitrary nominal overloading.
For class-like values, writable handle-slot access and writable referent
authority remain independent. `write ref<C>` permits replacing a stored
readonly `C` handle, not mutating that object's contents; `ref<write C>`
permits access through a writable handle without granting slot replacement.
Content-only class operations take an eligible `write C` receiver value,
without requiring a replaceable handle slot. A fixed binding, value-returning
accessor or temporary can supply that value when its declared write authority
and lifetime permit the call; readonly object access cannot.
Handle replacement instead requires explicit writable-slot access.
If content-only and slot-updating candidates are both applicable, lookup must
select one under the ordinary checked ranking rules or diagnose ambiguity,
not invent a winner from static/instance spelling. Destination and operand
evaluation remain once-only and source ordered, with declared effects and no
implicit rollback or owning transfer. Static operand-shaped `++`/`--` remain
distinct; no C# instance-operator machinery is inferred.
Do not split compound operator spellings into an ordinary binary operator
followed by `=`.

**Conversion eligibility:** `new T(...)` selects construction.
For a nonliteral `T(value)` conversion, first resolve applicable destination-
owned declarations; consider source-owned conversions only when the
destination has none applicable. Each selected set must have one unique best
candidate under ordinary ranking. Destination ambiguity diagnoses, and a
selected conversion's failure propagates; neither triggers fallback to the
source. Source binding records one declaration, not runtime trial calls.
Contextual literal construction retains its own checked target-domain rules.
Explicit `Target.via<Source>(value)` or `Target.via<Target>(value)` restricts
conversion lookup to the selected eligible owner without supplying a receiver
argument. The owner must be the source or destination type, not an arbitrary
factory provider. Normal applicability/ranking still applies; a literal needs
one unambiguous selected input context. This is distinct from extension
`value.via<Provider>.Member(...)`, whose receiver is an argument. Ordinary
named factories remain available, but no `Source.Target` method is synthesized.

**Pointer formation and dereference:** `&place` forms an address only with
checked initialized storage, access, lifetime and stable/rooted backing.
GC storage requires a statically known covering pin; `unsafe` is not a
substitute for rooting or pinning. Safe pin-derived pointer conversion retains
the necessary access, pin and backing dependencies; implicit/explicit
conversion and release spelling remain in
[Q26.1](proposals/grammar-open-questions.md#q261-pointer-formation-and-dereferencemember-boundaries).
Dereference uses ordinary attached prefix `*` and requires unsafe access;
it does not create a new `(* ... )` fence. `*p`, `*(p + offset)`,
`*GetPointer()` and `*pointers[index]` retain ordinary prefix/postfix precedence.
When the unparenthesized operand's postfix receiver chain contains member
selection, grouping must identify the pointer expression or pointee boundary:
`(*p).M` and `*(p.M)` are distinct, while `*p.M` and `*GetPointer().M` reject.
Members within call arguments or index expressions are not that receiver
chain. `*p + offset` adds to the dereferenced value; it is not address
arithmetic. `*(*p)` dereferences twice; `**p`, `* *p` and `* p` reject under
the existing operator-run and attachment rules. Grouping supplies neither
validity, bounds, pinning nor write authority.

**Explicitly deferred:** nominal `&&`/`||` short-circuiting remains unavailable
until a concrete need motivates a complete protocol for truth testing,
conditional right-operand evaluation, result and ownership obligations.
The paired `true`/`false` declarations do not adopt C#'s derivation through
`&`/`|`, nor can an ordinary binary witness license eager evaluation of both
operands. Built-in Boolean short-circuit ordering is unchanged.

Still **OPEN** for a full source grammar: nominal safe union cases and complete
patterns, full associated-identity checking and family/adapter declarations,
conditional/default conformance and text-target fragment contracts,
general `const` declarations/expressions,
general ownership/reference results, complete constructor applicability, async completion
and scheduling protocols, producers, capture/exception transport, and registered collection-fragment
construction. Source type packs and tuple arity extensions remain deferred.
Specifying a source form does not complete its open lifetime,
effect, representation or separate-compilation contracts. Hexadecimal float
spelling and target rounding are above; exact lexical recovery/boundaries
remain to specify.

### 5.14 Documentation-comment grammar and tooling

Documentation attaches to the following declaration, including its attributes
and modifiers, not to an intervening attribute's implementation. No blank
source line may interrupt attachment from the documentation run through the
attribute/modifier prefix to the declaration. `/// text` immediately followed
on the next line by `public class Widget { }` attaches; inserting an empty source line
does not. Blank prose lines represented inside a documentation comment remain
content and do not detach it. Partial/generated parts retain their own source
attachment; merging does not manufacture attachment across files.

Parse Markdown before recognizing the finite, case-sensitive top-level
metadata-heading vocabulary in [language](language.md#documentation-comments).
Unknown metadata-looking headings diagnose; headings inside code, lists or
quotes are not directives. References reuse source name/type syntax where
unambiguous and require an explicit signature for overload selection.
Inherited-document selection is explicit by resolved member identity, not
same-name copying or an inferred stronger behavioral contract.

The following details remain **specification/tooling work**, not pending votes
on the attachment and reference principles or a defined XML schema:

| Surface | What remains to specify |
|---|---|
| Attachment | Diagnostics for detached runs, multiple declarations and explicit partial/generated-document merging |
| Markdown | Supported GFM normalization and XML/HTML-style tags; exact recognition of metadata-looking unknown headings |
| Mentions | Escaping and boundaries for `@value`, `@T`, `@Type.Member`, generic and overloaded targets; ordinary Markdown navigation versus explicit custom/qualified `xref:` links |
| XML artifact | Schema/version, stable member IDs, parameter names, location, overload/generic identity and compatibility across evolution |
| Inheritance | Explicit selection of documentation from interface members, aliases and forwarders, not arbitrary name matching; precedence for generated sections and authored prose |
| Diagnostics | Malformed markup, unresolved/ambiguous references, renamed/stale parameters and optional documentation-coverage lint |
| Examples | Explicit source/profile context for complete examples, fragments or design-only pseudocode; checking is opt-in, never executed during extraction |
| Distribution | Package representation, visibility/trimming, dependency availability, deterministic output and IDE/site rendering, separate from executable ABI |

The compiler derives effects, ownership, lifetimes, initialization, output and
failure guarantees from checked declarations. Documentation, including
inherited prose and `# safety` explanations, cannot change those contracts.
Normal extraction is deterministic and does not run embedded code, fetch
remote includes or reveal private declarations merely to resolve a link.

### 5.15 Specification-to-tooling gate

Tooling must implement the approved lexical, grammatical and static-semantic
rules together, including modifier legality and declaration-body restrictions.
Remaining specification work includes exact contextual-token classification,
open lexical boundaries and semantic forms that still lack productions.
Positive and negative source examples should cover each specified boundary;
recovery syntax and informative API examples do not establish new productions.
Implementation support is recorded separately, not inferred from approval.

## 6. Source-form coverage and open boundaries

This index separates concepts for readers; it does not add productions or
approve unresolved forms. The sections named in the middle column give the
source forms or accepted semantic direction. An open boundary
remains unspecified rather than becoming legal merely because a related word
appears in §2.

| Concept | Defined direction | Still to specify |
|---|---|---|
| Lexical tokens | §2 gives keyword escapes, contextual-word positions, explicit operator glyphs/priority, role-based attachment and whole-star comment fences; primitive names are identifiers with reserved bindings. | Malformed floating-token and delimiter recovery; compiler/formatter adoption. |
| Literals | §§2 and 4 give character-scalar and context-typed numeric forms, including hex-float target rounding; §§2 and 3.4 give raw framing/headers, deterministic interpolation, identifier/quoted/expression formats and runtime alignment. | Complete floating token recovery, character diagnostics, raw/interpolation recovery and registered text/whole-output normalization APIs. |
| Files | §3.1 sketches directive order. | Other directive/file forms, if any. |
| Namespace | §3.1 permits file-scoped or nested block namespaces without mixing, with lexically scoped imports before declarations. | Other namespace forms, if any. |
| Visibility | §3.1 permits namespace-level `public`/module-internal declarations and declaring-type `private`; equal namespace names do not cross module access boundaries. | File-limited visibility and the remaining declaration-kind modifier matrix. |
| Nominal types | §3.1 sketches structs, classes, interfaces and integral-base enums with exact constants, checked successor overflow and deliberate aliases. | Union/nested declarations, selected integer catalogs, flags schemas and contextual attributes. |
| Generic declarations | §§3.1–3.2 give bounded constant domains, checked opt-in primitive-wrapper constants, earlier-only defaults, `_` inference and explicit `default` selection. | Wrapper capability spelling/value encoding and complete inference contracts. |
| Aliases | §§3.1 and 5.4 give finite qualified expansion, cycle rejection, namespace-level aliases and transparent public export. | Metadata encoding. |
| Associated types | §§3.1 and 5.4 give requirements, replaceable defaults, inherited identity resolution, qualified bindings, optional `<this T>`, `.via<I>` selection and exact `is` identity constraints. | Complete checking diagnostics and metadata. |
| Partial declarations | §5.12 defines one-identity merge, complete-signature agreement, consistent repeated defaults/contracts and required body provision; §3.1 requires resolved attribute schemas/multiplicity. | Concrete attribute catalogs/encoding and any explicitly permissioned future API-absence mechanism. |
| Core primitive declarations | §2 preserves reserved primitive identities with a privileged backing field. | Artifact identity, permitted members and separate-compilation enforcement. |
| Fields | §3.1 sketches instance fields. | Applicable attribute contracts and layout restrictions. |
| Constructors | §§3.1 and 5.10 give `ensure P is init` minimum guarantees and repeatable unpublished init-property writes. | Completion validation hooks, value/effect compatibility and metadata encoding. |
| Properties | §§3.1, 5.10 and 5.12 give mixed backing, receiver modes, expression bodies and authored indexers. | Implicit storage initialization, ref-result contracts and metadata. |
| Operators | §§3.1, 5.7 and 5.13 describe nominal unary/binary operators and static void compound operations over struct slots or writable object contents. | Complete applicability, checked witness/metadata encoding and tooling adoption. |
| Conversions | §§3.1 and 5.13 give `operator T(U source)`, destination-first `T(value)` and explicit `.via<Owner>` selection, distinct from `new T(...)`. | Complete eligible conversion catalogs and binding/metadata diagnostics. |
| Interface members | §§3.1, 5.4 and 5.12 give explicit qualified implementations, shared implicit implementations and restricted current-receiver default-body bypass. | Complete inherited-default resolution and representation; constructors remain deferred. |
| Types | §3.2 gives recursive types, tuple types and normalized optionals. | General ownership/reference-result forms and public tuple ABI. |
| Ownership | §§3.2 and 5.12 sketch owner forms and explicit transfer. | Complete ownership-form grammar. |
| Reference access | §§3.2 and 5.12 sketch loans and reference qualifications. | Complete legality of `pin`, `write` and other qualifier combinations. |
| Parameters | §§3.2, 5.1–5.2 give modes, final `params`, eligible Copy defaults and direct/named attribute targets. | Complete qualifier legality and output/loan metadata. |
| Arguments | §§3.2 and 5.1 give labels, source-order evaluation and position-preserving named/positional mixtures. | Complete passing-mode/place and overload contracts. |
| Statements | §3.3 gives blocks, transfers, standalone discard and `yield expression;` / producer `return;`; §§3.1 and 5.12 give the `[Iterator]` marker; §3.4 permits await expressions in async bodies. | Statement-place checks and producer protocol/lifetime contracts. |
| Iteration | §3.3 gives post-tested `do` loops and by-value/`ref`/`mut ref` iteration, including `await for` and structured close; §5.12 gives producer unwind. | Iterator loan/backing, protocol identities and cancellation integration. |
| Expressions | §3.4 gives conditional/coalescing/optional access, half-open slicing, end-relative bounds, tuples, target-directed static/async lambdas, await and collection spreads. | Slice/first-class index/range protocols, callable capture/representation, awaiter registration and collection-fragment contracts; comprehensions/repetition deferred for library/tensor design. |
| Patterns | §3.3 gives shared switch arms, guarded matching, compatible `or` bindings and explicit constant/wildcard transfers. | Full nominal-case, relational and destructuring patterns. |
| Policies | §§3.3–3.4 and 5.12 describe lexical preferences, strict selection, signature-bound variants and constant-formal forwarding. | Registered variant attribute/catalog encoding; general policy queries are deferred until their subject is explicit. |
| Contracts | §§3.1, 3.3–3.4 and 5.8 give grouped named/lambda `require`/`ensure`, contextual result `return`, distinct body-level checks and registered producer phases. | Concrete phase selectors and correlated-output metadata; declaration-level static qualifiers are deferred. |
| Static proof | §5.9 describes the portable static-proof floor. | Constant-evaluation and symbolic source forms. |
| Exceptions | §§3.3–3.4 give typed/bare catches, filters, throw expressions, rethrow and explicit captured-failure propagation. | Catch eligibility, capture APIs, transport, unwind phase and chaining metadata. |
| Cleanup | §§3.3 and 5.12 distinguish lexical `defer` from `[Cleanup]`. | Defer action validation and attribute placement. |
| Finalization | §5.12 leaves finalizers absent unless a concrete need warrants a separate reviewed lifecycle contract. | Any future finalizer proposal and its lifecycle/ABI contract. |
| Documentation comments | §§1 and 5.14 describe attachment and structured prose direction. | Exact markup/reference grammar and XML artifact schema. |

The specified lexical, grammatical and static-semantic rules establish
conformance within their stated scope. The [implementation reference](implementation.md)
records supported subsets separately from this specification.
