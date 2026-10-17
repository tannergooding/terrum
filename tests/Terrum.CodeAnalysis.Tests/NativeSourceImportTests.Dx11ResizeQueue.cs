// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.IO;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private static string ResizeQueueSource => File.ReadAllText(Path.Combine(FindRepositoryRoot(),
        "samples", "design", "win32-dx11-triangle", "window-resize-queue-stage.terrum"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static BoundProgram CompileResizeQueue(string source)
    {
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        SemanticVerifier.VerifyReferences(program);
        return program;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WindowMessagePumpUsesOrdinarySourceControlFlow(bool windows)
    {
        var source = windows ? ResizeQueueSource.Replace("\n", "\r\n", StringComparison.Ordinal) : ResizeQueueSource;
        Assert.That(source.Contains("dx11resizequeueproof", StringComparison.Ordinal), Is.False);

        var program = CompileResizeQueue(source);
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);

        Assert.That(image.AsSpan().IndexOf("ShowWindow\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("GetMessageW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("TranslateMessage\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("DispatchMessageW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("PeekMessageW\0"u8) < 0, Is.True);
    }

    [Test]
    public void ResizeQueueProofNameIsNotReserved()
    {
        const string source = """
            int32 dx11resizequeueproof(int32 value) { return value + 1; }
            int32 Main() { return dx11resizequeueproof(2); }
            """;
        _ = CompileResizeQueue(source);
    }

    [Test]
    public void WindowMessagePumpUsesCataloguedImports()
    {
        var image = NativeEmitter.EmitWithNativeImports(CompileResizeQueue(ResizeQueueSource),
            NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf("user32.dll\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("GetMessageW\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("TranslateMessage\0"u8) >= 0, Is.True);
        Assert.That(image.AsSpan().IndexOf("DispatchMessageW\0"u8) >= 0, Is.True);
    }
}
