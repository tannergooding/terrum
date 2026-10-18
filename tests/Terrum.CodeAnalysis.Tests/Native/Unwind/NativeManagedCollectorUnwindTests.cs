// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests.Native.Unwind;

public sealed class NativeManagedCollectorUnwindTests
{
    [TestCase(BoundRuntimePolicy.FailFast)]
    [TestCase(BoundRuntimePolicy.CheckedExceptions)]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var code = NativeEmitter.EmitCode(program, (NativeRuntimePolicy)policy);
        var collector = code.Functions[^1];
        Assert.That(collector.StackSize, Is.EqualTo(120));
        Assert.That(code.Code[collector.End - 1], Is.EqualTo(0xC3));
        Assert.That(collector.End + 3 < code.Code.Length, Is.True);
        Assert.That(code.Code[collector.End], Is.EqualTo(0x48));
        Assert.That(code.Code[collector.End + 1], Is.EqualTo(0x85));
        Assert.That(code.Code[collector.End + 2], Is.EqualTo(0xC0));
        var callsToHelper = Enumerable.Range(collector.Start, collector.End - collector.Start - 4)
            .Where(at => code.Code[at] == 0xE8
                && at + 5 + BinaryPrimitives.ReadInt32LittleEndian(code.Code.AsSpan(at + 1, 4))
                    == collector.End).ToArray();
        Assert.That(callsToHelper.Length, Is.EqualTo(2));
        Assert.That(global::System.Linq.Enumerable.Any(code.Functions,range => range.Start <= collector.End && collector.End < range.End),Is.False);
    }
}
