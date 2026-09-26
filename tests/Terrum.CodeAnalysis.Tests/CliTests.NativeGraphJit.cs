// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class CliTests
{
    [Fact]
    public async Task CheckedJitApphostCatchesAndReportsUnhandledFailuresUnderCetAsync()
    {
        var apphost = GraphJitApphostPath();
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var source = Path.Combine(repository, "artifacts", "checked-jit-apphost-"
            + Guid.NewGuid().ToString("N") + ".terrum");
        try
        {
            foreach (var (body, expected) in new[] {
                ("unsafe { try { require false; } catch { return 42; } } return 0;", 42),
                ("require false; return 0;", unchecked((int)0xE0540003)),
            })
            {
                await File.WriteAllTextAsync(source, "int32 Main() { " + body + " }");
                var start = new ProcessStartInfo(apphost) {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("jit");
                start.ArgumentList.Add(source);
                start.ArgumentList.Add("--runtime");
                start.ArgumentList.Add("checked-exceptions");
                using var process = Process.Start(start)
                    ?? throw new InvalidOperationException("Could not start native JIT apphost.");
                var error = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (TimeoutException)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                    throw;
                }
                Assert.Equal(expected, process.ExitCode);
                Assert.Empty(await error.ConfigureAwait(true));
            }
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task NativeGraphJitReleasesOwnedBufferReturnedByLeafAsync(string policy)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-jit-buffer-" + Guid.NewGuid().ToString("N"));
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
                public OwnedInt32Buffer Build() {
                    require OwnedInt32Buffer.TryCreate(2, out var created);
                    mut OwnedInt32Buffer buffer = move created;
                    require buffer.TryAppend(40);
                    require buffer.TryAppend(2);
                    return move buffer;
                }
                """);
            await File.WriteAllTextAsync(middleSource,
                "public OwnedInt32Buffer Forward() { return Build(); }");
            await File.WriteAllTextAsync(rootSource,
                "int32 Main() { OwnedInt32Buffer value = Forward(); return value.Read(0) + value.Read(1); }");
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", leafSource, "Leaf",
                "--export", "Build", "-o", leaf, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "producer", middleSource, "Middle",
                "--provider", leaf, "--export", "Forward", "-o", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "-o", root, "--runtime", policy)).ExitCode);
            Assert.Equal(42, (await RunAsync([], "run-native-graph", root,
                "--provider", middle, "--provider", leaf, "--runtime", policy)).ExitCode);
            var (jitExit, jitOutput, jitError) = await RunAsync([], "run-native-graph-jit", root,
                "--provider", leaf, "--provider", middle, "--runtime", policy);
            Assert.True(jitExit == 42, $"Expected 42, got {jitExit}: stdout: {jitOutput}; stderr: {jitError}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task NativeGraphJitDropsLeafOwnedCompositeThroughRootAsync(string policy)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-jit-composite-" + Guid.NewGuid().ToString("N"));
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
                public unique struct Child {
                    public int32 Value;
                    [NoThrow] public Child(int32 value) { Value = value; }
                    [NoThrow] [Cleanup] public void Close() {}
                }
                public unique struct Parent {
                    public Child Owned;
                    [NoThrow] public Parent(int32 value) { Owned = new Child(value); }
                }
                public Parent Make() { return new Parent(40); }
                """);
            await File.WriteAllTextAsync(middleSource, "public Parent Forward() { return Make(); }");
            await File.WriteAllTextAsync(rootSource, policy == "checked-exceptions"
                ? "int32 Main() { unsafe { try { Parent value = Forward(); require false; } "
                    + "catch { return 42; } } return 0; }"
                : "int32 Main() { Parent value = Forward(); return value.Owned.Value + 2; }");
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", leafSource, "Leaf",
                "--export", "Make", "--export", "Child.Close", "-o", leaf, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "producer", middleSource, "Middle",
                "--provider", leaf, "--export", "Forward", "-o", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", middle, "--provider", leaf, "-o", root, "--runtime", policy)).ExitCode);
            Assert.Equal(42, (await RunAsync([], "run-native-graph", root,
                "--provider", middle, "--provider", leaf, "--runtime", policy)).ExitCode);
            Assert.Equal(42, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", leaf, "--provider", middle, "--runtime", policy)).ExitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task NativeGraphJitRunsPersistedImagesAndCompatibleLeafReplacementAsync(string policy)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-jit-cli-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var leaf = Path.Combine(directory, "leaf.trmn");
        var replacement = Path.Combine(directory, "replacement.trmn");
        var middle = Path.Combine(directory, "middle.trng");
        var root = Path.Combine(directory, "root.trng");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var fixture = Path.Combine(repository, "samples", "native", "graph");
        try
        {
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixture, "owned-leaf.terrum"), "Leaf", "--export", "Make",
                "--export", "Ticket.Close", "-o", leaf, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "producer",
                Path.Combine(fixture, "owned-middle.terrum"), "Middle", "--provider", leaf,
                "--export", "Forward", "--export", "TryForward", "-o", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer",
                Path.Combine(fixture, "owned-consumer.terrum"), "--provider", middle,
                "--provider", leaf, "-o", root, "--runtime", policy)).ExitCode);
            var originalRoot = await File.ReadAllBytesAsync(root);
            var originalMiddle = await File.ReadAllBytesAsync(middle);
            Assert.Equal(42, (await RunAsync([], "run-native-graph", root, "--provider", middle,
                "--provider", leaf, "--runtime", policy)).ExitCode);
            Assert.Equal(42, (await RunAsync([], "run-native-graph-jit", root, "--provider", leaf,
                "--provider", middle, "--runtime", policy)).ExitCode);
            var (apphostOriginal, originalError) = await RunGraphJitApphostAsync(
                "run-native-graph-jit", root, "--provider", middle, "--provider", leaf, "--runtime", policy);
            Assert.Equal(42, apphostOriginal);
            Assert.Empty(originalError);
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixture, "owned-leaf-replacement.terrum"), "Leaf", "--export", "Make",
                "--export", "Ticket.Close", "-o", replacement, "--runtime", policy)).ExitCode);
            Assert.Equal(44, (await RunAsync([], "run-native-graph", root, "--provider", replacement,
                "--provider", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(44, (await RunAsync([], "run-native-graph-jit", root, "--provider", middle,
                "--provider", replacement, "--runtime", policy)).ExitCode);
            var (apphostReplacement, replacementError) = await RunGraphJitApphostAsync(
                "run-native-graph-jit", root, "--provider", replacement, "--provider", middle, "--runtime", policy);
            Assert.Equal(44, apphostReplacement);
            Assert.Empty(replacementError);
            Assert.Equal(originalRoot, await File.ReadAllBytesAsync(root));
            Assert.Equal(originalMiddle, await File.ReadAllBytesAsync(middle));

            Assert.Equal(1, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(1, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", replacement, "--provider", replacement, "--provider", middle,
                "--runtime", policy)).ExitCode);
            var incompatibleSource = Path.Combine(directory, "incompatible.terrum");
            var incompatible = Path.Combine(directory, "incompatible.trmn");
            var originalSource = await File.ReadAllTextAsync(Path.Combine(fixture, "owned-leaf.terrum"));
            await File.WriteAllTextAsync(incompatibleSource,
                originalSource.Replace("public int32 Value;", "public int64 Value;", StringComparison.Ordinal)
                    .Replace("Value = value;", "Value = (int64)value;", StringComparison.Ordinal));
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer", incompatibleSource, "Leaf",
                "--export", "Make", "--export", "Ticket.Close", "-o", incompatible,
                "--runtime", policy)).ExitCode);
            Assert.Equal(1, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", incompatible, "--provider", middle, "--runtime", policy)).ExitCode);
            var damaged = Path.Combine(directory, "damaged.trmn");
            var bytes = await File.ReadAllBytesAsync(replacement);
            bytes[^1] ^= 1;
            await File.WriteAllBytesAsync(damaged, bytes);
            Assert.Equal(1, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", damaged, "--provider", middle, "--runtime", policy)).ExitCode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("require false; return n;", unchecked((int)0xE0540003))]
    [InlineData("return 2147483647 + n;", unchecked((int)0xE0540001))]
    [InlineData("return n / (n - n);", unchecked((int)0xE0540002))]
    public async Task NativeGraphJitPreservesUnhandledCheckedFailureAsync(string body, int expected)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-jit-unhandled-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var rootSource = Path.Combine(directory, "root.terrum");
        var leaf = Path.Combine(directory, "leaf.trmn");
        var root = Path.Combine(directory, "root.trng");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        try
        {
            await File.WriteAllTextAsync(rootSource, "int32 Main() { int32 n = Leaf(); " + body + " }");
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(repository, "samples", "native", "graph", "leaf.terrum"),
                "NodeB", "--export", "Leaf", "-o", leaf,
                "--runtime", "checked-exceptions")).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer", rootSource,
                "--provider", leaf, "-o", root, "--runtime", "checked-exceptions")).ExitCode);
            Assert.Equal(expected, (await RunAsync([], "run-native-graph", root,
                "--provider", leaf, "--runtime", "checked-exceptions")).ExitCode);
            Assert.Equal(expected, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", leaf, "--runtime", "checked-exceptions")).ExitCode);
            var (apphostExit, apphostError) = await RunGraphJitApphostAsync(
                "run-native-graph-jit", root, "--provider", leaf, "--runtime", "checked-exceptions");
            Assert.Equal(expected, apphostExit);
            Assert.Empty(apphostError);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("fail-fast")]
    [InlineData("checked-exceptions")]
    public async Task NativeGraphJitServicesInternalReadonlyViewLeafAsync(string policy)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "artifacts",
            "native-graph-view-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        var original = Path.Combine(directory, "leaf.trmn");
        var replacement = Path.Combine(directory, "leaf-view.trmn");
        var middle = Path.Combine(directory, "middle.trng");
        var root = Path.Combine(directory, "root.trng");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        var fixtures = Path.Combine(repository, "samples", "native", "graph");
        try
        {
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixtures, "scoped-view-leaf.terrum"), "NodeB", "--export", "Leaf",
                "-o", original, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "producer",
                Path.Combine(fixtures, "middle.terrum"), "NodeA", "--provider", original,
                "--export", "Middle", "-o", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native-graph", "consumer",
                Path.Combine(fixtures, "consumer.terrum"), "--provider", middle,
                "-o", root, "--runtime", policy)).ExitCode);
            var originalRoot = await File.ReadAllBytesAsync(root);
            var originalMiddle = await File.ReadAllBytesAsync(middle);
            Assert.Equal(42, (await RunAsync([], "run-native-graph-jit", root,
                "--provider", original, "--provider", middle, "--runtime", policy)).ExitCode);
            Assert.Equal(0, (await RunAsync([], "compile-native", "producer",
                Path.Combine(fixtures, "scoped-view-leaf-replacement.terrum"), "NodeB",
                "--export", "Leaf", "-o", replacement, "--runtime", policy)).ExitCode);
            Assert.Equal("native-16", NativeModuleCompiler.Inspect(await File.ReadAllBytesAsync(replacement))
                .NativeProfileVersion);
            Assert.Equal(0, (await RunAsync([], "validate-native-graph", root,
                "--provider", middle, "--provider", replacement)).ExitCode);
            Assert.Equal(44, (await RunAsync([], "run-native-graph", root,
                "--provider", replacement, "--provider", middle, "--runtime", policy)).ExitCode);
            var (jitExit, jitOutput, jitError) = await RunAsync([], "run-native-graph-jit", root,
                "--provider", middle, "--provider", replacement, "--runtime", policy);
            Assert.True(jitExit == 44, $"Expected 44, got {jitExit}: stdout: {jitOutput}; stderr: {jitError}");
            var (apphostExit, apphostError) = await RunGraphJitApphostAsync(
                "run-native-graph-jit", root, "--provider", replacement, "--provider", middle,
                "--runtime", policy);
            Assert.True(apphostExit == 44, $"Expected 44, got {apphostExit}: {apphostError}");
            Assert.Empty(apphostError);
            Assert.Equal(originalRoot, await File.ReadAllBytesAsync(root));
            Assert.Equal(originalMiddle, await File.ReadAllBytesAsync(middle));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GraphJitApphostPath()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", ".."));
        return Path.Combine(repository, "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
    }

    private static async Task<(int ExitCode, string Error)> RunGraphJitApphostAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(GraphJitApphostPath()) {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start native graph JIT apphost.");
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().ConfigureAwait(true);
            throw;
        }
        return (process.ExitCode, await error.ConfigureAwait(true));
    }
}
