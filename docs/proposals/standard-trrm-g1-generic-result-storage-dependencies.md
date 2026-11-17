# G1 generic-method result storage dependencies

**Status: unapproved; provisional direction only under the user's authorized
missing-contract workflow.** This supplements the
[G1 proposal](standard-trrm-generic-method-call-body-encoding.md#invocation-results-and-failure);
it changes no accepted specification or support claim.

## Decision and scope

**Allow and preserve Dependency relationship `1=borrow source storage` for
G1 ordinary generic-method results.** Ordinary Param mode `00` does not mean
that a parameter cannot have checked-reference type. In particular, returning
the original initialized `ref<T>` input can truthfully use relationship 1:
its source storage is the selected **T referent**, not either the caller's
descriptor variable or the callee's parameter-copy slot.

Keep the current result-only projection: MethodDef-owned Dependency,
`destinationKind=1`, `destinationOrdinal=0`, `phase=1`; nonempty sources are
`sourceKind=1` with zero-based ordinary-parameter ordinals. Preserve 2 as
backing-only and 3 as the declared union. Do not equate 1 with either.
No new source annotation, row, tag, Feature, schema identity, Body operation,
reference representation or ABI is selected.

This supersedes only the provisional assumption that this projection is
exhaustively representable by DependsOn plus optional ForwardsBacking, hence
relationships 2/3. It preserves G1's exact-schema selection, full-domain
template checking and independent execution gates, and
[reference provenance carriers](reference-provenance-carriers.md#standard-exchange-reuse-not-another-instance-table),
which already requires preserving independently restricted relationship 1.
There is no remaining relationship-policy choice for this slice.

## Governing meaning and input shapes

The accepted owners are
[whole-source dependencies](../specs/language/dependencies-and-views.md#dependency-identities-and-obligations),
[reference descriptors](../specs/language/references.md#descriptors-referents-and-bindings),
[lifetimes](../specs/lifetimes.md#three-independent-dependencies) and
[Dependency clauses](../specs/representation/contract-clauses.md#6-dependency).
No accepted meaning is replaced. Type spellings below are accepted source
forms, not claims of complete G1 execution support.

| Source shape | Relationship-1 consequence |
|---|---|
| Admitted ordinary initialized, nonoptional, non-GC `ref<V>`, including admitted write qualifications | Borrow the selected live V place and eligible inline substorage. V may be a scalar, method formal, nested reference or admitted Span. Passing the descriptor by value does not remove this source. |
| `ref<ref<V>>` | Own storage is the inner descriptor's **slot**. Copying that stored inner descriptor instead forwards its carried backing; it must not be relabelled as a slot borrow. Returning the original outer descriptor remains storage borrowing. |
| Independent scalar/unit or by-value Span | Neither the caller variable nor the parameter-copy slot becomes eligible escaping storage. A result cannot justify such a borrow with code 1, or forward the Span's backing under code 1 alone. |
| Opaque by-value type formal | Keep the exact symbolic source/type and complete declared possibilities. Do not infer a storage-bearing shape from a proof placeholder or one closed reference actual, or silently restrict the generic caller domain. A use needing unavailable source/provenance facts is unsupported. |
| `ref<init V>` or another initialization output | Not an ordinary live input source. An explicitly published output-as-source needs its own complete outcome/publication obligations; that route is outside this bounded projection. Reading vacant storage is invalid, not a missing codec workaround. |
| Otherwise valid optional, GC-backed, nominal, receiver or deferred-phase source needing missing support | Refuse the affected use as unsupported; this decision supplies none of its missing type, phase or runtime prerequisites. |

These are source **permissions**, not a requirement that every listed source
be used. A well-framed code-1 row alone does not assert an actual borrow of
ineligible storage. Distinguish an invalid actual parameter-slot escape or
false forwarding claim from a known relationship whose required consumption
is unsupported. Do not diagnose all code-1 inputs as malformed.

Borrowing retains the selected place's actual generation and all supporting
backing, root, owner/guard, access, invalidation and code/load obligations.
“Storage only” excludes independently selecting forwarded backing from that
source; it does not strip capabilities necessary to support the borrow.
Ending/rebinding the input descriptor does not end its referent, and ending
the referent cannot be repaired by keeping descriptor bytes alive.

## Exact transport and provider compatibility

Reuse the assigned 20-byte Dependency row unchanged. Normalize broad source
DependsOn to 3 and its selected ForwardsBacking sources to 2. Source spelling
does not independently express 1: retain it from a truthful published contract
without inventing an attribute or narrowing an exported broad promise from
private body knowledge. Re-emission preserves 1 exactly.

After identity resolution, complete substitution and ordinal source mapping,
compare **each source's permitted alternatives**, not numeric tag ordering:

| Expected permission | Permitted provider permission at that source |
|---|---|
| Omitted source, `0` below | `0` |
| `1` storage only | `0`, `1` |
| `2` backing only | `0`, `2` |
| `3` union | `0`, `1`, `2`, `3` |

Here 0 means exclusion of that source from an **established effective result
relationship**, not a sourced row with relationship zero. Equivalently,
provider alternatives must be a subset of expected alternatives for every
source, including provider-only sources. Thus 1 and 2 are incomparable; either
can satisfy 3. Exclusion satisfies either refinement without a redundant
ForwardsBacking annotation. Check multiple sources independently; one
source's narrower relation cannot compensate for another's addition/widening.

Preserve the explicit empty-source row and its prohibition on mixing with
sourced rows. Absence of the whole relationship is not an empty set:
establish any governing default/inherited normalization before comparison;
missing required normalization/support refuses the binding. All other exact
signature, caller-domain, phase and validity obligations still apply.

Keep each original expectation separate from the provider's contract.
Calling through expected 3 retains that bound even when provider 1 satisfies
it. Exploiting the stronger published 1 retains an additional replacement
obligation; a later provider offering only 3 fails that obligation.
This specializes the existing
[directed binding rules](../specs/representation/binding-and-replacement.md#3-bounds-predicates-and-retained-observations),
not a new provider lookup or ABI compatibility rule.

## Implementation consequences and evidence

* **Semantic representation:** retain an exact per-source normalized
  storage/backing permission through declarations, expectations, calls and
  instances. The current annotation-shaped `BoundDependencyContract` alone
  cannot distinguish 1 from 3. Do not decode 1 as plain DependsOn, turn it into
  ForwardsBacking, or serialize compiler enum numbers as wire tags. Shared
  traversal, rewriting, merging and verification must preserve the distinction.
* **Reader/writer:** extend
  [`DecodeG1GenericMethodDependencyContracts`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceReader.cs)
  beyond its current `2 or 3` support gate and make
  [`EncodeGenericMethodDependencyContracts`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodSourceWriter.cs)
  preserve imported normalized 1 while retaining source 3/2 normalization.
  The shared
  [Contract reader](../../sources/Terrum.CodeAnalysis/Modules/TrRmContractTableReader.cs)
  already recognizes relationship values 1..3.
* **Binding:** implement the table above in
  [`GenericMethodDependencyContractsSatisfy`](../../sources/Terrum.CodeAnalysis/Modules/TrRmGenericMethodProviderBinding.cs).
  Its current expected-forwarding subset check also wrongly rejects excluding
  an expected backing-only source; compare only provider-retained sources.
* **Private persistence:** the existing revision-106 general Dependency
  encoder, reader and replacement comparison now preserve relationship 1.
  This does not persist open generic methods or complete their private call
  route; those remain separate gates.
* **Verification:** retain ordinary extent, owner/range, source-ordinal,
  uniqueness, empty-set and support checks. Producers check authored body
  truth over the entire generic domain. Under
  [reader trust](../specs/representation/reader-trust.md), source-free consumers
  need not re-prove provider body truth; unsupported facts cannot be fabricated
  under that permission.
* **Runtime/lowering:** bind formals to once-staged actual descriptors/places,
  retain exact referent and supporting obligations through substitution and
  normal publication, and preserve them when closing/cloning a Body. No
  implicit retain, root, pin, scope extension or ownership transfer occurs.
  Missing reference-result lowering/ABI support remains an execution blocker.

The active
[`EmitsAndReconstructsGenericReferenceResultDependencies`](../../tests/Terrum.CodeAnalysis.Tests/Modules/TrRm/TrRmGenericMethodSourceWriterTests.cs)
currently annotates returning the original `ref<T>` with ForwardsBacking.
That is not evidence that identity forwarding means relationship 2: it
conflicts with accepted
[DREL-17](../specs/conformance/source-dependencies-and-views.md#whole-source-relationships).
Correct that fixture when implementing this decision; do not approve its
premise. The source reader's ordinary-mode check already admits checked-ref
TypeSpecs, so blanket rejection of 1 cannot be justified by Param mode `00`.

## Focused acceptance cases

These are required tests, not executed results. Use the existing public
compiler APIs and active G1 source-writer/provider-import fixtures.

| Case | Required outcome |
|---|---|
| Independently constructed truthful open generic reference-identity provider with code 1, plus bodyless expected import | Source-free decoding, compatible binding and re-emission preserve 1; ordinary `ref<T>` source remains eligible despite mode `00`. Check scalar and nested reference/Span referents without flattening qualifiers. |
| Data-driven expected/provider matrix above, including both one-source and mixed-source contracts | Accept precisely the listed combinations. Cover excluding expected 1/2 sources, adding a source, renaming formals, and rebinding to provider 3 after relying on 1. Retain the expected broad view separately. |
| Original outer reference returned under 2; parameter-copy-slot escape from by-value scalar/Span; referent retirement with a surviving code-1 result | Authored-source/finite lifetime checks reject the invalid claim or action. Copying the original reference descriptor does not make it backing-only; metadata presence cannot create storage. Use a genuine by-value Span backing-forwarding case as the code-2 positive control. |
| Explicit empty versus absent/default relationship; duplicate source rows, empty/nonempty mixing, bad ordinals or unknown relationship values | Preserve established empty/default distinctions; reject malformed structure. Valid required receiver/output/GC forms outside this projection refuse as unsupported, not as unknown code 1. |
| Nested-reference slot versus copied inner reference; multiple possible sources; named-argument staging | Preserve own-storage versus forwarded backing, every declared source, once-only source-order evaluation and declaration-ordinal mapping. No private branch or closed instantiation narrows the exported contract. |
| Source-free table/Body remapping and later closed lowering | Rebuild decoded ownership/ranges, references, maps, record/code offsets and optional source data as needed; retain relationship 1 and every original/reliant provider obligation. No matching-byte substitution or schema fallback. Execute only once the independently required lowering/ABI route exists. |
