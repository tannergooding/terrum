// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmConstantSourceValueValidator
{
    internal static void ValidateFrozenScalar(int constantSourceRow,
        uint expectedTypeSpecRef, int ownerModule,
        TrRmConstantSourceTableReader constantSources,
        TrRmFrozenValueNodeTableReader frozenValueNodes,
        TrRmBlobTableReader blobs, TrRmTypeSpecTableReader typeSpecs,
        TrRmFeatureTableReader features, TrRmModuleTableReader modules)
    {
        var sourceKind = constantSources.GetKind(constantSourceRow);
        if (sourceKind != 1)
        {
            throw new System.NotSupportedException(
                "This selected use supports only frozen scalar ConstantSource rows.");
        }
        if (!TrRmFeatureManifestValidator.Requires(features, modules, ownerModule,
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId))
        {
            throw new System.NotSupportedException(
                "A selected frozen scalar value requires TypedFrozenValues revision 1.");
        }

        var nodeRow = constantSources.GetPayloadReference(constantSourceRow);
        if (frozenValueNodes.GetKind(checked((int)nodeRow)) != 1)
        {
            throw new System.NotSupportedException(
                "This selected use supports only frozen scalar values.");
        }

        var mapType = typeSpecs.GetTypeSpecRef(expectedTypeSpecRef);
        var nodeType = typeSpecs.GetTypeSpecRef(
            frozenValueNodes.GetTypeSpecRef(checked((int)nodeRow)));
        if (mapType.Form != 0x01 || mapType.Qualifiers != 0
            || nodeType.Form != 0x01 || nodeType.Qualifiers != 0
            || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(mapType.TypeCode)
            || !TrRmFrozenValueNodeTableReader.IsRegisteredScalarTypeCode(nodeType.TypeCode))
        {
            throw new System.NotSupportedException(
                "This selected use requires an unqualified registered scalar type.");
        }
        if (mapType.TypeCode != nodeType.TypeCode
            || mapType.ArgumentCode != nodeType.ArgumentCode)
        {
            throw new InvalidDataException("ConstantMap type does not match its frozen value.");
        }

        frozenValueNodes.ValidateScalarValue(checked((int)nodeRow), blobs);
    }
}
