# Source name-binding conformance cases

These cases apply [names, declarations, and lookup](../language/names-and-binding.md).
They assume otherwise valid bodies, admitted complete types, selected module
dependencies, and the stated access/extension registrations.
They specify source outcomes, not current compiler certification.

## Declaration spaces

| Case | Situation | Required result |
|---|---|---|
| NAME-01 | Two namespace bodies in one module declare `A.B` | Compose its declaration container, not their import scopes |
| NAME-02 | A namespace and a type both declare `Token` in one space | Diagnose cross-kind collision |
| NAME-03 | A type and an ordinary function both declare `Token` in one space | Diagnose, regardless of return/argument types or generic arities |
| NAME-04 | An alias and a function share a name in one space | Diagnose |
| NAME-05 | A nominal type and alias have equal name/arity and equal resolved target | Diagnose duplicate source declaration |
| NAME-06 | Two aliases have equal name/arity and equal target | Diagnose |
| NAME-07 | A nominal type and alias share a name but have different declared arities | Permit separate type-name entries |
| NAME-08 | A field/property and method share a member name | Diagnose |
| NAME-09 | Two partial parts contribute equal ordinary field names, even with disjoint `file` access | Diagnose duplicate members |
| NAME-10 | Checked primary/secondary parts of one nominal type have equal name/arity | Compose under the partial contract |
| NAME-11 | Equal nominal names/arity occur in different modules | Distinct identities; no cross-module partial composition |
| NAME-12 | Explicit `IReadable.Read` and `IWritable.Read` implement distinct interface members | Preserve their distinct identities; do not create duplicate unqualified members |

## Generic names and arity

| Case | Situation | Required result |
|---|---|---|
| NAME-13 | `Box` and `Box<T>` occur in one type-name space | Permit |
| NAME-14 | `Block<T>` and `Block<const int32 N>` occur in that space | Diagnose equal-arity duplicate |
| NAME-15 | Both `Box` and `Box<T = void>` are visible; use bare `Box` | Select arity zero |
| NAME-16 | `Box<T>` and `Box<T,U>` are visible; use `Box<int32>` | Select exact arity one before further checks |
| NAME-17 | Those two declarations are visible without arity zero; use bare `Box` | Diagnose ambiguous type name, even if only one constructor fits |
| NAME-18 | Sole `Task<TResult = void>` is visible; use bare `Task` | Expand the selected declaration to `Task<void>` |
| NAME-19 | Sole `Pair<T,U = bool>` is visible; use `Pair<int32>` | Select it and fill `U` from its declared default |
| NAME-20 | Sole `Box<T>` has no default; use bare `Box` in a type-only position without inference | Diagnose missing generic argument; do not invent a type |
| NAME-21 | Exact-arity declaration rejects a supplied formal kind/constraint while a larger-arity declaration could fit | Diagnose the selected declaration; no retry |
| NAME-22 | A type application supplies `_` or `default` | Count each as one written position, then obey its separate inference/default rule |
| NAME-23 | A generic list repeats a formal name, including type versus constant formals | Diagnose |
| NAME-24 | `Echo<T>(T T)` reuses its type formal as a value parameter name | Diagnose |
| NAME-25 | A local or lambda parameter reuses an enclosing generic type/constant formal name | Diagnose |
| NAME-26 | A method's generic formal reuses an enclosing type's generic formal name | Diagnose |
| NAME-27 | Separate sibling methods each declare generic `T` | Permit |
| NAME-28 | A generic `T` hides an outer nominal `T` | Resolve the formal in its scope; qualification can select the nominal declaration |
| NAME-29 | A method return type refers to its later-written generic formal | Resolve that formal; signature scope is not limited to text after the list |
| NAME-30 | A generic default refers to a later formal | Diagnose despite whole-declaration name scope |

## Lexical and imported lookup

| Case | Situation | Required result |
|---|---|---|
| NAME-31 | A namespace's own accessible `Parse` group competes with an imported `Parse` | Select the declared group at that level |
| NAME-32 | `A.Parse(int32)` and `B.Parse(float64)` are both imported; call `Parse(int32(1))` | Diagnose ambiguous imported groups before argument fitting |
| NAME-33 | The imported groups have different generic arities | Still distinct competing groups; do not merge |
| NAME-34 | Multiple imports/paths name the same semantic declaration or group | Deduplicate; do not create ambiguity or duplicate overloads |
| NAME-35 | Qualify `A.Parse(int32(1))` after NAME-32 | Select only `A`'s group |
| NAME-36 | A type and a callable with the same name come from distinct imported namespaces | Expression-name ambiguity; type-only lookup excludes the callable |
| NAME-37 | Distinct imported types have the same requested name/arity | Diagnose ambiguity, not first-import selection |
| NAME-38 | Imported type names differ in arity and one exactly matches the written arity | Select the exact-arity declaration |
| NAME-39 | A local named `Token` is a zero-argument callable; an outer type supports `Token(int32)` conversion | `Token(int32(1))` diagnoses that local invocation; no type retry |
| NAME-40 | A selected nearer callable group has no applicable overload; a farther group does | Diagnose the nearer group; no outward retry |
| NAME-41 | A noncallable local `Token` hides a type in expression lookup | `Token(value)` diagnoses, while `new Token(...)` uses type-only lookup |
| NAME-42 | A block local is referenced before its declaration while a containing member has that name | Diagnose the local use; no member fallback |
| NAME-43 | Same-named namespace bodies in different files import different providers | Each body retains only its lexical import chain |
| NAME-44 | A qualified lookup's subsequent component is absent but an import elsewhere has that name | Diagnose the missing component; do not restart import lookup |
| NAME-45 | Only inaccessible declarations match an unresolved name | Diagnose access, not a fabricated value or target |
| NAME-46 | Selected dependencies expose two distinct semantic definitions with equal qualified keys | Diagnose ambiguity; equal text/filenames do not deduplicate them |

## Member access and candidate construction

| Case | Situation | Required result |
|---|---|---|
| NAME-47 | Outside a type, private `Measure` competes with an explicitly eligible accessible extension | Filter the private member; extension may apply |
| NAME-48 | Inside that type, its private ordinary `Measure` is accessible and applicable | Ordinary member wins |
| NAME-49 | An accessible ordinary method group is inapplicable, with an eligible applicable extension | Extension may apply |
| NAME-50 | Accessible applicable ordinary members are ambiguous, with a uniquely applicable extension | Diagnose member ambiguity; do not retry extension |
| NAME-51 | An accessible field stores a callable value and shares a spelling with an eligible extension | Invoke that field value's contract |
| NAME-52 | An accessible noncallable value member shares a spelling with an eligible extension | Diagnose invalid invocation of that member |
| NAME-53 | Explicit selection names an inaccessible member/provider | Diagnose access |
| NAME-54 | Two equally good eligible extension providers remain | Diagnose; explicit eligible provider selection can disambiguate |
| NAME-55 | The same interface member is inherited through several paths | Deduplicate by declaring-member identity |
| NAME-56 | Unrelated interface identities/defaults compete without checked resolution | Diagnose; do not choose by inheritance/import order |
| NAME-57 | An explicitly selected provider is accessible but not registered as eligible | Diagnose; naming it cannot grant extension semantics |
| NAME-58 | `r.Member` and `r->Member` for `ref<T>` | Search the descriptor and projected `T` respectively; no implicit dereference |
| NAME-59 | A selected caller-unsafe method is applicable outside an unsafe context; a safe extension also exists | Diagnose the selected operation's obligation; do not retry |
| NAME-60 | Inlining relocates a body using private/file members | Preserve the body's original semantic access principals |

## File-local and captured names

| Case | Situation | Required result |
|---|---|---|
| NAME-61 | File-local type declarations with equal name/arity occur in distinct source units | Distinct semantic identities, not partial composition |
| NAME-62 | A file-local and ordinary type with equal name/arity are visible in one unit | Diagnose collision |
| NAME-63 | A file-local callable group and an ordinary callable group share a name in one unit | Diagnose; do not combine their overloads |
| NAME-64 | A generic pattern's named type capture reuses an enclosing local, parameter, or generic formal name | Diagnose |
| NAME-65 | A pattern type capture is named later in its supporting space | Resolve that declaration; type use additionally requires retained checked evidence, without inventing a generic formal |

Lookup success does not prove initialization, valid capture, complete
publication, or a permitted ownership/effect contract. The
[local-name cases](source-local-declarations.md) and
[reference cases](source-references.md) apply those independent restrictions.
These cases allocate no new metadata records or extension-registration syntax.
