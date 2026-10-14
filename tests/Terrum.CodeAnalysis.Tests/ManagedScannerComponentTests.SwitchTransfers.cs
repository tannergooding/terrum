// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ManagedScannerComponentTests
{
    [Test]
    public async Task ScalarFrontendSwitchTransferAuditsIdentityAndNestedLoopDepthAsync()
    {
        const string text = "public int32 Main() { switch (1) { 1 => { while (true) { continue _; } }, _ => { return 42; } } }";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes,
                (BoundRuntimePolicy)policy);
            var projection = global::System.Linq.Enumerable.Single(read.Projection.Functions);
            var transfer = global::System.Linq.Enumerable.Single(projection.Statements, static statement => statement.Kind == 40);
            Assert.That(transfer.Slot, Is.EqualTo(0));
            Assert.That(transfer.Root, Is.EqualTo(-1));
            Assert.That(transfer.Span, Is.EqualTo(new TextSpan(text.IndexOf("continue _;", StringComparison.Ordinal), 11)));
            Assert.That(projection.Locals, Has.Exactly(1).Items);
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = read.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var function = global::System.Linq.Enumerable.Single(program.Functions);
            Assert.That(function.Variables.Length, Is.EqualTo(2));
            var selection = ((global::System.Func<BoundBlock>)(() => { var __nunitActual1 = global::System.Linq.Enumerable.Single(function.Body.Statements); Assert.That(__nunitActual1, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual1!; }))();
            var state = ((global::System.Func<BoundLocal>)(() => { var __nunitActual2 = selection.Statements[1]; Assert.That(__nunitActual2, Is.TypeOf<BoundLocal>()); return (BoundLocal)__nunitActual2!; }))();
            var dispatcher = ((global::System.Func<BoundWhile>)(() => { var __nunitActual3 = selection.Statements[2]; Assert.That(__nunitActual3, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual3!; }))();
            var arms = ((global::System.Func<BoundIf>)(() => { var __nunitActual4 = dispatcher.Body; Assert.That(__nunitActual4, Is.TypeOf<BoundIf>()); return (BoundIf)__nunitActual4!; }))();
            Assert.That(((global::System.Func<BoundRead>)(() => { var __nunitActual6 = ((global::System.Func<BoundBinary>)(() => { var __nunitActual5 = arms.Condition; Assert.That(__nunitActual5, Is.TypeOf<BoundBinary>()); return (BoundBinary)__nunitActual5!; }))().Left; Assert.That(__nunitActual6, Is.TypeOf<BoundRead>()); return (BoundRead)__nunitActual6!; }))().Variable, Is.EqualTo(state.Variable));
            var arm = ((global::System.Func<BoundBlock>)(() => { var __nunitActual7 = arms.Then; Assert.That(__nunitActual7, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual7!; }))();
            var nestedLoop = ((global::System.Func<BoundWhile>)(() => { var __nunitActual8 = arm.Statements[0]; Assert.That(__nunitActual8, Is.TypeOf<BoundWhile>()); return (BoundWhile)__nunitActual8!; }))();
            var nestedBody = ((global::System.Func<BoundBlock>)(() => { var __nunitActual9 = nestedLoop.Body; Assert.That(__nunitActual9, Is.TypeOf<BoundBlock>()); return (BoundBlock)__nunitActual9!; }))();
            Assert.That(((global::System.Func<BoundAssignment>)(() => { var __nunitActual10 = nestedBody.Statements[0]; Assert.That(__nunitActual10, Is.TypeOf<BoundAssignment>()); return (BoundAssignment)__nunitActual10!; }))().Variable, Is.EqualTo(state.Variable));
            Assert.That(((global::System.Func<BoundContinue>)(() => { var __nunitActual11 = nestedBody.Statements[1]; Assert.That(__nunitActual11, Is.TypeOf<BoundContinue>()); return (BoundContinue)__nunitActual11!; }))().TargetDepth, Is.EqualTo(2));
            Assert.That(NativeJit.Execute(program with { EntryPoint = 0 }, policy), Is.EqualTo(42));
        }
    }

    [Test]
    public async Task ScalarFrontendSwitchTransferReaderRejectsTamperedAndStaleRecordsAsync()
    {
        const string text = "public int32 Main() { switch (1) { 1 => { continue _; }, _ => { return 42; } } }";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var source = SourceText.From(text);
            var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var function = global::System.Linq.Enumerable.Single(read.Projection.Functions);
            var transferIndex = Array.FindIndex(function.Statements.ToArray(),
                static statement => statement.Kind == 40);
            var offset = 8 + 332 + transferIndex * 5;
            foreach (var (field, value) in new[] {
                (0, 7), (1, 1), (1, transferIndex), (2, 0), (2, -2),
                (3, function.Statements[transferIndex].Span.Start + 1),
                (4, function.Statements[transferIndex].Span.Length - 1),
            })
            {
                var tampered = (byte[])programBytes.Clone();
                BinaryPrimitives.WriteInt32LittleEndian(
                    tampered.AsSpan((offset + field) * sizeof(int)), value);
                Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
                    source, bytes, tampered, (BoundRuntimePolicy)policy));
            }
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadProgram(
                SourceText.From(text.Replace("continue _", "continue 1", StringComparison.Ordinal)),
                bytes, programBytes, (BoundRuntimePolicy)policy));
        }
    }

    [Test]
    public async Task ScalarFrontendSwitchTransferNestedCleanupRunsBeforeTargetAsync()
    {
        const string text = """
            public int32 Main() {
                mut int32 effects = 0;
                switch (1) {
                    1 => {
                        try {
                            try { continue _; }
                            finally { effects = effects | 1; }
                        } finally { effects = effects == 1 ? 18 : 0; }
                    },
                    _ => { return effects; },
                }
            }
            """;
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes,
                (BoundRuntimePolicy)policy);
            var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual12 = read.Program; Assert.That(__nunitActual12, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual12!; }))();
            Assert.That(NativeJit.Execute(program with { EntryPoint = 0 }, policy), Is.EqualTo(18));
        }
    }

    [Test]
    public async Task ScalarFrontendSwitchTransferCyclesHaveNoNormalCompletionAsync()
    {
        foreach (var declaration in new[] { "public int32", "[NoThrow][DoesNotReturn] public void" })
        {
            var text = $"{declaration} Main() {{ switch (1) {{ 1 => {{ continue _; }}, _ => {{ continue 1; }} }} }}";
            foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
            {
                var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
                var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes,
                    (BoundRuntimePolicy)policy);
                var function = global::System.Linq.Enumerable.Single(read.Projection.Functions);
                Assert.That(function.Statements.Count(static statement => statement.Kind == 40), Is.EqualTo(2));
                var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual13 = read.Program; Assert.That(__nunitActual13, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual13!; }))();
                Assert.That(NativeEmitter.EmitLibrary(program, ["Main"], policy), Is.Not.Empty);
            }
        }
    }
}
