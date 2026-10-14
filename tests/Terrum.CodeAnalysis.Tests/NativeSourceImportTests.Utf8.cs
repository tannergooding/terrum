// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using NUnit.Framework;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string Utf8ZFixtureKey = "test.application.native-import-abi.NativeFfiUtf8Z";

    [Test]
    public void Utf8ZRequiresAnAsciiLiteralAtACataloguedNoEscapeArgument()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-native-utf8z-v1",
            [CreateUtf8ZImportDefinition()]);
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{Utf8ZFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiUtf8Z(ptr<char8> first, uint32 second, uint64 third,
                ptr<char8> fourth, uint32 fifth, ptr<char8> sixth);
            """;

        foreach (var firstArgument in new[] {
            "utf8z(\"bad\\0text\")",
            "utf8z(\"caf\\u00E9\")",
            "utf8z(\"first\" + \"suffix\")",
            "\"first\"",
        })
        {
            var source = $"{declaration}\nint32 Main() {{ return NativeFfiUtf8Z({firstArgument}, 1, 2, "
                + "utf8z(\"second\"), 3, utf8z(\"third\")); }";
            var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
                BoundRuntimePolicy.FailFast);
            Assert.That(compilation.Program, Is.Null);
            Assert.That(compilation.Diagnostics, Is.Not.Empty);
        }

        foreach (var malformedType in new[] {
            "ptr<write char8>",
            "ptr<char8?>",
            "ptr<char8<Unknown>>",
        })
        {
            var malformedDeclaration = declaration.Replace("ptr<char8> first", $"{malformedType} first",
                StringComparison.Ordinal);
            var malformedCompilation = Compilation.CreateWithNativeImports(
                [SyntaxTree.Parse(malformedDeclaration + "\nvoid Main() {}")], catalog,
                BoundRuntimePolicy.FailFast);
            Assert.That(malformedCompilation.Program, Is.Null);
            Assert.That(malformedCompilation.Diagnostics, Is.Not.Empty);
        }

        var storedLiteral = declaration
            + "\nint32 Main() { ptr<char8> value = utf8z(\"first\"); return 0; }";
        var storedCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(storedLiteral)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(storedCompilation.Program, Is.Null);
        Assert.That(storedCompilation.Diagnostics, Is.Not.Empty);

        foreach (var escapingFunction in new[] {
            declaration + "\nptr<char8> Escape(ptr<char8> value) { return value; }\nvoid Main() {}",
            declaration + "\nvoid Escape(ptr<char8> value) {}\nvoid Main() {}",
        })
        {
            var escapingCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(escapingFunction)],
                catalog, BoundRuntimePolicy.FailFast);
            Assert.That(escapingCompilation.Program, Is.Null);
            Assert.That(escapingCompilation.Diagnostics, Is.Not.Empty);
        }

        var missingContract = new NativeImportDefinition(Utf8ZFixtureKey, "native-import-abi-probe.dll",
            "NativeFfiUtf8Z", [PrimitiveType.NativeUtf8ZPointer], PrimitiveType.Int32,
            true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-native-utf8z-without-contract-v1", [missingContract]))!;
        var productionCatalog = new NativeImportCatalog("production-native-utf8z-v1",
            [CreateUtf8ZImportDefinition()]);
        Assert.That(productionCatalog.SupportsParameterType(PrimitiveType.NativeUtf8ZPointer), Is.True);
    }

    [Test]
    public void Utf8PointerElementDoesNotOverrideSourceTypesNamedChar8()
    {
        const string importKey = "test.application.native-import-abi.NativeFfiWideAlias";
        var wideImport = new NativeImportDefinition(importKey, "native-import-abi-probe.dll",
            "NativeFfiUtf16Z", [PrimitiveType.NativeUtf16ZPointer], PrimitiveType.Void,
            true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.ReadonlyUtf16ZNoEscape],
        };
        var wideCatalog = NativeImportCatalog.CreateForTesting("test-native-char8-alias-v1", [wideImport]);
        var aliasSource = $$"""
            alias char8 = char16;
            [Terrum.Interop.NativeImport("{{importKey}}")]
            [NoThrow]
            extern void NativeFfiWideAlias(ptr<char8> text);
            void Main() { NativeFfiWideAlias(utf16z("wide")); }
            """;
        var aliasCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(aliasSource)], wideCatalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(aliasCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, aliasCompilation.Diagnostics));

        var utf8Catalog = NativeImportCatalog.CreateForTesting("test-native-char8-struct-v1",
            [CreateUtf8ZImportDefinition()]);
        var structSource = $$"""
            struct char8 { int32 Value; }
            [Terrum.Interop.NativeImport("{{Utf8ZFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiUtf8Z(ptr<char8> first, uint32 second, uint64 third,
                ptr<char8> fourth, uint32 fifth, ptr<char8> sixth);
            void Main() {}
            """;
        var structCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(structSource)], utf8Catalog,
            BoundRuntimePolicy.FailFast);
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
        const string importKey = "test.application.native-import-abi.NativeFfiMixedZ";
        var definition = new NativeImportDefinition(importKey, "native-import-abi-probe.dll",
            "NativeFfiUtf8Z", [PrimitiveType.NativeUtf16ZPointer, PrimitiveType.NativeUtf8ZPointer],
            PrimitiveType.Void, true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyUtf16ZNoEscape,
                NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
            ],
        };
        var catalog = NativeImportCatalog.CreateForTesting("test-native-mixedz-v1", [definition]);
        var declaration = $$"""
            [Terrum.Interop.NativeImport("{{importKey}}")]
            [NoThrow]
            extern void NativeFfiMixedZ(ptr<char16> wide, ptr<char8> narrow);
            """;
        var exactLimitLiteral = new string('u', NativeStringZPoolContract.MaximumStorageBytes / sizeof(char) - 2);
        var exactSource = string.Concat(declaration, "\nvoid Main() { NativeFfiMixedZ(utf16z(\"",
            exactLimitLiteral, "\"), utf8z(\"x\")); }");
        var exactCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(exactSource)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(exactCompilation.HasErrors, Is.False, string.Join(Environment.NewLine, exactCompilation.Diagnostics));

        var overLimitLiteral = new string('u', NativeStringZPoolContract.MaximumStorageBytes / sizeof(char) - 1);
        var overLimitSource = string.Concat(declaration, "\nvoid Main() { NativeFfiMixedZ(utf16z(\"",
            overLimitLiteral, "\"), utf8z(\"x\")); }");
        var overLimitCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(overLimitSource)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(overLimitCompilation.Program, Is.Null);
        Assert.That(global::System.Linq.Enumerable.Any(overLimitCompilation.Diagnostics,static diagnostic => diagnostic.Code == "TRM3013"),Is.True);
    }

    [Test]
    public void Utf8ZLiteralIndexIsRecheckedBeforeNativeEmission()
    {
        var catalog = NativeImportCatalog.CreateForTesting("test-native-utf8z-v1",
            [CreateUtf8ZImportDefinition()]);
        var source = $$"""
            [Terrum.Interop.NativeImport("{{Utf8ZFixtureKey}}")]
            [NoThrow]
            extern int32 NativeFfiUtf8Z(ptr<char8> first, uint32 second, uint64 third,
                ptr<char8> fourth, uint32 fifth, ptr<char8> sixth);
            int32 Main() {
                return NativeFfiUtf8Z(utf8z("first"), 1, 2,
                    utf8z("second"), 3, utf8z("third"));
            }
            """;
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.That(compilation.HasErrors, Is.False, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = ((global::System.Func<BoundProgram>)(() => { var __nunitActual0 = compilation.Program; Assert.That(__nunitActual0, Is.TypeOf<BoundProgram>()); return (BoundProgram)__nunitActual0!; }))();

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
            program with { NativeUtf8ZLiterals = [] }, NativeRuntimePolicy.FailFast))!;
    }

    private static NativeImportDefinition CreateUtf8ZImportDefinition() =>
        new(Utf8ZFixtureKey, "native-import-abi-probe.dll", "NativeFfiUtf8Z",
            [PrimitiveType.NativeUtf8ZPointer, PrimitiveType.UInt32, PrimitiveType.UInt64,
                PrimitiveType.NativeUtf8ZPointer, PrimitiveType.UInt32, PrimitiveType.NativeUtf8ZPointer],
            PrimitiveType.Int32, true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [
                NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
                NativeImportParameterContract.Value,
                NativeImportParameterContract.ReadonlyUtf8ZNoEscape,
            ],
        };
}
