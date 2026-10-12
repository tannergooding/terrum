# Source-bounded parameterless bootstrap helper call chains

Status: UNAPPROVED recommendation.

Recommend extending only the provisional direct-source Windows x64 route with
a callee-first sequence: one existing internal parameterless `int32` helper
leaf, one or more internal parameterless `int32` expression-bodied forwarders,
then public parameterless `int32 Main()` calling the last declaration:

```text
internal int32 Leaf() => (40 + 2) * 3;
internal int32 First() => Leaf();
internal int32 Outer() => First();
public int32 Main() => Outer();
```

Each forwarder must call only its immediately preceding helper, once, without
arguments or arithmetic around the result. Retain the existing parameterless
leaf expression parser, checked folding fallback and leaf encodings. `Main`
retains its expression body or existing block with source-bounded immutable
locals followed by one helper-call return; locals cannot shadow any helper.

Derive depth from validated declarations within the existing 16,384-UTF-16-unit
source and supplied workspace bounds, not an enumerated depth limit. Preserve
every runtime call: each nonleaf uses the existing 14-byte call frame with
40-byte aligned stack/shadow-space allocation and a separate runtime-function
and unwind record. Check code/image capacity and metadata/PE size arithmetic
before publication. Failure leaves the corresponding length unpublished.

Reject duplicate names, mismatched types or signatures, unresolved, skipped,
recursive or out-of-order calls, extra declarations/statements, forwarding
locals or blocks, arguments, unsupported leaf expressions, intermediate
arithmetic overflow and invalid division/remainder. This does not authorize
parameterized-helper changes, general call graphs, runtime expression lowering,
a general ABI/linker, canonical specification edits or proposal approval.

Provisional regression coverage executes forwarding depths 1 and 3 and a
generated chain's maximum source-fitting depth under both runtime policies,
through JIT/AOT source emission and generated PE execution. It checks unchanged leaf bytes,
every nonleaf code byte, entry alignment, native unwind through every frame,
exact/one-byte-short code and image capacities, short expression/local
workspaces, malformed chains and checked arithmetic rejection. This is
implementation evidence, not approval.
