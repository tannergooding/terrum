using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Terrum.CodeAnalysis.Semantics;

internal static class BoundGenericMethodTypeTerms
{
    internal static bool AreEquivalent(
        BoundGenericMethodTypeTerm left,
        BoundGenericMethodTypeTerm right, int depth = 0)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || left.Kind != right.Kind
            || left.ConcreteType != right.ConcreteType
            || left.FormalOrdinal != right.FormalOrdinal
            || left.SourceGenericTypeDefinitionIndex
                != right.SourceGenericTypeDefinitionIndex
            || left.IsWritable != right.IsWritable
            || left.IsGarbageCollected != right.IsGarbageCollected
            || left.IsInitialization != right.IsInitialization
            || left.IsOptional != right.IsOptional
            || left.NominalTypeIdentity != right.NominalTypeIdentity
            || left.ElementCount != right.ElementCount
            || left.CallingConvention != right.CallingConvention
            || left.ParameterCount != right.ParameterCount
            || left.AllowsVoid != right.AllowsVoid
            || left.IsElementWritable != right.IsElementWritable
            || left.TypeArguments.IsDefault
            || right.TypeArguments.IsDefault
            || left.TypeArguments.Length != right.TypeArguments.Length
            || left.ConstantArguments.IsDefault
            || right.ConstantArguments.IsDefault
            || left.ConstantArguments.Length
                != right.ConstantArguments.Length)
        {
            return false;
        }

        for (var index = 0; index < left.TypeArguments.Length; index++)
        {
            if (!AreEquivalent(left.TypeArguments[index],
                right.TypeArguments[index], depth + 1))
            {
                return false;
            }
        }

        for (var index = 0; index < left.ConstantArguments.Length; index++)
        {
            var leftArgument = left.ConstantArguments[index];
            var rightArgument = right.ConstantArguments[index];
            if (leftArgument.Kind != rightArgument.Kind
                || leftArgument.FormalOwnerKind
                    != rightArgument.FormalOwnerKind
                || leftArgument.FormalOrdinal
                    != rightArgument.FormalOrdinal
                || leftArgument.FormalDefinitionOrdinal
                    != rightArgument.FormalDefinitionOrdinal
                || leftArgument.DomainType != rightArgument.DomainType
                || leftArgument.CarrierType != rightArgument.CarrierType
                || leftArgument.Bits != rightArgument.Bits)
            {
                return false;
            }
        }

        return true;
    }

    internal static string CreateKey(BoundGenericMethodTypeTerm term)
    {
        var key = new StringBuilder();
        AppendKey(key, term, 0);
        return key.ToString();
    }

    internal static bool ContainsKind(
        BoundGenericMethodTypeTerm term,
        BoundGenericMethodTypeTermKind kind, int depth = 0)
    {
        if (term is null || depth > SemanticVerifier.MaximumDepth
            || term.TypeArguments.IsDefault)
        {
            return false;
        }
        if (term.Kind == kind)
        {
            return true;
        }
        foreach (var child in term.TypeArguments)
        {
            if (ContainsKind(child, kind, depth + 1))
            {
                return true;
            }
        }
        return false;
    }

    private static void AppendKey(StringBuilder key,
        BoundGenericMethodTypeTerm term, int depth)
    {
        if (term is null || depth > SemanticVerifier.MaximumDepth)
        {
            _ = key.Append("invalid");
            return;
        }

        _ = key.Append('(')
            .Append((byte)term.Kind).Append(':')
            .Append((int)term.ConcreteType).Append(':')
            .Append(term.FormalOrdinal).Append(':')
            .Append(term.SourceGenericTypeDefinitionIndex).Append(':')
            .Append(term.IsWritable ? '1' : '0')
            .Append(term.IsGarbageCollected ? '1' : '0')
            .Append(term.IsInitialization ? '1' : '0')
            .Append(term.IsOptional ? '1' : '0')
            .Append(term.ElementCount).Append(':')
            .Append((byte)term.CallingConvention).Append(':')
            .Append(term.ParameterCount).Append(':')
            .Append(term.AllowsVoid ? '1' : '0')
            .Append(term.IsElementWritable ? '1' : '0');
        if (term.NominalTypeIdentity is
            BoundGenericNominalTypeIdentity identity)
        {
            _ = key.Append(':').Append(identity.ProviderIdentity.Length)
                .Append(':').Append(identity.ProviderIdentity)
                .Append(':').Append(identity.ModuleIdentity.Length)
                .Append(':').Append(identity.ModuleIdentity)
                .Append(':').Append(identity.DefinitionId);
        }
        else
        {
            _ = key.Append(":no-nominal");
        }

        _ = key.Append(":T").Append(term.TypeArguments.IsDefault
            ? -1 : term.TypeArguments.Length).Append('[');
        if (!term.TypeArguments.IsDefault)
        {
            foreach (var child in term.TypeArguments)
            {
                AppendKey(key, child, depth + 1);
            }
        }
        _ = key.Append("]:C").Append(term.ConstantArguments.IsDefault
            ? -1 : term.ConstantArguments.Length).Append('[');
        if (!term.ConstantArguments.IsDefault)
        {
            foreach (var argument in term.ConstantArguments)
            {
                _ = key.Append((byte)argument.Kind).Append(':')
                    .Append((byte)argument.FormalOwnerKind).Append(':')
                    .Append(argument.FormalDefinitionOrdinal).Append(':')
                    .Append(argument.FormalOrdinal).Append(':')
                    .Append((int)argument.DomainType).Append(':')
                    .Append((int)argument.CarrierType).Append(':')
                    .Append(argument.Bits.Low).Append(':')
                    .Append(argument.Bits.High).Append(';');
            }
        }
        _ = key.Append("])");
    }

    internal static bool TrySubstitute(
        BoundGenericMethodTypeDefault typeDefault,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        out BoundGenericMethodTypeDefault substituted)
        => TrySubstitute(typeDefault, typeArguments, [],
            out substituted);

    internal static bool TrySubstitute(
        BoundGenericMethodTypeDefault typeDefault,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        ImmutableArray<BoundGenericMethodConstantArgument?>
            constantArguments,
        out BoundGenericMethodTypeDefault substituted)
    {
        substituted = null!;
        if (typeDefault is null || typeArguments.IsDefault
            || constantArguments.IsDefault)
        {
            return false;
        }

        if (typeDefault.FormalDependencies.IsDefault
            || !TryGetFormalDependencies(typeDefault.Term,
                out var originalDependencies)
            || !AreEqual(typeDefault.FormalDependencies,
                originalDependencies)
            || typeDefault.ConstantFormalDependencies.IsDefault
            || !TryGetConstantFormalDependencies(typeDefault.Term,
                out var originalConstantDependencies)
            || !AreEqual(typeDefault.ConstantFormalDependencies,
                originalConstantDependencies))
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        if (!TryCopyTypeArguments(typeArguments, ref remaining,
                out var checkedArguments)
            || !TrySubstitute(typeDefault.Term, checkedArguments,
                ref remaining, 0, out var term)
            || !TrySubstituteConstantArguments(term, constantArguments,
                ref remaining, 0, out term)
            || !TryGetFormalDependencies(term, out var dependencies)
            || !TryGetConstantFormalDependencies(term,
                out var constantDependencies))
        {
            return false;
        }

        substituted = new BoundGenericMethodTypeDefault(term, dependencies) {
            ConstantFormalDependencies = constantDependencies,
        };
        return true;
    }

    private static bool TrySubstituteConstantArguments(
        BoundGenericMethodTypeTerm term,
        ImmutableArray<BoundGenericMethodConstantArgument?>
            constantArguments,
        ref int remaining, int depth,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (depth > SemanticVerifier.MaximumDepth || --remaining < 0
            || term.TypeArguments.IsDefault
            || term.ConstantArguments.IsDefault)
        {
            return false;
        }

        var typeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(term.TypeArguments.Length);
        foreach (var child in term.TypeArguments)
        {
            if (!TrySubstituteConstantArguments(child, constantArguments,
                ref remaining, depth + 1, out var rewrittenChild))
            {
                return false;
            }
            typeArguments.Add(rewrittenChild);
        }

        var constants = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                term.ConstantArguments.Length);
        foreach (var argument in term.ConstantArguments)
        {
            if (argument.Kind
                    != BoundGenericMethodTypeConstantArgumentKind.Formal
                || argument.FormalOwnerKind
                    != BoundGenericTypeFormalOwnerKind.GenericMethod)
            {
                constants.Add(argument);
                continue;
            }

            if ((uint)argument.FormalOrdinal
                    >= (uint)constantArguments.Length
                || constantArguments[argument.FormalOrdinal] is not
                    BoundGenericMethodConstantArgument actual)
            {
                return false;
            }

            if (actual.Kind == BoundGenericMethodConstantArgumentKind.Value)
            {
                constants.Add(new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Value, -1,
                    actual.DomainType, actual.CarrierType, actual.Bits));
            }
            else if (actual.Kind
                    == BoundGenericMethodConstantArgumentKind.Formal
                && actual.CallerDefinitionOrdinal >= 0
                && actual.CallerConstantFormalOrdinal >= 0
                && (actual.CallerFormalOwnerKind
                        == BoundGenericTypeFormalOwnerKind.GenericMethod
                    ? actual.CallerFormalOrdinal >= 0
                    : actual.CallerFormalOwnerKind
                        == BoundGenericTypeFormalOwnerKind.SourceGenericType
                        && actual.CallerFormalOrdinal == -1))
            {
                constants.Add(new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Formal,
                    actual.CallerConstantFormalOrdinal,
                    PrimitiveType.Void, PrimitiveType.Void, default) {
                    FormalDefinitionOrdinal =
                        actual.CallerDefinitionOrdinal,
                    FormalOwnerKind = actual.CallerFormalOwnerKind,
                });
            }
            else
            {
                return false;
            }
        }

        substituted = term with {
            TypeArguments = typeArguments.MoveToImmutable(),
            ConstantArguments = constants.MoveToImmutable(),
        };
        return true;
    }

    internal static bool TrySubstitute(
        BoundGenericMethodTypeTerm term,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (typeArguments.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        if (!TryCopyTypeArguments(typeArguments, ref remaining,
            out var checkedArguments))
        {
            return false;
        }

        return TrySubstitute(term, checkedArguments, ref remaining, 0,
            out substituted);
    }

    internal static bool TrySubstitute(
        BoundGenericMethodTypeTerm term,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        ImmutableArray<BoundGenericMethodConstantArgument?>
            constantArguments,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (typeArguments.IsDefault || constantArguments.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        return TryCopyTypeArguments(typeArguments, ref remaining,
                out var checkedArguments)
            && TrySubstitute(term, checkedArguments, ref remaining, 0,
                out var typeSubstituted)
            && TrySubstituteConstantArguments(typeSubstituted,
                constantArguments, ref remaining, 0, out substituted);
    }

    internal static bool TrySubstituteSourceGenericTypeFormals(
        BoundGenericMethodTypeTerm term,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        int sourceGenericTypeDefinitionIndex,
        ImmutableArray<BoundGenericMethodTypeConstantArgument>
            constantArguments,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (typeArguments.IsDefault
            || sourceGenericTypeDefinitionIndex < 0
            || constantArguments.IsDefault)
        {
            return false;
        }

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        return TryCopyTypeArguments(typeArguments, ref remaining,
                out var checkedTypeArguments)
            && TrySubstitute(term, checkedTypeArguments, ref remaining, 0,
                out var typeSubstituted)
            && TrySubstituteSourceGenericTypeFormals(typeSubstituted,
                sourceGenericTypeDefinitionIndex, constantArguments,
                ref remaining, 0, out substituted);
    }

    internal static bool TrySubstituteSourceGenericTypeFormals(
        BoundGenericMethodTypeTerm term,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        int sourceGenericTypeDefinitionIndex,
        ImmutableArray<BoundGenericMethodConstantArgument?>
            constantArguments,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (constantArguments.IsDefault)
        {
            return false;
        }

        var converted = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                constantArguments.Length);
        foreach (var argument in constantArguments)
        {
            if (argument is null)
            {
                return false;
            }

            if (argument.Kind
                == BoundGenericMethodConstantArgumentKind.Value)
            {
                converted.Add(new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Value, -1,
                    argument.DomainType, argument.CarrierType,
                    argument.Bits));
            }
            else if (argument.Kind
                        == BoundGenericMethodConstantArgumentKind.Formal
                    && argument.CallerDefinitionOrdinal >= 0
                    && argument.CallerConstantFormalOrdinal >= 0
                    && (argument.CallerFormalOwnerKind
                            == BoundGenericTypeFormalOwnerKind.GenericMethod
                        ? argument.CallerFormalOrdinal >= 0
                        : argument.CallerFormalOwnerKind
                                == BoundGenericTypeFormalOwnerKind
                                    .SourceGenericType
                            && argument.CallerFormalOrdinal == -1))
            {
                converted.Add(new BoundGenericMethodTypeConstantArgument(
                    BoundGenericMethodTypeConstantArgumentKind.Formal,
                    argument.CallerConstantFormalOrdinal,
                    PrimitiveType.Void, PrimitiveType.Void, default) {
                    FormalDefinitionOrdinal =
                        argument.CallerDefinitionOrdinal,
                    FormalOwnerKind = argument.CallerFormalOwnerKind,
                });
            }
            else
            {
                return false;
            }
        }

        return TrySubstituteSourceGenericTypeFormals(term, typeArguments,
            sourceGenericTypeDefinitionIndex, converted.MoveToImmutable(),
            out substituted);
    }

    private static bool TrySubstituteSourceGenericTypeFormals(
        BoundGenericMethodTypeTerm term,
        int sourceGenericTypeDefinitionIndex,
        ImmutableArray<BoundGenericMethodTypeConstantArgument>
            constantArguments,
        ref int remaining, int depth,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (!IsValidNode(term, ref remaining, depth))
        {
            return false;
        }

        var typeArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(term.TypeArguments.Length);
        foreach (var argument in term.TypeArguments)
        {
            if (!TrySubstituteSourceGenericTypeFormals(argument,
                sourceGenericTypeDefinitionIndex, constantArguments,
                ref remaining, depth + 1, out var child))
            {
                return false;
            }
            typeArguments.Add(child);
        }

        var rewrittenConstants = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                term.ConstantArguments.Length);
        foreach (var argument in term.ConstantArguments)
        {
            if (argument.Kind
                    != BoundGenericMethodTypeConstantArgumentKind.Formal
                || argument.FormalOwnerKind
                    != BoundGenericTypeFormalOwnerKind.SourceGenericType
                || argument.FormalDefinitionOrdinal
                    != sourceGenericTypeDefinitionIndex)
            {
                rewrittenConstants.Add(argument);
                continue;
            }

            if ((uint)argument.FormalOrdinal
                    >= (uint)constantArguments.Length)
            {
                return false;
            }

            var actual = constantArguments[argument.FormalOrdinal];
            if ((actual.Kind
                    == BoundGenericMethodTypeConstantArgumentKind.Value
                    && (actual.FormalOrdinal != -1
                        || actual.DomainType == PrimitiveType.Void
                        || actual.CarrierType == PrimitiveType.Void
                        || !actual.Bits.IsCanonicalFor(
                            actual.CarrierType)))
                || (actual.Kind
                        == BoundGenericMethodTypeConstantArgumentKind.Formal
                    && (actual.FormalOrdinal < 0
                        || actual.FormalDefinitionOrdinal < 0
                        || actual.FormalOwnerKind is not (
                            BoundGenericTypeFormalOwnerKind.GenericMethod
                            or BoundGenericTypeFormalOwnerKind
                                .SourceGenericType)
                        || actual.DomainType != PrimitiveType.Void
                        || actual.CarrierType != PrimitiveType.Void
                        || actual.Bits.Low != 0
                        || actual.Bits.High != 0))
                || actual.Kind is not (
                    BoundGenericMethodTypeConstantArgumentKind.Value
                    or BoundGenericMethodTypeConstantArgumentKind.Formal))
            {
                return false;
            }

            rewrittenConstants.Add(actual);
        }

        substituted = term with {
            TypeArguments = typeArguments.MoveToImmutable(),
            ConstantArguments = rewrittenConstants.MoveToImmutable(),
        };
        return true;
    }

    internal static bool TryRewrite(
        BoundGenericMethodTypeDefault typeDefault,
        Func<PrimitiveType, PrimitiveType> rewriteType,
        Func<int, int> rewriteFormalOrdinal,
        Func<int, int> rewriteSourceGenericDefinition,
        Func<BoundGenericNominalTypeIdentity,
            BoundGenericNominalTypeIdentity> rewriteNominalIdentity,
        out BoundGenericMethodTypeDefault rewritten)
    {
        rewritten = null!;
        if (typeDefault is null
            || typeDefault.FormalDependencies.IsDefault
            || !TryGetFormalDependencies(typeDefault.Term,
                out var originalDependencies)
            || !AreEqual(typeDefault.FormalDependencies,
                originalDependencies)
            || typeDefault.ConstantFormalDependencies.IsDefault
            || !TryGetConstantFormalDependencies(typeDefault.Term,
                out var originalConstantDependencies)
            || !AreEqual(typeDefault.ConstantFormalDependencies,
                originalConstantDependencies)
            || !TryRewrite(typeDefault.Term, rewriteType,
                rewriteFormalOrdinal, rewriteSourceGenericDefinition,
                rewriteNominalIdentity, out var term)
            || !TryGetFormalDependencies(term, out var dependencies)
            || !TryGetConstantFormalDependencies(term,
                out var constantDependencies))
        {
            return false;
        }

        rewritten = new BoundGenericMethodTypeDefault(term, dependencies) {
            ConstantFormalDependencies = constantDependencies,
        };
        return true;
    }

    private static bool AreEqual(ImmutableArray<int> left,
        ImmutableArray<int> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }
        return true;
    }

    internal static bool TryRewrite(
        BoundGenericMethodTypeTerm term,
        Func<PrimitiveType, PrimitiveType> rewriteType,
        Func<int, int> rewriteFormalOrdinal,
        Func<int, int> rewriteSourceGenericDefinition,
        Func<BoundGenericNominalTypeIdentity,
            BoundGenericNominalTypeIdentity> rewriteNominalIdentity,
        out BoundGenericMethodTypeTerm rewritten)
    {
        rewritten = null!;
        ArgumentNullException.ThrowIfNull(rewriteType);
        ArgumentNullException.ThrowIfNull(rewriteFormalOrdinal);
        ArgumentNullException.ThrowIfNull(rewriteSourceGenericDefinition);
        ArgumentNullException.ThrowIfNull(rewriteNominalIdentity);

        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        return TryRewrite(term, rewriteType, rewriteFormalOrdinal,
            rewriteSourceGenericDefinition, rewriteNominalIdentity,
            ref remaining, 0, out rewritten);
    }

    internal static bool TryGetFormalDependencies(
        BoundGenericMethodTypeTerm term,
        out ImmutableArray<int> dependencies)
    {
        var found = new SortedSet<int>();
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        if (!CollectFormalDependencies(term, found, ref remaining, 0))
        {
            dependencies = [];
            return false;
        }

        dependencies = [.. found];
        return true;
    }

    internal static bool TryGetConstantFormalDependencies(
        BoundGenericMethodTypeTerm term,
        out ImmutableArray<int> dependencies)
    {
        var found = new SortedSet<int>();
        var remaining = SemanticVerifier.MaximumAnalysisVisits;
        if (!CollectConstantFormalDependencies(term, found,
                ref remaining, 0))
        {
            dependencies = [];
            return false;
        }

        dependencies = [.. found];
        return true;
    }

    private static bool TrySubstitute(
        BoundGenericMethodTypeTerm term,
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        ref int remaining, int depth,
        out BoundGenericMethodTypeTerm substituted)
    {
        substituted = null!;
        if (!IsValidNode(term, ref remaining, depth))
        {
            return false;
        }

        if (term.Kind == BoundGenericMethodTypeTermKind.Formal)
        {
            if (term.FormalOrdinal < 0
                || term.ConcreteType != PrimitiveType.Void
                || term.SourceGenericTypeDefinitionIndex != -1
                || !term.TypeArguments.IsEmpty
                || term.NominalTypeIdentity is not null
                || term.IsGarbageCollected || term.IsInitialization
                || term.IsOptional || term.AllowsVoid
                || term.ElementCount != 0
                || term.CallingConvention != NativeCallingConvention.Native
                || term.ParameterCount != 0
                || (uint)term.FormalOrdinal >= (uint)typeArguments.Length)
            {
                return false;
            }

            // The replacement stays in the caller's formal context.
            substituted = typeArguments[term.FormalOrdinal];
            return true;
        }

        var arguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(term.TypeArguments.Length);
        foreach (var argument in term.TypeArguments)
        {
            if (!TrySubstitute(argument, typeArguments, ref remaining,
                depth + 1, out var rewritten))
            {
                return false;
            }
            arguments.Add(rewritten);
        }

        substituted = term with {
            TypeArguments = arguments.MoveToImmutable(),
        };
        return true;
    }

    private static bool TryRewrite(
        BoundGenericMethodTypeTerm term,
        Func<PrimitiveType, PrimitiveType> rewriteType,
        Func<int, int> rewriteFormalOrdinal,
        Func<int, int> rewriteSourceGenericDefinition,
        Func<BoundGenericNominalTypeIdentity,
            BoundGenericNominalTypeIdentity> rewriteNominalIdentity,
        ref int remaining, int depth,
        out BoundGenericMethodTypeTerm rewritten)
    {
        rewritten = null!;
        if (!IsValidNode(term, ref remaining, depth))
        {
            return false;
        }

        var concreteType = term.Kind
                == BoundGenericMethodTypeTermKind.Concrete
            ? rewriteType(term.ConcreteType) : term.ConcreteType;
        var formalOrdinal = term.Kind
                == BoundGenericMethodTypeTermKind.Formal
            ? rewriteFormalOrdinal(term.FormalOrdinal) : term.FormalOrdinal;
        var sourceGenericDefinition = term.Kind
                == BoundGenericMethodTypeTermKind.SourceGeneric
            ? rewriteSourceGenericDefinition(
                term.SourceGenericTypeDefinitionIndex)
            : term.SourceGenericTypeDefinitionIndex;
        var nominalIdentity
            = term.NominalTypeIdentity;
        if (term.Kind == BoundGenericMethodTypeTermKind.ImportedGeneric
            && nominalIdentity is BoundGenericNominalTypeIdentity identity)
        {
            nominalIdentity = rewriteNominalIdentity(identity);
        }

        if ((term.Kind == BoundGenericMethodTypeTermKind.Formal
                && formalOrdinal < 0)
            || (term.Kind
                    == BoundGenericMethodTypeTermKind.SourceGeneric
                && sourceGenericDefinition < 0)
            || (term.Kind
                    == BoundGenericMethodTypeTermKind.ImportedGeneric
                && nominalIdentity is not BoundGenericNominalTypeIdentity {
                    IsValid: true,
                }))
        {
            return false;
        }

        var arguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(term.TypeArguments.Length);
        foreach (var argument in term.TypeArguments)
        {
            if (!TryRewrite(argument, rewriteType, rewriteFormalOrdinal,
                rewriteSourceGenericDefinition, rewriteNominalIdentity,
                ref remaining, depth + 1, out var rewrittenChild))
            {
                return false;
            }
            arguments.Add(rewrittenChild);
        }

        var constantArguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeConstantArgument>(
                term.ConstantArguments.Length);
        foreach (var argument in term.ConstantArguments)
        {
            if (argument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                constantArguments.Add(argument with {
                    DomainType = rewriteType(argument.DomainType),
                    CarrierType = rewriteType(argument.CarrierType),
                });
                continue;
            }

            var formalDefinitionOrdinal
                = argument.FormalOwnerKind
                    == BoundGenericTypeFormalOwnerKind.SourceGenericType
                    ? rewriteSourceGenericDefinition(
                        argument.FormalDefinitionOrdinal)
                    : argument.FormalDefinitionOrdinal;
            if (formalDefinitionOrdinal < 0)
            {
                return false;
            }

            constantArguments.Add(argument with {
                FormalDefinitionOrdinal = formalDefinitionOrdinal,
            });
        }

        rewritten = term with {
            ConcreteType = concreteType,
            FormalOrdinal = formalOrdinal,
            SourceGenericTypeDefinitionIndex = sourceGenericDefinition,
            NominalTypeIdentity = nominalIdentity,
            TypeArguments = arguments.MoveToImmutable(),
            ConstantArguments = constantArguments.MoveToImmutable(),
        };
        return true;
    }

    private static bool TryCopyTerm(BoundGenericMethodTypeTerm term,
        ref int remaining, int depth,
        out BoundGenericMethodTypeTerm copy)
    {
        copy = null!;
        if (!IsValidNode(term, ref remaining, depth))
        {
            return false;
        }

        var arguments = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(term.TypeArguments.Length);
        foreach (var argument in term.TypeArguments)
        {
            if (!TryCopyTerm(argument, ref remaining, depth + 1,
                out var copiedChild))
            {
                return false;
            }
            arguments.Add(copiedChild);
        }

        copy = term with { TypeArguments = arguments.MoveToImmutable() };
        return true;
    }

    private static bool TryCopyTypeArguments(
        ImmutableArray<BoundGenericMethodTypeTerm> typeArguments,
        ref int remaining,
        out ImmutableArray<BoundGenericMethodTypeTerm> copies)
    {
        copies = [];
        if (typeArguments.IsDefault || typeArguments.Length > remaining)
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<
            BoundGenericMethodTypeTerm>(typeArguments.Length);
        foreach (var argument in typeArguments)
        {
            if (!TryCopyTerm(argument, ref remaining, 0,
                out var copiedArgument))
            {
                return false;
            }
            builder.Add(copiedArgument);
        }

        copies = builder.MoveToImmutable();
        return true;
    }

    private static bool CollectFormalDependencies(
        BoundGenericMethodTypeTerm term, SortedSet<int> dependencies,
        ref int remaining, int depth)
    {
        if (!IsValidNode(term, ref remaining, depth))
        {
            return false;
        }
        if (term.Kind == BoundGenericMethodTypeTermKind.Formal
            && !dependencies.Add(term.FormalOrdinal))
        {
            return true;
        }

        foreach (var argument in term.TypeArguments)
        {
            if (!CollectFormalDependencies(argument, dependencies,
                ref remaining, depth + 1))
            {
                return false;
            }
        }
        return true;
    }

    private static bool CollectConstantFormalDependencies(
        BoundGenericMethodTypeTerm term, SortedSet<int> dependencies,
        ref int remaining, int depth)
    {
        if (!IsValidNode(term, ref remaining, depth))
        {
            return false;
        }

        foreach (var argument in term.ConstantArguments)
        {
            if (argument.Kind
                    == BoundGenericMethodTypeConstantArgumentKind.Formal)
            {
                _ = dependencies.Add(argument.FormalOrdinal);
            }
        }

        foreach (var argument in term.TypeArguments)
        {
            if (!CollectConstantFormalDependencies(argument, dependencies,
                ref remaining, depth + 1))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidNode(BoundGenericMethodTypeTerm? term,
        ref int remaining, int depth)
    {
        if (term is null || depth > SemanticVerifier.MaximumDepth
            || --remaining < 0 || term.TypeArguments.IsDefault
            || term.TypeArguments.Length > remaining
            || term.ConstantArguments.IsDefault
            || term.ConstantArguments.Length > remaining
            || (term.Kind != BoundGenericMethodTypeTermKind.Span
                && term.IsElementWritable))
        {
            return false;
        }

        foreach (var argument in term.ConstantArguments)
        {
            if (argument is null)
            {
                return false;
            }

            if (argument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Value)
            {
                if (argument.FormalOrdinal != -1
                    || argument.FormalDefinitionOrdinal != -1
                    || argument.DomainType == PrimitiveType.Void
                    || argument.CarrierType == PrimitiveType.Void
                    || !argument.Bits.IsCanonicalFor(
                        argument.CarrierType))
                {
                    return false;
                }
            }
            else if (argument.Kind
                == BoundGenericMethodTypeConstantArgumentKind.Formal)
            {
                if (argument.FormalOrdinal < 0
                    || argument.FormalDefinitionOrdinal < 0
                    || argument.FormalOwnerKind is not (
                        BoundGenericTypeFormalOwnerKind.GenericMethod
                        or BoundGenericTypeFormalOwnerKind.SourceGenericType)
                    || argument.DomainType != PrimitiveType.Void
                    || argument.CarrierType != PrimitiveType.Void
                    || argument.Bits.Low != 0 || argument.Bits.High != 0)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        var hasNoTypeTermFlags = !term.IsWritable
            && !term.IsGarbageCollected && !term.IsInitialization
            && !term.IsOptional && !term.IsElementWritable;
        var hasNoNativeSignature = term.CallingConvention
                == NativeCallingConvention.Native
            && term.ParameterCount == 0;
        var hasNoArrayExtent = term.ElementCount == 0;
        var noNominalIdentity = term.NominalTypeIdentity is null;
        var hasNoConstantArguments = term.ConstantArguments.IsEmpty;
        return term.Kind switch {
            BoundGenericMethodTypeTermKind.Concrete =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.TypeArguments.IsEmpty && noNominalIdentity
                && hasNoConstantArguments
                && hasNoTypeTermFlags && hasNoNativeSignature
                && hasNoArrayExtent
                && (!term.AllowsVoid
                    || term.ConcreteType == PrimitiveType.Void),
            BoundGenericMethodTypeTermKind.Formal =>
                term.FormalOrdinal >= 0
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.IsEmpty && noNominalIdentity
                && hasNoConstantArguments
                && !term.IsGarbageCollected && !term.IsInitialization
                && !term.IsOptional && !term.AllowsVoid
                && hasNoNativeSignature && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.CheckedReference =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.Length == 1 && noNominalIdentity
                && hasNoConstantArguments
                && !(term.IsGarbageCollected && term.IsInitialization)
                && !term.AllowsVoid && hasNoNativeSignature
                && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.SourceGeneric =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex >= 0
                && term.ConcreteType == PrimitiveType.Void
                && noNominalIdentity
                && hasNoTypeTermFlags
                && !term.AllowsVoid && hasNoNativeSignature
                && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.OptionalValue =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.Length == 1 && noNominalIdentity
                && hasNoConstantArguments
                && hasNoTypeTermFlags && !term.AllowsVoid
                && hasNoNativeSignature && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.ImportedGeneric =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.NominalTypeIdentity
                    is BoundGenericNominalTypeIdentity { IsValid: true }
                && hasNoTypeTermFlags && !term.AllowsVoid
                && hasNoNativeSignature && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.NativePointer =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.Length == 1 && noNominalIdentity
                && hasNoConstantArguments
                && !term.IsGarbageCollected && !term.IsInitialization
                && !term.IsOptional && !term.AllowsVoid
                && hasNoNativeSignature && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.NativeFixedArray =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.Length == 1 && noNominalIdentity
                && hasNoConstantArguments
                && hasNoTypeTermFlags && !term.AllowsVoid
                && hasNoNativeSignature,
            BoundGenericMethodTypeTermKind.Span =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.Length == 1
                && noNominalIdentity && hasNoConstantArguments
                && !term.IsGarbageCollected && !term.IsOptional
                && !(term.TypeArguments[0].Kind
                        == BoundGenericMethodTypeTermKind.Concrete
                    && (term.TypeArguments[0].ConcreteType
                            == PrimitiveType.Void
                        || term.TypeArguments[0].AllowsVoid))
                && (!term.IsElementWritable
                    || term.TypeArguments[0].Kind
                        == BoundGenericMethodTypeTermKind.Formal)
                && !term.AllowsVoid && hasNoNativeSignature
                && hasNoArrayExtent,
            BoundGenericMethodTypeTermKind.NativeFunctionPointer =>
                term.FormalOrdinal == -1
                && term.SourceGenericTypeDefinitionIndex == -1
                && term.ConcreteType == PrimitiveType.Void
                && term.TypeArguments.Length
                    == (long)term.ParameterCount + 1
                && term.ParameterCount >= 0
                && noNominalIdentity && hasNoTypeTermFlags
                && hasNoConstantArguments
                && !term.AllowsVoid && hasNoArrayExtent
                && term.CallingConvention is
                    NativeCallingConvention.Native
                    or NativeCallingConvention.Unmanaged
                    or NativeCallingConvention.Cdecl
                    or NativeCallingConvention.Stdcall
                    or NativeCallingConvention.Vectorcall,
            _ => false,
        };
    }
}
