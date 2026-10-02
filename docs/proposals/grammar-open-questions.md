# Source-language decision inventory

> [!IMPORTANT]
> This inventory preserves the numbered source review and its scope boundaries.
> The [source grammar](../grammar-spec.md) remains authoritative for settled
> forms. This inventory does not reopen those choices or authorize a compiler
> to choose new semantics on the user's behalf.

This is a decision inventory, not a compiler backlog. Stable question numbers
are retained. All forty-two parent IDs
have only [canonical link redirects](#resolved-question-link-redirects) at
the end of this document, so old review links remain usable without mixing
their rules into a pending queue.

There are **0 pending numbered groups**. The numbered review is complete,
not the language implementation or its concrete semantic schemas. Deferred
features and library/metadata/proof work remain in the boundaries below.

## Boundaries that are not unanswered source decisions

| Category | Treatment |
|---|---|
| Already settled | Raw quote fences/framing/newline directives/brace splitting; interpolation format alternatives and capture order; grouped callable/lambda clauses and contextual `return`; aliases' transparent export; optional `<this T>`; `.via<I>` and restricted default-body calls; parameter-directed result dependency annotations; vacant backing and getter-only property initialization; constructor minimum guarantees and independent final completion validation; repeated unpublished init; load-scoped static groups/lazy fields, explicit constructor policy, deterministic field order, explicit sharing even for reads and phase-bound static init accessors; uniform reference/concrete/generic matching, checked runtime type capture and incremental post-unwind filter selection; params/default/named-argument order; tuples/constant indexing; optional/coalescing forms; switch transfers; eager async versus lazy `[Iterator]`; awaited structured close. Do not ask these again. |
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
pattern/extraction contracts are linked from [Q28](#q28-complete-pattern-and-type-test-forms).
Approval does not claim compiler support or adopt the concrete Core sum types.

### Q28. Complete pattern and type-test forms

Resolved; see [patterns and type tests](../grammar-spec.md#33-statements-and-local-declarations).
Matching is consistent for concrete/generic/reference/pointer forms and does
not form a reference. Runtime-only captures use checked type evidence and
ordinary generic constraints with supported execution. Witness/observation
schemas and implementation remain separate work.

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
Filter timing is resolved in
[Q32.1](#q321-catchability-registration-and-filter-phase).

### Q32.1. Catchability registration and filter phase

Resolved; [catch contracts](../grammar-spec.md#33-statements-and-local-declarations)
select incrementally after unwinding to each candidate's live scope.
Inner cleanup precedes type/filter testing, false continues selection,
and failures retain ordinary causal propagation without rollback or replay.
Failure transport, registration catalogs and native ABI integration remain work.

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
