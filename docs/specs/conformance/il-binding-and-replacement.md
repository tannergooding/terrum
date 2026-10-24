# Binding and replacement conformance

These cases apply [binding compatibility](../representation/binding-and-replacement.md),
[complete types](../language/types-and-normalization.md) and
[requirements and admission](../representation/requirements-and-admission.md).
Named modules, definitions, predicates and proof results are schematic resolved
facts. A supplied checked implication is not proof of an arbitrary predicate;
a supplied supported signature/layout is not an execution or physical ABI claim.
Independent row fragments assess their actual shapes, not a complete linker.
Cases define producer truth and successful compatibility claims. They do not
require consumers to validate malformed metadata or prescribe how
incompatible/unsupported input is diagnosed or handled; incompatible facts do
not establish a successful binding or replacement claim.

## 1. Selected definitions and expectations

| Case | Situation | Required result |
|---|---|---|
| IBID-01 | One exact producer/key selects a declaration of the expected kind with a satisfying contract | Binding succeeds, subject to the complete admission gates |
| IBID-02 | Same key and shape are offered by a different Module identity | Do not substitute that producer |
| IBID-03 | ASCII and UTF-16 String rows decode to the same scalar identity/key | Equal decoded text selects the same identity |
| IBID-04 | Case folding or Unicode normalization would make two different keys equal | Do not fold/normalize; the exact selected key must exist |
| IBID-05 | Producer publishes two export targets for one selected key, or leaves two producer revisions selected | Verification rejects the ambiguous selected identity before binding |
| IBID-06 | Two distinct keys export one public local definition | Preserve one resolved definition identity through both aliases |
| IBID-07 | Producer publishes an Export row naming an imported expectation or private declaration | Verification rejects the invalid identity/access reference before binding |
| IBID-08 | Aliased keys formerly selected one retained nominal identity but replacement splits them | Reject the broken retained equality relationship |
| IBID-09 | Two consumers retain distinct complete callable expectations for one key | Check each independently against the selected provider |
| IBID-10 | Provider satisfies only one retained expectation | Reject the other edge; do not pick the weaker expectation |
| IBID-11 | Producer publishes one expected local declaration mapped to two different selected definitions | Verification rejects the inconsistent binding before affected use |
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
| IBPR-01 | Expected callable may fail and provider preserves its published operation/failure contract | Compare the complete declared contract; do not infer an unencoded failure guarantee |
| IBPR-02 | Provider implementation appears not to fail, but publishes no corresponding contract | Do not strengthen compatibility from private-body behavior |
| IBPR-03 | Expected caller-unsafe requirement is removed without changing other promises | Compatible domain widening |
| IBPR-04 | Provider adds caller-unsafe to a safe expectation | Reject |
| IBPR-05 | Provider adds DoesNotReturn where no promised normal completion or other outcome is violated | Effect-bound comparison permits it, subject to clause applicability and all other contracts |
| IBPR-06 | Expected DoesNotReturn promise is removed | Reject |
| IBPR-07 | Provider claims stronger effects by eliminating an expected guaranteed normal success | Reject; do not satisfy the API by vacuity |
| IBPR-08 | Producer emits retired Effects bit `0x01`, unknown bits, or treats Effects as Pure/ConstEval | Verification rejects the malformed clause; no other capabilities follow |
| IBPR-09 | All combinations of the two assigned Effects bits are compared | DoesNotReturn may be added subject to all other contracts; caller-unsafe may be removed, not added; numeric mask ordering is irrelevant |
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
| IBST-04 | DoesNotReturn or caller-unsafe is offered to erase an independent exact output-state contract | Reject the state mismatch; Effects do not override exact dimensions |
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
| IBGR-03 | Producer emits structural TypeSpec references that form a cycle or point forward illegally | Verification rejects the invalid structure before affected use |
| IBGR-04 | A reachable node behind a nominal cycle changes signature/effects/state | Reject despite having visited the root |
| IBGR-05 | One physical structural row resolves imported/formal identities differently in two using contexts | It cannot be shared as one meaning |
| IBGR-06 | Merger clones that row and remaps only the affected context | Preserve both complete meanings and expectations |
| IBGR-07 | Memoization keys omit module or complete formal/substitution context | Do not reuse acceptance across the distinct pairs |
| IBGR-08 | TypeSpec row 255/256 remapping leaves Body-local t1 membership unchanged | Remap fixed map references, not identical instruction data bytes; apply the registry's complete fixups |
| IBGR-09 | An Import is checked then rewritten to its selected packed definition | Preserve the expectation and all remaining dependency obligations |
| IBGR-10 | Applicable comparison resources are exhausted before the closure finishes | Explicitly decline; no success-shaped depth cutoff |
| IBGR-11 | Unproved predicates circularly cite each other's guarantee as their only evidence | A visited graph is not an implication/body proof |
| IBGR-12 | Producer publishes an associated binding/substitution that expands indefinitely | Verification rejects the non-finite normalization contract; nominal cycle rules do not make it valid |
| IBGR-13 | A valid constructed TypeSpec has visible type/constant arguments and existential arguments | Preserve arguments in the existing variable-length TypeSpec prefix/payload scheme; the selected reserved existential argument code composes with compact built-in argument codes. Its exact code value, slot layout, references and canonical encoding remain a coordinated schema-review gate; do not revive fixed 8-byte tagged entries or a TypeArg range |
| IBGR-14 | A nominal generic with hidden arguments is packed or remapped | Preserve exact visible arguments and each value's per-hidden-ordinal TypeSpec/evidence range; remap nested typed references without making evidence type-global |
| IBGR-15 | A generic formal has an intrinsic property or user-defined behavioral requirement | Use a registered property code or an interface-instance Constraint/witness as appropriate; property codes do not replace evidence, and zero `targetTypeSpec` is not a sentinel |
| IBGR-16 | Producer emits an unknown or malformed type-property Constraint | Verification rejects before affected use; it does not authorize dropping or reinterpreting the requirement |
| IBGR-17 | An associated type declaration is imported and selected through a witness | Preserve its first-class DefId/Import identity and bind it under the exact complete interface instance; do not resolve by name or ordinal alone |
| IBGR-18 | An associated constant is used through a complete interface witness | Require an explicit typed witness binding; preserve its distinct `AssociatedConstDef` identity, value/domain/dependencies and exact interface instance, with no declaration-default fallback |
| IBGR-19 | One generic-call operation targets a direct method or an interface requirement | Preserve the tagged target role; interface calls retain the exact formal `Constraint`, closed `InterfaceWitness`, or closed `WitnessProjection`, while direct calls carry neither; consumers do not reconstruct evidence from names or private bodies |
| IBGR-20 | Core publishes a primitive interface witness through the shared witness family | Key it by the exact primitive implementing `TypeSpec` and complete interface instance; reject ordinary-package primitive membership and fabricated nominal type identities |
| IBGR-21 | Generated content captures a reference-bearing or ownership-bearing value | Capture mode distinguishes copy from ownership transfer only; preserve full type/access placement and backing/provenance separately, including distinct `ref<write T>` and `write ref<T>` identities |
| IBGR-22 | An open formal requirement and closed implementing evidence are both retained | Keep the formal `Constraint` free of a witness reference; retain closed evidence as the exact `InterfaceWitness` or `WitnessProjection` |
| IBGR-23 | Interface, type-property and associated-relation requirements use the shared `Constraint` row | Valid artifacts use the nonzero in-row payload by exact kind as an interface TypeSpec, property code or associated-relation reference; verify typed references and variants before affected use |
| IBGR-24 | A formal has `where T : write`, or a value carries a root/pin dependency | Check immediate write access against the complete normalized argument without rewriting it; preserve root/pin dependencies separately from TypeSpec identity |
| IBGR-25 | A selected generic method has matching formal arity after declaring-owner substitution, but formal names differ | Map requirement and selected method formals by ordinal; preserve each formal's constraints, defaults and contracts |
| IBGR-26 | A selected generic method requires reordered arguments, a different arity or an implicit projection | Reject; do not invent an argument mapping without a separately admitted forwarding/projection contract |
| IBGR-27 | Two generated text-content shapes normalize to the same complete key | Share one logical generated type only within the same originating module; key by ordered complete capture TypeSpecs/modes, fragment-kind structure and exact formatter/writer requirements; exclude literal bytes and per-instance values |
| IBGR-28 | A closed projected witness omits a normalized inherited requirement or maps one twice | Require exactly one typed mapping per requirement in the normalized requested-interface closure, including unchanged/direct mappings; missing and duplicate entries violate the producer schema |
| IBGR-29 | Two existential values have the same generic view but different hidden arguments or witnesses | Preserve an independent first-class owner per value and one outer evidence entry per hidden formal ordinal; retain its hidden TypeSpec and nested constraint-keyed witness range without merging values' evidence |
| IBGR-30 | Two tuple TypeSpecs have the same ordered element TypeSpecs but different source labels | Give them the same structural tuple identity; labels are source-view only, while arity and ordered element types determine the type |
| IBGR-31 | A frozen nominal union value selects one declared case with typed payload fields | Retain separate `UnionCaseDef` and `UnionCaseField` identities, exact active-case identity and payload slots; do not treat cases as ordinary fields or infer the case from payload layout |
| IBGR-32 | A generic formal has a type or constant default | Retain the approved 32-byte `GenericParam` shape and typed default: TypeSpec for a type formal, `ConstantSource` for a constant formal; reject inapplicable domain/reference fields |
| IBGR-33 | One required constant request uses a checked method or admitted intrinsic | Retain one `ConstantExecution` per request with exact result type, substitution, ordered transformed inputs, executed/layout dependencies and resource recipe; the result is a separate frozen value, not evaluator bytecode |
| IBGR-34 | A published predicate or dependency is packed/remapped | Preserve each graph as an ordered occurrence tree; keep read-footprint, per-query layout, ordinary backing/retention and static-provider dependencies as distinct typed families with their own owners |
| IBGR-35 | A generated content shape varies only in literal bytes, alignment/format data, parameter ordinal, consumer instance, source position or generated name | Preserve one module-local shape identity; none of those instance/display details enter the approved complete shape key |
| IBGR-36 | A `G<_>` parameter/result or stored field receives different hidden types at different runtime instances | Keep one slot-level evidence shape, but retain each runtime value's own hidden TypeSpec and witness references; a static owner must not freeze one concrete hidden type or rely on Body descriptors alone |
| IBGR-37 | One hidden formal has multiple interface-instance `Constraint` rows, including a projected witness | Preserve one nested evidence slot per applicable interface constraint, keyed to the exact `Constraint`; statically known formations reference exact direct/projected witnesses, while runtime values supply their own; reject missing, duplicate or mismatched entries without converting property/associated-relation facts into witnesses |

## 6. Replacement and admission

| Case | Situation | Required result |
|---|---|---|
| IBRP-01 | Broad old expectation binds to a stronger provider | Do not silently rewrite the retained caller's published contract |
| IBRP-02 | Native optimization relies on a provider ABI/layout fact not present in the broad expectation | Retain and revalidate that stronger dependency; the broad old expectation alone is insufficient |
| IBRP-03 | Same module/key is reused after contract/context/rules/target, deployment-provenance policy or relied-on audit result changes | Revalidate affected admission/comparison decisions |
| IBRP-04 | A packed direct call has no Import row but retains provider layout/value/cleanup/code obligations | Those edges remain in replacement admission |
| IBRP-05 | Arbitrary transformed/native uses include offsets, constants, witnesses and code/load roots | Relink/rebuild or prove preservation for all affected uses; metadata-only rebinding is insufficient |
| IBRP-06 | Semantic contract matches but installed native ABI/calling convention does not | Reject unchanged-native servicing |
| IBRP-07 | A newer rule revision is offered without an applicable explicit support mapping | Reject; effect strengthening is not semantic revision compatibility |
| IBRP-08 | Unsupported/unrepresented required facts are present despite a producer-published contract | Reject before affected use |
| IBRP-09 | General init/generic/witness/predicate semantics have only inspectable assigned row shapes | Do not claim execution/import support without the complete required representation |
| IBRP-10 | Private body hashes or algorithm similarity are offered as binding evidence | Do not infer undeclared compatibility |
| IBRP-11 | Admission runs a user getter/equality/initializer/cleanup to discover compatibility | Not permitted; use declared supported records and checking evidence |
| IBRP-12 | Contract comparison succeeds but an actual invocation violates a precondition or implementation lies | Ordinary invocation/truth rules still apply; comparison is not their proof |
| IBRP-13 | A compatible provider is hot-swapped into running code/owners | No permission follows; require separately admitted activation, lifetime and concurrency protocols |

## 7. FieldDef imports and layout paths

| Case | Situation | Required result |
|---|---|---|
| IBFP-01 | A consumer FieldDef stub and its expected owner TypeDef bind to public exports from the exact selected Module | Bind each identity independently; the field export's owner must be the selected provider owner |
| IBFP-02 | Provider field has the same name, type and offset but a different nominal owner | Reject; names, layout and row coincidence do not establish field identity |
| IBFP-03 | Expected/provider owner formals are renamed or renumbered but correspond by kind/ordinal | Apply the complete owner substitution at each use and compare the resulting field contract |
| IBFP-04 | A FieldPath step is an Import and the preceding field substitutes to a constructed next owner | Resolve the Import against that exact immediate owner; reject root-substitution reuse or a cached application for another constructed type |
| IBFP-05 | Field type, static/mutable role, visibility or complete access differs | Reject the mismatch, including an unsafe caller attempting to bypass private visibility |
| IBFP-06 | A complete promised Layout changes field membership/order or the queried field's position | Reject even if its current offset happens to be unchanged |
| IBFP-07 | A non-Blittable facade omits private fields needed for implementation layout | It cannot answer the offset from the stub alone; require admitted complete implementation layout and coupling or reject |
| IBFP-08 | FieldMap directly references a FieldDef/Import for a one-field path | Accept only when the exact root is its non-generic declaring nominal type; constructed, qualified, generic or nested paths use FieldPath |
| IBFP-09 | FieldPath is packed/shared across modules with different Import rows or expectations | Clone/remap as needed; each using Module must preserve its own exact Import and owner obligations |
| IBFP-10 | A satisfied FieldDef Import is replaced by a direct FieldDef DefId | Permit only when the exact provider identity and all expected/relied-on access, representation and layout obligations remain retained |
| IBFP-11 | A native symbol or `NativeExport` shares the metadata export key | It cannot satisfy FieldDef Import; metadata declaration binding and native binding remain distinct |
| IBFP-12 | A TypeIdentity comparison is folded or packed across provider replacement | Preserve normalized nominal identity, complete substitution/evidence and guarded refinement; equal TypeMap indexes, names or layouts do not satisfy the retained relation |
