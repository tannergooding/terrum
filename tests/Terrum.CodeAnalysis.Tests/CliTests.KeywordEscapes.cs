// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task KeywordEscapesExecuteAcrossHostedComponentAndFrontendAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var scanner = frontend;
        const string Source = "/// heading\nint32 Main() { mut int32 @const = 0; while (const < 42) { @const += 1; } return const; }";
        foreach (var arguments in new[] {
            new[] { "jit", "-", "--runtime", policy, "--json" },
            new[] { "frontend-run", frontend, "-", "--runtime", policy, "--json" },
        })
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments);
            Assert.Equal(42, exit);
            Assert.Empty(output);
            Assert.Empty(error);
        }
        var prefix = Path.Combine(root, "artifacts", $"keyword-escapes-{Guid.NewGuid():N}");
        var hosted = prefix + ".hosted.exe";
        var component = prefix + ".component.exe";
        var native = prefix + ".frontend.exe";
        var library = prefix + ".frontend.dll";
        var hostedLibrary = prefix + ".hosted.dll";
        try
        {
            foreach (var arguments in new[] {
                new[] { "build", "-", "-o", hosted, "--runtime", policy, "--json" },
                new[] { "build-component", scanner, "-", "-o", component, "--runtime", policy, "--json" },
                new[] { "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json" },
            })
            {
                var (exit, _, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), arguments);
                Assert.Equal(0, exit);
                Assert.Empty(error);
            }
            Assert.Equal(42, await ExecuteNativeImageAsync(hosted));
            Assert.Equal(42, await ExecuteNativeImageAsync(component));
            Assert.Equal(42, await ExecuteNativeImageAsync(native));
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(native));
            const string Library = "public int32 @await(int32 @const, int32 some) { mut var @return = const + @some; @return += 2; return @return; }";
            var (libraryExit, _, libraryError) = await RunAsync(Encoding.UTF8.GetBytes(Library),
                "frontend-build-library", frontend, "-", "-o", library, "--runtime", policy, "--json");
            Assert.Equal(0, libraryExit);
            Assert.Empty(libraryError);
            var (hostedExit, _, hostedError) = await RunAsync(Encoding.UTF8.GetBytes(Library),
                "build-library", "-", "--export", "await", "-o", hostedLibrary, "--runtime", policy, "--json");
            Assert.Equal(0, hostedExit);
            Assert.Empty(hostedError);
            Assert.Equal(await File.ReadAllBytesAsync(hostedLibrary), await File.ReadAllBytesAsync(library));
            var handle = NativeLibrary.Load(library);
            try
            {
                var invoke = Marshal.GetDelegateForFunctionPointer<ScalarBinary>(NativeLibrary.GetExport(handle, "await"));
                Assert.Equal(42, invoke(20, 20));
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
            var original = await File.ReadAllBytesAsync(native);
            var (invalidExit, _, invalidError) = await RunAsync(
                Encoding.UTF8.GetBytes("int32 Main() { int32 @out = 42; return @out; }"),
                "frontend-build", frontend, "-", "-o", native, "--runtime", policy, "--json");
            Assert.Equal(1, invalidExit);
            Assert.Empty(invalidError);
            Assert.Equal(original, await File.ReadAllBytesAsync(native));
        }
        finally
        {
            foreach (var path in new[] { hosted, component, native, library, hostedLibrary })
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task KeywordEscapeScannerComponentPreservesRawNamesAndSpansAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        const string Source = "/* heading */ @await @const @union @via @out @int32 @ await";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source), "scan-component",
            Path.Combine(root, "self-host"), "-", "--runtime", policy, "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(ScannerHost.SchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        var tokens = document.RootElement.GetProperty("tokens").EnumerateArray().ToArray();
        Assert.Equal(new[] { "@await", "@const", "@union", "@via", "@out", "@int32", "@", "await", "" },
            tokens.Select(token => Source.Substring(token.GetProperty("start").GetInt32(),
                token.GetProperty("length").GetInt32())));
        Assert.Equal(new[] { "Identifier", "Identifier", "Identifier", "Identifier", "Bad", "Bad", "Bad", "AwaitKeyword", "EndOfFile" },
            tokens.Select(token => token.GetProperty("kind").GetString()));
        Assert.Equal(14, tokens[0].GetProperty("start").GetInt32());
        Assert.Equal(6, tokens[0].GetProperty("length").GetInt32());
    }
}
