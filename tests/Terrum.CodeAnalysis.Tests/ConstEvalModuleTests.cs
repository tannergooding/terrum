// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ConstEvalModuleTests
{
    private const string Library = """
        [ConstEval] int32 Offset(int32 input) { return input + 1; }
        public int32 Compute(int32 input) {
            const int32 bias = Offset(40);
            return bias + Offset(input);
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task PrivateConstantHelperServicesUnchangedSemanticAndNativeConsumerAsync(NativeRuntimePolicy policy)
    {
        const string Consumer = "int32 Main() { return Compute(0); }";
        var initial = CompileLibrary(Library, policy);
        var producer = ModuleCompiler.CreateProducer(initial, ["Compute"]);
        var reference = ModuleCompiler.CreateReference(producer);
        _ = Assert.Single(reference.Functions);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer(Consumer, reference, policy), producer);
        var snapshot = SHA256.HashData(consumer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));

        var changed = CompileLibrary(Library.Replace("input + 1", "input + 2", StringComparison.Ordinal), policy);
        var replacement = ModuleCompiler.CreateProducer(changed, ["Compute"]);
        Assert.Equal(44, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy));
        Assert.Equal(snapshot, SHA256.HashData(consumer));

        var nativeProducer = NativeModuleCompiler.CreateProducer(initial, ["Compute"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Consumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy), [nativeProducer], policy);
        var nativeSnapshot = SHA256.HashData(nativeConsumer);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(changed, ["Compute"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeSnapshot, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ExpressionRequirementServicesUnchangedConsumersAsync(NativeRuntimePolicy policy)
    {
        const string Source = """
            [ConstEval] int32 Offset(int32 input) { return input + 1; }
            public int32 Compute(int32 input) {
                return Offset(input) + const Offset(40);
            }
            """;
        const string Consumer = "int32 Main() { return Compute(0); }";
        var original = CompileLibrary(Source, policy);
        var producer = ModuleCompiler.CreateProducer(original, ["Compute"]);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer(Consumer,
            ModuleCompiler.CreateReference(producer), policy), producer);
        var snapshot = SHA256.HashData(consumer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy));

        var revised = CompileLibrary(Source.Replace("input + 1", "input + 2", StringComparison.Ordinal), policy);
        var replacement = ModuleCompiler.CreateProducer(revised, ["Compute"]);
        Assert.Equal(44, NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy));
        Assert.Equal(snapshot, SHA256.HashData(consumer));

        var nativeProducer = NativeModuleCompiler.CreateProducer(original, ["Compute"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Consumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy), [nativeProducer], policy);
        var nativeSnapshot = SHA256.HashData(nativeConsumer);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(revised, ["Compute"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.Equal(44, await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(nativeSnapshot, SHA256.HashData(nativeConsumer));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    [Fact]
    public void ImportedRuntimeFunctionsDoNotSatisfyRequiredConstantCalls()
    {
        var producer = ModuleCompiler.CreateProducer(CompileLibrary(Library, NativeRuntimePolicy.FailFast), ["Compute"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 Main() { const int32 value = Compute(1); return value; }"), reference);
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3024");
    }

    [Fact]
    public void NonexportedEligibilityDoesNotSelectANewSemanticContract()
    {
        const string Source = """
            [ConstEval] int32 Helper(int32 input) { return input + 1; }
            public int32 Compute(int32 input) { return Helper(input); }
            """;
        var marked = ModuleCompiler.CreateProducer(CompileLibrary(Source, NativeRuntimePolicy.FailFast),
            ["Compute"]);
        var ordinary = ModuleCompiler.CreateProducer(CompileLibrary(
            Source.Replace("[ConstEval] ", "", StringComparison.Ordinal), NativeRuntimePolicy.FailFast),
            ["Compute"]);
        Assert.Equal(BinaryPrimitives.ReadInt32LittleEndian(ordinary.AsSpan(4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(marked.AsSpan(4, 4)));
        var markedFunction = Assert.Single(ModuleCompiler.CreateReference(marked).Functions);
        var ordinaryFunction = Assert.Single(ModuleCompiler.CreateReference(ordinary).Functions);
        Assert.Equal(ordinaryFunction.Name, markedFunction.Name);
        Assert.Equal(ordinaryFunction.ReturnType, markedFunction.ReturnType);
        Assert.Equal(ordinaryFunction.Variables[0].Type, markedFunction.Variables[0].Type);
    }

    private static BoundProgram CompileConsumer(string source, CompilationReference reference, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference, (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        return Assert.IsType<BoundProgram>(compilation.Program);
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
