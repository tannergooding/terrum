# Generic Copy outputs for non-Boolean calls

**Status: Unapproved**

## Summary

Compilation-local `[Copy]` generic templates currently support conditional
`out T` publication only when the function returns `bool`. Ordinary hosted
functions separately support Copy-struct `out` values with non-Boolean and
`void` results, requiring initialization on every normal return. Consider
extending the existing generic specialization path to use that same
normal-return publication rule.

## Question

Should a compilation-local `[Copy]` generic template with a fixed supported
non-Boolean scalar result or `void` be allowed to publish `out T` on every
normal return, when the concrete `T` also satisfies the existing ordinary
non-Boolean output shape?

## Recommendation

Adopt this as a bounded hosted extension. The hosted verifier already has an
unconditional normal-return obligation for non-Boolean outputs, including
Copy-struct values. Generic proof should reuse that rule rather than introduce
a new publication semantic.

Allow only fixed supported non-Boolean scalar results or `void`; a formal
generic result could specialize to `bool` and silently change publication
from unconditional to conditional. Keep the change to ordinary `out T` on
compilation-local Copy templates and closed private specializations. At a
concrete call, require `T` to satisfy the existing ordinary non-Boolean output
shape, excluding managed handles, unique values and other unsupported output
types. Require every normal return to initialize the output, and retain
existing exceptional-departure behavior. Preserve Boolean `out` conditional
publication and Boolean `init ref<T>` vacancy/publication behavior unchanged.
Do not widen generic type eligibility, static-protocol constraints, public
generic exports, module formats, ABI, source-free TRRM or the scalar frontend.
Concrete floating argument eligibility remains governed by its separate
unapproved proposal.

## Required evidence

Cover explicit and inferred specializations over supported Copy types;
non-Boolean and `void` results; rejection of a path that returns normally
without initializing the output; void fallthrough proof; checked exceptional
departure without publication; and direct execution under both runtime
policies. Reject formal-valued results, `init ref<T>` with non-Boolean
results, and Copy substitutions outside the existing output shape. Exercise
closed private specializations through semantic-module and native producer
replacement without changing the consumer or wire format. Retain the existing
Boolean `out` and `init ref<T>` regressions.

This proposal does not approve the target contract. Any implementation is
provisional evidence pending review.
