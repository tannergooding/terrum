// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public void WindowsMessageFieldAccessorsBindAndEmitFromOrdinarySource()
    {
        const string source = """
            [Terrum.Interop.NativeImport("windows-x64.system32.user32.PeekMessageW")]
            [NoThrow]
            extern int32 PeekMessageW(
                Terrum.Interop.Windows.MSGAddress message,
                Terrum.Interop.Windows.HWND window,
                uint32 minimum,
                uint32 maximum,
                uint32 remove);

            int32 Main() {
                mut Terrum.Interop.Windows.MSG message = msgzero();
                PeekMessageW(&message, Terrum.Interop.Windows.HWND.NoOwner, 0, 0, 1);
                uint32 messageId = msgid(&message);
                nuint wParam = msgwparam(&message);
                if (messageId == 0 && wParam == 0) {
                    return 0;
                }
                return 1;
            }
            """;
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse(source),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue0 = compilation.Program;
        Assert.That(__nunitAssertValue0, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue0);
        var accessors = program.Functions.Where(NativeWindowMessageContract.IsAccessorFunction).ToArray();

        Assert.That(accessors.Length, Is.EqualTo(2));
        Assert.That(accessors, Has.Some.Matches(static accessor => accessor.Name == NativeWindowMessageContract.IdAccessorName));
        Assert.That(accessors, Has.Some.Matches(static accessor => accessor.Name == NativeWindowMessageContract.WParamAccessorName));
        var image = NativeEmitter.EmitWithNativeImports(program, NativeRuntimePolicy.CheckedExceptions);
        Assert.That(image.AsSpan().IndexOf("PeekMessageW\0"u8) >= 0, Is.True);
    }

    [Test]
    public void UnusedWindowsMessageFieldAccessorsAreNotInjected()
    {
        var compilation = Compilation.CreateWithNativeImports(SyntaxTree.Parse("int32 Main() { return 0; }"),
            BoundRuntimePolicy.CheckedExceptions);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var __nunitAssertValue1 = compilation.Program;
        Assert.That(__nunitAssertValue1, Is.TypeOf<BoundProgram>());
        var program = ((BoundProgram)__nunitAssertValue1);

        Assert.That(NativeWindowMessageContract.IsAccessorFunction, Does.Not.Contain(program.Functions));
    }
}
