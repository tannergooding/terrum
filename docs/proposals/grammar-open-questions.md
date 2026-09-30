# Remaining source-language decisions

> [!IMPORTANT]
> Open recommendations are review inputs, not adopted rules. Entries marked
> **Accepted** record decisions incorporated into the source grammar.
> The [source grammar](../grammar-spec.md) remains authoritative for settled
> forms. This inventory does not reopen those choices or authorize a compiler
> to choose new semantics on the user's behalf.

This is a decision inventory, not a compiler backlog. Stable question numbers
are retained after a decision so related review references remain usable.
Questions are grouped
by the source contract a user must choose, rather than by missing parser
productions or implementation files. The number of questions is not a target.
Examples use approved Terrum syntax where possible to expose the issue;
referenced library types and APIs are illustrative. Explicitly labeled
candidate spellings are unapproved alternatives, and an example whose legality
is the question does not assert that it is valid. No candidate establishes a
new grammar production. Method bodies omitted from surrounding
examples are not a new bodyless-concrete-declaration rule.

## Lexical and declaration choices

### Q01. Escaping reserved words in identifiers

**Accepted:** Permit contiguous `@` followed by a reserved or contextual
keyword spelling, preserving the underlying identifier name and suppressing
keyword recognition at that occurrence. It does not permit arbitrary-string
identifiers or confer primitive-definition authority.
**Example:** `alias @await = Task<int32>;` and `@await pending;`.

**C# model:** A narrow escape would be modeled on C# `@identifier`, not its
Unicode identifier normalization rules. Terrum retains ordinal, unnormalized
Unicode 17 spelling; an escape must not authorize defining a reserved primitive.
See [C# lexical structure](#official-comparison-sources).

**Rationale:** Supports generated/imported names without creating a second
arbitrary-text name or changing primitive authority. See the adopted
[lexical notation](../grammar-spec.md#2-lexical-notation).

### Q02. Reserved versus contextual words

**Accepted:** Use contextual recognition for narrowly placed words and publish
their context table. Reserve a spelling globally only where its grammar
requires that treatment. Future reservations and contextual recognition must
consider ordinary source use; meaning-changing additions require a
source-language version boundary. The adopted table covers `field`, `some`,
`when`, `type`, `policy`, `const`, `via` and pattern combinators.
**Example:** `int32 Read(int32 some) => some;`

**C# model:** Modeled on C# contextual keywords as a technique, not its keyword
list. Terrum's `init` loan, `some` pattern, associated `type`, and policy scopes
have different contexts.

**Rationale:** Avoids accidental source breakage from ordinary names without
making recognition depend on library lookup. Exact source-version selection
remains a tooling contract, not an implicit library-dependent language mode.

### Q03. Exact operator-run boundaries

**Question:** Specify which glyphs join a run and which contextual boundaries
split it: comment openers, optional/type punctuation, generic delimiters,
range punctuation and adjacent prefix operators. The rejection of `x+++y`
as concatenated operators is already settled.
**Accepted operator spacing:** Symbolic binary arithmetic, shift, comparison,
bitwise, Boolean and coalescing expression operators require separation on
both sides; symbolic prefix/postfix operators attach to their operand on
the appropriate side. Member access attaches on at least one side.
Whitespace, line breaks and comments separate tokens and break attachment.
Determine operator roles from grammar before type checking, without
reinterpreting an invalid expression. This replaces the earlier allowance
of unspaced binary arithmetic; the remaining punctuation categories below
are still under review.
**Example:** `items[start.. ^1]` deliberately separates range and end-relative
punctuation; `List<List<int32>>` uses type-context closers.

**C# model:** Not C# maximal-munch operator concatenation. Terrum requires one
defined operator per contiguous run except explicit contextual exceptions.
Unlike C#, operator-role spacing and attachment are source validity rules.

**Recommendation (under review):** Use an explicit allow list, not a deny
list or backtracking over possible expressions. Separate punctuation tokens
require trivia unless a listed lexical/grammatical boundary permits adjacency.
The proposed allow list is:

| Allowed adjacency | Scope and example |
|---|---|
| One defined operator/punctuator spelling | Exactly a spelling assigned by the grammar, such as `++`, `+=`, `<<`, `??`, `?.`, `=>` or `..`; not concatenated operators. |
| Grouping and list delimiters | `(`, `)`, `[`, `]`, `{`, `}`, `,` and `;` bound separate tokens, as in `x * (-y)` or `f(a[i])`; ordinary interior/separator trivia is flexible, with receiver attachment required for calls/indexing. |
| Comment boundaries | Recognize ordinary `//`/`/*` and selected documentation `///`/`//*` openers before operator-run validation outside literals/comments; documentation-block recognition precedes ordinary line comments. |
| Type-context punctuation | Optional `?` followed by generic closers and consecutive generic closers, as in `List<T?>` and `List<List<T>>`; only when required by the type/argument grammar. |
| Range/from-end punctuator | Candidate named composite `..^` introduces an end-relative upper slice bound, as in `items[start..^1]`; not a freely concatenated `..` and `^` or a general arithmetic operator. |

Literal contents and escaped identifier tokens follow their own lexical
rules, not operator-run validation. No other operator-token concatenation is
permitted. New operators must be added to the language's defined spelling
catalog; new adjacency exceptions must be added to this list. Neither a
formatter nor error recovery guesses an expression from an unlisted run.
This keeps common indexing concise while making separation the ordinary
rule and preserving room for future operators. Exact operator-glyph
membership and adoption of the proposed list remain open.

**Additional boundary cases under review:** Delimiter adjacency is a lexical
permission, not permission to mismatch or arbitrarily interleave nesting.
Ordinary code permits adjacent `()`, `[]` and `{}` boundaries without spaces;
literal/interpolation contents retain their own delimiter rules. Generic
closers adjoining assignment, as in `alias A<T>=T;`, are not a listed
exception: use `alias A<T> = T;`. Group constant-generic expressions containing
comparison/shift punctuation when needed to make their boundary explicit.
The `..^` proposal does not permit arbitrary range/spread-plus-prefix runs.
Prefix chains such as `!!x`, `&*p` and `*&x` need grouping unless a distinct
spelling or adjacency boundary is deliberately defined. Separating two
symbolic prefix operators with trivia does not satisfy operand attachment.

**Accepted access attachment:** Apply an operator-specific attachment rule,
separate from punctuation-run recognition. Member-access `.` and `?.` must
directly adjoin the receiver's final token or the following member's first
token, or both. Thus `x.y`, `x. y`, `x .y` and a fluent chain continuing with
`.Member` on the next line remain valid; `x . y` and `x ?. y` do not.
Whitespace, line breaks and comments break attachment on that side.
Grouping boundaries can supply attachment, as in `(x). Member`.
The numeric-receiver grouping rule still applies independently.
If Q26 selects prefix dereference, require `*` to attach to its following
operand token: `*p`, `*(p)` and `*(*p)` are valid forms, but `* p` and
`* *p` are not. Binary multiplication remains distinct and permits `x * y`.
This attachment restriction deviates from C#'s trivia-insensitive member
access and prefix dereference; it does not make arbitrary punctuation runs
valid or select pointer-expression syntax by itself.

**Accepted call/index attachment:** Require call
and index opening delimiters to attach directly to their receiver.
Calls `f(...)` and indexing `f[...]` are delimited postfix operators, not
grouping constructs; their grammatical role selects the attachment rule.
Use `f(x)`, `int32(value)`, `new Widget(value)`, `items[i]` and
`items?[i]`; reject `f (x)` and `items [i]`. Attachment also excludes
comments or line breaks between the receiver and opening delimiter.
For generic calls, the receiver ends at the type-argument closing delimiter,
as in `f<T>(x)`; parenthesized receivers can attach at their closing
parenthesis. This treats calls/indexing as receiver-attached access rather
than freely spaced structural punctuation. Grouping and control-flow
parentheses have no receiver: `(x + y)` and `if (ready)` remain valid,
subject to independent operator rules inside them.
**C# model:** Call/index syntax is C#-inspired, but this attachment
restriction deliberately deviates from C#'s trivia-insensitive delimiters.

**Accepted delimiter interior spacing:** Keep interior trivia flexible
inside ordinary parentheses, brackets and braces:
`f(x)` and `f( x )`, `(x + y)` and `( x + y )`, and `items[i]` and
`items[ i ]` are all valid. Ordinary block braces also impose no padding
requirement of their own. Delimiters already establish explicit structural
boundaries, so interior padding does not undermine operator-run evolution.
Receiver attachment and operator spacing still apply independently:
`f (x)` and `f(- x)` reject. **C# model:** This follows C#'s flexible
interior trivia, not a prescribed formatter style. Literal/interpolation
delimiters retain their own rules. Separators follow the rule below.
This includes multiline arguments, with the call opener attached
to the callee and the arguments/closing delimiter on subsequent lines.

**Accepted separator spacing:** Treat commas and semicolons as fixed
structural separators, not postfix operators, with flexible
trivia: `f(x,y)`, `f(x, y)` and `f(x , y)` are valid forms, as are
`return x;` and `return x ;`. They already establish explicit boundaries
and are excluded from operator runs, so this does not weaken the operator
spacing/evolution rules. A formatter may prefer attached separators followed
by spaces or line breaks without making that style mandatory.
**C# model:** This follows C#'s trivia-insensitive separator spacing.
It does not change where separators, trailing commas or empty statements
are permitted. Leading-comma alignment across lines is also valid;
spacing around a comma is not a formatter mandate.

**Generic opener attachment question (awaiting review):** Should generic
argument and declaration parameter lists' `<` attach directly to the
preceding name?
**Recommendation:** Require `List<T>` and `F<T>(x)`, rejecting
`List <T>` and `F <T>(x)`. Generic application is a delimited construct,
whereas comparison remains a spaced binary operation, as in `x < y`.
This supplies a grammatical boundary without consulting operand types or
trying another interpretation after a binding failure.
Apply the same attachment rule to generic parameter lists in declarations.
**C# model:** Generic syntax is C#-inspired, but requiring opener attachment
deviates from C#'s trivia-insensitive generic delimiters. Interior trivia,
keyword-selected angle lists and optional-type punctuation remain separate
decisions. This recommendation has not been accepted.

**Comment-fence question:** Should a block comment select its closing fence
from the opening star count, analogously to the selected delimiter length of
raw text? Candidate `/*** **/ ***/` would contain `**/` as payload and end at
the three-star fence. This is not C# block-comment syntax and is not an
adopted Terrum rule. **Accepted documentation marker:** Documentation adds
one slash to the ordinary line/block form: `///` and `//* ... *//`.
Documentation blocks are recognized before ordinary line comments.
The longer star-count fences below remain candidates, independent of that
accepted marker distinction:

| Ordinary form | Documentation form |
|---|---|
| `// text` | `/// text` |
| `/* text */` | `//* text *//` |
| `/** text **/` | `//** text **//` |
| `/**** text ****/` | `//**** text ****//` |

The selected marker is in the canonical source/lexical grammar; a plain
`/** ... */` is not a documentation form. Select generalized fence syntax,
including empty/nonoverlapping fences, shorter terminators as payload and whether
longer terminator-looking runs are payload or errors. **Recommendation:** If
fences are adopted, use a maximal opening-star count and whole-run closing
star matches, not a matching suffix inside a longer star run. Specify the
one-/two-slash closing boundary without consuming a following comment's
slashes. Do not introduce nesting as an accidental side effect.

**Constant-generic argument boundary question:** Should direct constant
arguments be limited to literals and constant names (including qualified
names), with other expressions parenthesized? Candidate
`InlineArray<T, (N >> 1)>` explicitly separates a shift from generic closers;
`InlineArray<T, (N >= Minimum ? N : Minimum)>` separates comparison and
conditional syntax from the argument boundary. Direct `InlineArray<T, N>`
and `InlineArray<T, 16>` remain concise. Under this proposed rule, unary
negative arguments also need grouping because their sign is an operator,
not part of the literal. This does not select constant-value domains or
default dependencies from Q12. It is not a restriction on interpolation:
the accepted interpolation grammar already parses a complete conditional
expression before alignment/format suffixes, without mandatory grouping.

### Q04. Hex-float versus member-access token boundaries

**Accepted:** Require grouping before member access on bare numeric literals
of every radix and format, including optional member access and witness
selection. Trivia does not remove the restriction. Valid numeric fractions,
exponents and range bounds remain unchanged; malformed numeric spelling
cannot be reinterpreted as valid ungrouped member access.
**Example:** `float32 x = 0x1.fp+2;` is a hex-float; `(0x1).face`
has a separate member-access receiver boundary, still requiring a valid
numeric target. `float32(0x1.fp+2).Foo` supplies an explicit receiver type.

**C# model:** Not a C# hexadecimal-floating feature; C# has hexadecimal integer
tokens but no corresponding hex-float literal. Decimal recovery offers only
comparative lexer practice.

**Rationale:** Trades a small grouping requirement for stable literal/member
boundaries independent of whether the member name resembles fractional or
exponent digits. Strings and other delimited literals have no equivalent
restriction. Grouping does not supply a missing literal type. Exact malformed
token recovery/diagnostic text remains tooling work.

### Q05. Namespace nesting and local imports

**Accepted:** Allow nested block namespaces and lexically scoped imports
before declarations in each scope. Nested qualified names extend their
enclosing namespace. Imports apply to their lexical scope and nested blocks,
not other blocks of the same namespace or other source files. Preserve the
prohibition on mixing file-scoped and block namespaces in one file.
**Example:** `namespace A.B { struct Value { } }` expresses a qualified
namespace without requiring nested blocks.

**C# model:** C# permits nested namespaces and namespace-local `using`
directives. Terrum already permits file-scoped or block form without mixing
them in one file; adopting local imports need not add global/static imports.

**Rationale:** Permits generated and hand-authored namespace grouping without
import-order lookup or merging the imports of separately authored blocks.
See the adopted [source-file structure](../grammar-spec.md#31-source-file-structure-and-declarations).

### Q06. Visibility and modifier applicability

**Accepted visibility boundary:** Namespace/file-level declarations permit
`public` or module-scoped `internal`, not `private`. Type-member `private`
is restricted to the declaring type within its module. Equal namespace names
in another module never extend either access domain; there is no friend-module
access through linking or unsafe authorization.

**Remaining question:** Complete the declaration-kind matrix:
interface-member access, static nominal containers, default access, and combinations of
`mut`, `unique`, `partial`, `unsafe` and `async`. Permutation is already legal;
duplicates/conflicts are not. Should a separate file-limited access domain be
available, and to which declaration kinds would it apply? `file private` and
`file internal` are review ideas, not adopted modifier combinations.
**Example:** `internal struct Helper { }` is accessible throughout its module,
not only the file or namespace that declares it.

**C# model:** Partly modeled on explicit declaration modifiers. Do not import
`protected`, friend assemblies, stateful class inheritance, or C#'s different
top-level-private restrictions.

**Rationale for the accepted boundary:** Namespace organization does not add
another access domain, and `private` is not an alias for `internal`.
Neither access choice changes ownership, layout or effects.
**Recommendation for the remaining questions:** Specify each declaration
kind's legal modifiers independently. Consider file-limited access separately
if a concrete source-local helper use warrants it; do not infer a new `file`
modifier or equivalent attribute merely from accepting the module boundary.

### Q07. Attribute arguments and multiplicity

**Question:** Which constant values/type references can attribute arguments
contain, how are attribute identities registered, and when can one attribute
repeat or merge? Direct/named targets and selectors are already specified.
**Example:** `[param(count): Limit(16)] void Read(int32 count) { }`

**C# model:** Modeled on resolved attribute identity and target applicability,
not CLR attribute classes, boxed attribute values, or C#'s fixed argument-type
catalog.

**Recommendation:** Use checked declared argument types with compile-time
values, explicit target eligibility and explicit multiplicity. Retain
source-order merging only where multiplicity permits it; reject unknown
targets rather than redirecting them.

### Q08. Partial-declaration compatibility

**Question:** Complete matching and conflict rules for generic parameter names,
accessor parts, attributes, defaults and repeated clauses. One identity, one
implementation, consistent contracts and deterministic file/source order are
already approved.
**Example:** `partial int32 Read(int32 value);` and
`partial int32 Read(int32 value) => value;` illustrate matching parts.

**C# model:** Partial composition is C#-inspired. Terrum does not inherit C#'s
historical erasable private partial methods; it requires an implementation
and forbids generators from altering an existing published contract.

**Recommendation:** Match by declared identity and complete signature; require
explicit repeated defaults/contracts to agree rather than accumulate stronger
obligations. This makes generated-part integration reviewable and stable.

### Q09. Enum constant and flag restrictions

**Question:** Specify eligible underlying integer types, implicit member-value
progression/overflow, duplicate-valued names and `[Flags]` validity checks.
Unnamed underlying values remain permitted.
**Example:** `enum Access : uint32 { Read = 1, Write = 2 }`

**C# model:** Integral-base enums and named flags are C#-inspired, but Terrum's
width families, required constant evaluation and checked arithmetic differ.
Do not infer closed-case exhaustiveness from an enum declaration.

**Recommendation:** Check constants in the exact underlying type; reject
implicit successor overflow, permit deliberate aliases, and keep flags
annotation validation separate from runtime value validity.

### Q10. Reserved primitive bindings and privileged members

**Question:** Finalize the primitive-name set, native-sized spellings, and
which member forms a designated Core declaration may supply. One intrinsic
backing field and the prohibition on source-defined primitive operators are
already specified.
**Example:** `struct int32 { private int32 _value; }` is meaningful only in
the designated Core compilation.

**C# model:** Not C# source redefining `System.Int32`. Terrum uses ordinary
identifier tokens with privileged primitive binding; no keyword escape grants
that privilege.

**Recommendation:** Publish a closed primitive binding catalog independently
of optional library APIs, and allow ordinary checked members only where they
cannot redefine representation or built-in arithmetic. Artifact identity is
separate metadata/build work, not this source decision.

## Types, lookup and references

### Q11. Alias recursion and substitution

**Question:** Specify well-formed generic alias expansion, recursive cycles,
qualification and accessibility of the expanded target. Transparent public
export and namespace-only placement are already approved.
**Example:** `public alias Rows<T> = List<List<T>>;`

**C# model:** Not C# `using` aliases: C# aliases do not introduce generic alias
formals or exported alias declarations. See the official using reference.

**Recommendation:** Require finite structural expansion, diagnose cycles with
the declaration chain, and preserve complete qualified arguments. Recursive
data should use nominal declarations, not an infinitely expanded synonym.

### Q12. Constant formals and dependent defaults

**Question:** Which compile-time value domains may form generic arguments?
Can defaults refer to earlier or later formals, and how are dependency cycles
diagnosed? Defaults may already interleave with required formals; `_` never
falls back to a default.
**Example:** `struct Pair<T, U = T> { }` exposes a dependency between formals.

**C# model:** Not C# generic parameter defaults or constant generic parameters;
neither is part of the C# generic declaration model.

**Recommendation:** Beyond the named policy enums already contemplated by the
grammar, initially permit fixed-width integer, Boolean and nominal enum
constants, but not floating values, arbitrary structs, references or allocated
object identities. For example, the candidate
`struct Tile<T, const uint32 Width, const uint32 Height = Width> { }`
would allow a width-dependent default. A default may depend only on earlier
formals; self, forward and cyclic dependencies reject. These proposed domain
and dependency restrictions support widths/policies without a cyclic solver
or an unbounded compile-time object identity model.

### Q13. Remaining overload specificity and constructor inference

**Question:** Complete applicability and specificity for combinations of
generic inference, declared projections, defaults and constructor candidates.
Do not revisit argument dominance, no numeric fallback, fixed-versus-expanded
priority, member priority, or the exclusion of return-only overloads.
**Example:** `int32 Parse(int32 value) => value;` versus a generic
`T Parse<T>(T value) => value;` exposes the remaining equally applicable case.

**C# model:** Partly C#-inspired, with material differences: Terrum may use an
expected result to fill unresolved generic positions, forbids implicit numeric
promotion searches, and has explicit loan/witness contracts.

**Recommendation:** Prefer an applicable nongeneric exact declaration only
after argument quality ties; define structural generic specificity rather
than counting inferred parameters. Apply the same staged inference to
constructors without deriving types from field capture or arbitrary witnesses.

### Q14. Associated equality constraints

**Question:** Choose the source form that binds an associated type to another
formal and define its inference direction and collision checks.
**Example:** `T.via<ISource>.Item` already explicitly selects a projection;
the missing form is a constraint equating that projection with `U`.

**C# model:** Not C# associated types. Its generic constraints and static
abstract interfaces are comparison points, not associated-type equality.

**Recommendation:** Add an explicit equality clause with the candidate spelling
`where T.via<ISource>.Item == U`. Here `==` compares type identities in a
constraint, not runtime values, and the clause requires the named `ISource`
witness rather than searching for an implementer. Resolve the witness and
ordinary type arguments first, then check equality; do not infer an otherwise
unbound `T` backward from `Item`. The new clause is proposed, not accepted.

### Q15. Inherited default/member resolution

**Question:** Complete qualification and resolution for multiple inherited
paths to the same member or competing defaults. Explicit implementations,
separate witness identities and restricted `default<I>.Member()` are settled.
**Example:** `interface ICombined : ILeft, IRight { }` needs a resolution rule
when both paths supply the relevant default.

**C# model:** C# default interfaces require a unique most-specific
implementation. Terrum must not import an inheritance-order tie-break or a
physical vtable model; its associated members also participate.

**Recommendation:** Require one unambiguous inherited identity/default or an
explicit resolution. Reuse the same rule for associated defaults and callable
defaults, so adding an unrelated base cannot silently retarget calls.

### Q16. Extension declarations and call disambiguation

**Question:** Choose declaration/import scope and an explicit way to name a
selected extension when ordinary lookup is ambiguous. Extensions do not
satisfy interface witnesses; applicable members already outrank extensions.
**Example:** `value.Measure()` could have two imported extension candidates;
`value.via<IMeasure>.Measure()` instead selects a real witness, not either
unrelated extension.

**C# model:** Extension lookup is C#-inspired, but do not import C# 14 extension
block syntax or permit retroactive conformance merely because it is available.

**Recommendation:** Keep explicit static-call qualification available and
make extension eligibility lexical, with the receiver's full access/loan
contract. Avoid a second overload-ranking system.

### Q17. Adapter declaration and forwarding

**Question:** How does source declare an explicit adapter and checked member
forwarding, including any one-way container projection?
**Example:** `struct ExtraView : IExtra { }` can name a nominal witness, but
does not by itself forward an original object's representation or members.

**C# model:** Not C# extension methods or inheritance. Terrum adapters retain
their own witness set; no original type gains a new conformance.

**Recommendation:** Begin with explicit nominal wrappers and declared
forwarding operations. Add projection syntax only where validity, ownership,
access, backing and cost contracts are expressible; equal bits are insufficient.

### Q18. Family and conditional-member declarations

**Question:** Choose source declarations for family opt-in, member eligibility,
conditional conformance and checked erased projections.
**Example:** `TextSpan<char16>` and `TextSpan<void>` name an already described
family, but ordinary `struct Box<T> { }` must not acquire `Box<void>` erasure.

**C# model:** Not C# constructed generic types alone. Terrum needs explicit
relationships that C# name/arity matching does not provide.

**Recommendation:** Separate family membership from extension lookup; require
declared projection and per-member constraints. Keep a conditional member's
guarantee visible to definition-site generic checking.

### Q19. Complete parameter and reference qualifier matrix

**Question:** Reconcile `ref T`, `mut ref T`, ordinary `ref<T>` values,
`write ref<T>`, `implicit ref<T>`, `init ref<T>` and `pin ref<T>` in every
parameter/result/storage context. Existing access distinctions remain fixed.
**Example:** `void Inspect(implicit ref<Item> item) { }` and
`void Update(write ref<Item> item) { }` need consistent explicit-call rules.

**C# model:** Not aliases for C# `ref`, `in` and `out`. Terrum separates the
slot, referent authority, vacancy and dependencies; readonly is not deep
immutability.

**Recommendation:** Publish a passing/storage matrix and one explicit
expansion for each convenience form. Reject combinations that cannot express
their access or initialization contract, rather than invent implicit copying.

### Q20. Declaring result, capture and backing dependencies

**Question:** Complete source notation for borrowed results, stored views,
multiple backing sources, lambda captures and suspension dependencies.
**Example:** `ref<T> Select<T>(ref<T> left, ref<T> right)` needs a published
dependency relationship; its result type alone does not identify the backing.

**C# model:** Ref safety and `scoped` are relevant prior art, not Terrum syntax.
Terrum additionally distinguishes descriptor, backing and allocation/lease
lifetimes and preserves explicit ownership.

**Recommendation:** Begin with an explicit result annotation, for example the
candidate `[return: DependsOn("left", "right")]` on `Select`. Constant strings
identify parameter declarations of that callable; unknown names or incompatible
dependency kinds are errors, not runtime lookup. The result may depend on
either input, so callers retain both possible backing obligations rather than
only backing common to both inputs; neither input's lifetime is extended. Use separate
targeted contracts for receiver storage, forwarded backing and stored captures;
the result annotation alone must not imply all three. This proposed attribute
contract and its default treatment of unannotated results remain unapproved.

### Q21. Reservation versus immediate reference formation

**Question:** Do ordinary writable receiver/reference calls need a
reservation/activation phase? Implicit readonly borrowing must use the same
mechanism as its explicit expansion.
**Example:** `target.Update(target.Read())` can conflict if forming the receiver
loan precedes evaluating an argument that accesses it.

**C# model:** Not a C# ref-parameter rule transplanted into exclusive loans;
C#'s aliasing model does not provide Terrum's required exclusion contract.

**Recommendation:** Keep immediate formation unless concrete APIs demonstrate
a need for reservation. If introduced, specify reentrancy and invalidation
for both explicit and implicit forms before accepting additional calls.

## Construction, operators and expressions

### Q22. Implicit storage initialization and completion hooks

**Question:** How are auto-backed properties initialized when not assigned by
a constructor/caller, and what explicit hook validates completed construction?
Mixed backing, storage order, repeated unpublished `init`, and constructor
minimum guarantees are already approved.
**Example:** `struct Point { int32 X { get; init; } }` does not establish a
universal zero value merely by allocating a backing field.

**C# model:** C# auto-properties and init accessors are prior art. Terrum does
not inherit zero-initialized `default(T)` or substitute `required` annotations
for checked constructor guarantees.

**Recommendation:** Treat unassigned backing as vacant, require explicit
initialization, and make completion validation an explicit declared operation
with an identified phase. Do not let ordinary live-value withers invoke `init`.

### Q23. Nominal compound-operator declaration shape

**Question:** Complete declarations for `+=` and other compound operators,
including destination/operand eligibility, result and effect contracts.
The distinct operation contract with a `write ref<T>` destination is settled.
**Example:** `counter += increment;` must select that contract, not silently
expand to `counter = counter + increment`.

**C# model:** Deliberately different. C# 14 adds public instance `void`
compound operators; older C# derives compound assignment from binary
operators. Neither defines Terrum's destination-loan form.

**Recommendation:** Use a static, void-returning destination-loan candidate,
for example `public static void operator +=(write ref<Counter> destination, int32 increment)`
with an ordinary body. Extend the same shape to the eligible compound family,
but not the built-in `??=` operation. The assignment is a statement: it does
not produce a copied replacement value or infer an owning transfer. Evaluate
the destination and operand once under the ordinary access/effect/loan rules;
the declaration must state its effects, without an inferred `NoThrow` or
transactional rollback promise. This declaration form is proposed, not
accepted. Keep static `++`/`--` operand-shaped results distinct; do not add
C# instance mutation operators by analogy.

### Q24. Constructor and conversion eligibility/ranking

**Question:** When source- and destination-owned conversions both apply, or a
constructor has a matching parameter, what is eligible for `T(value)`?
Static `operator T(U source)` spelling and explicit invocation are settled.
**Example:** `var parsed = Target(source);` must have one recorded declaration.

**C# model:** Not C# syntax: C# separates `new T(x)` construction from `(T)x`
conversion. Terrum's type-call form requires its own rule.

**Recommendation:** Keep `new T(...)` for construction and require a unique
eligible explicit conversion for a nonliteral type-call. Reject competing
source/destination conversions rather than choose by declaration order.
Preserve contextual literal construction as its own case.

### Q25. User-defined short-circuit operations

**Question:** Can nominal `&&`/`||` participate, and how do they select a
truth test, conditionally evaluate the right operand and preserve ownership?
The `true`/`false` pair and its `bool` result do not answer all these rules.
**Example:** `left && right` currently has settled built-in Boolean ordering.

**C# model:** C# does not directly overload `&&`/`||`; it combines `true`/`false`
with eligible `&`/`|`. Terrum has not adopted that complete derivation.

**Recommendation:** Keep nominal short-circuiting unavailable until one
explicit protocol covers all evaluation and result obligations. A normal
binary witness cannot license eager evaluation of both operands.

### Q26. Pointer formation, dereference and pin acquisition

**Question:** Select the source operations that form/dereference pointers and
acquire a pin, separately from the already approved pointer/reference types.
**Example:** `ptr<int32>` and `pin ref<int32>` describe different authority;
neither spelling alone roots or pins arbitrary storage.

**C# model:** C# unsafe pointer operators and `fixed` are comparison points,
not adopted syntax. Terrum has checked provenance/backing dependencies and
does not use `pin` as a statement keyword.

**Recommendation:** Use candidate prefix `&place` and `*pointer` operations
inside `unsafe`, for example `unsafe { ptr<int32> p = &local; int32 n = *p; }`.
Restrict address formation to addressable, nonmoving storage or storage covered
by an explicit live pin; `unsafe` itself does not root or pin managed storage.
Retain source provenance, bounds, access and lifetime obligations, and do not
grant writable access merely by taking an address. Acquire pins through a
checked library operation, with candidate call shape
`Pin.TryAcquire(source, init pinned)` and a scoped release/dependency contract,
not a new `pin` statement. Both operators and the acquisition API are proposals;
their concrete result/guard types must be reviewed together before adoption.

**Dereference/member grouping question:** Under the candidate prefix `*`
operation and ordinary postfix precedence, `(*p).M` accesses a member of the
dereferenced value, while `*(p.M)` dereferences a pointer-valued member.
`(*(*p)).M` dereferences twice before member access; `*(*p).M` would instead
dereference the member obtained after one dereference. These are distinct
operations, subject to ordinary type/authority checks, not interchangeable
spellings. **Recommendation:** Require the explicit grouped forms when
dereference and member access combine, rather than allowing the visually
ambiguous `*p.M` if prefix dereference is selected. This does not settle
whether standalone `*p` is allowed. An alternative candidate is a fenced
dereference `(*expression)` with `(*` as a dedicated opener and `)` as its
close; the entire interior expression supplies the pointer. Under that
candidate `(*p + offset)` dereferences the address expression `p + offset`,
not an already-dereferenced value plus an offset. Nested forms are
`(*(*p))`; no bare `**p` or `****p` spelling is added. Selecting a contiguous
`(*` token versus a grammar construct allowing trivia between `(` and `*`
also needs a decision. Keep ordinary prefix dereference and this fenced
alternative distinct rather than adopting one from a grouping example.
Also compare an indexer
spelling: candidate pointer `p[0]` corresponds to one dereference and
`p[0][0].M` makes two indirections followed by member access explicit.
Whether to offer indexing alone or alongside prefix dereference remains
open; indexing must retain the same unsafe/provenance/lifetime/access
obligations, not imply an unavailable length or bounds guarantee.
Under the general punctuation-separation proposal, multiple prefix
indirections require an explicit boundary between operators. With the
access-attachment direction from Q03, prefix dereference must adjoin its
following operand token, so use `*(*p)`, not `* *p` or `* p`.
Grouping follows from attachment plus the absence of a `**` spelling,
not a blanket restriction on every prefix operator.
Do not add a `**` punctuation exception.
Pointer types remain `ptr<ptr<T>>`, not C-style `T**`.
This grouping restriction is proposed, not part of the accepted postfix
grammar. It does not supply provenance, bounds, pinning or writable authority.

### Q27. Nominal sums and closed-case declarations

**Question:** Choose declaration/source construction forms for tagged cases
and the explicit closed-case contract. Optional flattening and unknown-case
safety are already settled.
**Example:** `enum LookupStatus : int32 { Missing, Found }` with a separate
`init ref<T?>` output illustrates why `Found(null)` differs from `Missing`.

**C# model:** Not a C# enum, inheritance hierarchy or `Nullable<T>` wrapper;
none supplies Terrum's owning tagged-payload contract automatically.

**Recommendation:** Use explicit nominal cases and explicit closedness,
keeping evolvable cases separate from closed exhaustive dispatch. Require
rooting/cleanup of unknown payloads before allowing compatible extension.

### Q28. Complete pattern and type-test forms

**Question:** Complete relational, nominal-case, type-test and destructuring
patterns, including binding ownership and access. Existing guards,
`and`/`or`/`not`, compatible `or` bindings and fallback behavior remain fixed.
**Example:** `switch (value) { some item => Use(item), _ => 0 }`
already exposes an optional payload without defining arbitrary destructuring.

**C# model:** Pattern families are C#-inspired; Terrum does not inherit
property-getter duplication, implicit numeric conversion, or ownership-neutral
payload extraction merely from similar spelling.

**Recommendation:** Specify evaluation once, binding modes and refinement
invalidation before extending patterns. Keep tuple structure separate from
name-discovered `Deconstruct` methods.

### Q29. First-class index/range values

**Question:** Are `^offset` and `start..end` permitted as first-class values
outside indexing, and which declared contracts accept them?
**Example:** `items[start.. ^1]` is already an approved contextual slice.

**C# model:** C# uses `System.Index`/`System.Range`; Terrum has not adopted
universal types or their runtime representations.

**Recommendation:** Keep the contextual forms initially and require an
explicit registered type/contract before allowing standalone values. This
preserves checked domains and backing without inventing an allocating range.

### Q30. Nested collection-fragment construction

**Question:** How are nested fragments contextually typed, and is construction
without an intermediate collection part of the registered contract?
**Example:** `List<int32> values = [1, ..(include ? [2] : [])];`

**C# model:** Target-directed collection expressions and spreads are
C#-inspired. Terrum retains explicit move/refusal/abandonment contracts and
must preserve its capture/drain ordering.

**Recommendation:** Require explicit fragment support from the target
registration. Do not promise allocation elision by syntax alone or turn
observable builder effects into an optimization detail.

### Q31. Producer contract phases and suspension loans

**Question:** Which declared clauses apply at producer creation, advance,
yield and completion, and how are element/capture loans published?
**Example:** `[Iterator] Sequence<int32> Values() { yield 1; return; }`
already starts lazily; adding an `ensure` must not silently mean per-yield.

**C# model:** Iterators are C#-inspired, with approved differences:
resolved `[Iterator]`, `yield expression;`, ordinary `return;`, and explicit
checked close/loan obligations. C# does not supply Terrum's clause phases.

**Recommendation:** Keep ordinary creation and completion contracts distinct
from advance/item contracts, selected by a declared producer protocol.
Do not infer lifetime extension or per-yield checks from the body.

### Q32. Catch eligibility and failure matching

**Question:** Which failure types may be caught, how does typed matching select
a handler, and at what checked phase are filters evaluated? Transport bytes
and exception object ABI are separate.
**Example:** `try { Work(); } catch (Failure error) when (CanHandle(error)) { Recover(error); }`
uses the approved typed/filter form within required unsafe recovery scope.

**C# model:** Syntax is C#-inspired, not an automatic `System.Exception`
hierarchy. A settled deviation is that a throwing filter does not silently
become false; it follows ordinary failure/chaining rules.

**Recommendation:** Match declared catchable contracts in source order and
make filter-phase access explicit. Never require speculative reads of
unwound frames or silently erase a filter failure.

### Q33. Policy variant declarations and compile-time queries

**Question:** Choose how a variant identifies its base declaration and policy
tuple, and whether a source policy query tests an active request or a selected
call. Exact-tuple lexical preference and strict `require policy` are settled.
**Example:** `policy<Arithmetic.Wrap> { Use(value); }`

**C# model:** Not C# `checked`/`unchecked`. Terrum selects a declared variant
of an already resolved base; the request is not ambient behavior inherited
by callees.

**Recommendation:** Use a candidate declaration annotation such as
`[PolicyVariant("Add(int32, int32)", Arithmetic.Wrap)]` on the variant callable.
The compiler resolves the constant signature string to one base declaration in
the declaring scope and checks the policy tuple and complete callable contract;
it must reject ambiguity rather than select an overload by spelling alone.
This proposes an attribute contract, not an adopted attribute or wire identity.
Do not allow import-attached variants. Omit a general policy query until its
subject is explicit; an active preference is not proof of selected behavior.

### Q34. General constants and type/layout queries

**Question:** Complete eligible declaration contexts/value domains for
constants and the source identity of type/layout/reflection queries.
Required constant evaluation and static body proofs already have distinct
roles; their portable rule floor is separate proposal work.
**Example:** `const int32 count = 4;` inside a body is established; using a
computed value in `[Layout(...)]` must obey a defined compile-time contract.

**C# model:** Partly C#-inspired syntax, but Terrum permits required checked
evaluation rather than C#'s restricted constant-expression model. It does not
inherit CLR reflection objects or universal `default(T)`.

**Recommendation:** Define compiler-known query identities and bounded
constant domains, not magic arbitrary library names. Keep compile-time-only
formation distinct from optional runtime folding.

### Q35. External declarations and convention annotations

**Question:** Complete `extern` declaration forms, import identity, calling
convention annotation placement and caller-unsafe obligations.
**Example:** `unsafe int32 ReadDevice(uint64 address) { unsafe { return 0; } }`
illustrates the existing obligation boundary before replacing the body with
an external definition.

**C# model:** C# external declarations and function-pointer convention
attributes offer comparison, not adoption of P/Invoke, marshalling or CLR
managed conventions.

**Recommendation:** Make external identity/convention explicit and retain
the complete Terrum callable contract. Do not let an ABI annotation suppress
exception containment, rooting or loan obligations.

### Q36. Whether to provide GC finalizers

**Question:** Is a separate GC-finalization feature needed at all? There is no
`~Type()` production. Deterministic owner cleanup is already a different
mechanism.
**Example:** `[Cleanup] void Release() { }` on an eligible owner does not
register a GC finalizer.

**C# model:** Not implicit adoption of C# finalizers or their resurrection and
runtime-thread behavior.

**Recommendation:** Keep finalizers absent unless a concrete API need survives
explicit ownership/supervision alternatives. If added, review resurrection,
affinity, failure and resource guarantees together, not as syntax sugar.

### Q37. Loop-label scope and shadowing

**Question:** Specify whether nested enclosing loops may reuse a label and
whether labels share a declaration space with values. Named loop targets are
approved; the source contract does not yet spell out collision rules.
**Example:** `outer: while (ready) { inner: do { break outer; } while (again); }`
has unambiguous distinct labels.

**C# model:** Not C# `goto` labels: C# `break` and `continue` are unlabeled.
Terrum labels name enclosing loops only and cannot escape cleanup actions.

**Recommendation:** Reject duplicate simultaneously enclosing loop labels
initially, in a separate label namespace. Reusing labels in disjoint scopes
is harmless; silently retargeting a transfer through nested shadowing is not.

## Documentation source choices

### Q38. Documentation attachment and structured headings

**Question:** Complete attachment across blank lines, attributes, partial and
generated parts, plus unknown-heading and heading-case behavior. GFM prose,
implicit summary, top-level metadata headings and separate XML output are
already approved.
**Example:** `/// # param value` followed by a documented declaration must
attach to that declaration, not to an intervening attribute's implementation.

**C# model:** Declaration attachment is C#-inspired; the GFM heading model is
not C# XML documentation syntax.

**Recommendation:** Parse Markdown first, recognize a finite case-sensitive
heading vocabulary only at top level, and diagnose unknown metadata-looking
headings without interpreting code/list/quote contents as directives.

### Q39. Complex documentation references and inheritance

**Question:** Select overload/generic member reference spelling, escaping,
custom-label collisions, and explicit inherited-document selection.
**Example:** `/// See @Type.Member and [details](README.md).` distinguishes
symbol references from ordinary navigation.

**C# model:** C# `cref` overload identity is useful prior art; Terrum also has
declaration-scoped mentions and explicit `xref:` navigation. Backticks remain
code, not inferred references.

**Recommendation:** Reuse source name/type syntax where unambiguous and require
an explicit signature when overload selection is needed. Make inheritance
selection explicit; never copy a stronger contract from prose.

## Additional ownership and representation source choices

### Q40. Wither updates on unique owners

**Question:** Define receiver consumption, field replacement, failure and
cleanup for wither-style updates on unique/owning values. Ordinary functional
update does not reopen construction-only `init`.
**Example:** `var replacement = current.with(Name: name);` needs an ownership
contract if `current` contains a uniquely owned resource.

**C# model:** C# record `with` is only comparative prior art. Terrum's
`.with(...)` direction is not a C# shallow copy or synthesized record clone;
non-`Copy` ownership cannot be duplicated.

**Recommendation:** Require an explicit consuming or borrowing update contract
for owners and define the old/new cleanup state on failure. Do not synthesize
a copy-based wither for every nominal type.

### Q41. Iteration element-binding modes

**Question:** Complete value, borrowed and consuming bindings in ordinary and
asynchronous iteration, including advance invalidation. Loop expansion and
checked close are already selected.
**Example:** `for (var item in items) { Use(item); }` needs a binding contract
when an element is non-`Copy` or backed by an advancing cursor.

**C# model:** C# `foreach`/`ref foreach` supplies useful comparison, not
Terrum's spelling or ownership rule. Terrum also checks capture, suspension
and asynchronous close.

**Recommendation:** Derive the element's access from the declared iteration
protocol and require explicit consumption or writable borrowing when needed.
Never promote an iteration-local view to an independently owning value.

### Q42. Layout-control argument spelling

**Question:** Choose the attribute argument names/forms for member packing
and aggregate over-alignment, including their combined use. Accepted
`Auto`, `Sequential`, `Union`, `Transparent` and `Blittable` semantics must
not be altered by this syntax choice.
**Example:** `[Layout(LayoutKind.Sequential)] struct Pair { int32 First; int32 Second; }`
establishes field order, but does not select packing or over-alignment.

**C# model:** C#/.NET layout attributes are comparative prior art; the
`StructLayoutAttribute.Pack` field limits member/derived type alignment.
The candidate below instead uses positional constant arguments and adds a
separate aggregate-alignment control. Terrum's aggregate over-alignment and
explicitly checked `Blittable` boundary do not follow from CLR packing or
sequential layout.

**Recommendation:** Use candidate positional arguments in the order
kind, member-packing limit, aggregate alignment:
`[Layout(LayoutKind.Sequential, 1, 16)]` would request one-byte member packing
and 16-byte aggregate alignment. This fits the existing constant-argument
production without presuming a decision on named attribute arguments. The
argument contract is unapproved; it must preserve the selected packing,
over-alignment, tail-padding and array-stride semantics. Neither extra argument
freezes layout or grants `Blittable`. Reject unsupported target combinations,
including custom controls on `Transparent`, and retain separate published-
layout eligibility. Target limits and wire encoding remain ABI work.

## Boundaries that are not unanswered source decisions

| Category | Treatment |
|---|---|
| Already settled | Raw quote fences/framing/newline directives/brace splitting; interpolation format alternatives and capture order; grouped callable/lambda clauses and contextual `return`; aliases' transparent export; optional `<this T>`; `.via<I>` and restricted default-body calls; constructor minimum guarantees; repeated unpublished init; params/default/named-argument order; tuples/constant indexing; optional/coalescing forms; switch transfers; eager async versus lazy `[Iterator]`; awaited structured close. Do not ask these again. |
| Explicitly deferred | Declaration `static require`/`static ensure`; entry snapshots/history; nested nominal types; interface constructors; source type packs and zero/one-arity tuple extensions; collection comprehensions/repetition pending deeper library/tensor design. They are not pending votes in this inventory. |
| Implementation work | Parser/binder/verifier/native lowering, compiler-component parity, lossless recovery and diagnostics, regression fixtures and support-version migration. Approval does not claim any of these are complete. |
| Metadata/ABI work | Wire rows, witness/capture representation, dispatch tables, compatibility encoding, exception transport, public tuple/callable ABI, layouts and resource limits. These must carry chosen source contracts, not choose them. Existing reserved fields remain generically reserved. |
| Library API work | Iterator/awaiter/close operation identities, scheduler/executor/cancellation APIs, task ownership surfaces, text/collection handler registration APIs, primitive comparer names, allocator and tensor APIs. Review in the [core-library proposal](core-library-reference.md), with source dependencies above where needed. |
| Proof/tooling work | Exact portable proof rules/budgets remain in the [proof proposal](static-proof-and-constant-evaluation.md). XML schema/member IDs, package layout and optional documentation tests follow the approved attachment/reference decisions; they are not extra language features. |

## Official comparison sources

These sources describe C#, not Terrum. Feature references may document a newer
C# release than the standardized chapter; their differing scopes are explicit.

- [C# lexical structure](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure): identifiers, keywords, literals and token formation.
- [C# using directives](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/using-directive): namespace-local imports, concrete aliases and alias qualification.
- [C# namespaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/namespaces): namespace declaration spaces and nesting.
- [C# classes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes): generic formals, members, constants, constructors and partial declarations.
- [C# expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions): inference, overload resolution, conversions and conditional logical operators.
- [C# interfaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces): nominal conformance and most-specific inherited implementation.
- [C# operator overloading](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading): C# 14 instance compound operators and the distinct older derived-compound rule.
- [C# exception handling](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements): filter selection.
- [.NET user-filtered handlers](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/using-user-filtered-exception-handlers): explicitly specifies discarded filter exceptions and a false filter result for C#/VB.
- [C# attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/attributes): attribute identity, placement and arguments.
- [.NET member packing](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack): `StructLayoutAttribute.Pack` and its member/derived type alignment limit, not Terrum aggregate over-alignment.
- [C# documentation comments](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments): XML documentation and member references, not Terrum's GFM source model.
