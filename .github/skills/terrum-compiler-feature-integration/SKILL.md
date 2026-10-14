---
name: terrum-compiler-feature-integration
description: Implement or generalize Terrum compiler features end to end. Use when changing type, effect, initialization, ownership, layout, native-call, or bound-tree facts that must survive verification, separate compilation, and lowering without sample-specific policy.
---

# Terrum compiler feature integration

Follow `.github\copilot-instructions.md`. This skill guides implementation
within the authorized feature scope; it does not approve new language or
metadata contracts. Paths below are repository-relative.

## Establish the contract and integration boundary

1. Identify the exact user requirements and settled decisions. Read the relevant
   chapters through `docs\specs\README.md`, the grammar, and current support in
   `docs\implementation.md`. Read the governing proposal for genuinely open
   choices. Do not infer approval from existing prototypes or compiler APIs.
2. Describe the semantic fact being introduced or preserved: its owner,
   representation, valid states, access/effects, lifetime/provenance, and failure
   behavior. Resolve material contract gaps before adopting a convenient
   source form, wire schema, ABI, or runtime fallback.
3. Trace prior art and callers directly. Reuse shared representations and
   mechanisms instead of adding a parallel sample/profile path. Choose one
   coherent end-to-end boundary, not a file-count or validation-checkpoint quota.
4. Keep a compact contract-to-test matrix in the conversation or session state.
   For each relevant layer, identify the carrier, producer, consumer,
   invariant, required changes, and regression evidence. Record deliberate
   exclusions as support gaps; do not create a repository tracking document.

## Map every affected surface

Use these starting points, then trace the actual feature's consumers. A row
is not complete merely because one listed file was edited.

| Surface | Starting points and integration obligations |
|---|---|
| Source admission | Syntax APIs and `sources\Terrum.CodeAnalysis\Semantics\Binder.cs`; preserve grammar, declaration effects, and argument/target identity |
| Checked representation | Bound nodes/types and `sources\Terrum.CodeAnalysis\Semantics\BoundTreeTraversal.cs`; cover nested children, limits, visiting, rewriting, and remapping |
| Mandatory verification | `sources\Terrum.CodeAnalysis\Semantics\SemanticVerifier.cs` and applicable partials; revalidate reconstructed and producer-generated inputs |
| Standard interchange | `sources\Terrum.CodeAnalysis\Modules\TrRmExecutableCompiler.cs` and applicable TRRM readers/writers; use only the reviewed standard records/profile |
| Private dependency paths | `sources\Terrum.CodeAnalysis\Modules\ModuleCompiler.cs` and `sources\Terrum.CodeAnalysis\Modules\NativeModuleCompiler.cs`; preserve imported contracts, reconstruction, and replacement obligations |
| Target execution | `sources\Terrum.CodeAnalysis\Native\NativeEmitter.cs`, `sources\Terrum.CodeAnalysis\Native\NativeJit.cs`, and relevant partials; preserve ABI, cleanup, target, and runtime-policy behavior |
| Public and CLI evidence | Active tests in `tests\Terrum.CodeAnalysis.Tests`; exercise the public APIs and actual selected CLI paths without adding `InternalsVisibleTo` |

Standard `.trrm`, private semantic modules, native packages, JIT, and AOT are
distinct routes. A private round trip does not prove standard interchange.
If an in-scope route currently rejects the feature, either implement its
required contract path or retain explicit rejection and report the remaining
gap. Do not bypass admission or verification to obtain an apparent pass.

## Implement a coherent slice

- Carry complete identities and facts through conversions, generic
  substitution, adapters, copies, traversal/rewriting, persistence, dependency
  binding, and lowering. Preserve loan/owner provenance, output-state
  relationships, and unsafe obligations rather than only ABI-shaped values.
- Check remapped indices, exported/imported declarations, nested aggregates,
  optional/default records, and compatibility/replacement validation where
  affected. A source binder check is not a substitute for consumer validation.
- Preserve evaluation order, conditional publication, partial acquisition,
  failure/unwind, and cleanup. Do not silently initialize an output, erase
  authority, discard a dependency, or turn missing metadata into success.
- Use general COM, function-pointer, handle, layout, ownership, initialization,
  and effect contracts. API names, IIDs, vtable slots, and application-specific
  layouts belong at authored library/dependency boundaries through reviewed
  representations. Renaming a DX11 helper or moving its data to another
  compiler catalog does not remove its policy.
- Keep boundary validation and shared verifier coverage intact. Extend
  traversal and codecs alongside the representation, not as optional
  follow-ups. Do not trust serialized inputs merely because the normal writer
  would not produce the invalid case.
- Update directly related support inventories and proposals without changing
  approval status. Keep normative specifications free of implementation
  snapshots; do not extend adjacent APIs or reopen settled decisions.

## Exercise the contract, not just the motivating sample

- Add active regressions through public compiler APIs using the test area's
  patterns. Check project exclusions before relying on a fixture.
- Include an unrelated synthetic type/API and rejection cases for the changed
  invariant. Preserve supported behavior as well as rejecting bad input.
  Select relevant variants such as nested arguments, forwarding/adapters,
  generic substitution, success/failure outputs, and cleanup exits.
- For persistence/dependencies, exercise independent producer/consumer paths,
  round trips, reconstruction, and malformed or incompatible contracts.
  Include consumer-side rejection even where source binding already rejects.
- Cover required JIT/AOT and runtime-policy variants separately. Use the
  `terrum-native-qualification` skill for native input selection and evidence,
  and `terrum-example-writing` when authoring Terrum snippets.
- Implement the coherent batch before its integration checkpoint. Combine
  related test selectors, build the matching configuration, and run the normal
  analyzer/style checkpoint when required. Group related corrections and rerun
  only invalidated or missing evidence.

## Reconcile and continue within scope

Review the full relevant diff and the matrix against the final patch. Confirm
that every required producer/consumer route preserves the fact, or that its
remaining gap is explicit. Preserve unrelated work and remove only abandoned
scaffolding introduced by this batch.

When independent review is requested, give `terrum-contract-review` the
original concerns, explicit decisions, frozen diff/snapshot identity, and
validation evidence. Integrate actionable findings and revalidate affected
paths; the reviewer does not replace implementation or acceptance work.

Distinguish slice completion from milestone acceptance. Reconcile remaining
special cases, persistence/dependency gaps, contract decisions, todo
dependencies/statuses, and final-patch evidence before advancing an authorized
roadmap continuation. Commit only when authorized; a passing slice does not
authorize publication or unrelated design.
