// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string StorageRecordImportKey = "test.native.storage-record.Observe";
    private const string StorageRecordPairImportKey = "test.native.storage-record.ObservePair";
    private const string StorageRecordTypeName = "StorageRecord";
    private const string StorageRecordInitializerName = "storageRecord";
    private static NativeStorageRecordLayoutDefinition StorageRecordLayout { get; } = new(
        StorageRecordTypeName, StorageRecordInitializerName, 16, 8, [
            new("Data", PrimitiveType.NativeStorageAddress, 0, NativeTargetProfile.PointerSize),
            new("ByteExtent", PrimitiveType.UInt32, NativeTargetProfile.PointerSize, sizeof(uint),
                MinimumValue: 1, MaximumValue: 16),
        ], BackingPointerFieldIndex: 0, BackingExtentFieldIndex: 1);

    private static NativeImportCatalog CreateStorageRecordCatalog(bool paired = false,
        NativeStorageRecordLayoutDefinition? layout = null, bool isReadonly = true, string? loanGroup = null)
    {
        var parameterTypes = paired
            ? ImmutableArray.Create(PrimitiveType.NativeStorageAddress, PrimitiveType.NativeStorageAddress)
            : ImmutableArray.Create(PrimitiveType.NativeStorageAddress);
        var parameterContracts = paired
            ? ImmutableArray.Create(NativeImportParameterContract.ReadonlyNativeStorageNoEscape,
                NativeImportParameterContract.ReadonlyNativeStorageNoEscape)
            : ImmutableArray.Create(NativeImportParameterContract.ReadonlyNativeStorageNoEscape);
        var storageRecordParameters = paired
            ? ImmutableArray.Create(
                new NativeStorageRecordParameterDefinition(0, StorageRecordTypeName, isReadonly, "shared-backing"),
                new NativeStorageRecordParameterDefinition(1, StorageRecordTypeName, isReadonly, "shared-backing"))
            : ImmutableArray.Create(
                new NativeStorageRecordParameterDefinition(0, StorageRecordTypeName, isReadonly, loanGroup));
        var key = paired ? StorageRecordPairImportKey : StorageRecordImportKey;
        var exportName = paired ? "ObserveStorageRecordPair" : "ObserveStorageRecord";
        var import = new NativeImportDefinition(key, "native-storage-record-probe.dll", exportName,
            parameterTypes, PrimitiveType.Int32, true, false, false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = parameterContracts,
            StorageRecordParameters = storageRecordParameters,
        };
        return NativeImportCatalog.CreateForTesting("test.native-storage-record.v1", [import],
            fixedArrayStorageLayouts: [
                new(nameof(PrimitiveType.UInt32), 4, sizeof(uint), sizeof(uint), 4 * sizeof(uint)),
            ], storageRecordLayouts: [layout ?? StorageRecordLayout]);
    }

    private static Compilation CompileStorageRecord(string body, bool paired = false)
    {
        var key = paired ? StorageRecordPairImportKey : StorageRecordImportKey;
        var name = paired ? "ObserveStorageRecordPair" : "ObserveStorageRecord";
        var parameterList = paired
            ? "Terrum.Interop.NativeStorageAddress first, Terrum.Interop.NativeStorageAddress second"
            : "Terrum.Interop.NativeStorageAddress value";
        var source = $$"""
            [Terrum.Interop.NativeImport("{{key}}")]
            [NoThrow]
            extern int32 {{name}}({{parameterList}});
            int32 Main() {
            {{body}}
            }
            """;
        return Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            CreateStorageRecordCatalog(paired), BoundRuntimePolicy.FailFast);
    }

    [Fact]
    public void CataloguedStorageRecordUsesExactLayoutAndReadonlyBackingLoan()
    {
        var compilation = CompileStorageRecord("""
            InlineArray<uint32, 4> backing = [1, 2, 3, 4];
            StorageRecord record = storageRecord(&backing[0], 16);
            return ObserveStorageRecord(&record);
            """);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = program.Functions[program.EntryPoint];
        var record = Assert.IsType<BoundLocal>(((BoundBlock)function.Body).Statements[1]);
        Assert.Equal(16, function.Variables[record.Variable].NativeStorageRecordLayout?.ByteExtent);
        Assert.Equal(new NativeStorageLoan(0, PrimitiveType.NativeFixedArray, PrimitiveType.UInt32, true, 16),
            function.Variables[record.Variable].NativeStorageRecordFacts?.Loan);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Fact]
    public void CataloguedStorageRecordRejectsInvalidExtentAndEscapes()
    {
        var invalidExtent = CompileStorageRecord("""
            InlineArray<uint32, 4> backing = [1, 2, 3, 4];
            StorageRecord record = storageRecord(&backing[0], 17);
            return ObserveStorageRecord(&record);
            """);
        Assert.True(invalidExtent.HasErrors);

        var mutable = CompileStorageRecord("""
            InlineArray<uint32, 4> backing = [1, 2, 3, 4];
            mut StorageRecord record = storageRecord(&backing[0], 16);
            return ObserveStorageRecord(&record);
            """);
        Assert.True(mutable.HasErrors);

        var byValue = CompileStorageRecord("""
            InlineArray<uint32, 4> backing = [1, 2, 3, 4];
            StorageRecord record = storageRecord(&backing[0], 16);
            StorageRecord copy = record;
            return ObserveStorageRecord(&copy);
            """);
        Assert.True(byValue.HasErrors);
    }

    [Fact]
    public void CataloguedStorageRecordCatalogRejectsInvalidLayoutsAndLoanContracts()
    {
        foreach (var layout in new[] {
            StorageRecordLayout with { ByteExtent = 12 },
            StorageRecordLayout with { BackingPointerFieldIndex = null },
            StorageRecordLayout with {
                ByteExtent = 8,
                Alignment = sizeof(uint),
                Fields = [
                    new("First", PrimitiveType.UInt32, 0, sizeof(uint)),
                    new("Second", PrimitiveType.UInt32, sizeof(uint), sizeof(uint)),
                ],
                BackingPointerFieldIndex = null,
                BackingExtentFieldIndex = null,
            },
            StorageRecordLayout with { BackingPointerFieldIndex = 1 },
            StorageRecordLayout with {
                Fields = StorageRecordLayout.Fields.SetItem(1,
                    StorageRecordLayout.Fields[1] with { MinimumValue = 17 }),
            },
            StorageRecordLayout with {
                Fields = StorageRecordLayout.Fields.SetItem(1,
                    StorageRecordLayout.Fields[1] with { MaximumValue = (long)uint.MaxValue + 1 }),
            },
        })
        {
            _ = Assert.Throws<ArgumentException>(() => CreateStorageRecordCatalog(layout: layout));
        }
        _ = Assert.Throws<ArgumentException>(() => CreateStorageRecordCatalog(isReadonly: false));
        _ = Assert.Throws<ArgumentException>(() => CreateStorageRecordCatalog(loanGroup: "singleton"));
    }

    [Fact]
    public void CataloguedStorageRecordLoanGroupsRequireTheSameBacking()
    {
        const string prefix = """
            InlineArray<uint32, 4> first = [1, 2, 3, 4];
            InlineArray<uint32, 4> second = [5, 6, 7, 8];
            StorageRecord firstRecord = storageRecord(&first[0], 16);
            StorageRecord secondRecord = storageRecord(&SECOND[0], 16);
            return ObserveStorageRecordPair(&firstRecord, &secondRecord);
            """;
        var sharedBacking = CompileStorageRecord(prefix.Replace("SECOND", "first", StringComparison.Ordinal),
            paired: true);
        Assert.False(sharedBacking.HasErrors, string.Join(Environment.NewLine, sharedBacking.Diagnostics));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(
            Assert.IsType<BoundProgram>(sharedBacking.Program), NativeRuntimePolicy.FailFast);

        var distinctBacking = CompileStorageRecord(prefix, paired: true);
        Assert.True(distinctBacking.HasErrors);
    }

    [Fact]
    public void SemanticVerifierRederivesCataloguedStorageRecordFacts()
    {
        var compilation = CompileStorageRecord("""
            InlineArray<uint32, 4> backing = [1, 2, 3, 4];
            StorageRecord record = storageRecord(&backing[0], 16);
            return ObserveStorageRecord(&record);
            """);
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = program.Functions[program.EntryPoint];
        var body = Assert.IsType<BoundBlock>(function.Body);
        var local = Assert.IsType<BoundLocal>(body.Statements[1]);
        var initializer = Assert.IsType<BoundNativeStorageRecordInitialization>(local.Initializer);
        var forgedInitializer = initializer with {
            Fields = initializer.Fields.SetItem(0, (BoundConstant)initializer.Fields[0] with { Value = 17 }),
        };
        var forgedBody = body with {
            Statements = body.Statements.SetItem(1, local with { Initializer = forgedInitializer }),
        };
        var forgedFunction = function with { Body = forgedBody };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, forgedFunction),
        };
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(forgedProgram, NativeRuntimePolicy.FailFast));
    }
}
