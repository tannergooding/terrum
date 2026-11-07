// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class TrRmLocalAggregateLayoutConsumerTests
{
    private const string SchemaIdentity
        = "trrm/1-draft-local-aggregate-layout-execution-1";
    private const string A2R1SchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-copy-origin-return-coverage-repeat-initialization-1";
    private const string ModuleIdentity = "l1-local-register-body-consumer";

    [Test]
    public void LoadsAndReemitsL1LocalAggregateBodiesFromArtifactBytes()
    {
        var artifact = EmitArtifactBytes();
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            ModuleIdentity, []);

        Assert.That(loaded.EntryPoint, Is.GreaterThanOrEqualTo(0));
        Assert.That(loaded.Structures.Select(static structure =>
            structure.Name), Does.Contain("Packet"));
        Assert.That(loaded.NativeFunctionPointerSignatures, Is.Empty);
        Assert.That(loaded.NativePointerTypes, Is.Empty);
        Assert.That(loaded.StandardMethodProviderClosure, Is.Not.Null);
        Assert.That(loaded.StandardMethodProviderClosure!.Modules.Length,
            Is.EqualTo(1));
        Assert.That(
            loaded.StandardMethodProviderClosure.Modules[0].Program
                .StandardMethodProviderClosure, Is.Null);
        Assert.That(
            loaded.StandardMethodProviderClosure.Artifacts[0].SchemaIdentity,
            Is.EqualTo(SchemaIdentity));

        var constructionFunction = loaded.Functions.Single(function =>
            function.ConstructionHomeIndex >= 0);
        Assert.That(constructionFunction.ConstructionHomeIndex,
            Is.EqualTo(constructionFunction.ParameterCount));
        Assert.That(constructionFunction.IsPrivate, Is.True);
        Assert.That(constructionFunction.IsInternal, Is.False);
        Assert.That(constructionFunction.IsStatic, Is.True);
        Assert.That(constructionFunction.Body.Statements.Any(
            static statement => statement is BoundFieldAssignment), Is.True);

        var entry = loaded.Functions[loaded.EntryPoint];
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal { Initializer: BoundCall }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundFieldAssignment), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundExpressionStatement {
                Expression: BoundNativeFixedArrayWrite,
            }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal {
                Initializer: BoundNativeFixedArrayRepeatInitialization,
            }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal { Initializer: BoundFieldRead }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal {
                Initializer: BoundNativeFixedArrayElement,
            }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal {
                Initializer: BoundNativeFixedArrayInitialization,
            }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundVacantLocal), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundIf), Is.True);
        Assert.That(loaded.Functions.Any(static function =>
            function.Body.Statements.Any(static statement =>
                statement is BoundReturn {
                    Value: BoundBinary {
                        Operator: Terrum.CodeAnalysis.Lexing.TokenKind.EqualsEquals,
                    },
                })), Is.True);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            ModuleIdentity, SchemaIdentity, 1);
        Assert.That(reemitted, Is.Not.Empty);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            ModuleIdentity, []);
        Assert.That(reloaded.Functions.Length,
            Is.EqualTo(loaded.Functions.Length));
        Assert.That(reloaded.EntryPoint, Is.EqualTo(loaded.EntryPoint));
    }

    [Test]
    public void LoadsL1ImportedMethodBodiesFromExactProviderArtifacts()
    {
        const string rootIdentity = "Root";
        const string providerIdentity = "Provider";
        var (rootArtifact, emittedProviderArtifact)
            = TrRmLocalAggregateLayoutProducerTests
                .CreateL1MethodProviderArtifacts();
        var providerArtifact = RenameProviderMethodNames(
            emittedProviderArtifact);
        var rootInputBytes = (byte[])rootArtifact.Clone();
        var providerInputBytes = (byte[])providerArtifact.Clone();
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootInputBytes),
            rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                providerInputBytes)]);
        Array.Clear(rootInputBytes, 0, rootInputBytes.Length);
        Array.Clear(providerInputBytes, 0, providerInputBytes.Length);

        var closure = loaded.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "L1 MethodDef Imports must retain their checked closure.");
        Assert.That(closure.RootArtifactIndex, Is.Zero);
        Assert.That(closure.RootModuleIdentity, Is.EqualTo(rootIdentity));
        Assert.That(closure.Artifacts, Has.Length.EqualTo(2));
        Assert.That(closure.MethodEdges, Has.Length.EqualTo(2));
        Assert.That(closure.TypeEdges, Is.Empty);

        var edge = closure.MethodEdges[0];
        Assert.That(edge.ConsumerModuleIdentity, Is.EqualTo(rootIdentity));
        Assert.That(edge.ImportRow, Is.EqualTo(1));
        Assert.That(edge.ProducerIdentity, Is.EqualTo(providerIdentity));
        Assert.That(edge.ProviderModuleIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(edge.ExportKey, Is.EqualTo("Actual"));
        Assert.That(edge.Expected.ArtifactIndex, Is.Zero);
        Assert.That(edge.Actual.ArtifactIndex, Is.EqualTo(1));
        Assert.That(edge.Expected.DefinitionId >> 24, Is.EqualTo(0x16));
        Assert.That(edge.Actual.DefinitionId >> 24, Is.EqualTo(0x16));
        Assert.That(edge.Expected.DefinitionId,
            Is.Not.EqualTo(edge.Actual.DefinitionId));
        var uncalledEdge = closure.MethodEdges[1];
        Assert.That(uncalledEdge.ImportRow, Is.EqualTo(2));
        Assert.That(uncalledEdge.ExportKey, Is.EqualTo("UnusedActual"));
        Assert.That(uncalledEdge.Expected.ArtifactIndex, Is.Zero);
        Assert.That(uncalledEdge.Actual.ArtifactIndex, Is.EqualTo(1));
        Assert.That(edge.ExportRow, Is.GreaterThan(0));
        Assert.That(uncalledEdge.ExportRow, Is.GreaterThan(0));
        Assert.That(closure.Artifacts[0].ArtifactSnapshot.ToArray(),
            Is.EqualTo(rootArtifact));
        Assert.That(closure.Artifacts[1].ArtifactSnapshot.ToArray(),
            Is.EqualTo(providerArtifact));

        var rootModule = closure.Modules.Single(module =>
            module.ArtifactIndex == 0
                && module.ModuleRow == closure.RootModuleRow);
        var providerModule = closure.Modules.Single(module =>
            module.Identity == providerIdentity);
        Assert.That(rootModule.Program.StandardMethodProviderClosure,
            Is.Null);
        Assert.That(providerModule.Program.StandardMethodProviderClosure,
            Is.Null);
        Assert.That(rootModule.Program.NativeFunctionPointerSignatures,
            Is.Empty);
        Assert.That(providerModule.Program.NativeFunctionPointerSignatures,
            Is.Empty);
        Assert.That(rootModule.Program.Functions.Any(static function =>
            function.DirectNativeImport is not null), Is.False);
        Assert.That(providerModule.Program.Functions.Any(static function =>
            function.DirectNativeImport is not null), Is.False);
        Assert.That(rootModule.Program.Functions[edge.Expected.FunctionIndex]
            .ModuleImport?.ExportKey, Is.EqualTo("Actual"));
        Assert.That(rootModule.Program.Functions[
                rootModule.FunctionIndexByMethodRow[
                    edge.Expected.MethodDefRow]].IsExternal, Is.True);
        Assert.That(providerModule.FunctionIndexByMethodRow[
                edge.Actual.MethodDefRow],
            Is.EqualTo(edge.Actual.FunctionIndex));
        Assert.That(providerModule.Program.Functions[
                uncalledEdge.Actual.FunctionIndex].Name,
            Is.EqualTo("UnusedActualImplementation"));
        Assert.That(providerModule.Program.Functions[
                uncalledEdge.Actual.FunctionIndex].Name,
            Is.Not.EqualTo(uncalledEdge.ExportKey));
        Assert.That(rootModule.Program.Functions[
                uncalledEdge.Expected.FunctionIndex].ModuleImport?.ExportKey,
            Is.EqualTo("UnusedActual"));
        Assert.That(providerModule.Program.Functions[edge.Actual.FunctionIndex]
            .IsExternal, Is.False);
        Assert.That(providerModule.Program.Functions[edge.Actual.FunctionIndex]
            .Name, Is.Not.EqualTo(edge.ExportKey));
        Assert.That(providerModule.Program.Functions[edge.Actual.FunctionIndex]
            .Body.Statements, Is.Not.Empty);
        var rootEntry = rootModule.Program.Functions[
            rootModule.Program.EntryPoint];
        var captureOrder = rootEntry.Body.Statements
            .OfType<BoundLocal>()
            .Select(static local => local.Initializer)
            .OfType<BoundCall>()
            .Select(call => rootModule.Program.Functions[
                call.Function].Name)
            .Where(static name => name is "First" or "Second")
            .ToArray();
        Assert.That(captureOrder, Is.EqualTo(["Second", "First"]));
        var importedCallFound = false;
        Assert.That(BoundTreeTraversal.VisitExpressions(rootEntry.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.Function == edge.Expected.FunctionIndex)
                {
                    importedCallFound = true;
                }
            }), Is.True);
        Assert.That(importedCallFound, Is.True);

        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(rootArtifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var entryBodyRow = tables.Methods.GetBodyRow(
            tables.Modules.GetEntryMethod(1));
        var malformedMethodMap = ReplaceBodyMethodMapToken(rootArtifact,
            entryBodyRow, 0x1C00_0001u, 0x1C00_0003u);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedMethodMap), rootIdentity,
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    providerArtifact)]));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                rootIdentity, []));

        var expectedMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Methods.GetName(methodRow), "Expected"))
            {
                expectedMethodRow = methodRow;
                break;
            }
        }

        Assert.That(expectedMethodRow, Is.GreaterThan(0));
        var directExpectedRoot = ReplaceBodyMethodMapToken(rootArtifact,
            entryBodyRow, 0x1C00_0001u,
            0x1600_0000u | checked((uint)expectedMethodRow));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    directExpectedRoot), rootIdentity, []));
        var directExpectedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                directExpectedRoot), rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                providerArtifact)]);
        var directClosure
            = directExpectedLoaded.StandardMethodProviderClosure
                ?? throw new InvalidOperationException(
                    "A direct expected-stub call must retain its Import closure.");
        var directRootModule = directClosure.Modules.Single(module =>
            module.ArtifactIndex == 0
                && module.ModuleRow == directClosure.RootModuleRow);
        var directExpectedFunction
            = directRootModule.FunctionIndexByMethodRow[
                directClosure.MethodEdges[0].Expected.MethodDefRow];
        Assert.That(directRootModule.Program.Functions[
            directExpectedFunction].IsExternal, Is.True);
        var directExpectedCallFound = false;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            directExpectedLoaded.Functions[
                directExpectedLoaded.EntryPoint].Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.Function == directExpectedFunction)
                {
                    directExpectedCallFound = true;
                }
            }), Is.True);
        Assert.That(directExpectedCallFound, Is.True);

        var incompatibleProvider = EmitLibraryArtifactBytes("""
            public int32 Actual(int32 first, uint8 second) {
                return first;
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """, providerIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    rootArtifact), rootIdentity,
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    incompatibleProvider)]));

        var wrongIdentityProvider = EmitLibraryArtifactBytes("""
            public int32 Actual(int32 first, int32 second) {
                return first;
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """, "WrongProvider");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    rootArtifact), rootIdentity,
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    wrongIdentityProvider)]));

        var wrongKeyProvider = EmitLibraryArtifactBytes("""
            public int32 Different(int32 first, int32 second) {
                return first;
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """, providerIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    rootArtifact), rootIdentity,
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    wrongKeyProvider)]));

        var disconnectedProvider = EmitLibraryArtifactBytes("""
            public int32 Disconnected(int32 value) {
                return value;
            }
            """, "Disconnected");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    rootArtifact), rootIdentity,
                [
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        providerArtifact),
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        disconnectedProvider),
                ]));
    }

    [Test]
    public void LoadsAndReemitsL1ImportedImplicitReferenceParameters()
    {
        const string rootIdentity = "Root";
        const string providerIdentity = "Provider";
        var (rootArtifact, emittedProviderArtifact)
            = TrRmLocalAggregateLayoutProducerTests
                .CreateL1MethodProviderArtifacts(implicitReference: true);
        var providerArtifact = RenameProviderMethodNames(
            emittedProviderArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                providerArtifact)]);

        var closure = loaded.StandardMethodProviderClosure
            ?? throw new InvalidOperationException(
                "The imported implicit-reference declaration must retain its provider closure.");
        var edge = closure.MethodEdges.Single(static candidate =>
            candidate.ExportKey == "Actual");
        var rootModule = closure.Modules.Single(module =>
            module.ArtifactIndex == closure.RootArtifactIndex
                && module.ModuleRow == closure.RootModuleRow);
        var providerModule = closure.Modules.Single(module =>
            module.Identity == providerIdentity
                && module.ArtifactIndex == edge.Actual.ArtifactIndex
                && module.ModuleRow == edge.Actual.ModuleRow);
        var expected = rootModule.Program.Functions[
            edge.Expected.FunctionIndex];
        var actual = providerModule.Program.Functions[
            edge.Actual.FunctionIndex];
        Assert.That(expected.Variables[0].IsImplicitRef, Is.True);
        Assert.That(actual.Variables[0].IsImplicitRef, Is.True);
        Assert.That(actual.Body.Statements, Is.Not.Empty);

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            rootIdentity, SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                providerArtifact)]);
        Assert.That(reloaded.StandardMethodProviderClosure!.MethodEdges,
            Has.Length.EqualTo(2));
        var reloadedProvider = reloaded.StandardMethodProviderClosure.Modules
            .Single(module => module.Identity == providerIdentity);
        Assert.That(reloadedProvider.Program.Functions[
                edge.Actual.FunctionIndex].Variables[0].IsImplicitRef,
            Is.True);

        var byValueProvider
            = TrRmLocalAggregateLayoutProducerTests
                .CreateL1MethodProviderArtifacts().ProviderArtifact;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
                rootIdentity,
                [new TrRmStandardArtifactInput(SchemaIdentity,
                    byValueProvider)]));

        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
            reemitted);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                index);
        var mainMethod = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainMethod = methodRow;
                break;
            }
        }
        Assert.That(mainMethod, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethod), tables.Contracts.Count);
        var importedBorrowFound = false;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x02 || record[4] != 1)
            {
                continue;
            }

            var targetMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            var targetToken = BinaryPrimitives.ReadUInt32LittleEndian(
                body.MethodMap[(targetMapIndex * sizeof(uint))..]);
            if (targetToken != 0x1C00_0001u)
            {
                continue;
            }

            Assert.That((record[20], record[21]),
                Is.EqualTo(((byte)2, (byte)3)));
            importedBorrowFound = true;
        }
        Assert.That(importedBorrowFound, Is.True);
    }

    [Test]
    public void ReemitsL1ImportsAgainstShiftedProviderMethodRows()
    {
        const string rootIdentity = "Root";
        const string providerIdentity = "Provider";
        var (rootArtifact, originalProviderArtifact)
            = TrRmLocalAggregateLayoutProducerTests
                .CreateL1MethodProviderArtifacts();
        var shiftedProviderArtifact = EmitLibraryArtifactBytes("""
            public int32 Prefix(int32 value) {
                return value;
            }

            public int32 Actual(int32 left, int32 right) {
                return left;
            }

            public int32 UnusedActual(int32 value) {
                return value;
            }
            """, providerIdentity);

        var originalRoot = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                originalProviderArtifact)]);
        var originalEdge = originalRoot.StandardMethodProviderClosure!
            .MethodEdges.Single(static edge => edge.ExportKey == "Actual");
        var shiftedRoot = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, rootArtifact),
            rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                shiftedProviderArtifact)]);
        var shiftedEdge = shiftedRoot.StandardMethodProviderClosure!
            .MethodEdges.Single(static edge => edge.ExportKey == "Actual");

        Assert.That(shiftedEdge.Expected.DefinitionId,
            Is.EqualTo(originalEdge.Expected.DefinitionId));
        Assert.That(shiftedEdge.Actual.DefinitionId,
            Is.Not.EqualTo(originalEdge.Actual.DefinitionId));

        var reemitted = TrRmExecutableCompiler.EmitStandard(shiftedRoot,
            rootIdentity, SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            rootIdentity,
            [new TrRmStandardArtifactInput(SchemaIdentity,
                shiftedProviderArtifact)]);
        var reemittedEdge = reloaded.StandardMethodProviderClosure!
            .MethodEdges.Single(static edge => edge.ExportKey == "Actual");
        Assert.That(reemittedEdge.Expected.DefinitionId,
            Is.EqualTo(originalEdge.Expected.DefinitionId));
        Assert.That(reemittedEdge.Actual.DefinitionId,
            Is.EqualTo(shiftedEdge.Actual.DefinitionId));

        var rootModule = reloaded.StandardMethodProviderClosure.Modules
            .Single(module => module.Identity == rootIdentity);
        var importedExpectedFunctionIndex = reemittedEdge.Expected.FunctionIndex;
        var rootEntry = rootModule.Program.Functions[
            rootModule.Program.EntryPoint];
        var calledExpected = false;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            rootEntry.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && call.Function == importedExpectedFunctionIndex)
                {
                    calledExpected = true;
                }
            }), Is.True);
        Assert.That(calledExpected, Is.True);
    }

    [Test]
    public void RejectsMalformedL1BodyAndIncompatibleSchemaSelection()
    {
        var artifact = EmitArtifactBytes();
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var bodyRow = tables.Methods.GetBodyRow(1);
        var bodyBytes = tables.Bodies.GetRow(bodyRow).ToArray();
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var code = body.Code.ToArray();
        var bodyOffset = FindSequence(bodyBytes, code);
        var artifactOffset = FindSequence(artifact, bodyBytes);
        Assert.That(bodyOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(artifactOffset, Is.GreaterThanOrEqualTo(0));

        var malformed = (byte[])artifact.Clone();
        malformed[artifactOffset + bodyOffset] = 0xFF;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, malformed),
                ModuleIdentity, []));

        var typeMapOffset = FindSequence(bodyBytes, body.TypeMap.ToArray());
        Assert.That(typeMapOffset, Is.GreaterThanOrEqualTo(0));
        var malformedTypeMap = (byte[])artifact.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedTypeMap.AsSpan(artifactOffset + typeMapOffset),
            uint.MaxValue);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedTypeMap), ModuleIdentity, []));

        var unsupportedRecordOffset = -1;
        for (var recordIndex = 0;
            recordIndex < body.RecordCount; recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] is 0x04 or 0x05)
            {
                unsupportedRecordOffset = FindSequence(bodyBytes,
                    record.ToArray());
                break;
            }
        }

        Assert.That(unsupportedRecordOffset, Is.GreaterThanOrEqualTo(0));
        var unsupportedRecord = (byte[])artifact.Clone();
        unsupportedRecord[artifactOffset + unsupportedRecordOffset] = 0x0F;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    unsupportedRecord), ModuleIdentity, []));

        var entryMethodRow = tables.Modules.GetEntryMethod(1);
        var entryBodyRow = tables.Methods.GetBodyRow(entryMethodRow);
        var entryBodyBytes = tables.Bodies.GetRow(entryBodyRow).ToArray();
        var entryBody = tables.Bodies.GetRegisterBody(entryBodyRow,
            tables.Contracts.Count);
        var entryCode = entryBody.Code.ToArray();
        Assert.That(entryCode[^2], Is.EqualTo(0x17));
        var entryBodyOffset = FindSequence(artifact, entryBodyBytes);
        var entryCodeOffset = FindSequence(entryBodyBytes, entryCode);
        Assert.That(entryBodyOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(entryCodeOffset, Is.GreaterThanOrEqualTo(0));

        var edgeOffset = -1;
        for (var recordIndex = 0;
            recordIndex < entryBody.RecordCount; recordIndex++)
        {
            var record = entryBody.GetRecord(recordIndex);
            if (record[0] == 0x06)
            {
                edgeOffset = FindSequence(entryBodyBytes, record.ToArray());
                break;
            }
        }

        Assert.That(edgeOffset, Is.GreaterThanOrEqualTo(0));
        var malformedEdge = (byte[])artifact.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(
            malformedEdge.AsSpan(entryBodyOffset + edgeOffset + 4),
            checked((ushort)entryBody.BlockCount));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedEdge), ModuleIdentity, []));

        var callSiteOffset = -1;
        for (var recordIndex = 0;
            recordIndex < entryBody.RecordCount; recordIndex++)
        {
            var record = entryBody.GetRecord(recordIndex);
            if (record[0] == 0x02)
            {
                callSiteOffset = FindSequence(entryBodyBytes,
                    record.ToArray());
                break;
            }
        }

        Assert.That(callSiteOffset, Is.GreaterThanOrEqualTo(0));
        var malformedCallSite = (byte[])artifact.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformedCallSite.AsSpan(entryBodyOffset + callSiteOffset + 16),
            1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedCallSite), ModuleIdentity, []));

        var malformedCallSiteFlags = (byte[])artifact.Clone();
        malformedCallSiteFlags[entryBodyOffset + callSiteOffset + 6] = 1;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedCallSiteFlags), ModuleIdentity, []));

        var mismatchedReturn = (byte[])artifact.Clone();
        mismatchedReturn[entryBodyOffset + entryCodeOffset
            + entryCode.Length - 2] = 0xB8;
        mismatchedReturn[entryBodyOffset + entryCodeOffset
            + entryCode.Length - 1] = 0;
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    mismatchedReturn), ModuleIdentity, []));

        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, artifact),
                ModuleIdentity,
                [new TrRmStandardArtifactInput(A2R1SchemaIdentity,
                    artifact)]));
    }

    [Test]
    public void RejectsL1InheritedRegisterOpcodeOutsideItsConsumerSubset()
    {
        var artifact = EmitArtifactBytes();
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var codeArtifactOffsets = new List<int>();
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow == 0)
            {
                continue;
            }

            var bodyBytes = tables.Bodies.GetRow(bodyRow).ToArray();
            var body = tables.Bodies.GetRegisterBody(bodyRow,
                tables.Contracts.Count);
            for (var codeOffset = 0; codeOffset < body.Code.Length;)
            {
                var opcode = body.Code[codeOffset];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        TrRmArtifactSchema.LocalAggregateLayoutDraft);
                if (opcode == 0xD5)
                {
                    var artifactOffset = FindSequence(artifact, bodyBytes);
                    var bodyCodeOffset = FindSequence(bodyBytes,
                        body.Code.ToArray());
                    if (artifactOffset >= 0 && bodyCodeOffset >= 0)
                    {
                        codeArtifactOffsets.Add(artifactOffset
                            + bodyCodeOffset + codeOffset);
                    }
                }

                codeOffset += instructionLength;
            }
        }

        Assert.That(codeArtifactOffsets, Is.Not.Empty);
        var foundUnsupportedOpcode = false;
        var rejectedCandidates = new List<string>(codeArtifactOffsets.Count);
        foreach (var codeArtifactOffset in codeArtifactOffsets)
        {
            var unsupportedOpcode = (byte[])artifact.Clone();
            unsupportedOpcode[codeArtifactOffset] = 0xD7;
            try
            {
                _ = TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        unsupportedOpcode), ModuleIdentity, []);
            }
            catch (NotSupportedException exception)
                when (exception.Message.Contains("opcode 0xD7",
                    StringComparison.Ordinal))
            {
                foundUnsupportedOpcode = true;
                break;
            }
            catch (InvalidDataException exception)
            {
                rejectedCandidates.Add(exception.Message);
            }
            catch (NotSupportedException exception)
            {
                rejectedCandidates.Add(exception.Message);
            }
        }

        Assert.That(foundUnsupportedOpcode, Is.True,
            string.Join(Environment.NewLine, rejectedCandidates));
    }

    [Test]
    public void LoadsFieldBaseArrayPlaceSelectionFromArtifactBytes()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Details {
                public mut [uint8; 2] Values;
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut Details Details;

                internal Packet() {
                    Details.Values = [uint8(0), uint8(1)];
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                packet.Details.Values[0] = uint8(1);
                mut uint8 nestedElement = packet.Details.Values[0];
                return int32(nestedElement);
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            ModuleIdentity, []);
        var entry = loaded.Functions[loaded.EntryPoint];
        var constructor = loaded.Functions.Single(function =>
            function.ConstructionHomeIndex >= 0);

        Assert.That(constructor.Body.Statements.Any(static statement =>
            statement is BoundFieldAssignment assignment
            && assignment.NestedFields.Length > 0
            && assignment.Value is BoundRead read
            && NativeFixedArrayTypes.IsType(read.Type)), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundExpressionStatement {
                Expression: BoundNativeFixedArrayWrite {
                    FieldPlace: not null,
                },
            }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal {
                Initializer: BoundNativeFixedArrayElement {
                    FieldPlace: not null,
                },
            }), Is.True);
    }

    [Test]
    public void LoadsElementBaseFieldPlaceSelectionFromArtifactBytes()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Item {
                public mut uint8 Value;

                internal Item() {
                    Value = uint8(0);
                }
            }

            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut [Item; 2] Items;

                internal Packet() {
                    Items = [new Item(), new Item()];
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                packet.Items[0].Value = uint8(1);
                mut uint8 observed = packet.Items[0].Value;
                return int32(observed);
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, artifact),
            ModuleIdentity, []);
        var entry = loaded.Functions[loaded.EntryPoint];

        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundExpressionStatement {
                Expression: BoundNativeFixedArrayPathWrite {
                    LeafFields.Length: > 0,
                },
            }), Is.True);
        Assert.That(entry.Body.Statements.Any(static statement =>
            statement is BoundLocal {
                Initializer: BoundFieldRead,
            }), Is.True);
    }

    [Test]
    public void RejectsL1ConstructorThatLeavesAFieldUninitialized()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut uint8 First;
                public mut uint8 Second;

                internal Packet() {
                    First = uint8(0);
                    Second = uint8(1);
                }
            }

            int32 Main() {
                mut Packet packet = new Packet();
                return int32(packet.First);
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var constructorMethodRow = 0;
        var firstFieldRow = 0;
        var secondFieldRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow)
                .EndsWith("#ctor", StringComparison.Ordinal))
            {
                constructorMethodRow = methodRow;
                break;
            }
        }
        for (var fieldRow = 1; fieldRow <= tables.Fields.Count; fieldRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Fields.GetName(fieldRow), "First"))
            {
                firstFieldRow = fieldRow;
            }
            else if (StringComparer.Ordinal.Equals(
                         tables.Fields.GetName(fieldRow), "Second"))
            {
                secondFieldRow = fieldRow;
            }
        }
        Assert.That(constructorMethodRow, Is.GreaterThan(0));
        Assert.That(firstFieldRow, Is.GreaterThan(0));
        Assert.That(secondFieldRow, Is.GreaterThan(0));
        var constructorBodyRow
            = tables.Methods.GetBodyRow(constructorMethodRow);
        var constructorBodyBytes
            = tables.Bodies.GetRow(constructorBodyRow).ToArray();
        var constructorBody = tables.Bodies.GetRegisterBody(
            constructorBodyRow, tables.Contracts.Count);
        Assert.That(tables.Fields.GetOwnerTypeDef(firstFieldRow),
            Is.EqualTo(tables.Fields.GetOwnerTypeDef(secondFieldRow)));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                tables.Fields.GetTypeSpecRef(firstFieldRow),
                tables.Fields.GetTypeSpecRef(secondFieldRow)),
            Is.True);

        var firstFieldSelectionOffset = -1;
        for (var recordIndex = 0;
            recordIndex < constructorBody.RecordCount; recordIndex++)
        {
            var record = constructorBody.GetRecord(recordIndex);
            if (record[0] == 0x04 && record.Length == 24
                && record[4] == 2 && record[5] == 3
                && BinaryPrimitives.ReadUInt32LittleEndian(record[16..])
                    == (uint)firstFieldRow)
            {
                firstFieldSelectionOffset = FindSequence(
                    constructorBodyBytes, record.ToArray());
                break;
            }
        }

        var artifactBodyOffset = FindSequence(artifact,
            constructorBodyBytes);
        Assert.That(firstFieldSelectionOffset,
            Is.GreaterThanOrEqualTo(0));
        Assert.That(artifactBodyOffset, Is.GreaterThanOrEqualTo(0));
        var partialConstruction = (byte[])artifact.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(
            partialConstruction.AsSpan(artifactBodyOffset
                + firstFieldSelectionOffset + 16),
            checked((uint)secondFieldRow));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    partialConstruction), ModuleIdentity, []));
    }

    [Test]
    public void RejectsL1FixedArrayLayoutGeometryThatDisagreesWithItsType()
    {
        var artifact = EmitArtifactBytes();
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                        index);
        var selectedLayoutRow = 0;
        var selectedLayout = default(TrRmFixedArrayLayoutRow);
        ulong elementCount = 0;
        for (var layoutRow = 1;
            layoutRow <= tables.FixedArrayLayouts.Count; layoutRow++)
        {
            var candidate = tables.FixedArrayLayouts.GetRow(layoutRow);
            if (candidate.OwnerModuleRow == 1
                    && tables.TypeSpecs.TryGetFixedInlineArray(
                        checked((uint)candidate.ArrayTypeSpecRow), out _,
                        out var candidateCount)
                    && candidateCount > 0)
            {
                    selectedLayoutRow = layoutRow;
                    selectedLayout = candidate;
                    elementCount = candidateCount;
                    break;
            }
        }

        Assert.That(selectedLayoutRow, Is.GreaterThan(0));
        Assert.That(index.TryGetSection(0x0048, out var layoutSection,
            out _), Is.True);
        var layoutSectionOffset = FindSequence(artifact,
            layoutSection.ToArray());
        Assert.That(layoutSectionOffset, Is.GreaterThanOrEqualTo(0));

        var mismatchedGeometry = (byte[])artifact.Clone();
        var rowOffset = layoutSectionOffset
            + ((selectedLayoutRow - 1) * 40);
        var changedElementStride = checked(selectedLayout.ElementStride
            + selectedLayout.Alignment);
        var changedByteExtent = checked(elementCount
            * changedElementStride);
        BinaryPrimitives.WriteUInt64LittleEndian(
            mismatchedGeometry.AsSpan(rowOffset + 16),
            changedElementStride);
        BinaryPrimitives.WriteUInt64LittleEndian(
            mismatchedGeometry.AsSpan(rowOffset + 24),
            changedByteExtent);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(SchemaIdentity,
                        mismatchedGeometry), ModuleIdentity, []));
    }

    [Test]
    public void RoundTripsL1ZeroStrideRepeatAtUInt64MaxCountCompactly()
    {
        const string source = """
            int32 Main() {
                        mut [void; 18446744073709551615] units
                            = [(); 18446744073709551615];
                        return int32(42);
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var input = new TrRmStandardArtifactInput(SchemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            ModuleIdentity, []);
        var array = loaded.NativeFixedArrayTypes.Single();
        Assert.That((array.ElementType, array.ElementCount,
            array.ElementStride, array.ByteExtent),
            Is.EqualTo((PrimitiveType.Void, ulong.MaxValue, 0UL, 0UL)));

        var repeatLocal = loaded.Functions[loaded.EntryPoint].Body.Statements
            .OfType<BoundLocal>().Single(static local =>
                        local.Initializer
                            is BoundNativeFixedArrayRepeatInitialization);
        var repeat = (BoundNativeFixedArrayRepeatInitialization)
            repeatLocal.Initializer;
        Assert.That((repeat.Layout.ElementCount,
            repeat.Layout.ElementStride, repeat.Layout.ByteExtent),
            Is.EqualTo((ulong.MaxValue, 0UL, 0UL)));

        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                        .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                            index);
        var mainMethodRow = tables.Modules.GetEntryMethod(1);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainMethodRow), tables.Contracts.Count);
        var repeatStoreCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x05 && record[7] == 6)
            {
                        repeatStoreCount++;
            }
        }
        Assert.That(repeatStoreCount, Is.EqualTo(1));

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            ModuleIdentity, SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            ModuleIdentity, []);
        Assert.That(reloaded.NativeFixedArrayTypes.Single().ElementCount,
            Is.EqualTo(ulong.MaxValue));
    }

    [Test]
    public void ReemitsL1HighestUInt16TypeMapAndRecordIndexes()
    {
        var artifact = EmitArtifactBytes();
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var entryMethodRow = tables.Modules.GetEntryMethod(1);
        var bodyRow = tables.Methods.GetBodyRow(entryMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var code = body.Code.ToArray();
        var selectedRecordIndex = -1;
        var selectedInstructionOffset = -1;
        var selectedRecord = Array.Empty<byte>();
        for (var codeOffset = 0; codeOffset < code.Length;)
        {
            var opcode = code[codeOffset];
            var instructionLength
                = TrRmRegisterBodyReader.InstructionLength(opcode,
                    TrRmArtifactSchema.LocalAggregateLayoutDraft);
            if (opcode is 0xD8 or 0xDA)
            {
                var recordIndex
                    = BinaryPrimitives.ReadUInt16LittleEndian(
                        code.AsSpan(codeOffset + 2));
                var candidate = body.GetRecord(recordIndex);
                if (candidate[0] == 0x04)
                {
                    selectedRecordIndex = recordIndex;
                    selectedInstructionOffset = codeOffset;
                    selectedRecord = candidate.ToArray();
                    break;
                }
            }

            codeOffset += instructionLength;
        }
        Assert.That(selectedRecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(selectedRecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(selectedRecordIndex, Is.GreaterThanOrEqualTo(0));
        var originalTypeMap = body.TypeMap.ToArray();
        var originalTypeMapCount = body.TypeCount;
        var originalTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                selectedRecord.AsSpan(20));
        Assert.That(originalTypeMapIndex, Is.LessThan(originalTypeMapCount));
        var selectedTypeSpecRef
            = BinaryPrimitives.ReadUInt32LittleEndian(
                originalTypeMap.AsSpan(originalTypeMapIndex * sizeof(uint)));
        var expandedTypeMap = new byte[65_536 * sizeof(uint)];
        originalTypeMap.CopyTo(expandedTypeMap, 0);
        for (var mapIndex = originalTypeMapCount;
            mapIndex < 65_536; mapIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                expandedTypeMap.AsSpan(mapIndex * sizeof(uint)),
                selectedTypeSpecRef);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(
            selectedRecord.AsSpan(20), ushort.MaxValue);
        code[selectedInstructionOffset + 2] = byte.MaxValue;
        code[selectedInstructionOffset + 3] = byte.MaxValue;

        var records = new ReadOnlyMemory<byte>[65_536];
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records[recordIndex] = recordIndex == selectedRecordIndex
                ? selectedRecord
                : body.GetRecord(recordIndex).ToArray();
        }
        for (var recordIndex = body.RecordCount;
            recordIndex < records.Length; recordIndex++)
        {
            records[recordIndex] = selectedRecord;
        }

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }

        var homeMap = body.HomeMap.ToArray();
        var methodMap = body.MethodMap.ToArray();
        var fieldMap = body.FieldMap.ToArray();
        var constantMap = body.ConstantMap.ToArray();
        var encodedBody = TrRmRegisterBodyWriter.Encode(
            homeMap, expandedTypeMap, methodMap, fieldMap, constantMap,
            blocks, records, code,
            tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        var mapCapacityFailure = Assert.Throws<ArgumentOutOfRangeException>(
            () => TrRmRegisterBodyWriter.Encode(homeMap,
                new byte[65_537 * sizeof(uint)], methodMap, fieldMap,
                constantMap, blocks, records, code,
                schema: TrRmArtifactSchema.LocalAggregateLayoutDraft));
        Assert.That(mapCapacityFailure!.ParamName, Is.EqualTo("typeMap"));
        var recordCapacityFailure
            = Assert.Throws<ArgumentOutOfRangeException>(() =>
                TrRmRegisterBodyWriter.Encode(homeMap, expandedTypeMap,
                    methodMap, fieldMap, constantMap, blocks,
                    new ReadOnlyMemory<byte>[65_537], code,
                    schema: TrRmArtifactSchema.LocalAggregateLayoutDraft));
        Assert.That(recordCapacityFailure!.ParamName,
            Is.EqualTo("records"));
        var boundaryArtifact = ReplaceBodyRow(artifact, bodyRow,
            encodedBody);
        var boundaryIndex
            = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(
                boundaryArtifact);
        var boundaryTables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    boundaryIndex);
        var boundaryBody = boundaryTables.Bodies.GetRegisterBody(bodyRow,
            boundaryTables.Contracts.Count);
        Assert.That(boundaryBody.TypeCount, Is.EqualTo(65_536));
        Assert.That(boundaryBody.RecordCount, Is.EqualTo(65_536));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                boundaryBody.Code[(selectedInstructionOffset + 2)..]),
            Is.EqualTo(ushort.MaxValue));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                boundaryArtifact), ModuleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            ModuleIdentity, SchemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, reemitted),
            ModuleIdentity, []);
        Assert.That(reloaded.EntryPoint, Is.EqualTo(loaded.EntryPoint));
    }

    [Test]
    public void LoadsExactResultHomePreparationAndRejectsIncompatibleState()
    {
        var artifact = EmitArtifactBytes();
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var entryMethodRow = tables.Modules.GetEntryMethod(1);
        var entryBodyRow = tables.Methods.GetBodyRow(entryMethodRow);

        var initializedResult = AddResultHomePreparation(artifact,
            entryBodyRow, priorState: 1, transition: 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                initializedResult), ModuleIdentity, []);
        Assert.That(loaded.Functions[loaded.EntryPoint].Body.Statements.Any(
            static statement => statement is BoundLocal {
                Initializer: BoundCall,
            }), Is.True);

        var replacedResult = AddLiveResultHomeReplacementPreparation(
            artifact, entryBodyRow);
        var loadedReplacement = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, replacedResult),
            ModuleIdentity, []);
        Assert.That(loadedReplacement.Functions[
                loadedReplacement.EntryPoint].Body.Statements.Any(
                    static statement => statement is BoundAssignment),
            Is.True);

        var mismatchedState = AddResultHomePreparation(artifact,
            entryBodyRow, priorState: 2, transition: 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    mismatchedState), ModuleIdentity, []));

        var maybeLivePreparation = AddResultHomePreparation(artifact,
            entryBodyRow, priorState: 3, transition: 3);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    maybeLivePreparation), ModuleIdentity, []));
    }

    [Test]
    public void RejectsL1ConditionalHomeJoinThatIsNotLiveOnEveryPredecessor()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Marker {
                public mut int32 Value;
            }

            bool Condition(uint8 value) {
                return value == uint8(1);
            }

            int32 Main() {
                mut bool flag = Condition(uint8(1));
                mut int32 scratch = int32(0);
                mut int32 chosen = flag ? int32(1) : int32(2);
                return chosen;
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var entryMethodRow = tables.Modules.GetEntryMethod(1);
        var entryBodyRow = tables.Methods.GetBodyRow(entryMethodRow);
        var bodyBytes = tables.Bodies.GetRow(entryBodyRow).ToArray();
        var body = tables.Bodies.GetRegisterBody(entryBodyRow,
            tables.Contracts.Count);
        var code = body.Code.ToArray();
        var bodyCodeOffset = FindSequence(bodyBytes, code);
        var artifactBodyOffset = FindSequence(artifact, bodyBytes);
        Assert.That(bodyCodeOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(artifactBodyOffset, Is.GreaterThanOrEqualTo(0));

        var stores = new List<(int BlockIndex, int CodeOffset,
            int HomeIndex, byte Opcode)>();
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            var blockCodeOffset
                = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    block));
            var blockCode = body.GetBlockCode(blockIndex);
            for (var position = 0; position < blockCode.Length;)
            {
                var opcode = blockCode[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema);
                if (opcode == 0x14)
                {
                    stores.Add((blockIndex,
                        blockCodeOffset + position,
                        blockCode[position + 1] & 0x0F, opcode));
                }
                else if (opcode == 0xD6)
                {
                    stores.Add((blockIndex,
                        blockCodeOffset + position,
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            blockCode[(position + 2)..]), opcode));
                }

                position += instructionLength;
            }
        }

        var branchStoreGroups = stores
            .Where(static store => store.BlockIndex != 0)
            .GroupBy(static store => store.HomeIndex)
            .Where(static group => group.Select(
                static store => store.BlockIndex).Distinct().Count() >= 2)
            .ToArray();
        Assert.That(branchStoreGroups, Has.Length.EqualTo(1));
        var chosenHome = branchStoreGroups[0].Key;
        var (BlockIndex, CodeOffset, HomeIndex, Opcode) = branchStoreGroups[0].First();
        var chosenTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap[((chosenHome * 8) + 2)..]);
        var scratchHome = -1;
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            if (homeIndex == chosenHome
                || body.HomeMap[homeIndex * 8] != 3
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    body.HomeMap[((homeIndex * 8) + 2)..])
                    != chosenTypeMapIndex)
            {
                continue;
            }

            var hasEntryStore = false;
            foreach (var store in stores)
            {
                hasEntryStore |= store.BlockIndex == 0
                    && store.HomeIndex == homeIndex;
            }

            if (!hasEntryStore)
            {
                continue;
            }

            if (scratchHome >= 0)
            {
                throw new InvalidOperationException(
                    "The L1 Home-join fixture has multiple matching scratch Homes.");
            }

            scratchHome = homeIndex;
        }

        if (scratchHome < 0)
        {
            throw new InvalidOperationException(
                "The L1 Home-join fixture has no matching scratch Home.");
        }

        var malformedJoin = (byte[])artifact.Clone();
        var storeOffset = artifactBodyOffset + bodyCodeOffset
            + CodeOffset;
        if (Opcode == 0x14)
        {
            Assert.That(scratchHome, Is.LessThan(16));
            var sourceRegister = malformedJoin[storeOffset + 1] & 0xF0;
            malformedJoin[storeOffset + 1]
                = (byte)(sourceRegister | scratchHome);
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                malformedJoin.AsSpan(storeOffset + 2),
                checked((ushort)scratchHome));
        }

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    malformedJoin), ModuleIdentity, []));
    }

    [Test]
    public void LoadsL1RegisterJoinsAndCyclicControlFlowFromArtifactBytes()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Marker { public uint8 Value; }

            bool Condition(int32 value, uint8 unused) {
                return value == int32(1);
            }

            int32 Main() {
                mut int32 marker = int32(1);
                mut uint8 small = uint8(0);
                if (Condition(marker, small)) {
                    return int32(3);
                } else {
                    return int32(4);
                }
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var joined = RewriteMainReturnsToLiveRegisterJoin(artifact,
            mismatchedFalseEdge: false);
        var loadedJoin = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, joined),
            ModuleIdentity, []);
        Assert.That(loadedJoin.Functions[
                loadedJoin.EntryPoint].Body.Statements.Any(
                    static statement => statement is BoundIf),
            Is.True);

        var mismatched = RewriteMainReturnsToLiveRegisterJoin(artifact,
            mismatchedFalseEdge: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity, mismatched),
                ModuleIdentity, []));

        var deadIncomingMismatch
            = RewriteMainReturnsToDeadRegisterJoin(artifact);
        _ = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity,
                deadIncomingMismatch), ModuleIdentity, []);

        var cyclic = RewriteMainReturnToBackedge(artifact);
        var loadedCycle = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, cyclic),
            ModuleIdentity, []);
        Assert.That(loadedCycle.Functions[
                loadedCycle.EntryPoint].Body.Statements.Any(
                    static statement => statement is BoundWhile),
            Is.True);
    }

    [Test]
    public void PreservesExactPlaceAuthorityAcrossL1RegisterJoins()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1)]
            public struct Packet {
                public mut int32 Value;
                public mut int32 Other;

                internal Packet() {
                    Value = int32(0);
                    Other = int32(0);
                }
            }

            bool Condition(int32 value) {
                return value == int32(1);
            }

            int32 Main() {
                mut Packet packet = new Packet();
                if (Condition(int32(1))) {
                    packet.Value = int32(3);
                } else {
                    packet.Value = int32(4);
                }
                return packet.Value;
            }
            """;
        var artifact = EmitArtifactBytes(source);
        var placeJoin = RewriteMainFieldPlaceJoin(artifact,
            mismatchedFalseEdge: false);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(SchemaIdentity, placeJoin),
            ModuleIdentity, []);
        var fieldReadFound = false;
        Assert.That(BoundTreeTraversal.VisitExpressions(
            loaded.Functions[loaded.EntryPoint].Body,
            expression => fieldReadFound |= expression is BoundFieldRead),
            Is.True);
        Assert.That(fieldReadFound, Is.True);

        var mismatchedPlaceJoin = RewriteMainFieldPlaceJoin(artifact,
            mismatchedFalseEdge: true);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(SchemaIdentity,
                    mismatchedPlaceJoin), ModuleIdentity, []));
    }

    private static byte[] EmitArtifactBytes()
    {
        const string source = """
            [Blittable]
            [Layout(LayoutKind.Sequential, Packing: 1, Alignment: 32)]
            public struct Packet {
                public mut uint8 Tag;

                internal Packet() {
                    Tag = uint8(0);
                }
            }

            bool Condition(uint8 value) {
                return value == uint8(1);
            }

            bool Unused(uint8 value) {
                return value == uint8(2);
            }

            int32 Main() {
                mut Packet packet = new Packet();
                packet.Tag = uint8(0);
                mut uint8 observedTag = packet.Tag;
                mut [uint8; 2] values = [uint8(0); 2];
                values[0] = uint8(0);
                mut uint8 observedElement = values[0];
                mut [uint8; 2] explicitValues = [uint8(1), uint8(2)];
                mut uint8 observedExplicit = explicitValues[1];
                mut bool isExpected = Condition(observedTag);
                mut int32 selectedResult = int32(0);
                if (isExpected) {
                    selectedResult = int32(1);
                } else {
                    selectedResult = int32(2);
                }
                mut int32 conditionalResult = isExpected
                    ? int32(3)
                    : int32(4);
                return selectedResult + conditionalResult;
            }
            """;
        return EmitArtifactBytes(source);
    }

    private static byte[] EmitArtifactBytes(string source,
        string moduleIdentity = ModuleIdentity)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(
            compilation.Program
                ?? throw new InvalidOperationException(
                    "A successful compilation must contain a bound program."),
            moduleIdentity, SchemaIdentity, 1);
    }

    private static byte[] RenameProviderMethodNames(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var stringRows = new List<(string Text, int Encoding)>(
            tables.Strings.Count + 2);
        var stringRowsByText = new Dictionary<string, int>(
            tables.Strings.Count + 2, StringComparer.Ordinal);
        for (var row = 1; row <= tables.Strings.Count; row++)
        {
            var text = tables.Strings.DecodeNamespace(row);
            stringRows.Add((text, 1));
            _ = stringRowsByText.TryAdd(text, row);
        }

        int GetStringRow(string text)
        {
            if (!stringRowsByText.TryGetValue(text, out var row))
            {
                row = stringRows.Count + 1;
                stringRows.Add((text, 1));
                stringRowsByText.Add(text, row);
            }

            return row;
        }

        var methodRows = new TrRmMethodDefRow[tables.Methods.Count];
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            var name = tables.Methods.GetName(methodRow) switch {
                "Actual" => "ActualImplementation",
                "UnusedActual" => "UnusedActualImplementation",
                var existing => existing,
            };
            var (contractFirst, contractCount)
                = tables.Methods.GetContractRange(methodRow);
            methodRows[methodRow - 1] = new TrRmMethodDefRow(
                tables.Methods.GetOwnerType(methodRow),
                GetStringRow(name),
                tables.Methods.GetSignature(methodRow),
                tables.Methods.GetFlags(methodRow), contractFirst,
                contractCount, tables.Methods.GetBodyRow(methodRow));
        }

        var stringsBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringsBytes,
            stringRows.Count);
        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, tables.Modules, tables.TypeDefs, tables.Signatures,
            tables.Contracts.Count, tables.Bodies.Count);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(kind switch {
                0x0002 => new TrRmSection(kind, flags,
                    checked((uint)stringRows.Count), stringsBytes),
                0x0016 => new TrRmSection(kind, flags,
                    checked((uint)methodRows.Length), methodBytes),
                _ => new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
    }

    private static byte[] EmitLibraryArtifactBytes(string source,
        string moduleIdentity)
    {
        var compilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), new CompilationOptions {
                LayoutCoreRegistration =
                    LayoutCoreRegistrationTestData.Create(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(
            compilation.Program
                ?? throw new InvalidOperationException(
                    "A successful library compilation must contain a bound program."),
            moduleIdentity, SchemaIdentity, 1);
    }

    private static byte[] RewriteMainReturnsToLiveRegisterJoin(
        byte[] artifact, bool mismatchedFalseEdge)
        => RewriteMainBranchReturns(artifact, createJoin: true,
            mismatchedFalseEdge: mismatchedFalseEdge,
            overwriteIncomingRegister: false);

    private static byte[] RewriteMainReturnsToDeadRegisterJoin(
        byte[] artifact)
        => RewriteMainBranchReturns(artifact, createJoin: true,
            mismatchedFalseEdge: true, overwriteIncomingRegister: true);

    private static byte[] RewriteMainReturnToBackedge(byte[] artifact)
        => RewriteMainBranchReturns(artifact, createJoin: false,
            mismatchedFalseEdge: false, overwriteIncomingRegister: false);

    private static byte[] RewriteMainBranchReturns(byte[] artifact,
        bool createJoin, bool mismatchedFalseEdge,
        bool overwriteIncomingRegister)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = 0;
        var conditionMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            switch (tables.Methods.GetName(methodRow))
            {
                case "Main":
                    mainMethodRow = methodRow;
                    break;
                case "Condition":
                    conditionMethodRow = methodRow;
                    break;
            }
        }

        if (mainMethodRow == 0 || conditionMethodRow == 0)
        {
            throw new InvalidOperationException(
                "The L1 register-flow fixture has no Main/Condition Methods.");
        }

        var mainBodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(mainBodyRow,
            tables.Contracts.Count);
        var (branchBlock, trueTarget, falseTarget)
            = FindConditionalBlocks(body);
        if (trueTarget == falseTarget)
        {
            throw new InvalidOperationException(
                "The L1 register-flow fixture does not have two distinct branch arms.");
        }

        var int32TypeMapIndex = FindBodyTypeMapIndex(body, tables,
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(mainMethodRow)));
        var int32HomeIndex = -1;
        if (overwriteIncomingRegister)
        {
            for (var homeIndex = 0; homeIndex < body.HomeCount;
                homeIndex++)
            {
                var homeOffset = homeIndex * 8;
                if (body.HomeMap[homeOffset] == 3
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        body.HomeMap[(homeOffset + 2)..])
                        == int32TypeMapIndex)
                {
                    int32HomeIndex = homeIndex;
                    break;
                }
            }

            if (int32HomeIndex < 0)
            {
                throw new InvalidOperationException(
                    "The L1 register-flow fixture has no local int32 Home.");
            }
        }

        var smallHomeIndex = -1;
        if (mismatchedFalseEdge)
        {
            var (parameterStart, parameterCount)
                = tables.Signatures.GetParamRange(
                    tables.Methods.GetSignature(conditionMethodRow));
            if (parameterCount < 2)
            {
                throw new InvalidOperationException(
                    "The L1 register-flow fixture needs a uint8 Condition formal.");
            }

            var uint8TypeMapIndex = FindBodyTypeMapIndex(body, tables,
                tables.Parameters.GetTypeSpec(
                    parameterStart + parameterCount - 1));
            for (var homeIndex = 0; homeIndex < body.HomeCount;
                homeIndex++)
            {
                var homeOffset = homeIndex * 8;
                if (body.HomeMap[homeOffset] == 3
                    && BinaryPrimitives.ReadUInt16LittleEndian(
                        body.HomeMap[(homeOffset + 2)..])
                        == uint8TypeMapIndex)
                {
                    smallHomeIndex = homeIndex;
                    break;
                }
            }

            if (smallHomeIndex < 0)
            {
                throw new InvalidOperationException(
                    "The L1 register-flow fixture has no local uint8 Home.");
            }
        }

        var joinBlock = body.BlockCount;
        var rewrittenCodeByBlock = new byte[body.BlockCount][];
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex).ToArray();
            var rewriteBlock = createJoin
                ? blockIndex == trueTarget || blockIndex == falseTarget
                : blockIndex == falseTarget;
            if (!rewriteBlock)
            {
                rewrittenCodeByBlock[blockIndex] = code;
                continue;
            }

            var (terminalOffset, terminalLength)
                = FindBlockTerminal(code, body.Schema);
            if (terminalLength != 2 || code[terminalOffset] != 0x17)
            {
                throw new InvalidOperationException(
                    "The L1 register-flow fixture branch arms must return one register.");
            }

            var newLength = checked(terminalOffset + 4
                + (mismatchedFalseEdge && blockIndex == falseTarget ? 4 : 0));
            var rewritten = new byte[newLength];
            code.AsSpan(0, terminalOffset).CopyTo(rewritten);
            var offset = terminalOffset;
            if (mismatchedFalseEdge && blockIndex == falseTarget)
            {
                rewritten[offset++] = 0xD5;
                rewritten[offset++] = 0;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    rewritten.AsSpan(offset),
                    checked((ushort)smallHomeIndex));
                offset += sizeof(ushort);
            }

            rewritten[offset++] = 0xE3;
            rewritten[offset++] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                rewritten.AsSpan(offset),
                checked((ushort)(createJoin
                    ? joinBlock : branchBlock)));
            rewrittenCodeByBlock[blockIndex] = rewritten;
        }

        var records = new List<ReadOnlyMemory<byte>>(
            body.RecordCount + (createJoin ? 1 : 0));
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records.Add(body.GetRecord(recordIndex).ToArray());
        }

        var blocks = new TrRmRegisterBodyBlock[
            body.BlockCount + (createJoin ? 1 : 0)];
        var codeBytes = new List<byte>();
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var originalBlock = body.GetBlock(blockIndex);
            var code = rewrittenCodeByBlock[blockIndex];
            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                checked((uint)codeBytes.Count),
                checked((uint)code.Length),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    originalBlock[8..]));
            codeBytes.AddRange(code);
        }

        if (createJoin)
        {
            var entryStateRecord = checked((ushort)records.Count);
            var incomingRegisterCount
                = overwriteIncomingRegister ? 0u : 1u;
            var entryState = new byte[checked(
                8 + (int)(incomingRegisterCount * 5))];
            entryState[0] = 0x01;
            BinaryPrimitives.WriteUInt32LittleEndian(
                entryState.AsSpan(4), incomingRegisterCount);
            if (!overwriteIncomingRegister)
            {
                entryState[8] = 0;
                entryState[9] = 0;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    entryState.AsSpan(10), int32TypeMapIndex);
                entryState[12] = 0;
            }
            records.Add(entryState);

            var joinCode = overwriteIncomingRegister
                ? new byte[] {
                    0xD5, 0,
                    (byte)int32HomeIndex,
                    checked((byte)(int32HomeIndex >> 8)),
                    0x17, 0,
                }
                : [0x17, 0];
            blocks[joinBlock] = new TrRmRegisterBodyBlock(
                checked((uint)codeBytes.Count),
                checked((uint)joinCode.Length), entryStateRecord);
            codeBytes.AddRange(joinCode);
        }

        var encodedBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), body.FieldMap.ToArray(),
            body.ConstantMap.ToArray(), blocks, records,
            codeBytes.ToArray(), tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        return ReplaceBodyRow(artifact, mainBodyRow, encodedBody);
    }

    private static (int BranchBlock, int TrueTarget, int FalseTarget)
        FindConditionalBlocks(TrRmRegisterBodyReader body)
    {
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var (offset, _) = FindBlockTerminal(code, body.Schema);
            var instruction = code[offset..];
            var opcode = instruction[0];
            int recordIndex;
            if (opcode is 0xB1 or (>= 0xA0 and <= 0xAF))
            {
                recordIndex = instruction[1] >> 4;
            }
            else if (opcode is 0xE4 or 0xE5)
            {
                recordIndex = BinaryPrimitives
                    .ReadUInt16LittleEndian(instruction[2..]);
            }
            else
            {
                continue;
            }

            var edge = body.GetRecord(recordIndex);
            if (edge[0] != 0x06 || edge.Length != 8)
            {
                throw new InvalidOperationException(
                    "The L1 register-flow fixture has a malformed conditional edge.");
            }

            return (blockIndex,
                BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]));
        }

        throw new InvalidOperationException(
            "The L1 register-flow fixture has no conditional branch.");
    }

    private static (int Offset, int Length) FindBlockTerminal(
        ReadOnlySpan<byte> code, TrRmArtifactSchema schema)
    {
        var lastOffset = -1;
        var lastLength = 0;
        for (var position = 0; position < code.Length;)
        {
            var length = TrRmRegisterBodyReader.InstructionLength(
                code[position], schema);
            lastOffset = position;
            lastLength = length;
            position += length;
        }

        if (lastOffset < 0)
        {
            throw new InvalidOperationException(
                "The L1 register-flow fixture has an empty Block.");
        }

        return (lastOffset, lastLength);
    }

    private static ushort FindBodyTypeMapIndex(
        TrRmRegisterBodyReader body,
        TrRmStructuralMetadataValidator.ValidatedExecutionTables tables,
        uint typeSpecRef)
    {
        for (var typeMapIndex = 0; typeMapIndex < body.TypeCount;
            typeMapIndex++)
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap[(typeMapIndex * sizeof(uint))..]);
            if ((token >> 24) == 0x11
                && tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                    typeSpecRef, token & 0x00FF_FFFF))
            {
                return checked((ushort)typeMapIndex);
            }
        }

        throw new InvalidOperationException(
            "The L1 register-flow fixture has no matching TypeMap entry.");
    }

    private static byte[] RewriteMainFieldPlaceJoin(byte[] artifact,
        bool mismatchedFalseEdge)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var mainMethodRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (StringComparer.Ordinal.Equals(
                    tables.Methods.GetName(methodRow), "Main"))
            {
                mainMethodRow = methodRow;
                break;
            }
        }

        if (mainMethodRow == 0)
        {
            throw new InvalidOperationException(
                "The L1 place-flow fixture has no Main Method.");
        }

        var bodyRow = tables.Methods.GetBodyRow(mainMethodRow);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var mainTypeSpecRef = tables.Signatures.GetResultTypeSpec(
            tables.Methods.GetSignature(mainMethodRow));
        var referentTypeMapIndex = FindBodyTypeMapIndex(body, tables,
            mainTypeSpecRef);
        var placeRegister = 13;

        var originalSelection = -1;
        var fieldRow = 0;
        var baseHomeIndex = -1;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04 || record.Length != 24
                || record[4] != 2 || record[6] != 1
                || record[7] != 2 || record[12] != 0
                || record[13] != 1
                || BinaryPrimitives.ReadUInt16LittleEndian(
                    record[20..]) != referentTypeMapIndex)
            {
                continue;
            }

            originalSelection = recordIndex;
            fieldRow = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(record[16..]));
            baseHomeIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                record[10..]);
            break;
        }

        if (originalSelection < 0 || fieldRow <= 0
            || baseHomeIndex < 0)
        {
            throw new InvalidOperationException(
                "The L1 place-flow fixture has no local field PlaceSelection.");
        }

        var selectedOwner = tables.Fields.GetOwnerTypeDef(fieldRow);
        var alternateFieldRow = 0;
        for (var candidate = 1; candidate <= tables.Fields.Count;
            candidate++)
        {
            if (tables.Fields.GetOwnerTypeDef(candidate) == selectedOwner
                && StringComparer.Ordinal.Equals(
                    tables.Fields.GetName(candidate), "Other")
                && tables.TypeSpecs.AreEquivalentTypeSpecRefs(
                    mainTypeSpecRef,
                    tables.Fields.GetTypeSpecRef(candidate)))
            {
                alternateFieldRow = candidate;
                break;
            }
        }

        if (mismatchedFalseEdge && alternateFieldRow == 0)
        {
            throw new InvalidOperationException(
                "The L1 place-flow fixture has no same-typed alternate FieldDef.");
        }

        var successors = new List<int>[body.BlockCount];
        var predecessors = new List<int>[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            successors[blockIndex] = [];
            predecessors[blockIndex] = [];
        }

        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var (offset, _) = FindBlockTerminal(code, body.Schema);
            var instruction = code[offset..];
            int[] targets = [];
            if (instruction[0] is 0xB0 or 0xE3)
            {
                targets = [
                    instruction[0] == 0xB0 ? instruction[1]
                        : BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]),
                ];
            }
            else if (instruction[0] is 0xB1
                or (>= 0xA0 and <= 0xAF)
                or 0xE4 or 0xE5)
            {
                var edgeRecordIndex
                    = instruction[0] is 0xB1 or (>= 0xA0 and <= 0xAF)
                        ? instruction[1] >> 4
                        : BinaryPrimitives.ReadUInt16LittleEndian(
                            instruction[2..]);
                var edge = body.GetRecord(edgeRecordIndex);
                targets = [
                    BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(edge[6..]),
                ];
            }

            successors[blockIndex].AddRange(targets);
            foreach (var target in targets)
            {
                predecessors[target].Add(blockIndex);
            }
        }

        var joinBlock = -1;
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            var (terminalOffset, _) = FindBlockTerminal(code, body.Schema);
            if (predecessors[blockIndex].Count >= 2
                && code[terminalOffset] == 0x17)
            {
                joinBlock = blockIndex;
                break;
            }
        }

        if (joinBlock < 0 || predecessors[joinBlock].Count < 2)
        {
            throw new InvalidOperationException(
                "The L1 place-flow fixture has no shared scalar-return Block.");
        }

        var records = new List<ReadOnlyMemory<byte>>(
            body.RecordCount + predecessors[joinBlock].Count + 1);
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records.Add(body.GetRecord(recordIndex).ToArray());
        }

        var selectionRecordByPredecessor
            = new Dictionary<int, ushort>();
        foreach (var predecessor in predecessors[joinBlock])
        {
            var selection = body.GetRecord(originalSelection).ToArray();
            selection[5] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(
                selection.AsSpan(8), checked((ushort)placeRegister));
            if (mismatchedFalseEdge
                && predecessor == predecessors[joinBlock][^1])
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    selection.AsSpan(16),
                    checked((uint)alternateFieldRow));
            }

            var recordIndex = checked((ushort)records.Count);
            records.Add(selection);
            selectionRecordByPredecessor.Add(predecessor, recordIndex);
        }

        var entryStateRecord = checked((ushort)records.Count);
        var entryState = new byte[13];
        entryState[0] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(entryState.AsSpan(4), 1);
        entryState[8] = checked((byte)placeRegister);
        entryState[9] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(
            entryState.AsSpan(10), referentTypeMapIndex);
        entryState[12] = 1;
        records.Add(entryState);

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        var codeBytes = new List<byte>();
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var originalBlock = body.GetBlock(blockIndex);
            var originalCode = body.GetBlockCode(blockIndex);
            byte[] rewrittenCode;
            if (blockIndex == joinBlock)
            {
                rewrittenCode = [0x63, 0xD0, 0x17, 0];
            }
            else if (selectionRecordByPredecessor.TryGetValue(
                blockIndex, out var selectionRecordIndex))
            {
                var (terminalOffset, _) = FindBlockTerminal(originalCode,
                    body.Schema);
                rewrittenCode = new byte[originalCode.Length + 4];
                originalCode[..terminalOffset].CopyTo(
                    rewrittenCode.AsSpan());
                rewrittenCode[terminalOffset] = 0xD8;
                rewrittenCode[terminalOffset + 1]
                    = checked((byte)placeRegister);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    rewrittenCode.AsSpan(terminalOffset + 2),
                    selectionRecordIndex);
                originalCode[terminalOffset..].CopyTo(
                    rewrittenCode.AsSpan(terminalOffset + 4));
            }
            else
            {
                rewrittenCode = originalCode.ToArray();
            }

            var entryIndex = blockIndex == joinBlock
                ? entryStateRecord
                : BinaryPrimitives.ReadUInt16LittleEndian(
                    originalBlock[8..]);
            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                checked((uint)codeBytes.Count),
                checked((uint)rewrittenCode.Length), entryIndex);
            codeBytes.AddRange(rewrittenCode);
        }

        var encodedBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), body.FieldMap.ToArray(),
            body.ConstantMap.ToArray(), blocks, records,
            codeBytes.ToArray(), tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        return ReplaceBodyRow(artifact, bodyRow, encodedBody);
    }

    private static byte[] AddLiveResultHomeReplacementPreparation(
        byte[] artifact, int bodyRow)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var callSiteRecordIndex = -1;
        var resultHome = -1;
        byte[]? callSite = null;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x02 || record[5] != 2
                || BinaryPrimitives.ReadUInt32LittleEndian(
                    record[16..]) != 0)
            {
                continue;
            }

            var candidateHome
                = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            if (candidateHome >= body.HomeCount
                || body.HomeMap[candidateHome * 8] != 3
                || body.HomeMap[(candidateHome * 8) + 1] == 0)
            {
                continue;
            }

            callSiteRecordIndex = recordIndex;
            resultHome = candidateHome;
            callSite = record.ToArray();
            break;
        }

        if (callSiteRecordIndex < 0 || callSite is null)
        {
            throw new InvalidOperationException(
                "The L1 Body has no mutable local result-Home CallSite.");
        }

        var preparationRecordIndex
            = checked((ushort)body.RecordCount);
        var replacementCallSiteIndex
            = checked((ushort)(body.RecordCount + 1));
        var preparation = new byte[20];
        preparation[0] = 0x03;
        preparation[4] = 2;
        preparation[5] = 2;
        preparation[6] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(
            preparation.AsSpan(8), checked((ushort)resultHome));
        var inputCount = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(callSite.AsSpan(12)));
        var inputs
            = new List<TrRmRegisterBodyCallInput>(inputCount);
        for (var inputIndex = 0; inputIndex < inputCount; inputIndex++)
        {
            var inputOffset = 20 + (inputIndex * 4);
            inputs.Add(new TrRmRegisterBodyCallInput(
                callSite[inputOffset], callSite[inputOffset + 1],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    callSite.AsSpan(inputOffset + 2))));
        }

        var replacementCallSite
            = TrRmRegisterBodyRecordWriter.EncodeCallSite(
                callSite[4], callSite[5],
                BinaryPrimitives.ReadUInt16LittleEndian(
                    callSite.AsSpan(8)),
                checked((ushort)resultHome), inputs,
                [preparationRecordIndex]);
        var records = new List<ReadOnlyMemory<byte>>(
            body.RecordCount + 2);
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records.Add(body.GetRecord(recordIndex).ToArray());
        }
        records.Add(preparation);
        records.Add(replacementCallSite);

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        var code = new List<byte>();
        var inserted = false;
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var originalBlock = body.GetBlock(blockIndex);
            var originalCode = body.GetBlockCode(blockIndex);
            var rewrittenCode = new List<byte>(originalCode.Length + 4);
            for (var position = 0; position < originalCode.Length;)
            {
                var length = TrRmRegisterBodyReader.InstructionLength(
                    originalCode[position], body.Schema);
                var instruction = originalCode.Slice(position, length);
                var selectedRecordIndex = instruction[0] switch {
                    0x90 => instruction[1],
                    0xE0 => BinaryPrimitives.ReadUInt16LittleEndian(
                        instruction[2..]),
                    _ => -1,
                };
                rewrittenCode.AddRange(instruction.ToArray());
                if (!inserted
                    && selectedRecordIndex == callSiteRecordIndex)
                {
                    if (instruction[0] is not (0x90 or 0xE0))
                    {
                        throw new InvalidOperationException(
                            "The L1 CallSite fixture uses an unsupported instruction form.");
                    }

                    rewrittenCode.Add(0xE0);
                    rewrittenCode.Add(0);
                    rewrittenCode.Add((byte)replacementCallSiteIndex);
                    rewrittenCode.Add(
                        (byte)(replacementCallSiteIndex >> 8));
                    inserted = true;
                }

                position += length;
            }

            code.AddRange(rewrittenCode);

            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                checked((uint)(code.Count - rewrittenCode.Count)),
                checked((uint)rewrittenCode.Count),
                BinaryPrimitives.ReadUInt16LittleEndian(
                    originalBlock[8..]));
        }

        if (!inserted)
        {
            throw new InvalidOperationException(
                "The selected L1 CallSite has no instruction reference.");
        }

        var encodedBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), body.FieldMap.ToArray(),
            body.ConstantMap.ToArray(), blocks, records, code.ToArray(),
            tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        return ReplaceBodyRow(artifact, bodyRow, encodedBody);
    }

    private static byte[] AddResultHomePreparation(byte[] artifact,
        int bodyRow, byte priorState, byte transition)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var records = new List<ReadOnlyMemory<byte>>(
            body.RecordCount + 1);
        byte[]? preparation = null;
        var callSiteFound = false;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex).ToArray();
            if (!callSiteFound && record[0] == 0x02
                && record[5] == 2
                && BinaryPrimitives.ReadUInt32LittleEndian(
                    record.AsSpan(16)) == 0)
            {
                var inputCount = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        record.AsSpan(12)));
                var inputs
                    = new List<TrRmRegisterBodyCallInput>(inputCount);
                for (var inputIndex = 0; inputIndex < inputCount;
                    inputIndex++)
                {
                    var inputOffset = 20 + (inputIndex * 4);
                    inputs.Add(new TrRmRegisterBodyCallInput(
                        record[inputOffset], record[inputOffset + 1],
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            record.AsSpan(inputOffset + 2))));
                }

                var targetIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    record.AsSpan(8));
                var resultIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    record.AsSpan(10));
                var preparationIndex
                    = checked((ushort)body.RecordCount);
                preparation = new byte[20];
                preparation[0] = 0x03;
                preparation[4] = 2;
                preparation[5] = priorState;
                preparation[6] = transition;
                BinaryPrimitives.WriteUInt16LittleEndian(
                    preparation.AsSpan(8), resultIndex);
                records.Add(TrRmRegisterBodyRecordWriter.EncodeCallSite(
                    record[4], record[5], targetIndex, resultIndex,
                    inputs, [preparationIndex]));
                callSiteFound = true;
            }
            else
            {
                records.Add(record);
            }
        }

        if (!callSiteFound)
        {
            throw new InvalidOperationException(
                "The L1 test artifact has no result-Home CallSite.");
        }
        records.Add(preparation
            ?? throw new InvalidOperationException(
                "The L1 result-Home preparation was not constructed."));

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }

        var encodedBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(),
            body.MethodMap.ToArray(), body.FieldMap.ToArray(),
            body.ConstantMap.ToArray(), blocks, records,
            body.Code.ToArray(), tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        return ReplaceBodyRow(artifact, bodyRow, encodedBody);
    }

    private static byte[] ReplaceBodyRow(byte[] artifact, int bodyRow,
        byte[] replacementBody)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        if (!index.TryGetSection(0x0020, out var bodySection,
                out var bodyCount)
            || bodyRow <= 0 || bodyRow > bodyCount)
        {
            throw new InvalidOperationException(
                "The L1 test artifact has no selected Body row.");
        }

        var offsetBytes = checked((bodyCount + 1) * sizeof(uint));
        var bodyRows = new byte[bodyCount][];
        var bodyDataLength = 0;
        for (var row = 1; row <= bodyCount; row++)
        {
            var start = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[((row - 1)
                    * sizeof(uint))..]));
            var end = checked((int)BinaryPrimitives
                .ReadUInt32LittleEndian(bodySection[(row
                    * sizeof(uint))..]));
            var rowBytes = row == bodyRow
                ? (byte[])replacementBody.Clone()
                : bodySection.Slice(offsetBytes + start,
                    end - start).ToArray();
            bodyRows[row - 1] = rowBytes;
            bodyDataLength = checked(bodyDataLength + rowBytes.Length);
        }

        var newBodySection = new byte[checked(offsetBytes
            + bodyDataLength)];
        var nextOffset = 0;
        for (var rowIndex = 0; rowIndex < bodyRows.Length; rowIndex++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                newBodySection.AsSpan(rowIndex * sizeof(uint)),
                checked((uint)nextOffset));
            var rowBytes = bodyRows[rowIndex];
            rowBytes.CopyTo(newBodySection, offsetBytes + nextOffset);
            nextOffset = checked(nextOffset + rowBytes.Length);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(
            newBodySection.AsSpan(bodyCount * sizeof(uint)),
            checked((uint)nextOffset));
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                kind == 0x0020 ? newBodySection : section.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
    }

    private static byte[] ReplaceBodyMethodMapToken(byte[] artifact,
        int bodyRow, uint token, uint replacement)
    {
        var index = TrRmArtifactIndex.ForLocalAggregateLayoutDraft(artifact);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyReturnCoverageRepeatInitializationDraftAndGetExecutionTables(
                    index);
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var methodMap = body.MethodMap.ToArray();
        var replacements = 0;
        for (var mapIndex = 0; mapIndex < body.MethodCount; mapIndex++)
        {
            var offset = mapIndex * sizeof(uint);
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    methodMap.AsSpan(offset)) != token)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                methodMap.AsSpan(offset), replacement);
            replacements++;
        }

        if (replacements != 1)
        {
            throw new InvalidOperationException(
                "The L1 Body must contain exactly one selected MethodMap token.");
        }

        var blocks = new TrRmRegisterBodyBlock[body.BlockCount];
        for (var blockIndex = 0; blockIndex < body.BlockCount;
            blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks[blockIndex] = new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..]));
        }

        var records
            = new List<ReadOnlyMemory<byte>>(body.RecordCount);
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            records.Add(body.GetRecord(recordIndex).ToArray());
        }

        var encodedBody = TrRmRegisterBodyWriter.Encode(
            body.HomeMap.ToArray(), body.TypeMap.ToArray(), methodMap,
            body.FieldMap.ToArray(), body.ConstantMap.ToArray(), blocks,
            records, body.Code.ToArray(), tables.Contracts.Count,
            TrRmArtifactSchema.LocalAggregateLayoutDraft);
        return ReplaceBodyRow(artifact, bodyRow, encodedBody);
    }

    private static int FindSequence(byte[] bytes, byte[] sequence)
    {
        if (sequence.Length == 0)
        {
            return -1;
        }

        for (var start = 0; start <= bytes.Length - sequence.Length; start++)
        {
            if (bytes.AsSpan(start, sequence.Length).SequenceEqual(sequence))
            {
                return start;
            }
        }

        return -1;
    }
}
