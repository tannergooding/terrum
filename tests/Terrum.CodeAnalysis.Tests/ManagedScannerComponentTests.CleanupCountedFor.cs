// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ManagedScannerComponentTests
{
    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast, false)]
    [InlineData(NativeRuntimePolicy.FailFast, true)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, false)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions, true)]
    public async Task ScalarFrontendCleanupCountedForProvesBothLoweringShapesAsync(NativeRuntimePolicy policy, bool localContinue)
    {
        var text = "public int32 Main() { mut int32 value = 0; try {} finally { switch (1) { 1 => { "
            + "for (mut int32 i = 0; i < 4; i = i + 1) { "
            + (localContinue ? "continue; " : "value = value | i; ")
            + "} continue _; }, _ => {} } } return value; }"
            + " [NoThrow] int32 Overwrite(out int32 value) { { value = 0; } return 0; }";
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
        var program = Assert.IsType<BoundProgram>(read.Program);
        var function = program.Functions[0];
        var cleanup = Assert.IsType<BoundTryFinally>(function.Body.Statements[1]);
        var selection = Assert.IsType<BoundBlock>(Assert.Single(cleanup.Finally.Statements));
        var dispatcher = Assert.IsType<BoundWhile>(selection.Statements[2]);
        var arms = Assert.IsType<BoundIf>(dispatcher.Body);
        var first = Assert.IsType<BoundBlock>(arms.Then);
        var counted = Assert.IsType<BoundBlock>(first.Statements[0]);
        Assert.True(counted.IsManagedLexicalScope);
        var initial = Assert.IsType<BoundLocal>(counted.Statements[0]);
        var loop = Assert.IsType<BoundWhile>(counted.Statements[1]);
        Assert.Equal(counted.Span, loop.Span);
        Assert.False(loop.IsPostTested);
        BoundBlock body;
        BoundAssignment iterator;
        if (localContinue)
        {
            body = Assert.IsType<BoundBlock>(loop.Body);
            iterator = Assert.IsType<BoundAssignment>(loop.Iterator);
            Assert.IsType<BoundContinue>(Assert.Single(body.Statements));
        }
        else
        {
            Assert.Null(loop.Iterator);
            var appended = Assert.IsType<BoundBlock>(loop.Body);
            body = Assert.IsType<BoundBlock>(appended.Statements[0]);
            iterator = Assert.IsType<BoundAssignment>(appended.Statements[1]);
        }
        var condition = Assert.IsType<BoundBinary>(loop.Condition);
        var step = Assert.IsType<BoundBinary>(iterator.Value);
        Assert.Equal(initial.Variable, iterator.Variable);
        Assert.Equal(initial.Variable, Assert.IsType<BoundRead>(condition.Left).Variable);
        Assert.Equal(initial.Variable, Assert.IsType<BoundRead>(step.Left).Variable);
        Assert.Equal(TokenKind.Less, condition.Operator);
        Assert.Equal(TokenKind.Plus, step.Operator);
        Assert.Equal(1, Assert.IsType<BoundConstant>(step.Right).Value);
        Assert.Equal(localContinue ? 0 : 3, NativeJit.Execute(program with { EntryPoint = 0 }, policy));
        var returning = program with {
            EntryPoint = 0,
            Functions = program.Functions.SetItem(0, function with {
                Body = function.Body with { Statements = function.Body.Statements.SetItem(1, cleanup with {
                    Body = new BoundBlock([new BoundReturn(new BoundRead(0, PrimitiveType.Int32, cleanup.Span),
                        cleanup.Span)], cleanup.Body.Span),
                }) },
            }),
        };
        SemanticVerifier.Verify(returning);
        Assert.Equal(0, NativeJit.Execute(returning, policy));
        if (policy == NativeRuntimePolicy.CheckedExceptions)
        {
            var failure = new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, cleanup.Span), cleanup.Span);
            var handler = new BoundTryCatch(new BoundBlock([cleanup with {
                Body = new BoundBlock([failure], cleanup.Body.Span),
            }], cleanup.Span), new BoundBlock([], cleanup.Span), null, cleanup.Span);
            var exceptional = program with {
                EntryPoint = 0,
                Functions = program.Functions.SetItem(0, function with {
                    Body = function.Body with { Statements = function.Body.Statements.SetItem(1,
                        new BoundUnsafe(new BoundBlock([handler], cleanup.Span), cleanup.Span)) },
                }),
            };
            SemanticVerifier.Verify(exceptional);
            Assert.Equal(localContinue ? 0 : 3, NativeJit.Execute(exceptional, policy));
        }

        BoundWhile WithBody(BoundBlock replacement) => localContinue ? loop with { Body = replacement }
            : loop with { Body = new BoundBlock([replacement, iterator], loop.Body.Span) };
        var counterRead = new BoundRead(initial.Variable, PrimitiveType.Int32, loop.Span);
        var zero = new BoundConstant(0, PrimitiveType.Int32, loop.Span);
        var mutations = new (string Name, BoundLocal Initial, BoundWhile Loop, ImmutableArray<BoundVariable> Variables)[] {
            ("unknown-initial", initial with { Initializer = new BoundRead(0, PrimitiveType.Int32, loop.Span) }, loop, function.Variables),
            ("noncanonical-initial", initial with { Initializer = new BoundConstant((long)int.MaxValue + 1, PrimitiveType.Int32, loop.Span) },
                loop, function.Variables),
            ("noncanonical-bound", initial, loop with { Condition = condition with {
                Right = new BoundConstant((long)int.MinValue - 1, PrimitiveType.Int32, loop.Span),
            } }, function.Variables),
            ("post-tested", initial, loop with { IsPostTested = true }, function.Variables),
            ("immutable", initial, loop, function.Variables.SetItem(initial.Variable,
                function.Variables[initial.Variable] with { IsMutable = false })),
            ("body-write", initial, WithBody(body with { Statements = body.Statements.Insert(0,
                new BoundAssignment(initial.Variable, zero, loop.Span)) }), function.Variables),
            ("body-shared-step", initial, WithBody(body with { Statements = body.Statements.Insert(0,
                new BoundAssignment(0, step, loop.Span)) }), function.Variables),
            ("cleanup-write", initial, WithBody(body with { Statements = body.Statements.Insert(0,
                new BoundTryFinally(new BoundBlock([], loop.Span),
                    new BoundBlock([new BoundAssignment(initial.Variable, zero, loop.Span)], loop.Span), loop.Span)) }),
                function.Variables),
            ("body-update", initial, WithBody(body with { Statements = body.Statements.Insert(0,
                new BoundExpressionStatement(new BoundUpdate(TokenKind.PlusPlus, counterRead, false,
                    PrimitiveType.Int32, loop.Span), loop.Span)) }), function.Variables),
            ("body-alias", initial, WithBody(body with { Statements = body.Statements.Insert(0,
                new BoundExpressionStatement(new BoundCall(1,
                    [new BoundOutArgument(initial.Variable, false, PrimitiveType.Int32, loop.Span)],
                    PrimitiveType.Int32, loop.Span), loop.Span)) }), function.Variables),
            ("body-uninitialized", initial, WithBody(body with { Statements = body.Statements
                .Insert(0, new BoundVacantLocal(function.Variables.Length, loop.Span))
                .Insert(1, new BoundAssignment(0, new BoundRead(function.Variables.Length, PrimitiveType.Int32, loop.Span), loop.Span)) }),
                function.Variables.Add(new BoundVariable("vacant", PrimitiveType.Int32, false, loop.Span) {
                    IsInitializationLocal = true,
                })),
            ("escaping-break", initial, WithBody(body with { Statements = [new BoundBreak(loop.Span) { TargetDepth = 3 }] }),
                function.Variables),
            ("raw-dispatch-continue", initial, WithBody(body with { Statements = [new BoundContinue(loop.Span) { TargetDepth = 2 }] }),
                function.Variables),
            ("unproved-body-loop", initial, WithBody(body with { Statements = [
                new BoundWhile(new BoundBinary(TokenKind.BangEquals, new BoundRead(0, PrimitiveType.Int32, loop.Span),
                    zero, PrimitiveType.Bool, loop.Span), new BoundBlock([], loop.Span), loop.Span),
            ] }), function.Variables),
        };
        foreach (var mutation in mutations)
        {
            var candidate = ReplaceCountedLoop(program, mutation.Initial, mutation.Loop, mutation.Variables);
            var error = Record.Exception(() => SemanticVerifier.VerifyLibrary(candidate));
            Assert.True(error is ArgumentException, $"{mutation.Name}: {error}");
            Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(candidate, ["Main"], policy));
        }
        if (!localContinue)
        {
            var bypass = WithBody(body with { Statements = [new BoundContinue(loop.Span)] });
            Assert.Throws<ArgumentException>(() => SemanticVerifier.VerifyLibrary(
                ReplaceCountedLoop(program, initial, bypass, function.Variables)));
        }
        var composed = WithBody(new BoundBlock([
            new BoundTryFinally(body, new BoundBlock([new BoundAssignment(0,
                new BoundConstant(8, PrimitiveType.Int32, loop.Span), loop.Span)], loop.Span), loop.Span),
        ], body.Span) { IsManagedLexicalScope = true });
        var withCleanup = ReplaceCountedLoop(program, initial, composed, function.Variables) with { EntryPoint = 0 };
        SemanticVerifier.Verify(withCleanup);
        Assert.Equal(8, NativeJit.Execute(withCleanup, policy));
        var innerLoop = new BoundWhile(new BoundConstant(1, PrimitiveType.Bool, loop.Span),
            new BoundBlock([new BoundAssignment(0, new BoundBinary(TokenKind.Pipe,
                new BoundRead(0, PrimitiveType.Int32, loop.Span), counterRead, PrimitiveType.Int32, loop.Span), loop.Span),
                new BoundContinue(loop.Span) { TargetDepth = 2 }], loop.Span), loop.Span);
        var outerContinue = ReplaceCountedLoop(program, initial,
            WithBody(new BoundBlock([innerLoop], body.Span) { IsManagedLexicalScope = true }), function.Variables) with {
                EntryPoint = 0,
            };
        if (localContinue)
        {
            SemanticVerifier.Verify(outerContinue);
            Assert.Equal(3, NativeJit.Execute(outerContinue, policy));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(outerContinue));
        }
        // The full signed int32 distance exceeds int32.MaxValue. Verification
        // must be structural; do not execute or unroll these multi-billion-step loops.
        foreach (var ascending in new[] { true, false })
        {
            var extremeInitial = initial with {
                Initializer = new BoundConstant(ascending ? int.MinValue : int.MaxValue, PrimitiveType.Int32, loop.Span),
            };
            var extremeStep = iterator with { Value = step with {
                Operator = ascending ? TokenKind.Plus : TokenKind.Minus,
            } };
            var extremeLoop = loop with {
                Condition = condition with {
                    Operator = ascending ? TokenKind.Less : TokenKind.Greater,
                    Right = new BoundConstant(ascending ? int.MaxValue : int.MinValue, PrimitiveType.Int32, loop.Span),
                },
                Iterator = localContinue ? extremeStep : null,
                Body = localContinue ? body : new BoundBlock([body, extremeStep], loop.Body.Span),
            };
            var finite = ReplaceCountedLoop(program, extremeInitial, extremeLoop, function.Variables);
            SemanticVerifier.VerifyLibrary(finite);
            Assert.NotEmpty(NativeEmitter.EmitLibrary(finite, ["Main"], policy));
        }

        BoundProgram ReplaceCountedLoop(BoundProgram original, BoundLocal replacementInitial,
            BoundWhile replacementLoop, ImmutableArray<BoundVariable> variables) => original with {
                Functions = original.Functions.SetItem(0, function with {
                    Variables = variables,
                    Body = function.Body with {
                        Statements = function.Body.Statements.SetItem(1, cleanup with {
                            Finally = cleanup.Finally with { Statements = [selection with {
                                Statements = selection.Statements.SetItem(2, dispatcher with {
                                    Body = arms with { Then = first with {
                                        Statements = first.Statements.SetItem(0, counted with {
                                            Statements = [replacementInitial, replacementLoop],
                                        }),
                                    } },
                                }),
                            }] },
                        }),
                    },
                }),
            };
    }
}
