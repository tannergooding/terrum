// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("public int32 Value(int32 x, int32 y) { return x + y + x; }")]
    [TestCase("public int32 Value(int32 x) => +(+(+(+(+(+(+(+x))))))) + x + x + x + x + x + x;")]
    [TestCase("public int32 Value(int32 x, int32 y) => x + (y * 3) - (x / 2);")]
    [TestCase("public int64 Value(int64 x) => -(x + (10 * (x - 3)));")]
    [TestCase("public uint32 Value(uint32 x) => (x & 7) | ((x ^ 3) + 1);")]
    [TestCase("public uint64 Value(uint64 x) => (x ^ 0xFFFFFFFFFFFFFFFF) & (x | 4);")]
    [TestCase("public bool Value(bool x, bool y) => !(x && (false || y)) || x;")]
    [TestCase("public bool Value(int64 x, int64 y) => (x + 3) < (y * 2) && x != y;")]
    [TestCase("public bool Value(int64 x) => (1 | 2) == x;")]
    [TestCase("public bool Value(uint64 x) => (1 ^ 2) == x;")]
    [TestCase("public bool Value(char16 x, char16 y) => (x == y) || (x != 42);")]
    [TestCase("public char16 Value(char16 x) => x;")]
    [TestCase("public int32 Value(int32 x, int32 y, int32 z) { if ((x + y) > (z * 2)) { return (x - z) + y; } return z + (x * 2); }")]
    [TestCase("public int32 Value(out int32 result, int32 input) { if (input == 0) { result = 42; return 0; } return Value(input: 0, result: out result); }")]
    [TestCase("public int32 Value(init ref<int32> result, int32 input) { if (input == 0) { result = 42; return 0; } return Value(input: 0, result: init result); }")]
    [TestCase("public int32 Value(out int32 result, int32 input) { if (input == 0) { result = 42; return 0; } int32 status = Value(input: 0, result: out var nested); result = nested; return status; }")]
    [TestCase("public int32 Value(init ref<int32> result, int32 input) { if (input == 0) { result = 42; return 0; } int32 status = Value(input: 0, result: init var nested); result = nested; return status; }")]
    [TestCase("public int32 Value(int32 x) => x + (-2147483648);")]
    [TestCase("public int64 Value(int64 x) => x + -9223372036854775808;")]
    public async Task TreeFrontendLibraryMatchesHostedBytesAsync(string source)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var nativePath = CreateTestArtifactPath($"tree-frontend-{Guid.NewGuid():N}.native.dll");
        var hostedPath = CreateTestArtifactPath($"tree-frontend-{Guid.NewGuid():N}.hosted.dll");
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
    [TestCase("int32 Main() => 2 + 3 * (4 + 4);",26)]
    [TestCase("int32 Main() { return (2 + 3) * (4 + 4); }",40)]
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
            Assert.That(nativeExit, Is.EqualTo(expected));
            Assert.That(nativeExit, Is.EqualTo(hostedExit));
            Assert.That(nativeOutput, Is.Empty);
            Assert.That(hostedOutput, Is.Empty);
            Assert.That(nativeError, Is.Empty);
            Assert.That(hostedError, Is.Empty);
        }
    }
    [TestCase("public int32 Value(int32 x) => (x + missing) * 2;",3,"missing")]
    [TestCase("public int32 Value(int32 x) => x + (true * 2);",2,"2")]
    [TestCase("public int32 Value(int32 x) => x + 2147483648;",2,"2147483648")]
    [TestCase("public uint64 Value(uint64 x) => x + -1;",2,"1")]
    [TestCase("public bool Value(int32 x) => (x < 5) && x;",2,"&&")]
    [TestCase("public int32 Value(int32 x) => x + (2 * 3;",1,";")]
    [TestCase("public int32 Value(int32 x) => x + 2 * 3);",1,")")]
    [TestCase("public bool Value() => 1 < 2;",2,"<")]
    [TestCase("public bool Value() => (1 + 2) < 4;",2,"<")]
    [TestCase("public bool Value() => (1 & 2) == 3;",2,"==")]
    [TestCase("public bool Value() => (1 | 2) == 3;",2,"==")]
    [TestCase("public bool Value() => (1 ^ 2) == 3;",2,"==")]
    public async Task TreeFrontendRejectsInvalidExpressionsAsync(
        string source, int code, string offending)
    {
        Assert.That(Compilation.Create(SyntaxTree.Parse(source
            + " int32 Main() => 42;")).HasErrors, Is.True);
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", frontend, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        Assert.That(result.GetProperty("errorCode").GetInt32(), Is.EqualTo(code));
        Assert.That(result.GetProperty("errorSpan").GetProperty("start").GetInt32(), Is.EqualTo(source.LastIndexOf(offending, StringComparison.Ordinal)));
    }

    [Test]
    public async Task TreeFrontendLimitsExpressionDepthAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = "int32 Main() => " + new string('(', 17) + "42"
            + new string(')', 17) + ";";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task TreeFrontendLimitsPostorderNodeCountAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var operands = new string[34];
        Array.Fill(operands, "x");
        var source = "public int32 Value(int32 x) => "
            + string.Join(" + ", operands) + ";";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        Assert.That(document.RootElement.GetProperty("result")
            .GetProperty("errorCode").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task TreeFrontendPublishesPostorderTypedFactsAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = "public int32 Value(int32 x) => x + (x * 2);";
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", Path.Combine(root, "self-host"), "-",
            "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(0));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
        Assert.That(result.GetProperty("expressionBody").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("conditionRoot").GetInt32(), Is.EqualTo(-1));
        var nodes = result.GetProperty("nodes");
        Assert.That(nodes.GetArrayLength(), Is.EqualTo(6));
        Assert.That(nodes[result.GetProperty("trueRoot").GetInt32()]
            .GetProperty("operation").GetInt32(), Is.EqualTo(57));
        for (var index = 0; index < nodes.GetArrayLength(); index++)
        {
            var node = nodes[index];
            var left = node.GetProperty("left").GetInt32();
            var right = node.GetProperty("right").GetInt32();
            if (node.GetProperty("operation").GetInt32() is 0 or 3)
            {
                continue;
            }
            Assert.That(left, Is.InRange(0, index - 1));
            if (right >= 0)
            {
                Assert.That(right, Is.InRange(0, index - 1));
            }
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task TreeFrontendFailureRetainsPreviousLibraryAsync(string policy)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var frontend = Path.Combine(root, "self-host");
        var output = CreateTestArtifactPath($"tree-frontend-{Guid.NewGuid():N}.dll");
        try
        {
            var (success, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) => x * (3 + 4);"),
                "frontend-build-library", frontend, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(success, Is.EqualTo(0));
            var previous = await File.ReadAllBytesAsync(output);
            var (failure, _, _) = await RunAsync(Encoding.UTF8.GetBytes(
                "public int32 Value(int32 x) => x * (3 + missing);"),
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
