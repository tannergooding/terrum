# RootPin runtime provider deployment

**Status: unapproved; provisional direction under the user's authorized
missing-contract workflow.** This recommendation does not amend normative
specifications, approve Profile `(2,1)`, or establish implementation support.
It completes only the physical provider-selection boundary of the
[RootPin runtime candidate](reference-root-pin-runtime.md#candidate-provider-discovery-and-domain-sharing).

## Decision and scope

Recommend **one host-selected, fully qualified ASCII provider path P,
encoded directly in every affected AOT PE import descriptor, with JIT
loading that same P**. Ordinary PE IAT binding, checked actual module identity
and process-lifetime retention supply one provider/domain across both routes.
No separate provider-path setting, private provider-address slots or
provider-attachment transport is required.

This is deliberately a **deployment-path-bound** candidate, not an
installation-location-independent native image format. It selects absolute
PE imports rather than the late-binding alternative below; it does not
reclassify a basename import as an absolute import. Matching trusted preloads
may be admitted: the APIs used below do not report whether a mapping was
newly loaded or previously "owned" by a RootPin binding.

The provider role remains `terrum-root-pin-runtime.dll`; its required exports
for this combined candidate are exactly:

* `TerrumRootPinGetRuntimeV1`, with the unchanged 112-byte runtime info and
  104-byte/twelve-entry operation table.
* [Loaded-type admission V2](reference-root-pin-array-layout-descriptors.md#exact-supplementary-abi-amendment),
  `TerrumRootPinAdmitLoadedTypesV2`, not the superseded V1 interface.
* [Loaded-method admission V1](reference-root-pin-loaded-method-descriptors.md#exact-supplementary-interface),
  `TerrumRootPinAdmitLoadedMethodsV1`.

No source declaration, NativeImport setting, Core API, TRRM record, Feature,
TargetProfile revision, private codec or new provider export is introduced.
The [exact B + RootPin schema](trrm-root-pin-schema-selection.md) and existing
acquisition, descriptor, construction and nonforeign-call gates remain
unchanged. This decision does not select a general native-package resolver,
public startup API, installer directory, authentication scheme or sandbox.

## Why the contract is missing

| Existing owner/evidence | Established fact; missing decision |
|---|---|
| [Runtime discovery candidate](reference-root-pin-runtime.md#candidate-provider-discovery-and-domain-sharing) | Assigns a DLL/export candidate and requires the same provider/domain for AOT and JIT. It explicitly requires deployment/runtime adoption but supplies no physical locator or loading sequence. |
| [Loaded-type authority](reference-root-pin-loaded-type-descriptors.md#identity-inputs-and-the-loader-authority), [loaded-method identity](reference-root-pin-loaded-method-descriptors.md#loader-owned-method-identity) | One loader for D owns canonical identities; supplementary exports must come from the same instance. Neither determines where that instance is loaded from. |
| [Windows deployment §§4–5](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Requires exact selected closure, origins and lifetime, separates candidate search, and requires an admitted launch adapter. It deliberately selects no resolver algorithm or default launch surface. |
| [Native binding](../specs/native.md#binding-and-image-admission), [JIT admission](../specs/jit.md#admitted-execution) | Successful DLL loading, export spelling and equal layout do not establish admitted identity or runtime support. |
| [PE import directory](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format#import-directory-table), [load-time linking](https://learn.microsoft.com/en-us/windows/win32/dlls/load-time-dynamic-linking) | `IMAGE_IMPORT_DESCRIPTOR.Name` is an RVA to an ASCII DLL-name string; Windows loads the declared dependencies and fills the IAT before ordinary startup. The format does not require the writer to strip a supplied path. |
| [`NativeEmitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), [`PeImage`](../../sources/Terrum.CodeAnalysis/Native/PeImage.cs) | `QualifyDirectNativeImports` replaces `DllName` with `System32ImportPath`; `BuildImports` writes that complete string through `WriteAscii`, and `ImportTable.Relocate` places its RVA in the descriptor's Name field. Absolute PE imports already have a source-level implementation mechanism; RootPin still needs its separate internal runtime-role selection. |
| [`NativeModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativeModuleTests.cs) | `StandardTrRmSystemDirectoryImportRemainsQualifiedInLibraryEmission` asserts that the absolute System32 path survives library emission. This is emission-test source evidence, not a fresh RootPin execution result. |
| [`NativeJit`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs), [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs) | JIT loads `NativeJitModule.FullPath`; graph JIT constructs those paths from selected graph images and direct imports. These paths do not select an internal RootPin provider. |
| [Provider source](../../sources/Terrum.RootPin.Runtime/RootPinLoadedTypesV2.cpp) | GetRuntimeV1 returns static runtime info and an image-local singleton domain. Loading another copy can produce another domain; that implementation is not a process-wide selection protocol. |

This is a missing deployment contract, not an already selected algorithm
awaiting implementation. RootPin is neither an authored NativeImport nor an
ordinary selected graph provider under the existing candidate.

## Owners and intended amendments

On adoption, integrate these changes together; this document makes none now.

| Owner | Exact proposed integration |
|---|---|
| [Windows deployment §4](../specs/targets/windows-x64-deployment.md#4-selected-closure-origin-and-replacement) | Add a RootPin-specific runtime-role edge, distinct from semantic Module/native-package providers. Require all Profile `(2,1)` attachments to use the single binding below. Preserve complete selected-closure and original-origin checks. |
| [Windows deployment §5](../specs/targets/windows-x64-deployment.md#5-invocation-and-process-launch-boundary) | Require the selected native deployment to supply P at AOT finalization or JIT execution selection, and complete runtime admission before affected initialization/invocation. AOT takes P from its finalized imports, not a second launch-time path setting. |
| [Windows deployment §3](../specs/targets/windows-x64-deployment.md#3-native-image-stack-and-unwind-support) | Add a new §6, “RootPin runtime-provider binding”, owning absolute import names, actual H/domain checks and process-lifetime retention; reference it from publication requirements. Use ordinary PE imports/IATs, not an additional provider-slot format. |
| [Runtime candidate: provider discovery](reference-root-pin-runtime.md#candidate-provider-discovery-and-domain-sharing) | Complete its physical library identity as the selected absolute P. Preserve compiler-generated AOT imports and the exact helper ABI; require JIT to resolve the same P and actual H. Adopt neither a late-bound-only format nor a blanket unowned-preload rejection; the runtime's same-provider rule remains unchanged. |
| [Type admission](reference-root-pin-loaded-type-descriptors.md#exact-supplementary-windows-x64-interface), [array amendment](reference-root-pin-array-layout-descriptors.md#exact-supplementary-abi-amendment), [method admission](reference-root-pin-loaded-method-descriptors.md#exact-supplementary-interface) | Reference the canonical process binding for “same provider”. Select their conservative process-lifetime domain/origin branch; no early whole-domain teardown is supplied here. Preserve all records, statuses, semantic identity and admission rules. |

[Native lifetime](../specs/native.md#runtime-and-code-lifetime),
[JIT replacement](../specs/jit.md#replacement-and-code-lifetime), abstract
lifetimes and binding/replacement remain semantic authorities. They need no
replacement rule. Physical file identity never replaces Module/type/method
identity or the producer's truth obligations.

## Selection, assumptions and canonical identity

One trusted **process host authority** selects P when finalizing the native
deployment and owns the existing domain loader's canonical identity maps.
P names the complete local DLL file with leaf `terrum-root-pin-runtime.dll`,
using one agreed drive-qualified Windows path with backslashes, no relative
components and printable ASCII characters. ASCII is a real restriction of
this PE-name candidate; do not encode Unicode through a process code page,
replace characters, truncate, or infer an 8.3 alias. Unsupported paths/capacity
decline. Bare names, drive-relative paths, network/device input paths,
environment expansion and suffix inference are not alternative spellings.

There is **no default** derived from the executable directory, importing
image, compiler/JIT assembly, staging directory, current directory, System32,
`PATH`, registered DLL directories or a package catalog. An installer may
choose an application-local layout, but it must finalize its absolute P
explicitly. Every affected AOT image stores the **same P verbatim** as the
NUL-terminated Name string of its ordinary import descriptor for
`TerrumRootPinGetRuntimeV1`. Supplementary loader imports, when emitted, use
that same P; alternatively resolve those exports from the verified H.

An AOT-led execution takes P from its selected root's finalized runtime import;
a JIT-led execution uses that same selected native-deployment input. Before
admitting another image, compare its encoded P with that selection. Do not
choose a path from the first plugin or maintain a second override/sidecar
provider-path setting. Different encoded aliases are not independently
selectable per-image locators, even when they reach one file. Moving the
provider requires re-finalizing affected native images and re-admitting the
deployment, including applicable image-signing checks; it does not change TRRM.

Assume a trusted deployment whose files, mount/drive mappings and reparse
ancestors stay stable **from the provider's first mapping through admission**.
Thereafter retain the selected file against writes/deletion and forbid
namespace replacement affecting further loads. This includes a prior eager
PE load or trusted host preload, not just the later JIT call. Unknown preload
history cannot acquire that guarantee from a pathname query. Host/native code
must neither introduce another participating RootPin runtime nor perform
earlier RootPin use outside the one domain-loader authority. These are host
truth obligations, not sandbox guarantees against arbitrary native code.

The admitted process binding retains:

* P, selected file identity F and file/closure holds;
* the actual loaded module H and its load instance;
* the immutable runtime-info identity, D, exact operation table and required
  supplementary export addresses;
* the one domain loader authority and its retained identity/origin state.

Same instance requires **the same retained H/load instance and D under that
authority**, not just P or F. Another mapping of the same file, a copied file
with equal bytes, equal D bits from another H or an equal-shaped table does
not establish identity. P is a locator; F compares opened filesystem objects;
H identifies the retained live mapping. None replaces semantic origin or
authorizes a second domain. These are process-local facts, never serialized
runtime authorization.

## Loading and publication

1. Preflight the exact selected native closure, encoded paths and runtime
   requirements under the existing image/admission rules. A deployment/host
   controlling image loading rejects conflicting P values before loading that
   closure; it does not load alternative runtimes to compare them. Preserve
   all private dependency, platform/API-set and supported-load-schedule checks.
   An absolute top-level name does not select its transitive dependencies.
2. AOT uses **ordinary eager PE import resolution**, with P in Name and
   normal OS-filled IAT entries. JIT uses `LoadLibraryExW(P, NULL,
   LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)`.
   Do not claim that AOT's implicit loader uses these JIT flags. Neither route
   substitutes another name/path, changes process search directories or
   treats a search-order result as the selected runtime.
3. For AOT, recover H using
   [`GetModuleHandleExW`](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandleexw)
   with `GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS` and the **actual resolved
   GetRuntimeV1 IAT target**, not a consumer-side import thunk. This acquires
   an owning reference. JIT already owns its LoadLibraryExW reference.
   Check H against any committed binding; a different H rejects, not a new
   loader/domain. A basename GetModuleHandle lookup cannot replace this step.
4. Open P with
   [`CreateFileW`](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew),
   `GENERIC_READ`, `FILE_SHARE_READ`, `OPEN_EXISTING` and ordinary file
   attributes; retain the handle.
   Obtain F with `GetFileInformationByHandleEx(FileIdInfo)` and perform the
   mapped-file consistency check below. Unsupported identity queries, wrong
   mapped file, unreadable/unstable backing or non-image mappings decline.
5. Resolve all three exact exports from H, not from a global name lookup.
   Require nonforwarded exports and operation-table targets in H's admitted
   executable sections. Private helpers behind those entries remain part of
   its retained closure, not independently selectable RootPin providers.
   The actual AOT IAT target must equal H's resolved GetRuntimeV1 export.
   Validate readable/aligned runtime-info extent, exact revision/table size,
   twelve non-null targets and nonzero D under the existing ABI. Repeated
   GetRuntimeV1 observations must preserve the same immutable info, D and table.
   Never call a second provider with D as a compatibility probe.
6. Serialize first admission under the host's one domain-loader authority.
   After all checks, pin the verified H with GetModuleHandleExW using
   `GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN`
   and its verified export address; require the returned H to match.
   The PIN flag guarantees retention until process termination, independently
   of later image unloads/FreeLibrary calls. Commit one binding for the agreed
   runtime/type-V2/method-V1 bundle; later AOT/JIT admission must match it.
   Complete existing type/method admission, generated descriptor binding and
   ordinary code/unwind/cache publication before affected code becomes callable.

The [Windows loader documentation](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw)
describes redirection even for a supplied path; its
[search-order rules](https://learn.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order)
also distinguish dependency lookup from the top-level path.
[Run-time linking](https://learn.microsoft.com/en-us/windows/win32/dlls/run-time-dynamic-linking)
reuses an already mapped DLL and increments its reference count; same basename
in a different directory does not establish the same DLL. These mechanisms
support same-P reuse, but actual H checks remain required. DLL/SxS redirection
must not select an alternative F/H. Native DLL initialization can already run
during implicit or explicit loading; post-load admission is not a promise to
execute no native code.

Runtime admission occurs outside DllMain/the Windows loader lock. Missing file,
identity mismatch, wrong machine, missing/forwarded export, unsupported ABI,
invalid table or failed load is admission failure, never optional absence,
source allocation failure, rootless fallback or permission to try a private
collector. A missing eager dependency can prevent OS process/library loading
before an application diagnostic exists; JIT can instead report its load
failure. Neither is source allocation failure. No affected descriptor admission
or source execution is published on binding failure. A failed initial selection
does not try another P; a later attachment failure leaves a committed binding
unchanged. Release uncommitted resources only when safe, otherwise retain them.

### Mapped-file checks and preload limits

[`GetMappedFileNameW`](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getmappedfilenamew)
at H returns a **name**, normally an NT device path, not an open file handle
or file ID. Grow/check the buffer and never accept truncation. For the selected
local-file scope, reopen the complete returned NT path with CreateFileW by
prefixing `\\?\GLOBALROOT`, as supported by the
[Windows namespace rules](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file#nt-namespaces).
Do not pass `\Device\...` as an ordinary DOS filename. If the reported namespace
cannot be opened as the required local regular file, decline rather than
guessing a drive letter. This derived reopen name is verification evidence,
not another provider locator/configuration channel or a fallback load path.

Use
[`GetFileInformationByHandleEx`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getfileinformationbyhandleex)
with `FileIdInfo` on **both opened file handles**, not on H. Compare the exact
[`FILE_ID_INFO`](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info)
pair `(VolumeSerialNumber, FileId[128])`. Equality establishes the same opened
filesystem object, not equal image contents. Under the stable namespace and
first-mapping assumptions this is an implementable mapped-name/file
consistency check. It is **not** a direct query of the original section's
file-object ID: if a mapped image's pathname was replaced before these opens,
the named APIs do not recover that history. A new read/share-denying handle
also cannot retroactively establish immutability. Do not claim race-free
authentication, an immutable past, or repair unknown history with this check.

Likewise, LoadLibraryExW returns no "fresh versus reused" flag, and neither
GetMappedFileNameW nor FileIdInfo reports host ownership. A blanket
**unowned-preload rejection is not implemented by these APIs and is not
required by this candidate**. A trusted eager PE load or matching host preload
may supply H before the first binding record exists; admit it through the
same P/F/H/ABI checks and host provenance obligations. Unsupported prior
runtime use or unknown stability remains a host-admission gate, not a
detectable ownership bit.

A stronger policy would need coordinated records covering the first load,
all relevant loads/unloads and exclusion of conflicting activity.
[`EnumProcessModulesEx`](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-enumprocessmodulesex)
alone is insufficient: its documented snapshots can be wrong while modules
change and contain no ownership history. That additional protocol is not
silently added here.

### AOT and JIT share P without another provider channel

AOT carries the provider locator in its PE import directory and obtains the
actual function through its ordinary IAT. JIT resolves the same selected P
and validates the same H/D; neither uses the source NativeImport/System32
policy to select RootPin. Every affected set/graph image has the same encoded
P regardless of graph position or forwarding directory.

The existing loaded-type/method proposals still require **one canonical
domain loader and their identity/admission handoff**; absolute PE imports do
not create descriptors or remove that requirement. They remove only the
additional private provider-path/address-slot/attachment channel. This proposal
does not assign a public startup API or portable descriptor-binding format.
Loading an AOT DLL is not semantic admission: RootPin-dependent initializers,
exports and JIT entries remain unavailable until the existing checks complete.
DllMain/TLS callbacks cannot perform RootPin-dependent source execution.

## Lifetime and consumer consequences

Retain the provider load reference, selected file/closure holds, singleton D,
domain loader maps, admitted descriptors and their required origin leases
until **process reclamation**. Per-image unload, JIT disposal, an empty IAT,
the last return or the last pin does not release that binding. There is no
provider unload/reload, second domain, version roll-forward or early
whole-domain shutdown in this candidate; adding one needs a separate exact
quiescence/lifetime amendment. Installation updates take effect in a new
process, not by retargeting a live binding. The held file cannot be replaced
in place while this process retains it; use a distinct P with newly finalized
images or wait for the retaining processes to end.

Original code, callback, allocator, retirement and unwind backing remain
retained under the existing type/method origin rules, including after an
intermediate attachment disappears. Failed unwind deregistration never
authorizes freeing registered backing. Process exit promises no additional
source Cleanup. Retaining metadata/provider code does not root source objects,
keep PinCells active or extend loans, guards or ordinary support endpoints.

Semantic consumers still select the exact schema, admit original expectations
and verify required support. Provider loading does not grant source authority,
prove native body truth or supply missing construction/persistence contracts.
Standard/private semantics never serialize P, F, H, D, trusted loader keys or
live descriptor pointers as runtime authorization. P belongs in the **native
PE import name**, not TRRM or another semantic provider-selection record.
Persisted native images carry ordinary imports and existing relocatable
descriptor requirements, never a previously admitted process binding.

## Alternatives and recommendation

| Candidate | Tradeoff |
|---|---|
| **Host-selected absolute P directly in AOT import descriptors; JIT uses P; recommended** | Reuses the existing PE Name/IAT mechanism and preserves the runtime candidate's compiler-generated imports. One native-deployment path supplies both routes, with checked H/D and process-lifetime pinning. Costs an ASCII, installation-path-bound AOT image and early OS loading/failure; relocation requires re-finalization. No second provider-path setting or new provider attachment format is necessary. |
| Explicit launch-time P with private late-bound provider slots | Materially different and coherent for relocatable images or Unicode-only locations. The host can select P after emission, but must define and implement provider-address fixups and host/image attachment transport. These are additional contracts, not prerequisites forced by PE semantics. Keep this alternative separate; no automatic fallback between the two layouts is selected. |
| Always beside the process executable | Simple deterministic layout if adopted explicitly, but couples JIT/embedded applications to their host executable installation. It is not necessarily the program or runtime directory. Application-local deployment remains possible by explicitly supplying P. |
| Eager **basename** PE import, with preload/search-directory setup | Different from an absolute Name string: it allows lookup/reuse/search state to supply the provider locator. An absolute preload alone does not turn every later basename import into a checked absolute dependency. Rejected; no basename/search-order selection is authorized. |
| Make RootPin an ordinary graph/Module provider | Material redesign: assign a semantic provider role, retained expectations and representation for runtime discovery/bootstrap in every route. Coherent if deliberately selected, but no such Module edge exists now and it still needs process-wide domain/lifetime rules. |
| Inject only an arbitrary host table/D, or statically link a runtime per image | Table-only injection requires a different trusted provider-origin/lease handoff; equal-shaped tables cannot prove instance identity. Per-image static runtimes violate the selected singleton. A single statically hosted runtime could be a separate replacement ABI, not a fallback for this DLL candidate. |

Absent a requirement for installation-independent AOT images or Unicode
provider paths, absolute PE imports are the smaller coherent choice. The
source mechanisms and Windows API contracts support that choice; they are
not RootPin compiler/runtime conformance evidence. Formal owner adoption,
runtime-role path selection, startup identity/lifetime checks, the existing
descriptor-loader integration and cross-route qualification remain gates.
No compiler/provider implementation is authorized by this document.

## Focused conformance cases

These are proposed required outcomes, **not executed tests or current support
claims**. Runtime cases assume independently admitted object/call boundaries;
they do not establish general GC construction.

| Case | Required outcome |
|---|---|
| RPD-01 Absolute PE carrier | Inspect every affected AOT image: Name contains the selected full ASCII P, not its basename, a second record or a private runtime slot. Load from a non-default directory and verify the actual GetRuntimeV1 IAT target/H. Changing current directory, PATH, importing-image or graph staging locations does not change selection. |
| RPD-02 No discovery fallback | AOT and JIT use the same P. An absent absolute path rejects even with another same-basename provider already loaded or available through search. Preserve OS eager-load failure versus host JIT diagnostic distinctions; neither becomes source allocation failure. |
| RPD-03 Path agreement | Reject bare/relative/drive-relative/network/device/Unicode paths, embedded NUL, truncation and conflicting encoded P. Filesystem aliases may establish equal F, but do not authorize a different per-image P. A copied file with equal bytes and different F is not the selected instance. |
| RPD-04 Cross-route identity | Exercise AOT-before-JIT and JIT-before-AOT, repeated loads and a forwarded/diamond closure. Require the same retained H/D and canonical type descriptors. Reject a second H/domain even for equal bytes/table shape; distinct JIT/AOT entry instances still retain distinct LoadedMethod identities. |
| RPD-05 Mapped-file recipe | Query a complete mapped NT name, reopen it through GLOBALROOT and compare FileIdInfo on file handles. Exercise buffer growth, unsupported query/reopen, different F and DLL/SxS redirection. Never pass H as a file handle or claim that a reopened pathname detects replacement predating the host's stability guarantee. |
| RPD-06 Preload limits | Accept a trusted matching eager/preloaded H through ordinary checks even before a binding record exists. Unknown prior stability/use fails host admission, not a fictional Windows ownership flag. A racy module snapshot cannot prove freshness or ownership. Native DLL initialization may already have run. |
| RPD-07 Exact interface | Reject wrong machine, truncated/misaligned info, wrong revision/extent, null D/target, forwarded exports, entry targets outside H, missing type V2 or missing method V1. Do not try type V1, wider tables, another P or a private collector. |
| RPD-08 Publication timing | AOT uses ordinary OS-filled IATs; before affected initialization/invocation, perform the same identity/ABI/descriptor admission as JIT. No new provider-address slots/handoff are required. RootPin-dependent DllMain/TLS source execution remains unsupported. |
| RPD-09 Concurrency and failure | Concurrent admissions share the preselected P and one canonical loader/binding. Reject conflicting native paths at controlled deployment/load preflight. Failed first/later admission publishes no affected descriptors/code and never replaces an existing binding or selects another runtime. |
| RPD-10 Retained origins | Drop intermediate AOT/JIT attachments while a descriptor, forwarded object or deferred Cleanup retains leaf code/helper/unwind/allocator origins. The actual origins and H remain usable; last-pin retirement still unpins promptly. Per-invocation disposal cannot unload the provider. |
| RPD-11 Replacement/process boundary | Pin H independently of AOT-image and JIT load-reference disposal. Refuse live replacement or unload/rebootstrap. Moving P requires re-finalizing every affected AOT import for a new admitted deployment, not a runtime override. Fresh processes create fresh bindings/keys; saved pointers/file IDs never authorize reuse. |
| RPD-12 Scope and schemas | Exact RootPin schema/runtime agreement and all original binding checks remain required. Ordinary NativeImport/System32 and graph-provider behavior is unchanged; provider presence enables no missing source, allocation, codec or F/G composition. |
