# Generic nominal and construction metadata

**Status: UNAPPROVED physical integration proposal.** Accepted generic,
witness and construction semantics remain in their source/runtime owners.
The recommendations, row shapes, numbers and first-profile exclusions here
are candidates, not registered encodings or implementation claims.

## Recommendation

Reuse `TypeDef.genericFirst/count`, `GenericParam`, `Constraint`,
`TypeSpec` forms `0x04`/`0x05` and `TypeArg` for an explicitly bounded
type-formal profile. Retain complete implementing/declaring instances,
requirement identities and selected target substitutions in required
interface/witness records. Bind target-owned `BuilderRegistration` and
parameter-local `TextTarget` to resolved Core identities and complete
type recipes, not names or attribute blobs.

Do not persist a second construction language or a construction-specific
runtime opcode. After source binding and eager-capture planning, lower
construction to ordinary typed locals, selected calls, control flow, and
the ordinary initialization/output/ownership contracts. Retain shape records
for generated `TextTarget` content, whose module-local nominal identity cannot
be reconstructed from layout or printed names. A consumer must verify these
records and the reconstructed body; producer generation is not authority to
skip mandatory semantic verification.

This is not a request to freeze the pre-v1 draft or preserve historical
experimental decoders. Review may amend existing rows/opcodes and consolidate
shared codecs. A changed schema requires exact out-of-band agreement while
the draft header remains `(1, 0)`; a reader must not guess an edition from a
numeric collision, producer name or apparently unused field.

## Accepted owners and current gap

| Logical subject | Canonical owner |
|---|---|
| Complete type identity, qualification, formals/defaults and substitution | [Types and normalization](../specs/language/types-and-normalization.md), [grammar](../specs/grammar-spec.md), [constant domains](../specs/language/constant-generics.md) |
| Nominal conformance, inherited requirement identity, defaults and self projection | [Interface witnesses](../specs/language/interface-witnesses.md), [logical witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-encoding-pending) |
| Target authorship, builder recipes, factory/acceptance/consuming completion, TextTarget shape and eager order | [Construction handlers](../specs/language/construction-handlers.md), [logical construction records](../specs/representation/logical-records.md#builder-type-recipes-and-construction-handlers-encoding-pending) |
| Actual backing, output reservations, failure and cleanup | [Dependencies/views](../specs/language/dependencies-and-views.md), [lifetimes](../specs/lifetimes.md), [joint physical proposal](trrm-output-failure-ownership.md) |
| Optional literal parser registration and frozen values | [Literal construction](../specs/language/literal-construction.md), [proof/constant follow-up](static-proof-and-constant-evaluation.md); not BuilderRegistration |

The [registry](../specs/representation/registry.md) has generic framing but
no executable Constraint kind, interface witness or construction-registration
codec. The [metadata chapter](../specs/il-metadata.md) explicitly separates
decodable generic rows from admitted execution. Private closed-specialization
support does not fill those gaps. Reuse these structures without inventing
another type tree:

| Existing record | Recommended meaning |
|---|---|
| `TypeDef` kind and `genericFirst/count`; `GenericParam` | Existing interface kind; ordered formals owned by the exact TypeDef/MethodDef, with defaults and requirements retained |
| `TypeSpec` form `0x04` | Exact formal identity by `GenericParam` row |
| `TypeSpec` form `0x05` and `TypeArg` | Closed or symbolic constructed nominal type and ordered arguments |
| `Constraint` | Definition-site typed requirements; interface kind below is only one needed variant |
| `MethodDef`, `MethodSig`, `Param`, and existing contract rows | Requirement and selected member signatures, modes, and published contracts |

Formal identity is the owning definition and ordinal, never the formal name.
Substitution is forward from an already selected target/receiver type. It
cannot infer a target from a desired builder, rebind by spelling, or drop a
constraint after specialization. A generic MethodSig belongs to its actual
MethodDef; sharing one formal range between unrelated methods is invalid.

### Minimal extension versus shared redesign

| Approach | Consequence and recommendation |
|---|---|
| Reallocate numbers only | Makes an allocation plan coherent; fixes none of the missing type, witness, ownership or call semantics. Fixed-width IDs do not become smaller. |
| Extend existing rows plus local typed instance fields | **Recommended first profile.** Reuses normal type/definition references and call verification; the candidate records below add complete declaring/implementing/selected-owner instances where bare MethodDef/TypeDef identities were insufficient. Their larger fields carry new information, not a renumbering optimization. |
| Shared callable-instance/witness/constraint codec across generic, native and predicate records | A possible substantive redesign. It could centralize substitutions and constraints, but changes reference kinds, imports, Body maps, manifest closure and remapping across all consumers. Do not choose it solely to avoid repeated fields or historical numbers. Review its full schema and affected carriers before substituting it for this recommendation. |

A shared callable carrier must also preserve the native proposal's distinct
roles: its candidate 20-byte NativeVTable row can retain a separate ordinary
imported role-4 `expectedMethodDef?` beside the materialized local dispatch
declaration. A slot dispatch is not a call to a provider code export.
Generic witness selection must keep that expected-contract/local-target
distinction rather than flatten both into one imported MethodDef.

The minimal recommendation still needs shared **facts**. `[Copy]`, `[Sized]`,
qualification, root/cleanup obligations and fixed layout cannot be assumed
from a generic formal's name or a native ABI size. The native proposal's
recommended directly interoperable representation commitment is rootless,
cleanup-free and Copy. Reuse one reviewed definition/property commitment and
validate each closed substitution against it; do not define a competing
generic "value" bit that admits rooted Copy handles, cleanup-bearing owners
or omits the boundary's padding/validity rules. Ordinary Copy remains broader
than native interoperability. That native commitment itself remains unapproved.

The exact definition-side property carrier and Constraint variants for
`[Copy]`, `[Sized]`, write capability, associated relations and defaults are
shared blockers. The existing Constraint shape requires a nonzero TypeSpec:
zero cannot silently mean a no-type property variant. Review either an
explicit variant/schema amendment or a complete shared property-reference
codec. This proposal supplies no numeric assignment for those variants.
Constraint requirements and a provider's representation commitment are
different facts even if a common property vocabulary is selected.

## Candidate records

Coordinate this **candidate**, not reserved, allocation plan with the
[native metadata proposal](trrm-native-interop-metadata.md):
native required sections `0x0021..0x0024`, then the eleven sections
`0x0025..0x002F` below; native Features `1..5`, then `6..8` below.
Native clauses `0x0006..0x0009` and reviewed variants of the existing
20-byte Dependency `0x0004` belong to that joint review. Native TypeSpec
`0x0D` and calls `0x31/0x32` do not collide with existing constructed types
or candidate GenericCall `0x33`. Different registry namespaces must be
compared separately. No number is kept unused merely because an experiment
used it, and no byte-saving claim follows from this consecutive layout.
Reconcile the complete registry and all sibling proposals before adoption.

All candidate sections are required (`policy=0x0000`). Fixed payload sizes
exclude section indexes/directory framing. `str`, TypeSpec and typed row
references are little-endian fixed `u32`; typed rows have reserved-zero high
bytes and a nonzero 24-bit index. Only optional reference fields marked `?`
allow zero; range starts instead follow the empty `(0, 0)` rule below.
Ordinals are zero-based; row references are one-based.

| Namespace | Candidate | Proposed shape |
|---|---:|---|
| Definition kind | `0x20` | `AssociatedTypeDef` |
| Constraint kind | `0x0001` | Interface-instance requirement; complete `targetTypeSpec`, `witnessRef=0` for the open declaration. It does not encode property constraints. |
| Body opcode | `0x33` | `GenericCall`; exact direct/requirement role, MethodDef, declaring-owner instance, optional implementing instance, ordered method type arguments and ordinary arguments, as below |
| Required section | `0x0025` | `InterfaceDef`: `u32 ownerTypeDef, u32 selfGenericParam?, u32 baseFirst, u32 baseCount, u32 associatedTypeFirst, u32 associatedTypeCount` (24 bytes) |
| Required section | `0x0026` | `InterfaceBase`: `u32 ownerInterfaceDef, u32 ordinal, u32 baseTypeSpec` (12 bytes) |
| Required section | `0x0027` | `AssociatedTypeDef`: `u32 ownerInterfaceDef, str name, u32 defaultTypeSpec?` (12 bytes) |
| Required section | `0x0028` | `InterfaceWitness`: `u32 ownerTypeDef, u32 implementingTypeSpec, u32 interfaceTypeSpec, u32 memberFirst, u32 memberCount, u32 associatedFirst, u32 associatedCount` (28 bytes) |
| Required section | `0x0029` | `WitnessMember`: `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 requirementMethodDef, u32 selectedOwnerTypeSpec?, u32 selectedMethodDef` (20 bytes) |
| Required section | `0x002A` | `WitnessAssociatedType`: `u32 witnessRow, u32 declaringInterfaceTypeSpec, u32 associatedTypeDef, u32 valueTypeSpec` (16 bytes) |
| Required section | `0x002B` | `BuilderRegistration`: `u32 targetTypeDef, u32 schemaTypeSpec, u32 recipeTypeSpec` (12 bytes) |
| Required section | `0x002C` | `TextTarget`: `u32 ownerMethodDef, u32 schemaTypeSpec, u32 parameterOrdinal` (12 bytes) |
| Required section | `0x002D` | `TextContentShape`: `u32 generatedTypeDef, u32 captureFirst, u32 captureCount, u32 fragmentFirst, u32 fragmentCount` (20 bytes) |
| Required section | `0x002E` | `TextCaptureShape`: `u32 shapeRow, u32 ordinal, u32 typeSpec, u8 mode, u8 flags=0, u16 reserved=0` (16 bytes) |
| Required section | `0x002F` | `TextFragmentShape`: `u32 shapeRow, u32 ordinal, u8 kind, u8 flags=0, u16 reserved=0, u32 valueCapture?, u32 alignmentCapture?, u32 formatCapture?, u32 formatterWitness?, u32 formatterConstraint?, u32 formatterMethodDef?, u32 writerRequirementMethodDef` (40 bytes) |
| Feature identity | `6` | Type-formal generic nominal instances, admitted constraints, nominal interfaces/witnesses and compile-time specialization |
| Feature identity | `7` | Target-owned builder registrations and exact recipe substitution; depends on `6` and the actual operation contracts |
| Feature identity | `8` | TextTarget/generated shapes and verified construction lowering; depends on `6`, and on `7` only where a builder is actually used |

### Nominal and witness identity

One InterfaceDef supplements one interface-kind TypeDef; it does not create
a competing interface flag. `selfGenericParam` is absent or names its exact
first `<this T>` formal. InterfaceBase retains ordered complete instances,
not just base definitions. Reject cycles and incompatible inherited closure.

A witness is authored by the implementing TypeDef's primary authority.
`implementingTypeSpec` is that exact complete type over its own formals;
`interfaceTypeSpec` is its complete conformance instance in the same
environment. Neither is a runtime dictionary or a structural conformance.
The member key is `(declaring interface instance, requirement MethodDef)`;
the associated key analogously includes the declaring instance and exact
associated declaration. Reaching `I<A>` and `I<B>` must not merge two
same-MethodDef requirements. Repeated inheritance paths to the same complete
identity deduplicate under the accepted rules; distinct eligible bindings
remain distinct or ambiguous, not first-row-wins.

Selected-owner substitution is explicit. A selected member-owned method
requires its complete declaring type; zero selected owner is possible only
for a source-eligible top-level target. Selected generic method formals
correspond by checked ordinal after owner substitution, with their full
constraints/defaults/contracts retained. This first candidate supplies no
arbitrary method-type-argument permutation or private adaptation language.
A case needing one rejects until its explicit forwarding/projection carrier
is admitted. Selected authored/default origin, requirement labels/defaults,
receiver modes and effect/output/dependency bounds are not replaced by a
matching selected signature or body.

AssociatedTypeDef identity is origin module, exact enclosing interface and
declaration identity, not its name. Adding its DefId kind requires complete
Import/Export expected-definition, visibility, parent and reference rules.
Those rules and associated constraints/projections remain adoption gates;
the candidate discriminator alone does not make the row importable.

### Registration and generated shape

`schemaTypeSpec` identifies the resolved Core registration schema including
its selected imported definition. BuilderRegistration is target-authored;
its recipe is a complete type tree in that target's own formal environment.
Check accessibility, forward substitution, unique factory/acceptance/
completion witnesses and exact contextual result, including access and
representation. No inverse inference or source-element inspection chooses
the relationship. TextTarget selects the exact zero-based signature Param,
its eligible by-value or parameter-only implicit readonly input, and primary
authorship. It does not annotate a return, receiver or arbitrary ref input.

TextContentShape applies only to generated content TypeDefs and retains the
originating module. Capture rows keep ordered complete types and normalized
capture modes, including actual ownership/access; the mode enumeration still
requires review. Fragment kinds are candidate `0=literal segment` and
`1=formatted value`. Every hole, including rune, uses the typed formatted
role. A rune formatter can call TryWriteScalar; there is no source-graph
"rune bypass" variant.

Each formatted fragment refers to three **one-based capture rows** for its
value, effective int32 alignment and effective TextSpan format. The captures
belong to that shape; row zero is absence, never source ordinal zero.
It retains the exact formatter requirement plus either its complete witness
or the exact formal interface Constraint proving it. Exactly one of those
two references is present. Its writer requirement is the resolved
TryWriteFormatted; a literal segment has no capture/formatter references and
retains the resolved TryWrite. Preserve all ordered literal nodes, including
empty segments that make no writer call. Instance text, captured values and
zero/empty defaults are data, not shape discriminants; the ordinary generated
layout/body still supplies their checked storage.

The pair of witness/constraint references is a minimum-field recommendation,
not a claim that all open witness projections already have a codec. Its
admitted constraint kinds and closed substitution rules are prerequisites.
Shape identity includes complete types/modes, graph and selected identities;
it excludes literal contents, alignment/format values, parameter ordinal,
consumer/writer instance, source coordinates and generated names.
Equal closed shapes in one originating module have one logical nominal
identity; equal shapes in different modules do not. Producers/consumers must
normalize that identity, but no physical interning table, allocation strategy
or shared captured value is required. Packed aliases cannot invent distinct
logical generated types or merge types from different origins.

### Generic calls

GenericCall is a candidate compile-time specialization operation, not a
runtime generic ABI. Its proposed logical operands are:

1. direct or interface-requirement role and ordinary optional result;
2. exact MethodDef and complete declaring-owner instance, absent only for a
   top-level direct callable;
3. complete implementing instance for a requirement call, absent for direct;
4. ordered method type arguments, then ordinary receiver/value/place inputs.

Use existing definition references and Body-local type-map references for
these identities; review the exact role/absence/count byte grammar and width
participation with the instruction schema before assignment. A bare MethodDef
plus method type arguments is insufficient for static members of constructed
owners or differently instantiated inherited requirements. At a formal
call, the exact checked constraint closure supplies the witness; at a closed
call, the complete implementing/declaring identities select it. Neither is a
name-based implementation search. Preserve selected/default origin through
specialization and verify the substituted body before lowering.

An uninstantiated generic body is checked against its declaration's complete
constraints; success on one closed instantiation is not proof of the generic
contract. Finite demanded specialization does not imply general runtime
dictionaries, boxing, a hidden native argument or arbitrary recursive
specialization. Capability/admission rules still need a bounded termination/
resource policy and source-independent program availability.

## Construction lowering and consumer obligations

The source/checked planning stage owns eager capture and the construction
expansion. Keep its typed plan through shared traversal and verification
until ordinary operations completely express the contract. A lowered body
does not let a consumer reconstruct vanished source order or annotations.
Semantic verification checks the actual ordered body, retained registration/
shape facts and selected contracts; source-expansion qualification separately
checks that source capture and drain were lowered correctly. If a claim
requires source provenance not expressible there, retain an explicit reviewed
record or reject that claim rather than infer a hidden ConstructionGraph.

The expansion and every later transformation must preserve:

- source-order capture and transfer before factory acquisition;
- ordered drain and spread enumeration, with no preflight count, replay, or
  rollback;
- exact selected witnesses and callable identities after substitution;
- refusal versus exceptional failure, output validity, ownership transfer,
  residual cleanup, and publication on each outcome.

This recommendation does not waive missing ordinary body semantics. The
current TRRM profile does not yet encode the complete initialization-output,
failure, ownership, and cleanup composition required by these operations.
Those gates are shared with the
[output/failure/ownership proposal](trrm-output-failure-ownership.md);
this proposal does not duplicate their clauses. Required unsafe operations
reuse the accepted bounded indexed Unsafe region `0x06`; the native proposal
recommends its broader integration, not a new allocation or blanket authority.
There is no standard UnsafeTail `0x2E` or generic authorization bit here;
candidate section `0x002E` is a different namespace. Caller-unsafe,
receiver write, pinning, root/backing, cleanup and output validity remain
independent obligations. Private semantic modules require their own support
and do not establish standard TRRM conformance.

### Row, binding and rewriting obligations

- Empty ranges are `(0, 0)`; nonempty ranges are contiguous, in bounds under
  mathematical arithmetic and exactly owned. Child rows cannot be shared,
  orphaned or assigned another parent. Ordinals are dense within their owner.
  Unknown kind/mode/flag and nonzero reserved bytes reject.
- Canonical writers group children in owner/declaration order, including
  inherited normalized requirement identities, and omit empty sections.
  Keys are unique: interface per TypeDef, witness per complete implementing/
  interface relationship, member/associated keys above, builder per target/
  schema role, TextTarget per actual Param and shape per generated identity.
  Identical duplicate local rows reject rather than combine.
- Imports bind complete expected declarations against provider authority and
  contracts. Repeated imports of one identity may deduplicate physical data;
  every expectation is still checked. Distinct registrations conflict even
  when recipe names or output layouts match. A foreign module cannot author
  a conformance/registration for another primary definition.
- Preserve all direct and inherited clauses, labels/default origins,
  access/ownership, NoAlias, dependency/phase and retention contracts during
  witness matching and replacement. Optional attributes/Blob bytes cannot
  carry these required typed edges.
- Packing remaps every typed row, TypeSpec/formal, selected owner, Constraint,
  witness, range and import/export expectation. Rewrite decoded Body operands
  and rebuild maps, width prefixes, offsets and indexes; do not patch raw
  bytes. Retain/remap SourceMap instruction locations only when their meaning
  survives, otherwise discard that optional map.
- Advertise the exact rule revision and complete capability closure in
  [manifests](../specs/representation/semantic-manifests.md). A Feature row is
  not permission to execute an unsupported required form, and a producer's
  semantic indexes do not replace consumer-side type/ownership verification.

## Explicit remaining gates

This proposal neither reopens accepted source rules nor claims its candidate
rows cover all of them. Review the following gates before publication:

| Gate | Required decision or dependency |
|---|---|
| Complete first-profile constraint/property coverage | Shared definition-side value facts and exact Constraint variants/defaults, including construction's Copy/Sized and associated obligations; exact primitive-definition/member authority for primitive formatter/numeric witnesses; no erasure of unrepresented requirements |
| Callable/witness carrier | Confirm the minimal complete-instance fields, method-formal mapping, open constraint proofs and GenericCall byte grammar, or approve a coherent shared codec redesign |
| Associated identities | Complete DefId/import/export/visibility rules and constraints; associated constants, constant formals and qualified-self projections remain outside this first profile until their value/adaptation codecs exist |
| Registration and shape | Complete schema identity, authorship, mode/discriminant, generated-layout, open-to-closed witness and normalized identity rules; none follows just from the row sizes |
| Executable body closure | Ordinary output/failure/ownership/dependency/cleanup, exact sequence/spread phases, unsafe regions and complete constant/effect programs wherever required |
| Physical adoption | Coordinated registry/metadata/instruction/clause/binding/manifest amendments, exact capabilities, canonical writers, independent readers and final conformance cases |

The shared Dependency `0x0004` candidate must distinguish general source/
backing, native retention and evaluator footprint/layout variants. Its
20-byte reserved shape does not currently encode those semantics. Whether
one reviewed variant family suffices or a shared graph carrier is needed is
still open; no generic-only alias for it is recommended.

## Conformance and implementation sequence

The intended implementation sequence is to establish generic nominal and
witness/property facts first, then target registrations and TextTarget,
then ordinary construction lowering once its body prerequisites are admitted.
Use unrelated synthetic providers as well as Core roles.

Conformance must include:

- same generic base reached with different arguments versus repeated paths
  to one identity; selected defaults/associated bindings and changed labels,
  constraints or caller/effect/output obligations;
- a constructed owner's static method with its own method formals, nested
  substitutions, missing property evidence, wrong selected-owner type and
  unsupported projection/constant formals;
- same-spelled non-Core schemas, unauthorized/conflicting registrations,
  repeated imports, private helpers, recipes with the wrong exact completion
  result and element relationships that remain ambiguous;
- equal closed generated shapes with different literal/alignment/format data,
  distinct originating modules, empty literal nodes, rune holes using the
  typed hook and supplied/default effective argument captures;
- capture failure before acquisition, later-argument effects before call-side
  drain, refusal/exception at each reached operation, once-only formatting,
  unreached spread factories versus already-adopted cursors, partial owner
  unwind and exact result publication without fabricated lifetime/authority;
- independent producers/consumers without source or private module lookup,
  deliberate row/range/reference/contract corruption, compatible replacement,
  packing/remapping and mandatory re-verification before native lowering.

Existing closed internal generic-struct tests establish a bounded host
profile, not public generic exchange or standard construction support.
No test execution or proposal approval is recorded here.
