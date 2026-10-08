# Static proof and constant-evaluation specification proposal

> [!IMPORTANT]
> Partially accepted direction, not an approved rule catalog, resource budget
> or implementation promise. The accepted policy and semantic boundaries are
> in [language contracts](../specs/language-contracts.md#4-a-reproducible-proof-and-evaluation-foundation);
> current hosted support is tracked separately in the
> [implementation reference](../implementation.md#hosted-compiler).

## Portable proof floor

The accepted proof obligations, v1 floor and arithmetic boundaries are in the
[canonical proof-floor specification](../specs/language-contracts.md#42-versioned-portable-proof-floor).
The exact rule and transfer catalog, fact domain, supported predicate catalog,
branch-edge and join transfers, structural work accounting, guaranteed
resource values, and concrete conformance vectors remain open. Facts about
optional presence and ownership are not part of the accepted v1 floor. Loop
induction and invariant reasoning are outside that floor; cycle transfers and
loop-modified facts for any extension remain unspecified. Call summaries and
alias-sensitive facts are likewise not guaranteed by the floor, and any
extension rules remain open. A bounded tree depth such as **64** is
illustrative only, not an accepted limit. No proof-certificate or wire
encoding is selected.

## Effect footprints and observations

The accepted behavior for checked effects, stable observations, faulting
predicates and check elimination is in
[language contracts](../specs/language-contracts.md#43-effects-and-stable-observations).
The exact checked-dependency footprint and effect annotation remain open; a
candidate footprint accounts for reads, writes, captures, allocation, failure,
blocking, suspension, synchronization and lifetime/ownership changes. A
candidate initial predicate subset could admit scalars and stable checked
readonly observations while excluding I/O, ambient state, volatile/atomic
observations, synchronization, suspension and user-visible mutation. The
admissible predicate set and its exact effect requirements remain to specify.
A `Pure` name alone does not establish that an observation is stable.
An unannotated call's invalidation boundary also remains open; a candidate
rule invalidates storage it may affect, but not independent copied scalar
values.

## Deterministic evaluator and proposed budgets

The accepted target-semantic evaluator contract and no-fallback behavior are
in [language contracts](../specs/language-contracts.md#44-deterministic-constant-execution).
The hosted implementation currently uses internal limits of 4,096
interpreted statement steps and a call-depth limit of 64. These are not the
portable evaluation budget and do not approve or measure the candidates
below; see the [implementation reference](../implementation.md#hosted-compiler)
for the supported evaluator subset.

| Resource | Candidate unit / ceiling, **not approved or measured** |
|---|---|
| Evaluation work | 1,048,576 semantic steps; charge defined scalar operations, branches, call entries and element/code-unit operations |
| Call depth | 256 active evaluator frames |
| Temporary storage | 16 MiB of live abstract payload storage, independent of host allocator overhead |
| Serialized result | 1 MiB in the target-defined value encoding |

Candidate values need validation against representative literal parsers before
selecting defaults. Bulk intrinsics need a defined expansion or exact cost
function; counting an arbitrary native helper as one step defeats the bound.
Allocation accounting needs to use defined payload/state, not a host-GC
artifact. Memoization may save physical work without changing the budget
outcome or diagnostics. Wall-clock cancellation is a tool resource failure,
not a different language proof result. The required conformance vectors and
expected outcomes remain to specify.

Evaluator operations, deterministic accounting, and result serialization
remain specification work distinct from sound beyond-floor proof extensions.
Initial serializable results should use defined target value encodings; the
eligible result forms and encoding details remain open. Managed graphs, native
resources and identity-bearing host objects need separate representation
contracts. Whether reproducibility identifiers and budgets are stored in
artifacts, and how, remains a metadata decision; see
[versioning and independent implementations](../specs/language-contracts.md#45-versioning-and-independent-implementations).
