# Producer obligations, consumer verification and schema-owned references

## 1. Valid artifacts and producer obligations

The representation chapters define valid artifacts: their schema, record
relationships, declared requirements and semantic meaning. A producer must
emit metadata that satisfies these rules and whose published semantic facts
are true. Source compilation checks authored source against the language
contract; a merger or rewriter is responsible for correctness of the data it
changes or emits.

Before affected use, consumers must perform the structural checks needed to
safely decode and consume selected input: exact schema and framing, bounded
extents and counts, assigned record shapes, schema-owned reference ranges,
required-feature support, and binding compatibility checks needed for the
selected use. These checks prevent unsafe structural consumption; they do
not prove that a structurally consumable Body or published contract is
semantically true. Consumers may rely on producer-published facts without
re-proving them. A producer that publishes false facts violates its
obligation, but a consumer is not required to detect or reject a structurally
valid artifact solely because such a fact is semantically false; execution
that relies on false metadata is not guaranteed. A consumer must refuse
affected use when the input cannot be safely decoded, a required schema or
feature is unsupported, or a required binding is incompatible.
The specification does not prescribe diagnostic wording or the internal
organization of these checks.

Verification cannot prove arbitrary external provider behavior or authored
unsafe promises. Such declarations remain trust contracts: the consumer
checks their structural representation and applicable caller obligations,
but does not claim to prove their truth, foreign code, hardware behavior or
other facts outside the verified artifact. A signature, checksum, producer
name or self-declared trust bit does not establish semantic truth.

## 2. Verification organization

Structural safe-consumption checks are mandatory, but this chapter does not
prescribe whether they are performed during decoding, binding,
reconstruction, lowering or a dedicated pass. They must complete before
affected use. Structurally malformed, unsafe-to-consume or unsupported
required input cannot be used. Semantic truth is a producer obligation, not
a mandatory consumer proof; an implementation may offer a separate audit.
An implementation that does not support a required schema or semantic
feature cannot claim support for it and must reject affected use.

## 3. Producer facts and optional audits

Producer obligations apply to complete artifacts even when a consumer uses
only selected records. A producer is responsible for the truth of indexed
locations, definition/use facts, liveness, effects, ownership and call-site
authorization it publishes. Consumers validate the selected structures,
extents, references and supported forms needed for safe consumption, but do
not have to re-establish those semantic facts against the emitted program or
reconstruct source text.

An implementation may additionally provide a deeper audit of facts outside
the mandatory verification scope. Such an audit may claim only the scope and
facts it actually examines; it does not prove arbitrary external behavior or
replace verification of the selected artifact.

## 4. Schema-owned reference namespaces

For a valid artifact, the field or registered enclosing form determines the
reference namespace. Equal numeric row indexes in different tables do not
identify the same declaration. These rules define valid encodings; mandatory
structural checks reject out-of-range, wrong-namespace or otherwise
unsupported references before affected use.

| Field or form | Schema-owned target |
|---|---|
| `MethodDef.signature`, `bodyRow`, `ownerTypeDef` | MethodSig, Body and TypeDef respectively |
| `Import.expectedLocalDefId` | TypeDef, FieldDef, MethodDef or PropertyDef; FieldDef expectations are checked stubs in their expected owner's range |
| `Export.localDefId` | Public local TypeDef, FieldDef, MethodDef or PropertyDef; FieldDef owner must also be public |
| TypeSpec nominal/import/formal operands | TypeDef, Import and GenericParam respectively |
| Body TypeMap word | Body-specific token kind; TypeSpec rows use kind `11`, not TypeSpecRef row form `00` |
| Body FieldMap word | FieldDef DefId `0x14`, FieldDef Import `0x1C`, or FieldPath row token `0x1D`; the token kind fixes its table and expected target |
| FieldPath row | Section `0x004B`; `rootTypeSpecRef` is a complete TypeSpecRef and each ordered step is a FieldDef DefId or FieldDef Import |
| `TargetLayoutDependency.fieldFirst/fieldCount` | Exclusive ordered range in `TargetLayoutDependencyField` section `0x004A`, not a Body FieldMap or FieldPath row |
| `fieldDefRow`, Body MethodMap entry | FieldDef; MethodMap resolves an exact MethodDef or Import token, with no direct-row escape |
| Effects/OutputState `ownerRow` | MethodDef, selected by clause kind |
| Cleanup `ownerRow`, `cleanupMethodDef` | TypeDef and MethodDef respectively |
| Reference-slot selector 1 | Body; its section-kind field is reserved zero |

Current metadata row indexes occupy a little-endian `u32`, with row
`1..0xFF_FFFF` and zero only where the selected field permits it; the high
byte is reserved zero. A genuinely polymorphic definition field, such as
Export, Import's expected definition or GenericParam's TypeDef/MethodDef
owner, uses a DefId whose high byte selects one of that field's permitted
definition kinds and whose low 24 bits contain the one-based row.

Names, layouts, equal numeric row IDs and coincident payload bytes do not
change a reference's namespace. A meaning-preserving rewriter maps decoded
reference fields according to their schema-owned table and context, not by
substituting matching octets.

For stream Bodies, consumers derive the prefix size only from the selected
schema's `mapMask`: `17 + 4*popcount(mapMask)`. They check reserved bits,
count words for set bits, map payload extents and the fixed payload order
before affected use. A plausible body length, Feature 13 or byte-2 value
does not select a schema or supply omitted count fields. FieldMap references
validate their exact token kind/row; a FieldPath row validates its variable
extent, nonzero field count, complete root type and each ordered
FieldDef/Import reference. Consumers need not semantically re-prove the
producer's field accessibility or published layout facts.
`BlockEntryState.carrierKind=2` requires its unused `typeMapIndex` and
`accessKind` fields to be zero, and support must admit TypeIdentity's
restricted producer, alias, merge, comparison and conditional-selection
flow before use. Exact open-generic identity evidence is required where the
selected execution model needs it; the entry row alone cannot manufacture
that evidence or prove a source refinement.

## 5. Transformations, verification reuse and audit claims

A rewriter safely decodes its selected input before affected use and is
responsible for the correctness of transformed data and any new facts it
publishes, including affected references, indexes, requirements and
ownership relationships. Its output must satisfy the selected schema and
producer obligations. Cached structural/admission checks may be reused only
for an unchanged artifact under the same exact schema, binding, target and
rule context. Removing code does not itself discharge remaining type,
layout, constant, witness, cleanup or load dependencies.

Rewriting a stream Body reconstructs its maps and query operations, remaps
FieldMap tokens and any referenced FieldPath row's root/ordered steps, and
rebuilds the `mapMask`, conditional count words, payload extents, instruction
indexes and containing section extents. A closed TypeIdentity fold may
replace the comparison with ordinary bool/uint8 constants or control flow
only while preserving exact identity-binding, substitution and refinement
obligations. Discarding debug data, deleting a FieldPath row or replacing an
Import with a direct FieldDef reference cannot silently drop a surviving
layout, binding or provider obligation.

Any claimed deeper semantic audit is optional and limited to the bytes,
schema, context, rules, targets and bindings actually examined. This
representation assigns no Feature ID, physical trust flag, opcode,
unassigned variant or independent execution profile for verification.
