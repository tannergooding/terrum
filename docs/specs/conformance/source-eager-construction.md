# Eager construction conformance cases

These cases apply to [eager-construction/1](../language/construction-handlers.md#eager-capture-and-acquisition).
Assume otherwise valid declarations, checked nominal construction/writer/
formatter contracts, and admitted iteration where used. Call-side cases
use the [TextTarget parameter schema](../language/construction-handlers.md#texttarget-parameter-schema)
where marked. Physical construction/capture representations remain separately
gated; these traces do not allocate them.
[Value-iteration cases](source-value-iteration.md) additionally distinguish
unreached factory acquisition from abandonment of an already-captured direct
cursor, whose close obligation exists without advancement.
Expected event traces describe semantic execution, not compiler conformance.

## Capture and acquisition

| Case | Situation | Required result |
|---|---|---|
| HCAP-01 | Text interpolation has holes A() and B() | Capture A then B once before its builder acquisition or drain |
| HCAP-02 | First hole has Value(), Width() and Format(); another hole calls Next() | Evaluate Value, Width, Format, Next in that order before acquisition |
| HCAP-03 | Collection is [A(), ..Source(), B()] | Capture A, Source and B once in source order before acquisition; do not enumerate Source during capture |
| HCAP-04 | A later hole value throws after an earlier capture succeeded | Stop remaining captures; acquire no affected builder; clean live staged owners under ordinary unwinding |
| HCAP-05 | Width() throws after the hole value was captured | Do not evaluate that hole's format or later holes; acquire no affected builder |
| HCAP-06 | Runtime Format() throws | Stop later holes; acquire no affected builder; retain ordinary cleanup and failure effects |
| HCAP-07 | Builder acquisition refuses or throws | All successful source captures precede that operation; their effects and transfers are not rolled back |
| HCAP-08 | Writer refuses the first literal segment | Every successful hole capture already occurred; refusal cannot skip B() |
| HCAP-09 | Admitted call-side logger is disabled and returns without formatting | Interpolation captures still execute at its argument's source position |
| HCAP-10 | A normal argument follows the interpolated argument | Complete interpolation capture before that later argument; invoke the callee after ordinary staging/checks |
| HCAP-11 | A capture contains Condition() ? Left() : Right() | Evaluate Condition and only the selected operand once; eager construction does not defeat inner short-circuit control flow |
| HCAP-12 | One outer capture constructs a nested registered value | Complete that nested site's own sequence at the capture position before later outer captures; acquire the outer builder afterward |
| HCAP-13 | Empty collection has a checked acquisition/completion protocol | No source captures; preserve that protocol's actual acquisition/completion effects |
| HCAP-14 | Interpolation has literal segments but no holes | No hole captures; remain target-directed interpolation, not an implicit noninterpolated parser fallback |
| HCAP-15 | A reference capture is followed by an allowed write to its referent | Form the reference at capture; later drain reads the then-current state, not an invented early snapshot |
| HCAP-16 | A captured Copy handle points to state changed by another capture | Preserve the copied handle and shared backing; no deep copy or assumed immutability |
| HCAP-17 | Same construction site executes twice | Perform each activation's reached source captures once; no source-result cache or replay from another activation |
| HCAP-18 | A surrounding policy selects a declared variant of a hole call | Preserve that call's ordinary selected variant and eager capture position; policy does not move source evaluation into drain |

## Drain, refusal and publication

| Case | Situation | Required result |
|---|---|---|
| HDRAIN-01 | Source() returns a lazy sequence with observable iteration | Evaluate Source before later source captures; start its iteration only at the spread's drain position after acquisition |
| HDRAIN-02 | An earlier element/segment refuses before a factory-source spread is reached | Source's capture effects remain, but acquire no iterator and execute no steps for that unreached spread |
| HDRAIN-03 | Acceptance refuses on an item of a reached spread | Stop subsequent steps/items and later drain fragments; close live reached iteration state once |
| HDRAIN-04 | Reached spread adapter observes an effectful Current inside TryMoveNext and acceptance refuses that item | Observe the item under the selected iteration contract once; do not replay Current or lose cleanup of a consumed rejected item |
| HDRAIN-05 | An earlier segment refuses before formatting a later captured value | Later value capture effects occurred, but its formatter is not invoked |
| HDRAIN-06 | Writer has already accepted a prefix when a later operation refuses | Keep earlier output/effects; no rollback, replay or transactional stream guarantee |
| HDRAIN-07 | Eager captures all spread-source expressions, but drain refuses early | Eager does not promise full enumeration or downstream formatting effects |
| HDRAIN-08 | Builder-generated capacity/enablement operation has effects | Execute it after this site's captures under its exact admitted operation contract, not as a hidden early gate |
| HDRAIN-09 | Builder seeks a capacity hint by repeating source expressions or an undeclared enumeration pass | Reject unsupported protocol; no speculative replay/preflight pass |
| HDRAIN-10 | Element acceptance copies, borrows or consumes its captured value | Preserve the selected complete item mode and dependencies; syntax does not choose another ownership policy |
| HDRAIN-11 | Materialized collection accepts only a prefix and then refuses | Do not publish that prefix as successful complete collection construction |
| HDRAIN-12 | Completion returns true without its required live initialization result | Reject the contradictory operation contract; no fabricated default target or success-shaped result |
| HDRAIN-13 | Reached spread enumerates zero items | Preserve source validity/backing and actual iterator acquisition/close; emptiness does not erase dependencies |
| HDRAIN-14 | Value formatter forwards nested components | Use the same checked typed writer path without mandatory boxing, ToString or temporary text |
| HDRAIN-15 | Already typed value has a builder registration | Do not reinterpret it as source interpolation/collection syntax |
| HDRAIN-16 | Required constant construction reaches a failing capture or unsupported executed operation | Diagnose under ordinary constant admission/resources; no runtime fallback or incremental path skipping |

## Availability, ownership and cleanup

| Case | Situation | Required result |
|---|---|---|
| HLIFE-01 | Named unique value is explicitly moved into a capture and consumer later refuses | Source remains vacant; the current owner cleans/transfers the captured value exactly once |
| HLIFE-02 | A later capture throws after an earlier unique capture was moved | No ownership rollback; unwind live staged captures once |
| HLIFE-03 | Named unique value needs transfer into capture but no move is written | Reject missing explicit transfer; eager construction is not copy authority |
| HLIFE-04 | Fresh capture holder contains a copied handle/view | Retain original backing/provenance; holder freshness is not fresh backing or NoAlias proof |
| HLIFE-05 | Acquisition would invalidate a captured owner/loan dependency | Reject the incompatible operation/lifetime plan; do not delay capture or manufacture authority to make it valid |
| HLIFE-06 | Capture holder retires while a materialized source-site backing temporary belongs to an enclosing supporting scope | Keep the ordinary backing-local lifetime; holder/drain completion does not imply its early retirement |
| HLIFE-07 | Drain throws with live builder, captures and iteration state | Follow ordinary unwinding/cleanup order once; no reexecution of source captures |
| HLIFE-08 | A capture fails before its value is fully initialized | Clean only its actually live partial/complete obligations under the type contract, never a fabricated complete value |
| HLIFE-09 | Authored finally/defer action faults during abandonment | Preserve its ordinary failure and causal-chaining behavior; generated owner cleanup remains nonthrowing/nonsuspending, with no transaction |
| HLIFE-10 | Successful publication transfers an owned result out of the builder | Retire remaining builder/capture obligations without destroying the transferred result again |
| HLIFE-11 | Returned view would depend on already retired capture-holder storage | Reject invalid backing; retain only a genuinely surviving admitted source relationship |
| HLIFE-12 | Async consumer retains eager captured loans after ordinary return | Require its explicit completion-bounded capture contract; eager evaluation alone grants no retention/suspension permission |

## Plain construction refusal

| Case | Situation | Required result |
|---|---|---|
| HREFUSE-01 | Plain construction's selected acquisition returns false | All source captures preceded acquisition; raise ordinary require failure and run applicable cleanup without inventing a builder/result |
| HREFUSE-02 | Element or text acceptance returns false in plain construction | Raise ordinary require failure immediately; do not call later drain operations or completion |
| HREFUSE-03 | Completion returns false with a vacant result | Raise ordinary require failure; abandon still-owned private state without publishing a prefix/default target |
| HREFUSE-04 | Selected operation throws rather than returning a Boolean | Propagate that exception; do not substitute false or a new requirement failure |
| HREFUSE-05 | Capture itself throws before acquisition | Preserve that failure and staged-owner cleanup; no builder refusal operation occurred |
| HREFUSE-06 | Registration tries to select a different failure solely for plain-construction Boolean refusal | No such registration role; plain construction uses the ordinary require failure contract |
| HREFUSE-07 | Explicit TryBuild/TryWrite invocation returns false | Preserve its declared false/output behavior; no implicit require failure from its name or use of a builder |
| HREFUSE-08 | Admitted call-side consumer is disabled or reports writer refusal | Preserve its own declared outcome, completed eager capture effects and already accepted output; plain-construction failure is not imposed on every consumer |
| HREFUSE-09 | A successful result is transferred into the plain construction destination | Publish it under the exact result relation once; remaining cleanup cannot destroy it through the former builder |
| HREFUSE-10 | Ordinary requirement failure is followed by a faulting authored finally/defer action | Apply the existing causal-chaining rule; no transaction or weakening of generated owner cleanup |

## Admission and evolution boundaries

| Case | Situation | Required result |
|---|---|---|
| HBOUND-01 | Type has Add/Append/Build spellings or role attributes without required nominal witnesses | No checked construction protocol from names or per-method role annotations |
| HBOUND-02 | Selected construction lacks its required checked operation/call-side contract | Diagnose before affected execution; do not run captures and hope a private implementation works |
| HBOUND-03 | User enum is named ConstructionEvaluation with Eager/Incremental cases | Ordinary user policy identity only; no Core construction-evaluation domain or deferred-operand authority |
| HBOUND-04 | User interface is named ITextContent or ITextContext | Ordinary nominal interface only; no recognized deferred content program, implicit implementation or call conversion |
| HBOUND-05 | Source uses identifiers eager, incremental or streaming | No new keyword or handler evaluation selector from these spellings |
| HBOUND-06 | Independent retained construction program lacks its required eager rule/schedule | Reject unsupported required semantics; optional attribute erasure cannot change source effects |
| HBOUND-07 | Replacement changes eager captures to conditional or drain-time evaluation | Incompatible observable construction contract, even when one execution yields equal output |
| HBOUND-08 | Optimization preserves final elements but changes capture/acquisition order, alias reads, refusal or cleanup | Equality of final output alone is insufficient; require permitted or proved-unobservable transformation |
