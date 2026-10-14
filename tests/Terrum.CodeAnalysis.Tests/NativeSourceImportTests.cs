// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string ImportKey = "windows-x64.system32.kernel32.GetCurrentThreadId";
    private const string SleepImportKey = "windows-x64.system32.kernel32.Sleep";
    private const string SignedFixtureKey = "test.application.native-import-abi.NativeFfiSigned";
    private const string UnsignedFixtureKey = "test.application.native-import-abi.NativeFfiUnsigned";
    private const string Utf16ZFixtureKey = "test.application.native-import-abi.NativeFfiUtf16Z";
    private const string MessageBoxFixtureKey = "test.application.native-import-abi.NativeFfiMessageBox";

    [Test]
    public void SourceImportRequiresItsExactCatalogSignatureAndCompilerAttribute()
    {
        foreach (var source in new[] {
            $"[Terrum.Interop.NativeImport(\"{ImportKey}\")] [NoThrow] extern int32 CurrentThreadId(); void Main() {{}}",
            "[Terrum.Interop.NativeImport(\"unknown\")] [NoThrow] extern uint32 CurrentThreadId(); void Main() {}",
            $"[Terrum.Interop.NativeImport(\"{ImportKey}\")] [NoThrow] extern uint32 CurrentThreadId(uint32 value); void Main() {{}}",
            $"[NativeImport(\"{ImportKey}\")] [NoThrow] extern uint32 CurrentThreadId(); void Main() {{}}",
        })
        {
            var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source));
            Assert.That(compilation.Program, Is.Null);
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3030"
                || diagnostic.Code == "TRM3001"),Is.True);
        }
    }

    [Test]
    public void SourceImportSleepMatchesExactFixedWidthSystemSignature()
    {
        var source = $$"""
            [Terrum.Interop.NativeImport("{{SleepImportKey}}")]
            [NoThrow]
            extern void Sleep(uint32 milliseconds);

            int32 Main() {
                Sleep(16);
                return 0;
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));

        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var sleep = global::System.Linq.Enumerable.Single(program.Functions, static function => function.IsExternal);
        Assert.That(sleep.NativeImportKey, Is.EqualTo(SleepImportKey));
        Assert.That(sleep.ReturnType, Is.EqualTo(PrimitiveType.Void));
        Assert.That(global::System.Linq.Enumerable.Single(sleep.Variables).Type, Is.EqualTo(PrimitiveType.UInt32));
        Assert.That(sleep.IsNoThrow, Is.True);
        Assert.That(sleep.RequiresUnsafeCaller, Is.False);
        Assert.That(sleep.DoesNotReturn, Is.False);

        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("KERNEL32.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("Sleep\0"u8) >= 0, Is.True);

        foreach (var declaration in new[] {
            $"[NoThrow] extern void Sleep(int32 milliseconds);",
            $"[NoThrow] extern uint32 Sleep(uint32 milliseconds);",
            "extern void Sleep(uint32 milliseconds);",
        })
        {
            var invalidSource = $$"""
                [Terrum.Interop.NativeImport("{{SleepImportKey}}")]
                {{declaration}}
                int32 Main() { Sleep(16); return 0; }
                """;
            var invalidCompilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(invalidSource));
            Assert.That(invalidCompilation.Program, Is.Null);
            Assert.That(global::System.Linq.Enumerable.Any(invalidCompilation.Diagnostics,static diagnostic =>
                diagnostic.Code == "TRM3030" || diagnostic.Code == "TRM3001"),Is.True);
        }
    }

    [Test]
    public void SourceCannotShadowCompilerOwnedImportAttribute()
    {
        var source = $$"""
            namespace Terrum.Interop;
            struct NativeImport { int32 Value; }
            [NoThrow]
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            extern uint32 CurrentThreadId();
            void Main() {}
            """;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3003"),Is.True);
    }

    [Test]
    public void Utf16ZRequiresAValidLiteralAndCataloguedNoEscapeArgument()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-native-utf16z-v1",
            [CreateUtf16ZImportDefinition()]);
        var sources = new[] {
            $$"""
                [Terrum.Interop.NativeImport("{{Utf16ZFixtureKey}}")]
                [NoThrow]
                extern int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                    ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);
                int32 Main() {
                    return NativeFfiUtf16Z(utf16z("bad\0text"), 1, 2,
                        utf16z("second"), 3, utf16z("third"));
                }
                """,
            $$"""
                [Terrum.Interop.NativeImport("{{Utf16ZFixtureKey}}")]
                [NoThrow]
                extern int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                    ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);
                int32 Main() {
                    return NativeFfiUtf16Z(utf16z("first" + "suffix"), 1, 2,
                        utf16z("second"), 3, utf16z("third"));
                }
                """,
            $$"""
                [Terrum.Interop.NativeImport("{{Utf16ZFixtureKey}}")]
                [NoThrow]
                extern int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                    ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);
                int32 Main() {
                    return NativeFfiUtf16Z("first", 1, 2,
                        utf16z("second"), 3, utf16z("third"));
                }
                """,
            $$"""
                [Terrum.Interop.NativeImport("{{Utf16ZFixtureKey}}")]
                [NoThrow]
                extern int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                    ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);
                int32 Main() {
                    ptr<char16> value = utf16z("first");
                    return 0;
                }
                """,
        };

        foreach (var source in sources)
        {
            var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
                BoundRuntimePolicy.FailFast);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        var withoutNoEscape = new NativeImportDefinition(Utf16ZFixtureKey, "native-import-abi-probe.dll",
            "NativeFfiUtf16Z", [PrimitiveType.NativeUtf16ZPointer], PrimitiveType.Int32,
            true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-native-utf16z-without-contract-v1", [withoutNoEscape]))!;
    }

    [Test]
    public void Utf16ZLiteralPoolValidatesEncodingTerminationAndSize()
    {
        var maximum = new string('x', (NativeUtf16ZContract.MaximumStorageBytes / sizeof(char)) - 1);
        Assert.That(NativeUtf16ZContract.IsValidLiteral(maximum), Is.True);
        Assert.That(NativeUtf16ZContract.IsValidLiteral(maximum + "x"), Is.False);
        Assert.That(NativeUtf16ZContract.IsValidLiteral("before\0after"), Is.False);
        Assert.That(NativeUtf16ZContract.IsValidLiteral("supplementary \U0001F642"), Is.True);
        Assert.That(NativeUtf16ZContract.IsValidLiteral(new string('\uD83D', 1)), Is.False);
        Assert.That(NativeUtf16ZContract.IsValidPool([maximum, "extra"]), Is.False);
    }

    [Test]
    public void NativeHwndNoOwnerIsRestrictedToItsCataloguedArgument()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-native-hwnd-v1",
            [CreateMessageBoxImportDefinition()]);
        var external = $$"""
            [Terrum.Interop.NativeImport("{{MessageBoxFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiMessageBox(Terrum.Interop.Windows.HWND owner, ptr<char16> text,
                ptr<char16> caption, uint32 flags);
            """;
        var invalidBodies = new[] {
            $$"""
                int32 Main() {
                    return NativeFfiMessageBox(owner: 0, text: utf16z("text"),
                        caption: utf16z("caption"), flags: 0);
                }
                """,
            $$"""
                int32 Main() {
                    return NativeFfiMessageBox(owner: null, text: utf16z("text"),
                        caption: utf16z("caption"), flags: 0);
                }
                """,
            $$"""
                int32 Main() {
                    Terrum.Interop.Windows.HWND owner = Terrum.Interop.Windows.HWND.NoOwner;
                    return NativeFfiMessageBox(owner: owner, text: utf16z("text"),
                        caption: utf16z("caption"), flags: 0);
                }
                """,
            $$"""
                int32 Forward(Terrum.Interop.Windows.HWND owner) {
                    return NativeFfiMessageBox(owner: owner, text: utf16z("text"),
                        caption: utf16z("caption"), flags: 0);
                }
                int32 Main() {
                    return Forward(Terrum.Interop.Windows.HWND.NoOwner);
                }
                """,
            $$"""
                int32 Main() {
                    return NativeFfiMessageBox(
                        owner: true ? Terrum.Interop.Windows.HWND.NoOwner
                            : Terrum.Interop.Windows.HWND.NoOwner,
                        text: utf16z("text"), caption: utf16z("caption"), flags: 0);
                }
                """,
        };

        foreach (var body in invalidBodies)
        {
            var compilation = Compilation.CreateWithNativeImports(
                [SyntaxTree.Parse(external + Environment.NewLine + body)], catalog,
                BoundRuntimePolicy.FailFast);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        var spoofed = $$"""
            namespace Terrum.Interop.Windows;
            struct HWND { int32 Value; }
            [Terrum.Interop.NativeImport("{{MessageBoxFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiMessageBox(HWND owner, ptr<char16> text,
                ptr<char16> caption, uint32 flags);
            int32 Main() {
                return NativeFfiMessageBox(owner: HWND.NoOwner, text: utf16z("text"),
                    caption: utf16z("caption"), flags: 0);
            }
            """;
        var spoofedCompilation = Compilation.CreateWithNativeImports(
            [SyntaxTree.Parse(spoofed)], catalog, BoundRuntimePolicy.FailFast);
        Assert.That(spoofedCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(spoofedCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3003"),Is.True);

        var validSource = $$"""
            [Terrum.Interop.NativeImport("{{MessageBoxFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiMessageBox(Terrum.Interop.Windows.HWND owner, ptr<char16> text,
                ptr<char16> caption, uint32 flags);
            int32 Main() {
                return NativeFfiMessageBox(flags: 0, caption: utf16z("caption"),
                    text: utf16z("text"), owner: Terrum.Interop.Windows.HWND.NoOwner);
            }
            """;
        var validCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(validSource)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(validCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, validCompilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = validCompilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var mainIndex = Array.FindIndex(program.Functions.ToArray(), static function => function.Name == "Main");
        var main = program.Functions[mainIndex];
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(
            program, NativeRuntimePolicy.FailFast))!;
        var boundReturn = ((global::System.Func<BoundReturn>)(() => { var __nunitActual2 = main.Body.Statements[0]; Assert.That(__nunitActual2, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual2!; }))();
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual3 = boundReturn.Value; Assert.That(__nunitActual3, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual3!; }))();
        var ownerArgumentIndex = Enumerable.Range(0, call.Arguments.Length)
            .Single(index => call.ParameterIndex(index) == 0);
        Assert.That(call.Arguments[ownerArgumentIndex], Is.TypeOf<BoundNativeNoOwner>());

        var forgedCall = call with {
            Arguments = call.Arguments.SetItem(ownerArgumentIndex,
                new BoundConstant(0, PrimitiveType.NativeWindowHandle,
                    call.Arguments[ownerArgumentIndex].Span)),
        };
        var forgedMain = main with {
            Body = main.Body with {
                Statements = main.Body.Statements.SetItem(0, boundReturn with { Value = forgedCall }),
            },
        };
        var forgedProgram = program with {
            Functions = program.Functions.SetItem(mainIndex, forgedMain),
        };
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
            forgedProgram, NativeRuntimePolicy.FailFast))!;

        var externalIndex = Array.FindIndex(program.Functions.ToArray(),
            static function => function.IsExternal);
        var strippedProgram = program with {
            Functions = program.Functions.SetItem(externalIndex,
                program.Functions[externalIndex] with { NativeImportKey = null }),
            NativeImportCatalog = null,
            NativeImportCatalogId = null,
            NativeUtf16ZLiterals = [],
        };
        _ = Assert.Throws<NotSupportedException>(() =>
            SemanticVerifier.RejectSourceNativeImports(strippedProgram, "JIT"))!;
        _ = Assert.Throws<ArgumentException>(() =>
            NativeEmitter.EmitCode(strippedProgram, NativeRuntimePolicy.FailFast))!;

        var withoutNoOwner = new NativeImportDefinition(MessageBoxFixtureKey,
            "native-import-abi-probe.dll", "NativeFfiMessageBox",
            [PrimitiveType.NativeWindowHandle], PrimitiveType.Int32,
            true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-native-hwnd-without-contract-v1", [withoutNoOwner]))!;
    }

    [Test]
    public void Utf16ZLiteralIndexIsRecheckedBeforeNativeEmission()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-native-utf16z-v1",
            [CreateUtf16ZImportDefinition()]);
        var source = $$"""
            [Terrum.Interop.NativeImport("{{Utf16ZFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);
            int32 Main() {
                return NativeFfiUtf16Z(utf16z("first"), 1, 2,
                    utf16z("second"), 3, utf16z("third"));
            }
            """;
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
            program with { NativeUtf16ZLiterals = [] }, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void BodylessCallableRequiresExtern()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("uint32 CurrentThreadId(); void Main() {}"));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3030"),Is.True);
    }

    [Test]
    public async Task SourceImportCallsReviewedSystemFunctionAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var source = $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 CurrentThreadId();

            [Terrum.Interop.NativeImport("{{SleepImportKey}}")]
            [NoThrow]
            extern void Sleep(uint32 milliseconds);

            int32 Main() {
                if (CurrentThreadId() == 0) {
                    return 1;
                }
                Sleep(1);
                return 0;
            }

            int32 Helper() { return 7; }
            """;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        Assert.That(program.NativeImportCatalogId, Is.EqualTo(NativeImportCatalog.WindowsX64.Identity));
        Assert.That(program.Functions.Count(static function => function.IsExternal), Is.EqualTo(2));

        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitWithImports(program,
            [new NativeImportBinding("CurrentThreadId", "kernel32.dll", "GetCurrentThreadId")],
            NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program,
            NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => ModuleCompiler.CreateProducer(
            program with { EntryPoint = -1 }, ["Helper"]))!;
        _ = Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            program with { EntryPoint = -1 }, "native-import-test"))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitLibraryWithImports(
            program with { EntryPoint = -1 }, ["Helper"],
            [new NativeImportBinding("CurrentThreadId", "unreviewed.dll", "UnreviewedTarget")],
            NativeRuntimePolicy.FailFast))!;

        var directory = Directory.CreateTempSubdirectory("terrum-source-import-").FullName;
        var executablePath = Path.Combine(directory, "source-import.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Native executable did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(0));
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(executablePath).ConfigureAwait(true);
            Directory.Delete(directory);
        }
    }

    [Test]
    public async Task SourceImportsMatchIndependentCAbiAndDx11SdkLayoutsAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        Assert.That(File.Exists(vswhere), Is.True, "Visual Studio discovery is required to build the C ABI fixture.");
        var repositoryRoot = FindRepositoryRoot();
        var discover = new ProcessStartInfo(vswhere);
        discover.WorkingDirectory = repositoryRoot;
        foreach (var argument in new[] {
            "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property", "installationPath",
        })
        {
            discover.ArgumentList.Add(argument);
        }
        var installation = (await RunAsync(discover, TimeSpan.FromSeconds(10)).ConfigureAwait(false)).Trim();
        var environment = Path.Combine(installation, "VC", "Auxiliary", "Build", "vcvars64.bat");
        Assert.That(File.Exists(environment), Is.True, "Visual Studio x64 C++ build tools are required for the C ABI fixture.");

        var directory = Directory.CreateTempSubdirectory("terrum-native-import-abi-").FullName;
        var libraryPath = Path.Combine(directory, "native-import-abi-probe.dll");
        var objectPath = Path.Combine(directory, "native-import-abi-probe.obj");
        var executablePath = Path.Combine(directory, "native-import-abi-probe.exe");
        var sourcePath = Path.Combine(repositoryRoot, "tests", "native", "native-import-abi-probe.c");
        var layoutProbeObjectPath = Path.Combine(directory, "dx11-sdk-layout-probe.obj");
        var layoutProbeSourcePath = Path.Combine(repositoryRoot, "tests", "native",
            "dx11-sdk-layout-probe.c");
        try
        {
            var build = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /LD /TC /Fo\"{objectPath}\" /Fe\"{libraryPath}\" \"{sourcePath}\" /link /incremental:no\"",
                WorkingDirectory = repositoryRoot,
            };
            _ = await RunAsync(build, TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            var layoutProbeBuild = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /wd4201 /TC /c /Fo\"{layoutProbeObjectPath}\" \"{layoutProbeSourcePath}\"\"",
                WorkingDirectory = repositoryRoot,
            };
            _ = await RunAsync(layoutProbeBuild, TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            var catalog = NativeImportCatalog.CreateForTesting("test-native-import-abi-v1", [
                new NativeImportDefinition(SignedFixtureKey, "native-import-abi-probe.dll", "NativeFfiSigned",
                    [PrimitiveType.Int32, PrimitiveType.UInt32, PrimitiveType.Int64, PrimitiveType.UInt64,
                        PrimitiveType.Int32, PrimitiveType.UInt32],
                    PrimitiveType.Int64, true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory),
                new NativeImportDefinition(UnsignedFixtureKey, "native-import-abi-probe.dll", "NativeFfiUnsigned",
                    [PrimitiveType.UInt64, PrimitiveType.UInt32, PrimitiveType.Int64, PrimitiveType.UInt64,
                        PrimitiveType.UInt32],
                    PrimitiveType.UInt64, true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory),
                CreateUtf16ZImportDefinition(),
                CreateMessageBoxImportDefinition(),
            ]);
            var source = $$"""
                [Terrum.Interop.NativeImport("{{SignedFixtureKey}}")]
                [NoThrow]
                extern int64 NativeFfiSigned(int32 first, uint32 second, int64 third, uint64 fourth,
                    int32 fifth, uint32 sixth);

                [Terrum.Interop.NativeImport("{{UnsignedFixtureKey}}")]
                [NoThrow]
                extern uint64 NativeFfiUnsigned(uint64 first, uint32 second, int64 third, uint64 fourth,
                    uint32 fifth);

                [Terrum.Interop.NativeImport("{{Utf16ZFixtureKey}}")]
                [NoThrow]
                extern int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                    ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);

                [Terrum.Interop.NativeImport("{{MessageBoxFixtureKey}}")]
                [NoThrow]
                extern int32 NativeFfiMessageBox(Terrum.Interop.Windows.HWND owner, ptr<char16> text,
                    ptr<char16> caption, uint32 flags);

                int32 Main() {
                    if (NativeFfiSigned(-7, 11, -13, 17, -19, 23) != -1234567890123) {
                        return 1;
                    }
                    if (NativeFfiUnsigned(2, 3, -5, 7, 11) != 18364758544493064720) {
                        return 2;
                    }
                    if (NativeFfiUtf16Z(sixth: utf16z(""), fifth: 610800471,
                        fourth: utf16z("caption"), third: 81985529216486895,
                        second: 324478056, first: utf16z("A\n\U0001F642")) != 0) {
                        return 3;
                    }
                    if (NativeFfiMessageBox(flags: 0, caption: utf16z("caption"),
                        text: utf16z("A\n\U0001F642"), owner: Terrum.Interop.Windows.HWND.NoOwner) != 1) {
                        return 4;
                    }
                    return 0;
                }
                """;
            var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
                BoundRuntimePolicy.FailFast);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
            var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);

            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Native ABI fixture executable did not start.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                throw;
            }
            Assert.That(process.ExitCode, Is.EqualTo(0));

            var fakeUser32BuildDirectory = Path.Combine(directory, "fake-user32");
            Directory.CreateDirectory(fakeUser32BuildDirectory);
            var fakeUser32Path = Path.Combine(fakeUser32BuildDirectory, "user32.dll");
            var currentDirectory = Path.Combine(directory, "current");
            var pathDirectory = Path.Combine(directory, "path");
            Directory.CreateDirectory(currentDirectory);
            Directory.CreateDirectory(pathDirectory);
            var fakeUser32Source = Path.Combine(repositoryRoot, "tests", "native",
                "native-import-loader-fake.c");
            var loaderProbeSource = Path.Combine(repositoryRoot, "tests", "native",
                "native-import-loader-probe.c");
            var buildFakeUser32 = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /LD /TC /Fe\"{fakeUser32Path}\" \"{fakeUser32Source}\" /link /incremental:no\"",
                WorkingDirectory = fakeUser32BuildDirectory,
            };
            _ = await RunAsync(buildFakeUser32, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
            File.Copy(fakeUser32Path, Path.Combine(directory, "user32.dll"));
            File.Copy(fakeUser32Path, Path.Combine(currentDirectory, "user32.dll"));
            File.Copy(fakeUser32Path, Path.Combine(pathDirectory, "user32.dll"));

            var loaderProbePath = Path.Combine(directory, "native-import-loader-probe.exe");
            var buildLoaderProbe = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec")
                ?? throw new InvalidOperationException("ComSpec is unset.")) {
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /O2 /MT /TC /Fe\"{loaderProbePath}\" \"{loaderProbeSource}\" /link /incremental:no\"",
                WorkingDirectory = directory,
            };
            _ = await RunAsync(buildLoaderProbe, TimeSpan.FromMinutes(2)).ConfigureAwait(false);

            var loaderProbe = new ProcessStartInfo(loaderProbePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = currentDirectory,
            };
            loaderProbe.Environment["PATH"] = pathDirectory + ";"
                + Environment.GetEnvironmentVariable("PATH");
            using var loaderProcess = Process.Start(loaderProbe)
                ?? throw new InvalidOperationException("Native DLL loader probe did not start.");
            try
            {
                await loaderProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (!loaderProcess.HasExited)
                {
                    loaderProcess.Kill(entireProcessTree: true);
                    await loaderProcess.WaitForExitAsync().ConfigureAwait(false);
                }
                throw;
            }
            Assert.That(loaderProcess.ExitCode, Is.EqualTo(0));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tests", "native", "native-import-abi-probe.c")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the native-import ABI fixture.");
    }

    private static NativeImportDefinition CreateUtf16ZImportDefinition() =>
        new(Utf16ZFixtureKey, "native-import-abi-probe.dll", "NativeFfiUtf16Z",
            [PrimitiveType.NativeUtf16ZPointer, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.NativeUtf16ZPointer, PrimitiveType.UInt32, PrimitiveType.NativeUtf16ZPointer],
            PrimitiveType.Int32, true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
            ],
        };

    private static NativeImportDefinition CreateMessageBoxImportDefinition() =>
        new(MessageBoxFixtureKey, "native-import-abi-probe.dll", "NativeFfiMessageBox",
            [PrimitiveType.NativeWindowHandle, PrimitiveType.NativeUtf16ZPointer,
                PrimitiveType.NativeUtf16ZPointer, PrimitiveType.UInt32],
            PrimitiveType.Int32, true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.NoOwnerWindowHandle,
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.Value,
            ],
        };

    private static async Task<string> RunAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start native ABI fixture command.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }

        var stdout = await output.ConfigureAwait(false);
        var stderr = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Native ABI fixture command failed ({process.ExitCode}):\n{stdout}\n{stderr}");
        }
        return stdout;
    }
}
