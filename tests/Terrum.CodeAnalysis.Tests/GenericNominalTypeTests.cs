// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class GenericNominalTypeTests
{
    [Test]
    public void SpecializesInternalGenericStructsAcrossFieldsMethodsAndConstruction()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Box<T>
            {
                T value;
                public Box(T value) { this.value = value; }
                public T Get() { return this.value; }
            }
            struct Nested<T>
            {
                Box<T> value;
                public Nested(Box<T> value) { this.value = value; }
                public T Get() { return this.value.Get(); }
            }
            uint32 ReadUnsigned(Box<uint32> value) { return value.Get(); }
            int32 Main()
            {
                Nested<int32> value = new Nested<int32>(new Box<int32>(42));
                return value.Get();
            }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var box = program.Structures.Single(static structure => structure.Name == "Box<int32>");
        Assert.That(box.Size, Is.EqualTo(4));
        Assert.That(box.Fields.Select(static field => field.Type),
            Is.EqualTo(new[] { PrimitiveType.Int32 }));
        var unsignedBox = program.Structures.Single(static structure => structure.Name == "Box<uint32>");
        Assert.That(unsignedBox.Fields.Select(static field => field.Type),
            Is.EqualTo(new[] { PrimitiveType.UInt32 }));
        var nested = program.Structures.Single(static structure => structure.Name == "Nested<int32>");
        Assert.That(nested.Fields.Single().Type, Is.EqualTo((PrimitiveType)(100
            + program.Structures.IndexOf(box))));
        Assert.That(program.Functions.Single(static function => function.Name == "Box<int32>.Get").ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(program.Functions.Single(static function => function.Name == "Box<int32>.Get").IsInternal,
            Is.True);
        Assert.That(program.Functions.Single(static function => function.Name == "Box<uint32>.Get").ReturnType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(program.Functions.Single(static function => function.Name == "Nested<int32>.Get").ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(program.Functions.Single(static function => function.Name == "Main").ReturnType,
            Is.EqualTo(PrimitiveType.Int32));
    }

    [TestCase("public struct Box<T> { T value; }")]
    [TestCase("class Box<T> { int32 value; }")]
    [TestCase("interface I<T> {} struct Box<T> : I<T> { T value; }")]
    [TestCase("struct Box<T> where T : I<T> { T value; }")]
    [TestCase("struct Box<T> { T value; public U Get<U>() { return value; } }")]
    public void RejectsGenericNominalFormsOutsideTheClosedStructProfile(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            declaration + " int32 Main() { return 0; }"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3005"), Is.True);
    }

    [TestCase("Box")]
    [TestCase("Box<int32, uint32>")]
    public void RejectsMissingOrIncorrectGenericStructArity(string use)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"struct Box<T> {{ T value; }} int32 Read({use} item) {{ return 0; }} int32 Main() {{ return 0; }}"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("requires its declared type argument count",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DoesNotExportCompilationLocalGenericTypesThroughPublicCallables()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Box<T> { T value; public T Get() { return this.value; } }
            public int32 Read(Box<int32> value) { return value.Get(); }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("compilation-local generic struct",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void DoesNotExposeCompilationLocalGenericTypesThroughPublicMethods()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            struct Box<T> { T value; public Box(T value) { this.value = value; } }
            public class Catalog
            {
                public Box<int32> Create() { return new Box<int32>(42); }
            }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("Public callables cannot expose a compilation-local generic struct",
                System.StringComparison.Ordinal)), Is.True);
    }

    [TestCase("struct Catalog { private Box<int32> value; }")]
    [TestCase("struct Inner { Box<int32> value; } struct Catalog { private Inner value; }")]
    [TestCase("class Catalog { private Box<int32> value; }")]
    public void DoesNotExposeCompilationLocalGenericTypesThroughPublicAggregateLayouts(string declaration)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            $"struct Box<T> {{ T value; }} public {declaration} int32 Main() {{ return 0; }}"));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("compilation-local generic struct",
                System.StringComparison.Ordinal)), Is.True);
    }
}
