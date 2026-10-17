# Generic nominal and construction metadata

**Status: UNAPPROVED extension proposal.** This recommends an additive
standard-TRRM route for generic nominal definitions, exact interface witnesses,
and target-owned construction registrations. The candidate allocations and
record shapes are not canonical. The accepted source and runtime contracts
remain in the language specifications.

## Recommendation

Activate the existing `TypeDef.genericFirst/count`, `GenericParam`,
`Constraint`, `TypeSpec` forms `0x04`/`0x05`, and `TypeArg` rows for type-only
generic declarations. Add required interface and witness records that preserve
the selected nominal identities and member mappings. Add required
`BuilderRegistration` and `TextTarget` records that bind directly to resolved
Core schema identities and complete `TypeSpec` recipes.

Do not persist a second construction language or a construction-specific
runtime opcode. After source binding and eager-capture planning, lower
construction to ordinary typed locals, selected calls, control flow, and
existing initialization/output contracts. Retain a small shape record only
for generated `TextTarget` content, whose module-local nominal identity cannot
be reconstructed from layout or printed names. A consumer must verify these
records and the lowered body; a producer's checked result is not authority to
skip validation.

This is an incremental feature proposal. Its first profile is limited to
type-formal generics, type-associated members, and witness forms required by
the construction contracts. Constant formals, associated constants, and
qualified-self witness projections remain explicitly unsupported until their
complete value/projection records are specified. A module requiring those
semantics must reject under this profile rather than erase them.

## Current gap and reuse

The accepted contracts already define complete generic type identity,
substitution, nominal interface witnesses, target-owned builder recipes,
`TextTarget`, eager capture, refusal, cleanup, and publication. The physical
registry already has generic row shapes, but its metadata chapter says that
their row shapes do not admit executable generic contracts. It has no
interface-witness, builder-registration, or `TextTarget` records. Therefore
the proposal extends the early draft; it does not reinterpret existing
optional attributes or assign meaning to a reserved field.

Reuse these current structures without adding another type encoding:

| Existing record | Recommended meaning |
|---|---|
| `TypeDef.genericFirst/count` and `GenericParam` | Ordered type formals owned by the exact type or method definition |
| `TypeSpec` form `0x04` | Exact formal identity by `GenericParam` row |
| `TypeSpec` form `0x05` and `TypeArg` | Closed or symbolic constructed nominal type and ordered arguments |
| `Constraint` | Definition-site interface requirement over a complete `TypeSpec`; the candidate interface-constraint kind below |
| `MethodDef`, `MethodSig`, `Param`, and existing contract rows | Requirement and selected member signatures, modes, and published contracts |

Formal identity is the owning definition and ordinal, never the formal name.
Substitution is forward from an already selected target/receiver type. It
cannot infer a target from a desired builder, rebind by spelling, or drop a
constraint after specialization.

## Candidate records

The following values were checked against `trrm/1-draft-unified-3` and the
current proposal set. Native interop proposes section kinds `0x0030..0x0033`
and feature identities `0x0100..0x0104`; the candidates below do not overlap
those proposals or current registry assignments. Repeat the collision check
against the then-current registry when consolidating this proposal.

All candidate sections are required (`policy=0x0000`). References are typed,
bounded references, not semantic bytes hidden in a Blob. Candidate section
and feature numbers are review aids only; they allocate nothing.

| Namespace | Candidate | Proposed shape |
|---|---:|---|
| Definition kind | `0x20` | `AssociatedTypeDef` |
| Constraint kind | `0x0001` | Interface-instance requirement; `targetTypeSpec` identifies the complete instance and `witnessRef=0` on an open formal |
| Body opcode | `0x33` | `GenericCall`: result, exact generic `MethodDef`, a count and ordered Body type-map references, then ordinary arguments |
| Required section | `0x0034` | `InterfaceDef`: `u32 ownerTypeDef, u32 selfGenericParam?, u32 baseFirst, u32 baseCount, u32 associatedTypeFirst, u32 associatedTypeCount` (24 bytes) |
| Required section | `0x0035` | `InterfaceBase`: `u32 ownerInterfaceDef, u32 ordinal, u32 baseTypeSpec` (12 bytes) |
| Required section | `0x0036` | `AssociatedTypeDef`: `u32 ownerInterfaceDef, str name, u32 defaultTypeSpec?` (12 bytes) |
| Required section | `0x0037` | `InterfaceWitness`: `u32 ownerTypeDef, u32 interfaceTypeSpec, u32 memberFirst, u32 memberCount, u32 associatedFirst, u32 associatedCount` (24 bytes) |
| Required section | `0x0038` | `WitnessMember`: `u32 witnessRow, u32 requirementMethodDef, u32 selectedMethodDef` (12 bytes) |
| Required section | `0x0039` | `WitnessAssociatedType`: `u32 witnessRow, u32 associatedTypeDef, u32 valueTypeSpec` (12 bytes) |
| Required section | `0x003A` | `BuilderRegistration`: `u32 targetTypeDef, u32 schemaTypeSpec, u32 recipeTypeSpec` (12 bytes) |
| Required section | `0x003B` | `TextTarget`: `u32 ownerMethodDef, u32 schemaTypeSpec, u32 parameterOrdinal` (12 bytes) |
| Required section | `0x003C` | `TextContentShape`: `u32 generatedTypeDef, u32 captureFirst, u32 captureCount, u32 fragmentFirst, u32 fragmentCount` (20 bytes) |
| Required section | `0x003D` | `TextCaptureShape`: `u32 generatedTypeDef, u32 ordinal, u32 typeSpec, u8 mode, u8 flags=0, u16 reserved=0` (16 bytes) |
| Required section | `0x003E` | `TextFragmentShape`: `u32 generatedTypeDef, u32 ordinal, u8 kind, u8 flags=0, u16 reserved=0, u32 captureOrdinal?, u32 formatterWitness?, u32 formatterMethodDef?, u32 writerRequirementMethodDef?` (28 bytes) |
| Feature identity | `0x0105` | Generic nominal types, interface instances, constraints, witnesses, and generic specialization |
| Feature identity | `0x0106` | Target-owned builder registrations and exact recipe substitution |
| Feature identity | `0x0107` | `TextTarget`, generated content shape, and verified eager construction lowering |

`InterfaceDef.selfGenericParam` is zero when the interface has no self formal;
otherwise it names the exact first `<this T>` formal. `InterfaceBase` retains
each closed or open base instance in declaration order. A witness is anchored
to its implementing `TypeDef`; its `interfaceTypeSpec` is the complete
interface instance over that type's formals. `WitnessMember` maps every
inherited or direct requirement identity to one selected implementation or
default method. `WitnessAssociatedType` binds the exact associated declaration
identity to one complete type.

The `schemaTypeSpec` in `BuilderRegistration` and `TextTarget` names the
resolved Core schema type, including its selected imported definition. It is
not a spelling, an attribute argument blob, or a compiler-side name lookup.
`BuilderRegistration` is owned by the target definition; its recipe is a
complete type tree bound in that target's declaration environment.
`TextTarget.parameterOrdinal` is zero-based and binds exactly one `Param`
after checking the existing signature, eligible input mode, and primary
authorship rules.

`TextContentShape` is present only for compiler-generated text content types.
Capture rows retain ordered complete input types and modes. Fragment rows use
candidate kinds `0=literal`, `1=rune`, and `2=formatted value`; non-applicable
references are zero. The selected formatter witness and formatter method are
retained for formatted fragments, and the exact writer requirement is retained
for emitted text fragments. Literal bytes, captured values, alignment, and
format values remain instance data. Consumers compare the ordered shape, not
layout, output text, source coordinates, or generated names. The producer
interns equal closed shapes within the originating module; shape rows let a
consumer validate that identity rule.

`GenericCall` is a compile-time specialization operation, not a runtime
generic ABI. Its type arguments are Body-local type-map entries referring to
complete TypeSpecs. The consumer checks generic arity, formal kinds, all
constraints and witness requirements, then specializes the selected body
before native lowering. Calls to an interface requirement retain that exact
requirement `MethodDef`; the receiver's complete type or checked formal
constraint selects its witness. Specialization rewrites it to the selected
method. No boxed interface value, runtime dictionary, hidden foreign argument,
or implicit conversion is introduced.

## Construction lowering and consumer obligations

Lowering is preferred over a persisted `ConstructionGraph` or new
construction opcode because the logical operation already has selected,
typed identities before execution. The lowered body must preserve:

- source-order capture and transfer before factory acquisition;
- ordered drain and spread enumeration, with no preflight count, replay, or
  rollback;
- exact selected witnesses and callable identities after substitution;
- refusal versus exceptional failure, output validity, ownership transfer,
  residual cleanup, and publication on each outcome.

This recommendation does not waive missing ordinary body semantics. The
current TRRM profile does not yet encode the complete initialization-output,
failure, ownership, and cleanup composition required by these operations.
Those existing gates must be completed; a builder-only opcode or optional
metadata row must not be used to bypass them. Private semantic modules need
their own reviewed producer/consumer support and do not establish standard
TRRM conformance.

Before use, an independent consumer validates row ownership, ranges,
uniqueness, references, schema identity, target authorship, recipe formation,
generic substitution, conformance closure, selected method contracts, and
generated content shape. It then runs mandatory semantic verification on the
reconstructed body, including capture order, effects, initialization,
ownership, backing, cleanup, and conditional publication. Conflicting
registrations reject; repeated imports deduplicate only the same record
identity. Unknown required semantics reject rather than being ignored.

## Explicit remaining gates

This proposal does not approve new source spellings or change the accepted
logical contracts. In particular:

- associated constants and constant-generic formals need typed value/domain
  records and remain unsupported by this candidate profile;
- qualified-self witness projections need their own complete retained
  adaptation records and are not inferred from a matching signature;
- collection spread execution still needs exact sequence, item-transfer,
  invalidation, and cleanup witnesses in the lowered body;
- ordinary output, failure, ownership, and cleanup metadata must be available
  before construction bodies can claim executable standard support;
- numeric candidates must be rechecked and registered with the generic,
  output/failure/ownership, manifest, reader-trust, and conformance contracts
  together after consolidated review.

The intended implementation sequence is to establish generic nominal and
witness facts first, then bind target registrations and `TextTarget`, lower a
small unrelated synthetic handler end to end, and finally exercise
independent producer/consumer paths. Passing syntax tests or a private
round-trip is not evidence of standard construction support.
