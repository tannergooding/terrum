# Numeric selection and bounds-failure conformance

These cases apply to [numeric-selection-members/1 and invalid-bounds-failure/1](../language/numeric-selection.md).
They retain ordinary source-order call staging, explicit requirement
construction and local/captured event responsibility. They select no
additional generic interfaces, NaN signaling/status policy, physical member
registration or implementation support.

Finite evidence uses exact integers, supplied target facts, exact decoded
binary values and admitted decimal coefficient/exponent representations.
Construction and ownership evidence reuses the accepted nominal, requirement
and captured-event models. This is not a Terrum compiler, arbitrary numeric
provider, resource/OOM fixture or independent physical codec.

## Exact members and binding

| Case | Input or condition | Required outcome |
|---|---|---|
| NSID-01 | All 20 numeric owning identities and three selected members | Exactly 60 public static nongeneric member signatures |
| NSID-02 | Min/Max and Clamp formals/results | Required same-type by-value inputs named left/right or value/min/max, with same-type result |
| NSID-03 | Contextual integer literals at the owning type's boundaries | Use that exact owner, not a fallback numeric type |
| NSID-04 | An already typed different scalar argument | No implicit promotion, narrowing or signedness conversion |
| NSID-05 | Expected result has another numeric identity | Does not retarget the selected owner or result |
| NSID-06 | Transparent owner aliases versus equal-width fixed/native types | Alias identity is preserved; native and fixed identities remain distinct |
| NSID-07 | Supplied 32/64-bit native target contexts | Retain the actual native width, not host width |
| NSID-08 | The six nonnumeric language scalars | No selected Min/Max/Clamp membership |
| NSID-09 | Optional, enum, wrapper, SIMD or numeric-looking user type | No structural membership from names or representation |
| NSID-10 | Ref/init/default/params/receiver/generic/nonpublic signature substitution | Does not satisfy the selected ordinary member |
| NSID-11 | Generic call without an actually declared member witness | No hierarchy, body inference or synthesized witness |
| NSID-12 | Actual source lookup, literal binding, registration and witnesses | Document-only: supplied signatures do not execute a frontend or physical library record |

## Integer selection

| Case | Input or condition | Required outcome |
|---|---|---|
| NSIN-01 | Every int8/uint8 Min pair | Exact lesser input value with no overflow |
| NSIN-02 | Every int8/uint8 Max pair | Exact greater input value with no overflow |
| NSIN-03 | Wider fixed/native endpoints, signed minima and unsigned maxima | Same-type extrema without a magnitude or subtraction intermediate |
| NSIN-04 | Equal integer inputs, including zero | That same representable value |
| NSIN-05 | All valid int8/uint8 bound pairs and piecewise boundary partitions | Clamp returns the original value or the selected supplied bound |
| NSIN-06 | Value strictly below or above valid bounds | Normal min/max-bound result, not an argument failure |
| NSIN-07 | Value inside or equal to a valid endpoint | Return that supplied value |
| NSIN-08 | Equal integer bounds and values on either side | Valid relationship; successful selection |
| NSIN-09 | Whole-domain bounds at every fixed/native owner | Every sampled endpoint/interior value is returned unchanged |
| NSIN-10 | Corresponding 32/64-bit native boundary cases | Distinct exact domains; no host-width substitution |
| NSIN-11 | Normal Min/Max/Clamp values at the signed minimum | No checked-Abs, quotient or arithmetic-overflow failure |
| NSIN-12 | Actual arbitrary-width integer providers and source lowering | Document-only: exact finite oracles do not implement every provider |

## Floating values and representation ties

| Case | Input or condition | Required outcome |
|---|---|---|
| NSFL-01 | Quiet/signaling NaN in either or both Min/Max positions in every format | Quiet NaN, not a numeric-key or number-only selection; canonical positive zero payload in admitted constant execution |
| NSFL-02 | Unequal finite values, signs, quanta and subnormals | Use same-format numeric ordering and the selected input representation, not raw bits or rescaling |
| NSFL-03 | Opposite-sign zero inputs in either argument order | Min selects negative zero and Max positive zero |
| NSFL-04 | Same-sign binary zeros or equal nonzero binary values | Preserve the common numeric representation |
| NSFL-05 | Equal or opposite infinities and finite/infinite pairs | Correct same-format numeric extreme |
| NSFL-06 | Supplied boundary encodings for all five binary formats | Exact decoded classes/values remain in their own formats |
| NSFL-07 | Equal positive decimal values with different quantum exponents | Min selects the smaller exponent and Max the larger |
| NSFL-08 | Equal negative decimal values with different quantum exponents | Min selects the larger exponent and Max the smaller |
| NSFL-09 | Equal positive decimal zeros with different quantum exponents | Apply the positive-sign exponent tie-break |
| NSFL-10 | Equal negative decimal zeros with different quantum exponents | Apply the negative-sign exponent tie-break |
| NSFL-11 | Opposite-sign decimal zeros with different quanta | Sign preference wins before quantum comparison |
| NSFL-12 | Equal decimal inputs and an attempted first-input or third-cohort rule | Select the prescribed supplied representation; no rescaling or key normalization |
| NSFL-13 | Reversed arguments and canonical non-NaN representation triples | Order-independent selection, idempotence, associativity and absorption |
| NSFL-14 | Legal decimal coefficient/exponent boundaries | Retain the selected quantum without cross-format rounding |
| NSFL-15 | Proposed unrestricted totalOrder or numeric-key Min/Max | Does not replace NaN propagation or signed-zero observations |
| NSFL-16 | Valid floating Clamp with NaN, subnormal, infinite, zero or decimal input | Quiet NaN on the NaN path; in-range non-NaN input keeps its representation |
| NSFL-17 | Different permitted runtime quiet-NaN sign/payload results | No bit-copy, ambient status, Pure or NoThrow guarantee; constant selection uses its separate canonical recipe |
| NSFL-18 | Actual floating providers, quieting and distinct noncanonical encodings | Document-only: finite value/quantum facts do not prove all format or encoding behavior |

## Ordered bounds and the Core payload

| Case | Input or condition | Required outcome |
|---|---|---|
| NSBF-01 | Core InvalidBoundsException declaration and constructor | Exact public nongeneric zero-data Exception/Copy struct with public zero-parameter instance construction |
| NSBF-02 | InvalidBoundsException versus ArgumentOutOfRangeException | Distinct nominal siblings and catch subjects |
| NSBF-03 | Static, nonpublic, optional/required-input or effectful replacement constructor | Does not supply the selected Core construction |
| NSBF-04 | Transparent payload alias or another same-spelled provider | Alias preserves identity; another provider does not |
| NSBF-05 | Every reversed int8/uint8 bound pair | Fail the exact published min <= max precondition |
| NSBF-06 | Reversed finite or infinite floating bounds | Bounds failure before value selection |
| NSBF-07 | NaN in either bound, including with NaN value | Invalid bounds, not successful NaN selection |
| NSBF-08 | Equal bounds, either zero-sign order, equal decimal values or infinities | Valid numeric relationship, independent of representation order |
| NSBF-09 | Negated min > max or totalOrder used as the bound predicate | Not the selected predicate; neither may admit an unordered NaN pair |
| NSBF-10 | Value outside valid bounds | Normal Clamp result, not ArgumentOutOfRangeException |
| NSBF-11 | Unrelated requirement, comparison or min/max parameter name | No automatic InvalidBoundsException selection |
| NSBF-12 | Actual Core source declaration and zero-input constructor binding | Document-only: supplied nominal/construction facts are not source registration |

## Construction and event responsibility

| Case | Input or condition | Required outcome |
|---|---|---|
| NSCR-01 | Successful Min/Max or valid Clamp | No failure constructor or event |
| NSCR-02 | Invalid bounds under admitted checked propagation | Construct once and publish one fresh event owned by the Clamp precondition |
| NSCR-03 | Callable or first argument fails, including at a terminal precondition site | Keep the actual event/channel; skip remaining arguments and bounds construction |
| NSCR-04 | Later argument fails after earlier effects or staged invalid bounds | Preserve that failure/effects; do not invoke the member precondition |
| NSCR-05 | Actual preparation failure before bounds-event publication | Propagate that failure; no fabricated InvalidBoundsException event or result |
| NSCR-06 | Two invalid-bound calls with equal zero-data payloads | Distinct fresh events |
| NSCR-07 | Copy catch and subsequent throw of a payload copy | Copy no event responsibility; authored throw creates a new event |
| NSCR-08 | Active rethrow of the bounds failure | Preserve its event, origin and causes |
| NSCR-09 | Borrowed catch, lexical Capture and later consuming throw | Preserve the event; retain the descriptor's original lexical extent |
| NSCR-10 | Bounds failure escapes a still-responsible handler | Preserve the earlier event as a cause |
| NSCR-11 | Cleanup replaces the pending bounds failure | Preserve the cause closure and exactly-once retirement |
| NSCR-12 | Admitted terminal precondition policy | No local event/catch/cleanup promise after termination |
| NSCR-13 | Real constructor preparation, resources, rooting or OOM/replacement | Document-only: zero data does not prove allocation-free or failure-free transport |
| NSCR-14 | Executable runtime handlers, capture and delivery programs | Document-only: reused finite responsibility is not an actual runtime implementation |
| NSCR-15 | The bound comparison itself fails under its admitted effect contract | Keep that actual event/channel; no false-check bounds payload, result or terminal-policy reinterpretation |

## Staging, result state and phases

| Case | Input or condition | Required outcome |
|---|---|---|
| NSST-01 | Counted callable and positional argument expressions | Evaluate once in written source order before invocation checks |
| NSST-02 | Named arguments written in a different order from formals | Stage in written order, then map the staged values |
| NSST-03 | Invalid bounds after all ordinary argument staging | Keep every completed argument effect; no predicate-driven reevaluation |
| NSST-04 | Earlier by-value input followed by mutation of its source place | Use the staged snapshot, not the later place value |
| NSST-05 | First Min/Max input is NaN or already determines the extreme | Still evaluate the later argument normally |
| NSST-06 | Invalid bounds while assigning initialized storage | Publish no failed numeric replacement |
| NSST-07 | Invalid bounds while initializing vacant storage | Publish no fabricated zero/default result |
| NSST-08 | An argument writes the destination before bounds failure | That actual write remains; no transaction rollback |
| NSST-09 | Explicit move of a Copy scalar before later argument or bounds failure | Source stays vacant |
| NSST-10 | Getter stages a value and the Clamp precondition fails | Keep getter effects; do not reach a result-dependent setter |
| NSST-11 | Synchronous, before-establishment or established inline async context | Preserve the actual synchronous/failed-completion boundary and precondition origin |
| NSST-12 | Producer creation and synchronous/async advance or close | Preserve each actual escaping channel |
| NSST-13 | Admitted constant call fails, or an actual handler recovers | No runtime fallback or failed-call result; an authored recovery is a separate result |
| NSST-14 | Actual arbitrary source staging, flow, suspension and effect proofs | Document-only: supplied stages do not prove a complete frontend or scheduler |

## Retained contracts and admission

| Case | Input or condition | Required outcome |
|---|---|---|
| NSAD-01 | Missing exact owner/member/formal/result/domain/target support | Decline before affected evaluation |
| NSAD-02 | Missing numeric, NaN, zero, subnormal or decimal-tie rule | Decline; matching machine compare/select names do not fill it |
| NSAD-03 | Missing Clamp predicate, exact payload/constructor or requirement support | Decline rather than choose a fallback failure |
| NSAD-04 | Missing source origin, phase, policy, result state or actual event/transport dependency | Decline before affected evaluation |
| NSAD-05 | Changed bound predicate, result rule, failure identity or source owner | Does not satisfy the retained contract |
| NSAD-06 | Changed scalar identity, signedness or native width | Cannot silently reuse equal-layout support |
| NSAD-07 | Reserved policy band, runtimePolicy=1 or an unrelated physical feature | Recognition is not member/precondition execution support |
| NSAD-08 | Normalized aliases and helper relocation with unchanged source contract | Preserve exact member/payload and semantic precondition origin |
| NSAD-09 | Stronger guarantees or unrelated valid provider additions | Do not break a complete unchanged expectation |
| NSAD-10 | Trusted selected-record access | Visit the retained closure, not unrelated Bodies |
| NSAD-11 | Required constant use without opt-in/program/value/choice/cost/resource support | No implicit authorization or runtime fallback |
| NSAD-12 | Pure/NoThrow inferred solely from integer selection or zero-data payloads | Require the actual complete guarantee/proof |
| NSAD-13 | Registry, prior corpora, historical tools and accepted evidence | Preserve byte-exact inputs; allocate no new wire IDs or codecs |
| NSAD-14 | Independent member/value/precondition/program/event/handler/cause codecs | Document-only: logical supplied facts are not source-free physical conformance |

## Evidence boundary

There are 97 cases. NSID-12, NSIN-12, NSFL-18, NSBF-12, NSCR-13,
NSCR-14, NSST-14 and NSAD-14 are document-only. The remaining 89 have
explicit finite signature, numeric representation, requirement, event/state,
supplied-phase or admission evidence.

The decimal tie-break fixes the selected representation and quantum, not
every encoding of an indistinguishable representation. Full floating
signaling/status, generic witnesses, real runtime resources and physical
publication remain separate.
