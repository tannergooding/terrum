# References and explicit indirection

This chapter owns source reference formation, descriptor use, explicit
indirection, and reference-parameter identity. [Lifetimes](../lifetimes.md)
owns the supporting storage, root, backing, and cleanup obligations;
[source productions](../grammar-spec.md#32-types-parameters-and-arguments)
provide the grammar. Current implementation support is not a restriction on
these language contracts.

## Descriptors, referents, and bindings

`ref<T>` denotes a checked, non-owning reference descriptor for a `T` place.
`ptr<T>` denotes an explicit pointer descriptor. An expression naming either
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

Assignment to a mutable descriptor place evaluates the destination once,
then the source once, and replaces that descriptor under ordinary assignment
and dependency rules. `a = b` does not copy or assign the pointed-to `T`;
no special `= ref` rebinding rule exists. An eligible descriptor copy
preserves every referent, authority, root, backing, and lifetime dependency.
It does not copy `T`, transfer its owner, acquire a pin, or extend its lifetime.
Rebinding also cannot retire a descriptor whose outstanding dependencies
still require it.

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
To replace that slot, explicitly borrow it with an appropriate
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
`ref b` instead borrows the storage holding `b`, producing a reference to
the descriptor slot: `ref<ref<T>>` when that slot is readonly. If the slot
is writable, its outer reference may retain that write authority.
`ref *b` explicitly reborrows the original referent with no longer lifetime
or greater authority.

Ordinary formation acquires no pin. The explicitly declared
`pin ref<T>` local acquisition follows the checked pin-acquisition rules in
the [source grammar](../grammar-spec.md#32-types-parameters-and-arguments).
Ordinary reference formation to movable managed storage retains whatever
root/base information its runtime requires; it does not promise a stable raw
machine address.

## Dereference and member access

`*r` for a live `ref<T>` descriptor produces its `T` referent place.
Dereferencing a valid ordinary reference is safe. Reading or writing the
result then requires the referent's initialization and access contract;
dereference itself does not copy `T`.

`*p` for `ptr<T>` produces the corresponding place only under unsafe
authorization and the pointer's validity, alignment, provenance, and lifetime
obligations. Identical syntax does not make a pointer into a checked reference.
Unsafe authorization does not waive a statically known missing pin, invalid
lifetime, or insufficient write authority.

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

## Parameters and call adaptation

Only canonical reference type forms occur in signatures. Parameter-only
`ref T`, `mut ref T`, `out T`, `T&`, and `T*` are not alternate spellings.
[Typed catch references](local-failures.md#4-catch-subjects-and-binding-modes)
use those same descriptor semantics: `catch (ref<E> failure)` borrows the
active E payload, and `failure->Member` explicitly accesses it. Catch inputs
do not admit implicit reference formation, write/pin authority or owner
consumption. Bare rethrow closes that handler's borrows before event transfer.
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
| `init ref<T>` | Lend one vacant `T` destination under the declared initialization/publication relation |

Explicit calls use an existing descriptor or `ref place`, not a separate
argument passing-mode marker. An `init` call uses `Create(init destination)`;
`init var destination` declares and supplies a newly typed vacant local under
the [output and inference rules](../language-contracts.md).
An initialization-loan parameter is not rebindable: its promised destination
cannot silently be replaced with another place. Ordinary reference and
initialization-loan parameters have no default argument.
`[NoAlias]` or its exact parameter selector can publish a separate caller
separation requirement. It uses the staged referent/backing and destination
state, not the parameter descriptor slot's address; adaptation and local
rebinding do not manufacture that proof.

Before initialization, the destination cannot be read as `T`, and the loan
cannot be converted to an ordinary initialized-reference contract, captured,
or passed opaquely through an unconstrained generic value parameter. It may
be forwarded to a compatible initialization contract, preserving destination
identity and cleanup ownership.
After complete initialization, ordinary uses still obey the declaration's
publication and exceptional-cleanup obligations. A reference to a vacant
destination is not evidence that its `T` is live.

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

`implicit` applies only to an ordinary unpinned readonly outer `ref<T>`,
although its complete `T` may itself carry write authority. `mut implicit
ref<T>` permits local descriptor rebinding without changing that adaptation.
`params` is a separate final collection parameter, not a reference passing
mode. Lambda parameters use the same canonical forms and contracts as named
callables.

## Initialization destination projection

An `init ref<T> destination` parameter has a live, fixed descriptor binding
and a separate destination state. `*destination` projects the `T` place
without reading `T`; its initialization mode permits complete initialization
when vacant, not ordinary replacement after it is live. The unqualified
outer `ref<T>` remains readonly for ordinary post-initialization access.
Neither the mode nor write authority inside `T` grants an outer referent-write
upgrade.

`*destination is init` tests the destination state without reading its value.
`destination is init` instead tests the already live descriptor binding and
cannot establish that its referent is initialized. A value predicate uses
explicit indirection, such as `*destination > int32(0)`, only after its
read is structurally valid. `destination->Member` likewise requires the
eligible initialized referent/member access.

A caller alias cannot inspect or initialize a destination reserved by an
[async completion-output operation](async-iteration.md#invocation-and-pending-initialization).
Its producing operation retains its checked init authority, but private writes
do not publish caller availability. The descriptor's own state remains distinct;
only checked completion observation releases the caller's pending reservation.

```terrum
public void Create(init ref<int32> destination)
    ensure *destination is init
{
    *destination = int32(1);
}
```

Forwarding uses `Helper(init *destination)`, supplying the same vacant
`T` place, not `Helper(init destination)`, which would target the descriptor
binding. Forwarding preserves complete type, source identity, partial
cleanup, and publication obligations. Ordinary `ref *destination`
formation requires an initialized referent; the initialization mode is not
an escape from ordinary borrowing checks.

`nameof(destination)` still selects the parameter declaration, not its
referent value. Generic substitution adds no transparent indirection:
for `T = ref<U>`, `*destination` is a `ref<U>` destination place.
[Structural flow](initialization-and-flow.md) keeps descriptor, destination,
initialization, and presence facts separate.

## Lending outputs and iteration aliases

[Lending iteration](lending-iteration.md) initializes a descriptor value through
`init ref<ref<T>>` or `init ref<write ref<T>>`. The destination holds the inner
reference; its outer init mode grants neither element-write authority nor
ownership of T. Ordinary manual descriptor uses retain explicit indirection.

The iteration-specific `ref`/`mut ref` prefix instead binds an alias to the
published element place. Its name denotes exact T, not the output descriptor
slot; `ref name` reborrows the source element. Ending that binding releases its
loan, not T, and does not end copied/reborrowed loans. This source construct
does not change ordinary `mut ref<T>` descriptor rebinding, nested init-output
invariance, aliasability or reference-valued by-value iteration.

[Generated producer creation](generated-producers.md#creation-and-captured-state)
retains ordinary reference/receiver captures and their actual backing.
It does not capture init-reference formals for later initialization;
the generated advance's selected init destination is a separate operation.

## Signature identity

Normalize the complete parameter type before comparing signatures.
Reference and pointer constructors, inner qualification, and immediate
`write`/`pin` qualifications participate in identity. The initialization-loan
mode is distinct from an ordinary initialized reference of the same `T`.

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
