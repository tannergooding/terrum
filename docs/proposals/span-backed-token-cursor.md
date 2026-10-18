# Span-backed token cursor

**Status: bounded contract accepted.** General source/dependency contracts
are independently accepted; general view representation, public ABI and
persisted capture support are not established by this exception.

## Accepted bounded exception

The exception admits a compiler-known readonly `Span<Token>` projected from a
validated `TokenSnapshot`, stored by the unique `TokenCursor`, and passed
synchronously through the bounded scalar parser on Windows x64:

- A 16-byte descriptor denotes token backing and extent. Indexing copies a
  `Token` value; it does not return an element reference, transfer ownership
  or retain the owner.
- Length/index arithmetic uses an unsigned native-width carrier, with a
  full-width bounds check before scale/address calculation. A narrowed or
  signed-reinterpreted index must not bypass the check.
- Cursor capture forwarding retains the snapshot's backing/access
  dependencies. Descriptor copies, cursor moves, joins, private calls and
  cleanup do not discard them or extend the owner's permitted lifetime.
- Sequential advancement changes the remaining view. Indexed reads do not
  consume it. Empty and end views retain their backing/guard obligations.
- The synchronous parser passes a borrowed view and keeps scalar token
  positions in parser state; this is not permission for arbitrary stored
  parser views or source-free persistence.

The [hosted view inventory](../implementation/view-and-owner-profiles.md#tokencursor-view)
owns the exact accepted envelope and current coverage, including the bounded
64-token parser/window path. The [self-host inventory](../implementation/self-host.md)
separately records full cursor traversal and component execution. Those
different operations must not be collapsed into a universal 64-token Span
length limit.

## Relationship to accepted source contracts

[Dependencies and views](../specs/language/dependencies-and-views.md) and
[lifetimes](../specs/lifetimes.md#three-independent-dependencies) already
distinguish descriptor storage, forwarded backing and owner/guard cleanup.
Forwarding this by-value descriptor depends on backing, not a borrowed caller
descriptor slot. Capturing it does not implicitly pin, root, retain or
suppress disposal. Whole-source declarations must be verified against those
relationships; the bounded exception does not replace them with a single
`scoped` bit.

No grammar change follows from this proposal. General span/source contracts
are not “undecided” merely because their implementation and target
representation remain incomplete.

## Separate unapproved extensions

Keep these decisions with their owners:

| Extension | Decision owner |
|---|---|
| Source-bounded indexed function windows and larger scalar resources | [Bootstrap/frontend profiles](bootstrap-frontend-profiles.md#scalar-projection-and-resources) |
| Private descriptor operations in persisted modules | [Private token spans](token-span-semantic-module.md) |
| Same-compilation returned backing | [Returned token spans](token-span-result-forwarding.md) |
| Complete persisted producer/reader invocation | [Frontend invocation](scalar-frontend-invocation-profile.md) |

None automatically widens cursor capture storage. General construction and
slicing, mutable/writable spans, ref-returning elements, arbitrary stored or
escaping views, public/imported/exported span signatures, persisted capture
metadata, owner/guard retention and a universal public descriptor ABI remain
outside the exception.

## Verification boundary

Preserve mandatory layout, snapshot provenance, initialized/live-owner,
capture and staged-argument checks for source and reconstructed inputs.
Out-of-range full-width indexes must fail their bounds checks. Verification
must reject incompatible index carriers, read-after-retirement, owner
move/drop/replacement while a derived view is live, lost dependencies at joins
and invalidation by a later argument. Retiring only a descriptor does not
retire its backing; retiring every dependent descriptor permits only otherwise
valid owner operations.

The [cursor tests](../../tests/Terrum.CodeAnalysis.Tests/TokenCursorTests.cs)
and [Terrum component runner](../../tests/self-host/TokenCursorComponentTestRunner.terrum)
are evidence entry points, not adoption gates for the unapproved extensions.
Maintain both-policy execution, exhaustion and indexed non-consumption checks
separately from persistence or general view claims.
