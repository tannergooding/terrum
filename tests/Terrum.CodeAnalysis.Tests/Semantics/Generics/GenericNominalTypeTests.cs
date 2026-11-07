// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class GenericNominalTypeTests
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
        var boxDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Box");
        var nestedDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Nested");
        Assert.That(boxDefinition.Formals.Select(static formal => formal.Ordinal),
            Is.EqualTo([0]));
        Assert.That(boxDefinition.Formals.Single().HasTypeDefault, Is.False);
        Assert.That(program.Structures.Single(static structure
                => structure.Name == "Box<int32>")
                .SourceGenericTypeDefinitionIndex,
            Is.EqualTo(boxDefinition.DefinitionIndex));
        var int32Box = program.Structures.Single(static structure
            => structure.Name == "Box<int32>");
        var int32BoxIndex = program.Structures.IndexOf(int32Box);
        Assert.That(program.SourceGenericTypeInstances.Single(instance
                => instance.StructureIndex == int32BoxIndex).TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var uint32Box = program.Structures.Single(static structure
            => structure.Name == "Box<uint32>");
        var uint32BoxIndex = program.Structures.IndexOf(uint32Box);
        Assert.That(program.SourceGenericTypeInstances.Single(instance
                => instance.StructureIndex == uint32BoxIndex).TypeArguments,
            Is.EqualTo([PrimitiveType.UInt32]));
        Assert.That(program.SourceGenericTypeInstances.Single(instance
                => instance.DefinitionIndex == nestedDefinition.DefinitionIndex)
                .TypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var box = program.Structures.Single(static structure => structure.Name == "Box<int32>");
        Assert.That(box.Size, Is.EqualTo(4));
        Assert.That(box.Fields.Select(static field => field.Type),
            Is.EqualTo([PrimitiveType.Int32]));
        var unsignedBox = program.Structures.Single(static structure => structure.Name == "Box<uint32>");
        Assert.That(unsignedBox.Fields.Select(static field => field.Type),
            Is.EqualTo([PrimitiveType.UInt32]));
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

    [Test]
    public void AppliesGenericStructTypeDefaultsInDeclarationOrder()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell<T = int32, U = T> {
                public T first;
                public U second;
            }

            int32 ReadDefault(Cell value) { return value.second; }
            uint32 ReadExplicit(Cell<uint32> value) { return value.second; }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var cellDefinition = program.SourceGenericTypeDefinitions.Single(
            static definition => definition.Name == "Cell");
        Assert.That(cellDefinition.Formals.Select(static formal
                => formal.HasTypeDefault),
            Is.EqualTo([true, true]));
        var defaultCell = program.Structures.Single(static structure
            => structure.Name == "Cell<int32, int32>");
        Assert.That(defaultCell.Fields.Select(static field => field.Type),
            Is.EqualTo([
                PrimitiveType.Int32,
                PrimitiveType.Int32,
            ]));
        var unsignedCell = program.Structures.Single(static structure
            => structure.Name == "Cell<uint32, uint32>");
        Assert.That(unsignedCell.Fields.Select(static field => field.Type),
            Is.EqualTo([
                PrimitiveType.UInt32,
                PrimitiveType.UInt32,
            ]));
        Assert.That(program.Functions.Single(static function
            => function.Name == "ReadDefault").Variables[0].Type,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.IndexOf(defaultCell))));
        Assert.That(program.Functions.Single(static function
            => function.Name == "ReadExplicit").Variables[0].Type,
            Is.EqualTo((PrimitiveType)(100
                + program.Structures.IndexOf(unsignedCell))));
    }

    [Test]
    public void AppliesGenericStructOwnershipAndBlittableFormalDomains()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Bits { public int32 Value; }
            struct BitCell<T> where T : [Blittable] { public T Value; }
            struct UniqueMarker<T> where T : unique { public int32 Value; }
            struct FlexibleMarker<T> where T : unique? { public int32 Value; }
            struct NullableMarker<T> where T : null? { public int32 Value; }
            unique struct Owner { public int32 Value; }
            class Node { public int32 Value; public Node() { Value = 0; } }

            void ReadBits(BitCell<int32> value) { }
            void ReadStructBits(BitCell<Bits> value) { }
            void ReadUnique(UniqueMarker<Owner> value) { }
            void ReadFlexibleCopy(FlexibleMarker<int32> value) { }
            void ReadFlexibleUnique(FlexibleMarker<Owner> value) { }
            void ReadNullable(NullableMarker<Node?> value) { }
            int32 Main() { return 0; }
            """), new CompilationOptions {
                LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.Diagnostics, Is.Empty);
    }

    [Test]
    public void VerifiesSourceGenericDefinitionAndInstanceIdentity()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Box<T> { public T Value; }
            int32 Read(Box<int32> value) { return value.Value; }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var instance = program.SourceGenericTypeInstances.Single();
        _ = Assert.Throws<System.ArgumentException>(() =>
            SemanticVerifier.Verify(program with {
                SourceGenericTypeInstances = [],
            }));
        _ = Assert.Throws<System.ArgumentException>(() =>
            SemanticVerifier.Verify(program with {
                SourceGenericTypeInstances = [
                    instance with {
                        TypeArguments = [PrimitiveType.Void],
                    },
                ],
            }));
    }

    [Test]
    public void RewritesSourceGenericActualsWithNormalizedNativeTypes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct PointerBox<T> { public T Value; }
            unsafe void Use(PointerBox<ptr<int32>> value) { }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics, Is.Empty);
        var program = compilation.Program!;
        var instance = program.SourceGenericTypeInstances.Single();
        var fieldType = program.Structures[instance.StructureIndex]
            .Fields.Single().Type;
        Assert.That(instance.TypeArguments, Is.EqualTo([fieldType]));
        Assert.That(NativePointerTypeContract.IsValidType(fieldType,
            program.NativePointerTypes), Is.True);
    }

    [TestCase("struct CopyCell<T> { public T Value; } unique struct Owner { public int32 Value; } void Use(CopyCell<Owner> value) { } int32 Main() { return 0; }")]
    [TestCase("struct UniqueMarker<T> where T : unique { public int32 Value; } void Use(UniqueMarker<int32> value) { } int32 Main() { return 0; }")]
    [TestCase("unique struct Owner { public int32 Value; } struct CopyCell<T = Owner> { public T Value; } void Use(CopyCell value) { } int32 Main() { return 0; }")]
    public void RejectsGenericStructArgumentsOutsideTheirOwnershipDomain(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("ownership domain",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void RejectsGenericStructArgumentsThatDoNotSatisfyBlittableConstraint()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct NotBlittable { public int32 Value; }
            struct BitCell<T> where T : [Blittable] { public T Value; }
            void Use(BitCell<NotBlittable> value) { }
            int32 Main() { return 0; }
            """), new CompilationOptions {
                LayoutCoreRegistration = LayoutCoreRegistrationTestData.Create(),
            });

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("Blittable constraint",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void RejectsNullableGenericStructArgumentsWithoutNullabilityDomain()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Node { public int32 Value; public Node() { Value = 0; } }
            struct NonNullableMarker<T> { public int32 Value; }
            void Use(NonNullableMarker<Node?> value) { }
            int32 Main() { return 0; }
            """));

        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
                && diagnostic.Message.Contains("nullability domain",
                    System.StringComparison.Ordinal)), Is.True);
    }

    [TestCase("struct Cell<T = U, U = int32> { public T First; public U Second; } int32 Read(Cell value) { return value.Second; } int32 Main() { return 0; }")]
    [TestCase("struct Cell<T = T> { public T Value; } int32 Read(Cell value) { return value.Value; } int32 Main() { return 0; }")]
    public void RejectsGenericStructDefaultsThatReferenceCurrentOrForwardFormals(
        string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"), Is.True);
    }

    [TestCase("public struct Box<T> { T value; }")]
    [TestCase("class Box<T> { int32 value; }")]
    [TestCase("interface I<T> {} struct Box<T> : I<T> { T value; }")]
    [TestCase("interface I<T> where T : unique { }")]
    [TestCase("struct Box<T> where T : I<T> { T value; }")]
    [TestCase("struct Box<T> where T : write { T value; }")]
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
