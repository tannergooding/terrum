// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class ScannerHostIntegerParsingTests
{
    [TestCase("12__34",(long)(1234))]
    [TestCase("0x__FF_FF",(long)(65535))]
    [TestCase("0b__10_01",(long)(9))]
    public void BindsIntegerWithoutDecodedScannerValues(string spelling, long expected)
    {
        var lexical = Lexer.Lex(SourceText.From($"int32 Main() {{ return {spelling}; }}"));
        var compilation = Compilation.Create(SyntaxTree.Parse(lexical));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions[0];
        var statement = ((global::System.Func<BoundReturn>)(() => { var __nunitActual1 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual1!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual2 = statement.Value; Assert.That(__nunitActual2, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual2!; }))().Value, Is.EqualTo(expected));
    }

    [Test]
    public void RejectsOverflowingIntegerWithoutDecodedValues()
    {
        var lexical = Lexer.Lex(SourceText.From("uint64 Main() { return 18446744073709551616; }"));
        var compilation = Compilation.Create(SyntaxTree.Parse(lexical));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3013"),Is.True);
    }
    [TestCase("public int32 Read() { return -2147483648; }",int.MinValue)]
    [TestCase("public int64 Read() { return -9223372036854775808; }",long.MinValue)]
    public void BindsSignedMinimumWithoutDecodedScannerValues(string source, long expected)
    {
        var lexical = Lexer.Lex(SourceText.From(source));
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(lexical));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var function = ((global::System.Func<BoundProgram>)(() => { var __nunitActual3 = compilation.Program; Assert.That(__nunitActual3, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual3!; }))().Functions[0];
        var statement = ((global::System.Func<BoundReturn>)(() => { var __nunitActual4 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual4, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual4!; }))();
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual5 = statement.Value; Assert.That(__nunitActual5, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual5!; }))().Value, Is.EqualTo(expected));
    }
}
