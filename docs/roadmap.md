# Development roadmap

The project is a bounded checked native compiler, not the whole language.
[Language and grammar specifications](grammar-spec.md) define target
contracts; the [implementation reference](implementation.md) records current
support; [proposals](proposals/README.md) collect unapproved choices.
Approval, implementation, and pre-v1 stability are separate.
Keep the grammar normative: source productions, semantic constraints and
explicit scope boundaries, not approval history or implementation snapshots.
Record support and migration details in the implementation reference; use
this roadmap for adoption sequencing and remaining work.

## Active work (priority order)

1. Carry accepted runtime and initialization contracts through compiler
   binding, verification, persistence and lowering. Keep their required
   metadata, proof, API and ABI work explicit in the planned areas below; an
   accepted source form is not a support claim. The
   [grammar](grammar-spec.md) records source contracts and their boundaries.
2. Refine concrete library proposals. Candidate signatures and contracts do
   not imply parser, compiler, runtime or library support.

The source-free TRRM scalar path remains fail-fast and supports the assigned
`NoThrow` and `DoesNotReturn` Effects in its bounded Body subset. It also
provisionally round-trips caller-unsafe Effects on top-level declarations,
and admits calls only within validated indexed Body unsafe scopes.
The kind-`0x06` scope representation is still reserved by the accepted
instruction-set contract; it remains a provisional implementation proposal,
not standardized Body support. Checked-exception Bodies and other
Effects/contracts remain unsupported. See [implementation](implementation.md)
for the current support boundary.

Hosted source binding now carries grammar-defined named calls, including
`out` and `init ref<T>` destinations, through verification, native lowering
and semantic-module persistence. The mapping preserves source-order
evaluation; overload selection remains outside current support.

Hosted contextual construction also supports `float32` and `float64` literals:
decimal and hexadecimal floating spellings are rounded directly to the target
format, based integers construct directly in that format, and unary signs
preserve IEEE bit patterns. Explicit `float32`/`float64` conversions follow
IEEE-754 target-format conversion with ties-to-even rounding independent of
ambient rounding and flush settings, as do typed `int32`, `uint32`, `int64`
and `uint64` conversions to either floating format. `char16`-to-floating
conversion is provisionally implemented by treating the code unit as an
unsigned value; its exact eligibility remains an unapproved proposal. The
hosted implementation also provisionally supports checked conversion from
either floating format to
`int32`, `uint32`, `int64` and `uint64`, truncating before the range check;
the exact pair eligibility remains an unapproved proposal. Floating arithmetic
(`+`, `-`, `*`, `/`) and IEEE comparisons are now supported for same-format
`float32`/`float64` values by the hosted binder, verifier and native emitter.
Arithmetic NaN bit encodings remain provisional under an unapproved proposal.
The bounded Terrum-authored scalar frontend now projects same-format unary
signs, target-typed decimal and hexadecimal floating literals, based integer
literals directly targeting `float32`/`float64`, `+`, `-`, `*`, `/` and IEEE
comparisons through v85. The hosted reader validates token spans and
reconstructs exact target-format literal bits; verification and emission
remain hosted. V85 also projects conversions between `float32`/`float64` and
from `int32`, `uint32`, `int64` and `uint64` to either format.
`char16`-to-floating conversion is provisionally projected through v85 under
its still-unapproved eligibility proposal; verification and emission remain
hosted. The frontend also provisionally projects float-to-integer
conversions under the still-unapproved pair-matrix proposal; verification and
emission remain hosted. Source-free TRRM provisionally
supports same-format floating add, subtract, multiply and divide under an
unapproved proposal. Same-format comparisons and exact-bit constants,
explicit conversion between the two floating formats, conversion from the
four fixed-width integer types, `char16`-to-floating conversion and unary
negation are separately provisional under their unapproved proposals.
Floating-to-integer conversion is also provisionally encoded as a distinct
fail-fast operation under its unapproved proposal; checked-exception TRRM
remains unsupported. None of these floating operations is part of the approved
`(1, 0)` Body profile.

The bounded Terrum-authored scalar source frontend supports v85 complete
global-function snapshots and source-order scalar call targets, including
forward and recursive calls with up to three by-value scalar arguments.
It permits calls to void functions only as direct
expression-statement roots and admits the existing void-only
`[DoesNotReturn]` contract in complete snapshots and carries its verified
continuation effect across those calls. Independent source auditing precedes
the existing hosted verifier/emitter. General binding/imports, broader
output/reference-call migration beyond scalar `out` and `out var`, and
bootstrap compilation remain pending; this does not extend source-free TRRM
checked exceptions.

Complete-program scalar projections also resolve up to eleven top-level
internal, non-generic aliases to scalar types in return, parameter and local
type positions, including forward chains. The reader independently resolves
them from original source; duplicate, unresolved, cyclic, public, attributed
and generic alias declarations reject. The host's separate type/function name
spaces are retained, and alias names remain invalid as explicit conversion-call
targets. Invalid aliases report their offending token span. V85 layouts and
hosted verification/emission are unchanged.

Projection v85 carries named by-value and scalar `out`/`init ref<T>`
arguments through bounded direct scalar calls. Arguments remain source-order
children while the independent reader verifies their formal-parameter map
against source names. Single-function v85 covers self-calls; complete-program
v85 covers direct self/global calls. Overload selection and the hosted
verifier/emitter boundary remain unchanged.

Complete-program v85 also projects bounded `switch` statements with selectors
of type `int32`, `uint32`, `int64`, `uint64`, `bool` or `char16`, distinct
constant-value arms and a final `_` arm. Floating selectors are excluded
because the projection lowers matching to hosted equality without defining NaN
constant-pattern semantics. Arm bodies
are braced blocks containing already-supported statements; guards, patterns,
fallthrough forms, and missing or misplaced defaults reject. The reader
source-audits the arm records, captures the selector once, and lowers the
ordered arms to hosted conditionals. This reuses previously invalid statement
kinds 38/39 and local flag 8 without changing frame layouts; the current
reader accepts them only after source validation, while older v85 readers
reject unknown records. Hosted verification and emission remain authoritative,
single-function projection is unchanged, and this does not add switch support
to source-free TRRM or claim bootstrap support.

V85 complete snapshots carry the existing scalar `out`
parameter modes, explicit destinations and `out var` call destinations.
They also carry the existing scalar `init ref<T>` mode and matching
`init`/`init var` destinations to complete snapshots. Hosted
definite-publication verification remains authoritative; general
`init ref<T>` migration and bootstrap compilation remain pending. Single-
function v85 carries the existing scalar `init ref<T>` mode and
non-Boolean scalar `out` mode to the bounded self-call frontend, including
matching `out var` destinations in recursive self-calls.
Complete-program v85 supports existing-destination calls to scalar `out` parameters on
non-Boolean/void-returning functions; the hosted verifier requires publication
on every normal return, and the hosted emitter publishes them without using
the result as a success flag. It also supports matching `out var` destinations
for these calls, with the destination available after normal return. This
remains a hosted-verifier/emitter-backed projection; it does not broaden
non-scalar non-Boolean outputs. Projection tags are alpha implementation/validation
details, not roadmap or product versions. Only v85 is current for both families,
all function frames and diagnostics; every obsolete v1–v84 tag is rejected.
Both v85 projections also project the accepted `is init` test for named scalar locals and
parameters; hosted definite-initialization verification and native emission
remain authoritative.

The hosted compiler now accepts non-Boolean and void `init ref<T>` completion
for supported output types, requiring full publication on every normal return
and withholding publication on exceptions. Unique outputs transfer on normal
completion independently of the non-Boolean result bits. Managed graph exports
remain limited to handle-free Copy outputs; the bounded scalar projections
project scalar non-Boolean/void init-ref declarations to the hosted verifier
and emitter through the current v85 layouts.

Hosted non-Boolean and void `out` declarations and calls also support
non-unique struct outputs, including `out var` across public semantic and
native module boundaries when the established Copy export shape permits it.
The hosted verifier retains the every-normal-return publication rule;
Boolean `out` keeps conditional publication. Unique outputs and scalar
frontend projections beyond scalar output types remain unsupported here.
This is implemented hosted support, not a proposal approval or a general
`out`/`init ref<T>` migration.

Single-function v85 extends the same bounded self-call
frontend with source-audited `unsafe(expr)` scopes and caller-unsafe callable
declarations. Both projection families use only v85; verification and emission
remain hosted.

Separately, semantic-module revision 59 provisionally persists top-level
caller-unsafe contracts and bounded readonly-instance and static method slices for
public non-unique `Copy` owners; calls still require explicit `unsafe { }`
blocks and readers reconstruct
authorization from that scope. Executable TRRM provisionally round-trips
top-level caller-unsafe Effects and supports calls only inside validated
indexed Body unsafe scopes. Other unsafe members remain unsupported; the
broader reference-call metadata proposal remains unapproved.

## Planned work (priority order)

The roadmap has four current areas, in area priority order. The sequence below
is priority order across pending work; numbering does not imply that a
prerequisite has already been met.

| Priority | Area | Next contracts and dependencies |
|---|---|---|
| 1 | Full runtime/structured contracts | Priorities 1–3, 9–10 below |
| 2 | Resources/graphs | Priority 4 |
| 3 | General separate compilation | Priorities 5–6 and 11 |
| 4 | Rich libraries/self-hosting | Priorities 7–8 and 12 |

1. **Full runtime/structured contracts — references and safety.** General
   reference/access/loan metadata and caller-unsafe member contracts still
   need definition before wider use across calls or imports. Structured
   semantic-module revision 59 provisionally carries top-level caller-unsafe
   obligations and narrow readonly-instance and static `Copy` method slices,
   including recursively exportable by-value Copy signatures, through explicit
   unsafe-block scopes; this bounded implementation does not settle
   the general schema or approve the
   [reference-call metadata proposal](proposals/reference-call-metadata.md),
   which recommends a narrow first dependency registry and reuse of the
   assigned caller-unsafe effect bit. The proposal recommends a structurally
   validated indexed-Body region for explicit caller-unsafe scopes. A bounded
   kind-`0x06` implementation now admits indexed calls provisionally, while
   compact-linear calls and calls without validated unsafe scope remain
   unsupported; the broader proposal remains unapproved.
2. **Full runtime/structured contracts — initialization and outputs.** Complete
   general `out`/`init ref<T>` declaration and call migration with definite
   initialization, cleanup and publication rules. Preserve their distinct
   contracts; this is not a parser-only rename. Carry the
   [static initialization contracts](grammar-spec.md#static-initialization-and-load-scoped-storage)
   through separate metadata/schema work before compiler adoption: loaded
   closed-type groups and lazy units, explicit constructor triggers, checked
   sharing, failure/recovery, initialization effects and load-scope cleanup.
   Do not infer sharing permission from `static` or adopt source forms without
   their state, lifetime and publication machinery.
   Preserve uniform generic/concrete reference matching and supply checked
   runtime type evidence/generic execution before adopting runtime captures.
   General source catches/filters require incremental post-inner-cleanup
   selection, not reuse of the bounded Windows first-pass discriminator as an
   authored predicate.
3. **Full runtime/structured contracts — conditions and constants.** Specify
   published/static conditions and required literal/constant domains, including
   proof/effect limits, before claiming general proof execution.
4. **Resources/graphs.** Extend beyond bounded owners, views and managed graphs
   only after representation, lifetime, cleanup, failure and publication
   rules are defined. General stored views, partial field moves, arbitrary
   owning graphs, allocators and relocation remain pending.
5. **General separate compilation — layouts.** Specify fixed public
   `Blittable` layouts, packing and over-alignment with target limits,
   metadata, access lowering and compatibility checks. Exact source spellings
   and implementation are not settled by this plan.
6. **General separate compilation — binding contracts.** Complete overload
   and witness selection plus adapter, family and explicit-interface
   contracts. Keep unresolved declaration, lookup and compatibility choices
   pending rather than adopting convenient host behavior.
7. **Rich libraries — builders and handlers.** Specify registered builders and
   handlers with checked eager capture before draining, ownership, refusal,
   cleanup, failure and publication behavior.
8. **Rich libraries — callables.** Progress in order from fixed-signature
   callables and tuples, to generic scalar/SIMD functors, to contextual generic
   lambdas. Each stage depends on its declared type, effect, access, loan,
   ownership and ABI contracts; it does not imply type packs or polymorphic
   callable erasure.
9. **Full runtime/structured contracts — conversions.** Implement accepted
   numeric-conversion policies, including floating support, only with their
   target contracts and boundary behavior. This is not current floating-point
   compiler support.
10. **Full runtime/structured contracts — asynchronous producers.** Specify
    completion, producer, capture and suspension contracts after their loan,
    effect, ownership and cleanup prerequisites. This targeted work is not an
    async-runtime support claim.
11. **General separate compilation — evolution.** Add evolvable-case
    contracts, exact-contract forwarders and compatibility analysis without
    silently widening existing contracts.
12. **Rich libraries/self-hosting — bootstrap components.** Grow the current
    bounded components toward a compiler bootstrap only as their language and
    library prerequisites are met and through the shared
    [self-host gate](#compiler-and-self-hosting-gate). A bounded semantic-module
    JIT path is provisionally implemented under the still-unapproved
    [invocation-scoped source-grant proposal](proposals/invocation-scoped-source-grant.md);
    the maintained Windows x64 build now also has an explicit opt-in,
    provisional source-grant companion for the checked UTF-16 scalar adapter,
    scalar source-text-length, source-code-unit, source-offset and
    source-position queries, scalar source-line-count and source-line
    content/including-break end queries, a scalar source-range validity
    predicate, and paired scalar integer-parsing queries, with selected-export
    descriptors bound to the image hash and end-relative
    reads based on invocation length. The source-free
    compiler-primitives DLL remains unchanged; this does not promote the
    unapproved ABI or establish general source-dependent DLL support. The
    scanner and scalar frontend still rely on hosted verification/emission. A separate
    [native source-grant library proposal](proposals/native-source-grant-library.md)
    records the candidate per-call ABI and remaining promotion decisions; it
    remains unapproved.
    A scanner-specific `scanner-10` persisted semantic-module JIT profile is
    implemented provisionally under the still-unapproved
    [scanner invocation-profile proposal](proposals/scanner-invocation-profile.md).
    It does not expand the source-only grant contract or establish support for
    graphs, module sets, native DLLs or persisted AOT.

These are pending contracts, not support claims. Exact grammar, metadata and
implementation remain subject to their canonical specifications and proposals.

### Later capabilities excluded from this sequence

Production JIT, additional backends, wholesale source generation/reflection/
interop and scalable SIMD remain outside these four current areas. This does
not exclude the existing bounded JIT proof or the targeted asynchronous
contracts listed above.

Future tooling prerequisites also remain outside the four current areas and
are not implementation claims. Semantic facts must distinguish unknown,
invalid and proven; an empty diagnostic set is not safety proof. Semantic
queries need snapshot, target/options and dependency identity with fact
provenance, and semantic edits must recheck affected contracts and diagnostics
against the resulting compilation before publication. Edits should be
candidate batches with checked preconditions. Generated source needs a stable
document identity, producer/version/input digest and authorization, with
separate spelling and expansion maps that can associate a generated range
with zero, one or several original ranges; diagnostics may carry related
locations, rule arguments and fixes scoped to the same snapshot.

Analyzer and generator APIs should be versioned snapshot consumers, not
callbacks that mutate compiler internals. Pin dependencies and input manifests;
require explicit capabilities, cancellation and resource bounds. Generators
approved by the author may access only declared implementation scope; consumer
analyzers do not inherit that grant. Comments and generated text grant no
execution, filesystem, network or private-member authority. Generation must
use declared deterministic inputs and not depend on undeclared clock,
environment or network state. An in-process interface alone is not a security
boundary. Plugin discovery, query scheduling, remote services and persistent
query caches remain deferred until usage establishes requirements; cache keys
must include compiler/schema versions, target, references, contract-rule
version and authorization context, not just source content.

## Completed work (newest first)

Hashes identify local repository commits. Detailed support and compatibility
boundaries remain in the [implementation reference](implementation.md).

| Commit | Completed milestone |
|---|---|
| `802a69ae` | Add provisional source-dependent `CanGetSourceRange` predicate with hosted subtext-boundary parity, maximum-grant coverage and maintained manifest binding |
| `4f550508` | Add provisional scalar source-line content and including-break end queries with same-grant range reconstruction, boundary coverage and maintained manifest binding |
| `175cbe05` | Add provisional scalar source-grant integer parsing pairs with all-radix oracle coverage and a maintained manifest, without changing the source-free companion or classifier |
| `121c2e27` | Round-trip provisional scalar source-position coordinates through the source-offset export on the same grant |
| `b6bf45a9` | Add provisional scalar source-position queries matching hosted UTF-16 line/character coordinates, including CRLF and EOF boundaries |
| `c62d94b4` | Add provisional scalar line/character-to-source-offset query with hosted boundary parity across both policies and the maintained manifest |
| `2114a1e1` | Add the provisional scalar source-code-unit query with exhaustive UTF-16 value checks, hosted `SourceText` parity and maintained manifest binding |
| `c7063261` | Add the provisional scalar source-length query through an isolated component, preserving whole-program classifier and source-free DLL boundaries |
| `7d0f7b38` | Exhaust all 65,536 `char16` inputs for all four saturation and checked-conversion widening helpers across source-free DLLs, persisted consumers and Terrum component tests |
| `b5dee01b` | Add provisional scalar source-line-count export to the maintained companion with hosted line-count parity under both policies |
| `7d2a7938` | Exhaust all 65,536 `char16` inputs for the four integer-wrapping widening candidates through Terrum, persisted consumers and the source-free DLL |
| `1ea672d0` | Verify conditional Boolean `out` vacancy, later initialization and compatible provider replacement across native consumer sets under both runtime policies |
| `ec530cda` | Lower end-relative source-grant reads from the invocation's captured length, with both-policy boundary coverage |
| `fce7db24` | Add the opt-in provisional maintained source-grant companion with hash-bound export metadata and preserve image/manifest pairs on publication failures |
| `89a896d5` | Compare test-only native source content and including-break line extents with hosted `SourceText.Lines` under both runtime policies |
| `7e71c3dd` | Match generated native source positions and inverse offsets to hosted mappings at every offset of `CR` plus each possible UTF-16 code unit |
| `1d891ea6` | Match native line/character and inverse-offset results to hosted SourceText at both offsets of every UTF-16 code unit |
| `9c2ab483` | Verify native CRLF pairing and adjacent-line-break counts for every possible UTF-16 code unit following CR under both runtime policies |
| `2a711178` | Compare generated native line counts with hosted `SourceText` for every single UTF-16 code unit under both runtime policies |
| `983fb9a1` | Round-trip test-only native source positions through scalar line/character queries against hosted `SourceText.TryGetOffset` under both runtime policies |
| `cb3fd7b8` | Compare test-only generated native source positions with hosted `SourceText.GetLinePosition` for all short-fixture offsets and maximum-boundary samples under both runtime policies |
| `8c226927` | Compare test-only generated native source line counts with hosted `SourceText` across line-break, malformed-UTF-16 and maximum-bound cases under both runtime policies |
| `d699fc80` | Verify test-only generated source-grant bindings follow private-callee dependence while production DLL emission remains rejected |
| `403b51b0` | Extend caller-unsafe semantic-module method signatures to public non-unique Copy values; verify links, native graphs and module sets under both runtime policies |
| `6a2f3fe5` | Execute persisted native consumers with replacement providers publishing `out` values from `finally` under both runtime policies |
| `efcc7d2a` | Verify semantic and native module replacements reject `out`/`init ref<T>` mode changes under both runtime policies |
| `b843eef0` | Verify semantic and native module replacements preserve retained `NoThrow` and `DoesNotReturn` effects under both runtime policies |
| `fd64de10` | Verify native module-set replacement preserves retained `NoThrow` and `DoesNotReturn` effects under both runtime policies |
| `04dcbdb1` | Verify Copy-output graph replacements preserve the `out` versus `init ref<T>` contract |
| `cf9a107b` | Verify unchanged consumers execute against compatible 16-byte Copy-output graph replacements in both modes |
| `4b3e7a9a` | Verify retained `NoThrow` graph replacement compatibility under both runtime policies |
| `dfedc517` | Verify retained `DoesNotReturn` graph replacement compatibility under both runtime policies |
| `8dc1272c` | Verify native graph replacement rejects weakening a retained `DoesNotReturn` promise |
| `350e1b2e` | Verify source-dependence fixed-point propagation through terminating mutual recursion under both runtime policies with hosted source-grant JIT parity |
| `e8263b74` | Verify Boolean scalar and 16-byte Copy-struct `out` vacancy on false and publication on true across native graphs under both runtime policies |
| `b50470b3` | Verify conditional scalar and 16-byte Copy-struct `init ref<T>` publication across native graphs under both runtime policies |
| `70a23fdc` | Verify 16-byte public Copy-struct `out` and `init ref<T>` execution across native graphs under both runtime policies |
| `cbe70846` | Verify 16-byte public Copy-struct `out` execution across native graphs under both runtime policies |
| `58efc268` | Verify Boolean `out` vacancy on false and publication on true across native graphs under both runtime policies |
| `bd345edf` | Verify both scalar `out` and `init ref<T>` execution and replacement directions across native graphs under both runtime policies |
| `1e7c69a8` | Verify conditional scalar `init ref<T>` publication across native graphs under both runtime policies |
| `82971fc4` | Keep scalar `init ref<T>` vacant after checked failure across native graph calls |
| `ec61562b` | Verify scalar `init ref<T>` output and replacement compatibility across native graphs |
| `d9990d0e` | Preserve `DoesNotReturn` promises when replacing native module-set providers |
| `7bca6ec4` | Reject semantic/native replacement that removes a retained `NoThrow` or `DoesNotReturn` promise |
| `ac08b34d` | Verify conditional scalar frontend calls to `[DoesNotReturn]` functions |
| `81f586a8` | Verify scalar frontend `[DoesNotReturn]` library parity under both runtime policies |
| `9dae43f7` | Check that a staged unique `init ref<T>` remains unpublished on false across module sets |
| `65fb3bbe` | Verify conditional unique `init ref<T>` transfer through native module-set replacement |
| `def3cb33` | Preserve vacant `init ref<T>` state after checked failure across native consumer sets |
| `8c287c19` | Verify named `init ref<T>` output in native consumer sets under both policies |
| `43620eb9` | Reject semantic/native replacement that changes `out` versus `init ref<T>` |
| `74316f31` | Verify generic `init ref<T>` remains unpublished after failure across module replacement |
| `77230db0` | Preserve hosted unsafe-caller diagnostics across scalar frontend rejection |
| `c3406537` | Reject unscoped caller-unsafe frontend calls without replacing prior artifacts |
| `43014ae1` | Compare caller-unsafe frontend library artifacts with hosted builds and execute both scope forms |
| `dd5eb071` | Compare checked catch/finally library output and cleanup behavior with hosted builds |
| `97685f7c` | Compare scalar switch library artifacts with hosted conditional output |
| `8bafab14` | Verify positional argument evaluation order across linked module artifacts |
| `6e5c1a4e` | Preserve a caller root across nested imported graph replacement and collection |
| `3c30d6d1` | Verify source-order evaluation of named output calls across module artifacts |
| `0232c341` | Share the scanner source bound across scalar projection readers |
| `667fbe28` | Verify frontend source-limit rejection before artifact publication |
| `e282c0de` | Compare maximum-length scalar frontend builds with hosted output under both runtime policies |
| `e5e5ff82` | Compare maximum-length zero and overflow inputs for `uint64` parsing in every radix |
| `23be9989` | Compare all five integer parsers on maximum-length successful zero and overflowing digit slices |
| `36e567af` | Check maximum-length ASCII literal output against exact-capacity buffers for all eight encodings |
| `9e17bd0a` | Encode a maximum-length ASCII StringLiteral into exact-capacity output under both runtime policies |
| `08300113` | Retain every lexical diagnostic from a maximum-length malformed source in TokenDiagnosticSnapshot |
| `1bfe03be` | Publish and verify maximum-density token, trivia and integer snapshots from scanner input |
| `e41ae04e` | Traverse every token in a maximum-length scanner snapshot through TokenCursor under AOT and JIT |
| `de783076` | Exercise every TokenSnapshot index on a maximum-length scanner source under AOT and JIT |
| `d41e924a` | Exercise every line and code-unit position in a maximum-length all-CRLF indexed source |
| `9de43928` | Validate the densest scanner-10 integer record sequence under the maximum source bound |
| `501b2645` | Fill the scanner-10 trivia record bound with maximum-length newline input |
| `f19b42ca` | Fill the scanner-10 diagnostic record bound with maximum-length malformed UTF-16 input |
| `8c7e000b` | Stress maximum-length scanner-10 token and trivia record publication |
| `58cea52b` | Compare malformed BMP `\u` spellings through scanner-10 with hosted diagnostics |
| `6e295cb9` | Compare malformed and unterminated Unicode escapes through scanner-10 with hosted diagnostics |
| `837fd45f` | Check scanner-10 integer snapshots against binary, decimal and hexadecimal uint64 limits |
| `bc747956` | Compare scalar frontend switches with hosted conditionals at signed minima, both Boolean values and char16 maximum |
| `83838e0e` | Compare every isolated raw surrogate code unit in scanner-10 with hosted lexer diagnostics |
| `3d359f50` | Compare every supplementary scalar in both identifier-start and identifier-continue positions through scanner-10 |
| `b32f4303` | Compare all raw supplementary Unicode scalars in quoted strings through scanner-10 against the hosted lexer |
| `a1cd2981` | Compare maximum-length scanner grants ending in either unpaired surrogate with the hosted lexer |
| `fca9fa3b` | Compare every rejected surrogate-valued `\U` escape through scanner-10 against the hosted lexer |
| `780f072a` | Compare every valid supplementary `\U` scalar and invalid surrogate/out-of-range spellings through scanner-10 against the hosted lexer |
| `981042fb` | Check maximum-length scanner-profile input with a supplementary scalar at the quoted-source boundary |
| `6cff8a90` | Compare supplementary Unicode identifier start/continue range endpoints through persisted scanner-10 against the hosted lexer |
| `dd4cde86` | Compare raw supplementary string scalars at every Unicode-plane boundary through the persisted scanner-10 profile |
| `c2dd22d4` | Compare persisted scanner-10 `\U` escapes at every Unicode-plane boundary and reject surrogate/out-of-range values against the host lexer |
| `cb6d3410` | Check duplicate switch constant-label rejection across all supported scalar selector types |
| `ded27ee2` | Compare switch default-arm selection with hosted conditionals for all supported scalar selector types under both runtime policies |
| `254bbff9` | Compare scalar frontend switch selectors with hosted conditionals at all supported type boundaries under both runtime policies |
| `61663624` | Compare the persisted scanner-10 profile with the host lexer for every BMP `\u` escape and surrogate diagnostic |
| `75cacbfc` | Verify all BMP `\u` character literals and surrogate diagnostic spans through source-grant JIT |
| `b7ffa3a6` | Verify all BMP `\u` escape scalars, consumed widths and surrogate diagnostics through source-grant JIT |
| `71a6b6d0` | Verify all BMP `\u` scalar escapes, surrogate rejection and literal interior offsets through source-grant JIT |
| `135963eb` | Verify sequential StringLiteral cursor scalar and next-offset parity across every valid scalar spelling |
| `212f2722` | Verify successful StringLiteral encoding across all ASCII and Latin-1 scalar values and exhaustive ASCII StringRow parity |
| `30d89c48` | Compare StringRow output with the hosted writer for every nonzero Unicode scalar |
| `62711bdd` | Compare StringRow names and namespaces for every nonzero Unicode scalar |
| `0205b2aa` | Verify rejection of every surrogate-valued `\U` escape through source-grant JIT |
| `dde285a9` | Compare every valid Unicode scalar encoding with host encoders through source-grant JIT |
| `41f9722e` | Compare every supplementary scalar encoding with host encoders through source-grant JIT |
| `fff80843` | Verify every isolated UTF-16 surrogate is rejected by native compiler primitives |
| `47c35ecd` | Verify every supplementary Unicode escape through character, string-escape and string-literal source-grant JIT |
| `0fa46337` | Verify every valid Unicode scalar through native UTF-16 compiler primitives |
| `49a56eb2` | Verify every supplementary scalar spelling through character- and string-literal source-grant JIT |
| `24da1f2e` | Exhaustively verify every supplementary scalar and high-surrogate boundary through the test-only native grant ABI |
| `fd2cd282` | Verify native source-scalar decoding at one-unit ends for every BMP code unit |
| `fbbe77dc` | Exhaustively verify signed and unsigned native source reads through the test-only grant ABI |
| `1f763d43` | Exhaustively verify scanner-delimited BMP string literal scalars through source-grant JIT |
| `a895ca24` | Exhaustively verify scanner-delimited BMP string escape introducers through source-grant JIT |
| `ae2d4e4d` | Exhaustively verify scanner-delimited BMP character literals through source-grant JIT |
| `c65293fd` | Exhaustively verify every UTF-16 code-unit read through source-grant JIT |
| `b8df80c0` | Erase recursively nested literal-false loops without regions |
| `284f62a2` | Execute fallthrough bodies once when erasing literal-false post-tests |
| `e3c4cabd` | Preserve returning direct calls before terminal loop exits |
| `a1737884` | Lower scalar-only conditional prefixes before acyclic loop tails |
| `1db31f72` | Preserve requirement checks and their fail-fast status before terminal loop exits |
| `8837912c` | Erase empty literal-false pre-tests without creating loop regions |
| `249a089c` | Exercise nested acyclic breaks through persisted CLI and native execution |
| `9736e304` | Verify nested and enclosing acyclic breaks retain distinct exits |
| `7fa2b0f4` | Preserve proven nested-loop breaks before enclosing terminal tails |
| `ac170e0f` | Erase nested return-only loops without adding indexed loop regions |
| `46c1bb4c` | Cover scalar initializer prefixes in embedded-iterator for-loop erasure |
| `7e58375b` | Verify persisted failure status for local initializers before terminal loop exits |
| `a300506c` | Preserve supported scalar local initializers before terminal loop exits |
| `3e6bd6b8` | Preserve supported scalar assignment expressions before terminal loop exits |
| `64cfabcf` | Verify compound RHS ordering and division-by-zero failure survive terminal loop erasure |
| `c3ac3c1b` | Cover terminal-loop compound prefixes across integer widths and shifts |
| `22271cbb` | Verify terminal-loop compound overflow remains a persisted fail-fast failure |
| `4f2d4bcc` | Preserve primitive integer compound assignments before terminal loop exits |
| `0d7a74e5` | Preserve scalar local initialization before terminal loop exits |
| `58f25496` | Exercise direct-value SourceRange returns from concrete CopyAlgorithms specializations |
| `35725fc2` | Exercise concrete CopyAlgorithms specializations through test-only native Token wrappers |
| `1b60f08a` | Exercise all 24 IntegerFacts predicates through test-only native compiler-primitives exports |
| `f041664c` | Preserve bounded scalar updates and assignments before terminal loop exits |
| `4e4079ec` | Lower terminal implicit-else paths in acyclic loop erasure |
| `9a7687b4` | Omit embedded for iterators when every terminal branch returns or breaks |
| `3cf962d1` | Lower terminal nested acyclic loop branches to existing returns and fallthrough without TRRM loop regions |
| `9ffc7292` | Verify acyclic loop erasure skips post-loop conditions and preserves for initializers |
| `8903a118` | Erase return-only and direct-break acyclic loops to existing terminal or fallthrough flow without TRRM loop regions |
| `1d93e1bd` | Erase return-only acyclic source loops to terminal returns without adding TRRM loop regions or wire forms |
| `50c47740` | Provisionally validate indexed caller-unsafe regions and scope exits without changing the accepted Body layout |
| `45a5f7e3` | Round-trip top-level caller-unsafe Effects in executable TRRM while rejecting calls without validated Body scopes |
| `a7780a64` | Exercise both existing SourceRange end-query forms through the internal Windows x64 compiler-primitives DLL |
| `bca63d3d` | Exercise the existing Token constructor and aggregate argument/return path through the internal Windows x64 compiler-primitives DLL |
| `a1e83b6a` | Exercise existing IntegerFormatting decimal and radix formatters through the internal Windows x64 compiler-primitives DLL |
| `2fde0cac` | Exercise existing IntegerFormatting radix-length queries through the internal Windows x64 compiler-primitives DLL |
| `2cc4eade` | Exercise existing float-selection candidates through the internal Windows x64 compiler-primitives DLL using integer bit carriers |
| `ff2dc783` | Verify IntegerFacts native execution under both runtime policies |
| `a79e0884` | Verify existing private IntegerFacts predicates in native execution |
| `15c8f6b2` | Exercise existing integer wrapping candidates through the internal Windows x64 compiler-primitives DLL |
| `8e522cb3` | Exercise existing integer saturation candidates through the internal Windows x64 compiler-primitives DLL |
| `821099f0` | Exercise existing checked integer and char16 conversion candidates through the internal Windows x64 compiler-primitives DLL |
| `81a462ea` | Exercise existing checked 32-bit arithmetic helpers through the internal Windows x64 compiler-primitives DLL |
| `57ffb835` | Exercise existing IntegerMath candidates through the internal Windows x64 compiler-primitives DLL |
| `dbc534a2` | Exercise the existing 32-/64-bit BitOperations candidates through the internal Windows x64 compiler-primitives DLL |
| `c5e9e400` | Exercise checked wide arithmetic through the internal Windows x64 compiler-primitives DLL |
| `79155d79` | Exercise checked wide accumulators through the internal Windows x64 compiler-primitives DLL |
| `13eb3cc8` | Invoke wide signed magnitude through the internal Windows x64 compiler-primitives DLL |
| `2f9bb5da` | Reuse wide signed magnitude when parsing signed integer limits |
| `93a7cd51` | Reuse wide-integer signed magnitude in integer formatting |
| `aa9cd378` | Exercise checked `SourceRange` construction through the Windows x64 compiler-primitives DLL |
| `505ea127` | Verify the valid two-element index-from-end boundary in scanner host-buffer access |
| `524a7b25` | Supply the scalar parser test harness with its required `FrontendProgram` buffer |
| `a2a7f929` | Exercise all CharacterFacts exports across the UTF-16 code-unit domain through the Windows x64 compiler-primitives DLL |
| `17eb08cc` | Exercise ASCII hexadecimal conversion through the Windows x64 compiler-primitives DLL |
| `9ac38530` | Share ASCII hexadecimal digit parsing across character and string escapes |
| `4f078737` | Exercise UTF-16 compiler primitive exports from Windows x64 |
| `900f9f15` | Extract pure UTF-16 code-unit decoding into compiler primitives |
| `e1f5e167` | Verify decimal digit conversion across UTF-16 values through persisted modules |
| `7549848e` | Verify every 32-/64-bit BitOperations position and round-up boundary through modules |
| `8b973b9c` | Verify wide decimal and radix accumulators through persisted modules |
| `99671971` | Verify 32-/64-bit BitOperations round-up failure outputs stay unreadable |
| `9b19cf9b` | Verify provisional IntegerMath helpers through persisted semantic modules |
| `b821d3fb` | Verify provisional integer wrapping helpers through persisted semantic modules |
| `efa78c74` | Verify provisional integer saturation helpers through persisted semantic modules |
| `223a9d1b` | Verify checked 32-bit arithmetic helpers through persisted semantic modules |
| `a6c8b305` | Verify checked wide-integer helpers through persisted semantic modules |
| `089e2800` | Verify checked conversion helpers through persisted semantic modules |
| `4199a815` | Add provisional persisted scanner-profile JIT support |
| `35db563a` | Return precise source spans for rejected scalar aliases |
| `2c0d6dbf` | Preserve hosted type/function namespace parity for scalar aliases |
| `2a79685d` | Verify `SourceRange` boundaries through persisted semantic-module JIT |
| `91764c60` | Project bounded internal scalar aliases through complete v85 snapshots |
| `e13aa606` | Verify managed token-chain behavior through persisted semantic-module JIT |
| `b16d95c` | Execute `CopyAlgorithms` generic specializations through persisted semantic modules |
| `fe9245d` | Verify `IntegerFacts` checked predicates through persisted semantic-module JIT |
| `c85735b` | Exhaustively verify `CharacterFacts` through semantic-module JIT grants |
| `c1021ed` | Verify bounded UTF-16 scalar decoding through semantic-module JIT |
| `1844cc5` | Verify ordinal `StringRow` name and namespace comparisons through semantic-module JIT |
| `dc55669` | Exercise provisional `StringRow` name and namespace encodings through semantic-module JIT |
| `dd8b0f8` | Verify all `StringLiteral` encodings through persisted semantic-module JIT |
| `767bd39` | Exercise decimal and all-radix integer formatting through semantic-module JIT |
| `8878f1c` | Exercise all-radix `IntegerParsing` through persisted semantic-module JIT grants |
| `2a8bd66` | Exercise streaming and indexed source-line queries through semantic-module JIT |
| `f206b87` | Verify caller-unsafe semantic-module scope reconstruction and replacement compatibility |
| `1e4d06c` | Exercise decimal `IntegerParsing` through invocation-scoped semantic-module JIT grants |
| `335ecc7` | Exercise `StringEscape` decoding through invocation-scoped semantic-module JIT grants |
| `025e2c8` | Exercise `SourceLineIndex` counts and extents through invocation-scoped semantic-module JIT grants |
| `fa84e73` | Exercise provisional UTF-8 `StringRow` encoding through invocation-scoped semantic-module JIT grants |
| `d94ffcd` | Execute the bounded `StringLiteral` decoder through invocation-scoped semantic-module JIT grants |
| `180fa2f` | Execute the bounded `CharacterLiteral` decoder through invocation-scoped semantic-module JIT grants |
| `8cce660` | Persist invocation-scoped `Host.Source` requirements for semantic-module JIT execution |
| `3fd426c` | Pre-size verifier lookup indexes |
| `d15dd48` | Pre-size verifier uniqueness sets |
| `677ab34` | Pre-size native import validation maps |
| `d9c60ca` | Pre-size producer linkage name map |
| `ae35fcf` | Parse scalar frontends from indexed snapshot windows |
| `b5abb12` | Add repeatable indexed TokenCursor reads |
| `5a62f4e` | Pre-size CET continuation validation set |
| `cf78764` | Size graph cleanup index by actual entries |
| `3901f21` | Pre-size graph cleanup function index |
| `37baa12` | Pre-size graph catalog traversal sets |
| `a0bd8bb` | Bound SourceMap file indexes by table row counts |
| `d599d6f` | Pre-size graph continuation validation |
| `9aa551a` | Pre-size indexed-body liveness sets |
| `1db07c9` | Pre-size TRRM module identity index |
| `e215945` | Pre-size SourceMap duplicate index |
| `45932c8` | Pre-size TRRM import and export indexes |
| `38d61bd` | Encode source-line CRLF markers in line starts |
| `0eb79b0` | Route streaming line reads through the `SourceText` facade |
| `1c07d66` | Reuse validated frontend tokens for the single-function fallback |
| `acc45de` | Add an invocation-scoped UTF-16 `SourceText` facade over the immutable host grant |
| `3b53fa2` | Transfer validated scalar frontend snapshots through `TokenCursor` before existing managed parsing |
| `5e9816f` | Pre-size module class identity index |
| `909b4e8` | Pre-size semantic module read indexes |
| `d05d654` | Pre-size linked module name index |
| `f22939d` | Pre-size producer contract indexes |
| `d60a6d7` | Reuse empty consumer import map |
| `b5eefaa` | Pre-size graph declaration tracking |
| `1c48c8a` | Check graph declaration totals before allocation |
| `d253fbe` | Pre-size native external function names |
| `b679039` | Pre-size graph cleanup owner index |
| `f5cf507` | Check graph producer dependencies directly |
| `d620203` | Scan graph symbols by index |
| `8a74581` | Size graph import tracking from declarations |
| `0b03510` | Scan graph symbols without LINQ |
| `f10ab45` | Validate IAT entries within `.idata` directly |
| `84e472d` | Sort graph catalog selections directly |
| `d48f00f` | Create checked-unwind cleanup indexes lazily |
| `6c263d4` | Check imported type members directly |
| `3be7f3c` | Skip unused graph import tracking sets |
| `6bfcde8` | Reuse validated PE sections for unwind checks |
| `d181d9d` | Pre-size graph catalog indexes |
| `d8376c7` | Pre-size imported structure membership |
| `f9152e9` | Pre-size shared structure indexes |
| `18d45ea` | Validate native set bindings without a projection |
| `e034a34` | Check native declaration membership without a temporary set |
| `219820f` | Check native call imports directly |
| `02a0652` | Materialize native names directly |
| `7a0e3bc` | Pre-size graph imported-type builder |
| `48d55b0` | Skip unused imported cleanup indexes |
| `aada8d9` | Skip empty imported graph type indexes |
| `6e754ca` | Pre-size semantic module-set imports |
| `2118a7d` | Check set machine imports directly |
| `ee241f2` | Pre-size graph declaration matching |
| `dac23be` | Pre-size lazy graph ownership sets |
| `e2993c0` | Pre-size graph import name validation |
| `83cba8f` | Pre-size graph traversal and execution state |
| `147f7c8` | Pre-size module-set identity validation |
| `6e8923d` | Pre-size graph provider identity validation |
| `9edbfdb` | Bound graph usage grouping capacity |
| `b460340` | Bound module-set import grouping capacity |
| `691a1af` | Build persisted graph provenance indexes directly |
| `77c4ec4` | Build graph origin indexes directly |
| `23a772d` | Build provider origin maps directly |
| `f9de8fb` | Materialize graph closure directly |
| `f387239` | Materialize graph info dependencies directly |
| `c2c658b` | Sort graph exports in an exact array |
| `4e71260` | Pre-size module-set native bindings |
| `c78618a` | Build graph cleanup indexes directly |
| `6073495` | Validate module-set exports directly |
| `feca5db` | Read module-set providers directly into arrays |
| `4aed52a` | Build graph ownership indexes directly |
| `f6e66d6` | Build leaf graph exports directly |
| `7d11b76` | Read graph providers directly into arrays |
| `975dd98` | Build module-set import names directly |
| `38a9cf7` | Materialize graph type origins directly |
| `ee7602b` | Materialize module-set reference arrays directly |
| `38bad38` | Avoid empty module-set import dictionaries |
| `fa10ee2` | Build graph native bindings directly |
| `4244e6a` | Materialize module-set imports directly |
| `9cf5819` | Materialize graph bindings directly |
| `1df0976` | Avoid empty graph import dictionaries |
| `13f72dd` | Build graph binding catalogs with direct iteration |
| `61b2096` | Materialize graph reference provenance arrays directly |
| `aa3d5c7` | Skip graph type provenance for typeless modules |
| `0bfe311` | Sort graph origin records in place |
| `41fabd1` | Reuse the empty graph dependency import array |
| `55594d8` | Validate graph provider arrays without LINQ materialization |
| `c2c1bdb` | Skip provider type indexing for typeless consumers |
| `e24bcdf` | Build consumer call indexes on demand |
| `1f26779` | Avoid unused graph producer dependency indexes |
| `93c02b8` | Pre-size module-set reference merge collections |
| `74b1a26` | Pre-size graph reference function materialization |
| `be4ea09` | Build module-set references without LINQ materialization |
| `073a2b0` | Cover no-import module-set provider validation |
| `1046881` | Share direct provider import declaration materialization |
| `c6ed7e9` | Build consumer provider indexes only when imports exist |
| `0bbda4e` | Materialize cached consumer declarations directly |
| `3d09cc0` | Build consumer module-set declarations directly |
| `72b53a0` | Skip unused graph dependency function indexes |
| `9de26e0` | Build module-set restoration inputs directly |
| `dbd07c5` | Materialize provider declarations and references directly |
| `5516431` | Build native consumer call indexes directly |
| `91672b2` | Build native producer validation inputs directly |
| `7a8a7f3` | Materialize native producer declarations directly |
| `9f30b3c` | Skip empty graph validation lookups |
| `c2343ea` | Skip empty graph dependency indexes |
| `4825748` | Validate graph exports without indexing every function name |
| `e8754a7` | Remap graph classes through an exact-capacity builder |
| `41cb712` | Materialize graph module functions without LINQ |
| `9b4b317` | Build graph function indexes directly |
| `02d8f0c` | Materialize verified graph functions directly |
| `7840941` | Pre-size and directly remap graph export declarations |
| `bd074c4` | Limit graph export collision tracking to function names |
| `cd90d39` | Check graph caller-unsafe contracts without LINQ |
| `36f71f6` | Check graph dependency type membership without LINQ |
| `ea0c364` | Pre-size native producer export-address tracking |
| `71a9af9` | Validate TRRM zero-filled gaps with span search |
| `4d7ed3f` | Skip exceptional-cleanup tracking for fail-fast images |
| `21b3027` | Skip export-address set construction for native consumers |
| `c149c87` | Validate imported symbols without per-import lists |
| `24ba4b8` | Validate native provider imports without a temporary dictionary |
| `15b75ba` | Validate exceptional cleanup targets without LINQ |
| `94e80e0` | Sort fallback native symbol names in one array |
| `a826758` | Check checked-exception handler import without sequence allocation |
| `f6e5449` | Sort multiple native image providers in a pre-sized array |
| `993c7a0` | Pre-size native image import validation collections |
| `6c1c458` | Avoid sorting zero- and one-provider import lists |
| `28996b4` | Skip sorting canonical native import lists |
| `6a37644` | Skip extent sorting for ordered TRRM section payloads |
| `2e0b2e1` | Skip sorting canonical native image export lists |
| `f7c83a4` | Build sorted graph import snapshots without LINQ materialization |
| `e9942cb` | Reuse graph import lookups during managed-boundary validation |
| `db76eb0` | Reuse cleanup structure traversal state |
| `71ac1b0` | Reuse cleanup dependency function buffer |
| `9028f1e` | Sort graph providers in place after validation |
| `34fa8d1` | Skip JIT range copies and sorting for ordered emitter output |
| `8861bb2` | Validate exported layouts without LINQ |
| `d78209a` | Skip empty trivia builders during scanner adaptation |
| `000224b` | Carry decoded comment width through scanner advancement |
| `8f62ded` | Build native function metadata in exact-capacity arrays |
| `cf7a5e7` | Avoid repeated scalar checks in exported value layouts |
| `c1fd39f` | Group PE imports before sorting DLL groups |
| `2e467e1` | Reuse the contract index while loading executable TRRM |
| `0aa9ae2` | Allocate token-span backing sets only when populated |
| `5767080` | Allocate token-span dependency edges only when populated |
| `f1ac891` | Seed token-span dependency facts without LINQ snapshots |
| `e8f7274` | Validate GenericParam ownership without owner map |
| `05c0f85` | Check method ownership against current module range |
| `19151a0` | Validate FieldDef rows in a single pass |
| `22ad919` | Validate TypeArg coverage without owner map |
| `2f02671` | Build PE import targets with IAT offsets |
| `fb87f8f` | Write PE names without temporary byte arrays |
| `3803df5` | Reuse PE unwind record sizes |
| `f3fc260` | Skip redundant PE import sort key |
| `c6953c7` | Reuse managed-node detection during emission |
| `4550f00` | Skip JIT snapshots when only returning exit code |
| `1045730` | Exclude runtime metadata from JIT snapshots |
| `ab7214d` | Pre-size compilation snapshot collection |
| `f72d204` | Index repeated CLI diagnostic source lookups |
| `0f99f3d` | Materialize module declarations as arrays |
| `0b23da5` | Validate image sections by index |
| `f1cbc39` | Copy indexed scalar types directly |
| `be9c62d` | Reuse graph JIT provider snapshots |
| `7963de0` | Copy native set dependencies directly |
| `fe699ec` | Index unwind sections by validated order |
| `c525acc` | Validate empty graph dependencies in one pass |
| `7ef2ac7` | Sort native imports in place before writing |
| `10c7696` | Encode native package identities into destinations |
| `d9901c9` | Preallocate staged package path lists |
| `736c44d` | Remove repeated module-set import validation |
| `199b23d` | Sort validated module set providers in place |
| `a42e57f` | Write graph strings without temporary arrays |
| `36eedc8` | Populate graph JIT package indexes together |
| `cdc3dff` | Skip empty graph type-origin indexes |
| `2b1518e` | Build native consumer bindings and names together |
| `440303c` | Avoid temporary reads in indexed optional equality checks |
| `1a1296e` | Reuse indexed TRRM value descriptors across block prefixes |
| `fd107f0` | Skip redundant native buffer structure scan |
| `28969c9` | Skip native buffer-clear scan when buffer types are absent |
| `92035f7` | Remove obsolete managed output slot rescanner |
| `f679574` | Reuse native machine import membership set |
| `48fe445` | Collect managed class offsets in one pass |
| `f625d17` | Count managed output slots incrementally |
| `ae9c961` | Collect module-set machine imports during parsing |
| `84eb86b` | Binary-search graph declaration ownership |
| `92fbf02` | Fuse imported layout validation with indexing |
| `d80faf9` | Fuse managed-node facts into verifier validation |
| `ac09000` | Skip producer type scans without cleanup types |
| `591c1e6` | Skip producer cleanup discovery without cleanup types |
| `5b98063` | Build cleanup function indexes on demand |
| `27fb846` | Filter cleanup structures during producer closure |
| `7313cd4` | Reuse structure indexes during producer closure |
| `9682007` | Reuse nested managed export shape checks |
| `d20620e` | Reuse class indexes when binding factories |
| `f68327a` | Index native PE import groups and symbols |
| `3add77d` | Index JIT import providers during validation |
| `5c4288d` | Reuse public layout checks across library exports |
| `f8ed4aa` | Index struct visibility for library exports |
| `794ee9f` | Index functions during library export selection |
| `f549b2f` | Fuse cleanup detection into spill sizing |
| `76f170b` | Reuse validated source-map slot selectors |
| `226831c` | Reuse field names when validating getters |
| `97e647d` | Skip compact body scans when no bodies exist |
| `02bd15c` | Reuse generic parameter indexes during constraint binding |
| `1c78294` | Reuse completed struct alignments during layout |
| `e29a0d8` | Reuse the declaring struct name across conformance checks |
| `39ba720` | Collect protocol operator operands without array snapshots |
| `0884680` | Capture static protocol names during interface classification |
| `2a32680` | Recognize one syntax argument without array snapshots |
| `925f434` | Match protocol arguments without LINQ enumeration |
| `7b63d6a` | Scan protocol application tokens without an array snapshot |
| `abdcf32` | Reuse struct visibility during layout |
| `426707d` | Validate buffer-view arguments without array snapshots |
| `7f4b7b8` | Preserve cached class visibility during layout |
| `d2d1a66` | Classify generic declaration effects with signature facts |
| `14dbab9` | Reuse recorded type visibility when validating aliases |
| `1818ad3` | Classify static protocol signature nodes during the member scan |
| `8080dc7` | Reuse qualified struct names during layout |
| `035f281` | Reuse static protocol argument names |
| `6da62ac` | Reuse generic declaration dispatch result |
| `e282982` | Check generic type parameter facts together |
| `871405e` | Collect alias parameters and detect duplicates together |
| `dfa2266` | Classify alias declaration facts in one syntax pass |
| `e365577` | Reuse managed-class mutability during declaration validation |
| `bf28c0b` | Collect generic signature nodes in one pass |
| `3c6e58f` | Scan static protocol formals once without intermediate arrays |
| `45e7514` | Reuse generic declaration attribute and modifier facts |
| `f629661` | Classify static protocol declaration facts in one syntax pass |
| `2ab20d9` | Classify static protocol member facts in one syntax pass |
| `b073691` | Reuse static protocol NoThrow attribute count |
| `a030840` | Reuse one immutable source snapshot array throughout compilation |
| `76c3791` | Reuse verified consumer functions for module-set type closure |
| `f32c374` | Reuse validated module function facts when classifying owned-buffer profiles |
| `37e16ea` | Collect persisted ownership-transfer profile facts during module feature validation |
| `a5b618d` | Collect owned-buffer and buffer-view profile facts during managed-feature traversal |
| `8653b14` | Aggregate native kernel import policy facts during symbol validation |
| `3c6f15a` | Check native export owners in one structure pass |
| `be2c547` | Reuse module call traversals for buffer-clear reachability |
| `6392c7c` | Validate structure cleanup owners through one name index |
| `cf26d88` | Count class construction pairs during function validation |
| `3932fb2` | Inspect optional-handle and managed-node profile features together |
| `fd4a240` | Collect unique and composite module profile facts in one pass |
| `50290f2` | Inspect persisted readonly/mutable view features in one body pass |
| `c99350e` | Cache native optional-assignment owner destinations |
| `8865e0c` | Classify optional managed and node profiles in fewer function passes |
| `c38db3a` | Reuse resolved owners for property field collision checks |
| `e8036c8` | Resolve semantic owners through a lazy ordinal index |
| `a3c15f4` | Inspect token-span metadata and function bodies in one pass |
| `d457228` | Validate consumed generic closers and punctuation spans in one pass |
| `90a1a9b` | Detect owned buffers and buffer views in one function traversal |
| `2b49dd9` | Detect unique and composite profile features in one structure traversal |
| `7325427` | Count managed class construction pairs in one function pass |
| `d4e6f30` | Detect native buffer views in one profile scan |
| `c4fe7ac` | Remap graph and module-set structures without intermediate immutable snapshots |
| `192d0ee` | Avoid a temporary imported-type array during module-set validation |
| `88d6667` | Reuse imported-type lookup sets across module and graph validation |
| `4b4b274` | Reuse validated managed-import results during consumer restoration |
| `8ff5a6f` | Reuse the validated producer structure index during linking |
| `30c989c` | Resolve module owner visibility and export layout in one pass |
| `88b92f3` | Resolve declared structure dependencies with one lookup pass |
| `9c0204b` | Reuse imported ownership-transfer facts during graph library creation |
| `b13036b` | Validate graph cleanup ownership during the structure traversal |
| `b10b0a8` | Combine graph origin and cleanup-owner construction into one structure pass |
| `31abf0a` | Reuse consumer declaration contracts and ownership facts during packaging |
| `6b672a3` | Detect exception imports during native JIT fixup validation |
| `e08248d` | Resolve each native JIT module export once per setup |
| `f5fbbf2` | Binary-search validated provider exports when checking native graph JIT imports |
| `de1c4d7` | Build provider export sets without temporary arrays during module-set validation |
| `4e4b6f2` | Reuse parsed contract snapshots during semantic linking |
| `bad9a04` | Reuse validated leaf profiles during graph artifact inspection |
| `61f0576` | Reuse provider contract snapshots in native package validation |
| `7e1c4e8` | Reuse validated provider profiles during native image checks |
| `0424a30` | Reuse consumer contract snapshots in native package validation |
| `c142b4f` | Reuse parsed graph modules during closure validation |
| `53ffd7f` | Reuse the validated graph profile during artifact inspection |
| `ca1d3cb` | Reuse graph contract snapshots through creation and closure validation |
| `ae0558c` | Reuse parsed semantic contract facts when validating graph packages |
| `99fb9c1` | Avoid repeating consumer contract validation after validated serialization |
| `8741761` | Avoid validating each native-set provider image twice |
| `a65e2ac` | Reuse private managed-state analysis while reading modules |
| `9c2448d` | Reuse private managed-state analysis during producer validation |
| `f6103f8` | Reuse private managed-state analysis across graph library validation |
| `77ac7d3` | Remove a redundant module-set import declaration membership check |
| `56df5fa` | Reuse validated native package checksums for catalog duplicate filtering |
| `f8d3914` | Validate native graph and module-set checksums into stack storage |
| `33fa5b5` | Aggregate sparse graph type origins only when repeated provider probes outweigh a bounded entry pass |
| `7a53bb9` | Index native module-set machine imports once when validating semantic call coverage |
| `3149b1b` | Group native module-set machine imports by DLL before serialization |
| `6da56e9` | Group emitted graph imports by dependency before validating ownership |
| `3268fb5` | Populate indexed TRRM value remaps, types and owners in one pass |
| `dd5e072` | Reuse the validated graph dependency-function map during contract reconstruction |
| `eb3f0a8` | Reuse the strict UTF-8 codec across native graph and module-set package records |
| `041b780` | Reuse the strict UTF-8 codec across semantic module readers and string records |
| `055386d` | Compare parsed graph and module-set declarations against already-built import sets |
| `3425e7f` | Use sorted declarations directly for native graph machine-import ownership checks |
| `12ad2ac` | Preserve sorted provider export order when forming graph declarations |
| `534f921` | Write native graph and module-set checksums without an intermediate payload copy |
| `ef83b3c` | Replace indexed TRRM per-use tree updates with dictionary updates and final key sorting |
| `ae4ceea` | Cache managed-state detection outside per-function module-set boundary checks |
| `3bb7cc7` | Cache graph managed-state checks outside per-function boundary validation |
| `bb928ac` | Remove redundant graph dependency identity set after ordinal-order validation |
| `5c565b4` | Index structure type remapping across graph and module-set references |
| `51ce7d0` | Index graph package dependency and type-origin owner lookups |
| `9392340` | Fingerprint duplicate graph catalog candidates before byte comparison |
| `a548580` | Reduce repeated graph-origin scans and per-type allocations |
| `6239fcc` | Index graph cleanup validation lookups to avoid repeated function scans |
| `feccab4` | Reuse a decoded graph contract during export validation |
| `b33fde4` | Index graph type and import lookups to avoid repeated linear scans |
| `f9f7729` | Exercise non-Boolean/void Copy outputs through managed native graph exports |
| `3f7f4ed` | Cover compatible native provider replacement with reordered type declarations |
| `bc0fe0a` | Support hosted non-Boolean/void Copy-struct `out` outputs through semantic and native modules |
| `7183c25` | Provisionally persist top-level caller-unsafe contracts and explicit unsafe-block call scopes in semantic modules |
| `db6fdb5` | Implement a bounded readonly `TokenCursor` over validated snapshots without general stored-view support |
| `ba2e36e` | Project named scalar `out`/`init ref<T>` arguments through bounded v85 calls |
| `f93fbfd` | Preserve source-order evaluation and formal binding for named calls in indexed TRRM |
| `d9a94f0` | Project named by-value calls with source-order evaluation through the bounded scalar frontend |
| `6921e19` | Support hosted named by-value calls with source-order evaluation and persisted formal-parameter mapping |
| `fa82131` | Project source-audited unsafe expression scopes and caller-unsafe self-calls through single-function v82/v83 |
| `fefd3e4` | Project caller-unsafe call scopes from explicit `unsafe(expr)` expressions through complete-program v84 |
| `e6277c7` | Project caller-unsafe callable obligations through bounded complete-program v83 while keeping verification hosted |
| `2562118` | Propagate caller-unsafe static operator requirements through generic proof bodies |
| `03ef008` | Enforce same-compilation unsafe field access scopes while persisted writers fail closed |
| `f714926` | Add same-compilation caller-unsafe checks while persisted writers fail closed |
| `84d2564` | Project scalar non-Boolean/void `init ref<T>` completion through v81/v82 source-audited projections |
| `63699aa` | Support hosted non-Boolean/void `init ref<T>` completion without widening scalar projections |
| `63016a8` | Project named-storage `is init` tests in bounded single-function v80 and complete-program v81 scalar frontend |
| `7d8f117` | Support named-storage `is init` tests in hosted native-20 and semantic v49 |
| `8681e1f` | Support vacant-safe optional-handle `is var` and expression-capable `is var?` patterns in hosted native-20 and semantic v48 |
| `a519f22` | Accept the existing initialized/non-null optional-handle presence pattern as `is var item` |
| `db573eb` | Support non-Boolean scalar `out var` recursive self-calls in single-function projection 79 |
| `a74593b` | Support non-Boolean/void scalar `out var` calls in complete-program projection 80 |
| `15c4f0e` | Flatten semantic-module revision 47 serialization and remove obsolete reader branches |
| `2717561` | Retire obsolete semantic-module revisions; revision 47 only |
| `d919c6a` | Native scalar `init ref<T>` parameters and recursive self-call destinations, single-function projection 79 |
| `40664fb` | Native scalar `init ref<T>` parameter modes and call destinations in multi-function snapshots, projection 78 |
| `e0cf111` | Native scalar `out` parameter modes and call destinations in multi-function snapshots, projection 77 |
| `4c17397` | Native scalar multi-function snapshots with `[DoesNotReturn]` contracts and call continuation, projection 76 |
| `5e30392` | Native scalar calls to other void functions as expression statements, projection 75 |
| `3e7fcf8` | Native scalar multi-function snapshots and source-order scalar call targets, projection 74 |
| `296658a` | Native scalar void direct self-call expression statements, projection 73 |
| `324c2d4` | Native scalar direct self-call expression statements, projection 72 |
| `7346396` | Native scalar direct self-calls with inferred `out var` destinations, projection 71 |
| `a1f5cb6` | Native scalar direct self-calls with existing `out` destinations, projection 70 |
| `3c7e65d` | Native scalar `out` parameter declarations with source-audited projection 69 and hosted definite-publication verification |
| `b4e477a` | Native scalar conditional const initializers with lazy branch evaluation, projection 64 |
| `daadff9` | Native scalar short-circuit static proof folding, projection 68 |
| `1f4635b` | Native scalar constant-conditional static proof folding, projection 68 |
| `cdc392c` | Native scalar foldable-true static proofs, projection 68 |
| `f55f904` | Native scalar checked bare catch with optional `finally`, projection 67 |
| `158999e` | Native scalar explicit unsafe scopes and checked bare catch, projection 66 |
| `264e7ee` | Native scalar nested `try`/`finally`, projection 65 |
| `e8f0465` | Native scalar foldable const initializers, projection 64 |
| `8bcd3ff` | Native scalar literal-initialized const locals, projection 63 |
| `7d162b6` | Native scalar inferred vacant locals, projection 62 |
| `088b732` | Native scalar explicit vacant locals, projection 61 |
| `4d7999c` | Native scalar local lifetime ending, projection 60; verifier feasibility memoization within the fixed work limit |
| `df788b8` | Hosted explicit local lifetime ending, checked cleanup/root and loan retirement, and semantic v47 graph persistence |
| `1c80cc9` | Hosted assignment initialization of definitely vacant locals, checked publication and semantic v46 |
| `e40d0b4` | Single same-line loop-label attachment, syntax-44 |
| `586aa4b` | Native parameterless NoThrow/DoesNotReturn function contracts, projection 59 |
| `27aaf17` | Native braced scalar/void `else if` chains, projection 58 |
| `e43ea1c` | Native void arrow requirements and scalar discards, projection 57 |
| `6952d2d` | Native braced void functions and null returns, projection 56 |
| `e1fe270` | Native scalar standalone runtime requirements, projection 55 |
| `3b10d51` | Native scalar labeled loops and named transfers, projection 54 |
| `cee8c31` | Native scalar non-void discard statements, projection 53 |
| `9e20137` | Non-nearest named loop transfers through indexed region exits |
| `1fc9e7e` | Native scalar standalone lexical blocks, projection 52 |
| `e06aee6` | Syntax 43: maximal ASCII punctuation runs and expected-linear indexed diagnostic audits |
| `d6e65d3` | Syntax 42: contextual punctuation boundaries |
| `2f29b3e` | Full-width scalar, indexed and String TRRM serialization |
| `6f0d088` | Syntax 41: symbolic operator and binding spacing |
| `fb49af6` | Syntax 40: role-specific dot attachment |
| `af21c1f` | Syntax 39: attached constructs |
| `4e8fd9b` | Syntax 38: keyword escapes |
| `f2dc5c1` | Syntax 37: generalized comment fences |
| `8113012` | Indexed TRRM `do`/`for` loops |
| `85e1743` | Native scalar `do`/`for` projection 51 |
| `7c3c857` | Indexed lexical locals |
| `9051689` | Native scoped-local projection 50 |
| `3115e5a` | Module-local TRRM scalar calls |
| `e2267db` | Indexed shifts and mutations |
| `37576ad` | Native shifts and mutations, projection 49 |
| `a2083b5` | Indexed branches and loops |
| `0a5200e` | Initial persisted scalar TRRM execution |

Earlier compiler, runtime, module and component milestones are summarized in
the implementation reference rather than repeated here as a second feature
inventory.

## Language, semantic IL and metadata specification

Maintain one evolving language specification organized around syntax, types,
static semantics and execution. Distinguish intended rules, implementation
coverage and unresolved proposals; do not version chapters independently.
Breaking changes are expected before v1. Prior alpha formats are not
compatibility targets and should not be preserved merely because they were
implemented; retain only selected versions as comparison/validation fixtures,
and remove or reshape the rest as the current contracts advance. Migration
semantics apply only where the accepted language contract explicitly requires
them. Roadmap progress is toward the approved metadata design's v1.0; do not
version each internal increment. If a preview is shipped, its versioning is an
explicit part of that release decision. No pre-v1 stability promise is made.
Language major/minor versions define language evolution and its compatibility
story. Internal projection `v#` tags identify the current implementation
layout; they are not language versions or implicit compatibility promises.
Compatibility is an explicit design choice with a deliberately scoped version
boundary, never an automatic consequence of retaining an old reader.
Work may proceed against approved grammar while remaining decisions stay in
the proposal queue; using a provisional design for an increment does not
approve other proposals.

TRRM is a compact binary representation of the language and ecosystem: an
input for metadata binding, reflection, tooling, linking and AOT/JIT code
generation without source recompilation. It is not merely private-IR
serialization or a verifier certificate. The approved source grammar and
current `(1, 0)` metadata and coded-instruction designs specify target
contracts, not a complete executable profile, current compiler support or a
released schema. The complete Body/verifier contract, feature negotiation and
resource budgets remain work. Source compilation checks authored bodies;
binary consumers validate structure, references, binding, compatibility and
caller use, then may rely on declared contracts for external dependencies
without re-proving their bodies. Malformed data is never trusted.

Pre-v1 changes, including breaking format changes, remain possible; no frozen
versioning scheme, published managed-machine ABI, or pre-v1 stability level is
promised. Do not preserve prior formats or require migrations solely for
backward compatibility; keep at most a small selected set for independent
comparison and validation, then retire obsolete shapes. Validate independently
authored readers, writers and verifier cases, not only same-version round
trips. Dynamically composed code must be checked against dependency, compiler
and runtime assumptions before execution; an implementation may adapt,
rebuild, discard or reject it. No JIT fallback or IR retention is required.
Declared FFI exports remain fixed to their target profile.

Keep minimal language conformance separate from the application-library and
runtime surface. Core requirements stay small; richer features retain their
checked dependencies without silent fallback. The library proposal is not an
approved inventory, and no universal runtime, backend or API set is implied.
The source specification, verifier, metadata/module interchange and target
profiles must state their boundaries explicitly. Do not infer implementation
from an approved syntax form or infer approval from an implementation choice.

## Compiler and self-hosting gate

Maintain the hosted C# compiler and Terrum-authored compiler in parallel
through at least 1.0 GA; the first self-build does not replace the host.
Compare both on the same source, dependencies, options, runtime policy and
target profile: lexical/diagnostic facts, canonical semantic artifacts, then
native output for their shared subset. The gate is a hosted build, a rebuild
with that compiler, and a further rebuild plus native execution against shared
fixtures. Require byte equality only for output whose ordering, layout and
tie-breaking are specified; explain other differences rather than normalizing
them away. Current scanner/library components are precursors, not evidence that
the gate passes. The hosted compiler supports a compilation-local readonly
`TokenCursor` over a validated `TokenSnapshot`, and the Terrum-authored scalar
frontend now consumes that snapshot through bounded indexed parser windows.
This does not add general stored views or move the frontend's independently
audited result beyond the hosted verifier/emitter boundary.

Do not translate every design sample at once or build a plugin loader first.
Extend the verified executable corpus and semantic checks in coherent slices;
keep diagnostics and source mappings useful on incomplete input. Bounded
managed access, unique-owner mutation and field-view contracts are implemented;
the bounded owned-`int32` buffer/view path preserves natural element alignment.
General `out` to `init ref<T>` migration remains scheduled. The existing
conditional-init subset is not general reference support. Bounded static
exports do not imply `func`, general `fnptr`, tuples, callable witnesses or
contextual lambdas. Fixed-signature callables precede generic scalar/SIMD
functors, which precede contextual generic lambdas; type packs, polymorphic
erasure and arbitrary callback vectorization are excluded from these stages.

## Memory-model implementation sequence

The [memory model](memory-model.md) is accepted design, not implemented
concurrency support. Integrate it through the four roadmap areas above, while
preserving existing single-threaded behavior.

| Order | Increment and prerequisite | Required evidence |
|---|---|---|
| 1 | Preserve observable evaluation, checked arithmetic, cleanup, ordinary access widths and current unsafe-recovery behavior. | Source/IR/AOT/JIT regressions show optimization adds no invalid access or observable fault/effect. Unsafe discovery must not invent repair or trap behavior; caller-unsafe declarations and explicit scopes express call-site obligations, not memory-safety proofs or persisted metadata. |
| 2 | Implement caller obligations for `unsafe` callable declarations, explicit unsafe-block requirements, and producer-owned transfer/share capabilities with reference/loan/parameter metadata and separate compilation. Ordinary safe declarations may contain checked internal unsafe blocks; no trusted-implementation marker is required. | Wrappers/imports cannot erase obligations; unsafe operations remain scoped; generic bodies use published constraints; private layout/JIT facts do not change source binding; incompatible capability/safety changes reject without freezing opaque layouts. |
| 3 | Add bounded `MemoryPrimitives` value access and scalar atomics after typed access paths, representation eligibility and compile-time order binding; begin with supported Windows x64 profiles. | Check atomicity, ordering and progress separately; define bitwise CAS comparison; checked overflow leaves storage unchanged; unaligned access is not a snapshot; prove target capability and AOT/JIT/module parity; no hidden lock fallback. |
| 4 | Build checked handoff, immutable publication and scoped synchronization on those primitives within resources/graphs and rich libraries; allow an explicitly selected coordinated fallback where feasible. | Test publication and mixed-order litmus cases; enforce guards, lifetimes and aliases; justify lock-free and wait-free claims separately; share fallback protocols across modules; preserve effects while allowing valid read elision. |
| 5 | Extend reference atomics only after collector-aware roots, barriers, safepoints and reclamation are defined. Add explicit SIMD, non-temporal and cache operations only with layout, feature and memory-domain contracts. | Establish concurrent root safety before reference APIs; preserve ranges and per-location guarantees through SIMD; complete non-temporal stores before publication; do not confuse cache maintenance with DMA coherence or persistence. |

Before concurrent operations, specify mixed-order/fence rules and executable
litmus cases. Preserve the accepted explicit acquire-release relay rule, not
implicit relaxed-RMW release sequences. Exact declaration spellings,
conditional-capability metadata, CAS order-pair validation and target tables
remain specification work. This sequence does not authorize new backends,
public managed-handle ABI, or wholesale adoption of design-only libraries.

Represent access, ordering and lifetime effects in checked IR before lowering.
Persist and validate those contracts across semantic imports, native producers,
module sets and graphs, with feature-based version selection and rejection
under incompatible headers. Existing ordinary IR and lexical unsafe recovery
do not already encode those guarantees.
