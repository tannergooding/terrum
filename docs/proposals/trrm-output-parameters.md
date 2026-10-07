# Output parameters in executable TRRM

**Status: Unapproved**

## Summary

TRRM metadata assigns structural parameter modes for `out` and `init ref<T>`
and defines Boolean-correlated `OutputState` rows, while executable
source-free TRRM currently accepts only by-value parameters. The body model
contains place descriptors and place operations, but those shapes alone do not
specify executable output behavior: the writer and loader reject non-value
parameters, and `OutputState` is not an executable contract.

## Question

Should a future executable TRRM profile admit `out` and `init ref<T>` output
parameters, with publication and vacancy behavior validated from the body and
declared output-state contract?

## Recommendation

Keep current TRRM execution fail-fast and reject non-value parameter modes
until one profile defines the complete place, call and output-state contract.
Do not infer publication from a Boolean return, a normal return, or the
structural `OutputState` row alone. Preserve the existing distinction:
`out` designates output storage, while `init ref<T>` designates vacant storage
that may only be initialized. Do not widen compact-linear bodies or reinterpret
by-value operands as places.

Any future profile should specify and validate:

- How local and field places are created, typed, authorized, and kept within
  their owner and region; how place arguments map to formal ordinals; and which
  aliases or overlapping loans reject.
- How each mode is represented in calls and imports, including exact type
  matching, storage vacancy or initialization requirements, and any producer
  qualification or compatibility obligations.
- Which stores publish output, when publication becomes observable, and how
  control-flow joins correlate Boolean results with each output's state.
- The required state on every normal return and the state on exceptional
  departure, including cleanup and whether a prior write may be observed after
  failure.
- How the writer, loader, verifier and native executor prove these rules
  together, and how malformed or contradictory metadata is rejected.

This proposal does not assign opcodes, body forms, parameter metadata changes,
or native ABI rules. It does not approve broader reference-call metadata,
checked-exception TRRM, scalar-frontend verification/emission, or a general
`out`-to-`init ref<T>` migration. Implementation remains blocked on review of
the target contract.
