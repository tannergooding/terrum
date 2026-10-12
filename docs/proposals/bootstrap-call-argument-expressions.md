# Bounded bootstrap call-argument expressions

Status: UNAPPROVED recommendation.

The direct-source bootstrap emitter already parses and checked-folds a bounded
`int32` expression for helper bodies and supported `Main` locals. This
recommendation covers the initial one-operation call-argument increment. The
provisional implementation now also supports operation chains under the
separate still-UNAPPROVED
[call-argument precedence recommendation](bootstrap-call-argument-precedence.md),
and unary-minus operands under the still-UNAPPROVED
[call-argument unary-negation recommendation](bootstrap-call-argument-unary.md),
and grouping under the still-UNAPPROVED
[call-argument parentheses recommendation](bootstrap-call-argument-parentheses.md).
That implementation evidence does not accept any of these recommendations.

Recommend allowing each helper-call argument to contain at most one existing
checked arithmetic operation over the currently supported integer literals and
recognized `Main` locals. The supported operations remain addition,
subtraction, multiplication, integer division, and remainder. Fold each
argument to an `int32` before placing it in the existing register/stack
argument slots. Overflow and division or remainder by zero must reject the
whole source before publishing code or an image. This changes no call ABI,
frame, stack-probe, or unwind behavior.

Keep parentheses, chained operations, function calls, mutable values, runtime
expression lowering, conversions, and general expression parsing outside this
recommendation. It only extends the existing bounded source-to-PE subset; it
does not establish a source-language, metadata, or ABI contract. The
recommendation and implementation remain unapproved.
