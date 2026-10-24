# Interchange resource-accounting conformance

These cases apply [resource accounting](../representation/resource-accounting.md),
[consumer verification](../representation/reader-trust.md), [canonicality](../representation/encoding-canonicality.md)
and the exact `trrm/1-draft-layout-queries-1` schema. Numerical limits in fixtures
are selected test envelopes, not portable minima. Finite action/graph models
do not certify an arbitrary verifier, resource codec or production consumer.
Cases involving malformed metadata state producer violations and require
consumer rejection before affected use under mandatory verification.
Optional audits account only for work they actually perform.

## 1. Envelopes and portable capacity

| Case | Input or observation | Required outcome |
|---|---|---|
| IREN-01 | No standard numerical minima have been assigned | Report actual selected capacity; no inferred baseline number |
| IREN-02 | Consumer supplies an explicitly larger or constrained envelope | Use and report the actual envelope without changing artifact semantics |
| IREN-03 | Placeholder or fixture limits are proposed as portable minima | Requires independent workload/profile selection; no current numerical claim |
| IREN-04 | Valid encoded object exceeds a selected capacity | Decline capacity, not a malformed-artifact verdict |
| IREN-05 | Selected object is exactly at each applicable limit | Permit the action if its other obligations hold |
| IREN-06 | Producer hint/trust/identity requests a larger budget | Does not override the selected environment envelope |
| IREN-07 | Header opens an artifact containing a large skipped optional section | Encoded artifact size includes it; no decoded copy or payload-work charge |
| IREN-08 | Two retained views overlap in the same backing generation | Charge their interval union, not two full copies |
| IREN-09 | Equal bytes have distinct backing generations | Charge each retained backing separately |
| IREN-10 | External payload is excluded from totalLength | Separate admitted resource contract needed; affected request still has aggregate limits |
| IREN-11 | Capacity declarations contain negative values or missing recipe identity | Configuration failure; no implicit unlimited/default envelope |
| IREN-12 | Resource policy is offered as an opcode/Feature/profile extension | No allocation, schema or executable-membership change |

## 2. Counts, extents and derived inventories

| Case | Input or observation | Required outcome |
|---|---|---|
| IRBO-01 | Producer emits a directory count whose count-times-16 extent exceeds the section | Verification rejects before affected use |
| IRBO-02 | Producer omits entries from a count-plus-one variable index | Verification rejects before affected use |
| IRBO-03 | Producer emits a fixed row count/stride product beyond its section | Producer violates the selected schema; capacity cannot make it valid |
| IRBO-04 | A valid artifact has selected row offsets while another row is unvisited | Mandatory verification validates required index framing/ownership before use; a deeper audit of unrelated semantic claims reports only its actual scope |
| IRBO-05 | Producer emits offset/length/alignment values outside their containing domain | Verification rejects before affected use |
| IRBO-06 | Table rows cross 255/256 and 65,535/65,536 | Preserve fixed reference/index widths and independent capacities |
| IRBO-07 | Present count reaches the 24-bit maximum or next value | Maximum remains representable if other framing holds; next value is invalid |
| IRBO-08 | Zero list or eligible empty Blob is supplied | Preserve its valid empty form and zero payload charge |
| IRBO-09 | Producer emits an empty String/semantic row without required framing | Verification rejects before affected use |
| IRBO-10 | Producer emits a variable-size element count without enough containing bytes | Producer violates the selected schema; no speculative-allocation check is mandated |
| IRBO-11 | Flattened receiver/formal inventory or a HomeMap reaches/exceeds the `u16` index domain | Permit at most 65,536 local/formal slots; reject overflow rather than widening or wrapping |
| IRBO-12 | Body maps, fixed Block entries, record offsets/data or code bytes approach the row extent | Account the `17 + 4*popcount(mapMask)` stream prefix, present map payloads (including four-byte FieldMap tokens), fixed Block sizes, `(recordCount+1)*4` offsets, record extent and `codeByteLength` without wrapped products |
| IRBO-13 | A Body-local index equals `0xFFFF` | Treat it as an ordinary usable index when its inventory count is 65,536; it is not an absence or offset sentinel |
| IRBO-14 | A valid selected reference is used under an envelope | It has the schema-assigned namespace, nullability and reserved-field meaning |
| IRBO-15 | Nested row or Body would reset root work | Same root ledger; only per-object capacity is local |
| IRBO-16 | A complete traversal exceeds its remaining capacity/work | Explicitly decline; no skipped-element completion claim |
| IRBO-17 | FieldPath row or `TargetLayoutDependencyField` range is traversed | Charge row bytes and ordered FieldReference steps to the owning artifact/root budget; use checked 24-bit row bounds and `u32` extents |
| IRBO-18 | Repeated Bodies reuse an identical global FieldPath row | Reuse encoded row storage where allowed, but do not merge query occurrences or their exclusively owned `004A` observation ranges |

## 3. Strings and expansion

| Case | Input or observation | Required outcome |
|---|---|---|
| IRTX-01 | ASCII/UTF-8/UTF-16LE/UTF-32LE rows use the same unit count | Check exact one/two/four-byte content, terminator and aligned row size |
| IRTX-02 | UTF-8 supplementary scalar or UTF-16 surrogate pair is decoded | Unit count is not scalar/output-unit count |
| IRTX-03 | Transcoding output is exactly at or above selected capacity | Check exact output/bound before append; at-limit succeeds, excess declines |
| IRTX-04 | Decoder uses a counting pass then a writing pass | Both passes consume their action expansion |
| IRTX-05 | Streaming text validation does not retain decoded content | No mandatory whole decoded buffer, but complete actual work remains charged |
| IRTX-06 | Producer supplies a name/path/key with malformed encoding or embedded zero | Verification rejects affected use before binding |
| IRTX-07 | Unrelated String has malformed scalar content | Selected lookup does not traverse it; no claim of whole-String semantic validity |
| IRTX-08 | String unitCount is maximum at each width | A valid producer encodes full product/header/terminator/alignment without truncation |
| IRTX-09 | Producer pairs a large String count with a short actual row | Verification rejects before affected use |
| IRTX-10 | Copy/re-encoding retains source while building output | Simultaneous charges; no early-retirement accounting shortcut |
| IRTX-11 | Tiny input requests an unsupported expansion recipe | Decline affected use; no uncharged helper or new codec |
| IRTX-12 | Raw/ill-formed literal or segmented String permission is inferred from capacity | Remains unassigned; resource policy is not semantic permission |

## 4. Live state and lifetime

| Case | Input or observation | Required outcome |
|---|---|---|
| IRME-01 | A request retains input, scratch and output concurrently | Account each applicable live dimension and peak |
| IRME-02 | Copied buffers have identical bytes | Independent constructed payload charges |
| IRME-03 | Wire-omitted descriptors are materialized | Their logical state entries count |
| IRME-04 | A record contains scalar fields plus nested entries/payload | Use the exact recipe shape, not native object headers |
| IRME-05 | Building output would exceed live capacity | Reserve before construction; no partial published result |
| IRME-06 | State is retired at its last supporting use | Release live charge, never refund work |
| IRME-07 | View outlives the original input handle | Retain the backing charge until all supporting holders end |
| IRME-08 | Same exact logical object/context is deduplicated | Sharing allowed only under its recipe identity/lifetime |
| IRME-09 | Same row number occurs in different module/substitution contexts | Do not merge state or obligations by raw row ID |
| IRME-10 | Child output is transferred to a parent scope | Charge remains live in the applicable root/aggregate ledger |
| IRME-11 | Independent requests select an aggregate cap | Enforce root limits and the explicitly selected shared cap |
| IRME-12 | Failed reservation or repeated retirement is presented as successful state | No reservation publication; broken retirement invariant is a tool failure |
| IRME-13 | Live entry/payload counters reach their limits | Inclusive capacities; nonwrapping complete reservation preflight |
| IRME-14 | Logical entries/bytes are offered as measured host allocations or throughput | Requires independent implementation measurement |

## 5. Work, graph depth and recipe identity

| Case | Input or observation | Required outcome |
|---|---|---|
| IRWK-01 | Elementary field/element/check action examines bytes | Charge one action plus every examined/copied/generated byte in its named phase |
| IRWK-02 | Bulk list/copy/comparison hides elements behind one helper call | Expose the complete elementary expansion |
| IRWK-03 | Same recipe/inputs use physical caching or parallel execution | Same logical action charges; host execution strategy is not the recipe |
| IRWK-04 | Same semantic work repeats after a physical cache hit | Charge its full repeated expansion unless valid completed-decision reuse discharges it |
| IRWK-05 | Completed decision is reused in unchanged context | Check and record reuse basis; retained state still counts |
| IRWK-06 | In-progress pair or changed context is offered as reused success | No certificate; complete closure still required |
| IRWK-07 | Same complete graph pair is encountered again | Deduplicate local expansion, but account actual edge/lookup actions |
| IRWK-08 | Finite anchored cycle has a mismatch behind its root | Cannot accept before every required local/edge obligation succeeds |
| IRWK-09 | Iterative audit has many simultaneously active logical expansions | Depth/state limits still apply; native stack avoidance is not free capacity |
| IRWK-10 | Structural decoding occurs during a semantic audit | Charge structural and semantic counters separately; neither borrows the other's budget |
| IRWK-11 | Recipe has no expansion for an intrinsic or required rule | Decline metered use/claim, not guessed one-step cost |
| IRWK-12 | Finite resource graph model is offered as mandatory consumer proof of arbitrary predicate/body truth | Do not require such proof for safe consumption; producer truth remains required, and any optional audit must state its scope |
| IRWK-13 | Different algorithm/recipe is compared under the same numeric envelope | Report actual recipe; no cross-recipe deterministic-result guarantee |
| IRWK-14 | Loader accounting is offered as replacing the source proof/ConstEval machine | Separate unchanged source contracts remain required |

## 6. Failure and reporting scopes

| Case | Input or observation | Required outcome |
|---|---|---|
| IRSC-01 | Structural work, semantic work, depth or state would exceed a limit | Explicit dimension/scope refusal before the action |
| IRSC-02 | Required audit exhausts after checking a proper subset | No complete-scope certificate or fallback execution |
| IRSC-03 | Selected seek fits while an unvisited block would not | Seek can succeed; no full-body resource/validation claim |
| IRSC-04 | Whole-Body/artifact claim is requested after selected access only | Decline the overbroad claim |
| IRSC-05 | Producer responsibility is offered after exposed invalidity or exhaustion | Does not turn either into successful validation |
| IRSC-06 | Counter arithmetic or charge is negative/overflowing | Never wrap; invalid accounting/configuration is distinct from input validity |
| IRSC-07 | Cancelled/OOM/I/O-failed work reports deterministic budget exhaustion | Report actual host failure, no reproducible envelope-result substitution |
| IRSC-08 | Receipt omits actual envelope, recipe, peak or checked scope | Insufficient resource/validation claim |
| IRSC-09 | Resource receipt is offered as source/runtime conformance | No such implication |
| IRSC-10 | Capacity fit is offered as native execution, collector or external-resource support | Separate implementation/profile contracts required |
| IRSC-11 | Tiny explicit fixture limits are compared with unassigned minima | Fixture outcome only; no baseline-portability certificate |
| IRSC-12 | Recipe/counter metadata is proposed as an on-wire trust/Feature field | No new physical field, opcode, CLI schema or authority |
