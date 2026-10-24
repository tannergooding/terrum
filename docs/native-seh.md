# Windows x64 exception ABI probe

This is an independently executed ABI check, not evidence for a general Terrum
exception ABI. It records the current legacy prototype and does not establish
conformance to the accepted explicit-unsafe-catch, normal-only Cleanup or
native-boundary schema; see [CG-003](implementation/conformance-gaps.md#cg-003)
and [CG-004](implementation/conformance-gaps.md#cg-004). The compiler now
separately emits an explicit bounded
`checked-exceptions` profile with a private, versioned four-reason identity,
Windows scope dispatch and exceptional cleanup; `fail-fast` retains its
original termination behavior. Compiler-generated source functions preserve
RBP with shared PE/JIT unwind metadata and keep outgoing stack arguments
RSP-relative. Cleanup funclets borrow the source frame through RDX; ordinary
exits call them explicitly, while checked exceptional exits dispatch them
through native unwind.

The [frame probe](../scripts/check-native-frame.cs), invoked with an explicit
built compiler as described in [native qualification](building.md#native-qualification),
separately loads a
compiler-generated DLL into a C caller. It checks normal cleanup, stack arguments
and published outputs, then inspects and unwinds foreign faults in both a source
function and a cleanup funclet. The latter deliberately receives an invalid parent
address: `RtlVirtualUnwind` must use the funclet's own RSP-based frame and restore
the caller's saved RBP, not treat its borrowed parent address as the unwind frame.
The enclosing C handlers, not Terrum, contain these test-only foreign faults.
Synthetic contexts additionally check both prologs before the RBP push, after
the push, after stack allocation and after setting the frame base. Each unwind
must recover the original return address, RSP and saved RBP.

From the repository root, with Visual Studio x64 C++ build tools installed:

```powershell
dotnet run --file scripts\check-native-seh.cs
```

The script discovers the installed toolchain with `vswhere`, builds
[`seh-probe.c`](../tests/Terrum.CodeAnalysis.Tests/Native/Unwind/Probes/seh-probe.c) with `/W4 /WX`, and executes it
in a ten-second supervised child. The executable is under
`artifacts\bin\native-probes\native-seh`, the compiler listing and object are
under `artifacts\obj\native-probes\native-seh`, and build/execution logs are
under `artifacts\log\native-probes\native-seh`. These fixed filenames are
overwritten on each run; the script does not write a completion marker.
The C compiler is a reference for this probe,
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
be modeled by eagerly running it in those first-pass search callbacks.
The bounded marker/reason filters above are native identity discriminators,
not authored source `when` predicates. The target
[source handler contract](specs/grammar-spec.md#33-statements-and-local-declarations)
instead unwinds to each candidate's live scope before testing its catches and
filters. Adopting that contract requires staged language-level dispatch or
another checked integration; the probe and current bounded dispatcher do not
establish it or permit substituting Windows' first-pass observable timing.

## Remaining integration

The compiler now has separate source verification of unsafe recovery,
exceptional cleanup, partial-output failure edges and module policy matching.
The shared AOT/JIT writer emits scope-bearing saved-RBP source records and
registered handler thunks under the checked policy. The independent probe
does not establish the compiler's source-to-code correctness: that is covered
by bounded AOT/JIT and persisted-package execution regressions. Neither proof
establishes general exception classes/identity, allocation or low-memory
behavior, GC roots, ownership cleanup or containment across arbitrary foreign
callbacks. Neither authorizes catching arbitrary Windows faults as Terrum
exceptions.
