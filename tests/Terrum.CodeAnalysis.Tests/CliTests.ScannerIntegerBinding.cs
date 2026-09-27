// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("int32 Main() { return 42; }")]
    [InlineData("int32 Main() { int32 value = 2147483647; return value - 2147483605; }")]
    [InlineData("int32 Main() { int64 value = int64(-9223372036854775808); if (value == int64.MinValue) { return 42; } return 0; }")]
    [InlineData("int32 Main() { int64 value = -9223372036854775808; if (value == int64.MinValue) { return 42; } return 0; }")]
    [InlineData("int32 Main() { int32 value = -2147483648; if (value == int32.MinValue) { return 42; } return 0; }")]
    [InlineData("int32 Main() { uint64 value = 18446744073709551615; return int32(value % 1000); }")]
    [InlineData("int32 Main() { char16 value = 65535; return int32(value) - 65493; }")]
    [InlineData("int32 Main() { uint32 a = 4294967295; int64 b = 42; return int32(uint64(a) % 1000) - 295 + int32(b); }")]
    public async Task CompiledIntegerValuesEmitTheSameImageAsHostBuildAsync(string source)
    {
        var scanner = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host", "Scanner.terrum");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-decoded-integer-{Guid.NewGuid():N}");
        var ordinary = prefix + ".exe";
        var compiled = prefix + ".compiled.exe";
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (hostExit, hostOutput, hostError) = await RunAsync(input, "build", "-", "-o",
                ordinary, "--runtime", "fail-fast", "--json");
            var (componentExit, componentOutput, componentError) = await RunAsync(input, "build-component",
                scanner, "-", "-o", compiled, "--runtime", "fail-fast", "--json");
            Assert.Equal(0, hostExit);
            Assert.Equal(0, componentExit);
            Assert.Empty(hostError);
            Assert.Empty(componentError);
            using var host = JsonDocument.Parse(hostOutput);
            using var component = JsonDocument.Parse(componentOutput);
            Assert.Equal(host.RootElement.GetProperty("contentId").GetString(),
                component.RootElement.GetProperty("contentId").GetString());
            Assert.Equal(await File.ReadAllBytesAsync(ordinary), await File.ReadAllBytesAsync(compiled));
        }
        finally
        {
            File.Delete(ordinary);
            File.Delete(compiled);
        }
    }

    [Theory]
    [InlineData("int32 Main() { return uint32(4294967296); }")]
    [InlineData("int32 Main() { char16 value = 65536; return int32(value); }")]
    [InlineData("int32 Main() { uint64 value = 18446744073709551616; return 42; }")]
    [InlineData("int32 Main() { int64 value = -9223372036854775809; return 42; }")]
    [InlineData("int32 Main() { return 18446744073709551616; }")]
    [InlineData("int32 Main() { return 1i128; }")]
    [InlineData("int32 Main() { return 1.5; }")]
    [InlineData("int32 Main() { return 1e+; }")]
    public async Task CompiledIntegerFailuresRetainHostDiagnosticCodesAndSpansAsync(string source)
    {
        var scanner = Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..")), "self-host", "Scanner.terrum");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-decoded-error-{Guid.NewGuid():N}");
        var ordinary = prefix + ".exe";
        var compiled = prefix + ".compiled.exe";
        try
        {
            var input = Encoding.UTF8.GetBytes(source);
            var (hostExit, hostOutput, hostError) = await RunAsync(input, "build", "-", "-o",
                ordinary, "--runtime", "fail-fast", "--json");
            var (componentExit, componentOutput, componentError) = await RunAsync(input, "build-component",
                scanner, "-", "-o", compiled, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, hostExit);
            Assert.Equal(hostExit, componentExit);
            Assert.Empty(hostError);
            Assert.Empty(componentError);
            using var host = JsonDocument.Parse(hostOutput);
            using var component = JsonDocument.Parse(componentOutput);
            Assert.Equal(host.RootElement.GetProperty("diagnostics").EnumerateArray()
                .Select(static diagnostic => (diagnostic.GetProperty("code").GetString(),
                    diagnostic.GetProperty("span").GetProperty("start").GetInt32(),
                    diagnostic.GetProperty("span").GetProperty("length").GetInt32())),
                component.RootElement.GetProperty("diagnostics").EnumerateArray()
                    .Select(static diagnostic => (diagnostic.GetProperty("code").GetString(),
                        diagnostic.GetProperty("span").GetProperty("start").GetInt32(),
                        diagnostic.GetProperty("span").GetProperty("length").GetInt32())));
            Assert.False(File.Exists(ordinary));
            Assert.False(File.Exists(compiled));
        }
        finally
        {
            File.Delete(ordinary);
            File.Delete(compiled);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidCompiledIntegerRecordsRejectBuildWithoutReplacingArtifactAsync(bool missing)
    {
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-invalid-decoder-{Guid.NewGuid():N}");
        var scanner = prefix + ".terrum";
        var artifact = prefix + ".exe";
        var record = missing ? "" : """
                Host.IntegerValues[0] = 1;
                Host.IntegerValues[1] = 0;
                Host.IntegerValues[2] = 0;
                Host.IntegerValues[3] = 42;
                Host.IntegerValues[4] = 0;
                Host.IntegerValues[5] = 6;
            """;
        var source = $$"""
            int32 Scan() {
                Host.Tokens[0] = 3;
                Host.Tokens[1] = 0;
                Host.Tokens[2] = Host.Source.Length;
                Host.Tokens[3] = 0;
                Host.Tokens[4] = Host.Source.Length;
                Host.Tokens[5] = 0;
            {{record}}
                return 2;
            }
            """;
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllTextAsync(scanner, source);
            await File.WriteAllBytesAsync(artifact, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes("42"),
                "build-component", scanner, "-", "-o", artifact, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM5001",
                document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(original, await File.ReadAllBytesAsync(artifact));
        }
        finally
        {
            File.Delete(scanner);
            File.Delete(artifact);
        }
    }
}
