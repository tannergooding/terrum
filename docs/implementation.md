# Implementation support

Terrum's accepted target contracts and its implemented subset are tracked
separately. The [specifications](specs/README.md) define intended behavior;
the [roadmap](roadmap.md) lists pending contracts and dependencies; and
[proposals](proposals/README.md) contain unapproved choices.

## Hosted compiler

The compiler host is written in C#. It exposes a Unicode-aware, lossless
syntax API and a bounded Windows x64 native profile with direct PE emission
and a JIT worker. The implemented language subset includes checked scalar
operations, compact values and unique owners, conditional outputs, bounded
generics, managed roots/classes, and deterministic cleanup. The native backend
emits Terrum-owned code; CIL is neither a dependency nor the default.

Current profile markers are syntax language version 45, semantic body features
v43-v49, semantic-module revision 76, and native profiles native-10 through
native-20. These markers describe supported implementations, not approval of
the remaining language contracts. The exact source, layout, metadata, ABI,
and runtime contracts are in the [specification index](specs/README.md).
The [sample catalog](samples.md) distinguishes runnable programs from design
sketches.

## Persistent and self-hosted components

The additive TRRM scalar path emits approved metadata and compact-linear or
indexed Bodies for a bounded source-free, fail-fast scalar subset. It uses the
shared hosted verifier and backend; general imported-call linking and checked
exception Body support are not implemented.

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
provisionally under the unapproved [Windows native FFI proposal](proposals/windows-native-ffi.md):
bodyless top-level or static `extern` callables use the exact
`Terrum.Interop.NativeImport` attribute and a key in the compiler-owned
immutable catalog. Binding, mandatory verification and PE emission check the
catalogued fixed-width integer/`void` signature and effects; the production
catalog currently contains only `GetCurrentThreadId`. Unsupported
module/JIT/TRRM persistence paths reject these imports. This is not general
foreign-function support and does not move scalar frontend verification or
emission into Terrum.

## Syntax and CLI

The public C# syntax APIs are experimental. Syntax trees preserve source text
and spans; edits are content-bound to the snapshot they were prepared against.
The parser can consume a validated compiled-scanner snapshot, but the scanner
component remains responsible for correct token classification. CLI result
shapes and versioning are specified in the [machine protocol](specs/cli-protocol.md).

## Bounded generic functions

The hosted compiler supports selected compilation-local constrained generic
functions and static operator protocols. Templates are checked against their
declared constraints and produce closed specializations for supported concrete
types. This does not establish generic classes or structs, public generic
witnesses, runtime generic captures, or support for every accepted generic
contract. The [native profile](specs/native.md) and
[source grammar](specs/grammar-spec.md) define the relevant boundaries.

## Executable TRRM scalar interchange

`compile-trrm` emits approved `(1, 0)` metadata and compact-linear or indexed
Bodies for a bounded source-free, fail-fast scalar subset, separately from
development semantic-module encoding. Consumers load artifact bytes, validate
metadata and Bodies, reconstruct typed inputs, and invoke the shared mandatory
verifier and backend; there is no producer-trust bypass. General imported-call
linking and checked-exception Body support are not implemented. This path does
not move source verification or emission into Terrum-authored components.

## Platform and validation

Native execution tests require Windows x64, although PE emission does not
invoke an installed platform toolchain. Use the [build guide](building.md)
for repository commands and the [native exception probe](native-seh.md) for
the bounded unwind evidence.
