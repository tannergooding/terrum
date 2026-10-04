// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StaticProtocolOutputTests
{
    private const string Value = """
        struct Value {
            public int64 First;
            public int64 Second;
            public Value(int64 first) { First = first; Second = 2; }
        }
        """;

    [Theory]
    [InlineData("int32", "-2147483648", PrimitiveType.Int32)]
    [InlineData("uint32", "4294967295", PrimitiveType.UInt32)]
    [InlineData("int64", "-9223372036854775808", PrimitiveType.Int64)]
    [InlineData("uint64", "18446744073709551615", PrimitiveType.UInt64)]
    [InlineData("char16", "char16(55296)", PrimitiveType.Char16)]
    [InlineData("bool", "true", PrimitiveType.Bool)]
    public void GenericKnownResultTypeComesFromReturnSignature(string type, string literal, PrimitiveType expected)
    {
        var source = Value + $$"""
            {{type}} Echo<T>(T ignored, {{type}} value) where T : [Copy] { return value; }
            int32 Main() {
                var actual = Echo<Value>(new Value(1), {{literal}});
                if (actual == {{literal}}) { return 42; }
                return 1;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(expected, Assert.Single(program.Functions,
            function => function.Name == "Echo<Value>").ReturnType);
        Assert.Equal(expected, Assert.Single(program.Functions[program.EntryPoint].Variables,
            variable => variable.Name == "actual").Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalHelpersForwardWholeCopyAndScalarOutputs(bool instantiate)
    {
        var entry = instantiate
            ? "int32 Main() { require Pick<Value>(new Value(40), out var copy, out var count); return int32(copy.First) + count; }"
            : "int32 Main() { return 0; }";
        var source = Value + """
            [NoThrow] bool Pick<T>(T value, out T result, out int32 count) where T : [Copy] {
                return Forward<T>(value, out result, out count);
            }
            [NoThrow] bool Forward<U>(U value, out U result, out int32 count) where U : [Copy] {
                result = value;
                count = 2;
                return true;
            }
            """ + entry;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.DoesNotContain(program.Functions, function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal));
        if (instantiate)
        {
            var helper = Assert.Single(program.Functions, function => function.Name == "Forward<Value>");
            Assert.Equal(PrimitiveType.Bool, helper.ReturnType);
            Assert.True(helper.Variables[1].IsOut);
            Assert.True(helper.Variables[2].IsOut);
        }
    }

    [Theory]
    [InlineData("return true;")]
    [InlineData("T old = result; result = value; return true;")]
    [InlineData("if (publish) { result = value; } return true;")]
    [InlineData("Other<T>(value, out result); return true;")]
    public void InvalidOutputFlowRejectsEvenInAnUnusedGenericDefinition(string body)
    {
        var source = Value + """
            bool Other<T>(T value, out T result) where T : [Copy] { return false; }
            bool Pick<T>(T value, bool publish, out T result) where T : [Copy] {
            """ + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("T")]
    [InlineData("int32")]
    [InlineData("uint64")]
    public void OutputsDoNotInventSuccessRulesForNonBooleanGenericResults(string resultType)
    {
        var source = Value + resultType
            + " Pick<T>(T value, out T result) where T : [Copy] { result = value; "
            + (resultType == "T" ? "return value;" : "return 0;") + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NonBooleanCopyOutStillRequiresNormalReturnPublication()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Pair {
                public int64 First;
                public Pair(int64 first) { First = first; }
            }
            int32 Fill(bool publish, out Pair result) {
                if (publish) { result = new Pair(42); }
                return 1;
            }
            int32 Main() { return 0; }
            """));
        Assert.Contains(compilation.Diagnostics, error => error.Code == "TRM3016");
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void NonBooleanUniqueOutRemainsUnsupported()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            unique struct Owner {
                public int32 Value;
                public Owner(int32 value) { Value = value; }
                [NoThrow] [Cleanup] public void Close() {}
            }
            int32 Fill(out Owner result) {
                result = new Owner(42);
                return 1;
            }
            int32 Main() { return 0; }
            """));
        Assert.Contains(compilation.Diagnostics, error => error.Code == "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("return result.First;")]
    [InlineData("Pick<Value>(new Value(40), out result); return result.First;")]
    public void FalseOrUnprovenGenericCallsDoNotInitializeCallerOutputs(string after)
    {
        var source = Value + """
            bool Pick<T>(T value, out T result) where T : [Copy] { result = value; return false; }
            int64 MainValue() {
                Pick<Value>(new Value(40), out var result);
            """ + after + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void AnOutputOnlyTypeParameterCanDescribeAnAlwaysAbsentResult()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            [NoThrow] bool None<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                if (None<Value>(out var result)) { return 1; }
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.Equal(PrimitiveType.Bool, Assert.Single(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name == "None<Value>").ReturnType);
    }

    [Fact]
    public void UnusedGenericInitOutputIsCheckedAtDefinition()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            [NoThrow] bool Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            int32 Main() { return 42; }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.DoesNotContain(Assert.IsType<BoundProgram>(compilation.Program).Functions,
            function => function.Name.StartsWith("Fill<", StringComparison.Ordinal));
    }

    [Fact]
    public void UntypedGenericInitDestinationDoesNotInferTheType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            [NoThrow] bool None<T>(init ref<T> result) where T : [Copy] { return false; }
            int32 Main() {
                if (None(init var result)) { return 1; }
                return 42;
            }
            """));
        Assert.Contains(compilation.Diagnostics, error => error.Code == "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("return true;")]
    [InlineData("return result.First;")]
    [InlineData("if (publish) { result = value; } return true;")]
    public void InvalidGenericInitPublicationRejectsAtDefinition(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            bool Fill<T>(T value, bool publish, init ref<T> result) where T : [Copy] {
            """ + body + "} int32 Main() { return 0; }"));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("if (Fill<int32>(int32(1), init var result)) { return 1; } return result;", "TRM3016")]
    [InlineData("require Fill<int32>(int32(1), out var result);", "TRM3011")]
    [InlineData("require Fill<int32>(int32(1), init var result); require Fill<int32>(int32(2), init result);", "TRM3016")]
    public void GenericInitCannotReadVacancyChangeModeOrRepublish(string body, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            bool Fill<T>(T value, init ref<T> result) where T : [Copy] { return false; }
            int32 Main() {
            """ + body + " return 0; }"));
        Assert.Contains(compilation.Diagnostics, error => error.Code == diagnostic);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GenericScalarLookupUsesTheImportedTemplatesDeclaredReturnType()
    {
        var compilation = Compilation.Create([
            SyntaxTree.Parse("namespace Values;" + Value),
            SyntaxTree.Parse("""
                namespace Helpers;
                [NoThrow] uint64 Echo<T>(T ignored, uint64 value) where T : [Copy] { return value; }
                """),
            SyntaxTree.Parse("""
                using Helpers;
                using Values;
                int32 Main() {
                    var result = Echo<Value>(new Value(1), 18446744073709551615);
                    if (result == 18446744073709551615) { return 42; }
                    return 1;
                }
                """),
        ]);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(PrimitiveType.UInt64, Assert.Single(program.Functions[program.EntryPoint].Variables,
            variable => variable.Name == "result").Type);
    }

    [Fact]
    public void ScalarReturnDoesNotPermitAccessToUndeclaredGenericMembers()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            int64 Read<T>(T value) where T : [Copy] { return value.First; }
            int32 Main() { return int32(Read<Value>(new Value(42))); }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3007");
        Assert.Null(compilation.Program);
    }
}
