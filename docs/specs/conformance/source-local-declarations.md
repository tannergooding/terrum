# Local declaration-space conformance cases

These cases apply [local declaration spaces](../language/local-declarations.md)
and the [lexical identifier rules](../lexical-grammar.md#source-text-and-identifiers).
Fragments assume otherwise valid declarations, admitted types, and bodies.
They specify outcomes, not current implementation support.

| Case | Situation or fragment | Required result |
|---|---|---|
| LOCAL-01 | Declare `value` twice in one block | Diagnose duplicate local declarations |
| LOCAL-02 | A nested block redeclares an enclosing local `value` | Diagnose regardless of either declaration's source order |
| LOCAL-03 | A nested block redeclares an enclosing named parameter | Diagnose |
| LOCAL-04 | Two sibling blocks each declare `value`, without an enclosing declaration of that name | Permit independent names and value lifetimes |
| LOCAL-05 | Two independent callable bodies use the same parameter/local names | Permit; neither body's space contains the other |
| LOCAL-06 | One block declares `value` and `Value` | Distinct ordinal names |
| LOCAL-07 | An initialized local named `@await` is followed by another declaration of `@await` in the same space | Diagnose duplicate name; keyword escaping does not create another identity |
| LOCAL-08 | Use `value` before its later declaration in the same block | Diagnose use before declaration, not outer-name fallback |
| LOCAL-09 | `int32 value = Read(value);` | Resolve the local, then diagnose its uninitialized read |
| LOCAL-10 | `init int32 value; value = int32(1); value = _; init int32 value;` | Diagnose redeclaration; early lifetime end leaves the name in scope |
| LOCAL-11 | `init mut int32 value; Inspect(ref value);` | Resolve the name, then diagnose vacant ordinary borrowing |
| LOCAL-12 | `init mut int32 value; value = int32(1); Inspect(ref value);` | Permit naming and initialized borrowing under ordinary access/dependency rules |
| LOCAL-13 | A lambda parameter redeclares a containing local or parameter | Diagnose nested collision; a new callable scope does not hide the enclosing declaration |
| LOCAL-14 | Two sibling lambdas each have parameter `value`, with no enclosing local/parameter of that name | Permit |
| LOCAL-15 | A `for` initializer declares `index`; use it in that loop's condition, iterator, or body | Name is in scope; initialization and use rules still apply |
| LOCAL-16 | Use that `index` after leaving its `for` statement | No local binding from the departed header scope |
| LOCAL-17 | A loop-body local is used in the loop's condition or iterator | No binding from the body block |
| LOCAL-18 | An iteration binding's name is used in its own sequence expression | The iteration binding is not in scope there |
| LOCAL-19 | Two sibling catches use the same named failure binding | Permit independent candidate scopes |
| LOCAL-20 | A catch filter uses its own named failure binding | Name is in scope; failure/availability rules remain checked |
| LOCAL-21 | An accessor declares a local `value` inside its implicit `value` parameter's scope | Diagnose |
| LOCAL-22 | A lambda occurrence uses a later block local before that local's point of declaration | Diagnose even if intended invocation is later |
| LOCAL-23 | A pattern declares a name in a block and a later statement names it | Name remains in that block independently of matching; flow separately checks availability |
| LOCAL-24 | An initially vacant binding is read after its first match failed | Resolve its in-scope name, then diagnose vacant access |
| LOCAL-25 | An earlier argument names a local introduced by a later `init var` argument | Diagnose use before its declaration point |
| LOCAL-26 | A compiler's explanatory temporary spelling matches a user local's spelling | No source-name collision or new accessible temporary; compiler identities are separate |
| LOCAL-27 | A getter without an index parameter named `value` declares a local `value` | Permit; getters have no implicit `value` parameter |
| LOCAL-28 | An indexer parameter named `value` is combined with a setter or init accessor | Diagnose collision with that accessor's implicit parameter |
| LOCAL-29 | A deconstruction right-hand side reads one of the locals being declared | Resolve that local, then diagnose its uninitialized read |
| LOCAL-30 | A union-case constructor declares two payload parameters with the same name | Diagnose duplicate parameters |
| LOCAL-31 | Two `or` alternatives declare `item` in one supporting space | Diagnose duplicate declaration, even with equal types or `mut` |
| LOCAL-32 | Separate switch arms each declare `item` | Permit sibling scopes without an enclosing-name collision |

Composed/decomposed Unicode spellings remain distinct when the lexical profile
admits both; no normalization or case folding is inherited from a platform,
filesystem, or C# comparison. Source-unit, namespace/type/member collision,
import ambiguity, and generic arity have their own rules; these local cases
do not settle them.
