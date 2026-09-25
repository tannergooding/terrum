# Windows x64 native profile

`native-7` is a deliberately bounded executable language profile, separate from
the wider syntax grammar and proposed libraries. The compiler is hosted in
C#/.NET, but emitted programs are native PE32+ images with no CLR header,
CIL, LLVM, source translation or external linker dependency.

## Compile and run

```powershell
.\build.cmd -restore
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll analyze samples\native\control-flow.terrum --json
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build samples\native\control-flow.terrum -o artifacts\control-flow.exe --runtime fail-fast --json
.\artifacts\control-flow.exe
$LASTEXITCODE # 45
```

The output directory must already exist. `build` requires explicit
`--runtime fail-fast`; it never runs the output. Syntax, binding and native
resource-limit failures produce no new artifact. The complete image is written
to a unique sibling file, flushed and renamed over the destination only after
success, so a failed compilation does not truncate an existing executable.
The input source cannot also be the output path. I/O failures exit 2.

`check`, `tokens` and `tree` keep their syntax-only meaning. `analyze` performs
profile-specific binding without emission. Compile diagnostics exit 1; success
exits 0. JSON schema 1 uses UTF-16 ranges and retains the source content ID.
Successful builds add an `artifact` containing its path, target, explicit runtime
policy, byte length and SHA-256. A successful syntax check alone does not
authorize emission.

`analyze`, `build`, `build-library` and `jit` accept repeatable `--source <path>` options.
All files participate in one declaration/binding pass, so file order does not
restrict forward references. At most 256 distinct canonical paths are accepted;
only the first input may be stdin. A build output cannot replace any input.
Single-source JSON remains schema 1. Source sets use schema 2 with input-ordered
`sources` (path, content ID and UTF-16 length) and a `sourceIndex` on every
diagnostic; a successful build adds the same `artifact` object.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build samples\native\modules\wide-entry.terrum --source self-host\WideIntegerFacts.terrum --source self-host\WideIntegerTests.terrum -o artifacts\wide.exe --runtime fail-fast
```

This compiles source together; it does not replace the persisted, independently
compiled consumer/producer contract exercised by [modules](modules.md).

Native libraries use `build-library`, explicit public export names and a `.dll`
output. They require no `Main` and have no generated initialization entry point.
The same source-set, fail-fast and output-preservation rules apply. Library
artifact JSON additionally identifies `kind: "dynamic-library"`. Invalid native
exports produce `TRM4002`; source visibility alone never exports a declaration.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build-library self-host\SourceRange.terrum --export Terrum.CodeAnalysis.SourceRange.TryCreate --export Terrum.CodeAnalysis.SourceRange.GetEnd -o artifacts\ranges.dll --runtime fail-fast
```

## Executable language

| Area | Supported |
|---|---|
| Program | File-scoped namespaces, using directives, top-level/static/instance functions; parameterless `int32 Main()` or `void Main()`; multi-source executable/library compilation |
| Types | `int32`, `uint32`, `int64`, `uint64`, `char16`, `bool`, `void` results; compact reference-free structs |
| Literals/constants | Contextual integers, explicit `i32`/`u32`/`i64`/`u64` tags, primitive `MinValue`/`MaxValue`; booleans; unit `()` |
| Variables | Initialized typed/inferred locals; immutable bindings by default, explicit `mut` assignments; lexical scopes |
| Expressions | Direct calls, forward calls/recursion, constructors, field reads, checked numeric casts; unary `+`, signed checked `-`, `~`, `!` |
| Operators | Checked signed/unsigned `+ - * / %`, bitwise operations, signed/unsigned comparisons, same-type scalar equality, boolean operations; code-unit comparison but no `char16` arithmetic |
| Flow | Blocks, `if`/`else`, `while`, scoped `for`, return, boolean `require`; left-to-right argument/operand evaluation and short-circuit `&&`/`||` |
| Outputs | Bool/out true-only publication and definite assignment through branches, `!`, `&&`, `||` and `require` |
| Storage | Explicitly granted, initialized fixed host buffers with checked indexing and read/write access; capabilities are not first-class source values |

Fixed host buffers still use the explicit 32-bit-word interchange described in
the [scanner contract](scanner.md). Adding 64-bit scalar values does not enlarge
that storage contract: `int64`/`uint64` buffer descriptors are rejected.

Literal context comes from declarations, parameters, return types or a known
operand, not an implicit `int32` fallback. `var x = 1` is rejected; `var x = 1i32`
and `int32 x = 1` are accepted. A signed minimum literal can be constructed
directly, without first overflowing its positive magnitude. Locals cannot be
read before declaration/initialization, immutable bindings cannot be assigned,
conditions must be boolean and non-void functions must return on all validated
paths (loops are conservatively allowed to execute zero times). Function
overloading, active-scope local shadowing, named arguments, `ref`/`mut ref`
passing and mutating instance methods are not supported.

For loops accept an optional initializer, boolean condition and iterator,
with a braced body. Initialization runs once; each successful condition is
followed by the body and then the iterator. Returning from the body skips the
iterator. An omitted condition is true. Header locals are scoped to the loop;
the iterator cannot access body locals. Use assignments such as `i = i + 1`:
increment operators, comma-separated clauses, break and continue are not
implemented. [The loop sample](../samples/native/for-loops.terrum) returns 285.

Other integer widths, floating point, character/text literals, shifts, classes,
optional/generic/family binding, general borrowed references, ownership/cleanup,
exceptions, GC, async, arbitrary interop and the standard library are outside the
profile. Their syntax may be inspectable, but a build
must diagnose unsupported semantics. No source library is silently assumed.
The 64-bit types are fixed-width integers, not aliases for `nint`, `nuint` or
native pointers. Entry-point process statuses remain `int32`; a wider return
value requires an explicit checked conversion before returning from `Main`.

## Values, places and conditional publication

Struct fields contain supported primitive values or other acyclic compact structs, with explicit constructors
and naturally aligned declaration-order layouts of 1 through 16 bytes.
Constructors must initialize every field
before reading or returning the entire receiver; individual initialized fields
can be read during construction. Copying a value does not create a storage
alias. Fields are read-only after construction unless declared `mut`.
Field writes require an accessible `mut` field and mutable receiver, except
that a constructor can write its own receiver's fields regardless of `mut`.
That exception does not grant write authority over nested fields declared by
another type. Instance methods receive copies;
mutating instance methods are rejected rather than silently losing writes.
Layouts resolve across source files regardless of declaration order; recursive
value containment is rejected. Nested field reads and whole-field replacement
preserve value copies, visibility and constructor initialization. A nested
aggregate's alignment is its maximum field alignment, not its byte size.
Sizes other than 1, 2, 4 and 8 require the Windows indirect aggregate ABI;
the compiler owns argument copies and result storage, not source-visible pointers.
Nested field paths can be assigned through a mutable local or constructor
receiver. Every containing value must already be initialized, including after
evaluating the right-hand side; a call cannot vacate an enclosing output and
leave a partially valid nested write. Each field's visibility and mutation
authority is checked, including all containing fields. A writable binding does
not upgrade a read-only field, and an `out` value does not grant constructor
authority after publication into the callee's scratch storage.
Only whole top-level fields establish constructor initialization. Writes
through temporary values or immutable receivers are rejected.
Whole-value replacement through a mutable local remains valid even when its
fields are read-only. `SourceRange` and `Token` retain read-only fields; updates
construct and replace valid values rather than bypass their constructors.
Aggregates over 16 bytes, reference-containing aggregates and implicit
construction are not implemented.

Top-level functions and types default to `internal`; struct members,
constructors and fields default to `private`. Module exports require explicit
public declarations and supported public value layouts. Top-level `private`,
field initializers and field-level `mut`/`static` are rejected. Field writes
are governed by the owning value's access, not a separate field-mutability flag.

Checked casts preserve a numeric value or fail for range overflow. Raw
`char16` values include surrogate code units: a successful cast is not Unicode
scalar validation. The profile has no implicit conversion between typed numeric
values and no implicit text encoding.

The signed/unsigned 32- and 64-bit types retain their full range through calls,
fields, conditional outputs and persisted modules. Widening a signed negative
value preserves its sign; narrowing or crossing signedness checks the numeric
range rather than reinterpreting bits. The same signed-minimum division and
remainder rules apply at both signed widths. The
[wide integer candidate](../self-host/WideIntegerFacts.terrum) uses these rules
for non-overflowing signed-addition checks and bounded decimal accumulation
through `uint64.MaxValue`, with true-only output publication.

A bool-returning declaration with `out` parameters promises initialized outputs
on true. Callees construct outputs in private scratch storage and publish only
on successful return; a false return does not manufacture a readable default.
An output target is unavailable while the call is pending and is vacant on
false, even if its backing storage still contains a previous value's bits.
Calls stage output storage separately from ordinary arguments. Aliased output
targets and reads of not-yet-initialized outputs are rejected. Conditional
assignment facts follow evaluated short-circuit paths, and `require` carries
the true-path facts forward. Owning outputs and exceptional cleanup are
rejected, not simulated by this scratch protocol.

[`self-host/SourceRange`](../self-host/SourceRange.terrum),
[`CharacterFacts`](../self-host/CharacterFacts.terrum),
[`RangeTests`](../self-host/RangeTests.terrum) and
[`Token`](../self-host/Token.terrum) execute through this profile.
The [scanner component](scanner.md) additionally uses fixed checked buffers;
this does not implement a general span ABI or a complete lifetime checker.

## Runtime policy

The language's eventual ordinary runtime uses exceptions for exceptional
failures. This executable profile has **no catch/cleanup runtime** and accepts
only explicit `fail-fast` selection. Checked arithmetic remains checked:

| Failure | Process exit status |
|---|---|
| Checked signed/unsigned arithmetic or numeric conversion overflow | `0xE0540001` |
| Integer division or remainder by zero | `0xE0540002` |
| A false boolean `require` predicate | `0xE0540003` |
| Negative or out-of-range host-buffer index | `0xE0540004` |

`require predicate;` evaluates its boolean once and continues only on true.
This is executable semantic code, not a debug assertion, unchecked assumption
or inferred public precondition. Its ordinary language exception contract is
not yet available: **the explicit fail-fast profile terminates instead**.
Exceptional cleanup remains unsupported rather than approximated.
A predicate's own arithmetic failure retains that failure
status. `samples/native/requirements.terrum` demonstrates a parameter-dependent
requirement and returns 10.

Division truncates toward zero; signed minimum remainder -1 is zero, not an
overflow. Neither compiler-host overflow mode nor x64 `idiv` traps define these
semantics. Failure terminates through imported `ExitProcess`, rather than
continuing with a fabricated value or advertising a catchable Terrum exception.
Normal `int32 Main` returns its 32-bit value as the process status; `void Main`
returns zero. Consequently a deliberate program return can match a reserved
failure status; exit codes are not a general language exception transport.

The profile does not promise recovery from stack exhaustion, OS termination or
resource exhaustion. It does not implement unwinding Terrum cleanup, exception
filters or fatal-error text formatting. A future full runtime must preserve
ordinary language semantics rather than silently inherit this explicit profile.

## Semantic and target boundaries

`Compilation.Create` declares names/signatures before binding bodies.
It returns deterministic diagnostics and no `BoundProgram` on errors, including
definite-assignment and construction failures found during source verification.
For multiple source snapshots, `SyntaxTrees` retains input order and
`SourceDiagnostics` pairs each diagnostic with its owning snapshot; the legacy
`SyntaxTree` property refers only to the first snapshot.
Resolved functions, slots and types are immutable public data. Structured
blocks, branches and loops preserve control-flow meaning; no source token is
translated directly to an opcode.

`SemanticVerifier.Verify` checks the public bound representation again before
emission, including operation types, calls, slot initialization/mutability and
returns. This matters because a library client can construct bound objects
without going through the binder. Invalid IR is an argument error, not a
machine-code fallback. Limits on semantic depth and native frame/image size
are intentional compilation failures, not stack-overflow handling strategies.
The current ceilings are 1,024 functions, 256 parameter/local slots per function,
64 arguments, 128 levels of IR nesting, 100,000 verification visits, 3,840-byte frames and
8 MiB of machine code. Valid programs exceeding emission limits report
`TRM4001`; these are implementation limits, not language type or ABI guarantees.
Verification visits include conditional-flow analysis, not only unique IR
nodes. Source analysis reports an exhausted verification budget as `TRM3016`.
The fixed-buffer profile additionally limits descriptors to 256 and total
initialized storage to 1,048,576 32-bit words.

The x64 backend uses fixed stack frames, caller shadow space and aligned calls.
The first four scalar arguments use RCX, RDX, R8 and R9; later arguments
occupy stack slots. Live intermediate values and arguments are spilled across
nested calls. Functions preserve nonvolatile registers and publish Windows
unwind metadata for stack adjustment; those records support platform stack
walking, not Terrum exception handling.

Aggregate sizes of 1, 2, 4 and 8 bytes use integer carriers. Other supported
sizes use pointers to caller-owned 16-byte-aligned copies. Indirect results
receive a hidden first argument in RCX, shifting explicit arguments by one
position; the callee returns that storage address in RAX. These addresses are
backend storage, not language pointers, escaping borrows or shared mutable
references. Outputs use separate exact-size publication into the caller's
storage only on a true return.

The independent ABI fixture uses a C caller, rather than validating calls only
by having Terrum call itself. From an x64 Visual Studio developer shell at the
repository root:

```powershell
ml64 /nologo /c /Foartifacts\abi-carrier.obj tests\native\abi-carrier.asm
cl /nologo /W4 /WX /O2 /TC tests\native\abi-probe.c /Foartifacts\abi-probe.obj /Feartifacts\abi-probe.exe /link artifacts\abi-carrier.obj /INCREMENTAL:NO
dotnet run --file scripts\check-native-abi.cs
```

It checks flat and nested direct and indirect aggregates passed/returned by value,
hidden results and register/stack arguments, true/false output publication, guard storage
around scalar and aggregate output destinations, and narrow
arguments with deliberately unspecified upper register bits. The script
obtains function addresses from encoder ranges; the development executable
does not advertise an exported native API.

Images prefer base `0x140000000`, with 4 KiB section alignment and 512-byte
file alignment. `.text` is read/execute, `.idata` read/write and
`.pdata`/`.xdata` read-only. Optional `.rdata` holds immutable buffers and
`.data` holds writable buffers. NX, dynamic base and high-entropy 64-bit VA
are enabled. Code and data references are RIP-relative; import and unwind
metadata use RVAs. There are no image-base-dependent locations to fix up, so
the base relocation directory is empty and relocations are **not** marked
stripped. The [PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)
defines relocation entries for addresses needing correction; the
[`RELOCS_STRIPPED` flag](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-image_file_header)
would instead require loading at the preferred base. A separate Windows
loader probe reserves that address, loads two images concurrently, and calls
their RVA-addressed functions. It also observes ordinary executable image
bases through Windows debug events, checking successful and fail-fast exits
after relocation.
This remains a single-image executable artifact, not a DLL export surface,
module hot-swap mechanism or general-purpose loader.

PE layout, relative code/import fixups and unwind records are target-specific.
The same encoder's `NativeCode` result is consumed by both PE layout and the
[JIT execution proof](jit.md); code/data fixups and unwind ranges are explicit.
The [module proof](modules.md) persists typed semantic bodies and normalized
contracts, supports source binding against artifact declarations, verifies replacements,
then lowers at process start. Producer libraries need no artificial entry point;
unresolved imported declarations cannot reach the encoder. Static AOT
linking from these artifacts is separate from replacement of an unchanged PE.
There is no optimizer, debug-symbol format, native export/import API, managed
root map, production runtime or hot replacement. The broader LMOD design
remains separate from both PE and any optional CIL representation.

Authoritative ABI references:
[Windows x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention),
[unwind data](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64),
and [PE/COFF](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
