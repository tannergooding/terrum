using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

public sealed partial class NativeManagedClassModuleTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingClassHandlesRetainAndRetireWithinSelectedRuntimeAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
                public int32 Read() { return Value; }
            }
            public int32 Main() {
                require Leaf.TryCreate(40, out var leaf);
                var duplicate = leaf;
                return duplicate.Read() + 2;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        var code = NativeEmitter.EmitCode(program, policy, agreement);
        Assert.That(code.ManagedRuntimeOffset, Is.EqualTo(-1));
        Assert.That(code.NormalBackingRuntimeOffset, Is.Zero);
        Assert.That(code.Data.Length, Is.EqualTo(24));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task NormalBackingConstructionFailureRetiresUnpublishedControlAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf() { Value = 1; require false; }
            }
            public int32 Main() {
                unsafe {
                    try {
                        require Leaf.TryCreate(out var leaf);
                    } catch {
                        return 42;
                    }
                }
                return 1;
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, NativeRuntimePolicy.CheckedExceptions,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions, agreement)).ConfigureAwait(true), Is.EqualTo(42));
    }
}
