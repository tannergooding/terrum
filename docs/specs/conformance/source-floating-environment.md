# Floating environment conformance cases

These cases apply to the [floating contracts](../language/floating-environment.md)
with admitted formats, values, execution forms and dependencies.
Representation facts and supplied result choices are not execution of an
IEEE provider, Terrum compiler, constant interpreter or foreign adapter.

| Case | Situation | Required outcome |
|---|---|---|
| FENV-01 | Ordinary floating execution has no selected status-aware API | No ambient language flag value/event/callback |
| FENV-02 | Equal value executions leave different incidental hardware status bits | Not a distinct ordinary Terrum observation |
| FENV-03 | Non-stop arithmetic is used to infer that no hardware flag was raised | No such inference; language observability and physical flags differ |
| FENV-04 | Software arithmetic has no hardware status register | Same selected value contract; no sticky-flag simulation requirement |
| FENV-05 | An actual ABI requires nonvolatile control restoration | Preserve that obligation; no ambient-status rule weakens it |
| FENV-06 | Negate finite/subnormal inputs in each binary format | Toggle exactly sign; retain all other bits |
| FENV-07 | Negate/Abs either zero sign | Negation toggles; Abs clears |
| FENV-08 | Negate/Abs either infinity sign | Negation toggles; Abs clears |
| FENV-09 | Quiet/signaling NaNs with either sign and different payloads | Negation toggles and Abs clears only sign; quiet/signaling state and payload survive |
| FENV-10 | Negate twice over every binary16/bfloat16 encoding | Original bits |
| FENV-11 | Abs twice over every binary16/bfloat16 encoding | Same sign-cleared bits as once |
| FENV-12 | Decimal finite/zero inputs use different valid quantum exponents | Sign operation preserves each coefficient/quantum |
| FENV-13 | A target admits distinct encodings of one decimal representation | Sign operation preserves every admitted non-sign bit, not a normalized re-encoding |
| FENV-14 | Floating arithmetic consumes quiet/signaling NaN | Quiet NaN result in every binary/decimal format; no mutation of staged input |
| FENV-15 | Invalid binary arithmetic creates NaN without a NaN input | Quiet NaN, not any signaling result |
| FENV-16 | An admitted nonidentity numeric conversion returns NaN | Quiet destination NaN; runtime payload/sign unspecified |
| FENV-17 | Copy, unary plus or same-format identity observes a signaling NaN | Preserve its supplied representation |
| FENV-18 | Min/Max receive NaN in either/both positions | Quiet NaN after both arguments stage; no first-NaN short circuit |
| FENV-19 | Valid-bound Clamp receives a signaling NaN value | Quiet NaN, not a signaling copy |
| FENV-20 | Clamp has NaN value and unordered/reversed bounds | Bounds failure before result; no canonical NaN substitute |
| FENV-21 | Non-NaN selection chooses decimal/zero/subnormal input | Preserve selected input representation and existing tie rules |
| FENV-22 | Admitted constant arithmetic/conversion/Min/Max/Clamp produces NaN | Positive quiet zero-payload NaN in the result format |
| FENV-23 | That canonical constant NaN is negated | Negative quiet zero-payload NaN, not canonicalized again |
| FENV-24 | Admitted constant Abs receives a negative signaling NaN | Positive signaling NaN with original payload |
| FENV-25 | A supplied NaN is copied before sign/processing operations | Original copy remains unchanged |
| FENV-26 | Canonical-result recipe is mistaken for Pure/ConstEval opt-in | Reject required evaluation without actual authorization/program support |
| FENV-27 | Target-required control fields versus volatile status bits are retained | Check only the actual control contract; no blanket environment-preservation promise |
| FENV-28 | Runtime processing-result choice varies sign/payload or returns signaling NaN | Accept valid quiet choices; reject signaling/wrong-format choices |
| FENV-29 | All actual IEEE arithmetic/conversion providers and valid decimal encodings | Document-only: representation/result fixtures do not implement full arithmetic |
| FENV-30 | Actual native control restoration and unwind machine code | Document-only: supplied control states do not prove generated execution |
| FENV-31 | Actual source/member/constant binding and interpreter programs | Document-only: logical eligibility is not executable compiler support |
| FENV-32 | Actual helper resources, runtime effects and foreign observability | Document-only: no blanket failure/purity/ABI implementation claim |
