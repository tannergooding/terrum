# Native-import resolution in private persistence

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow, not normative adoption or implementation support.**
The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Decision and scope

Amend the **unpublished candidate private revision 97**, rather than select
another revision. Retain its complete convention/foreign-identity composition
and add one mandatory, syntax-normalized **resolution token in the existing
declaration `Contract` String** for every direct callable import definition
and retained expectation:

```text
|NativeImportResolution:Link
|NativeImportResolution:Dynamic:Default
|NativeImportResolution:Dynamic:CompatibilitySearch
```

These are three alternative complete tokens, not three optional attributes.
Keep candidate 97's separate `|NativeImportCallConv:K` token. Resolution is
not part of a function-pointer type's convention, a native-output role or the
enclosing module's checked-failure policy.

Keep the existing DirectNativeImport Function payload and the **outer-3
TNPK/TNSP/TNGP expectation table byte layouts unchanged**. Restore resolution
from the importing declaration, and join the existing outer descriptor to
that original declaration before provider comparison. Store the new fact
once, not in the Function payload and again in a native-container row.

The normalized resolution domain applies equally to callable and static-data
imports. This bounded delta places it on the already selected callable
declaration carrier; **private static-data declaration, address and provider
transport remain a separate, closed gate**. No field is encoded as a function,
and no data convention or outer data row is invented.

This decision completes persistence of the missing facts, not their target
implementation. In particular, the prototype's SystemDirectory route is
**not** an encoding of Link or Dynamic/Default. Native use still requires a
selected implementation of the actual linker/loader contract below.

## Owners and precise intended amendments

| Owner | Contract retained or amendment selected here |
|---|---|
| [Native declaration roles](../specs/native.md#native-declaration-roles) | Owns Link versus Dynamic, conditional policy omission, target loader behavior, provider-library retention, callable/data applicability, unsafe access and exact symbol spelling. Preserve these accepted semantics without adding a resolver, fallback or per-pointer unload lease. |
| [NativeImport metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry](../specs/representation/registry.md) | Own the assigned 16-byte standard row and its distinct callable/data cases. No row, reference namespace, numeric assignment or standard format pair changes here. |
| [Exact source schemas](native-calling-convention-source-schema.md#complete-nativeimport-schema), [Core registration](native-calling-convention-core-registration.md#proposed-registration-hand-off) | Own the unapproved exact argument/enum candidates, registered identities, checked typed constants, defaults and source rejection rules. Consume their normalized result; do not introduce a new source overload or textual enum recognizer. |
| [Convention private persistence](native-calling-convention-private-persistence.md#exact-composition-and-selection) | Retain its complete 97 composition, callable tokens, private fnptr domain and foreign variant. Amend its direct-import declaration grammar/restoration and outer admission to require the resolution fact here. Supersede its statement that resolution transport is unassigned; do not alter its other carriers. |
| [Direct-import provider binding](native-output-direct-import-provider-binding.md) | Retain the selected provider/export route, real leaf export, original foreign expectation, image checks and exclusions. Amend comparison to include resolution from the inner declaration. Its SystemDirectory bridge is not retrospectively assigned an accepted source resolution meaning. |
| [Foreign private identity](native-output-foreign-interface-private-persistence.md), [native-output cells](native-pointer-output-ownership.md), [source dependencies](aggregate-native-output-source-dependencies.md) | Keep nominal directories, typed foreign calls, output collections/reservations, dependencies, ownership and result correlation unchanged. Foreign-interface methods are not direct library imports and receive no resolution token. |
| [Persistence taxonomy](../specs/representation/persistence.md), [format versioning](../specs/representation/format-versioning.md), [binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Preserve private/standard separation, exact draft agreement, every original retained expectation, schema-owned references and structural safe consumption versus producer truth. |
| [Module linking](../specs/modules.md), [sets](../specs/module-sets.md), [graphs](../specs/native-graph.md), [JIT](../specs/jit.md), [Windows deployment](../specs/targets/windows-x64-deployment.md) | Preserve consumption scopes, selected precompiled providers, dependency/origin/cleanup closure and actual code/support lifetime. A new string token enables none of those facilities by itself. |

These are integration instructions for the affected candidates, not edits to
those documents or normative owners. The provider-binding document retains
its own unapproved/provisional status. No standard Section, DefId, clause,
Feature, instruction, target ID or source semantic edition is allocated.

## Current implementation evidence

These are inspected source boundaries, **not newly run tests, ABI probes or
execution evidence**. The worktree contains in-progress changes; newer
in-memory facts must not be confused with their persisted representation.

| Inspected source | Evidence and consequence |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader` | The writer's final structured selection and the header reader's sole admitted revision are **92**. The header uses `TRRM` followed by an i32 revision. Intermediate version predicates and native-profile labels do not implement a complete 97. |
| Same file, `PutFunction`, `GetFunction`, `Contract`, `ReadFunctions` | The direct Function branch writes library/export strings and the native-input vector, with outputs owned by the declaration collection. It has no resolution fields. The declaration fingerprint has no resolution token; imported ModuleFunctions can have no Function payload. A Function-only addition would lose expectations on those imports. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `DirectNativeImport` | In-progress typed resolution mode/policy and convention properties are present. The internal policy sentinel `NotApplicable=0` can represent absence on Link; it is not a source policy, a new accepted policy or proof of a wire carrier. Default-valued object properties do not establish checked source normalization. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), `DirectImportExpectation`, serialization and provider validation | Outer version **3** retains local name, provider/key, foreign library/export and native parameters, but no resolution or convention. Current descriptor comparison does not recover the original missing resolution expectation. |
| [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), snapshots | Projection, merging, graph reads and restoration are separate paths. `RecordedImportPromises` restores selected effect flags from a fingerprint; that alone does not restore normalized import facts or the original full native descriptor. |
| [`NativeModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs), [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | Set/graph packages also select outer 3 and carry parsed Contracts. Root-only JIT re-lowers the root while retaining precompiled providers. Every edge and cached expectation needs the same resolution checks. |
| [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), `System32ImportPath`, `QualifyDirectNativeImports`; [`NativeJit.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs), `EmitForExecution` | Ordinary direct PE imports are qualified to the Windows system directory; direct JIT constructs that same full path. The JIT loader uses `LoadLibraryExW` with `0x100 \| 0x800` on selected full paths. Neither observation establishes source Dynamic/Default's bare-name `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` behavior or configured-linker Link semantics. |
| [`TrRmNativeImportTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeImportTable.cs) | Its provisional row is four u32 words: method/library/export/policy, with a System32 policy check. Although also 16 bytes, it is **not** the assigned ownerKind/mode/policy/convention standard row. Equal extent, TRRM magic or a policy integer of 1 cannot select an alias decoder. |
| [`NativeOutputModulePersistenceTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs), [`NativeSourceImportTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Interop/NativeSourceImportTests.cs), [`NativeModuleTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleTests.cs) | Existing test source covers revision-92 output persistence, prototype source imports and SystemDirectory image behavior. The [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj) has no explicit Compile exclusions. These observations establish test locations, not passing coverage of this resolution proposal. |

## Normalized meaning and explicit domain mappings

Use the logical sum **Link | Dynamic(Default) |
Dynamic(CompatibilitySearch)**. An equivalent checked implementation can use
a mode plus a conditional policy, but Link has **no policy value**. Internal
absence sentinels must not escape as source arguments or serialized policies.

The source enum integers below are the **unapproved source-schema
candidates**. The standard values are already assigned; the private mapping
selected here consists only of the exact tokens.

| Checked source selection | Effective mode/policy | Standard mode/policy bytes | Exact private resolution token |
|---|---|---|---|
| Mode omitted or explicit Link; policy omitted | Link, no policy | `1 / 0` | `\|NativeImportResolution:Link` |
| Explicit Dynamic; policy omitted or explicit Default | Dynamic, Default | `2 / 1` | `\|NativeImportResolution:Dynamic:Default` |
| Explicit Dynamic; explicit CompatibilitySearch | Dynamic, CompatibilitySearch | `2 / 2` | `\|NativeImportResolution:Dynamic:CompatibilitySearch` |
| Mode omitted or Link; any explicit policy, including Default | Invalid source | No row | No token |

The proposed Core mode enum is exactly Link=1, Dynamic=2; the proposed policy
enum is exactly Default=1, CompatibilitySearch=2, with the exact nominal
owners/registration and int32 bases selected by the source-schema candidate.
An integer, unrelated enum, wrong owner, unknown value or zero does not
become one of these values by casting or matching a spelling. Checked
same-enum constant forwarding and aliases to the exact selected declarations
retain ordinary identity; no member-expression provenance tag is required.

The omission distinctions are deliberate:

* Omitted mode and explicit Link normalize identically. On Link, **omitted
  policy is required**, not a selection of Default. Explicit Default on Link
  is rejected before normalization, not discarded.
* On Dynamic, omitted policy and explicit Default normalize identically.
  Persist the effective Default, not an omitted/unspecified policy for a
  consumer to choose later.
* A private declaration missing the mandatory resolution token is malformed,
  even if source would have allowed omitted arguments. A reader does not
  apply source defaults.
* Persist no authored-omission bit. Source/debug data may retain spelling,
  but is not needed to recover meaning and cannot repair missing semantics.

Convention remains independent:

| Import owner | Effective convention | Assigned standard ownerKind/callConv | Private projection |
|---|---|---|---|
| Static callable | Omitted CallConv selects Unmanaged; otherwise one of Unmanaged/Cdecl/Stdcall/Vectorcall | `1 / 1..4` | Existing `NativeImportCallConv:K`, with exactly those four case-sensitive mnemonics |
| Directly addressable static data field | No convention; any explicit CallConv, including Unmanaged, rejects | `2 / 0` | No callable token; private field transport remains gated |

Retain candidate 97's explicit private **fnptr signature** map
`0=Unmanaged, 1=Cdecl, 2=Stdcall, 3=Vectorcall`. It does not encode resolution.
Do not cast the source enums, the host enum, standard bytes or a prototype's
SystemDirectory policy word into either private domain. `Native` is not a
97 mnemonic or an alias for the source omission result.

Normalize names under the source owner as well: omitted entry means the exact
declaration name; a supplied entry has its exact target spelling. Keep the
resolved library/export names in their existing descriptor carriers. No new
name token, suffix, decoration, runtime string conversion or empty sentinel
is selected. Existing private Windows DLL/name restrictions remain private
admission limits, not restrictions added to accepted source or other targets.

## Exact revision selection

The destination is **amended 97**, composed as follows:

1. Retain the complete 92 output/NoThrow-retirement composition, including
   the scalar/pointer-cell amendment, resolved arithmetic modes, declaration
   output collection, old-vector/suffix removal and reservation tag 41.
2. Retain 93's mandatory general declaration dependencies, 94's complete
   wide-scalar catalog/type-derived payloads, and 95's mandatory copy-origin
   summaries. Output-specific dependencies remain in their own collection;
   foreign summaries remain unavailable.
3. Retain 96's nominal directories, foreign ModuleFunction signature variant,
   typed tag-5 call targets, origins and foreign-aware outer admission.
4. Apply the complete [97 convention delta](native-calling-convention-private-persistence.md#exact-composition-and-selection):
   private fnptr 0..3, NativeCallable/NativeImport convention tokens, and the
   foreign variant's 27-byte fixed prefix with no convention byte.
5. Apply this document's resolution-token grammar, restoration, validation
   and native expectation join. All other fields and extents remain those
   of that exact composition.

This is not “92 plus two strings,” a reduced 97, or permission to omit
prerequisites. All structured artifacts, including those without native
imports, select the same little-endian **i32 revision 97**. Every ordinary,
graph-library, producer, nested-Contract, reference and snapshot path must
select that complete schema.

| Revision alternative | Assessment |
|---|---|
| **Amend/reallocate unpublished 97** | Selected. The previous 97 is a proposal, while the inspected selected codec is 92. Completing its direct-import facts needs no historical compatibility concession or extra header value. This document intentionally replaces that earlier exact draft selection. |
| New revision 98 containing complete prior 97 plus this delta | Reasonable if independently exchanged 97 artifacts need a separately selected schema identity. It would require updating every header/allowlist/snapshot path, and still would not supply exact draft agreement or loader support. Not selected merely to preserve unpublished assignments. |
| Keep both interpretations under 97 and detect tokens/payload shape | Not selected. This is a guessed decoder, not exact schema selection. |

Apply [format versioning](../specs/representation/format-versioning.md)
without inventing a historical alias:

* Exchange first agrees out of band on this entire draft composition, not
  just the numeral 97, a filename, registry digest, build/profile label or
  native container. No new on-wire schema discriminator is assigned.
* Under this selection, reject other inner revisions before their records
  are decoded; no `>=97`, old reader, retry, legacy tuple or byte promotion.
  The selected outer-3 reader admits only the agreed outer draft, with
  independently selected amended-97 Contracts throughout the retained
  closure; outer 2 and unknown versions are not alternatives.
* The earlier 97 draft is not supported input or an alias. Mandatory tokens
  catch missing resolution on retained direct imports, but a token-free
  module with no imports can be byte-identical across drafts. Equal bytes
  neither prove draft selection nor reveal every dishonestly relabelled
  stream's history.
* Regenerate from complete checked inputs. No old-artifact converter is
  required. An explicit conversion would need selected source/destination
  schemas, decoded meaning and complete required facts, then rebuilt
  references/indexes/lengths. It cannot infer Link, Default or a foreign
  descriptor from old missing fields, SystemDirectory paths or a replacement.
  Rewriting a header is not conversion.

## Exact declaration and Function encoding

### Declaration carrier and token grammar

The carrier is the existing **ModuleFunction.Contract String**, not a new
ModuleFunction field or table. Inner String remains a little-endian i32 UTF-8
byte count followed by that many strict UTF-8 bytes. The fixed token text is
ASCII, case-sensitive, without whitespace, escaping, numeric operands or
qualified source enum names.

For an ordinary callable direct import, the existing signature/role portion
is followed by this exact suffix grammar:

```text
ResolutionToken :=
    "|NativeImportResolution:Link"
  | "|NativeImportResolution:Dynamic:Default"
  | "|NativeImportResolution:Dynamic:CompatibilitySearch"

ConventionToken := "|NativeImportCallConv:" K
K := "Unmanaged" | "Cdecl" | "Stdcall" | "Vectorcall"
ReturnToken := "|MayReturn" | "|DoesNotReturn"

DirectImportSuffix :=
    ResolutionToken ConventionToken ReturnToken "|CallerUnsafe"
```

ResolutionToken is immediately **before** the existing convention token,
which remains immediately before the final return/effect suffix. Any other
eligible role tokens precede this suffix; that ordering does not authorize
direct imports to acquire vtable, NativeCallable or NativeCallableEntry roles.
Raw callable imports require CallerUnsafe even with scalar-only signatures.
Existing independently valid no-return rules remain unchanged.

| Added token | Exact additional UTF-8 bytes inside Contract |
|---|---:|
| `\|NativeImportResolution:Link` | 28 |
| `\|NativeImportResolution:Dynamic:Default` | 39 |
| `\|NativeImportResolution:Dynamic:CompatibilitySearch` | 51 |

There is **no additional length prefix**, policy byte, flags word or padding.
The existing convention token remains `22 + byteLength(K)` bytes; it is not
counted again as a new resolution field. NativeCallable and the foreign
variant keep their existing `NativeCallable:K` grammar with **no** resolution
token. Parameter fnptrs have their own conventions and select no import mode.

Require exactly one ResolutionToken and one ConventionToken on each owned
direct callable declaration and each module-selected direct callable
expectation, including unused declarations, declarations without outputs or
input annotations, and expectations with `hasFunction=false`. Neither token
is optional because a value is a default. Ordinary nonnative declarations,
NativeCallable-only methods and foreign-interface members have neither direct
import token.

Parse the declaration grammar into typed facts; do not search arbitrary
substrings or infer a native role from an output annotation. Reject duplicate,
misordered, misplaced, incomplete and unknown tokens even if duplicated
values agree. In particular reject `Link:Default`, `Link:0`,
`Link:NotApplicable`, `Dynamic`, `Dynamic:0`, `Dynamic:None`, numeric mode or
policy operands, extra components and case variants. Text in an ordinary
declaration name is not a role token.

### Unchanged stored DirectNativeImport payload

Keep its existing position inside the ordinary Function record:

```text
Boolean isExternal
Boolean hasDirectNativeImport
if hasDirectNativeImport:
    String libraryName
    String exportName
    i32 nativeParameterCount
    repeat nativeParameterCount:
        i32 formalOrdinal
        i32 pointeeType
        u8 access
        u8 retention
        i32 extent
// Existing ParameterCount, variable counts, spans, variables and body follow.
```

Boolean remains exactly one byte, 0 or 1. The conditional descriptor,
excluding the two existing Booleans, remains exactly
`12 + libraryByteLength + exportByteLength + 14*n` bytes. Its binary delta
is **zero**. The input count is mandatory, including zero; no old output tuple
or new mode/policy/convention byte follows it. Output facts remain exclusively
in the existing declaration-owned output collection.

ReadFunctions decodes the retained resolution and convention **before**
GetFunction. GetFunction receives those checked facts and restores the
DirectNativeImport descriptor alongside the already supplied output facts.
It must not invoke an object-constructor default to choose resolution.
An internal NotApplicable sentinel, if used, is assigned only as the
implementation representation of Link's absence, never decoded as a policy.

For a stored direct declaration, require the two tokens, an eligible
external/bodyless/static callable and a valid direct payload. A payload
without either token, or tokens without that required payload on a stored
Function, rejects. A bodyless imported expectation deliberately has the
tokens without a Function payload; it is not an owned native implementation
or permission for unbound standalone execution. Static callable eligibility
includes the source owner's top-level extern functions, not only explicitly
static member declarations.

Writers derive tokens from checked normalized declaration facts and reject
conflicting retained contracts or invalid mode/policy combinations. A missing
in-memory fact is not source omission. A reader reconstructs the Function
from the declaration authority and then performs the existing full
declaration/Function consistency checks. There are not two serialized
authorities with a precedence rule.

Retain the generic inner String limit of 1,024 UTF-8 bytes **after expansion**,
the 4 MiB structured-artifact limit, and existing function/slot/node/depth
budgets. Check length/count arithmetic and remaining extents before allocation
or consumption. Never truncate a token, drop a native role or reset a budget
to fit the limit. Existing native-input bounds, pointee/reference validation,
ordinal uniqueness/order, access/retention/extent rules and disjoint
input/output roles remain unchanged.

## Outer-3 tables and original provider expectations

Do not insert mode/policy into the outer table or reinterpret its Strings.
Its exact existing shape remains:

```text
u16 directExpectationCount
repeat directExpectationCount:
    String localFunctionName
    String providerIdentity
    String providerExportKey
    String foreignLibraryName
    String foreignExportName
    u16 nativeParameterCount
    NativeParameter[nativeParameterCount]  // Existing 14-byte outer row.
```

Outer String is **u16 UTF-8 byte length plus strict UTF-8**, not the inner
String codec. The table prefix is two bytes. Each row remains
`12 + sum(the five string byte lengths) + 14*n` bytes. NativeParameter keeps
u32 formalOrdinal, i32 pointeeType, u8 access, u8 retention and i32 extent.
No new field, optional trailer, data-owner discriminator or outer version 4
is selected.

The table remains immediately before embedded Contract bytes: after TNPK's
machine imports, TNSP's dependency/declaration/machine-import lists, or TNGP's
type-origin table. Keep the mandatory zero count on standalone producers
without selected dependencies. Counts remain bounded by 1,024 retained
imports and 256 actual explicit formals; retain canonical ordinal local-name
order, all name/identity bounds, uniqueness and complete extent checks.

The new token changes embedded Contract bytes and therefore ContractLength,
following offsets, total size and SHA-256. It changes neither outer row
extent nor ImageLength merely by being serialized. Keep the existing length
and checksum coverage rules; do not reuse an old image as proof that its
newly labelled contract is implemented.

Before affected emission/loading:

1. Admit the exact outer draft and inner amended 97 independently. Validate
   retained declarations, including their role and mode/policy consistency.
2. Require a bijection between outer expectation rows and module-selected
   callable declarations carrying **both** direct-import tokens. Join by
   checked localFunctionName and enclosing dependency provider/key. A row
   cannot name a source-owned import, ordinary provider function, foreign
   member or field. A retained uncalled import still needs its row even if
   it has no machine IAT use.
3. Retain resolution and convention from the **original importing
   declaration**. Resolve the selected provider identity/export key first.
   Require its owned DirectNativeImport declaration, exact normalized
   mode/policy and convention, then compare the retained outer descriptor
   and all other obligations. No replacement-derived expectation is valid.
4. Compare the existing foreign library names under the selected private
   Windows ordinal-ignore-case rule, export/key text ordinally, and input
   pointees by complete remapped identities and logical formal
   correspondence. Preserve all output, dependency, copy-origin, access,
   ownership, target/runtime and declared-behavior checks.
5. Preserve every using Module's original tokens and outer descriptor after
   success. Do not merge conflicting expectations because DLL/export names,
   function types, x64 carriers or loaded native handles coincide.

Mode/policy is an **exact dimension**, not a directed “more secure” guarantee.
Link cannot satisfy Dynamic, and Default cannot substitute for
CompatibilitySearch in either direction. They remain distinct on Linux even
where the current loader mapping is the same. Physical lookup/IAT/handle
sharing is permissible only when the selected target mapping satisfies all
retained contracts; it cannot erase their declaration-level distinction.

This does not move the full foreign descriptor into a bare semantic imported
ModuleFunction. Candidate 97 still leaves library/export/native-input facts
in the owned Function or the native outer expectation. Capture that original
descriptor from the original checked reference-bearing program before
Function erasure when constructing native packages; the existing outer
table then retains it. If an already-erased `.trm` expectation has no such
original checked input available, refuse native promotion. No new serialized
shadow descriptor or optional trailer is selected. Copying the replacement's
descriptor is not recovery. Retaining mode/policy alone does not close this
existing full-descriptor transport gate.

## Actual resolution and the provider bridge

Preserve the accepted target behavior, not an approximation inferred from
the current emitter:

| Effective selection | Required behavior |
|---|---|
| Link | Use the configured native linker, which may select static or shared inputs. There is no runtime resolution policy. A source omission is not authority to replace that linker with the prototype's system-directory lookup. |
| Windows Dynamic/Default | Use the selected loader's documented `LOAD_LIBRARY_SEARCH_DEFAULT_DIRS` behavior: application directory, System32 and registered user directories; no current-directory or PATH fallback. |
| Windows Dynamic/CompatibilitySearch | Use documented ordinary unqualified loader search, not System32-only lookup or an automatic fallback after Default fails. |
| Linux Dynamic/Default or Dynamic/CompatibilitySearch | Both currently use documented bare-name dlopen search, including process-start LD_LIBRARY_PATH. Preserve distinct selected policies despite that common mapping; this private Windows codec does not thereby acquire a Linux backend. |

Keep documented loader behavior, including its loaded-module rules. Do not
invent a fresh-instance guarantee, arbitrary resolver callbacks, library-
specific fallback, probing through several modes or new failure sentinel.
Unavailable linker/loader support or unresolved required symbols decline
affected use; successful metadata decoding is not successful native binding.

For Dynamic, retain the provider library for the lifetime of the importing
Terrum module/image. Address formation or imported-data access does not
acquire a shorter per-pointer unload lease. In the provider bridge, the
provider owns its foreign import and its retention obligation; the consumer
also retains that selected provider's code/support for all uses. Package
loading of `terrum-P.dll` is a separate deployment boundary, not the
NativeImport policy for P's foreign library.

The selected provider route remains:

```text
consumer IAT for selected provider P/export K
    -> P's real six-byte FF 25 disp32 leaf export
    -> P's exact declared foreign IAT target
```

The leaf is ABI-transparent, not a loader, static-link adapter, convention
adapter or failure bridge. Retain its exact export/IAT association,
displacement/extent checks, rooting of otherwise uncalled foreign imports,
leaf-unwind treatment and actual provider/consumer PE validation. Keep the
provider-binding candidate's address/entry-alias/re-export exclusions.
An imported proxy is not provider-owned. Do not bypass P to resolve its
foreign target in the consumer or root JIT.

**No new native resolution implementation is selected by this persistence
delta.** The current SystemDirectory-qualified foreign IAT route is not
equivalent to any normalized selection merely because it reaches a DLL:

* It is not Dynamic/Default, which admits application and registered user
  directories. A full-path JIT LoadLibraryEx call with other search flags is
  not that bare-name mapping.
* It is not Dynamic/CompatibilitySearch, whose documented search differs.
* It establishes no configured-linker Link result. Link/shared might fit
  this leaf/IAT shape only with an explicitly selected, complete linker
  mapping and inputs that actually produce and validate that exact target.
  A static-linked result without the required foreign IAT cannot be
  smuggled into this provider bridge.

Consequently, do not enable affected native creation/emission/loading under
amended 97 by assigning the legacy route one of these labels. A known,
structurally valid resolution fact may be retained and inspected without
claiming its target mapping is supported. Until the required mapping is
supplied, decline the affected native use, including a declaration-only
provider expectation whose resolution requirement is unsupported.

A future runtime-loaded cell, loader helper, delay-load scheme, differently
rooted static link or data symbol bridge needs its own complete image,
initialization/failure/lifetime and validation decision. Keeping the same
outer field widths does not authorize such an image-schema change. In
particular, a Dynamic token does not silently reinterpret the existing
foreign PE IAT as a runtime-initialized pointer cell.

## Static-data gate, not a callable-only semantic rule

Accepted static-data imports use **the same three normalized resolution
alternatives** and conditional policy rules. They have no CallConv,
NativeCallable role, fnptr signature or generated callable leaf. Their
storage is the actual imported static field, not a copied value. Preserve
direct interoperability, caller-unsafe reads/writes, complete field/owner
identity, representation, address validity and module/image library
retention.

Native field mutability retains its specific direction: a read-only import
may bind to a mutable provider, but a writable import cannot bind to a
read-only provider. This is not hardware page protection, initialization,
ownership transfer, pinning, marshalling or authorization for unsafe writes.
Ordinary metadata FieldDef layout-query imports are not native symbol
bindings.

Candidate 97 does not select a complete private static extern FieldDef
declaration/expectation/address codec or an outer data-provider record.
Existing aggregate member-field serialization is not such a carrier.
Therefore:

* This proposal assigns no field Contract String, no extra field bytes and
  no use of callable `DirectExpectation` rows for data.
* Reject private transport/native promotion requiring those missing data
  carriers. Do not encode data as a zero-argument function, use convention
  zero/Unmanaged as “data,” or synthesize an accessor/thunk.
* Keep the logical resolution value independent of callable representation.
  A later complete field codec must carry that same value on its declaration
  and every original expected import, with its exact owner/type/mutability,
  provider/key and foreign descriptor. If it chooses declaration Contract
  tokens, the resolution grammar above is reusable **without**
  NativeImportCallConv; that is not an allocation of that field codec here.
* The already assigned standard NativeImport field row is independent of
  this private gate. Do not forbid accepted data syntax or remove its
  standard mapping because private transport is incomplete.

## Standard/private separation

The assigned standard NativeImport row remains exactly:

```text
offset 0:  u8 ownerKind          // 1 MethodDef, 2 FieldDef
offset 1:  u8 resolutionMode     // 1 Link, 2 Dynamic
offset 2:  u8 resolutionPolicy   // 0 only for Link; Dynamic 1 or 2
offset 3:  u8 callConv           // Callable 1..4; data 0
offset 4:  u32 ownerRef
offset 8:  u32 libraryName
offset 12: u32 entryName         // 0 means exact declaration name
```

Its extent is 16 bytes. Policy zero denotes absence for Link, **not a third
policy** or source Default; callable zero and data nonzero conventions are
invalid. Owner/ref/name, uniqueness and target requirements remain under
the metadata owner. The private tokens allocate no standard String payload,
Contract clause or shadow NativeImport table.

The provisional four-u32 row in current TrRmNativeImportTable code has
different offsets and semantics. Neither equal row width nor matching
version bytes makes it this row. Any future standard implementation must
select the exact assigned schema and explicitly map normalized facts;
this private proposal supplies no guessed decoder, compatibility reader or
old-policy-to-Default conversion.

## Propagation, replacement and re-encoding

| Boundary | Required integration |
|---|---|
| Binding and mandatory verification | Normalize from the checked registered source schema; carry a complete resolution value in definitions and retained expectations. Validate conditional policy applicability even on uncalled imports. An unsafe declaration propagates caller-unsafety but does not authorize unsafe operations in its body. |
| Shared traversal/rewriting and reference projection | Preserve the immutable normalized fact when cloning/adapting/remapping a direct descriptor. Retain both tokens when Function is cleared; do not infer them from outputs, fnptr parameters, current defaults or a provider replacement. |
| Bare semantic linking/JIT | Compare original mode/policy and convention in the exact signature portion before proxy substitution. Preserve them in restored state, including RecordedImportPromises paths. Require every other contract fact needed for the selected use; the full-native-descriptor gate remains as stated above. No unbound module proxy becomes a source-owned direct import. |
| Single-provider/set AOT | Capture original descriptors before erasure; join outer rows to inner facts; validate selected providers and actual images before affected loading. Keeping a stored PE does not waive new resolution support or expectation checks. |
| Graph roots/intermediates/leaves and diamonds | Check every edge and every using context, including semantic-only and declaration-only dependencies. Keep exact identities/origins/cleanup closure and all original expectations. Equal foreign targets do not merge providers or resolve existing ambiguities/path collisions. |
| Snapshots/caches | Retain exact draft identity, original Contract tokens, typed facts, outer descriptors and the complete dependency closure together. Reuse validation only for unchanged artifacts/schema/bindings/target/rule context. Old-97 or replacement-derived parsed expectations are not reusable. |
| Root-only graph JIT | Validate the entire selected closure, providers/images and resolution mappings first; restore original facts and re-lower only the root. Keep providers precompiled and the same provider route as AOT. Do not resolve a provider's foreign import using root defaults, rebuild providers or choose a fallback JIT path. |

Tokens and mode/policy identities are not indexes. Re-encoding must still
operate on decoded facts and complete using contexts:

1. Remap structures, pointers, fnptr signatures, nominal owners/members,
   formal ordinals, native-input/output/reservation pointees, dependency
   paths, copy origins and typed call targets in their own namespaces.
2. Rebuild fingerprints from remapped complete signatures, retaining the
   same normalized resolution. Keep every original expectation when an
   Import is replaced by a direct reference or a physical row is removed.
3. Rebuild local-name/provider/key joins. Outer native-input pointees belong
   to the **importing embedded Contract's** final type universe, not raw
   provider indexes. Do not compare or substitute coincident numeric IDs.
4. Rebuild affected entry indexes, source spans/maps, lengths, offsets and
   checksums; do not patch token octets or reuse verifier caches. Regenerate
   affected machine images from supported complete facts when required.

Resolution does not change outputs' result partitions, reservation timing,
normal publication, cancellation, exceptional indeterminacy, backing/source
dependencies, ownership or cleanup. It adds no pin/root/retain operation,
resource adoption, foreign unwind permission or NoThrow promise. A returned
pointer or function pointer does not carry a replacement loader policy and
cannot erase actual code/backing lifetime obligations.

Producers must publish valid, truthful facts and code implementing them.
Consumers check exact schema/framing, bounded extents/references, role
consistency, required support and every retained binding before affected use.
These checks do not semantically re-prove arbitrary producer bodies or
foreign libraries. A checksum, export name, successful load or a falsely
labelled native image proves neither resolution truth nor ABI/output truth.
Optional audits remain separate.

## Alternatives and remaining inputs

| Carrier alternative | Decision |
|---|---|
| Add mode/policy only inside the existing DirectNativeImport Function payload | Incomplete: declaration-only imports lose the expectation. Not selected. |
| **Declaration resolution token beside the existing convention token** | Selected: complete for the new facts across owned/imported callable declarations, with no duplicated authority or outer byte change. Static-data transport is explicitly separate. |
| Add declaration token plus Function and/or outer mode/policy fields | Redundant; requires consistency rules for repeated facts without repairing an additional loss. Not selected. |
| Move the complete foreign descriptor into a declaration-owned native-binding variant, applicable to callable and field owners | Substantive coherent alternative. It can repair the existing bare-semantic descriptor loss and redesign/retire outer duplication, but needs complete field/declaration ownership, all projection/restoration paths and explicit outer-schema changes. Not selected as an incidental resolution amendment. |
| Add a new shared native-binding table for all declarations | Also viable with exact owner kinds/references and complete field gates. Adds a reference namespace/count/table for facts the existing callable Contract can retain. Not needed for this bounded decision. |

No carrier, normalized value, token placement, extent or revision choice is
left open here. The following **inputs and implementation gates remain**:

* Authorized Core authority, exact schema/enum provider Module identities,
  export keys/kinds and complete retained expectations from the source/Core
  candidates. Do not guess a production Core provider. Already normalized
  source-free private decoding needs no fresh source/Core registration.
* The selected native target/rule context and real Link implementation:
  configured linker, admitted static/shared inputs, symbol/result mapping,
  deployment dependencies and evidence satisfying the existing provider
  image/leaf contract. A library string alone does not provide those inputs.
* An actual Dynamic loader implementation for each supported target/policy,
  with exact lookup, symbol binding, failure, module/image retention and
  image/runtime validation. This proposal assigns no helper ABI, mutable
  cell layout or loader-initialization record.
* Exact selected semantic provider P/key K, the original expected foreign
  descriptor, actual provider export/image and foreign library availability.
  The package layer cannot obtain a missing expectation from the provider it
  is supposed to check.
* Complete 93–96 prerequisites, full 97 convention integration, source
  normalization, shared rewriting/verifying, all reader/writer allowlists,
  snapshot identity and provider joins. In-memory enums or passing old
  SystemDirectory samples are not those implementation gates.
* A separately selected private static-data declaration/provider/address
  transport, and independently supported source/Core/general declaration
  codecs where required. No placeholder field or callable adaptation fills
  that gap.

Formal adoption of this proposal and its unapproved prerequisites remains
outstanding. Target implementation/qualification and broader roadmap
acceptance are separate from completing this design decision.

## Required acceptance cases

These are **future tests**, not results from this documentation task. Extend
the existing public compiler API test homes; use independent exact byte
fixtures as well as round trips. Keep source/record admission, image emission,
actual ABI behavior and supervised execution evidence distinct.

| Family | Required positive, negative and boundary observations |
|---|---|
| Source normalization | Omitted/explicit Link without policy, Dynamic with omitted/explicit Default and Dynamic/CompatibilitySearch normalize exactly. Reject every explicit Link policy, including Default, wrong Core/enum owners, raw integers, zero/unknown values, duplicate labels, positional resolution arguments and same-shaped unrelated enums. Checked typed forwarding and exact aliases work under the source owner. |
| Omission versus persisted presence | Omitted Dynamic policy and explicit Default produce the same token; omitted mode and explicit Link produce the same Link token with no policy operand. Remove either required direct token from an owned or bodyless/uncalled import and reject; no reader default. |
| Callable combinations | Round-trip all three resolutions with all four conventions, zero/nonzero native inputs and no/one/multiple outputs. Known Vectorcall can retain its identity but must decline unsupported native mapping. NativeCallable/foreign/fnptr roles keep their independent carriers and receive no import resolution. |
| Exact bytes and framing | Independently encode all three tokens with exact 28/39/51-byte growth, unchanged Function descriptor extents and unchanged outer rows. Confirm the i32 inner String versus u16 outer String distinction, mandatory zero counts, tail field alignment and full composed-97 prerequisites, not only a same-writer/read-back pass. |
| Token malformed input | Reject duplicate/equal/conflicting, misplaced/reordered, truncated, wrong-case, extra-component, numeric, whitespace/escaped and unknown tokens; Link:Default/0/NotApplicable and policy-less/zero/unknown Dynamic. Reject payload/token disagreement and conflicting native roles without substring-based false positives in ordinary names. |
| Bounds | Test zero/max/over-limit counts, strings exactly at and beyond the expanded 1,024-byte Contract bound, native name limits, total 4 MiB/outer limits, malformed UTF-8, negative/overflowing/truncated lengths, 14*n input extents, wrong formal/type namespaces and invalid Boolean/enum/access/retention/extent. Preserve shared depth/work and independent call-argument limits. |
| Source-free retention | Remove source/debug and imported Function payloads. References, sets, graph intermediates and snapshots retain original mode/policy/convention. Missing original library/export/input expectations block native promotion rather than being recovered from a replacement. |
| Outer joins | Require every imported direct callable's row, including unused imports with no IAT use; reject missing/duplicate/orphan/unsorted rows, wrong provider/key, rows for source-owned/ordinary/foreign/data declarations and a direct row lacking either inner token. Recompute checksums on mutations to reach the intended validation gate. |
| Replacement | Unchanged resolution permits compatible unrelated body/image changes and physical table reorder. Reject changing only Link/Dynamic, Default/CompatibilitySearch, native role or convention even with equal x64 carriers, DLL/export or Linux loader behavior. Preserve all independent using expectations; no last/weakest-wins merge. |
| Sets/graphs/snapshots | Root/intermediate/leaf direct providers, diamonds, declaration-only and semantic-only dependencies retain original expectations and cleanup/origin closure. Reject stale schema/context/provider snapshots. AOT and root-only JIT use the same selected providers; JIT does not rebuild providers or resolve their foreign imports under root defaults. |
| Remapping and packing | Reorder/remap structures, pointers, nested fnptrs, nominal foreign members, functions and formal/path references while preserving identity, output/dependency/copy-origin contracts and resolution. Rebuild fingerprints, joins, lengths/maps/checksums. Resolution tokens are not references or byte-substitution targets. |
| Link target boundary | Exercise explicitly configured shared/static linker inputs only where their mapping is supported. A SystemDirectory route relabelled Link rejects; a static result without the selected bridge's required IAT/leaf is not admitted. No runtime policy is manufactured for Link. |
| Dynamic target boundary | In isolated supported target fixtures, test Default's actual documented directory/flag behavior versus CompatibilitySearch, unavailable library/symbol and no fallback. Respect loaded-module behavior. Linux tests, if a Linux backend is independently selected, preserve distinct identities despite common bare-name dlopen behavior. Unsupported mappings decline rather than claim success. |
| Dynamic lifetime and addresses | Retain the foreign library through the importing module/image, including imported-data addresses and independently admitted callable addresses. No per-pointer unload lease or early unload after symbol resolution. Provider/root lifetime and foreign lifetime are separate retained obligations; existing excluded module-selected address/entry paths still reject. |
| Data and mutability | Source normalization and the assigned standard field row cover all three resolution alternatives with callConv zero. Reject explicit field CallConv, instance/initialized imports and wrong owner kind. Preserve read-only-to-mutable native binding and reject writable-to-read-only binding. Private data transport/promotion rejects until its separate codec exists; no function/accessor/thunk substitution. |
| Standard/private separation | Independently test the assigned 16-byte row's owner/mode/policy/convention offsets and callable/data domains. Same-sized prototype four-u32/System32 bytes do not become that schema. Private tokens are not standard clauses and old policy 1 is not an inferred Dynamic/Default mapping. |
| Exact schema selection | Reject 92–96/other unsupported inner revisions, unselected 98, old/unknown outers and inconsistent nested closure/snapshots. Prior-97 artifacts are not aliases; a structurally identical no-import stream still requires exact out-of-band agreement. No header-only upgrade, guessed/historical decoder or promise to detect all relabelled provenance. |
| Provider leaf and actual image | Retain the selected P/K route, real six-byte export, exact rooted foreign IAT and unwind/image checks. With valid metadata/checksums, reject wrong export/IAT/target/extent/forwarder or missing required imports. A Dynamic token cannot turn a static IAT into an unreviewed runtime cell. Metadata validity alone cannot prove a lying provider's lookup/code truth. |
| Cross-feature safety | Preserve native-output success/complement results, cancellation before Invoke, exceptional indeterminacy, source/backing dependencies, general dependencies, copy-origin summaries and cleanup on every outcome. Unsafe declarations do not authorize body operations; resolution supplies no marshalling, initialization, ownership transfer, pin/root or foreign-unwind permission. |

Native execution tests must be headless, deadline-supervised and tied to the
final compiler/configuration/input identity. Keep documented fixture,
loader, platform and hardware opt-ins; do not install test DLLs into
SystemDirectory or weaken gates to obtain a pass. Inspect active selections
and distinguish pass, skip, refusal, failure and incomplete execution.
This proposal claims none of those outcomes, compiler support, standard TRRM
conformance or completion of the active implementation milestone.
