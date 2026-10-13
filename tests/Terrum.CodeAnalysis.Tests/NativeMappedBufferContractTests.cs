// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeMappedBufferContractTests
{
    [Fact]
    public void GenericMappedBufferMetadataResolvesForAnUnrelatedNativeApi()
    {
        const string interfaceIdentity = "IFixtureByteReader";
        const string mapIdentity = "fixture.byte-reader.map";
        const string unmapIdentity = "fixture.byte-reader.unmap";
        const string releaseIdentity = "fixture.byte-reader.release";
        var interfaceDefinition = new NativeComInterfaceDefinition(
            interfaceIdentity, Guid.Parse("77937FC4-59B4-44E4-B62C-FDE3301BE924"), null,
            [$"{interfaceIdentity}::Map", $"{interfaceIdentity}::Unmap", $"{interfaceIdentity}::Release"],
            [$"{interfaceIdentity}::Map", $"{interfaceIdentity}::Unmap", $"{interfaceIdentity}::Release"]);
        var acquisitionImport = new NativeImportDefinition(
            "fixture.byte-reader.acquire", "fixture.dll", "AcquireByteReader",
            [PrimitiveType.NativeComInterfaceOutputAddress], PrimitiveType.Int32,
            IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.OwnedNativeComInterfaceOutput],
            ComOutputs = [new(0, interfaceIdentity, NativeComEffects.AcquiresReference)],
        };
        var mapMethod = new NativeComMethodDefinition(
            mapIdentity, interfaceIdentity, interfaceIdentity, 0,
            $"HRESULT({interfaceIdentity}*,{interfaceIdentity}*,UINT,MAPPED_BYTES*)",
            "windows-x64-rcx-rdx-r8d-r9-eax", NativeComEffects.SignedHResult, IsCallable: true) {
            VTableMethodIdentity = $"{interfaceIdentity}::Map",
            BorrowedOwnerParameters = [1],
            ParameterAdaptations = [
                new(3, "MAPPED_BYTES", "MAPPED_BYTES*",
                    NativeComParameterAdaptationKind.AggregateStorageAddress, 16, 8, NoEscape: true),
            ],
            MappedBufferOutput = new NativeComMappedBufferDefinition(
                3, "MAPPED_BYTES", 0, 8, unmapIdentity) {
                ReadOnlyMapArguments = [new(2, 1)],
            },
        };
        var unmapMethod = new NativeComMethodDefinition(
            unmapIdentity, interfaceIdentity, interfaceIdentity, 1,
            $"void({interfaceIdentity}*,{interfaceIdentity}*,UINT)",
            "windows-x64-rcx-rdx-r8d-void", NativeComEffects.None, IsCallable: true) {
            VTableMethodIdentity = $"{interfaceIdentity}::Unmap",
            BorrowedOwnerParameters = [1],
        };
        var releaseMethod = new NativeComMethodDefinition(
            releaseIdentity, interfaceIdentity, interfaceIdentity, 2,
            $"ULONG({interfaceIdentity}*)", "windows-x64-rcx-eax",
            NativeComEffects.ReleasesReference, IsCallable: true) {
            VTableMethodIdentity = $"{interfaceIdentity}::Release",
        };
        var owner = new NativeComOwnerDefinition(
            "Fixture.ByteReader", interfaceIdentity, NativeComOwnerAcquisitionKind.NativeImportOutput,
            acquisitionImport.Key, 0, true, sizeof(long), sizeof(long)) {
            ReleaseMethodIdentity = releaseIdentity,
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-mapped-buffer.v1", [acquisitionImport], [interfaceDefinition], [owner],
            [mapMethod, unmapMethod, releaseMethod], []);
        var ownerStructure = new BoundStruct(owner.BoundOwnerName, [], sizeof(long)) {
            IsUnique = true,
            CleanupMethod = owner.BoundOwnerName + ".Close",
        };
        var mappedStructure = new BoundStruct("Fixture.MAPPED_BYTES", [
            new BoundStructField("Address", PrimitiveType.Int64, 0, sizeof(long)),
            new BoundStructField("Length", PrimitiveType.UInt32, sizeof(long), sizeof(uint)),
        ], 16);
        var structures = new[] { ownerStructure, mappedStructure };

        Assert.True(NativeMappedBufferContract.TryGetDefinition((PrimitiveType)101,
            catalog, structures, out var selectedMap, out var mappedBuffer));
        Assert.Same(mapMethod, selectedMap);
        Assert.NotNull(mappedBuffer);
        Assert.Equal(3, mappedBuffer.OutputParameterIndex);
        Assert.Equal("MAPPED_BYTES", mappedBuffer.SourceType);
        Assert.Equal(0, mappedBuffer.PointerOffset);
        Assert.Equal(8, mappedBuffer.LengthOffset);
        Assert.Equal(unmapIdentity, mappedBuffer.UnmapMethodIdentity);

        var functions = NativeComCallContract.DirectMethodFunctions(catalog, [.. structures],
            new HashSet<string>(StringComparer.Ordinal) { "Map", "Unmap" });
        var mapFunction = Assert.Single(functions,
            function => function.NativeComMethod?.Identity == mapIdentity);
        Assert.Equal("Fixture.ByteReader.Map", mapFunction.Name);
        Assert.Equal(PrimitiveType.NativeStorageAddress, mapFunction.Variables[3].Type);
        Assert.Contains(functions, function => function.NativeComMethod?.Identity == unmapIdentity);
    }
}
