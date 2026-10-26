// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using NUnit.Framework;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;

namespace Terrum.CodeAnalysis.Tests.Semantics;

public sealed class RawTextLiteralBindingTests
{
    [Test]
    public void RawTextLiteralBindsThroughNativeUtf8Z()
    {
        const string source = """""
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiUtf8Z")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]

            extern unsafe int32 NativeFfiUtf8Z(ptr<void> text);
            int32 Main() {
                unsafe {
                    return NativeFfiUtf8Z(utf8z("""raw"""));
                }
            }
            """"";
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);

        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        Assert.That(compilation.Program!.NativeUtf8ZLiterals, Is.EqualTo(new[] { "raw" }));
    }
}
