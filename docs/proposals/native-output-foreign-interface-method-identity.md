# Foreign-interface method identity for NativeOutput

**Status: unapproved; explicitly provisional under the user's authorized
missing-contract workflow.** The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

Subsequent authorized work may use this recommendation provisionally. This
document does not approve syntax or registrations, assign normative IDs,
change specifications, implement a compiler path, or establish support.

## Decision and scope

Register a **nominal, flat, bodyless foreign native-interface contract**.
Select its exact declaring interface and MethodDef before consulting any
NativeOutput facts. Derive dispatch from that interface's complete ordered
method declarations, committed target layout and each method's effective
NativeCallable convention. A slot is a consequence of this binding, not an
interface or method identity.

Recommend one new source registration, `ForeignNativeInterface`, and one
small required metadata record. Reuse InterfaceDef, InterfaceMethod,
MethodDef, MethodSig, Param, Import/Export and NativeCallable. Use a narrowly
defined type-qualified invocation with an explicit receiver argument; do
not introduce a stored interface descriptor, owner, acquisition operation,
general witness dispatcher or duplicate method-signature registry.

[Scalar and pointer native-output cells](native-pointer-output-ownership.md)
remains the semantic owner of eligible scalar/pointer/aggregate T, result
conditions, vacancy/reservation/publication, source dependencies, failures
and resource boundaries. Its
[aggregate](aggregate-native-outputs.md),
[source-dependency](aggregate-native-output-source-dependencies.md) and
[persistence](aggregate-native-output-private-persistence.md) prerequisites
remain provisional. This proposal supplies only the previously missing
**foreign-interface declaration identity and dispatch binding**.
It changes none of those output choices.

### Baseline and the genuine gap

These are direct source observations, not results from compilation or native
qualification performed for this document:

| Owner | Current boundary |
|---|---|
| [Native declaration roles](../specs/native.md#native-declaration-roles) | NativeImport is a static foreign-symbol binding. NativeCallable supplies an effective convention, not a vtable or foreign-interface identity; the exact source/Core convention schema is separately proposed in [NativeCallable source schemas](native-calling-convention-source-schema.md). NativeExport is a separate OS-symbol role. |
| [Interface layout](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations) and [target member ABI](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack) | A native interface needs both a stable target layout and effective callable contracts. Inheritance/adjustments are target-specific. The conceptual interface `Layout(Blittable)` commitment is not a selected foreign-interface source registration: the [registered storage-layout schemas](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) explicitly exclude interfaces. |
| [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.NativeVTable.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.NativeVTable.cs) | The prototype decodes `NativeVTableMethod(slot)` text and takes the first explicit pointer as receiver. Binding rejects NativeOutput there without an independently admitted interface method contract. |
| [`NativeVTableCallContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeVTableCallContract.cs) | The prototype record contains slot, vptr byte offset, parameter annotations and an old output vector. Its validity gate requires that output vector to be empty. It contains no nominal interface/member binding. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`NativeOutputContractValidator.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputContractValidator.cs) | `ForeignInterfaceMethod = 2` exists in memory, but the inspected binder produces DirectImport and the validator requires DirectNativeImport and rejects vtable declarations. The enum is prior art, not source or wire admission. |
| [`SemanticVerifier.DirectNativeImport.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.DirectNativeImport.cs) | Output helper lookup mentions NativeVTableMethod. That routing neither establishes an admitted declaration role nor supplies receiver dependency support. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`NativeEmitter.NativeVTable.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.NativeVTable.cs) | Private expected-vtable checks compare numeric slot/offset and parameter facts; output-role validation accepts only DirectImport. Lowering dispatches the prototype's numeric slot through its explicit receiver. Neither supplies the missing identity graph. |

The [implementation inventory](../implementation.md) describes private and
experimental vtable paths, including incompatible prototype TRRM assignments.
Those are not the standard InterfaceMethod/NativeCallable contracts and do
not admit this role.

### Alternatives and recommendation

| Candidate | Consequence |
|---|---|
| Amend `NativeVTableMethod(slot)` on a top-level/static raw-receiver stub with exact interface and method references | Viable only with an independently registered complete interface, exact member reference rather than a name string, duplicate-signature agreement and an explicit stub-to-member receiver/formal map. Adding a slot, IID, signature hash or nominal-looking string alone is insufficient. Once complete, this creates an extra declaration and adaptation boundary without removing the registration prerequisite. |
| Register a typed foreign-interface declaration and invoke its selected member directly | Its existing method declaration owns the signature, output contracts and implicit receiver identity. One layout-role record completes the bounded foreign dispatch projection; no forwarding stub or second output authority is needed. |

**Select the second candidate.** This is substantive relative to the
prototype but smaller as a complete contract. The prototype remains
ineligible for NativeOutput; there is no automatic upgrade, compatibility
alias or inference from a matching numeric slot.

## Candidate source contract

All additions in this section are **unapproved**. Resolve
`Terrum.Interop.ForeignNativeInterface` through the selected Core
registration. Matching qualified text or a user attribute does not supply
that identity; aliases may denote the exact registered identity.

The schema is primary-type-contract-only, nonrepeatable, and applies only
to a nominal interface. It has exactly one required **named**
`VTableByteOffset: uint32` constant argument, including an explicit zero.
There is no IID, provider filename, slot count, ownership option, default
layout or positional overload. Wrong types, missing/duplicate/unknown
arguments, secondary-part authorship and wrong targets diagnose.

The registration commits the bounded foreign dispatch layout below. It is
the proposed concrete foreign-subset spelling for the conceptual stable
interface-layout commitment, **not** permission to apply the struct
Blittable/Layout schemas to interfaces. NativeCallable remains independently
required; neither half supplies the other.

An eligible interface:

* Is closed, nongeneric and nonpartial, with no self formal, bases,
  associated members, fields, properties, indexers, static members or
  default/helper bodies.
* Declares a nonempty complete ordered list of public synchronous instance
  methods. Each ends with `;`, has no authored body/secondary implementation,
  and explicitly uses the existing `unsafe` modifier. The role independently
  retains caller-unsafety; no NoThrow promise is introduced.
* Does **not** use `extern` on an instance method. Its bodyless foreign role
  comes from the registered containing interface. The accepted static-only
  extern/NativeImport rules remain unchanged.
* Gives every method one effective NativeCallable convention through the
  existing interface default or method override. Publish that effective
  contract on the exact MethodDef, not only on its interface.
* Uses independently admitted complete native types, access/modes, layouts,
  dependencies and target mappings. Method generics, variadics, async and
  unsupported crossings are excluded. NativeOutput is permitted only on
  the cell proposal's exact eligible explicit formals and result types.

Declare **every** slot in order, including methods without outputs. There
are no opaque holes, reserved slots or inferred inherited prefixes.
Method n occupies slot n in this flat target projection. NativeVTableMethod
is neither needed nor admitted as a second slot authority on these methods.
Ordinary overloads still select distinct exact MethodDefs under the existing
call rules; return type and output annotations do not create overloads.

This registration is a protocol identity, not a value-representation
registration for I. It enables no `I` value, `ptr<I>` receiver conversion,
`new I`, cast, witness, conformance, interface upcast or checked acquisition.
Flat declarations may explicitly describe an externally inherited vtable,
but their methods are this declaration's own identities; that does not
establish a Terrum base relation or a release/AddRef protocol.

### Proposal-only fragment

This is not currently supported or runnable syntax. The selected Core owns
all attribute identities. The synthetic foreign provider promises the
already-proposed cell contract; its name selects no compiler behavior.

```terrum
[Terrum.Interop.ForeignNativeInterface(VTableByteOffset: 0)]
[Terrum.Interop.NativeCallable]
public interface ICellReader
{
    [param(value): Terrum.Interop.NativeOutput(
        When: Terrum.Interop.NativeOutputCondition.NonNegative)]
    public unsafe int32 TryRead(write ptr<uint32> value);
}
```

Given an independently established, non-null `ptr<void> receiver` satisfying
this exact interface/provider contract, the proposed invocation inside a
statement scope is:

```terrum
unsafe {
    init uint32 value;
    int32 status = ICellReader.TryRead(receiver, &init value);
    if (status >= 0) {
        uint32 observed = value;
    }
}
```

`I.M(receiver, arguments)` is a **new binding applicability** of the existing
qualified-call grammar, only for this registered foreign role. It is not a
static method declaration or a relaxation for ordinary interfaces.
The first supplied argument must be an unlabelled ordinary receiver
expression; it has no default, `init` or `&init` form. Remaining arguments
map to M's explicit formals under ordinary label/overload rules.
Resolve I, then M within that exact owner, and retain the selected
declaration. A later safety/output failure cannot retry another overload,
interface, extension or slot.

The declaration asserts an external provider protocol. The raw receiver
argument does not prove that protocol. Calling it is an authored unsafe
assertion that an independently established receiver implements this exact
nominal contract; a false assertion is not repaired by successful dispatch.
An acquisition using an IID must fix or validate it in an independently
admitted authored boundary. Passing a caller-selected IID and `void**`,
casting bits or choosing I in source does not establish a checked typed
interface, ownership or lifetime guarantee.

## Exact identity and record candidate

Use the existing nominal identity and import relation:

1. A separately referenced interface retains one TypeDef Import selecting
   one Module identity/export key and one MethodDef Import for every declared
   method. Resolve them to that provider's TypeDef/InterfaceDef and distinct
   MethodDefs owned by exactly that interface. Local declarations bind
   directly. A method key is not a member-name lookup or a slot ordinal.
2. The InterfaceMethod row identifies the directly declared member and its
   declaration ordinal. Its MethodDef supplies the sole complete MethodSig,
   effective NativeCallable, caller-unsafety and output/dependency contracts.
3. The foreign layout record and exact target supply dispatch. The member
   ordinal yields its slot only **after** identity and contract resolution.

Public local declarations can be published with existing semantic Export
keys; keys are explicitly selected by the module producer, not reconstructed
from source spelling. An imported expectation is not a new nominal type or
a re-export. Source lookup names, parameter labels, table indexes, equal
signatures and equal slot numbers do not replace the selected identities.
Two independent declarations of the same external ABI remain distinct unless
an independently admitted explicit adapter establishes the required relation.

### Proposed standard metadata delta

Recommend required section **`0x004C`, ForeignNativeInterface**, policy
`0x0000`, fixed stride **12**, with this exact candidate row:

```text
u32 ownerInterfaceDef
u32 targetProfileRef
u32 vtableByteOffset
```

**This is an unapproved numeric candidate, not an allocation.** The inspected
registry assigns through FieldPath `0x004B`; no inspected proposal claims
`0x004C`. Keeping InterfaceDef/InterfaceMethod and the native family intact
avoids a broader table reorganization. Adoption still requires a combined
registry/catalog/chapter change and exact draft agreement.

`ownerInterfaceDef` and `targetProfileRef` are nonzero typed row references
to InterfaceDef and TargetProfile, respectively: little-endian u32 with
one-based `1..0xFF_FFFF` row and reserved-zero high byte. The offset is a
full-width unsigned integer, **not** a reference or derived object-field
offset. There is one record per foreign interface in this bounded,
single-target candidate; duplicates reject even when identical. Canonical
writers order rows by ownerInterfaceDef. Ordinary readers accept valid other
row orders; ownership and references, not order, determine their meaning.

The target is exactly the existing registered `windows-x64-native/1`
profile, `(profileId, ruleRevision)=(1,1)`. It selects the flat projection
defined below, not an arbitrary C++ ABI discovered from a platform name.
Other profiles need their own complete mapping and are outside this slice.

Reuse these assigned records without adding parallel identity carriers:

| Record | Candidate invariant |
|---|---|
| InterfaceDef/TypeDef | Exact nominal owner; all excluded generic/base/associated ranges are empty. This role has no instance payload Layout row or invented interface storage size. |
| InterfaceMethod | Complete exclusive method range, contiguous declaration ordinals `0..count-1`, each naming one distinct instance MethodDef of this owner. That ordinal derives the flat slot; it is not independently an identity. |
| MethodDef | Body row zero, no static/import/export native role, exact owner and sole signature/contract ranges. A generated dispatch implementation is not an authored semantic body. |
| MethodSig | Nonzero receiver TypeSpec, exactly non-null `ptr<void>` for a readonly method or `write ptr<void>` for a `mut` method. Explicit Param rows exclude this receiver. This is a role-specific opaque receiver projection, not a stored I carrier. |
| Param | Existing ordinary mode and exact complete type/access. Output formal n remains explicit ordinal n, never n+1 or a slot ordinal. |
| Contract | Existing effective NativeCallable `0x000C` and caller-unsafe Effects, plus the cell proposal's shared OutputState/Dependency amendments and result selectors. No duplicate convention or new native-output table. |

The source `mut` receiver selects the writable opaque projection; there is
no readonly-to-write upgrade. Receiver types/authority are exact contract
dimensions even where their machine addresses are identical.
The role is derivable from the checked owner/method relationship, not an
Attr row, serialized authorization Boolean or the in-memory enum value 2.
ForeignInterfaceMethod output admission requires the complete relationship;
a role tag alone remains invalid.

The machine registry already registers **NativeInterfaceCalls `5/1`** with
NativeBoundaryContracts `1/1` and RootlessNativeValues `2/1` prerequisites.
Recommend integrating this bounded foreign declaration/dispatch scope into
that family under the exact amended, unreleased draft, rather than
allocating another interface Feature. Output-bearing methods additionally
require the cell proposal's NativeOutputCells/result-selector capability
closure, including all of those owners' prerequisites. Existing native
Feature registration alone does not supply these missing records or approve
the output candidates. No new opcode, TypeSpec family or convention is
proposed.

Producers must publish schema-valid, complete owner/member/contract records
and truthful external assertions. Source compilation checks the authored
registration, declarations and caller states; it does not inspect a native
provider or ordinary helper body to infer identity or output promises.
Before affected use, consumers check framing/counts, reference namespaces,
exclusive ranges, owner/member/import correspondence, bodyless role,
receiver/signature/convention consistency, all required capability/target
support and retained bindings. Uncalled retained declarations still count.
Unsupported or missing required facts reject rather than becoming an empty
output/dependency collection. Consumers need not semantically re-prove
producer-published facts or arbitrary foreign behavior.

## Receiver, dispatch and output mapping

### Flat layout and provider obligations

For receiver address R and registered offset O:

1. R designates the exact live interface subobject promised by the foreign
   contract, not an arbitrary containing object.
2. Read one native vtable pointer at `R + O`. O is a nonnegative multiple
   of eight; the caller/provider establish readable, suitably aligned,
   stable backing through at least `O + 8` bytes.
3. The vtable has at least `InterfaceDef.methodCount` readable eight-byte
   entries. Entry n is the callable for the **resolved member n's** full
   signature, convention and behavior, not merely a non-null address.
4. Invoke that entry with **R unchanged** as the native member receiver.
   O locates the vptr; it is not a receiver adjustment. No base search,
   offset-to-top, IID query, RTTI, hidden context or thunk is inferred.

Check offset/count arithmetic and represented extents without truncation.
The target/backend must implement the selected address/call mapping or
decline affected use; current emitter displacement limits are not new
language limits. Declared ordering/layout must match the external provider.

The foreign provider and unsafe boundary establish actual object validity,
receiver access, vptr/table stability, compatible callable entries and
required native implementation/code-provider lifetime through invocation.
Receiver mutation/release, table replacement, concurrent access or unloading
cannot invalidate those obligations. These are not proved by pointer bits,
semantic-module identity, a matching method name or an OS load.
Statically known contradictory provenance/access/lifetime facts reject;
arbitrary lying foreign implementations remain outside mandatory consumer
proof. No pin, retain, lease, rooting or resource right is acquired here.

The **semantic declaration provider** is the selected Module/definition
publishing this interface contract. The **executing native provider** is
the implementation reached through this receiver. They need not be the same
image or a fixed DLL. Multiple native implementations may satisfy the same
declared protocol, but each must supply its actual backing/code dependencies.
A same-named DLL or another semantic module cannot replace either relation.

### Logical versus physical positions

Capture the receiver once before evaluating explicit arguments. Stage
arguments/reservations once in source order; labels only change their formal
map. Later arguments cannot invalidate required receiver/code/backing facts.
Load the vptr and selected entry at Invoke, not during source lookup or by
hoisting the foreign reads before argument effects.

| Domain | Mapping |
|---|---|
| Logical receiver | Separate receiver identity; `this` in a dependency recipe selects it, not explicit formal zero. |
| Logical output | NativeOutput on Param n and its published inner T; labels resolve against that declaration. |
| Lowered private parameter array, if receiver-first | Receiver at position 0; explicit formal n at position n+1. Apply this conversion explicitly, including output/source ordinals. |
| General CallSite | Exact MethodMap MethodDef/Import target, not a captured Register/Home callable. Input 0 is the completed receiver, then completed explicit inputs in formal order. Receiver uses copy/read of its raw descriptor, not owner transfer or initialization. Existing output-reservation inputs retain the cell proposal's source-use/phase rules. |
| Windows x64 native member ABI | Follow the effective member convention, not an arbitrary static explicit-receiver signature. For Unmanaged/Cdecl/Stdcall, this is first, then any required hidden result pointer, then explicit formals. Output-bearing methods have the existing int32/uint32 result and no hidden aggregate result. |

When explicit formal zero is an output, its address uses RDX for these
output-bearing methods, not RCX; later scalar/floating/stack positions use
the target's shared physical ordinal. T's cell extent/alignment is still
independent of its address carrier. Keep distinct convention identities even
when Windows maps them to the same physical sequence. Vectorcall and
receiver/base adjustments are excluded from this initial projection rather
than guessed.

For output dependencies, preserve the source-dependency owner's separate
receiver source and staged explicit formals. `DependsOn(this)` can retain
only independently established receiver backing/capabilities; it cannot
borrow a temporary pointer-descriptor slot or manufacture a typed pointee,
owner or provider hold. Empty output sources do not erase independent
receiver/code obligations. No raw output becomes a source.

### Failure and cleanup boundary

Identity/schema/provider admission precedes affected use. Required
pre-invocation checks and failed staging cancel only unentered reservations
under the cell contract; they do not retry another interface/provider.
Enter Invoke before the first potentially foreign dispatch read. An admitted
failure from that point, including a vptr/entry read or native call failure,
uses the existing exceptional indeterminate-state rule, never a fabricated
false result, normal vacancy, readable output or completed release.

On normal return, capture one result and use the exact selected member's
output relations at the existing Publish phase after required checks and
phase cleanup. A local dispatch adapter introduces no second reservation,
result conversion or publication. Native failure transport remains separately
gated; no general NoThrow or foreign-unwind permission follows.

The output owner retains its complementary-outcome provider settlement and
resource-wrapper publication-interval restrictions. This identity proposal
does not relax them. It supplies no synthesized Cleanup, COM reference,
resource adoption, failure-live output, release retry or exceptional cleanup
policy. Authored wrappers remain responsible under their independent
contracts.

## Binding, replacement and persistence

Apply [binding and replacement](../specs/representation/binding-and-replacement.md)
to the **original complete retained expectation**, not facts reconstructed
from the candidate replacement:

* Resolve the selected owner/member before comparison. Preserve both selected
  declaration identities and every independently retained import expectation,
  including declaration-only dependencies.
* Compare exact foreign role, complete ordered interface method set and
  correspondence, target/profile, vptr offset, derived slot, receiver
  projection/access, signature, effective convention and required layout.
  Keep the shared behavior/dependency satisfaction rules and the private
  output owner's stricter exact normalized output comparison.
* Adding/reordering/removing a slot, changing its selected member, or
  changing convention/receiver/layout is not compatible merely because a
  particular caller uses the same slot number or equal ABI carriers.
  A fully rebuilt consumer is not evidence that an unchanged precompiled
  consumer can accept that change.
* Preserve each output's exact explicit-formal correspondence, complete T,
  selectors/states, source recipes/refinements, formation and publication
  obligations. Do not copy stronger replacement facts over the expectation
  before checking it.
* Remap decoded TypeDef/InterfaceDef/InterfaceMethod/MethodDef and Import/
  Export references, MethodSig/Param/TypeSpec and Contract ranges, profile
  references, method maps, receiver/input maps, reservation Homes, native
  fixups and source maps under their owning contexts. Rebuild ranges,
  indexes, extents and checksums; never substitute numeric rows or bytes.
  Clone shared structural rows when different using contexts would diverge.

Source-free contract artifacts retain the complete interface method/layout
closure and each selected expected owner/member contract. A foreign method
has no standalone code address or OS export: it is not a NativeImport,
NativeExport or the
[direct-import provider thunk](native-output-direct-import-provider-binding.md).
Consumers lower this admitted dispatch locally from the retained declaration.
They must not redirect a semantic import to a similarly named native symbol,
pretend an abstract method has a provider PE export, or add it to the direct
foreign-expectation table. The executing receiver's code-provider lifetime
remains independently required.

Private semantic modules, reference projection, native package/set/graph
embeddings and process-start JIT must retain these same facts and original
expectations wherever this role is used. Existing numeric expected-vtable
records cannot encode the missing nominal owner/member relation. The
declaration-owned output collection may reuse its proposed foreign-method
role only after that relation is independently representable and checked;
do not add another scalar/pointer/aggregate output codec.

**Private interface persistence is a prerequisite, not supplied by this
standard record candidate.** The private interface/type/method identity
carrier and composed revision must be defined and completed explicitly before
that path can accept or emit this role. Until then, reject affected private
persistence/native-container use, including declaration-only requirements.
Neither current private output revisions nor outer version 3 selects the
new schema. This proposal does not choose a private revision number or
mislabel prototype `.trrm`/`.trmn` bytes as standard interchange.

For standard interchange, adopt the exact record/capability and CallSite
applicability amendments together. The
[format-version owner](../specs/representation/format-versioning.md) allows
draft changes without a new format pair, but requires out-of-band agreement
on the **whole exact draft**. Equal `(1,0)`, Feature 5, native container
headers or plausible records do not select it. No alias, header patch,
historical decoder or fallback interpretation is recommended.

## Integration owners and outstanding gates

These are future amendments, not changes made to normative owners here:

| Owner | Narrow integration |
|---|---|
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [names/binding](../specs/language/names-and-binding.md#qualified-and-member-lookup), [calls](../specs/language/calls-and-inference.md) | Register the one attribute and bodyless role; add only the described type-qualified explicit-receiver call applicability. Keep ordinary extern, interface/witness and source-order rules. |
| [Native roles](../specs/native.md#native-declaration-roles), [interface layout](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations), [Windows member ABI](../specs/targets/windows-x64-call-interface.md#3-physical-argument-positions-and-stack) | Supply this concrete foreign-subset layout commitment and flat target projection. Preserve NativeCallable's independent signature role; no general storage-interface ABI or inheritance mapping is superseded. |
| [Metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry](../specs/representation/registry.md), [machine registry](../specs/representation/trrm-1.registry.json), [requirements](../specs/representation/requirements-and-admission.md), [logical records](../specs/representation/logical-records.md) | Integrate the candidate required section, existing interface/method identities and exact Feature 5 scope/closure. Output records remain owned by the output proposals; no new semantic Attr or trust flag. |
| [General CallSite](../specs/il-instruction-set.md#general-call-site-record), [reader trust](../specs/representation/reader-trust.md), [binding/replacement](../specs/representation/binding-and-replacement.md) | Specify receiver-first input correspondence for this role, exact owner/member/layout checks, context-aware remapping and retained expectations before use. |
| [Native boundary cases](../specs/conformance/native-boundaries.md) and private persistence owners | Add the cases below and complete independent private representations before claiming that consumption scope. Keep NBP-04's current rejection until the output schema is separately selected. |

Implementation must add actual source/metadata binding, receiver-aware bound
calls, shared traversal/rewriting, mandatory role/contract validation,
receiver/output provenance, reference projection, persistence/replacement and
target lowering. Removing the existing binder diagnostic, accepting enum
value 2 or broadening a helper's branch is not that implementation.
The complete cell/selector/reservation and independently required backing,
code-lifetime and target representations are prerequisites throughout.

## Required qualification

These are future acceptance cases, **not passing tests claimed here**.
Use unrelated synthetic, headless providers and public compiler APIs.
Keep source admission, structural consumption, emitted ABI and supervised
native execution evidence separate and tied to the final compiler, selected
configuration and exact inputs.

| Case | Required outcome |
|---|---|
| Flat `ICellReader.TryRead` with a scalar output; unrelated interfaces with pointer and committed aggregate outputs | Select exact owner/member, reserve the cell once and apply the existing output owner unchanged. No API/name recognizer. |
| Several methods and output formals, named argument reordering, a nested later argument and mixed integer/floating/stack inputs | Preserve receiver-first capture, source-order staging, logical formal ordinals, exact member ABI and one publication per invocation. |
| Receiver-sourced pointer output with admitted backing; empty sources on an independent scalar output | Preserve actual receiver backing/code obligations through dependent copies; do not borrow the receiver variable slot or infer a hold/owner. |
| Two interfaces or overloads with equal names, signatures, slot zero or pointer addresses | Remain distinct selected identities. A source-selected valid unsafe declaration is an external assertion, not runtime authentication of its receiver. |
| Numeric NativeVTableMethod, NativeCallable alone, storage Layout/Blittable markers, same-spelled attributes, wrong Core identity or role byte 2 alone | No foreign-method NativeOutput admission. |
| Wrong output target/type/mode, receiver as output, explicit-formal zero confused with receiver, wrong signedness/convention or access upgrade | Reject before affected use; do not reinterpret positions or equal carriers. |
| Missing method, duplicate member/ordinal, gaps, nonempty base/generic ranges, a helper body, static/extern/native-import/export role or unsupported target/Vectorcall | Reject this bounded role. Do not synthesize missing prefix methods, bodies, adjustments or conventions. |
| Out-of-range/wrong-table owner/profile references, mismatched MethodDef owner, duplicate foreign rows, overlapping ranges, truncated row, unaligned vptr offset or overflowing extents | Reject malformed/unsafe-to-consume records, including uncalled retained declarations. |
| Later staging invalidates receiver/table/code backing; conflicts with an output reservation or aliases it during reentry | Preserve existing known-lifetime/reservation rejection and pre-Invoke cancellation; no implicit retain/pin or reordered dispatch read. |
| Normal success/complement, partial foreign writes, ignored/stale discriminator and destination reuse | Exact cell-owner outcomes and generation correlation; no default value, adoption or success for another output. |
| Native dispatch/call failure after Invoke, including required post-return work before Publish | No normal output facts or fabricated vacancy. Preserve exceptional state and existing resource-wrapper/failure gates. |
| DoesNotReturn member with otherwise valid output contracts | Retain the complete contracts and terminal-call rules; no normal publication or cancellation of an entered reservation is inferred. |
| Lying provider uses the wrong interface entry/signature, produces invalid success bytes or transfers a failure resource | A provider/trust violation, not mandatory arbitrary native-code re-proof. A native harness can detect fixture behavior; do not describe structural admission as proof of truth. |
| Separate compilation through explicit interface/member Imports, then row/table reordering and packing | Preserve exact owner correspondence, derived layout and receiver/output/source maps after remapping. No source names, declaration body or debug data is needed. |
| Contract-preserving provider revision with different physical rows/private bodies and additional unrelated exports | Accept when the complete original expectation and target/runtime obligations hold. Adding unrelated exports does not permit extending or reordering this retained flat interface. |
| Replacement changes semantic provider/key, member owner, vptr offset, slot order, signature, effective convention, output condition or dependency | Reject the changed retained obligation even with the same slot/carrier/native DLL. Check original expectations and every declaration-only edge. |
| Source-free reference artifact with all source/debug data removed, followed by AOT/JIT dispatch against an independent native implementation | Admit only the complete exact schema and retained contracts; execute that selected method without source-body inspection, a provider-symbol shortcut or an abstract OS export. Keep the actual native provider live. |
| Private/standard round trip or native set/graph with missing interface carriers/requirements, old prototype rows, equal header bytes or erased expected facts | Reject affected use. Regenerate under a completed exact schema; never clear obligations or guess another reader. |

## Remaining decisions

The recommended **identity, source registration, receiver mapping, flat
dispatch rule and standard record candidate are concrete**. Their formal
approval and registry/catalog integration remain outstanding; the candidate
number above is not assigned by this document.

The private identity baseline is specified, still unapproved, by
[Private persistence of foreign-interface method identity](native-output-foreign-interface-private-persistence.md).
Its candidate-96 identity directory and typed-call design are retained in the
exact revision-97 composition selected by
[NativeCallable convention persistence](native-calling-convention-private-persistence.md),
which moves the foreign member convention into the common declaration token.
Implementation still depends on the complete base revisions and outer-3
foreign-aware draft; neither proposal claims that path is implemented.

Inheritance/adjustments, other target mappings, stored checked interface
values, runtime interface authentication, IID protocols, COM/resource
ownership, callbacks/authored slot implementations and resource adoption are
excluded, not implicitly approved or implementation-defined escape hatches.
