using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Terrum.CodeAnalysis.Semantics;

internal sealed record CheckedReferenceTypeDefinition(
    PrimitiveType ReferentType,
    bool IsWritable,
    bool IsGarbageCollected,
    bool IsInitialization,
    bool IsOptional);

internal static class CheckedReferenceTypes
{
    internal static bool IsType(PrimitiveType type) =>
        (int)type is < 0 and >= -SemanticVerifier.MaximumAnalysisVisits;

    internal static int Index(PrimitiveType type) => checked(-(int)type - 1);

    internal static PrimitiveType Type(int index) => (PrimitiveType)checked(-index - 1);
}

internal static class SourceOptionalTypes
{
    internal static bool IsOptional(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (ManagedClassTypes.IsOptional(type))
        {
            return true;
        }
        return IsOptionalReference(type, referenceTypes);
    }

    internal static bool IsOptionalReference(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes) =>
        CheckedReferenceTypeContract.IsValidType(type, referenceTypes)
        && referenceTypes[CheckedReferenceTypes.Index(type)].IsOptional;

    internal static bool IsSupported(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes,
        IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundClass> classes,
        int enumDefinitionCount,
        string? nativeLoweringAgreement)
    {
        if (ManagedClassTypes.IsOptional(type))
        {
            return true;
        }
        if (nativeLoweringAgreement is not (
                CompilationOptions.WindowsX64NormalBackingAgreement
                or CompilationOptions.WindowsX64RootlessReferencesAgreement)
            || !TryGetRequired(type, referenceTypes, out var requiredType)
            || !CheckedReferenceTypes.IsType(requiredType))
        {
            return false;
        }

        var requiredReference = referenceTypes[CheckedReferenceTypes.Index(requiredType)];
        return !requiredReference.IsGarbageCollected && !requiredReference.IsInitialization
            && !requiredReference.IsOptional
            && CheckedReferenceTypeContract.IsSupportedReferenceStoreValueType(
                requiredType, structures, classes, enumDefinitionCount,
                [.. referenceTypes]);
    }

    internal static bool TryGetRequired(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes,
        out PrimitiveType requiredType)
    {
        if (ManagedClassTypes.IsOptional(type))
        {
            requiredType = ManagedClassTypes.Required(type);
            return true;
        }
        if (!IsOptionalReference(type, referenceTypes))
        {
            requiredType = default;
            return false;
        }

        var requiredDefinition = referenceTypes[CheckedReferenceTypes.Index(type)] with
        {
            IsOptional = false,
        };
        for (var index = 0; index < referenceTypes.Count; index++)
        {
            if (referenceTypes[index] == requiredDefinition)
            {
                requiredType = CheckedReferenceTypes.Type(index);
                return true;
            }
        }

        requiredType = default;
        return false;
    }

    internal static bool IsReadonlyProjection(
        PrimitiveType source,
        PrimitiveType target,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (ManagedClassTypes.IsWritable(source))
        {
            return ManagedClassTypes.Readonly(source) == target;
        }
        if (!CheckedReferenceTypeContract.IsValidType(source, referenceTypes))
        {
            return false;
        }

        var sourceReference = referenceTypes[CheckedReferenceTypes.Index(source)];
        if (!sourceReference.IsWritable)
        {
            return false;
        }
        var readonlyDefinition = sourceReference with { IsWritable = false };
        for (var index = 0; index < referenceTypes.Count; index++)
        {
            if (referenceTypes[index] == readonlyDefinition)
            {
                return CheckedReferenceTypes.Type(index) == target;
            }
        }
        return false;
    }
}

internal static class CheckedReferenceTypeContract
{
    internal static bool IsValidTable(ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (referenceTypes.IsDefault || referenceTypes.Length > SemanticVerifier.MaximumAnalysisVisits)
        {
            return false;
        }

        var states = new byte[referenceTypes.Length];
        for (var index = 0; index < referenceTypes.Length; index++)
        {
            if (!IsValidReference(index, referenceTypes, states, 0))
            {
                return false;
            }
        }
        return true;
    }

    internal static bool IsValidType(
        PrimitiveType type,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes) =>
        CheckedReferenceTypes.IsType(type)
        && (uint)CheckedReferenceTypes.Index(type) < (uint)referenceTypes.Count;

    internal static bool IsImplicitReferenceValueType(
        PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (DirectNativeImportContract.IsScalar(type)
            || NativeFunctionPointerTypes.IsType(type))
        {
            return true;
        }
        if (IsValidType(type, referenceTypes))
        {
            var reference = referenceTypes[CheckedReferenceTypes.Index(type)];
            return !reference.IsGarbageCollected
                && !reference.IsInitialization
                && !reference.IsOptional;
        }

        var typeValue = (int)type;
        var structureIndex = typeValue >= 100 ? typeValue - 100 : -1;
        return structureIndex >= 0
            && (uint)structureIndex < (uint)structures.Count
            && !structures[structureIndex].IsUnique;
    }

    internal static bool IsCanonicalImplicitReferenceType(
        PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (!IsValidType(type, referenceTypes))
        {
            return false;
        }

        var reference = referenceTypes[CheckedReferenceTypes.Index(type)];
        return !reference.IsWritable
            && !reference.IsGarbageCollected
            && !reference.IsInitialization
            && !reference.IsOptional
            && IsImplicitReferenceValueType(reference.ReferentType,
                structures, referenceTypes);
    }

    internal static bool IsSupportedWriteValueType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, int enumDefinitionCount)
    {
        return IsSupportedWriteValueType(type, structures, enumDefinitionCount,
            []);
    }

    private static bool IsSupportedWriteValueType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, int enumDefinitionCount,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes)
    {
        var states = new byte[structures.Count];
        return IsSupportedWriteValueType(type, structures, enumDefinitionCount,
            referenceTypes, states, 0);
    }

    internal static bool IsSupportedReferenceStoreValueType(
        PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundClass> classes, int enumDefinitionCount,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes)
    {
        if (SourceOptionalTypes.IsOptionalReference(type, referenceTypes))
        {
            if (!SourceOptionalTypes.TryGetRequired(
                type, referenceTypes, out var requiredType))
            {
                return false;
            }
            type = requiredType;
        }

        if (IsSupportedWriteValueType(type, structures, enumDefinitionCount,
                referenceTypes)
            || type == PrimitiveType.OwnedInt32Buffer)
        {
            return true;
        }

        var typeValue = (int)type;
        var structureIndex = typeValue >= 100 ? typeValue - 100 : -1;
        if (structureIndex >= 0 && structureIndex < structures.Count
            && structures[structureIndex].IsUnique)
        {
            return true;
        }

        if (type is PrimitiveType.ManagedNode or PrimitiveType.WriteManagedNode
            or PrimitiveType.OptionalManagedNode or PrimitiveType.OptionalWriteManagedNode)
        {
            return true;
        }
        if (ManagedClassTypes.IsClass(type) || ManagedClassTypes.IsOptional(type))
        {
            var classType = ManagedClassTypes.IsOptional(type)
                ? ManagedClassTypes.Required(type) : type;
            return ManagedClassTypes.ClassIndex(classType) < classes.Count;
        }
        return false;
    }

    private static bool IsSupportedWriteValueType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, int enumDefinitionCount,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        byte[] states, int depth)
    {
        if (type == PrimitiveType.TokenSpan)
        {
            return true;
        }
        if (CheckedReferenceTypes.IsType(type))
        {
            return IsSupportedReferenceDescriptorType(type, referenceTypes, depth);
        }
        if (BoundEnumTypes.IsType(type))
        {
            return BoundEnumTypes.IsValidType(type, enumDefinitionCount);
        }
        if (PrimitiveTypeFacts.ScalarWidth(type) > 0)
        {
            return true;
        }

        var index = (int)type - 100;
        if (depth > SemanticVerifier.MaximumDepth || index < 0 || index >= structures.Count
            || index >= BoundEnumTypes.First - 100 || states[index] == 1)
        {
            return false;
        }
        if (states[index] == 2)
        {
            return true;
        }

        var structure = structures[index];
        if (structure is null || structure.IsUnique || structure.CleanupMethod is not null
            || structure.Fields.IsDefaultOrEmpty)
        {
            return false;
        }

        states[index] = 1;
        foreach (var field in structure.Fields)
        {
            if (!IsSupportedWriteValueType(field.Type, structures, enumDefinitionCount,
                referenceTypes, states, depth + 1))
            {
                return false;
            }
        }
        states[index] = 2;
        return true;
    }

    private static bool IsSupportedReferenceDescriptorType(
        PrimitiveType type,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        int depth)
    {
        if (depth > SemanticVerifier.MaximumDepth
            || !IsValidType(type, referenceTypes))
        {
            return false;
        }

        var reference = referenceTypes[CheckedReferenceTypes.Index(type)];
        return !reference.IsGarbageCollected && !reference.IsInitialization
            && (!CheckedReferenceTypes.IsType(reference.ReferentType)
                || IsSupportedReferenceDescriptorType(
                    reference.ReferentType, referenceTypes, depth + 1));
    }

    private static bool IsValidReference(
        int index,
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes,
        byte[] states,
        int depth)
    {
        if ((uint)index >= (uint)referenceTypes.Length || depth > SemanticVerifier.MaximumDepth
            || states[index] == 1)
        {
            return false;
        }
        if (states[index] == 2)
        {
            return true;
        }

        var referenceType = referenceTypes[index];
        if (referenceType is null || referenceType.ReferentType == PrimitiveType.Void
            || (referenceType.IsGarbageCollected && referenceType.IsInitialization))
        {
            return false;
        }

        states[index] = 1;
        if (CheckedReferenceTypes.IsType(referenceType.ReferentType)
            && !IsValidReference(CheckedReferenceTypes.Index(referenceType.ReferentType),
                referenceTypes, states, depth + 1))
        {
            return false;
        }
        states[index] = 2;
        return true;
    }
}
