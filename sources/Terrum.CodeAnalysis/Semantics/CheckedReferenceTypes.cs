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
    internal static bool IsType(PrimitiveType type) => (int)type is < 0 and not int.MinValue;

    internal static int Index(PrimitiveType type) => checked(-(int)type - 1);

    internal static PrimitiveType Type(int index) => (PrimitiveType)checked(-index - 1);
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
        ImmutableArray<CheckedReferenceTypeDefinition> referenceTypes) =>
        CheckedReferenceTypes.IsType(type)
        && (uint)CheckedReferenceTypes.Index(type) < (uint)referenceTypes.Length;

    internal static bool IsSupportedWriteValueType(PrimitiveType type,
        IReadOnlyList<BoundStruct> structures, int enumDefinitionCount)
    {
        var states = new byte[structures.Count];
        return IsSupportedWriteValueType(type, structures, enumDefinitionCount, states, 0);
    }

    internal static bool IsSupportedReferenceStoreValueType(
        PrimitiveType type, IReadOnlyList<BoundStruct> structures,
        IReadOnlyList<BoundClass> classes, int enumDefinitionCount)
    {
        if (IsSupportedWriteValueType(type, structures, enumDefinitionCount)
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
        IReadOnlyList<BoundStruct> structures, int enumDefinitionCount, byte[] states, int depth)
    {
        if (type == PrimitiveType.TokenSpan)
        {
            return true;
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
                states, depth + 1))
            {
                return false;
            }
        }
        states[index] = 2;
        return true;
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
