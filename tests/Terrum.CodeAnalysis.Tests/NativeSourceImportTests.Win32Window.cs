// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string CreateWindowExImportKey = "windows-x64.system32.user32.CreateWindowExW";
    private const string DestroyWindowImportKey = "windows-x64.system32.user32.DestroyWindow";
    private const string GetModuleHandleImportKey = "windows-x64.system32.kernel32.GetModuleHandleW";

    private const string DirectWindowSource = $$"""
        [Terrum.Interop.NativeImport("{{GetModuleHandleImportKey}}")]
        [NoThrow]
        extern Terrum.Interop.Windows.HINSTANCE GetModuleHandleW(
            Terrum.Interop.Windows.NativeNullPointer moduleName);

        [Terrum.Interop.NativeImport("{{CreateWindowExImportKey}}")]
        [NoThrow]
        extern Terrum.Interop.Windows.HWND CreateWindowExW(
            uint32 exStyle,
            ptr<char16> className,
            ptr<char16> windowName,
            uint32 style,
            int32 x,
            int32 y,
            int32 width,
            int32 height,
            Terrum.Interop.Windows.HWND parent,
            Terrum.Interop.Windows.HMENU menu,
            Terrum.Interop.Windows.HINSTANCE instance,
            Terrum.Interop.Windows.LPVOID parameter);

        [Terrum.Interop.NativeImport("{{DestroyWindowImportKey}}")]
        [NoThrow]
        extern int32 DestroyWindow(Terrum.Interop.Windows.HWND window);

        int32 Main() {
            return DestroyWindow(CreateWindowExW(
                0, utf16z("STATIC"), utf16z("Independent title"), 0,
                0, 0, 1, 1,
                Terrum.Interop.Windows.HWND.NoOwner, null, GetModuleHandleW(null), null)) == 0 ? 1 : 0;
        }
        """;

    [Test]
    public async Task DirectCreateWindowExAndDestroyWindowRunWithoutANativeShim()
    {
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowSource)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(program.NativeImportCatalogId, Is.EqualTo(NativeImportCatalog.WindowsX64.Identity));
        var __nunitAssertValue1 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeImportKey == CreateWindowExImportKey));
        Assert.That(__nunitAssertValue1, Has.Exactly(1).Items);
        var createWindow = __nunitAssertValue1[0];
        Assert.That(createWindow.ReturnType, Is.EqualTo(PrimitiveType.NativeWindowHandle));
        Assert.That(createWindow.ParameterCount, Is.EqualTo(12));
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeImportKey == DestroyWindowImportKey));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var destroyWindow = __nunitAssertValue2[0];
        Assert.That(destroyWindow.ReturnType, Is.EqualTo(PrimitiveType.Int32));

        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("user32.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("CreateWindowExW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("DestroyWindow\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("native-import-abi-probe.dll\0"u8) < 0, Is.True);

        var directory = Directory.CreateTempSubdirectory("terrum-direct-window-").FullName;
        var executablePath = Path.Combine(directory, "direct-window.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Direct Win32 import executable did not start.");
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
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void WindowCreationImportsCannotBeRedefinedByTestCatalogs()
    {
        var catalog = NativeImportCatalog.WindowsX64;
        Assert.That(catalog.TryGetImport(CreateWindowExImportKey, out var definition), Is.True);
        var __nunitAssertValue3 = definition;
        Assert.That(__nunitAssertValue3, Is.TypeOf<NativeImportDefinition>());
        var altered = ((NativeImportDefinition)__nunitAssertValue3) with {
            ExportName = "CreateWindowExW2",
        };

        _ = Assert.Throws<ArgumentException>(() =>
            NativeImportCatalog.CreateForTesting("forged-window-creation-v1", [altered]));
    }

    [Test]
    public void WindowCreationRejectsNonNullRestrictedPointers()
    {
        var source = DirectWindowSource.Replace(
            "Terrum.Interop.Windows.HWND.NoOwner, null, GetModuleHandleW(null), null",
            "Terrum.Interop.Windows.HWND.NoOwner, 1, GetModuleHandleW(null), null",
            StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void CreatedWindowHandleCannotBeStoredOrDiscarded()
    {
        var stored = WithMain("""
            int32 Main() {
                Terrum.Interop.Windows.HWND window = CreateWindowExW(
                    0, utf16z("STATIC"), utf16z(""), 0,
                    0, 0, 1, 1,
                    Terrum.Interop.Windows.HWND.NoOwner, null, GetModuleHandleW(null), null);
                return DestroyWindow(window) == 0 ? 1 : 0;
            }
            """);
        var discarded = WithMain("""
            int32 Main() {
                CreateWindowExW(
                    0, utf16z("STATIC"), utf16z(""), 0,
                    0, 0, 1, 1,
                    Terrum.Interop.Windows.HWND.NoOwner, null, GetModuleHandleW(null), null);
                return 0;
            }
            """);

        var storedCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(stored)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        var discardedCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(discarded)],
            runtimePolicy: BoundRuntimePolicy.FailFast);

        Assert.That(storedCompilation.HasErrors, Is.True);
        Assert.That(discardedCompilation.HasErrors, Is.True);
    }

    [Test]
    public async Task Win32WindowImportSignatureMatchesTheSelectedWindowsSdk()
    {
        var repositoryRoot = FindRepositoryRoot();
        var native = Path.Combine(repositoryRoot, "tests", "native");
        var environment = await FindNativeFixtureEnvironmentAsync(repositoryRoot).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("terrum-win32-sdk-").FullName;
        try
        {
            var objectPath = Path.Combine(directory, "window-sdk-probe.obj");
            var sourcePath = Path.Combine(native, "win32-window-sdk-probe.c");
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") {
                WorkingDirectory = repositoryRoot,
                Arguments = $"/d /s /c \"call \"{environment}\" >nul && cl /nologo /W4 /WX /Od /TC /c /Fo\"{objectPath}\" \"{sourcePath}\"\"",
            };
            _ = await RunAsync(start, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            Assert.That(File.Exists(objectPath), Is.True);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string WithMain(string main)
    {
        var mainStart = DirectWindowSource.IndexOf("int32 Main()", StringComparison.Ordinal);
        Assert.That(mainStart >= 0, Is.True);
        return DirectWindowSource[..mainStart] + main;
    }
}
