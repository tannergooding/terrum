# Standard foreign-interface parameter labels and defaults

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This proposes one exact composition, not
normative adoption, numeric allocation, compiler support or qualification.
Existing specifications, profiles and proposals remain unchanged.

## Finding and recommendation

**F+I cannot retain the implemented reusable source-call surface.**
The [foreign-interface owner](native-output-foreign-interface-method-identity.md#candidate-source-contract)
requires ordinary explicit-formal label/overload rules and separate-compilation
identity. Its [F+I selection](trrm-foreign-native-interface-schema-selection.md#exact-proposed-selection)
adds the foreign role but preserves F's catalog; it selects neither the
parameter surface nor the completed scalar frozen-value registration.

Inspected evidence, not newly executed validation:

| Evidence | Consequence |
|---|---|
| [Source binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ForeignNativeInterface.cs), active `ForeignNativeInterfaceCallsKeepExactOwnerAndMapLabelsAndDefaults` [regression](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs) | Labels map to the selected member's explicit ordinals; omitted non-output inputs use its checked typed default. Receiver identity and source-order staging remain separate. |
| [Metadata Param contract](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [schema dispatch](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) | Structural MethodSig/Param rows carry neither labels nor ordinary defaults. F+I does not select `UsesMethodParameterLabels`, `UsesClassParameterDefaults` or `UsesScalarFrozenValues`. |
| [Surface reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmMethodParameterLabelTableReader.cs), [Feature closure](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs) | Label-only `0066` rows are 12 bytes; the class-default route uses 16 bytes, Feature 26 and nonzero defaults' Feature 11. Those gates do not admit foreign owners. F's incomplete `11 -> 9` catalog is not SF1. |

An already completed CallSite contains full arguments, not the declaration's
labels/defaults for an unseen caller. Optional source/debug data, names guessed
from a Body, or a scalar-shaped Blob cannot repair the missing required facts.

Recommend **one distinct F+I+SF1+PS1 agreement** below. Reuse the
[D1 surface](standard-trrm-interface-parameter-defaults.md#complete-parameter-surface)
and [CM1 record](standard-trrm-c1-class-parameter-defaults.md#declaration-surface-and-exact-wire-contract),
but select only foreign-member ownership, not their general profile/owner sets.
A sparse default table would need another assignment and association; adding
fields to shared Param would conflate declaration views. Widening F+I in place
is permissible before release, but does not preserve the requested distinct
selection. Composing all of G1/C1 would admit unrelated contracts.

## One exact provisional agreement

| Selection | Candidate |
|---|---|
| Case-sensitive out-of-band identity | `trrm/1-draft-foreign-native-interface-parameter-surface-1` |
| Distinct internal `TrRmArtifactSchema` selector | `ForeignNativeInterfaceParameterSurfaceDraft` |
| Complete composition | The complete existing F+I selection, plus the complete Standard-facing SF1 scalar registration/codec, plus PS1 below |
| Format / governing edition / foreign target | `(1,0)` / `1` / `windows-x64-native/1`, TargetProfile `(1,1)` |

**SF1** is [scalar frozen-value registration](scalar-frozen-value-registration.md):
the complete 24-fixed-scalar ConstantMap/ConstantSource/FrozenValueNode/Blob
codec, including non-Body uses, exact bits, validity, interning and remapping.
It replaces `11 -> 9` by `11 -> []` **only in this new whole schema**.
Preserve F's bounded six phase-3 selectors and `10 -> []`; do not import
SF1's baseline `10 -> 11` edge. Keep incomplete `12 -> {10,11}`.
No general phase-1/2 graph or Feature 17 is selected.

**PS1** selects the exact foreign-member surface below. It adds this bounded
owner applicability to candidate `26/1`, without selecting G1/L1, D1/CM1,
receiver-access, class/construction or generic-call profiles.
F+I's `004C`, NativeCallable, receiver-first CallSite, output/dependency,
dispatch and binding rules otherwise remain intact, as does all of F.

Keep `trrm/1-draft-foreign-invocation-contracts-1` and
`trrm/1-draft-foreign-native-interface-contracts-1` unchanged.
This identity is no alias, default or combinable flag. It selects no F+S,
fixed-array, L1P, CopyOrigin, RootPin, private or CLI composition.
Revision 1 means this exact agreed snapshot of the governing owners/catalog,
not future moving text at their links; changed contents need explicit selection.

## Required PS1 declaration surface

Select candidate **`0066=MethodParameterSurface`**, required directory policy
**`0000`**, fixed stride **16**, no offset index or trailer:

| Offset | Little-endian field |
|---:|---|
| 0 | `u32 ownerMethodDef` |
| 4 | `u32 ordinal` |
| 8 | `u32 name` |
| 12 | `u32 defaultValue` |

`ownerMethodDef` and `name` are required local typed MethodDef/String rows;
nonzero `defaultValue` names ConstantSource `0030`. Row references are
one-based `1..00FF_FFFF`, high byte zero, bounded by their actual tables,
not DefIds, Imports, TypeSpecRefs or Body-map indexes. `ordinal` is full-width
`u32`, zero-based **explicit logical Param** position, never receiver/slot/ABI
position. Check all extents, `16*recordCount` and summed coverage without
overflow; global row limits and Body's `u16` capacities remain distinct.

The owner set is exactly every retained MethodDef selected by an
InterfaceMethod of an admitted `004C` foreign interface, including uncalled
local declarations and consumer-local expected copies. The complete foreign
owner/member relationship must validate first. Other ordinary methods,
interfaces, anchors, helpers and generic formals acquire no surface.

Require exactly one row per explicit Param, including required/output/unit
positions. Zero-parameter owners have no rows; the section is absent exactly
when no rows exist. Owner/ordinal pairs and decoded names within each view are
unique. Reuse [L1's identifier rules](standard-trrm-g1-interface-parameter-labels.md#record-inputs-and-closure),
without case folding, normalization or invented `argN` labels.
Existing Export aliases share one declaration vector; distinct expected views
do not merge through equal providers, keys or shared MethodSigs. Canonical row
order is MethodDef then ordinal; valid permutations remain ordinary input.

`defaultValue=0` means **no default**, not unknown/unrepresentable.
A nonzero value is exactly **ConstantSource kind 1 -> FrozenValueNode kind 1
-> SF1 scalar Blob**, with normalized complete root type equal to the Param
type in that declaration's context. Ordinary value mode/flags are `00/00`;
receiver, NativeOutput, reference/init or other ineligible positions cannot
default. Unit has nonzero source/node references and zero Blob reference;
exact floating/BID bits survive without host numeric conversion.

Otherwise valid defaults outside SF1, including native-width values, nominal
or aggregate values, backing/address identities and open/evaluation sources,
refuse retained publication. They never become zero, required-only parameters
or runtime thunks, even if all observed calls supplied arguments. This is a
bounded carrier, not publication support for the entire source-default domain.

## Complete conditional Feature closure

All pairs below have exact revision **1** and governing edition **1**.
Compute each Module's least closure from its explicit requirements and all
retained declarations, expectations, values, Bodies and dependencies.
The profile itself and another Module's rows supply no missing requirement.

| Capability | Immediate prerequisites in this one schema |
|---|---|
| `1`, `13` | `[]`; preserve native-boundary and layout-query scopes |
| `2`, `7`, `16` | `[1]`; preserve F's independent retained-use triggers |
| `3`, `5`, `6` | `[1,2]`; 5 includes F+I's foreign declaration/dispatch scope |
| `4` | `[3]`; preserve native export/address/indirect-use scope |
| `8` | `[1,2,10]`; output-bearing contracts/use |
| `10` | `[]`; exactly F's six bounded phase-3 selectors |
| `11` | `[]`; retained SF1 value paths, including nonzero PS1 defaults |
| `12` | `[10,11]`; still incomplete and unsupported where required |
| `26` | `[]`; every retained PS1 owner, even uncalled or zero-parameter |

A foreign-member surface without defaults/outputs requires
**`{1,2,5,26}`**. A default adds **11**; outputs add **8 and 10**.
Together the closure is **`{1,2,5,8,10,11,26}`**.
Neither 3, 4, 9, 12, 13 nor 16 follows merely from those facts.
Independent calls, addresses, NativeParameter `000E`, retained queries,
unwinding and dependencies retain their own triggers and transitive closure.
Compile-time formation alone does not require 12. Feature 9 remains pending;
unselected richer families and incomplete required capabilities remain gates.
No Feature word chooses a stride, owner set or decoder.

## Source, binding and preservation obligations

* Capture checked declaration names and formed defaults before rewriting.
  Restore that exact surface for source binding: map supplied labels/positions,
  check omission eligibility and rank only supplied evidence, then fill the
  selected overload's omissions.
  Capture the receiver once, stage written arguments once in source order,
  and append omitted constants in formal order before invocation. The completed
  CallSite passes all explicit formals; no new ABI/default sentinel appears.
  Preserve caller-unsafety, receiver access, output reservations and provenance.
* Preserve each **original expected** surface separately from the actual
  provider. Label/default differences alone do not invalidate otherwise
  compatible binary binding: completed old calls already pass their selected
  values. Fresh source can observe the selected provider's new surface.
  Keep unequal expected proxies/Imports during packing rather than overwrite
  them. F+I's exact foreign owner/member/slot/signature/output checks still apply.
* Select every input and destination explicitly before schema-dependent
  indexing/writing/loading under [format versioning](../specs/representation/format-versioning.md).
  Carry one immutable selection through all components. Separately selected
  F/F+I inputs can bind only with complete supported retained-contract checks;
  they cannot recover absent surfaces. Unknown/unsupported selections reject
  without retries, aliases, default upgrades or partial output.
* Source-free re-encoding remaps Module/owner partitions, MethodDef and
  InterfaceMethod links, MethodSig/Param/TypeSpec ranges, Strings, surface
  references, source/node/Blob graphs, Imports/Exports, `004C` profiles,
  contracts and Body maps together. Preserve exact scalar payloads and original
  source views; rebuild affected ranges/directories/SourceMaps, not bytes.
  A destination unable to retain the closure refuses publication.
* Consumers check framing, namespaces, complete owner/vector coverage,
  identifiers, default eligibility/type/value validity, manifest/support and
  binding before affected exposure/use. Missing labels/defaults are not an
  empty-success fallback. [Producer truth](../specs/representation/reader-trust.md)
  remains distinct: do not rerun defaults or re-prove published Body facts.

## Approval boundary and future acceptance

Approval remains required for this identity/composition, foreign-owner
`0066`/16-byte applicability, Feature-26 scope and the whole-schema SF1
catalog amendment. F+I's existing `004C`/Feature-5/8/CallSite candidates remain
unapproved prerequisites. Adoption would amend
[metadata](../specs/il-metadata.md), [registry](../specs/representation/registry.md)
and [manifests](../specs/representation/semantic-manifests.md) together;
accepted source-call, exact-schema and directed-binding rules are preserved.
Compiler integration and qualification are separate, still-open gates.

Future cases, **not results from this documentation task**:

* Fresh source-free imported calls: reordered labels, nontrailing omissions,
  required/output positions, overloaded exact owner selection and once-only
  receiver/argument effects; reject duplicate/unknown labels and omitted outputs.
* Complete uncalled/expected/zero-parameter views and independent Module
  closures; malformed/missing/extra rows, wrong references/types, 12-byte rows,
  invalid scalar payloads and unsupported defaults must refuse affected use.
* Defaults versus absence, unit where independently admitted, all admitted
  SF1 endpoints and exact floating/BID bits; no invented 9/12 dependency.
* Unequal expected/provider labels/defaults, reordered/shared value rows,
  source-free packing and another unseen caller; preserve original views.
  Reject surface content under old F/F+I and unsupported wider compositions
  without decoder trials. Common-subset bytes do not reveal their origin.
