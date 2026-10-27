# Standard native-address formation

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This proposal changes no normative owner,
allocation, implementation or stability promise. The authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

## Decision and scope

Recommend one Standard register operation, **candidate `native.address`**:
long primary **`E9`**, zero control, and a Body-local reference to a new
**record kind `0D` NativeAddress**. The record names an actual MethodMap
target, an explicit function-pointer TypeMap view, and a register or vacant
local Home result. It forms a value; it does not invoke the target.

`NativeAddress` here names the native-boundary operation, not a new source
keyword, intrinsic spelling or overload-resolution rule. This decision covers
already admitted environment-free native addresses. It does not make an
ordinary Terrum method native-addressable, bind a receiver, create a closure,
or select an ABI adapter.

The separate unapproved [source-contract decision](standard-function-pointer-source-contracts.md)
owns source defaults, inferred pointer contracts and checked projection;
the [anchor decision](standard-function-pointer-anchor-contracts.md) owns
applicability, structural identity and rebasing. This operation records their
already selected view rather than deciding it again. The separate
[default-convention decision](standard-function-pointer-default-convention.md)
proposes TypeSpec-only tag zero; it neither widens NativeCallable/NativeImport
nor makes a foreign NativeAddress a default-convention pointer. Under the
current normative schema, native conventions remain `1..4`. No convention
domain or function-pointer source identity is amended here.

### Why this is a contract gap

| Checked evidence | Consequence |
|---|---|
| [Instruction calls](../specs/il-instruction-set.md#calls), record `02` and opcodes `80..83`, `90`, `E0..E2` | `call.method`/`call.value` select invocation carriers and execute the call protocol. None selects address construction. A zero-input or unit-returning call still invokes. |
| [TypeSpec `0D`](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Its seven-byte row identifies a complete pointer contract through a bodyless MethodDef anchor. The anchor is explicitly neither callable nor addressable. |
| [ConstantMap](../specs/il-instruction-set.md#explicit-typed-constantmap) | Nonliteral addresses/relocations need their own admitted constant contract. Existing scalar/frozen payloads cannot encode this operation by storing pointer bits. |
| [Indexed producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Indexed.cs), [Body reconstruction](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Body.cs) | The Development path emits operation `31` with a method target and reconstructs a BoundNativeFunctionAddress under its indexed unsafe-region contract. This is not a Standard register assignment. |
| [Executable artifact producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) | It writes Development signature/native-callable sections and selects `EncodeDevelopment`; that route is not evidence of Standard formation. |
| [Register map validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs) | Local MethodMap validation rejects anchor MethodDefs, directly and through Import expectations. Reusing an anchor as the missing code target would violate an existing check, not complete the codec. |
| [Type reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeSpecTableReader.cs), [structural metadata validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmStructuralMetadataValidator.cs) | Standard seven-byte anchors, role validation and declaration/value Feature checks are distinct from Development six-byte signature references. They do not provide a formation instruction. |
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [bound address](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), [native lowering](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) | The current bounded source path forms an expected-type function address with lexical unsafe evidence. Lowering uses actual function fixups and import jump thunks, not a signature anchor. This establishes implementation mechanisms, not their Standard encoding or complete contract projection. |

The current [signature matcher](../../sources/Terrum.CodeAnalysis/Semantics/NativeFunctionPointerContract.cs)
also excludes DoesNotReturn and represents a narrower signature than the
companion complete-contract design. Those prototype restrictions are not
candidate operation semantics. Test names containing “Standard” do not
override the producer's selected Development schema.

## Owners and intended amendments

The routes in [the specification index](../specs/README.md) remain authoritative.
If adopted, make these additions in those owners; this document makes none.

| Owner | Proposed change; preserved boundary |
|---|---|
| [Machine registry](../specs/representation/trrm-1.registry.json), [registry chapter](../specs/representation/registry.md) | Add only long `E9`, record `0D`, their exact shapes/domains and admission rules below. Preserve the mixed codec, existing assignments and distinct reference namespaces. |
| [Instruction set](../specs/il-instruction-set.md#typed-operand-records) and its opcode/allocation/family tables | Add the record, value-producing operation, result and ordering contracts. Add native-address formation as a family entry; leave Calls as invocation. |
| [Metadata](../specs/il-metadata.md#body-table-and-coded-instruction-boundary) | Document the new record's map references and actual-target/anchor distinction. Keep Body framing, MethodMap/TypeMap tokens, `0D`, MethodSig/Param and native declaration rows unchanged. |
| [Native boundaries](../specs/native.md#native-declaration-roles), [native cases](../specs/conformance/native-boundaries.md) | Connect NativeAddress to this carrier and add formation cases. Preserve declaration eligibility, exact conventions, direct representation, caller-unsafety and code/provider lifetime. |
| [Manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Specify detection of retained `E9`/`0D` uses under existing `4/1`, with complete closure. No new Feature or reduced execution profile. |
| [Binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md), [IL cases](../specs/conformance/il-registry.md) | Add use-specific addressability/view checks, schema-owned references, remapping and independent cases. Preserve producer truth versus mandatory safe consumption. |
| [Grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments), [callable families](../specs/type-families.md#callable-values-and-tuples), companion proposals | No new syntax, receiver marker, inference policy, contract equality or convention tag. |

## Allocation review and alternatives

The complete current first-byte namespace in
`trrm/1-draft-layout-queries-1` was checked, including the shape-only cells.
All multibyte fields remain little-endian; there is no prefix or escape.

| Partition | Current allocation and all unassigned primary ranges | Candidate effect |
|---|---|---|
| Tiny `00..0F`: 16 cells | All 16 assigned | None |
| Short `10..BF`: 176 cells | 91 recognized, including shape-only `B6`. Unassigned: `1B..1F`, `2C..2F`, `37..3F`, `42..4F`, `53..5F`, `68..6F`, `84..8F`, `93..9F`, `B9..BF` | None; 85 cells remain unassigned |
| Long `C0..FF`: 64 cells | `C0..CC`, `D0..DE`, `E0..E8`, `EA..EB`: 39 recognized, including shape-only `EA`. Unassigned: `CD..CF`, `DF`, `E9`, `EC..FF` | The [aggregate-output candidate](aggregate-native-outputs.md#candidate-assignment-summary) already uses `DF`; assign `E9` here. Together the candidates recognize 41 cells and leave 23 unassigned. |
| Record kind `u8`: 256 cells | `01..0B`: 11 recognized, including RegionExit `0A` with no usable exit domain; `00` and `0C..FF` unassigned | The aggregate-output candidate already uses `0C`; assign `0D` here. Together the candidates recognize 13 kinds and leave 243 unassigned, with `00` still invalid. |

`B6`/`EA`/`0A` still do not supply ExceptionRegion or usable exit-kind
semantics. Existing family-control gaps are invalid, not free secondary
instructions: arithmetic recipes, query controls and call/terminator fields
keep their exact domains. The aggregate-output candidate assigns `DF`; `E9`
assigns only control zero, with its other 255 controls invalid. No short form,
tiny tuple, extra Body implementation kind, width selector or new register
role is proposed.

The Body record kind `0D` is a distinct namespace from TypeSpec form `0D`,
Contract `000D` and other tables. This consumes no metadata section kind,
definition kind, token kind, clause kind or Feature ID. Existing global tokens
remain four bytes with 24-bit rows, and typed MethodDef references retain
their reserved-zero high byte. Section kinds are `u16`, not token kinds;
`8000..FFFF` is nonsemantic extension space, not an address carrier.
`F4..FE` token kinds and reserved `FF` indirection are not consumed.

Development operation `31` cannot be imported numerically: Standard primary
`31` already means short OR. Development section `0033` signature data
likewise collides with Standard WitnessAssociatedConst, not a spare carrier.

| Alternative | Assessment |
|---|---|
| **`E9` plus explicit typed record `0D` — recommended** | Keeps address materialization distinct from the aggregate-output proposal's `DF`/`0C` reservation/cancellation operation. Carries the independently selected type view and full result location without changing invocation. One four-byte instruction and one 12-byte record per distinct plan; existing maps are reused. |
| Share `DF` and Body record `0C` through new operation/record discriminators | Saves one long-primary cell and one record kind but requires changing both operation schemas and validators; one overloaded tag obscures whether the payload addresses a target or reserves an output. Rejected in favor of distinct operation families. |
| Direct `E9`, `control=destination register` with high nibble zero, `payload=u16 MethodMap index` | Cannot state the selected contract view. A register has no permanent destination type; deriving a type from the target loses contextual projection, while deriving it from the anchor loses the actual target. Completing this alternative needs a second typed carrier or projection operation, defeating its apparent simplicity. |
| Extend CallSite `02` | Requires an explicit new operation discriminator and different input/preparation/effect rules. Existing result or zero-argument combinations cannot distinguish formation from invocation. No such overload or discriminator is selected. |
| Add a callable relocation ConstantSource variant | Could support a separately designed frozen/relocatable address contract, but must define constant eligibility, relocation, provider/code dependencies and evaluation modes. Existing ConstantMap is not that design; ordinary formation needs none of this new constant surface. |
| Reclaim `EA` and record `0A` from shape-only RegionExit | This would leave 24 long/244 record cells unassigned rather than 23/243, at the same payload size. Draft IDs may be reassigned without aliases; saving one cell in each namespace does not justify coupling this value operation to a control-family redesign at current capacity. No unpublished reservation is immutable. |

No allocation is frozen by this recommendation. A later coherent repacking
may replace these candidates. Under [draft versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts),
selection requires exact agreement on the amended schema and governing
semantics, even if `(1,0)` and Feature revisions remain unchanged. Current
Standard readers must still reject `E9`/`0D` until that schema is selected.
There are no compatibility aliases, guessed readers or historical shims.

## Exact candidate encoding

The only instruction form is:

```text
u8 opcode = 0xE9
u8 control = 0
u16 nativeAddressRecordIndex
```

It is a four-byte, nonterminal instruction at any valid instruction boundary,
without alignment padding. Its index is zero-based in the owning Body's
typed-record table and must select kind `0D`.

The complete record is exactly 12 bytes:

| Byte offset | Field | Required interpretation |
|---:|---|---|
| 0 | `u8 recordKind=0x0D` | NativeAddress |
| 1 | `u8 flags=0` | Common record flags |
| 2 | `u16 reserved=0` | Common record header |
| 4 | `u8 resultKind` | Existing ResultSelectorKind subset: `1=Register`, `2=Home`; None and other values invalid |
| 5 | `u8 reserved=0` | No authorization, adaptation or resolution flags |
| 6 | `u16 resultIndex` | Register selector or full HomeMap index, as below |
| 8 | `u16 methodMapIndex` | Actual selected callable declaration, never the type anchor |
| 10 | `u16 pointerTypeMapIndex` | Complete selected pointer view |

The reference chain is exact:

* `methodMapIndex` selects a four-byte MethodMap token: kind `16` MethodDef,
  or kind `1C` Import whose expected DefId has kind `16`. It is not a direct
  method row, a MethodSig, a NativeImport section row or a TypeMap index.
* `pointerTypeMapIndex` selects a TypeMap token of kind `11` whose row is
  Standard TypeSpec form `0D`, qualifiers zero. This supplies the complete
  result type, convention and signature/contract anchor. Optional pointers,
  raw data pointers, TypeIdentity and nominal wrappers are not this result.
* That TypeSpec's `signatureContractMethodDef` is the existing nonzero typed
  MethodDef row reference, not a MethodMap token. It remains a noncallable,
  bodyless anchor. It must not be the actual address target.

For illustration only, not a complete artifact: MethodMap[0] may be
`0x16000007` naming actual method 7, while TypeMap[0] is `0x11000002`
naming TypeSpec row 2, whose bytes are `0D 00 02 09 00 00 00`
(Cdecl, anchor method 9). Record[1] publishing R3 is then
`0D 00 00 00 01 00 03 00 00 00 00 00`, and its instruction is
`E9 00 01 00`. Record[0] can supply the separately required BlockEntryState.
All referenced declarations, contracts, ranges and Features must still be
valid; these example row numbers create no allocation.

Every local index value, including `0000` and `FFFF`, remains usable.
Relevant map/record/Home counts are `u32`, at most 65,536, independently for
each namespace. No index is an absence sentinel. Global rows remain
`1..0xFF_FFFF`; code lengths, offsets and instruction ordinals remain `u32`.

## Formation contract

### Actual declaration, selected view and substitution

1. Resolve the actual MethodMap target in the Body's Module context. A direct
   MethodDef must be a permitted local definition, not an Import expectation
   used to bypass binding. An Import retains its exact ModuleRef, export key,
   expected declaration and ordinary directed binding checks. Do not select
   another method by name, shape, row proximity or matching anchor.
2. The resolved target needs an admitted native entry: an actual
   NativeCallable declaration with its effective `000C` convention and usable
   implementation, or a static bodyless callable NativeImport owned by
   `0021` owner kind 1. The latter requires **no additional NativeCallable**.
   NativeExport implies/references its unique NativeCallable, but an OS export
   is not necessary for formation. Data imports, unbound bodyless methods,
   abstract interface requirements and signature anchors are not entries.
   Where independently valid roles coexist, their effective conventions must
   agree; no role-priority fallback resolves a contradiction.
3. Addressability is also a use-specific binding obligation encoded by this
   instruction and view. A generic Import resolving to a NativeImport does
   not need a synthetic NativeCallable clause on the provider. Conversely,
   satisfying the Import's ordinary signature alone cannot satisfy this use
   if the selected provider lacks the required native entry. Preserve any
   explicit role promises in the original expectation as well.
4. Require the exact complete ordered signature/modes and original convention
   of the selected native entry. Equal Windows x64 carriers do not equate
   Unmanaged, Cdecl, Stdcall or Vectorcall. Check target-contract satisfaction
   of the recorded static view under the companion source/anchor decisions;
   never erase required outputs, dependencies, caller requirements or phases
   to make a signature match. A valid weaker view changes no address bits and
   creates no thunk or convention cast. Unsupported implication is not success.
5. This record supplies no receiver, selected-owner construction, method
   type/constant argument list, witness, generic dictionary or environment.
   Require `receiverTypeSpec=0` and method-generic range `(0,0)` on both the
   selected callable signature and anchor. An authored static wrapper can
   expose an explicit receiver Param.
   Closed constructed/nested signature types are allowed under their existing
   contracts. An uninstantiated generic method or constructed declaring-owner
   adaptation cannot be inferred from this bare MethodMap token. Such a use
   needs an already admitted exact closed MethodDef/Import representation;
   this record does not invent one. Substitute every admitted signature and
   contract subject in its defining context before comparison, not from a
   consumer default or sample body. No free formal may remain at a concrete
   native execution boundary, including in nested types or contract subjects.

The anchor describes invocation only. Forming a pointer does not execute
the target, run its preconditions, transfer its future arguments or publish
its outputs. In particular a DoesNotReturn target still has an ordinary
pointer-valued formation operation and normal continuation. Only invocation
through a view retaining DoesNotReturn is terminal.

### Result placement and evaluation order

For `resultKind=1`, `resultIndex` must be `0..13` or `15`; upper register
index bits are zero and R14 is invalid. R15 explicitly discards this
nonowning pointer result, not checks, binding or required resolution effects.
Publishing a general register rebinds that logical name after formation;
it does not retire its previous value or discharge an affine responsibility.

For `resultKind=2`, the index names an exact-type, proven-vacant role-3 local
Home. Formation initializes that Home on success. There is no implicit
replacement, cleanup, source-use conversion or DestinationPreparation list.
An occupied destination uses an ordinary register result followed by the
existing appropriate Home/SemanticStore transition. Pointer Copy eligibility
does not make the destination vacant or grant write authority. A pointer
whose target returns unit is still a non-unit pointer result; None is invalid.

The operation occurs at the source expression's ordered position. The
declaration selection is already bound; there are no runtime receiver or
argument expressions hidden in the record. Resolve the selected entry and
complete required checks before publishing the destination. Link/Dynamic
resolution uses the existing NativeImport binding and target load contract;
this record adds no lazy resolver recipe, search policy or failure transport.
A usable native entry must be installed before a pointer can be published.
Resolution/publication failure follows that admitted contract and publishes
no pointer, never null/default bits or another provider.

Backend fixup or precompilation may happen earlier without executing user
code or changing source observations. The operation cannot be moved across
observable checks/effects merely because its result is an address. Subsequent
`call.value` captures the completed pointer once before evaluating its written
arguments; nested calls, register reuse or later source-variable writes must
not change that captured target. No call instruction recaptures or reevaluates
NativeAddress. Ordinary call result/output publication remains separate.

### Unsafe authorization and lifetime

Formation and native pointer invocation remain independently caller-unsafe.
An authored formation needs an explicit lexical unsafe block/expression;
an unsafe declaration or Effects `04` alone does not authorize its body.
Bound-tree traversal, rewriting and source verification must preserve that
authorization. This record has no trusted-authorization bit, grants no
unsafe scope and authorizes no later invocation or operation inside the
target body. It does not inject/delete Effects `04` on the pointer anchor.

For source-free Standard input, correct lexical placement is a
producer-published Body fact under [reader trust](../specs/representation/reader-trust.md),
not a consumer obligation to recover source from SourceMap. Ordinary readers
must validate required structures, support and bindings, but need not
semantically re-prove source authorization. An optional source/body audit may
check it; a debug location is never authorization.

The resulting value carries the actual code origin/dependency, separately
from its structural type. Equal anchors can describe addresses in different
images. Copying, moving, storing, returning, projecting or merging the pointer
must retain the actual guarded code/provider dependencies, not replace them
with the anchor's Module or the latest forwarding Module.

The raw pointer has no independent environment, root, owner or unload lease.
Formation captures no receiver or argument owner. Invocation-contract source,
retention, backing, output and cleanup relationships still apply when actual
inputs are staged; they are not discharged or acquired by constructing the
pointer. Existing known source/owner validity, provenance and invalidation
constraints survive transport and unsafe use; address bits cannot repair them.

Native code, entry stubs, helpers and unwind registrations remain valid while
any admitted use can reach them. Dynamic NativeImport retains its provider for
the importing Module/image lifetime, without a new per-pointer retain. An
escaped pointer does not extend that lifetime automatically; its holder must
meet the retained lifetime obligation. A pointer to an importing image's
entry stub may depend on both that image and the native provider. Returning
from a factory, dropping an intermediate owner or removing the last direct
machine call does not discharge surviving code/load dependencies.

## Admission and safe consumption

Reuse the registered exact capabilities, not a new operation-specific Feature:

| Retained use | Required capability |
|---|---|
| Pointer contract/anchor and native declaration binding | `1/1 NativeBoundaryContracts` |
| Pointer value/type uses, including a Body TypeMap or Home | `2/1 RootlessNativeValues`, with `1/1` |
| Any retained `E9` instruction or kind-`0D` record, including unused records, discard and address-only imports | `4/1 NativeExportEntries`, with catalog closure `4/1 -> 3/1 -> 2/1 -> 1/1` |
| Callable NativeImport declaration itself | Existing `3/1 DirectNativeCalls` closure; formation additionally requires `4/1` |
| Retained query/TypeIdentity operations on related types | Additionally `13/1 LayoutQueries`, only for its own retained uses |

The Feature name does not require an OS export. Native interface dispatch,
native data access, exception unwinding, predicates, ownership or generic
execution retain their independent prerequisites when actually used. This
operation does not activate an incomplete carrier, broad predicate codec or
unregistered capability. A known convention without a complete target mapping
is valid metadata but unsupported execution, not permission to change tags.

Before affected binding, code generation or execution, consumers must check:

* Exact selected schema, Body extent/mapMask/count arithmetic, block and
  instruction boundaries, record offset/extent, kind `0D`, zero controls,
  flags/reserved bytes, assigned result selector and all local bounds.
* The map token namespaces and rows, using-Module ownership/context, actual
  versus expected MethodDef roles, valid noncallable anchor, complete selected
  TypeSpec/MethodSig/Param/Contract structures and referenced clauses.
* Result Home role and complete type agreement; target entry eligibility,
  convention/signature/view compatibility and every selected Import/provider
  binding. An address-only dependency needs these checks without a CallSite.
* Required Module/dependency manifests, complete Feature and target/runtime
  support, and actual entry/code-publication prerequisites before use.

Reject wrong-kind records/tokens, out-of-range references, callable/addressable
anchor use, invalid roles, malformed extents and incompatible bindings.
Unsupported required semantics decline affected use rather than erase facts.
Unknown `E9` controls, record variants or Development bytes are not alternate
decoders. Inspection may report unsupported requirements without executing.

Producers establish truthful Body facts: source authorization, selected
source contract/projection, register definitions, Home vacancy, flow, owner/
backing/lifetime correctness and cleanup obligations. Consumers perform the
structural and declared binding checks above; they need not re-prove arbitrary
body facts or foreign provider behavior. Correct lowering must preserve the
facts it consumes. False but structurally consumable semantic metadata remains
a producer violation, not a promise of mandatory consumer detection.

## Packing, rewriting and execution

Canonical writers use the sole four-byte form. Typed records and MethodMap/
TypeMap entries follow their existing first-logical-use rules, reuse identical
plans/tokens where valid and omit unused entries. Home identities remain
distinct. The plan's identity includes actual target, complete selected view
and result selector, not merely the anchor. Ordinary valid reordered or
duplicate entries remain admissible; canonicality auditing is separate.

Decode and remap each namespace independently: record index, method-map index,
type-map index and optional Home index; then global MethodDef/Import targets,
TypeSpec rows and typed anchor references. Rebuild MethodSig/Param/Contract
ownership/ranges, selector subjects and complete substitutions under the
companion anchor rules. Structural anchor deduplication must not merge actual
method identities. Shared rows with different resolved Module meanings need
context-preserving clones, not reinterpretation.

Packing retains each Module and every expected binding, including native
addressability and stronger provider facts actually used. Do not turn a packed
Import into an illegal cross-Module direct MethodMap token; keep the Import
when the destination reference rules require it. Any permitted direct-binding
rewrite still retains the original expectation and code/provider obligations.
Trimming a call or changing a contract view does not by itself remove an
address-only provider or its native binding.

Rebuild map masks/counts, record offsets, block offsets/lengths, instruction
ordinals, Body/section extents and affected SourceMap slots. Existing SourceMap
step role `0` identifies the whole formation operation; no new role is needed.
Changing placement/width is a typed re-encoding, never substitution of matching
opcode bytes in literals, rows or indexes.

AOT emits a real target-entry relocation/fixup; JIT resolves or publishes a
stable native entry for the selected declaration before exposing it. An
ABI-equivalent import jump stub is a backend implementation of that entry,
not a receiver/signature adapter. Never return an IAT cell's address, an
anchor address, a process address serialized as scalar bits, or an unresolved
placeholder. The selected convention controls subsequent invocation.

Code publication follows the selected target's write/execute, cache-fixup,
unwind and lifetime rules. A replacement must satisfy the original expected
contract plus the addressability/view and other retained dependencies.
Semantic relowering, unchanged precompiled consumers and live replacement
remain distinct. Updating an Import does not patch escaped pointers; old code
cannot retire while they can still use it. No hot-unload protocol is added.

## Proposed acceptance cases and completion

These are implementation requirements, not executed results. Keep syntax,
source semantics, malformed decoding, binding, emission and supervised runtime
evidence distinct. Use unrelated synthetic methods/providers, not API names
or application-pattern recognizers.

| Case | Required outcome |
|---|---|
| SNA-01 Local entry | Form an unexported static NativeCallable address, store/pass/return it and invoke it. No OS symbol or extra NativeExport is necessary. A method-form NativeExport uses its existing unique `000C`. |
| SNA-02 Target versus anchor | Two behaviorally different methods share an equivalent pointer view/anchor. Preserve which method was selected through round-trip and execution. Reject addressing either anchor directly or through an Import; permit its TypeMap use. |
| SNA-03 Callable import | Form callable NativeImport addresses without NativeCallable under Link and Dynamic. Preserve exact library/entry/policy and provider lifetime. Retain the native import when no direct call survives. Reject a field-form NativeImport as this method target. |
| SNA-04 No invocation | A target with observable effects is not executed by formation. A non-unit DoesNotReturn target forms a pointer and continues normally; a subsequent call through that no-return view uses terminal CallSite rules. |
| SNA-05 View/convention | Exercise exact and separately admitted weaker views. Reject signature/mode/convention mismatch and false output/dependency/no-return upgrades. Preserve distinct convention identities even with equal carriers; no projection to tag zero follows from this proposal. |
| SNA-06 Placement/order | General-register and vacant local-Home results agree. None, R14, oversized register selectors, wrong Home type/role and producer-invalid live-Home initialization fail their respective checks. R15 still requires binding/resolution. Nested argument calls and register reuse preserve a once-captured target and source evaluation order. |
| SNA-07 Malformed carrier | Reject nonzero instruction/header/record reserved fields, wrong record kind, truncation/trailing bytes, absent maps, invalid token kinds/rows, MethodSig-as-target, anchor-as-target, optional/data-pointer-as-view and inconsistent TypeMap/Home references. |
| SNA-08 Capacity | Exercise indexes 0, 15, 16, 255, 256, 65,535 and counts 65,536 independently for records/maps/Homes. Reject 65,537, checked-extent overflow and one-past-end references without wrapping. Validate highest representable in-table global rows; zero/high-byte-set typed anchor references remain invalid. |
| SNA-09 Unsafe | Remove a source/bound unsafe scope or forge its retained authorization: source verification rejects formation/invocation. An unsafe method modifier alone is insufficient. Loaded Standard structural checks do not claim to reconstruct lexical source or prove foreign behavior. |
| SNA-10 Eligibility/generics | Reject unbound bodyless, abstract, receiver-bearing, method-generic and unrepresented constructed-owner targets. Exercise admitted closed constructed and nested pointer signature types without inventing a receiver/environment or guessing substitutions. |
| SNA-11 Cross-feature contracts | Preserve supported initialization-output correlations, declared Effects, dependencies, ownership/access/cleanup and unit logical positions through formation and later invocation. Compose with nested/optional pointer storage through its ordinary operations, not an optional result from `E9`. Missing required carriers/support fail admission, including on no-return paths; scalar prototype restrictions do not replace the admitted target contract. |
| SNA-12 Provenance/lifetime | Transport through fields, copies, joins, forwarding Modules and results preserves guarded actual code/provider origins. Reject known use after owner/provider invalidation in source verification; runtime code retention follows admitted obligations without a new pointer lease/root. Keep an import stub's own image live as well as its provider. |
| SNA-13 Feature closure | Require `4/1` for address-only/discarded formation, retained unreachable instructions and unused `0D` records, with its exact closure; `1/1` or `2/1` alone is insufficient. Keep each dependency Module's manifest. Require `13/1` for retained queries, not merely for common Body framing. |
| SNA-14 Packing/replacement | Permute/deduplicate records/maps and remap global rows, anchors, clauses, substitutions and source slots. Preserve distinct actual targets and all Import expectations. Reject a replacement lacking native entry, changing convention or weakening retained output/backing/no-return facts. |
| SNA-15 Source-free execution | Produce the selected Standard schema, reload without source, form and indirectly call local and dependent-provider entries under AOT and JIT, standalone CLI and separately supplied dependent-runtime consumers. Verify actual returned behavior, argument/result ABI staging and code-origin retention; emitted bytes alone are not runtime evidence. |
| SNA-16 Schema separation | Current-schema `E9`/`0D` and Development `31`/signature rows are not compatibility input. An explicitly selected amended draft decodes only its exact assignments; changing format bytes or a Feature list does not choose it. |

Implement the selected admitted route through binding, shared bound-tree
rewriting, verification, Standard production/consumption, dependency loading,
packing and AOT/JIT lowering. **A blanket unsupported diagnostic or permanent
Development fallback is not completion.** Genuine independent carrier/target
prerequisites remain admission gates, not permission to omit this operation
from the planned implementation.

The operation's numeric choices, complete record, target/view relationship
and result protocol are selected here as **unapproved provisional candidates**;
none is left for an implementer to infer from CallSite or a prototype.
Normative adoption and the companion decisions' own outstanding approvals
remain separate. This documentation change supplies no compiler migration,
execution qualification or released-schema compatibility claim.
