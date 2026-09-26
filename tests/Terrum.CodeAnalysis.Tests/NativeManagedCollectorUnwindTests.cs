// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeManagedCollectorUnwindTests
{
    [Theory]
    [InlineData(BoundRuntimePolicy.FailFast)]
    [InlineData(BoundRuntimePolicy.CheckedExceptions)]
    public void LeafMarkHelperIsOutsideCollectorUnwindRange(BoundRuntimePolicy policy)
    {
        var source = """
            class Leaf {
                public int32 Value;
                public Leaf(int32 value) { Value = value; }
            }
            int32 Main() {
                require Leaf.TryCreate(42, out var leaf);
                return ManagedHeap.Collect() + leaf.Value;
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), policy);
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var code = NativeEmitter.EmitCode(program, (NativeRuntimePolicy)policy);
        var collector = code.Functions[^1];
        Assert.Equal(120, collector.StackSize);
        Assert.Equal(0xC3, code.Code[collector.End - 1]);
        Assert.True(collector.End + 3 < code.Code.Length);
        Assert.Equal(0x48, code.Code[collector.End]);
        Assert.Equal(0x85, code.Code[collector.End + 1]);
        Assert.Equal(0xC0, code.Code[collector.End + 2]);
        var callsToHelper = Enumerable.Range(collector.Start, collector.End - collector.Start - 4)
            .Where(at => code.Code[at] == 0xE8
                && at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.Code.AsSpan(at + 1, 4))
                    == collector.End).ToArray();
        Assert.Equal(2, callsToHelper.Length);
        Assert.DoesNotContain(code.Functions,
            range => range.Start <= collector.End && collector.End < range.End);
    }
}
