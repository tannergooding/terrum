# Static initialization conformance cases

These cases apply
[static initialization and load-scoped storage](../grammar-spec.md#static-initialization-and-load-scoped-storage)
and its [logical metadata requirements](../representation/logical-records.md#static-initialization-and-load-scope-encoding-pending).
They specify accepted behavior, not implementation support or a completed
physical metadata/runtime ABI. Fixtures supply valid declarations, dependency
contracts and cleanup behavior unless the case tests those obligations.

## Declaration identity and ordering

| ID | Scenario | Required outcome |
|---|---|---|
| SIID-01 | A loaded closed nominal type declares ordinary static storage | One type-wide initialization group owns its non-lazy fields and authored static constructor |
| SIID-02 | Two closed generic constructions of one nominal declaration are loaded | Each construction has distinct static storage and initialization state |
| SIID-03 | Two independently loaded identities denote the same source type | They retain separate load-scoped storage and initialization state |
| SIID-04 | A type is composed from partial source inputs | Initializers run in compiler-received input order, then source order within each input; the primary part has no priority |
| SIID-05 | Property backing storage occurs among static declarations | Its initialization occupies the property's declaration position |
| SIID-06 | A computed static property has no backing storage | Do not synthesize a static field or initialization step |
| SIID-07 | A constant or foreign `extern` field is encountered | It acquires no Terrum static-initialization obligation under this contract |
| SIID-08 | A static constructor has parameters, a result, an ordinary visibility, `unsafe`, `extern`, `mut` receiver or `async` | Reject the invalid declaration |
| SIID-09 | A static constructor has no authored trigger attribute | Reject; do not infer a trigger from its body |
| SIID-10 | `StaticInitializer` has no argument, a defaulted argument, multiple arguments or an unrecognized trigger case | Reject against its exact single explicit closed-enum argument schema |
| SIID-11 | A same-spelled unregistered `StaticInitializer` attribute is present | It grants no initialization semantics |
| SIID-12 | A partial static constructor has multiple policy authors or implementations | Reject; one primary owns policy/contracts and one secondary implements it |
| SIID-13 | A secondary partial static constructor repeats primary-only policy or contracts | Reject the secondary's conflicting authorship |
| SIID-14 | A `LazyInitialization` attribute has arguments or is repeated | Reject against its non-repeatable, no-argument primary-owned schema |
| SIID-15 | `LazyInitialization` is applied to an ineligible or `extern` field | Reject; it does not create managed lazy backing for foreign storage |
| SIID-16 | A same-named attribute or trigger enum has matching spelling/underlying values but not the registered Core identity | Do not select static-initialization semantics; resolve exact attribute, enum and case identities |

## Triggering, access and eager completion

| ID | Scenario | Required outcome |
|---|---|---|
| SIEG-01 | A type name is resolved, a member is bound or metadata is inspected | Create no runtime initialization demand |
| SIEG-02 | A type without an authored static constructor receives first runtime access to an ordinary static field | Trigger its eager group before the access |
| SIEG-03 | An authored static constructor selects grouped-static-field access | A grouped field access demands group completion; unrelated static member access does not itself select the alternate trigger |
| SIEG-04 | An authored static constructor selects runtime-static-member access | Runtime static-member access demands its group under the selected policy |
| SIEG-05 | Eager fields and an authored static constructor are present | Run ordered field initializers before the constructor's preconditions, body and normal-return postconditions |
| SIEG-06 | An initializer reads a field not yet initialized | Reject or fail under the specified definite-initialization contract; do not expose a default |
| SIEG-07 | An initializer writes its vacant field with valid initialization authority | Permit initialization; this authority does not permit replacing a fixed live field |
| SIEG-08 | A static field is read or written through a `StaticPlace` | Form an ordinary typed place with the exact field/group/access obligation; apply ordinary access, initialization and assignment rules |
| SIEG-09 | A static method is called | Use ordinary `Call`; its selected method's `InitializationDemand` supplies required group completion |
| SIEG-10 | A helper is called from an initializer but has no initialization-phase authority | Do not infer group-scoped authority merely from the call path |
| SIEG-11 | A checked helper has explicit validated group-scoped phase authority | Permit only the exact field/group operations and initialization phases covered by that authority |
| SIEG-12 | A call or imported contract can demand an initialized group | Preserve and propagate its exact demand through same-module calls, imports and linking |
| SIEG-13 | A source or metadata name resembles a static field/group but lacks its typed identity | Do not bind the access by spelling or row order |
| SIEG-14 | A role-bearing initialization helper is called as an ordinary method without matching group authority | Reject the call; the helper is initialization-only |
| SIEG-15 | A helper's validated role names a different group from the active initialization | Reject; group authority does not transfer by ordinary call |
| SIEG-16 | An unsafe request recovers one failed group or lazy field | Use one registered runtime-owned call through ordinary `Call` with a closed tagged exact-unit reference distinguishing the two unit kinds; validate exact identity and recovery preconditions, with no dedicated Body operation |

## Eager and lazy state, failure and concurrency

| ID | Scenario | Required outcome |
|---|---|---|
| SISTA-01 | An eager group is first demanded | Transition from `Uninitialized` to one `Initializing(owner)` execution, then to `Complete` or `Failed` |
| SISTA-02 | Permitted independent callers demand the same initializing group | Wait for its outcome; do not duplicate initialization or observe unfinished fields |
| SISTA-03 | A group recursively demands itself or forms a cycle of unfinished groups | Fail with the exact `InitializationCycleException`; do not deadlock or publish partial state |
| SISTA-04 | The initializer fails after acquiring cleanup-bearing values | Clean acquired partial state under ordinary cleanup rules and publish no live group |
| SISTA-05 | Failure escapes the initializer | Its initiating flow receives the actual escaping event, including ordinary cleanup replacement and causes |
| SISTA-06 | A waiting or later demand observes a failed group | Construct a fresh `InitializationFailedException` event; do not retain/copy the initiating payload or add an implicit shared cause |
| SISTA-07 | A failed group is demanded again | Preserve failed state; do not retry automatically |
| SISTA-08 | The initializer catches an internal failure and satisfies all completion obligations | The group may complete normally; a caught failure does not force the group to fail |
| SISTA-09 | A lazy field is accessed before its eager group completes | Complete the eager group first, then initialize only the requested lazy field |
| SISTA-10 | Another lazy field is not accessed | Leave its independent state uninitialized |
| SISTA-11 | A lazy initializer fails after its eager group completed | Fail only that lazy unit; retain the completed eager group |
| SISTA-12 | A lazy initializer fails while its eager group is still being initialized | The escaping failure also fails the eager group |
| SISTA-13 | A lazy unit recursively demands itself or creates an unfinished-unit cycle | Fail with the cycle contract, not an indefinite wait or invented default |
| SISTA-14 | Eager or lazy completion becomes visible to another permitted caller | Publish with the memory-model guarantees required by the sharing contract; do not infer synchronization for later field mutation |
| SISTA-15 | An initializer catches a cycle exception and then satisfies all completion obligations | The cycle exception alone does not fail every participant; the unit may complete |
| SISTA-16 | A cycle exception escapes one unit's initializer after cleanup | Fail that unit; do not automatically mark other participants failed unless the exception escapes their own initializers |
| SISTA-17 | A caller catches a fresh `InitializationFailedException` from a later demand | Keep the unit failed; handling the event does not reset it, and subsequent demands still receive fresh failure events |
| SISTA-18 | An explicit unsafe retry has exclusive authority after completed cleanup and no invalidated dependencies | Reset the failed unit to `Uninitialized` for a new attempt; do not mark it `Complete` |
| SISTA-19 | An explicit unsafe retry is attempted before cleanup completes or after a dependency is invalidated | Reject the retry; retain the failed state |
| SISTA-20 | An explicit unsafe retry names one failed lazy field while its eager group and sibling lazy fields have independent states | Reset only the named failed unit; preserve every sibling and group state |

## Dependencies, cleanup and load lifetime

| ID | Scenario | Required outcome |
|---|---|---|
| SILIFE-01 | A static value is managed or has ordinary cleanup/ownership obligations | Permit it when its root, dependency, ownership and every-exit cleanup contracts are satisfied |
| SILIFE-02 | Group initialization fails after earlier fields became live | Retire acquired values in the required order; do not publish the group or roll back external side effects |
| SILIFE-03 | A static initializer accesses a field from another module | Bind the exact provider field/group and preserve trigger, access, effect and compatibility obligations |
| SILIFE-04 | Producer packs, links or remaps imported static references | Preserve exact provider identities and order; consumer verification rejects dangling or wrong-kind references before affected use |
| SILIFE-05 | A module is unloaded while static calls, loans, roots, pins or owned values remain active | Keep the load alive until required quiescence and cleanup complete |
| SILIFE-06 | A checked unload completes | Retire applicable static values while required cleanup code remains available, then invalidate storage/code |
| SILIFE-07 | The same module is loaded again after unload | Create fresh load-scoped initialization state; do not reuse stale completion/failure state |
| SILIFE-08 | An initializer is pure only in its immediate computation | Preserve initialization timing, effects, failures, ownership, publication and memory-model behavior; purity alone does not permit hoisting |
| SILIFE-09 | A source-free consumer lacks a required static-storage/access contract | Reject the affected requirement before use; do not infer it from `FieldDef`, names or optional data |
| SILIFE-10 | A producer proposes a bounded `StaticInitProfile` that omits accepted cases | It cannot establish a conforming semantic subset or support claim; reject affected use rather than treating the profile as a valid subset |

## Logical record and Body-operation obligations

| ID | Scenario | Required outcome |
|---|---|---|
| SIREP-01 | A static field is represented alongside instance fields | Retain a distinct `StaticField` identity; do not overload instance `FieldDef` offsets or flags |
| SIREP-02 | Eager and lazy declarations are persisted | Preserve `EagerGroup`, `EagerInitializer` and independent `LazyUnit` identities, closed-type association and required order |
| SIREP-03 | Generated eager/lazy initializer methods are present | Keep them private and unspeakable with required `InitializationRole`; do not expose ordinary source-callable/imported methods |
| SIREP-04 | A Body accesses a static field | Use `StaticPlace` with a typed access obligation; ordinary place operations remain unchanged |
| SIREP-05 | A static method is invoked in Body | Reuse `Call` and retain `InitializationDemand` on the selected method contract |
| SIREP-06 | A required record or Body form is unknown or unsupported | Reject before affected binding/execution; no fallback to flags, method names or row order |
| SIREP-07 | Exact remaining row schemas, clause/opcode assignments or runtime handles are unavailable | Keep those physical assignments open; approved candidate shapes do not claim byte-level interoperability |
| SIREP-08 | An imported static access carries an initialization dependency | Resolve the static-field `DefId` through shared Import/Export identity and retain one `StaticDependency` per exact imported field and complete owner substitution for its group and provider trigger/effect contract; physical fields and typed-reference encoding remain open |
| SIREP-09 | Multiple access sites use the same imported static field | Share its `StaticDependency`; preserve each use's access kind and demand/phase authority in `StaticAccess`, without duplicating or weakening the provider contract |
| SIREP-10 | A method carries initialization role or demand obligations | Store them as typed MethodDef `Contract` clauses; keep per-use access obligations on `StaticAccess` and do not add separate method-role tables |
| SIREP-11 | One method is role-bearing or demands eager completion for one or more groups | Require at most one exact-group `InitializationRole`; encode each distinct closed-group demand once and reject duplicate demands |
| SIREP-12 | A method is a generated eager/lazy initializer, authored static constructor or initialization-only helper | Use one 20-byte `InitializationRole` Contract clause with tags `1=eager initializer`, `2=lazy initializer`, `3=authored static constructor`, `4=eager-phase helper`, `5=lazy-phase helper`; keep clause IDs and typed-reference encoding unassigned |
| SIREP-13 | Unsafe recovery targets one failed initialization unit | Use a closed tagged exact-unit reference for either one eager group or one lazy field, bound to its exact loaded identity; reject an unknown tag or mismatched/stale target and do not encode a module-wide reset |
| SIREP-14 | A persisted `StaticField` declares its post-initialization mutability | Use the approved 20-byte candidate row; flags `0` means not writable and `1` means writable, while every other flag value violates the producer schema |
| SIREP-15 | A body accesses a local or imported static field | Use the approved `StaticAccess` target/authority direction; retain one `StaticDependency` per exact imported field/owner substitution shared across use sites, with provider/group identity resolved through Import/Export; physical row schemas remain open |
