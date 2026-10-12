// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Text;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class ManagedScannerComponentTests
{
    [Fact]
    public async Task ScalarFrontendSwitchTransferAuditsIdentityAndNestedLoopDepthAsync()
    {
        const string text = "public int32 Main() { switch (1) { 1 => { while (true) { continue _; } }, _ => { return 42; } } }";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var read = ScalarFrontendHost.ReadProgram(SourceText.From(text), bytes, programBytes,
                (BoundRuntimePolicy)policy);
            var projection = Assert.Single(read.Projection.Functions);
            var transfer = Assert.Single(projection.Statements, static statement => statement.Kind == 40);
            Assert.Equal(0, transfer.Slot);
            Assert.Equal(-1, transfer.Root);
            Assert.Equal(new TextSpan(text.IndexOf("continue _;", StringComparison.Ordinal), 11), transfer.Span);
            Assert.Single(projection.Locals);
            var program = Assert.IsType<BoundProgram>(read.Program);
            var function = Assert.Single(program.Functions);
            Assert.Equal(2, function.Variables.Length);
            var selection = Assert.IsType<BoundBlock>(Assert.Single(function.Body.Statements));
            var state = Assert.IsType<BoundLocal>(selection.Statements[1]);
            var dispatcher = Assert.IsType<BoundWhile>(selection.Statements[2]);
            var arms = Assert.IsType<BoundIf>(dispatcher.Body);
            Assert.Equal(state.Variable, Assert.IsType<BoundRead>(
                Assert.IsType<BoundBinary>(arms.Condition).Left).Variable);
            var arm = Assert.IsType<BoundBlock>(arms.Then);
            var nestedLoop = Assert.IsType<BoundWhile>(arm.Statements[0]);
            var nestedBody = Assert.IsType<BoundBlock>(nestedLoop.Body);
            Assert.Equal(state.Variable, Assert.IsType<BoundAssignment>(nestedBody.Statements[0]).Variable);
            Assert.Equal(2, Assert.IsType<BoundContinue>(nestedBody.Statements[1]).TargetDepth);
            Assert.Equal(42, NativeJit.Execute(program with { EntryPoint = 0 }, policy));
        }
    }

    [Fact]
    public async Task ScalarFrontendSwitchTransferReaderRejectsTamperedAndStaleRecordsAsync()
    {
        const string text = "public int32 Main() { switch (1) { 1 => { continue _; }, _ => { return 42; } } }";
        foreach (var policy in new[] { NativeRuntimePolicy.FailFast, NativeRuntimePolicy.CheckedExceptions })
        {
            var (bytes, programBytes, _) = await ProduceScalarProgramAsync(text, policy);
            var source = SourceText.From(text);
            var read = ScalarFrontendHost.ReadProgram(source, bytes, programBytes, (BoundRuntimePolicy)policy);
            var function = Assert.Single(read.Projection.Functions);
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

    [Fact]
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
            var program = Assert.IsType<BoundProgram>(read.Program);
            Assert.Equal(18, NativeJit.Execute(program with { EntryPoint = 0 }, policy));
        }
    }

    [Fact]
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
                var function = Assert.Single(read.Projection.Functions);
                Assert.Equal(2, function.Statements.Count(static statement => statement.Kind == 40));
                var program = Assert.IsType<BoundProgram>(read.Program);
                Assert.NotEmpty(NativeEmitter.EmitLibrary(program, ["Main"], policy));
            }
        }
    }
}
