# Terrum specification proposals

The [source grammar](../grammar-spec.md), [metadata design](../il-metadata.md)
and bounded [coded instruction set](../il-instruction-set.md) are approved
target designs, not claims of implementation or released formats. The
proposal documents in this directory contain unapproved choices. The source
grammar records its own open choices and scope boundaries. An **accepted
direction** settles a semantic
boundary only; adjacent grammar and APIs remain open unless explicitly
approved.

- [Core-library proposal](core-library-reference.md) records candidate
  declarations, dependency layers and unresolved API contracts.
- [Float64 math selection](float64-math-selection.md) proposes a matching-width
  selection service separately from the unapproved `float32` candidate.
- [Static-proof and constant-evaluation proposal](static-proof-and-constant-evaluation.md)
  collects candidate minimum proof rules, effects and evaluator budgets;
  the portable-floor policy itself is accepted.
- [Semantic IL extensions](il-extensions.md) collect the unapproved inline
  built-in type word and compact-profile follow-up, separate from the
  approved `(1, 0)` designs.
- [Self-host frontend verification budget](self-host-frontend-verification-budget.md)
  records a deferred, unapproved architecture option if memoization no longer
  suffices to grow the authored scalar frontend within its shared limit.
- [Reference-call metadata](reference-call-metadata.md) records the proposed
  first dependency and caller-safety metadata slice for general cross-call
  references; it does not change the accepted source contracts.
- [Copy outputs for non-Boolean calls](non-boolean-copy-out.md) recommends a
  bounded hosted `out` extension using the existing normal-return publication
  contract; it does not add unique or managed outputs.
- [Generic Copy outputs for non-Boolean calls](generic-copy-out-publication.md)
  recommends extending that publication rule to compilation-local Copy
  specializations without widening the generic type domain or changing the
  private ABI.
- [Generic Copy initialization outputs for non-Boolean calls](generic-init-output-publication.md)
  recommends carrying ordinary `init ref<T>` normal-return publication through
  compilation-local Copy specializations while preserving Boolean vacancy.
- [Floating Copy generic specializations](floating-copy-generic-specializations.md)
  recommends admitting floating formats only as concrete arguments to hosted
  Copy-only generic templates; it does not grant symbolic floating operations
  or source-free generic support.
- [Concrete floating dependencies in hosted generic proofs](floating-copy-proof-dependencies.md)
  recommends a bounded floating-leaf descriptor and concrete-signature
  hydration slice without expanding managed-class closures.
- [Span-backed token cursor](span-backed-token-cursor.md) records the open
  backing, capture, indexing and construction contracts needed for the
  existing zero-copy self-host cursor candidate.
- [Invocation-scoped source grant](invocation-scoped-source-grant.md)
  recommends a narrow persisted-module dependency for source-aware compiler
  components without enabling arbitrary host-buffer capture.
- [Native source-grant library](native-source-grant-library.md) proposes a
  bounded per-call source ABI for internal native-library exports; it does not
  extend the semantic-module source-grant profile or establish DLL support.
- [Persisted scanner invocation profile](scanner-invocation-profile.md)
  considers a named, scanner-specific invocation profile and copied result
  publication without generalizing source grants to arbitrary buffers.
- [Checked-exception TRRM](checked-exception-trrm.md) records the unapproved
  exceptional-control-flow contract needed before source-free checked-exception
  Bodies can be considered.
- [Optional scalar TRRM execution](optional-scalar-trrm-execution.md) records
  a proposed internal execution boundary that does not imply source-language
  or public native ABI support.
- [Static initialization metadata and attribute schema](static-initialization-metadata.md)
  recommends how to persist the accepted static-storage contract without
  changing its initialization or failure semantics.
- [Compare-exchange order pairs](atomic-cas-order-pairs.md) recommends a
  finite compile-time-valid table for CAS success/failure memory orders;
  it does not add atomic support or change the accepted bitwise-comparison rule.
- [Checked float-to-integer eligibility](checked-float-to-integer-eligibility.md)
  recommends a bounded pair matrix for the accepted checked value policy;
  exact conversion eligibility remains unapproved.
- [Char16-to-floating eligibility](char16-floating-conversion-eligibility.md)
  recommends treating `char16` as an unsigned code-unit value for explicit
  floating conversion; exact pair eligibility remains unapproved.
- [Floating comparisons in executable TRRM](floating-trrm-comparisons.md)
  recommends a separately assigned IEEE comparison opcode and raw-bit
  constants in the unreleased `(1, 0)` profile; it does not add floating
  arithmetic or conversion support.
- [Floating format conversions in executable TRRM](floating-trrm-format-conversions.md)
  recommends a distinct opcode for the accepted `float32`/`float64` conversion
  pair without extending integer conversion policies or adding other pairs.
- [Integer-to-floating conversions in executable TRRM](integer-to-floating-trrm-conversions.md)
  recommends a distinct opcode for the eight accepted fixed-width
  integer-to-floating conversion pairs.
- [Floating negation in executable TRRM](floating-trrm-negation.md)
  recommends a total sign-bit operation that preserves the remaining payload
  bits without defining floating arithmetic NaN encodings.
- [Floating arithmetic in executable TRRM](floating-trrm-arithmetic.md)
  recommends same-format `float32`/`float64` add, subtract, multiply and divide
  using the separate provisional arithmetic-NaN policy.
- [Char16-to-floating conversions in executable TRRM](char16-to-floating-trrm-conversions.md)
  recommends a distinct unsigned code-unit conversion operation without
  widening integer conversions.
- [Floating-to-integer conversions in executable TRRM](floating-to-integer-trrm-conversions.md)
  recommends a distinct fail-fast operation for the eight provisional
  floating-to-fixed-width-integer pairs.

Use these proposals for candidate APIs, source choices and format extensions,
not as a second policy or implementation checklist. Settled call, output,
cleanup and proof contracts are in [language contracts](../language-contracts.md);
other accepted direction lives in the relevant [language](../language.md),
[lifetime](../lifetimes.md), [conversion](../conversions.md),
[layout](../data-layout-and-abi.md), [type-family](../type-families.md),
[memory](../memory-model.md) and [library](../libraries.md) specifications.
Current support and sequencing are in the [implementation reference](../implementation.md)
and [roadmap](../roadmap.md); candidate compiler components are in
[self-host](../../self-host/README.md).
