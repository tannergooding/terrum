# Standard TRRM raw Union initialization

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** This recommendation changes no
normative specification, approval status or implementation. The examples
and acceptance cases below are proposed behavior, not execution evidence.

**Proposal revision: `raw-union-initialization/3` (2026-11-02).**
The stronger-than-G cross-Module return contract is fully selected below;
its implementation and qualification remain required work, not a deferred
contract decision.

## Decision, scope and evidence

Recommend **one whole-union availability state, established by initializing
one complete alternative, plus derived byte-coverage and typed-payload facts;
unsafe alternative access; representation-preserving whole-value Copy without
an active-member tracker**. Whole-U availability does not imply that every
alternative's payload is initialized. Retain the existing layout/field
carriers. Publishing stronger provider-return coverage does require the
new MethodDef contract and exact **A2R1** successor specified below; do not
silently add it to A2. No implicit zeroing, discriminant or alternative
cleanup is introduced.

The scope is an ordinary inline Copy struct with raw `LayoutKind.Union`,
the accepted Windows x64 Blittable representation, and every alternative
independently satisfying
[A2 RootlessCopy](standard-trrm-rootless-copy-type-evidence.md#unapproved-complete-element-checking-rule).
This is not a nominal `struct union`/`class union`, a unique union element,
an owning overlap representation or a foreign by-value mapping. The
[fixed-array candidate](standard-trrm-fixed-array-identity-shape.md) retains
its independent array identity, geometry, initialization and address gates.

The [specification index](../specs/README.md) leads to a genuine **value-state
gap**, not permission to redesign already selected layout or safety rules:

| Owner | Already decided; remaining gap |
|---|---|
| [Data layout](../specs/data-layout-and-abi.md#fixed-directly-interoperable-representations), [Windows layout §§4–6](../specs/targets/windows-x64-data-layout.md#4-registered-source-layout-schemas) | Union requires Blittable, overlays every alternative at zero, permits only rootless Copy alternatives without owning-child retirement, and has no active-member tracker. Alternative access is unsafe. Packing, alignment, minimum empty extent and uninitialized padding are already specified. |
| [Structural flow](../specs/language/initialization-and-flow.md#states-reads-and-initialization), [ownership](../specs/language/ownership-and-cleanup.md#values-storage-and-structural-state) | Live means complete; raw overlapping fields do not acquire ordinary partial-field tracking. These chapters do not say which raw alternative initialization completes U, or how whole-U Copy preserves a representation without selecting a field. Requiring every overlapping alternative simultaneously valid does not follow. |
| [Source grammar §§3.1, 5.10](../specs/grammar-spec.md#510-properties-and-accessor-local-effects), [overlap safety](../specs/memory-model.md#overlap-representation-access-and-unions) | Existing declarations, constructors, assignment and explicit unsafe scopes suffice. Tagged-case construction is a different contract; C/C++ active-member or punning rules are not imported. General construction phase order does not decide the missing raw-union completion rule. |
| [Binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [BoundStruct](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [semantic verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | The bound representation derives `IsUnion`/`LayoutKind` from existing flags. Neither geometry support nor ordinary all-field-mask logic settles raw-Union source completion; its whole availability and representation coverage need the explicit rule below. |
| [A2 layout validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmFixedArrayLayoutValidator.cs), [layout tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmFixedArrayLayoutTableTests.cs) | `CalculateTypeDefLayout` already handles kind 2 with the largest alternative extent. `DerivesUnionArrayStrideFromLargestRootlessAlternative` and the missing-Copy negative case exist. Geometry is not the missing decision; these are source observations, not newly run checks or value-execution evidence. |
| [Private ModuleCompiler](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs) | The writer/reader already retain `IsTransparent`, `IsSequential` and `BlittableTargetRequirement`, sufficient to derive Union. `RequiresCallerUnsafeField` must account for the containing Union as well as a field's flag. A missing derivation is an implementation bug, not missing wire evidence; the current helper includes both. |
| [Contract clauses](../specs/representation/contract-clauses.md), [CopyOrigin candidate](reference-copy-origin-trrm.md), [selected reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs) | Effects supplies no coverage; OutputState describes whole initialization destinations, not returned bytes; Dependency describes backing, not D. CopyOrigin can transport facts of a copied entry value, but cannot promise fresh independently initialized return bytes. It is also a separate selected schema: A2 does not admit clause `000D`. A2 has no published fresh-return coverage carrier. |

## Alternatives

All choices here are unapproved.

| Direction | Assessment |
|---|---|
| **One complete alternative; opaque whole value; derived coverage — recommended** | Permits a short alternative to complete U while preserving unwritten bytes as uninitialized. Requires the coverage/typed-payload transfer and reconstruction rules below; whole-value availability alone is insufficient. No current alternative is selected for reads or cleanup. |
| Require every alternative initialized and valid | Confuses overlapping views with independent fields. Alternatives can have incompatible validity domains, and a later store can invalidate another interpretation. It does not define a useful raw union. |
| Require every possible payload byte initialized before U becomes live | Simpler initializedness: live U would guarantee full payload coverage. A Low-only initialization of Word would not complete U, and padded alternatives can need an additional covering construction operation. Coherent but deliberately narrows construction; not selected here. |
| Require/zero-fill every object-representation byte | Adds a policy covering padding without establishing nominal/pointer validity. Implicit zeroing is observable and cannot replace valid alternative construction. Neither is needed for typed Copy. |
| Track an active alternative, tag or per-alternative live mask | Can support checked sums and owning cleanup, but changes the accepted no-tracker direction and needs validity, transition and persistence rules. Use the independent tagged-union contract rather than silently giving raw Union those semantics. |

## Proposed source and runtime rules

### Construction and availability

1. Allocation, a Home declaration, layout admission and unchanged bytes do
   not create a live U. For a nonempty raw union, initialization of **one
   whole declared alternative from a complete valid value** establishes U's
   storage-completion obligation. Establish any independent nominal validity
   and final construction guarantees before publication. This deliberately
   replaces the all-required-fields completion rule **only for raw Union**.
2. The destination must be a structurally identified, wholly vacant U with
   actual initialization authority. Selecting its alternative for
   initialization does not read U. The complete alternative is staged before
   commitment; no partially initialized alternative is exposed. Writing
   subfields of a vacant alternative does not accumulate ordinary field bits
   or complete U. Construct that child separately, then store it whole.
3. Construction retains declaration-initializer, constructor, caller-entry
   and final-validation order. Subsequent permitted alternative stores act
   on the same overlapping representation, in source order; initialization
   does not grant later mutation. Existing unpublished-construction or
   writable-init authority can permit repeated writes. A vacancy-only loan
   cannot initialize U again after its first successful completion.
4. Every implicit or explicit alternative store needs explicit unsafe
   authorization covering that store. An unsafe constructor declaration
   does not authorize its body; an unsafe RHS alone does not authorize an
   implicit destination write outside that expression. Use an ordinary
   constructor with an explicit unsafe store; no new field-initializer
   authorization syntax or deferred raw-field completion contract is selected.
5. An alternative-free U has no alternative payload obligation: successful
   explicit ordinary construction can complete vacuously, subject to its
   nominal guarantees. It does not initialize its minimum padding byte.
   A nonempty U containing only zero-data alternatives still requires one
   explicit complete alternative initialization. Neither case supplies a
   default value merely because there are no payload bytes. The minimum
   extent remains U object storage for layout/stride, not an initialized
   byte or backing for a zero-extent alternative.

U thereafter has ordinary whole-place live/vacant/maybe-live flow.
`u is init` tests that availability without reading an alternative.
`u.F is init` is not an admitted alternative-state test, even in unsafe code:
there is no independently tracked F state or active-case discriminator.
Different successful alternative stores on incoming paths can join as live U,
without proving any particular projection valid.

### Minimum representation facts and transfer

Derive the following from the complete closed type and selected layout, using
half-open byte ranges relative to the value:

* `P(T)` is the union of declared possible payload footprints, recursively
  through admitted fields/array elements. It excludes bytes that are only
  layout padding; a separate raw-byte protocol does not enlarge P. A byte
  used by Wide belongs to `P(Word)` even when it lies beyond Low.
* `G(T)` is the initialized-byte lower bound promised by complete T alone.
  A scalar/pointer cell contributes its full payload; void contributes none.
  Ordinary aggregates take the union of their children's translated `G`
  footprints; arrays repeat the element footprint at the checked stride.
  Raw Union takes the **intersection** of its alternatives' `G` footprints,
  not their union; an alternative-free Union contributes none. Thus
  `P(Word)=[0,8)` but `G(Word)=[0,1)`. Independently unsupported constructors
  supply no guessed rule.
* `D(v)` records definitely initialized payload ranges of the actual value
  snapshot/current stored value, at least `G(T)` when complete T is established.
  Retain typed-view validity, known value restrictions, complete nested-value
  facts and pointer/code/backing provenance separately. Initialization of
  eight bytes is not proof that they denote a valid pointer, bool or wrapper.

D is a lower bound, not a claim that every omitted byte is always
uninitialized. A merge can lose proof of a byte that is initialized on some
paths. Whole Copy must preserve its actual contents/initializedness too;
the absence of a byte from D never permits clearing it.

Key current-place facts by actual storage generation, captured subplace and
content version; copied values retain their own snapshots. Typed-view facts
identify complete types and relative ranges/dependencies, not a last-written
FieldDef. Several interpretations can be valid simultaneously. These are
derived semantic facts, not payload bytes, an active-member discriminator,
an alternative live mask or source-visible state tests.

The minimum transfer rules are:

| Operation | Required fact transition |
|---|---|
| Vacant U storage | `D` is empty. This does not create live U. |
| Committed typed store of F at offset o | Let `W=[o,o+sizeof(F))`, including F's padding. Remove W from the destination's current D at commitment, after RHS effects; insert the staged RHS snapshot's D translated by o, then intersect with `P(U)`. Bytes outside W retain their facts. Padding within W acquires no initialization guarantee even if it is another alternative's payload. Invalidate value/validity/provenance facts whose dependent ranges overlap W, then install/rederive justified RHS facts. Ordinary captured-place generation/authority checks still apply. |
| First complete alternative store | Apply that store rule and establish U's storage-completion obligation; live U still requires its remaining validity gates. Do **not** replace D by `P(U)` or mark all alternatives initialized. The earlier prohibition on partial child construction remains. |
| Whole-U Copy/assignment | Transfer the source representation's actual initializedness and its D/typed facts, without evaluating an alternative's payload. Rebase relative locations into the destination, not pointee/code origins. A later source write changes current-place facts, not already captured copies. |
| Join/loop merge | Intersect guaranteed D and validity facts over reachable live incoming values; preserve all feasible backing/provenance obligations with their guards. Keep ordinary live/vacant guards separately. A live/live join stays live even when only a short common prefix is defined. Never union byte coverage from mutually exclusive paths. |
| State refinement or unknown mutation | `u is init` selects live edges but does not widen D. An insufficiently described write kills coverage and typed facts in its possible affected range. Only an admitted outcome contract can re-establish complete T and its G; the old live flag cannot do so. Without supported completeness/validity or range/alias facts, reject the mutation/affected use, not invent opacity or restored validity. |

A typed alternative read/Copy of scalar Wide requires `[0,8)` covered by D,
as well as its ordinary validity/access obligations. Live U with only
`D=[0,1)` therefore permits an opaque **whole-U Copy**, but rejects evaluating
`u.Wide`, including a Copy into a uint64 destination. Unsafe authorization is
not an assume-initialized operation.

Apply typed payload rules recursively: an ordinary aggregate/array read
requires its observed children complete; scalar leaves need their byte
coverage. A nested raw-union child may use opaque Copy only with its own
established complete-child fact, carrying its D rather than reading all
possible alternatives. Byte coverage alone cannot manufacture that fact.
Overlapping writes can invalidate an established typed view even while all
its bytes remain initialized; re-establish validity/provenance before using it.
An initialized-F reference or native borrow needs those same facts for F.
Whole-U addressability supplies no stronger payload coverage; an independently
admitted address-only/output operation cannot turn an unwritten F into a
readable one.

Across a call, consume published per-function promises, never the callee's
private initialization plan. In A2R1, `ReturnCoverage` supplies guaranteed
returned ranges in addition to `G(T)`; an admitted exact CopyOrigin can also
transfer staged entry-value facts. With neither promise only G is available.
This is a deliberately weaker API contract, not the implementation strategy
for MakeWide: the source producer must publish its verified wider coverage,
and source-free/provider execution must consume it. Unsafe alone cannot
upgrade coverage. Raw pointer boundaries retain their independent rules.

Represent array repetitions and captured element paths compactly; do not
expand a uint64 count into byte/element bitmaps. Required facts must survive
supported transforms and loop fixed points; unsupported representation or
analysis exhaustion is not permission to promote D to full coverage.
Whole-counted-array availability remains a separate existing obligation.

### Alternative access and overlapping writes

Preserve the accepted rule: every typed read, write, extraction, nested
projection or reference/pointer formation crossing a raw Union alternative
requires an explicit unsafe scope, including access inside its constructor.
Derive this from the owning layout kind; authors need not mark each field
`unsafe`. It does not make an ordinary wrapper callable caller-unsafe when
that wrapper establishes its safe contract internally.

A read or interior update requires live U, the coverage/complete-child facts
above, and actually valid storage for the selected interpretation, including
alignment, pointer provenance, backing and access obligations. Prior
initialization of F does not grant safe F access. Reinterpretation is allowed
only when these unsafe obligations hold, not because F is “active.”
Source/bound checking preserves known restrictions and rejects known
violations; it need not prove arbitrary unsafe validity algorithms.

A whole-alternative store uses the transfer rule at offset zero. Invalidate
affected facts for **all overlapping alternatives**, not just the named
FieldDef; do not clear bytes outside the authorized extent.
Every update must also establish affected enclosing validity guarantees
before exposing the updated live value, under ordinary staging/validation
rules; no automatic validation hook is inferred.

After publication require the actual writable containing path and selected
field's mutation permission. A fixed sibling alternative does not promise
preservation of its overlapping bytes under another permitted alternative
write. Checked loans and retained dependencies nevertheless cover their real
overlapping storage: another field name cannot bypass readonly/exclusive
access, retained backing or invalidation restrictions. No structural
field-move/end operation vacates F independently or leaves U as a partial
owning aggregate; use an unsafe Copy read or an admitted whole-U transfer.

### Whole-value Copy, ownership and failure

Copying live U is an ordinary typed representation transfer and needs no
unsafe scope solely because U has Union layout. It can copy a representation
whose D is smaller than `P(U)` precisely because it does **not** evaluate any
alternative's payload. Preserve defined payload bits, actual initializedness,
D and typed dependencies under the transfer rules above. Do not copy fields
sequentially, normalize an alternative, substitute zeros for preserved data
or expose indeterminate data as a typed scalar. Bytes that are only layout
padding retain no Copy preservation/initialization guarantee. Incidental
machine writes there establish no source guarantee. This is not source
authorization for an arbitrary byte blit or raw foreign transfer.

Whole assignment stages a complete U, then performs ordinary initialization
or build-before-replacement under the destination's authority and dependency
checks. Self-assignment and overlapping admitted source/destination paths
must preserve that snapshot. Whole moves retain their ordinary source-vacancy
rule. Copies preserve pointer-cell dependencies, not pointee ownership,
address stability or a rebased pointer into the copy's storage.

Every alternative must pass RootlessCopy, even when never written, private,
zero-sized or in a zero-count array. No alternative has automatic owning
retirement; no active alternative's destructor is selected. A2 still rejects
unique/no-Copy U and Cleanup-bearing U, including trivial-byte forms. The
accepted possibility of a complete unique outer wrapper cleaning non-owning
bits is preserved, not admitted as an A2 Copy element by this proposal.

Destination/base/index selection and checks precede RHS evaluation in the
ordinary order. A failing RHS has not committed the store; earlier permitted
effects remain. Failed construction/final validation publishes no U and
settles actual staged/acquired obligations under existing rules, never a
fabricated alternative or incomplete-parent Cleanup. No union-specific
exception, runtime active-case check, rollback or unsafe-bit-pattern
normalization is introduced. A false unsafe/provider validity promise has
no execution safety guarantee, not a promised catchable “bad union” failure.

### Illustration

This uses accepted declaration/unsafe syntax with the **proposed completion
and Copy semantics**. It assumes the registered Core layout identities and
illustrates the contract, not a qualified compiler execution result.

```terrum
[Blittable]
[Layout(LayoutKind.Union)]
public mut struct Word
{
    public mut uint64 Wide;
    public mut uint8 Low;
}

public Word MakeWide(uint64 value)
{
    init Word result;
    unsafe { result.Wide = value; }
    return result;
}

public uint64 Observe()
{
    mut Word original = unsafe(MakeWide(256));
    unsafe { original.Low = 7; }
    Word copy = original;
    unsafe { return copy.Wide; }
}
```

MakeWide publishes `ReturnCoverage` for `[0,8)`. Observe therefore has the
same proof when MakeWide is in a separately selected provider and its
consumer-facing declaration has no Body. No callee inspection is needed.
The scoped call authorization preserves any actual native-callable
obligation; it supplies no coverage by itself.
The vacant local and first alternative store use the proposed initialization
authority above; no new source attribute is selected.

| Reached operation/path | Whole state and derived coverage; consequence |
|---|---|
| Construct by Low only | Live, `D=[0,1)`. Unsafe Low read and whole-Word Copy are permitted; Wide read/uint64 Copy rejects. |
| Construct by Wide | Live, `D=[0,8)`. An unsafe Wide read is initialized. |
| Wide=256, then Low=7 | D remains `[0,8)`; Low's one-byte store invalidates the old Wide value fact, not its tail's initializedness. uint64 admits the resulting bits, so Observe returns 263 on Windows x64. |
| Join Low-only and Wide-initialized paths | Live on both; merged `D=[0,1)`. Whole-Word Copy remains permitted, but Wide read rejects without an admitted path refinement. |
| Whole-Word Copy after any row | The copy retains that D and its representation facts; neither Copy nor `copy is init` widens it. |

## Published return coverage

### Carrier decision

**A2 cannot express the fresh-return guarantee.** Its complete result type
supplies G, not the actual D established by MakeWide. CopyOrigin guarantees
copying entry values; its FreshInlineConstruction form also needs entry
origins and publishes only provenance-bearing facts. It cannot encode a
fresh constant-initialized range with no origin. OutputState's formal and
predicate fields cannot be repurposed as a returned-byte mask, and Dependency
does not promise copying or initialized bytes.

Recommend one **MethodDef-owned ReturnCoverage Contract, kind `0x000F`**,
plus composition with CopyOrigin for genuine entry-copy forwarding:

| Alternative | Assessment |
|---|---|
| **ReturnCoverage plus existing CopyOrigin — selected** | A small target-specific guaranteed range set covers fresh results. Exact copy guarantees transport input-dependent D without turning a backing relation into value copying. Both survive declaration-only provider references. |
| Existing TypeDef/Copy/Layout contracts | Describe the complete type and all permitted representations, supplying G but not which bytes every result of one function initializes. MakeWide and MakeLow return the same TypeDef with different D. Putting their promises on that TypeDef would falsely strengthen every U value or create different nominal types; a reserved layout/Copy bit is not a per-function carrier. |
| Existing OutputState | Its owner is a MethodDef, but its subject is an initialization-destination formal and its state is vacant/complete on a published outcome. Complete U still means G, not full alternative coverage. A normal returned value is not a fabricated formal, and state 1 cannot be reinterpreted as a byte mask. Extending those fields would itself need a new exact schema. |
| Existing Dependency | Publishes backing/retention bounds between declared subjects. Neither dependency on an initialized source nor a backing-only forwarding promise establishes returned bytes, exact value copying or preserved D. No existing strengthening relation turns a dependency into the required initialization guarantee. |
| Extend CopyOrigin with an empty/independent origin | Changes its exact-copy meaning and all origin/source validation. A new coverage clause separates initializedness from value/provenance identity and works when no copying source exists. |
| Encode a general postcondition | A2 has neither the complete general-predicate composition nor an initialized-byte predicate codec. Selecting them would still require choosing that predicate's semantics and replacement relation; a self-comparison or field-state test is not an existing coverage carrier. |
| Serialize observed D in Body/debug data, inspect the callee, or promise only G | Does not give an independently replaceable caller a published per-function guarantee. None completes the required source-free/provider route. |

### Exact successor and allocation

Recommend exactly **A2R1**:

**`trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-1`**

Candidate selector:
**`TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyReturnCoverageDraft`**.
Its complete meaning is
[A2EU+SF1](standard-trrm-a2eu-scalar-literal-comparisons.md#exact-combined-selection-and-manifest),
the full [CopyOrigin delta](reference-copy-origin-trrm.md), and this document's
raw-Union state/coverage semantics, CopyOrigin applicability amendment and
ReturnCoverage below. Keeping the already selected empty/unit/scalar paths
in this one successor avoids dropping those adoption requirements.

Original A2/A2E/A2EU/A2EU+SF1, historical CopyOrigin, Standard, F/F+S, A3,
general predicates, enums, RootPin and exception-region selections remain
unchanged. This is not a mutable meaning added to an A2 selector.

| Namespace | Exact unapproved delta/preservation |
|---|---|
| Contract kind `u16` | `000D` remains CopyOrigin, `000E` NativeParameter; assign **`000F=ReturnCoverage`** only in A2R1. Baseline `000A..000C`, private `0010..0012` and the separate static `0013/0014` candidates are not reassigned or composed. `0006..0009` remain unassigned here. |
| Feature | **`22/1=RawUnionValueCoverage`**, governing edition 1, immediate prerequisite `2/1`, deriving `1/1`. It covers the raw-union initializedness model, published return coverage, call instantiation, binding and source-free preservation together. Feature 21 remains the unrelated exception-region candidate. |
| Existing closure | Preserve array `20 -> 2 -> 1`, CopyOrigin `15 -> {}` with its conditional 13 requirements, F's 8/10/16 rules, and A2EU+SF1's complete scalar `11 -> {}` registration. Do not import G's different catalog or infer an enum/exception composition. |
| Physical envelope | Keep `(1,0)`, codec `01`, the ordinary header/Module rows, TypeDef Copy bit `0x20`, Layout kind 2, TypeSpec `11`, FixedArrayLayout `0012` and all Body record/map widths. The bounded empty-Union nullary-store admission below is explicit in R1 only. Add no section, DefId, opcode, Body record kind, runtime policy, tag, unsafe field bit or active-state payload. |

This checks distinct namespaces, not numeric coincidence. The registry ends
its assigned clause band at `000C`; the existing candidates occupy `000D/E`.
`000F` is unassigned, not an extension escape. Choosing a lower draft ID
would be permissible but saves no width and needlessly moves unrelated
clauses. Feature 22 is a separate capability identity, not clause `0016`
or a reuse of CopyOrigin Feature 15. Contract `000F` is not the exception
candidate's Body record `0F`.

Require 22 in every Module retaining ReturnCoverage, an expected coverage
promise, or a by-value raw-union-bearing signature/value operation, including
uncalled/declaration-only uses. It is also required when an initialized-backing
contract relies on this raw-Union availability model. Inspect the selected
inline subject, not arbitrary pointer-reachable storage. A type-only layout
provider without those uses keeps its independent 2 requirement; arrays still
require 20. Require 15 for retained CopyOrigin facts/expectations, including
the initializedness-only use below.

Select the whole schema before decoding, producing, binding or lowering.
No Feature/row/version sniffing, decoder retry, private fallback or per-Module
decoder is allowed. Independently selected A2 type-only providers can supply
unchanged nominal Copy/layout facts, not an unencoded ReturnCoverage promise.
Packing these promises requires A2R1. Formats using equal `(1,0)` bytes do
not acquire its meanings under
[exact draft agreement](../specs/representation/format-versioning.md).

### Exact ReturnCoverage row

All integers are little-endian. Reuse required Contract section `001B`,
its existing offset index/policy `0000`, and MethodDef `contractFirst/count`:

```text
u32 ownerRow                       // typed MethodDef row, not DefId
u16 clauseKind = 0x000F
u16 flags = 0
u32 targetProfileRef
u32 regionCount
repeat regionCount:
    u64 byteOffset
    u64 byteLength
    u32 repeatRank
    repeat repeatRank:            // innermost to outermost
        u64 repeatCount
        u64 repeatStride
```

The fixed prefix is **16 bytes**, each region prefix **20 bytes**, each
repetition dimension **16 bytes**. Exact row extent is
`16 + sum(20 + 16*repeatRank)`, with no padding/trailing data.
MakeWide's row with one `[0,8)` region, rank zero, is **36 bytes**.

Owner/subject and structural rules:

* `ownerRow` is a nonzero existing MethodDef row in `1..FF_FFFF`, high byte
  zero, owned exclusively by its containing Contract range. TypeDef,
  MethodSig, Body, FieldDef and PropertyDef cannot own it; a getter uses its
  actual MethodDef. Shared signatures do not share coverage clauses.
* The subject is the owner's **ordinary normal by-value result T**, derived
  from its complete normalized MethodSig, not another serialized TypeSpec.
  T must be closed, admitted RootlessCopy, have a covered fixed Blittable
  Windows x64 layout and contain a raw Union in its inline graph. No
  receiver/init-output/deferred-completion interpretation or constructor-role
  inference from a name/unit result is added.
* Bodyless expected MethodDefs and function-pointer contract anchors may
  retain this normal-result promise. An anchor must obtain it from the
  declared target contract, not its signature shape; include normalized
  coverage in exact pointer-contract identity/projection and retain the
  original target obligations at invocation. An actual foreign promise
  remains provider truth, not a new foreign aggregate ABI.
* `targetProfileRef` is a nonzero typed TargetProfile row naming exactly
  Windows x64 `(1,1)`; no default/zero profile. Interpret T in the owning
  Module, including its original import expectations and actual bindings.
  The Module has target requirement 1 and the required profile support.
* Occurrence key is `(owner MethodDef, ReturnCoverage, resolved profile)`.
  Duplicate clauses reject even if identical or their profile rows alias.
  Absence means no guarantee beyond G. A zero-region row has that same
  meaning and is accepted noncanonically; it still requires Feature 22.

A rank-zero region is `[byteOffset, byteOffset+byteLength)`.
`byteLength` is positive. Each dimension repeats the inner shape
`repeatCount>=2` times at positive `repeatStride`; its stride is at least
the preceding shape's span. Starting with `span=byteLength`, derive each
outer span as `checked((repeatCount-1)*repeatStride + span)`.
Every offset/sum/product must fit target nuint, and the final end must not
exceed `sizeof(T)`. Counts/offsets/lengths/strides are quantities, not row
references, bytecode, element indexes or private signed counts.
Zero repetitions contribute no region; one repetition omits that dimension.
Neither is serialized as a repeatCount of zero or one.

Let R be the union of all region instances. Require **`R subset P(T)`**:
no byte that is only layout padding can be promised by this clause.
Repeated regions must be checked against the compact target-derived payload
geometry without expanding a uint64 array count. Multiple regions may overlap
or repeat the same descriptor; their union is idempotent, not conflicting
initialization or an active-state choice. Nested repetition represents
array-in-struct-in-array layouts without enumerating elements.

Preflight `16 + 20*regionCount`, every `16*repeatRank`, the complete sums and
the section's `4*(recordCount+1)` offset index against actual extents before
allocation/traversal. Inventory counts use full u32 domains. Enforce reference
namespaces, owner partitions, closed roles/flags, type/layout cycles and
supported profile/feature closure independently of initialization truth.
Capacity exhaustion remains distinct from malformed input; it cannot turn
an unchecked region into a guarantee.

Canonical writers omit an absent/effectively-G-only guarantee, coalesce
adjacent rank-zero runs when valid, sort descriptors by offset/length/rank
then dimensions, and remove identical descriptors. No global minimum cover
or exponential expansion is required. Equivalent noncanonical region
partitions/orderings remain valid and compare by their byte-set meaning.

### Guarantee, source production and calls

ReturnCoverage promises that **every byte in R is initialized in the actual
result at every normal publication**, after applicable postconditions,
crossed finally/cleanup and final validation. It does not merely describe
the implementation observed once. An exceptional/nonreturning path publishes
no result; the clause supplies no normal-completion promise, runtime check,
default value or exception suppression.

The source producer computes the guaranteed D common to all normal exits
under this model and publishes its stronger-than-G coverage for retained
raw-union-bearing results, including generated declaration-only references.
It must publish `[0,8)` for MakeWide, not discard that fact to G because the
consumer is separately compiled. Bodies can be analyzed by their producer
to establish this promise; callers do not inspect them. Producer verification
rejects an authored/preserved R not established on every normal result.
Rewriters assume responsibility for changed guarantees. Ordinary consumers
validate the structures/bindings above, then trust the published promise
without re-proving the provider Body.

At normal call publication, seed the result with **`D = G(T) union R`**,
plus any separately justified exact-copy facts. R proves initialized bytes
only: it does not assert a valid bool/rune/wrapper interpretation, pointer
provenance, initialized pointees, backing, roots, ownership or an active
alternative. Wide:uint64 is usable because all its bytes are covered and
its bit patterns are valid; pointer alternatives keep their independent
proof/dependency requirements.

For genuine whole-value forwarding, A2R1 intentionally amends C's
“provenance-bearing subtrees only” restriction: permit a whole-root
NormalResult CopyOrigin fact for any otherwise eligible complete Copy root
whose inline graph contains raw Union, even if dependency-free. The
unchanged `000D` payload then copies D and other applicable value facts
from the staged entry value. Several possible origins yield the
**intersection** of guaranteed coverage, while preserving all feasible
dependencies. This is exact value copying, not equal bits or
DependsOn/ForwardsBacking. Existing path, owner, role and provenance rules
remain; the amendment supplies no arbitrary mutation/coverage-effect summary.

### Binding and retained replacement obligations

Resolve ModuleRef/ExportKey and actual MethodDef/nominal type identities
first; compare complete result/parameter/receiver contracts and fixed/coupled
layouts before comparing numeric byte coverage. Matching row numbers, names,
sizes or a same-sized different U is insufficient.

For the selected profile, provider coverage satisfies expected coverage
exactly when
`G(T_expected) union R_expected subset G(T_actual) union R_actual`
under the already satisfied complete-type/layout correspondence.
Additional initialized bytes strengthen this dimension only; all other
expected effects, value validity, origins, caller domain, failure and
publication obligations still apply. CopyOrigin expectations retain C's
independent comparison, not this byte-set test.

Thus replacing MakeWide's `[0,8)` guarantee with no row/G or `[0,4)`
fails, even if the body, name, size and layout otherwise look compatible.
An independent stronger guarantee can satisfy a smaller expected region.
Check every expected MethodDef stub, including retained uncalled expectations.
A declaration-only provider reference is sufficient evidence; inspecting
its execution Body is neither required nor an alternative comparison.

Project the promise into source references and retained expected MethodDefs.
Keep original expectations separately from actual provider declarations.
If an optimization relies on stronger actual coverage than its old stub,
retain an additional stronger MethodDef/Import expectation under the
existing multiple-expectation rules; do not overwrite the old one or keep
the relied-on fact only in a binding cache. Direct MethodMap rebinding and
co-location/packing preserve both expectations and actual code origins.
Relevant schema, provider, layout, profile or promise changes invalidate
P/G/D summaries and affected native code before reuse.

### Reconstruction, rewriting and lowering

To encode the alternative-free construction already chosen above, R1 also
admits [A2E's exact record-05](standard-trrm-empty-fixed-array-initialization.md#exact-record-and-state-semantics)
None/zero-source-use **Initialize** combination
for an initialization-authorized, wholly vacant, local-rooted PlaceHandle
whose referent is an ordinary Copy Blittable raw Union with **zero instance
alternatives**. Keep A2E's selector/zero-field/captured-place restrictions
and 20-byte record; do not make a generic default constructor. This explicit
operation establishes the empty U's storage-completion obligation, not
initialized padding or automatic nominal validation. Nonempty all-void unions
instead need a real typed unit-alternative store, using the inherited R14
unit source. The empty-Union case requires 22; the inherited N=0 array case
still requires 20. Old selectors do not admit the new empty-Union use.

Source-free construction/store operations derive their local D as above;
call results consume the selected published ReturnCoverage/CopyOrigin.
Neither local nor imported callee-body inspection substitutes for a missing
published call promise. A TypeMap/BlockEntryState value of type U or a true
state test never sets `D=P(U)`. Preserve captures, current content versions,
copied snapshots, overlap invalidation and guarded joins/loop fixed points.

Re-encoding remaps MethodDef owners, Contract ranges/rows, TargetProfile
references, MethodSig/TypeSpec and Import/ModuleRef contexts, C's FieldPaths
and entry sources, and every dependent Body map/record/contract-row reference.
Rebuild Contract offset indexes, section/Body extents, instruction indexes
and applicable source locations; clone shared recipes when a using context
changes. R's byte offsets, lengths, ranks, counts and strides are quantities,
not references to substitute. Retain the semantic range set, per-Module
Feature closure and every original/stronger replacement obligation.

Reuse existing BoundStruct flags for Union identity and derive inherent
alternative unsafety from the containing Union after loading. The earlier
private round-trip derivation bug needs no new Union field. The **new
per-function promise**, however, must survive every persistence path used
by the adoption work, through retained Standard declaration snapshots or
explicit method-contract transport; an imported promise cannot be recovered
from a missing Body or discarded as G.

Lower whole-U Copy as a bounded representation transfer, not evaluation of
an uninitialized uint64 alternative. Preserve actual initializedness even
outside the statically guaranteed D. Do not copy every overlapping field,
clobber preserved tail/neighbor bytes, add observable conflicts/faulting
accesses, or introduce a discriminator/default fill. Actual packed alignment,
whole-counted-array state and immediate-parent bounds/suffix rules survive.
An integer ABI register/temporary carrying U is not an initialized uint64
source value; its width or incidental zero extension does not widen D.
EC/NativeParameter cannot bootstrap initialization. Native pointer calls
retain caller-unsafety and the
[known/opaque/unavailable backing distinction](native-parameter-pointer-backing.md);
R initializes no pointee, pin, owner or padding exposed by a raw transfer.

**Required implementation work, not completed by this proposal:** integrate
A2R1 selection/catalog/API/CLI support; Contract reader/writer and independent
validation; source D analysis and automatic per-function publication;
declaration-only references, C's whole-root applicability, binding/replacement
and retained expectations; bound/traversal/persistence preservation;
source-free aggregate calls/results, reconstruction/re-emission and native
JIT/AOT lowering. Complete the independent-provider execution cases below.
Malformed/false source claims or incompatible bindings must fail without
partial publication or binding mutation. A valid MakeWide/provider path
must **execute**; weakening it to G, rejecting it as unsupported, inspecting
the callee, disabling tests or emitting an image without execution evidence
does not complete this authorized slice.

## Required conformance cases

These are **future tests, not checks run here**. Exercise public compiler
APIs, unrelated synthetic types/providers and independently selected
source-free artifacts; distinguish structural admission, emission and
supervised headless execution.

| Case | Required outcome |
|---|---|
| Construction/state | One complete scalar or aggregate alternative completes U without initializing all alternatives. Whole-U state tests and live/live joins after different alternatives work. Missing initialization, partial child writes, vacant/maybe-live whole reads and alternative-state tests reject. Empty U completes only through explicit construction; all-void U requires one explicit alternative store. |
| Byte coverage/typed reads | Low-only Word is live with `D=[0,1)`; whole-U Copy and unsafe Low read succeed, while Wide read/uint64 Copy and a readable Wide borrow reject, including after Copy or a true whole-U state test. Wide-only has `D=[0,8)`; a subsequent Low store preserves full coverage and the seven untouched bytes. |
| Partial overlap/padding | After Wide defines `[0,8)`, store an alternative with uint8 at 0, padding `[1,4)`, and uint32 at 4. D becomes `[0,1)` plus `[4,8)`; Wide read rejects even if a particular backend happened to preserve the padding. A one-byte write into an initialized pointer cell retains byte coverage but invalidates its typed provenance; no pointer read is justified by D alone. |
| Joins/loops | Low-only/Wide live joins intersect to `[0,1)`, never `[0,8)`; opaque U Copy preserves that guarantee and actual branch-selected initializedness. Preserve live/vacant guards separately. Loop backedges cannot accumulate guaranteed coverage from mutually exclusive iterations without an admitted initialization proof. |
| Unsafe/authority | Constructor, direct/nested reads, writes and alternative reference/address formation require explicit scopes. An unsafe declaration alone and an unsafe RHS outside the destination store are insufficient. Whole-U Copy needs no added unsafe scope. Readonly roots, fixed selected fields, initialization-only second writes and conflicting overlapping loans reject. |
| Representation/validity | Execute Observe above and copy/assignment variants, including self-assignment. A small-only store does not justify a wider tail read. A valid integer write does not validate a bool byte value of 2, an invalid rune or a nominal invariant through another interpretation. Invalid unsafe/provider promises are not required runtime-check or ordinary-consumer semantic-lie detection cases. |
| Copy fidelity | Union alternatives containing binary floats preserve signed zero and NaN payload/signaling bits; admitted decimal alternatives preserve cohorts/noncanonical containers. Do not numerically convert, read an invalid inactive scalar, or fabricate defined padding while copying U. |
| Failure/order | Side-effecting array base/index/RHS run once in order; bounds failure prevents RHS/store/call. RHS/final-validation failures publish no new U, preserve the precommit destination and earlier actual effects, and do not clean a fictitious alternative. Fail-fast supplies no unwind promise. |
| Type/ownership closure | Every alternative, including private/unused/zero-size fields and N=0 element closure, needs RootlessCopy and supported Blittable layout. Reject unique/no-Copy/Cleanup, owning or support-bearing alternatives and inline cycles; pointer recursion does not expand pointee storage. Tagged source unions do not enter through raw Layout. |
| Geometry/boundaries | Keep the existing largest-alternative case; add empty/all-void and differently sized/padded alternatives, packing, transitive explicit alignment, nested arrays, N=0/1 and target-overflow boundaries. Preserve the minimum padding extent and actual packed occurrence alignment, not an invented zero/one-byte initialized value. Whole-U address eligibility uses actual complete U storage; its padding cannot supply an addressable void/zero-extent alternative. |
| Pointer/cross-feature | Pointer/integer alternatives and nested containing-array Copy preserve known backing/code origins; overlapping mutation invalidates facts for every affected view. Expired/short backing, lost required facts, false alignment, native initialization via EC/NativeParameter and extra extent from containing padding reject. No implicit marshalling or lifetime extension. |
| Published fresh return | MakeWide emits the 36-byte rank-zero `[0,8)` promise; MakeLow supplies only G. A producer with Low-only or mixed Low/Wide normal exits cannot publish `[0,8)`. Validate at final result publication, including finally/postcondition failure paths; the clause performs no runtime initialization. Structurally well-formed semantic lies remain producer-truth/audit cases, not mandatory consumer Body proof. |
| Declaration-only provider | An independently authored consumer expectation with `[0,8)` binds a bodyless MakeWide reference carrying that promise. Bind without source/Body inspection, then resolve the execution provider and perform the Wide read. A same-signature/bodyless reference with only G cannot satisfy this expectation. This positive route is required, not an optional unsupported test. |
| Copy forwarding | Publish C's whole-root entry-copy fact for an otherwise dependency-free Word identity function in A2R1. A staged Wide-initialized input yields full result D; Low-only yields prefix D; two possible origins yield their intersection. Preserve entry snapshots across later argument effects. A modified result must not acquire an exact-copy promise merely because its bytes remain initialized. |
| R framing/admission | Reject wrong/orphan/shared owners, duplicate resolved-profile keys, wrong result roles/types, bad row/profile references or high bytes, flags, truncated/overlong rows, spoofed counts/ranks, zero length, bad repetition counts/strides, overflow, out-of-extent and padding-only coverage. Empty rows are G-equivalent noncanonical input. Check retained uncalled/nonreturning declarations and missing per-Module 22/15/20 requirements. |
| Compact coverage | Single-byte/full-width intervals, first/last byte, zero repetitions omitted and one repetition folded, positive count through max-u64 where geometry fits, nested array-in-struct repetitions, packed/overaligned roots and zero-payload roots. Validate rank-zero/one/multiple-dimension sets without expanding N; distinguish extent overflow and capacity exhaustion. Equivalent reordered/overlapping/partitioned regions compare by set meaning. |
| Directed replacement | Expected `[0,8)` accepts equivalent/full coverage and rejects G-only or `[0,4)`, despite equal layout. A larger actual guarantee satisfies a smaller expectation. Reject wrong nominal/provider/profile/layout before byte-set comparison. Preserve every independent old/stronger expectation after binding to direct MethodDefs and after co-location; replacing a cached stronger provider rechecks the retained requirement. |
| Pointer-call projection | Where the result ABI is already admitted, form an ordinary provider method's function pointer with R in its contract anchor; direct and E9 normal results receive the same D. Losing R during projection/rebasing/view/persistence cannot yield an equal complete pointer contract. R still provides no new pointer-pointee validity or raw aggregate foreign mapping. |
| Source-free coverage | Reconstruct/re-emit Low-only, Wide-then-Low, padded-store and join cases from actual Body operations with no source/debug data. At calls use published R/C, not a reconstruction of the callee. Byte facts must survive nested containing values, array element stores, copies, state tests, row remapping and re-emission without becoming full by type. |
| Empty/zero-data construction | Reconstruct R1's explicit empty-Union record-05 initialization with `P=G=D` empty and uninitialized minimum padding. It cannot replace a live U or initialize a nonempty all-void Union; the latter needs an actual typed unit-alternative store. Preserve state/effects through empty Union return/Copy, and reject the new nullary use under older selectors. |
| Independent provider execution | Through explicit A2R1 production/reference/load/binding, run MakeWide and a consumer that copies its result, updates Low, then reads Wide, including storage in an A2 array and an admitted native pointer readback. Require supervised headless JIT and AOT against separately supplied and co-located providers; check selected values, original storage and untouched neighbors. Repeat after non-byte-identical semantic re-encoding and compatible provider relocation. Emission or descriptor admission alone is insufficient. |
| Schema isolation | Standard/F/F+S/A2/A2E/A2EU/A2EU+SF1, old C, A3/G/enum/RootPin/exception schemas do not decode `000F` or gain A2R1 semantics from equal versions/Feature 22. R1 cannot erase C or 22 while retaining their uses. Older type-only providers may satisfy unchanged type/layout expectations, but missing return promises cannot be supplied by their bodies, flags or another Module's manifest. |

## Integration owners and remaining decisions

If adopted, add the raw-Union completion exception and whole-state rules to
[initialization/flow](../specs/language/initialization-and-flow.md) and
[ownership/construction](../specs/language/ownership-and-cleanup.md);
cross-reference them from [grammar construction](../specs/grammar-spec.md#510-properties-and-accessor-local-effects).
Add representation-preserving typed Copy and indeterminate-storage text
to [data layout](../specs/data-layout-and-abi.md),
[overlap safety](../specs/memory-model.md#overlap-representation-access-and-unions)
and [Windows layout](../specs/targets/windows-x64-data-layout.md), referring
to their existing unsafe-alternative-access rule without changing it.
Clarify the existing typed-store interpretation in the
[instruction contract](../specs/il-instruction-set.md#storage-reads-stores-takes-and-retirement);
retain the existing type/layout/Body allocations. Add `000F`, its closed
MethodDef ownership/range shape, Feature 22 and directed coverage comparison
to the [clause](../specs/representation/contract-clauses.md),
[registry](../specs/representation/registry.md),
[metadata](../specs/il-metadata.md),
[manifest](../specs/representation/semantic-manifests.md) and
[binding](../specs/representation/binding-and-replacement.md) owners only
upon adoption. Integrate C's whole-root initializedness applicability and the
exact A2R1 composition without changing any older selector. This supersedes
ordinary simultaneous-field completion for raw Union and the earlier
proposal-only G fallback for provider results, not ordinary aggregate,
tagged-sum or native safety contracts.

The recommendation leaves **no in-scope policy sub-decision** to
implementation. Approval and implementation/qualification remain outstanding.
Owning/managed overlaps, safe active-case access, general aggregate native
outputs/by-value foreign mappings and frozen union-value codecs retain their
independent contracts; none is silently selected here. There is now a concrete
published carrier for the required provider-return coverage. Completing its
source-free/provider execution and retained replacement obligations is
implementation work, not permission to end the adoption slice with an
unsupported diagnostic.
