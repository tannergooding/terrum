# Private persistence of foreign-interface method identity

**Status: unapproved; provisionally actionable under the user's authorized
missing-contract workflow.** The applicable authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

This document selects a complete private candidate, not formal approval,
implementation support, a standard allocation or a stable format. It changes
no normative owner or existing proposal.

> The candidate-96 foreign identity directory and typed-call design below
> remain part of the current exact composition. Its one-byte foreign
> `nativeConvention` placement is superseded by candidate 97 in
> [Native calling-convention private persistence](native-calling-convention-private-persistence.md):
> revision 97 uses `|NativeCallable:K` in the declaration Contract instead.
> This file is retained as the identity/target semantic owner, not an
> alternative revision-96 decoder.

## Decision and ownership

Select **candidate private structured-module revision 96**: the **complete
revision-95 candidate**, followed by the exact delta below. Add a bounded
nominal declaration directory, explicit foreign-member signatures on
`ModuleFunction`, and a typed foreign target in the existing call expression.
Retain the **outer version-3** TNPK/TNSP/TNGP field layout, with the exact
foreign-aware admission rules below. There is no revision-92-plus-identity
variant, reduced 96, optional extension trailer or compatibility reader.

The [foreign-interface identity candidate](native-output-foreign-interface-method-identity.md),
committed at `180c1ead`, remains the owner of the provisional nominal flat
interface, source registration, exact declaring interface/member selection,
receiver projection and target dispatch. This proposal supplies its missing
**private interface/type/member identity and revision composition** only.
Its standard `ForeignNativeInterface` section candidate and Feature 5
integration remain in that separate proposal; none of those standard rows
is encoded here.

[Scalar and pointer native-output cells](native-pointer-output-ownership.md)
remains the semantic owner of eligible T, result conditions, cell
vacancy/reservation/publication, source dependencies, failures, resource
adoption and ABI restrictions. Reuse its declaration-owned output collection
and expression tag 41 unchanged. No second output vector, resource flag,
interface-specific selector or provider thunk is selected.

The accepted [persistence taxonomy](../specs/representation/persistence.md),
[exact-schema policy](../specs/representation/format-versioning.md),
[binding relation](../specs/representation/binding-and-replacement.md),
[consumer obligations](../specs/representation/reader-trust.md) and
[canonicality distinction](../specs/representation/encoding-canonicality.md)
remain unchanged. A private `.trm`, embedded Contract, `.trmn`, `.tnset` or
`.trng` does not become standard `.trrm` interchange.

## Inspected boundary

These are direct source/document observations, not build, test, ABI or
execution results. Older proposals' baseline paragraphs describe earlier
worktree states; they do not override the inspected codecs.

| Owner | Current fact and consequence |
|---|---|
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader` | The writer's final selection and reader's sole admitted private revision are **92**. The header is `TRRM` plus little-endian **i32**, not standard u16 major/minor. Revision 92 output persistence is in progress; these constants do not establish its qualification. |
| `Module`, `ModuleFunction`, `PutFunction`, `ReadFunctions`, `Contract` | Structures/classes, function-pointer signatures and pointer types have private carriers. There is no foreign InterfaceDef/type/member identity directory. Imported functions can lose their `Function` payload; textual contracts and numeric expected-vtable records cannot reconstruct that identity. |
| `PutNativeOutputContracts`, `ValidateNativeOutputContracts`; [`NativeOutputContractValidator.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeOutputContractValidator.cs) | One normalized collection is already present. Both admission paths still require DirectImport. The current cell-type check includes scalar/pointer eligibility; accepting role byte 2 alone would not add interface identity. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs) | In-memory dependency/copy-origin facts and `ForeignInterfaceMethod = 2` exist. `BoundProgram` has no nominal foreign-interface collection, and `BoundFunction` has no exact foreign owner/member reference. These facts are not a private codec. |
| [`NativeVTableCallContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeVTableCallContract.cs) | The prototype carries slot, offset and parameter facts, requires its old output vector to be empty, and takes receiver position zero. It has no declaring-interface/member identity. It remains a different, output-ineligible role. |
| [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs), [`Graph`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs) | Reference merging/restoration and graph-library reads have distinct paths. They currently use function/type names, private type maps and retained expectations. `RecordedImportPromises` alone cannot preserve a new identity graph or its original collections. |
| [`FixedLayout`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.FixedLayout.cs), [`TokenSpans`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.TokenSpans.cs) | Revision predicates coexist with explicit allowlists and profile checks. Higher numbers do not complete those gates; compilation-local span persistence still rejects. |
| [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`ModuleSets`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.ModuleSets.cs), [`GraphPackages`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphPackages.cs) | All three outer formats select **3**. They embed independently read Contracts, images and checksums, and retain the direct-import expectation table. Current call-import/export enumeration assumes code symbols; that assumption is wrong for a bodyless foreign member. |
| [`GraphJit`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.GraphJit.cs), contract snapshots and graph catalog | Root JIT restores/re-lowers only the root; providers remain precompiled. Cached parsed contracts and selected provider snapshots must retain the new semantic-only edges even when no machine import exists. |

### Alternatives

The smallest textual change would add an interface key to the numeric
expected-vtable record and allow output role 2. That is not a viable exact
delta: it lacks the complete owner/member set, definition/import binding,
effective convention, bodyless signature and declaration-only expectations.
Adding those facts while keeping name-selected stub signatures and duplicate
slot/output authorities would create two declarations to reconcile.

**Select the targeted nominal directory below.** It reuses existing value
type/layout tables and ModuleFunction output/dependency collections. A small
origin map is necessary for nominal value types appearing in those signatures:
equal struct names/layouts cannot establish the nominal T of an output.
The directory gives the interface a declaration identity without inventing
an interface value or consuming a `PrimitiveType` range.

A more structural alternative would replace all private types/functions with
general TypeDef/InterfaceDef/MethodDef, Import/Export, complete TypeSpec and
MethodSig tables, essentially adopting a complete nominal metadata graph.
That would address broader private name-based limitations and aliases, but
requires migrating every ordinary declaration, generic/type context and
operation. It is not necessary for this closed foreign role. This candidate
instead rejects unsupported alias/visibility/type contexts explicitly; it
does not claim to solve general private nominal interchange.

For calls, a new foreign-call expression tag could preserve ordinary tag 5's
old prefix, but would duplicate its argument/mapping codec and traversal
paths. The selected target discriminator keeps one call-staging representation;
the exact new schema changes every tag-5 prefix deliberately.

## Exact revision composition

The composition is by these exact deltas, **not numeric ordering**:

| Candidate | Complete meaning retained by 96 |
|---|---|
| 92 | [NoThrow retirement](no-throw-private-persistence.md) composed with the complete [output-persistence delta](aggregate-native-output-private-persistence.md) from 90, including old-vector/suffix removal and tag 41. Apply the [scalar/pointer cell eligibility amendment](native-pointer-output-ownership.md#exact-candidate-representation-delta). Retain revision 89's resolved arithmetic modes. |
| 93 | Exactly that amended 92 plus the mandatory [general declaration-dependency collection](reference-provenance-carriers.md#private-exchange-candidate-revision-93) on every definition/expectation. Output phase-1 rows remain exclusively in the output collection. |
| 94 | Exactly 93 plus [wide scalar private persistence](scalar-wide-value-carriers.md#exact-private-schema-and-composition): its complete catalog and type-derived tag-1 payloads, not an unconditional i64. |
| 95 | Exactly 94 plus the mandatory [copy-origin summary field](reference-field-value-provenance.md#necessary-private-transport-candidate-95). Its zero/unavailable form remains present on foreign methods; foreign declarations acquire no summary attachment. |
| **96** | Exactly that 95 plus the two directory counts/tables, foreign ModuleFunction variant and tag-5 target discriminator defined here. Other field positions, operation tags, arithmetic, scalar payloads, output/dependency and summary meanings are unchanged. |

The inspected private namespace/proposals assign no competing 96. This
number is a proposed private **i32 header value**, not a standard format
pair, Feature, target ID, expression tag or outer container version.

At the 96 integration boundary, **all** structured writes/reads select
exact 96, including artifacts with zero directory counts and no outputs.
Ordinary readers, graph-library readers, nested contracts, reference
projection, producer validation and snapshots must agree. Reject 92–95,
older and unknown revisions before record decoding; do not retain historical
readers, use `>=95` as a layout selector, retry decoding, promote a header
or infer a schema from an empty collection.

Equal “96” bytes still require agreement on this whole draft and its
prerequisites. Reject malformed or unsupported selected input, but do not
promise to identify the provenance of every old byte stream whose dishonestly
changed header happens to produce valid new records.

## Candidate binary directory

All assignments below are **unapproved private records**. Integers are
little-endian; Boolean is the existing strict single byte 0/1. `String` is
the existing **i32 byte length plus strict UTF-8**, not an outer u16 string,
standard String row or DefId. Retain the current 4 MiB structured-artifact,
1,024-function, 256-slot, 64-call-argument, 100,000-node and depth-128 bounds.
New declarations consume those budgets rather than opening a second budget.

### Binding: definition, export or expected import

Use the following common record for each new nominal declaration:

```text
Binding:
    u8 originKind                  // 1 owned definition, 2 imported expectation
    Boolean isExported
    u16 reserved = 0
    String usingModuleIdentity
    String localDeclarationKey
    if originKind == 2:
        String selectedModuleIdentity
        String selectedExportKey
```

Module identities use the current private 1–64-character ASCII grammar:
initial letter, then letters/digits/underscore. They are ordinal,
case-sensitive **logical semantic Module identities**, not filenames,
receiver providers or image hashes. Native path-collision checks remain
separate and case-insensitive where required by Windows.

Declaration/export keys are explicit producer-selected nonempty strings of
at most 256 UTF-8 bytes. Compare decoded text ordinally, without case folding
or Unicode normalization. Never derive them from a source name, slot,
signature hash or file path. A producer/reference construction operation must
receive the logical Module identity and key assignments before writing the
directory; existing APIs lacking those inputs cannot silently invent them.
The keys are not required to be valid PE symbols.

For an owned definition, `(usingModuleIdentity, localDeclarationKey)` names
one local declaration of the record's exact kind. `isExported=1` publishes
that same key in the Module's semantic export namespace. This bounded
private profile permits **one canonical export key per such definition**;
export aliases are unsupported, not new nominal declarations or inferred
equivalences. Internal/unexported definitions use `isExported=0`.

An expectation has `isExported=0`. Its local pair identifies the **using
context's proxy**, not a new nominal type/member. Resolve its selected
Module/key to one exported owned definition of the required kind before
comparison. Do not re-export it, search other providers on failure or copy
identity/expectations from a replacement to repair it.

Local declaration keys are unique across these declaration kinds in each
using Module; exported keys also cannot collide with that Module's existing
ordinary semantic exports. A duplicate definition key or imported occurrence
is invalid. Distinct using contexts may retain different expectations for
the same selected declaration; preserve/check all of them. A packed artifact
may retain several logical Modules, but packing does not rename their origins
to the physical pack owner or discharge their original expectations.

### Table placement and nominal value origins

Immediately after the complete existing `NativePointerTypes` table and before
`Module.Functions.Length`, insert **both** mandatory tables:

```text
i32 nominalTypeOriginCount
repeat nominalTypeOriginCount:
    i32 privateType
    Binding nominalType

i32 foreignInterfaceCount
repeat foreignInterfaceCount:
    Binding interface
    String lookupName
    u8 visibility                  // 0 public, 1 internal
    u8 flags = 0
    u16 reserved = 0
    u32 targetProfileId = 1
    u32 targetProfileRuleRevision = 1
    u32 vtableByteOffset
    i32 methodCount
    repeat methodCount:
        Binding method
        u32 moduleFunctionIndex
```

`nominalTypeOriginCount` may be zero. Each `privateType` selects an existing
complete nominal inline struct (`100 + structure index`), including an
already admitted Transparent wrapper; its Binding supplies the missing
nominal origin/export relation. It adds no second field/layout codec.
This initial directory does not encode enums, classes, generic instances
or an interface as a value type.

Retain an origin for **every nominal value type in each foreign method's
complete signature closure**: result, receiver, explicit formals, native input
annotations, output inner T, and recursively all required fields, pointer
pointees and function-pointer signatures. Atomic scalars and structural
pointer/signature rows are not nominal declarations. The closure must be
finite and supported under the existing table/cycle rules.

Origin lookup uses `(usingModuleIdentity, privateType)` in the foreign
declaration's context, never the number alone. An owned origin resolves to
that Module's local struct; an imported origin retains the complete expected
struct/layout and resolves its exact type export. Exported origins require
public types, with all fields/layout facts needed for consumption retained;
private field facts grant no source access. Validate complete nominal identity,
field correspondence and every retained target/layout commitment. Require
Copy/Blittable only where the actual value/cell/boundary use requires it:
transporting a raw pointer does not assert Copy eligibility, positive extent,
initialization, accessible storage or ownership of its pointee. No equality
follows merely from matching names or layout bytes.

An origin row may also publish an explicitly selected nominal type export
before a local foreign interface uses it. Its retained type/layout closure
is then an export root. A foreign signature referring to another Module's T
requires that provider's actual published type key; a reference facade cannot
invent the export from T's name. This permits an ordinary value-type provider
to supply T without declaring a foreign interface of its own.

The same physical type/signature row may serve several using contexts only
when its complete meaning agrees in all of them. Otherwise clone/remap the
struct/pointer/signature closure or reject the merge when existing private
name/representation limits prevent it. Do not collapse two independent nominal
origins to make a set or diamond merge succeed. These records are required
for this foreign signature closure, not a retroactive general identity codec
for unrelated private exports.

### Interface and declared-member invariants

A foreign interface row is the bounded private projection of one nominal
TypeDef **and** its one InterfaceDef. It has no instance payload, size,
storage-layout record or `PrimitiveType` code. `lookupName` preserves its
nonempty qualified source lookup name, not identity. Initially support
module-level public/internal, closed, nongeneric, nonpartial flat interfaces;
nested/private owner scopes remain unsupported by this directory.

An exported interface is public and publishes **all** its declared public
methods. An imported interface is public. Every method Binding has the same
origin kind and using Module as its interface. On definitions, its export
flag equals the owner's; on expectations, its selected Module equals the
owner's selected Module, with an independent nonempty selected MethodDef key.
Resolve that key and require the selected method's owner to be exactly the
selected interface, not merely an equal-signature declaration in that Module.
No member is inherited, opaque, reserved or a forwarding stub.

`methodCount` is positive and the ordered entries are the **complete directly
declared list**. Entry n has declaration ordinal n and consequently flat slot n.
Do not sort this list by names, keys or function-table positions. Each
`moduleFunctionIndex` is a zero-based index into this artifact's complete
ModuleFunction table; zero is usable, not absent. Every listed function is
distinct, belongs exclusively to this row, and supplies the checked reverse
owner/member reference below. Every foreign ModuleFunction occurs exactly
once in such a list, including methods without outputs and uncalled members.

The target pair is exactly `windows-x64-native/1`, `(1,1)`, from the
identity candidate. The one full-width u32 offset is a vptr byte offset,
not a signed displacement, receiver adjustment or object-layout claim.
Require a multiple of eight, check `offset + 8` and `8 * methodCount` in
checked wide arithmetic, and preserve the provider's corresponding backing
extent/alignment obligations. Unsupported backend displacement/address
formation rejects lowering rather than narrowing the stored offset.
No second slot/offset or effective-convention authority is stored.

## ModuleFunction foreign-member variant

On **every** ModuleFunction, after the complete revision-95 copy-origin summary
and before the existing expected-vtable presence Boolean, insert:

```text
u32 foreignInterfaceRef             // 0 ordinary; otherwise one-based directory row
if foreignInterfaceRef != 0:
    u32 declaredMethodOrdinal
    i32 resultType
    i32 receiverType
    u8 nativeConvention             // 0 Unmanaged, 1 Cdecl, 2 Stdcall
    Boolean callerUnsafe = 1
    Boolean doesNotReturn
    u8 reserved = 0
    i32 explicitFormalCount
    repeat explicitFormalCount:
        String label
        i32 formalType
        u8 formalMode = 0           // ordinary value formal
        u8 flags = 0
        u16 reserved = 0
    i32 nativeInputCount
    repeat nativeInputCount:
        u32 formalOrdinal
        i32 pointeeType
        u8 access                  // existing 0 Read, 1 Write, 2 ReadWrite
        u8 retention = 0            // existing NoEscape
        i32 extent
```

The absent variant is exactly four bytes. The present variant's fixed extent
is **28 bytes**, including the reference, both counts and zero native-input
rows; each formal adds `String extent + 8`, each native input **14** bytes.
No optional convention, implicit receiver default or body signature follows.
These convention bytes are a new field's explicit private projection of the
[proposed effective NativeCallable contract](native-calling-convention-source-schema.md),
**not a cast of** the current single-case `NativeCallingConvention.Native`
enum or a standard Contract payload. Preserve the three convention identities
even where Windows uses identical physical carriers. Vectorcall rejects.

`resultType`, `receiverType`, `formalType` and `pointeeType` use the exact
revision-95 private type universe and the containing interface's using
context. Preserve all access qualification, pointer nesting and nominal
origin/layout facts. The readonly opaque receiver is private
`NativeRawPointer` identity **31**, representing non-null `ptr<void>`; a `mut`
receiver uses a validated native-pointer table entry with pointee Void and
writable access. Equal addresses do not permit substituting those types.
This uses the existing opaque-void pointer representation, not a new
`ptr<I>`, checked reference or arbitrary first pointer formal.

The initial private variant admits ordinary explicit formals only, with
independently supported directly interoperable signatures. NativeOutput's
`write ptr<T>` is precisely such an ordinary formal; it is **not** changed
to a checked-init formal. Other modes, default arguments and richer retention
need their complete independent carriers and remain unsupported here.
Labels are nonempty, unique within the declaration and subject to existing
string limits. They support source-free binding; bound replacement maps
corresponding formals by ordinal, not label equality.

Native inputs are strictly ordered by logical explicit formal ordinal,
unique, in range, exact in type/access/extent, and disjoint from NativeOutput
formals. Require `0 <= nativeInputCount <= explicitFormalCount` before
allocation. Retain existing private input eligibility and NoEscape semantics;
negative extents and unsupported required MayEscape facts reject, never
normalize to zero rows. The list supplies no new source annotation.

For a foreign variant:

* `Name` is the method's nonempty source lookup name. Validate lookup names
  and labels under their existing source identifier/qualified-name rules.
  Its existing `Contract` string is **empty**; this structured signature and
  shared declaration collections are its sole contract authority. Foreign
  overloads/owners may share names. Exclude them from the ordinary global
  name dictionary; do not mangle a name and treat the result as identity.
* The existing `hasFunction` Boolean is **false** on owned and imported
  foreign declarations alike. There is no `PutFunction` payload, authored
  body, local-variable list or generated dispatch Body. The directory
  independently distinguishes a definition from an expectation.
* `ModuleFunction.Import` is true exactly for an imported foreign expectation,
  false for an owned foreign declaration. Its semantic export status comes
  from Binding, **not** the ordinary producer flag's code-export convention.
  `HasOwnershipTransfer` is false in this rootless Copy/pointer slice.
* The expected numeric-vtable presence Boolean is false. No DirectNativeImport,
  NativeExport, callable entry name, native-function-address role or prototype
  NativeVTableMethod payload can coexist. This is a public synchronous instance
  method role, never a constructor, property, static/helper implementation
  or callback.
* The mandatory revision-95 summary byte is zero. Keep the revision-93
  general-dependency collection and revision-92 output collection on this
  same declaration; reconstruct their disjoint normalized view normally.
  No field-copy promise is inferred for opaque native outputs.

When `foreignInterfaceRef=0`, preserve the complete ordinary revision-95 row,
its nonempty contract grammar and existing Import/body rules. The new
discriminator is not an alternate parser selected by an empty contract:
decode framing, then validate the entire tagged relationship. The reader
must postpone role-dependent contract/output checks until the variant and
directory references are available. An empty string, missing body, output
role byte or dangling reference alone cannot select this role.

Reconstruct a receiver-aware foreign declaration/call identity in the shared
bound model. An empty in-memory body used by a host record is not an authored
implementation or a code-export target. The present BoundFunction flags and
numeric vtable intrinsic alone cannot represent this new declaration.

### Reusing NativeOutput without duplication

A nonempty existing `OutputContracts` collection uses its already proposed
`nativeRole=2` **only after** the above foreign relationship validates.
An empty collection retains its existing absent role header; the directory
still identifies a foreign member with no outputs. Direct imports keep role 1.
The prototype numeric-vtable role remains ineligible.

Use the exact cell owner's types, formation, selectors/states, dependencies
and publication protocol. Keep output-specific phase-1 dependency rows out
of the general collection; zero general rows do not erase required defaults
or independently required backing/code/owner facts. Both collections remain
owned by each definition or original expectation, not by a shared provider
summary, slot, raw pointer, result enum or directory-row number.

The private signature counts **explicit formals only**. NativeOutput ordinal n
and an explicit-formal dependency n select formal n, not receiver position zero.
The logical receiver is a separate source with ordinal zero. If lowering or
the current bound-function array places the receiver at position 0, explicit
formal n maps to position **n+1**, including native inputs, output reservations
and source recipes. Require `explicitFormalCount + 1 <= 256`; the separate
64-argument call limit includes that receiver.

## Exact call target and operation preservation

Revision 96 changes the payload prefix of **every expression tag 5**:

```text
ExpressionEnvelope                 // unchanged type/span envelope, kind = 5
u8 targetKind
if targetKind == 0:
    String symbolicTarget          // ordinary revision-95 target
if targetKind == 1:
    u32 moduleFunctionIndex         // exact foreign definition/expectation
i32 argumentCount
Boolean hasParameterMapping
if hasParameterMapping:
    i32 parameterPosition[argumentCount]
Expression sourceOrderedArguments[argumentCount]
```

Only target kinds 0/1 are valid. Kind 0 must resolve an ordinary function;
it cannot discover or authorize a foreign member by name. Kind 1 must resolve
the precise foreign ModuleFunction, its directory owner/member, signature
and target record. A foreign call is not a captured function-pointer call,
OS-symbol import or call of an abstract provider export. Tag 41 remains the
exact existing **21-byte**, call-only output reservation; no new expression
tag or second call-ID field is assigned.

Every foreign call, including one with no outputs or only scalar inputs,
requires an actual enclosing unsafe scope/expression under the existing
operation codec. The declaration's caller-unsafe field propagates that
obligation; an unsafe declaration does not authorize unsafe operations in
its own body. A failed safety/output check cannot retry a different member.

For a foreign call, capture the receiver once as source argument zero,
unlabelled and mapped to private parameter position zero. Stage the explicit
arguments once in source order; their checked map covers explicit positions
1..n exactly once, with `argumentCount=n+1`. An omitted mapping means that
same positional order. Labels never reorder evaluation. Retain receiver and
earlier argument/backing facts through nested later arguments.

At Invoke, derive the slot from the **resolved declaration ordinal**, load
the vptr at receiver plus the selected offset, load that entry and pass the
**unchanged** receiver under the effective member convention. Keep the
identity candidate's Invoke boundary before foreign dispatch reads, physical
receiver/hidden-result mapping, failure gates and cell-owner Publish point.
There is no early vptr read, receiver adjustment, duplicate reservation,
result conversion or second publication.

No receiver authentication, IID lookup, ownership, retain, pin or code hold
is obtained by decoding this call. The actual native implementation reached
through the receiver can differ from the semantic declaration provider.
Preserve independently established receiver/table/backing and executing-code
lifetime obligations; a live semantic Module cannot stand in for them.

## Binding, source-free projection and replacement

Build a typed export directory from owned exported Bindings. Imports select
that exact Module/key/kind, then compare the **original complete retained
expectation**, including declaration-only requirements:

1. Resolve the interface and every declared MethodDef separately. Require
   exact owner correspondence and the same complete ordered member list;
   member names, physical rows and equal signatures are not a resolution
   fallback. Adding/removing/reordering a slot is incompatible with the
   retained flat contract, even if the caller uses only one entry.
2. Resolve all nominal signature origins and structural type contexts.
   Compare complete receiver/result/formal types and modes, layout/target,
   effective convention, foreign role and source-to-formal maps. Keep
   caller-unsafety and no-return obligations under their owners' supported
   relations; the foreign role independently always requires an unsafe caller.
3. Apply the output owner's **exact normalized output comparison**.
   Separately compare the complete native-input vector exactly after checked
   type/formal mapping, retaining provider/type/layout obligations.
   Apply revision 93's directed general-dependency relation independently;
   it is not permission to weaken an exact output source set. Keep revision
   95 summary comparisons for ordinary declarations; foreign summaries stay
   unavailable, never copied from a replacement body.
4. Preserve every original expected collection and using context after a
   successful resolution. Extra provider precision used by an optimization
   becomes an additional retained obligation, not a replacement for the
   original. A cache entry for a different provider/target/context is invalid.

A contract-preserving replacement may renumber/reorder physical tables,
change private bodies/images and add unrelated exports. It must still supply
the selected exported declarations and every retained owner/equality/layout
relationship. An export key redirected to another owner, split identity,
changed convention, receiver access, offset, member ordering, T, selector,
dependency or publication obligation fails the appropriate comparison.
Equal slots, DLL names, machine carriers or output enum values do not repair it.

Reference projection retains the interface's **entire** method list and all
signature/layout/native-input/output/dependency closure, not just called
members. Convert definitions to checked expectation Bindings under the new
using Module, with separately selected owner and member keys. Imported
references never become new nominal types or semantic re-exports.
Source-free use requires only these admitted records and selected provider
contracts, not source text, debug data, provider body inspection or a foreign
method address. Retain uncalled declarations and dependency-only providers.
No required fact may live solely in a source map or host cache.

The same rules apply to ordinary `.trm` linking, reference sets, graph
intermediates/leaves, packed contracts and source-free root restoration.
Existing private ordinary-name ambiguities remain explicit support limits;
they cannot select or merge a foreign declaration by accident.

## Native containers, sets, graphs and snapshots

Select the exact pair **outer 3 foreign-aware draft / inner 96**, including
all retained provider artifacts. This intentionally supersedes any prior
outer-3 interpretation that requires every semantic dependency edge to carry
a code declaration or machine import, but only when inner 96 proves the
foreign interface/type requirement. The direct-import expectation table,
direct-import provider thunk, existing binary field widths/order and checksum
coverage remain unchanged. This amends outer admission and projection, not
the field layout. It is explicit draft agreement, not inference that old
version-3 artifacts have these rules.

| Boundary | Required foreign-aware behavior |
|---|---|
| Producer identity and exports | Owned exported foreign/type Bindings in a native producer belong to that producer's exact logical identity. They are **semantic-only exports in the inner directory**, excluded from PE export lists and `ProducerImageExports`. No abstract member thunk, PE forwarder or dummy implementation is emitted. Ordinary/direct-import code exports retain their existing checks. |
| Imported declarations and calls | Derive separate semantic foreign imports, ordinary code declarations and machine-call imports. A typed foreign call is not an IAT requirement for the semantic provider. Do not put its keys in the existing outer name-only declaration/machine-import lists or direct-import expectation table. That table and the direct-import provider leaf-thunk route remain unchanged. |
| Provider edges | Keep an existing TNPK/set/graph dependency entry for every selected foreign/type provider even when its code-name lists are empty. TNPK's consumer identity names its sole provider, not the consumer's using Module. Set/graph edges can carry empty code-declaration/machine lists **only when** the inner-96 directory or existing type-origin records establish a retained semantic requirement. Unsupported extra providers still exceed the existing closure limits; no implicit search is added. |
| Exact outer count admission | In this selected draft, the TNSP per-dependency code-declaration count may be zero through 1,024 rather than the current minimum one, subject to the preceding semantic-edge check. TNGP's existing zero-declaration edge check additionally recognizes inner foreign/type imports. Other counts, dependency limits, code-name uniqueness/order, nonempty image and producer code-export requirements remain unchanged. An unjustified empty edge is invalid. |
| Snapshots and catalogs | Consumer/Provider/GraphContractSnapshot retain both directories, all original expectations, typed call targets, exact schema and using contexts. Resolve/catalog selection includes semantic-only edges and detects conflicting selected revisions/case-ambiguous paths. Old `Declarations`/`CallImports` name arrays are only the code-symbol view, not the full semantic closure. |
| Type origins | Cross-check the new nominal origin bindings with existing graph structure/cleanup origins in their separate roles. The graph's name/origin rows neither replace exact type export keys nor acquire foreign interface rows. Preserve actual cleanup/code origins independently; mismatch rejects. |
| AOT and root JIT | Validate all semantic edges and provider images before affected image loading. AOT executes stored images. Root JIT restores typed root calls and expected facts, then emits receiver dispatch locally; provider DLLs stay precompiled. Do not regenerate providers, synthesize semantic imports as IAT entries or bypass a selected declaration provider to make replacement pass. |

A purely foreign-declaration `.trm` reference needs no code export. A native
package/graph provider still needs an independently admitted nonempty image
and its existing required real code-export shape. A foreign-only native
provider not satisfying that shape remains unsupported; do not fabricate
abstract implementations or weaken image checks. A mixed provider's unrelated
real code exports do not turn its foreign methods into PE exports.

Keep the current runtime-policy and native-profile derivation; these records
add no runtime ownership feature or new profile label. Validate their
rootless/Copy/private signature restrictions before using that derivation;
an empty foreign Contract string must not bypass type/support checking.
Actual executing-provider backing/code lifetime and foreign-failure transport
remain independent obligations, not supplied by the semantic dependency entry.

Recompute inner Contract lengths, outer payload offsets and SHA-256 after
encoding. Contract growth is not image length. Images must derive from the
same final checked facts/target/bindings as their metadata; regenerate affected
images rather than patching headers. A valid checksum/PE never bypasses inner
schema or binding admission. No image-only, empty-program, source-rebinding
or alternate-decoder fallback is authorized.

## Ordering, resources and rewriting

Canonical writers:

* Order origin rows by ordinal `(usingModuleIdentity, unsigned privateType)`.
* Order interface rows by ordinal `(usingModuleIdentity, localDeclarationKey)`.
  Preserve each method list's declaration order, never key order.
* Keep ordinary ModuleFunctions' existing relative order, then emit foreign
  functions in canonical interface-row/declaration-ordinal order. Remap
  function references and the entry point accordingly.
* Preserve the prerequisite output/general-dependency/summary ordering and
  native-input formal order. Preserve source-ordered call arguments.

Ordinary readers may accept reordered directories/functions with consistently
remapped references, then normalize. The prerequisite collections that require
strict ordinal ordering keep their own rules. A changed method declaration
order is changed dispatch meaning, **not** noncanonical row order. Duplicate
keys, ordinal ownership or conflicting origin rows are malformed, not harmless
deduplication opportunities.

Bound new table counts and their sums by the retained function/structure and
artifact budgets. `foreignInterfaceCount <= totalForeignMethods <= 1024`;
each method consumes one ModuleFunction. Bound origin rows by the actual
required `(using context, nominal type)` closure, including explicitly
published nominal export roots, and the existing node budget.
Check counts before allocation and all products/sums against remaining bytes:
a Binding's fixed prefix is four bytes; each String has its checked i32
extent; an origin adds four bytes; an interface adds its lookup String and
20-byte fixed fields; a member adds four bytes. Formal and native-input
extents are as specified above. Negative/truncated/overflowing counts,
uninitialized required arrays, unknown tags and nonzero reserved values reject.
No input length can allocate unchecked memory or reset a nested budget.

Decode and remap by field namespace/context:

* Module/declaration keys and every selected/using binding relation, including
  declaration-only expectations and nominal type closure;
* interface row references, method ordinals and forward/reverse function
  indexes; ordinary symbolic targets and tag-5 typed targets separately;
* complete result/receiver/formal/native-input types, struct/field identity,
  pointer/function-pointer tables, output inner types and tag-41 pointees;
* general-dependency/formal/receiver correspondence and revision-95 ordinary
  copy-origin paths, preserving their original nominal field context;
* entry indexes, source spans/maps when their operations change, outer
  dependency/type/cleanup origins, direct-import expectation pointees,
  lengths/offsets and checksums.

Resolve/compare each original expectation before replacing it with a local
reference. Retain the obligation even if packing removes a physical Import.
Rebuild occurrence-local call/reservation provenance after cloning/remapping;
do not persist runtime addresses, generations, verifier caches or authorization
bits. Clone context-dependent shared rows when necessary. Equal scalar bits,
private integers or strings in another namespace are not remapping references.
No byte substitution, signature-name matching or order-based provider choice
is a valid rewrite.

Producers establish schema-valid, truthful facts and authored unsafe
assertions. Consumers check framing, extents, namespaces, owner/member closure,
signature/role/target support and complete binding before affected use,
including uncalled retained declarations. They need not semantically re-prove
producer bodies or the arbitrary native receiver implementation. Optional
audits and provider-truth tests remain separate.

## Sequencing and implementation gates

This is design work only; no implementation or support inventory is changed.

Work that can proceed **before 93/94/95 codecs finish**:

* Add the typed in-memory nominal foreign declarations, explicit receiver and
  selected-member references, complete signature extraction/normalization and
  owner-aware shared traversal/rewriting.
* Implement/check source registration and binding under the separate identity
  candidate, using unrelated synthetic interfaces. Generalize role validation
  without admitting numeric prototype stubs or enum value 2 alone.
* Develop closure discovery, declaration projection, comparison/remapping and
  independent candidate-96 byte fixtures/negative vectors. Exercise local
  dispatch only where all existing type, cell, provenance, ABI and failure
  prerequisites actually exist.

Those activities do **not** authorize writing a partial 96. While the current
writer selects 92, affected foreign persistence/container use must reject,
including declaration-only requirements. Do not serialize missing general
dependencies as zero, replace wide scalar payloads with i64, omit the summary
byte, or silently project back to a name/slot.

At one complete destination-schema checkpoint, implement the full 92/93/94/95
composition and this delta together, or consume those already completed
prerequisites. Their prior intermediate readers/writers need not ship.
Update all exact header, role/type/layout/profile and producer-validation
allowlists, ordinary/graph reads, reference/set/graph merges, snapshots,
native import/export classification and image-validation projections.
Complete receiver-aware binding, mandatory verification, provenance and
lowering before enabling output role 2. Normal developer build/test commands
and final-input native qualification remain separate required evidence.

Unsupported in this candidate: standard/development `.trrm` foreign encoding;
export aliases/re-exports; nested/private interface scopes; interface values,
`ptr<I>` conversions or witnesses; generic/partial/inherited interfaces,
holes/adjustments, authored implementations/callbacks, method addresses,
Vectorcall/other targets; richer formal/default/retention modes; unencoded
nominal/optional/managed/reference/root/pin/owner carriers; IID authentication
or new resource-acquisition/adoption carriers; and any independently unsupported
native signature or failure path. This adds no restriction or guarantee to the
cell owner's separately authored raw-resource/wrapper boundary. Lossless scalar
persistence does not admit a foreign scalar without a target-native equivalent.
Existing TokenSpan/indexed-body and private ordinary-name ambiguity gates remain.

## Required acceptance cases

These are future cases, **not passing tests claimed for this document**.
Use public compiler APIs, independently authored bytes and unrelated headless
providers. Separate source/record admission, emitted member ABI, stored-image
execution and supervised root JIT evidence.

| Family | Required outcome |
|---|---|
| Identity and complete closure | Several methods, overloads and unrelated interfaces with equal method names/signatures/slot zero remain distinct. Retain all methods when only one is called, plus scalar, pointer and admitted nominal-aggregate output types and their exact origins. |
| Pointer cell versus pointee | Opaque void and admitted typed pointees retain their exact identities/access without acquiring pointee Copy/extent/initialization, checked dereference, lifetime or resource rights merely from the cell's pointer-sized layout. Missing independently required facts still reject the dependent use. |
| Source-free round trip | Strip source/debug/body data from references. Bind/restore the exact owner and each member with empty/nonempty output/general-dependency collections and zero foreign summary bytes. No name, body or native symbol is needed to recover identity. |
| Ordinary composition | Independent 96 bytes include resolved arithmetic, all revision-94 scalar widths, revision-93 non-output dependencies and an ordinary revision-95 nonempty copy-origin summary beside foreign declarations. Zero new tables still use 96 and the new tag-5 prefix. Verify tag-41 extent and adjacent-record boundaries independently of the writer. |
| Receiver/formal positions | Output at explicit formal zero, several outputs, receiver and input dependencies, reordered labels, mixed integer/floating/stack inputs and nested later calls. Preserve one receiver capture, source staging, n-to-n+1 conversion and the member ABI; no duplicate reservation/publication. |
| Existing cell outcomes | Retain the cell owner's success/complement, ignored/stale discriminator, cancellation and exceptional-indeterminate cases through imported calls. No output-field copy summary, pointee validity, ownership/adoption or failure settlement is invented by the identity codec. |
| Compatible replacement | Change physical interface/function/type/pointer rows, private bodies/images and unrelated exports while retaining selected keys and complete contract relationships. Reuse unchanged consumer/root/intermediate bytes; check both stored-image AOT and root-only JIT. |
| Incompatible replacement | Change provider/key/kind, selected member owner, complete member count/order, target/offset, receiver authority, convention, nominal T, output selector/source/phase or native input obligation. Reject every affected original expectation, including uncalled members and declaration-only edges, before image loading. |
| Directed versus exact facts | A supported general-dependency narrowing may satisfy a broad expectation; the output collection keeps its stricter exact rule. A provider-only stronger fact cannot overwrite the expectation. Ordinary summary guarantees still follow 95; raw foreign summaries cannot become present. |
| Malformed binding/directory | Reject missing/duplicate/orphan keys, wrong selected kind, imported exports, illegal visibility, wrong owner Module, missing member/type origins, conflicting using contexts, duplicate/multiply-owned function indexes, gaps in reverse ownership, wrong row namespace and out-of-range references. A slot or role 2 with no complete directory is insufficient. |
| Malformed signatures/calls | Reject non-unsafe roles, unsupported conventions/modes/retention, wrong readonly/writable receiver, body/static/import/export/function-address collisions, nonempty foreign Contract strings, numeric expected-vtable payloads, bad counts/native-input ordinals and typed targets naming ordinary functions. Name-selected foreign targets, receiver-as-output, wrong parameter maps and standalone tag-41 nodes remain invalid. |
| Counts and arithmetic | Zero interface tables; nonempty interface with zero/oversized methods; total methods/function-budget boundary; negative/truncated String/counts; reserved/Boolean domains; checked String/Binding/formal/input extents; aligned maximal u32 offsets, offset-plus-eight and slot-byte multiplication. Unsupported lowering limits refuse without signed truncation. |
| Rewriting and diamonds | Reorder directories/functions/types/pointer tables, clone contexts and preserve each using Module's original contracts across sets and diamonds. Same-named distinct nominal types cannot be interned by layout; existing unsupported ambiguities refuse explicitly. Remap typed calls and void-typed reservation pointees, not scalar payload bits. |
| Native semantic-only edges | TNPK single-provider, TNSP zero code-declaration dependency, TNGP root/intermediate and TNPK leaf snapshots retain semantic providers without foreign IAT/export entries. Check the original direct-import expectation/thunk route alongside them, no unjustified empty edges, graph/catalog selection and precompiled-provider preservation. |
| Native image/support gates | A checksum-valid inner corruption still reaches and fails semantic admission. No image load/invocation occurs on rejection. Foreign-only `.trm` references work when implemented; a native provider lacking the still-required real code/image shape refuses without dummy exports or weakened image validation. Actual receiver code lifetime remains independently required. |
| Exact-schema selection | Unknown/92/93/94/95 inner revisions, current outer-3/inner-92 artifacts, mismatched snapshots and dishonest header edits never select alternate decoders or an old tuple meaning. Recompute outer checksums on negative fixtures so the intended inner/binding gate is exercised. |
| Trust boundary | A structurally valid but lying native implementation or producer is a truth violation, not proof that consumers must inspect arbitrary code. A fixture may detect its behavior; structural acceptance alone is neither ABI nor runtime evidence. |

## Remaining approval and support gates

The private decision is complete: **96 = exact 95 + this directory,
foreign-member variant and typed tag-5 target**, inside the explicitly selected
outer-3 foreign-aware draft. No further revision, interface identity,
signature authority or output-codec choice is deferred within this candidate.

Formal approval of this composition and the separate source/standard identity
and output candidates remains outstanding. Implementing 96 still requires
the complete prior candidates, nominal binding/closure preservation, all
receiver/output/dependency and actual target/runtime support, native-container
classification and the acceptance cases above. Record support and final
patch/configuration/input evidence only after those gates pass. This proposal
does not accept standard `.trrm`, general foreign interfaces, resource wrappers,
general nominal persistence or the active compiler milestone merely by selecting
this private codec.
