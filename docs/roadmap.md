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
`NoThrow` and `DoesNotReturn` Effects in its bounded Body subset.
Checked-exception Bodies and other Effects/contracts remain unsupported. See
[implementation](implementation.md) for the current support boundary.

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

Projection v85 carries named by-value and scalar `out`/`init ref<T>`
arguments through bounded direct scalar calls. Arguments remain source-order
children while the independent reader verifies their formal-parameter map
against source names. Single-function v85 covers self-calls; complete-program
v85 covers direct self/global calls. Overload selection and the hosted
verifier/emitter boundary remain unchanged.

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

Separately, semantic-module revision 54 provisionally persists top-level
caller-unsafe contracts and calls enclosed by explicit `unsafe { }` blocks,
reconstructing authorization from that scope. Unsafe members and executable
TRRM caller-unsafe calls remain unsupported; the broader reference-call
metadata proposal remains unapproved.

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
   semantic-module revision 54 provisionally carries top-level caller-unsafe
   obligations through explicit unsafe-block scopes; this bounded
   implementation does not settle the general schema or approve the
   [reference-call metadata proposal](proposals/reference-call-metadata.md),
   which recommends a narrow first dependency registry and reuse of the
   assigned caller-unsafe effect bit.
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
    [self-host gate](#compiler-and-self-hosting-gate).

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
the gate passes. The hosted compiler now supports a compilation-local readonly
`TokenCursor` over a validated `TokenSnapshot`; this does not add general
stored views or extend the Terrum scalar frontend beyond its hosted
verifier/emitter.

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
