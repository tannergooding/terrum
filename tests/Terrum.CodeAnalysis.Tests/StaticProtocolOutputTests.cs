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

    [Fact]
    public void GenericOutputCannotDependOnAFormalReturnType()
    {
        var source = Value
            + "T Pick<T>(T value, out T result) where T : [Copy] { result = value; return value; }"
            + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32", false)]
    [InlineData("uint64", true)]
    [InlineData("void", false)]
    [InlineData("void", true)]
    public void GenericNonBooleanOutputsUseNormalReturnPublication(string resultType, bool explicitType)
    {
        var call = explicitType
            ? "Fill<Value>(new Value(40), out var result)"
            : "Fill(new Value(40), out var result)";
        var completion = resultType == "void"
            ? call + "; require result.First == 40; return 42;"
            : resultType + " status = " + call + "; require status == 7;"
                + " require result.First == 40; return 42;";
        var body = resultType == "void"
            ? explicitType ? "result = value; return;" : "result = value;"
            : "result = value; return 7;";
        var source = Value + "[NoThrow] " + resultType
            + " Fill<T>(T value, out T result) where T : [Copy] { "
            + body + "} int32 Main() { " + completion + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.Equal(resultType == "void" ? PrimitiveType.Void
            : resultType == "uint64" ? PrimitiveType.UInt64 : PrimitiveType.Int32, fill.ReturnType);
        Assert.True(fill.Variables[1].IsOut);
    }

    [Theory]
    [InlineData("int32", false)]
    [InlineData("int32", true)]
    [InlineData("uint32", false)]
    [InlineData("uint32", true)]
    [InlineData("int64", false)]
    [InlineData("int64", true)]
    [InlineData("uint64", false)]
    [InlineData("uint64", true)]
    [InlineData("char16", false)]
    [InlineData("char16", true)]
    [InlineData("void", false)]
    [InlineData("void", true)]
    public void GenericNonBooleanInitOutputsPublishOnNormalReturn(string resultType, bool explicitType)
    {
        var call = explicitType
            ? "Fill<Value>(new Value(40), init var result)"
            : "Fill(new Value(40), init var result)";
        var returnValue = resultType == "char16" ? "char16(7)" : "7";
        var completion = resultType == "void"
            ? call + "; require result.First == 40; return 42;"
            : "var status = " + call + "; require status == " + returnValue + ";"
                + " require result.First == 40; return 42;";
        var body = resultType == "void"
            ? explicitType ? "result = value; return;" : "result = value;"
            : "result = value; return " + returnValue + ";";
        var source = Value + "[NoThrow] " + resultType
            + " Fill<T>(T value, init ref<T> result) where T : [Copy] { "
            + body + "} int32 Main() { " + completion + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill<Value>");
        var expectedReturn = resultType switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "char16" => PrimitiveType.Char16,
            _ => PrimitiveType.Void,
        };
        Assert.Equal(expectedReturn, fill.ReturnType);
        Assert.True(fill.Variables[1].IsInit);
    }

    [Theory]
    [InlineData("int32", "if (publish) { result = value; } return 7;")]
    [InlineData("void", "if (publish) { result = value; }")]
    public void GenericNonBooleanOutputsRequirePublicationOnEveryNormalReturn(string resultType, string body)
    {
        var source = Value + resultType
            + " Fill<T>(T value, bool publish, out T result) where T : [Copy] { "
            + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32", "if (publish) { result = value; } return 7;")]
    [InlineData("void", "if (publish) { result = value; }")]
    public void GenericNonBooleanInitOutputsRequirePublicationOnEveryNormalReturn(string resultType, string body)
    {
        var source = Value + resultType
            + " Fill<T>(T value, bool publish, init ref<T> result) where T : [Copy] { "
            + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GenericNonBooleanOutputsKeepVoidTemplatesAndManagedHandlesOutsideTheOutExtension()
    {
        var voidWithoutOutput = Compilation.Create(SyntaxTree.Parse("""
            void Ignore<T>(T value) where T : [Copy] { }
            int32 Main() { return 0; }
            """));
        Assert.True(voidWithoutOutput.HasErrors);
        Assert.Null(voidWithoutOutput.Program);

        var managedHandle = Compilation.Create(SyntaxTree.Parse("""
            int32 Fill<T>(T value, out T result) where T : [Copy] {
                result = value;
                return 7;
            }
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                int32 status = Fill(node, out var copy);
                return status + copy.Value;
            }
            """));
        Assert.True(managedHandle.HasErrors);
        Assert.Null(managedHandle.Program);

        var fixedOutput = Compilation.Create(SyntaxTree.Parse(Value + """
            int32 Fill<T>(T value, out int32 result) where T : [Copy] {
                result = 7;
                return 42;
            }
            int32 Main() { return 0; }
            """));
            Assert.True(fixedOutput.HasErrors);
            Assert.Null(fixedOutput.Program);
    }

    [Theory]
    [InlineData("int32", false)]
    [InlineData("int32", true)]
    [InlineData("uint32", false)]
    [InlineData("uint32", true)]
    [InlineData("int64", false)]
    [InlineData("int64", true)]
    [InlineData("uint64", false)]
    [InlineData("uint64", true)]
    [InlineData("char16", false)]
    [InlineData("char16", true)]
    [InlineData("void", false)]
    [InlineData("void", true)]
    public void GenericNonBooleanMixedOutputsPreserveEachMode(string resultType, bool explicitType)
    {
        var call = explicitType
            ? "Fill<Value, int32>(new Value(40), int32(2), out copied, init var initialized)"
            : "Fill(new Value(40), int32(2), out copied, init var initialized)";
        var returnValue = resultType == "char16" ? "char16(7)" : "7";
        var body = resultType == "void"
            ? "result = value; initialized = second;" + (explicitType ? " return;" : "")
            : "result = value; initialized = second; return " + returnValue + ";";
        var completion = resultType == "void"
            ? "CALL; require copied.First == 40 && initialized == 2; return 42;"
            : "var status = CALL; require status == " + returnValue
                + " && copied.First == 40 && initialized == 2; return 42;";
        var source = Value + """
            RESULT Fill<T, U>(T value, U second, out T result, init ref<U> initialized)
                where T : [Copy] where U : [Copy] {
                BODY
            }
            int32 Main() {
                mut Value copied = new Value(0);
                COMPLETION
            }
            """.Replace("RESULT", resultType, StringComparison.Ordinal)
            .Replace("BODY", body, StringComparison.Ordinal)
            .Replace("COMPLETION", completion, StringComparison.Ordinal)
            .Replace("CALL", call, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill<Value, int32>");
        var expectedReturn = resultType switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "char16" => PrimitiveType.Char16,
            _ => PrimitiveType.Void,
        };
        Assert.Equal(expectedReturn, fill.ReturnType);
        Assert.True(fill.Variables[2].IsOut);
        Assert.False(fill.Variables[2].IsInit);
        Assert.True(fill.Variables[3].IsOut);
        Assert.True(fill.Variables[3].IsInit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericMixedOutputsCanShareOneTypeParameter(bool explicitType)
    {
        var call = explicitType
            ? "Fill<Value>(new Value(40), out copied, init var initialized)"
            : "Fill(new Value(40), out copied, init var initialized)";
        var source = Value + """
            int32 Fill<T>(T value, out T result, init ref<T> initialized) where T : [Copy] {
                result = value;
                initialized = value;
                return 7;
            }
            int32 Main() {
                mut Value copied = new Value(0);
                int32 status = CALL;
                require status == 7 && copied.First == 40 && initialized.First == 40;
                return 42;
            }
            """.Replace("CALL", call, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.True(fill.Variables[1].IsOut);
        Assert.False(fill.Variables[1].IsInit);
        Assert.True(fill.Variables[2].IsOut);
        Assert.True(fill.Variables[2].IsInit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericFixedScalarOutputsCanAccompanyGenericOutputs(bool explicitType)
    {
        var call = explicitType
            ? "Fill<Value>(new Value(40), out copied, out var count, init var score)"
            : "Fill(new Value(40), out copied, out var count, init var score)";
        var source = Value + """
            int32 Fill<T>(T value, out T result, out int32 count, init ref<float64> score)
                where T : [Copy] {
                result = value;
                count = 2;
                score = float64(3.5);
                return 7;
            }
            int32 Main() {
                mut Value copied = new Value(0);
                int32 status = CALL;
                require status == 7 && copied.First == 40 && count == 2 && score == float64(3.5);
                return 42;
            }
            """.Replace("CALL", call, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var fill = Assert.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.True(fill.Variables[1].IsOut);
        Assert.False(fill.Variables[1].IsInit);
        Assert.True(fill.Variables[2].IsOut);
        Assert.False(fill.Variables[2].IsInit);
        Assert.True(fill.Variables[3].IsOut);
        Assert.True(fill.Variables[3].IsInit);
    }

    [Theory]
    [InlineData("count = 2;")]
    [InlineData("score = float64(3.5);")]
    public void GenericFixedOutputsRequirePublicationOnEveryNormalReturn(string body)
    {
        var source = Value
            + "int32 Fill<T>(T value, out T result, out int32 count, init ref<float64> score)"
            + " where T : [Copy] { result = value; " + body + " return 7; }"
            + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32", "result = value; return 7;")]
    [InlineData("int32", "initialized = second; return 7;")]
    [InlineData("void", "result = value;")]
    [InlineData("void", "initialized = second;")]
    public void GenericNonBooleanMixedOutputsRequirePublicationOnEveryNormalReturn(
        string resultType, string body)
    {
        var source = Value + resultType
            + " Fill<T, U>(T value, U second, bool publish, out T result, init ref<U> initialized)"
            + " where T : [Copy] where U : [Copy] { " + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GenericMixedOutputDoesNotAdmitManagedOrdinaryOut()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Fill<T, U>(T value, U second, out T result, init ref<U> initialized)
                where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                return 7;
            }
            int32 Main() {
                require ManagedNode.TryCreate(42, out var node);
                int32 status = Fill(node, int32(3), out var copy, init var count);
                return status + copy.Value + count;
            }
            """));
        Assert.Contains(compilation.Diagnostics, diagnostic => diagnostic.Code == "TRM3011");
        Assert.Null(compilation.Program);
    }

    [Theory]
    [InlineData("int32(previous.First)")]
    [InlineData("int32(initialized)")]
    [InlineData("count")]
    [InlineData("int32(score)")]
    public void GenericMixedOutputIsNotReadableAfterExceptionalDeparture(string catchRead)
    {
        var source = """
            struct Pair {
                public int64 First;
                public int64 Second;
                public Pair(int64 first, int64 second) { First = first; Second = second; }
            }
            int32 Fill<T, U>(T value, U second, bool complete, out T result,
                init ref<U> initialized, out int32 count, init ref<float64> score)
                where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                count = 2;
                score = float64(3.5);
                require complete;
                return 7;
            }
            int32 Main() {
                mut Pair previous = new Pair(1, 2);
                init int64 initialized;
                mut int32 count = 0;
                init float64 score;
                unsafe {
                    try {
                        Fill(new Pair(40, 2), int64(3), false, out previous, init initialized,
                            out count, init score);
                    } catch {
                        return CATCH_READ;
                    }
                }
                return 0;
            }
            """.Replace("CATCH_READ", catchRead, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GenericNonBooleanOutputIsNotReadableAfterExceptionalDeparture()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Value {
                public int64 Number;
                public Value(int64 number) { Number = number; }
            }
            int32 Fill<T>(T value, bool complete, out T result) where T : [Copy] {
                result = value;
                require complete;
                return 7;
            }
            int32 Main() {
                mut Value result = new Value(1);
                unsafe {
                    try {
                        Fill(new Value(40), false, out result);
                    } catch {
                        return int32(result.Number);
                    }
                }
                return 0;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.True(compilation.HasErrors);
        Assert.Null(compilation.Program);
    }

    [Fact]
    public void GenericNonBooleanInitOutputIsNotReadableAfterExceptionalDeparture()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            struct Value {
                public int64 Number;
                public Value(int64 number) { Number = number; }
            }
            int32 Fill<T>(T value, bool complete, init ref<T> result) where T : [Copy] {
                result = value;
                require complete;
                return 7;
            }
            int32 Main() {
                init Value result;
                unsafe {
                    try {
                        Fill(new Value(40), false, init result);
                    } catch {
                        return int32(result.Number);
                    }
                }
                return 0;
            }
            """), BoundRuntimePolicy.CheckedExceptions);
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
