# Registered query conformance

These cases specify the dedicated
[registered query forms](../language/registered-queries.md), not compiler
support or a completed target-layout/metadata implementation. Each fixture
supplies its stated complete type facts, target profile and fixed-layout or
coupling obligations. Target-specific numerical fixtures do not define
universal ABI tables.

## Identity, shape and authority

| ID | Scenario | Required outcome |
|---|---|---|
| QREG-01 | `sizeof(Type)` has a complete selected-target layout | Observe the normalized value/handle extent and return nonnegative `nint` |
| QREG-02 | `alignof(Type)` has a complete selected-target layout | Observe its placement alignment and return nonnegative `nint` |
| QREG-03 | A type alias or transparent alias is used as the query subject | Preserve the resolved complete type and selected representation |
| QREG-04 | A user `RuntimeHelpers.SizeOf<T>()` has a similar purpose | It remains an ordinary call with its own effects and constant eligibility, not a query |
| QREG-05 | A live `countof(Place)` has the shared Core `ICountable` witness | Dispatch its readable instance `Count : nint` getter under its synchronous `Pure` contract |
| QREG-06 | A type has a same-shaped `Count` property only through an unrelated interface | Reject it as a live Count witness; only the shared nominal `ICountable.Count` requirement qualifies |
| QREG-07 | A different package attempts to attach compiler query semantics | Reject unauthorized query registration; the forms are grammar-owned |
| QREG-08 | A source author writes a same-named `[Query]` attribute | No general query registration schema or intrinsic authority follows |
| QREG-09 | A same-named ordinary method returns a different type | Keep it an ordinary call; query forms have their specified `nint` results |
| QREG-10 | A query has an unsupported operand shape | Diagnose unless it matches the selected Type, Place, field-path or known-shape production |
| QREG-11 | A query is written with generic method type arguments | Reject the invalid grammar; query operands are not generic calls |
| QREG-12 | A layout query has neither `Blittable` evidence nor unsafe authorization | Reject the non-Blittable observation |
| QREG-13 | `sizeof`/`alignof` appears in its query production | Parse it as a dedicated query; the names alone outside that production do not select a query |
| QREG-14 | `SizeOf` or `AlignmentOf` is used as an ordinary identifier | No built-in member identity follows from those names |
| QREG-15 | A query form is mistaken for a concrete member declaration | Query forms need no declaration body or method signature |
| QREG-16 | An unrelated candidate query/API is absent from Core | Do not invent a language query or imply every Core API belongs to the language floor |
| QREG-17 | A same-named ordinary helper competes with a query form | Parse and bind the query by its grammar-owned form; do not rebind it to the helper |
| QREG-18 | A same-named method or field is offered instead of the Count property | Do not treat it as the live Count witness |
| QREG-19 | `typeof(Place)` names a value whose static type is `G<_>` | Report the existential view type; do not read the value or recover its hidden argument/witness |

## Complete subjects and runtime evidence

| ID | Scenario | Required outcome |
|---|---|---|
| QSUB-01 | Alias I expands to int32 | Query the normalized int32 value representation |
| QSUB-02 | Two nominal structs have equal size/alignment | Equal numbers do not establish type identity or conversion |
| QSUB-03 | sizeof(MyClass) has a pointer-sized class-handle contract | Measure the handle, not fields/header of its allocated object |
| QSUB-04 | sizeof(val<MyClass>) has an admitted inline payload | Measure that selected inline representation |
| QSUB-05 | A compatible box<MyClass> normalizes to MyClass | Query the normalized handle once, not an extra box |
| QSUB-06 | sizeof(ref<int32>) has a three-word descriptor fixture | Measure all three descriptor words, not int32 or one guessed pointer |
| QSUB-07 | sizeof(ptr<void>) is admitted as an opaque pointer | Measure its pointer value; no unit payload address is created |
| QSUB-08 | sizeof(own<S>) is admitted for a non-Copy owner | Query the ownership handle without copying/transferring its allocation |
| QSUB-09 | A Span<int32> fixture has a two-word descriptor | Measure the descriptor independently of backing Length |
| QSUB-10 | An optional int32 has a separately admitted tag/padding layout | Query that optional layout, not int32 alone |
| QSUB-11 | A tuple has a supported complete element/layout contract | Query the whole tuple representation |
| QSUB-12 | A checked Transparent wrapper has an admitted carrier relationship | Observe carrier size/alignment without erasing nominal identity/validity |
| QSUB-13 | sizeof(void) is reached | Zero payload size; do not measure initialization bookkeeping |
| QSUB-14 | alignof(void) is reached | Return zero, meaning no placement requirement |
| QSUB-15 | A bare interface or unapplied generic constructor is supplied | Reject the ineligible complete subject |
| QSUB-16 | A scalable SIMD value uses a runtime vector-length model | Allow the runtime layout query; reject it in required-constant contexts unless target settings fix its extent |
| QSUB-17 | A generic body declares `where T : [Blittable]` | Safe layout query uses the actual T layout under the selected target |
| QSUB-18 | A generic body has neither Blittable evidence nor unsafe authorization, but later examples use int32 | Diagnose at the declaration; specialization cannot repair the source contract |
| QSUB-19 | `sizeof()` has no query subject | Diagnose the malformed query; no result type supplies an omitted subject |
| QSUB-20 | A captured symbolic type is visible without available evidence | Diagnose; name visibility does not invent layout/type evidence |
| QSUB-21 | A valid T has no initialized source value or constant-value codec | The type-only runtime query does not construct or encode a T value |
| QSUB-22 | A readonly/write qualification or constructed argument distinguishes the complete form | Retain its complete identity/formation and selected representation; do not project inner storage |
| QSUB-23 | nameof(parameter) occurs in an admitted dependency schema | Retain its declaration selector; no parameter read or general string-query codec follows |
| QSUB-24 | `sizeof(Place)` or `alignof(Place)` names a vacant `init` Place | Inspect its bound complete static type/layout without reading or initializing it |
| QSUB-25 | `offsetof(Type, FieldPath)` traverses nested inline fields | Return the checked cumulative offset from the supplied complete type |
| QSUB-26 | `offsetof(Place)` names an `init` field Place | Return its terminal offset in the immediate containing type without reading the Place |
| QSUB-27 | `offsetof` names a property, indexer, call result or inferred field name | Reject; do not invoke accessors or infer a path from layout |
| QSUB-28 | `offsetof` path crosses a reference or pointer indirection | Reject; only the supplied type's inline field path is observed |
| QSUB-29 | `countof(Type)` has an explicit fixed-cardinality fact | Return that logical cardinality; do not derive it from byte size or field layout |
| QSUB-30 | `countof(KnownShapeExpression)` has a statically known logical element count | Count syntactically without evaluating elements, materializing storage or enumerating an unknown source |
| QSUB-31 | `countof(Place)` selects the shared `ICountable.Count` witness | Observe the current live value through its exact synchronous `Pure` instance `Count : nint` getter; do not enumerate by shape |
| QSUB-32 | `countof` observes a string literal | Count logical code units in the selected encoding, not Unicode scalars or bytes |

## Target values and execution

| ID | Scenario | Required outcome |
|---|---|---|
| QTGT-01 | sizeof(int32) on supported 32-/64-bit target fixtures | Four bytes regardless of evaluator host or natural alignment |
| QTGT-02 | sizeof(nuint) on 32-/64-bit target fixtures | Four/eight bytes respectively, not host pointer width |
| QTGT-03 | alignof(int64) uses fixtures with 32-bit alignment 4 and 64-bit alignment 8 | Return four/eight while size remains eight |
| QTGT-04 | Sequential int8/int32 uses packing limit 1 | Query fixture size 5 and alignment 1 |
| QTGT-05 | The same fields use aggregate alignment 32 | Query fixture size 32 and alignment 32 |
| QTGT-06 | Packing limit 1 and aggregate alignment 32 are combined | Query fixture size 32 and alignment 32 |
| QTGT-07 | A procedure-call ABI pads/register-splits a value differently | Do not use call slots/classification as value storage size |
| QTGT-08 | A serialized codec or allocation header has a different byte count | Do not substitute it for the complete value extent |
| QTGT-09 | alignof(void) on any selected target | Return zero as the no-placement sentinel |
| QTGT-10 | Descriptor/optional/aggregate layout evidence is absent | Diagnose, without host layout or carrier guessing |
| QTGT-11 | A layout size equals the target nint maximum | Accept the exact representable size when the fixture's other layout rules hold |
| QTGT-12 | Size exceeds nint maximum or is negative | Reject before affected execution; no wrap/clamp/sentinel |
| QTGT-13 | A non-void type requiring placement has zero/negative alignment, or alignment exceeds nint maximum | Reject the invalid layout; zero is valid for `void`'s no-placement query |
| QTGT-14 | The query reaches an admitted coherent type/target record | Pure total observation with no mutable/static/source-value state |
| QTGT-15 | The queried type has constructors or static initialization effects | Do not run them; observing admitted layout does not initialize the type |
| QTGT-16 | A very large T has an admitted scalar size result | Do not allocate/traverse T or charge its extent as evaluator live storage |
| QTGT-17 | Two authored query calls return the same number | Retain both logical call/read/return charges; no caching discount |
| QTGT-18 | A fixed cardinality or live Count exceeds nint maximum | Reject the unrepresentable result; do not wrap, saturate or clamp |

## Required evaluation and retained dependencies

| ID | Scenario | Required outcome |
|---|---|---|
| QCONST-01 | A reached query has exact fixed int32/target evidence and scalar codec | Form its ordinary nint constant under the admitted recipe/envelope |
| QCONST-02 | A generic formal's concrete target layout is not fixed in shared code | Runtime support does not establish a definition-time numeric constant |
| QCONST-03 | A closed required query has exact actual-type evidence | Evaluate under ordinary forward binding and constant admission |
| QCONST-04 | Required target/layout evidence is missing or incompatible | Diagnose with no runtime/default/overload fallback |
| QCONST-05 | An otherwise well-formed runtime query is on an unselected constant path | Do not execute it or require unavailable constant layout evidence there |
| QCONST-06 | An unselected path has a malformed query/type or unsupported required rule | Reject ordinary structural/binding invalidity before interpretation |
| QCONST-07 | A reached query exhausts the selected work/depth/storage envelope | Diagnose exhaustion and publish no partial/default constant |
| QCONST-08 | A backend optionally folds an ordinary query | Preserve exact subject/target and every surviving layout dependency |
| QCONST-09 | An inline type publishes a compatible transitive fixed layout | A retained observation records that exact type/target commitment |
| QCONST-10 | A queried private Auto layout is in an explicitly admitted rebuild-coupled region | Retain the fact only with complete revision/dependency invalidation |
| QCONST-11 | The same Auto number is frozen as nint across an independent replacement boundary | Reject absent fixed commitment or explicit coupling |
| QCONST-12 | The result's own scalar Blittable codec is present | It does not freeze the queried type's layout |
| QCONST-13 | Transparent matches a carrier but has no fixed replacement promise/coupling | Current carrier equality alone cannot authorize independent retention |
| QCONST-14 | A stable class-handle representation is queried | Retain the handle observation without freezing unmeasured private object payload |
| QCONST-15 | A derived allocation extent depends on an unfrozen queried layout | Propagate layout dependencies through arithmetic and its retained use |
| QCONST-16 | An actual coupled representation revision changes | Regenerate/rebind/invalidate affected dependent values/code before use |
| QCONST-17 | Source says AOT/JIT or has a rebuild comment but no coupling records | Those labels/intention do not establish admission |
| QCONST-18 | Value numbering compares queries with different complete types or targets | Do not merge by equal current numeric results alone |
| QCONST-19 | A structured constant includes a query unresolved until target-aware JIT/AOT | Preserve typed values and field identities; resolve only the pending query, materialize from validated `Layout`, and do not replay completed source evaluation |
| QCONST-19 | Reuse occurs under the same query/type/target/layout dependencies | Preserve the exact observation and ordinary legal transformation rules |
| QCONST-20 | Required evaluation observes nint on a 32-/64-bit target | Use that exact result width/codec, independent of the host |
| QCONST-21 | An nint query result is supplied directly to an initial constant-generic formal | Apply the separate exact domain rules; nint is not newly admitted |
| QCONST-22 | An ordinary nint constant/default uses an admitted query result | Preserve the wider ordinary scalar constant domain |
| QCONST-23 | No T Copy/constant-value codec exists, but its fixed value layout is published | The scalar query result can be admitted without constructing/emitting T |
| QCONST-24 | A current runtime descriptor disagrees with code's selected value representation | Reject/rebind before use; do not mix representation revisions |
| QCONST-25 | `countof(KnownShapeExpression)` is reached in required evaluation | Count the admitted syntax shape without executing/materializing its elements |
| QCONST-26 | `countof(Place)` is reached in required evaluation | Require the shared `ICountable.Count` getter's separately admitted constant form/body; Pure alone does not make it constant |

## Independent admission and replacement

| ID | Scenario | Required outcome |
|---|---|---|
| QRET-01 | A retained query records exact operation/rule and, for live Count, the shared `ICountable.Count` requirement plus property/getter witness identity | Resolve those semantic identities, not display names |
| QRET-02 | A record retains normalized subject, target width and coherent layout facts | Preserve all through admission and lowering |
| QRET-03 | A required query feature/rule is unknown | No support claim for the affected use; optional data does not supply the missing rule |
| QRET-04 | A typed scalar result exists but its retained layout dependency is absent | The observation does not establish support for the incomplete dependency |
| QRET-05 | Producer supplies a query record with malformed shape, range or reference | Structural safe-consumption checks reject affected use before binding or execution |
| QRET-06 | Consumer uses a structurally consumable producer-published semantic query fact | Permit reliance without semantic re-proof; producer remains responsible for truth, and an optional audit may examine the fact |
| QRET-07 | Linking/remapping changes physical row indexes | Preserve exact query/type/target/dependency identities and result |
| QRET-08 | Debug/source names are stripped | Required query and coupling semantics remain |
| QRET-09 | The query result domain changes from nint | Incompatible with retained callers |
| QRET-10 | `sizeof` is replaced by object-payload-size behavior | Incompatible subject/observation contract |
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
