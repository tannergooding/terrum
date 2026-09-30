// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StructuredLoopTests
{
    [Theory]
    [InlineData("do { continue; } while (false);", SyntaxKind.DoStatement)]
    [InlineData("outer: while (true) { break outer; }", SyntaxKind.LabeledLoopStatement)]
    [InlineData("outer: for (;;) { do { continue outer; } while (false); }", SyntaxKind.LabeledLoopStatement)]
    public void SyntaxRetainsLoopLabelsAndPostTestedSemicolon(string statement, SyntaxKind kind)
    {
        var text = "void Main() { /*before*/ " + statement + " /*after*/ }";
        var tree = SyntaxTree.Parse(text);
        Assert.False(tree.HasErrors, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.Equal(text, tree.Root.ToFullString());
        var node = Assert.Single(tree.Root.DescendantNodes(), child => child.Kind == kind);
        Assert.Equal(statement, text.Substring(node.Span.Start, node.Span.Length));
    }

    [Theory]
    [InlineData("do { } while (false)", "TRM1001")]
    [InlineData("do { } (false);", "TRM1001")]
    [InlineData("outer: do { break absent; } while (false);", "TRM3008")]
    [InlineData("outer: while (true) { break outer; } break outer;", "TRM3008")]
    [InlineData("outer: while (true) { try { } finally { break outer; } }", "TRM3019")]
    [InlineData("outer: for (;;) { try { } finally { continue outer; } }", "TRM3019")]
    public void MalformedOrEscapingTransfersAreRejected(string statement, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("void Main() { " + statement + " }"));
        Assert.Contains(compilation.Diagnostics, item => item.Code == diagnostic);
    }

    [Theory]
    [InlineData("do { value = 42; } while (value == 0); return value;", false)]
    [InlineData("while (false) { value = 42; } return value;", true)]
    [InlineData("do { if (flag) { continue; } value = 42; } while (value == 0); return value;", true)]
    [InlineData("do { if (flag) { break; } value = 42; } while (false); return value;", true)]
    [InlineData("do { value = 42; continue; } while (value == 0); return value;", false)]
    public void FirstIterationAndTransferPathsPreserveDefiniteAssignment(string body, bool errors)
    {
        var source = "[NoThrow] bool Clear(out int32 output) { return false; }"
            + "int32 Take(bool flag) { mut int32 value = 0; Clear(out value); " + body + " }"
            + "int32 Main() { return Take(false); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.Equal(errors, compilation.HasErrors);
    }

    [Theory]
    [InlineData("outer: while (true) { while (true) { break outer; } }", 2)]
    [InlineData("outer: for (;;) { do { continue outer; } while (false); }", 2)]
    [InlineData("outer: do { inner: do { continue outer; } while (false); } while (false);", 2)]
    public void NamedTransfersResolveLexicalTarget(string body, int depth)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " return 42; }"));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var transfers = program.Functions[program.EntryPoint].Body.DescendantTransfers();
        Assert.Contains(transfers, transfer => transfer == depth);
    }

    [Fact]
    public void EnclosingLoopLabelsCannotBeShadowed()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                outer: do {
                    outer: do { break outer; } while (true);
                    break outer;
                } while (true);
                return 42;
            }
            """));
        Assert.Contains(compilation.Diagnostics, item => item.Code == "TRM3004");
    }

    [Fact]
    public void DisjointLoopsMayReuseLabels()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                outer: do { break outer; } while (true);
                outer: do { break outer; } while (true);
                return 42;
            }
            """));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.Equal(new[] { 1, 1 }, program.Functions[program.EntryPoint].Body.DescendantTransfers());
    }

    [Fact]
    public void ForgedNonEnclosingTargetFailsVerificationAndEmission()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { while (true) { break; } return 42; }"));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var function = program.Functions[program.EntryPoint];
        var loop = Assert.IsType<BoundWhile>(function.Body.Statements[0]);
        var invalid = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, loop with {
                        Body = new BoundBlock([new BoundBreak(default) { TargetDepth = 2 }], default),
                    }),
                },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid));
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast));
    }
}

internal static class LoopTestTraversal
{
    internal static int[] DescendantTransfers(this BoundStatement statement) =>
        statement switch {
            BoundBlock block => block.Statements.SelectMany(DescendantTransfers).ToArray(),
            BoundWhile loop => DescendantTransfers(loop.Body),
            BoundIf conditional => DescendantTransfers(conditional.Then).Concat(
                conditional.Else is null ? [] : DescendantTransfers(conditional.Else)).ToArray(),
            BoundTryFinally region => DescendantTransfers(region.Body),
            BoundBreak transfer => [transfer.TargetDepth],
            BoundContinue transfer => [transfer.TargetDepth],
            _ => [],
        };
}
