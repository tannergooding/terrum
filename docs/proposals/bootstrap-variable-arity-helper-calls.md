# Capacity-scaled bootstrap helper calls

**Status: UNAPPROVED implementation recommendation.** Windows x64 integer
arguments use four registers followed by regular eight-byte stack slots; there
is no ABI arity boundary at 25 or 32. The provisional bootstrap path must
remain distinct from any accepted general calling-convention or language
contract.

## Problem

The direct-source bootstrap subset accepts an `int32` sum helper and emits a
minimal import-free PE32+ image. The original fixed 512-byte `.text` section
made 32 arguments a PE-buffer limit, not an ABI boundary. Provisional
implementation now sizes the section and image from emitted code and unwind
metadata, subject to the provided output capacity. Frames below 0x1000 bytes
use a direct stack allocation; larger frames use an inline 4 KiB page-touch
loop before the final allocation. Native tests exercise guard growth on a
4 KiB-commit thread and unwind states before and after the final allocation.

The source scanner and host buffers are also bounded. Any generated caller
must fit the supplied code and image buffers, checked `int32` planning
arithmetic, PE field ranges, and the supported source/token limits. The
provisional implementation now touches each 4 KiB page before its final large
stack allocation and records that allocation at the end of the unwind prolog;
the frame remains bounded by the representable unwind operand.

## Recommendation

Keep argument parsing, source-order overflow checking, caller stores, helper
loads, frame sizing and PE validation count-driven. Do not add named
per-argument fields or another numeric arity ceiling. Scale the `.text`
virtual/raw sizes and final image length from the emitted code and unwind
records, subject to caller-provided output capacity and PE alignment/range
limits. Reject insufficient capacity before publishing output lengths, and
preserve zero output lengths on all failures.

Derive stack locations from the Windows x64 rule: zero-based argument indices
0–3 use `ECX`, `EDX`, `R8D`, and `R9D`; index `i >= 4` is stored by the caller
at `[rsp + 0x20 + 8 * (i - 4)]` and loaded by the helper at
`[rsp + 0x28 + 8 * (i - 4)]`. Compute the shadow space, stack slots and
pre-call alignment from the argument count. Before supporting allocations
that can cross a guard page, touch each intervening page before the final stack
decrement, and describe that decrement at the end of the unwind prolog; do not
rely on a large single decrement without preceding probes. Keep checked
planning and output-capacity failures transactional.

This recommendation concerns only the bounded bootstrap component. It does
not generalize helper bodies beyond checked left-associative `int32` addition,
change the source grammar, accept wider argument types, or approve a reusable
Windows x64 ABI/FFI surface.

## Evidence required

- Execute count-distinct generated images across register/stack transitions,
  section and `SizeOfImage` alignment boundaries, and frames spanning multiple
  probe pages. Confirm guard growth on a small-commit thread and exercise the
  probe-loop back-edge.
- Verify exact caller offsets, helper loads, pre-call alignment, frame
  calculation, code and output-image capacity boundaries, PE section sizing,
  and unwind restoration.
- Reject malformed source, arithmetic overflow, unrepresentable PE/unwind
  sizes and insufficient buffers with both published lengths zero.
- Confirm smaller images retain their existing layout; verify larger images
  use correctly aligned sections and fail only when the supplied output
  capacity or a format/platform bound is exceeded.

The section-size and stack-probe policies remain implementation choices under
this recommendation, not accepted language or ABI contracts. Dynamic PE
sizing and the inline page-probe implementation are provisional evidence, not
approval of this recommendation.
