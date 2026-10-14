// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Test]
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
        Assert.That(exit, Is.EqualTo(42));
        Assert.That(output, Is.Empty);
        Assert.That(error, Is.Empty);
    }

    [Test]
    public async Task CompiledScannerCharacterLiteralsMatchHostedBuildAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var scanner = Path.Combine(root, "self-host");
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
            Assert.That(hostExit, Is.EqualTo(0));
            Assert.That(componentExit, Is.EqualTo(0));
            Assert.That(hostError, Is.Empty);
            Assert.That(componentError, Is.Empty);
            using var host = JsonDocument.Parse(hostOutput);
            using var component = JsonDocument.Parse(componentOutput);
            Assert.That(component.RootElement.GetProperty("contentId").GetString(), Is.EqualTo(host.RootElement.GetProperty("contentId").GetString()));
            Assert.That(await File.ReadAllBytesAsync(compiled), Is.EqualTo(await File.ReadAllBytesAsync(hosted)));
        }
        finally
        {
            File.Delete(hosted);
            File.Delete(compiled);
        }
    }
}
