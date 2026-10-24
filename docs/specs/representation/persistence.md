# Interchange and private persistence

## 1. Status and scope

These rules define logical `persistence-taxonomy/1`. They distinguish a
standard serialized interchange contract from an implementation's saved
state. They assign no opcode, Feature ID, container version, target profile
or compatibility mapping.

The `.trrm` metadata/Body design is Terrum's normative serialized semantic
interchange. A conformance claim still names the exact schema, supported
features and required rules under [requirements and admission](requirements-and-admission.md).
It does not claim that every source-language feature already has a complete
physical encoding. Unassigned or unsupported required semantics remain
unusable, not private extensions to an unchanged standard schema.

The current development `.trm`, native package `.trmn`, native set `.tnset`
and native graph `.trng` serializations are **private persistence**. Their
extensions, tags, integer/string encodings, checksums, container revisions,
stored IR and implementation-profile labels are not Terrum-standard byte
contracts. An implementation may document or exchange them under an explicit
implementation-specific agreement; that is not independent Terrum
producer/reader conformance.

| Artifact surface | Standardized obligation | Serialization status |
|---|---|---|
| `.trrm` metadata and Bodies | Exact selected semantic schema, published contracts, required admission, supported records and behavior | Normative Terrum semantic interchange |
| `.trm` development structured IR | Preserve any source/runtime contracts used by an admitted implementation | Private implementation persistence, distinct from `.trrm` |
| `.trmn` native package | Satisfy the selected native ABI, binding, ownership, runtime and deployment contracts | Private package serialization |
| `.tnset` native set | Satisfy the selected multi-provider deployment and call contracts | Private set serialization |
| `.trng` native graph | Satisfy the selected dependency, origin, cleanup, code-lifetime and call contracts | Private graph serialization |
| Native PE/ELF image | Satisfy the selected target's image format, ABI and runtime requirements | Target/platform format, not another Terrum semantic byte schema |
| CLI machine response | Satisfy its separately selected protocol schema, diagnostics and status contract | Tooling protocol, not artifact serialization |

The taxonomy selects no new native deployment combination or cross-image
value mode. A documented development example does not admit managed handles,
views, owning values or failure transport outside the selected complete
target/runtime profile. Conversely, private serialization does not excuse
a violation of an already admitted source, ABI or runtime contract.

## 2. Identification and admission

A filename extension is a convention, not a decoder selector or evidence of
validity. Equal magic bytes do not establish equal schemas: the development
`.trm` and semantic `.trrm` paths can both use `TRRM` while retaining distinct
record and interpretation contracts. Relabeling either file does not convert
its bytes, semantics or conformance status.

The consumer selects and supports the actual schema before interpreting its
records. How an implementation identifies its private schema is outside this
standard; guessing from a familiar extension, shared signature, record shape
or native-profile number is not standard admission. A private container version
does not select the encoding of an embedded semantic payload, a governing
rule edition, a target ABI or a runtime policy.

The current normative draft still requires exact out-of-band agreement under
[format identity](../il-metadata.md#format-identity-and-revision-scope).
Matching `(1, 0)` or the registry edition alone is insufficient. This taxonomy
does not add a physical schema/feature manifest or fill its reserved fields.
Governing semantic requirements remain Module-owned; schema selection remains
artifact-wide, and numeric revision ordering is not compatibility.

An independently interpreted embedded `.trrm` payload retains its own exact
schema and complete requirements. A private outer container does not turn that
payload into an unversioned blob or make the entire container standardized.
An implementation-private representation with no independent semantic-reader
claim need not adopt `.trrm` as its internal serialization.

Producer responsibility is distinct from schema identity and execution support.
A checksum, digest, signature or successful platform load does not prove
semantic compatibility or arbitrary body/code truth. The
[consumer-verification contract](reader-trust.md) requires structural and
safe-consumption checks before affected use while preserving producer truth
obligations and the external-provider/authored-unsafe trust boundaries.
Consumers may rely on structurally consumable producer facts without
semantic re-proof. Unsupported requirements,
incompatible bindings and unavailable target/runtime support reject affected
use and do not establish a support claim.

## 3. Native and implementation boundaries

[Module linking](../modules.md), [native sets](../module-sets.md),
[native graphs](../native-graph.md) and [native execution](../native.md)
separate deployment behavior from package bytes. Public identities, expected
contracts, layout commitments, call classification, output state, ownership,
roots, cleanup providers and code lifetime are determined by their canonical
source/runtime/target contracts, not a private serializer's current fields.

Private storage may change or be regenerated where an implementation controls
the affected boundary. It must not silently reinterpret retained public
contracts, fabricate successful outputs, drop a retained dependency or invoke
an incompatible image. An independently replaceable native boundary retains
its ABI and lifetime obligations even when both images arrived in private
packages. A matching loader/export name is not sufficient compatibility.

For native composition, validate, adapt, recompile or reject only according to
the selected explicit boundary contract. Source rebinding, implicit allocation,
boxing, JIT fallback, live patching and hot unload are not supplied by private
persistence. A load-time lowering path and an unchanged precompiled image are
different consumption modes; support for one does not establish the other.

Source-free execution of a private persisted program can be a legitimate
implementation facility. It does not prove that an independent implementation
can produce or consume those bytes. Likewise, moving a development support
ledger out of the specification neither removes adopted runtime semantics nor
promotes its incomplete profiles to general conformance.

## 4. Tooling, resources and documentation ownership

The [CLI machine protocol](../cli-protocol.md) is a separate conformance object.
It can inspect, compile, link or run private artifacts while reporting their
actual kind, implementation schema and operation outcome. JSON schema
conformance, raw program status and artifact validity remain distinct. An
inspection response cannot label a private artifact as standard `.trrm`
interchange because it was successfully parsed.

An inspector may honestly report an unrecognized or unsupported artifact.
An execution or binding operation cannot replace declined admission with an
empty program, success-shaped value or guessed interpretation. A private
payload does not authorize unknown required semantic forms in a normative
artifact; optional nonsemantic sections cannot carry those forms.

Implementation limits on private artifacts are documented as such. They are
not the [portable interchange capacities](resource-accounting.md), whose
numerical minima remain deliberately unassigned. Processing an actual
normative payload retains its selected accounting, support and trust scope;
an outer private wrapper cannot reset that payload's applicable root budget.

Private wire revisions, serializer limits, implemented native-profile ranges
and command transcripts belong in the [implementation references](../../implementation.md),
not the normative language/runtime chapters. Those references may be precise
without promising a Terrum-standard private grammar or historical reader.
No unpublished alpha persistence reader, old-format migration or stable
private extension is required.

Making a private serialization normative later requires an explicit adoption:
one schema identity, complete public framing/records/reference spaces,
required semantics and support gates, transformations, resource/trust scope
and independent positive/negative vectors. A new filename, current version
number or permissive reader does not perform that adoption.
