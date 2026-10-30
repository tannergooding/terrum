using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
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

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingClassHandleFieldsRetainAndReplaceChildrenAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Child {
                public int32 Value;
                public Child(int32 value) { Value = value; }
            }
            mut class Parent {
                public mut Child? First;
                public mut Child? Second;
                public Parent() {
                    require Child.TryCreate(20, out var child);
                    First = child;
                    Second = child;
                }
            }
            public int32 Main() {
                require Parent.TryCreate(out var parent);
                require Child.TryCreate(22, out var replacement);
                parent.First = replacement;
                mut int32 result = 1;
                if (parent.First is some first) {
                    if (parent.Second is some second) {
                        result = first.Value + second.Value + 2;
                    }
                }
                return result;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(44));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(44));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingRetiresNestedHandleChainIterativelyAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Link {
                public int32 Value;
                public mut write Link? Next;
                public Link(int32 value) {
                    Value = value;
                    Next = null;
                    if (value > 0) {
                        require Link.TryCreate(value - 1, out var next);
                        Next = next;
                    }
                }
            }
            public int32 Main() {
                require Link.TryCreate(64, out var head);
                return head.Value;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(64));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(64));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingLocalReferencesUseSelectedDescriptorLayoutAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut int32 value = int32(40);
                mut int32 second = int32(0);
                mut write ref<int32> target = ref value;
                mut int32 observed = int32(0);
                {
                    ref<int32> readonlyTarget = ref *target;
                    *target = int32(41);
                    observed = *readonlyTarget;
                }
                target = ref second;
                *target = int32(42);
                mut Cell cell = new Cell(int32(0));
                write ref<int32> fieldTarget = ref cell.Value;
                *fieldTarget = observed + second;
                return cell.Value;
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
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(83));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(83));
    }

    [Test]
    public void NormalBackingReferenceCallAbiRemainsFailClosed()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            void Set(write ref<int32> target) { *target = int32(42); }
            int32 Main() {
                mut int32 value = int32(0);
                Set(ref value);
                return value;
            }
            """), new CompilationOptions {
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.Throws<NotSupportedException>(() =>
            NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast, agreement));
        Assert.Throws<NotSupportedException>(() =>
            NativeJit.ExecuteWithData(program, NativeRuntimePolicy.FailFast,
                nativeLoweringAgreement: agreement));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NormalBackingProgramCannotEnterPrivateNativeModuleGraph(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            public int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                return leaf.Value;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(() => NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["Main"], "normal-backing-isolation", policy),
            Throws.ArgumentException);
    }

    [Test]
    public async Task NormalBackingConstructionFailureRetiresChildFieldsAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Child {
                public int32 Value;
                public Child(int32 value) { Value = value; }
            }
            mut class Parent {
                public mut Child? First;
                public mut Child? Second;
                public Parent() {
                    require Child.TryCreate(20, out var first);
                    First = first;
                    require Child.TryCreate(22, out var second);
                    Second = second;
                    require false;
                }
            }
            public int32 Main() {
                unsafe {
                    try {
                        require Parent.TryCreate(out var parent);
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
