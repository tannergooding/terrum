// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmDefinitionResolver
{
    public static (int Module, uint Visibility, int OwnerType, int Body) Resolve(
        uint reference, TrRmTypeDefTableReader types, TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties, TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs)
    {
        var row = (int)(reference & 0xFF_FFFF);
        return (reference >> 24) switch
        {
            0x13 when row > 0 && row <= types.Count
                => (types.GetOwnerModule(row), types.GetFlags(row) & 3, 0, 0),
            0x20 when row > 0 && row <= associatedTypeDefs.Count
                => ResolveAssociatedType(row, types, interfaces, associatedTypeDefs),
            0x16 when row > 0 && row <= methods.Count
                => (methods.GetOwnerModule(row), methods.GetFlags(row) & 3,
                    methods.GetOwnerType(row), methods.GetBodyRow(row)),
            0x1E when row > 0 && row <= properties.Count
                => (types.GetOwnerModule(properties.GetOwnerType(row)),
                    properties.GetFlags(row) & 3, properties.GetOwnerType(row),
                    methods.GetBodyRow(properties.GetGetterMethod(row))),
            _ => throw new InvalidDataException("Invalid TRRM definition reference."),
        };
    }

    private static (int Module, uint Visibility, int OwnerType, int Body)
        ResolveAssociatedType(int row, TrRmTypeDefTableReader types,
            TrRmInterfaceDefTableReader interfaces,
            TrRmAssociatedTypeDefTableReader associatedTypeDefs)
    {
        var ownerInterface = associatedTypeDefs.GetOwnerInterfaceDef(row);
        var ownerType = interfaces.GetOwnerTypeDef(ownerInterface);
        return (types.GetOwnerModule(ownerType), types.GetFlags(ownerType) & 3,
            ownerType, 0);
    }
}
