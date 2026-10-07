# Output parameters in executable TRRM

**Status: Unapproved**

## Summary

The accepted executable TRRM contract still admits only by-value parameters.
The implementation provisionally executes a bounded `ScalarInitOutput` subset
behind a required Feature assignment without changing that contract.
Structural parameter modes, Boolean-correlated `OutputState` rows, and the
place shapes alone do not specify executable output behavior.

## Question

Should the bounded `ScalarInitOutput` profile below become an accepted
executable TRRM contract?

## Recommendation

Implement this as a **provisional, unapproved** profile while preserving the
existing `(1, 0)` by-value executable subset. This is an engineering
recommendation, not acceptance of a language or interchange contract.
Keep the following boundaries explicit:

- Retain artifact version `(1, 0)`, which is still pre-release. Activate core
  Feature section kind `0x0004` with flags `0` and its assigned eight-byte row
  `u32 featureId, u32 minimumRuleRevision`. Provisionally assign
  `featureId=1` to `ScalarInitOutput`, accept only rule revision `1`, and
  require each affected module's feature range to name that row. A module with
  no output behavior retains an absent Feature section and `(0, 0)` range.
  Reject unknown feature IDs and unsupported revisions before binding or
  lowering; do not add general same-major/future-minor acceptance.
- Require `ScalarInitOutput` for every output declaration, output-bearing
  call, and executable place body, including uncalled methods. Presence of the
  feature never waives the body's structural, type, lifetime, or semantic
  audit. Do not encode this behavior in an optional extension.
- Admit only non-exported module-local functions with `init ref<int32>` output
  formals and `void` or `int32` results. Other scalar formals remain by-value.
  Preserve the existing mode value `0x02`; reject `out`, Boolean-correlated
  outputs, imports, exported output functions, and `OutputState` clauses.
- Output-bearing methods and calls use general Body forms `0x00`, `0x01`, or
  `0x02`, with the narrowest valid local width, exactly one block, no regions,
  and no normal successors. Map non-value formals to places first in parameter
  order, then local places; by-value formals remain entry values. The function
  root block lists all output-formal and local places in `startPlaces`, where
  they become active and vacant. Initially admit only `int32` output/local
  places in region `0` with flags `0x04` (initially vacant, not rebindable or
  unique).
- Admit `PlaceLocal` (`0x0B`), `Load` (`0x0D`), `Initialize` (`0x0E`), and
  mode-aware local `Call` (`0x12`) with the existing supported scalar value
  operations. Reject field places, `Assign`, `Move`, `Drop`, place-handle
  copying/escape, and forwarding an output formal. Compact-linear and
  compact-indexed bodies remain valid for value-only functions, but cannot
  carry place arguments. In a general-form `Call`, each argument ID is
  a value ID interpreted by the selected formal's mode: mode `0x00` requires
  a kind-0 value, while mode `0x02` requires a kind-1 value denoting a place
  handle (not a place descriptor ID). The ordered argument count still
  exactly matches the signature, and zero is never an argument.
- Calls require exact parameter order, type and mode matches; every output
  destination is a distinct vacant local place. The call grants temporary
  exclusive initialization authority, so a destination cannot be supplied
  twice or accessed through another admitted alias. A callee must initialize
  each output formal before every normal `Return`. Ending the formal's place
  retires callee access authority; it does not erase the publication
  obligation or the initialized value. A successful call publishes every
  output independently of its `void`/`int32` result.
- Permit only `Fail` reason 1, which preserves the hosted `require false`
  failure path. Reject other failure reasons until this profile can preserve
  their distinct native statuses. No recovery or output state is promised
  after failure. Use private staging only to preserve the existing normal-
  return commit order; it does not make body effects transactional or promise
  rollback. Exclude cleanup, authored exception handling, checked-exception
  policy, postconditions, and exceptional output publication.

The assigned Body shapes already describe these operations, but the accepted
executable profile does not execute them. The provisional implementation adds
a separately audited general-place Body path and changes to the writer, loader
and semantic-verification path; it does not promote those changes into an
accepted contract. Any promotion must retain the legacy compact-body rule that
rejects non-value modes and place operands.

## Provisional implementation status

The current provisional writer and loader preserve by-value `bool`, `char16`,
`int32`, `uint32`, `int64`, `uint64`, `float32` and `float64` formals in
output-bearing general Bodies. Their values may be forwarded from local
scalar-returning calls into a local call; this does not widen output places,
which remain `int32`, output-bearing function results, which remain `void` or
`int32`, or the int32-only constants, initialized locals, assignment and
arithmetic operations. The writer and loader also provisionally support
Boolean and `char16` equality/inequality, signed integer equality/inequality,
integer ordering, and all six floating comparisons, with `bool` results.
Unsigned equality and non-`int32` local constants remain excluded. The
one-block control-flow boundary is unchanged. These value and comparison paths
are implemented support only, not approval of the proposed executable
contract.

The broader proposal remains unresolved. In particular, Boolean status facts
are call-specific and require correlation with their particular destinations;
`out`, fields, aliases, imports, forwarding, cleanup, checked exceptions,
exceptional publication, and general reference/loan contracts remain outside
this provisional slice. This proposal does not approve a general
`out`-to-`init ref<T>` migration, scalar-frontend verification/emission, or
any public ABI change.

## Additional open question: entry-use ordinals for places

**Summary:** The Body contract counts place activation and inherited state as
uses, and requires each listed `placeLastUses` ordinal to identify an actual
use. It assigns no ordinal to the entry-state use itself, leaving a started
place with no later operand or end transition without a representable final
use.

**Recommendation:** Within this still-unapproved profile, count each place in
`startPlaces` or `inheritedPlaces` as an actual place use at block entry with
ordinal 0. Its `placeLastUses` entry records the maximum ordinal among that
entry use and all instruction-operand, terminator-operand, and end-place
transition uses in the block, with coincident ordinals permitted, including in
blocks with zero operations. Inherited state continues an existing activation;
it does not restart the place or reset its initialization state. This
completes the profile's inventory rule without changing the accepted
instruction-set text or implying general TRRM conformance.
