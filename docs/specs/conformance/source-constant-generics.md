# Constant-generic conformance cases

These cases apply [constant-generics/1](../language/constant-generics.md) and
the cited type, call, constant-execution, layout and admission contracts.
Assume otherwise valid definitions and supported required dependencies.
`NonNegative` and `Even` are checked Transparent int32 wrappers with the
named invariants; `Flag` wraps bool; `Tagged<T>` wraps int32 while retaining
its complete tag argument. Factories are ordinary explicitly admitted APIs.
These are specification obligations, not compiler or physical codec results.

## Domain and Transparent admission

| Case | Situation | Required result |
|---|---|---|
| CDOM-01 | Each signed fixed integer width supplies a formal domain | Admit its exact type and mathematical range, including int128 |
| CDOM-02 | Each unsigned fixed integer width supplies a formal domain | Admit its exact type and mathematical range, including uint128 |
| CDOM-03 | bool supplies a domain | Exactly two logical values, no integer-domain equivalence |
| CDOM-04 | Nominal enum has an admitted fixed-integer base | Preserve nominal domain and underlying value, including unnamed values |
| CDOM-05 | nint/nuint has a known target width | Not an initial constant-formal domain, even when its current width equals int32/int64 |
| CDOM-06 | Character/code-unit, float/decimal or void domain is supplied | Reject initial-domain membership; other constant/type positions remain independent |
| CDOM-07 | Optional, tuple or general frozen Copy aggregate is supplied | Ordinary constant eligibility does not supply a constant-formal domain |
| CDOM-08 | Reference, pointer, owner, managed handle or typeof evidence is supplied | No initial constant-generic domain or implicit carrier extraction |
| CDOM-09 | Checked Transparent wrapper has one fixed-integer field and every required value commitment | Derive eligibility without a second attribute/protocol |
| CDOM-10 | Checked Transparent wrapper has one bool field and every required value commitment | Derive Boolean-carrier eligibility while preserving wrapper identity |
| CDOM-11 | Primitive field type is a transparent alias | Normalize alias and check the same carrier, not a new domain |
| CDOM-12 | Auto/Sequential or equal-sized one-field struct lacks Transparent | Reject missing checked carrier relationship |
| CDOM-13 | Only a same-spelled user layout attribute is present | No resolved Transparent capability |
| CDOM-14 | Transparent wrapper has zero or multiple instance fields | Reject; additional zero-data field still counts |
| CDOM-15 | Extra static fields exist beside the sole instance field | Static fields do not alter instance-field count; construction/effects remain checked |
| CDOM-16 | Copy, frozen validity, per-target Blittable or typed value codec is missing | Reject the affected required domain/value use, no shape-based fallback |
| CDOM-17 | Single field has enum, nominal wrapper, optional, floating or reference type | Outside the selected primitive-field subset; no recursive nominal erasure |
| CDOM-18 | Generic field type is established through valid forward substitution | Check that complete carrier and the instance's published obligations |
| CDOM-19 | Generic carrier remains unresolved without a domain guarantee | Reject unproved use; do not inspect later concrete instantiations |
| CDOM-20 | Only Copy/Blittable or integer-operation witnesses constrain an unknown field type | They do not imply membership in the closed integer/Boolean domain catalog |
| CDOM-21 | Wrapper promises unique/non-Copy ownership or uncommitted identity-bearing storage | Transparent alone does not admit an emitted Copy constant domain |
| CDOM-22 | Derived domain is used as a parser, conversion, operator or private-field-access capability | No such implication or generated API |
| CDOM-23 | Tagged<T> has a known int32 field and checked formation contracts while its tag remains symbolic | Prove the domain from those contracts; retain T in nominal identity without private-body or inverse inference |

## Canonical identity

| Case | Situation | Required result |
|---|---|---|
| CDKEY-01 | Same integer domain/value has different decimal/based source spellings | Equal canonical argument |
| CDKEY-02 | Signed integer zero has positive and negative source signs | Same mathematical zero |
| CDKEY-03 | Same numerical value uses different integer widths/signedness | Distinct domains, not implicit promotion |
| CDKEY-04 | false/true is compared with integer zero/one | Distinct domain/value identities |
| CDKEY-05 | Enum has duplicate-valued named members | Same canonical value within that exact enum |
| CDKEY-06 | Enum value is unnamed but fits its declared base | Valid canonical value; Flags/name lists do not change identity |
| CDKEY-07 | Two enums or an enum and its carrier have equal bits | Distinct domains |
| CDKEY-08 | NonNegative.Create(16) and NonNegative.Create(8 + 8) succeed | Same complete domain/carrier value and enclosing constructed type |
| CDKEY-09 | Different valid values share a wrapper type | Distinct canonical arguments |
| CDKEY-10 | NonNegative and Even contain the same valid carrier value | Distinct nominal arguments |
| CDKEY-11 | Tagged<int32> and Tagged<uint32> contain equal carriers | Distinct complete domains despite equal representation |
| CDKEY-12 | Transparent alias names a wrapper domain | Same normalized complete domain/value |
| CDKEY-13 | Equal nominal names come from different modules/source units | Distinct resolved definition identities |
| CDKEY-14 | Repeated imports resolve the same definition and checked value | Same canonical argument, independent of import order |
| CDKEY-15 | User equality says unequal carriers are equal, hashes collide or methods throw | Canonical comparison invokes none of those methods and still distinguishes values |
| CDKEY-16 | Authored source compares two wrapper values with == | Needs its ordinary operator/witness; no automatic source equality from generic identity |
| CDKEY-17 | Target endian/layout or physical row positions differ | Preserve typed canonical value; materialization/remapping is separate |
| CDKEY-18 | Raw carrier is out of range, bool encoding is invalid or wrapper state is vacant | Reject invalid value, not wrapping/canonicalization into success |
| CDKEY-19 | Factories/evaluation sites differ but yield equal valid wrapper values | Same canonical value without erasing either authored construction's obligations |
| CDKEY-20 | Generic arguments appear in different positions or in a different selected constructor | Preserve full ordered complete type identity |

## Formation and validity

| Case | Situation | Required result |
|---|---|---|
| CDFORM-01 | Eligible ConstEval factory fully initializes valid NonNegative | Admit the resulting argument under existing constant-request contracts |
| CDFORM-02 | Negative NonNegative input fails its declared check | Required evaluation diagnoses; no raw int32-bit wrapper manufacture |
| CDFORM-03 | Odd Even input fails its declared invariant | Required evaluation diagnoses despite representable integer payload |
| CDFORM-04 | Factory publishes vacant, partial or invalid output | No canonical argument or complete target value |
| CDFORM-05 | Registered literal parser refuses while forming a domain value | Required formation diagnoses, no default or another parser/overload |
| CDFORM-06 | Factory is Pure but lacks required ConstEval/body or admitted intrinsic support | No required Terrum evaluation from optional backend folding |
| CDFORM-07 | Reached operation/program/codec or dependency is unavailable | Explicit unsupported requirement, no runtime fallback |
| CDFORM-08 | Required construction exceeds the admitted resource envelope | Exhaustion diagnoses under existing accounting, no alternative construction |
| CDFORM-09 | Construction or its checked exit fails | No published argument; preserve failure/cleanup under the constant-execution contract |
| CDFORM-10 | Eligible construction performs private initialization/mutation | Ordinary pure-form checks permit it; completed valid result still required |
| CDFORM-11 | Constructor uses mutable ambient static state or other unsupported effects on its selected constant path | Reject constant-form eligibility; Transparent does not suppress effects |
| CDFORM-12 | Preexisting checked constant or earlier formal is forwarded | Retain exact domain/value/dependencies without rerunning its constructor |
| CDFORM-13 | An independent authored call happens to produce an already-known value | Equality alone does not erase required evaluation/validity effects or failure order |
| CDFORM-14 | Wrapper has no Integer literal registration but numeric syntax is supplied | Eligibility does not create a parser, constructor or implicit carrier wrapping |
| CDFORM-15 | Explicit admitted conversion produces the required wrapper domain | Evaluate/check it normally, not implicit generic-argument conversion |
| CDFORM-16 | Generic formal expects NonNegative but argument is typed int32/Even | Reject domain mismatch, no carrier-based rebinding |
| CDFORM-17 | A narrowed wrapper receives an invalid trusted raw reinterpretation | That operation retains its validity obligations; domain admission does not validate arbitrary bits |
| CDFORM-18 | Ordinary frozen aggregate/default remains valid outside this catalog | Preserve its ordinary constant use without admitting it as a constant-formal domain |

## Binding, defaults and forwarding

| Case | Situation | Required result |
|---|---|---|
| CDBIND-01 | Direct literal or qualified constant name is supplied in generic arguments | Existing syntax; selected formal still checks exact domain and validity |
| CDBIND-02 | Factory/arithmetic expression supplies a constant argument | Group under existing syntax, no new wrapper-specific production |
| CDBIND-03 | Explicit argument establishes a formal | Pin its exact domain/value; later evidence checks rather than reinfers it |
| CDBIND-04 | Typed matching injective nominal shape contains a known constant | Forward matching retains exact domain/value evidence |
| CDBIND-05 | Matching shape has unequal canonical constant values/domains | Reject incompatible evidence |
| CDBIND-06 | Shape exposes N + 1 or a wrapper-construction expression | No inverse arithmetic/constructor solver for unknown N |
| CDBIND-07 | Constant formal is forwarded into a builder recipe or another generic | Preserve nominal domain/value and already-bound dependencies |
| CDBIND-08 | A generic default uses earlier formals and yields the exact valid domain | Admit under declaration-ordered ordinary default rules |
| CDBIND-09 | Default refers to itself, a later formal or a cycle | Reject invalid dependency direction |
| CDBIND-10 | Required and defaulted formals interleave | Earlier-only dependency order still applies |
| CDBIND-11 | Explicit default selects a published earlier-formal expression | Resolve its existing dependencies; no replacement by convenient inference |
| CDBIND-12 | _ has no supported evidence but a default exists | Reject missing inference, not default fallback |
| CDBIND-13 | Domain default has an invalid wrapper value or another nominal domain | Reject; no carrier reinterpretation or fallback |
| CDBIND-14 | Rename the formal or reorder named source call arguments | Preserve owner/ordinal identity and source evaluation rules |
| CDBIND-15 | Constraint or desired result would be satisfied by another domain/value | No constraint-driven value search or literal-width inference |
| CDBIND-16 | Unproved const T formal is instantiated only with integers today | Current instantiations do not establish declaration-level eligibility |
| CDBIND-17 | A checked symbolic earlier formal supplies exact domain/value facts | Forward those facts under existing guarantees, no body inspection |
| CDBIND-18 | Same-layout carrier is used in an invariant nested generic argument | Not the nominal wrapper argument; preserve complete inner identity |
| CDBIND-19 | const Tagged<T> N is forwarded before T is closed | Retain the checked symbolic domain/formal relationship, then substitute T forward; no premature tag erasure or canonical value construction |

## Independent contracts and evolution

| Case | Situation | Required result |
|---|---|---|
| CDVER-01 | Defining module publishes the checked wrapper contracts | Derive eligibility from those commitments, no second registration |
| CDVER-02 | Unrelated package attaches an attribute or matching protocol | No authority to add eligibility to another nominal type |
| CDVER-03 | Consumer lacks required rules or domain/value obligations | Reject before generic use |
| CDVER-04 | Binary carrier/domain/value shape is malformed or inconsistent | Reject despite producer trust; no erasure to scalar or skipped required metadata |
| CDVER-05 | Producer validity facts require selected trust/audit admission | Use that explicit policy; metadata flag alone supplies no trust |
| CDVER-06 | Linker remaps definitions/fields/target dependencies | Preserve exact nominal/carrier value identity and obligations |
| CDVER-07 | Producer removes Transparent/required Copy/Blittable/value commitments | Breaks dependent domain/value contracts |
| CDVER-08 | Carrier type, width or canonical identity changes | Incompatible retained domain, not a body-only change |
| CDVER-09 | Validity narrows and invalidates previously promised values | Incompatible dependent contract |
| CDVER-10 | Private field name/body changes while every promise remains satisfied | No value-identity change solely from private spelling/implementation |
| CDVER-11 | Documented ConstEval construction results change incompatibly | Retain existing constant-result breaking-change policy |
| CDVER-12 | Optional Attr data, scalar bits or an unassigned physical slot is used as nominal-domain encoding | No physical schema activated by these logical rules |
| CDVER-13 | Different target profiles materialize the same canonical argument | Follow each admitted ABI/value recipe, not host bytes or new nominal erasure |
| CDVER-14 | Eligibility is extended to a new carrier or aggregate family | Requires separately admitted rules, not a matching layout or private compiler extension |
