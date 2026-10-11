# Development TRRM encoding history

This is an informative record of the overlapping development catalogs, not a
second specification or a compatibility-reader requirement. The
[unified registry](../specs/representation/registry.md) replaces their
allocations while preserving the selected semantics.

The earlier generic catalog placed constants/values, places, calls and checked
integer operations in `01..18`, held indexing/view shapes at `1A..1C`, and
rejected `19`. A separate floating draft used the following overlapping
numbers. Both used development format bytes `(1, 0)`; those bytes alone never
identified an interchangeable schema.

| Operation | Retired floating draft byte | Unified byte |
|---|---|---|
| FloatCompare | `19` | `40` |
| FloatConvert | `1A` | `41` |
| IntToFloat | `1B` | `42` |
| FloatNegate | `1C` | `43` |
| FloatBinary | `1D` | `44` |
| Char16ToFloat | `1E` | `45` |
| FloatToInteger | `1F` | `46` |

The corresponding generic indexing shapes move to `27..29`; Call moves to
`30`; the integer/Boolean and value families follow the current registry.
The retired numbers are not aliases. No heuristic may decide a catalog from
the operands or reinterpret an old operation using the new table.

The hosted/native inventories and provisional output proposal describe
implementation-era support, including exact binary32/binary64 constants,
comparison, conversion, arithmetic and preservation of the caller's floating
environment. Their former overflow status was `0xE0540001`.
The canonical [floating operation contracts](../specs/il-instruction-set.md#floating-operations)
now own those semantics. Their presence in the specification does not prove
that an implementation has migrated its writer, reader, persisted consumers,
indexes or tests to the unified bytes.

An explicitly selected historical development schema can be inspected by a
separate tool. It cannot be loaded as current-schema input merely because its
format pair matches. There is no requirement to retain unpublished formats,
legacy aliases or migration shims.
