# Registered queries

This chapter owns the selected source query family and the shared identity,
authorization and admission boundary for compiler-known queries. The dedicated
`typeof`, `sizeof`, `alignof`, `offsetof` and `countof` forms are not Core
method calls or general reflection. [Complete types](types-and-normalization.md),
[target layout](../data-layout-and-abi.md), and
[constant execution](proof-and-constant-evaluation.md) retain their separate
responsibilities.

## Selected query forms

| Query | Subject | Result and meaning |
|---|---|---|
| `typeof(Type)` or `typeof(Place)` | Complete type, or a Place's statically bound complete type | Type-identity evidence; never a runtime reflection object |
| `sizeof(Type)` or `sizeof(Place)` | Complete value/handle representation under the selected target | Nonnegative `nint` byte extent |
| `alignof(Type)` or `alignof(Place)` | The same complete selected-target representation | Nonnegative `nint` placement alignment; `alignof(void)` is `0`, meaning no placement requirement |
| `offsetof(Type, FieldPath)` | Complete type and inline field path | Nonnegative `nint` cumulative field offset from the supplied type |
| `offsetof(Place)` | A bound field Place and its exact static path | Nonnegative `nint` terminal field offset in its immediate containing type |
| `countof(Type)` | Complete type whose shape carries an explicit fixed-cardinality fact, such as an admitted fixed inline-array/buffer extent | Nonnegative `nint` fixed cardinality |
| `countof(KnownShapeExpression)` | A literal or collection expression with statically known logical element count | Nonnegative `nint` syntactic shape count, without evaluating values or materializing/enumerating a collection |
| `countof(Place)` | A live value with a selected nominal Count witness | The current nonnegative `nint` count, observed through that synchronous contract |

The expression-form operands of `typeof`, `sizeof`, `alignof` and
`offsetof(Place)` must bind as existing Terrum Places, not arbitrary
Expressions. These queries inspect type/layout facts and do not read or
evaluate the stored value, invoke a getter/indexer/call, or perform arithmetic.
An `init` Place may be named by `offsetof(Place)` because it observes no value.
Explicit dereference remains subject to ordinary access and unsafe rules.
`countof(Place)` is different: it observes the live value through its Count
witness and therefore requires ordinary readable-place and call authorization.
`typeof(Place)` reports the Place's statically bound type. For a `G<_>` Place,
it reports the existential view; it does not recover the hidden argument or
witness. That recovery requires a successful checked `G<var T>` pattern; no
general runtime type/witness-inspection query is selected.

## Identity and authority

`typeof`, `sizeof`, `alignof` and `offsetof` are grammar-owned queries with
their own complete subject and target contracts. A same-named user method or
attribute does not replace them. A user call such as
`RuntimeHelpers.SizeOf<T>()`, if declared, remains an ordinary call with its
own effects and constant eligibility; it does not acquire query semantics.
There is no general source `[Query]` registration schema.
The [semantic IL display map](../il-instruction-set.md#operation-identity-encoding-and-presentation)
reuses these selected query identities when displaying typed operations; it
does not add a Core method, opcode or alternate query contract.

`countof(Type)` and `countof(KnownShapeExpression)` use only fixed-cardinality
evidence. They do not infer count from fields/layout or invoke a live Count
getter. `countof(Place)` selects the shared nominal Core `ICountable` requirement
and reads its instance `Count : nint` property; an unrelated interface's
same-shaped property does not satisfy it. The property is readable and returns
a nonnegative value; its getter is synchronous and `Pure`. `Pure`
permits observing the
collection's current mutable state; it promises neither caching nor
constant-time access. A collection such as a linked list may traverse its own
representation. `ConstEval` is optional and separately admitted. Capacity is
not Count.

The single shared Core requirement is `ICountable.Count : nint`. Its witness
codec and built-in dynamic implementations remain separate Core/schema
decisions. In particular, literal `countof` counts logical code units in the
selected encoding, not Unicode scalars or bytes. `TextSpan<void>` must retain
or safely recover its selected-encoding evidence before a live Count
observation; it cannot invent an encoding.

`IsPureEval` and `IsConstEval` remain exact selected Core identities observing
the selected activation's immutable evaluation form. `IsConstEval` implies
pure mode when true. No duplicate claim with an incompatible owner, signature,
rule or recipe may select a winner through import order or spelling.
Declaration selectors such as `nameof(parameter)` in admitted dependency
schemas retain their existing binding rules and do not read parameters or
vacant outputs. This chapter does not select general object-payload,
reflection-root, policy-state or runtime-capability queries.

## Layout observation and authorization

Each type or Place query observes the complete normalized static type and its
selected representation. It never implicitly selects a pointee, inline
payload, runtime-derived type, object graph, or convenient ABI carrier.
`sizeof(C)` for a class handle measures the handle representation, not the
allocated object's payload/header; `sizeof(val<C>)` measures the admitted
inline representation. A reference, pointer, owner or view query measures
its complete descriptor/handle representation, not reachable storage or
backing length.

Every supported complete representation has a size under the selected target;
size exists independently of query authorization. `Blittable` commits the
relevant layout facts for a selected target and permits safe layout
observation. A non-Blittable layout query requires explicit unsafe
authorization because its observed size, alignment or offset depends on
version-sensitive private representation, even when the query performs no
direct memory access. Such an observation is not a compatibility promise.
Querying layout does not imply Copy, foreign equivalence, field access,
atomicity, reinterpretation permission, or a constant-value codec.
`typeof` reports complete type identity and does not itself inspect layout or
require unsafe. Scalable SIMD is not excluded merely because its extent is
not fixed for AOT: it is Blittable for its C ABI role, and its layout query
uses the selected target's vector-length model.

`offsetof(Type, FieldPath)` follows only inline fields and returns their
cumulative displacement from the supplied complete type. It does not cross
reference/pointer indirections. `offsetof(Place)` binds the exact field/layout
path and returns the terminal field displacement in its immediate containing
type; it never accumulates across a runtime indirection. Neither form accepts
properties, indexers, arbitrary calls or field names inferred from layout.
Ordinary field accessibility and unsafe rules still apply.

Size may be zero for any admitted complete representation with zero extent;
this does not collapse nominal identity. `sizeof(void)` is zero.
`alignof(void)` is also zero to mean that unit data has no placement
requirement; zero is not a valid alignment for an actual stored type. Other
stored types require their target's positive alignment rule. Every size,
alignment and count result must fit `0..nint.MaxValue`. An unrepresentable
layout is unsupported; queries never wrap, saturate, or return a success-shaped
sentinel.

## Target evaluation and required constants

One coherent layout is selected from the target ABI/profile and complete
representation dependencies. The evaluator host, build configuration,
register allocation and instruction selection do not supply query results.
Fixed target layouts may resolve during source compilation or closed-instance
JIT/AOT specialization and may be folded. Source binding does not pre-fold
target layout into an ABI-independent value. For example, `sizeof(int32)`
may fold during Terrum compilation when its selected representation is fixed,
while `sizeof(nint)` must resolve against the selected target during JIT/AOT
compilation. A required constant using a target-dependent query is resolved
before runtime; it is not a runtime query merely because Terrum compilation
could not yet know the result. If the selected layout remains
execution-dependent—for example, scalable vector length supplied by the
running process—the same semantic query remains a runtime operation.
Required-constant contexts reject such execution-dependent results unless the
selected target settings make them fixed.

`countof(Type)` uses only an explicit fixed-cardinality fact carried by the
complete type shape or a fixed constant type argument. It does not derive a
count from byte size or infer cardinality from unrelated fields. A statically
countable literal/collection shape is counted syntactically, without
evaluating elements, materializing storage, or enumerating an unknown
spread/source. A shape requiring runtime source
length has no shape count. `countof(Place)` observes the current live value
through the Count witness; mutable counts may differ between observations,
including through readonly aliases. It is not a fixed-shape query.

Required constant evaluation follows the ordinary
[constant-execution contract](proof-and-constant-evaluation.md). It needs
exact subject, target, selected operation and resource evidence; unavailable
or exhausted required evaluation diagnoses without a runtime/default fallback.
An optional backend fold of an ordinary query preserves the exact query,
subject, target and surviving layout dependencies.
An already source-evaluated structured constant retains recursive typed
values and complete field identities, not host bytes or its source expression.
Target-aware materialization derives ordinary field placement from validated
`Layout` records; explicit fixups are only for genuine address relocations.
JIT/AOT resolves only the query work that was still target-dependent and does
not replay completed source evaluation.

The result is a scalar observation, not a commitment to the queried type.
A retained layout-dependent value or derived decision must either record a
published fixed representation commitment for the selected target or retain
the complete explicit rebuild/rebind/invalidation dependency closure. A
Blittable result representation does not freeze an unfixed queried layout.
An AOT/JIT label, timestamp, rebuild comment or informal intention is not
layout coupling. Per-target commitments do not promise cross-target equality.

## Retained contracts and compatibility

| Required fact | Meaning |
|---|---|
| Query identity and rule | Exact grammar-owned operation or selected Count-property witness, including its getter identity, independent of display names and row indexes |
| Subject | Complete normalized type, bound Place/path, or statically known shape; preserve access, representation and ownership |
| Result | Exact nonnegative `nint` domain; `alignof(void)=0` is the no-placement sentinel |
| Target/layout | Selected target ABI, coherent layout facts, Blittable/fixed commitments or execution-dependent query evidence |
| Count witness | Exact shared `ICountable.Count : nint` requirement, property/getter identity and selected implementing witness for live Place observation; shape queries carry no runtime witness |
| Retained observation | Actual result and its fixed or explicitly invalidatable dependency basis |
| Admission | Supported query rules, complete references and consumer compatibility; published query facts are the producer's responsibility |

Implementations claiming support preserve the registered query rules,
subject/path references, target compatibility and representable results.
Consumers validate selected record structure, references and target support
before affected use, then may rely on the producer-published layout and query
facts without recomputing their semantic truth. Missing
layout evidence, unsafe authorization or incompatible retained observations
do not establish support. Linking, trimming and remapping preserve semantic
operation/type/field/witness/dependency identities independently of physical
rows, debug names and packing.
Changing a fixed observation is incompatible; changing an unfrozen private
layout is permitted only within its contract and after all explicitly coupled
consumers are regenerated, rebound or invalidated.

The physical representation is specified by the assigned `DE` query family,
Body FieldMap and global FieldPath table in the
[instruction set](../il-instruction-set.md#assigned-long-families) and
[IL metadata](../il-metadata.md#body-table-and-coded-instruction-boundary),
with Feature 13 admission and retained layout dependencies. `typeof` has an
internal TypeIdentity carrier only when runtime comparison needs a value;
it does not introduce a public reflection object. Fixed Type/shape
`countof` lowers to an ordinary `nint` constant, while Place `countof`
remains the selected Core getter call; there is no universal Count/Length
opcode or `length` alias. These source contracts remain unchanged. Their
cross-layer representation and consumer-validation obligations are tracked
in [logical records](../representation/logical-records.md#registered-query-contracts-and-representation)
and the [conformance inventory](../conformance/source-registered-queries.md).
