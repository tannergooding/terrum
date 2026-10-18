// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

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

    [Test]
    public void SelfAndOrdinaryConformancesSpecializeTheSameNominalOwner()
    {
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(Source));
        var compilation = Compilation.Create(tree);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions,static function => function.Name == "Calculate<Value>"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task SelfBoundProtocolsExecuteAotAsync(NativeRuntimePolicy policy)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), runtimePolicy: (BoundRuntimePolicy)policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var image = NativeEmitter.Emit(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))(), policy);
        Assert.That(await StaticProtocolTests.RunAsync(image).ConfigureAwait(true), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(NativeJit.Execute(((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))(), policy), Is.EqualTo(42));
        var missingWitness = Comparison.Replace("Less<Box, bool>, AlsoLess<Box, bool>",
            "AlsoLess<Box, bool>", StringComparison.Ordinal);
        var rejected = Compilation.Create(SyntaxTree.Parse(missingWitness), (BoundRuntimePolicy)policy);
        Assert.That(global::System.Linq.Enumerable.Any(rejected.Diagnostics,static diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
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
        Assert.That(oldLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, oldLibrary.Diagnostics));
        Assert.That(newLibrary.HasErrors, Is.False, string.Join(Environment.NewLine, newLibrary.Diagnostics));
        var oldProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = oldLibrary.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var newProgram = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = newLibrary.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var oldProducer = ModuleCompiler.CreateProducer(oldProgram, ["Read"]);
        var replacement = ModuleCompiler.CreateProducer(newProgram, ["Read"]);
        var oldConsumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            ModuleCompiler.CreateReference(oldProducer), sourcePolicy);
        Assert.That(oldConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, oldConsumerCompilation.Diagnostics));
        var oldConsumer = ModuleCompiler.CreateConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = oldConsumerCompilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))(), oldProducer);
        var snapshot = SHA256.HashData(oldConsumer);
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(oldConsumer, oldProducer), policy), Is.EqualTo(42));
        Assert.That(NativeJit.Execute(ModuleCompiler.Link(oldConsumer, replacement), policy), Is.EqualTo(44));
        Assert.That(SHA256.HashData(oldConsumer), Is.EqualTo(snapshot));

        var oldNative = NativeModuleCompiler.CreateProducer(oldProgram, ["Read"], "NodeB", policy);
        var nativeConsumerCompilation = Compilation.Create(SyntaxTree.Parse(Consumer),
            NativeModuleCompiler.CreateGraphReference([oldNative]), sourcePolicy);
        Assert.That(nativeConsumerCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, nativeConsumerCompilation.Diagnostics));
        var nativeConsumer = NativeModuleCompiler.CreateGraphConsumer(
            ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = nativeConsumerCompilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))(), [oldNative], policy);
        var nativeSnapshot = SHA256.HashData(nativeConsumer);
        var newNative = NativeModuleCompiler.CreateProducer(newProgram, ["Read"], "NodeB", policy);
        NativeModuleCompiler.ValidateGraph(nativeConsumer, [newNative]);
        Assert.That(await NativeModuleCompiler.RunGraphAsync(nativeConsumer, [newNative],
            TimeSpan.FromSeconds(10), policy).ConfigureAwait(true), Is.EqualTo(44));
        Assert.That(SHA256.HashData(nativeConsumer), Is.EqualTo(nativeSnapshot));
    }
    [TestCase("Addition<Value, Value>","Addition<int32, Value>","TRM3007")]
    [TestCase("Addition<Value, Value>","Addition<Value, int32>","TRM3007")]
    [TestCase("Negate<Value>","Negate<int32>","TRM3007")]
    [TestCase("Addition<Value, Value>","Addition<Value, Value, Value>","TRM3007")]
    [TestCase("Negate<Value>","Negate<Value, Value>","TRM3007")]
    [TestCase("Addition<T, T>","Addition<T, T, T>","TRM3007")]
    [TestCase("Negate<T>","Negate<T, T>","TRM3007")]
    [TestCase("Addition<T, T>","Addition<int32, T>","TRM3007")]
    [TestCase("interface Addition<this TLeft, TRight, TResult>",        "interface Addition<TLeft, this TRight, TResult>","TRM3001")]
    [TestCase("interface Addition<this TLeft, TRight, TResult>",        "interface Addition<this TLeft, this TRight, TResult>","TRM3001")]
    [TestCase("interface Addition<this TLeft, TRight, TResult>",        "interface Addition<this TLeft, TLeft, TResult>","TRM3001")]
    [TestCase("interface Addition<this TLeft, TRight, TResult>",        "interface Addition<this TLeft, TResult>","TRM3001")]
    [TestCase("static TResult operator +(TLeft left, TRight right)",        "abstract static TResult operator +(TLeft left, TRight right)","TRM1002")]
    [TestCase("static TResult operator +(TLeft left, TRight right)",        "static abstract TResult operator +(TLeft left, TRight right)","TRM1002")]
    public void RejectsMismatchedSelfOrRequirementShape(string before, string after, string code)
    {
        Assert.That(Source, Does.Contain(before));
        var compilation = Compilation.Create(SyntaxTree.Parse(Source.Replace(before, after, StringComparison.Ordinal)));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == code),Is.True);
    }

    [Test]
    public void AnAbstractNameIsAnOrdinaryIdentifier()
    {
        const string Source = "interface Abstract<T, R> { static R operator +(T abstract); } int32 Main() { int32 abstract = 42; return abstract; }";
        var tree = SyntaxTree.Parse(Source);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(Compilation.Create(tree).HasErrors, Is.False);
    }

    [Test]
    public void AFalseNoThrowSelfWitnessIsRejected()
    {
        var source = Source.Replace("[NoThrow] public static Value operator -(Value value)",
            "public static Value operator -(Value value)", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }

    [Test]
    public void DefinitionProofCannotInventAnUnlistedWitness()
    {
        var source = Source.Replace("Addition<T, T>, Negate<T>, Ordinary<T, T, T>, [Copy]",
            "Addition<T, T>, Negate<T>, [Copy]", StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }
}
