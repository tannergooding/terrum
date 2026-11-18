// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
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
    private const string C1M1Construction2SchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2";
    private const string C1M1Construction2ClassParameterDefaultsSchemaIdentity
        = "trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1-class-mutability-1-construction-2-parameter-labels-1-receiver-access-1-class-parameter-defaults-1";

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
                public int32 Complemented(int32 value) {
                    return ~value;
                }
                public static int32 StaticXor(int32 left, int32 right) {
                    return left ^ right;
                }
                public static int32 Add(int32 left, int32 right) {
                    return left + right;
                }
                public static int32 StaticAddAfterCall(
                    int32 left, int32 right) {
                    return Add(left, right) + right;
                }
                public int32 InstanceStaticAddAfterCall(
                    int32 left, int32 right) {
                    return Add(left, right) + right;
                }
                public static int32 Subtract(int32 left, int32 right) {
                    return right - left;
                }
                public static int32 Multiply(int32 left, int32 right) {
                    return left * right;
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
                public int32 XorAfterCall(int32 left, int32 right) {
                    return Xor(left, right) ^ right;
                }
                public int32 CopyThroughLocal(int32 value) {
                    int32 copy = value;
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
        var complemented = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Complemented"));
        var staticXor = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "StaticXor"));
        var add = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Add"));
        var staticAddAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "StaticAddAfterCall"));
        var instanceStaticAddAfterCall = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name
                    == "InstanceStaticAddAfterCall"));
        var subtract = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Subtract"));
        var multiply = members.Single(static node =>
            node.Kind == SyntaxKind.FunctionDeclaration
            && node.Children.OfType<SyntaxTokenElement>().Any(
                static token => token.Token.Name == "Multiply"));
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
                        new TrRmSourceClassMemberExport(0,
                            complemented.Span, "Box.Complemented"),
                        new TrRmSourceClassMemberExport(0, staticXor.Span,
                            "Box.StaticXor"),
                        new TrRmSourceClassMemberExport(0, add.Span,
                            "Box.Add"),
                        new TrRmSourceClassMemberExport(0,
                            staticAddAfterCall.Span,
                            "Box.StaticAddAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            instanceStaticAddAfterCall.Span,
                            "Box.InstanceStaticAddAfterCall"),
                        new TrRmSourceClassMemberExport(0, subtract.Span,
                            "Box.Subtract"),
                        new TrRmSourceClassMemberExport(0, multiply.Span,
                            "Box.Multiply"),
                        new TrRmSourceClassMemberExport(0, doubleValue.Span,
                            "Box.Double"),
                        new TrRmSourceClassMemberExport(0, negate.Span,
                            "Box.Negate"),
                        new TrRmSourceClassMemberExport(0, complement.Span,
                            "Box.Complement"),
                        new TrRmSourceClassMemberExport(0, copyStatic.Span,
                            "Box.CopyStatic"),
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
                            xorAfterCall.Span, "Box.XorAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            sumAfterCall.Span, "Box.SumAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            lessThanAfterCall.Span, "Box.LessThanAfterCall"),
                        new TrRmSourceClassMemberExport(0,
                            copyThroughLocal.Span, "Box.CopyThroughLocal"),
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
        var addControl = GetArithmeticControl(addBinary);
        Assert.That(addBody.GetBlockCode(0).ToArray(), Is.EqualTo(
            new byte[] {
                0xD5, 0, 0, 0,
                0xD5, 1, 1, 0,
                0xC0, addControl, 0xF0, 0x10,
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
        AssertComparisonBody("Box.LessThan", "LessThan", 1, 2, 0, 1);
        AssertComparisonBody("Box.EqualTo", "EqualTo", 2, 2, 0, 1);
        AssertComparisonBody("Box.LessOrEqual", "LessOrEqual", 3, 2, 0, 1);
        AssertComparisonBody("Box.GreaterThan", "GreaterThan", 4, 3, 0, 1);
        AssertComparisonBody("Box.GreaterOrEqual", "GreaterOrEqual", 6,
            2, 1, 0);
        AssertComparisonBody("Box.NotEqual", "NotEqual", 13, 3, 0, 1);
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
            int32 UseStaticAddAfterCall() {
                return Box.StaticAddAfterCall(2, 3);
            }
            int32 UseInstanceStaticAddAfterCall(Box value) {
                return value.InstanceStaticAddAfterCall(2, 3);
            }
            int32 UseSubtract() { return Box.Subtract(7, 3); }
            int32 UseMultiply() { return Box.Multiply(4, 5); }
            int32 UseDouble() { return Box.Double(4); }
            int32 UseNegate() { return Box.Negate(4); }
            int32 UseComplement() { return Box.Complement(4); }
            int32 UseCopyStatic() { return Box.CopyStatic(42); }
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

    private static byte[] AddMethodExportAlias(byte[] artifact,
        string originalExportKey, string aliasExportKey)
    {
        var schema = TrRmArtifactSchema
            .C1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft;
        var index = TrRmArtifactIndex
            .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                artifact);
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
            TrRmArtifactIndex
                .ForC1M1Construction2ParameterLabelsReceiverAccessClassParameterDefaultsDraft(
                    result));
        return result;

        int AddString(string text)
        {
            textRows.Add((text, 1));
            return textRows.Count;
        }
    }

    private static byte[] CreateExpectedClassMethodImportArtifact(
        byte[] expectedArtifact, byte[] providerArtifact,
        string consumerIdentity, string providerIdentity)
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
        AddImport(expectedTables, "Box.Initialize");
        AddImport(expectedTables, "Box.Create");
        AddImport(expectedTables, "Box.Read");
        if (!expectedTables.Exports.TryGetDefinitionForKey(1, "Box.Read",
                out var readDefinition))
        {
            throw new InvalidOperationException(
                "The expected class artifact does not export 'Box.Read'.");
        }

        importRows.Add(new TrRmImportRow(1, AddString("Box.ReadAlias"),
            readDefinition));

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
