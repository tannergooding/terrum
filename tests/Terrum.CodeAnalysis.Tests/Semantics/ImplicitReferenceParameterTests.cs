// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class ImplicitReferenceParameterTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ReadsAndForwardsTheCallerPlace(NativeRuntimePolicy policy)
    {
        var source = """
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Forward(implicit ref<int32> value) { return Read(value); }
            int32 Main() {
                mut int32 value = 41;
                return Forward(value);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(program.Functions[0].Variables[0].IsImplicitRef, Is.True);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(41));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "implicit-ref-test"));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Read"]));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void EarlierLoanObservesMutationFromLaterArgument(NativeRuntimePolicy policy)
    {
        var source = """
            int32 Replace(out int32 value) {
                value = 42;
                return 0;
            }
            int32 Observe(implicit ref<int32> value, int32 ignored) {
                return value;
            }
            int32 Main() {
                mut int32 value = 7;
                return Observe(value, Replace(out value));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void IndexedHostBufferLoanObservesMutationFromLaterArgument(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Replace() {
                Host.Values[0] = 42;
                return 0;
            }
            int32 Observe(implicit ref<int32> value, int32 ignored) { return value; }
            int32 Main() { return Observe(Host.Values[0], Replace()); }
            """),
            [new HostBuffer("Values", PrimitiveType.Int32, [7], true)],
            (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void IndexedLexicalViewLoanRetainsItsScopedBacking(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Observe(implicit ref<int32> value) { return value; }
            int32 Read(OwnedInt32Buffer owner) {
                ReadonlyInt32BufferView view = owner.Slice(0, owner.Count);
                return Observe(view.Read(0));
            }
            int32 Main() {
                require OwnedInt32Buffer.TryCreate(1, out var raw);
                mut OwnedInt32Buffer value = move raw;
                require value.TryAppend(42);
                return Read(move value);
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void NestedFieldCanBeReborrowedFromAnImplicitReference(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [Blittable] [Layout(LayoutKind.Sequential)] public struct Pair {
                public int32 Value;
                public Pair(int32 value) { Value = value; }
            }
            int32 Observe(implicit ref<int32> value) { return value; }
            int32 Reborrow(implicit ref<Pair> pair) { return Observe(pair.Value); }
            int32 Main() {
                Pair pair = new Pair(42);
                return Reborrow(pair);
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ReorderedNamedCallPassesTheReferenceAddressOnTheStack(NativeRuntimePolicy policy)
    {
        var source = """
            int32 ReadSix(int32 first, int32 second, int32 third, int32 fourth,
                implicit ref<int32> value, implicit ref<int32> materialized) {
                return value + materialized + first + second + third + fourth;
            }
            int32 Main() {
                mut int32 value = 37;
                return ReadSix(value: value, materialized: 5 + 6, fourth: 4,
                    third: 3, second: 2, first: 1);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(58));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void MaterializesValueExpressionsForReadonlyReferences(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() { return Read(40 + 2); }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var main = global::System.Linq.Enumerable.Single(program.Functions, static function => function.Name == "Main");
        Assert.That(global::System.Linq.Enumerable.Any(main.Variables,static variable => variable.IsImplicitReferenceTemporary),Is.True);
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(42));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "implicit-ref-temp-test"));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Read"]));

        var mainIndex = program.Functions.IndexOf(main);
        var call = ((global::System.Func<BoundCall>)(() => { var __nunitActual8 = ((global::System.Func<BoundReturn>)(() => { var __nunitActual7 = main.Body.Statements[0]; Assert.That(__nunitActual7, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual7!; }))().Value; Assert.That(__nunitActual8, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual8!; }))();
        var temporary = call.ImplicitReferenceTemporary(0);
        var forgedMain = main with {
            Variables = main.Variables.SetItem(temporary,
                main.Variables[temporary] with { Type = PrimitiveType.UInt32 }),
        };
        Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(program with {
            Functions = program.Functions.SetItem(mainIndex, forgedMain),
        }));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void MaterializedExpressionIsEvaluatedOnceBeforeLaterArguments(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Bump(int32 previous, out int32 value) {
                value = previous + 1;
                return value;
            }
            int32 Observe(implicit ref<int32> value, int32 later) {
                return value + later;
            }
            int32 Main() {
                mut int32 value = 7;
                return Observe(Bump(value, out value), Bump(value, out value));
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(17));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ReorderedNamedArgumentsPreserveImplicitReferenceEvaluationOrder(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Replace(out int32 value) {
                value = 42;
                return 7;
            }
            int32 Observe(int32 current, implicit ref<int32> snapshot) {
                return current * 100 + snapshot;
            }
            int32 Main() {
                mut int32 value = 1;
                return Observe(snapshot: value + 1, current: Replace(out value));
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(702));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void ReusesMaterializationSlotAcrossLoopIterations(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() {
                mut int32 index = 0;
                mut int32 sum = 0;
                while (index < 3) {
                    sum = sum + Read(index + 10);
                    index = index + 1;
                }
                return sum;
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(33));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
    [TestCase("bool","true","value ? 42 : 0",NativeRuntimePolicy.FailFast)]
    [TestCase("char16","char16(42)","int32(value)",NativeRuntimePolicy.FailFast)]
    [TestCase("int32","40 + 2","value",NativeRuntimePolicy.FailFast)]
    [TestCase("uint32","uint32(42)","int32(value)",NativeRuntimePolicy.FailFast)]
    [TestCase("int64","int64(42)","int32(value)",NativeRuntimePolicy.FailFast)]
    [TestCase("uint64","uint64(42)","int32(value)",NativeRuntimePolicy.FailFast)]
    [TestCase("float32","float32(42)","value == float32(42) ? 42 : 0",NativeRuntimePolicy.FailFast)]
    [TestCase("float64","float64(42)","value == float64(42) ? 42 : 0",NativeRuntimePolicy.FailFast)]
    [TestCase("bool","true","value ? 42 : 0",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("char16","char16(42)","int32(value)",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("int32","40 + 2","value",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("uint32","uint32(42)","int32(value)",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("int64","int64(42)","int32(value)",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("uint64","uint64(42)","int32(value)",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("float32","float32(42)","value == float32(42) ? 42 : 0",NativeRuntimePolicy.CheckedExceptions)]
    [TestCase("float64","float64(42)","value == float64(42) ? 42 : 0",NativeRuntimePolicy.CheckedExceptions)]
    public void BorrowsAndMaterializesSupportedScalarReferences(string type, string expression, string read,
        NativeRuntimePolicy policy)
    {
        var source = $$"""
            int32 Read(implicit ref<{{type}}> value) { return {{read}}; }
            int32 Main() {
                mut {{type}} local = {{expression}};
                return Read(local) + Read({{expression}});
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(84));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }

    [Test]
    public void ImplicitReferenceParameterIsReadonly()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Assign(implicit ref<int32> value) {
                value = 42;
                return value;
            }
            int32 Main() {
                mut int32 value = 7;
                return Assign(value);
            }
            """));

        Assert.That(compilation.HasErrors, Is.True);
    }

    [Test]
    public void ImplicitReferenceCannotBorrowAnUninitializedLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Read(implicit ref<int32> value) { return value; }
            int32 Main() {
                init int32 value;
                return Read(value);
            }
            """));

        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3016"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void BorrowsAndMaterializesNonUniqueAggregateReferences(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Triple {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public Triple(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                }
            }
            struct Quad {
                public int32 First;
                public int32 Second;
                public int32 Third;
                public int32 Fourth;
                public Quad(int32 value) {
                    First = value;
                    Second = value + 1;
                    Third = value + 2;
                    Fourth = value + 3;
                }
            }
            int32 Read(implicit ref<Triple> value) {
                return value.First + value.Second + value.Third;
            }
            Triple Copy(implicit ref<Triple> value) { return value; }
            int32 Forward(implicit ref<Triple> value) { return Read(value); }
            int32 ReadQuad(implicit ref<Quad> value) {
                return value.First + value.Second + value.Third + value.Fourth;
            }
            Quad CopyQuad(implicit ref<Quad> value) { return value; }
            int32 Main() {
                mut Triple value = new Triple(10);
                Triple copy = Copy(value);
                mut Quad large = new Quad(20);
                Quad largeCopy = CopyQuad(large);
                return Forward(value) + Read(new Triple(40)) + copy.Third
                    + ReadQuad(large) + largeCopy.Fourth;
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = compilation.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(277));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(program, "implicit-ref-aggregate-test"));
        Assert.Throws<ModuleContractException>(() => ModuleCompiler.CreateProducer(program, ["Read"]));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public void StructFieldPlacesAreBorrowedAndObserveLaterArgumentWrites(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Inner {
                public mut int32 Value;
                public Inner(int32 value) { Value = value; }
            }
            struct Triple {
                public mut Inner Nested;
                public int32 Second;
                public int32 Third;
                public Triple(int32 value) {
                    Nested = new Inner(value);
                    Second = value + 1;
                    Third = value + 2;
                }
                public mut int32 Change() { Nested.Value = 99; return 0; }
            }
            int32 Read(implicit ref<int32> value, int32 ignored) { return value; }
            int32 Main() {
                mut Triple value = new Triple(40);
                return Read(value.Nested.Value, value.Change());
            }
            """), (BoundRuntimePolicy)policy);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual14 = compilation.Program; Assert.That(__nunitActual14, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual14!; }))();
        Assert.That(NativeJit.Execute(program, policy), Is.EqualTo(99));
        Assert.That(NativeEmitter.Emit(program, policy), Is.Not.Empty);
    }
}
