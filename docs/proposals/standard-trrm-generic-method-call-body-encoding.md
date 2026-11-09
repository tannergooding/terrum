# Standard TRRM GenericCall Body encoding

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user's
> authorized missing-contract workflow.** This document changes no normative
> assignment, implementation or support inventory. It selects a Body carrier
> for the existing semantic generic-call operation, not approval of adjacent
> generic, witness, constant-execution or runtime representations.

## Decision and scope

Recommend **one Body `GenericCallTarget` record, candidate kind `10`, reached
through candidate `CallSite.targetKind=4`**. Keep the existing CallSite payload,
four-byte MethodMap entries and assigned short `90` / long `E0` instructions.
Use **`call.generic`** as the operation's display name for this target form.
Direct and interface-requirement targets are roles of that one operation,
not separate opcodes.

The target record contains the exact MethodMap declaration, explicit
declaring-owner instance, existing singular dispatch-evidence tag/reference
and a complete formal-order vector of tagged method arguments. Type arguments
use complete TypeSpecRefs; constant arguments use ConstantSource rows or an
explicit reference to an in-scope constant formal. The latter is a **new
Body-argument form**, not a new Standard ConstantSource tag.

The singular evidence selects **which interface requirement dispatch** the
call uses. It is not evidence for every generic argument. Bind every owner
and method constraint into a separate, plural, typed obligation environment
using the checked reconstruction rule below. Direct calls retain **no
encoded evidence**. If the required environment cannot be reconstructed
from supported published records, reject affected use; do not make one
witness stand for several obligations.

Select one exact candidate schema, G1, for independently exchanged public/open
generic MethodDefs and their calls. Its initial executable route is source-free
closed-instance specialization, not a standardized shared-generic dictionary,
open machine entry point or generic-class ABI. Open definitions remain checked
over their declared domains; successfully compiling one closed instance does
not validate an otherwise invalid template.

This decision does not add source syntax, inference, an overload rule,
constant arithmetic, interface-value storage, generic capture, existential
opening, qualification projection, ownership authority or a native ABI.
Existing unsupported type/value/contract dependencies remain unsupported.

## Baseline and normative owners

The [specification index](../specs/README.md) leads to these owners. The
proposed amendments intentionally complete their unassigned GenericCall
carrier; they do not infer one from existing generic declaration rows.

| Owner | Existing contract and intended amendment |
|---|---|
| [Logical generic records](../specs/representation/logical-records.md#generic-domains-and-specialization-branches-encoding-pending) | Preserve one operation, roles `1=direct`, `2=interface requirement`, evidence kinds `0=none`, `1=Constraint`, `2=InterfaceWitness`, `3=WitnessProjection`, direct calls without evidence, owner-first substitution and ordinal method-formal mapping. Replace only the unassigned display/opcode/byte-grammar boundary with this carrier and distinguish dispatch evidence from reconstructed argument obligations. |
| [Metadata rows and ownership](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Preserve MethodSig's 24-byte row, GenericParam's 32-byte row, Constraint's 12-byte row, Import/Export and all formal/default/range rules. Add the target/argument reference rules and the complete generic-definition publication obligation below. |
| [Body framing](../specs/il-metadata.md#body-table-and-coded-instruction-boundary), [typed records](../specs/il-instruction-set.md#typed-operand-records), [calls](../specs/il-instruction-set.md#calls) | Add target kind 4 and record 10. Preserve map widths, codec `01`, instruction lengths, ordinary selectors, preparations, phases and result/termination rules. |
| [Machine registry](../specs/representation/trrm-1.registry.json), [allocation chapter](../specs/representation/registry.md) | Add exactly the proposed record, target/argument domains, reference shapes, display relationship and Feature registration. No new primary, control, section, token or DefId kind. |
| [Complete types](../specs/language/types-and-normalization.md), [constant generics](../specs/language/constant-generics.md), [calls/inference](../specs/language/calls-and-inference.md), [witnesses](../specs/language/interface-witnesses.md) | Preserve complete identities, kinds/domains/defaults, source selection, full-domain checking, exact inherited requirement identities and selected target/default contracts. No source amendment. |
| [Binding/replacement](../specs/representation/binding-and-replacement.md), [reader trust](../specs/representation/reader-trust.md), [canonicality](../specs/representation/encoding-canonicality.md) | Add the schema-owned edges, complete instance/obligation comparison and source-free remapping rules below. Preserve producer truth, mandatory safe consumption and separate optional audits. |
| [Versioning](../specs/representation/format-versioning.md), [manifests](../specs/representation/semantic-manifests.md), [admission](../specs/representation/requirements-and-admission.md) | Identify the exact G1 draft and its coherent generic-method capability. Preserve `(1,0)`, out-of-band exact agreement, independent dependency selections and each Module's complete retained closure. |
| [Binding conformance](../specs/conformance/il-binding-and-replacement.md), especially IBGR-19/22/25/26/32, and [registry cases](../specs/conformance/il-registry.md) | Preserve the settled cases and add independent complete-artifact cases specified here. |

MethodSig's `genericFirst/genericCount` already names **MethodDef-owned**
GenericParams, not MethodSig-owned parameters. A nonempty generic range
therefore prevents sharing that MethodSig between distinct MethodDefs.
TypeDef-owned and MethodDef-owned ordinal zero are distinct formal identities.
Every formal's contiguous Constraint range is exclusively owned by it.

Already assigned formal kinds are `1=type`, `2=constant`; domain tags and
defaults are not new choices here. Type formals retain ownership domains
`1=default non-unique Copy-capable`, `2=unique-only`, `3=default plus unique`
and nullability `1=non-null`, `2=nullable allowed`. Constant formals retain
zero domain tags, a required constant-domain TypeSpec and no Constraint rows.
Type defaults are TypeSpecRefs; constant defaults are ConstantSource rows.
Constraint kinds remain `1=interface instance`, `2=intrinsic property`,
`3=associated relation`; property codes remain `1=Unique`, `2=Write`,
`3=Blittable`. There is no Copy/Sized/Pin property or PropertyWitness.

## Allocation review and alternatives

Numbers in Body/opcode/section columns are hexadecimal; Feature IDs are decimal.
The [complete machine registry](../specs/representation/trrm-1.registry.json)
assigns tiny `00..0F`, short forms within `10..BF`, long families
`C0..CC`, `D0..DE`, `E0..E8`, `EA..EB`, and typed records `01..0B`.
`B6/EA` are recognized RegionExit shapes without usable region domains.
Unlisted primary/control values are invalid, not extensions or escapes.
The first byte alone fixes length; no prefix or wider-index mode exists.

The proposal review also accounts for the separately unapproved
[DF/0C output reservation](aggregate-native-outputs.md),
[E9/0D address formation](standard-native-address-formation.md),
[EC/0E storage address](standard-native-storage-address-formation.md) and
[0F exception directory](standard-trrm-register-body-exception-regions.md).
[RootPin](trrm-root-pin-schema-selection.md) separately uses record 0D;
its future composition conflict is not solved or imported here. Record **10**
is unassigned in the accepted registry and these candidates. Choosing it
avoids unrelated family redesign, not an obligation to preserve draft bytes.
Record 10 is unrelated to primary `10=Move` or section `0010=ModuleRef`.

Accepted semantic sections run through `004B`, plus `7F00` SourceMap.
Companion candidates occupy `004C..005E`; none is needed here. DefId kinds,
complete-type/token discriminators, Contract clauses and SourceMap roles
remain unchanged. Assigned Features are `1..7`, `9..13`; companion candidates
use `8`, `14..22`, including `22/1` for
[raw-Union coverage](standard-trrm-raw-union-initialization.md).
Select **candidate `23/1=GenericMethodCalls`**, not the obsolete generic
Feature 6-8 candidates: those numbers conflict with current native assignments.
Neither record 10 nor Feature 23 is assigned by writing this proposal.

| Alternative | Assessment |
|---|---|
| Amend CallSite 02 with inline owner/argument/evidence fields | Coherent in a new exact schema, including an explicit target-kind-directed tail. It avoids one target record but couples the reusable bound target to every invocation's inputs/results/preparation and changes CallSite's extent rules. Not rejected for draft compatibility; not selected for this independent target fact. |
| Enlarge/reinterpret MethodMap entries as constructed method descriptors | Can unify declaration and constructed-target lookup, but replaces the fixed four-byte map and Body extent arithmetic, or needs another explicitly tagged global descriptor/token family. All ordinary map users would need revised rules. MethodMap alone still cannot supply missing owner/argument/evidence information. |
| **CallSite kind 4 plus one GenericCallTarget with inline tagged argument rows** | **Selected.** Separates immutable binding from evaluated invocation state, leaves ordinary map/CallSite framing intact and permits target sharing. Costs `20 + 8*A` target bytes and its offset-index entry; no measured size or speed advantage is claimed. |
| Separate target, argument and evidence records/ranges | Coherent but adds Body record kinds, independent indexes/counts and sharing/ownership rules for small positional lists. Inline tagged argument rows suffice. A second serialized evidence list is unnecessary only because the complete reconstruction rule below is mandatory. |
| New GenericCall primary, or nonzero E0 control | Would need explicit allocation and complete operands but provides no missing invocation semantics. Assigned `90`/`E0` already invoke a typed CallSite; select neither a new primary nor a control variant. Compact window instructions cannot carry this target form. |

## Exact schema and capability

Candidate out-of-band identity:
**`trrm/1-draft-generic-method-calls-1`**.
G1 means exactly the accepted **`trrm/1-draft-layout-queries-1`** schema,
the complete **SF1** delta in
[scalar frozen-value registration](scalar-frozen-value-registration.md),
and this document's generic-call delta. SF1's entire 24-scalar codec and
its proposed `11/1` registration with prerequisites `[]` are selected;
this is not another integer-only value codec. Constant-generic admission
is nevertheless restricted to the narrower domains below.

No F/S/array, exception-region, RootPin, enum/wrapper-value, associated-member,
general-predicate, static-storage, intrinsic or constant-request successor
is composed. Linked alternatives do not join G1 transitively. Feature 9,
10 and 12 registrations remain incomplete; retain `10 -> 11` and
`12 -> {10,11}`. ConstantSource tags 2 and 3 cannot obtain execution support
from this selection.

Keep format `(1,0)`, governing edition `1`, `implementationKind=01`,
`instructionEncoding=01`, map-mask bits/count order and
`17 + 4*popcount(mapMask)` prefix. There is no on-wire G1 discriminator.
Equal version bytes, a Feature list or a plausible target record cannot
select G1. Earlier draft generic encodings, private generic-constant tags
and retired assignments are not compatibility aliases. Current nongeneric
call forms keep their defined meaning; their bytes cannot stand for omitted
generic arguments, owner substitution or evidence.

**Candidate Feature `23`, revision `1`, governing editions `[1]`,
unconditional prerequisites `[]`:** retained open generic-method declarations,
their exact instantiation/binding contracts and this call carrier, including
source-free reconstruction, remapping and closed-instance lowering.
Its representation is the existing declaration/import/Body graph plus the
new record and argument forms. It selects no shared execution ABI.
Witness-dependent uses require complete supported witness records under
their own gates; 23 does not register missing witness/projection codecs.

Each Module requires 23/1 when it retains a MethodDef with method formals
(including an expected import or declaration-only view), a GenericCallTarget,
or a forwarded constant-formal argument. Owner-only instantiation through
this target also requires it. SF1 values/defaults additionally require 11/1
in every using Module; a constant-formal declaration or symbolic forwarding
alone does not. Other retained operations/contracts keep their own complete
requirements. In particular, 23 does not imply native interface Feature 5,
layout-query Feature 13, or a foreign-call ABI.

Requirements include unused retained declarations, defaults, constraints and
Bodies, not only reachable machine calls. Dependencies retain their own exact
schemas and manifests; packing selects one destination schema that represents
all surviving obligations. Unsupported prerequisites reject before affected
use even when this record's numeric fields can be inspected.

## Candidate wire contract

All multibyte fields are little-endian. `R(T)` below is a required one-based
`u32` reference to table T, high byte zero, bounded by the table's actual
count in `1..00FF_FFFF`. It is not a DefId, Import token, byte offset or
cross-artifact row number. Complete TypeSpecRefs retain the existing union:
TypeSpec row `00_000001..00_FFFFFF`, assigned `F2_0000_cc`, or eligible
`F3_qq_ff_pp`. Body TypeMap tokens `11/13/1C/F0/F1` are not TypeSpecRefs.

### CallSite and instruction selection

Add **`4=GenericCallTarget`** to CallSite's target-kind domain.
For kind 4, its existing `targetIndex:u16` selects record kind 10 in the
**same Body**. Kinds 1/2/3 keep MethodMap/Register/Home meanings.
This tag is distinct from the target record's direct/requirement role.

CallSite remains exactly:

```text
u8 recordKind=02, u8 flags=0, u16 reserved=0
u8 targetKind, u8 resultKind, u16 flags=0
u16 targetIndex, u16 resultIndex
u32 inputCount, u32 preparationCount
inputCount * { u8 selectorKind, u8 sourceUse, u16 selectorIndex }
preparationCount * { u16 destinationPreparationRecordIndex }
```

Its extent is exactly `20 + 4*inputCount + 2*preparationCount`.
No generic tail, padding, hidden receiver or extra result appears here.
Invoke it with `90 callSiteIndex:u8` for indexes 0..255, or
`E0 00 callSiteIndex:u16` for all Body record indexes. Canonical writers
choose 90 when possible; ordinary readers accept a valid longer E0.
The display `call.generic` identifies a CallSite whose target kind is 4;
it is not a second numeric interpretation of either primary.

### GenericCallTarget

| Offset | Bytes | Field | Required meaning |
|---:|---:|---|---|
| 0 | 1 | `u8 recordKind=10` | Candidate Body record kind. |
| 1 | 1 | `u8 flags=0` | No inference, trust, specialization or ABI flags. |
| 2 | 2 | `u16 reserved=0` | Required zero. |
| 4 | 1 | `u8 targetRole` | Exactly `1=direct` or `2=interface requirement`. |
| 5 | 1 | `u8 evidenceKind` | Existing logical domain `0..3`, with the support restrictions below. |
| 6 | 2 | `u16 methodMapIndex` | Exact declaration/requirement in this Body's MethodMap. Zero and FFFF are ordinary indexes. |
| 8 | 4 | `u32 declaringOwnerTypeSpec` | Complete owner instance; zero iff the selected MethodDef has no declaring TypeDef. |
| 12 | 4 | `u32 evidenceRef` | Zero for kind 0, otherwise the kind-selected global row reference. |
| 16 | 4 | `u32 argumentCount` | Exactly the selected MethodSig's method-formal count, including unused/defaulted formals. |
| 20 | `8*A` | Argument rows | Exactly A rows below, in zero-based method-formal ordinal order. |

Exact extent: **`20 + 8*A`**, with checked multiplication/addition and no
trailer, per-argument length or alignment padding. The ordinary typed-record
offset index supplies the containing extent. The record contains no reference
back to its CallSite and cannot execute on its own.

MethodMap still contains only `16_rrrrrr` MethodDef or `1C_rrrrrr` Import
tokens, with nonzero bounded rows. An Import must expect a MethodDef and
resolve under the Body's owning Module. A direct reference to an expected
stub cannot bypass that stub's retained Import binding. A signature anchor,
field, name, native address or bare row word is not a target.

For a member, the owner field identifies the exact normalized nominal
declaring instance, including all ordered owner arguments. It is not the
receiver's runtime type or an inferred owner. Resolve its declaration to
the selected MethodDef's exact owner, including through Import proxies.
An owner with formals requires an explicit constructed TypeSpec form 05
with every owner argument; a bare generic definition is not an implicit
identity substitution. A nongeneric owner uses its nominal form 02/03.
The owner's outer qualifier byte is zero: this field selects the declaration
instance, while qualifications inside its arguments remain exact.
For an inherited requirement it identifies the **actual declaring interface
instance**, not merely the most-derived requested interface. Receiver
qualification remains in the substituted signature. Module-level calls
require zero even if an argument happens to have a nominal type.

Use this target when method arguments, declaring-owner instantiation or
requirement evidence are needed. Do not encode a plain nongeneric direct
target with no such information here. Conversely, an open MethodDef cannot
be called through an ordinary MethodMap/window target by assuming current
formals, erased types or a pre-existing specialization. Compact descriptors
remain subject to their existing exact eligibility; identity-forwarder 05
does not acquire an implicit method-argument mapping from this proposal.

### Tagged method arguments

Each row is `u8 argumentKind, u8 flags=0, u16 reserved=0, u32 argumentRef`.
There is no encoded ordinal: position is the selected formal ordinal.

| Argument kind | Reference namespace | Matching formal and meaning |
|---:|---|---|
| `1=TypeActual` | Required complete TypeSpecRef | `formalKind=1`. Includes an in-scope type formal through TypeSpec form 04, nested complete types and eligible inline F2/F3 forms. |
| `2=ConstantActual` | R(ConstantSource), section `0030` | `formalKind=2`. G1 admits tag-1 SF1 values whose complete domain is exactly the formal's substituted domain. |
| `3=ConstantFormal` | R(GenericParam), section `0019` | `formalKind=2`. The referenced row is an in-scope **caller** constant formal with the exact substituted domain; forward its checked symbolic value and dependencies. |

Kind 3 is deliberately not GenericParam.formalKind 3, ConstantSource tag 3
(TargetValueBinding), or evidence kind 3 (WitnessProjection). In particular,
`1900_0001` is not R(GenericParam). Zero/other argument kinds, zero required
references, nonzero flags/reserved and kind/formal mismatches reject.
Type actuals never use a bare GenericParam reference; constant actuals never
use a TypeSpecRef or inline integer in the argument word.

The G1 constant-domain subset is the ten accepted fixed-width signed/unsigned
integer types and Boolean. Preserve exact type and mathematical/logical value,
including 128-bit endpoints; no carrier conversion, wrapping or host-width
inference. SF1's other scalar values remain available in their own admitted
positions but are not constant-generic domains. Accepted enum/Transparent
wrapper domains require their independent complete codecs and are not enabled.
ConstantSource tags 2/3 remain unsupported here, not retries as tag 1.

Kind 3 adds no general constant-expression or constant-load instruction.
It cannot be inserted into GenericParam.defaultArgumentRef or TypeSpec
form 05's ConstantSource-only constant slot. Symbolic constant defaults and
nested type constructions needing an open constant term still require a
separate Standard carrier; G1 rejects those uses rather than inventing
ConstantSource tag 4 or importing the
[private witness-persistence tag](source-interface-witness-private-persistence.md).
Nested TypeSpec constructions containing already represented closed constant
sources remain representable under their own formation contracts.

### Widths, ownership and canonical order

Record/map/Home/Block references are zero-based `u16`, including FFFF;
their counts are `u32` with maximum 65,536. Global formal/evidence/type/value
references retain their own 24-bit row domains in four-byte words.
`argumentCount` is not a Body-local index: 65,536 is not its maximum.
It is bounded by the exact GenericParam range and containing checked Body
extent. Ordinary formal/preparation ordinal fields retain their existing
widths; an unrepresentable required ordinal is not truncated.

Targets and inline arguments belong to one Body and its unique MethodDef
context. No argument row can be independently shared or referenced.
Several CallSites may share a target record only when declaration,
owner, arguments, dispatch evidence and using context all agree. They
still have independent staged values, destination state and preparations.
There are no raw inter-Body or inter-artifact local indexes.

Canonical writers retain the existing first-logical-use traversal. At a
kind-4 CallSite, visit the CallSite, its target, then its preparation
records in encoded order; inside the target visit MethodMap declaration,
owner, dispatch evidence and arguments in formal order. Reuse identical
targets, omit unused records/maps and rebuild offsets. Distinct Import
expectations do not merge merely because they currently select one provider.
Global TypeSpec dependencies retain their existing ordering rules.

Ordinary readers accept consistently reordered/duplicate maps and target
records and otherwise valid unused entries. Argument order is semantic,
not optional canonical sorting; never sort type arguments before constants.
Preparation order is likewise semantic. A scoped audit may enforce first
use/deduplication; malformed extents, kinds or references are not noncanonical.

## Binding, substitution and evidence

### Complete declaration and actual mapping

Resolve the exact MethodDef/Import first. Obtain its owning Module,
MethodSig, optional declaring TypeDef, complete formal ranges and contracts.
For an imported target, the retaining expected declaration is the call's
contract; bind it to the provider and retain both graphs and their exact
formal correspondence. Do not substitute the provider's current defaults,
relaxed constraints or stronger guarantees for that original view.
Actuals are interpreted in the **caller Body's** Module and lexical formal
context; declaration terms retain their own provider/expected context.
The only caller formals admitted by this carrier are those of its MethodDef
and declaring TypeDef, not arbitrary rows or hidden generic captures.

1. Validate owner/formal/range ownership and contiguous zero-based ordinals.
   Bind the explicit owner instance and check all its formation obligations.
2. Normalize actual terms in the caller's current instance context, then
   substitute declaring-owner arguments into the target's terms.
3. Bind every method actual by ordinal and formal kind; perform forward
   substitution and normalization, including nested types and constant
   domains/values. Distinguish the callee instance context from the caller,
   even for recursive calls to the same declaration. Substitution is
   capture-avoiding, not repeated global replacement of a formal row.
4. Check all domains and constraints and preserve the full substituted
   receiver, ordinary inputs, result, defaults and applicable contracts.
   Complete does not mean closed: supported caller-formal terms may remain
   symbolic until the enclosing instance is closed.

All defaults/inference and source overload selection precede this encoding.
There are no holes, `_`, `default` markers, omitted trailing arguments or
runtime default selection. Publish defaults on the declaration even when
every current call supplies explicit actuals. Type defaults may refer to
earlier in-scope formals through existing TypeSpecs; G1 constant defaults
must use its admitted ConstantSource path. An unencodable default makes the
retained declaration unsupported, not equivalent to `defaultArgumentRef=0`.

### Singular dispatch evidence

| Role/evidence | Required binding |
|---|---|
| Direct, kind 0 | `evidenceRef=0`. Both fields are zero as required; no dispatch witness or Constraint reference is attached. Access and all generic-argument obligations still apply. |
| Requirement, kind 1 | R(Constraint), section `001A`, owned by an in-scope caller type formal and **kind 1 interface instance**. Its exact subject/formal and substituted interface closure supply the open dispatch obligation. There is no closed witness reference in that Constraint. |
| Requirement, kind 2 | R(InterfaceWitness), section `0028`. Require the exact closed implementing/interface types and the selected member mapping for the complete declaring-interface/requirement identity. |
| Requirement, kind 3 | Logical WitnessProjection evidence remains preserved, but its physical table/reference/mapping codec is unassigned. G1 cannot encode or execute it; reject affected use as unsupported. No row, Import token or direct-witness alias is assigned for it. |

Other role/kind combinations, zero evidence for a requirement, or nonzero
evidence for a direct target are invalid. An intrinsic-property or
associated-relation Constraint cannot select interface dispatch.

The MethodMap target for role 2 is the **requirement MethodDef**, not a
convenient implementation with a matching signature. Check its InterfaceMethod
ownership and exact declaring interface in the evidence's inherited closure.
Repeated paths to that same complete identity deduplicate; unrelated
same-shaped requirements remain distinct.

For a complete assigned closed witness, select exactly one WitnessMember by
its declaring-interface instance and requirement MethodDef. Resolve its
selected owner/MethodDef, including any retained expected-stub bindings.
For open evidence the subject comes from Constraint.ownerGenericParam;
for closed evidence it comes from InterfaceWitness.implementingTypeSpec.
Bind any requirement receiver to that exact subject under the published
witness receiver/access contract. This is not a conversion to a stored
interface value or an additional ordinary argument. Bind an explicit
interface self formal only where that declaration identifies it; do not
retarget ordinary interface arguments that happen to equal the subject.
Substitute declaring owners first, then map requirement and implementation
method formals by kind/ordinal with matching arity. Preserve every mapped
constraint, default and contract. Reject an argument permutation, missing
binding, added caller obligation or adaptation needing an unadmitted carrier.
Retain an inherited/default target's original declaration and contracts.
Witness access does not expose a private selected implementation as an
independently callable direct member.

This record does not repair missing associated identities, witness origin
or member-reference codecs, parameterized witness publication or dispatch
representations. A structurally recognized witness is usable only when its
entire retained graph has a complete supported interpretation. Do not fill
missing mappings by private-body inspection or source rebinding.

### Plural generic-argument obligations

For each callable instance, build a finite typed environment keyed by
**formal declaration identity plus exact Constraint identity**. Include
declaring-owner formation constraints and every method-formal constraint,
not only the one used by the current instruction. Constraint identity retains
its owning formal, kind and complete substituted payload; do not key by
spelling, property number alone, physical row or one witness per type.

This environment is reconstructed from mandatory declaration/argument graphs,
not serialized as an additional GenericCall evidence range:

* For an open actual that is an in-scope caller type formal, retain the exact
  caller Constraint(s) supplying each obligation after substitution, including
  checked inherited interface views. Establish only implications supported
  by the published contracts; a coincidentally successful closed use is
  insufficient for the open caller's full domain.
* For a closed interface obligation, use the actual type's exact declared,
  complete witness for that interface, under the existing conformance
  authority and identity rules. Distinct interfaces require distinct slots
  even when one implementation method serves both. Equivalent paths may
  share the same witness only without merging the obligation keys.
  Distinct conflicting origins diagnose; order cannot choose a witness.
* Intrinsic properties use the exact substituted complete-type/domain facts:
  immediate write access is not synthesized; Blittable retains its selected
  target/Layout dependencies; uniqueness is not inferred from one value.
  These are typed facts, not interface-witness slots.
* Associated-relation constraints retain their own identities and equality
  obligations. Their separately proposed
  [payload/associated-identity completion](constraint-associated-relation-payload.md)
  is not composed into G1; a definition requiring that missing support
  rejects even if a particular call does not inspect the relation.

A direct target with two formals, each requiring two interfaces, needs
**four** constraint-keyed entries while its encoded evidence remains `(0,0)`.
A requirement call can additionally need its one selected dispatch reference.
If an actual needs a projection, an open constructed witness or another
unrepresented fact, neither a singular reference nor absence of a serialized
argument-evidence list makes that fact available: refuse the binding/use.

This is the explicit reconciliation with the singular logical contract.
It preserves direct calls without evidence and exact requirement dispatch
while requiring all additional obligations to be recoverable and retained.
It selects no general runtime witness search/dictionary and no reconstruction
of per-value existential evidence. A future carrier for non-reconstructible
evidence needs another explicit decision, not an implementation fallback.

## Invocation, results and failure

The fully substituted selected contract determines ordinary input count:
receiver if present, then every Param in declaration order, including unit
positions. Generic arguments/evidence are metadata, not extra ordinary inputs.
CallSite selectors name already evaluated values/descriptors. Preserve each
source-use mode, complete reference type, owner/loan provenance, initialization
destination and all output/backing dependencies.

Earlier Body instructions perform Capture and Stage once, in source order.
The call performs **Check, Prepare, Invoke, normal Publish** in that order.
Its preparation indexes select only DestinationPreparation records in the
chosen dependency-valid retirement/vacancy order. They are not generic
argument evaluation, witness acquisition code or arbitrary helper recipes.
Metadata binding/support must already be established before affected execution.

Normal non-unit results require a Register/Home destination with the exact
substituted type and responsibility. None uses index zero and is valid only
for an exact unit result or DoesNotReturn. A DoesNotReturn call retains its
declared result type, terminates its Block and has no normal successor.
Discard cannot erase affine cleanup, and a failed call publishes no normal
result. Preserve staged-owner cleanup, already performed transfers/retirements
and output state on each outcome; no rollback is invented.

Caller-unsafety, native ABI and data admissibility remain independent.
No target role authorizes marshalling, pinning, rooting, lifetime extension,
stronger access or treating a checked reference as a raw pointer.

Producers must diagnose invalid authored bindings and emit truthful facts.
Consumers must reject malformed framing, reserved fields, wrong namespaces,
bad ownership/ranges/scopes and incompatible selected bindings, and refuse
unsupported required semantics/target/runtime before affected use. Required
extent arithmetic is checked before allocation/indexing. An unknown record,
argument tag or evidence branch never retries another layout/table/schema.
Failure or resource exhaustion returns no success-shaped partial binding,
artifact or executable instance. Diagnostic wording and pass organization
remain implementation choices.

Consumers may rely on structurally consumable producer-published body facts;
this proposal does not mandate re-proving all dataflow or source semantics.
Creating a **new** instance/binding still checks its complete substituted
declaration obligations against the selected providers. A deeper body audit
is separate. Missing evidence is not permission to assume producer truth
for a reference or representation that does not exist.

## Providers, source-free remapping and lowering

Public generic definitions use the assigned MethodDef Import/Export kinds
and explicit producer-chosen export keys. No specialized-name convention,
fresh identity per instantiation, sample recognizer or automatic re-export
is added. An expected MethodDef is bodyless and retains its own complete
MethodSig/formals/constraints/defaults/contracts. An independently executable
generic provider must supply the actual checked open Body and its complete
dependency closure, or a separately admitted equivalent implementation
contract; a signature-only facade or precompiled closed instance does not
supply arbitrary new instantiations.

Bind Module identity/key/kind and owner before directed contract comparison.
Map provider/expected owner and method formals by kind/ordinal, never row
or name. Preserve each original expectation and any stronger published fact
actually relied upon. Formal renaming and physical row changes do not break
identity; changed arity/kind/constant domain, strengthened caller constraints,
lost guarantees or incompatible selected mappings do. A supported constraint
relaxation is not permission to erase the retained old obligations.
Existing calls keep their already selected defaults, not a replacement's
new default. Every retained default contract still participates in comparison.

Multiple keys/views selecting one definition retain their alias equality and
separate expectations. Replacing an Import by a packed direct MethodDef
requires preserving those expectations and all surviving binding obligations;
it cannot make the selected provider's current declaration the sole original
contract. Rebind the complete witness/constraint environment and invalidate
dependent specialization/cache decisions when the provider binding changes.

Source-free readers, mergers and writers perform these distinct fixups:

| Decoded edge | Remapping obligation |
|---|---|
| MethodDef/MethodSig/GenericParam/Constraint | Rebuild exclusive ranges, declaration owners and ordinal correspondences, defaults, kind-directed payloads and all nested type/constant/import references. Do not share a nonempty method generic range across different MethodDefs. |
| CallSite target kind 4 | Remap `targetIndex` through this Body's record map; it is not MethodMap. Other target/result/input/preparation selectors retain their own namespaces. |
| GenericCallTarget | Remap `methodMapIndex` through MethodMap; resolve/rebuild its MethodDef/Import token separately. Remap the optional complete owner through TypeSpecRef rules, evidence through its kind-selected global table, and every argument through its own tag. |
| TypeActual / ConstantActual / ConstantFormal | Remap TypeSpec graphs / ConstantSource-value graphs / exact caller GenericParam respectively. Preserve normalized complete type/value, formal scope and dependencies. Scalar Blob payload bytes are not reference slots. Closing a formal changes its representation as specified below, not just its row number. |
| Requirement and obligation graphs | Remap exact caller/target constraints, InterfaceWitness/member/owner references and all original expected/provider contexts. Reconstruct the plural environment after remapping; row equality is not evidence equality. Unsupported projection/associated codecs do not acquire a remap by resemblance. |
| Body and artifact indexes | Rebuild record offsets, map counts/mask, containing Body/section offsets, code extents, Block offsets/lengths and changed instruction ordinals. Remap or omit optional SourceMap slots under its contract, including 90/E0 width changes. |

A row shared across using contexts must retain the same resolved meaning;
otherwise clone/remap it. Re-encoding decodes operations and graphs, not
matching octets in argument references, constants, counts or code offsets.
Retained Modules keep their own requirements after packing/trimming.

When serializing a separately closed Body, replace caller-formal type terms
with the resulting complete TypeSpecRefs. Replace a closed ConstantFormal
argument with ConstantActual and an admitted exact SF1 ConstantSource,
retaining its value/dependencies and adding any newly required 11/1 entry.
Resolve open dispatch through the closed obligation environment; replace
kind-1 evidence with a supported kind-2 witness reference if that closed Body
no longer has the referenced formal in scope. Direct evidence remains zero.
If the needed closed evidence/value cannot be serialized, keep the original
scoped generic program or refuse that output operation. A dangling formal,
out-of-scope Constraint or unassigned projection reference is not a valid
closed artifact. Internal lowering may retain typed origin/context links
without serializing another Body, but that is not an alternate wire form.

Lower a closed instance only after complete binding, substitution and support
checks. Reconstruct its typed target, plural obligations, evaluated inputs,
preparation, exact result and dependency/ownership facts in the ordinary bound
tree. Shared traversal, rewriting, verification and lowering must preserve
these facts, including reference results and caller-formal constant forwarding.
Requirement lowering uses its selected mapped member/default; it cannot retry
overload resolution, bypass an incompatible override or upgrade to private
body facts. Backend devirtualization does not remove retained contracts.

Closed specialization may use the selected target's existing call interface;
there is no new generic machine ABI. Instance/cache identity includes resolved
declaration, complete owner and ordered method arguments, evidence/obligation
bindings, original expectations, provider revision/binding generation, exact
schemas/rules, target and runtime. Recursive exact instances may share work;
growing expansion or resource exhaustion must explicitly refuse completion,
not truncate a graph or claim a partial success.

Emitting only closed native code does not replace a retained public/open
Standard definition. Keep its semantic program when independent consumers
are promised new instantiations. No `.trmn`, private saved IR, source snapshot
or source parser is required to recover a G1 call's meaning.

## Required independent cases

These are proposed conformance/implementation gates, **not executed results**.
Supply complete independently constructed artifacts and public-API
producer/reference/consumer tests, not only one writer's round trips.
Execution cases use a selected supported target and explicit closed instances.
Witness-dependent cases require the complete witness prerequisites; numeric
recognition alone must not be reported as witness execution support.

| Case | Required outcome |
|---|---|
| Public module-level type-generic identity, imported by a separately compiled consumer with no producer source | Preserve open definition/formals and exact Import expectation; close, lower and execute a supported scalar instance. No specialized export-name contract. |
| Three-module forwarding chain with type and constant method formals interleaved | Preserve ordinal order and kinds 1/2/3; forward an open constant formal, then close it at the root without source, constructors or default replay. |
| Declaring owner ordinal 0 and method ordinal 0, plus recursive calls to the same generic MethodDef | Substitute owner first and keep caller/callee instance scopes distinct; no accidental formal capture. |
| Generic owner missing an explicit construction, qualified owner-selection node, wrong owner, or self-like ordinary interface argument | Reject invalid owner binding; retain actual receiver access separately and substitute only the declaration's explicit self role. |
| Nested indexed type constructors over a caller type formal; constructed nominal terms with represented closed type/constant arguments | Forward-substitute and remap the entire tree. Identity-only success does not claim generic-class field/layout/value execution. |
| Type defaults depending on earlier formals; admitted fixed constant defaults; unused formals | Emit all resolved argument slots and retain all published defaults/constraints. Do not infer omitted slots during reading. |
| Nonzero unit TypeSpecRef as a type actual; exact unit input/result positions | Preserve type identity and logical positions; do not confuse unit with null/None or admit unit as a constant-formal domain. |
| Fixed integer minima/maxima including signed/unsigned 128-bit; Boolean false/true | Preserve exact domain/value through SF1, generic binding, interning and source-free remapping. Boolean byte 2 rejects. |
| Open requirement dispatch through an exact caller Constraint; closed dispatch through a complete supported witness | Preserve requirement identity and selected mapping/default. Constraint contains no closed witness reference. |
| Inherited diamond versus two unrelated same-shaped requirements | Deduplicate only the identical complete declaring-member identity; never choose by method name or path order. |
| Direct call with four interface obligations across two actuals; requirement call with those obligations plus dispatch | Build all four keyed obligation entries; direct encoded evidence stays zero; the dispatch reference never substitutes for the four entries. Missing/conflicting prerequisites reject. |
| Mapped implementation has renamed method formals | Accept compatible kind/ordinal mappings after owner substitution; retain defaults and every applicable contract. |
| Mapped implementation changes arity, permutes formals, changes constant domain or adds caller requirements | Reject; no inferred forwarding/projection or argument rearrangement. |
| Broad Copy-or-unique caller passed to a default-domain callee without an encoded supported refinement | Reject the invalid authored open binding; one successful Copy specialization cannot validate it. |
| Wrong scoped formal, type/constant tag mismatch, zero actual, encoded inference/default hole, wrong argument count | Reject before affected use. A callee formal is not an implicit caller argument. |
| Direct evidence, requirement kind 0, property Constraint used as dispatch, mismatched subject/interface/member, duplicate/missing witness mapping | Reject invalid binding/structure; do not substitute a same-shaped target or default. |
| WitnessProjection, associated-relation dependency, open constructed witness, existential actual needing hidden evidence, enum/wrapper constant or symbolic constant term inside TypeSpec/default | Refuse unsupported required use with its actual missing prerequisite; no kind-2 witness alias, private tag, erasure or fabricated value. |
| Body record/map index 0, 255, 256, 65535; 65,536-entry inventories; 65,537 entries | First endpoints valid within actual counts; use E0 beyond CallSite index 255; reject oversized Body inventories without stealing FFFF for absence. |
| More than 65,536 method argument slots but a valid global formal range and sufficient resource/Body extent | Do not apply a u16 local-index cap to the u32 positional count. Conversely reject a global row beyond 00FF_FFFF or a count/extent overflow. No portable budget is promised. |
| Truncated record/header/argument/input/preparation lists, nonzero reserved fields/E0 control, unknown tags, wrong target record kind, trailing bytes, bad record offsets | Reject safely under the exact selected schema; no alternative decoder or table retry. |
| Import-shaped value in a row-only argument/evidence field; Body TypeMap token in a TypeSpecRef field; row with high byte nonzero | Reject wrong namespaces even when a plausible row exists in another table. |
| Compatible provider body/formal-name/row replacement; incompatible constraint/default/owner/alias/selected-member replacement | Preserve original expectations and all relied-on obligations for the former; reject the latter's incompatible retained graph. Revalidate cached instances against the replacement. |
| Different retained expectations select one provider/key; aliases name one declaration | Check every view and preserve equality; neither weakest-view merging nor provider-row deduplication may discard a contract. |
| Producer/consumer/provider tables, MethodMap and records independently permuted or duplicated; 90 rewritten to E0 and back; source/debug sections removed | Preserve identical typed calls/results and all bindings source-free, with complete local/global/range/offset/SourceMap fixups. Ordinary valid noncanonical forms remain accepted. |
| Open program specialized and re-encoded as a closed Standard Body | Replace formal type/value/evidence forms through their exact namespaces, retain original obligations and manifests, and reject dangling caller formals or missing closed witness/value representations. |
| Multiple dependency artifacts with independently selected supported schemas, then packing | Admit each separately; pack only into an explicit destination capable of retaining the whole closure. Equal `(1,0)` never supplies decoder agreement. |
| Evaluated inputs with observable order, init outputs/preparations, borrowed/reference results, unsafe targets and DoesNotReturn | Preserve once-only staging, Check/Prepare/Invoke/Publish order, exact state/dependencies, caller-unsafety and terminal behavior. Unsupported dependent codecs reject rather than dropping those contracts. |

## Implementation and adoption boundary

The [current bounded-generic inventory](../implementation.md#bounded-generic-functions)
describes compilation-local specialization, not public/open generic exports
or Standard generic-method execution.
[The conformance inventory](../implementation/conformance-gaps.md) separately
records structural GenericParam/Constraint work and remaining substitution,
witness, provider and execution gaps. Those observations do not implement
this proposal and are not updated here.

Before claiming G1, integrate the selected delta into the owners above and
complete these connected implementation gaps: public declaration/reference
binding with complete original expectations; typed generic targets/actuals
and plural obligation environments; shared bound-tree traversal/rewriting;
mandatory structural/binding verification; Standard reader/writer and
source-free remapping; complete provider/default/replacement comparison;
and selected-target closed-instance lowering with independent consumer cases.
Representation support must cover the final relevant inputs/configuration,
not an excluded prototype or private-module specialization.

**No additional wire choice is delegated to implementation.** Adoption would
select G1, record 10, target kind 4, argument tags 1/2/3, display `call.generic`,
Feature 23/1 and the explicit reconstruction/support rules together.
It would not approve the separate missing WitnessProjection/reference codec,
associated-member/witness completions, general symbolic constant terms,
generic-class storage, runtime generic sharing or existential evidence
carriers. Those remain concrete independent contract gates, not permission
to guess, fall back, recognize a sample or discard an obligation.
