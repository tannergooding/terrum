using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed class NativeRawPointerModuleTests
{
    [Test]
    public void OpaquePointerResultsRetainCallerUnsafeAcrossPrivateModules()
    {
        var providerCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe ptr<void> Echo(ptr<void> value) { return value; }
            """));
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(providerCompilation.Program!, ["Echo"]);
        var reference = ModuleCompiler.CreateReference(producer);

        var safeConsumer = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() { return Echo(null) == null ? 0 : -1; }
            """), reference);
        Assert.That(safeConsumer.Diagnostics.Any(static diagnostic => diagnostic.Code == "TRM3028"), Is.True);

        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe { return Echo(null) == null ? 0 : -1; }
            }
            """), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, producer);
        var linked = ModuleCompiler.Link(consumer, producer);

        Assert.That(NativeJit.Execute(linked, NativeRuntimePolicy.FailFast), Is.Zero);
    }
}
