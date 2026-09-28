# Terrum grammar specification draft

> [!IMPORTANT]
> Proposal-only working reference, not an accepted grammar or a statement of
> current parser support. Consult [current implementation](../implementation.md)
> and [language direction](../language.md) before implementing a production.

**Status: historical parser snapshot and provisional grammar/design reference.** Parser tables and examples record the historical syntax-19 baseline, not a claim about the current branch. The standalone discard migration is verified and implemented in syntax-20/scanner-5/schema-5; wildcard patterns, lambda placeholders and generic type holes remain unsupported. Preserve syntax-19 details only as historical reference. The document distinguishes (A) that parser snapshot, (B) accepted language shapes whose parser/binder support may be incomplete, and (C) productions still requiring exact specification. Where coherent implementation work needs details from this draft, use its concrete working designs provisionally, subject to retrospective correction before v1. This does not imply parser support, blanket approval of every production, or a stability promise; examples remain labeled by status. The canonical lexical contract remains `docs/lexical-grammar.md`; semantic direction and implementation limits remain in `docs/language.md`, `docs/conversions.md`, and `docs/implementation.md`. This companion does not turn an illustrative BCL signature into compilable source.

The authoritative language direction is in [language](../language.md); the
accepted specification organization and IL purpose are summarized in the
[roadmap](../roadmap.md#language-semantic-il-and-metadata-specification),
and library/runtime layering in
[libraries](../libraries.md#dependency-layers-and-package-boundaries).
The [semantic IL/metadata](terrum-il-metadata-spec.md) and
[core-library reference](terrum-core-library-reference.md) are separate
technical drafts, not grammar authority; cross-references do not adopt their
detailed proposals.

## 1. Notation and authority

The notation below is EBNF-like: `{ X }` means zero or more, `[ X ]` means optional, `|` means alternative, terminals are in quotes, and named lexical tokens such as `Identifier` are supplied by the lexer. Productions marked **PARSER** describe what the historical `syntax-19` `Parser.cs` snapshot builds, including constructs that its binder may reject; they do not assert the current branch's parser state. Productions marked **DIRECTION** summarize accepted language intent but are not claims of parser support. Productions marked **PROPOSAL** are candidate productions, not final normative grammar; where needed, they may guide provisional implementation under the roadmap direction and remain correctable before v1.

**Accepted specification organization:** Terrum has one evolving language
specification, organized into syntax, types, static-semantics and execution
chapters. It distinguishes intended rules, actual implementation coverage and
unresolved proposals. The specification is authoritative for current intent,
not frozen forever; intentional pre-v1 changes, including breaks, remain
possible and should become less frequent as the design solidifies. This does
not require independently versioned chapters or add a perpetual compatibility
promise. Existing implemented compatibility behavior and explicit migration
requirements remain unchanged. This grammar draft is the syntax-focused
working chapter/reference; its PARSER, DIRECTION and PROPOSAL labels keep
syntax support distinct from intended semantics and unresolved choices.

This is a bounded source-language profile, not a complete grammar for all advertised keywords. The presence of a keyword or token does not establish a production. Parser recovery can synthesize missing tokens and preserve skipped syntax; recovery trees are not conforming programs.

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
packaging remain work. Normal extraction does not execute embedded code or
fetch remote includes. This direction does not imply current parser support.

## 2. Lexical contract (PARSER)

Lexing is specified in full by `docs/lexical-grammar.md` in the repository; this historical parser draft is a separate proposal. Key syntax-boundary facts:

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

`Number` is one token: unary `-` is parsed as an operator, not part of the number. Consequently, the parser accepts decimal, hex, and binary token shapes, but numeric target selection/range and the lack of a default type are semantic rules (see `docs/conversions.md`).

## 3. Historical parser snapshot (`syntax-19`)

The productions in this section follow `sources/Terrum.CodeAnalysis/Syntax/Parser.cs`. `Identifier` means one lexical identifier token; dotted names are written explicitly. Semantic restrictions are called out separately below.

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

Actual parser permissiveness differs from a polished grammar in a few intentional ways: it accepts repeated/arbitrary modifier tokens and reports semantic/shape errors later; it accepts attributes only in the parameterless `[Name]` shape; it parses a class without a conformance clause (only structs have the `: Type, ...` production here); and a parsed interface has a mandatory type-parameter list, with no interface base list in this grammar. A field is parsed only inside a class/struct and cannot have declared type parameters. A constructor is recognized only in a class/struct body by matching its name to that containing type. Interface members do not use a containing-type context and therefore have no constructor/field form here. `SkippedSyntax` is parser recovery, not a valid declaration production. The file grammar permits at most one file-scoped namespace and requires it after usings and before declarations; ordering violations diagnose. Nested type declarations are parsed recursively. Implemented syntax is not equivalent to implemented semantics: for example, struct/class attributes may parse but can still be rejected by binding.

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

The parser reports a repeated `?` on the same source type (`TRM1005`); it does not parse repeated optional markers as valid grammar. Optional normalization such as `T?` with an already-optional substitution is a semantic/type-system rule, distinct from spelling `T??`. `write` is the only type-prefix qualifier parsed here; `mut` in a parameter is a passing mode (`mut ref`), not a type argument. Since `ref` and `own` are reserved tokens rather than identifiers, their canonical `ref<T>` and `own<T>` spellings are not type names accepted by this parser, despite being accepted design forms. Do not read the grammar as granting a particular lifetime, write authority, representation conversion, or generic variance.

Argument labels and passing modes parse, but argument reordering, overload resolution, and output behavior are binder contracts. The current bounded bool/out forms are implemented only in the documented profile. The design spelling `init ref<T>` is not recognized by this `Type` production (both `init` and `ref` prevent it from matching the current type shape) and is not represented by `PassingMode`. The accepted call-site spelling is `Create(init value)`; parser/binder support and coordinated migration remain pending.

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

`for` initializer and iterator use specialized no-semicolon parsing; the iterator can be an expression or one `=` assignment. Local declarations require an initializer. Blocks are required after `if`, `while`, `for`, `try`, `catch`, `finally`, and `unsafe`; a single unbraced statement is not the parser form. `catch` has no exception type or binding syntax. Assignment is a statement-level form, not a general right-associative expression. Parser recovery around a missing token/block is not an alternate accepted syntax.

### 3.4 Expressions and precedence

The parser handles unary and binary operators with precedence climbing. All binary operators are left-associative in the resulting parse tree (the recursive right operand uses the current precedence, so equal-precedence operators return to the outer loop). Calls/member/index postfixes bind inside a primary expression. `is null` and `is some name` are parsed at equality precedence. The precise parser table, highest first:

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

There is deliberately no level 8 operator in `Parser.cs`. Comparisons chain syntactically (`a < b < c`) and require semantic checking; the grammar does not imply C-style chained-comparison meaning. `is some` consumes the contextual identifier `some` followed by one identifier naming the bound value, e.g. `maybe is some value`; `is` and `some` are identifiers in the lexer and contextual in this parser branch.

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

For precision, the generic-call alternative is parsed only after a simple identifier and only when lookahead finds a balanced generic argument list immediately followed by `(`. This is explicit generic function-call syntax, not a general expression type-application operator. It does not express partial holes (`_`), static type methods, constructor-specific generic parameters/inference, or type packs. The parser has no conditional `?:`, null-coalescing, lambda, tuple, range, `await`, `throw`, type-test, prefix-cast, or general assignment-expression production. `CastExpression` exists as a syntax-node kind but `ParseExpression` does not produce it. `T(expression)` is parsed as an ordinary invocation/type-call-shaped expression; its numeric checked-conversion interpretation is semantic and bounded.

## 4. Implemented parser versus executable semantic subset

"Parses" below means syntax-tree shape in the recorded syntax-19 snapshot only; it does not mean every program runs or describe current-branch support. The then-current binder/native executable profile was narrower than that parser snapshot.

| Surface | Historical parser (`syntax-19`) | Bounded executable status at that snapshot |
|---|---|---|
| Decimal/hex/binary integer tokens, strings, bool/null | Parsed literals | Contextual decimal/hex/binary integers and booleans in supported places; null is limited to supported managed-handle contexts. String tokens parse but registered string-target execution is unsupported. No fallback numeric type; fractions/exponents, character literals, suffixes/custom literal parsers are not executable. |
| `T(value)` | Ordinary call syntax | Checked numeric conversions implemented for `int32`, `uint32`, `int64`, `uint64`, `char16`; target-selected literals parse directly. Not a prefix cast or general user conversion. |
| `ptr<T>`, `box<T>`, `val<T>`, `write T?` | Parsed as identifier/generic/qualifier shapes | Language direction documented; supported source subset varies. No assumption that parser acceptance implements arbitrary refs, raw pointers, owners or conversion. |
| `ref<T>`, `own<T>` | Not parsed as type names (`ref`/`own` are keyword tokens; `ParseType` accepts `write`, `void`, or identifier) | Accepted representation direction is not parser support; use is restricted to separately implemented/declared surfaces. |
| `mut` declarations and `mut ref` parameters/arguments | Parsed | Local rebinding, mutable nominal declarations, mutable receiver rules and ref/out behavior are bounded; `mut` is not a generic referent qualifier. |
| `where T : IFoo` and property forms like `where T : [Copy]` | Function constraints parse; interfaces/types parse | Generic operators and selected Copy-constrained function templates are implemented in a restricted profile. General witness, generic class/interface and method support is not implied. |
| Function declarations with duplicate names / source overload candidates | Parsed as ordinary declarations | Source-time overload binding is accepted language direction but general selection is not implemented; duplicate ordinary overloads currently diagnose (`TRM3003`). The selected declaration is fixed before IL/JIT/runtime execution. |
| `operator` declarations and expression operators | Parsed | Static operator protocol is compilation-local and restricted; this does not implement general function overload selection. |
| `is null`, `is some name` | Parsed | Managed-handle null/some tests supported only for bounded managed forms. |
| `try/catch/finally`, `unsafe`, `require` | Parsed | Checked failure policy and recovery are profile-scoped; general exception construction/ABI, arbitrary catch types and caller-unsafe declarations are absent. |
| `using`, file namespace, nested class/struct/interface | Parsed | Visibility, modules, full import/name resolution, attributes, declaration kinds and ABI promises have additional semantic checks. |

Do not copy future library examples into the historical `syntax-19` executable subset. The core-library companion may propose declarations beyond that parser; a reference API shape is not proof that its syntax compiles or that its runtime contract exists.

## 5. Accepted language direction without complete parser/binder support

These are established semantic directions, but their presence here does not declare complete source grammar or execution:

- **Access and representation:** canonical forms include `ref<T>`, `ptr<T>`, `box<T>`, `val<T>`, `own<T>` and `write` access. `mut` applies to a place, nominal declaration, or receiver: a `mut class C` declares mutable nominal storage, while instance receivers are readonly by default and a `mut` method requires a writable receiver. `write` qualifies access through a reference/view and grants no authority absent from the source; readonly access never upgrades, and write-to-readonly attenuation is constrained and not recursive. `implicit ref<T>` is ordinary readonly-reference behavior, including eligible existing places or a hidden local initialized at the argument's evaluation point in the current enclosing lexical scope. It adds no special noescape rule or permission to widen existing dependencies. For conditional outputs, postconditions use `ensure <expr>` with an ordinary Boolean expression and ordinary pattern/short-circuit behavior; legal value predicates imply initialization, while body/path analysis must establish legal reads and never inspect vacant storage to discover falsity. Live complete `T`, initialized nullable value (including null), and uninitialized storage are distinct. A caller may keep a checked relation to the status discriminator, but cannot access/clean up a vacant arm or receive an undiscriminated maybe-live owner. Exact `ensure` placement/grammar and metadata remain work. `init ref<T>` denotes vacant-storage initialization; the accepted call-site spelling is `Create(init value)`, while parser/binder support and coordinated migration remain pending.
- **Generic type checking:** explicit type parameters, constraints and static operator protocols have an implemented bounded subset. Generic inference and partial holes, general methods/classes/constructors, and associated-type grammar/binding are not generally supported. Associated-type relationships themselves are accepted direction; only their exact source productions remain open. Optional projection is non-injective; `T?` is normalized/idempotent semantically, while repeated source `??` is rejected.
- **Functions and overloads:** same-name declarations form source-time overload candidates; source binding selects and records one declaration and closed generic arguments. Linkers/JITs implement that binding rather than rerunning source overload resolution; runtime virtual/interface dispatch is separate. Exact matches precede only previously permitted, declared projections/erasures; there is no implicit numeric or general type-conversion search. Permitted access/qualification weakening may participate in matching, but no new projection, family erasure or reverse direction is inferred. Constraints filter applicability but do not create overloads, return-only overloads are excluded, applicable members beat extensions, and a by-value `T` beats `implicit ref<T>` when both apply to ordinary `F(value)`. A source literal can therefore become ambiguous when distinct numeric parameter overloads are added, while an already-bound caller retains its recorded target. These accepted semantic invariants do not mean the syntax-19 baseline binder supported ordinary overload sets: exact declaration/call grammar, full ranking, and extension-disambiguation spellings remain review work. `implicit ref<T>` was not parsed as a parameter modifier in that snapshot; see the implementation document for current coverage.
- **Types/declarations:** ordinary `struct`/`class` semantics, unique forms, enum/union design, attributes, visibility and layout have varying design/implementation limits. Keywords such as `enum`, `union`, `async`, `await`, `switch`, `case`, `foreach`, `throw`, `own` and `unique` being lexed do not themselves supply declaration/statement productions.
- **Adapters, families and operation protocols:** ordinary implementations are declared by their type; imports do not add retroactive global conformance. Explicit nominal adapters may add extension-interface behavior without changing the original witness set; ABI-transparent wrappers are distinct. Families opt in explicitly and declare checked associated projections/constraints; there is no universal `Type<T>`-to-`Type<void>` erasure or inferred covariance. No universal root is introduced merely for equality, hashing or formatting; use operation-specific witnesses/interfaces and explicit adapters. Exact adapter/family syntax, forwarding, metadata and projection eligibility remain specification work, including any explicit readonly array views and their representation, validity, backing, lifetime and cost checks.
- **Layout and interop direction (accepted semantics, grammar/spec work open):** `Auto` is the default with private fields; `Sequential` follows declaration order; `Union` gives every field offset zero and requires the fixed `Blittable` commitment; neither kind freezes representation across versions. Declared overlapping field storage is available only through explicit `Union` layout; access to overlapping alternatives is unsafe, with no active-member tracking or safe typed-overlap subset. This does not prohibit unsafe pointer-based aliasing/reinterpretation or compiler-private overlapping representations. A safe non-Blittable tagged/sum type is separate: case/tag checks must preserve ordinary validity, ownership, roots and cleanup, never grant raw reinterpretation; its grammar and representation remain open. Public field access requires fixed `Blittable` representation. Published language-level signatures and their layout/access/ownership/effect contracts are binary compatibility commitments without necessarily being foreign-interoperability promises; incompatible changes break replacement, while contract-preserving implementation changes and compatible extensions or stronger guarantees are not automatically breaking. This language/module contract is distinct from foreign ABI and serialization. Define additional serialization/callback/interchange encodings for concrete protocols as needed; no global bool/reference/SIMD wire format is selected. Unfrozen fields stay private; fixed representation may expose fields without changing ordinary readonly/`mut` access rules. `Transparent` is exactly one field with the carrier representation and argument/result ABI, inheriting the carrier and rejecting custom packing/alignment. `Blittable` is the accepted target-specific fixed/direct-interoperability contract over size, alignment, offsets and transitively compatible fields; ordinary `ref<T>` is excluded and `ptr<T>` may qualify under pointer-safety rules. A kind alone does not freeze; only `Blittable` freezes the per-profile representation. Unfrozen layout-dependent values may cross only layout-coupled compile/rebuild/invalidation scopes, not independently replaceable dynamic boundaries; JIT/AOT labels alone do not decide coupling. Both power-of-two member packing and aggregate over-alignment are accepted and may combine; exact attribute/argument grammar, limits and metadata remain work. Padding is uninitialized after field initialization; raw object-byte blits and unmediated raw foreign transfers are unsafe. Serializers account for every emitted byte; verified/trusted raw copy is allowed under a covering contract. These are semantic directions only, not claims about the current branch or the historical `syntax-19` parser/compiler-support snapshot.
- **Text/numeric intent:** no width suffixes or numeric fallback type; checked `T(expression)` numeric conversions and contextual integer literals are bounded support, and numeric operators are checked by default. Accepted conversion direction: checked integer conversions preserve value or throw; unchecked integer narrowing wraps to destination-width bits, while saturation is explicit. Floating-to-floating conversions follow IEEE-754 with nearest/ties-even default rounding and no language failures for inexactness, infinity, underflow or NaN. Float-to-integer truncates toward zero; checked range failure throws, unchecked or explicitly saturating conversion saturates rather than modulo-narrows, with NaN converting to zero. Floating support is target-only. Character literal notation and custom literal registration are design-only. Floating-point lexical forms are not executable. Encoded string targets such as `utf8z("...")` are ordinary target/call forms, not lexer prefixes, and are illustrative without target declarations.

For reference, ownership, lifetime and conversion rules, use the canonical docs rather than deriving semantics from EBNF: `docs/language.md`, `docs/conversions.md`, `docs/lifetimes.md`, and `docs/references.md`.

## 6. Open exact grammar decisions with recommendations

Examples in this section are illustrative and do not claim parser support.
Sections explicitly marked **accepted** record settled semantic direction;
remaining grammar, metadata and implementation questions are stated separately.
See the canonical [language direction](../language.md),
[library layering](../libraries.md#dependency-layers-and-package-boundaries),
and [specification roadmap](../roadmap.md#language-semantic-il-and-metadata-specification)
for authoritative organization and semantic IL/reference boundaries.

### 6.1 Initialization loans at call sites

**Accepted target spelling:** use `init` as the call-site argument mode for an
`init ref<T>` parameter, e.g. `Create(init value)`. It designates vacant
storage and is distinct from ordinary `ref`/`write ref` access and legacy
`out`; it does not make an existing initialized value writable or readable
through the initialization loan.

```terrum
// Accepted target call spelling; parser/binder migration is pending:
Create(init value);
```

This does not silently migrate existing `out` calls or select every production
for labels, places, overload binding, or conditional output contracts. Keep
the bounded `bool`/`out` implementation unchanged until declarations and call
sites migrate coherently. The migration and verifier/metadata work are
separate from this spelling decision; do not introduce an extra transactional
publication protocol.

### 6.2 Exact overload grammar, ranking, and disambiguation

**Accepted semantics, not open for reconsideration:** ordinary same-name declarations form source-time candidates; binding selects one concrete declaration and closed generic arguments. That target is recorded for downstream validation and code generation. Linkers/JITs do not rerun overload resolution; runtime virtual/interface dispatch is a separate mechanism. A by-value `T` wins over `implicit ref<T>` for ordinary `F(value)` when both apply. Exact matches precede previously permitted, declared projections/erasures; there is no implicit numeric or general type-conversion search. Permitted access/qualification weakening may participate in matching (for example, `write Span<T>` to a readonly `Span<T>` parameter), but no new or reversed projection/erasure is inferred. Constraints filter applicability, not form constraint-only overloads. Return type alone does not distinguish overloads. Applicable members outrank extensions. These decisions are authoritative in [language](../language.md#callable-values-params-and-tuples), [conversions](../conversions.md#conversion-policies), and [type families](../type-families.md); overload grammar and ranking details remain specification work.

**Historical implementation boundary:** the syntax-19 parser snapshot admitted repeated function declarations with the same spelling, but its binder rejected ordinary overload groups (`TRM3003`). The semantic ranking below is accepted direction; source grammar, diagnostics, and binder/metadata implementation remain work. No new overload keyword is required by the accepted model.

```terrum
// Accepted overload model; the syntax-19 snapshot parsed these declaration shapes,
// while its binder rejected this ordinary overload group.
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
parameter modifier is not yet parsed; the accepted illustration is:

```terrum
// Accepted direction, but not part of the historical syntax-19 parser snapshot:
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

**Specification work:** formalize the complete applicability and declared
specificity relation, interaction with generic inference and the accepted
by-value/`implicit ref<T>`/`params` priorities, exact ambiguity diagnostics,
extension-disambiguation spelling, and serialized binding metadata. This is
accepted semantic direction, not current general binder support.

### 6.3 Caller-unsafe declarations and explicit unsafe blocks

**Accepted target syntax:** `unsafe` on a callable declaration exposes its
caller obligations, e.g. `unsafe void M()`. An ordinary `void M()` does not
propagate a caller-unsafe obligation. The declaration modifier does not make
its body an unsafe context: unsafe operations inside either form still require
an explicit `unsafe { ... }` block. A caller invoking the unsafe declaration
must likewise satisfy the contract in an explicit unsafe block.

```terrum
// Accepted target spelling; implementation support remains pending.
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

Current parser/binder/runtime behavior remains bounded: unsafe recovery does
not currently implement general caller obligations or authorize arbitrary
unsafe operations. These target declarations do not claim compiler support.

### 6.4 Associated types: accepted direction, open source grammar; type packs out of scope

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

**Specification work:** formalize the interface/member, implementation
binding, projection, and associated-equality productions; witness selection,
qualification and collision rules; and their metadata representation. Keep
family opt-in and declared checked projections; do not introduce universal
erasure or inferred covariance.

**Source type packs are outside this task's scheduled scope.** No production or recommendation for variadic type parameters is made here; this is not a deferred implementation item in this grammar review.

### 6.5 Accepted temporary scope for `implicit ref<T>`

For `Foo(5).Bar(); Baz();`, materialization is shorthand for initializing an ordinary hidden local at the argument's original evaluation point in the current enclosing lexical scope, then passing its readonly reference. The local is not hoisted to scope entry and does not create a shorter lifetime-only scope. If conditional or short-circuit control flow skips the argument, no local is initialized and there is no owner to clean up. Scope exit follows ordinary initialized/moved-state cleanup. A valid returned reference may be used by later statements in that scope, subject to ordinary declared dependencies; existing place/backing lifetimes are not widened and capture/suspension/escape permissions do not change. This is accepted semantic direction, not parser support. See the canonical [reference contract](../language-contract-review.md#1-one-call-and-reference-model) and [implicit-loan lifetime rules](../lifetimes.md#parameter-only-implicit-readonly-loans). General source/loan metadata and implementation remain scheduled.

### 6.6 Reserved underscore and discard statement

**Accepted target semantics:** `_` is reserved and is never an identifier.
`_ = expression;` is the standalone discard statement: evaluate once, preserve
effects and failures, and apply ordinary ownership/cleanup. It is not assignment
and has no name-dependent fallback. Positional wildcard patterns and omitted
lambda parameter names are separate nonbinding contexts; they do not consume
the matched value or alter argument evaluation/parameter mode. A possible
generic type hole is another distinct nonbinding grammar role, not a named
identifier and not approved by the discard decision.

**Historical migration boundary:** the syntax-19 snapshot treated `_` as an
ordinary identifier, so `_ = expression;` assigned to it when bound and
mutable. In the verified syntax-20/scanner-5/schema-5 implementation, `_` is
reserved and standalone `_ = expression;` evaluates once with ordinary
ownership/cleanup. Other wildcard, lambda-placeholder and type-hole contexts
remain unsupported; see the [implementation boundary](../implementation.md).

```terrum
// Historical syntax-19 ordinary binding, not a discard:
int32 Existing() { mut int32 _ = 1; _ = 2; return _; }

// Implemented standalone discard in syntax-20/scanner-5/schema-5:
_ = MakeOwner(); // Evaluate once; clean the returned owner at statement end.
_ = int32(42);   // The type call supplies the literal's target.
```

Discard expressions are evaluated once. Any owner/result cleanup follows the
ordinary expression and statement cleanup rules; this does not force
collection. Further statement-context details are grammar/specification work,
not permission to alter the accepted nonbinding semantics.

### 6.7 Unary operator contract direction (accepted; protocol mapping remains work)

**Accepted direction:** ordinary unary `+`, `-`, `!` and `~` have
one operand of the containing type `T` (or `T?`) and separate operand/result
types; `!` is not inherently bool-result. Return type alone does not
distinguish overloads. Static `++` and `--` have operand-shaped results rather
than arbitrary independent result types. No derived-result relation is adopted.
`true` and `false` are another explicit exception: both must be declared
together and return `bool`. These rules describe the intended unary contract
shape, not implemented interfaces, exact protocol names, or compiler support.

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
operators with `void` return to mutate a receiver. That mutable-instance form
is not adopted here; it needs independent review and must not be inferred from
the static operator direction.

**Official comparison sources:** [C# language specification, §15.10.2 Unary
operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes#15102-unary-operators)
for unary result relationships and the `true`/`false` pair; [C# operator
overloading reference](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading)
for the separate C# 14 instance increment/decrement and compound-assignment
forms. These sources inform only the explicitly accepted comparison above;
they do not import C# inheritance, interface names, or implementation features.

### 6.8 Postcondition syntax for conditional output state

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
not optimizer-only hints. A historical relation could also state a genuine
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

**Specification work:** exact `ensure` clause placement/grammar and the
source-to-metadata mapping for correlated initialization facts. There is no
special conditional postcondition block grammar. The bounded `bool`/`out` form
remains executable until a coordinated migration. See the canonical
[conditional output contract](../language-contract-review.md#2-outcome-initialization-and-nullability-are-separate).

### 6.9 Static-proof floor and constant evaluation

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

### 6.10 Getter-only properties and accessor-local effects (accepted direction)

**Accepted target direction, not parser/binder support:** this brief is limited
to explicit getter-only, by-value properties on compact `Copy` structs and
source classes. It does not adopt setters, auto-properties, ref-returning
properties, or general property/accessor behavior.

The examples elsewhere use `{ get; }` sketches but do not say how a getter body
is authored or where `[NoThrow]` attaches. An explicit body should make the
getter an ordinary checked read-only callable member:

```terrum
// Accepted target spelling direction; not supported by the recorded syntax-19 snapshot.
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

An ordinary `[NoThrow] public int32 GetCount() { ... }` method is an alternative
when the compiler profile lacks property support; it is not a property-effect
placement option.

Treat the accessor as a named, source-bound
callable member with a readonly receiver and by-value result. The property
declaration and accessor must carry checked member identity and effect metadata;
do not infer a hidden `get_Name` function from a property name. A module may
encode the accessor as a callable internally, but consumers bind the declared
property/accessor identity and contract rather than a magic naming convention.

**Historical implementation boundary:** the `syntax-19` `MemberDeclaration`
production had fields, functions and other declaration forms but no property
production. Its binder had no property-getter binding contract, and its bounded
module contract had no declared property/accessor identity or property effect
record. Check the implementation document for validated current-branch
coverage; existing `{ get; }` documentation sketches do not establish parser,
binder or module support.

## 7. Conformance examples (checked against source and tests)

This table records the syntax-19 parser snapshot, not current-branch coverage. “Parser yes” means that snapshot recognized the shape; it does not assert successful binding. Its lexical grammar and parser source were checked directly, with focused parser tests for representative statement/declaration forms. Additional parser CLI probes confirmed empty parentheses, `void<int32>`, `void?`, `_` as an ordinary generic type argument, field initializers, empty statements, and based-literal forms.

| Example | Historical `syntax-19` parse | What that snapshot establishes |
|---|---:|---|
| `using A.B; namespace Demo.Core; int32 Main() { return 0; }` | Yes | File ordering and declaration shape; runtime entry semantics are separate. |
| `struct Pair : ICopy { int32 First; }` | Yes | Struct conformance/field shape; witness and field-initialization checks are semantic. |
| `int32 Add<T>(T x, T y) where T : [Copy] { return x + y; }` | Yes | Function generic/constraint syntax; eligibility/operator witness is profile-specific. |
| `void F() { int32 x = int32(0x_FF); }` | Yes | Local declaration, invocation/type-call-shaped AST, and based literal token; checked conversion is only for listed targets. |
| `void F(Span<int32> values) { for (mut int32 i = 0; i < 2; i = i + 1) { values[i] = i; } }` | Yes | Local/for/assignment/index syntax; type/effect validity still requires binding. |
| `void F() { if (item is some value) { Use(value); } else { ; } }` | Yes | Contextual pattern-like syntax; only bounded managed optional handle binding is supported. |
| `void F() { unsafe { try { require false; } catch { ; } finally { ; } } }` | Yes | Recovery/cleanup syntax; not general exception construction or arbitrary unsafe operations. |
| `void F(int32?? value) {}` | No conforming type syntax | Repeated marker gets `TRM1005`; recovery may retain a tree, but it is invalid. |
| `int32 F() { return (int32)1; }` | No | Prefix cast is not parsed; use a checked type-call where supported. |
| `int32 F() { return -1i32; }` | No conforming executable literal | Attached numeric width suffix diagnoses lexically; negative sign is a separate token. |
| `char32 F() { return 'x'; }` | No | Single-quoted character literals are not tokens. |
| `init ref<int32> target; F(init target);` | No | The `init` call-site spelling is accepted direction, but this snapshot has no init-lending call form; parser/binder support and coordinated migration remain pending. |
| `void F() { func<_, int32> f = Convert; }` | Yes, as type syntax | `_` is an ordinary identifier type argument, not a special generic/callable hole; the spelling gains no inference semantics. |
| `void F() { return (); }` | Yes | Empty parentheses produce the parser's `VoidExpression`; semantic use still requires a valid void context. |
| `void<int32> F() {}` / `void? F() {}` | Yes | `ParseType` permits generic and optional suffixes on `void`; semantic type validation is separate. |
| `struct Holder { int32 Value = 1; }` | Yes | Field initializers are part of the parser grammar; type/constant restrictions are semantic. |
| `void F() { ; }` | Yes | Empty statements parse. |
| `void F() { int32 h = int32(0x_FF); uint32 b = uint32(0b_1010); }` | Yes | Both based-literal forms/separators and type-call-shaped invocations parse. |
| `int32 Parse(int32 x) { return x; } uint32 Parse(uint32 x) { return x; }` | Parsed as declarations | The syntax parser accepts each function declaration; the syntax-19 binder rejected the ordinary overload group (`TRM3003`), while the accepted language direction requires source-time selection. |

## 8. Parser recovery and conformance boundary

The parser's `Expect` inserts zero-width missing tokens; `ParseExpression` can produce `MissingExpression`; declaration recovery preserves unknown tokens in `SkippedSyntax`. Nesting deeper than 128 emits `TRM1004` and preserves the token stream as skipped syntax. Parse diagnostics, lexical diagnostics, or any skipped/missing syntax make the source unsuitable as a conforming program even if a partial tree is returned. Lossless token/trivia preservation is an editor/compiler property, not permission to execute malformed source.

The reliable authority for implemented support is the current lexer/parser/binder plus tests and `docs/lexical-grammar.md`/`docs/implementation.md`. This grammar is an organized review aid only. Any change to lexical spelling, an accepted production, precedence, associativity or recovery contract requires a separate explicit decision and corresponding conformance tests.
