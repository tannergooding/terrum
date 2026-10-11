# Registered query conformance

These cases specify [registered-queries/1](../language/registered-queries.md),
not compiler support or a completed target-layout/metadata implementation.
Each fixture supplies its stated exact Core identities, complete type facts,
target profile and fixed-layout or coupling obligations.
Target-specific numerical fixtures do not define universal ABI tables.

## Identity, shape and authority

| ID | Scenario | Required outcome |
|---|---|---|
| QREG-01 | The exact admitted Core SizeOf<T>() is bound | Use its nuint value-layout intrinsic, not spelling-based member discovery |
| QREG-02 | The exact admitted Core AlignmentOf<T>() is bound | Use its nuint value-alignment intrinsic |
| QREG-03 | An ordinary alias/import resolves the same Core member | Preserve the intrinsic identity |
| QREG-04 | A user RuntimeHelpers.SizeOf<T>() has the same signature | Ordinary call with its own effects/constant eligibility, not a Core query |
| QREG-05 | Two imports retain the same compatible Core claim | Deduplicate; import order has no semantic effect |
| QREG-06 | Claims for one Core identity disagree on result or recipe | Reject the incompatible claims before use |
| QREG-07 | A different package attempts to attach query semantics | Reject unauthorized compiler/Core registration |
| QREG-08 | A source author writes a same-named Query attribute | No general query registration schema or intrinsic authority follows |
| QREG-09 | A claimed SizeOf member returns int32, int64 or a wrapper | Reject the mismatched exact nuint result |
| QREG-10 | A claimed query has a receiver or value argument | Reject the nonselected input shape |
| QREG-11 | A claimed query has zero/two type formals | Reject the nonselected generic arity |
| QREG-12 | A claimed query omits the checked Sized requirement | Reject the incompatible declared subject contract |
| QREG-13 | sizeof/alignof is used as if newly reserved syntax | No such syntax is selected; ordinary names retain their existing rules |
| QREG-14 | SizeOf or AlignmentOf is used as an ordinary identifier | No reserved/contextual token is introduced |
| QREG-15 | A concrete query signature ends in ; without an implementation | Reference notation does not admit a bodyless runnable concrete member |
| QREG-16 | Core is present but an unrelated candidate query is absent | Do not invent the query or imply every Core API belongs to the language floor |
| QREG-17 | A same-named ordinary helper is selected before required evaluation | Its actual body/opt-in is required; do not rebind to the intrinsic |

## Complete subjects and runtime evidence

| ID | Scenario | Required outcome |
|---|---|---|
| QSUB-01 | Alias I expands to int32 | Query the normalized int32 value representation |
| QSUB-02 | Two nominal structs have equal size/alignment | Equal numbers do not establish type identity or conversion |
| QSUB-03 | SizeOf<MyClass>() has a pointer-sized class-handle contract | Measure the handle, not fields/header of its allocated object |
| QSUB-04 | SizeOf<val<MyClass>>() has an admitted inline payload | Measure that selected inline representation |
| QSUB-05 | A compatible box<MyClass> normalizes to MyClass | Query the normalized handle once, not an extra box |
| QSUB-06 | SizeOf<ref<int32>>() has a three-word descriptor fixture | Measure all three descriptor words, not int32 or one guessed pointer |
| QSUB-07 | SizeOf<ptr<void>>() is admitted as an opaque pointer | Measure its pointer value; no unit payload address is created |
| QSUB-08 | SizeOf<own<S>>() is admitted for a non-Copy owner | Query the ownership handle without copying/transferring its allocation |
| QSUB-09 | A Span<int32> fixture has a two-word descriptor | Measure the descriptor independently of backing Length |
| QSUB-10 | An optional int32 has a separately admitted tag/padding layout | Query that optional layout, not int32 alone |
| QSUB-11 | A tuple has a supported complete element/layout contract | Query the whole tuple representation |
| QSUB-12 | A checked Transparent wrapper has an admitted carrier relationship | Observe carrier size/alignment without erasing nominal identity/validity |
| QSUB-13 | SizeOf<void>() is reached | Zero payload size; do not measure initialization bookkeeping |
| QSUB-14 | AlignmentOf<void>() has an explicit zero-data target rule | Return that rule's positive alignment, not a universal guessed value |
| QSUB-15 | A bare interface or unapplied generic constructor is supplied | Reject the ineligible complete subject |
| QSUB-16 | A genuinely sizeless scalable value is supplied | Reject; do not substitute a preferred fixed width or runtime vector length |
| QSUB-17 | A generic body declares where T : [Sized] | Runtime query can use the admitted actual T layout evidence |
| QSUB-18 | A generic body has no Sized guarantee but every later example uses int32 | Diagnose at the declaration; specialization cannot repair the source contract |
| QSUB-19 | SizeOf() has no explicit or ordinarily inferred type argument | Diagnose missing T; nuint return type supplies no inference evidence |
| QSUB-20 | A captured symbolic type is visible without available evidence | Diagnose; name visibility does not invent layout/type evidence |
| QSUB-21 | A valid T has no initialized source value or constant-value codec | The type-only runtime query does not construct or encode a T value |
| QSUB-22 | A readonly/write qualification or constructed argument distinguishes the complete form | Retain its complete identity/formation and selected representation; do not project inner storage |
| QSUB-23 | nameof(parameter) occurs in an admitted dependency schema | Retain its declaration selector; no parameter read or general string-query codec follows |

## Target values and execution

| ID | Scenario | Required outcome |
|---|---|---|
| QTGT-01 | SizeOf<int32>() on supported 32-/64-bit target fixtures | Four bytes regardless of evaluator host or natural alignment |
| QTGT-02 | SizeOf<nuint>() on 32-/64-bit target fixtures | Four/eight bytes respectively, not host pointer width |
| QTGT-03 | AlignmentOf<int64>() uses fixtures with 32-bit alignment 4 and 64-bit alignment 8 | Return four/eight while size remains eight |
| QTGT-04 | Sequential int8/int32 uses packing limit 1 | Query fixture size 5 and alignment 1 |
| QTGT-05 | The same fields use aggregate alignment 32 | Query fixture size 32 and alignment 32 |
| QTGT-06 | Packing limit 1 and aggregate alignment 32 are combined | Query fixture size 32 and alignment 32 |
| QTGT-07 | A procedure-call ABI pads/register-splits a value differently | Do not use call slots/classification as value storage size |
| QTGT-08 | A serialized codec or allocation header has a different byte count | Do not substitute it for the complete value extent |
| QTGT-09 | A target's unit alignment rule is absent | Diagnose incomplete layout; zero/one is not an unsupported fallback |
| QTGT-10 | Descriptor/optional/aggregate layout evidence is absent | Diagnose, without host layout or carrier guessing |
| QTGT-11 | A layout size equals the target nuint maximum | Accept the exact representable size when the fixture's other layout rules hold |
| QTGT-12 | Size exceeds nuint maximum or is negative | Reject before affected execution; no wrap/clamp/sentinel |
| QTGT-13 | Alignment is zero, negative or exceeds nuint maximum | Reject the invalid layout rather than return a successful value |
| QTGT-14 | The query reaches an admitted coherent type/target record | Pure total observation with no mutable/static/source-value state |
| QTGT-15 | The queried type has constructors or static initialization effects | Do not run them; observing admitted layout does not initialize the type |
| QTGT-16 | A very large T has an admitted scalar size result | Do not allocate/traverse T or charge its extent as evaluator live storage |
| QTGT-17 | Two authored query calls return the same number | Retain both logical call/read/return charges; no caching discount |

## Required evaluation and retained dependencies

| ID | Scenario | Required outcome |
|---|---|---|
| QCONST-01 | A reached query has exact fixed int32/target evidence and scalar codec | Form its ordinary nuint constant under the admitted recipe/envelope |
| QCONST-02 | A generic formal is only known Sized in shared code | Runtime support does not establish a definition-time numeric constant |
| QCONST-03 | A closed required query has exact actual-type evidence | Evaluate under ordinary forward binding and constant admission |
| QCONST-04 | Required target/layout evidence is missing or incompatible | Diagnose with no runtime/default/overload fallback |
| QCONST-05 | An otherwise well-formed runtime query is on an unselected constant path | Do not execute it or require unavailable constant layout evidence there |
| QCONST-06 | An unselected path has a malformed query/type or unsupported required rule | Reject ordinary structural/binding invalidity before interpretation |
| QCONST-07 | A reached query exhausts the selected work/depth/storage envelope | Diagnose exhaustion and publish no partial/default constant |
| QCONST-08 | A backend optionally folds an ordinary query | Preserve exact subject/target and every surviving layout dependency |
| QCONST-09 | An inline type publishes a compatible transitive fixed layout | A retained observation records that exact type/target commitment |
| QCONST-10 | A queried private Auto layout is in an explicitly admitted rebuild-coupled region | Retain the fact only with complete revision/dependency invalidation |
| QCONST-11 | The same Auto number is frozen as nuint across an independent replacement boundary | Reject absent fixed commitment or explicit coupling |
| QCONST-12 | The result's own scalar Blittable codec is present | It does not freeze the queried type's layout |
| QCONST-13 | Transparent matches a carrier but has no fixed replacement promise/coupling | Current carrier equality alone cannot authorize independent retention |
| QCONST-14 | A stable class-handle representation is queried | Retain the handle observation without freezing unmeasured private object payload |
| QCONST-15 | A derived allocation extent depends on an unfrozen queried layout | Propagate layout dependencies through arithmetic and its retained use |
| QCONST-16 | An actual coupled representation revision changes | Regenerate/rebind/invalidate affected dependent values/code before use |
| QCONST-17 | Source says AOT/JIT or has a rebuild comment but no coupling records | Those labels/intention do not establish admission |
| QCONST-18 | Value numbering compares queries with different complete types or targets | Do not merge by equal current numeric results alone |
| QCONST-19 | Reuse occurs under the same query/type/target/layout dependencies | Preserve the exact observation and ordinary legal transformation rules |
| QCONST-20 | Required evaluation observes nuint on a 32-/64-bit target | Use that exact result width/codec, independent of the host |
| QCONST-21 | A nuint query result is supplied directly to an initial constant-generic formal | Apply the separate exact domain rules; nuint is not newly admitted |
| QCONST-22 | An ordinary nuint constant/default uses an admitted query result | Preserve the wider ordinary scalar constant domain |
| QCONST-23 | No T Copy/constant-value codec exists, but its fixed value layout is published | The scalar query result can be admitted without constructing/emitting T |
| QCONST-24 | A current runtime descriptor disagrees with code's selected value representation | Reject/rebind before use; do not mix representation revisions |

## Independent admission and replacement

| ID | Scenario | Required outcome |
|---|---|---|
| QRET-01 | A retained query records exact owner/member/rules/signature | Resolve those semantic identities, not display names |
| QRET-02 | A record retains normalized subject, target width and coherent layout facts | Preserve all through admission and lowering |
| QRET-03 | A required query feature/rule is unknown | Reject before affected use, rather than ignore as optional data |
| QRET-04 | A typed scalar result exists but its retained layout dependency is absent | Reject the incomplete observation |
| QRET-05 | Producer trust is allowed but a record has a malformed shape/value | Trust does not waive structural/value/target checks |
| QRET-06 | Semantic facts are from an untrusted producer | Apply the existing explicit audit/decline policy, not a query flag |
| QRET-07 | Linking/remapping changes physical row indexes | Preserve exact query/type/target/dependency identities and result |
| QRET-08 | Debug/source names are stripped | Required query and coupling semantics remain |
| QRET-09 | The query result domain changes from nuint | Incompatible with retained callers |
| QRET-10 | SizeOf is replaced by object-payload-size behavior | Incompatible subject/observation contract |
| QRET-11 | A fixed size/alignment commitment changes for the same target | Breaking replacement; do not keep old observations under that promise |
| QRET-12 | An unfrozen private layout changes with complete coupled invalidation | Permitted within the remaining public contract, without a frozen-layout claim |
| QRET-13 | A private field is renamed while queried layout promises remain identical | Name change alone does not change the value-layout observation |
| QRET-14 | Current numbers happen to match across distinct nominal modules | Do not coalesce query subjects or their replacement obligations |
| QRET-15 | An optional Attr row, held opcode or scalar bits are used as a query carrier | No mandatory query representation is assigned there |
| QRET-16 | A query consumer tries to infer field access, atomicity or reinterpretation from its result | No such capability follows |
| QRET-17 | typeof(T) succeeds and an allocating reflection object is expected | Retain type evidence only; no object/value codec is selected |
| QRET-18 | IsConstEval is true under the resolved mode contract | IsPureEval is also true; layout queries do not change mode |
| QRET-19 | A runtime object-size, policy-state or arbitrary capability-query API is requested | Outside this bounded catalog until its own exact contract is selected |
| QRET-20 | A valid layout number is persisted without an admitted physical query/dependency representation | Logical approval does not grant physical interoperability |
