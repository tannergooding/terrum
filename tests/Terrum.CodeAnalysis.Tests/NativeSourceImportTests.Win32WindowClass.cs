// Copyright (c) Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

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
    private const string RegisterClassExImportKey = "windows-x64.system32.user32.RegisterClassExW";
    private const string UnregisterClassImportKey = "windows-x64.system32.user32.UnregisterClassW";

    private static string DirectWindowClassSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-stage.terrum"));

    [Test]
    public async Task DirectWindowClassRegistrationAndLifetimeRunWithoutANativeShim()
    {
        Assert.That(NativeWindowCreationContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        Assert.That(NativeWindowClassContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowClassSource)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(program.NativeImportCatalogId, Is.EqualTo("windows-x64-native-imports-v9"));
        Assert.That(ValueLayout.Size(PrimitiveType.NativeWindowClassEx, program.Structures), Is.EqualTo(80));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeWindowClassEx, program.Structures), Is.EqualTo(8));
        Assert.That(ValueLayout.Size(PrimitiveType.NativeWindowClassExAddress, program.Structures), Is.EqualTo(8));
        var main = program.Functions[program.EntryPoint];
        var __nunitAssertValue1 = main.Body.Statements[0];
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundLocal>());
        var descriptor = ((BoundLocal)__nunitAssertValue1);
        Assert.That(descriptor.Initializer, Is.TypeOf<BoundNativeWindowClassExInitialization>());
        var __nunitAssertValue2 = global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Where(program.Functions, static function => function.NativeImportKey == RegisterClassExImportKey));
        Assert.That(__nunitAssertValue2, Has.Exactly(1).Items);
        var register = __nunitAssertValue2[0];
        Assert.That(register.ReturnType, Is.EqualTo(PrimitiveType.Char16));
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.FailFast);
        Assert.That(image.AsSpan().IndexOf("GetModuleHandleW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("RegisterClassExW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("UnregisterClassW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("native-window-lifetime-probe.dll\0"u8) < 0, Is.True);

        var directory = TestArtifactPaths.CreateDirectory("terrum-direct-window-class-");
        var executablePath = Path.Combine(directory, "direct-window-class.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Direct Win32 class executable did not start.");
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
    public async Task WindowClassDescriptorAndImportsMatchTheSelectedWindowsSdk()
    {
        var repositoryRoot = FindRepositoryRoot();
        var native = Path.Combine(repositoryRoot, "tests", "native");
        var environment = await FindNativeFixtureEnvironmentAsync(repositoryRoot).ConfigureAwait(false);
        var directory = TestArtifactPaths.CreateDirectory("terrum-window-class-sdk-");
        try
        {
            var objectPath = Path.Combine(directory, "window-class-sdk-probe.obj");
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

    [TestCase("Terrum.Interop.Windows.WNDCLASSEXW windowClass =",
        "mut Terrum.Interop.Windows.WNDCLASSEXW windowClass =")]
    [TestCase("RegisterClassExW(&windowClass);",
        "Terrum.Interop.Windows.WNDCLASSEXW copy = windowClass; RegisterClassExW(&copy);")]
    [TestCase("RegisterClassExW(&windowClass);", "RegisterClassExW(null);")]
    [TestCase("utf16z(\"TerrumDirectClass\"), GetModuleHandleW(null)",
        "utf16z(\"OtherClass\"), GetModuleHandleW(null)")]
    [TestCase("&Host.WindowProcedure", "&Host.NotAWindowProcedure")]
    public void WindowClassRegistrationRejectsDescriptorCallbackAndNameEscapes(string before, string after)
    {
        Assert.That(DirectWindowClassSource.Contains(before, StringComparison.Ordinal), Is.True);
        var source = DirectWindowClassSource.Replace(before, after, StringComparison.Ordinal);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }

    [TestCase("Terrum.Interop.Windows.HINSTANCE saved = GetModuleHandleW(null); return 0;")]
    [TestCase("return GetModuleHandleW(null);")]
    [TestCase("return UnregisterClassW(utf16z(\"OtherClass\"), GetModuleHandleW(null));")]
    [TestCase("""
        Terrum.Interop.Windows.WNDCLASSEXW windowClass = windowclassex(
            &Host.WindowProcedure, GetModuleHandleW(null), utf16z("TerrumDirectClass"));
        RegisterClassExW(&windowClass);
        int32 destroyed = DestroyWindow(CreateWindowExW(
            0, utf16z("TerrumDirectClass"), utf16z(""), 0,
            0, 0, 1, 1,
            Terrum.Interop.Windows.HWND.NoOwner, null, GetModuleHandleW(null), null));
        return destroyed == 0 ? 1 : 0;
        """)]
    public void WindowClassRegistrationRequiresDirectModuleAndCompleteOrderedLifetime(string main)
    {
        var start = DirectWindowClassSource.IndexOf("int32 Main()", StringComparison.Ordinal);
        Assert.That(start >= 0, Is.True);
        var source = DirectWindowClassSource[..start] + "int32 Main() {\n" + main + "\n}";
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.FailFast);

        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics, Is.Not.Empty);
    }
}
