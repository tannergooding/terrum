# Implicit-reference materialization conformance cases

These cases apply the [source-equivalent shorthand contract](../overview/conformance.md#source-equivalent-shorthand),
[vacant-local grammar](../grammar-spec.md#33-statements-and-local-declarations),
and [reusable-local lifetime expansion](../lifetimes.md#repeated-evaluations-and-reusable-locals).
They are specification cases, not a claim of current compiler support.

Fragments assume visible declarations, complete types admitted by the selected
profile, and otherwise valid enclosing bodies. `Borrow` takes an
`implicit ref<T>` and returns a readonly reference under its declared input
dependency. `Remember` returns `bool`. Unless a retention case says otherwise,
neither call retains a loan. A retention case additionally requires an
explicitly permitted destination and valid supporting lifetime; materialization
does not grant those permissions.

## Vacant bindings

| Case | Fragment | Required result |
|---|---|---|
| VLOC-01 | `init int32 value;` | Parse a vacant, non-replaceable binding; do not construct `int32(0)` |
| VLOC-02 | `init mut int32 value; value = int32(1); value = int32(2);` | Initialize, then ordinarily replace the same binding |
| VLOC-03 | `init mut var value; value = int32(1); value = int32(2);` | Infer `int32` once; later replacement preserves that type |
| VLOC-04 | `init int32 value; value = int32(1); value = int32(2);` | Diagnose replacement of a live non-mutable binding |
| VLOC-05 | `init mut int32 value; Inspect(ref value);` | Diagnose borrowing vacant storage as an ordinary initialized reference |
| VLOC-06 | `init mut int32 value; value = int32(1); value = _; value = int32(2);` | End the first value once, then initialize a new value in the same binding |
| VLOC-07 | `init mut var value; value = int32(1); value = float32(2);` | Diagnose the changed complete type; mutability does not repeat inference |
| VLOC-08 | `init mut int32 value; if (value is init) { Inspect(ref value); }` | Initialization-state testing does not read a vacant `int32`; no inspection executes |

`init mut var?` follows the same rules with the established normalized nullable
type. A live null is initialized, not vacant. Nullable inference does not
supply a default, upgrade access, or remove backing dependencies.

## Sites, scopes, and dependencies

| Case | Source situation | Required result |
|---|---|---|
| MAT-01 | Borrow an eligible existing owner place | No materialized owner, Copy requirement, or ownership transfer merely to borrow |
| MAT-02 | Borrowing an existing place violates its ordinary contract | Diagnose; do not copy into a temporary to repair it |
| MAT-03 | Repeatedly reach `Borrow(CreateOwner())` in a `while` condition | Reuse one hidden binding in the existing enclosing block |
| MAT-04 | The condition's short-circuit operand skips that argument on every test | The binding remains vacant; no initializer or `T` cleanup |
| MAT-05 | A later test skips the argument after an earlier test initialized it | Leave the prior value unchanged until replacement or supporting-scope exit |
| MAT-06 | The last reached `Remember` returns false | Its initializer still executes; the resulting value remains live after the `while` |
| MAT-07 | The first initializer has a checked failure | No complete value is published; perform only applicable partial cleanup and unwind |
| MAT-08 | A later initializer fails during ordinary replacement | Preserve the old value until ordinary unwind or lifetime end; do not drop it before the failed construction |
| MAT-09 | A later argument fails after materialization | Do not invoke the outer callee; apply ordinary cleanup to the already assigned local |
| MAT-10 | Two distinct argument sites materialize the same `T` in one scope | Two logical bindings, not one shared mutable temporary |
| MAT-11 | One argument is `flag ? CreateLeft() : CreateRight()` | One materialization site; evaluate and assign only the selected result |
| MAT-12 | A site lies in a braced loop body entered repeatedly | One binding per body-scope activation, with per-execution cleanup |
| MAT-13 | The original `while` body executes `continue` | End that body's locals, then evaluate the next initializer and condition once |
| MAT-14 | The original `while` body executes `break` | No next initializer or condition; retain the current header temporary through its supporting block |
| MAT-15 | The first `do` body executes `break` before its condition | No condition materialization; its hidden binding remains vacant |
| MAT-16 | A `do` body executes `continue` | Evaluate its condition, including reached materialization, before the next body execution |
| MAT-17 | A `for` body executes `continue` | Evaluate the iterator and then the condition in their original order; reuse each site's separate header-scope binding |
| MAT-18 | A `for` body executes `break` | Skip iterator and next condition; retire live header values on leaving the loop |
| MAT-19 | The sequence expression of `await for` materializes an owner | Evaluate once; retain supporting values through required asynchronous iterator close |
| MAT-20 | A valid retained readonly reference names a mutable Copy temporary's slot | It can observe later assignments to that same place; previous values are not snapshots |
| MAT-21 | Replacement would invalidate a retained old-owner or backing dependency | Diagnose exactly as for explicit source assignment; no hidden collection of prior owners |
| MAT-22 | A retained result forwards independent backing and does not depend on the replaced slot or value | Preserve its published backing obligations; replacement alone does not create a new dependency |
| MAT-23 | A returned loan would outlive its materialized local's supporting scope | Diagnose; no implicit lifetime extension |
| MAT-24 | The complete materialized `T` is itself a reference descriptor | Preserve nested type, authority, and backing; do not implicitly dereference |
| MAT-25 | A later evaluation skips a side-effecting initializer or value-returning getter | Do not invoke it, retire the old value, or replay a previously computed result |
| MAT-26 | A loop has an iteration count unknown at compilation | No new scope-lived binding per iteration; storage for the fixed source sites and ordinary assignment staging suffices |

Ordinary validity is assessed against the declaration's public relationship,
not an inferred narrower behavior of its current implementation. Missing
rooting, cleanup, generic, or loop-protocol support is an unsupported
requirement, not permission to change these outcomes.

## Single-while expansion

For this source:

```terrum
while (Remember(Borrow(CreateOwner())))
{
    Body();
}
AfterLoop();
```

the equivalent code in its existing enclosing block is:

```terrum
{
    init mut T tmp;
    while (true)
    {
        tmp = CreateOwner();
        if (!Remember(Borrow(ref tmp)))
        {
            break;
        }
        {
            Body();
        }
    }
    AfterLoop();
    tmp = _;
}
```

The final lifetime end illustrates ordinary normal-scope cleanup after all
required borrower retirement; it does not add another action on unwind.
The original body's block is preserved. Transfers targeting the original
loop target this `while`; transfers to nested or other enclosing loops keep
their targets.

The following changes are not equivalent: computing `CreateOwner()` only
once before the loop, retaining a distinct owner after every evaluation,
retiring the final value at loop exit rather than supporting-block exit, or
placing renewal after the original body where `continue` can skip it.

## Observable owner traces

For these finite fixtures, `T` is a unique owner with synchronous cleanup
that returns normally on the exercised paths. Each completed `CreateOwner()` produces the next tag. A fixed
preallocated trace sink records events without allocating, blocking, or
throwing. The owner has no retained borrowers in these fixtures.

| Event | Observation |
|---|---|
| `Ck` | Initializer `k` completes, producing a complete owner |
| `Qk` | `Remember` tests the current owner tagged `k` |
| `Bk` | Original loop body executes with current tag `k` |
| `Gk` | A body-local guard's cleanup executes |
| `Dk` | Complete owner tagged `k` is retired exactly once |
| `Fk` | Initializer `k` fails before producing a complete owner |
| `Pk` | Its partial acquired state is cleaned before propagating failure |
| `A` | Following statements in the existing enclosing block execute |
| `L!` | A later argument has a checked failure |

`Qk` outcomes are specified by each row. Normal fixtures leave the enclosing
block immediately after `A`; failure fixtures unwind out of it. A cleanup
event does not signify storage reclamation or forced GC.

| Case | Conditions and body behavior | Exact trace |
|---|---|---|
| TRACE-MAT-01 | First test false | `C1 Q1 A D1` |
| TRACE-MAT-02 | Tests true, true, false | `C1 Q1 B1 C2 D1 Q2 B2 C3 D2 Q3 A D3` |
| TRACE-MAT-03 | Tests true, false; first body registers a guard then continues | `C1 Q1 B1 G1 C2 D1 Q2 A D2` |
| TRACE-MAT-04 | First test true; first body registers a guard then breaks | `C1 Q1 B1 G1 A D1` |
| TRACE-MAT-05 | First initializer fails after partial acquisition | `F1 P1` |
| TRACE-MAT-06 | First test true; second initializer fails after partial acquisition | `C1 Q1 B1 F2 P2 D1` |
| TRACE-MAT-07 | Standalone call materializes first argument; later argument fails | `C1 L! D1` |
| TRACE-MAT-08 | Short-circuit operand initially false | `A` |
| TRACE-MAT-09 | Short-circuit permits the first true test, then skips the next argument | `C1 Q1 B1 A D1` |

TRACE-MAT-02 distinguishes ordinary reuse from both hoisting and retained
fresh instances. TRACE-MAT-03 distinguishes the header renewal point from
body-tail renewal and verifies body cleanup before the next initializer.
TRACE-MAT-06 distinguishes build-before-drop replacement from prematurely
ending the old value.
