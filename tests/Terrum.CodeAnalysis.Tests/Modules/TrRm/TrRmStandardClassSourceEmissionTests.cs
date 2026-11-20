// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
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
    private const string C1M1Construction2SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
    private const string C1M1Construction2ClassParameterDefaultsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1";
    private const string C1M1Construction2ClassParameterDefaultsCopyEvidenceSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1-initializer-copy-evidence-1";

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
    public void EmitsConstruction2InitializerAndProjectionBodies()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public static int32 Answer() { return 42; }
                public void Touch() {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var memberNodes = classDeclaration.Children.OfType<SyntaxNode>();
        var fieldDeclaration = memberNodes.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructorDeclaration = memberNodes.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var methodDeclaration = memberNodes.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Answer"));
        var instanceMethodDeclaration = memberNodes.Skip(1)
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Touch"));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2Box", C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            fieldDeclaration.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructorDeclaration.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0,
                            methodDeclaration.Span, "Box.Answer"),
                        new TrRmSourceClassMemberExport(0,
                            instanceMethodDeclaration.Span, "Box.Touch"),
                    ]),
            ]);
        var index = TrRmArtifactIndex.ForC1M1Construction2Draft(
            artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);

        Assert.That(tables.Methods.Count, Is.EqualTo(4));
        Assert.That(tables.Signatures.Count, Is.EqualTo(4));
        Assert.That(tables.Parameters.Count, Is.EqualTo(2));
        Assert.That(tables.Contracts.Count, Is.EqualTo(2));
        Assert.That(tables.Bodies.Count, Is.EqualTo(4));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Initialize", out var initializer), Is.True);
        Assert.That(initializer >> 24, Is.EqualTo(0x16u));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Create", out var projection), Is.True);
        Assert.That(projection >> 24, Is.EqualTo(0x16u));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Answer", out var answer), Is.True);
        Assert.That(answer >> 24, Is.EqualTo(0x16u));
        var answerMethodRow
            = checked((int)(answer & 0x00FF_FFFF));
        var answerBodyRow = tables.Methods.GetBodyRow(answerMethodRow);
        Assert.That(answerBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(answerBodyRow,
            tables.Contracts.Count);
        Assert.That(tables.Bodies.GetRow(answerBodyRow)[0],
            Is.EqualTo(0x01));
        var answerBody = tables.Bodies.GetRegisterBody(answerBodyRow,
            tables.Contracts.Count);
        Assert.That(answerBody.HomeCount, Is.Zero);
        Assert.That(answerBody.ConstantCount, Is.EqualTo(1));
        Assert.That(answerBody.ConstantMap.Length, Is.EqualTo(8));
        var answerSignature = tables.Methods.GetSignature(answerMethodRow);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    answerBody.ConstantMap),
                tables.Signatures.GetResultTypeSpec(answerSignature)),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                answerBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(9, out _), Is.False);
        Assert.That(answerBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] { 0xD4, 0, 0, 0, 0x17, 0 }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Touch", out var touch), Is.True);
        Assert.That(touch >> 24, Is.EqualTo(0x16u));
        var touchMethodRow
            = checked((int)(touch & 0x00FF_FFFF));
        var touchBodyRow = tables.Methods.GetBodyRow(touchMethodRow);
        Assert.That(touchBodyRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(touchMethodRow);

        tables.ValidateRegisterBodyMapsForMethod(1);
        tables.ValidateRegisterBodyMapsForMethod(2);

        var privateProjectionArtifact
            = TrRmExecutableCompiler.EmitStandard(program,
                "Construction2PrivateProjection",
                C1M1Construction2SchemaIdentity,
                TrRmModuleTableReader
                    .WindowsX64NativeTargetRequirement,
                [], [], [
                    new TrRmSourceClassExport(program, 0,
                        classDeclaration.Span, "PrivateBoxKey", [
                            new TrRmSourceClassMemberExport(0,
                                constructorDeclaration.Span,
                                "Box.Initialize"),
                                new TrRmSourceClassMemberExport(0,
                                    methodDeclaration.Span, "Box.Answer"),
                                new TrRmSourceClassMemberExport(0,
                                    instanceMethodDeclaration.Span,
                                    "Box.Touch"),
                        ]),
                ]);
        var privateProjectionTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    TrRmArtifactIndex.ForC1M1Construction2Draft(
                        privateProjectionArtifact));
        Assert.That(privateProjectionTables.Methods.GetFlags(2),
            Is.Zero);
        Assert.That(privateProjectionTables.Exports
            .TryGetDefinitionForKey(1, "Box.Create", out _), Is.False);
        var privateAnswerBodyRow
            = privateProjectionTables.Methods.GetBodyRow(3);
        Assert.That(privateAnswerBodyRow, Is.GreaterThan(0));
        privateProjectionTables.Bodies.ValidateSelectedImplementation(
            privateAnswerBodyRow,
            privateProjectionTables.Contracts.Count);
        var privateTouchBodyRow
            = privateProjectionTables.Methods.GetBodyRow(4);
        Assert.That(privateTouchBodyRow, Is.GreaterThan(0));
        privateProjectionTables.ValidateRegisterBodyMapsForMethod(4);
        privateProjectionTables.ValidateRegisterBodyMapsForMethod(1);
        privateProjectionTables.ValidateRegisterBodyMapsForMethod(2);
    }

    [Test]
    public void BindsC2ModuleFunctionWithClassParameterFromFullReference()
    {
        const string classProviderIdentity = "C2ClassTypeProvider";
        const string moduleIdentity = "C2ClassFunctionProvider";
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
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var classMembers = classDeclaration.Children.OfType<SyntaxNode>();
        var fieldDeclaration = classMembers.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructorDeclaration = classMembers.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var classArtifact = TrRmExecutableCompiler.EmitStandard(
            program, classProviderIdentity,
            C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            fieldDeclaration.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructorDeclaration.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                    ]),
            ]);

        var classReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2SchemaIdentity, classArtifact),
                classProviderIdentity, []);
        var functionCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                """
                public void Consume(Box value, int32 count) {}
                internal void Helper(Box value) {}
                internal void HelperAgain(Box value) { Helper(value); }
                """),
            classReference);
        Assert.That(functionCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                functionCompilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)functionCompilation.Program!, moduleIdentity,
            C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2SchemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(reference.StandardClassDeclarations.Select(
                static declaration => declaration.Declaration.Name),
            Does.Contain("Box"));
        Assert.That(reference.Functions.Select(static function => function.Name),
            Does.Contain("Consume"));

        var downstream = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void Forward(Box value, int32 count) {
                Consume(value, count);
            }
            """), reference);
        Assert.That(downstream.HasErrors, Is.False,
            string.Join(Environment.NewLine, downstream.Diagnostics));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        const string reemittedModuleIdentity
            = "C2ClassFunctionProviderReemitted";
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            reemittedModuleIdentity, C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    TrRmArtifactIndex.ForC1M1Construction2Draft(
                        reemitted));
        var consumeDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export => export.ExportKey == "Consume")
            .Definition;
        var consumeMethodRow = checked((int)(
            consumeDefinition & 0x00FF_FFFF));
        var consumeBodyRow = reemittedTables.Methods.GetBodyRow(
            consumeMethodRow);
        Assert.That(reemittedTables.Methods.GetOwnerType(consumeMethodRow),
            Is.Zero);
        Assert.That(consumeBodyRow, Is.GreaterThan(0));
        Assert.That(reemittedTables.Bodies.GetRow(consumeBodyRow)[0],
            Is.EqualTo(0x01));
        Assert.That(reemittedTables.Exports.GetExportsForModule(1)
            .Any(static export => export.ExportKey == "Helper"), Is.False);
        var helperMethodRow = Enumerable.Range(1,
                reemittedTables.Methods.Count)
            .Single(row => reemittedTables.Methods.GetOwnerType(row) == 0
                && reemittedTables.Methods.GetName(row) == "Helper");
        Assert.That(reemittedTables.Methods.GetFlags(helperMethodRow) & 3u,
            Is.EqualTo(1u));
        Assert.That(reemittedTables.Methods.GetBodyRow(helperMethodRow),
            Is.GreaterThan(0));
        var helperAgainMethodRow = Enumerable.Range(1,
                reemittedTables.Methods.Count)
            .Single(row => reemittedTables.Methods.GetOwnerType(row) == 0
                && reemittedTables.Methods.GetName(row) == "HelperAgain");
        var helperAgainBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedTables.Methods.GetBodyRow(helperAgainMethodRow),
            reemittedTables.Contracts.Count);
        Assert.That(helperAgainBody.MethodMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                helperAgainBody.MethodMap),
            Is.EqualTo(0x1600_0000u | checked((uint)helperMethodRow)));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2SchemaIdentity, reemitted),
                reemittedModuleIdentity, []);
        var reloadedDownstream = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void ForwardAgain(Box value, int32 count) {
                    Consume(value, count);
                }
                """), reemittedReference);
        Assert.That(reloadedDownstream.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reloadedDownstream.Diagnostics));
    }

    [Test]
    public void EmitsMixedParameterAndConstantConstructorFieldStores()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public bool Enabled;
                public Box(int32 value) {
                    Value = value;
                    Enabled = true;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var fields = members.Where(static node =>
            node.Kind == SyntaxKind.FieldDeclaration).ToArray();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2ConstantInitializer",
            C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            fields[0].Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            fields[1].Span, "Box.Enabled"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Initialize", out var initializer), Is.True);
        var initializerRow
            = checked((int)(initializer & 0x00FF_FFFF));
        var bodyRow = tables.Methods.GetBodyRow(initializerRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(bodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(initializerRow);

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.EqualTo(1));
        Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        var typeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Box");
        var (fieldStart, fieldCount)
            = tables.TypeDefs.GetFieldRange(typeDefRow);
        var enabledFieldRow = Enumerable.Range(fieldStart, fieldCount)
            .Single(row => tables.Fields.GetName(row) == "Enabled");
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(body.ConstantMap),
                tables.Fields.GetTypeSpecRef(enabledFieldRow)),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                body.ConstantMap.Slice(sizeof(uint))),
            Is.GreaterThan(0u));
        Assert.That(body.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD8, 0, 1, 0,
                0xD9, 0, 2, 0,
                0xD8, 0, 3, 0,
                0xD4, 1, 0, 0,
                0xD9, 0, 4, 0,
                0x18, 0,
            }));
    }

    [Test]
    public void EmitsSeparateInitializerAndProjectionForEachConstructor()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public bool Enabled;
                public Box(int32 value, bool enabled) {
                    Value = value;
                    Enabled = enabled;
                }
                public Box(bool enabled, int32 value) {
                    Value = value;
                    Enabled = enabled;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>().ToArray();
        var fields = members.Where(static node =>
            node.Kind == SyntaxKind.FieldDeclaration).ToArray();
        var constructors = members.Where(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration).ToArray();
        Assert.That(fields, Has.Length.EqualTo(2));
        Assert.That(constructors, Has.Length.EqualTo(2));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2OverloadedBox", C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            fields[0].Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            fields[1].Span, "Box.Enabled"),
                        new TrRmSourceClassMemberExport(0,
                            constructors[0].Span, "Box.InitializeIntBool") {
                            ConstructionProjectionExportKey = "Box.CreateIntBool",
                        },
                        new TrRmSourceClassMemberExport(0,
                            constructors[1].Span, "Box.InitializeBoolInt") {
                            ConstructionProjectionExportKey = "Box.CreateBoolInt",
                        },
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));

        Assert.That(tables.Methods.Count, Is.EqualTo(4));
        Assert.That(tables.Signatures.Count, Is.EqualTo(4));
        Assert.That(tables.Parameters.Count, Is.EqualTo(8));
        Assert.That(tables.Contracts.Count, Is.EqualTo(4));
        Assert.That(tables.Bodies.Count, Is.EqualTo(4));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.InitializeIntBool", out var intBoolInitializer), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.CreateIntBool", out var intBoolProjection), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.InitializeBoolInt", out var boolIntInitializer), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.CreateBoolInt", out var boolIntProjection), Is.True);

        var intBoolInitializerRow = checked((int)(
            intBoolInitializer & 0x00FF_FFFF));
        var intBoolProjectionRow = checked((int)(
            intBoolProjection & 0x00FF_FFFF));
        var boolIntInitializerRow = checked((int)(
            boolIntInitializer & 0x00FF_FFFF));
        var boolIntProjectionRow = checked((int)(
            boolIntProjection & 0x00FF_FFFF));
        var intBoolInitializerParams = tables.Signatures.GetParamRange(
            tables.Methods.GetSignature(intBoolInitializerRow));
        var intBoolProjectionParams = tables.Signatures.GetParamRange(
            tables.Methods.GetSignature(intBoolProjectionRow));
        var boolIntInitializerParams = tables.Signatures.GetParamRange(
            tables.Methods.GetSignature(boolIntInitializerRow));
        var boolIntProjectionParams = tables.Signatures.GetParamRange(
            tables.Methods.GetSignature(boolIntProjectionRow));
        Assert.That(intBoolInitializerParams.Count, Is.EqualTo(2));
        Assert.That(intBoolProjectionParams.Count, Is.EqualTo(2));
        Assert.That(boolIntInitializerParams.Count, Is.EqualTo(2));
        Assert.That(boolIntProjectionParams.Count, Is.EqualTo(2));
        var intBoolFirstType = tables.Parameters.GetTypeSpec(
            intBoolInitializerParams.Start);
        var intBoolSecondType = tables.Parameters.GetTypeSpec(
            intBoolInitializerParams.Start + 1);
        var boolIntFirstType = tables.Parameters.GetTypeSpec(
            boolIntInitializerParams.Start);
        var boolIntSecondType = tables.Parameters.GetTypeSpec(
            boolIntInitializerParams.Start + 1);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                intBoolFirstType,
                tables.Parameters.GetTypeSpec(boolIntProjectionParams.Start
                    + 1)), Is.True);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                intBoolSecondType,
                tables.Parameters.GetTypeSpec(boolIntProjectionParams.Start)),
            Is.True);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                boolIntFirstType,
                tables.Parameters.GetTypeSpec(intBoolProjectionParams.Start
                    + 1)), Is.True);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                boolIntSecondType,
                tables.Parameters.GetTypeSpec(intBoolProjectionParams.Start)),
            Is.True);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                intBoolFirstType, boolIntFirstType), Is.False);
        foreach (var (initializerRow, projectionRow) in new[] {
            (intBoolInitializerRow, intBoolProjectionRow),
            (boolIntInitializerRow, boolIntProjectionRow),
        })
        {
            Assert.That(tables.Contracts.GetConstructionClause(
                initializerRow).RoleTag, Is.EqualTo(1));
            var projectionClause
                = tables.Contracts.GetConstructionClause(projectionRow);
            Assert.That(projectionClause.RoleTag, Is.EqualTo(2));
            Assert.That(projectionClause.InitializerMethodDefRow,
                Is.EqualTo((uint)initializerRow));
            Assert.That(tables.Methods.GetBodyRow(initializerRow),
                Is.GreaterThan(0));
            Assert.That(tables.Methods.GetBodyRow(projectionRow),
                Is.GreaterThan(0));
            tables.ValidateRegisterBodyMapsForMethod(initializerRow);
            tables.ValidateRegisterBodyMapsForMethod(projectionRow);
        }

        Assert.That(intBoolInitializerRow, Is.Not.EqualTo(
            boolIntInitializerRow));
        Assert.That(intBoolProjectionRow, Is.Not.EqualTo(
            boolIntProjectionRow));
    }

    [Test]
    public void EmitsPartialConstructorUsingItsPrimaryIdentityAndSecondaryBody()
    {
        var primaryTree = SyntaxTree.Parse("""
            public partial class Box {
                public int32 Value;
                public partial Box(int32 value);
            }
            """);
        var secondaryTree = SyntaxTree.Parse("""
            public partial class Box {
                partial Box(int32 value) { Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary([
            primaryTree,
            secondaryTree,
        ]);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = primaryTree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var primary = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration
            && node.Children.OfType<SyntaxNode>().Any(
                static child => child.Kind == SyntaxKind.ExternalBody));
        var secondary = secondaryTree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration)
            .Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ConstructorDeclaration);
        var function = program.Functions.Single(static candidate =>
            candidate.IsClassConstructor);
        Assert.That(function.SourceDeclarationInputOrdinal, Is.EqualTo(0));
        Assert.That(function.SourceInputOrdinal, Is.EqualTo(1));
        Assert.That(function.SourceDeclarationSpan, Is.EqualTo(primary.Span));
        Assert.That(function.Span, Is.EqualTo(secondary.Span));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2PartialBox",
            C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0, primary.Span,
                            "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Methods.Count, Is.EqualTo(2));
        Assert.That(tables.Signatures.Count, Is.EqualTo(2));
        Assert.That(tables.Parameters.Count, Is.EqualTo(2));
        Assert.That(tables.Contracts.Count, Is.EqualTo(2));
        Assert.That(tables.Bodies.Count, Is.EqualTo(2));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Initialize", out var initializer), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Create", out var projection), Is.True);
        var initializerRow = checked((int)(initializer & 0x00FF_FFFF));
        var projectionRow = checked((int)(projection & 0x00FF_FFFF));
        Assert.That(tables.Contracts.GetConstructionClause(
            initializerRow).RoleTag, Is.EqualTo(1));
        var projectionClause
            = tables.Contracts.GetConstructionClause(projectionRow);
        Assert.That(projectionClause.RoleTag, Is.EqualTo(2));
        Assert.That(projectionClause.InitializerMethodDefRow,
            Is.EqualTo((uint)initializerRow));
        Assert.That(tables.Methods.GetBodyRow(initializerRow),
            Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(projectionRow),
            Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(initializerRow);
        tables.ValidateRegisterBodyMapsForMethod(projectionRow);
    }

    [Test]
    public void EmitsStaticAndInstanceConditionalReturnBodies()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public static int32 Select(bool condition, int32 whenTrue,
                    int32 whenFalse) {
                    if (condition) { return whenTrue; }
                    else { return whenFalse; }
                }
                public static int32 SelectEqual(bool left, bool right,
                    int32 whenEqual, int32 whenDifferent) {
                    if (left == right) { return whenEqual; }
                    else { return whenDifferent; }
                }
                public static int32 SelectLess(int32 left, int32 right,
                    int32 less, int32 greaterOrEqual) {
                    if (left < right) { return less; }
                    else { return greaterOrEqual; }
                }
                public static int32 SelectArithmetic(bool condition,
                    int32 left, int32 right) {
                    if (condition) { return left + right; }
                    else { return left - right; }
                }
                public static int32 SelectArithmeticConstant(
                    bool condition, int32 value) {
                    if (condition) { return value + 5; }
                    else { return 7 - value; }
                }
                public static int32 SelectUnary(bool condition,
                    int32 value) {
                    if (condition) { return -value; }
                    else { return ~value; }
                }
                public static int32 SelectNested(bool first, bool second,
                    int32 innerTrue, int32 innerFalse, int32 outerFalse) {
                    if (first) {
                        if (second) { return innerTrue; }
                        else { return innerFalse; }
                    } else { return outerFalse; }
                }
                public static float64 SelectFloatLess(float64 left,
                    float64 right, float64 less, float64 greaterOrEqual) {
                    if (left < right) { return less; }
                    else { return greaterOrEqual; }
                }
                public int32 SelectInstance(bool condition, int32 whenTrue) {
                    if (condition) { return whenTrue; }
                    else { return 42; }
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var select = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Select"));
        var selectInstance = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectInstance"));
        var selectEqual = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectEqual"));
        var selectLess = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectLess"));
        var selectFloatLess = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectFloatLess"));
        var selectArithmetic = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectArithmetic"));
        var selectNested = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectNested"));
        var selectUnary = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SelectUnary"));
        var selectArithmeticConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "SelectArithmeticConstant"));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ConditionalClassMethod", C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, select.Span,
                            "Box.Select"),
                        new TrRmSourceClassMemberExport(0,
                            selectEqual.Span, "Box.SelectEqual"),
                        new TrRmSourceClassMemberExport(0,
                            selectLess.Span, "Box.SelectLess"),
                        new TrRmSourceClassMemberExport(0,
                            selectFloatLess.Span, "Box.SelectFloatLess"),
                        new TrRmSourceClassMemberExport(0,
                            selectArithmetic.Span,
                            "Box.SelectArithmetic"),
                        new TrRmSourceClassMemberExport(0,
                            selectArithmeticConstant.Span,
                            "Box.SelectArithmeticConstant"),
                        new TrRmSourceClassMemberExport(0,
                            selectNested.Span, "Box.SelectNested"),
                        new TrRmSourceClassMemberExport(0,
                            selectUnary.Span, "Box.SelectUnary"),
                        new TrRmSourceClassMemberExport(0,
                            selectInstance.Span, "Box.SelectInstance"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Select", out var selectDefinition), Is.True);
        var methodRow = checked((int)(selectDefinition & 0x00FF_FFFF));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(bodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(methodRow);

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(3));
        Assert.That(body.RecordCount, Is.EqualTo(2));
        Assert.That(body.HomeCount, Is.EqualTo(3));
        Assert.That(body.GetRecord(1).ToArray(),
            Is.EqualTo(new byte[] { 0x06, 0, 0, 0, 1, 0, 2, 0 }));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0, 0xE4, 0, 1, 0,
            }));
        Assert.That(body.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0, 0x17, 0,
            }));
        Assert.That(body.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 2, 0, 0x17, 0,
            }));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SelectEqual", out var equalDefinition), Is.True);
        var equalMethodRow
            = checked((int)(equalDefinition & 0x00FF_FFFF));
        var equalBodyRow = tables.Methods.GetBodyRow(equalMethodRow);
        Assert.That(equalBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(equalBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(equalMethodRow);
        var equalBody = tables.Bodies.GetRegisterBody(equalBodyRow,
            tables.Contracts.Count);
        Assert.That(equalBody.BlockCount, Is.EqualTo(3));
        Assert.That(equalBody.HomeCount, Is.EqualTo(4));
        Assert.That(equalBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC2, 0x01, 0xF0, 0x10,
                0xE4, 0, 1, 0,
            }));

        foreach (var methodName in new[] {
            "Box.SelectLess",
            "Box.SelectFloatLess",
        })
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                methodName, out var comparisonDefinition), Is.True);
            var comparisonMethodRow
                = checked((int)(comparisonDefinition & 0x00FF_FFFF));
            var comparisonBodyRow
                = tables.Methods.GetBodyRow(comparisonMethodRow);
            Assert.That(comparisonBodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(comparisonBodyRow,
                tables.Contracts.Count);
            tables.ValidateRegisterBodyMapsForMethod(
                comparisonMethodRow);
            var comparisonBody = tables.Bodies.GetRegisterBody(
                comparisonBodyRow, tables.Contracts.Count);
            Assert.That(comparisonBody.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0xD5, 0, 0, 0,
                    0xD5, 1, 1, 0,
                    0xC2, 0, 0xF0, 0x10,
                    0xC3, 1, 0, 0xFF,
                    0xE4, 0, 1, 0,
                }));
        }

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SelectArithmetic", out var arithmeticDefinition), Is.True);
        var arithmeticMethodRow
            = checked((int)(arithmeticDefinition & 0x00FF_FFFF));
        var arithmeticBodyRow
            = tables.Methods.GetBodyRow(arithmeticMethodRow);
        Assert.That(arithmeticBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(arithmeticBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(arithmeticMethodRow);
        var arithmeticBody = tables.Bodies.GetRegisterBody(
            arithmeticBodyRow, tables.Contracts.Count);
        Assert.That(arithmeticBody.BlockCount, Is.EqualTo(3));
        Assert.That(arithmeticBody.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, 0x01, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(arithmeticBody.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, 0x02, 0xF0, 0x10,
                0x17, 0,
            }));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SelectArithmeticConstant", out var constantArithmeticDefinition),
            Is.True);
        var constantArithmeticMethodRow = checked((int)(
            constantArithmeticDefinition & 0x00FF_FFFF));
        var constantArithmeticBodyRow
            = tables.Methods.GetBodyRow(constantArithmeticMethodRow);
        Assert.That(constantArithmeticBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            constantArithmeticBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(
            constantArithmeticMethodRow);
        var constantArithmeticBody = tables.Bodies.GetRegisterBody(
            constantArithmeticBodyRow, tables.Contracts.Count);
        Assert.That(constantArithmeticBody.ConstantCount, Is.EqualTo(2));
        Assert.That(constantArithmeticBody.ConstantMap.Length,
            Is.EqualTo(16));
        Assert.That(constantArithmeticBody.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD4, 1, 0, 0,
                0xC0, 0x01, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(constantArithmeticBody.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] {
                0xD4, 0, 1, 0,
                0xD5, 1, 1, 0,
                0xC0, 0x02, 0xF0, 0x10,
                0x17, 0,
            }));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SelectUnary", out var unaryDefinition), Is.True);
        var unaryMethodRow
            = checked((int)(unaryDefinition & 0x00FF_FFFF));
        var unaryBodyRow = tables.Methods.GetBodyRow(unaryMethodRow);
        Assert.That(unaryBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(unaryBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(unaryMethodRow);
        var unaryBody = tables.Bodies.GetRegisterBody(unaryBodyRow,
            tables.Contracts.Count);
        Assert.That(unaryBody.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xC0, 0x04, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(unaryBody.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xC1, 0x04, 0xF0, 0xF0,
                0x17, 0,
            }));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SelectNested", out var nestedDefinition), Is.True);
        var nestedMethodRow
            = checked((int)(nestedDefinition & 0x00FF_FFFF));
        var nestedBodyRow = tables.Methods.GetBodyRow(nestedMethodRow);
        Assert.That(nestedBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(nestedBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(nestedMethodRow);
        var nestedBody = tables.Bodies.GetRegisterBody(nestedBodyRow,
            tables.Contracts.Count);
        Assert.That(nestedBody.BlockCount, Is.EqualTo(5));
        Assert.That(nestedBody.RecordCount, Is.EqualTo(3));
        Assert.That(nestedBody.GetRecord(1).ToArray(),
            Is.EqualTo(new byte[] { 0x06, 0, 0, 0, 1, 0, 2, 0 }));
        Assert.That(nestedBody.GetRecord(2).ToArray(),
            Is.EqualTo(new byte[] { 0x06, 0, 0, 0, 3, 0, 4, 0 }));
        Assert.That(nestedBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0, 0xE4, 0, 1, 0,
            }));
        Assert.That(nestedBody.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0, 0xE4, 0, 2, 0,
            }));
        Assert.That(nestedBody.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 4, 0, 0x17, 0,
            }));
        Assert.That(nestedBody.GetBlockCode(3).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 2, 0, 0x17, 0,
            }));
        Assert.That(nestedBody.GetBlockCode(4).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 3, 0, 0x17, 0,
            }));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SelectInstance", out var instanceDefinition), Is.True);
        var instanceMethodRow
            = checked((int)(instanceDefinition & 0x00FF_FFFF));
        var instanceBodyRow
            = tables.Methods.GetBodyRow(instanceMethodRow);
        Assert.That(instanceBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(instanceBodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(instanceMethodRow);
        var instanceBody = tables.Bodies.GetRegisterBody(instanceBodyRow,
            tables.Contracts.Count);
        Assert.That(instanceBody.BlockCount, Is.EqualTo(3));
        Assert.That(instanceBody.HomeCount, Is.EqualTo(3));
        Assert.That(instanceBody.ConstantCount, Is.EqualTo(1));
        Assert.That(instanceBody.ConstantMap.Length, Is.EqualTo(8));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    instanceBody.ConstantMap),
                tables.Signatures.GetResultTypeSpec(
                    tables.Methods.GetSignature(instanceMethodRow))),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                instanceBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(instanceBody.HomeMap[0], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            instanceBody.HomeMap.Slice(4, 2)), Is.Zero);
        Assert.That(instanceBody.HomeMap[8], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            instanceBody.HomeMap.Slice(12, 2)), Is.Zero);
        Assert.That(instanceBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0, 0xE4, 0, 1, 0,
            }));
        Assert.That(instanceBody.GetBlockCode(1).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 2, 0, 0x17, 0,
            }));
        Assert.That(instanceBody.GetBlockCode(2).ToArray(),
            Is.EqualTo(new byte[] {
                0xD4, 0, 0, 0, 0x17, 0,
            }));
    }

    [Test]
    public void EmitsClassConstructorDefaultForInitializerAndProjection()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value = 42, void marker = ()) {
                    Value = value;
                }
                public static int32 Identity(int32 value = 11) {
                    return value;
                }
                public void Touch(int32 value = 5) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2ClassParameterDefaults",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(program, tree));
        var index = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);

        Assert.That(tables.Methods.Count, Is.EqualTo(2));
        Assert.That(tables.MethodParameterLabels.Count, Is.EqualTo(4));
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(1, 0,
            out var initializerNameRow), Is.True);
        Assert.That(tables.Strings.DecodeName(initializerNameRow),
            Is.EqualTo("value"));
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(2, 0,
            out var projectionNameRow), Is.True);
        Assert.That(tables.Strings.DecodeName(projectionNameRow),
            Is.EqualTo("value"));
        Assert.That(tables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(1, 0, out var initializerDefault),
            Is.True);
        Assert.That(tables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(2, 0, out var projectionDefault),
            Is.True);
        Assert.That(tables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(1, 1, out var initializerUnitDefault),
            Is.True);
        Assert.That(tables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(2, 1, out var projectionUnitDefault),
            Is.True);
        Assert.That(initializerDefault, Is.GreaterThan(0));
        Assert.That(projectionDefault, Is.EqualTo(initializerDefault));
        Assert.That(initializerUnitDefault, Is.GreaterThan(0));
        Assert.That(projectionUnitDefault,
            Is.EqualTo(initializerUnitDefault));
        var initializerUnitNode = checked((int)tables.ConstantSources
            .GetPayloadReference(checked((int)initializerUnitDefault)));
        Assert.That(tables.FrozenValueNodes.GetScalarPayload(
                initializerUnitNode, tables.Blobs).ToArray(),
            Is.Empty);
        Assert.That(tables.ConstantSources.GetKind(
            checked((int)initializerDefault)), Is.EqualTo(1));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                artifact),
            "Construction2ClassParameterDefaults", [], ["BoxKey"]);
        var classView = reference.StandardClassDeclarations.Single(
            static declaration => declaration.Declaration.Name == "Box")
            .Declaration;
        var initializerView = classView.Methods.Single(
            static method => method.ConstructionRole == 1);
        var projectionView = classView.Methods.Single(
            static method => method.ConstructionRole == 2);
        Assert.That(initializerView.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(42));
        Assert.That(projectionView.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(42));
        Assert.That(initializerView.Parameters[1].DefaultValue!.Type,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(projectionView.Parameters[1].DefaultValue!.Type,
            Is.EqualTo(PrimitiveType.Void));

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            var sectionBytes = bytes.ToArray();
            if (kind == 0x0066)
            {
                System.Buffers.Binary.BinaryPrimitives
                    .WriteUInt32LittleEndian(
                        sectionBytes.AsSpan(28, sizeof(uint)), 0);
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)count), sectionBytes));
        }

        var mismatchedProjection = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft);
        Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                            mismatchedProjection)));
    }

    [Test]
    public void EmitsReadonlyInstanceScalarParameterReturnHomeBody()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value = 7) { Value = value; }
                public int32 Echo(void marker = (), int32 value = 13) {
                    return value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var floatingTypeCases = new (string Name, byte TypeCode)[] {
            ("AddFloat16", 0x12),
            ("AddBFloat16", 0x13),
            ("AddFloat32", 0x14),
            ("AddFloat64", 0x15),
            ("AddFloat128", 0x16),
            ("AddDecimal32", 0x17),
            ("AddDecimal64", 0x18),
            ("AddDecimal128", 0x19),
        };
        var floatingControlCases = new (string Name, byte Control)[] {
            ("SubtractFloat64", 0x21),
            ("MultiplyFloat64", 0x22),
            ("DivideFloat64", 0x23),
            ("RemainderFloat64", 0x24),
        };
        var floatingAddExports = floatingTypeCases.Select(item =>
        {
            var member = members.Single(node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    StringComparer.Ordinal.Equals(
                        token.Token.Name, item.Name)));
            return new TrRmSourceClassMemberExport(0, member.Span,
                "Box." + item.Name);
        }).ToArray();
        var floatingControlExports = floatingControlCases.Select(item =>
        {
            var member = members.Single(node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    StringComparer.Ordinal.Equals(
                        token.Token.Name, item.Name)));
            return new TrRmSourceClassMemberExport(0, member.Span,
                "Box." + item.Name);
        }).ToArray();
        var floatingCallBodyNames = new[] {
            "StaticAddFloat64AfterCall",
            "StaticAddFloat64AfterCallConstant",
            "InstanceStaticAddFloat64AfterCall",
            "LessFloat64AfterCall",
            "LessFloat64AfterInstanceCall",
        };
        var floatingCallBodyExports = floatingCallBodyNames.Select(name =>
        {
            var member = members.Single(node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    StringComparer.Ordinal.Equals(
                        token.Token.Name, name)));
            return new TrRmSourceClassMemberExport(0, member.Span,
                "Box." + name);
        }).ToArray();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var echo = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Echo"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "InstanceReturnHome",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, echo.Span,
                            "Box.Echo"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Box.Echo",
            out var echoDefinition), Is.True);
        var echoMethodRow = checked((int)(echoDefinition & 0x00FF_FFFF));
        var echoBodyRow = tables.Methods.GetBodyRow(echoMethodRow);
        Assert.That(echoBodyRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(echoMethodRow);
        var echoBody = tables.Bodies.GetRegisterBody(echoBodyRow);
        Assert.That(echoBody.HomeCount, Is.EqualTo(3));
        Assert.That(echoBody.HomeMap[0], Is.EqualTo(1));
        Assert.That(echoBody.HomeMap[8], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                echoBody.HomeMap.Slice(12, sizeof(ushort))), Is.Zero);
        Assert.That(echoBody.HomeMap[16], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                echoBody.HomeMap.Slice(20, sizeof(ushort))), Is.EqualTo(1));
        Assert.That(echoBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0xEB, 0, 2, 0 }));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                artifact),
            "InstanceReturnHome", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Use(Box value) { return value.Echo(); }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var use = ((BoundProgram)consumer.Program!).Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(1));
        var arguments = calls[0].Arguments;
        Assert.That(arguments, Has.Length.EqualTo(3));
        Assert.That(arguments[1], Is.TypeOf<BoundConstant>());
        Assert.That(((BoundConstant)arguments[1]).Type,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(((BoundConstant)arguments[2]).Value, Is.EqualTo(13));
    }

    [Test]
    public void EmitsLocalUnaryReturnsForStaticAndReadonlyInstanceMethods()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public int32 NegatedCopy(int32 value) {
                    int32 copy = value;
                    return -copy;
                }
                public float32 NegatedFloatCopy(float32 value) {
                    float32 copy = value;
                    return -copy;
                }
                public int32 NegatedTwoCopies(int32 value) {
                    int32 first = value;
                    int32 second = first;
                    return -second;
                }
                public static int32 StaticNegatedCopy(int32 value) {
                    int32 copy = value;
                    return -copy;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>().ToArray();
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        SyntaxNode Method(string name)
            => members.Single(node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    StringComparer.Ordinal.Equals(token.Token.Name, name)));

        var negatedCopy = Method("NegatedCopy");
        var negatedFloatCopy = Method("NegatedFloatCopy");
        var negatedTwoCopies = Method("NegatedTwoCopies");
        var staticNegatedCopy = Method("StaticNegatedCopy");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "LocalUnaryReturns",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, classIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            field.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0,
                            negatedCopy.Span, "Box.NegatedCopy"),
                        new TrRmSourceClassMemberExport(0,
                            negatedFloatCopy.Span,
                            "Box.NegatedFloatCopy"),
                        new TrRmSourceClassMemberExport(0,
                            negatedTwoCopies.Span, "Box.NegatedTwoCopies"),
                        new TrRmSourceClassMemberExport(0,
                            staticNegatedCopy.Span, "Box.StaticNegatedCopy"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));

        byte[] AssertUnaryBody(string exportKey, string functionName,
            int expectedHomeCount)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1, exportKey,
                out var definition), Is.True);
            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            Assert.That(bodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(bodyRow,
                tables.Contracts.Count);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            Assert.That(body.HomeCount, Is.EqualTo(expectedHomeCount));

            var function = program.Functions.Single(candidate =>
                candidate.Name == functionName);
            var unary = (BoundUnary)((BoundReturn)function.Body.Statements[
                function.Body.Statements.Length - 1]).Value!;
            var (opcode, control) = unary.Type == PrimitiveType.Float32
                ? ((byte)0xC0, (byte)0x25)
                : ((byte)0xC0, unary.OverflowBehavior switch
                {
                    BoundIntegerOverflowBehavior.Checked => (byte)0x04,
                    BoundIntegerOverflowBehavior.Wrapping => (byte)0x08,
                    _ => throw new InvalidOperationException(
                        "The local-unary fixture must select checked or wrapping arithmetic."),
                });
            var code = new List<byte>();
            for (var localOrdinal = 0;
                localOrdinal < function.Variables.Length
                        - function.ParameterCount;
                localOrdinal++)
            {
                var local = (BoundLocal)function.Body
                        .Statements[localOrdinal];
                var initialRead = (BoundRead)local.Initializer!;
                var localVariable = local.Variable;
                code.Add(0xD5);
                code.Add(0);
                code.Add(checked((byte)initialRead.Variable));
                code.Add(checked((byte)(initialRead.Variable >> 8)));
                code.Add(0xD6);
                code.Add(0x10);
                code.Add(checked((byte)localVariable));
                code.Add(checked((byte)(localVariable >> 8)));
            }

            var lastLocal = function.Variables.Length - 1;
            code.Add(0xD5);
            code.Add(0);
            code.Add(checked((byte)lastLocal));
            code.Add(checked((byte)(lastLocal >> 8)));
            code.Add(opcode);
            code.Add(control);
            code.Add(0xF0);
            code.Add(0xF0);
            code.Add(0x17);
            code.Add(0);
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(code.ToArray()));
            return code.ToArray();
        }

        AssertUnaryBody("Box.NegatedCopy", "NegatedCopy",
            expectedHomeCount: 3);
        AssertUnaryBody("Box.NegatedFloatCopy", "NegatedFloatCopy",
            expectedHomeCount: 3);
        var negatedTwoCopiesCode = AssertUnaryBody("Box.NegatedTwoCopies",
            "NegatedTwoCopies", expectedHomeCount: 4);
        AssertUnaryBody("Box.StaticNegatedCopy", "StaticNegatedCopy",
            expectedHomeCount: 2);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.NegatedCopy", out var originalMethod), Is.True);
        var originalMethodRow = checked((int)(
            originalMethod & 0x00FF_FFFF));
        var originalBody = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(originalMethodRow),
            tables.Contracts.Count);

        var sourceFreeReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    artifact),
                "LocalUnaryReturns", [], ["BoxKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), sourceFreeReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "LocalUnaryReturnsReemitted",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Box.NegatedCopy", out var reemittedMethod), Is.True);
        var reemittedMethodRow = checked((int)(
            reemittedMethod & 0x00FF_FFFF));
        var reemittedBodyRow
            = reemittedTables.Methods.GetBodyRow(reemittedMethodRow);
        Assert.That(reemittedBodyRow, Is.GreaterThan(0));
        var reemittedBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedBodyRow, reemittedTables.Contracts.Count);
        Assert.That(reemittedBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            originalBody.GetBlockCode(0).ToArray()));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Box.NegatedTwoCopies", out var reemittedTwoLocalMethod),
            Is.True);
        var reemittedTwoLocalMethodRow = checked((int)(
            reemittedTwoLocalMethod & 0x00FF_FFFF));
        var reemittedTwoLocalBodyRow
            = reemittedTables.Methods.GetBodyRow(
                reemittedTwoLocalMethodRow);
        Assert.That(reemittedTwoLocalBodyRow, Is.GreaterThan(0));
        var reemittedTwoLocalBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedTwoLocalBodyRow, reemittedTables.Contracts.Count);
        Assert.That(reemittedTwoLocalBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(negatedTwoCopiesCode));
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    reemitted),
                "LocalUnaryReturnsReemitted", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Call(Box value, int32 input) {
                return value.NegatedCopy(input);
            }
            """), reemittedReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
    }

    [Test]
    public void EmitsAndReemitsBooleanLocalCopyChainReturnForReadonlyInstanceMethod()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public bool Copy(bool value) {
                    bool copy = value;
                    bool secondCopy = copy;
                    return secondCopy;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>().ToArray();
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var method = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Copy"));
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "BooleanLocalCopy", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, classIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            field.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0,
                            method.Span, "Box.Copy"),
                    ]),
            ]);

        byte[] GetBody(byte[] image)
        {
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                            image));
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "Box.Copy", out var definition), Is.True);
            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            Assert.That(bodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(bodyRow,
                tables.Contracts.Count);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            Assert.That(body.HomeCount, Is.EqualTo(4));
            return body.GetBlockCode(0).ToArray();
        }

        var bodyCode = GetBody(artifact);
        Assert.That(bodyCode, Is.EqualTo(new byte[] {
            0xD5, 0, 1, 0,
            0xD6, 0x10, 2, 0,
            0xD5, 0, 2, 0,
            0xD6, 0x10, 3, 0,
            0xD5, 0, 3, 0,
            0xEB, 0, 3, 0,
        }));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "BooleanLocalCopy", [], ["BoxKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "BooleanLocalCopyReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        Assert.That(GetBody(reemitted), Is.EqualTo(bodyCode));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "BooleanLocalCopyReemitted", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            bool Use(Box value, bool input) { return value.Copy(input); }
            """), reemittedReference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
    }

    [Test]
    public void EmitsNestedInlineStructFieldReadThroughPlaceSelections()
    {
        var tree = SyntaxTree.Parse("""
            public struct Point {
                public int32 X;
            }
            public struct Position {
                public Point Location;
            }
            public class Box {
                private Position _position;
                public Box(Position position) { _position = position; }
                public int32 ReadX() { return _position.Location.X; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ReadX"));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "NestedClassFieldRead",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, read.Span,
                            "Box.ReadX"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.ReadX", out var readDefinition), Is.True);
        var methodRow = checked((int)(readDefinition & 0x00FF_FFFF));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        Assert.That(tables.Bodies.GetRow(bodyRow)[0], Is.EqualTo(0x01));
        tables.Bodies.ValidateSelectedImplementation(bodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(methodRow);

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(4));
        Assert.That(body.GetRecord(1)[0], Is.EqualTo(0x04));
        Assert.That(body.GetRecord(1)[7], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(1).Slice(8, 2)), Is.EqualTo(1));
        Assert.That(body.GetRecord(1)[13], Is.EqualTo(1));
        Assert.That(body.GetRecord(2)[7], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(2).Slice(8, 2)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(2).Slice(10, 2)), Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[7], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(3).Slice(8, 2)), Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(3).Slice(10, 2)), Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD8, 1, 1, 0,
                0xD8, 1, 2, 0,
                0xDA, 0, 3, 0,
                0x17, 0,
            }));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                artifact),
            "NestedClassFieldRead", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Read(Box value) { return value.ReadX(); }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
    }

    [Test]
    public void EmitsClassFieldBodiesThroughImportedStructFieldStubs()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var providerTree = SyntaxTree.Parse("""
            public struct Leaf {
                public mut int32 Id;
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ImportedStructProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [
                new TrRmSourceTypeExport(string.Empty, "Leaf", "LeafKey") {
                    FieldExports = [
                        new TrRmSourceFieldExport("Id", "Leaf.Id"),
                    ],
                },
            ]);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            "ImportedStructProvider", [], ["LeafKey"]);

        var tree = SyntaxTree.Parse("""
            public mut class Box {
                private mut Leaf _leaf;
                public Box(Leaf leaf) { _leaf = leaf; }
                public int32 ReadId() { return _leaf.Id; }
                public mut void SetId(int32 value) {
                    this._leaf.Id = value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ReadId"));
        var write = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SetId"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ImportedClassFieldBodies", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, read.Span,
                            "Box.ReadId"),
                        new TrRmSourceClassMemberExport(0, write.Span,
                            "Box.SetId"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        Assert.That(Enumerable.Range(1, tables.Imports.Count)
                .Select(row => tables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24),
            Is.EquivalentTo([0x13u, 0x14u]));

        var leafTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Leaf");
        var (leafFieldRow, _) = tables.TypeDefs.GetFieldRange(
            leafTypeDefRow);
        var boxTypeDefRow = Enumerable.Range(1, tables.TypeDefs.Count)
            .Single(row => tables.TypeDefs.GetName(row) == "Box");
        var (boxFieldRow, _) = tables.TypeDefs.GetFieldRange(boxTypeDefRow);
        var fieldType = tables.TypeSpecs.GetTypeSpecRef(
            tables.Fields.GetTypeSpecRef(boxFieldRow));
        Assert.That(fieldType.Form, Is.EqualTo((byte)0x03));
        Assert.That(fieldType.Row, Is.EqualTo(1));
        Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(1),
            Is.EqualTo(0x1300_0000u | checked((uint)leafTypeDefRow)));
        Assert.That(tables.Imports.GetExpectedDefinitionForImportRow(2),
            Is.EqualTo(0x1400_0000u | checked((uint)leafFieldRow)));

        var referenceToBox = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "ImportedClassFieldBodies", [
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact),
            ], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                int32 Read(Box value) { return value.ReadId(); }
                """), referenceToBox);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        var incompatibleProviderTree = SyntaxTree.Parse("""
            public struct Leaf {
                public mut int64 Id;
            }
            """);
        var incompatibleProviderCompilation
            = Compilation.CreateLibrary(incompatibleProviderTree);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)incompatibleProviderCompilation.Program!,
                "ImportedStructProvider", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [
                    new TrRmSourceTypeExport(string.Empty, "Leaf",
                        "LeafKey") {
                        FieldExports = [
                            new TrRmSourceFieldExport("Id", "Leaf.Id"),
                        ],
                    },
                ]);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "ImportedClassFieldBodies", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProviderArtifact),
                ], ["BoxKey"]));
    }

    [Test]
    public void EmitsNestedInlineStructFieldWriteThroughPlaceSelections()
    {
        var tree = SyntaxTree.Parse("""
            public struct Point {
                public mut int32 X;
            }
            public struct Position {
                public mut Point Location;
            }
            public mut class Box {
                private mut Position _position;
                public Box(Position position) { _position = position; }
                public mut void SetX(int32 value) {
                    this._position.Location.X = value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var set = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SetX"));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "NestedClassFieldWrite",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, set.Span,
                            "Box.SetX"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SetX", out var setDefinition), Is.True);
        var methodRow = checked((int)(setDefinition & 0x00FF_FFFF));
        var bodyRow = tables.Methods.GetBodyRow(methodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        Assert.That(tables.Bodies.GetRow(bodyRow)[0], Is.EqualTo(0x01));
        tables.Bodies.ValidateSelectedImplementation(bodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(methodRow);

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(5));
        for (var recordIndex = 1; recordIndex <= 3; recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            Assert.That(record[0], Is.EqualTo(0x04));
            Assert.That(record[5], Is.EqualTo(2));
            Assert.That(record[7], Is.EqualTo(
                recordIndex == 1 ? 2 : 1));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(8, 2)), Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                record.Slice(10, 2)),
                Is.EqualTo(recordIndex == 1 ? 0 : 1));
        }

        var store = body.GetRecord(4);
        Assert.That(store[0], Is.EqualTo(0x05));
        Assert.That(store[4], Is.EqualTo(1));
        Assert.That(store[5], Is.EqualTo(2));
        Assert.That(store[6], Is.EqualTo(2));
        Assert.That(store[7], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            store.Slice(8, 2)), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            store.Slice(10, 2)), Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD8, 1, 1, 0,
                0xD8, 1, 2, 0,
                0xD8, 1, 3, 0,
                0xD9, 0, 4, 0,
                0x18, 0,
            }));
    }

    [Test]
    public void EmitsFieldReadDescriptorForReadonlyInstanceMethod()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public int32 ReadValue() { return this.Value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ReadValue"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FieldReadInstanceMethod",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, read.Span,
                            "Box.ReadValue"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.ReadValue", out var readDefinition), Is.True);
        var readMethodRow = checked((int)(readDefinition & 0x00FF_FFFF));
        var readBodyRow = tables.Methods.GetBodyRow(readMethodRow);
        Assert.That(readBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(readBodyRow,
            tables.Contracts.Count);
        var readBody = tables.Bodies.GetRow(readBodyRow);
        Assert.That(readBody[0], Is.EqualTo(0x02));
        var fieldDefinition = BinaryPrimitives.ReadUInt32LittleEndian(
            readBody[4..]);
        Assert.That(fieldDefinition >> 24, Is.EqualTo(0x14u));
        var fieldRow = checked((int)(fieldDefinition & 0x00FF_FFFF));
        Assert.That(tables.Fields.GetOwnerTypeDef(fieldRow),
            Is.EqualTo(tables.Methods.GetOwnerType(readMethodRow)));
        var signature = tables.Methods.GetSignature(readMethodRow);
        Assert.That(tables.Signatures.GetReceiverTypeSpec(signature),
            Is.Not.Zero);
        Assert.That(tables.Signatures.GetParamRange(signature).Count,
            Is.Zero);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                tables.Fields.GetTypeSpecRef(fieldRow),
                tables.Signatures.GetResultTypeSpec(signature)),
            Is.True);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                artifact),
            "FieldReadInstanceMethod", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Use(Box value) { return value.ReadValue(); }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
    }

    [Test]
    public void EmitsCopyFieldWriteDescriptorForMutableInstanceMethod()
    {
        var tree = SyntaxTree.Parse("""
            public mut class Box {
                public mut int32 Value;
                public Box(int32 value) { Value = value; }
                public mut void Set(int32 value) { this.Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var set = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Set"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FieldWriteInstanceMethod",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, set.Span,
                            "Box.Set"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Box.Set",
            out var setDefinition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Box.Value",
            out var fieldDefinition), Is.True);
        var setMethodRow = checked((int)(setDefinition & 0x00FF_FFFF));
        Assert.That(tables.Methods.GetFlags(setMethodRow) & 4u,
            Is.EqualTo(4u));
        var ownerTypeDefRow = tables.Methods.GetOwnerType(setMethodRow);
        Assert.That(tables.TypeDefs.GetFlags(ownerTypeDefRow) & 0x80u,
            Is.EqualTo(0x80u));
        var signature = tables.Methods.GetSignature(setMethodRow);
        var receiverTypeSpec = tables.Signatures
            .GetReceiverTypeSpec(signature);
        Assert.That(receiverTypeSpec, Is.Not.Zero);
        var receiverType = tables.TypeSpecs
            .GetTypeSpecRef(receiverTypeSpec);
        Assert.That(receiverType.Qualifiers & 1, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.GetNominalTypeDefRow(
                receiverTypeSpec, tables.TypeDefs, tables.Imports),
            Is.EqualTo(ownerTypeDefRow));
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var bodyRow = tables.Methods.GetBodyRow(setMethodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(bodyRow,
            tables.Contracts.Count);
        var body = tables.Bodies.GetRow(bodyRow);
        Assert.That(body[0], Is.EqualTo(0x03));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[4..]),
            Is.EqualTo(fieldDefinition));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(body[8..]),
            Is.Zero);
        Assert.That(body[10], Is.EqualTo(1));
        Assert.That(body[11], Is.Zero);
        var fieldRow = checked((int)(fieldDefinition & 0x00FF_FFFF));
        Assert.That(tables.Fields.GetOwnerTypeDef(fieldRow),
            Is.EqualTo(ownerTypeDefRow));
        Assert.That(tables.Fields.GetFlags(fieldRow) & 4u,
            Is.EqualTo(4u));
        Assert.That(tables.Fields.GetFlags(fieldRow) & 8u, Is.Zero);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                tables.Fields.GetTypeSpecRef(fieldRow),
                tables.Parameters.GetTypeSpec(parameterStart)),
            Is.True);
    }

    [Test]
    public void EmitsWritableReceiverForSameClassCallBody()
    {
        var tree = SyntaxTree.Parse("""
            public mut class Crate {
                public mut int32 Value;
                public Crate(int32 value) { Value = value; }
                public mut int32 Step(int32 left, int32 right) {
                    return left;
                }
                public mut int32 Combine(int32 left, int32 right) {
                    return Step(left, right) + right;
                }
                public int32 Observe(int32 left, int32 right) {
                    return left;
                }
                public mut int32 CombineReadOnly(
                    int32 left, int32 right) {
                    return Observe(left, right) + right;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var step = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Step"));
        var combine = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Combine"));
        var observe = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Observe"));
        var combineReadOnly = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "CombineReadOnly"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2WritableReceiverCall",
            C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "type.crate", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "field.crate.value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "member.crate.constructor") {
                            ConstructionProjectionExportKey
                                = "member.crate.create",
                        },
                        new TrRmSourceClassMemberExport(0, step.Span,
                            "member.crate.step"),
                        new TrRmSourceClassMemberExport(0, combine.Span,
                            "member.crate.combine"),
                        new TrRmSourceClassMemberExport(0, observe.Span,
                            "member.crate.observe"),
                        new TrRmSourceClassMemberExport(0,
                            combineReadOnly.Span,
                            "member.crate.combine-readonly"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "member.crate.combine", out var combineDefinition), Is.True);
        var combineMethodRow = checked((int)(
            combineDefinition & 0x00FF_FFFF));
        var signature = tables.Methods.GetSignature(combineMethodRow);
        var receiverTypeSpec = tables.Signatures.GetReceiverTypeSpec(
            signature);
        Assert.That(tables.Methods.GetFlags(combineMethodRow) & 4,
            Is.Zero);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(receiverTypeSpec)
            .Qualifiers, Is.EqualTo(1));
        var bodyRow = tables.Methods.GetBodyRow(combineMethodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var callSite = body.GetRecord(1);
        Assert.That(callSite[0], Is.EqualTo(0x02));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(4));

        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "member.crate.combine-readonly",
            out var readOnlyDefinition), Is.True);
        var combineReadOnlyMethodRow = checked((int)(
            readOnlyDefinition & 0x00FF_FFFF));
        var combineReadOnlyBodyRow
            = tables.Methods.GetBodyRow(combineReadOnlyMethodRow);
        Assert.That(combineReadOnlyBodyRow, Is.GreaterThan(0));
        var combineReadOnlyBody = tables.Bodies.GetRegisterBody(
            combineReadOnlyBodyRow, tables.Contracts.Count);
        var readOnlyCallSite = combineReadOnlyBody.GetRecord(1);
        Assert.That(readOnlyCallSite[0], Is.EqualTo(0x02));
        Assert.That(readOnlyCallSite[20], Is.EqualTo(2));
        Assert.That(readOnlyCallSite[21], Is.EqualTo(3));
    }

    [Test]
    public void EmitsRegisterBodyForReadonlyInstanceXorMethod()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public int32 Xor(int32 left, int32 right) {
                    return left ^ right;
                }
                public int32 Sum(int32 left, int32 right) {
                    return left + right;
                }
                public int32 Difference(int32 left, int32 right) {
                    return right - left;
                }
                public int32 Negated(int32 value) {
                    return -value;
                }
                public int64 Negate64(int64 value) {
                    return -value;
                }
                public float32 NegateFloat32(float32 value) {
                    return -value;
                }
                public int32 Complemented(int32 value) {
                    return ~value;
                }
                public static int32 StaticXor(int32 left, int32 right) {
                    return left ^ right;
                }
                public static int32 Add(int32 left, int32 right) {
                    return left + right;
                }
                public static int64 Add64(int64 left, int64 right) {
                    return left + right;
                }
                public static float16 AddFloat16(
                    float16 left, float16 right) {
                    return left + right;
                }
                public static bfloat16 AddBFloat16(
                    bfloat16 left, bfloat16 right) {
                    return left + right;
                }
                public static float32 AddFloat32(
                    float32 left, float32 right) {
                    return left + right;
                }
                public static float64 AddFloat64(
                    float64 left, float64 right) {
                    return left + right;
                }
                public static float64 SubtractFloat64(
                    float64 left, float64 right) {
                    return left - right;
                }
                public static float64 MultiplyFloat64(
                    float64 left, float64 right) {
                    return left * right;
                }
                public static float64 DivideFloat64(
                    float64 left, float64 right) {
                    return left / right;
                }
                public static float64 RemainderFloat64(
                    float64 left, float64 right) {
                    return left % right;
                }
                public float64 AddFloat64Instance(
                    float64 left, float64 right) {
                    return left + right;
                }
                public static float64 AddFloat64Constant(
                    float64 left, float64 right) {
                    return left + 0.5;
                }
                public static float128 AddFloat128(
                    float128 left, float128 right) {
                    return left + right;
                }
                public static decimal32 AddDecimal32(
                    decimal32 left, decimal32 right) {
                    return left + right;
                }
                public static decimal64 AddDecimal64(
                    decimal64 left, decimal64 right) {
                    return left + right;
                }
                public static decimal128 AddDecimal128(
                    decimal128 left, decimal128 right) {
                    return left + right;
                }
                public static bool LessFloat64(
                    float64 left, float64 right) {
                    return left < right;
                }
                public bool LessFloat64Instance(
                    float64 left, float64 right) {
                    return left < right;
                }
                public int64 Sum64(int64 left, int64 right) {
                    return left + right;
                }
                public static uint128 Xor128(uint128 left, uint128 right) {
                    return left ^ right;
                }
                public static int64 Sum64ThroughTwoLocals(
                    int64 left, int64 right) {
                    int64 first = left;
                    int64 second = first;
                    return second + right;
                }
                public static int64 Sum64ThroughLocalConstant(
                    int64 left, int64 right) {
                    int64 first = left;
                    int64 second = first;
                    return second + 7;
                }
                public static float64 SumFloat64ThroughTwoLocals(
                    float64 left, float64 right) {
                    float64 first = left;
                    float64 second = first;
                    return second + right;
                }
                public static float64 SumFloat64ThroughLocalConstant(
                    float64 left, float64 right) {
                    float64 first = left;
                    float64 second = first;
                    return second + 0.5;
                }
                public static bool Greater64(int64 left, int64 right) {
                    return left > right;
                }
                public static int32 StaticAddAfterCall(
                    int32 left, int32 right) {
                    return Add(left, right) + right;
                }
                public static int64 StaticAdd64AfterCall(
                    int64 left, int64 right) {
                    return Add64(left, right) + right;
                }
                public static int64 StaticAdd64AfterCallConstant(
                    int64 left, int64 right) {
                    return Add64(left, right) + 7;
                }
                public static float64 StaticAddFloat64AfterCall(
                    float64 left, float64 right) {
                    return AddFloat64(left, right) + right;
                }
                public static float64 StaticAddFloat64AfterCallConstant(
                    float64 left, float64 right) {
                    return AddFloat64(left, right) + 0.5;
                }
                public int32 InstanceStaticAddAfterCall(
                    int32 left, int32 right) {
                    return Add(left, right) + right;
                }
                public int64 InstanceStaticAdd64AfterCall(
                    int64 left, int64 right) {
                    return Add64(left, right) + right;
                }
                public float64 InstanceStaticAddFloat64AfterCall(
                    float64 left, float64 right) {
                    return AddFloat64(left, right) + right;
                }
                public static bool Greater64AfterCall(
                    int64 left, int64 right) {
                    return Add64(left, right) > right;
                }
                public bool Greater64AfterInstanceCall(
                    int64 left, int64 right) {
                    return Sum64(left, right) > right;
                }
                public bool LessFloat64AfterCall(
                    float64 left, float64 right) {
                    return AddFloat64(left, right) < right;
                }
                public bool LessFloat64AfterInstanceCall(
                    float64 left, float64 right) {
                    return AddFloat64Instance(left, right) < right;
                }
                public static int32 Subtract(int32 left, int32 right) {
                    return right - left;
                }
                public static int32 Multiply(int32 left, int32 right) {
                    return left * right;
                }
                public static int32 Divide(int32 left, int32 right) {
                    return left / right;
                }
                public static int32 AddConstant(int32 value) {
                    return value + 7;
                }
                public static int32 SubtractFromConstant(int32 value) {
                    return 7 - value;
                }
                public int32 Remainder(int32 left, int32 right) {
                    return left % right;
                }
                public int32 SubtractFromSeven(int32 value) {
                    return 7 - value;
                }
                public bool GreaterThanSeven(int32 value) {
                    return value > 7;
                }
                public static int32 Double(int32 value) {
                    return value + value;
                }
                public static int32 Negate(int32 value) {
                    return -value;
                }
                public static int32 Complement(int32 value) {
                    return ~value;
                }
                public static int32 CopyStatic(int32 value) {
                    int32 copy = value;
                    return copy;
                }
                public static int64 Copy64Static(int64 value) {
                    int64 copy = value;
                    return copy;
                }
                public static bool LessThan(int32 left, int32 right) {
                    return left < right;
                }
                public static bool EqualTo(int32 left, int32 right) {
                    return left == right;
                }
                public static bool LessOrEqual(int32 left, int32 right) {
                    return left <= right;
                }
                public bool GreaterThan(int32 left, int32 right) {
                    return left > right;
                }
                public static bool GreaterOrEqual(int32 left, int32 right) {
                    return right >= left;
                }
                public bool NotEqual(int32 left, int32 right) {
                    return left != right;
                }
                public static bool Same(bool left, bool right) {
                    return left == right;
                }
                public bool Different(bool left, bool right) {
                    return left != right;
                }
                public int32 SumAfterCall(int32 left, int32 right) {
                    return Sum(left, right) + right;
                }
                public bool LessThanAfterCall(int32 left, int32 right) {
                    return Sum(left, right) < right;
                }
                public static int32 StaticXorThroughLocal(
                    int32 left, int32 right) {
                    int32 copy = left;
                    return copy ^ right;
                }
                public static int32 SumThroughLocal(
                    int32 left, int32 right) {
                    int32 copy = left;
                    return copy + right;
                }
                public static int32 SumThroughTwoLocals(
                    int32 left, int32 right) {
                    int32 first = left;
                    int32 second = first;
                    return second + right;
                }
                public static bool LessThroughLocal(
                    int32 left, int32 right) {
                    int32 copy = left;
                    return copy < right;
                }
                public static int32 AddThroughLocalConstant(
                    int32 value) {
                    int32 copy = value;
                    return copy + 7;
                }
                public static bool LessThroughLocalConstant(
                    int32 value) {
                    int32 copy = value;
                    return copy < 7;
                }
                public int32 XorAfterCall(int32 left, int32 right) {
                    return Xor(left, right) ^ right;
                }
                public int32 CopyThroughLocal(int32 value) {
                    int32 copy = value;
                    return copy;
                }
                public int64 Copy64ThroughLocal(int64 value) {
                    int64 copy = value;
                    return copy;
                }
                public float64 CopyFloat64ThroughLocal(float64 value) {
                    float64 copy = value;
                    return copy;
                }
                public int32 XorThroughLocal(int32 left, int32 right) {
                    int32 copy = left;
                    return copy ^ right;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var xor = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Xor"));
        var sum = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Sum"));
        var difference = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Difference"));
        var negated = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Negated"));
        var negate64 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Negate64"));
        var negateFloat32 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "NegateFloat32"));
        var complemented = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Complemented"));
        var staticXor = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "StaticXor"));
        var add64 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Add64"));
        var addFloat64Constant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "AddFloat64Constant"));
        var lessFloat64 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "LessFloat64"));
        var addFloat64Instance = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "AddFloat64Instance"));
        var lessFloat64Instance = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "LessFloat64Instance"));
        var sum64 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Sum64"));
        var xor128 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Xor128"));
        var sum64ThroughTwoLocals = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "Sum64ThroughTwoLocals"));
        var sum64ThroughLocalConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "Sum64ThroughLocalConstant"));
        var sumFloat64ThroughTwoLocals = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "SumFloat64ThroughTwoLocals"));
        var sumFloat64ThroughLocalConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "SumFloat64ThroughLocalConstant"));
        var greater64 = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Greater64"));
        var add = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Add"));
        var staticAddAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "StaticAddAfterCall"));
        var staticAdd64AfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "StaticAdd64AfterCall"));
        var staticAdd64AfterCallConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "StaticAdd64AfterCallConstant"));
        var instanceStaticAddAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "InstanceStaticAddAfterCall"));
        var instanceStaticAdd64AfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "InstanceStaticAdd64AfterCall"));
        var greater64AfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Greater64AfterCall"));
        var greater64AfterInstanceCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "Greater64AfterInstanceCall"));
        var subtract = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Subtract"));
        var multiply = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Multiply"));
        var divide = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Divide"));
        var addConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "AddConstant"));
        var subtractFromConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SubtractFromConstant"));
        var remainder = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Remainder"));
        var subtractFromSeven = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SubtractFromSeven"));
        var greaterThanSeven = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "GreaterThanSeven"));
        var doubleValue = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Double"));
        var negate = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Negate"));
        var complement = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Complement"));
        var copyStatic = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "CopyStatic"));
        var copy64Static = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Copy64Static"));
        var lessThan = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "LessThan"));
        var equalTo = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "EqualTo"));
        var lessOrEqual = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "LessOrEqual"));
        var greaterThan = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "GreaterThan"));
        var greaterOrEqual = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "GreaterOrEqual"));
        var notEqual = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "NotEqual"));
        var same = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Same"));
        var different = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Different"));
        var staticXorThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "StaticXorThroughLocal"));
        var sumThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SumThroughLocal"));
        var sumThroughTwoLocals = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SumThroughTwoLocals"));
        var lessThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "LessThroughLocal"));
        var addThroughLocalConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "AddThroughLocalConstant"));
        var lessThroughLocalConstant = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "LessThroughLocalConstant"));
        var xorAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "XorAfterCall"));
        var sumAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "SumAfterCall"));
        var lessThanAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "LessThanAfterCall"));
        var copyThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "CopyThroughLocal"));
        var copy64ThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Copy64ThroughLocal"));
        var copyFloat64ThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "CopyFloat64ThroughLocal"));
        var xorThroughLocal = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "XorThroughLocal"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassInstanceXor", C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, xor.Span,
                            "Box.Xor"),
                        new TrRmSourceClassMemberExport(0, sum.Span,
                            "Box.Sum"),
                        new TrRmSourceClassMemberExport(0, difference.Span,
                            "Box.Difference"),
                        new TrRmSourceClassMemberExport(0, negated.Span,
                            "Box.Negated"),
                        new TrRmSourceClassMemberExport(0, negate64.Span,
                            "Box.Negate64"),
                        new TrRmSourceClassMemberExport(0,
                            negateFloat32.Span, "Box.NegateFloat32"),
                        new TrRmSourceClassMemberExport(0,
                            complemented.Span, "Box.Complemented"),
                        new TrRmSourceClassMemberExport(0, staticXor.Span,
                            "Box.StaticXor"),
                        new TrRmSourceClassMemberExport(0, add.Span,
                            "Box.Add"),
                        new TrRmSourceClassMemberExport(0, add64.Span,
                            "Box.Add64"),
                        ..floatingAddExports,
                        ..floatingControlExports,
                        ..floatingCallBodyExports,
                        new TrRmSourceClassMemberExport(0,
                            addFloat64Constant.Span,
                            "Box.AddFloat64Constant"),
                        new TrRmSourceClassMemberExport(0,
                            lessFloat64.Span, "Box.LessFloat64"),
                        new TrRmSourceClassMemberExport(0,
                            addFloat64Instance.Span,
                            "Box.AddFloat64Instance"),
                        new TrRmSourceClassMemberExport(0,
                            lessFloat64Instance.Span,
                            "Box.LessFloat64Instance"),
                        new TrRmSourceClassMemberExport(0, sum64.Span,
                            "Box.Sum64"),
                        new TrRmSourceClassMemberExport(0, xor128.Span,
                            "Box.Xor128"),
                        new TrRmSourceClassMemberExport(0,
                            sum64ThroughTwoLocals.Span,
                            "Box.Sum64ThroughTwoLocals"),
                        new TrRmSourceClassMemberExport(0,
                            sum64ThroughLocalConstant.Span,
                            "Box.Sum64ThroughLocalConstant"),
                        new TrRmSourceClassMemberExport(0,
                            sumFloat64ThroughTwoLocals.Span,
                            "Box.SumFloat64ThroughTwoLocals"),
                        new TrRmSourceClassMemberExport(0,
                            sumFloat64ThroughLocalConstant.Span,
                            "Box.SumFloat64ThroughLocalConstant"),
                        new TrRmSourceClassMemberExport(0, greater64.Span,
                            "Box.Greater64"),
                        new TrRmSourceClassMemberExport(0,
                            staticAddAfterCall.Span,
                            "Box.StaticAddAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            staticAdd64AfterCall.Span,
                            "Box.StaticAdd64AfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            staticAdd64AfterCallConstant.Span,
                            "Box.StaticAdd64AfterCallConstant"),
                        new TrRmSourceClassMemberExport(0,
                            instanceStaticAddAfterCall.Span,
                            "Box.InstanceStaticAddAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            instanceStaticAdd64AfterCall.Span,
                            "Box.InstanceStaticAdd64AfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            greater64AfterCall.Span,
                            "Box.Greater64AfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            greater64AfterInstanceCall.Span,
                            "Box.Greater64AfterInstanceCall"),
                        new TrRmSourceClassMemberExport(0, subtract.Span,
                            "Box.Subtract"),
                        new TrRmSourceClassMemberExport(0, multiply.Span,
                            "Box.Multiply"),
                        new TrRmSourceClassMemberExport(0, divide.Span,
                            "Box.Divide"),
                        new TrRmSourceClassMemberExport(0, addConstant.Span,
                            "Box.AddConstant"),
                        new TrRmSourceClassMemberExport(0,
                            subtractFromConstant.Span,
                            "Box.SubtractFromConstant"),
                        new TrRmSourceClassMemberExport(0, remainder.Span,
                            "Box.Remainder"),
                        new TrRmSourceClassMemberExport(0,
                            subtractFromSeven.Span,
                            "Box.SubtractFromSeven"),
                        new TrRmSourceClassMemberExport(0,
                            greaterThanSeven.Span,
                            "Box.GreaterThanSeven"),
                        new TrRmSourceClassMemberExport(0, doubleValue.Span,
                            "Box.Double"),
                        new TrRmSourceClassMemberExport(0, negate.Span,
                            "Box.Negate"),
                        new TrRmSourceClassMemberExport(0, complement.Span,
                            "Box.Complement"),
                        new TrRmSourceClassMemberExport(0, copyStatic.Span,
                            "Box.CopyStatic"),
                        new TrRmSourceClassMemberExport(0, copy64Static.Span,
                            "Box.Copy64Static"),
                        new TrRmSourceClassMemberExport(0, lessThan.Span,
                            "Box.LessThan"),
                        new TrRmSourceClassMemberExport(0, equalTo.Span,
                            "Box.EqualTo"),
                        new TrRmSourceClassMemberExport(0, lessOrEqual.Span,
                            "Box.LessOrEqual"),
                        new TrRmSourceClassMemberExport(0,
                            greaterThan.Span, "Box.GreaterThan"),
                        new TrRmSourceClassMemberExport(0,
                            greaterOrEqual.Span, "Box.GreaterOrEqual"),
                        new TrRmSourceClassMemberExport(0, notEqual.Span,
                            "Box.NotEqual"),
                        new TrRmSourceClassMemberExport(0, same.Span,
                            "Box.Same"),
                        new TrRmSourceClassMemberExport(0, different.Span,
                            "Box.Different"),
                        new TrRmSourceClassMemberExport(0,
                            staticXorThroughLocal.Span,
                            "Box.StaticXorThroughLocal"),
                        new TrRmSourceClassMemberExport(0,
                            sumThroughLocal.Span, "Box.SumThroughLocal"),
                        new TrRmSourceClassMemberExport(0,
                            sumThroughTwoLocals.Span,
                            "Box.SumThroughTwoLocals"),
                        new TrRmSourceClassMemberExport(0,
                            lessThroughLocal.Span, "Box.LessThroughLocal"),
                        new TrRmSourceClassMemberExport(0,
                            addThroughLocalConstant.Span,
                            "Box.AddThroughLocalConstant"),
                        new TrRmSourceClassMemberExport(0,
                            lessThroughLocalConstant.Span,
                            "Box.LessThroughLocalConstant"),
                        new TrRmSourceClassMemberExport(0,
                            xorAfterCall.Span, "Box.XorAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            sumAfterCall.Span, "Box.SumAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            lessThanAfterCall.Span, "Box.LessThanAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            copyThroughLocal.Span, "Box.CopyThroughLocal"),
                        new TrRmSourceClassMemberExport(0,
                            copy64ThroughLocal.Span,
                            "Box.Copy64ThroughLocal"),
                        new TrRmSourceClassMemberExport(0,
                            copyFloat64ThroughLocal.Span,
                            "Box.CopyFloat64ThroughLocal"),
                        new TrRmSourceClassMemberExport(0,
                            xorThroughLocal.Span, "Box.XorThroughLocal"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Box.Xor",
            out var xorDefinition), Is.True);
        var xorMethodRow
            = checked((int)(xorDefinition & 0x00FF_FFFF));
        var bodyRow = tables.Methods.GetBodyRow(xorMethodRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(bodyRow,
            tables.Contracts.Count);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.HomeCount, Is.EqualTo(3));
        Assert.That(body.MethodCount, Is.Zero);
        Assert.That(body.HomeMap[0], Is.EqualTo(1));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(body.GetBlockCode(0).ToArray(), Is.EqualTo(new byte[] {
            0xD5, 0, 1, 0,
            0xD5, 1, 2, 0,
            0xC1, 0x03, 0xF0, 0x10,
            0x17, 0,
        }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Sum", out var sumDefinition), Is.True);
        var sumMethodRow = checked((int)(sumDefinition & 0x00FF_FFFF));
        var sumBodyRow = tables.Methods.GetBodyRow(sumMethodRow);
        Assert.That(sumBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(sumBodyRow,
            tables.Contracts.Count);
        var sumBody = tables.Bodies.GetRegisterBody(sumBodyRow,
            tables.Contracts.Count);
        Assert.That(sumBody.HomeCount, Is.EqualTo(3));
        var sumFunction = program.Functions.Single(
            static function => function.Name == "Sum");
        var sumBinary = (BoundBinary)((BoundReturn)
            sumFunction.Body.Statements.Single()).Value!;
        Assert.That(sumBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, GetArithmeticControl(sumBinary), 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Difference", out var differenceDefinition), Is.True);
        var differenceMethodRow
            = checked((int)(differenceDefinition & 0x00FF_FFFF));
        var differenceBodyRow
            = tables.Methods.GetBodyRow(differenceMethodRow);
        Assert.That(differenceBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(differenceBodyRow,
            tables.Contracts.Count);
        var differenceBody = tables.Bodies.GetRegisterBody(
            differenceBodyRow, tables.Contracts.Count);
        var differenceFunction = program.Functions.Single(
            static function => function.Name == "Difference");
        var differenceBinary = (BoundBinary)((BoundReturn)
            differenceFunction.Body.Statements.Single()).Value!;
        Assert.That(differenceBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 2, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(differenceBinary), 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Remainder", out var remainderDefinition), Is.True);
        var remainderMethodRow
            = checked((int)(remainderDefinition & 0x00FF_FFFF));
        var remainderBodyRow = tables.Methods.GetBodyRow(remainderMethodRow);
        Assert.That(remainderBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(remainderBodyRow,
            tables.Contracts.Count);
        var remainderBody = tables.Bodies.GetRegisterBody(remainderBodyRow,
            tables.Contracts.Count);
        Assert.That(remainderBody.HomeCount, Is.EqualTo(3));
        Assert.That(remainderBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, 0x0D, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SubtractFromSeven", out var subtractFromSevenDefinition),
            Is.True);
        var subtractFromSevenMethodRow = checked((int)(
            subtractFromSevenDefinition & 0x00FF_FFFF));
        var subtractFromSevenBodyRow
            = tables.Methods.GetBodyRow(subtractFromSevenMethodRow);
        Assert.That(subtractFromSevenBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            subtractFromSevenBodyRow, tables.Contracts.Count);
        var subtractFromSevenBody = tables.Bodies.GetRegisterBody(
            subtractFromSevenBodyRow, tables.Contracts.Count);
        Assert.That(subtractFromSevenBody.HomeCount, Is.EqualTo(2));
        Assert.That(subtractFromSevenBody.ConstantCount, Is.EqualTo(1));
        var subtractFromSevenFunction = program.Functions.Single(
            static function => function.Name == "SubtractFromSeven");
        var subtractFromSevenBinary = (BoundBinary)((BoundReturn)
            subtractFromSevenFunction.Body.Statements.Single()).Value!;
        Assert.That(subtractFromSevenBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD4, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(subtractFromSevenBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.GreaterThanSeven", out var greaterThanSevenDefinition),
            Is.True);
        var greaterThanSevenMethodRow = checked((int)(
            greaterThanSevenDefinition & 0x00FF_FFFF));
        var greaterThanSevenBodyRow
            = tables.Methods.GetBodyRow(greaterThanSevenMethodRow);
        Assert.That(greaterThanSevenBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            greaterThanSevenBodyRow, tables.Contracts.Count);
        var greaterThanSevenBody = tables.Bodies.GetRegisterBody(
            greaterThanSevenBodyRow, tables.Contracts.Count);
        Assert.That(greaterThanSevenBody.HomeCount, Is.EqualTo(2));
        Assert.That(greaterThanSevenBody.ConstantCount, Is.EqualTo(1));
        Assert.That(greaterThanSevenBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD4, 1, 0, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 4, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Negated", out var negatedDefinition), Is.True);
        var negatedMethodRow
            = checked((int)(negatedDefinition & 0x00FF_FFFF));
        var negatedBodyRow = tables.Methods.GetBodyRow(negatedMethodRow);
        Assert.That(negatedBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(negatedBodyRow,
            tables.Contracts.Count);
        var negatedBody = tables.Bodies.GetRegisterBody(negatedBodyRow,
            tables.Contracts.Count);
        Assert.That(negatedBody.HomeCount, Is.EqualTo(2));
        var negatedFunction = program.Functions.Single(
            static function => function.Name == "Negated");
        var negatedUnary = (BoundUnary)((BoundReturn)
            negatedFunction.Body.Statements.Single()).Value!;
        var negatedControl = negatedUnary.OverflowBehavior switch
        {
            BoundIntegerOverflowBehavior.Checked => (byte)0x04,
            BoundIntegerOverflowBehavior.Wrapping => (byte)0x08,
            _ => throw new InvalidOperationException(
                "The Negated fixture must use a supported integer overflow policy."),
        };
        Assert.That(negatedBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 1, 0,
                0xC0, negatedControl, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Negate64", out var negate64Definition), Is.True);
        var negate64MethodRow
            = checked((int)(negate64Definition & 0x00FF_FFFF));
        var negate64BodyRow = tables.Methods.GetBodyRow(negate64MethodRow);
        Assert.That(negate64BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(negate64BodyRow,
            tables.Contracts.Count);
        var negate64Body = tables.Bodies.GetRegisterBody(negate64BodyRow,
            tables.Contracts.Count);
        var negate64Function = program.Functions.Single(
            static function => function.Name == "Negate64");
        var negate64Unary = (BoundUnary)((BoundReturn)
            negate64Function.Body.Statements.Single()).Value!;
        var negate64Control = negate64Unary.OverflowBehavior switch
        {
            BoundIntegerOverflowBehavior.Checked => (byte)0x04,
            BoundIntegerOverflowBehavior.Wrapping => (byte)0x08,
            _ => throw new InvalidOperationException(
                "The Negate64 fixture must use a supported integer overflow policy."),
        };
        Assert.That(negate64Body.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 1, 0,
                0xC0, negate64Control, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Complemented", out var complementedDefinition), Is.True);
        var complementedMethodRow
            = checked((int)(complementedDefinition & 0x00FF_FFFF));
        var complementedBodyRow
            = tables.Methods.GetBodyRow(complementedMethodRow);
        Assert.That(complementedBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(complementedBodyRow,
            tables.Contracts.Count);
        var complementedBody = tables.Bodies.GetRegisterBody(
            complementedBodyRow, tables.Contracts.Count);
        Assert.That(complementedBody.HomeCount, Is.EqualTo(2));
        Assert.That(complementedBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xC1, 0x04, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.StaticXor", out var staticXorDefinition), Is.True);
        var staticXorMethodRow
            = checked((int)(staticXorDefinition & 0x00FF_FFFF));
        var staticXorBodyRow
            = tables.Methods.GetBodyRow(staticXorMethodRow);
        Assert.That(staticXorBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(staticXorBodyRow,
            tables.Contracts.Count);
        var staticXorBody = tables.Bodies.GetRegisterBody(staticXorBodyRow,
            tables.Contracts.Count);
        Assert.That(staticXorBody.HomeCount, Is.EqualTo(2));
        Assert.That(staticXorBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC1, 0x03, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Xor128", out var xor128Definition), Is.True);
        var xor128MethodRow
            = checked((int)(xor128Definition & 0x00FF_FFFF));
        var xor128BodyRow = tables.Methods.GetBodyRow(xor128MethodRow);
        Assert.That(xor128BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(xor128BodyRow,
            tables.Contracts.Count);
        var xor128Body = tables.Bodies.GetRegisterBody(xor128BodyRow,
            tables.Contracts.Count);
        Assert.That(xor128Body.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC1, 0x03, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Box.Add",
            out var addDefinition), Is.True);
        var addMethodRow
            = checked((int)(addDefinition & 0x00FF_FFFF));
        var addBodyRow = tables.Methods.GetBodyRow(addMethodRow);
        Assert.That(addBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(addBodyRow,
            tables.Contracts.Count);
        var addBody = tables.Bodies.GetRegisterBody(addBodyRow,
            tables.Contracts.Count);
        Assert.That(addBody.HomeCount, Is.EqualTo(2));
        var addFunction = program.Functions.Single(
            static function => function.Name == "Add");
        var addBinary = (BoundBinary)((BoundReturn)
            addFunction.Body.Statements.Single()).Value!;
        static byte GetArithmeticControl(BoundBinary binary)
            => (binary.Operator, binary.OverflowBehavior) switch
            {
                (TokenKind.Plus, BoundIntegerOverflowBehavior.Checked)
                    => 0x01,
                (TokenKind.Minus, BoundIntegerOverflowBehavior.Checked)
                    => 0x02,
                (TokenKind.Star, BoundIntegerOverflowBehavior.Checked)
                    => 0x03,
                (TokenKind.Slash, BoundIntegerOverflowBehavior.Checked)
                    => 0x0C,
                (TokenKind.Percent, BoundIntegerOverflowBehavior.Checked)
                    => 0x0D,
                (TokenKind.Plus, BoundIntegerOverflowBehavior.Wrapping)
                    => 0x05,
                (TokenKind.Minus, BoundIntegerOverflowBehavior.Wrapping)
                    => 0x06,
                (TokenKind.Star, BoundIntegerOverflowBehavior.Wrapping)
                    => 0x07,
                _ => throw new InvalidOperationException(
                    "The arithmetic fixture must use a supported operation and overflow policy."),
            };
        void AssertComparisonBody(string exportKey, string functionName,
            byte predicate, int expectedHomeCount,
            ushort leftFormalOrdinal, ushort rightFormalOrdinal)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                exportKey, out var definition), Is.True);
            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            Assert.That(bodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(bodyRow,
                tables.Contracts.Count);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            Assert.That(body.HomeCount, Is.EqualTo(expectedHomeCount));
            var formalHomeOffset = expectedHomeCount == 3 ? 8 : 0;
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                    body.HomeMap.AsSpan(formalHomeOffset + 4, 2)),
                Is.EqualTo(leftFormalOrdinal));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                    body.HomeMap.AsSpan(formalHomeOffset + 12, 2)),
                Is.EqualTo(rightFormalOrdinal));
            var function = program.Functions.Single(
                candidate => candidate.Name == functionName);
            Assert.That(function.ReturnType, Is.EqualTo(PrimitiveType.Bool));
            var binary = (BoundBinary)((BoundReturn)
                function.Body.Statements.Single()).Value!;
            Assert.That(binary.Type, Is.EqualTo(PrimitiveType.Bool));
            var homeBase = expectedHomeCount == 3 ? 1 : 0;
            Assert.That(body.GetBlockCode(0).ToArray(), Is.EqualTo(
                new byte[] {
                    0xD5, 0, (byte)homeBase, 0,
                    0xD5, 1, (byte)(homeBase + 1), 0,
                    0xC2, 0, 0xF0, 0x10,
                    0xC3, predicate, 0, 0xFF,
                    0x17, 0,
                }));
        }
        void AssertFloatCallBody(string exportKey, string targetKey,
            int expectedHomeCount, bool hasConstant, byte[] expectedCode)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                exportKey, out var definition), Is.True);
            var methodRow = checked((int)(definition & 0x00FF_FFFF));
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            Assert.That(bodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(bodyRow,
                tables.Contracts.Count);
            tables.ValidateRegisterBodyMapsForMethod(methodRow);
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            Assert.That(body.HomeCount, Is.EqualTo(expectedHomeCount));
            Assert.That(body.MethodCount, Is.EqualTo(1));
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                targetKey, out var targetDefinition), Is.True);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                    body.MethodMap), Is.EqualTo(targetDefinition));
            Assert.That(body.ConstantCount, Is.EqualTo(
                hasConstant ? 1 : 0));
            if (hasConstant)
            {
                Assert.That(body.ConstantMap.Length, Is.EqualTo(8));
                Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            body.ConstantMap),
                        tables.Signatures.GetResultTypeSpec(
                            tables.Methods.GetSignature(methodRow))),
                    Is.True);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                        body.ConstantMap.Slice(4)),
                    Is.GreaterThan(0u));
            }
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(expectedCode));
        }
        var addControl = GetArithmeticControl(addBinary);
        Assert.That(addBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, addControl, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Add64", out var add64Definition), Is.True);
        var add64MethodRow
            = checked((int)(add64Definition & 0x00FF_FFFF));
        var add64BodyRow = tables.Methods.GetBodyRow(add64MethodRow);
        Assert.That(add64BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(add64BodyRow,
            tables.Contracts.Count);
        var add64Body = tables.Bodies.GetRegisterBody(add64BodyRow,
            tables.Contracts.Count);
        var add64Function = program.Functions.Single(
            static function => function.Name == "Add64");
        var add64Binary = (BoundBinary)((BoundReturn)
            add64Function.Body.Statements.Single()).Value!;
        Assert.That(add64Body.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(add64Binary), 0xF0, 0x10,
                0x17, 0,
            }));
        foreach (var floatingType in floatingTypeCases)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "Box." + floatingType.Name, out var floatingDefinition),
                Is.True);
            var floatingMethodRow = checked((int)(
                floatingDefinition & 0x00FF_FFFF));
            var floatingBodyRow
                = tables.Methods.GetBodyRow(floatingMethodRow);
            Assert.That(floatingBodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(floatingBodyRow,
                tables.Contracts.Count);
            tables.ValidateRegisterBodyMapsForMethod(floatingMethodRow);
            var floatingBody = tables.Bodies.GetRegisterBody(floatingBodyRow,
                tables.Contracts.Count);
            Assert.That(floatingBody.HomeCount, Is.EqualTo(2));
            var floatingSignature = tables.Methods.GetSignature(
                floatingMethodRow);
            var floatingResultTypeSpecRef
                = tables.Signatures.GetResultTypeSpec(floatingSignature);
            Assert.That(tables.TypeSpecs
                    .GetTypeSpecRef(floatingResultTypeSpecRef).TypeCode,
                Is.EqualTo(floatingType.TypeCode));
            Assert.That(floatingBody.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0xD5, 0, 0, 0,
                    0xD5, 1, 1, 0,
                    0xC0, 0x20, 0xF0, 0x10,
                    0x17, 0,
                }));
        }
        foreach (var floatingOperation in floatingControlCases)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                "Box." + floatingOperation.Name,
                out var floatingDefinition), Is.True);
            var floatingMethodRow = checked((int)(
                floatingDefinition & 0x00FF_FFFF));
            var floatingBodyRow
                = tables.Methods.GetBodyRow(floatingMethodRow);
            Assert.That(floatingBodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(floatingBodyRow,
                tables.Contracts.Count);
            tables.ValidateRegisterBodyMapsForMethod(floatingMethodRow);
            var floatingBody = tables.Bodies.GetRegisterBody(floatingBodyRow,
                tables.Contracts.Count);
            Assert.That(floatingBody.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] {
                    0xD5, 0, 0, 0,
                    0xD5, 1, 1, 0,
                    0xC0, floatingOperation.Control, 0xF0, 0x10,
                    0x17, 0,
                }));
        }
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.NegateFloat32", out var negateFloat32Definition), Is.True);
        var negateFloat32MethodRow = checked((int)(
            negateFloat32Definition & 0x00FF_FFFF));
        var negateFloat32BodyRow
            = tables.Methods.GetBodyRow(negateFloat32MethodRow);
        Assert.That(negateFloat32BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(negateFloat32BodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(negateFloat32MethodRow);
        var negateFloat32Body = tables.Bodies.GetRegisterBody(
            negateFloat32BodyRow, tables.Contracts.Count);
        Assert.That(negateFloat32Body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xC0, 0x25, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.LessFloat64", out var lessFloat64Definition), Is.True);
        var lessFloat64MethodRow = checked((int)(
            lessFloat64Definition & 0x00FF_FFFF));
        var lessFloat64BodyRow = tables.Methods.GetBodyRow(
            lessFloat64MethodRow);
        Assert.That(lessFloat64BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(lessFloat64BodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(lessFloat64MethodRow);
        var lessFloat64Body = tables.Bodies.GetRegisterBody(
            lessFloat64BodyRow, tables.Contracts.Count);
        Assert.That(lessFloat64Body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.AddFloat64Constant",
            out var addFloat64ConstantDefinition), Is.True);
        var addFloat64ConstantMethodRow = checked((int)(
            addFloat64ConstantDefinition & 0x00FF_FFFF));
        var addFloat64ConstantBodyRow
            = tables.Methods.GetBodyRow(addFloat64ConstantMethodRow);
        Assert.That(addFloat64ConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            addFloat64ConstantBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(
            addFloat64ConstantMethodRow);
        var addFloat64ConstantBody = tables.Bodies.GetRegisterBody(
            addFloat64ConstantBodyRow, tables.Contracts.Count);
        Assert.That(addFloat64ConstantBody.ConstantCount, Is.EqualTo(1));
        Assert.That(addFloat64ConstantBody.ConstantMap.Length,
            Is.EqualTo(8));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    addFloat64ConstantBody.ConstantMap),
                tables.Signatures.GetResultTypeSpec(
                    tables.Methods.GetSignature(
                        addFloat64ConstantMethodRow))),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                addFloat64ConstantBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        Assert.That(addFloat64ConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD4, 1, 0, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.AddFloat64Instance",
            out var addFloat64InstanceDefinition), Is.True);
        var addFloat64InstanceMethodRow = checked((int)(
            addFloat64InstanceDefinition & 0x00FF_FFFF));
        var addFloat64InstanceBodyRow
            = tables.Methods.GetBodyRow(addFloat64InstanceMethodRow);
        Assert.That(addFloat64InstanceBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            addFloat64InstanceBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(
            addFloat64InstanceMethodRow);
        var addFloat64InstanceBody = tables.Bodies.GetRegisterBody(
            addFloat64InstanceBodyRow, tables.Contracts.Count);
        Assert.That(addFloat64InstanceBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.LessFloat64Instance",
            out var lessFloat64InstanceDefinition), Is.True);
        var lessFloat64InstanceMethodRow = checked((int)(
            lessFloat64InstanceDefinition & 0x00FF_FFFF));
        var lessFloat64InstanceBodyRow
            = tables.Methods.GetBodyRow(lessFloat64InstanceMethodRow);
        Assert.That(lessFloat64InstanceBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            lessFloat64InstanceBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(
            lessFloat64InstanceMethodRow);
        var lessFloat64InstanceBody = tables.Bodies.GetRegisterBody(
            lessFloat64InstanceBodyRow, tables.Contracts.Count);
        Assert.That(lessFloat64InstanceBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            }));
        AssertFloatCallBody("Box.StaticAddFloat64AfterCall",
            "Box.AddFloat64", 2, false, [
                0xE0, 0, 1, 0,
                0xD5, 1, 1, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            ]);
        AssertFloatCallBody("Box.StaticAddFloat64AfterCallConstant",
            "Box.AddFloat64", 2, true, [
                0xE0, 0, 1, 0,
                0xD4, 1, 0, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            ]);
        AssertFloatCallBody("Box.InstanceStaticAddFloat64AfterCall",
            "Box.AddFloat64", 3, false, [
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            ]);
        AssertFloatCallBody("Box.LessFloat64AfterCall",
            "Box.AddFloat64", 2, false, [
                0xE0, 0, 1, 0,
                0xD5, 1, 1, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            ]);
        AssertFloatCallBody("Box.LessFloat64AfterInstanceCall",
            "Box.AddFloat64Instance", 3, false, [
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            ]);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Sum64ThroughTwoLocals",
            out var sum64ThroughTwoLocalsDefinition), Is.True);
        var sum64ThroughTwoLocalsMethodRow = checked((int)(
            sum64ThroughTwoLocalsDefinition & 0x00FF_FFFF));
        var sum64ThroughTwoLocalsBodyRow
            = tables.Methods.GetBodyRow(sum64ThroughTwoLocalsMethodRow);
        Assert.That(sum64ThroughTwoLocalsBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            sum64ThroughTwoLocalsBodyRow, tables.Contracts.Count);
        var sum64ThroughTwoLocalsBody = tables.Bodies.GetRegisterBody(
            sum64ThroughTwoLocalsBodyRow, tables.Contracts.Count);
        Assert.That(sum64ThroughTwoLocalsBody.HomeCount, Is.EqualTo(4));
        var sum64ThroughTwoLocalsFunction = program.Functions.Single(
            static function => function.Name == "Sum64ThroughTwoLocals");
        var sum64ThroughTwoLocalsBinary = (BoundBinary)((BoundReturn)
            sum64ThroughTwoLocalsFunction.Body.Statements.Single(
                static statement => statement is BoundReturn)).Value!;
        Assert.That(sum64ThroughTwoLocalsBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD6, 0x10, 3, 0,
                0xD5, 0, 3, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(sum64ThroughTwoLocalsBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Sum64ThroughLocalConstant",
            out var sum64ThroughLocalConstantDefinition), Is.True);
        var sum64ThroughLocalConstantMethodRow = checked((int)(
            sum64ThroughLocalConstantDefinition & 0x00FF_FFFF));
        var sum64ThroughLocalConstantBodyRow
            = tables.Methods.GetBodyRow(
                sum64ThroughLocalConstantMethodRow);
        Assert.That(sum64ThroughLocalConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            sum64ThroughLocalConstantBodyRow, tables.Contracts.Count);
        var sum64ThroughLocalConstantBody = tables.Bodies.GetRegisterBody(
            sum64ThroughLocalConstantBodyRow, tables.Contracts.Count);
        Assert.That(sum64ThroughLocalConstantBody.HomeCount, Is.EqualTo(4));
        Assert.That(sum64ThroughLocalConstantBody.ConstantCount,
            Is.EqualTo(1));
        Assert.That(sum64ThroughLocalConstantBody.ConstantMap.Length,
            Is.EqualTo(8));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    sum64ThroughLocalConstantBody.ConstantMap),
                tables.Signatures.GetResultTypeSpec(
                    tables.Methods.GetSignature(
                        sum64ThroughLocalConstantMethodRow))),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                sum64ThroughLocalConstantBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        var sum64ThroughLocalConstantFunction = program.Functions.Single(
            static function =>
                function.Name == "Sum64ThroughLocalConstant");
        var sum64ThroughLocalConstantBinary = (BoundBinary)((BoundReturn)
            sum64ThroughLocalConstantFunction.Body.Statements.Single(
                static statement => statement is BoundReturn)).Value!;
        Assert.That(sum64ThroughLocalConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD6, 0x10, 3, 0,
                0xD5, 0, 3, 0,
                0xD4, 1, 0, 0,
                0xC0, GetArithmeticControl(
                    sum64ThroughLocalConstantBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SumFloat64ThroughTwoLocals",
            out var sumFloat64ThroughTwoLocalsDefinition), Is.True);
        var sumFloat64ThroughTwoLocalsMethodRow = checked((int)(
            sumFloat64ThroughTwoLocalsDefinition & 0x00FF_FFFF));
        var sumFloat64ThroughTwoLocalsBodyRow
            = tables.Methods.GetBodyRow(
                sumFloat64ThroughTwoLocalsMethodRow);
        Assert.That(sumFloat64ThroughTwoLocalsBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            sumFloat64ThroughTwoLocalsBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(
            sumFloat64ThroughTwoLocalsMethodRow);
        var sumFloat64ThroughTwoLocalsBody = tables.Bodies.GetRegisterBody(
            sumFloat64ThroughTwoLocalsBodyRow, tables.Contracts.Count);
        Assert.That(sumFloat64ThroughTwoLocalsBody.HomeCount, Is.EqualTo(4));
        Assert.That(sumFloat64ThroughTwoLocalsBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD6, 0x10, 3, 0,
                0xD5, 0, 3, 0,
                0xD5, 1, 1, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SumFloat64ThroughLocalConstant",
            out var sumFloat64ThroughLocalConstantDefinition), Is.True);
        var sumFloat64ThroughLocalConstantMethodRow = checked((int)(
            sumFloat64ThroughLocalConstantDefinition & 0x00FF_FFFF));
        var sumFloat64ThroughLocalConstantBodyRow
            = tables.Methods.GetBodyRow(
                sumFloat64ThroughLocalConstantMethodRow);
        Assert.That(sumFloat64ThroughLocalConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            sumFloat64ThroughLocalConstantBodyRow, tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(
            sumFloat64ThroughLocalConstantMethodRow);
        var sumFloat64ThroughLocalConstantBody
            = tables.Bodies.GetRegisterBody(
                sumFloat64ThroughLocalConstantBodyRow,
                tables.Contracts.Count);
        Assert.That(sumFloat64ThroughLocalConstantBody.ConstantCount,
            Is.EqualTo(1));
        Assert.That(sumFloat64ThroughLocalConstantBody.ConstantMap.Length,
            Is.EqualTo(8));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    sumFloat64ThroughLocalConstantBody.ConstantMap),
                tables.Signatures.GetResultTypeSpec(
                    tables.Methods.GetSignature(
                        sumFloat64ThroughLocalConstantMethodRow))),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                sumFloat64ThroughLocalConstantBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        Assert.That(sumFloat64ThroughLocalConstantBody.GetBlockCode(0)
                .ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD6, 0x10, 3, 0,
                0xD5, 0, 3, 0,
                0xD4, 1, 0, 0,
                0xC0, 0x20, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Subtract", out var subtractDefinition), Is.True);
        var subtractMethodRow
            = checked((int)(subtractDefinition & 0x00FF_FFFF));
        var subtractBodyRow = tables.Methods.GetBodyRow(subtractMethodRow);
        Assert.That(subtractBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(subtractBodyRow,
            tables.Contracts.Count);
        var subtractBody = tables.Bodies.GetRegisterBody(subtractBodyRow,
            tables.Contracts.Count);
        Assert.That(subtractBody.HomeCount, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                subtractBody.HomeMap.AsSpan(4, 2)),
            Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                subtractBody.HomeMap.AsSpan(12, 2)),
            Is.Zero);
        var subtractFunction = program.Functions.Single(
            static function => function.Name == "Subtract");
        var subtractBinary = (BoundBinary)((BoundReturn)
            subtractFunction.Body.Statements.Single()).Value!;
        Assert.That(subtractBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(subtractBinary), 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Multiply", out var multiplyDefinition), Is.True);
        var multiplyMethodRow
            = checked((int)(multiplyDefinition & 0x00FF_FFFF));
        var multiplyBodyRow = tables.Methods.GetBodyRow(multiplyMethodRow);
        Assert.That(multiplyBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(multiplyBodyRow,
            tables.Contracts.Count);
        var multiplyBody = tables.Bodies.GetRegisterBody(multiplyBodyRow,
            tables.Contracts.Count);
        var multiplyFunction = program.Functions.Single(
            static function => function.Name == "Multiply");
        var multiplyBinary = (BoundBinary)((BoundReturn)
            multiplyFunction.Body.Statements.Single()).Value!;
        Assert.That(multiplyBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(multiplyBinary), 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Divide", out var divideDefinition), Is.True);
        var divideMethodRow
            = checked((int)(divideDefinition & 0x00FF_FFFF));
        var divideBodyRow = tables.Methods.GetBodyRow(divideMethodRow);
        Assert.That(divideBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(divideBodyRow,
            tables.Contracts.Count);
        var divideBody = tables.Bodies.GetRegisterBody(divideBodyRow,
            tables.Contracts.Count);
        Assert.That(divideBody.HomeCount, Is.EqualTo(2));
        Assert.That(divideBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, 0x0C, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.AddConstant", out var addConstantDefinition), Is.True);
        var addConstantMethodRow
            = checked((int)(addConstantDefinition & 0x00FF_FFFF));
        var addConstantBodyRow
            = tables.Methods.GetBodyRow(addConstantMethodRow);
        Assert.That(addConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(addConstantBodyRow,
            tables.Contracts.Count);
        var addConstantBody = tables.Bodies.GetRegisterBody(
            addConstantBodyRow, tables.Contracts.Count);
        Assert.That(addConstantBody.HomeCount, Is.EqualTo(1));
        Assert.That(addConstantBody.ConstantCount, Is.EqualTo(1));
        Assert.That(addConstantBody.ConstantMap.Length, Is.EqualTo(8));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                addConstantBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(9, out _), Is.False);
        var addConstantFunction = program.Functions.Single(
            static function => function.Name == "AddConstant");
        var addConstantBinary = (BoundBinary)((BoundReturn)
            addConstantFunction.Body.Statements.Single()).Value!;
        Assert.That(addConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD4, 1, 0, 0,
                0xC0, GetArithmeticControl(addConstantBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SubtractFromConstant",
            out var subtractFromConstantDefinition), Is.True);
        var subtractFromConstantMethodRow = checked((int)(
            subtractFromConstantDefinition & 0x00FF_FFFF));
        var subtractFromConstantBodyRow
            = tables.Methods.GetBodyRow(subtractFromConstantMethodRow);
        Assert.That(subtractFromConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            subtractFromConstantBodyRow, tables.Contracts.Count);
        var subtractFromConstantBody = tables.Bodies.GetRegisterBody(
            subtractFromConstantBodyRow, tables.Contracts.Count);
        var subtractFromConstantFunction = program.Functions.Single(
            static function => function.Name == "SubtractFromConstant");
        var subtractFromConstantBinary = (BoundBinary)((BoundReturn)
            subtractFromConstantFunction.Body.Statements.Single()).Value!;
        Assert.That(subtractFromConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD4, 0, 0, 0,
                0xD5, 1, 0, 0,
                0xC0, GetArithmeticControl(subtractFromConstantBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Double", out var doubleDefinition), Is.True);
        var doubleMethodRow
            = checked((int)(doubleDefinition & 0x00FF_FFFF));
        var doubleBodyRow = tables.Methods.GetBodyRow(doubleMethodRow);
        Assert.That(doubleBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(doubleBodyRow,
            tables.Contracts.Count);
        var doubleBody = tables.Bodies.GetRegisterBody(doubleBodyRow,
            tables.Contracts.Count);
        Assert.That(doubleBody.HomeCount, Is.EqualTo(1));
        var doubleFunction = program.Functions.Single(
            static function => function.Name == "Double");
        var doubleBinary = (BoundBinary)((BoundReturn)
            doubleFunction.Body.Statements.Single()).Value!;
        Assert.That(doubleBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 0, 0,
                0xC0, GetArithmeticControl(doubleBinary), 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Negate", out var negateDefinition), Is.True);
        var negateMethodRow
            = checked((int)(negateDefinition & 0x00FF_FFFF));
        var negateBodyRow = tables.Methods.GetBodyRow(negateMethodRow);
        Assert.That(negateBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(negateBodyRow,
            tables.Contracts.Count);
        var negateBody = tables.Bodies.GetRegisterBody(negateBodyRow,
            tables.Contracts.Count);
        var negateFunction = program.Functions.Single(
            static function => function.Name == "Negate");
        var negateUnary = (BoundUnary)((BoundReturn)
            negateFunction.Body.Statements.Single()).Value!;
        var negateControl = negateUnary.OverflowBehavior switch
        {
            BoundIntegerOverflowBehavior.Checked => (byte)0x04,
            BoundIntegerOverflowBehavior.Wrapping => (byte)0x08,
            _ => throw new InvalidOperationException(
                "The Negate fixture must use a supported integer overflow policy."),
        };
        Assert.That(negateBody.HomeCount, Is.EqualTo(1));
        Assert.That(negateBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xC0, negateControl, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Complement", out var complementDefinition), Is.True);
        var complementMethodRow
            = checked((int)(complementDefinition & 0x00FF_FFFF));
        var complementBodyRow
            = tables.Methods.GetBodyRow(complementMethodRow);
        Assert.That(complementBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(complementBodyRow,
            tables.Contracts.Count);
        var complementBody = tables.Bodies.GetRegisterBody(
            complementBodyRow, tables.Contracts.Count);
        Assert.That(complementBody.HomeCount, Is.EqualTo(1));
        Assert.That(complementBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xC1, 0x04, 0xF0, 0xF0,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.CopyStatic", out var copyStaticDefinition), Is.True);
        var copyStaticMethodRow
            = checked((int)(copyStaticDefinition & 0x00FF_FFFF));
        var copyStaticBodyRow
            = tables.Methods.GetBodyRow(copyStaticMethodRow);
        Assert.That(copyStaticBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(copyStaticBodyRow,
            tables.Contracts.Count);
        var copyStaticBody = tables.Bodies.GetRegisterBody(
            copyStaticBodyRow, tables.Contracts.Count);
        Assert.That(copyStaticBody.HomeCount, Is.EqualTo(2));
        Assert.That(copyStaticBody.HomeMap[0], Is.EqualTo(2));
        Assert.That(copyStaticBody.HomeMap[8], Is.EqualTo(3));
        Assert.That(copyStaticBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 1, 0,
                0xEB, 0, 1, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Copy64Static", out var copy64StaticDefinition), Is.True);
        var copy64StaticMethodRow = checked((int)(
            copy64StaticDefinition & 0x00FF_FFFF));
        var copy64StaticBodyRow
            = tables.Methods.GetBodyRow(copy64StaticMethodRow);
        Assert.That(copy64StaticBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(copy64StaticBodyRow,
            tables.Contracts.Count);
        var copy64StaticBody = tables.Bodies.GetRegisterBody(
            copy64StaticBodyRow, tables.Contracts.Count);
        Assert.That(copy64StaticBody.HomeCount, Is.EqualTo(2));
        Assert.That(copy64StaticBody.HomeMap[0], Is.EqualTo(2));
        Assert.That(copy64StaticBody.HomeMap[8], Is.EqualTo(3));
        Assert.That(copy64StaticBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 1, 0,
                0xEB, 0, 1, 0,
            }));
        AssertComparisonBody("Box.LessThan", "LessThan", 1, 2, 0, 1);
        AssertComparisonBody("Box.EqualTo", "EqualTo", 2, 2, 0, 1);
        AssertComparisonBody("Box.LessOrEqual", "LessOrEqual", 3, 2, 0, 1);
        AssertComparisonBody("Box.GreaterThan", "GreaterThan", 4, 3, 0, 1);
        AssertComparisonBody("Box.Greater64", "Greater64", 4, 2, 0, 1);
        AssertComparisonBody("Box.GreaterOrEqual", "GreaterOrEqual", 6,
            2, 1, 0);
        AssertComparisonBody("Box.NotEqual", "NotEqual", 13, 3, 0, 1);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Same", out var sameDefinition), Is.True);
        var sameMethodRow
            = checked((int)(sameDefinition & 0x00FF_FFFF));
        var sameBodyRow = tables.Methods.GetBodyRow(sameMethodRow);
        Assert.That(sameBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(sameBodyRow,
            tables.Contracts.Count);
        var sameBody = tables.Bodies.GetRegisterBody(sameBodyRow,
            tables.Contracts.Count);
        Assert.That(sameBody.HomeCount, Is.EqualTo(2));
        Assert.That(sameBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC2, 0x01, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Different", out var differentDefinition), Is.True);
        var differentMethodRow
            = checked((int)(differentDefinition & 0x00FF_FFFF));
        var differentBodyRow = tables.Methods.GetBodyRow(differentMethodRow);
        Assert.That(differentBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(differentBodyRow,
            tables.Contracts.Count);
        var differentBody = tables.Bodies.GetRegisterBody(differentBodyRow,
            tables.Contracts.Count);
        Assert.That(differentBody.HomeCount, Is.EqualTo(3));
        Assert.That(differentBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC2, 0x02, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.StaticXorThroughLocal",
            out var staticXorThroughLocalDefinition), Is.True);
        var staticXorThroughLocalMethodRow = checked((int)(
            staticXorThroughLocalDefinition & 0x00FF_FFFF));
        var staticXorThroughLocalBodyRow
            = tables.Methods.GetBodyRow(staticXorThroughLocalMethodRow);
        Assert.That(staticXorThroughLocalBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            staticXorThroughLocalBodyRow, tables.Contracts.Count);
        var staticXorThroughLocalBody = tables.Bodies.GetRegisterBody(
            staticXorThroughLocalBodyRow, tables.Contracts.Count);
        Assert.That(staticXorThroughLocalBody.HomeCount, Is.EqualTo(3));
        Assert.That(staticXorThroughLocalBody.HomeMap[0], Is.EqualTo(2));
        Assert.That(staticXorThroughLocalBody.HomeMap[8], Is.EqualTo(2));
        Assert.That(staticXorThroughLocalBody.HomeMap[16], Is.EqualTo(3));
        Assert.That(staticXorThroughLocalBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD5, 1, 1, 0,
                0xC1, 0x03, 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SumThroughLocal", out var sumThroughLocalDefinition),
            Is.True);
        var sumThroughLocalMethodRow = checked((int)(
            sumThroughLocalDefinition & 0x00FF_FFFF));
        var sumThroughLocalBodyRow
            = tables.Methods.GetBodyRow(sumThroughLocalMethodRow);
        Assert.That(sumThroughLocalBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(sumThroughLocalBodyRow,
            tables.Contracts.Count);
        var sumThroughLocalBody = tables.Bodies.GetRegisterBody(
            sumThroughLocalBodyRow, tables.Contracts.Count);
        var sumThroughLocalFunction = program.Functions.Single(
            static function => function.Name == "SumThroughLocal");
        var sumThroughLocalBinary = (BoundBinary)((BoundReturn)
            sumThroughLocalFunction.Body.Statements.Single(
                static statement => statement is BoundReturn)).Value!;
        Assert.That(sumThroughLocalBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(sumThroughLocalBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SumThroughTwoLocals",
            out var sumThroughTwoLocalsDefinition), Is.True);
        var sumThroughTwoLocalsMethodRow = checked((int)(
            sumThroughTwoLocalsDefinition & 0x00FF_FFFF));
        var sumThroughTwoLocalsBodyRow
            = tables.Methods.GetBodyRow(sumThroughTwoLocalsMethodRow);
        Assert.That(sumThroughTwoLocalsBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            sumThroughTwoLocalsBodyRow, tables.Contracts.Count);
        var sumThroughTwoLocalsBody = tables.Bodies.GetRegisterBody(
            sumThroughTwoLocalsBodyRow, tables.Contracts.Count);
        Assert.That(sumThroughTwoLocalsBody.HomeCount, Is.EqualTo(4));
        Assert.That(sumThroughTwoLocalsBody.HomeMap[0], Is.EqualTo(2));
        Assert.That(sumThroughTwoLocalsBody.HomeMap[8], Is.EqualTo(2));
        Assert.That(sumThroughTwoLocalsBody.HomeMap[16], Is.EqualTo(3));
        Assert.That(sumThroughTwoLocalsBody.HomeMap[24], Is.EqualTo(3));
        var sumThroughTwoLocalsFunction = program.Functions.Single(
            static function => function.Name == "SumThroughTwoLocals");
        var sumThroughTwoLocalsBinary = (BoundBinary)((BoundReturn)
            sumThroughTwoLocalsFunction.Body.Statements.Single(
                static statement => statement is BoundReturn)).Value!;
        Assert.That(sumThroughTwoLocalsBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD6, 0x10, 3, 0,
                0xD5, 0, 3, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(sumThroughTwoLocalsBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.LessThroughLocal", out var lessThroughLocalDefinition),
            Is.True);
        var lessThroughLocalMethodRow = checked((int)(
            lessThroughLocalDefinition & 0x00FF_FFFF));
        var lessThroughLocalBodyRow
            = tables.Methods.GetBodyRow(lessThroughLocalMethodRow);
        Assert.That(lessThroughLocalBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            lessThroughLocalBodyRow, tables.Contracts.Count);
        var lessThroughLocalBody = tables.Bodies.GetRegisterBody(
            lessThroughLocalBodyRow, tables.Contracts.Count);
        Assert.That(lessThroughLocalBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 2, 0,
                0xD5, 0, 2, 0,
                0xD5, 1, 1, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.AddThroughLocalConstant",
            out var addThroughLocalConstantDefinition), Is.True);
        var addThroughLocalConstantMethodRow = checked((int)(
            addThroughLocalConstantDefinition & 0x00FF_FFFF));
        var addThroughLocalConstantBodyRow
            = tables.Methods.GetBodyRow(addThroughLocalConstantMethodRow);
        Assert.That(addThroughLocalConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            addThroughLocalConstantBodyRow, tables.Contracts.Count);
        var addThroughLocalConstantBody = tables.Bodies.GetRegisterBody(
            addThroughLocalConstantBodyRow, tables.Contracts.Count);
        Assert.That(addThroughLocalConstantBody.HomeCount, Is.EqualTo(2));
        Assert.That(addThroughLocalConstantBody.ConstantCount, Is.EqualTo(1));
        var addThroughLocalConstantFunction = program.Functions.Single(
            static function => function.Name == "AddThroughLocalConstant");
        var addThroughLocalConstantBinary = (BoundBinary)((BoundReturn)
            addThroughLocalConstantFunction.Body.Statements.Single(
                static statement => statement is BoundReturn)).Value!;
        Assert.That(addThroughLocalConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 1, 0,
                0xD5, 0, 1, 0,
                0xD4, 1, 0, 0,
                0xC0, GetArithmeticControl(addThroughLocalConstantBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.LessThroughLocalConstant",
            out var lessThroughLocalConstantDefinition), Is.True);
        var lessThroughLocalConstantMethodRow = checked((int)(
            lessThroughLocalConstantDefinition & 0x00FF_FFFF));
        var lessThroughLocalConstantBodyRow
            = tables.Methods.GetBodyRow(lessThroughLocalConstantMethodRow);
        Assert.That(lessThroughLocalConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            lessThroughLocalConstantBodyRow, tables.Contracts.Count);
        var lessThroughLocalConstantBody = tables.Bodies.GetRegisterBody(
            lessThroughLocalConstantBodyRow, tables.Contracts.Count);
        Assert.That(lessThroughLocalConstantBody.HomeCount, Is.EqualTo(2));
        Assert.That(lessThroughLocalConstantBody.ConstantCount,
            Is.EqualTo(1));
        Assert.That(lessThroughLocalConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 0, 0,
                0xD6, 0x10, 1, 0,
                0xD5, 0, 1, 0,
                0xD4, 1, 0, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.XorAfterCall", out var xorAfterCallDefinition), Is.True);
        var xorAfterCallMethodRow
            = checked((int)(xorAfterCallDefinition & 0x00FF_FFFF));
        var callBodyRow
            = tables.Methods.GetBodyRow(xorAfterCallMethodRow);
        Assert.That(callBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(callBodyRow,
            tables.Contracts.Count);
        var callBody = tables.Bodies.GetRegisterBody(callBodyRow,
            tables.Contracts.Count);
        Assert.That(callBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                callBody.MethodMap),
            Is.EqualTo(xorDefinition));
        Assert.That(callBody.GetBlockCode(0).ToArray(), Is.EqualTo(new byte[] {
            0xE0, 0, 1, 0,
            0xD5, 1, 2, 0,
            0xC1, 0x03, 0xF0, 0x10,
            0x17, 0,
        }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.SumAfterCall", out var sumAfterCallDefinition), Is.True);
        var sumAfterCallMethodRow
            = checked((int)(sumAfterCallDefinition & 0x00FF_FFFF));
        var sumAfterCallBodyRow
            = tables.Methods.GetBodyRow(sumAfterCallMethodRow);
        Assert.That(sumAfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(sumAfterCallBodyRow,
            tables.Contracts.Count);
        var sumAfterCallBody = tables.Bodies.GetRegisterBody(
            sumAfterCallBodyRow, tables.Contracts.Count);
        Assert.That(sumAfterCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                sumAfterCallBody.MethodMap),
            Is.EqualTo(sumDefinition));
        var sumAfterCallFunction = program.Functions.Single(
            static function => function.Name == "SumAfterCall");
        var sumAfterCallBinary = (BoundBinary)((BoundReturn)
            sumAfterCallFunction.Body.Statements.Single()).Value!;
        Assert.That(sumAfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, GetArithmeticControl(sumAfterCallBinary), 0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.LessThanAfterCall", out var lessThanAfterCallDefinition),
            Is.True);
        var lessThanAfterCallMethodRow = checked((int)(
            lessThanAfterCallDefinition & 0x00FF_FFFF));
        var lessThanAfterCallBodyRow
            = tables.Methods.GetBodyRow(lessThanAfterCallMethodRow);
        Assert.That(lessThanAfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            lessThanAfterCallBodyRow, tables.Contracts.Count);
        var lessThanAfterCallBody = tables.Bodies.GetRegisterBody(
            lessThanAfterCallBodyRow, tables.Contracts.Count);
        Assert.That(lessThanAfterCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                lessThanAfterCallBody.MethodMap),
            Is.EqualTo(sumDefinition));
        Assert.That(lessThanAfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 1, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.StaticAddAfterCall", out var staticAddAfterCallDefinition),
            Is.True);
        var staticAddAfterCallMethodRow = checked((int)(
            staticAddAfterCallDefinition & 0x00FF_FFFF));
        var staticAddAfterCallBodyRow
            = tables.Methods.GetBodyRow(staticAddAfterCallMethodRow);
        Assert.That(staticAddAfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            staticAddAfterCallBodyRow, tables.Contracts.Count);
        var staticAddAfterCallBody = tables.Bodies.GetRegisterBody(
            staticAddAfterCallBodyRow, tables.Contracts.Count);
        Assert.That(staticAddAfterCallBody.HomeCount, Is.EqualTo(2));
        Assert.That(staticAddAfterCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                staticAddAfterCallBody.MethodMap),
            Is.EqualTo(addDefinition));
        var staticAddAfterCallFunction = program.Functions.Single(
            static function => function.Name == "StaticAddAfterCall");
        var staticAddAfterCallBinary = (BoundBinary)((BoundReturn)
            staticAddAfterCallFunction.Body.Statements.Single()).Value!;
        Assert.That(staticAddAfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(staticAddAfterCallBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.InstanceStaticAddAfterCall",
            out var instanceStaticAddAfterCallDefinition), Is.True);
        var instanceStaticAddAfterCallMethodRow = checked((int)(
            instanceStaticAddAfterCallDefinition & 0x00FF_FFFF));
        var instanceStaticAddAfterCallBodyRow
            = tables.Methods.GetBodyRow(instanceStaticAddAfterCallMethodRow);
        Assert.That(instanceStaticAddAfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            instanceStaticAddAfterCallBodyRow, tables.Contracts.Count);
        var instanceStaticAddAfterCallBody = tables.Bodies.GetRegisterBody(
            instanceStaticAddAfterCallBodyRow, tables.Contracts.Count);
        Assert.That(instanceStaticAddAfterCallBody.HomeCount, Is.EqualTo(3));
        Assert.That(instanceStaticAddAfterCallBody.MethodCount,
            Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                instanceStaticAddAfterCallBody.MethodMap),
            Is.EqualTo(addDefinition));
        var instanceStaticAddAfterCallFunction = program.Functions.Single(
            static function => function.Name == "InstanceStaticAddAfterCall");
        var instanceStaticAddAfterCallBinary = (BoundBinary)((BoundReturn)
            instanceStaticAddAfterCallFunction.Body.Statements.Single()).Value!;
        Assert.That(instanceStaticAddAfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, GetArithmeticControl(instanceStaticAddAfterCallBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.StaticAdd64AfterCall",
            out var staticAdd64AfterCallDefinition), Is.True);
        var staticAdd64AfterCallMethodRow = checked((int)(
            staticAdd64AfterCallDefinition & 0x00FF_FFFF));
        var staticAdd64AfterCallBodyRow
            = tables.Methods.GetBodyRow(staticAdd64AfterCallMethodRow);
        Assert.That(staticAdd64AfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            staticAdd64AfterCallBodyRow, tables.Contracts.Count);
        var staticAdd64AfterCallBody = tables.Bodies.GetRegisterBody(
            staticAdd64AfterCallBodyRow, tables.Contracts.Count);
        Assert.That(staticAdd64AfterCallBody.HomeCount, Is.EqualTo(2));
        Assert.That(staticAdd64AfterCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                staticAdd64AfterCallBody.MethodMap),
            Is.EqualTo(add64Definition));
        var staticAdd64AfterCallFunction = program.Functions.Single(
            static function => function.Name == "StaticAdd64AfterCall");
        var staticAdd64AfterCallBinary = (BoundBinary)((BoundReturn)
            staticAdd64AfterCallFunction.Body.Statements.Single()).Value!;
        Assert.That(staticAdd64AfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 1, 0,
                0xC0, GetArithmeticControl(staticAdd64AfterCallBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.StaticAdd64AfterCallConstant",
            out var staticAdd64AfterCallConstantDefinition), Is.True);
        var staticAdd64AfterCallConstantMethodRow = checked((int)(
            staticAdd64AfterCallConstantDefinition & 0x00FF_FFFF));
        var staticAdd64AfterCallConstantBodyRow
            = tables.Methods.GetBodyRow(
                staticAdd64AfterCallConstantMethodRow);
        Assert.That(staticAdd64AfterCallConstantBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            staticAdd64AfterCallConstantBodyRow, tables.Contracts.Count);
        var staticAdd64AfterCallConstantBody
            = tables.Bodies.GetRegisterBody(
                staticAdd64AfterCallConstantBodyRow,
                tables.Contracts.Count);
        Assert.That(staticAdd64AfterCallConstantBody.ConstantCount,
            Is.EqualTo(1));
        Assert.That(staticAdd64AfterCallConstantBody.ConstantMap.Length,
            Is.EqualTo(8));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                staticAdd64AfterCallConstantBody.ConstantMap),
            Is.Not.Zero);
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                BinaryPrimitives.ReadUInt32LittleEndian(
                    staticAdd64AfterCallConstantBody.ConstantMap),
                tables.Signatures.GetResultTypeSpec(
                    tables.Methods.GetSignature(
                        staticAdd64AfterCallConstantMethodRow))),
            Is.True);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                staticAdd64AfterCallConstantBody.ConstantMap.Slice(4)),
            Is.GreaterThan(0u));
        Assert.That(tables.Features.TryGetFeature(11, out _), Is.True);
        var staticAdd64AfterCallConstantFunction = program.Functions.Single(
            static function =>
                function.Name == "StaticAdd64AfterCallConstant");
        var staticAdd64AfterCallConstantBinary = (BoundBinary)(
            (BoundReturn)staticAdd64AfterCallConstantFunction.Body.Statements
                .Single()).Value!;
        Assert.That(staticAdd64AfterCallConstantBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD4, 1, 0, 0,
                0xC0, GetArithmeticControl(
                    staticAdd64AfterCallConstantBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.InstanceStaticAdd64AfterCall",
            out var instanceStaticAdd64AfterCallDefinition), Is.True);
        var instanceStaticAdd64AfterCallMethodRow = checked((int)(
            instanceStaticAdd64AfterCallDefinition & 0x00FF_FFFF));
        var instanceStaticAdd64AfterCallBodyRow
            = tables.Methods.GetBodyRow(
                instanceStaticAdd64AfterCallMethodRow);
        Assert.That(instanceStaticAdd64AfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            instanceStaticAdd64AfterCallBodyRow, tables.Contracts.Count);
        var instanceStaticAdd64AfterCallBody = tables.Bodies.GetRegisterBody(
            instanceStaticAdd64AfterCallBodyRow, tables.Contracts.Count);
        Assert.That(instanceStaticAdd64AfterCallBody.HomeCount,
            Is.EqualTo(3));
        Assert.That(instanceStaticAdd64AfterCallBody.MethodCount,
            Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                instanceStaticAdd64AfterCallBody.MethodMap),
            Is.EqualTo(add64Definition));
        var instanceStaticAdd64AfterCallFunction = program.Functions.Single(
            static function =>
                function.Name == "InstanceStaticAdd64AfterCall");
        var instanceStaticAdd64AfterCallBinary = (BoundBinary)((BoundReturn)
            instanceStaticAdd64AfterCallFunction.Body.Statements.Single())
            .Value!;
        Assert.That(instanceStaticAdd64AfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC0, GetArithmeticControl(
                    instanceStaticAdd64AfterCallBinary),
                0xF0, 0x10,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Greater64AfterCall",
            out var greater64AfterCallDefinition), Is.True);
        var greater64AfterCallMethodRow = checked((int)(
            greater64AfterCallDefinition & 0x00FF_FFFF));
        var greater64AfterCallBodyRow
            = tables.Methods.GetBodyRow(greater64AfterCallMethodRow);
        Assert.That(greater64AfterCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            greater64AfterCallBodyRow, tables.Contracts.Count);
        var greater64AfterCallBody = tables.Bodies.GetRegisterBody(
            greater64AfterCallBodyRow, tables.Contracts.Count);
        Assert.That(greater64AfterCallBody.HomeCount, Is.EqualTo(2));
        Assert.That(greater64AfterCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                greater64AfterCallBody.MethodMap),
            Is.EqualTo(add64Definition));
        Assert.That(greater64AfterCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 1, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 4, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Sum64", out var sum64Definition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Greater64AfterInstanceCall",
            out var greater64AfterInstanceCallDefinition), Is.True);
        var greater64AfterInstanceCallMethodRow = checked((int)(
            greater64AfterInstanceCallDefinition & 0x00FF_FFFF));
        var greater64AfterInstanceCallBodyRow
            = tables.Methods.GetBodyRow(
                greater64AfterInstanceCallMethodRow);
        Assert.That(greater64AfterInstanceCallBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(
            greater64AfterInstanceCallBodyRow, tables.Contracts.Count);
        var greater64AfterInstanceCallBody = tables.Bodies.GetRegisterBody(
            greater64AfterInstanceCallBodyRow, tables.Contracts.Count);
        Assert.That(greater64AfterInstanceCallBody.HomeCount,
            Is.EqualTo(3));
        Assert.That(greater64AfterInstanceCallBody.MethodCount,
            Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                greater64AfterInstanceCallBody.MethodMap),
            Is.EqualTo(sum64Definition));
        Assert.That(greater64AfterInstanceCallBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE0, 0, 1, 0,
                0xD5, 1, 2, 0,
                0xC2, 0, 0xF0, 0x10,
                0xC3, 4, 0, 0xFF,
                0x17, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.CopyThroughLocal", out var copyDefinition), Is.True);
        var copyMethodRow
            = checked((int)(copyDefinition & 0x00FF_FFFF));
        var copyBodyRow = tables.Methods.GetBodyRow(copyMethodRow);
        Assert.That(copyBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(copyBodyRow,
            tables.Contracts.Count);
        var copyBody = tables.Bodies.GetRegisterBody(copyBodyRow,
            tables.Contracts.Count);
        Assert.That(copyBody.HomeCount, Is.EqualTo(3));
        Assert.That(copyBody.HomeMap[0], Is.EqualTo(1));
        Assert.That(copyBody.HomeMap[8], Is.EqualTo(2));
        Assert.That(copyBody.HomeMap[16], Is.EqualTo(3));
        Assert.That(copyBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD6, 0x10, 2, 0,
                0xEB, 0, 2, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.Copy64ThroughLocal",
            out var copy64Definition), Is.True);
        var copy64MethodRow
            = checked((int)(copy64Definition & 0x00FF_FFFF));
        var copy64BodyRow = tables.Methods.GetBodyRow(copy64MethodRow);
        Assert.That(copy64BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(copy64BodyRow,
            tables.Contracts.Count);
        var copy64Body = tables.Bodies.GetRegisterBody(copy64BodyRow,
            tables.Contracts.Count);
        Assert.That(copy64Body.HomeCount, Is.EqualTo(3));
        Assert.That(copy64Body.HomeMap[0], Is.EqualTo(1));
        Assert.That(copy64Body.HomeMap[8], Is.EqualTo(2));
        Assert.That(copy64Body.HomeMap[16], Is.EqualTo(3));
        Assert.That(copy64Body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD6, 0x10, 2, 0,
                0xEB, 0, 2, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.CopyFloat64ThroughLocal",
            out var copyFloat64Definition), Is.True);
        var copyFloat64MethodRow
            = checked((int)(copyFloat64Definition & 0x00FF_FFFF));
        var copyFloat64BodyRow
            = tables.Methods.GetBodyRow(copyFloat64MethodRow);
        Assert.That(copyFloat64BodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(copyFloat64BodyRow,
            tables.Contracts.Count);
        tables.ValidateRegisterBodyMapsForMethod(copyFloat64MethodRow);
        var copyFloat64Body = tables.Bodies.GetRegisterBody(
            copyFloat64BodyRow, tables.Contracts.Count);
        Assert.That(copyFloat64Body.HomeCount, Is.EqualTo(3));
        Assert.That(copyFloat64Body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD6, 0x10, 2, 0,
                0xEB, 0, 2, 0,
            }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "Box.XorThroughLocal", out var xorLocalDefinition), Is.True);
        var xorLocalMethodRow
            = checked((int)(xorLocalDefinition & 0x00FF_FFFF));
        var xorLocalBodyRow = tables.Methods.GetBodyRow(xorLocalMethodRow);
        Assert.That(xorLocalBodyRow, Is.GreaterThan(0));
        tables.Bodies.ValidateSelectedImplementation(xorLocalBodyRow,
            tables.Contracts.Count);
        var xorLocalBody = tables.Bodies.GetRegisterBody(xorLocalBodyRow,
            tables.Contracts.Count);
        Assert.That(xorLocalBody.HomeCount, Is.EqualTo(4));
        Assert.That(xorLocalBody.HomeMap[0], Is.EqualTo(1));
        Assert.That(xorLocalBody.HomeMap[8], Is.EqualTo(2));
        Assert.That(xorLocalBody.HomeMap[16], Is.EqualTo(2));
        Assert.That(xorLocalBody.HomeMap[24], Is.EqualTo(3));
        Assert.That(xorLocalBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xD5, 0, 1, 0,
                0xD6, 0x10, 3, 0,
                0xD5, 0, 3, 0,
                0xD5, 1, 2, 0,
                0xC1, 0x03, 0xF0, 0x10,
                0x17, 0,
            }));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1Construction2SchemaIdentity,
                artifact),
            "ClassInstanceXor", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Use(Box value) { return value.Xor(2, 3); }
            int32 UseSum(Box value) { return value.Sum(4, 5); }
            int32 UseDifference(Box value) {
                return value.Difference(7, 3);
            }
            int32 UseNegated(Box value) { return value.Negated(4); }
            int32 UseComplemented(Box value) {
                return value.Complemented(4);
            }
            int32 UseStaticXor() { return Box.StaticXor(4, 5); }
            int32 UseAdd() { return Box.Add(4, 5); }
            int64 UseAdd64(int64 left, int64 right) {
                return Box.Add64(left, right);
            }
            float32 UseNegateFloat32(Box value, float32 input) {
                return value.NegateFloat32(input);
            }
            float64 UseAddFloat64Constant(float64 left, float64 right) {
                return Box.AddFloat64Constant(left, right);
            }
            float64 UseStaticAddFloat64AfterCall(
                float64 left, float64 right) {
                return Box.StaticAddFloat64AfterCall(left, right);
            }
            float64 UseStaticAddFloat64AfterCallConstant(
                float64 left, float64 right) {
                return Box.StaticAddFloat64AfterCallConstant(left, right);
            }
            float64 UseAddFloat64Instance(
                Box value, float64 left, float64 right) {
                return value.AddFloat64Instance(left, right);
            }
            float64 UseInstanceStaticAddFloat64AfterCall(
                Box value, float64 left, float64 right) {
                return value.InstanceStaticAddFloat64AfterCall(left, right);
            }
            bool UseLessFloat64(float64 left, float64 right) {
                return Box.LessFloat64(left, right);
            }
            bool UseLessFloat64AfterCall(float64 left, float64 right) {
                return Box.LessFloat64AfterCall(left, right);
            }
            bool UseLessFloat64Instance(
                Box value, float64 left, float64 right) {
                return value.LessFloat64Instance(left, right);
            }
            bool UseLessFloat64AfterInstanceCall(
                Box value, float64 left, float64 right) {
                return value.LessFloat64AfterInstanceCall(left, right);
            }
            int64 UseSum64(Box value, int64 left, int64 right) {
                return value.Sum64(left, right);
            }
            uint128 UseXor128(uint128 left, uint128 right) {
                return Box.Xor128(left, right);
            }
            int64 UseSum64ThroughTwoLocals(int64 left, int64 right) {
                return Box.Sum64ThroughTwoLocals(left, right);
            }
            int64 UseSum64ThroughLocalConstant(
                int64 left, int64 right) {
                return Box.Sum64ThroughLocalConstant(left, right);
            }
            float64 UseSumFloat64ThroughTwoLocals(
                float64 left, float64 right) {
                return Box.SumFloat64ThroughTwoLocals(left, right);
            }
            float64 UseSumFloat64ThroughLocalConstant(
                float64 left, float64 right) {
                return Box.SumFloat64ThroughLocalConstant(left, right);
            }
            int64 UseNegate64(Box value, int64 input) {
                return value.Negate64(input);
            }
            bool UseGreater64(int64 left, int64 right) {
                return Box.Greater64(left, right);
            }
            int32 UseStaticAddAfterCall() {
                return Box.StaticAddAfterCall(2, 3);
            }
            int32 UseInstanceStaticAddAfterCall(Box value) {
                return value.InstanceStaticAddAfterCall(2, 3);
            }
            int64 UseStaticAdd64AfterCall(int64 left, int64 right) {
                return Box.StaticAdd64AfterCall(left, right);
            }
            int64 UseStaticAdd64AfterCallConstant(
                int64 left, int64 right) {
                return Box.StaticAdd64AfterCallConstant(left, right);
            }
            int64 UseInstanceStaticAdd64AfterCall(
                Box value, int64 left, int64 right) {
                return value.InstanceStaticAdd64AfterCall(left, right);
            }
            bool UseGreater64AfterCall(int64 left, int64 right) {
                return Box.Greater64AfterCall(left, right);
            }
            bool UseGreater64AfterInstanceCall(
                Box value, int64 left, int64 right) {
                return value.Greater64AfterInstanceCall(left, right);
            }
            int32 UseSubtract() { return Box.Subtract(7, 3); }
            int32 UseMultiply() { return Box.Multiply(4, 5); }
            int32 UseDivide() { return Box.Divide(12, 3); }
            int32 UseAddConstant() { return Box.AddConstant(5); }
            int32 UseSubtractFromConstant() {
                return Box.SubtractFromConstant(2);
            }
            int32 UseRemainder(Box value) {
                return value.Remainder(12, 5);
            }
            int32 UseSubtractFromSeven(Box value) {
                return value.SubtractFromSeven(2);
            }
            bool UseGreaterThanSeven(Box value) {
                return value.GreaterThanSeven(8);
            }
            int32 UseDouble() { return Box.Double(4); }
            int32 UseNegate() { return Box.Negate(4); }
            int32 UseComplement() { return Box.Complement(4); }
            int32 UseCopyStatic() { return Box.CopyStatic(42); }
            int64 UseCopy64Static(int64 value) {
                return Box.Copy64Static(value);
            }
            bool UseLessThan() { return Box.LessThan(1, 2); }
            bool UseEqualTo() { return Box.EqualTo(1, 2); }
            bool UseLessOrEqual() { return Box.LessOrEqual(1, 2); }
            bool UseGreaterThan(Box value) {
                return value.GreaterThan(2, 1);
            }
            bool UseGreaterOrEqual() { return Box.GreaterOrEqual(1, 2); }
            bool UseNotEqual(Box value) {
                return value.NotEqual(1, 2);
            }
            bool UseSame() { return Box.Same(true, false); }
            bool UseDifferent(Box value) {
                return value.Different(true, false);
            }
            int32 UseStaticXorThroughLocal() {
                return Box.StaticXorThroughLocal(1, 2);
            }
            int32 UseSumThroughLocal() {
                return Box.SumThroughLocal(3, 4);
            }
            int32 UseSumThroughTwoLocals() {
                return Box.SumThroughTwoLocals(3, 4);
            }
            bool UseLessThroughLocal() {
                return Box.LessThroughLocal(1, 2);
            }
            int32 UseAddThroughLocalConstant() {
                return Box.AddThroughLocalConstant(5);
            }
            bool UseLessThroughLocalConstant() {
                return Box.LessThroughLocalConstant(5);
            }
            int32 UseAfterCall(Box value) {
                return value.XorAfterCall(2, 3);
            }
            int32 UseSumAfterCall(Box value) {
                return value.SumAfterCall(2, 3);
            }
            bool UseLessThanAfterCall(Box value) {
                return value.LessThanAfterCall(2, 3);
            }
            int32 UseLocal(Box value) {
                return value.CopyThroughLocal(4);
            }
            int64 UseCopy64(Box value, int64 input) {
                return value.Copy64ThroughLocal(input);
            }
            float64 UseCopyFloat64(Box value, float64 input) {
                return value.CopyFloat64ThroughLocal(input);
            }
            int32 UseLocalXor(Box value) {
                return value.XorThroughLocal(4, 5);
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
    }

    [Test]
    public void EmitsExactIdentityForwardersForStaticAndInstanceClassMethods()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value) { Value = value; }
                public static int32 Identity(int32 value) {
                    return value;
                }
                public static int32 Forward(int32 value) {
                    return Identity(value);
                }
                public int32 IdentityInstance(int32 value) {
                    return value;
                }
                public int32 ForwardInstance(int32 value) {
                    return IdentityInstance(value);
                }
                public void Touch(int32 value) {}
                public void ForwardTouch(int32 value) {
                    Touch(value);
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var identity = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Identity"));
        var forward = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Forward"));
        var identityInstance = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "IdentityInstance"));
        var forwardInstance = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ForwardInstance"));
        var touch = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Touch"));
        var forwardTouch = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ForwardTouch"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassIdentityForwarder", C1M1Construction2SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, identity.Span,
                            "Box.Identity"),
                        new TrRmSourceClassMemberExport(0, forward.Span,
                            "Box.Forward"),
                        new TrRmSourceClassMemberExport(0,
                            identityInstance.Span, "Box.IdentityInstance"),
                        new TrRmSourceClassMemberExport(0,
                            forwardInstance.Span, "Box.ForwardInstance"),
                        new TrRmSourceClassMemberExport(0, touch.Span,
                            "Box.Touch"),
                        new TrRmSourceClassMemberExport(0, forwardTouch.Span,
                            "Box.ForwardTouch"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Construction2Draft(artifact));
        AssertIdentityForwarder("Box.Forward", "Box.Identity", false);
        AssertIdentityForwarder("Box.ForwardInstance",
            "Box.IdentityInstance", true);
        AssertIdentityForwarder("Box.ForwardTouch", "Box.Touch", true);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(C1M1Construction2SchemaIdentity,
                artifact),
            "ClassIdentityForwarder", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 UseStatic() { return Box.Forward(7); }
            int32 UseInstance(Box value) {
                return value.ForwardInstance(7);
            }
            void UseTouch(Box value) {
                value.ForwardTouch(7);
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));

        void AssertIdentityForwarder(string forwardKey, string identityKey,
            bool hasReceiver)
        {
            Assert.That(tables.Exports.TryGetDefinitionForKey(1, identityKey,
                out var identityDefinition), Is.True);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1, forwardKey,
                out var forwardDefinition), Is.True);
            var identityMethodRow
                = checked((int)(identityDefinition & 0x00FF_FFFF));
            var forwardMethodRow
                = checked((int)(forwardDefinition & 0x00FF_FFFF));
            Assert.That(tables.Methods.GetOwnerType(forwardMethodRow),
                Is.EqualTo(tables.Methods.GetOwnerType(identityMethodRow)));
            var forwardBodyRow
                = tables.Methods.GetBodyRow(forwardMethodRow);
            Assert.That(forwardBodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(forwardBodyRow,
                tables.Contracts.Count);
            var body = tables.Bodies.GetRow(forwardBodyRow);
            Assert.That(body.Length, Is.EqualTo(8));
            Assert.That(body[0], Is.EqualTo(0x05));
            Assert.That(body[1], Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
                Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body[4..]),
                Is.EqualTo(identityDefinition));

            var identityBodyRow
                = tables.Methods.GetBodyRow(identityMethodRow);
            Assert.That(identityBodyRow, Is.GreaterThan(0));
            tables.Bodies.ValidateSelectedImplementation(identityBodyRow,
                tables.Contracts.Count);
            Assert.That(tables.Bodies.GetRow(identityBodyRow)[0],
                Is.EqualTo(0x01));
            if (!hasReceiver)
            {
                var identityBody = tables.Bodies.GetRegisterBody(
                    identityBodyRow, tables.Contracts.Count);
                Assert.That(identityBody.HomeCount, Is.EqualTo(1));
                Assert.That(identityBody.HomeMap[0], Is.EqualTo(2));
                Assert.That(identityBody.GetBlockCode(0).ToArray(),
                    Is.EqualTo(new byte[] {
                        0xD5, 0, 0, 0,
                        0x17, 0,
                    }));
            }

            var identitySignature
                = tables.Methods.GetSignature(identityMethodRow);
            var forwardSignature
                = tables.Methods.GetSignature(forwardMethodRow);
            var identityReceiver
                = tables.Signatures.GetReceiverTypeSpec(identitySignature);
            var forwardReceiver
                = tables.Signatures.GetReceiverTypeSpec(forwardSignature);
            Assert.That(forwardReceiver, Is.EqualTo(identityReceiver));
            Assert.That(forwardReceiver != 0, Is.EqualTo(hasReceiver));
            Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                    tables.Signatures.GetResultTypeSpec(forwardSignature),
                    tables.Signatures.GetResultTypeSpec(identitySignature)),
                Is.True);
            var (identityParamFirst, identityParamCount)
                = tables.Signatures.GetParamRange(identitySignature);
            var (forwardParamFirst, forwardParamCount)
                = tables.Signatures.GetParamRange(forwardSignature);
            Assert.That(forwardParamCount, Is.EqualTo(identityParamCount));
            for (var parameter = 0; parameter < forwardParamCount; parameter++)
            {
                Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                        tables.Parameters.GetTypeSpec(forwardParamFirst
                            + parameter),
                        tables.Parameters.GetTypeSpec(identityParamFirst
                            + parameter)),
                    Is.True);
                Assert.That(tables.Parameters.GetMode(forwardParamFirst
                        + parameter),
                    Is.EqualTo(tables.Parameters.GetMode(identityParamFirst
                        + parameter)));
            }
        }
    }

    [Test]
    public void BindsImportedClassMethodDefaultFromSelectedView()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                public Box(int32 value = 42) { Value = value; }
                public static int32 Identity(int32 value = 11) {
                        return value;
                }
                public static int32 Unit(void marker = ()) {
                        return 13;
                }
                public void Touch(int32 value = 5) {}
                public void TouchUnit(void marker = ()) {}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var identityDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Identity"));
        var touchDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Touch"));
        var touchUnitDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "TouchUnit"));
        var unitDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Unit"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "ClassMethodDefaults",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                        classDeclaration.Span, "BoxKey", [
                            new TrRmSourceClassMemberExport(0,
                                fieldDeclaration.Span, "Box.Value"),
                            new TrRmSourceClassMemberExport(0,
                                identityDeclaration.Span, "Box.Identity"),
                            new TrRmSourceClassMemberExport(0,
                                unitDeclaration.Span, "Box.Unit"),
                            new TrRmSourceClassMemberExport(0,
                                touchDeclaration.Span, "Box.Touch"),
                            new TrRmSourceClassMemberExport(0,
                                touchUnitDeclaration.Span, "Box.TouchUnit"),
                        ]),
            ]);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                        .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                            artifact));
        Assert.That(providerTables.Features.TryGetFeature(11, out _),
            Is.True);
        Assert.That(providerTables.Features.TryGetFeature(9, out _),
            Is.False);
        Assert.That(providerTables.Exports.TryGetDefinitionForKey(1,
            "Box.Unit", out var unitDefinition), Is.True);
        var unitMethodRow
            = checked((int)(unitDefinition & 0x00FF_FFFF));
        var unitBodyRow = providerTables.Methods.GetBodyRow(unitMethodRow);
        Assert.That(unitBodyRow, Is.GreaterThan(0));
        Assert.That(providerTables.Bodies.GetRow(unitBodyRow)[0],
            Is.EqualTo(0x01));
        providerTables.Bodies.ValidateSelectedImplementation(unitBodyRow,
            providerTables.Contracts.Count);
        var unitBody = providerTables.Bodies.GetRegisterBody(unitBodyRow,
            providerTables.Contracts.Count);
        Assert.That(unitBody.HomeCount, Is.Zero);
        Assert.That(unitBody.ConstantCount, Is.EqualTo(1));
        Assert.That(unitBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] { 0xD4, 0, 0, 0, 0x17, 0 }));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                artifact),
            "ClassMethodDefaults", [], ["BoxKey"]);
        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Use(Box box) {
                box.Touch();
                box.TouchUnit();
                return Box.Identity() + Box.Unit();
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerProgram = (BoundProgram)consumer.Program!;
        var use = consumerProgram.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                        calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls, Has.Count.EqualTo(4));
        var arguments = calls.Select(static call =>
            call.Arguments.Last()).ToArray();
        var unitArguments = arguments.Where(static argument =>
            argument.Type == PrimitiveType.Void).ToArray();
        Assert.That(unitArguments, Has.Length.EqualTo(2));
        Assert.That(unitArguments.All(static argument =>
                argument is BoundConstant constant
                && constant.Bits.IsCanonicalFor(PrimitiveType.Void)),
            Is.True);
        var defaultValues = arguments.Where(static argument =>
                argument.Type != PrimitiveType.Void)
            .OfType<BoundConstant>()
            .Select(static value => value.Value);
        Assert.That(defaultValues, Is.EquivalentTo(new[] { 5, 11 }));
    }

    [Test]
    public void BindsExpectedClassMethodDefaultsSeparatelyFromProviderDefaults()
    {
        var expectedArtifact = EmitClassMethodProvider(
            "ClassDefaultsExpected", 7, "int32");
        var providerArtifact = AddMethodExportAlias(
            EmitClassMethodProvider(
                "ClassDefaultsProvider", 9, "int32"),
            "Box.Read", "Box.ReadAlias");
        var consumerArtifact = CreateExpectedClassMethodImportArtifact(
            expectedArtifact, providerArtifact, "ClassDefaultsConsumer",
            "ClassDefaultsProvider");
        var providerInput = new TrRmStandardArtifactInput(
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                consumerArtifact),
            "ClassDefaultsConsumer", [providerInput]);
        var declaration = reference.StandardClassDeclarations.Single(
            static candidate => candidate.Declaration.Name == "Box");
        var expectation = declaration.Expectations.Single();
        var expectedInitializer = expectation.ExpectedDeclaration.Methods
            .Single(static method => method.ConstructionRole == 1);
        var providerInitializer = declaration.Declaration.Methods
            .Single(static method => method.ConstructionRole == 1);
        var expectedRead = expectation.ExpectedDeclaration.Methods.Single(
            static method => method.ExportKeys.Contains("Box.Read",
                StringComparer.Ordinal));
        var providerRead = declaration.Declaration.Methods.Single(
            static method => method.ExportKeys.Contains("Box.Read",
                StringComparer.Ordinal));

        Assert.That(expectedInitializer.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(7));
        Assert.That(providerInitializer.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(9));
        Assert.That(expectedRead.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(7));
        Assert.That(providerRead.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(9));
        var readExpectations = expectation.MethodExpectations.Where(
            static method => method.ExportKey is "Box.Read"
                or "Box.ReadAlias").ToArray();
        Assert.That(readExpectations, Has.Length.EqualTo(2));
        Assert.That(readExpectations.Select(
                static method => method.ImportRow).Distinct().Count(),
            Is.EqualTo(2));
        Assert.That(readExpectations.Select(
                static method => method.ProviderDefinitionId).Distinct()
                .Count(), Is.EqualTo(1));
        Assert.That(GetReadDefault(reference), Is.EqualTo(7));

        var replacementArtifact = AddMethodExportAlias(
            EmitClassMethodProvider(
                "ClassDefaultsProvider", 11, "int32"),
            "Box.Read", "Box.ReadAlias");
        var replacementReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    consumerArtifact),
                "ClassDefaultsConsumer", [
                    new TrRmStandardArtifactInput(
                        C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                        replacementArtifact),
                ]);
        var replacementDeclaration
            = replacementReference.StandardClassDeclarations.Single(
                static candidate => candidate.Declaration.Name == "Box");
        var replacementExpectation
            = replacementDeclaration.Expectations.Single();
        var replacementExpectedRead
            = replacementExpectation.ExpectedDeclaration.Methods.Single(
                static method => method.ExportKeys.Contains("Box.Read",
                    StringComparer.Ordinal));
        var replacementProviderRead
            = replacementDeclaration.Declaration.Methods.Single(
                static method => method.ExportKeys.Contains("Box.Read",
                    StringComparer.Ordinal));
        Assert.That(replacementExpectedRead.Parameters[0]
            .DefaultValue!.Value, Is.EqualTo(7));
        Assert.That(replacementProviderRead.Parameters[0]
            .DefaultValue!.Value, Is.EqualTo(11));
        Assert.That(GetReadDefault(replacementReference), Is.EqualTo(7));

        var incompatibleProvider = AddMethodExportAlias(
            EmitClassMethodProvider(
                "ClassDefaultsProvider", 9, "int64"),
            "Box.Read", "Box.ReadAlias");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    consumerArtifact),
                "ClassDefaultsConsumer", [
                    new TrRmStandardArtifactInput(
                        C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                        incompatibleProvider),
                ]));
        var incompatibleEffectsProvider = AddMethodExportAlias(
            EmitClassMethodProvider(
                "ClassDefaultsProvider", 9, "int32",
                readIsUnsafe: true),
            "Box.Read", "Box.ReadAlias");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    consumerArtifact),
                "ClassDefaultsConsumer", [
                    new TrRmStandardArtifactInput(
                        C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                        incompatibleEffectsProvider),
                ]));
        var incompatibleReceiverProvider = AddMethodExportAlias(
            EmitClassMethodProvider(
                "ClassDefaultsProvider", 9, "int32",
                readIsInstance: true),
            "Box.Read", "Box.ReadAlias");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    consumerArtifact),
                "ClassDefaultsConsumer", [
                    new TrRmStandardArtifactInput(
                        C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                        incompatibleReceiverProvider),
                ]));

        static int GetReadDefault(CompilationReference selectedReference)
        {
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
                "int32 Use() { return Box.Read(); }"), selectedReference);
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = (BoundProgram)compilation.Program!;
            var function = program.Functions.Single(
                static candidate => candidate.Name == "Use");
            var calls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall call)
                    {
                        calls.Add(call);
                    }
                }), Is.True);
            Assert.That(calls, Has.Count.EqualTo(1));
            var argument = calls[0].Arguments.Single();
            Assert.That(argument, Is.TypeOf<BoundConstant>());
            return checked((int)((BoundConstant)argument).Value);
        }
    }

    [Test]
    public void ReemitsIdentityForwardersAndTheirMethodTargets()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Marker;
                public static int32 Identity(int32 value) {
                    return value;
                }
                public static int32 Forward(int32 value) {
                    return Identity(value);
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var boxDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var boxMembers = boxDeclaration.Children.OfType<SyntaxNode>();
        var boxField = boxMembers.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var identity = boxMembers.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Identity"));
        var forward = boxMembers.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Forward"));
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "IdentityForwarderReemission", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, boxClassIndex,
                    boxDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            boxField.Span, "Box.Marker"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            identity.Span, "Box.Identity"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            forward.Span, "Box.Forward"),
                    ]),
            ]);

        static (int MethodRow, uint TargetToken) ReadForwarderTarget(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables)
        {
            var forwardDefinition = tables.Exports
                .GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "Box.Forward").Definition;
            var identityDefinition = tables.Exports
                .GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "Box.Identity").Definition;
            var methodRow = checked((int)(
                forwardDefinition & 0x00FF_FFFF));
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            var body = tables.Bodies.GetRow(bodyRow);
            Assert.That(body.Length, Is.EqualTo(8));
            Assert.That(body[0], Is.EqualTo((byte)0x05));
            return (checked((int)(identityDefinition & 0x00FF_FFFF)),
                BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4)));
        }

        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var (oldIdentityRow, oldForwardTarget)
            = ReadForwarderTarget(artifactTables);
        Assert.That(oldForwardTarget,
            Is.EqualTo(0x1600_0000u | checked((uint)oldIdentityRow)));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            artifactInput, "IdentityForwarderReemission", [], ["BoxKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "IdentityForwarderReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var (newIdentityRow, newForwardTarget)
            = ReadForwarderTarget(reemittedTables);
        Assert.That(newForwardTarget,
            Is.EqualTo(0x1600_0000u | checked((uint)newIdentityRow)));

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "IdentityForwarderReemitted", [], ["BoxKey"]);
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Use(int32 value) {
                    return Box.Forward(value);
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));
    }

    [Test]
    public void ReemitsBodylessClassMethodImportsWithClassParameterSurfaces()
    {
        var expectedArtifact = EmitClassMethodReemissionArtifact(
            "MethodExpected", 7, includePrefixType: true,
            readIsUnsafe: true, mutableReceiver: true);
        var providerArtifact = AddMethodExportAlias(
            EmitClassMethodReemissionArtifact("MethodProvider", 9,
                includePrefix: true, readIsUnsafe: true,
                mutableReceiver: true),
            "Box.Read", "Box.ReadAlias");
        var consumerArtifact = CreateExpectedClassMethodImportArtifact(
            expectedArtifact, providerArtifact, "MethodConsumer",
            "MethodProvider", [
                "Box.Read", "Box.ReadBox", "Box.ReadInstance",
                "Box.ReadMutable",
            ], "Box.Read", "Box.ReadAlias", "ContainerKey");
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "MethodConsumer", [providerInput], ["ContainerKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));

        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "MethodConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var consumerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        consumerArtifact));
        var oldBoxTypeImport = Enumerable.Range(1,
                consumerTables.Imports.Count)
            .Single(row => consumerTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x13);
        var oldBoxTypeDefRow = checked((int)(
            consumerTables.Imports.GetExpectedDefinitionForImportRow(
                oldBoxTypeImport) & 0x00FF_FFFF));
        var oldMethodImport = Enumerable.Range(1,
                consumerTables.Imports.Count)
            .First(row => consumerTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x16);
        var oldMethodDefRow = checked((int)(
            consumerTables.Imports.GetExpectedDefinitionForImportRow(
                oldMethodImport) & 0x00FF_FFFF));
        Assert.That(consumerTables.Methods.GetOwnerType(oldMethodDefRow),
            Is.EqualTo(oldBoxTypeDefRow));
        var methodImportRows = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Where(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x16)
            .ToArray();
        Assert.That(methodImportRows, Has.Length.EqualTo(5));
        Assert.That(methodImportRows.Select(row => reemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey),
            Is.EquivalentTo([
                "Box.Read", "Box.ReadAlias", "Box.ReadBox",
                "Box.ReadInstance", "Box.ReadMutable",
            ]));
        var expectedMethodDefinitions = methodImportRows.Select(row =>
                reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(row))
            .Distinct().ToArray();
        Assert.That(expectedMethodDefinitions, Has.Length.EqualTo(4));
        var expectedMethodRow = checked((int)(
            expectedMethodDefinitions[0] & 0x00FF_FFFF));
        var newBoxTypeImport = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x13);
        var newBoxTypeDefRow = checked((int)(
            reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                newBoxTypeImport) & 0x00FF_FFFF));
        Assert.That(newBoxTypeDefRow, Is.Not.EqualTo(oldBoxTypeDefRow));
        Assert.That(reemittedTables.TypeDefs.GetFlags(newBoxTypeDefRow)
            & 0x80u, Is.EqualTo(0x80u));
        Assert.That(reemittedTables.Methods.GetOwnerType(expectedMethodRow),
            Is.EqualTo(newBoxTypeDefRow));
        Assert.That(reemittedTables.Methods.GetBodyRow(expectedMethodRow),
            Is.Zero);
        Assert.That(reemittedTables.Contracts.GetEffectsMask(
            expectedMethodRow), Is.EqualTo(4));
        var expectedMethodSignature = reemittedTables.Methods
            .GetSignature(expectedMethodRow);
        Assert.That(reemittedTables.Signatures.GetParamRange(
            expectedMethodSignature).Count, Is.EqualTo(1));
        Assert.That(reemittedTables.MethodParameterLabels
            .TryGetNameStringRow(expectedMethodRow, 0,
                out var parameterNameStringRow), Is.True);
        Assert.That(reemittedTables.Strings.DecodeName(
            parameterNameStringRow), Is.EqualTo("value"));
        Assert.That(reemittedTables.MethodParameterLabels
            .TryGetDefaultConstantSourceRow(expectedMethodRow, 0,
                out var defaultConstantSourceRow), Is.True);
        Assert.That(defaultConstantSourceRow, Is.GreaterThan(0));
        var (methodStart, methodEnd)
            = reemittedTables.Modules.GetMethodDefRange(1);
        var instanceMethodRow = Enumerable.Range(methodStart,
                methodEnd - methodStart)
            .Single(row => reemittedTables.Methods.GetName(row)
                == "ReadInstance");
        var instanceSignature = reemittedTables.Methods
            .GetSignature(instanceMethodRow);
        var receiverTypeSpec = reemittedTables.Signatures
            .GetReceiverTypeSpec(instanceSignature);
        Assert.That(receiverTypeSpec, Is.Not.Zero);
        Assert.That(reemittedTables.TypeSpecs.GetNominalTypeDefRow(
                receiverTypeSpec, reemittedTables.TypeDefs,
                reemittedTables.Imports),
            Is.EqualTo(newBoxTypeDefRow));
        var readBoxMethodRow = Enumerable.Range(methodStart,
                methodEnd - methodStart)
            .Single(row => reemittedTables.Methods.GetName(row)
                == "ReadBox");
        var readBoxSignature = reemittedTables.Methods
            .GetSignature(readBoxMethodRow);
        var (readBoxParameterStart, readBoxParameterCount)
            = reemittedTables.Signatures.GetParamRange(readBoxSignature);
        Assert.That(readBoxParameterCount, Is.EqualTo(1));
        var readBoxParameterType
            = reemittedTables.Parameters.GetTypeSpec(
                readBoxParameterStart);
        Assert.That(reemittedTables.TypeSpecs.GetNominalTypeDefRow(
                readBoxParameterType, reemittedTables.TypeDefs,
                reemittedTables.Imports),
            Is.EqualTo(newBoxTypeDefRow));
        Assert.That(reemittedTables.Signatures.GetResultTypeSpec(
            readBoxSignature), Is.EqualTo(0xF200_0000u));
        var mutableMethodRow = Enumerable.Range(methodStart,
                methodEnd - methodStart)
            .Single(row => reemittedTables.Methods.GetName(row)
                == "ReadMutable");
        Assert.That(reemittedTables.Methods.GetFlags(mutableMethodRow) & 4u,
            Is.EqualTo(4u));
        var mutableSignature = reemittedTables.Methods
            .GetSignature(mutableMethodRow);
        var mutableReceiverTypeSpec = reemittedTables.Signatures
            .GetReceiverTypeSpec(mutableSignature);
        var mutableReceiverType = reemittedTables.TypeSpecs
            .GetTypeSpecRef(mutableReceiverTypeSpec);
        Assert.That(mutableReceiverType.Qualifiers & 1, Is.EqualTo(1));
        Assert.That(reemittedTables.TypeSpecs.GetNominalTypeDefRow(
                mutableReceiverTypeSpec, reemittedTables.TypeDefs,
                reemittedTables.Imports),
            Is.EqualTo(newBoxTypeDefRow));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "MethodConsumerReemitted", [providerInput],
                ["ContainerKey"]);
        var declaration = reemittedReference.StandardClassDeclarations
            .Single(candidate => candidate.Declaration.Name == "Box");
        var expectation = declaration.Expectations.Single();
        var expectedMethod = expectation.ExpectedDeclaration.Methods
            .Single(static method => method.Name == "Read");
        var providerMethod = declaration.Declaration.Methods
            .Single(static method => method.Name == "Read");
        Assert.That(expectedMethod.BodyRow, Is.Zero);
        Assert.That(providerMethod.BodyRow, Is.GreaterThan(0));
        Assert.That(expectedMethod.Parameters[0].Name, Is.EqualTo("value"));
        Assert.That(expectedMethod.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(7));
        Assert.That(providerMethod.Parameters[0].DefaultValue!.Value,
            Is.EqualTo(9));
        var expectedMutableMethod = expectation.ExpectedDeclaration.Methods
            .Single(static method => method.Name == "ReadMutable");
        var providerMutableMethod = declaration.Declaration.Methods
            .Single(static method => method.Name == "ReadMutable");
        Assert.That(expectedMutableMethod.BodyRow, Is.Zero);
        Assert.That(expectedMutableMethod.Flags & 4u, Is.EqualTo(4u));
        Assert.That(providerMutableMethod.BodyRow, Is.GreaterThan(0));
        Assert.That(providerMutableMethod.Flags & 4u, Is.EqualTo(4u));
        Assert.That(expectation.MethodExpectations.Select(
                static method => method.ExportKey),
            Is.EquivalentTo([
                "Box.Read", "Box.ReadAlias", "Box.ReadBox",
                "Box.ReadInstance", "Box.ReadMutable",
            ]));

        var relocatedProvider = AddMethodExportAlias(
            EmitClassMethodReemissionArtifact("MethodProvider", 11,
                includePrefix: true, readIsUnsafe: true,
                mutableReceiver: true),
            "Box.Read", "Box.ReadAlias");
        var relocatedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "MethodConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        relocatedProvider),
                ], ["ContainerKey"]);
        var relocatedClass = relocatedReference.StandardClassDeclarations
            .Single(candidate => candidate.Declaration.Name == "Box");
        var relocatedExpectation = relocatedClass.Expectations.Single();
        Assert.That(relocatedExpectation.ExpectedDeclaration.Methods
            .Single(static method => method.Name == "Read")
            .Parameters[0].DefaultValue!.Value, Is.EqualTo(7));
        Assert.That(relocatedClass.Declaration.Methods
            .Single(static method => method.Name == "Read")
            .Parameters[0].DefaultValue!.Value, Is.EqualTo(11));

        var incompatibleProvider = AddMethodExportAlias(
            EmitClassMethodReemissionArtifact("MethodProvider", 11,
                includePrefix: true, parameterType: "int64",
                readIsUnsafe: true, mutableReceiver: true),
            "Box.Read", "Box.ReadAlias");
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "MethodConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProvider),
                ], ["ContainerKey"]));
    }

    [Test]
    public void BindsTopLevelModuleFunctionParameterDefaults()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsCopyEvidenceSchemaIdentity;
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Payload {
                    public int32 Value;
                    public Payload(int32 value) { Value = value; }
                }
                public unsafe int32 Echo(Payload value, int32 count = 7) {
                    return 7;
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "DefaultProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(providerProgram, providerTree));
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    providerArtifact),
                "DefaultProvider", []);
        var echo = providerReference.Functions.Single(
            static function => function.Name == "Echo");
        Assert.That(echo.HasOrdinaryParameterDefaultSurface, Is.True);
        var echoDefault = echo.OrdinaryParameterDefaults.Single();
        Assert.That(echoDefault.ParameterOrdinal, Is.EqualTo(1));
        Assert.That(echoDefault.Value!.Bits,
            Is.EqualTo(ScalarBits.FromPayload(PrimitiveType.Int32,
                [7, 0, 0, 0])));

        var useTree = SyntaxTree.Parse("""
            namespace Demo {
                public unsafe int32 Use(Payload value) {
                    return unsafe(Echo(value));
                }
            }
            """);
        var useCompilation = Compilation.CreateLibrary(useTree,
            providerReference);
        Assert.That(useCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, useCompilation.Diagnostics));
        var useProgram = (BoundProgram)useCompilation.Program!;
        var useArtifact = TrRmExecutableCompiler.EmitStandard(useProgram,
            "DefaultConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(useProgram, useTree));
        var useTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                useArtifact,
                TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft));
        Assert.That(useTables.Exports.TryGetDefinitionForKey(1, "Use",
            out var useDefinition), Is.True);
        var useMethodRow = checked((int)(useDefinition & 0x00FF_FFFF));
        var useBody = useTables.Bodies.GetRegisterBody(
            useTables.Methods.GetBodyRow(useMethodRow),
            useTables.Contracts.Count);
        Assert.That(useBody.MethodCount, Is.EqualTo(1));
        Assert.That(useBody.ConstantCount, Is.EqualTo(1));
        var useConstantSourceRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                useBody.ConstantMap.Slice(sizeof(uint))));
        var useConstantNodeRow = checked((int)
            useTables.ConstantSources.GetPayloadReference(
                useConstantSourceRow));
        Assert.That(useTables.FrozenValueNodes.GetScalarPayload(
                useConstantNodeRow, useTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 7, 0, 0, 0 }));

        var useInput = new TrRmStandardArtifactInput(schemaIdentity,
            useArtifact);
        var useReference
            = TrRmExecutableCompiler.CreateStandardReference(useInput,
                "DefaultConsumer", [new TrRmStandardArtifactInput(
                    schemaIdentity, providerArtifact)]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), useReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var sourceFreeArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "DefaultConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var sourceFreeTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                sourceFreeArtifact,
                TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft));
        Assert.That(sourceFreeTables.Exports.TryGetDefinitionForKey(1,
            "Use", out var sourceFreeUseDefinition), Is.True);
        var sourceFreeUseMethodRow = checked((int)(
            sourceFreeUseDefinition & 0x00FF_FFFF));
        var sourceFreeUseBody = sourceFreeTables.Bodies.GetRegisterBody(
            sourceFreeTables.Methods.GetBodyRow(sourceFreeUseMethodRow),
            sourceFreeTables.Contracts.Count);
        Assert.That(sourceFreeUseBody.MethodCount, Is.EqualTo(1));
        Assert.That(sourceFreeUseBody.ConstantCount, Is.EqualTo(1));
        var sourceFreeConstantSourceRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                sourceFreeUseBody.ConstantMap.Slice(sizeof(uint))));
        var sourceFreeConstantNodeRow = checked((int)
            sourceFreeTables.ConstantSources.GetPayloadReference(
                sourceFreeConstantSourceRow));
        Assert.That(sourceFreeTables.FrozenValueNodes.GetScalarPayload(
                sourceFreeConstantNodeRow, sourceFreeTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 7, 0, 0, 0 }));
        var sourceFreeReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    sourceFreeArtifact), "DefaultConsumerReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        providerArtifact),
                ]);
        var sourceFreeEcho = sourceFreeReference.Functions.Single(
            static function => function.Name == "Echo");
        Assert.That(sourceFreeEcho.HasOrdinaryParameterDefaultSurface,
            Is.True);
        Assert.That(sourceFreeEcho.OrdinaryParameterDefaults.Single()
            .Value!.Bits, Is.EqualTo(ScalarBits.FromPayload(
                PrimitiveType.Int32, [7, 0, 0, 0])));
    }

    [Test]
    public void LinksTopLevelConstruction2MethodImportsForStandardExecution()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsCopyEvidenceSchemaIdentity;
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public unsafe int32 Get() { return 42; }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "MethodRuntimeProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(providerInput,
                "MethodRuntimeProvider", []);

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                int32 Main() { return unsafe(Get()); }
            }
            """);
        var consumerCompilation = Compilation.Create(consumerTree,
            providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "MethodRuntimeConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var providers = new[] { providerInput };

        var loaded = TrRmExecutableCompiler.LoadStandard(consumerInput,
            "MethodRuntimeConsumer", providers);
        Assert.That(loaded.EntryPoint, Is.GreaterThanOrEqualTo(0));
        Assert.That(loaded.Functions, Has.Length.EqualTo(2));
        Assert.That(loaded.Functions.Any(static function =>
                function.IsExternal || function.IsModuleReference),
            Is.False);
        var main = loaded.Functions.Single(
            static function => function.Name == "Main");
        var mainCall = (BoundCall)((BoundReturn)
            main.Body.Statements.Single()).Value!;
        var linkedTarget = loaded.Functions[mainCall.Function];
        Assert.That(linkedTarget.RequiresUnsafeCaller, Is.True);
        Assert.That(mainCall.IsUnsafeAuthorized, Is.True);
        Assert.That(NativeModuleCompiler.ExecuteStandard(consumerInput,
            "MethodRuntimeConsumer", providers,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        var nativeImage = NativeModuleCompiler.EmitStandardExecutable(
            consumerInput, "MethodRuntimeConsumer", providers,
            NativeRuntimePolicy.FailFast);
        Assert.That(nativeImage.AsSpan(0, 2).ToArray(),
            Is.EqualTo(new byte[] { (byte)'M', (byte)'Z' }));

        var incompatibleProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public int32 Get(int32 value) { return value; }
            }
            """);
        var incompatibleProviderCompilation
            = Compilation.CreateLibrary(incompatibleProviderTree);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderProgram
            = (BoundProgram)incompatibleProviderCompilation.Program!;
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderProgram, "MethodRuntimeProvider",
                schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(consumerInput,
                "MethodRuntimeConsumer",
                [new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleProviderArtifact)]));
    }

    [Test]
    public void LinksTopLevelConstruction2MethodImportsWithClassHandleTypes()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsCopyEvidenceSchemaIdentity;
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Marker { }
                public int32 Read(Marker value) { return 7; }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "ClassMethodProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(providerProgram, providerTree));
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(providerInput,
                "ClassMethodProvider", []);

        var consumerTree = SyntaxTree.Parse("""
            namespace Demo {
                public int32 Forward(Marker value) {
                    return Read(value);
                }
            }
            """);
        var consumerCompilation = Compilation.CreateLibrary(consumerTree,
            providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "ClassMethodConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(consumerProgram, consumerTree));
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(consumerInput,
            "ClassMethodConsumer", [providerInput]);

        var forward = loaded.Functions.Single(
            static function => function.Name == "Forward");
        var call = (BoundCall)((BoundReturn)forward.Body.Statements.Single())
            .Value!;
        var target = loaded.Functions[call.Function];
        Assert.That(target.IsExternal, Is.False);
        Assert.That(call.Arguments[0].Type,
            Is.EqualTo(forward.Variables[0].Type));
        Assert.That(BoundGenericNominalTypeTypes.IsType(
                forward.Variables[0].Type),
            Is.True);
        var classIdentity = loaded.GenericNominalTypeInstances[
            BoundGenericNominalTypeTypes.Index(
                forward.Variables[0].Type)].Definition;
        Assert.That(classIdentity.ProviderIdentity,
            Is.EqualTo("ClassMethodProvider"));
        Assert.That(classIdentity.ModuleIdentity,
            Is.EqualTo("ClassMethodProvider"));
        Assert.That(loaded.StandardClassDeclarations.Any(declaration =>
                declaration.Declaration.Identity == classIdentity),
            Is.True);
    }

    [Test]
    public void EmitsAndReemitsUnitCallsToForwardedModuleFunctions()
    {
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Payload {
                    public int32 Value;
                    public Payload(int32 value) { Value = value; }
                }
                public void Ping() {}
                public int32 Answer(int32 value) { return 42; }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerSchema = C1M1Construction2SchemaIdentity;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "UnitFunctionProvider", providerSchema,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(providerProgram, providerTree));
        var providerInput = new TrRmStandardArtifactInput(providerSchema,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "UnitFunctionProvider", []);

        var forwarderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int32 Value;
                    public Box(int32 value) { Value = value; }
                }
                public void Forward() { Ping(); }
                public int32 ForwardAnswer() { return Answer(42); }
            }
            """);
        var forwarderCompilation = Compilation.CreateLibrary(forwarderTree,
            providerReference);
        Assert.That(forwarderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                forwarderCompilation.Diagnostics));
        var forwarderProgram = (BoundProgram)forwarderCompilation.Program!;
        var forwarderArtifact = TrRmExecutableCompiler.EmitStandard(
            forwarderProgram, "UnitFunctionForwarder", providerSchema,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(forwarderProgram, forwarderTree));
        var forwarderTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                forwarderArtifact,
                TrRmArtifactSchema.C1M1Construction2Draft));
        var pingImportRow = Enumerable.Range(1,
                forwarderTables.Imports.Count)
            .Single(row => forwarderTables.Imports
                .GetBindingForImportRow(row).ExportKey == "Ping");
        var pingExpectedMethodRow = checked((int)(
            forwarderTables.Imports
                .GetExpectedDefinitionForImportRow(pingImportRow)
            & 0x00FF_FFFF));
        Assert.That(forwarderTables.Exports.TryGetDefinitionForKey(1,
            "Forward", out var forwardDefinition), Is.True);
        var forwardMethodRow = checked((int)(
            forwardDefinition & 0x00FF_FFFF));
        var forwardBodyRow
            = forwarderTables.Methods.GetBodyRow(forwardMethodRow);
        var forwardBody = forwarderTables.Bodies.GetRegisterBody(
            forwardBodyRow, forwarderTables.Contracts.Count);
        Assert.That(forwardBody.MethodMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                forwardBody.MethodMap),
            Is.EqualTo(0x1600_0000u
                | checked((uint)pingExpectedMethodRow)));
        var answerImportRow = Enumerable.Range(1,
                forwarderTables.Imports.Count)
            .Single(row => forwarderTables.Imports
                .GetBindingForImportRow(row).ExportKey == "Answer");
        var answerExpectedMethodRow = checked((int)(
            forwarderTables.Imports
                .GetExpectedDefinitionForImportRow(answerImportRow)
            & 0x00FF_FFFF));
        Assert.That(forwarderTables.Exports.TryGetDefinitionForKey(1,
            "ForwardAnswer", out var forwardAnswerDefinition), Is.True);
        var forwardAnswerMethodRow = checked((int)(
            forwardAnswerDefinition & 0x00FF_FFFF));
        var forwardAnswerBody = forwarderTables.Bodies.GetRegisterBody(
            forwarderTables.Methods.GetBodyRow(forwardAnswerMethodRow),
            forwarderTables.Contracts.Count);
        Assert.That(forwardAnswerBody.MethodMap.Length,
            Is.EqualTo(sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                forwardAnswerBody.MethodMap),
            Is.EqualTo(0x1600_0000u
                | checked((uint)answerExpectedMethodRow)));
        Assert.That(forwardAnswerBody.ConstantMap.Length,
            Is.EqualTo(2 * sizeof(uint)));
        var forwardAnswerConstantSourceRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                forwardAnswerBody.ConstantMap[sizeof(uint)..]));
        var forwardAnswerValueNodeRow = checked((int)
            forwarderTables.ConstantSources.GetPayloadReference(
                forwardAnswerConstantSourceRow));
        Assert.That(forwarderTables.FrozenValueNodes.GetScalarPayload(
                forwardAnswerValueNodeRow, forwarderTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 42, 0, 0, 0 }));

        var forwarderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(providerSchema,
                    forwarderArtifact),
                "UnitFunctionForwarder", [providerInput]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), forwarderReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "UnitFunctionForwarderReemitted", providerSchema,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemitted, TrRmArtifactSchema.C1M1Construction2Draft));
        var reemittedPingImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey == "Ping");
        var reemittedPingMethodRow = checked((int)(
            reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                reemittedPingImportRow) & 0x00FF_FFFF));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Forward", out var reemittedForwardDefinition), Is.True);
        var reemittedForwardRow = checked((int)(
            reemittedForwardDefinition & 0x00FF_FFFF));
        var reemittedForwardBody
            = reemittedTables.Bodies.GetRegisterBody(
                reemittedTables.Methods.GetBodyRow(reemittedForwardRow),
                reemittedTables.Contracts.Count);
        Assert.That(reemittedForwardBody.MethodMap.Length,
            Is.EqualTo(sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedForwardBody.MethodMap),
            Is.EqualTo(0x1600_0000u
                | checked((uint)reemittedPingMethodRow)));
        var reemittedAnswerImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey == "Answer");
        var reemittedAnswerMethodRow = checked((int)(
            reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                reemittedAnswerImportRow) & 0x00FF_FFFF));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "ForwardAnswer", out var reemittedForwardAnswerDefinition),
            Is.True);
        var reemittedForwardAnswerMethodRow = checked((int)(
            reemittedForwardAnswerDefinition & 0x00FF_FFFF));
        var reemittedForwardAnswerBody = reemittedTables.Bodies
            .GetRegisterBody(reemittedTables.Methods.GetBodyRow(
                reemittedForwardAnswerMethodRow),
                reemittedTables.Contracts.Count);
        Assert.That(reemittedForwardAnswerBody.MethodMap.Length,
            Is.EqualTo(sizeof(uint)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedForwardAnswerBody.MethodMap),
            Is.EqualTo(0x1600_0000u
                | checked((uint)reemittedAnswerMethodRow)));
        Assert.That(reemittedForwardAnswerBody.ConstantMap.Length,
            Is.EqualTo(2 * sizeof(uint)));
        var reemittedAnswerConstantSourceRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedForwardAnswerBody.ConstantMap[sizeof(uint)..]));
        var reemittedAnswerValueNodeRow = checked((int)
            reemittedTables.ConstantSources.GetPayloadReference(
                reemittedAnswerConstantSourceRow));
        Assert.That(reemittedTables.FrozenValueNodes.GetScalarPayload(
                reemittedAnswerValueNodeRow, reemittedTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 42, 0, 0, 0 }));

        var downstream = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public void InvokeForward() { Forward(); }
            public int32 InvokeForwardAnswer() { return ForwardAnswer(); }
            """), TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(providerSchema, reemitted),
                "UnitFunctionForwarderReemitted", [providerInput]));
        Assert.That(downstream.HasErrors, Is.False,
            string.Join(Environment.NewLine, downstream.Diagnostics));
    }

    [Test]
    public void ReemitsForwardedTopLevelMethodImportsAndBindsTheirProvider()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsCopyEvidenceSchemaIdentity;
        var providerTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Payload {
                    public int32 Value;
                    public Payload(int32 value) { Value = value; }
                }
                [return: DependsOn(nameof(value))]
                public unsafe int32 Echo(Payload value, int32 count = 7) {
                    return 7;
                }
            }
            """);
        var providerCompilation = Compilation.CreateLibrary(providerTree);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerProgram = (BoundProgram)providerCompilation.Program!;
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "FunctionProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(providerProgram, providerTree));
        providerArtifact = AddMethodExportAlias(providerArtifact, "Echo",
            "EchoAlias",
            TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference
            = TrRmExecutableCompiler.CreateStandardReference(providerInput,
                "FunctionProvider", []);
        Assert.That(providerReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Echo").And.Contain("EchoAlias"));

        var forwarderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Box {
                    public int32 Value;
                    public Box(int32 value) { Value = value; }
                }
                [return: DependsOn(nameof(value))]
                public unsafe int32 Forward(Payload value, int32 count) {
                    return unsafe(Echo(value, count));
                }
                [return: DependsOn(nameof(value))]
                public unsafe int32 ForwardAlias(Payload value, int32 count) {
                    return unsafe(EchoAlias(value, count));
                }
            }
            """);
        var forwarderCompilation = Compilation.CreateLibrary(
            forwarderTree, providerReference);
        Assert.That(forwarderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                forwarderCompilation.Diagnostics));
        var forwarderProgram = (BoundProgram)forwarderCompilation.Program!;
        var forwarderArtifact = TrRmExecutableCompiler.EmitStandard(
            forwarderProgram, "FunctionForwarder", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], CreateClassExports(forwarderProgram, forwarderTree));
        var forwarderTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                forwarderArtifact,
                TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft));
        var importedMethodRows = Enumerable.Range(1,
                forwarderTables.Imports.Count)
            .Where(row => forwarderTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x16)
            .ToArray();
        Assert.That(importedMethodRows, Has.Length.EqualTo(2));
        Assert.That(importedMethodRows.Select(row => forwarderTables.Imports
                .GetBindingForImportRow(row).ExportKey),
            Is.EquivalentTo(["Echo", "EchoAlias"]));
        Assert.That(importedMethodRows.Select(row =>
                forwarderTables.ModuleRefs.GetProducerIdentity(
                    forwarderTables.Imports.GetModuleRefForImportRow(row))),
            Is.All.EqualTo("FunctionProvider"));
        foreach (var importRow in importedMethodRows)
        {
            var importedMethodRow = checked((int)(
                forwarderTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow)
                    & 0x00FF_FFFF));
            Assert.That(forwarderTables.Contracts.GetEffectsMask(
                    importedMethodRow),
                Is.EqualTo(4));
            Assert.That(forwarderTables.Contracts.GetDependencyContracts(
                    importedMethodRow), Has.Length.EqualTo(1));
        }

        var forwarderInput = new TrRmStandardArtifactInput(schemaIdentity,
            forwarderArtifact);
        var forwarderReference
            = TrRmExecutableCompiler.CreateStandardReference(forwarderInput,
                "FunctionForwarder", [providerInput]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), forwarderReference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "FunctionForwarderReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(new TrRmArtifactIndex(
                reemitted,
                TrRmArtifactSchema.C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft));
        var reemittedMethodImports = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Where(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x16)
            .ToArray();
        Assert.That(reemittedMethodImports, Has.Length.EqualTo(2));
        Assert.That(reemittedMethodImports.Select(row => reemittedTables
                .Imports.GetBindingForImportRow(row).ExportKey),
            Is.EquivalentTo(["Echo", "EchoAlias"]));
        Assert.That(reemittedMethodImports.Select(row =>
                reemittedTables.ModuleRefs.GetProducerIdentity(
                    reemittedTables.Imports.GetModuleRefForImportRow(row))),
            Is.All.EqualTo("FunctionProvider"));
        foreach (var importRow in reemittedMethodImports)
        {
            var importedMethodRow = checked((int)(
                reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow)
                    & 0x00FF_FFFF));
            var exportKey = reemittedTables.Imports
                .GetBindingForImportRow(importRow).ExportKey;
            var sourceImportRow = importedMethodRows.Single(row =>
                forwarderTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == exportKey);
            var sourceMethodRow = checked((int)(
                forwarderTables.Imports
                    .GetExpectedDefinitionForImportRow(sourceImportRow)
                    & 0x00FF_FFFF));
            var hasSourceLabel = forwarderTables.MethodParameterLabels
                .TryGetNameStringRow(sourceMethodRow, 0,
                    out var sourceLabelRow);
            var hasReemittedLabel = reemittedTables.MethodParameterLabels
                .TryGetNameStringRow(importedMethodRow, 0,
                    out var reemittedLabelRow);
            Assert.That(hasReemittedLabel, Is.EqualTo(hasSourceLabel),
                exportKey);
            if (hasSourceLabel)
            {
                Assert.That(reemittedTables.Strings.DecodeName(
                        reemittedLabelRow),
                    Is.EqualTo(forwarderTables.Strings.DecodeName(
                        sourceLabelRow)), exportKey);
            }

            var hasSourceDefault = forwarderTables.MethodParameterLabels
                .TryGetDefaultConstantSourceRow(sourceMethodRow, 0,
                    out var sourceDefaultRow);
            var hasReemittedDefault = reemittedTables.MethodParameterLabels
                .TryGetDefaultConstantSourceRow(importedMethodRow, 0,
                    out var reemittedDefaultRow);
            Assert.That(hasReemittedDefault,
                Is.EqualTo(hasSourceDefault), exportKey);
            if (hasSourceDefault)
            {
                Assert.That(sourceDefaultRow, Is.Zero, exportKey);
                Assert.That(reemittedDefaultRow, Is.Zero, exportKey);
            }
            Assert.That(forwarderTables.MethodParameterLabels
                    .TryGetNameStringRow(sourceMethodRow, 1,
                        out var sourceCountLabel),
                Is.True, exportKey);
            Assert.That(reemittedTables.MethodParameterLabels
                    .TryGetNameStringRow(importedMethodRow, 1,
                        out var reemittedCountLabel),
                Is.True, exportKey);
            Assert.That(reemittedTables.Strings.DecodeName(
                    reemittedCountLabel),
                Is.EqualTo(forwarderTables.Strings.DecodeName(
                    sourceCountLabel)), exportKey);
            var hasSourceCountDefault = forwarderTables
                .MethodParameterLabels.TryGetDefaultConstantSourceRow(
                    sourceMethodRow, 1, out var sourceCountDefaultRow);
            var hasReemittedCountDefault = reemittedTables
                .MethodParameterLabels.TryGetDefaultConstantSourceRow(
                    importedMethodRow, 1,
                    out var reemittedCountDefaultRow);
            Assert.That(hasSourceCountDefault, Is.True, exportKey);
            Assert.That(hasReemittedCountDefault, Is.True, exportKey);
            var sourceCountDefaultNodeRow = checked((int)
                forwarderTables.ConstantSources.GetPayloadReference(
                    checked((int)sourceCountDefaultRow)));
            var reemittedCountDefaultNodeRow = checked((int)
                reemittedTables.ConstantSources.GetPayloadReference(
                    checked((int)reemittedCountDefaultRow)));
            Assert.That(reemittedTables.FrozenValueNodes.GetScalarPayload(
                    reemittedCountDefaultNodeRow, reemittedTables.Blobs)
                    .ToArray(),
                Is.EqualTo(forwarderTables.FrozenValueNodes
                    .GetScalarPayload(sourceCountDefaultNodeRow,
                        forwarderTables.Blobs).ToArray()), exportKey);
            Assert.That(reemittedTables.Contracts.GetEffectsMask(
                    importedMethodRow),
                Is.EqualTo(4));
            Assert.That(reemittedTables.Contracts.GetDependencyContracts(
                    importedMethodRow), Has.Length.EqualTo(1));
        }
        var payloadTypeImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row) >> 24 == 0x13
                && reemittedTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "PayloadKey");
        var payloadTypeDefRow = checked((int)(
            reemittedTables.Imports.GetExpectedDefinitionForImportRow(
                payloadTypeImportRow) & 0x00FF_FFFF));
        foreach (var importRow in reemittedMethodImports)
        {
            var importedMethodRow = checked((int)(
                reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(importRow)
                    & 0x00FF_FFFF));
            var signatureRow = reemittedTables.Methods.GetSignature(
                importedMethodRow);
            var (parameterStart, parameterCount) = reemittedTables.Signatures
                .GetParamRange(signatureRow);
            Assert.That(parameterCount, Is.EqualTo(2));
            Assert.That(reemittedTables.TypeSpecs.GetNominalTypeDefRow(
                    reemittedTables.Parameters.GetTypeSpec(parameterStart),
                    reemittedTables.TypeDefs, reemittedTables.Imports),
                Is.EqualTo(payloadTypeDefRow));
        }

        foreach (var (functionKey, importedKey) in new[] {
            ("Forward", "Echo"),
            ("ForwardAlias", "EchoAlias"),
        })
        {
            Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
                functionKey, out var functionDefinition), Is.True);
            var functionMethodRow = checked((int)(
                functionDefinition & 0x00FF_FFFF));
            Assert.That(forwarderTables.Exports.TryGetDefinitionForKey(1,
                functionKey, out var sourceFunctionDefinition), Is.True);
            var sourceFunctionMethodRow = checked((int)(
                sourceFunctionDefinition & 0x00FF_FFFF));
            Assert.That(forwarderTables.MethodParameterLabels
                    .TryGetNameStringRow(sourceFunctionMethodRow, 0,
                        out var sourceFunctionLabelRow),
                Is.True, functionKey);
            Assert.That(reemittedTables.MethodParameterLabels
                    .TryGetNameStringRow(functionMethodRow, 0,
                        out var reemittedFunctionLabelRow),
                Is.True, functionKey);
            Assert.That(reemittedTables.Strings.DecodeName(
                    reemittedFunctionLabelRow),
                Is.EqualTo(forwarderTables.Strings.DecodeName(
                    sourceFunctionLabelRow)), functionKey);
            Assert.That(forwarderTables.MethodParameterLabels
                    .TryGetDefaultConstantSourceRow(sourceFunctionMethodRow,
                        0, out var sourceFunctionDefaultRow),
                Is.True, functionKey);
            Assert.That(sourceFunctionDefaultRow, Is.Zero, functionKey);
            Assert.That(reemittedTables.MethodParameterLabels
                    .TryGetDefaultConstantSourceRow(functionMethodRow, 0,
                        out var reemittedFunctionDefaultRow),
                Is.True, functionKey);
            Assert.That(reemittedFunctionDefaultRow, Is.Zero, functionKey);
            Assert.That(reemittedTables.MethodParameterLabels
                    .TryGetNameStringRow(functionMethodRow, 1,
                        out var reemittedCountLabel),
                Is.True, functionKey);
            Assert.That(reemittedTables.Strings.DecodeName(
                    reemittedCountLabel),
                Is.EqualTo("count"), functionKey);
            Assert.That(reemittedTables.MethodParameterLabels
                    .TryGetDefaultConstantSourceRow(functionMethodRow, 1,
                        out var reemittedCountDefault),
                Is.True, functionKey);
            Assert.That(reemittedCountDefault, Is.Zero, functionKey);
            Assert.That(reemittedTables.Contracts.GetDependencyContracts(
                    functionMethodRow),
                Has.Length.EqualTo(1), functionKey);
            Assert.That(reemittedTables.Contracts.GetEffectsMask(
                    functionMethodRow),
                Is.EqualTo(4), functionKey);
            var signatureRow = reemittedTables.Methods.GetSignature(
                functionMethodRow);
            var (parameterStart, parameterCount) = reemittedTables.Signatures
                .GetParamRange(signatureRow);
            Assert.That(parameterCount, Is.EqualTo(2));
            Assert.That(reemittedTables.TypeSpecs.GetNominalTypeDefRow(
                    reemittedTables.Parameters.GetTypeSpec(parameterStart),
                    reemittedTables.TypeDefs, reemittedTables.Imports),
                Is.EqualTo(payloadTypeDefRow));
            var bodyRow = reemittedTables.Methods
                .GetBodyRow(functionMethodRow);
            var body = reemittedTables.Bodies.GetRegisterBody(bodyRow,
                reemittedTables.Contracts.Count);
            Assert.That(body.MethodMap.Length, Is.EqualTo(sizeof(uint)));
            var targetDefinition
                = BinaryPrimitives.ReadUInt32LittleEndian(body.MethodMap);
            Assert.That(reemittedMethodImports.Any(importRow =>
                    reemittedTables.Imports
                        .GetBindingForImportRow(importRow).ExportKey
                            == importedKey
                    && reemittedTables.Imports
                        .GetExpectedDefinitionForImportRow(importRow)
                            == targetDefinition),
                Is.True, functionKey);
        }

        var useTree = SyntaxTree.Parse("""
            namespace Demo {
                public int32 Use(Payload value, int32 count) {
                    unsafe(Forward(value, count));
                    return unsafe(ForwardAlias(value, count));
                }
            }
            """);
        var useCompilation = Compilation.CreateLibrary(useTree,
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "FunctionForwarderReemitted", [providerInput]));
        Assert.That(useCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, useCompilation.Diagnostics));

        var incompatibleProviderTree = SyntaxTree.Parse("""
            namespace Demo {
                public class Payload {
                    public int32 Value;
                    public Payload(int32 value) { Value = value; }
                }
                [return: DependsOn(nameof(value))]
                public int32 Echo(int32 value) { return value; }
            }
            """);
        var incompatibleProviderCompilation
            = Compilation.CreateLibrary(incompatibleProviderTree);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderProgram
            = (BoundProgram)incompatibleProviderCompilation.Program!;
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderProgram, "FunctionProvider",
                schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], CreateClassExports(incompatibleProviderProgram,
                    incompatibleProviderTree));
        incompatibleProviderArtifact = AddMethodExportAlias(
            incompatibleProviderArtifact, "Echo", "EchoAlias",
            TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsInitializerCopyEvidenceDraft);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "FunctionForwarderReemitted", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleProviderArtifact),
                ]));
    }

    [Test]
    public void BindsImportedImplicitReferenceMethodsAfterReemission()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var expectedArtifact = EmitClassMethodReemissionArtifact(
            "MethodExpected", 7, includeImplicitRefMethod: true);
        var providerArtifact = EmitClassMethodReemissionArtifact(
            "MethodProvider", 9, includeImplicitRefMethod: true);
        var consumerArtifact = CreateExpectedClassMethodImportArtifact(
            expectedArtifact, providerArtifact, "MethodConsumer",
            "MethodProvider", ["Box.ObserveImplicit"],
            rootClassExportKey: "ContainerKey");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, consumerArtifact),
            "MethodConsumer", [providerInput], ["ContainerKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));

        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "MethodConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var importedMethodRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey
                == "Box.ObserveImplicit");
        var expectedMethodDefinition = reemittedTables.Imports
            .GetExpectedDefinitionForImportRow(importedMethodRow);
        var expectedMethodRow = checked((int)(
            expectedMethodDefinition & 0x00FF_FFFF));
        var expectedSignature = reemittedTables.Methods
            .GetSignature(expectedMethodRow);
        var (parameterStart, parameterCount)
            = reemittedTables.Signatures.GetParamRange(expectedSignature);
        Assert.That(parameterCount, Is.EqualTo(1));
        Assert.That(reemittedTables.Parameters.GetMode(parameterStart),
            Is.Zero);
        Assert.That(reemittedTables.Parameters.GetFlags(parameterStart),
            Is.EqualTo(1));
        var parameterType = reemittedTables.TypeSpecs.GetTypeSpecRef(
            reemittedTables.Parameters.GetTypeSpec(parameterStart));
        Assert.That(parameterType.Form, Is.EqualTo((byte)0x07));
        Assert.That(parameterType.Qualifiers, Is.Zero);

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "MethodConsumerReemitted", [providerInput],
                ["ContainerKey"]);
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public void Forward(Box target, int32 value) {
                    target.ObserveImplicit(value);
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));
    }

    [Test]
    public void ReemitsLocalClassFieldReadBodiesWithoutSource()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var providerArtifact
            = EmitScalarBodyfulClassArtifact("BodyProvider",
                readsField: true, includePrefix: true,
                includeMethodCall: true, includeConstant: true);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "BodyProvider", [], ["BoxKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));

        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "BodyProviderReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        providerArtifact));
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var (providerMethodStart, providerMethodEnd)
            = providerTables.Modules.GetMethodDefRange(1);
        var providerMethodRow = Enumerable.Range(providerMethodStart,
                providerMethodEnd - providerMethodStart)
            .Single(row => providerTables.Methods.GetName(row) == "Read");
        var (reemittedMethodStart, reemittedMethodEnd)
            = reemittedTables.Modules.GetMethodDefRange(1);
        var reemittedMethodRow = Enumerable.Range(reemittedMethodStart,
                reemittedMethodEnd - reemittedMethodStart)
            .Single(row => reemittedTables.Methods.GetName(row) == "Read");
        var providerCallMethodRow = Enumerable.Range(providerMethodStart,
                providerMethodEnd - providerMethodStart)
            .Single(row => providerTables.Methods.GetName(row) == "CallRead");
        var reemittedCallMethodRow = Enumerable.Range(reemittedMethodStart,
                reemittedMethodEnd - reemittedMethodStart)
            .Single(row => reemittedTables.Methods.GetName(row) == "CallRead");
        var providerPrivateMethodRow = Enumerable.Range(providerMethodStart,
                providerMethodEnd - providerMethodStart)
            .Single(row => providerTables.Methods.GetName(row)
                == "ReadPrivate");
        var reemittedPrivateMethodRow = Enumerable.Range(
                reemittedMethodStart, reemittedMethodEnd - reemittedMethodStart)
            .Single(row => reemittedTables.Methods.GetName(row)
                == "ReadPrivate");
        var providerConstantMethodRow = Enumerable.Range(providerMethodStart,
                providerMethodEnd - providerMethodStart)
            .Single(row => providerTables.Methods.GetName(row)
                == "ReadConstant");
        var reemittedConstantMethodRow = Enumerable.Range(
                reemittedMethodStart, reemittedMethodEnd - reemittedMethodStart)
            .Single(row => reemittedTables.Methods.GetName(row)
                == "ReadConstant");
        var providerBodyRow
            = providerTables.Methods.GetBodyRow(providerMethodRow);
        var reemittedBodyRow
            = reemittedTables.Methods.GetBodyRow(reemittedMethodRow);
        Assert.That(providerBodyRow, Is.GreaterThan(0));
        Assert.That(reemittedBodyRow, Is.GreaterThan(0));
        var oldBoxDefinition = providerTables.Exports.GetExportsForModule(1)
            .Single(static export => export.ExportKey == "BoxKey").Definition;
        var newBoxDefinition = reemittedTables.Exports.GetExportsForModule(1)
            .Single(static export => export.ExportKey == "BoxKey").Definition;
        var oldBoxTypeDefRow = checked((int)(
            oldBoxDefinition & 0x00FF_FFFF));
        var newBoxTypeDefRow = checked((int)(
            newBoxDefinition & 0x00FF_FFFF));
        Assert.That(oldBoxTypeDefRow, Is.Not.EqualTo(newBoxTypeDefRow));
        var providerBody = providerTables.Bodies.GetRegisterBody(
            providerBodyRow, providerTables.Contracts.Count);
        var reemittedBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedBodyRow, reemittedTables.Contracts.Count);
        Assert.That(providerBody.FieldCount, Is.EqualTo(1));
        Assert.That(reemittedBody.FieldCount, Is.EqualTo(1));
        var providerFieldMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(
                providerBody.FieldMap);
        var reemittedFieldMapToken
            = BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedBody.FieldMap);
        Assert.That(providerFieldMapToken >> 24, Is.EqualTo(0x14u));
        Assert.That(reemittedFieldMapToken >> 24, Is.EqualTo(0x14u));
        var oldFieldRow = checked((int)(
            providerFieldMapToken & 0x00FF_FFFF));
        var newFieldRow = checked((int)(
            reemittedFieldMapToken & 0x00FF_FFFF));
        Assert.That(oldFieldRow, Is.Not.EqualTo(newFieldRow));
        Assert.That(providerTables.Fields.GetOwnerTypeDef(oldFieldRow),
            Is.EqualTo(oldBoxTypeDefRow));
        Assert.That(reemittedTables.Fields.GetOwnerTypeDef(newFieldRow),
            Is.EqualTo(newBoxTypeDefRow));
        var providerClassTypeMapToken = Enumerable.Range(0,
                providerBody.TypeCount)
            .Select(index => BinaryPrimitives.ReadUInt32LittleEndian(
                providerBody.TypeMap.Slice(index * sizeof(uint))))
            .Single(token => (byte)(token >> 24) == 0x11);
        var reemittedClassTypeMapToken = Enumerable.Range(0,
                reemittedBody.TypeCount)
            .Select(index => BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedBody.TypeMap.Slice(index * sizeof(uint))))
            .Single(token => (byte)(token >> 24) == 0x11);
        Assert.That(providerTables.TypeSpecs.GetNominalTypeDefRow(
                providerClassTypeMapToken & 0x00FF_FFFF,
                providerTables.TypeDefs, providerTables.Imports),
            Is.EqualTo(oldBoxTypeDefRow));
        Assert.That(reemittedTables.TypeSpecs.GetNominalTypeDefRow(
                reemittedClassTypeMapToken & 0x00FF_FFFF,
                reemittedTables.TypeDefs, reemittedTables.Imports),
            Is.EqualTo(newBoxTypeDefRow));
        Assert.That(reemittedBody.Code.ToArray(),
            Is.EqualTo(providerBody.Code.ToArray()));
        Assert.That(providerTables.Methods.GetFlags(providerPrivateMethodRow)
                & 3u,
            Is.Zero);
        Assert.That(reemittedTables.Methods.GetFlags(
                reemittedPrivateMethodRow) & 3u, Is.Zero);
        Assert.That(providerTables.Methods.GetBodyRow(
                providerPrivateMethodRow), Is.GreaterThan(0));
        Assert.That(reemittedTables.Methods.GetBodyRow(
                reemittedPrivateMethodRow), Is.GreaterThan(0));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Box.ReadPrivate", out _), Is.False);
        var providerCallBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerCallMethodRow),
            providerTables.Contracts.Count);
        var reemittedCallBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedTables.Methods.GetBodyRow(reemittedCallMethodRow),
            reemittedTables.Contracts.Count);
        Assert.That(providerCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(reemittedCallBody.MethodCount, Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                providerCallBody.MethodMap),
            Is.EqualTo(0x1600_0000u
                | checked((uint)providerPrivateMethodRow)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedCallBody.MethodMap),
            Is.EqualTo(0x1600_0000u
                | checked((uint)reemittedPrivateMethodRow)));
        var providerConstantBody = providerTables.Bodies.GetRegisterBody(
            providerTables.Methods.GetBodyRow(providerConstantMethodRow),
            providerTables.Contracts.Count);
        var reemittedConstantBody = reemittedTables.Bodies.GetRegisterBody(
            reemittedTables.Methods.GetBodyRow(reemittedConstantMethodRow),
            reemittedTables.Contracts.Count);
        Assert.That(providerConstantBody.ConstantCount, Is.EqualTo(1));
        Assert.That(reemittedConstantBody.ConstantCount, Is.EqualTo(1));
        var providerConstantTypeSpec
            = BinaryPrimitives.ReadUInt32LittleEndian(
                providerConstantBody.ConstantMap);
        var reemittedConstantTypeSpec
            = BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedConstantBody.ConstantMap);
        var providerConstantSourceRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                providerConstantBody.ConstantMap.Slice(sizeof(uint))));
        var reemittedConstantSourceRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedConstantBody.ConstantMap.Slice(sizeof(uint))));
        Assert.That(providerTables.ConstantSources.GetKind(
            providerConstantSourceRow), Is.EqualTo(1u));
        Assert.That(reemittedTables.ConstantSources.GetKind(
            reemittedConstantSourceRow), Is.EqualTo(1u));
        var providerConstantNodeRow = checked((int)providerTables
            .ConstantSources.GetPayloadReference(providerConstantSourceRow));
        var reemittedConstantNodeRow = checked((int)reemittedTables
            .ConstantSources.GetPayloadReference(reemittedConstantSourceRow));
        Assert.That(reemittedTables.FrozenValueNodes.GetScalarPayload(
                reemittedConstantNodeRow, reemittedTables.Blobs).ToArray(),
            Is.EqualTo(providerTables.FrozenValueNodes.GetScalarPayload(
                providerConstantNodeRow, providerTables.Blobs).ToArray()));
        Assert.That(reemittedConstantTypeSpec,
            Is.EqualTo(providerConstantTypeSpec));
        Assert.That(reemittedTables.Exports.TryGetDefinitionForKey(1,
            "Box.Read", out var reemittedMethodDefinition), Is.True);
        Assert.That(reemittedMethodDefinition,
            Is.EqualTo(0x1600_0000u | checked((uint)reemittedMethodRow)));

        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "BodyProviderReemitted", [], ["BoxKey"]);
        var reemittedMethod = reemittedReference
            .StandardClassDeclarations.Single()
            .Declaration.Methods.Single(static method =>
                method.Name == "Read");
        Assert.That(reemittedMethod.BodyRow, Is.GreaterThan(0));
    }

    [Test]
    public void ReemitsCompactClassFieldAccessBodiesWithoutSource()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public mut class Box {
                public mut int32 Value;
                public int32 ReadValue() {
                    return this.Value;
                }
                public mut void SetValue(int32 value) {
                    this.Value = value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ReadValue"));
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "CompactFieldAccess", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, boxClassIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            field.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            read.Span, "Box.ReadValue"),
                    ]),
            ]);
        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var artifactFieldDefinition = artifactTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Value").Definition;
        var artifactReadDefinition = artifactTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.ReadValue").Definition;
        var artifactReadBody = artifactTables.Bodies.GetRow(
            artifactTables.Methods.GetBodyRow(checked((int)(
                artifactReadDefinition & 0x00FF_FFFF))));
        Assert.That(artifactReadBody[0], Is.EqualTo((byte)0x02));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                artifactReadBody[4..]),
            Is.EqualTo(artifactFieldDefinition));
        var artifactSetMethodRow = Enumerable.Range(1,
                artifactTables.Methods.Count)
            .Single(row => artifactTables.Methods.GetName(row)
                == "SetValue");
        Assert.That(artifactTables.Exports.TryGetDefinitionForKey(1,
            "Box.SetValue", out _), Is.False);
        var artifactSetBody = artifactTables.Bodies.GetRow(
            artifactTables.Methods.GetBodyRow(artifactSetMethodRow));
        Assert.That(artifactSetBody[0], Is.EqualTo((byte)0x03));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                artifactSetBody[4..]),
            Is.EqualTo(artifactFieldDefinition));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            artifactInput, "CompactFieldAccess", [], ["BoxKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "CompactFieldAccessReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var reemittedFieldDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Value").Definition;
        var reemittedReadDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.ReadValue").Definition;
        var reemittedReadBody = reemittedTables.Bodies.GetRow(
            reemittedTables.Methods.GetBodyRow(checked((int)(
                reemittedReadDefinition & 0x00FF_FFFF))));
        Assert.That(reemittedReadBody[0], Is.EqualTo((byte)0x02));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedReadBody[4..]),
            Is.EqualTo(reemittedFieldDefinition));
        var reemittedSetMethodRow = Enumerable.Range(1,
                reemittedTables.Methods.Count)
            .Single(row => reemittedTables.Methods.GetName(row)
                == "SetValue");
        var reemittedSetBody = reemittedTables.Bodies.GetRow(
            reemittedTables.Methods.GetBodyRow(reemittedSetMethodRow));
        Assert.That(reemittedSetBody[0], Is.EqualTo((byte)0x03));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedSetBody[4..]),
            Is.EqualTo(reemittedFieldDefinition));
        Assert.That(reemittedSetBody[8], Is.Zero);
        Assert.That(reemittedSetBody[10], Is.EqualTo((byte)1));
        Assert.That(reemittedSetBody[11], Is.Zero);

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "CompactFieldAccessReemitted", [], ["BoxKey"]);
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Read(Box value) {
                    return value.ReadValue();
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));
    }

    [Test]
    public void ReemitsFieldReferenceBodiesAndRemapsDependencyRows()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Value;
                [return: DependsOn(this)]
                public unsafe ref<int32> ReadValue() {
                    return ref this.Value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "ReadValue"));
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var sourceArtifact = TrRmExecutableCompiler.EmitStandard(program,
            "FieldReferenceProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, boxClassIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            field.Span, "Box.Value"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            read.Span, "Box.ReadValue"),
                    ]),
            ]);
        var artifact = ReorderFieldReferenceContractRows(sourceArtifact);
        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var methodDefinition = artifactTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.ReadValue").Definition;
        var methodRow = checked((int)(
            methodDefinition & 0x00FF_FFFF));
        var body = artifactTables.Bodies.GetRow(
            artifactTables.Methods.GetBodyRow(methodRow));
        Assert.That(body[0], Is.EqualTo((byte)0x04));
        var dependency = artifactTables.Contracts
            .GetDependencyContracts(methodRow).Single();
        Assert.That(dependency, Is.EqualTo(new TrRmDependencyContract(
            1, 2, 3, 1, 0, 0)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(8)),
            Is.EqualTo(1u));
        Assert.That(artifactTables.Contracts.GetEffectsMask(methodRow),
            Is.EqualTo(4u));

        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "FieldReferenceProvider", [], ["BoxKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "FieldReferenceProviderReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var reemittedMethodDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.ReadValue").Definition;
        var reemittedMethodRow = checked((int)(
            reemittedMethodDefinition & 0x00FF_FFFF));
        var reemittedBody = reemittedTables.Bodies.GetRow(
            reemittedTables.Methods.GetBodyRow(reemittedMethodRow));
        Assert.That(reemittedBody[0], Is.EqualTo((byte)0x04));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedBody.Slice(8)),
            Is.EqualTo(2u));
        var remappedDependency = reemittedTables.Contracts
            .GetDependencyContractRows(reemittedMethodRow).Single();
        Assert.That(remappedDependency.Row, Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedBody.Slice(8)),
            Is.EqualTo(checked((uint)remappedDependency.Row)));

        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "FieldReferenceProviderReemitted", [], ["BoxKey"]);
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public unsafe int32 Read(Box value) {
                    return *value.ReadValue();
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));

        static byte[] ReorderFieldReferenceContractRows(byte[] artifact)
        {
            var schema
                = TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
            var index = TrRmArtifactIndex
                .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                    artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(index);
            var methodDefinition = tables.Exports
                .GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "Box.ReadValue").Definition;
            var methodRow = checked((int)(
                methodDefinition & 0x00FF_FFFF));
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            var body = tables.Bodies.GetRow(bodyRow).ToArray();
            var dependencyRow = tables.Contracts
                .GetDependencyContractRows(methodRow).Single().Row;
            var (contractFirst, contractCount)
                = tables.Methods.GetContractRange(methodRow);
            if (body.Length != 12 || body[0] != 0x04
                || contractFirst != 1 || contractCount != 2
                || dependencyRow != 2
                || tables.Contracts.GetEffectsMask(methodRow) != 4)
            {
                throw new InvalidDataException(
                    "The field-reference fixture requires a Body-04 getter with Effects and Dependency clauses.");
            }

            if (!index.TryGetSection(0x001B, out var contractSection,
                    out var contractRecordCount)
                || contractRecordCount != 2)
            {
                throw new InvalidDataException(
                    "The field-reference fixture requires two Contract rows.");
            }

            var contractIndex = new TrRmVariableTableIndex(
                contractSection, checked((int)contractRecordCount), 8, 1);
            var effectRow = Enumerable.Range(1,
                    checked((int)contractRecordCount))
                .Single(row => row != dependencyRow);
            var reorderedContracts = TrRmVariableTableWriter.EncodeBlobs([
                contractIndex.GetRow(dependencyRow).ToArray(),
                contractIndex.GetRow(effectRow).ToArray(),
            ]);
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), 1);

            var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
            for (var row = 1; row <= bodyRows.Length; row++)
            {
                bodyRows[row - 1] = row == bodyRow
                    ? body
                    : tables.Bodies.GetRow(row).ToArray();
            }
            var bodyBytes = TrRmVariableTableWriter.EncodeBodies(bodyRows);

            var sections = new List<TrRmSection>(index.SectionCount);
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount; sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var section, out var recordCount);
                sections.Add(kind switch
                {
                    0x001B => new TrRmSection(kind, flags,
                        contractRecordCount, reorderedContracts),
                    0x0020 => new TrRmSection(kind, flags, recordCount,
                        bodyBytes),
                    _ => new TrRmSection(kind, flags, recordCount,
                        section.ToArray()),
                });
            }
            sections.Sort(static (left, right) =>
                left.Kind.CompareTo(right.Kind));
            return TrRmArtifactWriter.Encode(sections, schema);
        }
    }

    [Test]
    public void EmitsAndReemitsImportedClassFieldReferenceBodies()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var providerArtifact = EmitLeafProvider("int32");
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "FieldReferenceLeafProvider", [], ["LeafKey"],
            ["Leaf.Value"]);
        var tree = SyntaxTree.Parse("""
            public class Box {
                [return: DependsOn(nameof(value))]
                public unsafe ref<int32> Read(Leaf value) {
                    return ref value.Value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree, leafReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var read = classDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Read"));
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FieldReferenceBox", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, boxClassIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            read.Span, "Box.Read"),
                    ]),
            ]);
        var artifactInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var artifactTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var methodDefinition = artifactTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Read").Definition;
        var methodRow = checked((int)(
            methodDefinition & 0x00FF_FFFF));
        var body = artifactTables.Bodies.GetRow(
            artifactTables.Methods.GetBodyRow(methodRow));
        Assert.That(body[0], Is.EqualTo((byte)0x04));
        var fieldImportRow = Enumerable.Range(1,
                artifactTables.Imports.Count)
            .Single(row =>
                artifactTables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14
                && artifactTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "Leaf.Value");
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4)),
            Is.EqualTo(0x1C00_0000u | checked((uint)fieldImportRow)));
        Assert.That(artifactTables.Contracts
                .GetDependencyContracts(methodRow).Single(),
            Is.EqualTo(new TrRmDependencyContract(1, 1, 3, 1, 0, 0)));
        Assert.That(artifactTables.Contracts.GetEffectsMask(methodRow),
            Is.EqualTo(4u));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            artifactInput, "FieldReferenceBox", [providerInput], ["BoxKey"],
            ["Leaf.Value"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "FieldReferenceBoxReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var reemittedMethodDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Read").Definition;
        var reemittedMethodRow = checked((int)(
            reemittedMethodDefinition & 0x00FF_FFFF));
        var reemittedBody = reemittedTables.Bodies.GetRow(
            reemittedTables.Methods.GetBodyRow(reemittedMethodRow));
        var reemittedFieldImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row =>
                reemittedTables.Imports
                    .GetExpectedDefinitionForImportRow(row) >> 24 == 0x14
                && reemittedTables.Imports.GetBindingForImportRow(row)
                    .ExportKey == "Leaf.Value");
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedBody.Slice(4)),
            Is.EqualTo(0x1C00_0000u
                | checked((uint)reemittedFieldImportRow)));
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "FieldReferenceBoxReemitted", [providerInput], ["BoxKey"],
                ["Leaf.Value"]);
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public unsafe int32 Use(Box box, Leaf value) {
                    return *box.Read(value);
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));

        var incompatibleArtifact = EmitLeafProvider("int64");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                artifactInput, "FieldReferenceBox", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        incompatibleArtifact),
                ], ["BoxKey"], ["Leaf.Value"]));

        byte[] EmitLeafProvider(string fieldType)
        {
            var providerTree = SyntaxTree.Parse($$"""
                public class Leaf {
                    public {{fieldType}} Value;
                }
                """);
            var providerCompilation
                = Compilation.CreateLibrary(providerTree);
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerProgram = (BoundProgram)providerCompilation.Program!;
            var leafClassDeclaration
                = providerTree.Root.Children.OfType<SyntaxNode>()
                    .Single(static node =>
                        node.Kind == SyntaxKind.ClassDeclaration);
            var field = leafClassDeclaration.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration);
            var leafClassIndex = Enumerable.Range(0,
                    providerProgram.Classes.Length)
                .Single(index => providerProgram.Classes[index].Name
                    == "Leaf");
            return TrRmExecutableCompiler.EmitStandard(providerProgram,
                "FieldReferenceLeafProvider", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], [
                    new TrRmSourceClassExport(providerProgram,
                        leafClassIndex, leafClassDeclaration.Span, "LeafKey",
                        [
                            new TrRmSourceClassMemberExport(
                                leafClassIndex, field.Span, "Leaf.Value"),
                        ]),
                ]);
        }
    }

    [Test]
    public void EmitsLocalClassFieldReferenceBodiesFromFormalBacking()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Leaf {
                public int32 Value;
            }
            public class Box {
                [return: DependsOn(nameof(value))]
                public static unsafe ref<int32> Read(Leaf value) {
                    return ref value.Value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node =>
                node.Kind == SyntaxKind.ClassDeclaration).ToArray();
        var leafDeclaration = classDeclarations.Single(static node =>
            node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Leaf"));
        var boxDeclaration = classDeclarations.Single(static node =>
            node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Box"));
        var leafField = leafDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FieldDeclaration);
        var read = boxDeclaration.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Read"));
        var leafClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Leaf");
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "LocalFieldReferenceProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, leafClassIndex,
                    leafDeclaration.Span, "LeafKey", [
                        new TrRmSourceClassMemberExport(leafClassIndex,
                            leafField.Span, "Leaf.Value"),
                    ]),
                new TrRmSourceClassExport(program, boxClassIndex,
                    boxDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            read.Span, "Box.Read"),
                    ]),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var methodDefinition = tables.Exports.GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Read").Definition;
        var methodRow = checked((int)(
            methodDefinition & 0x00FF_FFFF));
        var body = tables.Bodies.GetRow(
            tables.Methods.GetBodyRow(methodRow));
        var fieldDefinition = tables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Leaf.Value").Definition;
        Assert.That(body[0], Is.EqualTo((byte)0x04));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(4)),
            Is.EqualTo(fieldDefinition));
        Assert.That(tables.Contracts.GetDependencyContracts(methodRow)
                .Single(),
            Is.EqualTo(new TrRmDependencyContract(1, 1, 3, 1, 0, 0)));
        Assert.That(tables.Contracts.GetEffectsMask(methodRow),
            Is.EqualTo(4u));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            "LocalFieldReferenceProvider", [], ["BoxKey", "LeafKey"]);
        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "LocalFieldReferenceProviderReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemitted));
        var reemittedMethodDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Read").Definition;
        var reemittedMethodRow = checked((int)(
            reemittedMethodDefinition & 0x00FF_FFFF));
        var reemittedBody = reemittedTables.Bodies.GetRow(
            reemittedTables.Methods.GetBodyRow(reemittedMethodRow));
        var reemittedFieldDefinition = reemittedTables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Leaf.Value").Definition;
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
                reemittedBody.Slice(4)),
            Is.EqualTo(reemittedFieldDefinition));
        var reloadedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                "LocalFieldReferenceProviderReemitted", [],
                ["BoxKey", "LeafKey"]);
        var callerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public unsafe int32 Use(Leaf value) {
                    return *Box.Read(value);
                }
                """), reloadedReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));
    }

    [Test]
    public void RejectsFieldReferenceBodiesWithoutAnOwnedDependency()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 First;
                public int32 Second;
                [return: DependsOn(this)]
                public unsafe ref<int32> ReadFirst() {
                    return ref this.First;
                }
                [return: DependsOn(this)]
                public unsafe ref<int32> ReadSecond() {
                    return ref this.Second;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var fields = members.Where(static node =>
            node.Kind == SyntaxKind.FieldDeclaration).ToArray();
        var methods = members.Where(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration).ToArray();
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "FieldReferenceOwnerValidation", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, boxClassIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            fields[0].Span, "Box.First"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            fields[1].Span, "Box.Second"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            methods[0].Span, "Box.ReadFirst"),
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            methods[1].Span, "Box.ReadSecond"),
                    ]),
            ]);
        var schema
            = TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        artifact));
        var firstMethodRow = GetMethodRow(tables, "Box.ReadFirst");
        var secondMethodRow = GetMethodRow(tables, "Box.ReadSecond");
        var wrongOwnerDependencyRow = tables.Contracts
            .GetDependencyContractRows(secondMethodRow).Single().Row;

        AssertInvalid(RewriteBodyDependencyRow(artifact,
            firstMethodRow, 0), firstMethodRow);
        AssertInvalid(RewriteBodyDependencyRow(artifact,
            firstMethodRow, checked((uint)wrongOwnerDependencyRow)),
            firstMethodRow);

        static int GetMethodRow(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string exportKey)
            => checked((int)(tables.Exports
                .GetExportsForModule(1)
                .Single(export => export.ExportKey == exportKey)
                .Definition & 0x00FF_FFFF));

        byte[] RewriteBodyDependencyRow(
            byte[] artifact, int methodRow, uint dependencyRow)
        {
            var artifactIndex = TrRmArtifactIndex
                .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                    artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(artifactIndex);
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            var body = tables.Bodies.GetRow(bodyRow).ToArray();
            if (body.Length != 12 || body[0] != 0x04)
            {
                throw new InvalidDataException(
                    "The field-reference owner fixture requires a Body-04 method.");
            }

            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8),
                dependencyRow);
            var bodyRows = new ReadOnlyMemory<byte>[tables.Bodies.Count];
            for (var row = 1; row <= bodyRows.Length; row++)
            {
                bodyRows[row - 1] = row == bodyRow
                    ? body
                    : tables.Bodies.GetRow(row).ToArray();
            }

            var bodyBytes = TrRmVariableTableWriter.EncodeBodies(bodyRows);
            var sections = new List<TrRmSection>(artifactIndex.SectionCount);
            for (var sectionIndex = 0;
                sectionIndex < artifactIndex.SectionCount; sectionIndex++)
            {
                artifactIndex.GetSection(sectionIndex, out var kind,
                    out var flags, out var section, out var recordCount);
                sections.Add(kind == 0x0020
                    ? new TrRmSection(kind, flags, recordCount, bodyBytes)
                    : new TrRmSection(kind, flags, recordCount,
                        section.ToArray()));
            }

            return TrRmArtifactWriter.Encode(sections, schema);
        }

        static void AssertInvalid(byte[] artifact, int methodRow)
        {
            Assert.Throws<InvalidDataException>(() =>
            {
                var tables = TrRmStructuralMetadataValidator
                    .ValidateC1AndGetExecutionTables(
                        TrRmArtifactIndex
                            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                                artifact));
                tables.Bodies.ValidateSelectedImplementation(
                    tables.Methods.GetBodyRow(methodRow),
                    tables.Contracts.Count);
            });
        }
    }

    [Test]
    public void RetainsGenericMethodFormalRowsInClassViews()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var tree = SyntaxTree.Parse("""
            public class Box {
                public int32 Echo(int32 value) {
                    return value;
                }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var methodDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration);
        var classIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "GenericClassMethodProvider", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, classIndex,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(classIndex,
                            methodDeclaration.Span, "Box.Echo"),
                    ]),
            ]);
        var genericArtifact = AddGenericTypeFormal(artifact);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, genericArtifact),
            "GenericClassMethodProvider", [], ["BoxKey"]);
        var method = reference.StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box")
            .Declaration.Methods
            .Single(static method => method.Name == "Echo");
        Assert.That(method.GenericParameterCount, Is.EqualTo(1));
        Assert.That(method.GenericParameters, Has.Length.EqualTo(1));
        var formal = method.GenericParameters[0];
        Assert.That(formal.Row, Is.EqualTo(1));
        Assert.That(formal.OwnerDefinitionId, Is.EqualTo(method.DefinitionId));
        Assert.That(formal.Ordinal, Is.Zero);
        Assert.That(formal.Name, Is.EqualTo("Echo"));
        Assert.That(formal.FormalKind, Is.EqualTo((byte)1));
        Assert.That(formal.OwnershipDomain, Is.EqualTo((byte)2));
        Assert.That(formal.NullabilityDomain, Is.EqualTo((byte)1));
        Assert.That(formal.ConstantTypeSpecReference, Is.Zero);
        Assert.That(formal.DefaultArgumentReference, Is.EqualTo(0xF200_0008u));
        Assert.That(formal.Constraints, Has.Length.EqualTo(1));
        Assert.That(formal.Constraints[0].Row, Is.EqualTo(1));
        Assert.That(formal.Constraints[0].OwnerGenericParamRow,
            Is.EqualTo(formal.Row));
        Assert.That(formal.Constraints[0].Kind, Is.EqualTo((ushort)2));
        Assert.That(formal.Constraints[0].Payload, Is.EqualTo(1u));

        static byte[] AddGenericTypeFormal(byte[] artifact)
            => RewriteGenericMethodTypeFormal(artifact, 2,
                0xF200_0008u, includeUniqueConstraint: true);
    }

    private static byte[] RewriteGenericMethodTypeFormal(byte[] artifact,
        byte ownershipDomain, uint defaultArgumentReference,
        bool includeUniqueConstraint, bool useFormalParameter = false)
    {
        var schema
            = TrRmArtifactSchema
                .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
        var artifactIndex = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(artifactIndex);
        var methodDefinition = tables.Exports
            .GetExportsForModule(1)
            .Single(static export =>
                export.ExportKey == "Box.Echo").Definition;
        var methodRow = checked((int)(
            methodDefinition & 0x00FF_FFFF));
        var signatureRow = tables.Methods.GetSignature(methodRow);
        if (tables.Signatures.GetGenericCount(signatureRow) != 0
            || artifactIndex.TryGetSection(0x0019, out _, out _)
            || artifactIndex.TryGetSection(0x001A, out _, out _)
            || !artifactIndex.TryGetSection(0x0017, out var signatureSection,
                out var signatureCount)
            || signatureCount != (uint)tables.Signatures.Count)
        {
            throw new InvalidDataException(
                "The generic class MethodDef fixture requires one nongeneric MethodSig.");
        }

        var formalNameRow = Enumerable.Range(1, tables.Strings.Count)
            .Single(row => tables.Strings.DecodeName(row) == "Echo");
        var genericParam = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(genericParam,
            methodDefinition);
        BinaryPrimitives.WriteUInt32LittleEndian(genericParam.AsSpan(8),
            checked((uint)formalNameRow));
        if (includeUniqueConstraint)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                genericParam.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                genericParam.AsSpan(16), 1);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(
            genericParam.AsSpan(28), defaultArgumentReference);
        genericParam[20] = 1;
        genericParam[21] = ownershipDomain;
        genericParam[22] = 1;

        byte[]? constraint = null;
        if (includeUniqueConstraint)
        {
            constraint = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(constraint, 1);
            BinaryPrimitives.WriteUInt16LittleEndian(
                constraint.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(
                constraint.AsSpan(8), 1);
        }

        var signatureBytes = signatureSection.ToArray();
        var signatureOffset = checked((signatureRow - 1) * 24);
        BinaryPrimitives.WriteUInt32LittleEndian(
            signatureBytes.AsSpan(signatureOffset + 16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            signatureBytes.AsSpan(signatureOffset + 20), 1);
        byte[]? parameterBytes = null;
        ReadOnlyMemory<byte>[] typeSpecRows = [];
        byte[]? typeSpecBytes = null;
        if (useFormalParameter)
        {
            var (parameterFirst, parameterCount)
                = tables.Signatures.GetParamRange(signatureRow);
            if (parameterCount != 1
                || !artifactIndex.TryGetSection(0x0018,
                    out var parameterSection, out var parameterRecordCount)
                || parameterRecordCount != (uint)tables.Parameters.Count
                || !artifactIndex.TryGetSection(0x0011,
                    out _, out var typeSpecRecordCount)
                || typeSpecRecordCount != (uint)tables.TypeSpecs.Count)
            {
                throw new InvalidDataException(
                    "The generic class MethodDef fixture cannot add a type-formal parameter TypeSpec.");
            }

            typeSpecRows
                = new ReadOnlyMemory<byte>[tables.TypeSpecs.Count + 1];
            for (var row = 1; row <= tables.TypeSpecs.Count; row++)
            {
                typeSpecRows[row - 1]
                    = tables.TypeSpecs.GetRow(row).ToArray();
            }
            typeSpecRows[^1]
                = TrRmTypeSpecTableWriter.Reference(0x04, 1);
            typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                tables.TypeDefs.Count, tables.Imports.Count, 1);
            parameterBytes = parameterSection.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(
                    checked((parameterFirst - 1) * 16 + 8)),
                0x1100_0000u | checked((uint)typeSpecRows.Length));
        }

        var featureRows = new List<TrRmFeatureRow>();
        var hasFeatureSection = artifactIndex.TryGetSection(0x0004,
            out var featureSection, out var featureRecordCount);
        if (hasFeatureSection)
        {
            var existingFeatureCount = checked((int)featureRecordCount);
            for (var row = 0; row < existingFeatureCount; row++)
            {
                var feature = featureSection.Span.Slice(row * 8, 8);
                featureRows.Add(new TrRmFeatureRow(
                    BinaryPrimitives.ReadUInt32LittleEndian(feature),
                    BinaryPrimitives.ReadUInt32LittleEndian(feature[4..])));
            }
        }
        if (!featureRows.Any(static feature =>
                feature.FeatureId
                    == TrRmFeatureTableReader.GenericMethodCallsFeatureId))
        {
            featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
        }

        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var outputFeatureCount = checked((uint)(featureBytes.Length / 8));
        if (!artifactIndex.TryGetSection(0x0001, out var moduleSection,
                out var moduleRecordCount)
            || moduleRecordCount != 1)
        {
            throw new InvalidDataException(
                "The generic class MethodDef fixture requires one Module row.");
        }
        var moduleBytes = moduleSection.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(24),
            outputFeatureCount == 0 ? 0u : 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(28),
            outputFeatureCount);
        var sections = new List<TrRmSection>(
            artifactIndex.SectionCount + (hasFeatureSection ? 0 : 1)
                + (includeUniqueConstraint ? 2 : 1));
        for (var sectionIndex = 0;
            sectionIndex < artifactIndex.SectionCount; sectionIndex++)
        {
            artifactIndex.GetSection(sectionIndex, out var kind,
                out var flags, out var section, out var recordCount);
            sections.Add(kind switch
            {
                0x0001 => new TrRmSection(kind, flags, recordCount,
                    moduleBytes),
                0x0004 => new TrRmSection(kind, flags, outputFeatureCount,
                    featureBytes),
                0x0017 => new TrRmSection(kind, flags, recordCount,
                    signatureBytes),
                0x0018 when parameterBytes is not null
                    => new TrRmSection(kind, flags, recordCount,
                        parameterBytes),
                0x0011 when typeSpecBytes is not null
                    => new TrRmSection(kind, flags,
                        checked((uint)typeSpecRows.Length),
                        typeSpecBytes),
                _ => new TrRmSection(kind, flags, recordCount,
                    section.ToArray()),
            });
        }
        sections.Add(new TrRmSection(0x0019, 0, 1, genericParam));
        if (constraint is not null)
        {
            sections.Add(new TrRmSection(0x001A, 0, 1, constraint));
        }
        if (!hasFeatureSection)
        {
            sections.Add(new TrRmSection(0x0004, 0, outputFeatureCount,
                featureBytes));
        }
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    [Test]
    public void ValidatesStaticAndInstanceGenericClassMethodImportsAgainstProviderFormals()
    {
        var schemaIdentity
            = C1M1Construction2ClassParameterDefaultsSchemaIdentity;
        var providerArtifact = CreateGenericClassProvider(true, 1,
            0xF200_0008u);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "GenericClassMethodProvider", [], ["BoxKey"]);
        var consumerTree = SyntaxTree.Parse("""
            public class Container {
                public static int32 Use(Box value) {
                    return 42;
                }
            }
            """);
        var consumerCompilation
            = Compilation.CreateLibrary(consumerTree, providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = (BoundProgram)consumerCompilation.Program!;
        var containerDeclaration = consumerTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var useMethod = containerDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration);
        var containerClassIndex = Enumerable.Range(0,
                consumerProgram.Classes.Length)
            .Single(index => consumerProgram.Classes[index].Name
                == "Container");
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerProgram, "GenericClassMethodConsumer", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(consumerProgram,
                    containerClassIndex, containerDeclaration.Span,
                    "ContainerKey", [
                        new TrRmSourceClassMemberExport(
                            containerClassIndex, useMethod.Span,
                            "Container.Use"),
                    ]),
            ]);
        var consumerWithMethodImport
            = AddGenericClassMethodImport(consumerArtifact,
                0xF200_0008u);
        var consumerInput = new TrRmStandardArtifactInput(schemaIdentity,
            consumerWithMethodImport);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            consumerInput, "GenericClassMethodConsumer", [providerInput],
            ["ContainerKey"]);
        var importedBox = reference.StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box"
                && !declaration.Expectations.IsEmpty);
        var importedMethod = importedBox.Expectations
            .SelectMany(static expectation =>
                expectation.MethodExpectations)
            .Single(static expectation =>
                expectation.ExportKey == "Box.Echo");
        Assert.That(importedMethod.ProviderDefinitionId >> 24,
            Is.EqualTo(0x16u));

        var sourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), reference);
        Assert.That(sourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                sourceFreeCompilation.Diagnostics));
        var reemittedArtifact = TrRmExecutableCompiler.EmitStandard(
            (BoundProgram)sourceFreeCompilation.Program!,
            "GenericClassMethodConsumerReemitted", schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var reemittedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        reemittedArtifact));
        var reemittedMethodImportRow = Enumerable.Range(1,
                reemittedTables.Imports.Count)
            .Single(row => reemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey == "Box.Echo");
        var reemittedMethodId = reemittedTables.Imports
            .GetExpectedDefinitionForImportRow(reemittedMethodImportRow);
        var reemittedMethodRow = checked((int)(
            reemittedMethodId & 0x00FF_FFFF));
        var reemittedSignatureRow = reemittedTables.Methods
            .GetSignature(reemittedMethodRow);
        Assert.That(reemittedTables.Signatures
            .GetGenericCount(reemittedSignatureRow), Is.EqualTo(1));
        var (formalFirst, formalCount) = reemittedTables.Signatures
            .GetGenericRange(reemittedSignatureRow);
        Assert.That(formalCount, Is.EqualTo(1));
        Assert.That(reemittedTables.GenericParams
            .GetOwnerDefId(formalFirst), Is.EqualTo(reemittedMethodId));
        Assert.That(reemittedTables.GenericParams
            .GetDefaultArgumentRef(formalFirst), Is.EqualTo(0xF200_0008u));
        Assert.That(Enumerable.Range(1, reemittedTables.Imports.Count)
            .Where(row => reemittedTables.Imports
                .GetExpectedDefinitionForImportRow(row)
                == reemittedMethodId)
            .Select(row => reemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey),
            Is.EquivalentTo(["Box.Echo", "Box.EchoAlias"]));
        var reloadedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, reemittedArtifact),
            "GenericClassMethodConsumerReemitted", [providerInput],
            ["ContainerKey"]);
        Assert.That(reloadedReference.StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box"
                && !declaration.Expectations.IsEmpty)
            .Expectations.SelectMany(static expectation =>
                expectation.MethodExpectations)
            .Any(static expectation => expectation.ExportKey == "Box.Echo"),
            Is.True);
        var relocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, CreateGenericClassProvider(true, 1,
                0xF200_0008u, includePrefixMethod: true));
        var relocatedReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedArtifact),
                "GenericClassMethodConsumerReemitted",
                [relocatedProviderInput], ["ContainerKey"]);
        var relocatedMethod = relocatedReference
            .StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box"
                && !declaration.Expectations.IsEmpty)
            .Expectations.SelectMany(static expectation =>
                expectation.MethodExpectations)
            .Single(static expectation =>
                expectation.ExportKey == "Box.Echo");
        Assert.That(relocatedMethod.ProviderDefinitionId,
            Is.Not.EqualTo(importedMethod.ProviderDefinitionId));

        var constrainedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, CreateGenericClassProvider(true, 2,
                0xF200_0008u, includeUniqueConstraint: true,
                useFormalParameter: true));
        var constrainedConsumerArtifact = AddGenericClassMethodImport(
            consumerArtifact, 0xF200_0008u,
            ownershipDomain: 2, includeUniqueConstraint: true,
            useFormalParameter: true);
        var constrainedReference = TrRmExecutableCompiler
            .CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    constrainedConsumerArtifact),
                "GenericClassMethodConsumer",
                [constrainedProviderInput], ["ContainerKey"]);
        var constrainedSourceFreeCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(string.Empty), constrainedReference);
        Assert.That(constrainedSourceFreeCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                constrainedSourceFreeCompilation.Diagnostics));
        var constrainedReemittedArtifact
            = TrRmExecutableCompiler.EmitStandard(
                (BoundProgram)constrainedSourceFreeCompilation.Program!,
                "GenericClassMethodConsumerConstrainedReemitted",
                schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement);
        var constrainedReemittedTables
            = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                            constrainedReemittedArtifact));
        var constrainedMethodImportRow = Enumerable.Range(1,
                constrainedReemittedTables.Imports.Count)
            .Single(row => constrainedReemittedTables.Imports
                .GetBindingForImportRow(row).ExportKey == "Box.Echo");
        var constrainedMethodId = constrainedReemittedTables.Imports
            .GetExpectedDefinitionForImportRow(constrainedMethodImportRow);
        var constrainedMethodRow = checked((int)(
            constrainedMethodId & 0x00FF_FFFF));
        var constrainedSignature = constrainedReemittedTables.Methods
            .GetSignature(constrainedMethodRow);
        var (constrainedFormalFirst, constrainedFormalCount)
            = constrainedReemittedTables.Signatures
                .GetGenericRange(constrainedSignature);
        Assert.That(constrainedFormalCount, Is.EqualTo(1));
        var constrainedFormal = constrainedFormalFirst;
        Assert.That(constrainedReemittedTables.GenericParams
            .GetOwnershipDomain(constrainedFormal), Is.EqualTo((byte)2));
        var (constraintFirst, constraintCount)
            = constrainedReemittedTables.GenericParams
                .GetConstraintRange(constrainedFormal);
        Assert.That(constraintCount, Is.EqualTo(1));
        Assert.That(constrainedReemittedTables.Constraints
            .GetOwnerGenericParam(constraintFirst),
            Is.EqualTo(constrainedFormal));
        Assert.That(constrainedReemittedTables.Constraints
            .GetKind(constraintFirst), Is.EqualTo((ushort)2));
        Assert.That(constrainedReemittedTables.Constraints
            .GetPayload(constraintFirst), Is.EqualTo(1u));
        var (constrainedParameterFirst, constrainedParameterCount)
            = constrainedReemittedTables.Signatures
                .GetParamRange(constrainedSignature);
        Assert.That(constrainedParameterCount, Is.EqualTo(1));
        var constrainedParameterTypeSpec
            = constrainedReemittedTables.Parameters.GetTypeSpec(
                constrainedParameterFirst);
        var constrainedParameterType
            = constrainedReemittedTables.TypeSpecs.GetTypeSpecRef(
                constrainedParameterTypeSpec);
        Assert.That(constrainedParameterType.Form, Is.EqualTo((byte)0x04));
        Assert.That(constrainedParameterType.Row,
            Is.EqualTo(constrainedFormal));
        _ = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity,
                constrainedReemittedArtifact),
            "GenericClassMethodConsumerConstrainedReemitted",
            [constrainedProviderInput], ["ContainerKey"]);

        var callerTree = SyntaxTree.Parse("""
            public class Caller {
                public static int32 Run(Box box, int32 value) {
                    return Box.Echo<int32>(value);
                }
            }
            """);
        var callerCompilation = Compilation.CreateLibrary(callerTree,
            providerReference);
        Assert.That(callerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                callerCompilation.Diagnostics));
        var instanceProviderModuleIdentity
            = "GenericInstanceClassMethodProvider";
        var instanceProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, CreateGenericClassProvider(false, 1,
                0xF200_0008u));
        var instanceProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                instanceProviderInput, instanceProviderModuleIdentity, [],
                ["BoxKey"]);
        var instanceConsumerArtifact
            = AddGenericClassMethodImport(consumerArtifact,
                0xF200_0008u, hasReceiver: true);
        var instanceConsumerInput = new TrRmStandardArtifactInput(
            schemaIdentity, instanceConsumerArtifact);
        var instanceConsumerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                instanceConsumerInput, "GenericClassMethodConsumer",
                [instanceProviderInput], ["ContainerKey"]);
        var importedInstanceMethod = instanceConsumerReference
            .StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box"
                && !declaration.Expectations.IsEmpty)
            .Expectations.SelectMany(static expectation =>
                expectation.MethodExpectations)
            .Single(static expectation =>
                expectation.ExportKey == "Box.Echo");
        Assert.That(importedInstanceMethod.ProviderDefinitionId >> 24,
            Is.EqualTo(0x16u));

        var instanceCallerTree = SyntaxTree.Parse("""
            public class InstanceCaller {
                public static int32 Run(Box box, int32 value) {
                    return box.Echo<int32>(value);
                }
            }
            """);
        var instanceCallerCompilation
            = Compilation.CreateLibrary(instanceCallerTree,
                instanceProviderReference);
        Assert.That(instanceCallerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                instanceCallerCompilation.Diagnostics));

        var forwardingTree = SyntaxTree.Parse("""
            public class Forwarder {
                public static int32 Use(Box value) { return 42; }
            }
            public int32 Forward<T>(Box box, int32 value) {
                return Box.Echo<T>(value);
            }
            """);
        var forwardingCompilation = Compilation.CreateLibrary(
            forwardingTree, providerReference);
        Assert.That(forwardingCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                forwardingCompilation.Diagnostics));
        var forwardingProgram = (BoundProgram)forwardingCompilation.Program!;
        var forwardingDeclaration = forwardingTree.Root.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.ClassDeclaration);
        var useDeclaration = forwardingDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration);
        var forwardingClassIndex = Enumerable.Range(0,
                forwardingProgram.Classes.Length)
            .Single(index => forwardingProgram.Classes[index].Name
                == "Forwarder");
        var forwardingGenericMethod
            = forwardingProgram.GenericMethodDefinitions.Single();
        var forwardingArtifact = TrRmExecutableCompiler.EmitStandard(
            forwardingProgram, "GenericClassMethodForwarder",
            schemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [
                new TrRmSourceGenericMethodExport(forwardingProgram,
                    forwardingGenericMethod.SourceInputOrdinal,
                    forwardingGenericMethod.Span, "Forward"),
            ], [
                new TrRmSourceClassExport(forwardingProgram,
                    forwardingClassIndex, forwardingDeclaration.Span,
                    "ForwarderKey", [
                        new TrRmSourceClassMemberExport(
                            forwardingClassIndex, useDeclaration.Span,
                            "Forwarder.Use"),
                    ]),
            ]);
        var forwardingInput = new TrRmStandardArtifactInput(schemaIdentity,
            forwardingArtifact);
        var forwardingReference
            = TrRmExecutableCompiler.CreateStandardReference(
                forwardingInput, "GenericClassMethodForwarder",
                [providerInput], ["ForwarderKey"]);
        var forwardedImportedMethod = forwardingReference
            .StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box"
                && !declaration.Expectations.IsEmpty)
            .Expectations.SelectMany(static expectation =>
                expectation.MethodExpectations)
            .Single(static expectation =>
                expectation.ExportKey == "Box.Echo");
        Assert.That(forwardedImportedMethod.ProviderDefinitionId >> 24,
            Is.EqualTo(0x16u));

        var instanceForwardingTree = SyntaxTree.Parse("""
            public class InstanceForwarder {
                public static int32 Use(Box value) { return 42; }
            }
            public int32 Forward<T>(Box box, int32 value) {
                return box.Echo<T>(value);
            }
            """);
        var instanceForwardingCompilation = Compilation.CreateLibrary(
            instanceForwardingTree, instanceProviderReference);
        Assert.That(instanceForwardingCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                instanceForwardingCompilation.Diagnostics));
        var instanceForwardingProgram
            = (BoundProgram)instanceForwardingCompilation.Program!;
        var instanceForwardingDeclaration
            = instanceForwardingTree.Root.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.ClassDeclaration);
        var instanceUseDeclaration
            = instanceForwardingDeclaration.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FunctionDeclaration);
        var instanceForwardingClassIndex = Enumerable.Range(0,
                instanceForwardingProgram.Classes.Length)
            .Single(index =>
                instanceForwardingProgram.Classes[index].Name
                    == "InstanceForwarder");
        var instanceForwardingGenericMethod
            = instanceForwardingProgram.GenericMethodDefinitions.Single();
        var instanceForwardingArtifact
            = TrRmExecutableCompiler.EmitStandard(
                instanceForwardingProgram,
                "GenericInstanceClassMethodForwarder", schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [
                    new TrRmSourceGenericMethodExport(
                        instanceForwardingProgram,
                        instanceForwardingGenericMethod.SourceInputOrdinal,
                        instanceForwardingGenericMethod.Span, "Forward"),
                ], [
                    new TrRmSourceClassExport(
                        instanceForwardingProgram,
                        instanceForwardingClassIndex,
                        instanceForwardingDeclaration.Span,
                        "InstanceForwarderKey", [
                            new TrRmSourceClassMemberExport(
                                instanceForwardingClassIndex,
                                instanceUseDeclaration.Span,
                                "InstanceForwarder.Use"),
                        ]),
                ]);
        var instanceForwardingInput
            = new TrRmStandardArtifactInput(schemaIdentity,
                instanceForwardingArtifact);
        var instanceForwardingReference
            = TrRmExecutableCompiler.CreateStandardReference(
                instanceForwardingInput,
                "GenericInstanceClassMethodForwarder",
                [instanceProviderInput], ["InstanceForwarderKey"]);
        var forwardedInstanceMethod = instanceForwardingReference
            .StandardClassDeclarations
            .Single(static declaration =>
                declaration.Declaration.Name == "Box"
                && !declaration.Expectations.IsEmpty)
            .Expectations.SelectMany(static expectation =>
                expectation.MethodExpectations)
            .Single(static expectation =>
                expectation.ExportKey == "Box.Echo");
        Assert.That(forwardedInstanceMethod.ProviderDefinitionId >> 24,
            Is.EqualTo(0x16u));

        var incompatibleProvider = new TrRmStandardArtifactInput(
            schemaIdentity, CreateGenericClassProvider(true, 2,
                0xF200_0008u));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "GenericClassMethodConsumer",
                [incompatibleProvider], ["ContainerKey"]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedArtifact),
                "GenericClassMethodConsumerReemitted",
                [incompatibleProvider], ["ContainerKey"]));
        var incompatibleDefaultProvider = new TrRmStandardArtifactInput(
            schemaIdentity, CreateGenericClassProvider(true, 1,
                0xF200_0009u));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                consumerInput, "GenericClassMethodConsumer",
                [incompatibleDefaultProvider], ["ContainerKey"]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedArtifact),
                "GenericClassMethodConsumerReemitted",
                [incompatibleDefaultProvider], ["ContainerKey"]));

        byte[] CreateGenericClassProvider(bool isStatic,
            byte ownershipDomain, uint defaultArgumentReference,
            bool includeUniqueConstraint = false,
            bool useFormalParameter = false,
            bool includePrefixMethod = false)
        {
            var returnStatement = useFormalParameter
                ? "return 42;" : "return value;";
            var methodDeclaration = isStatic
                ? $"public static int32 Echo(int32 value) {{ {returnStatement} }}"
                : $"public int32 Echo(int32 value) {{ {returnStatement} }}";
            var prefixDeclaration = includePrefixMethod
                ? "public class Prefix { public static int32 Before() { return 1; } }"
                : string.Empty;
            var providerTree = SyntaxTree.Parse(
                $"{prefixDeclaration} public class Box " +
                $"{{ {methodDeclaration} }}");
            var providerCompilation
                = Compilation.CreateLibrary(providerTree);
            Assert.That(providerCompilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    providerCompilation.Diagnostics));
            var providerProgram = (BoundProgram)providerCompilation.Program!;
            var classDeclarations = providerTree.Root.Children
                .OfType<SyntaxNode>()
                .Where(static node =>
                    node.Kind == SyntaxKind.ClassDeclaration);
            var boxDeclaration = classDeclarations
                .Single(static node => node.Children
                    .OfType<SyntaxTokenElement>()
                    .Any(static token => token.Token.Name == "Box"));
            var echo = boxDeclaration.Children.OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FunctionDeclaration);
            var boxClassIndex = Enumerable.Range(0,
                    providerProgram.Classes.Length)
                .Single(index => providerProgram.Classes[index].Name
                    == "Box");
            var providerModuleIdentity = isStatic
                ? "GenericClassMethodProvider"
                : "GenericInstanceClassMethodProvider";
            var classExports = new List<TrRmSourceClassExport> {
                new(providerProgram, boxClassIndex,
                    boxDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(boxClassIndex,
                            echo.Span, "Box.Echo"),
                    ]),
            };
            if (includePrefixMethod)
            {
                var prefixClass = classDeclarations.Single(static node =>
                    node.Children.OfType<SyntaxTokenElement>().Any(
                        static token => token.Token.Name == "Prefix"));
                var prefixMethod = prefixClass.Children
                    .OfType<SyntaxNode>()
                    .Single(static node =>
                        node.Kind == SyntaxKind.FunctionDeclaration);
                var prefixClassIndex = Enumerable.Range(0,
                        providerProgram.Classes.Length)
                    .Single(index =>
                        providerProgram.Classes[index].Name == "Prefix");
                classExports.Add(new TrRmSourceClassExport(
                    providerProgram, prefixClassIndex,
                    prefixClass.Span, "PrefixKey", [
                        new TrRmSourceClassMemberExport(
                            prefixClassIndex, prefixMethod.Span,
                            "Prefix.Before"),
                    ]));
            }

            var provider = TrRmExecutableCompiler.EmitStandard(
                providerProgram, providerModuleIdentity,
                schemaIdentity,
                TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
                [], [], classExports);
            var providerWithAlias = AddMethodExportAlias(provider,
                "Box.Echo", "Box.EchoAlias");
            var formalProvider = RewriteGenericMethodTypeFormal(
                providerWithAlias,
                    ownershipDomain, defaultArgumentReference,
                    includeUniqueConstraint, useFormalParameter);
            return formalProvider;
        }

        static byte[] AddGenericClassMethodImport(byte[] artifact,
            uint defaultArgumentReference, bool hasReceiver = false,
            byte ownershipDomain = 1,
            bool includeUniqueConstraint = false,
            bool useFormalParameter = false)
        {
            var schema
                = TrRmArtifactSchema
                    .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
            var index = TrRmArtifactIndex
                .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                    artifact);
            var tables = TrRmStructuralMetadataValidator
                .ValidateC1AndGetExecutionTables(index);
            var useDefinition = tables.Exports
                .GetExportsForModule(1)
                .Single(static export =>
                    export.ExportKey == "Container.Use").Definition;
            var useMethodRow = checked((int)(
                useDefinition & 0x00FF_FFFF));
            var useSignature = tables.Methods.GetSignature(useMethodRow);
            var (parameterFirst, parameterCount)
                = tables.Signatures.GetParamRange(useSignature);
            if (parameterCount != 1 || tables.Methods.Count == 0
                || tables.Signatures.GetGenericCount(useSignature) != 0
                || tables.GenericParams.Count != 0
                || index.TryGetSection(0x0019, out _, out _)
                || index.TryGetSection(0x001A, out _, out _)
                || !index.TryGetSection(0x0016, out var methodSection,
                    out var methodCount)
                || methodCount != (uint)tables.Methods.Count
                || !index.TryGetSection(0x0017, out var signatureSection,
                    out var signatureCount)
                || signatureCount != (uint)tables.Signatures.Count
                || !index.TryGetSection(0x001C, out var importSection,
                    out var importCount)
                || importCount != (uint)tables.Imports.Count
                || !index.TryGetSection(0x0018, out var parameterSection,
                    out var parameterTableCount)
                || parameterTableCount != (uint)tables.Parameters.Count
                || !index.TryGetSection(0x0066, out var labelSection,
                    out var labelCount)
                || labelCount != (uint)tables.MethodParameterLabels.Count
                || (useFormalParameter
                    && (!index.TryGetSection(0x0011, out _,
                            out var typeSpecRecordCount)
                        || typeSpecRecordCount
                            != (uint)tables.TypeSpecs.Count)))
            {
                throw new InvalidDataException(
                    "The generic class MethodDef import fixture has an unexpected source shape.");
            }

            var boxTypeSpecReference
                = tables.Parameters.GetTypeSpec(parameterFirst);
            var boxTypeSpec
                = tables.TypeSpecs.GetTypeSpecRef(boxTypeSpecReference);
            var boxTypeSpecBytes
                = tables.TypeSpecs.GetTypeSpecBytes(boxTypeSpecReference);
            if (!boxTypeSpec.IsRow || boxTypeSpec.Form != 0x03
                || boxTypeSpecBytes.Length != 6)
            {
                throw new InvalidDataException(
                    "The generic class MethodDef import fixture requires an imported Box parameter.");
            }
            var ownerImportRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    boxTypeSpecBytes[2..]));
            var ownerDefinitionId = tables.Imports
                .GetExpectedDefinitionForImportRow(ownerImportRow);
            if ((ownerDefinitionId >> 24) != 0x13)
            {
                throw new InvalidDataException(
                    "The generic class MethodDef import fixture has no TypeDef owner Import.");
            }

            var moduleRefRow = tables.Imports
                .GetModuleRefForImportRow(ownerImportRow);
            var stringRows = new List<(string Text, int Encoding)>(
                tables.Strings.Count + 3);
            for (var row = 1; row <= tables.Strings.Count; row++)
            {
                stringRows.Add((tables.Strings.DecodeNamespace(row), 1));
            }

            int GetStringRow(string text)
            {
                var existing = stringRows.FindIndex(candidate =>
                    StringComparer.Ordinal.Equals(candidate.Text, text));
                if (existing >= 0)
                {
                    return existing + 1;
                }

                stringRows.Add((text, 1));
                return stringRows.Count;
            }

            var methodNameStringRow = GetStringRow("Echo");
            var exportKeyStringRow = GetStringRow("Box.Echo");
            var exportKeyAliasStringRow = GetStringRow("Box.EchoAlias");
            var formalNameStringRow = GetStringRow("T");
            var parameterNameStringRow = GetStringRow("value");
            var stringBytes = TrRmStringTableWriter.EncodeTextRows(
                stringRows);

            var methodRow = checked(tables.Methods.Count + 1);
            var signatureRow = checked(tables.Signatures.Count + 1);
            var formalRow = checked(tables.GenericParams.Count + 1);
            ReadOnlyMemory<byte>[] typeSpecRows = useFormalParameter
                ? new ReadOnlyMemory<byte>[tables.TypeSpecs.Count + 1]
                : [];
            var formalTypeSpecReference = 0u;
            if (useFormalParameter)
            {
                for (var row = 1; row <= tables.TypeSpecs.Count; row++)
                {
                    typeSpecRows[row - 1]
                        = tables.TypeSpecs.GetRow(row).ToArray();
                }

                typeSpecRows[^1]
                    = TrRmTypeSpecTableWriter.Reference(0x04,
                        checked((uint)formalRow));
                formalTypeSpecReference = 0x1100_0000u
                    | checked((uint)typeSpecRows.Length);
            }

            var methodBytes = methodSection.ToArray();
            Array.Resize(ref methodBytes, methodBytes.Length + 28);
            var methodOffset = methodBytes.Length - 28;
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodBytes.AsSpan(methodOffset),
                0x1300_0000u
                    | (ownerDefinitionId & 0x00FF_FFFF));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodBytes.AsSpan(methodOffset + 4),
                checked((uint)methodNameStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodBytes.AsSpan(methodOffset + 8),
                checked((uint)signatureRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodBytes.AsSpan(methodOffset + 12), 2);

            var signatureBytes = signatureSection.ToArray();
            Array.Resize(ref signatureBytes, signatureBytes.Length + 24);
            var signatureOffset = signatureBytes.Length - 24;
            BinaryPrimitives.WriteUInt32LittleEndian(
                signatureBytes.AsSpan(signatureOffset), 0xF200_0008u);
            BinaryPrimitives.WriteUInt32LittleEndian(
                signatureBytes.AsSpan(signatureOffset + 4),
                hasReceiver ? boxTypeSpecReference : 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                signatureBytes.AsSpan(signatureOffset + 8),
                checked((uint)tables.Parameters.Count + 1));
            BinaryPrimitives.WriteUInt32LittleEndian(
                signatureBytes.AsSpan(signatureOffset + 12), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                signatureBytes.AsSpan(signatureOffset + 16),
                checked((uint)formalRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                signatureBytes.AsSpan(signatureOffset + 20), 1);

            var parameterBytes = parameterSection.ToArray();
            Array.Resize(ref parameterBytes, parameterBytes.Length + 16);
            var parameterOffset = parameterBytes.Length - 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(parameterOffset),
                checked((uint)signatureRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(parameterOffset + 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                parameterBytes.AsSpan(parameterOffset + 8), 0xF200_0008u);
            if (useFormalParameter)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    parameterBytes.AsSpan(parameterOffset + 8),
                    formalTypeSpecReference);
            }

            var labelBytes = labelSection.ToArray();
            Array.Resize(ref labelBytes, labelBytes.Length + 16);
            var labelOffset = labelBytes.Length - 16;
            BinaryPrimitives.WriteUInt32LittleEndian(
                labelBytes.AsSpan(labelOffset),
                checked((uint)methodRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                labelBytes.AsSpan(labelOffset + 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                labelBytes.AsSpan(labelOffset + 8),
                checked((uint)parameterNameStringRow));

            var genericParam = new byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(genericParam,
                0x1600_0000u | checked((uint)methodRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                genericParam.AsSpan(8),
                checked((uint)formalNameStringRow));
            genericParam[20] = 1;
            genericParam[21] = ownershipDomain;
            genericParam[22] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(
                genericParam.AsSpan(28), defaultArgumentReference);
            byte[]? constraint = null;
            if (includeUniqueConstraint)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    genericParam.AsSpan(12), 1);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    genericParam.AsSpan(16), 1);
                constraint = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(constraint,
                    checked((uint)formalRow));
                BinaryPrimitives.WriteUInt16LittleEndian(
                    constraint.AsSpan(4), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    constraint.AsSpan(8), 1);
            }

            var importBytes = importSection.ToArray();
            Array.Resize(ref importBytes, importBytes.Length + 24);
            var importOffset = importBytes.Length - 24;
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(importOffset),
                checked((uint)moduleRefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(importOffset + 4),
                checked((uint)exportKeyStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(importOffset + 8),
                0x1600_0000u | checked((uint)methodRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(importOffset + 12),
                checked((uint)moduleRefRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(importOffset + 16),
                checked((uint)exportKeyAliasStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(importOffset + 20),
                0x1600_0000u | checked((uint)methodRow));

            var featureRows = new List<TrRmFeatureRow>();
            var hasFeatureSection = index.TryGetSection(0x0004,
                out var featureSection, out var featureRecordCount);
            if (hasFeatureSection)
            {
                for (var row = 0;
                    row < checked((int)featureRecordCount); row++)
                {
                    var feature = featureSection.Span.Slice(row * 8, 8);
                    featureRows.Add(new TrRmFeatureRow(
                        BinaryPrimitives.ReadUInt32LittleEndian(feature),
                        BinaryPrimitives.ReadUInt32LittleEndian(
                            feature[4..])));
                }
            }
            if (!featureRows.Any(static feature =>
                    feature.FeatureId
                        == TrRmFeatureTableReader
                            .GenericMethodCallsFeatureId))
            {
                featureRows.Add(TrRmFeatureRow.GenericMethodCallsCandidate);
            }
            if (!featureRows.Any(static feature =>
                    feature.FeatureId
                        == TrRmFeatureTableReader
                            .MethodParameterLabelsFeatureId))
            {
                featureRows.Add(
                    TrRmFeatureRow.MethodParameterLabelsCandidate);
            }

            var featureBytes
                = TrRmFeatureTableWriter.Encode(featureRows, schema);
            var outputFeatureCount
                = checked((uint)(featureBytes.Length / 8));
            if (!index.TryGetSection(0x0001, out var moduleSection,
                    out var moduleCount)
                || moduleCount != 1)
            {
                throw new InvalidDataException(
                    "The generic class MethodDef import fixture requires one Module row.");
            }
            var moduleBytes = moduleSection.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(
                moduleBytes.AsSpan(24), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                moduleBytes.AsSpan(28), outputFeatureCount);

            var sections = new List<TrRmSection>(index.SectionCount + 4);
            for (var sectionIndex = 0;
                sectionIndex < index.SectionCount; sectionIndex++)
            {
                index.GetSection(sectionIndex, out var kind, out var flags,
                    out var section, out var recordCount);
                sections.Add(kind switch
                {
                    0x0001 => new TrRmSection(kind, flags, recordCount,
                        moduleBytes),
                    0x0002 => new TrRmSection(kind, flags,
                        checked((uint)stringRows.Count), stringBytes),
                    0x0004 => new TrRmSection(kind, flags,
                        outputFeatureCount, featureBytes),
                    0x0016 => new TrRmSection(kind, flags,
                        checked((uint)tables.Methods.Count + 1),
                        methodBytes),
                    0x0017 => new TrRmSection(kind, flags,
                        checked((uint)tables.Signatures.Count + 1),
                        signatureBytes),
                    0x0018 => new TrRmSection(kind, flags,
                        checked((uint)tables.Parameters.Count + 1),
                        parameterBytes),
                    0x0011 when useFormalParameter
                        => new TrRmSection(kind, flags,
                            checked((uint)typeSpecRows.Length),
                            TrRmTypeSpecTableWriter.Encode(typeSpecRows,
                                tables.TypeDefs.Count,
                                tables.Imports.Count, 1)),
                    0x001C => new TrRmSection(kind, flags,
                        checked((uint)tables.Imports.Count + 2), importBytes),
                    0x0066 => new TrRmSection(kind, flags,
                        checked((uint)tables.MethodParameterLabels.Count
                            + 1), labelBytes),
                    _ => new TrRmSection(kind, flags, recordCount,
                        section.ToArray()),
                });
            }
            sections.Add(new TrRmSection(0x0019, 0, 1, genericParam));
            if (constraint is not null)
            {
                sections.Add(new TrRmSection(0x001A, 0, 1, constraint));
            }
            if (!hasFeatureSection)
            {
                sections.Add(new TrRmSection(0x0004, 0,
                    outputFeatureCount, featureBytes));
            }
            sections.Sort(static (left, right) =>
                left.Kind.CompareTo(right.Kind));
            return TrRmArtifactWriter.Encode(sections, schema);
        }
    }

    [Test]
    public void ComposesClassGenericAndInterfaceParameterDefaultSurfaces()
    {
        var tree = SyntaxTree.Parse("""
            public class Box {
                        public int32 Value;
                        public Box(int32 value = 42) { Value = value; }
            }
            public interface IValue {
                        int32 Read(void marker = ());
            }
            public int32 Echo<T>(int32 value = 9, void marker = ()) {
                        return value;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceMember = interfaceDefinition.Members.Single();
        var genericDefinition = program.GenericMethodDefinitions.Single();
        var interfaceExport = new TrRmSourceTypeExport("", "IValue",
            "IValue") {
            InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            interfaceMember.SourceInputOrdinal,
                            interfaceMember.SourceDeclarationSpan, "IValue.Read"),
            ],
        };
        var genericExport = new TrRmSourceGenericMethodExport(program,
            genericDefinition.SourceInputOrdinal, genericDefinition.Span,
            "Echo");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "Construction2D1ParameterDefaults",
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [interfaceExport], [genericExport],
            CreateClassExports(program, tree));
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                        TrRmArtifactIndex
                            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                                artifact));

        Assert.That(tables.Methods.Count, Is.EqualTo(6));
        Assert.That(tables.MethodParameterLabels.Count, Is.EqualTo(7));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "IValue.Read",
            out var interfaceMethod), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Echo",
            out var genericMethod), Is.True);
        tables.ValidateRegisterBodyMapsForMethod(3);
        tables.ValidateRegisterBodyMapsForMethod(4);
        foreach (var methodDefRow in new[] {
            1, 2, 3, 4,
            checked((int)(interfaceMethod & 0x00FF_FFFF)),
            checked((int)(genericMethod & 0x00FF_FFFF)),
        })
        {
            Assert.That(tables.MethodParameterLabels
                        .TryGetDefaultConstantSourceRow(methodDefRow, 0,
                            out var defaultValue), Is.True);
            Assert.That(defaultValue, Is.GreaterThan(0));
        }

        foreach (var (methodDefRow, parameterOrdinal) in new[] {
            (checked((int)(interfaceMethod & 0x00FF_FFFF)), 0),
            (checked((int)(genericMethod & 0x00FF_FFFF)), 1),
        })
        {
            Assert.That(tables.MethodParameterLabels
                    .TryGetDefaultConstantSourceRow(methodDefRow,
                        checked((uint)parameterOrdinal), out var defaultSource),
                Is.True);
            Assert.That(defaultSource, Is.GreaterThan(0));
            var signatureRow = tables.Methods.GetSignature(methodDefRow);
            var (parameterFirst, _) = tables.Signatures
                .GetParamRange(signatureRow);
            var parameterRow = checked(parameterFirst + parameterOrdinal);
            Assert.That(tables.Parameters.GetTypeSpec(parameterRow),
                Is.EqualTo(0xF200_0000u));
            var valueNodeRow = checked((int)tables.ConstantSources
                .GetPayloadReference(checked((int)defaultSource)));
            Assert.That(tables.FrozenValueNodes.GetScalarPayload(
                    valueNodeRow, tables.Blobs).ToArray(),
                Is.Empty);
        }

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                    C1M1Construction2ClassParameterDefaultsSchemaIdentity,
                    artifact),
            "Construction2D1ParameterDefaults", []);
        var importedInterfaceParameter = reference.Interfaces.Single()
            .Members.Single().Parameters.Single();
        Assert.That(importedInterfaceParameter.Type.PrimitiveType,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(importedInterfaceParameter.DefaultValue!.Type,
            Is.EqualTo(PrimitiveType.Void));
        var importedGenericDefault
            = reference.GenericMethodImports.Single()
                    .OrdinaryParameterDefaults[1]!;
        Assert.That(importedGenericDefault.ParameterType,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(importedGenericDefault.Value.Type,
            Is.EqualTo(PrimitiveType.Void));

        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            int32 Use<T>(T value) where T : IValue {
                return value.via<IValue>.Read() + Echo<bool>();
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        var consumerProgram = (BoundProgram)consumer.Program!;
        var use = consumerProgram.Functions.Single(
            static function => function.Name == "Use");
        var calls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(use.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    calls.Add(call);
                }
            }), Is.True);
        Assert.That(calls.Count(static call => call.Arguments.Any(
            static argument => argument.Type == PrimitiveType.Void)),
            Is.EqualTo(2));
    }

    [Test]
    public void RetainsUnselectedSourceClassesWithoutPublishingThem()
    {
        var tree = SyntaxTree.Parse("""
            public class Root {
                public int32 Value;
                public Root(int32 value) { Value = value; }
            }
            public class Unselected {
                public int32 Value;
                public Unselected(int32 value) { Value = value; }
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var rootDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(node => node.Kind == SyntaxKind.ClassDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Name == "Root"));
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "SelectedClassRoot", C1M1SchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    rootDeclaration.Span, "RootKey", []),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex.ForC1M1Draft(artifact));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(Enumerable.Range(1, tables.TypeDefs.Count)
            .Select(tables.TypeDefs.GetName),
            Is.EquivalentTo(new[] { "Root", "Unselected" }));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "RootKey",
            out _), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Unselected",
            out _), Is.False);
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
        Assert.That(classType[1], Is.EqualTo(0x01));
        Assert.That(optionalType[1], Is.Zero);
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

    private static byte[] EmitClassMethodProvider(string moduleIdentity,
        int defaultValue, string readParameterType,
        bool readIsUnsafe = false, bool readIsInstance = false)
    {
        var readModifiers = readIsInstance ? "public" : "public static";
        if (readIsUnsafe)
        {
            readModifiers += " unsafe";
        }

        var readMethod = readParameterType == "int32"
            ? $"{readModifiers} int32 Read(int32 value = {defaultValue}) {{ return value; }}"
            : $"{readModifiers} int32 Read(int64 value = {defaultValue}) {{ return 17; }}";
        var tree = SyntaxTree.Parse($$"""
            public class Box {
                public int32 Value;
                public Box(int32 value = {{defaultValue}}, void marker = ()) {
                    Value = value;
                }
                {{readMethod}}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclaration = tree.Root.Children.OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.ClassDeclaration);
        var members = classDeclaration.Children.OfType<SyntaxNode>();
        var field = members.Single(static node =>
            node.Kind == SyntaxKind.FieldDeclaration);
        var constructor = members.Single(static node =>
            node.Kind == SyntaxKind.ConstructorDeclaration);
        var read = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Read"));

        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, 0,
                    classDeclaration.Span, "BoxKey", [
                        new TrRmSourceClassMemberExport(0, field.Span,
                            "Box.Value"),
                        new TrRmSourceClassMemberExport(0,
                            constructor.Span, "Box.Initialize") {
                            ConstructionProjectionExportKey = "Box.Create",
                        },
                        new TrRmSourceClassMemberExport(0, read.Span,
                            "Box.Read"),
                    ]),
            ]);
    }

    private static byte[] EmitClassMethodReemissionArtifact(
        string moduleIdentity, int defaultValue,
        bool includePrefix = false, string parameterType = "int32",
        bool readIsUnsafe = false,
        bool includePrefixType = false, bool mutableReceiver = false,
        bool includeImplicitRefMethod = false)
    {
        var prefix = includePrefix
            ? "public class Prefix { public static int32 Before() { return 1; } }"
            : includePrefixType ? "public class Prefix {}" : string.Empty;
        var readBody = parameterType == "int32"
            ? "return value;" : "return 17;";
        var readModifiers = readIsUnsafe
            ? "public static unsafe" : "public static";
        var boxDeclaration = mutableReceiver
            ? "public mut class Box {" : "public class Box {";
        var boxField = mutableReceiver
            ? "public mut int32 Value;" : "public int32 Value;";
        var mutableMethod = mutableReceiver
            ? "public mut void ReadMutable() {}" : string.Empty;
        var implicitRefMethod = includeImplicitRefMethod
            ? "public void ObserveImplicit(implicit ref<int32> value) {}"
            : string.Empty;
        var tree = SyntaxTree.Parse($$"""
            {{prefix}}
            {{boxDeclaration}}
                {{boxField}}
                {{readModifiers}} int32 Read({{parameterType}} value = {{defaultValue}}) {
                    {{readBody}}
                }
                public static void ReadBox(Box value) {}
                public int32 ReadInstance(int32 value = {{defaultValue}}) {
                    return value;
                }
                {{mutableMethod}}
                {{implicitRefMethod}}
            }
            public class Container {
                public int32 Marker;
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node => node.Kind == SyntaxKind.ClassDeclaration)
            .ToArray();
        var boxDeclaration = classDeclarations.Single(node =>
            node.Children.OfType<SyntaxTokenElement>().Any(token =>
                token.Token.Kind == TokenKind.Identifier
                && token.Token.Name == "Box"));
        var containerDeclaration = classDeclarations.Single(node =>
            node.Children.OfType<SyntaxTokenElement>().Any(token =>
                token.Token.Kind == TokenKind.Identifier
                && token.Token.Name == "Container"));
        var readDeclaration = boxDeclaration.Children.OfType<SyntaxNode>()
            .Single(static member =>
                member.Kind == SyntaxKind.FunctionDeclaration
                && member.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "Read"));
        var readInstanceDeclaration = boxDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static member =>
                member.Kind == SyntaxKind.FunctionDeclaration
                && member.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "ReadInstance"));
        var readBoxDeclaration = boxDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static member =>
                member.Kind == SyntaxKind.FunctionDeclaration
                && member.Children.OfType<SyntaxTokenElement>().Any(
                    static token => token.Token.Name == "ReadBox"));
        var readMutableDeclaration = mutableReceiver
            ? boxDeclaration.Children.OfType<SyntaxNode>()
                .Single(static member =>
                    member.Kind == SyntaxKind.FunctionDeclaration
                    && member.Children.OfType<SyntaxTokenElement>().Any(
                        static token => token.Token.Name == "ReadMutable"))
            : null;
        var observeImplicitDeclaration = includeImplicitRefMethod
            ? boxDeclaration.Children.OfType<SyntaxNode>()
                .Single(static member =>
                    member.Kind == SyntaxKind.FunctionDeclaration
                    && member.Children.OfType<SyntaxTokenElement>().Any(
                        static token =>
                            token.Token.Name == "ObserveImplicit"))
            : null;
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => StringComparer.Ordinal.Equals(
                program.Classes[index].Name, "Box"));
        var containerClassIndex = Enumerable.Range(0,
                program.Classes.Length)
            .Single(index => StringComparer.Ordinal.Equals(
                program.Classes[index].Name, "Container"));

        var boxMemberExports = new List<TrRmSourceClassMemberExport> {
            new(boxClassIndex, readDeclaration.Span, "Box.Read"),
            new(boxClassIndex, readInstanceDeclaration.Span,
                "Box.ReadInstance"),
            new(boxClassIndex, readBoxDeclaration.Span, "Box.ReadBox"),
        };
        if (readMutableDeclaration is not null)
        {
            boxMemberExports.Add(new TrRmSourceClassMemberExport(
                boxClassIndex, readMutableDeclaration.Span,
                "Box.ReadMutable"));
        }
        if (observeImplicitDeclaration is not null)
        {
            boxMemberExports.Add(new TrRmSourceClassMemberExport(
                boxClassIndex, observeImplicitDeclaration.Span,
                "Box.ObserveImplicit"));
        }

        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement,
            [], [], [
                new TrRmSourceClassExport(program, boxClassIndex,
                    boxDeclaration.Span, "BoxKey", boxMemberExports),
                new TrRmSourceClassExport(program, containerClassIndex,
                    containerDeclaration.Span, "ContainerKey", []),
            ]);
    }

    private static byte[] EmitScalarBodyfulClassArtifact(
        string moduleIdentity, bool readsField = false,
        bool includePrefix = false, bool includeMethodCall = false,
        bool includeConstant = false)
    {
        var methodText = readsField
            ? "public int32 Read() { return Value; }"
            : "public int32 Read(int32 value) { return value; }";
        var methodCallText = includeMethodCall
            ? "private int32 ReadPrivate(int32 left, int32 right) { return Value; } public int32 CallRead(int32 left, int32 right) { return ReadPrivate(left, right) + right; }"
            : string.Empty;
        var constantMethodText = includeConstant
            ? "public static int32 ReadConstant() { return 42; }"
            : string.Empty;
        var prefixText = includePrefix
            ? "public class Prefix { public int32 Marker; }"
            : string.Empty;
        var tree = SyntaxTree.Parse($$"""
            {{prefixText}}
            public class Box {
                public int32 Value;
                {{methodText}}
                {{methodCallText}}
                {{constantMethodText}}
            }
            """);
        var compilation = Compilation.CreateLibrary(tree);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = (BoundProgram)compilation.Program!;
        var classDeclarations = tree.Root.Children.OfType<SyntaxNode>()
            .Where(static node =>
                node.Kind == SyntaxKind.ClassDeclaration)
            .ToArray();
        var classDeclaration = classDeclarations.Single(node =>
            node.Children.OfType<SyntaxTokenElement>().Any(token =>
                token.Token.Kind == TokenKind.Identifier
                && token.Token.Name == "Box"));
        var fieldDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node => node.Kind == SyntaxKind.FieldDeclaration);
        var methodDeclaration = classDeclaration.Children
            .OfType<SyntaxNode>()
            .Single(static node =>
                node.Kind == SyntaxKind.FunctionDeclaration
                && node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Name == "Read"));
        var methodCallDeclaration = includeMethodCall
            ? classDeclaration.Children.OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FunctionDeclaration
                    && node.Children.OfType<SyntaxTokenElement>().Any(
                        token => token.Token.Kind == TokenKind.Identifier
                            && token.Token.Name == "CallRead"))
            : null;
        var constantMethodDeclaration = includeConstant
            ? classDeclaration.Children.OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FunctionDeclaration
                    && node.Children.OfType<SyntaxTokenElement>().Any(
                        token => token.Token.Kind == TokenKind.Identifier
                            && token.Token.Name == "ReadConstant"))
            : null;
        var boxClassIndex = Enumerable.Range(0, program.Classes.Length)
            .Single(index => program.Classes[index].Name == "Box");
        var classExports = new List<TrRmSourceClassExport>();
        if (includePrefix)
        {
            var prefixDeclaration = classDeclarations.Single(node =>
                node.Children.OfType<SyntaxTokenElement>().Any(token =>
                    token.Token.Kind == TokenKind.Identifier
                    && token.Token.Name == "Prefix"));
            var prefixField = prefixDeclaration.Children
                .OfType<SyntaxNode>()
                .Single(static node =>
                    node.Kind == SyntaxKind.FieldDeclaration);
            var prefixClassIndex
                = Enumerable.Range(0, program.Classes.Length)
                    .Single(index =>
                        program.Classes[index].Name == "Prefix");
            classExports.Add(new TrRmSourceClassExport(program,
                prefixClassIndex, prefixDeclaration.Span, "PrefixKey", [
                    new TrRmSourceClassMemberExport(prefixClassIndex,
                        prefixField.Span, "Prefix.Value"),
                ]));
        }

        var boxMemberExports = new List<TrRmSourceClassMemberExport> {
            new(boxClassIndex, fieldDeclaration.Span, "Box.Value"),
            new(boxClassIndex, methodDeclaration.Span, "Box.Read"),
        };
        if (methodCallDeclaration is not null)
        {
            boxMemberExports.Add(new TrRmSourceClassMemberExport(
                boxClassIndex, methodCallDeclaration.Span,
                "Box.CallRead"));
        }
        if (constantMethodDeclaration is not null)
        {
            boxMemberExports.Add(new TrRmSourceClassMemberExport(
                boxClassIndex, constantMethodDeclaration.Span,
                "Box.ReadConstant"));
        }

        classExports.Add(new TrRmSourceClassExport(program, boxClassIndex,
            classDeclaration.Span, "BoxKey", boxMemberExports));
        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            C1M1Construction2ClassParameterDefaultsSchemaIdentity,
            TrRmModuleTableReader.WindowsX64NativeTargetRequirement, [], [],
            classExports);
    }

    private static byte[] AddMethodExportAlias(byte[] artifact,
        string originalExportKey, string aliasExportKey,
        TrRmArtifactSchema schema = TrRmArtifactSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var tables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        if (index.TryGetSection(0x0010, out _, out _)
            || index.TryGetSection(0x001C, out _, out _)
            || !tables.Exports.TryGetDefinitionForKey(1,
                originalExportKey, out var methodDefinition))
        {
            throw new InvalidOperationException(
                "The provider artifact cannot accept a method export alias.");
        }

        var textRows = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 1);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            textRows.Add((tables.Strings.DecodeNamespace(row), 1));
        }

        var aliasStringRow = textRows.Count + 1;
        textRows.Add((aliasExportKey, 1));
        if (!index.TryGetSection(0x0001, out var moduleSection,
                out var moduleCount))
        {
            throw new InvalidOperationException(
                "The provider artifact has no Module table.");
        }

        var sourceFileCount = index.TryGetSection(0x001F, out _, out var count)
            ? count : 0;
        var featureCount = index.TryGetSection(0x0004, out _, out count)
            ? count : 0;

        var existingExports = tables.Exports.GetExportsForModule(1);
        var exportRows = new List<TrRmExportRow>(
            existingExports.Length + 1);
        foreach (var (definition, exportKey) in existingExports)
        {
            var keyStringRow = AddString(exportKey);
            exportRows.Add(new TrRmExportRow(definition, keyStringRow));
        }

        exportRows.Add(new TrRmExportRow(methodDefinition, aliasStringRow));
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes, textRows.Count);
        var modules = new TrRmModuleTableReader(moduleSection, moduleCount,
            strings, 0, tables.TypeDefs.Count, tables.Methods.Count,
            sourceFileCount, featureCount);
        var exportBytes = TrRmExportTableWriter.Encode(exportRows, strings,
            tables.TypeDefs, tables.Methods, tables.Properties,
            tables.Imports, tables.Interfaces, tables.AssociatedTypeDefs,
            tables.Fields, tables.Features, modules, schema,
            tables.AssociatedConstDefs);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = kind switch {
                0x0002 => stringBytes,
                0x001D => exportBytes,
                _ => bytes.ToArray(),
            };
            var sectionRecordCount = kind switch {
                0x0002 => textRows.Count,
                0x001D => exportRows.Count,
                _ => recordCount,
            };
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)sectionRecordCount), sectionBytes));
        }

        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator.ValidateC1AndGetExecutionTables(
            new TrRmArtifactIndex(result, schema));
        return result;

        int AddString(string text)
        {
            textRows.Add((text, 1));
            return textRows.Count;
        }
    }

    private static byte[] CreateExpectedClassMethodImportArtifact(
        byte[] expectedArtifact, byte[] providerArtifact,
        string consumerIdentity, string providerIdentity,
        IReadOnlyList<string>? methodExportKeys = null,
        string? aliasTargetExportKey = null,
        string? aliasExportKey = null,
        string? rootClassExportKey = null)
    {
        var schema = TrRmArtifactSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
        var index = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                expectedArtifact);
        var expectedTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(index);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateC1AndGetExecutionTables(
                TrRmArtifactIndex
                    .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                        providerArtifact));
        if (index.TryGetSection(0x0010, out _, out _)
            || index.TryGetSection(0x001C, out _, out _))
        {
            throw new InvalidOperationException(
                "The expected artifact already has dependency rows.");
        }

        var textRows = new List<(string Text, int Encoding)>(
            expectedTables.Strings.Count + 7);
        for (var row = 1; row <= expectedTables.Strings.Count; row++)
        {
            textRows.Add((expectedTables.Strings.DecodeNamespace(row), 1));
        }

        var consumerIdentityRow = AddString(consumerIdentity);
        var providerIdentityRow = AddString(providerIdentity);
        var importRows = new List<TrRmImportRow>();
        AddImport(expectedTables, "BoxKey");
        IReadOnlyList<string> imports = methodExportKeys
            ?? ["Box.Initialize", "Box.Create", "Box.Read"];
        foreach (var exportKey in imports)
        {
            AddImport(expectedTables, exportKey);
        }

        var selectedAliasExportKey = aliasExportKey
            ?? (methodExportKeys is null ? "Box.ReadAlias" : null);
        if (selectedAliasExportKey is not null)
        {
            var aliasTarget = aliasTargetExportKey
                ?? imports.LastOrDefault()
                ?? throw new InvalidOperationException(
                    "A method alias requires a target method ExportKey.");
            if (!expectedTables.Exports.TryGetDefinitionForKey(1,
                    aliasTarget, out var methodDefinition))
            {
                throw new InvalidOperationException(
                    $"The expected class artifact does not export '{aliasTarget}'.");
            }

            importRows.Add(new TrRmImportRow(1,
                AddString(selectedAliasExportKey),
                methodDefinition));
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        var strings = new TrRmStringTableReader(stringBytes, textRows.Count);
        if (!index.TryGetSection(0x0001, out var moduleSection,
                out var moduleCount)
            || !index.TryGetSection(0x0016, out var methodSection,
                out var methodCount))
        {
            throw new InvalidOperationException(
                "The expected class artifact is missing its declaration tables.");
        }

        var moduleBytes = moduleSection.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(0, 4),
            checked((uint)consumerIdentityRow));
        var sourceFileCount = index.TryGetSection(0x001F, out _, out var count)
            ? count : 0;
        var featureCount = index.TryGetSection(0x0004, out _, out count)
            ? count : 0;
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, 1, expectedTables.TypeDefs.Count, methodCount,
            sourceFileCount, featureCount);

        var methodBytes = methodSection.ToArray();
        for (var row = 0; row < methodCount; row++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                methodBytes.AsSpan((row * 28) + 24, sizeof(uint)), 0);
        }

        var methodReader = new TrRmMethodDefTableReader(methodBytes,
            methodCount, strings, modules, expectedTables.TypeDefs,
            expectedTables.Signatures, expectedTables.Contracts.Count, 0,
            schema);
        var (targetRequirement, runtimePolicy)
            = providerTables.Modules.GetRequirements(1);
        var moduleRefBytes = TrRmModuleRefTableWriter.Encode([
            new TrRmModuleRefRow(providerIdentityRow, targetRequirement,
                runtimePolicy),
        ], strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes, 1,
            strings, modules);
        var importBytes = TrRmImportTableWriter.Encode(importRows, strings,
            moduleRefs, expectedTables.TypeDefs, methodReader,
            expectedTables.Properties, expectedTables.Interfaces,
            expectedTables.AssociatedTypeDefs, expectedTables.Fields,
            expectedTables.Features, modules, schema,
            expectedTables.AssociatedConstDefs);
        var exportBytes = Array.Empty<byte>();
        var exportCount = 0;
        if (rootClassExportKey is not null)
        {
            if (!expectedTables.Exports.TryGetDefinitionForKey(1,
                    rootClassExportKey, out var rootClassDefinition)
                || (rootClassDefinition >> 24) != 0x13)
            {
                throw new InvalidOperationException(
                    $"The expected class artifact does not export root class '{rootClassExportKey}'.");
            }

            exportBytes = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(exportBytes,
                rootClassDefinition);
            BinaryPrimitives.WriteUInt32LittleEndian(
                exportBytes.AsSpan(sizeof(uint)),
                checked((uint)AddString(rootClassExportKey)));
            exportCount = 1;
            stringBytes = TrRmStringTableWriter.EncodeTextRows(textRows);
        }

        var sections = new List<TrRmSection>(index.SectionCount + 2);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (kind is 0x001D or 0x0020)
            {
                continue;
            }

            var sectionBytes = kind switch {
                0x0001 => moduleBytes,
                0x0002 => stringBytes,
                0x0016 => methodBytes,
                _ => bytes.ToArray(),
            };
            var sectionRecordCount = kind == 0x0002
                ? textRows.Count : recordCount;
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)sectionRecordCount), sectionBytes));
        }

        sections.Add(new TrRmSection(0x0010, 0, 1, moduleRefBytes));
        sections.Add(new TrRmSection(0x001C, 0,
            checked((uint)importRows.Count), importBytes));
        if (exportCount != 0)
        {
            sections.Add(new TrRmSection(0x001D, 0,
                checked((uint)exportCount), exportBytes));
        }
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections, schema);
        _ = TrRmStructuralMetadataValidator.ValidateC1AndGetExecutionTables(
            TrRmArtifactIndex
                .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                    result));
        return result;

        int AddString(string text)
        {
            textRows.Add((text, 1));
            return textRows.Count;
        }

        void AddImport(
            TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
            string exportKey)
        {
            if (!tables.Exports.TryGetDefinitionForKey(1, exportKey,
                    out var expectedDefinition))
            {
                throw new InvalidOperationException(
                    $"The expected class artifact does not export '{exportKey}'.");
            }

            importRows.Add(new TrRmImportRow(1, AddString(exportKey),
                expectedDefinition));
        }
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
                .Where(static member =>
                    member.Kind == SyntaxKind.FieldDeclaration)
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
