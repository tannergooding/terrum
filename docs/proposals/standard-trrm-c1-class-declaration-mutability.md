# Standard TRRM C1 class declaration mutability

**Status: unapproved; provisional implementation direction under the
user-authorized missing-contract workflow.** This selects a declaration
carrier, not normative adoption, implementation support or qualification.

## Decision and alternatives

Recommend **TypeDef flag `0x0000_0080 = DeclaredClassMutable`** for the
class declaration's Boolean `IsMutable`, in the exact C1 successor below.
Clear means an immutable nominal declaration; set means a mutable nominal
declaration. This decides **only that declaration fact**, not any new layout
semantic, mutable-field rule, object representation or runtime policy.

The [C1 class/witness proposal](standard-trrm-exception-class-witness-binding.md#separate-gaps-and-acceptance)
leaves this fact unrepresented. The
[source contract](../specs/language.md#types-and-operations) and
[class grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations)
already distinguish nominal declaration `mut` from binding rebinding,
per-use `write` authority and individual field mutability.
[`Binder.ManagedClasses.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs)
records the declaration modifier in
[`BoundClass.IsMutable`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs);
neither fields nor Layout rows determine it.

| Carrier candidate | Assessment |
|---|---|
| C1-scoped TypeDef bit `0x10` | Legal if C1's kind stays exactly bits 2..3, with bit 4 treated separately. However, [NominalEnums](nominal-enum-persistence.md#allocation-and-representation) and [CG1](standard-trrm-constant-generic-methods.md#exact-selection-and-owner-integration) use bits 2..4 for kind. A shared three-bit kind reader would misread a mutable class as kind 6. Exact schema-specific extraction could resolve this, but needlessly overlaps two independent dimensions. |
| **C1-scoped TypeDef bit `0x80`** | **Selected.** Keeps declaration mutability outside that kind band and the struct-only [Copy `0x20`](standard-trrm-rootless-copy-type-evidence.md) and [Unique `0x40`](standard-trrm-closed-constructed-array-elements.md#positive-nominal-uniqueness-evidence) facts. Both bit placements use the same fixed-width flags word; a lower bit saves no bytes. This does not compose those other drafts. |
| New typed Contract and required Feature | A complete alternative would require exactly one TypeDef-owned `ClassMutability` clause per class, including expected proxies: one-byte payload `0=immutable, 1=mutable`, no other values, zero clause flags, none on other kinds. Its new Feature/revision would be required by every owning/using Module retaining the declaration, including bodyless inputs. This would add clause/Feature assignments, owner/cardinality/range checks and remapping for a fact required on every admitted class. Independent optional admission is unnecessary here; no such assignments are selected. |
| Layout/FieldDef inference or a host-only Boolean | Incomplete. Same-layout classes can have different declaration contracts; source/debug or host state alone cannot reconstruct a required source-free expectation. A TypeSpec `write` bit records a use, not the declaration's permission to form it. |

Reallocating unpublished enum/ownership bits or replacing the TypeDef flag
organization is permissible. It gives no smaller carrier or useful semantic
separation for this Boolean. The choice is orthogonality, not an additive-only
rule or compatibility duty toward existing drafts.

## Exact schema and proposed assignment

Select the distinct, ordinal, case-sensitive out-of-band identity:

**`trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1`**

Call it **C1M1** in this document only. Its complete schema is
[C1](standard-trrm-exception-class-witness-binding.md), including C1's
B+I+G+G1 catalog reconciliation and binding/publication rules, with exactly
the TypeDef amendment below. Apply C1's failure-identity handoff, construction
map and interface-anchor contexts to C1M1 under the same rules. This resolves
the declaration-mutability part of C1's separate-gap entry, not its layout,
member/value/witness or managed-runtime prerequisites.

Require this exact selection for the root, plan-bearing inputs and every
supplied provider before schema-dependent indexing, emission or loading.
The original C1 token keeps its original meaning and is **not an alias**.
No old C1 reader is required. Wholesale replacement of unpublished C1
would also be permissible, but would require changing its selection owners
consistently; this proposal instead names the complete successor explicitly.
No subset retry, bit/Feature sniffing, automatic conversion or API/CLI default
change is selected.

Keep `(1,0)`, governing edition 1 and Body codec/encoding `01`.
[Format versioning](../specs/representation/format-versioning.md) requires
exact agreement despite equal version bytes. Unchanged Standard, B+I+G,
G1, CG1, enum, A2/A3 and private schemas acquire no class-mutability flag.
The [G1 version-3 source map](standard-trrm-g1-interface-declaration-exports.md)
and [private class-reference carrier](private-nominal-class-reference.md)
are not C1M1 inputs or decoder aliases.

The intended amendments to
[metadata's TypeDef description/flag table](../specs/il-metadata.md#section-kinds-and-row-layouts)
and the [registry owner](../specs/representation/registry.md) are scoped to
this candidate; accepted assignments are unchanged:

| TypeDef item | Exact C1M1 rule |
|---|---|
| Framing | Existing 44-byte row; little-endian `u32 flags` at byte offset 8. No other column or range changes. |
| Visibility | `flags & 0x03`: private 0, internal 1, public 2; 3 invalid. |
| Kind | `(flags >> 2) & 0x03`: struct 1, class 2, interface 3; zero invalid. |
| Declaration fact | `flags & 0x80`, permitted **only on kind 2**. Every retained class, local or expected, carries the complete Boolean through set/clear; it is not an optional positive guarantee. |
| Reserved bits | Bits 4..6 and 8..31 are zero; permitted mask `0x0000_008F`, additionally restricting bit 7 to classes. C1M1 does not admit Copy/Unique flags or enum kind 4. |
| Public class values | Immutable `0x0000_000A`; mutable `0x0000_008A`. These differ in declaration contract, not nominal lookup identity. |
| Capability | No new Feature ID/revision. Supporting C1M1 class declarations requires this fact's semantics; all independent C1 per-Module Feature, codec, target and runtime gates remain. |

In another schema a zero reserved bit supplies **no** declaration-mutability
fact. Do not materialize `IsMutable=false` from old C1 or another schema.
No conversion is defined here; any future explicit conversion must obtain
the true fact from a complete admitted source representation or refuse.
The flag does not carry struct declaration mutability, uniqueness, static
class roles or other independently unsupported declaration facts.

## Publication, binding and retention

Producers set/clear the flag from the checked class declaration, not from
mutable fields, writable methods/uses, selected Core role, layout or name.
Publish it on every retained kind-2 TypeDef, including complete imported
expectations and private declarations required by the retained closure.
Preserve exact visibility and member access; retaining private fields does
not export them. `BoundClass.IsInternal` alone must not collapse distinct
metadata visibility values during re-emission.

Resolve `(Module/revision, ExportKey, TypeDef class kind)` before comparison.
Under [binding/replacement](../specs/representation/binding-and-replacement.md),
compare declaration mutability **exactly in both directions**: neither a
mutable replacement for an immutable expectation nor the reverse satisfies
this dimension. No strengthening/attenuation relation is selected. Check
every original expectation and retained alias equality independently;
preserve original expected facts separately from actual-provider facts and
any additionally relied-on obligations.

Keep class, struct and interface identities distinct throughout binding,
reference projection, shared bound-tree traversal/rewriting, verification
and lowering. Nominal TypeSpecs `02/03` retain their exact TypeDef/Import
origin and all legal qualifications. Do not reconstruct a class through a
structure index or turn its Import into an origin-free local declaration.
Import-to-direct resolution and source-free re-emission rebuild Module,
TypeDef, Import/Export, FieldDef/Layout/Contract ranges, TypeSpecs, signatures,
Body maps and witness/self relationships together, retaining the Boolean
and all original binding contexts. Clone context-dependent structural rows
where necessary; row/name equality and snapshot passthrough are not evidence
of a correct remap.

Before affected use, consumers check selected schema support, framing,
flag/kind applicability, bounded references/ranges, ownership and bindings.
Unsupported or malformed required facts refuse use, without defaulting or
publishing partial output. Under
[reader trust](../specs/representation/reader-trust.md), truthful declaration
facts are producer obligations; consumers need not infer or re-prove them
from fields or arbitrary Bodies.

## Layout, access and runtime boundary

Retain complete FieldDefs and each complete Layout variant: exactly one
default per TypeDef, exact target selection, kind, Blittable commitment,
declared alignment/packing and ordered field membership under
[existing metadata rules](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics).
The new bit changes none of them and specifies no class payload layout.
A consumer needing `BoundClass.Size`, physical field offsets or execution
still needs a supported target/runtime agreement; it must not invent zero
size, serialize hosted offsets or reuse a struct ABI as a class ABI.

Set does not make readonly fields writable or supply a writable receiver;
clear is not transitive immutability of independently writable stored
handles. Place rebinding, TypeSpec `write`/`gc`, unpublished initialization,
field access and unsafe authorization retain their existing separate rules.
Preserve caller-unsafety, ownership/cleanup, provenance, backing, roots,
lifetimes and provider/code dependencies. No object header, allocation/GC,
root descriptor, marshalling, stable address or managed call ABI is chosen.

## Focused acceptance and implementation gates

These are required future cases, not test results:

| Case | Required outcome |
|---|---|
| Producer/consumer | Through public compiler APIs, publish immutable and mutable unrelated classes, including empty/identical readonly field lists and Layout inputs; load source/debug-free references from a non-first explicitly selected provider. Recover exact `IsMutable`, nominal class identity, access and complete retained closure. |
| Malformed | Refuse bit `0x80` on struct/interface, invalid kind/visibility, reserved `0x10/0x20/0x40` or high bits, wrong owners/ranges and missing/duplicate default Layout. Validate expected proxies as well as definitions before use. |
| Replacement | Both Boolean mismatches refuse despite identical names/fields/layout. Agreeing contracts with different row placement pass. Check multiple using Modules/aliases and original expectations after a prior successful provider resolution. |
| Remapping | Legally permute Module/TypeDef/Import/FieldDef/Layout/Contract/TypeSpec and Body-map rows with agreeing maps and ownership/range/dependency order, including Import-to-direct packing and class self-witness edges; re-emit and reconsume without source. Preserve both declaration values, expected/actual origins and every retained obligation. |
| No inference | Identical field/Layout data must not merge the two declaration values. Changing a field's mutability or a Layout input never changes this Boolean. Old-schema zero bits are unknown, not immutable; unsupported physical class layout remains a gate even with a valid flag. |
| Access/unsafe | Reconstructed references enforce existing writable-type/receiver formation and readonly field/access rules, without upgrading readonly uses or dropping caller-unsafe, backing, ownership or cleanup obligations. |
| Exact selection | Reject mismatched root/provider selection and unsupported required closure before output replacement or execution. Original C1, G1-v3 maps, private inputs and enum/A2/A3 selections never auto-promote; Feature lists or plausible flags never select C1M1. |

Implementation remains gated on exact producer/index/reader selection,
schema-aware TypeDef masks/kind extraction (including
[`TrRmTypeDefTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs)'s
shared enum path), complete typed class expectations and all transformations
above. Existing
[`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
class rejection and structure-only nominal reconstruction are not removed
by documenting a bit.

No declaration-mutability choice remains open in this recommendation.
The [C1 prerequisite table](standard-trrm-exception-class-witness-binding.md#separate-gaps-and-acceptance)
still owns complete class/interface/witness/member publication, construction
and value closure, physical class layout where needed, and managed runtime
support/qualification. This Boolean does not discharge those gates or
establish C1 milestone completion. Normative adoption would separately
integrate the proposed TypeDef rule into metadata/registry and conformance
owners; this document changes none of them.
