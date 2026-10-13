# Design samples

This catalog describes **design sketches, not executable tests or compiled MVP programs**. Richer semantics remain unimplemented; the bounded source-class/`ManagedNode` access slice is exercised by migrated native samples. `.api.terrum` files sketch signatures and selected bodies, not complete implementations or foreign imports.

The sketches distinguish `mut` places/receivers from `write` reference/view access. `init ref<T>` and `init value` are the approved target forms for vacancy-only destinations; the executable profile supports a bounded boolean-success subset, while general references and migration from `out` remain pending. Inline vacant-output declarations remain schematic. Legacy `out`/`ref` call patterns are not target invocation syntax. The executable profile retains legacy `bool`/`out` beside distinct `bool`/`init ref<T>` contracts.

The numeric subset supports contextual decimal, hexadecimal and binary integers with digit separators; based literals use the expected target width as a bit pattern. Native buffer-view samples still use separate legacy access rules, not general `Span<T>` or reference/view support. Treat sketch outputs as acceptance criteria, not observed compiler results. [`samples/native`](../samples/native) contains runnable recursion (factorial: 720), loop/mutation (45), and six-argument call (91) examples under the [native runtime profile](implementation/native.md). See [language](language.md), [lifetimes](specs/lifetimes.md), [libraries](specs/libraries.md) and [implementation](implementation.md).

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
The [unique-owner sample](../samples/native/unique-owner.terrum) returns 42
with `--runtime checked-exceptions`: a mutable receiver updates the named
owner, and a subsequent failed replacement leaves that owner and its updated
value intact for the readonly query in `catch`.
The [composite owner sample](../samples/native/composite-owner.terrum) returns
42 with `--runtime checked-exceptions`: a failed constructor releases its
already acquired child before recovery constructs a complete owner.
The [owned buffer sample](../samples/native/owned-buffer.terrum) returns 42
under either runtime policy after appending, reading and updating the
initialized prefix of a fixed-capacity native buffer. It then moves that
allocation into a unique owner whose methods update, clear and reuse its
private writable buffer field without moving it out. One method updates the
first element through a scoped mutable field slice; readonly queries resume
after that loan ends.
The [readonly buffer-view sample](../samples/native/readonly-buffer-view.terrum)
returns 42 after checking a lexical slice of the initialized prefix and
reading it in a loop; the owner remains live until the view's scope ends.
The [writable buffer-view sample](../samples/native/mutable-buffer-view.terrum)
returns 42 after an exclusive scoped update and a NoThrow in-place `Clear`
followed by reuse of the same fixed-capacity owner.
The [Windows `MessageBoxW` sample](../samples/native/message-box.terrum) uses
the provisional compiler-owned native-import catalog, a no-owner `HWND`, and
static UTF-16-NUL literals. Build it with the explicit `build-native-import`
command; it returns success only when the user dismisses the dialog with OK.

```powershell
terrumc build-native-import samples\native\message-box.terrum -o artifacts\message-box.exe --runtime fail-fast
```

The [nested struct-receiver sample](../samples/native/nested-struct-receiver.terrum)
returns 42 under either runtime policy. It mutates nested counters in both
a Copy local and a unique owner through field paths and forwarded `this`
receivers, while adjacent readonly fields retain their values. The methods
update the original subobject rather than a copy.
The [managed-node cycle sample](../samples/native/managed-node-cycle.terrum)
returns 42 under either runtime policy: collection reclaims an unreachable
two-node cycle while preserving a rooted node and its child. This exercises
the bounded managed-node runtime, not general class syntax or a collection library.
The [managed-class sample](../samples/native/managed-classes.terrum) returns 42
under both policies. It constructs source-defined classes with multiple managed
edges, collects during partial construction without publishing it, and updates
a shared writable referent through an alias. This is not inheritance, optional
reference fields, a public managed-handle ABI or a general collection library.
The [optional-cycle sample](../samples/native/managed-optional-cycle.terrum)
adds explicitly initialized optional edges, source-defined cycles and non-null
presence snapshots. The [managed token-chain candidate](../self-host/ManagedTokenChain.terrum)
uses these edges and compact inline Copy fields for bounded linked storage of
complete tokens, with checked count/version updates and independent copied results.
The [private managed-node leaf](../samples/native/graph/managed-node-leaf.terrum)
replaces a scalar-only producer without changing its persisted consumer or
intermediate DLL. Its cycle allocation and explicit collection stay inside the
leaf under AOT and root-only JIT; see the [graph commands](implementation/native-graph.md).
The [private class leaf](../samples/native/graph/managed-class-leaf.terrum)
extends that replacement proof to source-defined optional cycles and checked
class metadata, without exposing a managed module ABI.
The [Copy-field class leaf](../samples/native/graph/managed-copy-class-leaf.terrum)
persists nested private Copy layouts and replaces the
same unchanged consumer/intermediate images.
The [managed Copy export original](../samples/native/graph/copy-contract-original.terrum)
and [managed Copy export replacement](../samples/native/graph/copy-contract-managed.terrum)
share the same public `Snapshot` value contract; the replacement collects its
private two-node managed cycle before returning the copied snapshot. The
[unchanged consumer](../samples/native/graph/copy-contract-consumer.terrum)
returns 40 with the original and 42 with the replacement, demonstrating
by-value and legacy `bool`/`out` Copy exports without exposing the
producer's private managed records. Exact AOT and root-only JIT commands are
in the [native graph guide](implementation/native-graph.md).
The [managed Copy middle](../samples/native/graph/managed-copy-middle.terrum)
and [managed Copy root](../samples/native/graph/managed-copy-root.terrum)
extend the same replacement to a managed intermediate and root. Both images
keep private managed records while exchanging only the leaf's public
handle-free `Snapshot` and scalar values; the unchanged graph returns 42 with
the original leaf and 44 with the managed replacement under AOT and root-only
JIT. See the [graph commands](implementation/native-graph.md).
The [managed Copy semantic consumer](../samples/native/modules/managed-copy-consumer.terrum)
uses the original and managed `Snapshot` providers above through the current
single-provider semantic consumer contract. Its private `Record` deliberately
shares a name with provider-private state, and remains rooted across provider
collection. The same persisted consumer artifact returns 42 or 44 when
`run-module` selects the original or managed provider; each `link-module`
command emits a fresh PE for its selected provider. Exact commands for both
runtime policies are in the [module guide](implementation/modules.md).
The [multi-type generic sample](../samples/native/multi-type-generics.terrum)
infers `int32` and the 16-byte `WidePair` independently in
`TrySelectPair<TFirst, TSecond>`. Its reversed helper forwards the ordered
specialization with type arguments swapped and publishes two different `out`
types; false results leave both outputs vacant. The executable returns 42.
This demonstrates bounded multi-parameter inference and helper composition,
not partial holes, general inference or heterogeneous operator protocols.
The [managed generic selection sample](../samples/native/managed-generic-selection.terrum)
combines a 16-byte `Stamp` with qualified source-class handles and optional
conditional outputs. It exercises readonly and
writable aliases, collection during a generic call, and retirement of one
unreachable cycle; the executable returns 42 without exposing a managed ABI.
The [static-protocol quadratic sample](../samples/native/static-protocol-quadratic.terrum)
returns 46 through AOT and JIT. Two nominal Copy wrappers explicitly implement
source-defined addition/multiplication protocols used by one constrained
generic expression. The [integer-power sample](../samples/native/static-protocol-power.terrum)
returns 42 using a structured generic algorithm and square helper for 32- and 64-bit wrappers.
Each helper in this example has one `T`, inferred from typed `Copy` struct
arguments; the caller supplies the identity. Scalar loop control does
not grant extra operations on the type parameter, and helper calls must be
justified by the caller's declared constraints. These remain closed
compilation-local specializations, not general generic-library or public
witness support.
The [binary-protocol sample](../samples/native/static-protocol-binary.terrum)
returns 42 using subtraction, division and remainder to compute two values,
then `&`, `|` and `^` to select bits from them. Its generic helpers declare
only the nominal protocols they use; the concrete wrapper implements all six.
The example uses ordinary inferred specializations, not primitive conformance,
operator-body inference or built-in numeric interface names.
The [comparison-protocol sample](../samples/native/static-protocol-comparison.terrum)
exercises the six separately declared equality/comparison protocols through
generic `Clamp`/`Within`/`Different` helpers and conditional equality
selection. Its probe returns 42 under AOT and direct JIT, with scanner parity
and private semantic/native servicing under both policies. It does not imply
derived comparison laws or universal equality/hash behavior.

| Program | Source | Design contract / expected behavior |
|---|---|---|
| Hello world | [hello-world](../samples/design/hello-world.terrum) | `void Main`; `Hello, world!`, exit 0 |
| Async hello | [hello-async](../samples/design/hello-async.terrum) | `MainAsync`, `Task == Task<void>`, rooted arguments through completion; two lines with optional first argument |
| Mandelbrot | [mandelbrot](../samples/design/mandelbrot.terrum) | Strict float32, explicit conversions, PBM; `5 3` yields rows `0 0 0 0 0`, `1 1 1 1 0`, `0 0 0 0 0`; invalid dimensions exit 2 |
| Calculator | [calculator](../samples/design/calculator.terrum) | Checked `int32`, status-conditional `init ref<int32>` publication, ASCII operator tokens; `7 / 3 = 2`, `-7 % 3 = -1`, min-int remainder `-1 = 0`; overflow/errors exit 2 |
| Collections | [collections](../samples/design/collections.terrum) | List/LinkedList collection builders, guarded writable span (accessor name provisional), fresh linked nodes and detach/reinsert identity, present-null versus missing; output `20`, `40`, `20`, `11`, `11`, `present, absent payload`, `missing element` |
| Random composition | [random-composition](../samples/design/random-composition.terrum) | Accepted target direction for constructor inference from the inner RNG and concrete `IRandom` dispatch without payload copy; generic constructor inference and interface dispatch remain unsupported, while contextual decimal/hexadecimal `uint32` literals are supported; stored generic needs `[Sized]`; four words `A5A5A5A5`, `A5A5A5A4`, `A5A5A5A7`, `A5A5A5A6` |
| Values/ownership | [types-and-ownership](../samples/design/types-and-ownership.terrum) | Copy struct vs readonly/writable shared class handles vs moved unique class, normalized optional, generic decimal parse, and `own<val<Text>>` native inline-payload direction (construction/conversion/cleanup notation unresolved) |
| Text/spans | [text-and-spans](../samples/design/text-and-spans.terrum) | One `TextSpan<TCodeUnit = void>` family, encoding-erased identity and checked typed projection, explicit `Span` interface projection, single-source forwarding, immutable text validity, `init ref` destinations, ASCII-only numeric tokens; `hello`, `1`, `Unicode lookalikes rejected.` |
| Formatting/construction | [construction-and-formatting](../samples/design/construction-and-formatting.terrum) | Typed nested Point/scalar writer forwarding, borrowed hole, `require`, owning text target and collection builder; `point = (3, 4)`, `saved = (3, 4)`, `4` |
| Requirements and contracts | [requirements](../samples/design/requirements.terrum) | Declaration pre/postconditions, private static proofs, body-local runtime checks and single-output `require Try` lifting with optional failure-type selection; illustrative syntax, intended return 21, not executable yet |
| Numerics/tensors | [numerics-and-tensors](../samples/design/numerics-and-tensors.terrum) | Generic math/SIMD composition with vector/lane ABI distinct from logical multidimensional `TensorSpan<T>` views; distinct lane masks, masked tails, `write Span<T>`/`write TensorSpan<T>` destinations, and explicit dense-view reduction; no execution or performance claim |
| Win32 + Direct3D 11 triangle | [overview](../samples/design/win32-dx11-triangle/README.md), [main](../samples/design/win32-dx11-triangle/main.terrum), [interop API](../samples/design/win32-dx11-triangle/interop.api.terrum), [shader](../samples/design/win32-dx11-triangle/triangle.hlsl), [NOTICE](../samples/design/win32-dx11-triangle/NOTICE.txt) | Intended cyan triangle on dark blue; window close exits 0; device removal reports and exits 3. Requires later native ownership/callback/ABI implementation |

The calculator must reject U+0661/U+FF11 digits, U+2212 minus and U+FF0B operator rather than normalize them into ASCII. `mandelbrot` marks points bounded after 64 updates, not proven set membership. Async entry returns only after both writes and required cleanup. Nothing here claims a Terrum compiler produced an image, window, executable or output.

Single ASCII operators/delimiters use target-typed character intent where helpful;
casts replace per-source numeric helpers without changing the proposed output.
The hosted compiler and compiled-scanner path support character literals
representable as `char16`; target-type checks remain separate from lexical
scalar validation. General family conformance, generic constructor inference
and builders are still design-only. Checked casts among `int32`, `uint32`,
`int64`, `uint64` and `char16`, boolean `require`, and legacy bool/out
publication for supported copy values run in the native profile. These
sketches' richer types and cleanup still need
later semantics.
See [conversion contracts](specs/conversions.md)
and [type families](specs/type-families.md), not parser success, for their rationale.

The triangle's `NOTICE.txt` contains the Microsoft Tutorial02 attribution and license. Its README describes platform contracts and deliberate exclusions rather than a runnable recipe. All design examples use provisional source notation; unsupported declarations are not silently treated as a compiler feature. For current executable parser/syntax fixtures, consult [implementation](implementation.md) instead.
