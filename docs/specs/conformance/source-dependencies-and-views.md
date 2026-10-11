# Dependency source and view conformance

These cases specify [general dependencies](../language/dependencies-and-views.md).
Fixtures supply exact declarations, source/target modes, published witness
facts, backing/capability identities and finite live-value graphs.
They do not prove arbitrary bodies, infer private layouts or implement a
collector, view ABI, task representation or physical dependency codec.

## Source recipes and declaration identity

| ID | Scenario | Required outcome |
|---|---|---|
| DSRC-01 | Exact Core DependsOn/Captures/ForwardsBacking schemas are resolved | Use their checked target/source meanings, not same-named user attributes |
| DSRC-02 | Annotation names a later formal with nameof | Bind the exact owning callable and declared ordinal |
| DSRC-03 | A direct text literal names that same formal | Same source identity; nameof remains preferred |
| DSRC-04 | Direct this recipe appears on an instance member | Bind receiver kind, never parameter ordinal zero |
| DSRC-05 | Escaped formal @this is named with nameof or literal | Bind the formal, not the implicit receiver |
| DSRC-06 | Direct this is used without a receiver | Diagnose missing source/target |
| DSRC-07 | Source is unknown, duplicated or repeated through equivalent recipes | Reject effective source conflict |
| DSRC-08 | Source argument is a field path, runtime value or computed string | Reject unsupported recipe, not runtime lookup |
| DSRC-09 | Inline/named placements duplicate one effective annotation | Reject nonrepeatable target placement |
| DSRC-10 | Secondary partial tries to replace a source relationship | Reject primary contract change |
| DSRC-11 | DependsOn() declares no borrowed receiver/formal source | Require independently valid result; preserve independent obligations without allocation or owner synthesis |
| DSRC-12 | ForwardsBacking adds a source absent from base/default contract | Reject refinement, not implicit retention |
| DSRC-13 | Named/positional call arguments reorder labels | Map sources to staged identities once in source order |
| DSRC-14 | Implementation formal names differ from interface labels | Map by checked requirement/formal identity |
| DSRC-15 | By-value scalar/unit is supplied from a caller local | No borrowed source place created |
| DSRC-16 | By-value handle/view is used as a source | Preserve carried backing, not original/callee handle or descriptor slot |
| DSRC-17 | Explicit init-output source is vacant in an outcome with a live view | Reject unavailable backing; no default output eligibility |
| DSRC-18 | Recipe claims a borrow of callee by-value parameter storage | Reject escaping dead parameter copy |
| DSRC-19 | Dependency schema omits its parenthesized source list | Reject malformed recipe; an empty set is explicit |
| DSRC-20 | Property-wide selector is ambiguous between accessors | Diagnose before source binding; do not synthesize a formal |

## Whole-source relationships

| ID | Scenario | Required outcome |
|---|---|---|
| DREL-01 | Dependency-bearing result has one eligible input/receiver | Use inherited/default broad source relationship |
| DREL-02 | Several eligible sources lack an explicit permitted set | Diagnose, not body-based selection |
| DREL-03 | Explicit sources differ across implementation branches | Caller retains every declared source |
| DREL-04 | Broad source permits inline storage or forwarded backing | Retain both possible bounds without requiring body to use both |
| DREL-05 | ForwardsBacking refines one source in a larger set | Remove only that own-storage alternative; other sources remain broad |
| DREL-06 | Result forwards a descriptor's existing backing | Descriptor can end when no remaining dependency needs its bytes |
| DREL-07 | Forwarded backing depends on an owner/lease/guard | Keep it valid; no permission to dispose or release |
| DREL-08 | Backing has separate data and shape-metadata sources | Preserve both complete dependency sets |
| DREL-09 | View is empty/end-position | Retain backing/root/guard/load obligations |
| DREL-10 | Equal numeric addresses are reused for different storage versions | Do not merge lifetime identities |
| DREL-11 | Caller wants to extend a source scope to admit the result | Reject missing bound; no hidden arena/retain |
| DREL-12 | Backing-only view's contents change under permitted mutation | Lifetime may remain valid; read-state facts invalidate separately |
| DREL-13 | Writable view/NoAlias is treated as exclusivity | No such authority; actual protocol still governs |
| DREL-14 | View is returned through physical hidden result storage | Preserve by-value semantics; no invented source loan |
| DREL-15 | Loan into moved owner's inline bytes survives the move | Reject unless an admitted preservation contract applies |
| DREL-16 | Owner transfer preserves allocation but not old wrapper bytes | Forwarded allocation and own-storage dependencies differ |
| DREL-17 | Original ref<T> input is returned under ForwardsBacking(source) | It still borrows the selected T place; reject unsupported narrower claim |
| DREL-18 | By-value view is forwarded without borrowing the caller descriptor | No own-storage alternative invented; retain actual carried backing |
| DREL-19 | Empty receiver/formal source set uses admitted immutable static backing | Preserve independent data/root/load dependencies |

## Capture destinations and phases

| ID | Scenario | Required outcome |
|---|---|---|
| DCAP-01 | Constructor Captures names borrowed inputs | Constructed receiver retains them for its full possible extent |
| DCAP-02 | this: Captures names source retained by existing receiver | Check receiver access, every alias and required source extent |
| DCAP-03 | param(holder): Captures selects retained storage/value | Exact destination identity and actual access/transfer, not ephemeral slot |
| DCAP-04 | return: Captures selects a returned holder | Preserve nested dependencies and publication/cleanup |
| DCAP-05 | Synchronous borrowed input has no declared retention | Cannot store beyond call except declared result relationship |
| DCAP-06 | Existing receiver stores borrowed state then call fails or its visible handles end | No rollback or inferred release; conservative retained-state extent remains |
| DCAP-07 | Fresh capture construction fails before publication | Clean acquired/private state once; no published holder |
| DCAP-08 | Caller moved an owner into a captured destination | No restoration; retain actual transferred instance and nested backing |
| DCAP-09 | Ordinary async Captures is applied to callable | Responsible operation extent, not payload or permanent receiver capture |
| DCAP-10 | Ordinary async return dependency targets a completed borrowed payload | Preserve payload's independent post-completion bounds |
| DCAP-11 | Generated producer has async body and source return dependency | Source result is synchronous creation cursor, not task/payload |
| DCAP-12 | Capture annotation attempts to weaken inherited producer/operation bound | Reject incompatible promise |
| DCAP-13 | Init-output dependency belongs to selected async publication | Respect reservation and exact checked observation |
| DCAP-14 | Task shape alone is used to defer init-output publication | Reject inferred completion relationship |
| DCAP-15 | Annotation attempts caller-local recipe capture or owner acquisition | No such source/capability |
| DCAP-16 | Instance source is ephemeral relative to a stored destination | Diagnose before invocation/publication |
| DCAP-17 | Named holder is a reference, handle or initialized output | Select referent/object/published value, not formal descriptor or output slot |
| DCAP-18 | By-value holder copy is used as a permanent caller destination | Reject fabricated escape; an actual result/transfer route retains dependencies |
| DCAP-19 | Non-async factory has an explicit completion-operation relationship | Capture its admitted operation extent without changing synchronous invocation failure |
| DCAP-20 | method: ForwardsBacking targets an admitted completion operation | Refine that capture only; no Task-shaped phase inference |

## Flow, copies and retirement

| ID | Scenario | Required outcome |
|---|---|---|
| DFLOW-01 | Descriptor/reference is copied under Copy contract | Duplicate complete dependencies, not referent/owner |
| DFLOW-02 | One of two dependent copies ends | Other copy still blocks backing retirement |
| DFLOW-03 | Last dependent view ends | Release its loan only; holder may separately release backing |
| DFLOW-04 | View is stored in wrapper/tuple/field or erased | Preserve dependency identities and access |
| DFLOW-05 | Two paths return views with different possible sources | Union their declared obligations at join |
| DFLOW-06 | Old source/result storage is retired and reused | Saved facts cannot attach to new version |
| DFLOW-07 | Guard/root/lease is released while a dependent view survives | Reject invalidating action |
| DFLOW-08 | Backing or required code/load artifact is reclaimed/unloaded | Reject while dependencies survive |
| DFLOW-09 | Mutable descriptor is rebound to another source | New provenance; old copies keep their own dependencies |
| DFLOW-10 | Stored holder's descriptor changes but broad capture bound remains | No private-body shortening of retained contract |
| DFLOW-11 | Source's dependency graph contains circular claims only | They do not create storage liveness or ownership |
| DFLOW-12 | Loop repeats a capture/materialization site | Existing scope/version rules; no retained-owner arena |
| DFLOW-13 | Pending async output is tested or reused through another alias | Reservation remains independent of view lifetime |
| DFLOW-14 | Completion task ends while published view remains live | Task retirement does not end its element/backing dependencies |
| DFLOW-15 | Old completion is reobserved after source/output reuse | Preserve original operation/version; no renewed proof |
| DFLOW-16 | NoAlias is satisfied at creation then references escape | No post-invocation separation guarantee |
| DFLOW-17 | Capture mutates a pre-existing destination in a purported pure form | Reject external effect; disjointness does not make it private |
| DFLOW-18 | A captured dependency is thread-affine or unshareable | Preserve complete handoff/share/affinity obligations |

## Witnesses and generic contracts

| ID | Scenario | Required outcome |
|---|---|---|
| DWIT-01 | Implementation's published source set is narrower than broad interface set | Compatible when all remaining validity/access bounds hold |
| DWIT-02 | Implementation forwards backing where interface permits storage borrowing | Compatible; preserve broad generic caller contract |
| DWIT-03 | Implementation adds source, capture or invalidation | Reject incompatible witness |
| DWIT-04 | Implementation narrows valid caller domain for refinement | Reject; annotation is not hidden precondition |
| DWIT-05 | Generic declaration has only broad interface conformance | Use broad source relationship |
| DWIT-06 | Generic declaration requires exact member ForwardsBacking sources | Establish the checked condition by published backing-only permission or exclusion of that requirement source |
| DWIT-07 | Member-contract projection lacks witness or is ambiguous/overloaded | Diagnose; no body/result-driven guessing |
| DWIT-08 | Constraint names a similarly spelled user schema/member | No registered fact |
| DWIT-09 | Concrete published witness establishes required refinement | Valid condition evidence, not private body inspection |
| DWIT-10 | Specialization/devirtualization observes narrower current behavior | Cannot strengthen unconstrained exported contract |
| DWIT-11 | Qualified-self projection maps source/result/input relationships | Preserve exact normalized types, identities, phases and bounds |
| DWIT-12 | Refinement removes cursor bytes but close releases a needed guard | Still blocked; lending operation invalidation is separate |
| DWIT-13 | Replacement weakens backing-only or adds retained sources | Reject incompatible relied-on contract |
| DWIT-14 | Rows/names are remapped across linking | Preserve semantic receiver/formal/target/witness identities |
| DWIT-15 | Member constraint selects exact initialized output | Preserve its result/output source identity and existing publication phase |
| DWIT-16 | Conjoined member facts are declared | Require all on the same selected witness; no alternative implementation search |

## Representation and independent admission

| ID | Scenario | Required outcome |
|---|---|---|
| DABI-01 | View sources have different owners | Same complete nominal Span<T>; no owner type argument or instance-dependent size |
| DABI-02 | Target supports an explicitly admitted fixed-size opaque anchor | Check all root/backing/transfer/ABI requirements |
| DABI-03 | Stored/erased view needs an unsupported representation | Reject before use; no boxing or allocation fallback |
| DABI-04 | Descriptor rooting is treated as resource/guard preservation | Reject conflated obligation |
| DABI-05 | Writable alias is manufactured through erasure/projection | Reject access/type upgrade |
| DABI-06 | Old TokenCursor/compact-owner support is read as source-language limit | Implementation inventory is not normative general contract |
| DABI-07 | Consumer lacks dependency rule or complete source/target records | Reject unsupported requirement |
| DABI-08 | Attribute/structural record is treated as proof of arbitrary body truth | Require checked evidence or explicit producer/audit trust |
| DABI-09 | Held opcode/table/optional Attr is used for mandatory dependencies | No physical allocation activated by logical schema |
| DABI-10 | View layout or anchor support changes behind same name | Compare actual retained target/layout/ABI contract |
| DABI-11 | General task/collector/shared-access behavior is inferred from source recipes | No such substrate selection |
| DABI-12 | Finite schema/lifetime fixtures pass | Not arbitrary-body, compiler, collector, concurrency or codec conformance |
