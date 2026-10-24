# Runtime requirement failures

This chapter owns logical `requirement-failures/1`: runtime failure-type
selection, ordinary zero-supplied-argument construction, callable-owned defaults and Core's
minimum RequirementException contract. [Local failures](local-failures.md) own
Exception eligibility, propagation responsibility and catch bindings.
[Calls](calls-and-inference.md) and [construction cleanup](ownership-and-cleanup.md)
retain their ordinary rules.

The selected contract uses ordinary constructor binding for the selected
failure type; it adds no separate failure-factory mechanism, implicit diagnostic arguments,
universal exception base or captured-failure API. It assigns no physical
attribute, exception-class, constructor, handler or operation-state codec.

## 1. Runtime forms and selection

An explicit `require<E>` selects the complete E failure type for that runtime
check. The same optional selector applies to a statement, a value-producing
requirement and an individual declaration precondition. It does not select
the checked operand's type, call overload, omitted output or normal result.

| Form | Normal behavior; the failed path uses the selected E |
|---|---|
| `require<E> predicate;` | Ordinary once-only Boolean statement check |
| `require<E> operand` without omitted outputs | Checks its Boolean operand and yields `void`/`()` |
| A value-producing requirement with omitted init outputs | Checks their declared complete availability and yields the selected outputs |
| `require<E> (Call() is pattern);` | Checks that ordinary pattern result; ignored outputs are not required or read |
| A declaration `require<E> predicate` | Checks that precondition in its established precondition phase |

The selector is exactly one ordinary Type, not a value, factory, type pack,
inference hole or list of alternative failures. `<` attaches to `require`;
`require <E>` is not this selector. Interior trivia and nested closers retain
the ordinary type-argument rules. Omitting the selector uses the default below.
Clause order, predicate/body-arrow delimiters and all existing operand rules
are unchanged.

`static require` and `static ensure` remain compile-time proof requests, not
runtime constructors. They admit no failure-type selector. There is no
`ensure<E>` form: [runtime postconditions](../runtime/standard-failures.md#2-runtime-postconditions)
use fixed Core PostconditionException on a normally false predicate.
RequireException does not override it. Arithmetic, capacity and hardware
failures retain their separate mappings. The `require` marker on a
policy form retains its policy-selection meaning.

## 2. Ordinary zero-supplied-argument construction

On a normally false runtime check, construct the selected exception type
through ordinary constructor resolution with zero explicitly supplied
arguments. This does not invoke `E.Create()` or select a failure factory.
E must be a concrete managed exception class satisfying the exact exception
eligibility contract in [local failures](local-failures.md). That evidence
does not establish that a suitable constructor is accessible or applicable.
The selected ordinary binding may use a parameterless constructor, optional
parameter defaults, or an admitted empty `params` expansion under the normal
overload, accessibility, default-value and ownership rules.

Retain the exact selected constructor binding, including any default/empty
expansion records. A missing, ambiguous, inaccessible or unsupported
zero-supplied binding rejects the affected source/artifact; do not guess a
default, infer a constructor from the exception interface/body, retry a
different class, or fall back to a common factory. The constructor receives
no failed predicate, source text, parameter name, allocator or caller context
unless ordinary bound default/parameter rules explicitly supply it.
The built-in `UnreachableException` is a runtime failure payload, not an
`IException<E>` class, and cannot be selected by `require<E>`.

Aliases and type recipes retain the exact exception-class identity and
qualifications. `IException<_>` is an existential view, not a concrete class
to instantiate. A raw descriptor or optional wrapper is not made throwable
by a constructor binding.

Use an explicit conditional throw when construction needs inputs:

```terrum
if (offset < int32(0))
{
    throw new ParseFailure(offset);
}
```

Here ParseFailure is an independently eligible exception class with that
ordinary constructor for this explicit source path, not a new standard
failure identity. A failed `require` instead uses its selected
zero-supplied-argument constructor binding.

## 3. Callable-owned defaults and the Core exception class

Core supplies the resolved `RequireException` semantic attribute. It is
nonrepeatable, primary-callable-owned and has one required type-reference
argument named `ExceptionType`, written directly as `typeof(E)`.
Ordinary positional/named mapping admits either
`[RequireException(typeof(E))]` or
`[RequireException(ExceptionType: typeof(E))]`; unknown labels, duplicates,
missing/extra arguments and computed type-object values diagnose.
Parentheses may preserve the direct query category but cannot turn an
ordinary value, string name or user `@typeof` call into that recipe.

The argument binds in the primary callable's declaration environment,
including enclosing and own type/constant formals, under ordinary lookup,
effective accessibility and finite normalization. Prefix placement does
not capture caller formals or imports. It neither evaluates a reflection
object nor constructs the exception. A complete symbolic application uses ordinary
forward substitution; open generic syntax, unresolved inference holes and
cyclic aliases are not recipes.
Every admitted substitution must establish a complete concrete eligible
exception class under the declaration's constraints. The selected
zero-supplied constructor binding is resolved for each check that uses the
recipe; the annotation does not itself select or prove that binding. A
malformed or ineligible default is not excused by an explicit selector
elsewhere.

The schema applies to named functions/methods, instance/static constructors,
operators/conversions and individual accessor or Cleanup callables, including
eligible primary interface/generated-slot contracts. Existing role, effect,
safety and generation restrictions still apply. An exact `[get: ...]` on a
property/indexer targets its getter; duplicate inline/targeted placements
share one occurrence key. A whole-property placement cannot set every
accessor's default.

Type, module, assembly, field, parameter, result and secondary-part placement
cannot supply this callable contract. A newly declared primary member in a
secondary type part still owns its own contract. The schema is not a type
capability or a following conditional attribute clause. No lambda-attribute
production is introduced; a lambda uses the Core default or an explicit
selector on its own requirement.

| Priority | Selected type |
|---|---|
| Explicit `require<E>` | That check's resolved complete E |
| No explicit E; semantic owning callable has resolved RequireException | Its substituted type recipe |
| Otherwise | Core's exact `Terrum.RequirementException` identity |

All written attributes/types remain valid source contracts; an explicit
selector does not excuse a malformed annotation. Constructor availability
is checked for the type actually selected at each check, not granted by an
unused default recipe.

`Terrum.RequirementException` is the exact public nongeneric managed class
implementing `IException<RequirementException>`. Its common factory creates
the exception object using its type-owned `DefaultMessage`; the class also
has an ordinary zero-supplied-argument constructor binding for runtime
requirements. The selected ConstructorBinding uses that ordinary binding,
not the common factory. This does not make the propagation event `Copy`,
select a byte layout, require a particular allocation strategy or add a
parameter-name or stack-trace guarantee. The exception object itself is the
event and carries its ordinary runtime provenance/parent relationship.

The fallback selects the Core identity, not a source lookup for a similarly
named user type. The attribute's short spelling still uses ordinary resolved
attribute lookup. Same-spelled user attributes have no default-selection
meaning. Other compiler/runtime failure identities remain separate catalogs.
[Clamp's ordered-bound precondition](numeric-selection.md#4-reusable-invalidboundsexception)
explicitly selects exact Core InvalidBoundsException under these ordinary
construction rules. Its published predicate is min <= max, not the unordered-
accepting negation of min > max, and caller defaults cannot change the selected
type or its semantic precondition owner.

For example, with the shown declarations and admitted runtime support:

```terrum
[RequireException(typeof(BadInput))]
public void CheckOffset(int32 offset)
    require offset >= int32(0)
{
    require<RequirementException> offset != int32(13);
}
```

Assume BadInput is an eligible exception class with a valid
zero-supplied-argument constructor binding. The precondition uses that
ordinary binding. The body check explicitly selects the ordinary resolved
RequirementException type. Neither constructor runs on a successful check.

## 4. Semantic ownership is not physical execution location

Each runtime requirement belongs to the source callable or initializer that
authored it. A called helper does not inherit its caller's default. A separate
function or lambda does not inherit an enclosing callable, type or module
default, nor one from its contextual callable target.
An initializer outside an authored callable has no callable override and
uses Core unless it writes an explicit selector. In particular, declaration
field initializers do not acquire whichever constructor happens to run them.
Caller object-initializer expressions retain their own source ownership.

Declared interface clauses/default bodies retain their exact originating
member and recipe. An implementation's own body checks retain that
implementation's default. Dispatch, witness projection or adaptation does
not transplant one declaration's default onto another's authored checks;
the selected published clause contract must be preserved.

Ordinary async code retains its semantic owner before and after suspension.
A generated producer body retains its producer's default after moving into
cursor methods. Creation clauses keep their producer owner; separately
authored shell-member clauses keep their own member owner.
This selects no new callables or declaration scopes beyond the source grammar.

The selected E constructor implementation has its own authored checks and
defaults. The outer requirement does not become an ambient construction policy.
Recursive construction, nontermination or a constructor with no normal exit
retains ordinary semantics; no substitute failure is fabricated.

## 5. Evaluation, output state and responsibility

Evaluate the predicate or adapted call once under its existing source-order
contract. On success, publish the normal unit/output result without constructing
E or forming a failure event.
For lifting, initialized null and an initialized Boolean false remain valid
payloads; declared output availability is not a guessed status-success group.
A non-Boolean ordinary operand needs an explicit predicate.

On failure, execute E's selected ordinary construction binding once. Its
completely initialized result is the exception object and fresh event; do not
create a detached payload or second event wrapper. The constructor and
delivery contracts retain a responsible holder at every step.

Failure evaluating the operand propagates that actual failure without
constructing E. Failure in constructor initialization, its authored
preconditions/body/postconditions or final validation likewise propagates its
actual failure, with acquired-state cleanup and no fabricated outer E event.
If a constructor failure replaces a pending exception in a handler, the runtime
retains the pending object through `ParentException`; it does not fabricate
another E.
Later transport/resource failure follows the selected propagation contract;
it cannot restore a consumed source, return normally or guess another E.

Preserve every completed operand/construction effect and the call's real
destination states. Under the ordinary call/output contract, a false
requirement or constructor failure does not vacate initialized explicit
outputs, undo a write or turn an initialized null into vacancy. Outputs still
private or reserved under a selected protocol retain that protocol's
exceptional-cleanup and publication obligations. Required output checks
precede any owning extraction; a failed all-output check must not read or move
its unavailable slots.
Ignored live outputs and reusable hidden destinations retain their ordinary
supporting-scope ownership and dependencies, not full-expression retirement.

Perform partial cleanup and live-value retirement at their actual acquired
state/scope exits. A check does not replay completed callee cleanup, invent
a catch-local rollback, or prematurely retire a hidden owner still needed by
another value. Constructor effects may invalidate saved state/read facts;
normal flow invalidation applies rather than assuming the failed predicate
remains stable while construction executes.

## 6. Effects, phases and failure channels

Constructor execution, selected default/empty-params binding, static initialization, partial cleanup
and propagation participate in the check's ordinary access, caller-safety,
ownership, dependency and effect obligations. A safe Boolean requirement
does not grant authority for an unsafe constructor.
Exception/RequireException annotations imply neither Pure nor a failure-free
contract. A selected Pure form must establish its applicable effect bound
through all admitted false/construction paths, or prove those paths absent.
An internally handled failure retains its actual effects.

Normal check elimination still preserves operand effects, saved output/state
facts and every retained rule/default/constructor fact used by the proof.
Required constant evaluation follows its existing program/value/failure
rules; these runtime defaults do not change static proof failures into
runtime exception creation or authorize a new constant program.

A declaration precondition fails before entering the body, so an unentered
body finally/defer does not run. Already transferred arguments and initialized
constructor-owned fields retain ordinary callee/partial-state cleanup.
For ordinary async execution, failure before operation establishment remains
synchronous; escaping check or construction failure after establishment is
failed completion, even inline or before the first await.
Producer creation remains synchronous, while body/advance/close use their
already selected operation contracts. Moving generated code or resuming on
another permitted flow changes neither its default nor its failure channel.

## 7. Publication and independent admission

Retain the resolved Core/default schema and fallback identity, complete
selected exception-class type or substituted recipe, explicit-versus-default
selection, semantic check owner and phase, exact ConstructorBinding and
complete value/state, provenance, dependency, cleanup and execution support.
An independent consumer must not rerun source-name overload lookup or invent
a constructor binding from an attribute string.

Replacement preserves facts actually retained by the client: changing a
relied-on selected failure, constructor binding, source owner, output state,
effect or failure channel requires the ordinary compatibility check.
Unrelated additions are not universally breaking, and equal machine
signatures do not erase exception-class or factory obligations.
Proof-dependent removal still retains the particular facts it used.

Trusted readers remain selective. Missing required Core identity, type,
factory, default/phase, provenance or cleanup support declines before
affected use; raw runtimePolicy, optional debug names and private code are
not complete records. Other exception classes cannot stand in for missing
Core support.
This logical selection allocates no Feature ID, Attr tag, opcode, TypeSpec,
factory ABI or exception-region layout.

[Conformance cases](../conformance/source-requirement-failures.md) distinguish
supplied finite source/default/constructor/state facts from actual compiler,
Core bootstrap, runtime roots, cleanup and physical-codec evidence.
