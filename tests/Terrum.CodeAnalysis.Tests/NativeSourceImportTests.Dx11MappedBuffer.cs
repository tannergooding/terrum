// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string MappedPixelEvidenceSource => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
        "samples", "design", "win32-dx11-triangle", "window-device-warp-resize-mapped-pixel-evidence.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string HardwareMappedPixelEvidenceSource => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
        "samples", "design", "win32-dx11-triangle",
        "window-device-hardware-resize-mapped-pixel-evidence.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void AssertMappedPixelEvidenceRejected(string source)
    {
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void Dx11MappedBufferComMethodsHaveCatalogDrivenDirectBindings()
    {
        var program = CompileSourceAuthoredResizeRender(hardware: false);
        var descriptor = new BoundStruct("Triangle.Interop.D3D11_TEXTURE2D_DESC",
            [.. Enumerable.Range(0, 11).Select(index =>
                new BoundStructField($"Field{index}", PrimitiveType.UInt32, index * sizeof(uint), sizeof(uint)))],
            44);
        var mapped = new BoundStruct("Triangle.Interop.D3D11_MAPPED_SUBRESOURCE", [
            new BoundStructField("DataPointer", PrimitiveType.Int64, 0, sizeof(long)),
            new BoundStructField("RowPitch", PrimitiveType.UInt32, sizeof(long), sizeof(uint)),
            new BoundStructField("DepthPitch", PrimitiveType.UInt32, sizeof(long) + sizeof(uint), sizeof(uint)),
        ], 16);
        var structures = program.Structures.Add(descriptor).Add(mapped);
        var __nunitAssertValue0 = program.NativeImportCatalog;
        Assert.That(__nunitAssertValue0, Is.TypeOf<NativeImportCatalog>());
        var catalog = ((NativeImportCatalog)__nunitAssertValue0);
        Assert.That(NativeMappedBufferContract.TryGetDefinition(
            (PrimitiveType)(100 + structures.Length - 1), catalog, structures, out var mappedMethod,
            out var mappedDefinition), Is.True);
        Assert.That(mappedMethod, Is.Not.Null);
        Assert.That(mappedDefinition, Is.Not.Null);
        Assert.That(mappedMethod, Is.SameAs(NativeDx11ComCatalog.Method(NativeDx11ComCatalog.MapIdentity)));
        Assert.That(mappedDefinition.OutputParameterIndex, Is.EqualTo(5));
        Assert.That(mappedDefinition.SourceType, Is.EqualTo("D3D11_MAPPED_SUBRESOURCE"));
        Assert.That(mappedDefinition.PointerOffset, Is.EqualTo(0));
        Assert.That(mappedDefinition.LengthOffset, Is.EqualTo(12));
        Assert.That(mappedDefinition.UnmapMethodIdentity, Is.EqualTo(NativeDx11ComCatalog.UnmapIdentity));
        Assert.That(mappedDefinition.ReadOnlyMapArguments, Is.EqualTo(new NativeComMappedBufferReadParameter[] { new(3, 1), new(4, 0) }));
        var functions = NativeComCallContract.DirectMethodFunctions(catalog,
            structures, new HashSet<string>(StringComparer.Ordinal) {
                "TryCreateTexture2D", "GetDesc", "CopyResource", "Map", "Unmap",
            });
        var getDescription = NativeDx11ComCatalog.Method(NativeDx11ComCatalog.Texture2DGetDescIdentity);
        Assert.That(getDescription.Slot, Is.EqualTo(10));
        Assert.That(getDescription.Signature, Is.EqualTo("void(ID3D11Texture2D*,D3D11_TEXTURE2D_DESC*)"));
        Assert.That(getDescription.Abi, Is.EqualTo("windows-x64-rcx-rdx-void"));
        Assert.That(getDescription.Effects, Is.EqualTo(NativeComEffects.None));
        var getViewports = NativeDx11ComCatalog.Method(NativeDx11ComCatalog.RSGetViewportsIdentity);
        Assert.That(getViewports.Slot, Is.EqualTo(95));
        Assert.That(getViewports.Signature, Is.EqualTo("void(ID3D11DeviceContext*,UINT*,D3D11_VIEWPORT*)"));
        Assert.That(getViewports.Abi, Is.EqualTo("windows-x64-rcx-rdx-r8-void"));
        Assert.That(getViewports.Effects, Is.EqualTo(NativeComEffects.None));

        Assert.That(functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.CreateTexture2DIdentity));
        Assert.That(functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.Texture2DGetDescIdentity));
        Assert.That(functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.CopyResourceIdentity));
        Assert.That(functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.MapIdentity));
        Assert.That(functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.UnmapIdentity));
    }

    [Test]
    public void Dx11SourceAuthoredMappedPixelEvidenceCompiles()
    {
        var source = MappedPixelEvidenceSource;
        Assert.That(source.Contains("dx11hiddenresizepixelevidencebegin", StringComparison.Ordinal), Is.False);
        Assert.That(source.Contains("nativebyte(mapped,", StringComparison.Ordinal), Is.True);
        Assert.That(source.Contains("context.Unmap(staging, 0)", StringComparison.Ordinal), Is.True);
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue1 = compilation.Program;
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue1);
        Assert.That(program.Functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.Texture2DGetDescIdentity));
        Assert.That(program.Functions, Has.Some.Matches(function =>
            function.NativeComMethod?.Identity == NativeDx11ComCatalog.RSGetViewportsIdentity));
        SemanticVerifier.VerifyReferences(program);
        Assert.That(NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [Test]
    public void Dx11SourceAuthoredHardwareMappedPixelEvidenceCompilesWithoutWarpFallback()
    {
        var source = HardwareMappedPixelEvidenceSource;
        Assert.That(source.Contains("trycreatehardwareswapchain(Handle,", StringComparison.Ordinal), Is.True);
        Assert.That(source.Contains("trycreatewarpswapchain", StringComparison.Ordinal), Is.False);
        Assert.That(source.Contains("nativebyte(mapped,", StringComparison.Ordinal), Is.True);
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue2 = compilation.Program;
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue2);
        Assert.That(program.Functions[0].Intrinsic, Is.EqualTo(BoundIntrinsic.Dx11HardwareTryCreate));
        SemanticVerifier.VerifyReferences(program);
        Assert.That(NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
    }

    [TestCase("if (!context.Map(staging, 0, 1, 0, &mapped)) {",
        "if (context.Map(staging, 0, 1, 0, &mapped)) {")]
    [TestCase("context.Unmap(staging, 0);", "context.Unmap(staging, 1);")]
    [TestCase("context.Unmap(staging, 0);", "context.ClearState();")]
    public void Dx11MappedBufferReadRejectsInvalidMapLifetime(string original, string replacement)
    {
        var source = MappedPixelEvidenceSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        AssertMappedPixelEvidenceRejected(source.Replace(original, replacement, StringComparison.Ordinal));
    }

    [Test]
    public void Dx11MappedBufferReadRejectsAWritableMapMode()
    {
        const string readOnlyMap = "context.Map(staging, 0, 1, 0, &mapped)";
        var source = MappedPixelEvidenceSource;
        Assert.That(source.Contains(readOnlyMap, StringComparison.Ordinal), Is.True);
        AssertMappedPixelEvidenceRejected(source.Replace(readOnlyMap,
            "context.Map(staging, 0, 2, 0, &mapped)", StringComparison.Ordinal));
    }

    [Test]
    public void Dx11MappedBufferCatalogRejectsAReadModeArgumentThatAliasesTheOutput()
    {
        var program = CompileSourceAuthoredResizeRender(hardware: false);
        var __nunitAssertValue3 = program.NativeImportCatalog;
        Assert.That(__nunitAssertValue3, Is.TypeOf<NativeImportCatalog>());
        var catalog = ((NativeImportCatalog)__nunitAssertValue3);
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(catalog.ComMethods, method => method.Identity == NativeDx11ComCatalog.MapIdentity));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var mapMethod = __nunitAssertValue4[0];
        var __nunitAssertValue5 = mapMethod.MappedBufferOutput;
        Assert.That(__nunitAssertValue5, Is.TypeOf<NativeComMappedBufferDefinition>());
        var mappedBuffer = ((NativeComMappedBufferDefinition)__nunitAssertValue5);
        var malformedMethod = mapMethod with {
            MappedBufferOutput = mappedBuffer with {
                ReadOnlyMapArguments = [new(mappedBuffer.OutputParameterIndex, 0)],
            },
        };
        var methods = catalog.ComMethods.Select(method => method.Identity == mapMethod.Identity
            ? malformedMethod : method);
        var exception = Assert.Throws<ArgumentException>(() => new NativeImportCatalog(catalog.Identity,
            catalog.ImportDefinitions, comInterfaces: catalog.ComInterfaces, comOwners: catalog.ComOwners,
            comMethods: [.. methods], comBaseEdges: catalog.ComBaseEdges,
            comSignatureTypes: catalog.ComSignatureTypes));
        Assert.That(exception.Message.Contains("invalid mapped-buffer metadata", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void Dx11MappedBufferReadRejectsReadsOutsideTheMappedTryFinally()
    {
        var source = MappedPixelEvidenceSource;
        var mapIndex = source.IndexOf("if (!context.Map(staging, 0, 1, 0, &mapped))", StringComparison.Ordinal);
        Assert.That(mapIndex >= 0, Is.True);
        var tryIndex = source.IndexOf("try {", mapIndex, StringComparison.Ordinal);
        Assert.That(tryIndex >= 0, Is.True);
        var lineStart = source.LastIndexOf('\n', tryIndex) + 1;
        var indentation = source[lineStart..tryIndex];
        source = source.Insert(lineStart, indentation + "uint32 outsideMappedProbe = nativebyte(mapped, 0);\n");
        AssertMappedPixelEvidenceRejected(source);
    }

    [Test]
    public void Dx11MappedBufferReadRejectsASecondUnmappedAggregate()
    {
        const string centerPixelRead = "nativebyte(mapped, centerOffset)";
        var source = MappedPixelEvidenceSource;
        Assert.That(source.Contains(centerPixelRead, StringComparison.Ordinal), Is.True);
        var mapIndex = source.IndexOf("if (!context.Map(staging, 0, 1, 0, &mapped))", StringComparison.Ordinal);
        Assert.That(mapIndex >= 0, Is.True);
        var lineStart = source.LastIndexOf('\n', mapIndex) + 1;
        var indentation = source[lineStart..mapIndex];
        source = source.Insert(lineStart, indentation
            + "mut D3D11_MAPPED_SUBRESOURCE unrelatedMapped = new D3D11_MAPPED_SUBRESOURCE(0, 0, 0);\n")
            .Replace(centerPixelRead, "nativebyte(unrelatedMapped, centerOffset)", StringComparison.Ordinal);
        AssertMappedPixelEvidenceRejected(source);
    }
}
