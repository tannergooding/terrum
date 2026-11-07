using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Native.Modules;

internal sealed partial class NativeManagedClassModuleTests
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
        Assert.That(code.Data.Length, Is.EqualTo(32));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
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
        Assert.That(code.Data.Length, Is.EqualTo(32));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(83));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(83));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalLocalReferencesCopyAndRebindAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = int32(40);
                mut ref<int32>? maybe = null;
                require maybe is null;
                maybe ??= ref value;
                mut ref<int32>? copy = maybe;
                maybe = null;
                if (maybe is var? state) {
                    require state is null;
                }
                if (copy is some selected) {
                    return *selected + int32(2);
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalReferenceKeepsClassSubplaceAliveAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut ref<int32>? selected = null;
                {
                    require Cell.TryCreate(40, out var cell);
                    selected = ref cell.Value;
                }
                if (selected is some value) {
                    return *value + int32(2);
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalReferenceCoalescingSelectsDescriptorAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = int32(40);
                mut ref<int32>? present = ref value;
                mut ref<int32>? absent = null;
                return *(present ?? ref value) + *(absent ?? ref value)
                    + *(null ?? ref value) - int32(78);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalReferenceFieldRetainsClassSubplaceAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Holder {
                public mut ref<int32>? Selected;
                public Holder() { Selected = null; }
            }
            int32 Main() {
                require Holder.TryCreate(out var holder);
                {
                    require Cell.TryCreate(40, out var cell);
                    holder.Selected ??= ref cell.Value;
                }
                if (holder.Selected is some selected) {
                    return *selected + int32(2);
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task NormalBackingOptionalReferenceFieldRollsBackSelfHoldAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public mut int32 Value;
                public mut ref<int32>? Selected;
                public Cell() {
                    Value = int32(40);
                    Selected = ref this.Value;
                    require false;
                }
            }
            int32 Main() {
                unsafe {
                    try {
                        require Cell.TryCreate(out var cell);
                    } catch {
                        return int32(42);
                    }
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program,
            NativeRuntimePolicy.CheckedExceptions,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions, agreement)).ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task NormalBackingOptionalReferenceRetainsAcrossCheckedUnwindAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Main() {
                mut ref<int32>? selected = null;
                unsafe {
                    try {
                        {
                            require Cell.TryCreate(40, out var cell);
                            selected = ref cell.Value;
                            require false;
                        }
                    } catch {
                        if (selected is some value) {
                            return *value + int32(2);
                        }
                    }
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = BoundRuntimePolicy.CheckedExceptions,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program,
            NativeRuntimePolicy.CheckedExceptions,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions, agreement)).ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalReferenceAggregateCopyRetainsLeavesAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            class Cell {
                public int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            struct OptionalReferences {
                public mut ref<int32>? First;
                public mut ref<int32>? Second;
            }
            mut class Holder {
                public mut OptionalReferences References;
                public Holder() {
                    References.First = null;
                    References.Second = null;
                }
            }
            int32 Main() {
                require Holder.TryCreate(out var holder);
                init mut OptionalReferences copy;
                {
                    require Cell.TryCreate(40, out var cell);
                    holder.References.First = ref cell.Value;
                    holder.References.Second = ref cell.Value;
                    copy = holder.References;
                    holder.References.First = null;
                    holder.References.Second = null;
                }
                if (copy.Second is some selected) {
                    return *selected + int32(2);
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public void NormalBackingOptionalReferenceAggregateTypeBinds()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct OptionalReferences {
                public mut ref<int32>? First;
                public mut ref<int32>? Second;
                public OptionalReferences() {
                    First = null;
                    Second = null;
                }
            }
            int32 Main() { return int32(0); }
            """), new CompilationOptions { NativeLoweringAgreement = agreement });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [TestCase("gc ref<int32>?")]
    [TestCase("ref<init int32>?")]
    public void NormalBackingOptionalReferencesRejectUnsupportedQualifiers(string type)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse(
            $"int32 Main() {{ {type} value = null; return 0; }}"),
            new CompilationOptions { NativeLoweringAgreement = agreement });
        Assert.That(compilation.HasErrors, Is.True);
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingClassSubplaceReferencesRetainSelectedBackingAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
            }
            mut class Parent {
                public mut int32 Marker;
                public mut write Child Inner;
                public Parent() {
                    Marker = int32(0);
                    require Child.TryCreate(int32(40), out var child);
                    Inner = child;
                }
            }
            public int32 Main() {
                mut int32 parentValue = int32(0);
                mut int32 fallback = int32(0);
                mut write ref<int32> childValue = ref fallback;
                {
                    require Parent.TryCreate(out var parent);
                    {
                        write ref<int32> marker = ref parent.Marker;
                        *marker = int32(40);
                        parentValue = *marker;
                    }
                    childValue = ref parent.Inner.Value;
                    require Child.TryCreate(int32(99), out var replacement);
                    parent.Inner = replacement;
                }
                *childValue = int32(2);
                return parentValue + *childValue;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingStoredReferenceFieldsRetainAfterSourceHandleEndsAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Owner {
                public mut int32 Local;
                public mut write ref<int32> Item;
                public Owner() {
                    Local = int32(0);
                    Item = ref Local;
                }
            }
            public int32 Main() {
                mut int32 result = int32(0);
                {
                    require Owner.TryCreate(out var owner);
                    var mirror = owner;
                    require Owner.TryCreate(out var secondOwner);
                    result = *(owner.Item) + *(secondOwner.Item);
                    {
                        require Cell.TryCreate(int32(40), out var cell);
                        require Cell.TryCreate(int32(50), out var second);
                        mirror.Item = ref cell.Value;
                        secondOwner.Item = ref second.Value;
                    }
                    *(owner.Item) = int32(42);
                    result = *(owner.Item) + *(secondOwner.Item);
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
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(92));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(92));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingNestedClassReferenceFieldsRetainSelectedObjectAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Box {
                public mut int32 Local;
                public mut write ref<int32> Item;
                public Box() {
                    Local = int32(0);
                    Item = ref Local;
                }
            }
            mut class Parent {
                public mut write Box Child;
                public Parent() {
                    require Box.TryCreate(out var child);
                    Child = child;
                }
            }
            public int32 Main() {
                mut int32 result = int32(0);
                {
                    require Parent.TryCreate(out var parent);
                    var mirror = parent;
                    require Parent.TryCreate(out var secondParent);
                    {
                        require Cell.TryCreate(int32(40), out var cell);
                        require Cell.TryCreate(int32(50), out var secondCell);
                        mirror.Child.Item = ref cell.Value;
                        secondParent.Child.Item = ref secondCell.Value;
                    }
                    *(parent.Child.Item) = int32(42);
                    *(secondParent.Child.Item) = int32(52);
                    result = *(parent.Child.Item) + *(secondParent.Child.Item);
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
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(94));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(94));
    }

    [Test]
    public async Task NormalBackingFailedConstructorDrainsSelfReferenceFieldsAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct References {
                public mut write ref<int32> First;
                public mut write ref<int32> Second;
            }
            mut class Owner {
                public mut int32 Local;
                public mut write ref<int32> Item;
                public mut References Items;
                public Owner() {
                    Local = int32(1);
                    Item = ref Local;
                    Items.First = ref Local;
                    Items.Second = ref Local;
                    require false;
                }
            }
            public int32 Main() {
                unsafe {
                    try {
                        require Owner.TryCreate(out var owner);
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
        Assert.That(NativeJit.ExecuteWithData(program,
            NativeRuntimePolicy.CheckedExceptions,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions, agreement)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public void NormalBackingReferenceFieldsRejectLocalBackingEscape()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Owner {
                public mut int32 Local;
                public mut write ref<int32> Item;
                public Owner() {
                    Local = int32(0);
                    {
                        mut int32 temporary = int32(41);
                        Item = ref temporary;
                    }
                }
            }
            public int32 Main() {
                require Owner.TryCreate(out var owner);
                return 42;
            }
            """), new CompilationOptions {
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3030"), Is.True,
            string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public async Task NormalBackingInlineReferenceAggregateReleasesOnCheckedUnwindAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct References {
                public mut write ref<int32> First;
                public mut write ref<int32> Second;
            }
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Owner {
                public mut int32 Local;
                public mut References Items;
                public Owner() {
                    Local = int32(0);
                    Items.First = ref Local;
                    Items.Second = ref Local;
                }
            }
            public int32 Main() {
                require Owner.TryCreate(out var owner);
                {
                    require Cell.TryCreate(int32(10), out var first);
                    require Cell.TryCreate(int32(20), out var second);
                    owner.Items.First = ref first.Value;
                    owner.Items.Second = ref second.Value;
                }
                unsafe {
                    try {
                        mut References copy = owner.Items;
                        require false;
                    } catch {
                        *(owner.Items.First) = int32(42);
                        return *(owner.Items.First);
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
        Assert.That(NativeJit.ExecuteWithData(program,
            NativeRuntimePolicy.CheckedExceptions,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions, agreement)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [Test]
    public async Task NormalBackingStoredReferenceAggregateReleasesOnCheckedUnwindAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct References {
                public mut write ref<int32> First;
                public mut write ref<int32> Second;
            }
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Owner {
                public mut int32 Local;
                public mut References Items;
                public Owner() {
                    Local = int32(0);
                    Items.First = ref Local;
                    Items.Second = ref Local;
                }
            }
            public int32 Main() {
                require Owner.TryCreate(out var owner);
                {
                    require Cell.TryCreate(int32(10), out var first);
                    require Cell.TryCreate(int32(20), out var second);
                    owner.Items.First = ref first.Value;
                    owner.Items.Second = ref second.Value;
                }
                unsafe {
                    try {
                        mut References copy = owner.Items;
                        require false;
                        *(copy.First) = int32(1);
                    } catch {
                        *(owner.Items.First) = int32(42);
                        return *(owner.Items.First);
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
        Assert.That(NativeJit.ExecuteWithData(program,
            NativeRuntimePolicy.CheckedExceptions,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions, agreement)).ConfigureAwait(true),
            Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingInlineReferenceFieldsCopyReplaceAndRetireAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct References {
                public mut write ref<int32> First;
                public mut write ref<int32> Second;
            }
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Owner {
                public mut int32 Local;
                public mut References Items;
                public Owner() {
                    Local = int32(0);
                    Items.First = ref Local;
                    Items.Second = ref Local;
                }
            }
            public int32 Main() {
                mut int32 result = int32(0);
                {
                    require Owner.TryCreate(out var owner);
                    require Owner.TryCreate(out var secondOwner);
                    {
                        require Cell.TryCreate(int32(10), out var first);
                        require Cell.TryCreate(int32(20), out var second);
                        require Cell.TryCreate(int32(30), out var third);
                        require Cell.TryCreate(int32(40), out var fourth);
                        owner.Items.First = ref first.Value;
                        owner.Items.Second = ref second.Value;
                        secondOwner.Items.First = ref third.Value;
                        secondOwner.Items.Second = ref fourth.Value;
                        mut References copy = owner.Items;
                        owner.Items = copy;
                        copy = secondOwner.Items;
                        owner.Items = copy;
                        *(copy.First) = int32(31);
                        *(copy.Second) = int32(41);
                    }
                    result = *(owner.Items.First) + *(owner.Items.Second);
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
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(72));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(72));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingInlineClassSubplaceReferencesRetainContainingObjectAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Holder {
                public mut Cell Data;
                public Holder() { Data = new Cell(int32(40)); }
            }
            public int32 Main() {
                mut int32 fallback = int32(0);
                mut write ref<int32> value = ref fallback;
                {
                    require Holder.TryCreate(out var holder);
                    value = ref holder.Data.Value;
                }
                *value = int32(42);
                return *value;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingClassHandleSlotReferenceRetainsContainingObjectAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
            }
            mut class Holder {
                public write Child Item;
                public Holder() {
                    require Child.TryCreate(int32(42), out var child);
                    Item = child;
                }
            }
            public int32 Main() {
                require Child.TryCreate(int32(0), out var fallback);
                mut ref<write Child> item = ref fallback;
                {
                    require Holder.TryCreate(out var holder);
                    item = ref holder.Item;
                }
                var child = *item;
                return child.Value;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingWritableClassHandleSlotReferencesRetainAndReplaceAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Child {
                public mut int32 Value;
                public Child(int32 value) { Value = value; }
            }
            mut class Holder {
                public mut write Child Item;
                public Holder() {
                    require Child.TryCreate(int32(10), out var child);
                    Item = child;
                }
            }
            public int32 Main() {
                require Child.TryCreate(int32(0), out var original);
                mut write Child local = original;
                mut write ref<write Child> item = ref local;
                require Child.TryCreate(int32(41), out var first);
                *item = first;
                {
                    require Holder.TryCreate(out var holder);
                    item = ref holder.Item;
                    require Child.TryCreate(int32(42), out var replacement);
                    *item = replacement;
                }
                var child = *item;
                return child.Value;
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task NormalBackingClassSubplaceReferenceReleasesOnCheckedUnwindAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Leaf {
                public mut int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            public int32 Main() {
                unsafe {
                    try {
                        require Leaf.TryCreate(int32(40), out var leaf);
                        {
                            write ref<int32> target = ref leaf.Value;
                            require false;
                            *target = int32(41);
                        }
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
    public async Task NormalBackingReferenceCallsTransferSupportBearingInputsAsync(
        NativeRuntimePolicy policy)
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
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingReferenceCallResultsRetainAcrossSourceHandleEndAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            mut class Holder {
                public mut int32 Local;
                public mut write ref<int32> Item;
                public Holder() {
                    Local = int32(0);
                    Item = ref Local;
                }
            }
            [return: DependsOn(nameof(source))]
            write ref<int32> Identity(write ref<int32> source) { return source; }
            int32 Main() {
                mut int32 result = int32(0);
                {
                    require Holder.TryCreate(out var holder);
                    {
                        require Cell.TryCreate(int32(40), out var cell);
                        holder.Item = Identity(Identity(ref cell.Value));
                    }
                    *holder.Item = *holder.Item + int32(2);
                    result = *holder.Item;
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
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingInlineReferenceCallResultCanBeReadAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [return: DependsOn(nameof(value))]
            ref<int32> Identity(ref<int32> value) { return value; }
            int32 Main() {
                int32 input = int32(42);
                return *Identity(ref input);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingReferenceCallsStageArgumentsBeforeRebindingAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(write ref<int32> value, int32 ignored) { return *value; }
            [param(target): Captures(nameof(replacement))]
            int32 Rebind(write ref<write ref<int32>> target, write ref<int32> replacement) {
                *target = replacement;
                return int32(0);
            }
            int32 Main() {
                mut int32 first = int32(41);
                mut int32 second = int32(1);
                mut write ref<int32> selected = ref first;
                return Read(selected, Rebind(ref selected, ref second));
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(41));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(41));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingAggregateReferenceCallTransfersSupportAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct ReferenceValue {
                public mut write ref<int32> First;
                public mut write ref<int32> Second;
            }
            mut class Owner {
                public mut int32 First;
                public mut int32 Second;
                public mut ReferenceValue Item;
                public Owner() {
                    First = int32(40);
                    Second = int32(2);
                    Item.First = ref First;
                    Item.Second = ref Second;
                }
            }
            int32 Read(ReferenceValue value) { return *value.First + *value.Second; }
            int32 Main() {
                require Owner.TryCreate(out var owner);
                return Read(owner.Item);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalReferenceAggregateCallPreservesPresenceAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct OptionalReference {
                public mut ref<int32>? Value;
            }
            mut class Owner {
                public mut int32 Local;
                public mut OptionalReference Item;
                public Owner() {
                    Local = int32(40);
                    Item.Value = ref Local;
                }
            }
            int32 Read(OptionalReference value) {
                if (value.Value is some selected) {
                    return *selected + int32(2);
                }
                return int32(1);
            }
            int32 Main() {
                require Owner.TryCreate(out var owner);
                return Read(owner.Item);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task NormalBackingOptionalReferenceCallHandoffPreservesPresenceAsync(
        NativeRuntimePolicy policy)
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            [return: DependsOn(nameof(source))]
            ref<int32>? Identity(ref<int32>? source) { return source; }
            int32 Main() {
                mut ref<int32>? selected = null;
                {
                    require Cell.TryCreate(int32(40), out var cell);
                    selected = ref cell.Value;
                }
                var copied = Identity(selected);
                var absent = Identity(null);
                if (absent is null) {
                    if (copied is some value) {
                        return *value + int32(2);
                    }
                }
                return int32(1);
            }
            """), new CompilationOptions {
            RuntimePolicy = (BoundRuntimePolicy)policy,
            NativeLoweringAgreement = agreement,
        });
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = compilation.Program!;
        Assert.That(NativeJit.ExecuteWithData(program, policy,
            nativeLoweringAgreement: agreement).ExitCode, Is.EqualTo(42));
        Assert.That(await ExecuteAsync(NativeEmitter.Emit(program, policy, agreement))
            .ConfigureAwait(true), Is.EqualTo(42));
    }

    [Test]
    public async Task NormalBackingReferenceCallUnwindReusesCallPlanAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            void Fail(write ref<int32> value) { require false; }
            void Set(write ref<int32> value) { *value = int32(42); }
            int32 Main() {
                mut int32 value = int32(0);
                unsafe {
                    try {
                        Fail(ref value);
                    } catch { }
                }
                Set(ref value);
                return value;
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

    [Test]
    public async Task NormalBackingReferenceCallReleasesStagedInputWhenLaterArgumentFailsAsync()
    {
        const string agreement = CompilationOptions.WindowsX64NormalBackingAgreement;
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            mut class Cell {
                public mut int32 Value;
                public Cell(int32 value) { Value = value; }
            }
            int32 Read(write ref<int32> first, write ref<int32> second) {
                return *first + *second;
            }
            [return: DependsOn(nameof(value))]
            write ref<int32> FailArgument(write ref<int32> value) {
                require false;
                return value;
            }
            int32 Main() {
                mut int32 result = int32(0);
                require Cell.TryCreate(int32(40), out var cell);
                unsafe {
                    try {
                        result = Read(ref cell.Value, FailArgument(ref cell.Value));
                    } catch {
                        result = int32(42);
                    }
                }
                return result;
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
