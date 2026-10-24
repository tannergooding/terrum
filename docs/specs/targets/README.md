# Target profiles

Target profiles own physical storage, procedure calls, foreign admission and
deployment support. They refine the [core layout contracts](../data-layout-and-abi.md)
without changing source identity, ownership or the
[abstract memory model](../memory-model.md).
The [semantic operation display map](../il-instruction-set.md#operation-identity-encoding-and-presentation)
does not prescribe machine instructions, ABI carriers or target support.

| Profile | Scope |
|---|---|
| [Windows x64 native](windows-x64.md) | Fixed scalar and aggregate storage, standard-mapped foreign calls, explicit stable borrows and complete owner transfer, non-unwinding foreign boundaries and native image/dependency lifetime |
| [Windows x64 failure bridge](windows-x64-failure-bridge.md) | Optional paired boundary-contained exception interchange, exact affine packet/domain wire and synchronous matched callbacks; no shared foreign unwind through Terrum |

A logical profile revision is not an encoding schema, numeric target/Feature
allocation, compiler support claim or promise of a frozen managed ABI.
Conformance requires the complete selected profile and its retained
dependencies, not a union of independently supported examples.
