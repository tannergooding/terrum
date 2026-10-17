// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string GenericFixedArrayStorageKey =
        "test.sample-array.generic-storage.NativeFfiFixedArrayStorage";
    private static NativeImportCatalog SampleArrayCatalog { get; } =
        NativeImportCatalog.CreateForTesting("test.generic-sample-array.v1", []);

    private static readonly string SampleArrayDeclarations = $$"""
        [Blittable] [Layout(Sequential)]
        struct Vertex {
            float32 X;
            float32 Y;
            float32 Z;
            public Vertex(float32 x, float32 y, float32 z) {
                X = x;
                Y = y;
                Z = z;
            }
            public float32 GetX() { return X; }
            public float32 GetY() { return Y; }
            public float32 GetZ() { return Z; }
        }

        [NoThrow]
        int32 SampleIndex(int32 value) { return value; }
        """;

    private static Compilation CompileSampleArray(string body,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast, string? declarations = null) =>
        CompileSampleArraySource(
            (declarations ?? SampleArrayDeclarations) + "\nint32 Main() {\n" + body + "\n}", policy);

    private static Compilation CompileSampleArraySource(string source,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast) =>
        Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], SampleArrayCatalog, policy);

    private static Compilation CompileGenericFixedArrayStorage(string body, string elementTypeName,
        int elementCount, int elementStride, int byteExtent, int elementAlignment = 4,
        string? declarations = null)
    {
        var import = new NativeImportDefinition(GenericFixedArrayStorageKey, "native-import-abi-probe.dll",
            "NativeFfiFixedArrayStorage", [PrimitiveType.NativeStorageAddress], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyNativeStorageNoEscape],
            StorageParameters = [
                new(0, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray,
                    byteExtent, IsReadonly: true, RequiredStartIndex: 0, ElementTypeName: elementTypeName,
                    ElementCount: elementCount,
                    ElementStride: elementStride, ElementAlignment: elementAlignment),
            ],
        };
        var catalog = NativeImportCatalog.CreateForTesting("test.generic-fixed-array-storage.v1", [import]);
        var source = (declarations ?? SampleArrayDeclarations) + $$"""

            [Terrum.Interop.NativeImport("{{GenericFixedArrayStorageKey}}")]
            [NoThrow]
            extern int32 NativeFfiFixedArrayStorage(Terrum.Interop.NativeStorageAddress vertices);
            int32 Main() {
            {{body}}
            }
            """;
        return Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
            BoundRuntimePolicy.FailFast);
    }

    [Test]
    public async Task SampleArraysHaveExactLayoutsAndExecuteClosedCopiedReads()
    {
        const string body = """
            InlineArray<uint32, 1> levels = [uint32(0xB000)];
            InlineArray<Vertex, 3> vertices = [
                new Vertex(0.25, -0.5, 0.75),
                new Vertex(1.25, 1.5, -1.75),
                new Vertex(2.25, 2.5, 2.75)];
            InlineArray<float32, 4> values = [1.0, -0.0, 0.0 / 0.0, 4.5];
            Vertex copied = vertices[1];
            if (levels[0] != uint32(0xB000)
                || copied.GetX() != 1.25 || copied.GetY() != 1.5 || copied.GetZ() != -1.75
                || values[0] != 1.0 || values[1] != 0.0 || (1.0 / values[1]) >= 0.0
                || values[2] == values[2] || values[3] != 4.5) {
                return 1;
            }
            return 0;
            """;
        var compilation = CompileSampleArray(body);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(NativeFixedArrayContract.MatchesProgram(program), Is.True);

        Assert.That(ValueLayout.MaximumSize, Is.EqualTo(16));
        var entryFunction = program.Functions[program.EntryPoint];
        var levelLayout = entryFunction.Variables.Single(static variable => variable.Name == "levels")
            .NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The uint32 array must carry its selected layout.");
        Assert.That(levelLayout.ElementCount, Is.EqualTo(1));
        Assert.That(levelLayout.ElementStride, Is.EqualTo(4));
        Assert.That(levelLayout.ByteExtent, Is.EqualTo(4));
        Assert.That(levelLayout.Alignment, Is.EqualTo(4));
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(program.Structures);
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var vertex = __nunitAssertValue1[0];
        Assert.That(vertex.IsSequential, Is.True);
        Assert.That(vertex.Size, Is.EqualTo(12));
        Assert.That(vertex.Fields.Select(static field => field.Name), Is.EqualTo(new[] { "X", "Y", "Z" }));
        Assert.Multiple(() => {
            foreach (var field in vertex.Fields)
            {
                Assert.That(field.Type, Is.EqualTo(PrimitiveType.Float32));
                Assert.That(field.IsPrivate, Is.True);
            }
        });
        Assert.That(vertex.Fields.Select(static field => field.Offset), Is.EqualTo(new[] { 0, 4, 8 }));
        var vertexLayout = entryFunction.Variables.Single(static variable => variable.Name == "vertices")
            .NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The vertex array must carry its selected layout.");
        Assert.That(vertexLayout.ElementCount, Is.EqualTo(3));
        Assert.That(vertexLayout.ElementStride, Is.EqualTo(12));
        Assert.That(vertexLayout.ByteExtent, Is.EqualTo(36));
        Assert.That(vertexLayout.Alignment, Is.EqualTo(4));
        var floatLayout = entryFunction.Variables.Single(static variable => variable.Name == "values")
            .NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The float array must carry its selected layout.");
        Assert.That(floatLayout.ElementCount, Is.EqualTo(4));
        Assert.That(floatLayout.ElementStride, Is.EqualTo(4));
        Assert.That(floatLayout.ByteExtent, Is.EqualTo(16));
        Assert.That(floatLayout.Alignment, Is.EqualTo(4));
        var __nunitAssertValue2 = program.Functions[program.EntryPoint].Body.Statements[0];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundLocal>());
        var __nunitAssertValue3 = (((BoundLocal)__nunitAssertValue2)).Initializer;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var levelInitializer = ((BoundNativeFixedArrayInitialization)__nunitAssertValue3);
        var __nunitAssertValue4 = levelInitializer.Elements[0];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundConstant>());
        Assert.That((((BoundConstant)__nunitAssertValue4)).Value, Is.EqualTo(0xB000L));

        var executable = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(await RunExecutableAsync(executable), Is.EqualTo(0));
        _ = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() => NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            ModuleCompiler.CreateProducer(program with { EntryPoint = -1 }, ["Main"]));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Main"], "SampleArray", NativeRuntimePolicy.FailFast));
        _ = Assert.Throws<NotSupportedException>(() =>
            TrRmExecutableCompiler.Emit(program with { EntryPoint = -1 }, "sample-array"));

        Assert.That(NativeFixedArrayContract.SupportsRuntimePolicy(
            SampleArrayCatalog, BoundRuntimePolicy.FailFast), Is.True);
        Assert.That(CompileSampleArray(
            "InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; return 0;",
            BoundRuntimePolicy.CheckedExceptions).Program, Is.Not.Null);
        Assert.That(Compilation.Create(SyntaxTree.Parse(
            SampleArrayDeclarations + "\nint32 Main() { " + body + " }")).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(SampleArrayDeclarations + "\nint32 Main() { " + body + " }")],
            NativeImportCatalog.WindowsX64, BoundRuntimePolicy.FailFast).Program, Is.Not.Null);

        var stripped = program with {
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            Functions = program.Functions.Select(static function =>
                function with { NativeImportKey = null }).ToImmutableArray(),
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            SemanticVerifier.RejectSourceNativeImports(stripped, "JIT"));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitCode(stripped, NativeRuntimePolicy.FailFast));
    }

    [Test]
    public async Task SampleArrayInitializersAndIndicesPreserveSourceOrderAndEvaluateOnce()
    {
        const string body = """
            mut int32 next = 0;
            int32 before = 12345;
            InlineArray<Vertex, 3> vertices = [
                new Vertex(float32(next++), float32(next++), float32(next++)),
                new Vertex(float32(next++), float32(next++), float32(next++)),
                new Vertex(float32(next++), float32(next++), float32(next++))];
            int32 after = 67890;
            InlineArray<float32, 4> values = [17.0, 18.0, 19.0, 20.0];
            float32 copied = values[SampleIndex(next++ - 9)];
            if (next != 10 || vertices[0].GetX() != 0 || vertices[0].GetY() != 1
                || vertices[0].GetZ() != 2 || vertices[1].GetX() != 3
                || vertices[1].GetY() != 4 || vertices[1].GetZ() != 5
                || vertices[2].GetX() != 6 || vertices[2].GetY() != 7
                || vertices[2].GetZ() != 8 || copied != 17.0
                || before != 12345 || after != 67890) {
                return 1;
            }
            return 0;
            """;
        var compilation = CompileSampleArray(body);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue5 = compilation.Program;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundProgram>());
        var executable = NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue5), NativeRuntimePolicy.FailFast);
        Assert.That(await RunExecutableAsync(executable), Is.EqualTo(0));
    }

    [Test]
    public async Task SampleArraysResolveRequiredConstantsInInitializersAndIndices()
    {
        const string body = """
            InlineArray<float32, 4> values = [float32(const int32(1)), 2.0, 3.0, 4.0];
            float32 copied = values[const (0)];
            return copied == 1.0 ? 0 : 1;
            """;
        var compilation = CompileSampleArray(body);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue6 = compilation.Program;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue6);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue7 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundLocal>());
        var arrayLocal = ((BoundLocal)__nunitAssertValue7);
        var __nunitAssertValue8 = arrayLocal.Initializer;
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var initializer = ((BoundNativeFixedArrayInitialization)__nunitAssertValue8);
        var __nunitAssertValue9 = initializer.Elements[0];
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundCast>());
        var firstElement = ((BoundCast)__nunitAssertValue9);
        _ = Assert.That(firstElement.Operand, Is.TypeOf<BoundConstant>());
        var __nunitAssertValue10 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundLocal>());
        var copied = ((BoundLocal)__nunitAssertValue10);
        var __nunitAssertValue11 = copied.Initializer;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundNativeFixedArrayElement>());
        var read = ((BoundNativeFixedArrayElement)__nunitAssertValue11);
        var __nunitAssertValue12 = read.Index;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundConstant>());
        Assert.That((((BoundConstant)__nunitAssertValue12)).Value, Is.EqualTo(0));
        var executable = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(await RunExecutableAsync(executable), Is.EqualTo(0));
    }

    [Test]
    public async Task MutableFixedArraysZeroInitializeAndWriteNarrowElements()
    {
        const string source = """
            [NoThrow]
            int32 Main() {
                mut InlineArray<char16, 4> values = [];
                mut uint32 index = 1;
                values[index] = char16(90);
                if (values[0] != char16(0) || values[index] != char16(90)
                    || values[2] != char16(0) || values[3] != char16(0)) {
                    return 1;
                }
                return 0;
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], SampleArrayCatalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue13 = compilation.Program;
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue13);
        var executable = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(await RunExecutableAsync(executable), Is.EqualTo(0));
    }

    [Test]
    public void CataloguedWritableStorageParametersBorrowMutableFixedArrays()
    {
        const string importKey = "test.mutable-char16-array.NativeFfiMutableStorage";
        var import = new NativeImportDefinition(importKey, "native-import-abi-probe.dll",
            "NativeFfiMutableStorage", [PrimitiveType.NativeStorageAddress], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.WritableNativeStorageNoEscape],
            StorageParameters = [
                new(0, PrimitiveType.NativeStorageAddress, PrimitiveType.NativeFixedArray,
                    4 * sizeof(char), IsReadonly: false, RequiredStartIndex: 0,
                    ElementTypeName: nameof(PrimitiveType.Char16), ElementCount: 4,
                    ElementStride: sizeof(char), ElementAlignment: sizeof(char)),
            ],
        };
        var catalog = NativeImportCatalog.CreateForTesting("test.mutable-char16-array-loan.v1", [import]);
        var source = $$"""
            [Terrum.Interop.NativeImport("{{importKey}}")]
            [NoThrow]
            extern int32 NativeFfiMutableStorage(Terrum.Interop.NativeStorageAddress storage);

            [NoThrow]
            int32 Main() {
                mut InlineArray<char16, 4> values = [];
                values[0] = char16(90);
                return NativeFfiMutableStorage(&values[0]);
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue14 = compilation.Program;
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue14);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue15 = main.Body.Statements[^1];
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue16 = (((BoundReturn)__nunitAssertValue15)).Value;
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue16);
        var __nunitAssertValue17 = global::System.Linq.Enumerable.ToArray(call.Arguments);
        Assert.That(__nunitAssertValue17, Has.Exactly(1).Items);
        var __nunitAssertValue18 = __nunitAssertValue17[0];
        Assert.That(__nunitAssertValue18, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue18);
        Assert.That(address.AddressType, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(address.CarrierType, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(address.ElementCount, Is.EqualTo(4));
        Assert.That(address.ElementStride, Is.EqualTo(2));
        Assert.That(address.ByteExtent, Is.EqualTo(8));
        Assert.That(address.IsReadonly, Is.False);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public void Utf16ZStorageParametersBorrowReadonlyFixedArrays()
    {
        const string importKey = "test.utf16z-storage.NativeFfiPath";
        var storageBytes = 4 * sizeof(char);
        var import = new NativeImportDefinition(importKey, "native-import-abi-probe.dll",
            "NativeFfiPath", [PrimitiveType.NativeUtf16ZPointer], PrimitiveType.Int32,
            true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyUtf16ZNoEscape],
            StorageParameters = [
                new(0, PrimitiveType.NativeUtf16ZPointer, PrimitiveType.NativeFixedArray,
                    storageBytes, IsReadonly: true, RequiredStartIndex: 0,
                    ElementTypeName: nameof(PrimitiveType.Char16), ElementCount: 4,
                    ElementStride: sizeof(char), ElementAlignment: sizeof(char)),
            ],
        };
        var catalog = NativeImportCatalog.CreateForTesting("test.utf16z-storage.v1", [import]);
        const string source = $$"""
            [Terrum.Interop.NativeImport("{{importKey}}")]
            [NoThrow]
            extern int32 NativeFfiPath(ptr<char16> path);

            [NoThrow]
            int32 Main() {
                InlineArray<char16, 4> path = [char16(65), char16(66), char16(67), char16(0)];
                return NativeFfiPath(&path[0]);
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue19 = compilation.Program;
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue19);
        var __nunitAssertValue20 = program.Functions[program.EntryPoint].Body.Statements[^1];
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue21 = (((BoundReturn)__nunitAssertValue20)).Value;
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue21);
        var __nunitAssertValue22 = global::System.Linq.Enumerable.ToArray(call.Arguments);
        Assert.That(__nunitAssertValue22, Has.Exactly(1).Items);
        var __nunitAssertValue23 = __nunitAssertValue22[0];
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue23);
        Assert.That(address.Type, Is.EqualTo(PrimitiveType.NativeUtf16ZPointer));
        Assert.That(address.AddressType, Is.EqualTo(PrimitiveType.NativeUtf16ZPointer));
        Assert.That(address.IsReadonly, Is.True);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);

        var invalidStart = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(source.Replace("&path[0]", "&path[1]", StringComparison.Ordinal))],
            catalog, BoundRuntimePolicy.FailFast);
        Assert.That(invalidStart.HasErrors, Is.True);
    }

    [Test]
    public void GenericBoundParametersMapToCataloguedImportContracts()
    {
        const string utf16ImportKey = "test.generic-parameter-map.NativeFfiPath";
        const string utf8ImportKey = "test.generic-parameter-map.NativeFfiEntry";
        var imports = new[] {
            new NativeImportDefinition(utf16ImportKey, "native-import-abi-probe.dll",
                "NativeFfiPath", [PrimitiveType.NativeUtf16ZPointer], PrimitiveType.Void,
                true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
                ParameterContracts = [NativeImportParameterContract.ReadonlyUtf16ZNoEscape],
            },
            new NativeImportDefinition(utf8ImportKey, "native-import-abi-probe.dll",
                "NativeFfiEntry", [PrimitiveType.NativeUtf8ZPointer], PrimitiveType.Void,
                true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
                ParameterContracts = [NativeImportParameterContract.ReadonlyUtf8ZNoEscape],
            },
        };
        var catalog = NativeImportCatalog.CreateForTesting(
            "test.generic-parameter-map.v1", [.. imports]);
        var function = new BoundFunction("ForwardStrings", PrimitiveType.Void, [
            new("path", PrimitiveType.NativeUtf16ZPointer, false, default),
            new("entry", PrimitiveType.NativeUtf8ZPointer, false, default),
        ], 2, new BoundBlock([], default), default) {
            NativeImportParameterMappings = [
                new(utf16ImportKey, 0, 0),
                new(utf8ImportKey, 1, 0),
            ],
        };
        var program = new BoundProgram([function], 0) {
            NativeImportCatalog = catalog,
        };

        Assert.That(NativeStorageParameterContract.IsReadonlyStringParameter(
            function, 0, PrimitiveType.NativeUtf16ZPointer, program), Is.True);
        Assert.That(NativeStorageParameterContract.IsReadonlyStringParameter(
            function, 1, PrimitiveType.NativeUtf8ZPointer, program), Is.True);
        Assert.That(NativeStorageParameterContract.IsReadonlyStringParameter(
            function with {
                NativeImportParameterMappings = [
                    new("test.generic-parameter-map.missing", 0, 0),
                    new(utf8ImportKey, 1, 0),
                ],
            }, 0, PrimitiveType.NativeUtf16ZPointer, program), Is.False);
    }

    [Test]
    public void CataloguedGenericStorageParametersBorrowFixedArrayElements()
    {
        const string body = """
            InlineArray<Vertex, 3> vertices = [
                new Vertex(0.25, -0.5, 0.75),
                new Vertex(1.25, 1.5, -1.75),
                new Vertex(2.25, 2.5, 2.75)];
            return NativeFfiFixedArrayStorage(&vertices[0]);
            """;
        var compilation = CompileGenericFixedArrayStorage(body, "Vertex", 3, 12, 36);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue24 = compilation.Program;
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue24);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue25 = main.Body.Statements[^1];
        Assert.That(__nunitAssertValue25, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue26 = (((BoundReturn)__nunitAssertValue25)).Value;
        Assert.That(__nunitAssertValue26, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue26);
        var __nunitAssertValue27 = global::System.Linq.Enumerable.ToArray(call.Arguments);
        Assert.That(__nunitAssertValue27, Has.Exactly(1).Items);
        var __nunitAssertValue28 = __nunitAssertValue27[0];
        Assert.That(__nunitAssertValue28, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue28);
        Assert.That(address.AddressType, Is.EqualTo(PrimitiveType.NativeStorageAddress));
        Assert.That(address.CarrierType, Is.EqualTo(PrimitiveType.NativeFixedArray));
        Assert.That(address.ElementCount, Is.EqualTo(3));
        Assert.That(address.ElementStride, Is.EqualTo(12));
        Assert.That(address.ByteExtent, Is.EqualTo(36));
        Assert.That(address.IsReadonly, Is.True);

        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("NativeFfiFixedArrayStorage\0"u8) >= 0, Is.True);
    }

    [Test]
    public void CataloguedGenericStorageAcceptsUnrelatedSequentialAggregate()
    {
        const string declarations = """
            namespace Telemetry;
            [Layout(Sequential)]
            struct Measurement {
                int64 Timestamp;
                int64 Value;
                public Measurement(int64 timestamp, int64 value) {
                    Timestamp = timestamp;
                    Value = value;
                }
            }
            """;
        const string body = """
            InlineArray<Measurement, 2> measurements = [
                new Measurement(10, 20), new Measurement(30, 40)];
            return NativeFfiFixedArrayStorage(&measurements[0]);
            """;

        var compilation = CompileGenericFixedArrayStorage(body, "Telemetry.Measurement", 2,
            elementStride: 16, byteExtent: 32, elementAlignment: 8, declarations: declarations);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue29 = compilation.Program;
        Assert.That(__nunitAssertValue29, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue29);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue30 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue30, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue30);
        var layout = main.Variables[local.Variable].NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The selected aggregate array must carry its layout.");
        Assert.That(layout, Is.EqualTo(new BoundNativeFixedArrayLayout(PrimitiveType.NativeFixedArray,
            layout.ElementType, 2, 16, 32, 8)));
        Assert.That(program.Structures[(int)layout.ElementType - 100].Name, Is.EqualTo("Telemetry.Measurement"));
        Assert.That(NativeFixedArrayContract.MatchesLayout(layout, program.Structures,
            program.NativeImportCatalog), Is.True);
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [Test]
    public async Task CataloguedFixedArrayLayoutsAcceptUnrelatedSequentialAggregate()
    {
        const string declarations = """
            namespace Telemetry;
            [Layout(Sequential)]
            public struct Measurement {
                public int64 Timestamp;
                public int64 Value;
                public Measurement(int64 timestamp, int64 value) {
                    Timestamp = timestamp;
                    Value = value;
                }
            }
            """;
        const string source = """
            int32 Main() {
                InlineArray<Measurement, 2> measurements = [
                    new Measurement(10, 20), new Measurement(30, 40)];
                return measurements[1].Value == 40 ? 0 : 1;
            }
            """;
        var catalog = NativeImportCatalog.CreateForTesting("test.catalogued-fixed-array-layout.v1", [],
            fixedArrayStorageLayouts: [
                new("Telemetry.Measurement", 2, 16, 8, 32),
            ]);
        var compilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(declarations + "\n" + source)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue31 = compilation.Program;
        Assert.That(__nunitAssertValue31, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue31);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue32 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue32, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue32);
        var layout = main.Variables[local.Variable].NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The selected aggregate array must carry its layout.");
        Assert.That(layout.ElementCount, Is.EqualTo(2));
        Assert.That(layout.ElementStride, Is.EqualTo(16));
        Assert.That(layout.ByteExtent, Is.EqualTo(32));
        Assert.That(NativeFixedArrayContract.MatchesLayout(layout, program.Structures, catalog), Is.True);
        Assert.That(await RunExecutableAsync(
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast)), Is.EqualTo(0));
    }

    [TestCase(0, 4, 4, 0)]
    [TestCase(2, 0, 4, 0)]
    [TestCase(2, 8, 3, 16)]
    [TestCase(2, 8, 4, 15)]
    [TestCase(513, 8, 4, 4104)]
    public void CataloguedFixedArrayLayoutsRejectInvalidGeometry(int elementCount, int elementStride,
        int elementAlignment, int byteExtent)
    {
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.invalid-fixed-array-layout.v1", [],
            fixedArrayStorageLayouts: [
                new("Telemetry.Measurement", elementCount, elementStride, elementAlignment, byteExtent),
            ]));
    }

    [Test]
    public void CataloguedFixedArrayLayoutsAllowTheMaximumExtent()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test.maximum-fixed-array-extent.v1", [],
            fixedArrayStorageLayouts: [
                new(nameof(PrimitiveType.Char16), 2048, sizeof(char), sizeof(char),
                    NativeFixedArrayContract.MaximumByteExtent),
            ]);
        var __nunitAssertValue33 = global::System.Linq.Enumerable.ToArray(catalog.FixedArrayStorageLayouts);
        Assert.That(__nunitAssertValue33, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue33[0].ByteExtent, Is.EqualTo(NativeFixedArrayContract.MaximumByteExtent));
    }

    [Test]
    public void CataloguedFixedArrayLayoutsRejectRecursiveAggregatesBeforeAlignment()
    {
        var recursiveType = (PrimitiveType)100;
        var structures = ImmutableArray.Create(new BoundStruct("Telemetry.Node",
            [new BoundStructField("Next", recursiveType, 0, 8)], 8) {
            IsSequential = true,
        });
        var catalog = NativeImportCatalog.CreateForTesting("test.recursive-fixed-array-layout.v1", [],
            fixedArrayStorageLayouts: [
                new("Telemetry.Node", 1, 8, 8, 8),
            ]);

        Assert.That(NativeFixedArrayContract.TryGetLayout(PrimitiveType.NativeFixedArray,
            recursiveType, 1, structures, catalog, out _), Is.False);
        Assert.That(NativeFixedArrayContract.IsSupportedElementType(recursiveType, structures, catalog), Is.False);
    }

    [TestCase("Other.Measurement", 2, 16, 32, 8)]
    [TestCase("Telemetry.Measurement", 1, 16, 16, 8)]
    [TestCase("Telemetry.Measurement", 2, 8, 16, 8)]
    [TestCase("Telemetry.Measurement", 2, 16, 32, 4)]
    [TestCase("Telemetry.Measurement", 2, 12, 24, 4)]
    public void CataloguedGenericStorageRejectsMismatchedAggregateMetadata(string elementTypeName,
        int elementCount, int elementStride, int byteExtent, int elementAlignment)
    {
        const string declarations = """
            namespace Telemetry;
            [Layout(Sequential)]
            struct Measurement {
                int64 Timestamp;
                int64 Value;
                public Measurement(int64 timestamp, int64 value) {
                    Timestamp = timestamp;
                    Value = value;
                }
            }
            """;
        const string body = """
            InlineArray<Measurement, 2> measurements = [
                new Measurement(10, 20), new Measurement(30, 40)];
            return NativeFfiFixedArrayStorage(&measurements[0]);
            """;

        var compilation = CompileGenericFixedArrayStorage(body, elementTypeName,
            elementCount, elementStride, byteExtent, elementAlignment, declarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void CataloguedGenericStorageRejectsAutoLayoutAggregates()
    {
        const string declarations = """
            namespace Telemetry;
            struct Measurement {
                int64 Timestamp;
                int64 Value;
                public Measurement(int64 timestamp, int64 value) {
                    Timestamp = timestamp;
                    Value = value;
                }
            }
            """;
        const string body = """
            InlineArray<Measurement, 2> measurements = [
                new Measurement(10, 20), new Measurement(30, 40)];
            return NativeFfiFixedArrayStorage(&measurements[0]);
            """;

        var compilation = CompileGenericFixedArrayStorage(body, "Telemetry.Measurement",
            2, 16, 32, elementAlignment: 8, declarations: declarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void CataloguedGenericStorageUsesSelectedArrayGeometry()
    {
        const string body = """
            InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0];
            return NativeFfiFixedArrayStorage(&values[0]);
            """;
        var compilation = CompileGenericFixedArrayStorage(body, "Float32", 4, 4, 16);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue34 = compilation.Program;
        Assert.That(__nunitAssertValue34, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue34);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue35 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue35, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue35);
        var layout = main.Variables[local.Variable].NativeFixedArrayLayout
            ?? throw new InvalidOperationException("The selected float array must carry its layout.");
        Assert.That(layout, Is.EqualTo(new BoundNativeFixedArrayLayout(PrimitiveType.NativeFixedArray,
            PrimitiveType.Float32, 4, 4, 16, 4)));
        var __nunitAssertValue36 = main.Body.Statements[1];
        Assert.That(__nunitAssertValue36, Is.TypeOf<BoundReturn>());
        var __nunitAssertValue37 = (((BoundReturn)__nunitAssertValue36)).Value;
        Assert.That(__nunitAssertValue37, Is.TypeOf<BoundCall>());
        var call = ((BoundCall)__nunitAssertValue37);
        var __nunitAssertValue38 = global::System.Linq.Enumerable.ToArray(call.Arguments);
        Assert.That(__nunitAssertValue38, Has.Exactly(1).Items);
        var __nunitAssertValue39 = __nunitAssertValue38[0];
        Assert.That(__nunitAssertValue39, Is.TypeOf<BoundNativeStorageAddress>());
        var address = ((BoundNativeStorageAddress)__nunitAssertValue39);
        Assert.That(new BoundNativeFixedArrayLayout(address.CarrierType, address.ElementType,
            address.ElementCount, address.ElementStride, address.ByteExtent, layout.Alignment), Is.EqualTo(layout));
        _ = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
    }

    [TestCase("return NativeFfiFixedArrayStorage(&vertices[1]);")]
    [TestCase("mut int32 index = 0; return NativeFfiFixedArrayStorage(&vertices[index]);")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; return NativeFfiFixedArrayStorage(&values[0]);")]
    public void CataloguedGenericStorageParametersRejectWrongFixedArrayPlaces(string body)
    {
        var compilation = CompileGenericFixedArrayStorage(
            "InlineArray<Vertex, 3> vertices = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)];\n"
            + body, "Vertex", 3, 12, 36);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("-1")]
    [TestCase("4")]
    [TestCase("-2147483648")]
    [TestCase("2147483647")]
    public void SampleArrayConstantIndicesRejectEveryOutOfRangeBoundary(string index)
    {
        var compilation = CompileSampleArray(
            $"InlineArray<float32, 4> values = [0.0, 0.0, 0.0, 0.0]; return values[{index}] == 0.0 ? 0 : 1;");
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("-1")]
    [TestCase("4")]
    [TestCase("-2147483648")]
    [TestCase("2147483647")]
    public async Task SampleArrayDynamicIndicesFailFastAtEveryOutOfRangeBoundary(string index)
    {
        var compilation = CompileSampleArray(
            $"InlineArray<float32, 4> values = [0.0, 0.0, 0.0, 0.0]; " +
            $"return values[SampleIndex({index})] == 0.0 ? 0 : 1;");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue40 = compilation.Program;
        Assert.That(__nunitAssertValue40, Is.TypeOf<BoundProgram>());
        var executable = NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue40), NativeRuntimePolicy.FailFast);
        Assert.That(await RunExecutableAsync(executable), Is.EqualTo(unchecked((int)0xE0540004)));
    }

    [Test]
    public async Task SampleArrayNoThrowUsesFailFastBoundsChecksAndDynamicIndexRunsOnce()
    {
        var declarations = SampleArrayDeclarations + """

            [NoThrow]
            int32 Main() {
                InlineArray<float32, 4> values = [7.0, 8.0, 9.0, 10.0];
                return values[3] == 10.0 ? 0 : 1;
            }
            """;
        var noThrow = Compilation.CreateWithNativeImports([SyntaxTree.Parse(declarations)],
            SampleArrayCatalog, BoundRuntimePolicy.FailFast);
        Assert.That(noThrow.HasErrors, Is.False, string.Join(Environment.NewLine, noThrow.Diagnostics));
        var __nunitAssertValue41 = noThrow.Program;
        Assert.That(__nunitAssertValue41, Is.TypeOf<BoundProgram>());
        Assert.That(await RunExecutableAsync(NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue41), NativeRuntimePolicy.FailFast)), Is.EqualTo(0));

        var dynamicNoThrow = CompileSampleArraySource(SampleArrayDeclarations +
            "\n[NoThrow] int32 Main() { InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; " +
            "return values[SampleIndex(0)] == 1.0 ? 0 : 1; }");
        Assert.That(dynamicNoThrow.HasErrors, Is.False, string.Join(Environment.NewLine, dynamicNoThrow.Diagnostics));
        var dynamicNoThrowProgram = dynamicNoThrow.Program ?? throw new AssertionException("Expected a bound program.");
        Assert.That(await RunExecutableAsync(NativeEmitter.EmitWithNativeImportsForTesting(
            dynamicNoThrowProgram, NativeRuntimePolicy.FailFast)), Is.EqualTo(0));

        var invalidNoThrow = CompileSampleArraySource(SampleArrayDeclarations +
            "\n[NoThrow] int32 Main() { InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; " +
            "return values[4] == 0.0 ? 0 : 1; }");
        Assert.That(invalidNoThrow.Program, Is.Null);
        Assert.That(invalidNoThrow.Diagnostics, Is.Not.Empty);

        const string body = """
            mut int32 evaluations = 0;
            InlineArray<float32, 4> values = [17.0, 18.0, 19.0, 20.0];
            float32 copied = values[SampleIndex(evaluations++)];
            return evaluations == 1 && copied == 17.0 ? 0 : 1;
            """;
        var dynamic = CompileSampleArray(body);
        Assert.That(dynamic.HasErrors, Is.False, string.Join(Environment.NewLine, dynamic.Diagnostics));
        var __nunitAssertValue42 = dynamic.Program;
        Assert.That(__nunitAssertValue42, Is.TypeOf<BoundProgram>());
        var image = NativeEmitter.EmitWithNativeImportsForTesting(
                    ((BoundProgram)__nunitAssertValue42), NativeRuntimePolicy.FailFast);
        var boundsCheck = IndexOf(image, [0x3D, 0x04, 0x00, 0x00, 0x00]);
        var addressScale = IndexOf(image, [0x6B, 0xC0, 0x04], boundsCheck + 1);
        Assert.That(boundsCheck >= 0 && addressScale > boundsCheck, Is.True, "The unsigned bounds check must precede sample-array address scaling.");
        Assert.That(await RunExecutableAsync(image), Is.EqualTo(0));
    }

    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0]; return 0;")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0, 5.0]; return 0;")]
    [TestCase("InlineArray<float32, 4> values = [1.0, , 3.0, 4.0]; return 0;")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, ]; return 0;")]
    [TestCase("InlineArray<Vertex, 3> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1)]; return 0;")]
    [TestCase("InlineArray<Vertex, 3> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2), new Vertex(3, 3, 3)]; return 0;")]
    [TestCase("InlineArray<D3D_FEATURE_LEVEL, 1> values = []; return 0;")]
    [TestCase("mut InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; return 0;")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; values[0] = 8.0; return 0;")]
    [TestCase("InlineArray<D3D_FEATURE_LEVEL, 1> values = [uint32(0xB000)]; return 0;")]
    [TestCase("InlineArray<D3D_FEATURE_LEVEL, 1> values = [D3D_FEATURE_LEVEL.LEVEL_10_0]; return 0;")]
    [TestCase("uint32 D3D_FEATURE_LEVEL = 0; InlineArray<D3D_FEATURE_LEVEL, 1> values = [D3D_FEATURE_LEVEL.LEVEL_11_0]; return 0;")]
    [TestCase("InlineArray<Vertex, 2> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1)]; return 0;")]
    [TestCase("InlineArray<Vertex, 4> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2), new Vertex(3, 3, 3)]; return 0;")]
    [TestCase("InlineArray<float32, 3> values = [1.0, 2.0, 3.0]; return 0;")]
    [TestCase("InlineArray<uint32, 4> values = [1, 2, 3, 4]; return 0;")]
    [TestCase("init InlineArray<float32, 4> values; return 0;")]
    [TestCase("InlineArray<InlineArray<float32, 4>, 4> values = []; return 0;")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; return int32(&values[0]);")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; return int32(&values);")]
    [TestCase("InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; InlineArray<float32, 4> copy = values; return 0;")]
    [TestCase("InlineArray<Vertex, 3> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)]; return int32(&values[0]);")]
    public void SampleArrayRejectsIncompleteMutableUnsupportedAndEscapingShapes(string body)
    {
        var compilation = CompileSampleArray(body);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("struct Holder { InlineArray<float32, 4> Values; }")]
    [TestCase("int32 Forward(InlineArray<float32, 4> values) { return 0; }")]
    [TestCase("InlineArray<float32, 4> Escape() { return [1.0, 2.0, 3.0, 4.0]; }")]
    public void SampleArrayCannotEnterFieldsParametersOrResults(string declaration)
    {
        var compilation = CompileSampleArray("return 0;", declarations: SampleArrayDeclarations + declaration);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void SampleArrayAcceptsUnrelatedBlittableAggregatesAndScalarAliases()
    {
        var scalarAlias = CompileSampleArray(
            "InlineArray<FeatureLevel, 1> levels = [uint32(0xB000)]; return 0;",
            declarations: SampleArrayDeclarations + "\nalias FeatureLevel = uint32;");
        Assert.That(scalarAlias.HasErrors, Is.False, string.Join(Environment.NewLine, scalarAlias.Diagnostics));

        const string namespacedVertex = """
            namespace Other;
            [Layout(Sequential)]
            struct Vertex {
                float32 X;
                float32 Y;
                float32 Z;
                public Vertex(float32 x, float32 y, float32 z) {
                    X = x;
                    Y = y;
                    Z = z;
                }
            }
            int32 Main() {
                InlineArray<Vertex, 3> values = [
                    new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)];
                return 0;
            }
            """;
        var vertexAlias = CompileSampleArraySource(namespacedVertex);
        Assert.That(vertexAlias.HasErrors, Is.False, string.Join(Environment.NewLine, vertexAlias.Diagnostics));
    }

    public void SampleArrayRejectsNonBlittableAggregateElements()
    {
        var declarations = SampleArrayDeclarations.Replace("[Blittable] ", "", StringComparison.Ordinal);
        var compilation = CompileSampleArray("""
            InlineArray<Vertex, 3> values = [
                new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)];
            return 0;
            """, declarations: declarations);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void SampleArrayVerifierRejectsForgedInitializerReadsAndTypes()
    {
        var compilation = CompileSampleArray(
            "InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; " +
            "return values[0] == 1.0 ? 0 : 1;");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue43 = compilation.Program;
        Assert.That(__nunitAssertValue43, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue43);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue44 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue44, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue44);
        var __nunitAssertValue45 = local.Initializer;
        Assert.That(__nunitAssertValue45, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var initializer = ((BoundNativeFixedArrayInitialization)__nunitAssertValue45);

        var badLength = initializer with { Elements = initializer.Elements.RemoveAt(0) };
        AssertVerifierRejects(ReplaceStatement(program, local with { Initializer = badLength }));
        var wrongElement = initializer with {
            Elements = initializer.Elements.SetItem(0,
                new BoundConstant(1, PrimitiveType.UInt32, initializer.Elements[0].Span)),
        };
        AssertVerifierRejects(ReplaceStatement(program, local with { Initializer = wrongElement }));
        var wrongArrayType = initializer with { Type = PrimitiveType.NativeDx11BufferDesc };
        AssertVerifierRejects(ReplaceStatement(program, local with { Initializer = wrongArrayType }));
        AssertVerifierRejects(program with { NativeImportCatalog = null, NativeImportCatalogId = null });
        var wholeArrayRead = new BoundRead(local.Variable,
            PrimitiveType.NativeFixedArray, local.Span);
        var badWholeArray = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1, new BoundReturn(wholeArrayRead, local.Span)),
            },
        };
        AssertVerifierRejects(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, badWholeArray),
        });

        var badReadMain = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(1,
                    new BoundReturn(new BoundNativeFixedArrayElement(local.Variable, initializer.Layout,
                        new BoundConstant(4, PrimitiveType.Int32, local.Span),
                        local.Span), local.Span)),
            },
        };
        AssertVerifierRejects(program with {
            Functions = program.Functions.SetItem(program.EntryPoint, badReadMain),
        });

        var forgedNamespacedVertex = program with {
            Structures = program.Structures.SetItem(0,
                program.Structures[0] with { Name = "Other.Vertex" }),
        };
        AssertVerifierRejects(forgedNamespacedVertex);
    }

    [Test]
    public void SampleArrayVerifierRejectsForgedVertexFieldMetadata()
    {
        var compilation = CompileSampleArray("""
            InlineArray<Vertex, 3> values = [
                new Vertex(0, 0, 0), new Vertex(1, 1, 1), new Vertex(2, 2, 2)];
            return values[0].GetX() == 0 ? 0 : 1;
            """);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue49 = compilation.Program;
        Assert.That(__nunitAssertValue49, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue49);
        var __nunitAssertValue50 = global::System.Linq.Enumerable.ToArray(program.Structures);
        Assert.That(__nunitAssertValue50, Has.Exactly(1).Items);
        var vertex = __nunitAssertValue50[0];
        foreach (var field in new[] {
            vertex.Fields[0] with { IsMutable = true },
            vertex.Fields[0] with { IsPrivate = false },
        })
        {
            var forgedVertex = vertex with {
                Fields = vertex.Fields.SetItem(0, field),
            };
            AssertVerifierRejects(program with {
                Structures = program.Structures.SetItem(0, forgedVertex),
            });
        }
    }

    [Test]
    public void SampleArrayFloatReadsUseRawBitsForSignedZeroAndNaNPayloads()
    {
        var compilation = CompileSampleArray(
            "InlineArray<float32, 4> values = [1.0, 2.0, 3.0, 4.0]; return values[0] == 1.0 ? 0 : 1;");
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue51 = compilation.Program;
        Assert.That(__nunitAssertValue51, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue51);
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue52 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue52, Is.TypeOf<BoundLocal>());
        var local = ((BoundLocal)__nunitAssertValue52);
        var __nunitAssertValue53 = local.Initializer;
        Assert.That(__nunitAssertValue53, Is.TypeOf<BoundNativeFixedArrayInitialization>());
        var initializer = ((BoundNativeFixedArrayInitialization)__nunitAssertValue53);
        var rawBits = new uint[] { 0x80000000, 0x7FC01234, 0xFFC54321, 0x00000001 };
        var rawInitializer = initializer with {
            Elements = rawBits.Select(bits =>
                (BoundExpression)new BoundConstant(bits, PrimitiveType.Float32, initializer.Span)).ToImmutableArray(),
        };
        var rawProgram = ReplaceStatement(program, local with { Initializer = rawInitializer });
        var image = NativeEmitter.EmitWithNativeImportsForTesting(rawProgram, NativeRuntimePolicy.FailFast);
        foreach (var bits in rawBits)
        {
            Assert.That(Contains(image, new byte[] { 0xB8, unchecked((byte)bits),
                unchecked((byte)(bits >> 8)), unchecked((byte)(bits >> 16)),
                unchecked((byte)(bits >> 24)) }), Is.True, $"PE image did not contain the exact float32 immediate 0x{bits:X8}.");
        }
        Assert.That(Contains(image, [0x89, 0x84, 0x24]) || Contains(image, [0x89, 0x85]), Is.True, "Float32 array initialization must store raw 32-bit values.");
        Assert.That(Contains(image, [0x8B, 0x00]), Is.True, "Float32 element reads must be raw 32-bit loads.");
    }

    [Test]
    public void SampleArrayElementsCannotBeMutatedThroughAReceiverLoan()
    {
        var declarations = SampleArrayDeclarations
            .Replace("public float32 GetX()", "public mut void SetX(float32 x) { }\npublic float32 GetX()",
                StringComparison.Ordinal);
        var compilation = CompileSampleArray(
            "InlineArray<Vertex, 3> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), " +
            "new Vertex(2, 2, 2)]; values[0].SetX(3.0); return 0;", declarations: declarations);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);

        var mutableFieldDeclarations = SampleArrayDeclarations.Replace(
            "float32 X;", "mut float32 X;", StringComparison.Ordinal);
        var mutableField = CompileSampleArray(
            "InlineArray<Vertex, 3> values = [new Vertex(0, 0, 0), new Vertex(1, 1, 1), " +
            "new Vertex(2, 2, 2)]; return 0;", declarations: mutableFieldDeclarations);
        Assert.That(mutableField.Program, Is.Null);
        Assert.That(mutableField.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    private static BoundProgram ReplaceStatement(BoundProgram program, BoundStatement statement)
    {
        var function = program.Functions[program.EntryPoint];
        return program with {
            Functions = program.Functions.SetItem(program.EntryPoint, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, statement),
                },
            }),
        };
    }

    private static void AssertVerifierRejects(BoundProgram program) =>
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));

    private static bool Contains(byte[] data, byte[] value) =>
        data.AsSpan().IndexOf(value) >= 0;

    private static int IndexOf(byte[] data, byte[] value, int startIndex = 0)
    {
        var offset = data.AsSpan(startIndex).IndexOf(value);
        return offset < 0 ? -1 : startIndex + offset;
    }

    private static async Task<int> RunExecutableAsync(byte[] executable)
    {
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-sample-array-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sample-array.exe");
        try
        {
            File.WriteAllBytes(path, executable);
            using var process = Process.Start(new ProcessStartInfo(path) {
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("The sample-array PE fixture could not be started.");
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("The sample-array PE fixture did not terminate.");
            }
            return process.ExitCode;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
