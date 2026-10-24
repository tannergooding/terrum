# Numeric capability conformance cases

These cases apply to [grouped numeric capabilities](../language/numeric-capabilities.md).
Supplied declaration/witness facts establish finite contract checks, not an
actual source binder, dispatch program or physical witness codec.

| Case | Situation | Required outcome |
|---|---|---|
| NGCP-01 | Exact Core interface/self/formal/member groups | Bind the selected identities and signatures, not broader candidate sketches |
| NGCP-02 | A selection provider supplies Min/Max but omits Clamp | Reject missing member; all three belong to one capability |
| NGCP-03 | Selection/composite inheritance reaches comparison/equality repeatedly | Share the exact inherited member identity, not duplicate slots |
| NGCP-04 | A caller requests a smaller group rather than IScalarNumber | Admit with that group's complete evidence alone |
| NGCP-05 | Numeric primitives implement additive/multiplicative/increment/magnitude/selection/composite | All 20 exact identities participate |
| NGCP-06 | Primitive equality witness matrix | All 26 scalars, including zero-data void |
| NGCP-07 | Primitive comparison witness matrix | Exactly 20 numeric plus four character/scalar identities, not bool/void |
| NGCP-08 | Primitive bitwise witness matrix, including bool rejection | Exactly 12 integers; Boolean's missing complement prevents conformance |
| NGCP-09 | Primitive shift witness matrix | All 144 integer data/count pairs |
| NGCP-10 | Native and fixed-width types happen to have equal widths | Distinct witness instances and retained target dependencies |
| NGCP-11 | Self or count lacks required Copy evidence | Reject missing constraint, not implicit boxing or copying |
| NGCP-12 | Same-spelled foreign interface/member claims Core registration | Reject without exact identity |
| NGCP-13 | A provider removes any required member | Reject before affected generic use |
| NGCP-14 | Provider adds optional unrelated members | Does not excuse a missing requirement or force a larger caller capability |
| NGCP-15 | Concrete specialization has matching members but generic definition lacks a witness | No implicit operation evidence |
| NGCP-16 | A selection-only generic helper adds an undeclared addition | Selection call admits; addition rejects missing additive evidence |
| NGCP-17 | Implementation uses different local formal names | Generic caller retains requirement labels left/right/value/min/max/count |
| NGCP-18 | A selection witness changes Clamp's predicate or fixed Core failure | Reject incompatible retained contract |
| NGCP-19 | Group/composite name is used to infer Pure/ConstEval | No such capability |
| NGCP-20 | Primitive witness dispatch changes signed overflow or floating NaN/sign rules | Reject changed primitive operation contract |
| NGCP-21 | A nominal witness is used to assume associativity, total order or IEEE bits | Requires separate published evidence |
| NGCP-22 | Count evidence is used as implicit numeric promotion | No data/count type merger or precheck narrowing |
| NGCP-23 | A replacement requirement adds params/defaults/init outputs or changes result | Not the exact selected shape |
| NGCP-24 | Unrelated same-spelled requirements occur on inherited paths | Distinct declaring-member identities do not merge |
| NGCP-25 | Repeated self inputs/results use a different complete type | Reject even with equal carrier/ABI |
| NGCP-26 | Actual generic source checking, calls and witness projection | Document-only: finite requirement sets do not implement binding/dispatch |
| NGCP-27 | Actual source-free witness/primitive/member records | Document-only: logical selection assigns no codec or ABI |
| NGCP-28 | Actual numeric programs, effect refinement and constant execution | Document-only: names/membership are not program truth or author opt-in |
