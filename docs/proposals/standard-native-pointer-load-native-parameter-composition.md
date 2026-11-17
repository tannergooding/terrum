# Standard pointer-load and NativeParameter composition

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** The recommendation below is provisional direction
for subsequent implementation, not normative adoption, implemented support or
qualification evidence. It changes no existing proposal or normative owner.

## Missing composition and recommendation

Select two exact successors of the [P pointer-load profiles](standard-native-pointer-load.md),
adding the existing candidate **NativeParameter `000E` and
`16/1=ForeignInvocationContracts`** for direct NativeImport declarations and
their invocations. Call this bounded selection **F16**. Preserve its complete
foreign obligations; do not remove the annotation, accept a private carrier,
or weaken consumer checks to make an ordering test emit.

F16 selects the direct-NativeParameter part of the
[foreign-invocation candidate](standard-native-import-function-pointer-contracts.md#candidate-nativeparameter-row),
not the full **F** bundle in the
[schema-composition owner](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities).
It adds no foreign-protocol function-pointer projection, NativeAddress E9,
raw-output DF reservation, storage-address EC, or raw OutputState applicability.
Feature 16 keeps its existing candidate identity, revision, prerequisites and
semantics; this is not a weaker revision or a claim to support every F use.
Other F uses need their independently selected complete carriers.

| Integration surface | Selected boundary |
|---|---|
| [`TrRmArtifactIndex`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs), P and G+P selectors | Add distinct whole-schema selectors for F16; do not make `UsesForeignInvocationContracts()` true for them, because that flag admits E9/DF and Feature 8. |
| [`TrRmContractTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs), case `0x0E`; [`TrRmFeatureTableReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs) | Admit the unchanged 20-byte Contract `000E` row and effective Feature 16/1 through a narrow direct-NativeParameter capability, while retaining Feature 8 and anchor applicability outside F16. |
| [`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), `EmitDevelopment` / `NativeParameterPayload`; [`Contract writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableWriter.cs) | Emit the normalized 12-byte `000E` payload only under F16; reject NativeParameter under P-only identities and retain legacy `0011` only on its Development route. |
| [`Native-output emitter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeOutput.cs), imported NativeParameter loop | Reuse its normalized `000E` payload mechanism without selecting the NativeOutput protocol. |
| [`TrRmRegisterBodyMapValidator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs), general CallSite validation | Preserve `contracts.GetNativeParameters` and `MatchesNativeParameterArgumentType` checks, including exclusions from initialization/raw-output inputs. |
| [`Standard NativeImport reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeImportTable.cs), `ValidateMethod`; [`structural validator`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) | Keep bodylessness, receiver/generic restrictions, published caller-unsafety, signature/layout validity, clause ownership and independent feature requirements. |

## Exact successors and selected contents

The recipe names are explanatory notation, not flags or a composition
algorithm:

| Component | Fixed selected owner |
|---|---|
| B | Assigned Standard interchange schema, catalog and governing chapters at [the registry](../specs/representation/registry.md#1-authority-and-schema-identity). This does not select the additional LQ source/provider/query-Body profile. |
| I | Complete [initialization-destination Body selection](standard-trrm-initialization-destination-callsite.md), including its bounded predicate companions, preparation, publication and provenance obligations. |
| P | Complete [typed pointer-load delta](standard-native-pointer-load.md), unchanged. |
| G | Complete existing [B+I+G composition](standard-trrm-initialization-predicate-composition.md), its checked-selector extension and whole G catalog. Retain the [failure-identity](standard-trrm-initialization-predicate-failure-identity.md) and [construction-binding](standard-trrm-initialization-predicate-postcondition-construction.md) accompanying context wherever required. |
| F16 | The unchanged candidate `000E` row, direct-import applicability and invocation/binding/preservation rules below, plus the existing candidate `16/1 -> [(1,1)]` registration. No other F allocation or applicability amendment is selected. |

Recommend these **case-sensitive, ordinal out-of-band identities** and
distinct proposed `TrRmArtifactSchema` selections:

| Exact candidate identity | Proposed selector | Complete schema |
|---|---|---|
| `trrm/1-draft-init-reference-bodies-pointer-loads-native-parameter-contracts-1` | `InitializationReferencePointerLoadNativeParameterDraft` | B+I+P+F16 |
| `trrm/1-draft-init-reference-bodies-general-predicate-graphs-pointer-loads-native-parameter-contracts-1` | `InitializationReferenceGeneralPredicatePointerLoadNativeParameterDraft` | B+I+G+P+F16 |

The G successor is justified because P already selects an exact B+I+G+P
parent. `000E`/16 does not collide with G's graph clauses/sections or 17.
Preserving that parent permits genuine G contracts on eligible wrappers beside
the foreign call; it does not extend G applicability to NativeParameter
anchors or raw outputs. The two catalogs remain distinct even when a Module
contains only common-subset content.

Each row means its parent's complete agreement, amended only by F16.
Revision 1 pins these contents, not future moving text at linked paths.
Keep format `(1,0)`, codec `01`, register Body implementation-kind `01`,
framing, map/record widths and Module governing edition `1`. There is no new
wire discriminator, opcode, Body record, TypeSpec, Feature ID or revision.
`000E` and 16 remain unapproved candidates, not newly accepted assignments.

Resolve selection before schema-dependent indexing, writing, binding or
lowering. Carry the complete selection through Contract/Feature/NativeImport
tables, Bodies, references, caches and native handoff. Reusing B/G decoders
must not reset F16 or P, nor enable the whole F bundle through a broad
`UsesForeignInvocationContracts()` check: that check currently also gates
DF/E9, raw-output applicability and Feature 8. Reusing that flag alone also
does not convert `0011`: these successors require truthful `000E` production
and effective Feature 16/1 together with the correct P or G+P selection.

All old B+I, B+I+G, P, F and F+G identities and API/CLI defaults are unchanged.
Neither new selector is an alias or automatic upgrade. Equal version bytes,
Feature lists, filenames, plausible operands, decoder failure or a native
container select nothing. Unsupported/unknown selections fail before the
affected operation publishes output; do not retry Standard, F or Development.
Common-subset bytes may be valid under an explicitly selected successor;
bytes cannot authenticate their originating draft.

For this bounded loading/reference/re-emission operation, root and every
supplied provider use **the same selected successor row**, preserving P/I's
uniform-selection boundary. A provider needing neither P nor `000E` may be
explicitly re-emitted under it. This adds no mixed-schema loading policy.
One packed artifact has one schema, never one decoder per Module.

## Unchanged NativeParameter and direct-call contract

Reuse the foreign owner's exact little-endian **20-byte total** row:

| Offset | Field and admitted domain |
|---:|---|
| 0 | `u32 ownerRow`: MethodDef row `1..00FF_FFFF`, reserved-zero high byte; not a DefId |
| 4 | `u16 clauseKind=000E`, `u16 flags=0` |
| 8 | `u32 formalOrdinal`: zero-based explicit formal in that owner's MethodSig |
| 12 | `u8 access`: `1=Read`, `2=Write`, `3=ReadWrite` |
| 13 | `u8 retention=1`: NoEscape |
| 14 | `u8 extentKind`: `0=NoStaticMinimum`, `1=MinimumElementCount`; `u8 reserved=0` |
| 16 | `u32 extent`: zero for kind 0, `1..FFFF_FFFF` for kind 1 |

The occurrence key is `(owner,000E,formalOrdinal)`; equal duplicates reject.
The row belongs exclusively to that MethodDef's valid contiguous Contract
range. Absence remains absence, not Read, NoEscape or a zero-minimum promise.
Contract `000E` is not TypeSpec `0E` initialization-reference identity or
Body record `0E` storage-address selection. For the regression's `destination`
and `source` formals, retain the normalized tuples
`(formalOrdinal,access,retention,extentKind,extent)=(1,2,1,1,1)` and
`(2,1,1,1,1)`, respectively.

In these successors, admit `000E` on an eligible bodyless callable NativeImport
or its bodyless retained expected declaration resolving to such a provider.
The expectation retains its own NativeImport descriptor and `000E` facts;
it does not become an independently resolved foreign entry. A Body, TypeDef,
ordinary wrapper or signature anchor cannot own this direct-only contract.
F's additional anchor applicability is not selected here.

The formal uses Param mode `00`, zero bounded Param flags and an exact
nonoptional `ptr<T>` or `write ptr<T>`. Write/ReadWrite requires writable
immediate pointee access. Preserve complete nested types, nominal origins and
qualifications; derive element stride/alignment from T and the selected target,
not pointer width or a private alignment field. Typed T needs an independently
admitted fixed directly interoperable region representation. Opaque
`ptr<void>` remains only Read, kind 0/extent 0, with no inferred typed access.
Reject unknown access/retention, overflow and unsupported representations,
not normalize them into the bounded descriptor.

Retain NativeImport section `0021`'s assigned **16-byte row**, MethodDef owner
kind `1`, typed owner/String references, conventions `1..4`, and resolution
tuples `(mode,policy)=(1,0),(2,1),(2,2)`. Preserve original library/entry,
effective defaults, Link/Dynamic policy, target mapping and binding lifetime.
The actual declaration stays static, bodyless, receiver-free and
method-generic-free with published Effects `04` caller-unsafety. No
NativeCallable marker is synthesized, and compatible ABI or method spelling
does not establish the native role.

The direct invocation uses the assigned MethodMap target, including a checked
Import binding, **not a NativeImport-table row index**. General `90`/`E0`
CallSite record `02` keeps target kind `1`, its exact
`20 + 4*inputCount + 2*preparationCount` extent, formal-order inputs and
ordinary result rules. A NativeParameter input selects an already staged,
complete pointer value in R0..R13 or a typed value Home with ordinary
copy/read source-use `1`. It is not a T Home, PlaceHandle, checked descriptor,
source-use `5` init destination or source-use `6` raw-output reservation.
The annotation adds no preparation action or argument-expression recipe.
Assigned compact calls retain their complete-protocol eligibility rules;
they cannot bypass NativeParameter validation.

At every invocation apply the selected declaration's NativeParameter facts
to the actual staged arguments and preserved backing. Read/Write/ReadWrite
needs the declared access, extent, initialization, alignment, validity,
stability and NoEscape through applicable normal/exceptional exits. Write
still requires initialized storage; it is not vacant-output initialization.
Kind 0 proves no capacity; kind 1 counts complete elements, with checked
extent/address arithmetic. Explicit unsafe authorization cannot waive known
violations. No implicit marshalling, pin, root, temporary copy-back, ownership
acquisition or lifetime extension is supplied.

P remains the ordered, nonconsuming exact-T read `1B`, independently unsafe.
Capture the pointer once and perform the read at its actual position. A
saved read before the foreign write must not be deferred until after it;
a later read must not reuse the earlier value. Preserve call evaluation,
effects, logical register/Home snapshots and alias invalidation.
`WriteProcessMemory` supplies its own copy behavior; P acquires no atomic or
synchronization semantics. An unsafe declaration/Effects row authorizes
neither authored read nor call body operations.

NativeParameter-only calls keep ordinary scalar, floating or unit results,
with no fabricated status/output relation. DoesNotReturn retains the full
signature and terminal general CallSite/None rules. Before-entry failure
performs no invocation; post-entry failure supplies no normal result or
rollback guarantee. Preserve earlier effects, outstanding responsibilities
and cleanup on actual exits; no foreign-fault translation is added.
Neither the call nor P publishes checked initialization, adopts a resource
or completes a pointee because its pointer cell is live.
I's genuine checked outputs retain their separate complete protocol.

## Feature closure and admission

Compute least exact closure **per Module**, from all retained declarations,
expectations, Bodies, types and dependencies, including uncalled/unreachable
uses. Another Module's manifest or a producer assertion supplies no missing
requirement.

| Retained use | Exact requirement / immediate prerequisites |
|---|---|
| Every `000E`, and its retained direct invocation facts | `16/1 ForeignInvocationContracts -> [(1,1)]` |
| P or actual raw-pointer value use | `2/1 RootlessNativeValues -> [(1,1)]` |
| Callable NativeImport row or direct native call | `3/1 DirectNativeCalls -> [(1,1),(2,1)]`, preserving the native declaration's own admission rules |
| I's bounded selectors, when retained | `10/1 -> []`, unchanged six-template scope; F16 alone adds no selector |
| Genuine G assertion/general checked selector/read-bound use | `17/1 -> [(10,1)]`; actual fixed-scalar literals additionally `11/1 -> []` in the G schema |
| Actual native export or native unwind use | Independently `4/1 -> [(3,1)]` or `7/1 -> [(1,1)]`; neither follows from a load or NativeParameter alone |
| Actual assigned layout/field-query/binding use | Conditional `13/1` and its complete target/provider obligations, unchanged |

A P Body plus the motivating callable import has effective closure
`{1/1,2/1,3/1,16/1}`. Genuine G use adds 10/17, and literals add 11 where
retained; choosing the G row alone does not force those uses.
Feature 8 is not added or activated by F16, nor are 4/7/10 implied by 16.
Unmentioned parent registrations and conditional requirements are unchanged.
The non-G parent's incomplete `11 -> 9`, `12 -> {10,11}` remain;
the G parent's whole-catalog `11 -> []`, `12 -> {11,17}` remain.
Neither selection completes Feature 9/12 or changes catalogs per Module.

Validate supported schema, clause/table shapes, owners/ranges, references,
formal types and modes, duplicate keys, NativeImport consistency, manifest
closure and complete binding before affected use. Producers own source
legality and truthful foreign/body facts. Under
[reader trust](../specs/representation/reader-trust.md), consumers need not
re-prove provider behavior, arbitrary unsafe promises or authored body truth.
A serialized authorization bit or mandatory foreign-body audit is not the
solution to this composition gap.

## Integration, replacement and round-trip obligations

The proposed owner amendments are bounded: extend P's exact-selection table
with these two rows; select `000E`/16 for them in the registry/catalog and
[Contract applicability](../specs/representation/contract-clauses.md);
cross-reference direct NativeImport and Body call consumption in
[metadata](../specs/il-metadata.md) and [the instruction set](../specs/il-instruction-set.md#calls).
Only these successors replace P's exclusion of foreign parameter contracts.
All underlying P/I/G/F semantics stay with their existing owners; no normative
text or other profile is widened by this document.

* **Producer and shared pipeline:** retain normalized NativeParameter facts,
  complete pointee types, lexical authorization, backing and actual call/read
  order through verification, traversal, rewriting and substitution. Emit
  `000E`'s 12-byte payload and correct per-Module closure, never private
  `0011`/28-byte rows in either successor. Preserve `0011` and its existing
  payload/decoder on the Development route; do not globally retag that route.
  Reuse the existing native-output emitter's normalized `000E` encoding
  without selecting its output protocol. Share generic mechanisms, not
  named-API/test policy.
* **Source-free consumption:** reconstruct the actual DirectNativeImport
  descriptor and parameter vector, checked direct call and ordered P read.
  Retain the existing register-Body NativeParameter argument checks through
  `GetNativeParameters` and `MatchesNativeParameterArgumentType`.
  Keep original expectations separately from provider facts. Reusing a
  NativeAddress caller/anchor loader does not supply this Body route.
  G assertions/selectors and required failure/construction context survive
  unchanged; unsupported context or enforcement declines affected use.
* **Binding and replacement:** resolve each original ModuleRef/ExportKey to
  one actual provider-owned declaration first. Preserve complete normalized
  signature/layout, target/runtime and native descriptor under their existing
  comparison rules. Compare the **entire NativeParameter protocol exactly**
  after formal/owner rebasing, including presence, access, retention,
  extent kind/count and resolved pointee identity. Do not numerically order
  access/counts or drop a seemingly stronger NoEscape guarantee. Ordinary
  effects, I/G guarantees and dependencies retain their directed
  [binding relation](../specs/representation/binding-and-replacement.md).
  All original expectations must pass; provider extras cannot rewrite them.
* **Re-emission and packing:** explicitly decode supported source schemas
  and choose one destination capable of every retained obligation. Rebuild
  MethodDef/Import/Export/ModuleRef identity correspondence, NativeImport
  owner/String references, Contract ranges and offsets, NativeParameter
  formal correspondence, complete nested TypeSpecs, Feature ranges/closure,
  Body maps/records/blocks/code extents and affected SourceMap references.
  Preserve distinct Homes and G's graphs/accompanying binding maps.
  P has no row operand, but its typed source and dependencies still remap.
  Header relabeling, octet substitution and dropped clauses are not conversion.
* **JIT/AOT and lifetime:** use the admitted target ABI and typed P lowering,
  retaining actual foreign/provider bindings, code and backing through all
  required uses. Preserve reads and foreign writes in their required order.
  Structural admission, emission and runtime execution are separate evidence.

No fallback admits compact-indexed Body kind `08`, Development `0010/0011/0012`,
raw-output `sourceUse=6`/DF, E9/NativeAddress, EC/storage addresses,
foreign-protocol anchors, RootPin, C1, generic-call or exception-region
compositions. Required unselected forms reject; they are not encoded by
dropping their obligations, changing pointer depth or disguising a reference.

## Focused acceptance cases

These are **future acceptance obligations**, not passing results.
Use existing public APIs and headless, supervised native qualification with
exact final compiler/configuration/input identities when implementation is ready.

| Case | Required outcome |
|---|---|
| Exact selection | Both named successors emit/index/load/re-emit kind-`01` P Bodies with truthful `000E`. Old P/B+I/B+I+G/default selections still reject the added carrier; adding 16 or retrying F does not repair selection. Unknown/case-changed identities and mixed component/provider contexts reject without output publication. |
| Producer/route separation | Direct `NativeImport.Parameters` emits 20-byte `000E` rows and effective 16/1 in each successor; 28-byte `0011` rows reject even with a foreign-profile flag. The existing Development route retains its legacy `0011` encoding/decoding. Do not remove that route or weaken argument validation. |
| Native ordering | Under both successors, the `WriteProcessMemory` regression loads and re-emits before execution: starting at 42 with a source value of 99, read-before returns 42, read-after returns 99, and each completed call leaves backing 99. Verify actual `1B`, direct CallSites and `000E`; inspect emission separately from running it. |
| General foreign use | Unrelated synthetic NoEscape imports exercise Read, Write and ReadWrite, two annotated formals, staged pointer/call-result inputs and an ordinary floating/unit result. Retain once-only evaluation and the same contracts without API-name recognition or invented output statuses. |
| Genuine G composition | The G successor retains an independently eligible wrapper's actual general assertion/checked selector beside P and the import, with 17/10, literal 11 and failure/construction context where required. Preserve phases, output associations and source-free remapping; the non-G row declines G-required content. |
| Cell versus pointee | Combine an admitted I pointer-cell completion with a later P read/native input on independently valid backing. Cell completion grants no pointee initialization/capacity, NativeParameter Write grants no vacancy privilege, and failure supplies no invented output state. |
| Malformed clauses/roles | Reject wrong total extent, flags/reserved bytes, owner/range/reference, duplicate formal key, out-of-range ordinal, Param mode/flags, nonpointer/optional formal, writable-access mismatch, unknown retention and invalid extent tuple. Reject `000E` on a wrapper/anchor and conflicting foreign output classification. |
| Independent native checks | Reject missing Effects 04, bodyful/receiver/method-generic import, invalid convention/resolution tuple, unavailable native mapping or incompatible selected target. No marker synthesis, alternate symbol search or equal-ABI bypass. |
| Retained requirements | Reject missing effective 16/2/3 for their respective uses, unknown/conflicting revisions and one Module borrowing another's manifest. Include uncalled declarations and unreachable reads/calls. Ordinary reordered/duplicate identical Feature pairs remain equivalent. |
| Unsafe/backing boundary | Source/shared verification rejects absent lexical authorization and known insufficient extent/alignment, vacancy, expired backing or forbidden access despite unsafe. Unknown external backing is not invented provenance; structurally consumable false foreign assertions remain producer violations, not a required consumer truth audit. |
| Replacement | Compatible same-successor providers bind after row/formal rebasing. Changed pointee identity/depth/access, NativeParameter presence/extent/retention, descriptor, effect, checked-output relation or dependency fails the applicable comparison. Keep multiple original expectations and actual provider origins distinct. |
| Round-trip/remapping | Source-free load, reorder valid Contract/Type/Method maps, re-emit consumer **and** provider, reload, then exercise linked JIT and native-library/AOT paths. Preserve clause values, formal correspondence, snapshots, code/read order, backing and G context, not just equal pointer bits or host readback. |
| Excluded compositions | Reject private parameter payloads, kind `08`, raw reservations, NativeParameter-bearing anchors and unselected address/root/pin/other profile uses. A no-P/no-`000E` provider can be explicitly rebuilt under the selected successor without manufacturing either fact. |

## Remaining decision and support boundary

The remaining in-scope approval decision is adoption of these two exact
direct-NativeParameter compositions. No row field, closure, invocation mode
or preservation policy is left unspecified within the recommendation.
The user-authorized workflow treats this candidate as provisional direction;
it does not normatively approve P, F16 or NativeParameter source registration.

Composing full F (including foreign-pointer projection and raw outputs) is a
materially wider alternative: it would require E9/DF, anchor applicability,
Feature 8 and their I/G interactions. Replacing NativeParameter with another
descriptor or erasing it would respectively redesign a settled candidate or
lose obligations. Neither is needed for this direct-call gap. Missing
representations, provider/rewrite routes and execution support remain honest
implementation gates, not reasons to widen an old schema or claim that the
current failing path already supports the recommendation.
