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
| [Decimal division by infinity: zero cohort](decimal-division-infinity-cohort.md) | Provisional minimum-quantum signed-zero rule for finite decimal/infinity division; preserves finite/finite preferred exponents and remains pending normative adoption. |
| [Explicit char16-to-fixed-integer conversion](char16-fixed-integer-conversions.md) | Unapproved, provisionally actionable ten-pair unsigned-code-unit extraction direction; compares the lossless approved floating bridge and four-pair compatibility amendment, fixes narrow checked boundaries, and preserves character identity and separate persistence/lowering gates. |
| [Source arithmetic profile selection](source-arithmetic-profile-selection.md) | Unapproved, provisional per-compilation options and CLI selection with a deterministic Development default; preserves accepted arithmetic and explicit compiled policies without a profile wire field. |
| [Private arithmetic operation persistence](source-arithmetic-private-persistence.md) | Unapproved private revision 89 and per-operation mode-byte candidate for resolved checked/wrapping operations; distinguishes retained semantic bodies, already-lowered images, development executable encodings, and standard register Bodies. |
| [Wide scalar value carriers](scalar-wide-value-carriers.md) | Unapproved, provisionally actionable revision-94 composition for exact typed scalar payloads; records the existing two-word carrier and current remaining private codec gap, and supersedes the earlier private persistence draft as candidate owner. |
| [Private scalar frozen-value persistence](scalar-frozen-value-private-persistence.md) | Superseded by [Wide scalar value carriers](scalar-wide-value-carriers.md); retained for historical rationale only, not as an alternative codec. |

## Callable contracts

| Proposal | Review boundary |
|---|---|
| [Constructor no-return applicability](constructor-no-return-applicability.md) | Unapproved, explicitly provisional direction admitting instance and authored static constructors; preserves initialization/cleanup contracts and separate constructor/static representation gates. |
| [NoThrow retirement and proof boundaries](no-throw-retirement-and-proof-boundaries.md) | Unapproved, provisionally actionable retirement of the general source/export promise; preserves scoped internal no-failure proofs and separate cleanup/native obligations without restoring the retired Effects bit or adding a carrier. |
| [Private NoThrow persistence retirement](no-throw-private-persistence.md) | Unapproved candidate revision 91 for retirement on the current 89 layout, then 92 for composition with aggregate outputs; leaves candidate 90 unchanged, retires serialized declaration/import promises and preserves scoped proof, exact admission and native regeneration boundaries. |
| [Exact native calling-convention source schemas](native-calling-convention-source-schema.md) | Unapproved, provisional exact Core enum and named CallConv schemas for NativeImport/NativeCallable, interface defaults and method overrides; preserves standard clause `000C`, convention values 1..4, fnptr type-use identity and the dependent foreign-interface NativeOutput candidates. |
| [Standard TRRM function-pointer anchor contracts](standard-function-pointer-anchor-contracts.md) | Unapproved, provisional completion of TypeSpec `0x0D` anchor applicability, indirect-call obligations and normalized identity; retains the seven-byte Standard row and separate capability gates, with predicates bounded to the phase-3 subset selected by the companion composition decision. |
| [Standard function-pointer source contracts](standard-function-pointer-source-contracts.md) | Unapproved, provisional source meaning for default `fnptr`, inferred NativeAddress contracts and checked directed projection; keeps exact type identity separate from forgetting stronger guarantees. |
| [Standard function-pointer default convention](standard-function-pointer-default-convention.md) | Unapproved, provisional TypeSpec-only candidate `0x00=Terrum`, distinct from Unmanaged; leaves native declaration convention domains unchanged and selects no universal machine ABI. |
| [Standard native-address formation](standard-native-address-formation.md) | Unapproved, provisional Standard register-IL `E9`/Body record `0D` carrier, deliberately distinct from the aggregate-output proposal's `DF`/`0C`; materializes an actual target address separately from its signature anchor. |
| [NativeImport obligations on Standard function pointers](standard-native-import-function-pointer-contracts.md) | Unapproved provisional design preserving exact declaration-owned NativeOutput/NativeParameter invocation facts through address formation, structural pointer views and every indirect call; proposes bounded Contract `000E`/Feature `16/1`, original-provider binding and an exact private-97 amendment. Supplies no compiler/runtime evidence. |
| [Complete function-pointer contracts in private persistence](native-function-pointer-contract-private-persistence.md) | Unapproved amendment to the unimplemented private revision-97 candidate: persist complete Effects, bounded selectors, checked outputs and dependencies, with exact pointer-type references in bodyless expectations. |
| [Terrum function-pointer addressability](terrum-function-pointer-addressability.md) | Unapproved, provisional ordinary static-function addressability rule and composition of the existing candidate Standard address carrier; requires a separately selected execution contract. |
| [Terrum function-pointer same-runtime call ABI](terrum-function-pointer-call-abi.md) | Unapproved, provisional bounded Windows x64 rootless-Copy ABI for jointly lowered Terrum-to-Terrum pointer calls; does not establish a universal or independently replaceable ABI. |
| [Checked function-pointer output parameters](function-pointer-checked-output-parameters.md) | Unapproved, provisional complete `ref<init T>` signature/state contract and bounded same-runtime output-address ABI addition; reuses shared selectors/dependencies, proposes scoped Standard formation flags, keeps NativeOutput separate and names the remaining private checked-reference wire decision. |
| [Predicate Feature composition for outputs and anchors](predicate-feature-composition.md) | Unapproved, provisional selector-only `10/1` with no prerequisites and unchanged `8/1 -> {1/1,2/1,10/1}`; restricts anchor predicates to that exact subset, gates general graphs, and defines deterministic per-Module composition without a new ID/revision. |
| [Native calling-convention Core registration](native-calling-convention-core-registration.md) | Unapproved, provisional per-Module exact Core enum/schema hand-off over shared nominal lookup and typed constant machinery; rejects name/value impersonation and leaves production provider selections and source-independent enum/schema codecs explicit. |
| [Nominal enum constant forwarding](nominal-enum-constant-forwarding.md) | Unapproved, provisional shared nominal-value/evaluator integration for int32-backed checked enums; preserves exact enum identity through ordinary copies, aliases, conditionals and ConstEval/runtime calls, excludes native-specific evaluation and leaves raw-enum persistence separately gated. |
| [Native calling-convention private persistence](native-calling-convention-private-persistence.md) | Unapproved, provisionally actionable exact revision-97 composition on candidate-96 identity; uses `NativeCallable:K` and `NativeImportCallConv:K` declaration tokens, keeps fnptr type-use convention distinct, and leaves outer-3 table bytes unchanged by joining direct-import expectations to the inner declaration. Source/standard ownership remains separate. |
| [NativeImport resolution private persistence](native-import-resolution-private-persistence.md) | Unapproved, provisionally actionable amendment to candidate 97 adding exact resolution tokens to declaration Contracts without changing Function or outer-3 row bytes; actual Link/Dynamic target support and private static-data transport remain separate gates. |
| [NativeImport LibraryName source contract](native-import-library-name-source-contract.md) | Unapproved, provisionally actionable printable-ASCII bare-name source domain for LibraryName; preserves spelling, adds no suffix/path inference or source-specific length cap, and separates standard/private serialization bounds from actual target support. |
| [Result-correlated aggregate native outputs](aggregate-native-outputs.md) | Unapproved, provisional vacant-only `Blittable Copy` outputs preserving NoThrow retirement; recommends shared OutputState/Dependency extensions, bounded result-selector Feature 10, new Feature 8 beside the native family, and DF/record-0C call reservations as prerequisites for the standard native path. |
| [Aggregate native-output private persistence](aggregate-native-output-private-persistence.md) | Unapproved candidate private revision 90, declaration-owned normalized output contracts and reservation expression tag 41; preserves exact inner-schema admission, dependency/replacement/remapping obligations and version-2 native containers without reinterpreting legacy output tuples. |
| [Aggregate native-output source dependencies](aggregate-native-output-source-dependencies.md) | Unapproved, provisionally actionable target-only amendment reusing DependsOn/ForwardsBacking for published inner aggregates; preserves the empty-set default, exact input/receiver sources, normal-outcome publication and no output-address escape, without new source or wire schemas. |
| [Scalar and pointer native-output cells](native-pointer-output-ownership.md) | Unapproved, provisional widening of NativeOutput to directly interoperable scalar/address and aggregate Copy cells; retains result-correlated vacancy/publication, shared carriers and provider binding, with explicit authored resource adoption rather than ownership inferred from pointer/handle bits. |
| [Foreign-interface method identity for NativeOutput](native-output-foreign-interface-method-identity.md) | Unapproved, provisional nominal flat foreign-interface registration and exact owner/member binding; reuses interface/method and callable contracts, proposes one required layout-role record, and preserves the output-cell semantic owner, raw receiver obligations and separate private-persistence gate. |
| [Foreign-interface identity private persistence](native-output-foreign-interface-private-persistence.md) | Unapproved candidate-96 identity/target design on complete 95; its foreign convention byte is superseded by the exact revision-97 composition in [Native calling-convention private persistence](native-calling-convention-private-persistence.md). It retains the output collection and outer-3 semantic-only edges separately from standard TRRM. |
| [Direct native-output provider binding](native-output-direct-import-provider-binding.md) | Unapproved, provisionally actionable outer version-3 provider/export binding and foreign-expectation table; its standalone inner selection is revision 92, superseded by the revision-94 composition in [wide scalar value carriers](scalar-wide-value-carriers.md). |
| [Static-initializer trigger identities](static-initializer-trigger-identities.md) | Unapproved Core schema/enum/case registration and exact trigger normalization; preserves both accepted choices and separate static metadata/runtime gates. |

## Batch 4: Encoding and operation representation

| Proposal | Review boundary |
|---|---|
| [Semantic IL and metadata extensions](il-extensions.md) | Remaining Body/runtime carriers, exception regions, roots/effects, sharing and inlining questions; TypeSpecRef and the register instruction/Body schema are integrated. |

## Metadata reference forms

| Proposal | Review boundary |
|---|---|
| [Reference provenance carriers](reference-provenance-carriers.md) | Unapproved, provisionally actionable operation-derived place/value/backing ledger and private revision-93 declaration collection; reuses shared Dependency rows and keeps executable root/pin support and target descriptor/anchor ABIs separately gated. |
| [Field-projected value provenance](reference-field-value-provenance.md) | Unapproved, provisionally actionable entry-value copy-origin summaries and exact private revision-95 declaration transport; preserves field identity across ordinary calls without narrowing whole-source bounds, adding annotations or supplying a standard summary codec. |
| [Standard TRRM copy-origin summaries](reference-copy-origin-trrm.md) | Unapproved, provisionally actionable Contract `000D`/Feature `15/1` candidate reusing FieldPath and exact contract-only type/field binding; preserves the semantic origin tuple, broad bounds and private revision 95. |
| [Exact-place roots and explicit pin runtime](reference-root-pin-runtime.md) | Unapproved, provisionally actionable Windows x64 stable-cell carrier, explicit root/pin acquisition and retirement, whole-object pin conversion, and candidate Feature 14/TargetProfile 2/F0/Body-0D integration; preserves the separate provenance ledger and private-persistence gates. |
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
