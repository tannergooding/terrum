// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics.Generics;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmExecutableArithmeticProfileTests
{
    private static BoundProgram Compile(string source, SourceArithmeticProfile profile)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), new CompilationOptions {
            SourceArithmeticProfile = profile,
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        });
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => {
            var __nunitActual0 = compilation.Program;
            Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>());
            return (BoundProgram)__nunitActual0!;
        }))();
    }

    private static ImmutableArray<TExpression> GetExpressions<TExpression>(BoundFunction function)
        where TExpression : BoundExpression
    {
        var expressions = ImmutableArray.CreateBuilder<TExpression>();
        void Visit(BoundBlock body)
        {
            Assert.That(BoundTreeTraversal.VisitExpressions(body, expression => {
                if (expression is TExpression typed)
                {
                    expressions.Add(typed);
                }
            }), Is.True);
        }

        if (function.IndexedBody is { } indexed)
        {
            foreach (var block in indexed.Blocks)
            {
                Visit(new BoundBlock(block.Operations, default));
            }
        }
        else
        {
            Visit(function.Body);
        }
        return expressions.ToImmutable();
    }

    private static BoundFunction Function(BoundProgram program, string name)
        => program.Functions.Single(function => function.Name == name);

    private static void AssertArithmeticMode(BoundFunction function, TokenKind operation,
        BoundIntegerOverflowBehavior behavior)
    {
        var binaries = GetExpressions<BoundBinary>(function)
            .Where(binary => binary.Operator == operation).ToArray();
        Assert.That(binaries, Is.Not.Empty);
        Assert.That(binaries.Select(static binary => binary.OverflowBehavior),
            Is.All.EqualTo(behavior));
    }

    private static async Task AssertNativeImageStatusAsync(BoundProgram program, int expected)
    {
        Assume.That(OperatingSystem.IsWindows()
            && RuntimeInformation.ProcessArchitecture == Architecture.X64);
        var image = NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast);
        Assert.That(await StaticProtocolTests.RunAsync(image).ConfigureAwait(true), Is.EqualTo(expected));
    }

    [TestCase(SourceArithmeticProfile.Development, false)]
    [TestCase(SourceArithmeticProfile.Release, true)]
    public void LinearBodiesPreserveBinaryAndNegationPolicies(
        SourceArithmeticProfile profile, bool expectedWrapping)
    {
        var expectedBehavior = expectedWrapping
            ? BoundIntegerOverflowBehavior.Wrapping : BoundIntegerOverflowBehavior.Checked;
        var original = Compile("""
            int32 Add(int32 left, int32 right) { return left + right; }
            int32 Negate(int32 value) { return -value; }
            uint32 NegateUnsigned(uint32 value) { return -value; }
            int32 Main() {
                return Add(20, 22) + Negate(int32(0))
                    + int32(NegateUnsigned(uint32(0)));
            }
            """, profile);
        var loaded = TrRmExecutableCompiler.Load(
            TrRmExecutableCompiler.Emit(original, "linear-arithmetic"));
        var add = Function(loaded, "Add");
        var signedNegate = Function(loaded, "Negate");
        var unsignedNegate = Function(loaded, "NegateUnsigned");
        Assert.That(add.IndexedBody, Is.Null);
        Assert.That(signedNegate.IndexedBody, Is.Null);
        Assert.That(unsignedNegate.IndexedBody, Is.Null);
        AssertArithmeticMode(add, TokenKind.Plus, expectedBehavior);
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(42));

        if (!expectedWrapping)
        {
            Assert.That(GetExpressions<BoundUnary>(signedNegate).Single().OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
        }
        else
        {
            AssertArithmeticMode(signedNegate, TokenKind.Minus, BoundIntegerOverflowBehavior.Wrapping);
        }
        AssertArithmeticMode(unsignedNegate, TokenKind.Minus, expectedBehavior);
    }

    [TestCase("int32")]
    [TestCase("uint32")]
    [TestCase("int64")]
    [TestCase("uint64")]
    public async Task ReleaseLinearBodiesWrapAtEveryAdmittedIntegerWidthAsync(string type)
    {
        var signed = type is "int32" or "int64";
        var zero = $"{type}(0)";
        var one = $"{type}(1)";
        var two = $"{type}(2)";
        var maximum = $"{type}.MaxValue";
        var minimumOrZero = signed ? $"{type}.MinValue" : zero;
        var addExpected = signed ? $"{type}.MinValue" : zero;
        var multiplyExpected = signed ? $"{type}(-2)" : $"{maximum} - {one}";
        var negateInput = signed ? $"{type}.MinValue" : one;
        var negateExpected = signed ? $"{type}.MinValue" : maximum;
        var program = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile($$"""
            {{type}} Add({{type}} left, {{type}} right) { return left + right; }
            {{type}} Subtract({{type}} left, {{type}} right) { return left - right; }
            {{type}} Multiply({{type}} left, {{type}} right) { return left * right; }
            {{type}} Negate({{type}} value) { return -value; }
            int32 Main() {
                return Add({{maximum}}, {{one}}) == {{addExpected}}
                    && Subtract({{minimumOrZero}}, {{one}}) == {{maximum}}
                    && Multiply({{maximum}}, {{two}}) == {{multiplyExpected}}
                    && Negate({{negateInput}}) == {{negateExpected}} ? 42 : 0;
            }
            """, SourceArithmeticProfile.Release), $"linear-overflow-{type}"));

        foreach (var name in new[] { "Add", "Subtract", "Multiply", "Negate" })
        {
            AssertArithmeticMode(Function(program, name), name switch
            {
                "Add" => TokenKind.Plus,
                "Subtract" => TokenKind.Minus,
                "Multiply" => TokenKind.Star,
                _ => TokenKind.Minus,
            }, BoundIntegerOverflowBehavior.Wrapping);
        }
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        await AssertNativeImageStatusAsync(program, 42).ConfigureAwait(true);
    }

    [TestCase("int32", "int32.MaxValue", "int32.MinValue + int32(1)")]
    [TestCase("uint32", "uint32.MaxValue", "uint32(1)")]
    [TestCase("int64", "int64.MaxValue", "int64.MinValue + int64(1)")]
    [TestCase("uint64", "uint64.MaxValue", "uint64(1)")]
    public async Task ReleaseIndexedUpdatesAndCompoundsRetainWrappingPoliciesAsync(
        string type, string maximum, string afterUpdates)
    {
        var program = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile($$"""
            int32 Main() {
                mut {{type}} value = {{maximum}};
                {{type}} previous = value++;
                value += {{type}}(1);
                return previous == {{maximum}} && value == {{afterUpdates}} ? 42 : 0;
            }
            """, SourceArithmeticProfile.Release), $"indexed-overflow-{type}"));
        var entry = Function(program, "Main");
        Assert.That(entry.IndexedBody, Is.Not.Null);
        var additions = GetExpressions<BoundBinary>(entry)
            .Where(static binary => binary.Operator == TokenKind.Plus).ToArray();
        Assert.That(additions, Has.Length.EqualTo(2));
        Assert.That(additions.Select(static binary => binary.OverflowBehavior),
            Is.All.EqualTo(BoundIntegerOverflowBehavior.Wrapping));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
        await AssertNativeImageStatusAsync(program, 42).ConfigureAwait(true);
    }

    [Test]
    public void DevelopmentIndexedUpdatesAndCompoundsRemainChecked()
    {
        var program = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile("""
            int32 Main() {
                mut int32 value = 20;
                int32 previous = value++;
                value += 1;
                return previous == 20 && value == 22 ? 42 : 0;
            }
            """, SourceArithmeticProfile.Development), "indexed-checked-arithmetic"));
        var entry = Function(program, "Main");
        Assert.That(entry.IndexedBody, Is.Not.Null);
        var additions = GetExpressions<BoundBinary>(entry)
            .Where(static binary => binary.Operator == TokenKind.Plus).ToArray();
        Assert.That(additions, Has.Length.EqualTo(2));
        Assert.That(additions.Select(static binary => binary.OverflowBehavior),
            Is.All.EqualTo(BoundIntegerOverflowBehavior.Checked));
        Assert.That(NativeJit.Execute(program, NativeRuntimePolicy.FailFast), Is.EqualTo(42));
    }

    [TestCase(SourceArithmeticProfile.Development, "20", 21,
        false)]
    [TestCase(SourceArithmeticProfile.Release, "int32.MaxValue", int.MinValue,
        true)]
    public async Task ScalarInitOutputBodiesPreserveArithmeticPolicyAsync(
        SourceArithmeticProfile profile, string input, int expectedExit,
        bool expectedWrapping)
    {
        var expectedBehavior = expectedWrapping
            ? BoundIntegerOverflowBehavior.Wrapping : BoundIntegerOverflowBehavior.Checked;
        var artifact = TrRmExecutableCompiler.Emit(Compile($$"""
            void Fill(int32 value, init ref<int32> result) {
                result = value + 1;
            }
            int32 Main() {
                init int32 result;
                Fill({{input}}, init result);
                return result;
            }
            """, profile), "output-arithmetic");
        var artifactIndex = TrRmArtifactIndex.ForDevelopment(artifact);
        Assert.That(artifactIndex.TryGetSection(4, out var featureBytes, out var featureCount), Is.True);
        Assert.That(new TrRmFeatureTableReader(featureBytes, featureCount).GetFeature(1).RuleRevision,
            Is.EqualTo(8));

        var loaded = TrRmExecutableCompiler.Load(artifact);
        var fill = Function(loaded, "Fill");
        Assert.That(GetExpressions<BoundBinary>(fill).Single().OverflowBehavior,
            Is.EqualTo(expectedBehavior));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expectedExit));
        await AssertNativeImageStatusAsync(loaded, expectedExit).ConfigureAwait(true);
    }

    [TestCase(SourceArithmeticProfile.Development, false)]
    [TestCase(SourceArithmeticProfile.Release, true)]
    public async Task ScalarInitOutputNegationPreservesSignedAndUnsignedPoliciesAsync(
        SourceArithmeticProfile profile, bool expectedWrapping)
    {
        var expectedBehavior = expectedWrapping
            ? BoundIntegerOverflowBehavior.Wrapping : BoundIntegerOverflowBehavior.Checked;
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(Compile("""
            void NegateSigned(int32 value, init ref<int32> result) {
                result = -value;
            }
            void NegateUnsigned(uint32 value, init ref<uint32> result) {
                result = -value;
            }
            int32 Main() {
                init int32 signed;
                init uint32 unsigned;
                NegateSigned(0, init signed);
                NegateUnsigned(uint32(0), init unsigned);
                return int32(signed) + int32(unsigned);
            }
            """, profile), "output-negation-arithmetic"));
        AssertArithmeticMode(Function(loaded, "NegateUnsigned"), TokenKind.Minus, expectedBehavior);
        var signed = Function(loaded, "NegateSigned");
        if (expectedWrapping)
        {
            AssertArithmeticMode(signed, TokenKind.Minus, BoundIntegerOverflowBehavior.Wrapping);
        }
        else
        {
            Assert.That(GetExpressions<BoundUnary>(signed).Single().OverflowBehavior,
                Is.EqualTo(BoundIntegerOverflowBehavior.Checked));
        }
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.Zero);
        await AssertNativeImageStatusAsync(loaded, 0).ConfigureAwait(true);
    }
}
