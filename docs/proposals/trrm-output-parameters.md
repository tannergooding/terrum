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
  `featureId=1` to `ScalarInitOutput`, initially require rule revision `1`,
  and require each affected module's feature range to name that row. The current
  provisional reader accepts revisions 1–4 as described below. A module with
  no output behavior retains an absent Feature section and `(0, 0)` range.
  Reject unknown feature IDs and unsupported revisions before binding or
  lowering; do not add general same-major/future-minor acceptance.
- Require `ScalarInitOutput` for every output declaration, output-bearing
  call, and executable place body, including uncalled methods. Presence of the
  feature never waives the body's structural, type, lifetime, or semantic
  audit. Do not encode this behavior in an optional extension.
- Under initial rule revision 1, admit only non-exported module-local
  functions with `init ref<int32>` output formals and `void` or `int32`
  results. Other scalar formals remain by-value.
  Preserve the existing mode value `0x02`; reject `out`, Boolean results,
  imports, exported output functions, and serialized `OutputState` clauses.
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
  obligation or the initialized value. Revisions 1–3 publish outputs
  independently of their `void` or scalar result; revision 4 conditionally
  publishes outputs only when its Boolean result is `true`.
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
`int32`. Constants and by-value scalar operations are supported independently
of the `int32`-only output places, initialized locals and assignments. The
writer emits typed `ConstBits` for supported scalar values, and the loader also
reconstructs audited `ConstSigned` integer constants. The writer and loader
also provisionally support
Boolean, `char16` and integer equality/inequality, integer ordering, and all
six floating comparisons, with `bool` results. Integer bitwise AND, OR and XOR
are supported for all four integer types. Unary support includes Boolean NOT,
integer NOT and unary plus, checked `int32`/`int64` negation, and floating
negation. Signed-minimum negation retains the existing checked overflow
status; unsigned negation remains outside the slice.
Floating addition, subtraction, multiplication and division are supported for
both floating types. Integer addition, subtraction, multiplication, division
and remainder are supported for all four integer types, including signed
truncation-toward-zero, dividend-signed remainder, and the minimum-value
remainder-by-negative-one case. CLI execution tests also preserve checked
overflow status for each arithmetic operator and the distinct division and
remainder zero-divisor statuses. Checked-count `<<` and `>>` are supported
for all four integer types; unsigned `>>>` is supported where it is
equivalent to `>>`. The source `int32` count is converted to the Body's
`uint32` count slot and back through checked casts, preserving overflow status
for negative and out-of-width counts. Signed logical `>>>` remains outside
this path because the output-body lowering has no bit-reinterpretation step
for signed operands. Integer and `char16` conversions are also provisionally
carried by opcode `0x16` and reconstructed as checked bound
casts, preserving overflow failure and `NoThrow` verification. Floating
conversion paths are also provisionally implemented: format conversion uses
`0x1A`, integer-to-float uses `0x1B`, `char16`-to-float uses `0x1E`, and
float-to-integer uses `0x1F`. Float-to-integer truncates toward zero before
range checking and preserves the existing overflow status; `NoThrow`
verification remains in force. The one-block control-flow boundary is
unchanged. These value, comparison, bitwise, unary, arithmetic and conversion
paths are implemented support only, not approval of the proposed executable
contract.
The current writer emits required feature rule revision 4 for output Bodies.
Rule revision 2 provisionally permits `bool`, `char16`, `int32`, `uint32`,
`int64`, `uint64`, `float32` and `float64` referents and scalar initialized
locals; rule revision 1 remains loadable with `int32` referents only. Rule
revision 3 additionally permits non-Boolean scalar output-bearing results;
rule revision 4 adds Boolean output-bearing results with conditional
publication.
Exact type identity is retained through place descriptors, handles, loads,
initialization values, call arguments and results. Revisions 1 and 2 retain
their `void`/`int32` output-bearing result restriction; unsupported revisions
reject.

## Additional open question: scalar output referent set

**Summary:** The initial `ScalarInitOutput` profile requires `int32` output
referents and initialized local places. The existing Body value and place
operations can represent the other supported non-void scalar types without
changing initialization, aliasing, or publication behavior, but older
rule-revision-1 readers understand only `int32` referents.

**Recommendation:** Add an explicitly provisional rule revision 2 that admits
`bool`, `char16`, `int32`, `uint32`, `int64`, `uint64`, `float32` and
`float64` output referents and scalar initialized locals. Preserve exact
type identity across declarations, place descriptors, handles, loads,
initialization values and call arguments; do not add conversions, forwarding,
recovery or Boolean-correlated publication. Keep `void`/`int32` output-bearing
results and all other profile limits unchanged. Rule revision 1 remains the
`int32`-only profile; newer readers may accept both revisions and must reject
revision-2 behavior when an artifact requires only revision 1. This is an
implementation recommendation only and does not approve either revision as
the executable TRRM contract.

The broader proposal remains unresolved. In particular, Boolean status facts
are call-specific and require correlation with their particular destinations;
`out`, fields, aliases, imports, forwarding, cleanup, checked exceptions,
exceptional publication, and general reference/loan contracts remain outside
this provisional slice. This proposal does not approve a general
`out`-to-`init ref<T>` migration, scalar-frontend verification/emission, or
any public ABI change.

## Additional open question: output-bearing scalar results

**Summary:** Rule revisions 1 and 2 restrict output-bearing results to `void`
or `int32`. The output Body and native call paths can preserve other
non-Boolean scalar results while requiring every output place to be initialized
on every normal return. Boolean results are different: hosted Boolean
`init ref<T>` calls publish only on `true`, so admitting them here without a
separate unconditional-publication contract would conflate distinct semantics.

**Recommendation:** Keep this extension provisional in rule revision 3.
Permit output-bearing functions to return `char16`, `uint32`, `int64`, `uint64`,
`float32` or `float64` in addition to the existing `void`/`int32`; preserve
exact result typing and mandatory normal-return initialization. Keep Boolean
results, public output targets, non-output place-bodied result widening, and
all other excluded output behaviors unchanged. This recommendation does not
approve the executable contract.

## Additional open question: Boolean output results

**Summary:** Hosted Boolean `init ref<T>` calls publish each output only when
that call returns `true`; false leaves the caller's destination vacant.
Revisions 1–3 exclude Boolean output results. The hosted verifier correlates a
direct Boolean call with its own output destinations, but storing that result
in a Boolean local does not preserve this per-call fact.

**Recommendation:** Keep Boolean output results provisional in rule revision
4, matching the hosted true-publishes/false-vacant behavior without serialized
`OutputState` rows or a change to hosted source semantics. Require every
output formal to be initialized before every normal return in the callee;
false still withholds publication at its caller. Treat destinations after a
Boolean call as not definitely initialized and rely on the hosted verifier to
reject unproven reads, reinitialization and repeated output passing. Do not
claim that storing the Boolean result proves destination state, and keep
forwarding, aliasing, recovery, cleanup, exports and general control-flow
outside this slice. Continue loading revisions 1–3 with their existing
restrictions. This is not approval of the executable contract.

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
