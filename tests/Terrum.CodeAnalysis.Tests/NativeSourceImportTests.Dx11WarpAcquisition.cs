// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
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
    private static string DirectWarpAcquisitionSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "device-swapchain-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileDirectWarpAcquisition()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWarpAcquisitionSource)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        return ((BoundProgram)__nunitAssertValue0);
    }

    private static NativeCode DirectWarpAcquisitionCode(BoundProgram program)
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
    public void DirectWarpAcquisitionCatalogsAndEmitsOwnerSpecificReleases()
    {
        var program = CompileDirectWarpAcquisition();
        var expected = new[] {
            (NativeDx11WarpAcquisitionContract.DeviceName + ".Close",
                NativeDx11WarpAcquisitionContract.ReleaseDevice),
            (NativeDx11WarpAcquisitionContract.SwapChainName + ".Close",
                NativeDx11WarpAcquisitionContract.ReleaseSwapChain),
            (NativeDx11WarpAcquisitionContract.ContextName + ".Close",
                NativeDx11WarpAcquisitionContract.ReleaseContext),
        };
        var code = DirectWarpAcquisitionCode(program);
        foreach (var (name, method) in expected)
        {
            var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, function => function.Name == name
                            && function.Intrinsic == BoundIntrinsic.NativeComOwnerClose));
            Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
            var close = __nunitAssertValue1[0];
            Assert.That(close.NativeComMethod, Is.SameAs(method));
            Assert.That(method.Effects, Is.EqualTo(NativeComEffects.ReleasesReference));
            Assert.That(method.Slot, Is.EqualTo(2));
            var functionIndex = program.Functions.IndexOf(close);
            var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, function =>
                            function.Start == code.SourceFunctionOffsets[functionIndex]));
            Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
            var range = __nunitAssertValue2[0];
            Assert.That(code.Code.AsSpan(range.Start, range.End - range.Start)
                .IndexOf(new byte[] { 0xFF, 0x50, 0x10 }) >= 0, Is.True);
        }
    }

    [TestCase("out Terrum.Interop.Dx11.ProvisionalSwapChainOwner swapChain,",
        "out Terrum.Interop.Dx11.ProvisionalDeviceOwner swapChain,")]
    [TestCase("result = 0;", "uint64 raw = (uint64)device; result = 0;")]
    [TestCase("result = 0;", "uint64 raw = &swapChain; result = 0;")]
    [TestCase("result = 0;", "Terrum.Interop.Dx11.ProvisionalContextOwner copy = context; result = 0;")]
    [TestCase("result = 0;", "Terrum.Interop.Dx11.ProvisionalDeviceOwner copy = device; result = 0;")]
    [TestCase("result = 0;", "DestroyWindow(window); result = 0;")]
    public void DirectWarpAcquisitionRejectsOwnerEscapesAndCatalogSignatureChanges(string original, string replacement)
    {
        var source = DirectWarpAcquisitionSource;
        Assert.That(source.Contains(original, StringComparison.Ordinal), Is.True);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(
            source.Replace(original, replacement, StringComparison.Ordinal))],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(compilation.Program, Is.Null);
    }

    [TestCase("struct")]
    [TestCase("class")]
    [TestCase("alias")]
    public void DirectWarpAcquisitionOwnerIdentitiesAreReservedOnlyByItsCatalog(string declaration)
    {
        foreach (var structure in NativeDx11WarpAcquisitionContract.Structures)
        {
            var name = structure.Name[(structure.Name.LastIndexOf('.') + 1)..];
            var ownerDeclaration = declaration switch {
                "alias" => "alias " + name + " = uint64;",
                "class" => "class " + name + " { uint64 Value; " + name
                    + "(uint64 value) { Value = value; } }",
                _ => "struct " + name + " { uint64 Value; }",
            };
            var tree = SyntaxTree.Parse("namespace Terrum.Interop.Dx11;\n" + ownerDeclaration
                + "\nint32 Main() { return 0; }");
            var ordinaryCompilation = Compilation.Create(tree);
            Assert.That(ordinaryCompilation.Program is not null, Is.True, string.Join(Environment.NewLine,
                ordinaryCompilation.Diagnostics));
            Assert.That(Compilation.CreateWithNativeImports([tree],
                runtimePolicy: BoundRuntimePolicy.FailFast).Program, Is.Null);
        }
    }

    [Test]
    public void DirectWarpAcquisitionRejectsForgedCatalogEntries()
    {
        var import = NativeDx11WarpAcquisitionContract.Import;
        Assert.That(import.ParameterTypes.Length, Is.EqualTo(12));
        Assert.That(import.ReturnType, Is.EqualTo(PrimitiveType.Int32));
        var imports = new[] {
            import with { ReturnType = PrimitiveType.UInt32 },
            import with { DllName = "native-application-helper.dll" },
            import with { DeploymentPolicy = NativeImportDeploymentPolicy.ApplicationDirectory },
            import with { ParameterTypes = import.ParameterTypes.SetItem(9, PrimitiveType.UInt64) },
            import with { ParameterContracts = import.ParameterContracts.SetItem(8, NativeImportParameterContract.Value) },
        };
        foreach (var forgedImport in imports)
        {
            Assert.That(NativeDx11WarpAcquisitionContract.MatchesImport(forgedImport), Is.False);
            Assert.Throws<ArgumentException>(() => new NativeImportCatalog(
                NativeWindowClassContract.CatalogIdentity, [forgedImport]));
        }
        Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            NativeWindowClassContract.CatalogIdentity, [import]));
        foreach (var declaration in new[] {
            "[NoThrow] extern int32 Raw(uint64 adapter, uint32 driver, uint64 software, uint32 flags, uint64 levels, uint32 count, uint32 sdk, uint64 descriptor, out uint64 swap, out uint64 device, out uint32 selected, out uint64 context);",
            "[NoThrow] extern uint32 Raw(uint64 adapter);",
        })
        {
            var rawSource = $"[Terrum.Interop.NativeImport(\"{NativeDx11WarpAcquisitionContract.ImportKey}\")]\n"
                + declaration + "\nint32 Main() { return 0; }";
            Assert.That(Compilation.CreateWithNativeImports(SyntaxTree.Parse(rawSource)).Program, Is.Null);
        }
    }

    [Test]
    public void WarpAcquisitionImportCanBeCalledWithGenericStorageAddresses()
    {
        var import = NativeDx11WarpAcquisitionContract.Import;
        var source = $$"""
            [Terrum.Interop.NativeImport("{{import.Key}}")]
            [NoThrow]
            extern int32 D3D11CreateDeviceAndSwapChain(
                Terrum.Interop.Windows.NativeNullPointer adapter,
                int32 driverType,
                Terrum.Interop.Windows.NativeNullPointer software,
                uint32 flags,
                Terrum.Interop.NativeStorageAddress featureLevels,
                uint32 featureLevelCount,
                uint32 sdkVersion,
                Terrum.Interop.NativeStorageAddress swapChainDescription,
                out {{NativeDx11WarpAcquisitionContract.SwapChainName}} swapChain,
                out {{NativeDx11WarpAcquisitionContract.DeviceName}} device,
                Terrum.Interop.NativeStorageAddress selectedFeatureLevel,
                out {{NativeDx11WarpAcquisitionContract.ContextName}} immediateContext);

            struct ModeDescription {
                uint32 Width;
                uint32 Height;
                uint32 RefreshNumerator;
                uint32 RefreshDenominator;
                uint32 Format;
                uint32 ScanlineOrdering;
                uint32 Scaling;

                public ModeDescription(uint32 width, uint32 height, uint32 refreshNumerator,
                    uint32 refreshDenominator, uint32 format, uint32 scanlineOrdering, uint32 scaling) {
                    Width = width;
                    Height = height;
                    RefreshNumerator = refreshNumerator;
                    RefreshDenominator = refreshDenominator;
                    Format = format;
                    ScanlineOrdering = scanlineOrdering;
                    Scaling = scaling;
                }
            }

            struct SampleDescription {
                uint32 Count;
                uint32 Quality;

                public SampleDescription(uint32 count, uint32 quality) {
                    Count = count;
                    Quality = quality;
                }
            }

            struct SwapChainDescription {
                ModeDescription BufferDescription;
                SampleDescription SampleDescription;
                uint32 BufferUsage;
                uint32 BufferCount;
                Terrum.Interop.Windows.HWND OutputWindow;
                int32 Windowed;
                uint32 SwapEffect;
                uint32 Flags;

                public SwapChainDescription(ModeDescription bufferDescription,
                    SampleDescription sampleDescription, uint32 bufferUsage, uint32 bufferCount,
                    Terrum.Interop.Windows.HWND outputWindow, int32 windowed, uint32 swapEffect,
                    uint32 flags) {
                    BufferDescription = bufferDescription;
                    SampleDescription = sampleDescription;
                    BufferUsage = bufferUsage;
                    BufferCount = bufferCount;
                    OutputWindow = outputWindow;
                    Windowed = windowed;
                    SwapEffect = swapEffect;
                    Flags = flags;
                }
            }

            int32 Main() {
                InlineArray<uint32, 1> featureLevels = [45056];
                mut InlineArray<uint32, 1> selectedFeatureLevel = [0];
                SwapChainDescription description = new SwapChainDescription(
                    new ModeDescription(0, 0, 0, 0, 28, 0, 0),
                    new SampleDescription(1, 0), 32, 2,
                    Terrum.Interop.Windows.HWND.NoOwner, 1, 0, 0);
                return D3D11CreateDeviceAndSwapChain(null, 5, null, 0,
                    &featureLevels[0], 1, 7, &description, out var swapChain, out var device,
                    &selectedFeatureLevel[0], out var immediateContext);
            }
            """;
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue3 = compilation.Program;
        Assert.That(__nunitAssertValue3, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue3);
        var catalog = NativeImportCatalog.WindowsX64;
        var __nunitAssertValue4 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, candidate =>
                    candidate.IsExternal && candidate.NativeImportKey == import.Key));
        Assert.That(__nunitAssertValue4, Has.Exactly(1).Items);
        var function = __nunitAssertValue4[0];

        Assert.That(catalog.MatchesFunction(function, import, program.Structures), Is.True);
        Assert.That(import.StorageParameters.Select(static parameter => parameter.ParameterIndex), Is.EqualTo(new[] { 4, 10 }));
        Assert.That(import.AggregateStorageParameters.Select(static parameter => parameter.ParameterIndex), Is.EqualTo(new[] { 7 }));
    }

    [Test]
    public async Task DirectWarpAcquisitionMatchesSdkAndCleansEveryInjectedOutput()
    {
        var root = FindRepositoryRoot();
        var environment = await FindNativeFixtureEnvironmentAsync(root).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-direct-warp-abi-").FullName;
        try
        {
            var fixture = Path.Combine(root, "tests", "native", "native-dx11-direct-acquisition-probe.c");
            var sdk = Path.Combine(root, "tests", "native", "dx11-direct-acquisition-sdk-probe.c");
            var dll = Path.Combine(directory, "native-dx11-direct-acquisition-probe.dll");
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = directory,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /Od /TC /c /Fosdk.obj \"{sdk}\" && cl /nologo /W4 /WX /wd4201 /Od /TC /LD /Fofixture.obj \"{fixture}\" /link /OUT:\"{dll}\" user32.lib\"",
            };
            build.Environment["PATH"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer") + ";" + build.Environment["PATH"];
            _ = await RunAsync(build, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            Assert.That(File.Exists(Path.Combine(directory, "sdk.obj")), Is.True);
            var program = CompileDirectWarpAcquisition();
            var code = DirectWarpAcquisitionCode(program);
            Assert.That(code.NativeImports, Has.Exactly(1).Matches(static import => import.ExportName == "D3D11CreateDeviceAndSwapChain"));
            var __nunitAssertValue5 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(code.Functions, function =>
                            function.Start == code.SourceFunctionOffsets[program.EntryPoint]));
            Assert.That(__nunitAssertValue5, Has.Exactly(1).Items);
            var acquisitionRange = __nunitAssertValue5[0];
            Assert.That(acquisitionRange.StackSize & 15, Is.EqualTo(8));
            // Only the test's emitted import fixup is substituted; neither the program nor catalog is weakened.
            var injected = code with {
                NativeImports = [.. code.NativeImports.Select(import =>
                import.ExportName == "D3D11CreateDeviceAndSwapChain" ? import with {
                    DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpAcquisition",
                } : import)]
            };
            var executable = Path.Combine(directory, "injected.exe");
            await File.WriteAllBytesAsync(executable, PeImage.Build(injected)).ConfigureAwait(false);
            for (var mode = 0; mode <= 20; mode++)
            {
                var mask = mode == 1 ? 0 : mode is >= 2 and <= 8 ? mode - 1
                    : mode is >= 10 and <= 16 ? mode - 10 : 7;
                var alias = mode is 17 or 18;
                var expectedTrace = "A" + (alias ? "RRR"
                    : ((mask & 4) != 0 ? "C" : "") + ((mask & 1) != 0 ? "D" : "")
                        + ((mask & 2) != 0 ? "S" : "")) + "Z";
                var expectedExit = mode is >= 10 and <= 16 ? NativeDx11WarpAcquisitionContract.NullSuccessError
                    : mode is >= 1 and <= 8 or 18 or 20 ? 1 : 0;
                for (var iteration = 0; iteration < 4; iteration++)
                {
                    var report = Path.Combine(directory, $"trace-{mode}-{iteration}.txt");
                    var start = new ProcessStartInfo(executable) { WorkingDirectory = directory };
                    start.Environment["TERRUM_DX11_MODE"] = mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    start.Environment["TERRUM_DX11_REPORT"] = report;
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("Injected WARP gate did not start.");
                    try
                    {
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                        var actualTrace = File.Exists(report)
                            ? await File.ReadAllTextAsync(report).ConfigureAwait(false) : "<missing>";
                        Assert.That(actualTrace, Is.EqualTo(expectedTrace));
                        Assert.That(process.ExitCode, Is.EqualTo(expectedExit));
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

            var interceptedExports = new[] {
                "RegisterClassExW", "CreateWindowExW", "DestroyWindow", "UnregisterClassW",
                "D3D11CreateDeviceAndSwapChain",
            };
            foreach (var export in interceptedExports)
            {
                Assert.That(code.NativeImports, Has.Some.Matches(import => import.ExportName == export));
            }
            var lifecycleInjected = code with {
                NativeImports = [.. code.NativeImports.Select(import => import.ExportName switch {
                    "RegisterClassExW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpRegisterClassExW",
                    },
                    "CreateWindowExW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpCreateWindowExW",
                    },
                    "DestroyWindow" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpDestroyWindow",
                    },
                    "UnregisterClassW" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpUnregisterClassW",
                    },
                    "D3D11CreateDeviceAndSwapChain" => import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpAcquisition",
                    },
                    _ => import,
                })]
            };
            var lifecycleExecutable = Path.Combine(directory, "lifecycle-failures.exe");
            await File.WriteAllBytesAsync(lifecycleExecutable, PeImage.Build(lifecycleInjected)).ConfigureAwait(false);
            foreach (var (mode, expectedExit, expectedTrace) in new[] {
                (21, 2, "rZ"),
                (22, 5, "RwUZ"),
            })
            {
                var report = Path.Combine(directory, $"lifecycle-trace-{mode}.txt");
                var start = new ProcessStartInfo(lifecycleExecutable) { WorkingDirectory = directory };
                start.Environment["TERRUM_DX11_MODE"] = mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                start.Environment["TERRUM_DX11_REPORT"] = report;
                using var process = Process.Start(start)
                    ?? throw new InvalidOperationException("Injected WARP lifecycle gate did not start.");
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

            var original = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
            var systemImport = Encoding.ASCII.GetBytes(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "d3d11.dll") + "\0");
            Assert.That(original.AsSpan().IndexOf(systemImport) >= 0, Is.True);
            File.Copy(dll, Path.Combine(directory, "d3d11.dll"));
            var real = Path.Combine(directory, "real.exe");
            await File.WriteAllBytesAsync(real, original).ConfigureAwait(false);
            await RunAsync(new ProcessStartInfo(real) { WorkingDirectory = directory },
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);

            // Keep invalid-SDK behavior deterministic while verifying the real DLL with the valid SDK above.
            var invalidSdkCode = (byte[])code.Code.Clone();
            var sdkValue = new byte[] { 0xB8, 7, 0, 0, 0 };
            var sdkOffset = invalidSdkCode.AsSpan().IndexOf(sdkValue);
            Assert.That(sdkOffset >= 0, Is.True);
            Assert.That(invalidSdkCode.AsSpan(sdkOffset + sdkValue.Length).IndexOf(sdkValue), Is.EqualTo(-1));
            invalidSdkCode[sdkOffset + 1] = 0;
            var invalidSdk = Path.Combine(directory, "invalid-sdk.exe");
            var invalidSdkInjected = code with {
                Code = invalidSdkCode,
                NativeImports = [.. code.NativeImports.Select(import =>
                    import.ExportName == "D3D11CreateDeviceAndSwapChain" ? import with {
                        DllName = Path.GetFileName(dll), ExportName = "NativeFfiDirectWarpAcquisition",
                    } : import)],
            };
            await File.WriteAllBytesAsync(invalidSdk,
                PeImage.Build(invalidSdkInjected)).ConfigureAwait(false);
            var invalidSdkReport = Path.Combine(directory, "invalid-sdk-trace.txt");
            var invalidSdkStart = new ProcessStartInfo(invalidSdk) { WorkingDirectory = directory };
            invalidSdkStart.Environment["TERRUM_DX11_MODE"] = "23";
            invalidSdkStart.Environment["TERRUM_DX11_REPORT"] = invalidSdkReport;
            using var invalidProcess = Process.Start(invalidSdkStart)
                ?? throw new InvalidOperationException("The invalid-SDK WARP gate did not start.");
            try
            {
                await invalidProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(invalidProcess.ExitCode, Is.EqualTo(1));
                Assert.That(await File.ReadAllTextAsync(invalidSdkReport).ConfigureAwait(false), Is.EqualTo("AZ"));
            }
            finally
            {
                if (!invalidProcess.HasExited)
                {
                    invalidProcess.Kill(entireProcessTree: true);
                    await invalidProcess.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DirectWarpAcquisitionCallsSystemD3D11WithoutAnApplicationHelper()
    {
        var program = CompileDirectWarpAcquisition();
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("D3D11CreateDeviceAndSwapChain\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("d3d11.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("NativeFfi"u8) < 0, Is.True);
        Assert.That(program.Structures.Count(static structure =>
            NativeDx11WarpAcquisitionContract.IsOwnerName(structure.Name)), Is.EqualTo(3));
        var directory = Directory.CreateTempSubdirectory("terrum-direct-warp-").FullName;
        try
        {
            var executable = Path.Combine(directory, "direct-warp.exe");
            await File.WriteAllBytesAsync(executable, image).ConfigureAwait(false);
            await RunAsync(new ProcessStartInfo(executable) { WorkingDirectory = directory },
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
