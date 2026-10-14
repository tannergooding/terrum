// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

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
    [TestCase("var result = Identity(42);","TRM3011")]
    [TestCase("Value result = Identity(42);","TRM3014")]
    [TestCase("Value result = Identity(new Other(42));","TRM3007")]
    [TestCase("var result = Both(new Value(1), new Other(2));","TRM3007")]
    [TestCase("var result = Identity(new Owner(42));","TRM3007")]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,error => error.Code == diagnostic),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions,function => function.Name == "None<Value>"),Is.True);
    }

    [Test]
    public void OutVarAloneDoesNotEstablishTheGenericType()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            bool None<T>(out T result) where T : [Copy] { return false; }
            int32 Main() {
                if (None(out var result)) { return result.Number; }
                return 42;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void KnownScalarReturnCannotBypassMissingStructInference()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            int32 Read<T>(T value, int32 count) where T : [Copy] { return count; }
            int32 Main() { return Read(42, 1) + Read(new Value(41), 1); }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("bool","true")]
    [TestCase("char16","char16(65)")]
    [TestCase("int32","int32(42)")]
    [TestCase("uint32","uint32(42)")]
    [TestCase("int64","int64(42)")]
    [TestCase("uint64","uint64(42)")]
    [TestCase("float32","float32(42)")]
    [TestCase("float64","float64(42)")]
    public void CopyScalarSpecializationAcceptsExplicitAndInferredArguments(string type, string value)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + $$"""
            int32 Main() {
                {{type}} first = Identity<{{type}}>({{value}});
                {{type}} second = Identity(first);
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))().Functions,function => function.Name == $"Identity<{type}>"),Is.True);
    }

    [Test]
    public void ExpectedScalarResultSuppliesTypeOnlyAfterTypedArguments()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            int32 Main() {
                int64 fromContext = Identity(42);
                int64 typed = Identity(int64(42));
                float32 floatFromContext = Identity(42);
                float64 wideFromContext = Identity(42);
                return int32(fromContext + typed);
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var functions = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))().Functions;
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.Name == "Identity<float32>"),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(functions,function => function.Name == "Identity<float64>"),Is.True);
    }
    [TestCase("float32")]
    [TestCase("float64")]
    public void ConcreteFloatingGenericArgumentsDoNotWidenGetterResults(string type)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse($$"""
            struct Value {
                public {{type}} Number { get { return {{type}}(42); } }
            }
            int32 Main() {
                Value value = new Value();
                return int32(value.Number);
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("int64 result = Identity(int32(42));")]
    [TestCase("int32 result = Identity<int32>(int64(42));")]
    public void TypedScalarArgumentCannotBeRetargetedByResultOrExplicitType(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values
            + "int32 Main() { " + body + " return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void FailedScalarOutputLeavesTargetUnavailableUntilRepublished()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values + """
            bool TryCopy<T>(T value, bool publish, out T result) where T : [Copy] {
                result = value;
                return publish;
            }
            int32 Main() {
                mut int32 target = int32(7);
                if (TryCopy<int32>(int32(42), false, out target)) { return 1; }
                return target;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void OwnedBufferIsNotACopyGenericArgument()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values
            + "int32 Main() { require OwnedInt32Buffer.TryCreate(1, out var raw);"
            + " mut OwnedInt32Buffer buffer = move raw; var result = Identity(buffer); return 0; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("return value + value;")]
    [TestCase("return new T();")]
    [TestCase("return int32(value);")]
    [TestCase("return value.Number;")]
    public void CopyOnlyDefinitionDoesNotBorrowScalarCapabilities(string body)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(Values
            + "int32 Inspect<T>(T value) where T : [Copy] { " + body + " }"
            + "int32 Main() { return Inspect(int32(42)); }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase("Add<int32>(int32(1))")]
    [TestCase("Add(int32(1))")]
    [TestCase("Add<float32>(float32(1))")]
    [TestCase("Add<float64>(float64(1))")]
    public void PrimitiveDoesNotAcquireOperatorWitnessAfterStructSpecialization(string call)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            interface Addition<A, B, C> { static C operator +(A left, B right); }
            struct Value : Addition<Value, Value, Value> {
                public int32 Number;
                public Value(int32 number) { Number = number; }
                public static Value operator +(Value left, Value right) {
                    return new Value(left.Number + right.Number);
                }
            }
            T Add<T>(T value) where T : Addition<T, T, T>, [Copy] { return value + value; }
            int32 Main() {
                var valid = Add(new Value(1));
                return
            """ + " " + call + "; }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3016"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }
    [TestCase(false)]
    [TestCase(true)]
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
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        }
        else
        {
            Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
        }
    }

    [Test]
    public void InferredHelperCannotBorrowCallersStrongerProtocolWitnesses()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            interface Addition<A, B, C> { static C operator +(A left, B right); }
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void VariableAndAmbiguousImportedTemplatesNeverSelectAnInferredCall()
    {
        var shadowed = Compilation.Create(SyntaxTree.Parse(Values + """
            int32 Main() {
                int32 Identity = 1;
                var result = Identity(new Value(42));
                return 0;
            }
            """));
        Assert.That(global::System.Linq.Enumerable.Any(shadowed.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);

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
        Assert.That(global::System.Linq.Enumerable.Any(ambiguous.Diagnostics,diagnostic => diagnostic.Code == "TRM3011"),Is.True);
    }
}
