# Windows x64 local exception-object transport

> **Unapproved; provisionally actionable under the explicit missing-contract
> authorization.** This proposal establishes neither implementation nor native
> runtime support.

## Decision

Complete the Windows x64 source-failure path using Terrum's existing
exception-object contract: the exact, fully initialized class object is the
propagation event. Do not expose scalar failure reasons as source exceptions
or allocate a separate public event wrapper.

The current native failure stubs emit scalar `E0540001..4` reasons, while the
target contract says OS unwind recovers frames but does not supply Terrum
exception identity. Under `CheckedExceptions`, every source failure reaching
a source catch must be constructed under its exact bound category and
transported as the actual object. A raw v1 reason is never source-catchable.
Foreign SEH remains separate: selected `NativeExceptionUnwind` normalizes an
admitted foreign event to `NativeException`.

| Candidate | Disposition |
|---|---|
| Catch current scalar reasons as source exceptions | Reject. They carry neither the selected exception object nor its exact identity and causal history. |
| Reuse `NativeExceptionUnwind` | Reject. It maps admitted foreign events; it does not create Terrum-owned objects or define their local propagation. |
| Add a private local object-event path | **Recommend.** Reuse the accepted class/event, cleanup and catch contracts; keep the physical packet private to the selected Windows runtime. |

This changes no source syntax, failure category, clause order, cleanup, output
publication, or foreign ABI.

## Candidate private event record

For in-process Terrum objects, select private exception code `0xE0540093` and
exactly two `EXCEPTION_INFORMATION` values:

```text
0: marker      = 0x54455252554D0001
1: rootToken   = nonzero runtime-issued u64
```

The exception code identifies this exact private record; the existing
`0xE0540092` three-value scalar record is not an alias or fallback. The root
token is a generation-checked index in a thread- and runtime-domain-local
registry, never an address. The registry roots the actual object and tracks
its active propagation extent; it is transport state, not another event
object. The object's header supplies exact type/self-witness identity and its
authored `InnerException`; the runtime attaches `ParentException` only when
replacing a distinct pending object. `EXCEPTION_RECORD.ExceptionAddress`
supplies the generated raise-site origin. Emit the final raise at a site-local
instruction, not in a shared helper that would replace the origin.

### Root-token lifecycle

1. Construct the selected class through its exact ordinary creation binding,
   then register the completed object before raising. Construction or root
   preparation failure is its actual failure; it never creates a
   null/default event.
2. The nearest Terrum dispatcher validates the live token and keeps the same
   object rooted while crossed cleanup runs. Windows first-pass search is
   frame recovery, not source catch selection; inner cleanup precedes outer
   filters.
3. Catch binding installs the same object in its checked catch owner before
   consuming the token. Same-object rethrow retains origin and responsibility.
   Replacement establishes `ParentException` before retiring the old root.
4. An unhandled event reaches the selected terminal policy. No normal
   completion, output publication, cleanup replay, or promise of cleanup
   after fail-fast is introduced. Stale, reused, cross-thread, or
   cross-domain tokens cannot resolve to another object.

## Failure and boundary rules

| Failure | Selected object |
|---|---|
| False declaration `ensure` | Exact PostconditionException of the clause owner |
| False runtime `require` | Its exact selected `E`, callable default, or Core default |
| Checked scalar operation | Its exact standard Core category |
| Foreign SEH under `NativeExceptionUnwind` | Exact admitted native mapping or opaque Core NativeException |
| Predicate, helper, constructor, or cleanup failure | That actual failure; never an outer PostconditionException |

Under `CheckedExceptions`, each local producer of a checked failure must
construct its selected object before source catch matching. This includes
requirement failures raised while evaluating a predicate or cleanup; those
remain their own failure, not PostconditionException. A v1 scalar record may
not bypass construction and dispatch. Missing binding, construction, or
runtime support declines before affected execution.

Declaration `ensure` executes after body/finally/local and untransferred
owned-parameter cleanup, in declaration order, over its saved result and
legally surviving state. A false clause constructs PostconditionException
once, skips later clauses and publishes no successful completion. It does
not undo already initialized ordinary outputs or replay cleanup. Reserved
outputs retain their selected exceptional-settlement contract.

FailFast still follows the selected ordinary construction contract when
required, then terminates without source recovery or promised unwind cleanup.
The existing terminal-policy allowance may omit only an unobservable event;
it does not authorize skipping an observable constructor or substituting a
generic failure reason.

The provisional [Core failure identity selection](core-contract-failure-identity-selection.md)
supplies exact provider roles only; it does not supply a constructor binding.
Use ordinary checked binding and retain the actual constructor, provider,
type/witness, cleanup and code dependencies. Terrum objects never unwind
outward through native exports/callbacks. Standard E1 register-Body encoding
and private persistence remain independent exact-schema selections.

## Owners and required evidence

Candidate target integration is `docs/specs/native.md` and the Windows x64
target chapters. Preserve the accepted contracts in
[local failures](../specs/language/local-failures.md),
[call phases](../specs/language-contracts.md), and
[runtime failures](../specs/runtime/standard-failures.md). The existing
Core identity and E1 exception-region proposals remain unapproved; this
candidate does not approve their schemas or compose their encodings.

Required conformance cases include:

- Successful and false postconditions under both policies; exact object
  identity, once-only construction, saved result, cleanup order and no
  successful publication.
- Explicit/default requirement classes, standard arithmetic failures, and
  actual predicate/constructor/cleanup failures; no category substitution.
- Nested handlers, same-object rethrow, distinct-object replacement with
  `ParentException`, filter failure, and root transfer/retirement.
- Ordinary and reserved outputs, partial owners, and failures after completed
  transfers; no cleanup replay or implicit rollback.
- Foreign SEH normalization remains NativeException-only; reject native
  outward propagation and malformed, stale, reused, cross-thread or
  cross-domain tokens.
- JIT, emitted PE execution, AOT, and provider/image lifetime through every
  live event and cleanup obligation.
