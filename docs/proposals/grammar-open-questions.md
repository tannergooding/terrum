# Remaining source-language decisions

> [!IMPORTANT]
> This is the pending source-decision queue. Recommendations are review
> inputs, not adopted rules.
> The [source grammar](../grammar-spec.md) remains authoritative for settled
> forms. This inventory does not reopen those choices or authorize a compiler
> to choose new semantics on the user's behalf.

This is a decision inventory, not a compiler backlog. Stable question numbers
are retained; gaps in the pending queue are intentional. Thirty-nine parent IDs
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

There are **5 pending numbered groups**. Q03 and all seven punctuation
subquestions have canonical redirects, not pending votes. Partially resolved
groups ask only about their remaining choice; settled context is linked or
labeled, not another vote.

| Pending area | Stable IDs |
|---|---|
| Declaration applicability and modifier order | Q06 |
| Pointer conversion, release and address diagnostics | Q26.1 |
| Other source forms and contracts | Q27-Q28, Q32.1 |

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

### Q26.1. Pointer formation and dereference/member boundaries

**Remaining question:** Choose implicit versus explicit pin-to-pointer
conversion, release/rebind spelling and any safe-context address-formation
warning policy. Pin acquisition/dependency safety and ordinary prefix
dereference with explicit member grouping are now approved in the
[official grammar](../grammar-spec.md#513-operators-assignments-and-expression-gaps).
**Example:** A successfully acquired `pin ref<int32>` roots and stabilizes
its backing for the pin loan's lifetime. `ptr<int32>` does not. Merely
casting/requalifying an ordinary reference cannot manufacture that pin.

**C# model:** Pointer operators and `fixed` are comparison points, not adopted
syntax. Distinguish the original C# unsafe model from the C# 15/.NET 11
updated-memory-safety preview: the latter allows pointer formation and
`fixed` outside unsafe, while pointer memory access still requires unsafe.
Terrum has its own checked provenance/backing dependencies and does not use
`pin` as a statement keyword.

**Settled direction:** taking an address need not itself require
unsafe when the place's lifetime, access and stable/rooted backing are
established. For GC storage, require a statically known covering pin;
independently stable storage still needs liveness and initialization
obligations. `unsafe` does not substitute for a missing root or pin.
A `pin ref<T>` to `ptr<T>` conversion is allowed in a safe context,
retaining its access and pin/backing dependencies. A safe-context warning
for address formation is a separate proposed diagnostic policy, not a
proof that the address is unsafe or permission to dereference it.

**Approved pin-acquiring local:**

```terrum
pin ref<int32> pinned = ref storage;
```

Here `storage` denotes eligible initialized storage. The initializer is
an explicit checked acquisition operation, not a qualifier-only cast.
The binding keeps its backing pinned for the promised local lifetime
or an explicit release/rebind. An address/conversion expression must designate
the pinned `int32` backing, not the storage of the reference descriptor;
whether the pin-to-pointer conversion is implicit or explicit remains open.
The local acquisition is approved target grammar, **not current compiler
support**. Implicit/explicit conversion and release spelling remain pending;
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

Prefix `*p`, grouped `(*p).M` versus `*(p.M)`, and the rejection of ambiguous
unparenthesized member chains are settled. No dedicated dereference fence,
pointer indexer protocol, `->`, or pointer-to-reference `ref(p)` form was
selected by that approval. Those alternatives are not additional pending
votes or implicit safe conversion paths.

### Q27. Nominal sums and closed-case declarations

**Remaining question:** Choose declaration and construction spellings for
explicit nominal tagged cases. Do not introduce `case`: switch arms already
omit it, and unions should not add a one-off keyword.
**Settled context:** Explicit closedness, exhaustive closed dispatch and
unknown evolvable payload safety are in the
[official grammar](../grammar-spec.md#4-accepted-language-direction).
Optional flattening remains separate.
**Example:** `enum LookupStatus : int32 { Missing, Found }` with a separate
`init ref<T?>` output illustrates why `Found(null)` differs from `Missing`.

**C# model:** Not a C# enum, inheritance hierarchy or `Nullable<T>` wrapper;
none supplies Terrum's owning tagged-payload contract automatically.

**Semantic example, not declaration syntax:**

```terrum
Lookup<int32?> missing = Lookup<int32?>.Missing;
Lookup<int32?> presentNull = Lookup<int32?>.Found(null);
Lookup<int32?> presentValue = Lookup<int32?>.Found(7);
```

The case construction above remains **illustrative, unselected spelling**,
not a new parser feature. It shows one nominal result: the first value has
no payload; the other
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

**Design constraint:** Composition must be clear and consistent with Terrum's
existing types, places, access and ownership. Do not adopt C#'s `x is { }`
or `{ X: > 10 }` composition by familiarity. Once-only scrutinee evaluation
does not settle member/extractor observation, refinement invalidation or
copy/borrow/move bindings. Tuple structure remains distinct from
name-discovered `Deconstruct`.

**Terrum counterpart (existing optional pattern only):**

```terrum
switch (Next())
{
    some item when Guard(item) => Use(item),
    _ => Other(),
}
```

This uses the approved optional payload form: call `Next()` once, evaluate
the guard only after matching, then select the corresponding body. It does
not introduce property, relational or general type patterns. Failures are not
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

### Q32.1. Catchability registration and filter phase

**Question:** At what phase relative to unwinding are checked filters evaluated?
Catchable-contract registration and source-order matching are settled directions;
transport bytes and exception object ABI are separate.
**Example:** `try { Work(); } catch (Failure error) when (CanHandle(error)) { Recover(error); }`
uses the approved typed/filter form within required unsafe recovery scope.

**C# model:** Syntax is C#-inspired, not an automatic `System.Exception`
hierarchy. A settled deviation is that a throwing filter does not silently
become false; it follows ordinary failure/chaining rules.

**Settled direction:** The
[official catch contract](../grammar-spec.md#33-statements-and-local-declarations) uses registered
catchable contracts, source-order selection and checked filter-phase access.
It forbids speculative reads of invalid unwound frames and silently erased
filter failures.

**Remaining question:** Select the exact filter/unwind phase. No particular
phase was recommended or approved; approval of registered catchability does
not choose C# pre-unwind timing. Concrete registration/type catalogs remain
library/metadata work, not an implicit `System.Exception` hierarchy.

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

<details>
<summary>Resolved decisions: legacy review links, not questions awaiting a response</summary>

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
Explicit lexical eligibility is settled; concrete declaration/import schemas
remain specification work.

#### Q16.1. Extension declaration and import eligibility

Resolved direction; eligibility must be declared and lexical, retaining
ordinary access/loan checks and ranking.
See [extension selection](../grammar-spec.md#extension-provider-selection).

### Q17. Adapter declaration and forwarding

Resolved; nominal adapters and forwarding are explicit. Opt-in generators
produce checked members, not a live wildcard; trivial forwarding should use
compact shared metadata without weakening member identity or contracts.
See [adapters](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).

### Q18. Family and conditional-member declarations

Resolved; family membership, conditional member guarantees and checked
projections are declared separately from extension lookup.
See [families](../grammar-spec.md#54-aliases-and-associated-types-declarations-and-open-binding).

### Q19. Complete parameter and reference qualifier matrix

Resolved access direction; slot/object authority, initialization, pinning and
dependencies are independent, including for reference-object handles.
See the [passing/storage matrix](../lifetimes.md#passing-and-storage-access).

### Q20. Declaring result, capture and backing dependencies

Parameter-directed result annotations approved, with `nameof` preferred;
see the [source contract](../grammar-spec.md#result-dependency-declarations).
Receiver/storage, backing and capture/suspension relationships have separate
targeted contracts, not an inferred extension of this parameter annotation.

#### Q20.1. Remaining result, capture and backing contracts

Resolved relationship direction; use distinct targeted contracts and the
language-versioned declaration defaults. Exact selector schemas remain work.
See [result dependencies](../grammar-spec.md#result-dependency-declarations).

### Q21. Reservation versus immediate reference formation

Resolved by correcting the premise: ordinary writable access is not an
exclusive loan. A scalar `Read` can finish before the outer `Update` call
without a general reservation mechanism. Explicit exclusive owner/guard
contracts remain separate.
See [call timing](../language-contracts.md#13-evaluation-and-loan-timing).

### Q22. Implicit storage initialization and completion hooks

Vacant backing and explicit property/constructor initialization approved;
see [backed properties](../grammar-spec.md#510-properties-and-accessor-local-effects).
Completion validation is explicitly registered after required initialization
and before publication; full and checked changed-state validation preserve
the same published guarantees.

<a id="q221-construction-completion-hook-identity-and-phase"></a>

#### Q22.1. Construction completion hook identity and phase

Direction approved; see the
[completion contract](../grammar-spec.md#510-properties-and-accessor-local-effects).
Concrete registration/schema encoding remains specification work.

#### Q22.2. Completion hook declaration and registration

Resolved registration direction; changed-state validation may reuse only
still-valid unchanged facts and must recheck every affected invariant.
See [completion validation](../grammar-spec.md#510-properties-and-accessor-local-effects).

### Q23. Nominal compound-operator declaration shape

Resolved; static void compound operations use `write ref<S>` for struct
storage and `write C` for class contents, distinct from handle-slot replacement.
See [compound contracts](../grammar-spec.md#513-operators-assignments-and-expression-gaps).

### Q24. Constructor and conversion eligibility/ranking

Resolved; `new T(...)` is construction, nonliteral conversion lookup is
destination-first and `.via<Owner>` explicitly restricts the eligible owner.
Ambiguity or selected failure does not trigger fallback.
See [conversion selection](../grammar-spec.md#513-operators-assignments-and-expression-gaps).

### Q25. User-defined short-circuit operations

Explicitly deferred until needed and designed as a complete protocol;
see [operator boundaries](../grammar-spec.md#513-operators-assignments-and-expression-gaps).
This does not adopt C# `true`/`false` derivation or alter built-in Boolean
short-circuit ordering.

### Q26. Pointer formation, dereference and pin acquisition

The initialized `pin ref<T> name = ref place;` acquisition is approved;
see [pin/reference contracts](../grammar-spec.md#32-types-parameters-and-arguments).
Only implicit/explicit conversion and warning/release choices
remain in [Q26.1](#q261-pointer-formation-and-dereferencemember-boundaries).

### Q29. First-class index/range values

Resolved; standalone forms require a registered value and consuming contract,
not universal `Index`/`Range` types or allocation.
See [contextual bounds](../grammar-spec.md#34-expressions-and-precedence).

### Q30. Nested collection-fragment construction

Resolved; target registration supplies fragment typing and any direct-
construction guarantee. Optimizations preserve memory observations, capture/
drain order, builder effects and failure/cleanup contracts.
See [collection construction](../grammar-spec.md#34-expressions-and-precedence).

### Q31. Producer contract phases and suspension loans

Creation/completion versus advance/item obligations and declared suspension
dependencies are approved directions; see
[producer contracts](../grammar-spec.md#512-modifiers-cleanup-and-finalization).
Concrete protocol catalogs and phase encodings remain library/metadata work.

#### Q31.1. Producer protocol identities and phase selectors

Resolved registration direction; creation, advance/item, final completion
and early-close obligations stay distinct under the declared protocol.
See [producer contracts](../grammar-spec.md#512-modifiers-cleanup-and-finalization).

### Q32. Catch eligibility and failure matching

Declared catchable contracts match in source order with checked filter-phase
access and normal filter-failure propagation; see
[catch contracts](../grammar-spec.md#33-statements-and-local-declarations).
Only exact filter/unwind timing remains in
[Q32.1](#q321-catchability-registration-and-filter-phase).

### Q33. Policy variant declarations and compile-time queries

Resolved; callable identity/full signature selects the base and a registered
semantic attribute supplies the exact tuple. General policy queries are
deferred until their subject is explicit.
See [policy selection](../grammar-spec.md#512-modifiers-cleanup-and-finalization).

### Q34. General constants and type/layout queries

Resolved domain/query direction; registered queries and required constants
need checked encodable guarantees. Unfrozen private layout cannot become an
independently persistent constant promise.
See [constant and query contracts](../grammar-spec.md#512-modifiers-cleanup-and-finalization).

### Q35. External declarations and convention annotations

Resolved contract direction; explicit import identity/convention preserves
the complete callable, safety, failure, rooting and loan contract.
Concrete registrations/productions remain specification work.
See [external declarations](../grammar-spec.md#512-modifiers-cleanup-and-finalization).

### Q36. Whether to provide GC finalizers

Resolved; finalizers remain absent without a concrete need and a separate
lifecycle/affinity/resurrection/failure/resource-safety design.
See [finalization boundary](../grammar-spec.md#512-modifiers-cleanup-and-finalization).

### Q37. Loop-label scope and shadowing

Resolved; labels have a separate namespace, reject active duplicates and
permit disjoint reuse. One label attaches to a loop on the same source line;
no stacked labels or arbitrary jump destinations.
See [loop statements](../grammar-spec.md#33-statements-and-local-declarations).

### Q38. Documentation attachment and structured headings

Resolved; documentation attaches through the declaration's attribute prefix
without blank source lines. Parsed top-level metadata headings use a finite,
case-sensitive vocabulary.
See [documentation grammar](../grammar-spec.md#514-documentation-comment-grammar-and-tooling).

### Q39. Complex documentation references and inheritance

Resolved; source name/type syntax and explicit overload signatures select
references, and inherited documentation explicitly selects a member.
See [documentation references](../grammar-spec.md#514-documentation-comment-grammar-and-tooling).

### Q40. Wither updates on unique owners

Resolved; explicit consuming/borrowing contracts preserve old/new ownership
and failure cleanup. No automatic copy-based record/wither synthesis;
author-selected generators can supply checked operations.
See [nominal contracts](../grammar-spec.md#4-accepted-language-direction).

### Q41. Iteration element-binding modes

Resolved contract direction; element access comes from the declared protocol,
with explicit consumption/writable borrowing and checked advance/suspension
dependencies. Exact protocol catalogs remain library work.
See [iteration](../grammar-spec.md#33-statements-and-local-declarations).

### Q42. Layout-control argument spelling

Resolved schema direction; the library attribute defines arguments under a
compiler-recognized, shape-checked semantic metadata mapping. No universal
positional overload is selected.
See [attribute contracts](../grammar-spec.md#31-source-file-structure-and-declarations).

</details>

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
