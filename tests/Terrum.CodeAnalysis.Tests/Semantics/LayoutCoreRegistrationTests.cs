// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class LayoutCoreRegistrationTests
{
    [Test]
    public void RegistrationRetainsExactProviderDeclarations()
    {
        var registration = CreateRegistration();

        Assert.That(registration.IsValid(), Is.True);
        Assert.That(registration.UsingModuleIdentity, Is.EqualTo("sample.consumer"));
        Assert.That(registration.ModuleBindings.Declarations,
            Has.Length.EqualTo(3));
        Assert.That(registration.ModuleBindings.Declarations.Select(
                static declaration => declaration.Identity.ExportKey),
            Is.EqualTo(["LayoutKind", "Layout", "Blittable"]));
    }

    [Test]
    public void LayoutSemanticsRequireTheSelectedRegistration()
    {
        const string Source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            """;

        var unregistered = Compilation.CreateLibrary(SyntaxTree.Parse(Source));
        Assert.That(unregistered.HasErrors, Is.True);
        Assert.That(unregistered.Diagnostics.Any(static diagnostic =>
            diagnostic.Message.Contains("exact selected Core registration",
                StringComparison.Ordinal)), Is.True);

        var registration = CreateRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Source),
            new CompilationOptions { LayoutCoreRegistration = registration });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.Not.Null);
        Assert.That(compilation.Program!.Structures.Single().Fields[1].Offset,
            Is.EqualTo(4));
        Assert.That(compilation.Program.EnumDefinitions.Any(definition =>
            definition.Identity
                == new BoundEnumDeclarationIdentity.Core(
                    registration.LayoutKinds.Identity)), Is.False);
    }

    [TestCase("[Layout(LayoutKind.Sequential, Packing: 1)]", 1, 5)]
    [TestCase("[Layout(Kind: LayoutKind.Sequential, Packing: 1)]", 1, 5)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]", 1, 32)]
    public void RegisteredArgumentsControlSequentialGeometry(string layout,
        int fieldOffset, int size)
    {
        var source = $$"""
            [Blittable]
            {{layout}}
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { LayoutCoreRegistration = CreateRegistration() });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var structure = program.Structures.Single();
        Assert.That(structure.Fields[1].Offset, Is.EqualTo(fieldOffset));
        Assert.That(structure.Size, Is.EqualTo(size));
        Assert.That(structure.Alignment, Is.EqualTo(
            layout.Contains("Alignment: 32", StringComparison.Ordinal) ? 32 : 1));
    }

    [TestCase(1, 1, 5, 1)]
    [TestCase(2, 2, 6, 2)]
    [TestCase(4, 4, 8, 4)]
    [TestCase(8, 4, 8, 4)]
    [TestCase(16, 4, 8, 4)]
    public void EveryPackingValueControlsPlacementWithoutChangingNaturalAlignment(
        int packing, int fieldOffset, int size, int alignment)
    {
        var source = $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: {{packing}})]
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { LayoutCoreRegistration = CreateRegistration() });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var structure = program.Structures.Single();
        Assert.That(structure.Fields[1].Offset, Is.EqualTo(fieldOffset));
        Assert.That(structure.Size, Is.EqualTo(size));
        Assert.That(structure.Alignment, Is.EqualTo(alignment));
        Assert.That(structure.RequiredAlignment, Is.EqualTo(1));
    }

    [TestCase(4, 8)]
    [TestCase(8, 8)]
    [TestCase(16, 16)]
    [TestCase(32, 32)]
    [TestCase(64, 64)]
    [TestCase(128, 128)]
    [TestCase(256, 256)]
    [TestCase(512, 512)]
    [TestCase(1024, 1024)]
    [TestCase(2048, 2048)]
    [TestCase(4096, 4096)]
    [TestCase(8192, 8192)]
    public void EverySupportedExplicitAlignmentControlsExtent(int alignment,
        int expectedSize)
    {
        var source = $$"""
            [Blittable]
            [Layout(LayoutKind.Sequential, Alignment: {{alignment}})]
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { LayoutCoreRegistration = CreateRegistration() });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var structure = compilation.Program!.Structures.Single();
        Assert.That(structure.Alignment, Is.EqualTo(alignment));
        Assert.That(structure.RequiredAlignment, Is.EqualTo(alignment));
        Assert.That(structure.Size, Is.EqualTo(expectedSize));
    }

    [Test]
    public void NestedAlignmentSurvivesPackingAndFixedArrayStride()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Alignment: 32)]
            public struct Element {
                public uint8 Tag;
                public uint32 Value;
            }
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Holder {
                public uint8 Prefix;
                public [Element; 2] Values;
                public uint16 Suffix;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { LayoutCoreRegistration = CreateRegistration() });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var element = program.Structures.Single(static structure =>
            structure.Name == "Element");
        var holder = program.Structures.Single(static structure =>
            structure.Name == "Holder");
        var array = global::System.Linq.Enumerable.Single(
            program.NativeFixedArrayTypes);
        Assert.That((element.Size, element.Alignment,
            element.RequiredAlignment), Is.EqualTo((32, 32, 32)));
        Assert.That((holder.Fields[1].Offset, holder.Fields[1].Size,
            holder.Fields[2].Offset), Is.EqualTo((32, 64, 96)));
        Assert.That((holder.Size, holder.Alignment, holder.RequiredAlignment),
            Is.EqualTo((128, 32, 32)));
        Assert.That((array.ElementStride, array.ByteExtent, array.Alignment,
            array.RequiredAlignment), Is.EqualTo((32UL, 64UL, 32UL, 32UL)));
        SemanticVerifier.VerifyLibrary(program);
        _ = Assert.Throws<ArgumentException>(() =>
            FixedLayoutStorage.Validate([element with {
                RequiredAlignment = 1,
            }]));
    }

    [Test]
    public void UnionPackingAndExplicitAlignmentRetainAlternativeGeometry()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Union, Packing: 1, Alignment: 32)]
            public struct Overlap {
                public mut uint32 Narrow;
                public mut uint64 Wide;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { LayoutCoreRegistration = CreateRegistration() });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var structure = program.Structures.Single();
        Assert.That(structure.Fields.Select(static field => field.Offset),
            Is.All.EqualTo(0));
        Assert.That((structure.Size, structure.Alignment,
            structure.RequiredAlignment), Is.EqualTo((32, 32, 32)));
        SemanticVerifier.VerifyLibrary(program);
    }

    [TestCase("[Layout(LayoutKind.Sequential)]", true)]
    [TestCase("[Layout(Kind: LayoutKind.Sequential)]", true)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 1)]", true)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 16)]", true)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 4)]", true)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 8192)]", true)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]", true)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 4, Packing: 1)]", true)]
    [TestCase("[Layout]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, 1)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Unknown: 1)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 1, Packing: 2)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Kind: LayoutKind.Sequential)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 0)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 3)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Packing: 32)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 0)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 2)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 3)]", false)]
    [TestCase("[Layout(LayoutKind.Sequential, Alignment: 16384)]", false)]
    [TestCase("[Layout(LayoutKind.Auto, Packing: 1)]", false)]
    public void RegisteredLayoutControlsAcceptOnlyTheDeclaredSchema(
        string layout, bool isValid)
    {
        const string prefix = """
            [Blittable]
            """;
        const string fields = """
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            """;
        var source = prefix + layout + Environment.NewLine + fields;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration = CreateRegistration(),
            });

        Assert.That(compilation.HasErrors, Is.EqualTo(!isValid),
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void SourceUseRetainsTheSelectedLayoutKindEnum()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            public LayoutKind Echo(LayoutKind value) { return value; }
            """;
        var registration = CreateRegistration();
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            new CompilationOptions { LayoutCoreRegistration = registration });

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.EnumDefinitions.Any(definition =>
            definition.Identity
                == new BoundEnumDeclarationIdentity.Core(
                    registration.LayoutKinds.Identity)), Is.True);
    }

    [Test]
    public void SameNamedUserTypesDoNotProvideLayoutRegistration()
    {
        const string Source = """
            struct LayoutKind { public int32 Value; }
            [Blittable]
            [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public uint8 Tag;
                public uint32 Value;
            }
            """;

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(Source),
            new CompilationOptions { LayoutCoreRegistration = CreateRegistration() });

        Assert.That(compilation.HasErrors, Is.True);
    }

    private static LayoutCoreRegistrationSet CreateRegistration()
        => LayoutCoreRegistrationTestData.Create();
}
