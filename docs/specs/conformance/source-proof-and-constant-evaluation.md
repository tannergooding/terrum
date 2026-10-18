# Proof and constant-execution conformance cases

These cases apply [the versioned floor](../language/proof-and-constant-evaluation.md),
[structural flow](../language/initialization-and-flow.md), and
[primitive arithmetic](../language/primitive-operators.md).
They are specification obligations, not executed compiler tests.
Assume correctly bound source, live valid values, available selected bodies
and admitted value encodings unless a case tests that condition.

`I`, `F` and `C` have the primitive chapter's exact meanings.
Proof expectations describe the reference floor. A sound beyond-floor engine
may establish a listed not-established result; it cannot contradict a
definite false result or change constant execution at the same selected
dependencies, target and envelope. Structural source checks remain mandatory.
Names such as `ToBits`, `FromBits` and `Log` are illustrative selected
declarations, not name-based intrinsic registration.

## Scalar facts and exact values

| Case | Situation | Required floor outcome |
|---|---|---|
| PFACT-01 | An initialized integer literal supplies a local value | Exact same-type singleton, without promotion |
| PFACT-02 | A local copies an exact integer then participates in a proof | Same exact value under its own binding/revision |
| PFACT-03 | `int32 denominator = 0;` precedes division by denominator | Same definite division failure as a direct typed zero |
| PFACT-04 | Assignment replaces a tracked scalar | Advance its revision and use the new fact on the normal edge |
| PFACT-05 | An integer fact excludes one interval endpoint | Trim that endpoint before extrema-based checking |
| PFACT-06 | Exclusions remove every value in a finite interval | Bottom, not a made-up surviving value |
| PFACT-07 | Duplicate/outside exclusions are normalized | One sorted in-range occurrence per excluded value |
| PFACT-08 | Native integer fact is inspected under 32/64-bit targets | Use each selected range, not the host's width |
| PFACT-09 | A nominal wrapper has integer-shaped fields | No automatic integer proof/operator adaptation |
| PFACT-10 | A transparent alias expands to the same scalar | Use the normalized identity and target range |
| PFACT-11 | A Boolean is initialized to true or false | Exact singleton truth set |
| PFACT-12 | An arbitrary live Boolean is read | Truth set contains both values; no numeric domain |
| PFACT-13 | Boolean negation uses a known truth set | Exchange true and false |
| PFACT-14 | Eager Boolean operations use exact operands | Apply the two-valued table after preserving both evaluations |
| PFACT-15 | A unit value is known | Unit fact without removal of availability/effects |
| PFACT-16 | A supported exact floating operation has a finite result | Round in its own format and retain the exact result |
| PFACT-17 | Exact floating operands produce an unspecified runtime NaN | Class-only NaN, not constant-evaluator payload bits |
| PFACT-18 | A frozen constant carries explicit NaN bits | Known representation under the selected encoding |
| PFACT-19 | A copied negative zero is known | Retain its sign; numeric equality with positive zero is still true |
| PFACT-20 | Decimal cohorts are numerically equal but have different exponents | Equal numeric observation, not identical representation facts |
| PFACT-21 | Known raw char16 surrogate and FFFF values compare | Exact unsigned raw-unit result, without decoding |
| PFACT-22 | Known valid rune values compare | Exact scalar-value ordering |
| PFACT-23 | An unavailable/vacant scalar appears in a proof expression | Structural diagnostic before numeric proof; do not read to get false |
| PFACT-24 | A constant-formal value has an admitted nominal domain | Preserve that domain/identity; do not widen the constant-formal catalog |

## Predicates, branches and joins

| Case | Situation | Required floor outcome |
|---|---|---|
| PBRANCH-01 | True edge of `x == c` for integer x and exact same-typed c | Intersect with c |
| PBRANCH-02 | False edge of `x == c` | Exclude c |
| PBRANCH-03 | True/false edges of `x != c` | Exclude/intersect c respectively |
| PBRANCH-04 | True/false edges of `x < c` | Upper at c-1 / lower at c |
| PBRANCH-05 | True/false edges of `x <= c` | Upper at c / lower at c+1 |
| PBRANCH-06 | True/false edges of `x > c` | Lower at c+1 / upper at c |
| PBRANCH-07 | True/false edges of `x >= c` | Lower at c / upper at c-1 |
| PBRANCH-08 | The exact constant is the left comparison operand | Reverse the refinement relation without runtime reordering |
| PBRANCH-09 | A bound would be outside the scalar range | Clip mathematical bounds; no out-of-range source constant construction |
| PBRANCH-10 | An unsigned x is tested below same-typed zero | True edge bottom |
| PBRANCH-11 | Signed int8 x is tested above same-typed maximum | True edge bottom, without computing int8(128) |
| PBRANCH-12 | `x != 0 && x > -10` succeeds | RHS sees nonzero refinement; result retains both conditions |
| PBRANCH-13 | `x < 0 \|\| x > 0` succeeds | Join true alternatives while retaining zero exclusion |
| PBRANCH-14 | A Boolean test is false/true | Refine that binding's truth set on the respective edge |
| PBRANCH-15 | `!predicate` is tested | Exchange predicate edges |
| PBRANCH-16 | Eager `&`/`\|` has a determining LHS and an effectful RHS | Preserve RHS; no short-circuit proof substitution |
| PBRANCH-17 | Integer domains are disjoint | Equality false and inequality true |
| PBRANCH-18 | Integer extrema order every represented pair | Prove that order |
| PBRANCH-19 | Stable integer/Boolean/code-unit value compares with itself | Reflexive equality true |
| PBRANCH-20 | Arbitrary floating value compares with itself | Equality not established because NaN remains possible |
| PBRANCH-21 | A supported tested `x < y` is reused with unchanged scalar revisions | Its selected-edge truth may be reused; no relational arithmetic inference |
| PBRANCH-22 | Two branches produce -1 and +1, then require nonzero | Join hull with zero excluded |
| PBRANCH-23 | Two branches produce 1 and 3; later supported predicate compares with typed 2 | The catalog/join can exclude 2; no enumeration of arbitrary gaps |
| PBRANCH-24 | One reachable arm has x=0 and another excludes zero | Join contains zero; no nonzero proof |
| PBRANCH-25 | One join input is bottom and the other reachable | Keep the reachable input facts |

## Arithmetic safety and completion

| Case | Situation | Required floor outcome |
|---|---|---|
| PSAFE-01 | int32 x is within 0..100, then adds same-typed 1 | Non-failing addition; result within 1..101 |
| PSAFE-02 | int8 x is within 0..126, then adds same-typed 1 | Non-failing addition in int8 |
| PSAFE-03 | int8 x may be 127, then adds same-typed 1 | Safety not established; a successful-edge result does not remove overflow |
| PSAFE-04 | int8 x is exactly 127, then adds 1 | Definite overflow |
| PSAFE-05 | int32 x/y has y strictly positive | Division non-failing for every represented x |
| PSAFE-06 | Divisor domain contains zero | Division/remainder nonfailure not established |
| PSAFE-07 | Divisor is exactly zero | Definite division-by-zero failure |
| PSAFE-08 | Signed minimum and -1 remain possible together for division | Quotient safety not established |
| PSAFE-09 | Dividend excludes signed minimum, divisor excludes zero | Quotient safe even when -1 remains possible |
| PSAFE-10 | Divisor excludes both zero and -1 | Quotient safe for any represented signed dividend |
| PSAFE-11 | Signed minimum remainder -1 is exact | Non-failing zero remainder |
| PSAFE-12 | Remainder divisor excludes zero | Remainder non-failing despite possible quotient overflow |
| PSAFE-13 | Unsigned negation input is zero | Non-failing same-type zero |
| PSAFE-14 | Unsigned negation input excludes zero | Definite overflow |
| PSAFE-15 | Unsigned negation input includes zero and nonzero | Safety not established; successful result can only be zero |
| PSAFE-16 | Signed negation excludes the minimum | Non-failing |
| PSAFE-17 | Bitwise operation has non-exact operands | Non-failing width-limited result; full-type result precision is sufficient |
| PSAFE-18 | int32 count is refined into 0..31 for uint32 data | Shift count check non-failing |
| PSAFE-19 | int8 count is refined nonnegative for uint128 data | All represented counts fit; do not construct int8(128) |
| PSAFE-20 | A typed wide count may exceed the data width | Count safety not established; no uint32 narrowing proof shortcut |
| PSAFE-21 | Count is exactly negative or exactly width | Definite invalid-count failure |
| PSAFE-22 | A source count operand first performs nonzero unsigned negation | Prove/diagnose operand overflow before considering shift safety |
| PSAFE-23 | Floating division has an arbitrary initialized divisor including zero | Arithmetic itself is non-failing; IEEE normal value classes remain possible |
| PSAFE-24 | Getter/RHS/setter can fail during a scalar update | Numeric range safety alone does not prove the whole update NoThrow |
| PSAFE-25 | A declared operation has a checked NoThrow contract | Consume its published safety contract without private body inference |
| PSAFE-26 | Runtime arithmetic computes an integer result on a successful edge | Intersect result bounds with its type range, without retroactive safety proof |
| PSAFE-27 | A definitely failing runtime expression is written directly or through an exact local | Same warning/promotable-to-error classification; required constant/NoThrow contexts remain errors |
| PSAFE-28 | An admitted normal-return clause guarantees a copied scalar result is positive | Refine that result's local fact and permit nonzero division use; no private body inspection |
| PSAFE-29 | An int32 dividend ranges over 1..6 and the divisor is exactly 6 | Normal remainder bounds include 0..5; endpoint remainders 1 and 0 do not prove the result at most 1 |

## Saved facts, mutation, loops and effects

| Case | Situation | Required floor outcome |
|---|---|---|
| PSTATE-01 | A Boolean saves a supported predicate and is later tested | Restore its edge facts if subject revisions are unchanged |
| PSTATE-02 | That Boolean is directly copied | Copy its relationship under the new Boolean binding |
| PSTATE-03 | Only the first Boolean binding is reassigned | Invalidate that binding's old relation, not its unchanged copy |
| PSTATE-04 | A subject scalar is reassigned after the predicate was saved | Remove every affected relationship; keep saved Boolean values |
| PSTATE-05 | A new subject value happens to equal its old value | Do not restore a discarded relationship by spelling/value coincidence |
| PSTATE-06 | An opaque call occurs after a scalar was independently copied into a non-exposed local | Preserve that copied fact |
| PSTATE-07 | An opaque call may reach an exposed mutable local | Reset its numeric fact and affected predicate relations |
| PSTATE-08 | A published footprint establishes no relevant mutation | Preserve facts justified by that footprint |
| PSTATE-09 | A readonly getter observes mutable backing | No stable scalar observation inferred from readonly |
| PSTATE-10 | A shared/volatile/atomic value is read repeatedly | No repeated-value fact from these rules |
| PSTATE-11 | A local value is moved or explicitly ended | Remove its numeric fact; ordinary availability becomes authoritative |
| PSTATE-12 | A cyclic loop can modify x | Reset x at the cyclic header/exit; no entry-value induction |
| PSTATE-13 | A loop never modifies independent copied scalar y | Preserve y's fact |
| PSTATE-14 | Current loop condition establishes a count range | Refine the current iteration after the conservative reset |
| PSTATE-15 | A break edge lacks the loop condition's false-edge fact | Join exits; do not impose the condition's negation on break |
| PSTATE-16 | A structural initialization loop joins live/vacant state | Run its mandatory finite structural analysis regardless of numeric reset |
| PSTATE-17 | A private function body suggests a useful numeric result | No required private-call inference |
| PSTATE-18 | A published conditional output status is saved and tested | Keep mandatory structural outcome facts; do not confuse them with optional numeric summaries |
| PSTATE-19 | A successful ordinary primitive require checks nonzero | Refine its normal continuation; its own possible failure remains |
| PSTATE-20 | A checked predicate is true but argument/getter evaluation has effects | Do not remove those effects merely by removing the check |
| PSTATE-21 | A copied scalar return value is saved before finally mutates its source | Preserve the saved scalar result's fact |
| PSTATE-22 | A returned reference observes backing mutated by finally | Invalidate/recompute dependent facts; no historical snapshot |
| PSTATE-23 | Phase cleanup invalidates a published predicate | Reestablish/prove survival or diagnose the declaration; do not replay the predicate |
| PSTATE-24 | Static ensure refers to a body local already retired at semantic completion | Availability diagnostic; no implicit saved local/history |
| PSTATE-25 | Static ensure appears directly in the outer body | Check all normal completion paths, not just paths through its location |
| PSTATE-26 | Static ensure appears in an inner block or declaration signature | Reject the unsupported placement |
| PSTATE-27 | An admitted primitive precondition over a by-value parameter survives phase cleanup | Use its successful-phase fact in the body; its check/effects remain separate |

## Proof outcomes and resource categories

| Case | Situation | Required floor outcome |
|---|---|---|
| PDIAG-01 | Facts establish true and predicate completion | Proven |
| PDIAG-02 | Facts establish false | Disproved, not exhausted/unknown |
| PDIAG-03 | Truth/safety is mixed within represented states | Not established, not a claimed concrete counterexample |
| PDIAG-04 | Numeric relational solving or loop induction would be needed | Not established by the floor; sound extensions permitted |
| PDIAG-05 | Floor work would exceed the selected limit | Exhausted; required proof rejects without runtime fallback |
| PDIAG-06 | Floor abstract state would exceed its limit | State exhaustion, not false |
| PDIAG-07 | An explicit lower proof envelope is selected | Use that envelope; no hidden credit from the default |
| PDIAG-08 | One callable has many point obligations | They share its analysis/catalog work envelope |
| PDIAG-09 | Canonical proof-event trace costs exactly W units | Budget W permits it; W-1 exhausts |
| PDIAG-10 | A required proof point is structurally/numerically unreachable | Vacuous proof after mandatory source validation |
| PDIAG-11 | A scalar has illegal type/binding or malformed constant input | Source/value diagnostic before treating it as unknown proof |
| PDIAG-12 | A no-return path escapes normal completion | No normal ensure edge; no invented successful result |
| PDIAG-13 | An implementation establishes an outside-floor obligation soundly | Additional acceptance is permitted without changing runtime semantics |
| PDIAG-14 | A host timeout interrupts analysis | Tool failure/cancellation, not a language false proof |
| PDIAG-15 | Cached facts avoid physical transfer work | Reference floor charges remain unchanged |
| PDIAG-16 | Two callers use an imported declaration | Use published contracts, not imported implementation inspection for ordinary proof |

## Constant forms, values and references

| Case | Situation | Required result |
|---|---|---|
| EFORM-01 | `const int32 x = F();` succeeds | Frozen named Copy value; runtime reads do not rerun F |
| EFORM-02 | `int32 x = const F();` succeeds | Ordinary local initialized from a frozen expression; not a constant declaration |
| EFORM-03 | `const F() + runtimeValue` | Only F's expression is required constant |
| EFORM-04 | `const (F() + runtimeValue)` | Entire group must form a constant or diagnose |
| EFORM-05 | `const(expr)` appears | Prefix plus grouping, not a callable or new function category |
| EFORM-06 | A local constant also specifies mut/init or has no initializer | Reject that local declaration |
| EFORM-07 | A constant local appears in a for initializer | Ordinary header declaration space; no replaceable constant slot |
| EFORM-08 | A constant name is borrowed as if it were a runtime place | No assumed backing; use an ordinary admitted materialization where permitted |
| EFORM-09 | All 26 valid scalar types form ordinary constants | Preserve their exact identities, validity and target semantics |
| EFORM-10 | Native arithmetic/encoding has a selected target width | Use that width, not evaluator host nint |
| EFORM-11 | An admitted nominal enum constant is formed | Keep nominal identity and admitted encoding |
| EFORM-12 | An inline Copy tuple/aggregate has Blittable and value-codec admission | Execute actual eligible construction and complete all required fields |
| EFORM-13 | A nominal aggregate only happens to have scalar-shaped private fields | No inferred encoder or constructor registration |
| EFORM-14 | An eligible Transparent integer/Boolean wrapper supplies a constant argument | Enforce its validity/complete initialization and canonical nominal/carrier value identity; no second domain opt-in is required |
| EFORM-15 | An ordinary aggregate/floating constant is used as a generic value formal | Apply the narrow formal-domain rule; ordinary const eligibility does not expand it |
| EFORM-16 | A Blittable/codec-admitted inline optional/union has an inactive payload arm | Preserve active-tag/arm validity; do not read vacant inactive data |
| EFORM-17 | Private evaluator locals are mutated in a bounded loop | Allowed under ordinary initialization/access/check rules |
| EFORM-18 | An init-reference output is initialized on success | Descriptor remains live; destination availability follows its actual state |
| EFORM-19 | A parser returns false with a vacant output | Literal diagnostic; never serialize/read/default that destination |
| EFORM-20 | An internal reference points to a live evaluator slot | Logical typed access is permitted; no host pointer |
| EFORM-21 | A result refers into an evaluator activation/input temporary | Reject escaping backing, even when its bytes appear stable |
| EFORM-22 | A registered static-data-backed value has a complete constant/backing contract | Use that admitted contract; not an automatic span representation |
| EFORM-23 | Managed identity, native resources or arbitrary graphs are required | Unsupported minimum constant value domain |
| EFORM-24 | Host/target address bits are requested | Unsupported; do not bake in process-specific bits |
| EFORM-25 | A valid rune/code unit is supplied | Preserve its actual validity; raw code units do not undergo scalar normalization |
| EFORM-26 | Malformed/invalid encoded scalar input is supplied | Reject before it justifies evaluation |
| EFORM-27 | A compile-time result is unit | One live logical value with zero payload bytes, not no result |
| EFORM-28 | A required constant context has unresolved types/overload | Binding diagnostic before parser/evaluation trial |
| EFORM-29 | ConstEval body uses an ordinary parameter in a definition-time const requirement | Annotation alone does not make the parameter a definition-time constant |
| EFORM-30 | A non-constant ordinary expression could be optimized | Optional folding remains separate; it does not satisfy an explicit constant-only context |
| EFORM-31 | Assignment or source lifetime end targets a constant name | Reject the non-place target; no mutable or vacant constant slot |
| EFORM-32 | A constant initializer reads its own name | Reject the unavailable self-dependent value; no recursive initialization |
| EFORM-33 | A block reads a constant name before its declarator, with an outer member of that name | Point-of-declaration error; do not fall back to the member |
| EFORM-34 | An inner constant reuses an enclosing local/parameter name | Declaration-space collision, independently of source order or value lifetime |
| EFORM-35 | Two sibling blocks declare the same constant name without an enclosing collision | Independent declarations and scopes are permitted |
| EFORM-36 | An initialization-state test targets a constant name | Reject the non-place operand; the frozen value is not an initialization-tracked runtime slot |

## Executed paths, effects and publication

| Case | Situation | Required result |
|---|---|---|
| EPATH-01 | Checked Square guards Log with !IsPureEval; false trace is supplied | Constant mode skips logging and produces 9 for input 3 |
| EPATH-02 | The same checked Square receives true trace in required work | Constant mode still skips logging and produces 9 |
| EPATH-03 | An unused branch contains a known ordinary unmarked call | No constant body needed for that unexecuted call |
| EPATH-04 | The selected path reaches an unmarked/opaque call | Missing opt-in/body diagnostic, not native invocation |
| EPATH-05 | A selected body contains an unknown instruction on an unused edge | Reject complete body schema before execution |
| EPATH-06 | An unused branch contains an out-of-range required literal | Binding/literal formation still diagnoses |
| EPATH-07 | A selected call has an available eligible body | Execute it with staged source-order receiver/arguments and actual default mapping |
| EPATH-08 | A selected static witness needs a body | Require the exact witness's constant implementation/default, not an extension-name substitute |
| EPATH-09 | A selected constructor/getter/initialization accessor has an admitted constant form | Execute its phases and permitted private/result effects |
| EPATH-10 | An actually admitted private-state scalar update uses accessors | Capture once, save old value, evaluate RHS, operate, set only on success |
| EPATH-11 | Earlier RHS writes complete before arithmetic failure | Retain them in the trace; no transactional rollback or constant value publication |
| EPATH-12 | Integer divide/overflow/count failure is reached | Constant failure diagnostic under its actual category |
| EPATH-13 | A floating operation produces infinity/underflow/NaN | Normal floating result under target rules, not integer-style failure |
| EPATH-14 | An ordinary requirement is false | Requirement failure prevents constant publication |
| EPATH-15 | A getter or selected body cleanup fails | No successful result, even if arithmetic/return value was computed |
| EPATH-16 | Body finally modifies a returned reference's backing | Ordinary reference/cleanup semantics remain; no replayed return expression |
| EPATH-17 | A precondition fails | Body not entered; ordinary private callee responsibility, no parser/output success |
| EPATH-18 | A postcondition fails after body cleanup | Diagnose without rerunning completed body cleanup |
| EPATH-19 | An ambient mutable global, clock/culture/environment or volatile observation is reached | Unsupported constant effect |
| EPATH-20 | Suspension, synchronization, producer or foreign call is reached | Unsupported minimum constant execution; runtime feature admission is insufficient |
| EPATH-21 | An unsafe recovery/exception-object path is reached | Unsupported absent a separately admitted constant recovery/value feature |
| EPATH-22 | Runtime static initialization has effects | Do not initialize runtime storage in the evaluator or mark its group complete |
| EPATH-23 | An immutable published constant is read | Use its recorded stable value/ABI dependency |
| EPATH-24 | ConstEval annotation is treated as ordinary-form NoThrow or termination | Pure-form support is implied; neither unconditional inference is valid |
| EPATH-25 | A hidden materialization/output site repeats inside a constant loop | Reuse supporting-scope storage with ordinary replacement/dependency rules |
| EPATH-26 | A supporting scope is entered but the site is skipped | Its initially vacant hidden slot exists for accounting; no site invocation/evaluation |
| EPATH-27 | A source scope containing hidden sites is never entered | No activation/frame/hidden storage for that scope |
| EPATH-28 | Authorized folding uses a supported pure/constant form | Omit only authorized guarded effects; preserve arguments, initialization, cleanup and other failures |

## NaNs, signed zero and representation

| Case | Situation | Required result |
|---|---|---|
| ENAN-01 | Required constant arithmetic produces an unspecified NaN | Positive quiet NaN with zero payload in that F |
| ENAN-02 | That result is observed under IEEE binary16 bits | 7E00 |
| ENAN-03 | That result is observed under bfloat16 bits | 7FC0 |
| ENAN-04 | That result is observed under IEEE binary32 bits | 7FC00000 |
| ENAN-05 | That result is observed under IEEE binary64 bits | 7FF8000000000000 |
| ENAN-06 | That result is observed under IEEE binary128 bits | 7FFF8000000000000000000000000000 |
| ENAN-07 | An IEEE decimal arithmetic NaN is formed | Positive quiet zero-payload class; encoding requires selected BID/DPD/value ABI |
| ENAN-08 | A NaN payload/sign is explicitly constructed then copied | Preserve the supplied valid representation |
| ENAN-09 | Unary plus observes that copied NaN | Identity/copy, not arithmetic canonicalization |
| ENAN-10 | An explicitly bit-preserving sign operation observes a NaN | Preserve every bit its explicit contract promises |
| ENAN-11 | Ordinary constant arithmetic consumes that NaN | Apply deterministic arithmetic-result policy |
| ENAN-12 | A canonical positive quiet NaN is negated in constant execution | Negative quiet zero-payload NaN; preserve every non-sign bit rather than canonicalizing again |
| ENAN-13 | A format-changing conversion leaves its NaN output bits unspecified | Apply the destination format's deterministic constant policy |
| ENAN-14 | An identity conversion/copy observes explicit NaN bits | Retain bits; do not reinterpret it as format-changing arithmetic |
| ENAN-15 | Runtime NaN bits are tested against the constant policy | Not established from this policy |
| ENAN-16 | A frozen arithmetic NaN constant is read and bit-observed | Its emitted known representation is repeatable |
| ENAN-17 | A runtime arithmetic NaN follows exact operands | Class-only proof fact; do not borrow constant execution's encoding |
| ENAN-18 | A copied negative zero is encoded | Preserve sign |
| ENAN-19 | Exact constant underflow rounds to negative zero | Retain negative sign |
| ENAN-20 | Constant floating remainder is exact zero for a negative dividend | Retain dividend sign |
| ENAN-21 | Decimal remainder cohorts (550,-2) and (20,-1) are evaluated | (150,-2), not normalized (15,-1) |
| ENAN-22 | Equal-valued decimal cohorts are copied/encoded | Retain required cohort despite numeric equality |
| ENAN-23 | A constant operation is rewritten by host precision/FMA/flush policy | Reject the changed finite/zero result |
| ENAN-24 | A cache reuses a result under a different rule/value ABI | No reuse without full dependency/envelope equivalence |

## Exact resource boundaries

The small expression fixture uses already formed int32 input constants 1
and 2 and a four-byte little-endian int32 value encoding. Its actions are
two reads, one addition, one final value validation and four output bytes:
eight steps. Binding/literal-parse requests are separate. The result cell
uses five abstract bytes; input/result overlap peaks at fifteen, while the
later result/encoding-buffer overlap is nine. There is no entered callable
frame in that expression fixture.

| Case | Situation | Required result |
|---|---|---|
| ELIMIT-01 | Small expression fixture has work 8, storage at least 15, result limit 4 | Value 3, bytes 03 00 00 00 |
| ELIMIT-02 | Same fixture has work 7 | Work exhaustion during encoding, no partial success |
| ELIMIT-03 | Same fixture has work 2 | Exhaust before applying addition |
| ELIMIT-04 | Same fixture has work 3 | Addition may finish; final validation exhausts, no constant publication |
| ELIMIT-05 | Same fixture has live storage 15 | Peak fits inclusively |
| ELIMIT-06 | Same fixture has live storage 14 | Storage exhaustion before creating the addition result |
| ELIMIT-07 | Same fixture has encoded result limit 4 | Payload fits inclusively |
| ELIMIT-08 | Same fixture has encoded result limit 3 | Oversized result diagnostic before emitting a partial value |
| ELIMIT-09 | A unit expression has read/validation and zero encoded bytes | Two steps and one logical state byte; zero result-byte limit is sufficient |
| ELIMIT-10 | A primitive int128 slot is vacant or initialized | Sixteen payload bytes plus one state byte |
| ELIMIT-11 | An internal reference/view slot is live | Thirty-two abstract descriptor bytes plus one state byte; not host sizeof |
| ELIMIT-12 | An entered frame has no user slots | Sixty-four abstract bookkeeping bytes |
| ELIMIT-13 | A root expression makes its first callable invocation | Depth becomes 1; root expression was not a callable frame |
| ELIMIT-14 | Active entered calls reach exactly 256 under the default | Depth permitted if work/storage also fit |
| ELIMIT-15 | Another call would raise active depth to 257 | Depth exhaustion before body entry |
| ELIMIT-16 | The same nesting is implemented as optimized tail calls | Same logical depth outcome |
| ELIMIT-17 | A body/branch is unselected | No execution/frame work for it; complete structure still validates |
| ELIMIT-18 | A bulk copy/read visits N logical cells | Charge each visited root/tag/field/element, not one native helper step |
| ELIMIT-19 | Zero-data cells are bulk visited | Charge their logical cells despite zero payload |
| ELIMIT-20 | A scalar helper or parser has no admitted body/cost recipe | Unsupported required execution, not a guessed one-step cost |
| ELIMIT-21 | A parser/intrinsic has a closed repeated/bulk action recipe | Charge its exact full expansion and live scratch/value storage |
| ELIMIT-22 | A physical cache avoids repeated interpreted work | No semantic work refund |
| ELIMIT-23 | Logical input regions share backing | Count each active region once plus actual descriptor slots; copied regions count separately |
| ELIMIT-24 | Result encoding buffer coexists with its source value | Count both; target bytes are not free temporary memory |
| ELIMIT-25 | Work, depth and state would all fail at one action | Work first, then depth, then state; perform no action |
| ELIMIT-26 | Work/state accounting would wrap a native counter | Exact checked accounting; never accept a wrapped charge |
| ELIMIT-27 | Default 1048576-step/16777216-byte/result-1048576 envelope is advertised | Provide at least it; private smaller implementation limits do not satisfy the floor |
| ELIMIT-28 | A larger/smaller explicit supported envelope is selected | Use that exact envelope and record it; same inputs/dependencies/target/envelope reproduce |
| ELIMIT-29 | Host cancellation/OOM occurs outside the logical model | Tool/resource failure, not a different value or false proof |
| ELIMIT-30 | A named constant declaration appears in a reached constant body | No runtime slot, declaration charge or initializer replay; subsequent value reads/materialization have their ordinary charges |
| ELIMIT-31 | An optional/union value ABI omits logical tag width or arm slot shapes | Incomplete constant-value/accounting contract; no guessed host layout or successful interpretation |

## Dependencies and independent consumption

| Case | Situation | Required result |
|---|---|---|
| EDEPS-01 | Ordinary binding needs only an imported signature | No constant body requirement unless execution is requested |
| EDEPS-02 | Required execution reaches a selected imported callable | Need its exact eligible constant body or admitted intrinsic |
| EDEPS-03 | That body is absent despite a valid runtime signature | Missing constant-body diagnostic, not runtime fallback |
| EDEPS-04 | An executed dependency has unknown/incompatible rule version | Reject before affected execution |
| EDEPS-05 | Code bodies, target or value ABI differ | They are different reproducibility inputs |
| EDEPS-06 | A frozen constant is imported | Validate its recorded type/value/dependencies; no producer rerun |
| EDEPS-07 | An unfrozen private layout is queried for a persistent constant | Reject unpromised stability or require explicit rebuild/invalidation dependency |
| EDEPS-08 | Published frozen layout and selected target ABI are available | Use them, not the host's layout |
| EDEPS-09 | Registration changes literal input transformation or intrinsic recipe | Treat as a changed evaluation dependency |
| EDEPS-10 | A same-named arbitrary parser/query/helper appears | No registration or intrinsic authority from its name |
| EDEPS-11 | Selected parser refuses/does not publish initialized value | Parse diagnostic without alternate-overload/runtime retry |
| EDEPS-12 | Selected constant value lacks a complete target encoding | Unsupported constant-result representation |
| EDEPS-13 | Decoded constant bytes violate nominal/scalar validity | Invalid value rejection before use |
| EDEPS-14 | Producer signature has unchanged runtime contract but body changes | New execution depends on changed code; old frozen values are not silently recomputed |
| EDEPS-15 | A compatible declared stable constant contract is replaced | Preserve its published constant promises |
| EDEPS-16 | An artifact contains unknown instruction/schema in unused selected-body data | Structural rejection, not executed-path exemption |
| EDEPS-17 | An implementation signer or optional deeper audit is present | Does not replace safe decoding, binding or value validation |
| EDEPS-18 | Physical constant-program/registration/value-ABI codec is unassigned | Explicit unsupported persisted requirement; logical floor allocates no wire meaning |
