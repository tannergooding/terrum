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
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        var program = Assert.IsType<BoundProgram>(read.Program);
        Assert.Equal(42, NativeJit.Execute(program with { EntryPoint = 0 }, policy));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        var program = Assert.IsType<BoundProgram>(read.Program);
        var function = program.Functions[0];
        var cleanup = Assert.IsType<BoundTryFinally>(function.Body.Statements[1]);
        var selection = Assert.IsType<BoundBlock>(Assert.Single(cleanup.Finally.Statements));
        var dispatcher = Assert.IsType<BoundWhile>(selection.Statements[2]);
        var state = Assert.IsType<BoundLocal>(selection.Statements[1]);
        Assert.Equal(state.Variable, dispatcher.SwitchArmVariable);
        var arms = Assert.IsType<BoundIf>(dispatcher.Body);
        var first = Assert.IsType<BoundBlock>(arms.Then);
        var last = Assert.IsType<BoundBlock>(arms.Else);
        var assignment = Assert.IsType<BoundAssignment>(first.Statements[0]);
        var continuation = Assert.IsType<BoundContinue>(first.Statements[1]);
        var condition = Assert.IsType<BoundBinary>(arms.Condition);
        var value = Assert.IsType<BoundLocal>(function.Body.Statements[0]).Variable;
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
        Assert.Equal(42, NativeJit.Execute(program with { EntryPoint = 0 }, policy));
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
            var error = Record.Exception(() => SemanticVerifier.VerifyLibrary(invalid));
            Assert.True(error is ArgumentException, $"{mutation.Name}: {error}");
            Assert.Throws<ArgumentException>(() => NativeEmitter.EmitLibrary(invalid, ["Main"], policy));
        }
    }

    [Fact]
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
            var program = Assert.IsType<BoundProgram>(read.Program);
            var function = Assert.Single(program.Functions);
            var cleanup = Assert.IsType<BoundTryFinally>(function.Body.Statements[0]);
            var selection = Assert.IsType<BoundBlock>(Assert.Single(cleanup.Finally.Statements));
            var outer = Assert.IsType<BoundWhile>(selection.Statements[2]);
            var outerArms = Assert.IsType<BoundIf>(outer.Body);
            var outerFirst = Assert.IsType<BoundBlock>(outerArms.Then);
            var innerSelection = Assert.IsType<BoundBlock>(outerFirst.Statements[0]);
            var inner = Assert.IsType<BoundWhile>(innerSelection.Statements[2]);
            var innerArms = Assert.IsType<BoundIf>(inner.Body);
            var innerLast = Assert.IsType<BoundBlock>(innerArms.Else);
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
                    Assert.Equal(42, NativeJit.Execute(candidate, policy));
                }
                else
                {
                    Assert.Throws<ArgumentException>(() => SemanticVerifier.Verify(candidate));
                    Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(candidate, policy));
                }
            }
        }
    }

    [Fact]
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
        var program = Assert.IsType<BoundProgram>(read.Program);
        var function = Assert.Single(program.Functions);
        var cleanup = Assert.IsType<BoundTryFinally>(function.Body.Statements[1]);
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
        Assert.Equal(42, NativeJit.Execute(exceptional, policy));
        Assert.Throws<ArgumentException>(() => NativeEmitter.Emit(exceptional with {
            RuntimePolicy = BoundRuntimePolicy.FailFast,
        }, NativeRuntimePolicy.FailFast));
    }

    [Theory]
    [InlineData(NativeRuntimePolicy.FailFast)]
    [InlineData(NativeRuntimePolicy.CheckedExceptions)]
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
        var program = Assert.IsType<BoundProgram>(read.Program);
        Assert.Equal(0, NativeJit.Execute(program with { EntryPoint = 0 }, policy));
    }
}
