# Hosted view and owner profiles

This is an informative support inventory relocated from the lifetime
specification. It describes bounded hosted projections, not limits on the
[general dependency contract](../specs/language/dependencies-and-views.md).
The [implementation summary](../implementation.md) owns current support.
No general view ABI, stored-reference codec or concurrent protocol follows.
The shared [definitions](../specs/language.md#definitions-and-notation)
distinguish copying a descriptor from copying its referent or acquiring its
owner. The bounded loan terms below retain their implementation-specific
access and retirement restrictions; they are not a separate source lifetime model.

<a id="tokencursor-view"></a>
## Direct parser spans; TokenCursor prototype

The selected implementation direction is to pass a validated readonly
`Span<Token>` directly through synchronous parser calls. Use `nint` length and
index values, reject negative indexes, then perform full-width bounds checks
before address calculation. A span copy does not own the TokenSnapshot or
extend its lifetime; ordinary backing, access and retirement dependencies
remain required. There is no general 64-token length cap.

The current `TokenCursor` wrapper and its `AdvanceOne`/capture profile remain
in the prototype and tests, but the user declined this extra abstraction.
They are not an accepted library/API or persistence contract and should not
be the parser's selected path. Direct spans do not add general slicing,
writable/ref-returning access, stored/escaping views, persisted captures,
owner/guard retention or a public ABI.

Within this compilation-local profile, normalized result/capture contracts
drive TokenSpan call provenance. Backing dependencies follow unique
TokenSnapshot owner moves to the new live slot, distinguish later reuse of the
old slot, union at conditional sources, and retire with the last dependent
view. This does not establish the same transfer or generation model for
general references, GC roots, pinning, stored views or persisted contracts.

## Ordinary checked-reference aggregate fields

The source verifier and JIT now carry ordinary, non-GC/non-init checked
references in inline aggregate fields through nested copies, local field
rebinding, generic substitution, constructor/receiver calls and field loans.
Fresh inline unique constructors are covered only by the unapproved,
provisionally selected [candidate-95 extension](../proposals/reference-unique-constructor-field-provenance.md);
every summarized field subtree remains Copy. This is not a general
reference-place or managed-field profile.
Bodyless private persistence, Standard TRRM summary transport, GC roots,
pinning, and other nonlocal storage families remain unsupported.

## Compact unique-owner loans

The bounded hosted owner projection uses synchronous unique receiver loans;
it does not implement general stored or escaping receiver loans.
Ordinary methods use readonly loans. A mut method requires a fully
initialized named mutable owner, an eligible writable field path or
forwarding through writable this. Its root is reserved before argument
evaluation, so argument calls cannot move, replace or invalidate it.
This receiver-loan profile does not itself grant ownership replacement.
Separately, direct assignment and ordinary writable-reference stores support
live unique-field replacement on structurally tracked local owners when the
old value's transitive cleanup is proven nonthrowing. Exact-place reference
aliases remain valid; overlapping references into the retired value reject
the replacement.

Unique child structs are borrowed through writable paths only after complete
root initialization. A borrowed child is not moved or separately destroyed;
recursive cleanup observes its updated state. Partial construction supports
readonly loans of initialized children.
A writable buffer-field slice activates after range evaluation and reserves
the whole root, excluding other root access including sibling fields.
The bounded projection does not copy, pass, store, return or escape these views.
Ending the loan leaves prior writes visible and ownership with the root.

Its cleanup projection recognizes a selected Cleanup unique-instance method,
not the Close name alone. Cleanup does not require a general no-failure
promise; normal cleanup failures propagate without resuming generated child
retirement. Compact owners contain primitive, nested Copy and
owning fields. Partial construction cleans acquired fields in reverse
acquisition order; complete cleanup recurses through owning fields.
The bounded projection permits a statically constant, in-range index proved
within an immutable host-buffer grant; general indexing remains unsupported
in this profile.
Managed roots preserve live values but do not implement unique release or
general view anchors.

These implementation restrictions do not prohibit a separately checked
published source/capture relationship in the language specification.
Standard TRRM persistence, broader dependency/ABI support and additional
addressable storage families remain separate implementation work.
The general [owning-field specification](../specs/language/ownership-and-cleanup.md)
defines the logical move/repair and complete/partial cleanup boundaries; this
bounded implementation inventory does not restrict them.
