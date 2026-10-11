# Names, declarations, and lookup

This chapter owns namespace/type/member declaration spaces, generic formal
names, lexical and qualified lookup, accessibility filtering, and type-call
classification. [Local declarations](local-declarations.md) own block-local
spaces and declaration points. Argument inference and overload ranking consume
the resulting candidate group; they do not repair a failed name lookup.

Identifier equality is the [ordinal lexical policy](../lexical-grammar.md#source-text-and-identifiers),
without case folding or Unicode normalization. A semantic declaration identity
also includes its owning module and declaration space, and where applicable
its generic arity, callable signature, or file-local source-unit identity.
Equal text is not equal declaration identity.

## Namespace and member declaration spaces

Within a module, a namespace has one declaration space across its source
parts. Namespace declarations with the same qualified name compose that
container; their lexical import scopes do not merge. Each nominal type has
one member declaration space across its checked partial parts.
Declarations contributed by a referenced module retain that module's
ownership; equal namespace text cannot merge nominal definitions or partial
parts across modules.

| Declarations sharing a name in one space | Result |
|---|---|
| Namespace and namespace | Compose one namespace container, retaining each body's import scope |
| Type-name declarations | Distinct declared generic arities are permitted; an equal name/arity is a duplicate except for checked parts of one nominal declaration |
| Alias and nominal type with equal name/arity | Duplicate type-name declaration, even if the alias expands to that type |
| Alias and alias with equal name/arity | Duplicate, even if their targets are equal |
| Function/method and function/method | Form one overload group; equal signatures require permitted partial composition, not a second declaration |
| Namespace, type-name, ordinary value member, or callable from different categories | Diagnose a cross-kind collision; generic arity does not permit a callable to share a type or namespace name |
| Fields, properties, constants, union cases, or associated type members with an occupied member name | Diagnose unless an explicitly qualified witness declaration selects a separate interface member identity |

Type-name declarations comprise nominal types and transparent aliases.
Associated type members do not acquire generic declaration forms from this
rule. Nested nominal types and local named functions remain outside the
admitted productions. Alias expansion does not remove the alias's source
declaration or permit a duplicate name.

Callable overloads use normalized complete parameter and generic signatures.
[Reference identity](references.md#signature-identity) preserves initialization
modes and complete qualifications. Return type, parameter labels, defaults,
constraints alone, local binding `mut`, and the source `implicit` adaptation
marker cannot distinguish otherwise equal overloads. Private or file visibility
does not permit duplicate ordinary members in the same nominal definition.

Constructors, operators, conversions, and indexers have their designated
operation identities and signature groups, not ordinary identifier entries
created from the enclosing type name, operator token, or `this`.
An explicit implementation such as `IReadable.Read` resolves the selected
interface member identity. It is not a second unqualified `Read` declaration
and does not expose that implementation through ordinary concrete-member
lookup. Existing witness compatibility and inherited-default checks still
apply.

The [file-local source-unit rules](../grammar-spec.md#31-source-file-structure-and-declarations)
remain an explicit identity boundary: file-local declarations in a namespace
need not share identity across units. Ordinary and file-local declarations
that occupy colliding name entries and are both visible in a unit diagnose
rather than shadowing. Type-name entries use name/arity; same-named callable
groups do not merge across that identity boundary. File-visible nominal
members still participate in their type's ordinary duplicate-member checks.

## Generic formal names and scope

Type and constant formals in one generic list must have distinct names.
Renaming a formal does not create another generic overload or nominal type.
An inner generic formal, value parameter, or local cannot reuse an enclosing
generic formal's name. The restriction spans nested lambda bodies as well
as ordinary blocks; sibling declarations can independently use `T`.
A generic formal can hide an ordinary outer nominal type name, which remains
available through suitable qualification.

The formals are in scope throughout their owning declaration's signature,
constraints, contracts, and implementation, including the return type written
before a method's generic list. Type formals participate in type-name lookup;
constant formals participate as declared constant values, not runtime
storage that can be borrowed or rebound.

Scope is not permission to use a forward default: a generic default can depend
only on earlier formals under the [generic argument rules](../grammar-spec.md#32-types-parameters-and-arguments).
Attribute target and `nameof` declaration selectors can resolve the completed
declaration's formals without evaluating their values or reading vacant
destinations. Attribute arguments still require their resolved schemas.
Value arguments need eligible constants; the
[builder type-reference schemas](construction-handlers.md#type-reference-arguments-are-recipes)
bind the target declaration's formal identities in their ordinary type tree.

## Type-name arity selection

The declared arity is the number of generic formals, including type,
constant, and an explicit implementing-type formal. Formal names, kinds,
constraints, and defaults do not distinguish two type-name declarations
with equal name/arity.

At a selected lookup level, let the written arity be the number of supplied
generic positions, or zero when no angle list is written. `_` and `default`
each occupy a position.

1. Select the one accessible declaration with that exact name/arity, if
   present. Distinct semantic declarations with the same requested key are
   ambiguous; declaration or import order cannot select one.
2. Otherwise consider accessible declarations with the same name and greater
   declared arity, which can accept that supplied prefix. Exactly one must
   remain. More than one is ambiguous before defaults, inference, or argument
   domains are checked.
3. Only after selecting the declaration, fill its omitted positions using
   the ordinary defaults or inference permitted in that context. A missing
   required argument, wrong formal kind, constraint failure, or invalid
   default diagnoses without retrying another declaration.

A level with no declaration accepting the written positions contributes no
type-name candidate; lookup may continue to the next level. A level that
selects or ambiguously names a candidate does not continue outward.
This is type-name selection, not an exact-generic-arity preference for
ordinary function overloads.

For example, `Box` and `Box<T>` may coexist. Bare `Box` selects the nongeneric
declaration; `Box<int32>` selects the one-formal declaration. A sole
`Task<TResult = void>` still permits `Task`, which expands to `Task<void>`.
If only `Box<T>` and `Box<T,U>` are visible at the selected level, bare `Box`
is ambiguous even if a constructor argument could fit only one declaration.
Explicit selection of `Box<int32>` resolves the one-formal declaration
before checking construction or conversion.

These declarations collide rather than overload by formal kind:

```terrum
public struct Block<T> { }
public struct Block<const int32 N> { }
```

## Lexical lookup and imports

Lookup uses the syntactic context. A type-only position, including the target
of `new` or the operand of `typeof`, searches namespaces, type-name declarations,
type formals, and
eligible associated type identities; ordinary value bindings do not hide a
type there. Type-only lookup uses the same enclosing levels described below,
filtered to those categories. Named type captures occupy their supporting
declaration space; their use requires available checked type evidence under
the [structural-flow rules](initialization-and-flow.md#binding-pattern-declarations-and-scopes).
An ordinary expression name can denote a value/place, constant,
callable group, type name, or namespace receiver as its context permits.
Neither `(` nor the apparent argument types turn that lookup into a search
through all farther scopes for a callable.

Expression lookup searches enclosing local and parameter spaces from nearest
to farthest, including generic formals. It then searches the containing
nominal member context, followed by namespace lookup levels from the current
namespace through its enclosing prefixes to the root. An implicit instance
member still requires a valid receiver/capture and access contract; static
code cannot invent a receiver.

At a namespace level, accessible declarations in that namespace precede
names contributed by the applicable lexical imports at that level. If
neither supplies a candidate, continue outward. Imports belong to the
source file or actual namespace body that authored them; synthetic namespace
prefix levels do not acquire another body's imports. A nested namespace
uses enclosing lexical imports at their enclosing levels, not as declarations
in the nested namespace. File-scoped namespace imports after the namespace
head belong to its remaining body; preceding file imports remain file-scoped.

Filter candidates by the applicable access domain before determining whether
they hide outer declarations. Deduplicate repeated paths to the same semantic
declaration or overload group. Two distinct same-named function groups imported
from different declaration spaces are ambiguous, even when their parameter
types or generic arities differ. They do not combine into one overload group.
Qualifying the namespace/provider selects that group's declarations instead.
Distinct imported type-name declarations use the requested arity rules above.
A mixed type/callable imported name is ambiguous in expression lookup; a
type-only context does not treat the callable as a type candidate.

Once a level names a value, group, or type declaration, later invocation,
initialization, availability, constraint, effect, or lifetime errors do not
restart lexical lookup. In particular, a local found before its declaration
diagnoses under the local rules; it does not expose an outer member or import.
An unresolved name with only inaccessible declarations diagnoses access;
those declarations do not grant a fallback target or additional authority.

## Qualified and member lookup

A qualified name resolves its first component in the appropriate context,
then each subsequent component in that resolved namespace, type, or value's
member context. It does not search lexical imports for a missing subsequent
component. An ambiguous or invalid component terminates lookup.
Namespace qualification selects a container, not a module by filename;
distinct exports with the same qualified key remain ambiguous unless the
declared dependency set already selects one semantic definition.

Type receivers can select their eligible static/associated members and
designated operations. Value receivers use their complete declared type and
actual checked receiver authority. A witness selector resolves an existing
witness, and an extension selector resolves an explicitly eligible provider;
neither invents conformance or bypasses access. Inherited paths to one
declaring-interface member deduplicate by identity, while unrelated
competing identities/defaults require the existing explicit resolution.

For an ordinary receiver-member invocation:

1. Construct its ordinary member candidates from the declared receiver
   contract and filter inaccessible declarations. Use the caller's actual
   module, declaring-type, source-unit, and explicit friend principals.
2. A selected nonmethod value member is invoked as that value if callable.
   If it is not callable, diagnose; do not reinterpret the member name as
   an extension.
3. For an ordinary method group, determine applicability under the
   [call rules](../grammar-spec.md#52-exact-overload-grammar-ranking-and-disambiguation).
   Applicable ordinary members outrank extensions. Ambiguity within the
   applicable member set diagnoses without extension fallback.
4. If no ordinary method is applicable, search only the explicitly eligible,
   accessible extension providers and apply the existing argument ranking.
   Equal best providers diagnose; an explicit `via<Provider>` can select one.

An inaccessible private member therefore does not block an eligible public
extension outside its access domain. Inside the declaring type, its applicable
ordinary member wins. Explicitly selecting an inaccessible declaration still
diagnoses; accessibility filtering is not a grant to call it.
Invocation-only safety and contract failures after target selection are not
reasons to retry an extension.

References and pointers use [explicit indirection](references.md#dereference-and-member-access):
`r.Member` looks on the descriptor, while `r->Member` looks on its projected
referent. Lookup never inserts an implicit dereference or extra write
authority. Ordinary class-handle member access retains `c.Member`.

## Calls, conversions, and recorded binding

After name lookup, classify the selected target:

| Target | Invocation meaning |
|---|---|
| Callable group | Run applicability/inference/ranking within that group |
| Callable value | Invoke its complete declared callable contract |
| Type name in `T(value)` | Apply the registered literal construction or checked explicit conversion rules for that selected type |
| Type name in `new T(arguments)` | Select the declared construction operation separately |
| Other value or namespace | Diagnose an invalid invocation |

[Calls and inference](calls-and-inference.md) supplies the finite algorithm
after this selection, including mapping, explicit pins, typed/structural
and delayed callable evidence, contextual completion and overload quality.

There is no callable-to-type or type-to-callable retry after a target is
selected. In particular:

```terrum
// N.Token supplies a checked int32 conversion.
func<void> Token = () => { };
Token(int32(1));   // Invalid invocation of the selected zero-argument local.
N.Token(int32(1)); // Selects the type conversion instead.
```

Expected result types, contextual literal fit, parameter defaults, and
private bodies cannot choose a different name/group. Conversion-owner
selection remains [destination-first](../conversions.md#conversion-policies)
after the target type is resolved. Attribute, literal/parser, query, handler,
and compiler-protocol recognition likewise requires the resolved registered
declaration identity, never a matching name alone.

Source binding records the selected declaration/operation and closed type
and constant arguments, including any selected witness or extension receiver
mapping. Downstream linking, packing, JIT/AOT, and virtual dispatch do not
rerun source lookup. Rewriting retains semantic identity and access principals;
inlining cannot grant the caller a body's private or file authority.
The [logical metadata contract](../il-metadata.md)
preserves nominal/formal identity; this chapter allocates no new physical
record or encoding for open metadata schemas.

[Binding conformance cases](../conformance/source-name-binding.md) specify
the selected outcomes. Pattern-flow availability, complete-type substitution,
call applicability, and statement/label transfers remain owned by their
respective contracts rather than being inferred from lookup.
