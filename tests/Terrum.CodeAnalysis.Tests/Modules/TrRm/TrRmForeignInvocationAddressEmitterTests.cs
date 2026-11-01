// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmForeignInvocationAddressEmitterTests
{
    private static void AssertHomeFirstUseTypeMap(
        TrRmRegisterBodyReader body)
    {
        var firstTypeMapIndexByToken
            = new Dictionary<uint, ushort>(body.TypeCount);
        for (var homeIndex = 0; homeIndex < body.HomeCount; homeIndex++)
        {
            var home = body.HomeMap.Slice(homeIndex * 8, 8);
            var typeMapIndex
                = BinaryPrimitives.ReadUInt16LittleEndian(home[2..]);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint),
                    sizeof(uint)));
            if (!firstTypeMapIndexByToken.TryGetValue(token,
                    out var firstTypeMapIndex))
            {
                firstTypeMapIndex
                    = checked((ushort)firstTypeMapIndexByToken.Count);
                firstTypeMapIndexByToken.Add(token, firstTypeMapIndex);
            }

            Assert.That(typeMapIndex, Is.EqualTo(firstTypeMapIndex));
        }

        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] != 0x04)
            {
                continue;
            }

            var typeMapIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                record[20..]);
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                body.TypeMap.Slice(typeMapIndex * sizeof(uint),
                    sizeof(uint)));
            if (!firstTypeMapIndexByToken.TryGetValue(token,
                    out var firstTypeMapIndex))
            {
                firstTypeMapIndex
                    = checked((ushort)firstTypeMapIndexByToken.Count);
                firstTypeMapIndexByToken.Add(token, firstTypeMapIndex);
            }

            Assert.That(typeMapIndex, Is.EqualTo(firstTypeMapIndex));
        }

        Assert.That(firstTypeMapIndexByToken.Count,
            Is.EqualTo(body.TypeCount));
    }

    [Test]
    public void ExplicitNativeImportConventionProducesAnInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Observe();

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var target = program.Functions.Single(static function =>
            function.Name == "Observe");
        var form = program.Functions.Single(static function =>
            function.Name == "Form");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundNativeFunctionAddress address)
            {
                addresses.Add(address);
            }
        }), Is.True);

        var address = addresses.Single();
        Assert.That(address.Function, Is.EqualTo(
            Enumerable.Range(0, program.Functions.Length)
                .Single(index => program.Functions[index].Name == "Observe")));
        Assert.That(target.DirectNativeImport!.CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));
        Assert.That(program.NativeFunctionPointerSignatures[
            NativeFunctionPointerTypes.Index(address.Type)].CallingConvention,
            Is.EqualTo(NativeCallingConvention.Unmanaged));

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(program, "native-address");
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var targetRow = 0;
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            targetRow = name == "Observe" ? row : targetRow;
            callerRow = name == "Form" ? row : callerRow;
        }
        Assert.That(targetRow, Is.GreaterThan(0));
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.StandardNativeImports.HasImportForMethod(targetRow), Is.True);
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.MapMask, Is.EqualTo((byte)0x06));
        Assert.That(body.MethodMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(body.TypeMap.Length, Is.EqualTo(sizeof(uint)));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] { 0xE9, 0, 1, 0, 0x18, 0 }));
    }

    [Test]
    public void StorageAddressSchemaSelectionPreservesForeignInvocationLoading()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-address-storage-schema";
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern void Observe();

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageAddressDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(index);
        Assert.That(tables.Schema, Is.EqualTo(
            TrRmArtifactSchema.ForeignInvocationStorageAddressDraft));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport, Is.Not.Null);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Form").Body, Is.Not.Null);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(reference.Functions.Single(static function =>
            function.Name == "Observe").ModuleImport?.ProducerIdentity,
            Is.EqualTo(moduleIdentity));
    }

    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
        false, false, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
        false, true, false)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1",
        false, false, true)]
    [TestCase("trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1",
        true, false, false)]
    public void FixedArraySchemaSelectionPreservesStorageAddressLoading(
        string schemaIdentity, bool usesGenericSchema,
        bool usesEmptyInitializationSchema, bool usesUnitInitializationSchema)
    {
        const string moduleIdentity = "native-address-fixed-array-schema";
        var expectedSchema = usesGenericSchema
            ? TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
            : usesUnitInitializationSchema
                ? TrRmArtifactSchema
                    .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                : usesEmptyInitializationSchema
                    ? TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                    : TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayCopyDraft;
        var artifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        var index = expectedSchema switch
        {
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                        artifact),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                => TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact),
            _ => TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyDraft(artifact),
        };
        var tables = expectedSchema switch
        {
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayGenericCopyDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                        index),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                        index),
            TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft
                => TrRmStructuralMetadataValidator
                    .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                        index),
            _ => TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                    index),
        };
        Assert.That(tables.Schema, Is.EqualTo(expectedSchema));
        Assert.That(tables.FixedArrayLayouts.Count, Is.Zero);
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                caller = row;
                break;
            }
        }
        Assert.That(caller, Is.GreaterThan(0));
        var a3Ledger = tables.A3LayoutResourceLedger;
        var workRemainingBeforeBodyValidation = usesGenericSchema
            ? a3Ledger?.WorkRemaining
            : null;
        tables.ValidateRegisterBodyMapsForMethod(caller);
        if (usesGenericSchema)
        {
            Assert.That(a3Ledger, Is.Not.Null);
            Assert.That(a3Ledger!.WorkRemaining,
                Is.LessThan(workRemainingBeforeBodyValidation!.Value));
        }
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var hasStorageAddressRecord = false;
        for (var record = 0; record < body.RecordCount; record++)
        {
            hasStorageAddressRecord |= body.GetRecord(record)[0] == 0x0E;
        }
        Assert.That(hasStorageAddressRecord, Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "GetSystemTimeAdjustment").DirectNativeImport,
            Is.Not.Null);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Main").Body, Is.Not.Null);
    }

    [Test]
    public void StandardA2EInitializesEmptyLocalAndRejectsTheRecordUnderA2()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
        const string moduleIdentity = "a2e-empty-array-initialization";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                [uint32; 0] empty = [];
                int32 zeroIndex = 0;
                mut [[uint32; 0]; 2] nested = [[], []];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = compilation.Program!;
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2", 1));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(2));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var homePlaceSelections = 0;
        var elementPlaceSelections = 0;
        var nullaryStores = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x04 && record[4] == 1)
            {
                homePlaceSelections++;
            }
            else if (record[0] == 0x04 && record[4] == 3)
            {
                elementPlaceSelections++;
            }
            else if (record[0] == 0x05 && record[5] == 0)
            {
                nullaryStores++;
                Assert.That(record[6], Is.EqualTo(1));
                Assert.That(record[7], Is.Zero);
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                    record[10..]), Is.Zero);
            }
        }
        Assert.That(homePlaceSelections, Is.EqualTo(1));
        Assert.That(elementPlaceSelections, Is.EqualTo(2));
        Assert.That(nullaryStores, Is.EqualTo(3));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var emptyInitializations = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization initialization
                    && initialization.Layout.ElementCount == 0)
                {
                    emptyInitializations.Add(initialization);
                }
            }), Is.True);
        Assert.That(emptyInitializations, Has.Count.EqualTo(3));
        Assert.That(emptyInitializations.All(static initialization =>
            initialization.Elements.IsEmpty), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var a2Tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var a2CallerRow = 0;
        for (var row = 1; row <= a2Tables.Methods.Count; row++)
        {
            if (a2Tables.Methods.GetName(row) == "Main")
            {
                a2CallerRow = row;
                break;
            }
        }
        Assert.That(a2CallerRow, Is.GreaterThan(0));
        var rejectedByA2 = false;
        try
        {
            a2Tables.ValidateRegisterBodyMapsForMethod(a2CallerRow);
        }
        catch (InvalidDataException)
        {
            rejectedByA2 = true;
        }
        Assert.That(rejectedByA2, Is.True);

        foreach (var defect in new[] {
            "nonzero-source-index",
            "nonzero-source-use",
            "wrong-transition",
            "nonzero-dependency",
            "formal-present",
            "nonzero-flags",
            "nonzero-formal-ordinal",
            "wrong-place-kind",
            "wrong-home-access",
        })
        {
            var malformed = RewriteA2EEmptyInitializationArtifact(artifact,
                defect);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity, malformed),
                    moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        }

        var doubleInitialization
            = DuplicateA2EEmptyHomeInitialization(artifact);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    doubleInitialization),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
    }

    [TestCase(0, "")]
    [TestCase(1, "()")]
    [TestCase(2, "(), ()")]
    public void StandardA2EUStoresTypedUnitElementsAndRejectsA2AndA2E(
        int unitElementCount, string unitInitializers)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-initialization";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [void; {{unitElementCount}}] values = [{{unitInitializers}}];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var unsupportedSchemas = unitElementCount == 0
            ? new[] {
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
            }
            : new[] {
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
            };
        foreach (var unsupportedSchema in unsupportedSchemas)
        {
            _ = Assert.Throws<NotSupportedException>(() =>
                TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                    unsupportedSchema, 1));
        }

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var unitElementStores = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            var record = body.GetRecord(recordIndex);
            if (record[0] == 0x05 && record[4] == 1
                && record[5] == 1)
            {
                Assert.That(record[6], Is.EqualTo(1));
                Assert.That(record[7], Is.EqualTo(1));
                Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
                    record[10..]), Is.EqualTo(14));
                unitElementStores++;
            }
        }
        Assert.That(unitElementStores, Is.EqualTo(unitElementCount));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArray = new List<BoundNativeFixedArrayInitialization>();
        var unitConstants = new List<BoundConstant>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    initialization
                    && initialization.Layout.ElementCount
                        == (ulong)unitElementCount
                    && initialization.Layout.ElementType == PrimitiveType.Void)
                {
                    unitArray.Add(initialization);
                }
                else if (expression is BoundConstant
                    { Type: PrimitiveType.Void } constant)
                {
                    unitConstants.Add(constant);
                }
            }), Is.True);
        Assert.That(unitArray, Has.Count.EqualTo(1));
        Assert.That(unitArray[0].Elements,
            Has.Length.EqualTo(unitElementCount));
        Assert.That(unitConstants, Has.Count.EqualTo(unitElementCount));
        Assert.That(unitConstants.All(static constant => constant.Value == 0),
            Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var incompatibleSchemas = unitElementCount == 0
            ? new[] {
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
            }
            : new[] {
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2",
                "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1",
            };
        foreach (var unsupportedSchema in incompatibleSchemas)
        {
            _ = Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(unsupportedSchema,
                        artifact),
                    moduleIdentity,
                    Array.Empty<TrRmStandardArtifactInput>()));
        }

        string[] defects = unitElementCount == 0
            ? []
            : [
                "unit-source-r15",
                "unit-store-source-use",
                "unit-store-transition",
                "unit-store-place-kind",
                "unit-store-dependency",
                "unit-store-formal",
            ];
        foreach (var defect in defects)
        {
            var malformed = RewriteA2EUUnitStoreArtifact(artifact, defect);
            Assert.Throws<InvalidDataException>(() =>
                TrRmExecutableCompiler.LoadStandard(
                    new TrRmStandardArtifactInput(schemaIdentity, malformed),
                    moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        }
    }

    [Test]
    public void StandardA2EUCapturesUnitInitializerCallsBeforeElementStores()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-effect-calls";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint32 input1 = 0;
                mut int32 output1 = 0;
                mut uint32 input2 = 0;
                mut int32 output2 = 0;
                unsafe {
                    mut [void; 2] values = [
                        Observe(&input1, &output1),
                        Observe(&input2, &output2)
                    ];
                    var callback = &Observe;
                    callback(&input1, &output1);
                }
                return output1;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);

        var directCallRecords = new List<int>();
        var code = body.GetBlockCode(0);
        var instructionOffsets = new Dictionary<int, int>();
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xE0)
            {
                var recordIndex = BinaryPrimitives.ReadUInt16LittleEndian(
                    code[(position + 2)..]);
                instructionOffsets.Add(recordIndex, position);
                if (body.GetRecord(recordIndex)[4] == 1)
                {
                    directCallRecords.Add(recordIndex);
                }
            }
            position += instructionLength;
        }

        Assert.That(directCallRecords, Has.Count.EqualTo(2));
        var firstUnitStoreOffset = int.MaxValue;
        var inputSourceHomes = new List<ushort>();
        var outputSourceHomes = new List<ushort>();
        foreach (var record in directCallRecords)
        {
            var callRecord = body.GetRecord(record);
            foreach (var (inputOffset, sourceHomes) in new[] {
                (22, inputSourceHomes),
                (26, outputSourceHomes),
            })
            {
                var inputHome = BinaryPrimitives.ReadUInt16LittleEndian(
                    callRecord[inputOffset..]);
                var foundStorageRecord = false;
                for (var recordIndex = 0;
                    recordIndex < body.RecordCount;
                    recordIndex++)
                {
                    var storageRecord = body.GetRecord(recordIndex);
                    if (storageRecord[0] != 0x0E
                        || BinaryPrimitives.ReadUInt16LittleEndian(
                            storageRecord[8..]) != inputHome)
                    {
                        continue;
                    }

                    Assert.That(storageRecord[4], Is.EqualTo(2));
                    sourceHomes.Add(BinaryPrimitives
                        .ReadUInt16LittleEndian(storageRecord[6..]));
                    foundStorageRecord = true;
                    break;
                }
                Assert.That(foundStorageRecord, Is.True);
            }
        }
        Assert.That(inputSourceHomes[0], Is.LessThan(inputSourceHomes[1]));
        Assert.That(outputSourceHomes[0], Is.LessThan(outputSourceHomes[1]));
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xD9)
            {
                firstUnitStoreOffset = Math.Min(firstUnitStoreOffset, position);
            }
            position += instructionLength;
        }
        Assert.That(instructionOffsets[directCallRecords[^1]],
            Is.LessThan(firstUnitStoreOffset));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArrays = new List<BoundNativeFixedArrayInitialization>();
        var initializerCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    { Layout.ElementType: PrimitiveType.Void } initialization
                    && initialization.Layout.ElementCount == 2)
                {
                    unitArrays.Add(initialization);
                }
                if (expression is BoundCall call)
                {
                    initializerCalls.Add(call);
                }
            }), Is.True);
        Assert.That(unitArrays, Has.Count.EqualTo(1));
        Assert.That(unitArrays[0].Elements.OfType<BoundConstant>().ToArray(),
            Has.Length.EqualTo(2));
        Assert.That(initializerCalls, Has.Count.EqualTo(2));
        Assert.That(initializerCalls.All(static call => call.Arguments
            .OfType<BoundNativeStorageAddress>().Count() == 2), Is.True);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCallsDistinctUnitImportsInSourceOrder()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-distinct-calls";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void MarkFirst();

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void MarkSecond();

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 2] values = [
                        MarkFirst(),
                        MarkSecond()
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        var directCallTargets = new List<string>();
        var lastDirectCallOffset = -1;
        var firstStoreOffset = int.MaxValue;
        var code = body.GetBlockCode(0);
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    var methodMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[8..]);
                    var methodToken
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            body.MethodMap[(methodMapIndex * sizeof(uint))..]);
                    var methodRow = checked((int)(methodToken
                        & 0x00FF_FFFF));
                    directCallTargets.Add(tables.Methods.GetName(methodRow));
                    lastDirectCallOffset = position;
                }
            }
            else if (code[position] == 0xD9)
            {
                firstStoreOffset = Math.Min(firstStoreOffset, position);
            }
            position += instructionLength;
        }
        Assert.That(directCallTargets,
            Is.EqualTo(new[] { "MarkFirst", "MarkSecond" }));
        Assert.That(lastDirectCallOffset, Is.LessThan(firstStoreOffset));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArrays = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    { Layout.ElementType: PrimitiveType.Void } initialization
                    && initialization.Layout.ElementCount == 2)
                {
                    unitArrays.Add(initialization);
                }
            }), Is.True);
        Assert.That(unitArrays, Has.Count.EqualTo(1));
        Assert.That(unitArrays[0].Elements.OfType<BoundConstant>().ToArray(),
            Has.Length.EqualTo(2));
        var unsafeBody = loadedMain.Body.Statements
            .OfType<BoundUnsafe>().Single().Body;
        var callStatementTargets = unsafeBody.Statements
            .OfType<BoundExpressionStatement>()
            .Select(static statement => statement.Expression)
            .OfType<BoundCall>()
            .Select(call => loaded.Functions[call.Function].Name)
            .ToArray();
        Assert.That(callStatementTargets,
            Is.EqualTo(new[] { "MarkFirst", "MarkSecond" }));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCapturesNestedScalarCallArgumentsBeforeMutation()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-nested-call";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern int32 MutateAndGet(write ptr<int32> value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordPair(int32 first, int32 second);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<uint32> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut int32 marker = 0;
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 1] values = [
                        RecordPair(marker, MutateAndGet(&marker))
                    ];
                    var callback = &Observe;
                    callback(&input, &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                        artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);

        var directCallTargets = new List<string>();
        var directCallResultKinds = new List<byte>();
        var directCallRecords = new List<ReadOnlyMemory<byte>>();
        var code = body.GetBlockCode(0);
        var firstCopyOffset = int.MaxValue;
        var firstDirectCallOffset = int.MaxValue;
        var lastDirectCallOffset = -1;
        var firstUnitStoreOffset = int.MaxValue;
        for (var position = 0; position < code.Length;)
        {
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                code[position], body.Schema);
            if (code[position] == 0xD5 || code[position] == 0xD6)
            {
                firstCopyOffset = Math.Min(firstCopyOffset, position);
            }
            if (code[position] == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    var methodMapIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            callRecord[8..]);
                    var methodToken
                        = BinaryPrimitives.ReadUInt32LittleEndian(
                            body.MethodMap[(methodMapIndex * sizeof(uint))..]);
                    var methodRow = checked((int)(methodToken
                        & 0x00FF_FFFF));
                    directCallTargets.Add(tables.Methods.GetName(methodRow));
                    directCallResultKinds.Add(callRecord[5]);
                    directCallRecords.Add(callRecord.ToArray());
                    firstDirectCallOffset = Math.Min(firstDirectCallOffset,
                        position);
                    lastDirectCallOffset = position;
                }
            }
            else if (code[position] == 0xD9)
            {
                firstUnitStoreOffset = Math.Min(firstUnitStoreOffset,
                    position);
            }
            position += instructionLength;
        }
        Assert.That(firstCopyOffset, Is.LessThan(firstDirectCallOffset));
        Assert.That(directCallTargets,
            Is.EqualTo(new[] { "MutateAndGet", "RecordPair" }));
        Assert.That(directCallResultKinds,
            Is.EqualTo(new byte[] { 2, 0 }));
        Assert.That(lastDirectCallOffset, Is.LessThan(firstUnitStoreOffset));
        var nestedResultHome = BinaryPrimitives.ReadUInt16LittleEndian(
            directCallRecords[0].Span[10..]);
        var recordPairCall = directCallRecords[1].Span;
        var snapshotHome = BinaryPrimitives.ReadUInt16LittleEndian(
            recordPairCall[22..]);
        var nestedResultInputHome = BinaryPrimitives.ReadUInt16LittleEndian(
            recordPairCall[26..]);
        Assert.That(snapshotHome, Is.Not.EqualTo(nestedResultHome));
        Assert.That(nestedResultInputHome, Is.EqualTo(nestedResultHome));
        Assert.That(body.HomeMap[nestedResultHome * 8], Is.EqualTo(3));
        Assert.That(body.HomeMap[snapshotHome * 8], Is.EqualTo(3));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var unitArrays = new List<BoundNativeFixedArrayInitialization>();
        var initializerCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization
                    { Layout.ElementType: PrimitiveType.Void } initialization
                    && initialization.Layout.ElementCount == 1)
                {
                    unitArrays.Add(initialization);
                }
                if (expression is BoundCall call)
                {
                    initializerCalls.Add(call);
                }
            }), Is.True);
        Assert.That(unitArrays, Has.Count.EqualTo(1));
        Assert.That(unitArrays[0].Elements.OfType<BoundConstant>().ToArray(),
            Has.Length.EqualTo(1));
        Assert.That(initializerCalls.Select(call =>
                loaded.Functions[call.Function].Name),
            Is.EquivalentTo(new[] { "MutateAndGet", "RecordPair" }));
        var recordPair = initializerCalls.Single(call =>
            loaded.Functions[call.Function].Name == "RecordPair");
        Assert.That(recordPair.Arguments.OfType<BoundRead>().ToArray(),
            Has.Length.EqualTo(2));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [TestCase(1, "uint32", "[0]", PrimitiveType.UInt32, false)]
    [TestCase(3, "uint32", "[0, 0, 0]", PrimitiveType.UInt32, false)]
    [TestCase(2, "int32", "[0, -1]", PrimitiveType.Int32, true)]
    public void StandardA2EmitsAndReconstructsInitializedArrayStorageAddress(
        int elementCount, string elementType, string initializer,
        PrimitiveType expectedElementType, bool hasAllBitsSetElement)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-storage-address";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            internal extern void Observe(
                ptr<{{elementType}}> value,
                write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [{{elementType}}; {{elementCount}}] values = {{initializer}};
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&values[zeroIndex], &output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(compilation.Program!,
                moduleIdentity,
                "trrm/1-draft-foreign-invocation-storage-addresses-1", 1));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(1));
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
            out _), Is.True);

        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                callerRow = row;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);
        var placeSelectionCount = 0;
        var semanticStoreCount = 0;
        var storageAddressCount = 0;
        var initializerRegisters = new List<ushort>();
        for (var record = 0; record < body.RecordCount; record++)
        {
            var bodyRecord = body.GetRecord(record);
            switch (bodyRecord[0])
            {
                case 0x04:
                    placeSelectionCount++;
                    break;
                case 0x05:
                    semanticStoreCount++;
                    initializerRegisters.Add(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            bodyRecord[10..]));
                    break;
                case 0x0E:
                    storageAddressCount++;
                    break;
            }
        }
        Assert.That(placeSelectionCount, Is.EqualTo(elementCount + 1));
        Assert.That(semanticStoreCount, Is.EqualTo(elementCount));
        Assert.That(storageAddressCount, Is.EqualTo(2));
        var expectedInitializerRegisters = hasAllBitsSetElement
            ? new ushort[] { 14, 15 }
            : Enumerable.Repeat((ushort)14, elementCount).ToArray();
        Assert.That(initializerRegisters,
            Is.EqualTo(expectedInitializerRegisters));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var arrayInitializations = new List<BoundNativeFixedArrayInitialization>();
        var storageAddresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression
                    is BoundNativeFixedArrayInitialization initialization)
                {
                    arrayInitializations.Add(initialization);
                }
                else if (expression is BoundNativeStorageAddress address)
                {
                    storageAddresses.Add(address);
                }
            }), Is.True);
        Assert.That(arrayInitializations, Has.Count.EqualTo(1));
        Assert.That(arrayInitializations[0].Elements.Length,
                Is.EqualTo(elementCount));
        Assert.That(storageAddresses, Has.Count.EqualTo(2));
        var arrayAddress = storageAddresses.Single(address =>
            NativeFixedArrayTypes.IsType(address.CarrierType));
        Assert.That(arrayAddress.ElementType, Is.EqualTo(expectedElementType));
        Assert.That(arrayAddress.ElementCount, Is.EqualTo((ulong)elementCount));
        Assert.That(arrayAddress.ElementStride, Is.EqualTo(4));
        Assert.That(arrayAddress.ByteExtent, Is.EqualTo(4));
        Assert.That(arrayAddress.Index, Is.TypeOf<BoundRead>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2DeclarationProducerPublishesCheckedCopyEvidence()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-copy-declarations";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Scalar {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Pair {
                public Scalar Low;
                public Scalar High;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(2));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.True);
        Assert.That(tables.TypeDefs.HasDeclaredCopy(2), Is.True);
        Assert.That(tables.TypeDefs.GetFlags(1) & 0x20, Is.EqualTo(0x20));
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            out var feature), Is.True);
        Assert.That(feature.RuleRevision, Is.EqualTo(1));
        Assert.That(tables.FixedArrayLayouts.Count, Is.Zero);
    }

    [Test]
    public void StandardA2SourceTypeExportsUseExplicitKeysOnly()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-explicit-type-exports";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct SourceName {
                public int32 Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var noExports = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);
        var noExportsWithEmptyMap = TrRmExecutableCompiler.EmitStandard(
            program, moduleIdentity, schemaIdentity, 1, []);
        var noExportTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(noExports));
        var emptyMapTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(
                        noExportsWithEmptyMap));
        Assert.That(noExportTables.Exports.TryGetDefinitionForKey(1,
            "SourceName", out _), Is.False);
        Assert.That(emptyMapTables.Exports.TryGetDefinitionForKey(1,
            "SourceName", out _), Is.False);

        const string exportKey = "library/types/source-name-v1";
        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1,
            [new TrRmSourceTypeExport("", "SourceName", exportKey)]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1,
            "unselected", out _), Is.False);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, exportKey,
            out var definition), Is.True);
        Assert.That(definition, Is.EqualTo(0x1300_0001u));
        Assert.That(tables.Exports.TryGetExportKeyForDefinition(1,
            definition, out var retainedKey), Is.True);
        Assert.That(retainedKey, Is.EqualTo(exportKey));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Structures.Single().Name, Is.EqualTo("SourceName"));
    }

    [Test]
    public void StandardA2SourceTypeExportsRequireNominalClosureMappings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-type-export-closure";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public [Element; 2] Values;
                public ptr<Element> Pointer;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(program.NativePointerTypes, Has.Length.EqualTo(1));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(program, moduleIdentity,
                schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Container", "container")]));

        var artifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1,
        [
            new TrRmSourceTypeExport("", "Container", "container"),
            new TrRmSourceTypeExport("", "Element", "element"),
        ]);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "container",
            out var containerDefinition), Is.True);
        Assert.That(tables.Exports.TryGetDefinitionForKey(1, "element",
            out var elementDefinition), Is.True);
        Assert.That(containerDefinition, Is.EqualTo(0x1300_0002u));
        Assert.That(elementDefinition, Is.EqualTo(0x1300_0001u));
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Structures, Has.Length.EqualTo(2));
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
        Assert.That(loaded.NativePointerTypes, Is.EqualTo(program.NativePointerTypes));
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        var reloaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, reemitted),
            moduleIdentity, []);
        Assert.That(reloaded.NativePointerTypes,
            Is.EqualTo(loaded.NativePointerTypes));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, reemitted),
                moduleIdentity, [], ["unused-key"]));
        var stalePointerProgram = loaded with {
            NativePointerTypes = loaded.NativePointerTypes.SetItem(0,
                loaded.NativePointerTypes[0] with {
                    PointeeType = PrimitiveType.Int32,
                }),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(stalePointerProgram,
                moduleIdentity, schemaIdentity, 1));

        var pointerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Element {
                    public int32 Value;
                }
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct PointerContainer {
                    public ptr<Element> Value;
                }
                """));
        Assert.That(pointerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, pointerCompilation.Diagnostics));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(pointerCompilation.Program!,
                "a2-pointer-export-closure", schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "PointerContainer",
                    "pointer-container")]));
        var pointerArtifact = TrRmExecutableCompiler.EmitStandard(
            pointerCompilation.Program!, "a2-pointer-export-closure",
            schemaIdentity, 1,
            [
                new TrRmSourceTypeExport("", "PointerContainer",
                    "pointer-container"),
                new TrRmSourceTypeExport("", "Element", "element"),
            ]);
        var pointerReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity, pointerArtifact),
                "a2-pointer-export-closure", [], ["pointer-container"]);
        Assert.That(pointerReference.Structures.Any(static structure
            => structure.Name == "Element"), Is.True);
    }

    [Test]
    public void StandardA2TypeReferenceSelectsRootsAndRetainsLocalOrigins()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-type-reference-provider";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Container {
                public [Element; 2] Values;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Unused {
                public int32 Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1,
        [
            new TrRmSourceTypeExport("", "Container", "container-key"),
            new TrRmSourceTypeExport("", "Element", "element-key"),
        ]);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, [], ["container-key"]);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            artifact);
        var loadedProvider = TrRmExecutableCompiler.LoadStandard(
            providerInput, moduleIdentity, []);
        var reemittedProvider = TrRmExecutableCompiler.EmitStandard(
            loadedProvider, moduleIdentity, schemaIdentity, 1);
        var reemittedReference
            = TrRmExecutableCompiler.CreateStandardReference(
                new TrRmStandardArtifactInput(schemaIdentity,
                    reemittedProvider),
                moduleIdentity, [], ["container-key"]);
        Assert.That(reemittedReference.Structures.Any(static structure
            => structure.Name == "Element"), Is.True);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(loadedProvider,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Container",
                    "replacement-container-key")]));

        Assert.That(reference.Structures, Has.Length.EqualTo(2));
        Assert.That(reference.Structures.Any(static structure
            => structure.Name == "Unused"), Is.False);
        var container = reference.Structures.Single(static structure
            => structure.Name == "Container");
        var containerOrigin = container.StandardTypeOrigin;
        Assert.That(containerOrigin, Is.Not.Null);
        Assert.That(containerOrigin!.IsLookupRoot, Is.True);
        Assert.That(containerOrigin.Provider.SchemaIdentity,
            Is.EqualTo(schemaIdentity));
        Assert.That(containerOrigin.Provider.ModuleIdentity,
            Is.EqualTo(moduleIdentity));
        Assert.That(containerOrigin.Provider.GoverningRuleEdition, Is.EqualTo(1));
        Assert.That(containerOrigin.Provider.TargetRequirement, Is.EqualTo(1));
        Assert.That(containerOrigin.Provider.RuntimePolicy, Is.Zero);
        Assert.That(containerOrigin.Provider.ArtifactSnapshot.AsSpan()
            .SequenceEqual(artifact), Is.True);
        Assert.That(containerOrigin.DefinitionId, Is.EqualTo(0x1300_0002u));
        Assert.That(containerOrigin.ExportKeys, Has.Length.EqualTo(1));
        Assert.That(containerOrigin.ExportKeys[0], Is.EqualTo("container-key"));
        var element = reference.Structures.Single(static structure
            => structure.Name == "Element");
        Assert.That(element.StandardTypeOrigin!.IsLookupRoot, Is.False);
        Assert.That(element.StandardTypeOrigin.ExportKeys, Has.Length.EqualTo(1));
        Assert.That(element.StandardTypeOrigin.ExportKeys[0],
            Is.EqualTo("element-key"));
        Assert.That(reference.NativeFixedArrayTypes, Has.Length.EqualTo(1));
        var multipleRoots = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, moduleIdentity, [],
            ["container-key", "element-key"]);
        Assert.That(multipleRoots.Structures, Has.Length.EqualTo(2));
        Assert.That(multipleRoots.Structures.All(static structure
            => structure.StandardTypeOrigin!.IsLookupRoot), Is.True);
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(providerInput,
                moduleIdentity, [], []));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(providerInput,
                moduleIdentity, [], ["missing-key"]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.CreateStandardReference(providerInput,
                moduleIdentity, [providerInput], ["container-key"]));

        var consumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Consumer {
                public Container Value;
                public [Container; 2] Values;
            }
            """), reference);
        Assert.That(consumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumer.Diagnostics));
        Assert.That(consumer.Program!.Structures[1].StandardTypeOrigin
            ?.IsLookupRoot, Is.True);
        Assert.That(consumer.Program.NativeFixedArrayTypes, Has.Length.EqualTo(2));

        var unexportedConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumer.Program!, "a2-type-reference-consumer", schemaIdentity, 1);
        var loadedUnexportedConsumer = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                unexportedConsumerArtifact),
            "a2-type-reference-consumer", [providerInput]);
        Assert.That(loadedUnexportedConsumer.Structures,
            Has.Length.EqualTo(3));
        Assert.That(loadedUnexportedConsumer.NativeFixedArrayTypes,
            Has.Length.EqualTo(2));

        var staleArrayProgram = consumer.Program! with {
            NativeFixedArrayTypes = consumer.Program.NativeFixedArrayTypes
                .SetItem(0, consumer.Program.NativeFixedArrayTypes[0] with {
                    ElementType = PrimitiveType.Int32,
                }),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(staleArrayProgram,
                "a2-type-reference-consumer", schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Consumer", "ConsumerKey")]));

        var closureLookup = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Blittable] [Layout(LayoutKind.Sequential)]
                public struct Consumer {
                    public Element Value;
                }
                """), reference);
        Assert.That(closureLookup.HasErrors, Is.True);

        var functionConsumer = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Read() {
                return 0;
            }
            """), reference);
        Assert.That(functionConsumer.HasErrors, Is.False,
            string.Join(Environment.NewLine, functionConsumer.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(functionConsumer.Program!,
                ["Read"]));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.CreateProducer(functionConsumer.Program!,
                ["Read"], "A2TypeReferenceConsumer",
                NativeRuntimePolicy.FailFast));
    }

    [Test]
    public void StandardA2SourceTypeExportsRejectHandBuiltAndDuplicateMappings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-invalid-type-exports";
        var handBuilt = new BoundProgram([], -1)
        {
            Structures = [
                new BoundStruct("Manual", [
                    new BoundStructField("Value", PrimitiveType.Int32, 0, 4),
                ], 4)
                {
                    IsSequential = true,
                    BlittableTargetRequirement = 1,
                },
            ],
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.EmitStandard(handBuilt,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Manual", "manual")]));

        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct First {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Second {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            struct Internal {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct Unique {
                public int32 Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var changedProgram = program with {
            Structures = program.Structures.SetItem(0,
                program.Structures[0] with { Name = "Changed" }),
        };

        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
            [
                new TrRmSourceTypeExport("", "First", "duplicate"),
                new TrRmSourceTypeExport("", "Second", "duplicate"),
            ]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
            [
                new TrRmSourceTypeExport("", "First", "first"),
                new TrRmSourceTypeExport("", "First", "second"),
            ]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(changedProgram,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "First", "changed")]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Internal", "internal")]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "Unique", "unique")]));
        _ = Assert.Throws<ArgumentException>(() =>
            TrRmExecutableCompiler.EmitStandard(program,
                moduleIdentity, schemaIdentity, 1,
                [new TrRmSourceTypeExport("", "First", "")]));
    }

    [Test]
    public void StandardA2DeclarationProducerDoesNotClaimCopyForUniqueStructs()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-unique-declarations";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public unique struct UniquePayload {
                public int32 Value;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.Structures.Single().IsUnique,
            Is.True);

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.TypeDefs.Count, Is.EqualTo(1));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.False);
        Assert.That(tables.TypeDefs.GetFlags(1) & 0x20, Is.Zero);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.RootlessNativeValuesFeatureId,
            out _), Is.False);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                moduleIdentity, []));
    }

    [Test]
    public void StandardA2DeclarationProducerEmitsNestedArrayTypeMetadata()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-field-declarations";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct ArrayHolder {
                public [[int32; 2]; 3] Values;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.NativeFixedArrayTypes,
            Has.Length.EqualTo(2));

        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));

        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(2));
        var innerLayout = tables.FixedArrayLayouts.GetRow(1);
        var outerLayout = tables.FixedArrayLayouts.GetRow(2);
        Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(
            (uint)innerLayout.ArrayTypeSpecRow, out _, out var innerCount),
            Is.True);
        Assert.That(innerCount, Is.EqualTo(2UL));
        Assert.That(tables.TypeSpecs.TryGetFixedInlineArray(
            (uint)outerLayout.ArrayTypeSpecRow,
            out var outerElementType, out var outerCount), Is.True);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(outerElementType).Form,
            Is.EqualTo((byte)0x11));
        Assert.That(outerCount, Is.EqualTo(3UL));
        Assert.That(innerLayout.ByteExtent, Is.EqualTo(8UL));
        Assert.That(outerLayout.ByteExtent, Is.EqualTo(24UL));
        Assert.That(tables.TypeDefs.HasDeclaredCopy(1), Is.True);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
            out var arrayFeature), Is.True);
        Assert.That(arrayFeature.RuleRevision, Is.EqualTo(1));
    }

    [Test]
    public void StandardA2TypeLibraryLoaderReconstructsCopyArrayFields()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-array-field-type-library";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Scalar {
                public int32 Value;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct ArrayHolder {
                public Scalar Payload;
                public [[int32; 2]; 3] Values;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);

        Assert.That(loaded.Functions, Is.Empty);
        Assert.That(loaded.EntryPoint, Is.EqualTo(-1));
        Assert.That(loaded.Structures, Has.Length.EqualTo(2));
        Assert.That(loaded.Structures[0].Name, Is.EqualTo("Scalar"));
        Assert.That(loaded.Structures[1].Name, Is.EqualTo("ArrayHolder"));
        Assert.That(loaded.Structures[1].Fields, Has.Length.EqualTo(2));
        Assert.That(loaded.Structures[1].Fields[0].Type,
            Is.EqualTo((PrimitiveType)100));
        Assert.That(loaded.Structures[1].Fields[1].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(1)));
        Assert.That(loaded.Structures[1].Size, Is.EqualTo(28));
        Assert.That(loaded.NativeFixedArrayTypes, Has.Length.EqualTo(2));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementCount,
            Is.EqualTo(3UL));
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
    }

    [Test]
    public void StandardA2TypeLibraryLoaderReconstructsLocalNominalArrayElements()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nominal-array-elements";
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Element {
                public [uint32; 2] Values;
            }
            [Blittable] [Layout(LayoutKind.Sequential)]
            public struct Holder {
                public [Element; 3] Items;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);

        Assert.That(loaded.Structures, Has.Length.EqualTo(2));
        Assert.That(loaded.Structures[0].Fields[0].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(0)));
        Assert.That(loaded.Structures[0].Size, Is.EqualTo(8));
        Assert.That(loaded.Structures[1].Fields[0].Type,
            Is.EqualTo(NativeFixedArrayTypes.Type(1)));
        Assert.That(loaded.Structures[1].Size, Is.EqualTo(24));
        Assert.That(loaded.NativeFixedArrayTypes, Has.Length.EqualTo(2));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementType,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementType,
            Is.EqualTo((PrimitiveType)100));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementCount,
            Is.EqualTo(3UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementStride,
            Is.EqualTo(8UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ByteExtent,
            Is.EqualTo(24UL));
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
    }

    [Test]
    public void StandardA2LoaderReconstructsNestedAndWideArrayTypes()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "native-address-array-types";
        var artifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        var firstArrayTypeSpecRow = tables.TypeSpecs.Count + 1;
        var functionPointerTypeSpecRow = 0;
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            if (functionPointerTypeSpecRow == 0
                && tables.NativeFunctionPointerAnchors
                    .HasAnchorForTypeSpec(row))
            {
                functionPointerTypeSpecRow = row;
            }
        }
        Assert.That(functionPointerTypeSpecRow, Is.GreaterThan(0));
        var wideCount = 1UL << 32;
        var arrayTypes = new (uint ElementTypeSpecRef, ulong ElementCount,
            ulong ElementStride, ulong ByteExtent, ulong Alignment)[]
        {
            (0xF200_0008, 2, 4, 8, 4),
            (0xF200_0008, wideCount, 4, wideCount * 4, 4),
            ((uint)firstArrayTypeSpecRow, 3, 8, 24, 4),
            (0xF200_0000, ulong.MaxValue, 0, 0, 1),
            (0xF300_8C08, 2, 8, 16, 8),
            ((uint)functionPointerTypeSpecRow, 2, 8, 16, 8),
        };
        artifact = AddA2FixedArrayTypes(artifact, arrayTypes);

        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(loaded.NativeFixedArrayTypes, Has.Length.EqualTo(6));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementType,
            Is.EqualTo(PrimitiveType.Int32));
        Assert.That(loaded.NativeFixedArrayTypes[0].ElementCount,
            Is.EqualTo(2UL));
        Assert.That(loaded.NativeFixedArrayTypes[1].ElementCount,
            Is.EqualTo(wideCount));
        Assert.That(loaded.NativeFixedArrayTypes[1].ByteExtent,
            Is.EqualTo(wideCount * 4));
        Assert.That(loaded.NativeFixedArrayTypes[2].ElementType,
            Is.EqualTo(NativeFixedArrayTypes.Type(0)));
        Assert.That(loaded.NativeFixedArrayTypes[3].ElementType,
            Is.EqualTo(PrimitiveType.Void));
        Assert.That(loaded.NativeFixedArrayTypes[3].ElementCount,
            Is.EqualTo(ulong.MaxValue));
        Assert.That(loaded.NativeFixedArrayTypes[3].ByteExtent, Is.Zero);
        Assert.That(NativePointerTypes.IsType(
            loaded.NativeFixedArrayTypes[4].ElementType), Is.True);
        Assert.That(NativeFunctionPointerTypes.IsType(
            loaded.NativeFixedArrayTypes[5].ElementType), Is.True);
        Assert.That(NativeFixedArrayContract.MatchesProgram(loaded), Is.True);
    }

    [Test]
    public void StandardA2LoaderRejectsInconsistentFixedArrayGeometry()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "native-address-invalid-array-types";
        var artifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        artifact = AddA2FixedArrayTypes(artifact,
        [
            (0xF200_0008, 4UL, 4UL, 16UL, 4UL),
        ], corruptFirstExtent: true);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, artifact),
                moduleIdentity, []));
    }

    [Test]
    public void EmitsInitializedLocalStorageAddressForNativeParameter()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1";
        const string moduleIdentity = "native-storage-address";
        var artifact = CreateStorageAddressArtifact(moduleIdentity);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageAddressDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                caller = row;
                break;
            }
        }
        Assert.That(caller, Is.GreaterThan(0));
        var module = tables.Methods.GetOwnerModule(caller);
        Assert.That(tables.Features.Count, Is.EqualTo(2));
        var featureIds = new uint[tables.Features.Count];
        for (var index = 0; index < featureIds.Length; index++)
        {
            featureIds[index] = tables.Features.GetFeature(index + 1).FeatureId;
        }
        Assert.That(featureIds, Is.EquivalentTo(new uint[] { 4, 16 }));
        foreach (var featureId in new uint[] { 1, 2, 3, 4, 16 })
        {
            Assert.That(TrRmFeatureManifestValidator.Requires(
                tables.Features, tables.Modules, module, featureId,
                tables.Schema), Is.True);
        }
        Assert.That(TrRmFeatureManifestValidator.Requires(
            tables.Features, tables.Modules, module,
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId,
            tables.Schema), Is.False);
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);

        Assert.That(body.MapMask, Is.EqualTo((byte)0x07));
        Assert.That(body.HomeMap.Length, Is.EqualTo(56));
        AssertHomeFirstUseTypeMap(body);

        var targetHomeTypeMapIndex
            = BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice(3 * 8 + 2, sizeof(ushort)));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(1)[10..]), Is.EqualTo(targetHomeTypeMapIndex));
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x0E));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(2));
        Assert.That(body.GetRecord(2)[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(2)[10..]),
            Is.EqualTo(BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice(4 * 8 + 2, sizeof(ushort)))));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(2)[8..]), Is.EqualTo(4));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(
            body.GetRecord(2)[12..]), Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[0], Is.EqualTo(0x0E));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(3)[10..]),
            Is.EqualTo(BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice(5 * 8 + 2, sizeof(ushort)))));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(3)[8..]), Is.EqualTo(5));
        Assert.That(body.GetRecord(4)[0], Is.EqualTo(0x0E));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(4)[10..]),
            Is.EqualTo(BinaryPrimitives.ReadUInt16LittleEndian(
                body.HomeMap.Slice(6 * 8 + 2, sizeof(ushort)))));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            body.GetRecord(4)[8..]), Is.EqualTo(6));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0x14, 0xE0, 0x14, 0xE1, 0x14, 0xF2,
                0xE9, 0, 1, 0,
                0xEC, 0, 2, 0, 0xEC, 0, 3, 0, 0xEC, 0, 4, 0,
                0xE0, 0, 5, 0,
                0xB8, 2,
            }));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var storageAddresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundNativeStorageAddress storageAddress)
                {
                    storageAddresses.Add(storageAddress);
                }
            }), Is.True);
        Assert.That(storageAddresses, Has.Count.EqualTo(3));
        Assert.That(storageAddresses.Select(address =>
            loadedMain.Variables[address.Variable].Type),
            Is.EquivalentTo(new[] {
                PrimitiveType.UInt32, PrimitiveType.UInt32, PrimitiveType.Int32,
            }));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(
                    "trrm/1-draft-foreign-invocation-contracts-1", artifact),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.EqualTo(1));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [TestCase("missing-write-authority")]
    [TestCase("oversized-scalar-extent")]
    public void StorageAddressConsumerRejectsInvalidLocalAuthorityOrExtent(
        string defect)
    {
        var artifact = CreateStorageAddressArtifact("invalid-storage-address");
        var malformed = RewriteStorageAddressArtifact(artifact, defect);

        void Validate()
        {
            var index = TrRmArtifactIndex
                .ForForeignInvocationStorageAddressDraft(malformed);
            var tables = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageAddressDraftAndGetExecutionTables(
                    index);
            var caller = 0;
            for (var row = 1; row <= tables.Methods.Count; row++)
            {
                if (tables.Methods.GetName(row) == "Main")
                {
                    caller = row;
                    break;
                }
            }
            Assert.That(caller, Is.GreaterThan(0));
            tables.ValidateRegisterBodyMapsForMethod(caller);
        }

        _ = Assert.Throws<InvalidDataException>(Validate);
    }

    [Test]
    public void EmitsParameterizedScalarNativeAddressSignature()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Observe(uint32 value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-parameterized");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        var resultType = tables.Signatures.GetResultTypeSpec(signature);

        Assert.That(tables.Signatures.ParamCount, Is.EqualTo(1));
        Assert.That(parameterCount, Is.EqualTo(1));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            resultType, tables.Parameters.GetTypeSpec(parameterStart)), Is.True);
        Assert.That(tables.TypeSpecs.GetTypeSpecRef(resultType).Form,
            Is.EqualTo(0x01));
        Assert.That(tables.Methods.GetSignature(caller),
            Is.Not.EqualTo(signature));
        tables.ValidateRegisterBodyMapsForMethod(caller);
    }

    [Test]
    public void EmitsNativeParameterOnNativeImportAndSignatureAnchor()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-parameter");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var parameterTypeSpec = tables.Parameters.GetTypeSpec(parameterStart);
        Assert.That(TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            tables.TypeSpecs, parameterTypeSpec, 1, tables.Layouts,
            out var pointeeType), Is.True);
        Assert.That(pointeeType, Is.EqualTo(PrimitiveType.UInt32));

        var targetContracts = tables.Contracts.GetNativeParameters(target);
        var anchorContracts = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetContracts.Length, Is.EqualTo(1));
        Assert.That(anchorContracts.Length, Is.EqualTo(1));
        Assert.That(targetContracts[0], Is.EqualTo(anchorContracts[0]));
        Assert.That(targetContracts[0].ParameterOrdinal, Is.Zero);
        Assert.That(targetContracts[0].Access, Is.EqualTo(1));
        Assert.That(targetContracts[0].Retention, Is.EqualTo(1));
        Assert.That(targetContracts[0].ExtentKind, Is.EqualTo(1));
        Assert.That(targetContracts[0].Extent, Is.EqualTo(uint.MaxValue));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.ForeignInvocationContractsCandidateFeatureId,
            TrRmArtifactSchema.ForeignInvocationDraft), Is.True);
        Assert.That(tables.StandardNativeImports.HasImportForMethod(target),
            Is.True);
        tables.ValidateRegisterBodyMapsForMethod(caller);
    }

    [Test]
    public void EmitsWritableNativeParameterPointerQualifier()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 0)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form() {
                unsafe { _ = &Observe; }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-writable-parameter");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        var signature = tables.Methods.GetSignature(target);
        var (parameterStart, parameterCount)
            = tables.Signatures.GetParamRange(signature);
        Assert.That(parameterCount, Is.EqualTo(1));
        var typeSpecRef = tables.Parameters.GetTypeSpec(parameterStart);
        var typeSpec = tables.TypeSpecs.GetTypeSpecBytes(
            typeSpecRef);
        Assert.That(typeSpec.Length, Is.EqualTo(4));
        Assert.That(typeSpec[1], Is.EqualTo(1));
        Assert.That(typeSpec[2], Is.EqualTo(0x8C));
        Assert.That(TrRmNativeImportTypeContract.TryGetNativeParameterPointee(
            tables.TypeSpecs, typeSpecRef, 2, tables.Layouts,
            out var pointeeType), Is.True);
        Assert.That(pointeeType, Is.EqualTo(PrimitiveType.UInt32));
        var targetContracts = tables.Contracts.GetNativeParameters(target);
        var anchorContracts = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetContracts.Length, Is.EqualTo(1));
        Assert.That(targetContracts[0], Is.EqualTo(anchorContracts[0]));
        Assert.That(targetContracts[0].Access, Is.EqualTo(2));
        Assert.That(targetContracts[0].ExtentKind, Is.Zero);
        Assert.That(targetContracts[0].Extent, Is.Zero);
    }

    [Test]
    public void EmitsIndirectUnitCallFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Observe();

            internal void Form() {
                unsafe {
                    var callback = &Observe;
                    callback();
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var form = program.Functions.Single(static function =>
            function.Name == "Form");
        var calls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundIndirectCall indirectCall)
            {
                calls.Add(indirectCall);
            }
        }), Is.True);
        var call = calls.Single();
        Assert.That(call.Target, Is.TypeOf<BoundRead>());

        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                program, "native-address-indirect-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                caller = row;
                break;
            }
        }

        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(3));
        Assert.That(body.GetRecord(1)[0], Is.EqualTo(0x0D));
        Assert.That(body.GetRecord(1)[4], Is.EqualTo(2));
        Assert.That(body.GetRecord(1)[6], Is.Zero);
        Assert.That(body.HomeMap.Length, Is.EqualTo(8));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x02));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(3));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x18, 0,
            }));
    }

    [Test]
    public void EmitsNativeParameterCallSiteFromInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                program, "native-address-parameter-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        var anchor = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
            anchor = name.StartsWith("__trrm_fnptr_anchor_",
                StringComparison.Ordinal) ? row : anchor;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(anchor, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(16));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(callSite[0], Is.EqualTo(0x02));
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[12], Is.EqualTo(1));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[22], Is.EqualTo(1));
        Assert.That(callSite[23], Is.Zero);
        var targetParameters = tables.Contracts.GetNativeParameters(target);
        var anchorParameters = tables.Contracts.GetNativeParameters(anchor);
        Assert.That(targetParameters.Length, Is.EqualTo(1));
        Assert.That(targetParameters[0], Is.EqualTo(anchorParameters[0]));
        Assert.That(targetParameters[0].Extent, Is.EqualTo(1u));
    }

    [Test]
    public void EmitsWritableNativeParameterCallSiteFromInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 0)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form(write ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-writable-parameter-call");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var target = 0;
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            target = name == "Observe" ? row : target;
            caller = name == "Form" ? row : caller;
        }

        Assert.That(target, Is.GreaterThan(0));
        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var targetParameters = tables.Contracts.GetNativeParameters(target);
        Assert.That(targetParameters.Length, Is.EqualTo(1));
        Assert.That(targetParameters[0].Access, Is.EqualTo(2));
        Assert.That(targetParameters[0].ExtentKind, Is.Zero);
        Assert.That(targetParameters[0].Extent, Is.Zero);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        Assert.That(body.HomeMap.Length, Is.EqualTo(16));
    }

    [Test]
    public void PreservesReorderedNativeParameterCallSiteInputs()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value, uint32 count);

            internal void Form(uint32 count, ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value, count);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-reordered-arguments");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                caller = row;
                break;
            }
        }

        Assert.That(caller, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(24));
        Assert.That(body.HomeMap[8], Is.EqualTo(2));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(callSite[12], Is.EqualTo(2));
        Assert.That(callSite[20], Is.EqualTo(2));
        Assert.That(callSite[21], Is.EqualTo(1));
        Assert.That(callSite[22], Is.EqualTo(2));
        Assert.That(callSite[24], Is.EqualTo(2));
        Assert.That(callSite[25], Is.EqualTo(1));
        Assert.That(callSite[26], Is.EqualTo(1));
    }

    [Test]
    public void EmitsIndirectScalarResultFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-result");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        var target = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            caller = name == "Form" ? row : caller;
            target = name == "Transform" ? row : target;
        }

        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(target, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[5], Is.EqualTo(1));
        Assert.That(callSite[10], Is.Zero);
        Assert.That(callSite[11], Is.Zero);
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0x17, 0,
            }));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(caller)),
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(target))), Is.True);
    }

    [Test]
    public void EmitsIndirectScalarResultThroughHomeFromNativeAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    var result = callback(value);
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                compilation.Program!, "native-address-result-home");
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var caller = 0;
        var target = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            caller = name == "Form" ? row : caller;
            target = name == "Transform" ? row : target;
        }

        Assert.That(caller, Is.GreaterThan(0));
        Assert.That(target, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(caller);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(caller), tables.Contracts.Count);
        var callSite = body.GetRecord(2);
        Assert.That(body.HomeMap.Length, Is.EqualTo(24));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(3));
        Assert.That(body.HomeMap[16], Is.EqualTo(2));
        Assert.That(callSite[4], Is.EqualTo(3));
        Assert.That(callSite[5], Is.EqualTo(2));
        Assert.That(callSite[10], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0, 0xB8, 1,
            }));
        Assert.That(tables.TypeSpecs.AreEquivalentTypeSpecRefs(
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(caller)),
            tables.Signatures.GetResultTypeSpec(
                tables.Methods.GetSignature(target))), Is.True);
    }

    [Test]
    public void StagesDirectNativeCallBeforeIndirectCallArguments()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Transform(uint32 value);

            internal uint32 Form(uint32 value) {
                unsafe {
                    var callback = &Transform;
                    return callback(Transform(value));
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-staged-call";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                callerRow = row;
                break;
            }
        }

        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(4));
        Assert.That(body.GetRecord(2)[4], Is.EqualTo(1));
        Assert.That(body.GetRecord(2)[5], Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[4], Is.EqualTo(3));
        Assert.That(body.GetRecord(3)[20], Is.EqualTo(1));
        Assert.That(body.GetRecord(3)[21], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0,
                0xE0, 0, 3, 0, 0x17, 0,
            }));

        var loaded = TrRmExecutableCompiler
            .LoadForeignInvocationDraftNativeAddressLibrary(artifact);
        var form = loaded.Functions.Single(static function =>
            function.Name == "Form");
        var indirectCalls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(form.Body, expression =>
        {
            if (expression is BoundIndirectCall indirectCall)
            {
                indirectCalls.Add(indirectCall);
            }
        }), Is.True);
        Assert.That(indirectCalls.Single().Arguments.Single(),
            Is.TypeOf<BoundCall>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StagesMultipleDirectCallsInIndirectArguments()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 Combine(uint32 left, uint32 right);

            internal uint32 Form(uint32 left, uint32 right) {
                unsafe {
                    var callback = &Combine;
                    var result = callback(
                        Combine(left, right),
                        Combine(right, left));
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-multiple-staged-calls";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Form")
            {
                callerRow = row;
                break;
            }
        }

        Assert.That(callerRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.RecordCount, Is.EqualTo(5));
        Assert.That(body.HomeMap.Length, Is.EqualTo(40));
        Assert.That(body.HomeMap[0], Is.EqualTo(3));
        Assert.That(body.HomeMap[8], Is.EqualTo(3));
        Assert.That(body.HomeMap[16], Is.EqualTo(3));
        Assert.That(body.HomeMap[24], Is.EqualTo(2));
        Assert.That(body.HomeMap[32], Is.EqualTo(2));
        var firstStagedCall = body.GetRecord(2);
        Assert.That(firstStagedCall[4], Is.EqualTo(1));
        Assert.That(firstStagedCall[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            firstStagedCall[10..]), Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            firstStagedCall[22..]), Is.EqualTo(3));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            firstStagedCall[26..]), Is.EqualTo(4));
        var secondStagedCall = body.GetRecord(3);
        Assert.That(secondStagedCall[5], Is.EqualTo(1));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            secondStagedCall[22..]), Is.EqualTo(4));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            secondStagedCall[26..]), Is.EqualTo(3));
        var indirectCall = body.GetRecord(4);
        Assert.That(indirectCall[4], Is.EqualTo(3));
        Assert.That(indirectCall[5], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            indirectCall[10..]), Is.EqualTo(1));
        Assert.That(indirectCall[20], Is.EqualTo(2));
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(
            indirectCall[22..]), Is.EqualTo(2));
        Assert.That(indirectCall[24], Is.EqualTo(1));
        Assert.That(body.GetBlockCode(0).ToArray(),
            Is.EqualTo(new byte[] {
                0xE9, 0, 1, 0, 0xE0, 0, 2, 0,
                0xE0, 0, 3, 0, 0xE0, 0, 4, 0, 0xB8, 1,
            }));

        var loaded = TrRmExecutableCompiler
            .LoadForeignInvocationDraftNativeAddressLibrary(artifact);
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void ReconstructsNativeAddressCallFromSelectedForeignInvocationProfile()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern uint32 Transform(ptr<uint32> value, uint32 count);

            internal uint32 Form(uint32 count, ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    var result = callback(value, count);
                    return result;
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-source-free";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        Assert.That(TrRmArtifactIndex.ForForeignInvocationDraft(artifact).Schema,
            Is.EqualTo(TrRmArtifactSchema.ForeignInvocationDraft));

        var loaded = TrRmExecutableCompiler
            .LoadForeignInvocationDraftNativeAddressLibrary(artifact);
        var target = loaded.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(target.DirectNativeImport!.LibraryName,
            Is.EqualTo("example.dll"));
        Assert.That(target.DirectNativeImport.Parameters.Single(),
            Is.EqualTo(new DirectNativeParameter(0, PrimitiveType.UInt32,
                NativeParameterAccess.Read, NativeParameterRetention.NoEscape, 1)));
        var pointerSignature = loaded.NativeFunctionPointerSignatures.Single();
        Assert.That(pointerSignature.NativeParameters.Single(),
            Is.EqualTo(target.DirectNativeImport.Parameters.Single()));

        var caller = loaded.Functions.Single(static function =>
            function.Name == "Form");
        var calls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body, expression =>
        {
            if (expression is BoundIndirectCall call)
            {
                calls.Add(call);
            }
        }), Is.True);
        var call = calls.Single();
        Assert.That(call.Arguments.Select(static argument =>
            ((BoundRead)argument).Variable), Is.EqualTo([1, 0]));
        Assert.That(call.Type, Is.EqualTo(PrimitiveType.UInt32));
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(loaded);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.Load(artifact));
        var reemitted = TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(artifact));
    }

    [Test]
    public void StandardFReferencePreservesImportedNativeCallContracts()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
            public extern uint32 Transform(ptr<uint32> value);

            internal uint32 ProviderBody(ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);

        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        const string providerIdentity = "native-provider";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var reference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            providerIdentity, []);
        var consumerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            internal uint32 Use(ptr<uint32> value) {
                unsafe {
                    var callback = &Transform;
                    return callback(value);
                }
            }
            """), reference);

        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var imported = consumerCompilation.Program!.Functions.Single(
            static function => function.Name == "Transform");
        Assert.That(imported.IsExternal, Is.True);
        Assert.That(imported.IsModuleReference, Is.True);
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("example.dll"));
        Assert.That(imported.DirectNativeImport.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        Assert.That(consumerCompilation.Program.NativeFunctionPointerSignatures
            .Single().NativeParameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));

        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program, "native-consumer", schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(consumerArtifact));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(1));
        Assert.That(tables.ModuleRefs.GetProducerIdentity(1),
            Is.EqualTo(providerIdentity));
        Assert.That(tables.Imports.Count, Is.EqualTo(1));
        Assert.That(tables.StandardNativeImports.Count, Is.EqualTo(1));
        var targetRow = 0;
        var callerRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            var name = tables.Methods.GetName(row);
            targetRow = name == "Transform" ? row : targetRow;
            callerRow = name == "Use" ? row : callerRow;
        }

        Assert.That(targetRow, Is.GreaterThan(0));
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.Imports.IsExpectedDefinition(
            0x1600_0000u | (uint)targetRow), Is.True);
        tables.ValidateRegisterBodyMapsForMethod(callerRow);

        var rootInput = new TrRmStandardArtifactInput(
            schemaIdentity, consumerArtifact);
        var providerInput = new TrRmStandardArtifactInput(
            schemaIdentity, providerArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            rootInput, "native-consumer", [providerInput]);
        var loadedTarget = loaded.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(loadedTarget.DirectNativeImport!.LibraryName,
            Is.EqualTo("example.dll"));
        Assert.That(loadedTarget.DirectNativeImport.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        Assert.That(loadedTarget.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(loaded.NativeFunctionPointerSignatures.Single()
            .NativeParameters.Single().Extent, Is.EqualTo(uint.MaxValue));
        var loadedCaller = loaded.Functions.Single(static function =>
            function.Name == "Use");
        var loadedCalls = new List<BoundIndirectCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedCaller.Body,
            expression =>
            {
                if (expression is BoundIndirectCall call)
                {
                    loadedCalls.Add(call);
                }
            }), Is.True);
        Assert.That(loadedCalls, Has.Count.EqualTo(1));
        SemanticVerifier.VerifyLibraryWithDirectNativeImports(loaded);
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitWithNativeImports(loaded,
                NativeRuntimePolicy.FailFast));
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, "native-consumer", schemaIdentity, 1),
            Is.EqualTo(consumerArtifact));

        var replacementProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace("ProviderBody",
                "ReplacementBody", StringComparison.Ordinal)), core);
        Assert.That(replacementProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                replacementProviderCompilation.Diagnostics));
        var replacementProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            replacementProviderCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        Assert.That(replacementProviderArtifact, Is.Not.EqualTo(providerArtifact));
        var replaced = TrRmExecutableCompiler.LoadStandard(rootInput,
            "native-consumer", [
                new TrRmStandardArtifactInput(schemaIdentity,
                    replacementProviderArtifact),
            ]);
        var replacedTarget = replaced.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(replacedTarget.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(replacedTarget.DirectNativeImport!.Parameters.Single()
            .Extent, Is.EqualTo(uint.MaxValue));

        var multiModuleConsumerArtifact = AddLeadingEmptyModule(
            consumerArtifact, "Use");
        var multiModuleProviderArtifact = AddLeadingEmptyModule(
            providerArtifact, "ProviderBody");
        var multiModuleRootInput = new TrRmStandardArtifactInput(
            schemaIdentity, multiModuleConsumerArtifact);
        var multiModuleProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, multiModuleProviderArtifact);
        var multiModuleLoaded = TrRmExecutableCompiler.LoadStandard(
            multiModuleRootInput, "native-consumer",
            [multiModuleProviderInput]);
        Assert.That(multiModuleLoaded.Functions.Single(static function =>
            function.Name == "Transform").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var multiModuleReference = TrRmExecutableCompiler
            .CreateStandardReference(multiModuleProviderInput,
                providerIdentity, []);
        Assert.That(multiModuleReference.Functions.Single(
            static function => function.Name == "Transform")
            .DirectNativeImport!.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(multiModuleRootInput,
                "Use", [multiModuleProviderInput]));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                rootInput, "native-consumer", []));

        var incompatibleProvider = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace("4294967295", "1",
                StringComparison.Ordinal)), core);
        Assert.That(incompatibleProvider.HasErrors, Is.False,
            string.Join(Environment.NewLine, incompatibleProvider.Diagnostics));
        var incompatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleProvider.Program!, providerIdentity, schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, "native-consumer", [
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleProviderArtifact),
            ]));
    }

    [Test]
    public void ReconstructsFProfileExecutableEntry()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 GetCurrentProcessId();

            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        const string moduleIdentity = "native-address-executable";
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(artifact));
        Assert.That(tables.Modules.GetEntryMethod(1), Is.GreaterThan(0));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        Assert.That(loaded.Functions[loaded.EntryPoint].Name,
            Is.EqualTo("Main"));
        Assert.That(TrRmExecutableCompiler.EmitStandard(
            loaded, moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast),
            Is.GreaterThan(0));
    }

    private static byte[] AddLeadingEmptyModule(byte[] artifact,
        string identity)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        var stringTableFound = index.TryGetSection(0x0002,
            out _, out var stringCount);
        var moduleTableFound = index.TryGetSection(0x0001,
            out var moduleBytes, out var moduleCount);
        Assert.That(stringTableFound && moduleTableFound, Is.True);
        Assert.That(moduleCount, Is.EqualTo(1));
        var identityStringRow = 0;
        for (var row = 1; row <= stringCount; row++)
        {
            if (tables.Strings.DecodeNamespace(row).Length != 0
                && tables.Strings.DecodeName(row) == identity)
            {
                identityStringRow = row;
                break;
            }
        }

        Assert.That(identityStringRow, Is.GreaterThan(0));
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var featureFirst = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            moduleBytes[24..]);
        var featureCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(
            moduleBytes[28..]);
        var originalIdentityStringRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes));
        var moduleRows = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(identityStringRow, targetRequirement,
                runtimePolicy, 0, 1, 1, 1, 1, 0, 0,
                tables.Modules.GetGoverningRuleEdition(1)),
            new TrRmModuleRow(originalIdentityStringRow,
                targetRequirement, runtimePolicy,
                tables.Modules.GetEntryMethod(1), 1, 1, 1, 1,
                featureFirst, featureCount,
                tables.Modules.GetGoverningRuleEdition(1)),
        ], tables.Strings, tables.ModuleRefs.Count, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            tables.Features.Count);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0001
                ? new TrRmSection(kind, flags, 2, moduleRows)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections);
    }

    [Test]
    public void LoadsSelectedModuleFromMultiplePopulatedModules()
    {
        var firstCompilation = CreateNativeAddressCompilation("other.dll");
        var secondCompilation = CreateNativeAddressCompilation("selected.dll");
        var firstArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                firstCompilation.Program!, "multi-module-other");
        var secondArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                secondCompilation.Program!, "multi-module-selected");
        var artifact = CombinePopulatedNativeAddressModules(
            firstArtifact, secondArtifact);

        var input = new TrRmStandardArtifactInput(
            "trrm/1-draft-foreign-invocation-contracts-1", artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            "multi-module-selected", []);

        var imported = loaded.Functions.Single(static function =>
            function.DirectNativeImport is not null);
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("selected.dll"));
        Assert.That(loaded.Functions.Any(static function =>
            function.Name == "Form"), Is.True);
    }

    [Test]
    public void BindsProviderModuleFromMultiplePopulatedModules()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "multi-module-provider-selected";
        var firstCompilation = CreateNativeAddressCompilation(
            "other.dll", exportObserve: true);
        var secondCompilation = CreateNativeAddressCompilation(
            "selected.dll", exportObserve: true);
        var firstArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                firstCompilation.Program!, "multi-module-provider-other");
        var secondArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                secondCompilation.Program!, providerIdentity);
        var providerArtifact = CombinePopulatedNativeAddressModules(
            firstArtifact, secondArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        Assert.That(providerReference.Functions.Single(
            static function => function.Name == "Observe")
            .DirectNativeImport!.LibraryName, Is.EqualTo("selected.dll"));

        var consumerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                internal void Use() {
                    unsafe {
                        var callback = &Observe;
                        callback();
                    }
                }
                """), providerReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler
            .EmitForeignInvocationDraftNativeAddressLibrary(
                consumerCompilation.Program!, "multi-module-consumer");
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
            "multi-module-consumer", [providerInput]);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "Observe");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("selected.dll"));
    }

    [Test]
    public void LowersProviderModuleCoLocatedWithTheSelectedRoot()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "co-located-native-consumer";
        const string providerIdentity = "co-located-native-provider";
        var providerCompilation = CreateCurrentProcessIdCompilation();
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            new TrRmStandardArtifactInput(schemaIdentity, providerArtifact),
            providerIdentity, []);
        var rootCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), providerReference);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, []));

        var artifact = CombinePopulatedNativeAddressModules(
            rootArtifact, providerArtifact);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            rootIdentity, []);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "GetCurrentProcessId");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.LibraryName,
            Is.EqualTo("kernel32.dll"));
        Assert.That(NativeModuleCompiler.ExecuteStandard(input, rootIdentity,
            [], NativeRuntimePolicy.FailFast), Is.GreaterThan(0));
        Assert.That(NativeModuleCompiler.EmitStandardExecutable(input,
            rootIdentity, [], NativeRuntimePolicy.FailFast), Is.Not.Empty);
        var selfSelectedInput = new TrRmStandardArtifactInput(
            schemaIdentity, RewriteFirstModuleRefToRoot(artifact));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(selfSelectedInput,
                rootIdentity, []));
    }

    private static Compilation CreateNativeAddressCompilation(
        string libraryName, bool exportObserve = false)
    {
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "{{libraryName}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            {{(exportObserve ? "public" : "internal")}} extern void Observe();

            internal void Form() {
                unsafe {
                    var callback = &Observe;
                    callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    internal static Compilation CreateCurrentProcessIdCompilation()
    {
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 GetCurrentProcessId();

            internal int32 Form() {
                unsafe {
                    var callback = &GetCurrentProcessId;
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    private static byte[] RewriteFirstModuleRefToRoot(byte[] artifact)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes, out _),
            Is.True);
        Assert.That(index.TryGetSection(0x0010, out var moduleRefBytes,
            out var moduleRefCount), Is.True);
        Assert.That(moduleRefCount, Is.GreaterThan(0));
        var rootIdentityStringRow
            = BinaryPrimitives.ReadUInt32LittleEndian(moduleBytes);
        var rewrittenModuleRefs = moduleRefBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(rewrittenModuleRefs,
            rootIdentityStringRow);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0010
                ? new TrRmSection(kind, flags, (uint)recordCount,
                    rewrittenModuleRefs)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    internal static byte[] CombinePopulatedNativeAddressModules(
        byte[] firstArtifact, byte[] secondArtifact)
    {
        var firstIndex
            = TrRmArtifactIndex.ForForeignInvocationDraft(firstArtifact);
        var secondIndex
            = TrRmArtifactIndex.ForForeignInvocationDraft(secondArtifact);
        var firstTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(firstIndex);
        var secondTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(secondIndex);
        Assert.That(firstTables.Modules.Count, Is.EqualTo(1));
        Assert.That(secondTables.Modules.Count, Is.EqualTo(1));
        Assert.That(firstTables.Methods.Count, Is.EqualTo(3));
        Assert.That(secondTables.Methods.Count, Is.EqualTo(3));
        var firstAnchorCount = 0;
        for (var row = 1; row <= firstTables.TypeSpecs.Count; row++)
        {
            if (firstTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(row))
            {
                firstAnchorCount++;
            }
        }
        var secondAnchorCount = 0;
        for (var row = 1; row <= secondTables.TypeSpecs.Count; row++)
        {
            if (secondTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(row))
            {
                secondAnchorCount++;
            }
        }

        Assert.That(firstAnchorCount, Is.EqualTo(1));
        Assert.That(secondAnchorCount, Is.EqualTo(1));
        Assert.That(firstTables.Signatures.ParamCount, Is.Zero);
        Assert.That(secondTables.Signatures.ParamCount, Is.Zero);
        Assert.That(firstTables.TypeDefs.Count, Is.Zero);
        Assert.That(secondTables.TypeDefs.Count, Is.Zero);
        Assert.That(firstTables.Fields.Count, Is.Zero);
        Assert.That(secondTables.Fields.Count, Is.Zero);
        Assert.That(firstTables.Imports.Count, Is.InRange(0, 1));
        Assert.That(secondTables.Imports.Count, Is.Zero);
        Assert.That(firstTables.ModuleRefs.Count,
            Is.EqualTo(firstTables.Imports.Count));
        Assert.That(secondTables.ModuleRefs.Count, Is.Zero);
        Assert.That(firstTables.Bodies.Count, Is.EqualTo(1));
        Assert.That(secondTables.Bodies.Count, Is.EqualTo(1));
        Assert.That(firstIndex.SectionCount, Is.AnyOf(9, 10, 11, 12));
        Assert.That(secondIndex.SectionCount, Is.AnyOf(9, 10));

        Assert.That(firstIndex.TryGetSection(0x0001, out var firstModuleBytes,
            out var firstModuleCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0002, out var firstStringBytes,
            out var firstStringCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0004, out var firstFeatureBytes,
            out var firstFeatureCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0016, out var firstMethodBytes,
            out var firstMethodCount), Is.True);
        Assert.That(firstIndex.TryGetSection(0x0011, out _, out var firstTypeSpecCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x0017, out _, out var firstSignatureCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x001B, out _, out var firstContractCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x0020, out _, out var firstBodyCount),
            Is.True);
        Assert.That(firstIndex.TryGetSection(0x0021, out _, out var firstNativeImportCount),
            Is.True);
        var firstModuleRefsFound = firstIndex.TryGetSection(0x0010,
            out var firstModuleRefBytes, out var firstModuleRefCount);
        var firstImportsFound = firstIndex.TryGetSection(0x001C,
            out var firstImportBytes, out var firstImportCount);
        var firstExportsFound = firstIndex.TryGetSection(0x001D,
            out var firstExportBytes, out var firstExportCount);
        Assert.That(secondIndex.TryGetSection(0x0001, out var secondModuleBytes,
            out var secondModuleCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0002, out var secondStringBytes,
            out var secondStringCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0004, out var secondFeatureBytes,
            out var secondFeatureCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0016, out var secondMethodBytes,
            out var secondMethodCount), Is.True);
        Assert.That(secondIndex.TryGetSection(0x0011, out _, out var secondTypeSpecCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x0017, out _, out var secondSignatureCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x001B, out _, out var secondContractCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x0020, out _, out var secondBodyCount),
            Is.True);
        Assert.That(secondIndex.TryGetSection(0x0021, out _, out var secondNativeImportCount),
            Is.True);
        var secondModuleRefsFound = secondIndex.TryGetSection(0x0010,
            out _, out var secondModuleRefCount);
        var secondImportsFound = secondIndex.TryGetSection(0x001C,
            out _, out var secondImportCount);
        var secondExportsFound = secondIndex.TryGetSection(0x001D,
            out var secondExportBytes, out var secondExportCount);
        Assert.That(firstModuleCount, Is.EqualTo(1));
        Assert.That(secondModuleCount, Is.EqualTo(1));
        Assert.That(firstMethodCount, Is.EqualTo(firstTables.Methods.Count));
        Assert.That(secondMethodCount, Is.EqualTo(secondTables.Methods.Count));
        Assert.That(firstTypeSpecCount, Is.EqualTo(firstTables.TypeSpecs.Count));
        Assert.That(secondTypeSpecCount, Is.EqualTo(secondTables.TypeSpecs.Count));
        Assert.That(firstSignatureCount, Is.EqualTo(firstTables.Signatures.Count));
        Assert.That(secondSignatureCount, Is.EqualTo(secondTables.Signatures.Count));
        Assert.That(firstContractCount, Is.EqualTo(firstTables.Contracts.Count));
        Assert.That(secondContractCount, Is.EqualTo(secondTables.Contracts.Count));
        Assert.That(firstBodyCount, Is.EqualTo(firstTables.Bodies.Count));
        Assert.That(secondBodyCount, Is.EqualTo(secondTables.Bodies.Count));
        Assert.That(firstNativeImportCount, Is.EqualTo(1));
        Assert.That(secondNativeImportCount, Is.EqualTo(1));
        Assert.That(firstModuleRefCount, Is.EqualTo(firstTables.ModuleRefs.Count));
        Assert.That(firstImportCount, Is.EqualTo(firstTables.Imports.Count));
        Assert.That(secondModuleRefCount, Is.Zero);
        Assert.That(secondImportCount, Is.Zero);
        Assert.That(firstModuleRefsFound, Is.EqualTo(firstModuleRefCount != 0));
        Assert.That(firstImportsFound, Is.EqualTo(firstImportCount != 0));
        Assert.That(secondModuleRefsFound, Is.False);
        Assert.That(secondImportsFound, Is.False);
        Assert.That(firstExportsFound, Is.EqualTo(firstExportCount != 0));
        Assert.That(secondExportsFound, Is.EqualTo(secondExportCount != 0));

        var firstStringIndex = new TrRmVariableTableIndex(
            firstStringBytes, firstStringCount, 8, 4);
        var secondStringIndex = new TrRmVariableTableIndex(
            secondStringBytes, secondStringCount, 8, 4);
        var stringRows = new List<(string Text, int Encoding)>(firstStringCount
            + secondStringCount);
        var stringRowByText = new Dictionary<(string Text, int Encoding), int>();
        for (var row = 1; row <= firstStringCount; row++)
        {
            var value = (firstTables.Strings.DecodeNamespace(row),
                (int)firstStringIndex.GetRow(row)[0]);
            stringRows.Add(value);
            _ = stringRowByText.TryAdd(value, row);
        }

        var secondStringMap = new int[secondStringCount + 1];
        for (var row = 1; row <= secondStringCount; row++)
        {
            var value = (secondTables.Strings.DecodeNamespace(row),
                (int)secondStringIndex.GetRow(row)[0]);
            if (!stringRowByText.TryGetValue(value, out var mappedRow))
            {
                mappedRow = stringRows.Count + 1;
                stringRows.Add(value);
                stringRowByText.Add(value, mappedRow);
            }

            secondStringMap[row] = mappedRow;
        }

        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Count);
        var featureBytes = firstFeatureBytes.ToArray()
            .Concat(secondFeatureBytes.ToArray()).ToArray();
        var featureCount = firstFeatureCount + secondFeatureCount;
        var features = new TrRmFeatureTableReader(featureBytes,
            featureCount, TrRmArtifactSchema.ForeignInvocationDraft);
        var methodCount = firstMethodCount + secondMethodCount;

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            firstTypeSpecCount + secondTypeSpecCount);
        for (var row = 1; row <= firstTypeSpecCount; row++)
        {
            typeSpecRows.Add(firstTables.TypeSpecs.GetRow(row).ToArray());
        }
        for (var row = 1; row <= secondTypeSpecCount; row++)
        {
            if (secondTables.NativeFunctionPointerAnchors
                .HasAnchorForTypeSpec(row))
            {
                var anchorMethod = firstMethodCount
                    + secondTables.NativeFunctionPointerAnchors
                        .GetAnchorMethodDefRow(row);
                typeSpecRows.Add(
                    TrRmTypeSpecTableWriter.NativeFunctionPointer(
                        secondTables.NativeFunctionPointerAnchors
                            .GetCallingConvention(row), (uint)anchorMethod));
            }
            else
            {
                var typeSpec = secondTables.TypeSpecs.GetRow(row);
                Assert.That(typeSpec[0], Is.EqualTo(0x01),
                    "The fixture merger supports only self-contained non-anchor TypeSpecs.");
                typeSpecRows.Add(typeSpec.ToArray());
            }
        }

        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            0, 0, 0, 0, TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: methodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, 0, 0, 0,
            TrRmArtifactSchema.ForeignInvocationDraft,
            methodDefCount: methodCount);

        var signatureRows = new List<TrRmMethodSigRow>(
            firstSignatureCount + secondSignatureCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Signatures
                : secondTables.Signatures;
            var signatureCount = artifact == 0 ? firstSignatureCount
                : secondSignatureCount;
            var typeOffset = artifact == 0 ? 0 : firstTypeSpecCount;
            for (var row = 1; row <= signatureCount; row++)
            {
                var resultType = source.GetResultTypeSpec(row);
                var receiverType = source.GetReceiverTypeSpec(row);
                if (artifact != 0)
                {
                    if (resultType != 0 && (resultType & 0xFF00_0000u) == 0)
                    {
                        resultType = checked(resultType + (uint)typeOffset);
                    }
                    if (receiverType != 0
                        && (receiverType & 0xFF00_0000u) == 0)
                    {
                        receiverType = checked(receiverType + (uint)typeOffset);
                    }
                }

                var (parameterStart, parameterCount)
                    = source.GetParamRange(row);
                var (genericStart, genericCount)
                    = source.GetGenericRange(row);
                Assert.That(parameterStart, Is.Zero);
                Assert.That(parameterCount, Is.Zero);
                Assert.That(genericStart, Is.Zero);
                Assert.That(genericCount, Is.Zero);
                signatureRows.Add(new TrRmMethodSigRow(resultType,
                    receiverType, 0, 0, 0, 0));
            }
        }

        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, 0, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, 0, 0);
        var (firstTargetRequirement, firstRuntimePolicy)
            = firstTables.Modules.GetRequirements(1);
        var (secondTargetRequirement, secondRuntimePolicy)
            = secondTables.Modules.GetRequirements(1);
        var firstIdentityRow = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(firstModuleBytes));
        var secondIdentityRow = checked((int)secondStringMap[
            (int)BinaryPrimitives.ReadUInt32LittleEndian(secondModuleBytes)]);
        var (firstFeatureStart, firstModuleFeatureCount)
            = firstTables.Modules.GetFeatureRange(1);
        var (_, secondModuleFeatureCount)
            = secondTables.Modules.GetFeatureRange(1);
        Assert.That(firstModuleFeatureCount, Is.EqualTo(firstFeatureCount));
        Assert.That(secondModuleFeatureCount, Is.EqualTo(secondFeatureCount));
        var firstEntryMethod = firstTables.Modules.GetEntryMethod(1);
        var secondEntryMethod = secondTables.Modules.GetEntryMethod(1);
        if (secondEntryMethod != 0)
        {
            secondEntryMethod += firstMethodCount;
        }

        var moduleRefCount = firstModuleRefCount + secondModuleRefCount;
        var moduleRows = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(firstIdentityRow,
                firstTargetRequirement,
                firstRuntimePolicy, firstEntryMethod, 1, 1, 1, 1,
                firstFeatureStart, firstFeatureCount,
                firstTables.Modules.GetGoverningRuleEdition(1)),
            new TrRmModuleRow(secondIdentityRow,
                secondTargetRequirement,
                secondRuntimePolicy, secondEntryMethod, moduleRefCount + 1, 1,
                firstMethodCount + 1, 1,
                firstFeatureCount + 1, secondModuleFeatureCount,
                secondTables.Modules.GetGoverningRuleEdition(1)),
        ], strings, moduleRefCount, 0, methodCount, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleRows, 2, strings,
            moduleRefCount, 0, methodCount, 0, featureCount);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstModuleRefBytes
                : [];
            var sourceCount = artifact == 0 ? firstModuleRefCount
                : secondModuleRefCount;
            for (var row = 0; row < sourceCount; row++)
            {
                var sourceRow = sourceRows.Slice(row * 12, 12);
                var identityStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow));
                if (artifact != 0)
                {
                    identityStringRow = secondStringMap[identityStringRow];
                }

                moduleRefRows.Add(new TrRmModuleRefRow(identityStringRow,
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[8..])));
            }
        }

        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            strings, modules);
        var moduleRefs = moduleRefRows.Count == 0
            ? default
            : new TrRmModuleRefTableReader(moduleRefBytes,
                moduleRefRows.Count, strings, modules);
        var contractCount = firstContractCount + secondContractCount;
        var bodyCount = firstBodyCount + secondBodyCount;
        var typeDefs = new TrRmTypeDefTableReader([], 0, strings, modules,
            0, 0, 0, contractCount);

        var methodRows = new List<TrRmMethodDefRow>(methodCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Methods
                : secondTables.Methods;
            var sourceRows = artifact == 0 ? firstMethodBytes
                : secondMethodBytes;
            var (methodStart, methodEnd)
                = (artifact == 0 ? firstTables.Modules : secondTables.Modules)
                    .GetMethodDefRange(1);
            var signatureOffset = artifact == 0 ? 0 : firstSignatureCount;
            var contractOffset = artifact == 0 ? 0 : firstContractCount;
            var bodyOffset = artifact == 0 ? 0 : firstBodyCount;
            for (var row = methodStart; row < methodEnd; row++)
            {
                var sourceRow = sourceRows.Slice((row - 1) * 28, 28);
                var nameRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]));
                if (artifact != 0)
                {
                    nameRow = secondStringMap[nameRow];
                }

                var (contractFirst, methodContracts)
                    = source.GetContractRange(row);
                if (methodContracts != 0)
                {
                    contractFirst += contractOffset;
                }
                var bodyRow = source.GetBodyRow(row);
                if (bodyRow != 0)
                {
                    bodyRow += bodyOffset;
                }

                methodRows.Add(new TrRmMethodDefRow(
                    source.GetOwnerType(row), nameRow,
                    source.GetSignature(row) + signatureOffset,
                    source.GetFlags(row), contractFirst, methodContracts,
                    bodyRow));
            }
        }

        var methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
            strings, modules, typeDefs, signatures, contractCount, bodyCount);
        var methods = new TrRmMethodDefTableReader(methodBytes,
            methodRows.Count, strings, modules, typeDefs, signatures,
            contractCount, bodyCount);

        var importRows = new List<TrRmImportRow>(
            firstImportCount + secondImportCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstImportBytes
                : [];
            var sourceCount = artifact == 0 ? firstImportCount
                : secondImportCount;
            for (var row = 0; row < sourceCount; row++)
            {
                var sourceRow = sourceRows.Slice(row * 12, 12);
                var moduleRefRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow));
                var keyStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]));
                var expectedDefinition
                    = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[8..]);
                if (artifact != 0)
                {
                    moduleRefRow += firstModuleRefCount;
                    keyStringRow = secondStringMap[keyStringRow];
                    expectedDefinition = (expectedDefinition
                        & 0xFF00_0000u)
                        | ((expectedDefinition & 0x00FF_FFFFu)
                            + (uint)firstMethodCount);
                }

                importRows.Add(new TrRmImportRow(moduleRefRow,
                    keyStringRow, expectedDefinition));
            }
        }

        var importBytes = importRows.Count == 0
            ? []
            : TrRmImportTableWriter.Encode(importRows, strings, moduleRefs,
                typeDefs, methods, default, default, default, default,
                features, modules,
                TrRmArtifactSchema.ForeignInvocationDraft);
        var imports = importRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, importRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, default, features, modules,
                TrRmArtifactSchema.ForeignInvocationDraft);

        var exportRows = new List<TrRmExportRow>(
            firstExportCount + secondExportCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstExportBytes
                : secondExportBytes;
            var sourceCount = artifact == 0 ? firstExportCount
                : secondExportCount;
            for (var row = 0; row < sourceCount; row++)
            {
                var sourceRow = sourceRows.Slice(row * 8, 8);
                var definition
                    = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow);
                var keyStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]));
                if (artifact != 0)
                {
                    definition = (definition & 0xFF00_0000u)
                        | ((definition & 0x00FF_FFFFu)
                            + (uint)firstMethodCount);
                    keyStringRow = secondStringMap[keyStringRow];
                }

                exportRows.Add(new TrRmExportRow(definition, keyStringRow));
            }
        }

        var contractRows = new List<TrRmContractRow>(contractCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Contracts
                : secondTables.Contracts;
            var sourceCount = artifact == 0 ? firstContractCount
                : secondContractCount;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            for (var row = 1; row <= sourceCount; row++)
            {
                var sourceRow = source.GetRow(row);
                var owner = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow);
                if (artifact != 0)
                {
                    Assert.That(owner >> 24, Is.EqualTo(0x16));
                    owner = (owner & 0xFF00_0000u)
                        | ((owner & 0x00FF_FFFFu) + (uint)methodOffset);
                }

                contractRows.Add(new TrRmContractRow(owner,
                    BinaryPrimitives.ReadUInt16LittleEndian(sourceRow[4..]),
                    sourceRow[8..].ToArray()));
            }
        }

        var contractBytes = TrRmContractTableWriter
            .EncodeForeignInvocationDraft(contractRows, typeDefs, methods,
                signatures, default, typeSpecs, features, modules,
                predicateGraphCount: 0);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, default,
            typeSpecs, 0, features, modules,
            TrRmArtifactSchema.ForeignInvocationDraft);

        var bodyRows = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceMethods = artifact == 0 ? firstTables.Methods
                : secondTables.Methods;
            var sourceBodies = artifact == 0 ? firstTables.Bodies
                : secondTables.Bodies;
            var sourceContracts = artifact == 0 ? firstContractCount
                : secondContractCount;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            var typeOffset = artifact == 0 ? 0 : firstTypeSpecCount;
            var bodyRow = 0;
            for (var method = 1; method <= sourceMethods.Count; method++)
            {
                if (sourceMethods.GetBodyRow(method) != 0)
                {
                    bodyRow = sourceMethods.GetBodyRow(method);
                    break;
                }
            }

            Assert.That(bodyRow, Is.GreaterThan(0));
            var body = sourceBodies.GetRegisterBody(bodyRow, sourceContracts);
            bodyRows.Add(RemapNativeAddressBody(body, typeOffset,
                methodOffset, contractCount));
        }

        var bodyBytes = TrRmVariableTableWriter.EncodeBodies(bodyRows);
        var nativeImportRows = new List<TrRmNativeImportRow>(
            firstNativeImportCount + secondNativeImportCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.StandardNativeImports
                : secondTables.StandardNativeImports;
            var sourceMethods = artifact == 0 ? firstTables.Methods
                : secondTables.Methods;
            var stringMap = artifact == 0 ? null : secondStringMap;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            for (var method = 1; method <= sourceMethods.Count; method++)
            {
                if (!source.HasImportForMethod(method))
                {
                    continue;
                }

                var import = source.GetImportForMethod(method);
                nativeImportRows.Add(import with
                {
                    OwnerRef = import.OwnerRef + methodOffset,
                    LibraryNameStringRow = stringMap is null
                        ? import.LibraryNameStringRow
                        : stringMap[import.LibraryNameStringRow],
                    EntryNameStringRow = import.EntryNameStringRow == 0
                        ? 0
                        : stringMap is null ? import.EntryNameStringRow
                            : stringMap[import.EntryNameStringRow],
                });
            }
        }

        var targetProfiles = new TrRmTargetProfileTableReader([], 0);
        var layouts = new TrRmLayoutTableReader([], 0, typeDefs,
            targetProfiles, 0);
        var nativeImportBytes = TrRmNativeImportTableWriter.Encode(
            nativeImportRows, strings, methods, default, typeDefs, imports,
            modules, signatures, default, typeSpecs, contracts, layouts);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, 2, moduleRows),
            new TrRmSection(0x0002, 0, (uint)stringRows.Count, stringBytes),
            new TrRmSection(0x0004, 0, (uint)featureCount, featureBytes),
        };
        if (moduleRefRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0010, 0,
                (uint)moduleRefRows.Count, moduleRefBytes));
        }

        sections.AddRange([
            new TrRmSection(0x0011, 0, (uint)typeSpecRows.Count,
                typeSpecBytes),
            new TrRmSection(0x0016, 0, (uint)methodRows.Count, methodBytes),
            new TrRmSection(0x0017, 0, (uint)signatureRows.Count,
                signatureBytes),
            new TrRmSection(0x001B, 0, (uint)contractRows.Count,
                contractBytes),
        ]);
        if (importRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x001C, 0,
                (uint)importRows.Count, importBytes));
        }

        if (exportRows.Count != 0)
        {
            var exportBytes = TrRmExportTableWriter.Encode(exportRows,
                strings, typeDefs, methods, default, imports, default, default,
                default, features, modules,
                TrRmArtifactSchema.ForeignInvocationDraft);
            sections.Add(new TrRmSection(0x001D, 0,
                (uint)exportRows.Count, exportBytes));
        }

        sections.AddRange([
            new TrRmSection(0x0020, 0, (uint)bodyRows.Count, bodyBytes),
            new TrRmSection(0x0021, 0, (uint)nativeImportRows.Count,
                nativeImportBytes),
        ]);
        var combinedArtifact = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var combinedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(combinedArtifact));
        Assert.That(combinedTables.Modules.Count, Is.EqualTo(2));
        for (var method = 1; method <= combinedTables.Methods.Count; method++)
        {
            if (combinedTables.Methods.GetBodyRow(method) != 0)
            {
                combinedTables.ValidateRegisterBodyMapsForMethod(method);
            }
        }

        return combinedArtifact;
    }

    private static byte[] RemapNativeAddressBody(
        TrRmRegisterBodyReader body, int typeSpecOffset,
        int methodDefOffset, int contractCount)
    {
        var typeMap = body.TypeMap.ToArray();
        for (var offset = 0; offset < typeMap.Length; offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                typeMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) == 0x1100_0000u)
            {
                token = 0x1100_0000u
                    | ((token & 0x00FF_FFFFu) + (uint)typeSpecOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    typeMap.AsSpan(offset), token);
            }
        }

        var methodMap = body.MethodMap.ToArray();
        for (var offset = 0; offset < methodMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                methodMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) == 0x1600_0000u)
            {
                token = 0x1600_0000u
                    | ((token & 0x00FF_FFFFu) + (uint)methodDefOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    methodMap.AsSpan(offset), token);
            }
        }

        var blocks = new List<TrRmRegisterBodyBlock>(body.BlockCount);
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var block = body.GetBlock(blockIndex);
            blocks.Add(new TrRmRegisterBodyBlock(
                BinaryPrimitives.ReadUInt32LittleEndian(block),
                BinaryPrimitives.ReadUInt32LittleEndian(block[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(block[8..])));
        }

        var records = new List<ReadOnlyMemory<byte>>(body.RecordCount);
        for (var record = 0; record < body.RecordCount; record++)
        {
            records.Add(body.GetRecord(record).ToArray());
        }

        return TrRmRegisterBodyWriter.Encode(body.HomeMap.ToArray(),
            typeMap, methodMap, body.FieldMap.ToArray(),
            body.ConstantMap.ToArray(), blocks, records, body.Code.ToArray(),
            contractCount, TrRmArtifactSchema.ForeignInvocationDraft);
    }

    [Test]
    public void NativeParameterIndirectCallBindsThroughAnInferredAddress()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            int32 Main() { return 0; }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var import = program.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport!;
        Assert.That(import.Parameters.Single().Extent, Is.EqualTo(1));
        var signature = program.NativeFunctionPointerSignatures.Single();
        Assert.That(signature.NativeParameters.Single().Extent, Is.EqualTo(1));
        var parameterType = signature.ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(parameterType), Is.True);
        Assert.That(program.NativePointerTypes[NativePointerTypes.Index(parameterType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, false)));
    }

    [Test]
    public void NativeParameterRejectsWriteAccessForReadonlyFormal()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);
            """), core);

        Assert.That(compilation.HasErrors, Is.True,
            "Write access must require a writable pointer formal.");
    }

    [Test]
    public void NativeParameterReadPreservesWritableFormalQualifier()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(write ptr<uint32> value);

            internal void Form(ptr<uint32> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            int32 Main() { return 0; }
            """), core);

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var formalType = program.NativeFunctionPointerSignatures.Single()
            .ParameterTypes.Single();
        Assert.That(NativePointerTypes.IsType(formalType), Is.True);
        Assert.That(program.NativePointerTypes[NativePointerTypes.Index(formalType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, true)));
        Assert.That(program.NativeFunctionPointerSignatures.Single()
            .NativeParameters.Single().Access, Is.EqualTo(NativeParameterAccess.Read));
        Assert.That(NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }

    [Test]
    public void ReadAccessDoesNotUpgradeWritableFormalDuringDevelopmentRoundTrip()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("example.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            extern void Observe(write ptr<uint32> value);

            int32 Main() {
                uint32 value = 42;
                unsafe { Observe(&value); return 0; }
            }
            """));

        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.Emit(compilation.Program!, "native-read-access");
        var loaded = TrRmExecutableCompiler.Load(artifact);
        var main = loaded.Functions.Single(static function => function.Name == "Main");
        var addresses = new List<BoundNativeStorageAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body, expression =>
        {
            if (expression is BoundNativeStorageAddress address)
            {
                addresses.Add(address);
            }
        }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(addresses[0].IsReadonly, Is.True);
        Assert.That(main.Variables[addresses[0].Variable].IsMutable, Is.False);
        var target = loaded.Functions.Single(static function => function.Name == "Observe");
        var formalType = target.Variables[0].Type;
        Assert.That(NativePointerTypes.IsType(formalType), Is.True);
        Assert.That(loaded.NativePointerTypes[NativePointerTypes.Index(formalType)],
            Is.EqualTo(new NativePointerTypeDefinition(PrimitiveType.UInt32, true)));
        Assert.That(target.DirectNativeImport!.Parameters.Single().Access,
            Is.EqualTo(NativeParameterAccess.Read));
    }

    [Test]
    public void NativeParameterIndirectCallRejectsDifferentPointeeType()
    {
        var core = new CompilationReference([], [], BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void Observe(ptr<uint32> value);

            internal void Form(ptr<uint64> value) {
                unsafe {
                    var callback = &Observe;
                    callback(value);
                }
            }
            """), core);

        Assert.That(compilation.HasErrors, Is.True,
            "The inferred pointer signature must retain the target's pointee type.");
    }

    internal static byte[] CreateStorageAddressArtifact(string moduleIdentity,
        string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-addresses-1")
    {
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetSystemTimeAdjustment",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(3, Write, NoEscape, 1)]
            internal extern int32 GetSystemTimeAdjustment(
                write ptr<uint32> adjustment,
                write ptr<uint32> increment,
                write ptr<int32> disabled);

            int32 Main() {
                mut uint32 adjustment = 0;
                mut uint32 increment = 0;
                mut int32 disabled = -1;
                unsafe {
                    var callback = &GetSystemTimeAdjustment;
                    callback(&adjustment, &increment, &disabled);
                }
                return disabled;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return TrRmExecutableCompiler.EmitStandard(compilation.Program!,
            moduleIdentity, schemaIdentity, 1);
    }

    private static byte[] AddA2FixedArrayTypes(byte[] artifact,
        IReadOnlyList<(uint ElementTypeSpecRef, ulong ElementCount,
            ulong ElementStride, ulong ByteExtent, ulong Alignment)> arrays,
        bool corruptFirstExtent = false)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageFixedArrayCopyDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                index);
        if (tables.Modules.Count != 1
            || tables.FixedArrayLayouts.Count != 0
            || tables.Features.TryGetFeature(
                TrRmFeatureTableReader.FixedInlineArrayStorageFeatureId,
                out _))
        {
            throw new InvalidOperationException(
                "The array augmentation requires an unmodified single-module A2 artifact.");
        }

        var (featureFirst, existingFeatureCount)
            = tables.Modules.GetFeatureRange(1);
        if (featureFirst != 1 || existingFeatureCount != tables.Features.Count)
        {
            throw new InvalidOperationException(
                "The source artifact does not own its complete Feature range.");
        }

        var featureRows = new List<TrRmFeatureRow>(
            tables.Features.Count + 1);
        for (var row = 1; row <= tables.Features.Count; row++)
        {
            featureRows.Add(tables.Features.GetFeature(row));
        }
        featureRows.Add(TrRmFeatureRow.FixedInlineArrayStorageCandidate);
        var featureBytes = TrRmFeatureTableWriter.Encode(featureRows, schema);
        var features = new TrRmFeatureTableReader(featureBytes,
            featureRows.Count, schema);

        if (!index.TryGetSection(0x0001, out var oldModuleBytes,
                out var moduleCount)
            || moduleCount != 1)
        {
            throw new InvalidOperationException(
                "The source artifact has no single Module row.");
        }
        var moduleBytes = oldModuleBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(moduleBytes.AsSpan(28),
            checked((uint)featureRows.Count));
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            tables.Strings, tables.Modules.ModuleRefCount,
            tables.TypeDefs.Count, tables.Methods.Count,
            tables.Modules.SourceFileCount, featureRows.Count);
        if (tables.TargetProfiles.Count != 0)
        {
            throw new InvalidOperationException(
                "The source artifact already contains target profiles.");
        }
        var targetProfileBytes = TrRmTargetProfileTableWriter.Encode([
            new TrRmTargetProfileRow(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision),
        ], schema);
        var targetProfiles = new TrRmTargetProfileTableReader(
            targetProfileBytes, 1, schema);

        var typeSpecRows = new List<ReadOnlyMemory<byte>>(
            tables.TypeSpecs.Count + arrays.Count);
        for (var row = 1; row <= tables.TypeSpecs.Count; row++)
        {
            typeSpecRows.Add(tables.TypeSpecs.GetRow(row).ToArray());
        }

        foreach (var array in arrays)
        {
            typeSpecRows.Add(TrRmTypeSpecTableWriter.FixedInlineArray(
                array.ElementTypeSpecRef, array.ElementCount));
        }
        var typeSpecBytes = TrRmTypeSpecTableWriter.Encode(typeSpecRows,
            tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            tables.Methods.Count);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, tables.TypeDefs.Count, tables.Imports.Count,
            tables.GenericParams.Count,
            tables.NativeFunctionPointerSignatures.Count, schema,
            tables.Methods.Count);
        if (!targetProfiles.TryGetProfile(
                TrRmTargetProfileTableReader.WindowsX64NativeProfileId,
                TrRmTargetProfileTableReader
                    .WindowsX64NativeProfileRuleRevision,
                out var profileRow))
        {
            throw new InvalidOperationException(
                "The source artifact has no Windows x64 target profile.");
        }

        var layoutRows = new TrRmFixedArrayLayoutRow[arrays.Count];
        for (var indexInArray = 0; indexInArray < arrays.Count; indexInArray++)
        {
            var array = arrays[indexInArray];
            layoutRows[indexInArray] = new TrRmFixedArrayLayoutRow(1,
                tables.TypeSpecs.Count + indexInArray + 1,
                (uint)profileRow, array.ElementStride, array.ByteExtent,
                array.Alignment);
        }
        var fixedArrayLayoutBytes = TrRmFixedArrayLayoutTableWriter.Encode(
            layoutRows, typeSpecs, targetProfiles, features, modules,
            schema);
        if (corruptFirstExtent)
        {
            var extent = BinaryPrimitives.ReadUInt64LittleEndian(
                fixedArrayLayoutBytes.AsSpan(24));
            BinaryPrimitives.WriteUInt64LittleEndian(
                fixedArrayLayoutBytes.AsSpan(24), checked(extent + 1));
        }

        var sections = new List<TrRmSection>(index.SectionCount + 1);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            switch (kind)
            {
                case 0x0001:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)moduleCount), moduleBytes));
                    break;
                case 0x0004:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)featureRows.Count), featureBytes));
                    break;
                case 0x0011:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)typeSpecRows.Count), typeSpecBytes));
                    break;
                case 0x0012:
                    throw new InvalidOperationException(
                        "The source artifact already contains FixedArrayLayout rows.");
                case 0x0048:
                    throw new InvalidOperationException(
                        "The source artifact already contains target profiles.");
                default:
                    sections.Add(new TrRmSection(kind, flags,
                        checked((uint)recordCount), section.ToArray()));
                    break;
            }
        }

        sections.Add(new TrRmSection(0x0048, 0, 1, targetProfileBytes));
        sections.Add(new TrRmSection(0x0012, 0,
            checked((uint)layoutRows.Length), fixedArrayLayoutBytes));
        sections.Sort(static (left, right)
            => left.Kind.CompareTo(right.Kind));
        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EEmptyInitializationArtifact(
        byte[] artifact, string defect)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var bodyBytes = bodyTable.GetRow(1).ToArray();
            var modified = false;
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                var isHomeAccessDefect = defect == "wrong-home-access"
                    && record[0] == 0x04 && record[4] == 1;
                var isNullaryStoreDefect = defect != "wrong-home-access"
                    && record[0] == 0x05 && record[5] == 0;
                if (!modified
                    && (isHomeAccessDefect || isNullaryStoreDefect))
                {
                    var recordOffset = bodyBytes.AsSpan().IndexOf(record);
                    if (recordOffset < 0)
                    {
                        throw new InvalidOperationException(
                            "The A2E nullary store has no serialized record bytes.");
                    }

                    if (isHomeAccessDefect)
                    {
                        bodyBytes[recordOffset + 5] = 2;
                    }
                    else
                    {
                        switch (defect)
                        {
                            case "nonzero-source-index":
                                bodyBytes[recordOffset + 10] = 1;
                                break;
                            case "nonzero-source-use":
                                bodyBytes[recordOffset + 7] = 1;
                                break;
                            case "wrong-transition":
                                bodyBytes[recordOffset + 6] = 2;
                                break;
                            case "nonzero-dependency":
                                bodyBytes[recordOffset + 12] = 1;
                                break;
                            case "formal-present":
                                bodyBytes[recordOffset + 16] = 1;
                                break;
                            case "nonzero-flags":
                                bodyBytes[recordOffset + 17] = 1;
                                break;
                            case "nonzero-formal-ordinal":
                                bodyBytes[recordOffset + 18] = 1;
                                break;
                            case "wrong-place-kind":
                                bodyBytes[recordOffset + 4] = 2;
                                break;
                            default:
                                throw new ArgumentOutOfRangeException(
                                    nameof(defect));
                        }
                    }

                    modified = true;
                }
            }

            if (!modified)
            {
                throw new InvalidOperationException(
                    "The A2E Body contains no nullary SemanticStore.");
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] DuplicateA2EEmptyHomeInitialization(
        byte[] artifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var homeSelection = default(ReadOnlyMemory<byte>);
            var nullaryStore = default(ReadOnlyMemory<byte>);
            var records = new List<ReadOnlyMemory<byte>>(body.RecordCount + 2);
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                records.Add(record.ToArray());
                if (homeSelection.IsEmpty && record[0] == 0x04
                    && record[4] == 1)
                {
                    homeSelection = record.ToArray();
                }
                else if (nullaryStore.IsEmpty && record[0] == 0x05
                    && record[5] == 0)
                {
                    nullaryStore = record.ToArray();
                }
            }

            if (body.BlockCount != 1 || homeSelection.IsEmpty
                || nullaryStore.IsEmpty)
            {
                throw new InvalidOperationException(
                    "The A2E Body cannot be extended with a second Home initialization.");
            }

            var selectionIndex = checked((ushort)records.Count);
            records.Add(homeSelection);
            var storeIndex = checked((ushort)records.Count);
            records.Add(nullaryStore);
            var originalCode = body.Code.ToArray();
            var insertionOffset = -1;
            var position = 0;
            while (position < originalCode.Length)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        originalCode[position], schema);
                if (position + instructionLength == originalCode.Length)
                {
                    insertionOffset = position;
                    break;
                }

                position += instructionLength;
            }

            if (insertionOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2E Body has no final instruction for insertion.");
            }

            var code = new byte[originalCode.Length + 8];
            originalCode.AsSpan(0, insertionOffset).CopyTo(code);
            code[insertionOffset] = 0xD8;
            code[insertionOffset + 1] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(
                code.AsSpan(insertionOffset + 2), selectionIndex);
            code[insertionOffset + 4] = 0xD9;
            code[insertionOffset + 5] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(
                code.AsSpan(insertionOffset + 6), storeIndex);
            originalCode.AsSpan(insertionOffset).CopyTo(
                code.AsSpan(insertionOffset + 8));

            var originalBlock = body.GetBlock(0);
            TrRmRegisterBodyBlock[] blocks = [
                new TrRmRegisterBodyBlock(0, checked((uint)code.Length),
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        originalBlock[8..])),
            ];
            var rewrittenBody = TrRmRegisterBodyWriter.Encode(
                body.HomeMap.ToArray(), body.TypeMap.ToArray(),
                body.MethodMap.ToArray(), body.FieldMap.ToArray(),
                body.ConstantMap.ToArray(), blocks, records, code,
                contractCount, schema);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([rewrittenBody])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EUUnitStoreArtifact(byte[] artifact,
        string defect)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var bodyBytes = bodyTable.GetRow(1).ToArray();
            var modified = false;
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                if (modified || record[0] != 0x05 || record[5] != 1
                    || BinaryPrimitives.ReadUInt16LittleEndian(
                        record[10..]) != 14)
                {
                    continue;
                }

                var recordOffset = bodyBytes.AsSpan().IndexOf(record);
                if (recordOffset < 0)
                {
                    throw new InvalidOperationException(
                        "The A2EU unit store has no serialized record bytes.");
                }

                switch (defect)
                {
                    case "unit-source-r15":
                        bodyBytes[recordOffset + 10] = 15;
                        break;
                    case "unit-store-source-use":
                        bodyBytes[recordOffset + 7] = 2;
                        break;
                    case "unit-store-transition":
                        bodyBytes[recordOffset + 6] = 2;
                        break;
                    case "unit-store-place-kind":
                        bodyBytes[recordOffset + 4] = 2;
                        break;
                    case "unit-store-dependency":
                        bodyBytes[recordOffset + 12] = 1;
                        break;
                    case "unit-store-formal":
                        bodyBytes[recordOffset + 16] = 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(defect));
                }

                modified = true;
            }

            if (!modified)
            {
                throw new InvalidOperationException(
                    "The A2EU Body contains no typed Unit SemanticStore.");
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    internal static byte[] RewriteStorageAddressArtifact(byte[] artifact,
        string defect)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationStorageAddressDraft;
        var index = TrRmArtifactIndex.ForForeignInvocationStorageAddressDraft(
            artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var sectionIndex = 0;
            sectionIndex < index.SectionCount; sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            if (kind != 0x0020)
            {
                sections.Add(new TrRmSection(kind, flags,
                    checked((uint)recordCount), section.ToArray()));
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            var homeMap = body.HomeMap.ToArray();
            if (defect == "missing-write-authority")
            {
                homeMap[1] = 0;
            }

            var records = new List<byte[]>(body.RecordCount);
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                records.Add(body.GetRecord(recordIndex).ToArray());
            }
            if (defect == "oversized-scalar-extent")
            {
                BinaryPrimitives.WriteUInt32LittleEndian(
                    records[2].AsSpan(12), 2);
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

            var rewrittenBody = TrRmRegisterBodyWriter.Encode(homeMap,
                body.TypeMap.ToArray(), body.MethodMap.ToArray(),
                body.FieldMap.ToArray(), body.ConstantMap.ToArray(), blocks,
                records.Select(static record => (ReadOnlyMemory<byte>)record)
                    .ToArray(),
                body.Code.ToArray(), contractCount, schema);
            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([rewrittenBody])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }
}
