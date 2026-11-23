using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmFniEnumClosureTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-foreign-native-interface-parameter-surface-1-enum-closure-1";

    [Test]
    public void EmitsEnumOnlyProviderWithoutForeignInterfaceRows()
    {
        var tree = SyntaxTree.Parse("""
            namespace Demo {
                public enum Shade : int8 {
                    Dark = -1,
                    Light = 1
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var declaration = tree.Root.DescendantNodes().Single(node =>
            node.Kind == SyntaxKind.EnumDeclaration);
        var definition = program.EnumDefinitions.Single(item =>
            item.Identity is BoundEnumDeclarationIdentity.Source source
            && source.Value.Parts.Any(part =>
                part.IsPrimary && ReferenceEquals(part.Tree, tree)
                && ReferenceEquals(part.Declaration, declaration)));
        Assert.That(definition.Members.Length, Is.EqualTo(2));
        var export = new TrRmSourceEnumDeclarationExport(program, 0,
            declaration.Span, ["enum.shade"],
            ["enum.shade.dark", "enum.shade.light"]);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "EnumProvider", SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [export]);
        var index
            = TrRmArtifactIndex
                .ForForeignNativeInterfaceParameterSurfaceEnumClosureDraft(
                    artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateAndGetExecutionTables(index);

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumDefs.Count, Is.EqualTo(1));
        Assert.That(tables.EnumMembers.Count, Is.EqualTo(2));
        Assert.That(index.TryGetSection(0x004C, out _, out _), Is.False);
        Assert.That(index.TryGetSection(0x0066, out _, out _), Is.False);
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Contain(18u));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Contain(TrRmFeatureTableReader.TypedFrozenValuesFeatureId));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Not.Contain(
                TrRmFeatureTableReader.NativeInterfaceCallsFeatureId));
        Assert.That(Enumerable.Range(1, tables.Features.Count)
            .Select(row => tables.Features.GetFeature(row).FeatureId),
            Does.Not.Contain(
                TrRmFeatureTableReader.MethodParameterLabelsFeatureId));
    }
}
