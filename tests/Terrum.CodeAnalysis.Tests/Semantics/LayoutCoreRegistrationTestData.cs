// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Collections.Immutable;
using Terrum.CodeAnalysis.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics;

internal sealed record LayoutCoreRegistrationParts(
    CoreEnumDefinition LayoutKinds,
    CoreLayoutAttributeSchema Layout,
    CoreLayoutAttributeSchema Blittable)
{
    internal ImmutableArray<CoreProviderDeclarationSnapshot> Declarations =>
        [
            CoreProviderDeclarationSnapshot.From(LayoutKinds),
            CoreProviderDeclarationSnapshot.From(Layout),
            CoreProviderDeclarationSnapshot.From(Blittable),
        ];
}

internal static class LayoutCoreRegistrationTestData
{
    internal static LayoutCoreRegistrationSet Create()
    {
        const string providerIdentity = "sample.layout-core";
        var parts = CreateDeclarations(providerIdentity);
        var bindings = CoreProviderModuleBindings.Create("sample.consumer", [
            new CoreProviderModuleOrigin(providerIdentity, "revision-1",
                new CoreProviderSchemaSelection("layout-registration", 1),
                GoverningSemanticEdition: 1, TargetRequirement: 0,
                RuntimePolicy: 0),
        ], parts.Declarations);
        return CreateForBindings(bindings, parts);
    }

    internal static LayoutCoreRegistrationParts CreateDeclarations(
        string providerIdentity)
    {
        var layoutKindsIdentity = new CoreDeclarationIdentity(providerIdentity,
            "LayoutKind", CoreDeclarationKind.EnumType);
        var members = ImmutableArray.Create(
            CreateMember(layoutKindsIdentity, "Auto", 0),
            CreateMember(layoutKindsIdentity, "Sequential", 1),
            CreateMember(layoutKindsIdentity, "Union", 2),
            CreateMember(layoutKindsIdentity, "Transparent", 3));
        var layoutKinds = new CoreEnumDefinition(layoutKindsIdentity,
            "LayoutKind", PrimitiveType.Int32, members, GenericArity: 0,
            IsPublic: true, IsPrimary: true, IsFlags: false);
        var layout = new CoreLayoutAttributeSchema(
            new CoreDeclarationIdentity(providerIdentity, "Layout",
                CoreDeclarationKind.AttributeSchema),
            "Layout",
            [
                new("Kind", CoreLayoutAttributeArgumentKind.LayoutKind,
                    layoutKindsIdentity, IsRequired: true,
                    AllowsPositional: true, DefaultUInt32: null),
                new("Packing", CoreLayoutAttributeArgumentKind.Unsigned32,
                    null, IsRequired: false, AllowsPositional: false,
                    DefaultUInt32: 16),
                new("Alignment", CoreLayoutAttributeArgumentKind.Unsigned32,
                    null, IsRequired: false, AllowsPositional: false,
                    DefaultUInt32: null),
            ],
            CoreLayoutAttributeTargets.InlineStruct,
            IsPrimaryTargetOnly: true, GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsRepeatable: false);
        var blittable = new CoreLayoutAttributeSchema(
            new CoreDeclarationIdentity(providerIdentity, "Blittable",
                CoreDeclarationKind.AttributeSchema),
            "Blittable", [],
            CoreLayoutAttributeTargets.InlineStruct
                | CoreLayoutAttributeTargets.IntegerBackedEnum,
            IsPrimaryTargetOnly: true, GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsRepeatable: false);
        return new LayoutCoreRegistrationParts(layoutKinds, layout,
            blittable);
    }

    internal static LayoutCoreRegistrationSet CreateForBindings(
        CoreProviderModuleBindings bindings,
        LayoutCoreRegistrationParts parts)
        => LayoutCoreRegistrationSet.Create(bindings, parts.LayoutKinds,
            parts.Layout, parts.Blittable);

    private static CoreEnumMemberDefinition CreateMember(
        CoreDeclarationIdentity declaringEnum, string name, int value)
    {
        var identity = new CoreDeclarationIdentity(
            declaringEnum.ProviderIdentity, $"LayoutKind.{name}",
            CoreDeclarationKind.EnumMember);
        return new CoreEnumMemberDefinition(identity, declaringEnum, name,
            value, IsPublic: true) {
            DeclarationKey = $"layout-kind/member/{name}",
        };
    }
}
