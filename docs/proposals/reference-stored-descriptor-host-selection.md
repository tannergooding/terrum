# Rootless stored-reference host selection

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** This is a host-selection amendment,
not approval, implementation support, a new runtime or a normative change.
The governing authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Decision and evidence

Recommend this **focused amendment** rather than another descriptor/runtime
design. The [stored-descriptor owner](reference-stored-descriptor-layout.md)
already selects externally bounded ordinary references with 8/8 storage,
one integer argument carrier and RAX results inside a jointly regenerated
closure. It owns formation, Copy, provenance, lifetime, initialization and
boundary exclusions. This amendment makes its unspecified host selection
concrete; it replaces none of those physical or source rules.

| Live evidence | Selection consequence |
|---|---|
| Stored proposal: [scope](reference-stored-descriptor-layout.md#recommendation-and-scope), [direct calls](reference-stored-descriptor-layout.md#ordinary-direct-value-call-agreement) and [exact draft agreement](reference-stored-descriptor-layout.md#metadata-schema-and-binding-consequences) | Requires explicit selection and whole-draft agreement, but names no host key or omitted-option meaning. Its claim of no remaining *carrier* choice does not settle host selection. |
| Accepted [Windows scope](../specs/targets/windows-x64.md#1-scope-and-identity), [layout](../specs/targets/windows-x64-data-layout.md#1-scalar-storage-and-validity) and [call interface §5](../specs/targets/windows-x64-call-interface.md#5-stable-input-borrows-and-initialization) | `(1,1)` assigns neither stored checked-reference layout nor a general reference-result ABI. The one-address, caller-supported call-extent projection is not this extension. |
| [`CompilationOptions` / `Compilation.ValidateOptions`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), [`SemanticVerifier.ValidateNativeLoweringAgreement`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) and [CLI parsing](../../sources/terrumc/Program.Compilation.cs) | The option already exists. Its only nonnull key is `terrum.experimental.windows-x64.normal-backing`; other strings reject. Verification compares host and program selections ordinally. |
| [`ValueLayout.CheckedReferenceSize`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs) and [`NativeLoweringAgreementTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeLoweringAgreementTests.cs) | Null currently gives ordinary references eight bytes; the normal key gives sixteen. Test sources distinguish holder offsets and require explicit candidate verification. These are not newly executed results. |
| [`NativeEmitter.EmitCode` / `PeCompiler`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) and [`NativeJit.EmitForExecution`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs) | Several nonnull branches mean normal-backing validation or unsupported candidate imports. Merely adding another accepted string would not implement correct dispatch. |

Complete reference types, the default eight-byte implementation and the
normal selector are present; historical missing-type/selector observations
in the older proposals are not the live blocker. The real gap is an exact
host identity distinguishing the proposed rootless contract from that
default and the separately selected normal contract. Whole-draft agreement
does not, by itself, populate `NativeLoweringAgreement`.

## Exact selection and defaults

Recommend exactly one case-sensitive ASCII host key:
**`terrum.experimental.windows-x64.rootless-references`**.
Reuse **`CompilationOptions.NativeLoweringAgreement`** and existing explicit
**`nativeLoweringAgreement`** verifier/emitter/JIT parameters; add no second
selection mechanism or process-global target mode. Resolve it once into
immutable closure-local state.

| Selection or boundary | Meaning |
|---|---|
| Null / omitted option | Preserve the existing noncandidate default, including currently admitted prototype behavior. It neither selects nor advertises this extension and cannot consume a program/code stamped with either explicit candidate agreement. Existing pointer-sized layout is not automatic opt-in. |
| Exact new rootless key | Select the stored owner's complete externally bounded 8/8 ordinary/optional descriptor and direct-value contract for one jointly lowered/regenerated native closure. No descriptor-owned runtime hold. |
| Existing exact normal key | Keep the [normal-backing agreement](reference-normal-backing-support.md#exact-host-selection-and-propagation): uniform 16/8 ordinary references and actual normal retention support in its own admitted closure. Zero support does not shrink a value to eight bytes. |
| Separately selected integrated-GC execution | Keep [common GC ABI](integrated-gc-runtime-abi.md#values-cell-identity-and-support) `terrum-gc/windows-x64/cells-coop/1` and target contract `windows-x64-integrated-gc/1`, including their distinct cell and 16/8 stable-reference envelopes. Those identities are not aliases or additional values of this option assigned here. |
| Accepted §5 call-extent projection | Keep its existing bounded input/init-destination admission, caller support and no stored/general-result promise. It does not require the new key and does not select the extension. |

Compare selections with `StringComparison.Ordinal`. Empty, unknown,
differently cased or whitespace-altered strings reject as invalid arguments;
do not trim, normalize, discover aliases or infer from type spelling,
descriptor width, source origin, CPU target or private `native-*` profiles.
CLI duplicate/missing values and unsupported command/mode combinations are
usage errors. A recognized agreement on an unavailable route rejects as
unsupported before affected use; it never falls back to null, normal backing,
tracing handles, RootPin or integrated GC.

Each complete ordinary-reference type has the selected representation in
all admitted fields, nested types, substitutions, arguments and results.
Neither a normal descriptor nor an integrated-GC stable descriptor becomes
rootless because its support word is zero. Pin-derived ordinary references
cannot be truncated, even when an external pin is live. GC roots/locators,
pin acquisition, roots/maps/barriers and normal-backing holds stay outside
the new identity. Externally bounded means *all* actual supporting obligations
cover the dependent extent, not an empty dependency set.

## Selection, propagation and verification points

These are required implementation points, not changes made by this document.

1. **Option resolution and binding.** Extend `Compilation.ValidateOptions`
   to recognize the exact key before target-sensitive binding, layout queries
   or constant evaluation. Every admitted `Create*` route forwards it through
   `Binder` and layout derivation. `ValueLayout.CheckedReferenceSize` selects
   eight bytes for this key and sixteen only for the exact normal key;
   arbitrary nonnull must not mean normal. Recompute enclosing placement,
   closed substitutions and call classifications under that same selection.
2. **Bound state and transformations.** Preserve the identity in existing
   `BoundProgram.NativeLoweringAgreement`. Shared traversal, rewriting,
   adapters, reference binding and replacement preserve it with complete
   type/provenance facts. Layout, verification, specialization and native-code
   caches distinguish the agreement as well as their other inputs; equal
   types or offsets are not interchangeable cached contexts.
3. **Mandatory verifier re-entry.** `SemanticVerifier.Verify`,
   `VerifyLibrary` and admitted import/reference variants require the
   explicit host selection and compare it with the bound identity before
   target-dependent checking. Existing null-only `VerifyReferences` /
   `VerifyLibraryReferences` routes must gain explicit propagation where
   admitted or decline candidate inputs. No overload infers selection from a
   program stamp; changing that stamp does not convert previously derived
   layout, calls or retained observations.
4. **AOT and JIT.** Forward the same selection through `NativeEmitter.Emit`,
   `EmitCode`, admitted import variants, `PeCompiler` and
   `NativeJit.Execute` / `ExecuteWithData`. Replace nonnull-is-normal
   assumptions with exact agreement dispatch for validation, carrier layout
   and helpers/state. A rootless-only closure gains neither normal retention
   state nor the tracing prototype as a substitute. Preserve/check existing
   `NativeCode.NativeLoweringAgreement` before attachment or entry publication.
   `NativeJit.EmitForExecution` forwards it on both ordinary and direct-import
   branches, or rejects a branch not implemented for it. Omitted-selector
   overloads mean null and reject candidate-stamped inputs/code. AOT fixes the
   agreement into the regenerated closure; launch does not reselect it.
5. **CLI and forwarding boundaries.** Reuse
   `--native-lowering-agreement terrum.experimental.windows-x64.rootless-references`
   on source `analyze`, `build`, `build-native-import` and `jit`.
   `TryReadCompilationOptions` parses once; `CompileTrees` supplies the same
   value to compilation and the selected native route. `analyze` claims only
   supported semantic checks. Library/export, Core-library, source-grant,
   component/frontend, persisted module/TRRM/native-package/graph commands
   remain outside this initial option surface and reject it. A forwarding
   worker cannot discard it. Source-library APIs may bind under explicit
   selection where supported without admitting CLI export or persistence.

Runtime-failure policy, arithmetic defaults, Core registration, exact input
schema selection and execution-instance/origin identity remain independent.
The key establishes none of their support and is not runtime bootstrap.
Normal-backing's existing route holds and unsupported cases remain intact.
Raw foreign imports keep their unsafe, signature and backing obligations;
an admitted scalar/raw-pointer import does not admit checked-reference values
at that boundary.

## Producer/consumer agreement and unchanged boundaries

Preserve the stored owner's **no-new-TargetProfile/no-new-Feature** direction:
its target candidate is an exact draft extension of `(1,1)`, not today's
accepted `(1,1)`. The existing host option can distinguish lowering choices,
so no registry change is needed for this bounded jointly regenerated route.
Allocate no numeric profile, Feature, private revision, tag or schema alias.
The key is not serialized into semantic metadata, a decoder selector or
trusted admission state.

Both producer and consumer independently select the complete supported
draft schema/governing contracts and this physical agreement. The key alone,
equal `(1,0)` version bytes or equal `(1,1)` profile bytes cannot establish
that agreement, admit an old artifact or authorize a different decoder.
Retained layout observations remain coupled to their exact selected draft;
normal-backed or integrated-GC layout cannot be published as this rootless
case. Existing reference/dependency capability and Body support gates remain.

Compatible, sufficiently complete target-independent semantic inputs may
participate after original expected/provider binding and complete relowering
under the one closure selection. Rebuild layout, specialization, call plans,
maps, indexes and affected source references; do not retag native bytes,
erase retained layout constraints or relabel a differently bound program.
A transport that cannot preserve required facts rejects until its own exact
codec/selection is supported.

Reference-valued private crossings to independently supplied precompiled
providers or another execution instance remain excluded, even with matching
keys. The joint closure still needs its actual failure/helper/thread-entry
agreement and retained original definition, provider, substitution and
code/load identities. Producers publish truthful initialization, backing and
body facts. Consumers check safe decoding, supported routes, complete binding,
agreement and lifetime before use; they need not re-prove producer bodies.

All stored-owner exclusions survive. References remain non-Blittable.
Foreign/NativeCallable and general function-pointer reference crossings,
stored uncompleted initialization capabilities, zero-extent referents,
erasure and suspension are not admitted by this key. Its proposed direct
descriptor-slot initialization is not an expansion of accepted §5's payload
set.

Preserve the user's **one integrated GC interface with multiple possible
implementations** decision through the [integrated boundary](integrated-gc-runtime-boundary.md)
and [common ABI/bootstrap](integrated-gc-runtime-abi.md). Add no separate
root/pin provider, GC selection or implicit GC dependency to rootless code.
Prior RootPin packaging is not a fallback for missing integrated support.

## Exact owners and remaining dependencies

| Owner | Responsibility remaining outside this selection decision |
|---|---|
| [Stored-descriptor proposal](reference-stored-descriptor-layout.md) | Owns the 8/8 physical/source/direct-call candidate and its conformance gates. This document supplements its explicit-selection language only. |
| [Windows profile](../specs/targets/windows-x64.md), [layout](../specs/targets/windows-x64-data-layout.md), [call interface](../specs/targets/windows-x64-call-interface.md) and [deployment](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Intended target owners on adoption; keep accepted scope and §5 unchanged until an explicit amendment. Exact closure/origin and actual lowering support remain prerequisites. |
| [References](../specs/language/references.md), [dependencies](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md), [provenance](reference-provenance-carriers.md) and [field copy origins](reference-field-value-provenance.md) | Preserve complete authority, generations, broad source bounds and independent owner/guard/code endpoints through every supported operation. No address-only value repairs missing facts. |
| [Normal-backing proposal](reference-normal-backing-support.md) | Owns normal control/hold acquisition, Copy/drop, final-dependent retirement and call handoff. A last-handle-surviving reference requires that separately admitted support route, not this key. |
| [Integrated GC boundary](integrated-gc-runtime-boundary.md) and [ABI](integrated-gc-runtime-abi.md) | Own common runtime selection, cells, roots/pins, barriers, maps and bootstrap/admission. Their implementation and successor schema/profile gates are independent. |
| [Versioning](../specs/representation/format-versioning.md), [requirements](../specs/representation/requirements-and-admission.md), [binding](../specs/representation/binding-and-replacement.md) and [consumer verification](../specs/representation/reader-trust.md) | Own exact draft/schema selection, retained capability closure, directed provider checks and safe consumption. This host key replaces none of them. |
| [Private reference persistence owner](checked-reference-private-persistence.md) and [stored-owner remaining gates](reference-stored-descriptor-layout.md#remaining-decisions-and-support-gates) | Own source-free codecs/exchange and still-unassigned zero-extent, independent-provider and broader reference routes. This amendment changes none of those schemas or decisions. |

The immediate work is exact selector dispatch/propagation and mismatched-route
rejection, followed by coverage of the admitted rootless operations and
final-input JIT/AOT qualification. Existing default-reference or normal-backed
tests do not establish those new-selector results. Formal review/approval and
the stored owner's remaining implementation gates remain outstanding.

## Minimal positive, negative and boundary tests

These are **future acceptance requirements**, not passing evidence.
Use public compiler APIs, unrelated declarations and data-driven selections.

| Family | Minimum cases |
|---|---|
| Selection/default | Exact key succeeds on an implemented route; omission preserves existing noncandidate behavior. Reject empty/unknown/case/whitespace variants and CLI duplicate/missing values. Unknown or recognized-but-unimplemented selection never retries another agreement. |
| Identity/propagation | Cover admitted source and source-library binding, verifier re-entry, AOT and both JIT branches or their explicit unsupported result. Reject rootless/normal/null host-program-code mismatches in both directions, including omitted overloads and cached outputs. |
| Layout/cache isolation | Compile the same nested reference holder under rootless and normal selections; observe 8/8 versus 16/8 and corresponding field offsets, including optional/Transparent values and normal zero-support descriptors. Concurrent compilations/caches cannot exchange derived facts. Rootless-only output has no normal/tracing support state. |
| Direct ABI boundary | Observe reference input and RAX result, the fourth/fifth physical-position transition with mixed floating/unit formals, and one-/two-descriptor holders with proper indirect/hidden-result classification. Keep accepted §5 call-extent projection working without this key; it gains no descriptor-containing payload or general result admission. |
| Support/lifetime exclusion | Valid externally bounded backing permits Copy/return under declared sources. Reject destroyed local/owner/guard/code endpoints, stale generations, normal escapes requiring an absent hold, GC-root or pin-derived truncation, foreign/fnptr descriptor crossings and uncompleted init storage. |
| Exchange/binding | Independently supplied compatible semantic expectations relower only after exact supported schema/agreement selection. Reject mismatches despite equal type/version/profile bytes, wrong retained layout/provider facts, unsupported codecs, native retagging and independently precompiled/cross-instance reference crossings. |

No additional carrier or host-identity choice is needed inside this proposed
bounded agreement. This document authorizes no code/specification change and
does not claim the broader reference milestone is complete.
