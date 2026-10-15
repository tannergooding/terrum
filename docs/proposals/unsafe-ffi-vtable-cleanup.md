# Unsafe FFI and native vtable cleanup

**Status: UNAPPROVED cleanup proposal.** The unsafe trust boundary and the
decision not to make COM ownership/acquisition compiler policy are settled
direction. This proposal does not approve source syntax, new language
contracts, standard metadata assignments, or package/linking behavior.

## Purpose

Remove compiler behavior that tries to prove or synthesize safety across an
unsafe native boundary. Keep the core language focused on minimal native ABI
support and ordinary checked semantics. Interop libraries and applications
author any safe wrappers they want to expose.

This proposal supersedes prior recommendations to validate or synthesize COM
owner/acquisition behavior and withdraws those clauses from the
[TRRM native interop proposal](trrm-native-interop-metadata.md). It does not
alter normative specifications or approve existing provisional experiments.

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
   semantics, reference-count-specific effects, owner provenance tracking,
   and synthesized owner close/clone functions. Preserve ordinary `unique`
   values and authored cleanup methods.
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

## Open decisions

- The source/type form for generic interface-to-vtable mapping and how it
  expresses the receiver calling convention.
- Whether the mapping needs a distinct interface layout commitment or can
  reuse an existing fixed-layout contract.
- Whether native access, retention, extent, or output annotations remain
  useful as optional authored declarations, separate from unsafe admission.
- Which suppressible analyzer diagnostics are practical and how tooling
  distinguishes an obvious contradiction from an unprovable native claim.
- Any standard metadata representation for native declarations or vtable
  mappings. Existing provisional TRRM codes provide no approval.

## Exclusions

This cleanup does not add a general C++ object model, Rust/Swift/Objective-C
interop, automatic wrapper generation, arbitrary DLL search or staging,
executable-body linking, native package redesign, or new canonical TRRM
records. Those require separate concrete needs and contract review.
