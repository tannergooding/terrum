# Core scalar checked-failure conformance

These cases apply to [scalar-failure-catalog/1 and zero-data-scalar-failures/1](../language/scalar-failures.md).
They require their stated scalar, construction, event, phase and execution
support. They do not admit additional numeric conversions, hardware fault
translation, a universal Exception base or missing physical handlers.

Finite evidence uses independent mathematical arithmetic/conversion checks,
exact supplied Core identities and the previously accepted local/captured
event model. Such evidence is not Terrum source execution or a full numeric,
allocation/OOM, async, native or physical-codec implementation.

## Core identity and payload

| Case | Input or condition | Required outcome |
|---|---|---|
| SFID-01 | The three selected Core declarations | Exact public nongeneric zero-data nominal struct identities |
| SFID-02 | Same-spelled types from another provider | Do not supply selected Core identities |
| SFID-03 | Each payload's Exception capability and Copy | Ordinary value eligibility/copy without event duplication |
| SFID-04 | Same bits but different failure types | Distinct nominal catch subjects |
| SFID-05 | Transparent aliases of the three selected types | Preserve normalized identity and matching |
| SFID-06 | Generic, class, field-bearing or kind-code replacement | Does not satisfy the selected zero-data struct contract |
| SFID-07 | Exact public zero-parameter constructors | Complete values with no authored predicates/effects |
| SFID-08 | Static replacement or optional/required operand, name, message or allocator constructor input | Not the selected construction member |
| SFID-09 | Void, null or vacant slot in place of a zero-data failure | Not a complete eligible payload |
| SFID-10 | Payload shape used to infer Pure/ConstEval/NoThrow/thread/native capability | No inferred additional contract |
| SFID-11 | Another eligible authored exception with richer fields | Remains supported under its own ordinary contract |
| SFID-12 | Actual Core declarations, registration and source constructor/catch binding | Document-only: supplied nominal facts do not prove a compiler or physical library record |

## Mathematical failure mapping

| Case | Input or condition | Required outcome |
|---|---|---|
| SFMAP-01 | Every int8/uint8 addition pair | Exact same-type result or OverflowException on unrepresentability |
| SFMAP-02 | Every int8/uint8 subtraction pair | Exact same-type result or OverflowException |
| SFMAP-03 | Every int8/uint8 multiplication pair | Exact same-type result or OverflowException |
| SFMAP-04 | Signed minima and unsigned nonzero negation at every fixed/native owner | OverflowException; zero negation succeeds |
| SFMAP-05 | Increment/decrement at each integer boundary | OverflowException only beyond that same type's range |
| SFMAP-06 | Every int8/uint8 division pair | Zero divisor maps to DivideByZeroException; other unrepresentable quotient maps to OverflowException |
| SFMAP-07 | Signed minimum divided by -1 at every signed width | OverflowException, not DivideByZeroException |
| SFMAP-08 | Every int8/uint8 remainder pair | Zero divisor maps to DivideByZeroException; finite remainder uses exact truncating quotient |
| SFMAP-09 | Signed minimum remainder -1 at every signed width | Successful zero with no OverflowException |
| SFMAP-10 | Signed minimum Abs versus unsigned maximum Abs | OverflowException versus unchanged successful unsigned value |
| SFMAP-11 | Each shift form with a negative source count | ArgumentOutOfRangeException, not conversion overflow |
| SFMAP-12 | Each shift form with count equal to or above data width | ArgumentOutOfRangeException before physical count conversion |
| SFMAP-13 | Too-wide uint64/uint128 count and a 32/64-bit physical control operand | Do not wrap into a valid count or change payload identity |
| SFMAP-14 | 128-bit data and all nonnegative int8 counts | Count accepted without trying to represent 128 in int8 |
| SFMAP-15 | Valid left shift discards high bits | Normal result; no arithmetic-overflow payload |
| SFMAP-16 | All selected fixed/native integer conversion source/destination boundary samples | Exact value or OverflowException at the destination range |
| SFMAP-17 | Each selected float32/64-to-integer pair near the truncation/range boundary | Truncate toward zero first; only an unrepresentable integer result fails |
| SFMAP-18 | NaN and either infinity in each selected checked floating-to-integer pair | OverflowException, not a NaN-specific or argument payload |
| SFMAP-19 | Already admitted numeric-to-char16 values including surrogate units | Range overflow maps to OverflowException; a representable surrogate is not rejected as a scalar |
| SFMAP-20 | Supplied IEEE arithmetic infinity/NaN/zero/subnormal outcomes | No catalog payload from those floating value classes |
| SFMAP-21 | Remainder-only versus combined quotient/remainder request | Fusion cannot add quotient overflow to a successful remainder |
| SFMAP-22 | Actual arbitrary-width scalar/software arithmetic, all floating formats and source lowering | Document-only: finite exact oracles do not prove every numeric provider or conversion pair |

## Construction and event responsibility

| Case | Input or condition | Required outcome |
|---|---|---|
| SFCR-01 | Successful checked operation | No failure construction or event |
| SFCR-02 | Mapped failed value check under checked policy | Construct exactly the selected complete payload once |
| SFCR-03 | First operand fails with an already owned event, including at a terminal value-check site | Preserve that event and its actual channel; skip later operands and mapped construction |
| SFCR-04 | Later operand fails after earlier effects | Preserve actual failure and effects without executing the numeric operation |
| SFCR-05 | Event preparation fails before mapped publication | Preserve actual preparation failure; no fabricated mapped event or normal result |
| SFCR-06 | Native/control helper name differs from source operation | Source failure origin/phase remains the failed source operation |
| SFCR-07 | Two same-category failed operations | Different fresh events despite equal zero-data payloads |
| SFCR-08 | Copy catch of a standard payload | Copies zero-data value, not provenance/cause responsibility |
| SFCR-09 | Borrowed catch, lexical Capture, later consuming throw | Preserve original event while descriptor extent remains the handler's |
| SFCR-10 | Fresh throw of an ordinary copied standard payload | New authored-throw event, not captured propagation |
| SFCR-11 | Scalar failure escapes a still-responsible handler | Earlier event becomes its retained cause |
| SFCR-12 | Cleanup replaces a pending scalar failure | Preserve earlier event/cause and exactly-once settlement |
| SFCR-13 | Terminal/fail-fast selected instead of checked transport | No promised local payload/handler or cleanup after termination |
| SFCR-14 | Raw hardware/native fault reaches a runtime boundary | No automatic catalog translation |
| SFCR-15 | Real event preparation, rooting, OOM/replacement and physical publication | Document-only: finite supplied responsibility is not resource/fault containment |
| SFCR-16 | Real runtime catch/capture/async delivery and retirement programs | Document-only: reused event model is not executable Terrum transport |

## State, evaluation and phases

| Case | Input or condition | Required outcome |
|---|---|---|
| SFST-01 | Counted operands on a failed binary operation | Evaluate once in source order; no failure-driven reevaluation |
| SFST-02 | Overflow while assigning an existing scalar destination | No numeric result replacement |
| SFST-03 | Overflow while initializing vacant storage | No fabricated zero/default result |
| SFST-04 | Argument writes the destination before numeric failure | Actual write survives; no transaction rollback |
| SFST-05 | Explicit move of a Copy scalar operand before failure | Source stays vacant |
| SFST-06 | Checked arithmetic portion of a compound/accessor update fails | No setter/update call; prior getter effects remain |
| SFST-07 | Supplied failed atomic arithmetic read/order effects | No write/release/update result is invented by event propagation |
| SFST-08 | RequireException override encloses an overflowing predicate | Operand OverflowException survives; no requirement fallback construction |
| SFST-09 | Explicit require<OverflowException> constructs the standard type | Fresh require-site origin, not invented arithmetic origin |
| SFST-10 | Before-establishment versus established async site | Synchronous failure versus failed completion, even inline |
| SFST-11 | Producer creation, synchronous/async advance or close | Preserve each actual failure channel and source site |
| SFST-12 | Literal/static proof failure versus unhandled required-constant numeric failure | Diagnostic/no publication, not runtime fallback or fabricated call result |
| SFST-13 | Handled numeric failure inside a supported constant/NoThrow program | Use actual handler/complete effects; catalog presence alone is no guarantee |
| SFST-14 | Nominal operator, extra conversion pair, atomic API or policy variant | Does not acquire this contract solely from a name |
| SFST-15 | Ordinary and native-width exact domains use a sound check proof | Retain source/type/mapping facts; no host-width or body guess |
| SFST-16 | Actual source evaluation, accessor/atomic lowering and suspension/effect proofs | Document-only: finite staged facts do not establish arbitrary program behavior |

## Retained contracts and admission

| Case | Input or condition | Required outcome |
|---|---|---|
| SFAD-01 | Missing exact mapped Core identity | Decline before affected operand evaluation |
| SFAD-02 | Missing Exception/Copy/value/constructor contract actually retained | Decline; zero bits do not fill the missing layer |
| SFAD-03 | Missing complete operand/result domain, source site, target width, no-result state or phase | Decline, not a different helper-origin event |
| SFAD-04 | Missing actual policy/event/cleanup/cause/transport dependency | Decline the affected use |
| SFAD-05 | Failure category changed with equal payload ABI | Does not satisfy the retained mapping |
| SFAD-06 | Original shift count replaced with a lossy physical count | Reject the changed meaning and failure type |
| SFAD-07 | Changed native target width or signedness | Cannot silently reuse the prior domain |
| SFAD-08 | Missing checked policy/handler support with runtimePolicy=1 or a reserved band | Decline; recognition is not execution support |
| SFAD-09 | Exact declaration aliases and relocation with complete retained origin | Preserve selected identities, not source-name lookup |
| SFAD-10 | Stronger or unrelated valid provider additions | Do not create a universal replacement break |
| SFAD-11 | Trusted selected record access | No preliminary all-Body scan to discover the catalog |
| SFAD-12 | Missing required constant opt-in/program/value support | No runtime fallback, published constant or implicit constructor authorization |
| SFAD-13 | Missing actual event/runtime/helper dependency versus removed compiler-only use | Check the complete retained closure, not all historical imports |
| SFAD-14 | NoThrow/Pure expectation justified only by the zero-data payload shape | Decline or require the actual complete proof/guarantee |
| SFAD-15 | Logical scalar-failure catalog is offered as physical registration | No physical IDs or codecs follow; complete selected encoding and support remain independently required |
| SFAD-16 | Actual independent declaration/value/operation-site/handler/cause/phase codecs | Document-only: supplied logical layers are not source-free conformance |

## Evidence boundary

There are 82 cases. SFID-12, SFMAP-22, SFCR-15, SFCR-16, SFST-16 and
SFAD-16 are document-only. The remaining 76 have explicit finite mathematical,
nominal, ownership/state, supplied-phase or admission evidence. Broader
failure catalogs and physical/compiler/runtime completion remain separate.
