// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("public int32 Value(int32 x) { var y = x; mut int32 z = y + 1; z = z * 2; return z; }")]
    [TestCase("public int64 Value(int64 x) { mut var y = x; y = y - 1; return y; }")]
    [TestCase("public int64 Value(int64 x) { int64 y = -9223372036854775808; return y + x; }")]
    [TestCase("public uint32 Value(uint32 x) { uint32 y = x & 255; return y + 1; }")]
    [TestCase("public uint64 Value(uint64 x) { mut var y = x; y = y | 0xFFFFFFFFFFFFFFFF; return y; }")]
    [TestCase("public bool Value(bool x) { mut var y = x; y = !y; return y || x; }")]
    [TestCase("public char16 Value(char16 x) { var y = x; return y; }")]
    [TestCase("public int32 Value(int32 x, bool flag) { mut var y = x; y = y + 2; if (flag && y > x) { return y; } return x; }")]
    [TestCase("public bool Value(int64 x) { var y = x; return (1 | 2) == y; }")]
    [TestCase("public bool Value(int32 x) { var less = 1 < x; return less; }")]
    [TestCase("public int32 Value(int32 x) { var a = x; var b = a; var c = b; var d = c; var e = d; var f = e; var g = f; var h = g; return h; }")]
    [TestCase("public int32 Value(int32 x) { mut var y = x; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; return y; }")]
    public async Task LocalFrontendLibraryMatchesHostedBytesAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, _, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, _, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit, Is.EqualTo(0));
                Assert.That(hostedExit, Is.EqualTo(0));
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Test]
    public async Task LocalFrontendSwitchLibraryMatchesHostedIfChainAsync()
    {
        const string source = """
            public int32 Value(int32 x) {
                switch (x) {
                    1 => { return 42; },
                    _ => { return 0; },
                }
            }
            """;
        const string hostedEquivalent = """
            public int32 Value(int32 x) {
                int32 choice = x;
                if (choice == 1) {
                    return 42;
                } else {
                    return 0;
                }
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var hostedInput = Encoding.UTF8.GetBytes(hostedEquivalent);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(hostedInput,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task LocalFrontendDoesNotReturnLibraryMatchesHostedBytesAndExecutionAsync(string policy)
    {
        const string source = """
            [DoesNotReturn] public void Halt() { require false; }
            public int32 Value(int32 stop) {
                if (stop != 0) { Halt(); }
                return 42;
            }
            """;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                "frontend-build-library", frontend, "-", "-o", nativePath,
                "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Halt", "--export", "Value", "-o", hostedPath,
                "--runtime", policy, "--json");
            Assert.That(nativeExit == 0, Is.True, $"{nativeExit}: {nativeOutput}{nativeError}");
            Assert.That(hostedExit == 0, Is.True, $"{hostedExit}: {hostedOutput}{hostedError}");
            Assert.That(nativeError, Is.Empty);
            Assert.That(hostedError, Is.Empty);
            Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));

            var handle = NativeLibrary.Load(nativePath);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScopedInt32Unary>(
                    NativeLibrary.GetExport(handle, "Value"));
                Assert.That(invoke(0), Is.EqualTo(42));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    private delegate int ScopedInt32Unary(int value);
    [TestCase("unsafe { return Danger() + 1; }")]
    [TestCase("return unsafe(Danger()) + 1;")]
    public async Task LocalFrontendCallerUnsafeLibraryMatchesHostedBytesAndExecutionAsync(string body)
    {
        var source = $"unsafe int32 Danger() {{ return 41; }} public int32 Value() {{ {body} }}";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.hosted.dll");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                    "frontend-build-library", frontend, "-", "-o", nativePath,
                    "--runtime", policy, "--json");
                var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                    "build-library", "-", "--export", "Value", "-o", hostedPath,
                    "--runtime", policy, "--json");
                Assert.That(nativeExit == 0, Is.True, $"{nativeExit}: {nativeOutput}{nativeError}");
                Assert.That(hostedExit == 0, Is.True, $"{hostedExit}: {hostedOutput}{hostedError}");
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));

                var handle = NativeLibrary.Load(nativePath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<ScopedInt32>(
                        NativeLibrary.GetExport(handle, "Value"));
                    Assert.That(invoke(), Is.EqualTo(42));
                }
                finally
                {
                    NativeLibrary.Free(handle);
                }
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task LocalFrontendRejectsUnscopedCallerUnsafeCallWithoutReplacingLibraryAsync(string policy)
    {
        const string validSource = "public int32 Value() { return 42; }";
        const string invalidSource = "unsafe int32 Danger() { return 41; } public int32 Value() { return Danger(); }";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var outputPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.dll");
        var missingPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.dll");
        try
        {
            var (success, successOutput, successError) = await RunAsync(
                Encoding.UTF8.GetBytes(validSource), "frontend-build-library", frontend, "-",
                "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(success == 0, Is.True, $"{success}: {successOutput}{successError}");
            var previous = await File.ReadAllBytesAsync(outputPath);

            var (failure, failureOutput, failureError) = await RunAsync(
                Encoding.UTF8.GetBytes(invalidSource), "frontend-build-library", frontend, "-",
                "-o", outputPath, "--runtime", policy, "--json");
            Assert.That(failure, Is.EqualTo(1));
            Assert.That(failureError, Is.Empty);
            using var document = JsonDocument.Parse(failureOutput);
            var result = document.RootElement;
            Assert.That(result.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(result.GetProperty("command").GetString(), Is.EqualTo("frontend-build-library"));
            Assert.That(result.GetProperty("path").GetString(), Is.EqualTo("<stdin>"));
            Assert.That(result.GetProperty("sourceLength").GetInt32(), Is.EqualTo(invalidSource.Length));
            Assert.That(result.GetProperty("runtimePolicy").GetString(), Is.EqualTo(policy));
            Assert.That(result.GetProperty("success").GetBoolean(), Is.False);
            Assert.That(result.TryGetProperty("artifact", out _), Is.False);
            var diagnostic = global::System.Linq.Enumerable.Single(result.GetProperty("diagnostics").EnumerateArray());
            Assert.That(diagnostic.GetProperty("code").GetString(), Is.EqualTo("TRM3028"));
            Assert.That(diagnostic.GetProperty("severity").GetString(), Is.EqualTo("error"));
            Assert.That(diagnostic.GetProperty("message").GetString(), Is.EqualTo("Calling 'Danger' requires an explicit unsafe context."));
            Assert.That(diagnostic.GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(invalidSource.LastIndexOf("Danger()", StringComparison.Ordinal)));
            Assert.That(diagnostic.GetProperty("span").GetProperty("length").GetInt32(), Is.EqualTo("Danger()".Length));
            Assert.That(await File.ReadAllBytesAsync(outputPath), Is.EqualTo(previous));

            var (missingFailure, missingOutput, missingError) = await RunAsync(
                Encoding.UTF8.GetBytes(invalidSource), "frontend-build-library", frontend, "-",
                "-o", missingPath, "--runtime", policy, "--json");
            Assert.That(missingFailure, Is.EqualTo(1));
            Assert.That(missingError, Is.Empty);
            using (var missingDocument = JsonDocument.Parse(missingOutput))
            {
                Assert.That(missingDocument.RootElement.GetProperty("success").GetBoolean(), Is.False);
                Assert.That(missingDocument.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString(), Is.EqualTo("TRM3028"));
            }
            Assert.That(File.Exists(missingPath), Is.False);

            if (policy == "checked-exceptions")
            {
                var (textFailure, textOutput, textError) = await RunAsync(
                    Encoding.UTF8.GetBytes(invalidSource), "frontend-build-library", frontend, "-",
                    "-o", missingPath, "--runtime", policy);
                Assert.That(textFailure, Is.EqualTo(1));
                Assert.That(textOutput, Is.Empty);
                Assert.That(textError, Does.Contain("<stdin>(1,"));
                Assert.That(textError, Does.Contain("TRM3028"));
                Assert.That(textError, Does.Contain("Calling 'Danger' requires an explicit unsafe context."));
                Assert.That(File.Exists(missingPath), Is.False);
            }
        }
        finally
        {
            File.Delete(outputPath);
            File.Delete(missingPath);
        }
    }
    [TestCase("int32 Main() { mut int32 x = 20; x = x + 2; return x; }",22)]
    [TestCase("int32 Main() { bool skip = false; mut int32 x = 40; if (skip && x / 0 == 1) { return 0; } return x + 2; }",42)]
    [TestCase("int32 Main() { bool x = true; if (x) { return 42; } else { return 0; } }",42)]
    public async Task LocalFrontendEntryMatchesHostedJitAndImageAsync(string source, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.exe");
        var hostedPath = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.hosted.exe");
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var input = Encoding.UTF8.GetBytes(source);
                var (nativeRun, _, nativeError) = await RunAsync(input,
                    "frontend-run", frontend, "-", "--runtime", policy, "--json");
                var (hostedRun, _, hostedError) = await RunAsync(input,
                    "jit", "-", "--runtime", policy, "--json");
                Assert.That(nativeRun, Is.EqualTo(expected));
                Assert.That(nativeRun, Is.EqualTo(hostedRun));
                Assert.That(nativeError, Is.Empty);
                Assert.That(hostedError, Is.Empty);
                var (nativeBuild, _, _) = await RunAsync(input, "frontend-build",
                    frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedBuild, _, _) = await RunAsync(input, "build",
                    "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.That(nativeBuild, Is.EqualTo(0));
                Assert.That(hostedBuild, Is.EqualTo(0));
                Assert.That(await File.ReadAllBytesAsync(nativePath), Is.EqualTo(await File.ReadAllBytesAsync(hostedPath)));
                using var process = Process.Start(new ProcessStartInfo(nativePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start native frontend executable.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.That(process.ExitCode, Is.EqualTo(expected));
            }
        }
        finally
        {
            await TestFileCleanup.DeleteWithRetryAsync(nativePath).ConfigureAwait(true);
            File.Delete(hostedPath);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task LocalFrontendDoesNotSuppressReachedCheckedFailureAsync(string policy)
    {
        const string source = "int32 Main() { mut int32 x = 2147483647; x = x + 1; return x; }";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var input = Encoding.UTF8.GetBytes(source);
        var (nativeExit, _, _) = await RunAsync(input, "frontend-run",
            Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
        var (hostedExit, _, _) = await RunAsync(input, "jit", "-",
            "--runtime", policy, "--json");
        Assert.That(nativeExit, Is.Not.EqualTo(0));
        Assert.That(nativeExit, Is.EqualTo(hostedExit));
    }
    [TestCase("public int32 Value(int32 x) { var y = 1; return x; }",2,"1")]
    [TestCase("public int32 Value(int32 x) { var y = 1 + 2; return x; }",2,"1 + 2")]
    [TestCase("public int32 Value(int32 x) { var y = missing; return x; }",3,"missing")]
    [TestCase("public int32 Value(int32 x) { var y = x; y = x; return y; }",2,"y =")]
    [TestCase("public int32 Value(int32 x) { x = 1; return x; }",2,"x =")]
    [TestCase("public int32 Value(int32 x) { var x = x; return x; }",4,"x =")]
    [TestCase("public int32 Value(int32 x) { var y = x; var y = x; return y; }",4,"y =")]
    [TestCase("public int32 Value(int32 x) { var y = z; int32 z = x; return y; }",3,"z;")]
    [TestCase("public int32 Value(int32 x) { bool y = x; return x; }",2,"x; return")]
    [TestCase("public bool Value(int32 x) { var y = (1 + 2) < 4; return y; }",2,"<")]
    [TestCase("public int32 Value(int32 x) { mut int32 y = x; y = 2147483648; return y; }",2,"2147483648")]
    public async Task LocalFrontendRejectsInvalidBindingsAsync(string source, int code, string offending)
    {
        Assert.That(Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() => 42;")).HasErrors, Is.True);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(code));
        Assert.That(result.GetProperty("errorSpan").GetProperty("start").GetInt32(), Is.EqualTo(source.LastIndexOf(offending, StringComparison.Ordinal)));
    }

    [Test]
    public async Task LocalFrontendPublishesOrderedSourceFactsAsync()
    {
        const string source = "public int32 Value(int32 x) { mut var y = x; y = y + 1; return y; }";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
        var locals = result.GetProperty("locals");
        Assert.That(locals.GetArrayLength(), Is.EqualTo(1));
        Assert.That(locals[0].GetProperty("type").GetInt32(), Is.EqualTo(1));
        Assert.That(locals[0].GetProperty("mutable").GetBoolean(), Is.True);
        Assert.That(locals[0].GetProperty("name").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf("y = x", StringComparison.Ordinal)));
        var statements = result.GetProperty("statements");
        Assert.That(statements.GetArrayLength(), Is.EqualTo(2));
        Assert.That(statements[0].GetProperty("kind").GetInt32(), Is.EqualTo(1));
        Assert.That(statements[1].GetProperty("kind").GetInt32(), Is.EqualTo(2));
        Assert.That(statements[0].GetProperty("slot").GetInt32(), Is.EqualTo(2));
        Assert.That(statements[1].GetProperty("slot").GetInt32(), Is.EqualTo(2));
        Assert.That(statements[0].GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf("mut var", StringComparison.Ordinal)));
        Assert.That(statements[1].GetProperty("span").GetProperty("start").GetInt32(), Is.EqualTo(source.IndexOf("y = y", StringComparison.Ordinal)));
    }
    [TestCase("public int32 Value(int32 x) { var a = x; var b = a; var c = b; var d = c; var e = d; var f = e; var g = f; var h = g; var i = h; return i; }")]
    [TestCase("int32 Main() { mut int32 y = 0; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; return y; }")]
    public async Task LocalFrontendRejectsBoundedStorageOverflowAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task LocalFrontendFailureRetainsPriorImageAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var output = Path.Combine(root, $"local-frontend-{Guid.NewGuid():N}.dll");
        try
        {
            var (success, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) { var y = x; return y + 1; }"),
                "frontend-build-library", frontend, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(success, Is.EqualTo(0));
            var previous = await File.ReadAllBytesAsync(output);
            var (failure, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) { var y = x; y = x; return y; }"),
                "frontend-build-library", frontend, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(failure, Is.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(previous));
        }
        finally
        {
            File.Delete(output);
        }
    }
}
