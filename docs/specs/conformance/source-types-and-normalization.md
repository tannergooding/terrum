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
| TYPE-24 | Alias expands to `gc ref<T>` before qualifier checking | Preserve its exact-place root and GC backing identity |
| TYPE-25 | `gc ptr<T>`, `gc own<T>`, `gc int32`, or `gc ref<init T>` | Reject; GC backing is limited to class handles, boxes and initialized roots |
| TYPE-26 | Substitution combines normal and GC backing for one class/box/reference form | Reject; do not normalize away the distinct backing contract |
| TYPE-27 | Repeated `write` or `gc` prefixes are written outside the grammar | Reject source syntax |
| TYPE-28 | `ref<void>` is used as an ordinary reference to unit storage | No eligible exposed payload place |
| TYPE-29 | An explicitly opaque foreign `ptr<void>` is declared | Keep its opaque pointer contract; not a unit payload address |
| TYPE-30 | `box<gc C>` versus `gc box<C>` | Preserve distinct complete types: normal box of a GC handle versus GC-backed box of `C` |
| TYPE-31 | `box<T>(value)` is contextually typed as `gc box<T>` | Select GC backing explicitly from the expected complete type; do not insert implicit boxing |
| TYPE-32 | `gc box<T>(value)` has no expected type | Select the explicitly requested GC-backed box |
| TYPE-33 | `gc C?` or `gc ref<T>?` | Preserve optional presence around the complete GC-backed form |

## Initialization-qualified element views and SIMD comparisons

| Case | Situation | Required result |
|---|---|---|
| SPAN-TYPE-01 | `Span<init int32>` | Admit the Span family's whole-range initialization view; `init` applies to elements, not the descriptor |
| SPAN-TYPE-02 | `write Span<init T>` versus `Span<init write T>` | Preserve outer whole-element write versus inner post-initialization mutation |
| SPAN-TYPE-03 | Standalone `init T` or `List<init T>` | Reject; generic `init` arguments are not general type qualifiers and are admitted for Span's range contract only |
| SPAN-TYPE-04 | A range contains live elements and is viewed as `Span<init T>` | Reject formation; the whole range must be vacant |
| SIMD-TYPE-01 | Compare `simd128<float32>` operands | Return the same `simd128<float32>` complete type, with each lane's bits all zero or all one |
| SIMD-TYPE-02 | Use `simd128mask<float32>` as a distinct type | Reject; there is no separate user-visible SIMD mask family |
| SIMD-TYPE-03 | Lower a vector comparison to a predicate register | Preserve the same source/metadata type; target predicate representation does not create a mask type |

## Generic existential views

Assume the generic constructor, values and selected operation contracts are
otherwise admitted. Existential views preserve each value's hidden complete
argument and required evidence without creating a copy or a new owner.

| Case | Situation | Required result |
|---|---|---|
| TYPE-30 | A value of nominal `G<T>` is viewed as `G<_>` in an admitted type position | Hide the argument while retaining its exact type and required evidence per value; do not erase it to `void` |
| TYPE-31 | `List<G<_>>` contains values with different hidden arguments | Preserve independent per-value correlations, not one list-wide hidden `T` |
| TYPE-32 | A member's complete signature or contract depends on hidden `T` | Expose it through `G<_>` only when the constructor's constraints and retained evidence make it valid for every admitted hidden argument |
| TYPE-33 | A successful `G<var T>` type pattern matches a `G<_>` value | Bind its exact hidden argument and required evidence in that branch only |
| TYPE-34 | Two unrelated generic constructors have same-named members or equal layouts | Preserve nominal and witness identity; do not adapt one existential view to the other |
| TYPE-35 | A caller requests general runtime type/witness inspection of `G<_>` | Provide no such general API; use `G<var T>` for branch-local recovery, while contract-specific projections remain separately governed |
| TYPE-36 | `G<int32, bool>` is viewed as `G<int32, _>` | Keep `int32` statically exact and hide only the second argument |
| TYPE-37 | Two values are viewed as `G<int32, _>` with different second arguments | Preserve each value's independent hidden argument and evidence |
| TYPE-38 | Values of `G<int32, bool>` and `G<bool, char8>` are viewed as `G<_, _>` | Preserve each complete pair per value; do not infer one hidden pair for the artifact or collection |
| TYPE-39 | A `G<bool, _>` value is used where `G<int32, _>` is required | Reject the mismatch in the visible slot; existential hiding does not widen it |
| TYPE-40 | `G<var T, var U>` successfully matches a `G<_, _>` value | Bind both exact hidden arguments and their required evidence only in the successful branch |
| TYPE-41 | `G<int32, var U>` matches a `G<int32, _>` value | Preserve the exact visible first argument and bind only the hidden second argument |
| TYPE-42 | A multi-slot existential type pattern fails | Do not expose any captured type or evidence after the failed branch |

## Generic formal domains and specialization

Assume each named type form is otherwise eligible. Generic tests apply to
formal domains and retain the exact substituted complete type.

| Case | Situation | Required result |
|---|---|---|
| GDOMAIN-01 | Unmarked type formal receives an ordinary Copy-capable non-null form | Admit it without an explicit `[Copy]` constraint |
| GDOMAIN-02 | Unmarked type formal receives a unique or nullable actual | Reject unless the corresponding domain modifier is present |
| GDOMAIN-03 | `where T : unique` receives a unique actual | Admit the unique-only domain |
| GDOMAIN-04 | `where T : unique` receives a non-unique Copy-capable actual | Reject |
| GDOMAIN-05 | `where T : unique?` receives either a default-domain or unique actual | Admit both without erasing the actual's identity |
| GDOMAIN-06 | `where T : null?` receives a nullable complete type | Admit it while preserving the exact optional substitution |
| GDOMAIN-07 | A formal admits both `unique?` and `null?`, with `T = UniqueC?` | Admit the nullable unique form and preserve both option presence and unique ownership |
| GDOMAIN-08 | An optional value is tested for presence inside a generic body | Refine only that value/place; do not change T or other values |
| GDOMAIN-09 | Same-signature overloads differ by default-domain versus `unique` constraints | Admit only when the domains are proven disjoint by closed, exhaustive properties |
| GDOMAIN-10 | Constraint-only overload domains may overlap or cannot be proven disjoint | Reject the overload set |
| GDOMAIN-11 | A generic body over `unique?` calls one default-only or unique-only overload without a branch | Reject because selection is not deferred until a future actual |
| GDOMAIN-12 | The body branches with `if (T is unique)` | Check both arms under refinement; resolve by specialization or shared/canonicalized generic execution, not general reflection |
| GDOMAIN-13 | The true arm of `if (T is unique)` | Carry proof/witness evidence for uniqueness |
| GDOMAIN-14 | The false arm under `T : unique?` | Establish the default Copy-capable domain because that domain is closed and exhaustive |
| GDOMAIN-15 | A property test's false arm is not closed/exhaustive | Exclude only the tested property; invent no complement |
| GDOMAIN-16 | One closed instantiation satisfies a generic body's unproved requirement | Do not treat that instance as proof that the generic definition satisfies its domain |
| GDOMAIN-17 | An imported property check lacks required typed property/witness evidence or supported shared/specialized execution | Reject or decline affected use; do not infer a fact from a name or one successful instance |
| GDOMAIN-18 | `bool enabled = T is unique;` is followed by `if (enabled)` without changing `enabled` | Preserve the tested-property correlation and refine the true/false paths |
| GDOMAIN-19 | A mutable saved Boolean or join has multiple reaching definitions | Refine only when every relevant definition/path proves the same property or a sound combination; no arbitrary Boolean theorem proving |
| GDOMAIN-20 | Shared generic code evaluates `T is unique` in Boolean value context | Produce a runtime Boolean using compact implementation-provided type metadata, not general reflection |
| GDOMAIN-21 | A generic formal declares `where T : [Sized]` | Reject the unsupported property; every type has a size, while safe layout observation requires `Blittable` evidence |

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
| WRITE-28 | Declarations have equal normalized signatures and differ only by this constraint | Admit only if the complete generic applicability domains are proven pairwise disjoint; `write` alone does not imply such a proof |
| WRITE-29 | Requirement is applied to a constant formal | Reject wrong formal kind |
| WRITE-30 | Consumer imports a generic declaration or selected witness with the requirement | Retain its formal identity and required check; unassigned encoding is not permission to omit it |
| WRITE-31 | Requirement is used to reinterpret writable storage or remove a loan/guard restriction | Reject; inner storage remains invariant and access checks remain independent |

## Optional and representation normalization

| Case | Situation | Required result |
|---|---|---|
| NORM-01 | Substitute `T=int32?` into `T?` | One presence layer, `int32?` |
| NORM-02 | `option<option<int32>>` | `int32?` |
| NORM-03 | Written `int32??` or `T??`, even with trivia between markers | Reject source form |
| NORM-04 | `List<int32?>?` | Retain separate nested optional positions |
| NORM-05 | Optional vacancy versus absent live optional | Distinct initialization/presence states |
| NORM-06 | `option<void>` | Absent versus present unit; no second unit value |
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
| NORM-20 | `option<T>` versus `T?` | Same optional-presence type identity |
| NORM-21 | `option<option<T>>` | One normalized presence layer |
| NORM-22 | Null supplies `option<C>` for a managed class handle C | Null means absence; no present-null payload or implicit `Some(null)` |
| NORM-23 | A user needs an empty/null-like class value inside `option<C>` | Require an explicit sentinel object for the present value |
| NORM-24 | Null supplies `option<ref<T>>` or `option<ptr<T>>` | Null means absence; a present reference/pointer retains its full contract |
| NORM-25 | A present `option<ref<T>>` crosses a loan/backing boundary | Preserve descriptor, backing, provenance and lifetime obligations |
| NORM-26 | `ref<T?>` versus `option<ref<T>>` | Borrow an optional slot versus hold an optional reference; retain the distinct nesting |
| NORM-27 | Type shorthand `T**` | Desugar to `ptr<ptr<T>>` |
| NORM-28 | Type shorthand `T*?` | Desugar to `option<ptr<T>>` |
| NORM-29 | Type shorthand `T?*` | Desugar to `ptr<option<T>>` |
| NORM-30 | `write C*` and `write T**` | Apply `write` to the complete postfix result: `write ptr<C>` and `write ptr<ptr<T>>` |
| NORM-31 | `write T**` versus `ptr<write T*>` | Preserve writable outer pointer-cell access versus writable inner T referent |
| NORM-32 | `T write*` or ungrouped `ref ref T` | Reject; access remains prefix-only and nested prefix constructors require grouping |
| NORM-33 | Unqualified `ptr<T>` is formed or imported | Preserve its non-null pointer contract; absence is not a present null pointer value |
| NORM-34 | `ptr<T>?` is checked for presence before use | Refine only the pointer descriptor to present; retain the pointee's unsafe validity/lifetime obligations |
| NORM-35 | `ptr<T?>` is used | Preserve a non-null pointer to an optional `T` slot, distinct from `ptr<T>?` |
| NORM-36 | `ptr<T?>?` is used | Preserve both optional pointee-slot state and optional pointer-descriptor presence |
| NORM-37 | `T?*?` is written | Desugar to `option<ptr<option<T>>>` with both optional layers intact |

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
| TSHAPE-12 | A capture/effect bound is required by target formation | Check that guarantee independently; shape alone does not prove it |
| TSHAPE-13 | Effects, labels or defaults alone try to distinguish ordinary overloads | Reject contract-only duplicate signature |
| TSHAPE-14 | A capturing lambda is targeted as `fnptr` | No hidden environment or allocated adapter from matching shape |
| TSHAPE-15 | Alias of a callable signature | Preserve its full signature/convention and required contracts |
