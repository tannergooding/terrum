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
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("public int32 Value(int32 x) { var y = x; mut int32 z = y + 1; z = z * 2; return z; }")]
    [InlineData("public int64 Value(int64 x) { mut var y = x; y = y - 1; return y; }")]
    [InlineData("public int64 Value(int64 x) { int64 y = -9223372036854775808; return y + x; }")]
    [InlineData("public uint32 Value(uint32 x) { uint32 y = x & 255; return y + 1; }")]
    [InlineData("public uint64 Value(uint64 x) { mut var y = x; y = y | 0xFFFFFFFFFFFFFFFF; return y; }")]
    [InlineData("public bool Value(bool x) { mut var y = x; y = !y; return y || x; }")]
    [InlineData("public char16 Value(char16 x) { var y = x; return y; }")]
    [InlineData("public int32 Value(int32 x, bool flag) { mut var y = x; y = y + 2; if (flag && y > x) { return y; } return x; }")]
    [InlineData("public bool Value(int64 x) { var y = x; return (1 | 2) == y; }")]
    [InlineData("public bool Value(int32 x) { var less = 1 < x; return less; }")]
    [InlineData("public int32 Value(int32 x) { var a = x; var b = a; var c = b; var d = c; var e = d; var f = e; var g = f; var h = g; return h; }")]
    [InlineData("public int32 Value(int32 x) { mut var y = x; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; return y; }")]
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
                Assert.Equal(0, nativeExit);
                Assert.Equal(0, hostedExit);
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Fact]
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
                Assert.True(nativeExit == 0, $"{nativeExit}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{hostedExit}: {hostedOutput}{hostedError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task LocalFrontendDoesNotReturnLibraryMatchesHostedBytesAndExecutionAsync(string policy)
    {
        const string source = """
            [DoesNotReturn] public void Halt() { require false; }
            public int32 Value() {
                if (false) { Halt(); }
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
            Assert.True(nativeExit == 0, $"{nativeExit}: {nativeOutput}{nativeError}");
            Assert.True(hostedExit == 0, $"{hostedExit}: {hostedOutput}{hostedError}");
            Assert.Empty(nativeError);
            Assert.Empty(hostedError);
            Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                await File.ReadAllBytesAsync(nativePath));

            var handle = NativeLibrary.Load(nativePath);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScopedInt32>(
                    NativeLibrary.GetExport(handle, "Value"));
                Assert.Equal(42, invoke());
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

    [Theory]
    [InlineData("unsafe { return Danger() + 1; }")]
    [InlineData("return unsafe(Danger()) + 1;")]
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
                Assert.True(nativeExit == 0, $"{nativeExit}: {nativeOutput}{nativeError}");
                Assert.True(hostedExit == 0, $"{hostedExit}: {hostedOutput}{hostedError}");
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));

                var handle = NativeLibrary.Load(nativePath);
                try
                {
                    var invoke = Marshal.GetDelegateForFunctionPointer<ScopedInt32>(
                        NativeLibrary.GetExport(handle, "Value"));
                    Assert.Equal(42, invoke());
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

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
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
            Assert.True(success == 0, $"{success}: {successOutput}{successError}");
            var previous = await File.ReadAllBytesAsync(outputPath);

            var (failure, failureOutput, failureError) = await RunAsync(
                Encoding.UTF8.GetBytes(invalidSource), "frontend-build-library", frontend, "-",
                "-o", outputPath, "--runtime", policy, "--json");
            Assert.Equal(1, failure);
            Assert.Empty(failureError);
            using var document = JsonDocument.Parse(failureOutput);
            var result = document.RootElement;
            Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("frontend-build-library", result.GetProperty("command").GetString());
            Assert.Equal("<stdin>", result.GetProperty("path").GetString());
            Assert.Equal(invalidSource.Length, result.GetProperty("sourceLength").GetInt32());
            Assert.Equal(policy, result.GetProperty("runtimePolicy").GetString());
            Assert.False(result.GetProperty("success").GetBoolean());
            Assert.False(result.TryGetProperty("artifact", out _));
            var diagnostic = Assert.Single(result.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("TRM3028", diagnostic.GetProperty("code").GetString());
            Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
            Assert.Equal("Calling 'Danger' requires an explicit unsafe context.",
                diagnostic.GetProperty("message").GetString());
            Assert.Equal(invalidSource.LastIndexOf("Danger()", StringComparison.Ordinal),
                diagnostic.GetProperty("span").GetProperty("start").GetInt32());
            Assert.Equal("Danger()".Length, diagnostic.GetProperty("span").GetProperty("length").GetInt32());
            Assert.Equal(previous, await File.ReadAllBytesAsync(outputPath));

            var (missingFailure, missingOutput, missingError) = await RunAsync(
                Encoding.UTF8.GetBytes(invalidSource), "frontend-build-library", frontend, "-",
                "-o", missingPath, "--runtime", policy, "--json");
            Assert.Equal(1, missingFailure);
            Assert.Empty(missingError);
            using (var missingDocument = JsonDocument.Parse(missingOutput))
            {
                Assert.False(missingDocument.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal("TRM3028", missingDocument.RootElement.GetProperty("diagnostics")[0]
                    .GetProperty("code").GetString());
            }
            Assert.False(File.Exists(missingPath));

            if (policy == "checked-exceptions")
            {
                var (textFailure, textOutput, textError) = await RunAsync(
                    Encoding.UTF8.GetBytes(invalidSource), "frontend-build-library", frontend, "-",
                    "-o", missingPath, "--runtime", policy);
                Assert.Equal(1, textFailure);
                Assert.Empty(textOutput);
                Assert.Contains("<stdin>(1,", textError, StringComparison.Ordinal);
                Assert.Contains("TRM3028", textError, StringComparison.Ordinal);
                Assert.Contains("Calling 'Danger' requires an explicit unsafe context.",
                    textError, StringComparison.Ordinal);
                Assert.False(File.Exists(missingPath));
            }
        }
        finally
        {
            File.Delete(outputPath);
            File.Delete(missingPath);
        }
    }

    [Theory]
    [InlineData("int32 Main() { mut int32 x = 20; x = x + 2; return x; }", 22)]
    [InlineData("int32 Main() { bool skip = false; mut int32 x = 40; if (skip && x / 0 == 1) { return 0; } return x + 2; }", 42)]
    [InlineData("int32 Main() { bool x = true; if (x) { return 42; } else { return 0; } }", 42)]
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
                Assert.Equal(expected, nativeRun);
                Assert.Equal(hostedRun, nativeRun);
                Assert.Empty(nativeError);
                Assert.Empty(hostedError);
                var (nativeBuild, _, _) = await RunAsync(input, "frontend-build",
                    frontend, "-", "-o", nativePath, "--runtime", policy, "--json");
                var (hostedBuild, _, _) = await RunAsync(input, "build",
                    "-", "-o", hostedPath, "--runtime", policy, "--json");
                Assert.Equal(0, nativeBuild);
                Assert.Equal(0, hostedBuild);
                Assert.Equal(await File.ReadAllBytesAsync(hostedPath),
                    await File.ReadAllBytesAsync(nativePath));
                using var process = Process.Start(new ProcessStartInfo(nativePath) {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("Could not start native frontend executable.");
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Assert.Equal(expected, process.ExitCode);
            }
        }
        finally
        {
            File.Delete(nativePath);
            File.Delete(hostedPath);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
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
        Assert.NotEqual(0, nativeExit);
        Assert.Equal(hostedExit, nativeExit);
    }

    [Theory]
    [InlineData("public int32 Value(int32 x) { var y = 1; return x; }", 2, "1")]
    [InlineData("public int32 Value(int32 x) { var y = 1 + 2; return x; }", 2, "1 + 2")]
    [InlineData("public int32 Value(int32 x) { var y = missing; return x; }", 3, "missing")]
    [InlineData("public int32 Value(int32 x) { var y = x; y = x; return y; }", 2, "y =")]
    [InlineData("public int32 Value(int32 x) { x = 1; return x; }", 2, "x =")]
    [InlineData("public int32 Value(int32 x) { var x = x; return x; }", 4, "x =")]
    [InlineData("public int32 Value(int32 x) { var y = x; var y = x; return y; }", 4, "y =")]
    [InlineData("public int32 Value(int32 x) { var y = z; int32 z = x; return y; }", 3, "z;")]
    [InlineData("public int32 Value(int32 x) { bool y = x; return x; }", 2, "x; return")]
    [InlineData("public bool Value(int32 x) { var y = (1 + 2) < 4; return y; }", 2, "<")]
    [InlineData("public int32 Value(int32 x) { mut int32 y = x; y = 2147483648; return y; }", 2, "2147483648")]
    public async Task LocalFrontendRejectsInvalidBindingsAsync(string source, int code, string offending)
    {
        Assert.True(Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() => 42;")).HasErrors);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("valid").GetBoolean());
        Assert.Equal(code, result.GetProperty("errorCode").GetInt32());
        Assert.Equal(source.LastIndexOf(offending, StringComparison.Ordinal),
            result.GetProperty("errorSpan").GetProperty("start").GetInt32());
    }

    [Fact]
    public async Task LocalFrontendPublishesOrderedSourceFactsAsync()
    {
        const string source = "public int32 Value(int32 x) { mut var y = x; y = y + 1; return y; }";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal(85, result.GetProperty("projectionVersion").GetInt32());
        var locals = result.GetProperty("locals");
        Assert.Equal(1, locals.GetArrayLength());
        Assert.Equal(1, locals[0].GetProperty("type").GetInt32());
        Assert.True(locals[0].GetProperty("mutable").GetBoolean());
        Assert.Equal(source.IndexOf("y = x", StringComparison.Ordinal),
            locals[0].GetProperty("name").GetProperty("start").GetInt32());
        var statements = result.GetProperty("statements");
        Assert.Equal(2, statements.GetArrayLength());
        Assert.Equal(1, statements[0].GetProperty("kind").GetInt32());
        Assert.Equal(2, statements[1].GetProperty("kind").GetInt32());
        Assert.Equal(2, statements[0].GetProperty("slot").GetInt32());
        Assert.Equal(2, statements[1].GetProperty("slot").GetInt32());
        Assert.Equal(source.IndexOf("mut var", StringComparison.Ordinal),
            statements[0].GetProperty("span").GetProperty("start").GetInt32());
        Assert.Equal(source.IndexOf("y = y", StringComparison.Ordinal),
            statements[1].GetProperty("span").GetProperty("start").GetInt32());
    }

    [Theory]
    [InlineData("public int32 Value(int32 x) { var a = x; var b = a; var c = b; var d = c; var e = d; var f = e; var g = f; var h = g; var i = h; return i; }")]
    [InlineData("int32 Main() { mut int32 y = 0; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; y = y; return y; }")]
    public async Task LocalFrontendRejectsBoundedStorageOverflowAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32());
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
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
            Assert.Equal(0, success);
            var previous = await File.ReadAllBytesAsync(output);
            var (failure, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) { var y = x; y = x; return y; }"),
                "frontend-build-library", frontend, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.Equal(1, failure);
            Assert.Equal(previous, await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(output);
        }
    }
}
