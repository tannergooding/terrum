// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed partial class NativeSourceImportTests
{
    private const string Utf8ZFixtureKey = "test.application.native-import-abi.NativeFfiUtf8Z";

    [Fact]
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
            Assert.Null(compilation.Program);
            Assert.NotEmpty(compilation.Diagnostics);
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
            Assert.Null(malformedCompilation.Program);
            Assert.NotEmpty(malformedCompilation.Diagnostics);
        }

        var storedLiteral = declaration
            + "\nint32 Main() { ptr<char8> value = utf8z(\"first\"); return 0; }";
        var storedCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(storedLiteral)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.Null(storedCompilation.Program);
        Assert.NotEmpty(storedCompilation.Diagnostics);

        var missingContract = new NativeImportDefinition(Utf8ZFixtureKey, "native-import-abi-probe.dll",
            "NativeFfiUtf8Z", [PrimitiveType.NativeUtf8ZPointer], PrimitiveType.Int32,
            true, false, false, NativeImportDeploymentPolicy.ApplicationDirectory);
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test-native-utf8z-without-contract-v1", [missingContract]));
        _ = Assert.Throws<ArgumentException>(() =>
            new NativeImportCatalog("production-native-utf8z-v1", [CreateUtf8ZImportDefinition()]));
    }

    [Fact]
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
        Assert.False(aliasCompilation.HasErrors, string.Join(Environment.NewLine, aliasCompilation.Diagnostics));

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
        Assert.Null(structCompilation.Program);
        Assert.NotEmpty(structCompilation.Diagnostics);
    }

    [Fact]
    public void Utf8ZLiteralPoolValidatesAsciiTerminationAndSharedStorageLimit()
    {
        var maximum = new string('x', NativeUtf8ZContract.MaximumStorageBytes - 1);
        Assert.True(NativeUtf8ZContract.IsValidLiteral(maximum));
        Assert.False(NativeUtf8ZContract.IsValidLiteral(maximum + "x"));
        Assert.False(NativeUtf8ZContract.IsValidLiteral("before\0after"));
        Assert.False(NativeUtf8ZContract.IsValidLiteral("caf\u00E9"));
        Assert.False(NativeUtf8ZContract.IsValidLiteral(new string('\uD800', 1)));

        var maximumUtf16 = new string('u', NativeUtf16ZContract.MaximumStorageBytes / sizeof(char) - 1);
        Assert.True(NativeStringZPoolContract.IsValid([maximumUtf16], []));
        Assert.False(NativeStringZPoolContract.IsValid([maximumUtf16], ["x"]));
    }

    [Fact]
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
        Assert.False(exactCompilation.HasErrors, string.Join(Environment.NewLine, exactCompilation.Diagnostics));

        var overLimitLiteral = new string('u', NativeStringZPoolContract.MaximumStorageBytes / sizeof(char) - 1);
        var overLimitSource = string.Concat(declaration, "\nvoid Main() { NativeFfiMixedZ(utf16z(\"",
            overLimitLiteral, "\"), utf8z(\"x\")); }");
        var overLimitCompilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(overLimitSource)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.Null(overLimitCompilation.Program);
        Assert.Contains(overLimitCompilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3013");
    }

    [Fact]
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
        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);

        _ = Assert.Throws<ArgumentException>(() => NativeEmitter.EmitWithNativeImportsForTesting(
            program with { NativeUtf8ZLiterals = [] }, NativeRuntimePolicy.FailFast));
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
