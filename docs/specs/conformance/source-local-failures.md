# Local failure eligibility and typed catch conformance

These cases apply to [local failure types and catch bindings](../language/local-failures.md),
ordinary complete types/witnesses and the existing incremental unwind rule.
They do not claim compiler/runtime support or allocate a physical failure
codec. Each fixture supplies the stated complete declarations and runtime
contracts; actual evidence limits remain explicit.

## Registered nominal opt-in

| ID | Fixture | Required outcome |
|---|---|---|
| EXRG-01 | Primary nominal struct has resolved no-argument Exception | Eligible, without added Copy or storage facts |
| EXRG-02 | Primary unique class has resolved Exception | Eligible under its own complete ownership/representation contract |
| EXRG-03 | Primary enum and union have resolved Exception | Eligible without adding interface-conformance syntax |
| EXRG-04 | Type is named Exception or ParseException but is unannotated | Ineligible; spelling supplies no opt-in |
| EXRG-05 | Same-spelled attribute resolves to another module identity | Ordinary attribute, not exception opt-in |
| EXRG-06 | Exception receives positional, named or runtime predicate arguments | Diagnose invalid schema |
| EXRG-07 | Exception repeats on one primary or across partial placements | Diagnose; no last placement wins |
| EXRG-08 | Exception occurs only on a secondary partial | Diagnose primary-contract violation |
| EXRG-09 | Exception targets static class, alias, formal, field, callable or parameter | Diagnose inapplicable target |
| EXRG-10 | Nominal type explicitly witnesses a marked category interface | Eligible through its exact declared witness |
| EXRG-11 | Type witnesses a derived interface inheriting a marked closed category | Inherited eligibility; retain exact closed identities |
| EXRG-12 | Duplicate inherited paths reach the same category | Deduplicate the same identity; no ambiguity or repeated annotation |
| EXRG-13 | Conflicting witnesses or cyclic category inheritance | Diagnose under ordinary witness rules |
| EXRG-14 | Matching methods, imported extension or unrelated adapter resemble category | No conformance or eligibility |
| EXRG-15 | Direct annotation and compatible category witness coexist | One compatible eligibility fact; not a repeated annotation |
| EXRG-16 | Actual Core bootstrap and independently compiled schema registration | Requires real compiler/registration evidence, not a finite declaration map |

## Complete subjects, representation and constraints

| ID | Fixture | Required outcome |
|---|---|---|
| EXTY-01 | Transparent alias resolves to an eligible complete payload | Preserve target identity/eligibility |
| EXTY-02 | Alias expansion cycles | Diagnose; no invented eligible target |
| EXTY-03 | Unannotated Transparent wrapper has eligible field and equal layout | Ineligible wrapper |
| EXTY-04 | Annotated wrapper retains a complete owner/descriptor field | Eligibility does not waive its dependencies or retirement |
| EXTY-05 | Generic T requires resolved Exception and is instantiated with eligible E | Check selected complete T without rewriting or inferring it |
| EXTY-06 | T lacks the required Exception fact or a category witness is malformed | Diagnose; body/name/layout inference cannot repair it |
| EXTY-07 | Annotated generic family is formed under its ordinary constraints | All admitted complete instances retain their declared eligibility |
| EXTY-08 | Capability-specific where clause follows Exception | Diagnose unsupported annotation clause; header constraints remain ordinary |
| EXTY-09 | Distinct nominal modules, generic arguments or representations share bits | Remain distinct failure/catch subjects |
| EXTY-10 | E, write C, ref<C> and ref<write C> participate in catches | Preserve exact stored qualifications; no reference covariance |
| EXTY-11 | Mandatory scalar, void or raw descriptor is proposed as annotated payload | Diagnose non-nominal target or ineligible throw form |
| EXTY-12 | Annotated zero-data nominal aggregate is complete | Eligible; not void and not fabricated initialization |
| EXTY-13 | Optional E is absent or thrown without presence refinement | Not an eligible E payload; no implicit null/default failure |
| EXTY-14 | Bare category interface is used as a fresh payload | Diagnose absent concrete storage; no implicit boxing |
| EXTY-15 | Eligible complete E has no supported transport/layout or retained source | Decline unsupported use or diagnose invalid dependency; marker supplies neither |
| EXTY-16 | A borrowed wrapper would outlive its source frame, guard or exact generation | Reject invalid escape; bytes/root alone do not retain authority |

## Fresh throws and responsibility

| ID | Fixture | Required outcome |
|---|---|---|
| EXTH-01 | Throw operand has observable evaluation and is complete | Evaluate once; no normal continuation |
| EXTH-02 | Named unique eligible owner is thrown with move | Transfer once, vacate source and preserve dependencies |
| EXTH-03 | Named unique operand omits move | Diagnose missing transfer, not implicit consumption |
| EXTH-04 | Fresh complete unique result is thrown | Transfer directly without Copy |
| EXTH-05 | Named Copy payload is thrown | Perform ordinary value copy; original source remains live |
| EXTH-06 | Operand evaluation fails before producing the outer payload | Propagate actual operand failure; fabricate no outer payload/event |
| EXTH-07 | Payload is vacant or structurally partial | Diagnose incomplete whole-value use |
| EXTH-08 | A catch value copy is freshly thrown | New origin; copied data does not preserve the handled event |
| EXTH-09 | Fresh throw uses a Copy handle to the same object | New event/provenance; no required deep snapshot |
| EXTH-10 | Fresh throw later fails delivery after a successful move | Retain responsible state; do not restore the vacated source |
| EXTH-11 | Exception opt-in is treated as NoThrow, Pure, Blittable or thread permission | Reject inferred unrelated capability |
| EXTH-12 | Failure is internally handled under valid effect bounds | No escaping-failure fact by mere presence of throw; retain actual effects |
| EXTH-13 | Payload carries unsettled task/guard/protocol responsibility | Must settle or validly transfer it; catching cannot silently abandon it |
| EXTH-14 | Actual partial construction, every-exit delivery and resource failure | Requires runtime/state evidence beyond supplied finite obligations |

## Explicit catch bindings

| ID | Fixture | Required outcome |
|---|---|---|
| EXCB-01 | ref<E> names a complete non-Copy payload | Borrow readonly descriptor; original propagation owner remains |
| EXCB-02 | Value E names a Copy payload | One ordinary copy; original event remains independently responsible |
| EXCB-03 | Named value E has no Copy contract | Diagnose even if today's pending failure would not match |
| EXCB-04 | E underscore tests a non-Copy payload | No local/read/copy; exact type test remains |
| EXCB-05 | Marked category I underscore sees an exact declared witness | Match without interface storage or payload copy |
| EXCB-06 | Category I has no matching closed witness | No match; no structural or open-generic fallback |
| EXCB-07 | Unmarked interface is used as a category, without a marked base | Diagnose ineligible catch subject |
| EXCB-08 | Named value uses bare interface I | Diagnose absent storable value, even for Copy concrete payload |
| EXCB-09 | ref<I> uses bare interface as referent, including underscore | Diagnose absent ordinary interface place; no raw reinterpretation |
| EXCB-10 | Catch uses writable, pinned, pointer, init, implicit or mutable input | Diagnose unsupported catch mode |
| EXCB-11 | Borrow name is used with member dot as though it were E | Ordinary descriptor lookup; use dereference or arrow for E members |
| EXCB-12 | Borrow attempts whole payload move or slot replacement | Reject readonly referent consumption/write |
| EXCB-13 | ref<write C> observes actual stored write C | Retain existing inner object authority, not writable slot authority |
| EXCB-14 | ref<C> is tested against stored write C | No match through inner qualification attenuation |
| EXCB-15 | Concrete nominal/representation/argument mismatch reaches catch | No conversion, box, adapter or widening is tried |
| EXCB-16 | Type mismatch has an effectful filter | Execute no filter or input formation |
| EXCB-17 | Match has a named input and filter | Initialize input before once-only filter; scope is candidate-local |
| EXCB-18 | Value Copy contains a scalar versus an aliased handle | Scalar copy independent; reachable handle state need not be a snapshot |
| EXCB-19 | Borrowed or copied input exits on filter false, failure or body exit | Retire its own obligations, never the borrowed original twice |
| EXCB-20 | Actual typed storage, roots, Copy helpers and reference lowering | Requires compiler/runtime evidence, not finite binding facts |
| EXCB-21 | Catch Type alias resolves to ref<E> or ordinary E | Select normalized borrow versus Copy/test mode without name-based adaptation |
| EXCB-22 | Alias hides writable/pinned/pointer input, or ref<ref<E>> is requested | Reject forbidden form or ineligible inner descriptor; never unwrap again |

## Selection and active rethrow

| ID | Fixture | Required outcome |
|---|---|---|
| EXSEL-01 | Exited inner finally changes state tested by candidate filter | Cleanup precedes type/input/filter tests |
| EXSEL-02 | Inner cleanup replaces pending failure before candidate type test | Test replacement; retain earlier cause responsibility |
| EXSEL-03 | First matching filter is false and next sibling matches | End first binding, retain effects and test next sibling in source order |
| EXSEL-04 | No candidate at one scope matches | Continue outward cleanup; do not unwind past untested candidate |
| EXSEL-05 | Candidate filter raises a checked failure | Propagate from recovery scope to lexical enclosing handlers, not false/sibling restart |
| EXSEL-06 | Handler body raises a fresh failure | Its own protected try does not catch that body failure |
| EXSEL-07 | Normal handler exits with an input borrow and value locals | End locals/borrow before discharging original event |
| EXSEL-08 | Reference catch executes bare throw | End catch-scope borrow, then propagate same owner/origin without Copy |
| EXSEL-09 | Nested handler executes bare throw | Rethrow innermost active event, not enclosing handler's event |
| EXSEL-10 | Bare throw is outside handler or inside separately invoked lambda/helper | Diagnose missing source handler context |
| EXSEL-11 | Rethrow reaches later candidates | Do not repeat previously completed cleanup or create a fresh origin |
| EXSEL-12 | Copies/borrows/catches are used as rollback, snapshot or suppression | Preserve real effects/aliases and every-exit responsibility |
| EXSEL-13 | Exception annotation is used to bypass unsafe recovery authorization | Diagnose missing existing unsafe recovery authorization |
| EXSEL-14 | Actual nested typed/filter propagation with source cleanup | Requires executable source/runtime evidence for all entered/exited scopes |
| EXSEL-15 | Handler cleanup faults while a rethrow or normal departure is pending | Preserve live input through authored cleanup, end it, then propagate replacement with earlier cause |

## Retention, requirement failure and foreign mapping

| ID | Fixture | Required outcome |
|---|---|---|
| EXCP-01 | Copy payload is retained after catch and later freshly thrown | Fresh event; not captured original provenance |
| EXCP-02 | Catch descriptor escapes after original event retirement | Diagnose invalid dependency |
| EXCP-03 | Annotated wrapper or method named Capture claims preserved event | Require actual registered captured-failure protocol |
| EXCP-04 | Unique captured event has admitted transfer contract | Consume original responsibility; payload Copy does not permit duplication |
| EXCP-05 | Repeatable captured propagation lacks checked sharing/thread facts | Reject unsupported repeat/crossing; marker is insufficient |
| EXCP-06 | Requirement selector names eligible E but no construction contract | Decline missing constructor/catalog; no default or false-success substitute |
| EXCP-07 | Predicate evaluation fails instead of returning false | Preserve actual predicate failure, not selected requirement failure |
| EXCP-08 | Foreign packet is mapped to annotated complete local E | Check mapping before local tests and retain affine domain/cause dependencies |
| EXCP-09 | Raw packet, unadmitted foreign domain or hardware/fail-fast condition is thrown | No automatic eligibility/catchability or cleanup guarantee |
| EXCP-10 | Actual captured payload/cause roots, allocator and image retirement | Requires runtime/protocol evidence beyond finite responsible holders |
| EXCP-11 | Independent replacement retains an earlier frame/guard/generation-dependent cause | Check the complete cause closure; replacement independence cannot hide invalid sources |
| EXCP-12 | Cause still has a live input borrow or unsettled protocol responsibility | Preflight retirement; do not partly release another cause and then abandon this one |
| EXCP-13 | A cause owner is reattached, duplicated or used to create an owning cycle | Reject invalid responsibility graph rather than duplicate release |

## Publication and independent admission

| ID | Fixture | Required outcome |
|---|---|---|
| EXAD-01 | Independent consumer retains direct Exception or category-witness route | Check exact schema, complete identities and declared route |
| EXAD-02 | Source/debug names replace required eligibility or witness records | Decline missing semantic support; optional names are insufficient |
| EXAD-03 | Replacement removes retained direct or inherited eligibility | Incompatible affected use |
| EXAD-04 | Replacement changes closed category arguments or actual representation | Reject changed relied-on subject/witness contract |
| EXAD-05 | Replacement changes borrowed input into Copy-required value input | Reject changed ownership/mode promise |
| EXAD-06 | Catch elimination relied on positive or negative witness facts | Retain those facts and recheck replacement |
| EXAD-07 | New eligibility/category is added without changing retained client facts | Apply ordinary compatibility; not universally breaking or automatically valid |
| EXAD-08 | A selected body is accessed by trusted reader | Keep selective access and required retained semantics; no preliminary all-Body scan |
| EXAD-09 | Missing payload/handler/cause/cleanup codec is treated as runtimePolicy support | Decline before affected source-free use |
| EXAD-10 | Exception attribute name is assigned a private opcode/Feature ID | No standard allocation or feature activation follows |
| EXAD-11 | Prior contained native bridge and ordinary direct calls are retained | Preserve original supported contracts; new local marker supplies no shared unwind ABI |
| EXAD-12 | Complete independent serialized handler/capture and source execution | Requires real producers/readers/runtime; logical unit allocates none of those codecs |
