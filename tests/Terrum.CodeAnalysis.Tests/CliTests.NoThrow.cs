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
    public async Task NoThrowValueAndOutputContractsExecuteThroughJitAsync()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var source = await File.ReadAllBytesAsync(Path.Combine(root, "samples", "native", "no-throw.terrum"));
        var (exit, output, error) = await RunAsync(source, "jit", "-", "--runtime", "fail-fast", "--json");
        Assert.Equal(42, exit);
        Assert.Empty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task NoThrowViolationHasSourceCoordinatesAndPreservesNativeOutputAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"terrum-no-throw-{Guid.NewGuid():N}.exe");
        byte[] original = [1, 2, 3];
        try
        {
            await File.WriteAllBytesAsync(path, original);
            var (exit, output, error) = await RunAsync(Encoding.UTF8.GetBytes(
                "[NoThrow] int32 Main() { require false; return 0; }"),
                "build", "-", "-o", path, "--runtime", "fail-fast", "--json");
            Assert.Equal(1, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("TRM3017", diagnostic.GetProperty("code").GetString());
            Assert.True(diagnostic.GetProperty("span").GetProperty("length").GetInt32() > 0);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
