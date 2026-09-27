# Compiler and tooling implementation

## Current boundary: syntax-17, native-10 through native-20, semantic v31 managed-backed Copy exports

`Terrum.CodeAnalysis` is a reusable C# compiler library. `terrumc` exercises
that library; it is not an independent parser or binder. Syntax snapshots remain
immutable and usable independently. The separate [native profiles](native.md)
binds a bounded scalar/compact-value language and emits Windows x64 executables through
verified structured operations. A clean syntax result is deliberately not a
valid-program claim: `var x = 1;` parses but semantic analysis rejects its missing
numeric context. General ownership/lifetime checking, general semantic IL/LMOD,
general managed graphs and standard-library execution are not implemented.
The current parser rejects attached numeric width suffixes with `TRM0006` and
has no prefix-cast production. It supports context-typed decimal, hexadecimal
and binary integer literals without a fallback type, numeric digit separators,
and checked numeric type-call conversions for `int32`, `uint32`, `int64`,
`uint64` and `char16`. Empty `0x`/`0X` and `0b`/`0B` prefixes diagnose as
`TRM0007` and `TRM0008`; malformed digit separators diagnose as `TRM0009`.
Hexadecimal and binary literals construct target-width bit patterns, while
decimal literals retain numeric range semantics.
Existing source function and local shadowing keeps its ordinary call-binding
precedence. Top-level one-`T` generic function templates can infer `T` for
eligible `Copy` structs and the scalar value types `bool`, `char16`, `int32`,
`uint32`, `int64` and `uint64` from typed arguments or a typed existing `out`
destination. Expected-result context fills only an unresolved `T` before
contextual argument binding; literals never supply a default type.
Explicit type arguments remain supported. Scalars support Copy-only
templates, without granting primitive operator witnesses. This does not
enable unique or managed `T`, constructor/method inference, ordinary
overloads, partial holes, or a public generic ABI. This uses the existing
call grammar and changes no source syntax or artifact version. Floating
literal binding, custom literal/string-target execution and `[ConstEval]`
remain unsupported.
Semantic v24 validates source-class/`ManagedNode` readonly-versus-writable
access and mutable-class receivers. Semantic v25 adds a distinct persisted,
non-exported `MutReceiver` contract for methods on non-owning compact structs;
source visibility may be public or private, but does not expose a mutating-
receiver ABI. Calling one on a named mutable local or its writable nested
struct fields, or forwarding the existing writable receiver through `this`
and its writable fields, borrows and updates that caller place,
including writes before a checked exception; default-readonly methods and
by-value arguments retain copy behavior, and the ordinary nonmutating receiver
ABI is unchanged. The verifier rejects forged receiver
loans from immutable places, and nested field writes within a valid receiver
loan are allowed.

Named mutable locals, their writable nested struct fields, and forwarding
through an existing writable `this` and its writable fields are supported
receiver places. A nested call such as `outer.Inner.Mutate()` requires
writable containing storage and fields at every step. The receiver reserves
its root before argument evaluation; arguments cannot invalidate that root.
Partially initialized receiver roots, temporaries, managed-class-field receiver paths,
rebinding `this`, public exported
mutating-receiver ABI and general references remain unsupported.
Semantic v26 persists the nested mutable-field loan path, including its root
and field identities; the verifier rechecks access, types, initialization
and mutability rather than trusting serialized byte offsets. Whole-local
non-owning receiver loans still use v25. Older module headers reject the respective
contracts; programs needing neither retain their existing semantic versions
and bytes. Primitive
buffer-view alignment and the `out`-to-`init ref<T>` migration remain scheduled.
Readers still normalize private managed artifacts from versions 18 through 23
without granting more authority than their original contract. Writer version
selection follows the required receiver contracts, not managedness.
Semantic v27 permits ordinary `mut` methods on unique structs through
initialized named mutable owners or forwarded writable `this`. The methods
may update mutable scalar/Copy fields; they cannot replace owning fields,
move `this`, or widen the readonly NoThrow cleanup contract. Receiver
reservation rejects argument-side invalidation before invocation, and
mutation neither transfers ownership nor adds a cleanup obligation.
Readonly unique loans cannot invoke these methods. Writers select v27 when
such methods are present; earlier headers reject them.
Semantic v28 extends nested mutable-field loans through fully initialized
unique owners and unique child structs. The same writable-path checks and
whole-root reservation apply; a borrowed child is not staged as an owning
argument or separately destroyed. Checked failures retain earlier writes,
and eventual recursive cleanup observes the updated state exactly once.
Semantic v29 permits the same writable owning-field paths for
`OwnedInt32Buffer.Set`, `TryAppend` and `Clear`, without widening the allowed
leaf or intrinsic target to arbitrary types or operations. Semantic v30 adds
lexical `SliceMutable` acquisition from those writable buffer fields. Range
arguments run once before activation and cannot invalidate the reserved root;
the active view excludes all other root access, including sibling fields.
Ending the scope or unwinding releases only the loan, not the buffer.
Partial-constructor mutation and owning-field replacement remain unsupported.
Earlier artifacts retain their existing bytes when they do not
require a newer loan contract; older headers reject the respective broadened
contract even though the field-path encoding is unchanged.
Semantic v31 permits managed-backed leaf producers to export existing public,
handle-free compact `Copy` structs of at most 16 bytes by value as parameters
and results, and through the existing `bool`/`out` conditional-output contract,
including scalar outputs. The existing Windows x64 value ABI and `NoThrow`
contracts are unchanged. Public-field visibility and nominal type provenance
requirements apply to each `Copy` type that crosses an argument or result ABI,
not automatically to an export's declaring owner. Existing scalar-only static exports on public owner
types with private fields remain valid, and a static declaration on a unique
owner does not transfer that owner. Snapshots are independent values, not
references into private managed storage. Managed handles, unique values,
nested managed-containing values, views and mutating-receiver ABI
remain excluded, as does importing another producer from a managed leaf.
Writers require semantic v31 for these expanded export contracts; scalar-only
managed producers retain their prior required version, and older headers reject
the expanded contracts. This adds no syntax or scanner change and does not
change the native physical ABI or profile versions.

| Surface | Implemented | Not yet implemented |
|---|---|---|
| Text | Immutable UTF-16 source, half-open ranges, line mapping, exact malformed-editor-input preservation | Rope storage, byte maps, generated-source maps |
| Lexing | Pinned Unicode identifiers, ASCII decimal, `0x`/`0X` hexadecimal and `0b`/`0B` binary numeric tokens, digit separators, decimal fractional/exponent token boundaries, comments, leading trivia, strings, diagnostics including `TRM0006`–`TRM0009` for numeric suffixes, missing based digits and malformed separators, EOF; the scanner-4 integer record retains suffix metadata only for diagnostic-bearing tokens | Interpolation, raw/character/native literals, directives |
| Declarations | Using, file-scoped namespaces, functions, compact structs and `unique struct` with primitive/nested Copy/owning fields, bounded source classes including `mut class`, readonly default and writable `mut` class receivers, non-owning compact-struct `mut` methods with named mutable-local receiver writeback, explicit constructors/methods, checked `[NoThrow]`/`[DoesNotReturn]` and unique `[Cleanup]`; compilation-local static operator protocols and closed constrained functions; library binding and validated references, including combined leaf-producer [module sets](module-sets.md) and explicitly selected acyclic [native graphs](native-graph.md) | Inheritance, unique/inline classes, general interfaces, enums/unions, owning fields in Copy structs, general attributes/generics (struct and field attributes diagnose `TRM3001`), parameter-only `implicit ref<T>` loan/noescape contracts, public generic witnesses, cyclic dependencies, automatic version resolution and resilient module layouts |
| Types | Qualified named types, nested type arguments, one `?` per type nesting level, `void`; source-class and `ManagedNode` optional handles with `write` capability preserved through fields, parameters, results and inference; repeated `?` diagnoses | Scalar options, generic optional normalization after substitution, family defaults/conditional conformance, general ownership/form qualifiers and general borrowed references, transparent-wrapper ABI contracts |
| Statements | Initialized typed/inferred locals, `mut`, blocks, `if`/`else`, `while`, scoped `for`, nearest-loop `break`/`continue`, return, statement-form `require`, structured `try`/`finally`, unsafe checked `try`/`catch` (optionally with `finally`), assignment and expression statements | `foreach`, labeled transfers, general switch/patterns and wildcard patterns, context-specific discard destinations, declaration pre/postconditions, `static require`/`static ensure` proofs, typed requirement-failure selection, general exception objects/throw, async, compound assignment |
| Expressions | Names, raw literals, `()`, grouping, unary/binary operations, context-typed decimal/hexadecimal/binary integer literals without fallback (based values construct target-width bit patterns), numeric digit separators, checked numeric type-call conversions for `int32`/`uint32`/`int64`/`uint64`/`char16`, member/index access, calls, explicit closed static-protocol calls, bounded inference of one `T` for top-level generic function templates over `Copy` structs or existing scalar value types from typed arguments or typed existing `out` destinations (expected-result context fills only an unresolved `T`), generic signature-based result-type lookup, `new`, named whole-owner `move`, by-value unique arguments, conditional unique `bool`/`out` results, readonly unique and initialized owning-field loans, qualified source-class/`ManagedNode` writes and mutable-class receivers, named-place and writable nested-field loans for compact-struct `mut` receivers including fully initialized unique roots/children, named/ref/out argument syntax, checked synchronous `OwnedInt32Buffer` operations including `Set`/`TryAppend`/`Clear` and lexical `SliceMutable` through writable owning fields, and lexical readonly buffer slices | Floating/custom literal binding, registered literal/`[ConstEval]` execution, `require` output-expression form and exception-type selection, implicit `ref<T>` call-site loan introduction and temporary materialization, character literals, partial owning-field moves, owning field replacement, general expression temporaries, temporary/managed-class-field mutating-struct receivers, partial-constructor mutation, rebinding `this`, public mutating-receiver ABI, general stored loans/references, evaluation outside the native profile, user-defined conversions, unique/managed generic `T`, primitive operator conformance, general argument/result/partial-hole inference, constructor/method inference, ordinary overload declarations (rejected with `TRM3003`), contextual discard/lambda `_` forms, conditional expressions |
| Managed storage | Aliasable `ManagedNode` and source-class handles, optional edges and non-null pattern snapshots, readonly bare handles and explicit `write` capability, mutable-class receiver checks, qualified field/parameter/result/inference paths, handle-free compact Copy fields in classes, descriptor-based cycle tracing, complete-publication class factories, exact frame/temporary roots and explicit nonmoving collection under both policies, including exceptional root retirement and escaping-frame unlink | Managed handles inside user structs, stored interior pointers, implicit GC, native DLL/module graph managed-handle ABI |
| Tooling | Traversal, lossless reconstruction, deterministic content IDs, checked edit batches, structured CLI schema | Semantic queries, rename, formatter, generators/analyzer loading, LSP, incremental reuse |

Only the grammar below is supported. Unsupported constructs diagnose rather than
being ignored. Parser recovery is useful for partial files, but not a promise to
recover every later declaration optimally.

```text
file        = using* namespace? declaration* EOF
using       = "using" qualifiedName ";"
namespace   = "namespace" qualifiedName ";"
declaration = attributes modifiers (function | struct | class | interface)
attributes  = ("[" name "]")*
modifiers   = ("public" | "internal" | "private" | "static" | "abstract" | "mut" | "unique")*
function    = type name typeParameters? parameters constraints* block
struct      = "struct" name (":" type ("," type)*)? "{" member* "}"
class       = "class" name "{" member* "}"
interface   = "interface" name typeParameters "{" (attributes modifiers operator)* "}"
member      = attributes modifiers (field | constructor | function | operator)
operator    = type "operator" ("+" | "*") parameters (";" | block)
typeParameters = "<" name ("," name)* ">"
constraints = "where" name ":" (type | attributes) ("," (type | attributes))*
field       = type name ("=" expression)? ";"
constructor = containingTypeName parameters block
parameters  = "(" (mode? type name ("," mode? type name)*)? ")"
type        = "write"? (qualifiedName | "void") ("<" type ("," type)* ">")? "?"?
mode        = "ref" | "mut" "ref" | "out"
local       = localInitializer ";"
localInitializer = "mut"? ("var" | type) name "=" expression
statement   = block | local | if | while | for | tryStatement | unsafeStatement | return | require | assignment | "break" ";" | "continue" ";" | expression ";" | ";"
if          = "if" "(" expression ")" block ("else" (block | if))?
while       = "while" "(" expression ")" block
for         = "for" "(" initializer? ";" expression? ";" iterator? ")" block
initializer = localInitializer | assignmentClause | expression
iterator    = assignmentClause | expression
tryStatement = "try" block ("catch" block ("finally" block)? | "finally" block)
unsafeStatement = "unsafe" block
return      = "return" expression? ";"
require     = "require" expression ";"
assignment  = assignmentClause ";"
assignmentClause = expression "=" expression
```

Assignment is a statement, not an expression. Native places include initialized
mutable locals, accessible fields of mutable compact values, constructor fields,
conditional output scratch slots and checked writable host-buffer elements.
Unique local places additionally track vacancy and cleanup obligations.
Composite owning fields are assigned once along sequential constructor flow;
complete owners can be transferred, but partial field moves are unsupported.
`OwnedInt32Buffer` is an opaque, compiler-owned, move-only native resource with
fixed capacity and an initialized `int32` prefix. `TryCreate(capacity, out buffer)`
publishes on true only; `Count`, `Capacity` and `Read(index)` borrow
an initialized owner, while `Set(index, value)`, `TryAppend(value)` and NoThrow
`Clear()` require a mutable named buffer or a writable buffer-field path
through a fully initialized unique owner. The latter reserve that original
root before evaluating arguments: moving or replacing it within an argument
is rejected even if its slot becomes live again. Readonly nested queries
remain possible. Field mutation neither moves the child buffer nor creates
an additional cleanup obligation; active views of that root block mutation.
An `out var`
binding is readonly; explicitly move it into a `mut OwnedInt32Buffer` binding
to append or set. `ReadonlyInt32BufferView starts = buffer.Slice(start, length);`
checks that the range lies in the initialized prefix once, then permits
`starts.Length` and checked `starts.Read(index)` within its braced lexical
scope. The owner (including an owning field's root) cannot be moved, replaced,
dropped or mutated while any of its readonly slices is active; multiple
readonly slices and completed readonly queries may coexist. Slice start and
length are evaluated once before capturing the owner place; calls in these
expressions cannot invalidate that owner. A view cannot be copied, assigned,
passed, returned, stored in a field, declared as a for initializer or used
as a temporary expression. `mut MutableInt32BufferView window =
buffer.SliceMutable(start, length);` requires a named mutable buffer or a
writable buffer-field path through a fully initialized unique owner and grants
exclusive access to `window.Length`, checked `window.Read(index)` and
`window.Set(index, value)` until its lexical scope ends. While it is active,
no other view or owner operation may access, replace, move or drop that root;
range expressions can query the original owner before the view becomes active
but cannot invalidate it. Exclusion is conservative for composite owners:
even reads or independent views of sibling fields are blocked while the
mutable field view is active. Scope exit or checked unwind ends only the
loan, preserving prior element writes and the owner's cleanup obligation.
`Clear()` resets only the initialized count and
retains the allocation and capacity; it cannot run during any active view.
There is no resize, general aliasable view or generic
buffer or user-defined buffer representation. `TryAppend` is a bounded
initialized-prefix adapter for compiler components, not an implementation of
the proposed general `Buffer<T>`/`TryAdd` library contracts.
The `ManagedNode` proof copies handles without duplicating payloads or a
cleanup obligation. `ManagedNode.TryCreate(value, out var node)` publishes only
on true; `node.Value`, `SetValue`, `SetNext`, `ClearNext` and
`TryGetNext(out var next)` access a scalar and a single nullable internal edge.
Semantic version 24 applies readonly-by-default access to these intrinsics:
reads use readonly handles, while mutation requires a writable handle and
checked writable receiver. `write` qualification is preserved through optional
handles, fields, parameters, results and inference. A bare fixed handle cannot
mutate the node.
`ManagedHeap.Collect()` walks typed active roots across generated frames,
including staged call arguments, outputs and pending returns, then follows
descriptor-listed managed edges and reclaims unmarked cycles. This bounded collector runs only
at explicit calls and at executable entry teardown; it does not rely on
reference counts, stack word scanning or hidden allocation-triggered GC.
Managed handles cannot be placed in user structs or passed across external
calls, DLL exports or public persisted module contracts. Both runtime
policies admit closed executables/JIT and isolated native DLLs with scalar
exports and, under semantic v31, managed-backed compact handle-free `Copy`
exports; managed state remains private. DLL callers must serialize calls and
explicitly collect abandoned nodes before unload.
Checked unwind retires departed lexical roots after their nested user cleanup,
clears argument and unpublished output staging, and unlinks a root frame only
when unwinding out of that function. A same-frame catch keeps its surviving
roots; a pending managed return remains rooted through collecting cleanup.
Source classes use the same root and collection machinery. Their explicit
constructors initialize every field before the generated
`C.TryCreate(arguments, out C)` factory publishes an eight-byte handle.
Allocation failure returns false; checked constructor failure frees the
unpublished object and propagates. Collector-private zeroed reference slots are
not source defaults. Class payloads are bounded to 128 bytes; readonly/mutable
field access, visibility and partial-construction checks remain mandatory.
Class fields may contain acyclic handle-free Copy structs up to 16 bytes, including
nested compact values. Those fields are copied as complete values, not traced as
potential pointers. Unique owners, views and managed handles inside structs are
not admitted by this extension. Factories accept at most 63 by-value
scalar, managed-handle or handle-free compact Copy arguments. Copy arguments
are complete snapshots, including when later arguments mutate their source;
the factory forwards them through the existing direct/indirect aggregate ABI.
The ordinary 64-argument limit includes the factory's output or constructor's
receiver. Arguments beyond the register positions use ordinary stack slots.
Neither the provisional object nor managed arguments can disappear during
constructor collection. Copy payloads are not conservative roots.
Classes have no inheritance, inline class representation or public managed
module ABI; `new C(...)` is rejected rather than inventing an allocation exception.
Semantic version 24 records nominal mutability and `write` access for source
classes. A `mut class` may declare mutable fields and writable `mut` class
receivers; bare class handles are readonly and writes through a handle require
`write` authority in addition to mutable member declarations. A writable
child handle stored in a field retains that capability when read through a
readonly parent; no deep-read propagation is applied. Constructors initialize
the unpublished receiver and publish only after complete initialization.
Writable storage is invariant, output destinations require the exact qualified
type, and value-level `write`-to-readonly attenuation is allowed. These rules
also apply through nested handles subject to private/internal access and
partial-construction checks.
`native-20` adds `C?` and `ManagedNode?`, contextual `null`, non-null-to-optional
lifting, `is null` and `is some value`. Successful extraction creates a non-null
handle snapshot; clearing the original edge does not invalidate it.
Patterns participate in conditional flow, including short-circuit conditions,
`while` and `require`. The payload name belongs to the enclosing lexical scope;
reads additionally require proof that extraction succeeded on the current path.
The pattern does not narrow the original variable or an aliased field.
An initialized absent value is not an uninitialized
variable or an output from a failed call. Constructors must explicitly initialize
optional fields, and direct optional dereference is rejected. Optional scalar
types and generic normalization remain unsupported.
Version 18 semantic modules separately admit private `ManagedNode` code in a
leaf producer whose exported declarations are scalar and by-value only.
Version 19 extends that private-leaf contract to source classes and their
optional handles, serializing checked layouts, construction and root metadata.
Class-free modules retain the version 18 format. Classes with inline Copy fields
use version 20, retaining their private nested layouts and remapping type IDs
during semantic linking. Version 19 still rejects those fields and remains the
writer format for scalar/reference-only classes.
Classes whose constructors accept Copy structs require version 22, including
constructors that retain only scalar portions of those arguments. Older
versions reject aggregate constructor parameters. The native profile, scalar
export boundary and private-leaf dependency restriction remain unchanged.
Constructor signatures with more than three inputs require version 23; older
versions retain their original arity limit. High-arity functions retain the
existing page-safe frame bound and exact roots for potentially managed spills.
Semantic version 24 records managed class/`ManagedNode` access capabilities.
Managed artifacts requiring only this contract use v24; readers normalize
private managed artifacts from versions 18 through 23 into explicitly
qualified internal IR, translating legacy class/`ManagedNode` mutation
authority into `write` exactly where each older contract granted it. A v18-v23
header cannot contain v24 write identifiers. Methods requiring the distinct
v25 `MutReceiver` contract use v25 unless their bodies require v26 nested
mutable-field loan expressions, they mutate a unique receiver (v27), or
their field loans traverse unique roots (v28), or their mutable field loans
target the supported buffer intrinsics (v29), or they acquire mutable
buffer-field views (v30).
Older headers reject the respective contract
or expression. Programs needing neither retain their historical versions and
bytes regardless of managedness.
The private managed-state boundary and absence of a public managed-handle ABI
are unchanged. Semantic v31 expands leaf exports to managed-backed compact
handle-free `Copy` values by value; earlier semantic versions retain their
existing scalar-only export contracts.
Optional-node-only modules,
managed consumer declarations and managed imported/exported signatures still
reject.
Native packages validate its managed profile, heap imports and root metadata.
The leaf's collection is image-local and explicit; neither public managed
contracts nor DLL-unload collection are implemented. Unchanged persisted
consumer/intermediate images accept a compatible replacement under AOT and
root-only JIT.
The [source-line index candidate](../self-host/SourceLineIndex.terrum) uses `OwnedInt32Buffer`
to build and own a line-start table, with binary-searched UTF-16 positions checked
against host fixtures under both native failure policies.
General borrowed references and stored loans remain unsupported. The accepted
`ref<T>`/`ptr<T>`/`box<T>`/`val<T>` forms, general `write` views, `own<T>` and
`init ref<T>` invocation still require their respective representation,
lifetime, cleanup and ABI implementations; they are scheduled implementation
work, not deferred to the Later capabilities bucket. The current explicit
`bool`/`out` publication path remains in place until its coherent migration is
implemented and verified.
For-loop initialization runs once, followed by condition, body and iterator;
the iterator does not run after a returning or breaking body. `continue` runs
the iterator before testing the next condition. An omitted condition is true.
Initializer locals remain in scope through the header and body, not afterward.
The iterator cannot reference body locals. Multiple comma-separated clauses,
increment/compound-assignment operators are not implemented.
`break` and `continue` target the nearest enclosing loop. They run the normal
cleanup of exited scopes, including nested `finally` and owner cleanup, and
retire those scopes' managed roots; a `for` initializer remains live across
continue and retires when the loop exits. A transfer cannot escape a `finally`,
but a loop wholly inside that `finally` can use its own transfers.
Flow verification distinguishes loop transfers from return and fallthrough
when merging initialization and ownership states.
The binder retains the legacy `while` lowering for unaffected loops and a
distinct iterator target when a `for` needs it for `continue`.
Braces are required on control bodies. Supported precedence, strongest
first: postfix; unary `+ - ! ~ move`; `* / %`; `+ -`; `< <= > >=`; `== !=`; `&`; `^`;
`|`; `&&`; `||`. Binary operators associate left. Lexing does not fuse angle
brackets into shift tokens. Generic-call/comparison ambiguities are not solved by
guessing semantic types in the lexer. See [lexical grammar](lexical-grammar.md)
for precise number, string, identifier and newline rules.

`TRM1005` marks each extra optional modifier in `int32??`, `T??`, or
`T? /* trivia */ ?`. Recovery retains every modifier and continues parsing.
`List<int32?>?` and `T?` remain valid syntax; determining that `T` is already
optional and normalizing its substituted type is future binder work.
Explicit `Option<Option<T>>` syntax is unchanged. `syntax-1` introduced this
rejection rule; `syntax-2` added the lossless `RequireStatement` node.
`syntax-7` includes scoped for-loops, `try`/`finally`, parameterless declaration attributes and
legacy checked numeric prefix casts for supported 32- and 64-bit integer types and `char16`.
`syntax-8` adds `unsafe` blocks and the parameterless checked-policy `catch`,
with optional `finally`; a try requires at least one handler or cleanup clause.
`syntax-9` adds `unique struct` declarations and `move` expressions.
`syntax-10` adds static operator interfaces, explicit struct conformance,
generic function constraints and generic-name call expressions. Its executable
subset is deliberately compilation-local: each interface has three type
parameters and one `static abstract` binary operator declaration (originally
`+` or `*`, extended in syntax-17); an internal Copy
struct explicitly implements a closed homogeneous instance. Parameter/result
types, accessibility and declared effects must match. An internal generic
function has one `[Copy]` type parameter and returns that type or a supported
scalar. By-value parameters and initialized locals may mix that type with
supported scalars. Boolean-returning templates also support conditional `out`
parameters of either kind. The ordinary publication rules apply: true requires
all outputs initialized, false or an exception publishes none, and a caller
cannot read an output without proving success. Output-only type parameters are
permitted, but generic construction and a default value are not implied.
Structured bodies use assignments, blocks, `if`, `while`, scoped `for`,
`break`/`continue`, `require` and return; operations on the type parameter
must come from its declared operator constraints. `[Copy]` alone suffices for
helpers that only copy or select values; it grants no operators. The definition is checked
even when never instantiated. Concrete capabilities cannot make an undeclared
generic operation valid. Calls select a concrete Copy struct or supported
scalar, explicitly or through the bounded inference described above.
Inside a template, helper calls use its current type parameter, explicitly
or inferred, and every nominal protocol required by the callee must already be
declared by the caller. Identical operator signatures do not make distinct
protocols interchangeable. Each callee is checked independently, and callers
use its declared effects, not stronger facts inferred from its body.
Forward calls, imported local templates, and direct/mutual recursion reuse
closed specializations; recursion has the ordinary native call/stack behavior,
not a compile-time evaluator or a termination guarantee. At most 32 templates
and 32 total closed specializations, including transitively reached helpers,
are admitted. These lower to ordinary verified calls.
The [quadratic example](../samples/native/static-protocol-quadratic.terrum)
executes for two distinct wrappers. The [integer-power example](../samples/native/static-protocol-power.terrum)
combines scalar loop control with a constrained square helper and explicit
caller-supplied identity.
The [binary-protocol example](../samples/native/static-protocol-binary.terrum)
uses subtraction/division/remainder and explicit bitwise witnesses in
separately constrained helpers. Witness bodies keep ordinary checked arithmetic:
division by zero and signed division overflow fail; signed-minimum remainder
by minus one yields zero. Bitwise primitives themselves do not fail, but an
operator call still has its declaration's effects rather than effects inferred
from its body.
The [Copy selection candidate](../self-host/CopyAlgorithms.terrum)
selects complete values without requiring operator conformance.
Its `TrySelectCopy` variant publishes a selected value only for index zero or
one. Scalar return typing and output modes remain part of each helper's declared
signature during definition-site proof and closed specialization.
Copy-only templates also specialize for `bool`, `char16`, `int32`, `uint32`,
`int64` and `uint64`, using their existing concrete argument/result ABI.
This permits scalar selection and conditional output, not new arithmetic
rights on symbolic `T`.
General interface dispatch, primitive operator conformance, general generic inference,
concrete type arguments inside templates, member access on a
type parameter and public
generic/witness metadata remain unsupported. A public concrete wrapper may
use a private specialization without publishing its template or witnesses.
`syntax-11` adds source class declarations; `native-19` adds their bounded
construction and descriptor-based tracing contract.
`syntax-12` adds optional-presence patterns; `native-20` executes them for managed
handles with exact roots and explicit absence.
`syntax-13` adds nearest-loop `break` and `continue`, including scoped `for`
iterators. Semantic modules use version 21 when retaining these operations;
native profiles and exported contracts do not change.
`syntax-14` removes numeric prefix casts, adds checked primitive type-call
conversions, and gives decimal integer literals contextual typing. Attached
numeric width suffixes diagnose as `TRM0006`.
`syntax-15` adds contextual hexadecimal integer literals with `0x`/`0X`;
`0x1f32` is one hexadecimal value, and a prefix with no following ASCII hex
digit diagnoses as `TRM0007`. At syntax-15, based literals used nonnegative
magnitude/range interpretation; syntax-16 supersedes that with target-width
bit-pattern construction. The scanner-4 record layout and semantic/native
artifact versions do not change.
`syntax-16` adds `0b`/`0B` binary literals, digit separators, and target-width
bit-pattern construction for based literals. A missing binary digit diagnoses
as `TRM0008`, and malformed separators as `TRM0009`. The scanner-4 record
layout and semantic/native artifact versions do not change.
`syntax-17` extends homogeneous static binary operator declarations with `-`,
`/`, `%`, `&`, `|` and `^`, in addition to `+` and `*`. Each closed Copy-struct
conformance and generic use still requires the matching nominal protocol and
declared effects. Unary, comparison, shift and heterogeneous protocols remain
unsupported. These specializations lower to existing concrete calls: scanner-4,
semantic module versions and physical native ABIs are unchanged.
`[NoThrow]` on functions/constructors, `[DoesNotReturn]` on void functions and
`[Cleanup]` on eligible unique instance methods are currently bound;
unknown, duplicate and misplaced attributes diagnose rather
than becoming unchecked metadata. Attribute nodes retain their brackets and
name as three children. The `native-10` (fail-fast), `native-11`
(checked-exceptions) and `native-12` (compact unique owner) profiles support checked
signed/unsigned 32- and 64-bit integers, raw code units, conditional outputs,
fixed host-buffer capabilities and acyclic value layouts up to 16 bytes.
Fields are read-only after construction unless explicitly declared `mut`.
Binding and mandatory IR verification check mutation authority at every
nested field, independently of mutable local bindings and visibility.
Transitive module contracts and Windows x64 indirect argument/result storage
preserve those layouts. Statically rooted writable field paths enforce
initialization and visibility at every level. Larger aggregates, general
borrowed references and mutation through returned temporary values still
diagnose before emission.
`BoundFunction.IsNoThrow` is an explicit declaration guarantee checked by the
mandatory verifier, not inferred from a body for consumers. `TRM3017` locates
an operation whose throwing effect violates that bound. The bounded proof
accepts comparisons, bitwise operations, fields, total numeric conversions
and calls to other declared NoThrow functions. A host-buffer access is
NoThrow only with a statically constant index proven within that exact
compilation grant. It conservatively rejects checked arithmetic, potentially
failing conversions and general buffer indexes without range or invariant inference.
The [native contract](native.md) describes
these limits. `BoundFunction.DoesNotReturn` separately removes a call's normal
continuation. Its implementation must have no reachable return or fallthrough
(`TRM3018`). Constant-true loops, statically false requirements and declared
nonreturning calls can establish that proof. Unreachable syntax and IR still
undergo type and initialization checks; an unreachable return is not evidence
of normal completion. `BoundTryFinally` retains both regions, verifies NoThrow
cleanup independently of the function's own effect, and runs it on normal
fallthrough, returns and exiting loop transfers, inside-out. `TRM3019` rejects
returns and transfers escaping cleanup; a loop entirely inside cleanup may
exit locally. `TRM3020` conservatively rejects potentially executing cleanup
loops other than `while (true) { break; }`, and reachable `DoesNotReturn`
calls. This is not a termination proof for other NoThrow
callees. Return values are
snapshotted before cleanup; bool/out publication is checked after all cleanup.
Fail-fast checked failures do not unwind or run cleanup. Catch and other
effect annotations remain unsupported.
Source return diagnostics use that same mandatory continuation analysis,
including boolean negation and short-circuit constant feasibility; the binder
does not maintain a second, narrower return-path proof.
Existing single-source compiler JSON remains schema 1;
scanner and multi-source diagnostic schemas are versioned separately below.

## Public syntax API

Reference `sources/Terrum.CodeAnalysis/Terrum.CodeAnalysis.csproj`. Public APIs are
currently experimental; tools should pin the compiler and check schema/language
versions. No `InternalsVisibleTo` is used by tests.

```csharp
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

SyntaxTree tree = SyntaxTree.Parse(
    SourceText.From("void Main() { return; }", "main.terrum"));

foreach (SyntaxNode node in tree.Root.DescendantNodes())
{
    Console.WriteLine($"{node.Kind}: {node.Span.Start}..{node.Span.End}");
}

string original = tree.Root.ToFullString();
SyntaxTree edited = tree.WithChanges(tree.ContentId,
    [new TextChange(new TextSpan(5, 4), "Run")]);
```

`SyntaxTree.Parse(LexResult)` accepts a completed lexical snapshot, including
one produced by the compiled Terrum scanner. It validates the exact source
partition, token/trivia text, EOF and diagnostic shape before invoking the same
parser; it does not invoke the host lexer. The supplied component remains
responsible for correct classification. Normal source-based parsing uses its
own trusted lexer output without repeating this boundary validation.
`SyntaxTree.Parse(LexResult, ImmutableArray<ScannerIntegerValue>)` additionally
requires complete, ordered integer records for that stream. Binding uses their
magnitudes rather than decoding digits again, including signed-minimum operands.
Coverage and tag validation do not prove a scanner's arithmetic; the component
must be trusted, with differential fixtures checking its numeric behavior.
Raw token text, source identity and lossless reconstruction remain unchanged.

`SourceText` owns exact input text and a diagnostic path label. All library
offsets, line characters and lengths are UTF-16 units, not scalar/grapheme/display
columns; ranges are half-open. Lines and characters are zero-based. Only human
CLI diagnostics convert to one-based positions. Invalid UTF-16 is retained and
diagnosed by the library; file/stdin decoding is strict UTF-8 and rejects invalid
bytes before parsing. A UTF-8 BOM is transport metadata, not a source character.

`SyntaxTree` owns source, lexical tokens, root and diagnostics. Nodes/tokens are
immutable; trees hold no mutable global tables, symbol state or parent pointers.
`SyntaxElement` distinguishes nodes from token leaves. `Children` preserves
source order including punctuation. `Span` excludes outer trivia; `FullSpan`
includes it. Trivia belongs to the following token; EOF owns trailing trivia.
Recovery introduces zero-width, empty-text missing tokens or explicit skipped
nodes, never discards input. `ToFullString` reconstructs from leaves rather than
returning the original string. `Tokens` is the lexical stream; the root traversal
also includes parser-inserted tokens.

The lexer does not reserve `_`; the binder currently handles it as an ordinary
identifier and assignment. Contextual discard values, wildcard patterns and
generic type holes are not implemented, and existing expression statements or
unused-local cleanup do not supply those semantics.

This compact kind-based CST avoids two divergent parser models. It lacks Roslyn's
typed convenience wrappers and structural reuse today. Add generated typed
accessors over the same slots as grammar stabilizes, not a separate lossy AST
for AI clients. Syntax nodes are owned by one snapshot; references/JSON indices
are not stable symbol identities.

`WithChanges` takes an expected `ContentId` and ordered, nonoverlapping original
source ranges. It rejects stale content, duplicate-position insertions, overlap
and out-of-range edits before exposing a result. It returns a full reparse and
does not mutate a workspace or write a file. Content IDs hash exact UTF-16LE
code units using SHA-256, including unpaired surrogates; they exclude the path
and are not project/semantic identities or revision counters. A future workspace
must also compare document identity and its own monotonically increasing
revision before atomically publishing edits. The current API prevents applying
old offsets to different content; it does not resolve concurrent edits.

Cancellation is checked during lexing/parsing and edit application. Recursive
grammar nesting is limited to 128 active parser frames, with `TRM1004` on excess;
this counts implementation frames rather than language blocks. On this failure
the root retains all tokens in one skipped node. Long left-associated trees and
public traversal use iteration. Source length remains bounded by .NET strings
and signed 32-bit ranges. No performance claims or unmeasured latency budgets
are attached to this first implementation.

## Human and machine protocol

```powershell
dotnet run --project sources\terrumc -- check samples\syntax\hello.terrum
dotnet run --project sources\terrumc -- tokens samples\syntax\control-flow.terrum --json
dotnet run --project sources\terrumc -- tree samples\syntax\hello.terrum --json
dotnet run --project sources\terrumc -- analyze samples\native\factorial.terrum --json
dotnet run --project sources\terrumc -- build samples\native\factorial.terrum -o artifacts\factorial.exe --runtime fail-fast --json
```

Use the built executable or `dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll ...`
when stdout must contain only protocol output; `dotnet run` may print build
messages. `-` reads stdin. Commands exit 0 for no relevant errors, 1 for source
or compilation diagnostics, and 2 for usage/input/output failures. `tokens` performs only lexical
analysis. Human diagnostics go to stderr; JSON writes one object to stdout,
including failures. These inspection/build commands do not execute source.
`jit`, `scan-component` and `run-module` are explicit execution workers;
clients must supervise them with deadlines. JIT/module execution returns the
program's raw status, not the compilation-command status convention.
`run-native` validates its persisted consumer/producer packages and supervises
a separate native child with a 30-second deadline. It likewise returns the
program's raw status; a deadline failure reports `TRM5004`.

Schema 1 publishes `languageVersion`, coordinate system, path, length, success
and diagnostics. Parsed commands also include the content ID. Diagnostics have
stable codes, severity, message and source span/range; usage/input diagnostics
have no fictitious source position. Tokens include exact text, full/ordinary
spans, missing state and structured trivia. Tree output uses a flat node table,
root index and child references to node/token indices. This avoids JSON depth
limits on long expression chains and permits targeted consumers to index nodes
without recursively decoding source-sized nested objects.

Messages are human-facing and not stable dispatch keys. `TRM0xxx` denotes lexical
errors, `TRM1xxx` parsing, `TRM2xxx` CLI I/O/usage, `TRM3xxx` semantic
analysis, `TRM4xxx` native emission/runtime-host failures, and `TRM5xxx` bounded
module/scanner interchange errors. Schema changes require
versioning. Clients must reject unknown versions or explicitly negotiate them,
not infer meaning from enum ordinals or diagnostic English.

## Architecture evolution

The next front-end boundary is an immutable `Compilation` over source snapshots,
parse options, module/reference identities and expanded public contracts. A
semantic model resolves names/types/places and exposes structured queries:
symbol at range, required/proven obligations, call target, initialization and
escape facts, and why a diagnostic applies. Syntax is usable without binding.
The initial `Semantics.Compilation` already binds one syntax snapshot to
source-mapped, typed `BoundProgram` operations or diagnostics, never a partially
valid executable. It is not yet the multi-module semantic query service.
The multi-source overload declares all files before binding bodies, rejects
ambiguous entry points, and exposes `SyntaxTrees` and source-associated
`SourceDiagnostics`. Consumers must not interpret every multi-source diagnostic
against the legacy first `SyntaxTree` property.
Partial/error semantics must distinguish unknown, invalid and proven states;
absence of a diagnostic is not a safety proof.

Human/IDE and AI tools should consume the same model. A useful analysis response
includes snapshot identity, target/options, dependency identities, fact
provenance and related ranges, not merely generated explanatory prose. Edits and
refactorings return candidate edit batches and checked preconditions; callers
choose whether to publish. Semantic edits must recheck affected contracts and
diagnostics on the resulting compilation. Source comments or generated text do
not grant execution, filesystem, network or private-member privileges.

Generated source needs a stable document identity, producer/version/input
digest, authorization and a map from generated ranges to zero/one/multiple
original ranges. Preserve spelling and expansion locations separately rather
than forcing a lossy single-range mapping. Diagnostics may carry related
locations, rule arguments and suggested fixes tied to the same snapshot.
None of this source-map/semantic service is implemented yet.

Analyzer and generator APIs will be versioned consumers of snapshots, not
callbacks allowed to mutate compiler internals. Pin dependencies and input
manifests; isolate execution with explicit capabilities, cancellation and
resource bounds. Author-approved generation may access declared implementation
scope; consumer analyzers do not inherit those rights. Deterministic generation
requires explicit inputs, not hidden clock/environment/network reads. An
in-process interface alone would not be a security boundary.

Delay plugin discovery, persistent caches, remote services and query scheduling
until actual usage supplies requirements. Content identity already enables
safe exchanges; a later query cache additionally needs compiler/schema versions,
target, references, contract-rule version and authorization context. Changes in
serviced library bodies must not silently change a declaration-derived public
lifetime contract or a source program's validity.

## Binding and backend path

Keep diagnostics and source mapping through syntax -> bound operations ->
validated structured Terrum operations -> target lowering. The semantic layer
must represent values versus places, copy/move/borrow, initialization and
conditional publication, blocks/edges/block arguments, exceptional cleanup,
regions, loops and suspension. Mandatory validation checks terminators,
dominance, joins, region entry, cleanup and lifetime contracts. Irreducible flow
is represented honestly, not disguised as one natural loop. Cached analyses
are optional/versioned; validating structure is not optional.

LMOD is the future independently versioned Terrum module/contract artifact, not
a renamed CLI assembly or a PE file. The current backend emits Windows x64
machine instructions and PE32+ images directly, with no CIL/LLVM translation
or linker dependency. Source functions use a saved RBP frame base for
locals and spills, separate from the RSP-relative outgoing call area. Shared
PE/JIT unwind metadata describes both the saved register and allocation.
Cleanup has out-of-line funclets that borrow the source frame in RDX
while reserving their own call areas; they are invoked explicitly on normal
exits and by Windows on checked-policy exceptional exits. Versioned scope
records and filters admit only four compiler-generated checked failures.
`BoundProgram` retains structured blocks, branches,
loops, slot reads/writes and checked expressions; its verifier precedes
machine lowering. It is not a claim to implement the complete semantic IL.
Target-specific encodings, import fixups and unwind records live under `Native`,
not in binding or the public lossless syntax model.

An optional CIL/reference backend would need a concrete validation/deployment
benefit; it is not a prerequisite for native progress. The [JIT proof](jit.md)
reuses the encoder with W^X memory and dynamic unwind registration.
[Semantic modules](modules.md) persist verified bodies and normalized contracts
for bounded process-start replacement. Consumers bind against imported declarations
without producer source; private implementation layouts and helpers can evolve
without changing the persisted consumer. Native `.trmn` packages additionally
persist the EXE and DLL images, validate their contracts before launch, and
support compatible single-provider replacement without regenerating consumer
machine code. This is process-start servicing, not the general LMOD design or
hot replacement. Unsupported runtime/ABI capabilities diagnose rather than
inherit accidental CLR behavior.
Multi-source compilations are available through the API and repeatable
`--source` options on `analyze`, `build`, `build-library`, `jit`, `compile-module`
and `compile-native`.
Repeatable producer `--export` options select the module public surface.
Multi-source compiler JSON uses schema 2 with input-ordered snapshot identities,
diagnostic source indexes and, for successful builds, the native artifact.
Single-source and module-artifact response schemas remain unchanged.

## Design references and tradeoffs

These are specific prior art, not claims that another compiler lacks AI support
or that Terrum has already improved its performance or extensibility.

| Reference | Applied lesson and deliberate limit |
|---|---|
| [Roslyn syntax model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-syntax) | Immutable full-fidelity nodes/tokens/trivia and explicit recovery are the shared tool surface. Start with a compact CST, not premature red/green reuse or a parallel AI AST. |
| [Roslyn incremental generator design](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) | Declared immutable transform inputs enable caching. Defer pipeline APIs until we have binding and explicit generator authorization. |
| [rustc incremental queries](https://rustc-dev-guide.rust-lang.org/queries/incremental-compilation.html) | Cached results depend on actual query dependencies and their order, not just file timestamps. Don't serialize every cheap result or equate a source hash with semantic validity. |
| [Clang AST](https://clang.llvm.org/docs/IntroductionToTheClangAST.html) and [AST importer](https://clang.llvm.org/docs/InternalsManual.html#the-astimporter) | Retaining written syntax helps refactoring; semantic graphs and redeclaration/identity relationships are not interchangeable with a syntax tree. Keep our CST separate from later symbol graphs. |
| [LLVM language reference](https://llvm.org/docs/LangRef.html) | Explicit IR contracts and linkage distinctions matter. LLVM linkage is not Terrum lexical visibility, and this reference does not select LLVM as the backend. |
| [Runtime type loader design](https://github.com/dotnet/runtime/blob/bd6d336bfd95a333e8b3f22f26259bbb659974cf/docs/design/coreclr/botr/type-loader.md) | Type identity, metadata validation and loading phase affect compatibility and failure timing. Terrum contracts need an explicit binder/publication boundary instead of accidental JIT-time discovery. |

References were consulted on 2026-09-24; living design pages can change.
