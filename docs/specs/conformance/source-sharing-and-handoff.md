# Thread-capability and sharing conformance

These obligations apply to [sharing and handoff](../runtime/sharing-and-handoff.md).
Fixtures supply complete type, capability, extent, alias, protocol and endpoint
facts. Finite authority/retirement transitions do not prove arbitrary bodies,
native algorithms, weak-memory observations, collector behavior or codecs.

## Published capability identity and opt-in

| ID | Scenario | Required outcome |
|---|---|---|
| TCAP-01 | Unannotated user type has transferable-looking private fields | No inferred public thread capability |
| TCAP-02 | A same-named attribute/interface is not the resolved Core identity | No capability selection |
| TCAP-03 | Compiler specializes a private layout or body | Preserve declared capability facts; no new source binding |
| TCAP-04 | Same capability is annotated twice | Reject even if promises agree |
| TCAP-05 | Secondary partial type adds/changes capability opt-in | Reject primary-contract change |
| TCAP-06 | Capability is placed on alias/formal/member/static container/interface | Reject inapplicable opt-in target |
| TCAP-07 | A capability is checked for a qualified form | Retain immediate and reachable access, representation and full obligations |
| TCAP-08 | Only transferability or only shareability is declared | Do not infer the other property |
| TCAP-09 | Nominal opt-in condition is unmet | Type can remain valid; capability unavailable |
| TCAP-10 | Mandatory scalar/void complete value form is used | Core publishes both eligibility facts; no current exposure or wider atomic access |
| TCAP-11 | Readonly slot contains a writable reachable handle | Preserve its writable authority; no deep immutability inference |
| TCAP-12 | Alias normalizes to an eligible qualified target | Preserve that target's exact facts; no new nominal opt-in |
| TCAP-13 | Unannotated nominal wrapper/adapter has identical representation | No inherited capability from bits |
| TCAP-14 | A partial owning aggregate attempts whole handoff/publication | Reject incomplete whole value |
| TCAP-15 | A task/view/reference is small or its T is transferable | Require the family/carrier and actual dependency contracts; no outer-T inference |

## Capability-owned conditions

| ID | Scenario | Required outcome |
|---|---|---|
| TCOND-01 | Attribute-local where refers to later-declared exact formal | Bind that formal without a runtime/name-string recipe |
| TCOND-02 | Snapshot has type-wide Copy and local transfer/share conditions | Check Copy for formation; check the other conditions only for their capability facts |
| TCOND-03 | Two capabilities have different clauses on the same generic type | Keep their conjunctions and facts separate |
| TCOND-04 | Valid closed type does not satisfy a local capability condition | Confined type/operations remain valid |
| TCOND-05 | Ordinary generic body assumes a local capability condition globally | Reject unavailable fact |
| TCOND-06 | No-argument opt-in has no local conditions | Establish its unconditional promise; no inferred extra premises |
| TCOND-07 | Marked type's exposed protocol cannot satisfy its promised domain | Reject unestablished declaration truth |
| TCOND-08 | Runtime predicate/unknown property supplies a local condition | Reject unsupported condition, not deferred runtime inference |
| TCOND-09 | Multiple local clauses constrain several type arguments | Require every clause; no alternative condition or guessed argument |
| TCOND-10 | Condition selects associated/member witness facts | Preserve existing exact binding/constraint rules |
| TCOND-11 | Same-looking names/ordinals or constant formal substitute for T | Retain exact formal kind/identity; reject invalid binding |
| TCOND-12 | Caller writes ordinary where T : [ThreadTransferable] | Preserve existing terms and type-wide meaning; no where selector |
| TCOND-13 | Imported body privately proves stronger eligibility | Caller retains published conditions |
| TCOND-14 | Replacement strengthens a capability's local conditions | Reject incompatible relied-on capability domain |

## Confined crossing, aliases and return

| ID | Scenario | Required outcome |
|---|---|---|
| SHAND-01 | Complete eligible value crosses with admitted communication/holder | Transfer actual authority and dependencies |
| SHAND-02 | Ordinary move or copied pointer bits crosses a thread | No implicit handoff/publication permission |
| SHAND-03 | Sender retains a usable alias to remotely mutable state | Reject exclusive handoff |
| SHAND-04 | Temporary handoff reserves every conflicting sender alias | Permit only under actual return, lifetime and placement contract |
| SHAND-05 | Reserved referent is read/written/state-tested or derives a new alias | Reject access before checked return |
| SHAND-06 | Equal name/address is reused for another storage generation | Do not restore old authority to new storage |
| SHAND-07 | Crossing needs backing/root/guard/code that cannot survive | Reject; no hidden retained owner/root repair |
| SHAND-08 | Affine ownership handoff commits | Vacate named source; receiving/transit holder owns cleanup once |
| SHAND-09 | Preparation fails before commitment | Current holder remains responsible; actual argument/acquisition effects remain |
| SHAND-10 | Delivery fails after commitment | Preserve receiving/transit holder or checked settlement; no sender revival |
| SHAND-11 | Remote body fails then completes required cleanup/return | Return actual authority only at its specified endpoint |
| SHAND-12 | Cancellation/notification is used as return | Reject premature authority release |
| SHAND-13 | Status reports terminal before required observation/return edge | No inferred sender availability |
| SHAND-14 | Borrowed backing outlives descriptor but not handoff extent | Preserve actual backing/return endpoints, not descriptor address |
| SHAND-15 | Permanent crossing leaves an unaccounted nonowning alias | Reject missing authority accounting |
| SHAND-16 | Independently complete child crosses from partial structural parent | Check child and surviving siblings; no whole-parent transfer |
| SHAND-17 | Value is only usable on a different home thread/executor | Reject incompatible destination affinity |
| SHAND-18 | Use is permitted remotely but cleanup is home-affine | Preserve actual cleanup placement or reject unsupported responsibility transfer |
| SHAND-19 | Serialized async flow migrates threads | Check each actual crossing, reservation and affinity |
| SHAND-20 | Return depends on hidden blocking/join/fairness | No invented settlement or progress guarantee |

## Immutable and mutable publication

| ID | Scenario | Required outcome |
|---|---|---|
| SPUB-01 | Required value/shape/storage state is incomplete | Reject publication |
| SPUB-02 | Receiver observes the matching release publication with acquire | Establish the declared publication relation |
| SPUB-03 | Receiver reads an older publication flag/value | No selected payload publication |
| SPUB-04 | Matching bits came from relaxed/nonpublishing communication | No inferred acquire/release edge |
| SPUB-05 | Shallow readonly container retains writable elements | Not an immutable extent without the actual element protocol |
| SPUB-06 | Immutable extent retains a permitted conflicting writer | Reject mutation exclusion claim |
| SPUB-07 | Mutable extent becomes concurrently exposed | Every competing path adopts its checked protocol before access |
| SPUB-08 | Earlier copied/private alias bypasses the current shared protocol | Reject; alias age/bits do not grant confined access |
| SPUB-09 | Established immutable reader performs ordinary reads | Allowed with retained publication/lifetime; no repeated acquire required |
| SPUB-10 | Root/reachability is used as publication or disposal authority | Reject missing independent contract |
| SPUB-11 | Owner withdraws/reclaims while readers/operations remain | Reject until actual quiescence and dependencies permit |
| SPUB-12 | Empty/end view is published or retained | Preserve root/backing/publication/return duties |
| SPUB-13 | Payload stays owned/cleaned home while permitted alias is shared | Keep distinct alias crossing and payload ownership/affinity contracts |
| SPUB-14 | Private protected T is not independently shareable | Encapsulation can be valid; actual remote use, transfer and affinity still required |

## Reader/exclusive guard authority

| ID | Scenario | Required outcome |
|---|---|---|
| SGRD-01 | Exclusive writer overlaps another holder's reader/writer | Reject overlapping acquisition/access |
| SGRD-02 | Multiple compatible readers hold the same extent | Allow reads under their actual tokens and writer exclusion |
| SGRD-03 | Single writer is used to permit unguarded concurrent readers | Reject missing reader/writer coordination |
| SGRD-04 | Acquisition succeeds | Publish one actual affine token and selected authority |
| SGRD-05 | Permitted Try acquisition refuses | No token/access or fabricated output |
| SGRD-06 | Acquisition setup fails after prior source effects | Keep effects and actual holder; no guard authority before success |
| SGRD-07 | Token moves versus copies | Move preserves one release owner; Copy rejects |
| SGRD-08 | Borrowed token is still needed by nested use | Block conflicting release/retirement |
| SGRD-09 | Returned/captured view still needs guard-backed authority | Guard remains live; no release or outliving borrow |
| SGRD-10 | Same execution recursively acquires without protocol support | Reject; thread identity is not reentrancy permission |
| SGRD-11 | Checked helper borrows an existing token | Bounded nested use; no second release owner |
| SGRD-12 | Successful acquire follows matching actual release | Import the primitive's stated ordering, not a method-name convention |
| SGRD-13 | Multiple readers follow a completed writer epoch | Each imports required writer publication |
| SGRD-14 | Reader releases a token | Do not invent a writer modification/publication |
| SGRD-15 | Arbitrary callback is assumed nonreentrant/noncapturing | Require its actual effects, token and capture contract |
| SGRD-16 | Synchronized wrapper hides nonshareable mutable state | No blanket field-shareability rule; check all access paths |
| SGRD-17 | Guard protects thread-affine state | No remote-use or cleanup permission added by exclusion |
| SGRD-18 | Guard crosses suspension/handoff with no admitted extent | Reject missing continuation/release/affinity/progress contract |
| SGRD-19 | Protocol explicitly admits a suspension extent | Preserve token, loans, responsibility and actual placement |
| SGRD-20 | Guarded body throws/unwinds | Release once after dependent uses under checked release contract |
| SGRD-21 | ListWriteAccess slot guard is treated as a mutex | Reject missing cross-thread protocol |
| SGRD-22 | Token covers another region/generation/guard identity | Reject conflicting protected access or fabricated release |

## Shared scalar access and effects

| ID | Scenario | Required outcome |
|---|---|---|
| SACC-01 | Ordinary shared scalar path is explicitly permitted | Preserve actual width/alignment/non-tearing/coherence and lifetime guarantees |
| SACC-02 | Competing shared x++ is used without compound/exclusive contract | Reject; load/add/store is not an RMW |
| SACC-03 | Explicit atomic operation lacks current shared/write authority | Reject; operation names/order do not acquire permissions |
| SACC-04 | Target cannot meet the requested width/representation contract | Reject before use; no torn fallback |
| SACC-05 | Incompatible overlapping-width access is introduced | Require separately admitted compatible protocol |
| SACC-06 | Invalid/nonconstant memory order is requested | Reject invalid selected operation contract |
| SACC-07 | Checked RMW arithmetic overflows | No committed update; retain its defined failure/ordering contract |
| SACC-08 | Native/lock-backed participants use different coordination | Reject unsupported mixed protocol |
| SACC-09 | Shareable/readonly field is assumed stable or fresh | No value-numbering/freshness guarantee without actual input-state protocol |
| SACC-10 | Helper is pure versus acquiring/synchronizing itself | Only the actual pure read/effect contract qualifies |

## Captures, views, tasks and guarded joins

| ID | Scenario | Required outcome |
|---|---|---|
| SHOLD-01 | Captured view requires actual guard/handoff sources | Preserve them through actual last use and release |
| SHOLD-02 | Task<T> owns completion or T is eligible | No automatic task crossing/shareability |
| SHOLD-03 | Task terminal/payload observation occurs | Does not independently release another guard/return reservation |
| SHOLD-04 | NoAlias is used as post-return handoff/exclusion | Reject invented authority extent |
| SHOLD-05 | ForwardsBacking is used across publication/return | Preserve actual guard, lifetime and publication duties |
| SHOLD-06 | Owner moves while an alias needs its former inline storage | Reject invalidation; allocation/backing and slot identity differ |
| SHOLD-07 | Field/parent cleanup would invalidate a surviving shared dependency | Reject schedule; no hidden topological reorder |
| SHOLD-08 | Callback retains data without an admitted destination | Reject capture rather than silently retaining a guard |
| SHOLD-09 | Pending output/version responsibility joins guarded states | Preserve every operation, destination generation and path duty |
| SHOLD-10 | One path is available and another remotely reserved | No unconditional ordinary authority at the join |
| SHOLD-11 | Cleanup continuation crosses incompatible affinity | Reject missing placement; no executor inheritance |
| SHOLD-12 | Shared/captured use outlives code/load scope | Preserve retained code/load source or reject |
| SHOLD-13 | Erasure/wrapper drops a guard/protocol relationship | Reject incompatible projection |
| SHOLD-14 | Return/publication endpoint ends but a resulting view remains | Keep that result's independent backing/guard obligations |

## Checking, runtime failure and trust

| ID | Scenario | Required outcome |
|---|---|---|
| SFAIL-01 | Admitted handoff/publication/guard/scalar protocols compose | Check their actual identities, permissions, endpoints and effects |
| SFAIL-02 | Available arbitrary synchronization body lacks admitted proof | Do not infer safe primitive semantics |
| SFAIL-03 | Ordinary safe wrapper uses explicit internal unsafe work | Retain checked exported contract and implementation responsibility; no extra trust marker |
| SFAIL-04 | Unsafe callable exposes its caller obligations | Require explicit unsafe call context and actual obligations |
| SFAIL-05 | Attribute/signature/provenance is treated as implementation proof | Reject conflation with checked facts or explicit trust |
| SFAIL-06 | Safe source authority violation is known/unestablished | Required diagnostic, not optional warning or silent repair |
| SFAIL-07 | Permitted runtime contention is returned as false | Follow declared Try semantics; unsupported capability is not contention |
| SFAIL-08 | Failure occurs after successful guard adoption | Preserve/release the actual affine holder once on permitted exits |
| SFAIL-09 | Failure occurs after committed handoff adoption | Preserve receiver/transit responsibility; no source revival |
| SFAIL-10 | Trusted artifact lacks a required rule/profile/import | Required rejection still applies |
| SFAIL-11 | Consumer cannot trust implementation truth | Perform required semantic audit or decline execution |
| SFAIL-12 | Unsafe access-contract violation occurs | No invented synchronization, unrelated traps or arbitrary whole-program data-race UB; not unsafe-code containment proof |

## Retained contract and independent admission

| ID | Scenario | Required outcome |
|---|---|---|
| SADM-01 | Consumer lacks capability/protocol rule version | Reject required semantics before use |
| SADM-02 | Source logical approval is used to set a physical bit/opcode | No physical allocation or runtime helper ABI follows |
| SADM-03 | Mixed-SC/fence/causality/lifetime profile remains incomplete | No complete concurrent-execution conformance claim |
| SADM-04 | Carrier/root/call representation cannot transport required authority | Reject crossing; no boxing/pinning/retain fallback |
| SADM-05 | Linking remaps type/formal/protocol/extent/generation identities | Preserve exact relationships, not names or row ordinals |
| SADM-06 | Replacement adds condition/caller obligation | Reject relied-on caller-domain restriction |
| SADM-07 | Replacement removes a relied-on thread capability | Reject |
| SADM-08 | Replacement weakens guard/return/ordering/failure promises | Reject incompatible retained contract |
| SADM-09 | Code/load/dependency endpoint changes | Preserve actual retained source obligations or reject |
| SADM-10 | Producer truth is trusted while use is structurally invalid | Reject use; trust is not a capability bypass |
| SADM-11 | Stronger private conditions change generic binding | No new inference evidence, conformance or overload preference |
| SADM-12 | Finite supplied-fact authority model passes | Not source compiler, arbitrary algorithm, concurrent event model, collector or physical-codec conformance |
