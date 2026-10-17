# Unsafe FFI and native vtable cleanup

**Status: UNAPPROVED cleanup/representation recommendations.** The unsafe
trust boundary, removal of compiler-owned API/COM ownership policy, and the
direction of explicit native-callable entry support are settled. Exact source
syntax, physical metadata assignments and package/linking choices remain
unapproved. The [standard TRRM extension](trrm-native-interop-metadata.md)
now recommends concrete import, native-entry, function-pointer and generic
vtable records rather than deferring their design.

## Purpose

Remove compiler behavior that tries to prove foreign-provider behavior or
synthesize API-specific safety across an unsafe native boundary. Keep the
core language focused on minimal native ABI support and ordinary checked
semantics. Interop libraries and applications author any safe wrappers they
want to expose.

This proposal supersedes prior recommendations to validate or synthesize COM
owner/acquisition behavior and withdraws those clauses from the
[TRRM native interop proposal](trrm-native-interop-metadata.md). It does not
alter normative specifications or approve existing provisional experiments.
Its current recommendations also supersede compiler-selected API catalogs,
sample-specific carrier admission and COM owner/acquisition recipes in the
historical [Windows FFI stages](windows-native-ffi.md) and
[native architecture proposal](general-native-compiler-architecture.md).
Preserving those documents does not restore their withdrawn policy.

## Settled direction

### Unsafe is an authored trust boundary

Raw native imports and native-callable declarations cross a boundary the
compiler cannot verify and remain caller-unsafe even when their signatures
contain only scalar values. An unsafe declaration propagates caller
unsafety; its body still requires explicit unsafe authorization for unsafe
operations.

Unsafe code is the author's assertion that potentially dangerous operations
are intentional. The language does not prove foreign-provider behavior, raw
pointer validity, or native-resource lifetime, and must not turn an
unprovable assertion into undefined behavior or a success-shaped fallback.
Tooling may offer suppressible diagnostics for trivially incorrect unsafe
code. Those diagnostics are not mandatory safety proofs.

The unsafe boundary may also let an author intentionally bypass checks such
as initialization or definite assignment where a source form permits it.
That is an authored assertion, not a reason to silently repair the program or
reinterpret its behavior as undefined. Exact source forms remain subject to
the relevant language contract.

Safe Terrum code retains the ordinary language's checked rules for its
visible values, initialization, access, ownership, and cleanup. A wrapper can
use explicit unsafe operations internally and choose a safe public API; that
does not prove that a foreign pointer or provider satisfies the wrapper's
assumptions. No automatic marshalling, pinning, rooting, ownership transfer,
or lifetime extension follows from `unsafe`.

That boundary does not make every verifier check optional. Preserve and check
known backing, live pin/loan, access, alignment, initialization, output-state,
ordinary owner and cleanup facts. A copy, adapter, indirect call or unsafe
scope cannot erase them. Missing foreign proof is different from a known
checked-language contradiction or malformed metadata; the latter still
rejects under the ordinary contracts. Suppressible foreign-code diagnostics
are not a substitute for required consumer revalidation.

### Keep FFI minimal and ABI-shaped

The native boundary should primarily expose C-compatible scalar values,
pointers, function pointers, and explicitly committed Blittable layouts.
Signatures should state the ABI shape directly. API names, SDK-specific
layouts, IIDs, reference-count policy, wrapper algorithms, and application
protocols belong in authored declarations and source code, not compiler
catalogs or sample recognizers.

Pointer spelling alone does not establish native validity, retention,
ownership, or pointee initialization. The compiler must not require a
provider-specific lifetime or acquisition proof before admitting an
otherwise-supported unsafe call.

An explicit native-callable opt-in, analogous in direction to
`UnmanagedCallersOnly`/`repr(C)`, selects the foreign entry/signature rather
than exposing arbitrary Terrum methods implicitly. Retain caller-unsafety,
the exact target ABI, fault containment/termination and code lifetime.
Ordinary public visibility is not a native export, and the opt-in creates no
closure, environment, root or image retain. Its exact source spelling remains
for review; these settled boundaries do not require a compiler API catalog.

### Treat COM as a vtable binding, not a language ownership model

COM method calls use an object pointer, an explicit receiver convention,
vtable slots, signatures, and target ABI rules. Those are the only potential
compiler-facing facts for a minimal C++ vtable mapping. Reference counting is
performed through ordinary vtable methods. IID selection, `AddRef`/`Release`
policy, typed acquisition, cleanup, and owner wrappers are authored library
behavior.

The language may provide an explicit opt-in that maps an interface-shaped
declaration to a C++ vtable contract. The spelling and representation remain
open. An interface opt-in must not imply COM ownership, automatic cleanup,
provider verification, or a fixed IID unless a separate reviewed contract
requires that fact.

## Cleanup direction

1. Retire compiler-owned `NativeComOwner` and `NativeComAcquisition`
   semantics, reference-count-specific effects, COM-specific owner provenance
   policy, and synthesized owner close/clone functions. Preserve ordinary
   `unique` values, their provenance/dependencies and authored cleanup methods.
2. Separate generic vtable receiver/slot/signature lowering from COM IIDs,
   reference-count effects, hidden parameters, argument adaptations, and
   output cleanup recipes. Retain only ABI facts needed for the generic call.
3. Audit native parameter, output, retention, extent, storage, callback, and
   catalog contracts. Keep a contract only when it independently expresses
   an implemented ABI or checked-language fact; do not use it as proof of
   raw-pointer safety or foreign behavior.
4. Remove application-specific source admission, verification, storage
   recipes, and method/control-flow recognizers. Keep API declarations,
   ownership wrappers, and convenience adaptations in Terrum libraries and
   samples.
5. Preserve explicit caller-unsafety, target ABI classification, direct
   pointer/function-pointer representation, layout validation, and ordinary
   checked semantics. Update each supported producer/consumer path only
   under its separately reviewed metadata contract.

## Representation recommendations for review

| Concern | Recommendation |
|---|---|
| Raw imports | Ordinary DllImport-shaped declarations carry explicit library/export/ABI facts through standard TRRM and ordinary Terrum dependency binding. No approved-API catalog is required. |
| Native entries and function pointers | Explicit foreign entry opt-in and a complete native callable-signature carrier, preserving effects, pointer/output contracts and selected code origin. |
| Generic vtable calls | Use explicit pointer receiver, vptr byte offset, slot and native signature. Recommend this minimal binding before an interface-to-vtable convenience form; no distinct COM/interface ownership commitment is needed. |
| Native annotations | Retain optional authored access, retention, extent and result-correlated validity facts where useful. They do not prove foreign behavior, authorize raw calls by themselves or confer owners. Absence is not no-retention or initialized-output evidence. |
| Known pointer/owner facts | Preserve them through every producer/consumer and nested operation. Generic ABI adaptation does not change their authority, backing, publication or cleanup. |
| Metadata evolution | Extend the early draft additively with required records and exact capabilities; consolidate the concrete candidates in the TRRM proposal before canonical registration. Existing provisional codes provide no approval. |

The remaining design choices are exact source ergonomics, product loading
defaults and shared-versus-scoped physical callable/value/dependency carriers.
The [TRRM recommendation](trrm-native-interop-metadata.md#consolidation-decisions-and-experimental-history)
chooses reasonable defaults and concrete candidate IDs for review; none is a
reason to leave the needed route unspecified. Suppressible analyzer
diagnostics remain tooling choices, separate from mandatory validation.

## Review and acceptance boundary

Aggregate scratch overlap, wrong-origin imported addresses, lost pointer
backing, missing layout validation, incomplete nested traversal and
address-only import accounting are implementation/verification issues, not
reasons to restore compiler-owned provider policy. The
[review disposition](trrm-native-interop-metadata.md#review-feedback-defects-versus-contract-gaps)
separates them from missing standard carriers. Old findings are not current
patch verdicts, and an uninspected follow-up is not clearance.

Native output annotations likewise do not justify reading an unwritten
failure pointer, inventing a default owner or synthesizing Release. Compose
authored wrapper publication/cleanup through the
[joint proposal](trrm-output-failure-ownership.md#proposed-native-boundary-composition).
Design review, implementation correction and final-revision qualification
remain distinct gates.

## Exclusions

This cleanup does not add a general C++ object model, Rust/Swift/Objective-C
interop, automatic wrapper generation, arbitrary DLL search or staging,
executable-body linking, native package redesign, or new canonical TRRM
records. Those require separate concrete needs and contract review.
