// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class PrivateNominalClassPointerFieldTests
{
    [Test]
    public void ClassReferenceRemapsPointerFieldClosureAcrossProviderReplacement()
    {
        var original = CreateProvider(reorderStructures: false,
            changeHiddenFieldType: false);
        var reordered = CreateProvider(reorderStructures: true,
            changeHiddenFieldType: false);
        var changed = CreateProvider(reorderStructures: false,
            changeHiddenFieldType: true);
        var reference = ModuleCompiler.CreateReference(original);
        var widget = reference.PrivateClassReferences.Single();
        Assert.That(reference.Functions.Select(static function => function.Name),
            Is.EqualTo(["GetValue"]));
        Assert.That(reference.NativePointerTypes, Has.Length.EqualTo(1));
        Assert.That(reference.Classes.Single().Fields.Single().Type,
            Is.EqualTo(NativePointerTypes.Type(0)));
        Assert.That(widget.ExpectedSnapshot.Structures.Select(
            static structure => structure.Name).ToArray(),
            Does.Contain("Hidden"));

        var extra = CreateExtraPointerFieldProvider();
        var extraReference = ModuleCompiler.CreateReference(extra);
        foreach (var combinedReference in new[] {
            ModuleCompiler.CreateReferenceSetFromReferences(
                [extraReference, reference]),
            ModuleCompiler.MergeGraphReferences(
                [extraReference, reference]),
        })
        {
            var combinedWidget = combinedReference.Classes.Single(
                static @class => @class.Name == "Widget");
            var combinedPointerType = combinedWidget.Fields.Single().Type;
            Assert.That(combinedPointerType,
                Is.EqualTo(NativePointerTypes.Type(1)));
            Assert.That(combinedReference.PrivateClassReferences.Single(
                static classReference =>
                    classReference.Origin.LocalDeclarationKey
                        == "types.widget")
                .ExpectedNativePointerMapping, Is.EqualTo([1]));
            var combinedCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main() { return 0; }"),
                combinedReference);
            Assert.That(combinedCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    combinedCompilation.Diagnostics));
            var hiddenTypeCompilation = Compilation.Create(
                SyntaxTree.Parse("int32 Main(Hidden value) { return 0; }"),
                combinedReference);
            Assert.That(hiddenTypeCompilation.HasErrors, Is.True,
                "A class's private field dependency must not become a source lookup root.");
        }

        var compilation = Compilation.Create(SyntaxTree.Parse(
            "public int32 Main() { return GetValue(); }"), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var inputs = PrivateModuleIdentityInputs.Create(
            compilation.Program!, "consumer", [],
            [
                new PrivateNominalImport(widget.ClassType,
                    "types.widget.proxy", "provider", "types.widget"),
            ]);
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!,
            original, inputs);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, reordered));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, changed));
    }

    private static byte[] CreateExtraPointerFieldProvider()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public ptr<uint32> Value;
            }
            public int32 GetExtra() { return 0; }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var holderType = (PrimitiveType)(100
            + program.Structures.Select((structure, index) =>
                    (structure, index))
                .Single(static item => item.structure.Name == "Holder")
                .index);
        return ModuleCompiler.CreateProducer(program, ["GetExtra"],
            PrivateModuleIdentityInputs.Create(program, "extra",
                [new PrivateNominalDefinition(holderType, "types.holder",
                    IsExported: true)], []));
    }

    private static byte[] CreateProvider(bool reorderStructures,
        bool changeHiddenFieldType)
    {
        var noise = """
            struct Noise {
                public int32 Value;
                public Noise(int32 value) { Value = value; }
            }
            """;
        var hidden = $$"""
            struct Hidden {
                public {{(changeHiddenFieldType ? "uint32" : "int32")}} Value;
                public Hidden({{(changeHiddenFieldType ? "uint32" : "int32")}} value) {
                    Value = value;
                }
            }
            """;
        var source = (reorderStructures ? noise + hidden : hidden + noise) + """
            public class Widget {
                private ptr<Hidden> Data;
                public Widget(ptr<Hidden> data) { Data = data; }
            }
            public int32 GetValue() { return 1; }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var widgetType = (PrimitiveType)ManagedClassTypes.First;
        var definitions = program.Structures
            .Select((structure, index) => new PrivateNominalDefinition(
                (PrimitiveType)(100 + index),
                structure.Name == "Hidden" ? "types.hidden" : "types.noise",
                IsExported: false))
            .ToList();
        definitions.Add(new PrivateNominalDefinition(widgetType,
            "types.widget", IsExported: true));

        return ModuleCompiler.CreateProducer(program, ["GetValue"],
            PrivateModuleIdentityInputs.Create(program, "provider",
                definitions, []));
    }
}
