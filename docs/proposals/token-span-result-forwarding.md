# Forwarded `Span<Token>` results

**Status: Deferred until a concrete same-compilation producer/consumer path
requires returned `Span<Token>` views. The proposed `DependsOn` result
contract remains unapproved; existing tests are implementation evidence, not
contract approval.**

## Summary

The hosted compiler currently rejects ordinary `Span<Token>` function
results. General borrowed results still need distinct source-place and
forwarded-backing relationships, but a readonly `Span<Token>` result and its
eligible inputs are by-value descriptors; the result can forward backing
without borrowing a parameter's descriptor storage. Consider a narrow
same-compilation experiment that uses the declared parameter dependencies
while leaving the general relationship model open.

## Deferred decision

Keep this profile as a candidate, but do not accept or broaden it until a real
same-compilation producer/consumer path needs a function to return a
`Span<Token>`. The current cursor and parser path do not require returned
views. Existing tests help preserve the behavior if the need arises; they do
not establish that the proposed annotation or contract is needed.

If reconsidered, review the closed dependency-set semantics, conservative
propagation across branches, calls, copies and joins, and exact source
annotation before acceptance. This deferral does not approve general
`Dependency` metadata, public or persisted signatures, or the separately
deferred semantic-module follow-on.

## Candidate profile (unapproved)

The candidate would allow synchronous, non-generic, non-external top-level
functions to return one readonly `Span<Token>` descriptor when one
return-target `DependsOn` annotation declares a nonempty closed dependency
set:

```terrum
[return: DependsOn(nameof(tokens))]
internal Span<Token> Identity(Span<Token> tokens) {
    return tokens;
}
```

`DependsOn` lists a nonempty closed set of formal parameters. Only readonly
`Span<Token>` parameters may be listed; scalar parameters may select a result
but do not provide backing. Because the result and eligible parameters are
by-value descriptors, this experiment treats their relation as forwarding
backing rather than borrowing descriptor storage; `DependsOn` alone records
the possible backing sources. This type-specific conclusion does not define a
general relation encoding for reference or receiver-storage results. The
function body must prove that every normal result forwards backing from one or
more declared parameters, without capturing a local owner. A missing or
incompatible dependency set, or a body that can return unrelated backing, is
rejected.

At each call, conservatively retain dependencies from every declared source,
regardless of which branch the implementation selects. Calls, copies and
joins propagate the same backing set; scalar selector arguments add none.
The source backing remains live until every result derived from it is retired.
This does not extend the source's permitted lifetime or authorize storing the
result in unrelated receiver storage.

Keep this experiment compilation-local. Public/exported declarations,
semantic/native module persistence, module replacement, executable TRRM,
the scalar frontend, general `ref<T>` results, receiver dependencies, writable
views, managed interiors, pins/leases, generic functions, borrowed outputs,
implicit reference materialization, stored captures and asynchronous retention
remain outside its scope. A private semantic-module follow-on is separately
proposed and currently deferred until a concrete provider/consumer corpus and
supported invocation profile justify the full IR and type-remapping work
([proposal](token-span-semantic-module.md)). The existing checked verifier and
native emitter remain responsible for execution; this does not add general
loan metadata.

## Required evidence

Verify every declaration, including unused functions, against the declared
source set. Cover direct, conditional, named-argument and transitive forwarding
through calls under both runtime policies. Reject undeclared inputs, local
owner escapes, missing or duplicate dependencies, non-Span sources and
incorrectly qualified results. At callers, prove owner move/replacement and
scope exit are rejected while a derived result is live and become valid after
that result is retired, while retiring a temporary descriptor alone does not
retire its backing obligation. Stage forwarded backing across later argument
evaluation, return cleanup and checked unwind. Exercise full-width descriptors
through native AOT/JIT without changing their ABI. Confirm module writers and
source-free execution continue to fail closed on span-dependent metadata.

This proposal does not approve a target contract or general dependency
metadata. Its annotation spelling and bounded support remain provisional.
