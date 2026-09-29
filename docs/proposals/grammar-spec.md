# Terrum grammar proposal

> [!IMPORTANT]
> Proposal only: this document is not an approved grammar or a claim of
> implementation. Accepted semantic direction does not approve the candidate
> productions below.

This proposal covers source notation, candidate syntax, and selected semantic
constraints. It inventories known gaps as well as candidate productions so
that a tool does not mistake lexer recognition or an example for support.
It is not an approved language specification; exact grammar and several
semantic contracts remain open. Examples are informative unless explicitly
identified as accepted semantic direction.

The [language](../language.md), [memory model](../memory-model.md),
[conversions](../conversions.md), [lifetimes](../lifetimes.md), and
[libraries](../libraries.md) documents define the related accepted semantic
direction. The [approved current semantic IL/metadata design](../il-metadata.md)
and [core-library proposal](core-library-reference.md) are separate
documents; cross-references do not adopt their candidate forms.

## 1. Scope, status, and notation

The notation is EBNF-like: `{ X }` means zero or more, `[ X ]` means
optional, `|` means alternative, terminals are in quotes, and named lexical
tokens such as `Identifier` are supplied by the lexer. A production labeled
**PROPOSED** is a candidate, not normative grammar. **ACCEPTED DIRECTION** identifies a semantic constraint that is normative in
the canonical language references; it does not approve the syntax used to
illustrate it. **OPEN**
marks a decision not made by this proposal. Examples are informative and
conformance cannot be inferred from matching an example.

The proposal uses three distinct kinds of statement: accepted semantic
constraints, candidate source forms, and unresolved choices. It makes no
compatibility promise or implementation claim.

This is a bounded source-language proposal, not a complete grammar for all
language features. A lexical token does not by itself establish a production.
Recovery syntax is not a conforming source form.

**Documentation-comment direction (accepted source form, detailed grammar
open):** attach `///` documentation to declarations, with `/** ... */` as an
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

## 2. Lexical notation

The lexical contract is specified in the canonical
[lexical grammar](../lexical-grammar.md). The following summarizes relevant
syntax boundaries:

- Source positions and token lengths count UTF-16 code units. Identifier starts/continues follow the pinned Unicode 17.0.0 XID properties plus `_`; spelling is ordinal, case-sensitive, and not normalized. Keywords are lower-case ASCII token spellings. Primitive names such as `int32` are ordinary identifiers.
- Whitespace and comments are leading trivia; line breaks include CRLF, CR, LF, NEL, LS, PS. Line/block comments do not nest. EOF owns trailing trivia.
- The current lexer recognizes unsuffixed decimal, hexadecimal (`0x`/`0X`), or binary (`0b`/`0B`) numbers with separators between digits and, for based values, immediately after the prefix. Decimal fraction/exponent spellings lex but are not executable values. Hexadecimal floating-point syntax below is a **proposal**, not part of that lexer contract. Signs are separate operators except an exponent sign. Width suffixes are rejected; `10i32` is not a typed literal. `_123` is an identifier.
- Strings use `"..."` with the documented escapes; no interpolation exists. Character literals and numeric suffixes are not tokens.
- Lexical diagnostics preserve malformed source spelling; bad characters/surrogates, invalid escapes, unterminated literals/comments, bad prefixes, separators, and attached width suffixes have distinct diagnostic codes in the lexical reference.

The schematic lexical terminals used below are:

```ebnf
Identifier       = lexer-identifier-token ;
Number           = DecimalNumber | HexInteger | BinaryInteger ;
DecimalNumber    = DecimalDigits, [ ".", DecimalDigits ],
                   [ ( "e" | "E" ), [ "+" | "-" ], DecimalDigits ] ;
HexInteger       = ( "0x" | "0X" ), { "_" }, HexDigit, { { "_" }, HexDigit } ;
BinaryInteger    = ( "0b" | "0B" ), { "_" }, BinaryDigit, { { "_" }, BinaryDigit } ;
DecimalDigits    = Digit, { { "_" }, Digit } ;
Digit            = "0" | "1" | "2" | "3" | "4" | "5" | "6" | "7" | "8" | "9" ;
HexDigit         = Digit | "a".."f" | "A".."F" ;
BinaryDigit      = "0" | "1" ;
String           = '"', { StringCharacter | Escape }, '"' ;
Escape           = "\n" | "\r" | "\t" | "\\" | '\"' | "\0"
                 | "\u", HexDigit, HexDigit, HexDigit, HexDigit
                 | "\U", HexDigit, HexDigit, HexDigit, HexDigit,
                   HexDigit, HexDigit, HexDigit, HexDigit ;
Boolean          = "true" | "false" ;
Null             = "null" ;
Trivia           = whitespace | line-comment | non-nesting-block-comment ;
```

`lexer-identifier-token` denotes the single lexical Identifier token, excluding
keywords and the standalone reserved `_`; it is not three grammar symbols.
`StringCharacter` excludes an unescaped quote, backslash, or line break; `\U` must decode to a Unicode scalar. Separators may repeat between digits and after a based prefix, but not at the end of a run or beside a decimal point/exponent marker/sign. The lexical reference defines malformed-token recovery and exact diagnostics.

`Number` is one token: unary `-` is an operator, not part of the number.
Numeric target selection/range and the lack of a default type are semantic
rules (see [conversions](../conversions.md)).

**PROPOSED lexical extension, not currently recognized or executable:** a hex
float requires a binary exponent (`p`/`P`) so `0x1f` remains an integer and
`0x1.fp+2` denotes a candidate floating token. It may have digits on either
side of the point, but not neither. The exponent has decimal digits; `_`
separators follow the existing between-digits rules. A leading sign is a
separate unary operator, apart from the exponent sign.

```ebnf
HexFloat       = ( "0x" | "0X" ), ( { "_" }, HexDigits, [ ".", [ HexDigits ] ]
               | ".", HexDigits ), ( "p" | "P" ), [ "+" | "-" ], DecimalDigits ;
HexDigits      = HexDigit, { { "_" }, HexDigit } ;
```

The hex-float proposal does **not** extend the current `Number` production or
override the [lexical grammar](../lexical-grammar.md); doing so requires
coordinated token boundaries and diagnostics (including incomplete `p`
exponents, separators and identifier tails), target-format rounding and
literal conversion rules, and implementation tests. Neither decimal nor
hexadecimal floating spellings have an executable value in the present
frontend.

Primitive names (`int32`, `float32`, `bool`, `char16`, etc.) are ordinary
identifiers resolved against compiler/library types, **not** reserved lexical
keywords. The proposed width-explicit integer, floating, character, vector
and decimal families are listed in the
[language design](../language.md#primitives-strict-arithmetic-and-text);
their names and available targets are not inferred from this production.
`void` is a distinct keyword. The current lexer's keyword tokens are `using`,
`namespace`, `public`, `internal`, `private`, `static`, `void`, `var`, `mut`,
`ref`, `out`, `if`, `else`, `while`, `return`, `true`, `false`, `struct`,
`class`, `interface`, `enum`, `union`, `unique`, `own`, `move`, `new`, `for`,
`foreach`, `break`, `continue`, `switch`, `case`, `default`, `null`, `async`,
`await`, `require`, `try`, `catch`, `finally`, `throw`, `unsafe`, `abstract`,
`operator`, `where`, and `write`. Recognition alone does not authorize any
source form. `abstract` remains a reserved lexical token for the bounded
parser's existing syntax; it is not a declaration modifier in the target
interface grammar below. Candidate words such as `init`, `implicit`, `params`,
`ensure`, `get`, `some`, `is` and `const` need explicit
keyword-versus-contextual-token decisions before adoption; they are not all
current keyword tokens. In particular,
§3.4 uses `is` contextually although it is not a current keyword token. `_` is
its own reserved token.

## 3. Candidate grammar productions (PROPOSED)

The productions below are candidates, not an adopted grammar. `Identifier`
means one lexical identifier token; dotted names are written explicitly.
Semantic constraints are stated separately.
The prefix names below are metasyntactic placeholders, not productions: each
stands for only the attributes/modifiers valid in its declaration context.
Their exact sets and combinations remain **OPEN**.

### 3.1 Source-file structure and declarations

```ebnf
CompilationUnit  = { UsingDirective }, [ FileNamespace ], { TopDeclaration }, EOF ;
UsingDirective  = "using", DottedIdentifier, ";" ;
FileNamespace   = "namespace", DottedIdentifier, ";" ;
DottedIdentifier = Identifier, { ".", Identifier } ;

TopDeclaration = TopPrefix, ( StructDecl | ClassDecl | InterfaceDecl
                              | EnumDecl | FunctionDefinition ) ;
MemberDeclaration = MemberPrefix,
                    ( ConstructorDecl | FunctionDefinition | OperatorDefinition
                    | FieldDecl | PropertyDefinition ) ;
InterfaceMemberDeclaration = InterfaceMemberPrefix,
                             ( InterfaceFunction | InterfaceOperator
                             | InterfaceProperty ) ;
TopPrefix       = ContextualPrefix ;
MemberPrefix    = ContextualPrefix ;
InterfaceMemberPrefix = ContextualPrefix ;
Attribute       = "[", Identifier, "]" ;
DeclarationModifier = "public" | "internal" | "private" | "static"
                    | "mut" | "unique" | "unsafe" ;

StructDecl      = "struct", Identifier, [ TypeParameterList ],
                  [ ConformanceList ], { ConstraintClause },
                  "{", { MemberDeclaration }, "}" ;
ClassDecl       = "class", Identifier, [ TypeParameterList ],
                  [ ConformanceList ], { ConstraintClause },
                  "{", { MemberDeclaration }, "}" ;
InterfaceDecl   = "interface", Identifier, [ TypeParameterList ],
                  [ InterfaceBaseList ], { ConstraintClause },
                  "{", { InterfaceMemberDeclaration }, "}" ;
EnumDecl        = "enum", Identifier, ":", IntegralType,
                  "{", [ EnumMember, { ",", EnumMember }, [ "," ] ], "}" ;
EnumMember      = Identifier, [ "=", ConstantExpression ] ;
ConformanceList = ":", Type, { ",", Type } ;
InterfaceBaseList = ":", Type, { ",", Type } ;
IntegralType    = DottedIdentifier ;
ConstantExpression = Expression ;
ConstructorDecl = Identifier, ParameterList, Block ;
FunctionDefinition = Type, Identifier, [ TypeParameterList ], ParameterList,
                     { ConstraintClause }, Block ;
InterfaceFunction = Type, Identifier, [ TypeParameterList ], ParameterList,
                    { ConstraintClause }, ( ";" | Block ) ;
OperatorDefinition = Type, "operator", OperatorSymbol, ParameterList, Block ;
InterfaceOperator = Type, "operator", OperatorSymbol, ParameterList,
                    ( ";" | Block ) ;
FieldDecl       = Type, Identifier, [ "=", Expression ], ";" ;
PropertyDefinition = Type, Identifier, "{", GetterDefinition, "}" ;
GetterDefinition = { Attribute }, "get", Block ;
InterfaceProperty = Type, Identifier, "{", InterfaceGetter, "}" ;
InterfaceGetter = { Attribute }, "get", ( ";" | Block ) ;

TypeParameterList = "<", Identifier, { ",", Identifier }, ">" ;
ConstraintClause  = "where", Identifier, ":",
                    ( Type | "[", Identifier, "]" ),
                    { ",", ( Type | "[", Identifier, "]" ) } ;
OperatorSymbol  = "+" | "-" | "*" | "/" | "%" | "&" | "|" | "^"
                | "==" | "!=" | "<" | "<=" | ">" | ">=" | "!" | "~"
                | "++" | "--" | "true" | "false" ;
```

This candidate requires a single file-scoped namespace after using directives
and before declarations. Fields and constructors occur in class/struct bodies;
interface members do not include fields or constructors. The `ContextualPrefix`
placeholder is deliberate: attributes and modifiers are checked by declaration
context, and their exact legal combinations remain **OPEN**. It is not a
production that permits every modifier on every declaration.

Top-level and concrete function/operator definitions and concrete property
getters have bodies. Only interface function, operator, and getter declarations
may end in `;`; that form requires a conformance implementation. A body on an
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
bodyless static interface operator is a required compile-time witness; a
bodyful one supplies a default. Static operator witness selection needs no
runtime vtable. The candidate generic type parameters and conformance lists
apply to structs **and** classes; interface base lists express interface
composition, not implementation of an interface by itself.
The candidate integral enum base and constant-valued members preserve unnamed
underlying values; `[Flags]` is an optional declaration contract, not a
different closed enum grammar. `IntegralType` and `ConstantExpression` require
semantic restrictions beyond their syntactic approximations. Only a
named getter-only property is proposed here; neither property production is an
indexer or `this[...]` form. `get` is a candidate contextual token;
accessor effects attach to its attributes, not the entire property. The exact
property/accessor grammar, attribute targets, modifier applicability and
ordering remain **OPEN**. Constructors must name their enclosing type. The
lexer does not yet recognize `++` or `--` as single tokens; their inclusion in
`OperatorSymbol` is a future source-form proposal, not executable syntax.
Nested type declarations are deliberately excluded from this candidate
grammar; this is a proposed scope boundary, not a claim that nested type
arguments in `Type` are excluded. See [§5.11](#511-nested-types-and-scoped-operators).

```terrum
// Design-only target sketch; not executable syntax. The present bounded
// parser still requires "static abstract".
interface IAdd<T>
{
    static T operator +(T left, T right);
    int32 Size();
    int32 DefaultSize() { return 0; }
}
```

### 3.2 Types, parameters, and arguments

```ebnf
Type            = [ "write" ], ( "void" | DottedIdentifier ),
                  [ "<", Type, { ",", Type }, ">" ], [ "?" ] ;
ParameterList   = "(", [ Parameter, { ",", Parameter } ], ")" ;
Parameter       = [ PassingMode ], Type, Identifier ;
PassingMode     = "ref" | "out" | "mut", "ref" ;

Arguments       = "(", [ Argument, { ",", Argument } ], ")" ;
Argument        = [ Identifier, ":" ],
                  ( "out", "var", Identifier | [ PassingMode ], Expression
                  | "init", Expression ) ;
```

Repeated optional markers such as `T??` are not a proposed source form.
Optional normalization such as `T?` with an already-optional substitution is a
separate semantic rule. In these productions, `mut` in a parameter is a
passing mode (`mut ref`), not a type argument. Grammar does not itself grant a
particular lifetime, write authority, representation conversion, or generic
variance.

Argument labels and passing modes are source forms only; argument reordering,
overload resolution, and output behavior are semantic contracts. The proposed
`Type` production does not define `init ref<T>`; `init` is a
distinct initialization-loan parameter mode, with the accepted call-site
spelling `Create(init value)`. `write` qualifies access in a type (for example,
`write ref<T>`); `mut` qualifies a binding/receiver or a `mut ref` mode.
Neither `write` nor `init` is an unrestricted declaration modifier. Exact
`init ref<T>` parameter grammar and legality of `write ref<T>` in `Parameter`
remain open, and this bounded production must not be used to reject the
accepted forms.

### 3.3 Statements and local declarations

```ebnf
Block           = "{", { Statement }, "}" ;
Statement       = Block | IfStatement | WhileStatement | ForStatement
                | RequireStatement | TryStatement | UnsafeStatement
                | ReturnStatement | LoopTransfer | LocalDecl
                | DiscardStatement | AssignmentStatement
                | ExpressionStatement | ";" ;
IfStatement     = "if", "(", Expression, ")", Block,
                  [ "else", ( Block | IfStatement ) ] ;
WhileStatement  = "while", "(", Expression, ")", Block ;
ForStatement    = "for", "(", [ ForInit ], ";", [ Expression ], ";",
                  [ ForStep ], ")", Block ;
ForInit         = LocalDeclNoSemi | ExpressionStatementNoSemi | empty ;
ForStep         = ExpressionStatementNoSemi | empty ;
RequireStatement = "require", Expression, ";" ;
TryStatement    = "try", Block, ( "finally", Block
                  | "catch", Block, [ "finally", Block ] ) ;
UnsafeStatement = "unsafe", Block ;
ReturnStatement = "return", [ Expression ], ";" ;
LoopTransfer    = ( "break" | "continue" ), ";" ;
LocalDecl       = [ "mut" ], ( "var" | Type ), Identifier, "=", Expression, ";" ;
LocalDeclNoSemi = [ "mut" ], ( "var" | Type ), Identifier, "=", Expression ;
DiscardStatement = "_", "=", Expression, ";" ;
AssignmentStatement = Expression, "=", Expression, ";" ;
ExpressionStatement = Expression, ";" ;
ExpressionStatementNoSemi = Expression, [ "=", Expression ] ;
```

`for` initializers and iterators omit their internal semicolon; an iterator
may be an expression or one `=` assignment. Local declarations require an
initializer. Blocks are required after `if`, `while`, `for`, `try`, `catch`,
`finally`, and `unsafe`; a single unbraced statement is not part of this
candidate. `catch` has no exception type or binding syntax. Assignment is a
statement-level form, not a general right-associative expression; its left
expression must pass a writable-place check, which this syntactic
approximation does not define. `DiscardStatement` is only the standalone
discard form: `_` is not an identifier, pattern wildcard, or omitted lambda
parameter. Recovery forms are not alternate accepted syntax.

### 3.4 Expressions and precedence

The candidate grammar makes binary operators left-associative. Calls,
member-access and index postfixes bind within a primary expression. `is null`
and `is some name` have equality precedence. The proposed precedence table,
highest first:

| Level | Syntax | Associativity |
|---:|---|---|
| 11 | prefix `+ - ! ~ move` | prefix/right-nesting |
| 10 | `* / %` | left |
| 9 | `+ -` | left |
| 7 | `< <= > >=` | left |
| 6 | `== !=`, `is null`, `is some name` | left |
| 5 | `&` | left |
| 4 | `^` | left |
| 3 | `|` | left |
| 2 | `&&` | left |
| 1 | `||` | left |

Comparisons may chain syntactically (`a < b < c`) and require semantic
checking; the grammar does not imply chained-comparison meaning. In
`is some`, `some` is contextual and is followed by an identifier naming the
bound value, e.g. `maybe is some value`.

```ebnf
Expression      = LogicalOr ;
LogicalOr       = LogicalAnd, { "||", LogicalAnd } ;
LogicalAnd      = BitwiseOr, { "&&", BitwiseOr } ;
BitwiseOr       = BitwiseXor, { "|", BitwiseXor } ;
BitwiseXor      = BitwiseAnd, { "^", BitwiseAnd } ;
BitwiseAnd      = Equality, { "&", Equality } ;
Equality        = Relational, { ( ( "==" | "!=" ), Relational | IsTail ) } ;
IsTail          = "is", ( "null" | "some", Identifier ) ;
Relational      = Additive, { ( "<" | "<=" | ">" | ">=" ), Additive } ;
Additive        = Multiplicative, { ( "+" | "-" ), Multiplicative } ;
Multiplicative  = Unary, { ( "*" | "/" | "%" ), Unary } ;
Unary           = ( "+" | "-" | "!" | "~" | "move" ), Unary | Postfix ;
Postfix         = Primary, { ( ".", Identifier | Arguments
                             | "[", Expression, "]" ) } ;
Primary         = "(", [ Expression ], ")" | "new", Type, Arguments
                | Number | String | Boolean | Null
                | Identifier | Identifier, TypeArguments, Arguments ;
TypeArguments   = "<", Type, { ",", Type }, ">" ;
```

`HexFloat` is not in this baseline `Primary`: §2 proposes it as a separate
lexical extension, not as an executable literal or part of the current
`Number` token.

The generic-call alternative is explicit function-call syntax, not a general
expression type-application operator. It does not express partial holes (`_`),
static type methods, constructor-specific generic parameters/inference, or
type packs. The proposed productions do not define conditional `?:`,
null-coalescing, lambda, tuple, range, `await`, `throw`, type-test,
prefix-cast, or general assignment expressions. `T(expression)` is an
ordinary type-call-shaped expression; its checked-conversion interpretation
is semantic.

## 4. Accepted language direction

These accepted semantic constraints do not approve the proposed source forms
or claim an implementation.

- **Access, borrowing, and output state:** canonical forms include `ref<T>`,
  `ptr<T>`, `box<T>`, `val<T>`, `own<T>` and `write`. `mut` qualifies a place,
  nominal declaration or receiver; here `mut ref` is a passing mode, not a
  generic type qualifier. Access and receiver rules follow [language types and
  operations](../language.md#types-and-operations); readonly access does not
  upgrade to writable access. `implicit ref<T>` follows the [parameter-only
  loan contract](../lifetimes.md#parameter-only-implicit-readonly-loans); it
  adds no noescape rule or permission to widen declared dependencies.
  Conditional outputs follow [declaration postconditions](../language-contracts.md#2-outcome-initialization-and-nullability-are-separate):
  `ensure` is an ordinary Boolean postcondition, live/nullable-live/vacant
  states remain distinct, and vacant storage is never read. Exact `ensure`
  placement and metadata remain open (see [§5.8](#58-postcondition-syntax-for-conditional-output-state)).
  `init ref<T>` means
  vacant-storage initialization, distinct from ordinary `ref`/`write ref` and
  `out`; `Create(init value)` is the accepted call-site spelling, with exact
  grammar and output metadata still open.
- **Generic type checking:** explicit parameters, constraints and static
  operator protocols are accepted direction; associated types and family
  membership follow [type-family contracts](../type-families.md#explicit-families)
  and [associated relationships](../type-families.md#nominal-witnesses-and-associated-relationships).
  Optional projection is non-injective and semantically idempotent, but
  repeated source `T??` is not proposed syntax. Inference, partial holes,
  generic methods/classes/constructors, and exact associated-type grammar
  remain open; type packs are out of scope.
- **Functions and overloads:** overload selection is source-time and fixes the
  selected declaration and closed type arguments; runtime virtual/interface
  dispatch is separate. Applicability and ranking follow [callable and
  `params` rules](../language.md#callable-values-params-and-tuples): only
  declared projections/attenuations apply, no implicit numeric/conversion-chain
  search is performed, constraints do not create overloads, return type is not
  a tie-breaker, and applicable members outrank extensions. By-value `T` beats
  `implicit ref<T>` when both apply; fixed arity beats an equally good expanded
  `params` candidate. Exact grammar, diagnostics and extension disambiguation
  remain open.
- **Types and declarations:** type/declaration semantics follow [language
  interfaces and generic properties](../language.md#interfaces-access-and-generic-properties).
  Lexical recognition of `enum`, `union`, `async`, `await`, `switch`, `case`,
  `foreach`, `throw`, `own` or `unique` does not itself define a declaration or
  statement production.
- **Adapters, families, and protocols:** conformance, adapters, associated
  projections and family erasure follow [type-family contracts](../type-families.md#nominal-witnesses-and-associated-relationships).
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
  definitions](../data-layout-and-abi.md#definitions), [fixed
  representations](../data-layout-and-abi.md#fixed-directly-interoperable-representations),
  [packing/alignment](../data-layout-and-abi.md#packing-and-aggregate-alignment),
  and [overlap safety](../memory-model.md#overlap-representation-access-and-unions).
  Exact syntax and profile limits remain open.
- **Text and numeric forms:** [conversion policies](../conversions.md#conversion-policies)
  define numeric behavior and [character intent](../conversions.md#character-intent)
  defines character forms. There are no width suffixes or default numeric type;
  `T(expression)` is a checked conversion, literals are context-typed, and
  numeric operators are checked by default. Floating APIs are target-specific;
  character literals and custom literal registration remain proposed, floating
  forms may lex but are not executable values, and `utf8z("...")` is an
  ordinary call rather than a lexer prefix.

## 5. Open grammar and semantic choices

Examples in this section are informative. **Accepted direction** records
settled semantic constraints; **PROPOSED** forms remain candidates and
**OPEN** choices are unresolved.
See the canonical [language direction](../language.md),
[library layering](../libraries.md#dependency-layers-and-package-boundaries),
and [specification roadmap](../roadmap.md#language-semantic-il-and-metadata-specification)
for authoritative organization and semantic IL/reference boundaries.

### 5.1 Initialization loans at call sites

**Accepted target spelling:** use `init` as the call-site argument mode for an
`init ref<T>` parameter, e.g. `Create(init value)`. It designates vacant
storage and is distinct from ordinary `ref`/`write ref` access and legacy
`out`; it does not make an existing initialized value writable or readable
through the initialization loan.

```terrum
// Accepted semantic direction; candidate grammar form:
Create(init value);
```

This does not define migration from `out` or settle productions for labels,
places, overload binding, or conditional output contracts. It does not
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
[language](../language.md#callable-values-params-and-tuples),
[conversions](../conversions.md#conversion-policies), and
[type families](../type-families.md); exact overload grammar remains open.

The declarations below illustrate accepted overload semantics; their exact
source grammar and diagnostics remain open. No new overload keyword is
required by this direction.

```terrum
// Candidate declarations illustrating accepted overload semantics.
int32 Parse(int32 value) { return value; }
uint32 Parse(uint32 value) { return value; }

void ParseExample()
{
    // Parse(42) is ambiguous; explicit target construction selects int32.
    int32 parsed = Parse(int32(42));
}
```

The accepted `implicit ref<T>` form also participates, but a by-value match
wins for ordinary `Inspect(item)` when both candidates apply. Its exact source
parameter modifier remains open:

```terrum
// Candidate parameter form illustrating accepted overload semantics:
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

**Open:** formalize the complete applicability and declared
specificity relation, interaction with generic inference and the accepted
by-value/`implicit ref<T>`/`params` priorities, exact ambiguity diagnostics,
extension-disambiguation spelling, and serialized binding metadata. This is
accepted semantic direction, not an adopted grammar or complete contract.

### 5.3 Caller-unsafe declarations and explicit unsafe blocks

**Accepted target syntax:** `unsafe` on a callable declaration exposes its
caller obligations, e.g. `unsafe void M()`. An ordinary `void M()` does not
propagate a caller-unsafe obligation. The declaration modifier does not make
its body an unsafe context: unsafe operations inside either form still require
an explicit `unsafe { ... }` block. A caller invoking the unsafe declaration
must likewise satisfy the contract in an explicit unsafe block.

```terrum
// Candidate declaration form:
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

The exact declaration grammar and contract metadata remain open. The example
does not claim compiler support.

### 5.4 Associated types: accepted direction, open source grammar; type packs out of scope

**Accepted semantic direction:** families opt in explicitly and declare
checked associated projections and constraints. Existing explicit
declarations such as `TextSpan<void>` are not reopened; this is not universal
`Type<T>`-to-`Type<void>` erasure. Associated types are named type members of
an interface witness. For example, `IInteger<TSelf>` may declare
`type Wider;`, and a conformance binds it with
`type Wider = MyWideInteger;`. `T.Wider` resolves through the selected
`IInteger<T>` witness. An associated equality may explicitly bind a type
parameter, illustratively `where T : IInteger<T, Wider = U>`. Do not infer the
original `T` backward from an associated result, choose an arbitrary
implementer from a constraint, or silently resolve collisions; ambiguous
witness/projection uses require explicit qualification.

**Open:** formalize the interface/member, implementation
binding, projection, and associated-equality productions; witness selection,
qualification and collision rules; and their metadata representation. Keep
family opt-in and declared checked projections; do not introduce universal
erasure or inferred covariance.

**Source type packs are outside this proposal's scope.** No production or
recommendation for variadic type parameters is made here.

### 5.5 Accepted temporary scope for `implicit ref<T>`

For `Foo(5).Bar(); Baz();`, materialization is shorthand for initializing an ordinary hidden local at the argument's original evaluation point in the current enclosing lexical scope, then passing its readonly reference. The local is not hoisted to scope entry and does not create a shorter lifetime-only scope. If conditional or short-circuit control flow skips the argument, no local is initialized and there is no owner to clean up. Scope exit follows ordinary initialized/moved-state cleanup. A valid returned reference may be used by later statements in that scope, subject to ordinary declared dependencies; existing place/backing lifetimes are not widened and capture/suspension/escape permissions do not change. See the canonical [reference contract](../language-contracts.md#1-one-call-and-reference-model) and [implicit-loan lifetime rules](../lifetimes.md#parameter-only-implicit-readonly-loans). General source/loan metadata remain open.

**Open recommendation:** implicit borrowing introduces no special two-phase
reservation/activation mechanism. Form the reference when the equivalent
explicit loan forms it; if a future ordinary reference or mutating-receiver
contract needs reservation, specify its aliasing, reentrancy and timing
semantics for both spellings first. Convenience syntax must not silently
select a distinct mechanism.

### 5.6 Reserved underscore and discard statement

**Accepted target semantics:** `_` is reserved and is never an identifier.
`_ = expression;` is the standalone discard statement: evaluate once, preserve
effects and failures, and apply ordinary ownership/cleanup. It is not assignment
and has no name-dependent fallback. Positional wildcard patterns and omitted
lambda parameter names are separate nonbinding contexts; they do not consume
the matched value or alter argument evaluation/parameter mode. A possible
generic type hole is another distinct nonbinding grammar role, not a named
identifier and not approved by the discard decision.

```terrum
// Accepted discard semantics; candidate source form:
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
operator +(T operand) -> R;  // T is the containing type (or nullable T?).
operator -(T operand) -> R;
operator !(T operand) -> R;  // R need not be bool.
operator ~(T operand) -> R;
operator ++(T operand) -> T; // Static; operand-shaped result.
operator --(T operand) -> T;
operator true(T operand) -> bool;
operator false(T operand) -> bool;
```

No return-only overload is permitted: result type is part of the selected
operation contract, not a mechanism for distinguishing declarations. The
`true`/`false` pairing is the explicit pair requirement here; do not infer
other pair constraints from C#. Exact Terrum witness/interface names,
constraints, conformance shape, dispatch metadata and source syntax remain
specification work. Existing binary contracts are unchanged.

C# 14 separately permits instance `++`/`--` and instance compound-assignment
operators with `void` return to mutate a receiver. That form is not part of
this proposal and must not be inferred from the static operator direction.

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

**Open:** exact `ensure` clause placement/grammar and the
source-to-metadata mapping for correlated initialization facts. There is no
special conditional postcondition block grammar. A `bool`/`out` form remains
distinct from an `init ref<T>` contract. See the canonical
[conditional output contract](../language-contracts.md#2-outcome-initialization-and-nullability-are-separate).

### 5.9 Static-proof floor and constant evaluation

**Accepted policy, not parser-support claim:** required static obligations use
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

### 5.10 Getter-only properties and accessor-local effects (accepted direction)

**Accepted target direction:** this brief is limited
to explicit getter-only, by-value properties on compact `Copy` structs and
source classes. It does not adopt setters, auto-properties, ref-returning
properties, or general property/accessor behavior.

Interface properties are separate contract members: a bodyless interface
getter requires a conformance implementation, while a getter body supplies a
default under §5.12. This does not broaden the accepted concrete-property
direction above. Indexers (including the core-library `this[...]` sketches)
have no production here and remain design-only.

An explicit concrete getter body should make the getter an ordinary checked
read-only callable member; `{ get; }` on a concrete type is not a body form.
Accessor attributes such as `[NoThrow]` attach to the getter:

```terrum
// Candidate property form illustrating the accepted direction:
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

Accessor effects attach to the accessor: `[NoThrow] get { ... }`. If an
expression-bodied property form is supported, `[get: Attr] public int32 Count
=> 5` is a possible attribute-target direction; this does not select
expression-bodied property support or an alternate effect placement.

An ordinary `[NoThrow] public int32 GetCount() { ... }` method is an
alternative callable form; it is not a property-effect placement option.

Treat the accessor as a named, source-bound
callable member with a readonly receiver and by-value result. The property
declaration and accessor must carry checked member identity and effect metadata;
do not infer a hidden `get_Name` function from a property name. A module may
encode the accessor as a callable internally, but consumers bind the declared
property/accessor identity and contract rather than a magic naming convention.

The exact property and accessor grammar, effect placement, and metadata
representation remain open. This example does not claim compiler support.

### 5.11 Nested types and scoped operators

**Recommendation (PROPOSED):** begin with namespace-level nominal types and
their members; do not make nested type declarations a prerequisite for the
initial general grammar. Private nesting is genuinely useful for hiding
implementation details, tying a helper's type parameters to its container,
and grouping tightly related types. On the other hand, nested public types
couple API naming and accessibility to their outer type, complicate
qualification, generic substitution, metadata identity, source lookup and
binary compatibility, and often read better as separate namespace types.
Module-internal namespace types already hide a helper from external
consumers, though not from the rest of its module. Allowing only private
nesting later is a reasonable next step if use cases justify the added
binding and identity rules; nesting does not imply an implicit outer
instance or capture.

**Recommendation (PROPOSED):** do not allow operator declarations in
`TopDeclaration`. A user operator is selected from the containing type's
declared operation or an applicable checked interface witness. Imports and
extension lookup must not silently add an operator or a witness to a foreign
type. A distinct explicit adapter/wrapper can implement an extension
interface without retroactively changing the original type's witness set;
its operator is a member of that adapter or its interface contract. Define
any later extension-operator syntax, operand eligibility and selection
separately rather than interpreting a global `operator` as an extension.
These choices preserve the [accepted adapter and witness
semantics](../language.md#interfaces-access-and-generic-properties).

### 5.12 Modifiers, cleanup and finalization

**Accepted semantic separation; exact grammar OPEN:** `public`/`internal`/
`private` are visibility; `static` is member placement; `mut` controls a
place/receiver/nominal declaration; `write` qualifies referent access in a
type; `ref`/`out`/`init ref<T>` are distinct parameter/call-site modes.
`unique` is an ownership form, not an access synonym. `unsafe` on a callable
declares caller obligations while an `unsafe { ... }` block scopes operations
inside a body. Interface requirements need no `abstract` modifier: a
bodyless declaration requires a witness implementation, while a body supplies
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
representation remain open. Do not infer legality from the permissive
`ContextualPrefix` placeholder.

The current bounded parser still recognizes `static abstract` operator
declarations. Removing that spelling from the **proposed** grammar does not
silently migrate existing sources or alter the implemented syntax; coordinated
parser, binder, diagnostic and fixture migration is a separate implementation
step.

There is no proposed `~Type()` finalizer production. Deterministic
`[Cleanup]` for an owning value and scope/unwind cleanup are not GC
finalization: a GC finalizer would need a separate lifecycle, resurrection,
threading, failure/effects and resource-safety policy. **OPEN:** whether to
offer finalizers at all; if needed, specify their contract and syntax apart
from `[Cleanup]`. Do not synthesize finalizers from owner cleanup or class
declarations.

### 5.13 Operators, assignments and expression gaps

**Accepted semantics, source form OPEN:** user `operator +=` is a separate declared contract from
`operator +`, using a `write ref<T>` destination; it cannot be desugared to
read/copy/write or inferred from addition. The same question applies to
`-=`, `*=`, `/=`, `%=`, `&=`, `|=` and `^=`: spelling, legal places, precedence,
evaluation order, failure behavior, source tokenization and whether each
operator is supported must be specified together. The current lexer has
neither compound-assignment nor increment/decrement tokens, and the candidate
`AssignmentStatement` only covers `=`. Do not treat compound assignments as
the existing binary operators followed by `=`.

```ebnf
(* PROPOSED extension only; not part of section 3's current token set. *)
CompoundAssignmentStatement = Place, CompoundAssignmentOperator, Expression, ";" ;
CompoundAssignmentOperator = "+=" | "-=" | "*=" | "/=" | "%="
                           | "&=" | "|=" | "^=" ;
Place = Expression ;
```

`Place` requires a separately specified assignability check; an arbitrary
expression is not a writable destination. A concrete declaration production
for each compound operator and source/metadata binding must be designed before
this shorthand becomes a legal statement.

Still **OPEN** for a full source grammar: nominal safe union cases and
patterns/matching, associated-type bindings and family/adapter declarations,
conditional/default conformance, conditional expressions, character and
text-target fragments, general `const` declarations/expressions, `ensure`
placement, `init ref<T>` parameters, optional access/ownership forms,
generic defaults and constructor inference, lambdas, async/await, throw,
typed catches, interpolation and collections. Reserved `switch`, `case`,
`foreach`, `async`, `await`, `union`, `throw` and `own` are not implementations
of those features. They need complete syntax, binding, effects and
representation rules, not isolated extra alternatives in `Statement` or
`Primary`. The baseline `Primary` includes `Number` (decimal fractions
lexically, integer values executable); `HexFloat` is a separate proposed
lexical extension, not a baseline grammar alternative, and cannot become a
lexer token or executable value without the coordinated lexical and numeric
work above.

### 5.14 Documentation-comment grammar and tooling

The accepted attachment, markup and contract boundary is in
[language](../language.md#documentation-comments). The following details are
**open specification work**, not parser or XML-schema approval:

| Surface | What remains to specify |
|---|---|
| Attachment | Exact `///` and `/** ... */` attachment across blank lines, attributes, multiple declarations and generated/partial declarations |
| Markdown | Supported GFM normalization and XML/HTML-style tags; metadata-heading recognition, heading-name case and unknown-heading behavior without treating headings inside code, lists or quotes as metadata |
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

Before promoting this proposal to an approved grammar or using it as a
conformance source for tools, reconcile every production with the canonical
lexical tokens (including contextual keywords and longest-match rules),
restrict modifiers and declaration bodies by context, define all accepted
semantic forms that currently lack productions, specify static semantics
for invalid combinations, and add positive/negative parser and token-boundary
fixtures. Separate the full target language from the bounded
[implemented syntax](../implementation.md);
accepting a candidate spelling in an example does not claim compiler support.

## 6. Coverage and conformance boundary

This map is an index, not an additional grammar or approval. **A** means
accepted semantic direction, **P** a candidate source form only, **O** an
unresolved choice, and **I** support documented by the implementation
reference. These statuses can coexist; implementation does not approve a
proposal, and accepted semantics do not approve its syntax.

| Surface | Coverage and status | Explicit boundary or remaining gap |
|---|---|---|
| Lexing, keywords, built-in names | The canonical lexer contract is [lexical grammar](../lexical-grammar.md); §2 summarizes it and separately proposes `HexFloat`. Primitive type names are identifiers; `void` is a keyword. | `abstract` remains a lexical token for legacy parser syntax, not a target modifier. `is` is used contextually but is not a current keyword token; `get`, `some`, `init`, `implicit`, `params`, `ensure`, and `const` need token decisions. Built-in/library APIs are declarations, not grammar productions. |
| Files and nominal declarations | **P** §3.1 sketches using/file namespace, top-level functions, structs, classes, interfaces, and integral-base enums; interface bases differ from nominal conformances. | Exact modifier/attribute legality and enum constant restrictions remain open. No union or nested nominal declaration is proposed; recursive generic type arguments in `Type` are not excluded. |
| Constructors, members, operators, properties | **P** §3.1 requires bodies for concrete functions/operators/getters; constructors name their owner; operators are type/interface members, not global declarations. **A** §5.7 specifies unary operator direction; §5.10 limits property direction to explicit getter-only by-value properties. | Interface `;` versus body forms are separated in §3.1. Property/accessor effects, attribute targets, and metadata remain **O**; setters, auto/ref-return properties and indexers are not adopted. `++`/`--` appear only as future operator spellings, not current lexer tokens. |
| Interface witnesses and dispatch | **A** §5.12: bodyless members require checked implementations; bodies supply replaceable defaults, selected by declaring-member identity. **P** §3.1 sketches the corresponding `;`/body forms. Static operator witnesses need no runtime vtable. | Explicit mapping syntax, metadata/ABI, inherited-default resolution syntax, and compatible binding for older witnesses remain **O**. No physical slot layout is selected. |
| Types, parameters, arguments, modifiers | **P** §3.2 sketches recursive type arguments, one optional marker, `ref`/`out`/`mut ref`, labels, and `init` arguments. `mut`, `unique`, `unsafe`, visibility and `static` are candidate modifier vocabulary. | `init ref<T>`, `implicit ref<T>`, `write` parameter legality, `params`, generic defaults/inference, associated types, `ensure` contracts and exact modifier combinations remain **O**. Prefix names are context-valid placeholders; `abstract` is deliberately absent from target modifiers. |
| Statements and expressions | **P** §§3.3–3.4 sketch blocks, control flow, locals, calls, assignments, precedence, `is null`/`is some`, and `T(expression)`. **A** §5.6 gives standalone discard semantics; §3.3 records its distinct `_ = expression;` form. | Assignment's left side needs a writable-place check; catch typing/binding, constant-evaluation/static-proof syntax, and omitted constructs (including lambdas, patterns, `throw`, `await`, conditional expressions and general assignment expressions) remain outside this grammar. `is`/`some` token treatment remains open. |
| Literals and compound operations | **I** lexical numbers/strings and target-typed integer direction are described in §§2, 4 and the implementation reference. **A** §5.13 separates compound-operation semantics from `+`. | `HexFloat` is a separate proposed lexical extension, not a baseline `Primary` or executable literal. Compound-assignment spelling, tokens, places and evaluation rules are **O**; `+=`…`^=` shorthand in §5.13 is not legal grammar. |
| Cleanup and finalizers | **A** deterministic `[Cleanup]` is distinct from GC finalization; **I** support is listed by the implementation reference. | Attribute placement is still governed by open contextual-prefix rules. No finalizer production is proposed; whether to support one and its lifecycle/ABI contract remain **O** (§5.12). |
| Implemented boundary | **I** is defined only by [implementation](../implementation.md), not by this proposal's examples or productions. | The bounded parser's existing `static abstract` syntax is unchanged. Nothing else marked **P** is thereby implemented or source-compatible. |

An implementation conforms only to an adopted language specification and its
specified lexical, grammatical, and static-semantic rules. A recovery tree or
an example accepted in isolation is not evidence of conformance. The
implementation boundary is in [implementation](../implementation.md); the
lexical contract is in [lexical grammar](../lexical-grammar.md).
