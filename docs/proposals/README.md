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
| [Source arithmetic profile selection](source-arithmetic-profile-selection.md) | Unapproved, provisional per-compilation options and CLI selection with a deterministic Development default; preserves accepted arithmetic and explicit compiled policies without a profile wire field. |
| [Private arithmetic operation persistence](source-arithmetic-private-persistence.md) | Unapproved private revision 89 and per-operation mode-byte candidate for resolved checked/wrapping operations; distinguishes retained semantic bodies, already-lowered images, development executable encodings, and standard register Bodies. |

## Callable contracts

| Proposal | Review boundary |
|---|---|
| [Constructor no-return applicability](constructor-no-return-applicability.md) | Unapproved, explicitly provisional direction admitting instance and authored static constructors; preserves initialization/cleanup contracts and separate constructor/static representation gates. |
| [NoThrow retirement and proof boundaries](no-throw-retirement-and-proof-boundaries.md) | Unapproved, provisionally actionable retirement of the general source/export promise; preserves scoped internal no-failure proofs and separate cleanup/native obligations without restoring the retired Effects bit or adding a carrier. |
| [Static-initializer trigger identities](static-initializer-trigger-identities.md) | Unapproved Core schema/enum/case registration and exact trigger normalization; preserves both accepted choices and separate static metadata/runtime gates. |

## Batch 4: Encoding and operation representation

| Proposal | Review boundary |
|---|---|
| [Semantic IL and metadata extensions](il-extensions.md) | Remaining Body/runtime carriers, exception regions, roots/effects, sharing and inlining questions; TypeSpecRef and the register instruction/Body schema are integrated. |

## Metadata reference forms

| Proposal | Review boundary |
|---|---|
| [Body TypeMap complete types](body-typemap-pseudo-type-retirement.md) | Unapproved retirement of F0/F1 in favor of existing complete TypeSpec forms; distinguishes assigned numeric fields from missing elaboration, preserves identity/qualification and defines canonical writing/remapping without compatibility aliases. |
| [TextFragmentShape reference forms](text-fragment-reference-forms.md) | Unapproved namespaces and conditional references for captured values, formatter evidence and writer requirements; preserves the assigned 40-byte row and literal/formatted tags. |
| [Text-construction profile identities](text-construction-profile-identities.md) | Unapproved out-of-band per-using-Module Core identity context for exact formatter/writer role and signature validation; preserves existing declaration binding and fragment rows. |
| [Witness associated-type binding origin](witness-associated-type-binding-origin.md) | Unapproved closed `bindingOrigin` enum and materialized inherited selections for the assigned WitnessAssociatedType row; does not allocate associated-declaration identities or witness runtime carriers. |
| [ImmutableData declaration references](immutable-data-declaration-reference.md) | Unapproved direct-DefId target sets and owner substitution for constants and immutable regions; numeric identities, richer feature closure and publication/runtime carriers remain separate gates. |
| [ImmutableData definition-kind assignments](immutable-data-definition-kinds.md) | Unapproved `0x22/0x23/0x24` DefId candidates for fixed constants, selected associated-constant bindings and static fields; preserves the separate `0x20` associated-type candidate and publication/value/runtime gates. |

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
