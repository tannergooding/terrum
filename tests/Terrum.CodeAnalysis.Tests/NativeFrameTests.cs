// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeFrameTests
{
    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        {var __nunitItems0 = code.Functions.Skip(6);        Assert.Multiple(() => {
            foreach (var __nunitItem1 in __nunitItems0)
            {
                {
                    Assert.That(__nunitItem1.UsesFramePointer, Is.True);
                    Assert.That(code.Code[__nunitItem1.Start], Is.EqualTo((byte)0x55));
                    Assert.That(code.Code.AsSpan(__nunitItem1.Start + 8, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0xE5 }), Is.True);
                    Assert.That(__nunitItem1.StackSize <= 0xF00, Is.True);
                }
            }
        });}
        Assert.That(global::System.Linq.Enumerable.Any(code.Functions.Take(6),function => !function.UsesFramePointer),Is.True);
        Assert.That(code.Code.AsSpan().IndexOf(new byte[] { 0x48, 0x89, 0x84, 0x24, 0x20, 0x00, 0x00, 0x00 }) >= 0, Is.True);
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual2 = compilation.Program; Assert.That(__nunitActual2, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual2!; }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        var funclets = code.Functions.Where(function => function.UsesParentFrame).ToArray();
        Assert.That(funclets.Length, Is.EqualTo(2));
        {var __nunitItems1 = funclets;        Assert.Multiple(() => {
            foreach (var __nunitItem3 in __nunitItems1)
            {
                {
                    Assert.That(__nunitItem3.UsesFramePointer, Is.True);
                    Assert.That(code.Code[__nunitItem3.Start], Is.EqualTo((byte)0x55));
                    Assert.That(code.Code.AsSpan(__nunitItem3.Start + 8, 3).SequenceEqual(new byte[] { 0x48, 0x89, 0xD5 }), Is.True);
                }
            }
        });}
    }

    [Test]
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
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual4 = compilation.Program; Assert.That(__nunitActual4, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual4!; }))();
        var code = NativeEmitter.EmitCode(program, NativeRuntimePolicy.FailFast);
        AssertFunctionRangesAreOrdered(code);
        Assert.That(code.Code.Length > 0, Is.True);
        Assert.That(global::System.Linq.Enumerable.Any(code.Functions.Skip(6),function => function.UsesFramePointer),Is.True);
    }

    private static void AssertFunctionRangesAreOrdered(NativeCode code)
    {
        for (var index = 1; index < code.Functions.Length; index++)
        {
            Assert.That(code.Functions[index - 1].Start < code.Functions[index].Start, Is.True);
        }
    }
}
