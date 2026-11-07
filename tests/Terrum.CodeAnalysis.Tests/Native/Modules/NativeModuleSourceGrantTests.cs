using System;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeModuleSourceGrantTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
    public void NativePackagesRejectTestOnlySourceGrantProfiles(BoundRuntimePolicy policy)
    {
        var source = SyntaxTree.Parse("""
            namespace Terrum.CodeAnalysis;
            public int32 ReadSourceLength() { return int32(Host.Source.Length); }
            """);
        var compilation = Compilation.CreateLibrary([source],
            ScannerHost.CreateInvocationProfileBuffers(SourceText.From("seed")), policy);
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program, Is.TypeOf<BoundProgram>());

        var program = (BoundProgram)compilation.Program!;
        _ = Assert.Throws<ArgumentException>(() => NativeJit.Execute(program, (NativeRuntimePolicy)policy));
        _ = Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitCode(program, (NativeRuntimePolicy)policy));
        _ = Assert.Throws<NotSupportedException>(() => NativeModuleCompiler.CreateProducer(program,
            ["Terrum.CodeAnalysis.ReadSourceLength"], "SourceGrantTest",
            (NativeRuntimePolicy)policy));
    }
}
