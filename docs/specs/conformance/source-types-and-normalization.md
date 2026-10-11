# Complete-type and normalization conformance cases

These cases apply [complete types](../language/types-and-normalization.md),
source binding, and the selected constructor contracts. They are specification
obligations, not executed compiler tests. Assume otherwise eligible payloads,
valid registrations, constraints, and target support. `C` is a compatible
ordinary managed class, `S` an inline Copy payload, and `D` another class.

## Identity, qualification, and finite substitution

| Case | Situation | Required result |
|---|---|---|
| TYPE-01 | Equal normalized atomic identities | Equal complete type |
| TYPE-02 | Equal storage width but different scalar identities | Unequal types |
| TYPE-03 | Two nominal wrappers have equal fields/carrier ABI | Unequal nominal types |
| TYPE-04 | Two zero-payload nominal types or a nominal type versus `void` | Preserve distinct identities |
| TYPE-05 | Same nominal declaration with different complete type arguments | Unequal constructed types |
| TYPE-06 | Same value bits in different nominal constant domains | Preserve argument domain/identity |
| TYPE-07 | Source formals are renamed consistently | Same formal positions/signature; spelling does not create identity |
| TYPE-08 | File-local definitions have equal names in different source units | Preserve semantic source-unit identity |
| TYPE-09 | Transparent alias of a qualified target | Same complete target, including inner qualifications |
| TYPE-10 | Direct or indirect alias-declaration cycle | Reject with its dependency chain |
| TYPE-11 | Alias cycle grows type arguments rather than repeating an instance | Still reject nonfinite expansion |
| TYPE-12 | `Identity<Identity<int32>>` with `alias Identity<T> = T;` | `int32`; finite argument nesting is not a declaration cycle |
| TYPE-13 | Alias names a finite recursive nominal-data type | Stop at the nominal anchor; do not expand fields |
| TYPE-14 | Generic substitution receives `write C` | Preserve that complete `T`, not `C` |
| TYPE-15 | A formal-dependent constructor cannot yet be reduced | Retain its symbolic term/formation obligations; do not guess an implementation |
| TYPE-16 | A constraint or required constructor capability fails | Diagnose; normalization does not waive eligibility |
| TYPE-17 | `mut` appears as a type argument | Reject binding authority in type position |
| TYPE-18 | `write int32` or `write S` for an ordinary inline payload | Reject writable-value type; use a writable place |
| TYPE-19 | `write C` on a live handle | Retain only its permitted object-member access; no slot replacement |
| TYPE-20 | `write ref<C>` versus `ref<write C>` | Unequal; slot access and contained object access remain separate |
| TYPE-21 | `write Span<C>` versus `Span<write C>` | Unequal; element-slot versus contained-handle access |
| TYPE-22 | A readonly aggregate/box holds an independently writable view/handle | Retain that inner capability, without aggregate-slot mutation |
| TYPE-23 | Generic `write T` has no formation guarantee at its use | Reject the unproved form; no universal mutation or specialization-time repair |
| TYPE-24 | Alias expands to a legal pinned reference before qualifier checking | Check the normalized `ref` form and retain its pin dependency |
| TYPE-25 | `pin ptr<T>`, `pin C`, or pin on another non-reference form | Reject; pin qualifier applies only to `ref<T>` |
| TYPE-26 | Substitution repeats an admitted write/pin qualification | Retain one semantic qualifier; no new authority or pin acquisition |
| TYPE-27 | Repeated prefix tokens are written outside the grammar | Reject source syntax despite semantic idempotence |
| TYPE-28 | `ref<void>` is used as an ordinary reference to unit storage | No eligible exposed payload place |
| TYPE-29 | An explicitly opaque foreign `ptr<void>` is declared | Keep its opaque pointer contract; not a unit payload address |

## Generic write requirements

These cases apply `where T : write` to the normalized complete argument,
not the access of a particular source binding. Assume the stated reference
and view forms are otherwise eligible. `Forward<T>` denotes the constrained
example in the [type chapter](../language/types-and-normalization.md#generic-write-requirements).

| Case | Situation | Required result |
|---|---|---|
| WRITE-01 | `T = write C` | Satisfy the requirement; preserve complete `T` |
| WRITE-02 | `T = C`, including a class declared `mut` | Reject readonly type; nominal mutation eligibility is not actual write access |
| WRITE-03 | `T = write ref<C>` | Satisfy immediate referenced-slot access |
| WRITE-04 | `T = ref<write C>` | Reject; independently writable contained handle is not writable reference access |
| WRITE-05 | `T = write ptr<S>` or `write own<S>` | Satisfy eligible indirection access, without waiving pointer or ownership rules |
| WRITE-06 | `T = write Span<C>` | Satisfy a registered writable view's immediate access |
| WRITE-07 | `T = Span<write C>` | Reject readonly view despite writable contained handles |
| WRITE-08 | `T = write C?` | Satisfy access of the present type; do not infer presence or initialization |
| WRITE-09 | `T = C?` | Reject readonly present type |
| WRITE-10 | A mutable local holds readonly `C` or ordinary inline `S` | Binding replacement does not satisfy a type write requirement |
| WRITE-11 | Writable unique owner satisfies the requirement | Do not infer Copy, pin, thread capabilities or duplication permission |
| WRITE-12 | Body invokes an undeclared mutation operation using only the write requirement | Reject missing operation/member contract |
| WRITE-13 | Explicit writable type argument is supplied by a readonly value | Reject unavailable value authority even though the type meets the requirement |
| WRITE-14 | `Forward<C>(writableValue)` | Reject explicit readonly `T`; do not rewrite it to `write C` |
| WRITE-15 | Ordinary inference supplies `T = write C` | Keep that complete type and check the requirement |
| WRITE-16 | Ordinary inference supplies readonly `T` | Reject the requirement; no inferred box, loan or access upgrade |
| WRITE-17 | Body writes redundant `write T` under `where T : write` | Same complete qualified `T`, with all inner qualifications retained |
| WRITE-18 | Published signature uses unproved generic `write T` but all current calls happen to be writable | Reject the declaration; current instantiations are not its guarantee |
| WRITE-19 | Call supplies `Forward<write U>` | Check that type argument under caller facts; do not add a constraint to the callee's generic body |
| WRITE-20 | Generic formal is declared as `<write T>` | Reject unselected formal shorthand |
| WRITE-21 | `where T is write` is written | Reject unsupported constraint form; associated `is` still compares complete types |
| WRITE-22 | Constrained formal has a default `write C` versus a default `C` | Admit the eligible writable default; reject a readonly default without auto-qualification |
| WRITE-23 | Caller forwards `U` with a declared requirement that entails write | Use that published guarantee without changing `U` |
| WRITE-24 | Caller has only an unconstrained `U` or a behavioral witness not promising write | Reject an unproved requirement; no private-layout or specialization proof |
| WRITE-25 | Alias resolves to `write C` | Satisfy after transparent expansion |
| WRITE-26 | `box<write box<S>>` versus `write box<box<S>>` supplies `T` | First normalizes readonly and fails; second normalizes writable and satisfies |
| WRITE-27 | Readonly box or nominal cell contains a writable view/handle | Reject an immediate write requirement; do not search its payload |
| WRITE-28 | Declarations have equal normalized signatures and differ only by this constraint | Reject constraint-only overloading; no better conversion rank |
| WRITE-29 | Requirement is applied to a constant formal | Reject wrong formal kind |
| WRITE-30 | Consumer imports a generic declaration or selected witness with the requirement | Retain its formal identity and required check; unassigned encoding is not permission to omit it |
| WRITE-31 | Requirement is used to reinterpret writable storage or remove a loan/guard restriction | Reject; inner storage remains invariant and access checks remain independent |

## Optional and representation normalization

| Case | Situation | Required result |
|---|---|---|
| NORM-01 | Substitute `T=int32?` into `T?` | One presence layer, `int32?` |
| NORM-02 | `Option<Option<int32>>` for `alias Option<T> = T?;` | `int32?` |
| NORM-03 | Written `int32??` or `T??`, even with trivia between markers | Reject source form |
| NORM-04 | `List<int32?>?` | Retain separate nested optional positions |
| NORM-05 | Optional vacancy versus absent live optional | Distinct initialization/presence states |
| NORM-06 | `Option<void>` | Absent versus present unit; no second unit value |
| NORM-07 | Nominal `Missing` versus `Found(null)` | No normalization of distinct union cases |
| NORM-08 | `ref<T?>` versus `ref<T>?` | Optional slot versus optional reference; unequal |
| NORM-09 | `own<T?>` versus `own<T>?` | Different complete storage/ownership forms |
| NORM-10 | `ptr<T?>` versus `ptr<T>?` | No invented commutation with presence |
| NORM-11 | `write C?` | Optional writable handle; presence still checked |
| NORM-12 | `val<S>` with already-inline eligible `S` | Same inline type, no extraction |
| NORM-13 | `val<box<S>>` | Eligible payload type `S`, not a managed handle value |
| NORM-14 | `val<write box<S>>` | Payload type, not inherited box-write authority |
| NORM-15 | `val<C>` without a published inline-payload contract | Diagnose unsupported formation; do not inspect private fields for eligibility |
| NORM-16 | Eligible `val<C>` versus class-handle `C` | Distinct representations/contracts |
| NORM-17 | `write val<C>` for ordinary inline payload | No writable-value default; use place/loan authority |
| NORM-18 | An unrelated family has matching name/arity or `F<void>` member | No universal erasure, nesting collapse, or covariance |
| NORM-19 | Inference tries to invert optional/box/associated normalization | No guessed original type |

## Managed boxing and requested access

| Case | Situation | Required result |
|---|---|---|
| BOX-01 | `box<C>` for compatible ordinary class `C` | Readonly `C` |
| BOX-02 | `box<write C>` | Readonly `C`; same managed identity with requested access |
| BOX-03 | `write box<C>` | `write C` type; supplying value still needs authority |
| BOX-04 | `write box<write C>` | `write C`; no duplicated qualifier |
| BOX-05 | `box<box<S>>` | `box<S>`; no second box/handle slot |
| BOX-06 | `box<write box<S>>` | Readonly `box<S>` |
| BOX-07 | `write box<box<S>>` | `write box<S>` type |
| BOX-08 | `write box<write box<S>>` | `write box<S>` type |
| BOX-09 | Substitute `U=write box<S>` into `box<U>` | Readonly `box<S>`; original `U` remains writable |
| BOX-10 | The resulting readonly box is compared with `write box<S>` | Unequal complete types; attenuation is not exact type identity |
| BOX-11 | An existing writable box is viewed through that readonly result | Preserve box identity, backing, roots and lifetime without allocation |
| BOX-12 | A readonly value supplies a requested writable normalized box | Reject authority upgrade |
| BOX-13 | Type normalization or alias expansion occurs | No runtime allocation, copy, transfer or clone |
| BOX-14 | An explicit boxed-value operation receives eligible inline `S` | Its declared construction/allocation/failure/cleanup contract applies |
| BOX-15 | `box<S?>` | `box<S>?`; preserve absence, no fabricated `S` |
| BOX-16 | `write box<S?>` | Optional writable `box<S>`; no access while absent |
| BOX-17 | `box<write box<S>?>` | Optional readonly `box<S>` |
| BOX-18 | `box<write Span<S>>` | Readonly managed box retaining a writable span payload |
| BOX-19 | `write box<Span<S>>` | Box payload's permitted writes, not automatic writable span elements |
| BOX-20 | Nominal cell has a `write box<S>` field and is boxed readonly | Preserve that field type; no automatic rewriting inside the cell |
| BOX-21 | A distinct replaceable slot containing a box is required | Use explicit nominal storage; redundant compatible boxing is not that slot |
| BOX-22 | Ownership/lifetime representation is not a compatible existing managed box | No collapse by name; require its published compatible contract |
| BOX-23 | An independent default-readonly generic `F<U>` receives writable `U` | Keep complete `U`; only explicitly requested `box<U>` attenuates boxing access |

## Tuple and callable shape

| Case | Situation | Required result |
|---|---|---|
| TSHAPE-01 | Tuple labels differ but ordered complete element types agree | Same type; retain caller-visible source-view labels |
| TSHAPE-02 | Tuple order/arity differs | Unequal types |
| TSHAPE-03 | `(void, int32)` versus `int32` | Preserve unit position and tuple identity |
| TSHAPE-04 | `()` | Unit, not zero-arity tuple |
| TSHAPE-05 | One tuple element is unique | No Copy tuple merely because the remaining elements copy |
| TSHAPE-06 | `func<void>` versus `func<void, void>` | Zero versus one logical unit parameter; unequal signatures |
| TSHAPE-07 | `func<A, R>` versus `fnptr<A, R>` | Distinct callable representations |
| TSHAPE-08 | Callable parameter labels differ with equal complete signature | Same invocation shape; calls use statically visible labels |
| TSHAPE-09 | Callable parameter mode differs | Distinct logical signature; no by-value alias |
| TSHAPE-10 | Bare `func<ref<T>, void>` receives an initialization-mode lambda | Reject mode mismatch; type argument denotes an ordinary descriptor-value parameter |
| TSHAPE-11 | Function-pointer calling conventions differ | Unequal complete types; no implicit ABI cast |
| TSHAPE-12 | A declared NoThrow/capture bound is required by target formation | Check that guarantee independently; shape alone does not prove it |
| TSHAPE-13 | Effects, labels or defaults alone try to distinguish ordinary overloads | Reject contract-only duplicate signature |
| TSHAPE-14 | A capturing lambda is targeted as `fnptr` | No hidden environment or allocated adapter from matching shape |
| TSHAPE-15 | Alias of a callable signature | Preserve its full signature/convention and required contracts |
