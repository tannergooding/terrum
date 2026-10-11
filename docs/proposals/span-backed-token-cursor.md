# Span-backed token cursor proposal

**Status: bounded contract accepted.** The compilation-local `TokenCursor` and
indexed scalar-parser path are recorded in the
[hosted view inventory](../implementation/view-and-owner-profiles.md#tokencursor-view).
General Span source/dependency and native-width scalar contracts are
independently specified; this bounded acceptance does not implement them.
General view representations and persisted capture metadata remain separate.
Current
implementation coverage is tracked separately in the
[self-host reference](../../self-host/README.md).

## Current boundary

`Span<T>` remains one qualified view family with distinct read, write,
descriptor-rebinding and referent capabilities. Forwarding a descriptor does
not keep its backing dependencies valid. Outside the accepted bounded case,
the general anchor representation and calling convention remain open.
[Whole-source relationships](../specs/language/dependencies-and-views.md)
are now specified, without selecting an anchor or extending backing lifetime.

## Accepted bounded exception

Acceptance is limited to a compiler-owned readonly `Span<Token>` projected
from a validated `TokenSnapshot`, stored in the unique `TokenCursor`, and used
by the bounded scalar parser on the current Windows x64 profile. It uses a
16-byte descriptor, by-value `Copy` indexing with unsigned native-width bounds
checks, and compiler-checked capture forwarding without ownership or lifetime
extension. The implementation inventory above records the full bounded contract,
including the synchronous borrowed parser path and unchanged 64-token bound.

## Unapproved general support

The bounded exception does not approve general view construction or slicing,
arbitrary storage or escape, writable access, ref-returning element loans,
persisted capture metadata, owner or guard retention, or a new public ABI.
Those implementation and representation boundaries remain open; they do not
restrict the independently accepted general source/dependency contracts.
