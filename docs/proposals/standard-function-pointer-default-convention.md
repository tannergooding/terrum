# Standard function-pointer default convention

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This document allocates nothing and changes no
normative owner, source contract, registry, implementation or stability promise.
The authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Decision and evidence

Recommend extending the existing seven-byte Standard TypeSpec `0x0D` with
**candidate convention byte `0x00 = Terrum`**, scoped to that TypeSpec field.
It denotes the already-accepted default source callable convention, not
Unmanaged, an unspecified convention or a newly frozen machine ABI.

This is separate from the unapproved
[source-contract proposal](standard-function-pointer-source-contracts.md),
which owns invocation-contract defaults and checked projection, and the
[anchor-contract proposal](standard-function-pointer-anchor-contracts.md),
which owns clause applicability, normalization and rebasing. If selected,
this direction supersedes only their foreign-only `0x0D` boundary and
zero-convention rejection for this TypeSpec. Their other recommendations and
prerequisites remain unapproved; this document does not approve them.
It also intentionally amends only the Standard TypeSpec `0x0D` domain
discussed by the unapproved [private persistence candidate](native-calling-convention-private-persistence.md);
that candidate's revision-97 private token domains and payloads remain
unchanged.

The repository establishes the gap, not its numeric answer:

* [Callable families](../specs/type-families.md#callable-values-and-tuples)
  accept a default Terrum convention, four explicit foreign conventions and
  identity preservation without implicit convention casts.
  [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
  instead restricts `0x0D` to nonzero `NativeCallingConvention` values `1..4`.
* The internal [enum and signature checks](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs)
  recognize `Native = 0`, but [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)
  routes unattributed pointers through `BindNativeFunctionPointerType` with
  the legacy `NativeAbi` prefix requirement. Legacy NativeCallable/import
  paths also use zero; registered NativeCallable/import omission selects
  Unmanaged. Thus internal zero is not evidence that accepted default Terrum
  pointers are implemented.
* [NativeEmitter](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs)
  admits internal zero alongside Unmanaged/Cdecl/Stdcall and uses common
  Windows x64 scalar argument/result staging for `BoundIndirectCall`.
  This does not establish a general Terrum callable ABI.
  The [Standard reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs)
  rejects convention zero; [TrRmExecutableCompiler](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
  still writes Development signature references and selects
  `EncodeDevelopment`, not the proposed Standard representation.

| Unapproved alternative | Assessment |
|---|---|
| **Extend `0x0D` with TypeSpec-only Terrum tag zero — recommended** | Same environment-free code-pointer family and signature/contract anchor; one closed-domain amendment, no new form ID or parallel codec. Chosen for semantic fit, not prototype compatibility. |
| Separate Terrum TypeSpec form/ID or dedicated callable-signature representation | Can separate convention domains structurally, but needs a complete form/reference, anchor, contract, feature and remapping agreement. No distinct payload requirement currently justifies it; no candidate ID is allocated here. |
| Explicit Standard exclusion of default Terrum pointers | Accurately describes today's schema, but leaves accepted source without interchange and cannot satisfy the authorized end-to-end implementation goal. Not the proposed completion policy. |
| Encode Terrum as `1=Unmanaged` | Loses accepted convention identity even where machine carriers match. Rejected, not a compatibility alias. |

## Candidate schema and owners

Retain the entire row:
`u8 form=0x0D, u8 qualifiers=0, u8 callingConvention,
u32 signatureContractMethodDef`.
The candidate **TypeSpec-only** domain is:

| Byte | Meaning |
|---:|---|
| `0x00` | Terrum: the default logical callable convention under the defining Module's governing rules and admitted target/runtime contracts |
| `0x01` | Unmanaged |
| `0x02` | Cdecl |
| `0x03` | Stdcall |
| `0x04` | Vectorcall |
| `0x05..0xFF` | Unassigned; invalid in this selected candidate schema |

Zero is explicit identity, never omission, wildcard, failed decoding or
permission to substitute a consumer's default. All five identities remain
distinct through aliases, nested types, substitution, binding and lowering.
The typed, nonzero MethodDef reference remains a contract anchor, never the
address target. No reference namespace, qualifier, extent or anchor-role
change follows.

Do **not** extend the source `NativeCallingConvention` enum or its
NativeCallConv use. [NativeImport](../specs/native.md#native-declaration-roles)
and [NativeCallable `000C`](../specs/representation/contract-clauses.md#2-clause-matrix-and-uniqueness)
retain only `1..4`; omitted native CallConv remains Unmanaged. Callable-import
zero remains invalid. A data import's zero still means **no call convention**,
even when its field type contains a function pointer. NativeExport still
references the actual method's unique `000C`. None of these roles is required
on, or manufactured for, a Terrum signature anchor. No `Native`/`NativeAbi`
source alias or new native-address eligibility is proposed.

| Owner | Intended amendment if adopted |
|---|---|
| [IL metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Rename the `0x0D` description to raw function pointer; replace its nonzero/four-value restriction with the scoped table above. Preserve complete signature/contracts and bodyless, noncallable anchors. |
| [Registry](../specs/representation/trrm-1.registry.json), [registry authority](../specs/representation/registry.md) | Describe the broadened TypeSpec and add its separate convention domain; do not add zero to `native_calling_conventions`, NativeImport or `000C` domains. Amend the affected Feature scopes below explicitly. |
| [Semantic manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Include default-convention pointer identity/value/invocation in the selected draft's corresponding capability scopes, preserving complete closure and before-use support. |
| [Callable families](../specs/type-families.md#callable-values-and-tuples), [ABI layers](../specs/data-layout-and-abi.md#contract-layers), [calls](../specs/il-instruction-set.md#calls) | Preserve source convention identity and logical invocation phases; clarify the interchange mapping without inventing syntax, foreign admission or a universal Terrum machine ABI. |
| [Native conformance](../specs/conformance/native-boundaries.md), companion proposals | Add default/foreign distinction cases and replace only TypeSpec-zero rejection in the candidate anchor cases; native-role zero rejection remains. |

Under [format versioning](../specs/representation/format-versioning.md),
this unreleased amendment may retain `(1, 0)` and existing Feature revisions,
but requires exact out-of-band agreement on the amended schema **and**
semantic catalog. Equal version bytes or Feature lists do not select it.
Current-schema `0x0D` convention zero remains invalid until that different
draft is selected.
No historical reader, dual interpretation, migration shim or released
compatibility promise is required or authorized.

## Capability and integration obligations

Propose reusing these exact draft capabilities, explicitly broadening their
pointer scope rather than allocating a Feature:

| Retained use | Candidate gate |
|---|---|
| TypeSpec/anchor identity and declaration binding | `1/1 NativeBoundaryContracts` |
| Raw pointer values, fields/places and Body TypeMap value uses | `2/1 RootlessNativeValues`, with prerequisite `1/1` |
| Admitted address formation and indirect invocation, with their pointer-transport/code-origin obligations | `4/1 NativeExportEntries`; retain catalog closure `4/1 -> 3/1 -> 2/1 -> 1/1` |
| Retained registered identity/layout query operations | Additionally `13/1 LayoutQueries`; a signature-only TypeSpec does not itself require it |

Feature names do not give a default pointer a foreign declaration role.
The code pointer itself is environment-free; its signature is **not**
restricted to the implementation's scalar/raw-pointer whitelist merely
because it shares `0x0D`. Apply foreign ABI admissibility to foreign
conventions, not as the definition of Terrum calls. Retained reference,
initialization, owner/root/cleanup, generic, predicate, failure and runtime
semantics need their own complete carriers and capability closure, including
the companion proposals' outstanding gates. No absent Feature or unreachable
call licenses dropping those obligations.

* **Produce and bind:** recognize accepted default source types independently
  of the legacy enum path; carry the exact convention and complete selected
  invocation contract through binding, shared traversal/rewriting,
  verification, persistence and dependency materialization. Emit Standard
  anchors from that static type, not a synthetic foreign NativeCallable.
  Compare convention exactly in source-free provider/replacement checks.
* **Consume and validate:** safely decode exactly seven bytes, zero
  qualifiers, the closed tag and typed 24-bit anchor reference with its
  reserved-zero high byte, actual bounds, owners, ranges, signature and
  contract validity. Retain the anchor proposal's role/cycle checks.
  Separate presence/failure from the now-valid TypeSpec zero. Admit the
  selected Module/dependency closure before use; producer-published body
  truth remains a producer obligation, not mandatory consumer re-proof
  ([reader trust](../specs/representation/reader-trust.md)).
* **Remap:** preserve the convention while rebuilding anchors, complete
  TypeSpecRefs, MethodSig/Param and Contract ownership/ranges, selector
  references, generic contexts and affected Body maps/offsets. Preserve
  retained expectations, actual address targets and optional source/debug
  references under their owners. No byte substitution or equal-carrier
  deduplication can turn zero into Unmanaged.
* **Lower and execute:** use the admitted Terrum call contract for zero,
  including argument/result staging, effects, output correlations, reference
  provenance, ownership and code lifetime. Keep raw-pointer caller-unsafety
  and declared effects distinct; invent neither a safe `func` nor Effects
  `04` solely from the tag. No environment, marshalling, pin, retain, thunk or
  lifetime extension follows from serialization.

## Proposed acceptance cases and remaining boundaries

These are requirements to implement and test, not executed evidence:

| Case | Required outcome |
|---|---|
| Positive/schema | Accepted default source type produces and reloads a seven-byte `0x0D` with tag zero and a valid anchor; foreign tags `1..4` retain their exact meanings. No Development signature section or fallback decoder is involved. |
| Identity/negative | Otherwise equal Terrum and Unmanaged pointers differ, including nested aliases, fields, parameters and results. Reject implicit conversion or provider replacement across conventions even with equal Windows x64 carriers; a foreign NativeAddress cannot acquire a default type by retagging. |
| Domain/bounds | Reject tags `5..255`, nonzero qualifiers, six-byte/trailing-byte rows and zero, wrong-kind, high-byte-set or out-of-range anchors. Validate the highest representable in-table anchor. Reject zero in callable NativeImport/`000C` and source NativeCallConv; preserve data-import zero. |
| Cross-feature | Preserve mixed nested conventions, closed generic substitution, checked references/init outputs, ownership/cleanup, no-return and dependencies through anchor comparison and rewriting. Queries distinguish identities even when layout observations agree. Missing required carriers/support fail admission, never erase facts. |
| Packing/binding | Reorder/deduplicate structural rows across Modules and rebuild ranges, selectors and Body/source maps; preserve defining contexts, every expected binding and code origin. Same signature with a different convention is not deduplicated. |
| Runtime/CLI | Exercise source-to-Standard, source-free load and logical `call.value`, headless native execution, standalone CLI and separately supplied dependent-runtime consumers. Include target failure/cleanup and mixed foreign/default cases; emitting or inspecting bytes alone is not execution evidence. |

Two other boundaries must not be answered by assigning zero. First,
[Windows x64 nonforeign defaults](../specs/targets/windows-x64-call-interface.md#2-carrier-classification-and-nonforeign-defaults)
expressly do not standardize a Terrum-to-Terrum ABI.
[Independent precompiled crossings](../specs/data-layout-and-abi.md#private-implementation-versus-independently-replaceable-boundaries)
still need explicit compatible compiler/runtime/target assumptions or a
separately selected adaptation contract; this proposal chooses neither a
universal register convention nor a stable callback export ABI.

Second, the unapproved
[NativeAddress carrier proposal](standard-native-address-formation.md)
selects a candidate `E9`/Body-record `0D` operation for addresses of already
admitted native declarations. It deliberately does not make an ordinary
Terrum method native-addressable, project a foreign target to tag `0`, or
select a target ABI adapter. Any requirement to form a pointer to an ordinary
Terrum method remains a separate source/entry-eligibility decision; tag zero
does not authorize that operation. Complete the selected address-carrier,
source and companion contract gates as part of the end-to-end implementation.
A blanket diagnostic or permanent Standard exclusion is not completion.
