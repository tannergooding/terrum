# Private persistence of foreign native interfaces

**Status: unapproved; provisionally actionable only under the user's authorized
missing-contract workflow.** That workflow permits implementation using the
recommendation as provisional direction. It does not formally approve a private
revision, amend accepted specifications, stabilize bytes or establish compiler
or runtime support. No build, test or native qualification accompanies this
proposal.

## Decision, scope and owners

Recommend **PF111**, exact case-sensitive agreement
**`terrum/private-semantic-111-foreign-native-interfaces-1`**, with private
little-endian **i32 revision 111**. Compose the complete selected
[106/-2 B+C+R+U+A baseline](private-raw-union-return-coverage.md#exact-selection-and-framing)
with only the foreign directory/member, parameter-surface, explicit nominal
Copy and typed-call deltas below. Keep unrelated baseline records and behavior.

The foreign role, NativeOutput protocol and aggregate values are already
specified. The missing decision is their **current exact private composition**,
not another source syntax, output predicate, ownership system or dispatch ABI.
The older
[96 identity carrier](native-output-foreign-interface-private-persistence.md)
and [97 convention amendment](native-calling-convention-private-persistence.md#foreign-interface-methods)
do not select a usable nonempty directory in the implemented bounded 106 route.
PF111 supersedes their private FNI framing/header recommendations **for this
exchange**, including the old u32-only member discriminator, receiver-included
argument vector and foreign NativeParameter vector. Their logical identity and
convention rules remain prerequisites, not alternate decoders.

| Owner | Preserved contract or dependency |
|---|---|
| [Persistence](../specs/representation/persistence.md), [versioning](../specs/representation/format-versioning.md), [binding](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md) | Private versus Standard distinction, exact schema, original expectations, truthful producers and mandatory safe structural/binding checks. No normative text is replaced. |
| [Foreign interface identity](native-output-foreign-interface-method-identity.md) | Nominal flat bodyless owner/member, opaque receiver, complete slot list, target, caller-unsafety, dispatch and semantic versus executing provider distinction. |
| [NativeOutput cells](aggregate-native-outputs.md), [private output carrier](aggregate-native-output-private-persistence.md) | Exact cell type, selector partition, dependencies, reservation, cancellation and publication. Reuse the carrier already composed into 106, not standalone revision 90. |
| [Convention normalization](native-calling-convention-source-schema.md), [private convention](native-calling-convention-private-persistence.md) | One normalized effective member convention, distinct identities and exact mnemonic token. |
| [PS1 parameter surface](trrm-foreign-native-interface-parameter-surface-schema-selection.md) | Complete explicit-formal names and checked frozen scalar defaults, including uncalled and original expected declarations. Private bytes below are not section `0066`. |
| [AC1 aggregate consumption](trrm-foreign-native-interface-aggregate-output-consumption.md), [positive Copy evidence](standard-trrm-rootless-copy-type-evidence.md) | Positive nominal Copy, complete rootless storage/layout closure and ordinary aggregate reads/returns. Private evidence below is not TypeDef flag `0x20`; AC1 is a semantic prerequisite, not PF111's decoder. |
| [Windows x64 layout](../specs/targets/windows-x64-data-layout.md), [calls](../specs/targets/windows-x64-call-interface.md) | Exact physical representation, access, alignment, argument positions, independent by-value copies and result storage. |

The new foreign boundary admits the already selected closed scalar/raw-pointer/
function-pointer signature subset and positive-extent fixed-rootless
`Blittable Copy` nominal aggregate cells. Ordinary caller aggregates may be
copied and returned under their existing ABI. PF111 adds no interface value,
`ptr<I>` conversion, witness, inheritance/receiver adjustment, marshalling,
resource acquisition/release, native retention annotation, general foreign
aggregate-by-value ABI or exception transport. Arrays, raw-Union output coverage,
optional/managed/owner/reference carriers and open generic foreign signatures
need their independent selected boundary contracts; unrelated ordinary 106
array/reference/Union operations retain their existing admission.
Standard F+I/PS1/AC1 schemas, Core-manifest configuration and `.trrm` conversion
are not selected or changed here.

### Inspected evidence

These are source observations, not validation results:

| Path | Current private boundary |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader` | Revision 111 now dispatches the exact PF111 candidate, composes the checked-reference baseline, and carries foreign owner/member directory rows, member surfaces and Copy evidence. Revisions 104/106 retain their existing framing. |
| [`ModuleCompiler.CheckedReferences.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.CheckedReferences.cs), function metadata | PF111 now persists bodyless owned/expected member declarations, receiver/signature/default facts, NativeOutput, and receiver-relative dependencies. |
| `ModuleCompiler.cs`, `PutExpression`, `GetExpression` | PF111 tag 5 now carries an exact zero-based ModuleFunction target, explicit-formal mapping and a separate receiver before source-ordered arguments. Source-free resolution restores the receiver-aware bound call; tag 41 retains output reservations. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`ForeignNativeInterfaceContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/ForeignNativeInterfaceContract.cs) | Checked interfaces/members, separate receiver, convention and output facts exist in memory. `BoundStruct.DeclaredCopyEvidence` is nullable; private struct records do not explicitly persist it. |
| [`PrivateModuleIdentityInputs.cs`](../../sources/Terrum.CodeAnalysis/Modules/PrivateModuleIdentityInputs.cs), [`private origins`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.PrivateNominalOrigins.cs) | Snapshot-bound FNI owner/member assignments, provider resolution, nominal-origin closure, positive Copy checks and type/layout replacement checks are implemented alongside nominal identity inputs. |
| [`Foreign emitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.ForeignNativeInterface.cs), [module inventory](../implementation/modules.md) | Source dispatch has a receiver-aware lowering route. That does not restore erased private declarations or establish private source-free execution. |
| [`Program.NativeModules.cs`](../../sources/terrumc/Program.NativeModules.cs), [`Program.Modules.cs`](../../sources/terrumc/Program.Modules.cs), [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs) | Both private CLI routes now carry exact PF111 selection; identity manifest v2 and the existing NativeCore/NativeOutput readers configure source binding without changing the outer native package revision. |

These are implementation observations, not qualification results. Owner/member
and typed-call persistence, nominal closure/provider checks, v2 identity
transport, and private NativeCore/NativeOutput manifest routing are being
implemented under this provisional direction. The active validation hold
prevents builds, tests and runtime qualification; the proposal remains
unapproved.

## Exact selection and backward behavior

Write `TRRM` followed by **`6F 00 00 00`** at offset 4, not Standard's u16
major/minor pair. PF111 means the complete agreed 106/-2 snapshot plus this
delta, not future moving contents at its links or numerical inheritance.
The separate [G108S](private-span-type-term-persistence.md),
[CH109](private-class-handle-ownership-persistence.md) and
[CC110](private-class-cleanup-persistence.md) candidates are **not composed**.
111 is recommended, not approved or assigned by the current compiler.

Amending unpublished 106 in place would be permissible with a distinct whole
agreement such as a separately specified 106/-3. Prefer 111: nonempty owned
bodyless declarations, new signature extents and the changed tag-5 prefix then
have an explicit decoder boundary. This does not impose draft stability.
A universal private TypeDef/MethodDef/TypeSpec graph would migrate unrelated
families; the focused existing-directory extension is sufficient here.

Reuse the candidate exact-selection API/CLI shape from
[private schema selection](private-class-cleanup-persistence.md#host-schema-selection):
`CreateProducerWithSchema`, `CreateConsumerWithSchema` and corresponding
reference/set/graph/native creation routes carry `schemaIdentity`;
**both `compile-module producer|consumer` and `compile-native producer|consumer`**
use `--private-schema <exact-identity>`. This reuse selects
the interface shape, **not CC110's schema or class changes**. Existing overloads
and default commands retain their current behavior and never auto-select PF111
from an attribute, output, nominal input or profile label.

Select every supplied provider and destination explicitly. For this bounded
exchange, all participating inner Contracts select PF111; no automatic
104/106-to-111 conversion is defined. Artifact-only readers can dispatch the
known i32 111 to the one agreed PF111 schema; an explicitly expected identity
must match. Carry selection through readers/writers, binding, reconstruction,
snapshots, caches and lowering. Reject missing/unknown/wrong-case selections,
wrong headers and unsupported composites without retry or partial output.
Existing exact 104/106 routes remain independent; they acquire no nonempty FNI
support, new Copy byte or alternate tag-5 prefix. Regenerate from checked inputs.
Header patching, empty counts, checksums and native-profile strings are not
conversion or proof of provenance.

### CLI option boundary

Select the same paired option **`--private-schema <exact-schema-identity>`** on
those four source-to-artifact routes. For PF111 the value is exactly
`terrum/private-semantic-111-foreign-native-interfaces-1`, case-sensitive;
`PF111`, numeric `111`, aliases and case variants are not accepted spellings.
It may occur once among the ordinary options after the positional arguments,
before a final `--json` when used. Missing/empty values, duplicates even with
equal values, unknown selections and use on an unselected command are input
errors, not ignored options.

| Route | Required PF111 selection |
|---|---|
| `compile-module producer|consumer` | Existing ModuleCompiler exact-selection path; the selected producer and destination must agree. |
| `compile-native producer` | Call `NativeModuleCompiler.CreateProducerWithSchema` with the same immutable selection for the inner semantic Contract and its native creation context. Output remains outer-v3 `.trmn`. |
| `compile-native consumer` | Check the positional producer `.trmn` through `NativeModuleCompiler.CreateReferenceWithSchema` before exposing declarations to source binding; then call `CreateConsumerWithSchema` with that same selection for the expected provider and destination Contract. |

An explicit selection must not disappear between option parsing, reference
creation and serialization or fall through to the default API. Reject a wrong
inner producer revision/schema or unsupported required content before affected
binding/emission; write no partial artifact or replacement output on failure.
The native command still selects its runtime policy and real image/export
requirements independently. The private-schema option itself supplies no
nominal keys, Core registrations, executing receiver provider or new ABI. The
separate, unapproved
[foreign-interface identity-input proposal](private-foreign-native-interface-identity-inputs.md)
recommends complete owner/member assignments in identity-manifest v2 and
explicitly gates that transport on PF111; it does not change this schema's
exact selector or grant Core-registration authority. Owned annotated source
also needs the separate, unapproved
[private compilation Core-registration route](private-compilation-native-core-registration.md);
neither identity nor schema selection supplies that authority.

**Absent option:** preserve the existing legacy creation/default-selection
behavior on each route, including supported 104/106 inputs; do not select PF111
from source facts or an outer header. Source compilation of a consumer against
a PF111 producer requires the explicit option, rather than upgrading its
destination implicitly. Explicit PF111 ordinary-only production still writes
111 and all mandatory fields.

This amendment adds no `--private-schema` option to artifact-only
`link-module`, `run-module`, `validate-native` or `run-native`. Those routes
retain supported exact inner-revision dispatch and existing outer/runtime
checks; they do not rewrite or select a new output schema. It adds no flag to
Standard TRRM or unrelated commands. Native outer version 3 continues to select
container framing only, so it cannot justify omitting selection from
`compile-native`'s inner Contract creation.

## Exact private records

All multi-byte integers are little-endian. `str` is the inherited bounded i32
UTF-8 byte length plus strict UTF-8 bytes; Boolean is exactly u8 `0/1`.
Private type fields are i32 identities in the complete PF111 type universe and
their using context, not Standard TypeSpecRefs/DefIds. Preserve the 4 MiB artifact,
1,024 total ModuleFunctions, 256 function slots, 64 supplied call arguments,
100,000-node and depth-128 limits. New records share those budgets.

Reuse the existing nominal Binding fields:

```text
Binding:
    u8 originKind                 // 1 owned definition, 2 original expectation
    Boolean isExported
    u16 reserved = 0
    str usingModuleIdentity
    str localDeclarationKey
    if originKind == 2:
        str selectedModuleIdentity
        str selectedExportKey
```

Module identities retain the private 1–64-character ASCII grammar: initial
letter, then letters/digits/underscore. Keys are nonempty, at most 256 UTF-8
bytes; all identities/keys compare ordinally without normalization. Definition
and exported keys are unique in their respective using-Module namespaces.
An expectation is not exported and retains its own local key plus exact
selected provider/export. Equal names, geometry or slot numbers supply no key.
An owned exported Binding publishes its local declaration key as its one
canonical export key. This bounded carrier does not encode multiple export
aliases; required additional aliases cannot be silently discarded or treated
as distinct nominal declarations.

Extend the snapshot-bound private identity inputs with complete foreign
assignments: checked interface ordinal, local owner key, definition/export or
selected owner Module/key, and an ordered member map containing every local
member key and, for imports, every selected method export key. Ordinals select
the checked input snapshot only; Bindings are the persisted identities.
Validate exact snapshot, kind, uniqueness, owner and complete correspondence.
Source-free input reuses retained Bindings; it never invents keys from names.

### Directories and positive Copy

The top-level order is the complete 106 order:

```text
111 header/profile/kind/runtime/grant/invocation buffers/entry
Structures / Classes / ImportedStructures
complete NativeFunctionPointerSignatures / NativePointerTypes / NativeFixedArrayTypes
completeReferenceTypes / nominalTypeOrigins / EnumDefinitions
foreignInterfaces
ModuleFunctions
```

All existing mandatory counts remain. In **every Structure**, insert one
mandatory Boolean **`hasDeclaredCopy` immediately after `IsUnique`, before
`IsTransparent`**. Other fields remain unchanged: inline size/offset/field size
are u64; packing, declared/completed alignment, Blittable target, complete
ordered fields and class i32 geometry retain 106 meanings.

`1` publishes a positive nominal Copy guarantee; `0` supplies none, not a
negative uniqueness or initialization guarantee. Reconstruct explicit evidence,
not unknown `null` followed by `!IsUnique`. A source producer establishes Copy
from the checked complete declaration; an importer/rewriter retains published
evidence. Layout, absent Cleanup and unknown imported nonuniqueness cannot
manufacture it. Copy-positive plus unique/Cleanup is incompatible.
Require positive evidence on every nominal inline child used by the new
aggregate output/read/return route, including private fields. Independently
check rootlessness, exact layout, closed storage and finite acyclic inline
closure. Pointer cells retain identity/dependencies but do not recursively
classify pointee storage as inline payload. An output additionally requires
positive extent. Preserve existing target capacity and packed-access gates;
neither one logical value nor a Copy byte imposes a machine-word/16-byte limit.

Keep nominal origin rows for the entire foreign signature/output type closure
and all retained aggregate storage/layout obligations. Their original using
and provider contexts survive projection. Required private field facts grant
no source visibility or invented public type export.

### Foreign owner directory

Replace the forced-zero directory with:

```text
i32 foreignInterfaceCount
repeat foreignInterfaceCount:
    Binding owner
    str lookupName
    u8 visibility                  // 0 public, 1 internal
    u8 flags = 0
    u16 reserved = 0
    u32 targetProfileId = 1
    u32 targetProfileRuleRevision = 1
    u32 vtableByteOffset
    i32 methodCount
    repeat methodCount:
        Binding member
        u32 moduleFunctionIndex    // zero-based; zero is usable
```

The interface's fixed fields after `lookupName` occupy **20 bytes**, including
`methodCount`; each member adds its Binding and four-byte function index.
Require `1 <= methodCount`, distinct complete members and
`foreignInterfaceCount <= totalForeignMethods <= 1024`. All directory counts,
string extents, references and summed rows preflight remaining bytes/budgets.

The owner is one closed nongeneric nonpartial flat bodyless interface, not a
private value type. `lookupName` is nonempty source lookup text, never identity.
Every member is public synchronous instance callable, without bases, generics,
associated members, helpers, accessors or authored bodies. Definitions may be
public/internal; exported owners are public and export every member.
Imported owners are public; their member Bindings select methods in the same
provider Module and require their actual owner to equal the selected interface.
Definition members share the owner's using Module/origin/export status.

Entry n is the exact declared member n and derives flat slot n. Its function
index has a checked reverse reference; each foreign ModuleFunction belongs
exactly once to one owner. Physical row/function reordering is allowed with
remapping, but declared member reordering changes the contract.
Target `(1,1)` is exactly `windows-x64-native/1`. O is an unsigned multiple of
eight; check `O+8` and `8*methodCount` in wide arithmetic. O locates the vptr,
not an adjusted receiver or an object size. Store no duplicate slot/offset.
Canonical writers order interface rows ordinally by
`(usingModuleIdentity, localDeclarationKey)`, retain ordinary ModuleFunctions'
relative order, then append foreign functions in interface/declaration order.
Remap indexes/entry accordingly. Ordinary readers accept consistent physical
permutations; they never sort a declared slot list.

### Declaration-owned foreign member and surface

Preserve the actual 106 ModuleFunction order:

```text
Name / Contract / Import / hasFunction / HasOwnershipTransfer
NativeOutput role and collection
general Dependency count/rows
checked selector count/rows / checked output-state count/rows
raw-Union return-coverage count/bytes
CopyOrigin kind/payload
foreign-member discriminator/variant
expected-direct-import Boolean/descriptor
expected-vtable Boolean/descriptor
conditional stored Function
```

Replace the mandatory u8 zero at the foreign-member position with:

```text
u8 foreignMemberKind               // 0 ordinary, 1 foreign member; others invalid
if foreignMemberKind == 1:
    u32 foreignInterfaceRef        // one-based, nonzero directory row
    u32 declaredMethodOrdinal
    i32 resultType
    i32 receiverType
    Boolean declaredCallerUnsafe = 1
    Boolean doesNotReturn
    u8 reserved = 0
    i32 explicitFormalCount
    repeat explicitFormalCount:
        str name
        i32 formalType
        u8 mode = 0                // ordinary value formal, including write ptr<T>
        u8 flags = 0
        u16 reserved = 0
        u8 defaultKind             // 0 absent, 1 checked frozen scalar
        if defaultKind == 1:
            ScalarBytes(formalType)
```

Absent variant extent is **one byte**, not the old candidate's four.
Present fixed extent is **24 bytes**, including kind/reference/count;
each formal adds `str extent + 9 + scalar payload extent`.
`ScalarBytes(T)` reuses the exact type-derived private 106 scalar payload,
width, validity and bit-preservation rules, without a tag/type/span envelope.
Only independently admitted fixed-scalar defaults qualify; unit, when admitted,
has a present default with zero payload bytes. Pointer/native-width/nominal/
aggregate/backing/evaluation defaults have no codec here and cannot become
absent or runtime helper calls. Names are complete, nonempty, valid source
identifiers and unique within each view. Output formals cannot default.

For kind 1, `hasFunction=false` for owned **and** expected declarations;
`Import` agrees with Binding origin. `HasOwnershipTransfer=false`, expected
direct/vtable presence is false, checked-selector/state counts and raw-Union
coverage count are zero, and CopyOrigin kind is zero. General dependencies and
NativeOutput's distinct phase-1 rows remain on this same declaration.
Delay role-dependent body/Contract/output checks until the directory and
variant are available; the current early bodyless-owned rejection cannot apply.

`Contract` is exactly **`|NativeCallable:K`**, with K exactly `Unmanaged`,
`Cdecl` or `Stdcall`. This is the sole convention authority. Vectorcall and
other spellings reject for this role; no convention byte, inherited sentinel,
NoThrow promise or direct-import fingerprint is added.
Declared caller-unsafety is explicit and true; the foreign role independently
requires an unsafe caller. `doesNotReturn` preserves its ordinary effect and
does not promise no failure. The exact readonly opaque receiver is private
type 31 (`ptr<void>`); a mutable receiver is the validated writable void-pointer
type. Recover receiver access from that type, never from pointer width.
Require `explicitFormalCount+1 <= 256`. No foreign NativeParameter vector is
selected: PS1/FNI currently selects no such member applicability.

Foreign `Name` is lookup text; owners/overloads may share it. Exclude these
records from ordinary name-selected function dictionaries and code exports.
For kind 0 all ordinary 106 declaration meanings remain; only the new Copy
byte and common call prefix are unconditional schema deltas.

### Typed calls and reservation correspondence

Every expression tag 5 in PF111 has this prefix:

```text
ExpressionEnvelope                // unchanged kind/type/source-span envelope
u8 targetKind                     // 0 ordinary, 1 foreign; others invalid
if targetKind == 0:
    str symbolicTarget
if targetKind == 1:
    u32 foreignModuleFunctionIndex // zero-based exact definition/expectation
i32 argumentCount
Boolean hasParameterMapping
if hasParameterMapping:
    i32 explicitFormalOrdinal[argumentCount]
if targetKind == 1:
    Expression receiver
Expression sourceOrderedArguments[argumentCount]
```

Kind 0 retains ordinary target/mapping semantics and cannot find a foreign
member by name. Kind 1 selects its exact directory/member/signature; its count
and map exclude the receiver and cover every explicit formal exactly once.
An absent map means positional correspondence. Calls have no omission sentinel:
written arguments retain source order, then omitted checked constants appear in
formal order. The separate receiver evaluates once **before** those arguments.
The total 64-argument limit includes the receiver, so a foreign invocation has
at most 63 explicit arguments.

Reconstruct `BoundCall.ForeignInterfaceFunction` plus the exact
`ForeignInterfaceCall`, separate receiver, explicit argument map and member
output facts. Do not reconstruct an ordinary stub, numeric vtable intrinsic,
captured function pointer or generated semantic body.
Logical output/source ordinal n remains explicit n; a receiver source has
ordinal zero in its separate namespace. Native receiver-first position is 0,
explicit n maps to n+1 for these scalar-status output calls. Thus output
formal zero follows the receiver, not RCX. Preserve independent floating/stack
positions and any separately admitted hidden-result classification.

Reuse **tag 41's exact 21-byte** void envelope/local/pointee reservation.
It occurs only at the mapped annotated explicit argument of its own call
occurrence, under actual persisted unsafe scopes. Require the exact whole
initialization local, type, stable extent/alignment and distinct destination
for every output; it is no ordinary pointer value or receiver.
Neither call nor reservation gains a serialized authorization bit, call ID,
runtime address or generation. Each dynamic invocation reserves the current
vacancy generation; cloned occurrences rebuild their own associations.

### NativeOutput facts and publication

Keep the existing declaration collection byte shape: i32 count; for nonzero
count, u8 role/u8 zero/u16 zero; each **20-byte** output prefix plus its
**12-byte** dependency rows. Nonempty foreign collections require role 2 only
after the complete owner/member relation validates; direct imports keep role 1.
Empty collections omit role. Prototype numeric vtables remain output-ineligible.

Require exact ordinary `write ptr<T>`, formation/publication `1/1`, state pair
live/vacant `1/0`, and selector pair **3/4** (nonzero/zero, int32 or uint32) or
**5/6** (nonnegative/negative, int32). These are private projections, not Core
enum values or Standard PredicateGraph bytes.
Output/dependency ordinals select explicit formals, never ABI positions or
directory rows. Retain the complete source set: one Empty row for no sources,
otherwise distinct ordered Formal/Receiver sources with exact Borrow/Forward/
Union relationships. Zero dependency count and mixed Empty/nonempty rows are
invalid. Every row targets its containing output formal, destination kind 2,
normal-publication phase 1; receiver/Empty source ordinal is zero.
Keep these rows out of the separate general Dependency collection.

Reserve at argument evaluation before later argument effects. Cancel only
abandoned **unentered** reservations when staging prevents invocation.
Enter Invoke before the first foreign vptr/entry read. Any admitted failure
after that point leaves the output indeterminate, not complementary vacancy.
On normal completion, capture one exact result and apply every output's
selector at the existing Publish boundary after required checks/phase cleanup,
even if source ignores the result. Success publishes complete T and its
declared dependencies; complement publishes vacancy irrespective of bytes.
No constructor, zero fill, readback validation, resource cleanup or rollback
is synthesized. No-return calls publish no default result/output.
Preserve saved-result/local-generation correlation through copies, joins,
loops and invalidation; an old result cannot revive a later cell generation.
Pointer-cell publication says nothing about pointee validity or ownership.

## Reconstruction, binding and lowering

Decode bounded framing first, resolve the complete type/directory universe,
then perform contextual role/signature/output checks before affected exposure
or lowering. Reconstruct interfaces/members in the shared bound model and
retain original expected views separately from actual provider declarations.
An admitted normalized private foreign origin establishes the persisted role;
consuming it requires no source Core attribute lookup. Preserve owned versus
expected origins rather than fabricating a Core registration or marking an
owned interface imported to bypass the current source-registration gate.
Use existing traversal/rewriting and verification paths with the full facts:
receiver children, defaults, member identities, inner reservation T, explicit
Copy, general/output dependencies and declaration-only closure must all survive.
An empty verifier-view body is not an authored implementation or code export.

Resolve exact owner and every method/type export; require complete ordered
member correspondence, target/offset/slot, receiver access, signature, effective
convention and retained effects. Compare NativeOutput's complete normalized
role/type/formation/selectors/states/source set exactly; use existing directed
general-dependency satisfaction separately. Positive Copy expectations require
actual positive provider evidence and complete nested layout/type obligations.
Expected evidence is not proof about a replacement.
Keep original names/defaults: surface changes alone do not invalidate already
completed binary calls, but cannot overwrite their selected defaults or proxies.
New source observes its selected declaration view.

Preserve multiple original using views selecting the same actual declaration;
do not collapse them through equal providers, names or layout. Nominal rows
with equal lookup names require their disambiguating complete origin contexts,
not name-based merging. Clone/remap context-dependent structural rows as needed.
Additional provider facts relied on become retained obligations rather than
silently strengthening the original expectation. Re-encoding rebuilds directory/
function indexes and reverse links, entry index, complete type/origin maps,
ordinary versus foreign targets, formal maps, dependencies, reservation locals
and pointees, layouts, defaults and source spans. Declared member order, source
argument order and distinct local generations are not canonicalization choices.
Invalidate cached checks on changed schema/provider/type/target/rule context.

Consumers owe safe decoding, supported type/layout/role/effect consumption and
complete binding checks, including uncalled retained declarations. Producers
establish truthful Copy/flow/output facts and authored unsafe assertions.
Existing reconstruction verification may check its supported bound invariants;
this proposal does **not** require consumers to semantically re-prove arbitrary
Body dominance/correlation or foreign receiver behavior. Source checking and
optional deeper audits remain distinct.

Lower through the existing receiver-aware foreign emitter: stage receiver and
explicit arguments once, project each reserved local address at its exact
formal, load the vptr at `R+O`, select `slot*8`, and invoke with **R unchanged**.
Keep actual receiver/backing/code-provider obligations until their endpoints;
the semantic declaration provider need not own the code reached by R.
After publication, ordinary aggregate copies/returns use exact-extent storage
and the selected ABI, including alignment, padding, spills and hidden result
support. A hidden aggregate result buffer is not the NativeOutput cell.
Unsupported independent mappings/capacity reject rather than truncate or
fabricate a successful value; the in-scope PF111 route requires implementation,
not replacement by a generic unsupported diagnostic.

### Native embeddings and standalone execution

Keep **TNPK/TNSP/TNGP outer version 3 and byte layouts**, selecting the joint
**outer-3/PF111 agreement** for their independently decoded inner Contracts.
Retain existing direct-import expectations/thunks and image checks unchanged.
Foreign owner/member exports are semantic-only; never create PE exports,
IAT imports, direct-expectation rows or dummy abstract-member implementations.

Dependency closure includes foreign/type Bindings even when the existing
code-name/machine-import lists are empty. Under PF111, permit a zero code-list
count only when the inner Contract proves that exact retained semantic edge;
an unjustified empty edge is invalid. Keep those edges in sets/graphs and
contract snapshots instead of dropping uncalled declaration providers.
A foreign-only semantic `.trm` needs no code export. Native providers retain
their independent real image/export admission; no fictitious foreign thunk
satisfies it. Standalone consumer compilation can lower dispatch locally from
the selected semantic reference.

AOT validates semantic edges and images, then executes stored images. Root JIT
restores/re-lowers the root's typed foreign calls; provider images remain
precompiled. Neither route infers an executing receiver provider from an outer
semantic dependency. Rebuild Contract extents, container offsets and SHA-256;
regenerate affected native images from final checked bindings, not header patches.
Profile/runtime policy, supervision and existing headless opt-ins remain intact.

## Focused future acceptance and remaining decision

These are required cases, **not checks run**. Use public compiler APIs, existing
[module/output tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs),
[source FNI tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs),
set/graph suites and supervised CLI/native patterns. No friend assembly or
API/sample-specific compiler recognizer is needed.

| Case | Required evidence |
|---|---|
| Independent exact bytes | Own/expected/uncalled interfaces, ordinary rows and zero directories; verify one-byte absent and 24-byte present variants, defaults, Copy byte, tag-5 prefix and adjacent field boundaries without deriving all vectors from the writer. Reject bad counts/UTF-8/reserved bits/kinds/references, incomplete/repeated members, wrong reverse owner and trailing bytes. |
| Source-free closure | Producer reference, unseen source caller, consumer persist/reload/re-emit and standalone JIT/PE; preserve every member, labels/default bits, imports and outputs without source/Core-attribute reconstruction. Same member names under distinct owners and overloads must select exact identities. |
| Invocation and ABI | Zero/nonzero vptr offsets, nonzero slots, readonly/mutable receivers, labels/defaults, floating inputs and arguments beyond register positions; receiver-first mapping, once-only source staging and formal-zero output address. Exercise scalar/pointer and multiple output cells with one status. |
| Output flow | Negative NonZero success, zero/nonzero endpoints and signed NonNegative partition; normal complement despite partial bytes, ignored/stale results, nested calls and loop generations. Source/audit negatives cover unsafe omission, omitted/reused/overlapping cells, false guards and cancellation; admitted exceptional routes preserve indeterminate state without selecting new transport. |
| Aggregate consumption | Nested nominal Copy-positive cells, small and larger-than-16-byte values, packed/over-aligned layout, complete reads/copies and non-inlined helper returns. Reject missing positive/nested Copy, mismatched T/layout, unique/Cleanup/rootful/unsupported inline closure; copying a pointer cell never classifies its pointee as initialized. |
| Servicing/remapping | Compatible physical interface/function/type/pointer-table relocation, set/diamond graph and source-free re-emission; preserve all original expectation views. Reject removed/reordered/split member identities, another owner with equal slots/signature, convention/access/offset/selector/dependency changes, and a provider lacking actual Copy evidence. Changed labels/defaults preserve completed old calls and original surface. |
| Native composition | Mixed providers with real unrelated code exports plus semantic-only FNI edges; TNPK/TNSP/TNGP AOT and graph root-only JIT retain metadata without abstract PE/IAT thunks. Reject unjustified empty edges and incompatible inner contracts before affected image use. Capture actual supervised process results, not just emission. |
| CLI selection | Exercise producer and consumer modes of both `compile-module` and `compile-native`, including ordinary-only PF111 artifacts. Confirm explicit input/destination selection reaches the reference and creation APIs; reject missing values, duplicate/wrong-case/unknown selections, wrong producer inner schema and unsupported option scopes without output. Preserve flag-absent legacy behavior; reject flag-absent compilation against PF111 rather than infer it. |
| Isolation and limits | Unchanged exact 104/106 ordinary routes; explicit PF111 ordinary-only writes still use 111/new mandatory fields. Reject old FNI framing, wrong schema/header and unselected G108S/CH109/CC110/Standard composites without fallback. Check formal/argument/table limits and wide offset arithmetic independently of runtime allocation. |

**Remaining decision:** formal adoption of the single PF111 identity/revision
and the complete delta above, including its outer-3 semantic-edge admission.
The user's workflow makes this recommendation provisionally actionable; there
is no further in-scope source, output, receiver, Copy or codec choice delegated
to implementation. Compiler persistence/projection/remapping, standalone and
native execution qualification remain open work, not documentation completion.
