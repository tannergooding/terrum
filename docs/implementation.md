# Implementation support

Terrum's accepted target contracts and its implemented subset are tracked
separately. The [specifications](specs/README.md) define intended behavior;
the [roadmap](roadmap.md) lists pending contracts and dependencies; and
[proposals](proposals/README.md) contain unapproved choices.

The [library support inventory](implementation/libraries.md) keeps bounded
examples and API sketches separate from the selected library/protocol
inventory and its provider/consumer contracts.

The [development JIT proof](implementation/jit.md) records the implemented
process-start worker and private backend/memory behavior.
[Bounded layout directions](implementation/layout-profiles.md) retain the
initial public-layout slice and module/replacement observations without
narrowing the selected target's transitive eligibility or assigning a
standard target/Feature identity.

The [bounded conversion inventory](implementation/conversion-profiles.md)
keeps scalar-front-end projections and legacy output support outside the
source literal/conversion rules.

## Hosted compiler

The compiler host is written in C#. It exposes a Unicode-aware, lossless
syntax API and a bounded Windows x64 native profile with direct PE emission
and a JIT worker. The implemented language subset includes checked scalar
operations, compact values and unique owners, conditional outputs, bounded
generics, managed roots/classes, and deterministic cleanup. Hosted calls also
support non-unique, handle-free `Copy`-struct `out` values on non-Boolean and
`void` functions across semantic modules, native libraries and native graphs;
compilation-local `[Copy]` templates support the bounded generic `out T` and
`init ref<T>` profiles with fixed `int32`, `uint32`, `int64`, `uint64`,
`char16`, or `void` results, plus the mixed all-generic-output profile.
Single-mode generic outputs also support fixed `float32` and `float64` results
without fixed side outputs. Generic initialization outputs retain
compilation-private managed-handle support. Fixed scalar side outputs are
supported with the non-floating result set and one generic output mode. The
accepted bounded floating-result profile also combines mixed generic output
modes with fixed scalar side outputs. Compilation-local `[Copy]` templates
also accept concrete `float32` and `float64` generic arguments, forwarding and
publishing their substituted values under existing rules. This grants no
symbolic floating operations. Generic proofs hydrate floating leaves only for
reached non-unique `Copy`-struct descriptors and concrete non-class callable
signatures; managed-class dependency closures remain separate. Public generic
exports and executable TRRM support remain separate. The native backend emits
Terrum-owned code; CIL is neither a dependency nor the default.

Required constants and static proofs currently use the hosted scalar evaluator:
`bool`, `char16`, and fixed-width integers with eligible `[ConstEval]` bodies
supporting scalar locals, assignments, branches, loops, returns, and calls to
other `[ConstEval]` functions. Its implementation limits are 4,096 interpreted
statement steps and a call-depth limit of 64; these are not portable
guarantees. Static proof support does not yet implement a general built-in
predicate-refinement catalog for runtime values, so it does not fulfill the
accepted v1 proof-floor scope.

Current profile markers are syntax language version 46, semantic-module base
revision 76 with private feature revisions through 83, and native profiles
native-10 through native-20. These markers describe supported implementations,
not approval of the remaining language contracts. The exact source, layout,
metadata, ABI, and runtime contracts are in the
[specification index](specs/README.md).
Public fixed-layout source support is implemented for natural Windows x64
`Sequential` and scalar-or-raw-pointer-carrier `Transparent` layouts committed
with `[Blittable]`. Mandatory verification recomputes the fixed-storage facts,
and private semantic modules persist them. Packing, alignment controls,
and standard TRRM layout persistence remain unsupported. Raw pointer fields
carry only their declared address bits, not pointee validity, backing,
ownership, or lifetime.
Direct `NativeParameter` inputs can point at committed aggregates in source,
private semantic modules, and native execution. The canonical standard TRRM
`NativeParameter` encoding remains unapproved; the provisional executable
experiment is described below. Direct native outputs remain limited to scalar
and raw-pointer pointees.
The detailed [hosted output profiles](implementation/output-profiles.md)
are informative development inventories. Their historical `out` modes do not
add parameter forms to the canonical reference model.
The [hosted view and owner profiles](implementation/view-and-owner-profiles.md)
record bounded TokenCursor storage, owner/field loans and cleanup projections;
their restrictions do not limit the general source dependency contracts.
The [sample catalog](samples.md) distinguishes runnable programs from design
sketches.
The [experimental CLI response reference](implementation/cli-protocol.md)
preserves current schema-1/schema-2 and raw execution-worker behavior.
It does not claim implementation of the independently selected
[terrum.cli/1 supervisor protocol](specs/cli-protocol.md).

## Persistent and self-hosted components

Only [`.trrm` semantic interchange](specs/representation/persistence.md) has
Terrum-standard serialized producer/reader conformance. Current saved
structured IR and native package/set/graph serializations are private.
Their detailed [native profile](implementation/native.md),
[module](implementation/modules.md), [set](implementation/module-sets.md)
and [graph](implementation/native-graph.md) references preserve current
revisions, reader limits and commands. The
[native execution specification](specs/native.md) separately retains ABI,
binding, ownership and code-lifetime obligations.

The additive TRRM scalar path emits approved metadata and compact-linear or
indexed Bodies for a bounded source-free, fail-fast scalar subset. It uses the
shared hosted verifier and backend; general imported-call linking and checked
exception Body support are not implemented.
This inventory describes its development encoding, not verified support for
the [unified registry](specs/representation/registry.md). The
[encoding history](implementation/trrm-encoding-history.md) distinguishes
retired floating/generic numbers from current normative assignments.

Terrum-authored scanner and scalar-frontend components are bounded candidates,
not a bootstrap compiler. The scalar frontend audits source projections before
passing them to the hosted verifier/emitter. Its support does not imply that
verification or emission has moved into Terrum. The source-free compiler
primitives DLL is likewise distinct from source-grant companion profiles.
See the [self-host component inventory](../self-host/README.md) and
[scanner boundary](scanner.md).

Some source-grant and invocation-profile extensions are implemented only
provisionally under unapproved proposals. They do not change the accepted
source-free ABI or establish general source-dependent DLL, graph, module-set,
or persisted-AOT support. The [proposal index](proposals/README.md) records
those boundaries and pending promotion decisions.

The first Windows x64 source-native-import slice is also implemented
Native DLL imports are a provisional source feature under the unapproved
[TRRM native interop proposal](proposals/trrm-native-interop-metadata.md).
Source declarations name a library and export directly and carry generic
pointer access, retention, output-validity, and unsafe-caller contracts; they
do not select a compiler-owned Win32, DX11, or native-import catalog.
`build-native-import` explicitly selects the Windows x64 native-import emitter;
ordinary `build` does not emit source imports. Fixed-array local storage is
admitted by ordinary source compilation for fixed-width scalar or committed
Blittable Windows x64 elements,
with a positive extent and 4096-byte total limit. It does not depend on
native-import emission mode. The carrier is local-only and initializers must
fully initialize each element, except empty scalar arrays which zero-initialize.
Checked element addresses retain their extent bounds. Current lowering remains
limited to the native import AOT emitter; private semantic modules, native
modules, JIT, and standard TRRM continue to reject fixed-array expressions.
The `MessageBoxW` sample is authored using the same direct import form as any
other native function; its unattended CLI test
checks the emitted import without invoking the modal API. This remains
provisional implementation support, not canonical TRRM approval or a
cross-platform FFI contract. Source-level typed and nested `ptr<T>` signatures
and unsafe pointer address/read/write nodes persist through private semantic
module revision 83. Typed pointer formation can take the address of an element
in an initialized fixed-array local; native lowering retains its checked
element bounds. Fixed-array expressions, including those pointer addresses,
remain unsupported by private semantic modules. Standard TRRM does not encode
these pointer tables or nodes; that private support does not extend the
standard format.

A separate prototype under the unapproved
[TRRM native interop proposal](proposals/trrm-native-interop-metadata.md)
supports two-string `Terrum.Interop.NativeImport` declarations with
`Terrum.Interop.NativeOutput` for result-correlated writable scalar pointers
and opaque `ptr<void>` outputs through `write ptr<ptr<void>>`. Direct imports
also admit supported scalar results and opaque `ptr<void>` results; typed
pointer tables remain rejected by this standard TRRM prototype.
The default output condition is nonzero; `NativeOutput(ordinal, NonNegative)`
provisionally expresses signed nonnegative status results such as HRESULT
success. The latter
pointer output represents only unmanaged pointer bits; it adds no backing,
provenance, ownership, pinning, or cleanup guarantee. The prototype carries the raw
import's caller-unsafety and direct representation through a provisional TRRM
NativeImport table and source-reference path; that standard path is limited to
named Windows System32 imports. Its section, clause, and Feature assignments
are not canonical TRRM assignments. The source prototype also accepts
`NativeParameter` for raw pointer inputs with explicit `Read`, `Write`, or
`ReadWrite` access, `NoEscape` retention, and extent zero or one for supported
scalar, raw-pointer, and fixed-layout Blittable aggregate pointees. Extent zero
does not prove a buffer length, string termination, or any other property of
the foreign access. Aggregate addresses require an initialized local; writable
access additionally requires mutable backing. Windows `HWND`/`HINSTANCE`
aliases do not receive generic direct-import parameter admission; spell those
ABI values as explicitly contracted pointers. `NativeOutput` remains limited
to scalar and raw-pointer pointees, and `ptr<void>` input values are limited to
readonly, zero-extent metadata. Direct pointer arguments also accept `null`;
matching `utf8z`/`utf16z` literals require read-only, no-escape, zero-extent
metadata and do not require a selected API catalog. When a call forms an
address with `&local` or the first element of a fixed array, the backing must be initialized
and match the pointee type; `Write` and `ReadWrite` require mutable backing.
Raw pointer values remain caller-authored unsafe inputs. Caller-unsafe source
functions may return opaque `ptr<void>` values through private semantic
modules. This preserves caller-unsafety and address bits only; it conveys no
provenance, backing, ownership, or lifetime, and source-body pointer results
remain outside the current TRRM executable prototype. This input metadata
round-trips through private semantic-module revision 80, native packages,
consumer sets, and graph-JIT roots. The experimental TRRM direct-import path
also round-trips scalar, opaque-pointer, and `ptr<void>` pointees, plus
supported scalar and `ptr<void>` results, using provisional clause kind 10.
It includes extent-zero scalar and character-pointer inputs and explicitly
committed `ptr<TypeDef>` aggregate pointees using provisional feature ID 4 and
contract kind 12. The loader recomputes supported scalar/nested-struct layouts,
and `CreateReference` reconstructs their identities for consumer binding.
This path is limited to declaration-only import libraries; pointer fields,
function-pointer fields, aggregate bodies, by-value aggregate ABI, and
aggregate outputs remain unsupported. These assignments remain unapproved
and do not establish canonical TRRM conformance.

Direct imports with writable scalar outputs and `NativeParameter` inputs also
round-trip through private semantic producer/consumer and native package
paths. Their PE imports are checked against retained calls and kept separate
from selected provider dependencies. Active synthetic coverage validates
single-provider and consumer-set pairs plus a graph consumer/provider pair.
Standalone and graph NativeJit resolve direct imports from the consumer root
only by absolute System32 path; graph and consumer-set execution do not stage
arbitrary raw-import DLLs. Runtime regressions exercise
`CompareStringOrdinal` with readonly local `char16` inputs and
`GetCurrentThreadId` in standalone and graph JIT paths. Arbitrary
non-System32 imports and COM runtime behavior remain unqualified.

An additional, provisional prototype supports `[Terrum.Interop.NativeCallable]`
functions and explicit `fnptr<NativeAbi, ...>` values on Windows x64. It
retains concrete signatures through address formation, local/field transport,
indirect calls, private semantic modules (revision 82), native module sets and
graph consumers. Address formation and indirect invocation require an explicit
unsafe scope; native-callable entries are `NoThrow` and caller-unsafe. Function
pointers carry code addresses only: they do not root or extend the lifetime of
the image containing the target. A supervised independent C caller exercises
the exported native ABI, and JIT/module regressions exercise provider callback
transport. Standard TRRM explicitly rejects this feature until its declaration,
type, and body records are reviewed; the source forms and private revision are
not normative grammar or interchange assignments.

The source-only prototype also supports a generic numeric-slot vtable call
declaration with an explicit receiver and ordinary Windows x64 argument
positions. It forwards scalar, pointer, function-pointer and output arguments
without inferring interface identity, ownership, or provider behavior.
Supervised native execution covers mixed register/stack arguments, floating
arguments and results, and output publication. The provisional
`NativeVTableMethod` spelling is not normative. Private semantic-module
revision 84 persists and revalidates the slot and parameter contract; exported
methods are exposed to consumers as ordinary caller-unsafe external functions.
Native module packages use the same producer/consumer path. Standard TRRM
continues to reject these declarations until its metadata contract is reviewed.

The earlier COM-specific source metadata path has been retired. COM-shaped
attributes have no compiler-owned meaning; unsupported qualified interop
attributes use the generic unsupported-attribute diagnostic. Compiler-owned
interface identity, owner/acquisition, reference-count, hidden-parameter,
argument-adaptation, and mapped-buffer behavior is not supported. The
provisional TRRM COM feature and contract kinds 7-9 and 11 are not part of the
accepted format. No canonical COM metadata records have been assigned.

`NativeParameter` remains a generic pointer access/retention/extent annotation
for native declarations, and `NativeOutput` can describe result-correlated
output validity. These annotations do not prove foreign-provider behavior or
confer ownership. Writable `ptr<T>` values remain ABI-shaped pointer values;
without an output contract the compiler does not infer that the pointee becomes
initialized.

The maintained DX11 sample authors its Win32 imports, ABI layouts, callbacks,
message loop, and resource cleanup in Terrum source. The compiler's remaining
native mechanisms are generic direct imports, pointers, function pointers,
fixed layouts, and numeric-slot vtable calls; vtable calls do not infer an
interface identity, ownership, or provider behavior. DX11 generalization is
not yet accepted: WARP/hardware execution remains unqualified, and earlier
COM-specific DX11 prototype tests remain excluded from the active project
pending conversion or retirement. These are explicit evidence gaps, not
reasons to restore sample-specific compiler policy.

## Syntax and CLI

The public C# syntax APIs are experimental. Syntax trees preserve source text
and spans; edits are content-bound to the snapshot they were prepared against.
The parser can consume a validated compiled-scanner snapshot, but the scanner
component remains responsible for correct token classification. Current CLI
result shapes are in the [experimental response reference](implementation/cli-protocol.md);
the selected [machine protocol](specs/cli-protocol.md) is a separate target contract.

## Bounded generic functions

The hosted compiler supports selected compilation-local constrained generic
functions and static operator protocols. Templates are checked against their
declared constraints and produce closed specializations for supported concrete
types. This does not establish generic classes or structs, public generic
witnesses, runtime generic captures, or support for every accepted generic
contract. The [development native profile](implementation/native.md) and
[source grammar](specs/grammar-spec.md) define the relevant boundaries.
Bounded output combinations remain provisional only where they extend the
accepted profiles: fixed sides alongside mixed generic modes, or floating
results with mixed modes or fixed side outputs. Remaining decisions are in
the [proposal index](proposals/README.md); the accepted
floating-result contracts are in
[language contracts](specs/language-contracts.md#22-declared-outcome-relationships).
They do not establish general reference metadata or executable TRRM support.

## Executable TRRM scalar interchange

`compile-trrm` emits approved `(1, 0)` metadata and compact-linear or indexed
Bodies for a bounded source-free, fail-fast scalar subset, separately from
development semantic-module encoding. Consumers load artifact bytes, validate
metadata and Bodies, reconstruct typed inputs, and invoke the shared mandatory
verifier and backend; there is no producer-trust bypass. General imported-call
linking and checked-exception Body support are not implemented. Body-local
optional scalar values use independent presence and payload state, with the
assigned optional operations covered by linear/indexed JIT and AOT tests.
Optional floating types remain rejected, and optional `char16` is limited to
its assigned optional operations and equality. This does not add optional
source types, optional call signatures or native ABI support. This path does
not move source verification or emission into Terrum-authored components.
Static initialization is not encoded or executed by the current TRRM path.
The accepted first implementation gate is a self-contained, single-module
profile for non-generic, non-partial nominal types with ordinary eager scalar
static fields. Authored static constructors use only the accepted trigger
choices; execution requires enforced nonconcurrent access and fail-fast
termination. The gate excludes lazy fields, static-property backing storage,
foreign storage, generic or partial composition, and managed or
cleanup-bearing static values. Imported or packed definitions cannot
participate in an initialization dependency, though unrelated imports may
remain in the module. This is an implementation boundary, not a source
restriction or a complete TRRM 1.0 GA profile. The
[proposal](proposals/static-initialization-metadata.md) tracks unresolved
schemas, encoding and runtime integration.
The unreleased `(1, 0)` draft also accepts opcode `0x1F` for the eight accepted
checked floating-to-integer pairs; the implementation and linear/indexed
coverage are present. Opcode `0x1E` is accepted for explicit `char16` to
`float32`/`float64`, with unsigned code-unit semantics and total `NoThrow`
behavior; its implementation and linear/indexed coverage are also present.
Opcode `0x19` and `ConstBits` are accepted for same-format `float32`/`float64`
comparisons and exact raw-bit constants; float TypeSpecs also use the existing
type-directed signature, `Copy` and eager `Select` rules. These draft-only
encodings also include opcode `0x1A` for distinct `float32`/`float64`
conversions under nearest/ties-to-even target-format semantics. Their
implementations and linear/indexed coverage are present. Opcode `0x1B` is
accepted for the eight fixed-width integer-to-`float32`/`float64` pairs with
nearest/ties-to-even target-format semantics; its implementation and
linear/indexed coverage are present. Opcode `0x1C` is accepted for total
same-format `float32`/`float64` negation by sign-bit toggling, preserving all
other bits; its implementation and linear/indexed coverage are present. These
draft-only encodings do not change the approved IL specifications or promise
post-release compatibility. Opcode `0x1D` is accepted for same-format
`float32`/`float64` addition, subtraction, multiplication and division under
the working profile's NaN-result-class contract; implementation and
linear/indexed coverage are present. Remaining source-free TRRM floating
operations remain under unapproved proposals.

## Platform and validation

Native execution tests require Windows x64, although PE emission does not
invoke an installed platform toolchain. Use the [build guide](building.md)
for repository commands and the [native exception probe](native-seh.md) for
the bounded unwind evidence.
