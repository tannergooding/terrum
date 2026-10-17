using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const uint ResizeWindowFlags = 0x0016;

    [StructLayout(LayoutKind.Sequential)]
    private struct ClientRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out ClientRect rectangle);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private static string ResizeRenderSource(bool hardware) => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
        "samples", "design", "win32-dx11-triangle",
        hardware ? "window-device-hardware-resize-stage.terrum" : "window-device-warp-resize-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileSourceAuthoredResizeRender(bool hardware)
    {
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(ResizeRenderSource(hardware)),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program), Is.True);
        SemanticVerifier.VerifyReferences(program);
        return program;
    }

    [Test]
    public void Dx11SampleEntryPointUsesExactWarpResizeRenderSeals()
    {
        AssertDx11SampleEntryPointUsesExactResizeRenderSeals("main.terrum", hardware: false);
    }

    [Test]
    public void Dx11HardwareSampleEntryPointUsesExactHardwareResizeRenderSeals()
    {
        AssertDx11SampleEntryPointUsesExactResizeRenderSeals("main-hardware.terrum", hardware: true);
    }

    private static BoundProgram AssertDx11SampleEntryPointUsesExactResizeRenderSeals(string sourceName, bool hardware)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            "samples", "design", "win32-dx11-triangle", sourceName))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.That(source, Is.EqualTo(ResizeRenderSource(hardware)));
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue1 = compilation.Program;
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue1);
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program), Is.True);
        Assert.That(program.Functions[0].Intrinsic, Is.EqualTo(hardware ? BoundIntrinsic.Dx11HardwareTryCreate : BoundIntrinsic.Dx11WarpTryCreate));
        SemanticVerifier.VerifyReferences(program);
        Assert.That(NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
        return program;
    }

    [Test]
    public void Dx11SourceAuthoredResizeCallbackRejectsMessageAndQuitMutations()
    {
        var program = AssertDx11SampleEntryPointUsesExactResizeRenderSeals("main.terrum", hardware: false);
        var callbackIndex = program.Functions.IndexOf(
            program.Functions.Single(static function => function.Name == NativeWindowOwnerContract.CallbackName));
        var callback = program.Functions[callbackIndex];
        BoundProgram ReplaceCallback(BoundFunction replacement) => program with {
            Functions = program.Functions.SetItem(callbackIndex, replacement),
        };
        var __nunitAssertValue2 = callback.Body.Statements[0];
        Assert.That(__nunitAssertValue2, Is.TypeOf<BoundIf>());
        var sizeBranch = ((BoundIf)__nunitAssertValue2);
        var __nunitAssertValue3 = sizeBranch.Then;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundBlock>());
        var sizeBody = ((BoundBlock)__nunitAssertValue3);
        var __nunitAssertValue4 = sizeBody.Statements[0];
        Assert.That(__nunitAssertValue4, Is.TypeOf<BoundIf>());
        var postBranch = ((BoundIf)__nunitAssertValue4);
        var __nunitAssertValue5 = postBranch.Condition;
        Assert.That(__nunitAssertValue5, Is.TypeOf<BoundBinary>());
        var postCondition = ((BoundBinary)__nunitAssertValue5);
        var __nunitAssertValue6 = postCondition.Left;
        Assert.That(__nunitAssertValue6, Is.TypeOf<BoundCall>());
        var postMessage = ((BoundCall)__nunitAssertValue6);
        var __nunitAssertValue7 = postMessage.Arguments[1];
        Assert.That(__nunitAssertValue7, Is.TypeOf<BoundConstant>());
        var appMessage = ((BoundConstant)__nunitAssertValue7);
        var changedPost = postMessage with {
            Arguments = postMessage.Arguments.SetItem(1, appMessage with { Value = 32769 }),
        };
        var changedPostBranch = postBranch with {
            Condition = postCondition with { Left = changedPost },
        };
        var changedSizeBody = sizeBody with {
            Statements = sizeBody.Statements.SetItem(0, changedPostBranch),
        };
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(ReplaceCallback(callback with {
            Body = callback.Body with {
                Statements = callback.Body.Statements.SetItem(0,
                sizeBranch with { Then = changedSizeBody })
            },
        })), Is.False);
        var __nunitAssertValue8 = callback.Body.Statements[1];
        Assert.That(__nunitAssertValue8, Is.TypeOf<BoundIf>());
        var closeBranch = ((BoundIf)__nunitAssertValue8);
        var __nunitAssertValue9 = closeBranch.Then;
        Assert.That(__nunitAssertValue9, Is.TypeOf<BoundBlock>());
        var closeBody = ((BoundBlock)__nunitAssertValue9);
        var __nunitAssertValue10 = closeBody.Statements[1];
        Assert.That(__nunitAssertValue10, Is.TypeOf<BoundIf>());
        var closeGuard = ((BoundIf)__nunitAssertValue10);
        var __nunitAssertValue11 = closeGuard.Condition;
        Assert.That(__nunitAssertValue11, Is.TypeOf<BoundBinary>());
        var closeCondition = ((BoundBinary)__nunitAssertValue11);
        var __nunitAssertValue12 = closeCondition.Left;
        Assert.That(__nunitAssertValue12, Is.TypeOf<BoundCall>());
        var closePeek = ((BoundCall)__nunitAssertValue12);
        var __nunitAssertValue13 = closePeek.Arguments[4];
        Assert.That(__nunitAssertValue13, Is.TypeOf<BoundConstant>());
        var remove = ((BoundConstant)__nunitAssertValue13);
        var changedPeek = closePeek with {
            Arguments = closePeek.Arguments.SetItem(4, remove with { Value = 1 }),
        };
        var changedGuard = closeGuard with {
            Condition = closeCondition with { Left = changedPeek },
        };
        var changedCloseBody = closeBody with {
            Statements = closeBody.Statements.SetItem(1, changedGuard),
        };
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(ReplaceCallback(callback with {
            Body = callback.Body with {
                Statements = callback.Body.Statements.SetItem(1,
                closeBranch with { Then = changedCloseBody })
            },
        })), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Dx11SourceAuthoredResizeRenderUsesCataloguedComMethods(bool hardware)
    {
        var source = ResizeRenderSource(hardware);
        var program = CompileSourceAuthoredResizeRender(hardware);
        Assert.That(program.Functions[0].Intrinsic, Is.EqualTo(hardware ? BoundIntrinsic.Dx11HardwareTryCreate : BoundIntrinsic.Dx11WarpTryCreate));
        var __nunitAssertValue14 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeComMethod?.VTableMethodIdentity
                        == NativeDx11ComCatalog.ResizeBuffersIdentity));
        Assert.That(__nunitAssertValue14, Has.Exactly(1).Items);
        var resizeFunction = __nunitAssertValue14[0];
        Assert.That(resizeFunction.Intrinsic, Is.EqualTo(BoundIntrinsic.NativeComVTableCall));
        var resizeFunctionIndex = program.Functions.IndexOf(resizeFunction);
        var foundBoundResizeCall = false;
        _ = BoundTreeTraversal.RewriteCalls(
            program.Functions[NativeDx11DirectPipelineContract.RunIndex], call => {
                foundBoundResizeCall |= call.Function == resizeFunctionIndex
                    && call.NativeComCall?.Method == resizeFunction.NativeComMethod;
                return call;
            });
        Assert.That(foundBoundResizeCall, Is.True);

        var code = DirectWindowDeviceShaderCode(program);
        Assert.That(code.NativeImports, Has.Some.Matches(static import => import.ExportName == "GetClientRect"));
        Assert.That(code.NativeImports, Has.None.Matches(static import => import.ExportName.StartsWith("NativeFfi",
            StringComparison.Ordinal)));
        Assert.That(NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions), Is.Not.Empty);
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Dx11SourceAuthoredResizeRenderDoesNotRequireLegacySourceSeal(bool hardware)
    {
        var source = ResizeRenderSource(hardware).Replace(
            "mut bool redrawPending = true;",
            "mut bool redrawPending = true; // renderer policy stays in source",
            StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue15 = compilation.Program;
        Assert.That(__nunitAssertValue15, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue15);
        Assert.That(NativeDx11DirectPipelineContract.MatchesProgram(program), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CheckedResizePipelineAllowsAdditionalOrdinarySource(bool hardware)
    {
        var tree = SyntaxTree.Parse(ResizeRenderSource(hardware));
        foreach (var compilation in new[] {
            Compilation.Create(tree, BoundRuntimePolicy.CheckedExceptions),
            Compilation.CreateWithNativeImports(tree, BoundRuntimePolicy.FailFast),
        })
        {
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        var multiSourceCompilation = Compilation.CreateWithNativeImports(
            [tree, SyntaxTree.Parse("void Other() { }")], BoundRuntimePolicy.CheckedExceptions);
        Assert.That(multiSourceCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, multiSourceCompilation.Diagnostics));
        Assert.That(multiSourceCompilation.Program, Is.TypeOf<BoundProgram>());

        var binder = new Binder([tree], [], default, true, null, BoundRuntimePolicy.CheckedExceptions);
        Assert.That(binder.Bind(), Is.Null);
        Assert.That(binder.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }

    [Test]
    public void Dx11ResizeRenderDoesNotReserveOrdinaryOperationNames()
    {
        const string source = """
            int32 dx11resizebegin(int32 value) { return value + 1; }
            int32 dx11resizenext(int32 value) { return value + 1; }
            int32 dx11resizeorredrawnext(int32 value) { return value + 1; }
            int32 dx11resizeviewport(int32 value) { return value + 1; }
            int32 dx11resizeresult(int32 value) { return value + 1; }
            int32 Main() { return dx11resizeresult(dx11resizeviewport(dx11resizeorredrawnext(dx11resizenext(dx11resizebegin(0))))); }
            """;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());
    }

    [Test]
    public void Dx11ResizeRenderCatalogSealsPostAndCloseGuardImports()
    {
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(NativeWindowMessageContract.PostMessageKey,
            out var post), Is.True);
        Assert.That(post, Is.Not.Null);
        Assert.That(NativeWindowMessageContract.MatchesImport(post), Is.True);
        Assert.That(post.DllName, Is.EqualTo("user32.dll"));
        Assert.That(post.ExportName, Is.EqualTo("PostMessageW"));
        Assert.That(post.DeploymentPolicy, Is.EqualTo(NativeImportDeploymentPolicy.System32));
        Assert.That(post.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        Assert.That(post.ParameterTypes.AsEnumerable(), Is.EqualTo(new[] { PrimitiveType.NativeWindowHandle, PrimitiveType.UInt32, PrimitiveType.UInt64,
            PrimitiveType.Int64 }));
        Assert.That(post.ParameterContracts.AsEnumerable(), Is.EqualTo(new[] { NativeImportParameterContract.WindowProcedureBorrowedHandle,
            NativeImportParameterContract.Value, NativeImportParameterContract.Value,
            NativeImportParameterContract.Value }));
        Assert.That(post.IsNoThrow, Is.True);
        Assert.That(post.RequiresUnsafeCaller, Is.False);
        Assert.That(post.DoesNotReturn, Is.False);
        var peek = NativeWindowMessageContract.Imports[7];
        Assert.That(NativeImportCatalog.WindowsX64.TryGetImport(NativeWindowMessageContract.PeekMessageKey,
            out var cataloguedPeek), Is.True);
        Assert.That(cataloguedPeek, Is.EqualTo(peek));
        foreach (var forged in new[] {
            post with { Key = "test.PostMessageW" },
            post with { DllName = "callback.dll" },
            post with { ExportName = "PostMessageA" },
            post with { DeploymentPolicy = NativeImportDeploymentPolicy.ApplicationDirectory },
            post with { ReturnType = PrimitiveType.Int64 },
            post with { IsNoThrow = false }, post with { RequiresUnsafeCaller = true },
            post with { DoesNotReturn = true },
            post with { ParameterTypes = post.ParameterTypes.SetItem(2, PrimitiveType.UInt32) },
            post with { ParameterTypes = post.ParameterTypes.SetItem(3, PrimitiveType.UInt64) },
            post with { ParameterContracts = post.ParameterContracts.SetItem(0,
                NativeImportParameterContract.NoOwnerWindowHandle) },
            peek with { ParameterTypes = peek.ParameterTypes.RemoveAt(4) },
            peek with { DeploymentPolicy = NativeImportDeploymentPolicy.ApplicationDirectory },
        })
        {
            Assert.That(NativeWindowMessageContract.MatchesImport(forged), Is.False);
            Assert.Throws<ArgumentException>(() =>
                new NativeImportCatalog(NativeWindowMessageContract.CatalogIdentity, [forged]));
            Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting("forged-signal", [forged]));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Dx11SourceAuthoredResizeRenderRejectsForgedResizeMethodProvenance(bool hardware)
    {
        var program = CompileSourceAuthoredResizeRender(hardware);
        var __nunitAssertValue16 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeComMethod?.VTableMethodIdentity
                        == NativeDx11ComCatalog.ResizeBuffersIdentity));
        Assert.That(__nunitAssertValue16, Has.Exactly(1).Items);
        var resizeFunction = __nunitAssertValue16[0];
        var resizeFunctionIndex = program.Functions.IndexOf(resizeFunction);
        var found = false;
        var run = BoundTreeTraversal.RewriteCalls(
            program.Functions[NativeDx11DirectPipelineContract.RunIndex], call => {
                if (call.Function != resizeFunctionIndex)
                {
                    return call;
                }
                found = true;
                if (call.NativeComCall is not NativeComCallFacts facts)
                {
                    throw new InvalidOperationException("The catalogued resize call has no provenance.");
                }
                var forgedMethod = facts.Method with { Slot = facts.Method.Slot - 1 };
                return call with { NativeComCall = facts with { Method = forgedMethod } };
            });
        Assert.That(found, Is.True);
        var forged = program with {
            Functions = program.Functions.SetItem(NativeDx11DirectPipelineContract.RunIndex, run),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyReferences(forged));
        Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitWithNativeImports(forged, NativeRuntimePolicy.CheckedExceptions));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Dx11ResizeRenderAbiQueueAndRepeatedSurfaceLifetimeAreDeterministicAsync(bool hardware)
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(true);
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-resize-render-");
        try
        {
            var sdk = Path.Combine(root, "tests", "native", "dx11-resize-queue-sdk-probe.c");
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-resize-render-probe.c");
            var dll = Path.Combine(directory, "native-dx11-resize-render-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fosdk.obj \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /wd4191 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\"\"",
            };
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(true);
            Assert.That(File.Exists(Path.Combine(directory, "sdk.obj")), Is.True);
            File.Copy(Path.Combine(root, "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var code = DirectWindowDeviceShaderCode(CompileSourceAuthoredResizeRender(hardware));
            string[] intercepted = [
                "RegisterClassExW", "CreateWindowExW", "PeekMessageW", "GetMessageW", "TranslateMessage",
                "DispatchMessageW", "GetClientRect", "ShowWindow", "DestroyWindow", "UnregisterClassW",
                "LoadLibraryExW", "GetProcAddress", "FreeLibrary", "PostMessageW", "PostQuitMessage", "DefWindowProcW",
            ];
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import => intercepted.Contains(import.ExportName)
                    ? import with { DllName = Path.GetFileName(dll), ExportName = "NativeFfiResizeRender" + import.ExportName }
                    : import)],
            };
            var executable = Path.Combine(directory, "resize-source.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(true);
            const string prefix = "NH1AB2LVPh";
            const string bindings = "GRKcovlbtxydpKrg";
            const string frame = "eKz" + bindings;
            const string redraw = "e" + bindings;
            const string suffix = "KYXIjBCSD6WUZ";
            const string initial = "qE";
            const string next = "wE";
            const string paintNext = "wtfE";
            const string firstExtent = "800x600";
            const string resizedExtents = firstExtent + ";1280x720";
            const string redrawnExtents = firstExtent + ";" + firstExtent;
            const string none = "none";
            (string Mode, string Body, int Exit, string Frames, string Resizes)[] cases = [
                ("initial", initial + frame + "w", 0, firstExtent, firstExtent),
                ("get-quit", initial + frame + "w", 0, firstExtent, firstExtent),
                ("resize", initial + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("positive-success", initial + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("coalesced", initial + frame + "wqqqE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("full-width", initial + frame + next + frame + "w", 0, firstExtent + ";70000x80000", firstExtent + ";70000x80000"),
                ("unchanged", initial + frame + next + "ew", 0, firstExtent, firstExtent),
                ("restore-unchanged", initial + frame + next + "wEew", 0, firstExtent, firstExtent),
                ("zero-restore-unchanged", initial + frame + next + "ewEew", 0, firstExtent, firstExtent),
                ("foreign", initial + frame + "wtfqtfEw", 0, firstExtent, firstExtent),
                ("minimized", initial + next + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("minimized-last", "qqEwE" + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("zero-width", initial + "e" + next + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("zero-height", initial + "e" + next + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("zero-both", initial + "e" + next + frame + next + frame + "w", 0, resizedExtents, resizedExtents),
                ("peek-quit", "q", 0, none, none), ("pending-quit", "qq", 0, none, none),
                ("quit-after-frame", initial + frame + "wq", 0, firstExtent, firstExtent),
                ("quit-error-peek", "qq", 1, none, none),
                ("quit-error-get", initial + frame + "w", 1, firstExtent, firstExtent),
                ("unrelated-messages", initial + frame + "wtfqtfqtfEw", 0, firstExtent, firstExtent),
                ("callback-get", initial + frame + "sawE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("callback-wide-wparam", initial + frame + "sawE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("callback-coalesced", initial + frame + "sasasasasasawqqqqqE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("callback-minimized", initial + frame + "sawEsawE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("callback-peek-post-failure", initial + frame + "wtfsauQq", 1, firstExtent, firstExtent),
                ("callback-get-post-failure", initial + frame + "sauQw", 1, firstExtent, firstExtent),
                ("callback-post-failure-close", initial + frame + "sauQuw", 1, firstExtent, firstExtent),
                ("callback-dispatch", initial + frame + "wtsafqE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("callback-dispatch-post-failure", initial + frame + "wtsauQfq", 1, firstExtent, firstExtent),
                ("callback-show", "saqqE" + frame + "w", 0, firstExtent, firstExtent),
                ("callback-show-post-failure", "sauQq", 1, none, none),
                ("callback-create-post-failure", "q", 1, none, none),
                ("callback-delegate", initial + frame + "w", 0, firstExtent, firstExtent),
                ("callback-close", initial + frame + "uQw", 0, firstExtent, firstExtent),
                ("callback-close-post-failure", initial + frame + "uQsauQw", 1, firstExtent, firstExtent),
                ("message-error", initial + frame + next + frame + "w", 1, resizedExtents, resizedExtents),
                ("query-error", initial + frame + next + "e", 1, firstExtent, firstExtent),
                ("negative-width", initial + frame + next + "e", 1, firstExtent, firstExtent),
                ("negative-height", initial + frame + next + "e", 1, firstExtent, firstExtent),
                ("left", initial + frame + next + "e", 1, firstExtent, firstExtent),
                ("top", initial + frame + next + "e", 1, firstExtent, firstExtent),
                ("resize-failure", initial + frame + next + "eKz", 1, firstExtent, resizedExtents),
                ("get-failure", initial + frame + next + "eKzG", 1, resizedExtents, resizedExtents),
                ("get-missing", initial + frame + next + "eKzG", 1, resizedExtents, resizedExtents),
                ("get-partial", initial + frame + next + "eKzGg", 1, resizedExtents, resizedExtents),
                ("initial-get-partial", initial + "eKzGg", 1, firstExtent, firstExtent),
                ("rtv-failure", initial + frame + next + "eKzGRg", 1, resizedExtents, resizedExtents),
                ("rtv-missing", initial + frame + next + "eKzGRg", 1, resizedExtents, resizedExtents),
                ("rtv-partial", initial + frame + next + "eKzGRrg", 1, resizedExtents, resizedExtents),
                ("initial-rtv-partial", initial + "eKzGRrg", 1, firstExtent, firstExtent),
                ("present-failure", initial + frame + next + frame, 1, resizedExtents, resizedExtents),
                ("paint-initial", "qtfqtf" + initial + frame + "w", 0, firstExtent, firstExtent),
                ("paint-get", initial + frame + paintNext + redraw + "w", 0, redrawnExtents, firstExtent),
                ("paint-peek", initial + frame + "wtfqtfE" + redraw + "w", 0, redrawnExtents, firstExtent),
                ("paint-coalesced", initial + frame + "wtfqtfqtfE" + redraw + "w", 0, redrawnExtents, firstExtent),
                ("paint-repeat", initial + frame + paintNext + redraw + paintNext + redraw + paintNext + redraw + "w",
                    0, redrawnExtents + ";" + firstExtent + ";" + firstExtent, firstExtent),
                ("paint-then-resize", initial + frame + paintNext + redraw + next + frame + "w",
                    0, redrawnExtents + ";1280x720", resizedExtents),
                ("resize-then-paint", initial + frame + next + frame + paintNext + redraw + "w",
                    0, resizedExtents + ";1280x720", resizedExtents),
                ("paint-unchanged-size", initial + frame + "wtfqqtfE" + redraw + "w", 0, redrawnExtents, firstExtent),
                ("paint-foreign", initial + frame + "wtfqtfqtfEw", 0, firstExtent, firstExtent),
                ("paint-resize", initial + frame + "wtfqqE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("resize-paint", initial + frame + "wqtfqqtfE" + frame + "w", 0, resizedExtents, resizedExtents),
                ("paint-final-rect", initial + frame + "wtfqqqtfE" + frame + "w", 0, firstExtent + ";1600x900", firstExtent + ";1600x900"),
                ("paint-extent-change", initial + frame + paintNext + frame + "w", 0, resizedExtents, resizedExtents),
                ("paint-minimized-stale", initial + frame + next + "wtfqtfE" + next + "e" + paintNext + "w", 0, firstExtent, firstExtent),
                ("paint-zero-stale", initial + frame + next + "e" + "wtfqtfE" + "e" + next + "e" + paintNext + "w", 0, firstExtent, firstExtent),
                ("paint-pending-quit", "qtfq", 0, none, none),
                ("paint-quit-after-frame", initial + frame + "wtfq", 0, firstExtent, firstExtent),
                ("paint-message-error", initial + frame + paintNext + redraw + "w", 1, redrawnExtents, firstExtent),
                ("paint-query-error", initial + frame + paintNext + "e", 1, firstExtent, firstExtent),
                ("paint-negative-width", initial + frame + paintNext + "e", 1, firstExtent, firstExtent),
                ("paint-negative-height", initial + frame + paintNext + "e", 1, firstExtent, firstExtent),
                ("paint-left", initial + frame + paintNext + "e", 1, firstExtent, firstExtent),
                ("paint-top", initial + frame + paintNext + "e", 1, firstExtent, firstExtent),
                ("paint-get-failure", initial + frame + paintNext + "eG", 1, redrawnExtents, firstExtent),
                ("paint-get-missing", initial + frame + paintNext + "eG", 1, redrawnExtents, firstExtent),
                ("paint-get-partial", initial + frame + paintNext + "eGg", 1, redrawnExtents, firstExtent),
                ("paint-rtv-failure", initial + frame + paintNext + "eGRg", 1, redrawnExtents, firstExtent),
                ("paint-rtv-missing", initial + frame + paintNext + "eGRg", 1, redrawnExtents, firstExtent),
                ("paint-rtv-partial", initial + frame + paintNext + "eGRrg", 1, redrawnExtents, firstExtent),
                ("paint-present-failure", initial + frame + paintNext + redraw, 1, redrawnExtents, firstExtent),
            ];
            cases = [.. cases, ("acquire-failure", "", 1, none, none)];
            foreach (var (mode, body, expected, frames, resizes) in cases)
            {
                var report = Path.Combine(directory, mode + ".txt");
                var start = new ProcessStartInfo(executable) {
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                };
                start.Environment["TERRUM_DX11_RESIZE_RENDER_MODE"] = mode;
                start.Environment["TERRUM_DX11_RESIZE_RENDER_REPORT"] = report;
                start.Environment["TERRUM_DX11_RESIZE_RENDER_FRAMES"] = frames;
                start.Environment["TERRUM_DX11_RESIZE_RENDER_RESIZES"] = resizes;
                start.Environment.Remove("TERRUM_DX11_RESIZE_TEXTURE_EVIDENCE");
                start.Environment.Remove("TERRUM_DX11_RESIZE_HARDWARE");
                if (hardware)
                { start.Environment["TERRUM_DX11_RESIZE_HARDWARE"] = "1"; }
                using var process = Process.Start(start)
                    ?? throw new InvalidOperationException("The deterministic resize renderer did not start.");
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(true);
                    var errors = await stderr.ConfigureAwait(true);
                    Assert.That(File.Exists(report), Is.True, $"{hardware}/{mode}, exit {process.ExitCode}: {errors}");
                    var trace = await File.ReadAllTextAsync(report).ConfigureAwait(true);
                    Assert.That(process.ExitCode == expected, Is.True, $"{hardware}/{mode}: expected {expected}, exit {process.ExitCode}, trace {trace}: {errors}");
                    var setup = mode switch {
                        "callback-create-post-failure" => "NHsauQ1AB2LVPh",
                        "callback-delegate" => "NHd1AB2LVPh",
                        _ => prefix,
                    };
                    var expectedTrace = mode == "acquire-failure" ? "NH1ACSD6WUZ" : setup + body + suffix;
                    Assert.That(trace == expectedTrace, Is.True, $"{hardware}/{mode}: expected {expectedTrace}, actual {trace}");
                    Assert.That(errors, Is.Empty);
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().ConfigureAwait(true);
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public Task DirectWarpResizeStageSurvivesClientResizeAsync() =>
        RunResizeStageAsync(hardware: false);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public Task DirectHardwareResizeStageSurvivesClientResizeAsync() =>
        RunResizeStageAsync(hardware: true);

    [HardwareIntegrationTest]
    [Category("HardwareIntegration")]
    public async Task DirectHardwareSampleEntryPointSurvivesResizeAndClosesNormallyAsync()
    {
        var directory = TestArtifactPaths.CreateDirectory("terrum-dx11-hardware-sample-");
        try
        {
            var executable = Path.Combine(directory, "triangle-hardware.exe");
            await CliTests.BuildDx11PipelineAsync("main-hardware.terrum", executable).ConfigureAwait(true);
            File.Copy(Path.Combine(FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle",
                "triangle.hlsl"), Path.Combine(directory, "triangle.hlsl"));
            Assert.That(Directory.EnumerateFiles(directory, "*.dll"), Is.Empty);
            await AssertDx11ResizeApplicationRunsAsync(executable, directory, "hardware sample")
                .ConfigureAwait(true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private async Task RunResizeStageAsync(bool hardware)
    {
        var driver = hardware ? "hardware" : "warp";
        var driverLabel = hardware ? "Hardware" : "WARP";
        var program = CompileSourceAuthoredResizeRender(hardware);
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        var directory = TestArtifactPaths.CreateDirectory($"terrum-real-{driver}-resize-");
        try
        {
            File.Copy(Path.Combine(FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "triangle.hlsl"),
                Path.Combine(directory, "triangle.hlsl"));
            var executable = Path.Combine(directory, $"{driver}-resize.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(true);
            Assert.That(Directory.EnumerateFiles(directory, "*.dll"), Is.Empty);
            await AssertDx11ResizeApplicationRunsAsync(executable, directory, driverLabel).ConfigureAwait(true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static async Task AssertDx11ResizeApplicationRunsAsync(
        string executable, string workingDirectory, string driverLabel)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        }) ?? throw new InvalidOperationException($"The {driverLabel} resize application did not start.");
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            var window = IntPtr.Zero;
            while (DateTime.UtcNow < deadline && !process.HasExited)
            {
                process.Refresh();
                window = process.MainWindowHandle;
                if (window != IntPtr.Zero
                    && process.MainWindowTitle == "Terrum DX11 Triangle"
                    && IsWindowVisible(window))
                {
                    break;
                }
                await Task.Delay(50).ConfigureAwait(true);
            }

            Assert.That(process.HasExited, Is.False, $"{driverLabel} resize application exited before showing its window.");
            Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
            Assert.That(IsWindowVisible(window), Is.True);
            Assert.That(GetClientRect(window, out var initialClient), Is.True, $"Could not query the initial client rectangle (Win32 {Marshal.GetLastPInvokeError()}).");
            var initialWidth = initialClient.Right - initialClient.Left;
            var initialHeight = initialClient.Bottom - initialClient.Top;
            Assert.That(initialWidth > 0 && initialHeight > 0, Is.True);

            await Task.Delay(500).ConfigureAwait(true);
            process.Refresh();
            Assert.That(process.HasExited, Is.False, $"{driverLabel} resize application exited before the client-size change.");
            Assert.That(SetWindowPos(window, IntPtr.Zero, 0, 0, 1024, 768, ResizeWindowFlags), Is.True, $"Could not resize the {driverLabel} window (Win32 {Marshal.GetLastPInvokeError()}).");
            Assert.That(GetClientRect(window, out var resizedClient), Is.True, $"Could not query the resized client rectangle (Win32 {Marshal.GetLastPInvokeError()}).");
            Assert.That(resizedClient.Right - resizedClient.Left > initialWidth, Is.True);
            Assert.That(resizedClient.Bottom - resizedClient.Top > initialHeight, Is.True);

            await Task.Delay(500).ConfigureAwait(true);
            process.Refresh();
            Assert.That(process.HasExited, Is.False, $"{driverLabel} resize application exited after the client-size change.");
            Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True, $"The {driverLabel} resize application did not accept WM_CLOSE.");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.That(process.ExitCode, Is.EqualTo(0));
            Assert.That(HardwareIsWindow(window), Is.False);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
        }
    }
}
