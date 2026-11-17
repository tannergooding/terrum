# Standard TRRM method receiver access

> [!IMPORTANT]
> **Unapproved proposal; provisional direction only under the user-authorized
> missing-contract workflow, pending later review.** This document changes no
> normative specification, registry allocation, compiler or support claim.

## Missing contract and carrier decision

Recommend one **MethodDef `RequiresWritableReceiver` flag**, combined with
MethodSig's existing receiver presence, for ordinary method receiver access.
Select the exact receiver-bearing successors below; do not silently add this
meaning to the existing G1L1/C1-family agreements.

The [specification index](../specs/README.md) identifies these owners:

| Repository evidence | Established rule or missing boundary |
|---|---|
| [Language receiver defaults](../specs/language.md#types-and-operations), [callable/constructor/accessor grammar](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | Ordinary instance receivers are readonly unless explicitly `mut`. Receiver `mut`, binding rebinding, type `write` and unpublished initialization are distinct. |
| [Interface witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity) | Every selected requirement/implementation must preserve receiver access, including inherited requirements and qualified-self adaptation. |
| [MethodDef/MethodSig and flag registries](../specs/il-metadata.md#section-kinds-and-row-layouts) | Receiver presence is separate from explicit Params; MethodSig records declaration order. Receiver access needs a registered contract field, not names. MethodDef currently assigns only visibility. |
| [Directed binding](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | A provider cannot narrow the retained caller domain. This proposal supplies the explicit receiver-access satisfaction relation below, not general type covariance. |
| [G1/C1 labels](standard-trrm-g1-interface-parameter-labels.md#exact-selection-allocations-and-alternatives) | L1 preserves parameter labels, not receiver access. Its five whole-schema selections need an explicit receiver delta. |
| [Class receiver mapping](standard-trrm-c1-instance-call-receivers.md#proposed-exact-mapping), [class/member publication](standard-trrm-c1-class-source-publication.md#required-constructor-and-member-closure) | Their class-specific C/`write C` mapping is useful but cannot represent a general mutable-place/interface receiver contract. Their statements that no MethodDef flag carries access are superseded **only for this proposed route**. |
| [Private callable declarations](source-interface-witness-private-persistence.md#requirements-defaults-and-callable-instances) | Private `CallableDeclaration.receiverMode` preserves this dimension and an abstract interface receiver. Its tags/sidecar are not Standard allocations or a Standard carrier. |

### Physical alternatives and allocation review

| Carrier | Assessment |
|---|---|
| **MethodDef flag plus existing receiver presence** | **Selected.** Three ordinary states need one additional Boolean. No row growth, optional-record absence rule or new reference space. Complete callable comparison must include the MethodDef, not only its structural MethodSig. |
| Append `u8 receiverMode, u8 reserved[3]=0` to MethodSig | Viable 28-byte successor row: None/Readonly/Mutable, checked against presence, with initialization still role-owned. It puts access directly in structural signature identity but widens every 24-byte signature and changes its sharing/encoding. No required information is added over the flag. |
| Required MethodDef-owned receiver Contract/record | Viable explicit mode with owner/cardinality and presence checks, including expected declarations. It needs a new kind, payload, coverage rules and remapping for the same fact. Consuming or other future receiver modes are not a present requirement. |
| Infer access from TypeSpec, body, member name or private tags | Insufficient: a writable place of value type and an abstract interface receiver need an independent access requirement. Body inspection cannot supply an imported declaration contract. |

The [machine registry](../specs/representation/trrm-1.registry.json) assigns
section `0016` MethodDef a 28-byte row, with its little-endian `u32 flags`
at offset 12 and permitted mask `03`; visibility occupies bits 0..1.
MethodSig `0017` remains 24 bytes. **Candidate only: bit 2,
`0x0000_0004 = RequiresWritableReceiver`; candidate MethodDef mask `07`.**
Bits 3..31 remain zero; visibility value 3 remains invalid. Public readonly
and mutable ordinary MethodDefs would therefore use `02` and `06`.

This is the MethodDef flag namespace, not FieldDef mutable `04`, Effects
caller-unsafe `04`, CallSite source-use 4, Construction Contract `0003` or
private record `0104`/mode tags. The inspected related proposals do not assign
another MethodDef bit 2 meaning. No section, DefId, Contract kind, Feature,
opcode, prefix/escape, operand, width or map capacity is added. Reorganizing
unpublished flags is permissible, but gives no smaller complete carrier.
These are proposed registry edits for later integration, not allocations here.

## Exact receiver semantics

Classify the selected callable's supported construction/initialization role
**before** interpreting ordinary receiver access. For an ordinary method:

| `receiverTypeSpec` | Candidate flag | Meaning and validity |
|---|---|---|
| Zero | Clear | **None**. Module-level functions and type-owned static methods have no receiver input or receiver authority. |
| Nonzero | Clear | **Readonly** receiver borrow. |
| Nonzero | Set | **Mutable** receiver borrow: caller must supply writable receiver access. |
| Zero | Set | Invalid, including on a static method, static constructor or receiverless signature anchor. |

Every nonzero receiver needs the existing nonzero owner TypeDef and a valid
receiver type for that declaration. InterfaceMethod still requires an instance
MethodDef of its exact interface owner. Wrong owners, invalid visibility,
reserved bits, illegal receiver qualifications or unsupported required roles
refuse before affected use. A bodyless or unused declaration is not exempt.
Clear is a complete readonly/None fact **under this selected schema only**,
not evidence recovered from another schema's reserved zero bit.

Map the checked source declaration directly:

- Ordinary instance callable without receiver `mut`: clear. With receiver
  `mut`: set, including interface requirements, defaults and implementations
  wherever independently admitted. Type-owned `static` and module functions:
  no receiver and clear. Never derive the flag from local/formal `mut`,
  fields, nominal declaration mutability, an observed write or body purity.
- For an admitted property/indexer, apply the same flag independently to each
  ordinary `get`/`set` MethodDef from that accessor's `mut`; the enclosing
  Property/Indexer row carries no receiver mode. `init` is not an ordinary
  receiver operation, and its authority remains phase-owned.
- Preserve the complete receiver TypeSpec and its owner substitutions. For
  ordinary class methods retain the class proposal's nonoptional normal `C`
  for readonly and `write C` for mutable receiving; **require agreement with
  the flag**, rather than infer a missing flag. A set flag grants neither
  class declaration mutability nor a writable handle. For value receivers,
  preserve their actual complete type; writable receiving requires the
  ordinary writable place/loan, not a fabricated `write S` type or raw pointer.
- For an interface-owned instance method, the distinguished receiver TypeSpec
  identifies its exact declaring interface instance in declaration scope,
  with outer qualifiers zero and all owner arguments retained. It is an
  **abstract implementing-receiver position**, not an interface-object value.
  Bind only that position to the exact Constraint subject or witness
  implementing type. Apply an explicit self-formal substitution only where
  declared; do not replace unrelated occurrences of the interface/type in
  parameters, results or ordinary generic arguments. The flag survives this
  binding unchanged.
- Nongeneric MethodSigs may still be shared where their structural fields
  agree, even if using MethodDefs have different access flags. Access remains
  part of the complete callable contract and all relevant comparison/cache
  keys. Existing exclusive method-generic ranges remain exclusive.

Readonly is not Pure, immutable backing, a snapshot or NoAlias. Mutable access
does not make fixed fields writable, confer exclusivity beyond ordinary loan
rules, authorize unsafe operations or transfer receiver ownership.

### Initialization is not mutable receiving

Keep the [constructor proposal](standard-trrm-c1-constructor-declarations.md#declaration-receiver-and-body-mapping)
and [construction lifecycle](standard-trrm-c1-construction-runtime-boundary.md)
as separate authorities:

- Role-1 I has its required receiver, **clear flag**, and initialization
  authority from Construction, not Readonly or Mutable. Its receiver input
  remains source-use 5 over the exact fresh unpublished context.
- Role-2 P has no receiver and a clear flag. Neither role can substitute for
  an ordinary interface requirement. Set on I/P is invalid in this route.
- Source constructors reject `mut`; source `init` accessors likewise do not
  become mutable ordinary methods. Their admitted role/phase must carry
  initialization authority; a missing accessor/initialization codec still
  refuses. Static initialization remains receiverless.
- An explicit `ref<init T>`/initialization-output formal keeps its own complete
  type, ordinal, vacancy, outcome and publication contract. Neither receiver
  bit nor ordinary borrow grants access to vacant/partial storage or permits
  reinitializing a live object.

## Satisfaction, calls and replacement

After resolving identities, substituting owner arguments and corresponding
method formals by kind/ordinal, compare ordinary access as a **caller-domain
requirement**, not by flag equality or integer ordering:

| Retained expectation / interface requirement | Readonly provider / implementation | Mutable provider / implementation |
|---|---|---|
| Readonly | Satisfies this dimension | Reject: adds writable-access obligation |
| Mutable | Satisfies by checked writable-to-readonly borrowing | Satisfies this dimension |

None requires None; receiver presence, callable/initialization role and all
other required dimensions still agree. For concrete class receivers, compare
the same complete nominal owner/backing and allow only the corresponding
outer `write C` to `C` receiver attenuation in the Mutable→Readonly case.
This is not exact TypeSpec equality or permission to weaken inner arguments,
representation, pin status, ordinary parameters/results or init storage.
Interface-to-implementer binding uses the distinguished position above,
not nominal equality between interface I and implementing C.

Thus a readonly implementation **can satisfy a `mut` requirement**, but a call
through that requirement still requires a writable receiver, even after
specialization/devirtualization. Its forwarding view may attenuate the already
captured borrow; it cannot broaden the caller's visible contract, repeat
evaluation, copy an affine receiver or invent authority. A separately selected
ordinary concrete readonly method remains callable under its own contract.
Apply the relation to every required/inherited member, selected default and
supported qualified-self projection, not just currently called members.
All type/mode, safety/effect, dependency/capture, initialization/output,
ownership/cleanup and supported constraint obligations remain independently
checked. Missing projection/evidence codecs remain gates.

### One invocation plan

Use the **statically selected declaration view**: the retained expected
MethodDef for an imported direct call, or the requirement MethodDef for an
interface call. Binding to a less demanding provider does not rewrite it.

1. For direct generic calls, perform the existing owner-first/method-ordinal
   substitution and retain that view's flag. For G1 role-2 calls, resolve the
   exact declaring-interface/requirement identity and open Constraint or
   admitted closed witness, bind its subject, and check requirement access
   before selecting/lowering the implementation. A generic constraint,
   writable local binding, carrier shape or favorable target body does not
   itself provide writable receiver authority.
2. Count the receiver once, before every explicit logical Param, including
   unit Params: checked `inputCount = receiverCount + paramCount`. Receiver
   is not Param ordinal zero; label, preparation, output and dependency
   formal ordinals never shift.
3. Ordinary general CallSite receiver rows use **3=borrow read** for Readonly
   or **4=borrow write** for Mutable, according to that selected caller view.
   None, copy/read 1, transfer 2 and initialization 5 are invalid ordinary
   receiver uses. Require an admissible captured live, complete receiver with
   sufficient typed access/backing/loan support. For classes this borrows
   object access from the captured handle, not the handle slot. For a mutable
   value receiver it must identify an admitted writable place/loan; copying
   a temporary does not repair missing authority. Unsupported physical
   place/loan forms refuse rather than erase the requirement.
4. Compact ordinary MethodMap calls derive the same mode from the complete
   selected MethodDef contract and obey the same window, state, access and
   preparation restrictions. G1 GenericCall stays its existing general
   target-kind-4/record-10 form; no compact generic call is added. I retains
   its construction-only source-use-5/general-call rules.
5. Capture receiver once before written arguments; retain ordinary
   Capture/Stage/Check/Prepare/Invoke/Publish ordering and every-exit cleanup.
   Preserve actual owner/generation, backing, roots and receiver-dependent
   results/captures for their declared extents. A Mutable→Readonly adapter
   passes a checked readonly view to the target without dropping the caller
   view's loan obligations. No implicit ownership transfer, pinning, rooting,
   materialization authority or lifetime extension is introduced.

For source-free imports/replacement, resolve Module/revision, ExportKey,
definition kind and owner first. Check each original expected view against
the selected provider with the same relation; preserve requirement access
separately from implementation access. Multiple expectations/aliases all
remain obligations, including any stronger provider fact actually relied on.
Readonly→Mutable replacement fails a retained readonly view even with equal
MethodSig bytes, labels or machine ABI. Retain the checked access facts through
bound traversal, rewriting, proof stubs, verification and specialization.

Packing/re-emission decodes typed MethodDefs and remaps their signatures,
owners, Imports/Exports, interface/witness edges, constraints, labels,
contracts and Body maps together. Preserve expected flags separately from
actual target flags; do not merge views merely because their MethodSig or
provider/key matches. Rebuild affected offsets/source maps when code changes.
Consumers check framing, flags/presence/role consistency, references, support
and binding before use; they need not re-prove arbitrary producer body facts.

Lower an admitted closed call to its checked concrete target/forwarding plan,
preserving the chosen backend's existing receiver representation and calling
convention. This adds **no dynamic interface ABI**, vtable, boxing, runtime
witness search/dictionary, hidden runtime argument or native-interface grant.
Open metadata can remain open until supported specialization; unavailable
evidence or execution support refuses. Native lowering/execution retains its
independently selected target/runtime agreement.

## Exact profile consequence

`R1` below is document shorthand, not a Feature or independently selectable
component. Recommend these **five new, unapproved, exact whole agreements**,
each its corresponding [L1 agreement](standard-trrm-g1-interface-parameter-labels.md#exact-selection-allocations-and-alternatives)
plus precisely this receiver delta:

| Candidate shorthand | Exact candidate identity |
|---|---|
| G1L1R1 | `trrm/1-draft-generic-method-calls-parameter-labels-1-receiver-access-1` |
| C1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-parameter-labels-1-receiver-access-1` |
| C1M1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-parameter-labels-1-receiver-access-1` |
| Construction-1L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-1-parameter-labels-1-receiver-access-1` |
| Construction-2L1R1 | `trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1` |

Keep `(1,0)`, governing edition 1, existing row strides, Body encoding and
parent catalogs. No new Feature is selected: ordinary receiver access is a
mandatory MethodDef contract dimension in each exact successor, like C1M1's
mandatory declaration fact, not an optional capability inferred from set bits.
Preserve independent per-Module Feature 23/26 triggers and all other inherited
closure, including C1's reconciled catalog and Construction-2's Feature 25.
An interface-only nongeneric requirement does not acquire Feature 23 from R1.

For this receiver-preserving route, G1 source-map versions 1 and 3 keep their
grammars but pair **only with G1L1R1**; update emitter selection with that
pairing, not the JSON shape. The C1 declaration stage selects C1L1R1;
class-mutability binding selects C1M1L1R1; constructor/member publication
selects Construction-1L1R1; complete phase-1/2 class/witness/failure closure
selects Construction-2L1R1. C1's typed source associations, I/P keys, original
role/interface-anchor contexts, and the separate phase-1/phase-2 maps retain
their exact parent shapes and meanings. Remap their original Clause/Origin/
Plan references with MethodDefs and the actual Plan Body map contexts.

C1L1R1 still lacks class declaration mutability; never infer that Boolean
from a mutable method. C1M1L1R1 does not acquire construction roles, and
Construction-1L1R1 does not acquire Construction-2 backing operations. Complete
class/member/witness closure and Construction-2's independent host/runtime
admission remain required; receiver/label success discharges none of them.
G1's bounded owner/member/evidence gates remain G1's, not C1's broader codecs.

Require one exact selected literal for root, **all** supplied providers
(including unused/declaration-only inputs), and captured Clause/Origin/Plan
artifacts before schema-dependent decoding/indexing. Parent L1 or sibling
artifacts are not implicitly mixed, upgraded or decoded by a G1 sub-reader.
No CG1/version-2, E1, private or native composition is selected here.
Keep old selectors' meanings unchanged; no old reader or compatibility alias
is required. Replacing unpublished L1 agreements wholesale would also be legal
if all owners changed consistently, but silently changing only their receiver
reader is not that replacement. [Exact draft agreement](../specs/representation/format-versioning.md)
cannot be inferred from equal version bytes, a zero flag, labels or Features;
no schema-key-only conversion can recover an already erased source fact.

## Current implementation and acceptance gaps

The source changes below are unverified and do not amend the normative
specification or assign a Standard schema:

| Current state | Remaining gate |
|---|---|
| `BoundInterfaceMemberDefinition` retains callable receiver access; interface accessor `IsMutable` is included in property identity. Source witness matching applies the directed relation. Concrete `.via<I>` callable/getter calls check caller write access separately from the selected implementation signature; generic proof stubs require both a writable formal and a mutable place. New negative, forged-input, G1L1-refusal and NativeJit regressions are unverified. | Run the focused source and NativeJit cases; preserve access through supported traversal/substitution routes. Setter/indexer calls and broader projection codecs remain outside this source slice. |
| G1L1 remains readonly-only. G1L1R1 emits ordinary receiver flags for base methods and interface MethodDefs, and role-2 CallSite source-use `3/4`; source-free decoding retains the mutable loan. The producer requires the writable formal for mutable requirements, and the consumer checks that requirement plus the selected MethodDef mode. Readonly/mutable round-trip, wrong-mode, and directed expected/provider replacement regressions are added but unverified. | Keep L1 rejection. Complete G1/C1 expected-provider remapping and re-emission, direct and class CallSite paths, and lowering under exact successor selection; do not infer receiver access from names or another schema. |
| Private semantic-module production has no interface declaration/witness codec. The working tree now rejects interface-bearing programs explicitly; its regression is unverified. | Keep private and Standard persistence separate. Add a private codec only through its own assigned contract; private tags are not a Standard receiver carrier. |
| The MethodDef reader rejects flags above `2`. Class ordinary/I handling has the separate [class receiver integration gate](standard-trrm-c1-instance-call-receivers.md#alternatives-and-integration-gates). | Make masks and role/access checks schema-aware and share the invocation plan across general/compact/generic validation, reconstruction and lowering. Do not globally admit candidate bits in other schemas or remove construction/member gates. |

The current [active G1 writer/import tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericMethodSourceWriterTests.cs)
include label preservation, readonly G1L1 calls and unverified G1L1R1
readonly/mutable receiver source-use cases. Extend these and
the [target-import tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericMethodTargetImportTests.cs)
through public compiler APIs, with structural cases in the existing metadata
test area. Required focused cases:

| Cases | Required outcome |
|---|---|
| Two unrelated class/value APIs; readonly/mutable instance methods, receiverless static/module controls; zero/several explicit Params and logical units where independently admitted | Preserve exact presence/access and ordinals through source→Standard→source-free binding. Readonly accepts writable access; mutable rejects readonly access despite a mutable handle binding. A fixed binding holding `write C` is not rejected merely for lacking slot rebinding. |
| All four witness table combinations, explicit implementations/defaults, unused requirements and distinct same-shaped interface identities | Apply the table to every requirement. A readonly implementation of a mutable requirement still rejects readonly callers through that requirement, including after devirtualization; its concrete readonly entry keeps its own contract. |
| Open role-2 and direct generic calls; owner then method substitution, inherited declaring identities and qualified-self adaptation where complete codecs are independently admitted | Preserve the access requirement and exact witness subject without rewriting unrelated type occurrences. Keep unsupported projection/closed-evidence paths rejected, rather than invent a witness or dynamic ABI. |
| Mutable without receiver; visibility 3/high bits; wrong owner/anchor; class TypeSpec/flag disagreement; set flag on I/P; ordinary source-use 1/2/5 or reversed 3/4 | Refuse malformed combinations before use/output. Retain valid I/5, P/None and init-output formal controls; `mut` never substitutes for initialization authority. |
| Source-free replacement from a non-first/multi-Module provider, aliases and multiple expected views; legal row permutations, shared MethodSigs, packing/re-emission/reconsumption | Preserve labels, separate expected/actual access, identities and all retained obligations. Mutable expectation→readonly provider may pass; reverse fails. A relied-on readonly view cannot disappear in a later replacement. |
| Each exact successor, empty declarations and unused providers; parent L1, sibling, private and CG1 mismatches | Use only the selected complete schema. Reject mismatches without sniffing/retry; schema selection alone grants no extra parent capability or runtime support. |
| Receiver-dependent outputs/captures, unit/init formals, unsafe/effect clauses, no-return and later-argument failure/rebinding | Preserve once-only capture, formal ordinals, loan extents, output correlation, cleanup and authority through adapters and lowering. No early release, lost dependency or receiver ownership transfer. |

An eventual native claim additionally needs the existing independently admitted,
headless source-free JIT/AOT qualification exercising reconstructed receiver
calls, not only parsing, binding or emission. No such claim is made here.

On later approval/integration, amend metadata's MethodDef flag/receiver rules
and the registry together; add this explicit directed dimension to
binding/replacement and call validation, link it from the logical witness
requirements, and update the listed proposal selection/mapping owners.
Record actual support/gates in the implementation inventories when changed.
No receiver-carrier policy choice remains deferred in this recommendation;
formal approval, those integrations and their qualification remain open.
