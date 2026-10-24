# Published predicate and pure-evaluation conformance cases

These cases apply the [published-predicate and effect contract](../language/published-predicates-and-effects.md),
[contract phases](../language-contracts.md#3-contracts-bracket-semantic-body-completion),
[memory model](../memory-model.md) and
[constant-execution domain](../language/proof-and-constant-evaluation.md#constant-execution-domain).
They are specification obligations, not a claim that a compiler executed them.

Unless testing admission, assume valid complete types, accessible checked
declarations, valid input/backing, supported rule/target/value contracts and
sufficient resources. A selected pure form is checked over its declared
valid-input domain, not certified by one successful evaluation.
Examples named Square use the canonical range precondition and mode guard.
An ordinary result, initialized-null value and vacant output remain distinct.
Invalid producer-published metadata violates producer obligations. Consumers
reject metadata that is structurally unsafe, malformed or unsupported before
affected use; they are not required to detect a structurally consumable but
semantically false assertion. Optional semantic audits may examine it.
Diagnostic wording and internal check order are not prescribed.

## Purity and valid-input domains

| Case | Situation | Required result |
|---|---|---|
| PURE-01 | A selected pure call receives identical complete logical inputs | Results obey the same declared value/representation contract, without observable effects |
| PURE-02 | A pure instance getter reads this._count | Permit with its receiver-state dependency; no observer modifier |
| PURE-03 | Explicit and implicit references read the same supplied state | Same purity rule; implicit adaptation is not a special privilege |
| PURE-04 | A write-reference parameter is read but never externally written | Write permission alone does not make that form impure |
| PURE-05 | Two input references alias and both are read | Permit; no mandatory NoAlias declaration |
| PURE-06 | A selected form reads a mutable ambient static | Reject purity, even in a thread-confined execution profile |
| PURE-07 | A selected form reads checked immutable published data | Permit with actual identity/publication/lifetime dependencies |
| PURE-08 | A readonly static handle reaches mutable storage | Readonly slot/RVA placement does not make that ambient storage a pure input |
| PURE-09 | A selected form mutates private scalar locals in a loop | Permit with ordinary validity, checks and cleanup |
| PURE-10 | A closed pure-region proof verifies a helper writes only region-private storage | Permit that selected execution; retain complete confinement/effect evidence |
| PURE-11 | A helper writing a live caller receiver is annotated Pure for arbitrary callers | Reject; one temporary-backed caller does not establish the declaration guarantee |
| PURE-12 | A callable constructs an ordinary result or vacant init destination | Logical result construction, subject to validity and publication |
| PURE-13 | A call allocates a fresh externally distinguishable managed identity | Reject selected-form purity |
| PURE-14 | A call creates a native resource identity or registers an external callback | Reject selected-form purity |
| PURE-15 | A callee-private owner cleanup performs observable I/O | Reject; private storage does not hide destructor effects |
| PURE-16 | A call writes pre-existing caller state disjoint from every read input | Reject purity; disjointness does not erase the write |
| PURE-17 | A trace branch performs unguarded I/O on an admitted input | Reject the pure/ConstEval claim; a successful no-trace request is insufficient |
| PURE-18 | A selected form reads clocks, culture, environment or input streams | Reject ambient varying dependencies |
| PURE-19 | A selected form performs volatile or atomic observations | Reject that effect; readonly and acquire ordering are insufficient |
| PURE-20 | A selected form synchronizes, blocks or suspends | Reject selected-form purity |
| PURE-21 | A valid-input selected form can escape checked arithmetic failure | Reject the purity claim unless its domain/checks establish absence |
| PURE-22 | A caller violates the declared precondition | Preserve its ordinary check/failure; Pure's exception exclusion applies only on its valid domain |
| PURE-23 | A pure recursion/loop does not terminate for some admitted input | No termination guarantee; do not eliminate it solely because it is pure |
| PURE-24 | Required constant work exhausts its envelope in otherwise pure computation | Required resource diagnostic, not parser false or an alternative value |
| PURE-25 | A private write uses a copied handle into pre-existing external storage | External write remains impure; copying the descriptor does not privatize backing |
| PURE-26 | All escaping failures are caught but an identity/I/O effect remains | Catching failures alone does not establish purity |
| PURE-27 | A mode-unselected branch contains a known external effect | Permit only when the checked pure domain excludes its execution |
| PURE-28 | A supposedly unused branch has an unknown instruction or invalid source form | Reject structural validation before selection |
| PURE-29 | A selected form invokes fail-fast or process termination | Observable termination effect is impure; nonreturning does not mean pure |
| PURE-30 | A computation merely has DoesNotReturn or has no failure on one observed input | Neither supplies a purity, input-dependency or totality guarantee |

## Modes, propagation and authorization

| Case | Situation | Required result |
|---|---|---|
| PMODE-01 | An ordinary invocation has no selected evaluation form | IsPureEval=false, IsConstEval=false |
| PMODE-02 | An ordinary invocation names a Pure/ConstEval-annotated member | Annotation alone still leaves both flags false |
| PMODE-03 | An authorized pure form receives nonconstant runtime inputs | IsPureEval=true, IsConstEval=false |
| PMODE-04 | Terrum evaluates a required constant request | IsPureEval=true, IsConstEval=true |
| PMODE-05 | An authorized backend selects constant evaluation | Both flags true; backend folding is not Terrum source opt-in |
| PMODE-06 | Producer publishes an impossible false/true evaluation-mode state | Verification rejects before affected use |
| PMODE-07 | Optimization removes an ordinary computation without changing its form | No automatic flag change |
| PMODE-08 | Input arguments happen to be compile-time constants | No implicit form selection or call admission from their constancy alone |
| PMODE-09 | A nested eligible call reads the mode queries | Inherit the selected activation's mode |
| PMODE-10 | An eligible helper tries to revert to ordinary execution during constant work | No silent reset or runtime fallback |
| PMODE-11 | An ordinary callable evaluates a declaration predicate | Clause selects pure true/false, then ordinary body resumes false/false |
| PMODE-12 | A declaration predicate runs inside required constant evaluation | Clause and its eligible callees observe true/true |
| PMODE-13 | Square has trace=true in ordinary mode | Retain logging and its ordinary failures/order |
| PMODE-14 | Square has trace=true in selected pure or constant mode | Guard omits logging; retain range requirement and result 9 for input 3 |
| PMODE-15 | I/O is guarded only by !IsConstEval on an admitted true-trace input | It remains in true/false pure mode; reject unconditional pure-form support |
| PMODE-16 | IsConstEval selects a different constant-compatible pure algorithm | Both forms satisfy the documented result/ownership contract |
| PMODE-17 | Mode-dependent implementation silently changes a promised result | Reject the claimed contract; mode selection is not an undeclared result change |
| PMODE-18 | A same-named user RuntimeHelpers property returns a Boolean | No intrinsic identity, special mode or evaluation permission |
| PMODE-19 | The resolved Core mode getter is reached | Immutable semantic query, no mutable static read or runtime initialization group |
| PMODE-20 | A query is assigned or rebound as a runtime field | Reject; it is a readonly intrinsic property |
| PMODE-21 | Pure authorizes a safe VN/CSE/folding transformation | Select its checked form only with other memory/effect/validity requirements |
| PMODE-22 | Closed whole-program proof establishes equivalent pure execution without an annotation | Permit that sound transformation; retain its actual dependencies |
| PMODE-23 | An unannotated alternate branch avoids ordinary logging | Not author permission or equivalence proof to discard logging |
| PMODE-24 | A Pure-only callable is reached by required Terrum evaluation | Need ConstEval opt-in or an admitted intrinsic; pure name/body is insufficient |
| PMODE-25 | A ConstEval callable reaches a Pure-only helper during required work | Diagnose missing helper constant admission unless separately admitted |
| PMODE-26 | A reached eligible call lacks its required body/value recipe | Diagnose; no native call, host evaluator or runtime fallback |
| PMODE-27 | A backend proves an admitted Pure-only result and can materialize it | Optional backend folding is allowed without Terrum interpreter opt-in |
| PMODE-28 | A selected pure call skips effectful argument evaluation or caller cleanup | Reject transformation; callee form authorization does not cover those effects |

## Read roots, immutable data and shared state

The dependency fixture has separate `count` and `other` storage regions.
Reading count records that region's state and retained backing; an unrelated
other-region write does not change count. Two distinct handle slots can both
reach the same count region. Abstract revisions are proof state, not runtime
instrumentation.

| Case | Situation | Required result |
|---|---|---|
| PREAD-01 | A by-value scalar input is copied, then its caller variable changes | The staged scalar dependency retains its original value |
| PREAD-02 | A by-value handle input is copied, then observed referent state changes | Descriptor copy is not a referent snapshot; invalidate affected observation |
| PREAD-03 | A receiver descriptor is rebound after its captured call stage | Call record keeps the captured receiver/backing, not a later variable reread |
| PREAD-04 | An input view traverses nested element handles | Include actual observed element backing, not just container/descriptor bytes |
| PREAD-05 | A getter reads count but a checked call writes only other | Preserve count observation if backing and all other dependencies survive |
| PREAD-06 | A getter reads count and a call may write count | Invalidate observation |
| PREAD-07 | A complete root record conservatively covers more state than is read | Sound but potentially less reusable; no omitted relevant dependency |
| PREAD-08 | A finer field/range record proves noninterference | Permit precision only with checked complete extents and no hidden imported-layout assumption |
| PREAD-09 | An input region read is absent from a claimed pure record | Reject incomplete contract, even if the current examples never expose it |
| PREAD-10 | An ordinary call has no published effect record | Conservative unknown, not an empty read/write set |
| PREAD-11 | A public Count getter reads private representation | Publish receiver-state dependency without requiring caller access to that private field |
| PREAD-12 | An imported getter has no body but a complete checked pure/read contract | After structural checks and support admission, predicate use may rely on the declared contract; required execution additionally needs a body/intrinsic |
| PREAD-13 | Two observations differ in selected form/rules/code/target semantics | Do not reuse from input equality alone |
| PREAD-14 | A read-only RVA region has proven immutable extent and publication | Admit its immutable dependency, retaining artifact/load lifetime |
| PREAD-15 | Data has RVA placement but writable aliases or an unproved immutable extent | Do not infer immutable pure data |
| PREAD-16 | Immutable static data has an outstanding effectful runtime initializer | Discharge or retain the entry demand at its required point; pure body does not erase it |
| PREAD-17 | Required evaluation reads immutable compiler-published data | Use recorded values; do not run or complete a runtime initialization group |
| PREAD-18 | A load-scoped immutable region is retired | Its observation/backing dependency cannot survive retirement |
| PREAD-19 | Explicitly shared mutable input is read through a readonly descriptor | Require its actual protocol and stability extent; readonly alone is insufficient |
| PREAD-20 | Two atomic reads return equal values | Atomicity/equality does not prove an unchanged reusable observation |
| PREAD-21 | Caller retains a checked nonconcurrent/exclusion extent while a pure callee reads | Permit ordinary reads only under that actual access/stability contract |
| PREAD-22 | Callee acquires/releases synchronization to manufacture a stable input | Those effects are not made pure by the resulting snapshot |
| PREAD-23 | Body releases a guard before a backing-dependent postcondition | Do not extend the guard; guarantee needs surviving access/stability |
| PREAD-24 | A live copied scalar snapshot is returned from protected state | It can survive release without replaying the protected read |
| PREAD-25 | Mode query is used by a selected algorithm | Bind the predefined immutable mode root, not an ambient mutable field |
| PREAD-26 | Input/result backing contracts are trimmed while a dependency is retained | Reject weakened required semantics |

## Alias and invalidation boundaries

| Case | Situation | Required result |
|---|---|---|
| PALIAS-01 | Observe(count,memory0) repeats without affecting effects | Reuse may be allowed; no physical memory-version counter is required |
| PALIAS-02 | A relevant mutation occurs before Observe(count,memory1) | Different logical input; no unconditional reuse |
| PALIAS-03 | The same reference address is used after referent mutation | Address equality is not complete input equality |
| PALIAS-04 | Distinct root addresses reach shared nested backing | Do not infer disjoint read/write regions |
| PALIAS-05 | Ordinary ref/implicit-ref/write-ref inputs may overlap | Aliasing is the default; no may-alias marker |
| PALIAS-06 | Both aliased inputs are only read by the selected form | No disjointness proof is needed for purity alone |
| PALIAS-07 | A required proof needs separation already established by caller analysis | No additional source attribute is mandatory |
| PALIAS-08 | Required separation is declared but not established by the caller | Diagnose that obligation; do not trust different root names |
| PALIAS-09 | A checked disjointness contract covers only descriptor slots | It cannot establish separation of shared element/backing regions |
| PALIAS-10 | Checked separation covers exact relevant extents and active scope | Use that fact only within those bounds |
| PALIAS-11 | Disjointness is used to grant write, ownership or concurrency authority | Reject; separation supplies none of those capabilities |
| PALIAS-12 | A no-alias fact is used after replacement or retirement of its subject | Invalidate or reestablish; do not retain stale extent identities |
| PALIAS-13 | A same-thread callback can write observed input state | Include its effect and invalidate potentially affected observations |
| PALIAS-14 | An opaque call can affect exposed/captured mutable locals | Reset their dependent facts conservatively |
| PALIAS-15 | A nonexposed scalar copy is independent of an opaque call | Preserve that scalar's fact under the existing floor |
| PALIAS-16 | Checked writes are disjoint from all retained mutable read regions | Preserve those observations, subject to other effect/lifetime bounds |
| PALIAS-17 | A move/cleanup invalidates retained backing without writing its old bytes | Invalidate; byte stability is not live backing |
| PALIAS-18 | A source loop writes a relevant region | Reset affected relationships at the established loop boundary |
| PALIAS-19 | Two floating inputs are numerically equal positive/negative zero | Do not merge representation-sensitive inputs solely by numeric equality |
| PALIAS-20 | A copied NaN or explicit NaN construction has a bit-preserving contract | Preserve its promised sign/payload bits |
| PALIAS-21 | A pure arithmetic operation produces some unspecified NaN | Purity does not freeze runtime sign/payload; use its actual observation contract |
| PALIAS-22 | Constant arithmetic NaN is used as a proof of runtime payload bits | Not established from constant canonicalization |
| PALIAS-23 | A repeated pure NaN result is compared with itself using floating == | Equality remains false; purity does not make floating equality reflexive |
| PALIAS-24 | Equal-valued decimal cohorts are copied or encoded | Preserve required coefficient/exponent observations |

## NoAlias parameter contracts

Each annotated endpoint is a declared parameter after call mapping. Its
footprint contains checked mutable data extents and logical availability
state identities; proven immutable data is separately identified. The
required relation compares that endpoint with the receiver and all other
input/output endpoints, not with alternate paths inside the same endpoint.

| Case | Situation | Required result |
|---|---|---|
| PNOALIAS-01 | Primary synchronous parameter has resolved no-argument NoAlias | Admit the checked parameter schema, not an exclusive reference type |
| PNOALIAS-02 | Same-named user NoAlias attribute appears | No recognized separation contract |
| PNOALIAS-03 | NoAlias supplies arguments or pair selectors | Reject the unsupported argument schema |
| PNOALIAS-04 | Inline and selector placements both apply NoAlias to one effective formal | Reject duplicate nonrepeatable placement |
| PNOALIAS-05 | NoAlias targets a callable, receiver, type, field or result | Reject wrong target; do not silently choose a parameter |
| PNOALIAS-06 | param(other):NoAlias resolves the same formal as inline placement | Same bound requirement under ordinary target rules |
| PNOALIAS-07 | Secondary partial authors or changes a primary NoAlias requirement | Reject contract-changing secondary authorship |
| PNOALIAS-08 | Named arguments arrive in a different order from formal ordinals | Bind exact mapped formal identities; preserve source evaluation order |
| PNOALIAS-09 | Several parameters are annotated | Require separation for each pair with at least one annotated endpoint |
| PNOALIAS-10 | Two ordinary unannotated read inputs overlap | Aliasing remains permitted; pure reads do not require NoAlias |
| PNOALIAS-11 | Those overlapping mutable reads now have one annotated endpoint | Diagnose required separation despite readonly access/no writes |
| PNOALIAS-12 | Separate handle/view slots reach overlapping mutable backing | Different root addresses are insufficient |
| PNOALIAS-13 | Two accesses within one allocation have checked disjoint field/range extents | Permit the established separation without requiring different allocations |
| PNOALIAS-14 | Required backing/extent proof is unknown | Diagnose; no pointer-inequality guess or inserted runtime alias fallback |
| PNOALIAS-15 | Endpoints share the same proven immutable published table | Exempt immutable extent; retain publication/lifetime obligations |
| PNOALIAS-16 | A readonly/RVA region still has mutable aliases | Not the immutable exemption |
| PNOALIAS-17 | An annotated by-value scalar/unit or generic substitution has no external mutable region | Legal but vacuous; do not claim its caller variable is disjoint |
| PNOALIAS-18 | A by-value handle/aggregate carries observed mutable state | Include actual referent/backing, not just copied parameter-slot bytes |
| PNOALIAS-19 | An annotated implicit-ref argument adapts an existing place | Use the same actual region/obligations as explicit reference formation |
| PNOALIAS-20 | A materialized value has a fresh slot but copies shared mutable handles | Fresh slot does not make nested backing disjoint |
| PNOALIAS-21 | Materialization creates a genuinely independent handle-free value region | May prove separation from actual freshness and surviving dependencies |
| PNOALIAS-22 | An otherwise invalid borrow is copied to make a NoAlias call | Reject the borrow; no copy-based escape from access/lifetime rules |
| PNOALIAS-23 | One init destination is annotated and aliases another destination | Diagnose required separation even when unannotated compatible overlap could work |
| PNOALIAS-24 | Aliased init-ref-unit destinations have zero payload bytes | Their accessed logical availability states still overlap |
| PNOALIAS-25 | An empty by-value view has no mutable payload access | No data-region conflict is invented; backing/lifetime is not erased |
| PNOALIAS-26 | One normal params formal has internally overlapping element handles | No pairwise separation within that one endpoint is implied |
| PNOALIAS-27 | Expanded params creates fresh slots whose handles overlap another endpoint | Compare actual backing; expansion does not discharge NoAlias |
| PNOALIAS-28 | An annotated ordinary default is a published constant | Use the actual staged value/regions and selected default identity |
| PNOALIAS-29 | An opaque call lacks a complete footprint but a parameter is NoAlias | No empty-effect inference; required separation needs actual supported bounds |
| PNOALIAS-30 | A NoAlias write-reference performs a pre-existing external write | It remains observable/impure |
| PNOALIAS-31 | NoAlias is used to grant missing write/ownership/pin/lifetime authority | Reject; separation grants none of those capabilities |
| PNOALIAS-32 | NoAlias is used to bypass an explicitly shared access protocol | Reject; no synchronization, exclusion or sharing permission |
| PNOALIAS-33 | Caller retains an unrelated alias outside the other supplied endpoints | NoAlias alone does not forbid that alias or acquire an exclusive loan |
| PNOALIAS-34 | A returned/captured view outlives invocation | Retain its backing contract, not an extended NoAlias guarantee |
| PNOALIAS-35 | A contract/finally/parameter cleanup accesses a relevant region | Include it in the whole-invocation requirement |
| PNOALIAS-36 | Callee introduces crossed backing and accesses it under a stale separation fact | Reject the false promise/proof; preserve actual changed provenance |
| PNOALIAS-37 | A locally mutable reference formal is rebound to another input | Its new descriptor cannot inherit the staged argument's NoAlias fact |
| PNOALIAS-38 | A parameter is forwarded to another NoAlias callable | Map and discharge that callee's exact endpoints/extents; no transferable type capability |
| PNOALIAS-39 | Interface implementation adds a NoAlias requirement absent from the retained interface | Reject restricted caller domain |
| PNOALIAS-40 | A NoAlias entry requirement is relaxed/removed | Widen only while preserving the old domain's separation and other guarantees |
| PNOALIAS-41 | Variant/default replacement broadens accessed regions or weakens the call extent | Reject incompatible retained contract |
| PNOALIAS-42 | Qualified-self adaptation changes qualification but retains overlapping backing | No new separation from the qualified type name; check actual adapted roots |
| PNOALIAS-43 | Otherwise equal declarations differ only in NoAlias | No new overload identity or preference |
| PNOALIAS-44 | Unique selected candidate has an unestablished NoAlias obligation | Diagnose that call; do not retry another candidate/channel |
| PNOALIAS-45 | Opaque import has complete checked NoAlias/effect records but no body | Caller checking may use records; required constant execution separately needs opt-in/program |
| PNOALIAS-46 | Optional attributes, unused Param flags or Effects bits pretend to encode NoAlias | Reject unsupported mandatory carrier; no new wire allocation |
| PNOALIAS-47 | Lambda/callback target lacks a supported NoAlias effect shape | Do not add a caller requirement that target consumers cannot retain |
| PNOALIAS-48 | Async/producer use needs access after ordinary invocation completion | Require a separately admitted complete extent; no implicit task-lifetime extension |
| PNOALIAS-49 | The annotated input has a checked empty mutable footprint; another endpoint's effects are unknown | Separation is vacuous, not proof of purity or the other obligations |
| PNOALIAS-50 | An ordinary mutable formal is rebound and subsequent accesses use its new source provenance | Rebinding alone is permitted; no transfer of the old endpoint's separation fact |
| PNOALIAS-51 | A signature indexer parameter has NoAlias and participates in both accessors | Preserve that exact parameter requirement in each applicable accessor |
| PNOALIAS-52 | A setter-local param(value) selector and an inline placement target its same formal | Resolve the actual setter formal and apply effective multiplicity; no property-wide guessing |
| PNOALIAS-53 | Later argument staging changes backing before invocation | Prove separation from the final staged/prepared state, not a stale pre-staging fact |
| PNOALIAS-54 | Exceptional completion runs finally/parameter cleanup after a body failure | Separation extent includes those accesses until invocation completion |

## Initialization result positions

The output fixture binds formal destinations `p` and `q` to logical places.
Bindings can be distinct or identical. A construction plan lists the actual
once-only initializations, separately from the formal live/vacant promises.
An identical place can satisfy two live promises after one compatible
initialization, but cannot be both live and vacant.

| Case | Situation | Required result |
|---|---|---|
| PINIT-01 | A pure callable receives a vacant init-ref destination and publishes a valid value | Permit logical result construction |
| PINIT-02 | An ordinary return plus two initialization values are produced | Treat all three and their output states as the logical result |
| PINIT-03 | Boolean-success default is true/live and false/vacant | Preserve the per-invocation discriminator relation |
| PINIT-04 | Explicit normal false outcome promises a live initialized-null result | Preserve live-null, not the default vacant state |
| PINIT-05 | A false-vacant result is materialized as default/null for reuse | Reject fabricated value/publication |
| PINIT-06 | A destination is already live at invocation | Reject the init loan; no implicit destruction to vacate it |
| PINIT-07 | Old bytes exist in a vacant destination | No value read or validity inferred from those bytes |
| PINIT-08 | The callee rebinds an initialization descriptor or treats vacancy as an initialized loan | Reject; retain actual destination/access rules |
| PINIT-09 | Two distinct destinations are initialized once with compatible outputs | Permit with their separate validity/backing/cleanup |
| PINIT-10 | p and q name one vacant place, both promise live, and the plan initializes it once compatibly | Overlap alone does not violate the two state promises |
| PINIT-11 | The same overlapping place is initialized twice through p and q | Reject second initialization of a live place |
| PINIT-12 | p and q name one place but promise live/vacant | Reject contradictory availability |
| PINIT-13 | p and q name one place and promise different live values | Reject incompatible result promises |
| PINIT-14 | p and q overlap and both remain vacant on refusal | No initialized value is manufactured; all actual access obligations still hold |
| PINIT-15 | An output overlaps a live input or retained input backing | Must satisfy actual vacancy/access/alias obligations; init is not a blanket no-alias grant |
| PINIT-16 | Reuse computes once but two calls need separate Copy output destinations | Initialize each requested destination under its ordinary copy contract |
| PINIT-17 | A result view points into its own output value | Rematerialize/adapt each destination's backing; do not copy another invocation's view blindly |
| PINIT-18 | A uniquely owned/noncopyable result is duplicated by CSE | Reject without the actual permitted transfer/construction contract |
| PINIT-19 | Preparing a reusable hidden output retires an old owner | Retain caller cleanup/order; selected callee purity does not erase it |
| PINIT-20 | A supporting-scope hidden output site is skipped or repeated | Preserve its established vacancy/replacement/lifetime rules |
| PINIT-21 | A result copies a writable handle from an input, then writes through it | Pre-existing backing mutation remains impure |
| PINIT-22 | A partial construction or failed postcondition occurs | Ordinary cleanup/failure rules, no new output transaction or rollback |
| PINIT-23 | A task/producer later publishes an output after suspension | Not ordinary synchronous pure-result completion |
| PINIT-24 | Fresh output initialization allocates an externally distinguishable identity | Init mode alone does not make identity allocation pure |

## Published predicates and phase guarantees

| Case | Situation | Required result |
|---|---|---|
| PPRED-01 | A declaration predicate uses valid scalar operations and available values | Admit the typed checked expression |
| PPRED-02 | A predicate short-circuits before a partial operation | Preserve evaluation order and guard-established safety |
| PPRED-03 | A predicate uses initialization/presence/admitted type tests | Apply structural rules; never read vacancy to discover its state |
| PPRED-04 | A predicate calls an accessible checked pure Count getter | Admit with complete receiver/input-state dependencies |
| PPRED-05 | A readonly getter lacks purity/read contracts | Readonly is not sufficient predicate-call admission |
| PPRED-06 | A getter's failure behavior is bounded but it reads mutable ambient state | A failure bound alone is insufficient for Pure admission |
| PPRED-07 | A public clause directly references a private field/helper | Reject hidden caller requirements at effective accessibility |
| PPRED-08 | A public pure getter hides private representation behind its contract | Permit the visible abstraction; do not export private field access |
| PPRED-09 | A clause uses a checked helper/operator/conversion with complete roots | Admit its selected identity, valid call domain and actual dependencies |
| PPRED-10 | A clause constructs a fresh inline value or initialization result | Require checked pure construction and publication; no arbitrary object allocation |
| PPRED-11 | A clause tries I/O, mutation, suspension or a move-pattern | Outside the published subset |
| PPRED-12 | An indirect callback is currently known but has no supported effect shape | No purity admission from target coincidence |
| PPRED-13 | A clause uses arbitrary old(expr) or graph-entry snapshots | Unsupported; no implicit historical value capture |
| PPRED-14 | Pattern bindings appear in separate precondition/postcondition phases | Retain isolated scopes; no exported phase-local caller name |
| PPRED-15 | A later clause/phase cleanup can invalidate an earlier needed guarantee | Diagnose unless it survives or is soundly reestablished without replay |
| PPRED-16 | A compiler reruns a completed predicate to restore an invalidated fact | Reject replay and its changed effects/order |
| PPRED-17 | Body finally changes returned-reference backing | Postcondition reads surviving destination/backing after cleanup, not a replayed return |
| PPRED-18 | A moved/retired parameter is referenced as a live postcondition input | Reject unavailable source; observe a valid transferred destination instead |
| PPRED-19 | A postcondition relates the result to an unchanged by-value formal | Export the staged input and saved result identities |
| PPRED-20 | A rebound by-value formal is exported as the caller's unchanged input | Reject false subject mapping; no entry snapshot is invented |
| PPRED-21 | A clause's selected pure helper receives an invalid precondition | Preserve its ordinary domain failure; no blanket no-fault inference |
| PPRED-22 | A pure Boolean operand is false in an enclosing require | Enclosing requirement fails normally; computing false is not impurity |
| PPRED-23 | A successful body is followed by a failed postcondition | Propagate without rerunning completed body cleanup or rolling back effects |
| PPRED-24 | A known-true check has potentially failing operands or caller cleanup | Do not erase their required behavior from truth alone |
| PPRED-25 | Ordinary body require uses general executable Boolean code | Not automatically restricted to the published subset or switched to a new mode |
| PPRED-26 | A supported scalar published clause is consumed at a successful boundary | Map it into the portable scalar/structural floor without private body inference |
| PPRED-27 | An opaque pure predicate is treated as a numeric intrinsic/theorem solver | No unregistered numeric summary or extra proof-floor guarantee |
| PPRED-28 | An exported predicate is only a name or true result from one run | Require the bound typed expression graph and complete dependencies |

## Schemas, dispatch and constant compatibility

| Case | Situation | Required result |
|---|---|---|
| PVER-01 | Same-named user Pure or ConstEval attribute is applied | No compiler-recognized capability |
| PVER-02 | Pure/ConstEval has arguments, duplicate placement or an invalid target | Reject its resolved schema violation |
| PVER-03 | Pure and ConstEval are both applied once to a primary callable | Permit distinct schemas; pure-form support is redundant |
| PVER-04 | A secondary partial declaration changes the primary's effect contract | Reject contract-changing secondary authorship |
| PVER-05 | An accessor uses Pure or the enclosing property uses get:Pure | Bind the same getter identity under ordinary target/multiplicity rules |
| PVER-06 | A type/parameter/result/lambda placement substitutes for a supported callable target | Reject unsupported placement; do not silently retarget |
| PVER-07 | An interface/witness requirement promises pure forms and read bounds | Implementation/default/variant must preserve them |
| PVER-08 | A replacement weakens outputs, backing, effects, domain or mode semantics | Reject incompatible replacement |
| PVER-09 | Devirtualization/pinning reveals stronger private implementation facts | Do not strengthen source-visible contracts from those facts |
| PVER-10 | A retained qualified-self adaptation forwards a pure callable | Preserve selected identity, form, capture, output and read dependencies |
| PVER-11 | Independent consumer sees unknown mandatory predicate/effect rules | Reject before affected use, not optional-attribute fallback |
| PVER-12 | Retired Effects bit `0x01` is treated as a Pure/ConstEval carrier | Reject the malformed clause; the bit has no assigned interpretation |
| PVER-13 | Required constant result is a Copy Auto aggregate with no Blittable promise | Reject emitted result admission despite eligible computation |
| PVER-14 | Blittable result lacks Copy/frozen validity or a constant codec | Blittable alone is insufficient |
| PVER-15 | Private evaluator slots/views lack emitted-value layout/codec admission | Permit internal use if their actual execution/access domain is admitted |
| PVER-16 | Blittable/Copy/codec-admitted aggregate or optional/union is emitted | Preserve complete live/active state and nominal value identity |
| PVER-17 | Typed fields are encoded for a target with different endian/padding | Materialize under target layout; do not persist host object/padding bytes |
| PVER-18 | Native-sized constant is evaluated for a selected width | Check that target width/range, not host nint |
| PVER-19 | Pointer constant captures evaluator/process address bits | Reject absent a separately admitted symbolic relocation recipe |
| PVER-20 | Documented constant result changes incompatibly from x+1 to x+2 | Breaking behavior change even if both forms remain pure |
| PVER-21 | An arithmetic NaN payload changes within documented unspecified latitude | Not a new compatibility violation; preserve stronger copy/bit promises |
| PVER-22 | Evaluation body, immutable data, value ABI or form rules change | Revalidate actual dependencies; same purity flag does not preserve old answers |
| PVER-23 | Consumer relies on ordinary-form effects being erased by a Pure marker | Keep distinct ordinary and selected-form records |
| PVER-24 | Producer or signer identity is treated as semantic proof of all bodies | No waiver of bounded structural/identity/contract admission |
