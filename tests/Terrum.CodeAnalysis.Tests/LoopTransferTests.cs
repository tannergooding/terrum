// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class LoopTransferTests
{
    [Theory]
    [InlineData("break;", "TRM3008")]
    [InlineData("continue;", "TRM3008")]
    [InlineData("while (true) { try { } finally { break; } }", "TRM3019")]
    [InlineData("for (;;) { try { } finally { continue; } }", "TRM3019")]
    [InlineData("mut int32 value = 0; while (true) { Clear(out value); break; } return value;", "TRM3016")]
    [InlineData("mut int32 value = 0; for (; value < 2; Clear(out value)) { continue; } return value;", "TRM3016")]
    [InlineData("mut int32 value = 0; while (true) { try { break; } finally { Clear(out value); } } return value;", "TRM3016")]
    [InlineData("mut int32 value = 0; for (; value < 2; value = value + 1) { try { continue; } finally { Clear(out value); } } return value;", "TRM3016")]
    [InlineData("while (true) { break; }", "TRM3012")]
    public void InvalidTransferOrOutputStateIsRejected(string body, string diagnostic)
    {
        var source = "[NoThrow] bool Clear(out int32 x) { return false; } int32 Main() { " + body + " }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Theory]
    [InlineData("int32 Main() { while (true) { continue; } }")]
    [InlineData("int32 Main() { for (;;) { continue; } }")]
    [InlineData("[DoesNotReturn] void Never() { for (;;) { continue; } } int32 Main() { Never(); }")]
    public void InfiniteContinuesHaveNoNormalContinuation(string source)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
    }

    [Fact]
    public void BreakInvalidatesDeclaredDoesNotReturnContract()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse(
            "[DoesNotReturn] void Never(bool choose) { while (true) { if (choose) { break; } } } int32 Main() { return 42; }"));
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3018");
    }

    [Theory]
    [InlineData("break;")]
    [InlineData("continue;")]
    public void TransfersRetainLosslessTokensAndSourceSpans(string transfer)
    {
        var text = "void Main() { for (; true;) { /*before*/ " + transfer + " /*after*/ } }";
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
        var kind = transfer == "break;" ? SyntaxKind.BreakStatement : SyntaxKind.ContinueStatement;
        var node = Assert.Single(tree.Root.DescendantNodes(), item => item.Kind == kind);
        Assert.Equal(transfer, text.Substring(node.Span.Start, node.Span.Length));
    }

    [Fact]
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
        Assert.False(result.HasErrors, string.Join(Environment.NewLine, result.Diagnostics));
        var program = Assert.IsType<BoundProgram>(result.Program);
        var first = Assert.IsType<BoundBlock>(program.Functions[program.EntryPoint].Body.Statements[0]);
        var oldLoop = Assert.IsType<BoundWhile>(first.Statements[1]);
        Assert.Null(oldLoop.Iterator);
        _ = Assert.IsType<BoundBreak>(Assert.IsType<BoundBlock>(
            Assert.IsType<BoundBlock>(oldLoop.Body).Statements[0]).Statements[0]);
        var second = Assert.IsType<BoundBlock>(program.Functions[program.EntryPoint].Body.Statements[1]);
        var newLoop = Assert.IsType<BoundWhile>(second.Statements[1]);
        Assert.NotNull(newLoop.Iterator);
        _ = Assert.IsType<BoundContinue>(Assert.IsType<BoundBlock>(newLoop.Body).Statements[1]);
    }

    [Theory]
    [InlineData("outside")]
    [InlineData("escaping-finally")]
    [InlineData("invalid-iterator")]
    public void ForgedLoopTransfersCannotReachNativeEmission(string kind)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { while (true) { break; } return 42; }"));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = program.Functions[program.EntryPoint];
        var loop = Assert.IsType<BoundWhile>(function.Body.Statements[0]);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast));
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = program.Functions[program.EntryPoint];
        var forScope = Assert.IsType<BoundBlock>(function.Body.Statements[0]);
        var loop = Assert.IsType<BoundWhile>(forScope.Statements[1]);
        var local = Assert.IsType<BoundLocal>(Assert.IsType<BoundBlock>(loop.Body).Statements[0]);
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
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast));
    }
}
