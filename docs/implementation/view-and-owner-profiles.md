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

## Compact unique-owner loans

The bounded hosted owner projection uses synchronous unique receiver loans;
it does not implement general stored or escaping receiver loans.
Ordinary methods use readonly loans. A mut method requires a fully
initialized named mutable owner, an eligible writable field path or
forwarding through writable this. Its root is reserved before argument
evaluation, so argument calls cannot move, replace or invalidate it.
The bounded update surface covers mutable scalar and Copy fields, not
owning-field replacement.

Unique child structs are borrowed through writable paths only after complete
root initialization. A borrowed child is not moved or separately destroyed;
recursive cleanup observes its updated state. Partial construction supports
readonly loans of initialized children.
A writable buffer-field slice activates after range evaluation and reserves
the whole root, excluding other root access including sibling fields.
The bounded projection does not copy, pass, store, return or escape these views.
Ending the loan leaves prior writes visible and ownership with the root.

Its cleanup projection recognizes a NoThrow Cleanup unique-instance method,
not the Close name alone. Compact owners contain primitive, nested Copy and
owning fields. Partial construction cleans acquired fields in reverse
acquisition order; complete cleanup recurses through owning fields.
Cleanup cannot invoke a potentially throwing operation. A statically constant
index proved within an immutable host-buffer grant is usable in NoThrow
cleanup; general indexing is not.
Managed roots preserve live values but do not implement unique release or
general view anchors.

These implementation restrictions do not prohibit a separately checked
published source/capture relationship in the language specification.
Partial owning-field moves, complete view representations and wider
dependency/ABI support remain separate implementation work.
The general [owning-field specification](../specs/language/ownership-and-cleanup.md)
now defines those logical move/repair and complete/partial cleanup boundaries;
this bounded implementation inventory does not restrict them.
