# Storage backing and GC conformance

These cases specify the accepted backing, root, pin-reborrow and
backing-specific Cleanup contracts in
[language](../language.md),
[complete types](../language/types-and-normalization.md),
[references](../language/references.md),
[lifetimes](../lifetimes.md),
[conversions](../conversions.md) and
[ownership and cleanup](../language/ownership-and-cleanup.md). They are
specification cases, not a claim that the current compiler or native profile
implements the forms. Private managed-node/class prototypes do not establish
the normal-versus-GC type distinction or standard TRRM support; see
[CG-027](../../implementation/conformance-gaps.md#cg-027).

Assume `C` is an eligible class, `T` an eligible boxed payload, `S` an inline
value, and all named places/members are otherwise valid unless the case says
otherwise.

## Backing identity and construction

| ID | Scenario | Required outcome |
|---|---|---|
| SGC-01 | `new C(arguments)` without a GC target | Allocate stable, non-moving normal backing with shared-retaining handles |
| SGC-02 | `gc new C(arguments)` | Allocate the distinct GC-backed class form; backing may move |
| SGC-03 | `gc C value = new C(arguments)` | Use the expected complete type to select GC backing |
| SGC-04 | `C normal = gcValue` | Reject implicit conversion from `gc C` to normal `C` |
| SGC-05 | `C normal = C(gcValue)` | Explicitly pin the whole GC object and produce stable normal `C` |
| SGC-06 | Copy an eligible normal or GC class/box handle | Retain the same backing/root; do not copy the payload or duplicate object-level Cleanup |
| SGC-07 | A normal class/box graph contains a strong cycle | Do not collect it; normal shared-hold retirement does not provide cycle collection |
| SGC-08 | `gc box<T>` versus `box<T>` | Preserve distinct complete backing identities; do not normalize between them |
| SGC-09 | `box<gc C>` versus `gc box<C>` | Preserve distinct types: normal box of a GC handle versus GC-backed box of `C` |
| SGC-10 | `box<T>(value)` is contextually typed as `gc box<T>` | Select GC backing from the explicit expected complete type |
| SGC-11 | `gc box<T>(value)` has no expected type | Select GC backing explicitly |
| SGC-12 | Boxing is omitted and no declared conversion applies | Reject; no implicit boxing, allocation or transfer is inserted |
| SGC-13 | `gc int32`, `gc S`, `gc ptr<T>`, `gc own<T>` or `gc ref<init T>` | Reject; `gc` applies only to class handles, boxes and initialized exact-place roots |
| SGC-14 | `gc C?` or `gc ref<T>?` | Preserve optional presence around the full GC-backed type |

## Roots, stable reborrows and raw pointers

| ID | Scenario | Required outcome |
|---|---|---|
| SGC-15 | `gc ref place` names one initialized addressable place in GC-backed storage | Create an independent root for that exact place |
| SGC-16 | `ref place` has expected type `gc ref<T>` | Permit the same exact-place root formation under target typing |
| SGC-17 | `gc ref<T>` is used to access a sibling field or adjacent place | Reject; the root grants no safe sibling-place access |
| SGC-18 | `gc ref<T>` is used as an ordinary stable `ref<T>` or raw address | Reject direct use where stability is required; a root may move |
| SGC-19 | `ref *root` where `root` is `gc ref<T>` | Pin that exact referent and produce bounded stable `ref<T>` |
| SGC-20 | Copy a stable reference produced by `ref *root` | Preserve the same pin/backing dependency through every live copy |
| SGC-21 | Ordinary `ref<T>` or a normal `C` is converted implicitly to `gc ref<T>` or `gc C` | Reject; no implicit root or backing-policy conversion |
| SGC-22 | `gc ref<T>` or `ref<T>` is supplied directly to `ptr<T>` | Reject; no implicit checked-reference-to-pointer edge |
| SGC-23 | `unsafe(&*stableRef)` | Explicitly extract a non-owning, non-rooting pointer under unsafe authorization |
| SGC-24 | A derived pointer outlives the pin/root/backing that makes its address valid | Reject; raw pointers do not retain or extend those dependencies |
| SGC-25 | A statically apparent dangling pointer is retained as inert data in explicit unsafe code | Permit only under the selected suppressible warning; this does not validate any later dereference |
| SGC-26 | That dangling pointer is dereferenced later | Reject the known invalid access even inside unsafe code |
| SGC-27 | `ref *root` is applied to an ordinary reference rather than a GC root | Do not use it to acquire a GC pin; ordinary reborrow preserves only its existing backing contract |

## Backing-specific Cleanup

| ID | Scenario | Required outcome |
|---|---|---|
| SGC-28 | An eligible unique value, aliasable class object or boxed payload selects one `[Cleanup]` operation | Bind one exact object-level obligation; reject repeated or ambiguous selection |
| SGC-29 | Multiple normal class/box handles alias one Cleanup-bearing object | Run Cleanup once after the final strong hold and bounded dependency retire |
| SGC-30 | A normal shared cycle retains a Cleanup-bearing object | Do not collect the cycle or promise Cleanup while its strong holds remain |
| SGC-31 | Multiple GC handles alias one Cleanup-bearing object | Do not run per-handle Cleanup; retain one object-level obligation |
| SGC-32 | GC backing becomes unreachable | Run Cleanup once before reclamation, at nondeterministic collector-selected time |
| SGC-33 | GC Cleanup attempts to republish or otherwise resurrect its receiver | Reject resurrection |
| SGC-34 | GC Cleanup has thread-affine or non-transferable transitive obligations | Reject eligibility; no affinity-preserving cleanup scheduler is implied |
| SGC-35 | GC Cleanup is considered for a runtime thread | Treat its identity as unspecified; require thread-neutral/transferable obligations |
| SGC-36 | Cleanup is throwing, suspending, static, caller-unsafe or directly called by user code | Reject automatic Cleanup eligibility or the direct destruction path |
| SGC-37 | Cleanup has no authored operation | Apply only the declared backing-specific derived retirement; do not discover `Close`, `Dispose` or a finalizer by name |
| SGC-38 | A GC root keeps an object reachable but a separate native owner/guard has retired | Do not infer that the root preserves the independent resource or access contract |

## Separate-compilation and representation boundary

| ID | Scenario | Required outcome |
|---|---|---|
| SGC-39 | A producer serializes a GC type, retain, root or pin using an unassigned code/record | Reject producer output; do not infer an encoding from the retired pin qualifier bit |
| SGC-40 | A consumer receives a GC type whose complete schema/capability is unavailable | Reject affected use; do not reinterpret it as a normal type or trust producer claims |
| SGC-41 | Copies, fields, arguments, results or replacements cross a binding boundary | Preserve exact backing identity and all root/retain/pin/Cleanup dependencies |
| SGC-42 | A private managed-node/class prototype is used as proof of standard TRRM support | Reject the support claim; private profile evidence is not a standard codec or accepted source-form implementation |
| SGC-43 | A `ref<T>` names a normal class/box subplace after the source handle local ends | Keep that backing valid through the bounded reference dependency; do not extend a handle-slot, inline receiver, or owner/guard lifetime |
| SGC-44 | `ref *root` is applied to `gc ref<T>?` before presence testing and unwrapping | Reject; only the present `gc ref<T>` can be reborrowed, with no optional pinned-reference result |
| SGC-45 | GC allocation, root registration, or pin support encounters an admitted recoverable allocation failure | Follow the existing failure contract; publish no allocation/root/stable reference and clean actual partial acquisitions without a new failure category or optional result |
| SGC-46 | Backing support is released before its lexical boundary | Permit only when observationally equivalent; do not advance observable Cleanup or a published weak-observation boundary (no weak-reference API is selected) |
| SGC-47 | A strong cycle crosses normal shared and GC backing | Make no general mixed-cycle collection guarantee; do not infer that GC nodes alone reclaim the component |
