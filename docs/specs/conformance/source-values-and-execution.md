# Value and ordinary-execution conformance cases

These cases apply [values and statement execution](../language/expressions-and-statements.md),
[structural flow](../language/initialization-and-flow.md), and the source
grammar. They are specification obligations, not executed compiler tests.
Assume otherwise valid declarations, complete types, and checked operations.
Command helpers return `void` unless a case states another type.

## Unit values and their state

| Case | Fragment or situation | Required result |
|---|---|---|
| UNIT-01 | `()` in a value position | The single live `void` value |
| UNIT-02 | `void token = ();` | Permit an initialized fixed unit binding |
| UNIT-03 | `var token = ();` | Infer `void` |
| UNIT-04 | A named callable accepts one `void` parameter and receives `()` | One logical argument; no payload bytes |
| UNIT-05 | `F()` versus `F(())` with corresponding overloads | Preserve different logical arity |
| UNIT-06 | `(void, int32) pair = ((), int32(1));` | Permit both typed elements; unit is not an omitted element |
| UNIT-07 | `init void token;` then `token is init` | False without reading a payload |
| UNIT-08 | Read/discard that vacant `token` as a value | Diagnose despite zero data |
| UNIT-09 | Initialize it with `()` | Publish live state |
| UNIT-10 | `token = _;` on a live unit local | End its logical lifetime and leave vacancy |
| UNIT-11 | Reassign an ordinary fixed unit binding after its lifetime ended | Diagnose missing initialization permission |
| UNIT-12 | Reinitialize proven-vacant `init void` | Permit without inventing another unit value |
| UNIT-13 | Conditionally initialize `init void`, then inspect its state | Ordinary maybe-live join and state refinement |
| UNIT-14 | `init mut void` is maybe-live before ordinary assignment of `()` | Publish live state under the same conditional-retirement rules |
| UNIT-15 | Compare two effectful unit-producing expressions | Evaluate both in order; equality true, inequality false |
| UNIT-16 | `default<void>` | `()` |
| UNIT-17 | A field of type `void` with a valid initializer | Permit; preserve initialization/effects without payload bytes |
| UNIT-18 | An unmarked generic formal receives `()` | Admit complete `void` in the default Copy-capable, non-null domain, subject to its other constraints |
| UNIT-19 | A valid `Option<void>` specialization | Distinguish absent from present unit; no second unit value |
| UNIT-20 | `TextSpan<void>` | Retain its explicit encoding-erasure contract; not unit elements |
| UNIT-21 | `void token = ReadInt();` | Diagnose missing `int32`-to-`void` conversion |
| UNIT-22 | `void token = _;` | Diagnose; `_` is not a value |
| UNIT-23 | `ref token` or a reference to a zero-data unit field | Diagnose no exposable unit payload place |
| UNIT-24 | A genuine unit parameter has default `()` | Preserve logical parameter/default binding despite zero data |
| UNIT-25 | An unrelated nominal type has zero payload size | Retain that identity and explicit result-use/discard requirements; no unit conversion by size |

## Categories and explicit result handling

| Case | Fragment or situation | Required result |
|---|---|---|
| EXPR-01 | `Notify();` with a `void` result | Permit as an expression statement |
| EXPR-02 | `();` or an initialized unit local as an expression statement | Permit a genuine unit expression; keep ordinary read checks |
| EXPR-03 | `TryRemove(key);` with a `bool` result | Diagnose implicit unused non-unit result |
| EXPR-04 | `CreateOwner();` or `new Resource();` returns a non-unit value | Diagnose; do not silently create/drop an ignored owner |
| EXPR-05 | `int32(1);` or `count + int32(1);` | Diagnose implicit discard |
| EXPR-06 | `_ = TryRemove(key);` | Evaluate once and explicitly ignore the Boolean |
| EXPR-07 | `_ = count + int32(1);` | Permit explicit discard of a non-command value expression |
| EXPR-08 | `_ = CreateOwner();` completes normally | Construct once, then perform the returned owner's ordinary cleanup at expression end |
| EXPR-09 | `_ = owner;` for a named unique owner | Diagnose implicit copy/transfer |
| EXPR-10 | `_ = move owner;` with no conflicting obligations | Explicitly transfer/drop once and leave its source vacant |
| EXPR-11 | `owner = _;` | Local lifetime end, not a unit/default assignment |
| EXPR-12 | Explicit discard would violate a retained dependency or active-operation responsibility | Diagnose; underscore grants no abandonment permission |
| EXPR-13 | Bare prefix/postfix increment returns a non-unit value | Require explicit discard or a statement-level update |
| EXPR-14 | `for (...; ...; _ = index++)` | Permit the explicit header discard with normal update/order |
| EXPR-15 | `index += int32(1);` | Statement-level update; no value implicitly discarded |
| EXPR-16 | `public void F() => ReadInt();` | Diagnose non-unit expression-body result |
| EXPR-17 | `public void F() => _ = ReadInt();` | Explicit discard body completes with unit |
| EXPR-18 | A `func<void>` target receives `() => ReadInt()` | Diagnose; no return-value discard adaptation |
| EXPR-19 | The target receives `() => { _ = ReadInt(); }` | Permit explicit discard and unit fallthrough |
| EXPR-20 | A non-unit `await` result is used as a bare statement | Diagnose; consume or explicitly discard it |
| EXPR-21 | A generic body ignores a result not known to be `void` | Diagnose independently of a particular unit specialization |
| EXPR-22 | `_ = TypeName;` or `_ = NamespaceName;` | Diagnose nonvalue category |
| EXPR-23 | `_ = Method;` without a checked callable target | Diagnose; a callable group is not a value |
| EXPR-24 | Assign to a computed nonplace value, or borrow a value-getter property result | Diagnose; a declared reference result instead permits explicit projection of its real place |
| EXPR-25 | `*destination` projects a vacant initialization-loan place | Permit place formation/state inspection; do not read `T` |
| EXPR-26 | An integer, descriptor, handle, or unit is used as a condition | Diagnose missing Boolean value; no truthiness |
| EXPR-27 | A nullable Boolean is used without its eligible presence/extraction | Diagnose; no implicit absent-as-false rule |
| EXPR-28 | A unit-valued conditional/switch expression stands alone | Permit only when every normal result path has unit type |
| EXPR-29 | One normal conditional/switch arm returns a non-unit value and another returns unit | No implicit erasure/discard to unify them |
| EXPR-30 | Assign through an eligible property/indexer setter | Stage receiver, indices, and right-hand side once in order; preserve the selected accessor's invocation checks |

## Control flow and completion

Trace markers name observable initialization `I`, condition `C`, body `B`,
step `S`, body cleanup `D`, and header cleanup `H`. Include cleanup markers
only when the fixture has the corresponding live obligation.
Every stated marker-producing operation completes normally unless noted;
the condition sequence fixes the stated true/false outcomes.

| Case | Situation | Required result |
|---|---|---|
| EXEC-01 | `while` has a first false condition | `C`; no body or body cleanup |
| EXEC-02 | `while` has two true conditions then false, with one body owner each time | `C B D C B D C` |
| EXEC-03 | `do` has a first false condition | `B D C`; body runs first |
| EXEC-04 | `do` body executes `continue` | Complete body cleanup, then condition |
| EXEC-05 | `for` initializes a header owner, then its first condition is false | `I C H`; no body or step |
| EXEC-06 | `for` has one true condition then false, with body/header owners | `I C B D S C H` |
| EXEC-07 | That `for` body executes `continue` | Body cleanup, step, condition; no repeated initializer |
| EXEC-08 | That `for` body executes `break` | `I C B D H`; no step or repeated condition |
| EXEC-09 | Ordinary `for` omits its condition | Treat it as true without a hidden condition evaluation |
| EXEC-10 | Initializer or step is omitted | No operation/effect at that slot |
| EXEC-11 | `continue outer` leaves an inner `for` and its enclosing outer body | Inner body/header cleanup, outer body cleanup, then outer step/condition; no inner step |
| EXEC-12 | `break outer` leaves those scopes | Clean exited body/header scopes; no subsequent step/condition |
| EXEC-13 | A body local is initialized on each reentry | New activation; no retained prior body local |
| EXEC-14 | Iteration enters its sequence/iterator header | Evaluate sequence once, acquire once, advance per attempt |
| EXEC-15 | A continuing iteration body holds an item loan | End applicable body/item obligations before advancing |
| EXEC-16 | Iterator acquisition fails | Clean acquired partial state, without closing an unacquired iterator |
| EXEC-17 | Advancement fails after acquisition | Required close and ordinary cleanup/failure chaining; no silent normal completion |
| EXEC-18 | Async iteration exits through asynchronous close | Await required close before completing exit |
| EXEC-19 | A `void` callable normally falls through | Complete with unit after ordinary body cleanup and contract phases |
| EXEC-20 | A `void` callable executes `return;` or `return ();` | Same unit result, with reached evaluation/effects preserved |
| EXEC-21 | A `void` callable returns a non-unit expression | Diagnose, even for Copy scalars |
| EXEC-22 | A non-unit callable has one normal fallthrough path | Diagnose missing result; no default |
| EXEC-23 | A non-unit callable uses `return;` | Diagnose |
| EXEC-24 | Every path returns a complete eligible result or throws | Permit result completion |
| EXEC-25 | A structurally proven infinite loop has no normal exit | No fabricated return value; failure/cleanup obligations still apply |
| EXEC-26 | A return expression is effectful and `finally` changes state | Evaluate the result once, not again after cleanup; diagnose invalidation of its required backing before publication |
| EXEC-27 | Cleanup replaces a pending return with failure | No normal postcondition/publication; clean still-owned unpublished result once |
| EXEC-28 | A non-unit expression-bodied callable returns a matching value | Consume it as the result, not an implicit expression-statement discard |
| EXEC-29 | A deferred expression uses a local changed after registration | Evaluate operands at execution/exit, not an entry snapshot |
| EXEC-30 | Execution skips a `defer` statement | No action registered |
| EXEC-31 | Several defer actions are reached in one activation | Reverse actual registration order |
| EXEC-32 | A deferred action would read an ended/moved local on an exit path | Diagnose unavailable read/dependency |
| EXEC-33 | A cleanup action returns or transfers outside itself | Diagnose |
| EXEC-34 | A normally completing non-unit deferred computation is written as an expression action | Diagnose; use a block with explicit discard |
| EXEC-35 | Statement switch selects an arm whose block completes normally | Exit the switch without fallthrough |
| EXEC-36 | An explicit cross-arm switch transfer selects a valid constant/wildcard target | Clean exited scopes and enter the target without rematching |
| EXEC-37 | Ordinary Boolean `require predicate;` | Consume/check Boolean result; false uses requirement failure, not an ignored result |
| EXEC-38 | Value-producing `require` yields a non-unit output | Use/bind/transfer or explicitly discard it; statement checking cannot silently drop it |
| EXEC-39 | An empty statement | No evaluation, declaration, or extra value |
| EXEC-40 | `unsafe` or a selected policy wraps a statement | Preserve result consumption, initialization, and cleanup rules |
| EXEC-41 | A backward switch-arm `continue` creates a cycle inside a `finally` body | Accept as ordinary CFG; do not require a termination proof. If the cycle never exits, the `finally` body does not complete and later exits/cleanup do not occur |

Tuple/collection construction, imported callability, async/producer phase
identities, and complete primitive operator catalogs retain their canonical
contracts. These cases do not infer additional APIs or implementation support.
