# Design samples

The catalog below describes **design sketches, not executable tests or compiled MVP programs**. The native integer/bool profile does not implement the richer semantics illustrated here. `.api.terrum` is an API-signature sketch with selected bodies, not a complete implementation or an implied foreign import. Design sketches use the target access distinction: `mut` changes a place or requires a writable receiver; `write` grants access through a reference/view. They show `init ref<T>` as the accepted parameter/type form for vacant initialization destinations; call-site loan expressions and inline vacant-output declarations remain unresolved schematic notation. Where retained, legacy `out`/`ref` expressions in bodies illustrate prior call patterns only, not accepted target invocation grammar. The bounded executable profile retains legacy `bool`/`out` publication until declarations and call sites migrate together. Several executable class, buffer-view, and `ManagedNode` examples still use legacy access syntax and behavior; they are not examples of the target `mut`/`write` model. Compare expected behavior as design acceptance criteria, not as observed compiler output. Separately, [`samples/native`](../samples/native) contains runnable recursion (factorial: 720), loop/mutation (45), and six-argument call (91) examples under the explicit [native runtime profile](native.md). See also [language](language.md), [lifetimes](lifetimes.md), [libraries](libraries.md) and [implementation](implementation.md).

The runnable [NoThrow example](../samples/native/no-throw.terrum) returns 42
through annotated value construction, widening and conditional outputs; its
effect contracts do not require or imply the future catch/cleanup runtime.
The [no-return example](../samples/native/no-return.terrum) returns 42 when its
conditional output succeeds; the failure branch has no normal continuation.
The [checked recovery sample](../samples/native/checked-recovery.terrum)
returns 42 in both AOT and JIT with `--runtime checked-exceptions`: a
cross-function division failure unwinds the inner `finally` before entering
an unsafe `catch`. The separate
[checked bounds component](../samples/native/checked-bounds.terrum) uses the
same policy when compiled with an entry and an explicit host-buffer grant.
The [owned transfer sample](../samples/native/owner-transfer.terrum) returns 42
after a named move through a by-value parameter and failed then successful
owning outputs. Its compact owners have lexical cleanup but hold no OS resource.
The [composite owner sample](../samples/native/composite-owner.terrum) returns
42 with `--runtime checked-exceptions`: a failed constructor releases its
already acquired child before recovery constructs a complete owner.
The [owned buffer sample](../samples/native/owned-buffer.terrum) returns 42
under either runtime policy after appending, reading and updating the
initialized prefix of a fixed-capacity native buffer.
The [readonly buffer-view sample](../samples/native/readonly-buffer-view.terrum)
returns 42 after checking a lexical slice of the initialized prefix and
reading it in a loop; the owner remains live until the view's scope ends.
The [writable buffer-view sample](../samples/native/mutable-buffer-view.terrum)
returns 42 after an exclusive scoped update and a NoThrow in-place `Clear`
followed by reuse of the same fixed-capacity owner.
The [managed-node cycle sample](../samples/native/managed-node-cycle.terrum)
returns 42 under either runtime policy: collection reclaims an unreachable
two-node cycle while preserving a rooted node and its child. This exercises
the bounded managed-node runtime, not general class syntax or a collection library.
The [managed-class sample](../samples/native/managed-classes.terrum) returns 42
under both policies. It constructs source-defined classes with multiple managed
edges, collects during partial construction without publishing it, and updates
a shared writable referent through an alias. This is not inheritance, optional
reference fields, a public managed ABI or a general collection library.
The [optional-cycle sample](../samples/native/managed-optional-cycle.terrum)
adds explicitly initialized optional edges, source-defined cycles and non-null
presence snapshots. The [managed token-chain candidate](../self-host/ManagedTokenChain.terrum)
uses these edges and compact inline Copy fields for bounded linked storage of
complete tokens, with checked count/version updates and independent copied results.
The [private managed-node leaf](../samples/native/graph/managed-node-leaf.terrum)
replaces a scalar-only producer without changing its persisted consumer or
intermediate DLL. Its cycle allocation and explicit collection stay inside the
leaf under AOT and root-only JIT; see the [graph commands](native-graph.md).
The [private class leaf](../samples/native/graph/managed-class-leaf.terrum)
extends that replacement proof to source-defined optional cycles and checked
class metadata, without exposing a managed module ABI.
The [Copy-field class leaf](../samples/native/graph/managed-copy-class-leaf.terrum)
persists nested private Copy layouts in semantic version 20 and replaces the
same unchanged consumer/intermediate images.
The [static-protocol quadratic sample](../samples/native/static-protocol-quadratic.terrum)
returns 46 through AOT and JIT. Two nominal Copy wrappers explicitly implement
source-defined addition/multiplication protocols used by one constrained
generic expression. The [integer-power sample](../samples/native/static-protocol-power.terrum)
returns 42 using a structured generic algorithm and square helper for 32- and 64-bit wrappers.
Its caller supplies the identity; scalar loop control does not grant extra
operations on the type parameter, and helper calls must be justified by the
caller's declared constraints. These are closed specializations, not
general generic-library or public witness support.

| Program | Source | Design contract / expected behavior |
|---|---|---|
| Hello world | [hello-world](../samples/design/hello-world.terrum) | `void Main`; `Hello, world!`, exit 0 |
| Async hello | [hello-async](../samples/design/hello-async.terrum) | `MainAsync`, `Task == Task<void>`, rooted arguments through completion; two lines with optional first argument |
| Mandelbrot | [mandelbrot](../samples/design/mandelbrot.terrum) | Strict float32, explicit conversions, PBM; `5 3` yields rows `0 0 0 0 0`, `1 1 1 1 0`, `0 0 0 0 0`; invalid dimensions exit 2 |
| Calculator | [calculator](../samples/design/calculator.terrum) | Checked `int32`, status-conditional `init ref<int32>` publication, ASCII operator tokens; `7 / 3 = 2`, `-7 % 3 = -1`, min-int remainder `-1 = 0`; overflow/errors exit 2 |
| Collections | [collections](../samples/design/collections.terrum) | List/LinkedList collection builders, guarded writable span (accessor name provisional), fresh linked nodes and detach/reinsert identity, present-null versus missing; output `20`, `40`, `20`, `11`, `11`, `present, absent payload`, `missing element` |
| Random composition | [random-composition](../samples/design/random-composition.terrum) | Constructor generic inference from the inner RNG; contextual uint32 literals; `IRandom` dispatch without payload copy; stored generic needs `[Sized]`; four words `A5A5A5A5`, `A5A5A5A4`, `A5A5A5A7`, `A5A5A5A6` |
| Values/ownership | [types-and-ownership](../samples/design/types-and-ownership.terrum) | Copy struct vs readonly/writable shared class handles vs moved unique class, normalized optional, generic decimal parse, and `own<val<Text>>` native inline-payload direction (construction/conversion/cleanup notation unresolved) |
| Text/spans | [text-and-spans](../samples/design/text-and-spans.terrum) | One `TextSpan<TCodeUnit = void>` family, encoding-erased identity and checked typed projection, explicit `Span` interface projection, single-source forwarding, immutable text validity, `init ref` destinations, ASCII-only numeric tokens; `hello`, `1`, `Unicode lookalikes rejected.` |
| Formatting/construction | [construction-and-formatting](../samples/design/construction-and-formatting.terrum) | Typed nested Point/scalar writer forwarding, borrowed hole, `require`, owning text target and collection builder; `point = (3, 4)`, `saved = (3, 4)`, `4` |
| Numerics/tensors | [numerics-and-tensors](../samples/design/numerics-and-tensors.terrum) | Generic math/SIMD composition, distinct lane masks, masked tails, `write Span<T>`/`write TensorSpan<T>` destinations, and explicit dense-view reduction; no execution or performance claim |
| Win32 + Direct3D 11 triangle | [overview](../samples/design/win32-dx11-triangle/README.md), [main](../samples/design/win32-dx11-triangle/main.terrum), [interop API](../samples/design/win32-dx11-triangle/interop.api.terrum), [shader](../samples/design/win32-dx11-triangle/triangle.hlsl), [NOTICE](../samples/design/win32-dx11-triangle/NOTICE.txt) | Intended cyan triangle on dark blue; window close exits 0; device removal reports and exits 3. Requires later native ownership/callback/ABI implementation |

The calculator must reject U+0661/U+FF11 digits, U+2212 minus and U+FF0B operator rather than normalize them into ASCII. `mandelbrot` marks points bounded after 64 updates, not proven set membership. Async entry returns only after both writes and required cleanup. Nothing here claims a Terrum compiler produced an image, window, executable or output.

Single ASCII operators/delimiters use target-typed character intent where helpful;
casts replace per-source numeric helpers without changing the proposed output.
Character literals, family conformance, generic constructor inference and
builders are still design-only. Checked casts among `int32`, `uint32`,
`int64`, `uint64` and `char16`, boolean `require`, and legacy bool/out
publication for supported copy values run in the native profile. These
sketches' richer types and cleanup still need
later semantics.
See [conversion contracts](conversions.md)
and [type families](type-families.md), not parser success, for their rationale.

The triangle's `NOTICE.txt` contains the Microsoft Tutorial02 attribution and license. Its README describes platform contracts and deliberate exclusions rather than a runnable recipe. All design examples use provisional source notation; unsupported declarations are not silently treated as a compiler feature. For current executable parser/syntax fixtures, consult [implementation](implementation.md) instead.
