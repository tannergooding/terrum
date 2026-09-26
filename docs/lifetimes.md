# Lifetime and access contracts

This document specifies the accepted direction for checked loans, views and published member contracts, with **provisional notation** where the grammar is unresolved. It refines [language](language.md); [libraries](libraries.md) illustrates the span/text/collection protocols. Compact unique locals, explicit named moves, synchronous readonly receiver loans,
the exclusive, call-bounded receiver loan of `OwnedInt32Buffer.Set` and
`TryAppend`, and nonescaping lexical `ReadonlyInt32BufferView` locals obtained
through `OwnedInt32Buffer.Slice`, and exclusive lexical
`mut MutableInt32BufferView` locals obtained through `SliceMutable`, have
executable ownership checks today
([implementation](implementation.md)); a slice cannot be copied, passed,
stored or returned, and is not the proposed generic `Span<T>`.
The separate bounded `ManagedNode` proof allows copied aliasable handles,
records exact active roots for locals, call/output/return temporaries and
suspended frames, and traces its single managed edge at explicit collection
points. Managed `for` header locals and conditional outputs remain rooted
through the loop and retire when its header scope ends. It neither gives a
managed root authority over an `OwnedInt32Buffer`
nor implements general class, stored-view, checked-exception or module root
contracts.
The remaining lifetime constructs are rejected for execution.

## Three independent dependencies

1. **Receiver's own storage:** a result can refer to the receiver *place* or its inline fields. An exclusive or scoped borrow of that place bounds the result. A returned reference to a local receiver's inline field cannot escape that receiver merely because another field contains a long-lived allocation.
2. **Forwarded backing storage:** a receiver descriptor can contain a view whose address and validity come from another object, stack allocation, arena or native buffer. Forwarding that descriptor's span depends on its *backing* and access authority, not necessarily the lifetime of the descriptor variable. A temporary `Span<T>` may forward a view of a longer-lived allocation; a temporary owner/guard cannot forward a view past its disposal.
3. **Allocation, lease and disposal:** an owner or lease may keep backing alive and prohibit reclamation or reallocation; logical cleanup can still invalidate a native buffer even if its address and GC wrapper remain. A rooted GC object alone does not preserve a unique resource's cleanup scope, nor grant write authority. Capturing an owner, borrowing it, and transferring its cleanup obligation are different operations.

These relationships are not a single `scoped` bit. One result may borrow receiver bytes, propagate several backing dependencies, and require a separate owner/guard to remain undisposed. Access qualifiers, affinity, rooting and capture destinations travel with the dependencies. Keeping a pointer numerically unchanged is not proof of validity. Empty/end slices retain the same root/guard obligations as nonempty slices.

| Example | Required relationship |
|---|---|
| `InlineArray<T, N>` element loan | Receiver's inline storage and matched read/write authority |
| `Span<T>` slice or its explicit `IContiguousSequence<T>.AsSpan()` | Original backing, bounds, access and guard, not automatically the local span descriptor place |
| Known-code-unit `TextSpan<TCodeUnit>.AsSpan()` | Same backing, immutable read permission and validity-loss only; no writable alias |
| Typed text erasure to `TextSpan<void>` and successful typed projection | Actual encoding, validity, backing/root/lease and access retained; no receiver-storage borrow invented or cleanup obligation duplicated |
| Managed inline array elements | Tracked interior location and containing GC root/relocation |
| Native `UnmanagedArray<T>` elements | Specific allocation and owner/lease; merely retaining the wrapper is insufficient if it can dispose, replace, reallocate or return storage to a pool |
| `ListWriteAccess<T>.Span` | Guard-backed exclusive loan; cannot outlive/dispose the guard or coexist with overlapping writable extraction |
| `SpanTextWriter<T>` storing a destination | Declared captured exclusive loan in writer; not the default noncapturing synchronous parameter |
| Async write using an argument view | Operation-bounded capture until completion and cleanup; returned views inside the completed task retain their independent dependencies |

The public nominal type is still **one `Span<T>`**, not `Span<T, TOwner>`. A scoped data/length descriptor can keep its owner/guard live separately; a stored, returned or erased view needs a verified source relationship or a defined fixed-size representation with opaque anchors. Owner-dependent per-instance sizes, implicit per-slice allocations, per-slice retains and a global pointer-to-owner registry are not defaults. A tracked managed interior may move with GC; native allocations containing managed elements additionally need scanning/barriers. Aliased managed interiors cannot claim exclusive-loan optimizer facts. The exact anchor layout, source selector and calling convention are open and must not be guessed by a hosted lowering.

### Receiver-scoped versus receiver-unscoped

Here **receiver-scoped is a caller guarantee that an escaping result does not borrow the receiver's *own storage***. Receiver-unscoped instead permits such a borrow; it does not require one, promise any particular result lifetime, or erase forwarded backing and lease dependencies. A receiver-scoped implementation can forward backing held in the receiver's descriptor without borrowing the descriptor's own bytes. An implementation that *may* return a loan into the receiver's own fields cannot fulfill a receiver-scoped requirement.

| Interface receiver contract | Implementation may borrow receiver's own storage | Implementation forwards backing only, without borrowing receiver's own storage |
|---|---|---|
| Permits receiver-storage borrowing (unscoped) | Allowed; the result remains bounded by the receiver place | Allowed; the result retains its actual backing/lease dependencies |
| Guarantees no receiver-storage borrowing (scoped) | Rejected | Allowed; forwarding still cannot outlive backing, a guard or required disposal boundary |

The asymmetry is checked against the **declared implementation contract**, not inferred from its current body. In particular, a scoped requirement rejects an unscoped implementation even if today's implementation happens to forward only: that published permission could allow a later body to borrow receiver storage. Conversely, an unscoped requirement accepts a declared scoped implementation. Neither designation alone proves that an allocation is alive or that disposing its owner is safe.

The confirmed [C# 13 ref-struct interface proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md) has the analogous `[UnscopedRef]` interface/member asymmetry: an interface member with it permits an implementing member without it; an implementing member with it cannot satisfy a requirement lacking it. It also admits constrained ref-struct interface use without boxing; ordinary interface conversion still cannot box a ref struct. That proposal is **prior art**, not Terrum syntax, and does not supply Terrum's forwarded-backing or allocation/disposal model. The generic caller must obey the interface's broad public source contract, not the chosen implementation's narrower private behavior.

**Recommended extension:** a checked conditional *member-contract relationship*, published with the interface/witness, should express how an implementation's result may forward its receiver's existing backing while preserving the interface's broad source contract. This covers `Span<T>`'s explicit `IContiguousSequence<T>.AsSpan()` and generic `ReadUnits(ref sequence)` without inventing `IScopedSequence` or an owner generic argument. The broad interface relationship conservatively permits a result to borrow the source's own storage **or** forward its backing; a declared receiver-scoped refinement rules out the former without dropping backing/lease restrictions. An independently compiled generic caller must retain the broad source validity bound. A checked conditional contract exposed at the generic declaration can use a declared type/witness refinement where its condition is proven; otherwise even devirtualization or specialization cannot change the source's validity or assume a narrower escape relationship from today's body. Receiver-place scoping and backing forwarding require separate checks: “forwarded backing” metadata cannot smuggle a loan into dead receiver storage.

The final syntax for receiver modes, conditional relationships and source selectors is **open**. This recommendation is a front-end contract boundary, not permission to invent an annotation in the samples. `Span<T>` implements `AsSpan` explicitly; concrete `span.AsSpan()` remains intentionally unavailable. Text projection methods stay public.

## Declaration defaults, exceptions and published compatibility

Public relationships come from declarations, language-versioned defaults and inherited/registered interface/construction contracts. Bodies are verified against those relationships. Private or closed-world body inference may optimize a specific implementation, but cannot silently alter an externally replaceable promise.

| Declaration shape | Public default |
|---|---|
| Synchronous borrowed input | No retention beyond call, except its explicitly declared result relationship |
| View result with one eligible receiver/input | Broad source relationship: result may borrow that source's own storage **or** forward its backing/access dependencies; no unrelated capture |
| Multiple eligible sources | Explicit permitted source set; conservatively bound by **every** listed source, not by which branch today's body selects |
| `bool` plus `out` | Outputs publish on true; false leaves destinations vacant and cleans partial results |
| Other `out` | Initialize on every normal return absent explicit success policy |
| Declared `Task<TResult>` | May retain borrowed inputs/receiver until completion and required cleanup, whether or not the method is `async` |
| Implemented interface/registered builder | Inherit the source, access, output and effect contract; no implementation-by-implementation repetition |

An eligible view source can provide backing/borrowed access; outputs and independent scalars do not automatically qualify. A numeric parser's result is independent; a parser returning a view of input text retains the input dependency. A parameterless projection generally uses its receiver. An independent freshly allocated result needs its own owner/root and cost contract. More than one eligible input is not resolved by inspecting current control flow. Returning a view does **not** authorize storing it in an unrelated receiver. Constructor capture, exported callback factories and registrations need a declared destination/boundary. Local closures carry actual capture relationships; consuming a unique owner retains its ordinary transfer/cleanup rules rather than becoming an invisible borrowed capture.

Working annotations `[ViewOf(From = nameof(source))]`, `[ViewOf(Access.Write)]`, `[Captures(nameof(value))]` and `[OutputWhen(nameof(result), Status.Success)]` express **exceptions to defaults**, not unchecked assertions, and their spelling is provisional. Writable guard projections lend existing exclusive authority and block overlapping extraction, not upgrade a readonly guard. Conditional failure/exception cleanup leaves `out` vacant; no `default(T)` is synthesized. A success-status local can carry an output fact to a later branch. Async `out` targets operation-owned or valid rooted caller storage through completion, never a borrowed startup stack address smuggled into a suspended task. Fault capture requires trusted containment.

Matched access follows the relevant source through wrappers, interface dispatch, generic calls, reflection and generated adapters. A structural readonly list may yield independently mutable element handles, but not writable list-owned node/link projections; graph-read additionally restricts references traversed from its root. Readonly-to-immutable promotion needs checked freeze/publication excluding all writable aliases. Purity and stable reads are separate contracts; a `[Pure]` call on mutable input is not automatically memoizable.

Store both the **broad interface contract** and the **resolved published implementation/member refinement**, including conditional relationships, source selectors, capture destinations, root/disposal requirements, rule versions and ABI layouts. A witness must satisfy the interface requirement and the member's claimed refinement must be checked against its body. An independently compiled generic caller sees the broad interface contract; only an explicit checked conditional contract can expose a type/witness refinement when its condition is proven. Devirtualization, specialization and implementation pinning cannot change source validity or substitute body inference for a published relationship. Tightening an interface from permission to borrow receiver storage to a no-borrow requirement rejects existing implementations that published that permission, even if today's bodies do not use it. Broadening a published scoped implementation to permit receiver-storage borrowing invalidates existing callers that relied on its no-borrow guarantee; it requires a breaking revision or explicit checked adapter. Dynamic binding and AOT/JIT replacement checks must reject added capture, weakened access, changed view source, new cleanup obligation or invalid refinement, not merely compare CLR-signature shapes. Compatible changes may be replaced contract-bound; stronger implementation-only facts are not implicitly upgraded in consumer metadata. ABI layout and roll-forward need explicit version-aware validation.

## Implementation and unsettled points

The compact native profile tracks a unique named owner's vacancy across
branches, loops, returns and exceptional edges. By-value parameters transfer
from an explicit named move or fresh result and are dropped by the callee on
normal or checked exceptional exit unless moved again. Arguments are staged
in evaluation order: an earlier completed owner is dropped if a later
argument fails before invocation. A `bool`/`out` owner is published only on
true; on false or exceptional exit its completed private output is dropped
without manufacturing a readable caller value. Existing owners passed as
output destinations are dropped only when the call begins, not when earlier
arguments are still being evaluated. A successful `out var` gains its own
lexical cleanup scope. Receiver loans remain synchronous and readonly and
cannot be stored, consumed or passed as values. Cleanup is an explicit
`[NoThrow] [Cleanup] public void Close()`-shaped contract on a unique
instance method; the name itself has no special meaning. Only primitive fields
fit this first profile, and cleanup cannot invoke a potentially throwing
operation. A statically constant index proven within an immutable host
buffer grant may be used in NoThrow cleanup; general indexing cannot.
This is not a general owning-expression/field analysis, native allocation, OS-resource
adapter, stored interior, generic witness, return-anchor ABI or runtime
guard implementation. Later stages must check place disjointness and
last use, owning fields and temporaries, bounded result escapes,
interface variance/refinement and dynamic dependency binding before
executing examples that depend on them. Unsupported escapes must
diagnose, never silently box, clone, register, retain or copy.
