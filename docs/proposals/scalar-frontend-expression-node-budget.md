# Scalar frontend expression-node capacity

**Status: unapproved.** The accepted scalar parser and `TokenCursor` contracts
retain their documented bounds. This implementation experiment does not
change the canonical specification or approve a broader parser.

The provisional implementation now uses separate `v87` standalone and `v88`
complete-program layouts to carry 64 nodes. This implementation support is
not contract approval.

## Problem

The provisional scalar frontend stores at most 32 expression-tree nodes per
function. Removing the per-function token-count gate does not help
expression-heavy functions that exceed this independent projection capacity.
The fixed node limit can therefore reject a function even when its source,
statement, local, recursion and structural bounds are respected.

## Recommendation

Raise the provisional expression-tree capacity from 32 to 64 nodes per
function, while retaining the 16K source-unit, ten-function, twelve-alias,
eight-local, twelve-statement, 16-expression-recursion and six-structural-level
bounds. Keep storage fixed-size and checked: do not add unbounded allocation,
general `Span<T>` support, new syntax, or escaping parser state.

Represent the larger tree in new internal projection layouts (`v87` standalone
and `v88` complete-program), with source/host validation of every node and
call target. This is a bounded implementation recommendation, not a language
contract or metadata-version change. It is separate from the
[function-token-window recommendation](scalar-frontend-function-token-window.md);
both remain unapproved.

## Scope boundary

The experiment does not change the accepted 64-token `TokenCursor` contract,
generalize the scalar frontend grammar, or imply that verification and
emission have moved from the hosted compiler into Terrum.
