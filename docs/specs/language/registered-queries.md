# Registered queries

This chapter owns the selected Core value-layout queries and the shared
identity/admission boundary for compiler-known queries. Logical
`registered-queries/1` adds no source keyword, query attribute, intrinsic wire
ID or opcode. [Complete types](types-and-normalization.md), the
[target layout contract](../data-layout-and-abi.md), and
[constant execution](proof-and-constant-evaluation.md) retain their separate
responsibilities.

## Selected identities and authority

Compiler recognition follows the resolved declaration identity and its
admitted contract, never a member's spelling. Ordinary library lookup,
accessibility, generic argument binding and signature checking still apply.
Aliases or repeated imports of one declaration preserve that identity;
unrelated declarations with equal names/signatures do not acquire it.

| Selected operation | Subject and result | Canonical contract |
|---|---|---|
| Reserved `typeof(Type)` syntax | Complete bound type-identity evidence, not an ordinary reflection-object constant | [Source vocabulary](../grammar-spec.md#source-vocabulary) and [structural flow](initialization-and-flow.md#binding-pattern-declarations-and-scopes) |
| Core `RuntimeHelpers.IsPureEval` | Selected activation's immutable evaluation form; readonly Boolean result | [Evaluation modes](published-predicates-and-effects.md#evaluation-modes) |
| Core `RuntimeHelpers.IsConstEval` | Selected activation's immutable evaluation form; readonly Boolean result, implying pure mode when true | [Evaluation modes](published-predicates-and-effects.md#evaluation-modes) |
| Core `RuntimeHelpers.SizeOf<T>()` | Complete normalized value representation of T; `nuint` byte extent | [Value-layout queries](#value-layout-queries) |
| Core `RuntimeHelpers.AlignmentOf<T>()` | The same complete representation of T; `nuint` alignment in bytes | [Value-layout queries](#value-layout-queries) |

The designated compiler/Core profile publishes the exact intrinsic type/member
identities, signatures, supported forms and recipes. This is different from
target-owned literal or builder registration: another package cannot attach
compiler query semantics to a member it defines or to a foreign target.
There is no general source `[Query]` registration schema. A Core dependency
does not admit every candidate query or automatically make its facilities
part of the minimum `language` profile.

One selected identity has one compatible contract. Duplicate claims with
incompatible owners, signatures, rules or recipes reject; import order,
same-spelled namespaces and competing implementations cannot select a winner.
Two imports of the same compatible claim deduplicate. A source call bound to
an unrelated helper remains an ordinary call, including its actual effects
and constant eligibility; it is not silently rebound to Core.

Declaration selectors such as the `nameof(parameter)` arguments of admitted
dependency/capture schemas keep their [declaration-binding rules](names-and-binding.md#generic-formal-names-and-scope).
They do not evaluate a parameter or read a vacant output. This chapter does
not select a general string-producing `nameof` API, object-payload query,
reflection root, policy-state query or runtime capability-query catalog.
Each additional query needs its own exact subject, result domain, authority,
forms, dependencies and compatibility contract before use.

## Value-layout queries

The selected Core `RuntimeHelpers` members have these reference signatures:

| Member | Complete requirement |
|---|---|
| `public static nuint SizeOf<T>() where T : [Sized]` | One type formal, no receiver/value parameters, by-value `nuint` result |
| `public static nuint AlignmentOf<T>() where T : [Sized]` | The same formal/input/result shape, observing alignment instead of extent |

These are ordinary generic method calls, not `sizeof`/`alignof` syntax.
The signatures describe admitted intrinsic implementations; they do not
permit bodyless concrete methods in runnable source.
Their exact constant intrinsic contracts supply checked pure and required
constant forms. No mutable static field, static initialization group,
constructor, formatter, allocation, reflection object or source T value is
created or observed by either query.

`[Sized]` is the existing checked property of the complete qualified form,
not a same-named user attribute. T must first satisfy ordinary type formation
and evidence requirements. In a generic body, a declared `[Sized]` guarantee
supports runtime queries:

```terrum
public nuint ElementSize<T>() where T : [Sized]
{
    return RuntimeHelpers.SizeOf<T>();
}
```

The runtime can supply admitted layout evidence for the actual complete T;
machine-code sharing does not require a universal compile-time number.
A body lacking the required guarantee cannot rely on later specializations
to repair its source contract. Nor can `SizeOf()` infer T from its `nuint`
result: T occurs in no value parameter or result-type shape supplying such
evidence. Ordinary explicit type arguments, forward substitution and checked
captured type evidence remain available.

Normalize aliases, constructed arguments, optionals and requested
representations before selecting layout. Preserve complete nominal identity,
access and representation; do not implicitly select a pointee, inline
payload, runtime derived type or convenient carrier.

| Complete subject | Measured representation |
|---|---|
| Scalar, enum, inline struct or `val<C>` | Its selected target value storage, including any layout padding |
| Class handle C or a normalized compatible managed box | The handle value, not the allocated object's payload/header |
| `ref<T>`, `ptr<T>`, `own<T>` or a registered view | That complete descriptor/handle representation, not reachable storage or backing length |
| Optional or tuple | The actual complete optional/tuple representation, including admitted tag/layout rules |
| Checked Transparent wrapper | Its admitted carrier-equivalent representation, retaining its nominal identity |
| `void` | Zero payload extent; alignment follows the target's explicit zero-data value rule |

Thus `SizeOf<MyClass>()` does not sum class fields, and `SizeOf<Span<int32>>()`
does not multiply an instance's Length by four. A `ref<T>` need not occupy
one pointer, and no universal handle/reference width is invented here.
`SizeOf<val<MyClass>>()` instead selects the independently eligible inline
representation. Querying an owning/non-Copy form does not copy or transfer
an owner; subject eligibility does not require a T constant-value codec.

A bare interface, unapplied generic constructor, missing type witness,
unsupported representation or genuinely sizeless scalable value is not an
admitted sized subject. Equal size/alignment results prove neither type
identity, Copy, Blittable, atomicity, source conversion nor reinterpretation
permission. Reading a type's size does not expose its private field identities,
offsets, mutable state or construction/validity implementation.

## Target values and intrinsic execution

Both queries use one coherent admitted layout for T under the selected
target ABI/profile and representation dependencies. The evaluator's host,
debug/release setting, register allocation and current machine instruction
selection do not supply their values.
Size is the byte extent of one value's storage representation, including
its target-prescribed interior/tail padding. It is not a procedure-call
register slot, serialized length, allocation header or total reachable size.
Array/element stride and zero-data storage retain their profile rules;
the query does not invent a positive-size unit object.

`SizeOf<int32>()` is four under the mandatory primitive storage contract.
Native-sized values and handles use the selected target representation.
Alignment is that value representation's placement requirement in bytes,
not a cache-line size, allocation capacity, member packing limit or guessed
pointer width. Aggregate packing/over-alignment and Transparent carrier
rules determine the actual layout before it is queried.
The complete target table must explicitly cover admitted zero-data alignment
and descriptor/aggregate representations; an absent rule is unsupported,
not an implicit zero/one/default or host-layout answer.

Results are exact target-width `nuint` values. Size may be zero; alignment
must be positive. Both must fit the target's `nuint` range. An overflowing,
negative, contradictory or incomplete layout record cannot justify `[Sized]`
or a successful query. Admission diagnoses it before affected execution;
the APIs do not wrap, saturate or return an unsupported sentinel.
Once its complete subject/target evidence is admitted, a query is total and
has no observable mutation, language failure or escaping temporary backing.
Other arithmetic on its result retains ordinary checked arithmetic.

The intrinsic program enters the selected method, reads the chosen immutable
size/alignment fact, saves/publishes its `nuint` result, and retires the
activation. Apply the ordinary
[semantic action and storage accounting](proof-and-constant-evaluation.md#semantic-actions-and-normalization):
call entry, one scalar fact read, save/publication and logical result/frame
state. T itself is not allocated or traversed; a large hypothetical T does
not consume its size in evaluator storage.
Repeated authored queries retain those logical charges without a cache
discount. Physical lowering can load a runtime descriptor or fold an
available fact, provided it preserves the selected contracts/dependencies.
Evaluation-mode queries retain their separate Boolean getter recipes.

## Required constants and layout dependencies

Runtime query support is not a promise that every generic definition has a
constant size/alignment number. A reached required query needs exact admitted
layout evidence for its complete subject and selected target, in addition
to the intrinsic/rule/result-codec support and ordinary resource envelope.
`[Sized]` alone does not make an unresolved formal a definition-time constant.
Required failure, unavailable evidence or exhaustion diagnoses; no runtime,
default-value or different-overload fallback is permitted.
Executed-path eligibility follows [constant execution](proof-and-constant-evaluation.md#executed-path-eligibility-and-calls):
an unselected otherwise valid runtime query need not execute its constant
recipe, but ordinary type/binding/structural checks still apply.

The emitted number is a Blittable scalar. That freezes its own value
representation, not the queried type's layout. A retained layout-dependent
value or decision must satisfy one of the existing target-layout boundaries:

| Basis | Retention obligation |
|---|---|
| Published fixed representation | Record the exact complete type and transitive size/alignment commitment for the selected target; compatible replacement must preserve that observation |
| Explicit layout-coupled build/JIT region | Record every actual layout dependency and its selected revision; rebuild, rebind or invalidate dependent values/code before incompatible layout use |
| Unfrozen independently replaceable layout without coupling | Do not retain its current size/alignment as an independent layout-dependent constant |

The fixed basis can come from a primitive/handle representation contract or
the queried type's checked fixed layout. An inline Transparent carrier
relationship alone does not freeze every unpromised layout across versions.
Conversely, querying a stable class handle does not freeze the private object
payload it never measures. Per-target commitment is not cross-target equality.

Layout coupling is explicit admission, not a comment, timestamp, informal
intention to rebuild or inference from an AOT/JIT label. It propagates through
derived constants, enclosing layout/allocation decisions and other retained
uses that rely on the observed fact. A pure form, `nuint` result codec,
ordinary `const` spelling or backend fold cannot erase it.
Runtime descriptor use must agree with the selected value/code representation;
it does not justify baking a previous replaceable descriptor's value into
independent code. Reuse and value numbering account for exact query identity,
complete subject, target and every surviving representation dependency.
No source constructor, user equality or private imported body is replayed
to reconstruct these facts.

The `nuint` result remains outside the initial
[constant-generic catalog](constant-generics.md#initial-domains).
An ordinary scalar constant/default can use it; a generic constant argument
still needs its own exact eligible domain and checked conversion/formation.
No query creates a general type-object constant or object-value codec.

## Retained contracts and compatibility

| Required field | Meaning |
|---|---|
| Identity and rule | Exact Core type/member, `registered-queries/1` and the selected operation, independent of names/row numbers |
| Signature and forms | Type-formal ordinal, `[Sized]` requirement, empty value-input list, exact `nuint` result, runtime/pure/constant support and intrinsic recipe |
| Subject | Complete normalized type arguments, representation/access and available type evidence |
| Target and layout | Exact target ABI/width, coherent size/alignment facts and their transitive representation commitments or coupling dependencies |
| Retained observation | Actual chosen result/derived use, typed scalar codec and the fixed or explicitly invalidatable basis |
| Admission | Supported rules/features, complete references, contract compatibility and the existing explicit producer/audit trust policy |

An independent consumer validates required semantics, references and target
compatibility before affected use. Producer trust does not waive malformed
records, missing layout evidence or incompatible retention.
Linking, trimming and remapping preserve semantic member/type/dependency
identities; physical row reassignment, packing or debug stripping cannot
drop a surviving observation's obligations.
Changing the measured subject, result domain, intrinsic behavior or required
fixed observation is incompatible. A replaceable private layout can change
within its contract only when affected coupled consumers are regenerated or
invalidated; unrelated private field names alone do not change a retained
value-layout fact.

These [metadata requirements](../representation/logical-records.md#registered-query-contracts-encoding-pending)
are logical. No optional attribute, existing scalar constant bits, reserved
slot or name-based call encoding supplies them. Physical query/target/layout
dependency records remain separately gated.
[Conformance cases](../conformance/source-registered-queries.md) distinguish
source identity, complete subjects, target results, required evaluation and
retention from implementation or physical codec conformance.
