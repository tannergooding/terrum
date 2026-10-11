# Owning-field and cleanup conformance

These cases specify [ownership and cleanup](../language/ownership-and-cleanup.md).
Fixtures supply complete type, field, authority, dependency and protocol facts.
Finite state/retirement schedules assess those facts, not arbitrary source
checking, cleanup bodies, collector behavior, concurrency or physical codecs.

## Registered cleanup and complete types

| ID | Scenario | Required outcome |
|---|---|---|
| OREG-01 | Eligible unique type selects one Cleanup method | Bind the exact registered primary method, not its name |
| OREG-02 | Ordinary Copy aggregate contains an owning field | Reject duplicate ownership or silently removing Copy |
| OREG-03 | Copy type declares owning Cleanup | Reject |
| OREG-04 | Cleanup returns non-void or has parameters | Reject automatic cleanup eligibility |
| OREG-05 | Cleanup is static, async, extern or caller-unsafe | Reject automatic cleanup eligibility |
| OREG-06 | Cleanup body or automatic child cleanup may throw/suspend | Reject the transitive retirement contract |
| OREG-07 | Same type selects two Cleanup methods/repeated attributes | Reject |
| OREG-08 | Secondary partial declaration changes Cleanup selection | Reject primary-contract change |
| OREG-09 | User calls the selected Cleanup method directly | Reject a second destruction path |
| OREG-10 | Type has no authored Cleanup | Derive field retirement; do not discover Close/Dispose by name |
| OREG-11 | Cleanup or field types are substituted generically | Preserve complete type, field and selected operation identity |
| OREG-12 | Cleanup member is not publicly accessible | Retirement uses its registered identity without exposing ordinary access |

## Construction and acquired state

| ID | Scenario | Required outcome |
|---|---|---|
| OCON-01 | Field storage starts vacant | No live default, owning resource or zero-data publication |
| OCON-02 | Required fields initialize out of declaration order | Record actual outstanding acquisition order |
| OCON-03 | A conditional initializer is skipped | No acquired value or cleanup for that field |
| OCON-04 | Construction fails after several complete children | Retire remaining children in reverse acquisition order |
| OCON-05 | All fields are live but final validation fails | No parent Cleanup or published complete parent |
| OCON-06 | Complete child was acquired by a partial parent | Child runs its own complete Cleanup when parent construction fails |
| OCON-07 | Child itself is partially constructed | Unwind its acquired subset without its complete Cleanup |
| OCON-08 | Unpublished field is replaced before parent completion | Retire old child once; failure later retires only the current acquisition |
| OCON-09 | Constructing parent transfers a complete child elsewhere | Source field vacant; later failure must not clean the transferred child |
| OCON-10 | Constructor returns before declared caller init entries | Remain unpublished until full storage/validation completion |
| OCON-11 | Declaration/body/caller initializer or clause fails | Preserve its actual phase and acquired-state cleanup |
| OCON-12 | Whole partial parent escapes as ordinary T | Reject missing complete-value gate |
| OCON-13 | One child is live while its sibling remains vacant | Child use can be valid; no whole-parent read |
| OCON-14 | Repair completes an already-published partial structural owner | Establish affected validity; no constructor/initializer replay |
| OCON-15 | Later code fails after publication | Retire the complete responsible value, not failed-construction storage |
| OCON-16 | Fail-fast/process termination occurs | No ordinary unwind or Cleanup promise |
| OCON-17 | Partial child's acquisitions interleave with parent fields | Reverse actual outstanding acquisitions across partial storage, not field-start order |
| OCON-18 | Interleaved child becomes a complete owned value before parent failure | Its complete boundary groups its obligations; run its complete cleanup rather than a partial parent body |

## Field moves and repair

| ID | Scenario | Required outcome |
|---|---|---|
| OMOV-01 | Live mutable field of a writable structural unique aggregate moves | Transfer once; vacate only that field |
| OMOV-02 | Fixed field or readonly containing path is consumed directly | Reject without its ordinary initialization/write authority |
| OMOV-03 | Immutable local transfers its whole unique owner | Explicit whole move is still eligible |
| OMOV-04 | Already-vacant field is moved/read/borrowed | Reject |
| OMOV-05 | Parent has derived cleanup and a moved field | Preserve remaining field obligations; parent is partial, not vacant |
| OMOV-06 | Partial aggregate is read/called/returned/moved as a whole | Reject |
| OMOV-07 | Independently live sibling is projected | Allow only with surviving place/access/dependency facts |
| OMOV-08 | Mutable vacant field is repaired successfully | Install live child and establish affected complete validity |
| OMOV-09 | Repair construction fails | Field stays vacant, siblings/extracted result remain owned |
| OMOV-10 | Parent has an authored complete-value Cleanup | Reject direct escaping field vacancy |
| OMOV-11 | Enclosing ancestor has authored Cleanup | Same complete-receiver boundary through nested fields |
| OMOV-12 | Complete moved child itself has Cleanup | Child Cleanup does not forbid transfer from a derived-cleanup parent |
| OMOV-13 | Caller uses checked take/replace on a Cleanup type | Preserve complete receiver on every permitted exit; return actual old owner |
| OMOV-14 | Transfer/repair interval can throw, suspend or expose the partial receiver | Reject missing confinement/complete-receiver proof |
| OMOV-15 | Active sum/tuple payload is partially transferred | Retain exact structural/tag contract; raw overlapping Union supplies none |
| OMOV-16 | Field move is used to invent owner/read-write/root authority | Reject; existing full contract remains required |

## Replacement and destination identity

| ID | Scenario | Required outcome |
|---|---|---|
| OREP-01 | Live field receives a new complete owning value | Construct first; retire old once; install new |
| OREP-02 | New construction fails without changing destination | Keep old value until ordinary unwind; clean only new acquired state |
| OREP-03 | Destination expression has observable evaluation | Evaluate it once before the replacement source |
| OREP-04 | Source evaluation deliberately mutates permitted state | Keep those effects; no assignment rollback/snapshot |
| OREP-05 | Maybe-live mutable field is assigned | Retire old only on live paths; publish complete new child on success |
| OREP-06 | Initialization-only output receives live/partial storage | Reject implicit retirement or treating partial state as vacant |
| OREP-07 | Replacement's backing needs old owner being retired | Reject; no hidden old-owner retention |
| OREP-08 | Old value has surviving storage or resource dependents | Reject conflicting retirement |
| OREP-09 | Old task remains pending or failure-unobserved | Reject replacing it by automatic blocking/cancel/join |
| OREP-10 | Old value was already transferred | Initialize only eligible vacant storage; do not clean old value twice |
| OREP-11 | Post-staging retirement/installation has unsupported failure | Reject contract before use; do not strand staged ownership |
| OREP-12 | Complete-value invariant is affected by replacement | Establish affected validity before exposing the whole receiver |
| OREP-13 | Replacement happens in a type with Cleanup | Keep its complete-receiver boundary, including reentrant observation |
| OREP-14 | Named replacement value is affine | Explicit move vacates source; failed later use does not restore it |
| OREP-15 | Nested replacement/take changes an enclosing complete invariant | Establish every affected ancestor's validity; a valid child alone is insufficient |

## Complete and partial retirement order

| ID | Scenario | Required outcome |
|---|---|---|
| OCLN-01 | Complete parent has authored Cleanup and owning children | Parent body once while complete, then reverse-declaration field retirement |
| OCLN-02 | Complete parent has no authored body | Reverse-declaration field retirement only |
| OCLN-03 | Acquisitions occurred in a different order | Complete retirement still follows declaration order |
| OCLN-04 | Field was replaced or repaired later | Its declaration retirement position does not change |
| OCLN-05 | Structural parent remains partial after a move | Retire only live fields, in reverse declaration order |
| OCLN-06 | Nested complete child has authored Cleanup | Child body precedes its own reverse-declaration children |
| OCLN-07 | Nested structural child remains partial | No vacant-child read/cleanup or whole-child body |
| OCLN-08 | Zero-data field is live or vacant | Preserve logical state/retirement despite absent payload bytes |
| OCLN-09 | Field holds a borrowed descriptor/GC root | Retire its own obligations, not the borrowed/shared referent |
| OCLN-10 | Parent Cleanup reads a field before automatic retirement | Field remains complete and available throughout that body |
| OCLN-11 | Parent Cleanup leaves an owning field vacant or consumes this | Reject invalid complete-receiver body |
| OCLN-12 | Parent body releases backing needed by later child retirement | Reject incompatible dependency schedule |
| OCLN-13 | Field dependencies conflict with reverse-declaration cleanup | Reject unsupported schedule; no implicit topological reorder |
| OCLN-14 | A moved/replaced/retired value reaches later unwind | No cleanup replay or owner revival |
| OCLN-15 | Authored finally/defer throws or awaits | Ordinary causal cleanup contract, not generated Cleanup eligibility |
| OCLN-16 | Last field retires before containing storage release | Preserve storage/root/affinity obligations until then |

## Dependencies and storage carriers

| ID | Scenario | Required outcome |
|---|---|---|
| ODEP-01 | Inline field is moved while a surviving ref needs its storage | Reject conflicting invalidation |
| ODEP-02 | Unique handle moves but its allocation remains valid | Preserve allocation dependencies; source slot still becomes vacant |
| ODEP-03 | Moved owner contains a pointer to unrelated storage | Do not extend or acquire ownership of that target |
| ODEP-04 | Field depends on a sibling resource | Carry dependency until its actual use/retirement endpoint |
| ODEP-05 | Published broad relationship names whole receiver | Do not narrow it by private field inspection |
| ODEP-06 | Checked precise sibling dependency is disjoint from changed field | No blanket root exclusion; actual invalidations govern |
| ODEP-07 | Reentrant access observes an incomplete Cleanup receiver | Reject |
| ODEP-08 | Carrier binding is live while its designated aggregate is partial | Carrier state test is not payload completeness |
| ODEP-09 | Class/own/val projection crosses unsupported representation | Reject; no boxing, relocation or hidden retain fallback |
| ODEP-10 | Task/guard/lease field has outstanding protocol duties | Keep independent settlement, affinity and backing before retirement |
| ODEP-11 | Partial value is kept reachable by a collector | Roots alone neither restore validity nor perform resource settlement |
| ODEP-12 | NoAlias, write or unique is treated as another capability | Preserve distinct separation/access/ownership contracts |

## Flow, joins and exceptional holders

| ID | Scenario | Required outcome |
|---|---|---|
| OFLO-01 | One path moves a field and another keeps it live | Retain guarded field state/ownership; no unconditional whole-value use |
| OFLO-02 | Both paths restore all fields and checked validity | Whole complete use can be established |
| OFLO-03 | Inline aggregate is init tests false on partial state | Do not infer empty storage or permit whole init overwrite |
| OFLO-04 | State guard selects one vacant field | Repair only that eligible field, not unrelated obligations |
| OFLO-05 | Loop reuses a field through move/repair | Preserve finite per-place joins and exact current value generations |
| OFLO-06 | Later argument fails after a field owner was staged | Staged holder cleans/transfers it; old field is not restored |
| OFLO-07 | Callee consumes an extracted value and fails | Callee retains its actual exceptional responsibility |
| OFLO-08 | Saved return survives body cleanup then postcondition fails | Clean only still-owned saved value, without replay |
| OFLO-09 | Published field/output value survives another holder's failure | Retain its actual state/dependencies; no transaction rollback |
| OFLO-10 | Pending task/output reservation crosses a join | Preserve independent operation/owner/version guards; no fabricated completion |

## Retained identity and admission

| ID | Scenario | Required outcome |
|---|---|---|
| OADM-01 | Consumer lacks owning-fields-and-cleanup/1 | Reject required rule before use |
| OADM-02 | Link remaps field/type/cleanup/storage records | Preserve declaration identity/order and actual value generations |
| OADM-03 | Storage numeric/name identity is reused | Old retired value and acquired-state journal cannot revive |
| OADM-04 | Replacement adds authored Cleanup to a structurally movable type | Reject relied-on partial-move contract incompatibility |
| OADM-05 | Replacement changes observable field-retirement order | Reject incompatible retained contract |
| OADM-06 | Replacement weakens cleanup effects or complete-receiver guarantee | Reject |
| OADM-07 | Schema/body annotation is used as proof of arbitrary cleanup | Require checked evidence or explicit semantic producer/audit trust |
| OADM-08 | Layout/GC/native profile cannot transport required partial state | Reject crossing before use; no new type/hidden representation fallback |
| OADM-09 | Existing Move/Drop or held range encodes missing partial-state semantics | No physical ownership program/profile activated by logical approval |
| OADM-10 | Finite construction/cleanup model passes | Not compiler/runtime, arbitrary body/validity, concurrent, collector or codec conformance |
