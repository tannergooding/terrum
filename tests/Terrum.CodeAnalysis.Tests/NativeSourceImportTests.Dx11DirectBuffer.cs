// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string DirectBufferSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "device-buffer-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectBuffer()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectBufferSource)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        return ((BoundProgram)__nunitAssertValue0);
    }

    private static NativeCode DirectBufferCode(BoundProgram program)
    {
        var imports = program.Functions.Select((function, index) => (function, index))
            .Where(static item => item.function.IsExternal).ToDictionary(static item => item.index, item => {
                Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(
                    item.function.NativeImportKey ?? "", out var import), Is.True);
                Assert.That(import, Is.Not.Null);
                return new NativeImportBinding(item.function.Name, import.DllName, import.ExportName);
            });
        return new PeCompiler(program, default, managedNodes: false, imports: imports).Compile();
    }

    [Test]
    public void DirectBufferDoesNotReserveTheMethodNameOnOrdinarySourceTypes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Other {
                int32 Value;
                public static int32 TryCreateImmutableVertexBuffer() { return 7; }
            }
            int32 Main() { return Other.TryCreateImmutableVertexBuffer(); }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue1 = compilation.Program;
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue1);
        Assert.That(program.Functions, Has.None.Matches(function => NativeDx11DirectBufferContract.IsIntrinsic(function.Intrinsic)));
        SemanticVerifier.VerifyReferences(program);
    }

    [Test]
    public void DirectBufferCatalogsCreateAndReleaseAndEmitsTheirSdkSlots()
    {
        var program = CompileDirectBuffer();
        Assert.That(NativeDx11DirectBufferContract.MatchesProgram(program), Is.True);
        SemanticVerifier.VerifyReferences(program);
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Intrinsic == BoundIntrinsic.Dx11DirectBufferTryCreate));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var create = __nunitAssertValue2[0];
        Assert.That(create.NativeComMethod, Is.SameAs(NativeDx11DirectBufferContract.CreateBufferCom));
        var __nunitAssertValue3 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Name == NativeDx11DirectBufferContract.TypeName + ".Close"
                        && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
        Assert.That(__nunitAssertValue3, Has.Exactly(1).Items);
        var close = __nunitAssertValue3[0];
        Assert.That(close.NativeComMethod, Is.SameAs(NativeDx11DirectBufferContract.ReleaseCom));

        var code = DirectBufferCode(program);
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[4]));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var createRange = __nunitAssertValue4[0];
        var createBytes = code.Code.AsSpan(createRange.Start, createRange.End - createRange.Start).ToArray();
        Assert.That(CountCreateBufferSequence(createBytes, [0xFF, 0x50, 0x18]), Is.EqualTo(1));
        Assert.That(CountCreateBufferSequence(createBytes, [0xFF, 0x50, 0x10]), Is.EqualTo(1));
        var __nunitAssertValue5 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, range => range.Start == code.SourceFunctionOffsets[5]));
        Assert.That(__nunitAssertValue5, Has.Exactly(1).Items);
        var closeRange = __nunitAssertValue5[0];
        var closeBytes = code.Code.AsSpan(closeRange.Start, closeRange.End - closeRange.Start).ToArray();
        Assert.That(CountCreateBufferSequence(closeBytes, [0xFF, 0x50, 0x10]), Is.EqualTo(1));
    }

    [Test]
    public async Task DirectBufferCallsRealWarpCreateBufferWithoutAnApplicationHelper()
    {
        var program = CompileDirectBuffer();
        Assert.That(NativeDx11DirectBufferContract.MatchesProgram(program), Is.True);
        Assert.That(NativeDx11WarpAcquisitionContract.MatchesProgram(program), Is.False);
        var acquisitionProgram = CompileDirectWarpAcquisition();
        var __nunitAssertValue6 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(acquisitionProgram.Functions, static function => function.NativeImportKey == NativeDx11WarpAcquisitionContract.ImportKey));
        Assert.That(__nunitAssertValue6, Has.Exactly(1).Items);
        var acquisitionImport = __nunitAssertValue6[0];
        Assert.That(NativeImportCatalog.WindowsX64.MatchesFunction(acquisitionImport,
            NativeDx11WarpAcquisitionContract.Import, acquisitionProgram.Structures), Is.True);
        var __nunitAssertValue7 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Intrinsic == BoundIntrinsic.Dx11DirectBufferTryCreate));
        Assert.That(__nunitAssertValue7, Has.Exactly(1).Items);
        var createBuffer = __nunitAssertValue7[0];
        Assert.That(createBuffer.NativeComMethod, Is.SameAs(NativeDx11DirectBufferContract.CreateBufferCom));
        var __nunitAssertValue8 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.Name == NativeDx11DirectBufferContract.TypeName + ".Close"
                        && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
        Assert.That(__nunitAssertValue8, Has.Exactly(1).Items);
        var closeBuffer = __nunitAssertValue8[0];
        Assert.That(closeBuffer.NativeComMethod, Is.SameAs(NativeDx11DirectBufferContract.ReleaseCom));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.Identity, Is.EqualTo(NativeDx11ComCatalog.CreateBufferIdentity));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.Effects, Is.EqualTo(NativeComEffects.SignedHResult | NativeComEffects.AcquiresReference
            | NativeComEffects.OutputMayBeWrittenOnFailure));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.OutputParameterIndex, Is.EqualTo(3));
        Assert.That(NativeDx11DirectBufferContract.CreateBufferCom.OutputInterfaceIdentity, Is.EqualTo(NativeDx11ComCatalog.Buffer));
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
        Assert.That(CountCreateBufferSequence(image, [0xFF, 0x50, 0x18]), Is.EqualTo(1));
        Assert.That(CountCreateBufferSequence(image, [0xFF, 0x50, 0x10]) >= 2, Is.True);
        var directory = Directory.CreateTempSubdirectory("terrum-direct-buffer-").FullName;
        try
        {
            var executable = Path.Combine(directory, "direct-buffer.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(false);
            for (var iteration = 0; iteration < 4; iteration++)
            {
                await RunAsync(new ProcessStartInfo(executable) { WorkingDirectory = directory },
                    TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase("uint32 byteWidth = 36;", "uint32 byteWidth = 35;")]
    [TestCase("uint32 byteWidth = 36;", "uint32 byteWidth = 37;")]
    [TestCase("uint32 usage = 1;", "uint32 usage = 0;")]
    [TestCase("uint32 bindFlags = 1;", "uint32 bindFlags = 2;")]
    [TestCase("uint32 cpuAccessFlags = 0;", "uint32 cpuAccessFlags = 1;")]
    [TestCase("uint32 miscFlags = 0;", "uint32 miscFlags = 1;")]
    [TestCase("uint32 structureByteStride = 0;", "uint32 structureByteStride = 12;")]
    [TestCase("uint32 sysMemPitch = 0;", "uint32 sysMemPitch = 12;")]
    [TestCase("uint32 sysMemSlicePitch = 0;", "uint32 sysMemSlicePitch = 36;")]
    [TestCase("float32 Z;", "uint32 Z;")]
    [TestCase("float32 X;", "mut float32 X;")]
    [TestCase("[Layout(Sequential)]", "")]
    [TestCase("new Vertex(0, 0.6, 0.5)", "new Vertex(0, 0.7, 0.5)")]
    [TestCase("X = x;", "X = z;")]
    [TestCase("first, second, third,", "third, second, first,")]
    [TestCase("Vertex first =", "mut Vertex first =")]
    [TestCase("byteWidth, usage, bindFlags,", "byteWidth, bindFlags, usage,")]
    [TestCase("device.TryCreateImmutableVertexBuffer(", "context.TryCreateImmutableVertexBuffer(")]
    [TestCase("result = 0;", "buffer.Close(); result = 0;")]
    [TestCase("result = 0;", "uint64 pointer = (uint64)buffer; result = 0;")]
    [TestCase("result = 0;", "uint64 pointer = &buffer; result = 0;")]
    [TestCase("result = 0;", "uint64 pointer = &first; result = 0;")]
    [TestCase("result = 0;", "Terrum.Interop.Dx11.ProvisionalBufferOwner copy = buffer; result = 0;")]
    [TestCase("result = 0;", "buffer = move(buffer); result = 0;")]
    [TestCase("result = 0;", "device.Close(); result = 0;")]
    [TestCase("result = 0;", "DestroyWindow(window); result = 0;")]
    [TestCase("result = 0;", "return 0;")]
    [TestCase("out var buffer", "out buffer")]
    [TestCase("out var device, out var swapchain", "out var swapchain, out var device")]
    [TestCase("return message == 129 ? 1 : 0;", "return message == 129 ? 0 : 1;")]
    [TestCase("int32 destroyed = DestroyWindow(window);", "int32 destroyed = 1;")]
    [TestCase("if (registered == 0) {", "if (registered != 0) {")]
    [TestCase("if (window == Terrum.Interop.Windows.HWND.NoOwner) {",
        "if (window != Terrum.Interop.Windows.HWND.NoOwner) {")]
    [TestCase("    return result;", "    return result;\n}\nvoid Other() {")]
    public void DirectBufferRejectsSourceLayoutDescriptorEscapeAndLifecycleChanges(string original, string replacement)
    {
        var source = DirectBufferSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var mutation = source.Replace(original, replacement, StringComparison.Ordinal);
        Assert.That(Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutation)],
            runtimePolicy: BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [TestCase("struct")]
    [TestCase("class")]
    [TestCase("alias")]
    public void DirectBufferOwnerIdentityIsReservedOnlyByItsCatalog(string declaration)
    {
        var ownerDeclaration = declaration switch {
            "alias" => "alias ProvisionalBufferOwner = uint64;",
            "class" => "class ProvisionalBufferOwner { uint64 Raw; ProvisionalBufferOwner(uint64 raw) { Raw = raw; } }",
            _ => "struct ProvisionalBufferOwner { uint64 Raw; }",
        };
        var source = "namespace Terrum.Interop.Dx11;\n" + ownerDeclaration
            + "\nint32 Main() { return 0; }";
        var tree = SyntaxTree.Parse(source);
        var ordinaryCompilation = Compilation.Create(tree);
        Assert.That(ordinaryCompilation.Program is not null, Is.True, string.Join(Environment.NewLine,
            ordinaryCompilation.Diagnostics));
        Assert.That(Compilation.CreateWithNativeImports([tree],
            runtimePolicy: BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [Test]
    public void DirectBufferRejectsBoundSlotLayoutPublicationAndReleaseOrderForgeries()
    {
        var program = CompileDirectBuffer();
        var main = program.Functions[13];
        var __nunitAssertValue9 = main.Body.Statements[3];
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundIf>());
        var registration = ((BoundIf)__nunitAssertValue9);
        var __nunitAssertValue10 = registration.Else;
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundBlock>());
        var registeredBody = ((BoundBlock)__nunitAssertValue10);
        var __nunitAssertValue11 = registeredBody.Statements[1];
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundIf>());
        var windowFailure = ((BoundIf)__nunitAssertValue11);
        var __nunitAssertValue12 = windowFailure.Condition;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundNativeWindowHandleIsNoOwner>());
        var noOwnerTest = ((BoundNativeWindowHandleIsNoOwner)__nunitAssertValue12);
        var __nunitAssertValue13 = windowFailure.Else;
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundBlock>());
        var windowCreatedBody = ((BoundBlock)__nunitAssertValue13);
        var __nunitAssertValue14 = windowCreatedBody.Statements[0];
        Assert.That(__nunitAssertValue14, Is.TypeOf<BoundBlock>());
        var scope = ((BoundBlock)__nunitAssertValue14);
        var __nunitAssertValue15 = scope.Statements[1];
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundTryFinally>());
        var deviceCleanup = ((BoundTryFinally)__nunitAssertValue15);
        var __nunitAssertValue16 = deviceCleanup.Body.Statements[1];
        Assert.That(__nunitAssertValue16, Is.TypeOf<BoundTryFinally>());
        var swapCleanup = ((BoundTryFinally)__nunitAssertValue16);
        var __nunitAssertValue17 = swapCleanup.Body.Statements[1];
        Assert.That(__nunitAssertValue17, Is.TypeOf<BoundTryFinally>());
        var contextCleanup = ((BoundTryFinally)__nunitAssertValue17);
        var __nunitAssertValue18 = global::System.Linq.Enumerable.ToArray(contextCleanup.Body.Statements);
        Assert.That(__nunitAssertValue18, Has.Exactly(1).Items);
        var __nunitAssertValue19 = __nunitAssertValue18[0];
        Assert.That(__nunitAssertValue19, Is.TypeOf<BoundIf>());
        var acquisition = ((BoundIf)__nunitAssertValue19);
        var __nunitAssertValue20 = acquisition.Then;
        Assert.That(__nunitAssertValue20, Is.TypeOf<BoundBlock>());
        var body = ((BoundBlock)__nunitAssertValue20);
        var __nunitAssertValue21 = body.Statements[12];
        Assert.That(__nunitAssertValue21, Is.TypeOf<BoundTryFinally>());
        var bufferCleanup = ((BoundTryFinally)__nunitAssertValue21);
        var __nunitAssertValue22 = global::System.Linq.Enumerable.ToArray(bufferCleanup.Body.Statements);
        Assert.That(__nunitAssertValue22, Has.Exactly(1).Items);
        var __nunitAssertValue23 = __nunitAssertValue22[0];
        Assert.That(__nunitAssertValue23, Is.TypeOf<BoundIf>());
        var bufferBranch = ((BoundIf)__nunitAssertValue23);
        var __nunitAssertValue24 = bufferBranch.Condition;
        Assert.That(__nunitAssertValue24, Is.TypeOf<BoundCall>());
        var create = ((BoundCall)__nunitAssertValue24);
        var method = program.Functions[4];
        var close = program.Functions[5];
        BoundProgram ReplaceWindowFailure(BoundIf replacement)
        {
            var newRegisteredBody = registeredBody with {
                Statements = registeredBody.Statements.SetItem(1, replacement),
            };
            return program with {
                Functions = program.Functions.SetItem(13, main with {
                    Body = main.Body with {
                        Statements = main.Body.Statements.SetItem(3,
                            registration with { Else = newRegisteredBody }),
                    },
                }),
            };
        }
        BoundProgram ReplaceBody(BoundBlock replacement)
        {
            var newContext = contextCleanup with {
                Body = contextCleanup.Body with { Statements = [acquisition with { Then = replacement }] },
            };
            var newSwap = swapCleanup with {
                Body = swapCleanup.Body with { Statements = swapCleanup.Body.Statements.SetItem(1, newContext) },
            };
            var newDevice = deviceCleanup with {
                Body = deviceCleanup.Body with { Statements = deviceCleanup.Body.Statements.SetItem(1, newSwap) },
            };
            var newScope = scope with { Statements = scope.Statements.SetItem(1, newDevice) };
            var newWindowCreatedBody = windowCreatedBody with {
                Statements = windowCreatedBody.Statements.SetItem(0, newScope),
            };
            var newWindowFailure = windowFailure with { Else = newWindowCreatedBody };
            var newRegisteredBody = registeredBody with {
                Statements = registeredBody.Statements.SetItem(1, newWindowFailure),
            };
            return program with {
                Functions = program.Functions.SetItem(13, main with {
                    Body = main.Body with {
                        Statements = main.Body.Statements.SetItem(3,
                            registration with { Else = newRegisteredBody }),
                    },
                }),
            };
        }
        BoundProgram ReplaceCreate(BoundCall replacement) => ReplaceBody(body with {
            Statements = body.Statements.SetItem(12, bufferCleanup with {
                Body = bufferCleanup.Body with { Statements = [bufferBranch with { Condition = replacement }] },
            }),
        });
        var __nunitAssertValue25 = body.Statements[0];
        Assert.That(__nunitAssertValue25, Is.TypeOf<BoundLocal>());
        var vertex = ((BoundLocal)__nunitAssertValue25);
        var __nunitAssertValue26 = vertex.Initializer;
        Assert.That(__nunitAssertValue26, Is.TypeOf<BoundCall>());
        var vertexCall = ((BoundCall)__nunitAssertValue26);
        var __nunitAssertValue27 = body.Statements[3];
        Assert.That(__nunitAssertValue27, Is.TypeOf<BoundLocal>());
        var desc = ((BoundLocal)__nunitAssertValue27);
        var __nunitAssertValue28 = body.Statements[9];
        Assert.That(__nunitAssertValue28, Is.TypeOf<BoundLocal>());
        var pitch = ((BoundLocal)__nunitAssertValue28);
        var __nunitAssertValue29 = create.Arguments[12];
        Assert.That(__nunitAssertValue29, Is.TypeOf<BoundOutArgument>());
        var output = ((BoundOutArgument)__nunitAssertValue29);
        var forgeries = new[] {
            program with { NativeImportCatalog = null },
            program with { NativeImportCatalogId = "test-promoted-to-production" },
            program with { RuntimePolicy = BoundRuntimePolicy.CheckedExceptions },
            program with { EntryPoint = 11 },
            program with { Structures = program.Structures.SetItem(3, program.Structures[3] with { IsUnique = false }) },
            program with { Structures = program.Structures.SetItem(3, program.Structures[3] with { Size = 16 }) },
            program with { Structures = program.Structures.SetItem(3, program.Structures[3] with { CleanupMethod = program.Structures[0].CleanupMethod }) },
            program with { Structures = program.Structures.SetItem(3, program.Structures[3] with { Fields = [new("Raw", PrimitiveType.UInt64, 0, 8)] }) },
            program with { Structures = program.Structures.SetItem(4, program.Structures[4] with {
                Fields = program.Structures[4].Fields.SetItem(1, program.Structures[4].Fields[1] with { Offset = 8 }),
            }) },
            program with { Structures = program.Structures.SetItem(4, program.Structures[4] with { IsSequential = false }) },
            program with { Functions = program.Functions.SetItem(4, method with {
                NativeComMethod = NativeDx11DirectBufferContract.CreateBufferCom with { Slot = 4 },
            }) },
            program with { Functions = program.Functions.SetItem(5, close with {
                NativeComMethod = NativeDx11DirectBufferContract.ReleaseCom with { Slot = 1 },
            }) },
            program with { Functions = program.Functions.SetItem(4, method with { Intrinsic = BoundIntrinsic.None }) },
            program with { Functions = program.Functions.SetItem(4, method with { IsNoThrow = false }) },
            program with { Functions = program.Functions.SetItem(5, close with { IsCleanup = false }) },
            program with { Functions = program.Functions.SetItem(4, method with {
                Variables = method.Variables.SetItem(12, method.Variables[12] with { IsOut = false }),
            }) },
            program with { Functions = program.Functions.SetItem(13, main with {
                Variables = main.Variables.SetItem(7, main.Variables[7] with { IsMutable = true }),
            }) },
            program with { Functions = program.Functions.SetItem(11, program.Functions[11] with { IsStatic = true }) },
            program with { Functions = program.Functions.SetItem(11, program.Functions[11] with { Variables = default }) },
            program with { Functions = program.Functions.SetItem(13, main with { Variables = default }) },
            program with { Functions = program.Functions.SetItem(13, main with { IsInternal = false }) },
            ReplaceWindowFailure(windowFailure with { Condition = noOwnerTest with { IsNegated = true } }),
            ReplaceWindowFailure(windowFailure with { Else = null }),
            ReplaceWindowFailure(windowFailure with { Then = new BoundBlock([], default) }),
            ReplaceBody(body with { Statements = body.Statements.SetItem(0, vertex with {
                Initializer = vertexCall with { Arguments = vertexCall.Arguments.SetItem(0,
                    new BoundConstant(1, PrimitiveType.Float32, default)) },
            }) }),
            ReplaceBody(body with { Statements = body.Statements.SetItem(3, desc with {
                Initializer = new BoundConstant(37, PrimitiveType.UInt32, default),
            }) }),
            ReplaceBody(body with { Statements = body.Statements.SetItem(9, pitch with {
                Initializer = new BoundConstant(12, PrimitiveType.UInt32, default),
            }) }),
            ReplaceBody(body with { Statements = body.Statements.RemoveAt(0) }),
            ReplaceBody(body with { Statements = body.Statements.SetItem(12, bufferCleanup.Body) }),
            ReplaceBody(body with { Statements = body.Statements.SetItem(12, bufferCleanup with {
                Finally = new BoundBlock([new BoundDrop(6, 3, default)], default),
            }) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(0, new BoundRead(4, (PrimitiveType)100, default)) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(0, new BoundReceiverLoan(6, (PrimitiveType)102, default)) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(1, new BoundRead(8, (PrimitiveType)104, default)) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(12, output with { Variable = 4 }) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(12, output with { DeclaresVariable = false }) }),
            ReplaceCreate(create with { Arguments = create.Arguments.SetItem(12, output with { IsInit = true }) }),
            ReplaceCreate(create with { ArgumentParameterIndices = [0, 2, 1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12] }),
        };
        foreach (var forgery in forgeries)
        {
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forgery));
        }
        var __nunitAssertValue30 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue30, Is.TypeOf<BoundLocal>());
        var windowClassLocal = ((BoundLocal)__nunitAssertValue30);
        var __nunitAssertValue31 = windowClassLocal.Initializer;
        Assert.That(__nunitAssertValue31, Is.TypeOf<BoundNativeWindowClassExInitialization>());
        var windowClass = ((BoundNativeWindowClassExInitialization)__nunitAssertValue31);
        BoundExpression nestedModule = windowClass.Instance;
        for (var depth = 0; depth < 5_000; depth++)
        {
            nestedModule = new BoundCall(6, [nestedModule], PrimitiveType.NativeModuleHandle, default);
        }
        var deeplyForged = program with {
            Functions = program.Functions.SetItem(13, main with {
                Body = main.Body with {
                    Statements = main.Body.Statements.SetItem(0, windowClassLocal with {
                        Initializer = windowClass with { Instance = nestedModule },
                    })
                },
            }),
        };
        Assert.That(NativeDx11DirectBufferContract.MatchesProgram(deeplyForged), Is.False);
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(deeplyForged));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast));
        Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibrary(program, ["Main"], NativeRuntimePolicy.FailFast));
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.direct-buffer", [NativeDx11WarpAcquisitionContract.Import]));
        Assert.That(Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectBufferSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(DirectBufferSource), SyntaxTree.Parse("int32 Other() { return 0; }")],
            runtimePolicy: BoundRuntimePolicy.FailFast).Program, Is.Null);
        Assert.That(Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectBufferSource)],
            NativeImportCatalog.CreateForTesting("test.unrelated", [
                new("test.unrelated.Call", "test.dll", "Call", [], PrimitiveType.Int32,
                    true, true, false, NativeImportDeploymentPolicy.ApplicationDirectory),
            ]), BoundRuntimePolicy.FailFast).Program, Is.Null);
    }

    [Test]
    public async Task DirectBufferMatchesSdkAndCleansInjectedFailurePartialAndMissingOutputs()
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-direct-buffer-abi-").FullName;
        try
        {
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-direct-buffer-probe.c");
            var sdk = Path.Combine(root, "tests", "native", "dx11-direct-buffer-sdk-probe.c");
            var dll = Path.Combine(directory, "native-dx11-direct-buffer-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fosdk.obj \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" user32.lib\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            Assert.That(File.Exists(Path.Combine(directory, "sdk.obj")), Is.True);
            Assert.That(NativeDx11DirectBufferContract.DescriptorSize, Is.EqualTo(24));
            Assert.That(NativeDx11DirectBufferContract.DescriptorAlignment, Is.EqualTo(4));
            Assert.That(NativeDx11DirectBufferContract.DataSize, Is.EqualTo(16));
            Assert.That(NativeDx11DirectBufferContract.DataAlignment, Is.EqualTo(8));

            var program = CompileDirectBuffer();
            var code = DirectWarpAcquisitionCode(program);
            // Substitute test image fixups only; source, bound program and production catalog stay sealed.
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName switch {
                    "D3D11CreateDeviceAndSwapChain" => import with { DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectBufferAcquisition" },
                    "RegisterClassExW" => import with { DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectBufferRegisterClassExW" },
                    "CreateWindowExW" => import with { DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectBufferCreateWindowExW" },
                    "DestroyWindow" => import with { DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectBufferDestroyWindow" },
                    "UnregisterClassW" => import with { DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectBufferUnregisterClass" },
                    _ => import,
                })],
            };
            var executable = Path.Combine(directory, "injected.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(false);
            for (var mode = 0; mode <= 9; mode++)
            {
                var hasBuffer = mode <= 2 || mode is 4 or 5;
                var trace = "AV" + (hasBuffer ? "B" : "") + "CSDWUZ";
                for (var iteration = 0; iteration < 4; iteration++)
                {
                    await RunDirectBufferInjection(executable, directory, mode,
                        $"mode-{mode}-{iteration}", mode <= 2 ? 0 : 1, trace).ConfigureAwait(false);
                }
            }
            await RunDirectBufferInjection(executable, directory, 10, "registration-failure", 2, "RZ")
                .ConfigureAwait(false);
            await RunDirectBufferInjection(executable, directory, 11, "window-creation-failure", 5, "RCUZ")
                .ConfigureAwait(false);
            var __nunitAssertValue32 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, function => function.Start == code.SourceFunctionOffsets[4]));
            Assert.That(__nunitAssertValue32, Has.Exactly(1).Items);
            var createRange = __nunitAssertValue32[0];
            var __nunitAssertValue33 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, function => function.Start == code.SourceFunctionOffsets[5]));
            Assert.That(__nunitAssertValue33, Has.Exactly(1).Items);
            var closeRange = __nunitAssertValue33[0];
            Assert.That(createRange.StackSize & 15, Is.EqualTo(8));
            var createCall = new byte[] { 0x48, 0x8B, 0x01, 0xFF, 0x50, 24 };
            var releaseCall = new byte[] { 0x48, 0x8B, 0x01, 0xFF, 0x50, 16 };
            var wrongCreate = (byte[])injected.Code.Clone();
            var createOffset = wrongCreate.AsSpan(createRange.Start, createRange.End - createRange.Start).IndexOf(createCall);
            Assert.That(createOffset >= 0, Is.True);
            wrongCreate[createRange.Start + createOffset + 5] = 32;
            var wrongRelease = (byte[])injected.Code.Clone();
            var releaseOffset = wrongRelease.AsSpan(closeRange.Start, closeRange.End - closeRange.Start).IndexOf(releaseCall);
            Assert.That(releaseOffset >= 0, Is.True);
            wrongRelease[closeRange.Start + releaseOffset + 5] = 8;
            foreach (var mutation in new[] { ("wrong-create-slot", wrongCreate, "A"), ("wrong-release-slot", wrongRelease, "AV") })
            {
                var path = Path.Combine(directory, mutation.Item1 + ".exe");
                await File.WriteAllBytesAsync(path, PeImage.Build(injected with { Code = mutation.Item2 })).ConfigureAwait(false);
                await RunDirectBufferInjection(path, directory, 0, mutation.Item1,
                    unchecked((int)0xC0000409), mutation.Item3).ConfigureAwait(false);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task RunDirectBufferInjection(string executable, string directory, int mode,
        string name, int expectedExit, string expectedTrace)
    {
        var report = Path.Combine(directory, name + ".txt");
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory };
        start.Environment["TERRUM_DX11_MODE"] = mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["TERRUM_DX11_REPORT"] = report;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Direct buffer injection did not start.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(process.ExitCode, Is.EqualTo(expectedExit));
            Assert.That(await File.ReadAllTextAsync(report).ConfigureAwait(false), Is.EqualTo(expectedTrace));
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
