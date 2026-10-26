# Decimal division by infinity: zero cohort

**Status: unapproved; provisional implementation direction under the
authorized missing-contract workflow.** This proposal does not amend accepted
specifications or establish compiler support.

## Recommendation and scope

For a finite decimal dividend divided by either signed infinity, produce
canonical zero at the format's **minimum quantum exponent**, with the
exclusive-or of the operand signs. Include finite dividends that are already
signed zero. Apply one rule to `decimal32`, `decimal64` and `decimal128`.

This fills one special-result cohort gap. Preserve finite/finite preferred
exponents, rounding, NaN processing, remainder, copies and sign-only operations.
It requires no new source syntax, library API, capability, opcode or value
codec. Approval of the rule below remains the language decision; implementation
and qualification remain separate obligations.

## Authority and evidence

The [primitive-operators owner](../specs/language/primitive-operators.md#floating-arithmetic-and-comparisons)
assigns division's preferred exponent as the operand-exponent difference for
finite decimal operands. It forbids arbitrary equal-valued cohort selection,
but does not assign the zero cohort for finite/infinity division. Infinity
has no finite quantum exponent to subtract.

The [floating-environment owner](../specs/language/floating-environment.md#1-value-oriented-execution)
preserves decimal quantum and excludes ambient language status/traps; it does
not select this special cohort. The [BID target owner](../specs/targets/windows-x64-data-layout.md#3-ieee-decimal-bid)
defines finite quantum ranges and infinity classification, including ignored
infinity trailing bits, but supplies no arithmetic exponent for infinity.
The proposed addition therefore fills a missing choice rather than replacing
the finite/finite rule.

Current implementation assumption:
[`FloatingLiteralParser.Arithmetic.cs`](../../sources/Terrum.CodeAnalysis/Semantics/FloatingLiteralParser.Arithmetic.cs),
`TryDivide`, passes the finite dividend's exponent to `TryEncodeZero` for a
decimal infinite divisor. That preserves the numerator quantum, effectively
treating infinity's exponent as zero for this case. Existing code is not
authority for that choice. The recommendation intentionally changes it when
the numerator exponent is not the minimum.

Independent evidence favors the minimum quantum: GCC 15.2.0 libbid's
[`bid64_div`](https://github.com/gcc-mirror/gcc/blob/releases/gcc-15.2.0/libgcc/config/libbid/bid64_div.c)
and
[`bid128_div`](https://github.com/gcc-mirror/gcc/blob/releases/gcc-15.2.0/libgcc/config/libbid/bid128_div.c)
finite/infinity branches return sign-only zero containers. Under Terrum's
selected BID decoding, these have biased exponent zero, hence minimum
quantum, **not quantum zero**. This is informative implementation evidence,
not an import of those providers or a claim of executed Terrum conformance.

## Alternatives

| Candidate | Tradeoff |
|---|---|
| Preserve the finite dividend's exponent | Smallest code churn and retains its scale, but ratifies an otherwise unsupported exponent-zero convention for infinity and differs from the BID evidence above. |
| Use the format's minimum quantum — recommended | One format-derived special-result rule, independent of the dividend's cohort and infinity's ignored bits; matches the cited BID behavior. Requires correcting the current branch. |
| Use quantum zero | A simple fixed cohort, but introduces an arbitrary distinguished exponent rather than using the format boundary or the finite/finite preference. |
| Leave the cohort unspecified, or normalize all division zeros | Weakens existing observable-cohort guarantees or changes unrelated finite/finite results. A broader arithmetic redesign is unnecessary. |

The recommended local amendment is the smallest coherent integration with the
existing IEEE-oriented arithmetic and explicit BID representation, not merely
the smallest change to today's implementation.

## Exact proposed normative addition

Add the following paragraph in
[`primitive-operators.md`, “Floating arithmetic and comparisons”](../specs/language/primitive-operators.md#floating-arithmetic-and-comparisons),
immediately after the paragraph defining finite decimal preferred exponents:

> For division in `decimal32`, `decimal64` or `decimal128`, a finite dividend
> divided by an infinite divisor produces zero of that same type. Its sign
> is the exclusive-or of the operand signs, its coefficient is zero, and its
> quantum exponent is the format's minimum finite quantum exponent:
> -101 for `decimal32`, -398 for `decimal64`, and -6176 for `decimal128`.
> Produce the target's canonical finite encoding of that zero. This rule
> includes either signed-zero dividend and either sign of infinity,
> independently of the dividend's quantum and any ignored infinity-encoding
> bits. Infinity is not assigned a finite quantum exponent. This special case
> does not change the preferred-exponent rule for two finite operands or the
> rules for NaNs, infinite dividends, remainder, copies or sign operations.

No accepted text is removed or superseded. The
[primitive conformance owner](../specs/conformance/source-primitive-operators.md#floating-formats-and-observable-edges)
should receive corresponding cases on adoption; the target and environment
owners retain their existing roles, without duplicate arithmetic rules.

## Cross-layer consequences

* **Binding and libraries:** retain same-type primitive `/` and its `/=`
  expansion, eager operand order and effects. No infinity-name recognition,
  mixed-format promotion or new Core member is introduced. Numeric equality
  still equates zero cohorts; key/hash normalization remains separate.
* **Folding, proof and verification:** select the minimum quantum in the
  infinite-divisor branch, using format facts rather than a fabricated
  infinity exponent. Preserve exact type, sign and result bits through bound
  trees and admitted constant execution. Do not generalize the branch into
  normalization of other zero results. Existing operation/support checks
  remain required.
* **Runtime and lowering:** every admitted software/native implementation
  must implement the same class-specific result. On the selected BID target,
  canonical result containers have only the sign bit possibly set. Preserve
  subnormals, target control-state duties and the absence of a language
  floating-status channel. Emission alone is not runtime qualification.
* **Persistence, dependencies and replacement:** existing typed scalar
  constant carriers must retain exact zero sign/quantum bits; retained
  division bodies and replacement providers must preserve the selected
  operation contract. Do not normalize imported frozen zero constants to
  this operation's result cohort. No wire assignment or compatibility reader
  is needed. Keep structural safe-consumption checks and producer truth
  obligations; consumers need not re-prove published arithmetic results.

## Conformance and qualification gates

Use decoded `(coefficient, quantum)` fixtures, not new source syntax. The
following parameters come from the existing target owner:

| Format | Width N | Precision p | q minimum | q maximum |
|---|---:|---:|---:|---:|
| decimal32 | 32 | 7 | -101 | 90 |
| decimal64 | 64 | 16 | -398 | 369 |
| decimal128 | 128 | 34 | -6176 | 6111 |

Apply every applicable row below to **all three widths**. Sweep all four
operand-sign pairs, including both zero signs; non-NaN division results use
their exclusive-or. NaNs retain the existing runtime/constant result policies.
Observe class before fields; for finite results assert coefficient, quantum
and promised bits, not just numeric equality. For the proposed special result,
the complete expected BID container is `sign << (N - 1)`.

| Case | Required outcome |
|---|---|
| Finite/infinity, positive cases | Divide coefficients `0`, `1`, `10` and `10^p - 1`, at `q minimum`, `q minimum + 1`, `-2`, `0`, `1` and `q maximum`, by either infinity. Always zero at `q minimum`; no language arithmetic failure/trap. Include smallest subnormal and largest finite inputs. |
| Cohort discrimination | Equal-valued dividends `(120,-2)` and `(1200,-3)`, and zeros at different quantums, yield the same result cohort for the same operand signs. Results must not retain numerator quantum or default to quantum zero. |
| Encoding boundaries | Vary admitted ignored infinity bits and use noncanonical finite coefficients that decode as signed zero at nonminimum quantum. Classification precedes exponent extraction; result remains canonical zero at `q minimum`, with inputs unchanged. |
| Adjacent finite/finite zero | A zero at `qL` divided by finite nonzero `(1,qR)` yields zero at `clamp(qL - qR, q minimum, q maximum)`, not always at the minimum. Test interior differences and `(qL,qR)` equal to each ordering of the exponent endpoints. |
| Adjacent exact preferred cohorts | `(120,-2)/(20,-1)` gives `(6,-1)`; `(1200,-3)/(20,-1)` gives `(60,-2)`. Do not collapse their distinct preferred cohorts. `(100,-2)/(200,-2)` gives `(5,-1)` because the preferred exponent zero cannot express the exact quotient. |
| Finite rounding/boundaries | `(1,0)/(3,0)` gives coefficient `(10^p - 1)/3` at quantum `-p`. `(1,q minimum)/(1,0)` remains the smallest subnormal; division by `(2,0)` rounds it to zero at the minimum. `(3,q minimum)/(2,0)` rounds to coefficient `2` there, ties-to-even. `(1,q maximum)/(1,q minimum)` overflows to signed infinity, not the proposed zero. |
| Excluded operand classes | Zero/zero and infinity/infinity yield quiet NaN; NaN/infinity and finite/NaN also follow existing quiet-NaN policy. Nonzero finite/zero and infinity/finite-nonzero produce signed infinity. Never route these through the finite/infinity zero rule. |
| Cross-operation negatives | Finite remainder infinity preserves the dividend's cohort/sign; copies, unary plus, negation and Abs keep their existing representation contracts. Mixed decimal widths and binary/decimal operands still reject primitive division, rather than converting to obtain this result. |

Extend the existing public-compiler-API, exact-`ScalarBits` patterns in
[`WideFloatingOperatorSemanticsTests.cs`](../../tests/Terrum.CodeAnalysis.Tests/Semantics/WideFloatingOperatorSemanticsTests.cs)
with data-driven folded and admitted ConstEval cases. Exercise operand-fed
division and `/=` staging so a known infinite divisor does not erase operand
effects or repeat target capture. Qualify exact-bit persistence round trips
and nonconstant execution separately on each admitted path; unsupported paths
must decline before use, not substitute host decimal behavior.

These are acceptance gates, not executed-test evidence. Native wide-floating
lowering and standard-interchange support remain independently gated by the
[support inventory](../implementation.md#hosted-compiler); this proposal
neither completes those areas nor changes their status.
