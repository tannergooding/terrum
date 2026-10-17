# DX11 generalization check-in

> Review-only snapshot for the current milestone. This is not an acceptance
> record, a canonical contract change, or approval of the TRRM proposal.

**Snapshot:** 2026-10-14  
**Repository / branch:** `tannergooding/terrum` / `generic-first-history`  
**HEAD:** `7d4981ec76d1ae62ba29bddd836488aefaa0c617`  
**Proposal under review:** [TRRM native imports and COM metadata](../proposals/trrm-native-interop-metadata.md)

## Executive summary

The compiler now has generic catalog-backed native import, storage, COM, and
Win32 window mechanisms, and the maintained DX11 sample entry points use
source-authored calls. The milestone is **not complete**: DX11 API metadata
remains in the compiler, a sample-shaped window-owner contract is still
compiler-enforced, raw imports are not yet caller-unsafe in the implementation,
and native import metadata is not carried through a source-free TRRM
dependency path.

The immediate scope remains the user's requested minimal TRRM extension. The
broader `.trrm`/`.trmn`/`.trng` architecture redesign is deferred to the
separate future session. The formats are unstable and have no compatibility
requirement; missing capabilities should be designed and added rather than
rejected for back-compat reasons.

## Current plan and worktree

The session task graph still has several overlapping or stale items:

| Task | State | Check-in observation |
|---|---|---|
| `remove-shared-dx11-gates` | In progress | Includes acquisition/ownership and profile-policy removal. |
| `audit-dx11-acceptance` | In progress | Its prerequisite verification is recorded against older revision `bd2a5c5e`. |
| `genericize-native-acquisition` | In progress | The compiler-generated acquisition path remains. |
| `remove-dx11-source-profile-policy` | In progress | Source-profile gates remain in the compiler. |
| `generic-binder-com-support` | In progress | Needs reconciliation with committed generic COM work. |
| `rewrite-generic-first-history` | In progress | Keep the recovery/history state separate from feature acceptance. |
| `retire-dx11-bound-lowering` | Pending | Bound/emitter policy retirement is not complete. |
| `add-generic-dx11-independent-coverage` | Pending | Reconcile with the completed independent-test audit and current exclusions. |
| `separate-dx11-only-tests` | Pending | Prototype tests remain excluded from the active project. |
| `validate-generic-first-tip` | Pending | Requires validation of the intended final tip. |
| `next-roadmap-rich-libraries` | Pending | Explicitly follows this milestone; keep it separate. |

`verify-dx11-generalization` is marked done on `bd2a5c5e`, while HEAD is
`7d4981ec`. Its evidence is useful history, but the task graph should identify
which later changes were covered and which still need qualification.

The tracked worktree change at this snapshot is the TRRM proposal. The 104
pre-existing untracked files—historical sample stages, excluded prototypes,
and unrelated drafts—are preserved and were not staged or deleted. The
check-in report itself is an additional review-only Markdown file.

## Verified progress

- Local commits separate generic compiler work and the maintained sample
  migration, including `85c5557a` and `fdb6a2e9`.
- Generic COM catalog validation and vtable lowering have unrelated
  synthetic-interface coverage.
- The maintained WARP and hardware sources author their main control flow
  and calls in Terrum rather than receiving a compiler-generated render loop.
- The proposal now distinguishes raw `ptr<T>` ABI signatures from
  `init ref<T>` safe-wrapper outputs; it also states the unsafe, Blittable,
  no-implicit-marshalling model.
- A previous full active-suite TRX,
  `artifacts\tst\dx11-generalization-final-rerun-20261014.trx`, records
  **15,074 passed, 0 failed** at `2026-10-14 05:52:44 -07:00`. This check-in
  did not rerun tests. The active suite is not evidence of WARP/hardware
  runtime execution: the current test project excludes the unconverted
  `CliTests.Dx11Sample.cs` prototypes, and active sample CLI tests compile
  emitted images.

## Blockers and risks

1. **DX11 metadata is still compiler-owned.**  
   `NativeImportCatalog` composes `NativeDx11ComCatalog`, which contains
   D3D/DXGI imports, IIDs, slots, ownership, layouts, and parameter facts.
   The TRRM proposal is not yet implemented.

2. **Sample-specific Win32 owner policy remains.**  
   `NativeWindowOwnerContract` names `Triangle.Interop.WindowOwner`,
   `Triangle.Interop.Host.WindowProcedure`, and `TerrumOwnerClass`.
   `SemanticVerifier` contains special checks through `MatchesProgram`.
   This is not D3D API metadata, but it is still sample-shaped compiler
   policy that must be resolved for the stated no-sample-policy goal.

3. **The implemented unsafe contract conflicts with the requested FFI model.**  
   `NativeImportCatalog.cs` sets `RequiresUnsafeCaller: false` for catalog
   entries including `GetCurrentThreadId` and `Sleep`; binding requires the
   declaration to match that catalog flag. The proposal instead requires
   raw native imports and COM calls to propagate caller-unsafety.

4. **Native imports do not yet flow through binary dependencies.**  
   `ModuleCompiler`, `TrRmExecutableCompiler`, and graph/module paths call
   `RejectSourceNativeImports`. TRRM writing, reading, dependency binding,
   and emission need one integrated implementation slice.

5. **The proposed writable pointer examples exceed current binder support.**  
   `Binder.cs` currently admits only bounded readonly character-pointer
   forms in this FFI path; `write ptr<T>` and `write ptr<ptr<void>>` are not
   general executable signatures yet.

6. **Typed COM acquisition requires an explicit IID relation.**  
   `DXGIGetDebugInterface1` accepts arbitrary `REFIID` and returns `void**`.
   Its raw import is not inherently an `IDXGIDebug` acquisition. The proposed
   `NativeComAcquisition` belongs on a Terrum wrapper that fixes the IID,
   validates success, adopts the pointer, and releases any failure output.

7. **Some language type-model questions remain open.**  
   The current proposal distinguishes `ref<T>`, `pin ref<T>`, `ptr<T>`, and
   the `init ref<T>` parameter mode. The user has raised possible
   `ref<init T>` / `ptr<init T>` forms and the full nested-const/
   `[In]`/`[Out]` mapping; those questions should not be silently settled by
   the narrow TRRM extension.

## Recommended completion order

1. Review the focused TRRM proposal and settle only the import, parameter,
   output, COM-interface, slot, owner, and acquisition records needed for
   these examples. Keep proposal status unapproved until explicit review.
2. Implement a vertical slice for `QueryPerformanceCounter`: raw
   `extern unsafe int32` with writable `ptr<int64>`, explicit caller unsafe
   scope, target-Blittable validation, result-correlated output metadata, and
   an authored safe wrapper using stable local storage.
3. Implement `DXGIGetDebugInterface1` as raw `void**` metadata plus a
   separate IID-fixed Terrum wrapper; add generic IID/slot/owner/acquisition
   checks with unrelated synthetic APIs.
4. Move DX11/DXGI metadata out of `Terrum.CodeAnalysis`. Replace the
   sample-shaped window-owner verification with a generic/source-authored
   contract without weakening ownership, callback, failure, or cleanup checks.
5. Reconcile the task graph against current HEAD, then run combined focused
   tests during implementation and one full-suite checkpoint on the final
   integrated code revision. Test WARP/hardware runtime behavior separately
   from compilation-only CLI tests.

## Throughput and process recommendations

- **Rebaseline the task graph once.** Several active tasks overlap; the
  verification task references an older revision. Replace stale entries
  with a short dependency chain tied to concrete acceptance criteria rather
  than repeatedly marking old tasks complete.
- **Use a contract-to-test matrix.** For each TRRM record, track its source
  declaration, writer/reader validation, binder effect, target lowering,
  negative cases, and one unrelated synthetic API.
- **Work end-to-end by boundary.** Finish one import path across declaration,
  TRRM, dependency binding, verification, and PE emission before adding broad
  catalog coverage. Keep COM acquisition as a dependent second slice.
- **Separate compile and runtime evidence.** The 15,074-test pass does not
  establish actual WARP/hardware rendering. Track build/emission and runtime
  tests as distinct acceptance items, including environment skips.
- **Preserve evidence identity.** Record revision, selectors, counts, skips,
  and native-probe/compiler hashes with results; timestamps alone are
  insufficient after local history reconstruction.
- **Preserve, do not sweep.** Leave excluded prototypes and historical
  artifacts untouched unless their individual status is reviewed; do not
  re-enable them wholesale or stage them as maintained tests.
- **Keep future packaging redesign out of this gate.** The immediate blocker
  is dependency-authored TRRM metadata, not a redesign of all package layers.

## `/chronicle` commands

The Sol check-in attempted `/chronicle tips` and `/chronicle improve`, but its
environment reported no slash-command execution interface. Neither command
ran, and no output was invented. The recommendations above are Sol's
independent review, not Chronicle command results.
