# Terrum grammar proposal

> [!IMPORTANT]
> Proposal only: this document is not an approved grammar or a claim of
> implementation. Accepted semantic direction does not approve the candidate
> productions below.

This proposal specifies candidate source notation and selected semantic
constraints. It is not yet an approved language specification; exact grammar
and several semantic contracts remain open. Examples are informative unless
explicitly identified as accepted semantic direction.

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
language features. A reserved spelling does not by itself establish a
production; recovery syntax is not a conforming source form.

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
- Unsuffixed decimal, hexadecimal (`0x`/`0X`), binary (`0b`/`0B`) and proposed hexadecimal floating-point forms are described below. Separators may occur between digits and, for based values, immediately after the prefix. Signs are separate operators except an exponent sign. Width suffixes are invalid; `10i32` is not a typed literal. `_123` is an identifier.
- Strings use `"..."` with the documented escapes; interpolation syntax remains open. Single-quoted character literals denote one decoded Unicode scalar; numeric width suffixes are invalid.
- Lexical diagnostics preserve malformed source spelling; bad characters/surrogates, invalid escapes, unterminated literals/comments, bad prefixes, separators, and attached width suffixes have distinct diagnostic codes in the lexical reference.

The schematic lexical terminals used below are:

```ebnf
Identifier       = lexer-identifier-token ;
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
Character        = "'", ( CharacterScalar | Escape | "\'" ), "'" ;
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

Trivia separates tokens but does not impose a style rule on otherwise
unambiguous token sequences: `x+y` and `x + y` have the same meaning, as do
`Foo.Bar`, `Foo. Bar` and `Foo . Bar`. A formatter may canonicalize spacing
without making its output the only valid source. Spacing can still change
which tokens are formed under lexical longest-match and identifier-boundary
rules; whitespace is not permission to split or merge token spellings after
lexing. Comments and line breaks follow their documented trivia rules,
not an arbitrary mandatory space-around-operator policy.
A contiguous run of operator glyphs must be one defined operator, not an
implicit concatenation of separate operators. Thus `x+++y` rejects, while
`x++ +y` and `x+ ++y` separate the operators explicitly. Whitespace or a
comment can separate operators; ordinary grouping delimiters, commas and
semicolons are not part of the run. Nested generic closing delimiters are
recognized in type-argument context, so `List<List<T>>` does not require
spaces. Exact glyph membership and contextual boundary rules, including
optional/type punctuation and comment openers, remain lexical specification
work; this is not permission to reinterpret a run by guessing user intent.

`CharacterScalar` excludes an unescaped quote, backslash or line break.
Escape decoding must yield exactly one Unicode scalar, never a surrogate.
The expected character type must represent it: `char32`/`rune` admits any
scalar, `char16` one nonsurrogate code unit, and `char8` ASCII. There is no
fallback character type.

`Number` is one token: unary `-` is an operator, not part of the number.
Numeric target selection/range and the lack of a default type are semantic
rules (see [conversions](../conversions.md)). A hex float requires a binary
exponent (`p`/`P`), so `0x1f` is an integer while `0x1.fp+2` is a floating
token. It has digits on at least one side of the point; the exponent has
decimal digits, and `_` follows the existing between-digits rules. A leading
sign is separate from the token, unlike the exponent sign.

The `HexFloat` production above specifies its candidate spelling and is
included in `Number` and `Primary` just like other numeric literals.
Its token-boundary and diagnostic rules (including incomplete `p` exponents,
separators and identifier tails), target-format rounding and literal
conversion rules still need to be specified before adoption.
The selected floating target supplies the format; a hex float's exact
binary-rational value is rounded directly to that format using the selected
IEEE-754 nearest/ties-even default and the established overflow/underflow
rules, not through an intermediate host floating type. Both
`float32 x = 0x1.fp+2;` and `var x = float32(0x1.fp+2);` select `float32`;
`var x = 0x1.fp+2;` has no target and diagnoses.
The [lexical grammar](../lexical-grammar.md) specifies the adopted lexical
profile separately; this candidate does not silently revise that contract.

Primitive spellings (`int32`, `float32`, `bool`, `char16`, etc.) remain
ordinary identifier **tokens**, not lexical keywords. Their exact type
bindings are reserved for compiler/runtime-owned primitives: an ordinary
package cannot declare a type with a reserved primitive name, including in
another namespace. This does not reserve all lowercase type names or prevent
ordinary code from using primitive types. The proposed width-explicit integer,
floating, character, vector and decimal families are listed in the
[language design](../language.md#primitives-strict-arithmetic-and-text);
the set of reserved bindings follows the primitives actually selected by the
language, not every candidate in this grammar. Neither `@identifier` nor
backtick-delimited identifiers have a production here, so reserved words
cannot be used as names. A narrow `@identifier`
keyword escape remains an open alternative, not an adopted arbitrary-string
delimiter. Core does not need an escape to declare `int32`: escaping a token
would not confer the right to define a compiler-owned binding.

**Core bootstrap proposal:** A designated Core-library compilation has no
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

`void` is a distinct keyword; primitive type spellings are not keyword
tokens. The following is the intended vocabulary, grouped by source role;
the exact keyword-versus-contextual-token classification and the productions
for some words remain open. Listing a word does not permit it in an arbitrary
context.

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
| Contracts | `require`, `ensure` | Check preconditions or postconditions; declaration grammar remains open. |
| Exception flow | `try`, `catch`, `finally`, `throw` | Handle or propagate failures under their checked transport contracts. |
| Deferred cleanup | `defer` | Register lexical-block-exit actions. |
| Unsafe authorization | `unsafe` | Scope operations that require caller authorization. |
| Policy selection | `policy` | Prefer or require a method variant. |
| Literal values | `true`, `false`, `null` | Form Boolean and optional values. |
| Construction | `new` | Begin ordinary construction. |

`_` is a distinct nonbinding token, not an identifier. Vacant initialization
uses `init ref<T>`; arithmetic selection uses policy cases rather than a
separate arithmetic-toggle source form.

## 3. Candidate grammar productions (PROPOSED)

The productions below are candidates, not an adopted grammar. `Identifier`
means one lexical identifier token; dotted names are written explicitly.
Semantic constraints are stated separately.
The prefix names below are metasyntactic placeholders for only the
attributes/modifiers valid in each declaration context. Legal prefix
modifiers may be permuted; duplicate or conflicting modifiers reject.

### 3.1 Source-file structure and declarations

```ebnf
CompilationUnit  = { FileAttribute }, { UsingDirective },
                   ( FileNamespace, { TopDeclaration }
                   | { TopDeclaration | NamespaceDecl } ), EOF ;
FileAttribute    = "[", ( "module" | "assembly" ), ":", AttributeName,
                   [ AttributeArguments ], "]" ;
UsingDirective  = "using", DottedIdentifier, ";" ;
FileNamespace   = "namespace", DottedIdentifier, ";" ;
NamespaceDecl   = "namespace", DottedIdentifier, "{", { TopDeclaration }, "}" ;
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
AliasPrefix     = { Attribute | VisibilityModifier } ;
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
ConstructorDecl = Identifier, ParameterList, { PostconditionClause }, CallableBody ;
PostconditionClause = "ensure", Expression ;
FunctionDefinition = Type, MemberName, [ TypeParameterList ], ParameterList,
                     { ConstraintClause }, { PostconditionClause },
                     ( CallableBody | ";" ) ;
InterfaceFunction = FunctionDefinition ;
OperatorDefinition = Type, "operator", OperatorSymbol, ParameterList, CallableBody ;
ConversionDefinition = "operator", Type, "(", Type, Identifier, ")", CallableBody ;
InterfaceOperator = Type, "operator", OperatorSymbol, ParameterList,
                    ( ";" | CallableBody ) ;
CallableBody    = Block | ExpressionBody ;
ExpressionBody  = "=>", ( ExpressionStatement | AssignmentStatement
                       | DiscardStatement | RequireStatement | ThrowStatement ) ;
FieldDecl       = Type, Identifier, [ "=", Expression ], ";" ;
PropertyDefinition = Type, MemberName,
                     ( "{", GetterDefinition,
                       [ SetterDefinition | InitDefinition ], "}"
                     | ExpressionBody ) ;
AccessorPrefix  = { Attribute | "mut" } ;
GetterDefinition = AccessorPrefix, "get", ( CallableBody | ";" ) ;
SetterDefinition = AccessorPrefix, "set", ( CallableBody | ";" ) ;
InitDefinition   = { Attribute }, "init", ( CallableBody | ";" ) ;
InterfaceProperty = Type, Identifier, "{", InterfaceGetter,
                    [ InterfaceSetter | InterfaceInit ], "}" ;
InterfaceGetter = GetterDefinition ;
InterfaceSetter = SetterDefinition ;
InterfaceInit   = InitDefinition ;
IndexerDefinition = Type, [ Type, "." ], "this", IndexParameters,
                    ( "{", IndexGetter, [ IndexSetter | IndexInit ], "}"
                    | ExpressionBody ) ;
IndexParameters = "[", Parameter, { ",", Parameter }, "]" ;
IndexGetter     = AccessorPrefix, "get", CallableBody ;
IndexSetter     = AccessorPrefix, "set", CallableBody ;
IndexInit       = { Attribute }, "init", CallableBody ;
InterfaceIndexer = Type, "this", IndexParameters,
                   "{", InterfaceGetter, [ InterfaceSetter | InterfaceInit ], "}" ;

TypeParameterList = "<", GenericFormal, { ",", GenericFormal }, ">" ;
GenericFormal   = { Attribute }, ( Identifier, [ "=", Type ]
                | "const", Type, Identifier, [ "=", ConstantExpression ] ) ;
InterfaceTypeParameters = "<", ( { Attribute }, "this", Identifier
                                | GenericFormal ),
                          { ",", GenericFormal }, ">" ;
ConstraintClause  = "where", Identifier, ":",
                    ( Type | "[", Identifier, "]" ),
                    { ",", ( Type | "[", Identifier, "]" ) } ;
OperatorSymbol  = "+" | "-" | "*" | "/" | "%" | "&" | "|" | "^"
                | "<<" | ">>" | ">>>"
                | "==" | "!=" | "<" | "<=" | ">" | ">=" | "!" | "~"
                | "++" | "--" | "true" | "false" ;
```

This candidate permits one file-scoped namespace or namespace blocks, without
mixing those forms in a file. Namespace blocks may coexist with root
declarations; nested namespace blocks and namespace-local imports remain
separate grammar work. Declarations retain source order within compiler file
order. Fields and constructors occur in class/struct bodies;
interface members do not include fields or constructors. An interface
constant with an initializer is fixed by its declaration; one without an
initializer requires an implementer-supplied `const` binding of that selected
member identity. An unrelated same-named constant does not satisfy it.
The `ContextualPrefix` placeholder still requires context-specific
validation; permutation does not make every modifier legal on every
declaration. Repeated visibility and conflicting effects reject. Callable
contract clauses, if introduced, follow the parameter list; return types
remain before the callable name, consistently for functions and operators.
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
conversion keyword. Constructor-versus-conversion ranking and duplicate source-/target-owned
conversion eligibility remain open; declaration order cannot break ties.
A bodyless static interface operator is a required compile-time witness; a
bodyful one supplies a default. Static operator witness selection needs no
runtime vtable. The candidate generic type parameters and conformance lists
apply to structs **and** classes; interface base lists express interface
composition, not implementation of an interface by itself.
The candidate integral enum base and constant-valued members preserve unnamed
underlying values; `[Flags]` is an optional declaration contract, not a
different closed enum grammar. `IntegralType` and `ConstantExpression` require
semantic restrictions beyond their syntactic approximations. `AliasDecl`
names the target type without defining a new nominal identity or adding
conformance; generic aliases substitute their type formals into the target.
Its namespace-level placement and visibility are deliberate; aliases are
not nested in nominal types in this proposal. Associated
`type Name;` declares an interface requirement; `type Name = Type;` in an
implementing nominal type binds a selected requirement rather than declaring
an arbitrary nested nominal type. Witness mapping, alias cycles and metadata
remain separate checked contracts (see §5.4).
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
Nested type declarations are deliberately excluded from this candidate
grammar; this is a proposed scope boundary, not a claim that nested type
arguments in `Type` are excluded. See [§5.11](#511-nested-types-and-scoped-operators).

```terrum
// Candidate interface syntax.
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

Repeated optional markers such as `T??` are not a proposed source form.
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
Permitted constant-value domains beyond named policy enums and dependency
rules for defaults remain to specify.

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
Overload selection and output behavior remain checked semantic contracts. The proposed
`init ref<T>` is a distinct initialization-loan parameter form, with the
accepted call-site spelling `Create(init value)`. `write` qualifies access
through a reference and `pin` qualifies only `ref<T>` (for example,
`write pin ref<T>`); other uses of `pin` in this broad `Type` production
are invalid. `pin ref<T>` keeps its backing object rooted and at a stable
address for the pin loan's lifetime. It may be reborrowed as an ordinary
`ref<T>` without preserving the pin guarantee for that reborrow, but the
underlying pin cannot end while any derived address depends on it. A raw
`ptr<T>` is an address, not a relocatable managed reference; its value
does not itself pin or root a GC allocation. Forming one into GC storage
requires a live pin with an enforced provenance/lifetime dependency, or
independently stable storage. Neither applying a qualifier to an existing
reference nor forming a raw pointer silently acquires a pin. `mut` qualifies
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
LoopStatement   = [ Identifier, ":" ],
                  ( WhileStatement | ForStatement | ForEachStatement
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
established by a checked initialization; proposed `init T name;` makes its
type explicit. Neither form is a default value, and neither permits reads
before complete publication. Bare `T name;` is not a declaration here.
By-value iteration is the default. `ref` iteration borrows an element place
read-only and `mut ref` requires a writable element loan from the iterator;
neither silently copies a value or upgrades a readonly source. The iterator
protocol, loan extent, backing dependency and whether advancing it invalidates
the current element remain **OPEN**; these spellings do not grant escaping
loans or overlapping mutation.
`await for (... in ...)` selects an eligible checked async iteration protocol
inside an async implementation. The source expression evaluates once, and
each advancement uses that protocol's awaitable completion. It does not
implicitly await yielded elements: an async sequence of task values still
yields task values. Awaiting each task in an ordinary synchronous sequence
instead uses a normal `for (... in ...)` with an explicit `await` in its body.
`await` is not an element-binding modifier, and `for (await ... in ...)` is
not an alternate form. Reference modes retain their ordinary access checks,
with suspension preserving backing, loan and affinity obligations. Iterator
acquisition, close and cancellation protocol details remain library design;
the spelling introduces neither implicit blocking nor executor capture.
Blocks are required after `if`, `while`, `for`, `try`, `catch`,
`finally`, and `unsafe`; a single unbraced statement is not part of this
candidate. Assignment is a
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
silently becoming false. Catch matching, unwind phase, eligibility, and
failure transport remain separate design work. Bare `throw;` rethrows the
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

The candidate grammar uses the precedence table to parse an operand, then
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
| 13 | prefix `+ - ! ~ move ++ -- await` | prefix/right-nesting |
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
PrefixOperator  = "+" | "-" | "!" | "~" | "move" | "++" | "--" | "await" ;
BinaryOperator  = "*" | "/" | "%" | "+" | "-"
                | "<<" | ">>" | ">>>"
                | "<" | "<=" | ">" | ">=" | "==" | "!="
                | "&" | "^" | "|" | "&&" | "||" | "??" ;
IsTest          = "is", ( "null" | "some", Identifier | "init" ) ;
Postfix         = Primary, { PostfixPart } ;
PostfixPart     = ".", Identifier, [ TypeArguments ]
                | ".", "via", "<", Type, ">"
                | Arguments | "[", Expression, { ",", Expression }, "]"
                | "?.", Identifier, [ TypeArguments ]
                | "?[", Expression, { ",", Expression }, "]"
                | "++" | "--" ;
Primary         = "(", [ Expression ], ")" | TupleExpression
                | SwitchExpression | CollectionExpression
                | "new", Type, Arguments,
                  [ ObjectInitializer ]
                | Number | String | Character | Boolean | Null | "field"
                | Identifier, [ TypeArguments, Arguments ]
                | "unsafe", "(", Expression, ")"
                | [ "require" ], "policy", PolicyCases, "(", Expression, ")"
                | "default", "<", Type, ">" ;
TypeArguments   = "<", GenericArgument, { ",", GenericArgument }, ">" ;
GenericArgument = Type | ConstantExpression | "_" | "default" ;
PolicyCases     = "<", PolicyCase, { ",", PolicyCase }, ">" ;
PolicyCase      = DottedIdentifier ;
ObjectInitializer = "{", { Identifier, "=", Expression, ";" }, "}" ;
TupleExpression = "(", TupleElement, ",", TupleElement,
                  { ",", TupleElement }, ")" ;
TupleElement    = [ Identifier, ":" ], Expression ;
LambdaExpression = LambdaPrefix, LambdaParameters, "=>", ( Expression | Block ) ;
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
value semantics and lexical adoption gates are described in §2.

Generic argument parsing uses the selected declaration's formal kinds to
distinguish type arguments from constant-value expressions; the broad
alternatives above do not permit arbitrary runtime expressions as constants.
The grammar includes explicit `_` inference holes and `default` selectors,
not source type packs or unresolved exported holes. Constructor-specific
inference, general range, full type-test and prefix-cast forms remain
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

Lambdas require an explicit or contextual callable contract. An explicitly
typed parameter list does not make `var f = (int32 x) => x + 1` infer a
canonical `func` or `fnptr` representation. Expression/block bodies obey
ordinary result, effect, capture, ownership and lifetime checks.
`static` forbids capturing enclosing locals, parameters or `this`, but does
not itself select a representation, allocation strategy or inlining.
Contextually generic lambdas retain their definition-site constraint contract.

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

Collection expressions are target-typed and use only a registered, checked
construction contract; `[]` is empty and no default array/List or numeric
width is invented. `..sequence` spreads can interleave with ordinary elements
under eligible iteration and construction contracts, without reinterpretation
or implicit ownership transfer. Conditional inclusion composes existing forms:
`..(condition ? [element] : [])`; no collection-specific `if` form is added.
Contextual typing of such fragments and whether direct nesting has a
no-intermediate construction contract remain unresolved; optimization may
not erase observable builder effects or failures. The accepted eager-capture
and drain ordering must be preserved. Mapping/filtering initially compose
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
[language exception contract](../language.md#safe-and-unsafe-boundaries).

`PolicyCase` binds a compile-time constant named enum case, not a type
argument or a runtime enum value. Exact policy selection, strictness and
generic forwarding are described in [§5.12](#512-modifiers-cleanup-and-finalization).

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
  full placement and metadata remain open beyond the constructor-completion
  clauses below (see [§5.8](#58-postcondition-syntax-for-conditional-output-state)).
  `init ref<T>` means
  vacant-storage initialization, distinct from ordinary `ref`/`write ref`;
  `Create(init value)` is the accepted call-site spelling, with exact
  grammar and output metadata still open.
- **Generic type checking:** explicit parameters, constraints and static
  operator protocols are accepted direction; associated types and family
  membership follow [type-family contracts](../type-families.md#explicit-families)
  and [associated relationships](../type-families.md#nominal-witnesses-and-associated-relationships).
  Optional projection is non-injective and semantically idempotent, but
  repeated source `T??` is not proposed syntax. This proposal gives `_` holes,
  generic defaults and selected associated-type spellings; full inference,
  constructor and witness contracts remain separate work. Type packs are
  deferred, not ruled out as a future language capability.
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
  An intended reserved word does not establish a declaration or statement
  production; union, async, matching and failure forms require their
  respective grammar and semantic rules.
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
  character literals and custom literal registration remain proposed,
  floating token-boundary and registration details remain work, and `utf8z("...")` is an
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
storage and is distinct from ordinary `ref`/`write ref` access; it does not
make an existing initialized value writable or readable
through the initialization loan.

```terrum
// Accepted semantic direction; candidate grammar form:
Create(init value);
```

This does not settle productions for labels, places, overload binding, or
conditional output contracts. It does not
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

### 5.3 Caller-unsafe declarations and explicit unsafe scopes

**Accepted target syntax:** `unsafe` on a callable declaration exposes its
caller obligations, e.g. `unsafe void M()`. An ordinary `void M()` does not
propagate a caller-unsafe obligation. The declaration modifier does not make
its body an unsafe context: unsafe operations inside either form still require
an explicit `unsafe { ... }` block. A caller invoking the unsafe declaration
must likewise satisfy the contract in an explicit unsafe block. The candidate
`unsafe(expr)` form scopes the same caller obligations to one expression;
it is not yet part of the accepted block-only contract.

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

The exact declaration grammar and contract metadata remain open.

### 5.4 Aliases and associated types: candidate declarations, open binding

**Accepted semantic direction:** families opt in explicitly and declare
checked associated projections and constraints. Existing explicit
declarations such as `TextSpan<void>` are not reopened; this is not universal
`Type<T>`-to-`Type<void>` erasure. Associated types are named type members of
an interface witness. The candidate §3.1 forms are `type Wider;` in the
interface and `type Wider = MyWideInteger;` in a conforming type.
`T.Wider` resolves through the selected `IInteger<T>` witness. An associated
equality may explicitly bind a type parameter, illustratively
`where T : IInteger<T, Wider = U>`. Do not infer the original `T` backward
from an associated result, choose an arbitrary implementer from a constraint,
or silently resolve collisions; ambiguous witness/projection uses require
explicit qualification.

**Proposed alias form:** `alias Option<T> = T?;` names the same resolved type,
not a distinct wrapper or new witness. Aliases are namespace-level
declarations, with `public`, `internal` or `private` visibility like other
top-level declarations; they are not nominal members. Exporting or
qualifying an alias cannot create a new nominal identity or change the
target's access, ownership, layout or compatibility contract. A public alias
is exported as a named transparent source declaration; executable signatures
use its resolved target identity. Its public name and target mapping are source
contracts, not a new nominal runtime type. `Option<T>` is informative, not a
decision that Core or another library exports it. Cycles, substitution and
the exact alias metadata encoding still need checked rules.
Associated-type bindings are distinct: they satisfy a selected interface
member and have witness identity, rather than providing a general synonym.

**Proposed self-binding spelling:** `interface IInteger<this T>` declares
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

**Open:** complete associated-equality and inherited-witness qualification
rules and their metadata representation. Keep family opt-in and declared checked projections; do
not introduce universal erasure or inferred covariance.
Interface constructors are not part of this proposal: a future requirement
needs a checked construction and initialization-completion contract. Fixed
`const T Name = value;` interface constants are owned by the interface
definition; a bodyless `const T Name;` instead requires a checked
implementer-supplied binding. They have distinct identity/versioning rules
and neither follows automatically from a `type Name;` requirement.

**Source type packs are deferred.** No production for variadic type parameters
is made here; generic defaults and collection spreads are not type packs.

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
identifier and not approved by the discard decision. In a match over `T?`,
`some` covers the present case and `_` can cover the remaining absent case
when those are the only possibilities. The wildcard still means "any
remaining value", not `none`: it also matches other unhandled cases if the
matched type has them. `null` remains the optional absence value. The
bounded switch/pattern productions and coverage rules are in §3.3; complete
nominal-case and destructuring patterns remain to specify.

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
special conditional postcondition block grammar. See the canonical
[conditional output contract](../language-contracts.md#2-outcome-initialization-and-nullability-are-separate).

### 5.9 Static-proof floor and constant evaluation

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
source classes. **Proposed forms:** `get`, `set`, and construction-only `init`
accessors, including backed auto-properties and authored indexers (§3.1).
Ref-returning properties and indexers still need the general result-loan contract.

Interface properties are separate contract members: a bodyless interface
getter requires a conformance implementation, while a getter body supplies a
default under §5.12. Instance `this[...]` indexers are parameterized accessor
contracts; concrete accessors require authored bodies, not automatic
per-index backing storage.

An explicit concrete getter body makes the getter an ordinary checked
callable member with its declared readonly or `mut` receiver; `{ get; }` generates a backed auto-getter under
the proposed extension. Accessor attributes such as `[NoThrow]` attach to
the getter:

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
per-index backing field from this rule. Detailed initialization of implicit
storage, accessor applicability and metadata representation remain work.
`mut get` and `mut set` follow ordinary method receiver contracts; writing
through an independently writable referent does not by itself mutate `this`.
`init` provides mutability within its initialization scope without a redundant
`mut init` modifier.

**Proposed construction rule:** a constructor initializes all private state
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
annotation, validation hook, update syntax and ordinary wither contract
remain open. A normal `.with(...)` on an already live value instead produces
a separately validated updated value; it cannot mutate its source through
`init`. No `required` modifier is needed solely to express constructor
completion obligations.

### 5.11 Nested types and scoped operators

**Proposed boundary:** defer nested nominal types; begin with namespace-level
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
reject. Context-specific callable clauses, once specified, follow the
parameter list; result types remain in prefix position.

**Proposed vocabulary:** `do` introduces a post-tested loop;
`extern` an externally implemented declaration; and
`extension` a scoped extension declaration. Their productions, restrictions,
effects and lowering remain open. Accessors and `when` guards have bounded
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
backing lifetimes even if advancement is delayed or never occurs. Protocol
method names, element-loan contracts, abandonment cleanup and representation
remain separate design work. `[Iterator]` marks the
implementation; it is not a traversal-direction or element-access flag.
`[Iterator]` composes with `async` for an async producer. Its declared result
must satisfy a checked async-producer contract; `Task<Sequence<T>>` by itself
only represents completion of sequence creation, not asynchronous advancement.
Async producer creation remains lazy, unlike ordinary eager async invocation.
Advancement may suspend at an `await` and resumes under the ordinary checked
scheduling, failure and affinity rules until yield, completion or failure.
`yield expression;` still emits an element and `return;` finishes production;
neither introduces an implicit result wrapper. Protocol names, advance and
close operations, and cancellation integration remain library design.
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
Detailed merge compatibility, including partial accessor implementations and
attribute multiplicity, remains open; ordinary mixed backing is defined in §5.10.

**Policy selection (PROPOSED):** policy domains may be Core-defined or
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
or the base. All other policy scopes preserve ordinary fallback. The exact
source annotation on variant declarations remains open; no policy may
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
cannot select different source overloads depending on inlining. A query such
as `if (policy<P>)` remains open until compile-time conditional and generic
rules specify whether it tests the active request or a particular call's
selected variant. A policy case names the requested variant; its name does
not by itself impose additional semantic obligations. `require policy<...>`
guarantees selection of a matching variant, not properties beyond that
variant's declared contract.

Type-call conversions already use `T(expression)`. The proposed user
conversion declaration `operator T(U source)` fixes its destination type
at declaration; it does not grant raw reinterpretation or implicit
conversion. Conversion ownership, ambiguity, effects and safety constraints
remain open. `operator` already has a token; `cast` and `explicit` need not
be reserved solely to declare or invoke a checked conversion.
`in` marks the proposed iteration form; `and`, `or` and `not` combine
patterns, distinct from Boolean `&&`, `||` and `!`. The full pattern and
iteration binding rules remain open. Reflection and
layout queries, allocation forms, and pinning need separate semantic and
representation decisions before choosing between intrinsic names and
keywords; `new` already covers the proposed ordinary allocation form.

There is no proposed `~Type()` finalizer production. Deterministic
`[Cleanup]` for an owning value and scope/unwind cleanup are not GC
finalization: a GC finalizer would need a separate lifecycle, resurrection,
threading, failure/effects and resource-safety policy. **OPEN:** whether to
offer finalizers at all; if needed, specify their contract and syntax apart
from `[Cleanup]`. Do not synthesize finalizers from owner cleanup or class
declarations.

### 5.13 Operators, assignments and expression gaps

**Accepted semantics; declaration contract still to complete:** user `operator +=` is a separate declared contract from
`operator +`, using a `write ref<T>` destination; it cannot be desugared to
read/copy/write or inferred from addition. The same distinct-contract rule
applies to nominal `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=` and
`>>>=`. Their statement spellings are in §3.3; explicit nominal declaration
shape and metadata still need completion. A destination must satisfy the
ordinary writable-place checks, not merely parse as an expression. Built-in
integer shift assignments evaluate the place once and check shift counts.
`??=` is the built-in initialized-optional operation described in §3.4,
not a grant of arbitrary nominal overloading.
Do not split compound operator spellings into an ordinary binary operator
followed by `=`. Pointer dereference/address formation and short-circuit
`&&`/`||` overloading need their own access and evaluation-order contracts;
they are not ordinary overloads merely because they use operator punctuation.

Still **OPEN** for a full source grammar: nominal safe union cases and complete
patterns, associated equality and family/adapter declarations,
conditional/default conformance, text-target fragments and interpolation,
general `const` declarations/expressions, full contract-clause placement,
general ownership/reference results, constructor inference, async completion
and scheduling protocols, producers, capture/exception transport, and registered collection-fragment
construction. Source type packs and tuple arity extensions remain deferred.
Giving a form candidate syntax does not complete its checked lifetime,
effect, representation or separate-compilation contracts. Hexadecimal float
spelling and target rounding are above; exact lexical recovery/boundaries
remain to specify.

### 5.14 Documentation-comment grammar and tooling

The accepted attachment, markup and contract boundary is in
[language](../language.md#documentation-comments). The following details are
**open specification work**, not a defined XML schema:

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

Before promoting this proposal to an approved grammar, reconcile every
production with the lexical contract (including contextual keywords and
longest-match rules), restrict modifiers and declaration bodies by context,
define the accepted semantic forms that lack productions, and specify static
semantics for invalid combinations. Positive and negative source examples
should cover the specified boundaries. A candidate spelling in an example
does not establish a normative production.

## 6. Source-form coverage and open boundaries

This index separates concepts for readers; it does not add productions or
approve unresolved forms. The sections named in the middle column give the
candidate source forms or accepted semantic direction. An open boundary
remains unspecified rather than becoming legal merely because a related word
appears in §2.

| Concept | Defined direction | Still to specify |
|---|---|---|
| Lexical tokens | §2 gives identifier and reserved-word forms; primitive names are identifiers with reserved bindings. | Exact contextual-word classification and `HexFloat` token boundaries/diagnostics. |
| Literals | §§2 and 4 give character-scalar and context-typed numeric forms, including hex-float target rounding. | Complete floating token recovery, character diagnostics and registered text forms. |
| Files | §3.1 sketches directive order. | Other directive/file forms, if any. |
| Namespace | §3.1 permits file-scoped or block namespaces, without mixing. | Nested namespace blocks and namespace-local imports. |
| Visibility | §2 lists access words; §3.1 uses context-restricted prefixes. | Legal access levels for each declaration kind. |
| Nominal types | §3.1 sketches structs, classes, interfaces and integral-base enums. | Union and nested declarations, enum constant restrictions and contextual attributes. |
| Generic declarations | §§3.1–3.2 give type/constant formals, defaults, `_` inference and explicit `default` selection. | Permitted value domains, default dependencies and complete inference contracts. |
| Aliases | §§3.1 and 5.4 give namespace-level aliases and transparent public source export. | Cycles, substitution and metadata encoding. |
| Associated types | §§3.1 and 5.4 give requirements, replaceable defaults, qualified bindings, optional `<this T>` and `.via<I>` selection. | Complete associated-equality, inherited-witness rules and metadata. |
| Partial declarations | §5.12 defines one-identity merge and body-provision rules. | Detailed merge compatibility and attribute multiplicity. |
| Core primitive declarations | §2 preserves reserved primitive identities with a privileged backing field. | Artifact identity, permitted members and separate-compilation enforcement. |
| Fields | §3.1 sketches instance fields. | Applicable attribute contracts and layout restrictions. |
| Constructors | §§3.1 and 5.10 give `ensure P is init` minimum guarantees and repeatable unpublished init-property writes. | Completion validation hooks, value/effect compatibility and metadata encoding. |
| Properties | §§3.1, 5.10 and 5.12 give mixed backing, receiver modes, expression bodies and authored indexers. | Implicit storage initialization, ref-result contracts and metadata. |
| Operators | §§3.1, 5.7 and 5.13 describe nominal unary/binary operators and distinct compound-operation contracts. | Compound-operator declarations, tokenization, places and evaluation order. |
| Conversions | §§3.1 and 5.12 sketch `operator T(U source)` and explicit `T(value)`. | Conversion eligibility and constructor/conversion ranking. |
| Interface members | §§3.1, 5.4 and 5.12 give explicit qualified implementations, shared implicit implementations and restricted current-receiver default-body bypass. | Complete inherited-default resolution and representation; constructors remain deferred. |
| Types | §3.2 gives recursive types, tuple types and normalized optionals. | General ownership/reference-result forms and public tuple ABI. |
| Ownership | §§3.2 and 5.12 sketch owner forms and explicit transfer. | Complete ownership-form grammar. |
| Reference access | §§3.2 and 5.12 sketch loans and reference qualifications. | Complete legality of `pin`, `write` and other qualifier combinations. |
| Parameters | §§3.2, 5.1–5.2 give modes, final `params`, eligible Copy defaults and direct/named attribute targets. | Complete qualifier legality and output/loan metadata. |
| Arguments | §§3.2 and 5.1 give labels, source-order evaluation and position-preserving named/positional mixtures. | Complete passing-mode/place and overload contracts. |
| Statements | §3.3 gives blocks, transfers, standalone discard and `yield expression;` / producer `return;`; §§3.1 and 5.12 give the `[Iterator]` marker; §3.4 permits await expressions in async bodies. | Statement-place checks and producer protocol/lifetime contracts. |
| Iteration | §3.3 gives by-value and `ref`/`mut ref` iteration, including `await for`; §5.12 names `do`. | Iterator loan/backing, acquisition/close/cancellation contracts and post-test loop production. |
| Expressions | §3.4 gives conditional/coalescing/optional access, tuples, target-directed static/async lambdas, await and collection spreads. | Callable capture/representation, awaiter registration and collection-fragment contracts; comprehensions/repetition deferred for library/tensor design. |
| Patterns | §3.3 gives shared switch arms, guarded matching, compatible `or` bindings and explicit constant/wildcard transfers. | Full nominal-case, relational and destructuring patterns. |
| Policies | §§3.3–3.4 and 5.12 describe lexical preferences, strict selection and constant-formal forwarding. | Variant-declaration syntax and compile-time queries. |
| Contracts | §§3.3 and 5.8 describe `require` and `ensure` direction. | Declaration clauses and correlated output facts. |
| Static proof | §5.9 describes the portable static-proof floor. | Constant-evaluation and symbolic source forms. |
| Exceptions | §§3.3–3.4 give typed/bare catches, filters, throw expressions, rethrow and explicit captured-failure propagation. | Catch eligibility, capture APIs, transport, unwind phase and chaining metadata. |
| Cleanup | §§3.3 and 5.12 distinguish lexical `defer` from `[Cleanup]`. | Defer action validation and attribute placement. |
| Finalization | §5.12 does not equate GC finalization with cleanup. | Whether finalizers exist and their lifecycle/ABI contract. |
| Documentation comments | §§1 and 5.14 describe attachment and structured prose direction. | Exact markup/reference grammar and XML artifact schema. |

Only an adopted specification and its lexical, grammatical and static-semantic
rules establish conformance. The [implementation reference](../implementation.md)
records supported subsets separately from this proposal.
