# Bounded bootstrap helper call chains

**Status: UNAPPROVED implementation recommendation.** This candidate extends
only the provisional Terrum-authored bootstrap source-to-PE path. It does not
accept a source-language call contract, general Windows x64 ABI, or reusable
linker/backend capability.

## Problem

The current bounded route compiles one internal `int32` helper called by a
parameterless public `Main`. It can emit and validate helper bodies for
identity, left-associative addition, subtraction, or multiplication, but it
cannot compose helpers. A compiler bootstrap needs evidence that a generated
nonleaf helper can receive arguments, forward them through another call, and
return the callee result while preserving stack and unwind behavior.

## Recommendation

Add one exact straight-line call chain with declarations in callee-first source
order:

```text
internal int32 B(int32 p0, ..., int32 pn) => supported leaf body;
internal int32 A(int32 p0, ..., int32 pn) => B(p0, ..., pn);
public int32 Main() => A(existing bounded operands);
```

Require `A` and `B` to have the same parameter count and corresponding `int32`
parameter declarations. `A` forwards each parameter exactly once to `B`, in
declaration order. `B` uses only an existing supported parameterized-helper
body. `Main` uses the existing bounded literal or declared-local call
operands, optionally under unary negation. Emit and validate the actual runtime
calls; do not constant-fold or inline the chain as a substitute for testing
nonleaf argument forwarding.

Reuse the current scanner, checked-size, code-buffer, output-buffer, PE-field,
and unwind representability limits. Do not introduce a named argument-count
ceiling or infer an ABI arity boundary from the maximum source fixture.

## Exclusions

Reject duplicate helper names, mismatched arity, unresolved or recursive
calls, forward declarations, reordered/omitted/duplicated/modified forwarded
arguments, extra declarations or calls, branching, locals in `A`, arithmetic
around a call result, other signatures, and unsupported leaf bodies. Do not
change existing single-helper forms or accept general function composition.

## Implementation constraints

- Keep the executable in one `.text` section. Lay out the entry caller, the
  forwarding helper, and the leaf helper explicitly; compute both signed
  `rel32` call displacements from their final instruction offsets and validate
  their exact intended targets.
- For `A`, derive incoming stack-argument locations relative to its allocated
  frame, separately from outgoing caller locations. Preserve the first four
  incoming register arguments, the shadow space, alignment, and any required
  page probes. Do not assume incoming arguments are homed or reuse incoming
  home space as outgoing stack slots.
- Emit a sorted `RUNTIME_FUNCTION` record for each nonleaf region and valid,
  aligned unwind information describing that region's allocation/probe
  prolog. Validate unwind states through both callers, including call and
  epilogue PCs; do not treat the entrypoint record as evidence for `A`.
- Derive `.pdata`, unwind-data, section, file, image, and capacity sizes from
  the complete emitted chain. Preserve the current publication boundary:
  source-emitter failure publishes no code length; a later PE-writer failure
  publishes no image length and may retain the already valid code length.

## Evidence required

- Execute generated images through `Main -> A -> B` with nonuniform arguments
  that prove positional forwarding, covering register/stack argument
  transitions and each existing supported leaf operation.
- Check stack alignment at both helper entries, exact incoming/outgoing
  offsets, both call targets, code bytes, and the complete PE exception
  directory and unwind records.
- Exercise unwind restoration for both nonleaf frames, including a nested
  stack walk through `B`, `A`, and `Main`; verify stack growth for probed
  nested frames where a compact source fixture can reach the boundary.
- Reject arithmetic overflow and each excluded declaration/call shape before
  publishing code. Mutate call targets, frame setup, forwarding operands and
  unwind metadata to verify PE rejection and the existing code/image
  publication behavior.
- Preserve the existing single-helper AOT/JIT, native execution, unwind,
  capacity, and both-runtime-policy regressions.

This recommendation remains **UNAPPROVED**. Any implementation remains
provisional evidence and does not change a specification, proposal status,
general ABI contract, or self-hosting gate.
