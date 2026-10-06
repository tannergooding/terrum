# Generic Copy initialization outputs for non-Boolean calls

**Status: Unapproved**

## Summary

Compilation-local `[Copy]` generic templates currently support
`init ref<T>` only with Boolean-result conditional publication. Ordinary hosted
`init ref<T>` functions also support fixed non-Boolean and `void` results,
requiring initialization on every normal return while leaving the destination
vacant on exceptional departure. Consider extending that existing contract to
generic private specializations, separately from ordinary generic `out T`.

## Question

Should a compilation-local `[Copy]` generic template with a fixed `int32`,
`uint32`, `int64`, `uint64`, `char16`, or `void` result be allowed to initialize
`init ref<T>` on every normal return, without changing Boolean conditional
publication, inference, type eligibility or the private module ABI?

## Recommendation

Adopt this as a bounded hosted extension. Reuse the ordinary
`init ref<T>` normal-return proof and checked output scratch rather than
introducing a second publication rule. Every normal return, including implicit
`void` fallthrough, must initialize the destination; exceptional departure
leaves it vacant and unreadable. Boolean results remain conditional: false
leaves the destination vacant.

Keep this distinct from ordinary `out T`: calls must still designate vacant
storage with `init`, and an initialized destination remains invalid. Require a
fixed supported non-Boolean result or `void`; a formal generic result could
become `bool` and change the publication rule after specialization. Preserve
the current inference contract: explicit type arguments, typed input arguments
or an already typed vacant destination may establish `T`, while `init var`
alone cannot. Use only the existing private `init ref<T>` output shape and
`[Copy]` argument eligibility, including existing compilation-private
managed-handle specializations and their rooting/cleanup. Do not add unique or
owning values, fixed-valued output parameters, mixed `out`/`init ref<T>` modes,
public generic declarations/witnesses, module-format or ABI changes,
source-free TRRM support, or scalar-frontend verification/emission. Concrete
floating argument eligibility remains under its separate unapproved proposal.

## Required evidence

Cover explicit and inferred substitutions, every fixed non-Boolean result and
`void`, explicit returns and `void` fallthrough, every-normal-return
publication, rejection of an uninitialized normal path, and unchanged Boolean
false/vacancy behavior. Under checked exceptions, verify that a write followed
by failure does not publish, catch-path reads reject, and a later call can
initialize the same destination. Exercise scalar/Copy-struct values and
existing compilation-private managed-handle substitutions under both runtime
policies, including root retirement on failure. Persist closed specializations
through semantic-module and native producer replacement behind unchanged
consumers; do not change output mode metadata or wire formats.

This proposal does not approve the target contract. Any implementation is
provisional evidence pending review.
