// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendProjectsSameFormatFloatingOperatorsAsync(string policy)
    {
        const string Source = """
            public float32 Arithmetic(float32 left, float32 right) {
                return (left + right) * left / right - -left + +right;
            }
            public float64 Divide64(float64 left, float64 right) { return left / right; }
            public bool NotEqual64(float64 left, float64 right) { return left != right; }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var output = Path.Combine(Path.GetTempPath(), $"terrum-scalar-float-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(Path.GetTempPath(), $"terrum-hosted-float-{Guid.NewGuid():N}.dll");
        try
        {
            var (componentExit, componentJson, componentError) = await RunAsync(
                Encoding.UTF8.GetBytes(Source), "frontend-component", directory, "-",
                "--runtime", policy, "--json");
            Assert.True(componentExit == 0, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(85, result.GetProperty("projectionVersion").GetInt32());
                var functions = result.GetProperty("functions");
                Assert.Contains(functions[0].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 57
                        && node.GetProperty("type").GetInt32() == 7);
                Assert.Contains(functions[0].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 58
                        && node.GetProperty("right").GetInt32() == -1
                        && node.GetProperty("type").GetInt32() == 7);
                Assert.Contains(functions[0].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 57
                        && node.GetProperty("right").GetInt32() == -1
                        && node.GetProperty("type").GetInt32() == 7);
                Assert.Contains(functions[1].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 60
                        && node.GetProperty("type").GetInt32() == 8);
                Assert.Contains(functions[2].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 66
                        && node.GetProperty("type").GetInt32() == 5);
            }

            var (exit, json, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.True(exit == 0, $"{policy}: {json}{error}");
            Assert.Empty(error);
            using (var document = JsonDocument.Parse(json))
            {
                Assert.Equal(85, document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32());
            }
            var (hostedExit, hostedJson, hostedError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "build-library", "-", "--export", "Arithmetic", "--export", "Divide64",
                "--export", "NotEqual64", "-o", hosted, "--runtime", policy, "--json");
            Assert.True(hostedExit == 0, $"{policy}: {hostedJson}{hostedError}");
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }

    [Theory]
    [InlineData("public char16 Convert(float32 value) { return char16(value); }")]
    public async Task ScalarFrontendRejectsUnapprovedFloatingToChar16ConversionsAsync(string source)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(1, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        if (root.TryGetProperty("result", out var result))
        {
            Assert.False(result.GetProperty("valid").GetBoolean());
        }
        else
        {
            Assert.Contains(root.GetProperty("diagnostics").EnumerateArray(),
                static diagnostic => diagnostic.GetProperty("code").GetString() == "TRM5001");
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendRejectsNoThrowFloatingToIntegerConversionsAsync(string policy)
    {
        const string Source =
            "[NoThrow] public int32 Convert(float32 value) { return int32(value); }";
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var outputPath = Path.Combine(Path.GetTempPath(),
            $"terrum-scalar-no-throw-float-to-int-{Guid.NewGuid():N}.dll");
        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build-library", directory, "-", "-o", outputPath,
                "--runtime", policy, "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Theory]
    [InlineData("fail-fast", "public float32 Literal() => 0.1;", 7, 3)]
    [InlineData("checked-exceptions", "public float32 Literal() => 0.1;", 7, 3)]
    [InlineData("fail-fast", "public float32 Literal() { return 0.1; }", 7, 3)]
    [InlineData("checked-exceptions", "public float32 Literal() { return 0.1; }", 7, 3)]
    [InlineData("fail-fast", "public float32 Literal() { return -0.0; }", 7, 3)]
    [InlineData("checked-exceptions", "public float32 Literal() { return -0.0; }", 7, 3)]
    [InlineData("fail-fast", "public float32 Literal() { return +0.1; }", 7, 657)]
    [InlineData("checked-exceptions", "public float32 Literal() { return +0.1; }", 7, 657)]
    [InlineData("fail-fast", "public float32 Literal() { return 0b101; }", 7, 3)]
    [InlineData("checked-exceptions", "public float32 Literal() { return 0b101; }", 7, 3)]
    [InlineData("fail-fast", "public float64 Literal() { return 0x123; }", 8, 3)]
    [InlineData("checked-exceptions", "public float64 Literal() { return 0x123; }", 8, 3)]
    public async Task ScalarFrontendProjectsTargetTypedFloatingLiteralAsync(string policy, string source,
        int expectedType, int expectedOperation)
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var output = Path.Combine(Path.GetTempPath(), $"terrum-scalar-float-literal-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(Path.GetTempPath(), $"terrum-hosted-float-literal-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.True(componentExit == 0, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.True(result.GetProperty("valid").GetBoolean());
                if (result.TryGetProperty("projectionVersion", out var projectionVersion))
                {
                    Assert.Equal(85, projectionVersion.GetInt32());
                }
                Assert.Equal(expectedOperation,
                    result.GetProperty("whenTrue").GetProperty("operation").GetInt32());
                Assert.Equal(expectedType, result.GetProperty("whenTrue").GetProperty("type").GetInt32());
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.True(exit == 0, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Literal", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.True(hostedExit == 0, $"{policy}: {hostedJson}{hostedError}");
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingLiteralsIntoCompleteProgramsAsync(string policy)
    {
        const string Source = """
            public float32 Decimal32() { return 0.1; }
            public float32 NegativeZero32() { return -0.0; }
            public float64 Hex64() { return 0x1.fp+2; }
            public float32 AddLiteral32(float32 value) { return value + 0.1; }
            public bool CompareLiteral32(float32 value) { return 0.1 < value; }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var output = Path.Combine(Path.GetTempPath(), $"terrum-scalar-floats-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(Path.GetTempPath(), $"terrum-hosted-floats-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(Source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.True(componentExit == 0, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result")
                    .GetProperty("functions");
                Assert.Equal(5, functions.GetArrayLength());
                Assert.Contains(functions[0].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 3
                        && node.GetProperty("type").GetInt32() == 7);
                Assert.Contains(functions[1].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 58
                        && node.GetProperty("type").GetInt32() == 7);
                Assert.Contains(functions[2].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 3
                        && node.GetProperty("type").GetInt32() == 8);
                Assert.Contains(functions[3].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 57
                        && node.GetProperty("type").GetInt32() == 7);
                Assert.Contains(functions[4].GetProperty("nodes").EnumerateArray(),
                    static node => node.GetProperty("operation").GetInt32() == 67
                        && node.GetProperty("type").GetInt32() == 5);
                Assert.Equal(0x3DCCCCCDUL, ProjectedFloatLiteralBits(functions[0], 7));
                Assert.Equal(0UL, ProjectedFloatLiteralBits(functions[1], 7));
                Assert.Equal(0x401F000000000000UL, ProjectedFloatLiteralBits(functions[2], 8));
                Assert.Equal(0x3DCCCCCDUL, ProjectedFloatLiteralBits(functions[3], 7));
                Assert.Equal(0x3DCCCCCDUL, ProjectedFloatLiteralBits(functions[4], 7));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.True(exit == 0, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Decimal32", "--export", "NegativeZero32",
                "--export", "Hex64", "--export", "AddLiteral32", "--export",
                "CompareLiteral32", "-o", hosted, "--runtime", policy, "--json");
            Assert.True(hostedExit == 0, $"{policy}: {hostedJson}{hostedError}");
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(output));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendProjectsBasedAndDecimalFloatingBitsAsync(string policy)
    {
        const string Source = """
            public float64 Decimal64() { return 0.1; }
            public float32 Positive32() { return +0.1; }
            public float32 BasedBinary32() { return 0b101; }
            public float64 BasedHex64() { return 0x123; }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.True(exit == 0, $"{policy}: {output}{error}");
        using var document = JsonDocument.Parse(output);
        var functions = document.RootElement.GetProperty("result").GetProperty("functions");
        Assert.Equal(4, functions.GetArrayLength());
        Assert.Equal(0x3FB999999999999AUL, ProjectedFloatLiteralBits(functions[0], 8));
        Assert.Equal(0x3DCCCCCDUL, ProjectedFloatLiteralBits(functions[1], 7));
        Assert.Equal(0x40A00000UL, ProjectedFloatLiteralBits(functions[2], 7));
        Assert.Equal(0x4072300000000000UL, ProjectedFloatLiteralBits(functions[3], 8));
    }

    private static ulong ProjectedFloatLiteralBits(JsonElement function, int type)
    {
        JsonElement literal = default;
        var matches = 0;
        foreach (var node in function.GetProperty("nodes").EnumerateArray())
        {
            if (node.GetProperty("operation").GetInt32() == 3
                && node.GetProperty("type").GetInt32() == type)
            {
                literal = node;
                matches++;
            }
        }

        Assert.Equal(1, matches);
        return (ulong)(uint)literal.GetProperty("low").GetInt32()
            | ((ulong)(uint)literal.GetProperty("high").GetInt32() << 32);
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendExecutesChar16FloatingConversionsAsync(string policy)
    {
        const string Source = """
            int32 Main() { return
                (int32(float32(char16(55296))) == 55296
                    && int32(float64(char16(65535))) == 65535) ? 42 : 1;
            }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-run", directory, "-", "--runtime", policy);
        Assert.True(exit == 42, $"{policy}: {output}{error}");
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task ScalarFrontendExecutesFloatingIntegerConversionsAsync(string policy)
    {
        const string Source = """
            int32 Main() { return
                (int32(float32(1.9)) == 1 && uint32(float32(2.9)) == 2
                    && int64(float64(-3.9)) == -3
                    && uint64(float64(4.9)) == 4) ? 42 : 1;
            }
            """;
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-run", directory, "-", "--runtime", policy);
        Assert.True(exit == 42, $"{policy}: {output}{error}");
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("float32", "int32")]
    [InlineData("float32", "uint32")]
    [InlineData("float32", "int64")]
    [InlineData("float32", "uint64")]
    [InlineData("float64", "int32")]
    [InlineData("float64", "uint32")]
    [InlineData("float64", "int64")]
    [InlineData("float64", "uint64")]
    public async Task ScalarFrontendFailFastFloatingToIntegerUsesOverflowStatusAsync(
        string sourceType, string targetType)
    {
        var source = Encoding.UTF8.GetBytes(
            $"int32 Main() {{ return int32({targetType}({sourceType}(1.0 / 0.0))); }}");
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var (exit, output, error) = await RunAsync(source, "frontend-run", directory, "-",
            "--runtime", "fail-fast");
        Assert.Equal(unchecked((int)0xE0540001), exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task ScalarFrontendFloatingConversionsMatchHostedLibrariesAsync()
    {
        var directory = Path.Combine(RepositoryRoot(), "self-host");
        var outputDirectory = Path.Combine(Path.GetTempPath(),
            $"terrum-scalar-float-conversions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                foreach (var sourceType in new[] {
                    "int32", "uint32", "int64", "uint64", "char16", "float32", "float64",
                })
                {
                    string[] targetTypes = sourceType is "float32" or "float64"
                        ? ["int32", "uint32", "int64", "uint64", "float32", "float64"]
                        : ["float32", "float64"];
                    foreach (var targetType in targetTypes)
                    {
                        var noThrow = sourceType == "char16" ? "[NoThrow] " : string.Empty;
                        var source = Encoding.UTF8.GetBytes(
                            $"{noThrow}public {targetType} Convert({sourceType} value) => {targetType}(value);");
                        var output = Path.Combine(outputDirectory, "scalar.dll");
                        var hosted = Path.Combine(outputDirectory, "hosted.dll");
                        var (componentExit, componentJson, componentError) = await RunAsync(source,
                            "frontend-component", directory, "-", "--runtime", policy, "--json");
                        Assert.True(componentExit == 0, $"{policy}: {componentJson}{componentError}");
                        var (exit, json, error) = await RunAsync(source,
                            "frontend-build-library", directory, "-", "-o", output,
                            "--runtime", policy, "--json");
                        Assert.True(exit == 0, $"{policy}: {json}{error}");
                        var (hostedExit, hostedJson, hostedError) = await RunAsync(source,
                            "build-library", "-", "--export", "Convert", "-o", hosted,
                            "--runtime", policy, "--json");
                        Assert.True(hostedExit == 0, $"{policy}: {hostedJson}{hostedError}");
                        Assert.Equal(await File.ReadAllBytesAsync(hosted),
                            await File.ReadAllBytesAsync(output));
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
