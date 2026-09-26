# Bounded semantic and native modules

The version 13 `.trm` format records the selected native runtime policy and
persists verified, typed, source-mapped structured IR,
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
The native-package path below instead persists both machine images and uses
Windows DLL import slots, avoiding load-time lowering. The semantic-module JIT
path does not yet bind precompiled native imports.

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
Nested writes persist their root slot and field-index path, not an unchecked
byte offset or pointer. The linker remaps value identities and verifies every
path against the selected layouts before native address calculation.

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
`Main`; its program has `EntryPoint = -1` and cannot be emitted as an executable
without linking an entry point. Native library emission instead produces an
entryless DLL with explicit exports. `compile-module producer` uses library mode. A function
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

The wide-integer helpers exercise full-range signed and unsigned values across
the same independent module boundary:

```powershell
dotnet $cli compile-module producer self-host\WideIntegerFacts.terrum Terrum.CodeAnalysis.Numerics64.TryAdd --export Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal -o artifacts\wide-values.trm
dotnet $cli compile-module consumer self-host\WideIntegerTests.terrum artifacts\wide-values.trm --source samples\native\modules\wide-entry.terrum -o artifacts\wide-tests.trm
dotnet $cli run-module artifacts\wide-tests.trm artifacts\wide-values.trm --runtime fail-fast # exits 0
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
program's raw exit code with no JSON envelope, since a checked failure can
terminate the worker. `link-module` requires an explicit matching
`--runtime fail-fast` or `--runtime checked-exceptions`;
it writes only after compatibility checks and emission succeed, leaving an
existing destination unchanged on failure.

The `run-module` command is an explicitly selected-policy in-process JIT
operation: an unhandled failure terminates the *host process*. Use a child
process for untrusted input. Compilation and linking keep inputs distinct from their
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
structs are rejected at source binding. Field writes require declared `mut`
authority along the complete path, except a constructor's own fields during
construction; mutable local storage alone does not upgrade a readonly field.

The original semantic/native pair APIs support one producer reference per
consumer. [Native module sets](module-sets.md) additionally bind two to sixteen
independent leaf producers and persist one consumer EXE that imports their DLLs;
either provider can be compatibly replaced without re-emitting that consumer.
Both paths use compact acyclic value layouts and the existing native value
profile. Nested field types are retained
transitively and remapped across artifact-local type indexes. Every imported
nested layout participates in replacement checking, including field types,
order, access and declared mutation authority. Version 10 retains the checked
`IsMutable` flag per field; changing it in either direction changes the
published value contract and is rejected during replacement. Function bodies
and public contracts store the checked `IsNoThrow` and `DoesNotReturn`
declarations. Either promise can be strengthened independently: MayThrow may
be replaced by NoThrow, and MayReturn by DoesNotReturn. Weakening either
promise is rejected without inferring effects from replacement bodies.
`DoesNotReturn` applies only to void functions and means normal continuation
is unreachable, including after an imported call; it does not introduce a
public never type, general exception objects or asynchronous cleanup. Producer
bodies claiming either promise must independently pass the verifier's
structured-flow or conservative effect proof. NoThrow is about permitted
operations and declared callees, not an exception handler or a claim that
fail-fast process termination counts as NoThrow. Versions 10 (fail-fast) and 11 (checked-exceptions) remain readable; earlier
versions, including version 9 without structured
cleanup regions, are rejected;
recreate them from source. Transitive dependency/version resolution, dependency
cycles and resilient public layouts remain later work.

Version 12 stores the unique layout flag, declared cleanup method, cleanup
function contract, explicit move/receiver-loan nodes and lexical drop regions.
Version 13 additionally stores ownership transfer markers on retained function
declarations, conditional output drops and vacant `out var` owner declarations.
The semantic profile is computed from retained layouts and declaration
markers: native-13 requires an owning parameter or output; older native-12
continues to describe unique locals/returns. Versions 10, 11 and 12 remain
readable with their original layouts and contracts; a native package keeps
container version 1 and its selected runtime policy is checked separately.
A consumer importing a unique type must also import its exported NoThrow
cleanup method. Its cleanup identity and Copy/unique distinction participate
in layout and signature compatibility; a replacement implementation may
change without changing the consumer artifact. Native-12 marks contracts
containing unique values under either runtime policy. Producers/consumers
without unique layouts continue to use native-10 or native-11, even when
serialized by the newer `.trm` writer, so compatible older native-10/package-1
images remain usable. Native package version 1 embeds and checks the exact
semantic profile rather than inferring it from runtime policy alone.
`ModuleCompiler.GetNativeProfileVersion` and
`NativeModuleCompiler.Inspect(...).NativeProfileVersion` expose that retained
contract profile; module CLI artifact and validation responses report it too.
Unused unique declarations removed during semantic export selection do not
upgrade the artifact profile. Package and semantic runtime policies must still
match exactly when both use native-12. Compilation and module JSON responses
report `runtimePolicy` independently of `profile`, including analysis and
validation responses without an output artifact.

Native package validation checks both allocation-only and saved-RBP unwind
records, their ordered `.pdata` ranges, `.xdata` bounds and corresponding
machine-code prologs before accepting a replacement image. Checked native-11
images additionally validate bounded scope records and handler references.
Source functions precede cleanup funclets; exports must point to source-function
entries, never to funclets or runtime helpers. The native package container is
still version 1. Earlier native-10/package-1 fail-fast images with
allocation-only source frames remain valid: the four failure helpers (plus
two consumer entry wrappers) precede the source functions in both layouts.
Validation still rejects exports to helpers or funclets and unwind ranges
shorter than their described prolog.

`NativeEmitter.EmitLibrary` can instead emit a Windows x64 PE32+ DLL directly
from a verified `Compilation.CreateLibrary` program and an explicit list of
public function names. It has a conventional named export table, no process
entry or initialization routine, and retains policy-specific native unwind and
failure imports. Scalar, compact aggregate and conditional `out` calls
use the same Windows x64 encoder as the executable. The image contains no
base-dependent addresses: its references are RVAs or RIP-relative, so ASLR
needs no base relocation entries. Export names are exact and stable per
invocation; private/internal functions may be linked into their implementation
but are not exported. Host buffer capabilities and unresolved external
declarations are not accepted. This is an independently loadable native
producer artifact, **not** by itself a checked module replacement contract.
Callers must match the selected Windows x64 signatures and value
layouts themselves; unloading invalidates function pointers.

`NativeEmitter.EmitWithImports` emits a separately persisted Windows x64
consumer executable from a verified reference-bearing program. Every external
function declaration must have exactly one explicit binding from its qualified
source identity to a DLL basename and named export. The emitted consumer has
ordinary Windows import descriptors, lookup tables and writable IAT slots;
its calls use RIP-relative IAT operands while local calls retain the shared
encoder's direct relative form. Policy-specific failure imports remain independent runtime
import. No external declaration gets a fake native body or unwind range.
The Windows loader resolves the provider DLL before the consumer begins;
placing a compatible replacement DLL at the same filename changes behavior
on the next launch without rewriting the consumer image. Ordinary `Emit`
and in-memory JIT execution still reject external declarations. These
bindings assert **names only**, not signature or value-layout compatibility,
module provenance, or safe DLL search location: the caller must ensure the
selected provider has the expected ABI contract and trusted load path before
launch. This low-level artifact API does not perform contract validation;
the package layer below adds that check. Neither provides a multi-provider
contract linker, live patching or hot-unload protocol.

`NativeModuleCompiler` packages each separately compiled native producer DLL
or consumer EXE with its corresponding version 12 semantic `.trm` contract
(or a readable version 10/11 contract). Its version 1 package records
the contract's native-10/fail-fast, native-11/checked-exceptions or
native-12/compact-unique profile,
single explicit provider identity and the exact PE and semantic bytes. The
identity is 1–64 ASCII letters, digits or underscores, beginning with a
letter; its loader basename is `terrum-<identity>.dll`. Artifacts are bounded
to 16 MiB overall and 4 MiB of semantic data. A SHA-256 checksum covers the
package header and payload for corruption detection, **not** authentication.
The consumer package stores its own image, without pinning the selected
producer's image hash or implementation; the producer package retains its
own image hash for inspection. Unknown package versions, profiles and
requirements, excessive lengths, trailing bytes or inconsistent checksums
are rejected.

`CreateReference` exposes only the producer package's verified semantic
declarations to independent consumer binding. `CreateConsumer` emits the
consumer PE once, binding every external declaration from the reference and
emitting IAT entries for calls actually present in consumer bodies, named
exactly after their declarations. At validation and launch,
`ModuleCompiler.Link` checks the persisted declaration, value layout,
conditional output, field mutability, NoThrow and DoesNotReturn promises,
and visibility contracts and verifies the linked IR. It does **not** emit
code: the native consumer image is
unchanged when a compatible producer package is selected for another
launch. The consumer's exact imported call targets come from its bounded `.trm` reader,
not from names in the replacement's linked function table; a replacement can
add exports without changing a consumer that has an unrelated local function
of the same name. Unused declarations retain semantic contracts but need no
IAT cell. Bounded PE32+ structure checks require the declared exports
and imports, x64 section and directory bounds, no CLR metadata, no unexpected
initialization or dependency, and read-only executable text. `RunAsync`
checks both packages before staging their exact persisted images under a
unique temporary directory and launching one supervised Windows x64 child.
The positive deadline is limited to two minutes; cancellation and timeout
terminate that child and remove only its staged files and empty directory.
Image deletion retries transient Windows access/sharing failures for up to one
second per file, including delayed image-section teardown after process exit;
persistent cleanup failures are reported rather than silently discarded.
The child process's raw exit code includes unhandled failure codes. These checks
assume the native images were produced and distributed by trusted parties:
the checksum does not prove source-to-code equivalence or prevent an attacker
from deliberately replacing executable bytes and recomputing it. This is
neither an untrusted-code sandbox nor a hot-servicing loader.
