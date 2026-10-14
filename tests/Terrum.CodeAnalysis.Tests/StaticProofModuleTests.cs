// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProofModuleTests
{
    private const string Library = """
        [ConstEval] bool Valid() { return true; }
        public int32 Compute(int32 value) {
            static ensure Valid();
            static require Valid();
            require value > 0;
            return value + 1;
        }
        """;
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task StaticChecksLeaveNoPublicContractOrExecutableModuleCheckAsync(NativeRuntimePolicy policy)
    {
        const string Consumer = "int32 Main() { return Compute(41); }";
        var first = CompileLibrary(Library, policy);
        var producer = ModuleCompiler.CreateProducer(first, ["Compute"]);
        var reference = ModuleCompiler.CreateReference(producer);
        _ = global::System.Linq.Enumerable.Single(reference.Functions);
        var consumer = ModuleCompiler.CreateConsumer(CompileConsumer(Consumer, reference, policy), producer);
        var snapshot = SHA256.HashData(consumer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, producer), policy), Is.EqualTo(42));

        var next = CompileLibrary(Library.Replace("value + 1", "value + 2", StringComparison.Ordinal), policy);
        var replacement = ModuleCompiler.CreateProducer(next, ["Compute"]);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(consumer, replacement), policy), Is.EqualTo(43));
        Assert.That(SHA256.HashData(consumer), Is.EqualTo(snapshot));

        var nativeProducer = NativeModuleCompiler.CreateProducer(first, ["Compute"], "NodeB", policy);
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(CompileConsumer(Consumer,
            NativeModuleCompiler.CreateGraphReference([nativeProducer]), policy), [nativeProducer], policy);
        var nativeSnapshot = SHA256.HashData(nativeConsumer);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeProducer],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(42));
        var nativeReplacement = NativeModuleCompiler.CreateProducer(next, ["Compute"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [nativeReplacement]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(43));
        var framework = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = framework.Parent?.Name
            ?? throw new InvalidOperationException("Missing test configuration directory.");
        var apphost = Path.Combine(RepositoryRoot(), "artifacts", "bin", "sources", "terrumc",
            configuration, framework.Name, "terrumc.exe");
        Assert.That(await NativeModuleCompiler.RunGraphJitAsync(nativeConsumer, [nativeReplacement],
            apphost, TimeSpan.FromSeconds(30), policy).ConfigureAwait(true), Is.EqualTo(43));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeSnapshot));
    }

    private static BoundProgram CompileLibrary(string source, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static BoundProgram CompileConsumer(string source, CompilationReference reference, NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), reference, (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", ".."));
}
