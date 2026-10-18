// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using NUnit.Framework;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Tests.Infrastructure;
using Terrum.CodeAnalysis.Text;

namespace Terrum.CodeAnalysis.Tests.Lexing;

public sealed partial class ManagedScannerComponentTests
{
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendCleanupSwitchTransfersEvaluateSelectorOnceAsync(NativeRuntimePolicy policy)
    {
        const string text = """
            public int32 Main() {
                mut int32 value = 0;
                try {} finally {
                    switch (Pick(out value)) {
                        1 => { value = 40; continue _; },
                        _ => { value = value | 2; },
                    }
                }
                return value;
            }
            [NoThrow] int32 Pick(out int32 result) { { result = 1; } return 1; }
            """;
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = read.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        Assert.That(NativeJit.Execute(program with { EntryPoint = 0 }, policy), Is.EqualTo(42));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendCleanupSwitchTransferMarkerIsNotATerminationCertificateAsync(NativeRuntimePolicy policy)
    {
        const string text = """
            public int32 Main() {
                mut int32 value = 0;
                try {} finally {
                    switch (1) { 1 => { continue _; }, _ => { value = 42; } }
                }
                return value;
            }
            [NoThrow] int32 Overwrite(out int32 value) { { value = 0; } return 0; }
            """;
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = read.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
        var function = program.Functions[0];
        var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual2 = function.Body.Statements[1]; Assert.That(__nunitActual2, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual2!; }))();
        var selection = ((global::System.Func<BoundBlock>)(() => { var __nunitActual3 = global::System.Linq.Enumerable.Single(cleanup.Finally.Statements); Assert.That(__nunitActual3, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual3!; }))();
        var dispatcher = ((global::System.Func<BoundWhile>)(() => { var __nunitActual4 = selection.Statements[2]; Assert.That(__nunitActual4, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual4!; }))();
        var state = ((global::System.Func<BoundLocal>)(() => { var __nunitActual5 = selection.Statements[1]; Assert.That(__nunitActual5, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual5!; }))();
        Assert.That(dispatcher.SwitchArmVariable, Is.EqualTo(state.Variable));
        var arms = ((global::System.Func<BoundIf>)(() => { var __nunitActual6 = dispatcher.Body; Assert.That(__nunitActual6, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual6!; }))();
        var first = ((global::System.Func<BoundBlock>)(() => { var __nunitActual7 = arms.Then; Assert.That(__nunitActual7, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual7!; }))();
        var last = ((global::System.Func<BoundBlock>)(() => { var __nunitActual8 = arms.Else; Assert.That(__nunitActual8, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual8!; }))();
        var assignment = ((global::System.Func<BoundAssignment>)(() => { var __nunitActual9 = first.Statements[0]; Assert.That(__nunitActual9, Is.TypeOf<BoundAssignment>()); return (BoundAssignment)__nunitActual9!; }))();
        var continuation = ((global::System.Func<BoundContinue>)(() => { var __nunitActual10 = first.Statements[1]; Assert.That(__nunitActual10, Is.TypeOf<BoundContinue>()); return (BoundContinue)__nunitActual10!; }))();
        var condition = ((global::System.Func<BoundBinary>)(() => { var __nunitActual11 = arms.Condition; Assert.That(__nunitActual11, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual11!; }))();
        var value = ((global::System.Func<BoundLocal>)(() => { var __nunitActual12 = function.Body.Statements[0]; Assert.That(__nunitActual12, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual12!; }))().Variable;
        var span = dispatcher.Span;
        var zero = new BoundConstant(0, PrimitiveType.Int32, span);
        var one = new BoundConstant(1, PrimitiveType.Int32, span);
        var stateRead = new BoundRead(state.Variable, PrimitiveType.Int32, span);
        var readState = new BoundAssignment(value, stateRead, span);
        var writeState = assignment with { Value = zero };
        var nested = new BoundTryFinally(first, new BoundBlock([writeState], span), span);
        var mutations = new (string Name, BoundWhile Loop, ImmutableArray<BoundVariable> Variables)[] {
            ("unmarked", dispatcher with { SwitchArmVariable = -1 }, function.Variables),
            ("wrong-slot", dispatcher with { SwitchArmVariable = value }, function.Variables),
            ("wrong-name", dispatcher, function.Variables.SetItem(state.Variable,
                function.Variables[state.Variable] with { Name = "user-state" })),
            ("wrong-type", dispatcher, function.Variables.SetItem(state.Variable,
                function.Variables[state.Variable] with { Type = PrimitiveType.UInt32 })),
            ("immutable", dispatcher, function.Variables.SetItem(state.Variable,
                function.Variables[state.Variable] with { IsMutable = false })),
            ("wrong-ordinal", dispatcher with { Body = arms with { Condition = condition with { Right = one } } },
                function.Variables),
            ("wrong-comparison", dispatcher with { Body = arms with {
                Condition = condition with { Operator = TokenKind.BangEquals },
            } }, function.Variables),
            ("missing-fallback", dispatcher with { Body = arms with { Else = null } }, function.Variables),
            ("self-cycle", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.SetItem(0, writeState) },
            } }, function.Variables),
            ("out-of-range-target", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.SetItem(0, assignment with {
                    Value = new BoundConstant(2, PrimitiveType.Int32, span),
                }) },
            } }, function.Variables),
            ("dynamic-target", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.SetItem(0,
                    assignment with { Value = new BoundRead(value, PrimitiveType.Int32, span) }) },
            } }, function.Variables),
            ("raw-continue", dispatcher with { Body = arms with {
                Then = first with { Statements = [continuation] },
            } }, function.Variables),
            ("nonadjacent-transfer", dispatcher with { Body = arms with {
                Then = first with { Statements = [assignment, readState, continuation] },
            } }, function.Variables),
            ("state-read", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.Insert(0, readState) },
            } }, function.Variables),
            ("state-compound-assignment", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.Insert(0,
                    new BoundExpressionStatement(new BoundCompoundAssignment(
                        TokenKind.PipeEquals, stateRead, one, PrimitiveType.Int32, span), span)) },
            } }, function.Variables),
            ("state-out-argument", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.Insert(0,
                    new BoundExpressionStatement(new BoundCall(1,
                        [new BoundOutArgument(state.Variable, false, PrimitiveType.Int32, span)],
                        PrimitiveType.Int32, span), span)) },
            } }, function.Variables),
            ("implicit-backedge", dispatcher with { Body = arms with {
                Else = last with { Statements = [last.Statements[0]] },
            } }, function.Variables),
            ("cleanup-state-write", dispatcher with { Body = arms with {
                Then = first with { Statements = [nested] },
            } }, function.Variables),
            ("cleanup-state-read", dispatcher with { Body = arms with {
                Then = first with { Statements = [new BoundTryFinally(first,
                    new BoundBlock([readState], span), span)] },
            } }, function.Variables),
            ("misdirected-continue", dispatcher with { Body = arms with {
                Then = first with { Statements = first.Statements.SetItem(1,
                    continuation with { TargetDepth = 2 }) },
            } }, function.Variables),
        };
        Assert.That(NativeJit.Execute(program with { EntryPoint = 0 }, policy), Is.EqualTo(42));
        foreach (var mutation in mutations)
        {
            var invalid = program with {
                Functions = program.Functions.SetItem(0, function with {
                    Variables = mutation.Variables,
                    Body = function.Body with {
                        Statements = function.Body.Statements.SetItem(1, cleanup with {
                            Finally = cleanup.Finally with {
                                Statements = [selection with {
                                    Statements = selection.Statements.SetItem(2, mutation.Loop),
                                }],
                            },
                        }),
                    },
                }),
            };
            var error = TestExceptionCapture.Capture(() => SemanticVerifier.VerifyLibrary(invalid));
            Assert.That(error is ArgumentException, Is.True, $"{mutation.Name}: {error}");
            Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(invalid, ["Main"], policy));
        }
    }

    [Test]
    public async Task ScalarFrontendCleanupSwitchTransfersComposeNestedDispatcherLoopExitsAsync()
    {
        const string text = """
            public int32 Main() {
                try {} finally {
                    switch (1) {
                        1 => {
                            switch (2) { 2 => { continue _; }, _ => {} }
                            continue _;
                        },
                        _ => {},
                    }
                }
                return 42;
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = read.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))();
            var function = global::System.Linq.Enumerable.Single(program.Functions);
            var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual14 = function.Body.Statements[0]; Assert.That(__nunitActual14, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual14!; }))();
            var selection = ((global::System.Func<BoundBlock>)(() => { var __nunitActual15 = global::System.Linq.Enumerable.Single(cleanup.Finally.Statements); Assert.That(__nunitActual15, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual15!; }))();
            var outer = ((global::System.Func<BoundWhile>)(() => { var __nunitActual16 = selection.Statements[2]; Assert.That(__nunitActual16, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual16!; }))();
            var outerArms = ((global::System.Func<BoundIf>)(() => { var __nunitActual17 = outer.Body; Assert.That(__nunitActual17, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual17!; }))();
            var outerFirst = ((global::System.Func<BoundBlock>)(() => { var __nunitActual18 = outerArms.Then; Assert.That(__nunitActual18, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual18!; }))();
            var innerSelection = ((global::System.Func<BoundBlock>)(() => { var __nunitActual19 = outerFirst.Statements[0]; Assert.That(__nunitActual19, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual19!; }))();
            var inner = ((global::System.Func<BoundWhile>)(() => { var __nunitActual20 = innerSelection.Statements[2]; Assert.That(__nunitActual20, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual20!; }))();
            var innerArms = ((global::System.Func<BoundIf>)(() => { var __nunitActual21 = inner.Body; Assert.That(__nunitActual21, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual21!; }))();
            var innerLast = ((global::System.Func<BoundBlock>)(() => { var __nunitActual22 = innerArms.Else; Assert.That(__nunitActual22, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual22!; }))();
            foreach (var kind in new[] { "break", "post-continue", "backedge", "shared-state", "outer-state-write" })
            {
                var span = innerLast.Span;
                BoundStatement exit = kind == "break"
                    ? new BoundBreak(span) { TargetDepth = 2 }
                    : new BoundContinue(span) { TargetDepth = 2 };
                var revisedInner = inner with {
                    Body = innerArms with { Else = innerLast with { Statements = [exit] } },
                };
                if (kind == "shared-state")
                {
                    revisedInner = inner with { SwitchArmVariable = outer.SwitchArmVariable };
                }
                else if (kind == "outer-state-write")
                {
                    revisedInner = inner with {
                        Body = innerArms with {
                            Else = innerLast with {
                                Statements = innerLast.Statements.Insert(0,
                                    new BoundAssignment(outer.SwitchArmVariable,
                                        new BoundConstant(0, PrimitiveType.Int32, span), span)),
                            },
                        },
                    };
                }
                var nested = innerSelection with {
                    Statements = innerSelection.Statements.SetItem(2, revisedInner),
                };
                BoundStatement wrapped = kind is "shared-state" or "outer-state-write" ? nested
                    : new BoundWhile(new BoundConstant(kind == "post-continue" ? 0 : 1, PrimitiveType.Bool, span),
                        new BoundBlock([nested], span), span) { IsPostTested = kind == "post-continue" };
                var candidate = program with {
                    EntryPoint = 0,
                    Functions = program.Functions.SetItem(0, function with {
                        Body = function.Body with {
                            Statements = function.Body.Statements.SetItem(0, cleanup with {
                                Finally = cleanup.Finally with {
                                    Statements = [selection with {
                                        Statements = selection.Statements.SetItem(2, outer with {
                                            Body = outerArms with {
                                                Then = outerFirst with {
                                                    Statements = outerFirst.Statements.SetItem(0, wrapped),
                                                },
                                            },
                                        }),
                                    }],
                                },
                            }),
                        },
                    }),
                };
                if (kind is "break" or "post-continue")
                {
                    SemanticVerifier.Verify(candidate);
                    Assert.That(NativeJit.Execute(candidate, policy), Is.EqualTo(42));
                }
                else
                {
                    Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(candidate));
                    Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(candidate, policy));
                }
            }
        }
    }

    [Test]
    public async Task ScalarFrontendCleanupSwitchTransfersRunDuringExceptionalUnwindAsync()
    {
        const string text = """
            public int32 Main() {
                mut int32 value = 0;
                try {} finally {
                    switch (1) { 1 => { continue _; }, _ => { value = 42; } }
                }
                return value;
            }
            """;
        var policy = NativeRuntimePolicy.CheckedExceptions;
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual23 = read.Program; Assert.That(__nunitActual23, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual23!; }))();
        var function = global::System.Linq.Enumerable.Single(program.Functions);
        var cleanup = ((global::System.Func<BoundTryFinally>)(() => { var __nunitActual24 = function.Body.Statements[1]; Assert.That(__nunitActual24, Is.TypeOf<BoundTryFinally>()); return (BoundTryFinally)__nunitActual24!; }))();
        var failure = new BoundRequire(new BoundConstant(0, PrimitiveType.Bool, cleanup.Span), cleanup.Span);
        var protectedCleanup = cleanup with { Body = cleanup.Body with { Statements = [failure] } };
        var handler = new BoundTryCatch(new BoundBlock([protectedCleanup], cleanup.Span),
            new BoundBlock([], cleanup.Span), null, cleanup.Span);
        var exceptional = program with {
            EntryPoint = 0,
            Functions = program.Functions.SetItem(0, function with {
                Body = function.Body with {
                    Statements = function.Body.Statements.SetItem(1,
                        new BoundUnsafe(new BoundBlock([handler], cleanup.Span), cleanup.Span)),
                },
            }),
        };
        SemanticVerifier.Verify(exceptional);
        Assert.That(NativeJit.Execute(exceptional, policy), Is.EqualTo(42));
        Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(exceptional with {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        }, NativeRuntimePolicy.FailFast));
    }
    [TestCase(NativeRuntimePolicy.FailFast)]
    [TestCase(NativeRuntimePolicy.CheckedExceptions)]
    public async Task ScalarFrontendCleanupSwitchTransfersPreserveCapturedReturnAsync(NativeRuntimePolicy policy)
    {
        const string text = """
            public int32 Main() {
                mut int32 value = 0;
                try { return value; } finally {
                    switch (1) { 1 => { continue _; }, _ => { value = 42; } }
                }
            }
            """;
        var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
        var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes, (BoundRuntimePolicy)policy);
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual25 = read.Program; Assert.That(__nunitActual25, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual25!; }))();
        Assert.That(NativeJit.Execute(program with { EntryPoint = 0 }, policy), Is.EqualTo(0));
    }
}
