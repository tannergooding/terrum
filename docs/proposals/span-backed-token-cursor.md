# Span-backed token cursor proposal

**Status: bounded contract accepted.** The compilation-local `TokenCursor` and
indexed scalar-parser path are specified in the
[lifetime contracts](../specs/lifetimes.md#erased-interface-storage-is-explicit).
General `Span<T>` support, source-level `nuint`, writable or ref-returning
access, and persisted capture metadata remain unapproved. Current
implementation coverage is tracked separately in the
[self-host reference](../../self-host/README.md).

## Current boundary

`Span<T>` remains one qualified view family with distinct read, write,
descriptor-rebinding and referent capabilities. Forwarding a descriptor does
not keep its backing dependencies valid. Outside the accepted bounded case,
the general anchor representation, calling convention and source relationships
remain open.

## Accepted bounded exception

Acceptance is limited to a compiler-owned readonly `Span<Token>` projected
from a validated `TokenSnapshot`, stored in the unique `TokenCursor`, and used
by the bounded scalar parser on the current Windows x64 profile. It uses a
16-byte descriptor, by-value `Copy` indexing with unsigned native-width bounds
checks, and compiler-checked capture forwarding without ownership or lifetime
extension. The canonical lifetime section above defines the full contract,
including the synchronous borrowed parser path and unchanged 64-token bound.

## Unapproved general support

The bounded exception does not approve general view construction or slicing,
arbitrary storage or escape, writable access, ref-returning element loans,
source-level `nuint`, persisted capture metadata, owner or guard retention, or
a new public ABI. Those capabilities and the general representation and
source-relationship rules remain open.
