# Forwarded `Span<Token>` results

**Status: bounded implementation profile deferred until a concrete
same-compilation producer/consumer needs returned views.** The general
`DependsOn`/`Captures`/`ForwardsBacking` source contracts are already accepted;
their syntax and relationship model are not reopened here.

## Decision and integration

The current cursor and parser do not need returned spans. Keep the experiment
compilation-local until an actual consumer justifies it. The
[view inventory](../implementation/view-and-owner-profiles.md) owns current
implementation coverage; passing prototype tests does not adopt this profile.

This would extend bounded binding, flow verification and lowering to readonly
`Span<Token>` results of synchronous nongeneric internal, non-external
top-level functions. It changes neither grammar nor the
[accepted source relationships](../specs/language/dependencies-and-views.md).
Private persistence is a [separate proposal](token-span-semantic-module.md)
whose current corpus does **not** require returned views.

## Accepted relationship, bounded realization

A result and eligible `Span<Token>` inputs are by-value descriptors. Forwarding
the result transfers backing/access dependencies, not a borrow of a parameter's
descriptor slot. Scalar selector arguments provide no backing.

Normalize declarations under the accepted dependency rules:

- An untargeted callable `DependsOn` selects the ordinary result; an explicit
  return target is also possible. Do not require a new annotation spelling.
- The sole eligible receiver/input can supply the accepted broad default.
  The bounded top-level profile has no receiver, but must not recast the
  general default as an absent/malformed contract.
- An explicit source set constrains callers by **every** listed possible
  source, regardless of which branch today's body selects. Explicit
  `DependsOn()` is a valid declaration of no input dependency, not malformed
  syntax.
- Bodies must prove each normal result satisfies the selected relationship.
  Empty dependency syntax does not invent source-free span construction:
  a body returning input backing with no permitted input still fails proof.
  Unsupported backing origins may remain outside this implementation profile.

The prototype's explicit-return-target, nonempty-parameter-set restriction is
a narrower implementation boundary, not the language definition or a reason
to amend accepted defaults. Recommend normalizing to the accepted contracts
before broadening or promoting the implementation; do not standardize that
prototype restriction.

## Flow and lifetime obligations

At calls, map formals through positional/named argument mappings while
preserving source evaluation order. Retain every declared source's backing,
access, owner/guard, roots, code and affinity obligations as applicable.
Propagate those relationships through transitive calls, copies, conditions
and joins; a later callee body optimization cannot weaken an externally
declared relationship.

Derive origins and liveness in the mandatory verifier, including unused
functions. Reject undeclared input backing, local-owner escape and
incompatible access. Stage dependencies before later arguments can overwrite
or retire a source. Maintain them through return cleanup and checked unwind.

The descriptor may outlive an input descriptor variable when backing remains
valid. Conversely, copying or retaining a descriptor does not keep its owner
alive or defer logical cleanup. Moving, replacing or dropping backing while
any result depends on it must reject; once all dependencies are retired, only
ordinary otherwise-valid owner operations become available. An empty/end view
does not erase those obligations.

Use the [accepted bounded same-compilation representation](span-backed-token-cursor.md#accepted-bounded-exception)
where applicable; it does not select a public foreign span ABI. Returned
values require actual complete descriptor handling in both AOT and JIT, not
a scalar truncation or implicit reference materialization.

## Alternatives and exclusions

Keeping direct private view parameters avoids returned-view machinery and is
recommended until the consumer exists. General borrowed results would also
need receiver-storage, reference, output, mutable access and public
compatibility semantics beyond this implementation slice; do not approximate
them with a token-specific metadata shortcut.

Keep public/exported/imported or persisted span signatures, TRRM result
metadata, cursor capture persistence, fields, general `ref<T>` results,
receiver borrowing, writable views, pins/leases, generic functions, borrowed
outputs and asynchronous retention outside this profile. These are
implementation/representation exclusions, not withdrawals of their accepted
source-level dependency rules.

## Acceptance gates

Require the consumer, then:

1. Test explicit/untargeted and sole-source-default normalization separately
   from unsupported body origins. Test empty declarations against valid
   source-free origins only if such construction is independently admitted;
   otherwise reject the body rather than misdiagnosing declaration syntax.
2. Verify direct, conditional, named and transitive forwarding, multiple-source
   conservative joins and every unused declaration. Reject non-view sources,
   duplicate/unknown formals, unrelated/local-owner backing and invalid access.
3. Check live-result owner move/drop/replacement and scope exit, retirement,
   temporary-descriptor retirement, empty views, later-argument invalidation,
   return cleanup and checked recovery under both policies.
4. Exercise full-width descriptors through native AOT/JIT without treating
   execution as proof of general ABI or metadata support.
5. Confirm module writers/readers and source-free paths reject unsupported
   returned-span contracts; adopting private parameter persistence must not
   admit result metadata accidentally.

The [cursor/forwarding regressions](../../tests/Terrum.CodeAnalysis.Tests/TokenCursorTests.cs)
and [token-span verifier](../../sources/Terrum.CodeAnalysis/Semantics/TokenSpanContract.cs)
are evidence sources. This proposal makes no new semantic, metadata or target
acceptance claim.
