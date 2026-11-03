# Resource ownership at NativeOutput cells

**Status: unapproved; provisionally actionable only under the user's
authorized missing-contract workflow.** This is a separate resource and
typed-interface boundary for the
[NativeOutput cell contract](aggregate-native-outputs.md). It does not
redefine cell eligibility, reservation, result correlation, dependency
sources, standard records or private persistence.

## Decision and scope

NativeOutput publishes a complete value T; it does not, by itself, transfer
ownership of a resource represented by T. An integer handle initializes an
integer. A pointer initializes a pointer cell, not its pointee. Copying either
value does not acquire a new resource right, lease, release obligation or
typed interface identity.

A foreign acquisition or borrow therefore needs an additional authored
unsafe provider contract. A checked Terrum owner or typed interface may be
published only by an ordinary authored wrapper whose separate contract
establishes the resource's origin, validity, backing/retention, unique
responsibility, cleanup and failure behavior. This proposal assigns no
ownership flag, automatic adoption, synthesized release, recognized
constructor name or new source spelling. No COM `AddRef`/`Release` operation
is inferred from the output pointer's value.

The distinction is substantive: adding scalar/pointer T to the cell
eligibility domain does not make resource acquisition a cell-layout case.
The [cell owner](aggregate-native-outputs.md#candidate-cell-eligibility-and-ownership-boundary)
owns pointer/scalar representation and publication; this document owns only
the additional resource lifecycle recommendation. The exact foreign
interface/method identity and provider-binding decisions remain in their
[separate owners](native-output-foreign-interface-method-identity.md) and
[provider-binding proposal](native-output-direct-import-provider-binding.md).

## Bounded provider and wrapper obligations

The initial proposal-only resource scenario requires all of the following:

1. On the declared normal-success result, the provider establishes both a
   valid T and exactly the documented resource/reference responsibility. The
   caller becomes responsible at the provider's successful normal return,
   not only if a later owner constructor happens to succeed. Multiple
   acquired resources remain separately responsible until explicitly
   transferred or settled.
2. On the complementary normal result, the provider leaves no acquisition for
   this output that the caller must inspect or release. A failure-live
   diagnostic/error handle is not admitted by this success-only cell
   contract; an independently admitted adapter would need its own complete
   protocol.
3. On an admitted foreign exception after invocation begins, no acquisition
   is transferred through this output. The provider settles unpublished
   acquisitions. The cell itself retains the cell owner's indeterminate
   exceptional state; it is not treated as vacant.
4. No fallible operation may intervene between successful machine return and
   normal cell publication while an acquisition is hidden in unpublished
   bytes. The initial resource-wrapper slice therefore admits only crossings
   without fallible postconditions, consumed-owner phase cleanup or other
   fallible work in that interval. Selector classification and publication
   are non-failing bookkeeping.

This is not a general NoThrow promise on the native call, release function or
wrapper. A wider resource-aware contained adapter could settle an acquisition
before exceptional propagation, but its contract is not supplied here. If
the provider or publication path cannot meet these conditions, do not expose
checked owning acquisition through this bounded output scenario.

The authored wrapper establishes release context, provider/code dependencies
and exceptional cleanup structure before acquisition. It owns each raw
responsibility while validation/adoption can fail. A failed adoption leaves
responsibility with the wrapper unless that exact operation consumed or
settled it; successful adoption transfers responsibility once to the unique
owner. Multiple acquisitions must be settled in their actual required order,
preserving dependencies between them. An operation named `Adopt`, an owner
constructor, a cast or pointer equality proves no handoff by itself; each is
ordinary authored code with its own supported contract.

Retirement follows the existing complete-owner Cleanup and explicit
exceptional-finally rules. A raw Copy cell has no synthesized Cleanup, and
copying/dropping its bits is not compiler-enforced leak or double-release
checking. Release must use the exact compatible provider, allocator,
interface and required context. If release can fail, preserve its actual
remaining responsibility and reported failure; do not mark it settled,
blindly retry a possibly reused numeric handle, swallow an error or invent a
fail-fast/retry policy. The bounded qualification case assumes a provider
whose release completely consumes its obligation on normal return.

## Borrowed values and typed interfaces

A borrowed pointer/handle needs its actual backing, lease or guard and
retention endpoint. Do not adopt a borrow as a transferred reference or
release it by guessing. NativeOutput's no-retention guarantee applies to the
**output cell address**; it does not authorize retaining arbitrary input
addresses or extend the returned resource's lifetime.

Non-null pointer bits do not identify a foreign interface, establish its
vtable/slot contract or acquire a reference. Typed acquisition must fix the
requested interface identity in authored library code or validate the actual
identity under an independently admitted protocol before publishing a typed
owner. A caller-selected IID plus `void**`, equal slot, common release prefix
or cast is insufficient. Code pointers separately require an exact callable
signature/convention and live code/provider obligation; an address does not
establish them.

This document selects no universal adoption/release signature, COM API, IID,
slot or resource-type catalog. No `Transfer` clause or native-output-specific
ownership carrier is assigned. If the ordinary owner/dependency/cleanup
representations cannot express the wrapper's actual obligations, its affected
use remains unsupported; the cell contract does not erase those facts.

## Cross-layer ownership boundaries

| Layer | Separate obligation |
|---|---|
| Source and binding | The unsafe declaration/wrapper names the exact provider/resource promise. Existing provider identity and foreign-interface binding remain required; source names and matching pointer values do not select them. |
| Standard metadata and execution | Use the exact NativeOutput cell schema for value publication. It carries no resource ownership authorization. The current cell proposal's Features, predicate selectors, Body reservation and Dependencies do not imply resource transfer. |
| Private persistence and native containers | The private cell schema, provider-binding table and container versions remain owned by their separate proposals. No private revision, outer version, checksum or image validates a foreign ownership promise. |
| Consumer checks | Validate framing, selected identities, roles, types and supported contracts before use. Do not semantically re-prove a foreign provider or elevate raw bits into a safe owner. |
| Cleanup | Preserve actual owner/provider dependencies and settlement on every wrapper path. Do not synthesize Cleanup for raw Copy cells or assume cleanup runs during exceptional unwind. |

These are separate approval and support gates. A successful NativeOutput
cell implementation would not establish resource acquisition, checked
ownership, COM identity, or a release ABI.

## Required acceptance cases

These are future design/implementation requirements, not compiler or runtime
evidence from this documentation change.

| Case | Required outcome |
|---|---|
| Successful scalar or pointer cell carrying a foreign resource value | The provider's distinct unsafe promise establishes the acquisition/borrow contract; the cell's type or bits do not. |
| False result after no, partial or complete writes | No resource is adopted or released through the cell. The provider settles any unpublished acquisition; otherwise the provider violates the proposed contract. |
| Foreign exception after partial output writes | No resource responsibility transfers through the cell; provider settles acquisitions, while the cell state remains indeterminate. |
| Fallible post-return validation/cleanup before Publish | The bounded checked-acquisition wrapper rejects this crossing unless an independently designed adapter settles the acquisition. Do not move Publish ahead of required checks. |
| Wrapper validation or owner construction fails after acquisition | The wrapper settles each still-held responsibility exactly once; a successful adoption transfers it exactly once to a unique owner. |
| Several acquisitions with dependent cleanup | Preserve actual source/dependency order and settle every still-held acquisition on each failure path. |
| Borrowed pointer with ended descriptor slot but live backing, then ended backing | Preserve the actual lease/guard through surviving copies; never infer a borrow from slot bytes or release it as an owned resource. |
| Release fails or a numeric handle is reused | Preserve remaining responsibility and surface the release result; no silent settlement or blind retry. |
| Caller-chosen IID plus `void**`, wrong returned IID or equal slot number | Do not publish a typed interface owner without fixed or validated identity and independently admitted method/provider binding. |
| Function-pointer value returned in a cell, followed by contract erasure or provider unload | Preserve the separate signature, convention and code/provider lifetime gates; the cell does not authorize invocation. |
