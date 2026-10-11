# Boundary-contained foreign failure bridge

## 1. Scope and contract identity

Logical `foreign-failure-bridge/1` defines explicitly selected exception
interchange through paired adapters. Each runtime contains propagation within
its own frames. A normal procedure call returns across the language boundary
with either its declared result or an owned failure packet; only then does the
receiving runtime resume propagation or perform a declared translation.
No foreign unwinder crosses live Terrum frames under this contract.

Ordinary foreign calls remain direct and non-unwinding. Selecting this bridge
does not change their signature, add a hidden argument, install a catch thunk
on every call or give a normal status a universal exception meaning.
[Windows x64](../targets/windows-x64-failure-bridge.md) supplies the first
physical bridge. Another target needs its own complete representation and
procedure-call contract.

This bridge does not unify C++ exceptions, SEH records and Terrum failures.
It assigns neither a universal exception base class nor a general source
construction/capture API. [Local failure contracts](../language/local-failures.md)
define Exception/category opt-in and explicit reference/Copy catch bindings;
typed construction, captured state and actual dispatch support still require
independently complete admitted contracts. A raw packet
is not automatically an eligible `throw` operand or a payload for every catch.
Missing local failure support declines the affected bridge before execution;
a normal wire return is not a substitute for that support.

The selected source failure semantics remain authoritative:
[incremental unwind and selection](../language-contracts.md#33-normal-and-exceptional-exits)
complete exited inner cleanup before testing each still-live candidate scope.
Foreign first-pass filters cannot replace that ordering.
Fail-fast execution remains distinct and promises no checked cleanup.

## 2. Selected pair, domains and roles

An admitted bridge retains the exact source definition, normal wire entry,
originating and receiving runtime domains, supported failure-domain set and
the following roles. Names below describe roles, not compiler-known members
or a new attribute schema.

| Role | Responsibility |
|---|---|
| Origin adapter | Invoke the selected operation, contain its supported escaping failures, complete local cleanup and return the appropriate wire outcome |
| Capture | Establish one complete failure packet with valid payload, provenance, transitive responsibility and release support |
| Receiving adapter | Validate the declared wire outcome, consume the failure packet into its selected local failure mapping, then resume local propagation or explicitly translate it |
| Domain release | Retire the packet's remaining domain-owned responsibility once, nonthrowingly and nonsuspendingly |
| Optional native re-raise | Raise within its explicitly matched originating native runtime after the incoming wire call has returned |

A failure domain is an exact admitted runtime instance and generation, with
its selected capture, interpretation and release contract. It is not a DLL
basename, exception message, hashed type name, OS error number or arbitrary
pointer discovered from an untrusted payload. Different domains may use
different payload representations and exception runtimes.
Forwarding does not change a failure's domain to that of the intermediate
image.

The wire carries an opaque domain-owned state, not a cross-language object
layout. The domain's payload can be private storage, an immediate state or
another completely admitted holder. Its contract establishes validity,
storage/root lifetime, provenance and cleanup; equal machine words prove
none of them. A foreign runtime can retain its own exception object through
an opaque handle without exporting its managed-reference representation.
The corresponding root/handle and runtime/code obligations must actually
be established, not inferred from the packet's rootless machine fields.

Capture must preserve every identity/provenance property promised by the
selected failure domain. A native capture operation that copies an exception
does not establish original object-address identity. For example,
Microsoft's [exception functions](https://learn.microsoft.com/en-us/cpp/standard-library/exception-functions?view=msvc-170),
source revision `440177ddc9e1d0f715c8c0c249dcfdfe61a5b88f`, describe
copies made by `current_exception`. That is informative native evidence,
not a Terrum exception-object format or permission to copy an affine packet.

## 3. One responsibility and valid retained state

A live packet owns one responsibility. It is affine, not Copy, even if its
fields or the originating runtime's internal exception holder are copyable.
A named transfer consumes it under ordinary `move` rules. ABI supporting
copies used to implement that transfer are not independently live packets.
A failed call does not revive an already moved source.

Borrowed inspection does not transfer responsibility. Moving, raising,
mapping, replacing or retiring a packet must preserve every actual dependent.
Its state cannot refer to retired local storage, an exited catch variable or
an expired callback context. Source capture does not keep already unwound
frames live, rerun their cleanup or grant access to invalid references.

Domain release has the complete transitive nonthrowing/nonsuspending
retirement contract. It must release through the actual allocator/runtime
and code origin. A release requiring pending async settlement, a throwing
destructor or an unsupported cleanup affinity does not satisfy this bridge.
Expected native release errors need their declared reporting/supervision
contract; a `void` release is not permission to silently claim release.

Retain the domain descriptor, capture/mapping/re-raise/release code, payload
backing, allocator and any internal root/runtime while a packet, active call,
borrow, cause or captured failure can use them. Returning through an
intermediate adapter does not discharge those lifetimes.
No hidden process-wide ownership registry or per-value image retain is
required: the selected deployment must establish the obligations through its
admitted program/runtime contract.

The baseline packet grants no sharing, repeatable rethrow or cross-thread
authority. Such use needs the actual checked sharing/capture/handoff and
release-affinity contract. Internal reference counting in a native runtime
alone grants none of those source capabilities.

## 4. Invocation, completion and wire return

The selected bridge has a normal status and complete correlated output
states. The [Windows bridge](../targets/windows-x64-failure-bridge.md#3-wire-entry-and-normal-outcomes)
uses Boolean success, an optional primary-result destination and a separate
failure-packet destination. This is an explicit special entry, not the
signature of every imported method.

Source arguments evaluate and stage under ordinary invocation rules.
Pre-call failure cleans still-owned caller staging without inventing a
received packet. At actual invocation, declared input ownership transfers;
contained callee failure does not restore those sources. Captured failure
state cannot borrow the caller's staging after its lifetime ends.

On normal source completion, run the source operation's required body
cleanup and normal postconditions before publishing the bridge's primary
result. On a contained escaping failure, retire private partial/saved result
responsibility and establish the failure packet before publishing the
failure outcome. Never expose partial result bytes as a default or let two
live outputs own the same responsibility.

Other ordinary writable arguments and explicitly declared outputs retain
their own exact success/failure relations, including live failure outputs
where declared. This bridge is not rollback for those arguments, prior I/O,
cleanup, mutations or accepted prefixes. Its designated primary-result
publication rule does not redefine all source outputs as transactional.

The origin adapter contains its own supported propagation as well as that
of the wrapped operation. Its normal wire entry has no escaping language
exception. This does not make the wrapped source operation or receiving
proxy NoThrow: raising from a failure packet remains a source failure effect.
Selecting a bridge cannot erase that effect in binding or optimization.

## 5. Typed mapping, propagation and causal replacement

A receiving bridge declares its exact eligible local failure identity and
mapping. Complete type identity, qualifications, dependencies, payload
ownership and domain agreement matter; a same-spelled type is insufficient.
The mapping may retain the packet as opaque owned state or construct a
specifically supported local failure value. There is no mandatory conversion
to text, boxing, universal `Exception` or normal `Error` result.

Perform the selected mapping before entering local failure dispatch.
An ordinary local payload must have its exact admitted Exception capability;
a captured event instead needs its separately checked propagation protocol.
The annotation alone supplies neither mapping, capture nor domain ownership.
Typed catch tests do not invoke arbitrary foreign mappers, inspect an
unadmitted exception object or dynamically guess a failure type.
The mapper's effects, possible failure, output/partial cleanup, captures and
required support are declared. It cannot lose the incoming packet while
trying to establish a replacement.

An opaque fallback is permitted only if the bridge explicitly names its
eligible local failure contract and preserves the original domain state.
It is not a silent fallback for missing mapping, unknown domain, unsupported
semantics or malformed output. Translation to a normal domain-error/status
API is likewise explicit and publishes that API's complete output and
retirement contract; it is not automatic on every foreign failure.

Forwarding an unchanged pending failure preserves its captured identity,
provenance and owned state. A receiver can later hand that state back to an
explicitly matched originating-runtime adapter for native re-raise. The
incoming wire call must have returned before the native raise begins.
The common packet does not itself publish a universal rethrow function or
promise that every domain supports native re-raise.

On source cleanup, filter or mapping failure, the replacement preserves the
earlier pending failure as its cause under the
[source failure rules](../language.md). Completed cleanup is not replayed.
The cause retains its own packet/domain responsibility until the cause
holder retires it; replacing the top failure is not implicit release of its
cause. Preserve original provenance without destructive updates shared by
other observers. No exception-object address, stack-trace text format or
physical cause-chain layout is assigned here.

[Core lexical capture](../language/captured-exceptions.md) can retain an
already established local event from its active source handler body.
Its opaque affine owner and consuming throw preserve that event and all
packet/domain dependencies. A source-independent mapper needs its separate
checked event-adoption/transport contract; it cannot invoke Capture outside
that context or manufacture a Core owner from packet words.
No public adoption or shared/repeatable capture API is selected. Possessing
a native `exception_ptr` or two equal packet words bypasses none of these
contracts.

## 6. Callbacks, nesting and reentrancy

A callback uses the same boundary in reverse. The Terrum callback contains
its propagation before returning normally to the native caller. A matching
native adapter can then stop the native operation, forward the packet or
raise within its own supported native runtime. A native raise from that
callback outcome begins only after the callback has returned. Nested
native calls may fail inside their own containing adapters while outer
Terrum frames wait below those adapters; no such unwind crosses those frames.

Each active invocation owns its own result/failure destinations and pending
state. Nested calls and reentrant callbacks cannot overwrite a thread-global
"last exception", steal another invocation's packet or mistake its state for
their own. Context and function addresses retain their actual borrow,
generation, code and cleanup lifetimes; this contract assigns no general
closure or stored checked-reference descriptor.

The native operation's contract says how callback failure stops or propagates
the operation. After failure, it cannot continue as though a default callback
value were successful, call again by accident, or discard an earlier packet
when another callback occurs. Deliberate recovery must consume/retain the
failure under its explicit contract.
A callback with no admitted failure channel/stop behavior is unsupported;
the bridge does not guess zero, false, null or an ambient side channel.

The Windows profile admits its explicitly matched synchronous Boolean
callback entry and stable context. Deferred callbacks, general managed
closures, tasks/continuations and async settlement require separate complete
profiles. A packet is not a task join or authority to abandon a pending
operation.
`longjmp`, uncontained foreign unwind or an OS handler cannot skip live Terrum
owners merely because this bridge exists.

## 7. Capture failure, breach and fatal outcomes

A normal failure return requires a complete live packet. Capture exhaustion,
an unsupported runtime failure encountered contrary to the admitted
contract, malformed publication or a violated release/containment promise
cannot become false with vacant failure storage, true with a default result,
or a silently substituted exception.

Establish a checked replacement with the original cause only where the
selected runtime/domain can do so completely. If valid transport/replacement
cannot be established, use the selected host's explicit non-returning fatal
failure action. It must neither unwind through the wire boundary nor return
a normal protocol outcome. No process-status number, crash cleanup,
transactional rollback or durable-state recovery guarantee is implied.
Capacity exhaustion, physical OOM and a provider contract breach remain
distinct reported/fatal conditions, not successful capture.

Hardware traps, stack exhaustion, arbitrary SEH faults and process
termination are not ordinary captured exceptions in this profile.
A native language's terminal failure while unwinding is not retroactively a
Terrum causal chain. Broader recoverable-fault interchange needs its own
complete safety, state and transport contract.

## 8. Admission and representation requirements

Before affected binding/lowering/execution, retain and admit:

| Required content | Agreement |
|---|---|
| Pair and definitions | Exact origin/receiving definitions, normal wire entry and supported native callback/re-raise roles |
| Rules and carriers | Governing bridge/source/target editions, full logical signatures, physical parameters/results and valid output correlation |
| Failure interpretation | Exact supported domain instances/generations, capture validity and provenance, eligible local type and selected mapping/translation |
| Responsibility | Input/result/packet transfers, captures, causal replacement, partial cleanup, transitive release and actual dependencies |
| Runtime and deployment | Local failure/catch/capture/dispatch support, roots/handles where used, control/unwind support, resources, helpers and retained code/allocator lifetime |
| Provider truth | Checked or explicitly trusted implementation of containment, effects, output, mapping, capture and release contracts |

Use the existing
[directional binding and replacement](../representation/binding-and-replacement.md)
relation, including actually relied-on stronger facts.
Names, byte widths, integrity hashes, a loaded DLL or OS unwind metadata do
not prove that relation or body truth. Do not invoke capture, mapping or
release during admission to discover what the provider meant.

Logical bridge identities assign no `.trrm` Feature ID, new Attr schema,
reserved-bit use, opcode, exception region or value codec. Missing physical
records for source-free import/export, local failures, owned packets,
callbacks or mappings remain an unsupported affected requirement.
`runtimePolicy=1` and a target number do not activate this bridge.
Private native package bytes remain private.

[Required cases](../conformance/target-windows-x64-failure-bridge.md)
separate finite contract/ownership evidence and independent native wire
interoperability from actual Terrum dispatch, every-exit cleanup, root,
control and unload conformance.
