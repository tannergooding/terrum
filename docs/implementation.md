# Compiler and tooling implementation

## Current boundary: syntax-1

`Terrum.CodeAnalysis` is a reusable, immutable C# front end. `terrumc` exercises
that library; it is not an independent parser. There is no Terrum execution,
binding, type/lifetime checker, semantic IL, CLI IL emitter, runtime or working
standard library yet. A clean syntax result is deliberately not a valid-program
claim: `var x = 1;` parses even though the language requires a numeric target or
tag. API sketches and broad samples are design inputs, not successful compiler
fixtures.

| Surface | Implemented | Not yet implemented |
|---|---|---|
| Text | Immutable UTF-16 source, half-open ranges, line mapping, exact malformed-editor-input preservation | Rope storage, byte maps, generated-source maps |
| Lexing | Pinned Unicode identifiers, ASCII numbers/tags, comments, leading trivia, strings, diagnostics, EOF | Interpolation, raw/character/native literals, directives |
| Declarations | Using, optional file-scoped namespace, functions, structs with explicit fields/constructors/methods | Classes, interfaces, enums/unions, attributes, generic declarations/constraints, imports across modules |
| Types | Qualified named types, nested type arguments, one `?` per type nesting level, `void`; repeated `?` diagnoses | Type resolution, optional normalization after substitution, family defaults/conditional conformance, ownership/form qualifiers |
| Statements | Initialized typed/inferred locals, `mut`, blocks, `if`/`else`, `while`, return, assignment and expression statements | `require`, `for`/`foreach`, switch/patterns, exceptions, cleanup, async, compound assignment |
| Expressions | Names, raw literals, `()`, grouping, unary/binary operations, member/index access, calls, `new`, named/ref/out arguments | Cast syntax/binding, character literals, evaluation, conversions, overloads, generic calls, conditional expressions, constructor generic inference |
| Tooling | Traversal, lossless reconstruction, deterministic content IDs, checked edit batches, structured CLI schema | Semantic queries, rename, formatter, generators/analyzer loading, LSP, incremental reuse |

Only the grammar below is supported. Unsupported constructs diagnose rather than
being ignored. Parser recovery is useful for partial files, but not a promise to
recover every later declaration optimally.

```text
file        = using* namespace? declaration* EOF
using       = "using" qualifiedName ";"
namespace   = "namespace" qualifiedName ";"
declaration = modifiers (function | struct)
function    = type name parameters block
struct      = "struct" name "{" (field | constructor | function)* "}"
field       = modifiers type name ("=" expression)? ";"
constructor = modifiers containingTypeName parameters block
parameters  = "(" (mode? type name ("," mode? type name)*)? ")"
type        = (qualifiedName | "void") ("<" type ("," type)* ">")? "?"?
mode        = "ref" | "mut" "ref" | "out"
local       = "mut"? ("var" | type) name "=" expression ";"
statement   = block | local | if | while | return | assignment | expression ";" | ";"
if          = "if" "(" expression ")" block ("else" (block | if))?
while       = "while" "(" expression ")" block
return      = "return" expression? ";"
assignment  = expression "=" expression ";"
```

Assignment is a statement, not an expression; place validity is a future binding
check. Braces are required on control bodies. Supported precedence, strongest
first: postfix; unary `+ - ! ~`; `* / %`; `+ -`; `< <= > >=`; `== !=`; `&`; `^`;
`|`; `&&`; `||`. Binary operators associate left. Lexing does not fuse angle
brackets into shift tokens. Generic-call/comparison ambiguities are not solved by
guessing semantic types in the lexer. See [lexical grammar](lexical-grammar.md)
for precise number, string, identifier and newline rules.

`TRM1005` marks each extra optional modifier in `int32??`, `T??`, or
`T? /* trivia */ ?`. Recovery retains every modifier and continues parsing.
`List<int32?>?` and `T?` remain valid syntax; determining that `T` is already
optional and normalizing its substituted type is future binder work.
Explicit `Option<Option<T>>` syntax is unchanged. `syntax-1` publishes this
rejection rule; the JSON schema remains 1 because its data shape is unchanged.

## Public syntax API

Reference `sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj`. Public APIs are
currently experimental; tools should pin the compiler and check schema/language
versions. No `InternalsVisibleTo` is used by tests.

```csharp
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

SyntaxTree tree = SyntaxTree.Parse(
    SourceText.From("void Main() { return; }", "main.terrum"));

foreach (SyntaxNode node in tree.Root.DescendantNodes())
{
    Console.WriteLine($"{node.Kind}: {node.Span.Start}..{node.Span.End}");
}

string original = tree.Root.ToFullString();
SyntaxTree edited = tree.WithChanges(tree.ContentId,
    [new TextChange(new TextSpan(5, 4), "Run")]);
```

`SourceText` owns exact input text and a diagnostic path label. All library
offsets, line characters and lengths are UTF-16 units, not scalar/grapheme/display
columns; ranges are half-open. Lines and characters are zero-based. Only human
CLI diagnostics convert to one-based positions. Invalid UTF-16 is retained and
diagnosed by the library; file/stdin decoding is strict UTF-8 and rejects invalid
bytes before parsing. A UTF-8 BOM is transport metadata, not a source character.

`SyntaxTree` owns source, lexical tokens, root and diagnostics. Nodes/tokens are
immutable; trees hold no mutable global tables, symbol state or parent pointers.
`SyntaxElement` distinguishes nodes from token leaves. `Children` preserves
source order including punctuation. `Span` excludes outer trivia; `FullSpan`
includes it. Trivia belongs to the following token; EOF owns trailing trivia.
Recovery introduces zero-width, empty-text missing tokens or explicit skipped
nodes, never discards input. `ToFullString` reconstructs from leaves rather than
returning the original string. `Tokens` is the lexical stream; the root traversal
also includes parser-inserted tokens.

This compact kind-based CST avoids two divergent parser models. It lacks Roslyn's
typed convenience wrappers and structural reuse today. Add generated typed
accessors over the same slots as grammar stabilizes, not a separate lossy AST
for AI clients. Syntax nodes are owned by one snapshot; references/JSON indices
are not stable symbol identities.

`WithChanges` takes an expected `ContentId` and ordered, nonoverlapping original
source ranges. It rejects stale content, duplicate-position insertions, overlap
and out-of-range edits before exposing a result. It returns a full reparse and
does not mutate a workspace or write a file. Content IDs hash exact UTF-16LE
code units using SHA-256, including unpaired surrogates; they exclude the path
and are not project/semantic identities or revision counters. A future workspace
must also compare document identity and its own monotonically increasing
revision before atomically publishing edits. The current API prevents applying
old offsets to different content; it does not resolve concurrent edits.

Cancellation is checked during lexing/parsing and edit application. Recursive
grammar nesting is limited to 128 active parser frames, with `TRM1004` on excess;
this counts implementation frames rather than language blocks. On this failure
the root retains all tokens in one skipped node. Long left-associated trees and
public traversal use iteration. Source length remains bounded by .NET strings
and signed 32-bit ranges. No performance claims or unmeasured latency budgets
are attached to this first implementation.

## Human and machine protocol

```powershell
dotnet run --project sources\terrumc -- check samples\syntax\hello.terrum
dotnet run --project sources\terrumc -- tokens samples\syntax\control-flow.terrum --json
dotnet run --project sources\terrumc -- tree samples\syntax\hello.terrum --json
```

Use the built executable or `dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll ...`
when stdout must contain only protocol output; `dotnet run` may print build
messages. `-` reads stdin. Commands exit 0 for no relevant errors, 1 for lexical
or syntax errors, and 2 for usage/input failures. `tokens` performs only lexical
analysis. Human diagnostics go to stderr; JSON writes one object to stdout,
including failures. No source is executed.

Schema 1 publishes `languageVersion`, coordinate system, path, length, success
and diagnostics. Parsed commands also include the content ID. Diagnostics have
stable codes, severity, message and source span/range; usage/input diagnostics
have no fictitious source position. Tokens include exact text, full/ordinary
spans, missing state and structured trivia. Tree output uses a flat node table,
root index and child references to node/token indices. This avoids JSON depth
limits on long expression chains and permits targeted consumers to index nodes
without recursively decoding source-sized nested objects.

Messages are human-facing and not stable dispatch keys. `TRM0xxx` denotes lexical
errors, `TRM1xxx` parsing and `TRM2xxx` CLI input/usage. Schema changes require
versioning. Clients must reject unknown versions or explicitly negotiate them,
not infer meaning from enum ordinals or diagnostic English.

## Architecture evolution

The next front-end boundary is an immutable `Compilation` over source snapshots,
parse options, module/reference identities and expanded public contracts. A
semantic model resolves names/types/places and exposes structured queries:
symbol at range, required/proven obligations, call target, initialization and
escape facts, and why a diagnostic applies. Syntax is usable without binding.
Partial/error semantics must distinguish unknown, invalid and proven states;
absence of a diagnostic is not a safety proof.

Human/IDE and AI tools should consume the same model. A useful analysis response
includes snapshot identity, target/options, dependency identities, fact
provenance and related ranges, not merely generated explanatory prose. Edits and
refactorings return candidate edit batches and checked preconditions; callers
choose whether to publish. Semantic edits must recheck affected contracts and
diagnostics on the resulting compilation. Source comments or generated text do
not grant execution, filesystem, network or private-member privileges.

Generated source needs a stable document identity, producer/version/input
digest, authorization and a map from generated ranges to zero/one/multiple
original ranges. Preserve spelling and expansion locations separately rather
than forcing a lossy single-range mapping. Diagnostics may carry related
locations, rule arguments and suggested fixes tied to the same snapshot.
None of this source-map/semantic service is implemented yet.

Analyzer and generator APIs will be versioned consumers of snapshots, not
callbacks allowed to mutate compiler internals. Pin dependencies and input
manifests; isolate execution with explicit capabilities, cancellation and
resource bounds. Author-approved generation may access declared implementation
scope; consumer analyzers do not inherit those rights. Deterministic generation
requires explicit inputs, not hidden clock/environment/network reads. An
in-process interface alone would not be a security boundary.

Delay plugin discovery, persistent caches, remote services and query scheduling
until actual usage supplies requirements. Content identity already enables
safe exchanges; a later query cache additionally needs compiler/schema versions,
target, references, contract-rule version and authorization context. Changes in
serviced library bodies must not silently change a declaration-derived public
lifetime contract or a source program's validity.

## Binding and backend path

Keep diagnostics and source mapping through syntax -> bound operations ->
validated structured Terrum semantic IL -> CLI lowering. The semantic layer
must represent values versus places, copy/move/borrow, initialization and
conditional publication, blocks/edges/block arguments, exceptional cleanup,
regions, loops and suspension. Mandatory validation checks terminators,
dominance, joins, region entry, cleanup and lifetime contracts. Irreducible flow
is represented honestly, not disguised as one natural loop. Cached analyses
are optional/versioned; validating structure is not optional.

LMOD is the future independently versioned Terrum module/contract artifact, not
a renamed CLI assembly. CLI IL is the initial executable lowering. Start with
small verified functions and a JIT/AOT agreement corpus; unsupported runtime
capabilities must diagnose. Native AOT's managed dynamic-loading constraints
are bootstrap limitations, not a change to Terrum's eventual separate-library
semantics. An owned backend/runtime comes later, not LLVM by default.

## Design references and tradeoffs

These are specific prior art, not claims that another compiler lacks AI support
or that Terrum has already improved its performance or extensibility.

| Reference | Applied lesson and deliberate limit |
|---|---|
| [Roslyn syntax model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-syntax) | Immutable full-fidelity nodes/tokens/trivia and explicit recovery are the shared tool surface. Start with a compact CST, not premature red/green reuse or a parallel AI AST. |
| [Roslyn incremental generator design](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) | Declared immutable transform inputs enable caching. Defer pipeline APIs until we have binding and explicit generator authorization. |
| [rustc incremental queries](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html) | Cached results depend on actual query dependencies and their order, not just file timestamps. Don't serialize every cheap result or equate a source hash with semantic validity. |
| [Clang AST](https://clang.llvm.org/docs/IntroductionToTheClangAST.html) and [AST importer](https://clang.llvm.org/docs/InternalsManual.html#the-astimporter) | Retaining written syntax helps refactoring; semantic graphs and redeclaration/identity relationships are not interchangeable with a syntax tree. Keep our CST separate from later symbol graphs. |
| [LLVM language reference](https://llvm.org/docs/LangRef.html) | Explicit IR contracts and linkage distinctions matter. LLVM linkage is not Terrum lexical visibility, and this reference does not select LLVM as the backend. |
| [Runtime type loader design](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/docs/design/coreclr/botr/type-loader.md) | Type identity, metadata validation and loading phase affect compatibility and failure timing. Terrum contracts need an explicit binder/publication boundary instead of accidental JIT-time discovery. |

References were consulted on 2026-09-24; living design pages can change.
