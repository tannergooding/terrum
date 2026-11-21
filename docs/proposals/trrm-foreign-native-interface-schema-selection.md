# Exact Standard draft schema for foreign native interfaces

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This selects a proposed schema boundary, not
normative approval, a numeric allocation, implementation support or evidence
of execution. Existing proposals and accepted owners remain unchanged.

## Decision and scope

Select **one new whole-artifact identity for F + I**, composing the existing
ForeignInvocation/NativeOutput contracts with the
[foreign-interface candidate](native-output-foreign-interface-method-identity.md).
Do not reinterpret an existing F identity or introduce a second output codec.

The interface owner's requirement for exact draft agreement does not yet
name that composition. [Format versioning](../specs/representation/format-versioning.md)
and [admission](../specs/representation/requirements-and-admission.md) require
agreement on all governing records and semantics, not equal `(1,0)` bytes or
a registry digest. Amending F in place is permissible before release, but a
distinct selection keeps this trial from silently widening existing callers.
That choice imposes no historical-reader or draft-compatibility obligation.

## Exact proposed selection

| Selection | Proposed value |
|---|---|
| Case-sensitive out-of-band identity | `trrm/1-draft-foreign-native-interface-contracts-1` |
| Distinct internal `TrRmArtifactSchema` member | `ForeignNativeInterfaceDraft` |
| Complete composition | F amended by I, as defined below |
| Format pair | `(1,0)`; no new on-wire schema field |

**F** is precisely B amended by F in
[exact draft-schema selection](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities),
whose identity is `trrm/1-draft-foreign-invocation-contracts-1`.
Retain its entire schema and catalog, including shared OutputState/Dependency,
NativeOutputCells `8/1`, bounded phase-3 selectors `10/1`, DF/Body `0C`,
E9/Body `0D`, and NativeParameter `000E`/`16/1`. An implementation's limited
direct-output operation does not remove unsupported retained F obligations.

**I** is precisely the Standard-facing delta of the
[foreign-interface owner](native-output-foreign-interface-method-identity.md#proposed-standard-metadata-delta):
the complete nominal, flat, bodyless owner/member relationship, opaque
receiver projection, receiver-first CallSite correspondence, dispatch,
binding/replacement and remapping rules. Include candidate required section
`0x004C`, policy `0x0000`, stride 12, with exactly
`(u32 ownerInterfaceDef, u32 targetProfileRef, u32 vtableByteOffset)`.
Its target is `windows-x64-native/1`, TargetProfile `(1,1)`.
**`0x004C` remains an unapproved candidate, not an allocation.**

Integrate I into existing **NativeInterfaceCalls `5/1`**, retaining prerequisites
`{1/1,2/1}`. Every retained foreign-interface declaration/expectation/use
requires 5; output-bearing contracts additionally require **8/1**, deriving
`{1/1,2/1,10/1}`. Thus a basic interface-output use has closure
`{1/1,2/1,5/1,8/1,10/1}`. Neither 3 nor 16 follows merely from that use;
independent direct calls or NativeParameter content retain their requirements.
No new NativeParameter applicability on foreign members is selected.

Reuse the assigned MethodDef-only NativeCallable clause **`0x000C`**, expanded
to one effective convention per member under the
[source normalization owner](native-calling-convention-source-schema.md#exact-normalization-inheritance-and-witnesses).
That source/Core prerequisite adds no separate wire identity, inherited
convention sentinel or duplicate convention table. Normalized readers need
not reconstruct source attributes to consume the clause.

F+I does not select the parameter-name/default surface. Every retained FNI
MethodSig in this profile therefore has zero explicit Params; parameterized
FNI members require the separate F+I+SF1+PS1 selection in
[Foreign-native-interface parameter surface selection](trrm-foreign-native-interface-parameter-surface-schema-selection.md).
Do not treat absent labels/defaults as positional-only declarations or retry a
different profile.

Otherwise preserve F's catalog and applicability. This defines no F+S,
fixed-array A2/A3, L1/L1P, CopyOrigin, general-graph or RootPin composite.
Private interface/convention revisions and native wrappers are not included.
Revision 1 denotes this complete composition and the exact agreed snapshot
of its governing owners/catalog, not future moving text at their links.
Changed contents require another explicit exact selection; the string alone
does not establish agreement on that snapshot.

## Reader and writer obligations

1. Select each input and the destination explicitly, using existing
   schema-bearing API inputs, before schema-dependent indexing, row acceptance
   or loading. Unknown, missing, wrong-case or unsupported selections fail
   without decoder trials, aliases or partial output. Common-subset bytes can
   be valid under multiple explicitly agreed schemas; bytes cannot recover
   the originating selection.
2. Carry one immutable selection through every section, metadata/Body
   reader/writer, catalog, validator, binding, cache and lowering context.
   Coordinate the exact mapping and provider gates in
   [Standard consumption](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs),
   [source emission](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs),
   [indexing](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) and
   [framing](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactWriter.cs).
   Adding the string to an allowlist while retaining an F/F+S dispatch
   ternary is insufficient. Defaults and existing exact identities do not
   acquire this candidate.
3. Emit `004C` whenever foreign-interface facts are retained, including
   declaration-only inputs; omit no required member or output fact.
   Check the owner's row extent, typed reference namespaces/ranges, reserved
   high bytes, uniqueness, complete member ranges, role, receiver, convention,
   targets and per-Module closure before affected use. Unsupported retained
   facts refuse use, never become empty collections. Producers publish
   truthful facts; consumers need not re-prove foreign behavior or Body truth
   ([reader obligations](../specs/representation/reader-trust.md)).
4. Preserve the interface role during binding and lowering. An abstract
   member is not a NativeImport, OS export or direct-import provider thunk.
   The existing NativeOutput provider-set predicates and
   [direct-import binding plan](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs)
   do not establish interface support. Unsupported operations remain gated
   until their complete retained identity/receiver/output path is implemented.
5. Give every separate dependency its own exact selection; a root or container
   supplies none. Binding separately supported F and F+I inputs is conditional
   on complete retained-contract checks, not profile equality or an alias.
   Packing selects one destination that represents every retained obligation.
   Rebuild typed owner/member/profile references, ranges, maps and output
   correspondence under their owners; header patching or dropping requirements
   is not conversion. F+I cannot pack unselected additional composites.

## Future acceptance and remaining decisions

These are future checks, not results from this documentation task:

* Consume local and separately imported complete interfaces, with and without
  outputs; preserve exact conventions and the distinct requirement closures.
* Reject `004C` under old F/F+S or default Standard, unknown selections and
  mixed component contexts without retry. Test common-subset selection too.
* Reject truncated/duplicate rows, wrong-table or high-byte references,
  incomplete members and missing retained requirements even without a call.
* Reorder and re-encode valid owner/member/profile rows while preserving
  original import expectations, receiver/formal maps and output contracts;
  refuse destinations that cannot represent the retained closure.

Formal approval of this identity/composition and the interface owner's
`004C`, Feature-5/8 applicability and CallSite amendments remains outstanding.
Implementation and qualification are separate gates. Wider composites and a
CLI selection surface are not chosen here; fixed/default commands must not
auto-enable this draft. Private persistence remains with its separate owner.
