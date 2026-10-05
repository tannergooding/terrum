// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeFrameTests
{
    [Fact]
    public void SourceFramesSaveRbpWithoutRebasingOutgoingArguments()
    {
        const string Source = """
            struct Value {
                public int32 A; public int32 B; public int32 C;
                public Value(int32 a) { A = a; B = 2; C = 3; }
            }
            int32 Mix(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) {
                return a + b + c + d + e + f;
            }
            int32 Main() {
                Value value = new Value(42);
                return Mix(value.A, 2, 3, 4, 5, 6);
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        Assert.All(code.Functions.Skip(6), function => {
            Assert.True(function.UsesFramePointer);
            Assert.Equal((byte)0x55, code.Code[function.Start]);
            Assert.True(code.Code.AsSpan(function.Start + 8, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0xE5 }));
            Assert.True(function.StackSize <= 0xF00);
        });
        Assert.Contains(code.Functions.Take(6), function => !function.UsesFramePointer);
        Assert.True(code.Code.AsSpan().IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x20, 0x00, 0x00, 0x00 }) >= 0);
    }

    [Fact]
    public void CleanupFuncletsUseTheParentFrameWithTheirOwnCallArea()
    {
        const string Source = """
            [NoThrow] int32 Sixth(int32 a, int32 b, int32 c, int32 d, int32 e, int32 f) { return f; }
            int32 Main() {
                mut int32 value = 0;
                try {
                    try { value = 1; }
                    finally { value = Sixth(1, 2, 3, 4, 5, 42); }
                }
                finally { value = Sixth(1, 2, 3, 4, 5, value); }
                return value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        var funclets = code.Functions.Where(function => function.UsesParentFrame).ToArray();
        Assert.Equal(2, funclets.Length);
        Assert.All(funclets, function => {
            Assert.True(function.UsesFramePointer);
            Assert.Equal((byte)0x55, code.Code[function.Start]);
            Assert.True(code.Code.AsSpan(function.Start + 8, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0xD5 }));
        });
    }

    [Fact]
    public void OptionalAssignmentToOutParametersEmitsNativeCode()
    {
        const string Source = """
            bool SetIfEmpty(out ManagedNode? first, out ManagedNode? second) {
                first = null;
                second = null;
                first ??= null;
                second ??= null;
                return true;
            }
            int32 Main() { return 42; }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(Source));
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        Assert.True(code.Code.Length > 0);
        Assert.Contains(code.Functions.Skip(6), function => function.UsesFramePointer);
    }

    private static void AssertFunctionRangesAreOrdered(NativeCode code)
    {
        for (var index = 1; index < code.Functions.Length; index++)
        {
            Assert.True(code.Functions[index - 1].Start < code.Functions[index].Start);
        }
    }
}
