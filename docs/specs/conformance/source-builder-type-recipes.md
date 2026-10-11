# Builder type-recipe conformance cases

These cases apply to [builder-type-recipes/1](../language/construction-handlers.md).
Assume otherwise valid visible declarations and already admitted constant
domains. A bound builder recipe is not by itself executable construction
support; operations and lifecycle contracts remain separately required.

## Resolved schemas and authority

| Case | Situation | Required result |
|---|---|---|
| BREG-01 | Primary List<T> has resolved CollectionBuilder(typeof(ListBuilder<T>)) | Bind that exact target-owned type recipe, not an open-generic constructor value |
| BREG-02 | Primary text target has resolved TextBuilder(typeof(FormatterBuilder)) | Bind the fixed builder recipe for that target's interpolated-text kind |
| BREG-03 | A same-spelled user TextBuilder or CollectionBuilder attribute appears | No recognized builder registration |
| BREG-04 | Builder attribute has zero, two or computed value arguments | Reject wrong schema; exactly one direct typeof(Type) is required |
| BREG-05 | A string name, method group or @typeof call is supplied | Reject unsupported type-reference argument |
| BREG-06 | Inline type placement and an exact type target both register the same kind | Reject duplicate nonrepeatable effective placement |
| BREG-07 | Attribute appears on a callable, parameter, field or result | Reject wrong target; no implicit type registration |
| BREG-08 | Secondary partial authors or replaces the primary builder contract | Reject contract-changing secondary placement |
| BREG-09 | Target owner explicitly names a checked external builder | Preserve external identity/access/load dependencies; external placement does not transfer ownership of the registration |
| BREG-10 | An unrelated module/extension tries to add a registration | Reject wrong authority; use an owned nominal adapter |
| BREG-11 | Alias of an unrelated target is annotated to create a new registration | Reject new alias-based ownership; aliases do not create target definitions |
| BREG-12 | One target supports both construction kinds with complete contracts | Distinct keys; each kind remains independently checked and nonrepeatable |
| BREG-13 | Same record is reached through several imports | Deduplicate exact identity |
| BREG-14 | Distinct records claim one target/kind but name equal builders | Reject conflict; equality of builder types does not merge records |
| BREG-15 | Competing claim is inaccessible at one use | Conflict remains; accessibility does not choose the other claim |
| BREG-16 | Claims differ by import order, priority or conditional constraints | No selector supplied; conflicting claims reject |
| BREG-17 | Ordinary parentheses surround typeof(Builder<T>) | Preserve the bound type-reference category; grouping creates no type-object value |
| BREG-18 | A conditional/operator/conversion computes between type-query expressions | Reject unsupported computed recipe; use one ordinary bound Type tree |

## Bound recipes and ordinary substitution

The target's formals below denote their exact declaration identities and
ordinals. Type names in recipes resolve in the target declaration environment.

| Case | Situation | Required result |
|---|---|---|
| BRECIPE-01 | List<T> recipe is ListBuilder<T>; target is List<int32> | Select ListBuilder<int32> by forward substitution |
| BRECIPE-02 | Target attribute precedes the written <T> list | T binds to that target formal, not a same-spelled namespace type |
| BRECIPE-03 | Pair<T,U> recipe is PairBuilder<U,T> | Reorder the actual arguments exactly as the recipe states |
| BRECIPE-04 | Target<T,U> recipe repeats T or ignores U | Retain written mapping; no equal-arity/all-formals requirement |
| BRECIPE-05 | Generic target selects a fixed nongeneric builder | Legal type recipe when formation/construction obligations are met |
| BRECIPE-06 | Recipe nests a type, such as Builder<Pair<T,U>> | Substitute the complete nested tree; no inverse inference |
| BRECIPE-07 | Recipe uses admitted target constant N in Builder<T,N> | Retain N's exact constant domain/value, not its display text or a type argument |
| BRECIPE-08 | Recipe uses an eligible constant expression/default | Ordinary domain, evaluation/default and dependency rules apply; no new domain from the attribute |
| BRECIPE-09 | Supplied builder argument has the wrong type/constant formal kind | Diagnose, without selecting another arity |
| BRECIPE-10 | typeof(ListBuilder<>) is written | Reject unapplied generic constructor; no open-generic literal/mapping |
| BRECIPE-11 | typeof(ListBuilder<_>) leaves an unresolved ordinary hole | Diagnose; wildcard/inference hole is not a target formal |
| BRECIPE-12 | Recipe references a caller/method formal not owned by the target environment | Reject unresolved/wrong-scope binding; no use-site capture |
| BRECIPE-13 | A target formal is renamed but keeps its declaration ordinal | Same bound recipe semantics |
| BRECIPE-14 | Use-site imports contain another same-spelled builder type | Do not rebind; retain target-definition selection |
| BRECIPE-15 | Recipe uses a transparent type alias | Normalize its selected target; alias adds no registration authority |
| BRECIPE-16 | Recipe expands a cyclic/growing alias or associated binding | Reject nonfinite expansion under ordinary type rules |
| BRECIPE-17 | Recipe reaches an associated type through a declared witness | Retain that exact selected witness/binding dependency |
| BRECIPE-18 | Builder requires Copy but target's generic domain is unconstrained | Reject invalid generic registration, despite one Copy concrete use |
| BRECIPE-19 | Target contract entails the builder's formation constraints | Admit that proved formation; actual construction protocol still needs checking |
| BRECIPE-20 | A target write qualification or nested nominal wrapper reaches the builder argument | Preserve the complete type; no attenuation/carrier substitution to satisfy constraints |
| BRECIPE-21 | A generic default resolves an omitted recipe argument | Use the selected builder declaration's ordinary default, not arity guessing |
| BRECIPE-22 | Target or builder type-name arity selection is ambiguous | Diagnose before checking convenient values/constraints; no other-declaration retry |
| BRECIPE-23 | Recipe has only an unconstrained builder type formal | No builder capability from the formal or typeof spelling |
| BRECIPE-24 | Unsupported representation/witness/target obligations remain after substitution | Diagnose affected use; a normalized type name does not waive eligibility |

## Construction-use boundaries

| Case | Situation | Required result |
|---|---|---|
| BUSE-01 | Target context establishes a complete collection type with a recipe | Resolve its unique record before full construction checking |
| BUSE-02 | Context-free [] or interpolation has no unambiguous target | No default array/List, text type, numeric width or backwards builder inference |
| BUSE-03 | Two target candidates remain otherwise ambiguous | Builder availability/private behavior cannot supply a new ranking preference |
| BUSE-04 | Selected recipe has missing/inaccessible required operations | Diagnose; no similarly named method, constructor, conversion or alternate-record fallback |
| BUSE-05 | Builder type has Create/Add/Append/Build names but no checked roles | Names alone do not supply the construction protocol |
| BUSE-06 | Target has TextBuilder but a noninterpolated Text literal needs a parser | Literal registration remains distinct; no implicit allocating text/parser fallback |
| BUSE-07 | An already typed value happens to have a builder-registered type | Do not reinterpret it as source collection/interpolation syntax |
| BUSE-08 | Recipe binding names a builder with static initialization/owning state | Naming invokes nothing; actual construction operations retain initialization/access/ownership obligations |
| BUSE-09 | Recipe is used to grant write, pin, sharing or private-member authority | Reject unsupported authority; ordinary operation contracts apply |
| BUSE-10 | Captures have effects, moves or borrowed backing | Preserve eager once-only capture and actual dependencies; type binding does not change capture/drain behavior |
| BUSE-11 | A later drain refuses after prior fragments/holes were processed | Preserve earlier effects/output and exact cleanup; no replay/rollback from registration |
| BUSE-12 | A nested fragment/spread has no admitted direct-fragment/iteration contract | Diagnose unsupported construction; no intermediate/no-allocation promise from syntax |
| BUSE-13 | Ordinary runtime builder is not ConstEval or emitted-value eligible | Runtime support is independent; required constants need separate program/resource/value admission |
| BUSE-14 | Target is Blittable/frozen Copy but has no builder registration | No construction or constant-formal capability from layout alone |
| BUSE-15 | Builder lifecycle/capture protocol is unavailable | Diagnose affected construction; bound recipe alone is insufficient |
| BUSE-16 | Task/callback return is used to extend a synchronous borrowed capture | Require the separately admitted completion/lifetime protocol; no implicit extension |

## Independent records and evolution

| Case | Situation | Required result |
|---|---|---|
| BVER-01 | Independent consumer imports target/recipe records | Substitute exact bound definition/formal identities, not source names |
| BVER-02 | Linking remaps definitions, associated bindings and witnesses | Preserve exact identity/dependencies and check compatibility |
| BVER-03 | Consumer lacks required recipe or protocol rule version | Reject affected use; no optional-attribute erasure or guessed TypeSpec |
| BVER-04 | Producer removes a required registration or narrows valid target arguments | Breaking dependent source/construction contract |
| BVER-05 | Replacement builder returns equal elements but changes effects/refusal/backing/cleanup | Equality of final elements is insufficient for compatibility |
| BVER-06 | Changed builder satisfies all retained published construction/dependency obligations | Judge by that contract, not a requirement that private source text is unchanged |
| BVER-07 | Recipe is placed in optional Attr data or a held metadata/opcode slot | No carrier allocated; unsupported mandatory semantics cannot be ignored |
| BVER-08 | Consumer treats typeof(Builder<T>) as a persisted runtime type object | No such value codec, open-generic object or reflection API is admitted |
