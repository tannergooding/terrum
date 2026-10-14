// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LiteralSyntaxTests
{
    [TestCase("int32 Main() { return int32(42); }")]
    [TestCase("int32 Main() { int64 wide = int64(50000 * 50000); return int32(wide / 2500000000); }")]
    [TestCase("int32 Main() { int64 min = int64(-9223372036854775808); if (min == int64.MinValue) { return 42; } return 0; }")]
    [TestCase("int32 Main() { uint64 wide = uint64(18446744073709551615); if (wide == uint64.MaxValue) { return 42; } return 0; }")]
    [TestCase("struct Box { public int32 Value; public Box(int32 value) { Value = value; } } int32 Main() { Box box = new Box(42); return box.Value; }")]
    public void BindsTypeCallAndContextualLiterals(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }
    [TestCase("int32 Main() { return (int32)42; }")]
    [TestCase("int32 Main() { return 42i32; }")]
    [TestCase("int32 Main() { return int32(42, 1); }")]
    [TestCase("int32 Main() { return int32(value: 42); }")]
    [TestCase("int32 Main() { return int32(out var value); }")]
    [TestCase("int32 Main() { mut int32 value = 42; return int32(ref value); }")]
    [TestCase("int32 Main() { return int32(); }")]
    [TestCase("int32 Main() { return int32(true); }")]
    [TestCase("int32 Main() { var value = 42; return 0; }")]
    [TestCase("int32 Main() { int64 value = int32(50000) * 50000; return 0; }")]
    [TestCase("struct Box { public int32 Value; public Box(int32 value) { Value = value; } } int32 Main() { Box box = Box(42); return 0; }")]
    [TestCase("int32 Main() { return new int32(42); }")]
    [TestCase("int32 Main() { int32 int64 = 1; return int64(42); }")]
    public void RejectsLegacyAndMalformedTypeCalls(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
    }
    [TestCase("int32 Main() { return 1i32; }","TRM0006")]
    [TestCase("int32 Main() { return 1u64; }","TRM0006")]
    [TestCase("int32 Main() { return 1.0f32; }","TRM0006")]
    [TestCase("int32 Main() { return 0x; }","TRM0007")]
    [TestCase("int32 Main() { return 0b; }","TRM0008")]
    [TestCase("int32 Main() { return 1_; }","TRM0009")]
    [TestCase("int32 Main() { return 0x1__; }","TRM0009")]
    [TestCase("int32 Main() { return 0b1_; }","TRM0009")]
    public void RejectsAttachedTagsAndMalformedSeparators(string source, string diagnostic)
    {
        var tree = SyntaxTree.Parse(source);
        Assert.That(global::System.Linq.Enumerable.Any(tree.Diagnostics,error => error.Code == diagnostic),Is.True);
    }

    [Test]
    public void OrdinaryNamedFunctionTakesPrecedenceOverConversionTarget()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "int32 int32(int32 value) { return value + 1; } int32 Main() { return int32(41); }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var main = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))().Functions
            .Single(function => function.Name == "Main");
        var result = ((global::System.Func<BoundReturn>)(() => { var __nunitActual1 = global::System.Linq.Enumerable.Single(main.Body.Statements); Assert.That(__nunitActual1, Is.TypeOf<BoundReturn>()); return (BoundReturn)__nunitActual1!; }))();
        _ = ((global::System.Func<BoundCall>)(() => { var __nunitActual2 = result.Value; Assert.That(__nunitActual2, Is.TypeOf<BoundCall>()); return (BoundCall)__nunitActual2!; }))();
    }
}
