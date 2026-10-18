<a id="terrum-grammar-proposal"></a>

# Terrum source grammar

This document specifies source notation and selected semantic constraints.
It is a bounded specification, not a complete grammar for every language
feature. Informative examples and library names do not establish additional
productions or contracts.

The [language](language.md), [memory model](memory-model.md),
[conversions](conversions.md), [lifetimes](lifetimes.md), and
[libraries](libraries.md) documents define the related semantic
contracts. The [semantic IL/metadata specification](il-metadata.md),
[core-library proposal](../proposals/core-library-reference.md) and
[IL extension proposal](../proposals/il-extensions.md) are separate documents;
Source productions do not define library APIs or metadata extensions.

Sections [5](#5-open-grammar-and-semantic-choices) and
[6](#6-source-form-coverage-and-open-boundaries) identify unresolved source
choices and scope boundaries. Unapproved library APIs and format extensions
are collected separately in the [proposal index](../proposals/README.md); they do
not add source productions or contracts.

<a id="1-scope-status-and-notation"></a>

## 1. Scope and notation

The notation is EBNF-like: `{ X }` means zero or more, `[ X ]` means
optional, `|` means alternative, terminals are in quotes, and named lexical
tokens such as `Identifier` are supplied by the lexer. Productions and their
stated static-semantic restrictions define conforming source forms.
Unspecified boundaries identify matters outside this specification;
permissive placeholders do not resolve them. Examples are informative and
conformance cannot be inferred from matching an example.

This is a bounded source-language specification. A reserved spelling does not by itself establish a
production; recovery syntax is not a conforming source form.

**Documentation comments:** attach `///` documentation to declarations, with `//* ... *//` as an
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
packaging are outside this specification. Normal extraction does not execute embedded code or fetch remote includes.
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

- [Compilation input](language/source-input.md) is strict UTF-8, with one leading signature consumed and no normalization or replacement fallback; malformed bytes reject before lexing. Already-decoded UTF-16 input is a separate diagnosed entry contract.
- Source positions and token lengths count UTF-16 code units. Identifier starts/continues follow the pinned Unicode 17.0.0 XID properties plus `_`; spelling is ordinal, case-sensitive, and not normalized. Keywords are lower-case ASCII token spellings. Primitive names such as `int32` are ordinary identifiers, except the reserved `void` keyword.
- Whitespace and comments are leading trivia; line breaks include CRLF, CR, LF, NEL, LS, PS. Line/block comments do not nest. EOF owns trailing trivia.
- Unsuffixed decimal, hexadecimal (`0x`/`0X`), binary (`0b`/`0B`) and hexadecimal floating-point forms are described below. Separators may occur between digits and, for based values, immediately after the prefix. One attached leading ASCII sign is part of a number, as is an exponent sign; a sign before a grouped operand remains an operator. Width suffixes are invalid; `10i32` is not a typed literal. `_123` is an identifier.
- Strings use `"..."` with the documented escapes. Interpolated text uses `$"..."` with `{expression}` holes; ordinary strings do not interpolate. Single-quoted character literals denote one decoded Unicode scalar; numeric width suffixes are invalid.
- Lexical diagnostics preserve malformed source spelling; bad characters/surrogates, invalid escapes, unterminated literals/comments, bad prefixes, separators, and attached width suffixes have distinct diagnostic codes in the lexical reference.

The schematic lexical terminals used below are:

```ebnf
Identifier       = lexer-identifier-token | EscapedKeyword ;
EscapedKeyword   = "@", KeywordSpelling ;
Number           = [ "+" | "-" ], UnsignedNumber ;
UnsignedNumber   = DecimalNumber | HexFloat | HexInteger | BinaryInteger ;
IntegerNumber    = [ "+" | "-" ], ( DecimalDigits | HexInteger | BinaryInteger ) ;
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
recovery is outside this grammar.

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

`Number` is one token, including one attached leading sign if present.
`IntegerNumber` denotes its integer subset, not a separate prefix-operator
production. `-128` and `+0x80` are literals; `-(128)` is an operator
expression and `(-128)` groups a signed literal. Grouping is not removed
to create a different token.
Numeric target selection/range and the lack of a default type are semantic
rules (see [literal construction](language/literal-construction.md)).
A hex float requires a binary
exponent (`p`/`P`), so `0x1f` is an integer while `0x1.fp+2` is a floating
token. It has digits on at least one side of the point; the exponent has
decimal digits, and `_` follows the existing between-digits rules. A leading
sign, like the exponent sign, belongs to the token when directly attached.

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
ordinary code from using primitive types. The 26 identities in the
[mandatory scalar catalog](overview/conformance.md#mandatory-scalar-catalog)
are selected, including the width-explicit integer, floating, character and
decimal types. Vector families and additional APIs have their separately
selected admission contracts; a candidate does not acquire a reserved binding
merely by appearing in an example. A reserved or contextual
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
separate compilation are outside this source grammar.
The mandatory scalar binding catalog is independent of optional Core APIs
and of whether a target implements an operation in hardware or software.
Separately admitted primitive families must publish their closed binding
catalogs. Privileged source members must satisfy ordinary checking and cannot
redefine primitive representation or built-in operations. Additional candidate
families are not automatically added to those catalogs.

### Source vocabulary

`void` is a distinct keyword; primitive type spellings are not keyword
tokens. The following vocabulary is grouped by source role. Words in the
context table below are contextual; the remaining words in this vocabulary
are reserved. Productions for some words remain open. Listing a word does not
permit it in an arbitrary context.

| Concept | Intended words | Source role |
|---|---|---|
| File imports | `using` | Bring names into scope. |
| Namespace | `namespace` | Declare a file-scoped namespace or namespace blocks, without mixing them in a file. |
| Visibility | `public`, `internal`, `private`, `file`, `friend` | Restrict declaration access by module, declaring type, source unit or explicit same-module type grants. |
| Nominal declarations | `struct`, `class`, `interface`, `enum`, `union` | Name types; unions declare explicit named cases with optional payload parameters. |
| Type aliases | `alias` | Name a namespace-level transparent type synonym with declaration visibility. |
| Associated types | `type` | Declare an interface requirement or bind it in a conformance. |
| Declaration composition | `partial` | Merge parts of one identity. |
| Extension declarations | `extension` | Declare scoped extensions; exact syntax remains open. |
| External declarations | `extern` | Bind a complete callable to an external implementation under its declared contract. |
| Member kinds | `static`, `operator` | Declare static members and operators. |
| Property accessors | `get`, `set`, `init` | Read, write or initialize properties. |
| Property backing | `field` | Access only the enclosing property's backing place. |
| Receiver and self binder | `this` | Refer to the member receiver and mark an interface's implementing-type formal as `<this T>`. |
| Generic constraints | `where` | State formal parameter constraints. |
| Compile-time values | `const` | Require static evaluation or declare a compile-time value formal. |
| Type inspection | `typeof` | Inspect the supplied complete type identity with `typeof(Type)`; not a universal reflection-object API. |
| Type forms | `void`, `var` | Name the zero-data unit type or infer a non-null local/binding type; `var?` permits a normalized nullable type. |
| Ownership | `unique`, `own`, `move` | Express ownership and explicit transfer. |
| Reference access | `mut`, `ref`, `write`, `pin` | Qualify places, loans and references; pin qualifies `ref<T>` only. |
| Parameter and argument modes | `init`, `implicit`, `params` | Express vacant initialization, implicit loans or collection arguments; not every combination is legal. |
| Branching | `if`, `else`, `switch` | Choose conditional paths or pattern arms. |
| Default selection | `default` | Select a declared generic default or an interface default body. |
| Explicit selection | contextual `via` | Select an existing interface witness, an eligible extension provider, or the source/destination conversion owner after `.`. |
| Patterns | `when`, `is`, `and`, `or`, `not` | Match values and guard arms; inferred bindings use `var`/`var?`, independently of `is init`. |
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
| `via` | After `.` with an attached `<Type>` selector: interface witness, extension provider, or conversion owner according to the selected use. |
| `when` | Pattern-arm guard or catch-filter introducer. |
| `and`, `or`, `not` | Pattern combinators, not ordinary Boolean expression operators. |

Attribute-target spellings such as `module`, `assembly`, `property`,
`method`, `param` and `typeparam` likewise have meaning only in their target
positions; they are not additional reserved keywords. Reserved `return`
also has the designated result-reference meaning in a postcondition; that
does not make it an ordinary identifier elsewhere.

`typeof` is reserved in every position. `typeof(Type)` is the dedicated
type-query production, independent of a same-named user declaration;
`@typeof` is an ordinary identifier with the name `typeof`. It can name a
local, member or type under the usual declaration and lookup rules.
`@typeof(value)` is an ordinary call, not the query, and supplies no built-in
type-identity evidence. The query takes one `Type`, not a value expression,
an argument list or an unapplied generic constructor. Its keyword-introduced
parentheses allow ordinary trivia, including `typeof (T)`; they are not a
postfix call. Ordinary calls through `@typeof` retain call-opener attachment.

`via` remains an identifier outside its selector position. A local `via`,
`via(value)` or `value.via(value)` uses ordinary name binding.
`value.via<T>` starts the selector production, not an ordinary generic member
named `via`; use `value.@via<T>(value)` for that member. Recognition does not
depend on resolving `T` or discovering a witness/provider. Subsequent semantic
checking selects the applicable witness, provider or conversion-owner
contract; invalid selection diagnoses without reparsing as a same-named call.
Escaping grants neither conformance nor provider/conversion authority.

Compiler-recognized query or attribute names such as `SizeOf`, `Pure`,
`ConstEval` and `NoAlias` are not additional keywords. Their resolved identities
and admitted schemas, not lexer spelling, supply their semantics.
[Vocabulary conformance cases](conformance/source-vocabulary.md) distinguish
token/escape recognition, query evidence and all three selector roles.

Future additions must consider both ordinary name use and ambiguities in
real source. Under the [pre-v1 acceptance policy](overview/conformance.md#scope),
reviewed draft changes may replace reservations and recognition without an
automatic new version or old-source compatibility requirement. A selected
stable source-version promise would instead require an explicit boundary for
breaking recognition changes, not silent reclassification within that
promised version. Exact source-version selection is a separate tooling contract.

`_` is a distinct nonbinding token, not an identifier. Vacant initialization
uses `init ref<T>`; arithmetic selection uses policy cases rather than a
separate arithmetic-toggle source form.

<a id="3-candidate-grammar-productions-proposed"></a>

## 3. Grammar productions

The productions below are subject to their stated semantic restrictions and
explicit open boundaries. `Identifier`
means one lexical identifier token; dotted names are written explicitly.
Semantic constraints are stated separately.
Declaration prefixes have fixed role-specific order. Attributes precede
visibility; every primary declaration has explicit visibility. `partial`,
when present, is last before the type-kind modifiers and kind or the
callable's return type and signature. A secondary partial declaration omits
visibility and other contract-prefix modifiers. Duplicates, misordering and
illegal modifier/declaration pairs reject rather than being reinterpreted
through type lookup. Prefix productions give ordered syntactic slots, not
permission to use every slot on every declaration kind.

### 3.1 Source-file structure and declarations

[Names, declarations, and lookup](language/names-and-binding.md) defines
declaration-space collisions, type-name arity selection, access filtering,
and qualified/unqualified candidate groups.
Declaration preconditions form one isolated lexical scope; postconditions
form another. Their bindings can be used by later clauses of the same phase,
not by the authored body or other phase. The
[structural-flow chapter](language/initialization-and-flow.md) defines their
availability and ordinary phase-scope cleanup.

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

TopDeclaration = TypePrefix, ( StructDecl | ClassDecl | InterfaceDecl
                               | EnumDecl | UnionDecl )
               | CallablePrefix, FunctionDefinition
               | AliasPrefix, AliasDecl ;
MemberDeclaration = ConstructorPrefix, ConstructorDecl
                  | StaticConstructorPrefix, StaticConstructorDecl
                  | FieldPrefix, FieldDecl
                  | PropertyPrefix, PropertyDefinition
                  | IndexerPrefix, IndexerDefinition
                  | CallablePrefix, FunctionDefinition
                  | OperatorPrefix, ( OperatorDefinition | ConversionDefinition )
                  | AssociatedPrefix, ( AssociatedTypeBinding
                                      | AssociatedConstantBinding ) ;
InterfaceMemberDeclaration = CallablePrefix, InterfaceFunction
                           | OperatorPrefix, InterfaceOperator
                           | AssociatedPrefix, ( AssociatedTypeRequirement
                                               | InterfaceConstant )
                           | PropertyPrefix, InterfaceProperty
                           | IndexerPrefix, InterfaceIndexer ;
TypePrefix      = { TypeAttribute }, [ VisibilityModifier ], [ "partial" ] ;
TypeAttribute   = Attribute, { ConstraintClause } ;
CallablePrefix  = { Attribute }, [ VisibilityModifier ], [ "static" ],
                  [ "extern" ], [ "mut" ], [ "unsafe" ], [ "async" ],
                  [ "partial" ] ;
ConstructorPrefix = { Attribute }, [ VisibilityModifier ], [ "unsafe" ],
                    [ "partial" ] ;
StaticConstructorPrefix = { Attribute }, "static", [ "partial" ] ;
FieldPrefix     = { Attribute }, VisibilityModifier, [ "static" ],
                  [ "extern" ], [ "mut" ], [ "unsafe" ] ;
PropertyPrefix  = { Attribute }, [ VisibilityModifier ], [ "static" ],
                  [ "partial" ] ;
IndexerPrefix   = { Attribute }, [ VisibilityModifier ], [ "partial" ] ;
OperatorPrefix  = { Attribute }, [ VisibilityModifier ], [ "static" ],
                  [ "unsafe" ], [ "partial" ] ;
AssociatedPrefix = { Attribute }, VisibilityModifier ;
AliasPrefix     = { Attribute }, NamespaceVisibilityModifier ;
Attribute       = "[", [ AttributeTarget, ":" ], AttributeName,
                  [ AttributeArguments ], "]" ;
AttributeTarget = SingletonTarget
                | NamedTarget, [ "(", Identifier, ")" ] ;
SingletonTarget = "type" | "get" | "set" | "init" | "return" | "this" ;
NamedTarget     = "field" | "property" | "method" | "param" | "typeparam" ;
AttributeName   = DottedIdentifier ;
AttributeArguments = "(", [ AttributeArgument,
                     { ",", AttributeArgument } ], ")" ;
AttributeArgument = [ Identifier, ":" ], Expression ;
VisibilityModifier = "public" | "internal" | "private" | "file"
                   | FriendVisibility ;
NamespaceVisibilityModifier = "public" | "internal" | "file" ;
FriendVisibility = "friend", "<", FriendTarget, { ",", FriendTarget }, ">" ;
FriendTarget    = DottedIdentifier,
                  [ "<", FriendArgument, { ",", FriendArgument }, ">" ] ;
FriendArgument  = "_" | Type | ConstantGenericArgument ;
DeclarationModifier = "public" | "internal" | "private" | "file" | "friend" | "static"
                    | "extern" | "mut" | "unique" | "unsafe" | "partial" | "async" ;

StructDecl      = [ "mut" ], [ "unique" ], "struct", Identifier, [ TypeParameterList ],
                  [ ConformanceList ], { ConstraintClause },
                  "{", { MemberDeclaration }, "}" ;
ClassDecl       = ( [ "mut" ], [ "unique" ] | "static" ), "class",
                  Identifier, [ TypeParameterList ],
                  [ ConformanceList ], { ConstraintClause },
                  "{", { MemberDeclaration }, "}" ;
InterfaceDecl   = "interface", Identifier, [ InterfaceTypeParameters ],
                  [ InterfaceBaseList ], { ConstraintClause },
                  "{", { InterfaceMemberDeclaration }, "}" ;
EnumDecl        = "enum", Identifier, [ ":", IntegralType ],
                  "{", [ EnumMember, { ",", EnumMember }, [ "," ] ], "}" ;
EnumMember      = Identifier, [ "=", ConstantExpression ] ;
UnionDecl       = [ "unique" ], ( "struct" | "class" ), "union",
                  Identifier, [ TypeParameterList ],
                  { ConstraintClause }, "{", { UnionCaseDecl }, "}" ;
UnionCaseDecl   = Identifier,
                  ( ";" | "(", UnionPayload, { ",", UnionPayload }, ")", ";" ) ;
UnionPayload    = Type, Identifier ;
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
ConstructorDecl = Identifier, ParameterList, ContractClauses,
                  ( CallableBody | ";" ) ;
StaticConstructorDecl = Identifier, "(", ")", ContractClauses,
                        ( CallableBody | ";" ) ;
ContractClauses = { PreconditionClause }, { PostconditionClause } ;
PreconditionClause = "require", [ RequirementFailureSelection ], ContractPredicate ;
RequirementFailureSelection = "<", Type, ">" ;
PostconditionClause = "ensure", ContractPredicate ;
ContractPredicate = Expression ;
ReturnValueReference = "return" ;
FunctionDefinition = Type, MemberName, [ TypeParameterList ], ParameterList,
                     { ConstraintClause }, ContractClauses,
                     ( CallableBody | ";" ) ;
InterfaceFunction = FunctionDefinition ;
OperatorDefinition = Type, "operator", OperatorSymbol, ParameterList,
                     ContractClauses, ( CallableBody | ";" ) ;
ConversionDefinition = "operator", Type, "(", ConversionInput, ")",
                       ContractClauses, ( CallableBody | ";" ) ;
ConversionInput = { Attribute }, ( Type, Identifier
                | "implicit", "ref", "<", Type, ">", Identifier ) ;
InterfaceOperator = Type, "operator", OperatorSymbol, ParameterList,
                    ContractClauses, ( ";" | CallableBody ) ;
CallableBody    = Block | ExpressionBody ;
ExpressionBody  = "=>", ( ExpressionStatement | AssignmentStatement
                       | DiscardStatement | RequireStatement | ThrowStatement ) ;
FieldDecl       = Type, Identifier, [ "=", Expression ], ";" ;
PropertyDefinition = Type, MemberName,
                     ( "{", GetterDefinition,
                       [ SetterDefinition | InitDefinition ], "}",
                       [ "=", Expression, ";" ]
                     | ContractClauses, ExpressionBody ) ;
AccessorPrefix  = { Attribute }, [ VisibilityModifier ], [ "mut" ], [ "unsafe" ] ;
InitAccessorPrefix = { Attribute }, [ VisibilityModifier ], [ "unsafe" ] ;
GetterDefinition = AccessorPrefix, "get", ContractClauses, ( CallableBody | ";" ) ;
SetterDefinition = AccessorPrefix, "set", ContractClauses, ( CallableBody | ";" ) ;
InitDefinition   = InitAccessorPrefix, "init", ContractClauses, ( CallableBody | ";" ) ;
InterfaceProperty = Type, Identifier, "{", InterfaceGetter,
                    [ InterfaceSetter | InterfaceInit ], "}" ;
InterfaceGetter = GetterDefinition ;
InterfaceSetter = SetterDefinition ;
InterfaceInit   = InitDefinition ;
IndexerDefinition = Type, [ Type, "." ], "this", IndexParameters,
                    ( "{", IndexGetter, [ IndexSetter | IndexInit ], "}"
                    | ContractClauses, ExpressionBody ) ;
IndexParameters = "[", Parameter, { ",", Parameter }, "]" ;
IndexGetter     = AccessorPrefix, "get", ContractClauses, ( CallableBody | ";" ) ;
IndexSetter     = AccessorPrefix, "set", ContractClauses, ( CallableBody | ";" ) ;
IndexInit       = InitAccessorPrefix, "init", ContractClauses, ( CallableBody | ";" ) ;
InterfaceIndexer = Type, "this", IndexParameters,
                   "{", InterfaceGetter, [ InterfaceSetter | InterfaceInit ], "}" ;

TypeParameterList = "<", GenericFormal, { ",", GenericFormal }, ">" ;
GenericFormal   = { Attribute }, ( Identifier, [ "=", Type ]
                | "const", Type, Identifier, [ "=", ConstantExpression ] ) ;
InterfaceTypeParameters = "<", ( { Attribute }, "this", Identifier
                                | GenericFormal ),
                          { ",", GenericFormal }, ">" ;
ConstraintClause  = ConformanceConstraint | AssociatedIdentityConstraint
                  | MemberContractConstraint ;
ConformanceConstraint = "where", Identifier, ":",
                        ConstraintTerm, { ",", ConstraintTerm } ;
ConstraintTerm  = Type | "[", Identifier, "]" | "write" ;
AssociatedIdentityConstraint = "where", AssociatedProjection, "is", Type ;
AssociatedProjection = ( "void" | DottedIdentifier ), [ TypeArguments ],
                       ".", "via", "<", Type, ">", ".", Identifier ;
MemberContractConstraint = "where", AssociatedProjection, ":",
                           Attribute, { ",", Attribute } ;
OperatorSymbol  = "+" | "-" | "*" | "/" | "%" | "&" | "|" | "^"
                | "<<" | ">>" | ">>>"
                | "==" | "!=" | "<" | "<=" | ">" | ">=" | "!" | "~"
                | "++" | "--" | "true" | "false"
                | "+=" | "-=" | "*=" | "/=" | "%=" | "&=" | "|=" | "^="
                | "<<=" | ">>=" | ">>>=" ;
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

Namespace/file-level types, functions and aliases permit `public`, `internal`
or `file`, not `private` or `friend`. `internal` grants access within the
declaring module; `private` grants access to code of the declaring type,
including all its partial parts. Another module declaring the same namespace
does not extend either domain. Linking or unsafe authorization does not
grant friend-module access.

`file` is a standalone visibility word, not `file internal`. It is also
permitted on nominal members and accessors. Its domain is strictly the
declaring compiler source input unit: another partial part of the same type
in a different input does not gain access. Inputs with equal filenames or
paths remain distinct units; a separately supplied generated input does not
inherit another input's permissions. Implementation bodies retain their
actual source unit rather than borrowing their partial primary's origin.

Namespace-level file-local declarations in different units may have the same
qualified name without sharing an identity. A file-local and ordinary module
declaration that occupy colliding name entries and are both visible in one
unit diagnose rather than silently shadowing. Type-name entries use declared
name/arity; same-named callable groups do not merge across the file-local
identity boundary. File-visible nominal
members retain the ordinary no-duplicate-member rules across partial parts.
A file-local identity carries a module-owned semantic source-unit discriminator
in addition to its name; resolved references distinguish the definitions
even when namespace/name text is equal. File paths and optional debug data are
not semantic identity or access authority.

`friend<F, G>` occupies a nominal member's visibility slot. It means private
access by the declaring type across its partial parts, plus access by code
declared in the listed same-module types, across their partial parts. It grants
access only to that member, not every private member. Grants are nontransitive
and cannot name another module's type as the friend principal. Calling a
friend's methods, implementing a listed interface or defining extensions for
it does not confer friendship. A listed interface grants its own authored
bodies access, not its implementing types. Lowering or inlining preserves
declared code ownership rather than transferring access to unrelated code.

Friend targets resolve nominal declaration identities, not runtime objects or
name-string equality. Exact generic arguments are substituted in the owning
type's context: `friend<ListEnumerator<T>>` on `List<int32>` selects
`ListEnumerator<int32>`, not another specialization. A nongeneric target
remains the same helper for every owner instantiation.
An explicit `_` matches one generic argument slot without inference or
capture; `friend<ListEnumerator<_>>` grants that member to every specialization
of the named same-module generic type. Constructor, arity and argument kinds
must still match. Exact arguments retain ordinary type/constant normalization.
`var`, `var?`, captures, guards, runtime predicates and argument packs have
no friend-grant form. This permission wildcard does not change `_` inference
in ordinary generic applications.

Access grants do not bypass mutation, initialization, safety, ownership,
lifetime or containing-type access. Effective-access checks prohibit a
file-local type in a signature or other contract accessible outside its unit.
A hidden implementation may still be returned through an accessible public
interface when that is the exposed contract. Access domains are not one
linear keyword hierarchy; accessor narrowing must respect their actual access
sets. Friendship cannot make a containing type public or expose a file-local
type beyond its own domain.

Optional visibility in type, callable, constructor, property, indexer and operator
declaration prefixes exists only for secondary partial declarations, not a
default access level. Static constructors have a separate prefix without
visibility; their primary is identified by the required initialization-policy
attribute. Other declarations
without visibility must identify a secondary part with `partial`; otherwise
it rejects. Every composed identity has exactly one primary declaration.
Namespace declarations, local bindings, parameters, enum constants, union
cases and accessor introducers are not independent visibility-bearing
primary declarations. An accessor inherits its containing property's/indexer's
visibility unless it explicitly narrows that domain; it cannot broaden it.
`friend<...>` and its generic openers use the ordinary attached-angle rules.

Type heads keep their compound form together:

| Kind | Kind modifiers, in order | Restrictions |
|---|---|---|
| `struct` | Optional `mut`, then optional `unique` | Inline storage; ownership and mutable storage remain distinct. |
| `class` | Optional `mut`, then optional `unique` | Managed handle form; uniqueness does not imply a unique reachable graph. |
| `static class` | `static` is part of the kind | Non-instantiable container without instance members; cannot combine with nominal `mut` or `unique`. |
| `struct union` / `class union` | Optional `unique` | Explicit inline/managed sum storage; no nominal `mut`. |
| `enum` / `interface` | None | No nominal `mut`, `unique` or `static`. |

There is no bare `union` or `static struct` declaration. A type prefix has
no callable `extern`, `unsafe` or `async` slot. For example,
`public partial mut unique struct Lease` and a matching
`partial mut unique struct Lease` keep the kind form intact; the secondary's
kind modifiers identify the same form rather than granting new authority.
An enum primary explicitly supplies its integral base; a secondary enum
part inherits that contract and does not repeat it.

Named functions and methods use attributes, visibility, placement `static`,
linkage `extern`, receiver `mut`, caller obligation `unsafe`, execution
`async`, then composition `partial`, omitting absent slots. Receiver `mut`
requires an instance receiver; it cannot combine with `static` or qualify a
top-level function. On a type, nominal `mut` instead belongs after `partial`
as shown above. Return-type qualifiers remain part of the return type, not
reordered declaration modifiers. Other member kinds use the same ordered
prefix slots only where applicable; their remaining applicability boundaries
do not become legal merely because a prefix parses. Passing-mode and type
constructor productions retain their own fixed word order.

For callables, `extern` is permitted on top-level functions and static methods,
not instance methods or constructors. Instance methods and constructors may
call permitted external bindings under their ordinary checked contracts; a shared prefix
does not introduce an implicit-receiver or constructor ABI.
An `extern` callable is complete through its checked external binding,
ends with `;` and has no Terrum body or secondary implementation. It cannot
combine with `partial` or `async`; an extern member may occur in a partial
type. Returning an eligible awaitable value through a declared external ABI
does not make the callable `async`. A generator implementing an ordinary
partial callable cannot add `extern` to its secondary declaration.

Field prefixes order attributes, visibility, optional `static`, optional
`extern`, optional storage `mut`, then optional caller-`unsafe`, before the
type and name. Fields reject `async` and member-level `partial`; a partial
type may contribute a complete field declaration. A bare ordinary field is
fixed after initialization; `mut` permits later replacement of its stored
value, subject to nominal-storage and receiver authority. It does not grant
referent mutation. For reference type `C`, `write C` permits referent mutation
without slot replacement, `mut C` permits slot replacement without referent
mutation, and `mut write C` permits both. Directly held structs or `val<C>`
require `mut` place authority without an extra referent `write` qualifier;
nominal and member restrictions still apply.

An `extern` field requires `static` and binds foreign data storage, not a
function or an instance-layout member. It has no Terrum initializer and does
not implicitly allocate or initialize Terrum storage. Its binding explicitly
covers compatible data representation, permitted access, validity,
synchronization and storage lifetime. A non-`mut` extern field is a read-only
Terrum access path, not a promise that foreign code cannot change the value;
the compiler cannot infer load stability from the missing modifier.
Concrete data-import schemas and target mappings remain unspecified.
Ordinary static storage follows the
[initialization and load-scoped lifetime rules](#static-initialization-and-load-scoped-storage).
`static` specifies placement and lifetime, not concurrency permission.
Runtime static fields require the same explicit checked concurrency opt-in
as other shared storage, including shared reads. A fixed slot or readonly
access path does not establish permission to share its reachable state.
An access without that contract requires a proven nonconcurrent restriction,
such as an applicable single-threaded execution profile; otherwise it rejects.
Neither implicit thread-local copies nor first-accessing-thread ownership
repairs a missing contract.

Field-level `unsafe` requires an explicit unsafe context for every direct
read, write, move or reference/pointer formation. It expresses caller
obligations, not permission to bypass visibility, type, initialization,
ownership or lifetime checks. Operation-specific access obligations instead
belong on property/indexer accessors.

Fields and constructors occur in class/struct bodies; interface members do
not include fields or constructors. An interface
constant with an initializer is fixed by its declaration; one without an
initializer requires an implementer-supplied `const` binding of that selected
member identity. An unrelated same-named constant does not satisfy it.
Associated type/constant declarations and bindings have only attributes and
visibility before `type` or `const`. They reject `static`, `mut`, `extern`,
`unsafe`, `async` and member-level `partial`: these are type/compile-time-value
identities, not storage slots or callables. A partial enclosing type/interface
may contribute a complete associated declaration or binding. Existing
effective-access and conformance rules still apply; this prefix does not
permit a private interface requirement.
Ordered prefix slots still require context-specific validation.
Repeated visibility and conflicting effects reject. Callable
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
argument-kind eligibility and multiplicity require semantic validation.
Named attribute arguments use the same `name:` labels as ordinary calls.
The resolved schema maps positional/named arguments, rejects duplicates,
unknown labels, missing required arguments and unsupported positional forms,
and supplies its declared defaults. Attribute labels are not assignment
expressions or runtime writes to library properties.
Each resolved attribute identity declares its argument schema, applicable
targets and repetition policy. Value arguments use the ordinary checked,
encodable compile-time constant evaluator, not a separate attribute evaluator
or arbitrary runtime objects. A constant must also match its declared
argument type and encoding. An admitted type-reference schema instead binds
its direct `typeof(Type)` tree; it does not evaluate a type-object constant.
The [builder schemas](language/construction-handlers.md#type-reference-arguments-are-recipes)
retain constructed type recipes over the target declaration's own formals.
Expression-level `typeof` alone supplies neither attribute type encoding,
open-generic type literals nor a first-class type-object API.
Repeated placements merge in source order only
where that identity permits repetition; otherwise they reject. The [literal schema](language/literal-construction.md#checked-literal-record)
defines its concrete kinds, owner, signature and multiplicity; other semantic
registrations and physical metadata have their own catalogs.
The [Pure/ConstEval schemas](language/published-predicates-and-effects.md#checked-annotations-and-forms)
define their no-argument, nonrepeatable callable/accessor placement, primary
contract authorship and evaluation-form promises. Their mode queries are
resolved Core intrinsic properties, not new tokens or runtime mutable fields.
The [NoAlias schema](language/published-predicates-and-effects.md#noalias-parameters)
is no-argument, nonrepeatable and parameter-targeted, including exact
`param(name)` selectors. It specifies checked accessed-region separation,
not a type qualifier, exclusive loan or overload discriminator.
The resolved [ThreadTransferable/ThreadShareable schemas](runtime/sharing-and-handoff.md#explicit-source-opt-in-and-conditional-contracts)
are no-argument, nonrepeatable, primary nominal-storage-type opt-ins.
Only these schemas admit TypeAttribute's following ordinary constraint
clauses. Before the next attribute or visibility/type prefix, those clauses
belong to that capability, bind the annotated type's exact formals, and do
not constrain type formation. Unmet conditions remove the capability fact.
Type-header constraints remain type-wide. No `where [Capability]` selector,
runtime predicate argument or same-named user attribute selects this behavior.
The resolved [Exception schema](language/local-failures.md#1-registered-exception-capability)
is no-argument, nonrepeatable and primary-only for nominal storage types and
declared catch-category interfaces. Explicit category witnesses can supply
nominal opt-in, and `where T : [Exception]` checks the actual complete form.
This schema admits no following capability-specific constraint clauses and
grants no Copy, payload layout or constructor.
The resolved [RequireException schema](language/requirement-failures.md#3-callable-owned-defaults-and-the-core-payload)
has one required direct typeof(Type) recipe named ExceptionType and is
nonrepeatable, primary-callable/accessor-owned. It selects that owner's runtime
require default; exact get-targeted placement denotes the getter, not a
property-wide or inherited default. Ordinary named argument mapping applies.
Explicit `require<E>` wins; otherwise Core RequirementException is the fallback.
The recipe supplies neither a constructor capability nor type-property
constraint, and adds no lambda attribute.
The resolved [TextBuilder/CollectionBuilder schemas](language/construction-handlers.md#target-owned-builder-schemas)
each take one direct `typeof(Type)` recipe and apply to the target's primary
type contract. Ordinary substitution selects the constructed builder;
registration authority/conflicts and complete operation admission are
separate from attribute spelling.
The resolved [TextTarget schema](language/construction-handlers.md#texttarget-parameter-schema)
is no-argument, nonrepeatable and parameter-targeted, including exact
`param(name)` selectors. It supports typed eager IFormattable content
construction for eligible value/implicit-reference inputs, not a
callable attribute with a parameter-name argument or deferred hole execution.
The [consuming builder completion](language/construction-handlers.md#consuming-builder-completion)
contract retains one exact Core IBuilder witness/member and by-value builder
transfer with its checked init-result/cleanup relation. It is not discovered
from an instance TryBuild method or a similarly named user interface.
The [source factory](language/construction-handlers.md#parameterless-builder-acquisition)
uses the exact Core IBuilderFactory static requirement with vacant builder
output and no ordinary input arguments. Runtime context-taking construction
uses an explicit API, not caller-local capture by a type annotation.
Compiler-recognized semantic attributes obey the same resolved-identity and
schema rules. Their library definitions provide argument names, types,
defaults and applicable targets; the compiler checks the registered schema
and maps its checked values to the designated semantic metadata shape.
Neither arbitrary same-named attributes nor a changed library schema can
redefine that mapping. The registered
[Layout/Blittable schemas](targets/windows-x64-data-layout.md#4-registered-source-layout-schemas)
define primary-only, nonrepeatable type contracts. Layout has a required
LayoutKind and optional named uint32 Packing/Alignment controls through
ordinary attribute arguments. Unsupported target combinations diagnose;
Auto/Transparent reject custom controls, and nested explicit alignment
cannot be weakened by packing.
Packing or alignment alone does not grant `Blittable` or freeze a layout.
Attribute schemas also govern placement on secondary partial declarations.
Secondary attributes are permitted unless their resolved schema disallows
that placement, and combine only under the ordinary target and multiplicity
rules. Contract-changing semantic attributes must enforce primary-only
contract authorship; a schema cannot authorize a secondary to change layout,
calling obligations or another primary contract. Attributes on a new member
introduced in a secondary type part belong to that member's own declaration.
Attributes may attach directly to their target or identify a named target on
the enclosing declaration: `[param(count): Attr]` and `[typeparam(T): Attr]`
bind the same formal identities as inline attributes. A selector must resolve
exactly one applicable target, and placements share multiplicity and partial
merge rules. An untargeted callable attribute applies to the callable, not
its result; result attributes use `[return: Attr]`. No selector performs
overload guessing.
The [generated-producer contract](language/generated-producers.md) supplies
resolved no-argument `[IteratorCursor]` on an owned primary namespace-level
`mut unique struct` and `[Iterator]` on its sole same-module completed producer
callable with a block body. The declared result is that exact named shell.
The compiler supplies its private state and selected direct cursor members;
optional bodyless declarations of those exact members author their contracts.
No arbitrary same-named attribute, sequence result, Task<Cursor>, hidden
result inference or foreign type augmentation selects this generation.
Primary ownership, generic formal mapping, mode and capability checks belong
to that chapter; producer statements and phase summaries are in §5.12.
Core's privileged primitive declaration uses `StructDecl` and `FieldDecl`
without a separate grammar production; its binding and representation
restrictions are semantic checks, not lexical exceptions.

The [owning-field Cleanup schema](language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement)
is resolved, no-argument, nonrepeatable and primary-member-owned. It selects
one eligible unique type's synchronous parameterless readonly void instance
operation with checked NoThrow/nonsuspending retirement. Name matching or
ordinary direct invocation cannot select or repeat that destruction path.

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
An individual runtime precondition may write `require<E> predicate`.
Its optional failure selector uses the
[ordinary construction/default contract](language/requirement-failures.md),
without changing predicate typing, clause order or declaration/body scope.
`ensure`, static proof and the require-policy marker receive no such selector.
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
an applicable external binding or partial declaration supplies their
implementation; a bodyless partial function must have exactly
one matching implementation in the compilation. Exact cursor-slot declarations
on an admitted IteratorCursor shell are the separate restricted generated
implementation case, not a general bodyless-method exemption.
A concrete property accessor
ending in `;` uses checked compiler-generated backing storage; an explicit
accessor needs a body. Interface conformance members may end in `;` to require
a conformance implementation. A body on an interface conformance member
supplies a default. For each inherited conformance member, a checked nominal
conformance maps its declaring-interface member identity to a
compatible explicit implementation when present, otherwise to its default
body; if neither exists, the conformance is incomplete. An explicit
implementation may replace a default without changing the member identity or
weakening its published contract. Same-named extensions do not satisfy or
replace a witness.
Interface requirements may be `public`, `internal`, `file` or `friend<...>`.
A public interface can be consumed through its accessible operations and used in constraints while
an internal requirement restricts new conformances to its defining module.
File requirements restrict new conformance declarations to the requirement's
source unit; friend requirements restrict them to explicitly authorized type
code. The conformance primary authors its conformance list under those access
rules. Inherited requirements preserve every such restriction; interface
composition does not reopen implementation authority. Controlling the
implementers lets
a module evolve requirements and its implementations together, but does not
make an otherwise incompatible ABI change compatible. Private interface
members are complete implementation helpers owned by the declaring interface,
not conformance requirements or replaceable defaults. A private member cannot
require a conforming type to supply its implementation or participate in
conformance witness mapping. Its access remains declaring-type `private`,
not module access or access inherited by another interface.
These are interface contract and witness rules, not concrete inheritance (see
[§5.12](#512-modifiers-cleanup-and-finalization)).

Operators belong to the declaring nominal type or an interface contract,
**not** a global declaration. Retroactive behavior for another type needs an
explicit adapter or wrapper witness, not a global extension operator. A
conversion declaration `operator T(U source)` is a separate nominal member
form with result `T` and one `U` input, either by value or borrowed as
`implicit ref<U>`, invoked explicitly as
`T(source)`. It requires `static` and has no separate return-type prefix or
conversion keyword. `new T(...)` selects construction separately from
conversion type-calls. Destination-first conversion eligibility and explicit
owner selection are specified in §5.13; declaration order cannot break ties.
All user-defined operators and conversions require explicit `static` on
their primary; there is no implicit instance receiver or receiver `mut`
modifier. Mutation authority comes from the explicit operand access contract.
Their prefixes order attributes, ordinary member
visibility, `static`, optional caller-`unsafe`, then optional `partial`.
Lookup considers accessible operations under the ordinary access rules.
An unsafe operation requires an explicit unsafe context at its use site,
including operator syntax and `T(value)`; its body is not implicitly unsafe.
Private interface operators are complete helpers, not witness requirements.

Operator operands may use `implicit ref<T>` under the existing borrowed-slot,
qualification, lifetime and overload rules. Operator operand-shape constraints
apply to the logical operand type `T`, not a fabricated unrelated reference
operand. A conversion likewise accepts by-value `U` or `implicit ref<U>` as
the same logical source type, retaining ordinary by-value-versus-implicit-
reference ranking. Borrowing does not copy the payload or select a size-based
ABI adaptation, grant stronger access, erase dependencies or permit implicit
user-defined conversion chains.

Operators/conversions are synchronous and reject `async`; syntax never
introduces an implicit await. An ordinary awaitable result is permitted only
where the operation's existing result-shape contract accepts that type.
Extern operator/conversion binding has no source form without a separate
concrete interop design; authored implementations may call permitted extern
named methods.
A partial primary owns operand/result types, passing/access modes, safety
and conditions. Exactly one matching implementation supplies its body without
changing the contract. A bodyless concrete operator/conversion is legal only
as that partial primary; the secondary omits visibility, `static` and safety
modifiers and uses the primary's resolved contract.
A bodyless nonpartial public/internal static interface operator is a required
compile-time witness; a bodyful one supplies a default. Static operator
witness selection needs no runtime vtable. The generic type parameters and
conformance lists
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

Nominal union cases use semicolon-terminated declarations: `Missing;` has no
payload, while `Found(T value);` declares named type-first payload parameters.
Case names are unique, not overloadable; a payload parameter list is nonempty.
Qualified construction uses `Lookup<T>.Missing` or `Lookup<T>.Found(value)`,
with ordinary target-checked argument evaluation. The no-payload form is a
case construction expression, not a promise of constant eligibility or a
cached singleton. Payload names do not implicitly declare public fields,
getters, equality, formatting, deconstruction or withers. These intrinsic
named case constructors are not general record synthesis.

`[ClosedCases]` is the parameterless compiler-recognized semantic attribute
for an explicit closed case-set promise on a union declaration. Omission
retains the evolvable default. Recognition uses the registered library
attribute identity and checked schema, not arbitrary same-named attributes;
the exact library definition and metadata encoding belong to the library/metadata contracts.
Closed cases do not freeze representation, layout or the rest of the API.
Union declarations specify `struct union` or `class union`, optionally
preceded by `unique`. An ordinary struct union is an inline discriminated
value whose possible payloads must all support its promised Copy contract;
the unique form transfers noncopyable inline ownership. An ordinary class
union uses managed handles, so copying aliases the same object rather than
copying its active payload; a unique class union uses a unique owning handle
under the ordinary unique-class rules. Case validity, active-payload access
and cleanup remain checked in every form. Changing storage or ownership is
a contract change, not an inferred representation optimization. There is no
implicit storage default, nominal `mut union` or raw overlapping-layout
permission. Generic constraints retain their separate contracts.
No `case` keyword is introduced. Pattern/extraction forms follow §3.3;
this declaration does not select tag numbers, native exports or a public
payload accessor surface.

```terrum
[ClosedCases]
public struct union Lookup<T> where T : [Copy]
{
    Missing;
    Found(T value);
}

Lookup<int32?> missing = Lookup<int32?>.Missing;
Lookup<int32?> found = Lookup<int32?>.Found(null);
```

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
`field`; concrete implementing indexer accessors require bodies. Interface
conformance accessor semicolons instead declare implementation requirements;
a private interface accessor must be a complete helper.
Named properties may be instance or static; `this[...]` indexers are
instance-only. Property prefixes order attributes, visibility, optional
`static`, then optional `partial`; indexer prefixes omit `static`.
Receiver and caller-safety modifiers belong on individual accessors, not
the containing member prefix. Getter/setter prefixes order attributes,
optional narrowing visibility, optional `mut`, then optional `unsafe`;
init-accessor prefixes omit `mut`. Receiver `mut` requires an instance
accessor. Each `get`, `set` or `init` independently declares caller-safety
obligations for the selected operation. An unsafe declaration does not
make its body an unsafe context; unsafe operations still require explicit
authorization within the body.
Properties/indexers and their accessors are synchronous and reject `async`.
A getter may return an awaitable as an ordinary value; asynchronously
executing operations use named methods. Extern properties/accessors have
no source form without a separate concrete interop design; authored accessors
may call permitted external bindings. A static init accessor is available only
during its owning group's unpublished initialization phase, as specified in
§5.10; ordinary later assignment requires an applicable setter.

A partial property/indexer primary owns its value/index types, accessor
set, accessor visibility, receiver/safety contracts and conditions.
Its accessor semicolons describe the contract, not automatic backing storage.
One matching secondary supplies the implementation without adding or
changing accessors or contracts. Secondary accessor introducers omit
visibility, receiver and caller-safety modifiers; their bodies satisfy the
primary's resolved contracts. Bodyless concrete indexer accessors are legal
only in such a partial primary, not as automatic per-index storage.
An expression-bodied property/indexer uses the ordinary getter with inherited
visibility and no explicit receiver/safety modifiers. A getter needing such
modifiers or narrower visibility uses the accessor-list form.
An expression body returns its expression for value-producing members and
executes one legal statement for void methods, setters, init accessors and
constructors. It is shorthand for the corresponding checked block, not a
general assignment-expression feature. Constructors name their enclosing
type and must still establish their published completion guarantees.
Instance constructors perform synchronous initialization and complete before
publishing the instance. They have initialization-phase write authority over
their unpublished receiver, not a method's writable-live-receiver requirement.
Their prefix orders attributes, explicit primary visibility, optional `unsafe`,
then optional `partial`; `static`, `extern`, `mut` and `async` are invalid.
Asynchronous instance creation uses an ordinary async factory under its
declared suspension and publication contracts, not suspension inside a
constructor.
A partial constructor follows sole-primary ownership: its primary authors
visibility, defaults, conditions and completion guarantees. A bodyless
constructor is legal only as a partial primary with exactly one matching
implementing secondary. The secondary omits visibility and caller-unsafe
modifiers, repeats the matching signature, and must establish the primary's
completion guarantees. An ordinary constructor requires a body.

<a id="static-initialization-and-load-scoped-storage"></a>

**Static initialization and load-scoped storage.** Each loaded closed nominal
type with ordinary static storage or an authored static constructor has one
type-wide initialization group.
Different generic instantiations and independently loaded type identities have
distinct storage and initialization state. The group contains ordinary
non-lazy static fields and property backing storage, plus any authored static
constructor body. Constants and foreign `extern` storage do not acquire
Terrum initialization obligations through this rule. There is no implicit
zero/default value that makes vacant storage live.

An authored static constructor names its enclosing type, has no parameters or
result type, and has the ordered prefix attributes, `static`, optional
`partial`. It has no visibility, caller-`unsafe`, `extern`, receiver `mut` or
`async` modifier and is not ordinarily callable. Unsafe implementation work
still requires explicit unsafe scopes and must satisfy the safe initialization
contract. Each composed type identity has at most one static constructor.
It uses the ordinary grouped `require`/`ensure` clauses.

The primary static constructor requires the resolved compiler-recognized
semantic attribute `[StaticInitializer(...)]` with an explicit trigger choice:
grouped-static-field access or runtime static-member access. There is no
default trigger inferred from the presence of its body. Type names, metadata
inspection and compile-time member binding are not runtime access triggers.
The attribute takes exactly one positional compile-time value of a dedicated,
closed enum with one case for each accepted trigger choice; it has no default.
The compiler maps the selected case to the normalized trigger tag, not to a
runtime policy derived from the enum's underlying bits. The registered enum
identity and source case spellings remain attribute-catalog details. An
arbitrary same-named attribute cannot select this behavior.
With no authored static constructor, a synthesized group triggers on first
runtime access to a grouped static field without requiring a source annotation.

A partial static constructor has exactly one primary policy/contract author
and one implementation. The required policy attribute identifies the primary
instead of visibility. A bodyless primary ends in `;`; the secondary repeats
`static partial` and the matching empty-parameter signature but omits the
initialization policy, conditions and other contract-changing items. A
non-partial static constructor requires a body. Other secondary attributes
obey the ordinary resolved schema rules.

Ordinary static initializer expressions execute in the existing merged
declaration order: compiler-received source-input order, then source order
within each input, including property backing at its declaration position.
The primary part has no ordering priority. These initializers precede the
static constructor's preconditions, body and normal-return postconditions.
The static constructor and checked helpers have initialization authority for
vacant group storage, not new permission to replace fixed live fields.
Successful group completion establishes all required initialization and
completion guarantees before publishing the group to ordinary callers.
Missing initialization diagnoses; no caller can read vacant storage to
determine its state.

`[LazyInitialization]` is a compiler-recognized semantic attribute selecting
an individually demand-initialized ordinary, non-`extern` static field,
outside the ordinary group initializer sequence. It takes no arguments and
does not configure a trigger or other initialization policy. Its initializer
remains subject to the ordinary type, effect, access and completion checks.
An external access completes the type group before initializing the requested
lazy field. Other lazy fields remain untouched unless actually accessed.
Initializers and checked helpers may access already-initialized fields within
their unpublished initialization authority; this is not permission for
ordinary reentrant callers to observe partial group state. A completion
demand creating a cycle among unfinished units fails with exact Core
InitializationCycleException rather than waiting indefinitely or exposing
an invented default. A permitted independent wait is not itself a cycle.

The default initialization execution protocol permits one execution flow to
initialize a unit while competing permitted callers wait for its outcome.
Completion publication supplies the memory-model guarantees required by the
declared sharing contract; the gate does not authorize sharing or synchronize
subsequent field or referent mutation. The body need not be pure. Alternative
execution-policy schemas must separately establish valid initialization,
publication, ownership and failure behavior; removing blocking is not
permission for unchecked races or unvalidated duplicate initialization.

Initialization follows the existing exception policy. Fail-fast termination
promises no cleanup. Defined unwinding cleans acquired partial state without
publishing a live unit or rolling back external effects. A failure escaping initialization
leaves the unit failed and unpublished. Its initializing flow receives the
actual escaping event, including ordinary cleanup replacement and causes.
The unit retains failed state, not shared responsibility for that event.
Waiting or later completion demands construct fresh Core
InitializationFailedException events without rethrowing/copying the original
payload, adding an implicitly shared cause or automatically retrying.
The [standard category contract](runtime/standard-failures.md#5-static-and-lazy-initialization-failure)
owns these mappings. A failure caught within the initializer need not fail
a unit that subsequently completes all required obligations. A lazy-field failure
after group completion fails that lazy unit, not the completed group. If its
failure escapes during group initialization, the group fails as well.

An explicit unsafe recovery API may retry a failed unit only with exclusive
recovery authority, completed partial-state cleanup and no dependencies
invalidated by the retry. It is not a reset of a live initialized unit.
An explicit unsafe suppression API may bypass initialization execution;
suppression alone does not establish checked initialized-state facts. Any
unsafe assertion of valid state retains the caller's validity, lifetime,
ownership and publication obligations; an incorrect assumption can cause
undefined behavior. Concrete recovery/suppression APIs and their schemas
remain separate library work.

Static state belongs to its load scope, not an inherently process-immortal
lifetime. Checked unload requires quiescence and discharge of active calls,
loans, roots, pins and ownership dependencies before invalidating storage or
code. Applicable cleanup retires acquired values under the ordinary lifetime
and ownership rules; code needed by cleanup remains available until those
obligations complete. Removing the image is not resource cleanup.

AOT/JIT compilation, precomputation and hoisting preserve observable
initialization timing, effects, failures, identity, ownership, cleanup and
the memory model. Purity of immediate computation alone does not establish
all these facts. Stricter source trigger selection does not forbid sound
semantics-preserving optimization. Initialization requirements and effects
are part of the triggering operation's published contract; a scalar read or
otherwise effect-bounded static method cannot hide throwing, allocation or
blocking initialization behind its ordinary operation.

Nested type declarations are outside this
grammar; this is a scope boundary, not a claim that nested type
arguments in `Type` are excluded. See [§5.11](#511-nested-types-and-scoped-operators).

```terrum
// Informative interface example.
public interface IAdd<T>
{
    public static T operator +(T left, T right);
    public int32 Size();
    public int32 DefaultSize() { return 0; }
}
```

### 3.2 Types, parameters, and arguments

The [complete-type chapter](language/types-and-normalization.md) defines
formation, qualification, finite forward substitution, and normalized
identity. Grammar admission alone grants no representation or value conversion.
The [call chapter](language/calls-and-inference.md) owns argument mapping,
finite inference, applicability and ranking for these source forms.

```ebnf
Type            = [ "write" ], [ "pin" ], ( NamedType | TupleType ), [ "?" ] ;
NamedType       = ( "void" | "ref" | "own" | DottedIdentifier ), [ TypeArguments ],
                  [ ".", "via", "<", Type, ">", ".", Identifier ] ;
TupleType       = "(", TupleTypeElement, ",", TupleTypeElement,
                  { ",", TupleTypeElement }, ")" ;
TupleTypeElement = Type, [ Identifier ] ;
ParameterList   = "(", [ Parameter, { ",", Parameter } ], ")" ;
Parameter       = { Attribute }, ParameterBinding ;
ParameterBinding = ParameterType, Identifier, [ "=", ConstantExpression ]
                 | "params", Type, Identifier ;
ParameterType   = [ "mut" ], Type
                | [ "mut" ], "implicit", "ref", "<", Type, ">"
                | "init", "ref", "<", Type, ">" ;

Arguments       = "(", [ Argument, { ",", Argument } ], ")" ;
Argument        = [ Identifier, ":" ],
                  ( Expression | "init", Expression | "init", "var", Identifier ) ;
```

`void` is the inhabited zero-data Copy type with value `()`, not just a
return-type marker. It is legal in ordinary value positions, including
parameters, locals, fields, tuples, and eligible generic arguments.
It retains logical arity and initialization state without payload bytes.
The [value/execution chapter](language/expressions-and-statements.md#the-zero-data-void-value)
distinguishes unit values, nonaddressable unit places, and explicit discard.

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

The [type-name arity rules](language/names-and-binding.md#type-name-arity-selection)
select a declaration before omitted generic positions are filled. Argument
count and formal kinds still have to match that selected declaration.
Nested generic closing delimiters retain their type-context recognition, so
`List<List<T>>` does not require separating its closers. Comparison `<` and
`>` remain spaced binary expression operators, not generic delimiters.

Direct constant generic arguments are literals or qualified constant names;
other constant expressions require grouping. Use `InlineArray<T, N>`,
`InlineArray<T, (N >> 1)>` and
`InlineArray<T, (N >= Minimum ? N : Minimum)>`.
Signed literals are direct arguments: `G<-1>` and `G<+0x80>` use an explicit
generic-opener/signed-number boundary, not the unassigned expression
operators `<-` or `<+`. Grouped-operand negation such as `(-(N))` remains
an expression and requires the expression delimiter. This boundary does not restrict ordinary default
expressions or interpolation conditionals. The selected formal still checks
its eligible constant domain: syntax for a literal does not admit strings,
nulls or other values into the initial integer/Boolean/nominal-enum domains.

Designated keyword-introduced angle lists use the same opener attachment
and flexible interior trivia: `default<I>`, `.via<I>`,
`policy<Domain.Case>`, `require<E>` and `init ref<T>` require `<` to adjoin
their introducer. `default <I>`, `.via <I>`, `policy <Domain.Case>`,
`require <E>` and `init ref <T>` reject. This applies to the
reference-type/loan constructor
`ref<T>`, including its separately worded `init` and `implicit` forms.
Separate words retain their normal boundaries: `init ref<T>` is not
`initref<T>`. Attachment does not change witness selection, policy
selection, requirement operand typing, initialization or reference authority.

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
separate semantic rule. In these productions, `mut` permits rebinding the
parameter's own local place, not writing its referent or the caller's slot.
It is not a type argument. Grammar does not itself grant a
particular lifetime, write authority, representation conversion, or generic
variance.

The standalone constraint term `write`, as in `where T : write`, requires
the [normalized complete type argument's immediate write access](language/types-and-normalization.md#generic-write-requirements).
It neither qualifies a readonly argument nor supplies a universal mutation
protocol. It can accompany ordinary requirements:
`where T : write, [Copy], IReset<T>`.
The formal declaration remains `<T>`; `<write T>` is not shorthand.
`F<write T>(value)` remains a qualified argument at a call site, checked
under the caller's ordinary formation guarantees. `where T is write` is
not a constraint production; associated `is` retains exact type equality.

Generic type and `const Domain P` value formals may have declared defaults,
and defaulted and required formals may interleave. Explicit arguments pin
their positions; typed arguments, structural evidence in known tuple/collection
shapes and eligible delayed typed callable results establish types before
expected-result context fills unresolved positions.
Then declared defaults fill omitted unresolved positions before complete
contextual literal/lambda checking. Only known callable families and established
inputs permit delayed body evidence; it does not guess an input or literal
width. By-value evidence combines the strongest common admitted immediate
access over the same complete stem; invariant storage evidence stays exact.
After expected/default fallback establishes an input, resume the finite
typed-evidence worklist before selecting further defaults. Defaults are
declaration-ordered; established types do not change. An explicit `_`
requires inference and cannot fall back to a default. `default` explicitly
selects one formal's declared default and rejects if none exists.
Omitting a suffix or the whole list may use defaults; an empty comma slot
such as `Make<, int32>()` is invalid. Defaults must satisfy the formal's
constraints; they do not rescue contradictory or ambiguous inference.
The [constant-generic domain chapter](language/constant-generics.md) owns the
initial fixed-width integer, Boolean and admitted nominal-enum catalog.
Eligible one-field integer/Boolean wrappers derive participation from their
checked Transparent contract plus existing construction, validity, frozen
Copy, Blittable and value-codec obligations; no second marker or protocol is added.
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
not an observation of replaceable private fields or a constructor body.
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
Canonical generic identity is exact complete domain plus carrier value,
without executing user equality/hash operations. Physical generic/value
encoding remains separately gated. Attribute-encoded
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
call-site spelling `Create(init value)`. `write` qualifies access
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
Initializing vacant pin storage acquires the new pin before publishing the
value; acquisition failure leaves that destination vacant and cleans any
partially acquired state under the ordinary initialization rules. Replacing
an eligible live mutable pin binding keeps its old value and pin valid while
evaluating and acquiring the replacement. Failed acquisition leaves the old
value and pin in place. Successful replacement publishes the acquired value
and retires the old pin only when its outstanding dependencies permit that.
Neither staging nor successful acquisition bypasses those checks or permits
early unpinning. This does not roll back initializer side effects.

Reacquisition uses the existing vacant-initialization or mutable-assignment
authority, not a dedicated release/rebind keyword. An ordinary immutable pin
local can end its value lifetime but cannot be assigned again. For eligible
initialized backing places `first` and `second`, the target forms include:

```terrum
init pin ref<int32> pinned;
pinned = ref first;
pinned = _;
pinned = ref second;

mut pin ref<int32> current = ref first;
current = ref second;
```

The narrow implicit pin-to-pointer conversion and prefix dereference are
specified in §5.13. A named local's current value can end through `name = _;`
under §3.3; §5.13 also specifies pointer-formation diagnostics.
Pinning a slot containing a class handle does not also pin the separately
allocated object reached through that handle. `mut` qualifies a binding or
receiver, not a separate reference passing mode. `write ref<T>` is an ordinary
complete reference type admitted in `ParameterType`; `init ref<T>` is the
distinct initialization-destination contract. `implicit` applies only to
an unpinned readonly outer `ref<T>`; its `T` keeps its complete qualifications.
`mut init ref<T>`, defaults on reference or initialization-loan parameters,
and parameter-only `ref T`, `mut ref T`, or `out T` reject. `ref` and `own`
are reserved constructor introducers requiring exactly one type argument,
not ordinary identifier tokens. See
[references and signature identity](language/references.md).

### 3.3 Statements and local declarations

[Values and statement execution](language/expressions-and-statements.md)
defines expression categories, explicit result consumption, condition
eligibility, ordinary loop order, and return/fallthrough checking.

The [local declaration-space chapter](language/local-declarations.md) defines
name collisions, scopes, and points of declaration. Name lookup and value
availability are distinct: a visible name may still denote vacant storage.
[Initialization and structural flow](language/initialization-and-flow.md)
defines the required transfer/join rules and pattern-binding storage.

```ebnf
Block           = "{", { Statement }, "}" ;
Statement       = Block | IfStatement | LoopStatement | SwitchStatement
                | RequireStatement | StaticRequireStatement | StaticEnsureStatement
                | TryStatement | UnsafeStatement
                | DeferStatement | PolicyStatement
                | ReturnStatement | YieldStatement | ThrowStatement | LoopTransfer
                | SwitchTransfer | LocalDecl | DeconstructionDecl
                | DiscardStatement | LifetimeEndStatement | AssignmentStatement
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
StepExpression  = Expression | Assignment | ResultDiscard ;
ForEachStatement = "for", "(", [ "mut" | "ref" | "mut", "ref" ],
                   ( InferredType | Type ), Identifier,
                   "in", Expression, ")", Block ;
AsyncForEachStatement = "await", ForEachStatement ;
RequireStatement = "require", [ RequirementFailureSelection ], Expression, ";" ;
StaticRequireStatement = "static", "require", Expression, ";" ;
StaticEnsureStatement = "static", "ensure", Expression, ";" ;
TryStatement    = "try", Block, ( "finally", Block
                  | CatchClause, { CatchClause }, [ "finally", Block ] ) ;
CatchClause     = "catch", [ "(", Type, ( Identifier | "_" ), ")",
                            [ "when", Expression ] ], Block ;
UnsafeStatement = "unsafe", Block ;
DeferStatement  = "defer", ( ExpressionStatement | Block ) ;
PolicyStatement = [ "require" ], "policy", PolicyCases, Block ;
ReturnStatement = "return", [ Expression ], ";" ;
YieldStatement  = "yield", Expression, ";" ;
ThrowStatement  = "throw", [ Expression ], ";" ;
LoopTransfer    = ( "break" | "continue" ), [ Identifier ], ";" ;
SwitchTransfer  = "continue",
                  ( CaseLiteral | "_" | "(", ConstantExpression, ")" ), ";" ;
CaseLiteral     = IntegerNumber ;
LocalDecl       = LocalBinding, ";" | VacantLocal ;
InferredType    = "var", [ "?" ] ;
LocalBinding    = [ "mut" ], ( InferredType | Type ), Identifier, "=", Expression
                | ConstantLocalBinding ;
ConstantLocalBinding = "const", Type, Identifier, "=", ConstantExpression ;
VacantLocal     = "init", [ "mut" ], ( InferredType | Type ), Identifier, ";" ;
DiscardStatement = ResultDiscard, ";" ;
ResultDiscard   = "_", "=", Expression ;
LifetimeEndStatement = Identifier, "=", "_", ";" ;
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
ArmHeader       = { Attribute }, Pattern, [ "when", Expression ] ;
Pattern         = PatternOperand, { PatternOperator, PatternOperand } ;
PatternOperand  = { "not" }, PatternAtom ;
PatternOperator = "and" | "or" ;
PatternAtom     = "_" | "null" | InferredBindingPattern | TypedBindingPattern
                | ReferenceTypePattern | TuplePattern | UnionCasePattern
                | ConstantExpression | "(", Pattern, ")" ;
InferredBindingPattern = [ "mut" ], InferredType, ( Identifier | "_" ) ;
TypedBindingPattern = [ "mut" ], PatternType, [ Identifier ] ;
PatternType     = Type | GenericTypePattern ;
GenericTypePattern = [ "write" ], DottedIdentifier,
                     "<", GenericPatternArgument,
                     { ",", GenericPatternArgument }, ">", [ "?" ] ;
GenericPatternArgument = "_" | InferredType, [ Identifier ]
                       | Type | GenericTypePattern | ConstantGenericArgument ;
ReferenceTypePattern = [ "mut" ], [ "write" ], "ref", "<", GenericPatternArgument,
                       ">", ( Identifier | "_" ) ;
TuplePattern    = "(", Pattern, ",", Pattern, { ",", Pattern }, ")" ;
UnionCasePattern = Type, ".", Identifier,
                   [ "(", Pattern, { ",", Pattern }, ")" ] ;
```

`for` initializers and iterators omit their internal semicolon. Normally
completing header expressions must produce `void`; assignment and explicit `_ = expression`
discard are also permitted. A value-returning step such as `index++` needs
explicit discard, or use a statement-level update such as `index += int32(1)`.
Ordinary local bindings require an
initializer. `init var name;` instead declares vacant storage whose type is
established by a checked initialization; `init T name;` makes its
type explicit. `init mut var name;` and `init mut T name;` additionally grant
ordinary binding mutation after initialization. Vacancy and mutability are
independent: none of these forms supplies a default value or permits a read
before complete publication. Bare `T name;` is not a declaration here.
Inferred local and iteration bindings use `var` for a non-null type or `var?`
for its normalized nullable form where the type/profile permits that form.
This grants no new representation or invalid-bit-pattern assumption.
`var name = nullableExpression;` requires
established non-nullness; otherwise use `var?`, without an implicit unwrap,
throw or default. Nullable inference retains the existing substitution
normalization: an already-optional inferred type does not gain another layer.
Vacant `init var`/`init var?` bindings infer from a checked initialization and
retain that complete type for later lifetimes. Nullability does not initialize
storage or grant mutation authority. General inferred generic-argument
positions and their inference rules are outside this grammar.

`name = _;` explicitly ends a named local's current value lifetime and performs
that value's scope-exit obligations at the statement, leaving the binding
vacant. It is not an assignment of a discard value, a default/null value,
or a write through a borrowed referent. It does not end other locals or the
lexical declaration: redeclaring the same name in that scope still rejects.
The value must be definitely live, and outstanding dependent loans/pointers
must permit its lifetime to end. Later scope exit cannot clean that value
again; reads of the vacant binding reject.

Ending a lifetime does not grant mutation or initialization authority.
An ordinary immutable `var` cannot be assigned again. `init var` or `init T`
can be initialized when vacant, including by a checked `=` initialization,
but cannot replace a live value. `init mut var` or `init mut T` can also
replace a live value under ordinary assignment and dependency checks.
Once established, the binding's complete type does not change on
reinitialization or replacement. An eligible mutable binding follows its
existing write/initialization rules. Ordinary replacement constructs the new
value before retiring the old one; a failed right-hand side leaves the old
value live until its ordinary lifetime end or unwind.
The lifetime-end form targets a named local, not a field, property, indexer or parameter
alias to caller storage. Ending a local loan releases the loan, not its
referent. Value-specific cleanup, root retirement and dependency rules are
specified in [lifetimes](lifetimes.md#explicit-local-lifetime-end).

```terrum
var x = int32(1);
x = _;
// x = 2;     // Invalid: x is not mutable or an initialization binding.
// var x = 2; // Invalid: the declaration remains in scope.

init var y;
y = int32(1);
y = _;
y = 2;

init mut int32 current;
current = int32(1);
current = int32(2);
current = _;
current = int32(3);
```

An ordinary `for` statement has a header scope containing its initializer,
condition, iterator, and their locals. Iteration statements, including
`await for`, have a header scope containing the evaluated sequence and
iterator state. A header scope ends when its loop exits, after required
iterator close. The braced body has its own per-execution scope. A `while`
or `do` condition instead belongs to its existing enclosing block; those
forms introduce no separate header-local scope. These boundaries also govern
[implicit-reference temporaries](lifetimes.md#repeated-evaluations-and-reusable-locals).

By-value iteration is the default. `ref` iteration borrows an element place
read-only and `mut ref` requires a writable element loan from the iterator;
neither silently copies a value or upgrades a readonly source. The declared
iteration protocol determines element access, loan extent,
backing dependencies and advance invalidation. Consumption or writable
borrowing requires an explicit eligible contract; an iteration-local view
does not become an independently owning value. Outstanding loans constrain
advancement, capture, suspension and close.
The [lending chapter](language/lending-iteration.md) makes the iteration-specific
prefix an alias of an existing T element place; explicit T or var/var? names
that element type, not the hidden output descriptor. A live name denotes that
place, and `ref name` reborrows it. `name = _;` ends the binding loan, not T;
`name is init` tests binding availability without dereferencing an ended alias.
`for (mut ref<T> name in source)` instead uses ordinary mutable by-value
descriptor storage; `mut ref T` selects writable element lending, not rebinding.
The [value-iteration chapter](language/iteration.md) selects standard
synchronous ISequence<T, TCursor> factories and direct ICursor<T> sources,
with TryMoveNext(init ref<T>) and required Close. It does not provide a
Current property, inferred consumption of borrowed elements, or an element
loan for `ref`/`mut ref` modes. Those modes instead select exact
ILendingSequence/ILendingCursor or IWritableLendingSequence/IWritableLendingCursor
relationships with a single init-reference output and required close.
Readonly factories are readonly; writable factories require ordinary mut
receiver access and actual writable backing. These roles do not infer a
different witness from reference-valued by-value items or grant consumption.
`await for (... in ...)` selects an eligible checked async iteration protocol
inside an async implementation. The source expression evaluates once, and
each advancement uses that protocol's awaitable completion. It does not
implicitly await yielded elements: an async sequence of task values still
yields task values. Awaiting each task in an ordinary synchronous sequence
instead uses a normal `for (... in ...)` with an explicit `await` in its body.
`await` is not an element-binding modifier, and `for (await ... in ...)` is
not an alternate form. Reference modes retain their ordinary access checks,
with suspension preserving backing, loan and affinity obligations.
[Async value iteration](language/async-iteration.md) selects exact
IAsyncSequence<T, TCursor> with synchronous readonly GetAsyncEnumerator,
or direct IAsyncCursor<T>, whose TryMoveNextAsync(init ref<T>) publishes at
observed task completion. Its vacant output is reserved while pending:
no caller value/state test, competing init or overlapping advance/close.
Awaited true supplies the exact live-output discriminator; false/failure
leaves vacancy after required cleanup. Required CloseAsync is invoked/awaited
after the item and any pending advance are settled, before header exit.
These signatures do not introduce implicit blocking, task-wrapped acquisition
or executor capture. Cancellation API and general task/awaiter representation
remain separate contracts.
Async reference modes use the distinct IAsyncLendingSequence/IAsyncLendingCursor
or IAsyncWritableLendingSequence/IAsyncWritableLendingCursor roles.
Their GetAsyncEnumerator acquisition is likewise synchronous, with matched
readonly/mut receiver requirements. The completion reservation initializes
ref<T>/write ref<T> output; its published element loan remains cursor-bound
unless an available checked backing/invalidation refinement applies.
All surviving copies/reborrows constrain advance/close and suspension.
The selected DependsOn/Captures/ForwardsBacking schemas express the admitted
whole-source relationships and refinements. Additional lifetime annotations,
associated lifetime items and physical generated lending frames remain
separate work.
The [generated-producer chapter](language/generated-producers.md) separately
registers named affine producers for the selected direct roles.
An iteration loop closes its successfully acquired or adopted iterator
according to
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
ordinally equal labels on simultaneously enclosing loops reject; disjoint loop
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
expression must pass the applicable write or vacant-initialization check.
Normally completing expression statements require a genuine `void` result;
non-`void` results must be used or explicitly discarded. A checked no-return
expression produces no normally unused value. The header discard production
does not make assignment/discard a general value expression.
`DiscardStatement` is only the standalone
discard form; `LifetimeEndStatement` separately ends a local value. In neither
form is `_` an identifier, a value expression or a pattern wildcard. In a
pattern it is a separate nonbinding wildcard; it is not an absence literal.
Recovery forms are not conforming source syntax.

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
Guards and catch filters consistently use `when expression`; parentheses are
ordinary optional expression grouping, not mandatory filter punctuation.
As with declaration contract clauses, an unparenthesized top-level `=>`
delimits the arm rather than forming a guard lambda. A lambda within the
predicate must be grouped or nested in another expression delimiter.
Catch-filter timing follows the incremental unwinding rules below.

Each member path observed by a pattern is read once within an attempted arm;
that observation supplies the path's subpatterns. A later attempted arm makes
a fresh observation, including after a false guard. This does not cache
ordinary member expressions written in a guard or body, or make a readonly
getter pure. Getters and registered extractors retain their checked effects
and failures; failure is not a nonmatch or permission to retry silently.
Observation reuse or elimination requires proof that it preserves the
memory/side-effect contract. A source local can explicitly retain an observation
when the author wants subsequent tests to use that value.

For eligible nullable forms, inferred binding patterns distinguish
initialization from nullability:

| Scrutinee/output state | `is init` | `var item` | `var? item` |
|---|---|---|---|
| Vacant | False | No match | No match |
| Initialized, null | True | No match | Matches |
| Initialized, non-null | True | Matches | Matches |

`var item` binds the canonical non-null payload snapshot; `var? item` permits
null and binds the normalized nullable form. Tests establish initialization
before any value access; vacancy is never read to discover a nonmatch.
Access, backing and copy/borrow/move obligations remain checked. Matching
does not upgrade authority, implicitly copy a unique value or narrow an
aliased source place. `_` does not read or bind an ignored output, but does
not cancel cleanup obligations for a live ignored value.

An ordinary typed or inferred value binding uses an initialization-capable
local in its supporting scope, equivalent to `init T name`; an explicit
`mut` prefix uses `init mut T name`. `mut` requires a named value binding,
not `_`, a bare type test, or a named generic type capture. It changes binding
replacement permission, not referent authority.
A reached positive match performs ordinary initialization or permitted
build-before-drop replacement. Failed and skipped matches preserve any
prior binding value. Name scope is independent of conditional availability,
including after a negative guard or loop exit.
Copyable value types copy their
values; shared references copy handles with their access and rooting
contracts, not the objects. A binding does not create a reference to a
non-reference source slot. Binding an existing reference or pointer preserves
its ordinary alias, access and dependency contracts; unique payloads cannot
use a value binding without copy permission.
Matching does not implicitly consume ownership while testing an arm or its
guard. Explicit borrows support non-consuming inspection, with explicit
movement after selection subject to their ordinary backing and loan checks.
Explicit effects in a guard are not rolled back if it returns false; the
same operations retain their ordinary `if`-style ownership and failure rules.
`x is T t` uses the same type/value matching rules whether `T` is concrete or
generic, including reference and pointer forms permitted by its constraints.
Matching introduces no reference formation, additional indirection, implicit
dereference, boxing or arbitrary conversion chain. A reference to a
non-reference subject must be formed explicitly before matching; parameter-only
`implicit ref<T>` does not extend to patterns or introduce a hidden temporary.

`ref<var> item` matches an existing compatible readonly reference;
`write ref<var> item` requires existing writable reference authority.
A valid initialized `ref<int32>` subject matches the former with a
`ref<int32>` binding to the same referent, not a manufactured
`ref<ref<int32>>`. A reference pattern cannot narrow or reinterpret the
actual referent storage type because its value passed another test.
Movement remains explicit after selection.
For an existing reference to initialized nullable storage, `ref<var>` filters
out the nullable referent type even when its current value is non-null.
`ref<var?>` admits nullable and non-nullable referent types and retains them:
`ref<int32?>` remains `ref<int32?>`, and `ref<int32>` remains `ref<int32>`.
`var?` permits nullability; it does not add it. Writable forms use the same
referent-type filters and require existing write authority.
This differs from ordinary value-binding `var`, which tests current presence.

Generic type patterns name a particular generic declaration and match one
argument per written slot. `List<_>` matches an eligible closed application
without naming its argument. `List<var>` restricts the argument type to a
non-nullable form; `List<var?>` permits nullable or non-nullable forms.
Neither examines elements or asserts that a contained value is initialized.
`List<var T>` and `List<var? T>` additionally capture the actual complete
argument type as `T`, preserving nullability and access rather than promoting,
stripping or erasing them. A named capture is a type binding, distinct from
the ordinary value binding in `List<var T> list`.
The constructor, arity and declared argument kinds still have to match;
`_` and each inferred argument occupy one slot, not a pack or an unapplied
type constructor. Named `var` captures in these forms bind type arguments;
general constant-argument capture and higher-kinded application are outside
these forms. In an ordinary generic application, `_` remains an inference
hole requiring sufficient context, not an existential or erased argument.
Pattern captures do not authorize `var` in arbitrary ordinary applications
or introduce implicit generic formals in method constraints.

Named type captures participate in the pattern's supporting declaration
space even on paths without a match. Using a captured type requires available
checked positive evidence; a negative guard that leaves no normal nonmatching
path can establish that evidence for later code. Explicit `require` is not
the only way to establish it. The
[scope and flow rules](language/initialization-and-flow.md#binding-pattern-declarations-and-scopes)
apply to both value and type bindings.
They permit checked type inspection with `typeof(T)`.
`typeof(T) is typeof(int32)` is a recognized exact type-identity test:
where its truth is established, `T` refines to `int32`, permitting operations
justified by that identity without a runtime conversion. Logical composition
retains the proof only on paths that establish it; an arbitrary user comparison
of type objects or a call to `@typeof` is not proof. Compare complete normalized
identities, including qualification and closed arguments, not display names,
assignability or equal representation. An ordinary alias of `int32` has that
identity; a nominal wrapper does not.
Type inspection does not inspect element values,
grant copying, alter generic declaration contracts or upgrade reference access.
Statically recoverable arguments or declared type evidence support capture.
A type argument discovered only at runtime may also be captured through a
checked type-witness/representation contract. It remains an ordinary symbolic
type wherever its retained checked evidence is available in the supporting
scope, with its complete qualification,
declared constraints, required witnesses, access and ownership/lifetime facts.
Generic operations require a supported execution path, such as retained AOT
specialization or shared generic code. Capture does not implicitly box,
generate code or make unsupported runtime instantiations executable.
Missing required evidence, contracts or execution support diagnoses or rejects,
not a silently false pattern. Exact witness/ABI encoding remains separate work.

Union-case patterns mirror qualified case construction:
`Lookup<int32>.Missing` tests the no-payload case, while
`Lookup<int32>.Found(var item)` tests the declared case and matches its payload
positions. Payload patterns have the declaration's arity and order; a
no-payload case has no parentheses. These tests do not invoke a constructor,
discover a `Deconstruct` method by name, or synthesize public payload members.
Payload bindings retain the ordinary copy/reference/ownership rules.
An initialized `Found(null)` remains distinct from `Missing`; its payload
does not match `var item`, but can match `var? item`.

Tuple patterns are structural. In matching and value-producing `require`
contexts, omission of eligible output destinations requests lifting those
outputs. Explicitly supplied destinations remain ordinary arguments and are
not lifted. An explicit `init ref<T>` mode with a checked, published ordinary
call-completion output contract establishes eligibility without an extra
attribute or registration. Caller-local storage, representation and lifetime
obligations still apply; later task/producer completion is not invocation.
Ordinary calls and statement-form Boolean `require` do not request omission.
Partial omission is permitted; one omitted output has its own
binding shape, while multiple omitted outputs have a structural shape in
declaration order. Supplying every output destination leaves the ordinary
return value as the matching subject. Required inputs remain required.
Argument positions and names select the outputs, not arm types or implicit
conversions, and pattern arms do not steer callable selection. Ambiguous
callable selection diagnoses; specialization to `bool` or a status enum does
not change which channel an arm matches.

Ordinary and omitted-output forms use the same supplied-argument ranking.
The omitted destinations neither add comparison arguments nor make a
declaration more specific. If ordinary quality, binding priorities and
specificity leave the two forms tied, diagnose ambiguity rather than prefer
either channel. Explicit destinations can disambiguate; outside matching
and value-producing `require`, omitted-output forms do not participate.

The call evaluates once. Its contextual output projection preserves each
output's initialization, nullability, ownership and declared status
correlations; it is not an ordinary tuple constructed by reading vacant
slots, a mandatory library `Option`/`Result`, or a general implicit conversion.
The [supporting-scope hidden locals](language/calls-and-inference.md#hidden-destination-instances-and-retirement)
retain ignored live values beyond the containing `if` or `while`.
Repeated invocation prepares vacant destinations after argument staging
and ordinary invocation checks, with dependency-valid conditional retirement;
it does not keep an owner per iteration. Pattern bindings remain ordinary
supporting locals and can prevent retirement of a depended-on hidden owner.
Patterns test the selected outputs' availability and presence. Initialized
failure outputs may match; there is no universal success gate. Boolean
conditional-init contracts remain the case where true publishes the outputs
and false leaves them vacant. Enum and richer statuses retain their declared
per-output availability relations, not a guessed zero value or member name.
Matching an available output establishes the declared correlated facts about
explicitly supplied destinations as well.

A value-producing `require` demands complete initialization of every lifted
output and yields their values with their complete types. It does not add a
non-null check to a declared nullable output or select a separate default
success group. Owning transfers follow all required initialization checks,
not a speculative pattern/guard selection. Selective Copy extraction uses an explicit requirement on a pattern,
such as `require (Read() is (var value, _));`; `_` neither reads nor demands
initialization of the ignored output. Live ignored owners still require
cleanup. Ordinary explicitly written predicates retain their Boolean
requirement semantics. Without omitted outputs, a value-form `require`
checks its Boolean operand and yields `void`/`()` on true, not the checked
Boolean; false raises the selected requirement failure. Every supplied
destination stays in its caller local with the call's actual published state.
An ordinary non-Boolean status needs an explicit predicate. A lifted `bool`
payload instead retains its output value, including false.
The operand's Boolean context, not the requirement's unit result, supplies
ordinary expected-result inference. Unique selective extraction supplies an explicit
destination and moves it only after the desired match/guard; no implicit
unique binding or new move-pattern syntax is introduced.
An explicit `require<E>` chooses only the failed-path payload, not operand
inference, overload ranking or lifted outputs. On failure it performs ordinary
checked `new E()` with zero supplied arguments. Callable-local RequireException
or Core supplies an omitted E. Actual operand/constructor failure and every
output/hidden-owner state remain governed by
[requirement failures](language/requirement-failures.md); no factory, hidden
diagnostic arguments, rollback or early hidden-owner retirement is added.
Richer-status contract encoding, later-completion
lifting and projection metadata are outside these source forms;
omission does not authorize skipping arbitrary required arguments.
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
Bindings in `or` alternatives follow the ordinary declaration-space rules.
Repeating a binding name in one supporting space rejects, including across alternatives
with identical types or explicit `mut`; no merged local is introduced.
Different binding sets use ordinary path-sensitive availability at the join.
One binding followed by value tests, or separate switch arms with sibling
scopes, does not require a redeclaration.
Member and relational conditions use ordinary `when` expressions.
Dedicated member/relational pattern syntax, pattern `where` and registered
object-extractor forms are outside the forms above.
An ordinary wildcard suppresses missing-known-case warnings.
`[Exhaustive] _ => ...` retains a fallback while requesting those warnings;
guarded arms do not establish coverage merely by naming a case. The compiler
also warns when a known case reaches a fallback proved to have no normal
completion using a verified `DoesNotReturn` contract, not a helper-name guess.
Nonreturning does not imply always throwing. Authored fallback code, including
a throwing `unreachable()` helper, remains executable unless soundly proved
unreachable under all relevant inputs and replacement boundaries. Neither
undefined behavior nor a diagnostic authorizes changing source intent.

Typed catches use the [local failure subject and binding rules](language/local-failures.md#4-catch-subjects-and-binding-modes).
`catch (ref<E> failure)` borrows a complete eligible payload through an
ordinary readonly descriptor; `catch (E failure)` requires an ordinary Copy.
`catch (E _)` performs no payload read/copy, while a category `catch (I _)`
tests its exact declared witness without creating interface storage.
Bare `catch` handles contract-catchable failures without a universal object.
`when` filters are checked Boolean code: false continues handler selection,
and a filter failure follows ordinary failure/chaining rules rather than
silently becoming false. Handlers test declared catchable contracts in source
order after unwinding to each candidate handler's still-live scope.
Cleanup of exited inner scopes, including their `finally`/`defer` actions,
precedes that candidate's type tests and filters. Cleanup failure updates the
pending failure under the ordinary causal-chaining rules before further
selection. Filters have checked access to that pending failure and still-live
state, not invalid references into already unwound frames.

A false filter proceeds to the next catch in source order at that scope.
If no catch matches, propagation continues with further outward unwinding.
The handler body is entered only after its type and filter match; the runtime
does not unwind past a candidate scope before testing it. This is incremental
unwind-and-selection, not a first-pass search running authored filters before
inner cleanup. A filter failure follows ordinary failure/chaining propagation,
not a false result or a restart that replays completed cleanup.
Cleanup and filter side effects are observable even if no handler is selected;
there is no transaction deferring them until the filter succeeds. The pending
failure must remain valid during selection without keeping exited frames live.
Fail-fast termination retains its separate no-cleanup guarantee.
The local failure chapter specifies nominal Exception/category eligibility
and input ownership. [Core scalar failures](language/scalar-failures.md)
select exact zero-data overflow, integer zero-divisor and invalid-shift-count
payloads without changing source productions or operand evaluation.
[Numeric selection](language/numeric-selection.md) separately fixes exact
Min/Max/Clamp signatures and Core InvalidBoundsException for Clamp's ordinary
explicit min <= max precondition. Invalid bounds, a normally clamped value
and scalar arithmetic failures remain distinct.
Other failure catalogs, physical transport and native runtime support remain
separate contracts.
[The contained foreign bridge](runtime/foreign-failure-bridge.md) specifies
its own paired failure transport without completing those catalogs/codecs or
admitting a native unwinder through Terrum frames.
Bare `throw;` rethrows the
active handled failure; it is invalid outside a handler or in a separately
invoked lambda merely declared there. `throw expression` evaluates its
operand once and has no normal result; the operand must satisfy the admitted
[complete exception payload contract](language/local-failures.md#2-eligibility-is-not-storage-or-lifetime-permission)
or an explicitly checked captured-propagation protocol.

The [captured-event contract](language/captured-exceptions.md) selects exact
Core CapturedException and direct `CapturedException.Capture()` with no
arguments in the active source handler body, including its ordinary lexical
cleanup scopes. A filter or separately invoked helper/lambda has no such
context. Successful publication transfers the event and leaves this source
handler unable to Capture or bare-rethrow it again; existing input borrows
keep their original checked scope.
An exact captured owner uses the consuming route of the existing throw
statement and expression productions: a named owner requires
`throw move captured;`, while a fresh owner can transfer directly.
No special `throw<preserve>` production,
payload Copy or new origin is introduced. Captured state cannot retain
invalid references into unwound frames or replay cleanup. A shared/repeatable
family, implicit thread capability and physical owner/handler/cause/transport
records remain unassigned.

### 3.4 Expressions and precedence

The grammar uses the precedence table to parse an operand, then
repeated binary operators and operands. For example, `a && b || c` is
`(a && b) || c`; there is no need to read a chain of one production per
operator level. Ordinary binary operators at the same level associate left;
`??` and `?:` associate right. Prefix
operators nest right; postfix calls, member access and indexing bind more
tightly than prefix operators. Parentheses group a full `Expression` and
override the table, so they are not a binary precedence level. `is null`
and inferred `is var name` / `is var? name` tests have equality precedence. The table is highest first:

| Level | Syntax | Associativity |
|---:|---|---|
| 14 | postfix call, member access, `->`, index, `?.`, `?[]`, `++`, `--` | left |
| 13 | prefix `+ - ! ~ * & ref move ++ -- await`; contextual index-bound `^` | prefix/right-nesting |
| 12 | `* / %` | left |
| 11 | `+ -` | left |
| 10 | `<< >> >>>` | left |
| 9 | `< <= > >=` | left |
| 8 | `== !=`, `is null`, `is var name`, `is var? name`, `is init` | left |
| 7 | `&` | left |
| 6 | `^` | left |
| 5 | `\|` | left |
| 4 | `&&` | left |
| 3 | `\|\|` | left |
| 2 | `??` | right |
| 1 | `?:` | right |

`<<` and `>>` are left and arithmetic right shift respectively; `>>>` is
zero-filling right shift. The [primitive matrix](language/primitive-operators.md)
requires integer data and admits typed fixed/native integer counts, with
`int32` context for otherwise contextual counts. Counts are checked against
the data width without masking or implicit numeric conversion. Left shift truncates to the
operand width rather than checking multiplication overflow. Built-in
operator availability is distinct from whether a nominal type may declare an
overload or a generic protocol may require it. Comparisons may chain
syntactically (`a < b < c`) and require semantic checking; the grammar does
not imply chained-comparison meaning. `is var name` tests initialized
non-null presence and binds its payload snapshot; `is var? name` permits
initialized null. `is init` tests initialization independently of presence.

```ebnf
Expression      = LambdaExpression | ThrowExpression | ConditionalExpression ;
ConditionalExpression = OperatorExpression,
                        [ "?", Expression, ":", Expression ] ;
OperatorExpression = Operand, { ( BinaryOperator, Operand | IsTest ) } ;
Operand         = { PrefixOperator }, ( Postfix | ThrowExpression ) ;
PrefixOperator  = "+" | "-" | "!" | "~" | "*" | "&"
                | "ref" | "move" | "++" | "--" | "await" | "const" ;
BinaryOperator  = "*" | "/" | "%" | "+" | "-"
                | "<<" | ">>" | ">>>"
                | "<" | "<=" | ">" | ">=" | "==" | "!="
                | "&" | "^" | "|" | "&&" | "||" | "??" ;
IsTest          = "is", ( "init" | Pattern ) ;
Postfix         = Primary, { PostfixPart } ;
PostfixPart     = ".", Identifier, [ TypeArguments ]
                | "->", Identifier, [ TypeArguments ]
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
                | SwitchExpression | CollectionExpression | RequireExpression
                | "new", Type, Arguments,
                  [ ObjectInitializer ]
                | Number | String | RawText | InterpolatedText | InterpolatedRawText
                | Character | Boolean | Null | "field" | "this" | ReturnValueReference
                | "typeof", "(", Type, ")"
                | Identifier, [ TypeArguments ]
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
RequireExpression = "require", [ RequirementFailureSelection ], Postfix ;
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
                | { Attribute }, ParameterType, ( Identifier | "_" ) ;
ThrowExpression = "throw", Expression ;
CollectionExpression = "[", [ CollectionElement,
                       { ",", CollectionElement }, [ "," ] ], "]" ;
CollectionElement = Expression | "..", Expression ;
```

`IsTest` is a postfix-like binary test at equality precedence: it consumes
the preceding expression as its left operand and its pattern or `init`
tail as the right form. Unlike an ordinary binary operator, it may bind
`name` for the appropriate checked scope. The table, not the flat
`BinaryOperator` alternatives, resolves any mixed expression. `HexFloat`
participates in `Number` exactly like the other numeric alternatives; its
value semantics and lexical boundaries are described in §2.
When `Primary` is a bare `Number`, its first `PostfixPart` cannot be member
access (`.`, `->`, `?.`, or `.via<I>`); parenthesized or explicitly typed receivers
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
not source type packs or unresolved exported holes. Constructor inference
uses [the call algorithm](language/calls-and-inference.md) after type-name
selection. First-class range values and prefix-cast forms remain separate
work; assignment remains a statement. `T(expression)` is an
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
their own eligible contracts. First-class range values require their own registered contracts.

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

Built-in scalar prefix/postfix `++` and `--` apply to initialized writable
numeric places or eligible same-typed value getter/setter targets under
[primitive updates](language/primitive-operators.md#built-in-updates-and-accessor-targets).
Capture the target once, read/get once, perform checked arithmetic, then
store/set only on success. Prefix returns the new value while postfix
returns the saved old one, without narrow-integer promotion.
Operator failure does not commit the replacement, but does not roll back
other already-performed effects. A value getter is not made borrowable.
Nominal Copy-place update contracts remain separate; unique/owning update
contracts remain open.

Tuples have two or more elements with optional names, not a new nominal
identity or implied foreign/public ABI. `(x)` remains grouping and `()` is
the void value. Zero-/one-arity tuple extensions, including any unified
grouping/decomposition model, are outside this grammar;
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
The [delayed inference stage](language/calls-and-inference.md#delayed-callable-evidence)
can use an independently typed result after the callable family and inputs
are known, before expected-result/default fallback. It cannot retarget those
inputs, infer a representation, or use an untyped literal's value to select
an overload. Complete target checking follows unique selection.
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
retain the fixed role-specific ordering in §3.1. Lambda `static` and `async`
may occur in either order, at most once each. An async implementation must
have a declared or contextual task-like completion result, or the eligible
async-producer result contract described in §5.12 when marked `[Iterator]`.
Arbitrary result types are not implicitly wrapped, and `async void` rejects.
In an ordinary async body,
successful return or expression-body evaluation supplies the completion's
result, not a second task handle. Task-like and awaitable eligibility require
checked contracts, not similarly named methods.
The [async execution chapter](language/async-execution.md) fixes failure
channels: caller arguments and failed establishment propagate synchronously;
established precondition/body/cleanup/completion-check failure is failed
completion even when no await suspends. This does not transform an ordinary
non-async function merely because it returns Task<T>.

`await expression` is a prefix expression within an async implementation.
Postfix access/calls bind first; `await Read() + offset` means
`(await Read()) + offset`, while `await (ChooseTask())` awaits the grouped
operand. The operand evaluates once and its checked completion supplies the
await expression's result or propagates failure under the ordinary failure
rules. Awaiting an already-completed ordinary operation continues inline;
explicit continuation adapters retain their separately declared executor and
affinity contracts. An incomplete
operation suspends under its checked scheduling contract. Ordinary async
invocation starts eagerly, without implicit executor capture or a mandatory
worker hop.
Postconditions are checked before successful completion publication, and
suspension preserves ownership, loan, cleanup and affinity obligations.
The [Core Task<T> contract](language/task-responsibility.md) is affine.
Ordinary await consumes a fresh task or an explicitly moved named owner:
`await ReadAsync()` and `await move task`. Bare `await task` rejects for a
named affine Task<T>. The wait produces exact T or propagates terminal failure;
its source stays vacant on both exits. Every exit ending a task's owning extent
requires checked settlement or responsibility transfer, without implicit
blocking, async scope join, cancellation or supervision.
The selected [async cursor output](language/async-iteration.md#completion-and-observation)
has an explicit operation/destination reservation and completed-Boolean
relationship. Await observes it after terminal checks/cleanup; returning a
Task<bool> or inspecting IsCompleted alone does not initialize that destination.
This is not a changed default for arbitrary task-returning outputs or omitted
output lifting.
A separately declared non-async lambda is not an async body merely because
it occurs inside one. General awaiter registration, executor APIs, affinity annotations
and completion representation belong to the library/runtime contracts.

Interpolated text is target-directed through a registered checked text
handler, not an implicit allocating string type or arbitrary same-named
builder methods. [Builder type recipes](language/construction-handlers.md)
bind the chosen target's construction contract without introducing
open-generic type values or inferring a protocol from member names.
Construction uses [eager-construction/1](language/construction-handlers.md#eager-capture-and-acquisition).
Hole expressions evaluate once, left-to-right into typed captures before
that construction's builder acquisition or segment drain. A disabled
consumer or refusal cannot suppress those capture effects.
An alignment expression may be an ordinary
checked runtime expression, not just a compile-time constant. Evaluate the
value first, then its supplied alignment once, then its supplied runtime
format once, then proceed to the next hole. Width and format expressions
are not deferred until output draining and obey ordinary ownership,
lifetime, effect and failure checks.
The [standard text operation](language/construction-handlers.md#alignment-and-format-data)
checks alignment in its expected `int32` domain, without inventing an
independent numeric literal type for the value hole. Positive width means
right alignment, negative means left, and zero requires no padding. Width is
a minimum, not a truncation limit; longer formatted content remains intact.
Magnitude and padding arithmetic preserve mathematical results, including
2,147,483,648 for int32 minimum. Width counts Unicode scalars, not bytes,
code units, grapheme clusters or terminal columns. Formatting cannot replay
to measure output; staging retains the writer's actual storage/refusal
contract. Format text and the value are passed to that
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
and refused-capture cleanup under the declared contracts. Boolean refusal
in plain value construction uses the
[ordinary require failure](language/construction-handlers.md#refusal-in-plain-value-construction);
explicit writer/consumer operations retain their own status contracts.
The [nominal drain operations](language/construction-handlers.md#standard-text-operations)
retain exact writer/value members and normalized literal/value roles.
Physical staging/representation and resumable async formatting remain
separate library design.

Collection expressions are target-typed and use only a registered, checked
construction contract selected by the target-owned constructed builder
recipe; `[]` is empty and no default array/List or numeric
width is invented. `..sequence` spreads can interleave with ordinary elements
under eligible iteration and construction contracts, without reinterpretation
or implicit ownership transfer. Capture every reached element/spread-source
expression once in source order before acquiring this site's builder.
Enumerate a captured spread only when draining reaches it; eager capture
does not force full enumeration after refusal.
Boolean refusal in plain collection construction raises ordinary require
failure, never a successful prefix/default collection; operation exceptions
retain their ordinary propagation.
The [Core ICollectionBuilder requirement](language/construction-handlers.md#collection-acceptance-and-spreads)
supplies the by-value item acceptance and exact target-result relation,
composed with the parameterless factory and consuming completion.
Standard synchronous spreads use the [value-iteration contract](language/iteration.md#eager-collection-spreads):
an exact sequence factory or direct cursor supplies TryMoveNext's by-value
item and required close, without method-name discovery or moving out of
borrowed elements. An unreached factory is not acquired; an already-captured
direct cursor still closes during abandonment without advancing.
Lending/async and other extensions require their own admitted contracts.
Conditional inclusion composes existing forms:
`..(condition ? [element] : [])` when those nested expressions have their own
admitted target/direct-fragment relationship; no collection-specific `if`
form or default intermediate collection is added.
Nested fragment typing and any guaranteed direct-fragment construction must
come from the selected target registration. Without that support, nesting
does not acquire a type or a no-intermediate-construction guarantee by syntax
alone. An implementation may eliminate intermediates only when permitted by
the contract or proved unobservable, preserving identity/escape, allocation
refusal, allocator hooks, cleanup, memory observations, effects and failures.
Equivalent final elements are insufficient to justify substituting builder
operations with different contracts. The eager-capture and drain
ordering must be preserved. Mapping/filtering compose
sequence APIs and spreads. Dedicated comprehensions and repetition are
outside this grammar and require contracts for tensor rank/shape, broadcasting,
traversal, allocation, and copy-versus-per-element-generation contracts.

`defer expr;` and `defer { ... }` register an action when execution reaches
the statement. The expression or block runs when the enclosing lexical block
exits, including normal transfers and checked unwinding; operands are
evaluated then, not when registered. Registered actions run in reverse order.
An expression action follows the expression-statement result rule; a normally
completing non-unit computation needs a
block containing explicit discard or another use of its result.
References used by a deferred action must remain valid until it executes;
control transfers cannot leave the action, just as they cannot leave a
`finally`. Deferred actions have the same ordinary failure and effect rules
as authored `finally`, not a special `NoThrow` requirement. See the
[language exception contract](language.md#safe-and-unsafe-boundaries).

`PolicyCase` binds a compile-time constant named enum case, not a type
argument or a runtime enum value. Exact policy selection, strictness and
generic forwarding are described in [§5.12](#512-modifiers-cleanup-and-finalization).

<a id="4-accepted-language-direction"></a>

## 4. Source semantic constraints

These semantic constraints accompany the source forms above.

- **Access, borrowing, and output state:** canonical forms include `ref<T>`,
  `ptr<T>`, `box<T>`, `val<T>`, `own<T>` and `write`. `mut` qualifies a place,
  nominal declaration or receiver; it is not a passing mode or generic
  type qualifier. Explicit descriptors and their `*`/`->` access follow
  [references](language/references.md). Other access and receiver rules follow
  [language types and operations](language.md#types-and-operations); readonly access does not
  upgrade to writable access. `implicit ref<T>` follows the [parameter-only
  loan contract](lifetimes.md#parameter-only-implicit-readonly-loans); it
  adds no noescape rule or permission to widen declared dependencies.
  Conditional outputs follow [declaration postconditions](language-contracts.md#2-outcome-initialization-and-nullability-are-separate):
  `ensure` is an ordinary Boolean postcondition, live/nullable-live/vacant
  states remain distinct, and vacant storage is never read. Named callable
  clauses use §3.1 and target-checked lambdas use §3.4; named producer phases use
  [generated producers](language/generated-producers.md), while physical
  correlated-output metadata remains outside this source grammar
  (see [§5.8](#58-postcondition-syntax-for-conditional-output-state)).
  `init ref<T>` means
  vacant-storage initialization, distinct from ordinary `ref<T>`/`write ref<T>`;
  `Create(init value)` is the call-site spelling. The reference chapter
  defines parameter legality and signature identity; persisted output
  relations additionally require their specified metadata contract.
- **Generic type checking:** explicit parameters, constraints and static
  operator protocols follow the declaration contracts; associated types and family
  membership follow [type-family contracts](type-families.md#explicit-families)
  and [associated relationships](type-families.md#nominal-witnesses-and-associated-relationships).
  Optional projection is non-injective and semantically idempotent, but
  repeated source `T??` is not valid syntax. This specification gives `_` holes,
  generic defaults and selected associated-type spellings.
  [Complete types](language/types-and-normalization.md), [witnesses](language/interface-witnesses.md)
  and [calls](language/calls-and-inference.md) own their supported source
  algorithms; physical generic/witness representation remains separate.
  Type packs have no source production here.
- **Functions and overloads:** overload selection is source-time and fixes the
  selected declaration and closed type arguments; runtime virtual/interface
  dispatch is separate. Applicability and ranking follow [calls and inference](language/calls-and-inference.md):
  identity, access attenuation and other admitted bindings are compared
  per supplied argument. Only
  declared projections/attenuations apply, no implicit numeric/conversion-chain
  search is performed, constraints do not create overloads, return type is not
  a tie-breaker, and applicable members outrank extensions. By-value `T` beats
  `implicit ref<T>` when both apply; fixed arity beats an equally good expanded
  `params` candidate. Explicit extension-provider selection is specified in
  §5.2; concrete extension declaration/import schemas and physical binding
  metadata remain separate work.
- **Types and declarations:** type/declaration semantics follow [language
  interfaces and generic properties](language.md#interfaces-access-and-generic-properties).
  An intended reserved word does not establish a declaration or statement
  production. Union forms are specified in §3.1. Nominal tagged sums declare explicit cases
  and an explicit closedness contract; closed case sets support exhaustive
  dispatch, while evolvable sums require an unknown-case path preserving opaque
  payload validity, roots and cleanup. A live optional payload such as
  `Found(null)` is distinct from a no-payload `Missing`. Cases use semicolon
  declarations and qualified construction, with `[ClosedCases]` for explicit
  closedness; no `case` keyword is introduced.
  New pattern syntax must compose clearly with Terrum's existing types,
  access, places and ownership rather than importing C# property-pattern
  punctuation. Pattern members are observed once per path per attempted arm,
  with fresh observations in later attempts and ordinary checked effects.
  Value bindings follow ordinary copying; references and ownership transfer
  require explicit operations. Registered extractor forms and the remaining
  reference-formation details are outside these source forms.
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
- **Layout and interop:** `Auto` leaves instance fields private/unfixed; `Sequential`
  follows declaration order; `Union` overlays at offset zero and requires
  `Blittable`. `Transparent` is exactly one carrier field; `Blittable` fixes
  per-profile size/alignment/offsets; `Transparent` commits to its carrier's
  representation and direct call classification. Public instance fields require fixed `Blittable`
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
  The selected Layout/Blittable source schemas and
  [Windows x64 limits](targets/windows-x64-data-layout.md) apply. Additional
  target profiles and physical representation records remain separate work.
- **Text and numeric forms:** [conversion policies](conversions.md#conversion-policies)
  define numeric behavior and [character intent](conversions.md#character-intent)
  defines character forms. There are no width suffixes or default numeric type;
  `T(expression)` is a checked conversion, literals are context-typed, and
  numeric operators follow the [selected primitive contracts](language/primitive-operators.md).
  Mandatory floating behavior is not optional target-specific API policy.
  Character literals have the scalar/target rules in §2, and
  [literal construction](language/literal-construction.md) owns selected
  registration and parser contracts. Additional APIs, physical registration
  records and hexadecimal-float recovery details remain separate work.
  `utf8z("...")` is an ordinary call rather than a lexer prefix; its name does
  not establish a registered target.

<a id="5-open-grammar-and-semantic-choices"></a>

## 5. Source contracts and open choices

Examples in this section are informative. Source and semantic
contracts apply as stated; unspecified extensions are outside this grammar.
See the [language contracts](language.md) and
[library layering](libraries.md#dependency-layers-and-package-boundaries)
for semantic IL/reference boundaries.

### 5.1 Initialization loans at call sites

Use `init` as the call-site argument mode for an
`init ref<T>` parameter, e.g. `Create(init value)`. It designates vacant
storage and is distinct from ordinary `ref`/`write ref` access; it does not
make an existing initialized value writable or readable
through the initialization loan.

```terrum
// Initialization-loan argument.
Create(init value);
```

Argument labels are specified in §3.2; [references](language/references.md)
and [calls](language/calls-and-inference.md) define place formation, modes,
applicability and contextual output execution. Physical conditional-output
metadata remains separate work. This does not
introduce an extra transactional publication protocol.

### 5.2 Exact overload grammar, ranking, and disambiguation

Name lookup first selects one [callable group](language/names-and-binding.md#lexical-lookup-and-imports).
Ordinary same-name declarations within that group form source-time candidates;
binding selects one concrete declaration and closed
generic arguments. That target is recorded for downstream validation and code
generation. Linkers/JITs do not rerun overload resolution; runtime
virtual/interface dispatch is a separate mechanism.
[Calls and inference](language/calls-and-inference.md#applicability-and-best-candidate)
defines the complete supported-source algorithm. A by-value `T` wins an
otherwise equal match over implicit formation of `ref<T>` for `F(value)`.
Exact matches
precede previously permitted, declared projections/erasures; there is no
implicit numeric or general type-conversion search. Permitted
access/qualification weakening may participate in matching (for example,
`write Span<T>` to a readonly `Span<T>` parameter), but no new or reversed
projection/erasure is inferred. Constraints filter applicability, not form
constraint-only overloads. Return type alone does not distinguish overloads.
Applicable members outrank extensions. These decisions are authoritative in
[language](language.md#callable-values-params-and-tuples),
[conversions](conversions.md#conversion-policies), and
[type families](type-families.md), under the canonical call chapter.

The declarations below illustrate overload semantics. No new overload
keyword is required.

```terrum
// Declarations illustrating overload semantics.
public int32 Parse(int32 value) { return value; }
public uint32 Parse(uint32 value) { return value; }

public void ParseExample()
{
    // Parse(42) is ambiguous; explicit target construction selects int32.
    int32 parsed = Parse(int32(42));
}
```

The `implicit ref<T>` form also participates, but a by-value match
wins for ordinary `Inspect(item)` when both candidates apply:

```terrum
// Parameter forms illustrating overload semantics.
public int32 Inspect(Item value) { return 0; }
public int32 Inspect(implicit ref<Item> value) { return 1; }
public void InspectExample(Item item) { int32 selected = Inspect(item); }
```

Retain ordinary same-name declarations as
source-time candidates. Determine applicability first, then compare argument
quality independently: identity, then admitted immediate-access attenuation,
then other admitted single-step bindings. For example, attenuation from
`write pin ref<T>` to `pin ref<T>` is better than the direct binding to
`write ptr<T>`; neither acquires a pin or changes inner `T`.
A candidate is better only if it is no worse for every argument and better
for at least one; crossed better/worse arguments are ambiguous, not summed
into a score. Apply declared specificity and by-value/
`implicit ref<T>`/`params` priorities, including fixed-arity non-expanded over
an otherwise equal expanded `params` candidate. There are no return-type,
declaration-order, or literal-numeric-fit tie-breakers. Do not search implicit
numeric or arbitrary type-conversion chains. Constraints filter applicability
but do not form constraint-only overloads. Source binding records the selected
declaration and closed generic arguments; downstream tools do not rerun source
overload resolution.

After argument quality and the existing
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
public int32 Select(int32 value) => value;
public T Select<T>(T value) where T : [Copy] => value;
public int32 OrdinarySelection() => Select(int32(7));
public int32 ExplicitGenericSelection() => Select<int32>(int32(7));
```

Both candidates give an exact `int32` argument match in `OrdinarySelection`;
the nongeneric declaration wins the remaining tie. The explicit type argument
in `ExplicitGenericSelection` selects the generic declaration.

Constructors use the same staged inference: explicit arguments pin positions,
typed values and eligible delayed callable results contribute types, expected-result context fills
unresolved positions, and declared defaults fill omitted unresolved positions
before contextual literals/lambdas are checked. Do not infer generic arguments
from field capture, private constructor implementation or arbitrary witness
search. Construction remains separate from the conversion type-call under
§5.13.
Concrete extension declaration/import schemas and physical generic,
callable, witness and contextual-output binding metadata remain specification
work. The source algorithm does not enable those unsupported features.

#### Extension-provider selection

`value.via<Provider>.Member(arguments)` selects
an explicitly eligible extension provider. `Provider` is a nominal
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
public struct MetricsA
{
    public static int32 Measure(int32 value) => value;
}
public struct MetricsB
{
    public static int32 Measure(int32 value) => value + 1;
}
public int32 ChooseA(int32 value) => value.via<MetricsA>.Measure();
public int32 ChooseB(int32 value) => value.via<MetricsB>.Measure();
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
Inaccessible members are filtered before selection; they do not hide an
eligible accessible extension. Ambiguous applicable ordinary members diagnose
without extension fallback. A selected nonmethod value member is invoked as
that value or diagnoses rather than being reinterpreted as an extension.
Explicit provider selection can choose that provider even when an ordinary
same-named member would win `value.Member()`. Overloads within the selected
provider use the ordinary applicability/ranking rules, not declaration/import
order. Equally good providers remain ambiguous without selection; ordinary
qualified static calls remain available. The selector does not define
extension-block/registration syntax or runtime provider discovery.

### 5.3 Caller-unsafe declarations and explicit unsafe scopes

`unsafe` on a callable declaration exposes its
caller obligations, e.g. `public unsafe void M()`. An ordinary `public void M()` does not
propagate a caller-unsafe obligation. The declaration modifier does not make
its body an unsafe context: unsafe operations inside either form still require
an explicit `unsafe { ... }` block or `unsafe(expr)` expression. A caller invoking the unsafe declaration
must likewise satisfy the contract in an explicit unsafe context. The
`unsafe(expr)` form scopes the same caller obligations to one expression.

```terrum
// Caller-unsafe declaration.
public unsafe uint32 LoadAligned(uint64 address)
{
    unsafe { return 0; }
}

public uint32 Read(uint64 address)
{
    unsafe { return LoadAligned(address); }
}

// This ordinary declaration does not pass caller obligations onward.
public uint32 LoadValidated(uint64 address)
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
The [sharing contract](runtime/sharing-and-handoff.md) uses these same boundaries:
mechanical checks compose admitted protocols, while arbitrary/native algorithms
retain their explicit unsafe implementation responsibility and producer/audit
trust. Capability opt-in is not implementation proof.

The current [Effects clause](representation/contract-clauses.md) carries
caller-unsafety. That selected carrier does not complete additional native
annotations, callable ABI records or their physical integration.

<a id="54-aliases-and-associated-types-candidate-declarations-open-binding"></a>

### 5.4 Aliases and associated types: declarations and open binding

Families opt in explicitly and declare
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
declarations, with `public`, `internal`, or `file` visibility like other
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
interface/member identity. The [witness chapter](language/interface-witnesses.md#qualified-self-witness-projection)
defines checked outer-access projection to a qualified self, without changing
the inferred complete type or silently retargeting ordinary arguments,
associated bindings, or selected implementations. This does not alter the
associated-type relations; metadata encoding remains open. An interface such as
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
The [witness-resolution rules](language/interface-witnesses.md)
define inherited identity/default selection, exact associated equality, and
finite binding expansion. Metadata representation remains open.
For a selected `Item = int32`, `where T.via<ISource>.Item is U` succeeds
when the otherwise resolved `U` is `int32` or its transparent alias, not
`int64` or a nominal wrapper over `int32`.
Preserve the selected inherited-identity/default resolution rules. Keep family
opt-in and declared checked projections; do not introduce universal erasure
or inferred covariance.

Identity preserves the complete
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
mapping and replacement guarantees; exact encoding belongs to the metadata contract.
This efficiency requirement does not introduce a live wildcard or implicit
conformance. Concrete adapter/forwarding attribute schemas remain specification
work.

Interface constructors have no source production here; a construction requirement
needs a checked construction and initialization-completion contract. Fixed
`const T Name = value;` interface constants are owned by the interface
definition; a bodyless `const T Name;` instead requires a checked
implementer-supplied binding. They have distinct identity/versioning rules
and neither follows automatically from a `type Name;` requirement.

No production for variadic type parameters
is made here; generic defaults and collection spreads are not type packs.

<a id="55-accepted-temporary-scope-for-implicit-reft"></a>

### 5.5 Temporary scope for `implicit ref<T>`

For `Foo(5).Bar(); Baz();`, materialization is shorthand for assigning an
ordinary hidden mutable local at the argument's original evaluation point in
the existing enclosing lexical scope, then passing its readonly reference.
The local starts vacant and has the equivalent declaration `init mut T tmp;`.
One binding is reused per source materialization site and activation of its
supporting scope. Its initializer is not hoisted to scope entry, and no
shorter lifetime-only scope is introduced.

Skipping the argument creates no new value; it does not replace or retire a
value from an earlier reached evaluation. Assignment to the vacant binding
initializes it; assignment to the live binding performs ordinary replacement.
Existing dependencies may forbid that replacement. The compiler must not
keep an old owner alive in hidden retained storage to evade those checks.
Scope exit follows ordinary initialized/moved-state cleanup. A valid returned
reference may be used by later statements in the same scope, subject to its
ordinary declared dependencies; existing place/backing lifetimes and
capture/suspension/escape permissions are not widened.

The canonical [call contract](language-contracts.md#1-one-call-and-reference-model)
and [lifetime expansion](lifetimes.md#repeated-evaluations-and-reusable-locals)
specify evaluation order, loop transfers, replacement failure, and final
cleanup. General loan metadata remain separate work.

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
A borrowed result can publish its possible receiver/formal dependencies with
`[return: DependsOn(nameof(left), nameof(right))]`. Prefer `nameof` over a
literal parameter name; the named parameters belong to the annotated callable,
including when the annotation precedes its parameter list.

```terrum
[return: DependsOn(nameof(left), nameof(right))]
public ref<T> Select<T>(ref<T> left, ref<T> right, bool useLeft)
{
    return useLeft ? left : right;
}
```

The resolved Core schema accepts direct `this`, `nameof(parameter)` and
literal formal names as checked source recipes, not arbitrary computed
strings or runtime lookups. Receiver and formal identities are distinct;
`nameof(@this)` selects an escaped formal, while direct `this` selects the
receiver. Explicit parentheses are required, including `DependsOn()` for
no borrowed receiver/formal source. Unknown/duplicate sources or incompatible
targets diagnose. The body must satisfy the published relationship. Since either
input may supply the result, callers retain both possible backing obligations;
this is not the intersection of the inputs' backing sets, and neither input's
permitted lifetime is extended. An independently compiled caller relies on
that published relationship, not a replaceable branch implementation.

DependsOn selects the result's permitted whole sources, including broad
source-storage-or-forwarded-backing permission. Separate ForwardsBacking
refines selected sources to existing backing only. Its target and listed
sources must already belong to the effective relationship; it supplies no
new source or retention permission. An inline annotation on an init formal,
or `param(output): DependsOn`, selects that initialized output's inner value,
not its descriptor or caller destination slot.

Captures declares a retained destination: construction or explicit `this:`
receiver, `param(holder):` state, `return:` holder, or an already-admitted
operation/producer capture extent. It does not acquire an owner, grant writes,
extend source lifetimes or roll back storage on exceptional exit.
`this` is an attribute target only for a schema and declaration admitting a
receiver destination; it does not broaden parameter-only NoAlias.
Unannotated relationships follow the
[language-versioned declaration defaults](lifetimes.md#declaration-defaults-exceptions-and-published-compatibility),
not a dependency-bearing example or private body inference.

The member-contract constraint uses the existing witness projection:
`where T.via<I>.Member : [ForwardsBacking(this)]`.
Resolve one exact required callable or property getter and its published
relationship. The initial form admits only the resolved Core ForwardsBacking
schema, optionally with the same explicit destination targets as a declaration.
Source names bind against that requirement's receiver/formals, not the
enclosing caller. Ambiguous/overloaded targets, unestablished facts and arbitrary
attributes/predicates reject. It filters normal applicability after type
selection; it supplies no inference evidence, conformance, ranking preference
or constraint-only overload. Check the generic body under the published
condition and require it at application; do not infer it from private bodies.

[Dependencies and views](language/dependencies-and-views.md) owns the exact
source/target, phase, propagation and witness rules. Physical dependency
registration and encoding remain separate work.
The [selected query catalog](language/registered-queries.md)
defines ordinary Core value-layout APIs; broader query APIs require their
own admitted contracts.

### 5.6 Reserved underscore and discard statement

`_` is reserved and is never an identifier.
`_ = expression;` is the standalone discard statement: evaluate once, preserve
effects and failures, and apply ordinary ownership/cleanup. It is not assignment
and has no name-dependent fallback. Every normally produced non-`void`
expression result must be used or explicitly discarded; a bare value-returning
call, construction, increment, or await is not a unit-valued statement.
`void`/`()` are actual zero-data values, not a result-discard conversion.
Positional wildcard patterns and omitted
lambda parameter names are separate nonbinding contexts; they do not consume
the matched value or alter argument evaluation/parameter mode. A possible
generic type hole is another distinct nonbinding grammar role defined in §3.2,
not a named identifier or a consequence of the discard rule. In a match over `T?`,
`var item` covers the initialized non-null case and `_` can cover the remaining absent case
when those are the only possibilities. The wildcard still means "any
remaining value", not `none`: it also matches other unhandled cases if the
matched type has them. `null` remains the optional absence value. The
bounded switch/pattern productions, qualified union cases, structural tuples
and coverage rules are in §3.3. Arbitrary object extractors and their
registration remain separate work.

```terrum
// Standalone discard statements.
_ = MakeOwner(); // Evaluate once; clean the returned owner at statement end.
_ = int32(42);   // The type call supplies the literal's target.
```

Discard expressions are evaluated once. Any owner/result cleanup follows the
ordinary expression and statement cleanup rules; this does not force
collection. [Expression/statement execution](language/expressions-and-statements.md)
defines the result and cleanup contexts. A discard is also permitted in an
ordinary `for` initializer/step without its internal semicolon.
The inverse-looking `name = _;` is the distinct local lifetime-end statement
in §3.3. It runs the current value's scope-exit obligations without removing
the name's lexical declaration; `_` does not become an assignable value.

<a id="57-unary-operator-contract-direction-accepted-protocol-mapping-remains-open"></a>

### 5.7 Unary operator contracts

This section describes declared nominal operator shapes; the mandatory
[primitive signatures](language/primitive-operators.md#signatures-and-availability)
have their explicit scalar availability and result types.
Ordinary unary `+`, `-`, `!` and `~` have
one operand of the containing type `T` (or `T?`) and separate operand/result
types; `!` is not inherently bool-result. Return type alone does not
distinguish overloads. Static `++` and `--` have operand-shaped results rather
than arbitrary independent result types. There is no derived-result relation.
`true` and `false` are another explicit exception: both must be declared
together and return `bool`. These rules describe the unary contract shape, not the exact
protocol names or source declarations.

```terrum
// Shape notation; selected same-type Core groups are defined separately:
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
other pair constraints from C#. The
[selected same-type Core groups](language/numeric-capabilities.md)
publish their own complete requirements and primitive witnesses.
Broader heterogeneous/result protocols and physical witness/dispatch
metadata remain separate specification work. Existing binary contracts are unchanged.

Instance `++`/`--` and instance compound-assignment operators with `void`
return are not part of this grammar; they cannot be inferred from the static
operator contracts.

**Official comparison sources:** [C# language specification, §15.10.2 Unary
operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#15102-unary-operators)
for unary result relationships and the `true`/`false` pair; [C# operator
overloading reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading)
for the separate instance increment/decrement and compound-assignment
forms. These links are comparisons only; they do not specify Terrum
inheritance, interface names, or runtime behavior.

### 5.8 Postcondition syntax for conditional output state

Declaration postconditions are the normal authoring
route for conditional output initialization facts, with an `ensure <expr>`
clause whose expression is an ordinary Boolean expression. Ordinary patterns
and short-circuit expressions compose status and value facts. A legal
predicate over an ordinary value such as `param > 0` implies that value is
initialized. An initialization-reference predicate uses `*param > 0`;
`param is init` tests its descriptor, while `*param is init` tests the
destination. No redundant destination-state test is needed after a legally
established value predicate. Source-body/path analysis must establish that
each value read is legal and must never read vacant storage to discover a false
predicate. A successful value predicate implies initialization. A live
complete value, an initialized nullable value containing null, and an
explicitly uninitialized destination remain distinct states. Callers retain
checked correlation with a discriminator and cannot access or clean up a
vacant arm. Do not add a special `ensure-if` statement or equivalent routine
output attributes as a parallel authoring mechanism.

Within a declaration postcondition, contextual `return` denotes the result
of the declaration owning that clause, not a symbol named `result`.
For a `void` result it denotes `()`, including implicit unit fallthrough,
not an unavailable result or a value of some erased `T`.
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
not an invented result reference. In a registered generated producer,
source callable ensure observes the creation cursor. Generated advance/close
clauses observe their own normal result or async completion payload; the
producer body returns unit to finish, not that cursor again.
`return` does not implicitly make a source ensure a per-yield check.

The language-versioned default initializes a boolean-success output on `true`
and leaves it uninitialized on `false`; for other result types, an output is
initialized on every normal return unless an explicit declaration postcondition
specifies a conditional relationship. These defaults are not inferred from
method names. Postconditions may state observable boundary facts, including
relations to unchanged value inputs; such relations are behavioral guarantees,
not optimizer-only hints. A relation to prior state could state a genuine
behavioral obligation, but state-history support is outside this grammar; this does
not imply that such contracts lack value. A clause may refer only to declarations visible at
the method's effective accessibility and permitted by ordinary contract
expression rules. Private representation invariants cannot become hidden
caller preconditions. Caller facts derive from published guarantees checked or
proved for the callee and are invalidated by relevant mutation, alias or
lifetime changes; vacant storage is never read to test initialization.

The contract order is prologue, preconditions, method body,
postconditions, epilogue. Preconditions and postconditions belong to the public
declaration/signature outside the body; the body includes initialization of
local and output values, authored execution and `finally`, local cleanup, and
cleanup of untransferred owned parameters before postconditions. Exceptional
departure has no normal-return postcondition. Entry snapshots and arbitrary
state history are outside this contract model. Declaration clauses use the
[checked pure-expression subset](language/published-predicates-and-effects.md#published-predicate-subset)
at their declared phase. False predicates, partial-operation failures and
invalid-call-domain checks follow ordinary failure/unwinding; valid-input
selected pure calls have no escaping exception effects.
Completed body `finally` blocks are not rerun and
body side effects are not rolled back. This does not add a separate output
publication transaction.

§3.1 gives grouped declaration `require`/`ensure` placement across named
callable forms and the contextual result reference. Lambda clauses follow the target-checked form in §3.4.
Named producer phase mapping is in
[generated producers](language/generated-producers.md); physical
source-to-metadata mapping for correlated initialization facts remains open. There is no
special conditional postcondition block grammar. See the canonical
[conditional output contract](language-contracts.md#2-outcome-initialization-and-nullability-are-separate).

### 5.9 Static-proof floor and constant evaluation

Declaration-level `static require` and `static ensure` have no source production.
Ordinary declaration contracts and required static
body proofs retain their established roles; no caller-proof declaration
qualifier is introduced.

The [canonical floor](language/proof-and-constant-evaluation.md) defines
local scalar constants, Boolean relationships, integer bounds/exclusions,
branch/join/loop transfers, invalidation, and deterministic work/state accounting.
Required proofs are independent of optimization level; sound beyond-floor
analysis can accept more. False, not established and exhausted remain
distinct, without runtime fallback.

`static require expression;` is a body point obligation.
`static ensure expression;` is permitted directly in the outer callable body
and proves every normal semantic completion after body cleanup, not just paths
that reach its textual location. Nesting it in an inner block rejects.
Neither silently publishes a declaration guarantee.

`const T name = expression;` defines a required frozen Copy constant in the
ordinary local declaration space, including a `for` initializer binding.
It requires a complete supported value and encoding, cannot combine with
`mut`/`init`, and does not manufacture an addressable runtime place.
Prefix `const` binds at unary precedence: `const F() + x` requires only `F()`,
while `const (F() + x)` requires the entire grouped expression.
`const(expr)` is that prefix plus grouping, not a separate callable.

Required constant execution uses admitted inline values, private local
mutation and checked references, with eligibility on the selected path.
Emitted values require the per-target Blittable commitment, frozen Copy
validity and admitted typed constant encoding, not today's private layout.
`ConstEval` implies checked pure-form support and explicit Terrum evaluator
opt-in; `Pure` alone is not that opt-in. Neither promises termination,
ordinary-form `NoThrow` or every-input constant success.
An unused known runtime-only operation is distinct from an invalid source
or unknown body schema. Admitted constant NaN-processing arithmetic,
nonidentity numeric conversion and numeric selection use the selected positive
quiet zero-payload recipe. Copies and sign-only negation/Abs retain their
representation contracts; runtime arithmetic NaN sign/payload remain unspecified.
The canonical resource envelope and dependency requirements are logical
contracts, not a proof certificate, wire format, registration or target ABI.

Where runtime values/checks are permitted, an author may explicitly use an
ordinary expression/`require` instead of requiring static evaluation. This is
not a compiler fallback, and ordinary `require` remains enforced unless
soundly redundant. A runtime alternative is unavailable for compile-time-only
layout/type/metadata requirements and does not weaken structural lifetime or
initialization safety. The productions above select the stated proof/const
source forms; examples do not add further forms or evaluator capabilities.

### 5.10 Properties and accessor-local effects

By-value properties on compact `Copy` structs and
source classes use `get`, `set`, and construction-only `init`
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
public struct Point
{
    private int32 _x;

    public int32 X
    {
        [NoThrow] get { return _x; }
    }
}

public class Counter
{
    private int32 _count;

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
`[Pure]` and `[get: Pure]` use those same accessor targets. A Count-like getter
can read receiver state under the unified input-state purity contract;
its complete read dependencies still invalidate on relevant mutation.
NoAlias on an indexer parameter or an exact accessor parameter target
contributes the corresponding invocation requirement. Setter `value` retains
its actual formal identity; ambiguous property-wide selectors do not guess
an accessor.

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
public struct Point
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
construction obligations below. Additional accessor binding profiles and
physical metadata representation remain separate work.
`mut get` and `mut set` follow ordinary method receiver contracts; writing
through an independently writable referent does not by itself mutate `this`.
`init` provides mutability within its initialization scope without a redundant
`mut init` modifier.
For a static property, `init` is available only within its owning type group's
unpublished initialization phase, including checked helpers. Group completion
ends that authority. An ordinary later assignment requires an applicable
setter; neither static lookup nor unsafe authorization silently reopens
initialization.

**Construction rule:** a constructor initializes all private state
it does not leave to declared `init` properties. Its published completion
contract guarantees a minimum set of `init` properties through checked,
published clauses such as `S(int32 value) ensure P is init { P = value; }`.
Each guarantee must hold on every successful constructor return and be
available to downstream source and artifact consumers, not inferred from
body inference or duplicated by a routine attribute. The caller must initialize
each remaining property before the object is fully constructed. In
`new T(...) { P = value; }`, initializer entries execute in source order
after the constructor, within the same unpublished construction phase.
Declaration field/property initializers precede constructor preconditions,
followed by the constructor body and normal-return postconditions.
The caller initializer entries then run, followed by completion validation
and the final construction guarantees. Constructor postconditions establish
the constructor's outcome, not an automatic second check after caller
initialization; later assignments invalidate affected facts under the ordinary
mutation and dependency rules.
Construction publishes the object only after all storage and property
obligations are fulfilled and the final invariant/postcondition checks
succeed. Defined unwinding cleans partial state without publishing a live
object; fail-fast termination promises no cleanup.
The [owning-field contract](language/ownership-and-cleanup.md) unwinds remaining
actual acquisitions in reverse acquisition order. The incomplete parent's
Cleanup never runs, including failed final validation after all fields were
initialized; complete owned children still perform their own complete cleanup.
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
encoding belongs to the metadata contract.
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
These choices preserve the [adapter and witness
semantics](language.md#interfaces-access-and-generic-properties).

### 5.12 Modifiers, cleanup and finalization

`public`/`internal`/
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
binary has a newly appended slot. The
[witness chapter](language/interface-witnesses.md) selects source mapping and
inherited/default resolution; physical witness records and dispatch ABI
remain open. Ordered prefixes and the type-kind/callable
matrix are defined in §3.1; duplicates, conflicting visibility, misordering
and illegal modifier/declaration pairs reject. Named callable contract
clauses follow the parameter list and generic constraints as specified in
§3.1; result types remain in prefix position.

**External-declaration direction:** an external callable explicitly declares
its import identity, calling convention and complete Terrum callable contract.
An ABI annotation cannot suppress caller-unsafe obligations, failure
containment, rooting, access, ownership or loans. `extern` occupies the
primary linkage slot after `static`, applies only to top-level functions and
static methods for callables, and supplies a complete bodyless binding
and cannot combine with `partial` or `async`. Registered annotation identities,
import-selection schemas and target ABI mappings remain specification
work; no P/Invoke or marshalling model is implied.
Static extern fields instead bind data symbols under the separate
representation, access, validity, synchronization and storage-lifetime
contract in §3.1. They do not import a callable ABI or grant permission
for extern properties, accessors or instance fields.
`extension` remains proposed vocabulary for a scoped extension declaration.
Its productions, restrictions, effects and lowering remain open.
Accessors and `when` guards have bounded
productions above without implying complete effect/representation contracts.
The [generated-producer chapter](language/generated-producers.md) owns
`[IteratorCursor]` shells and their `[Iterator]` callable, exact direct
value/readonly/writable sync/async roles, captured frame and clauses.
Creation synchronously stages/captures inputs and checks source require/ensure;
contextual return there is the complete named cursor. The affine result owns
one traversal, not an automatically reusable sequence factory.
Its body starts lazily on first advance and resumes after each
`yield expression;`. Yield evaluates once in exact T or ref<T>/write ref<T>
output context, retains entered scopes and preserves real item/backing
contracts. `return;` or unit fallthrough finishes; yield return, yield break
and producer return expression are invalid.
Yield cannot publish from finally/defer or an ineligible nested callable.
This yield is not the scheduling operation Yield() and implies no worker hop.

Source creation contracts/effects do not become per-yield or final guarantees.
Generated advance/close clauses govern their operations and exact output
phases; body normal-completion ensures run only on successful body completion.
Async source bodies return named async cursors, not Task<Cursor> or a factory
sequence; creation still does not execute them. Async advancement and close
use [established-operation failure](language/async-execution.md), completion
reservation and checked observation regardless of suspension.

Close releases captures without starting an unentered body, or unwinds only
entered suspended scopes. It does not run remaining production, claim normal
completion or replay completed cleanup. Pending operations and published loans
must be settled before close, and async close is awaited before header/source
retirement. Manual consumers retain explicit close responsibility; ordinary
owner destruction cannot substitute throwing/suspending body cleanup.
Generic mapping, source code ownership and generated capability/layout
dependencies are checked, not inferred by name/body resemblance.
General task/cancellation APIs, nonstandard producer registration and physical
frame/phase/loan programs remain separately gated.
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
Complete [owning-value retirement](language/ownership-and-cleanup.md) uses
that declaration order in reverse, after the authored Cleanup body.
Structural partial field moves require ordinary write/init authority and no
escaping incomplete Cleanup receiver. Remaining live fields retain cleanup;
whole-value use requires checked repair/validity, not just a false init test.
`partial` merges explicitly marked type/member declarations across files
in one module, including generated parts, in the order the compiler receives
the files and then source order within each file. Every merged part must
declare `partial`, have matching kind and generic identity, and yield one
resulting identity without duplicate members or implementations. Exactly one
part is the primary, identified by explicit visibility rather than file
order, except that a static constructor's required initialization-policy
attribute identifies its primary. It authors visibility where applicable,
contract-prefix modifiers, conformances,
constraints, defaults and pre/postconditions. Secondary parts must not
repeat or add those contract items; they use only applicable attributes,
`partial` and the matching kind modifiers/kind or return type/signature.
A partial member may have a bodyless primary, but exactly one implementation
body must be provided in the compilation; a secondary cannot change its
signature, published contract or accessor shape. Secondary attributes remain
subject to their resolved schema's placement, target and multiplicity rules.
Contract-changing semantic schemas preserve primary-only authorship.
New members contributed by a secondary type part have their own primaries,
explicit visibility and contracts. Type-kind modifiers repeated by a
secondary identify the primary's storage/ownership form and must match.
A source generator running within the compilation may supply new parts and
implementations, but must not add or alter the published contract of an
existing declaration, including making a method unsafe or changing a
type's layout. Independently pre-generated files have ordinary source-file
status, not the compiler-known provenance of an in-compilation generator.
Partial parts match by declared identity and complete signature, including
generic formal names/order, callable parameters and accessor shape.
Defaults and conditions come from the primary alone; secondaries cannot
accumulate stronger obligations or erase an existing promise. Structural
signature matching uses the required type/constant normalization. Attribute
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
Policy selection does not defer receiver/argument expressions. No Core
construction-evaluation policy domain is introduced; text/collection
construction retains its [eager capture order](language/construction-handlers.md#eager-capture-and-acquisition).

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
and wire encoding belong to the library/metadata contracts. No policy may
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
conversion. Conversion input modes and caller-safety follow §3.1; lookup
and ambiguity follow §5.13. Complete ownership/effect compatibility and
metadata remain open. `operator` already has a token; `cast` and `explicit`
need not
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
The [registered-query chapter](language/registered-queries.md) owns exact
query identities, subject/result contracts and runtime/constant admission.
Core `RuntimeHelpers.SizeOf<T>()` and `AlignmentOf<T>()` are ordinary generic
APIs returning `nuint`, not new keywords. They measure the complete normalized
T value/handle representation under the selected target ABI, not host layout
or an allocated object's payload. `[Sized]` permits admitted runtime layout
evidence without promising a definition-time numeric constant.
A retained layout-dependent result needs a published fixed observation or
explicit rebuild/rebind/invalidation dependencies. Its scalar constant codec
does not freeze the queried layout; today's private representation or an
AOT/JIT label is not that promise. Query results do not widen the separate
constant-generic domains. Universal reflection objects, object-payload queries,
broader query catalogs and constructed-object encodings remain unselected.
Builder type-reference arguments instead retain their
[bound type recipes](language/construction-handlers.md#type-reference-arguments-are-recipes),
not values in an implicit reflection-object constant domain.

There is no `~Type()` finalizer production. Deterministic
`[Cleanup]` for an owning value and scope/unwind cleanup are not GC
finalization. GC finalizers remain absent unless a concrete API need survives
explicit ownership and supervision alternatives. Any future proposal must
review lifecycle, resurrection, affinity, failure/effects and resource safety
together, apart from `[Cleanup]`. Do not synthesize finalizers from owner
cleanup or class declarations.

### 5.13 Operators, assignments and expression gaps

User `operator +=` is separate from
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
writable-place checks, not merely parse as an expression. Built-in scalar
compound assignments have their [checked read/operation/store sequence](language/primitive-operators.md#built-in-updates-and-accessor-targets),
including eligible value getter/setter pairs; they do not make those getters
valid nominal write-ref destinations. Integer shift assignments keep the
same checked source count domain and data width as ordinary shifts.
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
accessible candidate under ordinary ranking, including the established
by-value-versus-implicit-reference rules for the logical source type.
Destination ambiguity diagnoses, and a
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

**Explicit indirection and pointer formation:** `ref place`, descriptor
assignment, safe `*r`, and `r->Member` follow
[references and explicit indirection](language/references.md). Neither
`ref<T>` nor `ptr<T>` implicitly forwards `.` member access to `T`.
Class handles keep ordinary `.` access. `&place` forms an address only with
addressable storage, access, lifetime and stable/rooted backing. Safe
formation requires initialized storage. In an explicit unsafe context,
formation and raw-pointer reads or writes may bypass definite-initialization
checks; they do not establish checked initialization facts for direct local
reads or checked-reference formation. This exception does not waive known
access, provenance, lifetime, alignment, backing/root or pin obligations.
GC storage requires a statically known covering pin; `unsafe` is not a
substitute for rooting or pinning. `pin ref<T>` has an implicit conversion to
the matching `ptr<T>` form, preserving referent access, lifetime, pin and
backing dependencies. This is one explicitly defined built-in conversion
edge, not general user-defined implicit conversion support. It acquires no
pin, grants no stronger access and does not promise identical representation
on every backend. The complete referent type remains the same.
Overload applicability recognizes this direct edge without searching
operators, intermediate types or conversion chains. An identity match is
better than this permitted projection under existing per-argument ranking.
Existing specificity rules apply to ties; unresolved ties or crossed
candidates remain ambiguous. Future exceptions
require separately selected rules, not extrapolation from this one.
Named-local early lifetime ending uses `name = _;` under §3.3 and retains all
pin dependency checks. Pin acquisition/replacement follows §3.2.
Proven lifetime escapes and missing required pins are checked errors,
including in an unsafe context; valid pointer formation itself produces no
warning merely for creating a pointer. Formation does not require unsafe
when its initialization, access, backing/root and pin obligations are satisfied.
Pointer dereference uses ordinary attached prefix `*` and requires unsafe access;
reference dereference uses the same syntax without making it unsafe.
it does not create a new `(* ... )` fence. `*p`, `*(p + offset)`,
`*GetPointer()` and `*pointers[index]` retain ordinary prefix/postfix precedence.
When the unparenthesized operand's postfix receiver chain contains member
selection, grouping must identify the pointer expression or pointee boundary:
`(*p).M` and `*(p.M)` are distinct, while `*p.M` and `*GetPointer().M` reject.
The same grouping restriction applies to explicit reference dereference.
`p->M` and `r->M` supply the explicit pointee-member boundary directly.
Members within call arguments or index expressions are not that receiver
chain. `*p + offset` adds to the dereferenced value; it is not address
arithmetic. `*(*p)` dereferences twice; `**p`, `* *p` and `* p` reject under
the existing operator-run and attachment rules. Grouping supplies neither
validity, bounds, pinning nor write authority.

Nominal `&&`/`||` short-circuiting has no source protocol here. Truth testing,
conditional right-operand evaluation, result and ownership obligations cannot
be inferred from an ordinary binary operator.
The paired `true`/`false` declarations do not derive short-circuiting through
`&`/`|`, nor can an ordinary binary witness license eager evaluation of both
operands. Built-in Boolean short-circuit ordering is unchanged.

The selected union-case/tuple patterns, associated-identity checks,
proof/const forms, constructor call algorithm and standard text/collection
builder contracts do not complete arbitrary object extractors, physical
union/witness/partial-owner/stored-reference representations, family/adapter
declarations, generalized conditional conformance, broader task APIs/general
awaiter and scheduling protocols, nonstandard producer/capture registration,
or physical exception transport. Additional literal/builder profiles and
constant positions/domains require their own contracts.
Source type packs and tuple arity extensions have no production here.
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

The following details are outside this source grammar; attachment and
reference principles do not define an XML artifact schema:

| Surface | Unspecified details |
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

<a id="515-specification-to-tooling-gate"></a>

### 5.15 Conformance

Conforming tooling must implement the lexical, grammatical and static-semantic
rules together, including modifier legality and declaration-body restrictions.
Unspecified contextual-token positions, lexical boundaries and semantic
forms without productions cannot be inferred from informative examples.
Positive and negative source examples should cover each specified boundary;
recovery syntax and informative API examples do not establish new productions.

## 6. Source-form coverage and open boundaries

This index separates concepts for readers; it does not add productions or
define unspecified forms. The sections named in the middle column give the
source forms or semantic constraints. An unspecified boundary
remains unspecified rather than becoming legal merely because a related word
appears in §2.

| Concept | Defined source contract | Unspecified scope |
|---|---|---|
| Lexical tokens | §2 gives keyword escapes, contextual-word positions, explicit operator glyphs/priority, role-based attachment and whole-star comment fences; primitive names are identifiers with reserved bindings. | Malformed floating-token and delimiter recovery. |
| Literals | §§2 and 4 give character-scalar and context-typed numeric forms, including hex-float target rounding; §§2 and 3.4 give raw framing/headers, deterministic interpolation, identifier/quoted/expression formats and runtime alignment. | Complete floating token recovery, character diagnostics, raw/interpolation recovery and registered text/whole-output normalization APIs. |
| Files | §3.1 sketches directive order. | Other directive/file forms, if any. |
| Namespace | §3.1 permits file-scoped or nested block namespaces without mixing, with lexically scoped imports before declarations. | Other namespace forms, if any. |
| Visibility and declaration prefixes | §3.1 defines fixed role-specific primary prefixes, strict source-unit file access, member-scoped same-module friend grants and restricted interface conformance authority. Static constructors have a policy-identified primary without visibility. | Complete access/provenance/grant and initialization-policy metadata encoding. |
| Nominal types | §3.1 gives ordered class/struct kind forms, static-class containers, interfaces, integral-base enums and explicit struct/class unions with qualified cases and independent closedness. Enum constants have checked successor overflow and deliberate aliases; the mandatory scalar catalog is selected separately. | Nested declarations, additional primitive-family bindings, flags schemas and contextual attributes. |
| Generic declarations | §§3.1–3.2 and the constant-generic chapter give bounded domains, derived checked Transparent primitive-wrapper eligibility, canonical nominal/carrier value identity, earlier-only defaults, `_` inference, explicit `default` selection and actual-type `where T : write` requirements; the call chapter defines finite source inference. | Physical generic/value/validity and witness records; broader constant-formal domains. |
| Aliases | §§3.1 and 5.4 give finite qualified expansion, cycle rejection, namespace-level aliases and transparent public export. | Metadata encoding. |
| Associated types and witnesses | §§3.1 and 5.4 give declaration forms; the witness chapter defines inherited/default identity, associated checking, and qualified-self adaptation. | Physical witness/projection metadata and dispatch ABI. |
| Partial declarations | §5.12 defines one-identity merge, complete-signature agreement, primary-owned defaults/contracts and required body provision; §3.1 requires resolved attribute schemas/multiplicity. | Concrete attribute catalogs/encoding and any explicitly permissioned future API-absence mechanism. |
| Core primitive declarations | §2 preserves reserved primitive identities with a privileged backing field. | Artifact identity, permitted members and separate-compilation enforcement. |
| Fields | §3.1 gives storage/referent authority, caller-unsafe access, foreign-data bindings, load-scoped static groups and lazy fields; [owning fields](language/ownership-and-cleanup.md) gives move/repair and cleanup, and [sharing](runtime/sharing-and-handoff.md) gives explicit capability opt-in, local conditions and admitted authority protocols. | Concrete initialization-policy/recovery APIs, concurrent primitive catalogs, unload machinery, external-data mappings and partial-state/layout metadata. |
| Constructors | §§3.1 and 5.10 give instance completion guarantees, repeatable unpublished init writes, static constructor policy/partial ownership and field/constructor/caller/final-validation ordering. | Completion-validation registration, value/effect compatibility and metadata encoding. |
| Properties | §§3.1, 5.10 and 5.12 give instance/static named properties, instance-only indexers, accessor-local contracts and static init authority limited to unpublished group initialization. | Physical ref-result records and ABI; external accessor binding needs a separate interop design. |
| Operators | §§3.1, 5.7 and 5.13 give static explicit operands, eligible implicit-reference inputs, ordinary visibility, synchronous caller-safety, partial primary ownership and distinct void compound operations. | Complete applicability and witness/metadata encoding; external binding needs a separate interop design. |
| Conversions | §§3.1 and 5.13 give static by-value/implicit-reference logical inputs, synchronous caller-safety, partial primary ownership, destination-first `T(value)` and `.via<Owner>` selection, distinct from `new T(...)`. | Complete eligible conversion catalogs and binding/metadata diagnostics; external binding needs a separate interop design. |
| Interface members | §§3.1, 5.4 and 5.12 and the witness chapter give explicit qualified implementations, shared implicit implementations, inherited/default resolution and restricted current-receiver default-body bypass. | Physical witness representation and dispatch ABI; constructors have no production here. |
| Types | §3.2 gives recursive types, tuple types and normalized optionals. | Remaining owning forms, unsupported reference/view representations and public tuple ABI. |
| Ownership | §§3.2 and 5.12 sketch owner forms and explicit transfer. | Complete ownership-form grammar. |
| Reference access | §§3.2 and 5.12 and the reference/type chapters define the admitted loans, qualifications and complete-type normalization. | Additional owning/view storage profiles, physical stored-reference representation and public ABI. |
| Parameters | §§3.2, 5.1–5.2 and the reference/type chapters give modes, qualifier legality, final `params`, eligible Copy defaults and direct/named attribute targets. | Physical output/loan metadata and unsupported representation contracts. |
| Arguments | §§3.2 and 5.1 and the call chapter give labels, source-order evaluation, position-preserving named/positional mixtures, normal/expanded forms and finite inference/ranking. | Concrete extension eligibility schemas and physical recorded-binding contracts. |
| Statements | §3.3 and [ordinary execution](language/expressions-and-statements.md) give expression/place categories, inhabited unit values, explicit non-unit result discard, scope/loop transfers, vacant locals, result checking and fallthrough. [Generated producers](language/generated-producers.md) gives named shells, yield/body completion and owned close. | Nonstandard producer and associated lifetime contracts; physical frame/phase records. |
| Iteration | §3.3 gives post-tested `do` loops and by-value/`ref`/`mut ref` iteration, including `await for` and structured close; the iteration chapters select synchronous/async value and distinct readonly/writable lending roles, single-output publication, completion reservations, element aliases and checked backing refinements; generated producers supply named affine bodies/captures and exact creation/operation phases. | Broader task/cancellation APIs, nonstandard producer registration and physical records. |
| Dependency sources and captures | §§3.1 and 5.5 and [dependencies and views](language/dependencies-and-views.md) define whole receiver/formal recipes, result/init-output and retained destinations, backing-only refinements, copies/joins/versions and exact member-contract constraints. | Physical source/capture/anchor records, arbitrary source-path predicates and separately selected task/closure/concurrency protocols. |
| Core task ownership | §3.4 and [task responsibility](language/task-responsibility.md) define affine Task<T>, consuming await with explicit named move, observation/payload/output facts and every-exit settlement or transfer. | General user awaiter registration, observation/supervisor/join/cancellation APIs and physical task/wait/root records. |
| Expressions | §3.4 gives conditional/coalescing/optional access, half-open slicing, end-relative bounds, tuples, target-directed static/async lambdas, await and collection spreads. | Slice/first-class index/range protocols, callable capture/representation, awaiter registration and collection-fragment contracts; comprehensions/repetition have no production here. |
| Patterns | §§3.3–3.4 and [structural flow](language/initialization-and-flow.md) give inferred/value/reference bindings, generic-slot type matching and named capture, checked `typeof` identity refinement, distinct initialization tests, qualified union cases, structural tuples, per-attempt observations, ordinary declaration rules for `or`, explicit mutable rematching, retained failed-match values, ordinary `when` conditions and explicit transfers. Omitted eligible output arguments select availability-based lifting, including partial omission and initialization-requiring extraction. | Runtime-only type-witness representation, registered object observations and richer-status/projection metadata. General application/constraint `var` binders, higher-kinded application, dedicated member/relational pattern syntax and pattern `where` are excluded from these forms. |
| Policies | §§3.3–3.4 and 5.12 describe lexical preferences, strict selection, signature-bound variants and constant-formal forwarding. | Registered variant attribute/catalog encoding and general policy-query subjects. |
| Contracts | §§3.1, 3.3–3.4 and 5.8 give grouped named/lambda `require`/`ensure`, explicit runtime failure selectors, contextual result `return`, distinct body-level checks, producer creation/operation phases and established async failure channels. | Nonstandard phase selectors and physical correlated-output/require-default metadata; declaration-level static qualifiers have no production here. |
| Static proof and constants | §5.9 and the proof/evaluation chapter give body proof/const forms, scalar facts/transfers, path eligibility, deterministic NaNs, dependencies and resource accounting; the query chapter selects ordinary Core value-layout APIs and explicit observation dependencies. | Broader intrinsic/query catalogs and physical query/dependency/constant-program/value-ABI records. |
| Exceptions | §§3.3–3.4, [local failures](language/local-failures.md), [scalar failures](language/scalar-failures.md), [numeric selection](language/numeric-selection.md), [requirement failures](language/requirement-failures.md) and [captured exceptions](language/captured-exceptions.md) give nominal opt-in, exact scalar/bounds payloads, borrowed/Copy catches, incremental selection, fresh/active/captured responsibility and source-owned require construction/defaults. | Other compiler/runtime failure catalogs, shared/typed capture families, physical constructor/default/owner/payload/handler/cause records and selected runtime support. |
| Cleanup | §§3.3 and 5.12 and [owning fields](language/ownership-and-cleanup.md) define the exact Cleanup role, complete body-then-reverse-declaration fields, failed-construction reverse acquisition and guarded partial retirement; lexical `defer` remains separate. | Arbitrary body/effect proof, physical cleanup/partial-state programs and complete storage profiles. |
| Finalization | §5.12 leaves finalizers absent unless a concrete need warrants a separate reviewed lifecycle contract. | Any future finalizer proposal and its lifecycle/ABI contract. |
| Documentation comments | §§1 and 5.14 describe attachment and structured prose direction. | Exact markup/reference grammar and XML artifact schema. |

The specified lexical, grammatical and static-semantic rules establish
conformance within their stated scope.
