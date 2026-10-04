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
    [InlineData("public float32 Literal() { return 0.1; }")]
    [InlineData("public float32 Convert(float64 value) { return float32(value); }")]
    public async Task ScalarFrontendRejectsFloatingLiteralsAndConversionsAsync(string source)
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
}
