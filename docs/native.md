# Windows x64 native profile

`native-1` is a deliberately bounded executable language profile, separate from
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

## Executable language

| Area | Supported |
|---|---|
| Program | One source file; top-level functions; parameterless `int32 Main()` or `void Main()` |
| Types | `int32`, `bool`, `void` results; no implicit typed numeric conversions |
| Literals | Contextual decimal integer literals and explicit `i32` tags; booleans; unit `()` |
| Variables | Initialized typed/inferred locals; immutable bindings by default, explicit `mut` assignments; lexical scopes |
| Expressions | Direct calls, forward calls/recursion, parentheses; unary `+`, checked `-`, `~`, `!` |
| Operators | Checked integer `+ - * / %`, bitwise operations, signed comparisons, same-type equality, boolean operations |
| Flow | Blocks, `if`/`else`, `while`, return, boolean `require`; left-to-right argument/operand evaluation and short-circuit `&&`/`||` |

Literal context comes from declarations, parameters, return types or a known
operand, not an implicit `int32` fallback. `var x = 1` is rejected; `var x = 1i32`
and `int32 x = 1` are accepted. A signed minimum literal can be constructed
directly, without first overflowing its positive magnitude. Locals cannot be
read before declaration/initialization, immutable bindings cannot be assigned,
conditions must be boolean and non-void functions must return on all validated
paths (loops are conservatively allowed to execute zero times). Function
overloading, function modifiers, namespace/using directives, active-scope local
shadowing and argument names/passing modes are not supported.

Other integer widths, unsigned arithmetic, floating point, casts, character/text
values, shifts, structs/classes, optional/generic/family binding, borrowed references,
conditional `out`, ownership/cleanup, exceptions, GC, async, interop, imports and
libraries are outside the profile. Their syntax may be inspectable, but a build
must diagnose unsupported semantics. No source library is silently assumed.

## Runtime policy

The language's eventual ordinary runtime uses exceptions for exceptional
failures. This executable profile has **no catch/cleanup runtime** and accepts
only explicit `fail-fast` selection. Checked arithmetic remains checked:

| Failure | Process exit status |
|---|---|
| Signed arithmetic overflow, including minimum divided by -1 | `0xE0540001` |
| Integer division or remainder by zero | `0xE0540002` |
| A false boolean `require` predicate | `0xE0540003` |

`require predicate;` evaluates its boolean once and continues only on true.
This is executable semantic code, not a debug assertion, unchecked assumption
or inferred public precondition. Its ordinary language exception contract is
not yet available: **the explicit fail-fast profile terminates instead**.
Conditional outputs and exceptional cleanup remain unsupported rather than
being approximated. A predicate's own arithmetic failure retains that failure
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

`Compilation.Create(SyntaxTree)` declares names/signatures before binding bodies.
It returns deterministic diagnostics and no `BoundProgram` on errors.
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
64 arguments, 128 levels of IR nesting, 100,000 IR nodes, 3,840-byte frames and
8 MiB of machine code. Valid programs exceeding emission limits report
`TRM4001`; these are implementation limits, not language type or ABI guarantees.

The x64 backend uses fixed stack frames, caller shadow space and aligned calls.
The first four integer/bool arguments use RCX, RDX, R8 and R9; later arguments
occupy stack slots. Live intermediate values and arguments are spilled across
nested calls. Functions preserve nonvolatile registers and publish Windows
unwind metadata for stack adjustment; those records support platform stack
walking, not Terrum exception handling.

Images currently use fixed base `0x140000000`, 4 KiB section alignment and
512-byte file alignment. `.text` is read/execute, `.idata` read/write and
`.pdata`/`.xdata` read-only. NX is enabled; ASLR is not advertised and base
relocations are not emitted. This is a single-image development artifact, not
a hardened or freely relocatable production/module format.

PE layout, relative code/import fixups and unwind records are target-specific.
There is no optimizer, debug-symbol format, exported native API, managed root
map, module loader, hot replacement or JIT registration yet. These need distinct
contracts rather than extending a single-image executable format by accident.
The design retains a future independently versioned semantic module/contract
format separate from both PE and any optional CIL representation.

Authoritative ABI references:
[Windows x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention),
[unwind data](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64),
and [PE/COFF](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
