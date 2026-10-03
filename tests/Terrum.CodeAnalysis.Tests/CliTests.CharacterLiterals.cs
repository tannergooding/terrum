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
    [Fact]
    public async Task CharacterLiteralsExecuteThroughHostedCompilerAndJitAsync()
    {
        const string Source = """
            int32 Main() {
                char16 direct = 'A';
                char16 escaped = '\u0041';
                if (direct == escaped) { return int32(direct) - 23; }
                return 0;
            }
            """;
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(Source),
            "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(42, exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task CompiledScannerCharacterLiteralsMatchHostedBuildAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = Path.Combine(root, "self-host", "Scanner.terrum");
        var prefix = Path.Combine(Path.GetTempPath(), $"terrum-character-literal-{Guid.NewGuid():N}");
        var hosted = prefix + ".exe";
        var compiled = prefix + ".compiled.exe";
        try
        {
            var input = Encoding.UTF8.GetBytes(
                "int32 Main() { char16 value = '\\U00000041'; if (value == 'A') { return int32(value) - 23; } return 0; }");
            var (hostExit, hostOutput, hostError) = await RunAsync(input, "build", "-", "-o",
                hosted, "--runtime", "fail-fast", "--json");
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
            Assert.Equal(await File.ReadAllBytesAsync(hosted), await File.ReadAllBytesAsync(compiled));
        }
        finally
        {
            File.Delete(hosted);
            File.Delete(compiled);
        }
    }
}
