# Windows x64 native profile

## Definitions and scope

Here, a **native profile** is a bounded set of source, verification, ABI, and
runtime capabilities accepted for native PE32+ emission. A **semantic
revision** identifies persisted typed-IR contracts; it is distinct from a
native-profile label. A **runtime policy** selects the failure and recovery
behavior emitted for a program (`fail-fast` or `checked-exceptions`). A
**native image** is the emitted Windows x64 executable or DLL, not the
compiler's managed host process.

This document specifies implemented native-profile behavior and its current
boundaries. The broader executable implementation status is in
[implementation](implementation.md); scheduled work is in [roadmap](roadmap.md).
The profile is deliberately narrower than the syntax grammar and proposed
libraries. It does not imply general ownership/lifetime checking, general
semantic IL, general managed graphs, or standard-library execution.

The additive [approved-TRRM scalar path](implementation.md#executable-trrm-scalar-interchange)
loads audited compact-linear/indexed Bodies into this same backend for JIT,
EXE and DLL output. Indexed merges and nested loops lower to direct native
branches; edge arguments are snapshotted before block-parameter writes.
Integer shifts and writable-local compounds/updates use immutable old and
replacement values through those same branches and loops. Equal-width integer
reinterpretation preserves bits without a numeric range check; checked shifts
still reject every count outside the operand width before the machine shift.
This does not add unchecked source casts, mutable TRRM places or a new opcode.
Module-local scalar calls use the existing direct-call fixups and scalar
register/stack ABI, including forward and recursive targets. Previously
evaluated arguments remain snapshots across later mutations or lazy branches.
Void calls execute without a synthetic value slot; call scratch/outgoing
argument space is included in the same frame analysis as value-returning calls.
Initialized scalar locals in lexical blocks, branches and loops use the same
immutable values and native lowering. Scope exits retain outer writes and
captured return/call values; edge snapshots include only target-visible source
identities, never uninitialized inner locals. Loop initializers execute per
iteration. Copy-local scope adoption does not enable lexical region kind `02`,
mutable places, cleanup or a new ABI.
Indexed `do` loops enter at the body before testing. Scoped `for` initializers
run once. Normal completion or `continue` targeting that loop reaches its
iterator before the next condition; break/return bypass it. Existing branches
and staged edge copies preserve local/call snapshots through these targets.
Non-nearest named `break`/`continue` in the source-free scalar path route
through designated single-exit indexed regions, carrying target-visible values
and preserving continuation order. Direct edges that bypass a designated exit
remain unsupported; nearest transfers keep their existing representation.
This adds no labels, opcodes, region kinds, widths, versions or ABI.
Indexed emission still requires a reachable loop backedge; always-transferring
bodies reject rather than acquiring synthetic edges or unaudited inactive
blocks. This source-free indexed path supports fail-fast only; the approved
Body subset does not
encode checked-exception dispatch, even though the native backend supports
that policy for the existing source and development-module paths. Loading
does not rebind producer source, bypass verification or introduce a new ABI.

Separately, current native scalar projection v85 admits named nearest and non-nearest
loop transfers under both runtime policies. It does not change the fail-fast
indexed-TRRM routing protocol or reader.

## Implemented profiles and versioned contracts

The version identifiers below state current implementation and validation
boundaries, not persisted compatibility requirements. Terrum is pre-1.0 and
alpha projection/profile formats may be retired or reshaped; retain only a
small selected set of older versions for comparison or validation, not as
compatibility commitments or roadmap milestones. Progress toward metadata
v1.0 does not require a version bump per increment; preview versioning, if any,
is decided explicitly when shipping a preview. Compiler evolution is summarized in
[implementation](implementation.md); scheduled changes are in
[roadmap](roadmap.md).

`native-10` (explicit fail-fast), `native-11` (explicit checked exceptions),
`native-12` (compact unique locals/returns), `native-13` (owned
by-value arguments and conditional outputs), `native-14` (composite unique
fields, including nested Copy fields, under either runtime policy),
`native-15` (compiler-defined owned native `int32` buffer), `native-16`
(lexical readonly slices of its initialized prefix), `native-17`
(lexical exclusive writable slices and in-place prefix reset),
`native-18` (closed-image managed-node tracing under either runtime policy),
`native-19` (source-defined managed classes and descriptor-based tracing),
and `native-20` (optional managed handles and presence-pattern snapshots)
are bounded executable profiles, separate from
the wider syntax grammar and proposed libraries. The compiler is hosted in
C#/.NET, but emitted programs are native PE32+ images with no CLR header,
CIL, LLVM, source translation or external linker dependency.
Persisted semantic modules currently write and accept only revision 55. The
semantic v24-v32 labels below are historical module revisions and implementation
milestones, not reader-compatibility promises; those revisions are retired and
rejected instead of normalized.
Revision 55 can carry the provisional `Host.Source` requirement for the
semantic-module JIT adapter only; it does not make source-dependent native DLL
or persisted AOT images executable.
Semantic version 24 carries the validated class/`ManagedNode` access
capability contract. Semantic version 25 adds the distinct `MutReceiver`
contract for persisted, non-exported mutating methods on non-owning compact
structs. Semantic version 26 adds nested mutable-field loan expressions.
Semantic version 27 permits ordinary mutating methods on named unique owners.
Semantic version 28 extends nested mutable-field loans through fully
initialized unique roots and children, preserving ownership and cleanup.
Semantic version 29 admits writable owning-buffer field loans for `Set`,
`TryAppend` and `Clear`, with no physical ABI change.
Semantic version 30 extends lexical `SliceMutable` acquisition to those fields.
Source visibility may be public or private. Named mutable locals, their
writable nested fields, and forwarding through an existing writable `this`
and its writable fields are supported receiver places. Current artifacts
record the validated access and receiver contracts directly; all use semantic
module revision 55 regardless of which supported constructs they contain.
Older revisions are not upgraded while reading.
The `native-18` through `native-20` labels below identify bounded runtime
features; they do not imply that general references/views, ownership forms, or
managed public ABI are implemented.
The current numeric surface accepts unsuffixed contextual decimal, hexadecimal
and binary integers, target-typed decimal and hexadecimal floating literals,
`float32`/`float64` unary signs and conversions between those formats,
`_` digit separators, and checked `T(expression)`
conversions for `int32`, `uint32`, `int64`, `uint64` and `char16`. Attached
width suffixes diagnose as `TRM0006`; missing hexadecimal/binary digits and
malformed separators diagnose as `TRM0007`–`TRM0009`. Based literals construct
target-width bit patterns; decimal literals retain numeric range semantics.
Based integers can also construct directly as floating values. Float-format
conversions follow IEEE-754 nearest/ties-to-even behavior; integer/floating
conversions, floating arithmetic/comparisons, custom literal parsing and
registered literal targets remain outside this executable profile.
The provisional hosted `char16`-to-floating conversion is also outside this
profile and does not expand the native scalar frontend or source-free TRRM.

The hosted compiler supports internal scalar `[ConstEval]` functions,
required `const T name = expression;` locals and nested scalar `const`
expressions. It substitutes successful required values before verification
and emission; ordinary calls remain executable. Checked arithmetic,
short-circuiting and branch/loop behavior follow the source contracts. The
64-call and 4,096-step limits diagnose during compilation; imported functions
cannot satisfy required constant calls. See the
[implementation reference](implementation.md) for the exact evaluator and
proof boundaries.

The current host parser language version is 46. The bounded native scalar
frontend is a separate source subset: its raw checks and source-bound audits
enforce supported spacing and attachment rules before projection selection.
It supports named loop transfers, but not optional types or managed handles;
those belong to the hosted compiler's broader native profiles. The scalar
frontend adds a bounded direct scalar-call subset: complete-program projection
supports self/global calls, single-function projection supports self-calls,
and v85 maps named by-value arguments while preserving source evaluation order.
Named output arguments, overload selection, attribute targets, general
binding, indexing, generics and dot-bearing forms remain excluded. Its `?`/`:`
spellings are conditional connectors only. Supported function headers and
scalar type calls require attached delimiters. String and format interiors
retain their lexical rules.
The compiled scanner and native input path support generalized comment fences
and closed-catalog `@keyword` names, preserving raw source ranges and name
identity.

Scanner-10/schema-10 appends `DotDot`, `DotDotCaret`, and `QuestionDot` token
kinds 104–106 after Scanner-9/schema-9 added `CharacterLiteral` kind 103.
Scanner-8/schema-8 diagnostic identities 11/12 for invalid and
defined-but-unsupported maximal runs remain unchanged. The hosted parser and
binder consume constant and dynamic `^offset` bounds for compiler-supplied
host-buffer indexing and `..`/`..^` ranges for readonly `OwnedInt32Buffer`
lexical views;
`?.` remains token-only. Scanner tokens do not imply support in the native
scalar frontend or general indexer/slice contracts. See the
[scanner contract](scanner.md) for spans and current record shapes. Hosted
role-specific punctuation rules are in the
[implementation reference](implementation.md). The bounded frontend produces
and accepts only v85: a 392-word single-function buffer or a separate 4,284-word
complete-program buffer, with fixed source/token/local/statement/node/parameter/
depth/work limits. Independent source audits check the current supported syntax
and layouts; older tags are rejected. Scanner, projection, semantic and
native-profile contracts remain separate; frontend projection tags are internal
implementation details, not product versions.
Conditional expressions evaluate one Boolean condition and only
the selected target-checked arm, with existing cleanup and rooting behavior.
The v41 feature milestone added retained conditional nodes, not a new native ABI.
Managed-handle `??` and statement-form `??=` use a single value/place evaluation
and lazy fallback. Optional assignment requires an initialized mutable local
or writable mutable class field; computed receivers remain rooted through
fallback calls and checked failure. Results retain existing nullability and
readonly/write contracts. The v42 feature milestone added retained nodes, with no native
ABI change or scalar/unique optional representation.
Prefix/postfix integer updates save the old value and commit a checked
replacement only on success. Supported mutable local, compact-field,
writable class-field and host-buffer places retain receiver/index
single evaluation, access checks and managed-root cleanup. Prefix returns
the replacement; postfix returns the saved value. Primitive update nodes
were added at the v43 feature milestone without changing the native ABI.
The hosted compiler also supports nominal static `++`/`--` calls for the
eligible writable `Copy`-struct place envelope. These preserve the same
single-evaluation and checked-writeback behavior: operator failure prevents
writeback but does not roll back earlier effects. The exact result type must
equal the operand type, unlike other unary operators. Existing static
requirements, self/no-self protocols, constrained generic definition proofs
and witness identity apply; there is no C# instance mutation, owning-value
update, arbitrary `char16` arithmetic, equality/default protocol or witness
ABI. Nominal calls were added at the v44 feature milestone after primitive
updates at v43. Private operator bodies and closed generic specializations can
be replaced behind unchanged semantic/native consumers. Cross-module
source-operator discovery and public generic witnesses are not added.
Nominal updates diagnose in required constant work: the ConstEval interpreter
still admits only scalar values, not struct construction/operator calls.
`char16` primitive updates, unique-value updates and nested owning-child paths
remain unsupported.
The hosted compiler supports primitive binary `<<`, `>>` and `>>>`, plus statement-form
`+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=` and `>>>=`
for initialized writable `int32`, `uint32`, `int64` and `uint64` places.
Binary shifts take one of those four integer types as the left operand.
Arithmetic and bitwise right operands must match the place type; shift counts
are `int32` and follow the existing checked shift-count rules. The destination
address and old value are captured before the right operand runs once, and
checked computation completes before writeback. Earlier place/right-operand
effects are not rolled back on failure. These operations reuse existing
update-place/root machinery; they introduce no destination-loan form.
Primitive scalar-slot compound assignments are available in bounded ConstEval.
Nominal compounds are rejected despite binary operator support; `char16` and
unique-value update destinations remain unsupported. Shift and
compound-assignment nodes were added at the v45 feature milestone (compound expression tag 26,
admitted only in statement position).
This adds no native ABI or profile version.

The executable native scalar frontend adopts these primitive operations in
current projection v85: ordinary integer shifts, all eleven compound assignments and
prefix/postfix updates of initialized writable named locals, including grouped
names. It retains the hosted `int32` count and checked count-range policy,
not the separate TRRM instruction contract. Shared update/compound nodes save
the old destination before RHS effects and commit only after successful checked
computation. Lazy branches and structured loops/returns use the same lowering
under both native runtime policies. Independent source validation rejects
forged places, types, source spans and older projection headers carrying new
operations. Actual DLL calls, JIT/EXE execution, byte parity and unchanged
persisted consumers cover the bounded component; no ABI/profile change,
general call syntax, field mutation or nominal operation is added.

The same v85 scalar frontend projections also carry same-format `float32` and
`float64` unary signs, target-typed decimal and hexadecimal floating literals,
based integer literals directly targeting either format, `+`, `-`, `*`, `/`
and IEEE comparisons. The hosted reader validates source token spans and
reconstructs exact target-format literal bits. It also projects conversions
between the floating formats and from `int32`, `uint32`, `int64` and `uint64`
to either format. V85 provisionally projects `char16`-to-floating and
floating-to-integer conversions under separate unapproved eligibility
proposals. Source-free TRRM floating operations are separately provisional
under unapproved proposals and remain outside the approved Body profile.
Verification and emission continue through the hosted implementation; this
adds no projection layout or native ABI change.

Only v85 is produced or accepted for single-function and complete-program
scalar frontend projections, every function frame and diagnostic records.
V1–v84 tags reject before body auditing. Compact expression and tree/statement
layouts, source mapping, type/mode/unsafe/named-call audits and the hosted
verifier/emitter boundary are unchanged; no version is bumped for this cleanup.
The following older projection labels document retired feature milestones and
their former restrictions, not supported input tags or compatibility readers.
The current reader has no historical-version selectors or feature-version
gates. Compact and tree layouts are both actively produced; no older layout is
kept merely for comparison.

Projection v50 additionally admits initialized scalar declarations inside the
component's existing braced branch/loop bodies. Lexical visibility, sequential
initialization, enclosing-name collision checks and writable authority are
validated independently of globally unique local slots. Loop reinitialization,
outer writes and scope exits reuse existing verified block/native lowering
under both policies. The eight-local function-wide identity budget and all
other component limits remain unchanged; v1–49 servicing and the native ABI
are preserved. No standalone blocks, vacant locals or general calls are added.

Projection v51 adds bounded braced scalar `do`/`for` loops. Shared lowering
preserves first-iteration and condition order, for-header scope and one-time
initialization, iterator execution on normal completion/nearest continue, and
break/return bypass. The independent source-bound reader validates clause roles
and local visibility before native execution. Proven non-fallthrough loops
need no synthetic return; reachable non-void fallthrough rejects. Both policies,
all existing upper bounds and v1–50 servicing remain; no ABI/profile/scanner
change, named transfers, standalone blocks or richer values are added.

Provisional projection v52 adds standalone lexical blocks using existing
brace syntax as statement kind 14: `slot` is the exclusive body-end index,
`root` is `-1`, and the span covers both
braces, with child statements in preorder. An independent source-bound audit
checks extents and scope before lowering to the existing managed `BoundBlock`;
failed validation publishes no partial projection. Versions 1–51 retain their
feature restrictions. The 392-word shape, record widths, existing bounds,
scanner IDs and semantic/native ABI remain unchanged. See the
[implementation reference](implementation.md) for name resolution and scope
rules. Projection v54 supports named transfers to enclosing loop headers; the
source-free indexed TRRM path uses its separate routing protocol, which remains
fail-fast-only and unchanged.

Provisional projection v53 appends non-void standalone `_ = expression;`
statements for the existing six primitive scalar types. Discard has no
contextual target type, so `_ = 1;` rejects while `_ = int32(1);` is valid.
The independently audited hidden readonly local preserves the expression's
existing single-evaluation, checked-failure and lazy-effect behavior and
counts toward the existing eight-identity limit. Versions 1–52 retain their
prior shapes and do not admit discard records; later projections may carry
kind 15 alongside newer kinds. This appends only a provisional frontend feature
version: source grammar (host language version 44), scanner-10/schema-10,
diagnostic/token inventories, semantic/ISA/Contract/TRRM formats, native
formats and ABI do not change. The exact record and publication contract is
in the [implementation reference](implementation.md).

Projection v55 adds standalone `require condition;` to the bounded native
scalar frontend. It uses the existing Boolean binding and checked-require
behavior, under both runtime policies, without a hidden local or new source
scope. Versions 1–54 retain their prior shapes; later projections may carry
kind 21 with earlier feature kinds. It does not extend the source-free
indexed-TRRM contract; see the [implementation reference](implementation.md)
for the bounded audit and flow rules.

Projection v56 adopts existing braced `void` function contracts in the bounded
native frontend: empty bodies, implicit completion, and bare or grouped
unit-literal returns. It reuses null-return lowering under both policies;
parameterless `void Main()` exits zero, and public void exports retain the
existing primitive-parameter ABI. The six-primitive expression, local and
parameter domains are unchanged. Versions 1–55 retain their void restrictions.
These v56 payloads do not admit void expression bodies; general calls remain
excluded. The
[implementation reference](implementation.md) specifies exact source auditing
and completion behavior. This does not widen source-free indexed TRRM.

Projection v57 adds void arrow bodies containing exactly one existing
requirement or non-void scalar discard statement. It preserves hosted
statement/body/function spans, managed lexical wrapping and implicit null
completion at the semicolon under both policies. Earlier versions retain their
arrow restrictions, and braced void functions without `else if` remain v56. It adds no unit
expression domain, calls, assignment/update arrows or ABI change; the
[implementation reference](implementation.md) defines the exact record and
source-audit boundary.

Projection v58 adds existing `else if` chains to braced scalar and void
functions. False arms lower directly to nested conditionals without synthetic
lexical blocks; selected conditions, local scopes, loop targets and checked
failures retain hosted behavior under both policies. Existing structural,
record and expression limits still apply to each nested conditional. Versions
1–57 retain their restrictions, and arrow bodies are not widened. The
[implementation reference](implementation.md) specifies marker association,
source coverage and feature precedence. Source-free indexed TRRM and the
native ABI are unchanged.

Projection v59 adds exact `[NoThrow]` and `[DoesNotReturn]` declaration
contracts over the current top-level scalar/void braced and arrow forms,
including supported primitive-parameter forms. Existing shared folding and
effect verification remain authoritative under both policies; DoesNotReturn
requires void and removes implicit normal completion. Source audits preserve
raw spelling, distinct flags, all source coverage, hosted spans/scopes and
conservative failure proofs. Normalized exports retain the existing
primitive-parameter ABI. Versions 1–58 keep their restrictions, all resource
limits remain, and general attributes/calls are not added. The
[implementation reference](implementation.md) specifies header bits and body
shapes; source-free indexed TRRM is unchanged.

Projection v60 adds explicit lifetime ending for a visible named scalar local.
Kind 24 stores the local slot, root `-1`, and source span through the
semicolon; a `for` clause omits its delimiter as usual. The independent
source-bound reader rejects parameters, unknown or inactive names and
non-local targets. Shared semantic verification still enforces definite
liveness and hosted cleanup/reinitialization rules; mutable locals retain
their existing reassignment authority. Version 60 composes with v59 effects.
It adds no ABI or native profile operation; older projection tags are
validation details, not compatibility requirements.

Projection v61 adds explicit `init T name;` for six scalar types. Kind 25
declares a vacant local with root `-1`, and local flag `0x2` records its
initialization authority. Existing assignment records initialize the local
through shared hosted verification; source audits validate the declared type,
name and lexical visibility, including `for` initializers. Inferred `init var`
and nullable local inference remain unsupported in v61, with no ABI or
source-free TRRM change.

Projection v62 adds bounded `init var name;` scalar locals. Kind 26 marks the
inferred declaration; the producer fixes its scalar type from the first
assignment whose expression already has a known type, and the source-bound
reader verifies the `init var` spelling and resolved local type. Untyped
numeric literals do not establish the type, and unresolved or incompatible
initializations remain errors. Nullable locals and other vacant storage remain
outside this bounded frontend. These provisional projections change no native
ABI, source-free TRRM or semantic-module format.

Projection v63 adds block-local `const T name = literal;` declarations for the
six scalar types. Kind 27 marks the declaration and local flag `0x4` records
constant authority. The source-bound reader accepts only a direct scalar
literal initializer and inlines later reads as its verified constant value;
constant expressions, inferred const locals and `for`-initializer const
declarations remain outside this bounded subset. The projection adds no native
ABI, source-free TRRM or semantic-module format.

Projection v64 adds kind 28 for typed scalar `const` initializers whose
expression tree the bounded constant folder reduces to a `BoundConstant`.
Direct literals remain projection v63 kind 27; folded initializers may use
earlier constant locals. Constant-selected conditional arms and Boolean
short-circuit paths follow only the evaluated branch. Runtime-dependent
evaluated expressions, unsupported forms, inferred const locals and
`for`-initializer const declarations remain outside this bounded subset. The
projection adds no native ABI, source-free TRRM or semantic-module format.

Projection v65 adds kinds 29 and 30 for nested scalar
`try { ... } finally { ... }` regions. The source-bound reader verifies both
block extents and lexical visibility before lowering to `BoundTryFinally`;
`catch` remains unsupported. Shared verification enforces the existing NoThrow
cleanup and control-flow restrictions under both runtime policies. This
remains a bounded frontend projection that depends on the hosted verifier and
emitter, not a standalone compiler or a native ABI/schema change.

Projection v66 adds kind 31 for explicit `unsafe` blocks and kinds 32 and 33
for bounded bare `try`/`catch` recovery inside them. Catch recovery is
available only with the checked-exceptions runtime policy; typed catches,
filters and `finally` on a catching region remain unsupported. The reader
audits both extents and lowers to the hosted `BoundUnsafe`/`BoundTryCatch`
verifier and emitter path. This remains a bounded projection, not a standalone
compiler or a native ABI/schema change.

Projection v67 adds kind 34 for an optional NoThrow `finally` after a supported
bare catch. The reader audits its separate extent, while shared verification
checks failure edges and cleanup under the checked-exceptions policy. Typed
catches and filters remain outside the bounded frontend; no native ABI or
schema changes.

Projection v68 adds kinds 35 and 36 for foldable-true body `static require` and
outer-body `static ensure` proofs. The reader validates the scopes and source
anchors, then erases only proven-true conditions; `static ensure` must be
closed, and unsupported or unestablished proofs reject. This does not add a
native ABI, source-free TRRM or semantic-module format.

Projection v69 adds a three-bit parameter-mode mask for scalar `out`
declarations. The source-bound reader audits each mode against the declaration
tokens and lowers it to the shared verifier as a mutable output parameter.
Hosted Boolean-return and definite-publication rules still apply; general
`out` call sites and general `init ref<T>` migration remain scheduled; the
later multi-function projections add only the existing scalar output modes.

Projection v70 adds direct calls from the one projected function to itself,
including existing mutable local or output-parameter destinations for `out`
arguments. Projection v71 adds `out var` destinations to those self-calls.
The reader audits the function name, argument order and modes, then lowers
the expression to the shared verifier/emitter as a recursive call. Other call
targets and `init ref<T>` arguments remain outside this subset; it does not
complete general `out` call-site migration.

Projection v72 also permits those direct self-calls as expression statements
when their result type is non-void. The source-bound reader verifies the
statement's call root and source spelling before lowering; void self-calls
remain unsupported. General call targets and unresolved `init ref<T>`
call-site metadata remain outside this frontend boundary.

Projection v73 permits void-returning direct self-calls only as expression
statements. Their zero-typed call node must be the root of kind 37; the shared
verifier/emitter continue to define call behavior. Void call values and other
call targets remain unsupported.

Projection v74 separately adds complete snapshots of top-level global
functions with unique normalized names and zero through three positional
by-value scalar parameters. Calls resolve against all declarations, including
forward calls and self/mutual recursion; visible locals and parameters
take precedence over function names. A distinct operation 105 and target table
carry each call's zero-based source-order function identity and callee-specific
argument roots/count/types. The independent reader audits every declaration,
body and call against the complete source before constructing a multi-function
`BoundProgram`. The single-function buffer is a separate projection surface;
its historical tags are not compatibility targets.
The existing 16K-source-unit/64-token snapshot bound is retained; body limits
apply per function. Ten function frames cover even the six-token empty void
declaration under that bound.

V74 admits only scalar-returning callees, including when the caller returns
void. It does not add non-self void calls, namespaces/imports, members,
templates, external bindings, callable values, overload/conversion search,
named/default/params arguments or reference parameters. V74–v76 exclude output
parameters, and v74/v75 exclude `[DoesNotReturn]` support in multi-function
snapshots. Hosted verification and
emission remain authoritative for execution under both runtime policies;
source-free TRRM checked exceptions and bootstrap gates are unchanged.

Program projection v75 keeps the v74 framing and target table, selecting
version 75 only when an operation-105 target returns void. Void calls are
allowed only as direct kind-37 expression-statement roots, including supported
`for` clauses; they are still rejected in value contexts. The reader checks
the callee return type and statement-root ownership before lowering. Version
74 continues to admit scalar-returning callees only. Earlier single-function
tags are implementation/validation details, not compatibility guarantees.

Program projection v76 admits the existing void-only `[DoesNotReturn]`
contract in complete snapshots. Its exact declaration effect is source-audited,
its body is rechecked by the hosted verifier, and direct kind-37 calls preserve
the contract's normal-completion effect. V76 is selected only when a declaration
uses the contract; ordinary void calls still require statement roots. This
does not imply `[NoThrow]`, change v74/v75 payloads or add source-free TRRM
checked-exception support.

Program projection v77 carries existing scalar `out` parameter modes through
complete declarations and direct operation-105 calls. Explicit destinations
and `out var` are accepted only for matching `bool`-returning scalar `out`
parameters; the independent reader audits the target mode and source argument,
then the hosted verifier retains definite-publication authority. V74–v76
continue to reject output parameters in multi-function snapshots.

Program projection v78 adds a separate mask for the existing scalar
`init ref<T>` parameter mode and matching `init name`/`init var name`
destinations. It is limited to the six scalar types and complete multi-function
snapshots; the independent reader audits each mode and the hosted verifier
retains the true-publishes/false-vacant contract. V74–v77 reject init-ref
modes. Single-function v81 and complete-program v82 additionally admit scalar
init-ref declarations with non-Boolean or void results, requiring publication
on every normal return. This does not add general reference arguments,
source-free TRRM support or standalone compiler support.

Single-function projection v79 carries the existing scalar `init ref<T>` and
non-Boolean scalar `out` modes through bounded self-calls. Header word 17
stores the init-ref mask separately from the `out` mask in word 16;
operations 107/108 encode matching existing and declared `init`/`init var`
destinations. Non-Boolean `out` accepts matching explicit or `out var`
destinations in recursive self-calls. Source auditing and hosted
definite-publication verification remain in force; this is not general
reference migration or standalone compiler support.

Complete-program projection v80 adds scalar `out` parameters on
non-Boolean/void-returning functions with matching existing or `out var`
destinations. The hosted verifier requires initialization on every normal
return, and the emitter publishes the output after successful completion
without treating the function's result as a success flag. V85 is the only
current reader boundary; V74–v78 tags themselves are rejected.
Non-scalar outputs and standalone compilation remain unsupported. This does
not widen the separate managed-module export shape for output parameters.

Single-function v79 uses the same non-Boolean/void output contract as v80 for
bounded recursive self-calls. It supports explicit and `out var` destinations
and requires normal-return initialization.
Single-function projection v80 and complete-program projection v81 also admit
operation 109 for `is init` on named scalar locals and parameters. The reader
audits the named storage source, while the shared hosted verifier and emitter
continue to own flow narrowing and runtime-state tracking; this remains a
bounded hosted-backed projection, not standalone compiler support.
Single-function v81 and complete-program v82 also project non-Boolean/void
scalar `init ref<T>` completion through the same hosted definite-publication
verification and result-independent publication path.

Single-function v82 source-audits kind 110 for `unsafe(expr)`, while
single-function v83 also carries the caller-unsafe callable flag and
source-audits call authorization against enclosing explicit unsafe scopes.
These tags are local to the single-function buffer and do not change the
complete-program v82/v83 meanings; verification and emission remain hosted.

Complete-program projection v83 adds source-audited caller-unsafe top-level
callable declarations. Its existing header flags record the declaration
requirement, and only calls enclosed by an explicit `unsafe { ... }` block are
authorized in the projected body. Version 84 adds a source-audited kind-110
wrapper for `unsafe(expr)` and authorizes calls contained by that expression.
The hosted verifier and emitter remain authoritative; unsafe declarations do
not implicitly authorize their own bodies, and persisted caller-safety metadata
and standalone compilation remain unsupported by this bounded projection.

The current hosted parser enforces single same-line loop-label attachment in
hosted/scanner parsing, the native snapshot and successful projection audits.
Newlines inside comments also detach a label. Existing label lookup, transfer
and cleanup semantics, historical semantic/module payloads, projection
versions and native ABI remain unchanged.

The native scalar frontend's provisional projection v48 represents scalar
returns as structured body statements: kind 8, slot 0, a typed result root and
the keyword-through-semicolon span. Header words 38–40 are `-1`; the source
reader consumes every expression root and uses verifier continuation rules to
reject reachable non-void fallthrough. Structured returns are available in
branches and loops. Projections v1–47 retain their existing terminal return
shapes, and v48 does not use hosted-parser fallback or synthesize a terminal
return. It adds no body-local declarations, general calls or native updates.
Its 392-word buffer and limits of 64 tokens, 32 expression nodes, 12
statements, eight locals and six structural levels are unchanged, as is the
16-level expression-recursion limit. It still lowers through the shared
hosted verifier/emitter; it is a compiler component, not a standalone
self-hosted compiler.
Standalone `init T name;` and `init var name;` declare vacant
readonly local slots without a default value. Existing checked initialization
controls publication, owner cleanup and managed-root activation. Semantic v40
persists these declarations when retained; they add no native ABI operation.
Hosted assignment to a definitely vacant `init` local uses the same checked
AOT/JIT publication, cleanup and rooting paths. It fixes inferred storage type
without granting live-value replacement authority; failed evaluation leaves
vacancy. Retained initialization assignments were added at the v46 feature milestone, with local
authority reconstructed from the vacant declaration. Conditional publication
must be resolved before another initialization, including across loop and
exception joins. The native scalar frontend separately supports explicit
typed scalar vacant locals in projection v61 and bounded inferred scalar locals
in projection v62; source-free TRRM is unchanged.
Hosted `name = _;` ends a definitely live named local value without deleting
its declaration or changing its type. A unique value uses the existing
scope-exit drop helper, clearing cleanup eligibility before executing cleanup;
later scope exit or checked unwind does not drop it again. A managed handle
clears only its local root, without destroying other handles or forcing
collection. A bounded buffer view retires its loan without destroying the
backing allocation; an owner with any possibly live dependent view cannot end.
Scalar and Copy values require no fabricated cleanup or default-value store.
Standalone `init` and mutable locals can publish a new lifetime under their
existing initialization authority; ordinary immutable locals cannot. Failed
initialization leaves vacancy and does not reactivate a root or cleanup bit.
Retained end nodes were added at the v47 feature milestone and are audited through
producer/reference/consumer linking and private native graphs. Both PE and
JIT policies use the same lowering under current semantic-module revision 74,
with no native ABI/profile or scanner change. Parameter/receiver aliases, fields, indexers, const aliases and
unresolved/vacant locals reject. General ref/pin types, enums, arbitrary
guards/ARC, view rebinding, source-free TRRM and the compiled scalar frontend
are not adopted by this operation.
The hosted compiler also supports closed scalar `static require`/`static
ensure` proofs, compilation-local generic substitutions and nominal operator
protocols. Expression-bodied functions, methods, static operators,
constructors and explicit by-value getters reuse checked return, statement and
cleanup paths. Their source, proof and specialization boundaries are detailed
in the [implementation reference](implementation.md).

The hosted `init ref<T>` contract publishes vacant storage only on true for a
Boolean result; a non-Boolean or void result requires complete initialization
on every normal return. Semantic v33 persists the distinct parameter/call mode;
exported contracts use `init:`, while older private declarations retain their
legacy `out` spelling. Public scalar, handle-free Copy and eligible unique
outputs reuse checked scratch, cleanup and the existing native ABI. Managed
graph boundaries admit Copy outputs only; generic contracts remain
compilation-local.

Explicit by-value getters use readonly receiver calls. Semantic v34 persists
property identity and accessor effects; Core primitive getters require v38.
Exports remain subject to existing public Copy-layout and image-local managed
state boundaries. Setters, auto-properties and ref-return accessors remain
unsupported.

One-field transparent Copy wrappers over `bool`, `char16`, `int32`, `uint32`,
`int64` and `uint64` retain nominal identity and field authority while storage
and direct Windows x64 argument/result classification match the scalar
carrier. Semantic v35 persists this layout intent and replacements must
preserve it. Nested or owning carriers, custom packing/alignment and broader
field-policy changes remain unsupported; see the
[layout reference](data-layout-and-abi.md).

Terrum has no built-in marshalling. Source or generated tooling defines and
performs conversions and wrappers for imported C APIs; the compiler/runtime
supply only the checked representations, ABI calls and ownership/lifetime
primitives needed to expose those APIs safely.

`OwnedInt32Buffer.TryCreate` accepts capacities from zero through 1,048,576
elements. It allocates an eight-byte count/capacity header plus fixed element
storage on the Windows process heap; zero capacity still has a live owner.
Allocation failure returns false without publishing an output. Negative or
excessive capacity and `Read`/`Set` outside the initialized prefix use the
selected checked-failure policy. `TryAppend` writes the element before
incrementing count and returns false at full capacity without modifying the
prefix. The handle is never a source-exposed pointer or copyable layout.
Whole-owner move, by-value call/return and true-only output transfer the
obligation; lexical and recursive composite cleanup release it exactly once.
`HeapFree` failure is a fatal process invariant error (`0xE054_0005`), not a
recoverable checked failure or a fabricated successful release. The buffer
operations do not store receiver loans or resize backing memory. `Set`,
`TryAppend` and `Clear` also accept writable field paths through fully
initialized unique owners, including forwarded writable `this`. The root
is reserved before argument evaluation, and active views block mutation.
The buffer remains owned by its containing value and is released by the
same recursive cleanup. `Clear()` is NoThrow for an eligible live writable
buffer place: it sets count to zero without freeing or reallocating the
fixed-capacity storage. Lexical mutable slices can also borrow those fields;
this does not implement the general `write`-view migration.
`ReadonlyInt32BufferView` captures a checked pointer/length descriptor in a
16-byte local frame slot, with its root retained only in structured semantic
IR. `Slice(start, length)` rejects negative or out-of-prefix ranges without
overflowing the count subtraction; zero-length slices at the initialized end
are valid but cannot be read. The verifier rejects root moves, replacements,
mutating loans and cleanup while a slice is active, including in branches,
loops and nested cleanup. `Read(index)` checks the slice length using the same
selected checked-failure policy. Readonly range scopes also accept dynamic
absolute and end-relative endpoints, evaluated once in source order and checked
as `0 <= start <= end <= initialized count`; omitted bounds use zero and the
initialized count. End-relative offsets are validated before conversion to
absolute positions.
The view has no call/return ABI and cannot
extend owner lifetime; neither GC rooting nor general aliasing is implied.
`mut MutableInt32BufferView view = owner.SliceMutable(start, length);` reserves
a named mutable buffer or a writable buffer-field path through a fully
initialized unique owner exclusively. Its `Length`, `Read(index)` and checked
`Set(index, value)` access the captured initialized prefix, with index and
value expressions each evaluated once. No owner reads, mutation, move,
replacement or drop, or another view of that owner, can overlap this lexical
scope. For a field view this exclusion covers the whole root, including
scalar reads and other buffer fields. Range arguments may query that root
before activation, but may not invalidate it. Ending a scope or unwinding
ends only the loan, not the allocation's lifetime. A mutable view cannot be
copied, passed, stored or returned.
These two bounded slice intrinsics have their own lexical verifier rules;
they are not a general `Span<T>` implementation or evidence that all
reference/view access uses the accepted `mut`/`write` contract. On the current
Windows x64 profile, `HeapAlloc` returns `MEMORY_ALLOCATION_ALIGNMENT`-aligned
storage (16 bytes); the eight-byte owner header and four-byte element stride
therefore keep every `int32` element naturally aligned. Slices add a
four-byte-scaled start offset and retain that alignment for both readonly and
mutable accesses. This guarantee applies only to compiler-created owned
buffers; no arbitrary external or unaligned view can be constructed.
[`HeapAlloc` alignment](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/nf-heapapi-heapalloc)
is part of the target-specific implementation basis.

The separate `native-18` proof exposes an opaque aliasable `ManagedNode`
handle, not a pointer or a native owner. `ManagedNode.TryCreate(value, out
var node)` returns false without publishing a handle when allocation fails;
`node.Value`, `SetValue(value)`, `SetNext(other)`, `ClearNext()` and
`TryGetNext(out var next)` access its scalar payload and one nullable internal
edge. Semantic version 24 now checks these intrinsic accesses: reads use a
readonly handle, while mutation requires explicit `write` capability and a
writable receiver. This qualification is preserved through optional handles,
fields, parameters, results and inference. Copying a handle shares node
identity. `ManagedHeap.Collect()` is an
explicit, allocation-free safe point: it traces exact initialized managed
locals, argument and output staging, return snapshots and suspended callers,
then sweeps unreachable objects, including cycles. The current private object
header has a work link at offset 0, allocation-list link at 8, immutable
descriptor pointer at 16 and mark word at 24; payload starts at 32.
`ManagedNode` stores its edge at 32 and scalar payload at 40. A class
descriptor lists only its managed-reference offsets. Marking uses an intrusive
worklist without recursion or a mark-stack allocation; it does not run implicitly on
allocation. Frame roots retire when their lexical block ends, and the
executable/JIT callable entry invokes the same generated collector after
`Main` returns and all source frames have unlinked. This drains surviving
nodes before the JIT unregisters and releases its executable image; the
JIT does not delegate reclamation to its worker process exit or CLR GC.
The maximum live allocation count is 4,096; a full heap makes `TryCreate`
return false rather than fabricate a result. `HeapFree` failure remains fatal.
Under checked exceptions, lexical root-retirement funclets run after nested
user cleanup. Staged arguments and unpublished outputs retire on failure;
an outer function scope unlinks its root frame only when unwind escapes it.
Local catches retain the surviving frame, and pending returns stay rooted
through collecting cleanup. These use the same verified root metadata,
collector and Windows unwind machinery as normal execution.
This is a single-threaded, image-private AOT/JIT proof with no
inheritance, managed user-struct
fields, public managed-handle ABI, finalizers,
interior pointers, pinning or generational/concurrent collection. Native DLL
emission supports scalar by-value parameters and scalar or `void` results,
with no `out` parameters; the v31 feature milestone adds the bounded
handle-free `Copy` value contracts described below. Managed state is private,
and calls must be serialized. An export does **not** collect merely because it
returns, so a caller must explicitly invoke the image's collection proof to
drain abandoned nodes before unloading the DLL. There is no automatic unload
teardown.
The independent `scripts/check-managed-node-abi.cs` probe instruments the
image's `HeapAlloc`/`HeapFree` imports. It checks cycle reclamation, retired
frame roots, pointer-shaped scalar nonroots, allocation failure and fatal
release failures through a separately compiled C caller under both policies.
Its checked-unwind fixture also checks lexical and staged-root retirement,
return snapshots, and a native C handler that validates the private versioned
exception record before collecting after both Terrum frames have departed.
That fixture is evidence for frame cleanup, not a public exception ABI.

Semantic module version 18 also permits a leaf producer to use private
`ManagedNode` storage behind scalar, by-value exports. Its exact roots and
collector remain inside the producer DLL. A compatible replacement can add this
private runtime without changing persisted consumer or intermediate images,
under either AOT or root-only JIT. The leaf must explicitly collect unreachable
objects; unloading a DLL does not run managed-heap teardown. Under the v18
contract, managed signatures, cross-image roots and source-class metadata
remain unsupported
([replacement proof](native-graph.md)).

`native-19` adds up to 256 source-defined classes with explicit constructors and
at most 16 scalar, compact Copy or non-null managed-reference fields in a payload of at most
128 bytes. Constructor arguments are limited to 63 by-value scalars, managed
handles or handle-free Copy structs of at most 16 bytes; unique owners and
borrowed views are not accepted by the factory.
The generated receiver/output consumes the remaining position in the ordinary
64-argument call bound. Stack arguments use the same Windows x64 slots and
aggregate alignment as ordinary calls, with outgoing space reserved even in the
compiler-generated factory. The existing page-safe frame limit still applies.
Class values are eight-byte aliasable handles, not inline copies.
`C.TryCreate(arguments, out var value)` is a compiler-owned allocation adapter:
it returns false only for allocation failure or the shared 4,096-object cap.
The factory preserves private constructor access; a public wrapper within the
class may expose construction without exposing that constructor.
It publishes the handle after the constructor initializes every field.
The provisional object and constructor arguments remain rooted during
collection; zeroed but uninitialized reference slots are collector-private,
never readable source defaults. Checked constructor failure frees the
unpublished allocation before propagating; fatal release failure still terminates.
Fields are readonly after construction unless declared `mut`. Through semantic
version 23, legacy class artifacts allowed bare handles to write declared
mutable fields and did not encode class nominal mutability. Semantic version 24
supports `mut class`, writable `mut` class receivers, readonly bare handles,
and explicit `write` authority. Writes still require both a mutable class
contract and an accessible mutable member. A writable child handle in a field
retains its capability when read through a readonly parent; there is no
deep-read propagation. Constructors initialize an unpublished receiver and
publish only after all fields are initialized. Writable storage remains
invariant, output storage requires the exact qualified type, and
value-level `write`-to-readonly attenuation is permitted. Private/internal
access and partial-construction checks still apply through nested handles.
An assignment selects its target referent before evaluating its value and keeps
that referent rooted across calls, collection and exceptional cleanup.
`new C(...)`, inheritance, unique/inline classes,
static managed roots and managed class values in inline structs remain
unsupported. Required references do not permit bootstrapping a cycle by leaking
partial objects. Optional fields in `native-20` provide an explicit construction
path for source-defined cycles.
At semantic versions 19 and 20, class metadata is image-private: versioned
semantic/native leaf modules retain it internally, while public DLL exports
remain scalar-only.
Executable class payloads also admit acyclic, handle-free Copy struct fields
of at most 16 bytes, within the same 128-byte payload bound. Whole-value reads
and writes copy all bytes; nested field access preserves declared mutability
and construction checks. These aggregates contribute no managed descriptor
slots. They cannot contain managed handles, owners or views. Version 20 persists these inline fields
and their private nested layouts; version 19 continues rejecting them rather
than reinterpreting its existing wire contract.
Factories also accept compact Copy arguments through the same direct/indirect
Windows x64 aggregate convention as ordinary calls. Values are snapshotted before
later argument effects and copied into constructor parameter storage; no caller
storage loan survives construction. Version 22 retains aggregate constructor
signatures and private layouts, without changing the scalar native export ABI.
Earlier semantic versions continue rejecting aggregate factory arguments.
Version 23 admits constructor signatures with more than three inputs; earlier
versions continue rejecting that arity. These remain private implementation
details behind unchanged scalar exports.
The current semantic-module revision carries the managed class/`ManagedNode`
access contract. It records that authority directly and rejects retired
semantic-module revisions rather than normalizing them. The semantic version 25
milestone adds a separate
`MutReceiver` contract for persisted, non-exported mutating methods on
non-owning compact structs. Methods may be source-public or private, but source
visibility does not expose a public mutating-receiver ABI. A receiver borrows a
named mutable caller place or its writable nested fields, or is forwarded
through an existing writable `this` and its writable fields, and
writes the original storage, including before a checked exception; readonly
methods and by-value arguments retain copy behavior, and the ordinary
readonly/by-value receiver ABI is unchanged. The verifier rejects forged
mutable receiver loans from immutable places, while nested field writes inside
a valid receiver borrow are allowed. Public scalar entry wrappers may call
non-exported mutating methods, including source-public methods; private helper
methods remain usable. Older headers reject the v25 contract. Nested
mutable-field loan expressions require semantic v26; v25 remains sufficient
for whole-local non-owning loans. The native/public ABI is unchanged.
Nested paths such as `outer.Inner.Mutate()` require writable storage and
fields throughout; calls address the original subobject rather than a
temporary copy followed by writeback. Semantic v27 permits ordinary mutating
methods on initialized named mutable unique owners or forwarded writable
`this`; scalar/Copy field mutation preserves the existing ownership and
cleanup responsibility. Older headers reject those methods.
Semantic v28 also admits nested receiver paths through fully initialized
unique roots and children, with writable fields throughout. It preserves
the same root reservation and recursive cleanup responsibility.
Partially initialized roots,
temporary receivers, managed-class-field receiver paths, rebinding `this`,
and public mutating-receiver ABI
remain unsupported. At semantic v28, the native physical handle ABI and
image-private managed state were unchanged, and public exports remained
scalar-only. Semantic v31 now permits managed-backed leaf producers to export
public handle-free compact `Copy` values up to 16 bytes through the existing
by-value parameter/result and `bool`/`out` forms, including scalar outputs.
Those snapshots use the existing native value ABI and do not expose private
managed storage or handles; exported managed handles, unique values, nested
managed-containing values, views, reference modes and mutating-receiver ABI
remain unsupported. The v31 milestone kept managed leaves from importing
another producer. Native physical ABI/profile versions are unchanged. The v32
milestone added private managed consumers through ordinary single-provider semantic
consumers and native single-provider/module-set entry points. Persisted acyclic
native graphs additionally permit managed intermediate and root producers.
These boundaries communicate only through the established scalar and handle-free public
`Copy` by-value/result and `bool`/`out` contracts; managed/unique values,
views, reference modes and mutating-receiver ABI do not cross those boundaries.
Each persisted native image has its own heap and root chain, imported calls
preserve caller roots, and collection remains local to the image. Semantic
modules statically link into one image, remap private class identities, and
use one collection domain. This adds no cross-image shared collection,
managed-handle ABI or graph cycle, and changes neither native physical
ABI/profile nor graph package format. See the
[managed graph replacement example](native-graph.md).
Primitive
`OwnedInt32Buffer` views keep their separate legacy access rules; general
`ref<T>`/view/`own<T>` and general `init ref<T>` call-site implementations
remain unsupported beyond the bounded conditional-init subset.
The [class sample](../samples/native/managed-classes.terrum)
returns 42 through shared aliases and heterogeneous managed edges. The independent
C heap probe checks multiple-edge liveness, pointer-shaped scalar nonroots,
allocation failure, direct/indirect Copy arguments, collection during construction,
and exact provisional-object
release through both Terrum and native catch boundaries. It also checks selected
assignment-target roots across edge replacement, collection and checked failure.
The managed token-chain probe additionally stores complete 12-byte tokens,
checks nested range fields and retains copied results after their source links
are collected, with unchanged exact allocation/release accounting.

`native-20` admits source-class and `ManagedNode` optional handles in locals,
parameters, results, conditional outputs and class fields. They occupy eight
bytes: zero is explicit absence, while a present value is an existing non-null
handle. Neither integer zero nor an uninitialized slot can manufacture a valid
non-null handle. `null` requires an optional target type; lifting a non-null
handle does not allocate. Scalar options and general generic optional
normalization remain outside this profile; bounded generic functions may use
the existing optional source-class/`ManagedNode` handle forms described in the
[generic implementation contract](implementation.md#bounded-generic-functions).

`value is null` tests absence. `value is some item` snapshots the present
non-null handle and publishes it only on the successful condition path,
including short-circuit conditions, loops and `require`. That snapshot has its
own precise root, so mutation of the tested optional cannot invalidate a
successful extraction. Optional fields still require explicit initialization,
and failed `bool`/`out` calls do not publish even an absent value.
Descriptors include optional reference slots; tracing ignores zero without
treating it as uninitialized source storage. The
[cycle sample](../samples/native/managed-optional-cycle.terrum) exercises
source-defined cycles and snapshots across edge clearing and collection.
Version 19 and 20 semantic modules retain optional IR and types only inside a private
source-class leaf. Neither this nor the version 18 private-node module exposes
an optional or managed-reference ABI. An optional-node-only module remains
unsupported.
For a named optional-handle local or parameter, `is var item` and `is var? item`
first test whether its storage is initialized. The former snapshots only a
non-null handle; the latter snapshots the complete optional value, including
initialized null. Both return false for vacant storage without reading its
payload. The `is var` spelling remains an alias for `is some` on other supported
optional-handle expressions. `is var?` on a non-name optional-handle expression
evaluates it once, captures the initialized optional value and succeeds when
evaluation returns. Contextual multiple-output matching remains unimplemented.
`is init` tests the runtime initialization state of named locals and parameters
without reading their payload. Native lowering uses managed-root flags or
unique-owner live flags where available and a frame flag for queried scalar
storage; assignments, successful output publication and lifetime end update
that flag. Fields and arbitrary expression tests are not supported.
See the [current support boundary](implementation.md).
The independent C probe additionally fills a 4,096-object source-class cycle,
checks that capacity refusal makes no extra allocation, then accounts for every
release and repeats full-capacity reuse. AOT/JIT fixtures exercise the same
limit and interleaved class/node descriptors, aliases and scalar nonroots.

The [static-protocol quadratic sample](../samples/native/static-protocol-quadratic.terrum)
uses source-declared `+` and `*` constraints and two nominal Copy wrappers,
returning 46 through AOT and JIT. Closed specialization emits ordinary concrete
calls through this backend; there is no second evaluator or generic runtime.
The [integer-power sample](../samples/native/static-protocol-power.terrum)
returns 42 using a structured generic body, scalar exponent and a separately
specialized constrained square helper. Its repeated-squaring loop skips the final unused square:
that operation could overflow even when the requested result is representable.
An exponent of zero returns the supplied identity without multiplying.
The [binary-protocol sample](../samples/native/static-protocol-binary.terrum)
also returns 42, using explicitly declared subtraction, division, remainder
and bitwise `&`, `|`, `^` witnesses. The same concrete call lowering preserves
ordinary checked arithmetic and declaration-site effect checks; bitwise
spelling alone does not make an arbitrary operator implementation NoThrow.
The hosted compiler supports the six nominal Copy-struct equality/comparison protocols
(`==`, `!=`, `<`, `<=`, `>` and `>=`); see the
[bounded generic contract](implementation.md#bounded-generic-functions).
Templates and witnesses remain compilation-local. Bounded generic functions
can also select existing source-class/`ManagedNode` handles as `Copy` type
arguments, preserving their readonly/write and supported optional forms and
using the established access/rooting rules. This does not create a public
generic or managed-handle ABI, or grant operations beyond the function's
constraints. See the
[bounded generic contract](implementation.md#bounded-generic-functions) for
inference, results, outputs and remaining limits; this does not implement the
proposed generic standard library or replaceable generic witnesses.

For owned buffers, allocation-failure and exactly-once-release behavior can be checked with
the trusted native ABI probe in `scripts/check-owned-buffer-abi.cs`, which
replaces the fixture DLL's `HeapAlloc`/`HeapFree` imports to inject a
deterministic failed allocation and track live blocks; tests do not rely on
exhausting host memory. Owning-field mutation probes check append/full-capacity
refusal, set, clear and reuse with one allocation and one final release,
no live blocks and no invalid frees. A checked-index failure retains earlier
writes and the initialized count before that release. The same accounting
covers mutable field-view updates and checked failures after a view write.
The probe also checks that only the low 32 bits of the
`HeapFree` BOOL result determine success, and that failed release terminates
even while a checked catch is available.

The [owned source-line index](../self-host/SourceLineIndex.terrum) builds a
line-start table with exactly one word per line start and publishes a unique immutable index.
Its position queries use binary search over initialized words, without a
retained source pointer or a general span/allocator API.

## Compile and run

```powershell
.\build.cmd -restore
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll analyze samples\native\control-flow.terrum --json
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build samples\native\control-flow.terrum -o artifacts\control-flow.exe --runtime fail-fast --json
.\artifacts\control-flow.exe
$LASTEXITCODE # 45
```

The output directory must already exist. `build` requires an explicit
`--runtime fail-fast` or `--runtime checked-exceptions`; it never runs the output. Syntax, binding and native
resource-limit failures produce no new artifact. The complete image is written
to a unique sibling file, flushed and renamed over the destination only after
success, so a failed compilation does not truncate an existing executable.
The input source cannot also be the output path. I/O failures exit 2.

`check`, `tokens` and `tree` keep their syntax-only meaning. `analyze` performs
profile-specific binding without emission. Compile diagnostics exit 1; success
exits 0. JSON schema 1 uses UTF-16 ranges and retains the source content ID.
Successful builds add an `artifact` containing its path, target, explicit runtime
policy, byte length and SHA-256. A successful syntax check alone does not
authorize emission.

`analyze`, `build`, `build-library` and `jit` accept repeatable `--source <path>` options.
All files participate in one declaration/binding pass, so file order does not
restrict forward references. At most 256 distinct canonical paths are accepted;
only the first input may be stdin. A build output cannot replace any input.
Single-source JSON remains schema 1. Source sets use schema 2 with input-ordered
`sources` (path, content ID and UTF-16 length) and a `sourceIndex` on every
diagnostic; a successful build adds the same `artifact` object.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build samples\native\modules\wide-entry.terrum --source self-host\WideIntegerFacts.terrum --source self-host\WideIntegerTests.terrum -o artifacts\wide.exe --runtime fail-fast
```

This compiles source together; it does not replace the persisted, independently
compiled consumer/producer contract exercised by [modules](modules.md).

Native libraries use `build-library`, explicit public export names and a `.dll`
output. They require no `Main` and have no generated initialization entry point.
The same source-set, selected-runtime and output-preservation rules apply. Library
artifact JSON additionally identifies `kind: "dynamic-library"`. Invalid native
exports produce `TRM4002`; source visibility alone never exports a declaration.

```powershell
dotnet artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll build-library self-host\SourceRange.terrum --export Terrum.CodeAnalysis.SourceRange.TryCreate --export Terrum.CodeAnalysis.SourceRange.GetEnd -o artifacts\ranges.dll --runtime fail-fast
```

### Contract-checked native packages

`compile-native` packages the native image with its versioned semantic contract;
it does not defer machine-code generation to launch. Producers declare an
identity and explicit exports. Consumers bind against a producer package and
persist their complete EXE. Both roles accept `--source` and require the same
explicit `--runtime` selection. Sources remain bounded to the module profile's 4 MiB
combined input; package reads are bounded to 16 MiB.

```powershell
$cli = "artifacts\bin\sources\terrumc\Debug\net11.0\terrumc.dll"
dotnet $cli compile-native producer self-host\WideIntegerFacts.terrum CompilerValues --export Terrum.CodeAnalysis.Numerics64.TryAdd --export Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal -o artifacts\wide-provider.trmn --runtime fail-fast
dotnet $cli compile-native consumer self-host\WideIntegerTests.terrum artifacts\wide-provider.trmn --source samples\native\modules\wide-entry.terrum -o artifacts\wide-consumer.trmn --runtime fail-fast
dotnet $cli validate-native artifacts\wide-consumer.trmn artifacts\wide-provider.trmn --json
dotnet $cli run-native artifacts\wide-consumer.trmn artifacts\wide-provider.trmn --runtime fail-fast
```

`validate-native` checks the selected pair without executing it. `run-native`
validates first, stages the persisted images together, and supervises the
native process with a 30-second deadline. It returns the program's raw status,
including checked-failure statuses; `--json` describes compiler/contract errors, not
a program-success envelope. Timeout reports `TRM5004`. A compatible replacement
changes the provider package, not the consumer image. Package checksums detect
corruption; these are trusted native-code artifacts, not a sandbox or an
authenticated distribution format. See [modules](modules.md) for the precise
identity, compatibility, loader and runtime boundaries.

[Native module sets](module-sets.md) extend this process-start proof to two to
sixteen independently produced leaf DLLs. The persisted `.tnset` consumer keeps
its original PE bytes while either selected provider is compatibly replaced;
validation checks every dependency and the exact PE import ownership. This is
not transitive dependency resolution or multi-provider JIT loading. The separate
[native graph commands](native-graph.md) validate explicit acyclic dependencies
between DLLs while retaining the original consumer and intermediate images.

## Checked no-throw declarations

`[NoThrow]` on a function or constructor declares a bound on language-level
throwing effects. Binding preserves it in `BoundFunction.IsNoThrow`; mandatory
verification rejects a violating implementation, including publicly constructed
IR. Calls depend on the callee's declaration, not its current body. Module
contracts preserve that guarantee: adding it is compatible, removing it is not.
The attribute neither disables runtime checks nor treats fail-fast termination
as a proof of no throwing effects.

The current conservative proof accepts initialized value/field reads and writes,
constants, comparisons, boolean and integer bitwise operations, unary plus and
complement, and calls to other NoThrow declarations. Numeric casts are total
for identity conversions, `char16` to the supported integers, `int32` to `int64`,
and `uint32` to `int64`/`uint64`. Negation of a constant operand is accepted
when its result is representable, including ordinary negative literals.
Other conversions, checked arithmetic, general negation and host-buffer
accesses without a statically constant, proven in-range index remain
potentially throwing in the verified IR.

Source binding first folds already-typed constant `+`, `-`, `*`, `/`, `%`,
`&`, `|` and `^`, supported unary plus/negation/complement, and integer/code-unit
conversions into existing constants when every intermediate result is
representable. Thus `[NoThrow] int32 Answer() { return (1 + 2) * 14; }`
is accepted without extending the verifier's arithmetic proof. Overflow,
division by zero and out-of-range casts remain runtime operations; a later
cancellation cannot erase an earlier failure. Calls and storage reads stay
opaque, including a call multiplied by zero. Folding does not provide missing
literal context, change operator binding, or infer a callee's effect contract.
It runs in every build and does not change semantic formats or native profiles.

Matching constant integer/code-unit comparisons and boolean equality also
fold into boolean constants, as do built-in boolean `!`, `&&`, `||`, `&`, `|`
and `^` when all their operands are constants. For example, `[NoThrow] void Check() {
require int32(1) == int32(1); }` satisfies the existing requirement proof.
False comparisons retain requirement failure, and unknown or failing operands
remain in the IR, even behind a constant short-circuit operand. Folding does
not remove the ordinary conservative checks on unreachable source or authorize
access to uninitialized outputs.

The proof uses the exact immutable host-buffer grant
and its element type/read-write capability; it does not authorize unchecked
pointers or treat fail-fast as NoThrow. A `require` needs a
boolean condition whose false outcome is statically impossible. There is no
numeric range or user-invariant inference; unreachable operations are still
checked conservatively. NoThrow says nothing about termination or fatal
operating-system faults.

The [no-throw sample](../samples/native/no-throw.terrum) returns 42 through
checked constructors, methods, widening conversion and conditional output
publication, using the same AOT/JIT encoder. `CharacterFacts` publishes
NoThrow on its two comparison-only classification helpers.

## Compact unique owners

`unique struct` supports primitive, nested Copy and owning fields in 1..16-byte
aligned layouts. Copy structs cannot contain owning fields. `move name` transfers
a live whole owner and leaves it
vacant; a read or second move before reinitialization is rejected at branch
and loop joins. Plain reads/copies of unique values, partial field moves,
and unbound owning temporaries used as receivers diagnose. Standalone
`_ = expression;` transfers an owning result into an inaccessible local and
cleans it at statement exit through the existing structured drop; it does not
add a native ABI or semantic wire node. Owning fields are initialized once in sequential constructor
flow, including straight-line `unsafe` wrappers. Branches, loops and nested
exception regions cannot acquire owning fields in this profile;
reinitialization and owning-field replacement are unsupported. Once the
whole root is initialized, a writable path can borrow a unique child for
an ordinary mutating method, without transferring or separately dropping
that child. Mutation during partial construction remains unsupported.
Readonly instance methods can synchronously borrow an initialized child field,
even while other fields of its parent are under construction, without copying
the child. A unique-returning constructor/factory result
can initialize a local or be returned; a return of a named owner needs
`move`. Instance method calls borrow a named unique receiver synchronously
by pointer. Ordinary methods default to readonly; a `mut` method requires
an initialized named mutable owner, a writable field path from that root,
or forwarded writable `this` and may
update mutable scalar/Copy fields. It cannot replace owning fields or move
`this`. Receiver reservation precedes argument evaluation and prevents
invalidation of the original owner. The call does not transfer ownership
or schedule an additional drop; checked failures preserve earlier field
updates, and the owner is still cleaned exactly once. Captured/stored loans
remain unsupported. By-value parameters consume an explicit named
move or fresh result; the callee drops an unreturned owner on normal or
checked exceptional exit. Earlier argument values remain caller-owned
until all later arguments finish evaluating; an argument failure drops
completed staged owners inside-out without starting the callee.

Unique `bool`/`out` parameters use private scratch. A completed output is
published only on true; false and checked exceptional exits drop it without
making the caller destination readable. An existing output owner's cleanup
occurs at invocation, after all argument evaluations. Successful `out var`
owners gain a lexical drop region even though they were not introduced by
an ordinary local initializer. Output fields are assigned as a complete
owner; piecewise output construction remains unsupported. Neither these
transfers nor the cleanup trace prove an allocator or real OS resource.

`[NoThrow] [Cleanup]` on one public parameterless readonly `void` instance
method declares that unique type's cleanup contract. The method name is
unimportant; ordinary Copy structs cannot declare cleanup. It cannot be
called directly. The owner enters a structured cleanup region only after
its initializer completes. A frame-local live bit conditions its cleanup
on normal fallthrough, return and checked-exception unwind, inside-out.
For a complete composite owner, its own cleanup runs while its fields are
readable, followed by owning children in reverse field-declaration order.
When construction fails, only completed owning fields are destroyed, in the
reverse order they were actually acquired; the incomplete parent's cleanup
never runs. Fail-fast termination skips this exceptional cleanup.
Moving clears that bit; assignment builds a replacement in separate frame
storage before dropping the old owner. Pending return values and replacement
storage never overlap cleanup-call spills. Fail-fast termination still does
not unwind. The separate `OwnedInt32Buffer` intrinsic allocates and frees
one fixed-capacity process-heap block; arbitrary owning graphs, general
OS-resource handles, stored loans and GC are not implied by this proof.
The [sample](../samples/native/unique-owner.terrum)
updates an owner through a mutating method, then exercises checked recovery
after a failed replacement while preserving the updated, still-live owner.

## Checked normal-continuation contracts

`[DoesNotReturn]` is a separate declaration guarantee on a `void` function,
including a void instance/static method. It does not introduce a public bottom
type or apply to a value-returning constructor or an asynchronously completed
operation. The verifier rejects reachable returns and possible fallthrough;
constant-true loops, statically false requirements and calls to other declared
nonreturning functions can prove the bound. Unreachable statements still receive
ordinary type/initialization checks, but an unreachable return does not violate
the guarantee. Caller analysis uses the declaration, never the callee's current
body.

Such a call removes only its normal continuation. For example, a bool/out
failure branch ending in a nonreturning call leaves only the successful,
initialized output path at the join. It neither manufactures a false-path
value nor removes potential failure effects. `[NoThrow]` and `[DoesNotReturn]`
may be combined for an infallible nonterminating body, while `require false`
satisfies only the no-normal-return guarantee. Both promises persist in module
contracts and may independently strengthen, but not weaken, during replacement.

The [no-return sample](../samples/native/no-return.terrum) returns 42 on success.
Changing its entry to `Select(false)` reaches the nonreturning failure helper
and terminates with the explicit requirement-failure status. AOT and JIT retain
the same native call ABI; the no-return proof does not require a fabricated
return value. General catch objects and GC root tracking remain unimplemented.

Structured [`try`/`finally`](../samples/native/try-finally.terrum) executes
cleanup on ordinary fallthrough, every normal return and exiting loop transfers, from inner region
to outer. Body locals are not in scope in the cleanup block; shared outer
locals remain accessible. Cleanup independently obeys NoThrow even within a
MayThrow function: calls need declared NoThrow contracts, and potentially
failing arithmetic, conversions and host-buffer access diagnose. A return
from cleanup diagnoses. This bounded profile also rejects potentially executing
cleanup loops other than `while (true) { break; }`, and reachable calls declared
`DoesNotReturn`; it does not infer
termination from the bodies or contracts of other NoThrow callees.
The backend snapshots scalar and compact aggregate pending results before
cleanup, then commits bool/out outputs only when the earlier return predicate
was true and the post-cleanup definite-assignment proof permits publication.
Each cleanup region has one out-of-line normal-exit funclet. Its caller passes
the owning source frame base in RDX; it preserves RBP, addresses shared slots
through that parent frame, and reserves its own aligned ABI call area. Return
and fallthrough edges call the funclet explicitly. Under `checked-exceptions`,
Windows also invokes it during exceptional unwind, inside-out; the explicit
fail-fast policy still terminates without unwinding it.
No owning values or destructor semantics are provided.

The independent [Windows SEH ABI probe](native-seh.md) checks filter search,
normal/exceptional finally order, frame-local access and dynamic unwind-table
lifetime against a C reference; the compiler now separately emits and tests the
matching bounded native contract.

## Bounded checked-failure recovery

`--runtime checked-exceptions` selects `native-11` for Copy-only programs,
`native-12` for compact unique locals/returns, `native-13` where
function declarations transfer ownership through parameters, `native-14`
for retained composite unique layouts, `native-15` for owned native buffers,
`native-16` for their lexical readonly views, and `native-17` for
exclusive writable views or `Clear`, without changing
older fail-fast artifacts. `analyze` also accepts this option to check recovery without emitting
an artifact; omission keeps its existing fail-fast profile. The reusable
compilation API accepts the policy with single or multiple original snapshots,
including explicit host-buffer grants. Only compiler-generated arithmetic/conversion, division-zero,
requirement and host-buffer-bounds failures use the private noncontinuable
Windows exception code `0xE054_0092`, with three checked parameters: marker
`0x5445_5252_554D_0001`, version 1 and reason 1 through 4. Generated filters
check code, flag, parameter count and every payload field. `unsafe { try {
... } catch { ... } }` recovers these failures without an exception object;
`try`/`finally` needs no unsafe context. An unhandled owned failure exits the
process with `0xE054_0001` through `0xE054_0004` in AOT and a bounded CLI JIT
worker. Foreign faults and mismatched private records continue native search,
not source recovery.

The verifier intersects initialized state at each checked failure site,
including left-to-right argument evaluation, invalidated out destinations and
nested exceptional cleanup. Catch and finally bodies cannot assume the try body
finished. Cleanup remains independently NoThrow; body-derived guarantees are
not published as module contracts. Windows `__C_specific_handler` dispatches
version-1 scope records shared by PE and registered JIT unwind images. Recovery
filters are leaf machine-code thunks; exceptional cleanup calls the same
source-frame-aware funclets used for normal exits. A native entry filter contains
unhandled owned failures before they reach the managed JIT caller.

This is not a general exception-class, throw-object or low-memory ABI. There is
no `throw`, typed/filter catch, GC rooting, async
or arbitrary native-fault recovery. Foreign callbacks must not unwind across
an unsupported ABI. The selected policy is part of the current persisted
module/native package contract and must match across linked artifacts; retired
native image layouts are rejected rather than loaded for compatibility.

## Executable language

| Area | Supported |
|---|---|
| Program | File-scoped namespaces, using directives, top-level/static/instance functions; parameterless `int32 Main()` or `void Main()`; multi-source executable/library compilation |
| Types | `int32`, `uint32`, `int64`, `uint64`, `char16`, `bool`, `void` results; compact reference-free structs |
| Literals/constants | Context-typed decimal, hexadecimal and binary integers without a fallback type; digit separators; based literals construct target-width bit patterns; primitive `MinValue`/`MaxValue`; booleans; unit `()` |
| Variables | Initialized typed/inferred locals; immutable bindings by default, explicit `mut` assignments; lexical scopes |
| Expressions | Direct calls, forward calls/recursion, constructors, field reads, checked numeric type-call conversions to `int32`, `uint32`, `int64`, `uint64` and `char16`; unary `+`, signed checked `-`, `~`, `!`; nominal static `++`/`--` updates on the eligible writable `Copy`-struct place envelope, with exact operand/result type equality |
| Operators | Checked signed/unsigned `+ - * / %`, bitwise operations, signed/unsigned comparisons, same-type scalar equality, boolean operations; code-unit comparison but no `char16` arithmetic |
| Flow | Blocks, `if`/`else`, `while`, scoped `for`, return, boolean `require`; left-to-right argument/operand evaluation and short-circuit `&&`/`||` |
| Outputs | Bool/out true-only publication and definite assignment through branches, `!`, `&&`, `||` and `require` |
| Storage | Explicitly granted, initialized fixed host buffers with checked indexing and read/write access; capabilities are not first-class source values |

Fixed host buffers still use the explicit 32-bit-word interchange described in
the [scanner contract](scanner.md). Adding 64-bit scalar values does not enlarge
that storage contract: `int64`/`uint64` buffer descriptors are rejected.

Literal context comes from declarations, parameters, return types or a known
operand, not an implicit numeric fallback. `var x = 1` is rejected;
`var x = 1i32` diagnoses as `TRM0006`, while `int32 x = 1` and
`uint64 x = 0xFFFF_FFFF_FFFF_FFFF` are accepted. A decimal signed minimum
literal can be constructed directly, without first overflowing its positive
magnitude; based literals instead select bit patterns at the expected target
width. Locals cannot be
read before declaration/initialization, immutable bindings cannot be assigned,
conditions must be boolean and non-void functions must return on every normally
completing path. Constant-true loops and declared nonreturning calls can remove
that continuation; other loop conditions remain conservative. Function
overloading, active-scope local shadowing, named output arguments, `ref`/`mut ref`
passing and mutating instance methods are not supported.

For loops accept an optional initializer, boolean condition and iterator,
with a braced body. Initialization runs once; each successful condition is
followed by the body and then the iterator. Returning or breaking from the body
skips the iterator; continuing reaches it before the next condition.
An omitted condition is true. Header locals are scoped to the loop;
the iterator cannot access body locals. Supported integer updates and compound
assignments may occupy the iterator; comma-separated clauses are not implemented.
Bare `break` and `continue` target the nearest loop; named forms select an
enclosing labeled loop. Both clean exited scopes inside-out,
including `finally`, unique owners and exact managed roots. A `for` initializer's
owner survives continue and retires on loop exit. Transfers out of `finally`
are rejected; loops entirely within a `finally` may use their own transfers.
Checked cleanup failures take the exception path instead of completing the
transfer; fail-fast termination still does not unwind.
[The loop sample](../samples/native/for-loops.terrum) returns 285.
The [native loop-transfer fixture](../tests/native/managed-loop-transfer-abi.terrum)
combines a retained initializer buffer, nested transfers/finally and managed
cycles. Its C caller checks exact allocation/release counts under both policies.

The hosted parser and binder also accept `do { ... } while (condition);`: the body executes
before the first test, and `continue` reaches that test after exited-scope
cleanup. Labels may identify enclosing `while`, `for` and `do` loops.
The bounded implementation rejects duplicate enclosing labels and permits
reuse in disjoint scopes; the general source collision policy remains
unspecified. The v39 feature milestone added post-tested loops and non-nearest
transfer depths; current semantic-module revision 74 serializes these nodes
when present. Native calling conventions, cleanup funclets and public ABI do
not change.

Other integer widths, floating point, character/text literals, classes,
optional/generic/family binding, general borrowed references, ownership/cleanup,
exceptions, GC, async, arbitrary interop and the standard library are outside the
profile. Their syntax may be inspectable, but a build
must diagnose unsupported semantics. No source library is silently assumed.
The 64-bit types are fixed-width integers, not aliases for `nint`, `nuint` or
native pointers. Entry-point process statuses remain `int32`; a wider return
value requires an explicit checked conversion before returning from `Main`.

## Values, places and conditional publication

Struct fields contain supported primitive values or other acyclic compact structs, with explicit constructors
and naturally aligned declaration-order layouts of 1 through 16 bytes.
Constructors must initialize every field
before reading or returning the entire receiver; individual initialized fields
can be read during construction. Copying a value does not create a storage
alias. Fields are read-only after construction unless declared `mut`.
Field writes require an accessible `mut` field and mutable receiver, except
that a constructor can write its own receiver's fields regardless of `mut`.
That exception does not grant write authority over nested fields declared by
another type. Instance methods receive copies;
mutating instance methods are rejected rather than silently losing writes.
Layouts resolve across source files regardless of declaration order; recursive
value containment is rejected. Nested field reads and whole-field replacement
preserve value copies, visibility and constructor initialization. A nested
aggregate's alignment is its maximum field alignment, not its byte size.
Sizes other than 1, 2, 4 and 8 require the Windows indirect aggregate ABI;
the compiler owns argument copies and result storage, not source-visible pointers.
Nested field paths can be assigned through a mutable local or constructor
receiver. Every containing value must already be initialized, including after
evaluating the right-hand side; a call cannot vacate an enclosing output and
leave a partially valid nested write. Each field's visibility and mutation
authority is checked, including all containing fields. A writable binding does
not upgrade a read-only field, and an `out` value does not grant constructor
authority after publication into the callee's scratch storage.
Only whole top-level fields establish constructor initialization. Writes
through temporary values or immutable receivers are rejected.
Whole-value replacement through a mutable local remains valid even when its
fields are read-only. `SourceRange` and `Token` retain read-only fields; updates
construct and replace valid values rather than bypass their constructors.
This is the current bounded `ValueLayout` profile. Struct and field attributes
diagnose `TRM3001`, so no transparent/native layout attribute is currently
honored; the 1–16-byte aggregate rules do not provide resilient module layouts.
Field positions and padding are computed from declaration order, size and
alignment; arbitrary explicit field offsets are not supported.
Aggregates over 16 bytes, reference-containing aggregates and implicit
construction are not implemented.

Top-level functions and types default to `internal`; struct members,
constructors and fields default to `private`. Module exports require explicit
public declarations and supported public value layouts. Top-level `private`,
field initializers and field-level `mut`/`static` are rejected. Field writes
are governed by the owning value's access, not a separate field-mutability flag.

The implemented checked `T(expression)` conversions preserve a numeric value
or fail for range overflow. Raw `char16` values include surrogate code units:
a successful conversion is not Unicode scalar validation. The profile has no
implicit conversion between typed numeric values and no implicit text encoding.

The signed/unsigned 32- and 64-bit types retain their full range through calls,
fields, conditional outputs and persisted modules. Widening a signed negative
value preserves its sign; narrowing or crossing signedness checks the numeric
range rather than reinterpreting bits. The same signed-minimum division and
remainder rules apply at both signed widths. The
[wide integer candidate](../self-host/WideIntegerFacts.terrum) uses these rules
for non-overflowing signed-addition checks and bounded decimal accumulation
through `uint64.MaxValue`, with true-only output publication.

The shared AOT/JIT encoder lowers `uint32` and `uint64` division by a
nonzero constant power of two to a logical shift, and remainder to a mask.
Dividing by one retains the numerator; remainder by one produces zero after
evaluating the numerator. Each numerator still evaluates exactly once, with
its original effects and failures. Signed `int32` and `int64` division/remainder
by a positive constant power of two uses a negative-numerator bias before an
arithmetic quotient shift, then subtracts quotient times divisor for the
remainder. This retains truncation toward zero and negative remainder signs,
including signed minima. Signed divisor one keeps the numerator for division
and yields zero for remainder after evaluating it. Zero, negative and other
non-power-of-two divisors retain their checked general paths. This instruction
selection changes neither verified IR nor source `NoThrow` acceptance, module
formats or ABI.

A bool-returning declaration with `out` parameters promises initialized outputs
on true. Callees construct outputs in private scratch storage and publish only
on successful return; a false return does not manufacture a readable default.
An output target is unavailable while the call is pending and is vacant on
false, even if its backing storage still contains a previous value's bits.
Calls stage output storage separately from ordinary arguments. Aliased output
targets and reads of not-yet-initialized outputs are rejected. Conditional
assignment facts follow evaluated short-circuit paths, and `require` carries
the true-path facts forward. Supported unique owning outputs clean partial
values on false or checked exceptional paths; neither path publishes a
partial output.
This bool/`out` behavior remains supported. The bounded `bool`/`init ref<T>`
source mode additionally requires a vacant caller destination and an exact
`init` argument; v33 persists that distinction, while both modes share the
same verified scratch/publication and native output ABI. Exported init
contracts remain distinct from out contracts. General references and
predicate-based postconditions remain scheduled.

[`self-host/SourceRange`](../self-host/SourceRange.terrum),
[`CharacterFacts`](../self-host/CharacterFacts.terrum),
[`RangeTests`](../self-host/RangeTests.terrum) and
[`Token`](../self-host/Token.terrum) execute through this profile.
The [scanner component](scanner.md) additionally uses fixed checked buffers;
this does not implement a general span ABI or a complete lifetime checker.

## Runtime policy

The eventual ordinary runtime needs a general exception contract. This
compiler instead requires explicit `fail-fast` or bounded `checked-exceptions`
selection. Both keep arithmetic checked:

Under fail-fast, `finally` handles normal exits only and a failure terminates
without executing pending cleanup. Under checked exceptions, owned checked
failures dispatch through unsafe catch and exceptional finally scopes.

| Failure | Process exit status |
|---|---|
| Checked signed/unsigned arithmetic or numeric conversion overflow | `0xE054_0001` |
| Integer division or remainder by zero | `0xE054_0002` |
| A false boolean `require` predicate | `0xE054_0003` |
| Negative or out-of-range host-buffer index | `0xE054_0004` |

`require predicate;` evaluates its boolean once and continues only on true.
This is executable semantic code, not a debug assertion, unchecked assumption
or inferred public precondition. The fail-fast profile terminates; the bounded
checked policy raises a privately identified native exception.
A predicate's own arithmetic failure retains that failure
status. `samples/native/requirements.terrum` demonstrates a parameter-dependent
requirement and returns 10.

Division truncates toward zero; signed minimum remainder -1 is zero, not an
overflow. Neither compiler-host overflow mode nor x64 `idiv` traps define these
semantics. Fail-fast terminates through imported `ExitProcess`. Checked failure raises an
owned native record and terminates at the owned entry boundary when unhandled,
rather than continuing with a fabricated value.
Normal `int32 Main` returns its 32-bit value as the process status; `void Main`
returns zero. Consequently a deliberate program return can match a reserved
failure status; exit codes are not a general language exception transport.

The profile does not promise recovery from stack exhaustion, OS termination or
resource exhaustion. It does not implement general exception classes, native
fault recovery or fatal-error text formatting. A future full runtime must preserve
ordinary language semantics rather than silently inherit this explicit profile.

## Semantic and target boundaries

`Compilation.Create` declares names/signatures before binding bodies.
It returns deterministic diagnostics and no `BoundProgram` on errors, including
definite-assignment and construction failures found during source verification.
For multiple source snapshots, `SyntaxTrees` retains input order and
`SourceDiagnostics` pairs each diagnostic with its owning snapshot; the legacy
`SyntaxTree` property refers only to the first snapshot.
Resolved functions, slots and types are immutable public data. Structured
blocks, branches and loops preserve control-flow meaning; no source token is
translated directly to an opcode.

`SemanticVerifier.Verify` checks the public bound representation again before
emission, including operation types, calls, slot initialization/mutability and
returns. This matters because a library client can construct bound objects
without going through the binder. Invalid IR is an argument error, not a
machine-code fallback. Limits on semantic depth and native frame/image size
are intentional compilation failures, not stack-overflow handling strategies.
The current ceilings are 1,024 functions, 256 parameter/local slots per function,
64 arguments, 128 levels of IR nesting, 100,000 verification visits, 3,840-byte frames and
8 MiB of machine code. Valid programs exceeding emission limits report
`TRM4001`; these are implementation limits, not language type or ABI guarantees.
Verification visits include conditional-flow analysis, not only unique IR
nodes. Source analysis reports an exhausted verification budget as `TRM3016`.
The fixed-buffer profile additionally limits descriptors to 256 and total
initialized storage to 1,048,576 32-bit words.

The x64 backend uses fixed stack frames, caller shadow space and aligned calls.
The first four scalar arguments use RCX, RDX, R8 and R9; later arguments
occupy stack slots. Live intermediate values and arguments are spilled across
nested calls. Source functions preserve RBP and address locals, spills and
pending returns from the post-allocation RBP frame base; outgoing arguments
remain RSP-relative in the ABI call area. Normal cleanup funclets load the
owning function's frame base from RDX into their saved RBP and use a separate
call area. PE and JIT share unwind records for the saved RBP and stack
allocation. Checked source and entry functions carry Windows scope records,
including filter and cleanup targets; fail-fast records retain the old shape.
The independent generated-DLL probe executes cleanup from C, inspects its
unwind record and unwinds through a foreign fault without consuming it:
`dotnet run --file scripts\check-native-frame.cs`.

Aggregate sizes of 1, 2, 4 and 8 bytes use integer carriers. Other supported
sizes use pointers to caller-owned 16-byte-aligned copies. Indirect results
receive a hidden first argument in RCX, shifting explicit arguments by one
position; the callee returns that storage address in RAX. These addresses are
backend storage, not language pointers, escaping borrows or shared mutable
references. This direct/indirect choice preserves the complete `T`'s source
semantics; it does not itself introduce a loan of the original value's storage.
A source-level borrowed result needs its own declared lifetime, access
and alias contract. The bounded one-field scalar transparent wrapper
inherits its carrier's direct ABI; no other transparent-wrapper ABI is
implemented. Outputs use separate exact-size publication into the caller's
storage only on a true return.

Unique instance receiver loans are an exception to by-value aggregate passing:
they always pass a pointer to the live readonly owner, including 4- and 8-byte
owners. Cleanup instead consumes its receiver through the aggregate value ABI.
Indirect cleanup arguments use a dedicated frame-local aligned copy; child
cleanup reads the original owner storage afterward, even if the parent cleanup
overwrites its by-value input storage.
The receiver-loan ABI composes with a hidden result pointer and stack arguments;
it neither transfers ownership nor makes the owner copyable.

The independent ABI fixture uses a C caller, rather than validating calls only
by having Terrum call itself. From an x64 Visual Studio developer shell at the
repository root:

```powershell
ml64 /nologo /c /Foartifacts\abi-carrier.obj tests\native\abi-carrier.asm
cl /nologo /W4 /WX /O2 /TC tests\native\abi-probe.c /Foartifacts\abi-probe.obj /Feartifacts\abi-probe.exe /link artifacts\abi-carrier.obj /INCREMENTAL:NO
dotnet run --file scripts\check-native-abi.cs
```

It checks flat and nested direct and indirect aggregates passed/returned by value,
including one-field transparent scalar wrappers independently called from C,
hidden results and register/stack arguments, true/false output publication, guard storage
around scalar and aggregate output destinations, and narrow
arguments with deliberately unspecified upper register bits. The provisional
private `[Layout(Sequential)]` fixture also checks independent C `sizeof`,
`__alignof` and `offsetof` for nested fields with interior and tail padding,
followed by by-value and output calls. This is physical classification
evidence for the existing Windows x64 profile, not an exported native layout
commitment. The script
also checks readonly unique receivers of 4, 8 and 16 bytes, preserved caller
storage, and a borrowed receiver combined with an indirect result and five
explicit arguments. These C fixtures provide initialized primitive payloads
at a trusted test boundary; they are not a general source interop facility.
The script
obtains function addresses from encoder ranges; the development executable
does not advertise an exported native API.

The separate checked-policy generated-DLL fixture
(`dotnet run --file scripts\check-owned-call-abi.cs`) exercises unique
direct/indirect by-value parameters and returns from an independent C caller,
including hidden-result RAX storage identity and a sixth owning argument.
It checks owning output stack placement, true-only publication, unchanged
false/partial/caught-failure output bytes, guard storage, repeated calls,
and caught later-argument failure. Composite cases cover 8- and 16-byte owners,
nested child loans, nonzero field offsets, and failed partial construction.
The nonzero field offsets in these cases are computed from normal field order,
size and alignment; they are not user-selected explicit offsets.
The C fixture's cleanup methods are empty;
Host.Trace execution tests, not this ABI probe, establish cleanup order and
exactly-once effects. This remains a bounded test boundary, not a general
native interop or resource-release contract.

`dotnet run --file scripts\check-cleanup-copy-abi.cs` supplies independently
compiled C cleanup callees instead. The parent cleanup deliberately overwrites
its entire indirect by-value argument; child cleanup must still observe the
original payload. Both runtime policies exercise normal destruction, replacement,
an owning output call with a staged owning argument, destruction while a
return value is pending, and recursive cleanup through an outer owner with no
custom cleanup.
The bounded worker checks cleanup order and counts as
well as returned values. This uses the verified IR's explicit native-import
boundary, not a source-level interop extension.

Images prefer base `0x1_4000_0000`, with 4 KiB section alignment and 512-byte
file alignment. `.text` is read/execute, `.idata` read/write and
`.pdata`/`.xdata` read-only. Optional `.rdata` holds immutable buffers and
`.data` holds writable buffers. NX, dynamic base and high-entropy 64-bit VA
are enabled. Code and data references are RIP-relative; import and unwind
metadata use RVAs. There are no image-base-dependent locations to fix up, so
the base relocation directory is empty and relocations are **not** marked
stripped. The [PE format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)
defines relocation entries for addresses needing correction; the
[`RELOCS_STRIPPED` flag](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-image_file_header)
would instead require loading at the preferred base. A separate Windows
loader probe reserves that address, loads two images concurrently, and calls
their RVA-addressed functions. It also observes ordinary executable image
bases through Windows debug events, checking successful and fail-fast exits
after relocation.
This RVA-based executable probe is not a module hot-swap mechanism or
general-purpose loader; separately deployed images use the DLL/package APIs.

PE layout, relative code/import fixups and unwind records are target-specific.
The same encoder's `NativeCode` result is consumed by both PE layout and the
[JIT execution proof](jit.md); code/data fixups and unwind ranges are explicit.
The [module proof](modules.md) persists typed semantic bodies and normalized
contracts, supports source binding against artifact declarations, verifies replacements,
then lowers at process start. Producer libraries need no artificial entry point;
imported declarations require explicit validated DLL/export bindings at the
encoder boundary. `Compilation.CreateLibrary` can bind one or more source
snapshots against a validated `CompilationReference` under the selected runtime
policy. `NativeEmitter.EmitLibraryWithImports` shares the DLL export and
executable import-binding checks; it verifies external declaration shapes and
bindings, not provider contract compatibility. A bounded execution test loads
an EXE through a dependent DLL into a leaf DLL using these raw APIs. Static AOT
linking from these artifacts is separate from replacement of an unchanged PE.
There is no optimizer, debug-symbol format, general source interop, managed
root map, production runtime or hot replacement. The broader TRRM IL design
remains separate from both PE and any optional CIL representation.

Authoritative ABI references:
[Windows x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention),
[unwind data](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64),
and [PE/COFF](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format).
