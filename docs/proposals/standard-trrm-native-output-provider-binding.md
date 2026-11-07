# Standard TRRM direct NativeOutput provider binding

**Status: UNAPPROVED; provisionally actionable under the authorized
missing-contract workflow.** This is implementation direction, not normative
adoption, implemented support or execution evidence.

## Decision and scope

Complete Standard selected-provider binding for **direct calls to a
provider-owned callable NativeImport with NativeOutput cells**. Retain the
consumer's original declaration separately from the selected provider
declaration; compare their complete normalized foreign contracts before use.
Use **equality for the foreign descriptor and raw output protocol**, including
dependency source permissions. The actionable F slice has **no NativeParameter
contracts**; ordinary input formals and target-only output dependencies remain
supported under their existing rules. NativeParameter comparison is a separate,
conditional F+S path below, not a prerequisite for F NativeOutput binding.
The complete signature type closure is limited to the context-free built-in
and raw-data-pointer forms below; nominal/provider-dependent binding is a
separate extension.
Keep the existing directed relation for other supported obligations. This
selects no general native-protocol subtyping or implication procedure.

The [cell contract](aggregate-native-outputs.md) and
[bounded predicate composition](predicate-feature-composition.md) remain the
semantic owners. This proposal supplies their direct-provider integration,
not new output semantics. It supersedes only the nonempty-output exclusions
and NativeAddress-shaped provider requirements in
[selected-provider consumption](standard-trrm-selected-provider-consumption.md)
and [provider-native lowering](standard-trrm-provider-native-lowering.md)
for this slice. A declaration-only provider needs no dummy caller or anchor.
Other retained content still needs independently supported admission.

There is no existing Standard-specific direct-output binding proposal.
The [private provider proposal](native-output-direct-import-provider-binding.md)
owns TNPK/TNSP/TNGP version 3 only and remains unchanged. Do not copy its
expectation table, private type indexes, DLL-name comparison or leaf encoding.
Foreign-interface identity/NativeVTable, checked-reference outputs, indirect
foreign calls, resource adoption and new deployment formats are outside this
decision.

## Exact schema boundary and alternatives

Select each complete artifact independently, before schema-dependent reading,
writing or binding:

| Exact out-of-band identity | Applicable composition |
|---|---|
| `trrm/1-draft-foreign-invocation-contracts-1` | Exact F selection from [draft-schema selection](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities). This task's operation admits shared OutputState/Dependency, bounded selectors and DF/record `0C`, but does **not** select NativeParameter `000E` or required Feature 16. |
| `trrm/1-draft-foreign-invocation-storage-addresses-1` | Exact F+S selection from [storage-address formation](standard-native-storage-address-formation.md#exact-schema-and-capability-selection), preserving EC/record `0E` and its initialized-backing requirements. NativeParameter provider comparison remains conditional on the separate decision below; EC alone does not authorize it. |

Use the full identities: the repository's **fixed-array A2** is separately
named `trrm/1-draft-foreign-invocation-storage-fixed-arrays-2`, not an alias
for F+S. This proposal selects neither that composition nor A3, L1, C+F,
F+G, C+F+G or Development. A separately supplied F provider can bind an F+S
consumer only after both selected schemas and all retained uses are supported;
that mixed selection does not admit NativeParameter. One artifact never
mixes per-Module decoders.

| Alternative | Disposition |
|---|---|
| **Complete existing Standard binding; reuse its records** | Recommended. F already selects the cell owner's binding/preservation obligations. This completes admission and comparison without changing bytes, catalog meanings, allocations or schema identities. |
| New exact provider-output composition | Necessary if adding an ownership carrier or changing a selected protocol/catalog; not needed merely to implement binding already required by F. A second name with identical meaning would add an unnecessary selection surface. It cannot retroactively supply missing nominal ownership evidence to F/F+S. |
| Private-table conversion or universal foreign descriptor | Adds unrelated persistence, identity and routing contracts. Neither is selected, inferred or used as a fallback. |

Keep `(1,0)`, existing reference namespaces and per-Module catalogs unchanged.
Every retained raw-output expectation or declaration requires effective
`8/1`, deriving `{1/1,2/1,10/1}`; direct calls additionally require `3/1`.
Neither an output alone nor this selected binding slice requires or selects
16, 4, 5, 11 or general-predicate 17. Actual independently admitted uses retain
their additional requirements. Required NativeParameter/16 content fails the
scope gate below rather than acquiring support from the manifest.
Features, filenames, equal version bytes and successful parsing never select
a decoder or repair another Module's missing requirements.

### NativeParameter scope and separate decision

The current [composition draft](trrm-draft-schema-profile-selection.md#exact-compositions-and-identities)
lists candidate `000E`/`16/1` in its F recipe, while the
[pointer-contract owner](standard-native-import-function-pointer-contracts.md#candidate-nativeparameter-row)
proposes that still-unapproved carrier. This is adjacent candidate text, not
authorization to implement NativeParameter in this task's F operation.
Preserve the exact schema selection; decline unsupported required content.
Do not redefine F's decoder/catalog, claim the candidate registered, or
silently select the broader pointer protocol.

For the actionable F binding path, **both the original expectation and actual
provider must be NativeParameter-free**. Refuse retained `000E`, required
16/1 or an in-memory NativeParameter obligation before affected use; never
erase it or substitute an empty collection. Absence of NativeParameter grants
no NoEscape, extent, pointee-validity or initialization promise to an ordinary
pointer input.

The F+S owner discusses NativeParameter CallSites and their prototype, but
its independent EC storage-address operation does not establish this plan's
authorization for input-contract provider binding. That path remains
**conditional and outside the selected implementation direction**.
The smallest separate decision is to authorize the owner's exact 20-byte
`000E` candidate and `16/1 -> {1/1}` specifically on direct NativeImport
declarations and retained expectations under exact F+S, with complete original
input-contract comparison. Both compared artifacts must explicitly select
F+S for that conditional path. This would select neither F NativeParameter
binding nor pointer-anchor applicability, indirect calls or another schema.
Until that decision is made, refuse required NativeParameter content in F+S
too; existing row readers or passing prototype tests cannot make the decision.

## Original expectations and selected declarations

Retain two distinct contexts:

* **Expectation:** owned input snapshot/schema, using Module, ModuleRef,
  Import, expected MethodDef, selected producer identity/export key and its
  complete original declaration graph. A NativeImport row on this stub is
  retained foreign-target evidence, not proof that the using Module owns the
  provider declaration.
* **Actual:** explicitly selected provider snapshot/schema, Module, public
  Export and provider-local MethodDef, with its own complete contracts and
  type/layout origins. Resolve this before comparing; never populate the
  expectation from the replacement being tested.

Resolve Module identity and ExportKey by the existing ordinal decoded-text
rules, without case folding, Unicode normalization, provider-order choice or
signature search. Match ModuleRef target/runtime requirements. The Export
must select a public, provider-owned, bodyless, static, receiver-free,
method-generic-free callable NativeImport of the required kind. Preserve the
currently admitted top-level owner restriction; this adds no static-member
provider path. An imported proxy, anchor, authored equal-ABI method, data
import or merely bodyless declaration is not that actual target.

Existing explicit Export keys may differ from MethodDef names and foreign
entry names; lookup aliases permitted by metadata do not authorize proxy
re-export. Resolve an omitted native entry name in **each original declaring
context before projection/renaming**, never from an ExportKey or replacement
name. Compare owner kind, resolution mode/applicable policy, effective
convention and decoded library/resolved entry names exactly, using Standard's
ordinal comparison. Platform filename matching is a separate native concern.

Correspond explicit formals by logical ordinal, not name or Param row.
Compare complete result/formal types, ordered arity, Param modes/flags,
qualifications, ownership/transfer/phase facts and required target layouts.
The type-identity gate below applies before comparison; this slice does not
resolve nominal/import/formal types through an assumed provider map.
Caller-unsafety remains required, including scalar calls. Apply supported
Effects satisfaction without reviving NoThrow or inferring body guarantees.

### Cross-artifact type-identity gate

The accepted [binding contract](../specs/representation/binding-and-replacement.md#2-directed-satisfaction-and-exact-dimensions)
requires complete resolved identities, not equal row numbers or bytes. The
bounded implementation direction is therefore an explicit **type-form gate**,
not a claim that F consumes A2/A3 type-provider bindings.

Before any cross-artifact type comparison, check both declarations' complete
signature closures: result, every formal, inner output T, dependency subjects,
selector result types and every nested pointer pointee. Admit only these
already assigned forms, subject to all existing role/ABI/type restrictions:

| Form | Bounded identity |
|---|---|
| Atomic `0x01` | Zero qualifiers and an independently admitted built-in atomic code for that position: native-value positions retain their existing scalar restrictions, selectors require bool, and unit is permitted only by its existing role. A row or eligible F2 reference denotes the decoded atomic identity, not its row. |
| Compact raw pointer `0x01`, family `0x8C` | Exact outer qualifier `0` or `write=1`, and its complete admitted atomic argument, including `void` as a pointer argument. Thus `ptr<void>` is a pointer-descriptor value, not an output of type `void`. Eligible row/F3 representations keep the same assigned meaning; NativeParameter input restrictions do not define output-cell eligibility. |
| Indexed raw pointer `0x0C`, family `0x8C` | Exact qualifier `0` or `write=1`; resolve its argument TypeSpecRef in that artifact and recursively require this gate. Preserve every pointer layer and qualifier. |

Distinguish these roles under the accepted
[pointer grammar](../specs/grammar-spec.md#32-types-parameters-and-arguments)
and [descriptor rules](../specs/language/references.md#descriptors-referents-and-bindings),
using the [cell candidate's pointer-value meaning](aggregate-native-outputs.md#candidate-cell-eligibility-and-ownership-boundary):

* **NativeOutput T=`ptr<void>`**, formal `write ptr<ptr<void>>`: admit where
  the existing NativeOutput representation admits that complete pointer
  value. On success publish the descriptor only; its cell extent/alignment
  are those of the pointer, not `void`. This requires no NativeParameter
  annotation or Feature 16/`000E` and promises nothing about the pointee's
  extent, initialization, validity, backing, access, lifetime or dereference.
* **NativeOutput T=`void`**, formal `write ptr<void>`: refuse the zero-extent
  output value. Permission for `void` as a pointer's type argument does not
  make `void` itself an eligible cell.
* **NativeParameter `ptr<void>` input**: remains outside this selected
  direction. Its separately conditional Read/NoEscape/no-static-minimum
  annotation rules do not restrict a NativeOutput pointer-descriptor value.
  Nothing here selects the deferred F+S `000E`/16 path.

The [source eligibility check](../../sources/Terrum.CodeAnalysis/Semantics/DirectNativeImportContract.cs),
`IsNativeOutputCellType`, checks positive representation size for T, not
for T's pointee. This clarification adds no pointee guarantee or new schema;
nominal pointees still fail the separate type-origin gate.

Everything else is outside this binding slice: nominal TypeDef/Import
(`02/03`), formals (`04`), constructed types (`05`), function-pointer anchors
(`0D`), optional/array/reference/owner and other constructors. Other indexed
pointer encodings not admitted by this executable subset remain unsupported,
not new aliases or fallback encodings. Validate ordinary row ranges, ordering,
reserved fields and finite reference closure before comparison; exhaustion
cannot turn an unvisited type into success.
A valid excluded signature is unsupported for this operation, not proof of
a nominal identity mismatch; retain its expectations and refuse affected use.

Compare decoded atomic identities, pointer constructors and qualifications,
following each artifact's own pointer references. Reordered/duplicated
permitted rows and eligible inline references must not change identity.
Whole-row byte equality is not a general type-binding test; it is usable only
for an already validated index-free leaf. In particular, equal bytes for
`ptr<Nominal>` do not establish its pointee's origin, and pointer-cell Copy/
rootless representation does not exempt pointee **type identity** from this
gate. Co-location, layout equality and an A2/A3 map in another route cannot
supply F's missing mapping.

**Deferred nominal/type-provider binding extension:** retain and refuse
provider-dependent signatures until an independently admitted checked mapping
from each original using-Module/type expectation to its selected provider
definition is actually handed to the MethodDef-edge comparer, reconstruction,
remapping and replacement checks. Positive Copy evidence alone is not that
mapping. The accepted contract already fixes identity and context rules;
the missing F handoff is an implementation/support extension, not an
undecided type-alias, fallback or schema rule. No such extension is selected
here, including for the conditional F+S NativeParameter path.

### Complete foreign protocol

| Component | Required normalized facts and checks |
|---|---|
| Conditional F+S NativeParameter inputs | **Only after the separate decision above**, and still under the type-identity gate, require equality of presence and the entire `000E` collection, keyed by explicit formal ordinal: complete pointer/pointee type, access `1/2/3`, NoEscape `1` including exceptional exits, extent kind and full unsigned `u32` count. Kind 0 requires zero; kind 1 requires `1..FFFF_FFFF` elements, not bytes. Derive and retain actual stride/alignment; do not invent depth/alignment override fields or narrow through `int32`. Write/ReadWrite requires writable **initialized** backing. For this deferred input-annotation contract only, `ptr<void>` would be Read/no-static-minimum only; that is not a pointer-valued output restriction. No formal is both NativeParameter and NativeOutput. Missing annotations are not default NoEscape rows. These rules do not admit this content under F. |
| Raw output | Exact `write ptr<T>`, ordinary Param mode `00`, direct-import role, vacant whole-object formation, positive exact object extent/alignment, no incoming-T read, call-extent address use/no address retention and normal publication. Require exactly two `0002` rows per output formal: `(success,state=1)` and `(complement,state=0)`. Pointer-cell publication initializes the pointer, not its pointee or a resource owner. |
| Selectors | Compare normalized result-type/test identities rebased to corresponding ordinary-result subjects, not graph rows, phase ordinals, source enum numbers or private condition tuples. NativeOutput uses nodes `3/4` for nonzero/zero on exact `int32` or `uint32`, or `5/6` for nonnegative/negative on exact `int32`. Boolean nodes `1/2` do not widen the raw-output domain. |
| Dependencies | Target-owned only; caller/anchor/other-method rows cannot supply target facts. Every applicable `0004` destination is kind `2`, that annotated output formal, phase `1`. Require one explicit empty row `(sourceKind,relationship,sourceOrdinal)=(0,0,0)`, or a distinct eligible formal-source set with kind `1` and relationship `2` backing-only or `3` declared union. Empty and sourced rows cannot mix. Receiver sources are invalid on these receiver-free declarations; outputs/reserved addresses cannot be sources. Reject self/output-to-output claims, orphan destinations and other phases/destinations in this bounded route. Compare complete normalized sets exactly, including each permission; no implicit empty set or source-name matching. |

Every selector must have its own MethodDef-owned `000B`, phase `3`, valid
contiguous phase-local ordinals, and structurally valid graph ownership.
Validate the cell owner's exact read-only shape: one bool-result node, one
ReadRoot operand, the sole OrdinaryResult root in its own footprint, no
dependency reads, edges, captures, literals, calls or output reads. Both
OutputState references must resolve to the proper attached graphs. Selector
observation classifies a saved normal result; it is not an asserted
postcondition, user-code evaluation or proof of foreign behavior.

Consistent ordinary table/clause permutations and equivalent duplicate
selector graphs remain valid; duplicate occurrence keys and cross-owner
graphs do not. Normalize before comparison. Exact dependency equality is
the selected bounded foreign relation, even where a future supported
relation could admit a narrower source bound. Independent type, backing,
provider/code and lifetime obligations survive an explicit empty source set.
An input pointer contributes only independently established backing/provenance,
not inferred pointee validity or storage in the callee's parameter slot.

### Type and ownership evidence gate

T must be closed, positive-extent, fixed-rootless, directly interoperable
`Blittable Copy` with its exact complete representation. For the gated forms,
use their admitted intrinsic type/Copy facts and selected target mapping.
A future nominal extension must additionally preserve nominal origins,
fields, nested types, layouts, formal domains/substitutions, Copy promises,
Unique classifications and cleanup/ownership facts on both sides.
Do not derive Copy or `IsUnique=false` from layout, a struct kind, empty
bodies, missing Cleanup or the replacement's favorable fields.
Unique owners, checked references, views, managed/support-bearing values,
zero-extent cells and unsupported type constructors do not qualify.

F/F+S supplies intrinsic scalar/raw-pointer cell facts, but does **not** add
[A2's positive Copy carrier](standard-trrm-rootless-copy-type-evidence.md)
or [A3's positive Unique carrier](standard-trrm-a3-positive-unique-type-evidence.md).
Nominal aggregate outputs and pointers with provider-dependent pointees
already fail the type-identity gate, independently of this ownership gap.
The existing local aggregate round-trip supplies neither cross-Module
identity binding nor the missing ownership evidence.

If nominal aggregate provider outputs are required next, the smallest
separate composition decision is to select an exact ownership-evidence
schema, alongside the deferred checked type-provider binding extension.
Existing A2/A3 candidates are explicit options, not inferred decoders or
permission to set their bits in F. That composition remains unselected here;
it does not block the intrinsic-cell slice or change the generalized cell
contract. Neither this ownership gate nor the separate NativeParameter
decision blocks the recommended NativeParameter-free intrinsic-cell slice.

## Before-use checks and native handoff

1. Preflight every input's exact selection and operation support; capture
   owned snapshots. Safely decode required framing, bounds, references,
   owner/range relationships, reserved fields, clauses, type facts, graph
   shapes and complete per-Module requirements. Unsupported retained facts
   cannot disappear because a declaration is uncalled.
2. Resolve and compare every retained original Import, including provider-owned
   dependencies, against its selected public local export. Apply the complete
   signature type gate to both sides before type comparison; refuse excluded
   signatures rather than treating raw row/byte equality as a binding.
   Publish no program, reference or native plan until the required closure
   succeeds. A visited Module or in-progress comparison is not acceptance of
   another expectation.
3. Source-free reconstruction and source-reference projection retain the
   original native descriptor, admitted input facts, output role/collection,
   selectors, dependencies and origins. Unsupported NativeParameter facts
   cause refusal, not projection to ordinary inputs. Never replace the
   shared clauses with the legacy Development `0012` output tuple or a
   trusted Boolean.
4. Preserve direct CallSite target selection and DF/record-`0C` reservations,
   `(selectorKind,sourceUse)=(2,6)`, distinct exact-T Homes and saved-result
   correlation.
   Binding adds no reservation, temporary T, copy-back, constructor,
   marshalling or publication. Pre-Invoke cancellation, complement vacancy
   and exceptional indeterminacy retain the cell owner's meanings.
   DoesNotReturn still requires valid complete output facts and has no
   normal publication.
5. The existing immutable Standard native plan consumes the checked actual
   declaration while retaining the original obligations. For this semantic
   relowering route, realize its admitted foreign binding; do not invent a
   precompiled provider DLL. If an already selected precompiled route is
   used elsewhere, preserve its actual provider entry/image/lifetime rather
   than silently regenerating or bypassing it. Reject unsupported convention,
   resolution or target mappings; a private SystemDirectory IAT is not a
   relabeling of Link/Dynamic. Imported expectations remain ineligible
   root-owned library exports.

These are mandatory structural/support/binding checks under
[reader trust](../specs/representation/reader-trust.md), not semantic re-proof
of arbitrary provider Bodies, source flow or foreign promises. Providers
owe truthful facts; optional audits are separate. Do not execute provider
code, predicates, getters or cleanup to discover compatibility.

## Replacement, topology and remapping

Apply the same complete comparison to single-provider selection and every
edge of **already admitted** Standard multi-provider/graph closures:
co-located Modules, repeated incoming views, intermediate dependencies,
uncalled/disconnected retained edges, diamonds and admitted cycles/self-edges.
Each edge selects its provider-owned declaration, not a downstream terminal
leaf. Preserve existing topology/name/resource bounds; F+S does not acquire
F-only graph admission by sharing the output codec. This defines no Standard
set/graph container and changes no private package route.

Replacement checks the untouched original expectations and any stronger
provider facts actually relied upon, under
[binding and replacement](../specs/representation/binding-and-replacement.md).
Equal Module/key, image hash, ABI or output bytes cannot excuse a changed
foreign target, input contract, state partition, dependency or type/ownership
fact. Recheck affected incoming edges and retained closure obligations after
replacement; do not merge expectations into a weakest view. Cache reuse
requires unchanged snapshots, schemas/catalogs, bindings, target/runtime and
rule contexts. This adds no live patching, quiescence or unload policy.

A rewriter decodes meaning and rebuilds, rather than substitutes bytes.
The equivalence claim here covers only the gated type closure, not arbitrary
nominal/import/formal or signature-anchor relocation:

* Remap Module/ModuleRef/Import/Export, MethodDef/MethodSig/Param and Contract
  owners/ranges, corresponding formal subjects, permitted TypeSpec references
  and selected TargetProfile identities. Resolve nested pointer references
  separately in each artifact. Provider-dependent/contextual type rows require
  the deferred extension, not a guessed map or a byte-preserving rewrite.
* Remap OutputState graph references, `000B` attachments, graph/node/operand/
  footprint/root ownership/ranges and destination/source formals. Rebuild
  contiguous phase ordinals without changing tests or sharing another
  MethodDef's result context; retain independently required graph tables.
* Rebuild Body MethodMap/TypeMap/HomeMap, CallSite target/input/result
  selectors, OutputReservation's CallSite/Home indexes and formal ordinal,
  DF operands and other preparation/record references. Preserve distinct
  Homes/generations, EC backing facts, record/code/Block extents and retained
  SourceMap instruction coordinates. Recompute each Module's requirements.

Metadata rows/typed DefIds and Body-local zero-based indexes are different
namespaces. An Import-to-definition rewrite retains the original obligation
and selected origin even when the Import row is removed. Packing selects one
explicit destination schema capable of all retained meaning; no header
patching, guessed decoder, obligation erasure or private fallback is conversion.

## Failures and required cases

Keep existing selected-consumer failures: argument/selection errors use
`ArgumentException` (including its existing subtypes), malformed/unresolved/
incompatible binding data uses `InvalidDataException`, and valid unsupported
required semantics/comparisons use `NotSupportedException`. Resource
exhaustion and overflow remain failures, never truncated successful closures.
Complete admission before affected lowering/loading/execution or output
publication; preserve destination files on failure. Native resolution failure
retains its native boundary, never successful null/fabricated publication.

The following are acceptance requirements, **not executed results**:

| Case | Required outcome |
|---|---|
| Positive direct binding | NativeParameter-free F scalar/raw-data-pointer cells and ordinary inputs whose complete types pass the gate, including typed atomic and `ptr<void>` value cells where independently admitted, NonZero/NonNegative, multiple outputs and unrelated synthetic foreign APIs. Separately supplied/co-located public declaration-only providers retain original expectations through load/reference/re-emission and JIT/PE handoff without dummy Bodies/anchors or Feature 16. |
| Void role separation | NativeParameter-free F/F+S `write ptr<ptr<void>>` output publishes only T=`ptr<void>` on success, without `000E`/16 or pointee guarantees; complement stays vacant. Refuse T=`void` (`write ptr<void>` output) and T=`ptr<Nominal>` for their distinct extent/type-origin reasons. NativeParameter `ptr<void>` input remains gated, not inferred from the successful pointer-cell case. |
| Ordinary inputs and backing | F/F+S outputs with admitted ordinary inputs and nonempty target-only output dependencies; preserve actual provenance without inferring NativeParameter guarantees. Independently admitted F+S EC formation retains initialized stable backing but does not authorize `000E`. |
| NativeParameter scope gate | F rejects `000E`, required 16/1 or in-memory NativeParameter obligations even when a decoder recognizes them. F+S does likewise until the separate decision is selected. No dropping annotations, default NoEscape, inferred composition or mixed F/F+S NativeParameter binding. |
| Conditional F+S input matrix | After explicit authorization only, combine outputs with Read/Write/ReadWrite inputs, typed/opaque pointers and staged local backing. Preserve full access/retention/extent/pointee expectations; reject mismatches, output overlap and vacant Write backing. This matrix is not a gate on NativeParameter-free F progress. |
| Independent identities | Legal public ExportKey differs from local/native names; explicit entries allow declaration-name changes without changing the native target. Missing/case-normalized Module/key, duplicate provider identity, wrong owner/kind, proxy re-export and changed implicit-entry resolution fail. |
| Exact replacement | Independently change library/entry, resolution/policy/convention, ordinary input types/modes, output type/partition/state/phase or dependency source/permission. Reject despite equal ABI or recomputed checksums; retain the consumer view before comparison. NativeParameter annotation presence/access/retention/extent comparisons belong only to the conditional F+S matrix; introducing them on F fails scope admission. Body-only changes preserving all retained facts may pass. |
| Predicate structure | Equivalent reordered/duplicated selector graphs bind. Missing complement, reversed states, wrong result type, cross-owner/out-of-range reference, output-reading footprint, edge/capture/call or phase-1/2 graph fails under this bounded selection. |
| Dependency structure | Explicit empty and exact nonempty target-only sets bind; missing/orphan/mixed-empty sets, receiver/self/output sources, duplicate occurrences and unrelated destination/phase fail. Equal source names in different formal positions do not match. |
| Cross-artifact identity gate | Refuse valid signatures containing `02/03/04/05/0D` or other excluded forms, including `ptr<Nominal>`, even with Copy pointer-cell layout or equal row bytes. Test equal nominal/anchor row numbers denoting different declarations in different Modules, and changed row numbers for the same selected nominal identity: neither is admitted without the checked extension. Never erase a pointee or substitute `ptr<void>`. |
| Ownership/type gate | Reject unsupported nominal Copy/Unique evidence, Unique/managed/checked-reference or zero-extent cells. Do not turn the local aggregate F fixture into approval of aggregate provider binding or interpret A2/A3 flags under F; positive flags alone cannot bypass the identity gate. |
| Remapping and retention | Permute Module, MethodDef, Param, permitted TypeSpec, graph and Body rows/maps, including nested raw-pointer argument references, duplicate/inline built-ins, multiple output Homes and retained SourceMaps. Preserve decoded identity and behavior, not necessarily identical bytes. Nominal/contextual/anchor remapping remains refused. Changing a terminal atomic code or any pointer qualification must not match despite equal ABI. |
| Set/graph replacement | Exercise existing single/multi-provider closures, a diamond with two original views of P/K, deep/intermediate uncalled output imports, and admitted cycles/self-edges. Check every incoming edge; one mismatch rejects the operation. Unsupported F+S topology is an explicit refusal, not a leaf-route retry. |
| Width/resource boundaries | Reject invalid zero/reserved-high-byte typed rows and wrong DefId namespaces; distinguish valid Body indexes `0/255/256/65535`. Admit count 65,536 where supported and reject 65,537. Preserve `u32` formal/count bounds and checked geometry; do not truncate or mark an unvisited comparison satisfied. NativeParameter extents `0/1/0x80000000/0xFFFFFFFF` and extent-kind boundaries are conditional F+S cases only. |
| Outcomes and native evidence | Success publishes only its exact Home generation; complement permits no read; abandoned calls cancel before Invoke; admitted exceptional paths remain indeterminate. Qualify runtime success/complement separately from metadata and PE emission, with supervised headless execution and no second publication or ownership inference. |
| Cross-feature/schema | Require each Module's effective 8/10 and 3 for direct calls; do not add 16 for ordinary inputs or output dependencies. The conditional F+S NativeParameter path alone would add 16. No unnecessary 11/17, another Module's Feature repair, checked-output source-use 5 alias, NativeVTable identity policy, indirect-call widening or default/other-schema retry. |

## Current implementation and remaining acceptance

This direction remains **unapproved**. Its bounded NativeParameter-free
implementation is provisional evidence, not normative adoption.

- [`TrRmExecutableCompiler.NativeOutputDeclarations`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeOutputDeclarations.cs)
  emits public bodyless F/F+S provider declarations using shared
  OutputState/PredicateGraph/Dependency records and a real Export, without a
  dummy caller or anchor.
- [`StandardConsumption`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.StandardConsumption.cs)
  retains the consumer expectation and compares the selected provider's
  foreign descriptor, unsafe/effect facts, formal types and modes, output
  states/selector meaning, and complete Dependency set. The shared anchor
  comparer is used only after the explicit built-in/raw-pointer identity gate;
  nominal/provider-dependent types and NativeParameter `000E`/Feature 16
  remain refused.
- [`NativeAddressReader`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeAddressReader.cs)
  reconstructs output-bearing ModuleRef/Import calls with the selected
  `ModuleImport`. [`NativeOutput` production](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.NativeOutput.cs)
  preserves that origin through source-free re-emission, and the
  [native binding plan](../../sources/Terrum.CodeAnalysis/Modules/TrRmStandardNativeBindingPlan.cs)
  revalidates the selected closure before PE/JIT lowering.
- The active [`foreign-invocation tests`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmForeignInvocationAddressEmitterTests.cs)
  cover separate and co-located declaration-only providers, F/F+S and
  F+S-consumer/F-provider selection, scalar and nested raw-pointer cells
  including `ptr<void>`, dependency and selector comparison, TypeSpec and
  predicate-graph row permutation, source-free re-emission, PE emission, and
  rejection of changed foreign targets/types/contracts. The combined
  NativeAddress/NativeImport/provider/local-aggregate selection passes
  514/514 with no skips, and the analyzer-enabled Debug build passes with
  zero warnings/errors.

F+S is deliberately bounded to one selected provider Module and does not
acquire F-only provider-graph admission. Active F regressions now cover
multiple output Imports including an uncalled second declaration and a changed
contract on an intermediate provider edge. The common F edge loop applies the
same comparer to every retained Import. Tests also cover an intermediate
provider that re-emits an uncalled public output declaration while importing
and calling a leaf output provider, plus per-edge comparison and changed-
selector rejection. Co-located root/provider selection is covered; the broader
and intermediate-provider selection is covered, including a co-located
intermediate Module artifact, a shared-leaf diamond, a self-edge and a
two-module cycle through closure validation and PE handoff. NativeParameter-
free F/F+S provider binding is complete for this bounded provisional slice;
the candidate remains unapproved. Nominal ownership
mapping, NativeParameter input binding, NativeVTable/foreign-interface
identity, checked or indirect outputs, and resource adoption/release remain
separate gates.
