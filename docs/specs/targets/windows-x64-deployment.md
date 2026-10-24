# Windows x64 deployment and support

## 1. Register and control-state preservation

Default calls preserve RBX, RBP, RDI, RSI, RSP and R12 through R15.
XMM6 through XMM15 preserve their low 128 bits.
RAX, RCX, RDX, R8 through R11 and XMM0 through XMM5 are volatile.
Applicable upper YMM/ZMM portions and additional registers follow the
[imported platform rules](windows-x64.md#3-imported-platform-rules);
availability of an optional ISA feature is not baseline support.
The direction flag is clear on entry/exit and before platform/runtime calls.

The x87 stack/MMX payload is not an argument/result convention.
The x87 control word is nonvolatile. MXCSR status bits 0 through 5 are
volatile; bits 6 through 15 are nonvolatile. The ordinary execution
environment has nearest/ties-even rounding, masked floating exceptions,
denormals-are-zero off and flush-to-zero off. Source arithmetic still obeys
its exact format, subnormal and failure rules; ambient hardware state cannot
replace them.

A function changing nonvolatile control fields restores them before return
and on an admitted internal unwind. A caller that changed them restores the
standard call environment before an ordinary call, unless an explicitly
selected compatible special contract says otherwise.
The [language floating-environment contract](../language/floating-environment.md)
supplies no ambient control-word or status-flag API, and does not promise
to reproduce, clear or preserve incidental volatile status flags.
This does not weaken the nonvolatile control/call obligations above.
An implementation can avoid saves where it provably leaves the fields
unchanged; it cannot canonicalize copied floating bits to avoid the work.

## 2. Direct calls and exception boundaries

Native imports and exports use the direct selected platform call interface,
with no hidden exception-state argument, automatic catch/translation thunk
or altered scalar carrier. Under the separate `NativeExceptionUnwind`
capability, a native exception from an imported call may unwind inward
through Terrum frames using the Windows ABI/SEH contract and is normalized
before source catch matching. This requires the exact platform/runtime
support; it is not selected by a `NoUnwind` attribute, an Effects bit or a
per-import adapter. Caller-unsafety and the truth of published native
contracts remain independent obligations.

Expected normal failures use explicitly matched return/status and output
relations. Their effects, partial cleanup and initialized/vacant outcomes
remain distinguishable from checked failure, cancellation or fatal host
failure. A false or nonzero integer is not a universal error convention.
No handler guesses a failure from partially written output.

The separately selected
[boundary-contained failure bridge](windows-x64-failure-bridge.md)
remains a distinct paired normal-return protocol. Its explicit adapter can
translate a supported exception domain to a normal result or return an owned
failure packet. It does not define the native inbound unwind path, and its
special contract is not charged to ordinary calls.

Terrum exceptions do not unwind outward through native frames. Native exports
and callbacks contain reverse-boundary propagation; no producer-language
exception object or managed exception payload is passed through. Windows SEH
events are normalized under the
[target exception mapping](windows-x64.md#6-native-exception-unwinding-and-normalization).
Uncaught normalized events may fail fast. Process termination, forced
unwinds, `longjmp` and cancellation remain excluded.

## 3. Native image, stack and unwind support

An emitted Windows executable/library uses the applicable AMD64 PE32+
image contract; native object files use the imported AMD64 COFF contract.
Validate used header/directory framing, checked image/section extents,
RVA-to-address conversion, relocations, imports/exports and exception data
under that platform contract before using them.
The image's platform entry address is not Module.entryMethod.

A leaf with no nonvolatile/RSP changes can use the platform's leaf unwind
rule. Functions allocating stack, calling other functions, saving nonvolatile
state or requiring handler support have accurate function/unwind records.
Each RUNTIME_FUNCTION table entry is twelve bytes: three u32 image-relative
addresses for the inclusive code start, exclusive code end and UNWIND_INFO.
Entries are DWORD-aligned in memory and sorted by code start. Code start/end
addresses need not be DWORD-aligned; they describe the actual byte extents.
UNWIND_INFO is separately DWORD-aligned. In version 1, the code array's
two-byte slots are padded to an even slot count, without counting that padding
as an operation; a chained tail is another twelve-byte RUNTIME_FUNCTION.
Other UNWIND_INFO/UNWIND_CODE framing, operation operands, slot counts,
prolog offsets and chains follow the imported revision.
Unsupported versions or unrepresentable ranges decline; reserved values are
not private Terrum extension fields.

Prologs/epilogs and their records describe actual nonvolatile/stack changes
at every supported instruction point, not just a normal-return snapshot.
Guarded stack allocation probes the complete required range before accessing
new pages, preserving incoming arguments and nonvolatile state.
Platform page/probing requirements are not source-language capacity minima
or a universal 4096-byte memory-model constant.

Dynamically generated code registers its complete function tables before
calls can reach it and retains code, records and registration backing while
the OS/runtime can use them. Code publication also completes fixups,
write/execute separation and instruction-cache requirements.
Retirement deregisters only after all relevant uses end; failure to
deregister prevents releasing still-registered backing.
Unwind tables support stack walking, platform exception dispatch under the
admitted native-unwind capability, and admitted internal cleanup. They do not
encode a foreign exception object or enable hot patching.

## 4. Selected closure, origin and replacement

The selected deployment contains one actual admitted consumer and its complete
retained provider closure, whether flat or transitive. Each edge preserves
the exact module/definition/type identity and directed expected/provider
relation, including target, runtime, source access, carrier, effects, output,
ownership, dependencies and helpers.
Candidate catalogs/search policies are separate; ambiguity is not resolved
by provider argument order, latest version, DLL basename or row order.

A finite dependency cycle is not automatically rejected as a physical import
cycle, nor does it establish a valid initialization, cleanup or load schedule.
The actual selected closure needs supported platform loading and the complete
language initialization/dependency contract. A resolver with a narrower
topology declines unsupported graphs before affected execution.
This profile introduces no graph package, resolver algorithm or numeric
candidate limit.

Nominal/cleanup origin survives intermediate forwarding. An owner originating
in Leaf remains Leaf's exact type and cleanup/allocator responsibility even
when Middle returns it to Root. Every surviving field, loan, allocation,
cleanup thunk, function pointer and helper can retain its responsible image
or runtime domain without an emitted direct machine import.
Closing a call, returning from Middle or emptying an IAT does not discharge
those obligations.

Storage release uses the actual allocator/release pairing and alignment
contract. Equal CRT names, process address space or pointer bits do not make
allocation domains interchangeable. Transfer does not require a hidden
per-value retain/registry; the admitted program/runtime instead establishes
the necessary domain and code lifetime from its declared obligations.
Managed heap/root sharing and cross-image task/view combinations remain
separately gated.

Compatible replacement can preserve an unchanged consumer only while every
retained expectation and actually relied-on stronger provider fact survives.
Changed fixed layout, carrier, validity, source identity, output state,
cleanup/order/allocator domain, capture, failure or required helper support
is not repaired by identical exports.
Private implementation changes are permitted only behind preserved
observable contracts. Persisted-semantic relowering, precompiled native
replacement and live patching remain different consumption modes.

No image, code/helper, unwind record or allocator domain retires while a live
call, value, function pointer, registered table, retained source or cleanup
can still use it. An independently verified quiescence/unload protocol is
needed for early unload; private packaging does not supply one.

## 5. Invocation and process-launch boundary

A host selects the logical Module and exact entry declaration under
[module identity rules](../il-metadata.md).
It admits the selected definition/body or its resolved import, complete
closed signature, arguments, outputs, effects and runtime before invocation.
Neither a same-spelled Main nor a bodyless unbound MethodDef becomes an entry.
An owned MethodDef reference alone does not promise a callable machine body.

Invocation uses the call interface for the exact admitted signature.
An instance receiver, input/output value, required factory or failure
transport must be explicitly provided; it is not guessed from C/CLR startup
conventions. The host retains entry-result ownership until it consumes or
retires it under that contract.

A process-launch adapter separately connects the platform PE entry/startup
contract to that selected invocation. Its published launch contract supplies
input construction, result-to-process-status mapping, failures, cleanup and
the lifetime of startup/runtime providers. With no such admitted adapter,
successful PE loading or semantic entry selection is not a complete process
launch. This profile assigns no default argv/text allocation, C Main shape,
CLI status mapping, implicit Task wait or unbound-import fallback.

This separates exact machine calls from the still separately selected
application/tooling startup surface. Private native packages and a CLI run
command do not silently fill that contract.
