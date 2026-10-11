# TextTarget parameter conformance cases

These cases apply to [text-target-parameters/1](../language/construction-handlers.md#texttarget-parameter-schema)
and [eager-construction/1](../language/construction-handlers.md#eager-capture-and-acquisition).
Assume otherwise valid declarations and admitted writer/formatter/fragment
contracts. They are source/contract obligations, not compiler execution
results or admission of the still-open physical representations.

## Resolved schemas and parameter identity

| Case | Situation | Required result |
|---|---|---|
| TTSCH-01 | Primary callable has [TextTarget] TContent content with declared IFormattable requirement | Bind no-argument Core schema to that exact input formal |
| TTSCH-02 | Primary callable uses [param(content): TextTarget] | Same effective formal ordinal as inline placement |
| TTSCH-03 | Same-spelled user TextTarget attribute appears | No recognized content-construction capability |
| TTSCH-04 | TextTarget is supplied a string, nameof(content), typeof(Type) or other argument | Reject argument-bearing schema |
| TTSCH-05 | Inline and named placement both target one content formal | Reject effective duplicate |
| TTSCH-06 | Secondary partial adds or repeats TextTarget | Reject primary-contract change/duplicate |
| TTSCH-07 | TextTarget appears untargeted on callable, or on type/field/result/receiver | Reject wrong target; no parameter-name fallback |
| TTSCH-08 | Named selector is missing, ambiguous or chooses an implicit setter value | Reject unresolved/ineligible target |
| TTSCH-09 | Content input is parameter-only implicit ref<TContent> with the required witness | Use ordinary generated-value materialization/readonly borrowing and supporting scope |
| TTSCH-10 | Content position is explicit ref, write ref, init ref or params | Reject ineligible content-construction input shape |
| TTSCH-11 | Named constructor/accessor or corresponding interface requirement has an eligible explicit formal | Preserve its ordinary primary parameter contract |
| TTSCH-12 | Attribute targets a lambda/callback parameter | No new recognized callback schema |
| TTSCH-13 | Unconstrained type formal or same-spelled user IFormattable is supplied | No standard formatting capability from names; require the checked nominal witness |
| TTSCH-14 | Two independent content formals each have one annotation | Distinct parameter contracts; each is nonrepeatable |
| TTSCH-15 | Unrelated module/extension tries to annotate an existing formal | Reject wrong authorship; imports do not attach contracts |
| TTSCH-16 | Async named consumer has annotation but lacks required retained-capture/completion support | Annotation alone cannot admit the unsupported affected use |

## Typed eager formatter construction

| Case | Situation | Required result |
|---|---|---|
| TTCAP-01 | Interpolation has independently typed values and checked fragment operations | Form its module-local closed-shape formatter type and selected IFormattable witness |
| TTCAP-02 | Value, alignment and runtime format have effects | Evaluate each once in existing source order during capture, not TryFormat |
| TTCAP-03 | Call-side consumer is disabled and never formats | Capture effects still occur; ordinary parameter cleanup remains |
| TTCAP-04 | A later ordinary argument follows the interpolation | Complete its capture first; callee preconditions/body follow normal staging/checks |
| TTCAP-05 | Already typed expression Compute() is passed to the annotated input | Ordinary argument evaluation/binding; no deferred body or reconstructed interpolation |
| TTCAP-06 | Consumer formats the same still-live content twice | Source captures run once; each formatting call follows its own writer/effect/refusal contract |
| TTCAP-07 | Copied handle/ref capture is followed by an allowed backing mutation | Preserve shallow capture and later read observations; no deep snapshot |
| TTCAP-08 | A named unique input needs by-value capture without explicit move | Reject missing transfer; no implicit Copy |
| TTCAP-09 | Named unique input is explicitly moved into eager capture | Retain vacant source and actual captured owner cleanup/transfer |
| TTCAP-10 | Character literal is a text fragment | Use existing rune context, not writer code-unit type or default character type |
| TTCAP-11 | Numeric hole has no established target/type evidence | Diagnose missing numeric type; IFormattable alone supplies no default width |
| TTCAP-12 | Alignment/format/fragment role lacks its checked nominal operation contract | Diagnose unsupported affected use; no member-name operation discovery |
| TTCAP-13 | Quoted and raw interpolation denote the same validated fragment plan | Use their existing segment transformations and eager construction, not runtime source parsing |
| TTCAP-14 | Content default is omitted | Use ordinary selected published Copy constant, not hidden hole evaluation |

## Inference and selection

| Case | Situation | Required result |
|---|---|---|
| TTBIND-01 | Typed capture signature and fragment contracts are established at a marked content formal | Fixed generated type supplies forward ordinary value/implicit-ref evidence |
| TTBIND-02 | Desired formatter type would be needed to guess unresolved hole types | No inverse inference or guessed numeric target |
| TTBIND-03 | Explicit generic argument fixes content to a different complete type | Keep it fixed; no retargeting to generated content |
| TTBIND-04 | Another typed argument already establishes the content formal incompatibly | Diagnose inconsistent evidence under ordinary inference |
| TTBIND-05 | Two source occurrences in one module have equal complete capture types, modes, fragment graphs and selected witnesses | Same generated complete type; both can supply compatible evidence to one shared content formal |
| TTBIND-06 | Same occurrence closes the same ordered capture signature twice | Same fixed logical generated identity; execution remains fresh per activation |
| TTBIND-07 | Capture signature changes qualification or a nested type argument | Retain exact complete capture identity and witness requirements |
| TTBIND-08 | Two otherwise applicable candidates remain tied/ambiguous | TextTarget supplies no new quality/specificity preference or tie-breaker |
| TTBIND-09 | Declarations differ only by TextTarget attributes | Annotation is not a signature discriminator or attribute-only overload |
| TTBIND-10 | Consumer body formats successfully but required public constraints/fragment contracts are missing | Private body cannot repair caller applicability/admission |
| TTBIND-11 | Unannotated generic input has only an IFormattable constraint | Do not invent generated interpolation content from that constraint alone |
| TTBIND-12 | Unannotated concrete text target has a checked TextBuilder registration | Ordinary target-directed interpolation remains supported independently |
| TTBIND-13 | A selected type/callable has a failed complete capture check | Diagnose without retrying another declaration, parser or construction channel |
| TTBIND-14 | Source tries to extract unrelated generic type arguments from private generated fields | No new inverse field/type-recipe inference rule |
| TTBIND-15 | Otherwise equal content shapes use different capture modes | Distinct generated complete types; layout equality does not erase the modes |
| TTBIND-16 | Otherwise equal content shapes retain different selected formatting witnesses or required writer/member identities | Distinct generated complete types; matching member names/bodies do not merge identities |
| TTBIND-17 | Equal closed content shapes originate in different modules | Distinct nominal types; no cross-module structural type equivalence |
| TTBIND-18 | Equal shapes in one module have different captured values, literal text, alignment or format data | Same generated complete type; preserve each formatter's own data and output |
| TTBIND-19 | Equal capture fields use a different fragment-operation count, order or role | Distinct generated complete types despite equal field layout |
| TTBIND-20 | Two source sites share a generated complete type | Keep separate capture evaluations, transfers, holder/backing identities and supporting scopes |

## Ownership, witnesses and retention

| Case | Situation | Required result |
|---|---|---|
| TTLIFE-01 | Every complete stored capture has Copy capability | Generated content may have the checked composite Copy capability; copies preserve backing dependencies |
| TTLIFE-02 | Some stored capture is unique or non-Copy | No generated Copy capability; ordinary explicit transfer and owner cleanup apply |
| TTLIFE-03 | Captured form lacks required field storage/representation eligibility | Diagnose unsupported content formation, regardless of printable size |
| TTLIFE-04 | Content is passed by implicit readonly reference | Preserve ordinary backing temporary scope; no unconditional noescape or full-expression retirement |
| TTLIFE-05 | Formatting refuses after accepting a prefix | Stop later fragment operations for that invocation; no replay, rollback or implicit resumption |
| TTLIFE-06 | Consumer stores/returns content whose backing would be invalid | Reject missing lifetime/capture contract; annotation is not retention authority |
| TTLIFE-07 | Async consumer retains captured views | Require exact completion-bounded dependencies and cleanup; source capture being eager is insufficient |
| TTLIFE-08 | Interface requirement has TextTarget but selected implementation uses another name/annotation | Retain the requirement's exact parameter capability; forward formed content without source replay |
| TTLIFE-09 | Fresh content holder includes mutable shared backing under NoAlias | Holder identity/Copy does not establish region separation |
| TTLIFE-10 | User treats TextTarget as Pure, ConstEval, NoThrow or allocator permission | No such implication; selected operation/evaluation/provider contracts govern |

## Independent records and evolution

| Case | Situation | Required result |
|---|---|---|
| TTVER-01 | Parameter is renamed without changing its declaration ordinal | Retain bound parameter identity; independent records do not rebind by string |
| TTVER-02 | Producer removes or narrows required TextTarget capability | Breaking source construction contract for affected dependent clients |
| TTVER-03 | Producer adds TextTarget and source is recompiled | Applicability may change under ordinary rules; no retargeting an existing compiled call |
| TTVER-04 | Independent consumer lacks required TextTarget/capture rule or witness records | Reject affected use; no optional annotation erasure or private-body reconstruction |
| TTVER-05 | Same fields/printed text are used to merge content from different modules or different complete shapes | Reject claimed type equality; output/layout equality is not the module-and-shape identity rule |
| TTVER-06 | TextTarget is written into an unassigned metadata carrier or interpreted as a deferred producer | No carrier or incremental-content semantics admitted by this logical schema |
| TTVER-07 | Linking remaps references or source sites move without changing originating module and complete shape | Preserve logical content identity and exact dependencies; source coordinates/physical table numbers are not the key |
