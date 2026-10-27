// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class NativeCoreRegistrationTests
{
    [Test]
    public void RegistrationRetainsSelectedEnumAndSchemaIdentities()
    {
        var registration = CreateRegistration();

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.CallingConventions.Members.Select(static member =>
            (member.Name, member.Value)), Is.EqualTo(new[]
            {
                ("Unmanaged", 1),
                ("Cdecl", 2),
                ("Stdcall", 3),
                ("Vectorcall", 4),
            }));
        Assert.That(registration.NativeImport.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
        Assert.That(registration.NativeCallable.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
        Assert.That(registration.NativeCallConv.CallingConventionEnum,
            Is.EqualTo(registration.CallingConventions.Identity));
    }

    [Test]
    public void RegistrationRejectsWrongEnumValuesAndSchemaOwners()
    {
        var registration = CreateRegistration();
        var invalidMembers = registration.CallingConventions.Members.SetItem(1,
            registration.CallingConventions.Members[1] with { Value = 1 });
        var invalidEnum = registration.CallingConventions with { Members = invalidMembers };

        Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.UsingModuleIdentity, invalidEnum, registration.ResolutionModes,
            registration.ResolutionPolicies, registration.NativeImport,
            registration.NativeCallable, registration.NativeCallConv, registration.NativeExport));

        var invalidImport = registration.NativeImport with {
            CallingConventionEnum = registration.ResolutionModes.Identity,
        };
        Assert.Throws<ArgumentException>(() => NativeCoreRegistrationSet.Create(
            registration.UsingModuleIdentity, registration.CallingConventions,
            registration.ResolutionModes, registration.ResolutionPolicies, invalidImport,
            registration.NativeCallable, registration.NativeCallConv, registration.NativeExport));
    }

    [Test]
    public void CompilationReferenceCarriesTheExplicitNativeCoreRegistration()
    {
        var registration = CreateRegistration();
        var reference = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: registration);

        Assert.That(reference.NativeCoreRegistration, Is.SameAs(registration));
        var compilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static NativeCoreRegistrationSet CreateRegistration()
    {
        var conventions = CreateEnum("NativeCallingConvention",
            "Terrum.Interop.NativeCallingConvention",
            ("Unmanaged", 1), ("Cdecl", 2), ("Stdcall", 3), ("Vectorcall", 4));
        var resolutionModes = CreateEnum("NativeImportResolutionMode",
            "Terrum.Interop.NativeImportResolutionMode", ("Link", 1), ("Dynamic", 2));
        var resolutionPolicies = CreateEnum("NativeImportResolutionPolicy",
            "Terrum.Interop.NativeImportResolutionPolicy",
            ("Default", 1), ("CompatibilitySearch", 2));

        return NativeCoreRegistrationSet.Create("consumer.module", conventions, resolutionModes,
            resolutionPolicies,
            CreateSchema("NativeImport", "Terrum.Interop.NativeImport",
                CoreNativeAttributeRole.NativeImport, conventions.Identity,
                resolutionModes.Identity, resolutionPolicies.Identity),
            CreateSchema("NativeCallable", "Terrum.Interop.NativeCallable",
                CoreNativeAttributeRole.NativeCallable, conventions.Identity),
            CreateSchema("NativeCallConv", "Terrum.Interop.NativeCallConv",
                CoreNativeAttributeRole.NativeCallConv, conventions.Identity),
            CreateSchema("NativeExport", "Terrum.Interop.NativeExport",
                CoreNativeAttributeRole.NativeExport));
    }

    private static CoreEnumDefinition CreateEnum(string exportKey,
        string qualifiedName, params (string Name, int Value)[] members)
    {
        var enumIdentity = Identity("core.native", exportKey, CoreDeclarationKind.EnumType);
        var memberDefinitions = ImmutableArray.CreateBuilder<CoreEnumMemberDefinition>(members.Length);
        foreach (var member in members)
        {
            memberDefinitions.Add(new CoreEnumMemberDefinition(
                Identity("core.native", $"{exportKey}.{member.Name}", CoreDeclarationKind.EnumMember),
                enumIdentity, member.Name, member.Value, IsPublic: true));
        }

        return new CoreEnumDefinition(enumIdentity, qualifiedName, PrimitiveType.Int32,
            memberDefinitions.MoveToImmutable(), GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsFlags: false);
    }

    private static CoreNativeAttributeSchema CreateSchema(string exportKey,
        string qualifiedName, CoreNativeAttributeRole role,
        CoreDeclarationIdentity? callingConventionEnum = null,
        CoreDeclarationIdentity? resolutionModeEnum = null,
        CoreDeclarationIdentity? resolutionPolicyEnum = null) =>
        new(Identity("core.native", exportKey, CoreDeclarationKind.AttributeSchema),
            qualifiedName, role, callingConventionEnum, resolutionModeEnum,
            resolutionPolicyEnum, GenericArity: 0, IsPublic: true,
            IsPrimary: true, IsRepeatable: false);

    private static CoreDeclarationIdentity Identity(string provider, string exportKey,
        CoreDeclarationKind kind) => new(provider, exportKey, kind);
}
