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
  provisional reader accepts revisions 1–6 as described below. A module with
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
- Under revisions 1–4, output-bearing methods and calls use general Body forms
  `0x00`, `0x01`, or `0x02`, with the narrowest valid local width, exactly one
  block, no regions, and no normal successors. Map non-value formals to places
  first in parameter order, then local places; by-value formals remain entry
  values. The function root block lists all output-formal and local places in
  `startPlaces`, where they become active and vacant. Initially admit only
  `int32` output/local places in region `0` with flags `0x04` (initially vacant,
  not rebindable or unique).
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
which support `int32` under revision 1 and all eight non-void scalar types
under revision 2; output-bearing function results range from `void`/`int32`
through supported non-Boolean scalars under revision 3 and Boolean under
revision 4. Constants and by-value scalar operations are supported
independently of the output places, initialized locals and assignments. The
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
casts, preserving overflow failure and `NoThrow` verification. FloatConvert
opcode `0x1A` is accepted for the unreleased `(1, 0)` draft, converting
between distinct `float32`/`float64` types with target-format rounding.
Integer-to-float opcode `0x1B` is also accepted for that draft for the eight
fixed-width integer sources to `float32`/`float64`, with nearest/ties-to-even
target-format rounding. Char16-to-float opcode
`0x1E` is accepted for the unreleased `(1, 0)` draft, interpreting `char16`
as its unsigned code-unit value; it is total and compatible with `NoThrow`.
Float-to-integer opcode `0x1F` is also accepted for that draft; it truncates
toward zero before range checking, preserves the existing overflow status, and
is incompatible with `NoThrow`. Control flow remains one-block except for the
single direct-call split under revisions 5 and 6 described below. These other
value, bitwise, unary, arithmetic and conversion paths are implemented support
only and do not approve other proposed executable contracts. Comparison opcode
`0x19` and exact-width floating `ConstBits` are accepted for the unreleased
draft, as is opcode `0x1D` for same-format `float32`/`float64` addition,
subtraction, multiplication and division. Arithmetic NaN results retain only
the NaN-class guarantee; sign and payload are unspecified.
The current writer emits required feature rule revision 6 for output Bodies.
Rule revision 2 provisionally permits `bool`, `char16`, `int32`, `uint32`,
`int64`, `uint64`, `float32` and `float64` referents and scalar initialized
locals; rule revision 1 remains loadable with `int32` referents only. Rule
revision 3 additionally permits non-Boolean scalar output-bearing results;
rule revision 4 adds Boolean output-bearing results with conditional
publication. Rule revision 5 admits one direct Boolean output-call condition
and two straight-line terminal arms, auditing inherited place state
independently on each edge. The writer emits one such conditional split; the
reader also accepts either terminal arm to use existing `Fail` reason 1.
Rule revision 6 also admits a direct Boolean call condition with no output
arguments; both successors inherit the audited post-call place state unchanged.
These remain provisional, unapproved implementation boundaries.
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

## Additional open question: conditional Boolean output control flow

**Summary:** Revision 4 can encode Boolean output calls but its one-block Body
profile cannot observe the call-specific publication state in executable
artifacts. The hosted verifier already permits the direct result to establish
initialization only on the true branch; storing the result does not preserve
that correlation. Extending this behavior requires a path-sensitive Body
audit, not merely allowing branch opcodes.

**Recommendation:** Keep the extension provisional in rule revision 5. Admit
one acyclic conditional split with two straight-line terminal arms in an
output Body. A direct Boolean output-call condition establishes that call's
destinations as initialized on the true edge and vacant on the false edge;
other places retain their audited post-call entry state on both edges. Each
arm may return normally or use the existing `Fail` reason 1; every
output formal must still be initialized on every normal return. Do not
transfer the correlation through stored Boolean results, copies, comparisons,
negation, short-circuit expressions, other calls or block parameters. Exclude
joins, nested conditions, loops, regions, cleanup, recovery, forwarding and
aliases. The reader must independently recognize and reconstruct the direct
call condition, recompute successor place state rather than trust serialized
inheritance, and retain the hosted verifier. Preserve revisions 1–4 and reject
revision-4 artifacts requiring the new Body shape. This is an engineering
recommendation only; it does not approve the executable contract.

## Additional open question: non-output Boolean call conditions

**Summary:** Revision 5's direct Boolean condition is an output call, so the
reader must correlate that call's result with the publication state of its
destinations. A direct Boolean call with no output arguments has no such
correlation: after evaluating its arguments and the call, both successors
inherit the same audited place state. Argument evaluation may itself affect
places; no-output on the direct call does not imply that the whole condition is
side-effect-free. The Body already carries a Boolean call result and a
three-block terminal split, but the current reader rejects an empty output
destination list.

**Recommendation:** Keep this extension provisional in rule revision 6.
Permit one direct Boolean call with no output-mode arguments as the condition
for the existing two straight-line terminal arms. Recompute the audited
post-call entry state and require both inherited successor states to match it
exactly. Preserve revision 5's output-call behavior and the existing Return or
Fail reason 1 terminals. Continue rejecting arbitrary Boolean expressions,
stored call results, nested or multiple conditions, joins, loops, cleanup,
recovery, forwarding and aliases. Preserve revisions 1–5; reject a revision-5
artifact that requires the new condition shape. This is an engineering
recommendation only and does not approve the executable contract.

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

**Implementation status:** The provisional reader and writer implement this
recommendation: starting and inherited places are observed at block entry with
ordinal 0, while `PlaceLocal`, operand and terminal uses produce the matching
last-use summaries. Regression coverage exercises a vacant place initialized
in either arm of a direct Boolean-condition split. This remains provisional
implementation evidence, not approval of the executable contract.
