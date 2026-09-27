// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class GenericControlFlowModuleTests
{
    private const string Stub = "public int32 PowerProbe() { return 40; }";
    private const string Middle = "public int32 Middle() { return PowerProbe() + 1; }";
    private const string Root = "int32 Main() { Middle(); return Middle() + 1; }";

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ClosedPowerSpecializationsRemainPrivateAfterSemanticReplacementAsync(
        NativeRuntimePolicy policy)
    {
        var stub = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            struct Int32Value {
                public int64 Unrelated;
                public Int32Value(int64 value) { Unrelated = value; }
            }
            struct Int64Value {
                public int32 Unrelated;
                public Int64Value(int32 value) { Unrelated = value; }
            }
            int32 Main() {
                Int32Value narrow = new Int32Value(1i64);
                Int64Value wide = new Int64Value(2);
                return PowerProbe() + (int32)narrow.Unrelated + wide.Unrelated - 3;
            }
            """, policy, ModuleCompiler.CreateReference(stub), library: false), stub);
        var consumerHash = SHA256.HashData(consumer);
        Assert.Equal(18, BinaryPrimitives.ReadInt32LittleEndian(consumer.AsSpan(4, 4)));

        var replacement = ModuleCompiler.CreateProducer(Compile(await PowerSourceAsync().ConfigureAwait(true), policy),
            ["PowerProbe"]);
        Assert.Equal(21, BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        Assert.Equal(policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11",
            ModuleCompiler.GetNativeProfileVersion(replacement, false));
        var reference = ModuleCompiler.CreateReference(replacement);
        Assert.Empty(reference.Structures);
        Assert.Equal("PowerProbe", Assert.Single(reference.Functions).Name);

        var original = ModuleCompiler.Link(consumer, stub);
        Assert.Equal(40, await ExecuteAsync(NativeEmitter.Emit(original, policy)).ConfigureAwait(true));
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(["Int32Value", "Int64Value", "$producer.Int32Value", "$producer.Int64Value"],
            linked.Structures.Select(static item => item.Name));
        Assert.Equal(2, linked.Functions.Count(static function =>
            function.Name.StartsWith("$producer.Power<", StringComparison.Ordinal)));
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Power<Int32Value>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Power<Int64Value>");
        Assert.Equal(2, linked.Functions.Count(static function =>
            function.Name.StartsWith("$producer.Square<", StringComparison.Ordinal)));
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Square<Int32Value>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Square<Int64Value>");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Int32Value.op_Multiply");
        Assert.Contains(linked.Functions, static function => function.Name == "$producer.Int64Value.op_Multiply");
        Assert.DoesNotContain(linked.Functions, static function => function.Name == "$producer.Power");
        Assert.DoesNotContain(reference.Functions, static function =>
            function.Name.Contains("Power<", StringComparison.Ordinal));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        var consumerPath = Path.Combine(AppContext.BaseDirectory, $"power-{Guid.NewGuid():N}.consumer.trm");
        var producerPath = Path.ChangeExtension(consumerPath, ".producer.trm");
        try
        {
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, stub).ConfigureAwait(true);
            var (originalExit, originalError) = await RunCliAsync("run-module", consumerPath, producerPath,
                "--runtime", policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions")
                .ConfigureAwait(true);
            Assert.Empty(originalError);
            Assert.Equal(40, originalExit);
            await File.WriteAllBytesAsync(producerPath, replacement).ConfigureAwait(true);
            var (exit, error) = await RunCliAsync("run-module", consumerPath, producerPath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions").ConfigureAwait(true);
            Assert.Empty(error);
            Assert.Equal(42, exit);
            Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
        Assert.Equal(consumerHash, SHA256.HashData(consumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ClosedPowerSpecializationsReplaceUnchangedNativeGraphAsync(NativeRuntimePolicy policy)
    {
        var original = NativeModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"],
            "NodeB", policy);
        var middle = NativeModuleCompiler.CreateGraphProducer(Compile(Middle, policy,
            NativeModuleCompiler.CreateGraphReference([original])), ["Middle"], "NodeA", [original], policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(Root, policy,
            NativeModuleCompiler.CreateGraphReference([middle]), library: false), [middle], policy);
        var rootHash = SHA256.HashData(root);
        var middleHash = SHA256.HashData(middle);

        var replacement = NativeModuleCompiler.CreateProducer(
            Compile(await PowerSourceAsync().ConfigureAwait(true), policy),
            ["PowerProbe"], "NodeB", policy);
        Assert.Equal(policy == NativeRuntimePolicy.FailFast ? "native-10" : "native-11",
            NativeModuleCompiler.Inspect(replacement).NativeProfileVersion);
        Assert.Equal("PowerProbe", Assert.Single(NativeModuleCompiler.CreateReference(replacement).Functions).Name);
        NativeModuleCompiler.ValidateGraph(root, [middle, original]);
        NativeModuleCompiler.ValidateGraph(root, [replacement, middle]);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [original, middle],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(root, [middle, replacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(root, [replacement, middle],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(rootHash, SHA256.HashData(root));
        Assert.Equal(middleHash, SHA256.HashData(middle));
    }

    private static async Task<string> PowerSourceAsync() =>
        await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "static-protocol-power.terrum")).ConfigureAwait(true);

    private static BoundProgram Compile(string source, NativeRuntimePolicy policy,
        CompilationReference? reference = null, bool library = true)
    {
        var syntax = SyntaxTree.Parse(source);
        var boundPolicy = (BoundRuntimePolicy)policy;
        var compilation = library
            ? reference is null ? Compilation.CreateLibrary(syntax, boundPolicy)
                : Compilation.CreateLibrary(syntax, reference, boundPolicy)
            : Compilation.Create(syntax, Assert.IsType<CompilationReference>(reference), boundPolicy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static async Task<int> ExecuteAsync(byte[] image)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"generic-module-{Guid.NewGuid():N}.exe");
        try
        {
            await File.WriteAllBytesAsync(path, image).ConfigureAwait(true);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Failed to start linked native program.");
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(true);
                }
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<(int ExitCode, string Error)> RunCliAsync(params string[] arguments)
    {
        var cli = Apphost();
        Assert.True(File.Exists(cli), $"CLI was not built: {cli}");
        var start = new ProcessStartInfo(cli) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Failed to start module JIT worker.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(true);
            }
            throw;
        }
        _ = await stdout.ConfigureAwait(true);
        return (process.ExitCode, await stderr.ConfigureAwait(true));
    }

    private static string Apphost()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = frameworkDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        return Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, frameworkDirectory.Name, "terrumc.exe");
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
