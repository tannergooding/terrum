# Cooperative cancellation and terminal settlement

**Status: unapproved, provisional direction.** This candidate closes only the
request/token and cancellation-failure admission left open by
[task responsibility](../specs/language/task-responsibility.md#cancellation-placement-and-independent-admission).
The user's provisional-direction authorization permits implementation work
against this candidate pending later review; it does not adopt normative
text, establish compiler support or select a physical schema.

## Decision and preserved baseline

Recommend a borrowed, cooperative request profile: one affine
`CancellationSource` owns monotonic request state; Copy `CancellationToken`
values observe that same state; an explicit checkpoint can raise an ordinary
Core exception. Existing consuming await, not a new cancel-and-wait API,
establishes the exact operation's checked terminal relation and settlement.
The initial profile admits same-thread request/observation, including
cooperatively interleaved async work; it does not admit concurrent access or
thread handoff.

Preserve these accepted owners unchanged:

- [Async execution](../specs/language/async-execution.md): establishment,
  eager execution, synchronous versus failed-completion channels, applicable
  cleanup before publication and ordinary causal chaining.
- [Task responsibility](../specs/language/task-responsibility.md): one affine
  completion owner, consuming wait, separate status/output/payload facts and
  explicit every-exit settlement or valid transfer.
- [Async iteration](../specs/language/async-iteration.md#completion-and-observation)
  and [generated producers](../specs/language/generated-producers.md#close-ownership-and-asynchronous-work):
  pending reservations, failed-output vacancy after cleanup, entered-scope
  unwind and once-only required close.
- [Lifetimes](../specs/lifetimes.md#declaration-defaults-exceptions-and-published-compatibility),
  [dependencies](../specs/language/dependencies-and-views.md) and
  [ownership](../specs/language/ownership-and-cleanup.md): requests do not end
  retained extents, restore authority, retire backing or manufacture cleanup.
- [Local failures](../specs/language/local-failures.md) and
  [captured events](../specs/language/captured-exceptions.md): actual event
  identity, explicit recovery, causes and responsible propagation.

Within provisional use, this is the candidate owner replacing only the
token/source signatures and cooperative-request paragraph of the
[Core-library task sketch](core-library-reference.md#tasks-await-cancellation-and-completion-responsibility).
It does not select that sketch's TaskStatus, TaskOutcome, awaiter, TaskScope
or executor APIs. No aliases, token inheritance, callbacks/registrations,
forced interruption, timeout/race, supervisor or multi-operation policy is
introduced.

### Alternatives

| Candidate | Consequence |
|---|---|
| **Recommended minimum amendment:** borrowed request state, explicit checkpoint, ordinary failure and existing consuming wait | Preserves accepted operation/ownership identities and exposes the missing request-versus-settlement tests without adding another responsible holder or observation path. Source lifetime remains an explicit obligation. |
| Independently owning shared request-state tokens, with a concurrent request/observation protocol | Can let tokens outlive the source handle and support remote requesters, but requires a different ownership/release, thread-capability and synchronization contract. This is a material redesign, not an interchangeable representation of the borrowed profile; defer it. |
| An operation-bound consuming cancel-and-settle operation | Could retain responsibility until actual cessation, but needs payload/output/failure and noncooperative-operation rules beyond the existing wait. A request-only shortcut that releases backing would contradict the preserved baseline. Do not add either API here. |

## Proposed Core source/API surface

These are **proposed signature requirements**, not executable declarations.
All identities below are public, nongeneric members/types in
`Terrum.Threading.Tasks`; the existing generic Task family is unchanged.

| Declaration/member | Exact proposed shape and contract |
|---|---|
| CancellationSource | `public mut unique class CancellationSource`, with `public CancellationSource()` creating fresh, initially unrequested state |
| Source token getter | `public CancellationToken Token { get; }`; each result observes the same source instance |
| Request | `public mut void Cancel()`; request only, never task consumption or settlement |
| CancellationToken | `public struct CancellationToken`; immutable Copy descriptor with no public construction or valid default fabrication |
| Token query | `public bool IsCancellationRequested { get; }` |
| Explicit checkpoint | `public void ThrowIfCancellationRequested()` on CancellationToken |
| Cancellation failure | `public class CancellationException : IException<CancellationException>`; the existing common exception creation paths and instance contract, with no additional token, source or operation fields |

The Token getter publishes the accepted whole-receiver `DependsOn(this)`
relationship. A class receiver means the actual object/backing, not the
source's handle slot. Copying or forwarding a token preserves that exact
dependency; it does not duplicate request authority or retain an independently
owned request allocation. A permitted move of the unique source handle
preserves the same object and request identity, not a new request generation.
There is no reset, linked source, implicit absent token or `None` member.
An API that does not accept cancellation simply has no token parameter.

For valid admitted live values, Token acquisition, the Boolean query and
Cancel are synchronous, nonsuspending and nonthrowing. Cancel performs no
callback, allocation, user cleanup or wait. Construction can fail under
ordinary construction rules, without publishing a source or starting work.
Source retirement is nonsuspending/nonthrowing state retirement only after
its dependents permit it; it does not implicitly Cancel. These are scoped
operation contracts, not a general NoThrow annotation.

Token acquisition and query/checkpoint do not mutate request state. The query
and Cancel are not Pure/ConstEval operations or general synchronization
edges. Cancel is an explicitly permitted noninvalidating request-state
mutation while tokens exist; it does not replace their backing or invalidate
them. No ThreadTransferable/ThreadShareable promise follows from Copy,
uniqueness or this API. Concurrent request/observation requires a separately
selected protocol and is outside this initial profile, even if an
implementation happens to use an atomic flag.

## Request identity, checkpoints and terminal outcomes

A source construction establishes a logical request identity R. All its
tokens observe `unrequested -> requested`; Cancel is idempotent and there is
no transition back. In this serialized profile, a query after Cancel returns
true and remains true. A query before it returns false. This establishes only
request state, not publication of unrelated data.

An established operation has its separate existing identity O. Several
operations may inspect R, and an operation may explicitly inspect several
tokens. R does not identify any one O, output destination/version or task
owner. Neither passing a token nor matching a parameter name registers
operation cancellation. There is no ambient token or automatic checkpoint at
invocation, await, yield, close or cleanup.

ThrowIfCancellationRequested reads its receiver's request state once. False
returns unit normally. True invokes CancellationException's common default
creation path once and raises the resulting fresh event under ordinary
throw-origin rules. Different checkpoint failures create different events,
not a cached exception stored in R. If event preparation fails, propagate
that actual failure under its admitted contract; if no failure can be
represented, preserve the existing fail-fast rule. Never fabricate cancellation
or success from an allocation/transport failure.

The exception retains its own durable message, origin and ordinary causes.
Checkpoint construction adds no token/source dependency; independently
retained cause state still has its ordinary dependencies. Exact Core identity
matters, not the message or a same-spelled provider class. Common construction
and explicit throw remain available: a CancellationException is a cancellation failure,
not proof that any particular request was made. No request-id or
operation-id comparison API is selected. An ordinary typed catch retains the
existing unsafe recovery requirements; cancellation adds no safe recovery
shortcut.

| Actual operation outcome | Terminal representation and observation |
|---|---|
| Work/required cleanup still pending, whether or not R is requested | Nonterminal; actual owner, captures and reservations remain |
| Normal body and applicable cleanup/completion checks succeed | Successful T and any selected output relation, even if a request was ignored or handled |
| The escaping primary event is the exact CancellationException | Failed completion carrying that same event; “terminal cancellation” is this failure category, not another payload/outcome carrier |
| Another event escapes, including a failure replacing cancellation during cleanup | Failed completion carrying the actual primary event and its complete causal chain |

There is no new public TaskStatus enumeration or TaskOutcome variant here.
Successful `Result`/status/domain-error payloads remain data. A caught and
explicitly handled checkpoint failure does not force later cancellation.
An awaited/captured cancellation event keeps its identity when it propagates
through another operation; each operation independently has to establish
cessation, cleanup and its terminal relation. An event object alone proves
none of those facts.

Before operation establishment, a checkpoint failure is synchronous and
cannot strand acquired state. After establishment, escaping failure is
failed completion even without suspension. Producer creation remains
synchronous and does not execute its lazy body to inspect a captured token.
An already-requested token does not bypass argument evaluation, parameter
transfer, preconditions or the existing establishment boundary.

## Settlement, cleanup and retained backing

The required ordering is:

1. **Request:** Cancel changes R only. It consumes no task, cancels no wait,
   publishes no output/vacancy and restores no loan or remote authority.
2. **Execution exit:** an escaping checkpoint event follows ordinary entered
   finally/defer and the operation's explicitly required cleanup. Skip
   normal-completion ensures on failure. Clean private/partial outputs once;
   never publish false/exhaustion or a default T as cancellation.
3. **Terminal publication:** establish cessation of all remaining use
   attributed to O and completion of its required execution cleanup/checks.
   Any valid responsibility transfer retains its actual new holder and full
   dependencies; it is not permission to hide still-running work behind a
   completed task. A later cleanup failure becomes primary with the pending
   event retained under ordinary causal chaining. Do not replay cleanup.
4. **Checked observation:** `await move operation` consumes the named task
   into the existing wait, retains its dependencies through suspension, and
   observes O's exact success/failure and output-version relation. The source
   is vacant on either exit; failure delivers the same event, not an Error
   projection or fresh rethrow. IsCompleted alone is insufficient.
5. **Dependent retirement:** only then discharge the observed operation's
   reservations and ended capture extents. Transferred payloads, published
   loans, failure/cause-owned state and other surviving dependents keep their
   independent backing, roots, guards, code and cleanup obligations.

A request never shortens a declared capture bound. Token backing must remain
valid through its last retained use, including producer/operation cleanup.
Data backing, destination storage, owner/lease/guard, root, relocation and
affinity obligations likewise remain until their checked endpoints. A root
or a failure handle does not substitute for the responsible owner. Broad
published sources cannot be shortened from a provider's private body.

Required settlement of nested tasks must be authored or explicitly transferred
on every checked exit, including cancellation cleanup; it is not injected by
the checkpoint. An ordinary consuming wait cannot return early just because
its producer's token was requested. Noncooperative work can remain pending
indefinitely with a valid owner and backing. No fairness, deadline or progress
guarantee is added.

Automatic normal-retirement Cleanup is not exceptional-unwind machinery.
This proposal adds neither a generated destructor during unwind nor a hidden
blocking/async cleanup bridge. Checked terminal promises require the selected
operation's actual cleanup/transfer contract. Fail-fast, process termination
and killing a worker retain their separate absence of an ordinary cleanup
promise; none is evidence of this checked cancellation relation.

Once an operation has published success or failure, a later request cannot
rewrite it. A published item stays live with its loans; an already-published
fault remains that event. A request during a pending failure does not replace
the event either. An explicitly executed checkpoint during authored cleanup
can itself raise a new failure, but must preserve the pending event as cause
and all outstanding settlement obligations under ordinary unwind rules.

## Generated-producer and cursor integration

Tokens are ordinary captured inputs; there is no new producer parameter,
cursor role or generated token field name. Creation copies their descriptors
with complete source bounds. Never-advanced close releases captures without
starting the body or executing a body checkpoint. Requesting cancellation of
a suspended producer does not resume it or close its traversal.

An explicit checkpoint escaping an advance makes that traversal failed and
cleanup-safe, not exhausted or resumable. For async advance, its particular
operation retains mutable cursor access and its exact init destination until
checked observation. Cancellation leaves that destination vacant only after
private/partial-item cleanup and observation of that failed operation.
Neither Cancel, a token query nor IsCompleted permits caller state-testing,
initializing, reusing or retiring the reserved destination.

Settle pending advance, then published item/loan obligations, then invoke the
required close exactly once. Await CloseAsync before dependent cursor/header/
source retirement. Early close unwinds only entered scopes, never remaining
production code or successful body-completion ensures. There is no implicit
poll that lets an already-requested token skip close cleanup. Authored cleanup
that can itself fail must still leave the accepted cleanup-safe residual state
and settle/transfer every outstanding responsibility. A close failure
preserves the actual event/causes and does not request another close.
Generated owner destruction remains nonthrowing/nonsuspending residual
retirement, not completion of abandoned close work.

## Adoption, persistence and implementation gates

After approval, make these precise integrations; this patch changes none of
these accepted owners:

| Owner | Intended integration |
|---|---|
| New `docs/specs/language/cancellation.md`, linked from the specification index | Own the proposed exact Core surface, logical borrowed-request profile, checkpoint and cancellation-failure mapping above; no grammar production changes |
| [Task responsibility](../specs/language/task-responsibility.md#cancellation-placement-and-independent-admission) and [async execution](../specs/language/async-execution.md#completion-cleanup-and-observation) | Replace only the request/token and failure-representation deferrals with the new owner; retain independent timeout/race/supervisor, awaiter, placement and physical-runtime gates |
| [Async iteration](../specs/language/async-iteration.md#completion-and-observation) and [generated producers](../specs/language/generated-producers.md#close-ownership-and-asynchronous-work) | Reference the selected request/failure profile without changing operation-bound output, lazy creation or required close semantics |
| [Adopted library inventory](../specs/libraries/adopted-inventory.md) | Record only this partial cancellation family; do not adopt adjacent Core-library task, observation, scope or I/O APIs |
| [Logical records](../specs/representation/logical-records.md#task-responsibility-encoding-pending) and its [async/producer requirements](../specs/representation/logical-records.md#async-execution-and-generated-producers-encoding-pending) | Retain exact Core/member and failure-event identities, request-source dependencies distinct from operation/output versions, failure phases and every-exit/close obligations; keep unassigned physical carriers gated |

Source binding/verifying checks those identities and actual dependencies,
checkpoint effects, complete failure transport and all exit paths. Shared
bound-tree traversal, rewriting, forwarding and lowering preserve the same
facts. Polling is an ordinary state read, not a captured Boolean snapshot;
lowering may not turn a token into its acquisition-time request value.
Providers, including foreign-operation adapters, may claim terminal
cancellation only after actual cessation and required cleanup. An OS request
acknowledgment, host exception, status code or stopped waiter is insufficient.

Separate compilation/replacement must preserve exact resolved declarations,
borrowed-source contracts, serialized profile, establishment/failure phases,
ordinary event/cause ownership and operation/output mappings. Declarations
retain source/formal relationships; invocation binds actual R, O and storage
versions. Runtime identities are not artifact row numbers. Consumer checks
cover schema selection/support, structural bounds/references, exact
Core registration and directed binding/replacement before affected use.
Providers publish truthful body/lifetime facts; consumers need not semantically
re-prove them. A false cessation/cleanup assertion is a provider contract
violation, not a permitted success-shaped fallback.

This decision allocates no feature ID, opcode, Body record, task/frame/token
layout, target ABI or private revision. Ordinary calls need no new cancellation
instruction. Existing declaration/dependency machinery is reusable where
complete, but is not a substitute for missing task/failure/phase codecs.
Independently consumed artifacts needing unrepresented obligations must
decline before use, not encode them in optional Attr data, guess from names
or accept an equal-version incompatible draft. Exact-schema selection follows
[format versioning](../specs/representation/format-versioning.md).

## Required conformance and remaining gaps

These are proposed logical cases, not claims of passing compiler tests:

| Case | Required outcome |
|---|---|
| Fresh source, copied tokens, repeated Cancel, separate source | Initially false; all copies observe their R becoming permanently true; another R remains unchanged; no duplicated request authority |
| Token survives a permitted source-handle move; default/forged token; backing ends early | Preserve actual object identity on valid move; reject fabricated validity and retirement while any retained source dependency survives |
| Already-requested token at async invocation or producer creation | Preserve staging/establishment and synchronous creation/lazy body; no hidden early cancellation |
| Checkpoint before versus after establishment; exception construction fails | Correct synchronous versus failed-completion channel; actual construction failure, no fabricated cancellation |
| Two operations share a token; one ignores or handles cancellation | Settle each exact operation independently; permit checked success after handling/ignoring; request alone completes neither |
| Cancel/query/IsCompleted while async output is reserved | No caller availability or retirement; only the exact checked terminal observation establishes failure/vacancy after cleanup |
| Cancellation after partial output formation; normal completion-check failure | Retire unpublished state once; no success/exhaustion; no replay of completed cleanup |
| Body cancellation followed by cleanup fault; ordinary fault followed by request | Actual cleanup fault is primary with the cancellation cause preserved; request does not replace the ordinary fault |
| Consuming await, forwarding and consuming captured rethrow | Preserve event/origin/causes, exact O/output generation and once-only ownership; old task source remains vacant on failure |
| Success/output already published before Cancel; surviving lent item | Preserve success/live item and its complete backing; cancellation observation does not end an independent loan |
| Never-advanced or suspended producer; pending advance; failed close | No body entry on unstarted close; settle advance/item before close; entered-scope-only unwind, awaited close before backing retirement, no second close |
| Cancellation cleanup owns a nested task or would release a needed guard | Require explicit settlement/valid transfer and dependency-safe order; reject abandonment or premature authority return |
| Noncooperative operation, concurrent request/query, incomplete dependency artifact or lying provider | Pending operation retains responsibility indefinitely; reject unsupported concurrent/profile/schema use; false provider facts do not relax the contract |

Current support remains the independent
[CG-021 gate](../implementation/conformance-gaps.md#cg-021), with shared
reference, failure/cleanup, ownership, runtime and physical-representation
prerequisites. The inspected C# [binder](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs)
uses System.Threading cancellation for compiler execution;
[CompilationTests](../../tests/Terrum.CodeAnalysis.Tests/Semantics/CompilationTests.cs)
checks that host cancellation is not a successful compilation.
The [compiler-host ICancellation sketch](../../library/compiler-host.api.terrum)
is also not this registered target family. None establishes Terrum token,
task/frame/failure transport, generated cancellation cleanup, native emission
or runtime support.

The provisional recommendation resolves the source/API and logical settlement
choices for the serialized borrowed profile. Approval remains outstanding.
Independent owning tokens and concurrent request/observation remain unselected
alternatives/extensions, not implementation details to invent. Exact required
persistence schemas, target representations/runtime support and active
positive/negative qualification remain implementation gates; this
documentation pass does not close them.
