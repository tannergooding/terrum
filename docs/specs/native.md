# Native execution boundaries

## Definitions and scope

A **native image** is target machine code and its required image/runtime data,
not a semantic-interchange artifact or the compiler's host process. **AOT**
emits an image before execution; **JIT** generates code for an admitted program
at an explicitly selected execution boundary. Neither changes the program's
source/runtime obligations.

This chapter retains native binding, call and code-lifetime obligations.
The [Windows x64 native profile](targets/windows-x64.md) owns complete scalar
storage, fixed rootless calls, stable projected reference inputs, owner transfer and
target exception-unwind rules. The bounded rules below retain
their admitted scalar/compact-value/owner scope. Neither scope selects a
general managed ABI or admits every source type at a foreign boundary.
The shared [definitions](language.md#definitions-and-notation) distinguish
copying a value, aliasing a referent, a checked reference and a raw pointer;
none of those explanations selects an additional native representation.

The optional [Windows x64 failure bridge](targets/windows-x64-failure-bridge.md)
separately specifies contained exception interchange and its matched
synchronous callbacks. It is a normal wire return with an owned failure
packet before local re-raise, not a foreign unwind through Terrum frames.

Development profile numbers, saved-IR revisions, native serialization,
emitter inventories and command examples are in the informative
[Windows x64 implementation reference](../implementation/native.md).
The [persistence taxonomy](representation/persistence.md) makes `.trmn`,
`.tnset` and `.trng` private packages, not weaker ABI contracts.

## Binding and image admission

An imported call retains its selected producer/definition and complete expected
contract. [Binding and replacement](representation/binding-and-replacement.md)
checks the applicable signature, access, effects, output state, ownership,
dependencies and target/runtime promises. Export names, DLL basenames,
equal-sized storage and implementation hashes do not establish that relation.

The selected target profile supplies exact value representations, argument and
result classification, reference/owner modes, unwind/failure transport and
required helpers. Missing support declines the crossing; it does not silently
allocate, box, copy a loan, add a retain, rebind source or choose a JIT fallback.
A platform loader's successful image load is not semantic admission or proof
that native instructions implement a declared body contract.

## Native declaration roles

The resolved Core declaration schemas `NativeImport`, `NativeCallable` and
`NativeExport` define distinct roles; matching names or an `extern` modifier
alone do not establish them.

`NativeImport` binds a static bodyless callable declaration or a directly
addressable static data field to a library symbol. Omitted `ResolutionMode`
means `Link`; `Dynamic` is explicit for either callables or data. Link mode
uses the configured native linker, which may select static or shared inputs.
Dynamic mode uses the selected target loader and retains the provider library
for the lifetime of the importing Terrum module/image, without a per-pointer
unload lease. Callable imports use the closed
`NativeCallingConvention` values `Unmanaged`, `Cdecl`, `Stdcall` and
`Vectorcall`; omitted `CallConv` means `Unmanaged`. Data imports have no call
convention. An omitted entry name is the exact declaration name, not an
inferred suffix or decoration; an explicit name supplies the exact target
symbol spelling. Instance-oriented APIs use an authored static import or
wrapper with an explicit receiver.

`ResolutionPolicy` applies only to Dynamic imports. Link imports encode no
runtime loader policy. The finite policies are `Default` and
`CompatibilitySearch`; omitted policy means `Default`. Each target follows
its documented loader behavior or rejects a policy it cannot implement.
On Windows, `Default` uses `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` (application
directory, System32 and registered user directories, with no current-directory
or `PATH` fallback); `CompatibilitySearch` uses the documented ordinary
unqualified loader search. On Linux, both currently use bare-name `dlopen`
search, including the process-start `LD_LIBRARY_PATH` input, because that API
does not provide a distinct per-call search flag. These names are not security
guarantees, arbitrary resolver hooks or permission to implement library-
specific fallback.

Native imports and native-callable invocation are caller-unsafe, including
scalar-only signatures and imported-data reads/writes. An `unsafe`
declaration propagates caller-unsafety; its body still needs explicit unsafe
authorization for unsafe operations. Imported data uses only the target's
directly interoperable scalar, pointer/function-pointer or `Blittable Copy`
representation. There is no implicit marshalling, rooting, pinning,
initialization, ownership transfer or lifetime extension. Provider
`FieldDef` mutability controls imported write authority: a read-only import
may bind to a mutable provider, but a writable import cannot bind to a
read-only provider. `mut` is not hardware page protection or a guarantee
against unsafe native writes.

`NativeCallable` makes an authored method's selected native signature and
effective convention stable for native function-pointer use; it does not
create an OS-visible symbol or an interface vtable. Interface-level
`NativeCallable` supplies the default convention to methods declared there;
inherited slots retain their declaring method's convention, and a method-level
contract overrides its own slot. Abstract interface methods have no
standalone code address; a conforming implementation may be addressed through
its concrete type. `NativeAddress` may form a pointer to a callable
`NativeImport` without an additional `NativeCallable` marker.

`NativeExport` explicitly publishes a static method or directly addressable
static data field as an OS-visible symbol, independently of Terrum visibility
or ordinary `.trrm` Export keys. A method export implies `NativeCallable` and
uses its one effective-convention contract; a field export exposes the
`FieldDef` storage, not a copy, and has no calling convention. Instance
methods and fields are not directly exported; authored static wrappers use
explicit receivers. Omitted export names use the exact declaration name,
subject to target symbol encoding. Duplicate output-image symbol names
reject.

The function-pointer type-use attribute
`[NativeCallConv(NativeCallingConvention.Cdecl)]` appears immediately before
`fnptr`. Its explicit closed enum argument is part of function-pointer type
identity; it is not an implicit cast or a convention on `func`. The same
enum is used for effective conventions on callable imports, `NativeCallable`
and method exports. There is no standalone `NativeAbi` role.

All native calls, member receivers, hidden result storage, explicit argument
order, generated frames, stack walking and unwind metadata follow the
selected target platform ABI for the effective call convention. There is no
universal member-call order or Terrum-specific stack-walk convention.
Terrum-generated code follows the target ABI, including only leaf-function,
optimized-code or other departures that ABI permits. A target mapping that
cannot be implemented completely rejects affected use.

Private values and code can change where the implementation controls and
regenerates both sides. Independently replaceable boundaries preserve their
published layout, ABI, identity and lifetime commitments. Load-time lowering
from persisted semantics is distinct from replacing a provider behind an
unchanged precompiled consumer. Neither facility implies live code patching.

## Native package identity and filename collisions

Logical `native-package-identity/1` distinguishes a native package's identity
from its mapped platform filename. A package identity is a nonempty ASCII
name satisfying the selected packager's published syntax/capacity contract.
Equality is exact ordinal, case-sensitive ASCII equality; there is no locale,
Unicode normalization or filesystem case folding. Foo and foo are distinct
identities. This does not redefine semantic Module/type identity or ordinary
file-path lookup, and does not standardize a private package's byte grammar.

A resolver matches the exact retained identity and complete provider
expectation. Duplicate/conflicting claims diagnose; input order or an
equal-folded basename cannot merge identities, pick a provider or authorize
replacement.

Separately, map the complete selected closure to actual target load paths and
reject collisions under that loader/filesystem's naming relation before
writing/staging images or starting affected execution. On Windows, distinct
Foo/foo identities mapped to terrum-Foo.dll and terrum-foo.dll collide in an
ordinary case-insensitive destination. Their semantic distinction remains;
the deployment is rejected, not silently merged, overwritten, renamed or
resolved by argument order. The actual target path relation also governs
other filename aliases; an ASCII lowercase comparison is not a complete
portable filesystem model.

Successful distinct filenames do not establish semantic identity, compatible
replacement, safe loading or code lifetime. An explicitly different supported
mapping needs its own declared contract; the default does not invent one.

## Bounded Windows x64 call rules

The call boundary uses caller shadow space and aligned calls. The first four
integer-carrier argument positions use RCX, RDX, R8 and R9; supported
floating carriers use XMM0 through XMM3 at those same shared physical
positions, not a separate count. Later positions use stack slots.
Nested calls preserve already evaluated argument/value
snapshots and the target's required nonvolatile state. Private frame layout,
spill selection and encoder object shapes are not public ABI commitments.

Supported aggregate sizes of 1, 2, 4 and 8 bytes use integer carriers. Other
supported compact sizes use pointers to caller-owned 16-byte-aligned copies.
An indirect result receives a hidden first argument in RCX, shifting explicit
argument positions; the callee returns that storage address in RAX.
These addresses represent backend-owned argument/result storage, not escaping
language pointers or a loan of the original by-value argument.

An admitted one-field scalar Transparent wrapper inherits its carrier's direct
classification while retaining nominal type and validity. Equal size or a
one-field source declaration alone does not establish that commitment.
Unit parameters/results carry no payload while preserving their logical
signature under [zero-data ABI rules](data-layout-and-abi.md#zero-data-values-and-void).

The bounded readonly unique instance receiver is a pointer to the live owner,
including 4- and 8-byte owners; it is not an owned by-value argument or a Copy
conversion. It composes with hidden results and stack arguments. Cleanup
instead consumes its receiver through the admitted aggregate value ABI.
Indirect cleanup arguments use distinct supporting storage so a parent
cleanup's writes to its consumed argument cannot alter the original child
payload needed by later child cleanup.

Output publication follows the exact declared state and completion contract.
The bounded true-only initialization-output forms use separate exact-size
caller storage and publish only on their successful normal result. Failure
does not make partial bytes a readable value or manufacture a default.
This physical convention does not redefine the more general normal-failure
output states in [references](language/references.md) or
[structural flow](language/initialization-and-flow.md).

The [canonical call interface](targets/windows-x64-call-interface.md) and
[data layout](targets/windows-x64-data-layout.md) specify the complete admitted
floating/packed-field rules and distinguish foreign mappings from nonforeign
scalar defaults. Variadic, callback, managed-reference and general view/task
crossings still require separate complete target/runtime contracts.

## Runtime and code lifetime

Source arithmetic, requirement, cleanup and output observations remain their
canonical language/runtime contracts; a target cannot define them merely by
inheriting a hardware trap or host exception. Explicit unsafe
`try`/`catch` handles modeled local failures without a runtime-policy opt-in;
uncaught failures may still fail fast.

Code and its required helpers, unwind data, collectors and cleanup providers
remain valid while any admitted call, retained value, root or cleanup
obligation can use them. Unloading an image invalidates its function pointers;
private packaging does not make unload safe while those dependencies remain.
Exact definition/type origin is distinct from the intermediate image that
forwards an owning value.

[Windows code publication](targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support) retains write/execute
separation, fixup and instruction-cache requirements and unwind-registration
lifetime. Failed deregistration cannot justify releasing still-registered code.
Process-start regeneration, supervised execution and a private staging
directory are not sandboxing, authentication or a hot-unload protocol.

The Windows profile [identifies its exact imported platform portions](targets/windows-x64.md#3-imported-platform-rules).
Its OS unwind support supplies frame recovery, not exception identity. Native
imports remain direct calls; the separate `NativeExceptionUnwind` capability
permits supported native exceptions to unwind inward and be normalized before
source catch matching. Terrum exceptions do not unwind outward through native
frames; native exports and callbacks contain reverse-boundary propagation.
The explicitly selected [contained bridge](runtime/foreign-failure-bridge.md)
remains a distinct paired normal-return protocol and does not define this
native unwind path.

## Native exception objects

An admitted native event is normalized to an established Terrum exception
class or to the opaque Core `NativeException` class implementing
`IException<NativeException>`. Its stable Terrum-owned snapshot exposes
read-only `TargetProfileId: u32`, `TargetProfileRuleRevision: u32`,
`EventDomain: Text`, `EventCode: nuint` and `Message` members. The fallback
message is `A native exception occurred.`; warranted descriptive text may
also be Terrum-owned, but foreign exception text is never copied. The event
code uses native pointer width and is interpreted by its target-defined
domain. These instance values are not serialized in `.trrm`.

The caller-unsafe `GetNativeContext()` returns opaque `ptr<void>?`, or `null`
when the target/event cannot safely expose a context. A non-null pointer's
availability, lifetime and copy requirements follow the target ABI; the
stable exception snapshot does not retain the native context. Unsafe access
does not waive known lifetime, provenance or access constraints. Windows x64
normalizes ordinary SEH events and defines its dedicated mappings in the
[target profile](targets/windows-x64.md#6-native-exception-unwinding-and-normalization).
An uncaught normalized event fails fast. Process termination, forced unwinds,
`longjmp` and cancellation are excluded; stack overflow is catchable only
after unwinding has recovered enough stack for exception construction and
handling.

Native exception propagation is not a caller-propagated language effect.
Matching a call convention alone does not admit exception unwinding; the
selected target ABI and runtime must support the complete feature before
affected use.
