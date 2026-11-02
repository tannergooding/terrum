// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmForeignInvocationAddressEmitterTests
{
    private const string A2EUScalarFrozenValuesSchemaIdentity
        = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int NativeAddressExport();

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
    public void A3NativeAddressProducerUsesOneRootLedgerForArtifactValidation()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        const string moduleIdentity = "native-address-a3";
        var compilation = CreateNativeAddressCompilation("example.dll");
        var program = compilation.Program
            ?? throw new InvalidOperationException(
                "A successful compilation must contain a bound program.");
        var publicArtifact = TrRmExecutableCompiler.EmitStandard(program,
            moduleIdentity, schemaIdentity, 1);

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var artifact = TrRmExecutableCompiler
                .EmitForeignInvocationDraftNativeAddressLibrary(program,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayGenericCopyDraft,
                    a3LayoutResourceLedger: ledger);
            Assert.That(artifact, Is.EqualTo(publicArtifact));
        });
    }

    [Test]
    public void A3StorageAddressProducerUsesOneRootLedgerForStaging()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-3-unique-1";
        const string moduleIdentity = "native-storage-address-a3";
        var sourceArtifact = CreateStorageAddressArtifact(moduleIdentity,
            schemaIdentity);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, sourceArtifact),
            moduleIdentity, []);
        var publicArtifact = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayGenericCopyDraft(
                publicArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayGenericCopyDraftAndGetExecutionTables(
                index);
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
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.GetRecord(2)[0], Is.EqualTo(0x0E));

        TrRmA3ResourceLedgerTestHelper.AssertExactWork(ledger =>
        {
            var artifact = TrRmExecutableCompiler
                .EmitForeignInvocationDraftNativeAddressLibrary(loaded,
                    moduleIdentity,
                    TrRmArtifactSchema
                        .ForeignInvocationStorageFixedArrayGenericCopyDraft,
                    a3LayoutResourceLedger: ledger);
            Assert.That(artifact, Is.EqualTo(publicArtifact));
        });
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
                init [uint32; 0] fixedEmpty;
                fixedEmpty = [];
                init mut [uint32; 0] mutableEmpty;
                mutableEmpty = [];
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
        var emptyInitializationCode = body.GetBlockCode(0).ToArray();
        Assert.That(emptyInitializationCode, Does.Contain((byte)0x91));
        Assert.That(emptyInitializationCode, Does.Contain((byte)0x92));
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
        Assert.That(homePlaceSelections, Is.EqualTo(3));
        Assert.That(elementPlaceSelections, Is.EqualTo(2));
        Assert.That(nullaryStores, Is.EqualTo(5));

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
        Assert.That(emptyInitializations, Has.Count.EqualTo(5));
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
            "nonzero-source-kind",
            "nonzero-source-index",
            "nonzero-source-use",
            "wrong-transition",
            "nonzero-dependency",
            "formal-present",
            "nonzero-flags",
            "nonzero-formal-ordinal",
            "wrong-place-kind",
            "wrong-home-access",
            "wrong-home-type",
            "out-of-range-home",
            "wrong-home-role",
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

    [Test]
    public async Task StandardA2ESourceFreeJitAndAotExecuteEmptyInitialization()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-initialization-1";
        const string moduleIdentity = "a2e-empty-array-runtime";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        const string source = """
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
                [uint32; 0] ordinary = [];
                init [uint32; 0] fixedEmpty;
                fixedEmpty = [];
                init mut [uint32; 0] mutableEmpty;
                mutableEmpty = [];
                int32 zeroIndex = 0;
                mut [[uint32; 0]; 2] nested = [[], []];
                mut uint32 adjustment = 0;
                mut uint32 increment = 0;
                mut int32 disabled = -1;
                unsafe {
                    var callback = &GetSystemTimeAdjustment;
                    callback(&adjustment, &increment, &disabled);
                }
                return disabled;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var initializations
            = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization init)
                {
                    initializations.Add(init);
                }
            }), Is.True);
        Assert.That(initializations, Has.Count.EqualTo(6));
        Assert.That(initializations.All(static init =>
            init.Layout.ByteExtent == 0), Is.True);
        Assert.That(initializations.Count(static init =>
            init.Layout.ElementCount == 0), Is.EqualTo(5));

        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyInitializationDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyInitializationDraft(
                        artifact));
        var mainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainRow = row;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        var storageAddressRecordCount = 0;
        for (var recordIndex = 0; recordIndex < body.RecordCount;
            recordIndex++)
        {
            storageAddressRecordCount += body.GetRecord(recordIndex)[0]
                == 0x0E ? 1 : 0;
        }
        Assert.That(storageAddressRecordCount, Is.EqualTo(3),
            "The only storage addresses are the three scalar import outputs.");

        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
        TestContext.WriteLine(
            $"A2E source-free runtime input: schema={schemaIdentity}; module={moduleIdentity}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={typeof(TrRmExecutableCompiler).Assembly.Location}; compiler-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TrRmExecutableCompiler).Assembly.Location)))}.");
        var jitResult = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2E source-free JIT: policy=FailFast; actual={jitResult}; allowed-disabled-range=0..1.");
        Assert.That(jitResult, Is.InRange(0, 1));

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The A2E native executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"A2E native executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"A2E source-free AOT: policy=FailFast; pid={processId}; actual={process.ExitCode}; allowed-disabled-range=0..1; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.InRange(0, 1));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [TestCase("init [uint32; 0] empty; return int32(empty[0]);")]
    [TestCase("init [uint32; 0] empty; empty = []; empty = []; return 0;")]
    [TestCase("init mut [uint32; 0] empty; empty = []; empty = []; return 0;")]
    public void A2ERejectsVacantReadsAndRepeatedNullaryInitialization(
        string body)
    {
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var source = $"int32 Main() {{ {body} }}";
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            options);
        Assert.That(compilation.HasErrors, Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
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

    [Test]
    public void StandardA2EUCapturesCheckedDivisionArgumentsBeforeUnitCalls()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-checked-arguments";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void Sleep(uint32 milliseconds);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void ObserveSigned(int32 value);

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
                mut uint32 numerator = 0;
                mut uint32 divisor = 0;
                mut int32 signedNumerator = -1;
                mut int32 signedDivisor = -1;
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 3] values = [
                        Sleep(numerator / divisor),
                        Sleep(numerator % divisor),
                        ObserveSigned(signedNumerator / signedDivisor)
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

        var arithmeticInstructions = new List<(byte Opcode, int Offset)>();
        var directCallOffsets = new List<int>();
        var firstUnitStoreOffset = int.MaxValue;
        var code = body.GetBlockCode(0);
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                opcode, body.Schema);
            if (opcode is 0x24 or 0x25)
            {
                arithmeticInstructions.Add((opcode, position));
            }
            else if (opcode == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    directCallOffsets.Add(position);
                }
            }
            else if (opcode == 0xD9)
            {
                firstUnitStoreOffset = Math.Min(firstUnitStoreOffset,
                    position);
            }

            position += instructionLength;
        }
        Assert.That(arithmeticInstructions.Select(
                static instruction => instruction.Opcode),
            Is.EqualTo(new byte[] { 0x24, 0x25, 0x24 }));
        Assert.That(directCallOffsets, Has.Count.EqualTo(3));
        for (var index = 0; index < arithmeticInstructions.Count; index++)
        {
            Assert.That(arithmeticInstructions[index].Offset,
                Is.LessThan(directCallOffsets[index]));
        }
        Assert.That(directCallOffsets[^1],
            Is.LessThan(firstUnitStoreOffset));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var capturedChecks = new List<BoundBinary>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Operator: TokenKind.Slash or TokenKind.Percent,
                    } check)
                {
                    capturedChecks.Add(check);
                }
            }), Is.True);
        Assert.That(capturedChecks, Has.Count.EqualTo(3));
        Assert.That(capturedChecks.All(static check =>
            (check.Type is PrimitiveType.Int32 or PrimitiveType.UInt32)
            && check.OverflowBehavior
                == BoundIntegerOverflowBehavior.Checked), Is.True);
        Assert.That(capturedChecks.Count(static check =>
            check.Type == PrimitiveType.UInt32), Is.EqualTo(2));
        Assert.That(capturedChecks.Count(static check =>
            check.Type == PrimitiveType.Int32), Is.EqualTo(1));
        Assert.That(capturedChecks.Count(static check =>
            check.Operator == TokenKind.Slash), Is.EqualTo(2));
        Assert.That(capturedChecks.Count(static check =>
            check.Operator == TokenKind.Percent), Is.EqualTo(1));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var malformed = RewriteA2EUCheckedArithmeticArtifact(artifact);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, malformed),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
    }

    [Test]
    public void StandardA2EUCapturesCheckedArrayAddressBeforeUnitCalls()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-checked-address";
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
            internal extern void ReadElement(ptr<uint32> value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordUnit();

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
                int32 invalidIndex = -1;
                mut [uint32; 1] source = [0];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 2] values = [
                        ReadElement(&source[invalidIndex]),
                        RecordUnit()
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

        var checkedAddressOffset = -1;
        var storageAddressOffset = -1;
        var directCallOffsets = new List<int>();
        var storeOffsets = new List<int>();
        var code = body.GetBlockCode(0);
        for (var position = 0; position < code.Length;)
        {
            var opcode = code[position];
            var instructionLength = TrRmRegisterBodyReader.InstructionLength(
                opcode, body.Schema);
            if (opcode == 0xD8)
            {
                var selection = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (selection[0] == 0x04 && selection[4] == 3
                    && selection[5] == 1 && selection[12] == 2
                    && selection[13] == 3)
                {
                    Assert.That(checkedAddressOffset, Is.EqualTo(-1));
                    checkedAddressOffset = position;
                }
            }
            else if (opcode == 0xEC && checkedAddressOffset >= 0
                && storageAddressOffset < 0)
            {
                storageAddressOffset = position;
            }
            else if (opcode == 0xE0)
            {
                var callRecord = body.GetRecord(
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        code[(position + 2)..]));
                if (callRecord[4] == 1)
                {
                    directCallOffsets.Add(position);
                }
            }
            else if (opcode == 0xD9)
            {
                storeOffsets.Add(position);
            }

            position += instructionLength;
        }
        Assert.That(checkedAddressOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(storageAddressOffset,
            Is.GreaterThan(checkedAddressOffset));
        Assert.That(directCallOffsets, Has.Count.EqualTo(2));
        Assert.That(storageAddressOffset,
            Is.LessThan(directCallOffsets[0]));
        Assert.That(directCallOffsets[^1],
            Is.LessThan(storeOffsets.First(offset =>
                offset > directCallOffsets[^1])));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var checkedAddressCalls = new List<BoundCall>();
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && loaded.Functions[call.Function].Name == "ReadElement")
                {
                    checkedAddressCalls.Add(call);
                }
            }), Is.True);
        var checkedAddressCall = checkedAddressCalls.Single();
        var address = checkedAddressCall.Arguments
            .OfType<BoundNativeStorageAddress>().Single();
        Assert.That(address.Index, Is.TypeOf<BoundRead>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EUCapturesAndReconstructsUnitConditionalArms()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-unit-array-conditional";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordFirst(int32 value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern void RecordSecond(ptr<uint32> value);

            [Terrum.Interop.NativeImport(
                "example.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordThird(uint32 value);

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
                bool choose = true;
                bool useFirst = false;
                int32 numerator = 0;
                int32 divisor = -1;
                uint32 unsignedNumerator = 0;
                uint32 unsignedDivisor = 0;
                int32 invalidIndex = -1;
                mut [uint32; 1] source = [0];
                mut uint32 input = 0;
                mut int32 output = 0;
                unsafe {
                    mut [void; 2] values = [
                        choose
                            ? (useFirst
                                ? RecordFirst(numerator / divisor)
                                : RecordSecond(&source[invalidIndex]))
                            : RecordThird(unsignedNumerator % unsignedDivisor),
                        ()
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
        Assert.That(body.BlockCount, Is.EqualTo(7));

        var conditionalEdges = new List<(
            int BlockIndex, int RecordIndex, int TrueBlock, int FalseBlock)>();
        var directCallBlocks = new List<int>();
        var storeBlocks = new List<int>();
        for (var blockIndex = 0; blockIndex < body.BlockCount; blockIndex++)
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var opcode = code[position];
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(opcode,
                        body.Schema);
                if (opcode == 0xE4)
                {
                    var conditionalEdgeIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]);
                    var edge = body.GetRecord(conditionalEdgeIndex);
                    conditionalEdges.Add((blockIndex, conditionalEdgeIndex,
                        BinaryPrimitives.ReadUInt16LittleEndian(edge[4..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(edge[6..])));
                }
                else if (opcode == 0xE0)
                {
                    var callSite = body.GetRecord(
                        BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]));
                    if (callSite[4] == 1)
                    {
                        directCallBlocks.Add(blockIndex);
                    }
                }
                else if (opcode == 0xD9)
                {
                    storeBlocks.Add(blockIndex);
                }

                position += instructionLength;
            }
        }

        Assert.That(conditionalEdges, Has.Count.EqualTo(2));
        var outerEdge = conditionalEdges.Single(static edge =>
            edge.BlockIndex == 0);
        var innerEdge = conditionalEdges.Single(static edge =>
            edge.BlockIndex == 1);
        Assert.That(outerEdge.RecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(innerEdge.RecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(body.GetRecord(outerEdge.RecordIndex)[0],
            Is.EqualTo(0x06));
        Assert.That(body.GetRecord(innerEdge.RecordIndex)[0],
            Is.EqualTo(0x06));
        Assert.That(outerEdge.TrueBlock, Is.Not.EqualTo(outerEdge.FalseBlock));
        Assert.That(innerEdge.TrueBlock, Is.Not.EqualTo(innerEdge.FalseBlock));
        Assert.That(directCallBlocks,
            Is.EquivalentTo(new[] {
                innerEdge.TrueBlock, innerEdge.FalseBlock, outerEdge.FalseBlock,
            }));
        var unitStoreBlocks = storeBlocks.Where(blockIndex =>
            blockIndex > Math.Max(innerEdge.FalseBlock,
                outerEdge.FalseBlock)).ToArray();
        Assert.That(unitStoreBlocks, Has.Length.EqualTo(2));
        Assert.That(unitStoreBlocks.All(blockIndex =>
            blockIndex == unitStoreBlocks[0]), Is.True);
        var trueArmCode = body.GetBlockCode(innerEdge.TrueBlock);
        var falseArmCode = body.GetBlockCode(innerEdge.FalseBlock);
        var outerFalseArmCode = body.GetBlockCode(outerEdge.FalseBlock);
        Assert.That(trueArmCode.IndexOf((byte)0x24),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(falseArmCode.IndexOf((byte)0xD8),
            Is.GreaterThanOrEqualTo(0));
        Assert.That(falseArmCode.IndexOf((byte)0xEC),
            Is.GreaterThan(falseArmCode.IndexOf((byte)0xD8)));
        Assert.That(outerFalseArmCode.IndexOf((byte)0x25),
            Is.GreaterThanOrEqualTo(0));
        static int FindInstructionOffset(ReadOnlySpan<byte> code,
            byte targetOpcode)
        {
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], TrRmArtifactSchema
                            .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft);
                if (code[position] == targetOpcode)
                {
                    return position;
                }

                position += instructionLength;
            }

            return -1;
        }

        Assert.That(FindInstructionOffset(trueArmCode, 0x24),
            Is.LessThan(FindInstructionOffset(trueArmCode, 0xE0)));
        var falsePlaceOffset = FindInstructionOffset(falseArmCode, 0xD8);
        Assert.That(falsePlaceOffset,
            Is.LessThan(FindInstructionOffset(falseArmCode, 0xEC)));
        Assert.That(FindInstructionOffset(falseArmCode, 0xEC),
            Is.LessThan(FindInstructionOffset(falseArmCode, 0xE0)));
        Assert.That(FindInstructionOffset(outerFalseArmCode, 0x25),
            Is.LessThan(FindInstructionOffset(outerFalseArmCode, 0xE0)));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, Array.Empty<TrRmStandardArtifactInput>());
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var conditionals = new List<BoundIf>();
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundIf branch)
                {
                    conditionals.Add(branch);
                }
            }), Is.True);
        Assert.That(conditionals, Has.Count.EqualTo(2));
        var outerConditional = conditionals.Single(branch =>
        {
            var nestedConditionals = 0;
            Assert.That(BoundTreeTraversal.VisitStatements(
                new BoundBlock([branch.Then], default),
                statement =>
                {
                    if (statement is BoundIf)
                    {
                        nestedConditionals++;
                    }
                }), Is.True);
            return nestedConditionals == 1;
        });
        var innerConditional = conditionals.Single(branch =>
            !ReferenceEquals(branch, outerConditional));
        static string[] GetCallNames(BoundStatement statement,
            BoundProgram program)
        {
            var names = new List<string>();
            Assert.That(BoundTreeTraversal.VisitExpressions(
                new BoundBlock([statement], default),
                expression =>
                {
                    if (expression is BoundCall call)
                    {
                        names.Add(program.Functions[call.Function].Name);
                    }
                }), Is.True);
            return [.. names];
        }

        Assert.That(GetCallNames(outerConditional.Then, loaded),
            Is.EquivalentTo(new[] { "RecordFirst", "RecordSecond" }));
        Assert.That(GetCallNames(outerConditional.Else!, loaded),
            Is.EqualTo(new[] { "RecordThird" }));
        Assert.That(GetCallNames(innerConditional.Then, loaded),
            Is.EqualTo(new[] { "RecordFirst" }));
        Assert.That(GetCallNames(innerConditional.Else!, loaded),
            Is.EqualTo(new[] { "RecordSecond" }));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));

        var malformed = RewriteA2EUConditionalRegisterArtifact(artifact, 0,
            14);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, malformed),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
        var malformedTarget = RewriteA2EUConditionalTargetArtifact(artifact,
            outerEdge.RecordIndex, checked((ushort)body.BlockCount));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedTarget),
                moduleIdentity, Array.Empty<TrRmStandardArtifactInput>()));
    }

    [Test]
    public void StandardA2EURejectsMismatchedComparisonPredicateRegister()
    {
        const string moduleIdentity
            = "a2eu-unit-conditional-comparison-malformed";
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, "markedError > clearError");
        var malformed = RewriteA2EUComparisonDestinationArtifact(
            input.Artifact.ToArray(), 1);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(input.SchemaIdentity,
                    malformed),
                moduleIdentity, []));
    }

    [Test]
    public void StandardA2EUScalarFrozenSchemaDoesNotRequireUnusedFeature()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string moduleIdentity = "a2eu-scalar-frozen-no-use";
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, schemaIdentity: schemaIdentity);
        var artifactIndex
            = TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    input.Artifact.Span);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                artifactIndex);
        var mainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainRow = row;
                break;
            }
        }

        Assert.That(mainRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.Zero);
        Assert.That(tables.Features.TryGetFeature(
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId, out _),
            Is.False);

        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
    }

    [Test]
    public void StandardA2EUScalarFrozenValuesRejectMalformedReferencesAndLegacySelection()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const string legacySchemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2eu-scalar-frozen-malformed";
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, conditionExpression: "clearError == 0",
            schemaIdentity: schemaIdentity);
        var artifact = input.Artifact.ToArray();
        var artifactIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                artifactIndex);
        var typedFrozenFeatureRow = 0;
        for (var row = 1; row <= tables.Features.Count; row++)
        {
            if (tables.Features.GetFeature(row).FeatureId
                == TrRmFeatureTableReader.TypedFrozenValuesFeatureId)
            {
                typedFrozenFeatureRow = row;
                break;
            }
        }
        Assert.That(typedFrozenFeatureRow, Is.GreaterThan(0));
        var missingTypedFrozenFeature = RewriteA2EUScalarFrozenSection(
            artifact, 0x0004, bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(
                    (typedFrozenFeatureRow - 1) * 8),
                    TrRmFeatureTableReader.NativeBoundaryContractsFeatureId));
        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    missingTypedFrozenFeature),
                moduleIdentity, []));

        var malformedSources = RewriteA2EUScalarFrozenSection(artifact,
            0x0030, static bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),
                    0x00FF_FFFF));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedSources),
                moduleIdentity, []));

        var malformedNodes = RewriteA2EUScalarFrozenSection(artifact,
            0x003F, static bytes =>
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8),
                    0x00FF_FFFF));
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedNodes),
                moduleIdentity, []));

        var malformedConstantMap
            = RewriteA2EUScalarConstantSourceMap(artifact, 0x00FF_FFFF);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    malformedConstantMap),
                moduleIdentity, []));

        Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(legacySchemaIdentity,
                    artifact),
                moduleIdentity, []));
    }

    [TestCase(true, false, "choose", false)]
    [TestCase(false, false, "choose", false)]
    [TestCase(true, true, "choose", false)]
    [TestCase(true, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false, "!choose", false)]
    [TestCase(true, false, "choose ? false : true", false)]
    [TestCase(true, false, "true", false)]
    [TestCase(false, false, "false", false)]
    [TestCase(true, false,
        "true || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "false && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false, "markedError != clearError", false)]
    [TestCase(true, false, "markedError == clearError", false)]
    [TestCase(true, false, "markedError < clearError", false)]
    [TestCase(true, false, "markedError <= clearError", false)]
    [TestCase(true, false, "markedError > clearError", false)]
    [TestCase(true, false, "markedError >= clearError", false)]
    [TestCase(true, false, "clearError == 0", false)]
    [TestCase(true, false, "0 == clearError", false)]
    [TestCase(true, false, "clearError != 0", false)]
    [TestCase(true, false, "0 != clearError", false)]
    [TestCase(true, false, "clearError < 1", false)]
    [TestCase(true, false, "0 <= clearError", false)]
    [TestCase(true, false, "1 > clearError", false)]
    [TestCase(true, false, "clearError >= 0", false)]
    [TestCase(true, false, "markedError == 0", false)]
    [TestCase(true, false, "0 == markedError", false)]
    [TestCase(true, false, "markedError != 0", false)]
    [TestCase(true, false, "0 != markedError", false)]
    [TestCase(true, false, "GetCurrentProcessId() > 0", false)]
    [TestCase(true, false, "0 == GetCurrentProcessId()", false)]
    [TestCase(true, false, "GetCurrentProcessId() == 0", false)]
    [TestCase(true, false, "uint32(0) == uint32(0)", false)]
    [TestCase(true, false, "choose == other", false)]
    [TestCase(false, false, "choose == other", false)]
    [TestCase(true, false, "choose != other", false)]
    [TestCase(false, false, "choose != other", false)]
    [TestCase(true, false, "choose == true", false)]
    [TestCase(false, false, "choose == true", false)]
    [TestCase(true, false, "choose == false", false)]
    [TestCase(false, false, "choose == false", false)]
    [TestCase(true, false, "choose != true", false)]
    [TestCase(false, false, "choose != true", false)]
    [TestCase(true, false, "choose != false", false)]
    [TestCase(false, false, "choose != false", false)]
    [TestCase(true, false, "clearError == GetCurrentProcessId()", false)]
    [TestCase(true, false, "GetCurrentProcessId() > clearError", false)]
    [TestCase(true, false,
        "GetCurrentProcessId() == GetCurrentProcessId()", false)]
    [TestCase(true, false,
        "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)", false)]
    [TestCase(true, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", true)]
    public void StandardA2EUConditionalJitExecutesOnlyTheSelectedNativeArm(
        bool choose, bool failFalseArm, string conditionExpression,
        bool useIfStatement)
    {
        const string moduleIdentity = "a2eu-unit-conditional-jit";
        AssertA2EUWindowsX64();
        var schemaIdentity = GetA2EUConditionalSchemaIdentity(
            conditionExpression);
        var input = CreateStandardA2EUConditionalRuntimeInput(choose,
            moduleIdentity, failFalseArm, conditionExpression,
            useIfStatement, schemaIdentity);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        if (conditionExpression is
            "choose || IsProcessorFeaturePresent(markedError / clearError)"
            or "choose && IsProcessorFeaturePresent(markedError / clearError)")
        {
            var main = loaded.Functions.Single(static function =>
                function.Name == "Main");
            var conditionalBlocks = new List<BoundIf>();
            Assert.That(BoundTreeTraversal.VisitStatements(main.Body,
                statement =>
                {
                    if (statement is BoundIf conditional)
                    {
                        conditionalBlocks.Add(conditional);
                    }
                }), Is.True);
            Assert.That(conditionalBlocks, Has.Count.GreaterThanOrEqualTo(2));
        }

        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, input.SchemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(input.Artifact.ToArray()));
        if (conditionExpression == "clearError == GetCurrentProcessId()")
        {
            AssertA2EUComparisonCallSequence(input.Artifact.ToArray(), 2,
                requiresReadSnapshot: true);
        }
        else if (conditionExpression
            == "GetCurrentProcessId() == GetCurrentProcessId()")
        {
            AssertA2EUComparisonCallSequence(input.Artifact.ToArray(), 3,
                requiresReadSnapshot: false);
        }

        var result = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        var conditionValue = conditionExpression switch {
            "choose" => choose,
            "!choose" or "choose ? false : true" => !choose,
            "true" or "true || IsProcessorFeaturePresent(markedError / clearError)" => true,
            "false" or "false && IsProcessorFeaturePresent(markedError / clearError)" => false,
            "choose || IsProcessorFeaturePresent(markedError / clearError)" => true,
            "choose && IsProcessorFeaturePresent(markedError / clearError)" => false,
            "markedError != clearError" or "markedError > clearError"
                or "markedError >= clearError" => true,
            "markedError == clearError" or "markedError < clearError"
                or "markedError <= clearError" => false,
            "clearError == 0" or "0 == clearError"
                or "clearError < 1" or "0 <= clearError"
                or "1 > clearError" or "clearError >= 0"
                or "uint32(0) == uint32(0)" => true,
            "clearError != 0" or "0 != clearError" => false,
            "markedError == 0" or "0 == markedError"
                or "0 == GetCurrentProcessId()"
                or "GetCurrentProcessId() == 0" => false,
            "markedError != 0" or "0 != markedError"
                or "GetCurrentProcessId() > 0" => true,
            "choose == other" => choose,
            "choose != other" => !choose,
            "choose == true" or "choose != false" => choose,
            "choose == false" or "choose != true" => !choose,
            "clearError == GetCurrentProcessId()" => false,
            "GetCurrentProcessId() > clearError" => true,
            "GetCurrentProcessId() == GetCurrentProcessId()"
                or "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)" => true,
            _ => throw new InvalidOperationException(
                "The A2EU test condition has no expected normal result."),
        };
        var expected = conditionValue ? 0 : Environment.ProcessId;
        TestContext.WriteLine(
            $"A2EU source-free JIT: choose={choose}; failFalseArm={failFalseArm}; useIfStatement={useIfStatement}; condition={conditionExpression}; policy=FailFast; expected={expected}; actual={result}.");
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(true, false, "choose", false)]
    [TestCase(false, false, "choose", false)]
    [TestCase(true, true, "choose", false)]
    [TestCase(false, true, "choose", false)]
    [TestCase(true, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false,
        "choose && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(true, false,
        "true || IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "false && IsProcessorFeaturePresent(markedError / clearError)", false)]
    [TestCase(false, false,
        "choose || IsProcessorFeaturePresent(markedError / clearError)", true)]
    [TestCase(true, false, "markedError > clearError", false)]
    [TestCase(true, false, "markedError <= clearError", false)]
    [TestCase(true, false, "choose == other", false)]
    [TestCase(false, false, "choose == other", false)]
    [TestCase(true, false, "choose != other", false)]
    [TestCase(false, false, "choose != other", false)]
    [TestCase(true, false, "choose == true", false)]
    [TestCase(false, false, "choose == true", false)]
    [TestCase(true, false, "choose == false", false)]
    [TestCase(false, false, "choose == false", false)]
    [TestCase(true, false, "choose != true", false)]
    [TestCase(false, false, "choose != true", false)]
    [TestCase(true, false, "choose != false", false)]
    [TestCase(false, false, "choose != false", false)]
    [TestCase(true, false, "clearError == GetCurrentProcessId()", false)]
    [TestCase(true, false, "GetCurrentProcessId() > clearError", false)]
    [TestCase(true, false, "clearError == 0", false)]
    [TestCase(true, false, "0 == clearError", false)]
    [TestCase(true, false, "markedError == 0", true)]
    [TestCase(true, false, "GetCurrentProcessId() > 0", false)]
    [TestCase(true, false, "uint32(0) == uint32(0)", false)]
    [TestCase(true, false,
        "GetCurrentProcessId() == GetCurrentProcessId()", false)]
    [TestCase(true, false,
        "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)", false)]
    [TestCase(true, false,
        "IsProcessorFeaturePresent(markedError / clearError) == other", false)]
    public async Task StandardA2EUConditionalAotExecutesOnlyTheSelectedNativeArm(
        bool choose, bool failFalseArm, string conditionExpression,
        bool useIfStatement)
    {
        const string moduleIdentity = "a2eu-unit-conditional-aot";
        AssertA2EUWindowsX64();
        var schemaIdentity = GetA2EUConditionalSchemaIdentity(
            conditionExpression);
        var input = CreateStandardA2EUConditionalRuntimeInput(choose,
            moduleIdentity, failFalseArm, conditionExpression,
            useIfStatement, schemaIdentity);
        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The A2EU native executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"A2EU native executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            var conditionCallIsSelected
                = conditionExpression switch {
                    "choose || IsProcessorFeaturePresent(markedError / clearError)"
                        => !choose,
                    "choose && IsProcessorFeaturePresent(markedError / clearError)"
                        => choose,
                    "IsProcessorFeaturePresent(markedError / clearError) == other"
                        => true,
                    _ => false,
                };
            var conditionValue = conditionExpression switch {
                "choose" => choose,
                "!choose" or "choose ? false : true" => !choose,
                "true" or "true || IsProcessorFeaturePresent(markedError / clearError)" => true,
                "false" or "false && IsProcessorFeaturePresent(markedError / clearError)" => false,
                "choose || IsProcessorFeaturePresent(markedError / clearError)" => true,
                "choose && IsProcessorFeaturePresent(markedError / clearError)" => false,
                "markedError > clearError" => true,
                "markedError <= clearError" => false,
                "clearError == 0" or "0 == clearError"
                    or "uint32(0) == uint32(0)" => true,
                "markedError == 0" => false,
                "GetCurrentProcessId() > 0" => true,
                "choose == other" => choose,
                "choose != other" => !choose,
                "choose == true" or "choose != false" => choose,
                "choose == false" or "choose != true" => !choose,
                "clearError == GetCurrentProcessId()" => false,
                "GetCurrentProcessId() > clearError" => true,
                "GetCurrentProcessId() == GetCurrentProcessId()"
                    or "IsProcessorFeaturePresent(markedError) == IsProcessorFeaturePresent(markedError)" => true,
                "IsProcessorFeaturePresent(markedError / clearError) == other" => false,
                _ => throw new InvalidOperationException(
                    "The A2EU test condition has no expected normal result."),
            };
            var expected = conditionCallIsSelected
                    || failFalseArm && !conditionValue
                ? unchecked((int)0xE0540002)
                : conditionValue ? 0 : processId;
            TestContext.WriteLine(
                $"A2EU source-free AOT: choose={choose}; failFalseArm={failFalseArm}; useIfStatement={useIfStatement}; condition={conditionExpression}; policy=FailFast; pid={processId}; expected={expected}; actual={process.ExitCode}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(expected));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardA2EUScalarLiteralComparisonTypesAndPredicatesRoundTrip()
    {
        const string moduleIdentity = "a2eu-scalar-literal-matrix";
        var scalarTypes = new (string TypeName, string VariableName,
            string Zero, string One)[] {
            ("int8", "signed8", "0", "1"),
            ("int16", "signed16", "0", "1"),
            ("int32", "signed32", "0", "1"),
            ("int64", "signed64", "0", "1"),
            ("uint8", "unsigned8", "0", "1"),
            ("uint16", "unsigned16", "0", "1"),
            ("uint32", "unsigned32", "0", "1"),
            ("uint64", "unsigned64", "0", "1"),
            ("float32", "single", "0.0", "1.0"),
            ("float64", "double", "0.0", "1.0"),
        };
        var declarations = new StringBuilder();
        var falseComparisons = new List<string>(61);
        foreach (var scalarType in scalarTypes)
        {
            declarations.Append(scalarType.TypeName).Append(' ')
                .Append(scalarType.VariableName).Append(" = ")
                .Append(scalarType.Zero).AppendLine(";");
            falseComparisons.Add(
                $"{scalarType.VariableName} == {scalarType.One}");
            falseComparisons.Add(
                $"{scalarType.VariableName} != {scalarType.Zero}");
            falseComparisons.Add(
                $"{scalarType.VariableName} < {scalarType.Zero}");
            falseComparisons.Add(
                $"{scalarType.One} <= {scalarType.VariableName}");
            falseComparisons.Add(
                $"{scalarType.Zero} > {scalarType.VariableName}");
            falseComparisons.Add(
                $"{scalarType.VariableName} >= {scalarType.One}");
        }

        falseComparisons.Add("uint32(0) == uint32(1)");
        var additionalStatements = new StringBuilder(
            "SetLastError(markedError);");
        foreach (var comparison in falseComparisons)
        {
            additionalStatements.Append(" if (")
                .Append(comparison)
                .Append(") { SetLastError(clearError); }");
        }
        var input = CreateStandardA2EUConditionalRuntimeInput(true,
            moduleIdentity, false, "choose", schemaIdentity:
                A2EUScalarFrozenValuesSchemaIdentity,
            additionalLocals: declarations.ToString(),
            additionalStatements: additionalStatements.ToString());
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                        input.Artifact.Span));
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
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.EqualTo(20));
        Assert.That(tables.ConstantSources.Count, Is.EqualTo(20));
        Assert.That(tables.FrozenValueNodes.Count, Is.EqualTo(20));
        Assert.That(TrRmFeatureManifestValidator.Requires(tables.Features,
            tables.Modules, 1,
            TrRmFeatureTableReader.TypedFrozenValuesFeatureId), Is.True);

        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var comparisons = new List<BoundBinary>();
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison
                    && (comparison.Left is BoundConstant
                        || comparison.Right is BoundConstant))
                {
                    comparisons.Add(comparison);
                }
            }), Is.True);
        Assert.That(comparisons, Has.Count.EqualTo(60));
        foreach (var scalarType in scalarTypes)
        {
            var boundType = scalarType.TypeName switch {
                "int8" => PrimitiveType.Int8,
                "int16" => PrimitiveType.Int16,
                "int32" => PrimitiveType.Int32,
                "int64" => PrimitiveType.Int64,
                "uint8" => PrimitiveType.UInt8,
                "uint16" => PrimitiveType.UInt16,
                "uint32" => PrimitiveType.UInt32,
                "uint64" => PrimitiveType.UInt64,
                "float32" => PrimitiveType.Float32,
                "float64" => PrimitiveType.Float64,
                _ => throw new InvalidOperationException(
                    "The A2EU test type has no bound primitive."),
            };
            Assert.That(comparisons.Count(comparison =>
                comparison.Left.Type == boundType),
                Is.EqualTo(6));
        }

        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, A2EUScalarFrozenValuesSchemaIdentity, 1),
            Is.EqualTo(input.Artifact.ToArray()));
        var result = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2EU+SF1 scalar matrix source-free JIT: expected={Environment.ProcessId}; actual={result}; comparisons={comparisons.Count}; constants={body.ConstantCount}; policy=FailFast.");
        Assert.That(result, Is.EqualTo(Environment.ProcessId));
    }

    private static TrRmStandardArtifactInput
        CreateStandardA2EUConditionalRuntimeInput(bool choose,
            string moduleIdentity, bool failFalseArm,
            string conditionExpression = "choose",
            bool useIfStatement = false,
            string? schemaIdentity = null,
            string additionalLocals = "",
            string additionalStatements = "")
    {
        schemaIdentity ??=
            "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var falseAction = failFalseArm
            ? "SetLastError(markedError / clearError)"
            : "SetLastError(markedError)";
        var conditional = useIfStatement
            ? $"if ({conditionExpression}) {{ SetLastError(clearError); }} "
                + $"else {{ {falseAction}; }}"
            : $"({conditionExpression}) ? SetLastError(clearError) : {falseAction};";
        var source = $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern bool IsProcessorFeaturePresent(uint32 feature);

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void SetLastError(uint32 errorCode);

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetLastError",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            // Both selected values fit the test's signed result surface.
            internal extern int32 GetLastError();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 GetCurrentProcessId();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            internal extern int32 IsBadReadPtr(
                ptr<int32> memory,
                uint64 byteCount);

            int32 Main() {
                unsafe {
                    bool choose = {{choose.ToString().ToLowerInvariant()}};
                    bool other = true;
                    uint32 clearError = 0;
                    uint32 markedError = GetCurrentProcessId();
                    {{additionalLocals}}
                    {{conditional}}
                    {{additionalStatements}}
                    int32 observedError = GetLastError();
                    uint64 byteCount = 0;
                    var callback = &IsBadReadPtr;
                    callback(&observedError, byteCount);
                    return observedError;
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        TestContext.WriteLine(
            $"A2EU source-free runtime input: schema={schemaIdentity}; module={moduleIdentity}; source-sha256={Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={typeof(TrRmExecutableCompiler).Assembly.Location}; compiler-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(TrRmExecutableCompiler).Assembly.Location)))}.");
        return new TrRmStandardArtifactInput(schemaIdentity, artifact);
    }

    private static bool IsA2EUScalarLiteralComparison(
        string conditionExpression)
        => conditionExpression is "clearError == 0" or "0 == clearError"
            or "clearError != 0" or "0 != clearError"
            or "clearError < 1" or "0 <= clearError"
            or "1 > clearError" or "clearError >= 0"
            or "markedError == 0" or "0 == markedError"
            or "markedError != 0" or "0 != markedError"
            or "GetCurrentProcessId() > 0"
            or "0 == GetCurrentProcessId()"
            or "GetCurrentProcessId() == 0"
            or "uint32(0) == uint32(0)";

    private static string GetA2EUConditionalSchemaIdentity(
        string conditionExpression)
        => IsA2EUScalarLiteralComparison(conditionExpression)
            ? A2EUScalarFrozenValuesSchemaIdentity
            : "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";

    private static void AssertA2EUWindowsX64()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
    }

    private static void AssertA2EUComparisonCallSequence(byte[] artifact,
        int expectedCallCount, bool requiresReadSnapshot)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var body = new TrRmRegisterBodyReader(bodyTable.GetRow(1),
                contractCount, schema);
            for (var blockIndex = 0; blockIndex < body.BlockCount;
                blockIndex++)
            {
                var code = body.GetBlockCode(blockIndex);
                var callOffsets = new List<int>();
                var copyStoreOffsets = new List<int>();
                var compareOffset = -1;
                for (var position = 0; position < code.Length;)
                {
                    var opcode = code[position];
                    var instructionLength
                        = TrRmRegisterBodyReader.InstructionLength(
                            opcode, schema);
                    if (opcode == 0xE0)
                    {
                        callOffsets.Add(position);
                    }
                    else if (opcode == 0xD6)
                    {
                        copyStoreOffsets.Add(position);
                    }
                    else if (opcode == 0x40)
                    {
                        compareOffset = position;
                    }

                    position += instructionLength;
                }

                if (compareOffset < 0)
                {
                    continue;
                }

                Assert.That(callOffsets, Has.Count.EqualTo(expectedCallCount));
                Assert.That(callOffsets[^1], Is.LessThan(compareOffset));
                if (requiresReadSnapshot)
                {
                    Assert.That(copyStoreOffsets.Any(offset =>
                        offset > callOffsets[0]
                        && offset < callOffsets[1]), Is.True);
                }

                return;
            }
        }

        throw new InvalidOperationException(
            "The A2EU artifact has no scalar comparison call block.");
    }

    [Test]
    public void StandardA2EUConditionalBodySurvivesSelectedModuleBodyRowRemapping()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        const string firstModuleIdentity = "a2eu-conditional-other";
        const string selectedModuleIdentity = "a2eu-conditional-selected";
        const string source = """
            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordFirst();

            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordSecond();

            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 ReadProcessId();

            [Terrum.Interop.NativeImport(
                "MODULE",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void Observe(write ptr<int32> output);

            int32 Main() {
                uint32 zero = 0;
                mut int32 output = 0;
                unsafe {
                    uint32 processId = ReadProcessId();
                    processId > zero
                        ? RecordFirst()
                        : RecordSecond();
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """;
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var firstCompilation = Compilation.Create(
            SyntaxTree.Parse(source.Replace("MODULE", "other.dll",
                StringComparison.Ordinal)), options);
        var selectedCompilation = Compilation.Create(
            SyntaxTree.Parse(source.Replace("MODULE", "selected.dll",
                StringComparison.Ordinal)), options);
        Assert.That(firstCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, firstCompilation.Diagnostics));
        Assert.That(selectedCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                selectedCompilation.Diagnostics));

        var firstArtifact = TrRmExecutableCompiler.EmitStandard(
            firstCompilation.Program!, firstModuleIdentity, schemaIdentity, 1);
        var selectedArtifact = TrRmExecutableCompiler.EmitStandard(
            selectedCompilation.Program!, selectedModuleIdentity,
            schemaIdentity, 1);
        var combinedArtifact = CombinePopulatedNativeAddressModules(
            firstArtifact, selectedArtifact, schema);
        var tables
            = TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    TrRmArtifactIndex
                        .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                            combinedArtifact));
        var selectedModuleRow = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            if (tables.Modules.GetIdentity(moduleRow)
                == selectedModuleIdentity)
            {
                selectedModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(selectedModuleRow, Is.EqualTo(2));
        var callerRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow)
                    == selectedModuleRow
                && tables.Methods.GetName(methodRow) == "Main")
            {
                callerRow = methodRow;
                break;
            }
        }
        Assert.That(callerRow, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(callerRow), Is.EqualTo(2));
        tables.ValidateRegisterBodyMapsForMethod(callerRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(callerRow), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        var edgeRecordIndex = -1;
        foreach (var blockIndex in Enumerable.Range(0, body.BlockCount))
        {
            var code = body.GetBlockCode(blockIndex);
            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], body.Schema);
                if (code[position] == 0xE4)
                {
                    edgeRecordIndex
                        = BinaryPrimitives.ReadUInt16LittleEndian(
                            code[(position + 2)..]);
                }

                position += instructionLength;
            }
        }
        Assert.That(edgeRecordIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(body.GetRecord(edgeRecordIndex)[0], Is.EqualTo(0x06));

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, combinedArtifact),
            selectedModuleIdentity, []);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Observe").DirectNativeImport!.LibraryName,
            Is.EqualTo("selected.dll"));
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var selectedCalls = new List<string>();
        var conditionalCount = 0;
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundIf)
                {
                    conditionalCount++;
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && loaded.Functions[call.Function].Name
                        is "RecordFirst" or "RecordSecond")
                {
                    selectedCalls.Add(loaded.Functions[call.Function].Name);
                }
            }), Is.True);
        Assert.That(conditionalCount, Is.EqualTo(1));
        Assert.That(selectedCalls,
            Is.EquivalentTo(new[] { "RecordFirst", "RecordSecond" }));
    }

    [Test]
    public void StandardA2EUScalarLiteralProviderBodySurvivesCoLocatedRemappingAndReplacement()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-scalar-frozen-values-1";
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
        const string providerIdentity = "a2eu-conditional-provider";
        const string consumerIdentity = "a2eu-conditional-consumer";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration: NativeCoreRegistrationTests.CreateRegistration());
        const string providerSource = """
            [Terrum.Interop.NativeImport(
                "provider.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            public extern void Observe(write ptr<int32> output);

            internal void ProviderBody(write ptr<int32> output) {
                unsafe {
                    var callback = &Observe;
                    callback(output);
                }
            }
            """;
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var providerIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                providerArtifact);
        var providerTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                providerIndex);
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, []);
        var consumerOptions = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport(
                "consumer.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordFirst();

            [Terrum.Interop.NativeImport(
                "consumer.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern void RecordSecond();

            [Terrum.Interop.NativeImport(
                "consumer.dll",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern uint32 GetCode();

            int32 Main() {
                mut int32 output = 0;
                unsafe {
                    GetCode() == 1 ? RecordFirst() : RecordSecond();
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """), providerReference, consumerOptions);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, consumerIdentity, schemaIdentity, 1);
        var combinedArtifact = CombinePopulatedNativeAddressModules(
            providerArtifact, consumerArtifact, schema);
        var combinedIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                combinedArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                combinedIndex);
        var consumerModuleRow = 0;
        for (var moduleRow = 1; moduleRow <= tables.Modules.Count;
            moduleRow++)
        {
            if (tables.Modules.GetIdentity(moduleRow) == consumerIdentity)
            {
                consumerModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(consumerModuleRow, Is.EqualTo(2));
        var mainRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow) == consumerModuleRow
                && tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        Assert.That(tables.Methods.GetBodyRow(mainRow), Is.EqualTo(2));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        Assert.That(body.BlockCount, Is.EqualTo(4));
        Assert.That(tables.ConstantSources.Count,
            Is.GreaterThan(providerTables.ConstantSources.Count));
        Assert.That(tables.FrozenValueNodes.Count,
            Is.GreaterThan(providerTables.FrozenValueNodes.Count));
        Assert.That(tables.Blobs.Count, Is.GreaterThan(providerTables.Blobs.Count));
        Assert.That(body.ConstantCount, Is.GreaterThan(0));
        for (var offset = 0; offset < body.ConstantMap.Length;
            offset += 2 * sizeof(uint))
        {
            var sourceRow = BinaryPrimitives.ReadUInt32LittleEndian(
                body.ConstantMap[(offset + sizeof(uint))..]);
            Assert.That(sourceRow,
                Is.GreaterThan((uint)providerTables.ConstantSources.Count));
            var nodeRow = tables.ConstantSources.GetPayloadReference(
                checked((int)sourceRow));
            Assert.That(nodeRow,
                Is.GreaterThan((uint)providerTables.FrozenValueNodes.Count));
            var blobRow = tables.FrozenValueNodes.GetScalarBlobRef(
                checked((int)nodeRow));
            Assert.That(blobRow,
                Is.GreaterThan((uint)providerTables.Blobs.Count));
        }

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, combinedArtifact),
            consumerIdentity, []);
        var target = loaded.Functions.Single(static function =>
            function.Name == "Observe");
        Assert.That(target.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(target.DirectNativeImport!.LibraryName,
            Is.EqualTo("provider.dll"));
        var loadedMain = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var conditionalCount = 0;
        var comparisonConstants = new List<BoundConstant>();
        var selectedCallNames = new List<string>();
        Assert.That(BoundTreeTraversal.VisitStatements(loadedMain.Body,
            statement =>
            {
                if (statement is BoundIf)
                {
                    conditionalCount++;
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison)
                {
                    if (comparison.Left is BoundConstant left)
                    {
                        comparisonConstants.Add(left);
                    }
                    if (comparison.Right is BoundConstant right)
                    {
                        comparisonConstants.Add(right);
                    }
                }
            }), Is.True);
        Assert.That(BoundTreeTraversal.VisitExpressions(loadedMain.Body,
            expression =>
            {
                if (expression is BoundCall call
                    && loaded.Functions[call.Function].Name
                        is "RecordFirst" or "RecordSecond")
                {
                    selectedCallNames.Add(
                        loaded.Functions[call.Function].Name);
                }
            }), Is.True);
        Assert.That(conditionalCount, Is.EqualTo(1));
        Assert.That(comparisonConstants, Has.Count.EqualTo(1));
        Assert.That(comparisonConstants[0].Type, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(comparisonConstants[0].Bits.Low, Is.EqualTo(1));
        Assert.That(selectedCallNames,
            Is.EquivalentTo(new[] { "RecordFirst", "RecordSecond" }));
        var reencodedConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            loaded, consumerIdentity, schemaIdentity, 1);
        var reencodedIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                reencodedConsumerArtifact);
        var reencodedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                reencodedIndex);
        Assert.That(reencodedTables.ConstantSources.Count,
            Is.GreaterThan(0));
        var reencodedLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                reencodedConsumerArtifact),
            consumerIdentity, [providerInput]);
        var reencodedMain = reencodedLoaded.Functions.Single(
            static function => function.Name == "Main");
        var reencodedComparisonConstants = new List<BoundConstant>();
        Assert.That(BoundTreeTraversal.VisitExpressions(reencodedMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison
                    && comparison.Right is BoundConstant constant)
                {
                    reencodedComparisonConstants.Add(constant);
                }
            }), Is.True);
        Assert.That(reencodedComparisonConstants, Has.Count.EqualTo(1));
        Assert.That(reencodedComparisonConstants[0].Type,
            Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(reencodedComparisonConstants[0].Bits.Low, Is.EqualTo(1));

        const string secondConsumerIdentity
            = "a2eu-conditional-consumer-second";
        var secondConsumerArtifact = TrRmExecutableCompiler.EmitStandard(
            consumerCompilation.Program!, secondConsumerIdentity,
            schemaIdentity, 1);
        var twiceCombinedArtifact = CombinePopulatedNativeAddressModules(
            combinedArtifact, secondConsumerArtifact, schema);
        var twiceCombinedIndex = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                twiceCombinedArtifact);
        var twiceCombinedTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                twiceCombinedIndex);
        var secondConsumerModuleRow = 0;
        for (var moduleRow = 1;
            moduleRow <= twiceCombinedTables.Modules.Count; moduleRow++)
        {
            if (twiceCombinedTables.Modules.GetIdentity(moduleRow)
                == secondConsumerIdentity)
            {
                secondConsumerModuleRow = moduleRow;
                break;
            }
        }
        Assert.That(secondConsumerModuleRow, Is.GreaterThan(0));
        var secondMainRow = 0;
        for (var methodRow = 1;
            methodRow <= twiceCombinedTables.Methods.Count; methodRow++)
        {
            if (twiceCombinedTables.Methods.GetOwnerModule(methodRow)
                    == secondConsumerModuleRow
                && twiceCombinedTables.Methods.GetName(methodRow) == "Main")
            {
                secondMainRow = methodRow;
                break;
            }
        }
        Assert.That(secondMainRow, Is.GreaterThan(0));
        var secondConsumerBodyRow
            = twiceCombinedTables.Methods.GetBodyRow(secondMainRow);
        Assert.That(secondConsumerBodyRow,
            Is.EqualTo(tables.Bodies.Count + 1));
        var secondConsumerBody = twiceCombinedTables.Bodies
            .GetRegisterBody(secondConsumerBodyRow,
                twiceCombinedTables.Contracts.Count);
        Assert.That(secondConsumerBody.ConstantCount, Is.EqualTo(1));
        var secondSourceRow = BinaryPrimitives.ReadUInt32LittleEndian(
            secondConsumerBody.ConstantMap[sizeof(uint)..]);
        Assert.That(secondSourceRow,
            Is.EqualTo((uint)tables.ConstantSources.Count + 1));
        var secondNodeRow = twiceCombinedTables.ConstantSources
            .GetPayloadReference(checked((int)secondSourceRow));
        Assert.That(secondNodeRow,
            Is.EqualTo((uint)tables.FrozenValueNodes.Count + 1));
        var secondBlobRow = twiceCombinedTables.FrozenValueNodes
            .GetScalarBlobRef(checked((int)secondNodeRow));
        Assert.That(secondBlobRow,
            Is.EqualTo((uint)tables.Blobs.Count + 1));
        Assert.That(twiceCombinedTables.FrozenValueNodes.GetScalarPayload(
            checked((int)secondNodeRow), twiceCombinedTables.Blobs).ToArray(),
            Is.EqualTo(new byte[] { 1, 0, 0, 0 }));

        var compatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(providerSource.Replace("ProviderBody",
                "ReplacementBody", StringComparison.Ordinal)), core);
        Assert.That(compatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compatibleProviderCompilation.Diagnostics));
        var compatibleProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            compatibleProviderCompilation.Program!, providerIdentity,
            schemaIdentity, 1);
        var compatibleCombinedArtifact = CombinePopulatedNativeAddressModules(
            compatibleProviderArtifact, consumerArtifact, schema);
        var compatibleLoaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                compatibleCombinedArtifact),
            consumerIdentity, []);
        Assert.That(compatibleLoaded.Functions.Single(
            static function => function.Name == "Observe")
            .ModuleImport!.ProducerIdentity, Is.EqualTo(providerIdentity));
        var compatibleMain = compatibleLoaded.Functions.Single(
            static function => function.Name == "Main");
        var compatibleComparisonConstants = new List<BoundConstant>();
        Assert.That(BoundTreeTraversal.VisitExpressions(compatibleMain.Body,
            expression =>
            {
                if (expression is BoundBinary {
                        Type: PrimitiveType.Bool,
                    } comparison
                    && comparison.Right is BoundConstant constant)
                {
                    compatibleComparisonConstants.Add(constant);
                }
            }), Is.True);
        Assert.That(compatibleComparisonConstants, Has.Count.EqualTo(1));
        Assert.That(compatibleComparisonConstants[0].Bits.Low, Is.EqualTo(1));

        var incompatibleProviderSource = providerSource
            .Replace("NativeParameter(1, Write, NoEscape, 1)",
                "NativeParameter(1, Read, NoEscape, 1)",
                StringComparison.Ordinal)
            .Replace("Observe(write ptr<int32> output)",
                "Observe(ptr<int32> output)", StringComparison.Ordinal)
            .Replace("ProviderBody(write ptr<int32> output)",
                "ProviderBody(ptr<int32> output)", StringComparison.Ordinal);
        var incompatibleProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(incompatibleProviderSource), core);
        Assert.That(incompatibleProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleProviderCompilation.Diagnostics));
        var incompatibleProviderArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleProviderCompilation.Program!, providerIdentity,
                schemaIdentity, 1);
        var incompatibleCombinedArtifact
            = CombinePopulatedNativeAddressModules(
                incompatibleProviderArtifact, consumerArtifact, schema);
        Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleCombinedArtifact),
                consumerIdentity, []));
    }

    [TestCase(1, "uint32", "[0]", PrimitiveType.UInt32, false, 1)]
    [TestCase(3, "uint32", "[0, 0, 0]", PrimitiveType.UInt32, false, 1)]
    [TestCase(2, "int32", "[0, -1]", PrimitiveType.Int32, true, 1)]
    [TestCase(3, "uint32", "[0, 0, 0]", PrimitiveType.UInt32, false, 2)]
    public void StandardA2EmitsAndReconstructsInitializedArrayStorageAddress(
        int elementCount, string elementType, string initializer,
        PrimitiveType expectedElementType, bool hasAllBitsSetElement,
        int nativeParameterExtent)
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
            [Terrum.Interop.NativeParameter(
                1, Read, NoEscape, {{nativeParameterExtent}})]
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
        var arrayRequiredElementCount = 0u;
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
                    if (bodyRecord[4] == 1)
                    {
                        arrayRequiredElementCount
                            = BinaryPrimitives.ReadUInt32LittleEndian(
                                bodyRecord[12..]);
                    }
                    break;
            }
        }
        Assert.That(placeSelectionCount, Is.EqualTo(elementCount + 1));
        Assert.That(semanticStoreCount, Is.EqualTo(elementCount));
        Assert.That(storageAddressCount, Is.EqualTo(2));
        Assert.That(arrayRequiredElementCount,
            Is.EqualTo((uint)nativeParameterExtent));
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
        Assert.That(arrayAddress.ByteExtent,
            Is.EqualTo((ulong)nativeParameterExtent * 4));
        Assert.That(arrayAddress.Index, Is.TypeOf<BoundRead>());
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [Test]
    public void StandardA2EmitsAndReconstructsNestedArrayInitialization()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2";
        const string moduleIdentity = "a2-nested-array-initialization";
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
            internal extern void Observe(write ptr<int32> output);

            int32 Main() {
                int32 zeroIndex = 0;
                mut [[[int32; 2]; 2]; 2] values = [
                    [[0, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                mut int32 output = 0;
                unsafe {
                    var callback = &Observe;
                    callback(&output);
                }
                return output;
            }
            """), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyDraftAndGetExecutionTables(
                TrRmArtifactIndex
                    .ForForeignInvocationStorageFixedArrayCopyDraft(artifact));
        Assert.That(tables.FixedArrayLayouts.Count, Is.EqualTo(3));

        var mainRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetName(row) == "Main")
            {
                mainRow = row;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        tables.ValidateRegisterBodyMapsForMethod(mainRow);
        var body = tables.Bodies.GetRegisterBody(
            tables.Methods.GetBodyRow(mainRow), tables.Contracts.Count);
        AssertHomeFirstUseTypeMap(body);

        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity, artifact),
            moduleIdentity, []);
        var main = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var initializations
            = new List<BoundNativeFixedArrayInitialization>();
        Assert.That(BoundTreeTraversal.VisitExpressions(main.Body,
            expression =>
            {
                if (expression is BoundNativeFixedArrayInitialization init)
                {
                    initializations.Add(init);
                }
            }), Is.True);
        Assert.That(initializations, Has.Count.EqualTo(7));
        Assert.That(initializations.All(static init =>
            init.Layout.ElementCount == 2
                && (ulong)init.Elements.Length == init.Layout.ElementCount),
            Is.True);
        Assert.That(initializations.Count(static init =>
            NativeFixedArrayTypes.IsType(init.Layout.ElementType)),
            Is.EqualTo(3));
        Assert.That(initializations.Count(static init =>
            init.Layout.ElementType == PrimitiveType.Int32),
            Is.EqualTo(4));
        int GetArrayTypeDepth(PrimitiveType type)
        {
            var depth = 0;
            while (NativeFixedArrayTypes.IsType(type))
            {
                depth++;
                type = loaded.NativeFixedArrayTypes[
                    NativeFixedArrayTypes.Index(type)].ElementType;
            }

            return depth;
        }
        var outerInitialization = initializations.Single(init =>
            GetArrayTypeDepth(init.Type) == 3);
        var sourceOrderedValues = new List<uint>();
        void CollectNestedValues(
            BoundNativeFixedArrayInitialization initialization)
        {
            foreach (var element in initialization.Elements)
            {
                if (element
                    is BoundNativeFixedArrayInitialization nested)
                {
                    CollectNestedValues(nested);
                }
                else
                {
                    Assert.That(element, Is.InstanceOf<BoundConstant>());
                    sourceOrderedValues.Add(unchecked((uint)
                        ((BoundConstant)element).Bits.Low));
                }
            }
        }
        CollectNestedValues(outerInitialization);
        Assert.That(sourceOrderedValues, Is.EqualTo(new uint[] {
            0, uint.MaxValue, uint.MaxValue, 0,
            uint.MaxValue, 0, 0, uint.MaxValue,
        }));
        Assert.That(TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1), Is.EqualTo(artifact));
    }

    [TestCase(0, 0, 0, 0)]
    [TestCase(0, 0, 1, -1)]
    [TestCase(0, 1, 0, -1)]
    [TestCase(0, 1, 1, 0)]
    [TestCase(1, 0, 0, -1)]
    [TestCase(1, 0, 1, 0)]
    [TestCase(1, 1, 0, 0)]
    [TestCase(1, 1, 1, -1)]
    public void StandardA2NestedArrayJitReadsInitializedRows(
        int outerIndex, int middleIndex, int innerIndex, int expected)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var moduleIdentity
            = $"a2-nested-array-read-{outerIndex}-{middleIndex}-{innerIndex}";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            outerIndex, middleIndex, innerIndex, out _);
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);

        var result = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2 nested source-free JIT row read: module={moduleIdentity}; expected={expected}; actual={result}; policy=FailFast.");
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(0, 0, 0, 42)]
    [TestCase(0, 0, 1, -1)]
    [TestCase(0, 1, 0, -1)]
    [TestCase(0, 1, 1, 0)]
    [TestCase(1, 0, 0, -1)]
    [TestCase(1, 0, 1, 0)]
    [TestCase(1, 1, 0, 0)]
    [TestCase(1, 1, 1, -1)]
    public void NestedArrayNativeParameterJitReadsInitializedRows(
        int outerIndex, int middleIndex, int innerIndex, int expected)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = CreateNativeNestedArrayReadbackSource(outerIndex,
            middleIndex, innerIndex);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var actual = NativeJit.Execute(compilation.Program!,
            NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"Nested NativeParameter source JIT: expected={expected}; actual={actual}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}.");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task NestedArrayNativeParameterAotReadsInitializedRow()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = CreateNativeNestedArrayReadbackSource(0, 0, 1);
        var compilation = Compilation.Create(SyntaxTree.Parse(source),
            new CompilationOptions {
                RuntimePolicy = BoundRuntimePolicy.FailFast,
                NativeCoreRegistration
                    = NativeCoreRegistrationTests.CreateRegistration(),
            });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var image = NativeEmitter.EmitWithNativeImports(
            compilation.Program!,
            NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-nested-native-parameter-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The nested NativeParameter executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"The nested NativeParameter executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"Nested NativeParameter source AOT: pid={processId}; expected=-1; actual={process.ExitCode}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(-1));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    [TestCase(2, 1)]
    [TestCase(3, 2)]
    public async Task PrivateRevision101AddressOnlyBackingRoundTripsAndExecutes(
        int dimensionCount, int ancestorCount)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var source = CreatePrivateNestedAddressSource(dimensionCount);
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));

        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            producer.AsSpan(sizeof(int), sizeof(int))), Is.EqualTo(101));
        var addressOffset = FindPrivateStorageAddressWithAncestorCount(
            producer, ancestorCount);
        var excessiveAncestorCount = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            excessiveAncestorCount.AsSpan(addressOffset + 50,
                sizeof(int)), 128);
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(excessiveAncestorCount));
        var mismatchedParent = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            mismatchedParent.AsSpan(addressOffset + 54, sizeof(int)),
            (int)PrimitiveType.Int32);
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(mismatchedParent));
        var truncatedPath = producer
            .AsSpan(0, addressOffset + 56).ToArray();
        Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(truncatedPath));
        var initializerOffset
            = FindPrivateNestedArrayInitializerOffset(producer);
        var shortInitializer = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            shortInitializer.AsSpan(initializerOffset + 13, sizeof(int)), 1);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(shortInitializer));
        var mismatchedChild = producer.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            mismatchedChild.AsSpan(initializerOffset + 18, sizeof(int)),
            (int)PrimitiveType.Int32);
        Assert.Throws<ModuleFormatException>(
            () => ModuleCompiler.CreateReference(mismatchedChild));

        var reference = ModuleCompiler.CreateReference(producer);
        var consumerSource = CreatePrivateNestedAddressSource(
            dimensionCount, "ConsumerBacking", "RtlMoveMemoryConsumer",
            "RtlMoveMemory")
            + Environment.NewLine
            + "int32 Main() { return Exercise() + ConsumerBacking(); }";
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), reference,
            BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));

        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);
        var reemittedProducer = ModuleCompiler.CreateProducer(
            linked with { EntryPoint = -1 }, ["Exercise"]);
        Assert.That(BinaryPrimitives.ReadInt32LittleEndian(
            reemittedProducer.AsSpan(sizeof(int), sizeof(int))),
            Is.EqualTo(101));
        _ = FindPrivateStorageAddressWithAncestorCount(reemittedProducer,
            ancestorCount);

        var reemittedReference
            = ModuleCompiler.CreateReference(reemittedProducer);
        var reemittedConsumerCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource), reemittedReference,
            BoundRuntimePolicy.FailFast);
        Assert.That(reemittedConsumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                reemittedConsumerCompilation.Diagnostics));
        var reemittedConsumer = ModuleCompiler.CreateConsumer(
            reemittedConsumerCompilation.Program!, reemittedProducer);
        var reemittedLinked = ModuleCompiler.Link(reemittedConsumer,
            reemittedProducer);

        Assert.That(NativeJit.Execute(linked,
            NativeRuntimePolicy.FailFast), Is.EqualTo(156));
        Assert.That(NativeJit.Execute(reemittedLinked,
            NativeRuntimePolicy.FailFast), Is.EqualTo(156));
        await AssertPrivateNestedBackingAotAsync(linked,
            $"private-nested-address-{dimensionCount}-first", 156)
            .ConfigureAwait(true);
        await AssertPrivateNestedBackingAotAsync(reemittedLinked,
            $"private-nested-address-{dimensionCount}-reemitted", 156)
            .ConfigureAwait(true);
    }

    private static string CreatePrivateNestedAddressSource(int dimensionCount)
        => CreatePrivateNestedAddressSource(dimensionCount, "Exercise",
            "RtlMoveMemory");

    private static string CreatePrivateNestedAddressSource(
        int dimensionCount, string functionName, string importName,
        string importExportName = "RtlMoveMemory")
    {
        var nextValue = 1;
        var arrayType = "int32";
        for (var dimension = 0; dimension < dimensionCount; dimension++)
        {
            arrayType = $"[{arrayType}; 2]";
        }

        var initializer
            = CreatePrivateNestedArrayInitializer(dimensionCount,
                ref nextValue);
        var selectedPath = string.Concat(
            Enumerable.Repeat("[selectedIndex]", dimensionCount));
        var untouchedPath = string.Concat(
            Enumerable.Repeat("[zeroIndex]", dimensionCount));
        return $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{importExportName}}")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void {{importName}}(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 {{functionName}}() {
                mut {{arrayType}} values = {{initializer}};
                int32 selectedIndex = 1;
                int32 zeroIndex = 0;
                mut int32 replacement = 77;
                mut int32 observed = 0;
                mut int32 untouched = 0;
                uint64 byteCount = 4;
                unsafe {
                    {{importName}}(&values{{selectedPath}},
                        &replacement, byteCount);
                    {{importName}}(&observed,
                        &values{{selectedPath}}, byteCount);
                    {{importName}}(&untouched,
                        &values{{untouchedPath}}, byteCount);
                }
                return observed + untouched;
            }
            """;
    }

    private static string CreatePrivateNestedArrayInitializer(
        int dimensionCount, ref int nextValue)
    {
        if (dimensionCount == 0)
        {
            return (nextValue++).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        var first = CreatePrivateNestedArrayInitializer(
            dimensionCount - 1, ref nextValue);
        var second = CreatePrivateNestedArrayInitializer(
            dimensionCount - 1, ref nextValue);
        return $"[{first}, {second}]";
    }

    private static int FindPrivateStorageAddressWithAncestorCount(
        byte[] artifact, int ancestorCount)
    {
        for (var offset = sizeof(int) * 2;
            offset <= artifact.Length - 54; offset++)
        {
            if (artifact[offset] == 35
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 1, sizeof(int)))
                    == (int)PrimitiveType.NativeStorageAddress
                && BinaryPrimitives.ReadUInt64LittleEndian(
                    artifact.AsSpan(offset + 25, sizeof(ulong))) == 2
                && BinaryPrimitives.ReadUInt64LittleEndian(
                    artifact.AsSpan(offset + 33, sizeof(ulong)))
                    == sizeof(int)
                && BinaryPrimitives.ReadUInt64LittleEndian(
                    artifact.AsSpan(offset + 41, sizeof(ulong)))
                    == sizeof(int)
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 50, sizeof(int)))
                    == ancestorCount)
            {
                return offset;
            }
        }

        Assert.Fail(
            $"The private storage-address path with {ancestorCount} ancestors was not encoded.");
        return -1;
    }

    private static int FindPrivateNestedArrayInitializerOffset(
        byte[] artifact)
    {
        for (var offset = sizeof(int) * 2;
            offset <= artifact.Length - 18; offset++)
        {
            if (artifact[offset] == 43
                && NativeFixedArrayTypes.IsType(
                    (PrimitiveType)BinaryPrimitives.ReadInt32LittleEndian(
                        artifact.AsSpan(offset + 1, sizeof(int))))
                && BinaryPrimitives.ReadInt32LittleEndian(
                    artifact.AsSpan(offset + 13, sizeof(int))) == 2
                && artifact[offset + 17] == 43)
            {
                return offset;
            }
        }

        Assert.Fail("The nested fixed-array initializer was not encoded.");
        return -1;
    }

    private static async Task AssertPrivateNestedBackingAotAsync(
        BoundProgram program, string filePrefix, int expectedExitCode)
    {
        var image = NativeEmitter.EmitWithNativeImports(program,
            NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{filePrefix}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The private nested-address executable was not started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"The private nested-address executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            Assert.That(await stdout.ConfigureAwait(true), Is.Empty);
            Assert.That(await stderr.ConfigureAwait(true), Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(expectedExitCode));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunPrivateModuleJitWorkerAsync(byte[] consumer, byte[] producer)
    {
        var consumerPath = Path.Combine(Path.GetTempPath(),
            $"terrum-private-consumer-{Guid.NewGuid():N}.trm");
        var producerPath = Path.Combine(Path.GetTempPath(),
            $"terrum-private-producer-{Guid.NewGuid():N}.trm");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer)
                .ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer)
                .ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name
                ?? throw new InvalidOperationException(
                    "The test output has no configuration directory.");
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", ".."));
            var cliPath = Path.Combine(root, "artifacts", "bin", "sources",
                "terrumc", configuration, framework.Name, "terrumc.dll");
            if (!File.Exists(cliPath))
            {
                throw new FileNotFoundException(
                    "The Terrum CLI was not built.", cliPath);
            }

            var start = new ProcessStartInfo("dotnet") {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add(cliPath);
            start.ArgumentList.Add("run-module");
            start.ArgumentList.Add(consumerPath);
            start.ArgumentList.Add(producerPath);
            start.ArgumentList.Add("--runtime");
            start.ArgumentList.Add("fail-fast");
            process = Process.Start(start) ?? throw new InvalidOperationException(
                "The private module JIT worker was not started.");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"The private module JIT worker exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            return (process.ExitCode, await stdout.ConfigureAwait(true),
                await stderr.ConfigureAwait(true));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(consumerPath)
                .ConfigureAwait(true);
            await TestFileCleanup.DeleteWithRetryAsync(producerPath)
                .ConfigureAwait(true);
        }
    }

    [TestCase("return values[1][1];")]
    [TestCase("values[1][1] = 77; return values[1][1];")]
    public void PrivateRevision101StillRejectsNestedArrayValueOperations(
        string operations)
    {
        var source = $$"""
            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                {{operations}}
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                compilation.Diagnostics));

        Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.CreateProducer(compilation.Program!,
                ["Exercise"]));
    }

    [Test]
    public void PrivateRevision101DoesNotTreatWriteAsNestedBackingInitialization()
    {
        const string source = """
            [Terrum.Interop.NativeImport("example.dll", "WriteValue")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            internal extern void WriteValue(write ptr<int32> value);

            public void Exercise() {
                init mut [[int32; 2]; 2] values;
                unsafe { WriteValue(&values[1][1]); }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void MayEscapeNativeParameterIsRejectedAtSourceBoundary()
    {
        const string source = """
            [Terrum.Interop.NativeImport("example.dll", "RetainValue")]
            [Terrum.Interop.NativeParameter(1, Write, MayEscape, 1)]
            internal extern void RetainValue(write ptr<int32> value);

            public void Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                unsafe { RetainValue(&values[1][1]); }
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public void NestedNativeParameterExtentCannotCrossTheImmediateRow()
    {
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 2)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut int32 observed = 0;
                uint64 byteCount = 8;
                unsafe {
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Diagnostics.Any(static diagnostic =>
            diagnostic.Code == "TRM3030"), Is.True);
    }

    [Test]
    public async Task PrivateRevision101AddressOnlyBackingSurvivesNativePackageSet()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var arrayCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(CreatePrivateNestedAddressSource(3)),
            BoundRuntimePolicy.FailFast);
        Assert.That(arrayCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                arrayCompilation.Diagnostics));
        var arrayProvider = NativeModuleCompiler.CreateProducer(
            arrayCompilation.Program!, ["Exercise"], "NestedAddressArray",
            NativeRuntimePolicy.FailFast);

        var biasCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Bias() { return 1; }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(biasCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                biasCompilation.Diagnostics));
        var biasProvider = NativeModuleCompiler.CreateProducer(
            biasCompilation.Program!, ["Bias"], "NestedAddressBias",
            NativeRuntimePolicy.FailFast);

        var providers = new[] { arrayProvider, biasProvider };
        var reference = NativeModuleCompiler.CreateReferenceSet(providers);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise() + Bias(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers,
            NativeRuntimePolicy.FailFast);

        var result = await NativeModuleCompiler.RunConsumerSetAsync(
            consumer, providers, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.That(result, Is.EqualTo(79));
    }

    [Test]
    public async Task PrivateRevision101AddressOnlyBackingSurvivesGraphPackage()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var biasCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public int32 Bias() { return 1; }
            """), BoundRuntimePolicy.FailFast);
        Assert.That(biasCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                biasCompilation.Diagnostics));
        var biasProvider = NativeModuleCompiler.CreateProducer(
            biasCompilation.Program!, ["Bias"], "GraphAddressBias",
            NativeRuntimePolicy.FailFast);

        var graphReference
            = NativeModuleCompiler.CreateGraphReference([biasProvider]);
        var graphProviderSource = CreatePrivateNestedAddressSource(3)
            .Replace("return observed + untouched;",
                "return observed + untouched + Bias();",
                StringComparison.Ordinal);
        var graphProviderCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(graphProviderSource), graphReference,
            BoundRuntimePolicy.FailFast);
        Assert.That(graphProviderCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                graphProviderCompilation.Diagnostics));
        var graphProvider = NativeModuleCompiler.CreateGraphProducer(
            graphProviderCompilation.Program!, ["Exercise"],
            "GraphAddressArray", [biasProvider],
            NativeRuntimePolicy.FailFast);

        var dependencies = new[] { graphProvider, biasProvider };
        var rootReference
            = NativeModuleCompiler.CreateGraphReference(dependencies);
        var rootCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise() + Bias(); }"),
            rootReference, BoundRuntimePolicy.FailFast);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                rootCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            rootCompilation.Program!, dependencies,
            NativeRuntimePolicy.FailFast);
        NativeModuleCompiler.ValidateGraph(graphConsumer, dependencies);

        var result = await NativeModuleCompiler.RunGraphAsync(graphConsumer,
            dependencies, TimeSpan.FromSeconds(10),
            NativeRuntimePolicy.FailFast).ConfigureAwait(true);
        Assert.That(result, Is.EqualTo(80));
    }

    [Test]
    public async Task PrivateRevision101NestedConstructionFailurePreventsAddressUse()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Denominator() { return 0; }

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [
                    [1, 2],
                    [3, 1 / Denominator()]];
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        const int expectedStatus = unchecked((int)0xE0540002);
        var jitResult = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(jitResult.ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(jitResult.Output, Is.Empty);
        Assert.That(jitResult.Error, Is.Empty);
        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-address-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision101NestedBoundsFailurePreventsNativeCall()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                int32 outerIndex = 2;
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values[outerIndex][0], byteCount);
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var expectedStatus = unchecked((int)0xE0540004);
        var jitResult = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(jitResult.ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(jitResult.Output, Is.Empty);
        Assert.That(jitResult.Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-bounds-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision101NestedSuffixFailurePreventsNativeCall()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 2)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 2)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut [int32; 2] copied = [0, 0];
                int32 selectedIndex = 1;
                uint64 byteCount = 8;
                unsafe {
                    RtlMoveMemory(&copied[0],
                        &values[0][selectedIndex], byteCount);
                }
                return copied[0];
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        const int expectedStatus = unchecked((int)0xE0540004);
        var jitResult = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(jitResult.ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(jitResult.Output, Is.Empty);
        Assert.That(jitResult.Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-suffix-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision101EffectfulNestedIndicesExecuteOnceInPathOrder()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[[int32; 2]; 2]; 2] values = [
                    [[1, 2], [3, 4]],
                    [[5, 6], [7, 8]]];
                mut int32 index = 0;
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values[index++][index++][0], byteCount);
                }
                return observed + index * 100;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        _ = FindPrivateStorageAddressWithAncestorCount(producer, 2);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var jitResult = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(jitResult.ExitCode, Is.EqualTo(203));
        Assert.That(jitResult.Output, Is.Empty);
        Assert.That(jitResult.Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-index-order", 203).ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision101ReadOnlyNestedBackingExecutes()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut int32 observed = 0;
                uint64 byteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed, &values[1][1], byteCount);
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        _ = FindPrivateStorageAddressWithAncestorCount(producer, 1);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        var jitResult = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(jitResult.ExitCode, Is.EqualTo(4));
        Assert.That(jitResult.Output, Is.Empty);
        Assert.That(jitResult.Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-readonly-backing", 4)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task PrivateRevision101LaterArgumentFailureSuppressesNativeCall()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string source = """
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory")]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            public int32 Exercise() {
                mut [[int32; 2]; 2] values = [[1, 2], [3, 4]];
                mut int32 observed = 0;
                unsafe {
                    RtlMoveMemory(&values[1][1], &observed,
                        uint64(1 / 0));
                }
                return observed;
            }
            """;
        var libraryCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(libraryCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                libraryCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            libraryCompilation.Program!, ["Exercise"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return Exercise(); }"),
            reference, BoundRuntimePolicy.FailFast);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, producer);
        const int expectedStatus = unchecked((int)0xE0540002);
        var jitResult = await RunPrivateModuleJitWorkerAsync(consumer,
            producer).ConfigureAwait(true);
        Assert.That(jitResult.ExitCode, Is.EqualTo(expectedStatus));
        Assert.That(jitResult.Output, Is.Empty);
        Assert.That(jitResult.Error, Is.Empty);

        var linked = ModuleCompiler.Link(consumer, producer);
        await AssertPrivateNestedBackingAotAsync(linked,
            "private-nested-later-argument-failure", expectedStatus)
            .ConfigureAwait(true);
    }

    [Test]
    public async Task StandardA2NestedArraySourceFreeJitAndAotExecute()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        const string moduleIdentity = "a2-nested-array-runtime";
        var input = CreateA2NestedArrayReadbackInput(moduleIdentity,
            0, 0, 1, out var source);
        var artifact = input.Artifact.ToArray();
        var loaded = TrRmExecutableCompiler.LoadStandard(input,
            moduleIdentity, []);
        var reemitted = TrRmExecutableCompiler.EmitStandard(loaded,
            moduleIdentity, schemaIdentity, 1);
        Assert.That(reemitted, Is.EqualTo(artifact));

        var compilerPath = typeof(TrRmExecutableCompiler).Assembly.Location;
        var importPath = Path.Combine(Environment.SystemDirectory,
            "kernel32.dll");
        TestContext.WriteLine(
            $"A2 nested source-free runtime input: schema={schemaIdentity}; module={moduleIdentity}; source-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))}; artifact-sha256={Convert.ToHexString(SHA256.HashData(artifact))}; compiler={compilerPath}; compiler-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(compilerPath)))}; import={importPath}!RtlMoveMemory; import-sha256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath)))}.");
        var jitResult = NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine(
            $"A2 nested source-free JIT leaf read: policy=FailFast; expected=-1; actual={jitResult}.");
        Assert.That(jitResult, Is.EqualTo(-1));

        var image = NativeModuleCompiler.EmitStandardExecutable(input,
            moduleIdentity, [], NativeRuntimePolicy.FailFast);
        Assert.That(image, Is.Not.Empty);
        var imagePath = Path.Combine(Path.GetTempPath(),
            $"terrum-{moduleIdentity}-{Guid.NewGuid():N}.exe");
        Process? process = null;
        try
        {
            await File.WriteAllBytesAsync(imagePath, image)
                .ConfigureAwait(true);
            process = Process.Start(new ProcessStartInfo(imagePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "The A2 nested-array executable was not started.");
            var processId = process.Id;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10))
                    .ConfigureAwait(true);
            }
            catch (TimeoutException exception)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                throw new TimeoutException(
                    $"A2 nested-array executable exceeded its 10-second deadline. {await stdout.ConfigureAwait(true)}{Environment.NewLine}{await stderr.ConfigureAwait(true)}",
                    exception);
            }

            var output = await stdout.ConfigureAwait(true);
            var error = await stderr.ConfigureAwait(true);
            TestContext.WriteLine(
                $"A2 nested source-free AOT leaf read: policy=FailFast; pid={processId}; expected=-1; actual={process.ExitCode}; image-sha256={Convert.ToHexString(SHA256.HashData(image))}; deadline=10s.");
            Assert.That(output, Is.Empty);
            Assert.That(error, Is.Empty);
            Assert.That(process.ExitCode, Is.EqualTo(-1));
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }

                process.Dispose();
            }

            await TestFileCleanup.DeleteWithRetryAsync(imagePath)
                .ConfigureAwait(true);
        }
    }

    private static TrRmStandardArtifactInput
        CreateA2NestedArrayReadbackInput(string moduleIdentity,
            int outerIndex, int middleIndex, int innerIndex, out string source)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-storage-fixed-arrays-2-empty-unit-initialization-1";
        var options = new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
            NativeCoreRegistration
                = NativeCoreRegistrationTests.CreateRegistration(),
        };
        var outerIndexExpression = outerIndex == 0
            ? "0" : "minusOne / minusOne";
        var middleIndexExpression = middleIndex == 0
            ? "0" : "minusOne / minusOne";
        var innerIndexExpression = innerIndex == 0
            ? "0" : "minusOne / minusOne";
        source = $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                int32 zeroIndex = 0;
                int32 minusOne = -1;
                mut [[[int32; 2]; 2]; 2] values = [
                    [[0, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                int32 outer = {{outerIndexExpression}};
                int32 middle = {{middleIndexExpression}};
                int32 inner = {{innerIndexExpression}};
                mut int32 observed = values[outer][middle][inner];
                uint64 copyByteCount = 0;
                unsafe {
                    var callback = &RtlMoveMemory;
                    callback(&observed,
                        &values[outer][middle][inner], copyByteCount);
                }
                return observed;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), options);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return new TrRmStandardArtifactInput(schemaIdentity, artifact);
    }

    private static string CreateNativeNestedArrayReadbackSource(
        int outerIndex, int middleIndex, int innerIndex)
    {
        return $$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "RtlMoveMemory",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            [Terrum.Interop.NativeParameter(1, Write, NoEscape, 1)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            internal extern void RtlMoveMemory(
                write ptr<int32> destination,
                ptr<int32> source,
                uint64 byteCount);

            int32 Main() {
                mut [[[int32; 2]; 2]; 2] values = [
                    [[42, -1], [-1, 0]],
                    [[-1, 0], [0, -1]]];
                mut int32 observed = 0;
                int32 outer = {{outerIndex}};
                int32 middle = {{middleIndex}};
                int32 inner = {{innerIndex}};
                uint64 copyByteCount = 4;
                unsafe {
                    RtlMoveMemory(&observed,
                        &values[outer][middle][inner], copyByteCount);
                }
                return observed;
            }
            """;
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
    public void StandardFProviderClosureValidatesMixedProviderDependencies()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerArtifact = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetTickCount", leafIdentity, "Leaf", core);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var emptyModuleInput = new TrRmStandardArtifactInput(schemaIdentity,
            CreateEmptyFModuleArtifact("EmptyF"));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [providerInput, leafInput, emptyModuleInput]);
        var imported = loaded.Functions.Single(static function =>
            function.Name == "Owned");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        Assert.That(imported.DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentProcessId"));

        var nonFirstRootInput = new TrRmStandardArtifactInput(schemaIdentity,
            AddLeadingEmptyModule(rootArtifact, "Main"));
        var nonFirstProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, AddLeadingEmptyModule(providerArtifact,
                "ProviderBody"));
        var nonFirstLoaded = TrRmExecutableCompiler.LoadStandard(
            nonFirstRootInput, rootIdentity,
            [nonFirstProviderInput, leafInput]);
        Assert.That(nonFirstLoaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var nonFirstProviderReference
            = TrRmExecutableCompiler.CreateStandardReference(
                nonFirstProviderInput, providerIdentity, [leafInput]);
        Assert.That(nonFirstProviderReference.Functions.Single(
            static function => function.Name == "Owned").Name,
            Is.EqualTo("Owned"));

        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [leafInput]);
        Assert.That(providerReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Owned").And.Not.Contain("Leaf"));

        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, [providerInput, leafInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (loweredProgram, nativeImports) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(nativeImports.Select(static binding =>
                binding.FunctionName),
            Is.EquivalentTo(["Owned"]));
        Assert.That(loweredProgram.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport, Is.Null);
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, [providerInput, leafInput],
            NativeRuntimePolicy.FailFast);
        Assert.That(image.Take(2), Is.EqualTo([(byte)'M', (byte)'Z']));

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity,
                [providerInput]));

        var incompatibleLeafCompilation
            = CreateMultipleImportProviderCompilation(
                "Leaf", "GetCurrentThreadId", core);
        var incompatibleLeafArtifact
            = TrRmExecutableCompiler.EmitStandard(
                incompatibleLeafCompilation.Program!, leafIdentity,
                schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleLeafArtifact),
            ]));

        var disconnectedArtifact = CreateMixedImportProviderArtifact(
            "closure-disconnected", "DisconnectedOwned",
            "GetCurrentThreadId", "Leaf",
            "GetTickCount", leafIdentity, "GetTickCount", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                leafInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    disconnectedArtifact),
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                leafInput,
                emptyModuleInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    emptyModuleInput.Artifact),
            ]));
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                leafInput,
                rootInput,
            ]));
    }

    [Test]
    public void StandardFProviderClosureChecksDeepNativeParameterExtent()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Transform";
        const string leafSource = """
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
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport(
                    "example.dll",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4294967295)]
                internal extern uint32 Transform(ptr<uint32> value);

                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetCurrentProcessId",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                public extern int32 Owned();

                internal int32 ProviderBody() {
                    unsafe {
                        var callback = &Owned;
                        return callback();
                    }
                }
                """), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        providerArtifact = AddRootMethodImports(providerArtifact, [
            ("Transform", leafIdentity, "Transform"),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
        var leafCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(leafSource), core);
        Assert.That(leafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, leafCompilation.Diagnostics));
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);

        var loadedProvider = TrRmExecutableCompiler.LoadStandard(providerInput,
            providerIdentity, [leafInput]);
        var imported = loadedProvider.Functions.Single(static function =>
            function.Name == "Transform");
        Assert.That(imported.ModuleImport!.ProducerIdentity,
            Is.EqualTo(leafIdentity));
        Assert.That(imported.DirectNativeImport!.Parameters.Single().Extent,
            Is.EqualTo(uint.MaxValue));
        var providerReference = TrRmExecutableCompiler.CreateStandardReference(
            providerInput, providerIdentity, [leafInput]);
        Assert.That(providerReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Owned").And.Not.Contain("Transform"));
        _ = TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
            providerInput,
            leafInput,
        ]);

        var incompatibleLeafCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse(leafSource.Replace("4294967295", "2147483648",
                StringComparison.Ordinal)), core);
        Assert.That(incompatibleLeafCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine,
                incompatibleLeafCompilation.Diagnostics));
        var incompatibleLeafArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleLeafCompilation.Program!, leafIdentity,
            schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleLeafArtifact),
            ]));
    }

    [Test]
    public void StandardFProviderClosureSupportsDiamondsAndChecksEveryView()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var (rootArtifact, _, _, _) =
            CreateMultipleRootMethodImportArtifacts();
        var firstProviderArtifact = CreateMixedImportProviderArtifact(
            "First", "First", "GetCurrentProcessId", "Leaf",
            "GetTickCount", leafIdentity, "Leaf", core);
        var secondProviderArtifact = CreateMixedImportProviderArtifact(
            "Second", "Second", "GetCurrentThreadId", "Leaf",
            "GetTickCount", leafIdentity, "Leaf", core);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity, leafArtifact),
        };

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "First").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Second").ModuleImport!.ProducerIdentity,
            Is.EqualTo("Second"));
        var reorderedLoaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers.Reverse().ToArray());
        Assert.That(reorderedLoaded.Functions.Single(static function =>
            function.Name == "First").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EquivalentTo(["First", "Second"]));

        var incompatibleSecondArtifact = CreateMixedImportProviderArtifact(
            "Second", "Second", "GetCurrentThreadId", "Leaf",
            "GetCurrentProcessId", leafIdentity, "Leaf", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providers[0],
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleSecondArtifact),
                providers[2],
            ]));
    }

    [Test]
    public void StandardFProviderClosureValidatesChainsLongerThanTwoEdges()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string secondIdentity = "Leaf";
        const string thirdIdentity = "Deep";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerArtifact = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetTickCount", secondIdentity, "Leaf", core);
        var secondArtifact = CreateMixedImportProviderArtifact(secondIdentity,
            "Leaf", "GetTickCount", "Deep", "GetCurrentThreadId",
            thirdIdentity, "Deep", core);
        var thirdCompilation = CreateMultipleImportProviderCompilation(
            "Deep", "GetCurrentThreadId", core);
        var thirdArtifact = TrRmExecutableCompiler.EmitStandard(
            thirdCompilation.Program!, thirdIdentity, schemaIdentity, 1);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var secondInput = new TrRmStandardArtifactInput(schemaIdentity,
            secondArtifact);
        var thirdInput = new TrRmStandardArtifactInput(schemaIdentity,
            thirdArtifact);
        var compatibleThirdCompilation
            = CreateMultipleImportProviderCompilation(
                "Deep", "GetCurrentThreadId", core,
                providerBodyName: "ReplacementBody");
        var compatibleThirdArtifact = TrRmExecutableCompiler.EmitStandard(
            compatibleThirdCompilation.Program!, thirdIdentity,
            schemaIdentity, 1);
        Assert.That(compatibleThirdArtifact, Is.Not.EqualTo(thirdArtifact));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [providerInput, secondInput, thirdInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity,
            [providerInput, secondInput, thirdInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(1));
        var compatibleProviderReplacement
            = CreateMixedImportProviderArtifact(providerIdentity, "Owned",
                "GetCurrentProcessId", "Leaf", "GetTickCount",
                secondIdentity, "Leaf", core,
                callerTargetMethodName: "Leaf");
        Assert.That(compatibleProviderReplacement,
            Is.Not.EqualTo(providerArtifact));
        _ = TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
            new TrRmStandardArtifactInput(schemaIdentity,
                compatibleProviderReplacement),
            secondInput,
            thirdInput,
        ]);
        var compatibleReplacement = TrRmExecutableCompiler.LoadStandard(
            rootInput, rootIdentity, [
                providerInput,
                secondInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    compatibleThirdArtifact),
            ]);
        Assert.That(compatibleReplacement.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));

        var incompatibleThird = CreateMultipleImportProviderCompilation(
            "Deep", "GetTickCount", core);
        var incompatibleThirdArtifact = TrRmExecutableCompiler.EmitStandard(
            incompatibleThird.Program!, thirdIdentity, schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity, [
                providerInput,
                secondInput,
                new TrRmStandardArtifactInput(schemaIdentity,
                    incompatibleThirdArtifact),
            ]));

        var coLocatedProviderArtifact
            = CombinePopulatedNativeAddressModules(providerArtifact,
                secondArtifact);
        var coLocatedProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, coLocatedProviderArtifact);
        var hybridLoaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [coLocatedProviderInput, thirdInput]);
        Assert.That(hybridLoaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(providerIdentity));
        var coLocatedRootProviderArtifact
            = CombinePopulatedNativeAddressModules(rootArtifact,
                providerArtifact);
        var coLocatedRootProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity, coLocatedRootProviderArtifact);
        var rootProviderLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedRootProviderInput, rootIdentity,
            [secondInput, thirdInput]);
        Assert.That(rootProviderLoaded.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));

        var coLocatedProviderChain = CombinePopulatedNativeAddressModules(
            CombinePopulatedNativeAddressModules(providerArtifact,
                secondArtifact),
            thirdArtifact);
        var fullyCoLocatedArtifact = CombinePopulatedNativeAddressModules(
            coLocatedProviderChain, rootArtifact);
        var fullyCoLocatedInput = new TrRmStandardArtifactInput(
            schemaIdentity, fullyCoLocatedArtifact);
        var fullyCoLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            fullyCoLocatedInput, rootIdentity, []);
        Assert.That(fullyCoLocatedLoaded.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));
        var fullyCoLocatedReference =
            TrRmExecutableCompiler.CreateStandardReference(
                fullyCoLocatedInput, providerIdentity, []);
        Assert.That(fullyCoLocatedReference.Functions.Select(
                static function => function.Name),
            Does.Contain("Owned").And.Not.Contain("Leaf"));
        var fullyCoLocatedPlan =
            TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                fullyCoLocatedInput, rootIdentity, [],
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast);
        Assert.That(fullyCoLocatedPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EqualTo(["Owned"]));

        var reorderedModuleArtifact =
            CombinePopulatedNativeAddressModules(
                CombinePopulatedNativeAddressModules(
                    CombinePopulatedNativeAddressModules(secondArtifact,
                        rootArtifact),
                    thirdArtifact),
                providerArtifact);
        var reorderedModuleInput = new TrRmStandardArtifactInput(
            schemaIdentity, reorderedModuleArtifact);
        var reorderedModuleLoaded = TrRmExecutableCompiler.LoadStandard(
            reorderedModuleInput, rootIdentity, []);
        Assert.That(reorderedModuleLoaded.Functions.Single(
            static function => function.Name == "Owned").ModuleImport!
            .ProducerIdentity, Is.EqualTo(providerIdentity));
        var reorderedModulePlan =
            TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                reorderedModuleInput, rootIdentity, [],
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast);
        Assert.That(reorderedModulePlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EqualTo(["Owned"]));
        var missingLeafReferenceArtifact = ReencodeModuleRefIdentity(
            reorderedModuleArtifact, 3, "MissingLeaf", encoding: 2);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    missingLeafReferenceArtifact), rootIdentity, []));
        var mismatchedImportKeyArtifact = ReplaceImportExportKey(
            reorderedModuleArtifact, 3, "Deep");
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    mismatchedImportKeyArtifact), rootIdentity, []));
        var foreignBodyMethodArtifact =
            ReplaceRootBodyMethodWithForeignModuleMethod(
                reorderedModuleArtifact, rootIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    foreignBodyMethodArtifact), rootIdentity, []));
        var foreignBodyTypeArtifact =
            ReplaceRootBodyFunctionPointerTypeWithForeignModuleType(
                reorderedModuleArtifact, rootIdentity);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    foreignBodyTypeArtifact), rootIdentity, []));
    }

    [Test]
    public void StandardFProviderClosureSupportsMixedMultipleImports()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "Provider";
        const string leafIdentity = "LeafA";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetCurrentProcessId",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                public extern int32 Owned();

                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetTickCount",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern int32 LeafA();

                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetTickCount",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern int32 LeafB();

                internal int32 ProviderBody() {
                    unsafe {
                        var callback = &Owned;
                        return callback();
                    }
                }
                """), core);
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        providerArtifact = AddRootMethodImports(providerArtifact, [
            ("LeafA", leafIdentity, "GetTickCount"),
            ("LeafB", leafIdentity, "GetTickCount"),
        ], reuseModuleRefs: true, duplicateSecondMethodName: false);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "GetTickCount", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            providerArtifact);
        var leafInput = new TrRmStandardArtifactInput(schemaIdentity,
            leafArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(providerArtifact));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(1));

        var loaded = TrRmExecutableCompiler.LoadStandard(providerInput,
            providerIdentity, [leafInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "LeafA").ModuleImport!.ProducerIdentity,
            Is.EqualTo(leafIdentity));
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "LeafB").ModuleImport!.ExportKey,
            Is.EqualTo("GetTickCount"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            providerInput, providerIdentity, [leafInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings.Select(
                static binding => binding.FunctionName),
            Is.EquivalentTo(["Owned", "LeafA", "LeafB"]));
    }

    [Test]
    public void StandardFProviderClosureRejectsDuplicateMixedMethodNames()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string providerIdentity = "Provider";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var providerArtifact = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetTickCount", leafIdentity, "Leaf", core);
        var duplicateNameArtifact = RenameMethodDef(providerArtifact,
            "Leaf", "Owned");
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetTickCount", core);
        var leafArtifact = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    duplicateNameArtifact), providerIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        leafArtifact),
                ]));
    }

    [Test]
    public void StandardFProviderClosureSupportsFanInBeyondSixteenModules()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "fan-in-root";
        const int providerCount = 17;
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var declarations = string.Join(Environment.NewLine,
            Enumerable.Range(0, providerCount).Select(index => $$"""
                [Terrum.Interop.NativeImport(
                    "kernel32.dll",
                    "GetCurrentProcessId",
                    CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
                internal extern int32 Provider{{index:D2}}();
                """));
        var rootCompilation = Compilation.Create(SyntaxTree.Parse($$"""
            {{declarations}}

            int32 Main() {
                unsafe {
                    var callback = &Provider00;
                    return callback();
                }
            }
            """), core);
        Assert.That(rootCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, rootCompilation.Diagnostics));
        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            rootCompilation.Program!, rootIdentity, schemaIdentity, 1);
        var bindings = new List<(string MethodName, string ProducerIdentity,
            string ExportKey)>(providerCount);
        var providers = new TrRmStandardArtifactInput[providerCount];
        for (var index = 0; index < providerCount; index++)
        {
            var identity = $"Provider{index:D2}";
            bindings.Add((identity, identity, identity));
            var compilation = CreateMultipleImportProviderCompilation(
                identity, "GetCurrentProcessId", core);
            var providerArtifact = TrRmExecutableCompiler.EmitStandard(
                compilation.Program!, identity, schemaIdentity, 1);
            WriteStandardFArtifactEvidence("fan-in-provider", identity,
                providerArtifact);
            providers[index] = new TrRmStandardArtifactInput(schemaIdentity,
                providerArtifact);
        }

        rootArtifact = AddRootMethodImports(rootArtifact, bindings,
            reuseModuleRefs: false, duplicateSecondMethodName: false);
        WriteStandardFArtifactEvidence("fan-in-root", rootIdentity,
            rootArtifact);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        Assert.That(loaded.Functions.Count(static function =>
            function.ModuleImport is not null), Is.EqualTo(providerCount));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(providerCount));
        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        Assert.That(image.Take(2), Is.EqualTo([(byte)'M', (byte)'Z']));
        var imports = ReadPeImports(image,
            NativeEmitter.System32ImportPath("kernel32.dll"));
        TestContext.WriteLine(
            $"Fan-in PE imports: {string.Join(",", imports)}; image-bytes={image.Length}; sha256={Convert.ToHexString(SHA256.HashData(image))}");
        Assert.That(imports, Does.Contain("GetCurrentProcessId"));
    }

    [Test]
    public void StandardFProviderPlanRetainsOwnedArtifactSnapshots()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        const string leafIdentity = "Leaf";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootBytes = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "Owned", core);
        var providerBytes = CreateMixedImportProviderArtifact(
            providerIdentity, "Owned", "GetCurrentProcessId",
            "Leaf", "GetCurrentProcessId", leafIdentity, "Leaf", core);
        var leafCompilation = CreateMultipleImportProviderCompilation(
            "Leaf", "GetCurrentProcessId", core);
        var leafBytes = TrRmExecutableCompiler.EmitStandard(
            leafCompilation.Program!, leafIdentity, schemaIdentity, 1);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity, providerBytes),
            new TrRmStandardArtifactInput(schemaIdentity, leafBytes),
        };
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            new TrRmStandardArtifactInput(schemaIdentity, rootBytes),
            rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);

        Array.Fill(rootBytes, (byte)0xCC);
        Array.Fill(providerBytes, (byte)0xCC);
        Array.Fill(leafBytes, (byte)0xCC);
        var (loweredProgram, bindings) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(bindings.Select(static binding => binding.FunctionName),
            Is.EquivalentTo(["Owned"]));
        Assert.That(loweredProgram.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport, Is.Null);
    }

    [Test]
    public void StandardFProviderClosureUsesTheSelectedPublicExportKey()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "Root";
        const string providerIdentity = "Owned";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var rootArtifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", providerIdentity, "GetCurrentProcessId", core);
        rootArtifact = ReencodeFirstModuleRefIdentity(rootArtifact,
            providerIdentity, encoding: 2);
        var providerCompilation = CreateMultipleImportProviderCompilation(
            "Owned", "GetCurrentProcessId", core);
        var providerArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, providerIdentity, schemaIdentity, 1);
        var aliasProviderArtifact = AddMethodExportAlias(providerArtifact,
            "Owned", "GetCurrentProcessId");
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providerInput = new TrRmStandardArtifactInput(schemaIdentity,
            aliasProviderArtifact);
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [providerInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Owned").DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentProcessId"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, [providerInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(1));

        var caseMismatchRootArtifact = CreateRootMethodImportArtifact(
            "CaseRoot", "owned", "owned", "GetCurrentProcessId", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    caseMismatchRootArtifact), "CaseRoot", [providerInput]));

        var normalizationRootArtifact = CreateRootMethodImportArtifact(
            "NormalizationRoot", "Owned", providerIdentity, "Owned", core);
        normalizationRootArtifact = ReencodeFirstModuleRefIdentity(
            normalizationRootArtifact, "e\u0301", encoding: 2);
        var normalizedProviderCompilation
            = CreateMultipleImportProviderCompilation(
                "Owned", "GetCurrentProcessId", core);
        var normalizedProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            normalizedProviderCompilation.Program!, "\u00E9",
            schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    normalizationRootArtifact), "NormalizationRoot", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        normalizedProviderArtifact),
                ]));

        const string caseKeyProviderIdentity = "GetCurrentProcessId";
        var caseKeyProviderCompilation
            = CreateMultipleImportProviderCompilation(
                "Owned", "GetCurrentProcessId", core);
        var caseKeyProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            caseKeyProviderCompilation.Program!, caseKeyProviderIdentity,
            schemaIdentity, 1);
        var caseKeyProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            AddMethodExportAlias(caseKeyProviderArtifact, "Owned",
                "GetCurrentProcessId"));
        var caseMismatchKeyArtifact = CreateRootMethodImportArtifact(
            "CaseKeyRoot", "getcurrentprocessid",
            caseKeyProviderIdentity, "getcurrentprocessid", core);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    caseMismatchKeyArtifact), "CaseKeyRoot",
                [caseKeyProviderInput]));
    }

    [Test]
    public void StandardFProviderClosureUsesExactUnicodeModuleIdentity()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "SupplementaryRoot";
        const string supplementaryProviderIdentity = "Provider-\U0001F680";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var providerCompilation = CreateMultipleImportProviderCompilation(
            "Owned", "GetCurrentProcessId", core);
        var supplementaryProviderArtifact =
            TrRmExecutableCompiler.EmitStandard(
                providerCompilation.Program!, supplementaryProviderIdentity,
                schemaIdentity, 1);
        var supplementaryRootArtifact = CreateRootMethodImportArtifact(
            rootIdentity, "Owned", "Owned", "Owned", core);
        supplementaryRootArtifact = ReencodeFirstModuleRefIdentity(
            supplementaryRootArtifact, supplementaryProviderIdentity,
            encoding: 2);
        var loaded = TrRmExecutableCompiler.LoadStandard(
            new TrRmStandardArtifactInput(schemaIdentity,
                supplementaryRootArtifact), rootIdentity, [
                new TrRmStandardArtifactInput(schemaIdentity,
                    supplementaryProviderArtifact),
            ]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo(supplementaryProviderIdentity));

        const string ringSymbolIdentity = "\u212B";
        const string composedRingIdentity = "\u00C5";
        var ringRootArtifact = CreateRootMethodImportArtifact(
            "RingRoot", "Owned", "Owned", "Owned", core);
        ringRootArtifact = ReencodeFirstModuleRefIdentity(
            ringRootArtifact, ringSymbolIdentity, encoding: 2);
        var composedRingProviderArtifact = TrRmExecutableCompiler.EmitStandard(
            providerCompilation.Program!, composedRingIdentity,
            schemaIdentity, 1);
        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    ringRootArtifact), "RingRoot", [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        composedRingProviderArtifact),
                ]));
    }

    [TestCase(0x0001)]
    [TestCase(0x0010)]
    [TestCase(0x0011)]
    [TestCase(0x0016)]
    [TestCase(0x001C)]
    [TestCase(0x0020)]
    public void StandardFProviderClosureRejectsOverLimitSectionCounts(
        int sectionKind)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "CountLimitRoot";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var artifact = CreateRootMethodImportArtifact(rootIdentity,
            "Owned", "Owned", "Owned", core);
        var overLimitArtifact = ReplaceSectionRecordCount(artifact,
            sectionKind, 0x0100_0000);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity,
                    overLimitArtifact), rootIdentity, []));
    }

    [Test]
    public void StandardFProviderClosureSupportsSelfAndMutualDependencies()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var selfArtifact = CreateMixedImportProviderArtifact(
            "SelfDependency", "Owned", "GetCurrentProcessId",
            "SelfDependency", "GetCurrentProcessId", "SelfDependency",
            "Owned", core);
        WriteStandardFArtifactEvidence("self-edge", "SelfDependency",
            selfArtifact);
        var selfInput = new TrRmStandardArtifactInput(schemaIdentity,
            selfArtifact);
        var selfLoaded = TrRmExecutableCompiler.LoadStandard(selfInput,
            "SelfDependency", []);
        Assert.That(selfLoaded.Functions.Single(static function =>
            function.Name == "SelfDependency").ModuleImport!.ProducerIdentity,
            Is.EqualTo("SelfDependency"));
        var selfPlan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            selfInput, "SelfDependency", [],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (_, selfImports) = selfPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(selfImports.Select(static binding => binding.FunctionName),
            Is.EquivalentTo(["Owned", "SelfDependency"]));
        selfPlan.ValidateLibraryExports(["Owned"]);
        _ = Assert.Throws<ArgumentException>(() =>
            selfPlan.ValidateLibraryExports(["SelfDependency"]));

        var coLocatedProviderArtifact
            = CreateMixedImportProviderArtifact("Owned", "Owned",
                "GetCurrentProcessId", "SelfDependency",
                "GetCurrentProcessId", "Owned", "Owned", core);
        var coLocatedRootArtifact = CreateRootMethodImportArtifact(
            "SelfRoot", "Owned", "Owned", "Owned", core);
        var coLocatedArtifact = CombinePopulatedNativeAddressModules(
            coLocatedProviderArtifact, coLocatedRootArtifact);
        var coLocatedInput = new TrRmStandardArtifactInput(schemaIdentity,
            coLocatedArtifact);
        var coLocatedLoaded = TrRmExecutableCompiler.LoadStandard(
            coLocatedInput, "SelfRoot", []);
        Assert.That(coLocatedLoaded.Functions.Single(static function =>
            function.Name == "Owned").ModuleImport!.ProducerIdentity,
            Is.EqualTo("Owned"));
        var coLocatedPlan
            = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
                coLocatedInput, "SelfRoot", [],
                TrRmStandardNativeOperation.EmitExecutable,
                NativeRuntimePolicy.FailFast);
        Assert.That(coLocatedPlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(1));

        var pArtifact = CreateMixedImportProviderArtifact("CycleP",
            "CycleP", "GetCurrentProcessId", "CycleQ",
            "GetCurrentProcessId", "CycleQ", "CycleQ", core);
        var qArtifact = CreateMixedImportProviderArtifact("CycleQ",
            "CycleQ", "GetCurrentProcessId", "CycleP",
            "GetCurrentProcessId", "CycleP", "CycleP", core);
        var pInput = new TrRmStandardArtifactInput(schemaIdentity, pArtifact);
        var qInput = new TrRmStandardArtifactInput(schemaIdentity, qArtifact);
        var pLoaded = TrRmExecutableCompiler.LoadStandard(pInput,
            "CycleP", [qInput]);
        Assert.That(pLoaded.Functions.Single(static function =>
            function.Name == "CycleQ").ModuleImport!.ProducerIdentity,
            Is.EqualTo("CycleQ"));
        var cyclePlan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            pInput, "CycleP", [qInput],
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        Assert.That(cyclePlan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable).Bindings,
            Has.Length.EqualTo(2));
    }

    [Test]
    public async Task StandardFSelfProviderExportThunkRemainsInvocableAsync()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string moduleIdentity = "SelfDependency";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var artifact = CreateMixedImportProviderArtifact(moduleIdentity,
            "Owned", "GetCurrentProcessId", "SelfDependency",
            "GetCurrentProcessId", moduleIdentity, "Owned", core);
        var input = new TrRmStandardArtifactInput(schemaIdentity, artifact);
        var image = NativeModuleCompiler.EmitStandardLibrary(input,
            moduleIdentity, [], ["Owned"], NativeRuntimePolicy.FailFast);
        var exports = ReadPeExports(image);
        TestContext.WriteLine(
            $"Self-edge DLL exports: {string.Join(",", exports)}; image-bytes={image.Length}; sha256={Convert.ToHexString(SHA256.HashData(image))}");
        Assert.That(exports,
            Does.Contain("Owned").And.Not.Contain("SelfDependency"));
        _ = Assert.Throws<ArgumentException>(() =>
            NativeModuleCompiler.EmitStandardLibrary(input, moduleIdentity,
                [], ["SelfDependency"], NativeRuntimePolicy.FailFast));

        var path = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"trrm-f-self-export-{Guid.NewGuid():N}.dll");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            var handle = NativeLibrary.Load(path);
            try
            {
                var function = Marshal.GetDelegateForFunctionPointer<
                    NativeAddressExport>(NativeLibrary.GetExport(handle,
                    "Owned"));
                var result = function();
                TestContext.WriteLine(
                    $"Self-edge DLL Owned export result: {result}");
                Assert.That(result, Is.GreaterThan(0));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(path)
                .ConfigureAwait(true);
        }
    }

    [Test]
    public void StandardFMultipleImportsBindUncalledProviderBeforeLowering()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = CreateMultipleRootMethodImportArtifacts();
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var firstProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            firstProviderArtifact);
        var secondProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            secondProviderArtifact);
        var providers = new[] { firstProviderInput, secondProviderInput };

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        var first = loaded.Functions.Single(static function =>
            function.Name == "First");
        var second = loaded.Functions.Single(static function =>
            function.Name == "Second");
        Assert.That(first.ModuleImport!.ProducerIdentity, Is.EqualTo("First"));
        Assert.That(second.ModuleImport!.ProducerIdentity, Is.EqualTo("Second"));
        Assert.That(first.ModuleImport.ExportKey, Is.EqualTo("First"));
        Assert.That(second.ModuleImport.ExportKey, Is.EqualTo("Second"));
        Assert.That(first.DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentProcessId"));
        Assert.That(second.DirectNativeImport!.ExportName,
            Is.EqualTo("GetCurrentThreadId"));

        var caller = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeFunctionAddress address)
                {
                    addresses.Add(address);
                }
            }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(addresses[0].Function,
            Is.EqualTo(loaded.Functions.IndexOf(first)));

        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (loweredProgram, nativeImports) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(nativeImports.Select(static binding =>
                binding.FunctionName),
            Is.EquivalentTo(["First", "Second"]));
        Assert.That(loweredProgram.Functions.Where(static function =>
                function.Name is "First" or "Second")
            .All(static function => function.ModuleImport is null
                && !function.IsModuleReference), Is.True);

        _ = Assert.Throws<InvalidDataException>(() =>
            TrRmExecutableCompiler.LoadStandard(rootInput, rootIdentity,
                [firstProviderInput]));
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var incompatibleProvider = CreateMultipleImportProviderCompilation(
            "Second", "GetTickCount", core);
        var incompatibleProviderInput = new TrRmStandardArtifactInput(
            schemaIdentity,
            TrRmExecutableCompiler.EmitStandard(
                incompatibleProvider.Program!, "Second", schemaIdentity, 1));
        _ = Assert.Throws<InvalidDataException>(() =>
            NativeModuleCompiler.EmitStandardExecutable(rootInput,
                rootIdentity, [firstProviderInput, incompatibleProviderInput],
                NativeRuntimePolicy.FailFast));

        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        Assert.That(image.Length, Is.GreaterThan(2));
        Assert.That(image[0], Is.EqualTo((byte)'M'));
        Assert.That(image[1], Is.EqualTo((byte)'Z'));
    }

    [TestCase(true, 1)]
    [TestCase(false, 2)]
    public void StandardFMultipleImportsPermitModuleRefReuse(
        bool reuseModuleRef, int expectedModuleRefCount)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, _, _)
            = CreateMultipleRootMethodImportArtifacts(
                "GetCurrentProcessId", "First", "First", reuseModuleRef);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var firstProviderInput = new TrRmStandardArtifactInput(schemaIdentity,
            firstProviderArtifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(rootArtifact));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(expectedModuleRefCount));
        Assert.That(tables.Imports.Count, Is.EqualTo(2));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, [firstProviderInput]);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "First").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Second").ModuleImport!.ProducerIdentity,
            Is.EqualTo("First"));
    }

    [Test]
    public void StandardFMultipleImportsRejectSameRootMethodNames()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = CreateMultipleRootMethodImportArtifacts(
                duplicateSecondMethodName: true);
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.LoadStandard(
                new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
                rootIdentity, [
                    new TrRmStandardArtifactInput(schemaIdentity,
                        firstProviderArtifact),
                    new TrRmStandardArtifactInput(schemaIdentity,
                        secondProviderArtifact),
                ]));
    }

    [Test]
    public void StandardFMultipleImportsSupportThreeProviderBindings()
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact,
            thirdProviderArtifact)
            = CreateMultipleRootMethodImportArtifacts(importCount: 3);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                thirdProviderArtifact),
        };
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(rootArtifact));
        Assert.That(tables.Imports.Count, Is.EqualTo(3));
        Assert.That(tables.ModuleRefs.Count, Is.EqualTo(3));

        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        Assert.That(loaded.Functions.Single(static function =>
            function.Name == "Second").ModuleImport!.ProducerIdentity,
            Is.EqualTo("Second"));
        var third = loaded.Functions.Single(static function =>
            function.Name == "Third");
        Assert.That(third.ModuleImport!.ProducerIdentity,
            Is.EqualTo("Third"));
        Assert.That(third.DirectNativeImport!.ExportName,
            Is.EqualTo("GetTickCount"));
        var plan = TrRmExecutableCompiler.LoadStandardNativeBindingPlan(
            rootInput, rootIdentity, providers,
            TrRmStandardNativeOperation.EmitExecutable,
            NativeRuntimePolicy.FailFast);
        var (_, nativeImports) = plan.CreateLoweringInputs(
            TrRmStandardNativeOperation.EmitExecutable);
        Assert.That(nativeImports.Select(static binding =>
                binding.FunctionName),
            Is.EquivalentTo(["First", "Second", "Third"]));
    }

    [Test]
    public void StandardFMultipleImportsSelectNonFirstAddressTarget()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact,
            thirdProviderArtifact) = CreateMultipleRootMethodImportArtifacts(
                importCount: 3, targetMethodName: "Third");
        WriteStandardFArtifactEvidence("non-first-target-root", rootIdentity,
            rootArtifact);
        WriteStandardFArtifactEvidence("non-first-target-first-provider",
            "First", firstProviderArtifact);
        WriteStandardFArtifactEvidence("non-first-target-second-provider",
            "Second", secondProviderArtifact);
        WriteStandardFArtifactEvidence("non-first-target-third-provider",
            "Third", thirdProviderArtifact);
        var rootInput = new TrRmStandardArtifactInput(schemaIdentity,
            rootArtifact);
        var providers = new[]
        {
            new TrRmStandardArtifactInput(schemaIdentity,
                firstProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                secondProviderArtifact),
            new TrRmStandardArtifactInput(schemaIdentity,
                thirdProviderArtifact),
        };
        var loaded = TrRmExecutableCompiler.LoadStandard(rootInput,
            rootIdentity, providers);
        var caller = loaded.Functions.Single(static function =>
            function.Name == "Main");
        var addresses = new List<BoundNativeFunctionAddress>();
        Assert.That(BoundTreeTraversal.VisitExpressions(caller.Body,
            expression =>
            {
                if (expression is BoundNativeFunctionAddress address)
                {
                    addresses.Add(address);
                }
            }), Is.True);
        Assert.That(addresses, Has.Count.EqualTo(1));
        Assert.That(loaded.Functions[addresses[0].Function].Name,
            Is.EqualTo("Third"));

        var image = NativeModuleCompiler.EmitStandardExecutable(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        Assert.That(ReadPeImports(image,
                NativeEmitter.System32ImportPath("kernel32.dll")),
            Does.Contain("GetTickCount"));
        var jitResult = NativeModuleCompiler.ExecuteStandard(rootInput,
            rootIdentity, providers, NativeRuntimePolicy.FailFast);
        TestContext.WriteLine($"Non-first NativeAddress JIT result: {jitResult}");
        Assert.That(jitResult, Is.GreaterThan(0));
    }

    [Test]
    public void StandardFMultipleImportJitExecutesSelectedProvider()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var (rootArtifact, firstProviderArtifact, secondProviderArtifact, _)
            = CreateMultipleRootMethodImportArtifacts();
        var result = NativeModuleCompiler.ExecuteStandard(
            new TrRmStandardArtifactInput(schemaIdentity, rootArtifact),
            rootIdentity, [
                new TrRmStandardArtifactInput(schemaIdentity,
                    firstProviderArtifact),
                new TrRmStandardArtifactInput(schemaIdentity,
                    secondProviderArtifact),
            ], NativeRuntimePolicy.FailFast);
        Assert.That(result, Is.GreaterThan(0));
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

    internal static (byte[] RootArtifact, byte[] FirstProviderArtifact,
        byte[] SecondProviderArtifact, byte[] ThirdProviderArtifact)
        CreateMultipleRootMethodImportArtifacts(
        string secondRootImportExport = "GetCurrentThreadId",
        string secondProducerIdentity = "Second",
        string secondExportKey = "Second", bool reuseModuleRef = false,
        bool duplicateSecondMethodName = false, int importCount = 2,
        string targetMethodName = "First")
    {
        if (importCount is < 2 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(importCount));
        }

        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        const string rootIdentity = "multi-import-root";
        var core = new CompilationReference([], [],
            BoundRuntimePolicy.FailFast,
            nativeCoreRegistration:
                NativeCoreRegistrationTests.CreateRegistration());
        var firstProvider = CreateMultipleImportProviderCompilation(
            "First", "GetCurrentProcessId", core);
        var secondProvider = CreateMultipleImportProviderCompilation(
            "Second", "GetCurrentThreadId", core);
        var thirdProvider = CreateMultipleImportProviderCompilation(
            "Third", "GetTickCount", core);
        var thirdDeclaration = importCount == 3
            ? """
              [Terrum.Interop.NativeImport(
                  "kernel32.dll",
                  "GetTickCount",
                  CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
              internal extern int32 Third();
              """
            : string.Empty;
        var root = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 First();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{secondRootImportExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 Second();

            {{thirdDeclaration}}

            int32 Main() {
                unsafe {
                    var callback = &{{targetMethodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(root.HasErrors, Is.False,
            string.Join(Environment.NewLine, root.Diagnostics));

        var rootArtifact = TrRmExecutableCompiler.EmitStandard(
            root.Program!, rootIdentity, schemaIdentity, 1);
        var importBindings = new List<(string MethodName,
            string ProducerIdentity, string ExportKey)>
        {
            ("First", "First", "First"),
            ("Second", secondProducerIdentity, secondExportKey),
        };
        if (importCount == 3)
        {
            importBindings.Add(("Third", "Third", "Third"));
        }

        rootArtifact = AddRootMethodImports(rootArtifact, importBindings,
            reuseModuleRef, duplicateSecondMethodName);

        return (rootArtifact,
            TrRmExecutableCompiler.EmitStandard(firstProvider.Program!,
                "First", schemaIdentity, 1),
            TrRmExecutableCompiler.EmitStandard(secondProvider.Program!,
                "Second", schemaIdentity, 1),
            TrRmExecutableCompiler.EmitStandard(thirdProvider.Program!,
                "Third", schemaIdentity, 1));
    }

    internal static Compilation CreateMultipleImportProviderCompilation(
        string methodName, string nativeExport,
        CompilationReference core, string providerBodyName = "ProviderBody")
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{nativeExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 {{methodName}}();

            internal int32 {{providerBodyName}}() {
                unsafe {
                    var callback = &{{methodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation;
    }

    private static byte[] AddRootMethodImports(byte[] artifact,
        IReadOnlyList<(string MethodName, string ProducerIdentity,
            string ExportKey)> bindings, bool reuseModuleRefs,
        bool duplicateSecondMethodName)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.Count, Is.EqualTo(1));
        Assert.That(tables.ModuleRefs.Count, Is.Zero);
        Assert.That(tables.Imports.Count, Is.Zero);
        Assert.That(bindings, Is.Not.Empty);
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0016, out var methodSectionBytes,
            out var methodCount), Is.True);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);

        var moduleRefCount = reuseModuleRefs
            ? bindings.Select(static binding =>
                binding.ProducerIdentity).Distinct(StringComparer.Ordinal)
                .Count()
            : bindings.Count;
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            tables.Strings, moduleRefCount, tables.Modules.TypeDefCount,
            tables.Modules.MethodDefCount, tables.Modules.SourceFileCount,
            tables.Features.Count);
        var (targetRequirement, runtimePolicy)
            = tables.Modules.GetRequirements(1);
        var moduleRefRows = new List<TrRmModuleRefRow>();
        var moduleRefRowByIdentity = new Dictionary<string, int>(
            StringComparer.Ordinal);
        var importRows = new List<TrRmImportRow>(bindings.Count);
        foreach (var binding in bindings)
        {
            var producerStringRow = FindStringRow(tables.Strings,
                stringCount, binding.ProducerIdentity);
            var moduleRefRow = 0;
            if (reuseModuleRefs)
            {
                _ = moduleRefRowByIdentity.TryGetValue(
                    binding.ProducerIdentity, out moduleRefRow);
            }

            if (moduleRefRow == 0)
            {
                moduleRefRows.Add(new TrRmModuleRefRow(producerStringRow,
                    targetRequirement, runtimePolicy));
                moduleRefRow = moduleRefRows.Count;
                if (reuseModuleRefs)
                {
                    moduleRefRowByIdentity.Add(binding.ProducerIdentity,
                        moduleRefRow);
                }
            }

            var expectedMethodRow = 0;
            for (var methodRow = 1; methodRow <= tables.Methods.Count;
                methodRow++)
            {
                if (tables.Methods.GetOwnerModule(methodRow) == 1
                    && tables.Methods.GetName(methodRow)
                        == binding.MethodName
                    && tables.StandardNativeImports
                        .HasImportForMethod(methodRow))
                {
                    if (expectedMethodRow != 0)
                    {
                        throw new InvalidDataException(
                            "The test artifact has duplicate NativeImport MethodDefs.");
                    }

                    expectedMethodRow = methodRow;
                }
            }

            Assert.That(expectedMethodRow, Is.GreaterThan(0));
            var exportKeyStringRow = FindStringRow(tables.Strings,
                stringCount, binding.ExportKey);
            importRows.Add(new TrRmImportRow(moduleRefRow,
                exportKeyStringRow,
                0x1600_0000u | (uint)expectedMethodRow));
        }

        var moduleRefBytes = TrRmModuleRefTableWriter.Encode(moduleRefRows,
            tables.Strings, modules);
        var moduleRefs = new TrRmModuleRefTableReader(moduleRefBytes,
            moduleRefRows.Count, tables.Strings, modules);
        var importBytes = TrRmImportTableWriter.Encode(importRows,
            tables.Strings, moduleRefs, tables.TypeDefs, tables.Methods,
            default, default, default, default, tables.Features, modules,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var methodBytes = methodSectionBytes.ToArray();
        if (duplicateSecondMethodName)
        {
            var firstNameStringRow = FindStringRow(tables.Strings,
                stringCount, "First");
            var secondMethodRow = 0;
            for (var methodRow = 1; methodRow <= tables.Methods.Count;
                methodRow++)
            {
                if (tables.Methods.GetName(methodRow) == "Second"
                    && tables.StandardNativeImports
                        .HasImportForMethod(methodRow))
                {
                    secondMethodRow = methodRow;
                    break;
                }
            }

            Assert.That(secondMethodRow, Is.GreaterThan(0));
            var methodRows = new List<TrRmMethodDefRow>(methodCount);
            for (var methodRow = 1; methodRow <= methodCount; methodRow++)
            {
                var row = methodSectionBytes.Slice((methodRow - 1) * 28, 28);
                var nameStringRow = checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(row[4..]));
                if (methodRow == secondMethodRow)
                {
                    nameStringRow = firstNameStringRow;
                }

                methodRows.Add(new TrRmMethodDefRow(
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(row)),
                    nameStringRow,
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[8..])),
                    BinaryPrimitives.ReadUInt32LittleEndian(row[12..]),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[16..])),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[20..])),
                    checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                        row[24..]))));
            }

            methodBytes = TrRmMethodDefTableWriter.Encode(methodRows,
                tables.Strings, modules, tables.TypeDefs, tables.Signatures,
                tables.Contracts.Count, tables.Bodies.Count);
        }

        var sections = new List<TrRmSection>(index.SectionCount + 2);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            if (kind is 0x0010 or 0x001C)
            {
                Assert.That(recordCount, Is.Zero);
                continue;
            }

            sections.Add(kind == 0x0016 && duplicateSecondMethodName
                ? new TrRmSection(kind, flags, (uint)recordCount,
                    methodBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        sections.Add(new TrRmSection(0x0010, 0,
            (uint)moduleRefRows.Count, moduleRefBytes));
        sections.Add(new TrRmSection(0x001C, 0,
            (uint)importRows.Count, importBytes));
        sections.Sort(static (left, right) =>
            left.Kind.CompareTo(right.Kind));
        var result = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var resultTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(result));
        Assert.That(resultTables.ModuleRefs.Count,
            Is.EqualTo(moduleRefRows.Count));
        Assert.That(resultTables.Imports.Count, Is.EqualTo(importRows.Count));
        for (var methodRow = 1; methodRow <= resultTables.Methods.Count;
            methodRow++)
        {
            if (resultTables.Methods.GetBodyRow(methodRow) != 0)
            {
                resultTables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }

        return result;
    }

    private static int FindStringRow(TrRmStringTableReader strings,
        int stringCount, string text)
    {
        for (var row = 1; row <= stringCount; row++)
        {
            if (StringComparer.Ordinal.Equals(
                    strings.DecodeNamespace(row), text))
            {
                return row;
            }
        }

        throw new InvalidDataException(
            $"The test artifact has no String row for '{text}'.");
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

    internal static byte[] CreateRootMethodImportArtifact(
        string moduleIdentity, string methodName,
        string producerIdentity, string exportKey, CompilationReference core)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "GetCurrentProcessId",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 {{methodName}}();

            int32 Main() {
                unsafe {
                    var callback = &{{methodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return AddRootMethodImports(artifact, [
            (methodName, producerIdentity, exportKey),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
    }

    private static byte[] CreateEmptyFModuleArtifact(string moduleIdentity)
    {
        var stringsBytes
            = TrRmStringTableWriter.EncodeNames([(moduleIdentity, 0)]);
        var strings = new TrRmStringTableReader(stringsBytes, 1);
        var modules = TrRmModuleTableWriter.Encode([
            new TrRmModuleRow(1, 1, 0, 0, 1, 1, 1, 1),
        ], strings, 0, 0, 0, 0, 0);
        return TrRmArtifactWriter.Encode([
            new TrRmSection(0x0001, 0, 1, modules),
            new TrRmSection(0x0002, 0, 1, stringsBytes),
        ], TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] AddMethodExportAlias(byte[] artifact,
        string methodName, string exportKey)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        var methodRow = 0;
        for (var row = 1; row <= tables.Methods.Count; row++)
        {
            if (tables.Methods.GetOwnerModule(row) == 1
                && StringComparer.Ordinal.Equals(
                    tables.Methods.GetName(row), methodName))
            {
                methodRow = row;
                break;
            }
        }

        Assert.That(methodRow, Is.GreaterThan(0));
        var exportRows = new List<TrRmExportRow>();
        foreach (var (definition, existingKey)
            in tables.Exports.GetExportsForModule(1))
        {
            exportRows.Add(new TrRmExportRow(definition,
                FindStringRow(tables.Strings, stringCount, existingKey)));
        }

        exportRows.Add(new TrRmExportRow(
            0x1600_0000u | (uint)methodRow,
            FindStringRow(tables.Strings, stringCount, exportKey)));
        var exports = TrRmExportTableWriter.Encode(exportRows,
            tables.Strings, tables.TypeDefs, tables.Methods,
            default, tables.Imports, tables.Interfaces,
            tables.AssociatedTypeDefs, tables.Fields, tables.Features,
            tables.Modules, TrRmArtifactSchema.ForeignInvocationDraft,
            tables.AssociatedConstDefs, tables.StaticFields,
            tables.StaticFieldContracts, tables.IntrinsicRegistrations);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x001D
                ? new TrRmSection(kind, flags, (uint)exportRows.Count,
                    exports)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static List<string> ReadPeImports(byte[] image, string library)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader
            ?? throw new InvalidDataException("Missing PE header.");
        var descriptors = pe.GetSectionData(
            header.ImportTableDirectory.RelativeVirtualAddress).GetReader();
        var result = new List<string>();
        while (true)
        {
            var lookup = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            var name = descriptors.ReadInt32();
            _ = descriptors.ReadInt32();
            if (lookup == 0)
            {
                return result;
            }

            if (!ReadPeName(pe, name).Equals(library,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entries = pe.GetSectionData(lookup).GetReader();
            for (var entry = entries.ReadUInt64(); entry != 0;
                entry = entries.ReadUInt64())
            {
                Assert.That(entry & 0x8000_0000_0000_0000UL, Is.EqualTo(0UL));
                result.Add(ReadPeName(pe, checked((int)entry + 2)));
            }
        }
    }

    private static string ReadPeName(PEReader pe, int rva)
    {
        var reader = pe.GetSectionData(rva).GetReader();
        var text = new StringBuilder();
        for (var value = reader.ReadByte(); value != 0;
            value = reader.ReadByte())
        {
            _ = text.Append((char)value);
        }

        return text.ToString();
    }

    private static void WriteStandardFArtifactEvidence(string role,
        string moduleIdentity, byte[] artifact)
    {
        TestContext.WriteLine(
            $"Standard F input {role}: module={moduleIdentity}; bytes={artifact.Length}; sha256={Convert.ToHexString(SHA256.HashData(artifact))}");
    }

    private static string[] ReadPeExports(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        var header = pe.PEHeaders.PEHeader
            ?? throw new InvalidDataException("Missing PE header.");
        var exportDirectory = header.ExportTableDirectory;
        if (exportDirectory.RelativeVirtualAddress == 0
            || exportDirectory.Size == 0)
        {
            return [];
        }

        var export = pe.GetSectionData(
            exportDirectory.RelativeVirtualAddress).GetReader();
        _ = export.ReadInt32();
        _ = export.ReadInt32();
        _ = export.ReadUInt16();
        _ = export.ReadUInt16();
        _ = export.ReadInt32();
        _ = export.ReadInt32();
        _ = export.ReadInt32();
        var nameCount = export.ReadInt32();
        _ = export.ReadInt32();
        var namesRva = export.ReadInt32();
        _ = export.ReadInt32();
        var names = pe.GetSectionData(namesRva).GetReader();
        var exports = new List<string>(nameCount);
        for (var index = 0; index < nameCount; index++)
        {
            exports.Add(ReadPeName(pe, names.ReadInt32()));
        }

        return [.. exports];
    }

    private static byte[] ReencodeFirstModuleRefIdentity(byte[] artifact,
        string identity, int encoding)
        => ReencodeModuleRefIdentity(artifact, 1, identity, encoding);

    private static byte[] ReencodeModuleRefIdentity(byte[] artifact,
        int moduleRefRow, string identity, int encoding)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(moduleRefRow, Is.InRange(1, tables.ModuleRefs.Count));
        Assert.That(index.TryGetSection(0x0001, out var moduleBytes,
            out var moduleCount), Is.True);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        Assert.That(index.TryGetSection(0x0010,
            out var moduleRefSectionBytes, out var moduleRefCount), Is.True);

        var stringRows = new (string Text, int Encoding)[stringCount + 1];
        for (var row = 1; row <= stringCount; row++)
        {
            stringRows[row - 1] = (tables.Strings.DecodeNamespace(row), 0);
        }

        stringRows[stringCount] = (identity, encoding);
        var stringBytes = TrRmStringTableWriter.EncodeTextRows(stringRows);
        var strings = new TrRmStringTableReader(stringBytes,
            stringRows.Length);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleCount,
            strings, tables.ModuleRefs.Count, tables.TypeDefs.Count,
            tables.Methods.Count, tables.Modules.SourceFileCount,
            tables.Features.Count);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCount);
        for (var row = 1; row <= moduleRefCount; row++)
        {
            var sourceRow = moduleRefSectionBytes.Slice((row - 1) * 12,
                12);
            var identityStringRow = row == moduleRefRow
                ? stringRows.Length
                : checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    sourceRow));
            var (targetRequirement, runtimePolicy)
                = tables.ModuleRefs.GetRequirements(row);
            moduleRefRows.Add(new TrRmModuleRefRow(identityStringRow,
                targetRequirement, runtimePolicy));
        }

        var rewrittenModuleRefBytes = TrRmModuleRefTableWriter.Encode(
            moduleRefRows, strings, modules);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind switch
            {
                0x0002 => new TrRmSection(kind, flags,
                    (uint)stringRows.Length, stringBytes),
                0x0010 => new TrRmSection(kind, flags,
                    (uint)moduleRefCount, rewrittenModuleRefBytes),
                _ => new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()),
            });
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] ReplaceImportExportKey(byte[] artifact,
        int importRow, string exportKey)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(importRow, Is.InRange(1, tables.Imports.Count));
        Assert.That(index.TryGetSection(0x001C, out var importSectionBytes,
            out var importCount), Is.True);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        var keyStringRow = FindStringRow(tables.Strings, stringCount,
            exportKey);
        var rewrittenImportBytes = importSectionBytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            rewrittenImportBytes.AsSpan((importRow - 1) * 12 + 4),
            (uint)keyStringRow);

        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x001C
                ? new TrRmSection(kind, flags, (uint)importCount,
                    rewrittenImportBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        var result = TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
        var resultTables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(
                TrRmArtifactIndex.ForForeignInvocationDraft(result));
        for (var methodRow = 1; methodRow <= resultTables.Methods.Count;
            methodRow++)
        {
            if (resultTables.Methods.GetBodyRow(methodRow) != 0)
            {
                resultTables.ValidateRegisterBodyMapsForMethod(methodRow);
            }
        }

        return result;
    }

    private static byte[] ReplaceSectionRecordCount(byte[] artifact,
        int sectionKind, uint recordCount)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var result = (byte[])artifact.Clone();
        var found = false;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out _, out _, out _);
            if (kind != sectionKind)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(
                32 + section * 16 + 4), recordCount);
            found = true;
            break;
        }

        Assert.That(found, Is.True);
        return result;
    }

    private static byte[] RenameMethodDef(byte[] artifact,
        string currentName, string newName)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(index.TryGetSection(0x0002, out _, out var stringCount),
            Is.True);
        Assert.That(index.TryGetSection(0x0016, out var methodBytes,
            out var methodCount), Is.True);
        var newNameStringRow = FindStringRow(tables.Strings, stringCount,
            newName);
        var rows = new List<TrRmMethodDefRow>(methodCount);
        var renamed = false;
        for (var row = 1; row <= methodCount; row++)
        {
            var data = methodBytes.Slice((row - 1) * 28, 28);
            var rowName = tables.Methods.GetName(row);
            var replaceName = StringComparer.Ordinal.Equals(rowName,
                currentName);
            if (replaceName)
            {
                Assert.That(renamed, Is.False);
                renamed = true;
            }

            rows.Add(new TrRmMethodDefRow(
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data)),
                replaceName ? newNameStringRow : checked((int)
                    BinaryPrimitives.ReadUInt32LittleEndian(data[4..])),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[8..])),
                BinaryPrimitives.ReadUInt32LittleEndian(data[12..]),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[16..])),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[20..])),
                checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                    data[24..]))));
        }

        Assert.That(renamed, Is.True);
        var renamedMethodBytes = TrRmMethodDefTableWriter.Encode(rows,
            tables.Strings, tables.Modules, tables.TypeDefs,
            tables.Signatures, tables.Contracts.Count, tables.Bodies.Count);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0016
                ? new TrRmSection(kind, flags, (uint)methodCount,
                    renamedMethodBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    internal static byte[] CreateMixedImportProviderArtifact(
        string moduleIdentity, string ownedMethodName,
        string ownedNativeExport, string importedMethodName,
        string importedNativeExport, string producerIdentity,
        string exportKey, CompilationReference core,
        string? callerTargetMethodName = null)
    {
        const string schemaIdentity
            = "trrm/1-draft-foreign-invocation-contracts-1";
        var targetMethodName = callerTargetMethodName ?? ownedMethodName;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{ownedNativeExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            public extern int32 {{ownedMethodName}}();

            [Terrum.Interop.NativeImport(
                "kernel32.dll",
                "{{importedNativeExport}}",
                CallConv: Terrum.Interop.NativeCallingConvention.Unmanaged)]
            internal extern int32 {{importedMethodName}}();

            internal int32 ProviderBody() {
                unsafe {
                    var callback = &{{targetMethodName}};
                    return callback();
                }
            }
            """), core);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var artifact = TrRmExecutableCompiler.EmitStandard(
            compilation.Program!, moduleIdentity, schemaIdentity, 1);
        return AddRootMethodImports(artifact, [
            (importedMethodName, producerIdentity, exportKey),
        ], reuseModuleRefs: false, duplicateSecondMethodName: false);
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
        byte[] firstArtifact, byte[] secondArtifact,
        TrRmArtifactSchema schema
            = TrRmArtifactSchema.ForeignInvocationDraft)
    {
        var usesScalarFrozenValues = schema
            == TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
        var usesA2EU = usesScalarFrozenValues || schema
            == TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var firstIndex = usesScalarFrozenValues
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    firstArtifact)
            : usesA2EU
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    firstArtifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(firstArtifact);
        var secondIndex = usesScalarFrozenValues
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    secondArtifact)
            : usesA2EU
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    secondArtifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(secondArtifact);
        var firstTables = usesScalarFrozenValues
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    firstIndex)
            : usesA2EU
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    firstIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    firstIndex);
        var secondTables = usesScalarFrozenValues
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    secondIndex)
            : usesA2EU
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    secondIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    secondIndex);
        Assert.That(firstTables.Modules.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(secondTables.Modules.Count, Is.EqualTo(1));
        Assert.That(firstTables.Methods.Count,
            Is.GreaterThanOrEqualTo(firstTables.Modules.Count * 3));
        Assert.That(secondTables.Methods.Count, Is.GreaterThanOrEqualTo(3));
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

        Assert.That(firstAnchorCount, Is.EqualTo(firstTables.Modules.Count));
        Assert.That(secondAnchorCount, Is.EqualTo(1));
        Assert.That(firstTables.TypeDefs.Count, Is.Zero);
        Assert.That(secondTables.TypeDefs.Count, Is.Zero);
        Assert.That(firstTables.Fields.Count, Is.Zero);
        Assert.That(secondTables.Fields.Count, Is.Zero);
        Assert.That(firstTables.Imports.Count,
            Is.InRange(0, firstTables.Modules.Count));
        Assert.That(secondTables.Imports.Count, Is.InRange(0, 1));
        Assert.That(firstTables.ModuleRefs.Count,
            Is.EqualTo(firstTables.Imports.Count));
        Assert.That(secondTables.ModuleRefs.Count,
            Is.EqualTo(secondTables.Imports.Count));
        Assert.That(firstTables.Bodies.Count,
            Is.EqualTo(firstTables.Modules.Count));
        Assert.That(secondTables.Bodies.Count, Is.EqualTo(1));
        Assert.That(firstIndex.SectionCount, Is.GreaterThanOrEqualTo(9));
        Assert.That(secondIndex.SectionCount, Is.GreaterThanOrEqualTo(9));

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
            out var secondModuleRefBytes, out var secondModuleRefCount);
        var secondImportsFound = secondIndex.TryGetSection(0x001C,
            out var secondImportBytes, out var secondImportCount);
        var secondExportsFound = secondIndex.TryGetSection(0x001D,
            out var secondExportBytes, out var secondExportCount);
        Assert.That(firstModuleCount, Is.EqualTo(firstTables.Modules.Count));
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
        Assert.That(firstNativeImportCount, Is.GreaterThan(0));
        Assert.That(secondNativeImportCount, Is.GreaterThan(0));
        Assert.That(firstModuleRefCount, Is.EqualTo(firstTables.ModuleRefs.Count));
        Assert.That(firstImportCount, Is.EqualTo(firstTables.Imports.Count));
        Assert.That(secondModuleRefCount,
            Is.EqualTo(secondTables.ModuleRefs.Count));
        Assert.That(secondImportCount,
            Is.EqualTo(secondTables.Imports.Count));
        Assert.That(firstModuleRefsFound, Is.EqualTo(firstModuleRefCount != 0));
        Assert.That(firstImportsFound, Is.EqualTo(firstImportCount != 0));
        Assert.That(secondModuleRefsFound,
            Is.EqualTo(secondModuleRefCount != 0));
        Assert.That(secondImportsFound, Is.EqualTo(secondImportCount != 0));
        Assert.That(firstExportsFound, Is.EqualTo(firstExportCount != 0));
        Assert.That(secondExportsFound, Is.EqualTo(secondExportCount != 0));
        var firstModuleFeatureCount = 0;
        for (var moduleRow = 1; moduleRow <= firstTables.Modules.Count;
            moduleRow++)
        {
            firstModuleFeatureCount += firstTables.Modules
                .GetFeatureRange(moduleRow).Count;
        }
        Assert.That(firstModuleFeatureCount, Is.EqualTo(firstFeatureCount));

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
            featureCount, schema);
        var scalarValueTables = usesScalarFrozenValues
            ? CombineScalarFrozenValueTables(firstIndex, secondIndex)
            : (BlobSection: Array.Empty<byte>(), BlobCount: 0,
                ConstantSourceSection: Array.Empty<byte>(),
                ConstantSourceCount: 0,
                FrozenValueNodeSection: Array.Empty<byte>(),
                FrozenValueNodeCount: 0);
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
            0, 0, 0, 0, schema,
            methodDefCount: methodCount);
        var typeSpecs = new TrRmTypeSpecTableReader(typeSpecBytes,
            typeSpecRows.Count, 0, 0, 0, 0,
            schema,
            methodDefCount: methodCount);

        var signatureRows = new List<TrRmMethodSigRow>(
            firstSignatureCount + secondSignatureCount);
        var paramRows = new List<TrRmParamRow>(
            firstTables.Signatures.ParamCount
            + secondTables.Signatures.ParamCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var source = artifact == 0 ? firstTables.Signatures
                : secondTables.Signatures;
            var sourceParameters = artifact == 0
                ? firstTables.Parameters : secondTables.Parameters;
            var signatureCount = artifact == 0 ? firstSignatureCount
                : secondSignatureCount;
            var signatureOffset = artifact == 0 ? 0 : firstSignatureCount;
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
                Assert.That(genericStart, Is.Zero);
                Assert.That(genericCount, Is.Zero);
                var parameterFirst = 0;
                if (parameterCount != 0)
                {
                    parameterFirst = paramRows.Count + 1;
                    for (var ordinal = 0; ordinal < parameterCount; ordinal++)
                    {
                        var sourceParamRow = parameterStart + ordinal;
                        var typeSpecRef
                            = sourceParameters.GetTypeSpec(sourceParamRow);
                        if (artifact != 0
                            && (typeSpecRef & 0xFF00_0000u) == 0)
                        {
                            typeSpecRef = checked(typeSpecRef
                                + (uint)typeOffset);
                        }

                        paramRows.Add(new TrRmParamRow(
                            signatureOffset + row, ordinal, typeSpecRef,
                            sourceParameters.GetMode(sourceParamRow),
                            sourceParameters.GetFlags(sourceParamRow)));
                    }
                }

                signatureRows.Add(new TrRmMethodSigRow(resultType,
                    receiverType, parameterFirst, parameterCount, 0, 0));
            }
        }

        var signatureBytes = TrRmMethodSigTableWriter.Encode(signatureRows,
            typeSpecs, paramRows.Count, 0);
        var signatures = new TrRmMethodSigTableReader(signatureBytes,
            signatureRows.Count, typeSpecs, paramRows.Count, 0);
        var parameterBytes = paramRows.Count == 0 ? []
            : TrRmParamTableWriter.Encode(paramRows, signatures, typeSpecs,
                schema);
        var parameters = paramRows.Count == 0
            ? default(TrRmParamTableReader)
            : new TrRmParamTableReader(parameterBytes, paramRows.Count,
                signatures, typeSpecs, schema);
        var moduleRows = new List<TrRmModuleRow>(
            firstModuleCount + secondModuleCount);
        for (var moduleRow = 1; moduleRow <= firstTables.Modules.Count;
            moduleRow++)
        {
            var (targetRequirement, runtimePolicy)
                = firstTables.Modules.GetRequirements(moduleRow);
            var (moduleRefStart, _) = firstTables.Modules
                .GetModuleRefRange(moduleRow);
            var (typeDefStart, _) = firstTables.Modules
                .GetTypeDefRange(moduleRow);
            var (methodDefStart, _) = firstTables.Modules
                .GetMethodDefRange(moduleRow);
            var (featureStart, moduleFeatureCount)
                = firstTables.Modules.GetFeatureRange(moduleRow);
            var identityStringRow = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(
                    firstModuleBytes.Slice((moduleRow - 1) * 48, 4)));
            moduleRows.Add(new TrRmModuleRow(identityStringRow,
                targetRequirement, runtimePolicy,
                firstTables.Modules.GetEntryMethod(moduleRow),
                moduleRefStart, typeDefStart, methodDefStart, 1,
                featureStart, moduleFeatureCount,
                firstTables.Modules.GetGoverningRuleEdition(moduleRow)));
        }

        var (secondTargetRequirement, secondRuntimePolicy)
            = secondTables.Modules.GetRequirements(1);
        var secondIdentityRow = checked((int)secondStringMap[
            (int)BinaryPrimitives.ReadUInt32LittleEndian(secondModuleBytes)]);
        var (secondModuleRefStart, _) = secondTables.Modules
            .GetModuleRefRange(1);
        var (secondTypeDefStart, _) = secondTables.Modules
            .GetTypeDefRange(1);
        var (secondMethodDefStart, _) = secondTables.Modules
            .GetMethodDefRange(1);
        var (secondFeatureStart, secondModuleFeatureCount)
            = secondTables.Modules.GetFeatureRange(1);
        Assert.That(secondModuleFeatureCount, Is.EqualTo(secondFeatureCount));
        var secondEntryMethod = secondTables.Modules.GetEntryMethod(1);
        if (secondEntryMethod != 0)
        {
            secondEntryMethod += firstMethodCount;
        }

        var moduleRefCount = firstModuleRefCount + secondModuleRefCount;
        moduleRows.Add(new TrRmModuleRow(secondIdentityRow,
            secondTargetRequirement, secondRuntimePolicy, secondEntryMethod,
            secondModuleRefStart + firstModuleRefCount,
            secondTypeDefStart, secondMethodDefStart + firstMethodCount, 1,
            secondFeatureStart + firstFeatureCount,
            secondModuleFeatureCount,
            secondTables.Modules.GetGoverningRuleEdition(1)));
        var moduleBytes = TrRmModuleTableWriter.Encode(moduleRows,
            strings, moduleRefCount, 0, methodCount, 0, featureCount);
        var modules = new TrRmModuleTableReader(moduleBytes, moduleRows.Count, strings,
            moduleRefCount, 0, methodCount, 0, featureCount);
        var moduleRefRows = new List<TrRmModuleRefRow>(moduleRefCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceRows = artifact == 0 ? firstModuleRefBytes
                : secondModuleRefBytes;
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
            var (methodStart, methodEnd) = artifact == 0
                ? (1, source.Count + 1)
                : secondTables.Modules.GetMethodDefRange(1);
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
                : secondImportBytes;
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
                features, modules, schema);
        var imports = importRows.Count == 0
            ? default
            : new TrRmImportTableReader(importBytes, importRows.Count,
                strings, moduleRefs, typeDefs, methods, default, default,
                default, default, features, modules, schema);

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
                signatures, parameters, typeSpecs, features, modules,
                predicateGraphCount: 0, schema: schema);
        var contracts = new TrRmContractTableReader(contractBytes,
            contractRows.Count, typeDefs, methods, signatures, parameters,
            typeSpecs, 0, features, modules, schema);

        var bodyRows = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var artifact = 0; artifact < 2; artifact++)
        {
            var sourceBodies = artifact == 0 ? firstTables.Bodies
                : secondTables.Bodies;
            var sourceContracts = artifact == 0 ? firstContractCount
                : secondContractCount;
            var methodOffset = artifact == 0 ? 0 : firstMethodCount;
            var typeOffset = artifact == 0 ? 0 : firstTypeSpecCount;
            var importOffset = artifact == 0 ? 0 : firstImportCount;
            for (var bodyRow = 1; bodyRow <= sourceBodies.Count; bodyRow++)
            {
                var body = sourceBodies.GetRegisterBody(bodyRow,
                    sourceContracts);
                bodyRows.Add(RemapNativeAddressBody(body, typeOffset,
                    methodOffset, importOffset, contractCount, schema,
                    artifact == 0 ? 0 : firstTables.ConstantSources.Count));
            }
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
            modules, signatures, parameters, typeSpecs, contracts, layouts);
        var sections = new List<TrRmSection>
        {
            new TrRmSection(0x0001, 0, (uint)moduleRows.Count, moduleBytes),
            new TrRmSection(0x0002, 0, (uint)stringRows.Count, stringBytes),
        };
        if (scalarValueTables.BlobCount != 0)
        {
            sections.Add(new TrRmSection(0x0003, 0,
                (uint)scalarValueTables.BlobCount,
                scalarValueTables.BlobSection));
        }
        sections.Add(new TrRmSection(0x0004, 0, (uint)featureCount,
            featureBytes));
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
        ]);
        if (paramRows.Count != 0)
        {
            sections.Add(new TrRmSection(0x0018, 0,
                (uint)paramRows.Count, parameterBytes));
        }
        sections.Add(new TrRmSection(0x001B, 0,
            (uint)contractRows.Count, contractBytes));
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
                schema);
            sections.Add(new TrRmSection(0x001D, 0,
                (uint)exportRows.Count, exportBytes));
        }

        sections.AddRange([
            new TrRmSection(0x0020, 0, (uint)bodyRows.Count, bodyBytes),
            new TrRmSection(0x0021, 0, (uint)nativeImportRows.Count,
                nativeImportBytes),
        ]);
        if (scalarValueTables.ConstantSourceCount != 0)
        {
            sections.Add(new TrRmSection(0x0030, 0,
                (uint)scalarValueTables.ConstantSourceCount,
                scalarValueTables.ConstantSourceSection));
        }
        if (scalarValueTables.FrozenValueNodeCount != 0)
        {
            sections.Add(new TrRmSection(0x003F, 0,
                (uint)scalarValueTables.FrozenValueNodeCount,
                scalarValueTables.FrozenValueNodeSection));
        }
        var combinedArtifact = TrRmArtifactWriter.Encode(sections, schema);
        var combinedIndex = usesScalarFrozenValues
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                    combinedArtifact)
            : usesA2EU
            ? TrRmArtifactIndex
                .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                    combinedArtifact)
            : TrRmArtifactIndex.ForForeignInvocationDraft(combinedArtifact);
        var combinedTables = usesScalarFrozenValues
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                    combinedIndex)
            : usesA2EU
            ? TrRmStructuralMetadataValidator
                .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraftAndGetExecutionTables(
                    combinedIndex)
            : TrRmStructuralMetadataValidator
                .ValidateForeignInvocationDraftAndGetExecutionTables(
                    combinedIndex);
        Assert.That(combinedTables.Modules.Count,
            Is.EqualTo(firstModuleCount + secondModuleCount));
        for (var method = 1; method <= combinedTables.Methods.Count; method++)
        {
            if (combinedTables.Methods.GetBodyRow(method) != 0)
            {
                combinedTables.ValidateRegisterBodyMapsForMethod(method);
            }
        }

        return combinedArtifact;
    }

    private static (
        byte[] BlobSection,
        int BlobCount,
        byte[] ConstantSourceSection,
        int ConstantSourceCount,
        byte[] FrozenValueNodeSection,
        int FrozenValueNodeCount) CombineScalarFrozenValueTables(
        TrRmArtifactIndex firstIndex, TrRmArtifactIndex secondIndex)
    {
        var firstHasBlobs = firstIndex.TryGetSection(0x0003,
            out var firstBlobBytes, out var firstBlobCount);
        var secondHasBlobs = secondIndex.TryGetSection(0x0003,
            out var secondBlobBytes, out var secondBlobCount);
        var firstHasSources = firstIndex.TryGetSection(0x0030,
            out var firstSourceBytes, out var firstSourceCount);
        var secondHasSources = secondIndex.TryGetSection(0x0030,
            out var secondSourceBytes, out var secondSourceCount);
        var firstHasNodes = firstIndex.TryGetSection(0x003F,
            out var firstNodeBytes, out var firstNodeCount);
        var secondHasNodes = secondIndex.TryGetSection(0x003F,
            out var secondNodeBytes, out var secondNodeCount);
        Assert.That(firstHasBlobs, Is.EqualTo(firstBlobCount != 0));
        Assert.That(secondHasBlobs, Is.EqualTo(secondBlobCount != 0));
        Assert.That(firstHasSources, Is.EqualTo(firstSourceCount != 0));
        Assert.That(secondHasSources, Is.EqualTo(secondSourceCount != 0));
        Assert.That(firstHasNodes, Is.EqualTo(firstNodeCount != 0));
        Assert.That(secondHasNodes, Is.EqualTo(secondNodeCount != 0));
        Assert.That(firstSourceCount, Is.EqualTo(firstNodeCount));
        Assert.That(secondSourceCount, Is.EqualTo(secondNodeCount));

        var blobs = new List<ReadOnlyMemory<byte>>(
            firstBlobCount + secondBlobCount);
        if (firstBlobCount != 0)
        {
            var reader = new TrRmBlobTableReader(firstBlobBytes,
                firstBlobCount);
            for (var row = 1; row <= reader.Count; row++)
            {
                blobs.Add(reader.GetRow(row).ToArray());
            }
        }
        if (secondBlobCount != 0)
        {
            var reader = new TrRmBlobTableReader(secondBlobBytes,
                secondBlobCount);
            for (var row = 1; row <= reader.Count; row++)
            {
                blobs.Add(reader.GetRow(row).ToArray());
            }
        }
        Assert.That(blobs.Count, Is.LessThanOrEqualTo(0xFF_FFFF));
        var blobSection = blobs.Count == 0
            ? []
            : TrRmVariableTableWriter.EncodeBlobs(blobs);

        var sourceCount = checked(firstSourceCount + secondSourceCount);
        Assert.That(sourceCount, Is.LessThanOrEqualTo(0xFF_FFFF));
        var sourceSection = new byte[checked(sourceCount * 8)];
        firstSourceBytes.CopyTo(sourceSection);
        for (var row = 0; row < secondSourceCount; row++)
        {
            var sourceRow = secondSourceBytes.Slice(row * 8, 8);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(sourceRow),
                Is.EqualTo(1));
            var payloadReference
                = BinaryPrimitives.ReadUInt32LittleEndian(sourceRow[4..]);
            Assert.That(payloadReference, Is.InRange(1u,
                (uint)secondNodeCount));
            var destination = sourceSection.AsSpan(
                (firstSourceCount + row) * 8, 8);
            sourceRow.CopyTo(destination);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..],
                checked(payloadReference + (uint)firstNodeCount));
        }

        var nodeCount = checked(firstNodeCount + secondNodeCount);
        Assert.That(nodeCount, Is.LessThanOrEqualTo(0xFF_FFFF));
        var nodeSection = new byte[checked(nodeCount * 24)];
        firstNodeBytes.CopyTo(nodeSection);
        for (var row = 0; row < secondNodeCount; row++)
        {
            var sourceRow = secondNodeBytes.Slice(row * 24, 24);
            var destination = nodeSection.AsSpan(
                (firstNodeCount + row) * 24, 24);
            sourceRow.CopyTo(destination);
            var blobReference = BinaryPrimitives.ReadUInt32LittleEndian(
                destination[8..]);
            if (blobReference != 0)
            {
                Assert.That(blobReference, Is.InRange(1u,
                    (uint)secondBlobCount));
                BinaryPrimitives.WriteUInt32LittleEndian(destination[8..],
                    checked(blobReference + (uint)firstBlobCount));
            }
        }

        for (var row = 0; row < nodeCount; row++)
        {
            var node = nodeSection.AsSpan(row * 24, 24);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node)
                & 0xFF00_0000u, Is.EqualTo(0xF200_0000u));
            Assert.That(node[4], Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node[12..]),
                Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node[16..]),
                Is.Zero);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(node[20..]),
                Is.Zero);
            var blobReference = BinaryPrimitives.ReadUInt32LittleEndian(
                node[8..]);
            Assert.That(blobReference, Is.InRange(1u, (uint)blobs.Count));
        }
        for (var row = 0; row < sourceCount; row++)
        {
            var source = sourceSection.AsSpan(row * 8, 8);
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(source),
                Is.EqualTo(1));
            var payloadReference = BinaryPrimitives.ReadUInt32LittleEndian(
                source[4..]);
            Assert.That(payloadReference, Is.InRange(1u, (uint)nodeCount));
        }

        var sourceSectionBytes = sourceCount == 0 ? [] : sourceSection;
        var nodeSectionBytes = nodeCount == 0 ? [] : nodeSection;
        return (blobSection, blobs.Count, sourceSectionBytes, sourceCount,
            nodeSectionBytes, nodeCount);
    }

    private static byte[] RewriteA2EUScalarFrozenSection(byte[] artifact,
        int sectionKind, Action<byte[]> rewrite)
    {
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                artifact);
        var sections = new List<TrRmSection>(index.SectionCount);
        var found = false;
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            var sectionBytes = bytes.ToArray();
            if (kind == sectionKind)
            {
                rewrite(sectionBytes);
                found = true;
            }
            sections.Add(new TrRmSection(kind, flags, (uint)recordCount,
                sectionBytes));
        }

        Assert.That(found, Is.True);
        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft);
    }

    private static byte[] RewriteA2EUScalarConstantSourceMap(byte[] artifact,
        uint sourceRow)
    {
        var schema = TrRmArtifactSchema
            .ForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraft(
                artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationStorageFixedArrayCopyEmptyUnitScalarFrozenValuesDraftAndGetExecutionTables(
                index);
        var mainRow = 0;
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetName(methodRow) == "Main")
            {
                mainRow = methodRow;
                break;
            }
        }
        Assert.That(mainRow, Is.GreaterThan(0));
        var bodyRow = tables.Methods.GetBodyRow(mainRow);
        Assert.That(bodyRow, Is.GreaterThan(0));
        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        Assert.That(body.ConstantCount, Is.GreaterThan(0));
        var constantMap = body.ConstantMap.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            constantMap.AsSpan(sizeof(uint)), sourceRow);
        var rewrittenBody = EncodeNativeAddressBody(body,
            body.TypeMap.ToArray(), body.MethodMap.ToArray(), constantMap,
            tables.Contracts.Count, schema);

        Assert.That(index.TryGetSection(0x0020, out var bodySection,
            out var bodyCount), Is.True);
        var bodyIndex = new TrRmVariableTableIndex(bodySection, bodyCount,
            1, 1);
        var bodies = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var row = 1; row <= bodyCount; row++)
        {
            bodies.Add(row == bodyRow
                ? rewrittenBody
                : bodyIndex.GetRow(row).ToArray());
        }
        var rewrittenBodySection = TrRmVariableTableWriter.EncodeBodies(
            bodies);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, (uint)recordCount,
                    rewrittenBodySection)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] ReplaceRootBodyMethodWithForeignModuleMethod(
        byte[] artifact, string moduleIdentity)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.TryFindModule(moduleIdentity,
            out var moduleRow), Is.True);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var bodyRow = 0;
        for (var methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0)
            {
                break;
            }
        }
        Assert.That(bodyRow, Is.GreaterThan(0));

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var methodMap = body.MethodMap.ToArray();
        var expectedToken = 0u;
        var foreignMethodRow = 0;
        for (var offset = 0; offset < methodMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                methodMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) == 0x1600_0000u
                && tables.Methods.GetOwnerModule(
                    checked((int)(token & 0x00FF_FFFFu))) == moduleRow)
            {
                expectedToken = token;
                break;
            }
        }
        for (var methodRow = 1; methodRow <= tables.Methods.Count;
            methodRow++)
        {
            if (tables.Methods.GetOwnerModule(methodRow) != moduleRow)
            {
                foreignMethodRow = methodRow;
                break;
            }
        }
        Assert.That(expectedToken, Is.Not.Zero);
        Assert.That(foreignMethodRow, Is.GreaterThan(0));
        var foreignToken = 0x1600_0000u | (uint)foreignMethodRow;
        var replaced = false;
        for (var offset = 0; offset < methodMap.Length;
            offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    methodMap.AsSpan(offset)) != expectedToken)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                methodMap.AsSpan(offset), foreignToken);
            replaced = true;
        }
        Assert.That(replaced, Is.True,
            $"Expected MethodMap token {expectedToken:X8}; "
            + $"MethodMap={Convert.ToHexString(methodMap)}.");

        var rewrittenBody = EncodeNativeAddressBody(body,
            body.TypeMap.ToArray(), methodMap, body.ConstantMap.ToArray(),
            tables.Contracts.Count,
            TrRmArtifactSchema.ForeignInvocationDraft);
        return ReplaceRegisterBody(artifact, bodyRow, rewrittenBody);
    }

    private static byte[] ReplaceRootBodyFunctionPointerTypeWithForeignModuleType(
        byte[] artifact, string moduleIdentity)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(tables.Modules.TryFindModule(moduleIdentity,
            out var moduleRow), Is.True);
        var (methodStart, methodEnd)
            = tables.Modules.GetMethodDefRange(moduleRow);
        var bodyRow = 0;
        for (var methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            bodyRow = tables.Methods.GetBodyRow(methodRow);
            if (bodyRow != 0)
            {
                break;
            }
        }
        Assert.That(bodyRow, Is.GreaterThan(0));

        var body = tables.Bodies.GetRegisterBody(bodyRow,
            tables.Contracts.Count);
        var typeMap = body.TypeMap.ToArray();
        var expectedToken = 0u;
        var foreignTypeSpecRow = 0;
        for (var offset = 0; offset < typeMap.Length;
            offset += sizeof(uint))
        {
            var token = BinaryPrimitives.ReadUInt32LittleEndian(
                typeMap.AsSpan(offset));
            if ((token & 0xFF00_0000u) != 0x1100_0000u)
            {
                continue;
            }

            var typeSpecRow = checked((int)(token & 0x00FF_FFFFu));
            if (tables.NativeFunctionPointerAnchors
                    .HasAnchorForTypeSpec(typeSpecRow)
                && tables.Methods.GetOwnerModule(tables
                    .NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(typeSpecRow)) == moduleRow)
            {
                expectedToken = token;
                break;
            }
        }
        for (var typeSpecRow = 1; typeSpecRow <= tables.TypeSpecs.Count;
            typeSpecRow++)
        {
            if (tables.NativeFunctionPointerAnchors
                    .HasAnchorForTypeSpec(typeSpecRow)
                && tables.Methods.GetOwnerModule(tables
                    .NativeFunctionPointerAnchors
                    .GetAnchorMethodDefRow(typeSpecRow)) != moduleRow)
            {
                foreignTypeSpecRow = typeSpecRow;
                break;
            }
        }
        Assert.That(expectedToken, Is.Not.Zero);
        Assert.That(foreignTypeSpecRow, Is.GreaterThan(0));
        var foreignToken = 0x1100_0000u | (uint)foreignTypeSpecRow;
        var replaced = false;
        for (var offset = 0; offset < typeMap.Length;
            offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    typeMap.AsSpan(offset)) != expectedToken)
            {
                continue;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                typeMap.AsSpan(offset), foreignToken);
            replaced = true;
        }
        Assert.That(replaced, Is.True,
            $"Expected TypeMap token {expectedToken:X8}; "
            + $"TypeMap={Convert.ToHexString(typeMap)}.");
        var rewrittenBody = EncodeNativeAddressBody(body, typeMap,
            body.MethodMap.ToArray(), body.ConstantMap.ToArray(),
            tables.Contracts.Count,
            TrRmArtifactSchema.ForeignInvocationDraft);
        return ReplaceRegisterBody(artifact, bodyRow, rewrittenBody);
    }

    private static byte[] ReplaceRegisterBody(byte[] artifact, int bodyRow,
        byte[] rewrittenBody)
    {
        var index = TrRmArtifactIndex.ForForeignInvocationDraft(artifact);
        var tables = TrRmStructuralMetadataValidator
            .ValidateForeignInvocationDraftAndGetExecutionTables(index);
        Assert.That(index.TryGetSection(0x0020, out _,
            out var bodyCount), Is.True);
        Assert.That(bodyRow, Is.InRange(1, bodyCount));
        var bodyRows = new List<ReadOnlyMemory<byte>>(bodyCount);
        for (var currentBodyRow = 1; currentBodyRow <= bodyCount;
            currentBodyRow++)
        {
            var currentBody = tables.Bodies.GetRegisterBody(
                currentBodyRow, tables.Contracts.Count);
            bodyRows.Add(currentBodyRow == bodyRow
                ? rewrittenBody
                : RemapNativeAddressBody(currentBody, 0, 0, 0,
                    tables.Contracts.Count,
                    TrRmArtifactSchema.ForeignInvocationDraft));
        }

        var rewrittenBodyBytes = TrRmVariableTableWriter
            .EncodeBodies(bodyRows);
        var sections = new List<TrRmSection>(index.SectionCount);
        for (var section = 0; section < index.SectionCount; section++)
        {
            index.GetSection(section, out var kind, out var flags,
                out var bytes, out var recordCount);
            sections.Add(kind == 0x0020
                ? new TrRmSection(kind, flags, (uint)bodyCount,
                    rewrittenBodyBytes)
                : new TrRmSection(kind, flags, (uint)recordCount,
                    bytes.ToArray()));
        }

        return TrRmArtifactWriter.Encode(sections,
            TrRmArtifactSchema.ForeignInvocationDraft);
    }

    private static byte[] RemapNativeAddressBody(
        TrRmRegisterBodyReader body, int typeSpecOffset,
        int methodDefOffset, int importOffset, int contractCount,
        TrRmArtifactSchema schema, int constantSourceOffset = 0)
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
            else if ((token & 0xFF00_0000u) == 0x1C00_0000u)
            {
                token = 0x1C00_0000u
                    | ((token & 0x00FF_FFFFu) + (uint)importOffset);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    methodMap.AsSpan(offset), token);
            }
        }

        var constantMap = body.ConstantMap.ToArray();
        for (var offset = 0; offset < constantMap.Length;
            offset += 2 * sizeof(uint))
        {
            var typeSpecRef = BinaryPrimitives.ReadUInt32LittleEndian(
                constantMap.AsSpan(offset));
            Assert.That(typeSpecRef & 0xFF00_0000u,
                Is.EqualTo(0xF200_0000u));
            var sourceRow = BinaryPrimitives.ReadUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)));
            Assert.That(sourceRow, Is.InRange(1u, 0x00FF_FFFFu));
            BinaryPrimitives.WriteUInt32LittleEndian(
                constantMap.AsSpan(offset + sizeof(uint)),
                checked(sourceRow + (uint)constantSourceOffset));
        }

        return EncodeNativeAddressBody(body, typeMap, methodMap,
            constantMap, contractCount, schema);
    }

    private static byte[] EncodeNativeAddressBody(
        TrRmRegisterBodyReader body, byte[] typeMap, byte[] methodMap,
        byte[] constantMap, int contractCount, TrRmArtifactSchema schema)
    {
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
            constantMap, blocks, records, body.Code.ToArray(),
            contractCount, schema);
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
            var isHomeRoleDefect = defect == "wrong-home-role";
            var modified = isHomeRoleDefect;
            if (isHomeRoleDefect)
            {
                ReadOnlySpan<byte> homeSelection = default;
                for (var recordIndex = 0;
                    recordIndex < body.RecordCount;
                    recordIndex++)
                {
                    var record = body.GetRecord(recordIndex);
                    if (record[0] == 0x04 && record[4] == 1)
                    {
                        homeSelection = record;
                        break;
                    }
                }

                if (homeSelection.IsEmpty)
                {
                    throw new InvalidOperationException(
                        "The A2E Body contains no Home PlaceSelection.");
                }

                var homeIndex = BinaryPrimitives.ReadUInt32LittleEndian(
                    homeSelection[16..]);
                if (homeIndex >= (uint)body.HomeCount)
                {
                    throw new InvalidOperationException(
                        "The A2E Home PlaceSelection has an invalid Home.");
                }

                var homeMap = body.HomeMap.ToArray();
                var homeMapOffset = bodyBytes.AsSpan().IndexOf(homeMap);
                if (homeMapOffset < 0)
                {
                    throw new InvalidOperationException(
                        "The A2E Body has no serialized HomeMap.");
                }

                bodyBytes[homeMapOffset + checked((int)homeIndex * 8)] = 2;
            }
            for (var recordIndex = 0; recordIndex < body.RecordCount;
                recordIndex++)
            {
                var record = body.GetRecord(recordIndex);
                var isHomePlaceDefect = defect is "wrong-home-access"
                    or "wrong-home-type" or "out-of-range-home";
                var isHomeAccessDefect = defect == "wrong-home-access"
                    && record[0] == 0x04 && record[4] == 1;
                var isNullaryStoreDefect = !isHomePlaceDefect
                    && !isHomeRoleDefect
                    && record[0] == 0x05 && record[5] == 0;
                if (!modified
                    && (isHomePlaceDefect && record[0] == 0x04
                        && record[4] == 1
                        || isNullaryStoreDefect))
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
                    else if (defect == "wrong-home-type")
                    {
                        if (body.TypeCount < 2)
                        {
                            throw new InvalidOperationException(
                                "The A2E fixture needs two TypeMap entries for the wrong-type case.");
                        }

                        var currentTypeMapIndex
                            = BinaryPrimitives.ReadUInt16LittleEndian(
                                record[20..]);
                        var wrongTypeMapIndex = checked((ushort)(
                            ((int)currentTypeMapIndex + 1)
                            % body.TypeCount));
                        BinaryPrimitives.WriteUInt16LittleEndian(
                            bodyBytes.AsSpan(recordOffset + 20),
                            wrongTypeMapIndex);
                    }
                    else if (defect == "out-of-range-home")
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            bodyBytes.AsSpan(recordOffset + 16),
                            checked((uint)body.HomeCount));
                    }
                    else
                    {
                        switch (defect)
                        {
                            case "nonzero-source-kind":
                                bodyBytes[recordOffset + 5] = 1;
                                break;
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

    private static byte[] RewriteA2EUCheckedArithmeticArtifact(
        byte[] artifact)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        var sections = new List<TrRmSection>(index.SectionCount);
        var modified = false;
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
            var code = body.GetBlockCode(0);
            var codeOffset = bodyBytes.AsSpan().IndexOf(code);
            if (codeOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU checked arithmetic has no serialized code bytes.");
            }

            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                if (code[position] is 0x24 or 0x25)
                {
                    bodyBytes[codeOffset + position + 1] = 0x0E;
                    modified = true;
                    break;
                }

                position += instructionLength;
            }

            if (!modified)
            {
                throw new InvalidOperationException(
                    "The A2EU Body contains no checked division or remainder.");
            }

            sections.Add(new TrRmSection(kind, flags,
                checked((uint)recordCount),
                TrRmVariableTableWriter.EncodeBodies([bodyBytes])));
        }

        return TrRmArtifactWriter.Encode(sections, schema);
    }

    private static byte[] RewriteA2EUConditionalRegisterArtifact(
        byte[] artifact, int blockIndex, byte conditionRegister)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var bodyBytes = bodyTable.GetRow(1);
            var body = new TrRmRegisterBodyReader(bodyBytes, contractCount,
                schema);
            var code = body.GetBlockCode(blockIndex);
            var bodyOffset = section.IndexOf(bodyBytes);
            var codeOffset = bodyBytes.IndexOf(code);
            var sectionOffset = artifact.AsSpan().IndexOf(section);
            if (bodyOffset < 0 || codeOffset < 0 || sectionOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU conditional Body has no serialized code range.");
            }

            for (var position = 0; position < code.Length;)
            {
                var instructionLength
                    = TrRmRegisterBodyReader.InstructionLength(
                        code[position], schema);
                if (code[position] == 0xE4)
                {
                    var malformed = (byte[])artifact.Clone();
                    var controlOffset = sectionOffset + bodyOffset
                        + codeOffset + position + 1;
                    malformed[controlOffset] = (byte)(
                        (malformed[controlOffset] & 0xF0)
                        | (conditionRegister & 0x0F));
                    return malformed;
                }

                position += instructionLength;
            }
        }

        throw new InvalidOperationException(
            "The A2EU Body contains no conditional branch.");
    }

    private static byte[] RewriteA2EUConditionalTargetArtifact(
        byte[] artifact, int edgeRecordIndex, ushort targetBlock)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var bodyBytes = bodyTable.GetRow(1);
            var body = new TrRmRegisterBodyReader(bodyBytes, contractCount,
                schema);
            var edge = body.GetRecord(edgeRecordIndex);
            if (edge[0] != 0x06)
            {
                throw new InvalidOperationException(
                    "The A2EU conditional record is not a ConditionalEdge.");
            }

            var sectionOffset = artifact.AsSpan().IndexOf(section);
            var bodyOffset = section.IndexOf(bodyBytes);
            var edgeOffset = bodyBytes.IndexOf(edge);
            if (sectionOffset < 0 || bodyOffset < 0 || edgeOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU ConditionalEdge has no serialized offset.");
            }

            var malformed = (byte[])artifact.Clone();
            BinaryPrimitives.WriteUInt16LittleEndian(
                malformed.AsSpan(sectionOffset + bodyOffset + edgeOffset + 4),
                targetBlock);
            return malformed;
        }

        throw new InvalidOperationException(
            "The A2EU artifact has no Body table.");
    }

    private static byte[] RewriteA2EUComparisonDestinationArtifact(
        byte[] artifact, byte destinationRegister)
    {
        const TrRmArtifactSchema schema
            = TrRmArtifactSchema
                .ForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft;
        var index = TrRmArtifactIndex
            .ForForeignInvocationStorageFixedArrayCopyEmptyUnitInitializationDraft(
                artifact);
        _ = index.TryGetSection(0x001B, out _, out var contractCount);
        for (var sectionIndex = 0; sectionIndex < index.SectionCount;
            sectionIndex++)
        {
            index.GetSection(sectionIndex, out var kind, out var flags,
                out var section, out var recordCount);
            _ = flags;
            if (kind != 0x0020)
            {
                continue;
            }

            var bodyTable = new TrRmBodyTableReader(section, recordCount,
                schema);
            var bodyBytes = bodyTable.GetRow(1);
            var body = new TrRmRegisterBodyReader(bodyBytes, contractCount,
                schema);
            var sectionOffset = artifact.AsSpan().IndexOf(section);
            var bodyOffset = section.IndexOf(bodyBytes);
            if (sectionOffset < 0 || bodyOffset < 0)
            {
                throw new InvalidOperationException(
                    "The A2EU comparison Body has no serialized range.");
            }

            for (var blockIndex = 0; blockIndex < body.BlockCount;
                blockIndex++)
            {
                var code = body.GetBlockCode(blockIndex);
                var codeOffset = bodyBytes.IndexOf(code);
                if (codeOffset < 0)
                {
                    throw new InvalidOperationException(
                        "The A2EU comparison Body has no serialized code range.");
                }

                for (var position = 0; position < code.Length;)
                {
                    var instructionLength
                        = TrRmRegisterBodyReader.InstructionLength(
                            code[position], schema);
                    if (code[position] == 0x41)
                    {
                        var malformed = (byte[])artifact.Clone();
                        var operandOffset = sectionOffset + bodyOffset
                            + codeOffset + position + 1;
                        malformed[operandOffset] = (byte)(
                            (malformed[operandOffset] & 0xF0)
                            | (destinationRegister & 0x0F));
                        return malformed;
                    }

                    position += instructionLength;
                }
            }
        }

        throw new InvalidOperationException(
            "The A2EU Body contains no comparison predicate.");
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
