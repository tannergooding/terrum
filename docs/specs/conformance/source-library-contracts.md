# Library inventory and scenario cases

These cases apply [library-contract-inventory/1](../libraries/adopted-inventory.md)
and the existing canonical contracts linked from it.
[Provider/consumer scenarios](../libraries/design-scenarios.md) supply the
declaration/profile assumptions; arbitrary same-named libraries do not.
The inventory adds no source syntax, API, capability, wire assignment or ABI.

Finite evidence can check the exact tables, declarations, admitted fixture
contracts and observable logical traces below. It does not establish full
source checking, parsing/formatting implementations, physical metadata,
native loading, scheduling or actual resource/error containment.
The final eight cases explicitly require those broader obligations.

## Inventory, identity and admission

| Case | Input or operation | Required outcome |
|---|---|---|
| LAPI-01 | The selected/deferred inventory tables and canonical links | Forty contiguous LCI rows and fourteen contiguous LCO rows with explicit status/owner; 38 Selected and two Family rows, with no candidate silently promoted |
| LAPI-02 | Exact construction-role declarations | Five public interfaces and seven explicitly public requirements; modes, constraints, factory/completion inheritance and complete item/result terms match their owner |
| LAPI-03 | Exact iteration declarations | Twelve distinct public interfaces and eighteen direct public requirements; six cursor roles/factories retain synchronous acquisition, access, descriptor and completion-phase distinctions |
| LAPI-04 | Selected standard failure payloads | Exactly ten distinct zero-data Exception/Copy payload identities with public zero-parameter construction; CapturedException is not an eleventh payload |
| LAPI-05 | Core Task/CapturedException selection and same-spelled substitutes | Exact selected owner identities and operations only; no Copy task, general awaiter, ordinary capture constructor, shared owner or universal Exception base |
| LAPI-06 | Numeric groups and selected direct members | Ten exact public interfaces with 26 public static direct requirements; INumericSelection retains Min/Max/Clamp together and inheritance does not merge member origins |
| LAPI-07 | SizeOf/AlignmentOf reference signatures | Exactly one Sized type formal, no ordinary values/receiver, by-value nuint result; size/layout facts do not create Copy, foreign equivalence or a T value |
| LAPI-08 | IsPureEval/IsConstEval query contract | Static readonly bool observations of the immutable selected activation form; constant implies pure, but no mode mutation or namespace-spelled substitute |
| LAPI-09 | Unrelated member with a selected role's name and shape | Remains an ordinary member; no compiler role or primitive authority from spelling/signature/body |
| LAPI-10 | Transparent alias versus equal-layout nominal replacement | Alias retains an established exact declaration; another nominal identity cannot substitute |
| LAPI-11 | A normal package claims a compiler-owned role | Reject the unauthorized registration; a Core dependency or same namespace does not change ownership |
| LAPI-12 | A literal/builder owner registers its own versus a foreign target | Owned exact target/pattern may register; foreign target claim rejects and cannot override by import order |
| LAPI-13 | Repeated import of one record versus distinct records claiming one key | Same identity deduplicates; distinct competing claims reject even with equal bodies/members |
| LAPI-14 | Each construction requirement loses public visibility or changes one mode | Reject the claimed selected declaration/contract; a matching name or specialization cannot repair it |
| LAPI-15 | Correct role/signature with a missing or incompatible rule | Decline before affected use; table membership alone is not executable support |
| LAPI-16 | Async/lending contract without its exact publication phase | Decline; Task<bool> or ref-valued item shape cannot register the missing relationship |
| LAPI-17 | Boolean init-output role without true/live false/vacant failure/vacant relation | Decline; success-shaped default outputs are not a substitute |
| LAPI-18 | A view/operation loses an actual backing/root/guard/load dependency | Decline the claimed contract; descriptor bits, empty extent or alias normalization do not remove it |
| LAPI-19 | Ownership-bearing role lacks residual/partial/exceptional retirement | Decline; Copy payload, ordinary Close spelling or consuming parameter does not settle responsibility |
| LAPI-20 | Logical role is present but required actual storage/transport is absent | Decline affected formation/execution; no hidden heap, blocking or different carrier fallback |
| LAPI-21 | Independently consumed role lacks a required declaration/value/operation/handler/cause codec | Decline affected independent use, including under producer trust; no invented wire identity from inventory ID |
| LAPI-22 | Supplied capacity fits/exceeds its supported envelope or is called a portable minimum | Admit only the exact supported request; exhaustion does not invalidate otherwise valid representation, and fixture/format maxima do not become portable minima |
| LAPI-23 | Two requirements have equal spelling/signature but distinct nominal origins | Retain both identities; grouped inventory rows and witness paths cannot merge or choose by private body |
| LAPI-24 | A Direction/Deferred candidate is supplied with complete-looking signatures | No selected standard role from that candidate; an independently checked ordinary API/explicit adapter remains possible |
| LAPI-25 | Copy/Sized/etc. constraint or a sketch-only semantic schema | Existing checked property facts remain exact; no runtime attribute constructor or new recognized schema inferred from a name |

## Provider/consumer traces

| Case | Input or operation | Required outcome |
|---|---|---|
| LAPI-26 | Whole-input registered parser succeeds/refuses or accepts only a prefix | True publishes one complete target; false leaves vacancy; prefix acceptance does not satisfy the whole-input promise |
| LAPI-27 | The same parser has an actual unexpected failure | Preserve failure and required cleanup; do not return normal false or a live default target |
| LAPI-28 | Eager interpolated content followed by a disabled/refusing writer | Capture each hole once before drain; writer refusal cannot discard those effects or adopt deferred policy |
| LAPI-29 | Typed formatter writes a prefix then refuses; same value occurs in another hole | At most one invocation per reached hole operation; earlier accepted output/effects remain; no automatic replay or cross-hole cached result |
| LAPI-30 | Collection factory/acceptance refuses before or during spread drain | All reached element/source expressions were captured before acquisition; an unreached spread is not enumerated, reached traversal closes once after item retirement |
| LAPI-31 | By-value cursor publishes absent optional or unit versus false | True publishes a live item even with no payload/presence; only false means no item, and no universal Copy bound is invented |
| LAPI-32 | Writable lending offered where readonly lending is required, or init storage is attenuated | No implicit witness/inheritance/covariant output storage; select a checked explicit adapter with retained authority/backing |
| LAPI-33 | Backing-only forwarder ends its descriptor or its actual owner | Descriptor ending may preserve the view; owner/guard/load ending cannot, and broad generic witness bounds are not silently narrowed |
| LAPI-34 | Empty/end view or hypothetical data-and-shape view loses one source | All actual dependencies remain, including independently stored shape metadata; zero length does not grant escape |
| LAPI-35 | Unique native-resource input is moved into a consuming call which fails | Source stays vacant; the actual responsible holder retires/transfers exactly once with complete versus partial state respected |
| LAPI-36 | Named affine Task is consumed by successful or failed await | Source becomes vacant in both cases and exact completion responsibility is settled/transferred; ordinary copy/drop/cancellation does not suffice |
| LAPI-37 | IsCompleted is true while payload/output observation is not established | Terminal observation alone supplies no exact init-output publication or responsibility discharge |
| LAPI-38 | Supplied contained-adapter publication/packet-responsibility fixture | Normal result and failure record are mutually exclusive; unique packet settlement cannot publish empty/default normal success; actual mapping/containment remains required by LAPI-45 |
| LAPI-39 | Generic Clamp with NaN value and invalid bounds, or a missing Copy/selection requirement | Bounds fail first under the exact Core category; missing evidence diagnoses and comparison is not representation totalOrder |
| LAPI-40 | Selected CLI reports normal negative program status or an unknown protocol | Negative signed int32 return remains data with completed outcome when other obligations succeed; unknown versions reject, never legacy/empty success fallback |

## Obligations beyond finite inventory evidence

| Case | Input or operation | Required outcome |
|---|---|---|
| LAPI-41 | Actual source checking of every scenario/provider declaration and generic forwarding path | Check real lookup, visibility, constraints, complete modes, phases and ownership; a table or supplied logical trace is not a compiler pass |
| LAPI-42 | Full literal parser, typed formatter, encoding/storage and concrete builder implementations | Verify whole-input semantics, numerical/text validity, actual once-only effects, progress and cleanup without hidden allocation/replay |
| LAPI-43 | Actual native resource, owner/lease/guard/root and load-lifetime behavior | Preserve complete and partial retirement, actual access and all dependencies, including resource exhaustion and exceptional paths |
| LAPI-44 | Actual async execution, resumption, completion-output publication and async close | Supply real scheduling, reservation, observation, cleanup and affinity guarantees; a finite owner model is not that runtime |
| LAPI-45 | Actual paired foreign failure/callback containment and mapping | Verify ABI/unwind/domain/payload/cause ownership with real native adapters and failures; signature/fixture acceptance is insufficient |
| LAPI-46 | Actual packager/filesystem/loader identity and destination collisions | Enforce case-sensitive semantic identity and actual target collision/alias relations before publication/launch; finite path equivalence is not universal filesystem conformance |
| LAPI-47 | Complete independently consumed physical artifact for each claimed selected surface | Supply and admit its full schema/rule/feature/declaration/value/operation/handler/cause closure; unchanged reserved capacity or logical roles cannot fill unassigned codecs |
| LAPI-48 | Future broader API/profile adoption and full FDG usability review | Select exact declarations/contracts and exercise concrete provider/consumer paths; these scenarios do not certify every candidate or waive deliberate-departure rationale |
