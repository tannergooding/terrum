// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Native.Interop;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public void Utf16ZLiteralCannotBeForwardedToAnUnannotatedUnsafeFunction()
    {
        var source = """
            unsafe void Consume(ptr<char16> value) { }

            int32 Main() {
                unsafe { Consume(utf16z("native")); }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.Program, Is.Null);
        Assert.That(compilation.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3011"), Is.True);
    }

    [Test]
    public void SourceCannotShadowCompilerOwnedImportAttribute()
    {
        var source = $$"""
            namespace Terrum.Interop;
            struct NativeImport { int32 Value; }
            [NoThrow]
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]
            extern uint32 CurrentThreadId();
            void Main() {}
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3003"),Is.True);
    }

    [Test]
    public void Utf16ZRequiresAValidLiteralAndExplicitNoEscapeArgument()
    {
        var declaration = """
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiUtf16Z")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(4, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(6, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 NativeFfiUtf16Z(ptr<char16> first, uint32 second, uint64 third,
                ptr<char16> fourth, uint32 fifth, ptr<char16> sixth);
            """;
        var sources = new[] {
            declaration + """
                int32 Main() {
                    unsafe {
                        return NativeFfiUtf16Z(utf16z("bad\0text"), 1, 2,
                            utf16z("second"), 3, utf16z("third"));
                    }
                }
                """,
            declaration + """
                int32 Main() {
                    unsafe {
                        return NativeFfiUtf16Z(utf16z("first" + "suffix"), 1, 2,
                            utf16z("second"), 3, utf16z("third"));
                    }
                }
                """,
            declaration + """
                int32 Main() {
                    unsafe {
                        return NativeFfiUtf16Z("first", 1, 2,
                            utf16z("second"), 3, utf16z("third"));
                    }
                }
                """,
            declaration + """
                int32 Main() {
                    ptr<char16> value = utf16z("first");
                    return 0;
                }
                """,
        };

        foreach (var source in sources)
        {
            var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        var addressSource = declaration + """
            int32 Main() {
                InlineArray<char16, 2> text = [char16(65), char16(0)];
                unsafe {
                    return NativeFfiUtf16Z(&text[0], 1, 2,
                        utf16z("second"), 3, utf16z("third"));
                }
            }
            """;
        var addressCompilation = Compilation.Create(
            SyntaxTree.Parse(addressSource), BoundRuntimePolicy.FailFast);
        Assert.That(addressCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, addressCompilation.Diagnostics));

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
    public void Utf16ZLiteralIndexIsRecheckedBeforeNativeEmission()
    {
        var source = """
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiUtf16Z")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 NativeFfiUtf16Z(ptr<char16> text);
            int32 Main() {
                unsafe {
                    return NativeFfiUtf16Z(utf16z("first"));
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(
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
    public async Task DirectImportsCallOrdinarySystemFunctionsAsync()
    {
        Assert.That(OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64, Is.True);
        var source = $$"""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]
            [NoThrow]
            extern unsafe uint32 CurrentThreadId();

            [Terrum.Interop.NativeImport("kernel32.dll", "Sleep")]
            [NoThrow]
            extern unsafe void Sleep(uint32 milliseconds);

            int32 Main() {
                unsafe {
                    if (CurrentThreadId() == 0) {
                        return 1;
                    }
                    Sleep(1);
                }
                return 0;
            }

            int32 Helper() { return 7; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        Assert.That(program.Functions.Count(static function => function.IsExternal), Is.EqualTo(2));

        var image = NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast);
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitWithImports(program,
            [new NativeImportBinding("CurrentThreadId", "kernel32.dll", "GetCurrentThreadId")],
            NativeRuntimePolicy.FailFast))!;
        _ = Assert.Throws<NotSupportedException>(() => NativeEmitter.EmitCode(program,
            NativeRuntimePolicy.FailFast))!;

        var directory = TestArtifactPaths.CreateDirectory("terrum-source-import-");
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
    public void NativeJitHandlesCaseVariantDllNamesAndSnapshotsHostBuffers()
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("kernel32.dll", "GetCurrentThreadId")]
            [NoThrow]
            extern unsafe uint32 CurrentThreadId();

            [Terrum.Interop.NativeImport("KERNEL32.DLL", "GetCurrentThreadId")]
            [NoThrow]
            extern unsafe uint32 CurrentThreadIdAgain();

            [Terrum.Interop.NativeImport("Kernel32.Dll", "lstrlenW")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 WideLength(ptr<char16> value);

            [Terrum.Interop.NativeImport("KERNEL32.DLL", "Sleep")]
            [NoThrow]
            extern unsafe void Sleep(uint32 milliseconds);

            int32 Main() {
                unsafe {
                    if (CurrentThreadId() == 0 || CurrentThreadIdAgain() == 0) {
                        return 1;
                    }
                    if (WideLength(utf16z("abc")) != 3) {
                        return 2;
                    }
                    Sleep(1);
                }
                return 0;
            }
            """), [
                new HostBuffer("ReadOnly", PrimitiveType.Int32, [0x12345678, 0x23456789], false),
                new HostBuffer("Writable", PrimitiveType.Int32, [0x3456789A], true),
            ], BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        var execution = NativeJit.ExecuteWithData(compilation.Program!, NativeRuntimePolicy.FailFast);
        Assert.That(execution.ExitCode, Is.Zero);
        Assert.That(execution.BufferOffsets, Is.EqualTo(new[] { 0, 8 }));
        Assert.That(execution.Data, Is.EqualTo(new byte[] {
            0x78, 0x56, 0x34, 0x12, 0x89, 0x67, 0x45, 0x23, 0x9A, 0x78, 0x56, 0x34,
        }));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SourceImportsUseSystemScalarAndStackArgumentAbisAsync(NativeRuntimePolicy policy)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("ucrtbase.dll", "_abs64")]
            [NoThrow]
            extern unsafe int64 Absolute(int64 value);

            [Terrum.Interop.NativeImport("ucrtbase.dll", "_rotl64")]
            [NoThrow]
            extern unsafe uint64 Rotate(uint64 value, int32 count);

            [Terrum.Interop.NativeImport("kernel32.dll", "CompareStringOrdinal")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 Compare(
                ptr<char16> first, int32 firstLength,
                ptr<char16> second, int32 secondLength, int32 ignoreCase);

            int32 Main() {
                unsafe {
                    if (Absolute(-1234567890123) != 1234567890123) { return 1; }
                    if (Rotate(0xFEDCBA9876543210, 4) != 0xEDCBA9876543210F) { return 2; }
                    if (Compare(ignoreCase: 1, secondLength: -1,
                        second: utf16z("a\n\U0001F642"), firstLength: -1,
                        first: utf16z("A\n\U0001F642")) != 2) { return 3; }
                    if (Compare(utf16z("A\n\U0001F642"), -1,
                        utf16z("a\n\U0001F642"), -1, 0) != 1) { return 4; }
                    if (Compare(utf16z(""), -1, utf16z(""), -1, 0) != 2) { return 5; }
                }
                return 0;
            }
            """), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.EmitWithNativeImports(compilation.Program!, policy);
        var directory = TestArtifactPaths.CreateDirectory("terrum-system-import-abi-");
        try
        {
            var path = Path.Combine(directory, "system-import-abi.exe");
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(false);
            _ = await RunAsync(new ProcessStartInfo(path) { WorkingDirectory = directory },
                TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        finally
        {
            await TestFileCleanup.DeleteDirectoryWithRetryAsync(directory).ConfigureAwait(false);
        }
    }

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
