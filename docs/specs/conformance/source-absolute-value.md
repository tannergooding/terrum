# Absolute-value member conformance

These cases apply to [absolute-value-members/1 and checked-integer-magnitude/1](../language/absolute-value.md).
They require the stated selected declarations and execution support. They do
not select the remaining generic-math hierarchy, an unchecked variant, an
exception-class catalog or a physical intrinsic.

Finite evidence uses supplied exact identities, target widths, valid scalar
values, call/effect facts and result destinations. Exhaustive 8-bit inputs
and wider mathematical boundaries are independent of a Terrum compiler.
Floating evidence uses exact supplied values/classes, not complete format
decoding, arithmetic providers or observable signaling/status behavior.

## Identities and call shape

| Case | Input or condition | Required outcome |
|---|---|---|
| AVID-01 | Each of the 20 numeric scalar owners | Public static nongeneric same-owner `T Abs(T value)` shape |
| AVID-02 | Receiver, optional/default parameter, init output or generic-method variation | Not the adopted one-value-formal signature |
| AVID-03 | Each small fixed integer subject | Same small result type; no promotion |
| AVID-04 | Native and equal-width fixed subjects | Distinct complete owner and result identities |
| AVID-05 | Transparent alias for an exact numeric owner | Preserve normalized owner/member identity |
| AVID-06 | Each of the six nonnumeric language scalars | No built-in Abs participation |
| AVID-07 | Wrapper, enum, optional or SIMD with equal carrier bits | No structural acquisition of the numeric member |
| AVID-08 | Same-spelled nominal or extension member | Ordinary separate identity, not an adopted numeric role |
| AVID-09 | A literal argument with a selected numeric owner | Owner supplies exact argument context, not a default width |
| AVID-10 | Typed argument from another numeric identity, even if its value fits | No implicit numeric binding |
| AVID-11 | Expected wider result around a small Abs call | Preserve call result identity; widening requires an explicit conversion |
| AVID-12 | Actual source member registration, lookup, alias and named-argument binding | Document-only: finite supplied signature facts do not prove a Terrum binder or registration codec |

## Integer magnitude and boundaries

| Case | Input or condition | Required outcome |
|---|---|---|
| AVIN-01 | Every int8 value | Exact nonnegative result except -128, which fails with no result |
| AVIN-02 | Every uint8 value | Value unchanged |
| AVIN-03 | Minimum of each fixed signed width | Checked overflow, not wrapped/saturated success |
| AVIN-04 | Minimum plus one, negative maximum, -1, zero, one and maximum at each signed width | Exact same-type mathematical magnitude |
| AVIN-05 | Zero and maximum at each fixed unsigned width | Value unchanged without a signed intermediate |
| AVIN-06 | Supplied 32- and 64-bit nint target contexts | Use each target's boundary; host width does not participate |
| AVIN-07 | Supplied 32- and 64-bit nuint target contexts | Preserve the complete unsigned range and native identity |
| AVIN-08 | Exact mathematical magnitude of a signed minimum | Equals maximum plus one; cannot fit in the same signed type |
| AVIN-09 | Signless int8 based-bit input 0x80 | Valid -128 input reaches Abs overflow |
| AVIN-10 | Direct signed int8 literal -128 | Valid argument construction; failure belongs to Abs |
| AVIN-11 | int8 input -127 | int8 result 127, not int32 |
| AVIN-12 | uint128 maximum | Same uint128 value; no reinterpretation as signed -1 |
| AVIN-13 | Signed minimum whose magnitude fits the corresponding unsigned width | No implicit unsigned result |
| AVIN-14 | Wider expected destination after an Abs on a signed minimum | Does not widen the intermediate or avoid overflow |
| AVIN-15 | Scalar source copied into a by-value call | Source remains live and unchanged |
| AVIN-16 | Supplied domain excluding versus including the signed minimum | Exact magnitude safety differs; no private-body proof requirement is added |
| AVIN-17 | Width-limited wrapping magnitude versus ordinary Abs | Wrapping maps minimum to itself; ordinary Abs still rejects; no public variant is adopted |
| AVIN-18 | Actual software/native magnitude helper and runtime failure path | Document-only: mathematical boundaries do not prove helper code, OOM handling or generated execution |

## Floating value classes

| Case | Input or condition | Required outcome |
|---|---|---|
| AVFL-01 | Positive/negative exact finite values in each of the eight formats | Nonnegative magnitude in that same format |
| AVFL-02 | Either zero sign in each format | Positive zero |
| AVFL-03 | Either infinity sign in each format | Positive infinity |
| AVFL-04 | Quiet/signaling NaNs with distinct signs/payloads | Clear only sign; preserve payload and quiet/signaling state |
| AVFL-05 | Positive/negative supplied minimum subnormal value for each format | Same nonzero magnitude, not flush-to-zero |
| AVFL-06 | Supplied float128 or decimal128 magnitude below binary64's smallest positive value | No binary64 intermediary or lost magnitude |
| AVFL-07 | Abs is replaced with an arithmetic NaN-processing operation | Reject if it quiets, changes payload or otherwise changes a non-sign bit |
| AVFL-08 | Finite decimal values in different equal-valued cohorts | Clear only sign; preserve each supplied coefficient/quantum and other admitted bits |
| AVFL-09 | Infinity or NaN under the magnitude value contract | No integer-overflow or invalid-bound value-domain failure |
| AVFL-10 | All actual format encodings, sign-only behavior and native control duties | Document-only: finite value/class evidence is not a complete floating provider or effect implementation |

## Evaluation and result state

| Case | Input or condition | Required outcome |
|---|---|---|
| AVEX-01 | Successful argument with a counted effect | Evaluate/stage once before magnitude and result publication |
| AVEX-02 | Argument effect produces the signed minimum | Effect remains; magnitude fails and publishes no result |
| AVEX-03 | Argument itself fails | Preserve that failure; do not enter Abs or substitute overflow |
| AVEX-04 | Source changes after the call's copied input/result | Previously staged value/result does not change |
| AVEX-05 | Nested Abs invocation, including failing inner Abs | Inner evaluation occurs once; inner failure prevents the outer operation |
| AVEX-06 | Exact overflow payload, failure construction, local dispatch or terminal handling | The [scalar catalog](../language/scalar-failures.md) selects OverflowException; actual allocation, handler and terminal implementation remain document-only |
| AVEX-07 | Authored recovery around a failed Abs | Recovery value belongs to the handler, not a successful Abs result |
| AVEX-08 | Assignment into an existing scalar destination | Successful result replaces it; failed result performs no replacement |
| AVEX-09 | Initialization into a vacant scalar destination | Success publishes a live value; failed result publishes nothing |
| AVEX-10 | Permissive versus required wrapping policy with no declared Abs variant | Permissive selection uses checked Abs; required missing variant rejects, not invented wrapping |
| AVEX-11 | Explicit widening conversion after a small Abs | Convert only a successful same-type result; minimum failure occurs before conversion |
| AVEX-12 | Explicit move of a Copy scalar during argument staging | Source stays vacant on successful magnitude or subsequent overflow; no implicit restoration |
| AVEX-13 | Argument writes an existing destination before magnitude overflow | Retain that actual write; failed result publication is not rollback |
| AVEX-14 | Actual source staging, cleanup, async/producer channels and lowering | Document-only: finite call/state traces do not prove executable Terrum scheduling or lifetime code |

## Effects and independent admission

| Case | Input or condition | Required outcome |
|---|---|---|
| AVAD-01 | Missing required magnitude rule | Decline before affected argument evaluation |
| AVAD-02 | Missing exact owner/member, parameter or result contract | Decline; a name or equal ABI is insufficient |
| AVAD-03 | Missing required failure/effect/helper support, even for a selected retained safe-input call | Decline the affected use, not a success-shaped fallback |
| AVAD-04 | Wrong retained native target width | Cannot silently use the host or another target's domain |
| AVAD-05 | Same carrier but wrapped, saturated or unsigned-result replacement | Does not satisfy the checked member's retained contract |
| AVAD-06 | Support for only an equal-width different scalar identity | Does not admit the missing owner |
| AVAD-07 | Mathematical safety facts versus the complete operation contract | Require the actual applicable domain and all checked failure/effect guarantees |
| AVAD-08 | Pure alone, ConstEval with support, or neither | Preserve distinct authorization and actual-program requirements |
| AVAD-09 | Missing constant program or a reached constant failure | No runtime fallback or published constant result |
| AVAD-10 | Compiler-only dependency with no retained runtime path versus an actual retained helper | Only the actual retained closure requires runtime support |
| AVAD-11 | Unrelated valid provider additions and selective consumer access | Do not invent an all-Body scan or universal replacement break |
| AVAD-12 | Actual declaration/variant/constant/call/failure physical codecs and independent reader | Document-only: logical admission facts allocate no records, opcodes or source-free implementation |

## Evidence boundary

There are 66 cases. AVID-12, AVIN-18, AVFL-10, AVEX-06, AVEX-14 and
AVAD-12 are document-only obligations. The other 60 have finite identity,
mathematical, value-class, state or supplied-admission evidence.
That evidence does not claim implementation conformance, public generic-math
completion, selected unchecked APIs or fixed floating payload/status choices.
