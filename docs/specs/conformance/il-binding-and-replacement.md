# Binding and replacement conformance

These cases apply [binding compatibility](../representation/binding-and-replacement.md),
[complete types](../language/types-and-normalization.md) and
[requirements and admission](../representation/requirements-and-admission.md).
Named modules, definitions, predicates and proof results are schematic resolved
facts. A supplied checked implication is not proof of an arbitrary predicate;
a supplied supported signature/layout is not an execution or physical ABI claim.
Independent row fragments assess their actual shapes, not a complete linker.

## 1. Selected definitions and expectations

| Case | Situation | Required result |
|---|---|---|
| IBID-01 | One exact producer/key selects a declaration of the expected kind with a satisfying contract | Binding succeeds, subject to the complete admission gates |
| IBID-02 | Same key and shape are offered by a different Module identity | Do not substitute that producer |
| IBID-03 | ASCII and UTF-16 String rows decode to the same scalar identity/key | Equal decoded text selects the same identity |
| IBID-04 | Case folding or Unicode normalization would make two different keys equal | Do not fold/normalize; the exact selected key must exist |
| IBID-05 | One selected producer/key has two export targets, or two producer revisions remain selected | Reject ambiguity even if both shapes match |
| IBID-06 | Two distinct keys export one public local definition | Preserve one resolved definition identity through both aliases |
| IBID-07 | Export row instead names an imported expectation or a private declaration | Reject; aliases do not enable re-export or access |
| IBID-08 | Aliased keys formerly selected one retained nominal identity but replacement splits them | Reject the broken retained equality relationship |
| IBID-09 | Two consumers retain broad and NoThrow expectations for one key and provider is NoThrow | Check both independently; both can succeed |
| IBID-10 | Provider satisfies only one retained expectation | Reject the other edge; do not pick the weaker expectation |
| IBID-11 | One expected local declaration is mapped to two different selected definitions | Reject conflicting resolution, not shape-based coalescing |
| IBID-12 | Module, definition and String rows are reordered with complete reference remapping | Preserve selected identities and comparison results |
| IBID-13 | A same-named implementation exists but the selected export key is missing | Reject unresolved import; do not redo lookup/overload selection |
| IBID-14 | Provider adds an unrequested export without changing retained obligations | Do not reject merely for the extra export |
| IBID-15 | Exported member has an inaccessible or different nominal owner | Reject the owner/access mismatch |

## 2. Complete signature and representation

| Case | Situation | Required result |
|---|---|---|
| IBTY-01 | Complete primitive result/parameter identities and modes match after row remapping | Exact signature comparison succeeds; execution support remains separate |
| IBTY-02 | Equal-width signed/unsigned or character/integer types replace one another | Reject distinct scalar identities |
| IBTY-03 | Provider omits an ordinary void parameter because it has no data bytes | Reject changed logical arity/ordinal |
| IBTY-04 | A receiver is added/removed or a type-scoped static method replaces an instance method | Reject the changed callable shape |
| IBTY-05 | Receiver or reference access is strengthened from readonly to write | Reject the unadapted signature/authority change |
| IBTY-06 | Outer ref access is confused with the stored inner handle's qualification | Reject; complete inner and outer types remain distinct |
| IBTY-07 | Pin qualification/dependency differs | Reject; equal address bits do not satisfy pin |
| IBTY-08 | Optional layer or qualification placement differs | Reject changed complete type |
| IBTY-09 | Box, inline, pointer or unique owning representation differs | Reject; equal payload size is not type equality |
| IBTY-10 | Corresponding method type formals have different names/row owners but matching kind/ordinal terms | Map the selected formal correspondence; compare normalized signatures |
| IBTY-11 | Callable constraints are relaxed under established old-domain implication while signatures/witness guarantees remain | Permitted directed satisfaction |
| IBTY-12 | Provider adds a formation/witness constraint not implied by the expectation | Reject a narrower caller domain |
| IBTY-13 | Constant-formal kind, domain, nominal wrapper or canonical value changes | Reject; not ordinary constraint relaxation |
| IBTY-14 | Closed nominal constructor or ordered complete arguments differ | Reject even if substituted layouts coincide |
| IBTY-15 | Property's selected getter changes private name/row but preserves its referenced accessor contract | Compare through the property-selected correspondence, not get_Name text |
| IBTY-16 | Property getter changes result, receiver, parameter mode or caller safety | Reject the complete accessor mismatch |
| IBTY-17 | A producer claims fixed layout/Blittable/ABI validity without checking its actual target implementation | Supplied matching numbers do not prove the physical commitment |
| IBTY-18 | Selected producer target or runtime policy mismatches the retained ModuleRef/ABI requirement | Reject; no numeric or more-capable-host roll-forward |

## 3. Directed bounds and predicates

| Case | Situation | Required result |
|---|---|---|
| IBPR-01 | Expected callable may throw; provider adds NoThrow with other promises preserved | Compatible effect strengthening |
| IBPR-02 | Expected NoThrow callable is replaced by one without that promise | Reject |
| IBPR-03 | Expected caller-unsafe requirement is removed without changing other promises | Compatible domain widening |
| IBPR-04 | Provider adds caller-unsafe to a safe expectation | Reject |
| IBPR-05 | Provider adds DoesNotReturn where no promised normal completion or other outcome is violated | Effect-bound comparison permits it, subject to clause applicability and all other contracts |
| IBPR-06 | Expected DoesNotReturn promise is removed | Reject |
| IBPR-07 | Provider claims stronger effects by eliminating an expected guaranteed normal success | Reject; do not satisfy the API by vacuity |
| IBPR-08 | Unknown Effects bits are used, or NoThrow is treated as Pure/ConstEval | Reject unknown bits; do not infer other capabilities |
| IBPR-09 | All combinations of the three assigned Effects bits are compared | Promise bits may be added; caller-unsafe may be removed, not the reverse; numeric mask ordering is irrelevant |
| IBPR-10 | Typed predicate graphs match after exact subject/formal correspondence | Equality can establish implication |
| IBPR-11 | Supported scalar/structural evidence establishes weaker provider requirements and stronger guarantees | Accept the established directions under the same subjects, guards and phases |
| IBPR-12 | Different opaque predicates are asserted equivalent by name, samples or private-body similarity | Reject unestablished compatibility |
| IBPR-13 | An arbitrary weaker require check changes specified invalid-input failure/evaluation/cleanup | Preserve the executable validation contract; domain widening alone is insufficient |
| IBPR-14 | Provider adds NoAlias to an expectation admitting overlap | Reject the new separation requirement |
| IBPR-15 | Provider removes NoAlias and preserves old-domain separation/output/effect promises | Domain widening is permitted |
| IBPR-16 | A specified rejection/validation observation is removed as an apparent optimization | Reject unless its preservation/absence is established under the governing rules |
| IBPR-17 | Added ensure invokes a failing predicate or phase cleanup on valid old inputs | Not strengthening; preserve actual failure/effect/cleanup behavior |
| IBPR-18 | A read/effect bound is narrowed, or instead adds a receiver-external source | Narrowing can satisfy the bound; an added source cannot |
| IBPR-19 | A true-result or pure-form conditional guarantee is offered as unconditional/ordinary | Reject the guard/form mismatch |
| IBPR-20 | Provider supports additional pure/constant forms | Preserve old selected-form behavior; extra support is not retrospective reselection |

## 4. State, ownership and dependent results

| Case | Situation | Required result |
|---|---|---|
| IBST-01 | False formerly leaves an init output vacant, but provider initializes it | Reject the changed state, not "more initialization" |
| IBST-02 | Provider publishes initialized output on an exceptional outcome required to be vacant | Reject |
| IBST-03 | Correlated states apply to a different output ordinal/status/phase | Reject |
| IBST-04 | NoThrow/DoesNotReturn bits are offered to erase an independent exact output-state contract | Reject the state mismatch; bounds do not override exact dimensions |
| IBST-05 | Provider adds a result source or capture destination | Reject added dependencies/capture |
| IBST-06 | Provider excludes a permitted source, or keeps it with backing-only forwarding | Can satisfy the broad bound; exclusion also satisfies that source's backing refinement |
| IBST-07 | Provider changes backing-only forwarding to source-storage borrowing | Reject |
| IBST-08 | Result/capture owner, publication phase or responsible holder changes | Reject |
| IBST-09 | Advance/close invalidation, release or cleanup-affinity promise is weakened | Reject |
| IBST-10 | An affine owner becomes Copy or transfers/settles responsibility differently | Reject the ownership contract change |
| IBST-11 | Arbitrary cleanup/unwind implementation changes the required phase/order | Establish actual preservation, not just matching metadata |
| IBST-12 | Original witness still matches but a retained qualified-self projection fails | Reject the complete retained graph |
| IBST-13 | A pure replacement changes a documented constant result | Reject the incompatible behavioral promise |
| IBST-14 | Same purity flag is used to reuse old evaluated values despite changed program/target dependencies | Revalidate actual constant dependencies; flag comparison is insufficient |

## 5. Graphs and using contexts

| Case | Situation | Required result |
|---|---|---|
| IBGR-01 | Several paths reach the same complete declaration/context pair | Deduplicate comparison work, not required obligations |
| IBGR-02 | Finite nominal contract graphs refer recursively to already selected identities | Complete the worklist without endless expansion |
| IBGR-03 | Encoded structural TypeSpec references form a cycle or point forward illegally | Reject malformed structure before comparison |
| IBGR-04 | A reachable node behind a nominal cycle changes signature/effects/state | Reject despite having visited the root |
| IBGR-05 | One physical structural row resolves imported/formal identities differently in two using contexts | It cannot be shared as one meaning |
| IBGR-06 | Merger clones that row and remaps only the affected context | Preserve both complete meanings and expectations |
| IBGR-07 | Memoization keys omit module or complete formal/substitution context | Do not reuse acceptance across the distinct pairs |
| IBGR-08 | TypeSpec row 255/256 remapping leaves Body-local t1 membership unchanged | Remap fixed map references, not identical instruction data bytes; apply the registry's complete fixups |
| IBGR-09 | An Import is checked then rewritten to its selected packed definition | Preserve the expectation and all remaining dependency obligations |
| IBGR-10 | Applicable comparison resources are exhausted before the closure finishes | Explicitly decline; no success-shaped depth cutoff |
| IBGR-11 | Unproved predicates circularly cite each other's guarantee as their only evidence | A visited graph is not an implication/body proof |
| IBGR-12 | An arbitrary associated binding/substitution expands indefinitely | Reject unsupported/nonfinite normalization; nominal cycle handling does not legalize it |

## 6. Replacement and admission

| Case | Situation | Required result |
|---|---|---|
| IBRP-01 | Broad old expectation binds to a stronger provider | Do not silently rewrite the retained caller's published contract |
| IBRP-02 | Native optimization exploits the provider's stronger NoThrow fact | Retain and revalidate that stronger dependency; the broad old expectation alone is insufficient |
| IBRP-03 | Same module/key is reused after contract/context/rules/target/trust facts change | Revalidate affected admission/comparison decisions |
| IBRP-04 | A packed direct call has no Import row but retains provider layout/value/cleanup/code obligations | Those edges remain in replacement admission |
| IBRP-05 | Arbitrary transformed/native uses include offsets, constants, witnesses and code/load roots | Relink/rebuild or prove preservation for all affected uses; metadata-only rebinding is insufficient |
| IBRP-06 | Semantic contract matches but installed native ABI/calling convention does not | Reject unchanged-native servicing |
| IBRP-07 | A newer rule revision is offered without an applicable explicit support mapping | Reject; effect strengthening is not semantic revision compatibility |
| IBRP-08 | Unsupported/unrepresented required facts are present despite trusted provider/body claims | Reject before affected use |
| IBRP-09 | General init/generic/witness/predicate semantics have only inspectable assigned row shapes | Do not claim execution/import support without the complete required representation |
| IBRP-10 | Private body hashes or algorithm similarity are offered as binding evidence | Do not infer undeclared compatibility |
| IBRP-11 | Admission runs a user getter/equality/initializer/cleanup to discover compatibility | Not permitted; use declared supported records and checking evidence |
| IBRP-12 | Contract comparison succeeds but an actual invocation violates a precondition or implementation lies | Ordinary invocation/truth rules still apply; comparison is not their proof |
| IBRP-13 | A compatible provider is hot-swapped into running code/owners | No permission follows; require separately admitted activation, lifetime and concurrency protocols |
