# Native calling-convention private persistence

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This document selects private/native-container transport only. It changes no
source schema, normative specification, standard assignment, output protocol
or implementation, and supplies no build, test, ABI or execution evidence.

## Decision and owners

Select **candidate private structured-module revision 97**: retain candidate
96's complete foreign identity/layout/call-target facts, replace its
foreign-member convention byte with the common declaration token below, and
add the direct-import token plus the expanded private fnptr convention
domain. Store every NativeCallable method's effective convention in its
existing declaration `Contract`, including instance methods that have no
addressable fnptr type. Keep a present `NativeFunctionPointerType` as a
separate type-use fact and require it to agree with the NativeCallable token.
Restore direct-import convention from its declaration token; add no
redundant stored-function or outer-table convention field.

Select **outer version 3, foreign/convention-aware draft / inner 97** for
TNPK, TNSP and TNGP. Keep the revision-96 foreign-aware outer field layout,
including the direct-import expectation table, **unchanged**. The expected
direct-import convention now survives in the importing inner declaration;
native binding must compare that original fact as well as the outer row.
No new outer convention byte, outer version 4, optional trailer or alternate
outer-3 interpretation is selected.

| Owner | Boundary preserved |
|---|---|
| [Exact convention source schemas](native-calling-convention-source-schema.md) | Owns exact Core/schema/member identity, named-only CallConv, source arguments, omission, applicability, interface defaults/method overrides, export implication, witnesses and effective convention semantics. This document transports its normalized result, not source annotations. |
| [Foreign-interface identity](native-output-foreign-interface-method-identity.md) | Owns nominal declaring-interface/member selection, flat foreign role, target layout, receiver dispatch and standard foreign-interface candidates. |
| [Foreign-interface private persistence](native-output-foreign-interface-private-persistence.md) | Owns candidate 96's nominal directories, bodyless signature variant, typed tag-5 targets and foreign-aware outer-3 admission. Candidate 97 retains those identity facts but replaces the variant's convention byte with the common NativeCallable token here. |
| [Native-output cells](native-pointer-output-ownership.md), [source dependencies](aggregate-native-output-source-dependencies.md) | Own eligible cells, result correlation, vacancy/reservation/publication, input/receiver sources, backing, failure settlement and resource adoption. No output/dependency fields or meanings change here. |
| [Direct-import provider binding](native-output-direct-import-provider-binding.md) | Owns selected provider/export routing, real leaf exports, retained outer foreign descriptors and replacement/image checks. Keep those requirements; add convention comparison from the inner expectation, not from a replacement. |
| [Persistence](../specs/representation/persistence.md), [format versioning](../specs/representation/format-versioning.md), [binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Own private/standard separation, exact draft agreement, original retained obligations and structural safe consumption versus producer truth. |

Standard NativeImport, Contract `0x000C`, NativeExport references and TypeSpec
`0x0D` remain under their existing owners. Their convention bytes **1..4**
are not changed or reused as the private convention encoding. No Section,
DefId, Feature, opcode, Body record, target ID or standard format pair is
allocated.

## Inspected implementation boundary

These are source observations, not qualification or support claims. They
describe the inspected worktree, not the older baseline paragraphs of the
composed proposals.

| Source | Observation and consequence |
|---|---|
| [`NativeFunctionPointerContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs) | The host enum contains only `Native`, implicitly zero. Both signature/table validation require that exact case. The current reader's enum cast does **not** make bytes 1..255 valid. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader`, `ReadNativeFunctionPointerSignatures` | The writer's final selection and header reader's sole admitted revision are **92**. Signature rows write a u8 convention; reading casts the byte, then validates the complete table. Internal `>=82` predicates coexist with exact header admission and other explicit revision allowlists. Raising a constant alone does not integrate a new schema. |
| Same file, `PutFunction`, `GetFunction`, `Contract`, `AppendTypeName` | Stored functions retain an optional `NativeFunctionPointerType`; the declaration fingerprint already includes `NativeCallable:` and a complete `fnptr:Native(...)` type identity. That is sufficient convention transport for the independently admitted callable slice, without a second per-function convention. Fixed wire spellings must replace dependence on host enum `ToString()`. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `DirectNativeImport`; `PutFunction`/`GetFunction` | DirectNativeImport has library/export/output/native-parameter facts but no convention. The revision-92 payload has library/export strings followed by the native-parameter list; it cannot preserve a selected direct-import CallConv. Its declaration fingerprint also lacks that fact. |
| `ReadFunctions`, reference/consumer creation | Imported ModuleFunctions may have no Function payload. Merely adding a byte inside PutFunction would still lose direct-import convention expectations in source-free consumers. A retained declaration carrier is required even for an unused import with no outputs. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), `DirectImportExpectation`, serialization and provider validation | Outer version 3 records provider/key, foreign library/export and native parameters, but no convention. Current provider comparison checks those descriptors, not convention. With the new inner expected token, the missing comparison can be completed without duplicating a byte in this table. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), snapshots | Reference projection, merging, graph reads and restoration are separate paths. `RecordedImportPromises` currently restores selected effect flags from a fingerprint; it is not complete restoration of new expected native facts. |
| [`NativeModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs), [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | Set/graph framing also selects 3. Their embedded contracts, parsed snapshots, dependency classification and root-only JIT must all retain/check the new expectation; a valid checksum or precompiled provider does not supply it. |

Candidates 93–96 remain prerequisites, not facts established by the current
92 header. In particular, general declaration dependencies, wide-scalar
payloads, copy-origin summaries and the nominal foreign directory cannot be
omitted to obtain an otherwise convenient “97.”

## Exact composition and selection

Composition is by these exact deltas, not by numeric ordering:

| Candidate | Complete meaning retained in 97 |
|---|---|
| 92 | [NoThrow retirement](no-throw-private-persistence.md) on 89 plus the complete [candidate-90 output delta](aggregate-native-output-private-persistence.md), including removal of legacy output vectors/suffixes and reservation tag 41. Apply the [scalar/pointer-cell eligibility amendment](native-pointer-output-ownership.md#exact-candidate-representation-delta). Retain resolved arithmetic operation modes from 89. |
| 93 | Exactly that amended 92 plus the mandatory [general declaration-dependency collection](reference-provenance-carriers.md#private-exchange-candidate-revision-93), disjoint from output-specific phase-1 rows. |
| 94 | Exactly 93 plus the complete catalog and type-derived scalar payloads in [wide scalar persistence](scalar-wide-value-carriers.md#exact-private-schema-and-composition). |
| 95 | Exactly 94 plus the mandatory [declaration copy-origin summary](reference-field-value-provenance.md#necessary-private-transport-candidate-95). Foreign summaries remain unavailable. |
| 96 | Exactly 95 plus the complete nominal type/interface directory, foreign ModuleFunction variant and every-tag-5 target discriminator in [foreign private persistence](native-output-foreign-interface-private-persistence.md#exact-revision-composition). |
| **97** | Exactly candidate 96's identity and call-target composition, but remove its foreign-member convention byte and require `|NativeCallable:K` in the existing Contract string for every NativeCallable declaration. Also add the private fnptr domain and `|NativeImportCallConv:K` declaration token/restoration rule below. No other record placement, counts, tags, scalar widths or collection meanings change. |

97 is an unapproved private little-endian **i32** header value following
`TRRM`, not the standard u16 major/minor pair or a native-profile version.
No competing private 97 was found in the inspected proposals. At integration,
every structured writer and reader selects exactly this complete 97,
including artifacts with no native declarations or empty signature/directory
tables. Ordinary reads, graph-library reads, producer validation, nested
Contracts, reference sets and snapshots must agree.

Reject 92–96, other old revisions and unknown revisions before decoding their
records. The selected native-container reader admits only the exact outer-3
draft described here with inner 97 throughout its retained closure. Outer 2,
unknown outer versions and old outer-3/inner-92 or inner-96 artifacts reject.
No `>=97` dispatch, reduced composition, historical decoder, old-byte alias,
header promotion, retry or schema inference from payloads/empty collections
is selected.

Equal revision/version bytes still require exact out-of-band agreement on
the whole draft. Regenerate artifacts from complete checked inputs; changing
a header is not conversion. A consumer cannot promise to detect the history
of every dishonestly relabelled old stream that happens to satisfy all new
structural rules.

## Normalized identity and numeric projections

Use explicit encode/decode mappings, not casts of host enum ordinals:

| Effective identity / fixed private mnemonic K | Core enum integer and standard byte | Private function-pointer signature byte | Private declaration token |
|---|---:|---:|---|
| Unmanaged | 1 | 0 | `NativeCallable:Unmanaged` or `NativeImportCallConv:Unmanaged` |
| Cdecl | 2 | 1 | `NativeCallable:Cdecl` or `NativeImportCallConv:Cdecl` |
| Stdcall | 3 | 2 | `NativeCallable:Stdcall` or `NativeImportCallConv:Stdcall` |
| Vectorcall | 4 | 3 | `NativeCallable:Vectorcall` or `NativeImportCallConv:Vectorcall` |

The private signature domain is exactly **0..3**; 4..255 reject.
Candidate 96's former foreign-member byte is absent in 97; no private
foreign-member byte domain remains. Standard byte 3 means Stdcall, whereas
private signature byte 3 means Vectorcall. Standard zero is invalid on a
callable, while private signature byte zero is a **present, normalized
Unmanaged identity**, never absence, inheritance, “native,” unspecified or a
request to choose a target default. Direct-import and NativeCallable
declarations use the exact mnemonic K, not an additional byte.

The fixed, case-sensitive ASCII mnemonics K are exactly `Unmanaged`,
`Cdecl`, `Stdcall`, `Vectorcall`. `Native`, decimal spellings, source enum
member aliases, qualified Core names and unknown spellings are not aliases.
Source enum values and selected Core/member identities are checked under
the source-schema owner before normalization. Decoding a normalized private
fact needs no source attribute, Core lookup or enum-member provenance.
It does not admit a source integer zero or a same-shaped unregistered enum.

Known identities are distinct even if Windows x64 maps Unmanaged, Cdecl and
Stdcall to the same carriers. Structural semantic transport of a known
identity is separate from target execution support. Native emission, address
formation, calls, entries or dispatch requiring an unsupported mapping must
decline affected use, never coerce it to Unmanaged. Vectorcall's registration
does not establish a backend implementation.

## Exact private record delta

Integers retain little-endian encoding. Inner `String` remains i32 UTF-8 byte
length plus strict UTF-8; Boolean remains exactly u8 0/1. This is not the
outer u16-length String codec. All prerequisite bounds and ordering rules
remain in force.

### Function-pointer signatures and ordinary NativeCallable

Keep the table in its existing position, after imported structures and before
NativePointerTypes and the candidate-96 nominal directories:

```text
i32 nativeFunctionPointerSignatureCount
repeat nativeFunctionPointerSignatureCount:
    u8 callingConvention           // private 0..3 above
    i32 resultType
    i32 explicitParameterCount
    i32 parameterType[explicitParameterCount]
```

The table prefix is four bytes; each row is exactly `9 + 4*n` bytes.
No row version, flags, padding or new convention field follows. Type codes
are the complete selected private type universe, not standard TypeSpecRefs.
Validate the entire referenced signature/pointer/type closure, bounded
nesting and existing unsupported-cycle rules; a recognized convention does
not make an invalid type/signature valid.

Keep the stored function's existing `Boolean hasNativeFunctionPointerType`
and, when present, **i32** NativeFunctionPointerType at its existing position
after declaring-owner metadata and before NativeCallableEntryName. Its
present extent is five bytes including the Boolean; its absent extent is
one. It remains a separate addressable type fact, not the NativeCallable
method-role carrier. Add no fixed-width Function convention field.

For private textual type identities, replace the old convention spelling in
`fnptr:Native(...)` with `fnptr:K(...)`, preserving the existing parameter and
result type grammar. K is the fixed mapping above, not host `ToString()`. Replace the old
`|NativeCallable:<complete fnptr identity>` marker with exactly one
`|NativeCallable:K` token for each effective NativeCallable role on a
definition or bodyless expectation. The existing base Contract signature
retains the declaring owner, receiver type/access, result, explicit formals
and modes; the new token carries the effective convention. Its placement is
after the signature/role tokens and before `|NativeCallableEntry:...`,
`|DoesNotReturn`/`|MayReturn` and `|CallerUnsafe`. This applies to static
and instance methods, including methods without an addressable fnptr type.
A foreign ModuleFunction Contract consists of this one token; its typed
foreign variant retains the rest of its complete signature. Absent role is
not implicit Unmanaged.

The existing `NativeFunctionPointerType` remains an independent addressable
type reference in the Function payload. When present for a NativeCallable
method, validate that its complete signature and private convention agree
with the declaration Contract. The function-pointer signature table still
owns the fnptr type-use convention; it does not grant NativeCallable role by
itself. The declaration Contract retains a method's receiver separately from
its explicit formals, so this role token does not reinterpret a member as a
static explicit-receiver fnptr or require a method code address.
NativeCallableEntryName retains its existing symbol validation and requires
its independently admitted pointer type.

The foreign-interface variant uses the same NativeCallable token as its sole
effective convention fact. Candidate 97 therefore removes candidate 96's
`nativeConvention` byte and uses the existing Contract string instead;
candidate 96's directory, typed target and remaining member-signature fields
are retained. Ordinary InterfaceDef/witness identity and a bodyless
requirement's selected member remain subject to their own complete private
carriers. Where those identity/signature facts are not representable, reject
transport rather than lose them; this does not narrow the source contract or
select a fallback.

Convention transport does not lift the provider-binding candidate's
module-selected function-address, indirect-call or entry-alias exclusions.
A plain fnptr cannot erase required native-output, native-input or dependency
facts; refuse an address/call route that cannot retain its complete contract.

### DirectNativeImport declaration and stored-function payload

Keep PutFunction/GetFunction's existing direct-import binary branch
**unchanged**:

```text
Boolean isExternal                 // existing Function fields
Boolean hasDirectNativeImport
if hasDirectNativeImport:
    String libraryName
    String exportName
    i32 nativeParameterCount       // always present in exact 97
    repeat nativeParameterCount:
        i32 formalOrdinal
        i32 pointeeType
        u8 access
        u8 retention
        i32 extent
// existing ParameterCount/variable-count/span/variable/body fields follow
```

The descriptor, excluding the two existing Booleans, is
`12 + libraryByteLength + exportByteLength + 14*n` bytes. Its binary delta
is **zero**. There is no optional parameter list in this exact schema and no
old output tuple after it. Output facts remain exclusively in the declaration
collection; any legacy in-memory view is reconstructed from that owner.

Require an eligible external, bodyless, caller-unsafe callable and the
existing native library/export, signature, native-input and output checks.
Do not add field/data imports, a convention inferred from a parameter's
fnptr type, or a redundant NativeCallable role. Carry a normalized
CallingConvention in the in-memory direct descriptor; a missing fact is not
converted to Unmanaged by the writer. The writer emits that fact once in
the containing declaration token below. ReadFunctions parses the typed
expected fact before GetFunction, which receives it alongside the
declaration-owned output facts and attaches it to the restored direct
descriptor. GetFunction has no independently defaulted convention.
Source omission must already have been resolved to the actual Unmanaged
identity.

### Direct-import declaration expectations

A byte in Function alone is insufficient: imported ModuleFunctions can have
`hasFunction=false`. Extend their **existing String Contract**, not every
ModuleFunction's binary fields, with exactly one token:

```text
|NativeImportCallConv:K
```

It occurs at the end of the signature/role portion, after any existing
NativeVTableParameter tokens and **immediately before** the final
`|DoesNotReturn` or `|MayReturn`, followed by the existing optional
`|CallerUnsafe`. A direct import cannot actually have those vtable tokens
or a NativeCallable/NativeCallableEntry token; the placement specifies one
grammar position, not permission to combine roles. The token contributes
`22 + byteLength(K)` ASCII bytes inside the existing i32-length Contract
String; no new length prefix or standalone byte is inserted on ModuleFunction.

The token is mandatory exactly for an owned direct-import declaration or a
retained module-selected direct-import expectation, whether used or uncalled,
with or without outputs/native inputs. There is no bare token or omitted
argument form. Reject wrong case, unknown K, duplicates, misplaced tokens
and conflicting native roles. Parse into a typed expected role/convention
under this exact grammar, retaining the original Contract; do not extract
arbitrary substrings or infer the token from an output role.

For a stored direct declaration, require a token and a valid direct payload.
A direct payload without the token, or token without the required direct
payload on a stored function, rejects. Restore the token's convention onto
the direct descriptor and cross-check the full reconstructed Function
against its declaration normally. On writing, derive the token from that
Function's checked fact and reject any conflicting separately retained
ModuleFunction contract. There is one serialized convention authority, not
a token/byte precedence rule. An imported bodyless expectation retains the
token without a Function payload, as intended. It supplies neither an
implementation nor permission for unbound standalone native execution.

Keep the token within the fingerprint's exact signature portion so ordinary,
set and graph binding compare role/convention **before** replacing the proxy.
Restore the original expected fact after resolution; do not copy it from
the replacement. Projection must not remove the token when clearing Function.
Retain it in parsed snapshots and any normalized declaration model.

This completes convention retention in bare private semantic modules as well
as native containers. It does **not** retrofit the full foreign DLL/export/
native-input descriptor onto a bare semantic import whose Function has already
been erased. The provider-binding proposal's native-promotion gate remains:
construct its outer descriptor from the original checked expected declaration
before erasure, or reject promotion. A retained convention alone cannot
recover the other missing foreign expectations from a new provider.

### Foreign-interface methods

Remove candidate 96's one-byte `nativeConvention` field from its foreign
ModuleFunction variant in revision 97. Keep `foreignInterfaceRef`,
declaredMethodOrdinal, resultType, receiverType, callerUnsafe, doesNotReturn,
reserved values, explicit-formal/native-input records, owner/member
references and reverse checks otherwise unchanged; the present variant's
fixed extent is reduced from 28 to **27 bytes** before formal/input payloads.
The foreign ModuleFunction Contract is exactly `|NativeCallable:K`, not
empty. Its convention comes from that one token and is checked against the
selected declaration's standard `0x000C` expectation when available.

Vectorcall, including private byte 3, remains invalid **for this foreign
member role**, independently of backend support. No NativeFunctionPointerType,
direct-import token/descriptor or additional convention byte is added.
Resolve exact declaring interface/member before consuming the token. Slot,
receiver access, vptr offset, raw prototype vtable metadata and output role 2
cannot choose it. Nested fnptr types retain their own independent complete
signature and target-support checks.

## Outer-3 expectation and provider binding

**Do not extend the outer DirectExpectation record.** Its complete shape
remains:

```text
u16 directExpectationCount
repeat directExpectationCount:
    String localFunctionName        // outer u16 length + strict UTF-8
    String providerIdentity
    String providerExportKey
    String foreignLibraryName
    String foreignExportName
    u16 nativeParameterCount
    NativeParameter[nativeParameterCount]  // unchanged 14-byte outer row
```

Its insertion remains immediately before Contract bytes: after TNPK machine
imports, TNSP dependency/declaration/machine-import lists, or TNGP type origins.
The importing Contract's mandatory direct-import token supplies the expected
convention. This is a checked **join by localFunctionName to the original
imported ModuleFunction**, not inference from an outer header, a provider
signature, DLL name, output role or replacement.

Before affected image loading/emission:

1. Admit the exact outer draft and inner 97 independently. Validate every
   retained declaration token/typed fact, including declaration-only imports.
2. Require a bijection between outer expectation rows and imported ordinary
   declarations carrying NativeImportCallConv. Each row selects one bodyless
   import with the same checked provider identity/key; each such import has
   one row, even with no machine call or outputs. No rows name source-owned
   direct imports, ordinary nonnative provider functions or foreign members.
   A standalone native producer with no selected dependencies has count zero.
3. Retain the expected convention from that importing declaration. Resolve
   the original selected provider/export and require an eligible owned
   DirectNativeImport whose convention is retained by its own declaration
   and whose **normalized convention exactly equals the expectation**.
   Compare every outer library/export/native-input fact and all inner
   signature, output, dependency, ownership, target/runtime and binding
   obligations under their existing rules. Library comparison remains Windows ordinal-ignore-case;
   export/key/convention comparison is exact. No convention has a strengthening
   relation to another.
4. Preserve both the original token and outer descriptor after success.
   Snapshots/caches cannot replace them with a provider's current defaults
   or recompute them from its body. Check all independent using expectations
   of one provider, not a weakest or last-wins merged contract.

These rules intentionally amend outer-3 admission/comparison together with
inner 97; old outer-3 snapshots cannot be reused under the new draft.
Adding a second outer convention byte would duplicate a fact already
required in the retained inner declaration and need another consistency
check without repairing an additional loss. Conversely, adding only an outer
byte and leaving semantic declarations unchanged would not solve bare `.trm`
or source-free reference/set/graph convention retention.

Keep the provider-binding route exactly:
consumer IAT for selected provider DLL/export, provider's real six-byte
`FF 25 disp32` leaf export, provider IAT for its declared foreign target.
The leaf is convention-transparent, not a convention-changing adapter.
Require support for the selected convention at **both** emitted boundaries;
equal x64 carriers do not permit metadata conversion. Keep its exact IAT
association, executable extent, displacement, export-root, frame/unwind and
actual-image validation. No bypass to the foreign DLL, PE forwarder, invented
semantic body or dummy abstract-member export is introduced.

The inherited private Windows x64/SystemDirectory route remains a bounded
implementation contract, **not** an encoding of source Link, Dynamic/Default
or CompatibilitySearch by omission. This proposal assigns no resolution-mode/
policy transport or source fallback. A source-selected loader/linker
requirement that this existing private route cannot represent and satisfy
must reject, not silently take SystemDirectory. The convention delta does
not expand that independently incomplete source/target boundary.

## Normalization, dependencies and consumption

Apply the source-schema owner's checked normalization **before** writing:

* Callable imports take only NativeImport.CallConv, omitted as Unmanaged.
  Data imports have no convention and acquire no private callable encoding
  here.
* Explicit method NativeCallable overrides its declaring interface's default.
  A bare or empty method annotation is an explicit Unmanaged override, not
  inheritance.
  Otherwise a directly declared method takes its interface default; method
  NativeExport without either selection implies Unmanaged.
* Publish one effective fact per affected declaring method, including
  uncalled/bodyless requirements. An inherited closed member keeps that
  exact declaring-member contract; a derived default cannot rewrite it.
  A same-named newly declared method has its own identity.
* Check exact convention/native role for selected witness implementations
  and retained requirements. No unannotated implementation, qualified-self
  forwarding, devirtualization or equal-carrier thunk silently acquires or
  changes that fact.

There is no persisted interface default or “inherit” byte. Consumers do not
replay source defaults. Candidate 96's foreign directory remains **flat**;
do not flatten ordinary inheritance into it, redeclare inherited members or
infer declaring identity from slots. Broader ordinary interface/witness
transport still needs independently complete owner/type/member records;
reject unavailable transport rather than claim this convention delta
supplies those codecs. Retained source/Core registration contracts, when
needed for source/schema consumption, keep their exact originating Module
context; normalized private consumers need no fresh Core registration merely
to interpret K or a convention byte.

Preserve the complete candidate-92 output collection and tag-41 reservations,
93 general dependencies and 95 copy-origin summaries on each original
definition/expectation. Foreign summaries remain zero/unavailable.
Convention selection does not narrow or widen any source bound, output
selector, result partition, publication phase, no-return guarantee, input
access/retention/extent, field origin or provider settlement. Output
comparisons remain exact where their owner requires equality; directed
general-dependency/summary relations remain directed under their own rules.

| Consumption boundary | Required retention/check |
|---|---|
| Private `.trm`, reference projection and semantic linking/JIT | Preserve typed signatures, direct tokens and foreign variants without source/debug text. Compare original role/convention before using a selected provider body/declaration. Existing semantic lowering scope does not create a PE export or waive native-container promotion gates. |
| TNPK and TNSP AOT | Capture original foreign descriptors before Function erasure. Join outer rows to inner expected conventions; validate all provider contracts/images before loading. Stored images and metadata derive from the same final checked identities, target and ABI. |
| TNGP roots, intermediates and TNPK leaves | Apply the same rules to every edge. Retain semantic-only foreign/type dependencies under candidate 96 even with empty code lists, plus declaration-only direct imports. Distinguish provider-owned imports from module-selected proxies; imported proxies remain ineligible for re-export. |
| Diamonds, packing and catalogs | Preserve every using Module's original provider/key/type/member/convention expectations. Equal foreign targets, names, slots or carriers do not merge identities. Preserve existing explicit ambiguity/path-collision refusals. |
| Consumer/Provider/GraphContractSnapshot | Retain exact schema, original fingerprints/tokens, typed signature closure, foreign directories and outer expectations together. Cache identity includes artifact/schema, target, binding and rule context; an old parsed module or replacement-derived expectation is not reusable. |
| Graph root-only JIT | Validate the entire selected closure and provider images, restore the root's original expected facts, then re-lower only that root. Keep provider DLLs precompiled and bind to the same provider/export route as AOT. No provider regeneration, implicit JIT fallback or source rebinding. |

All native invocations remain caller-unsafe, including scalar-only imports,
fnptr calls and foreign members. A serialized declaration propagates that
obligation; it is not unsafe authorization for operations in its body.
Preserve explicit unsafe scopes, known access/alignment/backing/provenance,
checked-reference versus raw-pointer distinctions, and actual code/provider
lifetime. No marshalling, pin, root, retain, ownership transfer, resource
adoption, cleanup, foreign unwind permission or NoThrow promise is created.
Output pointer-cell initialization is not pointee/resource validity.

## Remapping, bounds and verification

Remap decoded facts in their owning namespaces and contexts:

* Function-pointer/signature interning keys include exact convention and all
  complete parameter/result types. Clone/remap pointer/signature/type closure
  when using contexts differ; never merge different conventions by layout.
* Rewrite stored NativeFunctionPointerType references, fnptr occurrences,
  address/indirect-call targets, native-input/output/reservation pointees and
  all nominal origins, using the final private type maps. Regenerate textual
  type/fingerprint identities from the remapped complete facts.
* Preserve K's identity in a direct token and the same descriptor convention;
  neither convention byte nor mnemonic is a table index. Rebuild direct
  name/key joins and outer native-parameter pointees in the **importing
  embedded Contract's** final universe, not the provider's raw indexes.
* Keep candidate-96 interface/member/function forward and reverse mappings,
  typed tag-5 calls and source-order maps, plus general/output dependency
  ordinal conversions and copy-origin field paths. Physical table reorder
  is not a change to declaring-member order, selected origin or convention.
* Preserve original expectations before replacing proxies or eliminating a
  physical Import. Rebuild entry indexes, operation-local provenance and
  affected source spans/maps rather than persisting verifier caches.
  Recompute Contract lengths, outer offsets and SHA-256 after encoding;
  Contract growth is not image growth. Regenerate affected images from
  complete facts rather than patching semantic headers.

Retain the private 4 MiB structured-artifact, 1,024-function, 256-slot,
64-call-argument, 100,000-node and depth-128 budgets. Generic inner Strings
remain bounded by 1,024 UTF-8 bytes; native library/export names retain their
existing narrower 256-byte admission and private DLL-name rules. These are
private limits, not new source or standard capacities. Foreign receiver
position conversion and its `explicitFormalCount + 1 <= 256` restriction
remain unchanged.

For signature tables, validate nonnegative counts within the existing
100,000 bound, parameter counts 0..256, the minimum `9*signatureCount`,
each `4*parameterCount`, complete type-index ranges and total extents before
allocation/consumption. Apply the common work/depth budget across nested
types, not a fresh budget per signature. Empty tables are valid when no
retained fact needs a signature; an absent required row is not a default.

For direct payloads validate `0 <= nativeParameterCount <= explicit formals`
and <=256, `14*count`, string/descriptor extents and remaining bytes using
checked wide arithmetic. Preserve unique ordered formal ordinals, exact
pointee/access/retention/extent validity and disjoint input/output roles.
For tokens enforce the Contract String bound after expansion; never truncate
K or drop a role to fit. Reject unknown byte/mnemonic values and invalid
presence/role relationships on uncalled declarations as well as called ones.

Outer expectation count remains u16 bounded by 1,024 and retained imports;
nativeParameterCount remains u16 bounded by 256 and actual explicit formals.
Keep canonical ordinal localFunctionName order, uniqueness, name/identity
bounds, strict UTF-8 and the 14-byte native-input extent checks. Other outer
size, dependency, image and checksum limits remain those of each container.
No extra count, optional table or allocation budget is introduced. Existing
output/dependency/summary/directory counts, reserved fields, cycle checks and
canonicality rules still apply.

Producers must publish schema-valid and truthful normalized contracts and
emit images implementing them. Consumers must check framing, counts,
reference closure, role/type/schema/target support, internal consistency and
retained binding before affected use. These checks do not prove an arbitrary
foreign implementation or re-prove producer bodies. A lying producer that
erases a role or labels incompatible native code as a convention is a truth
violation; checksums, valid records and successful loading do not establish
truth. Optional audits remain separate, not a serialized trust flag or a
requirement to reconstruct source annotations.

## Alternatives and recommendation

| Candidate | Assessment |
|---|---|
| Add only a direct Function byte and an outer expectation byte | Small textual codec patch, but imported semantic declarations still lose the expected convention. It does not complete bare `.trm`, reference or semantic set/graph replacement. Not selected. |
| **Reuse callable/foreign facts; add the direct declaration token; restore the direct descriptor from it and join native expectations to it** | Small complete convention amendment across all existing consumption scopes. It follows existing declaration-owned restoration and needs no redundant Function or outer field. **Selected.** |
| Add the direct Function byte as well as that declaration token | Complete if checked for equality, but serializes the same new fact twice. GetFunction already receives declaration facts, so a second byte/authority supplies no needed information. Not selected. |
| Add convention/role fields to every ModuleFunction or put all conventions in a new common table | Could unify lookup, but duplicates existing typed callable and foreign facts, adds new references/consistency rules and does not by itself solve unsupported complete callable shapes. Not needed for this decision. |
| Move the entire direct-import descriptor from Function into a declaration-owned native-contract variant, unifying imported/owned descriptors and retiring or redesigning the outer foreign table | A substantive alternative: makes full bare-semantic foreign descriptor retention uniform and can remove declaration/body duplication. It requires changing direct descriptor ownership, every projection/restoration path and the existing outer table contract, not just convention transport. Worth a separate deliberate redesign, but unnecessary for the bounded missing fact and not selected as a second codec. |

The existing drafts are unpublished. Retaining outer 3 and the foreign 0..2
map is not a compatibility concession: their current records are sufficient
once the inner expected convention is retained. A fresh outer number would
not remove the need for exact draft agreement or complete inner admission.
97 gives this composed private candidate one exact selection; it does not
require shipping intermediate 93–96 readers/writers.

## Implementation gaps and acceptance cases

Implementation remains outstanding. The work required before enabling 97 is:

1. Bind/normalize the separately proposed exact source/Core schema and carry
   all four typed effective identities. Add the direct descriptor fact;
   preserve existing callable/foreign typed authorities without reducing
   unsupported roles to fnptrs.
2. Complete 93/94/95/96 prerequisites or integrate directly into the complete
   destination. Add the declaration grammar/restoration rule, explicit
   projection maps, role/conflict/presence validation and all exact revision/
   type/layout/profile/producer allowlists. Do not enable a partial 97.
3. Update shared traversal/rewriting, signature interning, reference
   projection, dependency binding/replacement and source-free restoration,
   including RecordedImportPromises and parsed snapshots. Preserve every
   original collection and convention before substituting a provider.
4. Join native expectations to the new inner fact in single-provider/set/
   graph paths. Retain the exact provider leaf/IAT and image validation
   requirements. Implement supported ABI mappings in AOT/JIT/lowering, or
   refuse affected use explicitly.
5. Add the cases below through public compiler APIs and independent exact
   byte fixtures. Record support and final-patch/compiler/configuration/input
   evidence only after appropriate qualification; update support inventories
   then, not by treating this design document as passing evidence.

These are **required future regressions**, not tests run in this task:

| Family | Required cases/outcomes |
|---|---|
| Source normalization | Each convention on eligible imports and NativeCallable methods; omitted import CallConv; bare/empty/explicit-Unmanaged NativeCallable; interface Cdecl default, Stdcall override and bare Unmanaged override; export implication. Exact-identity aliases and E-typed constant forwarding retain the same effective fact. Persist identity, not annotation presence or token spelling. Wrong Core/schema/member owners, same-spelled unrelated enums and wrong-type/zero/unknown/duplicate arguments still reject under the source owner. |
| Declaring identity/inheritance | Base Cdecl member under derived Stdcall defaults, same-named new members and diamonds retain the correct declaring identity and effective convention. Supported transport preserves it; missing ordinary interface/witness carriers refuse rather than flatten to candidate-96 foreign rows. Unannotated/mismatched witness implementations reject. |
| Private/standard projections | Independently exercise private 0/1/2/3 versus standard 1/2/3/4, especially standard Stdcall 3 versus private Stdcall 2. Source enum zero remains invalid despite private Unmanaged zero. Reject private 4/255 and old Native mnemonic; never cast or infer a schema. Standard bytes/clauses remain unchanged. |
| Callable/fnptr round trip | Same signature with four different conventions remains four identities through interning, references, remapping, address formation and indirect calls. C's typed reference and Contract agree without an added per-function convention. Missing/wrong signature reference, mismatching token, entry-name role conflict and attempted convention cast reject. |
| Direct payload/declaration | Independently authored exact-97 bytes cover all four direct conventions, zero/nonzero input annotations and no/one/multiple outputs. Verify token placement, unchanged descriptor counts/tail and restoration without a Function convention byte. Missing/duplicate/misplaced tokens, in-memory fact/retained-contract mismatches and direct versus C/foreign role collisions reject. An erased bodyless import still retains its expected role/convention. |
| Foreign member reuse | Foreign Unmanaged/Cdecl/Stdcall survive full owner/member/source-free projection through the common NativeCallable token; the variant has the exact 27-byte fixed extent and no convention byte. A Vectorcall token rejects for this foreign role even when generic fnptr Vectorcall is retained. Equal slot/signature/receiver bits never select another identity. Nested fnptr facts keep their own independent domain/support checks. |
| Whole composition | Exact independent 97 fixtures combine 89 arithmetic modes, 92 outputs/tag 41, nonempty 93 general dependencies, all 94 scalar payload widths, ordinary nonempty 95 summaries and 96 directories/typed tag-5 calls. Foreign summaries stay zero. Empty new collections still require their exact prefixes and 97 header. |
| Source-free expectations | Remove source/debug and provider bodies from reference expectations; retain effective C signatures/direct tokens/foreign variants. Exercise unused and declaration-only imports. A missing original foreign descriptor still blocks native promotion; never reconstruct it or the expected convention from a replacement. |
| Outer join/admission | TNPK/TNSP/TNGP have unchanged expectation row bytes and mandatory counts. Reject orphan/missing/duplicate/unsorted rows, wrong provider/key, a direct row targeting C/foreign/source-owned declarations, and a direct token without its required native row. Recompute checksums on negative fixtures so the inner/join gate is reached. |
| Compatible replacement | Change unrelated bodies/images and reorder physical type/signature/function/directory tables while retaining selected identities, conventions and complete contracts. Unchanged consumer/root/intermediate artifacts bind in semantic linking, stored-image AOT and root-only JIT. |
| Incompatible replacement | Change only Unmanaged to Cdecl or Cdecl to Stdcall with identical x64 carriers; change native role, provider/key/member owner, foreign DLL/export, native inputs or output/dependency facts. Reject each original expectation before affected loading. Two consumers expecting different conventions cannot be collapsed or repaired from the provider. |
| Sets/graphs/snapshots | Root/intermediate/leaf direct providers, semantic-only foreign/type edges, declaration-only imports, diamond using contexts and snapshot reuse preserve original conventions alongside all origin/cleanup/output contracts. Old-schema/context/provider snapshots refuse; graph JIT does not rebuild providers or bypass their thunks. |
| Rewriting | Reorder/remap structures, pointers, nested fnptrs, foreign owners/members, formals and call targets. Rebuild complete fingerprints, direct joins, output/reservation pointees, dependency/field paths, lengths and digests. Convention bytes and scalar payload octets are not references; different conventions never deduplicate by carrier. |
| Bounds/malformed input | Empty/max/over-limit signature and parameter counts, 256-slot versus independent 64-call limits, foreign receiver +1, depth/work exhaustion, negative/overflowing/truncated extents, malformed UTF-8, max/oversized fingerprints, invalid Boolean/reserved fields, wrong type namespaces, cycles and incomplete closures reject safely without budget resets. |
| Exact schema | Reject all 92–96/unknown inner revisions, outer 2/unknown versions, old outer-3 inner selections and mismatched nested/snapshot schemas. No header-only upgrade, `>=` branch, Native alias, old tuple reader or fallback. Do not claim provenance detection for every maliciously relabelled structurally valid stream. |
| ABI and unsupported mappings | For implemented mappings, exercise static versus member receiver/hidden-result order, mixed integer/floating/register/stack arguments, named source-order staging and native callback/address paths independently. A known unsupported convention declines native use without coercion; Vectorcall needs its complete mapping, not successful metadata round trip. |
| Provider leaf/image execution | Headless supervised single-provider/set/graph AOT and root-only JIT use the actual selected provider leaf/IAT route with precompiled providers. Retain all actual-PE mismatch/unwind/export tests; no convention-changing thunk or provider bypass. Respect loader/fixture gates, with no system-DLL installation or weakening of safety gates. |
| Output, resource and unsafe boundaries | Preserve success/complement results, cancellation before Invoke, exceptional indeterminacy, source/backing dependencies, explicit receiver/formal conversions and provider settlement through persistence. Unsafe declarations do not authorize body operations; CallConv supplies no marshalling, pin/root, ownership, resource adoption or code lifetime. |
| Trust/evidence | Valid metadata/checksums are structural evidence only. A lying native fixture is a provider truth violation, not mandatory arbitrary-code re-proof. Keep source/record admission, ABI emission, stored-image execution and supervised JIT evidence distinct and tied to final inputs. |

## Approval and completion boundary

No private convention map, placement, expectation carrier, outer-field choice
or version-composition decision is left open in this candidate:
**97 = complete 96 + this delta; outer 3 retains its exact field layout and
compares convention through the importing inner declaration.**

Formal adoption of this transport and its source/foreign/output prerequisites
remains outstanding. Broader Core/interface/witness declaration codecs,
resolution-policy transport, unsupported signatures/targets and actual native
qualification remain independent gates, not permission to invent fallbacks.
This proposal does not claim compiler support, standard TRRM conformance or
acceptance of the active compiler milestone.
