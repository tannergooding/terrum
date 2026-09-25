# Windows x64 exception ABI probe

This is an independently executed ABI prerequisite, **not implemented Terrum
exception syntax or a catchable compiler runtime**. Current Terrum artifacts
still require the explicit fail-fast policy.

From the repository root, with Visual Studio x64 C++ build tools installed:

```powershell
dotnet run --file scripts\check-native-seh.cs
```

The script discovers the installed toolchain with `vswhere`, builds
[`seh-probe.c`](../tests/native/seh-probe.c) with `/W4 /WX`, and executes it
in a ten-second supervised child. The compiler listing and build/execution
logs remain under `artifacts`. The C compiler is a reference for this probe,
not a Terrum backend dependency.

The fixture executes the same eight cases in a C `__try`/`__except`/`__finally`
function and a manually encoded x64 function:

| Path | Result | Observed order |
|---|---|---|
| Normal completion | 10 | Finally |
| Matching noncontinuable failure raised by a C callee | 20 | Filter, finally, selected handler |
| Foreign noncontinuable failure | 30 | Filter declines, finally, outer C handler |
| Same numeric failure code with mismatched marker, version, reason, payload length or continuable flag | 30 | Filter declines, finally, outer C handler |

The dynamic filter receives `EXCEPTION_POINTERS*` in RCX and the establisher
frame in RDX. The termination callback receives abnormal-termination state in
CL and the establisher frame in RDX. Both read a known initialized local at
frame offset 32; finally updates it and the selected handler verifies the
update before producing its result. The frame reserves 32 bytes of shadow
space and uses the compiler's existing seven-byte `sub rsp, imm32` prolog.
The probe checks noncontinuable exception flags, not just numeric codes.

## Encoded contract

The generated function uses version-1 `UNWIND_INFO`, flags
`UNW_FLAG_EHANDLER | UNW_FLAG_UHANDLER`, and `UWOP_ALLOC_LARGE` for its
56-byte frame. After the aligned unwind-code array comes the language-handler
RVA, followed by a scope count and 16-byte scope records:
`BeginAddress`, `EndAddress`, `HandlerAddress`, `JumpTarget`. All are relative
to the registered image base. The inner finally record has a zero jump target;
the enclosing catch record identifies a filter and landing pad. The C compiler's
emitted listing independently shows that structure and inner-before-outer order.

The matching filter checks a versioned marker and failure reason in the
exception information array, its exact argument count and noncontinuable
flag, in addition to the numeric exception code. An independent outer C
filter observes each deliberately mismatched payload without the inner
dynamic handler consuming it. This is identity discrimination, not an
authentication or unforgeability guarantee.

The handler is the actual `__C_specific_handler` export discovered in the
running Windows `ntdll.dll`. In-allocation leaf tail-jump trampolines make its
address and the filter/finally callbacks reachable by RVA without assuming an
external DLL falls within the dynamic allocation. Absolute callback pointers
live in a separate read-only page. Text is written while non-executable, changed
to executable/read-only, and flushed before registration/execution. The probe
checks the resulting page protections and `RtlLookupFunctionEntry` result.
It unregisters the function table, verifies removal, and frees memory only
after all callbacks and invocation frames have completed.

Normal-exit cleanup is emitted as an explicit call. Unwind metadata alone does
not run finally on a normal return. During exceptional propagation, Windows
performs filter search before unwind cleanup; source cleanup therefore cannot
be modeled by eagerly running it before filter selection.

## Remaining integration

This proof does not establish Terrum exception identity, allocation/low-memory
behavior, unsafe recovery checking, structured cleanup and partial-output
verification, compiler-generated funclets, GC roots, module exception contracts,
or containment at foreign callback boundaries. It also does not authorize
catching arbitrary Windows faults as Terrum exceptions. Those semantics must
be implemented before offering a different runtime policy. The shared AOT/JIT
unwind writer currently emits the existing no-handler allocation record only;
this probe remains separate until structured regions can supply verified
handler and scope metadata.
