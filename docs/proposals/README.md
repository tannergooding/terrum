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

The [registry](../specs/representation/registry.md) owns current accepted
assignments. Candidate tables stay in their proposals until adoption;
collision checks alone do not select the best design. Lower fixed-width
numbers do not save bytes.

## Libraries and numerics

| Proposal | Review boundary |
|---|---|
| [Core-library reference](core-library-reference.md) | Broad candidate APIs and dependency layers; accepted members/protocols link to the adopted inventory rather than being redefined. |

## Batch 4: Encoding and operation representation

| Proposal | Review boundary |
|---|---|
| [Semantic IL and metadata extensions](il-extensions.md) | Remaining Body/runtime carriers, exception regions, roots/effects, sharing and inlining questions; TypeSpecRef and the register instruction/Body schema are integrated. |

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
