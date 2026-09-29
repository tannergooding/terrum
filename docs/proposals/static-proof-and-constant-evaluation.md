# Static proof and constant-evaluation specification proposal

> [!IMPORTANT]
> Pending specification, not an approved rule catalog, resource budget or
> implementation promise. The accepted distinction between executable
> checks, required static proofs and required constant evaluation is in
> [language contracts](../language-contracts.md#4-a-reproducible-proof-and-evaluation-foundation).

## Portable proof floor

The accepted policy requires a versioned minimum of proof rules, complexity
and guaranteed resources independent of optimization level. Sound
implementations may prove more, but a required proof not established by
supported analysis fails compilation; disproved and not-established outcomes
must be distinguished. The following rule areas are **candidates**, not the
minimum floor:

| Component | Candidate transfer and checking rules |
|---|---|
| Values | Target-width scalar constants, immutable value identities, booleans, optional presence, initialization/vacancy and ownership state |
| Numeric facts | Closed intervals, exact target operations on constants, and conservative interval transfer only where defined; retain failure edges |
| Branches | Refine supported built-in comparisons on true/false edges; use interval hull and intersection of must-facts at joins |
| Loops | Forget numeric/alias facts for values or storage a cyclic region may change, retain unaffected facts, and refine again on branch edges |
| Calls | Use selected published pre/postconditions and an effect footprint, not a replaceable callee body |
| Aliases | Invalidate facts for affected places before later loads, but retain independent captured values |
| Unsupported reasoning | Diagnose not established, rather than assuming a predicate or relying on unstable analysis |

A conservative initial floor need not use implicit induction, guessed
invariants or machine-speed-dependent loop unrolling. Integer proofs must
respect checked overflow; floats require target rounding, signed zero, NaN
and no implicit reassociation/FMA. Exact constant floating evaluation is a
candidate only once the target evaluator is specified. Required conformance
vectors must cover arithmetic boundaries, branches, joins, cycles and
resource limits. Select a machine-checkable transfer catalog and structural
work accounting; a bounded tree depth such as **64 is illustrative only**.
Define authoring escape routes only where an ordinary runtime check is legal.
Versioning the floor does not select a wire encoding or proof certificate.

## Effect footprints and observations

A declaration's checked dependency footprint would need to account for
reads, writes, captures, allocation, failure, blocking, suspension,
synchronization and lifetime/ownership changes; neither a `Pure` name nor a
readonly receiver proves stability. An initial contract/proof subset could
admit scalars and stable checked readonly observations while excluding I/O,
ambient state, volatile/atomic observations, synchronization, suspension and
user-visible mutation. Imported calls may publish checked facts without
exposing their bodies; an unannotated call conservatively invalidates storage
it may affect, not independent copied scalar values. Exact footprint spelling
and admissible predicates are still to specify. If a predicate can fault, a
compiler must retain that behavior unless its absence is also proved;
eliminating a known-true check cannot drop argument evaluation, effectful
getters, roots, loans, cleanup or possible failure.

## Deterministic evaluator and proposed budgets

Required constant evaluation must execute a specified unoptimized semantic
instruction set with target types and arithmetic, not host overflow, `nint`,
floating defaults, culture, filesystem, clock, address or process state.
Checked evaluator-owned local mutation is possible without ambient writes or
escaping pointers. The initial serializable results should use defined
target value encodings; managed graphs, native resources and identity-bearing
host objects need separate representation contracts.

| Resource | Candidate unit / ceiling, **not approved or measured** |
|---|---|
| Evaluation work | 1,048,576 semantic steps; charge defined scalar operations, branches, call entries and element/code-unit operations |
| Call depth | 256 active evaluator frames |
| Temporary storage | 16 MiB of live abstract payload storage, independent of host allocator overhead |
| Serialized result | 1 MiB in the target-defined value encoding |

Validate candidate numbers against representative literal parsers before
selecting any defaults. An explicitly requested supported budget becomes a
recorded input. Bulk intrinsics need a defined expansion or exact cost
function; counting an arbitrary native helper as one step defeats the bound.
Allocation accounting must use defined payload/state, not a host-GC artifact.
Memoization may save physical work without changing the budget outcome or
diagnostics. Wall-clock cancellation is a tool resource failure, not a
different language proof result. Exhausting a required budget diagnoses
without fallback or guessed value; optional optimization may decline.

Specify the evaluator operations, deterministic accounting and serialized
results separately from sound beyond-floor proof extensions. Identify source
profile, proof-floor version, target value semantics, declared contract and
effect dependencies, and the required evaluation budget when reproducibility
needs them. Whether or how those identifiers are stored in artifacts remains
metadata work. A required proof against an imported declaration uses its
published contract; constant evaluation of that declaration needs an
available eligible body and its evaluated dependencies.
