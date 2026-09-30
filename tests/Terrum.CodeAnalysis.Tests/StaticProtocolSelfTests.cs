// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolSelfTests
{
    internal const string Source = """
        interface Addition<this TLeft, TRight, TResult> {
            static TResult operator +(TLeft left, TRight right);
        }
        interface Negate<this T, R> {
            [NoThrow] static R operator -(T value);
        }
        interface Ordinary<A, B, C> {
            static C operator *(A left, B right);
        }
        struct Value : Addition<Value, Value>, Negate<Value>, Ordinary<Value, Value, Value> {
            public int32 Number;
            [NoThrow] public Value(int32 number) { Number = number; }
            public static Value operator +(Value left, Value right) {
                return new Value(left.Number + right.Number);
            }
            [NoThrow] public static Value operator -(Value value) {
                return value;
            }
            public static Value operator *(Value left, Value right) {
                return new Value(left.Number * right.Number);
            }
        }
        [NoThrow] T Preserve<T>(T value) where T : Negate<T>, [Copy] {
            return -value;
        }
        T Calculate<T>(T left, T right)
            where T : Addition<T, T>, Negate<T>, Ordinary<T, T, T>, [Copy] {
            return left + right * Preserve(right);
        }
        int32 Main() { return Calculate(new Value(38), new Value(2)).Number; }
        """;

    [Fact]
    public void SelfAndOrdinaryConformancesSpecializeTheSameNominalOwner()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            static function => function.Name == "Calculate<Value>");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SelfBoundProtocolsExecuteAotAsync(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.Emit(Assert.IsType<BoundProgram>(compilation.Program), policy);
        Assert.Equal(42, await StaticProtocolTests.RunAsync(image).ConfigureAwait(true));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void SelfBoundComparisonPreservesBoolResultAndWitnessIdentity(NativeRuntimePolicy policy)
    {
        const string Comparison = """
            interface Less<this T, U, R> { static R operator <(T left, U right); }
            interface AlsoLess<this T, U, R> { static R operator <(T left, U right); }
            struct Box : Less<Box, bool>, AlsoLess<Box, bool> {
                public int32 Number;
                public Box(int32 number) { Number = number; }
                public static bool operator <(Box left, Box right) { return left.Number < right.Number; }
            }
            bool Check<T>(T left, T right) where T : Less<T, bool>, [Copy] {
                return left < right;
            }
            int32 Main() {
                if (Check(new Box(1), new Box(2))) { return 42; }
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Comparison), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program), policy));
        var missingWitness = Comparison.Replace("Less<Box, bool>, AlsoLess<Box, bool>",
            "AlsoLess<Box, bool>", StringComparison.Ordinal);
        var rejected = Compilation.Create(SyntaxTree.Parse(missingWitness), (BoundRuntimePolicy)policy);
        Assert.Contains(rejected.Diagnostics, static diagnostic => diagnostic.Code == "TRM3007");
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public async Task OldNoSelfProducerServesPersistedConsumersAfterSelfBoundReplacementAsync(
        NativeRuntimePolicy policy)
    {
        const string Consumer = "int32 Main() { return Read(38); }";
        var newSource = Source.Replace("int32 Main() { return Calculate(new Value(38), new Value(2)).Number; }",
            "public int32 Read(int32 n) { return Calculate(new Value(n), new Value(2)).Number + 2; }",
            StringComparison.Ordinal);
        var oldSource = newSource.Replace("interface Addition<this TLeft, TRight, TResult>",
                "interface Addition<TLeft, TRight, TResult>", StringComparison.Ordinal)
            .Replace("interface Negate<this T, R>", "interface Negate<T, R>", StringComparison.Ordinal)
            .Replace("Addition<Value, Value>", "Addition<Value, Value, Value>", StringComparison.Ordinal)
            .Replace("Negate<Value>", "Negate<Value, Value>", StringComparison.Ordinal)
            .Replace("Addition<T, T>", "Addition<T, T, T>", StringComparison.Ordinal)
            .Replace("Negate<T>", "Negate<T, T>", StringComparison.Ordinal)
            .Replace(".Number + 2;", ".Number;", StringComparison.Ordinal);
        var sourcePolicy = (BoundRuntimePolicy)policy;
        var oldLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(oldSource), sourcePolicy);
        var newLibrary = Compilation.CreateLibrary(SyntaxTree.Parse(newSource), sourcePolicy);
        Assert.False(oldLibrary.HasErrors, string.Join(Environment.NewLine, oldLibrary.Diagnostics));
        Assert.False(newLibrary.HasErrors, string.Join(Environment.NewLine, newLibrary.Diagnostics));
        var oldProgram = Assert.IsType<BoundProgram>(oldLibrary.Program);
        var newProgram = Assert.IsType<BoundProgram>(newLibrary.Program);
        var oldProducer = ModuleCompiler.CreateProducer(oldProgram, ["Read"]);
        var replacement = ModuleCompiler.CreateProducer(newProgram, ["Read"]);
        var oldConsumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(oldProducer), sourcePolicy);
        Assert.False(oldConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, oldConsumerCompilation.Diagnostics));
        var oldConsumer = ModuleCompiler.CreateConsumer(
            Assert.IsType<BoundProgram>(oldConsumerCompilation.Program), oldProducer);
        var snapshot = SHA256.HashData(oldConsumer);
        Assert.Equal(42, NativeJit.Execute(ModuleCompiler.Link(oldConsumer, oldProducer), policy));
        Assert.Equal(44, NativeJit.Execute(ModuleCompiler.Link(oldConsumer, replacement), policy));
        Assert.Equal(snapshot, SHA256.HashData(oldConsumer));

        var oldNative = NativeModuleCompiler.CreateProducer(oldProgram, ["Read"], "NodeB", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([oldNative]), sourcePolicy);
        Assert.False(nativeConsumerCompilation.HasErrors,
            string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            Assert.IsType<BoundProgram>(nativeConsumerCompilation.Program), [oldNative], policy);
        var nativeSnapshot = SHA256.HashData(nativeConsumer);
        var newNative = NativeModuleCompiler.CreateProducer(newProgram, ["Read"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [newNative]);
        Assert.Equal(44, await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [newNative],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true));
        Assert.Equal(nativeSnapshot, SHA256.HashData(nativeConsumer));
    }

    [Theory]
    [InlineData("Addition<Value, Value>", "Addition<int32, Value>", "TRM3007")]
    [InlineData("Addition<Value, Value>", "Addition<Value, int32>", "TRM3007")]
    [InlineData("Negate<Value>", "Negate<int32>", "TRM3007")]
    [InlineData("Addition<Value, Value>", "Addition<Value, Value, Value>", "TRM3007")]
    [InlineData("Negate<Value>", "Negate<Value, Value>", "TRM3007")]
    [InlineData("Addition<T, T>", "Addition<T, T, T>", "TRM3007")]
    [InlineData("Negate<T>", "Negate<T, T>", "TRM3007")]
    [InlineData("Addition<T, T>", "Addition<int32, T>", "TRM3007")]
    [InlineData("interface Addition<this TLeft, TRight, TResult>",
        "interface Addition<TLeft, this TRight, TResult>", "TRM3001")]
    [InlineData("interface Addition<this TLeft, TRight, TResult>",
        "interface Addition<this TLeft, this TRight, TResult>", "TRM3001")]
    [InlineData("interface Addition<this TLeft, TRight, TResult>",
        "interface Addition<this TLeft, TLeft, TResult>", "TRM3001")]
    [InlineData("interface Addition<this TLeft, TRight, TResult>",
        "interface Addition<this TLeft, TResult>", "TRM3001")]
    [InlineData("static TResult operator +(TLeft left, TRight right)",
        "abstract static TResult operator +(TLeft left, TRight right)", "TRM1002")]
    [InlineData("static TResult operator +(TLeft left, TRight right)",
        "static abstract TResult operator +(TLeft left, TRight right)", "TRM1002")]
    public void RejectsMismatchedSelfOrRequirementShape(string before, string after, string code)
    {
        Assert.Contains(before, Source, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(Source.Replace(before, after, StringComparison.Ordinal)));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void AnAbstractNameIsAnOrdinaryIdentifier()
    {
        const string Source = "interface Abstract<T, R> { static R operator +(T abstract); } int32 Main() { int32 abstract = 42; return abstract; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.False(Compilation.Create(tree).HasErrors);
    }

    [Fact]
    public void AFalseNoThrowSelfWitnessIsRejected()
    {
        var source = Source.Replace("[NoThrow] public static Value operator -(Value value)",
            "public static Value operator -(Value value)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3007");
    }

    [Fact]
    public void DefinitionProofCannotInventAnUnlistedWitness()
    {
        var source = Source.Replace("Addition<T, T>, Negate<T>, Ordinary<T, T, T>, [Copy]",
            "Addition<T, T>, Negate<T>, [Copy]", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3007");
    }
}
