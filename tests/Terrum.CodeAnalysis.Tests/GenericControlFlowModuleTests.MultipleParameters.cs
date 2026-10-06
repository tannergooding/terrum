// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
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
    private const string MultipleCopyValues = """
        struct Left {
            public int32 Number;
            public Left(int32 number) { Number = number; }
        }
        struct Right {
            public int64 Number;
            public int64 Spare;
            public Right(int64 number) { Number = number; Spare = 0; }
        }
        [NoThrow] A First<A, B>(A first, B second) where A : [Copy] where B : [Copy] {
            return first;
        }
        [NoThrow] B Reverse<A, B>(A first, B second) where A : [Copy] where B : [Copy] {
            return First<B, A>(second, first);
        }
        [NoThrow] B ReverseInferred<A, B>(A first, B second) where A : [Copy] where B : [Copy] {
            return First(second, first);
        }
        [NoThrow] bool Publish<A, B>(A first, B second, out A left, out B right)
            where A : [Copy] where B : [Copy] {
            left = first;
            right = second;
            return true;
        }
        A Step<A, B>(A first, B second, int32 remaining)
            where A : [Copy] where B : [Copy] {
            if (remaining == 0) { return first; }
            return Relay<B, A>(second, first, remaining - 1);
        }
        B Relay<A, B>(A first, B second, int32 remaining)
            where A : [Copy] where B : [Copy] {
            if (remaining == 0) { return second; }
            return Step<B, A>(second, first, remaining - 1);
        }
        A Direct<A, B>(A first, B second, int32 remaining)
            where A : [Copy] where B : [Copy] {
            if (remaining == 0) { return first; }
            return Direct<A, B>(first, second, remaining - 1);
        }
        A Triple<A, B, C>(A first, B second, C third)
            where A : [Copy] where B : [Copy] where C : [Copy] {
            return first;
        }
        """;

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OrderedTuplesComposeRecursivelyAndPublishIndependentOutputsAsync(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(MultipleCopyValues + """
            int32 Main() {
                require Publish(new Left(40), new Right(2), out var left, out var right);
                require Publish<Right, Left>(right, left, out var swapped, out var restored);
                var first = First<Left, Right>(restored, swapped);
                var second = Reverse<Right, Left>(swapped, first);
                var recursed = Step<Left, Right>(ReverseInferred(swapped, second), swapped, 2);
                var same = First<Left, Left>(recursed, first);
                var other = First<Right, Left>(swapped, same);
                var triple = Triple(Direct(same, other, 2), other, true);
                return triple.Number + int32(other.Number);
            }
            """), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Contains(program.Functions, function => function.Name == "First<Left, Right>");
        Assert.Contains(program.Functions, function => function.Name == "First<Right, Left>");
        Assert.Contains(program.Functions, function => function.Name == "First<Left, Left>");
        Assert.Contains(program.Functions, function => function.Name == "Step<Left, Right>");
        Assert.Contains(program.Functions, function => function.Name == "Relay<Right, Left>");
        Assert.Contains(program.Functions, function => function.Name == "ReverseInferred<Right, Left>");
        Assert.Contains(program.Functions, function => function.Name == "Triple<Left, Right, bool>");
        Assert.Equal(1, program.Functions.Count(function => function.Name == "First<Left, Right>"));
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(program, policy)).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task TupleSpecializationsStayPrivateAcrossUnchangedConsumerReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Replacement = """
            interface Addition<A, B, C> { static C operator +(A left, B right); }
            interface Multiplication<A, B, C> { static C operator *(A left, B right); }
            struct Left : Addition<Left, Left, Left> {
                public int32 Number;
                public Left(int32 number) { Number = number; }
                public static Left operator +(Left a, Left b) { return new Left(a.Number + b.Number); }
            }
            struct Right : Multiplication<Right, Right, Right> {
                public int64 Number;
                public int64 Spare;
                public Right(int64 number) { Number = number; Spare = 0; }
                public static Right operator *(Right a, Right b) { return new Right(a.Number * b.Number); }
            }
            A Combine<A, B>(A first, B second)
                where A : Addition<A, A, A>, [Copy]
                where B : Multiplication<B, B, B>, [Copy] {
                B check = second * second;
                return first + first;
            }
            public int32 PowerProbe() {
                return Combine(new Left(20), new Right(2)).Number
                    + int32(First<Right, Left>(new Right(2), new Left(1)).Number);
            }
            [NoThrow] A First<A, B>(A first, B second) where A : [Copy] where B : [Copy] {
                return first;
            }
            """;
        var original = ModuleCompiler.CreateProducer(Compile(Stub, policy), ["PowerProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile(
            "int32 Main() { return PowerProbe() + 0; }", policy,
            ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var replacementProgram = Compile(Replacement, policy);
        var replacement = ModuleCompiler.CreateProducer(replacementProgram, ["PowerProbe"]);
        Assert.Equal(63,BinaryPrimitives.ReadInt32LittleEndian(replacement.AsSpan(4, 4)));
        Assert.Empty(ModuleCompiler.CreateReference(replacement).Structures);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Contains(linked.Functions, function => function.Name == "$producer.Combine<Left, Right>");
        Assert.Contains(linked.Functions, function => function.Name == "$producer.First<Right, Left>");
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(Stub, policy),
            ["PowerProbe"], "NodeB", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(replacementProgram,
            ["PowerProbe"], "NodeB", policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return PowerProbe() + 0; }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task MultipleTypeParameterSampleReplacesUnchangedConsumersAsync(NativeRuntimePolicy policy)
    {
        var source = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "samples", "native",
            "multi-type-generics.terrum")).ConfigureAwait(true);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var sample = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(sample, policy)).ConfigureAwait(true));
        var original = ModuleCompiler.CreateProducer(Compile(
            "public int32 GenericPairProbe() { return 40; }", policy), ["GenericPairProbe"]);
        var replacement = ModuleCompiler.CreateProducer(sample, ["GenericPairProbe"]);
        var consumer = ModuleCompiler.CreateConsumer(Compile("""
            struct WidePair {
                public int32 Unrelated;
                public WidePair(int32 value) { Unrelated = value; }
            }
            int32 Main() { return GenericPairProbe() + new WidePair(0).Unrelated; }
            """, policy, ModuleCompiler.CreateReference(original), library: false), original);
        var hash = SHA256.HashData(consumer);
        var linked = ModuleCompiler.Link(consumer, replacement);
        Assert.Equal(["WidePair", "$producer.WidePair"], linked.Structures.Select(static item => item.Name));
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(linked, policy)).ConfigureAwait(true));
        var nativeOriginal = NativeModuleCompiler.CreateProducer(Compile(
            "public int32 GenericPairProbe() { return 40; }", policy),
            ["GenericPairProbe"], "NodeB", policy);
        var nativeReplacement = NativeModuleCompiler.CreateProducer(Compile(source, policy),
            ["GenericPairProbe"], "NodeB", policy);
        var root = NativeModuleCompiler.CreateGraphConsumer(Compile(
            "int32 Main() { return GenericPairProbe(); }", policy,
            NativeModuleCompiler.CreateGraphReference([nativeOriginal]), library: false),
            [nativeOriginal], policy);
        Assert.Equal(42, await NativeModuleCompiler.RunGraphAsync(root, [nativeReplacement],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(42, await NativeModuleCompiler.RunGraphJitAsync(root, [nativeReplacement],
            Apphost(), TimeSpan.FromSeconds(30), policy).ConfigureAwait(true));
        Assert.Equal(hash, SHA256.HashData(consumer));
    }

    [Theory]
    [InlineData("var result = First(new Left(1), 2);", "TRM3011")]
    [InlineData("Left result = First(new Left(1), 2);", "TRM3011")]
    [InlineData("var result = First<Left>(new Left(1), new Right(2));", "TRM3011")]
    [InlineData("var result = First<Left, Right, bool>(new Left(1), new Right(2));", "TRM3011")]
    [InlineData("Left result = First<Left, Right>(new Left(1), new Left(2));", "TRM3007")]
    [InlineData("Right result = First(new Left(1), new Right(2));", "TRM3007")]
    [InlineData("var result = First<Left, Right>(new Left(1), new Right(2));", null)]
    [InlineData("var result = First<Left, _>(new Left(1), new Right(2));", "TRM1001")]
    public void ExplicitArgumentsNeedCompleteListsAndInferredPositionsNeedTypes(
        string body, string? diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(MultipleCopyValues
            + "int32 Main() { " + body + " return 0; }"));
        if (diagnostic is null)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
        }
    }

    [Theory]
    [InlineData("T Identity<T, T>(T a, T b) where T : [Copy] { return a; }")]
    [InlineData("T Identity<T, U>(T a, U b) where T : [Copy] { return a; }")]
    [InlineData("T Identity<T, U>(T a, U b) where T : [Copy] where U : [Copy] where U : [Copy] { return a; }")]
    [InlineData("T Identity<T, U>(T a, U b) where T : [Copy] where V : [Copy] { return a; }")]
    public void AllIndependentFormalsNeedDistinctDeclaredCopyConstraints(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(declaration + "int32 Main() { return 0; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData("return left + right;")]
    [InlineData("return right;")]
    public void EqualConcreteArgumentsCannotRescueCrossParameterOperationsOrCopies(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            interface Addition<A, B, C> { static C operator +(A left, B right); }
            struct Value : Addition<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator +(Value a, Value b) { return new Value(a.Number + b.Number); }
            }
            A Invalid<A, B>(A left, B right) where A : Addition<A, A, A>, [Copy] where B : [Copy] {
            """ + body + "}" + """
            int32 Main() { return Invalid<Value, Value>(new Value(20), new Value(22)).Number; }
            """));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("new Plain(2)")]
    [InlineData("int32(2)")]
    public void EachConcretePositionRequiresItsOwnNominalOperatorWitness(string right)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            interface Multiplication<A, B, C> { static C operator *(A left, B right); }
            struct Plain {
                public int32 Number;
                public Plain(int32 number) { Number = number; }
            }
            struct HasWitness : Multiplication<HasWitness, HasWitness, HasWitness> {
                public int32 Number;
                public HasWitness(int32 number) { Number = number; }
                public static HasWitness operator *(HasWitness a, HasWitness b) {
                    return new HasWitness(a.Number * b.Number);
                }
            }
            A Keep<A, B>(A first, B second)
                where A : [Copy] where B : Multiplication<B, B, B>, [Copy] {
                B ignored = second * second;
                return first;
            }
            int32 Main() { return Keep(new HasWitness(40),
            """ + right + ").Number; }"));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ExistingTypedOutputEstablishesTheSecondIndependentFormal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(MultipleCopyValues + """
            [NoThrow] bool Absent<A, B>(A first, out B result)
                where A : [Copy] where B : [Copy] {
                return false;
            }
            int32 Main() {
                mut Right target = new Right(2);
                if (Absent(new Left(40), out target)) { return 1; }
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Absent<Left, Right>");
    }

    [Theory]
    [InlineData("bool", "true")]
    [InlineData("int32", "int32(2)")]
    [InlineData("int64", "int64(2)")]
    [InlineData("uint64", "uint64(2)")]
    [InlineData("char16", "char16(65)")]
    public void InferredAndExplicitTuplesAcceptIndependentScalarCopyArguments(string type, string value)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(MultipleCopyValues + $$"""
            int32 Main() {
                {{type}} first = Reverse<Left, {{type}}>(new Left(1), {{value}});
                {{type}} second = Reverse(new Left(1), first);
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == $"Reverse<Left, {type}>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResultContextFillsOnlyItsUnresolvedPosition(bool incompatibleTypedArgument)
    {
        var argument = incompatibleTypedArgument ? "int32(42)" : "42";
        var compilation = Compilation.Create(SyntaxTree.Parse(MultipleCopyValues + """
            [NoThrow] B Second<A, B>(A first, B second) where A : [Copy] where B : [Copy] {
                return second;
            }
            int32 Main() {
                int64 result = Second(new Left(1),
            """ + argument + "); return 42; }"));
        if (incompatibleTypedArgument)
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        }
        else
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
            Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
                function => function.Name == "Second<Left, int64>");
        }
    }

    [Fact]
    public void CalleeWitnessMustComeFromTheMatchingCallerFormal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            interface Addition<A, B, C> { static C operator +(A left, B right); }
            struct Value : Addition<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator +(Value a, Value b) { return new Value(a.Number + b.Number); }
            }
            A Needs<A, B>(A first, B second)
                where A : Addition<A, A, A>, [Copy] where B : [Copy] {
                return first + first;
            }
            A Borrow<A, B>(A first, B second)
                where A : [Copy] where B : Addition<B, B, B>, [Copy] {
                return Needs<A, B>(first, second);
            }
            int32 Main() { return Borrow<Value, Value>(new Value(20), new Value(22)).Number; }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoThrowTupleHelpersUseDeclaredCalleeEffect(bool noThrow)
    {
        var source = """
            [NoThrow] A Forward<A, B>(A first, B second)
                where A : [Copy] where B : [Copy] {
                return Pick<B, A>(second, first);
            }
            """ + (noThrow ? "[NoThrow] " : "") + """
            B Pick<A, B>(A first, B second)
                where A : [Copy] where B : [Copy] {
                return second;
            }
            int32 Main() { return Forward<int32, bool>(int32(42), true); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        if (noThrow)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
        }
    }

    [Fact]
    public async Task CheckedTupleFailureDoesNotPublishOutputsAsync()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(MultipleCopyValues + """
            bool Fail<A, B>(A first, B second, out A left, out B right)
                where A : [Copy] where B : [Copy] {
                left = first;
                right = second;
                require false;
                return true;
            }
            int32 Main() {
                unsafe {
                    try {
                        Fail<Left, Right>(new Left(40), new Right(2), out var left, out var right);
                        return 99;
                    }
                    catch { return 42; }
                }
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(42, await ExecuteAsync(NativeEmitter.Emit(program,
            NativeRuntimePolicy.CheckedExceptions)).ConfigureAwait(true));
    }
}
