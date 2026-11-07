// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Cli;

internal sealed partial class CliTests
{
    [Test]
    public async Task NativeGraphCliRetainsOriginAndPreservesOutputOnMissingCleanupProviderAsync()
    {
        var directory = Path.Combine(TestArtifactPaths.DirectoryPath,
            "native-graph-origin-cli-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var leafSource = Path.Combine(directory, "leaf.terrum");
        var middleSource = Path.Combine(directory, "middle.terrum");
        var rootSource = Path.Combine(directory, "root.terrum");
        var leaf = Path.Combine(directory, "leaf.trmn");
        var middle = Path.Combine(directory, "middle.trng");
        var root = Path.Combine(directory, "root.trng");
        try
        {
            await File.WriteAllTextAsync(leafSource, """
                [Blittable] [Layout(LayoutKind.Sequential)] public unique struct Ticket {
                    public int32 Value;
                     public Ticket(int32 value) { Value = value; }
                     [Cleanup] public void Close() {}
                }
                public Ticket Make() { return new Ticket(20); }
                """).ConfigureAwait(false);
            await File.WriteAllTextAsync(middleSource, "public Ticket Forward() { return Make(); }").ConfigureAwait(false);
            await File.WriteAllTextAsync(rootSource,
                "int32 Main() { Ticket value = Forward(); return value.Value + 22; }").ConfigureAwait(false);
            Assert.That((await RunAsync([], "compile-native", "producer", leafSource, "Leaf",
                "--export", "Make", "--export", "Ticket.Close", "-o", leaf,
                "--runtime", "fail-fast").ConfigureAwait(false)).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native-graph", "producer", middleSource, "Middle",
                "--provider", leaf, "--export", "Forward", "-o", middle, "--runtime", "fail-fast").ConfigureAwait(false)).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "--provider", leaf, "-o", root,
                "--runtime", "fail-fast").ConfigureAwait(false)).ExitCode, Is.EqualTo(0));
            var unchanged = await File.ReadAllBytesAsync(root).ConfigureAwait(false);
            Assert.That((await RunAsync([], "run-native-graph", root, "--provider", leaf,
                "--provider", middle, "--runtime", "fail-fast").ConfigureAwait(false)).ExitCode, Is.EqualTo(42));
            var (failureExit, _, _) = await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", "fail-fast").ConfigureAwait(false);
            Assert.That(failureExit, Is.Not.EqualTo(0));
            Assert.That(await File.ReadAllBytesAsync(root).ConfigureAwait(false), Is.EqualTo(unchanged));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
