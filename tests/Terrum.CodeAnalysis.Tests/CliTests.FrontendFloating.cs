// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendProjectsSameFormatFloatingOperatorsAsync(string policy)
    {
        const string Source = """
            public float32 Arithmetic(float32 left, float32 right) {
                return (left + right) * left / right - -left + +right;
            }
            public float64 Divide64(float64 left, float64 right) { return left / right; }
            public bool NotEqual64(float64 left, float64 right) { return left != right; }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-float-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-float-{Guid.NewGuid():N}.dll");
        try
        {
            var (componentExit, componentJson, componentError) = await RunAsync(
                Encoding.UTF8.GetBytes(Source), "frontend-component", directory, "-",
                "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
                var functions = result.GetProperty("functions");
                Assert.That(global::System.Linq.Enumerable.Any(functions[0].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 57
                        && node.GetProperty("type").GetInt32() == 7),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[0].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 58
                        && node.GetProperty("right").GetInt32() == -1
                        && node.GetProperty("type").GetInt32() == 7),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[0].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 57
                        && node.GetProperty("right").GetInt32() == -1
                        && node.GetProperty("type").GetInt32() == 7),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[1].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 60
                        && node.GetProperty("type").GetInt32() == 8),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[2].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 66
                        && node.GetProperty("type").GetInt32() == 5),Is.True);
            }

            var (exit, json, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            Assert.That(error, Is.Empty);
            using (var document = JsonDocument.Parse(json))
            {
                Assert.That(document.RootElement.GetProperty("result")
                    .GetProperty("projectionVersion").GetInt32(), Is.EqualTo(88));
            }
            var (hostedExit, hostedJson, hostedError) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "build-library", "-", "--export", "Arithmetic", "--export", "Divide64",
                "--export", "NotEqual64", "-o", hosted, "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("public char16 Convert(float32 value) { return char16(value); }")]
    public async Task ScalarFrontendRejectsUnapprovedFloatingToChar16ConversionsAsync(string source)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
            "frontend-component", directory, "-", "--runtime", "fail-fast", "--json");
        Assert.That(exit, Is.EqualTo(1));
        Assert.That(error, Is.Empty);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        Assert.That(root.GetProperty("success").GetBoolean(), Is.False);
        if (root.TryGetProperty("result", out var result))
        {
            Assert.That(result.GetProperty("valid").GetBoolean(), Is.False);
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(root.GetProperty("diagnostics").EnumerateArray(),static diagnostic => diagnostic.GetProperty("code").GetString() == "TRM5001"),Is.True);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendRejectsNoThrowFloatingToIntegerConversionsAsync(string policy)
    {
        const string Source =
            "[NoThrow] public int32 Convert(float32 value) { return int32(value); }";
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var outputPath = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-scalar-no-throw-float-to-int-{Guid.NewGuid():N}.dll");
        try
        {
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
                "frontend-build-library", directory, "-", "-o", outputPath,
                "--runtime", policy, "--json");
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error, Is.Empty);
            using var document = JsonDocument.Parse(output);
            Assert.That(document.RootElement.GetProperty("success").GetBoolean(), Is.False);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
    [TestCase("fail-fast","public float32 Literal() => 0.1;",7,3)]
    [TestCase("checked-exceptions","public float32 Literal() => 0.1;",7,3)]
    [TestCase("fail-fast","public float32 Literal() { return 0.1; }",7,3)]
    [TestCase("checked-exceptions","public float32 Literal() { return 0.1; }",7,3)]
    [TestCase("fail-fast","public float32 Literal() { return -0.0; }",7,3)]
    [TestCase("checked-exceptions","public float32 Literal() { return -0.0; }",7,3)]
    [TestCase("fail-fast","public float32 Literal() { return +0.1; }",7,657)]
    [TestCase("checked-exceptions","public float32 Literal() { return +0.1; }",7,657)]
    [TestCase("fail-fast","public float32 Literal() { return 0b101; }",7,3)]
    [TestCase("checked-exceptions","public float32 Literal() { return 0b101; }",7,3)]
    [TestCase("fail-fast","public float64 Literal() { return 0x123; }",8,3)]
    [TestCase("checked-exceptions","public float64 Literal() { return 0x123; }",8,3)]
    public async Task ScalarFrontendProjectsTargetTypedFloatingLiteralAsync(string policy, string source,
        int expectedType, int expectedOperation)
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-float-literal-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-float-literal-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("valid").GetBoolean(), Is.True);
                if (result.TryGetProperty("projectionVersion", out var projectionVersion))
                {
                    Assert.That(projectionVersion.GetInt32(), Is.EqualTo(87));
                }
                Assert.That(result.GetProperty("whenTrue").GetProperty("operation").GetInt32(), Is.EqualTo(expectedOperation));
                Assert.That(result.GetProperty("whenTrue").GetProperty("type").GetInt32(), Is.EqualTo(expectedType));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Literal", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingLiteralsIntoCompleteProgramsAsync(string policy)
    {
        const string Source = """
            public float32 Decimal32() { return 0.1; }
            public float32 NegativeZero32() { return -0.0; }
            public float64 Hex64() { return 0x1.fp+2; }
            public float32 AddLiteral32(float32 value) { return value + 0.1; }
            public bool CompareLiteral32(float32 value) { return 0.1 < value; }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-floats-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-floats-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(Source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result")
                    .GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(5));
                Assert.That(global::System.Linq.Enumerable.Any(functions[0].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 3
                        && node.GetProperty("type").GetInt32() == 7),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[1].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 58
                        && node.GetProperty("type").GetInt32() == 7),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[2].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 3
                        && node.GetProperty("type").GetInt32() == 8),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[3].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 57
                        && node.GetProperty("type").GetInt32() == 7),Is.True);
                Assert.That(global::System.Linq.Enumerable.Any(functions[4].GetProperty("nodes").EnumerateArray(),static node => node.GetProperty("operation").GetInt32() == 67
                        && node.GetProperty("type").GetInt32() == 5),Is.True);
                Assert.That(ProjectedFloatLiteralBits(functions[0], 7), Is.EqualTo(0x3DCCCCCDUL));
                Assert.That(ProjectedFloatLiteralBits(functions[1], 7), Is.EqualTo(0UL));
                Assert.That(ProjectedFloatLiteralBits(functions[2], 8), Is.EqualTo(0x401F000000000000UL));
                Assert.That(ProjectedFloatLiteralBits(functions[3], 7), Is.EqualTo(0x3DCCCCCDUL));
                Assert.That(ProjectedFloatLiteralBits(functions[4], 7), Is.EqualTo(0x3DCCCCCDUL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Decimal32", "--export", "NegativeZero32",
                "--export", "Hex64", "--export", "AddLiteral32", "--export",
                "CompareLiteral32", "-o", hosted, "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendProjectsBasedAndDecimalFloatingBitsAsync(string policy)
    {
        const string Source = """
            public float64 Decimal64() { return 0.1; }
            public float32 Positive32() { return +0.1; }
            public float32 BasedBinary32() { return 0b101; }
            public float64 BasedHex64() { return 0x123; }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-component", directory, "-", "--runtime", policy, "--json");
        Assert.That(exit == 0, Is.True, $"{policy}: {output}{error}");
        using var document = JsonDocument.Parse(output);
        var functions = document.RootElement.GetProperty("result").GetProperty("functions");
        Assert.That(functions.GetArrayLength(), Is.EqualTo(4));
        Assert.That(ProjectedFloatLiteralBits(functions[0], 8), Is.EqualTo(0x3FB999999999999AUL));
        Assert.That(ProjectedFloatLiteralBits(functions[1], 7), Is.EqualTo(0x3DCCCCCDUL));
        Assert.That(ProjectedFloatLiteralBits(functions[2], 7), Is.EqualTo(0x40A00000UL));
        Assert.That(ProjectedFloatLiteralBits(functions[3], 8), Is.EqualTo(0x4072300000000000UL));
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendDispatchesFloatingLiteralCaseVariantsAsync(string policy)
    {
        const string Source = """
            public float32 UpperHexFloat() { return 0X1.8P+1; }
            public float64 UpperBinary() { return 0B1_01; }
            public float64 UpperHexInteger() { return 0X123; }
            public float32 DecimalExponent() { return 1.25E-2; }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-float-cases-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-float-cases-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(Source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result").GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(4));
                Assert.That(ProjectedFloatLiteralBits(functions[0], 7), Is.EqualTo(0x40400000UL));
                Assert.That(ProjectedFloatLiteralBits(functions[1], 8), Is.EqualTo(0x4014000000000000UL));
                Assert.That(ProjectedFloatLiteralBits(functions[2], 8), Is.EqualTo(0x4072300000000000UL));
                Assert.That(ProjectedFloatLiteralBits(functions[3], 7), Is.EqualTo(0x3C4CCCCDUL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "UpperHexFloat", "--export",
                "UpperBinary", "--export", "UpperHexInteger", "--export",
                "DecimalExponent", "-o", hosted, "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingPayloadsInCallsAndLocalsAsync(string policy)
    {
        const string Source = """
            float64 Echo(float64 value) { return value; }
            public float64 Call64() { return Echo(0.1); }
            public float32 Local32() { float32 value = 0.1; return value; }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-float-contexts-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-float-contexts-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(Source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result").GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(3));
                Assert.That(ProjectedFloatLiteralBits(functions[1], 8), Is.EqualTo(0x3FB999999999999AUL));
                Assert.That(ProjectedFloatLiteralBits(functions[2], 7), Is.EqualTo(0x3DCCCCCDUL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Call64", "--export", "Local32",
                "-o", hosted, "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingPayloadsThroughOutCallsAsync(string policy)
    {
        const string Source = """
            bool TryGet(out float64 value) { value = 0.1; return true; }
            public float64 Read() {
                if (TryGet(out var value)) { return value; }
                return 0.0;
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-float-out-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-float-out-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(Source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result").GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                Assert.That(ProjectedFloatLiteralBits(functions[0], 8), Is.EqualTo(0x3FB999999999999AUL));
                Assert.That(ProjectedFloatLiteralBits(functions[1], 8), Is.EqualTo(0UL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Read", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("float32","3.5",7,0x40600000UL,"fail-fast")]
    [TestCase("float32","3.5",7,0x40600000UL,"checked-exceptions")]
    [TestCase("float64","0x1.fp+2",8,0x401F000000000000UL,"fail-fast")]
    [TestCase("float64","0x1.fp+2",8,0x401F000000000000UL,"checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingPayloadsThroughInitRefCallsAsync(
        string floatingType, string literal, int type, ulong expectedBits, string policy)
    {
        var source = $$"""
            bool TryGet(init ref<{{floatingType}}> value) { value = {{literal}}; return true; }
            public {{floatingType}} Read() {
                init {{floatingType}} value;
                if (TryGet(init value)) { return value; }
                return 0.0;
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-{floatingType}-init-ref-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-{floatingType}-init-ref-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result").GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                Assert.That(ProjectedFloatLiteralBits(functions[0], type), Is.EqualTo(expectedBits));
                Assert.That(ProjectedFloatLiteralBits(functions[1], type), Is.EqualTo(0UL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Read", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("float32",7,"fail-fast")]
    [TestCase("float32",7,"checked-exceptions")]
    [TestCase("float64",8,"fail-fast")]
    [TestCase("float64",8,"checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingPayloadsThroughNonBooleanOutCallsAsync(
        string floatingType, int type, string policy)
    {
        var source = $$"""
            {{floatingType}} Get(out {{floatingType}} value) {
                value = 7.75;
                return value;
            }
            public {{floatingType}} Read() {
                return Get(out var value) + value;
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-{floatingType}-out-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-{floatingType}-out-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result").GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                Assert.That(ProjectedFloatLiteralBits(functions[0], type), Is.EqualTo(type == 7 ? 0x40F80000UL : 0x401F000000000000UL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Read", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("float32",7,"fail-fast")]
    [TestCase("float32",7,"checked-exceptions")]
    [TestCase("float64",8,"fail-fast")]
    [TestCase("float64",8,"checked-exceptions")]
    public async Task ScalarFrontendProjectsFloatingPayloadsThroughVoidOutCallsAsync(
        string floatingType, int type, string policy)
    {
        var source = $$"""
            void Fill(out {{floatingType}} value) { value = 7.75; }
            public {{floatingType}} Read() {
                mut {{floatingType}} value = 0.0;
                Fill(out value);
                return value;
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-{floatingType}-void-out-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-{floatingType}-void-out-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var functions = document.RootElement.GetProperty("result").GetProperty("functions");
                Assert.That(functions.GetArrayLength(), Is.EqualTo(2));
                Assert.That(ProjectedFloatLiteralBits(functions[0], type), Is.EqualTo(type == 7 ? 0x40F80000UL : 0x401F000000000000UL));
            }

            var (exit, json, error) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Read", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("float32","3.5","fail-fast")]
    [TestCase("float32","3.5","checked-exceptions")]
    [TestCase("float64","0x1.fp+2","fail-fast")]
    [TestCase("float64","0x1.fp+2","checked-exceptions")]
    public async Task ScalarFrontendSingleFunctionProjectsFloatingOutAsync(
        string floatingType, string literal, string policy)
    {
        var source = $$"""
            public {{floatingType}} Fill({{floatingType}} seed, out {{floatingType}} value) {
                if (seed == 0.0) { value = {{literal}}; return seed; }
                return Fill(seed - 1.0, out value);
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-single-{floatingType}-out-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-single-{floatingType}-out-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("outParameterMask").GetInt32(), Is.EqualTo(2));
                Assert.That(result.GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(0));
            }

            var (buildExit, buildJson, buildError) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(buildExit == 0, Is.True, $"{policy}: {buildJson}{buildError}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Fill", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
    }
    [TestCase("float32","3.5","fail-fast")]
    [TestCase("float32","3.5","checked-exceptions")]
    [TestCase("float64","0x1.fp+2","fail-fast")]
    [TestCase("float64","0x1.fp+2","checked-exceptions")]
    public async Task ScalarFrontendSingleFunctionProjectsFloatingInitRefAsync(
        string floatingType, string literal, string policy)
    {
        var source = $$"""
            public bool Fill({{floatingType}} seed, init ref<{{floatingType}}> value) {
                if (seed == 0.0) { value = {{literal}}; return true; }
                return Fill(seed - 1.0, init value);
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var output = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-scalar-single-{floatingType}-init-ref-{Guid.NewGuid():N}.dll");
        var hosted = Path.Combine(TestArtifactPaths.DirectoryPath, $"terrum-hosted-single-{floatingType}-init-ref-{Guid.NewGuid():N}.dll");
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (componentExit, componentJson, componentError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
            using (var document = JsonDocument.Parse(componentJson))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(87));
                Assert.That(result.GetProperty("outParameterMask").GetInt32(), Is.EqualTo(0));
                Assert.That(result.GetProperty("initRefParameterMask").GetInt32(), Is.EqualTo(2));
            }

            var (buildExit, buildJson, buildError) = await RunAsync(input,
                "frontend-build-library", directory, "-", "-o", output,
                "--runtime", policy, "--json");
            Assert.That(buildExit == 0, Is.True, $"{policy}: {buildJson}{buildError}");
            var (hostedExit, hostedJson, hostedError) = await RunAsync(input,
                "build-library", "-", "--export", "Fill", "-o", hosted,
                "--runtime", policy, "--json");
            Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
            Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(output);
            File.Delete(hosted);
        }
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

        Assert.That(matches, Is.EqualTo(1));
        return (ulong)unchecked((uint)literal.GetProperty("low").GetInt32())
            | ((ulong)unchecked((uint)literal.GetProperty("high").GetInt32()) << 32);
    }

    private static void AssertProjectedChar16FloatLiterals(
        string json, string source, int projectionVersion)
    {
        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("result");
        Assert.That(result.GetProperty("projectionVersion").GetInt32(), Is.EqualTo(projectionVersion));
        var expressions = new (string Text, int Type, int CodeUnit)[] {
            ("float32(char16(0))", 7, 0),
            ("float32(char16(55296))", 7, 55296),
            ("float32(char16(0xFFFF))", 7, 65535),
            ("float64(char16(0))", 8, 0),
            ("float64(char16(55296))", 8, 55296),
            ("float64(char16(0xFFFF))", 8, 65535),
        };
        var matches = new int[expressions.Length];

        void Inspect(JsonElement owner)
        {
            foreach (var node in owner.GetProperty("nodes").EnumerateArray())
            {
                if (node.GetProperty("operation").GetInt32() != 3)
                {
                    continue;
                }
                var span = node.GetProperty("span");
                var start = span.GetProperty("start").GetInt32();
                var length = span.GetProperty("length").GetInt32();
                var text = source.Substring(start, length);
                for (var index = 0; index < expressions.Length; index++)
                {
                    var expected = expressions[index];
                    if (text != expected.Text)
                    {
                        continue;
                    }
                    Assert.That(node.GetProperty("type").GetInt32(), Is.EqualTo(expected.Type));
                    var bits = expected.Type == 7
                        ? BitConverter.SingleToUInt32Bits((float)expected.CodeUnit)
                        : BitConverter.DoubleToUInt64Bits((double)expected.CodeUnit);
                    Assert.That(node.GetProperty("low").GetInt32(), Is.EqualTo(unchecked((int)(uint)bits)));
                    Assert.That(node.GetProperty("high").GetInt32(), Is.EqualTo(expected.Type == 8 ? unchecked((int)(uint)(bits >> 32)) : 0));
                    matches[index]++;
                }
            }
        }

        if (result.TryGetProperty("functions", out var functions))
        {
            foreach (var function in functions.EnumerateArray())
            {
                Inspect(function);
            }
        }
        else
        {
            Inspect(result);
        }

        {var __nunitItems0 = matches;
        Assert.Multiple(() => { foreach (var __nunitItem0 in __nunitItems0) { Assert.That(__nunitItem0, Is.EqualTo(1)); } });}
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendExecutesChar16FloatingConversionsAsync(string policy)
    {
        const string StandaloneSource = """
            int32 Main() { return
                (float32(char16(0)) == 0
                    && float32(char16(55296)) == 55296
                    && float32(char16(0xFFFF)) == 65535
                    && float64(char16(0)) == 0
                    && float64(char16(55296)) == 55296
                    && float64(char16(0xFFFF)) == 65535) ? 42 : 1;
            }
            """;
        const string ProgramSource = """
            int32 Main() {
                return Verify() == 42 && VerifyCalls() == 42 ? 42 : 1;
            }
            float32 Echo32(float32 value) => value;
            float64 Echo64(float64 value) => value;
            int32 Verify() { return
                (float32(char16(0)) == 0
                    && float32(char16(55296)) == 55296
                    && float32(char16(0xFFFF)) == 65535
                    && float64(char16(0)) == 0
                    && float64(char16(55296)) == 55296
                    && float64(char16(0xFFFF)) == 65535) ? 42 : 1;
            }
            int32 VerifyCalls() { return
                (Echo32(float32(char16(1))) == 1
                    && Echo64(float64(char16(2))) == 2) ? 42 : 1;
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        foreach (var source in new[] { StandaloneSource, ProgramSource })
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (projectionExit, projectionJson, projectionError) = await RunAsync(input,
                "frontend-component", directory, "-", "--runtime", policy, "--json");
            Assert.That(projectionExit == 0, Is.True, $"{policy}: {projectionJson}{projectionError}");
            AssertProjectedChar16FloatLiterals(projectionJson, source,
                source == StandaloneSource ? 87 : 88);

            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source),
                "frontend-run", directory, "-", "--runtime", policy);
            Assert.That(exit == 42, Is.True, $"{policy}: {output}{error}");
            Assert.That(error, Is.Empty);
        }
    }
    [TestCase("fail-fast")]
    [TestCase("checked-exceptions")]
    public async Task ScalarFrontendExecutesFloatingIntegerConversionsAsync(string policy)
    {
        const string Source = """
            int32 Main() { return
                (int32(float32(1.9)) == 1 && uint32(float32(2.9)) == 2
                    && int64(float64(-3.9)) == -3
                    && uint64(float64(4.9)) == 4) ? 42 : 1;
            }
            """;
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "frontend-run", directory, "-", "--runtime", policy);
        Assert.That(exit == 42, Is.True, $"{policy}: {output}{error}");
        Assert.That(error, Is.Empty);
    }
    [TestCase("float32","int32")]
    [TestCase("float32","uint32")]
    [TestCase("float32","int64")]
    [TestCase("float32","uint64")]
    [TestCase("float64","int32")]
    [TestCase("float64","uint32")]
    [TestCase("float64","int64")]
    [TestCase("float64","uint64")]
    public async Task ScalarFrontendFailFastFloatingToIntegerUsesOverflowStatusAsync(
        string sourceType, string targetType)
    {
        var source = Encoding.UTF8.GetBytes(
            $"int32 Main() {{ return int32({targetType}({sourceType}(1.0 / 0.0))); }}");
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var (exit, output, error) = await RunAsync(source, "frontend-run", directory, "-",
            "--runtime", "fail-fast");
        Assert.That(exit, Is.EqualTo(unchecked((int)0xE0540001)));
        Assert.That(output, Is.Empty);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public async Task ScalarFrontendFloatingConversionsMatchHostedLibrariesAsync()
    {
        var directory = SelfHostPaths.SourceDirectory(RepositoryRoot());
        var outputDirectory = Path.Combine(TestArtifactPaths.DirectoryPath,
            $"terrum-scalar-float-conversions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var sourceTypes = new[] {
                "int32", "uint32", "int64", "uint64", "char16", "float32", "float64",
            };
            var conversions = sourceTypes.SelectMany(sourceType =>
            {
                string[] targetTypes = sourceType is "float32" or "float64"
                    ? ["int32", "uint32", "int64", "uint64", "float32", "float64"]
                    : ["float32", "float64"];
                return targetTypes.Select(targetType => (
                    SourceType: sourceType,
                    TargetType: targetType,
                    Name: $"Convert_{sourceType}_to_{targetType}"));
            }).ToArray();
            var source = Encoding.UTF8.GetBytes(string.Join(" ", conversions.Select(conversion =>
            {
                var noThrow = conversion.SourceType == "char16" ? "[NoThrow] " : string.Empty;
                return $"{noThrow}public {conversion.TargetType} {conversion.Name}"
                    + $"({conversion.SourceType} value) => {conversion.TargetType}(value);";
            })));
            foreach (var policy in new[] { "fail-fast", "checked-exceptions" })
            {
                var output = Path.Combine(outputDirectory, "scalar.dll");
                var hosted = Path.Combine(outputDirectory, "hosted.dll");
                var (componentExit, componentJson, componentError) = await RunAsync(source,
                    "frontend-component", directory, "-", "--runtime", policy, "--json");
                Assert.That(componentExit == 0, Is.True, $"{policy}: {componentJson}{componentError}");
                var (exit, json, error) = await RunAsync(source,
                    "frontend-build-library", directory, "-", "-o", output,
                    "--runtime", policy, "--json");
                Assert.That(exit == 0, Is.True, $"{policy}: {json}{error}");
                var hostedArguments = new List<string> { "build-library", "-" };
                foreach (var conversion in conversions)
                {
                    hostedArguments.Add("--export");
                    hostedArguments.Add(conversion.Name);
                }
                hostedArguments.Add("-o");
                hostedArguments.Add(hosted);
                hostedArguments.Add("--runtime");
                hostedArguments.Add(policy);
                hostedArguments.Add("--json");
                var (hostedExit, hostedJson, hostedError) = await RunAsync(source, [.. hostedArguments]);
                Assert.That(hostedExit == 0, Is.True, $"{policy}: {hostedJson}{hostedError}");
                Assert.That(await File.ReadAllBytesAsync(output), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
            }
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
