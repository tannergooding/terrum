# Concurrent execution conformance cases

These obligations apply to [concurrent-executions/1](../runtime/concurrent-executions.md),
not an implemented compiler/runtime or an imported C++/ISA model.
Unless specified otherwise, locations are valid initialized bool or int32
carriers in ordinary coherent CPU memory, with exact compatible non-tearing
access, authority and lifetime contracts. Initial bool values are zero.
Programs below are source-order pseudocode.

An **allowed** outcome needs actual read sources and modification orders,
grounded source facts, required access order and a consistent SC order.
A **forbidden** outcome fails at least one of those checks. Allowed does not
mean every target must realize it. A **rejected operation/profile** lacks an
admitted contract; it is not another successful memory outcome.
Each assessment must identify its actual finite instances and distinguish
document-only obligations from executable source or target conformance.

## Location and event identity

| Case | Scenario | Required outcome |
|---|---|---|
| CLOC-01 | Distinct generations have equal addresses and representation | Distinct locations and modification orders; reuse still requires actual retirement |
| CLOC-02 | Admitted ordinary and explicit atomic accesses share one exact location | One coherence order, not separate ordinary/atomic histories |
| CLOC-03 | Access uses an unadmitted width, alignment or observer scope | Reject before attempting the operation |
| CLOC-04 | Successful RMW is represented with read and write ports | One indivisible event with the immediate modification predecessor |
| CLOC-05 | Failed arithmetic or CAS attempts have read ports only | No modification-order entry or release write |
| CLOC-06 | Two stores write identical bits | Distinct modifications; equal bits do not identify the source |
| CLOC-07 | Read claims an initial default without actual initialization | Reject the candidate |
| CLOC-08 | Aggregate or overlapping-width accesses lack an exact compatible whole-value contract | No invented whole-value snapshot/source |
| CLOC-09 | Event/debug names are remapped without changing identity relationships | Allowed observations are invariant |
| CLOC-10 | New storage initialization depends on unresolved concurrent reads | Creation does not make its bits a causal seed |

## Coherence

| Case | Scenario | Required outcome |
|---|---|---|
| CCOH-01 | Two modifications are ordered by hb | Preserve that order in mo |
| CCOH-02 | A write happens before a read | The read cannot select an older source |
| CCOH-03 | A read happens before a write | The source strictly precedes that write in mo |
| CCOH-04 | Two reads of one location are hb-ordered in one execution | The second cannot go backwards in mo |
| CCOH-05 | Two reads are hb-ordered through an admitted cross-execution endpoint | The same monotonic-source rule applies |
| CCOH-06 | RMW selects a non-immediate predecessor | Reject even if its arithmetic result is otherwise correct |
| CCOH-07 | RMW selects the immediate predecessor and valid update | Preserve one indivisible modification |
| CCOH-08 | A modification precedes the actual initialization in mo | Reject |
| CCOH-09 | A protocol excludes competing writers and establishes its last write by hb | Read that write |
| CCOH-10 | No edge establishes a newer competing write before repeated polls | Older coherent observations remain possible; no freshness promise |

## Matched communication

| Case | Scenario | Required outcome |
|---|---|---|
| CPUB-01 | Acquire reads directly from a release write | Establish the actual sw edge and publish prior writes |
| CPUB-02 | Acquire reads initialization instead of the release | No publication from that release |
| CPUB-03 | Acquire reads a relaxed same-bits store rather than a release | Bits alone do not publish the release's payload |
| CPUB-04 | Release fence precedes the carrier write and an acquire reads it | Fence/access publication |
| CPUB-05 | Release write is read by a relaxed read followed by an acquire fence | Access/fence publication |
| CPUB-06 | Release fence/write and matching read/acquire fence use relaxed carriers | Fence/fence publication |
| CPUB-07 | The same fence pair uses admitted ordinary non-tearing carriers | The same publication contract |
| CPUB-08 | Fences have no matching carrier read source | No fabricated sw edge |
| CPUB-09 | Carrier source comes from another location or ended generation | Reject rather than publish |
| CPUB-10 | Relaxed RMW reads a release and a later acquire reads that RMW's write | No release-sequence relay |
| CPUB-11 | AcquireRelease RMW reads a release and republishes through its own write | Explicit incoming and outgoing edges publish transitively |
| CPUB-12 | A failed CAS/arithmetic read has an acquire side | It may import its actual source's release |
| CPUB-13 | Failed CAS/arithmetic is followed by another reader | No publication through an unstored write |
| CPUB-14 | Start, completion or guard primitive has an admitted exact endpoint | Use only the supplied endpoint's promised edge |
| CPUB-15 | Task/status/cancellation/notification is observed without such a primitive contract | No automatic memory or authority-return edge |

## Access order

For store buffering (SB), A stores x then reads y; B stores y then reads x.
For load buffering (LB), A reads y then stores x; B reads x then stores y.
Unconditional stores write one. All accesses are relaxed unless stated.

| Case | Scenario | Required outcome |
|---|---|---|
| CORD-01 | Relaxed SB, both reads zero | Allowed |
| CORD-02 | SB uses acquire loads but relaxed stores | Both zero still allowed |
| CORD-03 | SB uses release stores and acquire loads, both read initialization | Both zero still allowed |
| CORD-04 | Relaxed independent-store LB, both reads one | Allowed with grounded store facts |
| CORD-05 | LB loads acquire and stores are relaxed | Both one forbidden by required access/delivery order |
| CORD-06 | LB stores release and loads are relaxed | Both one forbidden by required access/delivery order |
| CORD-07 | Acquire fence lies between accesses | Preserve read/read and read/write coverage |
| CORD-08 | Release fence lies between accesses | Preserve read/write and write/write coverage |
| CORD-09 | AcquireRelease fence lies between store and load | Do not add a full store-to-load boundary |
| CORD-10 | Sequential full fence lies between accesses | Preserve all four read/write combinations |
| CORD-11 | Sequential RMW attempt lies between accesses | Preserve full ordering around the attempt, even on read-only failure |
| CORD-12 | CompilerFence is used without a CPU synchronization operation | No CPU publication, SC participation or full boundary |
| CORD-13 | Access existence/address/value depends on an unresolved read | Preserve the necessary dependency |
| CORD-14 | An all-input proof establishes the access fact's independence | It may remove that dependency, not independently requested ordering |

## Mixed SC and boundaries

IRIW uses independent release writes x = 1 and y = 1; C reads x then y;
D reads y then x. The opposite outcome is C = (1, 0), D = (1, 0).

| Case | Scenario | Required outcome |
|---|---|---|
| CSEQ-01 | All four SB accesses are Sequential | Both zero forbidden |
| CSEQ-02 | Relaxed SB has a Sequential fence between each store/load | Both zero forbidden |
| CSEQ-03 | Those fences are only AcquireRelease | Both zero may remain allowed |
| CSEQ-04 | Relaxed SB has strong RMWs on other locations between each store/load | Both zero forbidden |
| CSEQ-05 | Those strong RMW attempts fail comparison or checked arithmetic | The full-boundary prohibition still holds |
| CSEQ-06 | IRIW readers use acquire | Opposite observations allowed |
| CSEQ-07 | All four IRIW reader loads are Sequential but writers remain release | Opposite observations forbidden |
| CSEQ-08 | IRIW writers and readers are all Sequential | Opposite observations forbidden |
| CSEQ-09 | A mixed SC read chooses an older source than an SC modification ordered before it | Reject through the exact coherence/SC edges |
| CSEQ-10 | A mixed SC read selects a permitted later non-SC modification | Do not require every non-SC write to enter S |
| CSEQ-11 | SC participants are ordered by ordinary hb through non-SC communication | Preserve that order in S |
| CSEQ-12 | Both endpoints of an eco pair are SC accesses | Project access/access order into S |
| CSEQ-13 | First eco endpoint is SC and second precedes an SC full boundary | Project access/boundary order into S |
| CSEQ-14 | SC full boundary precedes first eco endpoint and second is SC | Project boundary/access order into S |
| CSEQ-15 | SC full boundaries surround both endpoints of an eco pair | Project boundary/boundary order into S |
| CSEQ-16 | The required SC projections cycle or demand a self-edge | No chosen total order repairs the candidate |

## Atomic results and failure

| Case | Scenario | Required outcome |
|---|---|---|
| CFAIL-01 | Strong CAS matches the exact comparison bits | Successful update, not spurious failure |
| CFAIL-02 | Strong CAS mismatches those bits | Read-only failure with its selected read order |
| CFAIL-03 | Default strong CAS fails | SC read and full boundary, no release write |
| CFAIL-04 | Each of the nine accepted CAS order pairs is selected | Admit only that pair's actual success/failure effects |
| CFAIL-05 | Any other success/failure pair, including release failure, is selected | Reject before a memory attempt |
| CFAIL-06 | CAS compares +0 versus -0 or distinct NaN encodings | Differing defined bits mismatch; no user equality call |
| CFAIL-07 | Comparison representation has unspecified padding | Reject unless an independently admitted defined comparison representation resolves it |
| CFAIL-08 | Checked arithmetic overflows with Relaxed | Retain a relaxed read, no modification |
| CFAIL-09 | Checked arithmetic overflows with Acquire | Retain an acquire read, no modification |
| CFAIL-10 | Checked arithmetic overflows with Release | Retain a relaxed read, no release or modification |
| CFAIL-11 | Checked arithmetic overflows with AcquireRelease | Retain an acquire read, no release or modification |
| CFAIL-12 | Checked arithmetic overflows with Sequential | Retain SC acquire/full-boundary effects, no modification |
| CFAIL-13 | Staging/precondition/admission fails before attempting memory | No fabricated read or ordering boundary |
| CFAIL-14 | Explicit wrapping arithmetic is selected | Compute the wrapped update with unchanged order/authority rules |
| CFAIL-15 | int32 subtraction computes -1 minus MinValue | Succeed with MaxValue; do not overflow by separately negating MinValue |
| CFAIL-16 | RMW has no result, previous result or updated result | Same access, modification and ordering effects |
| CFAIL-17 | Checked helper writes before detecting overflow or exposes a non-indivisible update | Not a refinement of the source no-update RMW |
| CFAIL-18 | Checked failure follows an actual acquiring read | Preserve imported effects, prior argument effects and ordinary exceptional continuation |

## Causal justification

| Case | Scenario | Required outcome |
|---|---|---|
| CCAU-01 | Unconditional constant stores in relaxed LB | Their existence/address/bits can be justified before unresolved read values |
| CCAU-02 | Each LB store instead copies its preceding read's value | Reject unseeded cyclic one, while independently sourced zero remains possible |
| CCAU-03 | Each store(1) is conditional on its read being one | Reject the circular store guard |
| CCAU-04 | Each store's address is selected by the read it would justify | Reject the circular address |
| CCAU-05 | A store uses constant bits but has an unresolved failure/branch guard | Constant bits do not justify existence |
| CCAU-06 | The full bool input domain proves r xor r is zero | Permit exact semantic independence |
| CCAU-07 | A claim of independence is true only for the selected read outcome | Reject that claim |
| CCAU-08 | A prior valid nonthrowing read's result is unused | Its normal continuation can be established without guessing the value |
| CCAU-09 | A prior read/evaluation may fail or has relevant effects | Do not drop that continuation gate |
| CCAU-10 | Floating subtraction or checked arithmetic is treated like integer cancellation | Require exact all-input value/failure semantics |
| CCAU-11 | Read selects a hypothetical or absent write | Reject even if its bits solve the final equations |
| CCAU-12 | A genuine independent input seeds actual store bits | Permit the derivation if all other rules hold |
| CCAU-13 | An unselected initial/same-bits source is offered as a cyclic read's proof | It cannot replace actual rf |
| CCAU-14 | Dynamic initialization depends on an unresolved cyclic read | No automatic ground seed |
| CCAU-15 | Arbitrary shared mutable state is labeled an independent supplied input | Not justified by that label |
| CCAU-16 | Independence uses an empty invented input domain | No vacuous proof |
| CCAU-17 | A finite derivation uses established rules with strictly earlier premises | Accept the grounded facts, not arbitrary cyclic assumptions |
| CCAU-18 | Causality is approximated by acyclic sb plus rf | Do not use that stronger rule to reject independent-store LB |

## Lifetimes and reuse

| Case | Scenario | Required outcome |
|---|---|---|
| CLIFE-01 | Actual initialized location is published before its admitted shared read | Preserve valid initialization hb use |
| CLIFE-02 | Shared use lacks required completed initialization/publication | Reject |
| CLIFE-03 | Read source comes from an earlier storage generation at equal bytes | Reject |
| CLIFE-04 | Ordinary write or successful RMW updates live storage | Keep its existing generation and mo |
| CLIFE-05 | Descriptor rebinding changes referent identity | Preserve slot/referent distinction and every retained source obligation |
| CLIFE-06 | All prior accesses end through actual checked quiescence endpoints | Retirement may follow those accesses |
| CLIFE-07 | A completed access is not ordered before claimed retirement | Reject the claimed quiescence |
| CLIFE-08 | A live loan, token or permitted in-flight use survives | Retirement is not supplied by an empty descriptor or status test |
| CLIFE-09 | Old aliases are used after same-address storage reuse | Reject; new generation does not revive them |
| CLIFE-10 | Reuse follows actual retirement and new valid initialization | Start a distinct modification order |
| CLIFE-11 | Atomic pointer removal, successful CAS or full fence is used as reclamation proof | It is insufficient |
| CLIFE-12 | Managed-reference access has CPU ordering but lacks collector/root support | Remains unadmitted |
| CLIFE-13 | Finite prefix has no retirement and its storage is still live | No forced reclamation or termination promise |
| CLIFE-14 | Cleanup accesses retain valid generations until their actual retirement | Preserve ownership cleanup order; do not retire early |

## Admission and implementation boundaries

| Case | Scenario | Required outcome |
|---|---|---|
| CADM-01 | Required access/profile identity is absent or incompatible | Reject before use |
| CADM-02 | A primitive order is unknown, invalid or not compile-time known | Reject, not a relaxed/no-op fallback |
| CADM-03 | Actual authority or compatible access extent is absent | Observation consistency does not repair it |
| CADM-04 | Logical concurrent-executions/1 is selected | No physical opcode, feature number, certificate or Attr allocation follows |
| CADM-05 | An arbitrary source/native synchronization algorithm is supplied | It still needs checked evidence or existing explicit unsafe/producer trust |
| CADM-06 | A target maps source operations to ISA/helpers | Prove refinement for exact width, order, failure, roots and progress; stronger outcomes may form a subset |
| CADM-07 | Optimization removes a result or changes access shape | Preserve every observable access/order/failure/lifetime effect |
| CADM-08 | A finite permitted-observation assessment succeeds | No freshness, fairness, wait-freedom or complete execution proof |
| CADM-09 | Unsafe code violates an access contract | No repair and no arbitrary whole-program data-race UB assumption |
| CADM-10 | Replacement weakens relied-on ordering/publication/failure/lifetime facts | Incompatible retained contract; no silent reinterpretation |
