// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmStandardClassSourceEmissionTests
{
    private const string C1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1";
    private const string C1M1SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1";

    [Test]
    public void EmitsCapturedC1M1ClassDeclarationsAndRemapsWithoutSource()
    {
        var tree = SyntaxTree.Parse("""
            public class Unused {
                public int32 Value;
                public Unused(int32 value) { Value = value; }
            }
            public mut class Counter {
                public mut int32 Value;
                public Counter(int32 value) { Value = value; }
            }
            public struct Point { public int32 X; }
            public class Record {
                public int32 Value;
                private int32 _hidden;
                private Point _point;
                public Record(int32 value, Point point) {
                    Value = value;
                    _hidden = value;
                    _point = point;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classExports = CreateClassExports(program, tree);

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "SourceClasses", C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], classExports);
        var index = TrRmArtifactIndex.ForC1M1Draft(artifact);
        Assert.That(index.TryGetSection(0x0012, out _, out _), Is.False,
            "C1M1 declaration emission must not add class MethodDefs.");
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Counter.Member0", out var counterField), Is.True);
        var counterFieldRow = checked((int)(counterField & 0x00FF_FFFF));
        Assert.That(counterField >> 24, Is.EqualTo(0x14u));
        Assert.That(tables.TypeDefs.GetName(
            tables.Fields.GetOwnerTypeDef(counterFieldRow)),
            Is.EqualTo("Counter"));
        Assert.That(tables.Fields.GetName(counterFieldRow),
            Is.EqualTo("Value"));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Record.Member0", out var recordField), Is.True);
        var recordFieldRow = checked((int)(recordField & 0x00FF_FFFF));
        Assert.That(recordField >> 24, Is.EqualTo(0x14u));
        Assert.That(tables.TypeDefs.GetName(
            tables.Fields.GetOwnerTypeDef(recordFieldRow)),
            Is.EqualTo("Record"));
        Assert.That(tables.Fields.GetName(recordFieldRow),
            Is.EqualTo("Value"));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Unused.Member0", out _), Is.True);

        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    artifact),
                "SourceClasses", [], ["Counter.Member0"]));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1SchemaIdentity, artifact),
            "SourceClasses", [], ["CounterKey", "RecordKey"]);
        Assert.That(reference.Classes, Is.Empty);
        var dependencyTypeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("public void Use(Point value) {}"), reference);
        Assert.That(dependencyTypeCompilation.HasErrors, Is.True,
            "The local struct retained as class field closure is not a standalone source root.");
        var counter = reference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Counter");
        var record = reference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Record");
        Assert.That(counter.Declaration.TypeDefFlags, Is.EqualTo(0x8Au));
        Assert.That(record.Declaration.TypeDefFlags, Is.EqualTo(0x0Au));
        Assert.That(counter.Declaration.Fields.Select(
                static field => (field.Name, field.Flags)),
            Is.EqualTo(new[] { ("Value", 0x06u) }));
        Assert.That(record.Declaration.Fields.Select(
                static field => (field.Name, field.Flags)),
            Is.EqualTo(new[] {
                ("Value", 0x02u), ("_hidden", 0u), ("_point", 0u),
            }));
        Assert.That(record.Declaration.Fields.Single(
                static field => field.Name == "_point")
                .TypeSpecReference,
            Is.Not.EqualTo(0xF200_0008u));
        Assert.That(record.Declaration.Layouts.Select(
                static layout => (layout.Kind, layout.Alignment, layout.Pack)),
            Is.EqualTo(new[] { ((byte)0, 0u, 0u) }));
        Assert.That(record.Declaration.Layouts[0].FieldDefinitionIds,
            Has.Length.EqualTo(3));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "SourceClassesRemapped", C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        Assert.That(reemitted.AsSpan().SequenceEqual(artifact), Is.False);
        var remappedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "SourceClassesRemapped", [], ["CounterKey", "RecordKey"]);
        var remappedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(reemitted));
        Assert.That(remappedTables.Exports.TryGetDefinitionForKey(1,
            "Counter.Member0", out var remappedCounterField), Is.True);
        var remappedCounterFieldRow = checked((int)(
            remappedCounterField & 0x00FF_FFFF));
        Assert.That(remappedTables.TypeDefs.GetName(
            remappedTables.Fields.GetOwnerTypeDef(
                remappedCounterFieldRow)), Is.EqualTo("Counter"));
        Assert.That(remappedTables.Fields.GetName(remappedCounterFieldRow),
            Is.EqualTo("Value"));
        Assert.That(remappedCounterField, Is.Not.EqualTo(counterField));
        Assert.That(remappedTables.Exports.TryGetDefinitionForKey(1,
            "Record.Member0", out var remappedRecordField), Is.True);
        var remappedRecordFieldRow = checked((int)(
            remappedRecordField & 0x00FF_FFFF));
        Assert.That(remappedTables.TypeDefs.GetName(
            remappedTables.Fields.GetOwnerTypeDef(
                remappedRecordFieldRow)), Is.EqualTo("Record"));
        Assert.That(remappedTables.Fields.GetName(remappedRecordFieldRow),
            Is.EqualTo("Value"));
        Assert.That(remappedRecordField, Is.Not.EqualTo(recordField));
        Assert.That(remappedTables.Exports.TryGetDefinitionForKey(1,
            "Unused.Member0", out _), Is.False);
        Assert.That(remappedReference.Classes, Is.Empty);
        Assert.That(remappedReference.StandardClassDeclarations.Select(
                static declaration => declaration.Declaration.TypeDefFlags),
            Is.EquivalentTo(new[] { 0x8Au, 0x0Au }));
    }

    [Test]
    public void DoesNotPromoteCapturedClassesToTheC1Schema()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classExports = CreateClassExports(program, tree);

        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "C1Box",
                C1SchemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], classExports));
    }

    [Test]
    public void C1M1DoesNotEraseUniqueClassOwnership()
    {
        var tree = SyntaxTree.Parse("""
            public unique class Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        Assert.That(program.Classes.Single().IsUnique, Is.True);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "UniqueClass",
                C1M1SchemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], CreateClassExports(program, tree)));
    }

    [Test]
    public void EmitsLocalClassFieldTypeSpecsWithoutConstructionRecords()
    {
        var tree = SyntaxTree.Parse("""
            public mut class Link {
                public mut write Link? Next;
                public Link() { Next = null; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Links", C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(program, tree));
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(artifact));
        var fieldTypeSpecReference = tables.Fields.GetTypeSpecRef(1);
        var optionalType = tables.TypeSpecs.GetTypeSpecBytes(
            fieldTypeSpecReference);
        Assert.That(optionalType[0], Is.EqualTo(0x06));
        var classTypeSpecReference
            = System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32LittleEndian(optionalType[2..]);
        var classType = tables.TypeSpecs.GetTypeSpecBytes(
            classTypeSpecReference);
        Assert.That(classType[0], Is.EqualTo(0x02));
        Assert.That(classType[1], Is.EqualTo(0x03));
        Assert.That(tables.TypeDefs.GetKind(
            checked((int)System.Buffers.Binary.BinaryPrimitives
                .ReadUInt32LittleEndian(classType[2..]))), Is.EqualTo(2));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1SchemaIdentity, artifact),
            "Links", [], ["LinkKey"]);
        Assert.That(reference.StandardClassDeclarations, Has.Length.EqualTo(1));
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "LinksRemapped", C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var remappedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(C1M1SchemaIdentity,
                    reemitted),
                "LinksRemapped", [], ["LinkKey"]);
        Assert.That(remappedReference.StandardClassDeclarations,
            Has.Length.EqualTo(1));
    }

    private static ImmutableArray<TrRmSourceClassExport> CreateClassExports(
        BoundProgram program, SyntaxTree tree)
    {
        var declarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.ClassDeclaration)
            .ToArray();
        return [.. declarations.Select(declaration =>
        {
            var name = declaration.Children
                .OfType<SyntaxTokenElement>()
                .Last(static token => token.Token.Kind == TokenKind.Identifier)
                .Token.Name;
            var memberOrdinal = 0;
            var members = declaration.Children.OfType<SyntaxNode>()
                .Where(static member => member.Kind
                    is SyntaxKind.FieldDeclaration
                    or SyntaxKind.ConstructorDeclaration)
                .Where(static member => member.Children
                    .OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Kind
                        == TokenKind.PublicKeyword))
                .Select(member => new TrRmSourceClassMemberExport(0,
                    member.Span,
                    $"{name}.Member{memberOrdinal++}"))
                .ToImmutableArray();
            return new TrRmSourceClassExport(program, 0,
                declaration.Span, $"{name}Key", members);
        })];
    }
}
