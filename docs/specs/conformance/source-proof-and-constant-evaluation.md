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
| PSAFE-22 | A source count operand first performs ordinary nonzero unsigned negation | Development/Debug: prove or diagnose checked overflow before shift safety; Release: prove the wrapped result before count safety, without treating negation as a failure |
| PSAFE-23 | Floating division has an arbitrary initialized divisor including zero | Arithmetic itself is non-failing; IEEE normal value classes remain possible |
| PSAFE-24 | Getter/RHS/setter can fail during a scalar update | Numeric range safety alone does not prove the whole update's failure/effect behavior |
| PSAFE-25 | An operation's published precondition proves its checked operation safe | Consume the published domain contract without private-body inference |
| PSAFE-26 | Runtime arithmetic computes an integer result on a successful edge | Intersect result bounds with its type range, without retroactive safety proof |
| PSAFE-27 | A definitely failing runtime expression is written directly or through an exact local | Same warning/promotable-to-error classification; required constant contexts remain errors |
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
| EFORM-26 | Producer supplies malformed/invalid encoded scalar input | Verification rejects before affected use |
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
| EPATH-24 | ConstEval annotation is treated as ordinary-form totality or termination | Pure-form support is implied; neither unconditional inference is valid |
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

## Typed record-family direction and admission

The logical record families below are selected; exact schemas and assignments
remain open. These cases do not claim byte-level interoperability.

| Case | Situation | Required result |
|---|---|---|
| EREC-01 | An evaluation form is retained for a callable/accessor | Bind an exact-method typed clause; do not reduce the form to an Effects bit |
| EREC-02 | A bound require/ensure predicate is retained | Preserve a typed predicate graph and its exact formal/result/output subjects |
| EREC-03 | A predicate's finite input-state reads are retained | Carry them as a separate read-footprint dependency with phase, backing and invalidation obligations |
| EREC-04 | Required constant execution is persisted | Reference an ordinary checked MethodDef Body or exact admitted intrinsic, not a separate constant-only instruction language |
| EREC-05 | Constant execution depends on work/state limits or imported values | Retain typed resource and dependency records; do not infer support from an implementation's private limits |
| EREC-06 | A frozen value is persisted across target/layout boundaries | Preserve its complete TypeSpec, typed representation and target-layout dependencies; host object bytes do not suffice |
| EREC-07 | A producer certificate accompanies an otherwise incomplete record | Validate required consumer-visible records independently; the certificate cannot supply absent semantics |
| EREC-08 | A required schema, value codec or dependency form is unknown | Reject before affected use; do not fall back to runtime evaluation or a success-shaped value |
| EREC-09 | A reference/public-contract artifact is used for independent required ConstEval | Retain its checked Body or exact admitted intrinsic registration, selected form, inputs and dependencies; charge a checked Body directly, and retain an intrinsic-owned versioned resource recipe without duplicating it in the execution; reject a null/empty/throw stub |
| EREC-10 | A public Blittable struct is consumed independently | Retain the complete field/representation facts needed to validate its target commitment; a non-Blittable reference facade may omit private fields, but its implementation artifact may not |
| EREC-11 | A typed frozen value is materialized for a selected target | Derive ordinary field placement from the validated target `Layout`; retain explicit fixups only for genuine address relocations, and do not replay completed source evaluation |
| EREC-12 | A nested inline value is frozen and remapped | Preserve its recursive typed-value nodes, complete TypeSpecs, tags and field identities; reject opaque host bytes, unknown variants and cycles outside the admitted value domain |
| EREC-13 | A producer-published proof or constant-execution fact is semantically false within the represented artifact | Producer violates its truth obligation; a consumer is not required to detect or reject the structurally consumable false fact. An optional audit may detect it; arbitrary external provider and authored unsafe behavior remain trust contracts |
| EREC-14 | A frozen value has a child node referenced twice or participates in a cycle | Verification rejects the strict-tree violation; every non-root node must have exactly one parent |
| EREC-15 | Two `ConstantExecution` requests query the same TypeSpec and target | Keep each query's layout-dependency range owned by its execution occurrence; do not globally intern dependencies by subject and target |
| EREC-16 | A required constant request is executed and its result is retained | Keep one `ConstantExecution` per request, including its checked Body/intrinsic, substitution, transformed inputs, executed dependencies and actual resource envelope; charge a checked Body directly under common evaluator rules, and require the exact intrinsic registration to own the compact recipe for an opaque bulk intrinsic without an eligible checked Body; retain the materialized result as a separate frozen value |
| EREC-17 | One published predicate contains nested/short-circuit expression occurrences | Give each evaluated occurrence its own typed node row and preserve its ordered child-edge rows; every non-root occurrence has one parent, while immutable TypeSpec, member and witness identities may be shared |
| EREC-18 | A predicate observation depends on fewer roots than the callable allows | Keep the callable's allowed-root contract distinct from the graph's separate actual read-footprint record; the observation may use the full allowed set but may not omit a required root |
| EREC-19 | Read-footprint, layout-query, ordinary backing/retention and static-provider dependencies coexist | Keep these as separate typed families with separate owners; do not collapse them into a general cross-proposal dependency graph |
| EREC-20 | A `PredicateGraph` is packed or remapped with repeated expression forms and shared typed identities | Preserve distinct occurrence-node rows and ordered child-edge rows; remap immutable typed identities without sharing evaluated nodes or changing child order; use the approved 16-byte node and 8-byte operand candidates plus the 12-byte edge candidate specified by EREC-29; keep node/operand kind values and per-kind operand roles/arities open |
| EREC-21 | Two `PredicateGraph` observations belong to one callable but read different roots | Give each graph its own separate actual read-footprint record; do not merge observation footprints or store them on the MethodDef clause in place of the callable-level allowed-root contract |
| EREC-22 | Two `ConstantExecution` requests use different actual resource envelopes | Store each request's four ceilings inline (semantic steps, active frames, live abstract storage bytes and encoded result bytes); require the separate deterministic recipe for an opaque bulk intrinsic without an eligible checked Body, and do not substitute producer-reported work totals or a shared resource-profile identity |
| EREC-23 | A required constant request uses a bulk intrinsic whose work and scratch depend on validated inputs | Resolve the exact intrinsic registration's versioned compact deterministic resource recipe, validate its logical inputs and calculate exact semantic work plus peak incremental live scratch/value storage before execution; use common evaluator rules for input/result slots, entered frames and result encoding; reject malformed/unknown recipes and do not accept a per-execution duplicate, serialized action expansion or producer-reported total |
| EREC-24 | A predicate observation reads multiple roots and depends on multiple calls or witnesses | Preserve one graph-owned footprint with two flat typed ranges for the complete actual root set and direct call/witness dependencies; do not nest dependencies per root or duplicate graph/Contract phase/order identity |
| EREC-25 | A footprint's root or dependency range arrives out of canonical order or contains a duplicate | Require unsigned kind-tag order followed by the target's complete stable logical identity; compare root ordinals numerically for tags 2-4; resolve typed targets and compare family-specific semantic identity tuples component-by-component in the approved field order, using complete TypeSpec identities for nested type components; compare Constraints by owner declaration identity, formal ordinal, kind and canonical typed payload, InterfaceWitnesses by complete implementing/interface TypeSpecs, WitnessProjections by source witness/requested complete interface TypeSpec/rule version, and ImmutableData by kind/declaration/owner substitution; reject duplicate semantic identities and never sort by physical row references or raw DefId bits; do not serialize a separate sort-key field |
| EREC-26 | Two `ConstantExecution` requests use the same opaque bulk intrinsic with different transformed inputs | Resolve one recipe from the exact versioned intrinsic registration, charge each request from its own inputs and envelope, and do not duplicate or substitute the registration recipe |
| EREC-27 | A predicate directly calls one pure method whose own footprint has additional call/witness dependencies | Store only the predicate's direct call/witness dependencies, retain the complete actual root set, and recursively validate the callees' and witnesses' own records instead of copying their transitive closure |
| EREC-28 | Contract rows for several predicates are reordered during packing or remapping | Preserve each predicate's explicit zero-based, contiguous `u32` phase-local clause-owned declaration ordinal and phase; do not derive declaration order from physical row order; reject gaps and duplicates |
| EREC-29 | A PredicateGraph ordered child edge is serialized or remapped | Use the 12-byte candidate row `u32 parentNodeRef, u32 childOrdinal, u32 childNodeRef`; require nonzero standard typed row references and a zero-based contiguous child ordinal per parent; reject zero/out-of-range/high-byte-invalid references, duplicate or gapped ordinals, multiple parents, cross-graph edges, cycles and a root with an incoming edge |
| EREC-30 | A PredicateGraph is serialized with its root and actual read footprint | Use the 8-byte candidate row `u32 rootNodeRef, u32 readFootprintRef` with nonzero standard typed row references in `1..0xFF_FFFF` and reserved-zero high bytes; keep subject identities in typed node operands rather than a graph-level subject range; do not duplicate callable, phase or declaration order |
| EREC-31 | A PredicateReadFootprint is serialized with empty or populated root/dependency ranges | Use the 16-byte candidate row `u32 rootFirst, u32 rootCount, u32 dependencyFirst, u32 dependencyCount`; require `(0,0)` for an empty range and nonzero one-based in-bounds ranges with reserved-zero first-row high bytes otherwise; resolve each pair against its assigned typed table |
| EREC-32 | A complete logical read root is serialized | Use the 8-byte candidate row `u8 rootKind, u8 flags=0, u16 reserved=0, u32 rootIdentity`; accept only tags `1=receiver`, `2=input value`, `3=input state`, `4=logical output`, `5=ordinary result`, `6=immutable data`, `7=evaluation mode`; require zero identity for singleton kinds, valid zero-based parameter/destination ordinals for tags `2..4`, and a nonzero standard typed row reference to `ImmutableData` for tag `6`; validate ordinals against the attached MethodDef signature and output destinations; reject unknown tags, nonzero flags/reserved fields and mismatched identity forms |
| EREC-33 | A direct call or witness dependency is serialized | Use the 8-byte candidate row `u8 dependencyKind, u8 flags=0, u16 reserved=0, u32 dependencyRef`; accept only tags `1=MethodDef` direct call, `2=Constraint` open generic dispatch, `3=InterfaceWitness` closed witness, `4=WitnessProjection` projected witness; resolve a nonzero standard typed row reference in `1..0xFF_FFFF` with reserved-zero high byte against the table selected by the tag, following the `Import` binding for an imported expected MethodDef; reject unknown tags, nonzero flags/reserved fields and mismatched target kinds; keep only unique direct dependencies and recursively validate their records without copying transitive closure |
| EREC-34 | Root or dependency rows are repacked with different physical row references | Preserve the semantic-identity ordering and duplicate relation across remapping; resolve typed targets before comparison and do not let changed row numbers or raw DefId bits change the order |
| EREC-35 | Two PredicateReadFootprints contain equal or overlapping root/dependency sets | Give each footprint disjoint, exclusively owned ranges in the corresponding typed tables; do not share equal ranges, partial ranges or individual rows; duplicate semantic entries across distinct footprints are permitted and serialized separately, while duplicates within one range remain invalid |
| EREC-36 | A PredicateGraph node carries typed non-expression operands | Use the 16-byte candidate row `u32 nodeKind, u32 resultTypeSpec, u32 operandFirst, u32 operandCount` and 8-byte operand rows `u8 operandKind, u8 flags=0, u16 reserved=0, u32 operandRef`; require a nonzero typed result TypeSpec reference, standard empty/nonempty operand ranges, and nonzero typed operand references resolved by operand kind; keep expression children in ordered child-edge rows; reject nonzero flags/reserved fields and malformed references |
| EREC-37 | Two PredicateNodes use the same semantic operand | Give each node a disjoint, exclusively owned operand range and a separate operand row, even when the typed target identity is equal; allow the immutable target identity itself to be referenced by both rows |
| EREC-38 | PredicateGraph child-edge rows are emitted or remapped | Sort physical rows by unsigned `parentNodeRef`, then `childOrdinal`; re-sort after remapping; do not infer child or evaluation order from table position, which remains defined by `childOrdinal` |
| EREC-39 | PredicateGraph node rows are emitted or remapped | Require each graph to be referenced by exactly one MethodDef Contract clause; sort graph groups by the clause's resolved MethodDef identity (module semantic identity, owner TypeDef or module scope, declaration/file scope where applicable, name, complete MethodSig and generic formal domains/constraints), then phase tag and phase-local ordinal; for imports include the selected producer identity and export key; order each graph root-first in depth-first traversal by `childOrdinal`; re-sort after remapping; do not infer expression evaluation order from table position or serialize a separate sort-key field |
| EREC-40 | PredicateNode distinguishes expression structure from selected identities | Encode structural expression form in `nodeKind`, selected operation/member/method/witness identities in typed non-expression operand rows, and expression-valued subexpressions in child edges; do not encode selected operations as executable Body opcodes; keep exact tags, target schemas and per-kind roles/arities open |
| EREC-41 | PredicateNode represents the published predicate subset | Cover value/subject, access/projection, operation application, selected call, test/pattern, Boolean composition and construction; use distinct structural variants when evaluation behavior differs, including short-circuit versus eager composition; preserve selected identities in typed operands and expression-valued children in ordered edges; keep exact tags and per-kind operand roles/arities open |
| EREC-42 | Root/dependency or graph rows are canonically ordered after typed-reference remapping | Resolve target rows and compare family-specific semantic identity tuples component-by-component in the approved field order, using complete TypeSpec identities for nested type components; do not serialize a separate sort-key field; remapping changes references but not semantic ordering |
| EREC-43 | MethodDef identities collide in canonical root/dependency ordering | Include module, owner or module scope, declaration/file scope where applicable, name, complete MethodSig and generic formal domains/constraints; for imported expectations include selected producer identity and export key; do not distinguish identities by physical row references, raw DefId bits or Body bytes |
| EREC-44 | A read footprint depends on an immutable constant or region | Represent either target through the single typed `ImmutableData` identity family, distinguish a resolved constant declaration from a published immutable-region declaration, include complete owner substitution where applicable, and follow value/publication/load-lifetime facts through existing typed dependencies rather than copying them |
| EREC-45 | An immutable-data root is serialized or remapped | Use section `0x0041`'s 12-byte row `u8 kind, u8 flags=0, u16 reserved=0, u32 declarationDefId, u32 ownerTypeSpec`; closed tags are `1=ResolvedConstant` and `2=PublishedImmutableRegion`. The tag restricts the shared typed `DefId`; `ownerTypeSpec` is zero only when no owner substitution applies. Preserve identity across remapping, including exact value/witness identity for resolved constants and explicit publication evidence for immutable regions; do not inline value bytes or copy contract snapshots |
| EREC-46 | An ImmutableData row has malformed tags, reserved fields or references | Verification rejects unknown kind values, nonzero flags/reserved fields, invalid DefIds, wrong definition kinds or inconsistent owner substitutions; `StaticField` alone is not an immutable-region publication contract |
| EREC-47 | Constraint, witness or witness-projection dependencies are ordered or duplicate-checked after remapping | Compare Constraints by owner generic declaration identity, formal ordinal, kind and canonical typed payload identity; compare `InterfaceWitness` by complete implementing/interface TypeSpecs and `WitnessProjection` by source witness identity, requested complete interface TypeSpec and projection-rule version; resolve nested TypeSpecs and `AssociatedRelation` identities, use kind/payload tuples for constraints embedded in a MethodDef identity to keep the comparator finite, reject duplicates only for equal semantic tuples, and ensure row remapping does not change order |
| EREC-48 | A predicate tests an admitted initialization, type, union-case or tuple pattern | Encode pattern forms structurally rather than as opaque source data; preserve exact TypeSpec and union-case identities as typed operands and nested payload-pattern order as child edges; keep pattern `not`/`and`/`or` distinct from Boolean `!`/`&&`/`||`; assign graph-local zero-based capture ordinals in declaration order and resolve them only within their graph; preserve source evaluation and binding behavior, while leaving numeric tags and capture wire schemas open |
| EREC-49 | Pattern captures are referenced in one or more predicate graphs | Represent each capture in a typed `PredicateCapture` row referring to its owning graph and graph-local ordinal; `PredicateNodeOperand` capture references target that row, but semantic identity remains `(graph identity, ordinal)`; allow the same ordinal in distinct graphs without aliasing, reject cross-graph references, preserve identity across row remapping, and never use source spelling, physical row numbers or pattern paths as identity |
| EREC-50 | A predicate captures a value, checked reference or generic type argument | Preserve the exact value TypeSpec and ownership/presence facts; for checked-reference captures preserve the existing `ref<T>` referent, access authority and backing/provenance without adding indirection; for generic-type captures preserve the complete hidden TypeSpec and checked branch evidence; keep `ptr<T>` as a pointer-typed value, not a checked reference |
| EREC-51 | PredicateCapture rows are duplicated or remapped | Reject duplicate `(graph identity, capture ordinal)` identities; remap the graph and operand references without changing capture identity; keep the PredicateGraph row at its approved 8-byte shape and do not add a capture range |
| EREC-52 | A capture refers to typed facts already owned by another record | Reference existing typed records when they fully represent the capture's required TypeSpec, ownership/presence, access, backing/provenance or branch evidence; do not duplicate their semantic payloads; encode only a missing role-specific fact in the PredicateCapture row, and reject missing, conflicting or incomplete facts |
| EREC-53 | PredicateCapture rows are canonically ordered after remapping | Order by the owning graph's stable identity (MethodDef identity, phase tag and phase-local ordinal), then capture ordinal; re-sort after typed-reference remapping and never order by physical row references |
| EREC-54 | A capture requires a fact absent from existing typed records | Encode only that missing fact in role-selected PredicateCapture row fields; do not add a separate capture-payload record family or duplicate existing payloads; reject role-inapplicable, missing, conflicting or malformed fields |
| EREC-55 | A predicate contains a literal operand | Reference the root `FrozenValueNode` of the existing typed-value tree; preserve its complete TypeSpec and scalar or structured payload, use the canonical scalar Blob contract where applicable, and reject inline raw literal bytes, predicate-only literal encodings or executable-code fallback |
| EREC-56 | A predicate operand denotes a read root | Reference the matching root-entry row in the same graph's `PredicateReadFootprint`; reject missing or cross-graph entries, and compare resolved root kind/identity rather than physical row references |
| EREC-57 | A selected-call operand is also a read-footprint dependency | Reference the matching direct dependency-entry row in the same graph's footprint for its MethodDef, Constraint, InterfaceWitness or WitnessProjection target; reject missing or cross-graph dependencies and resolve identity semantically |
| EREC-58 | A selected call has a generic construction | Preserve the complete call-site substitution as a separate typed fact from the generic declaration/dependency identity; reject consumers that reconstruct substitutions from names, signatures alone or private bodies |
| EREC-59 | A predicate selects a field or property | Reference the selected `FieldDef` or `PropertyDef`; represent an evaluated getter separately as a selected call; derive field placement from validated `Layout`, not stored offsets or source names |
| EREC-60 | A selected field or property belongs to a constructed generic owner | Preserve the member declaration identity and complete owner TypeSpec substitution; validate the effective member type under that construction rather than treating the definition row as the constructed member |
| EREC-61 | A pattern selects a union case on a constructed generic owner | Preserve the `UnionCaseDef` identity and complete owner TypeSpec substitution; validate payload fields under that construction and reject name-only or declaration-only matching |
| EREC-62 | A required `ConstantExecution` request is serialized | Use section `0x0042`'s 80-byte row with one source selector, result type, exact target profile, four exclusively owned first/count ranges and four `u64` resource ceilings; source tags are `1=CheckedBody`, `2=IntrinsicRegistration`; validate the selected source, substitution, argument order and resource accounting before use |
| EREC-63 | A request contains nested execution results or a by-reference input | Use the 8-byte argument rows with tags `1=FrozenValueNode`, `2=LogicalInputView`, `3=VacantInitDestination`, `4=ConstantExecutionResult`; require zero source only for tag 3, exact by-value type/profile match and an acyclic nested-request graph; reference/view inputs require explicit logical backing and access evidence |
| EREC-64 | A persisted target-layout dependency is serialized or reused | Use section `0x0045`'s 20-byte row and query tags `1=SizeOf`, `2=AlignOf`, `3=OffsetOfTypePath`, `4=OffsetOfPlace`; retain complete subject TypeSpec, exact TargetProfile and ordered FieldDef path only where required; derive layout closure, store no computed result, and keep ownership on the specific execution or target-value binding |
| EREC-65 | A target-dependent value is already completed and frozen | Use section `0x0049`'s 16-byte TargetValueBinding with the frozen root, exact profile and exclusively owned layout-dependency range; mismatch rejects use unless a separately selected execution exists for the exact requested profile; the binding has no fallback recipe |
| EREC-66 | A target-bound record names a profile | Resolve the typed TargetProfile row in section `0x0048`; `(profileId=1, ruleRevision=1)` is `windows-x64-native/1`, while `portable-semantic` is only a broad target requirement; do not reuse a result under a different profile or layout dependency |
| EREC-67 | A logical view is used as a transformed constant input | Use `LogicalInputView` and ordered `LogicalViewPathEntry` records to identify frozen/immutable/nested-result backing, referent type, path/range and access at each reference indirection; reject host pointers, opaque region IDs, invalid selectors, missing bounds or lifetime evidence |
| EREC-68 | Explanatory labels name `EvaluationForms`, `Predicate`/`ReadFootprint`, `ConstantExecution` or `FrozenValue` families | Map labels to the existing clauses and typed records only; preserve physical names, IDs and ownership, and keep the checked execution request distinct from its frozen result |
