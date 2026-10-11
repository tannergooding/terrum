# Source-vocabulary conformance cases

These cases apply to the [source vocabulary](../grammar-spec.md#source-vocabulary)
and [identifier/escape rules](../lexical-grammar.md#source-text-and-identifiers).
Assume otherwise valid declarations and admitted types/contracts. Lexical
recognition, grammatical role, binding and type evidence are separate checks;
a recognized spelling does not establish semantic eligibility.

## Keywords, identifiers and escapes

The keyword rows are families: apply each stated outcome to every spelling
in the designated vocabulary set, not only the displayed examples.

| Case | Situation | Required result |
|---|---|---|
| WORD-01 | Exact reserved vocabulary spelling appears, including typeof | Reserved keyword token independently of imports, declarations and expected type |
| WORD-02 | Exact contextual spelling appears outside its designated position | Ordinary identifier token/name binding |
| WORD-03 | Contiguous @ prefixes any reserved spelling | One escaped identifier with the spelling after @ as its name |
| WORD-04 | Contiguous @ prefixes any contextual spelling | One escaped identifier; suppress contextual recognition even in its designated position |
| WORD-05 | @ordinaryName or @SizeOf | Reject unsupported non-keyword escape |
| WORD-06 | @typeofX or @Typeof | Reject unsupported escape; exact ordinal spelling matters |
| WORD-07 | @ typeof or @/*comment*/typeof | Reject split escape; trivia cannot form one identifier token |
| WORD-08 | @@typeof or @_ | Reject unsupported escape; underscore remains a separate nonbinding token |
| WORD-09 | Typeof, TYPEOF, typeofX or _typeof | Ordinary identifiers, not typeof keyword variants |
| WORD-10 | @typeof is declared as a local, member or nominal type | Use ordinary identifier/declaration rules; no built-in authority |
| WORD-11 | Two @typeof declarations collide in the same declaration space | Diagnose duplicate name; escaping does not create a separate identity |
| WORD-12 | An unescaped typeof is written where an identifier/member name is required | Reject; reserved recognition does not depend on lookup |
| WORD-13 | typeof or via appears inside a comment or literal text | No keyword interpretation of that content |
| WORD-14 | A keyword is continued by an admissible identifier character | One ordinary identifier spelling, not a keyword plus suffix |
| WORD-15 | A primitive spelling other than void appears, including int32 | Identifier token; privileged primitive binding remains a later check |
| WORD-16 | Bare void appears in its type position | Reserved keyword naming the unit type |
| WORD-17 | A declaration uses @void | Ordinary name lookup/declaration; escape grants no primitive redefinition authority |
| WORD-18 | Pure, ConstEval, NoAlias, SizeOf or ObjectSize appears | Identifier, not a reserved/contextual keyword or name-based semantic registration |
| WORD-19 | An attribute target uses param, typeparam, module or assembly | Target-position syntax only; no new reserved word or accepted arbitrary @ escape |
| WORD-20 | A future reservation would reinterpret valid source in the same source-language version | Require a version boundary, not silent reclassification |

## Dedicated type query and identity evidence

These cases retain the existing checked `typeof` form. They do not supply a
universal first-class type-object domain, an open-generic type literal or a
runtime instantiation/dispatch ABI.

| Case | Situation | Required result |
|---|---|---|
| TYPEQ-01 | typeof(T), with a complete visible bound type T | Bind the dedicated query to that complete type, not a callable named typeof |
| TYPEQ-02 | typeof (T), with ordinary trivia before/inside parentheses | Same query grammar; not a postfix call-attachment error |
| TYPEQ-03 | @typeof(value), with an eligible user callable | Ordinary call, including its own arguments/effects/results; no built-in query evidence |
| TYPEQ-04 | @typeof (value) | Reject ordinary call-opener separation even though typeof (T) is a query |
| TYPEQ-05 | A visible callable/type named @typeof exists beside typeof(T) | Query remains built in; the declaration neither shadows nor registers it |
| TYPEQ-06 | typeof(@typeof), with an eligible user type of that name | Query that type under ordinary type-name lookup |
| TYPEQ-07 | typeof(), typeof(T, U) or typeof(value + int32(1)) | Reject; exactly one Type is required |
| TYPEQ-08 | A name in typeof(name) binds only a value | Diagnose missing type; no value-to-type or runtime object inspection fallback |
| TYPEQ-09 | typeof(List<>) is used as an open-generic literal | Unsupported; no unapplied type-constructor domain is admitted |
| TYPEQ-10 | typeof(List<_>) retains an unresolved inference hole | Diagnose unresolved type; not an open-generic witness or wildcard |
| TYPEQ-11 | typeof(T) is typeof(int32) is true on a checked path | Retain exact normalized T = int32 evidence, with no runtime conversion |
| TYPEQ-12 | An alias of int32 is queried in that test | Same normalized type identity |
| TYPEQ-13 | A nominal wrapper with int32 payload is queried | Distinct identity despite equal payload/layout |
| TYPEQ-14 | typeof(write C) is compared with typeof(C) | Different complete qualification; no authority erasure |
| TYPEQ-15 | Closed generic arguments, parameter modes or callable results differ | Distinct complete identities, not equal names or equal-width representations |
| TYPEQ-16 | A positive identity test is part of && and its right operand uses T as int32 | Permit on the established true edge under ordinary flow rules |
| TYPEQ-17 | That identity test is false, skipped or not established on every joined path | No positive refinement on the unproved path/join |
| TYPEQ-18 | A negative identity guard leaves no normal nonmatching path | Retain positive identity on the surviving normal path |
| TYPEQ-19 | A call to @typeof or arbitrary user comparison reports equality | No built-in exact-type evidence from a same spelling/Boolean result |
| TYPEQ-20 | A named captured type has visible name but no available evidence | Diagnose query/type use; visibility does not invent a witness |
| TYPEQ-21 | Captured type evidence exists only at runtime | Require the admitted witness/representation and execution path; unsupported use is not a false identity test |
| TYPEQ-22 | Query success is used to grant Copy, write, ownership, boxing or code generation | Reject the unsupported inference; retain ordinary checked contracts |
| TYPEQ-23 | typeof(Type) is supplied to an attribute | Require that attribute's admitted type-reference schema/encoding; expression syntax alone is insufficient |
| TYPEQ-24 | Type-query syntax is treated as an allocating reflection object or general constant value | No such object/value codec/API follows from the selected form |

## Contextual via selectors

Selectors preserve the [witness](../language/interface-witnesses.md),
[provider](../grammar-spec.md#extension-provider-selection) and
[conversion-owner](../grammar-spec.md#513-operators-assignments-and-expression-gaps)
contracts. Invalid binding does not alter lexical recognition or retry
another syntactic interpretation.

| Case | Situation | Required result |
|---|---|---|
| VIA-01 | via is a local/parameter name or via(value) is an ordinary call | Identifier use outside selector position |
| VIA-02 | value.via(value) names an ordinary nongeneric member | Ordinary member call; no type-selector production |
| VIA-03 | value.via<I>.Member() names a valid interface witness | Select the exact existing witness under its contract |
| VIA-04 | T.via<I>.Item names an associated type/constant | Resolve the exact selected witness and associated identity |
| VIA-05 | value.via<Provider>.Member() names an eligible nominal non-interface provider | Use the explicit provider contract; captured receiver supplies its receiver argument once |
| VIA-06 | Target.via<Source>(value) selects the eligible source conversion owner | Restrict conversion lookup to that owner; do not add a receiver argument |
| VIA-07 | Target.via<Target>(value) selects the destination conversion owner | Use destination-owned lookup with ordinary applicability/ranking |
| VIA-08 | The selector's chosen type is unresolved, inaccessible or semantically ineligible | Diagnose selection; no reinterpretation as a generic member named via |
| VIA-09 | value.via<T>(argument) is intended to invoke a user generic member | It is selector syntax; use value.@via<T>(argument) for the ordinary member |
| VIA-10 | value.@via<T>(argument) names an eligible user member | Ordinary generic member lookup/call, no witness/provider/conversion privilege |
| VIA-11 | via<T>(argument) is an unqualified generic callable use | Ordinary identifier/call; no selector without the preceding dot |
| VIA-12 | .via <I> separates the designated angle opener | Reject attachment violation; interior angle-list trivia remains permitted |
| VIA-13 | .via<I> is recognized while I's declaration category is unknown | Same syntactic selector; later binding chooses/checks its semantic role |
| VIA-14 | .via<I> names a missing interface conformance | Diagnose; selection cannot create a witness |
| VIA-15 | .via<Provider> names an arbitrary public static container | Diagnose missing declared/lexical extension eligibility; no name-based provider discovery |
| VIA-16 | Target.via<Factory>(value) chooses neither source nor destination owner | Reject unsupported conversion owner; ordinary named factories remain separate |
| VIA-17 | Witness/provider/owner selection would bypass access, loan or cleanup checks | Reject; explicit selection adds no authority |
| VIA-18 | A selected conversion fails or its selected candidate set is ambiguous | Propagate failure/diagnose ambiguity; no alternate-owner or ordinary-member fallback |
| VIA-19 | Equivalent token fragments resolve to an interface, provider or conversion owner in different environments | Lexical recognition/escape behavior is unchanged; semantic eligibility remains checked separately |
| VIA-20 | Selector naming is used to change overload ranking or nominal identity | No new preference, conformance or type equality from spelling via |
