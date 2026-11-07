# Development native conventions and import resolution

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This document selects a bounded Development
TRRM candidate, not normative adoption, implementation support or native
execution evidence. It changes no Standard or private structured-module
schema.

## Decision and scope

Recommend one explicitly selected successor to the
[declared-effects Development candidate](development-function-pointer-declared-effects.md):

* Put the normalized convention in NativeAbi roles `1/2/3`, preserving
  role-`3` internal `Native=0` as distinct Terrum-default pointer identity
  while adding the four foreign conventions. Role `4` keeps both reserved
  bytes zero and gains no convention. Keep the clause sixteen bytes, the
  signature-table row eight bytes and the function-pointer TypeSpec six bytes.
* Replace the Development NativeImport row's System32 policy word with
  explicit normalized resolution mode/policy and reserved bytes. Keep the
  row sixteen bytes and its three reference fields unchanged.
* Preserve the prerequisite's declared Effects and role-derived unsafety.
  Keep its empty-collection, representation, address-view and execution
  gates except for the convention/resolution transport restrictions
  explicitly replaced here.

The candidate identity is exactly, case-sensitively
**`trrm/1-development-native-conventions-resolution-1`**, with proposed
internal selector **`DevelopmentNativeConventionsDraft`**. Revision `1`
selects the complete existing Development schema, the precise declared-effects
amendment in its linked owner, and the deltas below. It does not select future
changes at those paths or whatever schema an implementation calls latest.
Exchange must agree on that complete schema and governing catalog.

Keep `TRRM` and the little-endian **u16 `(1,0)`** format pair. Add no header
discriminator, Feature, section, clause, instruction, Body record, target
assignment or private i32 revision. Existing `Development`,
`DevelopmentFunctionPointerEffectsDraft`, Standard and all other selections
keep their exact meanings. In particular, default `Emit`, `Load` and
`CreateReference` are **not** silently redirected to this candidate.
Candidate operations must opt in before schema-dependent work.

This is the smallest Development transport decision, not a source, foreign
ABI, NativeOutput, provider-routing or native-package redesign. Preserve
existing Development Native pointer anchors and their bounded invocation
behavior. Native data imports, complete pointer invocation collections,
projected address views and general Terrum addressability/call ABI remain
independently gated.

## Why a decision is needed

These are inspected source observations, not newly run tests.

| Evidence | Consequence |
|---|---|
| [`TrRmExecutableCompiler.Emit`, `EmitDevelopment` and `Load`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | Default emission/loading explicitly select Development. The producer refuses non-`Native` signatures/declarations and non-Link/no-policy imports. Merely deleting that guard would make loading substitute internal `Native=0` and default resolution rather than restore authored facts. |
| [`TrRmArtifactSchemaFacts`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) and the [declared-effects proposal](development-function-pointer-declared-effects.md) | The already-selected Effects draft changes declared masks, not convention or resolution transport; its owner explicitly retains the foreign-convention rejection. Neither existing Development selection represents this gap. |
| [`DevelopmentFunctionPointerSignatureReference`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableWriter.cs) and [`TrRmNativeFunctionPointerSignatureTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeFunctionPointerSignatureTable.cs) | TypeSpec `0x0D` names a signature-table row, which names a MethodDef anchor. Matching/canonical identities currently have no convention field to consult. |
| [`NativeAbiPayload`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) and [`TrRmContractTableReader`, kind `0x0010`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs) | The eight-byte payload is `u32 abiId=1, u8 role, u8 exceptionMode=0, u16 reserved=0`. Native role is present; normalized convention is not. |
| [Callable families](../specs/type-families.md#callable-values-and-tuples) and [`NativeFunctionPointerSignatureDefinition`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs) | The source default convention is Terrum; the signature table already accepts internal `Native=0`, and Development emission admits those signatures. The successor must preserve their identity, not discard them or reinterpret zero as Unmanaged. |
| [`NativeVTableMethodDefinition`/`NativeVTableCallContract`](../../sources/Terrum.CodeAnalysis/Semantics/NativeVTableCallContract.cs), [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) and [`TrRmNativeVTableTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeVTableTable.cs) | Role `4` retains Slot/Parameters/VTableByteOffset/Outputs, not an effective-convention field. Binder rejects combination with NativeCallable and does not populate NativeCallableConvention. The selected Development row retains NativeAbi `(1,4,0)` and bounded pointer/slot geometry; a new convention cannot be recovered from that carrier. |
| [`TrRmDevelopmentNativeImportTableWriter` and reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeImportTable.cs) | Development section `0x0030` contains method/library/export/policy as four u32 words. Its reader requires policy `1=WindowsSystem32`; this is not the Standard NativeImport row despite equal extent. |
| [`DirectNativeImport`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) and [`IsValidResolution`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs) | In-memory imports already distinguish convention and the three valid resolution alternatives. Those facts must survive unused declarations and source-free reference creation, not just call instructions. |
| [Native declaration roles](../specs/native.md#native-declaration-roles) and [Standard metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | The foreign convention domain, Link/Dynamic semantics, Standard sixteen-byte NativeImport row, `0x000C` NativeCallable clause and seven-byte `0x0D` TypeSpec are already defined. This is not a missing Standard convention allocation. |
| [`DirectNativeImportTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/DirectNativeImportTests.cs) | Tests expect default Development emit/load/reference and re-emission to preserve imports and pointer values. Those expectations, including legacy convention-prefix fixtures, do not approve a new Development schema or source alias. |

An implementation can use the already-defined Standard fields only by
selecting and implementing that different complete schema. It cannot put
them into Development while retaining the old selection. The reported
Release failures are consistent with this guard; their count and execution
outcomes were not independently reproduced for this documentation task.

Preserve the contracts reached through the
[specification index](../specs/README.md): [persistence separation](../specs/representation/persistence.md),
[exact draft agreement](../specs/representation/format-versioning.md),
[admission](../specs/representation/requirements-and-admission.md),
[binding](../specs/representation/binding-and-replacement.md),
[producer truth and consumer checking](../specs/representation/reader-trust.md)
and [canonicality](../specs/representation/encoding-canonicality.md).
This candidate intentionally replaces only its inherited NativeAbi
role-`1/2/3` reserved-byte rule, import policy-word meaning and foreign-fact
transport rejection. Role `4`'s reserved-byte rule remains unchanged.
It does not amend those accepted owners.

## Exact wire changes

All integers retain little-endian encoding. Reference namespaces, offset
indexes, section framing and exclusive owned ranges remain Development's.

### NativeAbi: one convention authority

Development Contract kind `0x0010` is exactly sixteen bytes:

| Byte offset | Field | Candidate meaning |
|---:|---|---|
| 0 | `u32 ownerDefId` | Existing MethodDef DefId: kind `0x16`, nonzero one-based 24-bit row. |
| 4 | `u16 kind` | Exactly `0x0010`. |
| 6 | `u16 flags` | Zero. |
| 8 | `u32 abiId` | Exactly `1`, retaining its existing Development meaning. Not a calling convention or new target ABI. |
| 12 | `u8 role` | Existing roles `1=NativeImport`, `2=NativeCallable`, `3=signature anchor`, `4=NativeVTable`. |
| 13 | `u8 exceptionMode` | Zero, unchanged. |
| 14 | `u8 conventionOrReserved` | Convention for roles `1/2/3`; reserved zero for role `4`, according to the exact matrix below. |
| 15 | `u8 reserved` | Zero. |

| Role | Valid byte 14 | Meaning |
|---:|---|---|
| `1` NativeImport | `0x01..0x04` | Required normalized foreign convention of the declaration. |
| `2` NativeCallable | `0x01..0x04` | Required normalized foreign convention of the declaration. |
| `3` signature anchor | `0x00..0x04` | Complete pointer convention, including the existing Native/Terrum default. |
| `4` NativeVTable | Exactly `0x00` | Reserved, not a convention. Byte 15 also remains zero. |

| Convention byte, only where the role admits it | Identity |
|---:|---|
| `0x00`, role `3` only | Terrum default; explicit mapping to existing internal `Native=0`. |
| `0x01` | Unmanaged |
| `0x02` | Cdecl |
| `0x03` | Stdcall |
| `0x04` | Vectorcall |
| `0x05..0xFF` | Invalid for roles `1/2/3`. |

Use explicit encode/decode mappings, not host-enum casts or `ToString()`.
NativeImport/NativeCallable argument omission has already normalized to
Unmanaged before production. Role-`3` zero explicitly preserves the
existing Native signature convention as Terrum-default identity; it is
never omission, unknown, inheritance, a consumer-selected default or an
alias for Unmanaged. Do not add zero to the source NativeCallingConvention
enum or admit zero on roles `1/2`. No `Native`/`NativeAbi` source alias is
introduced. The separate
[Standard Terrum-default candidate](standard-function-pointer-default-convention.md)
does not select this Development mapping or change Standard bytes here.

The owned clause is the sole convention authority for roles `1/2/3`.
Role `1` carries the direct import's convention even without a
function-pointer use. Role `2` carries the callable declaration's convention,
including an admitted bodyless expectation with no local entry row. Role
`3` carries the represented signature's convention; it remains a bodyless,
noncallable, nonaddressable anchor. Its zero mapping preserves the inherited
Native anchor representation, matching and bounded invocation behavior;
it adds no Terrum address family, general call ABI or Effects default.

**Role `4` stays outside the changed convention domain.** Its NativeAbi
payload remains exactly
`u32 abiId=1, u8 role=4, u8 exceptionMode=0, u16 reserved=0`.
Reject every nonzero byte at offsets 14 or 15, including `1..4`; do not
decode zero as Native or inject Unmanaged/NativeCallableConvention.
Refuse a producer input that attaches NativeCallableConvention to role `4`
rather than dropping that fact into a zero-reserved row.
Preserve its existing selected Windows x64 receiver/slot call contract
and role-to-record checks.
[Native declaration roles](../specs/native.md#native-declaration-roles)
assign effective conventions to NativeCallable/imports, not this distinct
prototype role. The [Windows x64 physical mapping](../specs/targets/windows-x64-call-interface.md)
and [bounded vtable lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeVTable.cs)
do not establish a normalized Unmanaged identity merely because their
carriers coincide with one foreign mapping. No new VTable source attribute,
interface identity, dispatch, output or provider codec is supplied.

Require exactly one NativeAbi clause per such MethodDef. Reject missing or
duplicate clauses, unknown roles, wrong owners, flags, reserved bytes,
ABI IDs and exception modes. The prerequisite's declared masks `0/2/4/6`
and canonical zero omission remain unchanged for roles `1/2/3`, with
effective caller-unsafety derived separately from their native roles.
Role `4` retains required Effects `04`, its explicit-unsafe eligibility and
no-return prohibition. A convention byte authorizes no call or callee-body
unsafe operation.

### Function-pointer references: unchanged bytes, complete identity

Keep section `0x0033` rows exactly
`u32 signatureMethodDefRow, u32 reserved=0`, and TypeSpec rows exactly
`u8 form=0x0D, u8 qualifiers=0, u32 signatureRow`. The first operand names a
MethodDef; the second names a signature-table row. Neither is a DefId or a
Standard `signatureContractMethodDef` operand.

Read the convention from the referenced role-`3` anchor's NativeAbi clause.
Do not add another convention to the TypeSpec or signature row. Normalize
identity by **convention, declared Effects and complete ordered result and
parameter identities**, recursively through nested pointer types. Neither
anchor names nor physical row numbers participate. Five same-shaped
signatures with Native/Terrum `0` and foreign conventions `1..4` are distinct
and valid; duplicate complete signatures retain Development's existing
rejection rule.

Every admitted declaration-to-anchor match includes its convention and
declared Effects, not shape or effective caller-unsafety alone. Restore
actual and bodyless expected role-`2` declarations from their owned clause
before matching. A direct import's convention is independent of the
conventions of any function-pointer result/parameter it transports; those
pointer types still use their own anchors. An import without pointer uses
needs no anchor.

Keep the inherited by-value/receiver-free/nongeneric anchor subset and
structural cycle/depth checks. Nonempty Selectors, OutputStates, Dependencies
or NativeParameters on a retained signature still reject; actual direct
NativeParameter/NativeOutput codecs are not substitute signature carriers.
This decision does not remove the existing projected-address or
direct-import-address representation gates.

### NativeImport: retain resolution, not the prototype search policy

Development section `0x0030` rows remain exactly sixteen bytes:

| Byte offset | Field | Candidate meaning |
|---:|---|---|
| 0 | `u32 methodDefRow` | Nonzero one-based MethodDef row, not a DefId; its unique NativeAbi role is `1`. |
| 4 | `u32 libraryNameStringRow` | Existing nonzero String row for the exact library name. |
| 8 | `u32 exportNameStringRow` | Existing nonzero String row for the normalized exact export name. |
| 12 | `u8 resolutionMode` | `1=Link` or `2=Dynamic`. |
| 13 | `u8 resolutionPolicy` | Conditional domain below. |
| 14 | `u16 reserved` | Zero. |

The last four bytes replace the old u32 WindowsSystem32 policy completely.
Only these three pairs are valid:

| Mode/policy bytes | Normalized meaning |
|---|---|
| `1 / 0` | Link; no loader policy. Zero represents required policy absence, not Default. |
| `2 / 1` | Dynamic, Default. |
| `2 / 2` | Dynamic, CompatibilitySearch. |

Reject every other pair, including Link with either explicit policy and
Dynamic with zero, unknown modes/policies and nonzero reserved bytes.
Always emit both bytes, even for source defaults. Preserve source omission
normalization, exact library/export spelling, the existing Development name
and reference bounds, duplicate-owner prohibition and signature/contract
checks. Unsupported valid source names reject this bounded profile rather
than gaining suffixes, decoration, path rewriting or a broader name codec.
An omitted source entry name is normalized to the declaration name before
writing; the existing required export String row is not made optional.

The old word `1` happens to equal the bytes for `Link/no-policy`. That is
**not** a semantic conversion from System32 search to Link. Exact schema
selection governs the interpretation; no reader infers a schema or source
resolution from that coincidence. Resolution is declaration-owned, not
part of a function-pointer type's convention or identity.

Metadata emit/load/reference and re-emission must preserve all three pairs
and all four foreign conventions, plus zero on any retained role-`3`
pointer type. Native use separately implements or refuses the
selected actual linker/loader and convention mapping. In particular,
successful prototype SystemDirectory imports do not establish Link or
Dynamic/Default semantics; Vectorcall identity transport establishes no
Vectorcall backend. Keep native.md's provider-library lifetime and target
search behavior, without a fallback or per-pointer unload lease.

## Producer, consumer and transformation obligations

1. Carry the explicit candidate through the producer, artifact index,
   Contract/signature/import and Body readers/writers, structural validation,
   reference creation and reconstruction. Named draft operations following
   the existing Effects-draft selection pattern are sufficient; this
   decision selects no new general public API or CLI auto-detection.
   Reject unknown/unsupported selections before indexing or writing,
   including artifacts with no native records.
2. Producers publish checked normalized facts, not default-valued objects.
   Replace only this candidate's convention/resolution persistence guard
   with its closed-domain admission. Keep every other inherited unsupported
   signature, Body, representation and address gate. All profile-bearing
   inputs must agree; a Boolean Development overload must not reset a
   candidate component to the baseline.
3. Consumers validate framing, exact sizes, reserved fields, owned ranges,
   role/row correspondence, reference bounds, convention domains, resolution
   pairs and required capability support before affected use. Reconstruct
   signatures and declarations explicitly; do not leave `Native`/Link
   object defaults in place of decoded fields. Consumers need not
   semantically re-prove producer Bodies or foreign-provider behavior.
4. Retain the original convention in native callable expectations and the
   original mode/policy in direct-import descriptors through source-free
   references, binding and rewriting. Apply existing binding checks to the
   complete retained facts; equal Windows x64 carriers cannot satisfy a
   convention mismatch. Add no direct-import Module proxy, provider search,
   replacement rule or private expectation encoding.
5. Re-emission selects this exact destination and remaps decoded String,
   TypeSpec, signature-table, MethodDef/MethodSig/Param and Contract owner/range
   references together with affected Body maps and source/debug indexes.
   Re-intern complete nested identities and rebuild offsets/counts/extents.
   Preserve normalized convention/resolution and declared/effective Effects;
   matching-octet substitution or header promotion is not re-encoding.

Keep existing Development Module requirements, Feature closure and
nonwrapping row/count/extent limits. Separately supplied artifacts require
their own exact selections; a root schema, ModuleRef, filename, `(1,0)`,
Feature list or native container selects no dependency decoder. Include
schema/catalog, binding and target/use context in cached validation keys.
Old Development/Effects selections receive none of these amendments.
Role-`1/2/3` clauses with a nonzero convention at offset 14 fail old
reserved-byte checks;
role-`3` zero and role-`4` clauses can retain those same bytes. Common-subset
bytes cannot reveal every artifact's originating draft; isolation means
explicit agreement and refusal of unsupported forms, not provenance inference.

Standard NativeImport `0x0021`, NativeCallable `0x000C`, seven-byte TypeSpec `0x0D`,
Standard foreign-invocation profiles, and private structured-module
candidates **97/105** retain their own identities and mappings. This
Development clause is not an encoding of any of them. No alias, reduced
composition, fallback, feature-derived decoder, old-schema retry or
compatibility reader is selected.

## Alternatives

| Alternative | Assessment |
|---|---|
| **Reuse NativeAbi's reserved byte for roles `1/2/3` and replace the import policy word — recommended** | One convention authority per affected owner, including Native/Terrum and foreign anchors; role `4` stays unchanged. No size increase, new reference namespace or duplicate convention field. Resolution fits the existing import row while removing the obsolete search-policy meaning. |
| Add a convention to the signature table and another to NativeImport/NativeCallable records | Can transport facts but duplicates authorities and adds correspondence/size changes without removing the declaration-owned gap. Anchor-owned convention already serves every pointer use. |
| Adopt Standard NativeImport and seven-byte TypeSpecs inside a new Development profile | Viable deliberate redesign, but changes owner/reference spaces and requires completing Standard anchor applicability and native-role handling for Development Bodies. Equal row extents supply no shortcut; unnecessary for these missing facts. |
| Retire Development native exchange and migrate callers to an explicitly selected Standard schema | Coherent separate integration direction, using already-owned Standard fields. Requires the complete selected producer/consumer and Body path, not removal of the Development guard. This proposal does not choose that migration. |
| Keep old bytes and infer Unmanaged/Link, map System32 to a source mode, or infer a profile from records | Reject: loses known identity or resolution, invents an unapproved interpretation and violates exact selection. |

## Required tests and remaining decisions

Use synthetic imports/callables, not a recognized library/sample policy.
These are acceptance requirements, not evidence that tests already pass.

| Area | Required cases |
|---|---|
| Per-role domains | Independently constructed vectors enforce role `1/2` bytes `1..4`, role `3` bytes `0..4` and role `4` reserved byte `0` only, with byte 15 zero throughout. Reject zero on roles `1/2`, `5..255` on roles `1/2/3`, every `1..255` byte on role `4`, and unknown roles. Do not share an unconditional convention decoder across all roles. |
| Four foreign conventions | Data-driven producer/load/reference vectors for Unmanaged/Cdecl/Stdcall/Vectorcall on roles `1/2/3`. Check owned convention restoration, including unused imports and bodyless callable expectations. No native execution is required to prove metadata transport. |
| Native/Terrum preservation | Producer/load/reference and independently constructed role-`3` byte-zero anchors preserve existing internal Native signature identity, declared masks and inherited bounded invocation behavior. Include an anchor-only retained type, indirect value uses and Native pointer results/parameters of foreign imports; re-emission keeps zero explicitly and never changes it to Unmanaged. No new address target or ABI is needed to transport these facts. |
| Role-4 preservation | Otherwise admitted NativeVTable metadata retains the exact old `(1,4,0)`/zero-reserved payload, Effects `04`, receiver/slot checks and absence of NativeCallableConvention through load/reference/re-emission. Nonzero bytes 14/15 reject; no default convention, new attribute or combination with NativeCallable is inferred. |
| Pointer identity | Five same-shaped signatures in one artifact remain distinct, especially Native/Terrum `0` versus Unmanaged `1`; combine conventions with declared masks `0/2/4/6`, nested pointers and function-pointer import results/parameters. Equal complete duplicates reject; declaration/anchor convention or mask mismatches reject even where machine carriers match, including a foreign role-`2` declaration matched to a zero anchor. Distinguish an import's own convention from its transported pointer conventions. |
| Import resolution | All twelve convention/resolution combinations, including zero-parameter imports with no pointer anchors. Omitted/explicit valid source defaults normalize identically; decoding never reapplies source defaults. Assert exact names and all three pairs after source-free reference creation and re-emission. |
| Independent consumer/re-emission | Build bytes independently of the executable producer; load, form a consumer/reference and re-emit under the exact candidate. Reorder/remap Strings, TypeSpecs, signature anchors and owned clauses/maps; preserve complete identity and original expectations. Canonical inputs should re-emit canonically; explicit-zero Effects and valid clause reordering retain their prerequisite meaning. |
| Unsafe and unchanged gates | Role-`1/2/3` mask-zero invocations remain caller-unsafe; explicit declared unsafe remains distinct. No-return flow survives. Retain role-`4` no-return rejection, every nonempty pointer collection rejection, projected/direct-import address gates, known backing/output obligations and no implicit marshalling. |
| Malformed conventions/contracts | Enforce the per-role byte matrix, then reject truncated/oversized clauses, duplicate/missing NativeAbi, wrong owner/role/ABI/exception mode, nonzero flags/reserved bytes and callable/address uses of anchors. Unknown conventions never become Native/Terrum zero or Unmanaged. |
| Malformed resolution | Reject modes `0/3/255`, policies `3/255`, Link with `1/2`, Dynamic with `0`, nonzero reserved bytes, duplicate imports and missing role/row correspondence. No System32-word or absent-field fallback. |
| Bounds | Zero signatures/imports, zero parameters, unit/non-unit results, nested cycle/depth boundaries, truncated tables, zero/high-byte/out-of-range references and nonwrapping count-times-row-size calculations. A valid zero convention never permits a zero signature-row or MethodDef reference. Cover existing name limits without allocating maximum-sized tables merely to test declared extents. |
| Schema isolation | Explicit candidate versus unchanged Development, Effects-only, Standard and private 97/105 selections; nonzero convention bytes on roles `1/2/3` reject old readers, while shared zero-anchor/role-`4` bytes do not establish schema provenance. Reject unknown selectors before parsing/writing even for empty/common-subset inputs. Common version bytes, Feature lists and import-row coincidences never choose a decoder or convert old meanings. |
| Native-support separation | After successful fact transport, unsupported convention or actual resolution mappings refuse affected native generation/use without coercion, fallback or claimed execution evidence. Preserve existing headless supervision and opt-ins if later native tests are added. |

No wire or selection choice remains open within this bounded recommendation;
formal review/adoption is pending. Implementation may proceed provisionally
with explicit candidate selection. Existing default-Development positive
tests must be retargeted deliberately if they are meant to qualify this
candidate, not used to approve a default change or compatibility aliases.
Complete invocation collections, additional address families, Standard
migration, provider/native-package transport and actual target support
remain separate gates.
