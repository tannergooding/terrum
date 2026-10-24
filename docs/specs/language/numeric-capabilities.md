# Grouped numeric capabilities

This chapter owns `numeric-capability-groups/1`: the selected logical Core
interfaces, their complete same-type requirements and primitive membership.
They are public definitions with exactly the formals shown, in the
`Terrum` namespace. They use the ordinary
[self-interface and witness rules](interface-witnesses.md), not structural
member discovery, a universal numeric root or new operator syntax.
The [semantic IL display map](../il-instruction-set.md#operation-identity-encoding-and-presentation)
does not turn these selected member witnesses into primitive opcodes or
weaken their exact contracts.

The groups expose existing scalar operations and Abs/Min/Max/Clamp.
Heterogeneous operand/result families, identity constants, parsing,
formatting, conversions, classification, SIMD and transcendental functions
are not prerequisites and are not adopted by these declarations.

## 1. Exact capability groups

All shown member requirements are public static, with required ordinary
by-value inputs. The self formal binds to the complete implementing type.
Unmarked type formals already admit the non-unique Copy-capable default
domain, so these declarations have no explicit `[Copy]` constraints. Copy
grants no layout, allocation, thread or lifetime property. There are no
defaults, params inputs, receivers, init outputs or implicit numeric
promotions.

```terrum
public interface IAdditiveOperators<this TSelf>
{
    public static TSelf operator +(TSelf value);
    public static TSelf operator -(TSelf value);
    public static TSelf operator +(TSelf left, TSelf right);
    public static TSelf operator -(TSelf left, TSelf right);
}

public interface IMultiplicativeOperators<this TSelf>
{
    public static TSelf operator *(TSelf left, TSelf right);
    public static TSelf operator /(TSelf left, TSelf right);
    public static TSelf operator %(TSelf left, TSelf right);
}

public interface IIncrementOperators<this TSelf>
{
    public static TSelf operator ++(TSelf value);
    public static TSelf operator --(TSelf value);
}

public interface IEqualityOperators<this TSelf>
{
    public static bool operator ==(TSelf left, TSelf right);
    public static bool operator !=(TSelf left, TSelf right);
}

public interface IComparisonOperators<this TSelf>
    : IEqualityOperators<TSelf>
{
    public static bool operator <(TSelf left, TSelf right);
    public static bool operator <=(TSelf left, TSelf right);
    public static bool operator >(TSelf left, TSelf right);
    public static bool operator >=(TSelf left, TSelf right);
}

public interface IBitwiseOperators<this TSelf>
{
    public static TSelf operator &(TSelf left, TSelf right);
    public static TSelf operator |(TSelf left, TSelf right);
    public static TSelf operator ^(TSelf left, TSelf right);
    public static TSelf operator ~(TSelf value);
}

public interface IShiftOperators<this TSelf, TCount>
{
    public static TSelf operator <<(TSelf value, TCount count);
    public static TSelf operator >>(TSelf value, TCount count);
    public static TSelf operator >>>(TSelf value, TCount count);
}

public interface IAbsoluteValue<this TSelf>
{
    public static TSelf Abs(TSelf value);
}

public interface INumericSelection<this TSelf>
    : IComparisonOperators<TSelf>
{
    public static TSelf Min(TSelf left, TSelf right);
    public static TSelf Max(TSelf left, TSelf right);
    public static TSelf Clamp(TSelf value, TSelf min, TSelf max)
        require<InvalidBoundsException> min <= max;
}

public interface IScalarNumber<this TSelf>
    : IAdditiveOperators<TSelf>,
      IMultiplicativeOperators<TSelf>,
      IIncrementOperators<TSelf>,
      IAbsoluteValue<TSelf>,
      INumericSelection<TSelf>
{
}
```

Min, Max and Clamp are one selection capability, not separately required
per-method interfaces. Selection inherits comparison evidence so that its
published bound predicate binds without private-body inference.
Additive and multiplicative operations are separate useful groups; neither
requires indexing, formatting or an unrelated conversion matrix.
IScalarNumber is only a convenience composition and adds no members.
A caller may use any smaller group without requiring that composite.

Parameter labels are those of the selected requirement. Implementations
cannot change a generic call's labels/defaults by choosing other local names.
The inherited comparison member identities remain shared requirements,
not duplicate slots from each composite path.

## 2. Primitive membership and behavior

The scalar sets below are the exact identities in the
[mandatory catalog](../overview/conformance.md#mandatory-scalar-catalog).
Core's privileged primitive declarations supply these checked nominal
witnesses. An ordinary package cannot replace those declarations or acquire
intrinsic semantics by declaring a similarly named interface.

| Interface instance | Required primitive implementing identities |
|---|---|
| IEqualityOperators<T> | All 26 scalars, including bool and void |
| IComparisonOperators<T> | The 20 numeric scalars and char8/char16/char32/rune |
| IAdditiveOperators<T>, IMultiplicativeOperators<T>, IIncrementOperators<T> | All 20 numeric scalars |
| IAbsoluteValue<T>, INumericSelection<T>, IScalarNumber<T> | All 20 numeric scalars |
| IBitwiseOperators<T> | All 12 integer scalars |
| IShiftOperators<T, C> | Each of the 12 integer data scalars for each of the 12 integer count scalars |

Native integers keep their distinct identities and selected target width.
The shift matrix therefore has 144 complete instances; it does not merge
native and equal-width fixed types or narrow a count before checking it.
An unresolved literal count still uses the primitive int32 contextual rule.
Boolean `&`, `|`, `^` alone do not satisfy IBitwiseOperators because Boolean
`~` is unavailable. Code units and rune do not acquire arithmetic or numeric
selection from their ordering witnesses.

Each primitive witness preserves the exact
[operator domains](primitive-operators.md), [Abs](absolute-value.md),
[selection](numeric-selection.md) and
[floating representation contracts](floating-environment.md).
In particular, integer arithmetic is checked; floating arithmetic is not
implicitly fused; floating equality is not numeric-key equality; order is
not totalOrder; Clamp has its fixed bounds failure; and source updates retain
their captured-place/getter/setter timing.
Dispatch through an interface does not substitute a broader number policy.

## 3. Other witnesses and generic calls

A nominal type may explicitly implement a group when its complete checked
requirements and constraints are satisfied. Matching names, fields, bit
width or extensions do not synthesize conformance. Adapters have their own
nominal identity. A witness may implement a smaller useful group without
implementing the composite or claiming primitive status.

The group is operation evidence, not a claim of algebraic associativity,
total ordering, numeric-key equality, IEEE representation or universally
nonnegative same-type magnitude. Algorithms needing stronger laws or
representations retain an actually published contract.
Primitive-specific numeric rules apply to the exact primitive bindings;
they are not inferred for every nominal implementation.
Clamp's inherited precondition and exact Core failure remain requirements
of every selected INumericSelection witness.

For example, an ordinary generic helper can request just selection:

```terrum
T Limit<T>(T value, T min, T max)
    where T : INumericSelection<T>
{
    return T.via<INumericSelection<T>>.Clamp(value, min, max);
}
```

Its use still needs the selected complete witness, valid bound relationship
or admitted checked-failure handling, and the ordinary call/effect contract.
Knowing T is Copy or knowing that one current specialization has Clamp is
not sufficient. A use of addition within that definition would additionally
need its declared additive evidence; the compiler cannot borrow it from a
concrete caller's composite.

These interfaces publish no unconditional Pure, ConstEval, failure-free,
allocation or nonblocking guarantee. Implementations expose and establish their actual
supported forms/effect bounds; callers may retain an available checked
member refinement under the ordinary rules.
An arithmetic interface name or constant input does not supply a constant
program, permit skipping operands or authorize private-body proof.
IScalarNumber does not add automatic author opt-in.

## 4. Publication and admission

Retain exact Core interface/self/formal/member identities, complete instances,
default Copy-capable formal domains, inherited requirements, selected
implementations and all published parameter/result, predicate, effect, phase
and target dependencies.
Generic substitution, witness projection, packing and compatible replacement
preserve those obligations. A new optional provider member cannot replace a
missing required binding.

Primitive membership is a logical requirement, not proof of an independently
executable witness codec. Missing declaration, witness, program, value,
failure, constant or target support declines before affected use.
This chapter assigns no new Feature ID, primitive code, intrinsic, opcode,
witness-table layout or public generic ABI.
The design-only broader numeric inventory remains proposal material.

[Conformance cases](../conformance/source-numeric-capabilities.md) cover exact
groups, primitive membership, independent generic evidence and retained
contracts without treating a supplied witness model as executable dispatch.
