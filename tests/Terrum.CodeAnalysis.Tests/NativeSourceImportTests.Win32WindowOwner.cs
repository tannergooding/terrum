using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const uint WindowCloseMessage = 0x0010;

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static string DirectWindowOwnerSource => File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "samples", "design", "win32-dx11-triangle", "window-owner-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    [InteractiveWindowTest]
    [Category("InteractiveWindow")]
    public async Task DirectWindowOwnersConstructAndCleanUpWithoutANativeShim()
    {
        Assert.That(NativeWindowCreationContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        Assert.That(NativeWindowClassContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        Assert.That(NativeWindowOwnerContract.MatchesCatalog(NativeImportCatalog.WindowsX64), Is.True);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(DirectWindowOwnerSource)],
            runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        Assert.That(program.NativeImportCatalogId, Is.EqualTo("windows-x64-native-imports-v9"));
        Assert.That(ValueLayout.Size(PrimitiveType.NativeWindowMessage, program.Structures), Is.EqualTo(48));
        Assert.That(ValueLayout.Alignment(PrimitiveType.NativeWindowMessage, program.Structures), Is.EqualTo(8));
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);

        var directory = Directory.CreateTempSubdirectory("terrum-direct-window-owner-").FullName;
        var executablePath = Path.Combine(directory, "direct-window-owner.exe");
        try
        {
            await File.WriteAllBytesAsync(executablePath, image).ConfigureAwait(false);
            using var process = Process.Start(new ProcessStartInfo(executablePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = directory,
            }) ?? throw new InvalidOperationException("Direct Win32 owner executable did not start.");
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
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
                    await Task.Delay(50).ConfigureAwait(false);
                }

                Assert.That(process.HasExited, Is.False, "Direct Win32 owner exited before showing its window.");
                Assert.That(window, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(process.MainWindowTitle, Is.EqualTo("Terrum DX11 Triangle"));
                Assert.That(IsWindowVisible(window), Is.True, "The Terrum-owned window was not visible.");
                Assert.That(PostMessageW(window, WindowCloseMessage, IntPtr.Zero, IntPtr.Zero), Is.True, "The Terrum-owned window did not accept WM_CLOSE.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (!process.HasExited)
                {
                    throw new TimeoutException("Direct Win32 owner did not exit after WM_CLOSE.");
                }
                Assert.That(process.ExitCode, Is.EqualTo(0));
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DirectWindowOwnerGateRejectsHandleEscapeAndLifecycleMutations()
    {
        var source = DirectWindowOwnerSource;
        var mutations = new (string Original, string Replacement)[] {
            ("ThreadId == GetCurrentThreadId()", "ThreadId != GetCurrentThreadId()"),
            ("Handle == Terrum.Interop.Windows.HWND.NoOwner && Atom != 0",
                "Handle != Terrum.Interop.Windows.HWND.NoOwner && Atom != 0"),
            ("    private char16 Atom;\n    private uint32 ThreadId;",
                "    private uint32 ThreadId;\n    private char16 Atom;"),
            ("    [NoThrow] [Cleanup]\n    public void Close()",
                "    public Terrum.Interop.Windows.HWND Leak() {\n        return Handle;\n    }\n\n"
                    + "    [NoThrow] [Cleanup]\n    public void Close()"),
            ("        if (!succeeded) {\n            ExitProcess(87);\n        }",
                "        if (!succeeded) {\n            if (succeeded) {\n                ExitProcess(87);\n            }\n        }"),
            ("                ExitIfFalse(UnregisterClassW(\n"
                    + "                    utf16z(\"TerrumOwnerClass\"), GetModuleHandleW(null)) != 0);",
                "                if (Atom == 0) {\n"
                    + "                    ExitIfFalse(UnregisterClassW(\n"
                    + "                        utf16z(\"TerrumOwnerClass\"), GetModuleHandleW(null)) != 0);\n"
                    + "                }"),
            ("        ExitIfFalse(UnregisterClassW(\n"
                    + "            utf16z(\"TerrumOwnerClass\"), GetModuleHandleW(null)) != 0);",
                "        if (Atom == 0) {\n"
                    + "            ExitIfFalse(UnregisterClassW(\n"
                    + "                utf16z(\"TerrumOwnerClass\"), GetModuleHandleW(null)) != 0);\n"
                    + "        }"),
            ("if (message == 16) {", "if (message == 129) {"),
            ("    public static nint WindowProcedure(Terrum.Interop.Windows.HWND window, uint32 message,\n"
                + "        nuint wParam, nint lParam) {\n        if (message == 16) {",
                "    public static nint WindowProcedure(Terrum.Interop.Windows.HWND window, uint32 message,\n"
                    + "        nuint wParam, nint lParam) {\n        while (message == 129) {\n"
                    + "            ExitProcess(0);\n        }\n        if (message == 16) {"),
            ("while (result > 0)", "while (result != 0)"),
            ("return result < 0 ? 1 : 0;", "return 0;"),
        };

        foreach (var mutation in mutations)
        {
            Assert.That(source.Contains(mutation.Original, StringComparison.Ordinal), Is.True, mutation.Original);
            var mutatedSource = source.Replace(mutation.Original, mutation.Replacement, StringComparison.Ordinal);
            var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mutatedSource)],
                runtimePolicy: BoundRuntimePolicy.CheckedExceptions);
            Assert.That(compilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
        }

        var failFastCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)],
            runtimePolicy: BoundRuntimePolicy.FailFast);
        Assert.That(failFastCompilation.Diagnostics, Has.Some.Matches(static diagnostic => diagnostic.Code == "TRM3030"));
    }
}
