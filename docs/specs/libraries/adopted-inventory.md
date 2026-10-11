# Adopted library and compiler-protocol inventory

This chapter owns `library-contract-inventory/1`: the inventory and status
of library-facing contracts selected by the canonical specifications. It
does not duplicate their semantic rules or approve adjacent proposals.
[Library layering](../libraries.md#dependency-layers-and-package-boundaries)
and [conformance](../overview/conformance.md) determine applicability.
The [design scenarios](design-scenarios.md) exercise actual selected paths,
not the breadth of an implementation or an API sketch.

## 1. Status and identity

| Status | Meaning |
|---|---|
| Selected | The cited owner selects the logical role, declaration or schema and its behavior; required support must still be admitted |
| Family | The cited owner selects family/value relationships, but this is not a complete exported member or representation catalog |
| Direction | Constraints on a possible design are accepted; the named candidate declarations are not selected APIs |
| Deferred | The additional contract is not selected, even if examples or historical tooling name it |

Each inventory row is a contract family, not a public type, export, opcode,
feature number or minimum supported package. A grouped row does not merge
its distinct interfaces, members, formals or nominal identities.
The linked canonical declaration remains authoritative for exact signatures,
constraints, phases, outputs, access, effects, dependencies and refusal.
The selected Core requirements are public primary declarations. Concrete signature
examples still require bodies or separately admitted intrinsic/foreign
implementations; this inventory does not make bodyless concrete source legal.

Recognition uses the exact registered declaration/schema and checked role,
never namespace spelling, naming conventions, member resemblance, private
body inspection or a dependency-tier label. Transparent aliases preserve an
established identity; unrelated equal-layout or same-named types do not.
Target-owned literal/builder records and ordinary nominal witnesses retain
their own authorities. Compiler-owned roles cannot be registered for an
unrelated declaration by a package that merely references Core.

`Core` below denotes the designated registration authority, not a public
namespace or fixed package binary identity. Full source identities explicitly
selected by an owner, including `Terrum.Threading.Tasks.Task` and the failure
payloads, are retained. A short role name is not permission to guess a missing
module/namespace identity, metadata selector, attribute constructor or
physical codec. Those fields must be supplied compatibly by the selected
profile before affected source-independent use.

## 2. Selected declarations and compiler roles

| ID | Status | Role or family | Canonical shape and owner |
|---|---|---|---|
| LCI-01 | Selected | Reserved scalar bindings | [26 mandatory complete identities](../overview/conformance.md#mandatory-scalar-catalog), including `void`; ordinary packages cannot define replacements |
| LCI-02 | Selected | Scalar operators and conversions | [Exact data/count signatures and evaluation](../language/primitive-operators.md), [selected conversions](../conversions.md); no inferred promotion or new conversion pair |
| LCI-03 | Selected | Numeric Abs/Min/Max/Clamp | [Same-type Abs](../language/absolute-value.md) and [selection](../language/numeric-selection.md) on the 20 numeric scalars, including Clamp's explicit ordered-bound requirement |
| LCI-04 | Selected | Ten numeric interfaces | [Exact public static requirements, self/Copy constraints and primitive instances](../language/numeric-capabilities.md); coherent groups, with Min/Max/Clamp together and optional IScalarNumber composition |
| LCI-05 | Selected | Value-layout queries | [Core RuntimeHelpers.SizeOf/AlignmentOf](../language/registered-queries.md#value-layout-queries): `public static nuint` result, one `[Sized]` type formal, no value inputs |
| LCI-06 | Selected | Evaluation-mode queries | [Readonly Boolean IsPureEval/IsConstEval](../language/published-predicates-and-effects.md#evaluation-modes) on the selected activation; not ambient mutable mode switches |
| LCI-07 | Selected | Literal and LiteralKind schemas | [Target-owned four-kind registration](../language/literal-construction.md#checked-literal-record); accessible static `bool` parser with readonly `TextSpan<void>` input and exact `init ref<Target>` output |
| LCI-08 | Selected | TextBuilder/CollectionBuilder schemas | [Primary target-owned registration](../language/construction-handlers.md#target-owned-builder-schemas), one direct `typeof(Type)` recipe under owning target formals |
| LCI-09 | Selected | TextTarget schema | [No-argument nonrepeatable parameter schema](../language/construction-handlers.md#texttarget-parameter-schema), one eligible by-value generic content formal with IFormattable evidence |
| LCI-10 | Selected | `IBuilderFactory<this TBuilder>` | [Public static TryCreate](../language/construction-handlers.md#parameterless-builder-acquisition), no runtime inputs, one `init ref<TBuilder>`, Boolean live/vacant relation |
| LCI-11 | Selected | `IBuilder<this TBuilder, TResult>` | [Public static TryBuild](../language/construction-handlers.md#consuming-builder-completion), by-value builder transfer and exact `init ref<TResult>`, Boolean live/vacant relation |
| LCI-12 | Selected | IFormattable | [Public TryFormat<TWriter>](../language/construction-handlers.md#standard-text-operations), writable writer reference, readonly erased text format, Boolean result; TWriter requires ITextWriter |
| LCI-13 | Selected | ITextWriter | [Public mut TryWrite/TryWriteScalar/TryWriteFormatted](../language/construction-handlers.md#standard-text-operations); validated text, rune or typed readonly value reference with int32 alignment and text format |
| LCI-14 | Selected | `ICollectionBuilder<this TBuilder, TItem, TResult>` | [Public mut TryAdd(TItem)](../language/construction-handlers.md#collection-acceptance-and-spreads), inheriting exact factory/completion requirements; one complete selected item/result relationship |
| LCI-15 | Selected | `ICursor<T>` / `ISequence<T, TCursor>` | [Public mut TryMoveNext(init ref<T>)/Close and readonly GetEnumerator](../language/iteration.md#nominal-protocol-identities), with exact TCursor witness |
| LCI-16 | Selected | `IAsyncCursor<T>` / `IAsyncSequence<T, TCursor>` | [Task<bool> advance, Task<void> close, synchronous readonly GetAsyncEnumerator](../language/async-iteration.md#nominal-protocol-identities); inherited completion-output relation |
| LCI-17 | Selected | `ILendingCursor<T>` / `ILendingSequence<T, TCursor>` | [Readonly sync lending](../language/lending-iteration.md#nominal-protocol-identities): `init ref<ref<T>>`, mut advance/close, readonly factory |
| LCI-18 | Selected | `IWritableLendingCursor<T>` / `IWritableLendingSequence<T, TCursor>` | [Writable sync lending](../language/lending-iteration.md#nominal-protocol-identities): `init ref<write ref<T>>`, mut advance/close and mut factory |
| LCI-19 | Selected | `IAsyncLendingCursor<T>` / `IAsyncLendingSequence<T, TCursor>` | [Readonly async lending](../language/lending-iteration.md#nominal-protocol-identities): the readonly descriptor output reserved through `Task<bool>` completion; synchronous readonly factory and `Task<void>` close |
| LCI-20 | Selected | `IAsyncWritableLendingCursor<T>` / `IAsyncWritableLendingSequence<T, TCursor>` | [Writable async lending](../language/lending-iteration.md#nominal-protocol-identities): the writable descriptor output reserved through completion; synchronous mut factory and `Task<void>` close |
| LCI-21 | Selected | IteratorCursor/Iterator schemas | [No-argument primary schemas](../language/generated-producers.md#owned-named-cursor-registration); same-module unique named shell and sole callable definition, selecting exactly one of six direct roles |
| LCI-22 | Selected | `Terrum.Threading.Tasks.Task<T = void>` | [Affine completion owner and readonly bool IsCompleted](../language/task-responsibility.md#core-identity-and-value-contract), with [compiler-owned consuming await](../language/task-responsibility.md#consuming-await); not a general awaiter/scheduler API |
| LCI-23 | Selected | DependsOn/Captures/ForwardsBacking schemas | [Whole-source declaration recipes and targets](../language/dependencies-and-views.md#source-schemas-and-targets), with exact result/output/holder/operation phase; refinement cannot acquire ownership or add a source |
| LCI-24 | Selected | Pure/ConstEval schemas | [No-argument primary callable/accessor contracts](../language/published-predicates-and-effects.md#checked-annotations-and-forms); author opt-in and actual checked form/program/value/resource support remain distinct |
| LCI-25 | Selected | NoAlias parameter schema | [Invocation-bound checked parameter contract](../language/published-predicates-and-effects.md), not a writable-access, lifetime, pure-form or async-completion grant |
| LCI-26 | Selected | Exception schema | [No-argument nominal capability/category opt-in](../language/local-failures.md#1-registered-exception-capability); no universal base type, payload layout or constructor evidence |
| LCI-27 | Selected | RequireException schema | [Primary-callable-owned typeof(E) recipe](../language/requirement-failures.md#3-callable-owned-defaults-and-the-core-payload), required ExceptionType argument; explicit require<E> takes priority |
| LCI-28 | Selected | Terrum.RequirementException | [Public nongeneric zero-data Exception/Copy struct and zero-parameter constructor](../language/requirement-failures.md#3-callable-owned-defaults-and-the-core-payload) for the runtime require fallback |
| LCI-29 | Selected | Terrum.OverflowException/DivideByZeroException/ArgumentOutOfRangeException | [Three distinct public zero-data Exception/Copy structs](../language/scalar-failures.md#1-exact-core-payloads), each with one public zero-parameter constructor and exact scalar mappings |
| LCI-30 | Selected | Terrum.InvalidBoundsException | [Reusable public zero-data Exception/Copy struct](../language/numeric-selection.md#4-reusable-invalidboundsexception), with one public zero-parameter constructor; Clamp selects it explicitly |
| LCI-31 | Selected | Five standard runtime payloads | [Terrum.PostconditionException/IndexOutOfRangeException/OutOfMemoryException/InitializationCycleException/InitializationFailedException](../runtime/standard-failures.md#1-exact-core-identities-and-construction), each a public zero-data Exception/Copy struct with one public zero-parameter constructor |
| LCI-32 | Selected | Terrum.CapturedException | [Public opaque nongeneric unique struct and public static Capture()](../language/captured-exceptions.md#1-exact-core-identity-and-public-surface), zero supplied inputs and checked lexical handler source; consuming captured propagation is compiler-owned |
| LCI-33 | Selected | Cleanup retirement schema | [No-argument schema selecting a synchronous parameterless readonly void instance operation](../language/ownership-and-cleanup.md#complete-value-cleanup-and-transitive-field-retirement), with checked NoThrow/nonsuspending retirement and authored visibility; no name-only role or mandatory IDisposable interface |
| LCI-34 | Selected | ThreadTransferable/ThreadShareable schemas | [Explicit primary nominal opt-in and capability-owned local conditions](../runtime/sharing-and-handoff.md#explicit-source-opt-in-and-conditional-contracts); eligibility is not current handoff/share authority |
| LCI-35 | Selected | Windows x64 Layout/Blittable/LayoutKind schemas | [Exact selected target schemas](../targets/windows-x64-data-layout.md#4-registered-source-layout-schemas), arguments, placements, enum cases and limits; not universal source layout or view/task ABI |
| LCI-36 | Family | `Span<T>`, `TextSpan<TCodeUnit = void>`, contiguous projection | [Qualified view/text families](../libraries.md#views-shapes-and-tensor-primitives) and [backing contracts](../language/dependencies-and-views.md); no complete constructor/indexer/slicing/descriptor export catalog follows |
| LCI-37 | Family | StaticInitializer/LazyInitialization schemas | [Group trigger and separate lazy-unit contracts](../grammar-spec.md#static-initialization-and-load-scoped-storage); exact required declaration roles remain distinct from an unselected public recovery API |
| LCI-38 | Selected | Native package identity contract | [Exact case-sensitive ASCII identity](../native.md), actual packager syntax/capacity and independent target filename-collision rejection; not a Core type or portable name-length minimum |
| LCI-39 | Selected | terrum.cli/1 tooling protocol | [Versioned envelope, nine command identifiers, result/execution/output/supervision contracts](../cli-protocol.md); no public compiler library or additional command catalog follows |
| LCI-40 | Selected | Contained foreign failure bridge | [Paired adapters, affine domains, mapping and callback containment](../runtime/foreign-failure-bridge.md), with [selected Windows support](../targets/windows-x64-failure-bridge.md); not a universal exported packet or managed exception ABI |

The twelve iteration interfaces retain six different cursor requirements and
six matching factories. Writable lending does not inherit readonly lending,
and a reference-valued by-value item is not lending. Source selection must
retain the complete witness, item, cursor, output mode and phase.
Task shape alone supplies neither an async output relation nor a source
annotation for registering another one.

The selected ten zero-data failure identities are RequirementException,
the three scalar payloads, InvalidBoundsException and the five standard
runtime payloads. Each ordinary payload value is distinct from its affine
event/cause responsibility. CapturedException is an additional opaque owner,
not an eleventh Copy payload or an ordinary Exception category.

Source type properties such as Copy, Sized, ManagedStorable and TextCodeUnit
retain their [checked complete-type contracts](../language/types-and-normalization.md)
and [type-family rules](../type-families.md). Their use as constraints does
not select runtime attribute constructors, a general property-query catalog
or an arbitrary same-named user capability. Reserved source forms, policies,
patterns and accessors are grammar contracts, not additional API declarations.

## 3. Direction and deferred surfaces

These rows explain the remaining inventory; they do not supply missing
declarations, witnesses, registrations or executable fallback paths.

| ID | Status | Candidate surface | Boundary |
|---|---|---|---|
| LCO-01 | Direction | Allocators, Buffer/Vector, arenas, pools, leases and owning text | Explicit allocation/retirement and nested backing obligations are required; exact APIs and profiles remain [candidates](../../proposals/core-library-reference.md) |
| LCO-02 | Direction | List/LinkedList/maps/sets/queues and write-access guards | Accepted mutation/initialized-slot/guard constraints do not adopt the [managed inventories](../../../library/collections.api.terrum), builders or lending witnesses |
| LCO-03 | Direction | SpanAlgorithms and callback interfaces | [Algorithm direction](../libraries.md#collections-algorithms-and-application-services) constrains caller destination, access/effects, once-only callbacks and overlap; no complete standard algorithm catalog |
| LCO-04 | Direction | Runtime IParsable/TryParse catalog | A [registered literal adapter](../language/literal-construction.md#signature-and-eligibility) has an exact selected signature; that does not adopt a general runtime parsing interface or primitive parser overloads |
| LCO-05 | Direction | Terminal, streams, files, network and serialization | [Application-service direction](../libraries.md#collections-algorithms-and-application-services) uses selected typed formatting, not adopted destinations, buffering/backpressure, cancellation or hosting APIs |
| LCO-06 | Direction | Fixed/scalable SIMD, masks, TensorShape/TensorSpan and TensorPrimitives | [Element/shape/alias/numerical constraints](../libraries.md#views-shapes-and-tensor-primitives) are direction, not a complete vector/mask family, storage ABI or operation set |
| LCO-07 | Direction | Key comparer/hash and reduction APIs | [Library direction](../libraries.md) requires paired equality/hash domains and explicit strict/relaxed reductions; scalar operators and selection do not register these APIs |
| LCO-08 | Deferred | Reusable/shared/cold tasks, awaiters, observers, schedulers, join/supervisor and cancellation APIs | The [selected affine owner/await contract](../language/task-responsibility.md) adds none of these public facilities or executor spellings |
| LCO-09 | Deferred | General query/reflection/capability catalog | Only the [selected query subjects](../language/registered-queries.md#selected-identities-and-authority) are admitted; schema-specific nameof recipes are not a general string-query API |
| LCO-10 | Deferred | Incremental/deferred content and construction-evaluation policies | [Eager construction](../language/construction-handlers.md#deferred-evaluation-is-not-admitted) is selected; no eager/streaming/lazy policy cases, deferred callback or resumable formatter |
| LCO-11 | Deferred | Additional CLI inspection/resolver/import commands | A legacy command/schema inventory is informative; each additional command requires the [selected envelope](../cli-protocol.md) and its own published contract |
| LCO-12 | Direction | Concrete SpanTextWriter/Text storage and projection ABI | The [nominal writer role](../language/construction-handlers.md#standard-text-operations) and immutable text obligations are selected, not every writer's refusal atomicity, string facade or view representation |
| LCO-13 | Deferred | ViewOf/MatchedAccess and other sketch-only semantic schemas | [API sketches](../../../library/) cannot create recognized compiler semantics; existing source/dependency/access contracts do not register these candidates |
| LCO-14 | Deferred | Portable numerical capacity minima | [Resource accounting](../representation/resource-accounting.md) requires explicit supported envelopes; format maxima and finite fixture limits are not portable minima |

Unselected declarations can be authored as ordinary user APIs if their
operations are otherwise admitted. They cannot acquire compiler roles,
primitive authority or imported compatibility guarantees from their names.
An explicit adapter may implement a selected protocol without adopting its
underlying candidate API as part of Core.

## 4. Provider and consumer admission

Every claimed selected surface retains these independently required fields:

| Field | Provider/consumer obligation |
|---|---|
| Identity and authority | Exact module/type/member/schema, owning declaration/formal ordinals and role registration; no spelling-derived substitute |
| Public contract | Complete normalized signature, constraints, witness/member origin, effects, access and compatible defaults |
| Phase and state | Invocation/creation/completion/close phase, true/false/failure publication, reservations and observed output versions |
| Responsibility | Input/result transfer, partial/residual cleanup, event/cause or completion ownership, and every-exit settlement |
| Backing and current authority | Every actual source/owner/root/lease/guard/load/affinity dependency and the admitted access/sharing protocol |
| Execution and representation | Actual source/runtime/target programs, storage and transport, required pure/constant recipes and value codecs where used |
| Physical admission | Selected schema/rule/feature identities and complete declaration/value/operation/handler/cause encodings before independent use |
| Supported envelope | Explicit actual capacity/work/state support and the operation's declared resource-failure behavior |

A missing required field diagnoses or declines before affected use, even
under trusted-producer admission. Successful lookup, type checking or a
logical role table does not establish all later layers.
A profile must say which surfaces it supports and supply their transitive
closure; an unspecified surface is not silently implemented.
No ordinary-path hidden heap, collector, scheduler, encoding or OS substrate
is introduced to complete an optional feature.

A library provider preserves the complete selected contracts when composing
roles. A consumer cannot erase them through boxing, interface dispatch,
generic forwarding, owner movement, output omission, alias normalization or
replacement. Public constraints do not inherit stronger facts from a private
body; a published checked refinement is needed where the protocol permits it.

Independent interchange remains subject to the [selected physical registry](../representation/registry.md)
and [requirements/admission closure](../representation/requirements-and-admission.md).
This inventory assigns no new Feature ID, Attr tag, TypeSpec, intrinsic ID,
opcode or ABI. A future physical extension must select those records and
capacity coherently rather than treating a row number here as wire identity.
