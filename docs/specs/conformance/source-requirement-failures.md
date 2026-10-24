# Runtime requirement failure conformance

These cases apply to [requirement failures](../language/requirement-failures.md),
ordinary calls/types/ownership and [local failures](../language/local-failures.md).
Fixtures supply the stated declarations, ordinary zero-supplied constructor
bindings and runtime facts.
They do not claim Terrum implementation or physical-codec conformance.

## Source forms and selectors

| ID | Fixture | Required outcome |
|---|---|---|
| RFSY-01 | Statement `require<E>` has a Boolean predicate | Check once; false selects E; true completes normally |
| RFSY-02 | Value `require<E>` has no omitted outputs | Check Boolean, yield void/() on true, not the Boolean |
| RFSY-03 | One ordinary init output is omitted in value `require<E>` | Retain normal lifting; E is not an output or operand type |
| RFSY-04 | Several eligible outputs are omitted | Check/return their complete declaration-order projection |
| RFSY-05 | Explicit `require<E>` tests a pattern selecting some outputs | Ordinary pattern Boolean; underscore does not demand/read an ignored output |
| RFSY-06 | Declaration precondition writes `require<E>` predicate | Use that clause's selector without changing phase/order |
| RFSY-07 | Lambda declaration precondition writes `require<E>` predicate | Explicit selector is available without adding lambda attributes |
| RFSY-08 | Selector has no type, multiple types, a value or inference hole | Diagnose invalid selector; no factory/type-pack fallback |
| RFSY-09 | `require <E>` separates keyword and opener | Diagnose; selector requires attachment |
| RFSY-10 | Attached selector has interior trivia and nested generic closers | Ordinary valid complete Type parsing |
| RFSY-11 | E is chosen to steer operand overload/output inference | Do not use failure type as that inference evidence |
| RFSY-12 | static require/static ensure receives a failure selector | Diagnose; static proof has no runtime payload selector |
| RFSY-13 | `ensure<E>` is written as a runtime postcondition selector | Diagnose unsupported form |
| RFSY-14 | Existing `require policy<Case>` is written | Retain policy selection, not exception creation |
| RFSY-15 | Actual complete source parsing, typing and diagnostics | Requires compiler evidence beyond a finite form map |

## Default schema and source ownership

| ID | Fixture | Required outcome |
|---|---|---|
| RFDF-01 | No explicit selector or callable annotation exists | Select exact Core RequirementException, not a same-named user type |
| RFDF-02 | Primary named callable has resolved RequireException(typeof(E)) | Use its own complete default recipe |
| RFDF-03 | Accessor annotation and exact get-targeted placement | Same effective getter owner; no blanket property default |
| RFDF-04 | Eligible constructor/static constructor/operator/conversion/Cleanup primary has annotation | Its authored checks use that callable default; ordinary role/effect restrictions remain |
| RFDF-05 | Primary interface requirement has a default and clause | Preserve its own nominal member/recipe through dispatch |
| RFDF-06 | Same-spelled attribute resolves to another identity | Ordinary attribute, not default override |
| RFDF-07 | Type/module/assembly/caller tries to provide inherited default | Reject inapplicable placement or ignore non-owning context; no ambient default |
| RFDF-08 | Field/parameter/result placement attempts default override | Diagnose inapplicable target |
| RFDF-09 | RequireException appears on secondary callable part | Diagnose primary-contract violation |
| RFDF-10 | Repeated inline/get-targeted placements select one accessor | Diagnose repeated effective annotation |
| RFDF-11 | One direct typeof(E) argument is positional or ExceptionType-labelled | Same valid recipe; construction is not invoked while binding the attribute |
| RFDF-12 | Missing/extra/duplicate arguments or unknown labels | Diagnose registered schema |
| RFDF-13 | String, computed reflection object or user @typeof replaces direct query | Diagnose wrong argument category |
| RFDF-14 | Recipe uses transparent alias or complete generic family | Normalize exact type and ordinary forward substitution |
| RFDF-15 | Prefix recipe mentions owning callable/enclosing formals also spelled by caller | Bind exact owner/ordinal, not caller spelling/imports |
| RFDF-16 | Recipe has open generic, unresolved hole, wrong formal kind or alias cycle | Diagnose ordinary formation/normalization failure |
| RFDF-17 | Attribute lookup is treated as constructor/default execution | No exception construction, runtime reflection or static-init event follows binding |
| RFDF-18 | Nested lambda/contextual callable or separately invoked helper has no own override | Use its own Core default; no enclosing/caller/target inheritance |
| RFDF-19 | Explicit E coexists with valid callable default | Explicit E wins; malformed annotation still diagnoses |
| RFDF-20 | Actual Core type/attribute bootstrap and independent registration | Requires real compiler/library evidence, not supplied tokens |
| RFDF-21 | RequireException is used as a type capability/conditional schema, or its recipe lacks exact exception eligibility | Diagnose; callable default is not a type fact or construction permission |

## Ordinary zero-supplied-argument construction

| ID | Fixture | Required outcome |
|---|---|---|
| RFCN-01 | Core fallback has an applicable zero-supplied constructor binding | Construct its complete exception object as the event |
| RFCN-02 | Selected E's ordinary constructor binding completes | Transfer its unique result once; no `Copy` requirement |
| RFCN-03 | A constructor default supplies the type-owned message | Use the bound default; do not inject predicate text or caller context |
| RFCN-04 | Parameterless, optional-default and empty-`params` candidates are present | Apply ordinary constructor overload resolution and retain the exact binding |
| RFCN-05 | The selected type has no accessible, applicable or unambiguous zero-supplied binding | Reject; do not guess or choose an alternative by cost or name |
| RFCN-06 | A candidate requires an unbound message, allocator or other hidden input | Reject the incomplete binding; do not invent arguments |
| RFCN-07 | Selected E is an existential interface view, raw descriptor or optional wrapper | Diagnose absent concrete exception class; no boxing or dereference |
| RFCN-08 | Selected E is an enum/union rather than an eligible exception class | Reject; do not select zero/default or a named case |
| RFCN-09 | Generic T has exception-interface evidence but no published ordinary constructor evidence | Reject; do not infer a constructor from the interface or implementation body |
| RFCN-10 | Generic T has complete published evidence for an ordinary zero-supplied binding | Resolve that binding under its declared constraints |
| RFCN-11 | Alias or qualification changes selected E | Preserve exact normalized class identity; equal layout does not substitute |
| RFCN-12 | Selected E's constructor raises another failure | Propagate the actual failure; fabricate no completed E event |
| RFCN-13 | Constructor initialization/precondition/body fails before E publication | Reverse actual partial acquisitions under ordinary cleanup |
| RFCN-14 | Constructor postcondition/final validation fails | No published E; completed cleanup is not replayed |
| RFCN-15 | Constructor's own require checks select a default | Use the constructor implementation's semantic owner, not the outer requirement's default |
| RFCN-16 | Selected constructor is caller-unsafe or exceeds effect bounds | Require ordinary authorization/bounds; the check grants none |
| RFCN-17 | Constructor has no normal continuation | Preserve actual nonreturning behavior; invent no E or substitute event |
| RFCN-18 | Delivery fails after fresh E ownership transfer | Retain actual responsible state; no source revival or normal result |
| RFCN-19 | Complete E carries invalid backing/guard/source-generation/code dependency | Reject invalid use; construction does not waive lifetime |
| RFCN-20 | Actual constructor storage, partial state, roots and every-exit transfer | Requires compiler/runtime evidence beyond finite states |
| RFCN-21 | Constructor claims publication of a vacant, partial or different result | Reject before forming a requirement event; only complete selected E can transfer |
| RFCN-22 | Constructor cleanup replaces an earlier creation failure | Propagate the actual replacement with its original parent/dependencies; fabricate no outer E event |

## Once-only evaluation and effects

| ID | Fixture | Required outcome |
|---|---|---|
| RFEV-01 | Operand call and argument evaluation have effects | Existing once-only/source-order evaluation |
| RFEV-02 | Runtime check succeeds | No exception construction or failure event |
| RFEV-03 | Runtime check fails normally | Execute the selected ordinary constructor binding once |
| RFEV-04 | Operand evaluation raises a failure before a result | Propagate that event; never construct E |
| RFEV-05 | Selected constructor fails while the Core fallback is available | Propagate actual failure; do not retry Core |
| RFEV-06 | Operand/constructor writes or consumes an owner before later failure | Preserve writes and vacancy; no rollback |
| RFEV-07 | Constructor mutates backing used by a saved predicate/status fact | Apply ordinary effect/version invalidation |
| RFEV-08 | Construction wants expression text, parameter name, caller or context | No hidden argument injection or predicate reevaluation |
| RFEV-09 | Ordinary operand returns a non-Boolean status | Require explicit predicate; no enum-zero/success-name conversion |
| RFEV-10 | Marker/default implies Text, diagnostic fields or a source snapshot | No such automatic contract; ordinary constructor effects still apply |
| RFEV-11 | Selected pure form has false path with constructor effects/failure | Reject absent valid-domain proof/bounds |
| RFEV-12 | Construction failure is moved into failed completion transport | Preserve complete effect extent; transport does not remove failure |
| RFEV-13 | Known-true requirement is optimized away | Preserve operand effects and relied-on default/constructor/rule dependencies |
| RFEV-14 | E constructor completes and requirement raises its object | The object is the fresh check-site event; no separate event/payload wrapper |

## Outputs and supporting storage

| ID | Fixture | Required outcome |
|---|---|---|
| RFOU-01 | False predicate follows call initializing an explicit output | Preserve live output until its ordinary scope retirement |
| RFOU-02 | False predicate follows call leaving explicit output vacant | Keep vacancy; never manufacture default/null |
| RFOU-03 | Lifted nullable output is initialized null | Successful availability check; no failure constructor or implicit unwrap |
| RFOU-04 | Lifted bool payload is initialized false | Return false payload; do not treat it as failed status |
| RFOU-05 | Rich status has declared per-output availability | Follow exact availability, not status zero or universal success group |
| RFOU-06 | One of several required lifted outputs is unavailable | Fail without reading/moving it or speculatively consuming available owners |
| RFOU-07 | Explicit pattern ignores an unavailable output | No read/init demand for ignored slot; preserve live ignored obligations |
| RFOU-08 | All required owning outputs are available | Transfer only after all required checks |
| RFOU-09 | Reusable hidden owner remains depended on after failed check | Ordinary supporting scope remains; no full-expression retirement |
| RFOU-10 | Constructor raises after prior output publication | Preserve actual destination states while unwinding applicable scopes |
| RFOU-11 | Output-producing call already performed partial cleanup | Requirement/constructor never replay that cleanup |
| RFOU-12 | Call throws before publishing its normal discriminator | Preserve actual exceptional output state, not a guessed false state |
| RFOU-13 | Failed check exits a scope with live owned state | Exactly-once valid ordinary retirement/transfer, not catch-local rollback |
| RFOU-14 | Statement/value Boolean check succeeds with no omitted outputs | Normal unit/void behavior; no lifted status or default E |
| RFOU-15 | Actual typed output projection, borrows and supporting-scope lowering | Requires source/compiler/runtime evidence |

## Phases and logical code owners

| ID | Fixture | Required outcome |
|---|---|---|
| RFPH-01 | Named callable body has its own override | Bind to that primary source owner |
| RFPH-02 | It calls helper with a different/default override | Helper owns its checks; caller default does not flow through call |
| RFPH-03 | Declaration precondition fails after argument ownership transfer | Callee/phase cleanup; no unentered body finally and no source restoration |
| RFPH-04 | Declaration field initializer contains an unqualified require | Core default absent authored callable override, not whichever constructor executes it |
| RFPH-05 | Caller object-initializer expression contains a check | Keep caller expression's owner, not constructed type/constructor |
| RFPH-06 | Async argument/establishment-side check fails before operation exists | Synchronous failure with actual responsible cleanup |
| RFPH-07 | Established async precondition/body fails before any await | Failed completion, including construction failure |
| RFPH-08 | Async resumes on an admitted flow and then checks | Same semantic default and established failure channel |
| RFPH-09 | Producer creation precondition fails | Producer-owned default; synchronous creation failure |
| RFPH-10 | Producer body moved into cursor advancement contains a check | Producer's default, not generated cursor member's default |
| RFPH-11 | Authored shell-member clause has its own override | That clause's member owner, not producer body override |
| RFPH-12 | Interface clause/default body and implementing body differ | Preserve each originating member/default without transplantation |
| RFPH-13 | Lambda has contextual target or enclosing default | Its own Core default or explicit selector; no new lambda annotation |
| RFPH-14 | Actual nested preconditions, async and generated advancement execute | Requires real source/runtime phase evidence |

## Publication and independent admission

| ID | Fixture | Required outcome |
|---|---|---|
| RFAD-01 | Independent consumer retains exact default/Core identities and type recipe | Check exact semantic schema/owner/substitution |
| RFAD-02 | Constructor record is replaced by a name or private body inference | Decline missing complete ordinary binding |
| RFAD-03 | Zero-input binding used optional defaults/params/initialization facts | Retain every actually required call/cleanup dependency |
| RFAD-04 | Replacement changes relied-on selected E/default/constructor | Reject affected contract unless ordinary retained compatibility establishes it |
| RFAD-05 | Equal ABI changes effect, safety, state or failure-channel promise | Reject unsatisfied retained expectation |
| RFAD-06 | Equal bits alias a distinct nominal/representation failure | Preserve complete type identity, not carrier equality |
| RFAD-07 | Code is relocated/generated without original check owner/phase | Reject changed retained default/access/phase contract |
| RFAD-08 | Core fallback identity or selected constructor support is unavailable | Decline before affected use; no substitute E |
| RFAD-09 | Attribute string, debug name or runtimePolicy claims default/constructor support | Decline missing semantic/operational records |
| RFAD-10 | Local rule is treated as a wire/opcode/Feature allocation | Exact registry stays unchanged; no allocation follows |
| RFAD-11 | Trusted consumer uses retained requirements selectively | No preliminary all-Body scan; complete required before-use gates remain |
| RFAD-12 | Unrelated new default/type/constructor is added | Ordinary retained compatibility; neither universally breaking nor universally valid |
| RFAD-13 | Requirement defaults coexist with selected local-failure/source/native contracts | Retain each contract's identity, phase, ownership and boundary obligations; default selection supplies no shared unwind ABI or physical schema |
| RFAD-14 | Complete independent serialized constructor/default/handler execution | Requires actual producers/readers/runtime; logical unit assigns no codec |
