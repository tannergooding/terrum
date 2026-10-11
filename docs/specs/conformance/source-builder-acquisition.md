# Parameterless builder acquisition conformance cases

These cases apply to [builder-acquisition/1](../language/construction-handlers.md#parameterless-builder-acquisition)
and [eager-construction/1](../language/construction-handlers.md#eager-capture-and-acquisition).
Assume otherwise valid recipes, source captures and the other required
construction operations. Outcomes are logical source/contract obligations,
not execution of a compiler/factory or physical metadata admission.

## Exact nominal factory

| Case | Situation | Required result |
|---|---|---|
| AQSIG-01 | Complete recipe builder has the checked Core IBuilderFactory self witness | Select its exact static TryCreate requirement/member |
| AQSIG-02 | Builder only has a same-named method or constructor | No nominal factory role; reject affected shorthand |
| AQSIG-03 | Builder implements a same-spelled user IBuilderFactory | No Core acquisition capability |
| AQSIG-04 | Factory takes an ordinary input argument, including one with a default | Wrong parameterless requirement; no compiler-supplied context/default |
| AQSIG-05 | Factory uses ordinary returned builder, ref or non-init output | Reject wrong required output signature |
| AQSIG-06 | Factory is an instance member | Reject wrong static role; no pre-existing receiver |
| AQSIG-07 | Factory output differs from exact complete recipe type | Reject inconsistent self/output relation; no qualification or representation repair |
| AQSIG-08 | Recipe instantiation fails the factory's declared requirements | Reject invalid generic registration/use; private concrete body cannot repair it |
| AQSIG-09 | Required factory/witness is inaccessible or missing in an independent consumer | Reject affected use before execution |
| AQSIG-10 | Output type lacks actual local storage/transfer or drain-operation permission | Reject unsupported construction; no implicit boxing or backing-write upgrade |
| AQSIG-11 | Factory has unchecked true/vacant, false/live or invalid-output behavior | Reject invalid nominal implementation contract |
| AQSIG-12 | Type recipe is present but remaining completion/acceptance protocols are missing | Factory alone does not admit a complete construction |

## Capture order, invocation and outputs

| Case | Situation | Required result |
|---|---|---|
| AQEXEC-01 | Text/collection captures all succeed | Execute factory after the complete source-order captures, before that builder's drain |
| AQEXEC-02 | A capture fails before acquisition | Factory is unreached; later captures do not run, completed captures clean normally |
| AQEXEC-03 | Captured spread source is lazy | No spread enumeration/capacity preflight before factory; enumerate only at reached drain |
| AQEXEC-04 | Empty source construction is evaluated | Invoke selected factory and completion despite having no captures |
| AQEXEC-05 | Factory returns true with valid live builder | Transfer output obligations to source local, then proceed to drain |
| AQEXEC-06 | Factory returns false | Output vacant, captures remain evaluated, plain construction raises ordinary require failure |
| AQEXEC-07 | Factory throws before or during partial construction | Original exception propagates, output vacant, private acquired state cleans once |
| AQEXEC-08 | Factory forms builder but a post-body check/authored exit action faults | Retire unpublished output, remain vacant and preserve actual failure/causal chain |
| AQEXEC-09 | An explicit caller observes false from the factory | Preserve Boolean false/vacant output unless caller code requires success |
| AQEXEC-10 | A later drain operation fails after successful acquisition | Current source builder cleans its owned state; factory does not reclaim it |
| AQEXEC-11 | Completion consumes successfully acquired builder | Source local stays vacant after transfer; no repeated factory cleanup |
| AQEXEC-12 | Source site repeats in one supporting-scope activation | Fresh successful acquisition creates a new value lifetime with ordinary reusable-local rules |
| AQEXEC-13 | Factory has ordinary static initialization/allocation/other effects | Preserve their declared invocation point and dependencies; no effect-free assumption |
| AQEXEC-14 | Call-side TextTarget forms eager typed content | No inferred target-builder factory just to store captures |
| AQEXEC-15 | Explicit factory call names an already live init output destination | Reject before factory entry; no implicit destruction/replacement or default value |

## Ownership, backing and runtime context

| Case | Situation | Required result |
|---|---|---|
| AQOWN-01 | Factory acquires one or more owning fields and later refuses | Clean acquired subset in its ordinary order; no uninitialized cleanup |
| AQOWN-02 | Successful factory transfers fields to output but retains private scratch | Output owns transferred subset, factory cleans residual subset |
| AQOWN-03 | Exceptional exit follows private partial output formation | Clean actual private/partial subsets once, never restore source capture owners |
| AQOWN-04 | Published builder borrows retired factory-local storage | Reject missing surviving backing contract |
| AQOWN-05 | Factory explicitly uses a checked allocator/provider dependency | Permitted under its declared authority/effects/ownership, not implicit language selection |
| AQOWN-06 | Type-only registration tries to capture caller locals/allocator/destination | No closure/context argument; require ordinary explicit API |
| AQOWN-07 | Source attempts a generated capacity/formatter/enumerator preflight | No extra factory input or undeclared pass; preserve once-only eager capture/drain |
| AQOWN-08 | Explicit context-taking API is called with ordinary eager interpolation argument | Normal argument staging/capture and API contracts, not new source shorthand |
| AQOWN-09 | Generated owner cleanup can throw or suspend | Invalid required cleanup contract; no suppression or leak fallback |
| AQOWN-10 | Mutable local binding would be used to mutate readonly backing | Local mutability does not supply missing complete-type/backing write authority |

## Independent records and evolution

| Case | Situation | Required result |
|---|---|---|
| AQVER-01 | Consumer lacks builder-acquisition/1 or exact retained factory record | Reject affected use before execution |
| AQVER-02 | Producer changes factory to require a runtime context/capacity argument | Changed source construction contract, not implicit new argument synthesis |
| AQVER-03 | Producer weakens output validity, exception cleanup or backing relationship | Incompatible retained obligations |
| AQVER-04 | Producer changes private allocation strategy within every published obligation | No incompatibility solely from private strategy; observable identity/effects remain checked |
| AQVER-05 | Source factory operation is optimized or eliminated | Preserve declared effects, refusal, identity, initialization, lifetime and cleanup |
| AQVER-06 | Parameterless factory is treated as Pure, ConstEval or NoThrow | No such implication; selected evaluation/effect contracts govern |
| AQVER-07 | Linking remaps factory references | Preserve exact complete witness/member/self/output and dependency identities |
| AQVER-08 | Factory role is placed into an unassigned metadata carrier/opcode | No physical allocation admitted by this logical approval |
