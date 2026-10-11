# Local declaration spaces and scopes

This chapter owns local name collisions, lexical scope, and points of
declaration. [Source productions](../grammar-spec.md#33-statements-and-local-declarations)
define the admitted declaration forms; initialization, availability, capture,
and lifetime checks remain separate obligations.

Namespace/type/member lookup and protection of generic formal names follow
[names and binding](names-and-binding.md).
Names use the [lexical identifier rules](../lexical-grammar.md#source-text-and-identifiers):
ordinal, case-sensitive spelling, without Unicode normalization. A keyword
escape suppresses keyword recognition but does not create a second name.
The reserved `_` is never a declaration.

[Dependency recipes](dependencies-and-views.md#source-schemas-and-targets)
resolve an exact callable/member's formals even before its written parameter
list. They are not executable local reads and do not move a local's point of
declaration. Direct `this` names the receiver, while `nameof(@this)` can name
an escaped formal. Member-contract constraint recipes bind the selected
requirement's formals, not same-spelled locals or enclosing caller inputs.

## Declaration spaces

A callable's named value parameters, including union-case constructor
parameters, occupy its parameter declaration space. Its body and each nested
block have local declaration spaces. Lambda
parameters have their own callable parameter space, with the enclosing source
spaces retained for name lookup and collision checking.

Two local or parameter declarations cannot have the same name in the same
space or in lexically nested local/parameter spaces. Source order does
not avoid the collision: an inner declaration cannot shadow an enclosing
local or parameter even if the enclosing declaration appears later.
Separate sibling blocks or callable bodies may reuse a name when neither
contains the other and no enclosing local/parameter already reserves it.
Ending a value lifetime does not remove its declaration.

These rules include named constants, deconstruction, iteration, catch, and pattern
bindings within their specified scopes, including named type captures in
generic patterns. A type capture is not a generic formal, but its declaration
still cannot reuse an enclosing local/parameter or protected generic formal
name. They do not introduce local named
function declarations absent from the statement grammar.

For example:

```terrum
mut int32 value = int32(1);
{
    int32 value = int32(2);
}
```

diagnoses the nested collision. By contrast, the following sibling scopes
are independent:

```terrum
{
    int32 value = int32(1);
    Inspect(value);
}
{
    int32 value = int32(2);
    Inspect(value);
}
```

Members, nominal types, namespace declarations, and aliases
are not implicitly classified as local value declarations by this rule.
Their collision and lookup rules belong to the corresponding declaration
contracts. Enclosing generic formal names are separately protected against
local/parameter reuse. A local may hide a containing member where ordinary unqualified
lookup permits it; an explicit receiver or qualification still names that
member. This is not permission to shadow another local or parameter.

## Scope and points of declaration

| Declaration | Name scope |
|---|---|
| Callable value parameter | Its declaration's parameter/contract context and implementation body; nested functions can refer to it only under their capture rules |
| Ordinary, vacant, or constant block local | The whole declaring block for local name lookup and collisions |
| `for` initializer local | Its loop header and braced body, not following statements outside the loop |
| Iteration binding | That iteration's body, not the sequence expression or following statements |
| Catch binding | Its candidate filter and handler body, not sibling catches |
| Pattern binding | Its supporting block/header/arm/phase scope under the [structural-flow rules](initialization-and-flow.md#binding-pattern-declarations-and-scopes), independently of whether its value/type evidence is available |
| Deconstruction binding | The corresponding declaring block, subject to completion of the right-hand side and the binding's initialization |

[Lending iteration bindings](lending-iteration.md#mode-selection-and-element-bindings)
use that same body scope but alias an existing element place rather than own
a T local. Each true advancement creates its body activation after publication;
binding availability, loan retirement and escaping reborrow dependencies are
separate from element ownership. Ending the binding does not remove its name,
rebind it or vacate the element.

[Generated producer suspension](generated-producers.md#lazy-body-and-yielded-output)
does not change source scopes or recreate an existing body block. Captured
parameters and reached locals retain availability and lifetime across yields;
new block reentry has ordinary new activation. Creator contract-phase locals
do not become producer body locals or per-advance parameter declarations.
Generated code preserves the authored owner/source unit, not new access grants.

The point of declaration of an ordinary, vacant, constant, or deconstruction local is
its declarator, before its initializer or deconstruction right-hand side if
present. Lookup can therefore find a block local
before that point, but an occurrence there is an error; it does not fall back
to an outer member or imported name. A read in its own initializer similarly
fails because the local is not initialized. Side effects or future invocation
of a lambda do not move a source occurrence past the point of declaration.

For example, neither fragment is valid:

```terrum
Inspect(value);
int32 value = int32(1);
```

```terrum
int32 value = Read(value);
```

A vacant declaration introduces storage and a name, not a readable `T`.
`init mut T value;` has the same name scope and point of declaration as
`init T value;`; mutability does not affect lookup or remove initialization
requirements. `value is init` tests state without reading the vacant value.
A permitted initialization starts its value lifetime without redeclaring the
name. Ending that lifetime with `value = _;` leaves the name and established
complete type in scope.

A [constant local](proof-and-constant-evaluation.md#semantic-actions-and-normalization)
instead binds a successfully formed frozen Copy value. It reserves its name
under these same rules, but is not runtime storage that can be assigned,
source-retired, initialization-tested, or directly borrowed. Its own value is
unavailable during formation of its initializer; a self-dependent constant
does not gain a value by recursive evaluation.

An ordinary value-binding pattern introduces the same initialization-capable
storage; its explicit `mut` form additionally permits live replacement.
A failed match does not remove the name or retire an earlier successful
value. Availability at a join follows current state, not visibility alone.

`init var value` in an initialization argument introduces a caller-local name
at that argument's declaration point in the supporting scope. Earlier
arguments cannot use it; subsequent uses still require declared publication.
Argument labels and `nameof(parameter)` declaration selectors name semantic
declarations, not reads of vacant parameter destinations.

## Implicit names and generated storage

A setter or init accessor's implicit `value` is a parameter for these
collision rules; declaring another local or parameter with that name in its
nested body diagnoses. An indexer's named parameters are in scope in each
accessor's clauses and body. A setter or init accessor cannot add its implicit
`value` when an index parameter already occupies that name. A getter has no
implicit `value` parameter. The contextual backing-storage keyword `field`
follows its accessor-specific rules, not ordinary identifier lookup.
Loop labels have their separately specified namespace and scope.

Compiler-only temporary and label identities do not occupy the user's name
spaces. Names in a [source-equivalent expansion](../overview/conformance.md#source-equivalent-shorthand)
are explanatory fresh names, not newly accessible symbols or permission to
shadow a source declaration. Each [materialization site's binding](../lifetimes.md#repeated-evaluations-and-reusable-locals)
uses the original supporting scope even when its storage declaration is shown
earlier than the reached assignment.

Name scope is not value lifetime or permission to capture, read, borrow,
move, or escape. Resolving an enclosing name in a lambda does not prove that
its capture is legal or that its value is initialized at the required point.
Likewise, a name remaining visible after a lifetime end does not make its
vacant storage readable.

[Conformance cases](../conformance/source-local-declarations.md) distinguish
the selected naming rules from these independent availability and lifetime
checks.
