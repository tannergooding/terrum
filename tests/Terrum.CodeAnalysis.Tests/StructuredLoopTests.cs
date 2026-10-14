// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class StructuredLoopTests
{
    [TestCase("do { continue; } while (false);",SyntaxKind.DoStatement)]
    [TestCase("outer: while (true) { break outer; }",SyntaxKind.LabeledLoopStatement)]
    [TestCase("outer: for (;;) { do { continue outer; } while (false); }",SyntaxKind.LabeledLoopStatement)]
    public void SyntaxRetainsLoopLabelsAndPostTestedSemicolon(string statement, SyntaxKind kind)
    {
        var text = "void Main() { /*before*/ " + statement + " /*after*/ }";
        var tree = SyntaxTree.Parse(text);
        Assert.That(tree.HasErrors, Is.False, string.Join(Environment.NewLine, tree.Diagnostics));
        Assert.That(tree.Root.ToFullString(), Is.EqualTo(text));
        var node = global::System.Linq.Enumerable.Single(tree.Root.DescendantNodes(), child => child.Kind == kind);
        Assert.That(text.Substring(node.Span.Start, node.Span.Length), Is.EqualTo(statement));
    }
    [TestCase("do { } while (false)","TRM1001")]
    [TestCase("do { } (false);","TRM1001")]
    [TestCase("outer: do { break absent; } while (false);","TRM3008")]
    [TestCase("outer: while (true) { break outer; } break outer;","TRM3008")]
    [TestCase("outer: while (true) { try { } finally { break outer; } }","TRM3019")]
    [TestCase("outer: for (;;) { try { } finally { continue outer; } }","TRM3019")]
    public void MalformedOrEscapingTransfersAreRejected(string statement, string diagnostic)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("void Main() { " + statement + " }"));
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == diagnostic),Is.True);
    }
    [TestCase("do { value = 42; } while (value == 0); return value;",false)]
    [TestCase("while (false) { value = 42; } return value;",true)]
    [TestCase("do { if (flag) { continue; } value = 42; } while (value == 0); return value;",true)]
    [TestCase("do { if (flag) { break; } value = 42; } while (false); return value;",true)]
    [TestCase("do { value = 42; continue; } while (value == 0); return value;",false)]
    public void FirstIterationAndTransferPathsPreserveDefiniteAssignment(string body, bool errors)
    {
        var source = "[NoThrow] bool Clear(out int32 output) { return false; }"
            + "int32 Take(bool flag) { mut int32 value = 0; Clear(out value); " + body + " }"
            + "int32 Main() { return Take(false); }";
        var compilation = Compilation.Create(SyntaxTree.Parse(source));
        Assert.That(compilation.HasErrors, Is.EqualTo(errors));
    }
    [TestCase("outer: while (true) { while (true) { break outer; } }",2)]
    [TestCase("outer: for (;;) { do { continue outer; } while (false); }",2)]
    [TestCase("outer: do { inner: do { continue outer; } while (false); } while (false);",2)]
    public void NamedTransfersResolveLexicalTarget(string body, int depth)
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { " + body + " return 42; }"));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var transfers = program.Functions[program.EntryPoint].Body.DescendantTransfers();
        Assert.That(global::System.Linq.Enumerable.Any(transfers,transfer => transfer == depth),Is.True);
    }

    [Test]
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
        Assert.That(global::System.Linq.Enumerable.Any(compilation.Diagnostics,item => item.Code == "TRM3004"),Is.True);
    }

    [Test]
    public void DisjointLoopsMayReuseLabels()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                outer: do { break outer; } while (true);
                outer: do { break outer; } while (true);
                return 42;
            }
            """));
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        Assert.That(program.Functions[program.EntryPoint].Body.DescendantTransfers(), Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void ForgedNonEnclosingTargetFailsVerificationAndEmission()
    {
        var compilation = Compilation.Create(SyntaxTree.Parse("int32 Main() { while (true) { break; } return 42; }"));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var function = program.Functions[program.EntryPoint];
        var loop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual3 = function.Body.Statements[0]; Assert.That(__nunitActual3, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual3!; }))();
        var invalid = program with {
            Functions = program.Functions.SetItem(program.EntryPoint, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(0, loop with {
                        Body = new BoundBlock([new BoundBreak(default) { TargetDepth = 2 }], default),
                    }),
                },
            }),
        };
        _ = Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(invalid))!;
        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(invalid, NativeRuntimePolicy.FailFast))!;
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
