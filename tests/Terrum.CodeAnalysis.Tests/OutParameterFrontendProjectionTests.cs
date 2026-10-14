// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using Terrum.CodeAnalysis.Lexing;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Text;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed class OutParameterFrontendProjectionTests
{
    [Test]
    public void OutParameterProjectionMatchesHostedBindingAndEmission()
    {
        const string text = "public bool Try(out int32 value) { return false; }";
        var source = SourceText.From(text);
        var words = Projection(text);
        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));
        Assert.That(result.OutParameterMask, Is.EqualTo(1));

        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(source, Bytes(words), policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();
            var actualFunction = global::System.Linq.Enumerable.Single(actual.Functions);
            var hostedFunction = global::System.Linq.Enumerable.Single(hosted.Functions);
            Assert.That(actualFunction.Variables[0].IsOut, Is.True);
            Assert.That(actualFunction.Variables[0].IsMutable, Is.True);
            Assert.That(hostedFunction.Variables[0].IsOut, Is.True);
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Try"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy)));
        }

        Reject(0, 68);
        Reject(16, 0);
        Reject(16, 2);
        Reject(16, -1);

        void Reject(int index, int value)
        {
            var original = words[index];
            words[index] = value;
            Assert.Throws<ArgumentException>(() => ScalarFrontendHost.ReadResult(source, Bytes(words)));
            words[index] = original;
        }
    }

    [Test]
    public void OutParameterMaskPreservesParameterOrdinals()
    {
        const string text = "public bool Try(int32 seed, out int32 first, out int32 second) { return false; }";
        var source = SourceText.From(text);
        var words = Projection(text);
        words[6] = 3;
        words[8] = 1;
        words[11] = text.IndexOf("seed", StringComparison.Ordinal);
        words[12] = 4;
        words[13] = text.IndexOf("first", StringComparison.Ordinal);
        words[14] = 5;
        words[16] = 6;
        words[34] = 1;
        words[35] = text.IndexOf("second", StringComparison.Ordinal);
        words[36] = 6;

        var result = ScalarFrontendHost.ReadResult(source, Bytes(words));
        Assert.That(result.OutParameterMask, Is.EqualTo(6));
        foreach (var policy in new[] { BoundRuntimePolicy.FailFast, BoundRuntimePolicy.CheckedExceptions })
        {
            var actual = ScalarFrontendHost.BindLibrary(source, Bytes(words), policy);
            var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(text), policy);
            Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
            var hosted = ((global::System.Func<BoundProgram>)(() => { var __nunitActual1 = compilation.Program; Assert.That(__nunitActual1, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual1!; }))();
            var actualFunction = global::System.Linq.Enumerable.Single(actual.Functions);
            var hostedFunction = global::System.Linq.Enumerable.Single(hosted.Functions);
            Assert.That(actualFunction.Variables[0].IsOut, Is.False);
            Assert.That(actualFunction.Variables[1].IsOut, Is.True);
            Assert.That(actualFunction.Variables[2].IsOut, Is.True);
            Assert.That(actualFunction.Variables[1].IsOut, Is.EqualTo(hostedFunction.Variables[1].IsOut));
            Assert.That(actualFunction.Variables[2].IsOut, Is.EqualTo(hostedFunction.Variables[2].IsOut));
            Assert.That(NativeEmitter.EmitLibrary(actual, ["Try"], (NativeRuntimePolicy)policy), Is.EqualTo(NativeEmitter.EmitLibrary(hosted, ["Try"], (NativeRuntimePolicy)policy)));
        }
    }

    private static int[] Projection(string text)
    {
        var words = new int[ScalarFrontendHost.WordCount];
        words[0] = 87;
        words[1] = 1;
        words[2] = 1;
        words[5] = 5;
        words[6] = 1;
        words[7] = 1;
        words[9] = text.IndexOf("Try", StringComparison.Ordinal);
        words[10] = 3;
        words[11] = text.IndexOf("value", StringComparison.Ordinal);
        words[12] = 5;
        words[16] = 1;
        words[37] = 1;
        words[38] = words[39] = words[40] = -1;
        words[298] = 1;
        words[42] = 21;
        words[43] = 5;
        words[44] = text.IndexOf("false", StringComparison.Ordinal);
        words[45] = 5;
        words[46] = -1;
        words[47] = -1;
        words[332] = 8;
        words[334] = 0;
        words[335] = text.IndexOf("return", StringComparison.Ordinal);
        words[336] = text.IndexOf(';', words[335]) + 1 - words[335];
        return words;
    }

    private static byte[] Bytes(int[] words)
    {
        var bytes = new byte[words.Length * sizeof(int)];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * sizeof(int)), words[index]);
        }
        return bytes;
    }
}
