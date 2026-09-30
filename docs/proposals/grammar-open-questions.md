# Remaining source-language decisions

> [!IMPORTANT]
> This is the pending source-decision queue. Recommendations are review
> inputs, not adopted rules.
> The [source grammar](../grammar-spec.md) remains authoritative for settled
> forms. This inventory does not reopen those choices or authorize a compiler
> to choose new semantics on the user's behalf.

This is a decision inventory, not a compiler backlog. Stable question numbers
are retained; gaps in the pending queue are intentional. Fourteen resolved IDs
have only [canonical link redirects](#resolved-question-link-redirects) at
the end of this document, so old review links remain usable without mixing
their rules into the questions below.
Questions are grouped
by the source contract a user must choose, rather than by missing parser
productions or implementation files. The number of questions is not a target.
Examples use approved Terrum syntax where possible to expose the issue;
referenced library types and APIs are illustrative. Explicitly labeled
candidate spellings are unapproved alternatives, and an example whose legality
is the question does not assert that it is valid. No candidate establishes a
new grammar production. Method bodies omitted from surrounding
examples are not a new bodyless-concrete-declaration rule.

There are **28 pending numbered groups**. Q03 and all seven punctuation
subquestions have canonical redirects, not pending votes. Partially resolved
groups ask only about their remaining choice; settled context is linked or
labeled, not another vote.

| Pending area | Stable IDs |
|---|---|
| Declaration applicability and modifier order | Q06 |
| Extension, adapter and family declaration forms | Q16-Q18 |
| References, initialization and operators | Q19-Q26 |
| Other source forms and contracts | Q27-Q37, Q40-Q42 |
| Documentation | Q38-Q39 |

## Lexical and declaration choices

<a id="q06-visibility-and-modifier-applicability"></a>

### Q06. Visibility, modifier applicability and ordering

**Settled context:** Module-scoped visibility and declaring-type `private`
are in the [official declaration contract](../grammar-spec.md#31-source-file-structure-and-declarations).
Neither is awaiting another vote.

**Accepted direction:** Specify legal modifiers independently for each
declaration kind; a legal modifier on one kind does not become legal everywhere.
Module and declaring-type boundaries remain fixed. File-limited visibility
needs a separate concrete use/decision, not an invented friend-module domain.

**Remaining question:** Complete the concrete declaration-kind matrix:
interface-member access, static nominal containers, default access, and combinations of
`mut`, `unique`, `partial`, `unsafe` and `async`. The currently approved
prefix permits permutation; the proposed stricter order below would replace
that rule only after acceptance. Duplicates/conflicts remain invalid.
Should a separate file-limited access domain be
available, and to which declaration kinds would it apply? `file private` and
`file internal` are review ideas, not adopted modifier combinations.
**Example:** Candidate `file internal struct Helper { }` would introduce a
file-limited helper; that modifier combination is not currently adopted.

**C# model:** Partly modeled on explicit declaration modifiers. Do not import
`protected`, friend assemblies, stateful class inheritance, or C#'s different
top-level-private restrictions.

**Ordering recommendation (not yet adopted):** Use one fixed declaration-prefix
order, omitting inapplicable/absent slots:

| Order | Role | Candidate spelling |
|---|---|---|
| 1 | Targeted semantic metadata | Attribute lists |
| 2 | Visibility | `public`, `internal` or `private` |
| 3 | Placement | `static` |
| 4 | Receiver/storage access | `mut` |
| 5 | Ownership | `unique` |
| 6 | Caller obligation | `unsafe` |
| 7 | Execution form | `async` |
| 8 | Declaration composition | `partial` |

For example, `public mut unique struct Lease { }` follows the proposed order;
`unique public mut struct Lease { }` would reject under it, despite being
permitted by the current permutation rule. This table is an ordering rule,
not permission to combine every slot on every declaration.

Passing modes and type constructors are separately ordered productions,
not interchangeable declaration modifiers. `mut ref T`, `init ref<T>` and
`implicit ref<T>` have fixed roles/word order; `ref mut T` is not a reordered
passing mode. The existing type production places `write` before `pin` when
both are valid. A future two-word construct must declare its complete
production, not happen to emerge from arbitrary adjacent modifiers.
Reject a misordered prefix explicitly instead of letting type lookup reinterpret
it. Formatter output uses that same canonical sequence.

This is stricter than C#'s mixture of permutable modifiers and ordered compound
forms. Reserve semantic roles rather than assuming today's keyword adjacency
is always safe to reuse. Exact applicability/defaults and file-local access
remain separate choices; neither `file` nor a replacement modifier order is
silently approved by this recommendation.

## Types, lookup and references

### Q16. Extension declarations and call disambiguation

**Question:** Choose declaration/import scope and an explicit way to name a
selected extension when ordinary lookup is ambiguous. Extensions do not
satisfy interface witnesses; applicable members already outrank extensions.
**Example:** `value.Measure()` could have two imported extension candidates;
`value.via<IMeasure>.Measure()` instead selects a real witness, not either
unrelated extension.

**C# model:** Extension lookup is C#-inspired, but do not import C# 14 extension
block syntax or permit retroactive conformance merely because it is available.

**Settled context; syntax still open:** Keep explicit static-call qualification available and
make extension eligibility lexical, with the receiver's full access/loan
contract. Avoid a second overload-ranking system.

**Concrete candidates and callers:**

```terrum
struct MetricsA
{
    public static int32 Measure(int32 value) => value;
}
struct MetricsB
{
    public static int32 Measure(int32 value) => value + 1;
}
int32 ChooseA(int32 value) => MetricsA.Measure(value);
int32 ChooseB(int32 value) => MetricsB.Measure(value);
```

These are ordinary qualified static calls. If both methods are explicitly
made eligible as extensions in one lexical scope, `value.Measure()` has
two equally good candidates and is ambiguous; the qualified calls select
7 and 8 respectively for argument 7. An applicable ordinary instance member
wins before extension lookup. `value.via<IMeasure>` selects an actual
interface witness, not an unrelated extension. Extension declaration/import eligibility syntax remains pending;
no registration attribute or block syntax is invented by this example.

**Revised recommendation (not yet adopted):** Allow `.via<Provider>` to
select an explicitly eligible extension provider as well as retaining
`.via<I>` for witness selection. They share the meaning "select this declared
behavior route"; provider selection does not invent an interface conformance.
Initially use a nominal non-interface provider container, keeping the
resolved selector kinds disjoint. It still needs explicit lexical extension
eligibility and ordinary access/loan checks; naming a container does not
expose every static/private member as an extension.

With the one-parameter helpers above:

```terrum
// CANDIDATE provider selection; eligibility declaration remains open.
int32 ExplicitA(int32 value) => value.via<MetricsA>.Measure();
int32 ExplicitB(int32 value) => value.via<MetricsB>.Measure();
```

These calls would lower to `MetricsA.Measure(value)` and
`MetricsB.Measure(value)`. The receiver is evaluated once before explicit
arguments and occupies the extension receiver parameter. Consequently
`value.via<MetricsA>.Measure(value)` would pass two values, not repeat or replace
the implicit receiver; it requires a corresponding two-parameter extension
and rejects for the shown helper. An explicit provider selection may choose
that provider even when an ordinary same-named member would win an unqualified
call. Overloads within the selected provider use the ordinary ranking rules,
not declaration/import order. This is not C# syntax; qualified static calls
remain available without approving this selector extension.

### Q17. Adapter declaration and forwarding

**Question:** How does source declare an explicit adapter and checked member
forwarding, including any one-way container projection?
**Example:** `struct ExtraView : IExtra { }` can name a nominal witness, but
does not by itself forward an original object's representation or members.

**C# model:** Not C# extension methods or inheritance. Terrum adapters retain
their own witness set; no original type gains a new conformance.

**Settled context; dedicated syntax still open:** Begin with explicit nominal wrappers and declared
forwarding operations. Add projection syntax only where validity, ownership,
access, backing and cost contracts are expressible; equal bits are insufficient.

**Concrete ordinary-wrapper example:**

```terrum
interface IMeasure { int32 Measure(); }
struct MeasuredInt : IMeasure
{
    private int32 _value;
    public MeasuredInt(int32 value) { _value = value; }
    public int32 Measure() => _value;
}
```

`new MeasuredInt(7)` copies the integer into a new nominal value that supplies
`IMeasure`; it does not make `int32` implement that interface. Forwarding is
an authored checked member, not an automatically invented vtable.
This example avoids unresolved stored-loan syntax. A wrapper around an
object/view must also publish its backing/lifetime and access dependencies.
A narrowing wrapper such as a nonnegative integer cannot admit arbitrary
negative `int32` bits, and `List<int32>` cannot become `List<MeasuredInt>`
merely because their element payload sizes match. Dedicated adapter and
one-way container-projection syntax remain pending.

**Recommendation (not yet adopted):** Use a checked adapter attribute naming
the carried field, and opt-in forwarding attributes on explicitly declared
members. Forward nothing by default; there is no wildcard forwarding followed
by a hiding list. Omitting a forwarding declaration keeps that wrapped member
off the adapter's public surface, and adding a member to the wrapped type does
not silently add one to the adapter.

```terrum
// CANDIDATE attribute identities and generated-member contract.
// Formatting types/members are illustrative core-library proposals.
[Adapter("_value")]
struct MeasuredInt : IMeasure
{
    private int32 _value;
    public MeasuredInt(int32 value) { _value = value; }
    public int32 Measure() => _value;

    [Forward("_value")]
    public bool TryFormat<TWriter>(write ref<TWriter> writer, TextSpan format)
        where TWriter : ITextWriter;
}
```

This would expose authored `Measure` and the explicitly forwarded `TryFormat`,
not automatically expose `int32` operators, parse methods or its witnesses.
The adapter marker resolves one actual instance field and preserves its
validity/access/ownership/backing contract. `Forward` resolves one compatible
member of that field's type from the authored complete signature, then generates
a checked body. Unknown fields, ambiguous targets, incompatible signatures or
weakened loans/effects reject; no name-only overload guessing. This candidate
is the explicit proposed exception enabling a generated body, not a claim
that arbitrary concrete methods may now end in `;`.

Initially forward ordinary instance callables/accessors, not implicit
conversions, operators or whole interfaces. Those often need authored
unwrapping/wrapping and invariant checks; forwarding must not silently choose
a constructor or reinterpret result bits. Extra interface conformance still
requires a declaration and checked witness mapping. A wrapper can explicitly
publish an underlying-value accessor if desired, but that is not implied by
the adapter marker. Attribute schemas, exact forwarding target selection and
dependency metadata remain pending; neither attribute makes a container
projection, foreign ABI promise or primitive constant domain automatically.

### Q18. Family and conditional-member declarations

**Question:** Choose source declarations for family opt-in, member eligibility,
conditional conformance and checked erased projections.
**Example:** `TextSpan<char16>` and `TextSpan<void>` name an already described
family, but ordinary `struct Box<T> { }` must not acquire `Box<void>` erasure.

**C# model:** Not C# constructed generic types alone. Terrum needs explicit
relationships that C# name/arity matching does not provide.

**Recommendation:** Separate family membership from extension lookup; require
declared projection and per-member constraints. Keep a conditional member's
guarantee visible to definition-site generic checking.

**Review boundary:** Explicit family membership and checked projections are
already accepted contracts. The following source-level example clarifies
the proposed conditional-member behavior without selecting a family-registration
attribute or keyword.

**Concrete family/member distinction (APIs remain proposals):**

| Source form | Candidate operation and required relationship |
|---|---|
| `TextSpan<char16>` | `.AsSpan()` yields `Span<char16>`; `.AsTextSpan()` yields validated erased `TextSpan<void>`, preserving backing and encoding information. |
| `TextSpan<void>` | Has encoding-independent text operations; no unconditional typed `.AsSpan()` member that invents a code-unit type or transcodes silently. |
| Ordinary `Box<int32>` | Has only its declared members; no automatic projection to `Box<void>` from matching name/arity. |

In a generic body over `TextSpan<TCodeUnit>`, typed code-unit access needs
the declared `[TextCodeUnit]` capability/family member guarantee; using that
member before establishing it rejects at definition checking, even if one
later instantiation happens to be `char16`. Erased validated text is not
an arbitrary writable byte span. Exact family/conditional-member declaration
and projection syntax remain unapproved; the table describes the requested
contract rather than introducing a new keyword.

```terrum
// Illustrative calls under the declared text-family contract.
// These API names/shapes remain library proposals.
TextSpan<void> EraseUtf16(TextSpan<char16> text) => text.AsTextSpan();
Span<char16> KeepUtf16Units(TextSpan<char16> text) => text.AsSpan();

// REJECT: erased text has no unconditional char16 AsSpan member.
Span<char16> GuessUtf16Units(TextSpan<void> text) => text.AsSpan();

Span<TCodeUnit> Units<TCodeUnit>(TextSpan<TCodeUnit> text)
    where TCodeUnit : [TextCodeUnit]
{
    return text.AsSpan();
}
```

`EraseUtf16` hides the static unit type but retains the actual encoding and
backing dependency; it neither transcodes nor creates owned storage.
`KeepUtf16Units` preserves the exact typed units. `GuessUtf16Units` cannot
infer an encoding from its requested result or reinterpret UTF-8/UTF-32 as
UTF-16. `Units` is definition-site valid only with the declared typed-member
guarantee; a later favorable instantiation cannot repair an unqualified
generic body. A runtime encoding check could participate in a separately
declared checked refinement projection, not an automatic overload fallback.
Ordinary `Box<T>` has no erasure operation unless a family/projection contract
explicitly opts it in. This example accepts no exact family-registration,
conditional-member or conditional-conformance declaration syntax.

### Q19. Complete parameter and reference qualifier matrix

**Question:** Reconcile `ref T`, `mut ref T`, ordinary `ref<T>` values,
`write ref<T>`, `implicit ref<T>`, `init ref<T>` and `pin ref<T>` in every
parameter/result/storage context. Existing access distinctions remain fixed.
**Example:** `void Inspect(implicit ref<Item> item) { }` and
`void Update(write ref<Item> item) { }` need consistent explicit-call rules.

**C# model:** Not aliases for C# `ref`, `in` and `out`. Terrum separates the
slot, referent authority, vacancy and dependencies; readonly is not deep
immutability.

**Recommendation:** Publish a passing/storage matrix and one explicit
expansion for each convenience form. Reject combinations that cannot express
their access or initialization contract, rather than invent implicit copying.

### Q20. Declaring result, capture and backing dependencies

**Question:** Complete source notation for borrowed results, stored views,
multiple backing sources, lambda captures and suspension dependencies.
**Example:** `ref<T> Select<T>(ref<T> left, ref<T> right)` needs a published
dependency relationship; its result type alone does not identify the backing.

**C# model:** Ref safety and `scoped` are relevant prior art, not Terrum syntax.
Terrum additionally distinguishes descriptor, backing and allocation/lease
lifetimes and preserves explicit ownership.

**Recommendation:** Begin with an explicit result annotation, for example the
candidate `[return: DependsOn("left", "right")]` on `Select`. Constant strings
identify parameter declarations of that callable; unknown names or incompatible
dependency kinds are errors, not runtime lookup. The result may depend on
either input, so callers retain both possible backing obligations rather than
only backing common to both inputs; neither input's lifetime is extended. Use separate
targeted contracts for receiver storage, forwarded backing and stored captures;
the result annotation alone must not imply all three. This proposed attribute
contract and its default treatment of unannotated results remain unapproved.

### Q21. Reservation versus immediate reference formation

**Question:** Do ordinary writable receiver/reference calls need a
reservation/activation phase? Implicit readonly borrowing must use the same
mechanism as its explicit expansion.
**Example:** `target.Update(target.Read())` can conflict if forming the receiver
loan precedes evaluating an argument that accesses it.

**C# model:** Not a C# ref-parameter rule transplanted into exclusive loans;
C#'s aliasing model does not provide Terrum's required exclusion contract.

**Recommendation:** Keep immediate formation unless concrete APIs demonstrate
a need for reservation. If introduced, specify reentrancy and invalidation
for both explicit and implicit forms before accepting additional calls.

## Construction, operators and expressions

### Q22. Implicit storage initialization and completion hooks

**Question:** How are auto-backed properties initialized when not assigned by
a constructor/caller, and what explicit hook validates completed construction?
Mixed backing, storage order, repeated unpublished `init`, and constructor
minimum guarantees are already approved.
**Example:** `struct Point { int32 X { get; init; } }` does not establish a
universal zero value merely by allocating a backing field.

**C# model:** C# auto-properties and init accessors are prior art. Terrum does
not inherit zero-initialized `default(T)` or substitute `required` annotations
for checked constructor guarantees.

**Recommendation:** Treat unassigned backing as vacant, require explicit
initialization, and make completion validation an explicit declared operation
with an identified phase. Do not let ordinary live-value withers invoke `init`.

### Q23. Nominal compound-operator declaration shape

**Question:** Complete declarations for `+=` and other compound operators,
including destination/operand eligibility, result and effect contracts.
The distinct operation contract with a `write ref<T>` destination is settled.
**Example:** `counter += increment;` must select that contract, not silently
expand to `counter = counter + increment`.

**C# model:** Deliberately different. C# 14 adds public instance `void`
compound operators; older C# derives compound assignment from binary
operators. Neither defines Terrum's destination-loan form.

**Recommendation:** Use a static, void-returning destination-loan candidate,
for example `public static void operator +=(write ref<Counter> destination, int32 increment)`
with an ordinary body. Extend the same shape to the eligible compound family,
but not the built-in `??=` operation. The assignment is a statement: it does
not produce a copied replacement value or infer an owning transfer. Evaluate
the destination and operand once under the ordinary access/effect/loan rules;
the declaration must state its effects, without an inferred `NoThrow` or
transactional rollback promise. This declaration form is proposed, not
accepted. Keep static `++`/`--` operand-shaped results distinct; do not add
C# instance mutation operators by analogy.

### Q24. Constructor and conversion eligibility/ranking

**Question:** When source- and destination-owned conversions both apply, or a
constructor has a matching parameter, what is eligible for `T(value)`?
Static `operator T(U source)` spelling and explicit invocation are settled.
**Example:** `var parsed = Target(source);` must have one recorded declaration.

**C# model:** Not C# syntax: C# separates `new T(x)` construction from `(T)x`
conversion. Terrum's type-call form requires its own rule.

**Recommendation:** Keep `new T(...)` for construction and require a unique
eligible explicit conversion for a nonliteral type-call. Reject competing
source/destination conversions rather than choose by declaration order.
Preserve contextual literal construction as its own case.

### Q25. User-defined short-circuit operations

**Question:** Can nominal `&&`/`||` participate, and how do they select a
truth test, conditionally evaluate the right operand and preserve ownership?
The `true`/`false` pair and its `bool` result do not answer all these rules.
**Example:** `left && right` currently has settled built-in Boolean ordering.

**C# model:** C# does not directly overload `&&`/`||`; it combines `true`/`false`
with eligible `&`/`|`. Terrum has not adopted that complete derivation.

**Recommendation:** Keep nominal short-circuiting unavailable until one
explicit protocol covers all evaluation and result obligations. A normal
binary witness cannot license eager evaluation of both operands.

### Q26. Pointer formation, dereference and pin acquisition

**Question:** Select the source operations that form/dereference pointers and
acquire a pin, separately from the already approved pointer/reference types.
**Example:** `ptr<int32>` and `pin ref<int32>` describe different authority;
neither spelling alone roots or pins arbitrary storage.

**C# model:** C# unsafe pointer operators and `fixed` are comparison points,
not adopted syntax. Terrum has checked provenance/backing dependencies and
does not use `pin` as a statement keyword.

**Recommendation:** Use candidate prefix `&place` and `*pointer` operations
inside `unsafe`, for example `unsafe { ptr<int32> p = &local; int32 n = *p; }`.
Restrict address formation to addressable, nonmoving storage or storage covered
by an explicit live pin; `unsafe` itself does not root or pin managed storage.
Retain source provenance, bounds, access and lifetime obligations, and do not
grant writable access merely by taking an address. Acquire pins through a
checked library operation, with candidate call shape
`Pin.TryAcquire(source, init pinned)` and a scoped release/dependency contract,
not a new `pin` statement. Both operators and the acquisition API are proposals;
their concrete result/guard types must be reviewed together before adoption.

**Dereference/member grouping question:** Under the candidate prefix `*`
operation and ordinary postfix precedence, `(*p).M` accesses a member of the
dereferenced value, while `*(p.M)` dereferences a pointer-valued member.
`(*(*p)).M` dereferences twice before member access; `*(*p).M` would instead
dereference the member obtained after one dereference. These are distinct
operations, subject to ordinary type/authority checks, not interchangeable
spellings. **Recommendation:** Require the explicit grouped forms when
dereference and member access combine, rather than allowing the visually
ambiguous `*p.M` if prefix dereference is selected. This does not settle
whether standalone `*p` is allowed. An alternative candidate is a fenced
dereference `(*expression)` with `(*` as a dedicated opener and `)` as its
close; the entire interior expression supplies the pointer. Under that
candidate `(*p + offset)` dereferences the address expression `p + offset`,
not an already-dereferenced value plus an offset. Nested forms are
`(*(*p))`; no bare `**p` or `****p` spelling is added. Selecting a contiguous
`(*` token versus a grammar construct allowing trivia between `(` and `*`
also needs a decision. Keep ordinary prefix dereference and this fenced
alternative distinct rather than adopting one from a grouping example.
Also compare an indexer
spelling: candidate pointer `p[0]` corresponds to one dereference and
`p[0][0].M` makes two indirections followed by member access explicit.
Whether to offer indexing alone or alongside prefix dereference remains
open; indexing must retain the same unsafe/provenance/lifetime/access
obligations, not imply an unavailable length or bounds guarantee.
Under the general punctuation-separation proposal, multiple prefix
indirections require an explicit boundary between operators. With the
access-attachment direction from Q03, prefix dereference must adjoin its
following operand token, so use `*(*p)`, not `* *p` or `* p`.
Grouping follows from attachment plus the absence of a `**` spelling,
not a blanket restriction on every prefix operator.
Do not add a `**` punctuation exception.
Pointer types remain `ptr<ptr<T>>`, not C-style `T**`.
This grouping restriction is proposed, not part of the accepted postfix
grammar. It does not supply provenance, bounds, pinning or writable authority.

### Q27. Nominal sums and closed-case declarations

**Question:** Choose declaration/source construction forms for tagged cases
and the explicit closed-case contract. Optional flattening and unknown-case
safety are already settled.
**Example:** `enum LookupStatus : int32 { Missing, Found }` with a separate
`init ref<T?>` output illustrates why `Found(null)` differs from `Missing`.

**C# model:** Not a C# enum, inheritance hierarchy or `Nullable<T>` wrapper;
none supplies Terrum's owning tagged-payload contract automatically.

**Recommendation:** Use explicit nominal cases and explicit closedness,
keeping evolvable cases separate from closed exhaustive dispatch. Require
rooting/cleanup of unknown payloads before allowing compatible extension.

### Q28. Complete pattern and type-test forms

**Question:** Complete relational, nominal-case, type-test and destructuring
patterns, including binding ownership and access. Existing guards,
`and`/`or`/`not`, compatible `or` bindings and fallback behavior remain fixed.
**Example:** `switch (value) { some item => Use(item), _ => 0 }`
already exposes an optional payload without defining arbitrary destructuring.

**C# model:** Pattern families are C#-inspired; Terrum does not inherit
property-getter duplication, implicit numeric conversion, or ownership-neutral
payload extraction merely from similar spelling.

**Recommendation:** Specify evaluation once, binding modes and refinement
invalidation before extending patterns. Keep tuple structure separate from
name-discovered `Deconstruct` methods.

### Q29. First-class index/range values

**Question:** Are `^offset` and `start..end` permitted as first-class values
outside indexing, and which declared contracts accept them?
**Example:** `items[start.. ^1]` is already an approved contextual slice.

**C# model:** C# uses `System.Index`/`System.Range`; Terrum has not adopted
universal types or their runtime representations.

**Recommendation:** Keep the contextual forms initially and require an
explicit registered type/contract before allowing standalone values. This
preserves checked domains and backing without inventing an allocating range.

### Q30. Nested collection-fragment construction

**Question:** How are nested fragments contextually typed, and is construction
without an intermediate collection part of the registered contract?
**Example:** `List<int32> values = [1, ..(include ? [2] : [])];`

**C# model:** Target-directed collection expressions and spreads are
C#-inspired. Terrum retains explicit move/refusal/abandonment contracts and
must preserve its capture/drain ordering.

**Recommendation:** Require explicit fragment support from the target
registration. Do not promise allocation elision by syntax alone or turn
observable builder effects into an optimization detail.

### Q31. Producer contract phases and suspension loans

**Question:** Which declared clauses apply at producer creation, advance,
yield and completion, and how are element/capture loans published?
**Example:** `[Iterator] Sequence<int32> Values() { yield 1; return; }`
already starts lazily; adding an `ensure` must not silently mean per-yield.

**C# model:** Iterators are C#-inspired, with approved differences:
resolved `[Iterator]`, `yield expression;`, ordinary `return;`, and explicit
checked close/loan obligations. C# does not supply Terrum's clause phases.

**Recommendation:** Keep ordinary creation and completion contracts distinct
from advance/item contracts, selected by a declared producer protocol.
Do not infer lifetime extension or per-yield checks from the body.

### Q32. Catch eligibility and failure matching

**Question:** Which failure types may be caught, how does typed matching select
a handler, and at what checked phase are filters evaluated? Transport bytes
and exception object ABI are separate.
**Example:** `try { Work(); } catch (Failure error) when (CanHandle(error)) { Recover(error); }`
uses the approved typed/filter form within required unsafe recovery scope.

**C# model:** Syntax is C#-inspired, not an automatic `System.Exception`
hierarchy. A settled deviation is that a throwing filter does not silently
become false; it follows ordinary failure/chaining rules.

**Recommendation:** Match declared catchable contracts in source order and
make filter-phase access explicit. Never require speculative reads of
unwound frames or silently erase a filter failure.

### Q33. Policy variant declarations and compile-time queries

**Question:** Choose how a variant identifies its base declaration and policy
tuple, and whether a source policy query tests an active request or a selected
call. Exact-tuple lexical preference and strict `require policy` are settled.
**Example:** `policy<Arithmetic.Wrap> { Use(value); }`

**C# model:** Not C# `checked`/`unchecked`. Terrum selects a declared variant
of an already resolved base; the request is not ambient behavior inherited
by callees.

**Recommendation:** Use a candidate declaration annotation such as
`[PolicyVariant("Add(int32, int32)", Arithmetic.Wrap)]` on the variant callable.
The compiler resolves the constant signature string to one base declaration in
the declaring scope and checks the policy tuple and complete callable contract;
it must reject ambiguity rather than select an overload by spelling alone.
This proposes an attribute contract, not an adopted attribute or wire identity.
Do not allow import-attached variants. Omit a general policy query until its
subject is explicit; an active preference is not proof of selected behavior.

### Q34. General constants and type/layout queries

**Question:** Complete eligible declaration contexts/value domains for
constants and the source identity of type/layout/reflection queries.
Required constant evaluation and static body proofs already have distinct
roles; their portable rule floor is separate proposal work.
**Example:** `const int32 count = 4;` inside a body is established; using a
computed value in `[Layout(...)]` must obey a defined compile-time contract.

**C# model:** Partly C#-inspired syntax, but Terrum permits required checked
evaluation rather than C#'s restricted constant-expression model. It does not
inherit CLR reflection objects or universal `default(T)`.

**Recommendation:** Define compiler-known query identities and bounded
constant domains, not magic arbitrary library names. Keep compile-time-only
formation distinct from optional runtime folding.

### Q35. External declarations and convention annotations

**Question:** Complete `extern` declaration forms, import identity, calling
convention annotation placement and caller-unsafe obligations.
**Example:** `unsafe int32 ReadDevice(uint64 address) { unsafe { return 0; } }`
illustrates the existing obligation boundary before replacing the body with
an external definition.

**C# model:** C# external declarations and function-pointer convention
attributes offer comparison, not adoption of P/Invoke, marshalling or CLR
managed conventions.

**Recommendation:** Make external identity/convention explicit and retain
the complete Terrum callable contract. Do not let an ABI annotation suppress
exception containment, rooting or loan obligations.

### Q36. Whether to provide GC finalizers

**Question:** Is a separate GC-finalization feature needed at all? There is no
`~Type()` production. Deterministic owner cleanup is already a different
mechanism.
**Example:** `[Cleanup] void Release() { }` on an eligible owner does not
register a GC finalizer.

**C# model:** Not implicit adoption of C# finalizers or their resurrection and
runtime-thread behavior.

**Recommendation:** Keep finalizers absent unless a concrete API need survives
explicit ownership/supervision alternatives. If added, review resurrection,
affinity, failure and resource guarantees together, not as syntax sugar.

### Q37. Loop-label scope and shadowing

**Question:** Specify whether nested enclosing loops may reuse a label and
whether labels share a declaration space with values. Named loop targets are
approved; the source contract does not yet spell out collision rules.
**Example:** `outer: while (ready) { inner: do { break outer; } while (again); }`
has unambiguous distinct labels.

**C# model:** Not C# `goto` labels: C# `break` and `continue` are unlabeled.
Terrum labels name enclosing loops only and cannot escape cleanup actions.

**Recommendation:** Reject duplicate simultaneously enclosing loop labels
initially, in a separate label namespace. Reusing labels in disjoint scopes
is harmless; silently retargeting a transfer through nested shadowing is not.

## Documentation source choices

### Q38. Documentation attachment and structured headings

**Question:** Complete attachment across blank lines, attributes, partial and
generated parts, plus unknown-heading and heading-case behavior. GFM prose,
implicit summary, top-level metadata headings and separate XML output are
already approved.
**Example:** `/// # param value` followed by a documented declaration must
attach to that declaration, not to an intervening attribute's implementation.

**C# model:** Declaration attachment is C#-inspired; the GFM heading model is
not C# XML documentation syntax.

**Recommendation:** Parse Markdown first, recognize a finite case-sensitive
heading vocabulary only at top level, and diagnose unknown metadata-looking
headings without interpreting code/list/quote contents as directives.

### Q39. Complex documentation references and inheritance

**Question:** Select overload/generic member reference spelling, escaping,
custom-label collisions, and explicit inherited-document selection.
**Example:** `/// See @Type.Member and [details](README.md).` distinguishes
symbol references from ordinary navigation.

**C# model:** C# `cref` overload identity is useful prior art; Terrum also has
declaration-scoped mentions and explicit `xref:` navigation. Backticks remain
code, not inferred references.

**Recommendation:** Reuse source name/type syntax where unambiguous and require
an explicit signature when overload selection is needed. Make inheritance
selection explicit; never copy a stronger contract from prose.

## Additional ownership and representation source choices

### Q40. Wither updates on unique owners

**Question:** Define receiver consumption, field replacement, failure and
cleanup for wither-style updates on unique/owning values. Ordinary functional
update does not reopen construction-only `init`.
**Example:** `var replacement = current.with(Name: name);` needs an ownership
contract if `current` contains a uniquely owned resource.

**C# model:** C# record `with` is only comparative prior art. Terrum's
`.with(...)` direction is not a C# shallow copy or synthesized record clone;
non-`Copy` ownership cannot be duplicated.

**Recommendation:** Require an explicit consuming or borrowing update contract
for owners and define the old/new cleanup state on failure. Do not synthesize
a copy-based wither for every nominal type.

### Q41. Iteration element-binding modes

**Question:** Complete value, borrowed and consuming bindings in ordinary and
asynchronous iteration, including advance invalidation. Loop expansion and
checked close are already selected.
**Example:** `for (var item in items) { Use(item); }` needs a binding contract
when an element is non-`Copy` or backed by an advancing cursor.

**C# model:** C# `foreach`/`ref foreach` supplies useful comparison, not
Terrum's spelling or ownership rule. Terrum also checks capture, suspension
and asynchronous close.

**Recommendation:** Derive the element's access from the declared iteration
protocol and require explicit consumption or writable borrowing when needed.
Never promote an iteration-local view to an independently owning value.

### Q42. Layout-control argument spelling

**Question:** Choose the attribute argument names/forms for member packing
and aggregate over-alignment, including their combined use. Accepted
`Auto`, `Sequential`, `Union`, `Transparent` and `Blittable` semantics must
not be altered by this syntax choice.
**Example:** `[Layout(LayoutKind.Sequential)] struct Pair { int32 First; int32 Second; }`
establishes field order, but does not select packing or over-alignment.

**C# model:** C#/.NET layout attributes are comparative prior art; the
`StructLayoutAttribute.Pack` field limits member/derived type alignment.
The candidate below instead uses positional constant arguments and adds a
separate aggregate-alignment control. Terrum's aggregate over-alignment and
explicitly checked `Blittable` boundary do not follow from CLR packing or
sequential layout.

**Recommendation:** Use candidate positional arguments in the order
kind, member-packing limit, aggregate alignment:
`[Layout(LayoutKind.Sequential, 1, 16)]` would request one-byte member packing
and 16-byte aggregate alignment. This fits the existing constant-argument
production without presuming a decision on named attribute arguments. The
argument contract is unapproved; it must preserve the selected packing,
over-alignment, tail-padding and array-stride semantics. Neither extra argument
freezes layout or grants `Blittable`. Reject unsupported target combinations,
including custom controls on `Transparent`, and retain separate published-
layout eligibility. Target limits and wire encoding remain ABI work.

## Boundaries that are not unanswered source decisions

| Category | Treatment |
|---|---|
| Already settled | Raw quote fences/framing/newline directives/brace splitting; interpolation format alternatives and capture order; grouped callable/lambda clauses and contextual `return`; aliases' transparent export; optional `<this T>`; `.via<I>` and restricted default-body calls; constructor minimum guarantees; repeated unpublished init; params/default/named-argument order; tuples/constant indexing; optional/coalescing forms; switch transfers; eager async versus lazy `[Iterator]`; awaited structured close. Do not ask these again. |
| Explicitly deferred | Declaration `static require`/`static ensure`; entry snapshots/history; nested nominal types; interface constructors; source type packs and zero/one-arity tuple extensions; collection comprehensions/repetition pending deeper library/tensor design. They are not pending votes in this inventory. |
| Implementation work | Parser/binder/verifier/native lowering, compiler-component parity, lossless recovery and diagnostics, regression fixtures and support-version migration. Approval does not claim any of these are complete. |
| Metadata/ABI work | Wire rows, witness/capture representation, dispatch tables, compatibility encoding, exception transport, public tuple/callable ABI, layouts and resource limits. These must carry chosen source contracts, not choose them. Existing reserved fields remain generically reserved. |
| Library API work | Iterator/awaiter/close operation identities, scheduler/executor/cancellation APIs, task ownership surfaces, text/collection handler registration APIs, primitive comparer names, allocator and tensor APIs. Review in the [core-library proposal](core-library-reference.md), with source dependencies above where needed. |
| Proof/tooling work | Exact portable proof rules/budgets remain in the [proof proposal](static-proof-and-constant-evaluation.md). XML schema/member IDs, package layout and optional documentation tests follow the approved attachment/reference decisions; they are not extra language features. |

## Resolved question link redirects

These stable headings preserve existing review links, not pending votes.
The rules live in the official grammar; their former proposal text is not
duplicated here.

### Q01. Escaping reserved words in identifiers

Resolved; see the [official grammar](../grammar-spec.md#2-lexical-notation).

### Q02. Reserved versus contextual words

Resolved; see the [official grammar](../grammar-spec.md#2-lexical-notation).

### Q03. Exact operator-run boundaries

Resolved; see the [official lexical grammar](../grammar-spec.md#2-lexical-notation).

#### Q03.1. Exact operator glyph set and lexical priority

Resolved; see [punctuation boundaries](../lexical-grammar.md#punctuation-boundaries).

#### Q03.2. Qualified-name dots outside expression member access

Resolved; see [punctuation boundaries](../lexical-grammar.md#punctuation-boundaries).

#### Q03.3. Declaration and attribute argument openers

Resolved; see [punctuation boundaries](../lexical-grammar.md#punctuation-boundaries).

#### Q03.4. Control-flow grouping and external block spacing

Resolved; see [structural syntax and canonical formatter spacing](../lexical-grammar.md#punctuation-boundaries).

#### Q03.5. Label and named-prefix colons

Resolved; see [punctuation boundaries](../lexical-grammar.md#punctuation-boundaries).

#### Q03.6. Constant-generic expression boundaries

Resolved; see [constant arguments](../grammar-spec.md#32-types-parameters-and-arguments); negatives currently require grouping.

#### Q03.7. Generalized comment fences and compatibility

Resolved; see [whole-star fences and opaque comment contents](../lexical-grammar.md#trivia-and-diagnostics).

### Q04. Hex-float versus member-access token boundaries

Resolved; see the [official grammar](../grammar-spec.md#2-lexical-notation).

### Q05. Namespace nesting and local imports

Resolved; see the [official grammar](../grammar-spec.md#31-source-file-structure-and-declarations).

### Q07. Attribute arguments and multiplicity

Resolved; see [resolved attribute schemas, targets and repetition](../grammar-spec.md#31-source-file-structure-and-declarations).

### Q08. Partial-declaration compatibility

Resolved; see the [official grammar](../grammar-spec.md#512-modifiers-cleanup-and-finalization).

### Q09. Enum constant and flag restrictions

Resolved; see the [official grammar](../grammar-spec.md#31-source-file-structure-and-declarations).

### Q10. Reserved primitive bindings and privileged members

Resolved; see the [official grammar](../grammar-spec.md#2-lexical-notation).

### Q11. Alias recursion and substitution

Resolved; see the [official grammar](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).

### Q12. Constant formals and dependent defaults

Resolved; see [checked wrapper constant domains and published guarantees](../grammar-spec.md#32-types-parameters-and-arguments).
Constructed-data recipes remain a separate undecided facility.

### Q13. Remaining overload specificity and constructor inference

Resolved recommendation; see [generic specificity and staged constructor inference](../grammar-spec.md#52-exact-overload-grammar-ranking-and-disambiguation).
Complete applicability/diagnostics and constructor-versus-conversion selection
remain separate specification work.

### Q14. Associated equality constraints

Resolved; see [exact `is` identity constraints](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).

### Q15. Inherited default/member resolution

Resolved; see the [official grammar](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).

## Official comparison sources

These sources describe C#, not Terrum. Feature references may document a newer
C# release than the standardized chapter; their differing scopes are explicit.

- [C# lexical structure](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure): identifiers, keywords, literals and token formation.
- [C# using directives](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/using-directive): namespace-local imports, concrete aliases and alias qualification.
- [C# namespaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/namespaces): namespace declaration spaces and nesting.
- [C# classes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes): generic formals, members, constants, constructors and partial declarations.
- [C# expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions): inference, overload resolution, conversions and conditional logical operators.
- [C# interfaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces): nominal conformance and most-specific inherited implementation.
- [C# operator overloading](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading): C# 14 instance compound operators and the distinct older derived-compound rule.
- [C# exception handling](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements): filter selection.
- [.NET user-filtered handlers](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/using-user-filtered-exception-handlers): explicitly specifies discarded filter exceptions and a false filter result for C#/VB.
- [C# attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/attributes): attribute identity, placement and arguments.
- [.NET member packing](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack): `StructLayoutAttribute.Pack` and its member/derived type alignment limit, not Terrum aggregate over-alignment.
- [C# documentation comments](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments): XML documentation and member references, not Terrum's GFM source model.
