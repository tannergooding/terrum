# Interchange resource accounting

## 1. Capacity, validity and portable minima

A resource envelope states the capacity offered for a selected loading,
binding, transformation or audit operation. It is not a byte-schema revision,
artifact-validity rule, producer obligation or execution permission.
A representable artifact can exceed a consumer's supported capacity; a small
artifact can require substantial semantic analysis.

Portable minimum supported envelopes are the intended conformance baseline.
Implementations may offer declared higher limits. An explicitly selected
constrained envelope is reported as such rather than silently borrowing the
baseline or claiming its guarantees. A minimum is not a fixed maximum,
mandatory allocation or scan, or a promise to verify every artifact below
one encoded-size threshold.

**Numerical portable minima are unassigned.** The following dimensions need
workload and implementation evidence before their values are selected:

| Envelope | Required dimensions | Portable minimum |
|---|---|---|
| Structural capacity | Artifact/section/row bytes, directory/table counts, list and Body-local inventories, String units and supported interpretation depth | Unassigned |
| Constructed processing state | Live retained input, copied/decoded/output payload bytes and logical state entries | Unassigned |
| Structural work | Deterministic access/copy actions for a selected operation or optional audit | Unassigned |
| Semantic audit | Deterministic rule actions, graph expansion, live facts/obligations and depth | Unassigned |

An implementation declares its actual supported envelope and accounting
recipes now. It cannot describe an invented number, zero, format maximum
or placeholder as the standard minimum. A future selected minimum will
apply to its named profiles, dimensions and recipes; a consumer offering
less cannot claim that baseline. No numerical minimum conformance claim
is currently available.

The existing [source proof and constant-execution envelope](../language/proof-and-constant-evaluation.md#portable-resource-envelope)
is separate and unchanged. Its numerical minima, step machine, frame/state
charges and failure priority are not loader or verifier defaults.

## 2. Selection and scope

Before resource-consuming work, select the operation, exact encoding/rule
editions, accounting recipes, envelope and requested claim. The environment
may select an explicitly constrained envelope within the consumer's supported
capacity. Artifact bytes, producer hints, identities or trust do not select
larger limits, suppress accounting or authorize a missing recipe.

The scopes are:

| Scope | Accounting boundary |
|---|---|
| Supplied artifact | Complete `TRRM` slice length and directory-declared inventory; includes optional sections, padding and encoded nested payload, not the containing PE/ELF or external resources |
| Selected section, row, list or Body | Its encoded extent and declared or derived inventory when an operation actually uses it |
| Root request | Cumulative work and simultaneously live processing state for the requested operation and every required nested access, dependency, comparison or rewrite |
| Explicit aggregate session | An additional environment-selected shared limit for multiple root requests, if offered; not an implicit process-global language budget |

A nested row lookup, Body, Module or dependency does not receive a free new
root budget. Per-object capacity and root aggregate work/state limits both
apply. Releasing an object can reduce live-state charges; it never refunds
work. Independently requested operations have separate ledgers unless a
selected aggregate scope additionally constrains them.

Declared artifact/directory capacity can be accounted without decoding
payloads. Local capacity is based on the selected object's actual inventory.
An implementation need not scan unrelated Bodies, String contents, offsets
or slots to fill a resource ledger. Skipped payload still belongs to the
artifact's encoded size, but is not decoded state or analysis work merely
because it is present.

Externally referenced resources require their own admitted identity,
resolution, integrity, codec and resource recipe. Their bytes do not become
part of `TRRM.totalLength`, and this chapter allocates no resource-reference
encoding. Following one, when supported, still belongs to the calling
request's aggregate work and live-state scope.

## 3. Structural capacity and valid inventories

This section defines the mathematical inventories used by resource recipes
and optional deeper audits. Required structural safe-consumption checks and
support admission are owned by [consumer verification](reader-trust.md);
semantic re-proof is not required. Check organization and diagnostic details
are not prescribed.

| Object | Required preflight |
|---|---|
| Header/directory | Actual slice length; fixed header, `entryCount * 16`, directory extent and declared section inventory |
| Fixed table | Present row count, per-kind/aggregate declared counts, `count * stride` and row extents |
| Variable table | `4 * (count + 1)` index storage, data extent and selected row extents |
| Repeated list | Encoded element count, fixed-size element product and containing extent |
| Variable-size elements | Count, containing extent, element minima and complete per-element shape |
| Body | Encoded row extent, implementation kind, 33-byte stream prefix, map/Block counts, record-offset index/data and code-byte extent; validate all fixed products with checked arithmetic |
| String | Encoded unit count, unit width, header, terminator and aligned extent; decoded/output capacity is separate |

Body preflight derives bounded inventories from the encoded form; it does not
re-prove operation typing, flow, effects, ownership or other producer-
published semantic facts.

These are valid-artifact and accounting facts that consumers check before
allocating from or using the selected input, to prevent unsafe structural
consumption. Producers use the schema's actual
widths and zero rules. Fixed metadata
references remain `u32`; Body-local indexes are fixed `u16` and corresponding
counts are `u32`. There are no instruction width classes or definition-offset
sentinels. Zero list counts and eligible empty Blob rows remain
valid; empty semantic rows and unsupported form codes have no assigned
meaning.

The flattened receiver/formal inventory and every `u16`-indexed map, Home,
Block and record inventory must fit the selected 65,536-entry bound. Register
bindings are reused; computed values do not create serialized per-expression
IDs or an unbounded value inventory.

Using a selected offset does not itself require copying the preceding stream.
An operation's resource charge covers only its actual accesses; an optional
full traversal or audit accounts for its complete declared scope.

Format maxima remain those of [metadata](../il-metadata.md) and
[Body/instruction encoding](../il-instruction-set.md). Producer inventories
fit their containing extent and representational domain. Resource charges
use mathematical products, not wrapped small values. A smaller supported
capacity explicitly declines an otherwise representable operation.

## 4. Text, expansion and live state

A String's 24-bit count is an encoded **unit** count, not a scalar count
or an assumed decoded length. UTF-8 supplementary scalars and UTF-16
surrogate pairs have their exact selected decoding/output lengths.
Before constructing output, reserve its actual size or a checked safe bound;
an incremental decoder checks capacity before each appended scalar/unit.
A counting pass consumes its full work even if a later pass does the writing.
Streaming validation need not retain a full decoded copy.

Producers supply String data valid for its selected field and encoding.
Consumers validate scalar and embedded-zero rules for selected String data
before use; unreferenced String payload need not be scanned for that use.
Malformed selected strings are rejected, while diagnostic wording is not
prescribed. A broader audit states the String scope it examines. Resource accounting
does not assign raw/ill-formed literal payload permissions or a universal
semantic interpretation to unreferenced String data.

Live processing state has separate dimensions:

| Dimension | Charge |
|---|---|
| Retained input bytes | Union of simultaneously retained intervals in each exact backing/storage generation; overlapping views share that backing charge |
| Constructed payload bytes | Actual logical byte extents of live copies, decoded sequences, scratch and output/re-encoding buffers; independent copies count separately |
| Logical state entries | Live constructed records, descriptors, list elements, graph edges, facts, obligations, frames and retained cache decisions, with their nested entry shape specified by the recipe |

Scalar fields contained in one logical record are not independent allocations
merely because the host represents them separately. Variable payload and nested
logical entries still count under their respective dimensions. Recipes state
their logical state shapes; allocator headers, native object layout and
physical caches do not determine portable charges.

Source, staging, scratch and destination coexist for as long as the operation
requires them. Reserve the new live charge **before** constructing the result;
do not retire input early to hide build-before-retire space. Release a charge
only when no supporting use remains. Transferring output or a retained decision
does not make it disappear from an applicable parent/aggregate scope.

Decoded descriptor inventories count even when they were not stored on wire.
A deduplicated logical object can share a charge only when the recipe's
identity/context and lifetime actually permit sharing. Equal bytes, row
numbers or layouts alone do not establish that fact.

No compression or whole-section inflation is assigned in the current schema.
A future expanded/segmented/resource codec needs an exact size, traversal,
scratch and work recipe before use; a tiny encoded input cannot license
unbounded expansion or an uncharged native helper.

## 5. Deterministic work, depth and audit recipes

Structural and semantic-audit work have separate cumulative counters.
Accounting uses logical actions, not elapsed time, CPU instructions, host
allocation calls or a whole library callback treated as one step.

Under `resource-accounting/1`, an elementary action processes one fixed-width
field, one list/stream element, one reference/index/range check, one bounded
scalar comparison, or one graph edge/fact/obligation transition. It costs one
unit in its named phase, plus one unit for each encoded/payload byte examined,
copied or generated by that action. A counted operation or list does not hide
all its fields/elements in one action. Byte/scalar scans, text equality,
copying, expansion, sorting comparisons and repeated analysis expose their
complete elementary actions.

A recipe identifies the exact action expansion, traversal order, phase
assignment, live-state shape and depth transitions for its rule/algorithm.
Structural decoding performed during an audit still consumes structural work;
it is not transferred to or substituted for semantic-rule work.
An intrinsic or unsupported analysis with no admitted recipe cannot supply
a metered success claim by guessing its cost.

Replay of the same logical recipe and inputs has the same charges regardless
of physical caching or parallel execution. Semantically repeated work costs
its full action expansion even when a cache avoids redoing it physically.
Valid reuse of a completed decision may instead discharge a repeated
obligation under the
[verification reuse rules](reader-trust.md#5-transformations-verification-reuse-and-audit-claims);
checking that reuse has its own recorded actions and retained-state charge.
A partial or in-progress decision is not reusable successful evidence.

Depth counts simultaneously active logical expansions, not native stack
frames or the longest possible path through every referenced graph.
An iterative algorithm does not erase its recipe's active obligations.
A finite anchored nominal cycle or deduplicated complete pair need not expand
forever; an unbounded substitution or structural cycle does not become valid
by exhausting depth or marking it visited. The
[complete comparison graph](binding-and-replacement.md#4-complete-comparison-graphs)
still must pass before acceptance.

Algorithms can have different supported recipes and charges. An envelope
receipt names the recipe actually used; a portable budget-result comparison
requires the same selected recipe and inputs. This chapter does not assign
one universal verifier algorithm, prove arbitrary authored facts, or supply
missing rule/feature codecs. A later portable minimum must name its applicable
recipes rather than pretend that encoded size determines analysis cost.

## 6. Exhaustion, host failures and claims

All selected limits are inclusive. Before an action, compute its complete
nonnegative work charge, proposed live-state reservation and depth in
nonwrapping arithmetic. Check every applicable dimension before performing
the action. Exactly-at-limit work is permitted when its other obligations
hold. A failed reservation does not establish a partial value, comparison
or validation certificate. Work already performed remains charged.

An invalid envelope, negative charge or broken accounting invariant is a
configuration/tool failure, not a small wrapped input charge. Producer
extent arithmetic exceeding the schema's bounds violates artifact validity;
an unrepresentable resource reservation or insufficient selected capacity
explicitly declines the requested operation.

Resource exhaustion states the affected operation/scope and exhausted
dimension. It does not declare otherwise valid bytes malformed, approve
unvisited work, weaken a retained contract or authorize fallback execution.
An optional whole-artifact/semantic/canonicality claim is not issued
unless its complete required scope succeeded.

Cancellation, unavailable physical memory, I/O failure and other host failures
are distinct from deterministic envelope exhaustion. They also decline the
unfinished operation; they cannot masquerade as a reproducible semantic
budget result or a successful partial artifact.

A resource/validation receipt identifies the exact operation and checked
scope, inputs/context, rule and recipe editions, selected envelope,
cumulative work, peak live/depth charges, completion or failure category,
and any reused decision basis. This is a logical reporting contract, not a
new CLI schema, on-wire field, trust bit or Feature ID. Fixture limits and
receipts are evidence for their supplied cases, not standard numerical minima.
