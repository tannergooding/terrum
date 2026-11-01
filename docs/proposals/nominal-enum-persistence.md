# Raw nominal enum declarations and values

**Status: unapproved candidate; provisional implementation direction only
under the user's explicit missing-contract workflow. This does not update
normative owners or allocate registry entries.**

## Decision and scope

Select one logical declaration/value contract and **two independently
selected physical carriers**: Standard TypeDef plus enum-specific required
rows and the existing frozen-value leaf; private semantic modules' nominal
origin directory plus a mandatory enum directory and typed constant leaf.
Private modules do not become Standard TRRM or have to embed its tables.
Implementation of either carrier is not implementation of the other.

The bounded domain is complete, module-level, public, nongeneric primary
**int32-backed enums**, including unrelated enums, duplicate-valued members
and unnamed values. Preserve an actual Flags declaration fact without
granting flags operations or inventing its source annotation schema.
Wider/native-width bases, nested/file/friend scopes, incomplete declaration
parts and additional enum operations retain their independent gates. This
is a carrier boundary, not a restriction on accepted enum source semantics.

This fills only the raw-enum persistence deferral in
[ordinary enum forwarding](nominal-enum-constant-forwarding.md) and
[Core registration](native-calling-convention-core-registration.md).
It selects no attribute-schema/registration codec, production Core provider,
host-configuration change, new enum operation or native convention.
[Native Core provider configuration](native-core-provider-configuration.md)
remains unchanged. Already normalized native contracts need neither carrier
unless a raw enum obligation independently survives.

## Evidence and ownership

The [specification index](../specs/README.md) and
[representation index](../specs/representation/README.md) lead to the
following distinct owners. Source observations below are not newly run
builds, tests or execution evidence.

| Inspected owner | Existing fact and consequence |
|---|---|
| [Grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [complete types](../specs/language/types-and-normalization.md), [constant domains](../specs/language/constant-generics.md#canonical-value-identity) | Enums already have nominal identity, checked integral member constants, permitted duplicate values and unnamed underlying values. Members inherit the enum's access; they are not independent visibility-bearing source declarations. |
| [`BoundEnumTypes.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundEnumTypes.cs) | The current bound directory carries exact enum/member identities, int32 bases, public/primary/Flags facts and member values. Its type range is distinct from inline structures. This is not a serialized directory. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | CreateProducer/CreateConsumer reject retained enum type uses; required-type discovery and RemapType also reject them. Module has no enum directory. Write selects exact private 92 and ReadModuleHeader accepts only 92. Reading an i32 in GetType does not establish an enum definition or consumer support. |
| [`TrRmExecutableCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs), [`Register`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.Register.cs) | Enum preflight/type/layout paths reject the unassigned nominal encoding. Standard scalar-return loading additionally excludes TypeDefs and admits primitive results only. Removing a producer guard alone cannot enable a consumer. |
| [`TypeDef reader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableReader.cs), [`writer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmTypeDefTableWriter.cs), [registry](../specs/representation/trrm-1.registry.json) | The writer validates through the reader; TypeDef mask `0F` and its two-bit kind admit struct/class/interface, not enum. Existing nominal TypeSpec forms can be reused after an explicit kind/record amendment. |
| [`FrozenValueNode`](../../sources/Terrum.CodeAnalysis/Modules/TrRmFrozenValueNodeTable.cs), [`constant consumer`](../../sources/Terrum.CodeAnalysis/Modules/TrRmConstantSourceValueValidator.cs) | Kind 1 is structurally recognizable, but selected scalar payload validation requires an unqualified registered atomic type. MaterializeFrozenScalar has no nominal-definition input. A type reference and four Blob bytes alone are insufficient. |
| [`NativeCoreRegistrationTests`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/NativeCoreRegistrationTests.cs) | RegisteredEnumValuesForwardThroughOrdinaryConstEval checks retained enum typing, native image emission and explicit private/default-emitter rejection. Other cases exercise unrelated enums and exact-domain failures. They do not assert this proposed wire contract. |
| [`TypeDef tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmTypeDefTableReaderTests.cs), [`scalar materialization tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFrozenScalarMaterializationTests.cs), [test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj) | Active NUnit sources cover current flags/ranges and primitive materialization. The project has no Compile exclusions. These sources identify regression locations, not passing enum-codec evidence. |

[Persistence](../specs/representation/persistence.md) owns the separation of
standard interchange and private persistence.
[Logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms)
owns the typed frozen-value family;
[metadata](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics)
owns rows, references and Module ownership;
[clauses](../specs/representation/contract-clauses.md) retains callable
applicability. No Effects, NativeCallable, OutputState or Dependency
reinterpretation is needed.
[Binding/replacement](../specs/representation/binding-and-replacement.md),
[reader trust](../specs/representation/reader-trust.md),
[canonicality](../specs/representation/encoding-canonicality.md) and
[format versioning](../specs/representation/format-versioning.md) govern both
carriers' retained meaning, without standardizing the private bytes.

## Logical identities and binding

* **Type:** the exact selected nominal declaration T, never its lookup
  spelling, underlying type, private number or physical row. Each using
  Module retains its own original expected declaration, selected producer
  Module/export key/kind, unambiguous provider revision and binding context.
  An expected TypeDef is a proxy for the selected definition, not another T.
* **Member:** `(T, producer-assigned member declaration key)`, with an exact
  owner, source lookup name, declaration ordinal and checked int32 value.
  The key is nonempty ordinal decoded text without zero scalars, unique
  within T, supplied by the actual declaration producer. It is not inferred
  from name, value or ordinal and is **not an independent Module export key**.
  The existing adapter's actual member identity must map explicitly to this
  key/owner; unsupported identity mappings reject rather than fabricate it.
* **Value:** `(complete normalized T, mathematical int32 value)`. Member
  provenance does not follow copies. Equal-valued members of T yield one
  value but remain different declarations. Every int32 container is a valid
  ordinary T value, including zero and values naming no member.
* **Declaration:** retain the complete ordered member list, base, effective
  namespace/name for ordinary lookup, public access, primary authority,
  Flags fact and actual target-specific
  Blittable commitment. The bounded route publishes a complete snapshot,
  not a subset of members observed in one body. Empty member lists are valid.
  There are no instance fields, formals, cleanup or hidden scalar field.

Resolve the exact enum owner before resolving its members. An imported enum
expectation retains its own complete snapshot; binding maps each expected
member to the selected owner's actual declaration by its explicit member key,
then checks name, ordinal and value. No search for another same-shaped enum
or same-name/value member is allowed. Ordinary lookup *within* an already
selected enum still selects its actual declared member by source name.
Ordinary transparent aliases normalize to T; this introduces no wire aliases.

For this complete-snapshot carrier, replacement preserves the namespace/name,
base, member key/name/ordinal/value list, Flags/access and retained
representation facts exactly. A changed or added member does not satisfy that snapshot merely
because old values remain in range. This is a deliberately bounded exact
comparison, not a new general promise that every enum API must be closed.
Check every independent expectation and any stronger relied-on fact; never
copy the replacement's own expectations over the consumer's originals.
Changing an unpromised ordinary helper body remains distinct from changing a
retained enum/member or published constant promise. Frozen values stay frozen;
replacement does not rerun their construction.

Source binding/evaluation still needs the actual eligible ConstEval program,
forms and dependencies when further evaluation is required. This carrier
does not supply those independently gated codecs or permit reference stubs
to stand in for that program.

## Standard candidate

### Allocation and representation

The following are **unapproved candidate assignments**, not current registry
entries. All multibyte words are little-endian.

| Item | Exact candidate |
|---|---|
| TypeDef, existing 44-byte row | Expand nominal kind to bits 2..4: `1=struct`, `2=class`, `3=interface`, **`4=enum`**; mask `0x1F`. Kind zero and 5..7 reject; visibility and all other definitions' masks are unchanged. Public `TypeDef.flags` are exactly `0x00000012`; these are distinct from `EnumDef.flags`. |
| Required section **`0x0053` EnumDef**, stride **20** | `u32 ownerTypeDef, TypeSpecRef underlyingType, u32 flags, u32 memberFirst, u32 memberCount` |
| Required section **`0x0054` EnumMemberDef**, stride **20** | `u32 ownerTypeDef, u32 ordinal, str declarationKey, str name, i32 value` |
| Feature **`18/1` NominalEnums** | Governing edition 1; immediate prerequisite **`11/1`** under the fixed-scalar registration selected below. Covers this complete declaration/member/type/value, binding, rewrite and materialization contract, not general enum operations, source schemas or evaluator programs. |

Both sections have required policy `0000`, existing directory/count framing
and one-based 24-bit table references in u32 words with zero high bytes.
Section numbers do not select DefId kinds. T uses existing TypeDef DefId
kind `0x13`; **no member DefId kind is allocated**. A member handle is a typed
EnumMemberDef row in its owner's checked range, resolving to `(T,key)`;
no selected polymorphic field needs a member token. The Core descriptor's
in-process declaration-kind enum is not a wire DefId assignment.
No new Body token, clause,
opcode, instruction length, prefix/escape, TypeSpec form or header field is
allocated. Existing Import/Export target sets remain unchanged: export/import
the enum TypeDef, not its member rows. No MethodMap/FieldMap/TypeMap or
ImmutableData target set gains an enum-member form.
Registered optional reference slots can name the new table through their
existing row-selector schema.

The whole [registry](../specs/representation/registry.md) has been considered:
sections through `004B` are assigned; candidate
[foreign-interface metadata](native-output-foreign-interface-method-identity.md#proposed-standard-metadata-delta)
uses `004C`; [general predicates](general-predicate-graph-schema.md) uses
`004D..0052` and TypeSpec `10`. Features 1..13 have existing assignments or
interacting output proposals; candidates 14/15/16/17 cover roots,
copy-origin, foreign invocation and general graphs. Definition candidates
[20](associated-type-definition-identity.md) and
[22..24](immutable-data-definition-kinds.md) have different owners; the
latter's alternative use of 25 is not a selected assignment or reservation.
Selecting 0053/0054 groups this new declaration family without
reinterpreting those records. Reallocating unpublished IDs would be legal,
but supplies no width/capacity advantage here. Unused 0012, F4..FE, FF,
optional sections and UnionCaseDef/FieldDef are not enum encodings.

### Owners, types and values

Exactly one EnumDef belongs to every kind-4 TypeDef, including an imported
expected TypeDef; no EnumDef may belong to another kind. Inherit Module
ownership through that TypeDef. The enum has public visibility and empty
field, generic and Contract ranges. It retains the existing required TypeDef
Layout range: one default row and any exact-target variants. Enum Layout rows
must use `layoutKind=Auto`, zero alignment and packing, and an empty
LayoutField range; this metadata does not imply a source `Layout` annotation.
Storage derives from the declared integer base, not a zero-field Auto struct
or fabricated Transparent field. No InterfaceDef or UnionCaseDef may use
this enum as its declaration owner.

`underlyingType` is a required complete TypeSpecRef resolving to unqualified
built-in int32, atomic code 08; equivalent TypeSpec-row and F2 spellings
agree. No nominal wrapper, other enum, generic formal or same-width type
satisfies it. `EnumDef.flags` permits only bit `01=Flags`; remaining bits
reject. This is separate from `TypeDef.flags`, which carries visibility and
nominal kind.

The existing per-target `Layout.flags` bit `01=Blittable` carries an enum's
actual representation commitment; no second target-profile field is added to
EnumDef. The default Layout row remains Auto with flags zero. A Blittable
enum has a complete Auto Layout variant for the exact committed target
profile, with the Blittable bit set and no fields or explicit alignment/
packing. The bounded candidate supports only profile **(1,1),
windows-x64-native/1**; other target profiles are unsupported and must not be
inferred from the underlying scalar or `Copy`. Readers validate the enum
specific Auto/Blittable combination against the selected declaration and
profile. The existing NativeBoundaryContracts `1/1` requirement still applies
to a module that publishes any Blittable Layout variant; `18/1` does not
replace or weaken that gate. The source rule that enums have no Layout
annotation is preserved.

The member range is `(0,0)` when empty, otherwise a checked, contiguous,
exclusive range of EnumMemberDef rows. Every member row belongs to exactly
one enum range, has the agreeing owner and ordinal `0..memberCount-1`,
and nonempty, valid source member name and declaration key. Names and keys
are independently unique within T; duplicate values are permitted.
Members inherit T's visibility and carry no separate visibility flags,
FieldDef storage, initializer program or independent export. Keys preserve
declaration identity when physical rows move.

Use existing TypeSpec form **02** for an in-artifact enum TypeDef and **03**
for its using-Module-owned TypeDef Import. Their row payloads and six-byte
extent are unchanged. Unqualified value T has zero qualifiers; any
context-permitted qualification retains the existing complete-type rules,
including the special Span-element-only init view. A complete type field names
the corresponding TypeSpec through TypeSpecRef; a Body TypeMap may use its
existing TypeDef/TypeSpec/Import token. F2 08 means int32, never T.
Nested pointer/ref/indexed-family forms and function-pointer anchor signatures
retain the actual enum TypeSpec wherever T occurs. This changes neither
their qualification rules nor their independent invocation contracts.

An enum frozen value uses the existing chain:

```text
ConstantMap:     TypeSpecRef T, u32 ConstantSource row
ConstantSource:  u32 kind=1, u32 FrozenValueNode row
FrozenValueNode: TypeSpecRef T, u8 kind=1, u8 flags=0, u16 reserved=0,
                 u32 scalarBlobRef, u32 childFirst=0, u32 childCount=0,
                 u32 activeUnionCaseDef=0
Blob:            exactly four bytes, int32 two's-complement, low byte first
```

The source/node/Blob references have their own typed namespaces; the complete
ConstantMap/use type and root type must resolve to the **same T** in the
actual using Module. Require a nonzero Blob reference of exactly four bytes.
There is no payload-local tag, length, member reference, sign extension or
padding. The frozen root must be the unqualified value type, not an init view.
EnumMemberDef.value likewise denotes its owning T's checked member
value, not an int32 value that a decoder may retag as an arbitrary enum.
Intern values by T and bits, not by base/bits alone. Blob bytes may be shared
without sharing the different typed roots. Unnamed values remain legal;
the Core convention schema's separate closed mapping can still reject them.

Retained enum declarations, signatures, members, values or nested type
closures require effective 18/1 in **each using Module**, including
declaration-only and uncalled uses. Derive 11/1; another Module's manifest
cannot supply it. Preserve all conditional native/layout/predicate features.
Presence of unrelated common value-table framing alone does not require 18.
An explicit enum export is a retained root even if no callable uses it.
An unused front-end registration is not itself an export; it may be omitted
only when no raw declaration/value/type or other retained obligation needs it.

### Exact schema selection and Feature composition

Select [scalar registration](scalar-frozen-value-registration.md)'s closed
24-scalar **11/1 with no unconditional prerequisite on 9**. It remains
scalar-only; 18/1 supplies the previously excluded enum interpretation of
kind 1. This explicitly amends the pending draft catalog, not Feature 11's
meaning inside an already selected older schema. Feature 9 remains pending;
18 requires neither unions nor general predicates. Feature 12 and ordinary
operation/runtime registrations are not completed.

Define the following closed out-of-band candidate selections. B is the
accepted `trrm/1-draft-layout-queries-1`; C/F/G are exactly the recipes in
[draft-schema selection](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities);
S is scalar registration above; E is this complete Standard delta.

| Exact candidate schema identity | Complete composition |
|---|---|
| `trrm/1-draft-nominal-enums-1` | B + S + E |
| `trrm/1-draft-copy-origin-nominal-enums-1` | B + C + S + E |
| `trrm/1-draft-general-predicate-nominal-enums-1` | B + G + E; G already includes S |
| `trrm/1-draft-foreign-invocation-nominal-enums-1` | B + F + S + E |
| `trrm/1-draft-copy-origin-foreign-invocation-nominal-enums-1` | B + C + F + S + E |
| `trrm/1-draft-foreign-invocation-general-predicate-nominal-enums-1` | B + F + G + E |
| `trrm/1-draft-copy-origin-foreign-invocation-general-predicate-nominal-enums-1` | B + C + F + G + E |

These are explicit definitions, not a flag/powerset or string-concatenation
decoder algorithm. No C+G-only or root/pin composition is selected. Keep
existing Standard, historical CopyOrigin, foreign-invocation and Development
selections unchanged. Every candidate retains `(1,0)` and requires exact
agreement on this whole schema/catalog before decoding or writing. Features,
filenames, version equality and plausible rows never choose it.

In all E compositions, 11 uses S's scope/empty prerequisites artifact-wide,
even for a Module with no enum use. Keep the selected base's other catalog
rules: F's bounded 10 has no prerequisites; G's 17 requires 10 and its still
incomplete 12 requires 11/17; without G, retain 12's 10/11 prerequisites.
In G+E, its existing Literal operand may reference this admitted enum leaf,
requiring 18/11 as well as the surrounding graph requirements. This does
not add enum equality, primitive operations on enums, or enum admission to
the six int32/uint32/bool result selectors.

Each dependency artifact has its own explicit selection. Packing chooses one
named destination that preserves all retained obligations or declines.
An unsupported complete selection fails, not a retry with fewer capabilities.
“Revision 1” fixes the complete proposed contents, not future text at linked
paths; later draft amendments require renewed exact agreement.

## Private candidate

### Complete composition, not a revision-byte shortcut

Amend the **unimplemented private i32 revision 97**, rather than allocate 98
solely to preserve an unpublished earlier 97. Use one exact selected whole
draft, designated here **`terrum/private-semantic-97-nominal-enums-1`** out of
band; it is not an extra wire field or alias for earlier 97. The wire header
remains `TRRM` followed by little-endian **i32 97**, not u16 major/minor.

Its prerequisite P is the complete composition in
[direct-import expectations](native-import-semantic-provider-expectations.md#exact-schema-composition):
amended 92 arithmetic/NoThrow/output semantics; 93 declaration dependencies;
94 [wide scalar identities/payloads](scalar-wide-value-carriers.md#exact-private-schema-and-composition);
95 copy origins; 96 nominal origins/foreign declarations/typed call targets;
97 convention and resolution tokens, complete `fnptr#i` invocation contracts,
the [foreign-invocation pointer amendment](standard-native-import-function-pointer-contracts.md#private-persistence-interaction),
and the import-only direct descriptor/full unsigned input expectations.
The pointer amendment supersedes the earlier raw-output exclusion. None of
those fields or meanings is omitted or optional in P+E. The historical
[scalar-private draft](scalar-frozen-value-private-persistence.md) is not
an alternative 94 codec or an enum codec.

The enum delta below is mandatory even with zero enums. Every structured
producer, consumer, reference/set/graph-library reader and snapshot selects
this same whole P+E. Current exact 92 support does not implement it.
Keep outer **3**'s selected foreign/convention-aware TNPK/TNSP/TNGP field
layout, independently selecting this inner draft throughout its retained
closure. No outer version 4, extra image field or native-profile number is
needed. Reject unsupported revisions/selections before record decoding; no
historical decoder, `>=97` dispatch, optional trailer or fallback is selected.
Equal 97/outer-3 bytes cannot distinguish every overlapping old byte image's
provenance and do not establish schema agreement.

### Exact directory and type/value references

Reuse [candidate 96's Binding and nominal origin records](native-output-foreign-interface-private-persistence.md#candidate-binary-directory).
Extend `nominalTypeOrigin.privateType`'s target set from supported inline
structs to these enums, with the same owned-definition/export versus
original-import-expectation distinction. Require an origin for **every
retained enum**, not only foreign signatures. Never serialize process handles
or invent a provider export from an in-memory lookup name.

After the complete `nominalTypeOriginCount`/origin table and **before**
`foreignInterfaceCount`, insert:

```text
i32 enumDefinitionCount
repeat enumDefinitionCount:
    u32 nominalTypeOriginRef              // one-based, nonzero
    String lookupName
    i32 underlyingPrivateType = 2         // exact private int32 identity
    u8 flags                             // bit 01 = Flags only
    u8 reserved = 0
    u16 reserved = 0
    i32 blittableTargetRequirement        // 0=none, 1=Windows x64
    i32 memberCount
    repeat memberCount:                  // declaration ordinal = list index
        String memberDeclarationKey
        String name
        i32 value
```

Integers are little-endian; String is the existing i32 byte-length plus
strict UTF-8 codec. The fixed entry extent excluding lookupName and members
is **20 bytes**. Member keys/owners/names/values mean exactly the logical
contract above. Public access and complete primary status are required,
not optional flags inferred from missing fields. The target requirement is
the existing private semantic value: zero means no commitment and
`FixedLayoutStorage.WindowsX64TargetRequirement` (1) means the exact
Windows x64 commitment; all other values reject. It is not the Standard
TargetProfile pair `(1,1)` and does not add a private profile-ID scheme.
`CoreEnumDefinition` and `BoundEnumDefinition` carry this requirement, and
table validation, equivalence, remapping and persistence preserve it.

Enum entry index `i` has private i32 type **`356 + i`**, for
`0 <= i < enumDefinitionCount <= 668`; maximum code is **1023**.
These are explicit private candidate numbers, matching the inspected bound
enum range, not a formula to recompute from future verifier limits.
They are disjoint from scalar identities through 46, inline structures
100..355, managed types beginning at 1024 and existing pointer/fnptr ranges.
P+E therefore requires at most **256 inline structures**, matching bound
verification. Current ReadStructures' looser 1,024-row framing bound must not
let a structure index invade the new enum range; reject that input before
type resolution, rather than choosing a kind from whichever table is present.
The referenced origin's privateType must equal that entry's code. Each enum
origin belongs to exactly one entry and vice versa, with agreeing using
Module/context. A repeated selected nominal definition may have distinct
original expectation entries; check all before any semantic interning.

The directory may follow earlier binary type uses. Decode their closed type
codes without consuming a guessed payload, then validate the complete
forward-reference closure before affected use. Every enum code must resolve
to its actual directory entry/origin. Reject absent, duplicate, dangling,
wrong-kind or context-inconsistent origins and unsupported structural cycles.
If merging would give one physical row different meanings, clone/remap it
or refuse an unrepresentable merge; do not merge by qualified name.

Keep the prerequisite private Binding identity/key grammar and capacities
as explicit limits. An otherwise valid in-process Core identity outside
that grammar cannot be renamed to fit or asserted to be an owned local
definition. Production requires actual selected origins/exports and their
original expectations, not merely a Core descriptor containing strings.
All new declarations/members count toward the existing 4 MiB artifact,
100,000-node and existing depth/string/slot limits; counts and extents use
checked arithmetic before allocation. Both new counts are nonnegative i32;
member totals consume the shared node budget, not one fresh budget per enum.
The 668 enum bound is private, not a
new Standard row limit or portable minimum.

At every grammatical textual type position in a declaration Contract use
exactly **`enum#i`**, with the zero-based directory index in canonical unsigned
decimal: no sign, spaces or leading zeros except `0`. Its extent is
`5 + decimalDigitCount(i)` ASCII bytes inside the existing String.
Use it inside existing nested pointer productions too; `fnptr#i` still
references its separate complete signature table, whose binary types may
contain enum codes. A name, int32 spelling or older type spelling is not an
alias. Parse typed atoms before comparison; equal indices/strings in
different modules are not equality, and physical reindexing must rewrite
these grammatical references. Do not replace matching text in ordinary names.

Constant expression tag 1 keeps its type/span envelope and uses **four**
payload bytes when that complete type is an admitted enum:

```text
u8 kind=1, i32 enumPrivateType, i32 spanStart, i32 spanLength,
u8 payload[4]                       // exact int32 two's-complement bits
```

The extent is **17 bytes**, with no new tag/length/high-word marker.
Other scalar and non-scalar constant cases retain P's exact meanings.
Unknown types never select this width or an eight-byte fallback.
Bodyless/reference expectations retain all `enum#i` roots and their complete
directories before Function erasure. Definitions, constants, fields, variables,
reservation pointees, declared native inputs/outputs and nested pointer/
function-pointer contracts all use typed directory references, not name-based
fingerprints. No unrelated private constant-publishing family is activated.

## Consumption, materialization and transformation

Producers establish schema-valid, truthful declarations/values and check
authored formation, including range/successor failures. Consumers check
selected framing/strides, checked counts/ranges, exact kinds/flags, source
names and keys, owner/key/ordinal uniqueness, references, complete type
agreement, requirements and selected bindings before affected use.
They do **not** re-evaluate member initializer programs or semantically
re-prove arbitrary producer Bodies. Structural validity, producer truth,
provider authentication and target/runtime support remain distinct.

Reconstruct checked enum definitions and context-local type mappings **before**
materializing a typed value. Copy the four owned payload bytes into the shared
ScalarBits model with unused high bits zero, retaining exact T. Validate the
selected target/actual declaration commitment before any use requiring
Blittable. Under Windows x64, storage is int32's four-byte, four-aligned
container; that fact grants no foreign signature compatibility, conversion,
unsafe authorization, backing, lifetime or resource ownership. Monomorphic
runtime copies need no extra dynamic tag. Lower to the integer carrier only
after exact typing and applicable representation/call checks. Do not invoke
the primitive scalar materializer and then retag its result as T.
Aggregate fields and independently admitted layout queries derive this same
representation while retaining the queried/contained T and selected target.
Enum metadata alone does not require Feature 13; retained queries still do.

Linking, trimming, sets, diamond graphs, snapshots and native consumers must:

1. Retain every original using-Module binding and exact enum/member/value
   expectation, including declaration-only roots and nested signatures.
   Do not flatten different Modules' selections into the current Core map.
2. Resolve identities before constructing enum/type/member maps. Canonical
   Standard EnumDef rows follow their owner TypeDef order; member ranges
   follow declaration ordinal. Ordinary valid physical EnumDef permutations
   remain readable. Preserve member keys/ordinals independently of row moves.
   Private entry order follows the selected origin-table order restricted to
   enum origins; reassign its context-local codes/atoms together.
3. Rebuild Standard Module cursors, TypeDef/EnumDef/member ownership ranges,
   Layout/LayoutField ranges and TargetProfile references, Import/Export and
   typed member-row references, nested TypeSpecs/MethodSigs/anchors, frozen
   source/node/Blob references and all affected Body maps/records, Block/code
   offsets and SourceMap/reference slots. Clone context-sensitive shared rows
   instead of modifying unaffected uses.
4. Remap every private enum code, origin reference and `enum#i` occurrence,
   recursively through structures, pointers, fnptr signatures, bodyless
   contracts, general/output/copy-origin collections, indirect calls and
   restored bodies. Preserve pointer qualifications and complete invocation
   facts. The scalar enum payload itself contains **no references**.
5. Rebuild outer Contract lengths/checksums and remap native expectation type
   references in the **importing** Contract's universe. Revalidate cached
   consumer/provider snapshots against their exact schema, original context,
   selected revision and dependencies. A matching image/hash does not repair
   missing semantic facts; regenerate incompatible derived images.

No byte substitution, integer-code equality, same-name/value inference,
silent dropping or source rebinding is a preservation algorithm. Already
lowered AOT images and restored/root-JIT programs retain separate ABI and
code-lifetime gates; this proposal selects no fallback between them.

A source-free consumer of **normalized** native conventions/resolution facts
uses their selected existing contracts. It must not reacquire Core, rebuild
source schemas/defaults or retain a synthetic enum runtime dependency solely
because source normalization used T. Conversely, an independently retained
raw-T signature, value, declaration or evaluation dependency still needs its
complete admitted carrier and cannot be dropped with the source annotations.

## Alternatives and adoption destinations

The selected amendment reuses nominal TypeDef/Import identity, existing
per-target Layout rows and the typed value tree, adding only enum-specific
declarations and a private directory projection. A substantive alternative is
a generalized kind-specific nominal-declaration graph, migrating
struct/class/interface/enum/union records and replacing private structured
types with one complete metadata graph. That could also address broader
private name/alias limitations, but requires new ownership, reference and
conversion rules across unrelated declarations. It is not necessary to persist
this bounded domain. Adding enum fields to every TypeDef would avoid one
supplementary table but enlarge unrelated rows; reusing struct fields/union
cases would misstate the declaration, not minimize the same contract.
Reallocating draft IDs or adopting Standard metadata as private IR is
permitted, not required for compatibility or byte economy.

On adoption, amend the registry and metadata flags/sections/typed references,
the enum-specific Layout-row validation rule and reference rules; add the
logical enum/value requirements, Feature registration/catalog and exact schema
compositions; add the enum snapshot application to binding/replacement and
reader/reference checking; add canonical orders and conformance vectors.
Update private implementation references only after implementing P+E with
evidence.
This proposal changes none of those owners now. It supersedes only the two
earlier proposals' unselected raw-enum codec boundary for the bounded domain;
their source/registration/ordinary forwarding rules remain intact.

## Required acceptance cases and independent gates

These are future public-API and independently authored artifact tests, **not
tests run by this documentation change**. Writer/reader round trips alone
are insufficient.

| Family | Required outcomes |
|---|---|
| Definitions and nominal values | Source-free publication/import of two unrelated public int32 enums preserves keys, owners, Flags fact and target-specific Layout commitment. Each enum retains its default Auto Layout row; a Blittable enum adds the exact target-profile variant, and a source Layout annotation remains invalid. Empty and duplicate-valued member lists are valid; duplicate names/keys, wrong owners, missing/extra directory rows or unsupported bases/scopes reject. |
| Exact leaf bytes | Independently encode 0, -1, int32 minimum/maximum and unnamed values. Four-byte -1 is `FF FF FF FF`, minimum `00 00 00 80`, maximum `FF FF FF 7F`. Reject 0/3/5/8-byte Blobs, missing Blob, children/union case, nonzero reserved bits and invalid type/node agreement. Out-of-range producer formation rejects rather than wrapping. |
| Nominal negatives | Equal bits of T, another enum and int32 remain distinct in calls, conditional joins, fields, constants, nested pointer/fnptr signatures and interning. Same names/values from another selected Module do not satisfy T. A member row cannot masquerade as a type/field/import or bypass its owner's binding; a fabricated member DefId has no assigned interpretation. |
| Exact schema/catalog | Independent Standard artifacts use 18/1 and derived scalar 11/1 without union 9. Missing using-Module requirements, unknown/conflicting revisions, wrong schema selections, TypeDef kinds 0/5..7, reserved flags and old-profile use reject. Equal headers or valid common-subset bytes are not provenance detection or decoder selection. |
| Private whole composition | Independent P+E fixtures include all mandatory prefixes, empty/nonempty enum directories, earlier 93..97 collections and full signature/direct-import amendments. Tag-1 enum records are exactly 17 bytes. Missing prefix, truncated/trailing payload, bad origin/code/atom, explicitly selected older/partial 97 and unsupported nested selection reject. Include old-i64 fixtures with invalid following fields/trailing bytes; do not claim to detect the history of overlapping structurally valid bytes. Never retry. |
| Capacity and references | Exercise Standard typed row 1/`FFFFFF`, zero/reserved-high-byte and wrong-table failures; Body indices 0/65535 and one-over counts. Private indices 0/667 yield codes 356/1023; count 669 and code without a row reject. Structure code 355/class 1024 remain distinct; structure count 257 rejects. Exercise exact/one-over private byte/node/string limits and checked extent arithmetic. |
| Remapping and retention | Reorder Modules, TypeDefs, EnumDefs/member ranges, origins, value rows and pointer/signature tables; preserve keys/ordinals/types and payload bytes resembling tokens. Bodyless/uncalled declarations, set/diamond bindings, root/intermediate/leaf snapshots and imported native expectations retain all original contexts. Clone divergent shared meanings or refuse unsupported merges. |
| Replacement | Physical reindexing with the same declarations succeeds. Changed base, member key/name/order/value, Flags/access or retained representation commitment fails each affected snapshot. Compatible ordinary body changes remain permitted; already frozen values are not recomputed. A new provider cannot manufacture the old expectation. |
| Cross-feature | Copies/aliases/conditionals and already supported runtime or ConstEval paths preserve T after restoration. Further required evaluation needs its actual program/forms/dependencies. G+E enum literals retain 18/11/17 closure; enum primitive operations/result selectors do not appear by carrier reuse. Native inputs/outputs preserve exact pointee, unsafe, provenance and publication obligations. |
| Normalization and target | Normalized native contracts remain source-free with no otherwise unused Core/enum directory. A retained raw T without its carrier rejects. Unknown convention values still fail the separate native mapping. Missing Blittable/target/call support declines affected materialization or lowering; successful serialization/emission does not establish execution. |

No unresolved enum codec, field-width, numeric-candidate or member-binding
choice remains within this recommendation. The independent gates are actual
implementation/qualification of each selected carrier and complete private P;
source-authored EnumDecl collection; source-independent attribute schemas and
registration publication; richer enum bases/operations/scopes; further
ConstEval/generic/value capabilities; and the selected target/native runtime
routes. None is automatically supplied by enum metadata.

The genuinely unsupplied production input remains the host-authorized Core
authority/adapter, exact provider Modules/revisions/exports, complete schemas
and actual representation commitments. This proposal selects none of them
and does not revise the host-configuration proposal. There is no new runtime
helper or provider selection needed merely for an admitted int32 enum copy.
Actual foreign libraries, unsupported calling conventions and execution
profiles retain their existing independent availability/support gates.
