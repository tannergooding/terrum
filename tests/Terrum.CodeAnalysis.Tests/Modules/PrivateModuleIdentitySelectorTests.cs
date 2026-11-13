using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class PrivateModuleIdentitySelectorTests
{
    [Test]
    public void SelectorsResolveOwnedStructuresAndRejectWrongKinds()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public struct Cell { int32 Value; }
                public int32 Read() { return 1; }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var provider = providerCompilation.Program!;
        var providerInputs = PrivateModuleIdentityInputs.CreateFromSelectors(
            provider, "SelectorProvider", [
                new PrivateNominalDefinitionSelection(
                    new PrivateNominalDeclarationSelector(
                        PrivateNominalDeclarationKind.Struct, "Cell"),
                    "types.cell", IsExported: true),
            ], [], [], []);
        Assert.That(providerInputs.Definitions.Single().Type,
            Is.EqualTo((PrimitiveType)100));
        Assert.That(providerInputs.Definitions.Single().LocalDeclarationKey,
            Is.EqualTo("types.cell"));
        Assert.That(providerInputs.Definitions.Single().IsExported, Is.True);

        _ = Assert.Throws<ArgumentException>(() =>
            PrivateModuleIdentityInputs.CreateFromSelectors(provider,
                "SelectorProvider", [
                    new PrivateNominalDefinitionSelection(
                        new PrivateNominalDeclarationSelector(
                            PrivateNominalDeclarationKind.Class, "Cell"),
                        "types.cell", IsExported: true),
                ], [], [], []));
    }
}
