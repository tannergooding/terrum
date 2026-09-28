# Bounded semantic and native modules

## Definitions and scope

A **module artifact** is a `.trm` file with the `TRRM` identifier. A **producer**
stores selected function bodies and exported contracts; a **consumer** stores
its own bodies and the imported declarations it was compiled against. A
**reference** validates a producer and exposes its exported signatures and
layouts during consumer compilation. A **runtime policy** is the persisted
`fail-fast` or `checked-exceptions` choice. A **semantic artifact revision**
versions persisted IR contracts; it is distinct from a native-profile label.

The artifact contains verified, typed, source-mapped structured IR, not source
text or a native DLL. The highest implemented semantic revision is 35, selected
by the artifact's required features; not every artifact uses revision 35.
Revision-specific reader behavior and feature-dependent writer selection
describe repository support, not an externally released binary compatibility
matrix. See [implementation](implementation.md) for the compiler support
boundary and [roadmap](roadmap.md) for scheduled format work.

## Current artifact and loading rules

`CreateProducer` stores selected function bodies
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
the complete ordered layout is checked separately before execution. The
current module format does not relax its exact layout checks. The bounded
syntax-24 transparent `Copy` wrapper is a specific v35 contract: one-field
wrappers over `bool`, `char16`, `int32`, `uint32`, `int64`, or `uint64` retain
nominal identity and field visibility/mutation rules, while storage and direct
argument/result classification match the carrier. Semantic artifact v35
persists the transparent-layout intent for private and exported structures,
and compatible replacements must preserve that intent. This does not add
implicit conversions or relax exact checks for other module layouts.
Current source binding records the selected declaration and its closed generic
arguments, not just a name. Linking or JIT loading validates and implements
that selected target; it never reruns source overload resolution against a
replacement's candidate set. Runtime virtual/interface dispatch and
devirtualization are separate mechanisms. The current bounded module format
does not yet provide general overload sets, generic-witness metadata or
compatibility forwarders.
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

## Proposed compatibility contracts

The proposed opaque/evolvable-by-default direction does not retroactively make
current module layouts resilient.

General compatibility forwarding remains scheduled work. A forwarder must
preserve the old identity and complete effects, access, lifetime and ABI
contract while naming one exact replacement or closed specialization; fuzzy
linker searches are not a compatibility policy. Source visibility of
metadata-only stubs is a separate rule. First-class build-tool compatibility
analysis is scheduled to compare declared baselines for binary breaks, source-binding risks,
witness/default conflicts, case-set evolution, and layout/access/ownership/
effect/lifetime changes. It can report declared contract differences, not prove
arbitrary implementation behavior. These facilities are not implied by the
bounded exact-contract replacement checks described above.
Published declaration preconditions and postconditions will also participate
in module compatibility: stronger preconditions and weaker postconditions break
callers. Body-local static proofs are not exported promises. These condition
contracts and their metadata are not supported by the current module format;
the existing replacement checks below remain limited to recorded contracts.

## Current compatibility checks

Existing module tests exercise those bounded checks: consumers retain their
recorded promises, weakened `NoThrow` contracts and changed signatures, output
modes, layouts or access contracts are rejected, and provider catalogs reject
missing, incompatible or multiple compatible selections. This is evidence for already-bound repository artifacts only; it does not
establish source compatibility or provide source-binding diagnostics.

The current binary reader rejects unknown versions, malformed/truncated records,
trailing bytes, unknown nodes, invalid graph depth, excessive symbols, strings
or input length. The artifact limit is 4 MiB, graph limit 100,000 nodes,
nesting limit 128, and function limit 1,024. No reflection-based serialization
or saved-source rebind occurs. Import contracts are declaration-derived, not
implementation hashes. These are current repository reader/verifier limits,
not selected limits for the pre-v1 `TRRM` specification. This format is not
authenticated: artifact
bytes are checked for structural safety and declared-contract compatibility,
not provenance. The historical-reader tests exercise repository fixtures and
do not promise support for artifacts distributed in production.
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
The [native graph format](native-graph.md) also permits producer DLLs to import
other producers. It validates a complete acyclic closure of at most sixteen
producers before execution; a compatible leaf replacement leaves both the root
EXE and intermediate DLLs unchanged. Direct dependencies are selected explicitly,
not resolved through an automatic version policy. A bounded local catalog
resolver may instead select exactly one compatible closure from explicitly
listed candidate packages. Two distinct compatible choices are ambiguous;
the resolver never ranks semantic profiles or implementation hashes. Execution
still revalidates the selected packages.
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
recreate them from source. A version-ordering policy and resilient public
layouts remain undefined; cyclic dependencies remain unsupported.

Version 12 stores the unique layout flag, declared cleanup method, cleanup
function contract, explicit move/receiver-loan nodes and lexical drop regions.
Version 13 additionally stores ownership transfer markers on retained function
declarations, conditional output drops and vacant `out var` owner declarations.
Version 14 records partial-constructor cleanup regions with actual acquisition
order and readonly owning-field places. Complete owners run their own NoThrow
cleanup before nested owning fields in reverse declaration order; constructor
failure cleans only acquired fields in reverse acquisition order. Retained
composite unique layouts select `native-14` regardless of whether a declaration
also transfers an owner. The runtime policy remains an independent artifact
property, separate from the native package container version.
The semantic profile is computed from retained layouts and declaration
markers: native-13 requires an owning parameter or output; older native-12
continues to describe unique locals/returns. Versions 10 through 13 remain
readable with their original layouts and contracts. Version 14 retains version
13's scalar wire encoding; composite requirements cannot be downgraded to an
older format or profile.
Version 15 adds the compiler-owned `OwnedInt32Buffer` identity, its canonical
intrinsic operation markers and reserved mutable-receiver loans. The resource
selects `native-15` from retained layouts, function bodies or declaration
contracts (including imports without bodies); a consumer cannot reinterpret
an imported owning buffer as a plain eight-byte integer. Native packages keep
their existing container version; the resource contract and selected runtime
policy remain in the embedded semantic profile. Versions 10 through 14 remain
readable under their original profiles.
Version 16 adds lexical `BoundViewScope`, `BoundViewLength` and `BoundViewRead`
nodes, retaining the owner place and range expressions through serialization,
remapping and linking. A function with a readonly buffer-view local selects
`native-16` even when that view does not appear in an exported declaration;
no view crosses a call or module ABI boundary. Version 15 modules remain
readable with their original intrinsic and mutable-loan wire tags.
Readers also preserve v15's historical `native-15` label caused solely by
a seven-parameter declaration; current writers classify actual value types,
not parameter counts.
Version 17 retains the view scope's exclusivity, its checked `BoundViewSet`
effects, and the canonical `OwnedInt32Buffer.Clear` intrinsic. A retained
writable view or used clear operation selects `native-17`; an unused intrinsic
declaration does not promote an existing buffer-only program. Version 16
readonly-view modules retain their original wire layout and profile. Neither
view type can appear in a field or exported/imported function contract.
Version 18 also retains lexical managed-root scope and retirement metadata.
It admits image-private `ManagedNode` operations in a leaf producer with
scalar/void, by-value-only public exports. The DLL owns and explicitly collects
its own unreachable graph; no managed handle crosses a module or native DLL
boundary. The package reader verifies the retained `native-18` profile, roots
and node operations before permitting the DLL's bounded heap imports.
Previously stored modules remain readable under their original versions.
The version 19 `.trm` variant is written for leaf producers with private
source classes whose fields are scalar or managed references. It stores their bounded field layouts, constructor/factory
association, optional-handle expressions and exact lexical roots, and validates
the complete retained class/function table on reload. Class leaves select
`native-19`, or `native-20` when optional handles occur; class-free modules keep
their version 18 wire layout and historical profiles. Exports remain scalar/void
by-value declarations, and a v19 managed leaf cannot import another provider.
Source classes and managed handles never become imported module layouts or
native DLL values. An optional-node-only program without a private source
class remains outside this bounded module increment. Class leaves with
acyclic, handle-free Copy struct fields use semantic version 20, retaining
their private nested value layouts and natural field alignment. Versions 18
and 19 keep their original wire layouts and reject inline class fields; class
leaves without those fields still write version 19. Native `native-19` and
`native-20` profiles still depend on optional handles, not the presence of a
private Copy field. Collection is local to the leaf image; no DLL-unload heap
cleanup of live objects is promised. For a runnable version 20 replacement,
use the [Copy-field class leaf](../samples/native/graph/managed-copy-class-leaf.terrum)
as the producer source in the [unchanged-root graph example](native-graph.md).
Semantic version 21 records nearest-loop `break` and `continue` and uses a
separate iterator-bearing loop tag when `continue` must run a `for` iterator.
The original while tag and unchanged for lowering retain their existing
bytes; producers, consumers, and class-free graph libraries write version 21
only when they retain a loop transfer or explicit iterator. Readers reject the
new tags in versions 18 through 20. Loop transfers do not change exported
declarations, native runtime profiles, or module dependency policy.
Semantic version 22 admits compact handle-free Copy struct parameters in private
class constructor/factory signatures. It retains and remaps their nested private
layouts even when the constructor stores only scalar fields. Versions 19 through
21 reject these parameters rather than interpreting them with the former
scalar/handle factory convention. Unaffected modules retain their older writer
versions and bytes. The new signatures stay private behind scalar exports;
native graph replacement does not rewrite root or intermediate images.
Semantic version 23 admits class constructors with more than three inputs,
up to 63 within the ordinary 64-argument receiver/output-inclusive bound.
It composes with the earlier Copy-argument and loop-transfer features; readers
of versions through 22 retain the three-input restriction. Native profiles and
the private managed-leaf dependency policy do not change.
Semantic version 24 carries the managed class/`ManagedNode` access contract.
Semantic version 25 adds a distinct `MutReceiver` contract for persisted,
non-exported mutating methods on non-owning compact structs. Source visibility
may be public or private; methods can be invoked through named mutable locals
or their writable nested fields, or forwarded through an existing writable
`this` and its writable fields. Older headers reject methods
requiring this contract. Semantic version 26 adds nested mutable-field loan
expressions; v25 and earlier headers reject the new expression tag. The
serialized path uses field identities, not unchecked byte offsets, and is
remapped and verified against the linked private layouts. Writers select v26
only when a body contains such a loan; whole-local mutable receivers retain
v25. Semantic v27 permits ordinary mutating methods on unique structs,
retaining the existing pointer receiver lowering and cleanup contract.
Readonly loans cannot target those methods, and earlier headers reject
them even though the method/loan record shapes are unchanged.
Semantic v28 extends nested mutable-field loans through unique roots and
children. Writer selection inspects the serialized function's field paths
against the module's remapped layouts, including loans in symbolic call
arguments. Earlier headers reject unique-root field loans; programs needing
only the v25-v27 contracts retain their existing versions.
Semantic v29 admits mutable owning-buffer field loans only for the existing
`Set`, `TryAppend` and `Clear` intrinsics. Version selection examines symbolic
call arguments; earlier headers reject these loans, while programs requiring
only earlier receiver contracts retain their versions. At v29, the physical
native ABI and then-current scalar-only public boundary did not change.
Semantic v30 adds mutable `BoundViewScope` acquisitions whose owner is a
writable buffer-field loan. Writers select v30 for that scoped contract;
v29 and earlier headers reject it, while named mutable views and ordinary
buffer-field calls retain their earlier versions. Field paths are remapped
against private layouts, and linking revalidates initialization, writable
access and whole-root exclusion.
Semantic v31 permits managed-backed leaf producers to export existing public,
handle-free compact `Copy` structs of at most 16 bytes as by-value parameters
and results, and through the existing `bool`/`out` conditional-output contract,
including scalar outputs. Public-field visibility and nominal type provenance
apply to `Copy` types actually crossing an argument or result ABI, not
automatically to the export's declaring owner. Existing scalar-only static
exports on public owner types with private fields remain valid, and a static
declaration on a unique owner does not transfer that owner. `NoThrow`
contracts and the existing Windows x64 aggregate classification remain part
of the contract; returned or published snapshots are values independent of
the producer's private managed heap. Managed handles, unique values, nested
managed-containing values, views, reference modes and mutating-receiver ABI
remain excluded, and v31 managed leaves could not import another producer.
Writers select semantic v31 when an expanded export contract requires it;
scalar-only managed producers retain their prior required version, and older
headers reject expanded contracts. This adds no syntax or native physical ABI
or profile version.
Programs requiring none of these contracts retain their existing versions and bytes
regardless of managedness. The ordinary readonly/by-value receiver ABI is unchanged.
Public scalar entry wrappers may call non-exported mutating methods; source
`public` visibility does not expose a public mutating-receiver ABI, and private
helpers remain private. Readers normalize private managed artifacts from
versions 18 through 23 into explicitly qualified internal IR, translating
legacy class/`ManagedNode` mutation authority into `write` exactly where it was
previously granted, without widening the contract. A v18-v23 header containing
v24 write identifiers is rejected. The native physical handle ABI and absence of a public managed-handle ABI
remain unchanged. Semantic v32 permits managed roots and intermediate
producers in persisted acyclic native graphs, subject to the scalar and
compact `Copy` value boundary above; it does not create cross-image shared
collection or graph cycles. Semantic v32 also supports private managed
consumers through the existing single-provider semantic `CreateConsumer`/
`Link` path and native single-provider and module-set entry points. Semantic modules
statically link into one image, remap private class identities, and use one
heap/root domain. Persisted native EXE/DLL sets retain separate image heaps
and root chains; imported calls preserve caller roots, and collection remains
image-local. A compatible semantic provider may be selected each time
`run-module` starts; JIT code is generated from the persisted artifacts for
that launch. `link-module` emits a fresh PE for the selected provider;
replacing a semantic provider does not change an already linked PE. Native
package replacement leaves the persisted consumer image unchanged. Managed
handles, unique values, views, reference modes and mutating-receiver ABI
cannot cross these boundaries. This adds no public semantic `LinkSet` or
native-set JIT API, syntax, scanner, native physical ABI/profile or graph
package-format change.

For the managed `Snapshot` consumer, compile both compatible providers and one
consumer artifact per policy. `run-module` services that same consumer
artifact with either provider. `link-module` instead emits a fresh executable
for each provider; it does not patch a previously linked PE:

```powershell
$cli = 'artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll'
foreach ($policy in 'fail-fast', 'checked-exceptions') {
    $suffix = $policy -replace '-', ''
    $original = "artifacts\snapshot-original-$suffix.trm"
    $replacement = "artifacts\snapshot-managed-$suffix.trm"
    $consumer = "artifacts\snapshot-consumer-$suffix.trm"
    $originalExe = "artifacts\snapshot-original-$suffix.exe"
    $replacementExe = "artifacts\snapshot-managed-$suffix.exe"
    dotnet $cli compile-module producer samples\native\graph\copy-contract-original.terrum ReadSnapshot --export TryReadSnapshot --export ReadTotal -o $original --runtime $policy
    dotnet $cli compile-module producer samples\native\graph\copy-contract-managed.terrum ReadSnapshot --export TryReadSnapshot --export ReadTotal -o $replacement --runtime $policy
    dotnet $cli compile-module consumer samples\native\modules\managed-copy-consumer.terrum $original -o $consumer --runtime $policy
    dotnet $cli run-module $consumer $original --runtime $policy # exits 42
    dotnet $cli run-module $consumer $replacement --runtime $policy # exits 44
    dotnet $cli link-module $consumer $original -o $originalExe --runtime $policy
    & $originalExe # exits 42
    dotnet $cli link-module $consumer $replacement -o $replacementExe --runtime $policy
    & $replacementExe # exits 44
}
```
A consumer importing a unique type must also import the exported NoThrow
cleanup methods of that type and its owning children. Their cleanup identities
and Copy/unique distinctions participate in layout and signature compatibility;
a replacement implementation may change without changing the consumer artifact.
Producer-private owners retain recursive cleanup targets without exporting
those private types or their methods. Native-12 marks contracts
containing unique values under either runtime policy. Producers/consumers
without unique layouts continue to use native-10 or native-11, even when
serialized by the newer `.trm` writer, so compatible older native-10/package-1
images remain usable. Native packages embed and check the exact
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
entries, never to funclets or runtime helpers. Earlier native-10/package-1 fail-fast images with
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
or consumer EXE with its corresponding semantic `.trm` contract. Producer
containers remain version 1; new consumer containers use version 2, and version
1 consumers remain readable. The container records the exact native profile
(native-10 through native-17), runtime policy,
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
emitting IAT entries for actual calls, including implicit recursive cleanup, named
exactly after their declarations. At validation and launch,
`ModuleCompiler.Link` checks the persisted declaration, value layout,
conditional output, field mutability, NoThrow and DoesNotReturn promises,
and visibility contracts and verifies the linked IR. It does **not** emit
code: the native consumer image is
unchanged when a compatible producer package is selected for another
launch. Version 2 records actual imported targets from the same encoder's
fixups alongside the semantic contract. Validation requires those targets to
belong to retained declarations, include semantic calls, and exactly match
the persisted PE's IAT. Version 1 retains its original semantic-call-derived
validation. Neither path derives imports from names in the replacement's
linked function table; a replacement can
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
