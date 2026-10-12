# Scalar frontend function token windows

**Status: unapproved.** The accepted scalar-parser and `TokenCursor`
contracts retain their documented 64-token bound. Current implementation
coverage beyond that bound is provisional and does not accept this extension
or change the canonical specification.

## Problem

The unresolved contract boundary is whether a bounded scalar frontend may
consume a function token span longer than 64 tokens while the separate general
cursor/window contract retains its 64-token bound. The current source-bounded
profile-2 implementation already exercises longer function spans provisionally;
that support does not accept the recommendation or change the canonical
specification.

## Recommendation

Recommend that the provisional scalar frontend impose no fixed per-function
token-count limit while retaining its 16K source-unit bound, ten-function and
twelve-alias capacities, eight locals, twelve statements, 32 expression
nodes, 16 expression-recursion levels and six structural levels. Continue to
pass the validated token view synchronously and store only scalar token
positions in parser state; do not add general `Span<T>` support, escaping
views, a new persisted representation or a metadata version.

This implementation experiment does not revise the accepted 64-token
`TokenCursor` or parser-window contract. Approval is still required before
that contract or its canonical specification is changed.

Node capacity is independent of token-window support. A separate
[expression-node capacity proposal](scalar-frontend-expression-node-budget.md)
recommends raising the provisional implementation limit to 64; that
recommendation and this proposal remain separate, unapproved decisions.

## Provisional implementation evidence

Profile 2 currently accepts functions longer than 64 tokens, including a
32-function program at the maximum 16,384-code-unit source length. The
implementation retains bounded source, local, statement, node, recursion and
structural budgets and passes token positions synchronously; the hosted reader
continues to audit the complete projection. Its provisional 32-function and
64-node capacities are separate from the ten-function and 32-node values in
this recommendation. No cursor/window contract or canonical specification
has changed, and this proposal remains **UNAPPROVED**.
