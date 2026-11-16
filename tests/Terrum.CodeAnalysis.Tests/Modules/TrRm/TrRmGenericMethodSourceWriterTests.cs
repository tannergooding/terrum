// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmGenericMethodSourceWriterTests
{
    private const string GenericMethodCallsSchemaIdentity
        = "trrm/1-draft-generic-method-calls-parameter-labels-1";

    [Test]
    public void RejectsSourceGenericConstantFormalsWithoutAssignedCarrier()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Echo<const int32 Marker>(int32 value) {
                return value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var exports = new[] {
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                "echo.api/v1"),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "constant-generic.provider",
                GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), exports));
    }

    [Test]
    public void EmitsOpenTypeGenericMethodWithExplicitExportAliases()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Echo(int32 value) { return value; } "
            + "public T Identity<T>(T value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var exports = new TrRmSourceGenericMethodExport[] {
            new(program, definition.SourceInputOrdinal, definition.Span,
                "identity.api/v1"),
            new(program, definition.SourceInputOrdinal, definition.Span,
                "identity.compat/v1"),
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "identity.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);

        var (methodCount, methodBytes) = GetSection(artifact, 0x0016);
        Assert.That(methodCount, Is.EqualTo(2));
        Assert.That(ReadUInt32(methodBytes, 12), Is.EqualTo(2));
        var method = methodBytes.AsSpan(28, 28);
        Assert.That(ReadUInt32(method, 12), Is.EqualTo(2));
        Assert.That(ReadUInt32(method, 24), Is.EqualTo(2));

        var (signatureCount, signatureBytes) = GetSection(artifact, 0x0017);
        Assert.That(signatureCount, Is.EqualTo(2));
        var signature = signatureBytes.AsSpan(24, 24);
        Assert.That(ReadUInt32(signature, 12), Is.EqualTo(1));
        Assert.That(ReadUInt32(signature, 16), Is.EqualTo(1));
        Assert.That(ReadUInt32(signature, 20), Is.EqualTo(1));

        var (genericParamCount, genericParamBytes) =
            GetSection(artifact, 0x0019);
        Assert.That(genericParamCount, Is.EqualTo(1));
        var genericParam = genericParamBytes.AsSpan(0, 32);
        Assert.That(ReadUInt32(genericParam, 0), Is.EqualTo(0x1600_0002));
        Assert.That(ReadUInt32(genericParam, 4), Is.Zero);
        Assert.That(genericParam[20], Is.EqualTo(1));
        Assert.That(genericParam[21], Is.EqualTo(1));
        Assert.That(genericParam[22], Is.EqualTo(1));
        Assert.That(genericParam[23], Is.Zero);

        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var typeSpecReader = new TrRmTypeSpecTableReader(typeSpecBytes,
            checked((int)typeSpecCount), 0, 0,
            checked((int)genericParamCount));
        var formalTypeSpecRef = ReadUInt32(signature, 0);
        var (_, parameterBytes) = GetSection(artifact, 0x0018);
        Assert.That(ReadUInt32(parameterBytes, 24),
            Is.EqualTo(formalTypeSpecRef));
        var formalTypeSpec = typeSpecReader.GetRow(
            checked((int)formalTypeSpecRef));
        Assert.That(formalTypeSpec[0], Is.EqualTo(0x04));
        Assert.That(ReadUInt32(formalTypeSpec, 2), Is.EqualTo(1));

        var (stringCount, stringBytes) = GetSection(artifact, 0x0002);
        var strings = new TrRmStringTableReader(stringBytes,
            checked((int)stringCount));
        Assert.That(strings.DecodeName(
            checked((int)ReadUInt32(method, 4))), Is.EqualTo("Identity"));
        Assert.That(strings.DecodeName(
            checked((int)ReadUInt32(genericParam, 8))), Is.EqualTo("T"));

        var (labelCount, labelBytes) = GetSection(artifact, 0x0066);
        Assert.That(labelCount, Is.EqualTo(1));
        Assert.That(ReadUInt32(labelBytes, 0), Is.EqualTo(2));
        Assert.That(ReadUInt32(labelBytes, 4), Is.Zero);
        Assert.That(strings.DecodeName(
            checked((int)ReadUInt32(labelBytes, 8))), Is.EqualTo("value"));
        var missingLabelFeature = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            missingLabelFeature.AsSpan(
                GetSectionOffset(artifact, 0x0004) + 8, sizeof(uint)), 23);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForGenericMethodCallsParameterLabelsDraft(
                            missingLabelFeature)));

        var (exportCount, exportBytes) = GetSection(artifact, 0x001D);
        Assert.That(exportCount, Is.EqualTo(3));
        for (var index = 1; index < exportCount; index++)
        {
            var export = exportBytes.AsSpan(
                checked((int)index * 8), 8);
            Assert.That(ReadUInt32(export, 0), Is.EqualTo(0x1600_0002));
        }
        Assert.That(strings.DecodeName(checked((int)ReadUInt32(
            exportBytes, 12))), Is.EqualTo("identity.api/v1"));
        Assert.That(strings.DecodeName(checked((int)ReadUInt32(
            exportBytes, 20))), Is.EqualTo("identity.compat/v1"));

        var (featureCount, featureBytes) = GetSection(artifact, 0x0004);
        Assert.That(featureCount, Is.EqualTo(2));
        Assert.That(ReadUInt32(featureBytes, 0), Is.EqualTo(23));
        Assert.That(ReadUInt32(featureBytes, 4), Is.EqualTo(1));
        Assert.That(ReadUInt32(featureBytes, 8), Is.EqualTo(26));
        Assert.That(ReadUInt32(featureBytes, 12), Is.EqualTo(1));
        var (bodyCount, bodyBytes) = GetSection(artifact, 0x0020);
        Assert.That(bodyCount, Is.EqualTo(2));
        var bodyIndex = new TrRmVariableTableIndex(bodyBytes,
            checked((int)bodyCount), 1, 1);
        var genericBody = bodyIndex.GetRow(2);
        Assert.That(genericBody[0], Is.EqualTo(0x04));
        Assert.That(genericBody[1], Is.EqualTo(6));
        Assert.That(ReadUInt32(genericBody, 2), Is.EqualTo(2));
        Assert.That(genericBody[26], Is.Zero);
        Assert.That(genericBody[27], Is.EqualTo(0x83));
        Assert.That(genericBody[28], Is.EqualTo(1));
    }

    [Test]
    public void EmitsAndReconstructsGenericSpanTypeArguments()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 TypeOnly<T>(int32 value) { return value; }
                public int32 UseRead<T>(int32 value) {
                    return TypeOnly<Span<T>>(value);
                }
                public int32 UseConcrete<T>(int32 value) {
                    return TypeOnly<Span<int32>>(value);
                }
                public int32 UseWrite<T>(int32 value)
                    where T : write {
                    return TypeOnly<Span<write T>>(value);
                }
                public int32 UseInitialize<T>(int32 value) {
                    return TypeOnly<Span<Span<init T>>>(value);
                }
                public int32 UseNested<T>(int32 value) {
                    return TypeOnly<Span<Span<T>>>(value);
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));

        var provider = providerCompilation.Program!;
        var definitions = provider.GenericMethodDefinitions;
        var typeOnly = definitions.Single(static definition =>
            definition.Name == "TypeOnly");
        var read = definitions.Single(static definition =>
            definition.Name == "UseRead");
        var concrete = definitions.Single(static definition =>
            definition.Name == "UseConcrete");
        var write = definitions.Single(static definition =>
            definition.Name == "UseWrite");
        var initialize = definitions.Single(static definition =>
            definition.Name == "UseInitialize");
        var nested = definitions.Single(static definition =>
            definition.Name == "UseNested");
        var artifact = TrRmExecutableCompiler.EmitStandard(provider,
            "span.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(provider, typeOnly.SourceInputOrdinal, typeOnly.Span,
                    "span.type-only/v1"),
                new(provider, read.SourceInputOrdinal, read.Span,
                    "span.read/v1"),
                new(provider, concrete.SourceInputOrdinal, concrete.Span,
                    "span.concrete/v1"),
                new(provider, write.SourceInputOrdinal, write.Span,
                    "span.write/v1"),
                new(provider, initialize.SourceInputOrdinal,
                    initialize.Span, "span.initialize/v1"),
                new(provider, nested.SourceInputOrdinal, nested.Span,
                    "span.read-nested/v1"),
            ]);
        var writeTables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(artifact,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft));
        Assert.That(writeTables.Features.TryGetFeature(27, out _), Is.True);
        Assert.That(writeTables.Features.TryGetFeature(24, out _), Is.False);
        var withoutWriteFeature = RemoveFeatureRow(artifact,
            TrRmFeatureTableReader.GenericWriteRequirementsFeatureId,
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(withoutWriteFeature,
                        TrRmArtifactSchema
                            .GenericMethodCallsParameterLabelsDraft)));
        foreach (var missingFeature in new uint[] { 23, 26 })
        {
            var incompleteArtifact = RemoveFeatureRow(artifact,
                missingFeature,
                TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                        new TrRmArtifactIndex(incompleteArtifact,
                            TrRmArtifactSchema
                                .GenericMethodCallsParameterLabelsDraft)));
        }
        var reference
            = TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                artifact, "span.provider");

        static BoundCall GetGenericCall(
            BoundGenericMethodImport import)
        {
            BoundCall? call = null;
            Assert.That(BoundTreeTraversal.VisitExpressions(
                import.ProofFunction.Body, expression =>
                {
                    if (expression is BoundCall {
                            ImportedGenericMethodCall: not null,
                        } importedCall)
                    {
                        call = importedCall;
                    }
                }), Is.True);
            return call!;
        }

        var readImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "span.read/v1");
        var readTypeArgument = GetGenericCall(readImport)
            .ImportedGenericMethodTypeArguments.Single();
        var readSpan = reference.SpanTypes[BoundSpanTypeTypes.Index(
            readTypeArgument)];
        Assert.That(readSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(BoundSpanTypeContract
                .ImportedFormalDefinitionOrdinal
                - readImport.FormalTypeOwnerOrdinal));
        Assert.That(readSpan.ElementFormalOrdinal, Is.Zero);
        Assert.That(readSpan.IsElementWritable, Is.False);

        var concreteImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "span.concrete/v1");
        var concreteSpan = reference.SpanTypes[BoundSpanTypeTypes.Index(
            GetGenericCall(concreteImport)
                .ImportedGenericMethodTypeArguments.Single())];
        Assert.That(concreteSpan.ElementType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(concreteSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(-1));

        var writeImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "span.write/v1");
        var writeTypeArgument = GetGenericCall(writeImport)
            .ImportedGenericMethodTypeArguments.Single();
        var writeSpan = reference.SpanTypes[BoundSpanTypeTypes.Index(
            writeTypeArgument)];
        Assert.That(writeSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(BoundSpanTypeContract
                .ImportedFormalDefinitionOrdinal
                - writeImport.FormalTypeOwnerOrdinal));
        Assert.That(readImport.FormalTypeOwnerOrdinal,
            Is.Not.EqualTo(writeImport.FormalTypeOwnerOrdinal));
        Assert.That(writeSpan.ElementFormalOrdinal, Is.Zero);
        Assert.That(writeSpan.IsElementWritable, Is.True);
        var initializeImport = reference.GenericMethodImports.Single(
            import => import.Origin.ExportKey == "span.initialize/v1");
        var initializeSpan = reference.SpanTypes[
            BoundSpanTypeTypes.Index(GetGenericCall(initializeImport)
                .ImportedGenericMethodTypeArguments.Single())];
        Assert.That(initializeSpan.IsInitialization, Is.False);
        Assert.That(BoundSpanTypeTypes.IsValidType(
            initializeSpan.ElementType, reference.SpanTypes), Is.True);
        Assert.That(reference.SpanTypes[BoundSpanTypeTypes.Index(
                initializeSpan.ElementType)].IsInitialization,
            Is.True);
        var nestedImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "span.read-nested/v1");
        var nestedTypeArgument = GetGenericCall(nestedImport)
            .ImportedGenericMethodTypeArguments.Single();
        var nestedSpan = reference.SpanTypes[
            BoundSpanTypeTypes.Index(nestedTypeArgument)];
        Assert.That(BoundSpanTypeTypes.IsValidType(
            nestedSpan.ElementType,
            reference.SpanTypes), Is.True);
        Assert.That(reference.SpanTypes.Count, Is.GreaterThanOrEqualTo(5));

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Forward<U>(int32 value) {
                    return UseRead<Span<U>>(value);
                }
                mut class Mutable { }
                public int32 ForwardWrite(int32 value) {
                    return UseWrite<write Mutable>(value);
                }
                public int32 ForwardNested<U>(int32 value) {
                    return UseNested<Span<Span<U>>>(value);
                }
                """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        SemanticVerifier.Verify(consumerCompilation.Program!);

        foreach (var name in new[] { "Forward", "ForwardNested" })
        {
            var definition = consumerCompilation.Program!
                .GenericMethodDefinitions.Single(candidate =>
                    candidate.Name == name);
            var function = definition.ProofProgram.Functions[
                definition.FunctionIndex];
            var calls = new List<BoundCall>();
            Assert.That(BoundTreeTraversal.VisitExpressions(function.Body,
                expression =>
                {
                    if (expression is BoundCall {
                            ImportedGenericMethodCall: not null,
                        } call)
                    {
                        calls.Add(call);
                    }
                }), Is.True);
            var call = calls.Single();
            Assert.That(call.ImportedGenericMethodTypeArguments,
                Is.EqualTo(function.GenericMethodProofTypeParameters));
        }
    }

    [Test]
    public void TypeDefWriteRequirementUsesFeature27WithoutGenericMethodFeatures()
    {
        const string genericMethodCallsSchemaIdentity
            = "trrm/1-draft-generic-method-calls-1";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> where T : write { }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        foreach (var (schemaIdentity, schema) in new[] {
            (genericMethodCallsSchemaIdentity,
                TrRmArtifactSchema.GenericMethodCallsDraft),
            (GenericMethodCallsSchemaIdentity,
                TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft),
        })
        {
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                "write.type-provider", schemaIdentity, 0, [
                    new TrRmSourceTypeExport("Example", "I",
                        "example.interface/I") {
                        GenericArity = 1,
                        InterfaceMethodExports = [],
                    },
                ], Array.Empty<TrRmSourceGenericMethodExport>());
            var tables = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(artifact, schema));
            Assert.That(tables.Features.TryGetFeature(27, out _), Is.True);
            Assert.That(tables.Features.TryGetFeature(23, out _), Is.False);
            Assert.That(tables.Features.TryGetFeature(26, out _), Is.False);
            Assert.That(tables.Features.TryGetFeature(24, out _), Is.False);

            var packedArtifact = AddUnrelatedEmptyModule(artifact, schema,
                moveWriteFeature: false);
            var packedTables = TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(packedArtifact, schema));
            Assert.That(packedTables.Modules.Count, Is.EqualTo(2));
            Assert.That(TrRmFeatureManifestValidator.Requires(
                packedTables.Features, packedTables.Modules, 1, 27, schema),
                Is.True);
            Assert.That(TrRmFeatureManifestValidator.Requires(
                packedTables.Features, packedTables.Modules, 2, 27, schema),
                Is.False);

            var misplacedFeature = AddUnrelatedEmptyModule(artifact, schema,
                moveWriteFeature: true);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                        new TrRmArtifactIndex(misplacedFeature, schema)));

            var reference = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                "write.type-provider",
                Array.Empty<TrRmStandardArtifactInput>());
            Assert.That(reference.Interfaces.Single().Formals[0]
                .RequiresWritable, Is.True);

            var withoutWriteFeature = RemoveFeatureRow(artifact,
                TrRmFeatureTableReader.GenericWriteRequirementsFeatureId,
                schema);
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmStructuralMetadataValidator
                    .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                        new TrRmArtifactIndex(withoutWriteFeature, schema)));
        }
    }

    [Test]
    public void GenericInterfaceWriteRequirementChecksConstructedTypeArguments()
    {
        var writable = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public mut class C { }
            public interface I<T> where T : write { }
            public interface Good : I<write C> { }
            """));
        Assert.That(writable.HasErrors, Is.False,
            string.Join(Environment.NewLine, writable.Diagnostics));

        var readonlyArgument = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public class C { }
            public interface I<T> where T : write { }
            public interface Bad : I<C> { }
            """));
        Assert.That(readonlyArgument.HasErrors, Is.True,
            string.Join(Environment.NewLine, readonlyArgument.Diagnostics));
        Assert.That(readonlyArgument.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains(
                "does not satisfy its write requirement",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine, readonlyArgument.Diagnostics));
    }

    [Test]
    public void MethodDefWriteRequirementAddsIndependentFeatures23And27()
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericMethodCallsDraft;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Identity<T>(T value) where T : write {
                return move value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "write.method-provider", "trrm/1-draft-generic-method-calls-1", 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal, definition.Span,
                    "identity.write/v1"),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(artifact, schema));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(27, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(24, out _), Is.False);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                "trrm/1-draft-generic-method-calls-1", artifact),
            "write.method-provider",
            Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(reference.GenericMethodImports.Single().Formals[0]
            .RequiresWritable, Is.True);
        var withoutMethodFeature = RemoveFeatureRow(artifact,
            TrRmFeatureTableReader.GenericMethodCallsFeatureId, schema);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    new TrRmArtifactIndex(withoutMethodFeature, schema)));
    }

    [Test]
    public void RebuildsTypeDefAndMethodDefWriteRangesUnderOneModuleFeature()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> where T : write { }
                public T Identity<T>(T value) where T : write {
                    return move value;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var methodDefinition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "write.combined-provider", GenericMethodCallsSchemaIdentity, 0,
            [
                new TrRmSourceTypeExport("Example", "I",
                    "example.interface/I") {
                    GenericArity = 1,
                    InterfaceMethodExports = [],
                },
            ], [
                new(program, methodDefinition.SourceInputOrdinal,
                    methodDefinition.Span, "example.Identity"),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(27, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(24, out _), Is.False);
        Assert.That(tables.Constraints.Count, Is.EqualTo(2));
        Assert.That(tables.GenericParams.GetConstraintRange(1).Count,
            Is.EqualTo(1));
        Assert.That(tables.GenericParams.GetConstraintRange(2).Count,
            Is.EqualTo(1));

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(GenericMethodCallsSchemaIdentity,
                artifact),
            "write.combined-provider",
            Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(reference.Interfaces.Single().Formals[0]
            .RequiresWritable, Is.True);
        Assert.That(reference.GenericMethodImports.Single().Formals[0]
            .RequiresWritable, Is.True);

        var permutedArtifact = PermuteTwoConstraintRows(artifact,
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
        var permutedTables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(permutedArtifact,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft));
        Assert.That(permutedTables.Constraints.GetPayload(1), Is.EqualTo(2));
        Assert.That(permutedTables.Constraints.GetPayload(2), Is.EqualTo(2));
        var permutedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    GenericMethodCallsSchemaIdentity, permutedArtifact),
                "write.combined-provider",
                Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(permutedReference.Interfaces.Single().Formals[0]
            .RequiresWritable, Is.True);
        Assert.That(permutedReference.GenericMethodImports.Single()
            .Formals[0].RequiresWritable, Is.True);

        var permutedFormalArtifact
            = PermuteTypeAndMethodGenericParamRows(artifact,
                TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
        var permutedFormalTables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(permutedFormalArtifact,
                    TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft));
        Assert.That(permutedFormalTables.GenericParams.GetOwnerDefId(1)
                >> 24,
            Is.EqualTo(0x16u));
        Assert.That(permutedFormalTables.GenericParams.GetOwnerDefId(2)
                >> 24,
            Is.EqualTo(0x13u));
        var permutedFormalReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    GenericMethodCallsSchemaIdentity,
                    permutedFormalArtifact),
                "write.combined-provider",
                Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(permutedFormalReference.Interfaces.Single().Formals[0]
            .RequiresWritable, Is.True);
        Assert.That(permutedFormalReference.GenericMethodImports.Single()
            .Formals[0].RequiresWritable, Is.True);
    }

    [Test]
    public void OwnerlessSpanElementMayShareImportedFormalTypeId()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 UseConcrete<T,
                    U = Span<int32>, V = int32>(int32 value) {
                    return value;
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));

        var program = providerCompilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "span.formal-collision.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal,
                    definition.Span, definition.Name),
            ]);
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(artifact,
                "span.formal-collision.provider");
        var import = reference.GenericMethodImports.Single(candidate =>
            candidate.Origin.ExportKey == "UseConcrete");
        var source = SyntaxTree.Parse("""
            struct Payload { int32 value; }
            int32 Main() {
                return UseConcrete<int32, int32>(42);
            }
            """);
        var consumerCompilation = Compilation.Create(source, reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var payloadIndex = consumerProgram.Structures.IndexOf(
            consumerProgram.Structures.Single(static structure =>
                structure.Name == "Payload"));
        var payloadType = (PrimitiveType)(100 + payloadIndex);
        Assert.That(payloadType,
            Is.EqualTo(import.Formals[0].PlaceholderType));

        var spanTypes = import.SpanTypes.ToArray();
        var spanIndex = Array.FindIndex(spanTypes, static spanType =>
            spanType.ElementFormalDefinitionOrdinal == -1);
        Assert.That(spanIndex, Is.GreaterThanOrEqualTo(0));
        spanTypes[spanIndex] = spanTypes[spanIndex] with {
            ElementType = payloadType,
        };
        var formals = import.Formals.ToBuilder();
        var typeDefault = formals[1].TypeDefault!;
        var defaultSpan = typeDefault.Term;
        var defaultElement = defaultSpan.TypeArguments.Single();
        defaultSpan = defaultSpan with {
            TypeArguments = [
                defaultElement with { ConcreteType = payloadType },
            ],
        };
        formals[1] = formals[1] with {
            TypeDefault = typeDefault with { Term = defaultSpan },
        };
        var remappedImport = new BoundGenericMethodImport(import.Origin,
            import.Signature, formals.ToImmutable(), import.ProofFunction,
            import.FormalTypeOwnerOrdinal, spanTypes);
        remappedImport.CompleteProofFunction(import.ProofFunction,
            import.ProofSupportFunctions, import.FormalTypeOwnerOrdinal);

        var mainIndex = consumerProgram.Functions.IndexOf(
            consumerProgram.Functions.Single(static function =>
                function.Name == "Main"));
        var main = consumerProgram.Functions[mainIndex];
        var remappedMain = BoundTreeTraversal.RewriteExpressions(main,
            expression => expression is BoundCall {
                    ImportedGenericMethodCall:
                        BoundGenericMethodImport target,
                } call
                && target.Origin.ExportKey == "UseConcrete"
                ? call with {
                    ImportedGenericMethodCall = remappedImport,
                }
                : expression);
        var functions = consumerProgram.Functions.SetItem(mainIndex,
            remappedMain);
        SemanticVerifier.Verify(consumerProgram with {
            Functions = functions,
        });
    }

    [Test]
    public void EmitsAndImportsNestedSpanTypeDefaults()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            alias NestedSpan<T> = Span<Span<T>>;
            alias ElementWritableSpan<T> = Span<write T>;
            alias NestedElementWritableSpan<T>
                = ElementWritableSpan<T>;
            alias InitializedSpan<T> = Span<Span<init T>>;
            public int32 Identity<T>(int32 value) { return value; }
            public int32 Use<T, U = NestedSpan<T>>(int32 value) {
                return Identity<U>(value);
            }
            public int32 UseInitialized<T,
                U = InitializedSpan<T>>(int32 value) {
                return Identity<U>(value);
            }
            public int32 UseElementWrite<T,
                U = NestedElementWritableSpan<T>>(int32 value)
                where T : write {
                return Identity<U>(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                "span.default." + definition.Name + "/v1")).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "span.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        var reference
            = TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                artifact, "span.provider");
        var import = reference.GenericMethodImports.Single(candidate =>
            candidate.Origin.ExportKey == "span.default.Use/v1");
        var typeDefault = import.Formals[1].TypeDefault!;

        Assert.That(typeDefault.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(typeDefault.Term.TypeArguments, Has.Length.EqualTo(1));
        Assert.That(typeDefault.Term.TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(typeDefault.Term.TypeArguments[0].TypeArguments[0].Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(typeDefault.Term.TypeArguments[0].TypeArguments[0]
                .FormalOrdinal,
            Is.Zero);
        Assert.That(typeDefault.FormalDependencies,
            Is.EqualTo(new[] { 0 }));
        Assert.That(reference.SpanTypes.Count, Is.GreaterThanOrEqualTo(2));

        var initializedImport = reference.GenericMethodImports.Single(
            candidate => candidate.Origin.ExportKey
                == "span.default.UseInitialized/v1");
        var initializedDefault
            = initializedImport.Formals[1].TypeDefault!.Term;
        Assert.That(initializedDefault.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(initializedDefault.IsInitialization, Is.False);
        Assert.That(initializedDefault.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(initializedDefault.TypeArguments.Single()
                .IsInitialization,
            Is.True);
        Assert.That(initializedDefault.TypeArguments.Single()
                .TypeArguments.Single().FormalOrdinal,
            Is.Zero);

        var elementWriteImport = reference.GenericMethodImports.Single(
            candidate => candidate.Origin.ExportKey
                == "span.default.UseElementWrite/v1");
        var elementWriteDefault
            = elementWriteImport.Formals[1].TypeDefault!.Term;
        Assert.That(elementWriteDefault.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(elementWriteDefault.IsWritable, Is.False);
        Assert.That(elementWriteDefault.IsElementWritable, Is.True);
        Assert.That(elementWriteDefault.TypeArguments.Single().Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Formal));
        Assert.That(elementWriteDefault.TypeArguments.Single()
                .FormalOrdinal,
            Is.Zero);

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Forward<T>(int32 value) where T : write {
                    return UseElementWrite<T, default>(value);
                }
                public int32 ForwardInitialized<T>(int32 value) {
                    return UseInitialized<T, default>(value);
                }
                """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        SemanticVerifier.Verify(consumerProgram);
        var forward = consumerProgram.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var proofBody = forward.ProofProgram.Functions[
            forward.FunctionIndex];
        BoundCall? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(proofBody.Body,
            expression =>
            {
                if (expression is BoundCall {
                        ImportedGenericMethodCall: not null,
                    } call)
                {
                    forwardedCall = call;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        var forwardedSpanType = forwardedCall!
            .ImportedGenericMethodTypeArguments[1];
        var forwardedSpan = forward.ProofProgram.SpanTypes[
            BoundSpanTypeTypes.Index(forwardedSpanType)];
        Assert.That(forwardedSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(forward.DefinitionOrdinal));
        Assert.That(forwardedSpan.ElementFormalOrdinal, Is.Zero);
        Assert.That(forwardedSpan.IsElementWritable, Is.True);

        var forwardInitialized = consumerProgram.GenericMethodDefinitions
            .Single(static definition =>
                definition.Name == "ForwardInitialized");
        var initializedBody = forwardInitialized.ProofProgram.Functions[
            forwardInitialized.FunctionIndex];
        BoundCall? initializedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            initializedBody.Body, expression =>
            {
                if (expression is BoundCall {
                        ImportedGenericMethodCall: not null,
                    } call)
                {
                    initializedCall = call;
                }
            }), Is.True);
        Assert.That(initializedCall, Is.Not.Null);
        var initializedActual = initializedCall!
            .ImportedGenericMethodTypeArguments[1];
        var initializedSpan = forwardInitialized.ProofProgram.SpanTypes[
            BoundSpanTypeTypes.Index(initializedActual)];
        Assert.That(initializedSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(-1));
        var initializedElementType = initializedSpan.ElementType;
        var initializedElement = forwardInitialized.ProofProgram.SpanTypes[
            BoundSpanTypeTypes.Index(initializedElementType)];
        Assert.That(initializedElement.ElementFormalDefinitionOrdinal,
            Is.EqualTo(forwardInitialized.DefinitionOrdinal));
        Assert.That(initializedElement.ElementFormalOrdinal, Is.Zero);
        Assert.That(initializedElement.IsInitialization, Is.True);
    }

    [Test]
    public void MergesSpanFormalOwnersAcrossG1References()
    {
        static CompilationReference CreateReference(string provider,
            string suffix)
        {
            var compilation = Compilation.CreateLibrary(
                SyntaxTree.Parse($$"""
                    public int32 TypeOnly{{suffix}}<T>(int32 value) {
                        return value;
                    }
                    public int32 Use{{suffix}}<T, U = Span<Span<T>>>(
                        int32 value) {
                        return TypeOnly{{suffix}}<Span<T>>(value)
                            + TypeOnly{{suffix}}<U>(value);
                    }
                    """));
            Assert.That(compilation.HasErrors, Is.False,
                string.Join(Environment.NewLine,
                    compilation.Diagnostics));

            var program = compilation.Program!;
            var exports = program.GenericMethodDefinitions.Select(
                definition => new TrRmSourceGenericMethodExport(program,
                    definition.SourceInputOrdinal, definition.Span,
                    provider + "." + definition.Name + "/v1")).ToArray();
            var artifact = TrRmExecutableCompiler.EmitStandard(program,
                provider, GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), exports);
            return TrRmExecutableCompiler
                .CreateGenericMethodCallsDraftReference(artifact,
                    provider);
        }

        var first = CreateReference("SpanFirst", "A");
        var second = CreateReference("SpanSecond", "B");
        var reference = ModuleCompiler.CreateReferenceSetFromReferences(
            [first, second]);
        var firstImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "SpanFirst.UseA/v1");
        var secondImport = reference.GenericMethodImports.Single(import =>
            import.Origin.ExportKey == "SpanSecond.UseB/v1");
        var firstCall = GetSpanActual(firstImport);
        var secondCall = GetSpanActual(secondImport);
        var firstSpan = reference.SpanTypes[
            BoundSpanTypeTypes.Index(firstCall)];
        var secondSpan = reference.SpanTypes[
            BoundSpanTypeTypes.Index(secondCall)];

        Assert.That(firstCall, Is.Not.EqualTo(secondCall));
        Assert.That(firstSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(BoundSpanTypeContract
                .ImportedFormalDefinitionOrdinal
                - firstImport.FormalTypeOwnerOrdinal));
        Assert.That(secondSpan.ElementFormalDefinitionOrdinal,
            Is.EqualTo(BoundSpanTypeContract
                .ImportedFormalDefinitionOrdinal
                - secondImport.FormalTypeOwnerOrdinal));
        Assert.That(firstImport.FormalTypeOwnerOrdinal,
            Is.Not.EqualTo(secondImport.FormalTypeOwnerOrdinal));
        Assert.That(firstImport.Formals[1].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(secondImport.Formals[1].TypeDefault!.Term.Kind,
            Is.EqualTo(BoundGenericMethodTypeTermKind.Span));
        Assert.That(BoundSpanTypeContract.TryCreateTypeIdentityIndex(
            reference.SpanTypes, out _), Is.True);

        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 ForwardA<T>(int32 value) {
                return UseA<T>(value);
            }
            public int32 ForwardB<T>(int32 value) {
                return UseB<T>(value);
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        SemanticVerifier.Verify(consumer.Program!);

        var graphReference = ModuleCompiler.MergeGraphReferences(
            [first, second]);
        Assert.That(BoundSpanTypeContract.TryCreateTypeIdentityIndex(
            graphReference.SpanTypes, out _), Is.True);
        var graphConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 GraphForwardA<T>(int32 value) {
                return UseA<T>(value);
            }
            public int32 GraphForwardB<T>(int32 value) {
                return UseB<T>(value);
            }
            """), graphReference);
        Assert.That(graphConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphConsumer.Diagnostics));
        SemanticVerifier.Verify(graphConsumer.Program!);

        static PrimitiveType GetSpanActual(
            BoundGenericMethodImport import)
        {
            var typeArguments = new List<PrimitiveType>();
            Assert.That(BoundTreeTraversal.VisitExpressions(
                import.ProofFunction.Body, expression =>
                {
                    if (expression is BoundCall {
                            GenericMethodCall:
                                BoundGenericMethodInstantiation call,
                        })
                    {
                        typeArguments.AddRange(call.TypeArguments.Where(
                            BoundSpanTypeTypes.IsType));
                    }
                }), Is.True);
            return typeArguments.Single();
        }
    }

    [TestCase("unknown-qualifier")]
    [TestCase("zero-reference")]
    [TestCase("self-reference")]
    [TestCase("foreign-formal")]
    public void RejectsMalformedSpanTypeSpecs(string corruption)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Identity<T>(int32 value) { return value; }
            public int32 Use<T>(int32 value) {
                return Identity<Span<T>>(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                "span.malformed." + definition.Name + "/v1")).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "span.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        var foreignFormalTypeSpecRef = 0u;
        if (corruption == "foreign-formal")
        {
            foreignFormalTypeSpecRef = FindForeignFormalTypeSpecRef(
                artifact);
        }
        var malformed = RewriteFirstSpanTypeSpec(artifact,
            (row, rowIndex) =>
            {
                if (corruption == "unknown-qualifier")
                {
                    row[1] |= 0x02;
                }
                else if (corruption == "self-reference")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        row.AsSpan(3), checked((uint)rowIndex));
                }
                else if (corruption == "foreign-formal")
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        row.AsSpan(3), foreignFormalTypeSpecRef);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        row.AsSpan(3), 0);
                }
            });

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                malformed, "span.provider"));
    }

    private static uint FindForeignFormalTypeSpecRef(byte[] artifact)
    {
        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var (genericParamCount, genericParamBytes) = GetSection(artifact,
            0x0019);
        var typeSpecRows = new TrRmVariableTableIndex(typeSpecBytes,
            checked((int)typeSpecCount), 2, 1);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            checked((int)typeSpecCount), 0, 0,
            checked((int)genericParamCount));
        uint currentOwner = 0;
        uint foreignReference = 0;
        for (var row = 1; row <= typeSpecRows.Count; row++)
        {
            var typeSpecRow = typeSpecRows.GetRow(row);
            if (typeSpecRow[0] == 0x0C
                && typeSpecRow.Length == 7
                && typeSpecRow[2] == 0x8A)
            {
                var element = typeSpecs.GetTypeSpecRef(
                    ReadUInt32(typeSpecRow, 3));
                if (!element.IsRow || element.Form != 0x04)
                {
                    continue;
                }

                var genericParamOffset = checked((element.Row - 1) * 32);
                currentOwner = ReadUInt32(genericParamBytes,
                    genericParamOffset);
                break;
            }
        }

        for (var row = 1; row <= typeSpecRows.Count; row++)
        {
            var typeSpecRow = typeSpecRows.GetRow(row);
            if (typeSpecRow.Length != 6 || typeSpecRow[0] != 0x04)
            {
                continue;
            }

            var genericParamRow = ReadUInt32(typeSpecRow, 2);
            var genericParamOffset = checked(
                (checked((int)genericParamRow) - 1) * 32);
            var owner = ReadUInt32(genericParamBytes,
                genericParamOffset);
            if (owner != currentOwner)
            {
                foreignReference = checked((uint)row);
                break;
            }
        }

        if (currentOwner == 0 || foreignReference == 0)
        {
            throw new InvalidDataException(
                "The G1 Span artifact has no formal owned by a different MethodDef.");
        }

        return foreignReference;
    }

    [Test]
    public void RejectsSpanValuesInG1MethodSignatures()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Consume<T>(Span<T> values) { return 1; }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "span.provider",
                GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), [
                    new(program, definition.SourceInputOrdinal,
                        definition.Span, "span.consume/v1"),
                ]));
    }

    [Test]
    public void RejectsWritableSpanActualForReadOnlyFormal()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 TypeOnly<T>(int32 value) { return value; }
            public int32 UseRead<T>(int32 value) {
                return TypeOnly<Span<T>>(value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                "span." + definition.Name + "/v1")).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "span.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        var malformed = AddWriteQualifierToFirstSpanFormal(artifact);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                malformed, "span.provider"));
    }

    [TestCase("trrm/1-draft-generic-method-calls-1")]
    [TestCase("trrm/1-draft-constant-generic-methods-1")]
    [TestCase("trrm/1-draft-init-reference-bodies-general-predicate-graphs-generic-method-calls-1")]
    public void SourceMappingsRequireTheExactG1L1Schema(
        string schemaIdentity)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public T Identity<T>(T value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();

        var exception = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "identity.provider",
                schemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), [
                    new(program, definition.SourceInputOrdinal,
                        definition.Span, "identity/v1"),
                ]));

        Assert.That(exception!.ParamName, Is.EqualTo("schemaIdentity"));
    }

    [Test]
    public void StandardReferenceRejectsG1L1ArtifactSelectedAsG1()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public T Identity<T>(T value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "identity.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal, definition.Span,
                    "identity/v1"),
            ]);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-generic-method-calls-1", artifact),
                "identity.provider",
                Array.Empty<TrRmStandardArtifactInput>()));
    }

    [Test]
    public void RetainsFeatureForZeroParameterGenericExportsWithoutLabelRows()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Zero<T>() { return 7; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "zero.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal, definition.Span,
                    "Zero"),
            ]);

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        Assert.That(index.TryGetSection(0x0066, out _, out _), Is.False);
    }

    [Test]
    public void EmptyInterfaceExportDoesNotRequireGenericMethodOrLabelFeatures()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> { }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "empty-interface.provider", GenericMethodCallsSchemaIdentity, 0,
            [
                new TrRmSourceTypeExport("Example", "I",
                    "example.interface/I") {
                    GenericArity = 1,
                    InterfaceMethodExports = [],
                },
            ], Array.Empty<TrRmSourceGenericMethodExport>());

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Interfaces.Count, Is.EqualTo(1));
        Assert.That(tables.InterfaceMethods.Count, Is.Zero);
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.False);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.False);
        Assert.That(tables.MethodParameterLabels.Count, Is.Zero);
        foreach (var feature in new[] {
            TrRmFeatureRow.GenericMethodCallsCandidate,
            TrRmFeatureRow.MethodParameterLabelsCandidate,
            TrRmFeatureRow.GenericWriteRequirementsCandidate,
        })
        {
            var overreportedArtifact = AddFeatureRow(artifact, feature);
            _ = Assert.Throws<InvalidDataException>(() =>
                ValidateGenericMethodArtifact(overreportedArtifact));
        }

        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(artifact,
                "empty-interface.provider");
        Assert.That(reference.Interfaces, Has.Length.EqualTo(1));
        Assert.That(reference.Interfaces[0].Members, Is.Empty);
    }

    [Test]
    public void ZeroParameterInterfaceRequirementRequiresFeatureWithoutLabelRows()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public void Clear();
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var clear = program.Interfaces.Single().Members.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "zero-parameter-interface.provider",
            GenericMethodCallsSchemaIdentity, 0, [
                new TrRmSourceTypeExport("Example", "I",
                    "example.interface/I") {
                    GenericArity = 1,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            clear.SourceInputOrdinal,
                            clear.SourceDeclarationSpan,
                            "example.interface/I.Clear"),
                    ],
                },
            ], Array.Empty<TrRmSourceGenericMethodExport>());

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Interfaces.Count, Is.EqualTo(1));
        Assert.That(tables.InterfaceMethods.Count, Is.EqualTo(1));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.False);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
        Assert.That(tables.MethodParameterLabels.Count, Is.Zero);

        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(artifact,
                "zero-parameter-interface.provider");
        var importedMember = reference.Interfaces.Single().Members.Single();
        Assert.That(importedMember.Parameters, Is.Empty);
        Assert.That(importedMember.ResultType!.PrimitiveType,
            Is.EqualTo(PrimitiveType.Void));
    }

    [Test]
    public void PreservesInterfaceAndGenericMethodLabelsInOneG1L1Artifact()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Read(T value);
                }
                public T Echo<T>(T input) { return input; }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceMember = interfaceDefinition.Members.Single();
        var genericDefinition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "combined.provider", GenericMethodCallsSchemaIdentity, 0, [
                new TrRmSourceTypeExport("Example", "I",
                    "example.interface/I") {
                    GenericArity = 1,
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            interfaceMember.SourceInputOrdinal,
                            interfaceMember.SourceDeclarationSpan,
                            "example.interface/I.Read"),
                    ],
                },
            ], [
                new(program, genericDefinition.SourceInputOrdinal,
                    genericDefinition.Span, "example.Echo"),
            ]);

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
        Assert.That(tables.MethodParameterLabels.Count, Is.EqualTo(2));
        var interfaceMethod = tables.InterfaceMethods.GetMethodDefRow(1);
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(
            interfaceMethod, 0, out var interfaceNameRow), Is.True);
        Assert.That(tables.Strings.DecodeName(interfaceNameRow),
            Is.EqualTo("value"));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "example.Echo",
            out var genericMethod), Is.True);
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(
            checked((int)(genericMethod & 0x00FF_FFFF)), 0,
            out var genericNameRow), Is.True);
        Assert.That(tables.Strings.DecodeName(genericNameRow),
            Is.EqualTo("input"));
        var typedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(GenericMethodCallsSchemaIdentity,
                artifact),
            "combined.provider",
            Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(typedReference.Interfaces.Single().Members.Single()
            .Parameters.Single().Name, Is.EqualTo("value"));
        Assert.That(typedReference.GenericMethodImports,
            Has.Length.EqualTo(1));
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(artifact,
                "combined.provider");
        Assert.That(reference.Interfaces.Single().Members.Single()
            .Parameters.Single().Name, Is.EqualTo("value"));
        Assert.That(reference.GenericMethodImports, Has.Length.EqualTo(1));

        var signatureRow = tables.Methods.GetSignature(interfaceMethod);
        var (signatureCount, signatureBytes)
            = GetSection(artifact, 0x0017);
        var signatureRowSize = signatureBytes.Length
            / checked((int)signatureCount);
        var signatureRowOffset
            = checked((signatureRow - 1) * signatureRowSize);
        var wrongReceiver = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            wrongReceiver.AsSpan(GetSectionOffset(artifact, 0x0017)
                + signatureRowOffset + sizeof(uint), sizeof(uint)),
            tables.Signatures.GetResultTypeSpec(signatureRow));
        _ = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(
                        wrongReceiver));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                wrongReceiver, "combined.provider"));

        var receiverTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            tables.Signatures.GetReceiverTypeSpec(signatureRow));
        var genericTypeSpec = ReadUInt32(
            tables.TypeSpecs.GetRow(receiverTypeSpec.Row), 2);
        var unsupportedResult = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            unsupportedResult.AsSpan(GetSectionOffset(artifact, 0x0017)
                + signatureRowOffset, sizeof(uint)), genericTypeSpec);
        _ = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(
                        unsupportedResult));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                unsupportedResult, "combined.provider"));
    }

    [Test]
    public void RejectsMutableInterfaceRequirementInG1L1()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public mut int32 Read(int32 value);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var member = program.Interfaces.Single().Members.Single();
        var export = new TrRmSourceTypeExport("Example", "I",
            "example.interface/I") {
            GenericArity = 1,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal,
                    member.SourceDeclarationSpan,
                    "example.interface/I.Read"),
            ],
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "mutable-interface.provider", GenericMethodCallsSchemaIdentity,
                0, [export],
                Array.Empty<TrRmSourceGenericMethodExport>()));
    }

    [Test]
    public void BindsExpectedG1L1InterfaceProviderAndRetainsExpectedLabels()
    {
        var typeExportKey = "example.interface/I";
        var readExportKey = "example.interface/I.Read";
        var clearExportKey = "example.interface/I.Clear";
        var providerArtifact = EmitG1InterfaceProvider("interface.provider",
            "T", typeExportKey, readExportKey, clearExportKey);
        var consumerArtifact = CreateExpectedG1InterfaceArtifact(
            providerArtifact, "interface.consumer", "expectedValue",
            typeExportKey, readExportKey, clearExportKey);
        var consumerInput = new TrRmStandardArtifactInput(
            GenericMethodCallsSchemaIdentity, consumerArtifact);
        var providerInput = new TrRmStandardArtifactInput(
            GenericMethodCallsSchemaIdentity, providerArtifact);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            consumerInput, "interface.consumer", [providerInput]);
        var importedInterface = reference.Interfaces.Single();
        Assert.That(importedInterface.ModuleImport!.ProducerIdentity,
            Is.EqualTo("interface.provider"));
        Assert.That(importedInterface.ModuleImport.ExportKey,
            Is.EqualTo(typeExportKey));
        var importedRead = importedInterface.Members.Single(
            static member => member.Name == "Read");
        Assert.That(importedRead.DeclarationKey, Is.EqualTo(readExportKey));
        Assert.That(importedRead.Parameters.Single().Name,
            Is.EqualTo("expectedValue"));
        Assert.That(importedRead.ResultType?.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.TypeParameter));
        Assert.That(importedRead.ResultType?.TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Interface));
        Assert.That(importedInterface.Members.Single(
            static member => member.Name == "Clear").ResultType!.PrimitiveType,
            Is.EqualTo(PrimitiveType.Void));

        var compilation = Compilation.Create(SyntaxTree.Parse("""
            namespace Example {
                struct S : I<S> {
                    int32 Field;
                    public S Read(S actual) { return actual; }
                    public void Clear() { }
                }
            }
            int32 Main() { return 42; }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.Verify(compilation.Program!);

        var incompatibleProviderArtifact = EmitG1InterfaceProvider(
            "interface.provider", "int32", typeExportKey, readExportKey,
            clearExportKey);
        var incompatibleProvider = new TrRmStandardArtifactInput(
            GenericMethodCallsSchemaIdentity, incompatibleProviderArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(consumerInput,
                "interface.consumer", [incompatibleProvider]));
    }

    [Test]
    public void PreservesExpectedInterfaceWriteRequirementAcrossProviderReplacement()
    {
        const string typeExportKey = "example.interface/I";
        const string readExportKey = "example.interface/I.Read";
        const string clearExportKey = "example.interface/I.Clear";
        var noWriteProvider = EmitG1InterfaceProvider("interface.provider",
            "T", typeExportKey, readExportKey, clearExportKey);
        var writeProvider = EmitG1InterfaceProvider("interface.provider",
            "T", typeExportKey, readExportKey, clearExportKey,
            requiresWritable: true);
        var expectedNoWrite = CreateExpectedG1InterfaceArtifact(
            noWriteProvider, "interface.consumer", "expectedValue",
            typeExportKey, readExportKey, clearExportKey);
        var expectedWrite = CreateExpectedG1InterfaceArtifact(writeProvider,
            "interface.consumer", "expectedValue", typeExportKey,
            readExportKey, clearExportKey);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    GenericMethodCallsSchemaIdentity, expectedNoWrite),
                "interface.consumer",
                [new TrRmStandardArtifactInput(
                    GenericMethodCallsSchemaIdentity, writeProvider)]));

        var relaxedProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(
                    GenericMethodCallsSchemaIdentity, expectedWrite),
                "interface.consumer",
                [new TrRmStandardArtifactInput(
                    GenericMethodCallsSchemaIdentity, noWriteProvider)]);
        Assert.That(relaxedProviderReference.Interfaces.Single().Formals[0]
            .RequiresWritable, Is.True);

        var writableConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                namespace Consumer {
                    public interface Good<T>
                        where T : write, Example.I<write T> { }
                }
                """), relaxedProviderReference);
        Assert.That(writableConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                writableConsumer.Diagnostics));

        var readonlyConsumer = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                namespace Consumer {
                    public interface Bad<T> where T : Example.I<T> { }
                }
                """), relaxedProviderReference);
        Assert.That(readonlyConsumer.HasErrors, Is.True,
            string.Join(Environment.NewLine,
                readonlyConsumer.Diagnostics));
        Assert.That(readonlyConsumer.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3005"
            && diagnostic.Message.Contains(
                "does not satisfy its write requirement",
                StringComparison.Ordinal)), Is.True,
            string.Join(Environment.NewLine,
                readonlyConsumer.Diagnostics));
    }

    [Test]
    public void ReadsOpenGenericCallToImportedInterfaceMethod()
    {
        const string typeExportKey = "example.interface/I";
        const string readExportKey = "example.interface/I.Read";
        const string clearExportKey = "example.interface/I.Clear";
        var providerArtifact = EmitG1OpenInterfaceCallProvider(
            "interface.provider", typeExportKey, readExportKey,
            clearExportKey, "ReadVia");
        var consumerArtifact = CreateExpectedG1InterfaceArtifact(
            providerArtifact, "interface.consumer", "expectedValue",
            typeExportKey, readExportKey, clearExportKey,
            requireImportedCallTarget: true);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(GenericMethodCallsSchemaIdentity,
                consumerArtifact),
            "interface.consumer",
            [new TrRmStandardArtifactInput(
                GenericMethodCallsSchemaIdentity, providerArtifact)]);

        var imported = reference.GenericMethodImports.Single();
        var call = (BoundCall)((BoundReturn)imported.ProofFunction.Body
            .Statements.Single()).Value!;
        var identity = call.InterfaceCallIdentity!;
        Assert.That(identity.IsConstraintCall, Is.True);
        Assert.That(identity.RequestedInterface.ModuleImport!
            .ProducerIdentity, Is.EqualTo("interface.provider"));
        Assert.That(identity.Requirement.DeclarationKey,
            Is.EqualTo(readExportKey));
        Assert.That(imported.ProofSupportFunctions, Has.Length.EqualTo(1));
        Assert.That(imported.ProofSupportFunctions[call.Function]
                .InterfaceRequirement,
            Is.EqualTo(identity.Requirement));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public T Invoke<T>(T value) where T : I<T> {
                    return ReadVia<T>(value);
                }
            }
            """), reference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        SemanticVerifier.VerifyLibrary(compilation.Program!);
    }

    [Test]
    public void RejectsOpenInterfaceCallWithoutOwnerOrEvidence()
    {
        var artifact = EmitG1OpenInterfaceCallProvider(
            "interface.provider", "example.interface/I",
            "example.interface/I.Read", "example.interface/I.Clear",
            "ReadVia");
        var targetOffset = GetGenericCallTargetOffset(artifact);

        var missingOwner = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            missingOwner.AsSpan(targetOffset + 8, sizeof(uint)), 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(missingOwner));

        var missingEvidence = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            missingEvidence.AsSpan(targetOffset + 12, sizeof(uint)), 0);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(missingEvidence));
    }

    [Test]
    public void RejectsDirectGenericCallRoleWithInterfaceEvidence()
    {
        var artifact = EmitG1OpenInterfaceCallProvider(
            "interface.provider", "example.interface/I",
            "example.interface/I.Read", "example.interface/I.Clear",
            "ReadVia");
        var wrongRole = artifact.ToArray();
        wrongRole[GetGenericCallTargetOffset(artifact) + 4] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(wrongRole));
    }

    [Test]
    public void PrivateGenericHelperRequiresG1ButNotParameterLabels()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "int32 Helper<T>(int32 value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "private-helper.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(),
            Array.Empty<TrRmSourceGenericMethodExport>());

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.False);
        Assert.That(tables.MethodParameterLabels.Count, Is.Zero);
    }

    [Test]
    public void RetainsDecodedEscapedKeywordParameterLabels()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public T Echo<T>(T @class) { return @class; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "keyword-label.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal, definition.Span,
                    "Echo"),
            ]);

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Echo",
            out var methodDef), Is.True);
        Assert.That(tables.MethodParameterLabels.TryGetNameStringRow(
            checked((int)(methodDef & 0x00FF_FFFF)), 0, out var nameRow),
            Is.True);
        Assert.That(tables.Strings.DecodeName(nameRow), Is.EqualTo("class"));
    }

    [Test]
    public void RejectsDuplicateParameterLabelsInOneDeclaration()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            """
            public int32 Add<T>(int32 first, int32 second) {
                return first + second;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "duplicate-label.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal, definition.Span,
                    "Add"),
            ]);

        var (_, labelBytes) = GetSection(artifact, 0x0066);
        Assert.That(ReadUInt32(labelBytes, 4), Is.Zero);
        Assert.That(ReadUInt32(labelBytes, 16), Is.EqualTo(1));
        var mutated = artifact.ToArray();
        var labelOffset = GetSectionOffset(artifact, 0x0066);
        BinaryPrimitives.WriteUInt32LittleEndian(
            mutated.AsSpan(labelOffset + 20, sizeof(uint)),
            ReadUInt32(labelBytes, 8));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForGenericMethodCallsParameterLabelsDraft(
                            mutated)));

        var (formalCount, formalBytes) = GetSection(artifact, 0x0019);
        Assert.That(formalCount, Is.EqualTo(1));
        var formalNameStringRow = ReadUInt32(formalBytes, 8);
        var formalCollision = artifact.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            formalCollision.AsSpan(labelOffset + 8, sizeof(uint)),
            formalNameStringRow);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmStructuralMetadataValidator
                .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForGenericMethodCallsParameterLabelsDraft(
                            formalCollision)));
    }

    [Test]
    public void RejectsG1L1InterfaceConstraintsWithoutInterfaceExports()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public interface I<T> { }
            public int32 Constrained<T>(T value) where T : I<T> {
                return 7;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var export = new TrRmSourceGenericMethodExport(program,
            definition.SourceInputOrdinal, definition.Span,
            "constrained.api/v1");
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "constrained.provider",
                GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), [export]));
    }

    [Test]
    public void RejectsInterfaceExportsInOriginalG1Schema()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public interface I<T> {
                public T Echo(T value);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.Interfaces.Single();
        var member = definition.Members.Single();
        var export = new TrRmSourceTypeExport("", "I",
            "interface/I") {
            GenericArity = definition.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    member.SourceInputOrdinal, member.SourceDeclarationSpan,
                    "interface/I.Echo"),
            ],
        };
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "interface.provider", "trrm/1-draft-generic-method-calls-1",
                0, [export], Array.Empty<TrRmSourceGenericMethodExport>()));
    }

    [Test]
    public void EmitsAndReconstructsG1L1InterfaceConstraints()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Echo(T value);
                }
                public interface J<T> {
                    public T Wrap(T value);
                }
                public int32 Constrained<T>(T value) where T : I<J<T>> {
                    return 7;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaceI = program.Interfaces.Single(static definition =>
            definition.QualifiedName == "Example.I");
        var memberI = interfaceI.Members.Single();
        var interfaceJ = program.Interfaces.Single(static definition =>
            definition.QualifiedName == "Example.J");
        var memberJ = interfaceJ.Members.Single();
        var interfaceExports = new[] {
            new TrRmSourceTypeExport("Example", "I",
                "example.interface/I") {
                GenericArity = interfaceI.Formals.Length,
                InterfaceMethodExports = [
                    new TrRmSourceInterfaceMethodExport(
                        memberI.SourceInputOrdinal,
                        memberI.SourceDeclarationSpan,
                        "example.interface/I.Echo"),
                ],
            },
            new TrRmSourceTypeExport("Example", "J",
                "example.interface/J") {
                GenericArity = interfaceJ.Formals.Length,
                InterfaceMethodExports = [
                    new TrRmSourceInterfaceMethodExport(
                        memberJ.SourceInputOrdinal,
                        memberJ.SourceDeclarationSpan,
                        "example.interface/J.Wrap"),
                ],
            },
        };
        var genericDefinition = program.GenericMethodDefinitions.Single();
        var methodExport = new TrRmSourceGenericMethodExport(program,
            genericDefinition.SourceInputOrdinal, genericDefinition.Span,
            "example.method/Constrained");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "constraint.provider", GenericMethodCallsSchemaIdentity, 0,
            interfaceExports, [methodExport]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "example.method/Constrained", out var methodDefId), Is.True);
        var methodRow = checked((int)(methodDefId & 0x00FF_FFFF));
        var signatureRow = tables.Methods.GetSignature(methodRow);
        var (formalFirst, formalCount)
            = tables.Signatures.GetGenericRange(signatureRow);
        Assert.That(formalCount, Is.EqualTo(1));
        var (constraintFirst, constraintCount)
            = tables.GenericParams.GetConstraintRange(formalFirst);
        Assert.That(constraintCount, Is.EqualTo(1));
        Assert.That(tables.Constraints.GetKind(constraintFirst),
            Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(
            tables.Constraints.GetPayload(constraintFirst)).Form,
            Is.EqualTo(0x05));

        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(artifact,
                "constraint.provider", "example.method/Constrained");
        var formal = reference.GenericMethodImports.Single().Formals.Single();
        var constraint = formal.InterfaceConstraints.Single();
        Assert.That(constraint.Kind, Is.EqualTo(BoundInterfaceTypeKind.Nominal));
        Assert.That(constraint.NominalName, Is.EqualTo("Example.I"));
        Assert.That(constraint.TypeArguments, Has.Length.EqualTo(1));
        var nestedConstraint = constraint.TypeArguments[0].Type!;
        Assert.That(nestedConstraint.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.Nominal));
        Assert.That(nestedConstraint.NominalName, Is.EqualTo("Example.J"));
        Assert.That(nestedConstraint.TypeArguments, Has.Length.EqualTo(1));
        Assert.That(nestedConstraint.TypeArguments[0].Type!.Kind,
            Is.EqualTo(BoundInterfaceTypeKind.TypeParameter));
        Assert.That(nestedConstraint.TypeArguments[0].Type!.TypeParameterScope,
            Is.EqualTo(BoundInterfaceTypeParameterScope.Member));
        Assert.That(nestedConstraint.TypeArguments[0].Type!
            .TypeParameterOrdinal, Is.Zero);
    }

    [Test]
    public void EmitsImportedWritableFormalInG1L1InterfaceConstraint()
    {
        const string typeExportKey = "example.interface/I";
        var providerArtifact = EmitG1InterfaceProvider(
            "interface.provider", "int32", typeExportKey,
            "example.interface/I.Read", "example.interface/I.Clear",
            requiresWritable: true);
        var providerInput = new TrRmStandardArtifactInput(
            GenericMethodCallsSchemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "interface.provider",
            Array.Empty<TrRmStandardArtifactInput>());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Consumer {
                public int32 Needs<T>(T value)
                    where T : write, Example.I<write T> {
                    return 7;
                }
            }
            """), providerReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var method = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "interface.consumer", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, method.SourceInputOrdinal, method.Span,
                    "consumer.method/Needs"),
            ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                new TrRmArtifactIndex(artifact,
                    TrRmArtifactSchema
                        .GenericMethodCallsParameterLabelsDraft));
        Assert.That(tables.Features.TryGetFeature(27, out _), Is.True);

        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                GenericMethodCallsSchemaIdentity, artifact),
            "interface.consumer", [providerInput]);
        var importedInterface = reference.Interfaces.Single(
            static definition =>
                definition.QualifiedName == "Example.I");
        Assert.That(importedInterface.Formals[0].RequiresWritable, Is.True);
        var importedMethod = reference.GenericMethodImports.Single();
        var constraint = importedMethod.Formals[0]
            .InterfaceConstraints.Single();
        Assert.That(constraint.ModuleImport!.ProducerIdentity,
            Is.EqualTo("interface.provider"));
        Assert.That(constraint.TypeArguments.Single().Type!.IsWritable,
            Is.True);

        var relaxedProvider = EmitG1InterfaceProvider(
            "interface.provider", "int32", typeExportKey,
            "example.interface/I.Read", "example.interface/I.Clear");
        var relaxedReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                GenericMethodCallsSchemaIdentity, artifact),
            "interface.consumer",
            [new TrRmStandardArtifactInput(
                GenericMethodCallsSchemaIdentity, relaxedProvider)]);
        Assert.That(relaxedReference.Interfaces.Single(
                static definition =>
                    definition.QualifiedName == "Example.I")
                .Formals[0].RequiresWritable, Is.True);
    }

    [Test]
    public void EmitsAndReconstructsG1L1OpenInterfaceRequirementCalls()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Echo(T value);
                }
                public T ReadVia<T>(T value) where T : I<T> {
                    return value.via<I<T>>.Echo(value);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceMember = interfaceDefinition.Members.Single();
        var interfaceExport = new TrRmSourceTypeExport("Example", "I",
            "example.interface/I") {
            GenericArity = interfaceDefinition.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    interfaceMember.SourceInputOrdinal,
                    interfaceMember.SourceDeclarationSpan,
                    "example.interface/I.Echo"),
            ],
        };
        var method = program.GenericMethodDefinitions.Single();
        var methodExport = new TrRmSourceGenericMethodExport(program,
            method.SourceInputOrdinal, method.Span,
            "example.method/ReadVia");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "interface.call.provider", GenericMethodCallsSchemaIdentity, 0,
            [interfaceExport], [methodExport]);

        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(artifact,
                "interface.call.provider", "example.method/ReadVia");
        var imported = reference.GenericMethodImports.Single();
        var call = (BoundCall)((BoundReturn)imported.ProofFunction.Body
            .Statements.Single()).Value!;
        Assert.That(call.InterfaceCallIdentity, Is.Not.Null);
        Assert.That(call.InterfaceCallIdentity!.IsConstraintCall, Is.True);
        Assert.That(imported.ProofSupportFunctions, Has.Length.EqualTo(1));
        Assert.That(imported.ProofSupportFunctions[call.Function]
                .InterfaceRequirement,
            Is.EqualTo(call.InterfaceCallIdentity!.Requirement));
        Assert.That(call.Arguments, Has.Length.EqualTo(2));
        Assert.That(imported.Formals.Single().InterfaceConstraints,
            Has.Length.EqualTo(1));
    }

    [Test]
    public void EmitsAndReconstructsG1L1OpenInterfaceRequirementCallsThroughImport()
    {
        const string typeExportKey = "example.interface/I";
        const string readExportKey = "example.interface/I.Read";
        const string clearExportKey = "example.interface/I.Clear";
        var providerArtifact = EmitG1InterfaceProvider(
            "interface.provider", "T", typeExportKey, readExportKey,
            clearExportKey);
        var providerInput = new TrRmStandardArtifactInput(
            GenericMethodCallsSchemaIdentity, providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, "interface.provider",
            Array.Empty<TrRmStandardArtifactInput>());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Consumer {
                public T ReadVia<T>(T actualValue)
                    where T : Example.I<T> {
                    return actualValue.via<Example.I<T>>.Read(actualValue);
                }
            }
            """), providerReference);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var method = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "interface.consumer", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, method.SourceInputOrdinal, method.Span,
                    "consumer.method/ReadVia"),
            ]);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(
                GenericMethodCallsSchemaIdentity, artifact),
            "interface.consumer", [providerInput]);

        var imported = reference.GenericMethodImports.Single();
        var call = (BoundCall)((BoundReturn)imported.ProofFunction.Body
            .Statements.Single()).Value!;
        var identity = call.InterfaceCallIdentity!;
        Assert.That(identity.IsConstraintCall, Is.True);
        Assert.That(identity.RequestedInterface.ModuleImport!
            .ProducerIdentity, Is.EqualTo("interface.provider"));
        Assert.That(identity.Requirement.DeclarationKey,
            Is.EqualTo(readExportKey));
        Assert.That(imported.ProofSupportFunctions, Has.Length.EqualTo(1));
        Assert.That(imported.ProofSupportFunctions[call.Function]
                .InterfaceRequirement,
            Is.EqualTo(identity.Requirement));
    }

    [Test]
    public void RejectsG1L1InterfaceConstraintsWithoutProviderClosure()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Echo(T value);
                }
                public int32 Constrained<T>(T value) where T : I<T> {
                    return 7;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceMember = interfaceDefinition.Members.Single();
        var interfaceExport = new TrRmSourceTypeExport("Example", "I",
            "example.interface/I") {
            GenericArity = interfaceDefinition.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    interfaceMember.SourceInputOrdinal,
                    interfaceMember.SourceDeclarationSpan,
                    "example.interface/I.Echo"),
            ],
        };
        var genericDefinition = program.GenericMethodDefinitions.Single();
        var methodExport = new TrRmSourceGenericMethodExport(program,
            genericDefinition.SourceInputOrdinal, genericDefinition.Span,
            "example.method/Constrained");
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "constraint.provider", GenericMethodCallsSchemaIdentity, 0,
            [interfaceExport], [methodExport]);
        var withoutInterfaceExports = RemoveG1InterfaceExports(artifact);

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                withoutInterfaceExports, "constraint.provider",
                "example.method/Constrained"));
    }

    [Test]
    public void AdmitsG1L1InterfaceWriteRequirementsWithFeature27()
    {
        var artifact = EmitG1InterfaceProvider("constraint.provider",
            "int32", "example.interface/I", "example.interface/I.Read",
            "example.interface/I.Clear", requiresWritable: true);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Features.TryGetFeature(23, out _), Is.False);
        Assert.That(tables.Features.TryGetFeature(26, out _), Is.True);
        Assert.That(tables.Features.TryGetFeature(27, out _), Is.True);

        var reference
            = TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                artifact, "constraint.provider");
        Assert.That(reference.Interfaces.Single().Formals[0]
            .RequiresWritable, Is.True);
    }

    [Test]
    public void RejectsG1L1InterfaceConstraintsOnForeignFormals()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Echo(T value);
                }
                public int32 Constrained<T, U>(T value) where T : I<U> {
                    return 7;
                }
                public int32 Other<V>(V value) {
                    return 8;
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaceDefinition = program.Interfaces.Single();
        var interfaceMember = interfaceDefinition.Members.Single();
        var interfaceExport = new TrRmSourceTypeExport("Example", "I",
            "example.interface/I") {
            GenericArity = interfaceDefinition.Formals.Length,
            InterfaceMethodExports = [
                new TrRmSourceInterfaceMethodExport(
                    interfaceMember.SourceInputOrdinal,
                    interfaceMember.SourceDeclarationSpan,
                    "example.interface/I.Echo"),
            ],
        };
        var constrained = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.EndsWith("Constrained",
                StringComparison.Ordinal));
        var other = program.GenericMethodDefinitions.Single(
            static definition => definition.Name.EndsWith("Other",
                StringComparison.Ordinal));
        var methodExports = new[] {
            new TrRmSourceGenericMethodExport(program,
                constrained.SourceInputOrdinal, constrained.Span,
                "example.method/Constrained"),
            new TrRmSourceGenericMethodExport(program,
                other.SourceInputOrdinal, other.Span, "example.method/Other"),
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "constraint.provider", GenericMethodCallsSchemaIdentity, 0,
            [interfaceExport], methodExports);
        var malformedArtifact = RetargetG1ConstraintToForeignFormal(artifact,
            "example.method/Constrained", "example.method/Other");

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                malformedArtifact, "constraint.provider",
                "example.method/Constrained"));
    }

    [Test]
    public void EmitsSourceGenericInterfaceAndDirectRequirements()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Read(T value);
                    public int32 Count();
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.Interfaces.Single();
        var methodExports = definition.Members.Select(member =>
            new TrRmSourceInterfaceMethodExport(
                member.SourceInputOrdinal, member.SourceDeclarationSpan,
                $"example.interface/I.{member.Name}")).ToArray();
        var typeExport = new TrRmSourceTypeExport("Example", "I",
            "example.interface/I") {
            GenericArity = definition.Formals.Length,
            InterfaceMethodExports = [.. methodExports],
        };
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "interface.provider", GenericMethodCallsSchemaIdentity, 1,
            [typeExport], Array.Empty<TrRmSourceGenericMethodExport>());
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForGenericMethodCallsParameterLabelsDraft(
                    artifact));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefs.GetKind(1), Is.EqualTo(3));
        Assert.That(tables.TypeDefs.GetGenericRange(1),
            Is.EqualTo((1, 1)));
        Assert.That(tables.GenericParams.GetOwnerDefId(1),
            Is.EqualTo(0x1300_0001));
        Assert.That(tables.GenericParams.GetOwnershipDomain(1),
            Is.EqualTo(1));
        Assert.That(tables.GenericParams.GetNullabilityDomain(1),
            Is.EqualTo(1));
        Assert.That(tables.Interfaces.Count, Is.EqualTo(1));
        Assert.That(tables.Interfaces.GetOwnerTypeDef(1), Is.EqualTo(1));
        Assert.That(tables.Interfaces.GetMethodRange(1),
            Is.EqualTo((1, 2)));
        Assert.That(tables.InterfaceMethods.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.GetLayoutRange(1).Count,
            Is.EqualTo(1));
        Assert.That(tables.Layouts.GetLayoutKind(1), Is.Zero);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "example.interface/I", out var interfaceDefId), Is.True);
        Assert.That(interfaceDefId, Is.EqualTo(0x1300_0001));

        for (var ordinal = 0; ordinal < methodExports.Length; ordinal++)
        {
            var interfaceMethodRow = ordinal + 1;
            var methodRow = tables.InterfaceMethods.GetMethodDefRow(
                interfaceMethodRow);
            Assert.That(tables.InterfaceMethods.GetOrdinal(
                interfaceMethodRow), Is.EqualTo(ordinal));
            Assert.That(tables.Methods.GetOwnerType(methodRow),
                Is.EqualTo(1));
            Assert.That(tables.Methods.GetBodyRow(methodRow), Is.Zero);
            Assert.That(tables.Exports.TryGetDefinitionForKey(1,
                methodExports[ordinal].ExportKey, out var methodDefId),
                Is.True);
            Assert.That(methodDefId, Is.EqualTo(
                0x1600_0000u | checked((uint)methodRow)));

            var signatureRow = tables.Methods.GetSignature(methodRow);
            var receiverTypeSpec = tables.Signatures
                .GetReceiverTypeSpec(signatureRow);
            Assert.That(tables.TypeSpecs.GetRow(
                checked((int)receiverTypeSpec))[0], Is.EqualTo(0x05));
        }
    }

    [Test]
    public void RejectsMissingDuplicateAndCrossOwnerInterfaceMappings()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public interface I<T> { public T Read(T value); }
            public interface J<T> { public T Read(T value); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var interfaces = program.Interfaces.ToDictionary(
            static definition => definition.QualifiedName,
            StringComparer.Ordinal);
        var first = interfaces["I"];
        var second = interfaces["J"];
        var firstMember = first.Members.Single();
        var secondMember = second.Members.Single();
        var firstMethodExport = new TrRmSourceInterfaceMethodExport(
            firstMember.SourceInputOrdinal,
            firstMember.SourceDeclarationSpan, "interface/I.Read");
        var firstTypeExport = new TrRmSourceTypeExport("", "I",
            "interface/I") {
            GenericArity = 1,
            InterfaceMethodExports = [firstMethodExport],
        };

        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "interface.provider", GenericMethodCallsSchemaIdentity, 0,
                [firstTypeExport with {
                    InterfaceMethodExports = [],
                }], Array.Empty<TrRmSourceGenericMethodExport>()));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "interface.provider", GenericMethodCallsSchemaIdentity, 0,
                [firstTypeExport with {
                    InterfaceMethodExports = [
                        firstMethodExport with {
                            ExportKey = firstTypeExport.ExportKey,
                        },
                    ],
                }], Array.Empty<TrRmSourceGenericMethodExport>()));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "interface.provider", GenericMethodCallsSchemaIdentity, 0,
                [firstTypeExport with {
                    InterfaceMethodExports = [
                        new TrRmSourceInterfaceMethodExport(
                            secondMember.SourceInputOrdinal,
                            secondMember.SourceDeclarationSpan,
                            "interface/I.wrong-owner"),
                    ],
                }], Array.Empty<TrRmSourceGenericMethodExport>()));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "interface.provider", GenericMethodCallsSchemaIdentity, 0,
                [firstTypeExport with {
                    InterfaceMethodExports = [
                        firstMethodExport with {
                            DeclarationSpan = new TextSpan(
                                firstMethodExport.DeclarationSpan.Start + 1,
                                firstMethodExport.DeclarationSpan.Length - 1),
                            ExportKey = "interface/I.stale-span",
                        },
                    ],
                }], Array.Empty<TrRmSourceGenericMethodExport>()));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "interface.provider", GenericMethodCallsSchemaIdentity, 0,
                [firstTypeExport, firstTypeExport with {
                    ExportKey = "interface/I.duplicate",
                }], Array.Empty<TrRmSourceGenericMethodExport>()));
    }

    [Test]
    public void RequiresExactProgramAnchorAndExplicitPublicKeys()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public T Identity<T>(T value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var staleProgram = program with { };
        var staleExport = new TrRmSourceGenericMethodExport(staleProgram,
            definition.SourceInputOrdinal, definition.Span, "identity/v1");
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "identity.provider",
                GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), [staleExport]));
        var duplicateKey = new TrRmSourceGenericMethodExport(program,
            definition.SourceInputOrdinal, definition.Span, "identity/v1");
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "identity.provider",
                GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(),
                [duplicateKey, duplicateKey]));
        var staleSpan = duplicateKey with {
            DeclarationSpan = new TextSpan(definition.Span.Start + 1,
                definition.Span.Length - 1),
            ExportKey = "identity/stale-span",
        };
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "identity.provider",
                GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), [staleSpan]));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, "identity.provider",
                GenericMethodCallsSchemaIdentity, 0));
    }

    [Test]
    public void RetainsGenericFormalDefaultsAndBlittableConstraints()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Identity<T, U = T>(T value)
                where T : [Blittable] {
                return value;
            }
            """), new CompilationOptions {
            LayoutCoreRegistration = Terrum.CodeAnalysis.Tests.Semantics
                .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "identity.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal,
                    definition.Span, "identity/v1"),
            ]);

        var (genericParamCount, genericParamBytes) =
            GetSection(artifact, 0x0019);
        Assert.That(genericParamCount, Is.EqualTo(2));
        var firstFormal = genericParamBytes.AsSpan(0, 32);
        Assert.That(ReadUInt32(firstFormal, 12), Is.EqualTo(1));
        Assert.That(ReadUInt32(firstFormal, 16), Is.EqualTo(1));
        var secondFormal = genericParamBytes.AsSpan(32, 32);
        Assert.That(ReadUInt32(secondFormal, 28), Is.Not.Zero);

        var (constraintCount, constraintBytes) =
            GetSection(artifact, 0x001A);
        Assert.That(constraintCount, Is.EqualTo(1));
        var constraint = constraintBytes.AsSpan(0, 12);
        Assert.That(ReadUInt32(constraint, 0), Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            constraint.Slice(4, 2)), Is.EqualTo(2));
        Assert.That(ReadUInt32(constraint, 8), Is.EqualTo(3));

        var (featureCount, featureBytes) = GetSection(artifact, 0x0004);
        Assert.That(featureCount, Is.EqualTo(3));
        Assert.That(ReadUInt32(featureBytes, 0), Is.EqualTo(13));
        Assert.That(ReadUInt32(featureBytes, 8), Is.EqualTo(23));
        Assert.That(ReadUInt32(featureBytes, 16), Is.EqualTo(26));

        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var typeSpecReader = new TrRmTypeSpecTableReader(typeSpecBytes,
            checked((int)typeSpecCount), 0, 0,
            checked((int)genericParamCount));
        var defaultTypeSpecRef = ReadUInt32(secondFormal, 28);
        var defaultTypeSpec = typeSpecReader.GetRow(
            checked((int)defaultTypeSpecRef));
        Assert.That(defaultTypeSpec[0], Is.EqualTo(0x04));
        Assert.That(ReadUInt32(defaultTypeSpec, 2), Is.EqualTo(1));
    }

    [Test]
    public void SerializesCheckedScalarOperationsInsideOpenGenericBody()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Increment<T>(int32 value) {
                return value + 1;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "increment.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal,
                    definition.Span, "increment/v1"),
            ]);

        var (bodyCount, bodyBytes) = GetSection(artifact, 0x0020);
        var bodyIndex = new TrRmVariableTableIndex(bodyBytes,
            checked((int)bodyCount), 1, 1);
        var body = bodyIndex.GetRow(1);
        Assert.That(body.IndexOf((byte)0x15), Is.GreaterThanOrEqualTo(0));
        Assert.That(body[^2], Is.EqualTo(0x83));
        Assert.That(body[^1], Is.EqualTo(3));
    }

    [Test]
    public void SerializesDirectGenericForwardingCallInOpenBody()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Identity<T>(T value) { return value; }
            public T Forward<T>(T value) { return Identity<T>(value); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                definition.Name)).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "forward.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        ValidateGenericMethodArtifact(artifact);

        var (bodyCount, bodyBytes) = GetSection(artifact, 0x0020);
        var bodies = new TrRmVariableTableIndex(bodyBytes,
            checked((int)bodyCount), 1, 1);
        var forwardingBody = new TrRmRegisterBodyReader(
            bodies.GetRow(2), schema:
                TrRmArtifactSchema.GenericMethodCallsDraft);
        Assert.That(forwardingBody.RecordCount, Is.EqualTo(3));
        var target = forwardingBody.GetRecord(1);
        Assert.That(target[0], Is.EqualTo(0x10));
        Assert.That(target[4], Is.EqualTo(1));
        Assert.That(target[5], Is.Zero);
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            target.Slice(6, sizeof(ushort))), Is.Zero);
        Assert.That(ReadUInt32(target, 8), Is.Zero);
        Assert.That(ReadUInt32(target, 12), Is.Zero);
        Assert.That(ReadUInt32(target, 16), Is.EqualTo(1));
        Assert.That(target[20], Is.EqualTo(1));

        var callSite = forwardingBody.GetRecord(2);
        Assert.That(callSite[0], Is.EqualTo(0x02));
        Assert.That(callSite[4], Is.EqualTo(4));
        Assert.That(callSite[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            callSite.Slice(8, sizeof(ushort))), Is.EqualTo(1));
        Assert.That(ReadUInt32(callSite, 12), Is.EqualTo(1));
        Assert.That(forwardingBody.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0x90, 2, 0xB8, 1 }));

        var badCallerFormal = artifact.ToArray();
        var (_, bodySection) = GetSection(artifact, 0x0020);
        var bodyIndexStart = GetSectionOffset(artifact, 0x0020);
        var rowStart = checked(bodyIndexStart
            + (((int)bodyCount + 1) * sizeof(uint))
            + (int)ReadUInt32(bodySection, sizeof(uint)));
        var rowLength = checked((int)(ReadUInt32(bodySection,
            checked((int)bodyCount * sizeof(uint)))
            - ReadUInt32(bodySection, sizeof(uint))));
        var bodyRow = badCallerFormal.AsSpan(rowStart, rowLength);
        var position = 5;
        var mapCounts = new int[5];
        for (var map = 0; map < mapCounts.Length; map++)
        {
            if ((bodyRow[2] & (1 << map)) == 0)
            {
                continue;
            }

            mapCounts[map] = checked((int)ReadUInt32(bodyRow, position));
            position += sizeof(uint);
        }
        var blockCount = checked((int)ReadUInt32(bodyRow, position));
        var recordCount = checked((int)ReadUInt32(bodyRow,
            position + sizeof(uint)));
        position += 3 * sizeof(uint);
        var mapsStart = position;
        var methodMapOffset = mapsStart + (mapCounts[0] * 8)
            + (mapCounts[1] * 4);
        position += (mapCounts[0] * 8) + (mapCounts[1] * 4)
            + (mapCounts[2] * 4) + (mapCounts[3] * 4)
            + (mapCounts[4] * 8) + (blockCount * 12);
        var recordOffsetsStart = position;
        var recordDataStart = recordOffsetsStart
            + ((recordCount + 1) * sizeof(uint));
        var targetRecordOffset = checked(recordDataStart
            + (int)ReadUInt32(bodyRow, recordOffsetsStart + sizeof(uint)));
        var badRole = artifact.ToArray();
        badRole[rowStart + targetRecordOffset + 4] = 2;
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(badRole));
        var (genericParamCount, genericParamBytes)
            = GetSection(artifact, 0x0019);
        var methodMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
            bodyRow.Slice(targetRecordOffset + 6, sizeof(ushort)));
        var targetMethodToken = ReadUInt32(bodyRow,
            methodMapOffset + (methodMapIndex * sizeof(uint)));
        var targetMethodRow = checked((int)(targetMethodToken
            & 0x00FF_FFFF));
        var (methodCount, methodBytes) = GetSection(artifact, 0x0016);
        var callerMethodRow = Enumerable.Range(1,
                checked((int)methodCount))
            .Single(row => ReadUInt32(methodBytes,
                ((row - 1) * 28) + 24) == 2);
        var targetFormalRow = Enumerable.Range(1,
                checked((int)genericParamCount))
            .Single(row => ReadUInt32(genericParamBytes,
                (row - 1) * 32)
                == (0x1600_0000u | (uint)targetMethodRow));
        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var typeSpecs = new TrRmVariableTableIndex(typeSpecBytes,
            checked((int)typeSpecCount), 2, 1);
        var targetFormalTypeSpecRef = 0;
        for (var row = 1; row <= typeSpecs.Count; row++)
        {
            var typeSpec = typeSpecs.GetRow(row);
            if (typeSpec[0] == 0x04
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    typeSpec.Slice(2, sizeof(uint)))
                    == (uint)targetFormalRow)
            {
                targetFormalTypeSpecRef = row;
                break;
            }
        }
        Assert.That(targetFormalTypeSpecRef, Is.GreaterThan(0));
        Assert.That(targetMethodRow, Is.Not.EqualTo(callerMethodRow));
        Assert.That(ReadUInt32(bodyRow, targetRecordOffset + 24),
            Is.Not.EqualTo((uint)targetFormalTypeSpecRef));
        BinaryPrimitives.WriteUInt32LittleEndian(
            bodyRow.Slice(targetRecordOffset + 24, sizeof(uint)),
            checked((uint)targetFormalTypeSpecRef));
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(badCallerFormal));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateGenericMethodCallsDraftReference(
                badCallerFormal, "forward.provider", "Forward"));

        var badCopyMode = artifact.ToArray();
        var callSiteRecordOffset = checked(recordDataStart
            + (int)ReadUInt32(bodyRow,
                recordOffsetsStart + (2 * sizeof(uint))));
        badCopyMode[rowStart + callSiteRecordOffset + 21] = 2;
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(badCopyMode));
    }

    [Test]
    public void RejectsNullableCallerFormalForNonnullableGenericTarget()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Target<T>(T value) { return value; }
            public U Forward<U>(U value) { return Target<U>(value); }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                definition.Name)).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "nullable-forward.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        ValidateGenericMethodArtifact(artifact);

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Forward",
            out var forwardDefinition), Is.True);
        var forwardMethod = checked((int)(
            forwardDefinition & 0x00FF_FFFF));
        var signature = tables.Methods.GetSignature(forwardMethod);
        var (formalFirst, formalCount)
            = tables.Signatures.GetGenericRange(signature);
        Assert.That(formalCount, Is.EqualTo(1));
        Assert.That(tables.GenericParams.GetNullabilityDomain(formalFirst),
            Is.EqualTo(1));

        var nullableCallerFormal = artifact.ToArray();
        var formalOffset = checked(GetSectionOffset(nullableCallerFormal,
            0x0019) + ((formalFirst - 1) * 32) + 22);
        nullableCallerFormal[formalOffset] = 2;
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(nullableCallerFormal));
    }

    [Test]
    public void RejectsNullableBlittableCallerFormalForNonnullableGenericTarget()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Target<T>(T value) { return value; }
            public U Forward<U>(U value) where U : [Blittable] {
                return Target<U>(value);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                definition.Name)).ToArray();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "nullable-blittable-forward.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        ValidateGenericMethodArtifact(artifact);

        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "Forward",
            out var forwardDefinition), Is.True);
        var forwardMethod = checked((int)(
            forwardDefinition & 0x00FF_FFFF));
        var signature = tables.Methods.GetSignature(forwardMethod);
        var (formalFirst, formalCount)
            = tables.Signatures.GetGenericRange(signature);
        Assert.That(formalCount, Is.EqualTo(1));
        Assert.That(tables.GenericParams.GetNullabilityDomain(formalFirst),
            Is.EqualTo(1));

        var nullableBlittableCallerFormal = artifact.ToArray();
        var formalOffset = checked(GetSectionOffset(
            nullableBlittableCallerFormal, 0x0019)
            + ((formalFirst - 1) * 32) + 22);
        nullableBlittableCallerFormal[formalOffset] = 2;
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(nullableBlittableCallerFormal));
    }

    [Test]
    public void RejectsAdditionalLocalsInGenericForwardingBody()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public T Identity<T>(T value) { return value; }
            public T Forward<T>(T value) {
                T result = Identity<T>(value);
                T copy = result;
                return copy;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var exports = program.GenericMethodDefinitions.Select(definition =>
            new TrRmSourceGenericMethodExport(program,
                definition.SourceInputOrdinal, definition.Span,
                definition.Name)).ToArray();
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                "forward.provider", GenericMethodCallsSchemaIdentity, 0,
                Array.Empty<TrRmSourceTypeExport>(), exports));
    }

    [Test]
    public void PreservesGenericCallerUnsafeObligation()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public unsafe T Identity<T>(T value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "identity.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal,
                    definition.Span, "identity/unsafe-v1"),
            ]);

        var (_, methodBytes) = GetSection(artifact, 0x0016);
        Assert.That(ReadUInt32(methodBytes, 16), Is.EqualTo(1));
        Assert.That(ReadUInt32(methodBytes, 20), Is.EqualTo(1));
        var (contractCount, contractBytes) = GetSection(artifact, 0x001B);
        Assert.That(contractCount, Is.EqualTo(1));
        var contractIndex = new TrRmVariableTableIndex(contractBytes,
            checked((int)contractCount), 8, 4);
        var contract = contractIndex.GetRow(1);
        Assert.That(ReadUInt32(contract, 0), Is.EqualTo(0x1600_0001));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            contract.Slice(4, 2)), Is.EqualTo(1));
        Assert.That(ReadUInt32(contract, 8), Is.EqualTo(4));
    }

    [Test]
    public void ExactG1ConsumerValidationRejectsForgedRowsAndMissingFeature()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public T Identity<T>(T value) { return value; }"));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.GenericMethodDefinitions.Single();
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            "identity.provider", GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(program, definition.SourceInputOrdinal,
                    definition.Span, "identity/v1"),
            ]);

        var badFormal = artifact.ToArray();
        var genericParamOffset = GetSectionOffset(badFormal, 0x0019);
        badFormal[genericParamOffset + 23] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(badFormal));

        var missingFeature = artifact.ToArray();
        var featureOffset = GetSectionOffset(missingFeature, 0x0004);
        BinaryPrimitives.WriteUInt32LittleEndian(
            missingFeature.AsSpan(featureOffset, 4), 13);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(missingFeature));
    }

    [Test]
    public void BindsAndExecutesImportedGenericMethodWithoutProviderSource()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(
                "public T Identity<T, U = T>(T value) "
                + "where T : [Blittable] { return value; }"),
            new CompilationOptions {
                LayoutCoreRegistration = Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var providerDefinition = providerProgram.GenericMethodDefinitions
            .Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "identity.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(providerProgram, providerDefinition.SourceInputOrdinal,
                    providerDefinition.Span, "Identity"),
            ]);
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "identity.provider", "Identity");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Identity<int32>(42); }"), reference,
            new CompilationOptions {
                LayoutCoreRegistration = Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var specialization = consumerProgram.Functions.Single(function =>
            function.ImportedGenericMethod is not null);
        Assert.That(specialization.ImportedGenericMethod!.Origin.ProviderIdentity,
            Is.EqualTo("identity.provider"));
        Assert.That(specialization.ImportedGenericMethod.Origin.ExportKey,
            Is.EqualTo("Identity"));
        BoundCall? genericCall = null;
        var main = consumerProgram.Functions.Single(static function =>
            function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundCall call)
                {
                    genericCall = call;
                }
            }), Is.True);
        Assert.That(genericCall!.ImportedGenericMethodCall,
            Is.SameAs(specialization.ImportedGenericMethod));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(consumerProgram,
                "identity.consumer"));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(consumerProgram, ["Main"]));
        Assert.That(NativeJit.Execute(consumerProgram,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void BindsAndExecutesSourceFreeGenericForwardingBody()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            """
            T Identity<T>(T identityArgument) where T : [Blittable] {
                return identityArgument;
            }
            public T Forward<T>(T callerArgument) where T : [Blittable] {
                T result = Identity<T>(callerArgument);
                return result;
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var forward = providerProgram.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "forward.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(providerProgram, forward.SourceInputOrdinal,
                    forward.Span, "Forward"),
            ]);
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "forward.provider", "Forward");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Forward<int32>(callerArgument: 42); }"),
            reference,
            new CompilationOptions {
                LayoutCoreRegistration =
                    Terrum.CodeAnalysis.Tests.Semantics
                        .LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var forwardingSpecialization = consumerProgram.Functions.Single(
            static function => function.ImportedGenericMethod is {
                Origin.ExportKey: "Forward",
            });
        Assert.That(forwardingSpecialization.Variables[0].Name,
            Is.EqualTo("callerArgument"));
        BoundCall? forwardedCall = null;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            forwardingSpecialization.Body, expression =>
            {
                if (expression is BoundCall {
                    ImportedGenericMethodCall: not null,
                } call)
                {
                    forwardedCall = call;
                }
            }), Is.True);
        Assert.That(forwardedCall, Is.Not.Null);
        Assert.That(forwardedCall!.ImportedGenericMethodCall!.Origin
            .IsMethodDefTarget, Is.True);
        Assert.That(forwardedCall.ImportedGenericMethodCall.Origin
            .MethodDefId >> 24, Is.EqualTo(0x16));
        Assert.That(forwardedCall.ImportedGenericMethodTypeArguments,
            Is.EqualTo([PrimitiveType.Int32]));
        var targetSpecialization = consumerProgram.Functions[
            forwardedCall.Function];
        Assert.That(targetSpecialization.ImportedGenericMethod,
            Is.SameAs(forwardedCall.ImportedGenericMethodCall));
        Assert.That(targetSpecialization.ImportedGenericMethodTypeArguments,
            Is.EqualTo(forwardedCall.ImportedGenericMethodTypeArguments));
        Assert.That(NativeJit.Execute(consumerProgram,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void SerializesAndConsumesGenericWriteForwardingObligation()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            """
            int32 Identity<T, U>(int32 left, int32 right)
                where T : write
                where U : [Blittable] {
                return left + right;
            }
            public int32 Forward<T, U>(int32 left, int32 right)
                where T : write
                where U : [Blittable] {
                return Identity<T, U>(left, right);
            }
            """), new CompilationOptions {
            LayoutCoreRegistration =
                Terrum.CodeAnalysis.Tests.Semantics
                    .LayoutCoreRegistrationTestData.Create(),
        });
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var forward = providerProgram.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "write-forward.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(providerProgram, forward.SourceInputOrdinal,
                    forward.Span, "Forward"),
            ]);
        var (bodyCount, bodyBytes) = GetSection(providerArtifact, 0x0020);
        var bodies = new TrRmVariableTableIndex(bodyBytes,
            checked((int)bodyCount), 1, 1);
        var forwardingBody = new TrRmRegisterBodyReader(
            bodies.GetRow(2), schema:
                TrRmArtifactSchema.GenericMethodCallsDraft);
        var callSite = forwardingBody.GetRecord(2);
        Assert.That(ReadUInt32(callSite, 12), Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[25], Is.EqualTo(1));
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "write-forward.provider", "Forward");
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { "
            + "return Forward<write ptr<int32>, int32>(40, 2); }"),
            reference, new CompilationOptions {
                LayoutCoreRegistration =
                    Terrum.CodeAnalysis.Tests.Semantics
                        .LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(consumerCompilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        var badCallerConstraint = providerArtifact.ToArray();
        var forwardingBodyRow = 0;
        for (var bodyRow = 1; bodyRow <= bodies.Count; bodyRow++)
        {
            if (bodies.GetRow(bodyRow)[0] == 0x01)
            {
                forwardingBodyRow = bodyRow;
                break;
            }
        }
        Assert.That(forwardingBodyRow, Is.GreaterThan(0));
        var (methodCount, methodBytes)
            = GetSection(providerArtifact, 0x0016);
        var callerMethodRow = Enumerable.Range(1,
                checked((int)methodCount))
            .Single(row => ReadUInt32(methodBytes,
                ((row - 1) * 28) + 24)
                == (uint)forwardingBodyRow);
        var (genericParamCount, genericParamBytes)
            = GetSection(providerArtifact, 0x0019);
        var callerGenericParamRow = Enumerable.Range(1,
                checked((int)genericParamCount))
            .Single(row => ReadUInt32(genericParamBytes,
                (row - 1) * 32)
                == (0x1600_0000u | (uint)callerMethodRow)
                && ReadUInt32(genericParamBytes,
                    ((row - 1) * 32) + 4) == 0);
        var constraintFirst = checked((int)ReadUInt32(
            genericParamBytes, ((callerGenericParamRow - 1) * 32) + 12));
        var constraintCount = checked((int)ReadUInt32(
            genericParamBytes, ((callerGenericParamRow - 1) * 32) + 16));
        var (_, constraintBytes) = GetSection(providerArtifact, 0x001A);
        var writeConstraintRow = Enumerable.Range(constraintFirst,
                constraintCount)
            .Single(row => BinaryPrimitives.ReadUInt16LittleEndian(
                constraintBytes.AsSpan(((row - 1) * 12) + 4, 2)) == 2
                && ReadUInt32(constraintBytes, ((row - 1) * 12) + 8) == 2);
        BinaryPrimitives.WriteUInt32LittleEndian(
            badCallerConstraint.AsSpan(
                GetSectionOffset(badCallerConstraint, 0x001A)
                    + ((writeConstraintRow - 1) * 12) + 8, 4), 3);
        _ = Assert.Throws<InvalidDataException>(() =>
            ValidateGenericMethodArtifact(badCallerConstraint));
    }

    [Test]
    public void BindsAndExecutesSourceFreeGenericMoveForwardingBody()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            """
            T Identity<T>(T value) where T : unique {
                return move value;
            }
            public T Forward<T>(T value) where T : unique {
                return Identity<T>(move value);
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var forward = providerProgram.GenericMethodDefinitions.Single(
            static definition => definition.Name == "Forward");
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "move-forward.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(providerProgram, forward.SourceInputOrdinal,
                    forward.Span, "Forward"),
            ]);
        var (bodyCount, bodyBytes) = GetSection(providerArtifact, 0x0020);
        var bodies = new TrRmVariableTableIndex(bodyBytes,
            checked((int)bodyCount), 1, 1);
        var forwardingBody = new TrRmRegisterBodyReader(
            bodies.GetRow(2), schema:
                TrRmArtifactSchema.GenericMethodCallsDraft);
        var callSite = forwardingBody.GetRecord(2);
        Assert.That(callSite[21], Is.EqualTo(2));
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "move-forward.provider", "Forward");
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
            }
            int32 Main() {
                Owner source = new Owner(42);
                Owner result = Forward<Owner>(move source);
                return result.Value;
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(consumerCompilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void DecodesAndExecutesImportedGenericScalarBodyWithoutProviderSource()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                public int32 Increment<T>(int32 value) {
                    return value + 1;
                }
                """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var providerDefinition = providerProgram.GenericMethodDefinitions
            .Single();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "increment.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), [
                new(providerProgram, providerDefinition.SourceInputOrdinal,
                    providerDefinition.Span, "Increment"),
            ]);
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "increment.provider", "Increment");

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Increment<bool>(41); }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(consumerCompilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [Test]
    public void RoundTripsTerminalGenericCallForNoReturnTargets()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [DoesNotReturn] public T Stop<T>(T value) {
                return Loop<T>(value);
            }
            [DoesNotReturn] public T Loop<T>(T value) {
                return Stop<T>(value);
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var definitions = providerProgram.GenericMethodDefinitions;
        var definition = definitions.Single(static candidate =>
            candidate.Name == "Stop");
        var proofFunction = definition.ProofProgram.Functions[
            definition.FunctionIndex];
        var proofCall = (BoundCall)((BoundReturn)
            proofFunction.Body.Statements.Single()).Value!;
        Assert.That(proofCall.GenericMethodCall, Is.Not.Null);
        Assert.That(proofFunction.DoesNotReturn, Is.True);
        var loopDefinition = definitions.Single(static candidate =>
            candidate.Name == "Loop");
        Assert.That(loopDefinition.ProofProgram.Functions[
            loopDefinition.FunctionIndex].DoesNotReturn, Is.True);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "no-return.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), definitions.Select(
                candidate => new TrRmSourceGenericMethodExport(providerProgram,
                    candidate.SourceInputOrdinal, candidate.Span,
                    candidate.Name)).ToArray());

        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(
            providerArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(index);
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            Assert.That(tables.Contracts.GetEffectsMask(methodRow) & 2,
                Is.EqualTo(2));
            var body = tables.Bodies.GetRegisterBody(
                tables.Methods.GetBodyRow(methodRow), tables.Contracts.Count);
            Assert.That(body.BlockCount, Is.EqualTo(1));
            Assert.That(body.GetBlockCode(0).ToArray(),
                Is.EqualTo(new byte[] { 0x90, 2 }));
            var callSite = body.GetRecord(2);
            Assert.That(callSite[0], Is.EqualTo(0x02));
            Assert.That(callSite[4], Is.EqualTo(4));
            Assert.That(callSite[5], Is.Zero);
        }

        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "no-return.provider");
        Assert.That(reference.GenericMethodImports, Has.Length.EqualTo(2));
        Assert.That(reference.GenericMethodImports.All(static imported =>
            imported.ProofFunction.DoesNotReturn), Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Stop<int32>(42); }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerProgram = consumerCompilation.Program!;
        var call = (BoundCall)((BoundReturn)consumerProgram.Functions
            .Single(static function => function.Name == "Main")
            .Body.Statements.Single()).Value!;
        Assert.That(consumerProgram.Functions[call.Function]
            .DoesNotReturn, Is.True);
        SemanticVerifier.Verify(consumerProgram);
    }

    [Test]
    public void ImportsAllGenericExportsFromOneG1Module()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            """
            public T Identity<T>(T value) { return value; }
            public int32 Increment<U>(int32 value) {
                return value + 1;
            }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerProgram = providerCompilation.Program!;
        var exports = providerProgram.GenericMethodDefinitions
            .Select(definition =>
                new TrRmSourceGenericMethodExport(providerProgram,
                    definition.SourceInputOrdinal, definition.Span,
                    definition.Name))
            .ToArray();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerProgram, "multi-generic.provider",
            GenericMethodCallsSchemaIdentity, 0,
            Array.Empty<TrRmSourceTypeExport>(), exports);
        var reference = TrRmExecutableCompiler
            .CreateGenericMethodCallsDraftReference(providerArtifact,
                "multi-generic.provider");
        Assert.That(reference.GenericMethodImports, Has.Length.EqualTo(2));

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { return Identity<int32>("
            + "Increment<bool>(41)); }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        Assert.That(NativeJit.Execute(consumerCompilation.Program!,
            NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    private static void ValidateGenericMethodArtifact(byte[] artifact)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(index);
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0 && tables.Bodies.GetRow(bodyRow)[0] == 0x01)
            {
                tables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }
    }

    private static byte[] AddFeatureRow(byte[] artifact,
        TrRmFeatureRow feature)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft;
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var (featureCount, featureBytes) = GetSection(artifact, 0x0004);
        var existingFeatures = new TrRmFeatureTableReader(featureBytes,
            checked((int)featureCount), schema);
        var featureRows = new TrRmFeatureRow[checked(
            (int)featureCount + 1)];
        for (var row = 1; row <= existingFeatures.Count; row++)
        {
            featureRows[row - 1] = existingFeatures.GetFeature(row);
        }
        featureRows[^1] = feature;

        var rewrittenFeatures = TrRmFeatureTableWriter.Encode(featureRows,
            schema);
        var (_, moduleBytes) = GetSection(artifact, 0x0001);
        if (ReadUInt32(moduleBytes, 28) == 0)
        {
            Write(moduleBytes, 24, 1);
        }
        Write(moduleBytes, 28, checked((uint)featureRows.Length));

        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind switch {
                0x0001 => new TrRmSection(kind, flags, 1, moduleBytes),
                0x0004 => new TrRmSection(kind, flags,
                    checked((uint)featureRows.Length), rewrittenFeatures),
                _ => new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(), schema);
    }

    private static byte[] RemoveFeatureRow(byte[] artifact, uint featureId,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var (featureCount, featureBytes) = GetSection(artifact, 0x0004);
        var featureReader = new TrRmFeatureTableReader(featureBytes,
            checked((int)featureCount), schema);
        var featureRows = new List<TrRmFeatureRow>();
        for (var row = 1; row <= featureReader.Count; row++)
        {
            var feature = featureReader.GetFeature(row);
            if (feature.FeatureId != featureId)
            {
                featureRows.Add(feature);
            }
        }

        if (featureRows.Count == featureCount)
        {
            throw new InvalidDataException("The selected Feature row is absent.");
        }

        var (_, moduleBytes) = GetSection(artifact, 0x0001);
        Write(moduleBytes, 24, featureRows.Count == 0 ? 0u : 1u);
        Write(moduleBytes, 28, checked((uint)featureRows.Count));
        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            if (kind == 0x0004 && featureRows.Count == 0)
            {
                continue;
            }

            sections.Add(kind, kind switch {
                0x0001 => new TrRmSection(kind, flags, 1, moduleBytes),
                0x0004 => new TrRmSection(kind, flags,
                    checked((uint)featureRows.Count),
                    TrRmFeatureTableWriter.Encode(featureRows, schema)),
                _ => new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(), schema);
    }

    private static byte[] PermuteTwoConstraintRows(byte[] artifact,
        TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var (genericParamCount, genericParamBytes)
            = GetSection(artifact, 0x0019);
        var (constraintCount, constraintBytes)
            = GetSection(artifact, 0x001A);
        if (genericParamCount != 2 || constraintCount != 2)
        {
            throw new InvalidDataException(
                "The fixture must contain one TypeDef and one MethodDef Write constraint.");
        }

        var firstConstraint = ReadUInt32(genericParamBytes, 12);
        var secondConstraint = ReadUInt32(genericParamBytes, 32 + 12);
        Write(genericParamBytes, 12, secondConstraint);
        Write(genericParamBytes, 32 + 12, firstConstraint);
        var firstOwner = ReadUInt32(constraintBytes, 0);
        var secondOwner = ReadUInt32(constraintBytes, 12);
        Write(constraintBytes, 0, secondOwner);
        Write(constraintBytes, 12, firstOwner);

        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind switch {
                0x0019 => new TrRmSection(kind, flags, genericParamCount,
                    genericParamBytes),
                0x001A => new TrRmSection(kind, flags, constraintCount,
                    constraintBytes),
                _ => new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(), schema);
    }

    private static byte[] PermuteTypeAndMethodGenericParamRows(
        byte[] artifact, TrRmArtifactSchema schema)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var (genericParamCount, genericParamBytes)
            = GetSection(artifact, 0x0019);
        var (constraintCount, constraintBytes)
            = GetSection(artifact, 0x001A);
        var (typeDefCount, typeDefBytes)
            = GetSection(artifact, 0x0013);
        var (interfaceDefCount, interfaceDefBytes)
            = GetSection(artifact, 0x0025);
        var (signatureCount, signatureBytes)
            = GetSection(artifact, 0x0017);
        var (typeSpecCount, typeSpecBytes)
            = GetSection(artifact, 0x0011);
        if (genericParamCount != 2 || constraintCount != 2)
        {
            throw new InvalidDataException(
                "The fixture must contain one TypeDef and one MethodDef formal.");
        }

        var typeFormalRow = 0u;
        var methodFormalRow = 0u;
        for (var row = 1; row <= genericParamCount; row++)
        {
            var owner = ReadUInt32(genericParamBytes,
                (row - 1) * 32);
            switch (owner >> 24)
            {
                case 0x13:
                    typeFormalRow = checked((uint)row);
                    break;
                case 0x16:
                    methodFormalRow = checked((uint)row);
                    break;
                default:
                    throw new InvalidDataException(
                        "The fixture has an unsupported generic formal owner.");
            }
        }
        if (typeFormalRow == 0 || methodFormalRow == 0)
        {
            throw new InvalidDataException(
                "The fixture must contain distinct TypeDef and MethodDef formals.");
        }

        var remappedFormalRows = new Dictionary<uint, uint> {
            [typeFormalRow] = methodFormalRow,
            [methodFormalRow] = typeFormalRow,
        };
        var permutedGenericParamBytes = new byte[genericParamBytes.Length];
        foreach (var (oldRow, newRow) in remappedFormalRows)
        {
            Buffer.BlockCopy(genericParamBytes,
                checked((int)(oldRow - 1) * 32),
                permutedGenericParamBytes,
                checked((int)(newRow - 1) * 32), 32);
        }
        for (var row = 1; row <= constraintCount; row++)
        {
            var offset = (row - 1) * 12;
            var owner = ReadUInt32(constraintBytes, offset);
            if (!remappedFormalRows.TryGetValue(owner, out var remappedOwner))
            {
                throw new InvalidDataException(
                    "The fixture has a Constraint owner outside the permutation.");
            }
            Write(constraintBytes, offset, remappedOwner);
        }

        for (var row = 0; row < typeDefCount; row++)
        {
            var offset = (row * 44) + 20;
            var first = ReadUInt32(typeDefBytes, offset);
            var count = ReadUInt32(typeDefBytes, offset + 4);
            if (count != 0)
            {
                if (!remappedFormalRows.TryGetValue(first,
                        out var remappedFirst))
                {
                    throw new InvalidDataException(
                        "The fixture has a TypeDef formal range outside the permutation.");
                }
                Write(typeDefBytes, offset, remappedFirst);
            }
        }
        for (var row = 0; row < signatureCount; row++)
        {
            var offset = (row * 24) + 16;
            var first = ReadUInt32(signatureBytes, offset);
            var count = ReadUInt32(signatureBytes, offset + 4);
            if (count != 0)
            {
                if (!remappedFormalRows.TryGetValue(first,
                        out var remappedFirst))
                {
                    throw new InvalidDataException(
                        "The fixture has a MethodSig formal range outside the permutation.");
                }
                Write(signatureBytes, offset, remappedFirst);
            }
        }
        for (var row = 0; row < interfaceDefCount; row++)
        {
            var offset = (row * 48) + 4;
            var selfFormal = ReadUInt32(interfaceDefBytes, offset);
            if (selfFormal != 0)
            {
                if (!remappedFormalRows.TryGetValue(selfFormal,
                        out var remappedSelfFormal))
                {
                    throw new InvalidDataException(
                        "The fixture has a self formal outside the permutation.");
                }
                Write(interfaceDefBytes, offset, remappedSelfFormal);
            }
        }

        var typeSpecRowsIndex = new TrRmVariableTableIndex(typeSpecBytes,
            checked((int)typeSpecCount), 2, 1);
        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            typeSpecRowsIndex.Count);
        for (var row = 1; row <= typeSpecRowsIndex.Count; row++)
        {
            var typeSpecRow = typeSpecRowsIndex.GetRow(row).ToArray();
            if (typeSpecRow[0] == 0x04)
            {
                var formalRow = ReadUInt32(typeSpecRow, 2);
                if (!remappedFormalRows.TryGetValue(formalRow,
                        out var remappedFormalRow))
                {
                    throw new InvalidDataException(
                        "The fixture has a TypeSpec formal outside the permutation.");
                }
                Write(typeSpecRow, 2, remappedFormalRow);
            }
            typeSpecRows.Add(typeSpecRow);
        }

        var importCount = index.TryGetSection(0x001C, out _, out var imports)
            ? checked((int)imports) : 0;
        var methodDefCount = index.TryGetSection(0x0016, out _,
            out var methods) ? checked((int)methods) : 0;
        var nativeFunctionPointerCount = index.TryGetSection(0x0052,
            out _, out var nativeFunctionPointers)
            ? checked((int)nativeFunctionPointers) : 0;
        var encodedTypeSpecs = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            checked((int)typeDefCount), importCount,
            checked((int)genericParamCount), nativeFunctionPointerCount,
            schema, methodDefCount);

        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind switch {
                0x0011 => new TrRmSection(kind, flags,
                    checked((uint)typeSpecRows.Count), encodedTypeSpecs),
                0x0013 => new TrRmSection(kind, flags, typeDefCount,
                    typeDefBytes),
                0x0025 => new TrRmSection(kind, flags, interfaceDefCount,
                    interfaceDefBytes),
                0x0017 => new TrRmSection(kind, flags, signatureCount,
                    signatureBytes),
                0x0019 => new TrRmSection(kind, flags, genericParamCount,
                    permutedGenericParamBytes),
                0x001A => new TrRmSection(kind, flags, constraintCount,
                    constraintBytes),
                _ => new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(), schema);
    }

    private static byte[] AddUnrelatedEmptyModule(byte[] artifact,
        TrRmArtifactSchema schema, bool moveWriteFeature)
    {
        var index = new TrRmArtifactIndex(artifact, schema);
        var (stringCount, stringBytes) = GetSection(artifact, 0x0002);
        var strings = new TrRmStringTableReader(stringBytes,
            checked((int)stringCount));
        var names = new List<(string Text, int Encoding)>(strings.Count + 1);
        for (var row = 1; row <= strings.Count; row++)
        {
            names.Add((strings.DecodeNamespace(row), 1));
        }
        names.Add(("write.unrelated", 1));
        var unrelatedIdentityRow = names.Count;
        var rewrittenStrings = TrRmStringTableWriter.EncodeTextRows(names);

        var (moduleCount, originalModuleBytes)
            = GetSection(artifact, 0x0001);
        if (moduleCount != 1)
        {
            throw new InvalidDataException(
                "The source fixture must contain one Module.");
        }
        var (typeDefCount, _) = GetSection(artifact, 0x0013);
        var moduleRefCount = index.TryGetSection(0x0010, out _, out var refs)
            ? refs : 0;
        var methodDefCount = index.TryGetSection(0x0016, out _, out var methods)
            ? methods : 0;
        var sourceFileCount = index.TryGetSection(0x001F, out _,
            out var sourceFiles) ? sourceFiles : 0;
        const int moduleRowSize = 48;
        var moduleBytes = new byte[moduleRowSize * 2];
        Buffer.BlockCopy(originalModuleBytes, 0, moduleBytes, 0,
            moduleRowSize);
        Buffer.BlockCopy(originalModuleBytes, 0, moduleBytes,
            moduleRowSize, moduleRowSize);
        Write(moduleBytes, moduleRowSize, checked((uint)unrelatedIdentityRow));
        Write(moduleBytes, moduleRowSize + 32,
            checked((uint)moduleRefCount + 1));
        Write(moduleBytes, moduleRowSize + 36,
            checked(typeDefCount + 1));
        Write(moduleBytes, moduleRowSize + 40,
            checked((uint)methodDefCount + 1));
        Write(moduleBytes, moduleRowSize + 44,
            checked((uint)sourceFileCount + 1));
        Write(moduleBytes, moduleRowSize + 24,
            moveWriteFeature ? 1u : 0u);
        Write(moduleBytes, moduleRowSize + 28,
            moveWriteFeature ? 1u : 0u);
        if (moveWriteFeature)
        {
            Write(moduleBytes, 24, 0);
            Write(moduleBytes, 28, 0);
        }

        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind switch {
                0x0001 => new TrRmSection(kind, flags, 2, moduleBytes),
                0x0002 => new TrRmSection(kind, flags,
                    checked((uint)names.Count), rewrittenStrings),
                _ => new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(), schema);
    }

    private static byte[] EmitG1InterfaceProvider(string moduleIdentity,
        string readResultType, string typeExportKey, string readExportKey,
        string clearExportKey, bool requiresWritable = false)
    {
        var writeConstraint = requiresWritable
            ? " where T : write" : "";
        var source = $$"""
            namespace Example {
                public interface I<T>{{writeConstraint}} {
                    public {{readResultType}} Read(T actualValue);
                    public void Clear();
                }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var definition = compilation.Program!.Interfaces.Single();
        var read = definition.Members.Single(static member =>
            member.Name == "Read");
        Assert.That(read.ResultType?.Kind, Is.EqualTo(readResultType == "T"
            ? BoundInterfaceTypeKind.TypeParameter
            : BoundInterfaceTypeKind.Primitive));
        var clear = definition.Members.Single(static member =>
            member.Name == "Clear");
        return TrRmExecutableCompiler.EmitStandard(compilation.Program,
            moduleIdentity, GenericMethodCallsSchemaIdentity, 0, [
                new TrRmSourceTypeExport("Example", "I",
                    typeExportKey) {
                    GenericArity = 1,
                    InterfaceMethodExports = [
                        new(read.SourceInputOrdinal,
                            read.SourceDeclarationSpan, readExportKey),
                        new(clear.SourceInputOrdinal,
                            clear.SourceDeclarationSpan, clearExportKey),
                    ],
                },
            ], Array.Empty<TrRmSourceGenericMethodExport>());
    }

    private static byte[] EmitG1OpenInterfaceCallProvider(
        string moduleIdentity, string typeExportKey, string readExportKey,
        string clearExportKey, string methodExportKey)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            namespace Example {
                public interface I<T> {
                    public T Read(T actualValue);
                    public void Clear();
                }
                public T ReadVia<T>(T actualValue) where T : I<T> {
                    return actualValue.via<I<T>>.Read(actualValue);
                }
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        var definition = program.Interfaces.Single();
        var read = definition.Members.Single(static member =>
            member.Name == "Read");
        var clear = definition.Members.Single(static member =>
            member.Name == "Clear");
        var method = program.GenericMethodDefinitions.Single();
        return TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
            GenericMethodCallsSchemaIdentity, 0, [
                new TrRmSourceTypeExport("Example", "I", typeExportKey) {
                    GenericArity = 1,
                    InterfaceMethodExports = [
                        new(read.SourceInputOrdinal,
                            read.SourceDeclarationSpan, readExportKey),
                        new(clear.SourceInputOrdinal,
                            clear.SourceDeclarationSpan, clearExportKey),
                    ],
                },
            ], [
                new TrRmSourceGenericMethodExport(program,
                    method.SourceInputOrdinal, method.Span,
                    methodExportKey),
            ]);
    }

    private static byte[] CreateExpectedG1InterfaceArtifact(
        byte[] providerArtifact, string consumerIdentity,
        string expectedReadLabel, string typeExportKey,
        string readExportKey, string clearExportKey,
        bool requireImportedCallTarget = false)
    {
        // Keep the declaration rows, but replace their exports with consumer expectations.
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForGenericMethodCallsParameterLabelsDraft(
                        providerArtifact));
        if (tables.Modules.Count != 1
            || tables.ModuleRefs.Count != 0
            || tables.Imports.Count != 0
            || !tables.Exports.TryGetDefinitionForKey(1, typeExportKey,
                out var typeDefId)
            || (typeDefId >> 24) != 0x13
            || !tables.Exports.TryGetDefinitionForKey(1, readExportKey,
                out var readMethodDefId)
            || (readMethodDefId >> 24) != 0x16
            || !tables.Exports.TryGetDefinitionForKey(1, clearExportKey,
                out var clearMethodDefId)
            || (clearMethodDefId >> 24) != 0x16)
        {
            throw new InvalidDataException(
                "The G1L1 interface test provider has unexpected declarations.");
        }

        var interfaceDefRow = tables.Interfaces.GetInterfaceDefForTypeDef(
            checked((int)(typeDefId & 0x00FF_FFFF)));
        if (interfaceDefRow == 0)
        {
            throw new InvalidDataException(
                "The G1L1 interface test provider has no InterfaceDef.");
        }

        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var (stringCount, stringBytes) = GetSection(providerArtifact, 0x0002);
        var originalStrings = new TrRmStringTableReader(stringBytes,
            checked((int)stringCount));
        var stringRows = new List<(string Text, int Encoding)>(
            checked((int)stringCount) + 2);
        for (var row = 1; row <= stringCount; row++)
        {
            stringRows.Add((originalStrings.DecodeNamespace(row), 1));
        }

        int GetOrAddString(string value)
        {
            var existing = stringRows.FindIndex(row =>
                StringComparer.Ordinal.Equals(row.Text, value));
            if (existing >= 0)
            {
                return existing + 1;
            }

            stringRows.Add((value, 1));
            return stringRows.Count;
        }

        var providerIdentityRow = GetOrAddString(
            tables.Modules.GetIdentity(1));
        var consumerIdentityRow = GetOrAddString(consumerIdentity);
        var typeExportKeyRow = GetOrAddString(typeExportKey);
        var readExportKeyRow = GetOrAddString(readExportKey);
        var clearExportKeyRow = GetOrAddString(clearExportKey);
        var expectedReadLabelRow = GetOrAddString(expectedReadLabel);
        var moduleBytes = GetSection(providerArtifact, 0x0001).Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(),
            checked((uint)consumerIdentityRow));

        var (labelCount, labelBytes) = GetSection(providerArtifact, 0x0066);
        var readMethodRow = checked((int)(
            readMethodDefId & 0x00FF_FFFF));
        var foundReadLabel = false;
        for (var row = 0; row < labelCount; row++)
        {
            var rowOffset = checked((int)row * 12);
            if (ReadUInt32(labelBytes, rowOffset) == (uint)readMethodRow
                && ReadUInt32(labelBytes, rowOffset + 4) == 0)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    labelBytes.AsSpan(rowOffset + 8, sizeof(uint)),
                    checked((uint)expectedReadLabelRow));
                foundReadLabel = true;
                break;
            }
        }

        if (!foundReadLabel)
        {
            throw new InvalidDataException(
                "The G1L1 interface test provider has no Read label row.");
        }

        var moduleRefBytes = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(moduleRefBytes,
            checked((uint)providerIdentityRow));
        BinaryPrimitives.WriteUInt32LittleEndian(moduleRefBytes.AsSpan(4),
            targetRequirement);
        BinaryPrimitives.WriteUInt32LittleEndian(moduleRefBytes.AsSpan(8),
            runtimePolicy);
        var importRows = new List<(int ExportKeyStringRow, uint ExpectedDefId)> {
            (typeExportKeyRow, typeDefId),
        };
        var readMethodImportRow = importRows.Count + 1;
        importRows.Add((readExportKeyRow, readMethodDefId));
        importRows.Add((clearExportKeyRow, clearMethodDefId));

        var importBytes = new byte[importRows.Count * 12];
        for (var index = 0; index < importRows.Count; index++)
        {
            var offset = index * 12;
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(offset), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(offset + 4),
                checked((uint)importRows[index].ExportKeyStringRow));
            BinaryPrimitives.WriteUInt32LittleEndian(
                importBytes.AsSpan(offset + 8),
                importRows[index].ExpectedDefId);
        }

        var bodyCount = 0u;
        var rewrittenBodySection = Array.Empty<byte>();
        var rewriteBodySection = tables.Bodies.Count != 0;
        var remappedInterfaceTargets = 0;
        if (rewriteBodySection)
        {
            var bodySection = GetSection(providerArtifact, 0x0020);
            bodyCount = bodySection.Count;
            var bodyIndex = new TrRmVariableTableIndex(
                bodySection.Bytes, checked((int)bodyCount), 1, 1);
            var bodies = new List<ReadOnlyMemory<byte>>(bodyIndex.Count);
            for (var bodyRow = 1; bodyRow <= bodyIndex.Count; bodyRow++)
            {
                var bodyBytes = bodyIndex.GetRow(bodyRow).ToArray();
                if (bodyBytes[0] == 0x01)
                {
                    var body = new TrRmRegisterBodyReader(bodyBytes,
                        schema: TrRmArtifactSchema
                            .GenericMethodCallsParameterLabelsDraft);
                    var methodMap = body.MethodMap.ToArray();
                    var bodyChanged = false;
                    for (var recordIndex = 0;
                        recordIndex < body.RecordCount; recordIndex++)
                    {
                        var target = body.GetRecord(recordIndex);
                        if (target[0] != 0x10 || target.Length < 8
                            || target[4] != 2)
                        {
                            continue;
                        }

                        var methodMapIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                target.Slice(6, sizeof(ushort)));
                        var tokenOffset
                            = checked((int)methodMapIndex * sizeof(uint));
                        if (ReadUInt32(methodMap, tokenOffset)
                            != readMethodDefId)
                        {
                            continue;
                        }

                        BinaryPrimitives.WriteUInt32LittleEndian(
                            methodMap.AsSpan(tokenOffset, sizeof(uint)),
                            0x1C00_0000u
                                | checked((uint)readMethodImportRow));
                        bodyChanged = true;
                        remappedInterfaceTargets++;
                    }

                    if (bodyChanged)
                    {
                        var blocks = new List<TrRmRegisterBodyBlock>(
                            body.BlockCount);
                        for (var blockIndex = 0;
                            blockIndex < body.BlockCount; blockIndex++)
                        {
                            var block = body.GetBlock(blockIndex);
                            blocks.Add(new TrRmRegisterBodyBlock(
                                ReadUInt32(block, 0), ReadUInt32(block, 4),
                                BinaryPrimitives.ReadUInt16LittleEndian(
                                    block[8..])));
                        }

                        var records
                            = new List<ReadOnlyMemory<byte>>(
                                body.RecordCount);
                        for (var recordIndex = 0;
                            recordIndex < body.RecordCount; recordIndex++)
                        {
                            records.Add(body.GetRecord(recordIndex)
                                .ToArray());
                        }

                        bodyBytes = TrRmRegisterBodyWriter.Encode(
                            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
                            methodMap, body.FieldMap.ToArray(),
                            body.ConstantMap.ToArray(), blocks, records,
                            body.Code.ToArray(), tables.Contracts.Count,
                            TrRmArtifactSchema
                                .GenericMethodCallsParameterLabelsDraft);
                    }
                }
                bodies.Add(bodyBytes);
            }

            if (remappedInterfaceTargets > 1
                || (requireImportedCallTarget
                    && remappedInterfaceTargets != 1))
            {
                throw new InvalidDataException(
                    "The G1L1 interface test provider has an unexpected imported call target count.");
            }
            rewrittenBodySection
                = TrRmVariableTableWriter.EncodeBodies(bodies);
        }

        var exportSection = GetSection(providerArtifact, 0x001D);
        var retainedExports = new List<byte>();
        for (var row = 0; row < exportSection.Count; row++)
        {
            var offset = checked(row * 8);
            var definitionId = ReadUInt32(exportSection.Bytes, offset);
            var definitionKind = definitionId >> 24;
            var definitionRow = checked((int)(
                definitionId & 0x00FF_FFFF));
            if (definitionKind == 0x13
                || definitionKind == 0x16
                    && tables.Methods.GetOwnerType(definitionRow) != 0)
            {
                continue;
            }

            retainedExports.AddRange(exportSection.Bytes
                .AsSpan(offset, 8).ToArray());
        }

        var sections = new SortedDictionary<ushort, TrRmSection>();
        var sectionCount = checked((int)ReadUInt32(providerArtifact, 12));
        for (var index = 0; index < sectionCount; index++)
        {
            var directoryOffset = checked(32 + (index * 16));
            var kind = BinaryPrimitives.ReadUInt16LittleEndian(
                providerArtifact.AsSpan(directoryOffset, 2));
            if (kind is 0x0010 or 0x001C or 0x001D)
            {
                continue;
            }

            var flags = BinaryPrimitives.ReadUInt16LittleEndian(
                providerArtifact.AsSpan(directoryOffset + 2, 2));
            var recordCount = ReadUInt32(providerArtifact,
                directoryOffset + 4);
            var sectionOffset = checked((int)ReadUInt32(providerArtifact,
                directoryOffset + 8));
            var sectionLength = checked((int)ReadUInt32(providerArtifact,
                directoryOffset + 12));
            var bytes = kind switch {
                0x0001 => moduleBytes,
                0x0002 => TrRmStringTableWriter.EncodeTextRows(stringRows),
                0x0066 => labelBytes,
                0x0020 when rewriteBodySection => rewrittenBodySection,
                _ => providerArtifact.AsSpan(sectionOffset,
                    sectionLength).ToArray(),
            };
            if (kind == 0x0002)
            {
                recordCount = checked((uint)stringRows.Count);
            }
            else if (kind == 0x0020 && rewriteBodySection)
            {
                recordCount = bodyCount;
            }

            sections.Add(kind, new TrRmSection(kind, flags, recordCount,
                bytes));
        }

        sections.Add(0x0010, new TrRmSection(0x0010, 0, 1,
            moduleRefBytes));
        sections.Add(0x001C, new TrRmSection(0x001C, 0,
            checked((uint)importRows.Count), importBytes));
        if (retainedExports.Count != 0)
        {
            sections.Add(0x001D, new TrRmSection(0x001D, 0,
                checked((uint)(retainedExports.Count / 8)),
                retainedExports.ToArray()));
        }
        return TrRmArtifactWriter.Encode(sections.Values.ToArray(),
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
    }

    private static byte[] RemoveG1InterfaceExports(byte[] artifact)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(index);
        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            if (kind != 0x001D)
            {
                sections.Add(kind, new TrRmSection(kind, flags,
                    checked((uint)count), bytes.ToArray()));
                continue;
            }

            var filtered = new List<byte>();
            for (var row = 0; row < bytes.Length / 8; row++)
            {
                var export = bytes.Slice(row * 8, 8);
                var definitionId = ReadUInt32(export, 0);
                var definitionKind = definitionId >> 24;
                var definitionRow = checked((int)(
                    definitionId & 0x00FF_FFFF));
                var isInterfaceDeclaration
                    = definitionKind == 0x13
                    || definitionKind == 0x16
                        && tables.Methods.GetOwnerType(definitionRow) != 0;
                if (!isInterfaceDeclaration)
                {
                    filtered.AddRange(export.ToArray());
                }
            }

            sections.Add(kind, new TrRmSection(kind, flags,
                checked((uint)(filtered.Count / 8)), filtered.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(),
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
    }

    private static byte[] RetargetG1ConstraintToForeignFormal(
        byte[] artifact, string constrainedExportKey,
        string foreignExportKey)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateGenericMethodCallsDraftAndGetExecutionTables(index);
        if (!tables.Exports.TryGetDefinitionForKey(1,
                constrainedExportKey, out var constrainedDefinition)
            || (constrainedDefinition >> 24) != 0x16
            || !tables.Exports.TryGetDefinitionForKey(1, foreignExportKey,
                out var foreignDefinition)
            || (foreignDefinition >> 24) != 0x16)
        {
            throw new InvalidDataException(
                "The G1L1 constraint test has no selected MethodDefs.");
        }

        var constrainedMethod = checked((int)(
            constrainedDefinition & 0x00FF_FFFF));
        var foreignMethod = checked((int)(
            foreignDefinition & 0x00FF_FFFF));
        var constrainedSignature = tables.Methods.GetSignature(
            constrainedMethod);
        var foreignSignature = tables.Methods.GetSignature(foreignMethod);
        var (constrainedFormalFirst, constrainedFormalCount)
            = tables.Signatures.GetGenericRange(constrainedSignature);
        var (foreignFormalFirst, foreignFormalCount)
            = tables.Signatures.GetGenericRange(foreignSignature);
        if (constrainedFormalCount < 1 || foreignFormalCount < 1)
        {
            throw new InvalidDataException(
                "The G1L1 constraint test has no method formals.");
        }

        var (constraintFirst, constraintCount)
            = tables.GenericParams.GetConstraintRange(
                constrainedFormalFirst);
        if (constraintCount != 1
            || tables.Constraints.GetKind(constraintFirst) != 1)
        {
            throw new InvalidDataException(
                "The G1L1 constraint test has an unexpected Constraint range.");
        }

        var constraintTypeSpec = tables.TypeSpecs.GetTypeSpecRef(
            tables.Constraints.GetPayload(constraintFirst));
        if (!constraintTypeSpec.IsRow || constraintTypeSpec.Form != 0x05)
        {
            throw new InvalidDataException(
                "The G1L1 constraint test has no constructed interface instance.");
        }

        var constructedType = tables.TypeSpecs
            .GetRow(constraintTypeSpec.Row).ToArray();
        var argumentTypeSpec = BinaryPrimitives.ReadUInt32LittleEndian(
            constructedType.AsSpan(6));
        var argumentType = tables.TypeSpecs.GetTypeSpecRef(
            argumentTypeSpec);
        if (!argumentType.IsRow || argumentType.Form != 0x04)
        {
            throw new InvalidDataException(
                "The G1L1 constraint test has no formal type argument.");
        }

        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var typeSpecRows = new TrRmVariableTableIndex(typeSpecBytes,
            checked((int)typeSpecCount), 2, 1);
        var foreignFormalTypeSpecRow = checked(typeSpecCount + 1);
        var rows = new List<ReadOnlyMemory<byte>>(
            checked((int)typeSpecCount + 1));
        for (var row = 1; row <= typeSpecRows.Count; row++)
        {
            var bytes = typeSpecRows.GetRow(row).ToArray();
            if (row == constraintTypeSpec.Row)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(6),
                    foreignFormalTypeSpecRow);
            }
            rows.Add(bytes);
        }
        rows.Add(TrRmTypeSpecTableWriter.Reference(0x04,
            checked((uint)foreignFormalFirst)));
        var rewrittenTypeSpecs = TrRmVariableTableWriter.EncodeBlobs(rows);

        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind == 0x0011
                ? new TrRmSection(kind, flags, foreignFormalTypeSpecRow,
                    rewrittenTypeSpecs)
                : new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(),
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
    }

    private static int GetGenericCallTargetOffset(byte[] artifact)
    {
        var (bodyCount, bodyBytes) = GetSection(artifact, 0x0020);
        var bodies = new TrRmVariableTableIndex(bodyBytes,
            checked((int)bodyCount), 1, 1);
        for (var bodyRow = 1; bodyRow <= bodies.Count; bodyRow++)
        {
            var row = bodies.GetRow(bodyRow);
            if (row[0] != 0x01)
            {
                continue;
            }

            var body = new TrRmRegisterBodyReader(row,
                schema: TrRmArtifactSchema
                    .GenericMethodCallsParameterLabelsDraft);
            for (var recordIndex = 0;
                recordIndex < body.RecordCount; recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                if (record[0] != 0x10 || record.Length < 8
                    || record[4] != 2)
                {
                    continue;
                }

                Assert.That(record.Length, Is.EqualTo(20));
                var recordOffset = row.IndexOf(record);
                if (recordOffset < 0
                    || row.LastIndexOf(record) != recordOffset)
                {
                    throw new InvalidDataException(
                        "The G1L1 GenericCallTarget record is not unique in its Body.");
                }
                var artifactOffset = artifact.AsSpan().IndexOf(record);
                if (artifactOffset < 0
                    || artifact.AsSpan().LastIndexOf(record)
                        != artifactOffset)
                {
                    throw new InvalidDataException(
                        "The G1L1 GenericCallTarget record is not unique in the artifact.");
                }
                return artifactOffset;
            }
        }

        throw new AssertionException(
            "The G1L1 Body has no open interface GenericCallTarget.");
    }

    private static byte[] AddWriteQualifierToFirstSpanFormal(byte[] artifact)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var (genericParamCount, _) = GetSection(artifact, 0x0019);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            checked((int)typeSpecCount), 0, 0,
            checked((int)genericParamCount));
        var typeSpecRows = new TrRmVariableTableIndex(typeSpecBytes,
            checked((int)typeSpecCount), 2, 1);
        var rows = new List<ReadOnlyMemory<byte>>(
            checked((int)typeSpecCount));
        var formalRow = -1;
        for (var row = 1; row <= typeSpecRows.Count; row++)
        {
            var bytes = typeSpecRows.GetRow(row).ToArray();
            if (formalRow < 0 && bytes.Length == 7
                && bytes[0] == 0x0C && bytes[2] == 0x8A)
            {
                var argument = typeSpecs.GetTypeSpecRef(
                    ReadUInt32(bytes, 3), allowInitQualifier: true);
                if (argument.IsRow && argument.Form == 0x04
                    && argument.Qualifiers == 0)
                {
                    formalRow = argument.Row;
                }
            }
            rows.Add(bytes);
        }

        if (formalRow < 1)
        {
            throw new InvalidDataException(
                "The G1 Span GenericCall actual has no unqualified formal row.");
        }

        var qualifiedFormal = rows[formalRow - 1].ToArray();
        qualifiedFormal[1] |= 0x01;
        rows[formalRow - 1] = qualifiedFormal;
        var rewrittenTypeSpecs = TrRmVariableTableWriter.EncodeBlobs(rows);
        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind == 0x0011
                ? new TrRmSection(kind, flags, typeSpecCount,
                    rewrittenTypeSpecs)
                : new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(),
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
    }

    private static byte[] RewriteFirstSpanTypeSpec(byte[] artifact,
        Action<byte[], int> rewrite)
    {
        var index = TrRmArtifactIndex
            .ForGenericMethodCallsParameterLabelsDraft(artifact);
        var (typeSpecCount, typeSpecBytes) = GetSection(artifact, 0x0011);
        var typeSpecRows = new TrRmVariableTableIndex(typeSpecBytes,
            checked((int)typeSpecCount), 2, 1);
        var rows = new List<ReadOnlyMemory<byte>>(
            checked((int)typeSpecCount));
        var rewritten = false;
        for (var row = 1; row <= typeSpecRows.Count; row++)
        {
            var bytes = typeSpecRows.GetRow(row).ToArray();
            if (!rewritten && bytes.Length == 7
                && bytes[0] == 0x0C && bytes[2] == 0x8A)
            {
                rewrite(bytes, row);
                rewritten = true;
            }
            rows.Add(bytes);
        }

        if (!rewritten)
        {
            throw new InvalidDataException(
                "The G1 artifact has no Span TypeSpec row.");
        }

        var rewrittenTypeSpecs = TrRmVariableTableWriter.EncodeBlobs(rows);
        var sections = new SortedDictionary<ushort, TrRmSection>();
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var bytes, out var count);
            sections.Add(kind, kind == 0x0011
                ? new TrRmSection(kind, flags, typeSpecCount,
                    rewrittenTypeSpecs)
                : new TrRmSection(kind, flags, checked((uint)count),
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections.Values.ToArray(),
            TrRmArtifactSchema.GenericMethodCallsParameterLabelsDraft);
    }

    private static (uint Count, byte[] Bytes) GetSection(byte[] artifact,
        ushort kind)
    {
        var sectionCount = ReadUInt32(artifact, 12);
        for (var index = 0; index < sectionCount; index++)
        {
            var directoryOffset = checked(32 + ((int)index * 16));
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    artifact.AsSpan(directoryOffset, 2)) != kind)
            {
                continue;
            }

            var count = ReadUInt32(artifact, directoryOffset + 4);
            var offset = checked((int)ReadUInt32(artifact,
                directoryOffset + 8));
            var length = checked((int)ReadUInt32(artifact,
                directoryOffset + 12));
            return (count, artifact.AsSpan(offset, length).ToArray());
        }

        throw new AssertionException(
            $"TRRM section 0x{kind:X4} was not emitted.");
    }

    private static int GetSectionOffset(byte[] artifact, ushort kind)
    {
        var sectionCount = ReadUInt32(artifact, 12);
        for (var index = 0; index < sectionCount; index++)
        {
            var directoryOffset = checked(32 + ((int)index * 16));
            if (BinaryPrimitives.ReadUInt16LittleEndian(
                    artifact.AsSpan(directoryOffset, 2)) == kind)
            {
                return checked((int)ReadUInt32(artifact,
                    directoryOffset + 8));
            }
        }

        throw new AssertionException(
            $"TRRM section 0x{kind:X4} was not emitted.");
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.Slice(offset, sizeof(uint)));
}
