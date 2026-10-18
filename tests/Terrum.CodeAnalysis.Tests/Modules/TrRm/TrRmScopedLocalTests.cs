// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Text;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Modules.TrRm;

public sealed class TrRmScopedLocalTests
{
    private static BoundProgram Compile(string source,
        BoundRuntimePolicy policy = BoundRuntimePolicy.FailFast)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        return ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
    }

    private static void RoundTrip(string source, int expected = 42)
    {
        var original = Compile(source);
        var artifact = TrRmExecutableCompiler.Emit(original, "scoped-locals");
        TrRmStructuralMetadataValidator.ValidateCompact(artifact);
        var index = new TrRmArtifactIndex(artifact);
        Assert.That(index.TryGetSection(0x20, out var bytes, out var count), Is.True);
        Assert.That(new TrRmBodyTableReader(bytes, count).GetRow(1)[0] & 0x0C, Is.EqualTo(0x08));
        var loaded = TrRmExecutableCompiler.Load(artifact);
        Assert.That(NativeJit.Execute(original, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(expected));
        Assert.That(NativeEmitter.Emit(loaded, NativeRuntimePolicy.FailFast), Is.Not.Empty);
    }
    [TestCase("int32 Main() { { int32 x = 1; } { int32 x = 42; return x; } }")]
    [TestCase("int32 Main() { { int32 x = 0; } int32 x = 42; return x; }")]
    [TestCase("int32 Main() { mut int32 result = 0; if (true) { int32 x = 42; result = x; } else { int32 x = 0; result = x; } return result; }")]
    [TestCase("int32 Main() { mut int32 result = 0; if (false) { int32 x = 0; result = x; } else if (true) { int32 x = 42; result = x; } return result; }")]
    [TestCase("int32 Main() { mut int32 x = 40; { int32 y = 2; x += y; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 3) { mut int32 local = 1; local++; x += local; } return x == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { int32 x = 0; if (true) { int32 y = false && (x == 1 / 0) ? 0 : 42; return y; } return x; }")]
    [TestCase("int32 Main() { mut int32 x = 0; if (true) { int32 y = true ? 40 : 1 / 0; x = y + 2; } return x; }")]
    [TestCase("int32 Main() { if (true) { int32 inner = 42; return inner; } return 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; while (x < 4) { int32 local = x++; { int32 inner = local; if (inner == 1) { continue; } if (inner == 3) { break; } } } return x == 4 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 0; mut int32 total = 0; while (x < 3) { int32 outer = x++; mut int32 iteration = 0; while (iteration < 2) { int32 inner = outer + 1; total += inner; if (iteration == 0) { break; } iteration++; } if (outer == 1) { continue; } total++; } return total == 8 ? 42 : 0; }")]
    [TestCase("int32 Main() { mut int32 x = 1; { int32 old = x++; int32 result = Take(old, x++, x); return result == 123 && x == 3 ? 42 : 0; } } int32 Take(int32 a, int32 b, int32 c) { return a * 100 + b * 10 + c; }")]
    [TestCase("int32 Main() { mut int32 x = 1; while (x < 2) { int32 old = x++; Touch(old); } return x == 2 ? 42 : 0; } void Touch(int32 value) { }")]
    [TestCase("int32 Main() { mut int32 result = 0; bool take = result == 0; { mut int32 local = 1; result = Take(local, take ? local++ : 1 / 0, local); } return result == 112 ? 42 : 0; } int32 Take(int32 a, int32 b, int32 c) { return a * 100 + b * 10 + c; }")]
    [TestCase("int32 Main() { if (false) { int32 local = 42; Fail(local); } return 42; } void Fail(int32 value) { int32 fault = value / 0; }")]
    [TestCase("int32 Main() { mut int32 total = 0; { mut int32 outer = 0; while (outer++ < 3) { mut int32 inner = 0; while (inner < 3) { int32 saved = inner++; if (saved == 1) { continue; } total++; } } } return total == 6 ? 42 : 0; }")]
    [TestCase("[NoThrow] int32 Main() { if (true) { bool local = true; return local ? 42 : 0; } return 0; }")]
    public void LexicalMembershipAndValuesSurviveSourceFreeExecution(string source)
        => RoundTrip(source);
    [TestCase("bool","true")]
    [TestCase("char16","char16(65535)")]
    [TestCase("int32","int32.MinValue")]
    [TestCase("uint32","uint32.MaxValue")]
    [TestCase("int64","int64.MinValue")]
    [TestCase("uint64","uint64.MaxValue")]
    public void AllSixScalarLocalTypesRetainTheirExactValues(string type, string value)
        => RoundTrip($"int32 Main() {{ return Read() == {value} ? 42 : 0; }} "
            + $"{type} Read() {{ if (true) {{ {type} local = {value}; return local; }} return {value}; }}");
    [TestCase("int32 Main() { return F(0); } int32 F(int32 x) { if (true) { int32 x = 42; return x; } return 0; }")]
    [TestCase("int32 Main() { int32 x = 1; { int32 x = 42; return x; } }")]
    public void LiveNamesCannotBeShadowed(string source)
        => Assert.That(Compilation.Create(SyntaxTree.Parse(source)).HasErrors, Is.True);
    [TestCase(83,true)]
    [TestCase(84,false)]
    public void ScopedValuesCountOnlyMaterializedSsaDefinitions(int blocks, bool supported)
    {
        var source = new StringBuilder("int32 Main() { mut int32 x = 0; while (x < 1) { ");
        for (var index = 0; index < blocks; index++)
        {
            source.Append($"{{ int32 local = {index}; x = x + 1; }} ");
        }
        source.Append("} return ");
        // Five fixed definitions plus three per scope; two unary values fill the positive case to 256.
        source.Append(supported ? "+(+x); }" : "x; }");
        var original = Compile(source.ToString());
        if (!supported)
        {
            Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(original, "over-limit"));
            return;
        }
        var loaded = TrRmExecutableCompiler.Load(TrRmExecutableCompiler.Emit(original, "at-limit"));
        Assert.That(loaded.Functions[0].Variables.Length, Is.EqualTo(256));
        Assert.That(NativeJit.Execute(loaded, NativeRuntimePolicy.FailFast), Is.EqualTo(blocks));
    }

    [Test]
    public void UnsupportedBodyInUnusedFunctionIsStillRejected()
    {
        var source = "int32 Main() { if (true) { int32 x = 42; return x; } return 0; } int32 Unused() { mut int32 x = 0; outer: while (x < 2) { do { break outer; } while (x < 1); } return x; }";
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(Compile(source), "unused-body"));
    }

    [Test]
    public void CheckedExceptionPolicyIsNotPersistedForScopedLocals()
    {
        var source = "int32 Main() { if (true) { int32 x = 42; return x; } return 0; }";
        Assert.Throws<NotSupportedException>(() => TrRmExecutableCompiler.Emit(
            Compile(source, BoundRuntimePolicy.CheckedExceptions), "checked-scope"));
    }
}
