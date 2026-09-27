// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolInferenceTests
{
    private const string Values = """
        struct Value {
            public int32 Number;
            public Value(int32 number) { Number = number; }
        }
        struct Other {
            public int32 Number;
            public Other(int32 number) { Number = number; }
        }
        T Identity<T>(T value) where T : [Copy] { return value; }
        """;

    [Theory]
    [InlineData("var result = Identity(42);", "TRM3011")]
    [InlineData("Value result = Identity(42);", "TRM3014")]
    [InlineData("var result = Identity(int32(42));", "TRM3011")]
    [InlineData("Value result = Identity(new Other(42));", "TRM3007")]
    [InlineData("var result = Both(new Value(1), new Other(2));", "TRM3007")]
    [InlineData("var result = Identity(new Owner(42));", "TRM3007")]
    public void InferenceDoesNotGuessOrRetargetTypedArguments(string body, string diagnostic)
    {
        var source = Values + """
            unique struct Owner {
                public int32 Number;
                public Owner(int32 number) { Number = number; }
            }
            T Both<T>(T first, T second) where T : [Copy] { return first; }
            int32 Main() {
            """ + body + "return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Contains(compilation.Diagnostics, error => error.Code == diagnostic);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void ExistingTypedOutLocalInfersTypeWithoutPublishingItDuringInference()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            [NoThrow] bool None<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                mut Value target = new Value(42);
                if (None(out target)) { return 1; }
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Contains(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "None<Value>");
    }

    [Fact]
    public void OutVarAloneDoesNotEstablishTheGenericType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            bool None<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                if (None(out var result)) { return result.Number; }
                return 42;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void KnownScalarReturnCannotBypassMissingStructInference()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            int32 Read<T>(T value, int32 count) where T : [Copy] { return count; }
            int32 Main() { return Read(42, 1) + Read(new Value(41), 1); }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void FailedInferredOutputLeavesExistingTargetUnavailableUntilRepublished()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            bool None<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                mut Value target = new Value(42);
                None(out target);
                return target.Number;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InferredHelperUsesDeclaredNoThrowEffect(bool helperNoThrow)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            [NoThrow] U Wrap<U>(U value) where U : [Copy] {
                return Leaf(value);
            }
            """ + (helperNoThrow ? "[NoThrow] " : "") + """
            V Leaf<V>(V value) where V : [Copy] { return value; }
            int32 Main() { return Wrap(new Value(42)).Number; }
            """));
        if (helperNoThrow)
        {
            Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3017");
        }
    }

    [Fact]
    public void InferredHelperCannotBorrowCallersStrongerProtocolWitnesses()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            interface Addition<A, B, C> { static abstract C operator +(A left, B right); }
            struct Value : Addition<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator +(Value left, Value right) {
                    return new Value(left.Number + right.Number);
                }
            }
            T Add<T>(T value) where T : Addition<T, T, T>, [Copy] { return value + value; }
            U Strong<U>(U value) where U : Addition<U, U, U>, [Copy] { return Add(value); }
            V Weak<V>(V value) where V : [Copy] { return Add(value); }
            int32 Main() {
                var valid = Strong(new Value(21));
                return Weak(valid).Number;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void VariableAndAmbiguousImportedTemplatesNeverSelectAnInferredCall()
    {
        var shadowed = Compilation.Create(SyntaxTree.Parse(Values + """
            int32 Main() {
                int32 Identity = 1;
                var result = Identity(new Value(42));
                return 0;
            }
            """));
        Assert.Contains(shadowed.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");

        var ambiguous = Compilation.Create([
            SyntaxTree.Parse("namespace One;" + Values),
            SyntaxTree.Parse("namespace Two;" + Values.Replace("struct Value", "struct ValueTwo",
                StringComparison.Ordinal).Replace("public Value(", "public ValueTwo(", StringComparison.Ordinal)),
            SyntaxTree.Parse("""
                using One;
                using Two;
                int32 Main() {
                    var result = Identity(new One.Value(42));
                    return 0;
                }
                """),
        ]);
        Assert.Contains(ambiguous.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
    }
}
