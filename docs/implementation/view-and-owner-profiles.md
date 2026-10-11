# Hosted view and owner profiles

This is an informative support inventory relocated from the lifetime
specification. It describes bounded hosted projections, not limits on the
[general dependency contract](../specs/language/dependencies-and-views.md).
The [implementation summary](../implementation.md) owns current support.
No general view ABI, stored-reference codec or concurrent protocol follows.

## TokenCursor view

On the current Windows x64 hosted profile, a compiler-owned readonly
Span<Token> is projected from a validated TokenSnapshot, stored in the unique
TokenCursor and used by the bounded scalar parser. Its descriptor is 16 bytes
(data address and element count). Indexing returns Copy elements by value with
unsigned native-width bounds checks, using uint64 in that implementation.
This does not remove nint/nuint from the independent language scalar catalog.

Captures(nameof(tokens)) forwards the complete backing and guard dependency
set to the cursor field without owning or extending them. Construction cannot
publish unless those dependencies outlive every use.
The cursor's mutable view field and cursor-only AdvanceOne rebind the
descriptor without dropping dependencies. The parser receives it
synchronously rather than storing it in managed parser state.
The existing 64-token limit and parser-window behavior are unchanged.

This bounded profile does not implement general view construction, slicing,
writable or ref-returning access, arbitrary storage or escape, persisted
capture metadata, owner/guard retention or a new public ABI.

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
