# Terrum grammar proposal

> [!IMPORTANT]
> Proposal only: this document is not an approved grammar or a claim of
> implementation. Accepted semantic direction does not approve the candidate
> productions below.

This proposal covers source notation, candidate syntax, and selected semantic
constraints. It is not a complete or approved language specification; exact
grammar and several semantic contracts remain open. Examples are informative
unless explicitly identified as accepted semantic direction.

The [language](../language.md), [memory model](../memory-model.md),
[conversions](../conversions.md), [lifetimes](../lifetimes.md), and
[libraries](../libraries.md) documents define the related accepted semantic
direction. The [approved current semantic IL/metadata design](terrum-il-metadata-spec.md)
and [core-library proposal](terrum-core-library-reference.md) are separate
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
- Numeric tokens are unsuffixed decimal, hexadecimal (`0x`/`0X`), or binary (`0b`/`0B`) forms with separators between digits and, for based values, immediately after the prefix. Decimal fraction/exponent spellings lex but are not executable values. Signs are separate operators except an exponent sign. Width suffixes are rejected; `10i32` is not a typed literal. `_123` is an identifier.
- Strings use `"..."` with the documented escapes; no interpolation exists. Character literals and numeric suffixes are not tokens.
- Lexical diagnostics preserve malformed source spelling; bad characters/surrogates, invalid escapes, unterminated literals/comments, bad prefixes, separators, and attached width suffixes have distinct diagnostic codes in the lexical reference.

The schematic lexical terminals used below are:

```ebnf
Identifier       = lexer identifier token ;
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

`StringCharacter` excludes an unescaped quote, backslash, or line break; `\U` must decode to a Unicode scalar. Separators may repeat between digits and after a based prefix, but not at the end of a run or beside a decimal point/exponent marker/sign. The lexical reference defines malformed-token recovery and exact diagnostics.

`Number` is one token: unary `-` is an operator, not part of the number.
Numeric target selection/range and the lack of a default type are semantic
rules (see [conversions](../conversions.md)).

## 3. Candidate grammar productions (PROPOSED)

The productions below are candidates, not an adopted grammar. `Identifier`
means one lexical identifier token; dotted names are written explicitly.
Semantic constraints are stated separately.

### 3.1 Source-file structure and declarations

```ebnf
CompilationUnit  = { UsingDirective }, [ FileNamespace ], { TopDeclaration }, EOF ;
UsingDirective  = "using", DottedIdentifier, ";" ;
FileNamespace   = "namespace", DottedIdentifier, ";" ;
DottedIdentifier = Identifier, { ".", Identifier } ;

TopDeclaration = Prefix, ( StructDecl | ClassDecl | InterfaceDecl
                            | FunctionDecl | OperatorDecl ) ;
MemberDeclaration = Prefix, ( StructDecl | ClassDecl | InterfaceDecl
                            | ConstructorDecl | FunctionDecl | OperatorDecl
                            | FieldDecl ) ;
Prefix          = { Attribute }, { Modifier | "abstract" } ;
Attribute       = "[", Identifier, "]" ;
Modifier        = "public" | "internal" | "private" | "static" | "mut" | "unique" ;

StructDecl      = "struct", Identifier, [ ConformanceList ], "{", { MemberDeclaration }, "}" ;
ClassDecl       = "class", Identifier, "{", { MemberDeclaration }, "}" ;
InterfaceDecl   = "interface", Identifier, TypeParameterList,
                  "{", { InterfaceMemberDeclaration }, "}" ;
InterfaceMemberDeclaration = Prefix,
                             ( StructDecl | ClassDecl | InterfaceDecl
                             | FunctionDecl | OperatorDecl ) ;
ConformanceList = ":", Type, { ",", Type } ;
ConstructorDecl = Identifier, ParameterList, Block ;
FunctionDecl    = Type, Identifier, [ TypeParameterList ], ParameterList,
                  { ConstraintClause }, Block ;
OperatorDecl    = Type, "operator", OperatorSymbol, ParameterList, ( ";" | Block ) ;
FieldDecl       = Type, Identifier, [ "=", Expression ], ";" ;

TypeParameterList = "<", Identifier, { ",", Identifier }, ">" ;
ConstraintClause  = "where", Identifier, ":",
                    ( Type | "[", Identifier, "]" ),
                    { ",", ( Type | "[", Identifier, "]" ) } ;
OperatorSymbol    = "+" | "-" | "*" | "/" | "%" | "&" | "|" | "^"
                  | "==" | "!=" | "<" | "<=" | ">" | ">=" ;
```

This candidate requires a single file-scoped namespace after using directives
and before declarations. Fields and constructors occur in class/struct bodies;
interface members do not include fields or constructors. These are proposed
grammar boundaries, not implementation facts.

### 3.2 Types, parameters, and arguments

```ebnf
Type            = [ "write" ], ( "void" | DottedIdentifier ),
                  [ "<", Type, { ",", Type }, ">" ], [ "?" ] ;
ParameterList   = "(", [ Parameter, { ",", Parameter } ], ")" ;
Parameter       = [ PassingMode ], Type, Identifier ;
PassingMode     = "ref" | "out" | "mut", "ref" ;

Arguments       = "(", [ Argument, { ",", Argument } ], ")" ;
Argument        = [ Identifier, ":" ],
                  ( "out", "var", Identifier | [ PassingMode ], Expression ) ;
```

Repeated optional markers such as `T??` are not a proposed source form.
Optional normalization such as `T?` with an already-optional substitution is a
separate semantic rule. In these productions, `mut` in a parameter is a
passing mode (`mut ref`), not a type argument. Grammar does not itself grant a
particular lifetime, write authority, representation conversion, or generic
variance.

Argument labels and passing modes are source forms only; argument reordering,
overload resolution, and output behavior are semantic contracts. The proposed
`Type` production does not define `init ref<T>`; `init` is a distinct argument
mode with the accepted call-site spelling `Create(init value)`.

### 3.3 Statements and local declarations

```ebnf
Block           = "{", { Statement }, "}" ;
Statement       = Block | IfStatement | WhileStatement | ForStatement
                | RequireStatement | TryStatement | UnsafeStatement
                | ReturnStatement | LoopTransfer | LocalDecl
                | AssignmentStatement | ExpressionStatement | ";" ;
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
AssignmentStatement = Expression, "=", Expression, ";" ;
ExpressionStatement = Expression, ";" ;
ExpressionStatementNoSemi = Expression, [ "=", Expression ] ;
```

`for` initializers and iterators omit their internal semicolon; an iterator
may be an expression or one `=` assignment. Local declarations require an
initializer. Blocks are required after `if`, `while`, `for`, `try`, `catch`,
`finally`, and `unsafe`; a single unbraced statement is not part of this
candidate. `catch` has no exception type or binding syntax. Assignment is a
statement-level form, not a general right-associative expression. Recovery
forms are not alternate accepted syntax.

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
  Conditional outputs follow [declaration postconditions](../language-contract-review.md#2-outcome-initialization-and-nullability-are-separate):
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

For `Foo(5).Bar(); Baz();`, materialization is shorthand for initializing an ordinary hidden local at the argument's original evaluation point in the current enclosing lexical scope, then passing its readonly reference. The local is not hoisted to scope entry and does not create a shorter lifetime-only scope. If conditional or short-circuit control flow skips the argument, no local is initialized and there is no owner to clean up. Scope exit follows ordinary initialized/moved-state cleanup. A valid returned reference may be used by later statements in that scope, subject to ordinary declared dependencies; existing place/backing lifetimes are not widened and capture/suspension/escape permissions do not change. See the canonical [reference contract](../language-contract-review.md#1-one-call-and-reference-model) and [implicit-loan lifetime rules](../lifetimes.md#parameter-only-implicit-readonly-loans). General source/loan metadata remain open.

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
[conditional output contract](../language-contract-review.md#2-outcome-initialization-and-nullability-are-separate).

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

The examples elsewhere use `{ get; }` sketches but do not say how a getter body
is authored or where `[NoThrow]` attaches. An explicit body should make the
getter an ordinary checked read-only callable member:

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

## 6. Examples and conformance boundary

Examples illustrate status and semantic rules; none independently defines
grammar or establishes implementation support.

| Status | Example | Rule illustrated |
|---|---|---|
| Accepted direction | `Create(init value);` | `init` denotes vacant-storage initialization and is distinct from `out` or writable access. Exact source grammar and publication contracts remain open. |
| Accepted direction | `int32 Parse(int32 x) { return x; }` and `uint32 Parse(uint32 x) { return x; }` | Same-name declarations are source-time overload candidates. `Parse(42)` is ambiguous when neither candidate is better; return type is not a tie-breaker. |
| Accepted direction | `F(value)` with by-value `T` and `implicit ref<T>` candidates | The by-value match wins when both apply. |
| Accepted direction | `_ = MakeOwner();` | Evaluate once, discard the result, and apply ordinary ownership and cleanup. |
| Not a proposed form | `T??` | Repeated optional markers are not source syntax; optional normalization is a separate semantic rule. |
| Open | `operator +(T operand) -> R;` | Illustrative operation contract; protocol names and declaration syntax are unresolved. |

An implementation conforms only to an adopted language specification and its
specified lexical, grammatical, and static-semantic rules. A recovery tree or
an example accepted in isolation is not evidence of conformance. Actual
implementation and historical support details are maintained in the
[implementation reference](../implementation.md); the lexical contract is in
the [lexical grammar](../lexical-grammar.md).
