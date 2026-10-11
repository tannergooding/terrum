# Runtime requirement failures

This chapter owns logical `requirement-failures/1`: runtime failure-type
selection, ordinary zero-input construction, callable-owned defaults and
Core's minimum requirement payload. [Local failures](local-failures.md) own
Exception eligibility, propagation responsibility and catch bindings.
[Calls](calls-and-inference.md) and [construction cleanup](ownership-and-cleanup.md)
retain their ordinary rules.

The selected contract introduces no failure factory, implicit diagnostic
arguments, universal exception base or captured-failure API. It assigns no
physical attribute, payload, constructor, handler or operation-state codec.

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

## 2. Ordinary zero-input construction

On a failed runtime check, perform the source-equivalent ordinary construction
`new E()` with zero supplied arguments. E must satisfy the complete concrete
Exception payload contract, and that construction must be statically admitted
at the check's semantic code owner.

Use ordinary constructor lookup, accessibility, qualification, generic
formation, argument mapping, applicability and ranking. Published optional
argument defaults or an eligible empty params expansion may participate:
zero supplied arguments is not a new zero-formal-only overload rule.
No constructor is selected by name resemblance, lower allocation cost or a
private body. Required message, context or allocator parameters are not
invented. No factory, type-call conversion, union case, enum-zero value or
default-value fallback is tried after ordinary construction fails to bind.

Exception eligibility supplies no constructor evidence. `where T : [Exception]`
alone therefore cannot establish `new T()` or `require<T>`. A statically known
constructed failure family can expose its ordinary checked constructor under
its published constraints. An opaque formal still needs whatever constructor
evidence its admitted source contract can actually provide; this chapter
introduces no new generic constructor constraint or body-based inference.

Aliases and type recipes retain the exact complete payload, representation
and qualifications. A category interface is not payload storage. A raw
descriptor or optional wrapper is not made throwable by this construction.
An eligible enum/union still needs an actual admitted ordinary construction;
the annotation does not add one to its grammar.

Use an explicit conditional throw when construction needs inputs:

```terrum
if (offset < int32(0))
{
    throw new ParseFailure(offset);
}
```

Here ParseFailure is an independently eligible type with that ordinary
constructor, not a new standard failure identity.

## 3. Callable-owned defaults and the Core payload

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
object nor runs a constructor. A complete symbolic application uses ordinary
forward substitution; open generic syntax, unresolved inference holes and
cyclic aliases are not recipes.
Every admitted substitution must establish the complete concrete Exception
payload facts under the declaration's constraints. A malformed or ineligible
default is not excused by an explicit selector elsewhere.

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
selector does not excuse a malformed annotation. Constructor availability is
checked for the type actually selected at each check, not granted by an
unused default recipe.

`Terrum.RequirementException` is a public, nongeneric, zero-data nominal
struct with the checked Exception and ordinary Copy contracts and one public
zero-parameter instance constructor. That constructor establishes the complete
zero-data value without authored effects or contract predicates. This does
not make the propagation event Copy, select a byte layout, require heap
allocation or add message, parameter-name or stack-trace fields.
Provenance remains part of the independently owned propagation event.

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
[Exception]
public struct BadInput
{
    public BadInput()
    {
    }
}

[RequireException(typeof(BadInput))]
public void CheckOffset(int32 offset)
    require offset >= int32(0)
{
    require<RequirementException> offset != int32(13);
}
```

The precondition uses BadInput. The body check explicitly selects the
ordinary resolved RequirementException type. Neither constructor runs on
a successful check.

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

The constructed E's constructor has its own authored checks and defaults.
The outer requirement does not become an ambient constructor policy.
Recursive construction, nontermination or a constructor with no normal exit
retains ordinary semantics; no substitute failure is fabricated.

## 5. Evaluation, output state and responsibility

Evaluate the predicate or adapted call once under its existing source-order
contract. On success, publish the normal unit/output result without invoking
E's constructor or forming a failure event.
For lifting, initialized null and an initialized Boolean false remain valid
payloads; declared output availability is not a guessed status-success group.
A non-Boolean ordinary operand needs an explicit predicate.

On failure, construct E once. Only after complete ordinary publication of
that fresh result can propagation take responsibility and establish the
requirement check's fresh event/origin. Transfer a unique result without
Copy; never stage a partial or vacant E as a complete payload.
The standard construction, staging and delivery contracts retain a responsible
holder at every step.

Failure evaluating the operand propagates that actual failure without
invoking E's constructor. Failure in declaration initialization, constructor
preconditions/body/postconditions or final validation likewise propagates its
actual failure, with acquired-state cleanup and no fabricated outer E event.
If construction cleanup replaces that failure, retain the original cause
and its complete dependencies under the local-failure contract.
Later transport/resource failure follows the selected propagation contract;
it cannot restore a consumed source, return normally or guess another E.

Preserve every completed operand/construction effect and the call's real
destination states. A false requirement or constructor failure does not
vacate initialized explicit outputs, undo a write or turn an initialized
null into vacancy. Required output checks precede any owning extraction;
a failed all-output check must not read or move its unavailable slots.
Ignored live outputs and reusable hidden destinations retain their ordinary
supporting-scope ownership and dependencies, not full-expression retirement.

Perform partial cleanup and live-value retirement at their actual acquired
state/scope exits. A check does not replay completed callee cleanup, invent
a catch-local rollback, or prematurely retire a hidden owner still needed by
another value. Constructor effects may invalidate saved state/read facts;
normal flow invalidation applies rather than assuming the failed predicate
remains stable while construction executes.

## 6. Effects, phases and failure channels

Construction, defaults/params staging, static initialization, partial cleanup
and propagation participate in the check's ordinary access, caller-safety,
ownership, dependency and effect obligations. A safe Boolean requirement
does not grant authority for an unsafe constructor.
Exception/RequireException annotations imply neither Pure nor NoThrow.
A selected pure or valid-domain NoThrow form must establish the applicable
bound through all admitted false/construction paths, or prove those paths
absent. An internally handled failure retains its actual effects.

Normal check elimination still preserves operand effects, saved output/state
facts and every retained rule/default/constructor fact used by the proof.
Required constant evaluation follows its existing program/value/failure
rules; these runtime defaults do not change static proof failures into
runtime payload construction or authorize a new constant program.

A declaration precondition fails before entering the body, so an unentered
body finally/defer does not run. Already transferred arguments and initialized
constructor fields retain ordinary callee/partial-state cleanup.
For ordinary async execution, failure before operation establishment remains
synchronous; escaping check or constructor failure after establishment is
failed completion, even inline or before the first await.
Producer creation remains synchronous, while body/advance/close use their
already selected operation contracts. Moving generated code or resuming on
another permitted flow changes neither its default nor its failure channel.

## 7. Publication and independent admission

Retain the resolved Core/default schema and fallback identity, complete
selected type or substituted recipe, explicit-versus-default selection,
semantic check owner and phase, bound ordinary constructor and its published
signature/default/params/contract facts. Also retain complete value/state,
payload/provenance, dependency, cleanup and execution support.
An independent consumer must not rerun source-name overload lookup or invent
a factory from an attribute string.

Replacement preserves facts actually retained by the client: changing a
relied-on selected failure, constructor binding, source owner, output state,
effect or failure channel requires the ordinary compatibility check.
Unrelated additions are not universally breaking, and equal machine
signatures do not erase constructor or payload obligations.
Proof-dependent removal still retains the particular facts it used.

Trusted readers remain selective. Missing required Core identity, type,
constructor, default/phase, payload or cleanup support declines before
affected use; raw runtimePolicy, optional debug names and private code are
not complete records. Other payloads cannot stand in for missing Core support.
This logical selection allocates no Feature ID, Attr tag, opcode, TypeSpec,
constructor ABI or exception-region layout.

[Conformance cases](../conformance/source-requirement-failures.md) distinguish
supplied finite source/default/construction/state facts from actual compiler,
Core bootstrap, runtime roots, cleanup and physical-codec evidence.
