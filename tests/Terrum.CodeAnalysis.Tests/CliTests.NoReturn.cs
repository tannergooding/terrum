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
    [InlineData(true, 42)]
    [InlineData(false, unchecked((int)0xE0540003))]
    public async Task NoReturnOutputBranchesExecuteWithoutManufacturedValuesAsync(bool success, int expected)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllTextAsync(Path.Combine(root, "samples", "native", "no-return.terrum"));
        if (!success)
        {
            source = source.Replace("return Select(true);", "return Select(false);", StringComparison.Ordinal);
        }
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(source), "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(expected, exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task NonReturningTailCallNeedsNoInventedReturnValueAsync()
    {
        var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
            "[DoesNotReturn] void Stop() { require false; } int32 Main() { Stop(); }"),
            "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(unchecked((int)0xE0540003), exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task UnprovedNoReturnDeclarationPreservesPriorArtifactAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-no-return-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
                "[DoesNotReturn] void Stop() { return; } int32 Main() { Stop(); }"),
                "build", "-", "-o", path, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("TRM3018", document.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString());
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
