# Contract clause conformance

These cases apply the [clause matrix](../representation/contract-clauses.md),
[metadata](../il-metadata.md) and existing source completion/ownership rules.
They do not activate reserved variants or certify a compiler/runtime.
Finite supplied declaration/body facts are distinct from independently
decoded bytes and arbitrary implementation truth.

The named row/range structures are decoded, compared or included in the
stated validation scope. Their global validity cases do not mandate a
preliminary scan of unrelated rows; [reader-trust cases](il-reader-trust.md)
own that access/audit distinction.

## Header, extent and reference framing

| Case | Input or observation | Required outcome |
|---|---|---|
| ICRW-01 | Each of the five assigned clause payloads has its exact extent | Decode 12, 16, 16, 20 and 12 bytes respectively, including the header |
| ICRW-02 | Any assigned row is truncated at any byte | Reject before reading outside its extent |
| ICRW-03 | A row has one extra byte or an extent different from its assigned length | Reject; no optional suffix or padding |
| ICRW-04 | Header flags are nonzero | Reject, including an otherwise valid zero-mask Effects |
| ICRW-05 | Clause kind is zero or an unassigned u16 value | Reject semantic consumption; never skip as debug |
| ICRW-06 | Owner is a nonzero typed row index naming an existing clause-implied owner | Effects/OutputState select MethodDef; Cleanup selects TypeDef without a table discriminator |
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
| ICOW-09 | Two OutputState rows select one owner's same parameter ordinal | Reject regardless of matching or different states |
| ICOW-10 | One owner has OutputState for distinct initialization ordinals | Accept the distinct keys and exact state pairs |
| ICOW-11 | Two TypeDef Cleanup rows designate the same or different target | Reject |
| ICOW-12 | Two MethodDefs share a MethodSig or independent expectations select one provider identity | Keep distinct range/occurrence keys and check every expectation; neither clause is inherited |

## Effects and zero-mask meaning

| Case | Input or observation | Required outcome |
|---|---|---|
| ICEF-01 | Each of the eight combinations of the three assigned bits | Structurally valid; truth and other contracts remain separate |
| ICEF-02 | Any unassigned u32 bit is set | Reject |
| ICEF-03 | Effects is TypeDef-owned | Reject; no type-wide mask |
| ICEF-04 | A PropertyDef tries to own Effects | Reject its owner; use the selected getter contract |
| ICEF-05 | No Effects row exists | Assigned mask zero, without inferring other effect capabilities |
| ICEF-06 | Exactly one well-formed zero-mask Effects row exists | Same assigned meaning as absence; semantic reader accepts |
| ICEF-07 | A canonical producer emits assigned mask zero | Omit the Effects row; retain independent clauses |
| ICEF-08 | NoThrow and DoesNotReturn are combined | No normal completion and no escaping failure; divergence can satisfy both |
| ICEF-09 | Caller-unsafe is combined with either promise bit | Combination is permitted; call-site authorization remains required |
| ICEF-10 | An enclosing type or another member has a NoThrow promise | Does not supply this callable's promise |
| ICEF-11 | NoThrow is offered as Pure, ConstEval, NoAlias or no-safepoint | No such implication |
| ICEF-12 | Caller-unsafe is offered as callee-body authorization or receiver write authority | Does not supply either |
| ICEF-13 | Property lookup resolves one getter but another getter has the desired mask | Use only the selected getter's complete contract |
| ICEF-14 | Source async/producer contracts have distinct execution extents | Preserve those extents; the bounded Effects row does not complete missing phase codecs |
| ICEF-15 | Effects validity is tested after a provider strengthens its mask | Invalid owner/shape still rejects before directional satisfaction |

## OutputState subjects and exact states

| Case | Input or observation | Required outcome |
|---|---|---|
| ICOS-01 | Boolean-result MethodDef selects a valid mode-01 ordinal | Apply its explicit normal state pair |
| ICOS-02 | Result is integer, optional bool, same-sized nominal type or Task<bool> | Reject this clause's subject; no shape-based completion reinterpretation |
| ICOS-03 | Selected ordinal is absent or is a Param row number instead of the logical ordinal | Reject |
| ICOS-04 | Param belongs to another MethodSig or has mismatched/noncontiguous ordinal | Reject |
| ICOS-05 | Param is ordinary value/ref or uses implicit adaptation | Not an initialization-destination subject |
| ICOS-06 | Initialization destination is writable, pinned or not an outer ref | Reject; preserve its exact canonical mode/type |
| ICOS-07 | Init destination's complete inner type is qualified, optional or void | Preserve the inner type; state is initialization, not payload/presence |
| ICOS-08 | Each of the four true/false state pairs | Valid explicit state choice; none is inferred from the method name |
| ICOS-09 | A state byte exceeds one or the reserved byte is nonzero | Reject |
| ICOS-10 | Exceptional state is initialized | Reject, even when the method claims NoThrow |
| ICOS-11 | A required retained/source Boolean correlation has no matching OutputState | Missing representation rejects use; do not guess a default |
| ICOS-12 | One output is true-live/false-vacant and another is live on both outcomes | Preserve both independently, including false-live ownership |
| ICOS-13 | DoesNotReturn is added to a Boolean method with a retained OutputState | Keep exact clauses; absence of normal completion does not repair a state mismatch |
| ICOS-14 | A runtime implementation publishes initialized-null, a partial owner or a later async output | Distinguish complete value/presence/phase; row decoding alone proves none of those executions |

## Cleanup target applicability

| Case | Input or observation | Required outcome |
|---|---|---|
| ICCU-01 | Unique TypeDef selects a same-owner readonly instance void/no-parameter NoThrow cleanup | Applicable only with all required uniqueness, role, phase and ownership facts |
| ICCU-02 | Cleanup is MethodDef-owned | Reject |
| ICCU-03 | TypeDef is ordinary Copy, or uniqueness evidence is missing | Reject semantic use; struct kind alone is insufficient |
| ICCU-04 | Target is zero, out of bounds, a DefId, String, Body or guessed name | Reject the typed target reference or selected target |
| ICCU-05 | Target belongs to an unrelated nominal type | Reject despite equal layout or spelling |
| ICCU-06 | Target is static, constructor, accessor, async or extern | Reject as the automatic cleanup operation |
| ICCU-07 | Receiver is writable, pinned, absent or refers to another type | Reject |
| ICCU-08 | Target result is non-void, or it has parameters/generic formals | Reject the selected parameterless ordinary cleanup shape |
| ICCU-09 | Target lacks checked NoThrow or requires an unsafe caller | Reject |
| ICCU-10 | Target is non-public but otherwise has the complete valid cleanup contract | Visibility need not create a public destruction alias |
| ICCU-11 | Cleanup diverges under a valid NoThrow/no-return contract | Do not invent completed retirement; no general termination promise is added |
| ICCU-12 | General complete/partial retirement or native cleanup execution is claimed from decoded rows | Requires independent ownership/runtime evidence and admitted codecs |

## No-return signatures, flow and calls

| Case | Input or observation | Required outcome |
|---|---|---|
| ICNR-01 | DoesNotReturn declaration has any assigned atomic result type | Applicable independent of result type and independent of execution support |
| ICNR-02 | A correctly typed no-return helper declares a nominal, optional or generic result | Source applicability does not require void or a public bottom type |
| ICNR-03 | A claimed no-return body has a reachable normal return, unit return or fallthrough | Reject the false promise |
| ICNR-04 | All reachable exits escape failure and no NoThrow is promised | Can satisfy DoesNotReturn; preserve failure/cleanup |
| ICNR-05 | A structurally divergent path has no normal exit | Can satisfy DoesNotReturn; NoThrow alone does not prove termination |
| ICNR-06 | NoThrow/no-return body has a reachable failure | Reject NoThrow independently of no-return truth |
| ICNR-07 | A task/cursor is normally returned and only later fails or diverges | Does not satisfy the factory's DoesNotReturn |
| ICNR-08 | Async establishment normally returns a responsible Task | Does not satisfy no-return merely because operation completion is absent |
| ICNR-09 | Unreachable source has an invalid type, binding or implicit non-unit discard | Diagnose independently of the absent normal edge |
| ICNR-10 | A non-unit no-return call is placed where an expression of void/different type is required | No bottom/discard conversion; ordinary typing still applies |
| ICNR-11 | Encoded Call targets non-unit no-return with result ID zero | Reject; the ordinary typed result operand is required |
| ICNR-12 | Correctly typed scalar no-return Call ends a block before its Fail fallback | Checked no-return contract admitted through producer trust or adequate audit removes the normal continuation; no result is fabricated |
| ICNR-13 | An earlier operation can fail or callee no-return lacks an admitted checking/trust basis | Do not justify a NoThrow body or unreachable fallback from the annotation alone |
| ICNR-14 | DoesNotReturn callable uses an otherwise unsupported scalar width/reference/owner signature | Reject affected execution; result-type applicability is not profile expansion |

## Reserved variants, trust and transformations

| Case | Input or observation | Required outcome |
|---|---|---|
| ICAD-01 | Transfer/Dependency rows have their exact reserved shapes | Inspection only; no payload value is a semantically admitted variant |
| ICAD-02 | All-zero reserved payload is offered as an empty transfer/dependency | Reject semantic use, not success with a no-op contract |
| ICAD-03 | Access to a trusted producer's clause exposes an unknown kind or malformed owner/range/mask | Required natural checks still reject; unrelated ranges need no preliminary global scan |
| ICAD-04 | A valid OutputState/Cleanup shape is offered to the scalar executor | Decline unsupported publication/ownership execution before use |
| ICAD-05 | Clause absence is offered as fresh backing or no cleanup responsibility | No such inference |
| ICAD-06 | Owner/Contract/MethodDef/Param rows are reordered with exact remapping | Same selected clauses, logical ordinals and occurrence keys |
| ICAD-07 | A rewriter changes coincident payload octets rather than decoded references | Does not preserve the contract; only reference fields are remapped |
| ICAD-08 | A retained getter/cleanup context or optimized stronger promise changes | Revalidate the complete affected dependency; do not substitute a weaker expectation |
| ICAD-09 | A context-sensitive shared structural row is packed without its required clone | Reject differing resolved meaning; same bytes are not sufficient |
| ICAD-10 | Full linking, arbitrary source/body proof, transformation or runtime correctness is claimed from this finite corpus | No such conformance claim; require the independently scoped evidence |
