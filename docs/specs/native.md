# Native execution boundaries

## Definitions and scope

A **native image** is target machine code and its required image/runtime data,
not a semantic-interchange artifact or the compiler's host process. **AOT**
emits an image before execution; **JIT** generates code for an admitted program
at an explicitly selected execution boundary. Neither changes the program's
source/runtime obligations.

This chapter retains native binding, call and code-lifetime obligations.
The [Windows x64 native profile](targets/windows-x64.md) owns complete scalar
storage, fixed rootless calls, stable projected borrows, owner transfer and
non-unwinding default foreign deployment. The bounded rules below retain
their admitted scalar/compact-value/owner scope. Neither scope selects a
general managed ABI or admits every source type at a foreign boundary.

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

Failure policy is explicit. The existing bounded fail-fast and checked-failure
implementations are distinct from a general checked-exception transport.
Source arithmetic, requirement, cleanup and output observations remain their
canonical language/runtime contracts; a target cannot define them merely by
inheriting a hardware trap or host exception.

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
OS unwind support does not define foreign exception-object interchange.
Default calls remain direct and non-unwinding; explicitly selected special
adapters under the [contained bridge](runtime/foreign-failure-bridge.md)
bear exception-interchange cost and obligations. The [selected local categories](runtime/standard-failures.md) still need
their actual runtime/physical codecs; other callback profiles are not supplied by
the native packet or OS tables.
