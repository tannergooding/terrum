# Count-driven bootstrap helper call chains

**Status: UNAPPROVED implementation recommendation.** This candidate extends
only the provisional Terrum-authored bootstrap source-to-PE path. It does not
accept a source-language call contract, general Windows x64 ABI, or reusable
linker/backend capability.

## Problem

The initial provisional route accepted exactly one forwarding helper between
`Main` and a supported leaf helper. That limit has since been removed from the
provisional implementation: it derives the forwarding count from parsed
declarations and validates the emitted chain against code, image and unwind
resources. This implementation evidence is not approval; the decision to
accept the recommended source contract remains open.

## Recommendation

Accept a callee-first sequence containing a supported leaf helper, one or more
same-arity forwarding helpers, and a public parameterless `int32 Main()` that
calls the outermost helper:

```text
internal int32 Leaf(int32 p0, ..., int32 pn) => supported leaf body;
internal int32 B(int32 p0, ..., int32 pn) => Leaf(p0, ..., pn);
internal int32 A(int32 p0, ..., int32 pn) => B(p0, ..., pn);
public int32 Main() => A(existing bounded operands);
```

Each helper returns `int32` and has the same ordered `int32` parameters.
Every forwarding helper makes exactly one direct call to the next declaration,
passing each parameter once in declaration order. The leaf uses only an
existing supported parameterized-helper body. `Main` uses the existing bounded
literal or declared-local call operands, optionally under unary negation. Emit
all runtime calls; do not constant-fold, inline, or otherwise erase the
forwarding chain.

Derive the function count, code offsets, signed `rel32` targets, `.pdata`
records, unwind data, PE section/file/image sizes and buffer requirements from
the source and emitted chain. Do not add an enumerated chain-depth or argument-
count limit. Reject duplicate names, mismatched signatures, unresolved or
recursive calls, reordered/omitted/duplicated/modified arguments, extra
declarations or calls, branching, locals in forwarding helpers, arithmetic
around call results, unsupported leaf bodies, and every size or unwind encoding
that is not representable by the existing checked resource limits.

## Exclusions

This remains a linear, same-signature implementation slice. It does not
authorize branching call graphs, recursion, arbitrary function composition,
other return or parameter types, general source-language calls, a reusable
linker/backend, or a general Windows x64 ABI.

## Provisional implementation evidence

Tests execute chain depths 1, 3 and 6 under both runtime policies. Identity
chains with 32, 128 and 256 forwarders additionally exercise deep execution and
unwind through every registered frame. Focused Debug and Release chain/unwind
runs each pass 12/12 cases; the depth-256 case exercises the 8-KiB
stack-guard reporting path. A separate Release/Debug regression derives the
maximum one-argument forwarding count whose complete source fits the
16,384-unit scanner bound, confirms one more forwarder exceeds it, and
validates and executes the generated PE with exact capacities under both
policies. At that source-bound depth, one-byte-short code and image buffers
reject without publishing the corresponding length. Another Release/Debug
regression derives the maximum source-fitting argument count through two
forwarders, verifies the next argument exceeds the source bound, and exercises
the same exact/insufficient capacity boundaries. The suite covers register and
stack arguments, identity/sum/subtraction/product leaves, each runtime-function
range and unwind record, code/image sizing, malformed input, and insufficient
code/image capacity. Mutation checks exercise every forwarding frame's prolog,
call, stack-copy entry and epilogue. This remains source-bound evidence, not a
published depth/arity limit or a maximum across other resource constraints.

On Windows x64, a 256-argument chain with three forwarding helpers unwinds
through every nonleaf frame to the frameless leaf and measures guard growth on
a 4-KiB-commit thread. The 512-argument, one-forwarder regression retains
large-frame probing, alignment, stack-growth and unwind checks. A 32-argument,
32-forwarder case checks every stack-argument slot at all 32 hops and exercises
unwind through the 32 forwarding functions and `Main` under both runtime
policies. These are implementation results only, not contract acceptance.

## Remaining review before acceptance

Review the recommended grammar, rejection boundary and resource-derived count
behavior explicitly. Tests now reach the scanner's maximum source-fitting
forwarding depth and exercise one-byte-short code/image capacities there, but
do not reach maximum code-size or PE metadata arithmetic representability.
Any additional boundary evidence should remain provisional until the
recommendation is approved.

This recommendation remains **UNAPPROVED**. Any implementation remains
provisional evidence and does not change a specification, proposal status,
general ABI contract, or self-hosting gate.
