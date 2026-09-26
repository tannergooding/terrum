# Compiler and tooling implementation

## Current boundary: syntax-9 and native-10/native-11/native-12/native-13

`Terrum.CodeAnalysis` is a reusable C# compiler library. `terrumc` exercises
that library; it is not an independent parser or binder. Syntax snapshots remain
immutable and usable independently. The separate [native profiles](native.md)
binds a bounded scalar/compact-value language and emits Windows x64 executables through
verified structured operations. A clean syntax result is deliberately not a
valid-program claim: `var x = 1;` parses but semantic analysis rejects its missing
numeric context. General ownership/lifetime checking, general semantic IL/LMOD,
managed graphs and standard-library execution are not implemented.

| Surface | Implemented | Not yet implemented |
|---|---|---|
| Text | Immutable UTF-16 source, half-open ranges, line mapping, exact malformed-editor-input preservation | Rope storage, byte maps, generated-source maps |
| Lexing | Pinned Unicode identifiers, ASCII numbers/tags, comments, leading trivia, strings, diagnostics, EOF | Interpolation, raw/character/native literals, directives |
| Declarations | Using, file-scoped namespaces, functions, compact structs and primitive-field `unique struct` with explicit fields/constructors/methods, checked `[NoThrow]`/`[DoesNotReturn]` and unique `[Cleanup]`; library binding and validated references, including combined leaf-producer [module sets](module-sets.md) | Classes, interfaces, enums/unions, owning fields, general attributes, generic declarations/constraints, general module dependency graphs |
| Types | Qualified named types, nested type arguments, one `?` per type nesting level, `void`; repeated `?` diagnoses | General type resolution, optional normalization after substitution, family defaults/conditional conformance, ownership/form qualifiers |
| Statements | Initialized typed/inferred locals, `mut`, blocks, `if`/`else`, `while`, scoped `for`, return, `require`, structured `try`/`finally`, unsafe checked `try`/`catch` (optionally with `finally`), assignment and expression statements | `foreach`, break/continue, switch/patterns, general exception objects/throw, async, compound assignment |
| Expressions | Names, raw literals, `()`, grouping, unary/binary operations, checked numeric casts, member/index access, calls, `new`, named `move` of unique locals or parameters, by-value unique arguments, conditional unique `bool`/`out` results, readonly unique receiver loans, named/ref/out argument syntax | Character literals, owning fields and general expression temporaries, stored loans, evaluation outside the native profile, user-defined conversions, overloads, generic calls, conditional expressions, constructor generic inference |
| Tooling | Traversal, lossless reconstruction, deterministic content IDs, checked edit batches, structured CLI schema | Semantic queries, rename, formatter, generators/analyzer loading, LSP, incremental reuse |

Only the grammar below is supported. Unsupported constructs diagnose rather than
being ignored. Parser recovery is useful for partial files, but not a promise to
recover every later declaration optimally.

```text
file        = using* namespace? declaration* EOF
using       = "using" qualifiedName ";"
namespace   = "namespace" qualifiedName ";"
declaration = attributes modifiers (function | struct)
attributes  = ("[" name "]")*
modifiers   = ("public" | "internal" | "private" | "static" | "mut" | "unique")*
function    = type name parameters block
struct      = "struct" name "{" member* "}"
member      = attributes modifiers (field | constructor | function)
field       = type name ("=" expression)? ";"
constructor = containingTypeName parameters block
parameters  = "(" (mode? type name ("," mode? type name)*)? ")"
type        = (qualifiedName | "void") ("<" type ("," type)* ">")? "?"?
mode        = "ref" | "mut" "ref" | "out"
local       = localInitializer ";"
localInitializer = "mut"? ("var" | type) name "=" expression
statement   = block | local | if | while | for | tryStatement | unsafeStatement | return | require | assignment | expression ";" | ";"
if          = "if" "(" expression ")" block ("else" (block | if))?
while       = "while" "(" expression ")" block
for         = "for" "(" initializer? ";" expression? ";" iterator? ")" block
initializer = localInitializer | assignmentClause | expression
iterator    = assignmentClause | expression
tryStatement = "try" block ("catch" block ("finally" block)? | "finally" block)
unsafeStatement = "unsafe" block
return      = "return" expression? ";"
require     = "require" expression ";"
assignment  = assignmentClause ";"
assignmentClause = expression "=" expression
```

Assignment is a statement, not an expression. Native places include initialized
mutable locals, accessible fields of mutable compact values, constructor fields,
conditional output scratch slots and checked writable host-buffer elements.
Unique local places additionally track vacancy and cleanup obligations.
General borrowed references and owning fields/parameters remain unsupported.
For-loop initialization runs once, followed by condition, body and iterator;
the iterator does not run after a returning body. An omitted condition is true.
Initializer locals remain in scope through the header and body, not afterward.
The iterator cannot reference body locals. Multiple comma-separated clauses,
increment/compound-assignment operators and break/continue are not implemented.
The binder lowers the scoped loop to existing verified blocks and `while`
operations; there is no separate machine-code or module-IR loop variant.
Braces are required on control bodies. Supported precedence, strongest
first: postfix; unary `+ - ! ~ move`; `* / %`; `+ -`; `< <= > >=`; `== !=`; `&`; `^`;
`|`; `&&`; `||`. Binary operators associate left. Lexing does not fuse angle
brackets into shift tokens. Generic-call/comparison ambiguities are not solved by
guessing semantic types in the lexer. See [lexical grammar](lexical-grammar.md)
for precise number, string, identifier and newline rules.

`TRM1005` marks each extra optional modifier in `int32??`, `T??`, or
`T? /* trivia */ ?`. Recovery retains every modifier and continues parsing.
`List<int32?>?` and `T?` remain valid syntax; determining that `T` is already
optional and normalizing its substituted type is future binder work.
Explicit `Option<Option<T>>` syntax is unchanged. `syntax-1` introduced this
rejection rule; `syntax-2` added the lossless `RequireStatement` node.
`syntax-7` includes scoped for-loops, `try`/`finally`, parameterless declaration attributes and
checked casts for supported 32- and 64-bit integer types and `char16`.
`syntax-8` adds `unsafe` blocks and the parameterless checked-policy `catch`,
with optional `finally`; a try requires at least one handler or cleanup clause.
`syntax-9` adds `unique struct` declarations and `move` expressions.
`[NoThrow]` on functions/constructors, `[DoesNotReturn]` on void functions and
`[Cleanup]` on eligible unique instance methods are currently bound;
unknown, duplicate and misplaced attributes diagnose rather
than becoming unchecked metadata. Attribute nodes retain their brackets and
name as three children. The `native-10` (fail-fast), `native-11`
(checked-exceptions) and `native-12` (compact unique owner) profiles support checked
signed/unsigned 32- and 64-bit integers, raw code units, conditional outputs,
fixed host-buffer capabilities and acyclic value layouts up to 16 bytes.
Fields are read-only after construction unless explicitly declared `mut`.
Binding and mandatory IR verification check mutation authority at every
nested field, independently of mutable local bindings and visibility.
Transitive module contracts and Windows x64 indirect argument/result storage
preserve those layouts. Statically rooted writable field paths enforce
initialization and visibility at every level. Larger aggregates, general
borrowed references and mutation through returned temporary values still
diagnose before emission.
`BoundFunction.IsNoThrow` is an explicit declaration guarantee checked by the
mandatory verifier, not inferred from a body for consumers. `TRM3017` locates
an operation whose throwing effect violates that bound. The bounded proof
accepts comparisons, bitwise operations, fields, total numeric conversions
and calls to other declared NoThrow functions. A host-buffer access is
NoThrow only with a statically constant index proven within that exact
compilation grant. It conservatively rejects checked arithmetic, potentially
failing conversions and general buffer indexes without range or invariant inference.
The [native contract](native.md) describes
these limits. `BoundFunction.DoesNotReturn` separately removes a call's normal
continuation. Its implementation must have no reachable return or fallthrough
(`TRM3018`). Constant-true loops, statically false requirements and declared
nonreturning calls can establish that proof. Unreachable syntax and IR still
undergo type and initialization checks; an unreachable return is not evidence
of normal completion. `BoundTryFinally` retains both regions, verifies NoThrow
cleanup independently of the function's own effect, and runs it on normal
fallthrough and returns, inside-out. `TRM3019` rejects returns inside cleanup;
`TRM3020` conservatively rejects cleanup loops that may execute and reachable
`DoesNotReturn` calls. This is not a termination proof for other NoThrow
callees. Return values are
snapshotted before cleanup; bool/out publication is checked after all cleanup.
Fail-fast checked failures do not unwind or run cleanup. Catch and other
effect annotations remain unsupported.
Source return diagnostics use that same mandatory continuation analysis,
including boolean negation and short-circuit constant feasibility; the binder
does not maintain a second, narrower return-path proof.
Existing single-source compiler JSON remains schema 1;
scanner and multi-source diagnostic schemas are versioned separately below.

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

`SyntaxTree.Parse(LexResult)` accepts a completed lexical snapshot, including
one produced by the compiled Terrum scanner. It validates the exact source
partition, token/trivia text, EOF and diagnostic shape before invoking the same
parser; it does not invoke the host lexer. The supplied component remains
responsible for correct classification. Normal source-based parsing uses its
own trusted lexer output without repeating this boundary validation.

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
dotnet run --project sources\terrumc -- analyze samples\native\factorial.terrum --json
dotnet run --project sources\terrumc -- build samples\native\factorial.terrum -o artifacts\factorial.exe --runtime fail-fast --json
```

Use the built executable or `dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll ...`
when stdout must contain only protocol output; `dotnet run` may print build
messages. `-` reads stdin. Commands exit 0 for no relevant errors, 1 for source
or compilation diagnostics, and 2 for usage/input/output failures. `tokens` performs only lexical
analysis. Human diagnostics go to stderr; JSON writes one object to stdout,
including failures. These inspection/build commands do not execute source.
`jit`, `scan-component` and `run-module` are explicit execution workers;
clients must supervise them with deadlines. JIT/module execution returns the
program's raw status, not the compilation-command status convention.
`run-native` validates its persisted consumer/producer packages and supervises
a separate native child with a 30-second deadline. It likewise returns the
program's raw status; a deadline failure reports `TRM5004`.

Schema 1 publishes `languageVersion`, coordinate system, path, length, success
and diagnostics. Parsed commands also include the content ID. Diagnostics have
stable codes, severity, message and source span/range; usage/input diagnostics
have no fictitious source position. Tokens include exact text, full/ordinary
spans, missing state and structured trivia. Tree output uses a flat node table,
root index and child references to node/token indices. This avoids JSON depth
limits on long expression chains and permits targeted consumers to index nodes
without recursively decoding source-sized nested objects.

Messages are human-facing and not stable dispatch keys. `TRM0xxx` denotes lexical
errors, `TRM1xxx` parsing, `TRM2xxx` CLI I/O/usage, `TRM3xxx` semantic
analysis, `TRM4xxx` native emission/runtime-host failures, and `TRM5xxx` bounded
module/scanner interchange errors. Schema changes require
versioning. Clients must reject unknown versions or explicitly negotiate them,
not infer meaning from enum ordinals or diagnostic English.

## Architecture evolution

The next front-end boundary is an immutable `Compilation` over source snapshots,
parse options, module/reference identities and expanded public contracts. A
semantic model resolves names/types/places and exposes structured queries:
symbol at range, required/proven obligations, call target, initialization and
escape facts, and why a diagnostic applies. Syntax is usable without binding.
The initial `Semantics.Compilation` already binds one syntax snapshot to
source-mapped, typed `BoundProgram` operations or diagnostics, never a partially
valid executable. It is not yet the multi-module semantic query service.
The multi-source overload declares all files before binding bodies, rejects
ambiguous entry points, and exposes `SyntaxTrees` and source-associated
`SourceDiagnostics`. Consumers must not interpret every multi-source diagnostic
against the legacy first `SyntaxTree` property.
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
validated structured Terrum operations -> target lowering. The semantic layer
must represent values versus places, copy/move/borrow, initialization and
conditional publication, blocks/edges/block arguments, exceptional cleanup,
regions, loops and suspension. Mandatory validation checks terminators,
dominance, joins, region entry, cleanup and lifetime contracts. Irreducible flow
is represented honestly, not disguised as one natural loop. Cached analyses
are optional/versioned; validating structure is not optional.

LMOD is the future independently versioned Terrum module/contract artifact, not
a renamed CLI assembly or a PE file. The current backend emits Windows x64
machine instructions and PE32+ images directly, with no CIL/LLVM translation
or linker dependency. Source functions use a saved RBP frame base for
locals and spills, separate from the RSP-relative outgoing call area. Shared
PE/JIT unwind metadata describes both the saved register and allocation.
Cleanup has out-of-line funclets that borrow the source frame in RDX
while reserving their own call areas; they are invoked explicitly on normal
exits and by Windows on checked-policy exceptional exits. Versioned scope
records and filters admit only four compiler-generated checked failures.
`BoundProgram` retains structured blocks, branches,
loops, slot reads/writes and checked expressions; its verifier precedes
machine lowering. It is not a claim to implement the complete semantic IL.
Target-specific encodings, import fixups and unwind records live under `Native`,
not in binding or the public lossless syntax model.

An optional CIL/reference backend would need a concrete validation/deployment
benefit; it is not a prerequisite for native progress. The [JIT proof](jit.md)
reuses the encoder with W^X memory and dynamic unwind registration.
[Semantic modules](modules.md) persist verified bodies and normalized contracts
for bounded process-start replacement. Consumers bind against imported declarations
without producer source; private implementation layouts and helpers can evolve
without changing the persisted consumer. Native `.trmn` packages additionally
persist the EXE and DLL images, validate their contracts before launch, and
support compatible single-provider replacement without regenerating consumer
machine code. This is process-start servicing, not the general LMOD design or
hot replacement. Unsupported runtime/ABI capabilities diagnose rather than
inherit accidental CLR behavior.
Multi-source compilations are available through the API and repeatable
`--source` options on `analyze`, `build`, `build-library`, `jit`, `compile-module`
and `compile-native`.
Repeatable producer `--export` options select the module public surface.
Multi-source compiler JSON uses schema 2 with input-ordered snapshot identities,
diagnostic source indexes and, for successful builds, the native artifact.
Single-source and module-artifact response schemas remain unchanged.

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
