# Standard TRRM Terrum function-pointer convention admission

**Status: unapproved; provisional direction only.** Every candidate owner
amendment, schema identity and integration choice below remains unapproved.
This draft changes no behavior, normative specification or released contract.
It supplies the bounded decision requested by the standing instruction:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This is a proposal-only handoff, not authorization to implement in this task.
It supersedes [Standard function-pointer default convention](standard-function-pointer-default-convention.md)
as the candidate owner of the TypeSpec convention domain and its exact-schema
selection. The companion [source contracts](standard-function-pointer-source-contracts.md),
[anchor contracts](standard-function-pointer-anchor-contracts.md) and
[NativeAddress carrier](standard-native-address-formation.md) remain separately
unapproved; none is adopted or automatically composed here.

## Decision and alternatives

**Unapproved recommendation:** admit `0x00 = Terrum` in the convention byte
of the existing seven-byte Standard TypeSpec form `0x0D`, through one explicitly
selected successor of the accepted baseline schema. Keep foreign declaration
convention domains `1..4`. A semantic pointer type can be persisted without
standardizing its machine call ABI or making an ordinary method addressable.

Preserve the accepted [callable-family contract](../specs/type-families.md#callable-values-and-tuples):
unqualified `fnptr` uses Terrum convention; an explicit foreign convention is
identity-bearing through aliases, nesting, parameters and results; no implicit
convention conversion exists. No source enum member, `NativeAbi` alias,
Terrum-to-Unmanaged mapping or convention inferred from machine carriers is
proposed.

| Unapproved alternative | Disposition |
|---|---|
| Extend `0x0D` with TypeSpec-only zero | Recommended: same environment-free pointer family, signature anchor and payload; only the closed convention domain and its admission scope change. |
| Keep Standard foreign-only and explicitly refuse retained Terrum-convention pointers | Coherent alternative: source binding stays valid, but Standard production refuses any retained zero-convention type, including nested types and declaration-only exports/import expectations. No partial artifact or fallback to Development is allowed. This avoids a schema amendment but leaves default source pointer interchange unavailable. |
| Separate Terrum TypeSpec form or callable-signature record | Not selected: it would need a new exact form/reference, anchor, capability and remapping agreement without a demonstrated distinct payload requirement. No candidate ID or parallel codec is assigned. |

Encoding Terrum as `1=Unmanaged` is not an alternative: it violates the
preserved source identity contract.

## Exact candidate owner changes

The following is **unapproved replacement text** for the `0x0D` table row and
its convention-domain paragraph in
[IL metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics):

> Form `0x0D` is a raw function-pointer contract. Its complete row is
> `u8 form=0x0D, u8 qualifiers=0, u8 callingConvention,
> u32 signatureContractMethodDef`, exactly seven bytes, with the `u32`
> little-endian. The TypeSpec-specific closed convention domain is
> `0=Terrum, 1=Unmanaged, 2=Cdecl, 3=Stdcall, 4=Vectorcall`.
> Values `5..255` are unassigned and invalid. Zero is an explicit Terrum
> convention identity, not omission, unspecified ABI, a wildcard or the
> consuming compiler's default.
>
> The nonzero typed MethodDef reference has a zero high byte and names an
> existing bodyless, non-exported signature/contract anchor. It is not a
> MethodSig reference, DefId or address target. The existing anchor,
> complete-signature and applicable-contract rules remain unchanged.
> Complete function-pointer identity includes the original convention;
> equal target carriers do not equate conventions. NativeImport,
> NativeCallable and NativeExport declaration-role domains are not widened
> by this TypeSpec field.

Byte offsets remain `0=form`, `1=qualifiers`, `2=convention`, `3..6=anchor`.
Anchor rows remain `1..0xFF_FFFF`, within the actual MethodDef table. This
changes no TypeSpecRef namespace, qualifier, token, opcode, Body record,
clause, section, index width or anchor role. TypeSpec form `0D` and the
NativeAddress candidate's Body record `0D` are different namespaces.

In the candidate [registry](../specs/representation/trrm-1.registry.json),
replace `metadata.type_forms`'s `0D` label with `function-pointer`, retaining
its payload, and add this distinct `metadata` domain:

```json
"function_pointer_calling_conventions": [
  [0, "Terrum"], [1, "Unmanaged"], [2, "Cdecl"],
  [3, "Stdcall"], [4, "Vectorcall"]
]
```

Leave `metadata.native_calling_conventions`, Contract `000C`'s domain and
callable NativeImport's convention checks at `1..4`. A data import's zero
still means **no call convention**, even when its field contains a
zero-convention function-pointer value. NativeExport still references its
actual method's unique `000C`; no such clause is manufactured for an anchor.
Source NativeCallConv still selects the four explicit foreign values.

Candidate [manifest](../specs/representation/semantic-manifests.md) and
[admission](../specs/representation/requirements-and-admission.md) scope
clarifications reuse, without renumbering, `1/1` for Terrum pointer identity
and declaration binding, `2/1 -> 1/1` for values, and existing `4/1` for
indirect invocation with its unchanged `4 -> 3 -> 2 -> 1` closure.
Feature 13 remains additionally required for retained identity/layout-query
uses, not merely for a signature anchor. The broadened scopes concern pointer
semantics, not new foreign declaration roles. Other required capabilities
and currently missing carriers remain independent gates.

## Exact selection and version consequences

**Unapproved candidate identity:**
`trrm/1-draft-layout-queries-terrum-fnptr-1`.
It means exactly the accepted `trrm/1-draft-layout-queries-1` baseline,
including its governing chapters/catalog, amended only by this document's
owner changes above. **Unapproved internal selection:** retain
`TrRmArtifactSchema.Standard` and select the exact successor through
`standardSchemaIdentity`. No `TerrumFunctionPointerDraft` enum member is
proposed. No on-wire profile field is added. The candidate registry's
top-level `schema` and the candidate
semantic-manifest chapter's current-draft identity both use this exact name;
neither continues to identify the unamended baseline.

The existing [artifact index](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs)
already stores `Schema` and `StandardSchemaIdentity` independently and
checks allowed combinations. Extend that closed identity/operation-support
gate with the single pair `(Standard, exact successor identity)`.
The existing gate does not yet recognize this candidate; merely storing a
new string is not implementation. A new enum profile is unnecessary because
the framing, table layouts and Body schema are unchanged, while it would require
allow-list changes in otherwise unchanged readers.

The identity must reach the check that changes:
[structural metadata validation](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs)
currently passes only `index.Schema`, not `index.StandardSchemaIdentity`,
to TypeSpec construction. **Unapproved transport requirement:** carry the
checked exact identity into internal TypeSpec reader/validation and writer/
row-construction paths, including standalone TypeSpec preflight when it claims
successor support. Retain it through copied/appended TypeSpec tables, dependency
materialization and re-encoding. Do not reset a successor operation
to an identity-less Standard overload. Unchanged readers can continue using
`Standard`; their enum alone must not authorize the zero-domain amendment.

The structural zero gate is exactly **Standard plus the selected successor
identity**, with that operation's successor support established. Independently
require the using Module's `NativeBoundaryContracts 1/1` and the applicable
value/invocation closure under the selected catalog before affected use.
Feature rows do not select the schema or enable zero under the old baseline;
no new Feature ID or caller-supplied permission Boolean is proposed.
Null/no explicit identity retains existing baseline behavior. An unknown
identity, incompatible enum/identity pair or unsupported successor operation
fails before schema-dependent work, including direct TypeSpec entry paths.

Under [draft format versioning](../specs/representation/format-versioning.md),
retain `(1,0)`, governing edition `1` and the stated Feature revisions.
Exchange still requires agreement on this complete successor schema and
semantic catalog before schema-dependent decoding or production. A name,
header pair, filename, Feature list or successful parse does not prove that
agreement. Select and retain the exact profile for each root/dependency input
and destination; cache reuse includes that selection and binding context.

Identity-less `Standard` defaults and other named drafts keep their own
selected schemas. In particular, the
[foreign-invocation profiles](trrm-draft-schema-profile-selection.md)
explicitly exclude default-convention amendments: they do not acquire zero
by being non-Development, sharing `0x0D`, or referencing this proposal.
This draft assigns no arbitrary `profile + zero` composition. An operation
needing both amendments must wait for an explicitly defined complete
successor composition; it cannot silently relabel an existing profile.
Moving the default Standard selection to this successor is a later adoption
step, not part of provisional opt-in.

The old baseline remains foreign-only when explicitly selected; zero is
invalid there. Common-subset bytes may be valid under either separately
selected schema, but do not select one. Unpublished schemas may be retired;
no old-reader obligation, compatibility alias, sniffing, migration shim or
automatic roll-forward is introduced.

## Producer, consumer and execution consequences

* **Produce:** carry the bound convention unchanged through shared bound-tree
  traversal/rewriting and verification; emit a bodyless anchor from the
  complete selected static type, not a foreign NativeCallable surrogate.
  Construct and validate zero rows only with the checked
  `(Standard, successor identity)` selection. Default identity-less Standard
  factories/encoders continue to enforce `1..4`; writer-side validation must
  receive the same identity, not only the enum. Missing signature, contract
  or capability support refuses affected production before output publication;
  an unreachable call does not excuse retained type erasure.
* **Consume and bind:** safely check selected framing, exact extent,
  qualifiers, domain, typed anchor bounds/roles, complete references and
  required support before affected use. A successful lookup can return
  convention zero: its success result, not that byte, distinguishes absence.
  Resolve anchors in their defining Module context and compare the exact
  convention plus complete signature/contracts against every retained
  original expectation and actual provider. Zero is not assignable to
  `1..4`, including through contract projection or compatible replacement.
  [Producer truth and mandatory consumer checks](../specs/representation/reader-trust.md)
  remain distinct; no consumer body re-proof is required.
* **Transform:** decode under explicit source/destination selections and
  rebuild TypeSpecs/TypeSpecRefs, MethodDef anchors, MethodSig/Param/Contract
  ranges and references, Imports/Exports, nested substitutions, Body maps,
  affected offsets and retained source/debug coordinates. Preserve the
  convention, defining contexts, code origins and original binding
  expectations. Reordered equivalent zero anchors may deduplicate; zero and
  foreign anchors may not. Header patching or octet substitution is not
  conversion.
* **Lower:** zero denotes the Terrum logical invocation contract, not an
  instruction to reuse an unmanaged ABI. Require explicit compatible
  joint-lowering/compiler/runtime/target assumptions for any machine call;
  independent precompiled crossings retain their separate
  [ABI gate](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults).
  Preserve unsafe authorization, declared effects, output correlations,
  references/backing, ownership/cleanup and code lifetime. Do not inject or
  remove Effects `04` solely because of the convention byte. No marshalling,
  closure, retain, thunk or lifetime extension follows.

**NativeAddress is not broadened.** Its companion candidate requires an
actual admitted NativeCallable or callable NativeImport entry with exact
foreign convention `1..4`, distinct from the anchor. A foreign address cannot
be projected to zero. Existing source support for taking an ordinary Terrum
function address does not approve its Standard carrier/entry eligibility or
a stable Terrum callback ABI. This successor adds no `E9` operation and does
not complete that separate decision.

Unknown/mismatched schema selections fail before schema-dependent work.
Malformed rows fail safe consumption; valid but unsupported retained
semantics or incompatible bindings fail before affected binding/lowering/
execution. Foreign-role zero remains invalid. No failure returns a partial
artifact, guessed convention, null/default address, weakened contract or
successful fallback.

## Proposed test matrix

These are **unapproved acceptance requirements, not executed results**.

| Area | Required cases and outcome |
|---|---|
| Source | Bare scalar `fnptr` retains Native/Terrum `0`; all four registered explicit conventions retain `1..4`. Reject implicit cross-convention initialization, argument/result conversion and projection, including nested aliases. |
| Selected codec | Data-driven `0..4` round trips with `(Standard, successor identity)` preserve seven bytes, typed anchor and exact identity. Identity-less/old-baseline Standard rejects zero even with Feature `1/1`; foreign-only cases remain valid. No Development signature section is used. |
| Domain/bounds | Reject `5`, `255`, nonzero qualifiers, six/eight-byte rows, zero/high-byte-set/wrong-namespace/out-of-table anchors. Accept the largest representable anchor only with valid actual table/owner bounds. |
| Roles | Zero in callable NativeImport or `000C` rejects; data-import no-convention zero stays valid. A zero TypeSpec does not make its anchor callable or supply NativeCallable/NativeExport. |
| Selection/transport | Unknown identity, wrong enum/identity pair, mismatched root/dependency context and unassigned composite fail without publication. Exercise index-to-TypeSpec, writer validation and copied/appended table identity retention. Common-subset bytes do not trigger auto-detection; missing required Feature/carrier support still refuses use. |
| Binding/remapping | Different anchor rows with equal complete zero contracts compare equal; zero versus Unmanaged rejects even with equal carriers. Reorder/deduplicate nested rows and packed Modules while retaining original expectations, selectors, maps and code origins. |
| Address/invocation | Existing foreign NativeAddress positive cases stay foreign; foreign target plus zero view rejects. Bare source ordinary-address support is separate from Standard admission. Removing unsafe authorization or losing required output/backing facts rejects. |
| Lowering | After actual support exists, separately qualify source-to-successor, source-free `call.value`, joint-lowered headless execution and target refusal. Serialization/emission alone is not runtime or independent-ABI evidence. |

## Source evidence and remaining decisions

Evidence was read at `c994372800082b1931b8b380c30811920034d903` plus the
pre-existing worktree edits. No builds or tests were run or changed for this
draft.

| Evidence | Observed boundary |
|---|---|
| [Grammar §3.2](../specs/grammar-spec.md#32-types-parameters-and-arguments), [callable families](../specs/type-families.md#callable-values-and-tuples), [native roles](../specs/native.md#native-declaration-roles) | Accepted bare result-last pointer syntax, default Terrum identity, explicit foreign identities and foreign role domains. |
| [Enum/signature model](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs), [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) `BindType` / `BindNativeFunctionPointerType` | `Native=0`; current bare binding passes it directly. The earlier default-convention proposal's NativeAbi-prefix observation no longer describes this source path. |
| [NativeCoreRegistrationTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs) `BareFunctionPointerUsesTheTerrumDefaultConvention`; [NativeFunctionPointerTests](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeFunctionPointerTests.cs) `NativeAddressInfersOrdinaryTerrumFunctionContract` | Active source tests assert zero identity and ordinary address behavior; they do not establish Standard persistence or a general ABI. |
| [TypeSpec reader](../../sources/Terrum.CodeAnalysis/Modules/TrRm/TrRmTypeSpecTableReader.cs), [writer](../../sources/Terrum.CodeAnalysis/Modules/TrRm/TrRmTypeSpecTableWriter.cs), [row tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmTypeSpecTableReaderTests.cs), [register fixtures](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmRegisterBodyReaderTests.cs) | The tracked baseline accepts and positively tests zero across Standard-schema paths. Uncommitted guard/test edits currently enforce `1..4` but do not yet provide the proposed exact successor selection, so they are not an integrated implementation. |
| [NativeAddress producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddress.cs), [record tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmRegisterBodyNativeAddressTests.cs), [standalone TypeSpec checks](../../sources/Terrum.CodeAnalysis.Terrum/Modules/StandaloneTypeSpecValidation.terrum) | Actual formation/profile checks remain distinct from TypeSpec identity. The uncommitted Standard producer guard refuses the Terrum convention, while standalone WP1 preflight is not yet gated by the proposed successor identity. Zero reserved fields in a 12-byte NativeAddress record are not the seven-byte TypeSpec's convention. |
| [Artifact index](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs) constructor / `StandardSchemaIdentity`; [structural validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) TypeSpec construction | The index already retains and checks a separate exact identity; the TypeSpec handoff currently omits it. Narrow identity registration and explicit transport, not a new enum profile, supply the missing internal gate. |
| [Test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj), [test props](../../tests/Directory.Build.props), [test targets](../../tests/Directory.Build.targets) | The cited fixtures are normal NUnit compile inputs, not excluded prototypes. Test source is evidence of expectations, not a passing run. |

Remaining **unapproved** decisions are whether to adopt this zero-domain/
successor recommendation rather than the explicit Standard refusal alternative;
which, if any, existing composite schemas need separately named successors;
and whether ordinary Terrum functions need a Standard address-formation entry
contract and independently interoperable call profile. The latter two do not
block the bounded type-identity decision and are not silently resolved by it.
