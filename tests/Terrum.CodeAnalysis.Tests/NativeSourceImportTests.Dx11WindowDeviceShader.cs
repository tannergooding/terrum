// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string DirectWindowDeviceShaderSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-device-shader-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectWindowDeviceShader()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowDeviceShaderSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(NativeDx11DirectShaderContract.MatchesProgram(program), Is.True);
        return program;
    }

    private static NativeCode DirectWindowDeviceShaderCode(BoundProgram program)
    {
        SemanticVerifier.VerifyReferences(program);
        var bindings = program.Functions.Select((function, index) => (function, index))
            .Where(static item => item.function.IsExternal).ToDictionary(static item => item.index, item => {
                Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(item.function.NativeImportKey ?? "", out var import), Is.True);
                Assert.That(import, Is.Not.Null);
                return new NativeImportBinding(item.function.Name, import.DllName, import.ExportName);
            });
        return new PeCompiler(program, default, managedNodes: false, imports: bindings).Compile();
    }

    [Test]
    public void DirectShaderImportParametersUseGenericBoundMappings()
    {
        var structures = NativeDx11WarpAcquisitionContract.Structures
            .AddRange(NativeDx11DirectShaderContract.Structures);
        var function = NativeDx11DirectShaderContract.Functions(structures)[0];
        var program = new BoundProgram([function], 0) {
            NativeImportCatalog = NativeImportCatalog.WindowsX64,
        };

        Assert.That(NativeStorageParameterContract.TryGetParameter(function, 0, program, out var storageParameter), Is.True);
        Assert.That(storageParameter, Is.Not.Null);
        Assert.That(storageParameter.AddressType, Is.EqualTo(PrimitiveType.NativeUtf16ZPointer));
        Assert.That(NativeStorageParameterContract.TryGetImportParameterContract(
            function, 0, program, out var fileContract), Is.True);
        Assert.That(fileContract, Is.EqualTo(NativeImportParameterContract.ReadonlyUtf16ZNoEscape));
        Assert.That(NativeStorageParameterContract.IsReadonlyStringParameter(
            function, 0, PrimitiveType.NativeUtf16ZPointer, program), Is.True);
        Assert.That(NativeStorageParameterContract.IsReadonlyStringParameter(
            function, 1, PrimitiveType.NativeUtf8ZPointer, program), Is.True);
        Assert.That(NativeStorageParameterContract.IsReadonlyStringParameter(
            function, 2, PrimitiveType.NativeUtf8ZPointer, program), Is.True);
    }

    [Test]
    public void DirectWindowDeviceShaderStageSealsSourceOwnersAndNativeAbi()
    {
        Assert.That(NativeDx11DirectShaderContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        var program = CompileDirectWindowDeviceShader();
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(program), Is.False);
        Assert.That(NativeDx11DirectPresentationContract.MatchesProgram(CompileDirectWindowDevicePresent()), Is.True);
        Assert.That(NativeDx11DirectShaderContract.CreateVertex.Slot, Is.EqualTo(12));
        Assert.That(NativeDx11DirectShaderContract.CreatePixel.Slot, Is.EqualTo(15));
        Assert.That(NativeDx11DirectShaderContract.CreateVertex.CodePointerSlot, Is.EqualTo(3));
        Assert.That(NativeDx11DirectShaderContract.CreateVertex.CodeSizeSlot, Is.EqualTo(4));
        Assert.That(NativeDx11DirectShaderContract.CreateVertex.CodeReleaseSlot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectShaderContract.AddRefCodeCom.Slot, Is.EqualTo(1));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayout.Slot, Is.EqualTo(11));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayoutCom, Is.SameAs(NativeDx11ComCatalog.Method(NativeDx11ComCatalog.CreateInputLayoutIdentity)));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayout.CodePointerSlot, Is.EqualTo(3));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayout.CodeSizeSlot, Is.EqualTo(4));
        Assert.That(NativeDx11DirectShaderContract.CreateInputLayout.CodeReleaseSlot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectShaderContract.ReleaseInputLayoutCom.Slot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectShaderContract.AddRefCodeCom.Slot, Is.EqualTo(1));
        Assert.That(NativeDx11DirectShaderContract.ReleaseCodeCom.Slot, Is.EqualTo(2));
        Assert.That(NativeDx11DirectShaderContract.GetCodePointerCom.Slot, Is.EqualTo(3));
        Assert.That(NativeDx11DirectShaderContract.GetCodeSizeCom.Slot, Is.EqualTo(4));
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Intrinsic == BoundIntrinsic.Dx11InputLayoutTryCreate));
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var createInputLayoutFunction = __nunitAssertValue1[0];
        Assert.That(createInputLayoutFunction.NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.CreateInputLayoutCom));
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Name == NativeDx11DirectShaderContract.InputLayoutName + ".Close"
                        && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var releaseInputLayoutFunction = __nunitAssertValue2[0];
        Assert.That(releaseInputLayoutFunction.NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.ReleaseInputLayoutCom));
        var __nunitAssertValue3 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Intrinsic == BoundIntrinsic.Dx11VertexShaderTryCreate));
        Assert.That(__nunitAssertValue3, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue3[0].NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.CreateVertexCom));
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Name == NativeDx11DirectShaderContract.VertexShaderName + ".Close"
                            && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue4[0].NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.ReleaseVertexCom));
        var __nunitAssertValue5 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Intrinsic == BoundIntrinsic.Dx11PixelShaderTryCreate));
        Assert.That(__nunitAssertValue5, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue5[0].NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.CreatePixelCom));
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Name == NativeDx11DirectShaderContract.PixelShaderName + ".Close"
                            && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue6[0].NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.ReleasePixelCom));
        var __nunitAssertValue7 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Name == NativeDx11DirectShaderContract.CodeName + ".Close"
                            && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
        Assert.That(__nunitAssertValue7, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue7[0].NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.ReleaseCodeCom));
        var __nunitAssertValue8 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Intrinsic == BoundIntrinsic.NativeComOwnerTryClone));
        Assert.That(__nunitAssertValue8, Has.Exactly(1).Items);
        Assert.That(__nunitAssertValue8[0].NativeComMethod, Is.SameAs(NativeDx11DirectShaderContract.AddRefCodeCom));
        foreach (var owner in program.Structures.Skip(9))
        {
            Assert.That(owner.IsUnique, Is.True);
            Assert.That(owner.Size, Is.EqualTo(8));
            Assert.That(owner.Fields, Is.Empty);
            Assert.That(owner.CleanupMethod, Is.EqualTo(owner.Name + ".Close"));
        }
        var code = DirectWindowDeviceShaderCode(program);
        var __nunitAssertValue9 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.NativeImports, static import => import.ExportName == "D3DCompileFromFile"));
        Assert.That(__nunitAssertValue9, Has.Exactly(1).Items);
        var compileImport = __nunitAssertValue9[0];
        Assert.That(compileImport.DllName, Is.EqualTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "D3DCompiler_47.dll")));
        Assert.That(code.NativeImports, Has.None.Matches(static import => import.ExportName.StartsWith("NativeFfi", StringComparison.Ordinal)));
        Assert.That(code.NativeImports, Has.None.Matches(static import => import.ExportName is "LoadLibraryA" or "LoadLibraryW" or "GetProcAddress"));
        var __nunitAssertValue10 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[12]));
        Assert.That(__nunitAssertValue10, Has.Exactly(1).Items);
        var compileRange = __nunitAssertValue10[0];
        var compileBytes = code.Code.AsSpan(compileRange.Start, compileRange.End - compileRange.Start).ToArray();
        Assert.That(compileRange.StackSize & 15, Is.EqualTo(8));
        foreach (var offset in new byte[] { 32, 40, 48, 56, 64 })
        {
            Assert.That(ContainsSequence(compileBytes, [0x48, 0x89, 0x84, 0x24, offset, 0, 0, 0]), Is.True, $"D3DCompileFromFile outgoing argument at offset {offset} must be a full machine word.");
        }
        foreach (var (index, slot) in new[] { (14, (byte)96), (16, (byte)120) })
        {
            var __nunitAssertValue11 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[index]));
            Assert.That(__nunitAssertValue11, Has.Exactly(1).Items);
            var range = __nunitAssertValue11[0];
            var bytes = code.Code.AsSpan(range.Start, range.End - range.Start).ToArray();
            Assert.That(range.StackSize & 15, Is.EqualTo(8));
            Assert.That(ContainsSequence(bytes, [0xFF, 0x50, slot]), Is.True);
            Assert.That(ContainsSequence(bytes, [0xFF, 0x50, 24]), Is.True);
            Assert.That(ContainsSequence(bytes, [0xFF, 0x50, 32]), Is.True);
            Assert.That(ContainsSequence(bytes, [0xFF, 0x50, 16]), Is.True);
            Assert.That(ContainsSequence(bytes, [0x48, 0x89, 0x84, 0x24, 32, 0, 0, 0]), Is.True, "CreateShader output is the fifth x64 argument, outside the four home slots.");
        }
        var __nunitAssertValue12 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[18]));
        Assert.That(__nunitAssertValue12, Has.Exactly(1).Items);
        var cloneRange = __nunitAssertValue12[0];
        var cloneBytes = code.Code.AsSpan(cloneRange.Start, cloneRange.End - cloneRange.Start).ToArray();
        Assert.That(ContainsSequence(cloneBytes, [0x48, 0x8B, 0x80, 0, 0, 0, 0]), Is.True, "The cloned blob receiver must be loaded from its owner cell.");
        Assert.That(ContainsSequence(cloneBytes, [0xFF, 0x50, 8]), Is.True, "The input-layout path must AddRef the shader blob through vtable slot 1.");
        var __nunitAssertValue13 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[19]));
        Assert.That(__nunitAssertValue13, Has.Exactly(1).Items);
        var layoutRange = __nunitAssertValue13[0];
        var layoutBytes = code.Code.AsSpan(layoutRange.Start, layoutRange.End - layoutRange.Start).ToArray();
        Assert.That(layoutRange.StackSize & 15, Is.EqualTo(8));
        foreach (var (index, slot) in new[] { (0, (byte)24), (1, (byte)32), (2, (byte)88) })
        {
            Assert.That(ContainsSequence(layoutBytes, [0xFF, 0x50, slot]), Is.True, $"The input-layout path must call the expected vtable slot {index}.");
        }
        foreach (var offset in new byte[] { 32, 40 })
        {
            Assert.That(ContainsSequence(layoutBytes, [0x48, 0x89, 0x84, 0x24, offset, 0, 0, 0]), Is.True, $"CreateInputLayout stack argument at offset {offset} must be a full machine word.");
        }
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf(Encoding.Unicode.GetBytes("triangle.hlsl\0")) >= 0, Is.True);
        foreach (var text in new[] { "VertexMain", "vs_5_0", "POSITION", "PixelMain", "ps_5_0" })
        {
            Assert.That(image.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text + "\0")) >= 0, Is.True);
        }
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
    }

    [TestCase("utf16z(\"triangle.hlsl\")", "utf16z(\"other.hlsl\")")]
    [TestCase("utf8z(\"VertexMain\")", "utf8z(\"PixelMain\")")]
    [TestCase("utf8z(\"vs_5_0\")", "utf8z(\"vs_4_0\")")]
    [TestCase("utf8z(\"POSITION\")", "utf8z(\"TEXCOORD\")")]
    [TestCase("utf8z(\"ps_5_0\")", "utf8z(\"vs_5_0\")")]
    [TestCase("move vertexCode", "vertexCode")]
    [TestCase("move pixelCode", "move vertexCode")]
    [TestCase("device.TryCreateVertexShader", "device.TryCreatePixelShader")]
    [TestCase("device.TryCreateInputLayout", "device.TryCreateVertexShader")]
    [TestCase("out var vertexShader", "out var pixelShader")]
    [TestCase("ShowWindow(Handle, 5);", "vertexShader.Close(); ShowWindow(Handle, 5);")]
    [TestCase("ShowWindow(Handle, 5);", "uint64 raw = &pixelShader; ShowWindow(Handle, 5);")]
    [TestCase("ShowWindow(Handle, 5);", "uint64 bytes = vertexCode.GetBufferPointer(); ShowWindow(Handle, 5);")]
    [TestCase("ShowWindow(Handle, 5);", "uint64 size = pixelCode.GetBufferSize(); ShowWindow(Handle, 5);")]
    [TestCase("if (swapchain.Present())", "if (!swapchain.Present())")]
    public void DirectWindowDeviceShaderStageRejectsSourceMutationAndBytecodeEscape(string original, string replacement)
    {
        var source = DirectWindowDeviceShaderSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var mutated = source.Replace(original, replacement, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutated)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void DirectWindowDeviceShaderStageRequiresCheckedSystemCatalogAndSingleSource()
    {
        var tree = SyntaxTree.Parse(DirectWindowDeviceShaderSource);
        var compilations = new[] {
            Compilation.Create(tree, BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(tree, runtimePolicy: BoundRuntimePolicy.FailFast),
            Compilation.CreateWithNativeImports([tree, SyntaxTree.Parse("void Other() { }")],
                runtimePolicy: BoundRuntimePolicy.CheckedExceptions),
        };
        foreach (var compilation in compilations)
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.HasErrors, Is.True, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
    }

    [TestCase("struct")]
    [TestCase("class")]
    [TestCase("alias")]
    public void DirectWindowDeviceShaderOwnerIdentityIsReservedOnlyByItsCatalog(string declaration)
    {
        foreach (var owner in NativeDx11DirectShaderContract.Structures)
        {
            var name = owner.Name[(owner.Name.LastIndexOf('.') + 1)..];
            var ownerDeclaration = declaration switch {
                "alias" => "alias " + name + " = uint64;",
                "class" => "class " + name + " { uint64 Raw; " + name
                    + "(uint64 raw) { Raw = raw; } }",
                _ => "struct " + name + " { uint64 Raw; }",
            };
            var source = "namespace Terrum.Interop.Dx11;\n" + ownerDeclaration + "\nint32 Main() { return 0; }";
            var tree = SyntaxTree.Parse(source);
            var ordinaryCompilation = Compilation.Create(tree);
            Assert.That(ordinaryCompilation.Program is not null, Is.True, string.Join(Environment.NewLine,
                ordinaryCompilation.Diagnostics));
            Assert.That(Compilation.CreateWithNativeImports([tree],
                runtimePolicy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        }
    }

    [Test]
    public void DirectWindowDeviceShaderStageDoesNotReserveOrdinaryShaderMethodNames()
    {
        const string source = """
            struct Other {
                int32 Value;
                public static int32 TryCreateVertexShader() { return 7; }
                public static int32 TryCreatePixelShader() { return 8; }
                public static int32 TryCreateInputLayout() { return 9; }
            }
            int32 D3DCompileFromFile() { return 9; }
            int32 Main() {
                return Other.TryCreateVertexShader() + Other.TryCreatePixelShader() + D3DCompileFromFile();
            }
            """;
        foreach (var compilation in new[] {
            Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
                runtimePolicy: BoundRuntimePolicy.CheckedExceptions),
        })
        {
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var __nunitAssertValue14 = compilation.Program;
            Assert.That(__nunitAssertValue14, Is.TypeOf<BoundProgram>());
            var program = ((BoundProgram)__nunitAssertValue14);
            Assert.That(program.Functions, Has.None.Matches(static function =>
                NativeDx11DirectShaderContract.IsIntrinsic(function.Intrinsic)));
            SemanticVerifier.VerifyReferences(program);
        }
    }

    [Test]
    public void DirectWindowDeviceShaderStageRejectsImportAndComContractForgeries()
    {
        var program = CompileDirectWindowDeviceShader();
        foreach (var (function, index) in program.Functions.Select((function, index) => (function, index))
            .Where(static item => item.function.NativeDx11DirectShaderMethod is not null))
        {
            var __nunitAssertValue15 = function.NativeDx11DirectShaderMethod;
            Assert.That(__nunitAssertValue15, Is.TypeOf<NativeDx11DirectShaderMethod>());
            var method = ((NativeDx11DirectShaderMethod)__nunitAssertValue15);
            foreach (var forgedMethod in new[] {
                method with { Slot = method.Slot + 1 },
                method with { Signature = "uint32(void*)" },
                method with { Abi = "x86-stdcall" },
                method with { CodePointerSlot = 2 },
                method with { CodeSizeSlot = 3 },
                method with { CodeReleaseSlot = 1 },
            })
            {
                var forged = program with {
                    Functions = program.Functions.SetItem(index,
                    function with { NativeDx11DirectShaderMethod = forgedMethod })
                };
                Assert.That(NativeDx11DirectShaderContract.MatchesProgram(forged), Is.False);
                Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
            }
        }
        foreach (var (function, index) in program.Functions.Select((function, index) => (function, index))
            .Where(item => item.function.NativeComMethod is not null
                && NativeDx11DirectShaderContract.MatchesFunction(item.function, program)))
        {
            var __nunitAssertValue16 = function.NativeComMethod;
            Assert.That(__nunitAssertValue16, Is.TypeOf<NativeComMethodDefinition>());
            var method = ((NativeComMethodDefinition)__nunitAssertValue16);
            var forged = program with {
                Functions = program.Functions.SetItem(index,
                    function with { NativeComMethod = method with { Slot = method.Slot + 1 } }),
            };
            Assert.That(NativeDx11DirectShaderContract.MatchesProgram(forged), Is.False);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
        }
        foreach (var owner in program.Structures.Where(static owner =>
            NativeDx11DirectShaderContract.IsOwnerName(owner.Name)))
        {
            foreach (var forgedOwner in new[] {
                owner with { IsUnique = false },
                owner with { Size = 16 },
                owner with { CleanupMethod = NativeDx11DirectShaderContract.CodeName + ".Other" },
                owner with { Fields = [new("Raw", PrimitiveType.UInt64, 0, 8)] },
            })
            {
                var index = program.Structures.IndexOf(owner);
                var forged = program with { Structures = program.Structures.SetItem(index, forgedOwner) };
                Assert.That(NativeDx11DirectShaderContract.MatchesProgram(forged), Is.False);
                Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
            }
        }
        var import = NativeDx11DirectShaderContract.CompileImport;
        Assert.That(import.ParameterTypes.Length, Is.EqualTo(9));
        Assert.That(import.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        foreach (var forged in new[] {
            import with { Key = "other.compiler" },
            import with { DllName = "application-shader-helper.dll" },
            import with { ExportName = "D3DCompile" },
            import with { ReturnType = PrimitiveType.UInt32 },
            import with { DeploymentPolicy = NativeImportDeploymentPolicy.ApplicationDirectory },
            import with { ParameterTypes = import.ParameterTypes.SetItem(8, PrimitiveType.UInt32) },
            import with { ParameterContracts = import.ParameterContracts.SetItem(7, NativeImportParameterContract.Value) },
        })
        {
            Assert.That(NativeDx11DirectShaderContract.MatchesImport(forged), Is.False);
            Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
                NativeWindowClassContract.CatalogIdentity, [forged]));
        }
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeWindowClassContract.CatalogIdentity, [import]));
        var raw = Compilation.CreateWithNativeImports(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport("{{NativeDx11DirectShaderContract.CompileKey}}")]
            [NoThrow] extern int32 Raw(ptr<char16> file, uint64 defines, uint64 include,
                ptr<char8> entry, ptr<char8> target, uint32 flags1, uint32 flags2, uint64 code, uint64 diagnostics);
            int32 Main() { return 0; }
            """));
        Assert.That(raw.Program, Is.Null);
        Assert.That(raw.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void DirectWindowDeviceShaderStageRejectsBoundPublicationMoveAndCleanupForgeries()
    {
        var program = CompileDirectWindowDeviceShader();
        var run = program.Functions[39];
        var shaderBlock = FindShaderStageScope(run.Body, 16);
        var __nunitAssertValue17 = shaderBlock.Statements[1];
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundTryFinally>());
        var shaderScope = ((BoundTryFinally)__nunitAssertValue17);
        var __nunitAssertValue18 = global::System.Linq.Enumerable.ToArray(shaderScope.Body.Statements);
        Assert.That(__nunitAssertValue18, Has.Exactly(1).Items);
        var __nunitAssertValue19 = __nunitAssertValue18[0];
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundIf>());
        var compileBranch = ((BoundIf)__nunitAssertValue19);
        var __nunitAssertValue20 = compileBranch.Condition;
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundCall>());
        var compile = ((BoundCall)__nunitAssertValue20);
        var vertexBlock = FindShaderStageScope(run.Body, 20);
        var __nunitAssertValue21 = vertexBlock.Statements[1];
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundTryFinally>());
        var vertexScope = ((BoundTryFinally)__nunitAssertValue21);
        var __nunitAssertValue22 = global::System.Linq.Enumerable.ToArray(vertexScope.Body.Statements);
        Assert.That(__nunitAssertValue22, Has.Exactly(1).Items);
        var __nunitAssertValue23 = __nunitAssertValue22[0];
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundIf>());
        var vertexBranch = ((BoundIf)__nunitAssertValue23);
        var __nunitAssertValue24 = vertexBranch.Condition;
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundCall>());
        var create = ((BoundCall)__nunitAssertValue24);
        var cloneBlock = FindShaderStageScope(run.Body, 17);
        var __nunitAssertValue25 = cloneBlock.Statements[1];
        Assert.That(__nunitAssertValue25, Is.TypeOf<BoundTryFinally>());
        var cloneScope = ((BoundTryFinally)__nunitAssertValue25);
        var __nunitAssertValue26 = global::System.Linq.Enumerable.ToArray(cloneScope.Body.Statements);
        Assert.That(__nunitAssertValue26, Has.Exactly(1).Items);
        var __nunitAssertValue27 = __nunitAssertValue26[0];
        Assert.That(__nunitAssertValue27, Is.TypeOf<BoundIf>());
        var cloneBranch = ((BoundIf)__nunitAssertValue27);
        var __nunitAssertValue28 = cloneBranch.Condition;
        Assert.That(__nunitAssertValue28, Is.TypeOf<BoundCall>());
        var clone = ((BoundCall)__nunitAssertValue28);
        var inputLayoutBlock = FindShaderStageScope(run.Body, 19);
        var __nunitAssertValue29 = inputLayoutBlock.Statements[2];
        Assert.That(__nunitAssertValue29, Is.TypeOf<BoundTryFinally>());
        var inputLayoutScope = ((BoundTryFinally)__nunitAssertValue29);
        var __nunitAssertValue30 = global::System.Linq.Enumerable.ToArray(inputLayoutScope.Body.Statements);
        Assert.That(__nunitAssertValue30, Has.Exactly(1).Items);
        var __nunitAssertValue31 = __nunitAssertValue30[0];
        Assert.That(__nunitAssertValue31, Is.TypeOf<BoundIf>());
        var inputLayoutBranch = ((BoundIf)__nunitAssertValue31);
        var __nunitAssertValue32 = inputLayoutBranch.Condition;
        Assert.That(__nunitAssertValue32, Is.TypeOf<BoundCall>());
        var createInputLayout = ((BoundCall)__nunitAssertValue32);
        BoundProgram ReplaceBlock(BoundBlock source, BoundBlock replacement) => program with {
            Functions = program.Functions.SetItem(39, run with {
                Body = NativeDx11StageProjection.ReplaceBlock(run.Body, source, replacement),
            }),
        };
        BoundProgram ReplaceCompile(BoundCall call) => ReplaceBlock(shaderBlock, shaderBlock with {
            Statements = shaderBlock.Statements.SetItem(1, shaderScope with {
                Body = shaderScope.Body with { Statements = [compileBranch with { Condition = call }] },
            }),
        });
        BoundProgram ReplaceClone(BoundCall call) => ReplaceBlock(cloneBlock, cloneBlock with {
            Statements = cloneBlock.Statements.SetItem(1, cloneScope with {
                Body = cloneScope.Body with { Statements = [cloneBranch with { Condition = call }] },
            }),
        });
        BoundProgram ReplaceCreate(BoundCall call) => ReplaceBlock(vertexBlock, vertexBlock with {
            Statements = vertexBlock.Statements.SetItem(1, vertexScope with {
                Body = vertexScope.Body with { Statements = [vertexBranch with { Condition = call }] },
            }),
        });
        BoundProgram ReplaceCreateInputLayout(BoundCall call) => ReplaceBlock(inputLayoutBlock, inputLayoutBlock with {
            Statements = inputLayoutBlock.Statements.SetItem(1, inputLayoutScope with {
                Body = inputLayoutScope.Body with { Statements = [inputLayoutBranch with { Condition = call }] },
            }),
        });
        var __nunitAssertValue33 = compile.Arguments[3];
        Assert.That(__nunitAssertValue33, Is.TypeOf<BoundOutArgument>());
        var codeOutput = ((BoundOutArgument)__nunitAssertValue33);
        var __nunitAssertValue34 = create.Arguments[2];
        Assert.That(__nunitAssertValue34, Is.TypeOf<BoundOutArgument>());
        var shaderOutput = ((BoundOutArgument)__nunitAssertValue34);
        var __nunitAssertValue35 = clone.Arguments[1];
        Assert.That(__nunitAssertValue35, Is.TypeOf<BoundOutArgument>());
        var cloneOutput = ((BoundOutArgument)__nunitAssertValue35);
        var __nunitAssertValue36 = createInputLayout.Arguments[3];
        Assert.That(__nunitAssertValue36, Is.TypeOf<BoundOutArgument>());
        var inputLayoutOutput = ((BoundOutArgument)__nunitAssertValue36);
        foreach (var forged in new[] {
            program with { RuntimePolicy = BoundRuntimePolicy.FailFast },
            program with { EntryPoint = 31 },
            program with { NativeImportCatalog = null },
            program with { NativeUtf8ZLiterals = program.NativeUtf8ZLiterals.SetItem(3, "vs_5_0") },
            program with { Functions = program.Functions.SetItem(12, program.Functions[12] with { Intrinsic = BoundIntrinsic.None }) },
            program with { Functions = program.Functions.SetItem(14, program.Functions[14] with {
                Variables = program.Functions[14].Variables.SetItem(1,
                    program.Functions[14].Variables[1] with { IsOut = true }),
            }) },
            program with { Functions = program.Functions.SetItem(39, run with {
                Variables = run.Variables.SetItem(16, run.Variables[16] with { IsMutable = true }),
            }) },
            ReplaceCompile(compile with { Arguments = compile.Arguments.SetItem(1,
                new BoundConstant(2, PrimitiveType.NativeUtf8ZPointer, default)) }),
            ReplaceCompile(compile with { Arguments = compile.Arguments.SetItem(3, codeOutput with { Variable = 18 }) }),
            ReplaceCompile(compile with { Arguments = compile.Arguments.SetItem(3, codeOutput with { IsInit = true }) }),
            ReplaceCompile(compile with { ArgumentParameterIndices = [0, 2, 1, 3] }),
            ReplaceClone(clone with { Function = 14 }),
            ReplaceClone(clone with { Arguments = clone.Arguments.SetItem(1, cloneOutput with { Variable = 20 }) }),
            ReplaceCreate(create with { Function = 16 }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(1,
                new BoundRead(16, (PrimitiveType)109, default)) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(1,
                new BoundMove(18, (PrimitiveType)109, default)) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(2, shaderOutput with { DeclaresVariable = false }) }),
            ReplaceBlock(vertexBlock, vertexBlock with {
                Statements = vertexBlock.Statements.SetItem(1, vertexScope with {
                    Finally = vertexScope.Finally with { Statements = [new BoundDrop(20, 20, default)] },
                }),
            }),
            ReplaceCreateInputLayout(createInputLayout with { Function = 14 }),
            ReplaceCreateInputLayout(createInputLayout with { Arguments = createInputLayout.Arguments.SetItem(1,
                new BoundRead(17, (PrimitiveType)109, default)) }),
            ReplaceCreateInputLayout(createInputLayout with { Arguments = createInputLayout.Arguments.SetItem(2,
                new BoundNull(PrimitiveType.NativeDx11InputElementDescAddress, default)) }),
            ReplaceCreateInputLayout(createInputLayout with { Arguments = createInputLayout.Arguments.SetItem(3,
                inputLayoutOutput with { DeclaresVariable = false }) }),
            ReplaceBlock(inputLayoutBlock, inputLayoutBlock with {
                Statements = inputLayoutBlock.Statements.SetItem(2, inputLayoutScope with {
                    Finally = inputLayoutScope.Finally with {
                        Statements = [new BoundDrop(19, 17, default)],
                    },
                }),
            }),
            ReplaceBlock(shaderBlock, shaderBlock with { Statements = [compileBranch] }),
            ReplaceBlock(shaderBlock, shaderBlock with { IsManagedLexicalScope = false }),
        })
        {
            Assert.That(NativeDx11DirectShaderContract.MatchesProgram(forged), Is.False);
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
            Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(forged,
                NativeRuntimePolicy.CheckedExceptions));
        }
        BoundExpression deeplyNested = compile;
        for (var depth = 0; depth < 5_000; depth++)
        {
            deeplyNested = new BoundCall(12, [deeplyNested], PrimitiveType.Bool, default);
        }
        var deepForgery = ReplaceBlock(shaderBlock, shaderBlock with {
            Statements = shaderBlock.Statements.SetItem(1, shaderScope with {
                Body = shaderScope.Body with { Statements = [compileBranch with { Condition = deeplyNested }] },
            }),
        });
        Assert.That(NativeDx11DirectShaderContract.MatchesProgram(deepForgery), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(deepForgery));
    }

    private static BoundBlock FindShaderStageScope(BoundStatement statement, int owner) =>
        FindShaderStageScopeOrNull(statement, owner)
        ?? throw new InvalidOperationException($"Sealed shader owner {owner} was not found.");

    private static BoundBlock? FindShaderStageScopeOrNull(BoundStatement statement, int owner)
    {
        if (statement is BoundBlock block)
        {
            for (var index = 0; index < block.Statements.Length - 1; index++)
            {
                if (block.Statements[index] is BoundOwnerDeclaration declaration
                    && declaration.Variable == owner
                    && block.Statements[index + 1] is BoundTryFinally)
                {
                    return block;
                }
            }
        }
        IEnumerable<BoundStatement> children = statement switch {
            BoundBlock scope => scope.Statements.AsEnumerable(),
            BoundTryFinally cleanup => new BoundStatement[] { cleanup.Body, cleanup.Finally },
            BoundIf branch => branch.Else is null ? new[] { branch.Then } : new[] { branch.Then, branch.Else },
            _ => [],
        };
        foreach (var child in children)
        {
            if (FindShaderStageScopeOrNull(child, owner) is BoundBlock found)
            {
                return found;
            }
        }
        return null;
    }

    [Test]
    public async Task DirectWindowDeviceShaderStageMatchesSdkAndCleansInjectedPartialOutputs()
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-direct-shader-abi-");
        try
        {
            var sdk = Path.Combine(root, "tests", "native", "dx11-direct-shader-sdk-probe.c");
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-direct-shader-probe.c");
            var dll = Path.Combine(directory, "native-dx11-direct-shader-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fosdk.obj \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" user32.lib\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            Assert.That(File.Exists(Path.Combine(directory, "sdk.obj")), Is.True);
            var code = DirectWindowDeviceShaderCode(CompileDirectWindowDeviceShader());
            // Substitute emitted imports only in this isolated ABI fixture, never in the sample or bound/catalog seals.
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName switch {
                    "D3DCompileFromFile" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectShaderCompile",
                    },
                    "D3D11CreateDeviceAndSwapChain" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectShaderAcquisition",
                    },
                    _ => import,
                })],
            };
            var executable = Path.Combine(directory, "injected-shader.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(false);
            string[] traces = [
                "ABVEaIvXvPEYpGyxlbCSDZ", "ABVEaIvXvPEYpGyxlbCSDZ", "ABVEaIvXvPEYpGyxlbCSDZ",
                "ABVbCSDZ", "ABVEvbCSDZ", "ABVEbCSDZ", "ABVEaIvXvxlbCSDZ", "ABVEaIvXvlbCSDZ",
                "ABVEaIvXvPEpxlbCSDZ", "ABVEaIvXvPExlbCSDZ", "ABVEaIvXvPEYpyxlbCSDZ",
                "ABVEaIvXvPEYpxlbCSDZ", "ABVEavvbCSDZ", "ABVEavvbCSDZ", "ABVvaIvXvPpYpGyxlbCSDZ",
                "ABVvvbCSDZ", "ABVEaIvXvlbCSDZ", "ABVEaIvXvPEYpxlbCSDZ", "ABVEvbCSDZ",
                "ABVEaIvXvPEYpyxlbCSDZ", "ABVEbCSDZ", "ABVEaIvlvbCSDZ", "ABVEaIvvbCSDZ",
                "ABVEaIvvbCSDZ", "ABVEaIvXvPEYpGyxlbCSDZ",
            ];
            for (var mode = 0; mode < traces.Length; mode++)
            {
                var report = Path.Combine(directory, $"trace-{mode}.txt");
                var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false };
                start.Environment["TERRUM_DX11_SHADER_MODE"] = mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                start.Environment["TERRUM_DX11_SHADER_REPORT"] = report;
                using var process = Process.Start(start) ?? throw new InvalidOperationException("Injected shader stage did not start.");
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(process.ExitCode, Is.EqualTo(1));
                    Assert.That(await File.ReadAllTextAsync(report).ConfigureAwait(false), Is.EqualTo(traces[mode]));
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
