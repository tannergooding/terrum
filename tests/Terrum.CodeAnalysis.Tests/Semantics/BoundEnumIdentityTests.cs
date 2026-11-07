using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class BoundEnumIdentityTests
{
    [Test]
    public void PrivateEnumOccurrencesKeepOwnerScopedMemberKeysDistinct()
    {
        var first = CreatePrivateEnum(BoundEnumTypes.Type(0), "first");
        var second = CreatePrivateEnum(BoundEnumTypes.Type(1), "second");
        var coreIdentity = new CoreDeclarationIdentity("module",
            "Enums.Color", CoreDeclarationKind.EnumType);
        var core = new BoundEnumDefinition(
            new BoundEnumDeclarationIdentity.Core(coreIdentity),
            "Enums.Color", PrimitiveType.Int32, [
                new BoundEnumMemberDefinition(
                    new BoundEnumMemberIdentity.Core(
                        new CoreDeclarationIdentity("module",
                            "member.red",
                            CoreDeclarationKind.EnumMember)),
                    new BoundEnumDeclarationIdentity.Core(coreIdentity),
                    "Red", 1, true, "member.red"),
            ], true, true, false);

        Assert.That(BoundEnumTypes.IsValidTable([first, second]), Is.True);
        Assert.That(first.Identity, Is.Not.EqualTo(second.Identity));
        Assert.That(first.Members[0].Identity,
            Is.Not.EqualTo(second.Members[0].Identity));
        Assert.That(BoundEnumTypes.AreEquivalent(first, core), Is.False);
        Assert.That(first.Members[0].DeclarationKey,
            Is.EqualTo(second.Members[0].DeclarationKey));
    }

    [Test]
    public void PrivateEnumMemberMustReferToItsExactOccurrence()
    {
        var first = CreatePrivateEnum(BoundEnumTypes.Type(0), "first");
        var second = CreatePrivateEnum(BoundEnumTypes.Type(1), "second");
        var mismatched = first with {
            Members = [first.Members[0] with {
                Identity = second.Members[0].Identity,
            }],
        };

        Assert.That(BoundEnumTypes.IsValidTable([mismatched]), Is.False);
    }

    [Test]
    public void SameNamedPrivateEnumOwnersRemainAmbiguousDuringSourceLookup()
    {
        var first = CreatePrivateEnum(BoundEnumTypes.Type(0), "first");
        var second = CreatePrivateEnum(BoundEnumTypes.Type(1), "second");
        var reference = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            enumDefinitions: [first, second],
            privateNominalTypeOrigins: [
                ((BoundEnumDeclarationIdentity.Private)first.Identity)
                    .Value.Origin,
                ((BoundEnumDeclarationIdentity.Private)second.Identity)
                    .Value.Origin,
            ]);

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public Enums.Color GetColor() {
                return Enums.Color.Red;
            }
            """), reference);

        Assert.That(compilation.HasErrors, Is.True,
            "Same-named enum owners must not unify during source lookup.");
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
                diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("ambiguous",
                    StringComparison.OrdinalIgnoreCase)),
            Is.True, string.Join(Environment.NewLine,
                compilation.Diagnostics));
    }

    [Test]
    public void PrivateEnumFixedArrayUsesItsInt32Representation()
    {
        var definition = CreatePrivateEnum(BoundEnumTypes.Type(0),
            "owner");
        var fixedArrays = new List<BoundNativeFixedArrayType>();

        Assert.That(BoundEnumTypes.IsValidTable([definition]), Is.True);
        Assert.That(NativeFixedArrayContract.TryCreateType(
                BoundEnumTypes.Type(0), 3, [], fixedArrays,
                out var arrayType, out var layout),
            Is.True);
        Assert.That(arrayType, Is.EqualTo(NativeFixedArrayTypes.Type(0)));
        Assert.That(layout.ElementStride, Is.EqualTo(4));
        Assert.That(layout.ByteExtent, Is.EqualTo(12));
        Assert.That(layout.Alignment, Is.EqualTo(4));
        Assert.That(layout.RequiredAlignment, Is.EqualTo(1));
        Assert.That(NativeFixedArrayContract.IsValidTable([], fixedArrays),
            Is.True);
    }

    private static BoundEnumDefinition CreatePrivateEnum(
        PrimitiveType type, string moduleIdentity)
    {
        var origin = new BoundPrivateNominalTypeOrigin(type,
            BoundPrivateNominalOriginKind.OwnedDefinition,
            IsExported: true, moduleIdentity, "types.Color");
        return BoundEnumDefinition.CreatePrivate(origin, "Enums.Color",
            PrimitiveType.Int32,
            [new BoundPrivateEnumMemberSnapshot(
                "Red", 1, true, "member.red")],
            isPublic: true, isPrimary: true, isFlags: false,
            blittableTargetRequirement: 0);
    }
}
