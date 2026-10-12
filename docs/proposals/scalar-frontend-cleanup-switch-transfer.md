# Scalar frontend cleanup-contained switch transfers

**Status: unapproved implementation recommendation.** This records an
implementation proof boundary only. It does not change the accepted
statement-switch transfer semantics or any canonical specification.

## Problem

The bounded complete-program scalar frontend lowers a cross-arm transfer to a
private arm-ordinal dispatcher loop. The grammar permits transfers between
arms of the same statement switch but forbids them from escaping a cleanup
action. The hosted cleanup verifier originally rejected any such dispatcher
because it could not establish termination. There is no source-semantics
decision pending here; the implementation gap is proving that cleanup-contained
control flow terminates.

## Recommendation

Add a bounded arm-graph termination proof to the hosted cleanup-verification
path. Derive transitions only from the already source-audited transfer records,
account for ordinary arm completion and nested cleanup, and admit a cleanup
dispatcher only when termination within that cleanup action is proven. Keep
the existing cleanup loop checks for all other loops; do not treat a cross-arm
transfer as permission to escape cleanup or waive termination verification.

Keep this proof internal to the provisional scalar frontend. Do not add
ordinary hosted parser support, source-free TRRM records, or a new language
construct. Unproven paths and graphs that may not terminate must continue to
reject.

## Provisional implementation evidence

The verifier now independently checks the private dispatcher structure and
derives arm transitions only from adjacent constant-ordinal state assignments
and dispatcher continues. Acyclic graphs must reach a finite exit from every
possible entry arm. The existing scope, initialization, `NoThrow`, cleanup and
exception checks remain mandatory; the marker is eligibility metadata, not a
termination certificate. Nested dispatchers and cleanup compose. Within an
audited dispatcher, a pre-tested counted `for` can now prove finite unit-step
progress over a mutable local `int32`: constant initializer/bound,
strict `<` with `counter = counter + 1`, or strict `>` with
`counter = counter - 1`, and no body write or alias of the counter. Both scalar
lowering shapes (appended iterator and separate iterator for local `continue`)
are recognized structurally; a continue bypassing an appended iterator rejects.
The initializer may declare the local or assign a constant to an existing local.
The strict bound keeps every step representable in `int32`, including extrema;
the proof does not subtract endpoints in `int32` or enumerate iterations.
A false initial condition exits without executing the body. Only the proved
iterator arithmetic gains a non-overflowing `NoThrow` proof; body effects and
exits still undergo the existing checks. Cycles, other unproved intra-arm
iteration and cleanup escapes still reject. Analysis uses the existing
semantic-verification budget.

Implementation may proceed using this recommendation as provisional evidence;
doing so does not accept this proposal or alter the grammar contract.

## Remaining boundary

Supporting cyclic arm graphs or data-dependent intra-arm iteration requires a
sound progress/ranking proof. Runtime termination of an individual example is
not enough to accept it, and this implementation does not approximate that
proof by weakening cleanup verification.

The counted-loop provisional recognition admits only a pre-tested `for` loop
over a mutable local `int32` counter with a constant initializer and bound, a strict
`counter < bound` / `counter > bound` condition, and an exact `counter + 1` /
`counter - 1` iterator in the matching direction. The body must not write or
alias the counter and must still pass the existing cleanup-flow checks. The
unit step reaches the constant bound without overflowing; a false initial
condition is a zero-iteration exit. Keep non-unit or data-dependent steps,
inclusive bounds, unknown initial values, counter mutation, and data-dependent
`while` loops rejected. This is a verifier-recognition boundary, not a source
language restriction or acceptance of this proposal.
