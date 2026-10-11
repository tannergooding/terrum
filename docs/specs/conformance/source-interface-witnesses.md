# Interface-witness conformance cases

These cases apply [interface witnesses](../language/interface-witnesses.md),
[complete qualification](../language.md#types-and-operations), source
binding, and ordinary call/lifetime contracts. They are specification
obligations, not executed compiler tests. Assume valid declarations and
all unrelated constraints/effects; generic examples use `[Copy]` when
passing named values by value without `move`.

## Nominal identity and associated members

| Case | Situation | Required result |
|---|---|---|
| WIT-01 | A type explicitly supplies every requirement of `I` | One checked nominal witness |
| WIT-02 | A type has the same member names/signatures but no conformance | No witness |
| WIT-03 | Importing an extension supplies matching methods | No new nominal conformance |
| WIT-04 | An explicit adapter implements `I` | Adapter witness only; original type unchanged |
| WIT-05 | Two unrelated interfaces declare `Read` with equal signatures | Two requirement identities |
| WIT-06 | One ordinary implementation satisfies both complete contracts | Two mappings may share that target, not their interface identity |
| WIT-07 | Multiple inheritance paths reach the same closed declaring member | One requirement |
| WIT-08 | Paths reach distinct closed instances of one generic interface | Keep complete instances distinct |
| WIT-09 | Interface inheritance cycles through different type arguments | Reject declaration cycle |
| WIT-10 | An explicit implementation names one interface member | Bind only that identity; no new unqualified member |
| WIT-11 | Eligible authored implementation candidates are ambiguous | Diagnose; no default fallback |
| WIT-12 | A selected authored implementation exists alongside a default | Use the authored binding |
| WIT-13 | No authored implementation is selected and one inherited default remains | Use that default |
| WIT-14 | The same default is reached through a diamond | Deduplicate its unchanged identity/binding |
| WIT-15 | An explicit more-derived binding replaces an inherited default | Use that checked replacement |
| WIT-16 | Unrelated remaining defaults compete for one identity | Require explicit resolution |
| WIT-17 | Two distinct interface identities each have a valid default | Keep their separate mappings; unqualified ambiguity still diagnoses |
| WIT-18 | `.via<I>` selects one of those existing identities | No import-order selection or new conformance |
| WIT-19 | One requested type/interface pair has incompatible claims | Diagnose; `via` cannot repair it |
| WIT-20 | Interface and implementation parameter labels/defaults differ | Interface callers use interface labels/defaults; compiled calls unchanged |
| WIT-21 | A private interface helper has a body | Not an implementation requirement |
| WIT-22 | A restricted inherited requirement carries a source-unit/type principal | Retain that principal; no access expansion by inheritance |
| WIT-23 | Explicit associated binding and replaceable default coexist | Use the explicit binding |
| WIT-24 | Associated `Item` is selected through two unrelated witnesses | Require explicit witness selection |
| WIT-25 | `where T.via<I>.Item is U` with `Item=int32` and an alias of `int32` | Succeed after ordinary resolution/normalization |
| WIT-26 | The same clause uses a nominal wrapper or differing qualification | Reject unequal complete identity |
| WIT-27 | Associated identity is the only purported evidence for unknown `T` or `U` | Do not invert it to infer an implementer/type |
| WIT-28 | Binding/default expansion recursively grows generic arguments | Reject nonfinite synonym/projection dependency |
| WIT-29 | A binding names a finite nominal recursive-data type | Preserve the nominal anchor; do not expand its fields as a synonym |

## Qualified self projection

Here `IEqual<this T>` requires a readonly `bool Equals(T)` operation.
`C : IEqual<C>` has a matching published readonly target unless stated
otherwise. `C` is an ordinary Copy class-handle form.

| Case | Situation | Required result |
|---|---|---|
| WPROJ-01 | `Same<T>` infers `write C` and requires `IEqual<T>` | Keep `T=write C`; permit the checked existing-witness projection |
| WPROJ-02 | The interface's `T` is an ordinary formal, not `<this T>` | No self projection merely because the bound argument is `C` |
| WPROJ-03 | `IGetSelf<this T>` requires `T GetSelf()`, implemented with declared result `C` | Reject projection to `IGetSelf<write C>`; no authority upgrade |
| WPROJ-04 | That implementation body happens to construct a writable result | Still reject when its published result contract is only `C` |
| WPROJ-05 | A selected qualification-polymorphic generic default returns its declared `T` | Instantiate normally at `write C` if every declared constraint holds |
| WPROJ-06 | A concrete readonly `C` override replaces that generic default | Reject incompatible projection; do not choose the old default |
| WPROJ-07 | A required parameter's adaptation would need stronger access than supplied | Reject whole projection |
| WPROJ-08 | A selected target requires receiver authority absent from the requested contract | Reject; no inferred receiver upgrade |
| WPROJ-09 | Receiver changes from `C<write D>` to `write C<write D>` | Keep the inner `write D` argument unchanged |
| WPROJ-10 | Requested self has different closed nominal arguments | Not an access-only self projection |
| WPROJ-11 | An ordinary interface type argument happens to equal the original self | Keep that argument; do not retarget it |
| WPROJ-12 | An ordinary interface constant argument differs | Reject identity mismatch; projection does not infer/rewrite constants |
| WPROJ-13 | Requested self changes representation, ownership, pin, or another capability | Not this outer-access projection |
| WPROJ-14 | A unit parameter/result occurs in a projected member | Preserve its complete type, position, mode, and logical arity |
| WPROJ-15 | A member returns a view with a published backing relationship | Forward every source/root/lease obligation; no shortened or invented dependency |
| WPROJ-16 | Original `init ref<C>` would have to satisfy requested `init ref<write C>` | Reject differing destination types; no covariant output storage |
| WPROJ-17 | Original `ref<C>` would have to become `ref<write C>` | Reject changed stored inner type, despite readonly outer access |
| WPROJ-18 | An eligible unique input is forwarded | Preserve explicit transfer exactly once; no duplicate copy or extra lifetime |
| WPROJ-19 | A projected contract's unused owning result is ignored by its forwarding operation | Reject implicit discard; result/cleanup obligation must be preserved |
| WPROJ-20 | Repeated receiver/argument evaluation would make an adapter work | Reject; ordinary one-evaluation staging applies |
| WPROJ-21 | `Item=C` is the selected associated binding | Retain `C`; do not choose a new default when self becomes `write C` |
| WPROJ-22 | A requested relation requires that retained `Item` equal the new self | Reject if complete identities differ |
| WPROJ-23 | A requested self fails an interface generic constraint | Reject projection; no arbitrary witness or type search |
| WPROJ-24 | A conditional member refinement is needed but its condition is not established | Do not treat it as an unconditional projection guarantee |
| WPROJ-25 | Equality adapts safely but another unused required member cannot adapt | Reject projection of that whole interface |
| WPROJ-26 | A smaller existing base interface has a fully safe closure | Its separately checked projection may remain valid |
| WPROJ-27 | `.via<IEqual<write C>>` is used without an original declared witness | Diagnose missing witness; selector creates none |
| WPROJ-28 | An exact requested witness already exists | Retain it; no projected replacement or overload-quality bonus |
| WPROJ-29 | Explicit `Use<C>(writableValue)` uses the original readonly self contract | Ordinary access attenuation; not retargeting inferred `T` |
| WPROJ-30 | Several paths reach the same origin/target projection | Deduplicate it |
| WPROJ-31 | Distinct available origins claim one complete projected interface incompatibly | Diagnose; no import/default/physical-cost winner |
| WPROJ-32 | An inherited base expression depends on the declared self formal | Substitute that dependence and check its complete target requirements |
| WPROJ-33 | Such substitution changes an invariant nested container input/result | Reject when identity/allowed attenuation cannot supply its contract |
| WPROJ-34 | A finite recursive self-constraint graph has a nominal origin and all local obligations hold | Close the anchored graph; no fabricated new witness |
| WPROJ-35 | Recursion is the only evidence, or one member/constraint remains unresolved | Reject unrooted/incomplete conformance |
| WPROJ-36 | Two erased values expose the same interface name | No shared hidden self identity or self projection from name equality |

## Publication and binary consumption

| Case | Situation | Required result |
|---|---|---|
| META-WIT-01 | An independent consumer needs a projected witness | Require complete origin, requested types, mappings, adaptations, and rule support |
| META-WIT-02 | Only the old unqualified interface contract is retained | Do not pretend it encodes the qualified projection |
| META-WIT-03 | Published records change only nominal/ABI names, not required contracts | Names/layout alone do not establish compatibility |
| META-WIT-04 | A replacement preserves the original witness but breaks a retained projected obligation | Reject that projected binding |
| META-WIT-05 | A consumer trusts the producer's checked witness/projection facts | Still validate bounded records, references, supported semantics, and binding compatibility |
| META-WIT-06 | A linker/JIT discovers a more favorable private body/default | Do not redo source witness or overload selection |
| META-WIT-07 | Required witness/projection semantics are unrecognized | Reject before use, not optional metadata skipping |
| META-WIT-08 | The existing bounded binary schema has no assigned witness encoding | No new table/flag/opcode alias; use requires a separately complete supported encoding |
