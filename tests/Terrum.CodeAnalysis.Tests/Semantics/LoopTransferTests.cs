// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using NUnit.Framework;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Infrastructure;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class LoopTransferTests
{
    [TestCase("break;","TRM3008")]
    [TestCase("continue;","TRM3008")]
    [TestCase("while (true) { try { } finally { break; } }","TRM3019")]
    [TestCase("for (;;) { try { } finally { continue; } }","TRM3019")]
    [TestCase("mut int32 value = 0; while (true) { Clear(out value); break; } return value;","TRM3016")]
    [TestCase("mut int32 value = 0; for (; value < 2; Clear(out value)) { continue; } return value;","TRM3016")]
    [TestCase("mut int32 value = 0; while (true) { try { break; } finally { Clear(out value); } } return value;","TRM3016")]
    [TestCase("mut int32 value = 0; for (; value < 2; value = value + 1) { try { continue; } finally { Clear(out value); } } return value;","TRM3016")]
    [TestCase("while (true) { break; }","TRM3012")]
    public void InvalidTransferOrOutputStateIsRejected(string body, string diagnostic)
    {
        var source = "[NoThrow] bool Clear(out int32 x) { return false; } int32 Main() { " + body + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }
    [TestCase("int32 Main() { while (true) { continue; } }")]
    [TestCase("int32 Main() { for (;;) { continue; } }")]
    [TestCase("[DoesNotReturn] void Never() { for (;;) { continue; } } int32 Main() { Never(); }")]
    public void InfiniteContinuesHaveNoNormalContinuation(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Test]
    public void BreakInvalidatesDeclaredDoesNotReturnContract()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "[DoesNotReturn] void Never(bool choose) { while (true) { if (choose) { break; } } } int32 Main() { return 42; }"));
        Assert.That(compilation.HasErrors, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3018"),Is.True);
    }
    [TestCase("break;")]
    [TestCase("continue;")]
    public void TransfersRetainLosslessTokensAndSourceSpans(string transfer)
    {
        var text = "void Main() { for (; true;) { /*before*/ " + transfer + " /*after*/ } }";
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
        var kind = transfer == "break;" ? SyntaxKind.BreakStatement : SyntaxKind.ContinueStatement;
        var node = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), item => item.Kind == kind);
        Assert.That(text.Substring(node.Span.Start, node.Span.Length), Is.EqualTo(transfer));
    }

    [Test]
    public void OnlyForContinueIntroducesAnIteratorBearingLoop()
    {
        var source = """
            int32 Main() {
                for (mut int32 i = 0; i < 1; i = i + 1) { break; }
                for (mut int32 j = 0; j < 1; j = j + 1) {
                    while (true) { continue; }
                    continue;
                }
                return 42;
            }
            """;
        var result = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = result.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var first = ((global::System.Func<BoundBlock>)(() => { var __nunitActual1 = program.Functions[program.EntryPoint].Body.Statements[0]; Assert.That(__nunitActual1, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual1!; }))();
        var oldLoop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual2 = first.Statements[1]; Assert.That(__nunitActual2, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual2!; }))();
        Assert.That(oldLoop.Iterator, Is.Null);
        _ = ((global::System.Func<BoundBreak>)(() => { var __nunitActual5 = ((global::System.Func<BoundBlock>)(() => { var __nunitActual4 = ((global::System.Func<BoundBlock>)(() => { var __nunitActual3 = oldLoop.Body; Assert.That(__nunitActual3, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual3!; }))().Statements[0]; Assert.That(__nunitActual4, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual4!; }))().Statements[0]; Assert.That(__nunitActual5, Is.TypeOf<BoundBreak>()); return (BoundBreak)__nunitActual5!; }))();
        var second = ((global::System.Func<BoundBlock>)(() => { var __nunitActual6 = program.Functions[program.EntryPoint].Body.Statements[1]; Assert.That(__nunitActual6, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual6!; }))();
        var newLoop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual7 = second.Statements[1]; Assert.That(__nunitActual7, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual7!; }))();
        TestAssertions.NotNull(newLoop.Iterator);
        _ = ((global::System.Func<BoundContinue>)(() => { var __nunitActual9 = ((global::System.Func<BoundBlock>)(() => { var __nunitActual8 = newLoop.Body; Assert.That(__nunitActual8, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual8!; }))().Statements[1]; Assert.That(__nunitActual9, Is.TypeOf<BoundContinue>()); return (BoundContinue)__nunitActual9!; }))();
    }
    [TestCase("outside")]
    [TestCase("escaping-finally")]
    [TestCase("invalid-iterator")]
    public void ForgedLoopTransfersCannotReachNativeEmission(string kind)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { while (true) { break; } return 42; }"));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual10 = compilation.Program; Assert.That(__nunitActual10, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual10!; }))();
        var function = program.Functions[program.EntryPoint];
        var loop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual11 = function.Body.Statements[0]; Assert.That(__nunitActual11, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual11!; }))();
        BoundStatement forged = kind switch {
            "outside" => new BoundContinue(default),
            "escaping-finally" => loop with {
                Body = new BoundTryFinally(new BoundBlock([], default),
                    new BoundBlock([new BoundBreak(default)], default), default),
            },
            "invalid-iterator" => loop with { Iterator = new BoundBreak(default) },
            _ => throw new InvalidOperationException("Unknown invalid loop."),
        };
        var invalid = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, function with {
                Body = function.Body with { Statements = [forged, function.Body.Statements[1]] },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast))!;
    }

    [Test]
    public void ForgedForIteratorCannotUseBodyLocalAfterItsScopeRetires()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                for (mut int32 i = 0; i < 1; i = i + 1) {
                    mut int32 hidden = 42;
                    continue;
                }
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = compilation.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
        var function = program.Functions[program.EntryPoint];
        var forScope = ((global::System.Func<BoundBlock>)(() => { var __nunitActual13 = function.Body.Statements[0]; Assert.That(__nunitActual13, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual13!; }))();
        var loop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual14 = forScope.Statements[1]; Assert.That(__nunitActual14, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual14!; }))();
        var local = ((global::System.Func<BoundLocal>)(() => { var __nunitActual16 = ((global::System.Func<BoundBlock>)(() => { var __nunitActual15 = loop.Body; Assert.That(__nunitActual15, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual15!; }))().Statements[0]; Assert.That(__nunitActual16, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual16!; }))();
        var forgedLoop = loop with {
            Iterator = new BoundAssignment(local.Variable, new BoundConstant(1, PrimitiveType.Int32, default), default),
        };
        var invalid = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0,
                        forScope with { Statements = forScope.Statements.SetItem(1, forgedLoop) }),
                },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast))!;
    }
}
