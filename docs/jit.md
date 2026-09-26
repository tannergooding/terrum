# Windows x64 JIT proof

The JIT consumes the same verified bound program and `NativeEmitter.EmitCode`
result as the PE writer. It is a second artifact consumer, not a second
instruction encoder, source interpreter or CIL/LLVM backend.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll jit samples\native\factorial.terrum --runtime fail-fast
$LASTEXITCODE # 720
```

`jit` is an execution worker, not an in-process compile command. It returns the
program's raw `int32` status, or zero for a void entry. `--json` formats compiler
diagnostics; a successfully executed program does not produce a success envelope
that could be confused with its own output. A caller running untrusted or
possibly nonterminating source must supervise the worker with a deadline.
Repeatable `--source <path>` options use the same source-set binding as `build`
and `analyze`. Additional inputs must be distinct file paths; only the first
input may use stdin. Multi-source compiler errors use schema 2 and identify
the owning input snapshot. Successful execution still returns only the program
status, not a JSON envelope.

## Code and memory contract

`NativeCode` contains text, a callable no-argument entry, a distinct AOT process
entry, function ranges/stack allocations, import displacements, and optional
initialized data with explicit read-only/writable partitions. Artifact
consumers patch RIP-relative references according to their own layout.
`BoundProgram` is verified before encoding; raw addresses are not part of the
source type system.

The JIT allocates private RW memory, writes text, import cells and unwind
records, applies fixups, and changes text/metadata pages to RX. Immutable source
and table buffers occupy read-only pages; token/output buffers occupy separate
RW pages. Nothing remains writable and executable. It flushes the instruction
cache before invocation.

Every emitted stack-adjusting function has a `RUNTIME_FUNCTION` and version-1
`UNWIND_INFO`. Registration uses `RtlAddFunctionTable`; the host verifies
`RtlLookupFunctionEntry` resolves each range before calling the entry point.
On normal return or host-side failure it unregisters before releasing the
allocation. Failed unregistration leaves memory allocated rather than freeing
storage still reachable by the operating system's unwind machinery.

The managed host calls the entry using the unmanaged Windows x64 ABI. Program
calls, stack arguments and runtime helpers use the encoder's existing ABI.
The data-return API copies result buffers before releasing native storage; no
native pointer or borrowed source descriptor escapes the invocation.

## Runtime boundaries

`--runtime fail-fast` remains explicit. Arithmetic, requirement and bounds
failures terminate the worker through `ExitProcess`, with the same profile
statuses as AOT; they are not translated into catchable host exceptions.
The test harness isolates such execution in bounded subprocesses. A native
failure can prevent normal resource cleanup in that worker; process termination
reclaims its address space.

`--runtime checked-exceptions` supports the same bounded recovery and cleanup
regions as AOT. Under CET, dynamic exception continuations require a trusted
remote registrar as well as the Windows continuation-registration API.
The CLI supplies its adjacent `terrumc.dll` registrar for source JIT,
`run-module`, and scanner/component execution; launching the apphost must not
silently drop that requirement. Fail-fast execution remains a distinct policy.

This proof has no executable-memory pooling, tiering, optimization,
concurrent code servicing, moving-GC maps, arbitrary managed interop or hot
unload. It does not make the public JIT execution API a sandbox.

Authoritative platform contracts:
[VirtualProtect](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotect),
[FlushInstructionCache](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-flushinstructioncache),
[RtlAddFunctionTable](https://learn.microsoft.com/en-us/windows/win32/api/winnt/nf-winnt-rtladdfunctiontable),
and [x64 unwind data](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64).
