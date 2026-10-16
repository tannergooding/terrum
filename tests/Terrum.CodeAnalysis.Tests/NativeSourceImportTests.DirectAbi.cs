// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public void SourceAuthoredWin32ImportUsesOrdinaryPointerAbi()
    {
        const string source = """
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Rectangle {
                int32 Left;
                int32 Top;
                int32 Right;
                int32 Bottom;
                public Rectangle(int32 left, int32 top, int32 right, int32 bottom) {
                    Left = left;
                    Top = top;
                    Right = right;
                    Bottom = bottom;
                }
            }

            [Terrum.Interop.NativeImport("user32.dll", "GetClientRect")]
            extern unsafe int32 GetClientRect(ptr<void> window, ptr<Rectangle> rectangle);

            int32 Main() {
                mut Rectangle rectangle = new Rectangle(0, 0, 0, 0);
                unsafe { return GetClientRect(null, &rectangle); }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(program.NativeImportCatalog, Is.Null);
        var import = program.Functions.Single(static function => function.Name == "GetClientRect");
        var directImport = import.DirectNativeImport
            ?? throw new AssertionException("Expected direct native import metadata.");
        Assert.That(import.Variables[0].Type, Is.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(import.Variables[1].Type, Is.Not.EqualTo(PrimitiveType.NativeRawPointer));
        Assert.That(program.NativePointerTypes, Has.Length.EqualTo(1));
        var rectangleType = (PrimitiveType)(100 + Array.FindIndex(program.Structures.ToArray(),
            static structure => structure.Name == "Rectangle"));
        Assert.That(program.NativePointerTypes[0].PointeeType,
            Is.EqualTo(rectangleType));
        Assert.That(directImport.Parameters, Is.Empty);
        Assert.That(directImport.Outputs, Is.Empty);
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("GetClientRect"));
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("user32.dll"));
    }

    [Test]
    public void DirectNativeImportsPreserveTypedAndNestedPointerSignatures()
    {
        const string source = """
            [Terrum.Interop.NativeImport("pointer-probe.dll", "Get")]
            extern unsafe ptr<ptr<int32>> GetPointer();

            [Terrum.Interop.NativeImport("pointer-probe.dll", "Use")]
            extern unsafe int32 UsePointer(ptr<ptr<int32>> pointer);

            [Blittable] [Layout(LayoutKind.Sequential)]
            struct PointerCell {
                ptr<int32> Value;
                public PointerCell(ptr<int32> value) { Value = value; }
            }

            int32 Main() {
                PointerCell cell = new PointerCell(null);
                mut int32 value = 1;
                unsafe {
                    write ptr<int32> scalar = &value;
                    *scalar = 42;
                    ptr<ptr<int32>> pointer = GetPointer();
                    return UsePointer(pointer) + *scalar;
                }
            }
            """;

        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(program.NativePointerTypes, Has.Length.EqualTo(3));
        var getPointer = program.Functions.Single(static function => function.Name == "GetPointer");
        var usePointer = program.Functions.Single(static function => function.Name == "UsePointer");
        Assert.That(getPointer.ReturnType, Is.EqualTo(usePointer.Variables[0].Type));
        Assert.That(program.Structures.Single(static structure => structure.Name == "PointerCell").Size,
            Is.EqualTo(NativeTargetProfile.PointerSize));

        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("Get"));
        Assert.That(Encoding.ASCII.GetString(image), Does.Contain("Use"));
    }

    [Test]
    public void TypedPointersSurvivePrivateSemanticModuleProducerConsumerRoundTrip()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Read(ptr<int32> value) {
                unsafe { return *value; }
            }
            """));

        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Read"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init mut int32 value;
                unsafe {
                    write ptr<int32> writable = &value;
                    *writable = 42;
                    ptr<int32> readable = &value;
                    return Read(readable);
                }
            }
            """), reference);

        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(consumerCompilation.Program!, "typed-pointers"));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);

        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), NativeRuntimePolicy.FailFast),
            Is.EqualTo(42));

        var uninitializedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init int32 value;
                unsafe {
                    ptr<int32> pointer = &value;
                    return *pointer;
                }
            }
            """));
        Assert.That(uninitializedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, uninitializedCompilation.Diagnostics));

        var directReadCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                init mut int32 value;
                unsafe {
                    write ptr<int32> pointer = &value;
                    *pointer = 42;
                    return value;
                }
            }
            """));
        Assert.That(directReadCompilation.HasErrors, Is.True,
            "An unsafe pointer write does not establish initialization for a direct local read.");
    }
}
