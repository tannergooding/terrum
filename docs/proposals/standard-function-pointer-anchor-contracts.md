# Standard TRRM function-pointer anchor contracts

**Status: applicability and identity recommendations unapproved; provisionally
actionable under the authorized missing-contract workflow.** The applicable
authorization is:

> At a genuine missing contract, prepare the smallest concrete decision needed as a new proposal doc using gpt-6.1-sol at max reasoning, then continue with it being treated as a provisional approval/direction.

The existing conformance matrix marks Contract `0x000B`, the `0x0037`
PredicateGraph section and the 20-byte attachment shape assigned by
[ICOW-16/18](../specs/conformance/il-contract-clauses.md#owner-ranges-and-occurrence-keys).
The owning clause chapter still says those assignments are open,
and registry Feature 10/its prerequisites remain registration-pending. This
proposal preserves the explicit conformance assignment provisionally,
documents that owner discrepancy, and does not resolve it by reallocating a
row or declaring a feature registered. The applicability and identity
recommendations below remain unapproved. Provisional direction for
implementation does not approve or amend normative text, complete another
capability, or establish compiler support.

The companion
[predicate Feature composition decision](predicate-feature-composition.md)
supersedes this proposal's former `10 -> 11 -> 9` requirement and broad
phase-1/2 predicate applicability for provisional integration. The proposed
`000B` applicability below is now limited to the six phase-3 normal-result
selectors; general pre/post predicates remain gated. The anchor validity,
identity and rebasing design is otherwise unchanged.

## Decision, scope and owners

Recommend retaining Standard TypeSpec `0x0D` unchanged and defining its
anchor's applicable Contract kinds as exactly **Effects `0001`, OutputState
`0002`, Dependency `0004` and the bounded PredicateGraph `000B` subset**,
subject to their ordinary validity and complete support prerequisites.
These describe the target invocation, not an executable anchor. Exclude
declaration roles and evaluation-form authorization. Define equality by the
normalized signature and these contracts, rebasing anchor-local subjects
rather than comparing MethodDef names or rows.

This decision allocates no new ID, Feature, flag, reference kind or wire byte.
PredicateGraph clause `000B` uses the existing `PublishedPredicates`
Feature **`10/1`**, with proposed prerequisites **`[]`** for the exact
[normal-result selector codec](aggregate-native-outputs.md#exact-shared-predicate-codec);
function-pointer identity requires `NativeBoundaryContracts` Feature 1,
while value use and address/indirect-call operations retain their existing
Feature 2 and Feature 4 gates. No Feature 11/9 requirement follows from a
selector; independently retained value/union uses keep their own gates.
Current registrations marked pending remain gates until the complete
combined provisional schema is selected and supported; this proposal alone
does not activate them.
The entire Standard row remains seven bytes:
`u8 form=0x0D, u8 qualifiers=0, u8 callingConvention,
u32 signatureContractMethodDef`. Convention values remain `1=Unmanaged`,
`2=Cdecl`, `3=Stdcall`, `4=Vectorcall`. Original convention identities remain
distinct even when a target maps them identically.

| Owner | Preserved baseline and proposed completion |
|---|---|
| [Metadata TypeSpec forms](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics) | Replace only the open phrase “applicable ordinary contracts” for `0x0D` with the applicability, validity and equality rules below. Preserve the bodyless, non-exported, noncallable anchor and existing row/reference schemas. |
| [Contract applicability](../specs/representation/contract-clauses.md), [registry](../specs/representation/trrm-1.registry.json), [logical predicate/evaluation records](../specs/representation/logical-records.md#assigned-pre-v1-identities) | Add anchor-specific applicability and subject rebasing; preserve the `000B` assignment/shape stated in conformance ICOW-16/18 while reconciling the clause chapter's conflicting open-status text; retain pending Feature and predicate-codec prerequisites. |
| [Complete types](../specs/language/types-and-normalization.md#identity-versus-checked-adaptation), [call protocol](../specs/il-instruction-set.md#calls), [native boundaries](../specs/native.md#native-declaration-roles), [NBC-10](../specs/conformance/native-boundaries.md#2-callable-identity-and-interface-calls) | Specialize Standard native function-pointer contract identity and preserve those contracts through indirect invocation. Do not change general overload discrimination, source syntax, target ABI mappings or safe callable families. |
| [Feature manifests](../specs/representation/trrm-1.registry.json) | Reuse Feature 1 for anchor identity, bounded `10/1` with no unconditional prerequisites for phase-3 selectors, and existing Feature 2/4 gates for value/address/indirect use, under the [composition decision](predicate-feature-composition.md). No general predicate Feature is allocated or implied. |
| [Native convention source-schema proposal](native-calling-convention-source-schema.md#standard-metadata-targets-and-dependent-consumers) | Supersede only its unresolved anchor-contract interpretation, not its source schema or NativeCallable direction. |

Development section `0x0033` NativeFunctionPointerSignature, its NativeAbi
role `0x10`, and its six-byte function-pointer TypeSpec are different schemas.
None supplies Standard applicability, a compatibility alias or a fallback
decoder. Standard NativeCallable `000C` and NativeExport `0x0022` remain
independent actual-declaration roles.

### Minimal completion versus redesign

| Unapproved alternative | Consequence |
|---|---|
| **Complete the existing MethodDef anchor contract (recommended)** | Reuse MethodSig, Param and ordinary Contract carriers; add a closed applicability set and structural equality rules. Requires distinguishing anchors from callable declarations, including during rewriting. |
| Replace the anchor with a dedicated structural signature/contract owner, or inline that structure in TypeSpec | Removes MethodDef role ambiguity, but requires a complete new owner/reference schema, contract ranges, predicate attachment ownership and migration/remapping rules. No demonstrated need here justifies that wider redesign or a parallel permanent codec. |

The recommendation is based on the bounded semantic gap, not an obligation
to preserve unpublished artifacts. [Exact draft agreement](../specs/representation/format-versioning.md#2-beta-draft-and-preview-artifacts)
still applies; equal version bytes do not select these candidate semantics.

## Proposed anchor validity

1. `signatureContractMethodDef` is a nonzero typed MethodDef row reference,
   not a DefId, MethodSig reference or TypeSpecRef union. Its reserved high
   byte is zero and its row exists. The anchor has a valid required name,
   signature, Module ownership and exclusively owned Contract range.
   Existing checked range, ordinal, flag and reference rules apply.
   A MethodDef referenced by `0x0D` has this role for all its uses;
   bodylessness alone does not establish it.
2. The anchor has `bodyRow=0`. It is not the local definition of an ordinary
   Export, a NativeExport owner, a NativeImport owner, or an Import's expected
   callable declaration. It is not an entry point, accessor, cleanup
   operation, interface requirement/implementation or other executable role.
   Such uses cannot turn a signature anchor into an actual declaration.
   No additional private-visibility or module-level-only restriction is
   proposed: ordinary valid visibility and `ownerTypeDef` values may remain.
3. A `Call`, callable `CallSite`/MethodMap use, `NativeAddress`, or any other
   reference requiring a callable/addressable declaration cannot select the
   anchor, directly or through an imported proxy. A predicate's selected
   direct-call dependency likewise cannot target it. A Body may reference
   the **function-pointer TypeSpec** in its TypeMap and invoke a value of
   that type; the anchor is not that value's code address.
4. **Recommend an empty method-generic range**, exactly `(0,0)`. This is an
   explicit additional anchor restriction: `0x0D` supplies no method-generic
   argument recipe and does not denote a universally generic callable.
   Admitted constructed generic types in the signature remain legal.
   Enclosing formals retain their exact declared identities and may be
   substituted only through an already admitted declaring/use context;
   the anchor supplies no new generic environment. A concrete native
   invocation requires the resulting closed signature and all required
   generic support, not guessed actual arguments.
5. **Require `receiverTypeSpec=0`.** The source
   [function-pointer production](../specs/grammar-spec.md#32-types-parameters-and-arguments)
   specifies ordered by-value parameters and a result; it has no
   receiver-bearing form. A MethodSig receiver cannot be silently converted
   into a bound capture or an implicit first Param. A method whose ABI needs a
   receiver must use an authored static wrapper with explicit receiver
   parameters or remain unsupported for function-pointer address formation.
   No implicit receiver binding is added.
6. Only the clauses below may occur in the anchor's range. Their complete
   required semantics remain required even with DoesNotReturn or apparently
   unused outputs. Bodylessness does not validate a promise, remove a
   capability dependency or permit an unknown clause.

These are anchor rules, not restrictions on the actual native method whose
address a value holds. That declaration retains its own body/import role,
NativeCallable clause where applicable, exports and binding obligations.

## Proposed applicable clauses and indirect-call meaning

All existing clause headers, extents, reserved values, occurrence keys and
cross-row rules remain unchanged.

| Allowed kind | Contribution to identity | Indirect invocation obligation |
|---|---|---|
| `0001` Effects | Assigned declared mask; absent and one explicit zero normalize to zero. Retain `02` DoesNotReturn and `04` caller-unsafe exactly. | DoesNotReturn removes the actual normal completion edge for any declared result type; it does not erase the result type, argument validation or other contracts. Caller-unsafe remains a call-site obligation, not authorization inside a callee. |
| `0002` OutputState | Formal ordinal, exact resolved normal-outcome predicate identity and declared vacant/complete state. | Publish only the selected ordinary checked initialization-output relation, with complete ownership/backing and whole-range rules. No pointer/status heuristic or exceptional-output promise is inferred. |
| `0004` Dependency | Destination kind/ordinal, phase, source kind/ordinal and relationship, including explicit empty-source records. | Preserve source-storage borrowing versus backing forwarding, publication, every-applicable-exit retention and admitted deferred-completion obligations. Receiver sources/destinations require an actual receiver; every selected holder/output must be eligible. |
| `000B` PredicateGraph | Phase 3, phase-local declaration ordinal and the normalized single-node selector/ordinary-result footprint; require exact `10/1` with prerequisites `[]`. | Classify the saved ordinary result at normal Publish under the shared selector contract. Phase-1/2 assertions, general nodes, literals, calls, captures and child edges remain gated. The anchor is not an evaluator, body or contract-checking thunk. |

Raw native function-pointer invocation is caller-unsafe independently of
Effects. **Do not require, inject or infer an Effects `04` row** merely
because `0x0D` occurs. For this candidate's exact declared-contract equality,
masks zero and `04` remain distinct, as do `02` and `06`, although both
members of each pair require unsafe invocation. This deliberately compares
normalized published records, not all promises logically redundant in a
particular call context. The ordinary absent/zero equivalence and zero-mask
omission rule are unchanged.

OutputState keeps ordinary Param mode `00` and its eligible complete
initialization-reference/range types. Resolve `predicateRef` only through
the shared typed `0x0037` reference and exact selector codec selected by the
composition decision; this proposal adds no other target encoding.
PredicateGraph attachment shape/IDs do not complete general node/operand
schemas or Feature registration. Feature 8 is not required merely for an
ordinary checked-output anchor, and its presence does not admit the raw
native-output reservation protocol on arbitrary function-pointer targets.
Missing required predicate interpretation declines affected use rather than
discarding the clause, synthesizing a Boolean convention or weakening the
anchor to Effects-only.

Dependency is not limited to normal-result records. Its existing phases
`1/2/3` retain their ordinary applicability. Phase 3 needs the complete
deferred-result/output and capability carriers; a scalar signature does not
acquire them from a byte value. Explicit empty sources and absence retain
their distinct published meanings; no source rows are merged to invent
relationship `3`. No ownership transfer, pin, retain or lifetime extension
is inferred from the native type.

| Forbidden on the anchor | Reason |
|---|---|
| `0003` Transfer, including zero payloads | No mode has assigned semantic admission; an anchor does not activate it. |
| `0005` Cleanup | TypeDef-owned, not a callable signature clause. Signature types still retain their independently selected cleanup contracts. |
| `000A` EvaluationForm, both Pure and ConstEval | Exact-method evaluation-form authorization, not an ordinary callback-effect type. The anchor has no callable program or form to select; this proposal does not invent an indirect constant evaluator. The admitted selector subset invokes no helper; general selected-call predicates remain gated. |
| `000C` NativeCallable, even with a matching convention | Gives an actual MethodDef the native-callable role; that conflicts with the noncallable anchor. The TypeSpec's convention is not a reference to or substitute for this role. |
| Any other clause, including unassigned initialization roles/demands or unapproved carrier candidates | Requires an explicit later applicability amendment and its complete schema/admission contract; “ordinary” is not an extension escape. |

Excluding declaration roles does not erase them from actual target admission.
No producer may erase an otherwise required invocation fact to fit this set.
A target formation needing an unrepresented required fact declines until its
complete carrier is admitted. A contract-only anchor cannot stand in for the
actual program needed by independently required constant execution.

## Proposed normalization and equality

Resolve MethodSig and Contract references in the anchor MethodDef's declaring
Module and admitted substitution context. Each consumer retains that resolved
meaning; reaching the anchor from another packed Module does not reinterpret
its Imports in the consumer's Module. Shared TypeSpecs still need valid,
identical resolved meaning in every using context. Normalize:

* The native-function-pointer family and original convention byte.
* Complete result TypeSpec identity, zero receiver, and every ordered logical
  Param's complete TypeSpec identity and mode. Retain unit parameters and all nested representations,
  qualifications, nominal identities and type/constant arguments.
* The set of applicable normalized clause records, keyed by clause kind
  and that clause's ordinary occurrence key, with the payload identities
  defined above.

Nested function pointers use this same rule. Row versus eligible inline
TypeSpecRef spellings do not distinguish complete types. Neither equal
machine carriers nor same-spelled nominal declarations establish equality.
Param `implicit` adaptation flags, labels/default source views, anchor name,
visibility, row, physical range order and lexical owner alone are not new
type-identity components. Preserve any retained source view separately.
An ordinary first parameter remains distinct from the forbidden MethodSig
receiver. The anchor has no method-generic formals to alpha-rename.

For comparison, replace each clause's anchor `ownerRow` with the same
**logical signature subject**; this is not a serialized marker. Rebase local
result/formal subjects by role and declaration ordinal. An
anchor-local predicate attachment is identified by its phase and phase-local
ordinal plus its complete normalized graph/footprint, not the owning anchor's
name or row. Its sole ordinary-result root rebases to that signature's result.
OutputState references to that predicate follow the same rebasing.
External type/declaration identities retain their exact resolved identities
and complete substitutions; they are not alpha-renamed. Predicate captures,
literal/helper operands and broader footprints are not admitted by this subset.

This specializes ordinary MethodDef-owned predicate identity **for anchor
type comparison only**. Graphs still have one physical attachment owner and
their ordinary physical canonical ordering. Equal expression occurrences
are not shared, declaration order is not commuted, and changing the closed
test or complete result/root identity changes the normalized contract.
Physical row sorting is not predicate evaluation order. The exact bounded
codec and its complete Feature registration must be selected and supported
before such records can be independently interpreted; a general graph or
private expression codec cannot fill that admission gap.

Construct a comparison inventory in clause-kind/occurrence-key order;
physical Contract row order is immaterial except where a payload explicitly
records logical order. Normalization is not duplicate repair: validate each
owner's occurrence keys, including resolved predicate identities, before
deduplicating records. Duplicate Effects rows or predicate/output keys reject
even when equal.
No theorem proving, arbitrary predicate equivalence, promise strengthening
or directed assignability counts as equality. The existing
[directed binding relation](../specs/representation/binding-and-replacement.md)
remains separate from exact function-pointer type equality. Checked address
formation retains actual native-role eligibility, exact convention and all
required target contracts; an unsafe raw pointer is not proof of provider truth.

### Structural cycles and contexts

Include `TypeSpec 0x0D -> anchor -> MethodSig` and all identity-bearing
structural contract/type edges when checking acyclicity. A cycle through
nested pointers or another signature anchor is still structural and rejects;
the MethodDef indirection is not a recursive-type escape. Retain existing
earlier-row rules for TypeSpec child references where required. Do not invent
an earlier-MethodDef rule for the anchor field.

Resolved nominal declarations are identity anchors, not instructions to
unfold their fields/bodies indefinitely. Valid nominal recursion and shared
acyclic subgraphs remain possible under their own storage/binding rules.
Predicate occurrence cycles remain invalid under the shared codec.
Use complete context-sensitive traversal/comparison keys. An in-progress
structural node is not a successful equality result; complete all required
binding obligations before publishing equality. Exhaustion or unsupported
interpretation declines the claim, not a successful approximate match.

## Canonical writing, remapping and integration gates

Semantic normalization does not require globally interning anchor rows or
producing a unique artifact byte image. Equal anchors may remain physically
distinct. Existing zero-Effects omission, TypeSpecRef spelling, Body-map and
predicate ordering rules continue to govern canonical writing.

A writer that packs, deduplicates or re-encodes must:

1. Resolve complete identities and retain every using Module's original
   requirements and expected bindings. Equal resolved types do not discharge
   distinct retained Import expectations. Clone context-sensitive structural
   rows when one physical row cannot preserve every use's meaning.
2. Remap decoded TypeSpec anchor fields, nested TypeSpecRefs, MethodDef
   signatures/owners, MethodSig/Param ranges and owners, generic-context
   references, Contract owners/ranges and every clause-specific typed target.
   Do not share Contract rows between anchors.
3. When merging equivalent anchors, correspond predicate attachments by
   their normalized phase/ordinal/content; rebuild exclusively owned
   graphs, footprints, roots, nodes and operands and remap OutputState
   references through the selected shared selector codec. Re-sort physical
   predicate rows under the surviving actual owner's canonical order.
   Child edges and captures are not admitted by the bounded subset.
4. Rebuild affected Body TypeMaps, map/record references, containing extents
   and offsets. Preserve optional source/debug references through their
   reference-slot rules, or regenerate/drop that data under the ordinary
   transformation contract. Equal octets are not references to substitute.
5. Keep real callable targets and NativeExport's exact owner-owned `000C`
   references distinct from anchors. Never replace an actual method with a
   signature-equivalent anchor or rewrite the convention to a target alias.

Binding, traversal/rewriting, verification and lowering must carry the same
normalized complete contract; libraries publish it without requiring source
names or bodies. Indirect calls retain caller-unsafety, outcome/dependency
facts and code-lifetime responsibilities. No new source notation, target
mapping, marshalling or private persistence revision follows.

The Development signature-row consumer does not establish Standard anchor
support. This proposal is not implementation or conformance evidence.
NativeBoundaryContracts and all other actually required capability closures
remain admission gates; general predicate/generic/phase carriers are not
completed here. The
[composition decision](predicate-feature-composition.md#proposed-manifest-and-cross-feature-cases)
owns exact selector/anchor/output manifest cases. Within this bounded
decision no further choice is delegated to an implementer.
Normative adoption and the independently missing capability schemas remain
separate decisions, not reasons to guess or erase required facts.

## Proposed conformance cases

These are candidate requirements, not executed results. Unsupported required
semantics decline affected use; malformed selected structures reject before
use under [consumer verification](../specs/representation/reader-trust.md).

| Case | Fixture | Candidate outcome |
|---|---|---|
| FPA-01 Positive | Seven-byte Standard `0x0D`, valid scalar MethodSig, no Contract rows or native declaration roles | Valid anchor without a mandatory Effects or `000C` row; native invocation remains caller-unsafe. |
| FPA-02 Equality | Two anchors differ only in name, visibility, owner location, MethodSig/Param rows and Contract row order | Equal when all resolved signature and normalized contract identities agree; physical deduplication is optional. |
| FPA-03 Boundary | Absent Effects versus one explicit zero; masks `00/04` or `02/06` | First pair is equal; the latter pairs are distinct declared-contract types, all requiring unsafe native invocation. Duplicate zero rows reject. |
| FPA-04 Cross-feature | DoesNotReturn with non-unit result, OutputState or a required unsupported predicate | Preserve declared result and all required contract checks/support; no normal publication. Unreachable outcomes do not license dropping records. |
| FPA-05 Signature | Zero parameters versus one unit parameter; nonzero MethodSig receiver versus a same-typed first Param; changed inner qualifier, nominal type or mode | The receiver form rejects; otherwise distinct complete signatures remain distinct. Reserved modes reject; an allowed source-only implicit-adaptation flag does not change type identity. |
| FPA-06 Generic | Closed constructed parameter/result types; nonempty anchor method-generic range; missing owner or unavailable substitution | Constructed types are eligible with complete required support; nonempty method-generic range is invalid; missing owner rejects and unavailable required substitution declines use. No bound environment is inferred. |
| FPA-07 Contracts | Equivalent anchor-local phase-3 selector graphs with remapped rows; changed closed test or complete result identity; phase-1/2 or helper/literal/capture variants | Row remapping preserves equality; changed admitted test/type identities distinguish contracts. General assertion/graph variants remain unsupported, not alternative equal or unequal supported types. |
| FPA-08 Outputs | OutputState with wrong formal/type/mode, invalid state, duplicate normalized predicate/output pair, contradictory applicable states or unresolved predicate | Reject invalid structures; decline missing required predicate support. No integer-status or Boolean-success guess repairs them. |
| FPA-09 Dependencies | Distinct borrow/forward/union records, empty sources versus absence, absent receiver selected as source/destination, phases 2/3 | Preserve distinct records; receiver mismatch rejects; phases retain every-exit/deferred obligations and require complete carriers. No inferred ownership or lifetime extension. |
| FPA-10 Roles | Anchor has a Body, ordinary Export, NativeExport, NativeImport, Import-callable expectation, `000C` or either `000A` variant | Reject the conflicting anchor use/contract, even with matching convention and signature. Actual native methods keep these roles separately. |
| FPA-11 Body references | Anchor selected by Call/NativeAddress/accessor/cleanup/witness/direct predicate call; function-pointer TypeSpec used by a Body | Reject callable/addressable anchor use; permit the type reference and otherwise supported indirect value invocation. |
| FPA-12 Other clauses | Transfer with all-zero payload, Cleanup on MethodDef, unknown/unapproved clause | Reject; reserved/inapplicable is not merely noncanonical or an extension attribute. |
| FPA-13 Malformed row | Qualifier bits set, convention zero/unknown, zero/out-of-range/wrong-namespace anchor reference, nonzero reference high byte, six-byte or trailing-byte row | Reject exact-schema/extent/reference violation; no Development fallback. |
| FPA-14 Bounds | Highest valid 24-bit anchor index within the actual table; overflowing Contract/range arithmetic or invalid ownership/ordinals | Accept the representable valid reference; reject invalid ranges and owners without wraparound. No extra sample/arity/depth cap is implied. |
| FPA-15 Cycles | Anchor directly or mutually reaches itself through signature/nested structural types; shared DAG; permitted nominal recursive type | Reject structural cycles including pointer-mediated cycles; accept the DAG and separately valid nominal recursion. Never treat “already visiting” as equality success. |
| FPA-16 Convention/identity | Same ABI mapping but different convention byte, or same-spelled types from different selected providers | Distinct identities; no convention cast or same-layout substitution. |
| FPA-17 Re-encoding | Permute/deduplicate anchors and type rows, rebuild predicates, ranges, Body maps and optional source references | Decode to the same complete identities and obligations; retain exclusive owners and source-coordinate meaning. No raw byte substitution or callable-to-anchor rewrite. |
| FPA-18 Binding boundary | Equivalent normalized type used in two Modules with different retained expectations or substitutions; raw foreign code address | Clone rows/preserve every expectation as needed; equality never discards a binding or proves the foreign provider's published truth. |
| FPA-19 Malformed contracts | Nonzero Contract/payload flags or reserved fields, retired/reserved Effects bits, wrong clause extent, orphaned/shared/overlapping ranges or invalid clause targets | Reject the malformed selected structure even when another clause removes its reachable outcomes; no ignored attributes or zero-mask duplicate repair. |
