# Forwarded `Span<Token>` results

**Status: Unapproved**

## Summary

Accepted result dependencies describe which parameter inputs may constrain a
borrowed result, but do not by themselves distinguish borrowing the
descriptor's own storage from forwarding backing already carried by a view.
The hosted compiler currently rejects ordinary `Span<Token>` function
results. Consider a narrow same-compilation experiment for forwarding those
backing dependencies through top-level functions.

## Recommendation

Provisionally allow synchronous, non-generic, non-external top-level functions
to return one readonly `Span<Token>` descriptor when both annotations are
present:

```terrum
[return: ForwardedBacking]
[return: DependsOn(nameof(tokens))]
internal Span<Token> Identity(Span<Token> tokens) {
    return tokens;
}
```

`DependsOn` lists a nonempty closed set of formal parameters. Only readonly
`Span<Token>` parameters may be listed; scalar parameters may select a result
but do not provide backing. `ForwardedBacking` is a proposed relation marker
for this bounded experiment, not an accepted attribute or general relation
encoding. The function body must prove that every normal result forwards
backing from one or more declared parameters, without borrowing their
descriptor storage or capturing a local owner. A missing marker, missing or
incompatible dependency set, or body that can return unrelated backing is
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
remain outside its scope. The existing checked verifier and native emitter
remain responsible for execution; this does not add general loan metadata.

## Required evidence

Verify every declaration, including unused functions, against the declared
source set. Cover direct, conditional, named-argument and transitive forwarding
through calls under both runtime policies. Reject undeclared inputs, local
owner escapes, missing or duplicate annotations, non-Span sources and
incorrectly qualified results. At callers, prove owner move/replacement and
scope exit are rejected while a derived result is live and become valid after
that result is retired, while retiring a temporary descriptor alone does not
retire its backing obligation. Stage forwarded backing across later argument
evaluation, return cleanup and checked unwind. Exercise full-width descriptors
through native AOT/JIT without changing their ABI. Confirm module writers and
source-free execution continue to fail closed on span-dependent metadata.

This proposal does not approve a target contract or general dependency
metadata. Its annotation spelling and bounded support remain provisional.
