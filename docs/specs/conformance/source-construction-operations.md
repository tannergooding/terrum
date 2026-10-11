# Standard construction operation conformance cases

These cases apply to [construction-operations/1](../language/construction-handlers.md#standard-text-operations),
[collection acceptance](../language/construction-handlers.md#collection-acceptance-and-spreads)
and the cited eager factory/completion/parameter rules. Assume otherwise valid
typed captures, witnesses and admitted storage/iteration contracts.
They describe source/contract outcomes, not compiler or library execution.

## Exact text requirements

| Case | Situation | Required result |
|---|---|---|
| OPSIG-01 | Value has the selected Core IFormattable witness with readonly TryFormat receiver and write ref<TWriter>/TextSpan inputs | Retain that exact member/signature and declared effects |
| OPSIG-02 | Writer has the selected Core ITextWriter TryWrite/TryWriteScalar/TryWriteFormatted requirements | Use exact nominal member identities, not matching names |
| OPSIG-03 | A same-spelled user interface or coincident member list is supplied | No standard writer/formatter conformance |
| OPSIG-04 | Formatted hook takes ref<TValue>, int32 alignment and TextSpan format | Required synchronous Boolean role with TValue's checked IFormattable constraint |
| OPSIG-05 | Hook uses write ref<TValue>, by-value value, another width type or two-argument legacy signature | Not the standard role; no hidden conversion/default role inference |
| OPSIG-06 | Writable writer input/receiver lacks backing access | Reject missing authority; mut local binding does not upgrade backing |
| OPSIG-07 | Formattable requirement is missing despite a successful private body | Reject missing caller-visible nominal capability |
| OPSIG-08 | Async writer result is supplied for a synchronous role | Reject wrong signature; no implicit await/async drain |
| OPSIG-09 | Writer accepts valid TextSpan data | True accepts complete scalar sequence; false follows declared complete-scalar prefix/refusal contract |
| OPSIG-10 | Writer accepts one rune | True accepts the scalar; normal false accepts none, never publishes a partial encoded scalar as valid text |
| OPSIG-11 | Raw bytes/invalid scalar data are substituted for text/rune inputs | Require their ordinary checked validity/encoding adapter; no reinterpretation |
| OPSIG-12 | Member is used as an allocator, NoAlias, Pure, ConstEval or retention guarantee | No such implication from these nominal roles |

## Source roles and typed content

| Case | Situation | Required result |
|---|---|---|
| OPDATA-01 | Interpolation has N holes | Ordered N+1 literal-data roles and N formatted-value roles; drain fragment order |
| OPDATA-02 | Literal data segment is empty | No writer invocation for that segment; retain logical graph role/type identity |
| OPDATA-03 | User explicitly calls TryWrite with empty text | Ordinary declared operation/effects; source empty-segment rule does not erase the call |
| OPDATA-04 | Hole omits alignment and format | Effective int32 zero and validated empty TextSpan data, no extra source evaluation |
| OPDATA-05 | Equivalent shape uses omitted defaults versus explicit typed zero/empty data | Same normalized complete content shape in one module; actual source evaluations remain distinct |
| OPDATA-06 | Numeric alignment literal and untyped numeric value hole are supplied | Alignment has int32 context; value hole still has no default type from IFormattable |
| OPDATA-07 | Character literal supplies a hole value | rune context, then standard typed hook/checked rune formatting to scalar write without temporary Text |
| OPDATA-08 | Identifier or quoted/raw format supplies static text | Use spelling/decoded validated data, not symbol lookup or runtime source parsing |
| OPDATA-09 | Parenthesized runtime format expression has effects | Bind expected TextSpan and capture once after alignment; no deferred stringify |
| OPDATA-10 | Generated interpolation formatter receives nonempty outer format | Ordinary require failure before draining; not Boolean writer refusal |
| OPDATA-11 | Two content values share a generated complete type | Separate data/captures/backing, no interning of values or source replay |
| OPDATA-12 | Nested source construction lacks its own selected target | No body inspection/flattening or implicit allocating intermediate representation |

## Unicode-scalar minimum alignment

| Case | Situation | Required result |
|---|---|---|
| OPALIGN-01 | Output is a with alignment 3 | Two U+0020 spaces before a |
| OPALIGN-02 | Output is a with alignment -3 | Two U+0020 spaces after a |
| OPALIGN-03 | Alignment is zero | No padding |
| OPALIGN-04 | Output scalar count is equal to or exceeds magnitude | Preserve full output, no truncation or negative padding |
| OPALIGN-05 | Output is one supplementary scalar represented by two UTF-16 units | Count one scalar, not two units |
| OPALIGN-06 | Output is e followed by one combining mark | Count two scalars, not one grapheme/display column |
| OPALIGN-07 | Equal scalar output has UTF-8/UTF-16/UTF-32 encodings | Same minimum-width semantics despite different storage lengths |
| OPALIGN-08 | Output is empty with nonzero alignment | Padding alone meets magnitude if accepted |
| OPALIGN-09 | Alignment is int32 minimum | Mathematical magnitude 2147483648; no int32 negation overflow/wraparound |
| OPALIGN-10 | Padding exceeds actual available staging/destination capacity | Follow declared refusal/failure without fabricated success or narrowing |
| OPALIGN-11 | Right alignment needs formatted width before final output placement | One formatting evaluation with admitted staging/in-place placement, not count-and-replay |
| OPALIGN-12 | A value formatter mutates observable state | Mutation executes at most once, including staging/refusal paths |
| OPALIGN-13 | Writer declines before reaching the value formatter | No formatter effects, but eager source captures remain performed |
| OPALIGN-14 | Writer refuses after formatter/staging or during padding | Preserve reached effects and accepted output; abandon actual staging obligations once |
| OPALIGN-15 | Width/format/formatter operation throws | Propagate the actual checked failure; not Boolean refusal or automatic rollback |
| OPALIGN-16 | Width is interpreted as bytes/code units/terminal columns | Not the standard role; separate explicit API/contract required |

## Draining, typed forwarding and refusal

| Case | Situation | Required result |
|---|---|---|
| OPDRAIN-01 | Plain text construction captures typed holes | Capture all values/alignment/runtime formats before factory and ordered writer operations |
| OPDRAIN-02 | Disabled TextTarget consumer never formats | All reached source captures complete; no inferred builder acquisition or writer drain |
| OPDRAIN-03 | First literal write refuses | Later formatter/writer operations are unreached; previously completed captures remain |
| OPDRAIN-04 | First formatted hook refuses | No later literal/hole drain; no source or formatter replay |
| OPDRAIN-05 | Writer forwards a nested typed value formatter | Same selected writer/typed hooks under declared contracts, no obligatory boxed string |
| OPDRAIN-06 | Custom writer intercepts typed values | Its checked behavior applies; source does not bypass its hook by reading private formatter bodies |
| OPDRAIN-07 | Same still-valid content formats again | Reuse held data, not source expressions; each call has ordinary effects/refusal |
| OPDRAIN-08 | Captured handle's backing changes lawfully before draining | Preserve shallow capture and read-time state; alignment/staging is not a snapshot guarantee |
| OPDRAIN-09 | Plain value construction gets normal writer false | Ordinary require failure, then owned builder/capture cleanup; no prefix target |
| OPDRAIN-10 | Explicit writer/consumer gets normal false | Preserve its declared status/prefix contract unless caller explicitly requires success |
| OPDRAIN-11 | Formatted value is non-Copy/owned | Readonly formatting loan does not transfer or clone captured ownership |
| OPDRAIN-12 | Borrowed value is retained beyond its supported dependency extent | Reject missing retention/lifetime relationship, not implicit boxing or caller-lifetime extension |
| OPDRAIN-13 | Two holes supply the same live captured value/handle | Distinct formatted-value operations, each with at most one formatter invocation; sharing still requires an ordinary equivalence proof |

## Collection item and result roles

| Case | Situation | Required result |
|---|---|---|
| OPITEM-01 | Selected Core ICollectionBuilder<TBuilder,TItem,TResult> witness | Retain by-value TryAdd and exact inherited factory/completion requirement identities |
| OPITEM-02 | Same-named TryAdd lacks that witness or takes ref<TItem> | Not this standard acceptance role |
| OPITEM-03 | Target formals forward-substitute the published element term | Use that known relationship for typed element/spread evidence; no inverse body inference |
| OPITEM-04 | Completion TResult differs from complete contextual target | Reject result relation; no hidden access/representation conversion or adapter search |
| OPITEM-05 | Explicit nominal adapter performs admitted internal result attenuation/conversion | It must initialize exact target output and satisfy every retained result/ownership contract |
| OPITEM-06 | Element target provides numeric literal context | Construct that type; no default element type for a context-free/empty family |
| OPITEM-07 | Typed element differs in invariant nested arguments | Reject incompatible item evidence; no recursive covariance/promotion |
| OPITEM-08 | Named unique item is captured without explicit move | Reject missing transfer |
| OPITEM-09 | Named unique item is moved or a fresh owned result is captured | Source becomes vacant where moved; drain transfers held item into by-value acceptance once |
| OPITEM-10 | TryAdd returns true | Append exactly one logical item in order and retain its actual owned/backing obligations |
| OPITEM-11 | TryAdd returns false with an owning input | No appended item; consumed rejected parameter cleaned before return, no restored source |
| OPITEM-12 | TryAdd throws after a partial owner transition | Preserve actual owner/effects and clean each current obligation once; no replay/rollback |
| OPITEM-13 | Earlier items were accepted before later refusal | Keep their sequence/ownership with the builder until actual abandonment cleanup |
| OPITEM-14 | An element contains borrowed/shared backing | No deep snapshot or new storage/lifetime/NoAlias permission |
| OPITEM-15 | Target supports affine/reference elements under checked contracts | No language-wide Copy/ManagedStorable restriction from this protocol |
| OPITEM-16 | Captures and builder are abandoned or builder is completed | Retire only current owners; successful result transfer is not dropped again by builder |
| OPITEM-17 | One recipe/target result admits distinct item witnesses | Reject ambiguous construction relationship before evaluating elements; convenient literal types do not choose one |
| OPITEM-18 | Item term cannot be established from the published owning-formal relationship | No inverse body/result inference or guessed item type |
| OPITEM-19 | Target's declared result semantics deduplicate/aggregate accepted inputs | Still accept each logical input once in order and honor ownership; no universal physical slot/count/order promise |

## Separately admitted spreads

| Case | Situation | Required result |
|---|---|---|
| OPSPREAD-01 | Source and item/advance/cleanup have an exact admitted iteration contract | Use those identities and declared order/modes, not method-name discovery |
| OPSPREAD-02 | Captured Source() is lazy | Capture source before factory; no enumeration until that drain position |
| OPSPREAD-03 | Expression contains A(), spread Source(), B() | Capture A, Source, B; acquire; accept A; iterate/accept source; accept B; complete |
| OPSPREAD-04 | Prior acceptance refuses before a factory-source spread is reached | No iterator acquisition/advance for it; source capture already executed |
| OPSPREAD-05 | Iterator yields no items | Execute admitted acquisition/exhaustion/cleanup as reached, without assuming effect-free empty iteration |
| OPSPREAD-06 | Acceptance refuses mid-spread | No later advance/item/spread; close live iteration state once before builder abandonment |
| OPSPREAD-07 | Iterator acquisition/advance/item operation throws | Preserve actual failure and reached cleanup/owners; no default element or retry |
| OPSPREAD-08 | A later completion refuses after spread exhausts | Spread state already retired, not closed again during completion failure |
| OPSPREAD-09 | Borrowed sequence exposes unique elements | No implicit move out of its slots |
| OPSPREAD-10 | Owning cursor yields fresh owned item results | By-value acceptance under its exact transfer contract; no duplicate/restore of yielded owners |
| OPSPREAD-11 | Copy item is obtained through an admitted readonly loan | Copy only under that item contract; preserve source backing and cleanup |
| OPSPREAD-12 | A retained lending item prevents next advance/publication | Reject invalid lifetime use; no snapshot/boxing/lifetime extension |
| OPSPREAD-13 | Async-only iterator is used in synchronous spread | No automatic await/enumeration; require a separately admitted contract/explicit API |
| OPSPREAD-14 | Nested untyped collection is supplied as a spread source | Needs its own target/direct-fragment relationship; standard TryAdd does not infer array/List |

## Independent contracts and evolution

| Case | Situation | Required result |
|---|---|---|
| OPVER-01 | Consumer lacks construction-operations/1 or exact required witness/member records | Reject affected use before execution |
| OPVER-02 | Producer changes alignment domain, measurement or argument modes | Incompatible required operation contract, not same-name signature adaptation |
| OPVER-03 | Producer replaces once-only formatting with count-and-replay | Changed effects/evaluation contract, even for equal final text |
| OPVER-04 | Producer weakens element/output ownership or cleanup | Incompatible dependent contract |
| OPVER-05 | Private writer/builder strategy changes within every retained obligation | Preserve source validity/behavior; no incompatibility solely from private code choice |
| OPVER-06 | Optional attributes or unassigned metadata/opcodes are used to carry standard roles | No physical encoding admitted by these logical rules |
| OPVER-07 | Linking remaps graph and operation records | Preserve nominal identities, int32/scalar semantics, exact result and backing/cleanup dependencies |
| OPVER-08 | Standard operations are treated as a deferred producer/evaluation policy | No such authority; eager captures and ordinary synchronous drain remain |
