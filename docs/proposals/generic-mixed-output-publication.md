# Mixed generic Copy outputs for non-Boolean calls

**Status: Unapproved**

## Summary

Compilation-local `[Copy]` templates have accepted bounded support for
ordinary `out T` and, separately, generic `init ref<T>` outputs with fixed
non-Boolean or `void` results. Combining the modes in one template remains
unapproved. Mixed-mode support is implemented provisionally, but its current
integration fixture also exercises fixed-valued output side parameters; this
proposal tracks the narrower all-output-formal contract and its independent
replacement evidence. This does not promote the behavior or its broader
extensions.

## Question

Should a compilation-local `[Copy]` generic template with a fixed `int32`,
`uint32`, `int64`, `uint64`, `char16`, or `void` result be allowed to combine
generic `out T` and `init ref<U>` outputs without changing either mode's
destination requirements, publication timing, inference or private module ABI?

## Recommendation

Adopt a bounded hosted extension for templates whose output parameters all
refer to generic Copy type parameters and use only the existing `out` or
`init ref<T>` forms and one of the fixed results above; reject formal-valued
results. Preserve the exact mode on each formal and call argument: `out` may
introduce a fresh output local or name a matching mutable local; `init` may
introduce a fresh vacant local or name a matching vacant local. Require every
output to be initialized on every normal return, including implicit `void`
fallthrough. On checked exceptional departure, neither result publishes: an
`init` destination remains vacant, and an existing `out` destination has no
rollback guarantee and cannot be read in the catch path. Reuse existing output
scratch, proof and private specialization machinery rather than adding a
combined publication rule.

Apply the non-Boolean eligibility checks independently to each output:
ordinary `out` retains its scalar/non-unique Copy shape and excludes managed
handles, while `init ref<T>` retains compilation-private managed-handle
support. If a formal is used by both modes, its concrete type must satisfy both
eligibility checks. This mixed-mode slice excludes fixed-valued output side
parameters; a separate unapproved proposal considers scalar fixed outputs
alongside at least one generic output. Also exclude unique/owning values,
public generic declarations or witnesses, ABI/module-format changes,
source-free TRRM and scalar-frontend verification/emission. Do not change the
existing Boolean-result mixed-mode behavior, including its conditional
publication and broader managed-`out`/fixed-output eligibility. Floating
concrete argument eligibility remains governed by its separate unapproved
proposal.

## Required evidence

Cover explicit and inferred substitutions, scalar and non-unique Copy-struct
outputs, every fixed result and `void`, normal-return proof that independently
requires each mode, and preservation of each call-site mode. Under checked
exceptions, verify that assignments to both outputs followed by failure
publish neither, catch-path reads of either output reject, and a later call can
reuse the `init` destination.
Include mixed managed-init success and failure/root retirement, reject
managed-handle ordinary `out`, and regress unchanged Boolean mixed-mode
publication, including its existing managed-`out` and fixed-output cases.
Exercise semantic-module and native producer replacement behind unchanged
consumers under both runtime policies; retain the `IsInit`/`IsOut` metadata and
existing wire formats.

**Progress:** The hosted binder/verifier and direct native tests already cover
mode preservation, inferred/explicit substitutions, one shared type parameter,
normal-return publication, checked exceptional departure, vacancy/retry,
managed-init root retirement and unchanged Boolean behavior. A dedicated
semantic/native replacement fixture now isolates only generic-typed `out` and
`init ref<T>` outputs, including an ordinary `void` result; it preserves the
consumer bytes while running semantic JIT/AOT and native graph AOT/JIT under
both runtime policies. Its checked-exception case also verifies managed-init
failure, root retirement and destination reuse. The separately maintained
fixed-output-side-parameter and floating-result experiments remain broader
unapproved proposals; they are not prerequisites or approvals of this
all-generic-output slice.

This proposal does not approve the target contract. The implementation and
tests remain provisional evidence pending review.
