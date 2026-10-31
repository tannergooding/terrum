// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.IO;

namespace Terrum.CodeAnalysis.Modules;

internal static class TrRmDefinitionResolver
{
    public static (int Module, uint Visibility, int OwnerType, int Body) Resolve(
        uint reference, TrRmTypeDefTableReader types, TrRmFieldDefTableReader fields,
        TrRmMethodDefTableReader methods,
        TrRmPropertyDefTableReader properties, TrRmInterfaceDefTableReader interfaces,
        TrRmAssociatedTypeDefTableReader associatedTypeDefs,
        TrRmAssociatedConstDefTableReader associatedConstDefs,
        TrRmArtifactSchema schema,
        TrRmStaticFieldTableReader staticFields = default,
        TrRmStaticFieldContractTableReader staticFieldContracts = default,
        TrRmIntrinsicRegistrationTableReader intrinsicRegistrations = default)
    {
        var row = (int)(reference & 0xFF_FFFF);
        return (reference >> 24) switch
        {
            0x13 when row > 0 && row <= types.Count
                => (types.GetOwnerModule(row), types.GetFlags(row) & 3, 0, 0),
            0x14 when row > 0 && row <= fields.Count
                => ResolveField(row, types, fields),
            0x20 when row > 0 && row <= associatedTypeDefs.Count
                => ResolveAssociatedType(row, types, interfaces, associatedTypeDefs),
            0x25 when schema.UsesGeneralPredicateGraphs()
                && row > 0 && row <= associatedConstDefs.Count
                    => ResolveAssociatedConstant(row, types, interfaces,
                        associatedConstDefs),
            0x24 when schema.UsesGeneralPredicateGraphs()
                && row > 0 && row <= staticFields.Count
                    => ResolveStaticField(row, types, staticFields,
                        staticFieldContracts),
            0x26 when schema.UsesGeneralPredicateGraphs()
                && row > 0 && row <= intrinsicRegistrations.Count
                    => ResolveIntrinsicRegistration(
                        row, intrinsicRegistrations),
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
        ResolveField(int row, TrRmTypeDefTableReader types,
            TrRmFieldDefTableReader fields)
    {
        var ownerType = fields.GetOwnerTypeDef(row);
        return (types.GetOwnerModule(ownerType), fields.GetFlags(row) & 3,
            ownerType, 0);
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

    private static (int Module, uint Visibility, int OwnerType, int Body)
        ResolveAssociatedConstant(int row, TrRmTypeDefTableReader types,
            TrRmInterfaceDefTableReader interfaces,
            TrRmAssociatedConstDefTableReader associatedConstDefs)
    {
        var ownerInterface = associatedConstDefs.GetOwnerInterfaceDef(row);
        var ownerType = interfaces.GetOwnerTypeDef(ownerInterface);
        return (types.GetOwnerModule(ownerType), types.GetFlags(ownerType) & 3,
            ownerType, 0);
    }

    private static (int Module, uint Visibility, int OwnerType, int Body)
        ResolveStaticField(int row, TrRmTypeDefTableReader types,
            TrRmStaticFieldTableReader staticFields,
            TrRmStaticFieldContractTableReader staticFieldContracts)
    {
        var ownerType = staticFields.GetOwnerTypeDefRow(row);
        var contract = staticFieldContracts.GetRowForStaticField(row);
        return (types.GetOwnerModule(ownerType),
            staticFieldContracts.GetVisibility(contract), ownerType, 0);
    }

    private static (int Module, uint Visibility, int OwnerType, int Body)
        ResolveIntrinsicRegistration(int row,
            TrRmIntrinsicRegistrationTableReader registrations)
        => (registrations.GetOwnerModuleRow(row),
            registrations.GetVisibility(row), 0, 0);
}
