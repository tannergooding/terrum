// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Semantics.Generics;

public sealed class StaticProtocolControlFlowTests
{
    private const string Declarations = """
        interface Multiplication<A, B, C> {
            static C operator *(A left, B right);
        }
        struct Meters : Multiplication<Meters, Meters, Meters> {
            public int32 Value;
            public Meters(int32 value) { Value = value; }
            public static Meters operator *(Meters left, Meters right) {
                return new Meters(left.Value * right.Value);
            }
            public static Meters operator +(Meters left, Meters right) {
                return new Meters(left.Value + right.Value);
            }
        }
        """;

    private const string PowerBody = """
        mut T result = one;
        mut T factor = value;
        mut uint32 remaining = exponent;
        while (remaining != 0) {
            if ((remaining & 1) != 0) { result = result * factor; }
            remaining = remaining / 2;
            if (remaining == 0) { break; }
            factor = factor * factor;
        }
        return result;
        """;

    private const string NoThrowDeclarations = """
        interface Multiplication<A, B, C> {
            [NoThrow] static C operator *(A left, B right);
        }
        """;

    [Test]
    public void StructuredTemplateUsesScalarFlowAndOnlyDeclaredOperators()
    {
        var compilation = Compile(PowerBody, instantiate: true);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var specialization = global::System.Linq.Enumerable.Single(program.Functions, function => function.Name.StartsWith("Power<", StringComparison.Ordinal));
        Assert.That(global::System.Linq.Enumerable.Any(specialization.Body.Statements,statement => statement is BoundWhile),Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(program.Structures,structure => structure.Name == "$T"),Is.False);
        Assert.That(global::System.Linq.Enumerable.Any(program.Functions,function => function.Name.Contains("$T", StringComparison.Ordinal)),Is.False);
    }

    [Test]
    public void AnUnusedStructuredTemplateIsCheckedWithoutAConcreteWitness()
    {
        var compilation = Compile(PowerBody, instantiate: false);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(global::System.Linq.Enumerable.Any(((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))().Functions,function => function.Name.StartsWith("Power<", StringComparison.Ordinal)),Is.False);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ScopedForContinueAndRequireBindAtDefinitionAndSpecialization(bool instantiate)
    {
        var compilation = Compile("""
            mut T result = one;
            for (mut uint32 i = 0; i < exponent; i = i + 1) {
                if (i == 0) { continue; }
                else { result = result * value; }
            }
            require (exponent < 100);
            return result;
            """, instantiate);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase("return value + one;","TRM3007")]
    [TestCase("return value.Value;","TRM3007")]
    [TestCase("return new T();","TRM3007")]
    [TestCase("mut uint32 count = exponent; count = value; return one;","TRM3007")]
    [TestCase("uint32 count = exponent; count = 0; return one;","TRM3008")]
    [TestCase("mut T result = value; mut T result = one; return result;","TRM3004")]
    [TestCase("if (value) { return one; } return value;","TRM3007")]
    [TestCase("{ mut T local = value; } return local;","TRM3006")]
    [TestCase("break; return one;","TRM3008")]
    [TestCase("if (exponent == 0) { return one; }","TRM3012")]
    [TestCase("return Power<Meters>(value, one, exponent);","TRM3007")]
    [TestCase("require (exponent == 0); return Missing(value);","TRM3011")]
    [TestCase("mut T result = one; for (mut uint32 i = 0; i < exponent; i = i + 1) { if (i == 0) { continue; } result = result / value; } return result;","TRM3007")]
    public void InvalidDefinitionRejectsEvenWithoutInstantiation(string body, string expected)
    {
        var compilation = Compile(body, instantiate: false);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == expected),Is.True);
        Assert.That(compilation.Program, Is.Null);
    }

    [Test]
    public void InvalidDefinitionCannotBeRescuedByConformingConcreteType()
    {
        var compilation = Compile("return value + one;", instantiate: true);
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3007"),Is.True);
    }

    [Test]
    public void InvalidDefinitionIsReportedOnItsSourceSnapshot()
    {
        var tree = SyntaxTree.Parse(Declarations + Generic("return value + one;") + "int32 Main() { return 0; }",
            "invalid-template.terrum");
        var compilation = Compilation.Create(tree);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.SourceDiagnostics,item => item.Diagnostic.Code == "TRM3007"
            && item.SyntaxTree == tree),Is.True);
    }

    [Test]
    public void NoThrowStructuredDefinitionRejectsScalarFailuresWithoutInstantiation()
    {
        var source = NoThrowDeclarations + "[NoThrow] " + Generic("""
            mut uint32 half = exponent / 2;
            if (half == 0) { return one; }
            return value * one;
            """) + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,diagnostic => diagnostic.Code == "TRM3017"),Is.True);
    }

    [Test]
    public void NoThrowStructuredDefinitionAcceptsOnlyConstrainedNoThrowCalls()
    {
        var source = NoThrowDeclarations + "[NoThrow] " + Generic("""
            if (exponent == 0) { return one; }
            return value * one;
            """) + "int32 Main() { return 0; }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    private static Compilation Compile(string body, bool instantiate)
    {
        var main = instantiate
            ? "int32 Main() { return Power<Meters>(new Meters(2), new Meters(1), 3).Value + 34; }"
            : "int32 Main() { return 0; }";
        return Compilation.Create(SyntaxTree.Parse(Declarations + Generic(body) + main));
    }

    private static string Generic(string body) =>
        "T Power<T>(T value, T one, uint32 exponent) where T : Multiplication<T, T, T>, [Copy] {"
        + body + "}";
}
