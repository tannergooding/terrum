# Direct-native-import expectations in private semantic modules

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** The applicable instruction is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This recommendation selects private expectation transport and comparison,
not normative adoption, implementation support or released stability. It
does not approve adjacent syntax, schemas, APIs or runtime policy.

## Decision and boundary

Retain an **import-only expected direct-native descriptor** on an ordinary
private `ModuleFunction` when its imported `Function` payload is removed.
The descriptor contains the original library name, foreign export name and
complete NativeParameter vector. It belongs to the importing declaration,
not to the provider being considered for replacement.

Amend the **unpublished candidate private revision 97** with the mandatory
presence field and conditional record below. Keep owned DirectNativeImport
Function payloads unchanged: a definition retains its descriptor there,
whereas a declaration-only import retains it in the new expectation record.
There are not two copies on one ModuleFunction.

Use the already separate candidate-97
[convention](native-calling-convention-private-persistence.md) and
[resolution](native-import-resolution-private-persistence.md) Contract
tokens as their sole serialized authorities. Do not add convention or
resolution fields to this descriptor. Keep NativeOutput's declaration-owned
role/collection and its separate comparison unchanged.

The scope is the existing bounded private Windows callable-import domain,
with or without native inputs or outputs. No static-data codec, foreign
interface role, new function-address permission, loader implementation or
Standard TRRM encoding is selected.

## Owners and observed gap

The accepted contracts reached through the [specification index](../specs/README.md)
remain the baseline:

| Owner | Obligation preserved |
|---|---|
| [Private persistence](../specs/representation/persistence.md), [format versioning](../specs/representation/format-versioning.md) and [conformance](../specs/overview/conformance.md) | Private storage is not normative interchange; exact draft selection precedes decoding, independently of version bytes or filenames. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md) | Resolve selected identity first; retain the original expectation and every relied-on fact; compare complete normalized types and all supported contract dimensions before substitution. |
| [Consumer verification](../specs/representation/reader-trust.md) and [admission](../specs/representation/requirements-and-admission.md) | Producers publish valid, truthful facts; consumers check structural safe consumption, required support and binding before affected use without re-proving arbitrary bodies or foreign behavior. |
| [Native roles](../specs/native.md#native-declaration-roles) and [Windows deployment](../specs/targets/windows-x64-deployment.md) | Preserve foreign symbol spelling, effective convention/resolution, caller-unsafety, actual target support and code/library lifetime. |
| [Modules](../specs/modules.md), [sets](../specs/module-sets.md), [graphs](../specs/native-graph.md) and [JIT](../specs/jit.md) | Semantic relowering and independently precompiled composition are distinct; every retained edge and origin obligation survives either route. |

These are inspected source boundaries, not newly executed validation:

| Evidence | Consequence |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `CreateReference`, `ImportDeclarations`, `Declaration` | Export projection retains DirectNativeImport in memory while stripping the body and setting `IsExternal`/`IsModuleReference`. The original descriptor is available before consumer persistence. |
| Same file, `Contract`, `CreateConsumer`, `ReadFunctions`, `Link` | Contract retains signature/visibility/roles, NativeCallable/entry, vtable parameters, no-return and caller-unsafety, but not the direct library/export/input vector. Imported ModuleFunctions lose Function; ordinary linking compares other contracts without the missing descriptor. |
| Same file, `PutFunction`, `GetFunction`, `SameDirectNativeParameters` | Owned direct payloads already carry library/export and 14-byte input records. The helper compares corresponding formal, normalized pointee identity, access, retention and full `uint` extent, but ordinary ModuleCompiler binding does not call it. |
| [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`Graph`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Consumer creation, graph import validation and restoration are separate paths. `RecordedImportPromises` overlays selected flags on a replacement, not an original complete direct descriptor. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), direct-expectation provider validation | Outer native containers already retain and compare direct descriptors. Their Windows library comparison is ordinal-ignore-case; foreign exports and provider identities are ordinal. This is not a carrier for bare semantic imports. |
| [`NativeParameterExtentPersistenceTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeParameterExtentPersistenceTests.cs) and the [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj) | The active direct replacement regression requires the original `uint.MaxValue` provider to link and a `ushort.MaxValue` replacement to reject. The assigning investigation reports the latter currently accepted; the analogous vtable test rejects it. Keep the failing direct assertion unweakened. |

The [implementation reference](../implementation/modules.md) distinguishes
the current private revision-92 codec from the unapproved compositions.
The missing representation is a private semantic expectation decision,
not permission to weaken accepted retained-binding obligations.

## Carrier alternatives

| Alternative | Assessment |
|---|---|
| Extend the existing Contract String | Viable if names receive unambiguous framing/escaping and every pointee gets a complete typed reference. It needs parsing/restoration, type discovery/remapping and target-specific name comparison rather than raw fingerprint equality. The fixed 1,024-byte Contract bound also constrains descriptors whose binary vectors otherwise fit. Not selected. |
| **Dedicated import-only expectation record** | Recommended. Mirrors the existing expected-vtable attachment, reuses the direct descriptor's typed vector and comparison, and leaves owned Function fields and the token owners intact. Adds one mandatory presence byte per declaration and payload only where the descriptor would otherwise disappear. |
| Hoist every native binding into a declaration-wide variant and retire outer duplication | Coherent broader redesign, but would migrate owned functions, data/owner representations and outer containers. Those changes are unnecessary for this callable expectation gap. |
| Function-only or outer-only retention; recover from the replacement | Incomplete. Bare semantic imports have neither payload. A replacement-derived expectation makes comparison vacuous. |

No new target/name tokens are added to Contract. Its existing candidate-97
resolution/convention tokens identify the direct role even when its native
input and output vectors are empty.

## Exact schema composition

Select one amended **private i32 revision 97 after `TRRM`**, not a new 98,
a revision-92 extension or a Standard major/minor pair. The inspected codec
selects 92; the intervening candidates are not historical readers to retain.

| Candidate component | Meaning retained in this selection |
|---|---|
| 92 | [NoThrow retirement](no-throw-private-persistence.md) plus the complete [aggregate-output delta](aggregate-native-output-private-persistence.md), [scalar/pointer-cell amendment](native-pointer-output-ownership.md#exact-candidate-representation-delta), resolved arithmetic modes and reservation tag 41. |
| 93 | Mandatory [general declaration dependencies](reference-provenance-carriers.md#private-exchange-candidate-revision-93), separate from output-specific rows. |
| 94 | Complete [wide-scalar catalog and type-derived payloads](scalar-wide-value-carriers.md#exact-private-schema-and-composition). |
| 95 | Mandatory [copy-origin summary field](reference-field-value-provenance.md#necessary-private-transport-candidate-95). |
| 96 | [Nominal directories, foreign ModuleFunction variant and tag-5 target discriminator](native-output-foreign-interface-private-persistence.md#exact-revision-composition). |
| Existing 97 amendments | [Convention tokens/foreign variant adjustment](native-calling-convention-private-persistence.md#exact-composition-and-selection), [resolution tokens](native-import-resolution-private-persistence.md#exact-revision-selection), [complete pointer contracts and typed references](native-function-pointer-contract-private-persistence.md), and the [foreign-invocation pointer amendment](standard-native-import-function-pointer-contracts.md#private-persistence-interaction). |
| This amendment | The import-only descriptor field, full unsigned direct-input extent interpretation and complete binding/restoration obligations below. No other record placement or meaning changes. |

This intentionally supersedes only the earlier candidates' statements that
bare semantic imports lack full direct descriptors and therefore always
need original pre-erasure inputs for native promotion. It also supersedes
their no-new-ModuleFunction-field recommendation **for these descriptor
facts**, not for convention, resolution or pointer contracts. Their owned
Function payloads, token grammars and outer-3 field layouts remain intact.

All structured writer/reader, producer, reference, set, graph-library,
nested-Contract and snapshot paths select this same complete draft, including
artifacts with no direct imports. Exchange agrees out of band on the whole
composition. Equal 97 or outer-3 bytes, a profile/build label, an empty table
or plausible payload cannot select an earlier interpretation.

Under this selection reject other revisions and unsupported nested schemas;
do not use `>=97`, retry an old layout, infer a schema from presence bytes or
invent fallback readers. Earlier-97 drafts are not aliases. Regenerate from
complete checked inputs. Header rewriting is not conversion; a separately
selected conversion needs original facts and rebuilt references/extents,
not guessed defaults or a replacement descriptor. No reader can infer the
history of every relabelled stream that happens to satisfy its selected schema.

## Candidate record and validity

On **every ModuleFunction**, after the complete existing expected-vtable
record and before any conditional stored Function, insert:

```text
Boolean hasExpectedDirectNativeImport
if hasExpectedDirectNativeImport:
    String libraryName
    String foreignExportName
    i32 nativeParameterCount
    repeat nativeParameterCount:
        i32 formalOrdinal
        i32 pointeeType
        u8 access
        u8 retention
        u32 extent
// The existing Function payload follows iff hasFunction is true.
```

The preceding output/general-dependency/copy-origin collections and
candidate-96 foreign variant retain their positions. The new presence byte
also occurs after a foreign variant, where it must be zero. All multi-byte
integers are little-endian. Boolean is exactly u8 0/1; String is the inner
i32 UTF-8 byte length plus strict UTF-8, not the outer u16 codec.
The absent record is one byte; the present record is exactly
**`13 + libraryByteLength + exportByteLength + 14*n` bytes**.

The proposed internal attachment name is `ExpectedDirectNativeImport`;
it is not a public API or Terrum source form. Its descriptor omits outputs,
convention and resolution because their existing declaration carriers supply
those facts. Require all of the following:

* Presence is true **exactly** for an ordinary declaration-only consumer
  import carrying the candidate-97 direct-import token pair. Require
  `Import=true`, `hasFunction=false`, ordinary `foreignInterfaceRef=0`,
  no expected-vtable binding and no conflicting native role. This includes
  unused imports and imports without outputs or input annotations.
* Producer exports also use the existing Import flag, but have a Function:
  their expectation presence is false. Owned direct imports in consumers
  likewise use only their Function descriptor. Ordinary imports, foreign
  members and source-owned direct declarations cannot carry this record.
  Tokens/payload/presence disagreement rejects; an absent record is not an
  unknown descriptor or permission to obtain one from a provider.
* Names preserve their original spelling. Reuse the bounded private Windows
  library domain: 5–256 ASCII characters, `.dll` suffix ignoring ASCII case,
  and only letters, digits, `_`, `-`, `.`. Export names are 1–256 printable
  non-space ASCII characters. These are private admission bounds, not a
  replacement for the [separate source LibraryName candidate](native-import-library-name-source-contract.md)
  or target symbol rules. Do not infer paths, suffixes or decorations.
* Require `0 <= nativeParameterCount <= explicitFormalCount <= 256`.
  Zero means no NativeParameter annotations, not unknown annotations.
  Records are strictly increasing by zero-based explicit formal ordinal,
  unique and in range. Direct callables have no implicit receiver; a static
  member's owning type does not occupy a parameter position. The record's
  ordinal is the existing private i32 projection, not the source annotation's
  one-based argument or the outer table's u32 field.
* Reuse private input access bytes `0=Read`, `1=Write`, `2=ReadWrite`;
  retention is `0=NoEscape`. Unsupported MayEscape and unknown bytes reject;
  retaining a retention field does not admit a new escaping protocol.
  Pointee, formal pointer type/access and ordinary input mode must agree
  under existing direct-input rules. The admitted opaque-void case remains
  Read/NoEscape/extent-zero. Write/ReadWrite requires writable initialized
  backing, not NativeOutput formation.
* `pointeeType` belongs to the **importing Module's complete private type
  universe**. Validate scalar/nominal, pointer and complete fnptr references
  and their required closure even when only this annotation refers to them.
  It is not a provider-local row, Standard TypeSpecRef or layout-sized alias.
* `extent` is the full **u32 `0..4,294,967,295`** domain. Zero retains the
  existing no-static-minimum meaning; positive values retain the existing
  minimum element-count meaning. It is not a byte length or allocation
  request. The owned direct-input and existing outer direct-input vectors
  use this same interpretation. This supersedes older candidate prose
  labelling those direct-input fields i32/nonnegative, without changing
  their four-byte width or position. Never narrow to ushort or signed int.
* NativeParameter formals are disjoint from the separate NativeOutput
  collection. A nonempty output collection requires its existing direct
  role and complete validity checks; empty output metadata cannot identify
  or erase the direct role.

Retain the 4 MiB artifact, 1,024-function, 1,024-byte generic String,
256-slot, 64-call-argument, 100,000-node and depth-128 private limits.
Apply the stricter direct-name limits above. Check counts, `14*n`,
string/record extents and remaining bytes with checked wide arithmetic before
allocation/consumption. Truncation, invalid UTF-8/Boolean, duplicate or
unordered formals, wrong references, conflicting roles and unsupported
required types reject before affected use. No new per-record budget or
success-shaped empty fallback is introduced.

## Complete comparison

First resolve the selected provider/export/owner in its existing binding
context; then compare the retained declaration against that provider.
The descriptor is attached to that import, not an independent lookup key.

| Dimension | Candidate relation and reason |
|---|---|
| Selected provider, export key and declaring owner | **Exact identity.** Preserve the using Module, provider selection, declaration kind and owning type/member context. Equal foreign targets do not merge providers. |
| Direct-native role/presence | **Exact.** An ordinary body, vtable method, foreign member or different external role cannot replace the expected direct import merely by matching signature/unsafe/output facts. |
| Library name | For this bounded **private Windows mapping only**, use the existing ordinal-ignore-case relation documented by the outer provider candidate and implemented by native provider validation. Preserve spelling; do not broaden this to general paths, other targets, Module identities or export keys. |
| Foreign export name | **Exact ordinal ASCII**, case-sensitive. Compare the resolved original entry name, not a replacement declaration's name or inferred A/W/decoration/suffix. |
| Calling convention | **Exact normalized identity**, obtained only from the [convention token owner](native-calling-convention-private-persistence.md). Equal Windows x64 carriers do not equate distinct conventions. |
| Resolution mode/policy | **Exact normalized identity**, obtained only from the [resolution token owner](native-import-resolution-private-persistence.md). Link has no policy; Link/Dynamic and Default/CompatibilitySearch have no strengthening relation. |
| NativeParameter presence and formal set | **Exact.** Compare every annotation at its corresponding logical formal; omission is not NoEscape, an unknown bound or an extra guarantee. Labels and physical table indexes do not establish correspondence. |
| NativeParameter pointee | **Exact complete normalized type identity** in each using context, including nominal provider/owner, nested pointer access and complete fnptr invocation contracts. Equal carriers, names or raw type codes are insufficient. |
| NativeParameter access, retention and extent | **Exact per formal**, including all 32 extent bits. No access attenuation, retention strengthening or numeric inequality is selected for replacement; the existing helper and vtable expectation path use equality, and no broader private protocol variance is defined here. Reject extent changes in either direction. |
| NativeOutput | Keep **separate exact normalized role/collection comparison**: formal/type, formation, publication phase, success/complement selectors and states, and dependencies. This descriptor neither duplicates nor replaces it. |
| Other declaration obligations | Preserve their existing relations: exact callable/type/layout/ownership/phase dimensions, directed no-return/caller-domain obligations and supported dependency/guarantee comparisons. Do not turn the entire declaration into byte equality or invent variance for another integer/flag. Raw imports remain caller-unsafe in every case. |

Use `SameDirectNativeParameters` as the existing comparison mechanism to
generalize, not as permission to compare unresolved fingerprints. Under the
complete-pointer amendment, textual `fnptr#i` references resolve in their
own Module: different indexes can mean equal types, and equal indexes can
mean different types. Any reused comparison must incorporate that amended
typed equality. Compare the whole finite contract closure before reporting
success; an in-progress recursive pair is not an accepted binding.

Ordinary semantic identities retain their [ordinal decoded-text rules](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics).
Native package identities separately retain exact case-sensitive ASCII
equality and target filename-collision rejection. Neither uses the DLL-name
case relation.

## Capture, restoration and transformations

| Boundary | Required integration |
|---|---|
| Producer/reference projection | Keep owned Function descriptors truthful and structurally valid. `Declaration`/CreateReference preserve them in the reference declaration while stripping implementation, including complete annotation-only type closure and original owner/provider context. An imported proxy is not a new provider-owned export. |
| CreateConsumer and CreateConsumerAgainstReference | Compare the expected direct role/descriptor as well as existing contracts against the selected original provider **before** Function erasure. Capture the expected declaration's own descriptor and tokens, not the provider's current Contract or extra facts. Persist the new attachment after remapping. |
| ReadFunctions and source-free reconstruction | Decode the original expectation without needing a provider. Validate its formals/types against the original declaration, join the independently parsed convention/resolution tokens and output collection, and reconstruct a complete expected DirectNativeImport view. Preserve module-reference status from checked ownership/binding, not a serialized authorization Boolean. |
| Link, RestoreConsumerAgainstReference and RecordedImportPromises paths | Check every original descriptor before replacing a proxy or using provider facts. Restore original expected views, not replacement metadata with only effect flags overwritten. Provider implementations remain distinct from expectations; success does not rewrite the saved expectation. |
| Reference sets and graph roots/intermediates/leaves | Apply the same comparison to each edge, including declaration-only/semantic-only and address-only dependencies. Keep each using expectation in diamonds; no weakest/last-wins merge. Existing ambiguous-name and unsupported-topology gates remain. |
| Consumer/Provider/GraphContractSnapshot and caches | Retain original descriptor, tokens, outputs, complete type context and selected binding together. Cached admission is reusable only for unchanged artifact/schema/binding/target/rule inputs; a previously accepted provider or matching revision number is insufficient. |
| Packing, pruning and remapping | Discover the new vector's types before pruning; remap structures/nominal origins, pointer tables, complete fnptr signatures and every descriptor pointee into the destination using Module. Rebuild typed Contract references, outputs/dependencies/copy origins, formal maps, function indexes and outer joins with their own namespaces. No raw-byte substitution. |

For the single-provider semantic API, selection is its explicitly supplied
producer and exact exported declaration name under the current no-alias
boundary. Where BoundModuleImport/native set/graph metadata carries provider
identity/key, retain and validate that exact selection. This attachment adds
no identity generator, alias API or alternate-provider search. If a selected
owner/provider context required for a use cannot be represented, reject that
use rather than infer identity from a foreign DLL, type name or table order.

Re-encoding retains the original expectation when an import becomes a direct
reference. Keep its import/binding record if existing carriers cannot otherwise
preserve that obligation; this proposal assigns no hidden proof blob.
Rebuild lengths, offsets, maps and checksums from decoded facts. A physical
type/table reorder is compatible only when complete identities/contracts and
origins remain unchanged.

## Native containers and Standard TRRM remain separate

Keep the [outer-version-3 provider table and leaf-thunk design](native-output-direct-import-provider-binding.md)
unchanged. TNPK/TNSP/TNGP rows still join to the original imported
declaration by their checked local-name/provider/key relationship. Under the
amended inner selection, cross-check the outer descriptor against the new
inner expectation and the selected provider; conflicting copies reject,
with no precedence rule. Writers copy original spelling; logical comparison
uses the relations above and resolves both vectors in the importing
Contract's final type universe.

The new inner record permits recovery of a previously persisted **amended-97**
expectation without original source; it supplies no missing descriptor for
92 or earlier-97 artifacts. Native promotion still requires every independent
owner, target, ABI, image and lifetime fact. Preserve the selected consumer
provider-DLL/export route and real provider leaf/IAT validation. Do not
bypass that provider, rebuild precompiled providers in root-only graph JIT,
or infer that the prototype's SystemDirectory route implements Link or
Dynamic. Actual resolution support remains with its existing owner.

Outer version 3 selects no inner schema by itself. Its field widths and
placements remain unchanged, including the full u32 direct-input extent.
Changed inner bytes require rebuilt Contract lengths, following offsets and
checksums; metadata changes are not proof that an old image implements them.

Only `.trrm` is normative serialized semantic interchange. Its NativeImport
row, Module/Import identities, complete TypeSpecs and independently proposed
NativeParameter/foreign-invocation contracts remain separate. This private
Boolean/record is not a Standard clause, Feature, DefId, row or decoder.
Neither TRRM magic nor equal field widths establish cross-format compatibility.

## Required cross-feature cases

These are proposed acceptance cases, not test results. Extend the existing
public compiler API tests and independent exact-byte fixtures; keep source
checking, structural admission, emission and execution evidence distinct.

| Family | Required observations |
|---|---|
| Existing regression | Preserve `PrivateModuleProviderReplacementRejectsChangedNativeParameterExtent`: original `uint.MaxValue` links, `ushort.MaxValue` replacement rejects. Keep the vtable contrast and existing outer native-provider test without weakening assertions or excluding the direct case. |
| Presence/role | Round-trip imported direct declarations with zero/nonzero inputs and no/one/multiple outputs, including uncalled imports. Ordinary imports, owned direct Function records, producer exports and foreign variants have the mandatory false byte. Reject missing/extra records, tokens without the required descriptor and direct-to-ordinary/vtable/foreign replacement. |
| Foreign targets | Unchanged target permits otherwise compatible private body/image changes. Reject changed DLL beyond the selected Windows case relation and changed export, including case-only export changes. A library-only ASCII case change follows that private relation; provider/key case changes never do. No replacement-name default or suffix inference. |
| Every input dimension | Independently change/add/remove formal annotations, pointee/nominal origin, pointer qualification, nested fnptr contract, access, retention and extent. Reject every mismatch; MayEscape/unknown retention remains unsupported. A zero input vector is distinct from a missing descriptor. |
| Unsigned boundaries | Preserve `0`, `1`, `65,535`, `65,536`, `int.MaxValue`, `int.MaxValue+1u` and `uint.MaxValue` in owned payloads and expectations. Include same-low-word/high-word-different replacements in both directions. No signed rejection, truncation, clamping or allocation proportional to extent. |
| Source-free lifecycle | Remove source/debug and imported Function payloads; reference projection, persisted consumer, Link/restoration and snapshots retain original facts. Replacing the provider before reconstruction cannot redefine the expectation. Test both initial consumer construction mismatch and later replacement mismatch. |
| Type remapping | Reorder/prune structures, native-pointer and complete fnptr tables, including annotation-only types and different `fnptr#i` indexes. Equivalent identities bind; coincident numeric indexes, equal layouts or same-named types from different owners do not. Preserve every outer/inner join after re-encoding. |
| Owners/sets/graphs | Single provider, reordered flat providers, root/intermediate/leaf and diamond paths check all using expectations and retained origins. Equal foreign targets cannot merge providers; wrong owner/key, ambiguous selection and proxy re-export reject. No emitted call/IAT entry is required for a retained obligation to matter. |
| NativeOutput separation | Changed output formal/type/formation/selector/state/phase/dependencies still rejects with an unchanged new descriptor. Input/output overlap rejects. Preserve reservation, cancellation-before-invoke, normal publication and exceptional indeterminacy; NativeParameter Write never initializes a vacant output. |
| 93–96 composition | General dependencies, wide-scalar values, copy-origin summaries and nominal directories remain mandatory and semantically independent. Cover an ordinary direct expectation beside a foreign member, whose new presence byte remains false. Unsupported prerequisite forms reject rather than disappearing. |
| Other 97 amendments | Exercise each normalized convention and resolution token with the descriptor, including no inputs/outputs, and complete nested fnptr/pointer projections when independently admitted. Changed convention/resolution rejects despite equal x64 carriers; address contracts preserve the same original provider edge. Unsupported target/address/private-reference forms remain gated. |
| Malformed/bounds | Independent fixtures cover strict Boolean, invalid UTF-8, empty/over-limit names, negative/oversized counts, truncation, checked extent arithmetic, duplicate/unsorted/out-of-range formals, wrong type namespaces, access/retention values, conflicting roles and 4 MiB/shared-budget boundaries. All u32 extent bit patterns remain numeric values, not malformed signed integers. |
| Exact schema/snapshots | Explicitly select the complete amended 97. Reject other inner revisions, unselected earlier-97 layouts, wrong nested schemas and stale cached contexts without decoder retry. Do not claim provenance detection for byte-identical valid streams. No header-only upgrade or reduced composition. |
| Native outer integration | Join amended-97 expectations to existing outer-3 rows; reject disagreement even with recomputed valid checksums. Preserve selected provider leaves, actual image checks and AOT/root-only-JIT routing. Promotion without remaining target/binding facts rejects; Standard/Development rows are not private aliases. |

Producers must publish valid, truthful descriptors and matching code.
Consumers check the selected schema, framing, bounded references, supported
roles and every binding before affected reconstruction/lowering/execution.
This does not require semantic re-proof of arbitrary provider bodies or
foreign promises. A checksum, export name or successful load cannot discharge
those producer obligations.

## Review boundary and remaining gates

The candidate chooses the carrier, placement, unsigned extent domain,
comparison relations and revision composition; no additional choice within
this bounded descriptor gap is deferred. Formal review/approval of this and
its unapproved prerequisites remains outstanding.

Full-schema integration, supplied logical provider/owner identities, actual
Link/Dynamic and convention support, unavailable richer type/data/address
codecs and native qualification remain separate gates under their existing
owners. None is supplied by retaining this record or by the user's permission
to proceed provisionally. This document changes no normative owner, source
code, test, other proposal or milestone acceptance.
