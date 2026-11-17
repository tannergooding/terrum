# Pure callable contract composition

**Status: unapproved; provisional direction under the authorized
missing-contract workflow.** This is a bounded record-ownership decision,
not normative adoption, a registered capability, a private-format revision
or a compiler-support claim.

## Decision and baseline

The accepted contract has **no complete, admitted persistent carrier today**.
An assigned identity is not a field schema or complete capability.
Recommend the smallest completion: make the logical callable record an
explicit **composition of MethodDef-owned contracts**, using the existing
unapproved `0051 EvaluationReadBound` candidate for the missing pure allowed-read
attachment. Do not invent a second output/ownership schema, enlarge Effects,
or introduce an ordinary-form failure-bound carrier.

| Owner | Established representation and limit |
|---|---|
| [Published predicates and pure evaluation](../specs/language/published-predicates-and-effects.md#read-roots-and-effect-records) | Requires identity/rules, signature/domain, reads, writes, outputs, transfers/retention, activity/failure, entry obligations and availability. Ordinary effects may be unknown; a claimed pure form may not have unknown allowed reads. |
| [Logical records](../specs/representation/logical-records.md#published-predicates-and-evaluation-forms) | Assigns MethodDef clause `000A`, its 12-byte marker and tags, but no callable allowed-root attachment. Its graph-owned actual footprints are not that attachment. |
| [Registry](../specs/representation/trrm-1.registry.json), [metadata](../specs/il-metadata.md#section-kinds-and-row-layouts) | Assign `0038..003A` footprint/root/direct-dependency shapes and `0042..0049` request/value-related shapes. Features 10–12 remain registration-pending. These assignments do not encode all required callable facts. |
| [Clause owner](../specs/representation/contract-clauses.md) | `0001` carries only DoesNotReturn/caller-unsafety; `0002` supplies conditional normal output states; `0004` supplies backing/retention, not ownership transfer. `0003` remains inactive. Some predicate assignment prose is stale relative to the registry; it supplies no alternate encoding. |
| [General predicate graphs](general-predicate-graph-schema.md#evaluationform-and-callable-allowed-reads) | Already proposes `0051`, exclusive callable footprints, per-form `000A` keys and Feature-17 composition. It is an unapproved candidate, not an accepted assignment. Reuse it rather than allocate a competing record. |

### Exact owner integration and supersession

Amend **only** the meaning of “an admitted record preserves” in the logical
record owner's evaluation section: it means the complete composite below,
not that every listed logical field needs another serialized field.
Incorporate the companion's callable-read attachment and its occurrence,
reference and admission rules when adopting this direction.

This proposal owns that composition and its absence/availability boundaries;
the general-graph proposal continues to own graph nodes and the `0051`
candidate codec. It changes neither that candidate's bytes nor its shared
true/false and true/true read bound. It supersedes no accepted purity,
constant-execution, output, ownership or failure behavior. Preserve the
separate [NoThrow retirement](no-throw-retirement-and-proof-boundaries.md),
[Core schema selection](core-pure-schema-identity-selection.md) and
[mode-property selection](core-evaluation-mode-query-identity-selection.md)
directions without approving or replacing them.

No new section, clause, Feature, DefId, TypeSpec form, opcode or private tag
is proposed. `0051` and Feature 17 remain **unapproved existing candidates**.
The current accepted registry, assignments and statuses remain unchanged.

## Complete logical callable record

The logical key is the **resolved declaring MethodDef, complete using/
substitution context and selected form**: Ordinary, Pure or ConstEval.
Property access selects its actual getter MethodDef, not a property-owned
effect record or a synthesized getter name. An imported expectation has its
own complete composite; it is not overwritten with provider rows.

| Required component | Exact owner and representation under this recommendation |
|---|---|
| Identity and rules | MethodDef/declaring TypeDef or module scope, original Import/ModuleRef/export correspondence, complete substitution, Module governing edition and exact effective Feature/rule catalog. `000A` authorizes its selected form; it is not an identity string or trust certificate. |
| Signature | MethodDef's required MethodSig, receiver, ordered Param range, complete TypeSpecRefs and generic formals. Preserve zero-data positions and initialization destinations. Constructor/accessor roles additionally need their own admitted role facts. |
| Domain | The same declaration's preconditions (`000B`, phase 1), constraints, complete access/validity, output-vacancy and formation obligations. No implicit narrower pure domain or new form-specific precondition is invented. A required eligibility/separation fact outside these supported carriers gates use. |
| Allowed reads and direct dependencies | One required `0051` attachment and its `0038..003A` footprint for either selected pure mode. It is the conservative union over the valid-domain pure forms, not one executed request's trace or a predicate's footprint. |
| External writes | For Pure/ConstEval, the accepted selected-form contract fixes the bound to **none**. Fresh private/result construction is not an external write. There is no serialized write mask to default or infer. Ordinary form has an unknown write bound here. |
| Outputs | MethodSig's result and init destinations, unconditional complete-on-normal-return rules, the owner's `0002` conditional relations and `000B` normal postconditions. These retain exact live/vacant/value relationships and phases in every form; no copied output range is added to `0051`. Exceptional state is not invented from a normal OutputState row. |
| Transfers and retention | Complete types, explicit Body/call transfer, responsible holders and selected Cleanup contracts, plus `0004` for its exact source/backing/retention relations. `0004` is not a transfer flag. Missing ownership, provenance, retirement or publication representation remains a gate even for a pure result. |
| Activity and failure | Pure/ConstEval derive the accepted exclusion of forbidden activity and escaping language failure **on valid inputs in that selected evaluation**. This does not prove termination, erase invalid-domain checks or suppress argument preparation, failed requirement creation, entry demands or surrounding cleanup. Ordinary activity/failure remain unknown, apart from independently published role contracts and assigned Effects facts. |
| Entry obligations | The owner's actual static-initialization demands and separate authority, publication and code/load-lifetime contracts. They are followed, not represented by an empty list in `0051`. Body purity cannot discharge them. Missing mandatory entry carriers prohibit affected independent use. |
| Availability | MethodDef's own Body, or an exact separately admitted intrinsic implementation/attachment, with its actual type/value/target and dependency closure. `000A`/`0051` contain no program reference. A concrete `0042` request selects Body or IntrinsicRegistration through its assigned tag-selected reference, not through the read bound. |

This is a resolved semantic projection, **not an additional wire record or
public API shape**. Implementations retain typed identities and these distinct
components through bound-tree traversal/rewriting and producer verification.
There is no required ordinary summary in this candidate: absence is
conservative unknown, never empty reads, no writes or ordinary NoThrow.
More precise ordinary effects require a separately reviewed carrier.

Pure's fixed write/activity bounds need no redundant encoded zeros.
Conversely, no general fallback treats an unrepresented output, ownership,
entry or availability obligation as one of those fixed bounds.

## Exact allowed-read attachment

Select the existing general-graph candidate unchanged:

| Record | Candidate shape and ownership |
|---|---|
| Contract `000A`, 12 bytes | Header `u32 ownerRow, u16 clauseKind=000A, u16 flags=0`; payload `u8 formTag, u8 flags=0, u16 reserved=0`. MethodDef owner; `1=Pure`, `2=ConstEval`. Adopt the companion's key `(owner,000A,formTag)`, deliberately replacing the current registry's owner/kind-only key. Each tag occurs at most once; ConstEval implies pure support. |
| Required section `0051`, stride 12 | `u32 ownerMethodDefRef, u32 readFootprintRef, u32 flags=0`. Exactly one row per actual callable having either `000A` tag, including bodyless expected declarations; none for an unmarked callable or noncallable signature anchor. Both tags still use one bound, not duplicate attachments. |
| `0038`, stride 16 | `u32 rootFirst, u32 rootCount, u32 dependencyFirst, u32 dependencyCount`, exclusively owned by that one attachment or by one graph, never both. The ranges target `0039` and `003A`. |
| `0039`, stride 8 | `u8 rootKind, u8 flags=0, u16 reserved=0, u32 rootIdentity`. Callable bounds use only the accepted kinds 1–7 below, not graph-local PhaseCapture kind 8. |
| `003A`, stride 8 | `u8 dependencyKind, u8 flags=0, u16 reserved=0, u32 dependencyRef`. Kinds are `1=MethodDef`, `2=Constraint`, `3=InterfaceWitness`, `4=WitnessProjection`; the tag fixes the required target table. |

All multibyte fields are little-endian. Owner, footprint and dependency
references are required typed `u32` rows in `1..00FF_FFFF`, high byte zero,
within the named table. They are not DefIds, Import tokens, addresses or
Body-map indexes. The `000A` owner is likewise a typed MethodDef row.
An imported MethodDef dependency names the consumer-local expected declaration
and follows its Import; it cannot bypass original binding obligations.
An unavailable WitnessProjection or other target codec does not become an
InterfaceWitness reference.

Root kinds retain their exact payloads: `1=receiver`, `5=ordinary result`
and `7=evaluation mode` require zero; `2=input value`, `3=input state` and
`4=logical output` carry zero-based signature parameter ordinals, with kind 4
restricted to actual initialization destinations; `6=immutable data` carries
a required typed `0041` row. Receiver/result applicability follows the actual
signature and role, including a supported initialization-only receiver.
Immutable identities retain their independently admitted declaration,
substitution, publication and load-lifetime records.

Ranges are `(0,0)` exactly when empty; otherwise their nonzero first row and
mathematical last row fit the typed table and its 24-bit capacity. A zero-root
bound still has its required footprint row. Root/dependency entry ranges are
exclusive and disjoint across owners, even when entries have equal meanings.
Reject orphan/shared footprints and entry rows, duplicate semantic roots or
direct dependencies, unknown tags and nonzero reserved fields. Check extents
with nonwrapping arithmetic.

The bound covers reachable mutable backing, contracts and cleanup as well as
direct expressions. Whole roots are a permissible conservative granularity;
no private field/range selector or imported layout inspection is introduced.
Direct dependencies remain a unique set; follow their own contracts for
closure, rather than copying the transitive call graph into every footprint.
Call substitutions and actual argument mappings remain with their Body or
predicate-call owner. Predicate actual footprints, constant executed
dependencies, layout observations and backing/retention remain separate.

## Admission, truth and availability

Select this composition only with exact out-of-band agreement on the entire
general-graph candidate and its governing semantics. It introduces no
on-wire selector or automatic compatibility with an older draft bearing the
same label, `(1,0)`, Feature list or twelve-byte row.

Use the companion's **unapproved** catalog: selector-only `10/1 -> {}`,
scalar `11/1 -> {}`, general `17/1 -> {10/1}`, and still-incomplete
`12/1 -> {11/1,17/1}`. Require 17 for retained `000A`/`0051`; ConstEval also
requires 12. Pure-only publication does not itself require 12 or a frozen
value codec. This does not change the accepted registry's different pending
prerequisite lists.

Compute complete closure per using Module, including uncalled/bodyless
expectations and retained obligations after folding. Add conditional
requirements for actual types, witnesses, outputs, FieldPath/layout queries,
immutable identities and static/runtime contracts through their own owners.
For example, the separate [ImmutableData candidate](immutable-data-feature-admission.md)
requires 19 for root kind 6; scalar 11 or general 17 alone cannot admit it.
Another Module's Features cannot supply the using Module's missing closure.

Producers establish truthful finite bounds, valid pure forms, mapped inputs,
output state, backing/ownership and every-exit cleanup against authored source
or admitted producer inputs. Consumers perform mandatory safe-consumption
checks of schema, extents, owners, keys, reference namespaces, roles,
signature ordinals, complete support and directed binding before use.
They may rely on those published semantic facts without re-proving bodies;
optional purity/effect audits remain separate.

A bodyless published Pure contract can be used as a checked call/proof
contract without exposing its implementation. It does not enable execution
without a selected implementation. Independently required ConstEval needs
the actual eligible retained Body or complete admitted intrinsic program,
input/value/target dependencies and resource rules; a marker, read footprint,
empty/throwing stub or provider name cannot supply them. Pure alone does not
promise a constant codec. Feature 12 and intrinsic recipe/attachment
completion remain separate gates, not fields an implementer may invent here.

## Binding, rewriting and cross-layer consequences

Compare every retained expectation in its original Module/formal/form
context. Preserve selected forms, exact signatures, domain, outputs,
ownership/phase obligations and mode-dependent behavior. A provider read bound
may narrow only under the supported root/backing containment relation after
mapping staged receiver/formals; unresolved alias/containment proof does not
pass. Adding required reads or removing a retained form is incompatible.
Equal names, raw rows or a currently pure replacement body are insufficient.
Code/value/target dependencies used by folding and documented constant-result
promises survive a still-pure replacement.

Remap decoded MethodDef owners, signature contexts, clauses/ranges, footprints,
roots, immutable identities and direct dependency targets by their typed
namespaces. Ordinals stay logical parameter positions. Preserve separate
original expectations even when exports alias one provider. Rebuild exclusive
ranges, variable-table offsets, directory extents and each Module's manifests;
sort root/dependency sets by kind and complete resolved identity for canonical
writing. Ordinary valid permutations remain admissible. Body transformations
also rebuild affected maps, blocks/records and SourceMap slots; never substitute
coincident bytes or serialize transient analysis revision/loan tokens.

Source syntax and Core authority are unchanged. Normalize checked forms only
after exact schema binding and producer verification; do not infer them from
names or serialize host authorization. A surviving schema-selection or
mode-intrinsic attachment dependency needs its own complete admitted context,
or publication declines. Already normalized contracts do not repeat source
authorization lookup.

Private `.trm`, `.trmn`, sets/graphs and precompiled images gain no codec from
these Standard candidates. Keep their refusal gates until an exact separately
selected format can preserve all surviving facts. Checked source-local
evaluation or a frozen nonexported helper may remain compilation-local where
no such dependency survives. Native folding/reuse must retain entry demands,
every requested output initialization/transfer, ownership and code/load
lifetime; emission alone is not runtime or independent-reader evidence.

## Alternatives and acceptance

**Recommended minimal amendment:** the explicit composite above plus the
already proposed `0051` attachment. It supplies the missing allowed-read owner
without duplicating signatures, predicates, outputs, retention or programs.
This intentionally retains unknown ordinary effects and conditional gates.

A substantive universal per-form effect record could instead carry ordinary
read/write regions, activity/failure bounds and independent per-form domains,
outputs and programs. That is a coherent future redesign, but requires new
applicability, region, phase, ownership, failure and codec decisions, including
whether to restore a modular ordinary no-failure promise. It closes no
additional requirement needed for this bounded pure-contract composition.
Extending `000A` with a footprint pointer instead would replace its assigned
shape and the existing candidate attachment without avoiding those gates.

Required positive/negative/boundary tests, **not results from this document**:

| Case | Required outcome |
|---|---|
| Pure-only, ConstEval-only and both tags; getter, bodyless expectation and eligible constructor | Preserve exact owner/form identity; one bound for both selected modes; ordinary form remains distinct and unknown. |
| Empty bound, ordinal 0, unit result/inputs and complete fresh outputs | Explicit empty footprint remains required; no lost zero-data position or fabricated default; each requested output initializes exactly once. |
| Aliased checked inputs, copied handles and immutable backing | Include actual reachable state/dependencies; readonly or equal descriptors do not prove stable reads. Unrelated writes may preserve reuse, possibly aliasing writes invalidate it. |
| Conditional output, retained backing, unique result and entry initialization | Preserve each independent contract and cleanup; absent unsupported carriers decline rather than infer empty obligations from Pure. |
| Missing/duplicate attachment or form tag; orphan/shared ranges; bad high bytes, ordinals, target kinds or reserved fields | Reject structurally, including uncalled declarations. Same-shaped graph footprints are not callable bounds. |
| Impure ordinary branch guarded out of the selected pure form | Ordinary effects remain; guarding only out of ConstEval cannot excuse effects in true/false Pure mode. Pure-only is not required-constant opt-in. |
| Imported provider replacement, several original expectations, reordered tables and re-emission without source | Preserve each identity/form/bound/output/entry/code dependency; reject widened reads, changed domain/state, missing forms and stale computed answers. |
| Exact last valid row/range, overflow/truncation, bounded cyclic dependency traversal and exhaustion | Validate mathematical bounds and complete finite context-sensitive closure; exhaustion is not successful comparison. No portable numeric minimum is assigned. |
| Wrong draft/catalog, missing per-Module closure, private-format round trip or missing constant program | No decoder guessing, runtime fallback, body-truth re-proof requirement or false support claim. |

Current code does not establish this publication contract:
[BoundFunction](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs)
retains schema bindings; the
[Pure verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.PureEvaluation.cs)
checks a bounded source form, not a persisted complete summary.
[Candidate table code](../../sources/Terrum.CodeAnalysis/Modules/TrRmGeneralPredicateTables.cs)
and [its tests](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGeneralPredicateGraphTableTests.cs)
exercise `0051` framing, not complete callable semantics.
[ModuleCompiler](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs)
and [TRRM production](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs)
decline exported forms they cannot retain.
[ConstEval module tests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/ConstEvalModuleTests.cs)
exercise private nonexported helpers and runtime exports, not independent
ConstEval publication.

Remaining dependencies are adoption of this composition and the selected
companion schema, implementation/qualification across producers and independent
consumers, and each actually used predicate, role, ownership/reference,
static/publication, schema/intrinsic or constant-program/value codec.
No new ordinary-effect policy or numeric allocation is left to implementation.
Do not declare full Pure/ConstEval persistence or milestone closure from the
bounded record decision.
