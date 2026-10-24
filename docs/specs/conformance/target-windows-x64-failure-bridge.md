# Windows x64 contained-failure bridge cases

These outcomes apply to the
[abstract bridge](../runtime/foreign-failure-bridge.md) and
[Windows x64 profile](../targets/windows-x64-failure-bridge.md).
They are required contract cases, not an implementation-support inventory.
Finite supplied contracts and native fixtures do not prove arbitrary body
truth, actual Terrum dispatch, every-exit retirement, roots or safe unload.

## Scope and propagation

| Case | Scenario | Required outcome |
|---|---|---|
| FBSC-01 | Ordinary foreign entry without bridge selection | Preserve direct non-unwinding signature; no hidden argument or catch thunk |
| FBSC-02 | Selected origin adapter contains a language failure | Complete local unwind before normal wire failure return |
| FBSC-03 | Receiving runtime resumes failure | Wire call has returned before local raise begins |
| FBSC-04 | Native runtime uses first-pass filters | Do not substitute that timing for Terrum incremental post-cleanup selection |
| FBSC-05 | A C++ domain offers ordinary language exceptions | No automatic SEH/hardware-fault or Terrum object equivalence |
| FBSC-06 | Native image has valid Windows unwind tables | Local unwind support only; no shared exception-object ABI |
| FBSC-07 | Raw entry returns a failure packet, source-facing proxy raises | Retain the proxy's actual source failure behavior |
| FBSC-08 | Native failure arrives through false/live packet | Exceptional protocol outcome, not automatic normal Error/Result translation |
| FBSC-09 | User catches a mapped failure | Ordinary unsafe recovery and invariant rules still apply |
| FBSC-10 | Filename, target number or runtimePolicy=1 is present | No implicit bridge selection or missing-feature activation |

## Packet representation and authority

| Case | Scenario | Required outcome |
|---|---|---|
| FBPK-01 | Independently declared packet and domain header | Packet 16/8, offsets 0/8; header 16/8, offsets 0/4/8 |
| FBPK-02 | Encode arbitrary domain/state words as machine data | Exact little-endian 64-bit fields, without persisted pointer identity |
| FBPK-03 | Live packet has State zero in a declaring domain | Valid immediate/static state; still one release responsibility |
| FBPK-04 | Live packet has Domain zero | Contract-invalid, never a readable vacant sentinel |
| FBPK-05 | Unknown domain pointer returned | Do not probe/call arbitrary header; breach/fatal path, not silent fallback |
| FBPK-06 | Header has an unsupported Version | Decline affected use before execution |
| FBPK-07 | Header ByteSize is less than 16 | Decline; no short default header |
| FBPK-08 | Header ByteSize is greater than 16 | Decline this version; no ignored public extension |
| FBPK-09 | Header Release is null or its contract is missing | Decline affected use |
| FBPK-10 | Domain does not admit a returned State | Provider breach, not an automatic allocation-pointer interpretation |
| FBPK-11 | Packet bytes are copied into a second live holder | Reject Copy/duplicate responsibility; ABI transfer copies are not another owner |
| FBPK-12 | Success leaves old bytes in vacant packet storage | Do not read Domain/State or release based on those bytes |
| FBPK-13 | Boolean carrier's upper bits differ | Required low-byte status only; upper bits unspecified |
| FBPK-14 | Low-byte status is neither zero nor one | Malformed normal outcome; no truthy integer normalization |

## Staging, outputs and failures

| Case | Scenario | Required outcome |
|---|---|---|
| FBST-01 | Non-unit operation completes normally | Source cleanup/ensures precede true/live result, failure vacant |
| FBST-02 | Supported operation failure | Private result retired, complete packet published, false/result vacant |
| FBST-03 | Unit operation completes or fails | Omit result-pointer payload; retain logical result/outcome identity |
| FBST-04 | Failure occurs during partial primary-result formation | Retire actual private acquisitions; no partial/default result publication |
| FBST-05 | Source ensure fails after forming a result | Contain failure and retire saved result without replaying completed body cleanup |
| FBST-06 | Argument staging fails before raw invocation | Caller retires still-owned staging; no invented received packet |
| FBST-07 | Moved input is owned by invoked callee, then fails | Callee/actual holder retires it once; caller source stays vacant |
| FBST-08 | Ordinary writable input was mutated before failure | Mutation remains; no bridge rollback promise |
| FBST-09 | Another declared init output is live on normal failure | Preserve its exact relation independently of primary-result vacancy |
| FBST-10 | Primary result and failure destinations overlap | Reject profile invocation requirements; do not publish overlapping responsibilities |
| FBST-11 | True with a live packet, or false with vacant packet | Contract breach, not a normal bridge outcome |
| FBST-12 | Caller reads a vacant primary/failure destination | Reject initialization/authority use regardless of old bytes |
| FBST-13 | Capture cannot establish packet or valid causal replacement | Explicit non-returning fatal action; no false/vacant or unreported substitution |
| FBST-14 | Hardware trap, stack exhaustion or process termination | No ordinary packet/default-result or crash-cleanup guarantee |
| FBST-15 | Adapter/release can escape or suspend on admitted cleanup | Decline the claimed containment/retirement contract |
| FBST-16 | Actual Terrum exits include result formation, ensure and capture faults | Execution must establish every-exit publication/cleanup; supplied traces are not proof |

## Mapping, identity and causes

| Case | Scenario | Required outcome |
|---|---|---|
| FBMP-01 | Exact eligible local failure and domain mapping are retained | Consume packet under that complete identity/ownership contract before dispatch |
| FBMP-02 | Explicit opaque local failure mapping | Preserve domain/state; no mandatory text, boxing or universal base object |
| FBMP-03 | Mapping or local catchability support is absent | Decline before affected execution; raw packet is not automatically throwable |
| FBMP-04 | Same-spelled local type or domain from another instance | No identity match without exact retained definition/runtime generation |
| FBMP-05 | Intermediate native image forwards failure | Preserve the same Domain/State, original provenance and one responsibility |
| FBMP-06 | Native re-raise is selected | Incoming wire return precedes matching-runtime raise; no Terrum frame crossing |
| FBMP-07 | Mapping fails while retaining incoming failure | New source failure retains original as cause; no lost owner |
| FBMP-08 | Authored cleanup replaces a pending failure | Preserve causal order and completed cleanup; do not replay or flatten cause |
| FBMP-09 | Filter fails or has observable false-path effects | Ordinary source chaining/selection; no silent false or rollback |
| FBMP-10 | Failure state references an already exited frame/catch local | Reject invalid capture/dependency lifetime |
| FBMP-11 | Shared observer's original provenance would be overwritten | Preserve original state; no destructive shared propagation update |
| FBMP-12 | Capture propagates after catch or repeatedly | Explicit capture contract; unique consumed, sharing required for repetition |
| FBMP-13 | Actual Terrum typed/filter dispatch receives a packet | Runtime must preserve eligible identity and cleanup-before-filter ordering |
| FBMP-14 | Actual foreign payload/cause owns managed roots or opaque runtime state | Domain must establish complete real lifetime/capture behavior, not infer it from two words |

## Callbacks and nesting

| Case | Scenario | Required outcome |
|---|---|---|
| FBCB-01 | Native calls matched synchronous Terrum callback | Callback contains local failure and returns Boolean/packet normally |
| FBCB-02 | Terrum enters native adapter which calls back into Terrum | Apply the paired boundary independently at each entry/return |
| FBCB-03 | Callback has explicit stable context | Preserve exact backing/generation/capture/code lifetime; no inferred closure descriptor |
| FBCB-04 | Callback context expires before its last use | Reject retained lifetime |
| FBCB-05 | Native adapter raises from callback failure | Callback wire return precedes native raise |
| FBCB-06 | Nested/reentrant bridge calls fail | Distinct invocation outputs/holders; no last-error overwrite or packet theft |
| FBCB-07 | Native operation would continue callbacks after unhandled failure | Decline incompatible stop/propagation contract; no default-value continuation |
| FBCB-08 | Native callback API lacks the matched failure channel | Require another complete explicit adapter; no guessed zero/false/TLS convention |
| FBCB-09 | Deferred/async callback or general managed closure is requested | Separate complete profile required |
| FBCB-10 | A callback failure retains moved inputs or partial results | Their actual holders retire/transfer exactly once without source revival |
| FBCB-11 | Foreign unwind/longjmp would skip Terrum owners | Unsupported crossing, even with an admitted packet bridge |
| FBCB-12 | Native adapter handles one failure then deliberately continues | Explicit recovery consumes/retains original responsibility before the next callback |
| FBCB-13 | Actual Terrum callback nests/reenters and then fails | Real runtime must contain propagation and preserve source cleanup/capture lifetimes |
| FBCB-14 | Native callback unwinder has its own handler timing | Its raise is wholly native and starts only after the callback returned |

## Retirement, origin and retained lifetime

| Case | Scenario | Required outcome |
|---|---|---|
| FBLF-01 | Owning packet is discarded, mapped or forwarded | Exactly one holder retains each live responsibility; final retirement calls Release once |
| FBLF-02 | By-value packet transfer uses an ABI indirect copy | Original consumed; supporting bytes do not become a second owner |
| FBLF-03 | State zero represents a static failure | Call its admitted release once; do not special-case zero as vacant |
| FBLF-04 | Release contract may report native resource failure | Use declared reporting/supervision; no silent success or blind retry |
| FBLF-05 | Leaf failure is returned through Middle to Root | Leaf domain/allocator/release/helper origin stays retained |
| FBLF-06 | A cause or borrowed native re-raise still uses state | Do not release packet/domain before that use ends |
| FBLF-07 | Provider replacement changes Domain/Release behind live packets | Retain original runtime generation/contract until its uses end |
| FBLF-08 | Old descriptor address is reused for a later domain | No compatibility with old generation; old live obligation prevented retirement |
| FBLF-09 | Native internal holder is reference counted/copyable | Packet remains affine; no automatic repeated propagation or thread capability |
| FBLF-10 | Cross-thread transfer is requested | Complete checked handoff/capability/root/release-affinity contract required |
| FBLF-11 | Payload has a pending Task or async retirement duty | Bridge cannot silently settle/drop it under nonsuspending Release |
| FBLF-12 | Packet/mapping/cause retains allocator, root or code helpers without direct machine imports | Preserve those actual deployment edges |
| FBLF-13 | Real descriptor/image/root retirement is attempted | Execution must establish quiescence and valid final release, not infer it from packet counts |
| FBLF-14 | Real native/source exits change nonvolatile or floating control state | Standard call and admitted internal-unwind restoration required on actual paths |

## Signature and independent admission

| Case | Scenario | Required outcome |
|---|---|---|
| FBAD-01 | Two ordinary arguments int32/float64 then result/failure pointers | RCX/XMM1/R8/R9; Boolean return, no hidden result pointer |
| FBAD-02 | Four ordinary arguments then result/failure pointers | Fifth/sixth physical slots at entry RSP+40/+48 with standard shadow/alignment |
| FBAD-03 | Unit result changes wire arity | Omit only result payload pointer; preserve exact logical unit result |
| FBAD-04 | Unsupported extended primitive is requested directly | Decline; explicit matched bits/buffer does not invent a foreign primitive |
| FBAD-05 | Ordinary signature gains an undeclared failure parameter | Incompatible; bridge is a separately matched entry |
| FBAD-06 | Missing packet/failure/mapping/callback codecs in retained TRRM | Unsupported affected feature; no activation by Attr, reserved bits or runtimePolicy |
| FBAD-07 | Required domain/capture/release/helper closure is incomplete | Decline before affected binding/lowering/execution |
| FBAD-08 | Readonly packet inspection has ordinary aliases | No automatic NoAlias, Copy, sharing or lifetime extension |
| FBAD-09 | Replacement changes output, effects, cause or ownership guarantees | Apply existing directed binding relation including relied-on stronger facts |
| FBAD-10 | Header/version and declared contracts are inspected for admission | Do not execute capture/mapping/release to discover meaning |
| FBAD-11 | Packet bytes, DLL load, export name or integrity digest match | Do not infer body truth or complete local failure support |
| FBAD-12 | Independent native producer/consumer passes its selected C++ fixture | Evidence for that wire/domain only; not Terrum or arbitrary-domain conformance |
