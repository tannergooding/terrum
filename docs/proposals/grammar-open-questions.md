# Remaining source-language decisions

> [!IMPORTANT]
> This is the pending source-decision queue. Recommendations are review
> inputs, not adopted rules.
> The [source grammar](../grammar-spec.md) remains authoritative for settled
> forms. This inventory does not reopen those choices or authorize a compiler
> to choose new semantics on the user's behalf.

This is a decision inventory, not a compiler backlog. Stable question numbers
are retained; gaps in the pending queue are intentional. Forty-one parent IDs
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

There are **2 pending numbered groups**. Q03 and all seven punctuation
subquestions have canonical redirects, not pending votes. Partially resolved
groups ask only about their remaining choice; settled context is linked or
labeled, not another vote.

| Pending area | Stable IDs |
|---|---|
| Other source forms and contracts | Q28, Q32.1 |

## Other source forms and contracts

### Q28. Complete pattern and type-test forms

**Question:** Confirm the remaining boundaries for runtime-only type capture
and already-reference-valued pattern subjects. Registered object extraction and richer-status
contract spelling remain separate library/schema work. Existing guards,
`and`/`or`/`not`, compatible `or` bindings and fallback behavior remain fixed.
**Accepted direction:** Replace the target `some` introducer with inferred
`var`/`var?` bindings. `var item` requires initialization and non-nullness;
`var? item` requires initialization but permits null. `is init` tests
initialization independently. Vacancy is not null and must not be read to
discover a nonmatch. Ordinary declaration inference follows the same
non-null/nullable distinction, without implicit unwraps or throws; generic
substitution keeps normalized optional semantics, not nested `T??` spelling.
See the [source contracts](../grammar-spec.md#33-statements-and-local-declarations).

Contextual multi-output matching views evaluate one call and support structural
output patterns such as `(var x, _)`, `(_, var? y)` and `(var x, var y)`.
They retain declared status/output correlations; output availability is not
call success, and ignored live owners still require cleanup. Omitted eligible
output arguments select the lifted projection, including partial omission;
supplied destinations stay ordinary arguments. With all outputs supplied,
matching examines the ordinary return value. Required inputs remain required,
and neither arm types nor implicit conversions determine the channel. See
[output matching](../language-contracts.md#23-checking-and-consuming-the-contract).
No mandatory library `Option`/`Result` or general implicit tuple conversion
is introduced.

Guards and catch filters use consistent `when expression`, with parentheses
as ordinary grouping. This does not change their checked effects/failures,
select catch-filter/unwind timing or replace function `require`/`ensure`.
Ordinary guard expressions provide member and relational conditions.
Dedicated member/relational pattern syntax and pattern `where` are deferred
until a concrete need, not pending adoption votes.

**Accepted lifted availability and requirements:** Matching uses declared
per-output availability and may bind initialized failure outputs. Boolean init
contracts already supply true publication/false vacancy; no additional matching
marker is required merely for that signature shape. Enum and object-backed
statuses retain checked availability relations rather than a universal
success gate. The call is evaluated once, preserving correlated initialization
facts for explicitly supplied destinations. Bare lifted `require` demands
initialization of every omitted/selected output, with full optional/ownership
types. Selective extraction uses an explicit pattern requirement, such as
`require (Read() is (var value, _));`. Ignored outputs need not be initialized
or read, but live ignored owners still require cleanup.

**C# model:** Pattern families are C#-inspired; Terrum does not inherit
property-getter duplication, implicit numeric conversion, or ownership-neutral
payload extraction merely from similar spelling.

**Design constraint:** Composition must be clear and consistent with Terrum's
existing types, places, access and ownership. Do not adopt C#'s `x is { }`
or `{ X: > 10 }` composition by familiarity. Once-only scrutinee evaluation
does not by itself authorize an observation optimization or invalidate
ordinary access/loan checks. Tuple structure remains distinct from
name-discovered `Deconstruct`.

**Terrum counterpart (accepted target direction, not current compiler support):**

```terrum
switch (Next())
{
    var item when Guard(item) => Use(item),
    null => Missing(),
    _ => Other(),
}
```

This uses the inferred non-null binding direction: call `Next()` once, evaluate
the guard only after matching, then select the corresponding body. It does
not introduce property, relational or general type patterns. Failures are not
nonmatches and cannot be silently retried.
The compiler still uses `is some item`; neither this switch form nor `var?`
inference/output matching is implemented.

**Accepted observation rule:** One read per member path within an attempted
arm feeds that path's subpatterns; a later attempted arm observes it afresh.
A false, effectful guard could have changed the backing: "evaluate once"
must not freeze every getter for the entire switch. Getters/extractors
retain ordinary effects/failures; do not call `Deconstruct` by name.
Ordinary member expressions written in guards/bodies retain ordinary reads,
not an implicit pattern cache. Optimizations must fit the memory/side-effect
contract; an explicit local lets the author retain a stable observation.

**Accepted binding rule:** Typed and inferred value bindings follow ordinary
value initialization: copy eligible value types or shared handles with their
complete access/rooting contracts. They do not create hidden aliases for
value-type structs. Aliasing requires an explicit reference form; unique
payloads require explicit borrow/move rather than an implicit copy.
`ref<var> item` and `write ref<var> item` are selected explicit borrowing
forms. They preserve ordinary addressability, authorized access, backing and
lifetime rules; requesting write does not create write authority. A matched
value does not authorize narrowing the actual aliased storage type.
Inside those reference forms, `var`/`var?` filter the actual initialized
storage type, not its current value: `ref<var>` does not match an `int32?`
slot merely because it contains `42`; `ref<var?>` matches and retains
`ref<int32?>`. For non-nullable storage, either form retains `ref<int32>`.
The writable forms follow the same rule. This nullable-storage decision is
settled; already-reference-valued subjects remain a distinct question.
Matching does not implicitly move during an attempted arm or its guard;
non-consuming inspection precedes an explicit move after selection.
Explicit guard effects are not rolled back, and ordinary loan/backing checks
still apply, as in equivalent `if`-style code.

**Accepted nominal-case spelling:**

```terrum
switch (result)
{
    Lookup<int32>.Found(var item) when item > 10 => Use(item),
    Lookup<int32>.Missing => Missing(),
    _ => Other(),
}
```

Patterns test the declared case and match payload positions in declaration
order and arity. They invoke neither a constructor nor name-discovered
`Deconstruct`, and add no `case` keyword or synthesized public payload members.
`Found(null)` remains distinct from `Missing`; `var?` permits its live null
payload. These source contracts do not claim executable union/pattern support.

**C# comparison:**

```csharp
var subject = Next();
var firstValue = subject.Value;
if (firstValue > 10 && firstValue < 100 && Guard(subject))
{
    Use(firstValue);
}
else
{
    var secondValue = subject.Value;
    // Test the next arm with this fresh observation.
}
```

This is an operational comparison, not adoption of C# property patterns or
record synthesis. An ordinary value binding is analogous to
`init T y; if (x is T) { y = T(x); }`: checked projection/initialization, not
a hidden struct alias or permission to select arbitrary implicit conversions.
An explicit reference binding corresponds to an ordinary reference alias,
subject to Terrum's access, backing and lifetime rules. C# does not enforce
Terrum's unique ownership.

**Accepted generic type-pattern direction:** `_` and each `var` form occupy
one argument slot of the named constructor, not a pack or an unapplied type
constructor. `List<_>` matches without naming its argument; `List<var T>`
captures a non-nullable argument type, and `List<var? T>` captures either
nullable or non-nullable arguments with their actual complete type.
Anonymous `var`/`var?` have the same filters without a name. These tests
inspect generic argument types, not initialization or values of list elements.
Ordinary application `_` inference remains distinct from a pattern wildcard.

```terrum
switch (items)
{
    List<var T> list when typeof(T) is typeof(int32) => HandleInt32(list),
    List<var? T> list => HandleOther(list),
}
```

The second arm includes other non-nullable arguments as well as nullable ones.
The direct `List<int32> list` pattern is the simpler equivalent for the first
arm. Type capture is distinct from value binding and permits `typeof(T)` in
the guard. A recognized exact identity test refines `T` on its proven path,
including the successful arm body, without a runtime conversion or rewriting
the generic declaration. Arbitrary user type-object comparisons do not
establish that proof. Captures retain ordinary access/ownership obligations
and have checked successful-path scope.

**Remaining boundaries:** Runtime-only type capture needs an explicit
type-witness/representation contract; it must not silently introduce boxing or
arbitrary runtime generic instantiation. Reference patterns retain the actual
storage type; nullable-storage filters are settled, but already-reference-value
cases still need reconciliation with reference formation. General
application/constraint `var` binders and higher-kinded application are deferred,
not inferred from pattern syntax. Registered object observations,
richer-status availability contract spelling and metadata remain library/schema
work; compiler adoption is downstream, not an additional source vote.
Call-site channel selection, partial output omission, availability-based
matching and all-lifted-output `require` are settled.

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
| Already settled | Raw quote fences/framing/newline directives/brace splitting; interpolation format alternatives and capture order; grouped callable/lambda clauses and contextual `return`; aliases' transparent export; optional `<this T>`; `.via<I>` and restricted default-body calls; parameter-directed result dependency annotations; vacant backing and getter-only property initialization; constructor minimum guarantees and independent final completion validation; repeated unpublished init; load-scoped static groups/lazy fields, explicit constructor policy, deterministic field order, explicit sharing even for reads and phase-bound static init accessors; params/default/named-argument order; tuples/constant indexing; optional/coalescing forms; switch transfers; eager async versus lazy `[Iterator]`; awaited structured close. Do not ask these again. |
| Explicitly deferred | Nominal `&&`/`||` derivation until a concrete need and complete protocol; extern accessor/operator/conversion forms until a concrete interop need and design; declaration `static require`/`static ensure`; entry snapshots/history; nested nominal types; interface constructors; source type packs and zero/one-arity tuple extensions; collection comprehensions/repetition pending deeper library/tensor design. They are not pending votes in this inventory. |
| Implementation work | Parser/binder/verifier/native lowering, compiler-component parity, lossless recovery and diagnostics, regression fixtures and support-version migration. Approval does not claim any of these are complete. |
| Metadata/ABI work | Wire rows, mandatory file/friend scope/access/provenance/grants, static group/lazy/policy/phase/sharing and unload contracts, witness/capture representation, dispatch tables, compatibility encoding, exception transport, public tuple/callable ABI, layouts and resource limits. These must carry chosen source contracts, not choose them. Existing reserved fields remain generically reserved. |
| Library API work | Static initialization-policy schemas, checked sharing and unsafe retry/suppression operations, external-data mappings, completion-validation registration, iterator/awaiter/close identities, scheduler/executor/cancellation APIs, task ownership surfaces, text/collection handler registration, primitive comparer names, allocator and tensor APIs. Review in the [core-library proposal](core-library-reference.md), with source dependencies above where needed. |
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

<a id="q06-visibility-and-modifier-applicability"></a>

### Q06. Visibility, modifier applicability and ordering

Resolved; see [declaration contracts](../grammar-spec.md#31-source-file-structure-and-declarations)
and [static initialization and load-scoped storage](../grammar-spec.md#static-initialization-and-load-scoped-storage).
Concrete semantic schemas, recovery APIs and wire/runtime machinery remain
separate specification and implementation work, not additional source votes.

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

Resolved; pin acquisition, acquire-before-publication/replacement ordering,
implicit pin-to-pointer conversion, local lifetime ending and pointer
formation/dereference diagnostics are approved target contracts. See
[pin/reference contracts](../grammar-spec.md#32-types-parameters-and-arguments)
and [pointer rules](../grammar-spec.md#513-operators-assignments-and-expression-gaps).
This does not claim executable pin/reference support.

#### Q26.1. Pointer formation and dereference/member boundaries

Resolved; failed acquisition leaves vacant storage vacant or preserves an
existing value and pin during eligible replacement. Successful replacement
still requires valid dependencies before retiring the old pin. Proven lifetime
escapes and missing required pins are errors; valid pointer formation alone
does not warn or require unsafe. Dereference still requires unsafe and the
specified prefix/member grouping. See the
[canonical pointer rules](../grammar-spec.md#513-operators-assignments-and-expression-gaps).
No empty-reference value, dedicated release keyword or alternative `ref(x)`
syntax is introduced.

### Q27. Nominal sums and closed-case declarations

Resolved; semicolon-terminated named cases, type-first payload parameters,
qualified `.Missing`/`.Found(value)` construction, explicit struct/class union
storage and the checked parameterless `[ClosedCases]` marker are approved
target forms. No `case` keyword or record
synthesis is introduced. See
[union declarations](../grammar-spec.md#31-source-file-structure-and-declarations).
Exact metadata/ABI representation and library attribute definition remain work;
pattern/extraction forms remain in [Q28](#q28-complete-pattern-and-type-test-forms).
Approval does not claim compiler support or adopt the concrete Core sum types.

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
