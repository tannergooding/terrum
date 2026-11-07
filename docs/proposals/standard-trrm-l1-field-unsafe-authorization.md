# L1 operation-local field unsafe authorization

**Status: unapproved; provisionally actionable only under the user's explicit
missing-contract workflow.** This is a focused integration amendment to
[L1](standard-trrm-local-aggregate-layout-execution.md), not approval,
implementation or qualification.

The authorization is: “At a genuine missing contract, prepare the smallest
concrete decision needed as a new proposal doc using gpt-6.1-sol at max
reasoning, then continue with it being treated as a provisional
approval/direction.” This task changes only this proposal.

## Decision and classification

Recommend **no wire change**. An admitted L1 field operation publishes the
producer's operation-local authorization fact; source-free reconstruction
restores that fact for the compiler's mandatory verifier without inventing a
lexical unsafe scope. Keep exact identity
`trrm/1-draft-local-aggregate-layout-execution-1`, its closed R inheritance,
catalog, record shapes and zero reserved flags unchanged.

The source rule and producer-truth boundary already exist. The remaining
L1 integration decision is how scope-erased operations reconstruct the
specific field facts consumed by mandatory compiler verification. It is
**not** a missing requirement to serialize or re-prove source text, nor a
new permission for an unsafe operation.

| Inspected owner/evidence | Finding |
|---|---|
| [Grammar §5.3](../specs/grammar-spec.md#53-caller-unsafe-declarations-and-explicit-unsafe-scopes), [field declarations](../specs/grammar-spec.md#31-source-file-structure-and-declarations) | An explicit unsafe block/expression authorizes its operations. An unsafe declaration exposes caller obligations, not an unsafe body. The accepted producer-truth clause permits consumption without semantic re-proof of published body facts. |
| [Reader trust §§1–3, 5](../specs/representation/reader-trust.md) | Source producers check authored source; consumers may rely on truthful published facts. Structural/support/binding checks remain mandatory. A rewriter is responsible for facts it changes or publishes. |
| [PlaceSelection](../specs/il-instruction-set.md#typed-operand-records), [field/place semantics](../specs/il-instruction-set.md#fields-elements-and-ranges) | Record 04 is 24 bytes including its header; its final payload `u16 flags` is zero. No operation-local unsafe flag is assigned. FieldMap membership grants no access. |
| [FieldDef rules](../specs/il-metadata.md#metadata-logical-tables-and-row-semantics), [FieldDef writer](../../sources/Terrum.CodeAnalysis/Modules/TrRmFieldDefTableWriter.cs), [reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmFieldDefTableReader.cs), [map validator](../../sources/Terrum.CodeAnalysis/Modules/TrRmRegisterBodyMapValidator.cs) | Standard FieldDef admits visibility, mutable `0x04` and **static `0x08`**, not `RequiresUnsafeCaller`. The reader permits only mask `0x0F`; the field-place validator's `flags & 8` rejection excludes static fields. Bit number 8 (`0x100`) is also reserved, not an unsafe encoding. |
| [Raw Union owner](standard-trrm-raw-union-initialization.md#alternative-access-and-overlapping-writes) | Alternative unsafety derives from the containing selected Union layout, without a new unsafe FieldDef bit. Whole-Union Copy does not access an alternative. R's ReturnCoverage remains independent. |
| [Bound definitions](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [source binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [verifier](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs) | `BoundFieldRead` and `BoundFieldAssignment` have internal `IsUnsafeAuthorized` facts; `BoundStructField.RequiresUnsafeCaller` is a distinct declaration fact. The verifier checks field authorization as well as state/access. |
| [L1 producer](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutBodyWriter.cs), [reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.LocalAggregateLayoutReader.cs) | Statement production lacks `BoundUnsafe` handling; reconstructed field reads/assignments do not restore these authorization facts. These observations are implementation gaps, not evidence for allocating a flag or skipping verification. |

Thus the accepted producer-truth clauses resolve the need for a serialized
lexical proof. This proposal supplies only the explicit L1 reconstruction
rule below. No assigned flag serializes the in-memory Boolean; none is needed
to consume the operation's published fact.
No new test or execution result is claimed.

## Exact operation-local fact

For a source-produced operation, the published fact means:

> Every unsafe-required direct field access performed by this operation,
> including each such step in its selected containing path, was checked
> within an explicit unsafe block or expression covering that access.

For a destination write, this includes the destination operation, not merely
its RHS. An unsafe declaration, a caller's scope, an unsafe RHS alone, an
unsafe sibling operation, Union layout, Blittable, Copy or ReturnCoverage
does not establish this fact. A source-free producer remains responsible for
the equivalent operation-local unsafe responsibility; it cannot substitute
a caller-unsafe MethodDef for that responsibility.

The implicit publication belongs to the **decoded operation**, not its
FieldDef, type, enclosing method or artifact. Requiring unsafe access is not
evidence that authored source supplied it. Source production must establish
it before erasing scopes. Reconstruction consumes the valid Body operation's
published assertion under the accepted trust boundary; it does not infer a
source scope merely from the selected type.

This amendment covers L1's admitted field accesses, including raw Union
alternatives and interleaved field/array paths. It adds no transport for an
independently declared field-level `unsafe` obligation: the Standard
FieldDef schema has no such declaration flag. Retained declarations needing
that unassigned carrier must not silently lose the obligation. This does
not prevent the required raw Union path, whose requirement is derivable
from its existing layout.

| Operation | Publication/reconstruction obligation |
|---|---|
| Field read, including Copy extraction | Preserve authorization for each unsafe-required projection actually evaluated. An extraction from a copied value is still an alternative access, not whole-U Copy. The record-backed load and an admitted short extraction have the same producer obligation; neither needs a new flag. |
| Field destination selection and semantic initialization/replacement | Preserve the complete captured destination path and the authorization covering that write. `BoundFieldAssignment.IsUnsafeAuthorized` discharges only this operation's unsafe-field requirement, not RHS obligations or unrelated operations. |
| Nested/interleaved field and element paths | Preserve each field step's owner, selected layout, captured base/index and authorization. Carry unsafe-required ancestor facts through the captured path; a safe terminal field cannot erase an unsafe ancestor. An expression-scoped ancestor does not authorize a later unsafe projection outside that expression. |
| PlaceHandle aliases, joins and later uses | Keep authorization attached to the exact selected path/access and its surviving origins. Aliasing does not grant another access kind, retarget storage or create ambient unsafe scope. A merge must preserve all required incoming facts, not OR unrelated authorizations into a general capability. |
| Reference/address formation, take or retirement | Apply the same rule wherever independently admitted by L1/R; authorization does not admit a new operation. Raw Union alternatives still have no independent field-move/retirement state. Formation retains its separate backing, alignment, access and lifetime obligations. |
| Whole-U load/store/Copy; metadata-only field-path queries | Do not invent an alternative access or added unsafe obligation. Offset queries do not execute field access. An operation which subsequently accesses an alternative has its own obligation. |

Callable Effects and operation-local authorization remain separate throughout.
Internal unsafe work does not make an ordinary wrapper caller-unsafe.
An actually caller-unsafe retained declaration keeps its Effects contract
and its callers' independent obligations; that contract never authorizes
the declaration's body. No new callable/native admission follows.

## Production, reconstruction and verification

1. **Production:** check authored source, preserve its checked facts through
   normalization, and run mandatory per-function verification before
   production. Visit explicit
   `BoundUnsafe`/`BoundUnsafeExpression` scopes without losing their
   operations, evaluation/check order or cleanup. Erase only lexical
   scaffolding after the applicable operation facts are established.
   Never authorize a false source fact during emission or treat an unsafe
   RHS as covering an enclosing destination.
2. **Structural consumption:** before affected use, perform L1's existing
   exact-schema/framing, zero-flags, map/record/selector, bounds, reference
   namespace, owner/type/path, requirement, target and binding checks.
   Resolve raw Union requirements from the exact selected owner/layout,
   including imported field expectations and actual provider correspondence.
   Do not reinterpret the static flag. A shape-valid assertion is not a
   proof of arbitrary unsafe validity.
3. **Reconstruction:** only within that checked decoded-Body boundary,
   restore the published operation facts on the corresponding field reads,
   writes and captured paths. For a decoded raw Union field projection,
   restore only that projection's `IsUnsafeAuthorized` from its implicit
   assertion. A flattened destination assignment receives the corresponding
   fact only when every required assertion on its complete path survives;
   ordinary projections with no unsafe requirement gain no authorization.
   Equivalent internal representations are permitted, but must
   distinguish this provenance from unchecked source/generated nodes.
   Do not wrap the entire method/Block in `BoundUnsafe`, set every
   `IsUnsafeAuthorized`, or reconstruct a scope from callable Effects.
4. **Mandatory verification:** run the existing compiler verifier on the
   reconstructed program and after meaning-changing rewrites. It consumes
   the published operation-local fact rather than re-proving a missing
   lexical scope, while retaining all other applicable type, visibility,
   mutability, initialization/coverage, overlap, loan, provenance and
   publication checks. Do not mark the Body verified or bypass the pass.
   Unsupported facts or exhausted analysis must not produce partial output.

Structurally valid false source-authorization assertions violate producer
truth. Ordinary consumers are not required to detect that lie or recover
the original lexical scopes; a scoped audit may check available evidence
but must not claim source proof from the Body alone. Missing source
authorization is a mandatory source-checking negative, not an invented
malformed-byte case. Known coverage, validity or backing restrictions are
not erased by the assertion.

## Transformation and native consequences

Meaning-preserving rewriting/re-emission retains the operation facts with
their exact accesses, captured paths and using-Module context. Remap decoded
FieldDef rows, FieldMap/FieldPath references, TypeSpecs, Layout/Profile and
Import/provider correspondence, plus affected Homes, registers, records,
Blocks, instruction/source indexes and extents under their existing owners.
Rebuild records and maps; do not substitute matching octets or retain a
fact by row number/name after retargeting. A newly introduced or changed
unsafe access requires the transforming producer's own justification.

Preserve original expectations and every stronger relied-on fact, including
ReturnCoverage. Replacement that changes field/path access obligations,
owner/layout or caller-unsafety must satisfy the existing directed contract;
an old operation's authorization cannot discharge a new obligation.
Unchanged typed operations can be re-emitted without source or lexical
scope reconstruction, then reloaded and mandatorily verified again.

Native lowering needs no unsafe instruction, runtime flag or lexical region.
It consumes the verified typed operations, preserving actual accesses,
coverage/overlap invalidation, packing/alignment, backing, staging and
normal-result publication. This creates no NativeAddress/NativeImport
operation, foreign fallback, E1 composition, sample policy or new ABI.

## Material alternative: explicit per-selection assertion

A wire assertion would make the per-selection fact explicit but would
still be a producer assertion, not proof of an authored scope. It adds a
schema and extraction conversion obligation without improving this trust
boundary. **Not recommended or selected.** If chosen instead, the concrete
candidate is:

* Exact identity
  `trrm/1-draft-local-aggregate-layout-execution-field-unsafe-authorization-1`;
  this succeeds L1 for this use, not an alias or extra L1 decoder flag.
  Inherit L1's closed composition/catalog except this stated amendment.
  Keep `(1,0)`, codec 01, record kind 04 and its 24-byte extent.
* At record offset 22 (payload offset 18), assign `u16 flags` bit 0
  (`0x0001`) to `FieldUnsafeAccessAuthorized`; bits 1..15 remain zero.
  Header flags/reserved remain zero. Bit 0 is required exactly when a
  `targetKind=2` field step requires unsafe access under its selected
  layout; otherwise it is zero. Other targets require zero. It describes
  that step/access, not its method, RHS or all descendants.
* Consumers check this closed mask and conditional presence after resolving
  the field's owner/layout. Carry each asserted ancestor with its captured
  path. A nonzero bit on an inapplicable target, missing required bit or
  unknown bit rejects. No FieldDef bit is added.
* Recordless `70..7F` extraction cannot carry the assertion: restrict it to
  safe projections in this candidate and encode an unsafe extraction using
  an existing record-backed load from an exact private Copy snapshot Home.
  Preserve snapshot/evaluation semantics, not an address into original
  storage. Rebuild Homes/maps/records/instruction and source indexes.
* The selected initial operation requires this successor for root and every
  supplied provider, including type-only providers, as L1 does for itself.
  Existing L1 keeps zero flags and does not decode this bit. Conversion
  requires explicit source/destination selection and typed reconstruction;
  no relabeling, mixed-profile packing, retry or compatibility reader.

The recommended no-wire option neither allocates that bit/identity nor
supersedes L1. It makes L1's already required producer fact usable by its
mandatory compiler verifier; it does not change the meaning of reserved
bytes or broaden any other profile.

## Owners and short integration amendment

On approval/provisional implementation, amend only L1's **Body
representation** and **Verification, reconstruction and replacement**
clauses to require scope erasure after checking and the operation-local
producer-fact reconstruction above. Keep its exact selection, R inheritance
and source-free execution acceptance gates. Do not duplicate its
constructor, layout, frame or provider-closure contracts here.

The accepted [grammar §5.3](../specs/grammar-spec.md#53-caller-unsafe-declarations-and-explicit-unsafe-scopes),
[reader-trust](../specs/representation/reader-trust.md),
[field/place semantics](../specs/il-instruction-set.md#fields-elements-and-ranges),
[binding/replacement](../specs/representation/binding-and-replacement.md)
and [format-versioning](../specs/representation/format-versioning.md) owners
need no semantic replacement for the recommendation. Raw Union/ReturnCoverage
remains owned by its existing proposal. No registry, manifest Feature,
FieldDef, PlaceSelection or proposal-status allocation changes now.

## Required acceptance cases

These are implementation gates, not results of this documentation task.

| Class | Required outcome |
|---|---|
| Positive/source-free | An ordinary non-caller-unsafe constructor/helper explicitly scopes an alternative initialization and retains its real ReturnCoverage. Emit L1 without native imports/addresses, remove source/private artifacts, load, verify, permute/re-emit, reload, then execute headless JIT and AOT. Include separately selected L1 providers and unrelated synthetic types; inspect retained coverage and observe the returned value. Merely producing bytes, accepting a declaration or reporting unsupported is not completion. |
| Read/write/path | Scoped direct/extracted reads, initialized alternative replacement, unsafe ancestors with safe leaves, and admitted mixed field/array paths preserve exact targets, side-effect order and facts through aliases/CFG. Copies preserve coverage and do not evaluate alternatives. |
| Negative/source | Unscoped alternative access rejects, including inside an unsafe declaration/constructor. An unsafe RHS does not authorize its destination; one scoped ancestor/sibling does not authorize another unsafe projection. Scoped access still rejects readonly/invalid mutation, insufficient coverage, conflicting overlap loans, unavailable backing and incomplete publication under the existing rules. |
| Boundaries | Safe ordinary fields and whole-U Copy acquire no added obligation. Test expression versus block scope, nested unions, zero-data alternatives and empty unions under R's existing completion rules, packed fields and copied snapshots. No new padding initialization, live state or active-member tracker appears. |
| Malformed versus false facts | Nonzero record/header flags, bad extents/counts/selectors, wrong-namespace/owner/type/path references, static-as-instance fields and invalid requirements/bindings reject before use. A shape-valid but false scope assertion is a producer-truth/audit case, not mandatory detection of unavailable source. |
| Remapping/replacement | Consistently permute field/layout/type/provider rows and Body maps/records/Blocks, crossing short/long index boundaries. Preserve operation facts and original/actual provider obligations. Retargeted paths cannot inherit unrelated authorization; weakened coverage or incompatible layout/access/caller contracts reject. |
| Cross-profile | Preserve L1's exact input rule: supplied R/B/E1 or other non-L1 artifacts do not become L1 because field bytes match. Nonzero candidate authorization flags still reject under L1. No E1 regions, native fallback or feature/version sniffing; an unrepresentable explicitly selected destination fails without overwriting existing output. |

**Remaining decision:** formally approve or replace the recommended no-wire
L1 reconstruction amendment. No in-scope encoding choice is deferred to the
implementer. Authored field-level unsafe declaration transport, broader
profile compositions and qualification remain outside this decision;
implementation and the positive execution gates remain required.
