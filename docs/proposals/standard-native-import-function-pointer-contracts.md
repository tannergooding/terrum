# NativeImport obligations on Standard function pointers

**Status: unapproved; authorized provisional design support only.** This
document recommends one integrated candidate. It approves no source schema,
NativeOutput allocation, Standard or private encoding, ABI, or runtime
behavior, and changes no normative owner or implementation. **It supplies no
compiler/runtime evidence.** Source inspection below identifies integration
points, not passing conformance or execution.

## Recommendation and scope

Permit the proposed Standard **E9 / Body-record-0D NativeAddress** to select a
callable NativeImport with declaration-owned NativeOutput/NativeParameter
contracts only when its complete foreign invocation protocol is represented
in the selected function-pointer type. Copy those invocation facts by value
into the noncallable signature anchor, rebase their subjects, and instantiate
them against the actual arguments at **every indirect invocation**.

Require **exact normalized foreign-protocol equality** at address formation
and pointer-view conversions. Keep the companion proposals' directed checking
for other admitted invocation facts. NativeImport resolution and selected
provider/definition identity remain actual-target obligations, not structural
pointer-type identity. Neither a matching ABI nor lexical unsafe authorization
permits erasing the foreign protocol.

Reuse shared OutputState, normal-result selectors and Dependency for raw
outputs, with an explicit anchor-applicability amendment. Supply the missing
NativeParameter carrier as candidate Contract **`000E`**, defined below.
Keep E9/record-0D formation separate from **DF/record-0C** reservation and from
the call instruction. No NativeCallable marker, checked-reference substitution,
target-body inspection, universal provider descriptor or runtime fat pointer
is introduced.

The bounded target is a synchronous, receiver-free, closed callable import,
including an exact selected Terrum provider that owns such an import.
NativeOutput retains its existing vacant whole-cell protocol and eligible
`Blittable Copy` payloads. NativeParameter transport covers the existing
bounded Read/Write/ReadWrite, NoEscape, static-minimum-or-unspecified-extent
case; it does not invent an escaping-buffer lifetime protocol. Foreign
interface slots, callbacks implementing raw outputs, variadics, live in/out
replacement and asynchronous outputs are not newly addressable here.

### Alternatives

| Candidate | Assessment |
|---|---|
| **1. Forbid E9 for every NativeImport with NativeOutput or NativeParameter** | Complete and safe as an admission restriction, and the required behavior while the complete carrier is unsupported. It leaves even a fixed, no-escape scalar output inaccessible indirectly. It is not the recommended completed interoperability rule. |
| **2. Preserve the exact declaration-owned invocation projection — recommended** | Reuses the existing anchor, state, selector, dependency and reservation designs. Adds one bounded parameter clause and explicit call applicability. Supports indirect use without changing foreign output semantics or making actual target identity part of a structural type. Exact comparison deliberately forgoes additional protocol subtyping. |
| **3. Replace NativeImport and call-site providers with a universal descriptor** | Could unify direct imports, interface dispatch, callbacks, checked outputs and ownership. It needs new identity/owner, provider resolution, source, failure, storage, binding and call representations across all those mechanisms. This gap does not justify that redesign. A descriptor naming only an ABI or symbol would still be incomplete. |

## Baseline and intended supersession

The accepted [native roles](../specs/native.md#native-declaration-roles),
[ABI layers](../specs/data-layout-and-abi.md#contract-layers),
[NativeImport and TypeSpec rows](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
and [OutputState applicability](../specs/representation/contract-clauses.md#5-outputstate)
remain the current authority. NativeOutput and a Standard NativeParameter
codec are not accepted by those owners. In particular, accepted OutputState
applies to checked initialization references/ranges, not raw pointer cells.
The [native conformance rejection](../specs/conformance/native-boundaries.md#4-pointer-dependency-and-lifetime-boundaries)
must not be treated as already amended.

The following are proposed, narrow replacements **if this bundle is selected**;
the linked documents and normative specifications are not edited here.

| Proposal owner | Intended amendment; preserved boundary |
|---|---|
| [Standard address formation](standard-native-address-formation.md) | Complete its callable-NativeImport eligibility and target/view check with the foreign projection below. Preserve E9, the 12-byte record, actual MethodMap target, TypeMap view, unsafe formation and code lifetime. |
| [Source contracts](standard-function-pointer-source-contracts.md) and [anchor contracts](standard-function-pointer-anchor-contracts.md) | Extend applicable invocation facts to this bounded foreign protocol. Add `000E` and raw-output applicability to the anchor's closed set; replace their exclusion of this protocol only for this candidate. Fixed bare defaults, exact identity, rebasing and other projection rules remain. |
| [Aggregate outputs](aggregate-native-outputs.md), [scalar/pointer cells](native-pointer-output-ownership.md) and [source dependencies](aggregate-native-output-source-dependencies.md) | Extend their direct-call-only reservation/indirect-call exclusion to a value whose type preserves the complete protocol. Their source annotation still belongs to the actual foreign declaration, not an arbitrary type-use attribute. Preserve the widened cell eligibility, state partition, dependencies and explicit resource-adoption boundary. |
| [Aggregate private persistence](aggregate-native-output-private-persistence.md) and [complete pointer persistence](native-function-pointer-contract-private-persistence.md) | Replace the latter's raw-output exclusion and extend tag 41's call context as specified below. Preserve the actual declaration's sole output collection and the composed, exact candidate-97 prerequisites; do not revive the original revision-90-only composition or legacy output tuples. |
| [Direct-import provider binding](native-output-direct-import-provider-binding.md) | Replace only its exclusion of module-selected addresses/indirect calls for an otherwise eligible provider-owned direct import. Use that selected provider's validated entry, original expectation and outer-3 table; preserve its no-re-export, no-alias and exact target-support restrictions. |
| [Checked pointer outputs](function-pointer-checked-output-parameters.md) and [same-runtime ABI](terrum-function-pointer-call-abi.md) | No replacement. Shared state/selector records do not equate their checked formation protocol with NativeOutput, or Terrum convention with a foreign convention. |

### Inspected implementation boundary

These are source observations, not claims that the proposed route works.
Older baseline paragraphs in composed proposals do not supersede current
code or provide qualification.

| Active path | Relevant fact and required integration |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | BoundFunction owns NativeOutputContracts separately from DirectNativeImport's library/export, convention, resolution and parameter vectors. NativeOutputContract carries formation, phase, selectors/states and dependencies. BoundIndirectCall has a target, expression arguments and unsafe evidence, not the direct-call native-output plan or mapped foreign facts. |
| [`NativeFunctionPointerContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs) | In-memory NativeImport address formation now projects raw outputs into the existing selectors, OutputStates and Dependencies identity and carries ordered NativeParameter descriptors; validation checks complete complementary output states and explicit dependency sets. NativeParameter-only indirect calls now bind, verify and use the existing foreign ABI lowering path for stable storage arguments. Raw-output/dependency-bearing invocation remains gated pending reservation, status correlation and provenance. This is not the proposed Standard schema or provider-consumer path. |
| [`DirectNativeImportContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs), [`NativeOutputContractValidator.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputContractValidator.cs) | The actual declaration validates output-cell types, conditions, normalized collections and input access/NoEscape/extent. Those facts are not supplied by an equal pointer signature. NativeParameter Write still requires initialized backing; it is not NativeOutput. |
| [`SemanticVerifier.DirectNativeImport.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.DirectNativeImport.cs), [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | Direct BoundCall occurrences create destination-generation/status facts and stage dependencies. Indirect calls invalidate correlations rather than creating the equivalent output facts; VerifyIndirectCall checks the value signature/arguments. Generalize the protocol, not an API recognizer. |
| [`TrRmStandardNativeImportTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeImportTable.cs) | The Standard reader validates the assigned 16-byte row, static bodyless receiver-free/method-generic-free callable, resolution/convention and published caller-unsafe requirement. It does not allocate NativeOutput or NativeParameter. |
| [`TrRmContractTableReader.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs), [`TrRmNativeFunctionPointerAnchorTable.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmNativeFunctionPointerAnchorTable.cs), [`TrRmStructuralMetadataValidator.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) | Standard contracts currently restrict OutputState to initialization types and reject Development native clauses `10/11/12`. Anchors allow only `0001/0002/0004/000B`, forbid actual native roles and check structural cycles. Extend these independent checks together; the Development NativeParameter structure is not an admitted Standard row. |
| [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) | Address fixups name actual entries, and indirect lowering captures the target before arguments. Its value-only argument staging does not implement this output reservation/publication plan. The existing private SystemDirectory import qualification is not evidence of Link or Dynamic source-resolution support. |

## Semantic owner, type identity and selected target

The **actual declaration** remains the semantic owner of its foreign promise:
selected Module/definition plus logical explicit-formal ordinal. The pointer
anchor owns a **structural invocation projection**, not that declaration's
identity, import row or executable role. Represent the complete pointer
contract as its existing signature/convention/Effects/selectors/states/
dependencies plus these normalized foreign facts:

| Fact | Required contents |
|---|---|
| Raw output | Formal ordinal and complete inner T; vacant-whole-cell formation; positive exact object extent/alignment from the selected layout; no incoming-T read; call-extent address use/no address retention; normal publication phase; exact success/complement selectors and live/vacant states; complete inner-value dependency set. |
| NativeParameter | Formal ordinal, complete pointer/pointee identity, access, retention, extent kind/count and derived target alignment/stride. Presence is significant; an absent annotation is not NoEscape, an unknown bound or an output privilege. |

The fixed raw-output properties need no independent stored flags. Under this
candidate, a complete `write ptr<T>` formal with the required raw OutputState
pair and dependencies denotes exactly that fixed formation protocol.
Checked-output formals are distinguished by their complete reference type,
not by a guessed state-row interpretation. NativeParameter facts use `000E`.
No formal may have both foreign classifications.

Resolve and substitute in the defining declaration's Module/rule context,
then rebase result/formal subjects into the receiver-free anchor. Preserve
complete nominal types, nested qualifications, selector attachment identity,
phases and external dependencies under the anchor normalization rules.
Physical rows, formal names and source enum numbers are not identity.
No free method/owner formal or hidden environment may remain at execution.
Include the added type/contract edges in structural cycle checking.

Two imports of different DLL symbols may consequently have equal pointer
types, yet their values still name different entries and retain different
code/provider origins. Conversely, equal machine carriers do not equate
NonZero with NonNegative, vacant with live failure, NoEscape with possible
retention, or different pointee/extent/access contracts.

### Exact target-to-view relation

For address formation, perform all three checks, without substitution of
one for another:

1. **Bind the original declaration expectation.** An owned declaration uses
   its own complete contract. An Import first retains its original expected
   declaration and resolves the exact selected provider/key. Check the
   full directed binding, exact foreign protocol and native descriptor,
   including library/entry, resolution mode/policy and convention.
2. **Establish addressability.** The selected provider must actually own an
   eligible callable NativeImport and supply an admitted native entry.
   No NativeCallable is required or synthesized. An Import expectation,
   signature anchor, data import, arbitrary equal-shaped method or unbound
   bodyless declaration cannot stand in for that entry.
3. **Check the selected type view.** Its complete ordered types/modes and
   original convention must agree. Require equality of every normalized raw
   output and NativeParameter fact, including raw-output dependency sets.
   Apply the companion directed relation to the remaining applicable facts.
   A DoesNotReturn weakening or redundant caller-unsafe projection cannot
   delete the foreign protocol. Unsupported implication is not success.

This exact foreign comparison is intentionally stricter than possible future
protocol subtyping. Do not drop even a seemingly stronger NoEscape guarantee,
change an extent requirement, delete a conditional output, or substitute a
broader raw-output source set through ordinary pointer projection.

At an indirect call, **bind the saved structural contract to the staged
arguments**, destinations and their actual provenance. This does not resolve
the DLL again or require knowing a unique target MethodDef at that call.
Values passed through parameters, fields, returns and control-flow joins
still have the complete protocol. Formation/transport retains each original
provider expectation and guarded actual code origin; those are not recovered
from the structural type or replaced with the anchor's Module.

## Source inference, views and call formation

An uncontextualized NativeAddress infers the selected declaration's complete
invocation projection. For a semantic Import, use the **original expected
contract after validated binding**, not extra facts discovered on the current
provider. A contextual pointer type is checked against that exact projection;
it does not alter the declaration or acquire initializer-dependent defaults.

A bare foreign `fnptr` has the source-contract proposal's fixed defaults and
**no raw NativeOutput/NativeParameter protocol**. A raw pointer parameter is
not an unconditional checked output. Thus a bare same-shaped type cannot
receive an obligation-bearing address under this candidate. Complete inferred
locals and imported exact types remain usable; no new contract-bearing source
type spelling, type-query workaround or arbitrary type-use annotation is
selected. An authored adapter must establish its own complete contract, not
be recognized by name or by examining its body at the call site.

Check exact foreign preservation at initialization, assignment, argument,
result and contextual-join boundaries. Inferred locals retain their types;
uncontextualized joins require equal complete types. Mutable pointer cells
and references to those cells remain invariant. A value projection changes
neither address bits nor actual code/backing dependencies.

Extend the output proposals' **proposal-only `&init` direct-argument form**
to an indirect call only at a formal classified as a raw output by that
saved type. It still selects one wholly vacant, initialization-capable local
of exact T. It is not a storable pointer expression, checked reference,
ordinary `&place`, field/array output, or arbitrary address-of adaptation.
Pointer calls do not gain omitted defaults or new named-argument syntax.
Where a pre-existing source view provides argument mapping, retain it rather
than reordering evaluation; otherwise use the ordered positional signature.

Formation and invocation independently need lexical unsafe authorization.
Preserve declared/published Effects `04` separately from role/family-imposed
unsafety; neither inject it into an anchor solely for being a pointer nor
erase one that was published. An unsafe declaration authorizes no operations
inside its body. E9 forms a pointer and has normal continuation even for a
DoesNotReturn target; it performs no argument reservations or invocation.

## One invocation and its outcomes

Use the ordinary Capture, Stage, Check, Prepare, Invoke, Publish phases:

1. Capture the typed callable value once, before written arguments. A later
   assignment or nested call cannot change the captured target or its view.
2. At each raw-output argument position reserve the exact vacant cell
   generation/backing. At each NativeParameter position stage the actual
   pointer/eligible stable address and check the declared access, extent,
   validity and NoEscape obligations. Preserve source order and earlier
   snapshots through nested calls.
3. NativeParameter Write/ReadWrite is access to **already initialized**
   admitted storage, not permission to supply vacancy. A missing static
   extent does not prove a region, initialization, alignment or terminator.
   Unsafe authorization cannot waive known violations. No implicit root,
   pin, retained temporary or lifetime extension is acquired.
4. Bind every reservation and dependency source to this call occurrence and
   formal. Keep output cells disjoint; reject known overlap/invalidation and
   conflicting alias/reentrant access. A call-local projected address may
   not escape through a result, output, capture or saved native alias.
5. After the ordinary target checks and dependency-valid preparation, Invoke
   receives each **actual reserved cell address**, not T's old bits, a
   pointer-to-pointee projection or a copy-back temporary.
6. For raw outputs, capture the declared int32/uint32 result once, including
   a hidden result holder when source ignores it. NativeParameter-only calls
   keep their ordinary result, including floating or unit results; they gain
   no status requirement or output relation. Complete required postconditions/
   phase cleanup before normal Publish. Selector evaluation/publication is
   non-failing bookkeeping, not a byte-validity check or a second call.
7. Publish the exact per-output state/dependencies and release the reservation
   according to the saved result and destination generation. Copying a
   discriminator preserves that correlation; replacement, retirement,
   storage reuse and relevant alias mutation invalidate it.

For raw outputs retain exactly the shared phase-3 selector pairs:
`3/4 = nonzero/zero` on int32 or uint32, and `5/6 = nonnegative/negative`
on int32 only. Each output has its own live-success/vacant-complement
relation over the same captured result. Signed negative values satisfy
NonZero; zero satisfies NonNegative. There is no Boolean-one, symbol-name,
pointer-null or all-outputs-success heuristic.

| Outcome | Required behavior |
|---|---|
| Target/argument/check/preparation failure before Invoke | Cancel only abandoned unentered reservations; their cells remain vacant. Preserve earlier effects and still-held staged responsibilities. An address formed earlier is not an entered foreign call. |
| Selected normal success | Publish one complete valid T and its declared inner-value dependencies, without a constructor, zero fill, marshalling or provider-bit repair. |
| Normal complement | Publish no T; leave logical vacancy even after partial/full writes. Do not read, adopt or release these bytes as an output. The provider settles any unpublished acquisitions required by its existing contract. |
| Undiscriminated or discarded normal result | Retain conservative possible-live state and guarded dependencies. No unguarded T read or unconditional-complete default follows. |
| Admitted exception after Invoke starts, including failure before normal Publish | No normal result or state relation. Keep exceptional indeterminacy and actual outstanding responsibilities; do not cancel as unentered, claim complement vacancy, or clean as complete T. Reuse needs independent state evidence. |
| DoesNotReturn | Keep all declarations, inputs, reservations and support checks; the call is terminal with None result and no normal publication. It is not a no-failure proof or post-entry cancellation. |
| Divergence, failfast or process termination | No fabricated return, rollback, output publication or cleanup guarantee. Backing/code required by active execution cannot be released as though the invocation ended. |

### Cell validity is not resource ownership

For T = `ptr<U>`, publication initializes the **pointer cell**, not U's
contents, allocation, extent or lifetime. For integer T it initializes an
integer, not a handle's resource or release obligation. A function-pointer
payload needs its own complete callable and code-lifetime promise; output
bytes alone cannot establish one. Raw Copy bits create no extra resource
reference, checked owner, typed interface identity or release permission.

Preserve the scalar/pointer output proposal's explicit authored adoption and
cleanup boundary, including fixed/validated interface identity and exact
release origin. Its bounded resource-wrapper case still requires no
escaping failure between successful machine return and cell Publish.
This indirect route neither weakens that gate nor supplies an automatic
resource ticket, cleanup adapter or failure bridge.

## Concrete unapproved Standard carrier

This is one exact candidate schema, not a claim that existing rows already
encode all the facts. NativeOutput itself remains proposal-only; **no
NativeOutput clause/table or approved allocation is created**.

| Surface | Candidate selection |
|---|---|
| NativeImport section `0021` | Unchanged 16-byte row: owner kind/mode/policy/convention bytes, typed owner row and two String references. Resolution belongs here, never in the anchor. |
| TypeSpec form `0D` | Unchanged seven bytes: form, zero qualifiers, original convention, typed `u32 signatureContractMethodDef`. Foreign conventions remain `1..4`; no retagging to Terrum. |
| Anchor contracts | Existing `0001/0002/0004/000B` plus new `000E`. Extend `0002`/destination-2 `0004` applicability to the fixed raw output on this signature anchor; do not attach NativeImport, NativeCallable or NativeExport to it. |
| Raw output states/dependencies | Keep the output proposals' 20-byte OutputState/Dependency rows, complete pair, explicit empty-or-nonempty source set and normal-publication phase. No copied source attribute or extra output ordinal table. |
| Selectors | Keep 20-byte `000B` attachments, phase `3`, six closed result tests and sections `0037/0038/0039/003B/003C` under the bounded Feature `10/1` composition. Preserve exclusive graph ownership and ordinary-result footprints. |
| NativeParameter | **Candidate `000E`, exactly 20 bytes**, below. It is a MethodDef-owned invocation fact, not a native-declaration role. |
| NativeAddress | Keep `E9 00 recordIndex:u16` and its 12-byte kind-`0D` record: actual MethodMap target plus complete TypeMap view and result selector. No obligation flag or second formation record. |
| Reserved output invocation | Keep DF/12-byte kind-`0C` OutputReservation and CallSite input `(selectorKind,sourceUse)=(2,6)`. Extend its matching CallSite from MethodMap target kind `1` to captured pointer Register `2` or Home `3`. |

### Candidate NativeParameter row

All multibyte fields are little-endian. The exact complete row is:

| Offset | Field | Domain |
|---:|---|---|
| 0 | `u32 ownerRow` | Nonzero typed MethodDef row; not a DefId |
| 4 | `u16 clauseKind=000E` | This candidate only |
| 6 | `u16 flags=0` | No authorization/extension bits |
| 8 | `u32 formalOrdinal` | Zero-based explicit formal in the owner's MethodSig |
| 12 | `u8 access` | `1=Read`, `2=Write`, `3=ReadWrite` |
| 13 | `u8 retention=1` | NoEscape for the complete invocation, including applicable exceptional exits |
| 14 | `u8 extentKind` | `0=NoStaticMinimum`, `1=MinimumElementCount` |
| 15 | `u8 reserved=0` | Required zero |
| 16 | `u32 extent` | Zero for kind 0; `1..FFFF_FFFF` for kind 1 |

Occurrence key is `(owner,000E,formalOrdinal)`; duplicates reject even when
equal. Absence means no such contract, not an implicit row. Each owner has
its own exclusively owned Contract range.

Admit the row on a selected bodyless callable NativeImport declaration,
its retained expected declaration, or a noncallable foreign-pointer anchor.
The formal has ordinary Param mode `00` and complete `ptr<T>` or
`write ptr<T>` type. Read does not upgrade access; Write/ReadWrite requires
the complete writable pointer type. Derive T, stride and required alignment
from that exact formal and selected layout; cross-check an in-memory
DirectNativeParameter.PointeeType rather than silently discarding it.
There is no independent type, pointer-depth or alignment-override field.

For typed T require its independently admitted fixed directly interoperable
region representation. Kind 1 requires at least that many complete elements,
with checked stride/extent/address arithmetic; it is not a byte count.
Kind 0 supplies no static minimum or permission for an unbounded access.
Actual provider operation bounds and raw-pointer validity remain required.
Opaque `ptr<void>` admits only Read, kind 0/extent 0, with no typed pointee
access inferred. NoEscape prohibits retention of the supplied call-extent
region/address; it does not retain its backing for the caller.

An escaping/unknown retention mode, extra alignment promise or richer region
contract is outside this bounded descriptor and rejects affected formation,
transport and invocation rather than becoming NoEscape. This preserves the
active direct-import restriction; it does not adopt MayEscape or guess an
endpoint from a Dependency row. No source registration or old ordinal-based
NativeParameter attribute syntax is approved here. Exact source/Core
registration remains its own gate; this row transports normalized facts.

### Raw-output anchor and call validity

For a raw formal require exact `write ptr<T>`, eligible T, the two phase-3
OutputState rows `(success,1)` and `(complement,0)`, and its explicit
destination-2, phase-1 dependency set. In a receiver-free pointer contract
sources can be eligible explicit inputs, not receivers, raw output formals
or the reserved address itself. Explicit empty sources preserve independent
type/backing/code/provider obligations. NativeParameter and raw-output
classification cannot overlap. An unannotated raw formal acquires no
initialization relation by default.

The anchor's role is established by its TypeSpec reference. Its raw output
applicability is explicitly admitted here under Features 8 and 16, **without
pretending it owns a NativeImport**. The actual address target still needs
the real declaration role. The anchor remains bodyless, receiver-free,
method-generic-free, non-exported, noncallable and nonaddressable.

The general kind-`02` CallSite keeps its exact existing extent:
`20 + 4*inputCount + 2*preparationCount` bytes including its header.
For raw outputs use general `90`/`E0`, never compact value-call windows.
Its captured target has the complete pointer type. Each source-use-6 input
names the reserved inner-T Home; each kind-`0C` record names this CallSite,
that Home and the full-width formal ordinal. Validate against the **saved
pointer signature**, not a MethodMap lookup or guessed actual target.
Reserve/cancel remains DF, not E9, DestinationPreparation or source-use `5`.

Normally returning output calls capture the status in R0..R13 or an exact
result Home, hidden if discarded. None, R14/R15 and an output Home cannot
lose or substitute that status. DoesNotReturn instead uses terminal general
CallSite/None while preserving the complete declared status type and inputs.
No new call record field, result ID, Home role or trusted flow flag is added.

### Capabilities, namespaces and allocation boundary

Recommend **candidate Feature `16/1=ForeignInvocationContracts`**, governing
edition `1`, prerequisites `[(1,1)]`. Its coherent scope is `000E` declaration
and signature facts, complete foreign-pointer identity/projection, indirect
protocol binding and their transformation/consumption rules. Require it for:

* Every retained `000E`, including uncalled expected declarations.
* Every raw-output function-pointer contract/view or nested type use.
* Formation/invocation retaining either foreign classification, including
  discard, no-return, unreachable uses and retained unused records.

Ordinary direct raw outputs retain Feature `8/1=NativeOutputCells` from the
scalar/pointer amendment and its `[(1,1),(2,1),(10,1)]` prerequisites;
raw-output anchors/indirect calls need **both 8 and 16**. Closed selectors
require bounded `10/1` with prerequisites `[]`. Plain NativeParameter alone
does not acquire Feature 8 or 10. Actual pointer values need 2; E9 and
indirect calls need 4, whose existing closure includes 3, 2 and 1.
Native-unwind 7 and query 13 remain conditional on their own retained uses.
Compute each using Module's complete closure, including nested types and
dependency Modules; a Feature name or provider's manifest is not a substitute.

`000E` is a `u16` Contract-kind candidate, not TypeSpec `0E`, a section or
Body kind. The registry assigns clauses through `000C`; the copy-origin
proposal separately proposes `000D`. Feature 16 is not registered: the
predicate-composition proposal mentions that number only for an **unselected**
general-graph alternative. This candidate intentionally supersedes that
alternative's use of `16/1` with `ForeignInvocationContracts`; general
predicate graphs remain unassigned and would need a different Feature ID if
later selected. Joint adoption must reconcile any competing candidate; low
fixed-width numbers save no bytes. Feature 14/15
root/pin/copy-origin candidates are not consumed.

No section, DefId/token kind, TypeSpec form, prefix, escape, instruction
width or new NativeOutput ID is allocated. Global typed rows remain
`1..00FF_FFFF` in u32 with reserved-zero high byte; String, MethodDef,
Contract and PredicateGraph fields retain their own namespaces.
MethodMap uses MethodDef/Import tokens, not NativeImport section rows;
TypeMap uses kind-`11` TypeSpec tokens, not TypeSpecRefs. The TypeSpec anchor
field is a typed MethodDef row, not either map index.

Body record/map/Home indexes remain zero-based u16 with counts up to
65,536, including usable `FFFF`; formal ordinals/counts remain u32 and
include logical unit parameters but no receiver/hidden result. Extent math
must reject overflow or an unsupported target/address range. Private limits
do not narrow Standard integer fields.

The separate [root/pin candidate](reference-root-pin-runtime.md) also proposes
Body kind `0D`. That pre-existing allocation conflict is **not** an alternate
decoder for NativeAddress. This bounded bundle selects NativeAddress `0D`,
not the pin runtime; a combined pin schema needs a separate explicit
noncolliding record allocation before affected use. No implicit pin is
needed or authorized by this rootless-cell route.

Select one exact amended draft out of band under
[format versioning](../specs/representation/format-versioning.md).
Equal `(1,0)`, Feature revisions or plausible bytes select no decoder.
Current Standard input does not acquire these meanings from Feature 16
alone. Development `11/12` clauses, six-byte pointer signatures and private
tuples are not aliases or fallback carriers.

## Resolution, provider binding and replacement

Keep the original DirectNativeImport descriptor: exact library/entry,
convention, Link versus Dynamic and applicable policy. Link has no runtime
loader policy; Dynamic retains Default/CompatibilitySearch and its actual
target mapping. No suffix inference, resolver fallback, success-shaped null
pointer or native-specific sample policy is added. A required unsupported
target mapping rejects use; a private SystemDirectory IAT is not relabeled
as an implementation of those source modes.

Resolve omitted entry names and other defaults in the original declaration's
context, not from a replacement's current name. Compare under the owning
target/binding rules: for example, the private Windows provider candidate
compares library names ordinal-ignore-case and foreign export names ordinally.
This adds no case normalization or alternative lookup policy.

Complete resolution and install a usable entry before E9 publishes its
result, even for discard. Failure publishes no address and reserves no
future output. Indirect invocation captures that established value; it
does not redo NativeAddress or perform a new symbol search.

For a module-selected provider `P`/export `K`, retain that semantic edge
and the original importing expectation. Where the private provider-binding
candidate selects a precompiled provider route, the pointer designates its
**validated executable entry for K**, which leaf-jumps through P's declared
foreign import. It is not the consumer's independently resolved foreign
symbol, an IAT cell address, a signature anchor or an imported-proxy re-export.
Root the provider entry/foreign binding when only address uses survive.
The ABI-transparent leaf adds no second reservation, publication, cleanup,
failure bridge or NativeCallable contract.

Before emission or affected code/image loading, check the selected identity
and every original expectation, then exact foreign protocol/descriptor,
ordinary directed obligations, addressability and selected view. Provider
extra facts do not rewrite the expectation before comparison. Equal foreign
DLLs or symbols do not merge P and another provider. A packed direct binding
must retain all original expectations and stronger provider facts actually
used; keeping a required Import is preferable to an illegal direct token.

Semantic relowering may use an explicitly admitted resolved binding while
preserving that proof and its origins. Native set/graph root-only JIT must
retain the same selected precompiled provider route as AOT, not silently
regenerate providers or bypass their entries. Check address-only and
declaration-only expectations as well as emitted direct machine imports.

Replacement preserves exact foreign formation, status partition/states,
type/layout/access, extent, no-retention, dependencies, phase, resolution,
convention and selected provider/key, plus all other retained guarantees.
An unchanged precompiled consumer cannot be serviced by dropping these facts
because its call is indirect. Updating a binding does not patch escaped
addresses. Old entries/providers cannot retire while an old pointer or
active invocation can still reach them.

## Private persistence interaction

This section is an **unapproved exact amendment to the unimplemented
candidate 97** in the complete pointer-persistence proposal, not a claim
that current private bytes support the protocol. Retain its complete
prerequisite composition and `fnptr#i` type/address-contract references.

1. Extend its existing 12-byte OutputState entries to raw-output formals
   under this document's type/classification rules. Keep checked and raw
   meanings distinct; add no parallel raw state/selector codec. Raw-output
   destination-2 dependencies use the existing 12-byte rows and phase 1.
2. Immediately after each signature row's Dependency vector append mandatory
   `i32 nativeParameterCount`, then that many **12-byte NativeParameter
   payloads**: `u32 formalOrdinal, u8 access, u8 retention, u8 extentKind,
   u8 reserved=0, u32 extent`. Domains are those above, owner implicit in the
   signature. With n parameters, s selectors, o output states, d dependencies
   and p native parameters, the amended row is exactly
   **`29 + 4*n + 12*(s+o+d+p)` bytes**. Count zero is present, not an optional
   old-layout variant. Require `0 <= p <= n` and unique valid formals.
   Canonical writers order them by formal ordinal; ordinary readers may
   accept consistent physical reordering.
3. Keep the actual declaration's NativeOutputContracts/role as the sole
   declaration authority. Cross-check the pointer projection against it and
   DirectNativeImport.Parameters, translating private/host enums explicitly.
   Do not serialize a direct-import role or library target inside a
   structural signature. Derive T from complete private formal types and
   cross-check any retained parameter pointee field. A prototype raw-pointer
   carrier whose pointee/access exists only in side metadata must first be
   normalized to the exact admitted private pointer type. Do not write a
   void pointee and recover the lost type from a provider after loading.
4. Retain tag 41's exact 21-byte void reservation envelope/local/pointee
   payload. Extend its admitted parent from direct BoundCall to an argument
   of BoundIndirectCall whose saved signature has the matching raw output.
   The containing call occurrence, actual formal mapping and local generation
   supply identity; spans, names, serialized row numbers and host object
   equality do not. It remains invalid as an ordinary value, cast, target,
   initializer, returned pointer or unannotated argument. Existing indirect
   target/argument expression framing remains unchanged.
5. Preserve the original `NativeAddressContract:fnptr#i` reference before
   Function payload erasure and validate the full foreign projection there.
   Keep the provider proposal's outer version **3** foreign-expectation table,
   including original native parameter/type facts and candidate-97 resolution/
   convention tokens. Cross-check, do not choose between conflicting copies
   or reconstruct an erased expectation from the replacement.

Private i32 type identities/table indexes are not Standard rows or
TypeSpecRefs. Remap their full pointee/nested-signature closure and every
`fnptr#i` reference. Existing signed/private capacity limits remain explicit
admission limits, including a count/extent that cannot be represented there.
Do not truncate a u32 Standard count to fit a private int.

The new vector and raw applicability intentionally replace the previous
candidate-97 signature layout and its raw exclusion. There is one selected
amended 97, no optional trailer, guessed older-97 reader or header-patched
conversion. The separate missing checked-reference private identity is not
solved by representing it as a raw cell.

For `.trmn`/sets/graphs, derive the semantic contract, retained expectations
and stored image from the same checked inputs; rebuild lengths/checksums.
Changed/lossy existing images need regeneration. Independently validate the
inner schema and original bindings before affected image loading. Private
success supplies neither Standard support nor this document's missing
compiler/runtime evidence.

## Verification, transformation and lowering handoff

The candidate is incomplete if integrated only into a binder or type table.
Implementation requires the following coherent changes; none is made here.

| Layer | Required preservation/check |
|---|---|
| Binding and normalized model | Obtain one authoritative declaration-owned output/parameter projection. Extend complete pointer identity, equality, interning, required-type discovery, source inference and exact foreign-view checking. Do not manufacture DirectNativeImport on an arbitrary pointer value. |
| Bound tree and producer verification | Give indirect calls the same protocol planning, formal/source correspondence, reservation occurrence identity, staged sources, generation/status facts and invalidation as direct calls. Shared traversal/rewriting must visit target, arguments, reservation pointees and every contract type. Revalidate replacement targets/types rather than retaining stale plans. |
| Standard readers/writers | Extend contract, anchor, import-signature, structural, feature and register-Body checks together. Resolve owner classification without requiring an anchor to have a NativeImport row; validate actual-target eligibility separately. Emit complete projected contracts/graphs and reservation-bearing calls source-independently. |
| Dependency/private/native consumers | Preserve every original expected descriptor, address-only edge and code origin through declaration projection, restoration, set/diamond merging, packing and replacement. Validate stored provider entries/images as well as semantic descriptors. |
| Lowering/JIT/AOT | Use the saved complete call plan and actual entry, stable exact T cells, checked native argument/result placement and normal/exceptional state protocol. Constant-target optimization/devirtualization cannot skip protocol checking, change publication or acquire a stronger view from the body. |

An indirect bound call needs a selected complete invocation-contract reference,
source-argument/formal correspondence, call-only reservation operands and
staged dependency-source facts. These can be a derived checked call plan,
not another native-import declaration. Generalize direct-call output facts
to either call occurrence and rebuild the plan after rewriting. A void-typed
BoundNativeOutputReservation is checked against the classified raw formal
and exact pointee/destination; ordinary `argument.Type == formal.Type` cannot
validate it or turn it into a first-class pointer value.

Producers verify authored vacancy, source ordering, alias/authority/backing
and unsafe facts, and publish truthful metadata. Mandatory consumers check
framing, exact shapes/domains, bounds, namespaces, owner/range consistency,
complete types/layouts, selector/result/phase applicability, state pairs,
parameter uniqueness, reservation-to-call/input agreement, capabilities,
addressability and every required binding before affected use.
They need not semantically re-prove arbitrary producer Body facts or foreign
provider behavior. Optional audits remain separate under
[reader trust](../specs/representation/reader-trust.md). A forged semantic
promise is not made true by a checksum, successful load or trusted bit.

Re-encoding remaps MethodDef/Import targets, TypeSpec/anchor references,
MethodSig/Param correspondence, exclusively owned Contract/selector graph
ranges, predicate references and dependency subjects. Remap E9's record/
MethodMap/TypeMap/result indexes separately from DF's record/CallSite/Home/
formal references and each CallSite's value target/input/result/preparation
selectors. Clone context-dependent rows rather than reinterpret their
imports. Structural type deduplication must not merge actual targets or
distinct output Homes.

Rebuild map masks/counts, record offsets/extents, block/code offsets and
lengths, instruction ordinals, SourceMap slots and containing section
lengths. E9 and DF retain whole-instruction SourceMap role 0. Canonical
writing orders the new parameter clauses by formal ordinal and follows
existing typed-record first-use/anchor rules; equivalent consistent physical
reordering remains ordinary input. No byte substitution or transient
provenance/verifier-cache serialization is a valid remap.

The foreign call retains its exact selected platform ABI. On Windows x64,
each output formal contributes one eight-byte pointer carrier irrespective
of T's cell size; a uint32 cell is still four bytes, not an eight-byte
write target. Preserve shared integer/floating argument positions, stack
arguments, shadow space, alignment, nonvolatile/control state and unwind
rules. E9 publishes a real resolved entry/fixup or validated equivalent leaf
stub, never unresolved or serialized process address bits.

JIT publication completes fixups, write/execute and instruction-cache work
and required unwind registration before exposure. AOT retains correct
entry relocations, images and provider closure. Keep entry stubs, provider
libraries, helpers and unwind backing live through every surviving address,
call and dependent value. Dynamic import lifetime remains the importing
Module/image lifetime with **no new per-pointer unload lease**; an escape
does not extend it automatically. Neither dropping direct calls nor type
equality discharges actual code/load obligations.

## Checked outputs and ordinary same-runtime calls

This proposal changes neither `ref<init T>` identity nor its checked-output
semantics. Those formals use Param `00`, checked capability formation and
CallSite source-use `5`; raw outputs use ordinary `write ptr<T>` plus this
foreign protocol, DF/record-0C and source-use `6`. Equal physical addresses
or shared OutputState/Dependency/selector rows do not make those signatures
or reservations interchangeable. A checked output may have its independently
admitted live-failure or unconditional-complete relation; this raw protocol
does not inherit it.

The ordinary same-runtime ABI candidate, including its separately proposed
checked-output addition, keeps the Terrum convention, validated runtime
domain, joint-lowering restrictions and ordinary failure dispatch. It does
not gain NativeImport provider resolution, a foreign callback entry,
automatic raw-cell adaptation or a cross-runtime ABI. A NativeImport keeps
its foreign convention and boundary/unwind/containment obligations even
when a target happens to use the same registers. Foreign `000E`/raw-output
facts on a Terrum-convention anchor are outside this candidate and reject.

## Adoption owners and required cases

If adopted, integrate the exact candidate into these owners, not optional
attributes or implementation-only exceptions:

| Owner | Intended addition/amendment |
|---|---|
| [Native boundaries](../specs/native.md), [grammar arguments](../specs/grammar-spec.md#32-types-parameters-and-arguments), [flow](../specs/language/initialization-and-flow.md) | NativeImport address/view preservation and indirect applicability of the output proposal's argument/reservation/result protocol; keep source-schema approval distinct. |
| [Complete types](../specs/language/types-and-normalization.md), [clauses](../specs/representation/contract-clauses.md), [metadata](../specs/il-metadata.md) | Exact foreign projection, `000E`, raw-output anchor applicability and actual-target versus structural-owner distinction. |
| [Registry](../specs/representation/registry.md) and [machine registry](../specs/representation/trrm-1.registry.json), [manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Exact new clause/Feature candidate and complete per-Module detection/closure, composed with the still-unapproved output/selector/address candidates. |
| [Instruction calls](../specs/il-instruction-set.md#calls), [canonicality](../specs/representation/encoding-canonicality.md), [binding](../specs/representation/binding-and-replacement.md), [consumer checks](../specs/representation/reader-trust.md) | Saved-pointer CallSite applicability, exact matching/remapping, before-use binding and producer/consumer proof boundary. |
| [Windows call interface](../specs/targets/windows-x64-call-interface.md), [deployment](../specs/targets/windows-x64-deployment.md), [JIT](../specs/jit.md), [native conformance](../specs/conformance/native-boundaries.md) | Preserve foreign ABI, provider entry and code lifetime; amend only the selected protocol's rejection and add independent cases below. |

The following are **future acceptance requirements, not tests run or evidence
supplied by this document**. Use public compiler APIs, independently authored
Standard byte fixtures and unrelated synthetic headless providers. Separate
source/type checking, decoding, binding, emission and supervised execution.

| Case | Required outcome |
|---|---|
| NI-FP-01 Addressable import | Local and selected-provider callable imports with output/parameter facts form addresses without NativeCallable. Exercise Link and both Dynamic policies where the target actually supports them; failed resolution publishes no pointer, including discard. |
| NI-FP-02 Independent cells | Scalar, nested aggregate, pointer and independently admitted fnptr payloads; several disjoint outputs using different supported conditions on one result. Preserve exact T and dependencies without constructors or ownership inference. |
| NI-FP-03 Identity and views | Equal protocols from different symbols give equal structural types but distinct actual targets/origins. Reject bare same-shaped type erasure, altered access/retention/extent, state/default upgrades, raw-to-checked substitution and convention casts. Preserve independent legal Effects projections. |
| NI-FP-04 NativeParameter | Read versus Write/ReadWrite, typed versus opaque, minimum count versus no static minimum, and NativeParameter-only floating/unit results without a forced status. Require initialized stable backing and actual access/extent/alignment; Write cannot replace NativeOutput. Reject unsupported escaping retention or extra descriptor facts rather than dropping them. |
| NI-FP-05 Capture and reservation | Target changed by a later argument, reordered mapped source arguments, nested calls and repeated call sites/identical spans. Capture once, retain outer reservations, bind exact occurrence/formal/generation and cancel only unentered calls. Reject overlap, second reservation and reentrant access. |
| NI-FP-06 Result boundaries | int32 minimum/-1/0/1/maximum and uint32 zero/maximum; zero succeeds only for NonNegative, negative nonzero succeeds for NonZero. False after no/partial/full writes stays vacant. Discard requires hidden status; stale/saved discriminators, joins and loops cannot revive another generation. |
| NI-FP-07 Failure boundaries | Failure during staging/check/preparation, admitted foreign exception after writes, fallible work before Publish, no-return non-unit status, divergence and failfast. Distinguish cancellation, indeterminacy and absent normal edges; no cleanup/rollback or default output is invented. |
| NI-FP-08 Cell versus resource | Published pointer does not initialize its pointee or establish a typed interface/resource owner. Invalid foreign success bits remain a provider-truth violation, not a promised decoder diagnosis. Explicit adoption/release and the resource-wrapper publication gate remain necessary. |
| NI-FP-09 Exact structures | Independent 20-byte `000E` fixtures, raw OutputState pairs/graphs and Register/Home target CallSites. Reject duplicate/orphan clauses, bad owners/modes/ordinals/flags, unknown enum values, missing/noncomplementary states, wrong result tests and cross-owner graphs. Anchors with NativeImport/NativeCallable or used as actual targets reject. |
| NI-FP-10 Reference/capacity boundaries | Body indexes 0/255/256/65535 and count 65536; reject 65537 and one-past references. Check global zero/high-byte/wrong-table references, full-width formal ordinals and u32 extent maxima with checked multiplication/address limits. Private narrowing rejects explicitly. |
| NI-FP-11 Invocation carrier | DF binds kind-02 CallSite target 2 or 3 and exact T Home/input/formal; E9 is only formation. Reject wrong call/Home/record, source-use 5 substituted for 6, compact output calls, lost status and cancellation after Invoke under the applicable structural/producer checks. |
| NI-FP-12 Original expectation | Unchanged consumers reject replacement changing selected provider/key, direct-import role, library/entry, resolution, convention, output partition/type/dependency or parameter contract despite equal ABI carriers. Do not infer the original expectation from the replacement. Address-only/uncalled dependencies still bind. |
| NI-FP-13 Re-encoding | Permute/deduplicate type, anchor, parameter, contract, graph, map and record rows; include eligible inline TypeSpecRefs, nested types and diamond origins. Retain distinct actual targets/Homes and clone context-sensitive rows. Rebuild code/source indexes rather than replacing bytes. |
| NI-FP-14 Private composition | Exact amended-97 empty/nonempty parameter vectors, raw state/dependency projection, `fnptr#i` references and tag 41 under indirect calls. Outer-3 module/set/graph expectations survive Function erasure, type pruning and restoration. Recomputed checksums do not hide malformed inner contracts or missing expectations. |
| NI-FP-15 Source-free execution | Emit the selected Standard bundle, reload without source, transport the pointer through parameters/fields/results and invoke under AOT and JIT. Separately test unchanged precompiled providers/root-only JIT through the real selected provider entry, register/stack outputs, no extra publication and retained code/stub/library lifetime. Image emission alone is insufficient. |
| NI-FP-16 Cross-feature separation | Checked ref-init output and ordinary Terrum pointer cases retain their own formation, state/default, same-runtime ABI and failure rules. Reject accidental equivalence from pointer size or shared selectors. Required unsupported contracts still reject on no-return paths. |
| NI-FP-17 Admission and truth | Missing 8/10/16 or ordinary 1/2/3/4 closure, unreachable retained uses, current-schema/Development bytes and equal-header older private drafts never erase obligations or select a fallback. Source/bound unsafe removal fails producer verification; mandatory loaded-input checks do not claim foreign/body semantic re-proof. |

The bounded foreign projection, exact comparison, Standard row/Feature,
call applicability and private amendment above are concrete recommendations,
not policy deferred to an implementer. Approval of this bundle and of its
output/selector/address/source prerequisites, implementation through every
affected consumer, and final-input compiler/runtime qualification remain
separate gates. The independent pin allocation and richer retention/ABI
surfaces are excluded, not silently supplied. **No compiler migration,
executed tests, native ABI probe or runtime qualification is supplied here.**
