# Core Count witness selection

> [!IMPORTANT]
> **Status: UNAPPROVED; provisional implementation direction only.**
> This proposal follows the user's instruction: “At a genuine missing
> contract, prepare the smallest concrete decision needed as a new proposal
> doc using gpt-6.1-sol at max reasoning, then continue with it being treated
> as a provisional approval/direction.”
>
> That authorization permits provisional implementation, not normative
> adoption, schema stabilization or a compiler-support claim. This document
> changes no source, tests, specification or existing proposal.

## Decision and preserved contract

Recommend **explicit, per-using-Module Core declaration selection followed
by the existing general nominal interface witness mechanism**. Live Count
is a selected property getter call, not a new registration protocol,
container intrinsic, instruction or witness codec.

The [registered-query owner](../specs/language/registered-queries.md) and
[LCI-05](../specs/libraries/adopted-inventory.md#2-selected-declarations-and-compiler-roles)
already select all of the following; none is proposed anew:

* Dedicated `countof(Place)` syntax and an exact nonnegative `nint` result.
* The shared nominal Core `ICountable.Count` readable instance property,
  with a synchronous `Pure` getter. Unrelated same-shaped properties,
  methods, fields and interfaces do not satisfy that requirement.
* Ordinary readable-place and call authorization. A live observation may
  change with receiver state, including through readonly aliases; Pure
  implies neither cached results nor constant-time access.
* Separately admitted `ConstEval` support. Type/known-shape counts use fixed
  cardinality evidence, do not invoke a getter and need no live witness.
* Retention of the exact requirement, property/getter, implementing witness,
  complete receiver type, effects and dependencies through later use.

The genuine missing selection is **which authorized Core declaration and
property supply those roles in a particular Module, and how that selection
reaches source-independent role checking**. General witness transport and
dispatch still have shared codec/admission gates; the query does not need
a second representation of those facts. The concrete built-in live
implementation catalog remains unselected.

## Owners and inspected implementation boundary

| Owner or evidence | Consequence |
|---|---|
| [Registered queries: identity and retained contracts](../specs/language/registered-queries.md#identity-and-authority) | The logical Count requirement is selected, but its witness codec and built-in dynamic implementations are expressly separate decisions. |
| [Nominal witnesses](../specs/language/interface-witnesses.md#nominal-conformance-and-member-identity) | Conformance belongs to the implementing declaration; requirements retain their declaring interface/member identities, selected implementations/defaults and complete contracts. Matching members cannot create conformance. |
| [Core identity and provider admission](../specs/libraries/adopted-inventory.md#1-status-and-identity) | Core denotes a designated authority, not a fixed package/namespace identity. The selected profile must supply missing exact identity fields before affected use. |
| [Logical witness records](../specs/representation/logical-records.md#nominal-and-qualified-self-witnesses-assigned-families-codec-gates-remain) | The shared typed InterfaceWitness family already covers nominal and privileged built-in implementers; reference/projection codecs and the dispatch ABI remain independently gated. |
| [Query representation](../specs/representation/logical-records.md#registered-query-contracts-and-representation), [query lowering](../specs/il-instruction-set.md#registered-query-lowering) | Live Count uses ordinary bound-call machinery, not the `DE` layout-query family. Feature 13 alone supplies no Count witness or generic dispatch support. |
| [Binding and replacement](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions) | Bind exact identities before directed contract comparison. Property binding includes its referenced getter; an independently exported getter key is not required. |
| [Producer truth and consumer checks](../specs/representation/reader-trust.md) | Required structure, support and binding checks precede use. Consumers need not semantically re-prove a provider's published getter behavior. |
| [`Parser.cs`](../../sources/Terrum.CodeAnalysis/Syntax/Parser.cs), [`ParserTests.ParsesRegisteredTypeAndLayoutQueries`](../../tests/Terrum.CodeAnalysis.Tests/Syntax/ParserTests.cs) | Dedicated query syntax and parser cases exist. These are syntax evidence, not semantic Count support. |
| [`Binder.BindExpression`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | The inspected switch has no query-expression cases; its default reports unsupported executable expressions. No `ICountable` implementation occurs in the inspected compiler/test sources. |
| [`Binder.Interfaces.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.Interfaces.cs), [`BoundInterfaceContract.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundInterfaceContract.cs) | Compilation-local nominal getter witnesses exist, but `TryBindInterfacePropertyGetter` currently refuses member/accessor attributes and contract tokens. Supporting the selected Pure getter is necessary general witness work, not a new Count contract. |
| [`InterfaceDeclarationBindingTests.QualifiedInterfacePropertyReadsUseGetterWitness`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/InterfaceDeclarationBindingTests.cs) | The active test checks a plain int32 getter call's InterfaceCallIdentity and includes JIT execution. It does not cover Core selection, Pure Count, nint Count or persistence. |
| [`TrRmInterfaceWitnessTables.cs`](../../sources/Terrum.CodeAnalysis/Modules/TrRmInterfaceWitnessTables.cs) | Assigned typed witness readers/writers check type references, owned ranges and MethodDef bounds. Their presence does not establish Core designation, provider satisfaction or executable witness support. |

These are source-inspection observations, not results from running tests.
The [Core-library reference](core-library-reference.md#registered-type-and-layout-queries)
also mentions `NoThrow` beside Count. The accepted query owner does not select
that additional declaration-wide promise. Retain the accepted
[Pure evaluation rules](../specs/language/published-predicates-and-effects.md#pure-means-complete-input-state),
including their valid-input pure-form no-escaping-failure obligation; do not
import the reference proposal's extra NoThrow wording.

## Alternatives

| Route | Assessment |
|---|---|
| **Exact Core selection plus ordinary nominal witnesses — recommended** | Supplies the missing authority-to-declaration handoff once, while reusing property/getter identity, conformance, contract satisfaction and call machinery. Shared codec work benefits every interface, not only Count. |
| A dedicated CountWitness schema or query-specific registration | Would still need the same Core authority, complete implementing/interface identities, property/getter correspondence, contracts and provider binding. It duplicates that graph and its validation/remapping rules without representing an otherwise missing Count fact. No new carrier is justified. |
| Compiler-known container adapters or name-only Count lookup | Does not establish nominal conformance or provider authority. Reject rather than move library policy into the compiler or invent a general source Query schema. |

## Candidate Core selection handoff

The proposed **nonserialized** `CountableCoreSelection` is one immutable
binding input per using Module that binds or validates a retained live
Count role. Its semantic fields are exactly:

| Field | Required meaning |
|---|---|
| `usingModule` | The originating logical Module and its immutable source/artifact binding context, not the caller's current default or the first packed Module. |
| `countableInterface` | The exact public primary nongeneric nominal interface declaration designated by the selected Core authority for ICountable, with its originating provider snapshot and complete declared requirement closure. |
| `countProperty` | The exact public readable instance PropertyDef belonging to that same interface and designated for Count. Its value is exactly reserved `nint`; it has no index parameters. Derive the requirement getter from this property, not a second independent getter selection. |

These are resolved declaration identities, not strings to search for.
For external declarations, ordinary binding resolves an exact
`(Module.identity, exportKey, definition kind)` and preserves the using
Module's original expected declaration graph. Source-local identities
belong to their actual primary declarations and source Module/snapshot;
do not fabricate external keys or resolve by namespace spelling.
The selected provider artifacts, exact schemas/revisions, governing rules,
target/runtime requirements and original expectations remain accompanying
ordinary binding inputs, not additional Count wire fields.

The Core authority/profile supplies the selection. A referenced package,
an interface implementer or a serialized self-assertion cannot authorize
it. Validate the selected interface/property ownership, accessibility,
complete `nint` type, synchronous getter and supported published Pure
contract before binding a use. A matching attribute name is not that
contract. Ordinary signature, safety, receiver/access and dependency
checks still apply.

Require exactly one coherent selection for each affected using Module.
Missing, conflicting, unauthorized, unresolved or unsupported selections
decline affected use; never try a same-named interface, another Count
overload, import-order winner or a built-in fallback. Type/known-shape
counts and unrelated operations need no selection.

Keep the originating selection with imported retained Count semantics.
Packing or a new host Core default cannot relabel an old witness. Distinct
export keys resolving to the same declaration preserve identity while
retaining every original expectation; equal spelling in another provider
does not. At a boundary requiring the shared Count role, preserve that
same resolved interface/property identity or use an explicitly authored
nominal adapter with its own checked conformance.

This is the complete proposed role handoff, not a general Core catalog or
a selected CLI/configuration file format. An API may use checked handles
for these three fields; their identity and ownership semantics do not
depend on the C# type name.

## Witness selection, calls and interchange

For a readable Place, select its already declared nominal conformance to
the selected ICountable, including inherited conformance under the ordinary
rules. Resolve the exact Count property getter requirement and its one
selected implementation/default. Generic bodies require their ordinary
published constraint evidence; do not discover a conformance by examining
one later concrete instance. Qualified receivers retain their complete
types and use only already admitted call adaptation.

Capture the receiver Place once under ordinary call staging. Preserve
initialization, access, loans/backing, ownership and the getter's published
evaluation/effect contracts. Repeated queries are repeated observations;
they cannot be cached merely because the same receiver address recurs.
Lower to the ordinary selected getter call while retaining the requested
interface, exact requirement/property/accessor and selected witness/target.
No field named Count, container descriptor, sample name or inferred layout
can replace those facts.

Reuse these existing carrier families without changing their payloads:

| Carrier | Retained fact and boundary |
|---|---|
| ModuleRef, Import/Export, TypeDef/InterfaceDef | Exact Core interface origin and original expected contracts, resolved independently of declaration names. Core authorization remains external to these rows. |
| PropertyDef and its getter MethodDef/MethodSig/contracts | Exact property-to-getter correspondence, readable instance shape, nint result and complete Pure/effect/result obligations. Ordinary property binding can establish getter correspondence without inventing a getter export key. |
| Complete TypeSpecRef, InterfaceWitness, WitnessMember | Exact implementing/interface pair, declaring getter requirement, selected owner/target/default and the whole required witness closure. The selected property remains in the retained declaration graph; a bare method name is insufficient. |
| Ordinary call and dependency representations | Receiver staging, selected call/witness evidence, complete types, surviving contracts, provider/code origins and optional constant-execution dependencies. A direct selected target alone cannot erase a retained witness obligation. |

The assigned WitnessMember method fields are **MethodDef rows**, not Import
tokens. Imported expectations must use the existing checked declaration
binding and owner correspondence; inserting a token into a row-only field
is not an extension. Complete call/witness reference, remapping and dispatch
support must exist for the selected route. If it cannot preserve a required
fact, decline that emission/loading/lowering route rather than erase it.
Structural-only inspection may still report its narrower result.

After source binding, an ordinary getter call needs no fresh Core search
at runtime. A source-independent operation claiming to validate the retained
live-Count role needs the originating selection context above, alongside
the ordinary bound graph. Merely executing an already admitted ordinary
call does not rediscover its role from the consumer's Core defaults.

No Count-specific table, Body record, Feature, intrinsic registration,
opcode, numeric assignment, private revision or compatibility alias is
proposed. The shared [private witness proposal](source-interface-witness-private-persistence.md)
already owns a distinct unapproved transport candidate; this proposal
neither adopts it nor changes it. Missing shared witness/reference/dispatch
contracts remain with their existing owners, not a parallel Count schema.
Exact [draft-schema agreement](../specs/representation/format-versioning.md)
and complete admission still precede independent exchange.

## Built-in implementations and the result boundary

**Select no mandatory built-in live implementer for this slice.** An authored
nominal type with an explicit checked conformance is sufficient to exercise
live Count. Future Core-owned primitive/family implementations use the same
general witness mechanism and its privileged declaration authority; they
cannot be fabricated by recognizing fields or giving primitives dummy
nominal TypeDefs. The concrete built-in conformance catalog remains a
separate decision, not an implicit promise about Span, TextSpan, arrays,
List or LinkedList.

The selected fixed Type/known-shape forms remain independent of live
witnesses. Literal text counts use the selected encoding's logical code
units. A future live `TextSpan<void>` implementation must retain or safely
recover its encoding evidence; this proposal selects no erased text
descriptor, hidden encoding default or dynamic collection representation.

The result domain `0..nint.MaxValue` is already fixed. Existing chapters
select no Count-specific negative-result exception or mandatory query-side
guard. Recommend **no additional query-side check or failure category**:
nonnegative Count is a successful-result obligation of the selected getter
and its producer, not a hidden caller precondition. A provider with wider
internal cardinality must establish representability before successfully
publishing the count; no truncation, wrapping, saturation or sentinel is
permitted. Known-invalid source/constant results cannot establish a valid
witness/result.

An actually declared runtime ensure that completes false already follows
the existing [PostconditionException rule](../specs/runtime/standard-failures.md#2-runtime-postconditions);
this proposal does not synthesize such a clause or infer that exception
from arbitrary negative bits. In a selected pure form, the ordinary
valid-input no-escaping-failure obligation still has to hold. A producer
publishing a false nonnegative semantic fact violates the existing producer
contract; consumers are not required to re-prove its body or guarantee
recovery from that lie. A universal runtime guard would be a further
contract amendment, not an implementation prerequisite for this route.

## Qualification and adoption boundary

These are required future cases, not tests run by this documentation change.
Use public compiler/metadata APIs and unrelated synthetic implementers,
not compiler-owned container names.

| Case | Expected boundary |
|---|---|
| Exact selected interface/property; direct or inherited declared witness | Bind the correct getter with its full identity; observe zero and target nint maximum exactly. |
| Same-shaped property through another interface, or a method/field/indexer | Reject as a Count witness, even if names and current values agree. |
| Wrong result type, static/async getter, missing Pure contract or extra caller obligations | Reject incompatible role/witness binding; do not adapt by numeric conversion or unsafe permission. |
| Two live reads with an intervening admitted mutation; readonly alias | Observe current state under the ordinary access/effect rules, not a cached shape or receiver address. |
| Vacant/moved/unreadable Place or unsupported generic/projection evidence | Diagnose the ordinary missing authorization/evidence; no automatic owner, boxing or witness search. |
| Missing/conflicting selection; correct role with wrong property owner/getter or incompatible provider expectation | Decline before affected use. A structural metadata pass alone is not selected-role admission. |
| Known negative or unrepresentable result; authored false ensure | Do not publish a valid Count result; retain the existing ensure failure when that clause is actually present. Do not add a query-specific exception. |
| Structurally valid but semantically false provider fact | Producer violation under reader trust, not a required consumer body audit or promised dynamic repair. |
| Local and independent provider/consumer builds; row permutations, export aliases, packing and replacement | Preserve exact role, property/getter, witness, complete types, original expectations and actual origins; reject incompatible changes and missing required carriers. |
| Type/known-shape Count without Core selection; reached required live Count | The former needs no getter; the latter needs the exact getter's separately admitted constant form/program, values and resources. Pure alone is insufficient. |
| Dynamic built-in or erased text lacking a selected conformance/encoding carrier | Decline live Count; do not infer a witness from layout, a Length property or a guessed encoding. |

If adopted, the selection handoff belongs in registered queries' **Identity
and authority** section, with a reference from **Retained contracts and
compatibility** and LCI-05. The logical-record query section should point to
the shared witness families and retain their independent codec gates.
The source witness, property-binding and Pure owners remain unchanged;
no grammar or IL allocation amendment is needed. The Core-library
reference's extra NoThrow wording should be reconciled to those owners,
not treated as authority by this implementation.

### Implementation dependencies

1. Continue accepted Type/layout/known-shape query bound-IR work independently;
   none requires this live Core selection.
2. Supply the provisional per-Module selection through checked source/reference
   binding; the production provider identity/keys are still unselected.
3. Generalize existing property-witness binding to preserve and check the
   selected Pure/result contracts. Carry the exact Count role and witness
   through shared bound-tree traversal/rewriting, mandatory verification and
   ordinary getter lowering; use synthetic nominal conformances first.
4. Admit persistence and source-free execution only when the selected shared
   witness, property/getter, call, evaluation and dependency representations
   are complete. Existing general codec decisions remain prerequisites;
   do not allocate a Count-only escape hatch.
5. Qualify the cases above at each claimed layer. Production built-in
   conformances and erased-text representation/encoding support remain
   separate, explicitly unselected work.
