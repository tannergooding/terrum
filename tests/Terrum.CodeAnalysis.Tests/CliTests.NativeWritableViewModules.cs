// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task NativeGraphJitServicesWritableViewLeafWithoutRebuildingRootAsync(string policy)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-writable-view-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "original.trmn");
        var readonlyReplacement = Path.Combine(directory, "readonly.trmn");
        var writableReplacement = Path.Combine(directory, "writable.trmn");
        var middle = Path.Combine(directory, "middle.trng");
        var root = Path.Combine(directory, "root.trng");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var fixtures = Path.Combine(repository, "samples", "native", "graph");
        try
        {
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixtures, "writable-view-leaf-original.terrum"), "NodeB",
                "--export", "Leaf", "-o", original, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixtures, "scoped-view-leaf-replacement.terrum"), "NodeB",
                "--export", "Leaf", "-o", readonlyReplacement, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixtures, "writable-view-leaf.terrum"), "NodeB",
                "--export", "Leaf", "-o", writableReplacement, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "producer",
                Path.Combine(fixtures, "middle.terrum"), "NodeA", "--provider", original,
                "--export", "Middle", "-o", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer",
                Path.Combine(fixtures, "consumer.terrum"), "--provider", middle,
                "-o", root, "--runtime", policy)).ExitCode);
            var persistedMiddle = await File.ReadAllBytesAsync(middle);
            var persistedRoot = await File.ReadAllBytesAsync(root);

            foreach (var (provider, expected) in new[] {
                (original, 42),
                (readonlyReplacement, 44),
                (writableReplacement, 46),
            })
            {
                Assert.Equal(0, (await RunAsync([], "validate-native-graph", root,
                    "--provider", middle, "--provider", provider)).ExitCode);
                Assert.Equal(expected, (await RunAsync([], "run-native-graph", root,
                    "--provider", middle, "--provider", provider, "--runtime", policy)).ExitCode);
                Assert.Equal(expected, (await RunAsync([], "run-native-graph-jit", root,
                    "--provider", provider, "--provider", middle, "--runtime", policy)).ExitCode);
                var (apphostExit, apphostError) = await RunGraphJitApphostAsync(
                    "run-native-graph-jit", root, "--provider", middle, "--provider", provider,
                    "--runtime", policy);
                Assert.Equal(expected, apphostExit);
                Assert.Empty(apphostError);
            }
            Assert.Equal(persistedRoot, await File.ReadAllBytesAsync(root));
            Assert.Equal(persistedMiddle, await File.ReadAllBytesAsync(middle));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
