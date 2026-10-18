# Terrum specification proposals

The [specification](../specs/README.md) records the currently accepted design,
not an immutable or stabilized language. A proposal may deliberately replace
accepted rules, reorganize records/reservations or reassign unpublished IDs.
Review that replacement as a whole; do not require draft compatibility or
prefer an additive patch solely to preserve current text or numbers.
The [conformance policy](../specs/overview/conformance.md) and
[format lifecycle](../specs/representation/format-versioning.md) govern the
distinction between current authority and future design.

These documents contain pending choices, integration recommendations or
independently useful alternatives/scenarios. Accepted portions are identified
and linked to their owners; they do not approve adjacent syntax, APIs or
encodings. Working prototypes do not approve a proposal. An intentional
amendment must state what it replaces, affected consumers, prerequisites and
approval still needed. Equal version bytes do not authorize interpreting
different draft schemas as interchangeable.

## Shared state, references and failures

| Proposal | Review boundary |
|---|---|
| [Reference-call metadata](reference-call-metadata.md) | Shared source/backing relationships, receiver/formal identity and publication/capture phases; physical integration remains pending. |
| [Executable output parameters](trrm-output-parameters.md) | Places, initialization destinations and result-correlated output state; development encoding is not frozen. |
| [Output, failure and ownership interchange](trrm-output-failure-ownership.md) | Joint state/phase/ownership admission, including raw native output validity without treating it as a checked destination or owner. |
| [Checked-exception TRRM](checked-exception-trrm.md) | A first executable exception slice, distinct from complete output/failure/ownership composition. |
| [Static initialization metadata](static-initialization-metadata.md) | Typed storage/group/unit identities and pending physical carriers for accepted initialization behavior. |

## Representation and encoding

| Proposal | Review boundary |
|---|---|
| [Semantic IL extensions](il-extensions.md) | Inline/compact type encodings, alternatives to Body-map-only integration, and measured size/reader consequences. |
| [Operation-encoding families](trrm-operation-encoding-families.md) | Combined family/reservation design, capacity, short forms and extension choices beyond the current registry. |

The [registry](../specs/representation/registry.md) owns current accepted
assignments. Candidate tables stay in their proposals until adoption;
collision checks alone do not select the best design. Lower fixed-width
numbers do not save bytes. The
[development-encoding inventory](../implementation/trrm-encoding-history.md)
describes current/private experiments, not alternate meanings of new IDs.

## Native boundaries, layout and application scenarios

| Proposal | Review boundary |
|---|---|
| [Unsafe FFI and native vtables](unsafe-ffi-vtable-cleanup.md) | Consolidated generic native/authored-library direction and remaining source/contract choices; no compiler-owned API or COM policy. |
| [Native boundary metadata](trrm-native-interop-metadata.md) | Coordinated candidate declaration, pointer, layout, dependency, output, callable and vtable representation with complete consumer obligations. |
| [Public fixed-layout integration](public-fixed-layout-profile.md) | Existing accepted target/source rules and natural-layout support versus wider controls, Union, standard carriers and ABI qualification. |
| [DX11 pipeline scenarios](dx11-direct-pipeline.md) | Authored application/lifecycle/image-evidence requirements, not a compiler integration API or universal pixel oracle. |
| [DX11 resize scenarios](dx11-event-driven-resize.md) | Application queue, authoritative extent, coalescing and cleanup behavior; window/hardware evidence remains opt-in. |

Native and generic metadata recommendations share one candidate allocation
and must reconcile declaration properties, complete callable identities and
dependencies before registration. Neither a working sample nor the
Windows ABI supplies missing language metadata.

## Libraries, numerics, proof and construction

| Proposal | Review boundary |
|---|---|
| [Core-library reference](core-library-reference.md) | Broad candidate APIs and dependency layers; accepted members/protocols link to the adopted inventory rather than being redefined. |
| [Numeric selection integration](numeric-selection-members.md) | Physical member/witness/program completion and separate candidate math APIs; selected signatures and scalar behavior are not reopened. |
| [Floating arithmetic NaN encoding](floating-arithmetic-nan-encoding.md) | Unselected fixed-runtime encoding alternative versus required quiet results and the selected deterministic constant recipe. |
| [Proof and constant-evaluation integration](static-proof-and-constant-evaluation.md) | Pending physical programs/value/registration records and optional extensions, not a competing proof floor. |
| [Generic nominal and construction metadata](generic-nominal-construction-metadata.md) | Complete instances/witnesses, registrations, generated shape and ordinary lowering; property/constraint/call/value codecs remain explicit gates. |
| [Registered UTF-8 string target](registered-string-target-binding.md) | Deferred target-specific registration/frozen parser model, distinct from an ordinary source-grant owned-row helper. |
| [Compare-exchange order pairs](atomic-cas-order-pairs.md) | Pending API/physical integration; the accepted order matrix remains in the memory model. |

## Self-host profiles, source views and invocation

| Proposal | Review boundary |
|---|---|
| [Bootstrap and scalar frontend profiles](bootstrap-frontend-profiles.md) | Consolidated expression/local/helper/namespace/projection/resource choices; bounded route limits are not language or ABI rules. |
| [Cleanup-contained switch transfers](scalar-frontend-cleanup-switch-transfer.md) | A bounded verification/proof strategy, independent of general switch semantics. |
| [Frontend verification budget](self-host-frontend-verification-budget.md) | Deferred decomposition/phase-handoff alternative with its own evidence and selection gate. |
| [Span-backed token cursor](span-backed-token-cursor.md) | Accepted bounded cursor exception versus pending general view support. |
| [Returned token spans](token-span-result-forwarding.md) | Accepted dependency relationships versus a concrete returned-view execution/promotion decision. |
| [Private token-span persistence](token-span-semantic-module.md) | Exact selected private schema and revalidation, not an obligation to retain historical revision bytes. |
| [Invocation-scoped source grant](invocation-scoped-source-grant.md) | Deferred consumer-specific authority and retirement contract. |
| [Native source-grant companion](native-source-grant-library.md) | Deferred native consumer/context ABI, scalar result and deployment contract. |
| [Persisted scanner invocation](scanner-invocation-profile.md) | Deferred integrated scanner consumer and whole-input/resource choices. |
| [Scalar frontend invocation](scalar-frontend-invocation-profile.md) | Distinct fresh-source/summary invocation identity, private-span requirements and bounded publication contract. |

## Review and implementation handoff

Review shared state and representation before dependent native, witness,
construction or invocation integration. Compare a minimal amendment with a
substantive redesign when the difference affects coherence; do not create
parallel permanent codecs merely to minimize churn. A proposal can remain
reviewable with an explicit blocked choice, but is not integration-ready
until its required source, record, admission, binding and execution contracts
are complete.

Current support belongs in the [implementation reference](../implementation.md);
remaining implementation and conditional qualification gates are in the
[compiler conformance handoff](../implementation/conformance-gaps.md).
The [roadmap](../roadmap.md) sequences remaining work. These roles do not
replace the accepted [source](../specs/language.md),
[representation](../specs/representation/README.md),
[runtime](../specs/runtime/README.md) and
[target](../specs/targets/README.md) owners.
