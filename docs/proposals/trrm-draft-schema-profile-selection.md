# Exact draft-schema selection for foreign invocation

**Status: unapproved; provisionally actionable under the authorized
missing-contract workflow.** This recommendation supplies selection and
composition direction, not normative adoption, Feature registration,
implementation support or compiler/runtime evidence. It changes neither
NativeImport Contract `000E` nor Feature `16/1` and approves no adjacent
source, ABI or persistence design.

## Decision and scope

Select the [NativeImport pointer candidate](standard-native-import-function-pointer-contracts.md)
through a **new, explicitly named whole-artifact schema profile**. Keep the
existing Standard, Development and historical CopyOriginDraft selections
unchanged. Define four closed candidate profiles below: foreign invocation
alone, and its combinations with CopyOrigin, general predicates, or both.
An implementation may support them incrementally; an unsupported selection
fails before schema-dependent indexing, writing or loading.

A profile determines one exact schema and governing catalog. Feature rows
declare a Module's semantic requirements **within that already selected
schema**; they never select or compose decoders. No header field, profile
record, compatibility alias, decoder negotiation or new wire allocation is
introduced. All these candidates retain format `(1,0)`.

The controlling accepted owners are [format versioning](../specs/representation/format-versioning.md),
[requirements and admission](../specs/representation/requirements-and-admission.md),
[semantic manifests](../specs/representation/semantic-manifests.md),
[the registry](../specs/representation/registry.md),
[consumer checking](../specs/representation/reader-trust.md) and
[canonicality](../specs/representation/encoding-canonicality.md), reached
through the [specification index](../specs/README.md). Their exact-schema,
per-Module closure, binding and producer-truth rules are preserved. This
proposal completes the interacting proposals' out-of-band selection gap;
it does not incorporate their amendments into accepted specifications.

## Exact compositions and identities

The following recipe names are notation in this document, **not** caller
flags, Feature sets or independently selectable subdecoders.

| Recipe | Complete selected contents |
|---|---|
| B | Accepted `trrm/1-draft-layout-queries-1`: its registry, governing chapters and semantic catalog, before the provisional amendments below. |
| C | The complete Standard-facing delta and bounded semantic guarantee in [CopyOrigin](reference-copy-origin-trrm.md), including `000D`, `15/1`, contract-only declaration identities and FieldPath rules. Its private candidate 95 is not a Standard codec. |
| F | The complete Standard-facing [NativeImport pointer-contract candidate](standard-native-import-function-pointer-contracts.md), including its amendments to [source pointer contracts](standard-function-pointer-source-contracts.md), [anchors](standard-function-pointer-anchor-contracts.md), [NativeAddress](standard-native-address-formation.md), [aggregate outputs](aggregate-native-outputs.md), [scalar/pointer output cells](native-pointer-output-ownership.md) and [output source dependencies](aggregate-native-output-source-dependencies.md). Select [bounded predicate composition](predicate-feature-composition.md), not its unselected general-graph alternative. This includes `000E`/`16/1`, `E9`/Body record `0D`, `DF`/Body record `0C`, and their complete applicability and preservation rules. |
| G | The complete [general-predicate graph candidate](general-predicate-graph-schema.md), including `17/1`, required sections `004D..0052`, scoped TypeSpec form `10`, read-root kind 8, its selected [scalar frozen-value registration](scalar-frozen-value-registration.md), and its catalog amendments described below. |

Only the Standard-facing parts of these owners enter the recipes. References
to private revisions, provider packages or runtime implementations do not
silently add their formats. Independent missing representations remain
gates. A recipe includes no unrelated proposal merely because an owner
mentions it.

The candidate names below are exact, case-sensitive out-of-band identities.
The proposed internal selectors are distinct enum members, not flags.

| Exact candidate profile identity | Proposed `TrRmArtifactSchema` selector | Whole schema |
|---|---|---|
| `trrm/1-draft-foreign-invocation-contracts-1` | `ForeignInvocationDraft` | B amended by F |
| `trrm/1-draft-copy-origin-foreign-invocation-contracts-1` | `CopyOriginForeignInvocationDraft` | B amended by C and F |
| `trrm/1-draft-foreign-invocation-general-predicate-graphs-1` | `ForeignInvocationGeneralPredicateDraft` | B amended by F and G |
| `trrm/1-draft-copy-origin-foreign-invocation-general-predicate-graphs-1` | `CopyOriginForeignInvocationGeneralPredicateDraft` | B amended by C, F and G |

These are four definitions, not a string-concatenation or powerset algorithm.
There is no implicit `Standard + Features`, arbitrary flag combination,
`latest`, superset fallback or automatic selection of the smallest profile
that fits some bytes. The standalone general-predicate designation remains
owned by its proposal; it is not an alias for an F composite. No C+G-only
profile is added by this bounded decision.

Revision 1 means the complete compositions selected here, including the
explicit owner amendments, not future text at those paths. Exchange must
agree on that complete schema/catalog, not merely a name, registry digest
or build identifier. A changed composition needs a distinct explicit
selection; unpublished implementations may retire it without retaining a
reader or converter. This is not a released-schema stability promise.

### Preserved selections

* `Standard` and the public default `TrRmArtifactIndex(artifact)` continue
  to select the existing Standard schema. Neither gains C, F or G through
  this proposal. Its present support limitations are not redefined here.
* `CopyOriginDraft` and `TrRmArtifactIndex.ForCopyOriginDraft` continue to
  select exactly `trrm/1-draft-copy-origin-contracts-1`, not C+F or C+F+G.
  Existing CopyOrigin callers must opt into a new composite to use it.
* `Development` remains its separately selected implementation-private
  format. Development clauses `10/11/12`, six-byte pointer signatures and
  private tuples are not encodings of F. Private revision 97 or an outer
  native container does not select any normative payload profile.
* Default writer and loader paths keep their existing selection. Adding
  the enum members must not widen a check such as “not Development” into
  admission of every new candidate.

### Composition rules and catalog prerequisites

The four profiles have one interpretation of every assignment throughout
the artifact, even when Modules require different optional capabilities.
Their complete catalogs are fixed by these rules:

| Capability | Immediate prerequisites and disposition |
|---|---|
| `1..7/1`, `13/1` | Preserve their existing native/layout registrations and conditional requirements. |
| `8/1=NativeOutputCells` | F selects the output candidates' scope and `[(1,1),(2,1),(10,1)]`. |
| `10/1` | F selects exactly the six phase-3 selectors, prerequisites `[]`; G preserves them rather than broadening them. |
| `15/1=CopyOriginContracts` | Available only in C-containing profiles, prerequisites `[]`; FieldPath/FieldDef binding still conditionally requires 13. |
| `16/1=ForeignInvocationContracts` | F, governing edition 1, prerequisites `[(1,1)]`, exactly as its owner proposes. Never general predicates. |
| `17/1=GeneralPredicateGraphs` | Available only in G-containing profiles, prerequisites `[(10,1)]`. |
| `11/1`, `12/1` without G | Preserve the bounded predicate-composition candidate's still-incomplete registrations: `11 -> 9`, `12 -> {10,11}`. Their presence does not complete their missing codecs or semantics. |
| `11/1`, `12/1` with G | G selects the closed fixed-scalar `11/1`, prerequisites `[]`, and changes incomplete `12/1` to prerequisites `[(11,1),(17,1)]`. Feature 12 remains incomplete. |

Feature 9 remains registration-pending. G's changed 11/12 catalog applies to
the **whole G-containing schema**, not only to Modules with explicit 17.
It does not change the historical C profile's catalog. No per-Module
interpretation switch, maximum-revision rule or presumed compatibility
mapping is introduced.

Selecting a profile does not make all its capabilities required by every
Module. Compute each Module's least exact closure from all retained content
and selected dependencies. In particular:

* `000E` alone requires 16, deriving 1, but not 8 or 10. Actual pointer
  values and address/indirect uses retain their 2/4 requirements.
* Raw-output pointer anchors and indirect calls need both 8 and 16;
  ordinary direct output cells do not acquire 16 merely from using 8.
* C facts and nonpublic contract bindings require 15, with 13 when their
  retained paths/binding use it. F does not imply 15.
* General phase-1/2 graphs and evaluation read bounds require 17 under G;
  scalar literals independently require 11. F does not imply 17 or 11.
* Declaration-only, unused-but-retained, nested-type and dependency
  obligations still count. Another Module's Feature rows cannot repair a
  missing requirement.

The compositions are not semantic unions that relax owner restrictions:
CopyOrigin remains ineligible on raw imports and pointer anchors. G still
does not permit phase-1/2 predicates or evaluation forms on those anchors.
F's native conventions, exact foreign protocol, unsafe and code-origin
obligations remain unchanged.

No profile here includes root/pin Feature 14. F selects Body record `0D`
as NativeAddress, not the root/pin candidate's conflicting record. Contract
`000D=CopyOrigin`, TypeSpec `0D`, and Body record `0D` are distinct namespaces,
so C causes no such collision. G preserves 16 for foreign invocation and
uses 17; its sections `004D..0052` do not allocate the separately proposed
`004C` foreign-interface table. Default-convention changes, ordinary Terrum
function addresses and other unlisted deltas are not implicitly selected.
An attempted combination with any of them has no profile here.

## Explicit caller selection and operation boundaries

The required caller contract is **one exact selected profile per artifact**,
supplied before decoding. The existing internal schema-bearing constructor
can carry the new selectors; profile-aware producer and loader entry points
must carry the same selection. This document fixes that behavior, not a new
public API shape or CLI option.

1. Resolve the caller's exact identity to a known profile and check that
   the requested operation implements that profile's complete structural
   schema and catalog. Reject unknown selectors and unsupported composites
   **before indexing, accepting rows for writing, or starting a load**.
   Do not inspect a Feature table or try a decoder to make this decision.
2. Keep that immutable selection through framing, table and Body readers,
   writers, manifest closure, structural validation, binding/replacement and
   loading/lowering. All profile-bearing component inputs must agree.
   A non-profile-aware overload cannot silently reset part of the operation
   to Standard, CopyOriginDraft or Development.
3. Decode only that schema; check its references, extents, applicability,
   manifest and support requirements. Knowing a profile is distinct from
   supporting every optional semantic/target/runtime use it can represent.
   Unsupported retained uses still fail before affected binding, code
   generation, code/image loading or execution; an inspector's structural
   support is not execution support.
4. Producers select the destination before encoding and establish truthful,
   schema-valid facts. Consumers perform mandatory safe-consumption and
   binding checks, not semantic re-proof of all producer body facts.
   Rejection must not return partial success or publish an output artifact.

A build supporting only F must reject a request for C+F+G even if the
supplied bytes happen to use neither C nor G. Conversely, supporting the
complete G-containing schema does not claim support for incomplete Feature
12, arbitrary target layouts or unimplemented runtime protocols.

Each separately supplied dependency artifact gets its own explicit
selection from the caller/resolver agreement. A root's profile, ModuleRef,
filename, extension, `(1,0)` header or enclosing package supplies no decoder
choice for another artifact. Cache/index reuse includes the exact profile,
catalog/support context and original binding inputs, not bytes or Module
name alone.

No CLI auto-detection is authorized. Existing commands remain on their fixed
Standard or private route; they must not secretly enable a candidate.
Until a command exposes an explicit exact-profile selection contract, that
command does not support candidate input/output. A future CLI surface must
name the exact input selections and, for production/conversion, the separate
destination selection. Missing dependency agreement or an unsupported
composite is an error, not inheritance or a retry with another schema.
No CLI change is a prerequisite for an explicitly selected internal slice.

Bare bytes cannot prove their originating draft. A common-subset artifact
may be valid under several profiles when each is explicitly selected; no
new discriminator is available to distinguish that provenance. Reject
schema-specific invalid/unsupported content and explicit context mismatches,
not every byte image that could also have been emitted under another
profile. Equal bytes never establish schema agreement.

## Conversion, packing and canonical production

Separately supplied C, F and G artifacts may be consumed together only when
each source schema is supported and complete binding, semantic, target and
runtime checks succeed. This does not authorize placing their original
tables/Bodies under one directory. Packing selects **one explicit destination
profile** capable of preserving every retained obligation.

For example, retained C plus F requires the named C+F profile, or the named
C+F+G profile if that destination is deliberately selected and implemented.
Retained F plus general graphs requires F+G or C+F+G. An F-only or historical
C destination cannot erase the other capability to make packing succeed.
If the needed composite is unsupported, decline before destination writing.

A converter selects every source and the destination separately, decodes
their meanings, and rebuilds affected Contract owner ranges/offsets,
TypeSpecRefs and anchors, declaration/import/export identities, FieldPaths,
graphs/helpers/capture scopes, per-Module Feature ranges/closures, and
Body-local maps, records, blocks and code extents. Preserve original
expected/provider contracts, foreign protocols, retained CopyOrigin
guarantees, stronger facts actually relied upon, and backing/code/load
dependencies. Rebuild retained SourceMap data for changed coordinates;
optional source data cannot carry missing semantics.

The G catalog's 11/12 changes require explicit semantic preservation too,
even for a Module with no explicit 17. Keeping equal Feature words is not
proof that the required scope was preserved. Missing source semantics,
destination representations or a valid preservation relation declines the
conversion. Header patching, relabeling, concatenation and dropping
requirements are not conversions. No converter or historical compatibility
reader is required by this proposal.

Canonical writers use the selected owners' orders and rebuild indexes after
remapping. C keeps its CopyOrigin-before-Body FieldPath traversal; G keeps
MethodDef/phase/ordinal and root-first graph traversal. For shared FieldPath
first use in these composites, preserve the existing Module/MethodDef
traversal; at each method visit C's canonical paths if present, then G graph
FieldPath operands in phase/ordinal, node and operand order, then the
existing Body traversal. Earlier first use retains precedence. This
cross-feature traversal does not reorder graph evaluation or physical
MethodDefs. Neither physical clause order nor path-row order is execution
order, and no unique whole-file image is required. Otherwise valid
noncanonical permutations remain ordinary input. An implementation must not
use a physical ordering or successful parse as profile selection.

## Required focused cases

These are future acceptance cases, **not executed implementation evidence**.
“Admit” assumes complete independent type, binding, target and operation
support; it does not assert producer truth or completion of another codec.

| Case | Input/selection | Required result |
|---|---|---|
| DSP-01 Preserved defaults | Existing Standard fixture through its default index/writer; historical C fixture through `ForCopyOriginDraft` | Preserve the original selections and behavior. No C/F/G auto-upgrade. |
| DSP-02 Declaration only | F selected; eligible 20-byte `000E`; explicit `{16/1}` | Derive `{1/1,16/1}` without 8/10/15/17; validate the retained declaration even if uncalled. |
| DSP-03 Foreign output pointer | F; valid raw-output anchor, E9 formation and indirect reservation; `{4/1,8/1,16/1}` | Closure `{1/1,2/1,3/1,4/1,8/1,10/1,16/1}`. Keep the exact protocol and selector-only anchor. |
| DSP-04 C+F | C+F selected; eligible ordinary wrapper has `000D`, separate import/anchor has F facts | Preserve both contracts. Add 15 and conditional 13 independently, without G. |
| DSP-05 F+G and C+F+G | Each named profile; general phase-1/2 graph on an eligible callable beside the F protocol; add C on an eligible wrapper for the latter | Use 17 with derived 10; literals add 11. Preserve 16 as foreign invocation and all per-Module requirements. |
| DSP-06 Cross-profile rejection | `000E`/16 under Standard or historical C; C's clause under F-only; G sections/nodes under either non-G F profile | Reject under the selected schema. Do not widen a historical selector or retry a composite decoder. |
| DSP-07 Unsupported composite | Only F is implemented; request named C+F or F+G, including with a common-subset byte image | Fail profile preflight before index construction, writer row acceptance or load side effects. No reduction to F based on content. |
| DSP-08 Unknown selection | Unknown identity, invalid enum, reordered concatenated profile name, `latest`, or ad hoc capability mask | Reject before decoding/encoding. No alias, numeric roll-forward or string composition. |
| DSP-09 Unchanged applicability | C clause on an F anchor/raw import, or G phase-1/2/evaluation form on a pointer anchor | Reject in every composite; profile composition grants no new role applicability. |
| DSP-10 Allocation conflict | Request F plus root/pin 14, or decode pin Body `0D` as an alternate NativeAddress | No supported composite; reject, never select by plausible operands or unsafe authorization. |
| DSP-11 Missing/conflicting requirements | Retained F facts without effective 16; `16/2`; conflicting revisions; one packed Module relies on another's 16 | Reject missing/unsupported requirements. High-bit full-width IDs are not masked into known IDs; closure storage must handle 16/17 without truncation. |
| DSP-12 One context | F index plus Standard/C-only table context, stale profile-keyed cache, or different selections for Modules in one artifact | Reject context mismatch rather than mixing catalogs/decoders. |
| DSP-13 Catalog distinction | Same explicit 11/12 words under F and F+G; reordered lists or repeated identical pairs | Use each explicitly selected whole catalog; never infer G from the list. Ordinary equivalent ordering/duplicates remain valid; incomplete 12 still rejects affected use. |
| DSP-14 Separate input, one output | Explicit historical C and F inputs bind; pack first to supported C+F, then attempt F-only/unsupported composite | Re-encode and preserve obligations for the supported destination; reject the other attempts before output publication. |
| DSP-15 Transformation identity | Remap imports, anchors, clauses, shared paths, graphs and Body maps; retain or remove optional SourceMap legally | Preserve decoded meaning and original expectations; validate rebuilt ranges/references and composite path first-use order. Ordinary consistent permutations remain valid; relabeling/header-only conversion fails. |
| DSP-16 Ambiguous common subset | Same valid baseline bytes explicitly selected under two supported profiles | Decode each selected schema without claiming to recover provenance. Equal `(1,0)`, bytes or registry digest supplies no implicit agreement. |

## Integration boundary and prerequisites

Source inspection identifies the selection boundary, not candidate support:
[`TrRmArtifactIndex`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactIndex.cs)
currently names Standard, Development and CopyOriginDraft and has the explicit
CopyOrigin factory;
[`TrRmArtifactWriter`](../../sources/Terrum.CodeAnalysis/Modules/TrRmArtifactWriter.cs)
defaults to Standard and passes its internal selection to the index.
The [Feature reader/manifest validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmFeatureTableReader.cs)
has a CopyOrigin-specific catalog path and 16-slot closure storage.
Those are integration points, not proof that any new profile is supported.

Implementation prerequisites are: carry the exact selected profile through
every participating reader/writer/validator/loader; implement its complete
structural deltas and single catalog; retain all semantic facts through
binding, shared rewriting, verification, persistence and lowering; and
qualify the relevant DSP cases against final inputs. Narrower F and C+F
profiles may be implemented before G profiles. Do not register a composite
as supported merely because its constituent names or some Feature cases
are recognized.

This closed list is smaller than a general profile algebra or new wire
identity. Reusing CopyOriginDraft as an ever-growing profile would silently
change its meaning; requiring only an all-capabilities profile would couple
foreign-invocation implementation to unrelated graph work. Four explicit
entries permit bounded staging without format guessing. Further compositions
require a separate exact decision, not an implementer choosing allocations
or precedence.

No selection/composition policy for these four candidates remains delegated
to implementation. Formal approval, normative owner reconciliation,
independent missing codecs, implementation, CLI exposure where wanted and
qualification remain separate gates. This document supplies no execution
evidence or milestone-completion claim.
