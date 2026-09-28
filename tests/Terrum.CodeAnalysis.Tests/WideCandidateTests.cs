// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class WideCandidateTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WideCompilerCandidatesExecuteFromIndependentModulesAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateCandidateLibrary((BoundRuntimePolicy)policy);
        var reference = ModuleCompiler.CreateReference(producer);
        var root = RepositoryRoot();
        var tests = ReadTree(Path.Combine(root, "self-host", "WideIntegerTests.terrum"));
        var entry = ReadTree(Path.Combine(root, "samples", "native", "modules", "wide-entry.terrum"));
        var compilation = Compilation.Create([tests, entry], reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(0, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 0, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SaturatingConversionsExecuteFromIndependentModuleAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerSaturation.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt32ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt32ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.SaturateUInt64ToInt64",
            "Terrum.CodeAnalysis.Numerics64.SaturateInt64ToUInt64");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (SaturateUInt32ToInt32(uint32.MaxValue) != int32.MaxValue
                    || SaturateUInt32ToInt32(42) != 42) { return 7; }
                if (SaturateInt32ToUInt32(int32.MinValue) != 0
                    || SaturateInt32ToUInt32(42) != 42) { return 8; }
                if (SaturateInt64ToInt32(int64.MinValue) != int32.MinValue
                    || SaturateInt64ToInt32(int64.MaxValue) != int32.MaxValue
                    || SaturateInt64ToInt32(42) != 42) { return 1; }
                if (SaturateInt64ToUInt32(-1) != 0
                    || SaturateInt64ToUInt32(int64.MaxValue) != uint32.MaxValue) { return 2; }
                if (SaturateUInt64ToInt32(uint64.MaxValue) != int32.MaxValue
                    || SaturateUInt64ToInt32(42) != 42) { return 3; }
                if (SaturateUInt64ToUInt32(uint64.MaxValue) != uint32.MaxValue
                    || SaturateUInt64ToUInt32(42) != 42) { return 4; }
                if (SaturateUInt64ToInt64(uint64.MaxValue) != int64.MaxValue
                    || SaturateUInt64ToInt64(42) != 42) { return 5; }
                if (SaturateInt64ToUInt64(int64.MinValue) != 0
                    || SaturateInt64ToUInt64(int64.MaxValue) != uint64(int64.MaxValue)) { return 6; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task WrappingConversionsExecuteFromIndependentModuleAsync(NativeRuntimePolicy policy)
    {
        var producer = CreateNumericsLibrary("IntegerWrapping.terrum", policy,
            "Terrum.CodeAnalysis.Numerics64.WrapInt32ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt32ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToUInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt32",
            "Terrum.CodeAnalysis.Numerics64.WrapInt64ToUInt64",
            "Terrum.CodeAnalysis.Numerics64.WrapUInt64ToInt64");
        var consumerSource = SyntaxTree.Parse("""
            using Terrum.CodeAnalysis.Numerics64;
            int32 Main() {
                if (WrapInt32ToUInt32(-1) != uint32.MaxValue
                    || WrapUInt32ToInt32(uint32.MaxValue) != -1) { return 1; }
                if (WrapInt64ToUInt32(-4294967297) != uint32.MaxValue
                    || WrapInt64ToInt32(2147483648) != int32.MinValue) { return 2; }
                if (WrapUInt64ToUInt32(uint64.MaxValue) != uint32.MaxValue
                    || WrapUInt64ToInt32(4294967295) != -1) { return 3; }
                if (WrapInt64ToUInt64(-1) != uint64.MaxValue
                    || WrapUInt64ToInt64(uint64.MaxValue) != -1) { return 4; }
                return 42;
            }
            """);
        var compilation = Compilation.Create(consumerSource, ModuleCompiler.CreateReference(producer),
            (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));
        await ExecuteBothAsync(consumer, producer, 42, policy).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("TryAdd(int64.MaxValue, 1, out var value); return int32(value);")]
    [InlineData("if (TryAdd(int64.MaxValue, 1, out var value)) { return 0; } return int32(value);")]
    [InlineData("uint32 value = 1; require TryAdd(value, 2, out var sum); return int32(sum);")]
    [InlineData("require TryAccumulateDecimal(18446744073709551616, 0, uint64.MaxValue, out var value); return 0;")]
    [InlineData("TryAccumulateBase(1, 1, 2, uint64.MaxValue, out var value); return int32(value);")]
    public void WideCandidateContractsRejectInvalidUse(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "using Terrum.CodeAnalysis.Numerics64; int32 Main() { " + body + " }"),
            ModuleCompiler.CreateReference(CreateCandidateLibrary()));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public async Task WideModuleContractsPermitImplementationReplacementButNotSignednessChangesAsync()
    {
        var original = Compilation.CreateLibrary(SyntaxTree.Parse("public int64 Echo(int64 value) { return value; }"));
        Assert.False(original.HasErrors, string.Join(Environment.NewLine, original.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(original.Program), ["Echo"]);
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { return int32(Echo(42)); }"),
            ModuleCompiler.CreateReference(producer));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(Assert.IsType<BoundProgram>(compilation.Program), producer);
        var preserved = (byte[])consumer.Clone();
        await ExecuteBothAsync(consumer, producer, 42).ConfigureAwait(true);
        var changed = Compilation.CreateLibrary(SyntaxTree.Parse("public int64 Echo(int64 value) { return value + 1; }"));
        Assert.False(changed.HasErrors, string.Join(Environment.NewLine, changed.Diagnostics));
        var replacement = ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(changed.Program), ["Echo"]);
        await ExecuteBothAsync(consumer, replacement, 43).ConfigureAwait(true);
        Assert.Equal(preserved, consumer);

        var incompatible = Compilation.CreateLibrary(SyntaxTree.Parse("public uint64 Echo(uint64 value) { return value; }"));
        Assert.False(incompatible.HasErrors, string.Join(Environment.NewLine, incompatible.Diagnostics));
        _ = Assert.Throws<ModuleContractException>(() => ModuleCompiler.Link(consumer,
            ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(incompatible.Program), ["Echo"])));
    }

    private static byte[] CreateCandidateLibrary(BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var path = Path.Combine(RepositoryRoot(), "self-host", "WideIntegerFacts.terrum");
        var compilation = Compilation.CreateLibrary(ReadTree(path), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), [
            "Terrum.CodeAnalysis.Numerics64.TryAdd",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateDecimal",
            "Terrum.CodeAnalysis.Numerics64.TryAccumulateBase",
        ]);
    }

    private static byte[] CreateNumericsLibrary(string name, NativeRuntimePolicy policy, params string[] exports)
    {
        var source = ReadTree(Path.Combine(RepositoryRoot(), "self-host", name));
        var compilation = Compilation.CreateLibrary(source, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(Assert.IsType<BoundProgram>(compilation.Program), exports);
    }

    private static SyntaxTree ReadTree(string path) => SyntaxTree.Parse(File.ReadAllText(path), path);

    private static string RepositoryRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    private static async Task ExecuteBothAsync(byte[] consumer, byte[] producer, int expected,
        NativeRuntimePolicy policy = NativeRuntimePolicy.FailFast)
    {
        var prefix = Path.Combine(AppContext.BaseDirectory, $"wide-candidate-{Guid.NewGuid():N}");
        var image = prefix + ".exe";
        var consumerPath = prefix + "-consumer.trm";
        var producerPath = prefix + "-producer.trm";
        try
        {
            var program = ModuleCompiler.Link(consumer, producer);
            await File.WriteAllBytesAsync(image, NativeEmitter.Emit(program, policy)).ConfigureAwait(true);
            await RunAsync(image, [], expected).ConfigureAwait(true);
            await File.WriteAllBytesAsync(consumerPath, consumer).ConfigureAwait(true);
            await File.WriteAllBytesAsync(producerPath, producer).ConfigureAwait(true);
            var framework = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = framework.Parent?.Name ?? throw new InvalidOperationException("Missing configuration.");
            var cli = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc", configuration, framework.Name, "terrumc.dll");
            await RunAsync("dotnet", [cli, "run-module", consumerPath, producerPath, "--runtime",
                policy == NativeRuntimePolicy.FailFast ? "fail-fast" : "checked-exceptions"], expected).ConfigureAwait(true);
            Assert.Equal(consumer, await File.ReadAllBytesAsync(consumerPath).ConfigureAwait(true));
        }
        finally
        {
            File.Delete(image);
            File.Delete(consumerPath);
            File.Delete(producerPath);
        }
    }

    private static async Task RunAsync(string path, ImmutableArray<string> arguments, int expected)
    {
        var start = new ProcessStartInfo(path) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start wide-integer worker.");
        var error = process.StandardError.ReadToEndAsync();
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
        Assert.Equal(expected, process.ExitCode);
        Assert.Empty(await error.ConfigureAwait(true));
    }
}
