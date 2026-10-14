// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ManagedScannerComponentTests
{
    [TestCase(NativeRuntimePolicy.FailFast,false)]
    [TestCase(NativeRuntimePolicy.FailFast,true)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,false)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions,true)]
    public async Task ScalarFrontendCleanupCountedForProvesBothLoweringShapesAsync(NativeRuntimePolicy policy, bool localContinue)
    {
        var text = "public int32 Main() { mut int32 value = 0; try {} finally { switch (1) { 1 => { "
            + "for (mut int32 i = 0; i < 4; i = i + 1) { "
            + (localContinue ? "continue; " : "value = value | i; ")
            + "} continue _; }, _ => {} } } return value; }"
            + " [NoThrow] int32 Overwrite(out int32 value) { { value = 0; } return 0; }";
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = read.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var function = program.Functions[0];
        var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual1 = function.Body.Statements[1]; Assert.That(__nunitActual1, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual1!; }))();
        var selection = ((global::System.Func<BoundBlock>)(() => { var __nunitActual2 = global::System.Linq.Enumerable.Single(cleanup.Finally.Statements); Assert.That(__nunitActual2, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual2!; }))();
        var dispatcher = ((global::System.Func<BoundWhile>)(() => { var __nunitActual3 = selection.Statements[2]; Assert.That(__nunitActual3, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual3!; }))();
        var arms = ((global::System.Func<BoundIf>)(() => { var __nunitActual4 = dispatcher.Body; Assert.That(__nunitActual4, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual4!; }))();
        var first = ((global::System.Func<BoundBlock>)(() => { var __nunitActual5 = arms.Then; Assert.That(__nunitActual5, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual5!; }))();
        var counted = ((global::System.Func<BoundBlock>)(() => { var __nunitActual6 = first.Statements[0]; Assert.That(__nunitActual6, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual6!; }))();
        Assert.That(counted.IsManagedLexicalScope, Is.True);
        var initial = ((global::System.Func<BoundLocal>)(() => { var __nunitActual7 = counted.Statements[0]; Assert.That(__nunitActual7, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual7!; }))();
        var loop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual8 = counted.Statements[1]; Assert.That(__nunitActual8, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual8!; }))();
        Assert.That(loop.Span, Is.EqualTo(counted.Span));
        Assert.That(loop.IsPostTested, Is.False);
        BoundBlock body;
        BoundAssignment iterator;
        if (localContinue)
        {
            body = ((global::System.Func<BoundBlock>)(() => { var __nunitActual9 = loop.Body; Assert.That(__nunitActual9, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual9!; }))();
            iterator = ((global::System.Func<BoundAssignment>)(() => { var __nunitActual10 = loop.Iterator; Assert.That(__nunitActual10, Is.TypeOf<BoundAssignment>()); return (BoundAssignment)__nunitActual10!; }))();
            Assert.That(global::System.Linq.Enumerable.Single(body.Statements), Is.TypeOf<BoundContinue>());
        }
        else
        {
            Assert.That(loop.Iterator, Is.Null);
            var appended = ((global::System.Func<BoundBlock>)(() => { var __nunitActual11 = loop.Body; Assert.That(__nunitActual11, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual11!; }))();
            body = ((global::System.Func<BoundBlock>)(() => { var __nunitActual12 = appended.Statements[0]; Assert.That(__nunitActual12, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual12!; }))();
            iterator = ((global::System.Func<BoundAssignment>)(() => { var __nunitActual13 = appended.Statements[1]; Assert.That(__nunitActual13, Is.TypeOf<BoundAssignment>()); return (BoundAssignment)__nunitActual13!; }))();
        }
        var condition = ((global::System.Func<BoundBinary>)(() => { var __nunitActual14 = loop.Condition; Assert.That(__nunitActual14, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual14!; }))();
        var step = ((global::System.Func<BoundBinary>)(() => { var __nunitActual15 = iterator.Value; Assert.That(__nunitActual15, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual15!; }))();
        Assert.That(iterator.Variable, Is.EqualTo(initial.Variable));
        Assert.That(((global::System.Func<BoundRead>)(() => { var __nunitActual16 = condition.Left; Assert.That(__nunitActual16, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual16!; }))().Variable, Is.EqualTo(initial.Variable));
        Assert.That(((global::System.Func<BoundRead>)(() => { var __nunitActual17 = step.Left; Assert.That(__nunitActual17, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual17!; }))().Variable, Is.EqualTo(initial.Variable));
        Assert.That(condition.Operator, Is.EqualTo(TokenKind.Less));
        Assert.That(step.Operator, Is.EqualTo(TokenKind.Plus));
        Assert.That(((global::System.Func<BoundConstant>)(() => { var __nunitActual18 = step.Right; Assert.That(__nunitActual18, Is.TypeOf<BoundConstant>()); return (BoundConstant)__nunitActual18!; }))().Value, Is.EqualTo(1));
        Assert.That(NativeJit.Execute(program with { EntryPoint = 0 }, policy), Is.EqualTo(localContinue ? 0 : 3));
        var returning = program with {
            EntryPoint = 0,
            Functions = program.Functions.SetItem(0, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(1, cleanup with {
                        Body = new BoundBlock([new BoundReturn(new BoundRead(0, PrimitiveType.Int32, cleanup.Span),
                        cleanup.Span)], cleanup.Body.Span),
                    })
                },
            }),
        };
        SemanticVerifier.Verify(returning);
        Assert.That(NativeJit.Execute(returning, policy), Is.EqualTo(0));
        if (policy == NativeRuntimePolicy.CheckedExceptions)
        {
            var failure = new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, cleanup.Span), cleanup.Span);
            var handler = new BoundTryCatch(new BoundBlock([cleanup with {
                Body = new BoundBlock([failure], cleanup.Body.Span),
            }], cleanup.Span), new BoundBlock([], cleanup.Span), null, cleanup.Span);
            var exceptional = program with {
                EntryPoint = 0,
                Functions = program.Functions.SetItem(0, function with {
                    Body = function.Body with {
                        Statements = function.Body.Statements.SetItem(1,
                        new BoundUnsafe(new BoundBlock([handler], cleanup.Span), cleanup.Span))
                    },
                }),
            };
            SemanticVerifier.Verify(exceptional);
            Assert.That(NativeJit.Execute(exceptional, policy), Is.EqualTo(localContinue ? 0 : 3));
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
            var error = TestExceptionCapture.Capture(() => SemanticVerifier.VerifyLibrary(candidate));
            Assert.That(error is ArgumentException, Is.True, $"{mutation.Name}: {error}");
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
        Assert.That(NativeJit.Execute(withCleanup, policy), Is.EqualTo(8));
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
            Assert.That(NativeJit.Execute(outerContinue, policy), Is.EqualTo(3));
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
            var extremeStep = iterator with {
                Value = step with {
                    Operator = ascending ? TokenKind.Plus : TokenKind.Minus,
                }
            };
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
            Assert.That(NativeEmitter.EmitLibrary(finite, ["Main"], policy), Is.Not.Empty);
        }

        BoundProgram ReplaceCountedLoop(BoundProgram original, BoundLocal replacementInitial,
            BoundWhile replacementLoop, ImmutableArray<BoundVariable> variables) => original with {
                Functions = original.Functions.SetItem(0, function with {
                    Variables = variables,
                    Body = function.Body with {
                        Statements = function.Body.Statements.SetItem(1, cleanup with {
                            Finally = cleanup.Finally with {
                                Statements = [selection with {
                                Statements = selection.Statements.SetItem(2, dispatcher with {
                                    Body = arms with { Then = first with {
                                        Statements = first.Statements.SetItem(0, counted with {
                                            Statements = [replacementInitial, replacementLoop],
                                        }),
                                    } },
                                }),
                            }]
                            },
                        }),
                    },
                }),
            };
    }
}
