# Declared function-pointer effects in Development TRRM

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This is a bounded Development persistence
decision, not approval, implementation evidence or a Standard assignment.
It changes neither normative `.trrm` nor private ModuleCompiler `.trm`
candidate 97.

## Decision and scope

**Reuse the signature anchor's existing MethodDef-owned Effects clause for
the signature's declared `EffectsMask`; derive native-role caller-unsafety
separately from NativeAbi.** Apply declared Effects to NativeImport role `1`,
NativeCallable role `2` and signature-anchor role `3`, including bodyless
declarations and admitted import expectations. Leaving role `1` on effective
Effects would still change its complete address type on reconstruction.
Keep NativeVTable role `4`'s existing required Effects `04`, explicit-unsafe
eligibility and no-return prohibition. Keep the eight-byte Development
signature row and six-byte TypeSpec unchanged.

Select one new, explicit out-of-band whole-artifact candidate:
**`trrm/1-development-function-pointer-declared-effects-1`**, with proposed
internal selector **`DevelopmentFunctionPointerEffectsDraft`**. Both names
and every schema amendment below are **unapproved**. Its exact composition
is the existing Development schema, amended only by this document.
Keep the original `Development`, Standard and other draft selections
unchanged. No Feature ID/revision, section, clause, opcode, Body record or
header field is allocated.

This deliberately admits **Effects-only signature contracts**, not every
contract the in-memory type can carry. Require empty Selectors, OutputStates,
Dependencies and NativeParameters on every retained function-pointer
signature. Do not remove their rejection merely to pass an Effects case.
Existing Development convention, value/ABI, address-view, Body and execution
restrictions remain independent gates; this proposal adds no foreign
convention encoding or new addressable target family.

| Unapproved alternative | Assessment |
|---|---|
| **Reuse owned Effects for roles `1/2/3` — recommended** | The anchor already has an exclusive Contract range and NativeAbi separately identifies each native role. Preserves declared `04` versus role-imposed unsafety without another field, owner or reference. Requires changing NativeImport as well as NativeCallable production, validation, matching and restoration, not just relaxing an anchor mask check. |
| Change roles `2/3` but leave NativeImport role `1` on effective Effects | Reject: a direct import with no declared unsafe effect would be serialized as `04`, then disagree with its declared-mask address signature or acquire a different complete type. Direct imports are bodyless and can be source address targets; this is not repaired by inspecting a body. |
| Put Effects in the signature row's reserved word, or append a structural contract payload | Can preserve the distinction while retaining the anchor's old role-only Effects `04`, but creates two Effects authorities or requires removing the old one. A complete payload also needs selector/output/dependency/parameter framing and actual-target correspondence. None is needed for the bounded Effects gap. The reserved word remains zero. |
| Reuse Development output/parameter clauses as a complete pointer carrier | Their applicability and payloads do not transport every normalized signature fact. A future complete carrier needs an explicit decision; substituting those tuples or the private-97 payload is not this selection. |

## Evidence and preserved owners

These are source observations, not newly executed tests.

| Repository evidence | Consequence |
|---|---|
| [Private pointer proposal, lines 16–20](native-function-pointer-contract-private-persistence.md#L16-L20) and [83–85](native-function-pointer-contract-private-persistence.md#L83-L85) | Selects ModuleCompiler candidate 97 and explicitly excludes the Development signature schema. Its [i32-97 selection](native-function-pointer-contract-private-persistence.md#L482-L490) is not a Development profile. |
| [Source unsafe modifier, lines 2508–2509](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L2508-L2509), [NativeImport role, line 3056](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs#L3056), [NativeImport signature, lines 860–898](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs#L860-L898) | Explicit `unsafe` sets declared and effective caller-unsafety; NativeImport independently sets only effective caller-unsafety. Its address signature uses `DoesNotReturn` and `DeclaredRequiresUnsafeCaller`, not effective `RequiresUnsafeCaller`. Thus direct imports with declared masks `0` and `4` are distinct despite both requiring unsafe callers. |
| [Signature writer/reader, lines 11–35 and 78–100](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeFunctionPointerSignatureTable.cs#L11-L100) | The eight-byte row names a synthetic MethodDef; the reader requires NativeAbi `(1,3,0)`, Effects `04` and exactly two contracts. [Matching and interning](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeFunctionPointerSignatureTable.cs#L144-L192) currently compare shape; [canonical IDs](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeFunctionPointerSignatureTable.cs#L264-L307) omit invocation facts. |
| [Producer gate, lines 78–83](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L78-L83), [declaration production, lines 563–594](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L563-L594), [anchor production, lines 628–656](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L628-L656), [signature loading, lines 1066–1091](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1066-L1091) | Emission uses the private-92 emptiness guard, NativeImport and NativeCallable declarations serialize effective unsafety, anchors hardcode Effects `04`, and loading constructs a default-contract signature. This is not a complete declared-effects round trip. |
| [Effect loading, lines 1230–1231](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1230-L1231), [declaration reconstruction, lines 1330–1343](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1330-L1343) | The loader restores `DoesNotReturn` and effective `RequiresUnsafeCaller`, but not `DeclaredRequiresUnsafeCaller`. Merely admitting a zero-mask role-1 row would make the loaded import effectively safe unless role-derived reconstruction changes too. |
| [Contract reader, lines 539–545](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs#L539-L545), [Development NativeImport reader, lines 428–431](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeImportTable.cs#L428-L431), [NativeCallable reader, lines 79–88](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeCallableTable.cs#L79-L88), [NativeVTable reader, lines 78–80](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeVTableTable.cs#L78-L80) | The global check requires bit `04` for every NativeAbi role, and role `1` independently requires that bit. Role `2` requires `04` and forbids `02`; role `4` does likewise. Relaxing only anchor/callable checks leaves a role-1 contradiction; globally deleting all role checks would also lose role `4`'s retained restriction. |
| [Signature definition, lines 19–38](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs#L19-L38), [declared-effects regression, lines 1930–1979](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs#L1930-L1979) | The in-memory contract has its own Effects mask. Active NativeCallable cases distinguish masks `0/2/4` from effective role-unsafety and currently expect both persistence routes to reject. |
| [Addressable NativeImport, lines 1034–1062](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs#L1034-L1062), [address projection, lines 852–857](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs#L852-L857), [Development address/convention gates, lines 84–116](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L84-L116) | Source address formation can select a direct NativeImport and project its contract. Development separately rejects projected address views and non-`Native` conventions; this amendment does not remove either gate or supply a carrier for the import signature's nonempty invocation collections. |
| [Vtable regression, lines 365–406](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeVTableCallTests.cs#L365-L406) | Its consumer combines same-shaped NativeCallable methods with implicit role-unsafety and explicit declared unsafety, then uses Development emission/loading. The supplied failure is consistent with the producer gate; it was not rerun here. |
| [Implementation summary, lines 714–729](../implementation.md#L714-L729), [dependency regression, lines 2323–2365](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs#L2323-L2365) | Documents complete-fact rejection; nonempty dependency contracts are an active independent rejection case, not an Effects default. |

The [specification index](../specs/README.md#semantic-interchange) leads to
the controlling [persistence taxonomy](../specs/representation/persistence.md),
[exact draft agreement](../specs/representation/format-versioning.md),
[admission](../specs/representation/requirements-and-admission.md),
[binding](../specs/representation/binding-and-replacement.md),
[reader obligations](../specs/representation/reader-trust.md) and
[canonicality](../specs/representation/encoding-canonicality.md).
Preserve these rules. Reuse the assigned
[Effects meanings](../specs/representation/contract-clauses.md#3-effects)
and the provisional source proposal's
[declared/effective distinction](standard-function-pointer-source-contracts.md);
do not adopt the Standard anchor or NativeImport codecs as Development bytes.

## Exact candidate interpretation

All existing Development framing and reference namespaces remain unchanged:
format `TRRM`, little-endian `u16 (1,0)`; section `0033` rows exactly
`u32 signatureMethodDefRow, u32 reserved=0`; TypeSpec exactly
`u8 form=0D, u8 qualifiers=0, u32 signatureRow`.
The first reference is a one-based MethodDef row, the TypeSpec reference is
a one-based signature-table row, and neither is a DefId.

Keep the existing Development Contract framing, including its MethodDef
owner DefId, zero flags and offset-indexed rows. Effects kind `0001`
remains exactly twelve bytes including the header, with one `u32` mask.
NativeAbi kind `0010` remains sixteen bytes including the header, with
payload `u32 abiId=1, u8 role, u8 exceptionMode=0, u16 reserved=0`;
roles remain `1=NativeImport`, `2=NativeCallable`, `3=signature anchor`,
`4=NativeVTable`. No role, flag or convention value is added.

For signature anchors, retain unique NativeAbi `(abiId=1, role=3,
exceptionMode=0)`, module-level ownership, zero MethodDef flags/body,
no receiver/method generics and ordinary by-value parameters. The exclusive
range contains exactly NativeAbi plus at most one Effects clause; no other
kind is admitted. Remove the fixed two-clause requirement.

For roles `1/2/3`, absent Effects or one explicit zero normalize to `0`;
otherwise the declared mask is exactly `02`, `04` or `06`. Role `4` is
deliberately excluded from this declared-effects amendment.

| Subject | Candidate Effects interpretation | Effective caller requirement |
|---|---|---|
| NativeImport declaration, NativeAbi `(1,1,0)` | Its own declared mask `0/2/4/6`, including a bodyless direct import with no pointer uses. Never inject `04` merely because it is a NativeImport. | Role `1` requires an unsafe caller independently of declared `04`. |
| NativeCallable declaration, NativeAbi `(1,2,0)`, including a bodyless expected imported declaration | Its own declared mask `0/2/4/6`; compare it with its exact address signature. | Role `2` requires an unsafe caller independently of declared `04`. |
| Signature anchor / value type, NativeAbi `(1,3,0)` | The pointer type's declared `EffectsMask`, `0/2/4/6`. | Role `3` identifies an unsafe native invocation contract for every mask, not a callable MethodDef. The anchor remains noncallable/nonaddressable. |
| NativeVTable declaration or admitted expectation, NativeAbi `(1,4,0)` | **Exactly `04` remains required.** Preserve existing explicit-unsafe eligibility and reject DoesNotReturn; absent/zero/`02`/`06` do not satisfy this role. | Preserve its existing unsafe-caller requirement; this candidate does not generalize role `4` to declared-only Effects. |
| Other Development declarations | Unchanged by this candidate. | Preserve their existing requirements. |

Reject retired bit `01`, every unknown bit and duplicate Effects clauses,
even equal zeroes. For roles `1/2/3`, a canonical writer omits zero Effects
and otherwise keeps the existing Effects-before-NativeAbi order. Ordinary
reading accepts one explicit zero and consistent physical clause reordering.
Consequently an anchor has one canonical contract for mask zero and two
for a nonzero mask; an explicit-zero input may have two. This one/two-clause
restriction is anchor-only: actual declarations retain their independently
admitted clause kinds and ranges. Role `4` still requires its nonzero Effects
clause.

For roles `1/2/3`, restore `DoesNotReturn` from `02` and
`DeclaredRequiresUnsafeCaller` from declared `04`. The effective caller
requirement is declared `04` **or the validated native role**: roles `1/2/3`
each independently require an unsafe caller. For role `3`, this applies to
the represented pointer invocation, never an invocation of the anchor.
Loaded role-`1/2` functions and role-`2` expectations therefore have
`RequiresUnsafeCaller=true` even with mask zero. Never derive declared `04`
from effective caller-unsafety or clear effective unsafety because Effects
is absent. Role `4` continues to restore its existing required `04`.

Do not reject role-`1/2/3` masks `02/06` merely because the result is non-unit
or because the old NativeCallable reader forbids them. Keep role `4`'s
no-return prohibition; relaxing the candidate's role-`2` check is not a
global NativeAbi waiver.
No-return keeps the complete result/arguments, removes normal invocation
flow and retains the existing no-return lowering obligations. Unsupported
Body/lowering forms still decline use, not the promise.

### Declaration matching and bodyless expectations

Exact signature identity and interning include the normalized mask plus
the existing convention and complete ordered result/parameter identities,
recursively through nested pointers. Same-shaped masks `0/2/4/6` are distinct.
Whenever an admitted NativeImport or NativeCallable declaration is associated
with an address signature, compare that declaration's own normalized
declared mask and complete invocation inventory with the anchor. A role-`1`
declaration with mask zero does not match an otherwise equal `04` anchor;
explicit declared unsafe `04` does not match a zero anchor merely because
both calls are effectively unsafe. Shape-only first-match, selecting the
only same-shaped anchor or using effective `RequiresUnsafeCaller` as the mask
is invalid. Equal complete signature duplicates remain invalid under the
existing Development uniqueness rule. A direct import without a retained
pointer use does not acquire a required anchor.

A direct NativeImport is already bodyless. Its existing NativeImport row,
NativeAbi role and own Effects range determine its declaration; a body is
neither required nor available to recover declared unsafety. For an admitted
bodyless NativeCallable expected declaration, validate and match the same
mask inventory even though there is no local NativeCallable entry row or
Body. Preserve the existing expected-Import/provider-binding requirements;
do not manufacture a local entry or infer facts from the selected provider's
body. Binding retains every expected declaration's original mask and address
type, checks the existing directed provider relation, and does not replace
those expectations with a provider's possibly stronger facts. Role-`4`
expectations keep their existing `04` restriction.

This amendment adds no new Module Import kind: Development's admitted
bodyless provider expectations remain NativeCallable and NativeVTable, not
role-`1` Module Import proxies
([current loader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs#L1352-L1355)).
Direct NativeImport rows are a separate existing bodyless mechanism.
Source NativeAddress can select a NativeImport and can project declarations;
neither fact implies that Development can persist every such address.
Directed projection is not equality, does not widen the existing
address-operation view gate and cannot retag a foreign convention to `Native`.

## Other invocation facts: explicit unsupported boundary

The in-memory [signature definition](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs#L19-L38)
also carries the following indivisible facts. This candidate assigns them
**no Development signature encoding**. A nonempty collection rejects the
retained signature before output/use, even with mask zero, DoesNotReturn,
no calls or an apparently unused formal.

| Collection | Facts that must not be discarded or reconstructed |
|---|---|
| Selectors and OutputState | Phase, phase-local ordinal and exact result-test identity; each formal/selector/state occurrence and complete destination type. Preserve whole-value/whole-range meaning, result correlation and checked-versus-raw formation. Raw NativeOutput additionally needs the complete complementary state pair, vacant whole-cell protocol and its dependency set; a raw pointer is not a checked initialization reference. |
| Dependency | Destination kind/ordinal, source kind/ordinal, relationship and phase for every row. Explicit empty sources differ from absence; borrowing differs from backing forwarding and declared union; publication, capture and deferred phases cannot be collapsed. |
| NativeParameter | Formal ordinal, complete pointer/pointee identity and access, NoEscape/retention, extent kind/count and applicable target alignment/stride. Presence is significant; Write requires initialized backing and does not become an output reservation. |

The existing Development Boolean OutputState uses a different 16-byte,
mode-based form; its Dependency path does not decode the shared collection,
and bounded NativeParameter/NativeOutput clauses `0011/0012` are actual
declaration facts, not a complete signature codec
([contract reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs#L170-L250),
[native clauses](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs#L448-L514)).
They must not be copied to an anchor to bypass this boundary.
Existing independently supported direct native declarations retain their
own codecs, including bounded direct NativeParameter/NativeOutput clauses.
Their direct invocation remains a separate support gate. Source formation
of a NativeImport address may already retain those facts; **this profile
does not make NativeImport NativeParameter addresses persistable**. Reject
any retained address/signature with a nonempty collection above, rather than
matching it to an empty anchor, erasing the collection during projection or
treating the direct declaration codec as its signature carrier. Effects-only
declaration preservation also leaves convention and address-view gates intact.

The [NativeImport pointer proposal](standard-native-import-function-pointer-contracts.md#private-persistence-interaction)
amends Standard applicability and private 97 with these foreign facts,
not Development. Enabling nonempty collections here requires a later
explicit, complete Development carrier/profile decision. This bounded
choice neither resolves that decision nor declares those facts unnecessary.

## Implementation and selection obligations

* Carry the exact new selection explicitly through producer, artifact index,
  table/Body readers and writers, structural validation, reference creation,
  loading and binding. Keep `(1,0)` and existing Development Feature closure;
  neither Features `0100..0104`, filenames, extensions, an outer container,
  plausible rows nor private revision 97 select it. Default entry points
  retain their existing selection; candidate operations must opt in.
* Reject unknown/unsupported selections before schema-dependent indexing or
  writing, even with no signatures. Do not fall through a “not Development”
  branch into Standard. Do not retry another profile, infer a schema from
  Features or patch a header. Common-subset bytes cannot prove their origin;
  explicit agreement, not claimed detection of every old byte image, is the
  boundary. Old role-only `04` cannot be converted to a declared mask without
  complete checked inputs.
* Replace only this candidate's use of `CanPersistRevision92` with a
  profile-owned Effects/empty-collections admission check. Preserve the
  private-92 and legacy Development gates. In `TrRmExecutableCompiler`,
  publish role-`1/2` masks from `DoesNotReturn` and
  `DeclaredRequiresUnsafeCaller`, and role-`3` masks from the exact signature,
  not effective `RequiresUnsafeCaller` or the hardcoded anchor `04`.
  Leave role-`4` production and restrictions intact. Validate all retained
  types/declarations/expectations before publishing an artifact, including
  role-`1` declarations in artifacts with no signatures.
* In `TrRmContractTableReader`, replace the candidate's global NativeAbi
  bit-`04` requirement with the role matrix above: permit declared masks for
  roles `1/2/3`, retain required `04` for role `4`, and preserve all
  role/owner/uniqueness checks. In `TrRmDevelopmentNativeImportTableReader`,
  remove only the candidate's separate bit-`04` requirement; keep its
  `(1,1,0)`, NativeImport-row correspondence, bodyless/receiver-free/
  nongeneric, name, policy, parameter and result checks. Its table writer
  must validate under that same explicitly selected schema.
* Update `TrRmNativeFunctionPointerSignatureTableReader`'s anchor validation,
  canonical IDs and complete matching, and
  `TrRmNativeCallableTableReader`'s implemented **and bodyless expected**
  paths. Lift only the candidate role-`2` bit-`04` and DoesNotReturn
  restrictions. Keep role-`4` validation unchanged. In
  `TrRmExecutableCompiler` loading/reference reconstruction, restore declared
  masks and signatures before exact matching, then derive effective
  role-unsafety independently, including direct NativeImport declarations.
* Update every direct/indirect call's effective-unsafe check together with
  that reconstruction. Dropping forced role-`1/2/3` `04` must not authorize
  calls to anchors, safe calls to native entries, or unsafe operations inside
  a callee.
  Preserve no-return flow and scoped unsafe evidence through rewriting and
  lowering; consumers need not semantically re-prove producer bodies.
* Remap decoded signature rows, TypeSpec references, MethodDef/MethodSig/Param
  references, exclusive Contract ranges/owners and all Body type/method maps
  together. Re-intern using complete nested identities; preserve original
  import expectations and provider/code origins. Rebuild offsets, extents
  and affected source/debug references; do not substitute matching bytes.
  Include exact profile, binding and type context in verification/cache keys.

Bounds, reserved fields, row-count/type-count agreement, role-to-anchor
bijection, structural cycle/depth checks and checked wide extent arithmetic
remain mandatory. Support for this schema does not establish target execution
or a complete function-pointer milestone.

## Required tests and remaining decisions

* **Positive:** independently construct/read and producer/load/reference
  round-trip role-`1/2/3` masks `0/2/4/6`, including four same-shaped
  signatures, nested pointer uses and bodyless NativeCallable expectations
  with no local entry row or Body. Check declared/effective unsafe flags
  separately. Accept absent/explicit-zero equivalence for all three roles
  and verify canonical zero omission. Exercise no-return metadata/flow
  without executing an infinite target. Keep a role-`4` `04` case, including
  an admitted expectation, to verify its unchanged boundary.
* **NativeImport:** independently construct and producer/load/reference
  round-trip an otherwise admitted bodyless direct import with declared
  Effects `0` and one with explicit declared unsafe `4`, including artifacts
  with no signature rows. The former omits Effects canonically and restores
  declared unsafe false; the latter emits `04` and restores declared unsafe
  true; **both** restore effective unsafe true. A single explicit-zero row
  is equivalent to absence for role `1`. Check exact declaration/anchor
  matching distinguishes these masks rather than injecting role-unsafety
  into the key. Use already admitted Development convention/representation
  fixtures for positive persistence checks; do not treat source foreign
  NativeAddress cases as positive Development-emission evidence.
* **Integration:** run the existing declared-effects and selected-provider
  callback cases under explicit candidate selection; add the vtable regression's
  implicit-versus-explicit unsafe NativeCallable pair without relaxing its
  role-`4` declaration. Keep direct NativeImport, NativeCallable and indirect
  pointer invocation unsafe with mask zero; callable-body operations still
  need their own unsafe scope. Retain bodyless expected masks across binding
  to a satisfying provider without replacing them with provider facts.
  Legacy/default persistence rejection expectations remain profile-specific.
* **Negative:** reject `01`/unknown masks, duplicate Effects, wrong
  owner/role/range, nonzero reserved words, duplicate complete signatures,
  shape-only target matches and use of an anchor as a callable/address target.
  Reject role-`4` absent/explicit-zero/`02`/`06` Effects and no-return
  declarations. Reject a role-`1` row missing its NativeImport record, a
  NativeImport record on an incompatible role, invented role-`1` Module Import
  support, and a bodyless role-`2` expectation missing its retained binding.
  Include equal-shape role-`1/2` mask-`0` versus mask-`4` anchor mismatches
  even when both declarations are effectively unsafe.
  Independently reject each nonempty collection above, including an explicit
  empty-source Dependency row and NativeImport NativeParameter-only and raw
  NativeOutput address signatures with mask zero. Retain the existing
  dependency rejection and the foreign-convention/projected-address gates.
* **Boundary/isolation:** cover zero signatures, zero parameters, non-unit
  no-return results, nested structural cycles/depth limits, truncated rows,
  high-byte/out-of-range references and overflowing count/extents. Reject
  unsupported selections before parsing, including common-subset/empty inputs.
  Keep Standard seven-byte function-pointer TypeSpecs, private i32-97 and
  Development six-byte TypeSpecs under their separately selected schemas;
  no inferred conversion.

The revised unapproved recommendation covers roles `1/2/3` together and
preserves role `4`'s `04` restriction; no contract choice remains open within
that bounded Effects-only direction. A complete Development
selector/output/dependency/NativeParameter carrier, foreign convention
transport and projected-address representation remain separate concrete
decisions/gates. Supporting NativeImport addresses with those facts requires
those decisions, not another interpretation of declared `04`. This document
neither supplies their approval nor implements any candidate. No normative
specification, support inventory, compiler or test change is made here.
