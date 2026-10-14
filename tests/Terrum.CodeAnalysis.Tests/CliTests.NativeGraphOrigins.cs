// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Test]
    public async Task NativeGraphCliRetainsOriginAndPreservesOutputOnMissingCleanupProviderAsync()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
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
                public unique struct Ticket {
                    public int32 Value;
                    [NoThrow] public Ticket(int32 value) { Value = value; }
                    [NoThrow] [Cleanup] public void Close() {}
                }
                public Ticket Make() { return new Ticket(20); }
                """);
            await File.WriteAllTextAsync(middleSource, "public Ticket Forward() { return Make(); }");
            await File.WriteAllTextAsync(rootSource,
                "int32 Main() { Ticket value = Forward(); return value.Value + 22; }");
            Assert.That((await RunAsync([], "compile-native", "producer", leafSource, "Leaf",
                "--export", "Make", "--export", "Ticket.Close", "-o", leaf,
                "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native-graph", "producer", middleSource, "Middle",
                "--provider", leaf, "--export", "Forward", "-o", middle, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            Assert.That((await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "--provider", leaf, "-o", root,
                "--runtime", "fail-fast")).ExitCode, Is.EqualTo(0));
            var unchanged = await File.ReadAllBytesAsync(root);
            Assert.That((await RunAsync([], "run-native-graph", root, "--provider", leaf,
                "--provider", middle, "--runtime", "fail-fast")).ExitCode, Is.EqualTo(42));
            var (failureExit, _, _) = await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", "fail-fast");
            Assert.That(failureExit, Is.Not.EqualTo(0));
            Assert.That(await File.ReadAllBytesAsync(root), Is.EqualTo(unchanged));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
