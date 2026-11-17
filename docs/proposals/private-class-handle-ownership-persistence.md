# Class-handle ownership in private module persistence

> [!IMPORTANT]
> **Unapproved candidate; provisional implementation direction under the
> authorized missing-contract workflow.** This document selects a concrete
> private agreement for review. It does not approve that agreement, change
> normative specifications or claim compiler/runtime support.

## Finding, scope and owners

Accepted [class ownership](../specs/language.md#types-and-operations)
distinguishes aliasable ordinary handles from unique owning handles.
[Retained ownership contracts](../specs/language/ownership-and-cleanup.md#retained-contracts-and-independent-admission)
must preserve that distinction without source. The missing decision is its
**private class-record carrier and consumer interpretation**, not the meaning
of `unique class`.

The source binder now accepts unique class declarations and retains their
nullable `BoundClass.IsUnique` fact, but full ownership-domain/runtime
integration and source-free persistence are still in progress. The current
private writer refuses a known unique class because its selected record has no
ownership byte; the C1M1 writer is likewise restricted to known aliasable
classes. This is not evidence that an existing successful unique-class
round-trip loses a flag. Source checking and runtime integration remain
prerequisites; no writer may publish false ordinary ownership.

| Inspected owner | Current fact and consequence |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `BoundClass` | Carries nullable `IsUnique`; source ordinary and unique declarations publish known facts, while old private records remain unknown. The current private writer/reader does not serialize the fact. `ManagedClassTypes` maps four qualified bands to one declaration index. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadClasses`, `ReadModuleHeader` | The writer emits class name/size/internal/mutable/fields; the reader reads exactly those fields. `Magic=0x4D525254` is `TRRM`; the next field is an **i32** private revision. The active reader explicitly admits 104 and 106, not a Standard u16 major/minor pair. |
| [`Binder.ManagedClasses.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ManagedClasses.cs) | Class-name collection selects the primary partial declaration; source binding now accepts `unique class` and declaration finalization/imported-snapshot rebuilding preserve its nullable fact. Generic ownership, field/cleanup and runtime paths remain incomplete. |
| [`CompilationReference.cs`](../../sources/Terrum.CodeAnalysis/Semantics/CompilationReference.cs), [`BoundTreeTraversal.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundTreeTraversal.cs) | References and `BoundPrivateClassSnapshot` retain class arrays. `RewriteTypes(BoundClass)` and the existing record-based remappers preserve the new fact; the current private byte format does not. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.StaticProtocols.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.StaticProtocols.cs), [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | Source ownership predicates and generic Copy/unique domains consume known class ownership; legacy imported ownership remains unknown and fails closed where the domain requires a fact. Field ownership, Cleanup and full runtime transfer remain incomplete. |
| [`ModuleCompiler.PrivateNominalOrigins.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateNominalOrigins.cs), `SameClass` in `ModuleCompiler.cs` | Projection and nominal-provider checks retain original class snapshots. Recursive comparison checks size, visibility, mutability, fields and their type closure, but has no class uniqueness dimension to compare. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Class-table merging/deduplication invokes `SameClass`; graph libraries have a separate reader that also invokes `ReadClasses`. Both routes need the same schema and ownership checks. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [sets](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [graph packages](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | TNPK/TNSP/TNGP version 3 containers embed semantic Contracts and delegate their checking to semantic snapshots/reference/link paths. Outer version, image hash and native-profile label supply no missing class fact. |

These are source observations, not newly executed validation.
The [implementation inventory](../implementation/modules.md#definitions-and-scope)
owns the currently selected private revisions. The
[persistence taxonomy](../specs/representation/persistence.md),
[format lifecycle](../specs/representation/format-versioning.md),
[complete types](../specs/language/types-and-normalization.md),
[binding/replacement](../specs/representation/binding-and-replacement.md) and
[consumer trust](../specs/representation/reader-trust.md) remain governing
contracts. No accepted text is replaced.

Scope is the existing bounded, closed, nongeneric private class table and
its owned-definition/reference expectations. Preserve existing layout,
access, nominal identity and construction-role restrictions. Add no generic,
static-class or class-union publication, new source rule, class layout/header,
allocation/GC/cleanup ABI, native calling convention or value-crossing route.
No other private record acquires a field or new variant.

### Existing proposals and alternatives

| Candidate or owner | Disposition |
|---|---|
| **One mandatory class Boolean in a distinct 106-based revision — recommended** | Mirrors the accepted independent uniqueness modifier; adds one byte per class and no owner/reference machinery. Exact schema selection makes clear a known ordinary fact, not an absent fact. |
| Amend unpublished 106 in place | Permissible; 106 is not frozen. It would require a distinct whole agreement, for example a coordinated successor to `106/-2`, because the same header would then precede different class extents. Prefer a new i32 revision to make the changed decoder boundary explicit; do not support both spellings as aliases. |
| Separate mandatory class-ownership clause/table | Materially different: permits independently available facts, but needs an owner selector, count/extent, exactly-one coverage for each required class, duplicate/missing/orphan checks, tags and remapping. A concrete alternative would use `i32 count` and eight-byte rows `i32 canonicalClassType, u8 ownership (1=aliasable, 2=unique), u8 reserved=0, u16 reserved=0`; missing ownership would remain unknown. Not selected: the existing class table already carries the complete retained class snapshot. No such directory or clause is allocated. |
| Tri-state byte on every class | Could encode unknown/ordinary/unique in the new wire schema. Not selected: a complete private class snapshot must publish a known contract. Unknown remains an in-memory availability state, not an incomplete successful artifact. |
| [Private nominal-class references](private-nominal-class-reference.md) | Reuse its reference-only role, original snapshots, origins and maps. Its no-new-class-bytes recommendation is amended **only for this successor's ownership fact**. `OwnedDefinition` means nominal implementation ownership, not a unique handle; neither it nor `IsExported` is a substitute. Its revision-105 selection is not this decoder. |
| [Private nominal-origin inputs](private-105-nominal-origin-inputs.md) | Preserve explicit declaration/proxy/provider keys and snapshot association. Add no ownership input or name-derived key: identity assignment cannot assert an absent source fact. |
| [Standard C1M1U1 ownership](standard-trrm-c1-class-declaration-ownership.md) | Separate carrier/selection for the same accepted distinction. Its TypeDef `0x40`, metadata-only declarations and generic publication are not composed, amended or prerequisites here. |
| [G107](private-generic-method-persistence.md), [G108S](private-span-type-term-persistence.md) | Separate private open-method/type-term candidates. Neither supplies class ownership. Numeric ordering does not compose their directories, formal flags, tags or Span terms into this class-only successor. |
| Infer ownership from class kind, mutability, fields, Cleanup absence, factory names or a successful old producer | Rejected. Those observations cannot distinguish the two accepted handle contracts or reconstruct an original expectation. |

## Exact schema and revision selection

Select ordinally and case-sensitively:

**`terrum/private-semantic-109-class-handle-ownership-1`**, abbreviated
**CH109**.

Its base is the exact
[`terrum/private-semantic-106-raw-union-return-coverage-2`](private-raw-union-return-coverage.md#exact-selection-and-framing)
agreement, with only the class insertion and ownership-preservation/checking
rules here. Preserve the active bounded 106 selection: foreign-interface
directory count and foreign-member discriminator remain zero; this does not
activate the wider referenced foreign-interface candidate.
The inherited declaration order is the reconciled
[106 order](private-generic-method-persistence.md#exact-base-and-header):
after checked output states, retain return coverage, CopyOrigin, the
foreign-member discriminator, expected-direct descriptor, expected-vtable
descriptor and conditional Function in that order. That link supplies the
base-order clarification, **not** G107's added records or admission.
All other base fields, widths, type codes, operation tags, contracts and
support restrictions remain unchanged.

Use `TRRM` followed by little-endian **i32 109**, bytes **`6D 00 00 00`** at
offset 4. Revision 109 is a proposed selection, not an implemented constant
or stable assignment. The new class byte must not be written under 104,
106, 107 or 108.

A writer operating under CH109 emits 109 for producers, consumers and graph
libraries, including class-free artifacts and outputs whose class table
becomes empty after lawful projection. This is not a request to change
unrelated existing private routes or make 109 the default for all compilation.
The selected CH109 root and every supplied provider/embedded semantic Contract
use this exact agreement before schema-dependent decoding or binding.
Implementing this route includes an exact CH109 writer for class-free
participants; class-count sniffing is not schema selection.

Existing separately selected 104/106 routes may remain independently
supported; they are not CH109 aliases or a retry path. No historical
conversion or compatibility shim is selected. Regenerate required artifacts
from complete checked facts under CH109; do not patch headers, insert guessed
false bytes, reuse optional source/debug data or obtain missing expected
ownership from a replacement provider. G108S or Standard composition would
need a separately selected whole agreement.

For native containers, select **outer version 3 / inner CH109**. Keep outer
TNPK/TNSP/TNGP fields and widths unchanged; rebuild affected Contract lengths,
offsets and hashes. Outer version 3 alone selects neither CH109 nor a class
execution agreement.

### Exact class record

Keep the class directory after Structures and before ImportedStructures.
Immediately after each class's `isMutable` byte and before `fieldCount`,
insert one byte `isUnique`:

```text
i32 classCount
Class classes[classCount]

Class:
    str name
    i32 size
    u8 isInternal
    u8 isMutable
    u8 isUnique                    // NEW: 0=aliasable, 1=unique
    i32 fieldCount
    ClassField fields[fieldCount]

ClassField:                        // unchanged
    str name
    i32 type
    i32 offset
    i32 size
    u8 isPrivate
    u8 isInternal
    u8 isMutable
```

Integers retain little-endian encoding. `str` is the inherited nonnegative
i32 UTF-8 byte length plus exactly that many strict UTF-8 bytes, without
terminator/padding, at most 1,024 bytes. All Boolean bytes are exactly
`00` or `01`; `02`/`FF` are not unknown or extension markers. There is no
presence byte, optional trailer, Contract-string token, per-qualified-use
duplicate or new reference. The added extent is exactly `classCount` bytes.

Retain the 256-class limit, 32-field bound, existing class name/field/layout
validity checks and **i32 class geometry**. In particular, this does not
admit empty/oversized owned layouts currently refused by the verifier or
widen class fields with revision 104's inline-struct u64 geometry.
Existing artifact/node/depth limits remain in force; added bytes count
against the 4 MiB semantic-artifact limit. Preflight extents/counts with
checked arithmetic before allocation.

The byte describes class-handle duplication rights, independently of
`isMutable` and `isInternal`. It is mandatory on every retained row: private
implementation, exported definition, hidden dependency and imported original
expectation. A trivial unique class without authored Cleanup remains unique.
An ordinary handle aliases one object; its Copy contract does not require
copying that object's fields or duplicating object-level Cleanup.

## Availability and consumer checks

The proposed host carrier is **`BoundClass.IsUnique : bool? { get; init; }`**,
defaulting to `null`, not `false`. Preserve the existing positional
constructor and other properties. This is a C# carrier recommendation, not
source syntax or an additional wire record:

| Value | Meaning and permitted publication |
|---|---|
| `null` | Unknown: no retained ownership evidence. May remain in an otherwise supported opaque/inspection context; cannot satisfy an ownership-dependent use or be written as a CH109 class. |
| `false` | Known ordinary aliasable handle contract, supplied by checked source or an exactly selected CH109 `00` byte. |
| `true` | Known unique owning handle contract, supplied by checked source or an exactly selected CH109 `01` byte. |

Checked declaration composition supplies the fact before class snapshots
are finalized. Preserve it across partial declarations, reference projection,
hidden class reconstruction and all rewrites. A host-created descriptor with
an omitted property is unknown. A nullable comparison such as
`IsUnique != true` must not become positive Copy evidence.
Older private records contain no CH109 ownership assertion: merely reading
104/106, or observing that one producer used an ordinary-only source subset,
does not supply a CH109 known-false fact. No legacy upgrade rule is selected.

All four existing class forms resolve to the same declaration fact:
canonical `1024+i`, optional `1280+i`, writable `1536+i` and optional-writable
`1792+i`, for the same valid row `i`. Preserve their independent access and
nullability; writable does not mean unique and optional does not mean Copy.
The fact does not classify the stored pointer/reference/box/owner cell as
unique merely because it mentions the class. Apply each complete constructor's
existing rules. This introduces no GC class form or new private type code;
existing intrinsic managed families outside these bands keep their own
contracts and are not fallback identities for a missing row.

Before an already admitted ownership-dependent consumer use:

| Retained class-handle fact | Requires Copy | Requires unique | Requires known Copy or unique |
|---|---|---|---|
| Known aliasable | Ownership requirement satisfied | Refuse | Ownership requirement satisfied |
| Known unique | Refuse | Ownership requirement satisfied | Ownership requirement satisfied |
| Unknown | Refuse | Refuse | Refuse |

These are necessary ownership checks, not sufficient formation, execution
or transport admission. Apply them consistently to imported handle copying,
transfer/borrow checks, supported queries/domains and recursively required
class facts. A pointer to a unique class does not inherit the pointee's
duplication restriction. No open-private-generic codec or new domain is
selected by this table.

Consumers check the selected schema/header, extents, closed Boolean domain,
class/type selectors, original-reference mappings, required support and
binding before affected use. Producers owe truthful facts; consumers need
not reconstruct source or semantically prove a Body's unique discipline.
A structurally consistent false byte is producer misconduct, not a newly
required source/Body audit. Existing verification is not weakened.

## Writers, binding, replacement and re-emission

Canonical writers emit the known Boolean from the checked declaration or
retained original view, never from field shape, selected provider operations
or a profile string. Unknown on **any retained class row** refuses CH109
output, even if no Body mentions that class. Pruning can remove an unneeded
row and its references under existing closure rules; it cannot erase a
required expectation to avoid this obligation.

Retain the property in `Module.Classes`, `CompilationReference.Classes`,
every `BoundPrivateClassSnapshot.Classes` array, mapped reference copies and
reconstructed `BoundProgram.Classes`. It needs no second collection or
snapshot field. Shared traversal and each manual constructor/remapper must
preserve availability as well as value. Dropping Bodies or diagnostic source
data cannot remove class ownership.

Resolve existing explicit Module/declaration/export keys and class kind
before comparing contracts. Keep nominal `OwnedDefinition` versus
`ImportedExpectation` and reference-only versus locally implemented roles
separate from handle uniqueness. Add known ownership equality to **every**
class pair in `SameClass` and all corresponding provider, original-snapshot,
set and graph comparisons, including recursively reached classes.

Replacement must preserve this dimension **exactly in both directions**.
Aliasable-to-unique and unique-to-aliasable both fail even with identical
names, fields, size, access and mutability. Unknown is neither a wildcard
nor equal evidence for ownership-dependent binding. Compare every original
expectation in its using context, including uncalled/type-only imports and
diamonds; retain any additional provider facts actually relied upon.
Do not overwrite the expected value with the actual provider's value after
an earlier successful binding.

Existing class maps still relate canonical selectors and all qualified
uses in each input context. Rebuild them with dependent structure, checked
reference, pointer, function-pointer, fixed-array and enum mappings wherever
the selected base admits those types. Rewrite declaration strings by their
existing type grammar, typed places/field-owner paths and every relevant
Body type, not matching numeric words or substrings. The new Boolean itself
is a scalar, not an index to remap.

Deduplicate same-owned-origin classes only after complete expected/actual
comparison, including ownership, succeeds. Keep distinct using expectations
even if their actual row is shared. Reindexing or provider enumeration order
must not select an arbitrary ownership value; conflicting copies refuse.
Schema, ownership, snapshot or provider changes invalidate affected cached
checks before reuse.

Source-free re-emission decodes the selected input and rebuilds complete
destination records, counts, maps, references and containing extents/hashes.
It preserves known ownership, origins, original expectations, construction
roles and unchanged independent obligations. It cannot convert an imported
class to an owned definition or fabricate a local construction pair.
Ownership equality does not replace existing size/field/access checks or
introduce resilient layout.

## Failure, prerequisites and provisional direction

| Boundary | Required failure behavior |
|---|---|
| Wrong revision/agreement, truncated class byte, Boolean outside 0/1, invalid counts/type maps or malformed retained records | Reject through existing private format failure handling (`ModuleFormatException`); no alternate parse, skipped class or partial successful reference/output. |
| Missing ownership evidence at publication/binding, incompatible ownership/provider, unavailable required closure or unsupported affected operation | Refuse through existing module contract handling (`ModuleContractException`). Source binding uses its existing diagnostics; normal argument-shape failures remain unchanged. No new diagnostic/exception family is selected. |
| Native image admission or regeneration | Check all existing target/runtime/ABI/lifetime obligations. Reject unsupported unique-handle value operations before lowering/admission; do not reinterpret them as shared retention or silently regenerate/patch provider images. |

Proceed provisionally in this order once implementation is authorized:

1. Establish checked ordinary/unique class facts and ownership-aware consumer
   checking under the **already accepted** language contract. The source
   binder's current rejection is not removed by this document. Keep unknown
   distinct and preserve existing unsupported-operation refusals.
2. Implement CH109's exact writer, ordinary reader and separate graph reader,
   with complete class/reference/snapshot propagation. Retain all other
   106/-2 records and existing class construction/reference invariants.
3. Integrate ownership comparison and original-view preservation through
   private provider replacement, reference sets, graph libraries and embedded
   native Contracts; re-emit and reconsume without provider source.
4. Qualify the matrix below using public compiler APIs and independent
   fixtures. This is a private persistence gate, not acceptance of class
   runtime execution or a broader source milestone.

The existing [class-reference proposal](private-nominal-class-reference.md)
and nominal-origin inputs supply identity/role prerequisites, not uniqueness.
Already supported ordinary-class paths must remain supported under CH109.
Source/checker ownership facts now exist, but the current private writer cannot
preserve them and must refuse unique-class producer output until CH109.
Metadata ownership support alone supplies no unique-class allocation,
Copy/move machine implementation, Cleanup/root/GC ABI or cross-image support.
Those are separate gates, not new decisions made or excused here.

## Required conformance cases

These are future acceptance cases, **not test results**. Extend the existing
[private class-reference fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/PrivateNominalClassPointerFieldTests.cs),
[private module fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/PrivateRevision106BodyTests.cs)
and [construction-invariant fixtures](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeManagedClassVerifierTests.cs)
through public APIs. The
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
currently has no Compile exclusions. Do not replace independent decoding
fixtures with only the same writer's round trips.

| Area | Required outcome |
|---|---|
| Truthful production and basic decoding | Unrelated ordinary/unique declarations, each mutable/immutable and with admitted visibility/layout, retain all four mutability/ownership combinations. Include trivial nonempty payloads with no authored Cleanup. Source binding now supplies the ownership fact; unique-class producers remain unavailable until the selected private schema can preserve it. |
| Exact bytes and framing | Independently encode both ownership bytes; verify their position between mutability and field count and unchanged field/type widths. Reject truncation, `02`, `FF`, wrong header/selection, malformed counts and trailing data. CH109 with zero classes remains 109 and obeys unchanged Module/function validity. |
| Availability and qualifiers | Omitted host fact and older-schema facts remain unknown; Copy, unique and either-domain uses all refuse unknown. Known unique never becomes Copy through optional/write qualification or reconstruction. Pointers/references retain their own duplication rules. Do not accidentally enable unsupported value operations. |
| Original expectations and replacement | Same keys/names/layout but opposite ownership fails both directions at root and nested class pairs. Bodyless/uncalled expectations retain the original value after a compatible replacement and after a later incompatible replacement. Mutability/access mismatches still fail independently. |
| Sets, graphs and diamonds | Two independent providers may both use class row zero. Remap without capture; reverse provider order. Same-origin agreeing diamonds deduplicate only after comparison; conflicting ownership and a different nominal owner fail. Preserve every using-context expectation and metadata-only dependency edge. |
| Source-free re-emission | Strip source/debug, permute valid class/type/function rows and change provider-local indexes. Re-emit producer, consumer, reference-set/graph-library closures and supported outer-3 packages; reopen them and revalidate ownership and original maps with no source or name rebinding. |
| Limits and malformed closure | Exercise class rows 0 and 255, the 256-row bound and rejection at 257, existing field/layout bounds, resource-limit refusal, invalid qualified selectors and inconsistent snapshot maps. Added ownership bytes count toward artifact length. |
| Isolation and retained gates | Reject older/sibling schemas in the CH109 route, including G108S and Standard C1M1U1; do not sniff or upgrade. Preserve rejection of owned descriptors without construction pairs, unsupported class-bearing native boundaries and unrepresentable required closure. Type-only preservation claims no runtime result. |
| Trust boundary | A deliberately false but structurally consistent ownership assertion is an optional producer-truth audit case, not a mandatory consumer semantic-reproof test. Malformed Boolean/reference data and incompatible providers are mandatory rejection cases. |

The recommendation is fully selected **within this unapproved candidate**:
one nullable host fact, one mandatory wire Boolean, exact CH109 over 106/-2,
no historical conversion and no new private record. Approval/adoption of
that selection remains open. Any later G108S composition, broader class
families or unique-class runtime admission needs its own explicit scope and
acceptance gate; none is silently selected here.
