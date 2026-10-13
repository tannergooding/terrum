// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProofTests
{
    [Fact]
    public void ProvenBodyChecksEraseWhileOrdinaryRequireRemainsExecutable()
    {
        const string Source = """
            [ConstEval] bool True() { return true; }
            int32 Main() {
                const bool known = True();
                static require known;
                static ensure True();
                require known;
                return 42;
            }
            """;
        var tree = SyntaxTree.Parse(Source);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(Source, tree.Root.ToFullString());
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.StaticRequireStatement);
        _ = Assert.Single(tree.Root.DescendantNodes(), node => node.Kind == SyntaxKind.StaticEnsureStatement);
        var version = Assert.IsAssignableFrom<System.Reflection.FieldInfo>(
            typeof(SyntaxTree).GetField(nameof(SyntaxTree.LanguageVersion)));
        Assert.Equal("syntax-46", version.GetRawConstantValue());

        var compilation = Compilation.Create(tree);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "Main");
        Assert.Collection(main.Body.Statements,
            statement => Assert.IsType<BoundLocal>(statement),
            statement => Assert.IsType<BoundRequire>(statement),
            statement => Assert.IsType<BoundReturn>(statement));
    }

    [Fact]
    public void OuterEnsureIsIndependentOfEarlierNormalReturnPaths()
    {
        const string Source = """
            int32 Main() {
                if (true) { return 42; }
                static ensure true;
                return 0;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions);
        Assert.DoesNotContain(main.Body.Statements,
            statement => statement.GetType().Name.Contains("Static", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequireUsesExactLocalPredicatesOnConditionalEdges()
    {
        const string Source = """
            int32 Verify(int32 input) {
                mut int32 value = input;
                mut bool enabled = true;
                if (value > 0) {
                    static require value > 0;
                } else {
                    static require value <= 0;
                }
                if (value > 0) {
                    static require value > 0;
                } else if (value == 0) {
                    static require value == 0;
                } else if (value < 0) {
                    static require value < 0;
                }
                if (0 < value) {
                    static require value > 0;
                }
                if (value != 1) {
                    static require value != 1;
                } else {
                    static require value == 1;
                }
                if (value > 0 && (value < 10 && enabled)) {
                    static require value > 0;
                    static require value < 10;
                    static require enabled;
                    static require value > 0 && (value < 10 && enabled);
                }
                if (value <= 0 || value >= 10) {
                    int32 ignored = 0;
                } else {
                    static require value > 0;
                    static require value < 10;
                }
                if (enabled) {
                    static require enabled;
                } else {
                    static require !enabled;
                }
                return 42;
            }
            int32 Main() { return Verify(1); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void StaticRequireUsesKnownOperandsToDisambiguateShortCircuitEdges()
    {
        const string Source = """
            int32 LeftKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value > 0) {
                    if (value > 0 && enabled) { } else {
                        static require !enabled;
                    }
                }
                return 42;
            }
            int32 RightKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (enabled) {
                    if (value > 0 && enabled) { } else {
                        static require value <= 0;
                    }
                }
                return 42;
            }
            int32 OrLeftKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (enabled) {
                    if (!enabled || value > 0) {
                        static require value > 0;
                    }
                }
                return 42;
            }
            int32 OrRightKnown(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value <= 0) {
                    if (enabled || value > 0) {
                        static require enabled;
                    }
                }
                return 42;
            }
            int32 Main() {
                return LeftKnown(1) + RightKnown(1) + OrLeftKnown(1) + OrRightKnown(1);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void StaticRequireRefinesRecursiveShortCircuitOperands(NativeRuntimePolicy policy)
    {
        const string Source = """
            int32 AndCompoundLeft(int32 input) {
                mut int32 value = input;
                mut bool enabled = true;
                if (value > 0 && enabled) {
                    if ((value > 0 && enabled) && value < 10) { }
                    else { static require value >= 10; }
                }
                return 42;
            }
            int32 AndCompoundRight(int32 input) {
                mut int32 value = input;
                mut bool enabled = true;
                if (value > 0 && enabled) {
                    if (value < 10 && (value > 0 && enabled)) { }
                    else { static require value >= 10; }
                }
                return 42;
            }
            int32 OrCompoundLeft(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value <= 0 && !enabled) {
                    if ((value > 0 || enabled) || value < 10) {
                        static require value < 10;
                    }
                }
                return 42;
            }
            int32 OrCompoundRight(int32 input) {
                mut int32 value = input;
                mut bool enabled = false;
                if (value <= 0 && !enabled) {
                    if (value < 10 || (value > 0 || enabled)) {
                        static require value < 10;
                    }
                }
                return 42;
            }
            int32 NestedComparisons(int32 input) {
                mut int32 value = input;
                if (value > 0 && value < 10) {
                    if ((value > 0 && value < 10) && value > 5) { }
                    else { static require value <= 5; }
                }
                return 42;
            }
            int32 Main() {
                return AndCompoundLeft(10) + AndCompoundRight(10)
                    + OrCompoundLeft(-1) + OrCompoundRight(-1) + NestedComparisons(3);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(210, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void StaticRequireUsesExactFloatingPredicatesWithoutOrderingComplements(NativeRuntimePolicy policy)
    {
        const string Source = """
            int32 Verify32(float32 input) {
                mut float32 value = input;
                if (value == float32(0)) {
                    static require value == float32(0);
                } else {
                    static require !(value == float32(0));
                }
                if (value != float32(0)) {
                    static require value != float32(0);
                } else {
                    static require !(value != float32(0));
                }
                if (value < float32(0)) {
                    static require value < float32(0);
                } else {
                    static require !(value < float32(0));
                }
                if (value <= float32(0)) {
                    static require value <= float32(0);
                } else {
                    static require !(value <= float32(0));
                }
                if (value > float32(0)) {
                    static require value > float32(0);
                } else {
                    static require !(value > float32(0));
                }
                if (value >= float32(0)) {
                    static require value >= float32(0);
                } else {
                    static require !(value >= float32(0));
                }
                if (float32(0) < value) {
                    static require value > float32(0);
                } else {
                    static require !(float32(0) < value);
                }
                return 42;
            }
            int32 Verify64(float64 input) {
                mut float64 value = input;
                if (value == float64(0)) {
                    static require value == float64(0);
                } else {
                    static require !(value == float64(0));
                }
                if (value != float64(0)) {
                    static require value != float64(0);
                } else {
                    static require !(value != float64(0));
                }
                if (value < float64(0)) {
                    static require value < float64(0);
                } else {
                    static require !(value < float64(0));
                }
                if (value <= float64(0)) {
                    static require value <= float64(0);
                } else {
                    static require !(value <= float64(0));
                }
                if (value > float64(0)) {
                    static require value > float64(0);
                } else {
                    static require !(value > float64(0));
                }
                if (value >= float64(0)) {
                    static require value >= float64(0);
                } else {
                    static require !(value >= float64(0));
                }
                if (float64(0) < value) {
                    static require value > float64(0);
                } else {
                    static require !(float64(0) < value);
                }
                return 42;
            }
            int32 Main() {
                return Verify32(float32(0) / float32(0))
                    + Verify32(float32(0)) + Verify32(float32(1)) + Verify32(float32(-1))
                    + Verify64(float64(0) / float64(0))
                    + Verify64(float64(0)) + Verify64(float64(1)) + Verify64(float64(-1));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(336, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Fact]
    public void StaticRequireDoesNotInferFloatOrderingComplements()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(float32 input) {
                mut float32 value = input;
                if (value > float32(0)) { }
                else { static require value <= float32(0); }
                return 42;
            }
            int32 Main() { return Verify(float32(0) / float32(0)); }
            """));

        Assert.Contains(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026"
                && diagnostic.Message.Contains("not established", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequireNormalizesReversedScalarComparisons()
    {
        const string Source = """
            int32 Int32Cases(int32 input) {
                mut int32 value = input;
                if (1 < value) {
                    static require value > 1;
                } else {
                    static require value <= 1;
                }
                if (1 <= value) {
                    static require value >= 1;
                } else {
                    static require value < 1;
                }
                if (1 > value) {
                    static require value < 1;
                } else {
                    static require value >= 1;
                }
                if (1 >= value) {
                    static require value <= 1;
                } else {
                    static require value > 1;
                }
                return 0;
            }
            int32 Int64Cases(int64 input) {
                mut int64 value = input;
                if (1 < value) {
                    static require value > 1;
                } else {
                    static require value <= 1;
                }
                return 0;
            }
            int32 UInt32Cases(uint32 input) {
                mut uint32 value = input;
                if (1 <= value) {
                    static require value >= 1;
                } else {
                    static require value < 1;
                }
                return 0;
            }
            int32 UInt64Cases(uint64 input) {
                mut uint64 value = input;
                if (1 > value) {
                    static require value < 1;
                } else {
                    static require value >= 1;
                }
                return 0;
            }
            int32 Char16Cases(char16 input) {
                mut char16 value = input;
                if (char16(1) >= value) {
                    static require value <= char16(1);
                } else {
                    static require value > char16(1);
                }
                return 0;
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
    public void StaticRequireDerivesIntegerComparisonRangesFromBranchConditions(NativeRuntimePolicy policy)
    {
        const string Source = """
            int32 Signed(int32 input) {
                mut int32 value = input;
                if (value > 0) {
                    static require value >= 1;
                    static require value != 0;
                }
                if (value < 0) {
                    static require value <= -1;
                }
                if (value > 0 && value < 2) {
                    static require value == 1;
                }
                return 42;
            }
            int32 Unsigned(uint64 input) {
                mut uint64 value = input;
                static require value >= uint64(0);
                static require value <= uint64(0xFFFFFFFFFFFFFFFF);
                if (value > uint64(0x7FFFFFFFFFFFFFFF)) {
                    static require value >= uint64(0x8000000000000000);
                }
                return 42;
            }
            int32 Main() {
                return Signed(1) + Unsigned(uint64(0x8000000000000000));
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source), (BoundRuntimePolicy)policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(84, NativeJit.Execute(program, policy));
        Assert.NotEmpty(NativeEmitter.Emit(program, policy));
    }

    [Fact]
    public void StaticRequireUsesIntegerExclusionsWithBranchBounds()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                mut int32 value = input;
                if (value <= 5 && value != 5) {
                    static require value <= 4;
                }
                if (value >= -3 && value != -3) {
                    static require value >= -2;
                }
                return 42;
            }
            int32 Main() { return Verify(1); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void StaticRequireReusesExactLocalToLocalComparisons()
    {
        const string Source = """
            int32 Compare(int32 first, int32 second) {
                mut int32 left = first;
                mut int32 right = second;
                if (left < right) {
                    static require left < right;
                    static require right > left;
                    static require !(left >= right);
                    static require !(right <= left);
                } else {
                    static require !(left < right);
                }
                if (left == right) {
                    static require right == left;
                }
                if (left != right) {
                    static require left != right;
                }
                return 42;
            }
            int32 Main() {
                return Compare(1, 2) + Compare(2, 1) + Compare(3, 3);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(126, NativeJit.Execute(program, NativeRuntimePolicy.FailFast));
        Assert.NotEmpty(NativeEmitter.Emit(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void StaticRequireTracksOrdinaryScalarValueParametersButNotOutputParameters()
    {
        const string Source = """
            int32 Verify(int32 value, bool enabled) {
                if (value > 0) {
                    static require value > 0;
                }
                if (enabled) {
                    static require enabled;
                }
                static require value == value;
                return 42;
            }
            int32 Main() { return Verify(1, true); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var outputCompilation = Compilation.Create(SyntaxTree.Parse("""
            bool Verify(out int32 value) {
                value = 1;
                if (value > 0) {
                    static require value > 0;
                }
                return true;
            }
            int32 Main() { return 0; }
            """));
        Assert.Contains(outputCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026"
                && diagnostic.Message.Contains("not established", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequireRefinesEagerBooleanOperatorEdges()
    {
        const string Source = """
            int32 Verify(bool left, bool right) {
                if (left & right) {
                    static require left;
                    static require right;
                    static require left & right;
                }
                if (!left) {
                    if (left | right) {
                        static require right;
                    }
                    if (left | right) { }
                    else {
                        static require !right;
                    }
                }
                if (left) {
                    if (left & right) { }
                    else {
                        static require !right;
                    }
                }
                if (left) {
                    if (left ^ right) {
                        static require !right;
                    } else {
                        static require right;
                    }
                }
                if (!left) {
                    if (left ^ right) {
                        static require right;
                    } else {
                        static require !right;
                    }
                }
                return 42;
            }
            int32 Main() {
                return Verify(false, false) + Verify(false, true)
                    + Verify(true, false) + Verify(true, true);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(168, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var reachableMiddle = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                mut int32 value = input;
                if (value < 4) {
                } else if (value > 6) {
                }
                static require value != 5;
                return 42;
            }
            int32 Main() { return Verify(0); }
            """));
        Assert.Contains(reachableMiddle.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026");
    }

    [Fact]
    public void StaticRequireTracksExactBooleanInitializersAssignmentsAndJoins()
    {
        const string Source = """
            int32 Verify(bool input) {
                mut bool enabled = true;
                static require enabled;
                enabled = false;
                static require !enabled;
                if (input) {
                    enabled = true;
                } else {
                    enabled = true;
                }
                static require enabled;
                enabled = input;
                if (enabled) {
                    static require enabled;
                }
                return 42;
            }
            int32 Main() { return Verify(false) + Verify(true); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(84, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void StaticProofAssignmentInvalidatesOnlyFactsAboutTheAssignedLocal()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                mut int32 unrelated = 0;
                if (input > 0) {
                    unrelated = 1;
                    static require input > 0;
                }
                return unrelated;
            }
            int32 Main() { return Verify(1); }
            """));

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(1, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var staleCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 1;
                if (value > 0) {
                    value = -1;
                    static require value > 0;
                }
                return 0;
            }
            """));
        Assert.Contains(staleCompilation.Diagnostics,
                static diagnostic => diagnostic.Code == "TRM3025");
    }

    [Fact]
    public void StaticProofJoinsCommonIntegerConsequencesFromReachableEdges()
    {
        const string Source = """
            int32 VerifyBound(int32 input) {
                mut int32 value = input;
                if (value < 5) {
                } else {
                    if (value < 8) {
                    } else {
                        return 0;
                    }
                }
                static require value < 8;
                return 42;
            }
            int32 VerifyExclusion(int32 input) {
                mut int32 value = input;
                if (value < 0) {
                } else if (value <= 0) {
                    return 0;
                }
                static require value != 0;
                return 42;
            }
            int32 VerifyJoinedExclusion(int32 input) {
                mut int32 value = input;
                if (value < -1) {
                } else {
                    if (value > 1) {
                    } else {
                        return 0;
                    }
                }
                static require value != 0;
                return 42;
            }
            int32 VerifyJoinedCatalogExclusion(int32 input) {
                mut int32 value = input;
                if (value < 4) {
                } else {
                    if (value > 6) {
                    } else {
                        return 0;
                    }
                }
                static require value != 5;
                return 42;
            }
            int32 Main() {
                return VerifyBound(7) + VerifyExclusion(-1) + VerifyJoinedExclusion(-2)
                    + VerifyJoinedCatalogExclusion(-2);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(168, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void StaticRequireTransfersRepresentableIntegerIntervalsToLocals()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                if (input >= 0 && input < 10) {
                    int32 initialized = input + 1;
                    static require initialized > 0 && initialized <= 10;
                    mut int32 assigned = 0;
                    assigned = input + 1;
                    static require assigned > 0 && assigned <= 10;
                }
                return 42;
            }
            int32 Main() { return Verify(9); }
            """));

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var possibleOverflow = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                mut int32 value = 0;
                value = input + 1;
                static require value > 0;
                return 42;
            }
            int32 Main() { return Verify(0); }
            """));
        Assert.Contains(possibleOverflow.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026");
    }

    [Fact]
    public void StaticProofTransfersCheckedIntegerRangesAndRejectsPossibleOverflow()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                mut int32 value = input;
                if (value < 9) {
                    static require value + 1 <= 9;
                }
                if (value == 9) {
                    static require value - 1 == 8;
                }
                return 42;
            }
            int32 VerifyUnsigned(uint32 value) {
                if (value < uint32(9)) {
                    static require value + uint32(1) <= uint32(9);
                }
                return 42;
            }
            int32 VerifyWide(uint64 value) {
                if (value <= uint64(3)) {
                    static require value + uint64(1) <= uint64(4);
                }
                return 42;
            }
            int32 VerifyProduct(int32 value) {
                if (value >= -3 && value <= 3) {
                    static require value * value <= 9;
                }
                return 42;
            }
            int32 VerifyDivision(int32 value) {
                if (value >= -10 && value <= 10 && value != 0) {
                    static require value / value >= -10 && value / value <= 10;
                    static require value % value >= -9 && value % value <= 9;
                }
                return 42;
            }
            int32 VerifyMinimumRemainder(int32 value) {
                if (value == int32.MinValue) {
                    static require value % -1 == 0;
                }
                return 42;
            }
            int32 VerifyShift(int32 value, int32 count) {
                if (count >= 0 && count < 32) {
                    static require (value << count) >= int32.MinValue;
                }
                if (value == 4 && count == 1) {
                    static require (value << count) == 8;
                }
                return 42;
            }
            int32 VerifyBitwise(int32 value) {
                if (value == -1) {
                    static require (value & int32.MaxValue) == int32.MaxValue;
                    static require (value | 0) == -1;
                    static require (value ^ -1) == 0;
                    static require ~value == 0;
                }
                if (value >= -10 && value <= 10) {
                    static require (value & 1) >= int32.MinValue;
                }
                return 42;
            }
            int32 VerifyUnsignedBitwise(uint32 value) {
                if (value == uint32(0x80000000)) {
                    static require (value | uint32(1)) == uint32(0x80000001);
                    static require ~value == uint32(0x7FFFFFFF);
                }
                return 42;
            }
            int32 VerifyWideShift(int64 value, int32 count) {
                if (value == -8 && count == 1) {
                    static require (value >> count) == -4;
                }
                if (value == -1 && count == 1) {
                    static require (value >>> count) > 0;
                }
                return 42;
            }
            int32 VerifyUnsignedShift(uint32 value, int32 count) {
                if (value == uint32(0x80000000) && count == 31) {
                    static require (value >> count) == uint32(1);
                    static require (value >>> count) == uint32(1);
                }
                return 42;
            }
            int32 Main() {
                return Verify(8) + Verify(9) + VerifyUnsigned(8)
                    + VerifyWide(uint64(3)) + VerifyProduct(-3) + VerifyDivision(7)
                    + VerifyMinimumRemainder(int32.MinValue) + VerifyShift(4, 1)
                    + VerifyWideShift(-8, 1) + VerifyWideShift(-1, 1)
                    + VerifyUnsignedShift(uint32(0x80000000), 31)
                    + VerifyBitwise(-1) + VerifyUnsignedBitwise(uint32(0x80000000));
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(546, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var divisionOverflow = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 value) {
                if (value == int32.MinValue) {
                    static require value / -1 < 0;
                }
                return 42;
            }
            int32 Main() { return Verify(int32.MinValue); }
            """));
        Assert.Contains(divisionOverflow.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026");

        var zeroDivisor = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 divisor) {
                static require int32(1) / divisor == 0;
                return 42;
            }
            int32 Main() { return Verify(1); }
            """));
        Assert.Contains(zeroDivisor.Diagnostics, static diagnostic => diagnostic.Code == "TRM3026");

        var uncheckedShiftCount = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 value, int32 count) {
                static require (value << count) >= int32.MinValue;
                return 42;
            }
            int32 Main() { return Verify(1, 2); }
            """));
        Assert.Contains(uncheckedShiftCount.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026");

        var potentiallyWideShiftCount = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 value, int32 count) {
                if (count >= 0) {
                    static require (value << count) >= int32.MinValue;
                }
                return 42;
            }
            int32 Main() { return Verify(1, 2); }
            """));
        Assert.Contains(potentiallyWideShiftCount.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026");

        var overflow = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 value) {
                if (value == int32.MaxValue) {
                    static require value + 1 > 0;
                }
                return 42;
            }
            int32 Main() { return Verify(int32.MaxValue); }
            """));
        Assert.Contains(overflow.Diagnostics, static diagnostic => diagnostic.Code == "TRM3026");
    }

    [Fact]
    public void StaticProofPreservesUnchangedLocalsAcrossCallsAndInvalidatesOutputs()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] void Effect() { }
            [NoThrow] bool Observe() { return false; }
            [NoThrow] void Set(out int32 value) { value = 1; }
            int32 Verify(int32 input) {
                mut int32 stable = input;
                mut int32 output = input;
                if (stable > 0 && output > 0) {
                    Effect();
                    static require stable > 0;
                    if (Observe()) {
                        static require stable > 0;
                    }
                    Set(out output);
                    static require stable > 0;
                    static require output > 0;
                }
                return 42;
            }
            int32 Main() { return Verify(1); }
            """));

        Assert.Equal("TRM3026", Assert.Single(compilation.Diagnostics).Code);
    }

    [Fact]
    public void SavedBooleanAndDirectCopiesRestoreCurrentPredicateFacts()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input, bool choose) {
                mut int32 value = input;
                mut bool saved = false;
                if (choose) {
                    saved = value > 0;
                } else {
                    saved = value > 0;
                }
                mut int32 unrelated = 0;
                unrelated = 1;
                bool copied = saved;
                bool inverse = !saved;
                if (copied) {
                    static require value > 0;
                }
                if (inverse) {
                    static require value <= 0;
                }
                return 42;
            }
            int32 Main() { return Verify(1, true) + Verify(-1, false); }
            """));

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(84, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var copiedValueCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 1;
                mut bool saved = false;
                if (value > 0) {
                    saved = value > 0;
                    bool copied = saved;
                    value = -1;
                    static require saved;
                    static require copied;
                }
                return 0;
            }
            """));
        Assert.False(copiedValueCompilation.HasErrors,
            string.Join(Environment.NewLine, copiedValueCompilation.Diagnostics));

        var invalidatedCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                mut int32 value = 1;
                bool saved = value > 0;
                value = 0;
                if (saved) {
                    static require value > 0;
                }
                return 0;
            }
            """));
        Assert.Contains(invalidatedCompilation.Diagnostics,
                static diagnostic => diagnostic.Code == "TRM3025");

        var differentPredicatesCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 first, int32 second, bool choose) {
                mut bool saved = false;
                if (choose) {
                    saved = first > 0;
                } else {
                    saved = second > 0;
                }
                if (saved) {
                    static require first > 0;
                }
                return 0;
            }
            int32 Main() { return Verify(1, 2, true); }
            """));
        Assert.Contains(differentPredicatesCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026");
    }

    [Fact]
    public void StaticRequireUsesStableWhileConditionOnTheBodyEdge()
    {
        const string Source = """
            int32 Verify(int32 input) {
                mut int32 value = input;
                while (value > 0) {
                    static require value > 0;
                    value = 0;
                }
                return 42;
            }
            int32 Main() { return Verify(1) + Verify(0); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(84, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void StaticRequireDoesNotCheckUnreachablePointObligations()
    {
        const string Source = """
            int32 Verify(int32 input) {
                if (false) {
                    static require false;
                }
                if (input > 0 && input <= 0) {
                    static require false;
                }
                while (false) {
                    static require false;
                }
                return 42;
                static require false;
            }
            int32 Impossible() {
                require false;
                static require false;
                return 0;
            }
            int32 Main() { return Verify(1); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));

        var effectfulCompilation = Compilation.Create(SyntaxTree.Parse("""
            bool Runtime() { return true; }
            int32 Main() {
                if (false) {
                    static require Runtime();
                }
                return 42;
            }
            """));
        Assert.Contains(effectfulCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3027");
    }

    [Fact]
    public void StaticRequireUsesTheSuccessfulEdgeOfAnOrdinaryRequire()
    {
        const string Source = """
            int32 Verify(int32 input) {
                mut int32 value = input;
                require value > 0;
                static require value > 0;
                return 42;
            }
            int32 Main() { return Verify(1); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(42, NativeJit.Execute(Assert.IsType<BoundProgram>(compilation.Program),
            NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void StaticRequireDoesNotUseFactsInvalidatedByAnEffectfulRequireCondition()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool SetNegative(out int32 target) {
                target = -1;
                return true;
            }
            int32 Verify(int32 input) {
                mut int32 value = input;
                if (value > 0) {
                    require SetNegative(out value) && value < 0;
                    static require value < 0;
                }
                return 42;
            }
            int32 Main() { return Verify(1); }
            """));
        Assert.Contains(compilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026"
                && diagnostic.Message.Contains("not established", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequirePreservesExactLocalComparisonsAcrossIndependentCalls()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Runtime() { return true; }
            int32 Compare(int32 first, int32 second) {
                mut int32 left = first;
                mut int32 right = second;
                if (left < right) {
                    Runtime();
                    static require left < right;
                }
                return 42;
            }
            int32 Main() { return Compare(1, 2); }
            """));

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void StaticRequireUsesBooleanAssignmentsAfterIndependentCalls()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            bool Runtime() { return true; }
            int32 Verify() {
                mut bool enabled = false;
                Runtime();
                enabled = true;
                static require enabled;
                return 42;
            }
            int32 Main() { return Verify(); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void StaticRequireUsesReflexiveEqualityOnlyForNonFloatingScalars()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(int32 input) {
                mut int32 value = input;
                static require value == value;
                static require !(value != value);
                return 42;
            }
            int32 Main() { return Verify(1); }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));

        var floatingCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Verify(float32 input) {
                mut float32 value = input;
                static require value == value;
                return 42;
            }
            int32 Main() { return Verify(float32(1)); }
            """));
        Assert.Contains(floatingCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3026"
                && diagnostic.Message.Contains("not established", StringComparison.Ordinal));
    }

    [Fact]
    public void StaticRequireRejectsFloatingPointOrderingComplements()
    {
        const string Source = """
            int32 Verify(float32 input) {
                mut float32 value = input;
                if (value > float32(0)) {
                    static require value > float32(0);
                } else {
                    static require value <= float32(0);
                }
                return 0;
            }
            int32 Main() { return Verify(float32(0)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Equal(1, compilation.Diagnostics.Length);
        Assert.All(compilation.Diagnostics, diagnostic => Assert.Equal("TRM3026", diagnostic.Code));
    }

    [Theory]
    [InlineData("if (value > 0) { static require value <= 0; }", "TRM3025")]
    [InlineData("if (value > 0) { static require value < 0; }", "TRM3025")]
    [InlineData("if (value > 0) { static require value > 1; }", "TRM3026")]
    [InlineData("if (value > 0) {} static require value > 0;", "TRM3026")]
    [InlineData("if (value > 0) { value = 0; static require value > 0; }", "TRM3025")]
    [InlineData("static require input > 0;", "TRM3026")]
    [InlineData("if (NoThrowPredicate(value)) { static require value > 0; }", "TRM3026")]
    [InlineData("if (value > 0 || value < 10) { static require value > 0; }", "TRM3026")]
    [InlineData("if (value > 0 && value < 10) {} else { static require value <= 0; }", "TRM3026")]
    [InlineData("mut bool enabled = true; if (value > 0 && enabled) {} else { static require !enabled; }", "TRM3025")]
    [InlineData("mut bool enabled = true; if (enabled || value > 0) { static require value > 0; }", "TRM3026")]
    public void StaticRequireDoesNotUseContradictedOrUnstableFacts(string statements, string code)
    {
        var source = """
            bool Runtime() { return true; }
            [NoThrow] bool NoThrowPredicate(implicit ref<int32> value) { return value > 0; }
            int32 Verify(int32 input) {
                mut int32 value = input;
                STATEMENTS
                return 42;
            }
            int32 Main() { return Verify(1); }
            """.Replace("STATEMENTS", statements, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Theory]
    [InlineData("static require false;", "TRM3025", "evaluates to false")]
    [InlineData("static ensure false;", "TRM3025", "evaluates to false")]
    [InlineData("static require input == 42;", "TRM3026", "not established")]
    [InlineData("static ensure input == 42;", "TRM3026", "not established")]
    [InlineData("static require int32(1) / int32(0) == 0;", "TRM3026", "checked arithmetic")]
    [InlineData("static require Recurse(100) == 0;", "TRM3026", "call depth")]
    [InlineData("static require Runtime();", "TRM3027", "unsupported effects")]
    [InlineData("static ensure Runtime();", "TRM3027", "unsupported effects")]
    [InlineData("const bool known = true; static ensure known;", "TRM3026", "not established")]
    public void FalseUnknownAndUnsupportedProofsHaveDistinctDiagnostics(string statement, string code, string text)
    {
        var source = """
            [ConstEval] int32 Recurse(int32 remaining) {
                if (remaining == 0) { return 0; }
                return 1 + Recurse(remaining - 1);
            }
            bool Runtime() { return true; }
            int32 Verify(int32 input) { STATEMENT return 42; }
            int32 Main() { return Verify(42); }
            """.Replace("STATEMENT", statement, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code
            && diagnostic.Message.Contains(text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("if (true) { static ensure true; }", "TRM3026")]
    [InlineData("static ensure true;", "TRM3026")]
    public void NestedEnsureAndConstructorEnsureAreNotPointChecks(string statement, string code)
    {
        ArgumentNullException.ThrowIfNull(statement);
        var source = statement.StartsWith("if", StringComparison.Ordinal)
            ? "int32 Main() { " + statement + " return 42; }"
            : "struct Box { public int32 Value; public Box(int32 value) { "
                + statement + " Value = value; } } int32 Main() { return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void StaticProofsCannotBePublishedAsTopLevelClauses()
    {
        var tree = SyntaxTree.Parse("static require true; int32 Main() { return 42; }");
        Assert.True(tree.HasErrors);
    }

    [Fact]
    public void RuntimeParametersInGenericTemplatesDoNotBecomeProofs()
    {
        const string Source = """
            T Forward<T>(T value) where T : [Copy] {
                static require true;
                return value;
            }
            int32 Main() { return Forward(int32(42)); }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3026");
    }
}
