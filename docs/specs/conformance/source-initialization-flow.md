# Initialization and pattern-flow conformance cases

These cases apply [initialization and structural flow](../language/initialization-and-flow.md).
Assume otherwise valid visible declarations and complete types. `T` in value
patterns has the required Copy contract; owning outputs use valid declared
initialization, dependency, and synchronous cleanup contracts.
These are specified outcomes, not executed compiler tests.

## Scope, vacancy, and joins

| Case | Situation | Required result |
|---|---|---|
| FLOW-01 | A failed `value is T item` is followed by a use of its name | Name remains in the supporting block; value access still needs initialization |
| FLOW-02 | A one-time test may match; then unconditionally read `item` | Diagnose maybe-vacant read |
| FLOW-03 | That test is followed by unconditional assignment to fixed `item` | Diagnose possible live replacement |
| FLOW-04 | `if (value is T item) { } else { item = Initialize(); } Use(item);` | Permit when both paths publish complete live values |
| FLOW-05 | Test `item is init`; initialize it only on the false edge; then read it | Permit |
| FLOW-06 | `if (value is not T item) { item = Initialize(); } Use(item);` | Permit for an initially vacant one-time binding with valid initialization |
| FLOW-07 | Ordinary `!(value is T item)` | Swap true/false facts; do not undo its successful binding assignment |
| FLOW-08 | Pattern name used before its source declarator | Diagnose, even if initialized on a later path |
| FLOW-09 | Pattern name redeclares an enclosing local/parameter/formal | Diagnose |
| FLOW-10 | False match followed by `item is init` on a never-initialized binding | False without reading `T` |
| FLOW-11 | Both branches initialize a fixed vacant local | Live at the join |
| FLOW-12 | One branch initializes it and the other leaves it vacant | Maybe-live at the join |
| FLOW-13 | A branch leaves it vacant but has no normal completion | That edge does not weaken the surviving normal live fact |
| FLOW-14 | One branch moves or early-ends a previously live local | Maybe-live at the normal join unless all surviving paths end it |
| FLOW-15 | Read a local after its explicit lifetime end | Diagnose despite retained name/type |
| FLOW-16 | Scope exit from maybe-live owning storage | Cleanup only live incoming paths, once, with ordinary ordering |

## Mutable and repeated bindings

| Case | Situation | Required result |
|---|---|---|
| FLOW-17 | `value is mut T item` or `value is mut var item` | Declare initialization-capable mutable binding, not write authority over the subject |
| FLOW-18 | `mut` appears on a type-test pattern without a named value binding | Diagnose |
| FLOW-19 | `while (Next() is T item) { }` can rematch with the binding still live | Diagnose fixed-binding replacement |
| FLOW-20 | Every continuing path of that loop explicitly ends `item` | Permit rematching only when vacancy and dependency checks hold |
| FLOW-21 | `while (Next() is mut T item) { }` has multiple successful matches | Reuse one binding; ordinary replacement/cleanup |
| FLOW-22 | Mutable loop has successful matches then a failed terminal match | Retain the last successful value for a post-loop `is init` test |
| FLOW-23 | Mutable loop's first match fails | Binding remains vacant; no `T` cleanup |
| FLOW-24 | A short-circuit operand skips a later pattern | Its existing binding state is unchanged |
| FLOW-25 | New subject evaluation fails before replacement | Prior binding remains live until ordinary lifetime end/unwind |
| FLOW-26 | Successful replacement would invalidate a retained backing dependency | Diagnose; no hidden retained owner collection |
| FLOW-27 | Value pattern inside a reentered body block | New supporting-scope activation, not accumulated same-activation declarations |
| FLOW-28 | `mut ref<var> item` matches a readonly reference | Mutable descriptor binding; no immediate referent-write upgrade |
| FLOW-29 | `mut write ref<var> item` matches a readonly reference | Nonmatch/diagnostic under existing authority rules, never an upgrade |
| FLOW-30 | `mut` applied to a named type capture | Diagnose; no mutable type identity |
| FLOW-31 | Rebinding would change the binding's established complete type | Diagnose; inferred `var` does not retarget its type |

## Composition, saved outcomes, and invalidation

| Case | Situation | Required result |
|---|---|---|
| FLOW-32 | `value is T item && Use(item)` | Right operand can use the positive match's live binding |
| FLOW-33 | `value is T item || Use(item)` on its initially vacant failure edge | Diagnose the right operand's vacant read |
| FLOW-34 | A positive match binds `item`, then a later Boolean operand/guard is false | Keep its completed binding assignment; no rollback |
| FLOW-35 | `or` alternatives repeat a binding name with equal contracts | Diagnose duplicate declaration; no merged binding |
| FLOW-36 | Repeated names have different types or both use `mut` | Still diagnose duplicate declaration |
| FLOW-37 | `value is (int32 item and 0)` for a nonzero `int32` | False, with the completed `item` initialization retained |
| FLOW-38 | `value is ((int32 item and 0) or 1)` for `int32(1)` | True; retain the first part's binding without declaring it again |
| FLOW-39 | Different `or` alternatives declare distinct names or different binding sets | Permit declarations; availability is the normal path join, not a promise that every binding is live |
| FLOW-40 | Save a Boolean conditional-output call result and branch on it | Refine that particular output's state under its declared contract |
| FLOW-41 | Copy that unchanged discriminator to another ordinary Boolean local | Preserve the supported correlation with unchanged outputs |
| FLOW-42 | Reassign the saved discriminator before testing it | Invalidate its old correlation |
| FLOW-43 | Move, replace, or early-end a correlated output | Invalidate the affected relation and retain actual cleanup state |
| FLOW-44 | An aliasing call may change a referenced value used by a predicate | Invalidate that predicate unless its published footprint proves stability |
| FLOW-45 | A readonly descriptor aliases writable Copy storage | Readonly does not establish snapshot/stable-value facts |
| FLOW-46 | A richer status has a declared initialized failure output | Its failure arm remains live; no universal true/success gate |
| FLOW-47 | A loop backedge invalidates a fact needed by its next iteration | Join conservatively; no implicit once-only or induction assumption |
| FLOW-48 | A pattern captured type name is visible but lacks positive type evidence | Diagnose type use; visible name does not create a witness |
| FLOW-49 | A negative guard exits all nonmatching paths before later captured-type use | Preserve the positive evidence on the surviving normal path |
| FLOW-50 | A declaration contract's pattern binding is used by the implementation body | No implementation-local binding exported by that phase |
| FLOW-51 | An earlier precondition initializes a pattern binding used by a later precondition | Permit in their shared precondition scope, subject to availability |
| FLOW-52 | A postcondition tries to use a precondition-local binding | Diagnose out-of-scope name |
| FLOW-53 | Pre/postcondition materializations own values | Ordinary cleanup before the respective phase completes; no implicit body-scope retention |
| FLOW-54 | A later postcondition or phase cleanup invalidates an earlier published guarantee | Require reestablishment or proof of preservation; otherwise diagnose, without predicate replay |
| FLOW-55 | A precondition fact is invalidated by precondition-scope cleanup | Do not pass that stale fact into body flow |
| FLOW-56 | Unconditionally assign maybe-live `init mut T` storage | Construct once; retire the old value only if live; publish live state under ordinary dependency checks |
| FLOW-57 | Copy a call's Boolean discriminator, then reassign the original while leaving the copy/output unchanged | Preserve the independent copy's relation; invalidate the reassigned binding's old relation |
| FLOW-58 | A fixed loop pattern's successful body always exits before another attempt | Permit without `mut` when no live reinitialization path exists |
| FLOW-59 | Save `bool matched = value is T item;`, then read `item` only when unchanged `matched` is true | Retain the positive initialization correlation with the unchanged binding |

## Initialization-loan projections

Assume `init ref<int32> destination` and a vacant caller destination.

| Case | Fragment or situation | Required result |
|---|---|---|
| INIT-01 | `*destination is init` on entry | False; inspect destination state without an `int32` read |
| INIT-02 | `destination is init` | Tests the live descriptor binding, not the destination's value state |
| INIT-03 | `*destination = int32(1);` | Initialize the destination under its loan mode |
| INIT-04 | `destination = int32(1);` | Diagnose descriptor assignment; no transparent referent update |
| INIT-05 | `destination = anotherReference;` | Diagnose forbidden initialization-descriptor rebinding |
| INIT-06 | `*destination > int32(0)` before initialization | Diagnose vacant read |
| INIT-07 | `*destination > int32(0)` after complete initialization | Read the live referent under the loan's permitted contract |
| INIT-08 | `Helper(init *destination)` before initialization | Forward the same vacant destination and its cleanup/publication obligations |
| INIT-09 | `Helper(init destination)` | Does not project `int32`; cannot reinterpret descriptor storage as its referent |
| INIT-10 | Convert/capture/pass the loan opaquely as an ordinary reference before publication | Diagnose |
| INIT-11 | Replace `*destination` after it is live using only initialization permission | Diagnose; initialization mode is not ordinary write authority |
| INIT-12 | `init ref<ref<U>>` with `*destination` | Project the descriptor destination `ref<U>`, not its `U` referent |
