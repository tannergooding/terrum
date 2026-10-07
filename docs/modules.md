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
text or a native DLL. It is distinct from the approved current
[`TRRM` metadata design](il-metadata.md), whose candidate `(1, 0)`
framing and rows are not this development semantic-module writer's output.
The separate [`compile-trrm` execution path](implementation.md#executable-trrm-scalar-interchange)
now emits approved metadata and compact-linear/indexed Bodies to `.trrm` files
for a self-contained, fail-fast scalar subset with bounded branches and loops,
integer shifts, initialized writable-local compounds/updates and direct
module-local scalar calls. Call targets use the module's MethodDef ordinals,
with signatures and declared effects checked against the complete function
table; forward and recursive calls do not require source or name rebinding. Audited
policy-3 integer reinterpretations preserve the source shift contract without
changing checked numeric conversions. Loaded scalar graphs, including linear
Bodies that need these representation operations or calls, are not serializable as
development structured IR.
Initialized six-primitive locals in lexical blocks, branches and loops use
existing indexed values and target-visible edge arguments. Scope retirement
does not discard outer writes or recycle identities; loop initializers execute
per iteration. This adds neither executable lexical regions nor a wire revision.
Indexed `do` and scoped `for` loops retain body/condition/iterator ordering,
transfers and header-local scope through the existing region contract.
Non-nearest named `break`/`continue` route through designated single exits,
carrying target-visible values and preserving iterator/condition order; edges
that bypass a designated exit remain unsupported. This adds no wire encoding
or reader change. They use the same source-free loading and native consumers;
emission rejects acyclic loop shapes without a reachable backedge rather than
inventing one.
It does not replace the `.trm`
producer/consumer commands or add TRRM import linking. This page describes
the existing development semantic/native module paths.
The bounded native scalar frontend validates its versioned source projections,
then lowers supported constructs to the same verified operations used by these
module writers. Projection versions are not persisted semantic-module
revisions; the current source subset and projection compatibility gates are
specified in the [native profile](native.md). The separately implemented
approved-TRRM loop envelope described above is a different wire format.
The current semantic-module revision is 73. Writers emit only revision 73 and
readers reject every other revision; older artifacts must be recreated from
source. The current format preserves verified, typed, source-mapped structured
IR and the supported declaration, layout, initialization, cleanup, and runtime
contracts described in this reference and the [implementation reference](implementation.md).

Revision 68 provisionally persists caller-unsafe field-value reads inside
`unsafe(expr)` for eligible public `Copy` paths, alongside the existing
expression-scoped call slice. Unsafe-block field mutation and supported
mutable-field loans remain separate; expression scopes do not add mutation,
loan, TRRM, or scalar-frontend verification/emission support. Revision 69
additionally persists getter-only and mixed caller-unsafe getter calls inside
`unsafe(expr)` for eligible public `Copy` owners, including recursively
exportable `Copy` results. Getter identity remains canonical in serialized
modules; linking permits only its established producer qualification. These
expression scopes do not add getter mutation, loans, TRRM, or scalar-frontend
verification/emission support. Revision 70 additionally persists eligible
caller-unsafe readonly instance methods inside `unsafe(expr)`, with a single
by-value receiver and recursively exportable by-value parameters/results. It
validates an owner-relative method identifier and admits producer
qualification only during linking. Revision 70 did not support mutable or loan
receivers, constructors, operators, reference/output modes, TRRM, or
scalar-frontend verification/emission. The broader
[reference-call metadata proposal](proposals/reference-call-metadata.md)
remains unapproved.

Revision 71 provisionally persists a mutable field-receiver loan only as
source argument zero mapped to formal parameter zero of an ordinary
synchronous mutable instance method inside `unsafe(expr)`. At least one field
edge must be caller-unsafe, with each edge's declared access authority checked
independently and existing recursive public, non-unique `Copy` layout,
declaration-access, initialization, place, mutability, ownership and
reservation checks enforced. The loan is nonescaping and reuses the existing
`BoundCall`/`BoundMutableFieldLoan` representation without an ABI or
authorization-bit change. Reordered or wrapped receivers, readonly field
loans, caller-unsafe mutable method contracts and other reference/output modes
remain unsupported.

Revision 72 provisionally permits the same direct receiver loan to target a
caller-unsafe mutable instance method only when the complete call remains
inside `unsafe(expr)`. The method's by-value explicit parameters and result
must pass the recursive public `Copy` layout gate, and the complete argument
mapping, `MutReceiver` body contract, receiver shape and scope are checked by
both writer and reader. It adds no receiver ABI or authorization bit, and
continues to reject mutable receiver imports, direct mutable-method exports,
wrapped/remapped receiver loans, unsafe-block calls, and unsupported
reference/output modes.

Revision 73 provisionally persists caller-unsafe static binary nominal
operator calls inside `unsafe(expr)` for eligible public, non-unique `Copy`
owners with recursively exportable layouts. The exact recognized binary
operator contract requires two by-value owner-typed operands and either the
owner result type or `bool` for comparisons; the complete `BoundCall` must be
inside the wrapper and retain the canonical positional argument mapping.
Writers and readers validate the same declaration contract and reconstruct
authorization from the expression scope. Unary operators and nominal
updates remain outside this new expression-scope slice; existing unsafe-block
operator support is unchanged. This adds no ABI, TRRM or scalar-frontend
verification/emission support and does not approve the broader
reference-call metadata proposal.

The invocation-scoped source grant and `scanner-10` profile remain provisional
semantic-module JIT capabilities under their respective proposals. They bind
fresh invocation inputs and do not imply graph, module-set, native DLL, or
persisted AOT support.

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
current module format does not relax its exact layout checks. The bounded transparent `Copy` wrapper is a specific contract: one-field
wrappers over `bool`, `char16`, `int32`, `uint32`, `int64`, or `uint64` retain
nominal identity and field visibility/mutation rules, while storage and direct
argument/result classification match the carrier. Semantic modules persist
the transparent-layout intent for private and exported structures, and
compatible replacements must preserve that intent. This does not add
implicit conversions or relax exact checks for other module layouts.
Managed classes support up to 32 fields; construction tracks the final field
in a 32-bit initialization mask. The collector descriptor remains an offset
list. Private wide classes can be retained in producers, graph participants
or consumers; this does not expose managed handles across module boundaries.
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
not selected limits for the pre-v1 `TRRM` specification. This format is not authenticated: artifact bytes are checked for structural
safety and declared-contract compatibility, not provenance. Reader regressions
exercise the current revision and rejection of retired or malformed inputs;
they do not promise compatibility with historical artifacts.
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
source diagnostics retain the originating snapshot. The self-host component
test builds `SourceRange`, `CharacterFacts`, `Utf16CodeUnits`, `IntegerFacts`,
the test-only `IntegerFactsTestExports` bridge, `Token`, `CopyAlgorithms`,
the producer-local `CopyAlgorithmsTests` wrapper, `Float32MathSelection`,
`Float64MathSelection`, the producer-local `FloatMathSelectionTests` wrapper,
`ManagedTokenChain` and its producer-local test wrapper, `WideIntegerFacts`,
`IntegerFormatting`, the producer-local `IntegerFormattingTests` wrapper,
`IntegerCheckedArithmetic`, `IntegerMath`, `IntegerSaturation`,
`IntegerWrapping`, `IntegerCheckedConversions`, and `BitOperations` into one
producer, then independently binds `RangeTests`, `TokenTests`, `WideIntegerTests`,
`CharacterFactsTests`, `Utf16CodeUnitsTests`, `IntegerFactsTests`,
`IntegerCheckedArithmeticTests`, `IntegerMathTests`,
`IntegerSaturationTests`, `IntegerWrappingTests`,
`IntegerCheckedConversionsTests`, `BitOperationsTests`, and the Terrum-authored
`ComponentTestRunner` against its exported declarations.
The formatter wrapper alone crosses the module boundary; its unique workspace
is created, reused, and consumed inside the producer, so the candidate owner
does not become a module export.
The copy wrapper likewise keeps generic specializations in the producer and
exports only its test result, not generic candidate functions.
The managed-chain wrapper keeps owners, links, cursors, and token values within
the producer; only its boolean result is exported. It exercises bounded
append/clear, indexed reads, cursor invalidation, and collection, without
establishing general owning-graph support.
Those persisted modules execute through AOT and JIT under both runtime
policies, including range boundaries,
conditional-output failure, character classification, hexadecimal mapping and
UTF-16 scalar decoding,
aggregate token construction, 32-/64-bit checked arithmetic and accumulation,
integer-math extrema, saturation and wrapping boundaries, checked-conversion
publication, bit-operation boundaries, finite `float32`/`float64` math-selection
cases, and repeated calls. The float checks cover only finite values and do not
resolve NaN, signed-zero, payload, signaling, or status behavior, or approve the
candidate APIs. C# still
orchestrates the test; the entry harness only composes these explicitly
selected component checks.
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

The bounded `ComponentTestRunner` composes the range, character-fact, UTF-16,
token, checked 32-/64-bit arithmetic, wide-integer, integer-math,
integer-saturation, integer-wrapping, checked-conversion and 32-/64-bit
bit-operation checks against one independently compiled producer:

```powershell
$componentSources = @(
    'self-host\SourceRange.terrum',
    'self-host\CharacterFacts.terrum',
    'self-host\Utf16CodeUnits.terrum',
    'self-host\IntegerFacts.terrum',
    'self-host\IntegerFactsTestExports.terrum',
    'self-host\Token.terrum',
    'self-host\ManagedTokenChain.terrum',
    'self-host\ManagedTokenChainTests.terrum',
    'self-host\CopyAlgorithms.terrum',
    'self-host\CopyAlgorithmsTests.terrum',
    'self-host\Float32MathSelection.terrum',
    'self-host\Float64MathSelection.terrum',
    'self-host\FloatMathSelectionTests.terrum',
    'self-host\WideIntegerFacts.terrum',
    'self-host\IntegerFormatting.terrum',
    'self-host\IntegerFormattingTests.terrum',
    'self-host\IntegerCheckedArithmetic.terrum',
    'self-host\IntegerMath.terrum',
    'self-host\IntegerSaturation.terrum',
    'self-host\IntegerWrapping.terrum',
    'self-host\IntegerCheckedConversions.terrum',
    'self-host\BitOperations.terrum'
)
$componentExports = @(
    'Terrum.CodeAnalysis.SourceRange.TryCreate',
    'Terrum.CodeAnalysis.SourceRange.GetEnd',
    'Terrum.CodeAnalysis.SourceRange.#get:End',
    'Terrum.CodeAnalysis.IsAsciiDigit',
    'Terrum.CodeAnalysis.IsLineBreak',
    'Terrum.CodeAnalysis.GetDecimalDigit',
    'Terrum.CodeAnalysis.TryGetHexDigit',
    'Terrum.CodeAnalysis.DecodeUtf16Scalar',
    'Terrum.CodeAnalysis.Utf16ScalarWidth',
    'IntegerFactsInt32Mask',
    'IntegerFactsUInt32Mask',
    'IntegerFactsInt64Mask',
    'IntegerFactsUInt64Mask',
    'Terrum.CodeAnalysis.Tests.CheckIntegerFormatting',
    'Terrum.CodeAnalysis.Tests.CheckCopyAlgorithms',
    'Terrum.CodeAnalysis.Tests.CheckFiniteFloatMathSelections',
    'Terrum.CodeAnalysis.CheckManagedTokenChain',
    'Terrum.CodeAnalysis.Token.#ctor',
    'Terrum.CodeAnalysis.Numerics64.TryAddInt64',
    'Terrum.CodeAnalysis.Numerics64.TrySubtractInt64',
    'Terrum.CodeAnalysis.Numerics64.TryMultiplyInt64',
    'Terrum.CodeAnalysis.Numerics64.TryDivideInt64',
    'Terrum.CodeAnalysis.Numerics64.TryRemainderInt64',
    'Terrum.CodeAnalysis.Numerics64.TryDivideUInt64',
    'Terrum.CodeAnalysis.Numerics64.TryRemainderUInt64',
    'Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal',
    'Terrum.CodeAnalysis.Numerics64.TryAccumulateBase',
    'Terrum.CodeAnalysis.Numerics64.TryAddInt32',
    'Terrum.CodeAnalysis.Numerics64.TrySubtractInt32',
    'Terrum.CodeAnalysis.Numerics64.TryMultiplyInt32',
    'Terrum.CodeAnalysis.Numerics64.TryDivideInt32',
    'Terrum.CodeAnalysis.Numerics64.TryRemainderInt32',
    'Terrum.CodeAnalysis.Numerics64.TryNegateInt32',
    'Terrum.CodeAnalysis.Numerics64.TryAbsoluteInt32',
    'Terrum.CodeAnalysis.Numerics64.MagnitudeInt32',
    'Terrum.CodeAnalysis.Numerics64.TryAddUInt32',
    'Terrum.CodeAnalysis.Numerics64.TrySubtractUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryMultiplyUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryDivideUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryRemainderUInt32',
    'Terrum.CodeAnalysis.Numerics64.AbsInt32',
    'Terrum.CodeAnalysis.Numerics64.TryAbsInt32',
    'Terrum.CodeAnalysis.Numerics64.SignInt32',
    'Terrum.CodeAnalysis.Numerics64.MinInt32',
    'Terrum.CodeAnalysis.Numerics64.MaxInt32',
    'Terrum.CodeAnalysis.Numerics64.ClampInt32',
    'Terrum.CodeAnalysis.Numerics64.BigMultiplyInt32',
    'Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt32',
    'Terrum.CodeAnalysis.Numerics64.AbsInt64',
    'Terrum.CodeAnalysis.Numerics64.TryAbsInt64',
    'Terrum.CodeAnalysis.Numerics64.SignInt64',
    'Terrum.CodeAnalysis.Numerics64.MinInt64',
    'Terrum.CodeAnalysis.Numerics64.MaxInt64',
    'Terrum.CodeAnalysis.Numerics64.ClampInt64',
    'Terrum.CodeAnalysis.Numerics64.GreatestCommonDivisorInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt32ToInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt64ToInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt32ToChar16',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToChar16',
    'Terrum.CodeAnalysis.Numerics64.SaturateInt64ToChar16',
    'Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToChar16',
    'Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.SaturateChar16ToInt64',
    'Terrum.CodeAnalysis.Numerics64.SaturateChar16ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapInt32ToInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt32ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapInt64ToInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt64ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapInt32ToChar16',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt32ToChar16',
    'Terrum.CodeAnalysis.Numerics64.WrapInt64ToChar16',
    'Terrum.CodeAnalysis.Numerics64.WrapUInt64ToChar16',
    'Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.WrapChar16ToInt64',
    'Terrum.CodeAnalysis.Numerics64.WrapChar16ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt32ToChar16',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt32ToChar16',
    'Terrum.CodeAnalysis.Numerics64.TryConvertInt64ToChar16',
    'Terrum.CodeAnalysis.Numerics64.TryConvertUInt64ToChar16',
    'Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt32',
    'Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToInt64',
    'Terrum.CodeAnalysis.Numerics64.TryConvertChar16ToUInt64',
    'Terrum.CodeAnalysis.Numerics64.LeadingZeroCountUInt32',
    'Terrum.CodeAnalysis.Numerics64.TrailingZeroCountUInt32',
    'Terrum.CodeAnalysis.Numerics64.PopCountUInt32',
    'Terrum.CodeAnalysis.Numerics64.RotateLeftUInt32',
    'Terrum.CodeAnalysis.Numerics64.RotateRightUInt32',
    'Terrum.CodeAnalysis.Numerics64.ReverseBitsUInt32',
    'Terrum.CodeAnalysis.Numerics64.ReverseEndiannessUInt32',
    'Terrum.CodeAnalysis.Numerics64.IsPow2UInt32',
    'Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt32',
    'Terrum.CodeAnalysis.Numerics64.LeadingZeroCountUInt64',
    'Terrum.CodeAnalysis.Numerics64.TrailingZeroCountUInt64',
    'Terrum.CodeAnalysis.Numerics64.PopCountUInt64',
    'Terrum.CodeAnalysis.Numerics64.RotateLeftUInt64',
    'Terrum.CodeAnalysis.Numerics64.RotateRightUInt64',
    'Terrum.CodeAnalysis.Numerics64.ReverseBitsUInt64',
    'Terrum.CodeAnalysis.Numerics64.ReverseEndiannessUInt64',
    'Terrum.CodeAnalysis.Numerics64.IsPow2UInt64',
    'Terrum.CodeAnalysis.Numerics64.TryRoundUpToPowerOfTwoUInt64'
)
$producerArgs = @('compile-module', 'producer', $componentSources[0], $componentExports[0])
for ($index = 1; $index -lt $componentSources.Count; $index++) {
    $producerArgs += @('--source', $componentSources[$index])
}
for ($index = 1; $index -lt $componentExports.Count; $index++) {
    $producerArgs += @('--export', $componentExports[$index])
}
$producerArgs += @('-o', 'artifacts\component-values.trm')
dotnet $cli @producerArgs

$componentTests = @(
    'self-host\RangeTests.terrum',
    'self-host\CharacterFactsTests.terrum',
    'self-host\Utf16CodeUnitsTests.terrum',
    'self-host\IntegerFactsTests.terrum',
    'self-host\TokenTests.terrum',
    'self-host\WideIntegerTests.terrum',
    'self-host\IntegerCheckedArithmeticTests.terrum',
    'self-host\IntegerMathTests.terrum',
    'self-host\IntegerSaturationTests.terrum',
    'self-host\IntegerWrappingTests.terrum',
    'self-host\IntegerCheckedConversionsTests.terrum',
    'self-host\BitOperationsTests.terrum',
    'self-host\ComponentTestRunner.terrum'
)
$consumerArgs = @('compile-module', 'consumer', $componentTests[0], 'artifacts\component-values.trm')
for ($index = 1; $index -lt $componentTests.Count; $index++) {
    $consumerArgs += @('--source', $componentTests[$index])
}
$consumerArgs += @('-o', 'artifacts\component-tests.trm')
dotnet $cli @consumerArgs
dotnet $cli run-module artifacts\component-tests.trm artifacts\component-values.trm --runtime fail-fast # exits 0
dotnet $cli link-module artifacts\component-tests.trm artifacts\component-values.trm -o artifacts\component-tests.exe --runtime fail-fast
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
fail-fast process termination counts as NoThrow. A version-ordering policy and
resilient public layouts remain undefined; cyclic dependencies remain
unsupported.

## Native package and image boundaries

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
serialized by the newer `.trm` writer. Native packages embed and check the exact
semantic profile rather than inferring it from runtime policy alone.
`ModuleCompiler.GetNativeProfileVersion` and
`NativeModuleCompiler.Inspect(...).NativeProfileVersion` expose that retained
contract profile; module CLI artifact and validation responses report it too.
Unused unique declarations removed during semantic export selection do not
upgrade the artifact profile. Package and semantic runtime policies must still
match exactly when both use native-12. Compilation and module JSON responses
report `runtimePolicy` independently of `profile`, including analysis and
validation responses without an output artifact.

Native package validation checks allocation-only records for the fixed runtime
prelude, saved-RBP records for current source functions and cleanup funclets,
their ordered `.pdata` ranges, `.xdata` bounds and corresponding machine-code
prologs before accepting an image. The four failure helpers (plus two consumer
entry wrappers) precede source functions; an image-private collector, when
present, is the final allocation-only function. Retired allocation-only source
frames are rejected. Checked native-11 images additionally validate bounded
scope records and handler references. Source functions precede cleanup
funclets; exports must point to source-function entries, never to funclets or
runtime helpers. Validation also rejects unwind ranges shorter than their
described prolog.

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
or consumer EXE with its corresponding semantic `.trm` contract. Producer and
consumer containers use version 2 and include a machine-import table; producer
tables are empty, while consumer tables record imports reported by that
emission. The reader accepts only version 2. The container records the exact
native profile (native-10 through native-17), runtime policy,
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
the persisted PE's IAT. Validation does not derive imports from names in the
replacement's linked function table; a replacement can add exports without
changing a consumer that has an unrelated local function of the same name.
Unused declarations retain semantic contracts but need no IAT cell. Bounded
PE32+ structure checks require the declared exports
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
