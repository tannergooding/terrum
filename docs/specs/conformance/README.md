# Specification conformance cases

These chapters give required outcomes for the cited contracts. They are not
implementation support inventories or a claim that a compiler has passed them.
Each case applies only with its stated declarations and admitted profile.

Cases apply the cited accepted rules and, for encoded cases, the exact
selected schema.
[Proposed amendments](../../proposals/README.md) do not silently change that
baseline. The [pre-v1 status](../overview/conformance.md#scope) permits reviewed
replacement of unpublished rules and assignments; this corpus does not
require their preservation in a later draft.

| Chapter | Coverage |
|---|---|
| [Source input](source-input.md) | Strict UTF-8 boundary vectors, invalid-byte extents, signatures/preservation, UTF-16 coordinates, and already-decoded API distinctions |
| [Source vocabulary](source-vocabulary.md) | Reserved/contextual words and exact escapes, dedicated typeof/type-identity evidence, and witness/provider/conversion-owner via roles |
| [Literal construction](source-literal-construction.md) | Target ownership/signatures, kind-first selection, contiguous signs, integer bits/ranges, floating rounding/cohorts, decoded text and required execution |
| [Builder type recipes](source-builder-type-recipes.md) | Resolved type-reference schemas, target authority/formals, ordinary substitution, conflicts and independent construction admission |
| [Consuming builder completion](source-builder-completion.md) | Exact nominal role, by-value transfer, initialized/vacant results, owned cleanup, backing and explicit reusable adapters |
| [Parameterless builder acquisition](source-builder-acquisition.md) | Exact factory signature, eager capture order, builder output/cleanup and explicit runtime-context boundaries |
| [Standard construction operations](source-construction-operations.md) | Nominal text/collection roles, Unicode-scalar alignment, once-only typed formatting, item/result identity and admitted spread boundaries |
| [Value iteration](source-value-iteration.md) | Nominal factory/direct sources, single-output publication, ownership/backing, loop close and eager spread capture/abandonment |
| [Async value iteration](source-async-value-iteration.md) | Synchronous async-cursor acquisition, pending reservations, terminal/observed publication, exact output correlations, forwarding and awaited close |
| [Lending iteration](source-lending-iteration.md) | Readonly/writable sync/async roles, element aliases, reference publication, backing/invalidation refinements, reservations and close/escape |
| [Async execution](source-async-execution.md) | Eager entry, establishment/failure channels, inline/suspended completion, cleanup, effects and responsible observation |
| [Task responsibility](source-task-responsibility.md) | Affine owner identity/transfer, consuming await, payload/output/terminal observation, every-exit settlement, cancellation and admission |
| [Ownership and cleanup](source-ownership-and-cleanup.md) | Construction/acquisition, structural partial moves/repair, complete Cleanup receivers, replacement, exact retirement order and retained dependencies |
| [Local failures](source-local-failures.md) | Nominal Exception schema/categories, exact complete subjects, explicit reference/Copy bindings, fresh versus active propagation, selection and admission |
| [Requirement failures](source-requirement-failures.md) | Runtime selectors, callable/default/Core identities, ordinary zero-input construction, operand versus constructor failure, output states, phases and admission |
| [Captured exceptions](source-captured-exceptions.md) | Opaque affine Core identity, lexical creation, handler transfer/borrow states, original-event rethrow, causes, retirement/phases and independent admission |
| [Thread capabilities and sharing](source-sharing-and-handoff.md) | Explicit/local-conditional eligibility, alias and return authority, publication, guard/scalar protocols, captured duties and unsafe/trust boundaries |
| [Concurrent executions](runtime-concurrent-executions.md) | Exact access/source generations, coherence, matched fence communication, required access order, mixed SC/full boundaries, failed attempts and grounded causality |
| [Generated producers](source-generated-producers.md) | Owned nominal schemas, generic/capture mapping, lazy body/yield, creation/operation clauses, close/failure and async responsibility |
| [Dependencies and views](source-dependencies-and-views.md) | Source/target recipes, broad and backing-only relationships, retained holders and phases, copies/versions, witness conditions and representation admission |
| [TextTarget parameters](source-text-targets.md) | Resolved parameter placement, eager typed content, module-local closed-shape identity, forward inference, ownership/retention and independent caller contracts |
| [Eager construction](source-eager-construction.md) | Source-order capture, after-capture acquisition, drain-only formatting/iteration, ordinary require refusal, ownership/cleanup and incremental-admission boundaries |
| [References and grammar](source-references.md) | Descriptor access, reference signatures, source forms, and metadata fragments |
| [Implicit-reference materialization](source-materialization.md) | Mutable vacant locals, reusable temporaries, scope and transfer rules, dependency failures, and owner traces |
| [Local declaration spaces](source-local-declarations.md) | Name collisions, points of declaration, loop/catch scopes, and lookup versus availability |
| [Source name binding](source-name-binding.md) | Namespace/member collisions, type arity, generic formal names, imported groups, accessibility, and type-call precedence |
| [Initialization and pattern flow](source-initialization-flow.md) | State joins, mutable and negative patterns, retained matches, phase scopes, outcome invalidation, and initialization-loan projections |
| [Values and ordinary execution](source-values-and-execution.md) | First-class unit, explicit result handling, expression categories, loops, and return completion |
| [Interface witnesses](source-interface-witnesses.md) | Nominal and inherited identity, associated bindings, checked qualified-self projection, and publication |
| [Complete types](source-types-and-normalization.md) | Finite substitution, qualifier/representation identity, actual-write constraints, requested-access boxing, optional normalization, and tuple/callable signatures |
| [Constant generics](source-constant-generics.md) | Closed domains, checked Transparent primitive wrappers, nominal/carrier value identity, validity/construction, forwarding/defaults and independent compatibility |
| [Registered queries](source-registered-queries.md) | Exact Core authority/signatures, complete value subjects, runtime/constant target evidence, fixed/coupled retention and replacement |
| [Calls and inference](source-calls-and-inference.md) | Argument mapping, finite type/constant and contextual evidence, quality/specificity, hidden-output scopes, ownership-safe extraction and exact retirement traces |
| [Built-in scalar operators](source-primitive-operators.md) | All scalar signatures, checked integer/count edges, strict floating formats/remainder, comparisons, and place/accessor update timing |
| [Absolute-value members](source-absolute-value.md) | Same-type numeric identity, exhaustive small integers, signed/native minima, floating classes, call/result states and effect/variant admission |
| [Numeric selection members](source-numeric-selection.md) | Exact Min/Max/Clamp members, integer/floating selection, signed zeros/cohorts, invalid-bound payload, call/event/state/phase and admission |
| [Floating environment](source-floating-environment.md) | Sign-only representations, quiet processing-result NaNs, canonical admitted constant recipes and no ambient status |
| [Numeric capabilities](source-numeric-capabilities.md) | Exact groups, self/Copy/member contracts, all primitive witnesses and independent generic evidence |
| [Library inventory and scenarios](source-library-contracts.md) | Selected versus candidate roles, exact public declarations, complete admission closure and provider/consumer responsibility |
| [Standard runtime categories](runtime-standard-failures.md) | Postcondition/index/recoverable-allocation mappings, initializer state and original versus fresh responsible events |
| [Scalar checked failures](source-scalar-failures.md) | Exact nominal zero-data Core types, arithmetic/conversion/count failure mapping, event/cause ownership, result/effect preservation and admission |
| [Proof and constant execution](source-proof-and-constant-evaluation.md) | Scalar facts/branches, safety and invalidation, constant path/dependency eligibility, deterministic NaNs and exact resource boundaries |
| [Published predicates and pure evaluation](source-published-predicates-and-effects.md) | Input-state purity, modes/authorization, checked reads/effects, NoAlias parameters, initialization results, predicate phases and compatibility |
| [IL unit values](il-unit-values.md) | Zero-data typed IDs, logical unit arguments, constant encoding, calls, equality, and state |
| [Unified IL registry](il-registry.md) | One catalog, allocation/role/shape consistency, scalar/floating admission, complete artifacts and opcode-aware rewrites |
| [Format versioning](il-format-versioning.md) | Stable exact released-schema pairs, incompatible same-pair drafts, explicit selection and unchanged framing |
| [Interchange requirements](il-requirements-and-admission.md) | Artifact schema, module-owned rules/features, exact support, retained closure, admission phases and packing |
| [Module semantic manifests](il-semantic-manifests.md) | Required governing editions, exact capability lists, derived closure, actual framing and finite packing/schema rewrites |
| [Binding and replacement](il-binding-and-replacement.md) | Selected identities, exact types/modes/states, directed guarantees, complete comparison contexts and retained replacement dependencies |
| [Contract clauses](il-contract-clauses.md) | Assigned extents/owners/keys, explicit zero-mask equivalence, output/cleanup subjects, no-return signatures and reserved-use gates |
| [Reader trust](il-reader-trust.md) | Trusted selective access, natural decoding/ABI/authorization checks, scoped audits and table-implied references |
| [Encoding canonicality](il-encoding-canonicality.md) | Equivalent noncanonical ordinary admission, strict writers/audits, width/map/value validity and re-encoding |
| [Resource accounting](il-resource-accounting.md) | Declared scoped capacity, checked inventories/expansion, deterministic work/state, explicit exhaustion and deferred portable minima |
| [Interchange and private persistence](interchange-persistence.md) | Normative versus private serialized claims, actual schema identity, native contract/lifetime gates, tooling scope and documentation ownership |
| [Windows x64 target](target-windows-x64.md) | Scalar/BID storage, source schemas, packing/alignment, standard foreign admission, positional calls, stable borrows, owner transfer and deployment/code lifetime |
| [Windows x64 failure bridge](target-windows-x64-failure-bridge.md) | Paired containment, packet/domain authority, exact result/failure states, mapping/causes, synchronous callbacks, unique retirement and retained admission |
| [Native package identity](native-package-identity.md) | Exact case-sensitive ASCII names versus independent target filename collisions |
| [CLI machine protocol](tooling-cli-protocol.md) | Unified envelope/status, exact input/diagnostic/result shapes, binary output routing and explicit supervision |
