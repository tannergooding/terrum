# Lifetime and access contracts

This document specifies accepted contracts for loans, views and published
members, using provisional notation where grammar remains open. It refines
[language](language.md); [libraries](libraries.md) illustrates span, text and
collection protocols.

## Definitions and notation

A **place** is addressable storage; a reference to a place is distinct from a
copy of the value stored there. A **loan** grants bounded access to a place or
backing storage without transferring its ownership. **Backing** is the storage
whose lifetime and validity support a view's elements. An **owner** retains
the responsibility to release a resource; a **lease** can keep backing valid
or prevent its reclamation/replacement for a bounded interval. These
relationships are independent: a rooted object does not by itself preserve a
native resource's cleanup scope or grant write authority.

Declaration places default to fixed/readonly. `mut` grants place rebinding or
a writable receiver; `write` grants capability through a reference or view.
Changing a local descriptor does not create writable referent access, and
neither qualifier overrides immutable fields or nominal access. The canonical
representation forms are `ref<T>`, `ptr<T>`, `box<T>`, `val<T>` and `own<T>`.
`mut` qualifies a place or receiver, not a generic type argument. Every
reference or view contract applies to the complete qualified form, including
access, backing, roots, ownership and region dependencies.

### Passing and storage access

| Form | Access and expansion |
|---|---|
| `T` by value | Preserve the complete value's authority/dependencies; copy only when eligible, otherwise transfer through the declared ownership contract |
| `ref T` parameter / `ref<T>` | Readonly alias to a `T` place, not a snapshot or exclusive loan |
| `mut ref T` parameter / `write ref<T>` | Writable alias to a `T` place; permits slot updates, not arbitrary writes through values stored in that slot |
| `implicit ref<T>` parameter | The same readonly reference contract, borrowing an eligible place or materializing the expression once in its ordinary enclosing scope |
| `init ref<T>` parameter | Vacant destination initialized according to the published normal-outcome contract, not an arbitrary writable alias |
| `pin ref<T>` | Explicitly acquired backing/root stability for its pin dependency; ordinary reference qualification does not acquire it |
| Class handle `C` / `write C` | Readonly / writable object access without borrowing the variable's handle slot |
| `ref<C>` / `write ref<C>` | Readonly / writable handle-slot access, independently of object authority |
| `ref<write C>` / `write ref<write C>` | Preserve object-write authority with readonly / writable handle-slot access |

Reference-object `C` carries rooting and lifetime dependencies but is not an
interior pointer into the variable containing its handle. Applicable access,
capture and lifetime principles are shared with references; their storage
indirections are not interchangeable. `mut C` permits rebinding a local handle,
not writing the object; `write C` permits eligible object writes, not rebinding
that local. Neither readonly access nor a writable alias automatically implies
exclusivity, deep immutability or stable reads.
Reject an inapplicable qualifier combination rather than inventing a copy,
pin, initialization route or stronger authority. `implicit` remains parameter-
only; writable and initialization loans do not acquire implicit materialization.

The selected implicit `pin ref<T>` to matching `ptr<T>` conversion is a
separate, narrowly defined built-in edge. It preserves access and all referent,
pin and backing dependencies, acquires no pin and does not transfer the pin's
ownership. A derived pointer cannot outlive its supporting pin or manufacture
write authority. Overload binding recognizes only this direct projection;
identity matches rank ahead of it, with no search for intermediate conversions.
This is a target contract, not current executable reference/pointer support.

Pin initialization acquires before publication: failure leaves a vacant
destination vacant and cleans partial acquisition state. Eligible mutable
replacement preserves the old value and pin through evaluation/acquisition
of its replacement; failed acquisition leaves them in place. On success,
publication and retirement still require that no outstanding dependency needs
the old pin. Initializer side effects are not rolled back. Existing
initialization/write authority applies; early lifetime ending does not make an
immutable pin binding reassignable. See
[pin acquisition](grammar-spec.md#32-types-parameters-and-arguments).
Proven lifetime escapes and missing required pins are checked errors, not
safe-context warnings or obligations erased by `unsafe`. Valid pointer
formation itself does not warn or require unsafe; dereference still does.
These are accepted target contracts, not current executable pin support.

Current executable ownership, view and managed-root boundaries are documented
in [implementation](../implementation.md) and [native](native.md); staged work
is tracked in [roadmap](../roadmap.md). A slice does not thereby become the
proposed generic `Span<T>`.

Transfer/share proofs cover the complete qualified form, including backing
storage, retained roots and region dependencies. A readonly loan is not by
itself permission for concurrent use. The [memory model](memory-model.md)
defines checked sharing and trusted implementation boundaries; neither erases
the lifetime obligations published by a safe interface.

## Three independent dependencies

1. **Receiver's own storage:** a result can refer to the receiver *place* or its inline fields. An exclusive or scoped borrow of that place bounds the result. A returned reference to a local receiver's inline field cannot escape that receiver merely because another field contains a long-lived allocation.
2. **Forwarded backing storage:** a receiver descriptor can contain a view whose address and validity come from another object, stack allocation, arena or native buffer. Forwarding that descriptor's span depends on its *backing* and access authority, not necessarily the lifetime of the descriptor variable. A temporary `Span<T>` may forward a view of a longer-lived allocation; a temporary owner/guard cannot forward a view past its disposal.
3. **Allocation, lease and disposal:** an owner or lease may keep backing alive and prohibit reclamation or reallocation; logical cleanup can still invalidate a native buffer even if its address and GC wrapper remain. A rooted GC object alone does not preserve a unique resource's cleanup scope, nor grant write authority. Capturing an owner, borrowing it, and transferring its cleanup obligation are different operations.

These relationships are not a single `scoped` bit. One result may borrow receiver bytes, propagate several backing dependencies, and require a separate owner/guard to remain undisposed. Access qualifiers, affinity, rooting and capture destinations travel with the dependencies. Keeping a pointer numerically unchanged is not proof of validity. Empty/end slices retain the same root/guard obligations as nonempty slices.

## By-value results and borrowed results

A source return of `T` retains its declared semantics even when the physical
ABI uses a hidden result-storage argument or copy elision. ABI-indirect storage
does not itself introduce a loan of the original value's storage or permit
skipping ordinary ownership/cleanup. Handles and loans already contained in
the complete `T` retain their declared dependencies. A borrowed result's
`ref<T>` representation must name the permitted source dependency, lifetime,
access and alias relationship.
Do not turn a large by-value result into a source borrow because of an
incidental size threshold.

No universal return keyword is implied. An associated iterator `Item<'a>` is
a candidate when a selected sequence witness relates `next` to a borrowed
item. That relation must preserve the complete qualified item type and its
ownership, backing and lifetime dependencies. Exact selection rules and ABI
remain open library design.

## Parameter-only implicit readonly loans

`implicit ref<T>` is an accepted parameter-only modifier on a readonly
reference parameter, not `implicit T` or `in T`, a qualifier inside `T` or
`ref<T>`, a `view<T>` type, or a representation attribute. The caller passes
an ordinary expression and it is evaluated once. If it denotes an eligible
place, the call borrows that place's slot; otherwise the compiler initializes
a hidden local at that argument's original evaluation point in the current
enclosing lexical scope. This is the same scope as an explicit local declared
there, not a lifetime-only block and not a value hoisted to scope entry. A
skipped conditional or short-circuit argument creates no initialized local or
owner to clean up. The local follows ordinary initialization, ownership/root
cleanup and unwind rules. Borrowing alone requires neither `[Copy]` nor
ownership transfer.

```terrum
void Inspect(implicit ref<LargeRecord> record);

Inspect(record);
Inspect(CreateRecord());
```

The introduced loan is an ordinary readonly reference. Return, storage,
capture and suspension obey the same declared dependencies and lifetime rules
as explicit `ref<T>`; `implicit` does not add an unconditional noescape rule.
A returned reference to a materialized local may be used by later statements
in the same lexical scope when its ordinary declared dependencies permit it;
it cannot outlive that local or extend the lifetime of an existing place or
backing value. This is the accepted temporary-scope rule, not a new lifetime
system for implicit arguments.
An eligible local, parameter, field or indexed element is borrowed as an
existing place. A reference-returning access supplies its declared place and
dependencies; a value-returning call, property, operator or conversion instead
initializes the hidden local exactly once. Source spelling resembling a field
does not turn a value-returning property into a loan of its backing storage.
If borrowing the existing place conflicts with its access or owner contract,
the call diagnoses rather than silently copying the value to evade the conflict.
The loan restricts access to the outer slot only. The complete qualified `T`
retains any write authority, backing, roots, ownership and dependency contracts
already carried by its value; a Copy descriptor copied out of the slot keeps
those same contracts. A readonly outer loan is not deep immutability, purity
or exclusivity.

The form is available for concrete/generic parameters and explicitly declared
extension receivers, without changing ordinary instance-receiver defaults. It
does not alter existing APIs unless they explicitly adopt the parameter
contract. The modifier does not apply to `write ref<T>`, `init ref<T>`, or
`ptr<T>` parameters and does not authorize arbitrary conversions. An explicit `ref<T>` and
`implicit ref<T>` share one reference signature rather than forming distinct
overloads. Preference between a by-value `T` overload and an
`implicit ref<T>` overload is settled: ordinary `F(value)` selects `F(T)` when
both are applicable, while an explicit reference argument may select a
reference overload. Explicit and implicit readonly `ref<T>` alone cannot
distinguish overloads. ABI lowering may choose a physical by-value or indirect
form only while preserving the declared snapshot/loan, aliasing, ownership and
lifetime semantics; there is no universal size threshold or adaptive type
attribute. Results remain `T` or an explicit `ref<T>` with declared lifetime
metadata; no adaptive generic value-or-loan result family is selected. This
contract is accepted direction; current compiler support is documented in
[implementation](../implementation.md).

The receiver/callable and then named or positional arguments are evaluated
in source order. Form a place by evaluating its receiver, indices and checks
once at that argument's position, and form the loan at the same point as the
equivalent explicit readonly reference. A later argument may change the
referent if ordinary alias rules permit it; a callee read is not a snapshot
of the earlier argument's value. If the change violates an owner or loan
constraint, both explicit and implicit spellings are invalid. Convenience
syntax neither delays the reference nor invents a distinct loan lifetime.

## Erased interface storage is explicit

An interface name `I` describes nominal behavior and an abstract payload, not
an implicit storable handle. Use `ref<I>` for a borrowed erased receiver,
`write ref<I>` for a writable borrow, `box<I>` for a managed aliasable
interface handle, or `write box<I>` for a writable object receiver. A
`List<box<I>>` stores homogeneous handle representations while allowing
different concrete implementations. Bare `I` is not a local/field storage
representation, and type notation alone does not allocate or project a value.

`write ref<I>` grants access to mutate the original implementation but cannot
replace an unknown inline payload with an unrelated implementation. The access
of a handle slot is independent of its referent: `write ref<box<I>>` makes the
slot writable but retains readonly object authority; `ref<write box<I>>` keeps
the slot readonly while preserving writable object authority. `ref<C>` borrows
a class-handle slot, not automatically the class payload. Explicitly boxing a Copy inline
struct creates an independent payload; a unique value requires explicit
transfer. Projecting an existing class object may preserve identity without
allocation. The exact checked projection, representation, rooting and ABI
remain implementation contracts, not consequences of the type spelling.

The target `func<Args..., R>` family carries its target's checked ownership,
root, borrow and access dependencies. Binding a receiver retains only the
dependencies declared by the callable contract; it does not silently own a
borrow, prolong a loan, or allocate storage. An open-instance callable instead
receives the receiver as an explicit argument, with access matching the method
(for example, `C` versus `write C`, or `ref<S>` versus `write ref<S>` for a
mutable struct receiver). The receiver marker's spelling is open and does not
grant access by itself. `fnptr<Args..., R>` has no independently retained
environment; its target code must outlive use. An explicit native pointer plus
context aggregate has the ordinary declared dependency rules rather than
hidden closure capture semantics.

| Example | Required relationship |
|---|---|
| `InlineArray<T, N>` element loan | Receiver's inline storage and matched read/write authority |
| `Span<T>` slice or its explicit `IContiguousSequence<T>.AsSpan()` | Original backing, bounds, access and guard, not automatically the local span descriptor place; `mut Span<T>` only rebinds the descriptor, while `write Span<T>` lends writable slots |
| `Span<write C>` | Readonly element slots containing independently writable class-handle capabilities; distinct from `write Span<C>` |
| Bound `func<...>` | Target and every declared receiver/capture dependency remain valid under the callable's ownership and loan contract |
| `fnptr<...>` | Target code and declared ABI remain valid through each invocation; the pointer retains no context |
| Known-code-unit `TextSpan<TCodeUnit>.AsSpan()` | Same backing, immutable read permission and validity-loss only; no writable alias |
| Typed text erasure to `TextSpan<void>` and successful typed projection | Actual encoding, validity, backing/root/lease and access retained; no receiver-storage borrow invented or cleanup obligation duplicated |
| Managed inline array elements | Tracked interior location and containing GC root/relocation |
| Native `UnmanagedArray<T>` elements | Specific allocation and owner/lease; merely retaining the wrapper is insufficient if it can dispose, replace, reallocate or return storage to a pool |
| `ListWriteAccess<T>.Span` | Guard-backed exclusive loan; cannot outlive/dispose the guard or coexist with overlapping writable extraction |
| `SpanTextWriter<T>` storing a destination | Declared captured exclusive loan in writer; not the default noncapturing synchronous parameter |
| Async write using an argument view | Operation-bounded capture until completion and cleanup; returned views inside the completed task retain their independent dependencies |

The public nominal type is still **one `Span<T>`**, not `Span<T, TOwner>`. Its
unqualified form is readonly; `mut Span<T>` permits descriptor rebinding,
`write Span<T>` grants writable slots, and a `write` element type grants a
separate referent capability. A scoped data/length descriptor can keep its
owner/guard live separately; a stored, returned or erased view needs a verified
source relationship or a defined fixed-size representation with opaque
anchors. Owner-dependent per-instance sizes, implicit per-slice allocations,
per-slice retains and a global pointer-to-owner registry are not defaults. A
tracked managed interior may move with GC; native allocations containing
managed elements additionally need scanning/barriers. Aliased managed interiors
cannot claim exclusive-loan optimizer facts. The exact anchor layout, source
selector and calling convention are open and must not be guessed by a hosted
lowering.

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
| Boolean-success `init ref<T>` without an explicit conditional postcondition | Destination starts vacant; `true` initializes it and `false` leaves it uninitialized |
| Other result type with `init ref<T>` and no explicit conditional postcondition | Destination starts vacant and must be completely initialized on every normal return |
| Explicit conditional postcondition for `init ref<T>` | The declared status/result relationship governs initialization; `ensure <expr>` uses an ordinary Boolean expression with [specified clause placement](grammar-spec.md#31-source-file-structure-and-declarations); correlated-output metadata remains to specify |
| Declared `Task<TResult>` | May retain borrowed inputs/receiver until completion and required cleanup, whether or not the method is `async` |
| Implemented interface/registered builder | Inherit the source, access, output and effect contract; no implementation-by-implementation repetition |

For conditional outputs, initialization is a live-state fact: an initialized
nullable value containing null is still a complete live `T`; an uninitialized
outcome has no live `T` lifetime, value access or `T` cleanup, even if its
storage remains allocated. Declaration postconditions are the normal authoring
route, using `ensure <expr>` with an ordinary Boolean expression, patterns and
short-circuit behavior. A legal value predicate implies initialization; body
and path analysis must establish that reads are legal and must never read
vacant storage to discover a false predicate. A successful value predicate
establishes initialization. Callers may retain a checked relation between
output state and its discriminator before testing it, but must not access or
clean up a vacant arm or receive an undiscriminated maybe-live owner. Exact
clause placement/grammar and metadata remain to be specified. The
boolean-success and other-result defaults above are language-versioned, not
inferred from method names. Legacy `bool`/`out` conditional publication is
distinct from `init ref<T>`; current support for each is documented in
[implementation](../implementation.md).

The task handle alone does not discharge responsibility for a started
operation. Its obligation remains with a structured owner that joins it, an
explicitly transferred responsible owner, or a supervisor with error, lifetime
and shutdown policy. Discarding a task does not cancel it or release its
captures. Cancellation does not shorten a borrowed input's declared lifetime:
backing and guards remain live until the operation actually stops and performs
required cleanup.

A `require` expression or matching context lifts eligible destinations omitted
from the call; explicitly supplied destinations are not lifted, and partial
omission is permitted. With all destinations supplied, matching uses the
ordinary return value. The call evaluates once, and projections preserve
per-output initialization/presence, status correlations and complete
ownership/access/backing contracts. Matching may select initialized failure
outputs rather than imposing a universal success gate.
Bare lifted `require` requires every lifted output initialized and yields its
complete value type, including permitted nullability. An explicit pattern
requirement can select fewer outputs or require non-nullness. `_` does not
read or require initialization of an ignored output, but cannot erase its live
cleanup obligations. No vacant read, ordinary tuple construction from vacant
slots, fabricated `default(T)` or mandatory `Option`/`Result` is introduced.
Definition/import checking establishes the availability relations; output
registration, richer-status spellings, metadata and exact reference extraction
remain work. A selected exception applies to the false condition only and does
not catch an exception raised while evaluating the call. These are target
contracts; current compiler support is documented in
[implementation](../implementation.md).

An eligible view source can provide backing/borrowed access; outputs and independent scalars do not automatically qualify. A numeric parser's result is independent; a parser returning a view of input text retains the input dependency. A parameterless projection generally uses its receiver. An independent freshly allocated result needs its own owner/root and cost contract. More than one eligible input is not resolved by inspecting current control flow. Returning a view does **not** authorize storing it in an unrelated receiver. Constructor capture, exported callback factories and registrations need a declared destination/boundary. Local closures carry actual capture relationships; consuming a unique owner retains its ordinary transfer/cleanup rules rather than becoming an invisible borrowed capture.

The accepted parameter-directed result annotation is
`[return: DependsOn(nameof(left), nameof(right))]`; `nameof` is preferred
over literal parameter names. All possible declared input dependencies remain
obligations at the caller, without extending their permitted lifetimes.
See the [source contract](grammar-spec.md#result-dependency-declarations).
Receiver-storage, forwarded-backing, stored-capture and suspension contracts
are separate targeted relationships. The result annotation alone does not
declare all of them. Unannotated defaults follow the language-versioned and
inherited/registered declaration rules above, not a dependency-bearing example
or today's implementation. Exact selector schemas and metadata encodings
remain specification work.

Working annotations `[ViewOf(From = nameof(source))]`,
`[ViewOf(Access.Write)]` and `[Captures(nameof(value))]` express **exceptions
to defaults**, not unchecked assertions, and their spelling is provisional.
Conditional output initialization is normally stated in declaration
postconditions, not a dedicated output attribute.
Writable guard projections lend existing exclusive authority and block
overlapping extraction; they do not upgrade a readonly guard. Conditional
failure/exception cleanup leaves an initialization destination vacant; no
`default(T)` is synthesized. A success-status local can carry an output fact to
a later branch. Async initialization targets operation-owned or valid rooted
caller storage through completion, never a borrowed startup stack address
smuggled into a suspended task. Fault capture requires trusted containment.
Legacy `bool`/`out` conditional publication is distinct from the `init ref<T>`
target notation; declaration and call sites must migrate coherently rather than treating
`init ref` as a spelling alias for arbitrary writable `ref<T>`.

Matched access follows the relevant source through wrappers, interface dispatch,
generic calls, reflection and generated adapters. A structural readonly list may
yield independently writable class-handle capabilities only when those
capabilities are present in its element type; that does not grant writes to
list-owned slots, nodes or links. Deep graph-read is a future proposal, not the
v1 access model; it could restrict references traversed from a root. A
separate immutable-graph/freeze contract would need checked publication
excluding all writable aliases. Purity and stable reads are separate
contracts; a `[Pure]` call on mutable input is not automatically memoizable.

An interface witness must satisfy the interface's published requirements.

Store both the **broad interface contract** and the **resolved published implementation/member refinement**, including conditional relationships, source selectors, capture destinations, root/disposal requirements, rule versions and ABI layouts. Source compilation checks a member's claimed refinement against its authored body. An independently compiled generic caller sees the broad interface contract; only an explicit checked conditional contract can expose a type/witness refinement when its condition is proven. Ordinary binary consumers rely on published dependency metadata rather than re-proving dependency bodies, while still checking references, contract compatibility and caller use. Devirtualization, specialization and implementation pinning cannot change source validity or substitute body inference for a published relationship. Tightening an interface from permission to borrow receiver storage to a no-borrow requirement rejects existing implementations that published that permission, even if today's bodies do not use it. Broadening a published scoped implementation to permit receiver-storage borrowing invalidates existing callers that relied on its no-borrow guarantee; it requires a breaking revision or explicit checked adapter. Dynamic binding and AOT/JIT replacement checks must reject added capture, weakened access, changed view source, new cleanup obligation or invalid refinement, not merely compare CLR-signature shapes. Compatible changes may be replaced contract-bound; stronger implementation-only facts are not implicitly upgraded in consumer metadata. ABI layout and roll-forward need explicit version-aware validation.

## Ownership transfer and cleanup

### Explicit local lifetime end

`name = _;` ends the current value's lifetime in a named local at that point,
as if its value reached the end of an enclosing block. It does not end the
block or unrelated values, and the local's declaration remains in lexical
scope. The binding becomes vacant rather than containing null or `default(T)`.
Redeclaring the same name in that scope still rejects.

The operation performs precisely the current value's scope-exit obligations:
a unique owner is cleaned up; a borrowed value releases its loan and entailed
dependencies without destroying the referent; a scope-cleanup guard performs
its registered cleanup, such as unlocking. Under an ARC profile it releases
this value's retain, possibly freeing the object if it was the last retain.
An ordinary tracing-GC handle retires this local's root, not the shared object,
and does not force collection. No operation is invented for a value whose
scope exit requires none. These examples describe the owning profile's
contracts, not current compiler support for arbitrary guards, ARC or pins.

The value must be definitely live. Outstanding loans, derived pointers,
captures or other dependencies can prevent its early end, including releasing
a supporting pin or owner. Ending a local reference ends that local loan; it
does not write through the reference or end caller-owned storage. The selected
form does not authorize fields, properties, indexers or parameter aliases.
It cannot bypass access, affinity or suspension obligations.

Cleanup uses the same ordering, effects and checked failure rules as ordinary
scope exit. Retirement is exact once: subsequent scope exit or unwind must not
repeat completed obligations for the ended value. No rollback or independent
failure-suppression rule is introduced.

Ending the value grants no new write authority. An ordinary immutable `var`
cannot be assigned again. An `init var` or `init T` local can be initialized
again when vacant, by checked assignment or eligible initialization completion,
without changing its established type; it cannot replace a live value merely
because it supports initialization. Mutable bindings retain their existing
write/initialization permissions. A new initialization starts a new value
lifetime with its own cleanup obligations, not a revival of the old value.
Reads, borrows and another lifetime-end operation require definite liveness
along every incoming control-flow path.

See the [statement grammar](grammar-spec.md#33-statements-and-local-declarations).
This is accepted target direction; current vacant locals support checked
initialization calls, not this early-end/assignment-reinitialization contract.

### Existing owner transfer and cleanup contracts

A unique named owner has one live-or-vacant state along every control-flow
path. By-value parameters transfer from an explicit named move or fresh
result and are dropped by the callee on normal or checked exceptional exit
unless moved again. Arguments are staged in evaluation order: an earlier
completed owner is dropped if a later argument fails before invocation. A
`bool`/`out` owner is published only on true; on false or exceptional exit its
completed private output is dropped without manufacturing a readable caller
value. Existing owners passed as output destinations are dropped only when
the call begins, not while earlier arguments are being evaluated. A successful
`out var` has a lexical cleanup scope. Initialization-loan output preserves
these publication and cleanup rules without becoming a general writable
reference.
User-defined unique receiver loans are synchronous and cannot be stored,
consumed or passed as values. Ordinary methods default to readonly loans.
A `mut` method requires a fully initialized named mutable owner, a writable
field path from it, or forwarding through its existing writable `this`;
it may update mutable scalar and Copy fields, not replace owning fields. Its
root is reserved before argument
evaluation, so argument calls cannot move, replace or otherwise invalidate
that owner. Mutation does not transfer ownership or change cleanup
responsibility; updates before a checked failure remain visible.
Unique child structs may be borrowed through writable paths only after the
whole root is initialized; the borrowed child is neither moved nor separately
destroyed, and eventual recursive cleanup sees its updated state. Partial
construction permits only readonly loans of initialized children. A writable
slice from a buffer field reserves its fully initialized root: range
expressions run before activation and cannot invalidate it; while active, the
view excludes other root access, including sibling fields. Normal scope exit
and checked unwind end only the loan, leaving prior writes intact and
recursive release to the owner. Such views cannot escape or be copied, passed,
stored or returned.
Cleanup is an explicit
`[NoThrow] [Cleanup] public void Close()`-shaped contract on a unique
instance method; the name itself has no special meaning. Compact owners can
contain primitive, nested Copy and owning fields; partial construction cleans
acquired fields in reverse acquisition order, and complete-value cleanup
recurses through owning fields. Cleanup cannot invoke a potentially throwing
operation. A statically constant
index proven within an immutable host-buffer grant may be used in NoThrow
cleanup; general indexing cannot. Managed roots preserve exact live values
through lexical and temporary scopes but do not implement unique cleanup or
general stored-view representation.

Exact rules for partial owning-field moves, expression temporaries, result
escapes, place disjointness, return anchors, interface refinement and dynamic
dependency binding remain open. Current executable boundaries are documented
in [implementation](../implementation.md) and staged in [roadmap](../roadmap.md).
An escape without a declared and checked contract diagnoses; it must not
silently box, clone, register, retain or copy.
