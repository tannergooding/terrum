# Contract clause conformance

These cases apply the [clause matrix](../representation/contract-clauses.md),
[metadata](../il-metadata.md) and existing source completion/ownership rules.
They do not activate reserved variants or certify a compiler/runtime.
Finite supplied declaration/body facts are distinct from independently
decoded bytes and arbitrary implementation truth.

These cases describe producer validity and successful support/compatibility
claims. Consumers validate required record shapes, owners, references and
support before affected use; producer-published semantics are not subject to
mandatory consumer re-proof. Structurally invalid or unsupported required
input is rejected. Diagnostic wording and internal check organization are not
prescribed. Optional semantic-audit scope is governed by
[consumer-verification cases](il-reader-trust.md).

## Header, extent and reference framing

| Case | Input or observation | Required outcome |
|---|---|---|
| ICRW-01 | Each assigned clause has its exact row extent | Decode 12, 20, 16, 20 and 12 bytes for Effects, OutputState, Transfer, Dependency and Cleanup, including the header |
| ICRW-02 | Any assigned row is truncated at any byte | Reject before reading outside its extent |
| ICRW-03 | A row has one extra byte or an extent different from its assigned length | Reject; no optional suffix or padding |
| ICRW-04 | Header flags are nonzero | Reject, including an otherwise valid zero-mask Effects |
| ICRW-05 | Clause kind is zero or an unassigned u16 value | Reject semantic consumption; never skip as debug |
| ICRW-06 | Owner is a nonzero typed row index naming an existing clause-implied owner | Effects/OutputState/Dependency select MethodDef; Cleanup selects TypeDef without a table discriminator |
| ICRW-07 | Owner is null or a DefId with a nonzero high byte | Reject; an explicit kind is not an alternate owner encoding |
| ICRW-08 | A range owner differs from the table implied by the clause kind | Reject; no FieldDef/PropertyDef/MethodSig/GenericParam/Body owner or type-wide Effects |
| ICRW-09 | Clause-implied owner row index is out of bounds | Reject |
| ICRW-10 | Contract offset index is decreasing, omits bytes or ends outside the table payload | Reject |
| ICRW-11 | Offset/count arithmetic would wrap or exceed the supplied bounded table | Reject before allocation/indexing |
| ICRW-12 | Header and payload are independently spelled little-endian bytes | Decode the actual kind/owner/value; do not reinterpret numeric octets as tags |

## Owner ranges and occurrence keys

| Case | Input or observation | Required outcome |
|---|---|---|
| ICOW-01 | Empty owner range is `(0, 0)` | Accept |
| ICOW-02 | Zero count has nonzero first, or positive count has zero first | Reject |
| ICOW-03 | A nonempty range exactly reaches the last supplied Contract row | Accept its inclusive bound |
| ICOW-04 | A range extends past the table or wraps its endpoint | Reject |
| ICOW-05 | A Contract is outside every owner range | Reject as orphaned |
| ICOW-06 | Two owners' ranges overlap, including an identical shared range | Reject |
| ICOW-07 | A ranged row names another valid owner | Reject, not inherited membership |
| ICOW-08 | Two Effects rows belong to one MethodDef | Reject even with equal masks or two zero masks |
| ICOW-09 | Two OutputState rows have the same owner, parameter ordinal and predicate identity | Reject duplicate key even if their states agree |
| ICOW-10 | One output has distinct predicate identities with a consistent state for every overlapping outcome | Accept the distinct relations |
| ICOW-11 | Two TypeDef Cleanup rows designate the same or different target | Reject |
| ICOW-12 | Two MethodDefs share a MethodSig or independent expectations select one provider identity | Keep distinct range/occurrence keys and check every expectation; neither clause is inherited |
| ICOW-13 | Static-initialization roles or demands are persisted | Place them as MethodDef-owned Contract clauses, not separate per-method tables; use the approved 20-byte role clause with tags `1..5` and 16-byte group-demand clause, while clause IDs and typed-reference encoding remain open |
| ICOW-14 | A method has more than one role, or duplicate demands for one exact closed eager group | Reject; a role occurs at most once, while demands may repeat only for distinct groups |
| ICOW-15 | An initialization role identifies a generated eager/lazy step, authored static constructor or helper | Use one `InitializationRole` clause kind with a closed role tag; do not split roles into clause kinds |
| ICOW-16 | A callable publishes one or more `require`/`ensure` predicates | Attach each exact `PredicateGraph` through the assigned MethodDef-owned `Contract` clause kind `0x000B`, shared by all phases; use closed `u8` phase tags `1=Precondition`, `2=NormalPostcondition`, rejecting zero/unknown values; carry an explicit zero-based, contiguous `u32` phase-local declaration ordinal independent of physical row order; reject gaps and duplicates; the graph references its own separate actual read-footprint record, distinct from the callable's allowed-root contract; the 20-byte candidate attachment layout is specified by ICOW-18; complete Feature registration remains open |
| ICOW-17 | A declaration predicate is attached with `static require`/`static ensure` or an exceptional-exit phase | Keep body-level static obligations separate from declaration-level predicate clauses; encode only precondition and normal-postcondition phases, and do not run a normal postcondition on an escaping exception |
| ICOW-18 | A PredicateGraph attachment is encoded in a Contract row | Require the approved 20-byte extent: the 8-byte header followed by `u8 phaseTag, u8 flags=0, u16 reserved=0, u32 phaseLocalOrdinal, u32 predicateGraphRef`; reject nonzero payload flags/reserved fields, zero/out-of-range indexes and a nonzero reference high byte; resolve the `u32` as a typed row reference and remap through the typed-reference map; clause kind `0x000B` and PredicateGraph section `0x0037` are assigned, while complete Feature registration and reference integration remain open |

## Effects and zero-mask meaning

| Case | Input or observation | Required outcome |
|---|---|---|
| ICEF-01 | Each of the four combinations of the two assigned bits | Structurally valid; truth and other contracts remain separate |
| ICEF-02 | Any unassigned or retired u32 bit is set | Reject |
| ICEF-03 | Effects is TypeDef-owned | Reject; no type-wide mask |
| ICEF-04 | A PropertyDef tries to own Effects | Reject its owner; use the selected getter contract |
| ICEF-05 | No Effects row exists | Assigned mask zero, without inferring other effect capabilities |
| ICEF-06 | Exactly one well-formed zero-mask Effects row exists | Same assigned meaning as absence; semantic reader accepts |
| ICEF-07 | A canonical producer emits assigned mask zero | Omit the Effects row; retain independent clauses |
| ICEF-08 | Retired Effects bit `0x01` is set | Reject; it is not a no-failure promise |
| ICEF-09 | Caller-unsafe is combined with DoesNotReturn | Combination is permitted; call-site authorization remains required |
| ICEF-10 | An enclosing type or another member has a DoesNotReturn promise | Does not supply this callable's promise |
| ICEF-11 | DoesNotReturn is offered as Pure, ConstEval, NoAlias or no-safepoint | No such implication |
| ICEF-12 | Caller-unsafe is offered as callee-body authorization or receiver write authority | Does not supply either |
| ICEF-13 | Property lookup resolves one getter but another getter has the desired mask | Use only the selected getter's complete contract |
| ICEF-14 | Source async/producer contracts have distinct execution extents | Preserve those extents; the bounded Effects row does not complete missing phase codecs |
| ICEF-15 | Effects validity is tested after a provider strengthens its mask | Invalid owner/shape still rejects before directional satisfaction |

## OutputState subjects and exact states

| Case | Input or observation | Required outcome |
|---|---|---|
| ICOS-01 | MethodDef has an ordinary-mode parameter whose complete type is `ref<init T>` and a valid published predicate | Apply that predicate's declared normal state to the selected output |
| ICOS-02 | Predicate is unresolved, not published, or selects a non-normal phase | Reject affected use; do not infer an outcome from names or status bits |
| ICOS-03 | Selected ordinal is absent or is a Param row number instead of the logical ordinal | Reject |
| ICOS-04 | Param belongs to another MethodSig or has mismatched/noncontiguous ordinal | Reject |
| ICOS-05 | Param mode is `01`, type is `ref<T>`, or the parameter relies on implicit adaptation | Reject; `ref<init T>` is a complete TypeSpec with ordinary mode `00` |
| ICOS-06 | `ref<init T>` combines unsupported outer access/pin qualification or the reserved mode | Reject; preserve the selected complete type and eligibility rules |
| ICOS-07 | Init destination's complete inner type is qualified, optional, unit or reference-like | Preserve the complete inner type; vacancy is distinct from payload/presence and does not initialize nested referents |
| ICOS-08 | State is `0` or `1` | Interpret as vacant or fully complete, respectively |
| ICOS-09 | State exceeds one or any reserved byte is nonzero | Reject |
| ICOS-10 | Exceptional state is encoded in OutputState | Reject; exceptional state is indeterminate and is not carried by this clause |
| ICOS-11 | A required output relation has no matching exact predicate row | Reject affected use; do not guess a default relation |
| ICOS-12 | Two applicable predicates for one output map the same normal outcome to different states | Reject the contradictory/ambiguous declaration |
| ICOS-13 | Unconditional output is complete on every normal return | No OutputState row is required |
| ICOS-14 | A live optional contains null, an aggregate is partial, or output is deferred | Distinguish complete value, partial state and phase; row decoding alone proves none of those executions |
| ICOS-15 | DoesNotReturn is added to a method with OutputState | Retain both exact contracts; no normal edge does not erase validation or other obligations |
| ICOS-16 | One call reuses an output place across status outcomes or destination generations | Use the selected callable contract and decoded Body call/value/place references to correlate the exact destination generation; the producer is responsible for semantic truth, and no opaque success flag is persisted |
| ICOS-17 | A consumer uses producer-published output correlation | Validate the selected record shapes and references, then rely on producer-published correlation without semantic re-proof |

## Dependency relations and phases

| Case | Input or observation | Required outcome |
|---|---|---|
| ICOD-01 | Dependency names a result, checked output, retained formal holder or retained receiver | Resolve destination kind and its exact ordinal namespace |
| ICOD-02 | Dependency names a formal source or receiver source | Resolve formal ordinals in declaration order; receiver ordinal is zero |
| ICOD-03 | Relationship is borrow-source-storage, forward-existing-backing or declared-union | Preserve the selected relationship; do not infer backing from equal values or layout |
| ICOD-04 | Phase is normal publication, retention/capture or deferred-completion publication | Preserve the distinct phase and its exact settlement boundary |
| ICOD-05 | Explicit empty-source row has all zero source/relationship fields | Accept only when no nonempty source row exists for that destination and phase |
| ICOD-06 | Empty-source row coexists with a source row for the same destination and phase | Reject |
| ICOD-07 | Duplicate destination/phase/source key occurs | Reject even if the relationship value agrees |
| ICOD-08 | Dependency uses the retired native-output destination | Reject under the selected schema; no compatibility alias is assigned |
| ICOD-09 | A native pointer/output fact is inferred from pointer type or import metadata | Do not infer it; use ordinary admitted predicates or an authored wrapper, otherwise decline the unsupported contract |
| ICOD-10 | Dependency row is used to infer ownership transfer or extends a source lifetime | Reject that interpretation; owner handoff and lifetime remain separate contracts |

## Cleanup target applicability

| Case | Input or observation | Required outcome |
|---|---|---|
| ICCU-01 | Unique TypeDef selects a same-owner readonly instance void/no-parameter cleanup | Applicable only with all required uniqueness, role, phase and ownership facts; cleanup runs on normal retirement and may throw |
| ICCU-02 | Cleanup is MethodDef-owned | Reject |
| ICCU-03 | TypeDef is ordinary Copy, or uniqueness evidence is missing | Reject semantic use; struct kind alone is insufficient |
| ICCU-04 | Target is zero, out of bounds, a DefId, String, Body or guessed name | Reject the typed target reference or selected target |
| ICCU-05 | Target belongs to an unrelated nominal type | Reject despite equal layout or spelling |
| ICCU-06 | Target is static, constructor, accessor, async or extern | Reject as the automatic cleanup operation |
| ICCU-07 | Receiver is writable, pinned, absent or refers to another type | Reject |
| ICCU-08 | Target result is non-void, or it has parameters/generic formals | Reject the selected parameterless ordinary cleanup shape |
| ICCU-09 | Target requires an unsafe caller or has an unsupported async form | Reject |
| ICCU-10 | Target is non-public but otherwise has the complete valid cleanup contract | Visibility need not create a public destruction alias |
| ICCU-11 | Cleanup throws while generated retirement has pending fields | Propagate the exception; do not resume generated field cleanup during unwind |
| ICCU-12 | General complete/partial retirement or native cleanup execution is claimed from decoded rows | Requires independent ownership/runtime evidence and admitted codecs |

## No-return signatures, flow and calls

| Case | Input or observation | Required outcome |
|---|---|---|
| ICNR-01 | DoesNotReturn declaration has any assigned atomic result type | Applicable independent of result type and independent of execution support |
| ICNR-02 | A correctly typed no-return helper declares a nominal, optional or generic result | Source applicability does not require void or a public bottom type |
| ICNR-03 | A producer claims no-return for a body with a reachable normal return, unit return or fallthrough | Producer violates the truth obligation; a consumer need not detect a structurally consumable false claim, though an optional audit may |
| ICNR-04 | All reachable exits escape failure or diverge | Can satisfy DoesNotReturn; preserve failure/cleanup |
| ICNR-05 | A structurally divergent path has no normal exit | Can satisfy DoesNotReturn; a possible failure is also a permitted terminal path |
| ICNR-06 | Producer sets retired Effects bit `0x01` for a body with a reachable failure | Reject the malformed clause; no no-failure contract is assigned |
| ICNR-07 | A task/cursor is normally returned and only later fails or diverges | Does not satisfy the factory's DoesNotReturn |
| ICNR-08 | Async establishment normally returns a responsible Task | Does not satisfy no-return merely because operation completion is absent |
| ICNR-09 | Unreachable source has an invalid type, binding or implicit non-unit discard | Diagnose independently of the absent normal edge |
| ICNR-10 | A non-unit no-return call is placed where an expression of void/different type is required | No bottom/discard conversion; ordinary typing still applies |
| ICNR-11 | A CallSite targets a verified non-unit DoesNotReturn method and uses result selector None | Accept as a terminal Block instruction; publish no normal result or successor |
| ICNR-12 | A normal unit CallSite uses result selector None | Accept with its normal continuation; do not infer DoesNotReturn from the unit result |
| ICNR-13 | A consumer infers failure-free behavior or no-return from annotation-shaped source text rather than the selected contract | Do not infer either fact; only the exact published contract applies, and the producer is responsible for its truth |
| ICNR-14 | DoesNotReturn callable uses a signature whose required records or prerequisites are unavailable | Reject affected execution for that missing contract; result-type applicability is not a blanket category exclusion |
| ICNR-15 | A compact window call targets a DoesNotReturn callable | Reject that form; encode the terminal call with a general CallSite record |

## Reserved variants, trust and transformations

| Case | Input or observation | Required outcome |
|---|---|---|
| ICAD-01 | Transfer/Dependency rows have their exact assigned shapes | Dependency variants are interpreted under the approved relation contract; Transfer remains semantically inactive and rejects use |
| ICAD-02 | An all-zero row or reserved payload is offered as an empty Dependency/Transfer | Reject; an empty-source Dependency still has a valid destination and phase, while Transfer has no admitted semantic variant |
| ICAD-03 | A producer emits an unknown kind or malformed owner/range/mask | Verification rejects affected use before consuming the clause |
| ICAD-04 | An execution requires OutputState, Dependency or Cleanup facts whose predicate, destination or cleanup prerequisites are unavailable | No support claim for the affected use; do not impose or claim a reduced scalar-only conformance floor |
| ICAD-05 | Clause absence is offered as fresh backing or no cleanup responsibility | No such inference |
| ICAD-06 | Owner/Contract/MethodDef/Param rows are reordered with exact remapping | Same selected clauses, logical ordinals and occurrence keys |
| ICAD-07 | A rewriter changes coincident payload octets rather than decoded references | Does not preserve the contract; only reference fields are remapped |
| ICAD-08 | A retained getter/cleanup context or optimized stronger promise changes | Revalidate the complete affected dependency; do not substitute a weaker expectation |
| ICAD-09 | A context-sensitive shared structural row is packed without its required clone | Reject differing resolved meaning; same bytes are not sufficient |
| ICAD-10 | Full linking, arbitrary source/body proof, transformation or runtime correctness is claimed from this finite corpus | No such conformance claim; require the independently scoped evidence |
