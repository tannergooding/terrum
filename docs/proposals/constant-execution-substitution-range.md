# Historical ConstantExecution type-only substitution range

**Status: superseded, unapproved historical candidate.** The original
recommendation proposed a required `005E` table of four-byte TypeSpecRef
entries for type-only Checked-Body substitutions. It did not select direct
constant-formal bindings or complete Feature 12, intrinsic catalogs or
resource recipes.

The sole active candidate owner for `005E` is
[ConstantExecution mixed checked-Body substitutions](constant-execution-mixed-substitutions.md).
That document recommends one eight-byte tagged `TypeActual`/`ConstantActual`
sequence, with exact owner-then-method formal order, typed reference
namespaces, exclusive range ownership, using-Module context and before-use
checks. It supersedes the four-byte shape for that successor selection;
the four-byte proposal is not an alternative decoder or compatibility layout.

Any current implementation of a four-byte type-only slice is not approval of
the eight-byte successor. Exchange must select one exact schema out of band;
matching `005E`, version bytes, row counts or plausible strides cannot select
between them. If the mixed successor is not approved/adopted, there is no
selected mixed-layout fallback.

Intrinsic identity `005D/26`, formal/input transformation catalogs, compact
resource recipes, Feature 12 completion and independently missing generic,
value, witness and runtime contracts remain separate decisions. The mixed
proposal owns the integrated acceptance cases and implementation gaps; this
historical pointer does not repeat its codec recipe or claim support.
