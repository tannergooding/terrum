# Static-field source-unit and friend access

> [!IMPORTANT]
> **Unapproved; provisional direction only under the user's authorized
> missing-contract workflow.** This document recommends a physical access
> contract. It does not approve it, allocate registry IDs, select a decoder,
> implement persistence or establish compiler/runtime support.

## Problem, scope and recommendation

The [immutable static-field proposal](immutable-static-field-persistence.md#static-identity-access-and-ownership)
selects a basic `StaticFieldContract.visibility` byte with
`0=private`, `1=internal`, `2=public`. It explicitly cannot preserve `file`,
member-scoped friendship or file-local owner identity. Its
[remaining-access decision](immutable-static-field-persistence.md#remaining-decisions-and-implementation-boundary)
therefore gates those declarations. The source behavior is already accepted
in [grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations)
and [names and binding](../specs/language/names-and-binding.md); the
[logical access records](../specs/representation/logical-records.md#source-unit-access-and-definition-identity-encoding-pending)
remain encoding-pending.

**Recommend five required access sidecars, candidate sections `005F..0063`:**
`SemanticSourceUnit`, `DefinitionAccess`, `FriendGrant`, `DeclaredCodeOrigin`
and `CodeAccessUse`. Extend the static visibility byte to `3=file`,
`4=friend`; use the existing two-bit visibility value `03` only as an
explicit extended-access marker on the participating ordinary definition
tables. The mandatory sidecar, not that marker, distinguishes the domains.

Preserve the eight static sections `0055..005C`, their strides and reference
fields, StaticField DefId **`24`**, clauses **`0013/0014`**, ImmutableData's
12-byte row and the existing `19/1` scope. In particular, keep StaticAccess
at 12 bytes and StaticDependency at 16 bytes. Access origins are required
sidecars to their uses, not new static storage, initialization authority or
another provider resolver.

Within the static proposal's independently admitted type/value scope, the
result represents:

* Exact semantic source units, file-local nominal identities and file-member
  access, independently of paths, SourceFile, source text and debug maps.
* Owner-private access plus explicitly listed same-module nominal friends,
  including exact type/constant substitution and one-slot `_` wildcards.
* Separate primary-declaration and implementation origins, including
  generated initializers/getters and operation origins surviving inlining.
* Required private/subordinate facts in static-provider expectations without
  making them imports, exports, callable APIs or accessible members.

This is an access amendment to the selected static/provider scope, not a
universal authorization program. It introduces no source syntax, external
friendship, reflective access API, native ABI, runtime publication protocol,
failure/retry policy or load-lifetime rule.

### Prerequisites and current boundary

The composed implementation needs the static proposal's complete owned and
expected groups, shared Import/Export binding, owner substitution and
publication records. It also needs the existing
[complete-type](../specs/language/types-and-normalization.md),
[constant-generic](../specs/language/constant-generics.md),
[consumer-checking](../specs/representation/reader-trust.md) and
[replacement](../specs/representation/binding-and-replacement.md) contracts
for every retained form.

The worktree's [bound facts](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs),
[binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) and
[static source tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/StaticInitializationCoreRegistrationTests.cs)
contain file-field origins, exact/type-wildcard friend fields and generated
friend getters, plus same-module checks. The inspected friend binder still
rejects constant arguments and limits wildcard templates to its source
generic-struct profile. These are implementation observations, not approval,
complete nominal identity, persisted access or execution evidence.

The [static reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmStaticFieldTables.cs)
implements the earlier basic-access candidate, not these records.
[CG-008](../implementation/conformance-gaps.md#cg-008) retains integration,
provider, Body and runtime gates. No existing test result qualifies this
candidate. Ordinary/source-free type, constant-value and checked-Body codecs
remain independently required; a referenced but unadmitted form refuses
affected use rather than acquiring an access-specific fallback.
The property's selected-getter and any required hidden-backing/initialization
association must likewise have their complete admitted representation.
A generated getter prototype is not approval of that separate carrier.

## Allocation audit, alternatives and schema boundary

Section, Contract and DefId numbers below are hexadecimal; Feature IDs are
decimal. They are separate namespaces.

| Namespace inspected | Current assignments and candidate interactions |
|---|---|
| Required sections | The [registry](../specs/representation/registry.md) and [machine registry](../specs/representation/trrm-1.registry.json) assign sections through `004B`, plus `7F00=SourceMap`. `8000..FFFF` remains nonsemantic extension space. |
| Active intervening candidates | [Foreign-interface identity](native-output-foreign-interface-method-identity.md#proposed-standard-metadata-delta) uses `004C`; [general predicates](general-predicate-graph-schema.md#new-candidate-sections) use `004D..0052`; [nominal enums](nominal-enum-persistence.md#allocation-and-representation) use `0053/0054`; static persistence uses `0055..005C`; [intrinsic identity](intrinsic-registration-identity.md#allocation-audit-and-alternatives) uses **`005D`**; the sole [mixed-substitution successor](constant-execution-mixed-substitutions.md#exact-successor-wire-contract) uses **`005E`**. Neither `005D` nor `005E` is available for this amendment. |
| Mentioned, unselected alternatives | Mixed substitutions explicitly decline a second table at `005F`. [Exception regions](standard-trrm-register-body-exception-regions.md#alternatives-and-allocation-selection) discuss `005F/0060` as a global alternative but select no global sections. These mentions are not active allocations or reservations. Inspection of the current proposal/specification Markdown and JSON registries found no active competing choice for `005F..0063`. |
| Contract kinds | Assigned `0001..0005`, `000A..000C`; separate candidates `000D` CopyOrigin, `000E` NativeParameter and `000F` ReturnCoverage; private/development `0010..0012`; static `0013/0014`. Allocate **none** here. Do not repeat the older static document's assertion that `000F` is still unassigned as an availability finding. |
| DefIds and other namespaces | Preserve assigned kinds and all companion `20..26` candidates, especially `24 -> 0055`. Allocate no DefId, TypeSpec form, Body opcode/record/map bit, Feature, target/profile, native symbol, runtime handle or source-map role. |

Select these **explicitly unapproved numeric candidates**, required policy
`0000`: `005F=SemanticSourceUnit`, `0060=DefinitionAccess`,
`0061=FriendGrant`, `0062=DeclaredCodeOrigin`, `0063=CodeAccessUse`.
No surrounding range is reserved. Five `u16` numbers consume five
assignments; lower numbers would not make these fields narrower.

| Alternative | Assessment |
|---|---|
| **Minimal amendment: static rows plus required identity, grant and origin sidecars** | Recommended. Keeps the selected static/storage/provider model and makes access inspectable without source or runtime execution. The origin-use table also covers ordinary calls to file/friend getters, so field-only persistence does not leave a callable escape. |
| Add only file/friend visibility bytes, a path, a friend-name list or an unowned source-input ordinal | Incomplete. It cannot bind an input to its originating Module, preserve separate primary/implementation origins, identify equal-named file owners or generic friends, or safely bind private group snapshots. |
| Add four bytes to StaticAccess and reuse predicate type patterns | A viable smaller per-static-use encoding, but still needs callable/contract/constant-use origins. PredicateTypePattern has captures and broader matching semantics; reusing it would require new applicability and Feature composition. Preserving the 12-byte access row and using one origin-use family avoids that consequential coupling. |
| Redesign every definition and Body as a common access-policy graph with delegated principals and executable permission predicates | Materially different and potentially coherent. It replaces the static snapshot, source-origin and binding model and needs a new policy/effect/proof contract. Existing file/friend semantics need only exact identities and slot matching; do not select this redesign or infer delegation. |
| Repack earlier unpublished section IDs | Permitted under format versioning, but no width/capacity or contract benefit justifies moving unrelated candidates here. No alternate assignment or historical alias is recommended. |

Under [format versioning](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts),
the proposed composite is the immutable static-field candidate, its expressly
selected declaration/Feature/value prerequisites, and **this access delta**.
A descriptive candidate label is
`trrm/1-draft-immutable-static-fields-access-1`; it is not an on-wire selector
or an implemented schema name. Exchange must agree on one exact, fixed
catalog/chapter/codec composition before decoding. Linked proposals do not
automatically compose their latest edits or activate unrelated array, native,
exception or GC candidates.

Keep `(1,0)`. **No alias or decoder is selected by this document.** An
existing basic-static decoder does not become an access decoder because the
old strides still fit. Equal version bytes, Feature 19, a recognized section,
visibility `03`, a filename or a native container cannot choose a schema.
Private persistence needs its own exact schema decision; no private revision
is assigned here.

## Physical records and common validity rules

All multibyte fields are little-endian. `R(T)` is a required one-based `u32`
row reference to T, high byte zero, in `1..00FF_FFFF` and within its actual
table. `R(T)?` permits zero only where stated. A DefId is instead the
permitted kind in the high byte plus a one-based low-24-bit row.
TypeSpecRef retains its existing four-byte complete-type grammar, including
admitted equivalent row/F2/F3 forms, never a Body TypeMap token.

Fixed sections contain exactly `recordCount * stride` bytes. FriendGrant
uses the existing `u32 offsets[recordCount+1]` variable-table framing.
Ranges are `(0,0)` iff empty; otherwise their inclusive end is checked
against table and 24-bit capacity. Counts, payload extents and instruction
ordinals are `u32` integers, not tagged references or Body-local `u16`
indexes. All additions, products and allocations are nonwrapping and bounded
by section/artifact extents and supported resource limits. Unknown tags,
wrong namespaces, nonzero flags/reserved values and trailing payload bytes
reject before affected use.

| Candidate section | Stride / exact field sequence |
|---|---|
| `005F` SemanticSourceUnit | **12**: `u8 ownerKind, u8 flags=0, u16 reserved=0, u32 ownerRow, u32 sourceInputOrdinal` |
| `0060` DefinitionAccess | **28**: `u32 declarationDefId, R(SemanticSourceUnit) primaryUnitRef, R(SemanticSourceUnit)? implementationUnitRef, R(SemanticSourceUnit)? fileIdentityUnitRef, u8 accessKind, u8 declarationRole, u16 reserved=0, u32 friendFirst, u32 friendCount` |
| `0061` FriendGrant | Variable: **12-byte prefix** `R(DefinitionAccess) grantorAccessRef, R(TypeDef) friendTypeDefRef, u32 slotCount`, followed by `slotCount` ordered slots below |
| `0062` DeclaredCodeOrigin | **12**: `R(MethodDef) declarationMethodRef, u8 partKind, u8 flags=0, u16 reserved=0, OwnerRef ownerTypeSpec` |
| `0063` CodeAccessUse | **16**: `u8 siteKind, u8 flags=0, u16 reserved=0, u32 siteRow, u32 instructionOrdinal, R(DeclaredCodeOrigin) originRef` |

`OwnerRef` preserves the static proposal's presence rule: zero exactly when
the declaring owner has no applicable formals; otherwise the exact complete
nominal owner construction with every ordered type/constant actual. It is
not a receiver's runtime type. Symbolic terms need their admitted formal
context; concrete matching resolves required actuals first.

### SemanticSourceUnit: identity, not location

`ownerKind` is exactly `1=OwnedModule`, selecting R(Module) in `ownerRow`,
or `2=SelectedProviderExpectation`, selecting R(ModuleRef). For kind 2 the
ModuleRef belongs to the using/expected-definition Module. It selects the
already selected provider, not another source-file service or resolver.

`sourceInputOrdinal` is the exact zero-based input identity already carried by
`BoundFunction.SourceInputOrdinal` and
`BoundStaticFieldDefinition.SourceInputOrdinal`, bounded by
`BoundProgram.SourceInputCount`. It may be zero; the nonzero table reference,
not the ordinal, indicates presence. The semantic unit is
**(resolved originating Module identity, sourceInputOrdinal)**. This reuses
the compiler's existing input identity and requires no separate key issuer.
It is not a SourceFile row, path, content digest or runtime-load handle.

A producer distinguishes every separately supplied input, including generated
inputs, even when bytes, filenames or paths agree. For each owner represented
in the table, emit exactly one row for every source input, with ordinals
contiguous from zero; the row order is not semantic. This makes ordinal bounds
structurally checkable without treating optional SourceFile/SourceMap data as
authority. Reordering inputs changes the ordinal identity and may make a
retained file/friend/origin expectation incompatible; path or content equality
cannot repair that difference. Different Modules using ordinal zero do not
share a unit. For kind 2, the selected ModuleRef identifies the provider
expectation; binding compares its complete source-unit directory to the
selected provider's owned directory. No kind-2 row creates consumer-authored
code in the provider unit. Stripping SourceFile, SourceMap and all source text
leaves the semantic identity intact.

One artifact has at most one row for an identical
`(ownerKind, ownerRow, sourceInputOrdinal)` triple. Different selected-reference
paths may resolve to the same semantic unit only after complete binding agrees.

### DefinitionAccess: domains and file-local definition identity

`declarationDefId` permits only **`13` TypeDef, `14` FieldDef,
`16` MethodDef, `1E` PropertyDef and `24` StaticField**. It names the direct
local/expected definition, not an Import token. This expands no Import/Export
target set. Alias source access is checked before expansion; this proposal
does not add an AliasDef, nested-type production or another nominal identity.

Require exactly one DefinitionAccess for every definition in a retained
access closure: an extended-access definition, a file-local identity, a
friend target, a code-origin declaration, and the owners/accessors/private
group declarations needed to interpret those records. Basic definitions
outside that closure may retain their existing flags without this sidecar.
There is no optional-debug substitute for a required row.

`accessKind` is exactly `0=Private`, `1=Internal`, `2=Public`,
`3=File`, `4=Friend`. Basic values agree with existing visibility bits.
On TypeDef/FieldDef/MethodDef/PropertyDef, values 3/4 require low visibility
bits **`03=ExtendedAccess`** and the unique sidecar; 03 alone means neither
file nor friend. Other flag bits retain the selected base schema's meanings.
On StaticField, `StaticFieldContract.visibility` equals `accessKind`,
including 3/4; StaticField's writability flags remain only 0/1.

The source restrictions still apply: namespace-level types/functions allow
internal/public/file, not private/friend; friendship belongs to nominal
members. Generated private unspeakable initializers/constructors keep the
static role/non-callability rules, not a new source visibility default.
Richer instance/accessor facts can occur in the required static closure;
this does not admit an otherwise unsupported instance or setter/init codec.

* `primaryUnitRef` is the actual primary declaration's semantic input.
  It owns file-member permission and the source context in which friend
  targets and primary contracts were bound.
* `implementationUnitRef` is the actual implementation's input when its
  origin is retained; otherwise zero. It may differ for a partial callable.
  An origin-only retained declaration can preserve it after its executable
  Body is removed; that does not make the stub executable.
* `fileIdentityUnitRef` equals `primaryUnitRef` exactly for a namespace-level
  file-local TypeDef or module-level MethodDef. It is zero otherwise,
  including a file-visible nominal field/accessor. Those members keep the
  ordinary no-duplicate-member identity across partial parts.
* `friendFirst/count` is a nonempty exclusive FriendGrant range exactly for
  access kind 4, otherwise `(0,0)`. Every grant belongs to that one range and
  agrees on `grantorAccessRef`; no shared or orphan grant rows.

File-local nominal identity is the selected Module, semantic file-identity
unit, namespace/name and declared arity. A non-file nominal identity has no
file discriminator. TypeDef references select the resolved definition;
equal namespace/name/arity in units A and B remain distinct. Names and
arity still use the ordinary source collision rules. Primary-unit metadata
does not add a file discriminator to an ordinary type or split its parts.

Declaration roles are `1=OwnedDefinition`, `2=ImportedExpectation`,
`3=SubordinateExpectation`:

* Role 1 agrees with the declaration's physical Module ownership and uses
  kind-1 units owned by that Module; it is not an imported proxy.
* Role 2 has its existing Import, uses kind-2 units through that Import's
  selected ModuleRef, and allocates no implementation/storage.
* Role 3 has no independent Import/Export. It is reached through an expected
  owner/group, selected property/getter, friend identity or retained code
  origin. Its kind-2 units identify that same already selected provider.
  It is comparison/provenance data, not ordinary lookup, call, storage or
  public access.

StaticField roles agree exactly with StaticFieldContract roles 1/2/3.
All units of one access record resolve to its one originating Module.
A physical consumer-owned proxy is not a consumer-authored friend type.
The subordinate correspondence rules below are mandatory, not an invitation
to bind an unexported definition by a guessed public key.

### FriendGrant: exact identities and ordered argument slots

`friendTypeDefRef` selects a **nominal declaration**, not a runtime object,
interface witness, receiver type, export-name string or structural layout.
For an owned grant it is an owned TypeDef in the grantor's originating Module,
not a TypeDef Import. An expected grant names its bound same-provider
expectation. The declaration's source context must also be allowed to name
that target: in particular, a file-local friend target must be accessible
from the grantor's primary unit. Same-module identity alone cannot repair
inaccessible target lookup.

Require exactly one slot per declared formal, in formal order, including
type, constant and an interface's explicit self formal where applicable.
Nongeneric targets have `slotCount=0`. Source-selected defaults are already
expanded. There are no omitted trailing slots, inferred arguments or packs.

Each slot starts with `u8 slotKind, u8 flags=0, u16 reserved=0,
u32 payloadBytes`, followed by exactly that payload:

| Kind | Payload | Meaning |
|---:|---|---|
| `0` AnyArgument | **0 bytes** | `_` in exactly this one type/constant slot; no inference, capture, guard or constraint waiver. |
| `1` ExactType | **4**: required TypeSpecRef | Complete exact type term bound in the granting owner's context; substitute that owner's formals, then normalize. Type-formal positions only. |
| `2` ExactConstant | **4**: R(ConstantSource) | Already admitted typed constant, including an independently required checked request under its own codec; preserve its exact domain/value/dependencies. Constant-formal positions only. |
| `3` OwnerConstantFormal | **4**: R(GenericParam) | Exact constant formal of the grantor's owning TypeDef, identified by owner/kind/ordinal. Forward its admitted value without reconstructing it. Not a method formal, runtime parameter or a new ConstantSource tag. |

The FriendGrant extent is exactly `12 + sum(8 + payloadBytes)`.
Payload length does not select another tag or decoder. Missing type/constant
codecs, unresolved formal identities or an unrepresentable nested dependent
type refuse affected use. Do not encode an owner constant formal as a
TypeSpec formal, an inline integer or an inference hole.

Kind 2 inherits ConstantSource's exact typed value/request contract. A
source-independent grouped constant expression can supply its admitted
canonical value, preserving every required construction/program dependency.
It is not a promise to run a runtime initializer. Failed required constant
work, invalid domains or exhaustion refuse the affected use, not produce a
nonmatching friend, empty grant set or default value.

The access delta does **not** silently select the existing encoding-pending
general symbolic constant-expression codec. For example, an open
`friend<Reader<(N + 1)>>` or an ExactType containing a nested constant-formal
argument needs a complete owner-parametric constant/type representation under
the [logical constant-generic owner](../specs/representation/logical-records.md#constant-generic-domains-encoding-pending).
The current ConstantSource/ConstantExecution layout is not presumed to supply
that unclosed input binding. Until that independent codec is selected,
affected template persistence is unsupported. A producer cannot freeze one
observed N, discard the term, invent a private evaluator carrier or publish
an empty grant. This preserves the accepted source contract and the static
proposal's existing generic/value prerequisite; it does not claim that this
access amendment closes that broader value-codec decision.

#### Access-domain calculation

For a member of a closed owner O, determine its domain from the records:

* Private: code declared by O's exact nominal definition across its checked
  partial parts, under the accepted private-owner rule.
* Internal: code of the originating Module.
* Public: the ordinary public domain.
* File: code whose **actual declared origin unit** equals primaryUnitRef.
  Owner identity, partial membership or phase authority supplies no exception.
* Friend: the private-owner domain **union** code declared in the exact
  listed nominal friend instances after O's substitution.

For a friend instance, constructor identity, total arity and every formal
kind must agree. Exact slots compare normalized complete types or canonical
typed constants. A wildcard ignores only its slot value, while ordinary
formation/domain/qualification checks still apply. A nongeneric helper is
the same helper for every O. No matching enumerates all wildcard instances,
executes a runtime type test, solves an equation or captures an argument.
Only the friend **constructor** must be same-module; its exact arguments can
refer to ordinarily selected external type/constant dependencies.

The source fragments here describe accepted syntax, not runnable examples
or current compiler coverage: `friend<Reader<T>>` on `Owner<int32>` selects
Reader<int32>, not Reader<float64>; `friend<Reader<_>>` selects eligible
instances of that same Reader declaration, not Other<T>. Mixed type/constant
slots preserve their positions and exact nominal constant domains.

Grant membership is a set union, not an order-dependent chain. This proposal
adds no source diagnostic rule for repeated written targets. Canonical writers
deduplicate identical normalized patterns; otherwise-valid duplicate owned
patterns are redundant noncanonical data. Overlapping but nonidentical
patterns need not be minimized. Empty friend lists remain invalid.

Always intersect the result with effective containing-type and accessor
access, then retain ordinary initialization, mutability, unsafe, ownership,
loan, lifetime, backing and representation checks. Friendship never widens
a containing file-local type, makes a private sibling accessible, changes
writability or supplies initialization-phase authority.
Reject a file-local type in any exposed signature or contract whose effective
access escapes its unit. Retaining a private comparison/provenance fact is
not exposing that type as a callable contract; returning a hidden implementation
through an accessible public interface keeps that interface as the contract.

## Declared code origins and source-free access sites

DeclaredCodeOrigin `partKind` is `1=PrimaryContract` or `2=Implementation`.
It selects the corresponding nonzero unit in the referenced MethodDef's
DefinitionAccess. The nominal principal is that MethodDef's declared owner,
under ownerTypeSpec; module-level code has no nominal principal. Resolve
expected definitions to their actual originating Module before computing
the principal. The record is a producer-published origin, **not an
“authorized” bit or a runtime capability**.

Primary contracts/attributes retain the primary's unit. An authored partial
implementation retains its implementation unit. Generated expression
initializers retain the expression's field/property input; a generated getter
retains the accessor input that generated it. Their MethodDef owner and static
role/property association must agree. A helper in another input does not
borrow the initializer or constructor primary's unit.

Generated closure/cursor/runtime containers and qualified-self adaptations
retain original authored principals; they do not become new friends.
An interface's authored/default body has the interface declaration principal,
not the implementing type or its explicit self argument. An extension body
has its actual provider principal, not its receiver.

CodeAccessUse's closed site kinds are:

| Kind | `siteRow` / `instructionOrdinal` | Required origin association |
|---:|---|---|
| `1` StaticAccess | R(StaticAccess) / **0** | Exactly one per retained access row, including phase-authority accesses. Its origin identifies the using code, not the field/provider. |
| `2` StreamInstruction | R(Body) / zero-based logical instruction ordinal | Explicit origin for that instruction's declared accesses/calls. Validate against decoded stream instructions, not byte offsets, Blocks or source positions. |
| `3` BodyDefault | R(Body) / **0** | Exactly one default for each retained Body using this access closure. Used by its instructions/compact operation unless an instruction override exists. |
| `4` PredicateNode | R(PredicateNode) / **0** | Original declaration/phase context for a node performing a restricted member/static/immutable read or call; defaults follow its owning attachment. |
| `5` ConstantRequest | R(ConstantExecution) / **0** | Context that forms the request and its restricted immutable inputs/views; execution of the selected Body separately uses that Body's origins. |

Keys `(siteKind, siteRow, instructionOrdinal)` are unique. Overrides apply only
to their exact site. StaticAccess is occurrence-owned, not interned by target
and authority: each StaticPlace access occurrence has its own StaticAccess row,
and different operations share only the StaticDependency behind that row.
The StaticAccess sidecar and its CodeAccessUse identify the same using origin.
A StaticPlace instruction's applicable origin must agree with that association.
Compact Bodies use BodyDefault and cannot hide mixed origins in an invented
instruction index. Predicate/request rows with context-dependent meanings are
cloned when necessary.

A Body default normally points to its own implementation origin. Inlining
keeps the caller default and gives moved instructions the callee's origins;
single-origin outlining can retain the original default. Multi-origin
operations require distinct representable sites or a decomposition preserving
all semantics; one instruction cannot OR origins into transferable privilege.
The producer establishes truthful association with transformed operations.
Consumers check the represented references/relationships, not source-text
history or arbitrary Body truth.

Every supported declaration-accessing operation obtains one original context:
StaticAccess uses kind 1; ordinary calls/property-getter selection use the
Body default/override or predicate node; a required immutable observation or
input-view formation uses its retained original context. A bodyless primary
contract's ordinary context derives from its MethodDef primary origin; an
allowed-only footprint/dependency retains that association without executing
a read. A bare identity or subordinate snapshot conveys no access permission.
Intrinsic/source forms lacking the required independently admitted origin
cannot invent one.

Default contract/predicate contexts derive from the exact owning MethodDef,
its recorded phase unit and the currently bound complete owner construction,
not an arbitrary DeclaredCodeOrigin row with the same method number. A
context that cannot be recovered requires the explicit occurrence association
or refuses affected use.

For access checking, the using Module/formal context is that original declared
context, not the physical Body's Module after inlining. Its already selected
Import/provider relationships remain retained and are validated in their
original contexts. This changes no provider/key choice and does not rerun
lookup. A moved local StaticAccess stays local to its original Module; a
moved imported dependency keeps its original using Module and resolver graph.

Calls do not lend the callee's privilege to callers. A public wrapper may
perform its own permitted internal read, but a caller's sibling operation
still has the caller's context. Inlined permitted reads remain original-code
operations, not authority for newly introduced caller reads. Retaining a
place/reference keeps its ordinary provenance/access/lifetime facts; it is
not a general friendship token.

No StaticPlace opcode, Body prefix, map bit or compact static operation is
selected here. The eventual supported static Body carrier still carries its
existing typed StaticAccess reference and now must preserve these origin
associations. Missing Body/lowering support remains a gate.

## Provider binding, replacement and independent consumption

Resolve the selected Module/revision, export key and declaration kind through
the existing resolver **before** comparing access. Preserve the separate
owner Import and exact static field/group correspondence.

Only public owned declarations with accessible public owners remain metadata
exports. File/friend fields, file owners, generated initializers and
subordinate grants/origins do not become Import/Export targets. A role-2
StaticField still requires its public provider export; richer access does not
permit a private same-module field Import as a cross-module loophole.
Private/file/friend role-3 siblings remain unusable as StaticAccess or
ImmutableData targets under the static proposal.

Expected source units carry the already selected provider's identity and exact
sourceInputOrdinal, not the consumer's ordinal. Required correspondences are:

1. Bind imported public owner/member identities normally.
2. Match subordinate fields/initializers/constructors through the existing
   complete group snapshot, declaration/execution ordinals and role edges.
   Match subordinate getters through PropertyDef's selected accessor edge.
3. Match friend/private owner TypeDefs using their complete semantic nominal
   identity: that selected originating Module, namespace/name, declared arity
   and the file-identity unit exactly when present. These are typed identity
   expectations in the selected owner's access closure, not a new public key,
   name-only equality, provider search or source lookup.
4. Match ordinary subordinate code declarations by the already selected
   nominal owner, designated callable identity/complete normalized signature
   and file discriminator where applicable. Generated static declarations
   follow group/role/property edges.
   No private method-name convention establishes an association.
5. Map corresponding formals by owner/kind/ordinal, and check units, grants,
   code principals and every complete type/value/program edge in context.

Subordinate-only TypeDefs/MethodDefs allocate no consumer storage, conformance
or callable entry. Retaining their facts for comparison/inlined provenance
does not let unrelated consumer code declare itself in that type/unit.
Authentication remains the selected environment's obligation.

The static proposal's conservative snapshot relation compares **exact access
domains**, not numeric visibility ordering. File permission/unit and
file-local owner identity must agree wherever relied on. Friend patterns are
compared as normalized sets after owner/formal correspondence, retaining
same-module constructors, every exact slot and wildcard positions.
No friend addition/removal, private-to-file conversion or containing-type
change is silently labeled strengthening. A broader relation would require
its own explicitly specified access-set proof.

For retained closed constant slots, compare complete canonical domains and
values plus every other retained observation/dependency. Owner-constant
forwarding compares the corresponding formal identity/domain before concrete
substitution. An unclosed expression requires its independently selected
complete term/program and comparison contract; a signature, ConstEval tag,
Body bytes or sample value is not that contract. Unsupported comparison
refuses compatibility. It does not narrow friendship to the constructions
seen in a prototype or make a failed constant an empty permission set.

Primary/body units unrelated to a retained identity, file permission or code
origin need not be frozen merely because a basic public/private sidecar
records them. Moving an ordinary non-file type's primary does not create a
new nominal definition. Conversely, a retained file access or inlined original
origin keeps its exact required unit relationship; replacement cannot change
that relationship by rebinding old code to a new unit.

Compare each expectation independently and retain the original graph plus
stronger provider facts actually used. Equal values, names, layouts, paths,
addresses or co-location cannot merge two source units/nominal friends.
Caches include selected originating Module/revision, complete definition and
unit identities, substitutions, access/program facts and admitted schema/rules.
They are not keyed solely by rows or earlier acceptance.

### Producer truth and mandatory consumer checks

Producers check authored access, target lookup, actual partial origins,
accessor narrowing, interface conformance authority and transformed origins.
They publish truthful records and establish all ordinary field/call/state/
unsafe/lifetime facts. New or retargeted accesses require new justification;
copying an old origin row is not that justification.

Consumers, including independent source-free readers, check before affected use:
exact schema/support; framing/extents; required sidecars; closed tags and flag
agreement; exclusive ranges/unique sites; reference kinds/bounds; Module and
source-unit ownership; file-identity rules; nominal/formal/slot shape; code
origin associations; complete selected provider correspondences; and retained
type/value/program/Feature closure. They derive represented access from those
identities, not an authorization byte. Structurally incompatible domains,
cross-module friend targets and mismatched origins refuse affected use.

Under [reader trust](../specs/representation/reader-trust.md), consumers need
not re-prove a producer's declared-code history, arbitrary Body access or
constant validity semantics against stripped source. A structurally valid
but false origin/grant/body fact violates producer truth; execution relying
on it is not guaranteed. An optional semantic audit is distinct from ordinary
safe structural consumption. Neither a checksum nor a “trusted producer”
flag replaces the required checks.

## Remapping, retention, admission and lowering

Packing, replacement, trimming and source-free re-emission must decode and
remap:

* Module/ModuleRef selectors, semantic source-unit references and exact
  file-identity discriminators; preserve sourceInputOrdinal rather than
  deriving it from the remapped SemanticSourceUnit row.
* DefIds and all TypeDef/MethodDef/PropertyDef/StaticField origin identities;
  primary/body units, grants, formals, complete TypeSpec/constant arguments,
  and every retained constant dependency/program graph.
* StaticDependency's original Import/owner/group/publication closure, including
  every role-3 access snapshot, without a parallel provider choice.
* Origin-use site rows and actual instruction ordinals after Body rewriting;
  predicate occurrences, request/input-view associations, default/override
  relationships and any separately retained SourceMap/debug slots.
* Owned ranges, offsets, counts, indexes and per-Module requirements.

Co-located provider code keeps provider-origin principals; consumer proxies
keep their expected/actual distinction. Shared context-sensitive rows are
cloned before a rewrite changes another using Module's meaning.
Internalizing an Import cannot turn expected file/friend facts into
consumer-local authorship or discard an original expectation still relied on.
No byte substitution, path/name mangling or artifact-global origin cache is
a meaning-preserving conversion.

Trimming may drop a grant/origin/unit only after all remaining identity,
access, contract, program and replacement edges are discharged. An origin-only
declaration needs no executable Body solely to prove history, but required
ConstEval execution/comparison still needs its independently admitted program.
Folding a value does not automatically discharge initialization timing,
effects, publication, provider, code or lifetime obligations.

Canonical writers use declaration-owned access rows, exclusive grant ranges,
first-logical-use origins, deterministic site keys and minimal exact slots.
Forwarded owner constants use kind 3; source-independent completed constants
use kind 2. Ordinary readers accept valid consistently remapped permutations
and redundant equivalent patterns, not duplicate access owners/sites or
overlapping owned ranges.

Allocate no new Feature. These required access schemas must be supported in
the selected composition even without a numeric capability. Feature 19 still
belongs only to actual retained ImmutableData uses in each using Module.
Source units/static fields/grants alone do not require 19, 11 or 17.
Retained values/ConstEval programs/graphs add their own exact Feature closure
under existing owners. Required constant work needs complete
constant-execution/evaluation support, not an access-only exception.

Native/JIT/AOT lowering consumes checked declaration and operation principals.
It may share code only while retaining exact owner substitutions and original
accesses. It must not substitute CLR file/assembly visibility or a runtime
receiver for those facts. This proposal changes **none** of the existing
static gate/publication, waiting/cycle/failure, explicit retry, cleanup,
quiescence, backing or load-generation obligations.

## Integration owners and exact proposed edits

These are adoption instructions, **not edits made by this proposal**.

| Owner | Exact integration |
|---|---|
| [Grammar §3.1](../specs/grammar-spec.md#31-source-file-structure-and-declarations), [static initialization](../specs/grammar-spec.md#static-initialization-and-load-scoped-storage), [properties §5.10](../specs/grammar-spec.md#510-properties-and-accessor-local-effects), [partial §5.12](../specs/grammar-spec.md#512-modifiers-cleanup-and-finalization) | Preserve all accepted productions/domains. Link their semantic identity, actual-body origin and access obligations to the selected representation; add no source keyword, default access, friend-module grant or nested-type production. |
| [Names and binding](../specs/language/names-and-binding.md#namespace-and-member-declaration-spaces), [recorded binding](../specs/language/names-and-binding.md#calls-conversions-and-recorded-binding), [complete types](../specs/language/types-and-normalization.md#complete-type-terms), [constant generics](../specs/language/constant-generics.md#canonical-value-identity) | Preserve file-local identity, exact nominal/formal/constants and original principals. Add representation cross-references, not path/string equality or inlining privilege. |
| [Logical source-unit access](../specs/representation/logical-records.md#source-unit-access-and-definition-identity-encoding-pending) | Replace this slice's unassigned physical-record/visibility deferral with the five records, slot variants and required consumer/binding rules. Preserve the logical source semantics, reflection separation and independent type/value/Body gates. |
| [Logical static records](../specs/representation/logical-records.md#static-initialization-and-load-scope-encoding-pending) | Require retained access identities/origins in owned and expected static closures. Preserve the existing 20/12-byte approved static shapes and roles/demands; do not close StaticPlace/recovery/runtime deferrals. |
| [Metadata rows/flags](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [registry](../specs/representation/registry.md), [machine registry](../specs/representation/trrm-1.registry.json) | Add required candidates `005F..0063` and their exact shapes/relations; replace participating ordinary visibility `03` rejection with ExtendedAccess requiring DefinitionAccess. Register every tagged typed-reference field, complete type field, variable slot extent and origin site namespace. Preserve unrelated masks, assignments, Module cursors and reference target sets. |
| [Binding and replacement §§1–5](../specs/representation/binding-and-replacement.md), [reader trust §§1–5](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md), [admission](../specs/representation/requirements-and-admission.md) | Add selected-provider subordinate correspondence, exact access/program comparison, original origin retention, structural checks and context-sensitive remapping. Preserve producer truth and ordinary noncanonical consumption. |
| [Immutable static-field proposal](immutable-static-field-persistence.md#static-identity-access-and-ownership), [binding](immutable-static-field-persistence.md#binding-source-free-use-and-replacement), [remaining gates](immutable-static-field-persistence.md#remaining-decisions-and-implementation-boundary) | Supersede only the richer-access deferral and basic visibility domain: compose 3/4 with the mandatory sidecars and complete expected access closure. Leave all eight row strides, clauses, DefId/Feature scope, public-only exports, publication and unrelated gates unchanged. |
| [ImmutableData declaration reference](immutable-data-declaration-reference.md#exact-owner-substitution), [Feature admission](immutable-data-feature-admission.md) | Close the separately gated file/friend access slice only when this complete composition is adopted/supported. Preserve direct 24, kind-2 targets, owner substitution and per-Module 19 detection. |
| [Source binding cases](../specs/conformance/source-name-binding.md), [static cases](../specs/conformance/source-static-initialization.md), [constant cases](../specs/conformance/source-constant-generics.md), representation binding/reader/format cases | Add the scenarios below to the owning families. A proposal-local case is not an active regression or execution result. |

Implementation handoff follows source/bound identities and shared traversal,
mandatory verification, schema-selected production/reading, exact provider
comparison and remapping before supported lowering/qualification.
Reconcile the maintained inventories only for demonstrated support; this
document does not edit them or mark CG-008 complete.

## Positive, negative and boundary conformance scenarios

These are required future cases, not tests run. Supply independently admitted
type/value/program/Body/runtime prerequisites except when their absence is
the scenario. Source fragments are accepted-contract illustrations, not
statements that the current compiler admits them.

| Case | Required outcome |
|---|---|
| Two input units with equal paths, bytes or optional SourceFile rows | Distinct semantic keys/identities; no shared file access or file-local nominal definition. Debug stripping changes neither. |
| Two Modules use the same sourceInputOrdinal and qualified type name | Distinct originating Module/type/unit identities. Packing does not merge them or their partial parts. |
| Ordinary type has parts in A/B; file field is declared in A | A code can access it; B implementation cannot, including initialization reads/writes in an otherwise valid group/phase. |
| Partial primary/contract in A, implementation in B | Primary contract uses A; Body uses B. Neither borrows the other's file permission. |
| Generated field initializer/getter in A; helper implementation in B | Generated code retains A and its actual nominal owner; helper retains B and cannot use a file field in A merely through a role/call. |
| Equal-named file-local TypeDefs in A/B, including owners of equal static fields | Direct type/field references remain distinct through export snapshots, substitution and re-emission. File fields themselves still obey duplicate-member rules within one owner. |
| File-local and ordinary name/arity entry are both visible in A | Preserve source collision, not hidden shadowing or overload-group union. |
| Owner and explicitly listed ordinary friend type have parts in multiple inputs | Private owner and listed friend code keep their respective nominal privileges across parts; unrelated types do not. |
| F is a friend, F lists G, caller invokes F, or receiver is an F object | No transitive/call/receiver-derived access for G or the caller. Grants concern only the named member. |
| Listed interface I has an authored/default Body; another type implements I | I code may qualify; implementer code does not acquire I's principal, including qualified-self adaptation. |
| Extension has friend-type receiver but unrelated provider | Receiver supplies no friendship. Use actual declared provider origin. |
| `friend<Reader<T>>` under Owner<int32>; Reader<float64> tries access | Exact substitution grants Reader<int32> only. Same size, alias spelling or runtime receiver cannot repair mismatch. |
| `friend<Reader<_>>`, mixed type/constant slots, generic class/interface/struct targets | Match only that same-module constructor and full arity/kinds; wildcard matches one eligible slot without inference/capture or dropping other exact arguments. Do not specialize policy to the current struct prototype. |
| Exact enum/wrapper constant has equal carrier bits to another domain | Domains differ; no match. Admitted equal-valued enum aliases or equivalent normalized exact constants preserve canonical equality. |
| Owner constant N is forwarded, including a constant-only owner | Kind 3 identifies the exact owner/kind/ordinal; retain full owner substitution. No constructor replay, TypeSpec-formal alias or zero-owner shortcut. |
| Source-independent grouped constant expression supplies an admitted canonical value; required work fails or exhausts | Preserve its domain/value/dependencies. Failure refuses use; no empty friend set, nonmatch, default or runtime fallback. |
| Open owner-dependent constant expression, such as N+1, has no selected complete parametric constant-input/term codec | Refuse affected template persistence without changing accepted source semantics. Do not invent an evaluator schema or freeze one convenient actual. |
| Nested dependent complete type/value codec is absent | Refuse that retained generic form. Do not erase its constant term or claim all friend generics supported from type-only tests. |
| `_` used as top-level target, pack, capture, guard, runtime predicate or wrong-slot type term | Reject invalid shape/applicability; no existential/inference reinterpretation. |
| Friend target originates in another Module, including a consumer-local imported proxy | Reject the grant. Co-location, unsafe context or identical namespace/key does not make it same-module. |
| Friend constructor is in M, an exact argument type/value dependency is from P | Permit ordinary admitted argument binding; P does not become a friend Module or supply the code principal. |
| Grant names an inaccessible file-local target in another input; containing owner is file-local outside caller's unit | Reject target binding/effective access. Friendship cannot publish or widen that type. |
| File/friend static property and generated getter; accessor attempts broader visibility | Preserve the property's exact selected getter and inherited/narrowed domain; reject broadening. Hidden backing supplies no public field lookup or grant to other private siblings. |
| Public signature/contract names a file-local type; public interface hides a file-local implementation | Reject the exposed file type; retain the accessible interface contract and original private implementation identity without publishing it. |
| Friend/file permitted caller reads vacant data, writes fixed live storage, lacks unsafe scope or loses backing/lifetime | Ordinary semantic checks still reject. Permission is neither completion, mutability nor lifetime authority. |
| Inlined friend/file read sits beside newly introduced unrelated caller read | Moved operation keeps original origin; new caller operation cannot reuse it. Calls/Body defaults do not union principals. |
| BodyDefault, instruction override and StaticAccess origin disagree; compact Body claims a made-up instruction origin | Reject structural association. An unsupported multi-origin operation must be represented without losing semantics or refused. |
| Missing sidecar, unknown visibility/slot/site tag, nonzero reserved fields, wrong reference kind, trailing variable payload | Reject exact-schema structural input; do not try basic/historical decoders. |
| Duplicate access owner/site, shared/overlapping grant range, orphan grant, wrong primary/file-identity unit | Reject invalid ownership/identity. Valid redundant equivalent grant patterns remain noncanonical set duplicates. |
| First/last 24-bit row, sourceInputOrdinal zero and maximum, huge slot/payload/count product, instruction ordinal at/one past end | Respect exact nonwrapping bounds; zero is a valid unit ordinal, valid representable endpoints survive, and one-past/exhausted cases refuse. No wider token escape or portable-minimum claim. |
| Public imported field retains private file/friend siblings in its full expected group | Bind all required access snapshots through the selected provider. Siblings remain role 3 with no import/export/access escape. |
| File owner/friend field/initializer is exported, or subordinate stub used for ordinary call/StaticAccess/kind-2 identity | Reject exposure. Public wrapper execution does not export its internal access principal. |
| Reference artifact or folded code retains only allowed root, immutable input view, grant/origin/program dependency | Preserve its actual original context and complete closure without initializing statics or executing unneeded metadata merely for admission. |
| Replacement changes file discriminator, required origin unit, friend constructor/exact slot/wildcard position or containing access | Fail the exact retained domain/identity comparison; numeric visibility ordering and equal values do not establish compatibility. |
| Exact constants have equal canonical values but different nominal domains/dependencies; unclosed term support is missing | Compare the complete retained facts, not carrier bits/sample outputs. Unsupported open comparison is not success. |
| Body/table/map permutations, source stripping, row cloning for two using Modules and source-free round-trip | Rebuild every typed reference/site/range and retain original expectations/principals. Never derive units from new row order or optional source maps. |
| Bare access metadata or static publication sidecar, then actual ImmutableData/constant/evaluation retention | Add no invented Feature for the former; derive every using Module's existing exact closure for the latter. Provider manifests do not satisfy consumer requirements. |
| Structurally valid but false authored origin or constant/body assertion | Producer-truth violation, not a mandatory source-history proof by the reader. Optional audits identify their actual scope. |
| Correct access records with no supported StaticPlace/lifecycle/private codec | Access representation alone establishes no emission, execution, immutable publication, retry or unload support. Refuse the unsupported route. |

## Remaining approval and implementation gates

The proposed fields, identity rules, slot variants, access computation,
subordinate correspondence and origin-use association are concrete
recommendations within the static proposal's admitted type/value scope; no
access payload is left to “provider-defined” decoding.
They remain unapproved and provisional.

Adoption and exact whole-schema selection are still required. Independent
generic/type/value/ConstEval/Body support and the encoding-pending
owner-parametric constant-expression/nested constant-type carrier, complete
property/backing/setter/init representation, private schema composition,
static orchestration and native qualification remain with their current
owners. This proposal supplies no new publication/failure/retry/lifetime
choice and no claim that the current prototypes or structural tests satisfy
those gates.

Only this new proposal and its README entry are changed. Normative adoption,
registry mutation, reader/runtime implementation, commit and publication are
outside this task.
