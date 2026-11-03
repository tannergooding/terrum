# Structural fixed arrays in private semantic persistence

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract authorization.** This is a proposal-only decision, not formal
approval, format stabilization, compiler support or execution evidence. It
changes no compiler, test or normative specification. [Proposal index](README.md).

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.” This recommendation supplies that provisional direction;
adoption and implementation remain separate.

The unapproved [artifact-local metadata companion](native-fixed-array-private-metadata-boundary.md)
governs projection and replacement of metadata-only recipes in this candidate.
It provisionally supersedes those obligations below; the byte schema, flat
runtime boundary and approval status remain unchanged.

## Decision and scope

Select **private semantic-module i32 revision 100**, exact draft identity
**`terrum/private-semantic-100-structural-fixed-arrays-1`**:

1. Inherit the complete [private revision-99 schema](native-storage-address-private-persistence.md),
   itself the revision-92 payload with FieldMove 42 and three u64 tag-35
   storage-address operands.
2. Add one mandatory, possibly empty, structural array type table immediately
   after the native-pointer table and before the function count.
3. Add expression **43 Initialization, 44 Element, 45 ElementAddress and
   46 ElementWrite**, with the exact shapes below. Reconstruct their layouts
   and array-local layouts from the type table, rather than repeating geometry
   in every body node.

The executable scope is initialized **flat direct-local** inline storage:
exact ordered construction, checked selection/read, mutable element replacement
and unsafe typed element addresses, followed by ordinary admitted pointer
operations. Flat means E is unit or a positive-size, rootless Copy value with
the inherited 99 runtime representation; it contains no inline fixed-array
value or metadata-only pointer/signature described below.
**Revision 100 excludes array-valued elements and non-unit zero-payload
elements from all four operations.** Nested type identities remain
metadata-only; recursive expression framing does not admit recursive array
captures. These are fixed admission rules of this recommendation, not features
to enable when a backend acquires additional support.

Preserve the unapproved [source array direction](standard-native-fixed-array-source-contract.md):
identity `(FixedInlineArray, normalized complete E, uint64 N)`, exact N
ordered elements, `[]` only at N=0, and unit-array `A=1, S=B=0` with logical
initialization and indexed checks. This document selects no additional source
syntax, Core array registration, frozen/bulk initializer, foreign array ABI,
checked-reference carrier, nonlocal place or ownership policy.

E and every child operation must have a complete, independently admitted
**99-based private representation**. Revision 100 does not inherit candidates
93–98: general declaration dependencies, wide scalar values, copy-origin
summaries, general nominal origins/enums/foreign directories, complete
function-pointer/checked-reference contracts and interfaces remain separately
gated. In particular, int128/uint128 source arrays or indices need the
[wide-scalar composition](scalar-wide-value-carriers.md); retaining a u64 N
does not admit their value codec. Reject required unrepresentable facts;
never replace them with a convenient scalar, spelling or empty collection.

The table can describe nested types and types in retained full-artifact
descriptive field/pointer records without granting those types a runtime value
or place representation. It does not publish a type-only export or make
metadata presence a bodyless-reference root. The
[metadata-only boundary](#metadata-only-nested-and-zero-payload-types)
enumerates the permitted records and rejected operations. Field-rooted/nested
element **places**, whole-array parameters/results, globals/statics, descriptor
bases and managed/non-Copy arrays remain outside this direct-local operation
slice. Existing independently admitted flat whole-Copy/assignment contexts keep
their contracts; they cannot provide a carrier for an excluded nested element.

## Owners, historical evidence and alternatives

Accepted [private-persistence taxonomy](../specs/representation/persistence.md),
[format selection](../specs/representation/format-versioning.md),
[reader trust](../specs/representation/reader-trust.md),
[binding](../specs/representation/binding-and-replacement.md) and
[Windows layout](../specs/targets/windows-x64-data-layout.md) remain controlling.
The [grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments) and
[construction owner](../specs/language/construction-handlers.md) do not yet
approve the structural source experiment. Only this proposal would own the
new private records; no Standard allocation is amended.

The following source observations are the historical baseline for this exact
revision-100 candidate, not a live support inventory or test-run results.
Current private writer/reader support is tracked at revision 103 in the
[module inventory](../implementation/modules.md) and
[conformance handoff](../implementation/conformance-gaps.md); it does not
make revision 100 a compatibility alias or select this unapproved candidate.

| Inspected owner | Observed baseline boundary |
|---|---|
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), `NativeFixedArrayTypes`, array records | Array IDs occupy a separate host range starting at `1 << 29`. A row retains E, N, S, B, A and required alignment R; initialization has ordered children, while element/address/write nodes have a local slot and index. |
| [`NativeFixedArrayContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/NativeFixedArrayContract.cs) | Structural interning and geometry checking exist. N/S/B/A/R are unsigned 64-bit; unit uses `(s,a,R)=(0,0,1)`. Revision 100 persists those rows and validates nested and pointer/fnptr metadata recipes independently of runtime operation admission. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), `Write`, `ReadModuleHeader`, `GetType`, `PutExpression` | At this proposal's inspected baseline, writer and reader selected exactly 100. It defined a mandatory bounded 44-byte array table and tags 43–46 for flat direct-local operations. The current writer/reader select revision 103 and retain later private compositions; revision 100 remains a separate candidate, not the current selection. |
| Same file, `PutFunction`/`GetFunction`, `Calls`, `RequiredStructures`, `RemapType`, native-type mapping | Variable wire records remain unchanged; local layouts are reconstructed from rows. `Calls` traverses nested expression children, ordinary Link remaps/deduplicates array identities, and type spellings include normalized recursive array identities. Runtime locals/calls using nested or ptr-to-array metadata remain rejected. |
| [`ModuleCompiler.Graph.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.Graph.cs), `ReadGraphLibrary`; [`ModuleCompiler.ModuleSets.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.ModuleSets.cs) | Graph libraries and sets use the shared revision-100 table/body codec. In-memory set/graph reference merging remaps reordered nested rows and pointer/fnptr recipes; replacement and embedded-contract closure remain in progress. |
| [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), `RejectSourceNativeImports`; [`NativePointerModuleTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativePointerModuleTests.cs) | Private-module admission allows checked direct-local arrays while retaining foreign-import obligations. The producer/reader reject array-derived runtime contracts, nested value locals, ptr-to-array runtime carriers and non-unit zero-payload array operations. |
| [`ValueLayout.cs`](../../sources/Terrum.CodeAnalysis/Semantics/ValueLayout.cs) | The runtime value-layout codec still has no general fixed-array value case. Revision 100 persists local storage geometry and flat operations only; array-valued captures, temporaries and whole-element copies are not implied by the table. |

| Alternative | Assessment |
|---|---|
| Repeat full layouts in the four body records | Closest to the current bound nodes, but still needs a type table for exact identity, empty/unused types, pointer/field references and structural remapping. Two serialized layout authorities increase mismatch checking without adding meaning. |
| **99 + one array table + four flat typed operations — recommended** | Smallest complete persistence extension for the local experiment. Shares identity/layout authority, retains recursive non-array child-expression framing and needs no standard/private bridge or array-valued runtime carrier. |
| Admit array-valued elements in 43–46 | Would also have to select sized/aligned array values and captures, read materialization versus subplace identity, whole-element Copy/store, ptr-to-array payload access and zero-size nominal values. Revision 100 deliberately does not select those additional runtime contracts; parseable references and counts do not supply them. |
| Compose full 98, or redesign all private types/places into a common graph | Could unify richer references, generic origins and subplaces, but requires the entire 93–98 schema and its unresolved execution gates. Unpublished numbers may be reassigned; draft compatibility is not the reason to defer this substantially larger migration. |

### Allocation and exact selection

The private revision constants and proposal catalog were checked: **100 has
no competing private assignment in this checkout**. It is a candidate here,
not a reservation made by the accepted Standard registry.

Expression and statement tags are separate u8 namespaces. In 100, expression
tags 1–42 retain **99's** shapes/admission, including rejected span tags
32–34 and FieldMove 42; statements retain 99's existing shapes/admission.
Expressions 47–255 gain no meaning. No prefix, escape, reserved-bit extension
or statement tag is added.

Tags 43–46 are deliberately **revision-local**. Candidate
[checked-reference 97](checked-reference-private-persistence.md) assigns
42–45 to reference operations; [interface composition 98](source-interface-witness-private-persistence.md#expression-42-composition-not-two-interpretations)
retains FieldMove 42 and assigns 43–46 to its checked-reference family.
Neither interpretation belongs to 100. A later composition must explicitly
choose one complete allocation; equal-width operands or a failed decode never
select another meaning.

Keep magic bytes `54 52 52 4D` and write little-endian **i32 100**
(`64 00 00 00`) at offset 4. The named draft identity above documents this
one selection; it is not another serialized string. All structured producers,
consumers, graph libraries and embedded semantic Contracts select 100,
including scalar-only artifacts and empty array tables. Readers require
exact 100 **before records**, rejecting honestly declared 99, 92–98,
older/unknown revisions and Standard input. An exact-99 reader rejects 100.
No `>=99` admission, empty-table fallback, profile-based guessing or retry.

Audit every exact-membership gate, including tag 35, structure fields,
invocation buffers, retained-profile/producer validation, snapshots and graph
reads. Selecting 100 must select **the whole 99-plus-array schema**, not omit
inherited fields because a revision list stops at 99. Keep the existing
native-profile labels and TNPK/TNSP/TNGP **outer version 3** layouts; each
embedded semantic payload independently selects 100. Outer 3 is not an alias
for either inner 99 or 100.

Recommend **regeneration**, not historical readers or conversion support.
Rebuild older artifacts from actual source or independently retained complete
checked IR, including enclosing lengths/checksums and native bindings.
Changing a header is not conversion. A separately authorized converter would
need exact source-schema decoding, reference remapping and complete re-emission;
it may not infer a codec from plausible bytes. No reader can promise to detect
every dishonestly relabelled stream that happens to satisfy the selected shape.

## Array table and complete type references

All multibyte fields are little-endian, with **no padding**. The complete
100 top-level order is:

```text
99 header/profile/kind/runtime/source-grant/invocation-buffer fields
99 entry-point field
99 structures; classes; imported-structure names
99 native-function-pointer signature table
99 native-pointer type table
i32 arrayTypeCount
ArrayType[arrayTypeCount]
99 functionCount and complete function/declaration/body records
EOF
```

The new count is an ordinary bounded **i32**, not N or a byte length;
`0 <= c <= 100,000`. It is always present, even at c=0. Each row is exactly
**44 bytes**, making the component extent `4 + checked(c * 44)`:

| Row offset | Field | Width / meaning |
|---:|---|---|
| 0 | `elementType` | i32 complete private type reference E |
| 4 | `elementCount` | u64 N, including zero |
| 12 | `elementStride` | u64 completed byte stride S |
| 20 | `byteExtent` | u64 complete array extent B |
| 28 | `alignment` | u64 positive natural array-container alignment A |
| 36 | `requiredAlignment` | u64 inherited transitive explicit-alignment requirement R |

An independent row vector for the provisional source type `[uint32; 2]` is
`(E,N,S,B,A,R)=(3,2,4,8,4,1)`, encoded as `i32,u64,u64,u64,u64,u64`.

There is no name, nominal owner, target/profile index, Copy/initialized/unsafe
bit, hidden anchor or optional trailer. Layout is for the same selected
Windows x64 target as the inherited private representation. Different
target-dependent layouts require their own supported schema/target agreement,
not two interchangeable rows for one type.

Row i has **zero-based** private type code `0x20000000 + i`, encoded as i32.
It must lie in `0x20000000..0x3FFFFFFF` and `i < c`; with this table cap the
largest possible code is `0x2001869F`. Codes are not Standard TypeSpecRefs,
Body map tokens or one-based rows. Preserve the disjoint existing namespaces:
structs, managed qualifiers, fnptr `[2048,0x20000000)`, and native pointers
`[0x40000000,int.MaxValue]`, with each table's actual bounds. Reserved singleton
`PrimitiveType.NativeFixedArray` **20** is not an array identity or alias.
Unknown, negative or wrong-family references reject under the selected
private type rules; candidates do not become admissible merely by fitting i32.

Every existing private type-bearing field uses this same extended reference
domain: fields, pointer pointees, signatures where independently admitted,
variables, declaration/native contracts, expression envelopes and tag-35
carrier/element operands. Reference admission does **not** widen their enclosing
value, native-call, ownership or ABI domains.

Decode all type tables before resolving inter-table references or computing
array layouts. Earlier structure/pointer records can refer forward to this
component. Array rows may also be forward-referenced by other array rows;
ordinary readers accept reordered acyclic rows. Bounds, duplicate normalized
identities and cycles are checked using the complete selected tables, not a
temporarily empty table or a numeric “struct-like” fallback. Nominal declarations
are identity leaves; inline containment and pointer/fnptr recipe graphs have
their distinct cycle checks. Pointer edges do not expand pointee **storage**.
This adds no equi-recursive structural type interpretation.

### Identity, layout authority and canonical writing

Identity is **constructor + complete normalized E + u64 N**. S/B/A/R are
retained layout commitments, not identity substitutes. Resolve E's private
nominal binding and all pointer access/depth/fnptr contracts before comparison;
source aliases and extent spelling disappear, but nominal origins and inner
qualifications do not. Equivalent normalized array identities must have one
row, with exactly matching geometry; duplicates reject even if their geometry
matches. Equal byte layouts, local row numbers or same-named providers do not
make different E identities equal.

For completed E layout `(s,a,R)`, require its independently admitted rootless
Copy **type/layout** representation, then:

```text
A = max(1, a)
S = checkedRoundUp(s, A)
B = checked(N * S)
array requiredAlignment = R
```

Validate the entire E graph even for N=0. A and R retain the selected target's
valid alignment domains; zero A/R, invalid power-of-two alignment, invalid
positive-size/zero-alignment E, incorrect rounding/product or inconsistent
submitted geometry reject. R is derived unchanged, **not zero for “absent”**:
unit has R=1, as does an independently admitted Transparent-over-void nominal
E. Both retain `(A,S,B,R)=(1,0,0,1)` without becoming the same type. A nominal
zero-size row is metadata-only in 100 even when its full declaration/layout is
representable; no operation may substitute unit for that nominal E.

Use checked unsigned target-nuint arithmetic for rounding, products and
subsequent offsets; reject overflow instead of truncating/clamping. No i32,
u32, 4096-byte or slot-allocation cap is a type-identity/wire bound. Existing
i32 field sizes and native stack/slot limits still constrain their **uses**.
A compact row with a huge zero-stride N allocates no N-element graph or backing.

Canonical writers compute the complete closure for the artifact's retained
roots, then intern arrays by normalized identity and emit each once. Full
implementation artifacts retain the array/type closure of every retained
declaration, field/layout and admitted body occurrence, including valid
metadata-only recipes. Bodyless references and Contract-only projections have
different roots: existing legally exported runtime contracts and their
required declarations/dependencies. A public type name, implementation row
or metadata presence alone is not an export or projection root; revision 100
adds no type-only export/import identity.
Use deterministic **first-use, element-before-parent** discovery: scan retained
non-array records in their existing wire order and their type fields in field
order; scan function bodies in encoded preorder/child order. A discovered
array recursively visits E's structural array/pointer/fnptr recipes before
assigning its destination row; nominal field closure is traversed through its
actual declaration records. Do not use old numeric IDs as ordering keys.
Reject recipe cycles/depth/work exhaustion. Omit unreferenced array rows
canonically; readers may consume well-formed unreferenced rows. Re-encoding a
noncanonical row permutation rebuilds all affected type references.

For an inherited textual expectation that actually retains this complete
array type as a legal runtime contract, add this recursive private type
spelling, where U64Decimal uses invariant unsigned decimal without leading
zeros and no whitespace is added. Do not generate a type-only expectation for
an unrelated descriptive recipe:

```text
ArrayContract(E,N) = "[" + ContractType(E) + ";" + U64Decimal(N) + "]"
```

Existing non-array spellings stay unchanged. This is a private contract token,
**not source parsing**: `[3;2]` denotes an array of private scalar code 3, not source
`[uint32; 2]`. Nested arrays and pointers recursively preserve their actual
types; no `array#oldRow` or bare numeric array-ID fallback is allowed.
For a retained legal contract, check the token against the owning decoded
tables and original expectation; existing nominal visibility/layout checks
remain required. Bodyless projections retain only those legal contracts and
their relied-on dependencies, not every array description. Keep the 1,024-byte
strict UTF-8 String limit and reject unrepresentable contracts.

### Metadata-only nested and zero-payload types

**Type/layout decoding is distinct from operation admission.** Subject to the
same exact references, complete nominal declarations, geometry, cycles and
resource checks, revision 100 can decode, normalize, remap and re-emit the
following descriptive recipes when they are retained by a full artifact's
implementation closure, or required by an independently admitted non-array
contract:

* Array rows whose E is another fixed-array row, recursively, including
  dimensions with N=0 or zero stride/extent. Preserve every dimension and the
  exact immediate E; never flatten counts or turn a nested row into a scalar.
* Descriptive nominal field/layout records containing those array references,
  but only when the entire declaration is valid under the inherited private
  record rules and the submitted layout is complete and truthful.
* Pointer-type recipes with a fixed-array pointee, including pointer chains,
  and otherwise valid descriptive fnptr recipes referencing such pointers.
  These references are legal **type metadata only**, not newly admitted
  callable signatures or runtime pointer values.
* Rows whose E is a distinct Copy Transparent-over-void nominal type, or
  another non-unit zero-payload nominal type, only when its full nominal
  declaration/layout has an admitted private representation. A bare name,
  zero size or unit row cannot replace missing nominal metadata.

Re-emission follows role-specific closure and canonical pruning rules:
preserve every referenced recipe in a retained full artifact and remap all its
references; a bodyless projection prunes metadata-only recipes unless an
independently admitted contract requires them. Genuinely unreferenced array
rows may be omitted. No new type-only section, export/import identity or
runtime flag is added. A scalar executable can retain valid descriptive
recipes without executing their storage; its execution is not evidence of
nested-array support. A pruned bodyless reference cannot substitute for the
full provider artifact needed for source-free implementation re-emission.

100 assigns no runtime value or element subplace for an array-valued E, no
array-valued temporary/capture, and no non-unit zero-payload element
value/place representation.
There is no implied B-byte array register, hidden address-valued result,
memcpy-based whole-element store, synthetic field path or fallback to a
zero-sized unit value. For nested-array/ptr-to-array recipes, metadata-only
status also propagates through a runtime carrier's nominal fields,
pointer-pointee recipes and fnptr signature contracts:
`ptr<NominalContainingArray>` or a wrapper containing `ptr<array>` cannot evade
the unchanged 99 runtime admission by avoiding a direct array type code.
Reject the following, even when all bytes frame correctly and even in an
uncalled function or unreachable branch:

| Retained runtime form | Exact revision-100 disposition |
|---|---|
| 43 targeting a row whose E is a fixed-array type | Reject construction at every outer/inner N, including an empty outer initializer and zero-payload inner arrays. |
| Recursive 43 element captures | Reject a tag-43 element child or array-valued capture through a read, conditional, cast or unsafe wrapper. A child cannot be lowered as a unit, expanded into the parent's children or captured as an unstated temporary. |
| 44 selecting an array-valued E | Reject the read: no complete inner-array value or reusable inner subplace is produced. Existing Update/CompoundAssignment wrappers cannot turn that node into an admitted place. |
| 45 addressing an array-valued E | Reject the formation, including when the inner array has positive B. A ptr-to-array result is not an address of its first scalar element. |
| 46 writing an array-valued E | Reject every whole-inner-array store, including exact-type copies, recursive initializers, zero-size inner arrays and equivalent flattened byte layouts. |
| Array-containing non-array E | Reject 43–46 when E's inline field closure contains a fixed-array value; the nominal wrapper does not supply the missing runtime carrier. Pointer edges do not expand inline storage, but runtime pointer/signature eligibility must still pass the unchanged 99 rules. |
| `ptr<array>` runtime use | Reject locals, field-value carriers, parameters/results, call operands, constants, copies/projections or address/read/write nodes requiring a ptr-to-array value, including pointer chains and fnptr invocation contracts requiring it. A pointer table row is not runtime admission. Tags 35/38 cannot form it; 39/40 cannot read/write its array payload. An array whose E is such a pointer is also not executable under 43–46. |
| Runtime storage for an excluded array row or carrier containing it | Reject local/parameter/result/static/field-value materialization and whole-value use, including vacant declarations and N=0. Retaining descriptive field/type recipes above does not allocate their storage. |
| Non-unit zero-payload E | Reject 43–46, including an empty initializer at N=0. The unit exception is exact type code 0, not a size/classification test. |

An **independent direct local** of an inner flat array type remains eligible
for its own 43–46 under the ordinary rules. It must be initialized in its own
right; it cannot be materialized, copied, borrowed or addressed from an
excluded outer element. Separate flat locals and metadata row nesting do not
constitute nested execution.

Inspection may frame a rejected record to diagnose it, but cannot return it
as an admitted runtime program or re-emit that Body as valid 100. Implementing
a new backend is not permission to relax these exclusions: broader operation/
value admission needs a separately selected concrete direction. This does
not change the wider, independently unapproved source or Standard A2 proposal.

## Body encoding and reconstruction

The common expression envelope is unchanged:

| Expression offset | Field |
|---:|---|
| 0 | u8 expression tag |
| 1 | i32 result type reference |
| 5 | i32 source span start |
| 9 | i32 source span length |
| 13 | tag-specific payload |

Spans retain their valid nonnegative/representable source-coordinate rules;
they carry no semantic reconstruction data. Children are complete recursively
encoded expressions, including their own envelopes. There is **no child byte
length, alignment padding or shared-child index**.

| Tag | Payload after the 13-byte envelope | Exact total extent / result |
|---|---|---|
| **43 (`2B`) Initialization** | `i32 k; Expression elements[k]` | `17 + sum(childExtent)`; result is the exact array type |
| **44 (`2C`) Element** | `i32 variable; i32 arrayType; Expression index` | `21 + indexExtent`; result is row E |
| **45 (`2D`) ElementAddress** | `i32 variable; i32 arrayType; Expression index` | `21 + indexExtent`; result is an admitted exact pointer-to-E type |
| **46 (`2E`) ElementWrite** | `i32 variable; i32 arrayType; Expression index; Expression value` | `21 + indexExtent + valueExtent`; result is unit/private Void code 0 |

**All four tags require an executable flat row** as selected above. This
check is separate from successful type-table decoding and applies before
returning admitted IR. For 43, resolve the envelope type to its row and require
`0 <= k <= 100,000`, **`(ulong)k == N`**, and exact child type E. The i32 k
frames actual children; it never narrows the u64 N. k=0 is valid only at N=0.
Only admitted non-array E captures participate; recursive tag-43 element
captures reject as above. No spread, omitted element, repeat count or padding
value is encoded.

For 44–46, variable is the containing function's nonnegative i32 **local
slot**, within its actual variable count and outside parameters/constructor
receiver slots. Its exact type must equal arrayType. arrayType resolves only
to this component; the envelope type and pointer pointee must match the row.
The index retains its own exact signed/unsigned integer type and payload.
This composition admits the inherited private 8/16/32/64-bit index carriers;
wide source indices decline for missing codec support, not by narrowing.
Native-width aliases retain their inherited resolved private scalar code.
46's value has exact E. No new statement-result convention is introduced.

Reconstruct `BoundNativeFixedArrayLayout` for admitted nodes and eligible flat
array locals from **the same row**, including carrier/E/N/S/B/A/R. Keep variable
wire records unchanged; `GetFunction` installs the derived layout after tables
exist. A writer checks every redundant bound layout against its row and rejects
disagreement. It does not overwrite conflicting input to make serialization
appear successful. Reconstruct vacancy/initialization-local status from the
actual existing declaration/body operations, not N/B or table presence.

Use existing statement 2 Local and statement 3 Assignment with an admitted
flat initializer/complete value, statement 24 VacantLocal before lawful
initialization, and statement 4 ExpressionStatement for unit-result writes. These statements retain
their spans, i32 slots and recursive expression framing. Existing Read 2 can
carry a complete array value only in an independently admitted whole-Copy
context; it cannot read vacant storage. This is not a new array-call/result ABI.

The enclosing operations cannot bypass the nested/zero-payload exclusions
through Read 2, Conditional 22, Unsafe 31, a typed pointer or a synthesized
temporary. Array layout B is a direct-local **storage extent**, not a general
array runtime value size or copy convention selected by this proposal.

There is **no serialized unsafe-authorization Boolean** on 45. Its writer
requires the bound authorization and an enclosing explicit unsafe block
(statement 11) or expression (expression 31). Recursive resolution restores
scope-derived authorization, including in initializer/index/value children.
An unsafe declaration alone is insufficient. Existing pointer read/write,
caller-unsafe calls and obligations remain intact.

An inherited tag-1 scalar constant is still **21 bytes**; a unit child uses
Void type 0 and i64 payload 0, preserving that existing scalar encoding.
Consequently two admitted constant children make tag 43 exactly **59 bytes**; a
constant-index 44/45 is **42 bytes**; constant index/value 46 is **63 bytes**.
The 45 vector requires a positive-size eligible E; a unit/nominal-zero or
array-valued pointee is not valid merely because its expression has 42 bytes.
Empty initialization is **17 bytes**. These are independent framing vectors,
not proof from a writer/reader agreeing with each other. Tag 35 remains 99's
**50-byte prefix plus index child**, with u64 operands at 25/33/41; its
existing requested-region meaning is not replaced by whole-array geometry.

### Execution, state and provenance

43 evaluates/acquires each reached admitted E capture once, left-to-right,
before installing the destination. No array-valued capture is admitted.
Install in increasing element order and publish the whole
array only after successful capture/construction under the destination's
existing initialization/replacement authority. Failure publishes no readable
prefix and does not retire the old destination; retain earlier external
effects and settle actual private acquisitions under ordinary unwinding.
No implicit initialization of payload or padding, bitmap, rollback, hidden
heap allocation or whole-array atomicity is introduced.

For 44–46, capture the local storage identity, then evaluate the index once.
Check original numeric **`0 <= i < N` before narrowing or computing i*S**;
compute offsets/containment with checked target-nuint arithmetic. Capture/check
a write or admitted enclosing update place **before its RHS**, then use that
same storage generation/projection; later rebinding never retargets it.
Selection failure evaluates no later value/argument and touches no element.
Use the existing selected checked-index failure/terminal policy, not a new
exception or serialized success/default fallback.

Reads/addresses/replacement require the **whole array complete**. A mutable
local/path authorizes replacement; E's pointer write qualification does not
authorize writing the array slot. Element writes cannot initialize a vacant
array one cell at a time. N=0 becomes complete only through a lawful explicit
empty construction; positive-N unit arrays still require N captures and
bounds-check every read/write even though no payload bytes move.

#### Exact zero-payload operation rules

For E equal to **unit/private Void code 0**, `(s,a,R)=(0,0,1)` and the array
has `(A,S,B,R)=(1,0,0,1)` for every N. Unit is an inhabited logical value with
no payload; it is not an absent value, address, initialized default or nominal
constructor. The operation rules are:

| Operation | Exact unit behavior | Distinct Transparent-over-void nominal E |
|---|---|---|
| 43 Construction | Admit iff k=N and every child has exact Void type and a valid inherited unit encoding. N>0 requires N evaluated captures despite B=0; N=0 requires k=0. Commit only logical whole-array completeness after successful captures; move no element/padding bytes and create no anchor. | Reject in 100 at every N, including k=N=0. Descriptive identity/layout may round-trip only under the metadata rules; no Void child, nominal constructor or zero-byte capture supplies runtime admission. |
| 44 Read | Require a complete array and evaluate/check the original integer index once. For `0 <= i < N`, produce the one exact Void unit value with no payload load. N=0 has no successful index. Bounds failure and prior effects remain observable even at zero stride. | Reject in 100. Produce neither a nominal zero-sized value nor Void, and do not expose a reference/subplace. |
| 45 Address | Always reject: no unit payload can be addressed. N>0/in-bounds, A=1, a raw `ptr<void>` type or unsafe scope supplies no backing. | Always reject: there is no payload; containing padding or a synthetic anchor is not backing. Its distinct nominal identity does not make the separate zero-extent native borrowing profile available. |
| 46 Whole-element write | Require mutable, complete backing; capture/check the index before evaluating the exact Void RHS once. Success preserves whole-array completeness without moving bytes or initializing padding. Index failure suppresses the RHS. It cannot initialize a vacant array one unit at a time. | Reject in 100 even for a complete/mutable logical array or exact nominal RHS. No zero-byte memcpy or coercion to unit is selected. |

The same exclusion applies to other non-unit zero-payload nominal E. Conversely,
N=0 for an otherwise executable **positive-size** E still admits empty 43;
it has no successful 44/46 selection and no 45 address. Zero count never
changes E's eligibility. Opaque `ptr<void>` values retain their inherited
pointer-cell rules, but cannot be manufactured from unit-array payload.

45 forms a pointer to the **original E cell**, not an ABI copy or detached
temporary. Require exact pointee/access, actual alignment, eligible initialized
stable backing and ordinary unsafe authority. Reject B=0/zero-payload element
addresses, unit borrowing, one-past and padding-backed formations. Retain the
immediate array's known `N-i` suffix, stride/alignment, local generation and
access/backing dependencies for subsequent admitted NativeParameter use.
45 makes the ordinary one-element formation; it does not encode a larger
native-argument request. Existing tag-35 argument-region checks retain their
separate requested extent and index/staging rules.

Whole Copy, element read/write, conditional flow and pointer adapters retain
per-element data/code-pointer origin, loans, address-stability and backing
facts. Copying cells copies no pointees and extends no lifetime. Structural
rows authorize neither rooting/pinning nor a native call. Declaration facts
that cannot be restored from operations or represented by inherited output
dependencies remain blocked on their own 93/95/97 transport; losing provenance
is not equivalent to genuinely opaque foreign backing.

## Validation and resource boundary

Producers emit schema-valid bytes and truthful facts and verify authored/
transformed IR. Consumers complete structural, schema-support and binding
checks before affected use: full component/body extents, valid spans/counts,
assigned tags, correct type/slot namespaces, acyclic complete recipes,
normalized uniqueness, exact geometry and supported enclosing contexts.
Distinguish descriptive metadata from runtime occurrences of excluded types;
reject the latter before accepting executable IR or emitting a Body/image,
without trying a scalar-size, unit, memcpy or pointer fallback.
Keep current private verifier passes; they can also reject false body state
facts. This proposal does **not** make general semantic re-proof of producer
Body facts a mandatory consumer contract or prove external unsafe promises.

Keep inherited **4 MiB/artifact, 1,024 functions, 256 slots, 100,000 body
nodes, depth 128 and 1,024-byte strings**. The new table cap is 100,000 rows;
its physical bytes and all retained components also have to fit 4 MiB.
The maximum row count therefore need not be achievable in a valid artifact.
Before allocating builders, check count/row-width multiplication and remaining
bytes; bound child counts by the remaining node/byte budget. Charge every new
body node/child to the existing shared body budget. Bound structural
normalization/layout/remapping work to 100,000 visits and depth 128, using
memoized states rather than N-element expansion or exponential recipe walks.

u64 geometry is a quantity, not a row count/allocation request. Wire
representability, valid target geometry, private codec capacity and concrete
stack/backing resource support are distinct checks. Reject the affected use
explicitly if its target/backend cannot allocate or address the retained
extent; do not redefine N or silently emit an empty array. Truncation,
arithmetic/coordinate overflow and I/O decoding failures use the existing
module-format boundary. Failed admission/emission produces no partial program,
artifact or image and preserves an existing requested output.

## Dependencies, structural remapping and replacement

Treat array, struct, pointer and fnptr references as one **bound type closure**,
with family-specific maps; do not add an independent numeric array offset to
the current pointer-only merge. The smallest required transformation is:

1. Decode each artifact under its exact schema and retain its original using/
   provider context. For a full implementation artifact, gather every retained
   declaration/body type and symbolic call, including initializer children,
   indices, values and hidden layout carrier/E fields. Follow nested arrays,
   nominal field layouts, pointer pointees and fnptr recipes; do not prune a
   retained implementation dependency because its only occurrence has zero
   extent or no executed element. For a bodyless reference or Contract-only
   projection, start instead from its existing legally exported runtime
   contracts and their required declarations/dependencies.
2. Resolve existing selected private nominal declarations/bindings first.
   Import proxies remain the selected declarations, not newly local E types.
   Preserve the current private ambiguity rejection; outer-3 native routes
   retain their exact provider Module identity/export-key and direct/native
   expectations. **100 adds no candidate-96/general nominal-origin codec**.
   If those existing mechanisms cannot represent an actual required origin
   or substitution, refuse that input rather than comparing only spelling.
   An implementation row, public type name or metadata recipe is not a
   type-only export root. Reject an excluded array-derived contract; do not
   erase its type facts or silently drop the export to make projection succeed.
3. Recursively map normalized E and intern `(FixedInlineArray,E,N)` in the
   destination, memoizing source-context-to-destination identities. Compatible
   duplicate identities from different inputs merge; differing N, constructor,
   nominal binding, pointer qualification/depth or fnptr contract do not.
   Distinct using contexts need distinct mapped recipes where meaning differs.
4. Map table element references, every declaration/field/variable/result type,
   all four admitted expression types/carriers, pointer/fnptr references and
   tag-35 types. Preserve local slots within their function (or map them with an
   explicit function-local map), ordered children, source spans and all
   geometry/provenance. Reconstruct all redundant layouts from the mapped row.
   Remap symbolic functions and recurse under actual unsafe scopes.
   Preserve the metadata-only versus executable boundary after mapping;
   neither row merging nor provider replacement may activate an excluded
   nested, ptr-to-array or nominal-zero runtime use.
5. Compare **original expectations against the selected actual provider**
   only for retained legal contracts and facts they actually rely on. Preserve
   every required non-array expectation: nominal identity, compatible
   fixed-layout/public-value contract, Copy/rootless, access, backing,
   output/native/call and code-lifetime obligations. Do not compare unrelated
   metadata-only array descriptions as though they were type-only exported
   contracts. A relied-on layout or required-alignment change rejects; do not
   recompute a new tuple and overwrite the old expectation to make replacement
   pass. Preserve existing private slot-flexible vtable replacement versus
   stricter native package/set/graph binding.
6. Validate the complete selected input before projection pruning. Pruning
   cannot repair malformed rows/references or an excluded runtime occurrence.
   Then re-emit through the exact-100 canonical writer, rebuilding maps, textual
   original expectations, container lengths/checksums and any affected native
   outputs. No octet substitution or header editing. Invalidate check/layout/
   code caches for changed bytes, schema, bindings, target or runtime context.

For full persisted modules, sets, graph libraries/edges and source-free
execution artifacts, preserve the complete retained implementation closure.
For bodyless references, embedded Contracts and snapshots, retain only their
existing legal contract roots and required dependencies; prune unrelated
metadata-only recipes. Do not infer type-only exports from names, rows or
metadata presence. Re-emission of implementation data requires the full
provider/consumer artifacts; a pruned bodyless reference cannot manufacture
omitted bodies or descriptions by rereading source or recognizing an
application. The [standard A2](standard-trrm-rootless-copy-type-evidence.md)
TypeSpec 11, section 0012, Copy bit and Feature closure are a **distinct,
unapproved schema**, not byte layouts or evidence aliases for this private
component. Cross-format conversion/adoption is separately scoped.

## Required tests and implementation dependencies

These are acceptance gates to implement through public compiler APIs and
independent fixtures, **not passing evidence**. Ordinary tests remain headless;
native execution uses the existing Windows x64 gates, both admitted runtime
policies where meaningful, deadlines, owned-process termination/wait and
bounded `TestFileCleanup` retries.

| Gate | Required cases / result |
|---|---|
| Producer and exact selection | Producer, scalar-only producer, consumer, set and graph-library writers declare 100 and include the empty/nonempty component. Public readers reject honest 99 and candidates 92–98, unknown versions, Standard/A2 bytes and inconsistent embedded selections before decoding records. Retain inherited outputs, FieldMove and u64 tag-35 fields in the same fixture. |
| Independent framing | Inspect all 44 row bytes and mandatory count; verify 17/59/42/63-byte operation vectors and adjacent statements/functions without drift. Decode independent fixtures, not only self-round-trips. Preserve an ordinary 21-byte scalar/unit child and the unchanged 71-byte constant-index tag 35. |
| Identity and closure | Equal normalized aliases/count expressions intern once; different E, N, nominal wrapper/origin, pointer depth/access and nested dimensions remain distinct. Full artifacts retain valid metadata-only nested rows, descriptive fields, ptr-to-array/pointer-chain/fnptr recipes and forward/reordered references through decoding/remapping/re-emission. Bodyless projections prune unrooted recipes unless an independently admitted non-array contract requires them. Canonical pruning may remove genuinely unreferenced rows. No type-only export, runtime storage or callable admission follows. A same-named user InlineArray remains nominal. |
| Initialization and failure | For eligible flat rows, N=0 with [] is complete only after explicit initialization; N>0 unit needs N children. Underfill/overfill, wrong child type, absent target row and nonzero unit payload reject. Ordered side-effecting/throwing non-array captures and failed replacement preserve prior effects and publish no prefix. Vacant read/write and forged live-state facts fail producer verification and the existing private verifier where applicable. |
| Nested operation exclusion | For E itself an array, reject each of 43/44/45/46 independently at N=0 and positive N, including positive-size and zero-size inner arrays. Reject recursive 43 captures through wrappers, array-valued reads/copies/conditionals/RHSs, nested update/place synthesis and array-containing nominal E. A separately initialized flat local of the inner type remains positive; it cannot be derived from an excluded outer element. Rejection is before publication/execution and is expected coverage, not an open positive gate. |
| Pointer-to-array exclusion | Valid descriptive ptr-to-array, pointer-chain and fnptr recipes round-trip. Reject runtime variables, field-value materialization, signature/call carriers, constants/copies/projections, 45 array-valued pointees and tag-35/38/39/40 formation/access requiring them, even in unreachable/uncalled code. An array of ptr-to-array cells also rejects 43–46. No scalar-pointer or first-element reinterpretation fallback. |
| Exact zero-payload distinction | Unit has positive 43/44/46 cases with N captures, index side effects/failure, RHS suppression and no payload access; 45 always rejects. N=0 positive-size flat E accepts only empty construction, with no successful selection/address. Distinct Transparent-over-void and other non-unit zero-payload E retain nominal metadata identity/geometry when representable, but all 43–46 reject, including empty N=0 construction. No unit alias, anchor or zero-byte value/store fallback. |
| Indexed storage | First/middle/last, negative, N and too-wide indices; inherited signed/unsigned 8/16/32/64-bit indices retain sign/bits and one-time evaluation. Failure suppresses RHS/later calls. Writes and native pointer writes change the original selected cell, not its neighbors, with fresh readback. Unit writes/read checks still execute. Int128/uint128 decline explicitly for missing 94 codec support. |
| Unsigned geometry and limits | Independent rows exercise N=0/1, int.MaxValue+1, uint.MaxValue+1, bit 63 and ulong.MaxValue without huge allocation, especially zero stride. Test product/round-up/offset overflow, incorrect S/B/A/R, zero/non-power alignment, N=0 with invalid E/cycles, packed/overaligned occurrences and backend resource rejection. Reject negative/excess counts, c=100,001, insufficient bytes at c=100,000, slot 256, node/depth/string/artifact boundaries; do not claim a physically impossible maximum-row artifact passed. |
| Malformed records | Truncate each table field, count and nested child; wrong-family/out-of-range/retired-20 type codes, duplicate/conflicting rows, recipe/inline cycles, invalid slots/result/pointee types, unknown tags, invalid spans, missing mandatory component and trailing bytes. Wrong-schema reference-family bytes never trigger a retry. |
| Authority/backing | Fixed versus mutable locals, unsafe block/expression versus declaration-only authorization, uninitialized/zero-payload/underaligned backing, one-past/padding and incorrect pointer access reject affected formation. Known suffix Q below/equal/above retained backing keeps inherited NativeParameter checks; no lifetime, ownership or write upgrade through copy/adaptation. |
| Remap/replacement | Non-byte-identical struct/array/pointer/fnptr table permutations round-trip and preserve retained layouts/children/contracts. Merge equivalent recipes across ordinary/set/graph inputs; preserve differing bindings. Compatible provider replacement may change/remove unrelated metadata-only recipes; changes to retained legal contracts or actually relied-on non-array facts reject. Exercise transitive provider closure and zero-extent retained imports. |
| Source-free/JIT/AOT/CLI | Compile scalar-export providers/consumers containing eligible flat local arrays, persist them, discard source trees, read/link/rewrite/re-emit/reload and execute actual bodies. Include dynamic nonzero indices, empty/unit arrays, mutation/readback and checked failure/order. Exercise ordinary JIT, PE/AOT and native package/set/graph routes plus their existing CLI artifact paths; inspect emission separately from supervised actual process results and preserve binding expectations. Scalar execution beside metadata-only recipes supplies no nested/nominal-zero execution evidence. |
| Deliberate external gates | Preserve explicit rejection for unsupported wide/general nominal/generic/reference/frozen/static/call/place routes. Nested array values, ptr-to-array runtime use and non-unit zero-payload operations are excluded by the revision-100 direction above; additional backend implementation alone does not turn their negative tests into positive gates. |

Active starting patterns are
[`NativeOutputModulePersistenceTests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/NativeOutputModulePersistenceTests.cs)
for exact headers, forged records and replacement type reordering,
[`NativePointerModuleTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Modules/NativePointerModuleTests.cs)
for pointer/set/graph binding and typed array-address persistence, and
[`NativeFixedArrayStorageTests`](../../tests/Terrum.CodeAnalysis.Tests/Native/Storage/NativeFixedArrayStorageTests.cs)
for source/local/unit behavior. The current
[test project](../../tests/Terrum.CodeAnalysis.Tests/Terrum.CodeAnalysis.Tests.csproj)
does not exclude these files. The support inventory records current focused
implementation evidence; it does not discharge the remaining acceptance gates
above or change this proposal's unapproved status.

| Remaining implementation gap | Dependency / completion condition |
|---|---|
| Exact operation vectors and malformed bodies | Independently assert the 17/59/42/63-byte 43–46 vectors and exercise forged malformed children, slots, spans and unsupported runtime rows. Keep writer/reader agreement separate from producer truth. |
| Role-specific metadata closure | Exercise nested array, nominal-field, pointer-chain and fnptr recipes through full module/set/graph persistence and remapping. A bodyless scalar-export projection prunes unrelated recipes; embedded Contracts retain only their legal expectations and required dependencies. No type-only export is required or inferred. |
| Provider-origin replacement | Permit valid unrelated implementation-only array descriptions, dimensions and geometry to change or disappear. Preserve original identities/expectations for legal exports and all actually relied-on non-array facts; include an imported non-array E used by a consumer flat array, for which incompatible provider layout/Copy/origin changes still reject. |
| Remaining CLI and native package paths | Cover CLI build/library and replacement paths in addition to the passing run/link, native set and graph executions; record final input identity and supervised process results. |
| Broader source and Standard A2 | Complete the separate source conformance and Standard A2 producer/source-free type/body/provider/CLI routes. Private revision 100 is not evidence for Standard A2. |
| Qualification | New independent producer/consumer/malformed fixtures and source-free execution evidence cover the final relevant compiler patch/configuration/input identities. Updating a header assertion or removing a rejection test does not discharge these gates. |
| Outside the selected 100 scope | The source proposal's broader nested/field/nonlocal/whole-call and non-unit zero-size routes need a separately selected complete runtime/value/place direction, not enabling a latent 100 Body form. ConstEval extents, general provenance, 94–98 codecs/composition and Standard A2 adoption remain distinct. Do not silently compose them into 100. |

**Remaining formal decisions:** approve candidate 100 and its exact 99-based
composition, the flat/artifact-local metadata and role-specific projection
boundary, table/operation
allocation, canonical/reference rules and exact-only regeneration policy.
The recommendation supplies all of these
provisionally; no wire width, table position, tag meaning or historical-reader
choice is delegated to an implementer. A future richer private composition,
nonlocal place codec or Standard/private conversion needs a separate concrete
decision. This document does not choose those unrelated policies or declare
the wider fixed-array milestone complete.
