# Source arithmetic profile selection

**Status: unapproved; explicitly provisional implementation direction.**
The authorized missing-contract workflow permits using this recommendation
provisionally. It does not adopt the candidate API/CLI spellings below, amend
the specification, allocate protocol or Feature identities, or establish
compiler support.

## Decision and scope

Recommend one explicit, immutable **source arithmetic profile per source
compilation**, supplied through public compilation options or a CLI option.
Omission selects **Development**, independently of how the compiler executable
was built. Release must be requested explicitly. A build driver may map its
subject program's Debug/Development configuration to Development and Release
configuration to Release by supplying that option; the compiler does not infer
this mapping from host configuration, optimization, debugging, environment
variables, output kind, target or runtime failure policy.

The missing contract is the selection boundary and default, not arithmetic
semantics. Preserve the accepted
[integer profile rules](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations):
Development/Debug defaults to checked integer add/subtract/multiply/negate and
their compound/increment/decrement forms; Release defaults to wrapping.
Explicit checked/wrapping operations retain their selected policy in either.
Select before body checking, effect/proof analysis and required constant
evaluation, including source constants outside function bodies.

This proposal introduces no Terrum syntax, arithmetic variant declaration,
conversion pair, numeric promotion, library API, target ABI or optimization
profile. Division/remainder, shift validation, conversions, literal validity,
floating behavior, extent checks and explicit Core APIs retain their current
contracts and numeric matrices. In particular, Release does not make a checked
conversion or ordinary signed Abs wrap.

## Evidence and the uncovered choice

| Owner or implementation | Established boundary |
|---|---|
| [Primitive operators](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations), [conversions](../specs/conversions.md#conversion-policies) and [IL source profiles](../specs/il-instruction-set.md#integer-source-profiles-and-explicit-il-semantics) | Define both defaults, explicit-operation precedence and consumer independence, but do not select a public options surface, omission default or CLI spelling. |
| [Staged constant evaluation](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation) | Requires profile resolution before evaluation and explicit policies in target-deferred work. |
| [`Compilation`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs) and [`Binder`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs) | Factories pass `BoundRuntimePolicy`, not a source arithmetic profile. There is currently no `CompilationOptions` type or arithmetic-profile option. |
| [`BoundProgram` operations](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [constant folding](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstantFolding.cs) and [`NativeEmitter`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs) | Ordinary unary/binary/update nodes have checked semantics without a profile selector. Folding uses exact integer calculations and range validation; ordinary native integer arithmetic emits overflow checks. This is checked-only source support, not implemented Debug/Release source selection. |
| [Host build properties](../../Directory.Build.props) | Debug enables C# `CheckForOverflowUnderflow`. This governs the compiler implementation's C# arithmetic, not the Terrum source profile; the inspected source-selection paths do not consult it. |
| [Ordinary CLI compilation](../../sources/terrumc/Program.Compilation.cs), [private module compilation](../../sources/terrumc/Program.Modules.cs) and [TRRM compilation](../../sources/terrumc/Program.TrRmExecution.cs) | Have no source-profile selection. `--runtime` selects checked-failure delivery/admission, separately passed to binding and native execution. |

The [proposal index](README.md) and existing numeric/library proposals do not
provide this selection contract. Their explicit-policy/library alternatives
are not a substitute for choosing the already accepted ordinary defaults.
The [implementation summary](../implementation.md) separately records bounded
scalar coverage and experimental CLI/TRRM support; this proposal does not
upgrade those support claims.

## Alternatives

| Boundary | Assessment |
|---|---|
| Explicit options with a fixed Development default — recommended | Makes both profiles available from the same compiler binary, preserves existing checked source behavior on omission and permits simple callers without sacrificing deterministic semantics. Release intent is visible at the source-compilation boundary. |
| Mandatory profile on every public factory and semantic CLI invocation | Makes every selection visible, but forces migration of callers that want the existing checked default. It adds no reproducibility over a documented fixed default; drivers can still require explicit selection in their own interfaces. |
| Infer from compiler-host Debug/Release, optimization or `--runtime` | Host inference changes subject semantics when distributing another compiler build; optimization and failure delivery are independent axes. This would leave public callers unable to select both profiles reliably and is not an implementation of the proposed boundary. |
| Source declarations or a serialized ambient Module profile | Could support per-source policy regions or consumer-selected reinterpretation, but introduces syntax, composition and/or execution contracts beyond the missing selection. Explicit operation policies already carry all required compiled semantics; a persisted selector is redundant or dangerous if it can override them. |

## Proposed public compilation boundary

All identifiers and host enum ordinals in this section are **unapproved
candidates**, not source, metadata or wire assignments.

In `Terrum.CodeAnalysis.Semantics`, introduce:

| Candidate | Exact proposed shape and meaning |
|---|---|
| `SourceArithmeticProfile` | Public non-flags enum with exactly `Development = 0` and `Release = 1`. Development denotes the accepted Development/Debug behavior; there is no separate `Debug` enum member. |
| `CompilationOptions` | Public `readonly record struct` with init-only `SourceArithmeticProfile SourceArithmeticProfile` and `BoundRuntimePolicy RuntimePolicy` properties. Zero initialization, `default` and parameterless construction all mean Development plus the existing FailFast runtime policy. No host-dependent initializer. |
| Factory options argument | Required `CompilationOptions options`, after the existing source/reference/buffer inputs and before optional `CancellationToken cancellationToken = default`. Do not combine an options argument with a second runtime-policy argument. |

Add an options overload for every existing source-binding input shape:

| Factory | Input prefixes receiving the options overload |
|---|---|
| `Create` | Single `SyntaxTree` or `IEnumerable<SyntaxTree>`; each also with its existing `CompilationReference` or `ImmutableArray<HostBuffer>` form. |
| `CreateLibrary` | Single tree or enumerable; each also with its existing reference form; enumerable with host buffers. |
| `CreateWithNativeImports` | Single tree and enumerable forms. |
| `CreateCoreLibrary` | Existing enumerable form. |

For example, the candidate signatures are
`Create(SyntaxTree tree, CompilationOptions options, CancellationToken cancellationToken = default)`
and
`Create(IEnumerable<SyntaxTree> trees, CompilationReference reference, CompilationOptions options, CancellationToken cancellationToken = default)`.
The table specifies overloads, not new combinations of references, buffers or
native admission. Retain existing overloads: those without runtime policy
delegate with default options; those with runtime policy preserve that value
and use Development. Do not remove existing positional cancellation forms.

Validate options at the public binding boundary, before source enumeration,
body checking or constant evaluation. A profile other than the two enum
values, or an unsupported runtime-policy enum value, throws
`ArgumentOutOfRangeException` naming `options`; do not silently use Development.
This is a host argument error, not a Terrum source diagnostic. Existing source,
reference and cancellation obligations remain.

Snapshot the resolved options once into the compilation context and pass them
to both `CreateCore` paths and all binder construction paths, including the
single-tree host-buffer path. Every source tree in that compilation uses the
same profile. There is no per-file override or mutation after checking starts.
Changing profile requires another source compilation; it is a semantic cache
input, alongside runtime policy and the other actual dependencies. Equal
source-byte/snapshot identities alone do not authorize reusing a bound result.
No new public options getter is required by this proposal.

## Proposed CLI selection

The **unapproved candidate** spelling is:

```text
--arithmetic-profile <development|release>
```

Names and values are exact, case-sensitive ASCII. Omission selects
`development`. There are no `debug`, numeric, Boolean, `checked`, `wrapping`,
abbreviated or `--arithmetic-profile=value` aliases. An external Debug build
driver supplies `development`; it does not need a third semantic value.
This option does not select optimization or failure delivery.

Accept it at most once, in the command's existing option region after its
required positional operands, independently of the order of other option/value
pairs. It governs all subject source inputs, including `--source` additions and
stdin. Keep the experimental trailing `--json` rule unchanged. Missing values,
unknown values, repeated occurrences (even identical), malformed spelling or
use on an ineligible command are usage errors, before compilation, execution
or artifact replacement. Preserve positional operands that happen to look
like options; do not find this selection by scanning arbitrary argument values.

| Existing command path | Proposed applicability |
|---|---|
| `analyze`, `build`, `build-native-import`, `build-library`, `jit` | Select the subject compilation, including `--corelib` and source-grant library variants. |
| `compile-trrm`, both `compile-module` roles, both `compile-native` roles, `compile-native-set`, both `compile-native-graph` roles | Select only the new source compilation. Imported providers retain their own compiled operations. |
| `build-component`, `frontend-component`, `frontend-build-library`, `frontend-build`, `frontend-run` | Select the subject source, before hosted binding or projection checking/folding. The compiler/scanner component itself is a separate compilation, explicitly Development under this recommendation; the subject option must not change the compiler implementation's arithmetic. |
| `check`, `tokens`, `tree`, `scan-component`; source-free validation, resolution, link/build-from-artifact and run-from-artifact commands; internal workers | Reject the option. Syntax/scanning does not select subject arithmetic, and artifact execution must not re-profile operations. `jit` and `frontend-run` above differ because they compile subject source first. |

Convert the parsed choice into the same `CompilationOptions` used by public
factories; do not maintain a CLI-only arithmetic default. `--runtime fail-fast`
versus `--runtime checked-exceptions` keeps its existing required/optional
status, defaults and support restrictions for each command. Neither flag
supplies the other, and all four profile/runtime combinations have independent
meaning wherever the selected implementation supports them.

For the current experimental CLI, use its existing usage-error path
(`TRM2000`, exit 2); allocate no diagnostic code. For eventual
[`terrum.cli/1`](../specs/cli-protocol.md#2-common-envelope-and-command-outcome)
integration, invalid selection uses existing `usage-error`/status 2 and required
constant failure uses `source-diagnostics`/status 1. This does not implement
`--machine`, adopt experimental command identifiers, change worker statuses or
supervision, add response fields, or select another envelope version. Existing
response `profile` fields describe native/private support, not this option.

## Resolution, persistence and separate compilation

Resolve each affected source operation to **checked or wrapping semantics**
before any pass relies on its effects or value. Explicit operations already
select their own policy. Bound operations must not retain an unresolved
“use the current profile” execution policy. Updates retain their once-only
place/RHS evaluation, prefix/postfix results and successful-store timing;
checked failure does not commit a replacement.

Carry the selected per-operation fact through shared traversal/rewriting,
constant folding/execution, mandatory verification, dependency substitution,
specialization and lowering. Compiler-generated operations preserving a
source operation retain its policy; internal extent/bounds calculations do
not inherit permission to wrap. Existing public checked bound-node inputs
remain checked; emission APIs must not reinterpret them using an ambient
source option. Effect and NoThrow checking use the selected operation's
actual failures and the existing runtime-policy rules, not a profile-wide
promise that all operations are nonfailing.

Ordinary calls use published callable/variant/effect contracts. The caller's
profile neither inspects a callee body to choose its source policy nor grants
a wrapping variant. A separately compiled callee, including a retained
constant-execution body or generic body specialized later, keeps its resolved
operations. Admitted constant execution may execute that body, but does not
rebind its defaults in the caller's profile.

The selection is **compile-time-only**, not a Module compatibility dimension,
target profile, runtime mode or retained source-wide artifact selector.
No source-profile bit, Module field, Body field, Feature, rule revision or CLI
response field is needed. Emit explicit operations using the accepted
[IL policy representations](../specs/il-instruction-set.md#integer-source-profiles-and-explicit-il-semantics).
Frozen constants retain their resulting typed values. Target-deferred
constant work retains explicit operation policies and resolves target widths
before execution; the eventual JIT/AOT configuration cannot choose them again.

Private persistence must likewise preserve each operation's semantics.
Current `.trm` token-based checked nodes cannot silently acquire wrapping
meaning. Any necessary private codec extension must encode resolved wrapping
operations under exact schema agreement, not serialize this source selector
and reinterpret all old nodes. No private encoding or revision is assigned
here. A path unable to preserve an operation must decline it explicitly.
Existing runtime-policy/binding checks remain; producer and consumer source
profiles need not match. Replacement preserves all relied-on published
contracts, not profile-label equality. Producers establish truthful operation
facts; consumers retain required structural/support/binding checks without
being required to re-prove producer bodies.

## Integration owners and prerequisites

These are proposed integration edits, not changes made to the owners.

| Owner | Required integration |
|---|---|
| [Primitive operators](../specs/language/primitive-operators.md#integer-arithmetic-and-bit-operations) | Add the per-compilation selection boundary, Development omission default and independence from compiler-host configuration. Keep this as the semantic owner; preserve its complete arithmetic table. |
| [Conversions](../specs/conversions.md#conversion-policies), [proof/constant evaluation](../specs/language/proof-and-constant-evaluation.md#staged-constant-evaluation), [IL profiles](../specs/il-instruction-set.md#integer-source-profiles-and-explicit-il-semantics) | Cross-reference the selection owner. Preserve conversion matrices, explicit-policy precedence, definition-site operations and consumer-independent execution; no encoding amendment. |
| [CLI protocol §2](../specs/cli-protocol.md#2-common-envelope-and-command-outcome) | On adoption, reference the candidate selection contract for its core source-semantic commands only. Preserve existing envelope/outcome assignments and the separate gate for other commands. |
| [`Compilation.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Compilation.cs), [`Binder.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.cs), [`Binder.ConstantFolding.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstantFolding.cs), [`Binder.ConstEval.cs`](../../sources/Terrum.CodeAnalysis/Semantics/Binder.ConstEval.cs) | Add the candidate options types/overloads and validate/propagate context before checking. Resolve operations before folding or required evaluation, including mutations and dependency bodies. |
| [`BoundProgram.cs`](../../sources/Terrum.CodeAnalysis/Semantics/BoundProgram.cs), [`SemanticVerifier.cs`](../../sources/Terrum.CodeAnalysis/Semantics/SemanticVerifier.cs), [`NativeEmitter.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeEmitter.cs), [`NativeJit.cs`](../../sources/Terrum.CodeAnalysis/Native/NativeJit.cs) | Preserve explicit policies through rewrites, effects, proof and verified public bound-IR entry points. Source-free AOT/JIT consume operation facts, not source-profile arguments. The combined source frontend helper `ExecuteWithScalarFrontendProfile` needs an options overload: `(BoundProgram program, SourceText source, CompilationOptions options, string? registrarAssembly = null)`. Its legacy runtime-policy overload delegates with Development; preserve the existing component/runtime-policy compatibility check, then pass options to subject projection reading before verification. The already-bound compiler component is not re-profiled. |
| [Terrum-authored semantics](../../sources/Terrum.CodeAnalysis.Terrum/Semantics), [`ScalarFrontendHost.cs`](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.cs), [complete-program reader](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.Program.cs), [invocation reader](../../sources/Terrum.CodeAnalysis/Lexing/ScalarFrontendHost.Invocation.cs) | Thread the same subject options through every read/bind/verification path, including compact, tree, standalone and whole-program projections, before semantic checks/folds. Add corresponding options overloads to existing public `ReadResult`, `ReadProgram` and `BindLibrary` input shapes; legacy forms use Development and preserve their runtime argument. Validate as above. The current projection preserves source operators, so supply context out of band rather than inventing a projection/header profile bit. Keep compiler-component compilation separate. |
| [`Program.cs`](../../sources/terrumc/Program.cs), [`Program.Compilation.cs`](../../sources/terrumc/Program.Compilation.cs), [`Program.Scanner.cs`](../../sources/terrumc/Program.Scanner.cs), [`Program.Frontend.cs`](../../sources/terrumc/Program.Frontend.cs), [`Program.Modules.cs`](../../sources/terrumc/Program.Modules.cs), [`Program.TrRmExecution.cs`](../../sources/terrumc/Program.TrRmExecution.cs), [`Program.NativeModules.cs`](../../sources/terrumc/Program.NativeModules.cs), [`Program.NativeModuleSets.cs`](../../sources/terrumc/Program.NativeModuleSets.cs), [`Program.NativeGraph.cs`](../../sources/terrumc/Program.NativeGraph.cs) | Apply the command matrix, help text and validation uniformly, including fixed-position component parsers and the shared module/native option parser. Preserve experimental protocol boundaries. |
| [`ModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/ModuleCompiler.cs), [`NativeModuleCompiler.cs`](../../sources/Terrum.CodeAnalysis/Modules/NativeModuleCompiler.cs), [`TrRmExecutableCompiler`](../../sources/Terrum.CodeAnalysis/Modules/TrRmExecutableCompiler.cs) and their partials | Persist/load/remap explicit policies and frozen/deferred constants without producer-source rebinding or profile equality. Complete missing operation codecs before claiming a particular source-to-artifact path supports Release; this proposal allocates none. |

No change to an accepted arithmetic rule is required. The recommendation fills
an unspecified selection boundary and supersedes no existing selected API/CLI
contract. Wider scalar support, target-deferred execution and current-schema
TRRM migration retain their own implementation gates. Do not block already
implementable hosted/private slices on unrelated numeric or CLI redesigns, or
claim those slices complete the wider gates.

## Proposed acceptance cases

These are implementation requirements, not reports of executed checks.
Exercise currently admitted scalar types first; the accepted twelve-integer
matrix remains the conformance target, with `nint`/`nuint` widths from the
selected target, not the compiler host.

| Case and test owner | Required observation |
|---|---|
| Public factories — `tests/Terrum.CodeAnalysis.Tests/Semantics/CompilationTests.cs` and a focused profile fixture | Cover every overload/input shape, default options, both explicit profiles, both runtime policies, all trees sharing one choice, invalid enum values before source enumeration, and unchanged cancellation/reference obligations. The same selections produce the same semantics with Debug- and Release-built compiler hosts, including Release from a Debug host and Development from a Release host. |
| Arithmetic boundaries — semantic scalar tests | Check in-range equality and signed/unsigned overflow for add/subtract/multiply, signed-minimum negation, and unsigned zero/nonzero negation. Development fails without a result; Release produces exact width-limited results, including signed minimum and unsigned zero after maximum plus one. Explicit checked/wrapping operations override both defaults without introducing new variant syntax. |
| Updates — `UpdateExpressionTests.cs`, `CompoundAssignmentTests.cs`, `BoundTreeTraversalTests.cs` | Cover increment/decrement, prefix/postfix and add/subtract/multiply assignment, nested rewriting and parameter/local/field places. Observe once-only evaluation and successful commit; an overflowing checked update leaves storage unchanged, while wrapping updates commit the modular value. |
| Constants — `ConstEvalTests.cs`, `ConstExpressionTests.cs`, `ConstEvalModuleTests.cs` | Required checked operations diagnose overflow under either runtime policy; required wrapping operations freeze the exact modular value. Cover both direct expressions and admitted calls, with defaults chosen at the operation's definition site, including mixed-profile modules. Deferred target work retains policies and resolves before execution; unavailable support declines, never falls back to runtime or the consumer's default. |
| Independent contracts — scalar, conversion and `NoThrowTests.cs`/`NoThrowModuleTests.cs` fixtures | Preserve zero division, signed-minimum quotient overflow versus zero remainder, shift-count rejection, checked conversions and literal rejection in both profiles. Preserve float/code-unit/type restrictions and ordinary Abs. Effect checking distinguishes checked from wrapping overflow but does not inspect ordinary callee bodies or erase their published failures. |
| CLI — `tests/Terrum.CodeAnalysis.Tests/Cli` | Cover omission/valid choices, malformed/missing/duplicate/case/numeric/alias options, option-like positional paths, `--source`/stdin, all applicable command families and rejection by syntax/artifact-only paths. Invalid selection must not replace existing output. Keep `--runtime` and experimental JSON behavior independent; do not count unavailable machine-protocol support as tested. |
| Terrum-authored frontend — `Semantics/Frontend` and `CliTests.Frontend*.cs` | Compare hosted and authored-produced source projections under both subject profiles, including constants, effects, updates and cross-function calls through compact/tree/whole-program paths. Selecting the subject's Release profile must not recompile compiler/scanner components under Release. Run applicable Terrum fixtures through the existing component runner; a projection alone is not emission/runtime evidence. |
| AOT/JIT and persistence — native/CLI fixtures, `Modules/ModuleTests.cs`, `CliTests.TrRm*.cs` | Execute profile-distinguishing cases through both source-to-PE and source-to-JIT paths with existing supervision for failure cases. Round-trip supported explicit policies through private semantic/native modules and supported TRRM Bodies, using checked producer/wrapping consumer and the reverse under a common admitted runtime policy. Source-free loading, linking, replacement, constant execution and specialization must not consult the consumer host profile. Unsupported codecs decline rather than change semantics. |

On adoption, add corresponding cases to
[primitive-operator conformance](../specs/conformance/source-primitive-operators.md),
[proof/constant conformance](../specs/conformance/source-proof-and-constant-evaluation.md)
and the existing [tooling cases](../specs/conformance/tooling-cli-protocol.md)
without treating experimental commands as adopted protocol identifiers.
Selection/default and the API/CLI candidates above are the remaining approval
decision; no additional arithmetic-semantic choice is needed to implement
this recommendation provisionally.
