# Lifetime and access contracts

This document specifies accepted contracts for loans, views and published
members. [Dependencies and views](language/dependencies-and-views.md) owns the
whole-source schemas, retained destinations and checked witness refinements.
This chapter refines
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
| `ref<T>` | Readonly descriptor for a `T` place, not a snapshot or exclusive loan |
| `mut ref<T>` binding or parameter | Locally rebindable readonly descriptor; does not write the referent or rebind the caller's descriptor |
| `write ref<T>` | Descriptor permitting writes to a `T` place; slot updates do not grant arbitrary writes through values stored in that slot |
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
Descriptor assignment, explicit `ref place`, safe reference dereference, and
`->` member access follow the canonical
[reference chapter](language/references.md), including in generic code.

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
in [implementation](../implementation.md) and the
[development native profile](../implementation/native.md); staged work
is tracked in [roadmap](../roadmap.md). A slice does not thereby become the
proposed generic `Span<T>`.

Transfer/share proofs cover the complete qualified form, including backing
storage, retained roots and region dependencies. A readonly loan is not by
itself permission for concurrent use. The [memory model](memory-model.md)
defines checked sharing and trusted implementation boundaries; neither erases
the lifetime obligations published by a safe interface.
The [sharing and handoff chapter](runtime/sharing-and-handoff.md) additionally
requires actual alias unavailability, return/publication endpoints and guard
token bounds. Eligibility, a root or a cancellation/status observation does
not restore reserved authority or end a retained loan.

[Concurrent generation rules](runtime/concurrent-executions.md#8-initialization-retirement-and-reuse)
require actual initialization/publication and quiescence before retirement.
An ordinary update or descriptor rebinding is not a new storage generation;
ending storage and reusing its bytes is. Slot coherence does not preserve a
referent's lifetime, and equal addresses do not revive old aliases or tokens.
These are logical obligations, not a selected physical tag or reclamation API.

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

No universal return keyword is implied. [Standard lending iteration](language/lending-iteration.md)
uses separate readonly/writable reference-output roles and checked
element/backing/invalidation relationships, not an associated lifetime formal.
An associated iterator `Item<'a>` remains an unselected candidate; its syntax,
selection and ABI cannot be inferred from those standard roles.

## Parameter-only implicit readonly loans

`implicit ref<T>` is an accepted parameter-only modifier on a readonly
reference parameter, not `implicit T` or `in T`, a qualifier inside `T` or
`ref<T>`, a `view<T>` type, or a representation attribute. The caller passes
an ordinary expression and it is evaluated once. If it denotes an eligible
place, the call borrows that place's slot; otherwise the compiler assigns
a hidden local at that argument's original evaluation point in the current
enclosing lexical scope. This is the same scope as an explicit local declared
there, not a lifetime-only block and not a value hoisted to scope entry.
A skipped conditional or short-circuit argument creates no new value. If
this site has not previously initialized its local, that local remains
vacant; otherwise skipping leaves its existing value unchanged. The local
follows ordinary initialization, replacement, ownership/root cleanup and
unwind rules. Borrowing alone requires neither `[Copy]` nor ownership transfer.

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
assigns the hidden local once per reached evaluation. Source spelling resembling a field
does not turn a value-returning property into a loan of its backing storage.
If borrowing the existing place conflicts with its access or owner contract,
the call diagnoses rather than silently copying the value to evade the conflict.
The loan restricts access to the outer slot only. The complete qualified `T`
retains any write authority, backing, roots, ownership and dependency contracts
already carried by its value; a Copy descriptor copied out of the slot keeps
those same contracts. A readonly outer loan is not deep immutability, purity
or exclusivity.

### Repeated evaluations and reusable locals

Each source argument materialization site has one ordinary hidden mutable
binding per activation of its supporting lexical scope. Its equivalent
declaration is `init mut T tmp;`: storage starts vacant, and the first reached
evaluation initializes it. Each later reached evaluation assigns the new
value to that same binding under ordinary replacement and dependency rules.
Re-entering the supporting block creates a new scope activation, not another
instance accumulated in the previous activation.

The vacant declaration may appear earlier in the source expansion, but
neither constructs a default `T` nor evaluates the initializer. Evaluate and
assign at the original argument point, before forming the readonly reference.
Short-circuit or conditional non-evaluation leaves the binding's state
unchanged. Distinct materialization sites have distinct bindings, even when
they have the same complete `T`.

For example, within an existing enclosing block:

```terrum
while (Remember(Borrow(CreateOwner())))
{
    Body();
}
AfterLoop();
```

has this ordinary source expansion. The braces below show the existing block,
not an introduced lifetime-only scope. `tmp` is explanatory, not a name
available to the program:

```terrum
{
    init mut T tmp;
    while (true)
    {
        tmp = CreateOwner();
        if (!Remember(Borrow(ref tmp)))
        {
            break;
        }
        {
            Body();
        }
    }

    AfterLoop();
    tmp = _;
}
```

The original braced body remains a block with its original cleanup scope.
The same construction preserves transfers targeting the original loop:
`continue` returns to the assignment and condition, while `break` skips them.
Nested-loop transfers keep their original targets. Moving the assignment to
the original body's tail is not generally equivalent: `continue` can skip it,
and body-local cleanup would occur after rather than before the next
initializer. An `if`/`do` expansion is not a distinct lifetime policy and is
valid only when it preserves these same evaluation and transfer rules.

For a short-circuited source condition
`while (Ready() && Remember(Borrow(CreateOwner())))`, the corresponding
loop can instead be written:

```terrum
init mut T tmp;
while (Ready())
{
    tmp = CreateOwner();
    if (!Remember(Borrow(ref tmp)))
    {
        break;
    }
    {
        Body();
    }
}
```

`Ready()` is not added to the first example; it is an operand of this second
source condition. If initially false, the binding remains vacant. If false
after a previous reached argument, that argument's value remains live until
its ordinary lifetime end. Neither case calls `CreateOwner()`.

Replacement constructs the new complete value before retiring the old one,
as for an explicitly written mutable local. If construction fails, no new
value is published: a vacant binding stays vacant, or the old value stays
live until ordinary unwind or lifetime end. Partial construction cleans only
its acquired state. A successful replacement ends the old value's obligations
once and installs the new value; its value is not another scope-lived owner
alongside all preceding results. A later argument or call failure uses ordinary
cleanup of the already assigned local, not by-value argument ownership.

A live dependency on an old owner or backing can forbid replacement.
Materialization diagnoses the same conflict as the explicit assignment; it
cannot retain an old owner in hidden storage to evade it. Conversely, an
ordinary readonly reference to an aliasable Copy place can observe later
assignments to that same place when ordinary rules permit them. Reuse is not
a promise to preserve each earlier value as a snapshot. A forwarded dependency
that does not need the replaced local remains governed by its actual published
source contract.

The [ordinary loop scopes](grammar-spec.md#33-statements-and-local-declarations)
determine final retirement:

| Materialization location | Supporting scope and cleanup point |
|---|---|
| `while` or `do` condition | Existing enclosing block; the last live value survives loop exit and retires at that block's exit |
| `for` initializer, condition, or iterator | The `for` header scope; the last live value retires on leaving the loop |
| Synchronous or async iteration sequence expression | The iteration header scope, retained through required iterator close |
| Braced loop body | That execution of the original body block; its local retires on normal body exit, transfer, or unwind |
| Declaration precondition or postcondition | The isolated scope shared by that phase's clauses; cleanup before the phase completes, not at body or invocation exit |

The final false condition assigns a value if it reaches the argument.
Ordinary scope exit cleans the last live value once; a never-initialized,
moved, or explicitly ended binding has no live `T` cleanup. `tmp = _` in
the first expansion exposes the normal exit point; checked unwind performs
the same applicable obligations. Borrowers must end before invalidating their
backing, and cannot escape beyond the supporting scope. This does not move
observable cleanup to a borrower's last use.

The number of materialized bindings is bounded by source sites, not iteration
count. There is no implicit per-iteration `stackalloc`, retained-owner arena,
boxing, or collection growth. Ordinary initializer, assignment, rooting, and
cleanup effects still apply; this is not a general allocation-free promise.
Physical lowering may coalesce or eliminate storage only while preserving
the explicit source's place identity, observations, dependencies, effects,
failures, and cleanup. [Conformance cases](conformance/source-materialization.md)
give the required traces and diagnostics.
Declaration phase scopes follow the
[contract binding rules](language/initialization-and-flow.md#binding-pattern-declarations-and-scopes).

### Parameter applicability and call timing

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
source relationship and an admitted representation with all required
root/relocation support. Owner-dependent per-instance sizes, implicit per-slice allocations,
per-slice retains and a global pointer-to-owner registry are not defaults. A
tracked managed interior may move with GC; native allocations containing
managed elements additionally need scanning/barriers. Aliased managed interiors
cannot claim exclusive-loan optimizer facts. Whole-source selectors are
specified by [the dependency schemas](language/dependencies-and-views.md#source-schemas-and-targets);
the exact anchor layout and calling convention remain target-profile work.
The bounded TokenCursor and compact-owner projections are informative
[implementation inventories](../implementation/view-and-owner-profiles.md),
not exceptions that restrict the general language contract.

### Receiver-scoped versus receiver-unscoped

Here **receiver-scoped is a caller guarantee that an escaping result does not borrow the receiver's *own storage***. Receiver-unscoped instead permits such a borrow; it does not require one, promise any particular result lifetime, or erase forwarded backing and lease dependencies. A receiver-scoped implementation can forward backing held in the receiver's descriptor without borrowing the descriptor's own bytes. An implementation that *may* return a loan into the receiver's own fields cannot fulfill a receiver-scoped requirement.

| Interface receiver contract | Implementation may borrow receiver's own storage | Implementation forwards backing only, without borrowing receiver's own storage |
|---|---|---|
| Permits receiver-storage borrowing (unscoped) | Allowed; the result remains bounded by the receiver place | Allowed; the result retains its actual backing/lease dependencies |
| Guarantees no receiver-storage borrowing (scoped) | Rejected | Allowed; forwarding still cannot outlive backing, a guard or required disposal boundary |

The asymmetry is checked against the **declared implementation contract**, not inferred from its current body. In particular, a scoped requirement rejects an unscoped implementation even if today's implementation happens to forward only: that published permission could allow a later body to borrow receiver storage. Conversely, an unscoped requirement accepts a declared scoped implementation. Neither designation alone proves that an allocation is alive or that disposing its owner is safe.

The confirmed [C# 13 ref-struct interface proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-13.0/ref-struct-interfaces.md) has the analogous `[UnscopedRef]` interface/member asymmetry: an interface member with it permits an implementing member without it; an implementing member with it cannot satisfy a requirement lacking it. It also admits constrained ref-struct interface use without boxing; ordinary interface conversion still cannot box a ref struct. That proposal is **prior art**, not Terrum syntax, and does not supply Terrum's forwarded-backing or allocation/disposal model. The generic caller must obey the interface's broad public source contract, not the chosen implementation's narrower private behavior.

The checked [whole-source member relationship](language/dependencies-and-views.md#published-witnesses-and-generic-refinements)
publishes broad permissions and per-source ForwardsBacking refinements.
Direct `this` selects the receiver; `nameof(parameter)` selects a formal.
ForwardsBacking(this) is the receiver-scoped guarantee above, not a
runtime receiver mode or a dropped guard dependency.
`where T.via<IContiguousSequence<int32>>.AsSpan : [ForwardsBacking(this)]`
can require that exact published member fact in a generic declaration.
Without an exposed and established condition, generic callers retain the
broad requirement; specialization, devirtualization and implementation pinning
cannot supply a stronger source contract from today's body.
`Span<T>` implements AsSpan explicitly; concrete `span.AsSpan()` remains
intentionally unavailable. Text projection methods stay public.

[Selected lending refinements](language/lending-iteration.md#output-dependencies-and-checked-refinements)
separately define a fixed inherited init-reference-output relationship:
cursor-bound by default, with checked published narrowing of element sources
and affected-operation invalidation. They preserve the distinction between
cursor/receiver storage, forwarded backing and owner/guard cleanup.
The general source schemas compose with that exact init-output selector.
ForwardsBacking alone does not establish surviving advance/close invalidation,
MatchedAccess registration or a Span/List lending witness.

## Declaration defaults, exceptions and published compatibility

Public relationships come from declarations, language-versioned defaults and inherited/registered interface/construction contracts. Bodies are verified against those relationships. Private or closed-world body inference may optimize a specific implementation, but cannot silently alter an externally replaceable promise.

| Declaration shape | Public default |
|---|---|
| Synchronous borrowed input | No retention beyond call, except its declared result or capture relationship |
| View result with one eligible receiver/input | Broad source relationship: result may borrow that source's own storage **or** forward its backing/access dependencies; no unrelated capture |
| Multiple eligible sources | Explicit permitted source set; conservatively bound by **every** listed source, not by which branch today's body selects |
| Boolean-success `init ref<T>` without an explicit conditional postcondition | Destination starts vacant; `true` initializes it and `false` leaves it uninitialized |
| Other result type with `init ref<T>` and no explicit conditional postcondition | Destination starts vacant and must be completely initialized on every normal return |
| Explicit conditional postcondition for `init ref<T>` | The declared status/result relationship governs initialization; `ensure <expr>` uses an ordinary Boolean expression with [specified clause placement](grammar-spec.md#31-source-file-structure-and-declarations); correlated-output metadata remains to specify |
| Registered Core `Task<TResult>` result | Affine completion owner; may retain borrowed inputs/receiver through its checked completion/cleanup extent, whether or not the method is async; completed payload bounds remain separate |
| Implemented interface/registered builder | Inherit the source, access, output and effect contract; no implementation-by-implementation repetition |
| Registered generated producer | Synchronous creation captures staged inputs/receiver into one affine cursor; retain all declared backing/owner/guard/affinity bounds through release and required completion/close |
| Selected lending reference output | Initialize an inner ref<T>/write ref<T> descriptor with cursor-bound element/source dependencies unless an available checked published refinement applies; no element ownership or output-slot borrowing |
| Selected async cursor completion output | Reserve its vacant destination and mutable cursor access with the returned operation; observed true initializes, false/failure leaves vacancy after completion cleanup |

The last row is the [explicit async-value-iteration relationship](language/async-iteration.md),
not a changed default for arbitrary Task-returning functions. Pending caller
storage cannot be tested for initialization, reused or retired through an alias.
Retain destination/source/backing, roots, guards, affinity and a responsible
completion owner until the operation's terminal relation is observed.
Task status or cancellation request alone does not supply that relation.
Forwarding preserves exact phase/formal/output and operation identities;
an awaited Boolean refines only the corresponding output version.
Actual completion cleanup can finish before caller observation, but no caller
availability or discharge fact follows from unobserved private execution.
After observation, ordinary output dependencies and guarded cleanup remain.

Async lending applies that same reservation to its descriptor destination;
the published element loan then retains its independently declared backing.
Completion/task retirement does not end that loan, and a retained task does
not preserve expired backing. Every copied/reborrowed loan constrains later
advance/close according to the broad or available refined relationship.
The ordinary value iterator can also publish reference-valued items without
providing a lending witness or element-place binding.

[Generated producers](language/generated-producers.md) separate their
synchronous creation lifetime from retained lazy body state and each
advance/item/close extent. Creation cleanup cannot strand a capture requiring
pending async release. Yield retains entered scopes; close unwinds only those
scopes and does not replay normal completion. Physical frame placement or
default confinement does not repair an unavailable source/guard.

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
correlated-output metadata remains to be specified; clause placement follows
the source grammar. [Structural flow](language/initialization-and-flow.md)
defines state joins, saved outcomes, and guarded initialization/cleanup. The
boolean-success and other-result defaults above are language-versioned, not
inferred from method names. Historical implementation output modes are
documented separately in [implementation](../implementation.md); they do not
change these initialization contracts.

Core [Task<T>](language/task-responsibility.md) is an affine completion owner,
not a reusable observation handle. Ordinary await consumes it; a named owner
requires `await move task`. Explicit transfer to a checked structured owner
or supervisor preserves error, lifetime, output and shutdown obligations.
An exit that could abandon work or unobserved failure rejects on normal and
exceptional paths. Discarding a task neither inserts settlement nor cancels it
or releases its captures. Cancellation does not shorten a borrowed input's declared lifetime:
backing and guards remain live until the operation actually stops and performs
required cleanup.

A `require` expression or matching context lifts eligible destinations omitted
from the call; explicitly supplied destinations are not lifted, and partial
omission is permitted. The explicit `init ref<T>` mode and its checked,
published ordinary call-completion output contract establish eligibility;
no additional registration is required. Hidden destinations must be valid
ordinary initially vacant locals with the complete type and dependencies,
not arbitrary storage or fabricated defaults. This does not lift required
inputs or outputs published only at later task/producer completion.
Ordinary calls and statement-form Boolean `require` still supply destinations.
With all destinations supplied, matching uses the
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
Definition/import checking establishes the availability relations;
richer-status encoding, later-completion lifting and physical metadata
remain work. [Calls and inference](language/calls-and-inference.md#omitted-outputs-and-retained-observations)
defines supported source extraction: Copy patterns do not consume unique
outputs, and a bare lifted requirement transfers owning values only after
all required initialization checks. An explicit destination and a body move
provide selective unique extraction after matching/guards.
A value-form requirement with no omitted outputs performs the ordinary
Boolean check and yields unit; it does not extract explicitly supplied
destinations or change their published state on a false check.
A selected exception applies to the false condition only and does
not catch an exception raised while evaluating the call. These are target
contracts; current compiler support is documented in
[implementation](../implementation.md).

An omitted output site has one hidden initially vacant mutable local per
activation of its original supporting scope. A condition in `if`, `while`
or `do` uses the enclosing block; `for`/iteration expressions use their
header. Reentered bodies and contract phases have their ordinary separate
activations. The owner is not retired merely because an `if` finishes,
the condition becomes false, or a copied view ends. Its complete backing,
root, cleanup and independent obligations remain checked.

For a reached repeated call, ordinary receiver/argument staging and
invocation-only checks precede conditional, dependency-valid retirement
of old hidden values. Initialization then starts in vacant storage.
This is not implicit-reference materialization's build-before-drop
replacement: explicit `init` never clears a live destination.
Retained dependencies can prevent preparation; no private owner arena
repairs it. A skipped call retains storage without presenting an old value
as a new invocation's output. Final cleanup occurs at the supporting
scope's ordinary boundary, subject to all stricter type/backing obligations.

An eligible view source can provide backing/borrowed access; outputs and independent scalars do not automatically qualify. A numeric parser's result is independent; a parser returning a view of input text retains the input dependency. A parameterless projection generally uses its receiver. An independent freshly allocated result needs its own owner/root and cost contract. More than one eligible input is not resolved by inspecting current control flow. Returning a view does **not** authorize storing it in an unrelated receiver. Constructor capture, exported callback factories and registrations need a declared destination/boundary. Local closures carry actual capture relationships; consuming a unique owner retains its ordinary transfer/cleanup rules rather than becoming an invisible borrowed capture.

The accepted whole-source result annotation includes
`[return: DependsOn(nameof(left), nameof(right))]`; `nameof` is preferred
over literal parameter names. All possible declared input dependencies remain
obligations at the caller, without extending their permitted lifetimes.
See the [source contract](grammar-spec.md#result-dependency-declarations).
DependsOn names permitted whole sources; ForwardsBacking separately rules
out their own-storage alternatives. Captures specifies retention in the
constructed/existing receiver, named formal holder, result or admitted operation.
These remain checked relationships, not access grants or assertions that
backing lives long enough. Untargeted defaults, explicit destination/phase
rules and conditional member requirements are specified in
[dependencies and views](language/dependencies-and-views.md).
The provisional ViewOf sketches do not establish any additional source,
write, guard or retention protocol. Physical relationship codecs remain work.
Conditional output initialization is normally stated in declaration
postconditions, not a dedicated output attribute.
Writable guard projections lend existing exclusive authority and block
overlapping extraction; they do not upgrade a readonly guard. A normal outcome
leaves an initialization destination vacant only when its declared outcome
contract requires vacancy. A declared false/error outcome may instead publish
a complete live value, including initialized null, with its ordinary cleanup
obligations. Failed partial initialization and checked exceptional departure
under an `init` contract requiring vacancy clean the partial state and leave
the destination vacant. An ordinary writable-reference destination has no exceptional
rollback guarantee; postcondition failure does not make output publication a
transaction or undo body effects. No `default(T)` is synthesized. A status
local can carry the declared output relation to a later branch.
Async initialization targets operation-owned or valid rooted
caller storage through completion, never a borrowed startup stack address
smuggled into a suspended task. Fault capture requires trusted containment.
[Local failure payloads and catch inputs](language/local-failures.md)
retain the same complete source/guard/owner generations. A catch ref<E>
borrows the runtime-owned payload only through its live recovery extent;
handler exit ends that borrow before event transfer/retirement. A Copy input
retains its own actual backing dependencies, not an automatic deep snapshot
or captured event. Neither annotation nor trapping a fault can keep an exited
source frame valid.
[Core captured-event ownership](language/captured-exceptions.md) transfers the
same event from its source handler without extending those inputs or any
payload/cause source lifetime. A valid independent holder may keep the event
live through the original input extent; retirement must wait until its
dependent borrows end under ordinary cleanup order. Returning/storing the
owner retains all hidden sources, guards, generations, runtime/code and
protocol obligations. A staged consuming throw ends relevant inputs before
outward propagation; neither capture nor suspension inserts a pin, root,
owner arena, wait or thread-transfer permission.
[Requirement failure construction](language/requirement-failures.md) has
the same obligations even with zero supplied arguments. A constructor can
still have effects, acquire state or return a dependency-carrying payload;
eligibility/default selection grants none of those dependencies lifetime.
Its failure preserves initialized call outputs and ordinary supporting-scope
hidden holders, with actual partial/unwind cleanup and no source restoration.
Initialization publication is distinct from arbitrary writable `ref<T>`
access. Historical implementation `out` modes do not add a normative
parameter form or an exceptional rollback guarantee.

Matched access follows the relevant source through wrappers, interface dispatch,
generic calls, reflection and generated adapters. A structural readonly list may
yield independently writable class-handle capabilities only when those
capabilities are present in its element type; that does not grant writes to
list-owned slots, nodes or links. Deep graph-read is a future proposal, not the
v1 access model; it could restrict references traversed from a root. A
separate immutable-graph/freeze contract would need checked publication
excluding all writable aliases. Purity and stable reads are separate
questions under the [unified input-state contract](language/published-predicates-and-effects.md#aliasing-memory-state-and-sharing):
reading mutable receiver/input state can be pure, but relevant writes change
that logical input. Reuse needs unchanged read dependencies and surviving
backing; readonly access and equal root addresses are insufficient.
No separate `Stable` or receiver-read modifier is required.
The [NoAlias parameter schema](language/published-predicates-and-effects.md#noalias-parameters)
can require checked accessed-region separation for the whole invocation.
It does not acquire exclusive access, erase nested backing or extend a
returned loan; writes and retirement still invalidate their actual dependencies.

An interface witness must satisfy the interface's published requirements.
The [qualified-self rules](language/interface-witnesses.md#qualified-self-witness-projection)
permit only checked source-equivalent forwarding from an existing witness.
They preserve receiver capture, input transfer, result/backing relationships,
roots and cleanup; projection is neither permission to strengthen a readonly
result nor body-derived evidence of a stronger lifetime guarantee.

Store both the **broad interface contract** and the **resolved published implementation/member refinement**, including conditional relationships, source selectors, capture destinations, root/disposal requirements, rule versions and ABI layouts. Source compilation checks a member's claimed refinement against its authored body. An independently compiled generic caller sees the broad interface contract; only an explicit checked conditional contract can expose a type/witness refinement when its condition is proven. Ordinary binary consumers rely on published dependency metadata rather than re-proving dependency bodies, while still checking references, contract compatibility and caller use. Devirtualization, specialization and implementation pinning cannot change source validity or substitute body inference for a published relationship. Tightening an interface from permission to borrow receiver storage to a no-borrow requirement rejects existing implementations that published that permission, even if today's bodies do not use it. Broadening a published scoped implementation to permit receiver-storage borrowing invalidates existing callers that relied on its no-borrow guarantee; it requires a breaking revision or explicit checked adapter. Dynamic binding and AOT/JIT replacement checks must reject added capture, weakened access, changed view source, new cleanup obligation or invalid refinement, not merely compare CLR-signature shapes. Compatible changes may be replaced contract-bound; stronger implementation-only facts are not implicitly upgraded in consumer metadata. ABI layout and roll-forward need explicit version-aware validation.

## Ownership transfer and cleanup

### Explicit local lifetime end

The [value/execution rules](language/expressions-and-statements.md#using-results-and-explicit-discard)
require explicit handling of every normally produced non-unit result.
`_ = expression;` deliberately retires an unused result; `name = _;`
instead ends that named local. Neither form erases active-operation or
backing dependencies, fabricates a unit/default value, or grants authority.

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

The value, or an eligible structural owner's retained partial state, must
be definitely present. Ending partial state retires only its live fields;
it does not read or transfer a complete aggregate. Outstanding loans, derived pointers,
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
because it supports initialization. `init mut var` and `init mut T` separately
grant ordinary binding mutation and replacement after initialization. Mutable
bindings retain their existing write/initialization permissions. A new
initialization starts a new value lifetime with its own cleanup obligations,
not a revival of the old value.
Reads, borrows and another lifetime-end operation require definite liveness
along every incoming control-flow path.

See the [statement grammar](grammar-spec.md#33-statements-and-local-declarations).
This is accepted target direction; current vacant locals support checked
initialization calls, not this early-end/assignment-reinitialization contract.

### Owner transfer and cleanup contracts

A unique named owner has one responsible state along every control-flow
path, retaining per-field facts when it is structurally partial. By-value
parameters require a complete transferred value from an explicit named move or fresh
result and retain their callee's checked retirement/transfer obligation on
normal and exceptional exits. Protocol owners such as Task<T> may require
explicit settlement first. Arguments are staged in evaluation order: an earlier
completed owner retains that same obligation if a later argument fails before invocation.
Initialization-loan destinations are vacant; passing one does not destroy an
existing owner as an implicit output adaptation. Publication follows the
declared normal outcome, including a declared live failure output. An outcome
requiring vacancy cleans any privately constructed owner without manufacturing
a caller-readable value. Checked exceptional initialization follows its
vacancy and partial-cleanup contract. A newly declared `init var` destination
has its ordinary lexical cleanup scope.
Unique receiver and field loans use their actual access, live-state and
owner/guard contracts. Borrowing neither moves the referred-to owner nor
duplicates its cleanup; mutation does not transfer responsibility and updates
before checked failure remain visible. A declared result or capture can retain
a loan only with its full checked source and destination extent. There is no
general ban on storing/passing/returning a view merely because its source is
unique, and no general exclusion of sibling fields from write access alone.
Separately selected exclusive loans retain their required root reservations.
[Ownership and cleanup](language/ownership-and-cleanup.md) defines structural
partial field moves under ordinary write/init authority, checked repair and
replacement, and the complete-receiver boundary for authored Cleanup.
Partial state retains live children and is not whole vacancy.

Complete retirement runs the selected NoThrow/nonsuspending readonly Cleanup
body once while the receiver is complete, then automatically retires fields
in reverse declaration order. The Close name alone registers nothing.
Without an authored body, cleanup is derived from the remaining live fields.
Failed unpublished construction instead unwinds actual acquired values in
reverse acquisition order without calling the incomplete parent's Cleanup.
Borrowed children are not independently destroyed; roots and retained
dependencies remain subject to their actual field retirement contracts.
Managed roots do not release unique resources or keep disposed backing valid.
The [hosted owner/view inventory](../implementation/view-and-owner-profiles.md)
records narrower supported field/update/loan shapes and cleanup proofs.

General source/capture/witness and logical owning-field transitions are
specified. [Windows x64 native](targets/windows-x64-call-interface.md#6-complete-owners-and-retirement-entries)
specifies complete fixed rootless owner transfer and call-extent stable borrow
projections without choosing a stored reference/owner descriptor.
Managed ownership, general return anchors, physical partial-state and dynamic
dependency codecs remain separate work.
Current executable boundaries are documented
in [implementation](../implementation.md) and staged in [roadmap](../roadmap.md).
An escape without a declared and checked contract diagnoses; it must not
silently box, clone, register, retain or copy.
Task-bearing owning state also retains the
[task settlement contract](language/task-responsibility.md#every-exit-settlement-and-ordinary-destruction).
Its generated destruction is not permission to block, suspend, cancel or
silently supervise a pending child operation.
