# Remaining source-language decisions

> [!IMPORTANT]
> This is the pending source-decision queue. Recommendations are review
> inputs, not adopted rules.
> The [source grammar](../grammar-spec.md) remains authoritative for settled
> forms. This inventory does not reopen those choices or authorize a compiler
> to choose new semantics on the user's behalf.

This is a decision inventory, not a compiler backlog. Stable question numbers
are retained; gaps in the pending queue are intentional. Twenty-one parent IDs
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
C# comparisons are illustrative fragments, with ordinary namespace imports
omitted. Placeholder APIs/types are identified where relevant; these are not
claims that the snippets were compiled or that Terrum adopts their contracts.

There are **27 pending numbered groups**. Q03 and all seven punctuation
subquestions have canonical redirects, not pending votes. Partially resolved
groups ask only about their remaining choice; settled context is linked or
labeled, not another vote.

| Pending area | Stable IDs |
|---|---|
| Declaration applicability and modifier order | Q06 |
| Extension eligibility, adapter and family declaration forms | Q16.1, Q17-Q18 |
| References, initialization and operators | Q19, Q20.1, Q21, Q22.2, Q23-Q24, Q26.1 |
| Other source forms and contracts | Q27-Q30, Q31.1, Q32.1, Q33-Q37, Q40-Q42 |
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

**C# comparison:**

```csharp
static public class Utility
{
    public static int Read() => 1;
}
file class GeneratedHelper { }
class Counter
{
    public int Count { get => field; set => field = value; }
}
```

C# accepts modifier order such as `static public`; `file` restricts a
top-level type to one source file, and `field` is contextual property backing
syntax in C# 14. Terrum's pending matrix must state its own canonical order,
applicability and file-local boundary rather than infer them from this mix
of C# keywords and multiword terms.

## Types, lookup and references

### Q16.1. Extension declaration and import eligibility

**Remaining question:** Choose how source marks a provider's members as
extensions and makes them eligible in one lexical scope. Q16's explicit
provider selector and receiver behavior are approved in the
[official grammar](../grammar-spec.md#extension-provider-selection), not
another pending vote.

**Example:** A public `MetricsA.Measure(int32)` static helper does not become
eligible for `value.Measure()` or `value.via<MetricsA>.Measure()` merely
because its container can be named. Which declaration/import grants eligibility?

**C# model:** Lookup is C#-inspired, but no C# 14 extension block or retroactive
conformance is adopted by approving the selector.

**Recommendation:** Require explicit lexical eligibility, retain ordinary
access/loan checks and ranking, and choose its concrete declaration/import
spelling separately. No registration attribute or block syntax is selected.

**C# comparison:**

```csharp
public static class NumberExtensions
{
    extension(int value)
    {
        public bool IsPositive => value > 0;
    }
}
```

C# 14 declares extension members in a static container; namespace/import
scope governs availability. The older method equivalent is
`public static bool IsPositive(this int value) => value > 0;`.
Terrum's approved `.via<Provider>` selector is not another vote:
only the concrete declaration/import eligibility mechanism remains here.

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

**Requested forwarding cases:** For a wrapped type with illustrative members
`Foo`, `Bar` and `Baz`, both of these authoring modes need consideration:

| Requested selection | Generated forwarding surface |
|---|---|
| Forward only `Foo` | Selected `Foo` declarations, no generated `Bar` or `Baz` |
| Forward everything except `Foo` | Eligible `Bar` and `Baz` declarations, no generated `Foo` |

These names describe hypothetical wrapped members, not claims that `int32`
has a `Foo` member. Full signatures, overload groups, access and effects
still need checked target selection; a name filter is not overload resolution.
A source generator could materialize ordinary forwarding bodies in compatible
partial declarations. That is distinct from a live compiler wildcard which
changes lookup when the wrapped type gains a member. Regenerating source can
change the wrapper's declared API, but replacing a wrapped dependency must not
be confused with regenerating the wrapper.

**Pending choice:** Should include/exclude filters be a generator/partial
convenience producing explicit checked members, a built-in live wildcard, or
distinct opt-in mechanisms for both? The exact selector syntax, treatment of
overloads/operators/accessors and evolution guarantees also remain pending.

**Recommendation (not yet adopted):** Prefer generated explicit declarations
for those selections, so the produced API can be inspected and versioned.
One candidate is a checked adapter attribute naming
the carried field, and opt-in forwarding attributes on explicitly declared
members. Forward nothing by default in this candidate; this does not rule out
the separately pending include/exclude convenience. Omitting a forwarding
declaration does not expose that wrapped member automatically; explicitly
authored adapter members remain available. Adding a member to the wrapped type does
not silently add one to an unchanged generated adapter.

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

**C# comparison (explicit members, not a language forwarding feature):**

```csharp
partial class Adapter
{
    private readonly Inner _inner;
    public Adapter(Inner inner) { _inner = inner; }
}
partial class Adapter
{
    public int Foo() => _inner.Foo();
}
```

Here `Inner` is an existing wrapped API. A generator can emit the second
partial to expose only `Foo`, or emit `Bar`/`Baz` and omit `Foo` for an
all-except selection. Those become ordinary explicit members; replacing a
dependency binary does not spontaneously add members to the published
wrapper. Regenerating source against a newer dependency can change that
wrapper API. C# partials do not declare a nominal adapter relationship,
checked forwarding guarantee or live wildcard; those Terrum choices remain
pending.

### Q18. Family and conditional-member declarations

**Remaining question:** Choose source declarations for family opt-in, member eligibility,
conditional conformance and checked erased projections.
**Example:** `TextSpan<char16>` and `TextSpan<void>` name an already described
family, but ordinary `struct Box<T> { }` must not acquire `Box<void>` erasure.

**C# model:** Not C# constructed generic types alone. Terrum needs explicit
relationships that C# name/arity matching does not provide.

**Recommendation:** Separate family membership from extension lookup; require
declared projection and per-member constraints. Keep a conditional member's
guarantee visible to definition-site generic checking.

**Accepted context, not another vote:** Explicit family membership and checked
projections retain the [canonical family contract](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).
Identity retains complete qualification and ownership; declared typed erasure
hides static information, with no unconditional erased-to-typed projection.
A checked refinement can use retained runtime evidence, not invent a `T`
from the no-data `void` value. The following example clarifies conditional
member guarantees without selecting a family-registration attribute or keyword.

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

**C# comparison (closest constrained API, not family syntax):**

```csharp
static string Format<T>(Box<T> box)
    where T : IFormattable
    => box.Value.ToString(null, CultureInfo.InvariantCulture);
```

This assumes an existing `Box<T>` exposing `Value`. C# can constrain a
separate generic helper/extension, but it does not make an unconstrained
`Box<T>` conditionally implement a nominal interface through that helper.
Nor does it opt `Box<T>` into an erased family. Casting a boxed value to
`object` is not a representation/backing-preserving family erasure and
does not supply the pending conditional member/conformance contract.

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

**C# comparison:**

```csharp
sealed class Cell { public int Count; }
static void Observe(in Cell cell) => cell.Count++;
static void Replace(ref Cell cell) => cell = new Cell();
static void Create(out Cell cell) => cell = new Cell();
static bool TryCreate(out Cell? cell) { cell = null; return false; }
```

C# `in` prevents replacing the parameter's handle, not mutation of the
referenced object. `ref` permits replacement; `out` must establish an
assigned value on every normal return. In the last method even failure
assigns a live null value, whereas Terrum's Boolean-success `init ref<T>`
can leave its destination vacant. None of those C# forms acquires a pin or
spells Terrum's independent object authority, initialization and dependency
contracts.

### Q20.1. Remaining result, capture and backing contracts

**Question:** Complete receiver/storage selectors, capture and suspension
contracts, and the default treatment of unannotated results.
Parameter-directed `[return: DependsOn(nameof(left), nameof(right))]`
is [approved](../grammar-spec.md#result-dependency-declarations), not another
vote. Its `nameof` arguments name parameters of the annotated callable.
**Example:** An item may borrow a cursor's own storage or forward backing
held by its descriptor; naming `"this"` alone does not distinguish them.

**C# model:** Ref safety and `scoped` are relevant prior art, not Terrum syntax.
Terrum additionally distinguishes descriptor, backing and allocation/lease
lifetimes and preserves explicit ownership.

**Recommendation:** Use separate targeted contracts for receiver storage,
forwarded backing and stored captures; the approved result annotation alone
must not imply all three. Do not adopt unannotated-result defaults from an
example that explicitly declares dependencies.

**C# comparison:**

```csharp
static ref readonly int Select(in int left, in int right, bool useLeft)
{
    if (useLeft) { return ref left; }
    return ref right;
}
```

C# ref safety constrains a returned reference by the possible source
parameters; it does not offer this `DependsOn` attribute or Terrum's separate
lease/owner and stored-capture contract. `scoped` can prohibit a parameter
escape, but is not a selector saying "forward this descriptor's backing,
without borrowing its own storage."

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

**Concrete receiver signatures:**

```terrum
struct Counter
{
    private int32 _value;
    public Counter(int32 value) { _value = value; }
    public int32 Read() { return _value; }
    public mut void Update(int32 value) { _value = value; }
}
mut Counter target = new Counter(4);
target.Update(target.Read());
```

`Read` needs readonly receiver access and returns an independent copied
`int32`; `Update` needs writable receiver access. With immediate formation,
the outer writable loan is already active when the inner readonly access
is attempted, so the final call conflicts. With reservation, the destination
is identified first, the argument read finishes, then writable access
activates. That additional permission is the unresolved decision, not a
different `Read`/`Update` overload. The explicit-reference equivalent must
use the same timing. Reading into a separate `int32 next = target.Read();`
before `target.Update(next);` avoids the overlap in either model.

**C# comparison:**

```csharp
struct Counter
{
    private int _value;
    public Counter(int value) { _value = value; }
    public readonly int Read() { return _value; }
    public void Update(int value) { _value = value; }
}
Counter target = new Counter(4);
target.Update(target.Read());
```

C# allows this call, but does not grant an exclusive borrow that would forbid
aliases or reentrant accesses. Terrum reservation would still need to reject
an argument that invalidates the destination, escapes a conflicting loan,
or moves/replaces storage needed at activation; ordinary left-to-right
evaluation is not enough to make those cases safe.

## Construction, operators and expressions

### Q22.2. Completion hook declaration and registration

**Question:** How is the accepted completion operation declared/registered
and identified in the published contract?
Vacant backing, explicit property/constructor initialization, mixed backing,
storage order, repeated unpublished `init`, and constructor minimum
guarantees are
[approved](../grammar-spec.md#510-properties-and-accessor-local-effects).
**Example:** A constructor initializes `X`, a caller initializer supplies
`Y`, then a declared completion gate must establish `X <= Y` before
publication. The hook's identity and invocation form remain to specify.

**C# model:** C# auto-properties and init accessors are prior art. Terrum does
not inherit zero-initialized `default(T)` or substitute `required` annotations
for checked constructor guarantees.

**Accepted direction:** An explicit declared operation validates completion
after required initialization and before publication. A live-value wither
cannot invoke `init`. Q22.1 is resolved on that direction; the operation's
source identity is not yet selected.

**Terrum counterpart (candidate attribute identity only):**

```terrum
public class Point
{
    public int32 X { get; } = 1;
    public int32 Y { get; init; }
    public Point() { }

    [ConstructionComplete]
    private void Validate()
    {
        require X <= Y;
    }
}

Point point = new Point() { Y = 3 };
```

The `ConstructionComplete` name/registration is **unapproved**; it illustrates
the identity still needed. The constructor initializes `X`, the caller
initializes `Y`, then the declared hook validates, then publication occurs.
Failure cleans unpublished state. Renaming an unrelated method `Validate`
does not register it. The earlier getter-only example below illustrates the
already approved initialization routes, not this missing hook.

**C# comparison:**

```csharp
sealed class Point
{
    public int X { get; } = 1;
    public int Y { get; }
    public Point(int y) { Y = y; }
}
```

These are the two approved initialization routes for a getter-only backed
property. Unlike Terrum, C# class backing fields already have zero/default
values; omitting the assignment to `Y` can therefore still yield a live
zero. C# has no general compiler-invoked hook after the entire object
initializer and before publication; an ordinary validating factory is
an API convention, not that language guarantee.


### Q23. Nominal compound-operator declaration shape

**Question:** Complete declarations for `+=` and other compound operators,
including destination/operand eligibility, result and effect contracts.
The distinct operation contract and existing writable-destination direction
are settled; the reference-type objection below asks whether content-only
operations need different destination eligibility.
**Example:** `counter += increment;` must select that contract, not silently
expand to `counter = counter + increment`.

**C# model:** Deliberately different. C# 14 adds public instance `void`
compound operators; older C# derives compound assignment from binary
operators. Neither defines Terrum's destination-loan form.

**Original declaration candidate, now requiring this distinction:** a static,
void-returning destination loan, for example
`public static void operator +=(write ref<Counter> destination, int32 increment)`
for a value-type `Counter`. It cannot be generalized to class contents
merely by keeping that same type spelling. The assignment is a statement: it does
not produce a copied replacement value or infer an owning transfer. Evaluate
the destination and operand once under the ordinary access/effect/loan rules;
the declaration must state its effects, without an inferred `NoThrow` or
transactional rollback promise. This declaration form is proposed, not
accepted. Keep static `++`/`--` operand-shaped results distinct; considering
receiver-shaped compound declarations does not also approve C# instance
`++`/`--`.

**Class contents versus handle replacement:**

| Destination contract | Replace the stored handle | Mutate the referenced object |
|---|---|---|
| `ref<C>` | No | No |
| `write ref<C>` | Yes | No |
| `ref<write C>` | No | Yes |
| `write ref<write C>` | Yes | Yes |

These four forms describe **loans to a handle slot**, not the preferred way
to pass a class receiver. A class value already is a handle: passing
`write C` directly grants object-write authority without a `ref<C>` loan to
the variable containing that handle. `ref<write C>` is useful only when the
operation actually needs that additional slot reference; it is unnecessary
for a content-only operator. This is a semantic indirection distinction,
not a promise about whether a backend optimizes a slot reference away.

**Terrum counterpart (ordinary helper contracts):**

```terrum
void Mutate(write Counter destination, int32 increment)
{
    destination.Value += increment;
}

void Replace(write ref<Counter> destination, Counter replacement)
{
    destination = replacement;
}
```

Here `Counter` is a copyable class handle with an accessible writable
`Value`; `replacement` is already live. `Mutate` changes the object without
replacing the caller's handle. `Replace` writes the handle slot without
needing write authority over the old or new object's contents. Candidate
`public static void operator +=(write Counter destination, int32 increment)`
can therefore express content-only mutation without an extra slot loan.
An instance candidate uses its declared writable receiver instead.
For a struct, by-value passing would mutate a copy, so the destination-loan
form remains materially different.

Object mutation still needs the selected member's permissions; it does not
grant arbitrary writes through every reachable reference. A static operator
can mutate contents without replacing a handle, but requiring a writable
slot still excludes a fixed binding that holds a writable object handle.
An instance-shaped declaration alone does not answer that eligibility issue.

**Remaining choice:** keep uniform writable-slot eligibility, with the
necessary referent qualifications stated separately, or introduce
receiver-shaped eligibility for content-only operations. The latter changes
the writable-place boundary, not just declaration spelling. Decide fixed
bindings, readonly slot loans, value/ref-returning getters and temporaries
explicitly. This review has not adopted the change.
**Recommendation for that case:** receiver-shaped eligibility fits
content-only mutation better than requiring a replaceable handle slot.
Use the existing receiver access contract; if both receiver-shaped and
destination-loan operators are offered, specify their selection rather
than infer a winner from static/instance spelling.

**C# comparison:**

```csharp
static void Mutate(Counter destination, int increment)
{
    destination.Value += increment;
}
static void Replace(ref Counter destination, int increment)
{
    destination = new Counter(destination.Value + increment);
}
```

These helpers assume a class `Counter` with a writable `Value` and a
matching constructor. `Mutate` changes what every alias sees without
replacing the caller's handle; `Replace` changes that handle and leaves
aliases referring to the old object. Inside the same class, C# 14 permits
`public void operator +=(int increment) { Value += increment; }`
for the content-mutating case. That operator still has C# variable
classification restrictions; it is not permission to apply `+=` to every
temporary or getter result.

### Q24. Constructor and conversion eligibility/ranking

**Question:** When source- and destination-owned conversions both apply, or a
constructor has a matching parameter, what is eligible for `T(value)`?
Static `operator T(U source)` spelling and explicit invocation are settled.
**Example:** `var parsed = Target(source);` must have one recorded declaration.

**C# model:** Not C# syntax: C# separates `new T(x)` construction from `(T)x`
conversion. Terrum's type-call form requires its own rule.

**Revised recommendation for review:** Keep `new T(...)` for construction.
For a nonliteral conversion type-call, resolve an eligible destination-owned
set first, then source-owned declarations only when the destination has no
applicable conversion. Within each set require a unique best applicable
declaration under the stated overload rules. An ambiguous destination set
is an error, not permission to try the source; a selected destination
conversion's failure is also not a fallback signal. This replaces the
earlier equal-owner recommendation **only if approved**. Contextual literal
construction remains its own case.

**Explicit selector candidate:** `Target.via<Source>(source)` could select a
conversion declared by `Source`, while `Target.via<Target>(source)` selects
one declared by the destination. This is consistent with the idea of an
explicit owner/provider, but is **not** already licensed by Q16:
that selector currently describes a member call on a value receiver,
not invocation on a type with no receiver parameter. Its declaration
eligibility, selected conversion set and call grammar need explicit approval.
Ordinary ranking within the selected set must still find one conversion;
selecting an owner does not select one of its ambiguous overloads.

For `Target.via<Source>(literal)`, the selected declaration must also provide
an unambiguous input context for the literal. Do not reinterpret existing
contextual primitive literals as custom conversions just because the glyph
resembles the provider selector. Named conversion/factory methods remain
an explicit alternative without extending `.via` to this role.

**Terrum counterpart and selector alternatives:**

```terrum
Target ordinary = Target(source);
Target namedBySource = Source.ToTarget(source);
Target namedByTarget = Target.FromSource(source);
```

The named forms use ordinary static-member selection if their authors
declare those APIs. They already solve explicit selection without extending
`.via`. A candidate `Source.Target(source)` could similarly name an authored
factory; using it to select the source's `operator Target(...)` would need
an explicit compiler-known conversion-member rule. It is not automatically
an existing named method merely because the result type is `Target`.
The same applies to candidate `Target.Target(source)`. Prefer ordinary
authored factories if no special conversion selector is needed; if operators
need a universal selector, choose its identity and any collision with
ordinary members explicitly. Destination-first priority does not by itself
create such members.

**Actual generic-math comparison:** .NET's `INumberBase<TSelf>.CreateChecked`
handles identity first, then calls the destination's
`TryConvertFromChecked`, then the source's `TryConvertToChecked` only if the
first reports unsupported. Saturating/truncating creation uses the same
owner order. This is a declared numeric API protocol, not C# cast overload
resolution. A checked range failure throws; it does not become
"unsupported" and try the other owner.

Terrum can adopt the **owner-priority idea** with compile-time applicability
without also importing runtime trial calls. If runtime negotiation is wanted,
it needs its own checked protocol, including effects and the precise
unsupported result. Checked/saturating/truncating behavior must still belong
to explicit contracts, not a policy block silently changing primitive
conversions.

**C# comparison:**

```csharp
Target bySource = Source.ToTarget(source);
Target byDestination = Target.FromSource(source);
Target constructed = new Target(source);
Target converted = (Target)source;
```

The first two are ordinary named APIs supplied by their authors. C# can have
competing conversion operators declared in source and destination types;
the cast then has no syntax selecting their declaring owner and can be
ambiguous. It does not compete with `new Target(source)` construction.

### Q26.1. Pointer formation and dereference/member boundaries

**Question:** Select the source operations that form/dereference pointers and
convert an acquired pin, separately from the already approved
pointer/reference types and pin-acquiring local.
**Example:** A successfully acquired `pin ref<int32>` roots and stabilizes
its backing for the pin loan's lifetime. `ptr<int32>` does not. Merely
casting/requalifying an ordinary reference cannot manufacture that pin.

**C# model:** Pointer operators and `fixed` are comparison points, not adopted
syntax. Distinguish the original C# unsafe model from the C# 15/.NET 11
updated-memory-safety preview: the latter allows pointer formation and
`fixed` outside unsafe, while pointer memory access still requires unsafe.
Terrum has its own checked provenance/backing dependencies and does not use
`pin` as a statement keyword.

**Revised candidate direction:** taking an address need not itself require
unsafe when the place's lifetime, access and stable/rooted backing are
established. For GC storage, require a statically known covering pin;
independently stable storage still needs liveness and initialization
obligations. `unsafe` does not substitute for a missing root or pin.
A `pin ref<T>` to `ptr<T>` conversion could be allowed in a safe context,
retaining its access and pin/backing dependencies. A safe-context warning
for address formation is a separate proposed diagnostic policy, not a
proof that the address is unsafe or permission to dereference it.

**Approved pin-acquiring local:**

```terrum
pin ref<int32> pinned = ref storage;
```

Here `storage` denotes eligible initialized storage. The initializer is
be an explicit checked acquisition operation, not a qualifier-only cast.
The binding keeps its backing pinned for the promised local lifetime
or an explicit release/rebind. An address/conversion expression must designate
the pinned `int32` backing, not the storage of the reference descriptor;
whether the pin-to-pointer conversion is implicit or explicit remains open.
The local acquisition is approved target grammar, **not current compiler
support**. Pointer conversion and explicit release spelling remain pending;
a potential `Pin.TryAcquire` API is not required in place of this local form.

Clearing/rebinding the local cannot release its pin while a derived address,
copy, capture or loan still depends on it. For example, deriving `p`,
copying it to `q`, clearing `pinned`, then using `q` must not unpin early.
The compiler must reject premature release or use a separately approved
ownership-transfer mechanism, not silently shorten a promised pin to the
ordinary last-use lifetime of its local. Pinning a class-handle slot also
does not stabilize the separately allocated object reached through it.

**C# comparison (original model, also valid inside unsafe in the preview):**

```csharp
unsafe
{
    fixed (int* p = &values[0])
    {
        int value = *p;
    }
}
```

With an existing `int[] values`, `fixed` holds the containing allocation
stable during the block; an ordinary `ref int` into it is relocatable and
does not acquire that pin. The pointer must not escape its pin's lifetime.
Neither this spelling nor C#'s lexical restrictions selects Terrum's
pin-acquiring local syntax.

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

**Revised prefix recommendation for review:** retain ordinary prefix
precedence and permit the uncomplicated cases. Restrict the unparenthesized
operand's postfix receiver chain when it contains member selection; grouping
must say whether the member belongs to the pointer expression or pointee.
Members inside a call's argument or an index expression do not become part
of that receiver chain. This is a candidate rule, not an adopted restriction:

| Candidate source | Meaning/status under this rule |
|---|---|
| `*p` or `(*p)` | One ordinary dereference. |
| `*(p + offset)` | Dereference the computed address. |
| `*GetPointer()` or `*pointers[index]` | Dereference the call/index result; no member chain ambiguity. |
| `*p + offset` | Add to the dereferenced value; do not reinterpret as address arithmetic. |
| `(*p).M` | Select the pointee's member. |
| `*(p.M)` | Dereference the pointer-valued member. |
| `*p.M` or `*GetPointer().M` | Reject; require one of the explicit member boundaries. |
| `(*(p + offset)).M` | Select the computed address's pointee member. |
| `*(p + offset).M` | Reject; ordinary postfix precedence would select `.M` before dereference. |
| `*(*p)` | Two dereferences separated by a grouping boundary. |
| `**p` | Reject under the already accepted maximal operator-run rule; no `**` spelling. |

This does **not** give parentheses a special dereference-fence meaning.
The current ordinary-prefix alternative's grouping behavior remains intact.
It addresses the troublesome combinations rather than requiring every
dereference to use a new delimiter or keyword.
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

**Further alternatives requested for review:**

| Candidate | Operand/member boundary | Meaning and tradeoff |
|---|---|---|
| Ordinary prefix `*p`, with `(*p).M` | Postfix binds before prefix; grouping exposes the pointee member | Familiar and concise, but `*p.M` is visually misleading and would need an explicit restriction. |
| Fenced `(*expression)` | Everything inside the fence supplies the pointer | Visibly bounded, but differs from normal grouping: `(*p + offset)` would dereference `p + offset`, not add to `*p`. |
| Dedicated always-required `*(expression)` | The whole closed form dereferences before postfix access; `*(p).M` selects the resulting member | Avoids the fenced form's reinterpretation of `(*p + offset)`; no standalone `*p`. Nested `*(*(p)).M` stays explicit, at the cost of mandatory parentheses. |
| Pointer `p[0]` | `p[0].M` accesses the first pointee's member | Uses familiar postfix structure; it is still unsafe pointer indexing, not a length-carrying span or a bounds proof. |
| Pointer-to-reference `ref(p)` or a named keyword/API | The conversion forms a loan; subsequent `.M` is ordinary reference member access | Makes the unsafe boundary explicit once, but must distinguish conversion from existing place-reference formation and specify reference validity/dependencies. |

The third row needs that dedicated closed-form grammar. Merely requiring a
parenthesized operand while keeping ordinary prefix/postfix precedence would
still parse `*(p).M` as dereferencing `(p).M`, not selecting a pointee member;
that weaker restriction would not solve the ambiguity.

The last option is attractive when code primarily wants a checked reference
rather than raw-pointer arithmetic. It does not copy a value or acquire a
pin. Forming that reference is itself a validity boundary, even without
loading memory: invalid managed-reference representation may be visible
to GC, and later ordinary operations cannot repair it. Require suitable
storage, alignment, initialized `T`, lifetime/provenance, access authority
and necessary rooting/stability, including barriers/managed-storage rules.
For an unknown foreign pointer these are an unsafe caller contract, not
compiler-invented proofs. Known pin-derived pointers must retain their
dependencies. Exact spelling and safe/unsafe formation rules remain open.

For ordinary C# pointer precedence, `(*p).M` selects the pointee member and
`*(p->Next)` dereferences a pointer-valued member. Terrum need not adopt
`->` or make `ref(p)` a safe escape hatch simply to improve readability.

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

**Terrum counterpart (candidate declaration/case syntax):**

```terrum
[Closed]
public union Lookup<T>
{
    case Missing;
    case Found(T value);
}

Lookup<int32?> missing = Lookup<int32?>.Missing;
Lookup<int32?> presentNull = Lookup<int32?>.Found(null);
Lookup<int32?> presentValue = Lookup<int32?>.Found(7);
```

`union`, `[Closed]`, the case declarations and case construction above are
**unapproved candidate spellings**, not a new parser feature. They show the
proposed single nominal result: the first value has no payload; the other
two have a live `int32?` payload, absent or containing `7` respectively.
Candidate arms `Lookup<int32?>.Missing => ...` and
`Lookup<int32?>.Found(value) => ...` expose only that case's payload.
They must not flatten `Found(null)` into `Missing`.
Closedness means all declared cases suffice for exhaustive matching.
An evolvable variant cannot use that assumption and needs an unknown-case
fallback preserving opaque payload roots/cleanup.

**C# comparison:**

```csharp
enum LookupStatus { Missing, Found }
LookupStatus Lookup(string key, out string? value)
{
    bool found = dictionary.TryGetValue(key, out value);
    return found ? LookupStatus.Found : LookupStatus.Missing;
}
```

Here `dictionary` is an existing `Dictionary<string, string?>`: a present
key whose value is null returns `Found` and null; a missing key returns
`Missing` and null. The status distinguishes them, but the C# result and
output are independent values rather than one statically tagged payload.
A C# record hierarchy can bundle a `Found(string? Value)` case and a
`Missing` case, but does not automatically establish a closed exhaustiveness
contract or affine payload cleanup.

The corresponding **semantic sketch, not selected declaration syntax**, is
`Lookup<T> = Missing | Found(T)`. `Found(null)` contains a live optional
payload; `Missing` has no payload to read or destroy. A closed declaration
can require exhaustive case handling. An evolvable declaration instead
needs an explicit unknown-case path preserving any new payload's rooting
and cleanup; treating an unrecognized tag as an empty `Missing` would lose
those obligations. This is why an enum plus separate output, optional
flattening, and a nominal sum are different contracts.

### Q28. Complete pattern and type-test forms

**Question:** Complete relational, nominal-case, type-test and destructuring
patterns, including binding ownership and access. Existing guards,
`and`/`or`/`not`, compatible `or` bindings and fallback behavior remain fixed.
**Example:** `switch (value) { some item => Use(item), _ => 0 }`
already exposes an optional payload without defining arbitrary destructuring.

**C# model:** Pattern families are C#-inspired; Terrum does not inherit
property-getter duplication, implicit numeric conversion, or ownership-neutral
payload extraction merely from similar spelling.

**Recommendation:** Separate the already settled once-only scrutinee from
new member/extractor evaluation rules. Specify binding modes and refinement
invalidation before extending patterns. Keep tuple structure separate from
name-discovered `Deconstruct` methods.

**Terrum counterpart (candidate property/type/relational patterns):**

```terrum
switch (Next())
{
    Sample { Value: > 10 } when Guard() => Large(),
    Sample { Value: > 0 } => Positive(),
    _ => Other(),
}
```

Those new pattern spellings are **unapproved**. Their candidate evaluation
contract is concrete: call `Next()` once; attempt the first arm's type test
then member read; call `Guard()` only if its pattern matches; call `Large()`
only if the guard succeeds. Otherwise attempt the next arm, then its body
only on success. `Other()` runs only on fallback. Failures are not
nonmatches and cannot be silently retried.

The additional choice is whether reads of `Value` are reused. Recommend
one read per member path within an attempted arm, feeding its relational
subpatterns from that value, and a new observation in a later attempted arm.
A false, effectful guard could have changed the backing: "evaluate once"
must not freeze every getter for the entire switch. Getters/extractors
retain ordinary effects/failures; do not call `Deconstruct` by name.
Optimizations require proof that changed observations/effects are
unobservable. This precise member-read rule remains a recommendation.

Binding ownership is a different question: binding a copyable `int32` is
not permission to copy a unique payload, move out of a borrowed scrutinee,
or retain a reference beyond its backing.

**C# comparison:**

```csharp
record Point(int X, int Y);
object value = new Point(3, 4);
if (value is Point { X: > 0, Y: var y })
{
    Console.WriteLine(y);
}
```

C# combines type/property/relational patterns and binds `y`; positional
record patterns can instead use generated `Deconstruct`. Similar spelling
does not select Terrum's copy/borrow/move binding, permit getter repetition,
or authorize name-discovered destructuring. Those additional contracts
must be explicit before adding the corresponding forms.
C# property-pattern lowering can reuse observations and does not provide
the proposed per-attempt Terrum getter contract merely from similar syntax.
For a direct operational comparison, ordinary C# code could store `Next()`
once, read `Value` for the first attempt, run the guard if eligible, then
read `Value` again before a later attempt. The point is which calls/reads
are authorized, not just which new pattern tokens are accepted.

### Q29. First-class index/range values

**Question:** Are `^offset` and `start..end` permitted as first-class values
outside indexing, and which declared contracts accept them?
**Example:** `items[start.. ^1]` is already an approved contextual slice.

**C# model:** C# uses `System.Index`/`System.Range`; Terrum has not adopted
universal types or their runtime representations.

**Recommendation:** Keep the contextual forms initially and require an
explicit registered type/contract before allowing standalone values. This
preserves checked domains and backing without inventing an allocating range.

Yes: design/register first-class value contracts before admitting their
standalone syntax; until then keep contextual slicing. That does not require
allocating types or a universal `Index`/`Range`.

**Terrum counterpart:**

```terrum
Slice<int32> middle = items[1.. ^1];
```

Here `items` has an eligible borrowed-slice contract; the established form
uses that context's bound domains/backing. Candidate `Index last = ^1;`
and `Range middle = 1..last;` would instead require registered standalone
value contracts and a consuming slice contract. Those library names and
forms are **not selected**; neither first-class values nor allocation
elision are prerequisites for the contextual form above.

**C# comparison:**

```csharp
int[] items = [1, 2, 3, 4];
Index last = ^1;
Range middle = 1..last;
Span<int> slice = items.AsSpan()[middle];
```

C# supports these standalone values through .NET's `System.Index` and
`System.Range`; the language integrates their construction and use.
The span slice borrows array storage, while applying a range directly to
an array creates an array copy. Terrum must choose registered first-class
contracts without assuming either representation or silently materializing
its contextual borrowed slice.

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

**Terrum counterpart (the unresolved nesting is explicit):**

```terrum
List<int32> values = [1, ..(include ? [2, 3] : [])];
```

`List` is an illustrative registered target. If `include` is known true,
flattening to `[1, 2, 3]` is valid **only when the selected contract permits
equivalent direct-fragment construction** or an optimization proves the
intermediate behavior unobservable. Allocation by itself need not be a
required observable event. Identity/escape, allocation-refusal or failure,
allocator hooks, cleanup and builder effects can nevertheless make a
particular intermediate construction observable.

A declared builder may drain captured elements through `Add`, a fragment
through `AddRange`, or a direct fragment operation. These are illustrative
operation names, not name-discovered registrations. First capture the
selected element/sequence expressions in the accepted eager order; then
drain according to the checked builder contract. Do not turn
`Next(1), Next(2), Next(3)` into interleaved `Add(Next(...))` calls when that
moves observable writes ahead of later captures. Nor can an optimizer
substitute `AddRange` for `Add` merely because the final elements agree if
their published effects/failures differ.

The pending decision is the **source typing and guaranteed fragment
protocol**, not whether a compiler may perform ordinary sound optimization.

**C# comparison:**

```csharp
int[] suffix = include ? [2, 3] : [];
List<int> values = [1, ..suffix];
```

C# 12 collection expressions are target-typed; here a conditional fragment
has an array context before being spread into a list. This does not promise
Terrum's nested fragment can construct directly in its registered destination,
nor define its move/refusal, ownership or observable builder-effect contract.
An implementation optimization is not a source guarantee of those behaviors.

### Q31.1. Producer protocol identities and phase selectors

**Question:** Which declared clauses apply at producer creation, advance,
yield and completion, and how are element/capture loans published?
**Example:** `[Iterator] Sequence<int32> Values() { yield 1; return; }`
already starts lazily; adding an `ensure` must not silently mean per-yield.

**C# model:** Iterators are C#-inspired, with approved differences:
resolved `[Iterator]`, `yield expression;`, ordinary `return;`, and explicit
checked close/loan obligations. C# does not supply Terrum's clause phases.

**Accepted direction:** Keep ordinary creation and completion contracts distinct
from advance/item contracts, selected by a declared producer protocol.
Do not infer lifetime extension or per-yield checks from the body.

**Remaining question:** Choose the producer protocol operation identities and
phase selectors that publish those separate obligations. The direction is
approved, not a particular clause annotation.

**Terrum counterpart (ordinary wrapper plus accepted iterator forms):**

```terrum
Sequence<int32> Values(int32 count)
{
    require count >= 0;
    return Produce(count);
}

[Iterator]
Sequence<int32> Produce(int32 count)
{
    for (int32 i = 0; i < count; i++)
    {
        yield i;
    }
    return;
}
```

`Sequence` represents an eligible declared producer contract.
The ordinary wrapper checks at invocation; constructing `Produce` does
not execute its loop. Advancement executes until yield/completion/failure.
Per-item guarantees belong to the selected advance/item protocol;
successful final-completion guarantees are not per-yield assertions, and
early close follows its cleanup contract rather than pretending iteration
finished normally. A yielded loan cannot outlive its published backing;
the next advance/close must respect outstanding item-loan restrictions.

**C# comparison:**

```csharp
static IEnumerable<int> Values(int count)
{
    if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count)); }
    yield return count;
}
IEnumerable<int> sequence = Values(-1);
using IEnumerator<int> cursor = sequence.GetEnumerator();
cursor.MoveNext();
```

The C# iterator body, including this validation, starts at advancement,
not the call creating `sequence`; argument evaluation still occurs at the
call. Terrum's creation, per-advance, suspension and completion clauses need
declared phases. C# deferred execution alone does not specify the ownership
and loans retained across those phases or Terrum's checked close obligation.

### Q32.1. Catchability registration and filter phase

**Question:** Which failure types may be caught, how does typed matching select
a handler, and at what checked phase are filters evaluated? Transport bytes
and exception object ABI are separate.
**Example:** `try { Work(); } catch (Failure error) when (CanHandle(error)) { Recover(error); }`
uses the approved typed/filter form within required unsafe recovery scope.

**C# model:** Syntax is C#-inspired, not an automatic `System.Exception`
hierarchy. A settled deviation is that a throwing filter does not silently
become false; it follows ordinary failure/chaining rules.

**Accepted direction:** Match declared catchable contracts in source order and
make filter-phase access explicit. Never require speculative reads of
unwound frames or silently erase a filter failure.

**Remaining question:** Choose the catchability registration/type domain and
exact filter/unwind phase. Agreeing with the direction does not select the
C# `System.Exception` hierarchy or all its timing rules.

**Terrum counterpart (failure APIs illustrative):**

```terrum
unsafe
{
    try { Work(); }
    catch (IoFailure error) when (CanHandle(error))
    {
        Recover(error);
    }
    catch (Failure error)
    {
        Log(error);
    }
}
```

The selected failure profile must declare whether `IoFailure` matches
`Failure`; this is not inherited CLR class matching. If the first type
matches, its filter runs under checked access. False proceeds to the next
handler. A throwing filter propagates/chains under the normal failure
contract rather than being silently converted to false. No catch may
retain invalid references into frames that have already been unwound.

**C# comparison:**

```csharp
try { Work(); }
catch (IOException error) when (CanHandle(error))
{
    Recover(error);
}
catch (Exception error) { Log(error); }
```

These names stand for ordinary application APIs. C# filters run before
unwinding; if `CanHandle` throws, its exception is discarded and that
filter is treated as false while selection continues for the original
exception. Terrum's ordinary checked filter-failure/chaining rule is
deliberately different. The remaining catchable-value/type pattern must
not implicitly import C#'s `System.Exception` hierarchy.

### Q33. Policy variant declarations and compile-time queries

**Question:** Choose how a variant identifies its base declaration and policy
tuple, and whether a source policy query tests an active request or a selected
call. Exact-tuple lexical preference and strict `require policy` are settled.
**Example:** `policy<Arithmetic.Wrap> { Use(value); }`

**C# model:** Not C# `checked`/`unchecked`. Terrum selects a declared variant
of an already resolved base; the request is not ambient behavior inherited
by callees.

**Revised recommendation:** Use the callable's own declared identity and full
signature to identify its base, with a semantic attribute supplying only the
policy tuple. Do not repeat a hand-authored signature string.
For example, the following is **candidate syntax**, with `PolicyVariant`
an unapproved attribute identity:

```terrum
int32 Add(int32 left, int32 right)
{
    return left + right;
}

[PolicyVariant(Arithmetic.Wrap)]
int32 Add(int32 left, int32 right)
{
    return WrappingMath.Add(left, right);
}
```

`WrappingMath.Add` is an illustrative API with an explicit wrapping
contract; the attribute does not make ordinary `+` inside the variant wrap.
If multiple policy dimensions are declared, the attribute supplies their
exact enum tuple, for example
`[PolicyVariant(Arithmetic.Wrap, Precision.Fast)]`; these names do not
establish a new domain.
Match scope/name, generic formals and full parameter identity to one base,
then verify the complete result/access/effect/lifetime contract and
policy-domain tuple. An attributed declaration is a variant, not an ordinary
duplicate overload; multiple bases or duplicate tuples reject.
No matching by parameter names, abbreviated signature strings or source
order. This proposes a declaration contract, not an adopted attribute or wire
identity.
Do not allow import-attached variants. Omit a general policy query until its
subject is explicit; an active preference is not proof of selected behavior.

**C# comparison:**

```csharp
int result;
checked
{
    result = left + right;
}
```

C# selects checked arithmetic in that lexical context, including eligible
checked user-defined operators; it does not change arithmetic inside an
ordinary separately declared callee. This is not Terrum's exact policy-tuple
variant of an already selected base declaration or strict `require policy`
selection. C# has no direct equivalent of that complete variant contract.

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

**Terrum counterpart (query names are candidates):**

```terrum
const int32 count = 4;
const nuint headerBytes = SizeOf<Header>();
const nuint headerAlignment = AlignOf<Header>();
```

The first statement is established. `SizeOf`/`AlignOf` illustrate
**unapproved registered query identities** supplied by the language profile,
not arbitrary functions the compiler recognizes by their names.
For a `Header` with a published eligible layout, they would provide
target-layout facts: a cross-compiler queries its selected target ABI, not
the machine running the evaluator. The contract must say whether size is
the inline value/handle size, object storage size or another measure.
Do not let a incidental private layout become a portable constant promise;
frozen/published layout and an eligible complete type are prerequisites
for a query that promises that layout.

Three decisions remain distinct: which declaration/attribute/generic
contexts require constants; which values belong to an eligible published
constant domain; and which registered queries expose what facts.
Existing integer/Boolean/nominal-enum generic domains and guaranteed
primitive-wrapper contracts do not thereby admit arbitrary constructed
objects or CLR `Type` instances.

A required `const`/attribute/generic argument must evaluate successfully
under its checked constant rules or diagnose; it cannot silently execute
at runtime. An ordinary runtime expression may be folded only when behavior
is preserved. Query-specific registration must state whether runtime use
is available; there is no universal reflection object or general runtime
opt-out merely because the proposed query uses function-call notation.

**C# comparison:**

```csharp
static void Queries<T>()
{
    const int Count = 4;
    const int IntBytes = sizeof(int);
    Type runtimeType = typeof(T);
}
```

C# constant expressions admit the primitive `sizeof(int)` here, whereas
`typeof(T)` produces a .NET `Type` value, not a `const`. Terrum's checked
constant evaluator, published wrapper domains and compiler-known query
registration remain distinct from arbitrary reflection/library calls.
The approved parameter `nameof` use in result annotations does not settle
this full query catalog.

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

**C# comparison:**

```csharp
[DllImport("native", CallingConvention = CallingConvention.Cdecl)]
internal static extern int ReadDevice();
```

C# combines an external declaration with .NET interop metadata; this
calling-convention name belongs to the interop API. The example does not
define Terrum's ABI, effects, unsafe caller obligation, exception containment
or root/loan contract. A familiar attribute name is not a portable contract.

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

**C# lifecycle sketch:** `ReleaseOnce` below stands for an idempotent,
thread-safe resource release, not a standard API or a complete implementation.

```csharp
sealed class Handle : IDisposable
{
    public void Dispose() { ReleaseOnce(); GC.SuppressFinalize(this); }
    ~Handle() { ReleaseOnce(); }
}
```

C# finalization is GC-driven, not scope cleanup at a chosen time; explicit
`Dispose` is a separate path. Production .NET native-resource wrappers
normally use `SafeHandle` rather than infer safety from this sketch.
Terrum's deterministic owner cleanup does not imply finalizer syntax,
resurrection support or a policy for failure on a finalizer thread.

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

**C# comparison:**

```csharp
retry:
while (ready)
{
    if (tryAgain) { goto retry; }
    break;
}
```

C# labels target `goto`; `break` and `continue` cannot name them and instead
target their nearest eligible enclosing construct. Terrum's labels name
enclosing loops for structured transfers. C# label-scope rules therefore
do not choose Terrum's duplicate/shadowing rules or turn its loop labels
into arbitrary jump destinations.

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

**C# comparison:**

```csharp
/// <summary>Describes the widget.</summary>
[Obsolete]
public class Widget { }
```

C# XML documentation precedes the declaration, including its attribute
sections; the attribute does not sever attachment. Terrum's GFM documentation
model is already approved, but its remaining exact attachment, structured
heading validation and merge rules cannot be inferred from the XML tags.

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

**C# documentation-tooling comparison:**

```csharp
/// <see cref="Widget.Open(int)"/>
/// <inheritdoc cref="BaseWidget.Open(int)"/>
void Use() { }
```

Assume those referenced members exist. C# `cref` binds symbol/overload
references; `inheritdoc` is supported by documentation tooling, not a
language rule inheriting behavioral contracts. Terrum must specify complex
mentions/`xref:` resolution and inherited-document selection independently,
especially when several interface/default members are eligible.

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

**C# comparison, not an ownership-preserving equivalent:**

```csharp
record Owner(string Name, List<string> Items);
Owner first = new Owner("A", items);
Owner second = first with { Name = "B" };
```

With an existing list `items`, these records share `Items`; ordinary C#
record `with` does not deep-clone it or enforce unique-resource transfer.
Terrum cannot duplicate an affine field by copying the surrounding value.
Its consuming/borrowing update, source vacancy, and failure-cleanup choices
must be specified rather than borrowed from record cloning.

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

**C# comparison:**

```csharp
Span<int> values = [1, 2];
foreach (ref int item in values)
{
    item++;
}
```

The span enumerator's `Current` returns a reference, permitting C# `ref`
iteration; `ref readonly` instead exposes readonly element access.
Ordinary `foreach` copies the current value. None of these selects
Terrum's protocol-derived borrow/consume mode or permits a loan to survive
conflicting cursor advancement, capture or suspension.

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

**C#/.NET comparison:**

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
struct Pair { public int First; public int Second; }
```

`Pack` limits member packing alignment through a named .NET attribute
property; it does not request aggregate over-alignment. The example neither
chooses Terrum's positional packing/alignment arguments nor establishes its
published layout, `Blittable`, padding-validity or compatibility guarantees.

## Boundaries that are not unanswered source decisions

| Category | Treatment |
|---|---|
| Already settled | Raw quote fences/framing/newline directives/brace splitting; interpolation format alternatives and capture order; grouped callable/lambda clauses and contextual `return`; aliases' transparent export; optional `<this T>`; `.via<I>` and restricted default-body calls; parameter-directed result dependency annotations; vacant backing and explicit getter-only property initialization; constructor minimum guarantees; repeated unpublished init; params/default/named-argument order; tuples/constant indexing; optional/coalescing forms; switch transfers; eager async versus lazy `[Iterator]`; awaited structured close. Do not ask these again. |
| Explicitly deferred | Nominal `&&`/`||` derivation until a concrete need and complete protocol; declaration `static require`/`static ensure`; entry snapshots/history; nested nominal types; interface constructors; source type packs and zero/one-arity tuple extensions; collection comprehensions/repetition pending deeper library/tensor design. They are not pending votes in this inventory. |
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

### Q16. Extension declarations and call disambiguation

Provider selection approved; see the
[official grammar](../grammar-spec.md#extension-provider-selection).
Only the concrete declaration/import eligibility spelling remains in
[Q16.1](#q161-extension-declaration-and-import-eligibility).

### Q20. Declaring result, capture and backing dependencies

Parameter-directed result annotations approved, with `nameof` preferred;
see the [source contract](../grammar-spec.md#result-dependency-declarations).
Receiver/storage selectors, capture/suspension contracts and unannotated-result
defaults remain in
[Q20.1](#q201-remaining-result-capture-and-backing-contracts).

### Q22. Implicit storage initialization and completion hooks

Vacant backing and explicit property/constructor initialization approved;
see [backed properties](../grammar-spec.md#510-properties-and-accessor-local-effects).
Completion validation is declared after required initialization and before
publication; only its identity/registration remains in
[Q22.2](#q222-completion-hook-declaration-and-registration).

<a id="q221-construction-completion-hook-identity-and-phase"></a>

#### Q22.1. Construction completion hook identity and phase

Direction approved; see the
[completion contract](../grammar-spec.md#510-properties-and-accessor-local-effects).
The exact declaration/registration spelling remains in Q22.2.

### Q25. User-defined short-circuit operations

Explicitly deferred until needed and designed as a complete protocol;
see [operator boundaries](../grammar-spec.md#513-operators-assignments-and-expression-gaps).
This does not adopt C# `true`/`false` derivation or alter built-in Boolean
short-circuit ordering.

### Q26. Pointer formation, dereference and pin acquisition

The initialized `pin ref<T> name = ref place;` acquisition is approved;
see [pin/reference contracts](../grammar-spec.md#32-types-parameters-and-arguments).
Pointer conversion, warning/release policy and dereference/member choices
remain in [Q26.1](#q261-pointer-formation-and-dereferencemember-boundaries).

### Q31. Producer contract phases and suspension loans

Creation/completion versus advance/item obligations and declared suspension
dependencies are approved directions; see
[producer contracts](../grammar-spec.md#512-modifiers-cleanup-and-finalization).
Exact protocol/phase identities remain in
[Q31.1](#q311-producer-protocol-identities-and-phase-selectors).

### Q32. Catch eligibility and failure matching

Declared catchable contracts match in source order with checked filter-phase
access and normal filter-failure propagation; see
[catch contracts](../grammar-spec.md#33-statements-and-local-declarations).
Exact catchability registration and unwind timing remain in
[Q32.1](#q321-catchability-registration-and-filter-phase).

## Official comparison sources

These sources describe C#, not Terrum. Feature references may document a newer
C# release than the standardized chapter; their differing scopes are explicit.

- [C# lexical structure](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/lexical-structure): identifiers, keywords, literals and token formation.
- [C# using directives](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/using-directive): namespace-local imports, concrete aliases and alias qualification.
- [C# namespaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/namespaces): namespace declaration spaces and nesting.
- [C# classes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/classes): generic formals, members, constants, constructors and partial declarations.
- [C# expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/expressions): inference, overload resolution, conversions and conditional logical operators.
- [C# interfaces](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/interfaces): nominal conformance and most-specific inherited implementation.
- [C# `file`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/file) and [C# 14 `field`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/field): source-file visibility and contextual property backing, not Terrum's modifier matrix.
- [C# 14 extension members](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/extensions): declaration/import scope and static containers.
- [C# partial declarations](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/partial) and [source generators](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview): generated explicit members rather than live wildcard forwarding.
- [C# generic constraints](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/generics/constraints-on-type-parameters): constrained generic APIs, not conditional family conformance.
- [C# method parameters](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/method-parameters): `ref`/`in`/`out` and their slot/assignment rules.
- [C# patterns](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/patterns): pattern families and bindings.
- [C# index/range operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/member-access-operators) and [collection expressions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-12.0/collection-expressions): library-backed values, slicing and target-typed construction.
- [C# iteration statements](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/iteration-statements): deferred iterators and reference-returning enumeration.
- [C# checked contexts](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/checked-and-unchecked): lexical arithmetic context rather than callable policy tuples.
- [C# constants](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/const), [`sizeof`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/sizeof) and [type/cast operators](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/type-testing-and-cast): constant eligibility, runtime type values and user conversions.
- [C# external declarations](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/extern): external members and interop metadata.
- [C# finalizers](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/finalizers) and [.NET `SafeHandle`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.safehandle): GC lifecycle versus explicit resource release.
- [C# `with`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/with-expression): record/value copying, not affine transfer.
- [C# XML documentation tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags): source references and tooling-supported inheritance.
- [C# operator overloading](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/operator-overloading): C# 14 instance compound operators and the distinct older derived-compound rule.
- [C# user-defined compound assignment](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/proposals/csharp-14.0/user-defined-compound-assignment): receiver mutation and variable-classification restrictions; a feature specification, not Terrum's destination-loan contract.
- [C# `nameof`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/operators/nameof): compile-time names and parameter references within attributes.
- [C# pointer safety models](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code): distinguish the original model from the C# 15/.NET 11 preview's relaxed pointer formation; dereference still requires unsafe.
- [C# `fixed`](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/fixed): pinning duration and the declared pointer's scope, not an implicit pin acquired by an ordinary reference.
- [.NET `INumberBase<TSelf>` implementation](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Numerics/INumberBase.cs): identity, destination-first and source-second checked/saturating/truncating creation; not C# cast ranking.
- [C# exception handling](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements): filter selection.
- [.NET user-filtered handlers](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/using-user-filtered-exception-handlers): explicitly specifies discarded filter exceptions and a false filter result for C#/VB.
- [C# attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/attributes): attribute identity, placement and arguments.
- [.NET member packing](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.structlayoutattribute.pack): `StructLayoutAttribute.Pack` and its member/derived type alignment limit, not Terrum aggregate over-alignment.
- [C# documentation comments](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/documentation-comments): XML documentation and member references, not Terrum's GFM source model.
