// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

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
    [TestCase("int32","-2147483648",PrimitiveType.Int32)]
    [TestCase("uint32","4294967295",PrimitiveType.UInt32)]
    [TestCase("int64","-9223372036854775808",PrimitiveType.Int64)]
    [TestCase("uint64","18446744073709551615",PrimitiveType.UInt64)]
    [TestCase("char16","char16(55296)",PrimitiveType.Char16)]
    [TestCase("bool","true",PrimitiveType.Bool)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Echo<Value>").ReturnType, Is.EqualTo(expected));
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions[program.EntryPoint].Variables, variable => variable.Name == "actual").Type, Is.EqualTo(expected));
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.IsExternal
            || function.Name.Contains('$', StringComparison.Ordinal)),Is.False);
        if (instantiate)
        {
            var helper = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Forward<Value>");
            Assert.That(helper.ReturnType, Is.EqualTo(PrimitiveType.Bool));
            Assert.That(helper.Variables[1].IsOut, Is.True);
            Assert.That(helper.Variables[2].IsOut, Is.True);
        }
    }
    [TestCase("return true;")]
    [TestCase("T old = result; result = value; return true;")]
    [TestCase("if (publish) { result = value; } return true;")]
    [TestCase("Other<T>(value, out result); return true;")]
    public void InvalidOutputFlowRejectsEvenInAnUnusedGenericDefinition(string body)
    {
        var source = Value + """
            bool Other<T>(T value, out T result) where T : [Copy] { return false; }
            bool Pick<T>(T value, bool publish, out T result) where T : [Copy] {
            """ + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericOutputCannotDependOnAFormalReturnType()
    {
        var source = Value
            + "T Pick<T>(T value, out T result) where T : [Copy] { result = value; return value; }"
            + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32",false)]
    [TestCase("uint64",true)]
    [TestCase("void",false)]
    [TestCase("void",true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.That(fill.ReturnType, Is.EqualTo(resultType == "void" ? PrimitiveType.Void
            : resultType == "uint64" ? PrimitiveType.UInt64 : PrimitiveType.Int32));
        Assert.That(fill.Variables[1].IsOut, Is.True);
    }
    [TestCase("int32",false)]
    [TestCase("int32",true)]
    [TestCase("uint32",false)]
    [TestCase("uint32",true)]
    [TestCase("int64",false)]
    [TestCase("int64",true)]
    [TestCase("uint64",false)]
    [TestCase("uint64",true)]
    [TestCase("char16",false)]
    [TestCase("char16",true)]
    [TestCase("void",false)]
    [TestCase("void",true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value>");
        var expectedReturn = resultType switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "char16" => PrimitiveType.Char16,
            _ => PrimitiveType.Void,
        };
        Assert.That(fill.ReturnType, Is.EqualTo(expectedReturn));
        Assert.That(fill.Variables[1].IsInit, Is.True);
    }
    [TestCase("int32","if (publish) { result = value; } return 7;")]
    [TestCase("void","if (publish) { result = value; }")]
    public void GenericNonBooleanOutputsRequirePublicationOnEveryNormalReturn(string resultType, string body)
    {
        var source = Value + resultType
            + " Fill<T>(T value, bool publish, out T result) where T : [Copy] { "
            + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32","if (publish) { result = value; } return 7;")]
    [TestCase("void","if (publish) { result = value; }")]
    public void GenericNonBooleanInitOutputsRequirePublicationOnEveryNormalReturn(string resultType, string body)
    {
        var source = Value + resultType
            + " Fill<T>(T value, bool publish, init ref<T> result) where T : [Copy] { "
            + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericNonBooleanOutputsKeepVoidTemplatesAndManagedHandlesOutsideTheOutExtension()
    {
        var voidWithoutOutput = Compilation.Create(SyntaxTree.Parse("""
            void Ignore<T>(T value) where T : [Copy] { }
            int32 Main() { return 0; }
            """));
        Assert.That(voidWithoutOutput.HasErrors, Is.True);
        Assert.That(voidWithoutOutput.Program, Is.Null);

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
        Assert.That(managedHandle.HasErrors, Is.True);
        Assert.That(managedHandle.Program, Is.Null);

        var fixedOutput = Compilation.Create(SyntaxTree.Parse(Value + """
            int32 Fill<T>(T value, out int32 result) where T : [Copy] {
                result = 7;
                return 42;
            }
            int32 Main() { return 0; }
            """));
        Assert.That(fixedOutput.HasErrors, Is.True);
        Assert.That(fixedOutput.Program, Is.Null);
    }
    [TestCase("int32",false)]
    [TestCase("int32",true)]
    [TestCase("uint32",false)]
    [TestCase("uint32",true)]
    [TestCase("int64",false)]
    [TestCase("int64",true)]
    [TestCase("uint64",false)]
    [TestCase("uint64",true)]
    [TestCase("char16",false)]
    [TestCase("char16",true)]
    [TestCase("void",false)]
    [TestCase("void",true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value, int32>");
        var expectedReturn = resultType switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "char16" => PrimitiveType.Char16,
            _ => PrimitiveType.Void,
        };
        Assert.That(fill.ReturnType, Is.EqualTo(expectedReturn));
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[2].IsInit, Is.False);
        Assert.That(fill.Variables[3].IsOut, Is.True);
        Assert.That(fill.Variables[3].IsInit, Is.True);
    }
    [TestCase("bool",false)]
    [TestCase("bool",true)]
    [TestCase("char16",false)]
    [TestCase("char16",true)]
    [TestCase("int32",false)]
    [TestCase("int32",true)]
    [TestCase("uint32",false)]
    [TestCase("uint32",true)]
    [TestCase("int64",false)]
    [TestCase("int64",true)]
    [TestCase("uint64",false)]
    [TestCase("uint64",true)]
    [TestCase("float32",false)]
    [TestCase("float32",true)]
    [TestCase("float64",false)]
    [TestCase("float64",true)]
    public void GenericFixedScalarOutputTypesRetainTheirMode(string outputType, bool isInit)
    {
        var parameter = isInit ? $"init ref<{outputType}> side" : $"out {outputType} side";
        var argument = isInit ? "init var side" : "out var side";
        var (assignment, predicate) = outputType switch {
            "bool" => ("true", "side"),
            "char16" => ("char16(7)", "side == char16(7)"),
            "int32" or "uint32" or "int64" or "uint64" => ("7", "side == 7"),
            "float32" => ("float32(7.5)", "side == float32(7.5)"),
            "float64" => ("float64(7.5)", "side == float64(7.5)"),
            _ => throw new InvalidOperationException($"Unexpected output type '{outputType}'."),
        };
        var source = Value + $$"""
            int32 Fill<T>(T value, out T result, {{parameter}}) where T : [Copy] {
                result = value;
                side = {{assignment}};
                return 7;
            }
            int32 Main() {
                int32 status = Fill(new Value(40), out var copied, {{argument}});
                require status == 7 && copied.First == 40 && {{predicate}};
                return 42;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual5 = compilation.Program; Assert.That(__nunitActual5, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual5!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.That(fill.Variables[1].IsOut, Is.True);
        Assert.That(fill.Variables[1].IsInit, Is.False);
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[2].IsInit, Is.EqualTo(isInit));
        var expectedType = outputType switch {
            "bool" => PrimitiveType.Bool,
            "char16" => PrimitiveType.Char16,
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "float32" => PrimitiveType.Float32,
            _ => PrimitiveType.Float64,
        };
        Assert.That(fill.Variables[2].Type, Is.EqualTo(expectedType));
    }
    [TestCase(false)]
    [TestCase(true)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual6 = compilation.Program; Assert.That(__nunitActual6, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual6!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.That(fill.Variables[1].IsOut, Is.True);
        Assert.That(fill.Variables[1].IsInit, Is.False);
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[2].IsInit, Is.True);
    }
    [TestCase("Fill(new Value(1), new Value(2), init copied, out var initialized);")]
    [TestCase("Fill(new Value(1), new Value(2), out copied, init copied);")]
    [TestCase("Fill(new Value(1), new Value(2), out copied, init initialized);")]
    [TestCase("Fill(new Value(1), new Value(2), out wrong, init var initialized);")]
    public void GenericMixedOutputsRejectModeAndDestinationMismatches(string call)
    {
        var source = Value + """
            int32 Fill<T, U>(T value, U second, out T result, init ref<U> initialized)
                where T : [Copy] where U : [Copy] {
                result = value;
                initialized = second;
                return 7;
            }
            int32 Main() {
                mut Value copied = new Value(0);
                mut Value initialized = new Value(0);
                mut int32 wrong = 0;
                CALL
                return 42;
            }
            """.Replace("CALL", call, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32",false)]
    [TestCase("int32",true)]
    [TestCase("uint32",false)]
    [TestCase("uint32",true)]
    [TestCase("int64",false)]
    [TestCase("int64",true)]
    [TestCase("uint64",false)]
    [TestCase("uint64",true)]
    [TestCase("char16",false)]
    [TestCase("char16",true)]
    [TestCase("void",false)]
    [TestCase("void",true)]
    public void GenericFixedScalarOutputsCanAccompanyGenericOutputs(string resultType, bool explicitType)
    {
        var call = explicitType
            ? "Fill<Value>(new Value(40), out copied, out var count, init var score)"
            : "Fill(new Value(40), out copied, out var count, init var score)";
        var returnValue = resultType == "char16" ? "char16(7)" : "7";
        var outputs = "result = value; count = 2; score = float64(3.5);";
        var body = resultType == "void" ? outputs : outputs + " return " + returnValue + ";";
        var completion = resultType == "void"
            ? "CALL; require copied.First == 40 && count == 2 && score == float64(3.5); return 42;"
            : "var status = CALL; require status == " + returnValue
                + " && copied.First == 40 && count == 2 && score == float64(3.5); return 42;";
        var source = Value + """
            RESULT Fill<T>(T value, out T result, out int32 count, init ref<float64> score)
                where T : [Copy] {
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual7 = compilation.Program; Assert.That(__nunitActual7, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual7!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value>");
        var expectedReturn = resultType switch {
            "int32" => PrimitiveType.Int32,
            "uint32" => PrimitiveType.UInt32,
            "int64" => PrimitiveType.Int64,
            "uint64" => PrimitiveType.UInt64,
            "char16" => PrimitiveType.Char16,
            _ => PrimitiveType.Void,
        };
        Assert.That(fill.ReturnType, Is.EqualTo(expectedReturn));
        Assert.That(fill.Variables[1].IsOut, Is.True);
        Assert.That(fill.Variables[1].IsInit, Is.False);
        Assert.That(fill.Variables[2].IsOut, Is.True);
        Assert.That(fill.Variables[2].IsInit, Is.False);
        Assert.That(fill.Variables[3].IsOut, Is.True);
        Assert.That(fill.Variables[3].IsInit, Is.True);
    }
    [TestCase("count = 2;")]
    [TestCase("score = float64(3.5);")]
    public void GenericFixedOutputsRequirePublicationOnEveryNormalReturn(string body)
    {
        var source = Value
            + "int32 Fill<T>(T value, out T result, out int32 count, init ref<float64> score)"
            + " where T : [Copy] { result = value; " + body + " return 7; }"
            + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("count = 2;")]
    [TestCase("score = float64(3.5);")]
    public void GenericFixedVoidOutputsRequirePublicationOnFallthrough(string body)
    {
        var source = Value
            + "void Fill<T>(T value, out T result, out int32 count, init ref<float64> score)"
            + " where T : [Copy] { result = value; " + body + " }"
            + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericFixedOutputsDoNotInferGenericTypeFromDestinations()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Fill<T>(init ref<T> result, out int32 count, init ref<float64> score)
                where T : [Copy] {
                for (;;) { }
            }
            int32 Main() {
                Fill(init var result, out var count, init var score);
                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericFixedOutputsRequireAGenericTypedOutput()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Fill<T>(T value, out int32 count, init ref<float64> score)
                where T : [Copy] {
                count = 2;
                score = float64(3.5);
                return 7;
            }
            int32 Main() {
                int32 status = Fill(int32(40), out var count, init var score);
                return status + count + int32(score);
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("float32","float32(-0.0)")]
    [TestCase("float64","float64(-0.0)")]
    public void GenericFloatingResultsPreserveOutputModes(string resultType, string resultValue)
    {
        var source = Value + resultType
            + " Fill<T>(T value, out T copied, init ref<T> initialized) where T : [Copy] {"
            + " copied = value; initialized = value; return " + resultValue + "; }"
            + "int32 Main() { mut Value copied = new Value(0);"
            + resultType + " status1 = Fill(new Value(40), out copied, init var initialized1);"
            + resultType + " status2 = Fill<Value>(new Value(42), out copied, init var initialized2);"
            + "return 42; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual8 = compilation.Program; Assert.That(__nunitActual8, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual8!; }))();
        var fill = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name == "Fill<Value>");
        Assert.That(fill.ReturnType, Is.EqualTo(resultType == "float32" ? PrimitiveType.Float32 : PrimitiveType.Float64));
        Assert.That(fill.Variables[1].IsOut, Is.True);
        Assert.That(fill.Variables[1].IsInit, Is.False);
        Assert.That(fill.Variables[2].IsInit, Is.True);
    }
    [TestCase("float32","result = value; initialized = value; count = 2; return float32(1.5);")]
    [TestCase("float64","result = value; initialized = value; score = float64(3.5); return float64(1.5);")]
    [TestCase("float32","initialized = value; count = 2; score = float64(3.5); return float32(1.5);")]
    [TestCase("float64","result = value; count = 2; score = float64(3.5); return float64(1.5);")]
    public void GenericFloatingResultRequiresEveryOutputOnNormalReturn(string resultType, string body)
    {
        var source = Value
            + resultType + " Fill<T>(T value, out T result, init ref<T> initialized,"
            + " out int32 count, init ref<float64> score)"
            + " where T : [Copy] { " + body + " } int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("float32 Create<T>(T value) where T : [Copy] { return float32(1.5); }")]
    [TestCase("float32 Fill<T>(T value, float32 threshold, out T result) where T : [Copy] { result = value; return float32(1.5); }")]
    public void GenericFloatingResultDoesNotWidenTemplateTypes(string declaration)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            declaration + " int32 Main() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericFloatingResultRequiresAGenericOutputAnchor()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            float32 Fill<T>(T value, out int32 count, init ref<float64> score)
                where T : [Copy] {
                count = 2;
                score = float64(3.5);
                return float32(1.5);
            }
            int32 Main() {
                Fill(int32(42), out var count, init var score);
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericFloatingResultAndFixedOutputsDoNotInferOutputTypes()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            [NoThrow] float64 None<T>(int32 marker, init ref<T> result, out int32 count,
                init ref<float64> score) where T : [Copy] {
                for (;;) { }
            }
            int32 Main() {
                float64 status = None(1, init var result, out var count, init var score);
                require status == float64(0.0);
                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void GenericFloatingResultDoesNotRelaxFinallyNoThrow()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            float64 Fill<T>(T value, out T copied) where T : [Copy] {
                copied = value;
                try {
                    return float64(0.0);
                } finally {
                    require false;
                }
            }
            int32 Main() { return 0; }
            """), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("copied")]
    [TestCase("initialized")]
    [TestCase("count")]
    [TestCase("int32(score)")]
    public void GenericFloatingResultOutputsAreUnreadableAfterExceptionalDeparture(string catchRead)
    {
        var source = """
            float64 Fill<T>(T value, bool complete, out T copied, init ref<T> initialized,
                out int32 count, init ref<float64> score) where T : [Copy] {
                copied = value;
                initialized = value;
                count = 2;
                score = float64(3.5);
                require complete;
                return float64(1.5);
            }
            int32 Main() {
                mut int32 copied = 0;
                init int32 initialized;
                mut int32 count = 0;
                init float64 score;
                unsafe {
                    try {
                        Fill(1, false, out copied, init initialized, out count, init score);
                    } catch {
                        return int32(CATCH_READ);
                    }
                }
                return 0;
            }
            """.Replace("CATCH_READ", catchRead, StringComparison.Ordinal);
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32","result = value; return 7;")]
    [TestCase("int32","initialized = second; return 7;")]
    [TestCase("void","result = value;")]
    [TestCase("void","initialized = second;")]
    public void GenericNonBooleanMixedOutputsRequirePublicationOnEveryNormalReturn(
        string resultType, string body)
    {
        var source = Value + resultType
            + " Fill<T, U>(T value, U second, bool publish, out T result, init ref<U> initialized)"
            + " where T : [Copy] where U : [Copy] { " + body + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int32(previous.First)")]
    [TestCase("int32(initialized)")]
    [TestCase("count")]
    [TestCase("int32(score)")]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,error => error.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,error => error.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("return result.First;")]
    [TestCase("Pick<Value>(new Value(40), out result); return result.First;")]
    public void FalseOrUnprovenGenericCallsDoNotInitializeCallerOutputs(string after)
    {
        var source = Value + """
            bool Pick<T>(T value, out T result) where T : [Copy] { result = value; return false; }
            int64 MainValue() {
                Pick<Value>(new Value(40), out var result);
            """ + after + "} int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void AnOutputOnlyTypeParameterCanDescribeAnAlwaysAbsentResult()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            [NoThrow] bool None<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                if (None<Value>(out var result)) { return 1; }
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Single(((global::System.Func<BoundProgram>)(() => { var __nunitActual9 = compilation.Program; Assert.That(__nunitActual9, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual9!; }))().Functions, function => function.Name == "None<Value>").ReturnType, Is.EqualTo(PrimitiveType.Bool));
    }

    [Test]
    public void UnusedGenericInitOutputIsCheckedAtDefinition()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            [NoThrow] bool Fill<T>(T value, init ref<T> result) where T : [Copy] {
                result = value;
                return true;
            }
            int32 Main() { return 42; }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))().Functions,function => function.Name.StartsWith("Fill<", StringComparison.Ordinal)),Is.False);
    }

    [Test]
    public void UntypedGenericInitDestinationDoesNotInferTheType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            [NoThrow] bool None<T>(init ref<T> result) where T : [Copy] { return false; }
            int32 Main() {
                if (None(init var result)) { return 1; }
                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,error => error.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("return true;")]
    [TestCase("return result.First;")]
    [TestCase("if (publish) { result = value; } return true;")]
    public void InvalidGenericInitPublicationRejectsAtDefinition(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            bool Fill<T>(T value, bool publish, init ref<T> result) where T : [Copy] {
            """ + body + "} int32 Main() { return 0; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("if (Fill<int32>(int32(1), init var result)) { return 1; } return result;","TRM3016")]
    [TestCase("require Fill<int32>(int32(1), out var result);","TRM3011")]
    [TestCase("require Fill<int32>(int32(1), init var result); require Fill<int32>(int32(2), init result);","TRM3016")]
    public void GenericInitCannotReadVacancyChangeModeOrRepublish(string body, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            bool Fill<T>(T value, init ref<T> result) where T : [Copy] { return false; }
            int32 Main() {
            """ + body + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,error => error.Code == diagnostic),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual11 = compilation.Program; Assert.That(__nunitActual11, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual11!; }))();
        Assert.That(global::System.Linq.Enumerable.Single(program.Functions[program.EntryPoint].Variables, variable => variable.Name == "result").Type, Is.EqualTo(PrimitiveType.UInt64));
    }

    [Test]
    public void ScalarReturnDoesNotPermitAccessToUndeclaredGenericMembers()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Value + """
            int64 Read<T>(T value) where T : [Copy] { return value.First; }
            int32 Main() { return int32(Read<Value>(new Value(42))); }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
}
