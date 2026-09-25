# Bounded semantic modules

The version 4 `.trm` format records the semantic profile and persists verified, typed, source-mapped structured IR,
not source text or a native DLL. `CreateProducer` stores selected function bodies
and exported contracts. `CreateConsumer` stores its own function bodies and the
reference producer's declaration contracts, **not** producer bodies.
`CreateReference` validates a producer artifact and exposes exported signatures
and their value layouts to `Compilation.Create`. Consumer source needs neither
producer source nor placeholder implementations. Source is bound once while
creating each artifact; linking reads only the two artifacts.

The process-start semantic-module JIT loader, `run-module`, reads the **same
persisted consumer artifact** and the selected separately persisted producer
artifact for every launch. `ModuleCompiler.Link` checks imported signatures
(including conditional `out` parameter modes), layouts and symbolic call
targets, reconstructs typed IR and runs the semantic verifier; only then does
`NativeJit.Execute` lower the linked program to native code **in memory**.
Consumer source is not read or rebound, producer reference bodies were not
stored in the consumer, and neither module artifact is rewritten. Changing
the compatible producer changes the next process launch's behavior. Changing
a contract fails before native execution. This is *load-time semantic-module
replacement*, not a patch to already running code or native DLL live servicing.
The CLI regression deletes both source files after compiling the artifacts,
then launches the worker with producer v1 and v2 and compares the consumer's
bytes before and after both executions.

`link-module` is separate: it emits a **new static PE** from the two artifacts.
The previously emitted PE remains bound to its original producer implementation
and is **not** replaceable merely by changing the producer artifact. Static
PE links demonstrate AOT emission from persisted IR, not replacement of an
unchanged native executable. The loader regenerates native machine code at
each start, but never recompiles or mutates the consumer *artifact*.
Precompiled native consumer code with runtime import slots could avoid this
load-time lowering; that ABI, its relocations and unwind registration are not
implemented by the bounded module profile.

Calls use symbolic function identities rather than binder-local indexes.
Types use declaration names, visibility and ordered field layouts as their contract;
public import keys include declaring-type and signature-type visibility, while
the complete ordered layout is checked separately before execution.
Compatible reordered type declarations are remapped to the consumer's local
type indexes before verification. Member contracts retain declaring type,
receiver/static mode and constructor identity; unexported producer dependencies
are kept solely in the producer artifact. A consumer call into a private
producer implementation is rejected even if both declarations share a
declaring type name.
Only layouts referenced by retained consumer definitions and imported
declarations are stored in the consumer. Imported layouts remain exact,
including private fields of a public static member's owner; this is not resilient
public layout evolution. Producer-only layouts and helpers have module-local
identities at linking and may change independently. Private names can coincide
with consumer-local names without binding to the wrong implementation.
Compatible replacements may also add exports not requested by the consumer.

The binary reader rejects unknown versions, malformed/truncated records,
trailing bytes, unknown nodes, invalid graph depth, excessive symbols, strings
or input length. The artifact limit is 4 MiB, graph limit 100,000 nodes,
nesting limit 128, and function limit 1,024. No reflection-based serialization
or saved-source rebind occurs. Import contracts are declaration-derived, not
implementation hashes. This format is not authenticated: untrusted artifact
bytes are checked for safety and compatibility, not provenance.
Linked programs additionally pass the semantic verifier's 100,000-visit work
budget, including conditional-flow analysis.
Host-provided buffer capabilities are outside this module format and are
rejected at artifact creation rather than silently dropped.

Producer artifacts select explicit exported function names from a verified library.
`Compilation.CreateLibrary` verifies source definitions without requiring
`Main`; its program has `EntryPoint = -1` and cannot be emitted or executed
without linking. `compile-module producer` uses this library mode. A function
named `Main` in library source is an ordinary definition, not a process entry.
External `BoundFunction` declarations have `IsExternal = true` and an empty body,
not a synthetic implementation. The source verifier checks their signatures and
conditional-output usage; the executable verifier rejects unresolved declarations.
The module linker supplies real, verified bodies before native lowering.
For existing API callers, `CreateConsumer` also accepts an already bound combined
program and extracts selected producer definitions. The CLI uses independent
consumer source and the validated artifact-reference path.
Both library and reference compilation accept multiple `SyntaxTree` snapshots;
source diagnostics retain the originating snapshot. The self-host module test
builds `SourceRange` and `CharacterFacts` into a producer, then independently
binds `RangeTests` and an entry harness against its exported declarations.
Those persisted modules execute through AOT and JIT, including range boundaries,
conditional-output failure, character classification and repeated calls.
The CLI accepts repeatable `--source <path>` options and, for producers,
`--export <qualified-name>` options. It rejects duplicate source paths or exports,
more than 256 sources or 1,024 exports, and more than 4 MiB of combined UTF-8
source. Each input remains a separate syntax snapshot; no source concatenation
changes namespace scope or locations.

From the repository root after building, compile the reference source and
replacement source independently, then persist each artifact:

```powershell
$cli = 'artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll'
dotnet $cli compile-module producer samples\native\modules\reference.terrum Helper -o artifacts\producer-v1.trm
dotnet $cli compile-module consumer samples\native\modules\consumer.terrum artifacts\producer-v1.trm -o artifacts\consumer.trm
dotnet $cli compile-module producer samples\native\modules\replacement.terrum Helper -o artifacts\producer-v2.trm
dotnet $cli run-module artifacts\consumer.trm artifacts\producer-v1.trm --runtime fail-fast # exits 11
dotnet $cli run-module artifacts\consumer.trm artifacts\producer-v2.trm --runtime fail-fast # exits 22
dotnet $cli link-module artifacts\consumer.trm artifacts\producer-v2.trm -o artifacts\linked.exe --runtime fail-fast
```

The actual compiler candidates can be built separately from their tests:

```powershell
dotnet $cli compile-module producer self-host\SourceRange.terrum Terrum.CodeAnalysis.SourceRange.TryCreate --source self-host\CharacterFacts.terrum --export Terrum.CodeAnalysis.SourceRange.GetEnd --export Terrum.CodeAnalysis.IsAsciiDigit --export Terrum.CodeAnalysis.IsLineBreak --export Terrum.CodeAnalysis.GetDecimalDigit -o artifacts\compiler-values.trm
dotnet $cli compile-module consumer self-host\RangeTests.terrum artifacts\compiler-values.trm --source samples\native\modules\range-entry.terrum -o artifacts\range-tests.trm
dotnet $cli run-module artifacts\range-tests.trm artifacts\compiler-values.trm --runtime fail-fast # exits 0
```

`Token` also compiles separately from its tests. Its nested `SourceRange` makes
the value twelve bytes, exercising indirect arguments and results rather than
the scalar-register-only profile:

```powershell
dotnet $cli compile-module producer self-host\SourceRange.terrum Terrum.CodeAnalysis.SourceRange.TryCreate --source self-host\Token.terrum --export Terrum.CodeAnalysis.SourceRange.GetEnd --export Terrum.CodeAnalysis.Token.#ctor -o artifacts\token-values.trm
dotnet $cli compile-module consumer self-host\TokenTests.terrum artifacts\token-values.trm --source samples\native\modules\token-entry.terrum -o artifacts\token-tests.trm
dotnet $cli run-module artifacts\token-tests.trm artifacts\token-values.trm --runtime fail-fast # exits 0
```

`samples/native/modules/incompatible.terrum` changes `Helper` to return `bool`.
Its producer artifact can be compiled, but loading it with this consumer reports
`TRM5002` before execution. The two compatible runs above use identical consumer
bytes; no consumer source is required at load time.

Each command accepts an optional trailing `--json`. Usage, artifact and
contract errors then use schema 1 `diagnostics` on stdout. Invalid single-source
compilations retain the source-mapped schema 1 response. Multi-source compilation
failures use schema 2: `sources` records each path, content ID and UTF-16 length
in input order; each `diagnostics` entry includes a zero-based `sourceIndex`
alongside the existing code, severity, message, span and line/character range.
Human-readable source diagnostics include the owning path and location. Successful
`compile-module` and `link-module` commands report the written artifact's
path, kind, size and SHA-256. Successful `run-module` still returns the
program's raw exit code with no JSON envelope, since fail-fast execution can
terminate the worker. `link-module` requires an explicit `--runtime fail-fast`;
it writes only after compatibility checks and emission succeed, leaving an
existing destination unchanged on failure.

The `run-module` command is an explicitly in-process fail-fast JIT operation: a checked
runtime failure terminates the *host process*. Use a child process for
untrusted input. Compilation and linking keep inputs distinct from their
output paths; module source and each artifact are limited to 4 MiB.

Private and internal methods cannot be exported or imported. Functions transitively
called by an exported producer function, including public helpers not explicitly
exported, are stored as producer-owned implementation only,
omitted from the consumer body table, and resolved in the producer's own symbol table;
the module linker rejects consumer calls into those bodies even if a forged
symbolic target has the same declaring type. Field visibility is part of the
serialized layout contract. Imported members must belong to public structs;
structs crossing an imported value signature must expose only public fields.
Module exports require explicit `public`: unmodified top-level functions and
structs default to `internal`, while unmodified members and constructors default
to `private`. The consumer-owned `Main` need not be exported.
Internal structs and private/internal fields may remain within a producer's
implementation but are not grants to the consumer. Top-level `private`
structs and `mut` struct-field declarations are rejected at source binding
rather than silently dropping their modifiers; field writes to mutable
*local values* do not change the declared field layout.

The proof supports one producer reference per consumer, compact acyclic value
layouts and the existing native value profile. Nested field types are retained
transitively and remapped across artifact-local type indexes. Every imported
nested layout participates in replacement checking, including field types,
order and access. Versions 2 and 3 are rejected;
recreate them from source. Multi-producer identity/version resolution, dependency
cycles, resilient public layouts and native DLL import slots remain later work.
