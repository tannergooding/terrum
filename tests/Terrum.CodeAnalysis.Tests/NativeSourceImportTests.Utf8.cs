// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    [Test]
    public void Utf8ZRequiresAnAsciiLiteralAtAnExplicitNoEscapeArgument()
    {
        var declaration = """
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiUtf8Z")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(4, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(6, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 NativeFfiUtf8Z(ptr<void> first, uint32 second, uint64 third,
                ptr<void> fourth, uint32 fifth, ptr<void> sixth);
            """;

        foreach (var firstArgument in new[] {
            "utf8z(\"bad\\0text\")",
            "utf8z(\"caf\\u00E9\")",
            "utf8z(\"first\" + \"suffix\")",
            "\"first\"",
        })
        {
            var source = $"{declaration}\nint32 Main() {{ unsafe {{ return NativeFfiUtf8Z({firstArgument}, 1, 2, "
                + "utf8z(\"second\"), 3, utf8z(\"third\")); } }";
            var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        foreach (var malformedType in new[] {
            "ptr<write char8>",
            "ptr<char8?>",
            "ptr<char8<Unknown>>",
        })
        {
            var malformedDeclaration = declaration.Replace("ptr<void> first", $"{malformedType} first",
                StringComparison.Ordinal);
            var malformedCompilation = Compilation.Create(
                SyntaxTree.Parse(malformedDeclaration + "\nvoid Main() {}"), BoundRuntimePolicy.FailFast);
            Assert.That(malformedCompilation.Program, Is.Null);
            Assert.That(malformedCompilation.Diagnostics, Is.Not.Empty);
        }

        var storedLiteral = declaration
            + "\nint32 Main() { ptr<void> value = utf8z(\"first\"); return 0; }";
        var storedCompilation = Compilation.Create(SyntaxTree.Parse(storedLiteral), BoundRuntimePolicy.FailFast);
        Assert.That(storedCompilation.Program, Is.Null);
        Assert.That(storedCompilation.Diagnostics, Is.Not.Empty);

        var escapingFunction = """
            unsafe void Escape(ptr<void> value) { }
            int32 Main() { unsafe { Escape(utf8z("native")); } return 0; }
            """;
        var escapingCompilation = Compilation.Create(
            SyntaxTree.Parse(escapingFunction), BoundRuntimePolicy.FailFast);
        Assert.That(escapingCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(escapingCompilation.Diagnostics,
            static diagnostic => diagnostic.Code == "TRM3011"), Is.True);

    }

    [Test]
    public void Utf8PointerElementDoesNotOverrideSourceTypesNamedChar8()
    {
        var aliasSource = """
            alias char8 = char16;
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiWideAlias")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe void NativeFfiWideAlias(ptr<char8> text);
            void Main() { unsafe { NativeFfiWideAlias(utf16z("wide")); } }
            """;
        var aliasCompilation = Compilation.Create(
            SyntaxTree.Parse(aliasSource), BoundRuntimePolicy.FailFast);
        Assert.That(aliasCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, aliasCompilation.Diagnostics));

        var structSource = """
            struct char8 { int32 Value; }
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiUtf8Z")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 NativeFfiUtf8Z(ptr<char8> text);
            int32 Main() { unsafe { return NativeFfiUtf8Z(utf8z("wide")); } }
            """;
        var structCompilation = Compilation.Create(
            SyntaxTree.Parse(structSource), BoundRuntimePolicy.FailFast);
        Assert.That(structCompilation.Program, Is.Null);
        Assert.That(structCompilation.Diagnostics, Is.Not.Empty);
    }

    [Test]
    public void Utf8ZLiteralPoolValidatesAsciiTerminationAndSharedStorageLimit()
    {
        var maximum = new string('x', NativeUtf8ZContract.MaximumStorageBytes - 1);
        Assert.That(NativeUtf8ZContract.IsValidLiteral(maximum), Is.True);
        Assert.That(NativeUtf8ZContract.IsValidLiteral(maximum + "x"), Is.False);
        Assert.That(NativeUtf8ZContract.IsValidLiteral("before\0after"), Is.False);
        Assert.That(NativeUtf8ZContract.IsValidLiteral("caf\u00E9"), Is.False);
        Assert.That(NativeUtf8ZContract.IsValidLiteral(new string('\uD800', 1)), Is.False);

        var maximumUtf16 = new string('u', NativeUtf16ZContract.MaximumStorageBytes / sizeof(char) - 1);
        Assert.That(NativeStringZPoolContract.IsValid([maximumUtf16], []), Is.True);
        Assert.That(NativeStringZPoolContract.IsValid([maximumUtf16], ["x"]), Is.False);
    }

    [Test]
    public void Utf8AndUtf16LiteralsShareTheAggregateStorageLimitDuringBinding()
    {
        var declaration = """
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiMixedZ")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe void NativeFfiMixedZ(ptr<void> wide, ptr<void> narrow);
            """;
        var exactLimitLiteral = new string('u', NativeStringZPoolContract.MaximumStorageBytes / sizeof(char) - 2);
        var exactSource = string.Concat(declaration, "\nvoid Main() { unsafe { NativeFfiMixedZ(utf16z(\"",
            exactLimitLiteral, "\"), utf8z(\"x\")); } }");
        var exactCompilation = Compilation.Create(SyntaxTree.Parse(exactSource), BoundRuntimePolicy.FailFast);
        Assert.That(exactCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, exactCompilation.Diagnostics));

        var overLimitLiteral = new string('u', NativeStringZPoolContract.MaximumStorageBytes / sizeof(char) - 1);
        var overLimitSource = string.Concat(declaration, "\nvoid Main() { unsafe { NativeFfiMixedZ(utf16z(\"",
            overLimitLiteral, "\"), utf8z(\"x\")); } }");
        var overLimitCompilation = Compilation.Create(SyntaxTree.Parse(overLimitSource), BoundRuntimePolicy.FailFast);
        Assert.That(overLimitCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(overLimitCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3013"),Is.True);
    }

    [Test]
    public void Utf8ZLiteralIndexIsRecheckedBeforeNativeEmission()
    {
        var source = """
            [Terrum.Interop.NativeImport("fixture.dll", "NativeFfiUtf8Z")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(4, Read, NoEscape, 0)]
            [Terrum.Interop.NativeParameter(6, Read, NoEscape, 0)]
            [NoThrow]
            extern unsafe int32 NativeFfiUtf8Z(ptr<void> first, uint32 second, uint64 third,
                ptr<void> fourth, uint32 fifth, ptr<void> sixth);
            int32 Main() {
                unsafe {
                    return NativeFfiUtf8Z(utf8z("first"), 1, 2,
                        utf8z("second"), 3, utf8z("third"));
                }
            }
            """;
        var compilation = Compilation.Create(SyntaxTree.Parse(source), BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImports(
            program with { NativeUtf8ZLiterals = [] }, NativeRuntimePolicy.FailFast))!;
    }

}
