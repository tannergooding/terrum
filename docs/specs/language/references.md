# References and explicit indirection

This chapter owns source reference formation, descriptor use, explicit
indirection, and reference-parameter identity. [Lifetimes](../lifetimes.md)
owns the supporting storage, root, backing, and cleanup obligations;
[source productions](../grammar-spec.md#32-types-parameters-and-arguments)
provide the grammar. Current implementation support is not a restriction on
these language contracts.
The shared [definitions](../language.md#definitions-and-notation) introduce
copying, aliasing, references and lifetime before the technical loan terms.

## Descriptors, referents, and bindings

`ref<T>` denotes a checked, non-owning reference descriptor for a `T` place.
Checked references are not inherently stack-only. Stored, returned, captured
or suspended uses still need their complete contracts and an admitted
representation, not merely a matching source spelling.
`ptr<T>` denotes an explicit non-null raw pointer descriptor. Its nullable
form is `ptr<T>?`; ordinary presence checks refine that optional descriptor,
not the pointee's validity or lifetime. An expression naming either
descriptor has that complete descriptor type; it does not automatically
evaluate to its referent. This rule is unchanged under generic substitution.

`mut` permits rebinding a declaration's own place. `write` on an indirection
permits writes to its immediate referent, subject to that referent's ordinary
member and storage rules. The two permissions are independent:

| Declaration | Descriptor rebinding | Immediate referent writes |
|---|---|---|
| `ref<T> r` | No | No |
| `mut ref<T> r` | Yes | No |
| `write ref<T> r` | No | Yes |
| `mut write ref<T> r` | Yes | Yes |

The corresponding distinction applies to `ptr<T>`. `write` does not bypass
an immutable field, a readonly containing place, a nominal mutation contract,
or an ownership/dependency restriction.
Pointer nullability and pointee access are independent: `ptr<T>?` is an
optional pointer to `T`, `ptr<T?>` is a non-null pointer to an optional `T`
slot, and `ptr<T?>?` carries both layers. `write ptr<T>` grants access
through the pointer to its immediate `T` referent; it does not make the
pointer nullable or establish that the address is valid. A presence check
must precede unsafe dereference of `ptr<T>?`.

Assignment to a mutable descriptor place evaluates the destination once,
then the source once, and replaces that descriptor under ordinary assignment
and dependency rules. `a = b` does not copy or assign the pointed-to `T`;
no special `= ref` rebinding rule exists. An eligible descriptor copy
preserves every referent, authority, root, backing, and lifetime dependency.
It does not copy `T`, transfer its owner or acquire a root/pin; it is not a
general lifetime-extension mechanism. Copying a GC-root handle retains that
same root under its declared backing contract.
Rebinding also cannot retire a descriptor whose outstanding dependencies
still require it.

The same distinction applies when a value carries a reference in a field:
copying the eligible containing value copies the descriptor and preserves its
referent's dependencies. A reference to that field's slot instead depends on
the containing field storage. Nesting does not create a different lifetime
system or make the referent outlive its backing.

For example:

```terrum
mut int32 first = int32(1);
mut int32 second = int32(2);
mut write ref<int32> current = ref first;
write ref<int32> next = ref second;

*current = int32(3);
current = next;
*current = int32(4);
```

The first write changes `first`; the second changes `second`. Assignment
to `current` changes only that local descriptor. Rebinding a by-value
reference parameter likewise does not rebind the caller's descriptor slot.
To replace that slot, explicitly reference it with an appropriate
`write ref<ref<T>>` contract.

Ordinary references are not exclusive loans. Readonly access is neither a
snapshot nor a promise that other aliases cannot write. A separate owner,
guard, or exclusion protocol may restrict copying or simultaneous access;
the complete contract, not the spelling `write`, establishes that restriction.
The optional [NoAlias parameter contract](published-predicates-and-effects.md#noalias-parameters)
requires separation of checked accessed regions for a call. It is not an
exclusive loan or new mutation/lifetime authority.

[Whole-source declarations](dependencies-and-views.md) distinguish a
reference source's selected referent place from backing forwarded through
that place. ForwardsBacking excludes the former; copying the original ref<T>
descriptor does not convert its referent into an independent backing source.
DependsOn names a result/init-output relationship, while Captures separately
authorizes retained state in its exact destination. All copies/reborrows
preserve the complete source versions, backing and capabilities.

## Formation

`ref place` evaluates and validates one addressable place, then creates a
reference descriptor to it. Its receiver and indices evaluate once in their
ordinary source order. The place must be live and completely initialized.
An independently live child of a partial structural unique aggregate can
be projected under its [field-state contract](ownership-and-cleanup.md);
this does not authorize a reference to the incomplete aggregate as a whole.
An inhabited `void` local/field is a logical place without an exposable
payload address. It, a computed value, and a value-returning property are not
borrowable places merely because an expression has that type.

Formation preserves the place's available immediate access and its complete
stored `T`. An expected readonly reference may attenuate writable access;
formation never upgrades readonly storage. Without an expected reference
type, the descriptor reflects the place's existing immediate access.
Write authority inside a stored handle is independent of slot writability:
a readonly slot containing `write C` can produce `ref<write C>`, not
`write ref<C>`.

Field move, replacement and retirement also preserve every surviving
storage/backing dependency. A writable reference alone cannot authorize
partial moves from a type whose authored Cleanup requires a complete receiver,
or make the field of a readonly containing path mutable.
An admitted [shared/handoff path](../runtime/sharing-and-handoff.md) also
retains actual guard, publication, affinity and alias reservations. A live
descriptor is not permission to access its reserved referent; descriptor
rebinding/ending and referent authority remain distinct.

An existing reference is already a descriptor value. For `ref<T> b`,
using `b` passes or copies that descriptor subject to its complete contract.
`ref b` instead references the storage holding `b`, producing a reference to
the descriptor slot: `ref<ref<T>>` when that slot is readonly. If the slot
is writable, its outer reference may retain that write authority.
`ref *b` explicitly reborrows the original referent with no longer lifetime
or greater authority.

Ordinary `ref<T>` formation designates stable normal backing and acquires no
pin. GC-backed storage instead uses the distinct `gc ref<T>` locator. A fresh
`ref place` may acquire this root when its complete expected type is
`gc ref<T>`; without that expected type, write `gc ref place`. Formation is
limited to one completely initialized, addressable place in GC-backed
storage. The locator keeps that exact place reachable but may move with its
backing, grants no sibling-field access, and does not own a native resource or
preserve a released guard.

A `ref<T>` to a subplace in normal class or box backing carries bounded
support for that backing and may outlive the local holding the class/box
handle. It does not retain a reference to the handle's own local slot or
extend an explicit owner/guard endpoint. A `gc ref<T>?` must be
presence-tested and unwrapped to `gc ref<T>` before reborrowing; the optional
root itself cannot produce an optional pinned reference.

`ref *root` explicitly reborrows a `gc ref<T>` referent as ordinary stable
`ref<T>`. It pins the exact designated place's backing for the bounded
reborrow; it does not promote an existing ordinary reference or acquire
authority beyond the root's access. Copying the resulting reference preserves
the pin dependency. Raw pointer extraction from this stable reference remains
a separate explicit unsafe operation.

If runtime resources required for root or pin support cannot be acquired, the
operation follows the existing runtime-failure contract. It publishes no
root or stable reference on failure and releases any partial acquisition;
failure does not produce an absent optional root or optional pinned reference.

## Dereference and member access

`*r` for a live `ref<T>` descriptor produces its `T` referent place.
Dereferencing a valid ordinary reference is safe. A `gc ref<T>` identifies
its exact rooted place but does not provide the ordinary stable-address
contract; use `ref *root` when an ordinary reference is required. Reading or
writing the resulting place still requires its initialization and access
contract; dereference itself does not copy `T`.

`*p` for `ptr<T>` produces the corresponding place only under unsafe
authorization and the pointer's validity, alignment, provenance, and lifetime
obligations. Identical syntax does not make a pointer into a checked reference.
Unsafe authorization does not waive a statically known invalid lifetime or
insufficient write authority. It also does not replace the `gc ref<T>` root
and `ref *root` pin needed to obtain a stable GC-backed address.

`r.Member` performs ordinary lookup on the complete reference descriptor
type. It does not forward to `T`, and no descriptor members are synthesized
by this rule. `r->Member` is exactly `(*r).Member`, preserving the place
category, access, roots, dependencies, and evaluation order. The shorthand
works identically for `ptr<T>` but retains its unsafe dereference requirement.
It may select a field, property, method, or eligible extension under ordinary
lookup; it does not invent a separate member or overload search.

A descriptor and an indirect method receiver are evaluated once before
arguments. Rebinding the source descriptor in a later argument does not
redirect the captured receiver. Neither `->` nor grouping introduces a
payload copy, a move, a pin, or an earlier dereference lifetime endpoint.

Class-handle `C` remains a reference-object value, not an explicit address
descriptor. `c.Member` uses ordinary object-member access; `*c` and
`c->Member` are not admitted just because `C` is a class. If `r` is
`ref<C>`, `*r` is the handle slot and `r->Member` first obtains the stored
handle, then performs ordinary `C` member access. These access axes remain
separate:

| Descriptor | Replace handle in borrowed slot | Mutate object through stored handle |
|---|---|---|
| `ref<C>` | No | No |
| `write ref<C>` | Yes | No |
| `ref<write C>` | No | Yes |
| `write ref<write C>` | Yes | Yes |

An optional descriptor must be established present before dereference;
this chapter adds no `?->` or implicit null handling. Prefix dereference
retains ordinary precedence. Use `(*r).Member` or `r->Member`, not ambiguous
`*r.Member`. Nested indirection uses grouping, such as `*(*r)`; it does not
admit the unassigned `**` operator run.

## Initialization references

`ref<init T>` is a complete checked-reference type distinct from `ref<T>`.
It denotes a live descriptor to one fixed place `P` whose exact complete
type is `T`. The descriptor is non-owning and non-Copy; it reserves
initialization authority for that destination without making vacant storage
readable or transferring ownership of `T`.

Form a local capability explicitly with `ref init place`. A direct call uses
that same formation, for example `Construct(ref init destination)`, and binds
to a parameter whose ordinary complete type is `ref<init T>`.
Evaluate the place once, preserve its identity and backing, and require the
selected destination to be eligible and vacant. An expected type, ordinary
`ref place`, implicit-reference adaptation or retyping cannot create
initialization authority. The same source form applies to inline values,
class handles, pointers, views and nested references; it initializes the
exact stored `T` place, not a value or referent nested inside `T`.

Forwarding a `ref<init T>` parameter to a compatible helper is a scoped
reborrow of the same reservation. The caller's capability is suspended while
the child view is active, the destination identity remains fixed, and the
caller resumes with the actual post-call state. A longer-lived transfer is
explicit and consuming. No copy or independent second reservation is
created. Direct output parameters cannot be rebound; a local `mut
ref<init T>` may rebind only after child reborrows end and its old reservation
can be released or transferred without abandoning partial initialization.

Before `P` is completely initialized, operations may initialize `P` under
the declared output contract but may not read `T`, replace a live value, or
convert the capability to an ordinary initialized reference. After complete
initialization, flow consumes only the `init` capability and preserves
independent access qualifiers at their positions: `ref<init T>` becomes
`ref<T>`, `ref<init write T>` becomes `ref<write T>`, and
`write ref<init T>` becomes `write ref<T>`. A separate readonly projection
may attenuate write authority where ordinary backing, dependency, settlement
and lifetime rules allow it. Initialization completion does not create new
write authority or permit reinitialization.

`ref<init write C>` can initialize a destination containing a writable class
handle, preserving object-write authority without permitting later
replacement of the handle slot. `write ref<init T>` is also admitted: it
requires initialization before reads, then permits repeated whole-place
replacement. On successful initialization its type attenuates to
`write ref<T>`, preserving outer write authority but consuming the one-time
initialization capability. For inline `S`, `write ref<S>` permits whole-place
replacement independently of `S`'s own mutation capability, whereas
`ref<write S>` requires an explicitly mutation-capable `S`. The extra
class-handle indirection remains significant; these forms are not universal
aliases.

GC-rooted initialization capabilities are not admitted. `gc ref<T>` is
formed only for a completely initialized exact place; `gc ref<init T>` and
any implicit pin acquisition for `ref<init T>` are not selected forms.

Ending a local capability after child reborrows and dependent loans end
releases its reservation. If `P` remains vacant, it remains vacant; if
initialization completed, the live `T` remains under its existing holder and
cleanup contract. Partial acquisitions must be settled or transferred under
the shared failure rules before a helper returns. Ending a capability does
not roll back initialization, synthesize a default, or run complete-`T`
cleanup on vacant storage. Ordinary local retirement and scope exit end the
loan; there is no last-use inference or special release syntax.

An uncompleted `ref<init T>` capability cannot be retained in fields,
results, captures, async state or opaque containers. Generic callables may
accept it directly with generic `T`. Once initialization completes and the
capability attenuates to ordinary `ref<T>`, that ordinary reference may be
retained only under its normal backing, dependency, settlement and cleanup
contracts. This is not a stack-only restriction on ordinary references.
Declaration `init ref<T> r;` remains different: it makes `r`'s descriptor
storage vacant; it does not create a live descriptor to a vacant `T`.

## Parameters and call adaptation

Only canonical reference type forms occur in signatures. Parameter-only
`ref T`, `mut ref T`, `out T`, `T&`, and `T*` are not alternate spellings.
[Typed catches](local-failures.md#4-catch-subjects-and-bindings)
bind managed exception objects or their common `IException<_>` view; they do
not borrow a typed payload through `ref<E>`. Catch inputs do not admit
implicit reference formation, write/pin authority or owner consumption.
[Lexical Capture](captured-exceptions.md) can transfer the active event while
that same input stays valid, but gives it no longer scope or authority.
The captured holder must keep the payload live until those uses end;
retirement/propagation cannot invalidate them. Borrowing a CapturedException
holder slot is a separate ordinary reference and cannot supply a consuming
throw or permit conflicting holder movement.
The parameter declaration's binding permission remains local:

| Form | Caller and callee contract |
|---|---|
| `ref<T>` | Pass an eligible readonly descriptor value |
| `write ref<T>` | Pass an eligible writable descriptor; dereference accesses caller storage |
| `mut ref<T>` | As `ref<T>`, with a locally rebindable parameter descriptor |
| `mut write ref<T>` | As `write ref<T>`, with a locally rebindable parameter descriptor |
| `implicit ref<T>` | Use the same readonly semantic signature with the specified source-call adaptation |
| `ref<init T>` | Receive a live, fixed descriptor to a vacant `T` destination under the declared initialization/publication relation |
| `write ref<init T>` | Receive the same fixed vacant destination; after successful initialization, permit repeated whole-place writes through the retained outer capability |

Explicit calls use an existing descriptor or explicit formation, not a
separate argument passing-mode marker. A `ref<init T>` parameter uses
`Create(ref init destination)`; `init var destination` declares vacant
storage, which is passed by forming `ref init destination`, under the
[output and inference rules](../language-contracts.md). An
initialization-reference parameter is not rebindable: its promised
destination cannot silently be replaced with another place. Ordinary
reference and initialization-reference parameters have no default argument.
`[NoAlias]` or its exact parameter selector can publish a separate caller
separation requirement. It uses the staged referent/backing and destination
state, not the parameter descriptor slot's address; adaptation and local
rebinding do not manufacture that proof.

`implicit ref<T>` changes argument adaptation, not reference value semantics.
An already compatible descriptor passes directly. Otherwise an eligible `T`
place is borrowed, or a `T` value is evaluated once into an ordinary hidden
local in the existing enclosing scope and then borrowed. The distinction uses
complete types: a parameter `implicit ref<ref<U>>` may borrow a descriptor
slot; it does not reinterpret an existing `ref<U>` as a reference to `U`.
Materialization cannot evade an invalid borrow, acquire write authority, or
shorten lifetime obligations.

Each source materialization site uses one initially vacant mutable local per
activation of its existing supporting scope. Repeated evaluation assigns that
same local under ordinary initialization, replacement, and dependency checks;
it does not retain a new owner on every loop iteration. A skipped evaluation
leaves the existing state unchanged. [The source expansion](../lifetimes.md#repeated-evaluations-and-reusable-locals)
defines the supporting scopes and cleanup points.
The local's compiler-only mutation permission does not grant write authority
through the parameter's readonly reference.

`implicit` applies only to an ordinary readonly outer `ref<T>`,
although its complete `T` may itself carry write authority. `mut implicit
ref<T>` permits local descriptor rebinding without changing that adaptation.
It cannot form or forward the distinct `gc ref<T>` root type. Existing
backing and pin dependencies on a supplied stable `ref<T>` remain attached.
`params` is a separate final collection parameter, not a reference passing
mode. Lambda parameters use the same canonical forms and contracts as named
callables.

## Initialization destination projection

A `ref<init T> destination` parameter has a live, fixed descriptor binding
and a separate destination state. `*destination` projects the exact `T`
place without reading `T`; its initialization capability permits complete
initialization when vacant, not ordinary replacement after it is live.
For `write ref<init T>`, the first successful initialization consumes the
init capability and leaves `write ref<T>` authority for later whole-place
replacement. Inner `write T` remains distinct and permits mutation through
the stored type only where that type allows it.

`*destination is init` tests the destination state without reading its value.
`destination is init` instead tests the already-live descriptor binding and
cannot establish that its referent is initialized. A value predicate uses
explicit indirection, such as `*destination > int32(0)`, only after its
read is structurally valid. `destination->Member` likewise requires an
eligible initialized referent/member access.

A caller alias cannot inspect or initialize a destination reserved by an
[async completion-output operation](async-iteration.md#invocation-and-pending-initialization).
Its producing operation retains its checked init authority, but private writes
do not publish caller availability. The descriptor's own state remains distinct;
only checked completion observation releases the caller's pending reservation.

```terrum
public void Create(ref<init int32> destination)
    ensure *destination is init
{
    *destination = int32(1);
}
```

Forwarding uses `Helper(ref init *destination)`, supplying a scoped reborrow
of the same reserved `T` place, not `Helper(ref init destination)`, which would
target the descriptor binding. The child preserves complete type, source
identity and publication obligations; the caller resumes with the actual
post-call state. Ordinary `ref *destination` formation requires a completely
initialized referent; initialization authority is not an escape from ordinary
borrowing checks. After complete initialization, flow may attenuate the
capability to `ref<T>` under the ordinary backing and dependency rules.

`nameof(destination)` still selects the parameter declaration, not its
referent value. Generic substitution adds no transparent indirection:
for `T = ref<U>`, `*destination` is a `ref<U>` destination place.
[Structural flow](initialization-and-flow.md) keeps descriptor, destination,
initialization, and presence facts separate. Declaration `init ref<T>
destination;` still means that the descriptor slot itself is vacant; it is
not a live `ref<init T>` to a vacant referent.

## Lending outputs and iteration aliases

[Lending iteration](lending-iteration.md) initializes a descriptor value
through `ref<init ref<T>>` or `ref<init write ref<T>>`. The destination holds
the inner reference; its initialization capability grants neither
element-write authority nor ownership of T. Ordinary manual descriptor uses
retain explicit indirection.

The iteration-specific `ref`/`mut ref` prefix instead binds an alias to the
published element place. Its name denotes exact T, not the output descriptor
slot; `ref name` reborrows the source element. Ending that binding releases its
loan, not T, and does not end copied/reborrowed loans. This source construct
does not change ordinary `mut ref<T>` descriptor rebinding, nested
initialization-output invariance, aliasability or reference-valued by-value
iteration.

[Generated producer creation](generated-producers.md#creation-and-captured-state)
retains ordinary reference/receiver captures and their actual backing.
It does not capture `ref<init T>` formals for later initialization;
the generated advance's selected init destination is a separate operation.

## Signature identity

Normalize the complete parameter type before comparing signatures.
Reference and pointer constructors, inner qualification, and immediate
`write`/`gc` backing qualifications participate in identity. Pin support is
a backing dependency, not a type qualifier. `ref<init T>` is
distinct from ordinary `ref<T>` of the same `T` and has its own complete
TypeSpec identity.

Local binding `mut`, parameter labels, defaults, and the `implicit` adaptation
marker do not distinguish overloads. Explicit and implicit readonly
`ref<T>` cannot define two otherwise identical signatures. A by-value `T`
and a `ref<T>` are distinct; ordinary `F(value)` prefers the applicable
by-value candidate over implicit materialization under existing ranking.
Explicit descriptor arguments have descriptor types, including in generic
inference; they do not silently infer or pass a dereferenced `T`.
NoAlias is likewise a published call contract, not reference type identity
or a new overload discriminator.

The published representation records complete reference types and the
initialization mode, not legacy readonly/writable-reference passing enums.
Source-call adaptation must be explicitly published for independently
compiled callers. A parameter's local descriptor mutability is a body fact,
not additional caller-slot authority.
