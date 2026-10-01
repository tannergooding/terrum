// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
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
    [InlineData("public int32 Value(int32 x, int32 y) { return x + y + x; }")]
    [InlineData("public int32 Value(int32 x) => +(+(+(+(+(+(+(+x))))))) + x + x + x + x + x + x;")]
    [InlineData("public int32 Value(int32 x, int32 y) => x + (y * 3) - (x / 2);")]
    [InlineData("public int64 Value(int64 x) => -(x + (10 * (x - 3)));")]
    [InlineData("public uint32 Value(uint32 x) => (x & 7) | ((x ^ 3) + 1);")]
    [InlineData("public uint64 Value(uint64 x) => (x ^ 0xFFFFFFFFFFFFFFFF) & (x | 4);")]
    [InlineData("public bool Value(bool x, bool y) => !(x && (false || y)) || x;")]
    [InlineData("public bool Value(int64 x, int64 y) => (x + 3) < (y * 2) && x != y;")]
    [InlineData("public bool Value(int64 x) => (1 | 2) == x;")]
    [InlineData("public bool Value(uint64 x) => (1 ^ 2) == x;")]
    [InlineData("public bool Value(char16 x, char16 y) => (x == y) || (x != 42);")]
    [InlineData("public char16 Value(char16 x) => x;")]
    [InlineData("public int32 Value(int32 x, int32 y, int32 z) { if ((x + y) > (z * 2)) { return (x - z) + y; } return z + (x * 2); }")]
    [InlineData("public int32 Value(int32 x) => x + (-2147483648);")]
    [InlineData("public int64 Value(int64 x) => x + -9223372036854775808;")]
    public async Task TreeFrontendLibraryMatchesHostedBytesAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = Path.Combine(root, $"tree-frontend-{Guid.NewGuid():N}.native.dll");
        var hostedPath = Path.Combine(root, $"tree-frontend-{Guid.NewGuid():N}.hosted.dll");
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

    [Theory]
    [InlineData("int32 Main() => 2 + 3 * (4 + 4);", 26)]
    [InlineData("int32 Main() { return (2 + 3) * (4 + 4); }", 40)]
    public async Task TreeFrontendEntryMatchesHostedJitAsync(string source, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (nativeExit, nativeOutput, nativeError) = await RunAsync(input,
                "frontend-run", frontend, "-", "--runtime", policy, "--json");
            var (hostedExit, hostedOutput, hostedError) = await RunAsync(input,
                "jit", "-", "--runtime", policy, "--json");
            Assert.Equal(expected, nativeExit);
            Assert.Equal(hostedExit, nativeExit);
            Assert.Empty(nativeOutput);
            Assert.Empty(hostedOutput);
            Assert.Empty(nativeError);
            Assert.Empty(hostedError);
        }
    }

    [Theory]
    [InlineData("public int32 Value(int32 x) => (x + missing) * 2;", 3, "missing")]
    [InlineData("public int32 Value(int32 x) => x + (true * 2);", 2, "2")]
    [InlineData("public int32 Value(int32 x) => x + 2147483648;", 2, "2147483648")]
    [InlineData("public uint64 Value(uint64 x) => x + -1;", 2, "1")]
    [InlineData("public bool Value(int32 x) => (x < 5) && x;", 2, "&&")]
    [InlineData("public int32 Value(int32 x) => x + (2 * 3;", 1, ";")]
    [InlineData("public int32 Value(int32 x) => x + 2 * 3);", 1, ")")]
    [InlineData("public bool Value() => 1 < 2;", 2, "<")]
    [InlineData("public bool Value() => (1 + 2) < 4;", 2, "<")]
    [InlineData("public bool Value() => (1 & 2) == 3;", 2, "==")]
    [InlineData("public bool Value() => (1 | 2) == 3;", 2, "==")]
    [InlineData("public bool Value() => (1 ^ 2) == 3;", 2, "==")]
    public async Task TreeFrontendRejectsInvalidExpressionsAsync(
        string source, int code, string offending)
    {
        Assert.True(Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() => 42;")).HasErrors);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", frontend, "-", "--runtime", "fail-fast", "--json");
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
    public async Task TreeFrontendLimitsExpressionDepthAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = "int32 Main() => " + new string('(', 17) + "42"
            + new string(')', 17) + ";";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task TreeFrontendLimitsPostorderNodeCountAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var operands = new string[18];
        Array.Fill(operands, "x");
        var source = "public int32 Value(int32 x) => "
            + string.Join(" + ", operands) + ";";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32());
    }

    [Fact]
    public async Task TreeFrontendPublishesPostorderTypedFactsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = "public int32 Value(int32 x) => x + (x * 2);";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.Equal(20, result.GetProperty("projectionVersion").GetInt32());
        Assert.True(result.GetProperty("expressionBody").GetBoolean());
        Assert.Equal(-1, result.GetProperty("conditionRoot").GetInt32());
        var nodes = result.GetProperty("nodes");
        Assert.Equal(6, nodes.GetArrayLength());
        Assert.Equal(57, nodes[result.GetProperty("trueRoot").GetInt32()]
            .GetProperty("operation").GetInt32());
        for (var index = 0; index < nodes.GetArrayLength(); index++)
        {
            var node = nodes[index];
            var left = node.GetProperty("left").GetInt32();
            var right = node.GetProperty("right").GetInt32();
            if (node.GetProperty("operation").GetInt32() is 0 or 3)
            {
                continue;
            }
            Assert.InRange(left, 0, index - 1);
            if (right >= 0)
            {
                Assert.InRange(right, 0, index - 1);
            }
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task TreeFrontendFailureRetainsPreviousLibraryAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var output = Path.Combine(root, $"tree-frontend-{Guid.NewGuid():N}.dll");
        try
        {
            var (success, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) => x * (3 + 4);"),
                "frontend-build-library", frontend, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.Equal(0, success);
            var previous = await File.ReadAllBytesAsync(output);
            var (failure, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) => x * (3 + missing);"),
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
