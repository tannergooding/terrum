// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Xunit;

namespace Terrum.CodeAnalysis.Tests;

public sealed class NativeCallbackCatalogTests
{
    private const string ImportKey = "test.generic-callback.invoke";
    private const string WindowImportKey = "test.generic-callback.invoke-window";

    private static NativeImportDefinition CreateImport() =>
        new(ImportKey, "callback-fixture.dll", "Invoke", [PrimitiveType.NativeCallbackAddress],
            PrimitiveType.UInt32, IsNoThrow: true, RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.NativeCallback],
            CallbackParameters = [
                new(0, NativeCallbackContract.TypeName, CreateSignature(),
                    NativeCallbackLifetime.SynchronousNoEscape),
            ],
        };

    private static NativeCallbackSignatureDefinition CreateSignature() =>
        new(PrimitiveType.NativeCallbackAddress, PrimitiveType.UInt32, [PrimitiveType.UInt32]);

    private static NativeImportCatalog CreateCatalog() =>
        NativeImportCatalog.CreateForTesting("test.generic-callback.v1", [CreateImport()]);

    private static NativeImportDefinition CreateWindowImport(
        string sourceTypeName = NativeWindowProcedureContract.TypeName) =>
        new(WindowImportKey, "callback-fixture.dll", "InvokeWindow",
            [PrimitiveType.NativeCallbackAddress], PrimitiveType.Int64, IsNoThrow: true,
            RequiresUnsafeCaller: false, DoesNotReturn: false,
            NativeImportDeploymentPolicy.ApplicationDirectory) {
            ParameterContracts = [NativeImportParameterContract.NativeCallback],
            CallbackParameters = [
                new(0, sourceTypeName, NativeWindowProcedureContract.CallbackSignature,
                    NativeCallbackLifetime.SynchronousNoEscape),
            ],
        };

    [Fact]
    public void CallbackSignatureIsSelectedFromTheNativeCatalog()
    {
        var source = $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 InvokeCallback(Terrum.Interop.NativeCallback callback);

            [NoThrow]
            uint32 Callback(uint32 value) { return value; }

            int32 Main() {
                InvokeCallback(&Callback);
                return 0;
            }
            """;
        var catalog = CreateCatalog();
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
            BoundRuntimePolicy.FailFast);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        var callback = Assert.Single(program.Functions, static function => function.Name == "Callback");
        Assert.True(catalog.MatchesCallbackFunction(PrimitiveType.NativeCallbackAddress, callback));
        Assert.NotEmpty(NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));
    }

    [Fact]
    public void CallbackSignatureRejectsFunctionsWithDifferentParameters()
    {
        var source = $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 InvokeCallback(Terrum.Interop.NativeCallback callback);

            [NoThrow]
            uint32 Callback(int32 value) { return 1; }

            int32 Main() {
                InvokeCallback(&Callback);
                return 0;
            }
            """;
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], CreateCatalog(),
            BoundRuntimePolicy.FailFast);

        Assert.Null(compilation.Program);
        Assert.Contains(compilation.Diagnostics, static diagnostic => diagnostic.Code == "TRM3030");
    }

    [Fact]
    public void CallbackParameterRequiresCatalogSignatureMetadata()
    {
        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-callback.missing-signature.v1", [
                CreateImport() with { CallbackParameters = [] },
            ]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NativeCallback")]
    [InlineData("Terrum..Interop.NativeCallback")]
    [InlineData("Terrum.Interop.Native Callback")]
    [InlineData("1Terrum.Interop.NativeCallback")]
    public void CallbackParameterRequiresQualifiedSourceTypeName(string sourceTypeName)
    {
        var import = CreateImport() with {
            CallbackParameters = [
                new(0, sourceTypeName, CreateSignature(), NativeCallbackLifetime.SynchronousNoEscape),
            ],
        };

        _ = Assert.Throws<ArgumentException>(() => NativeImportCatalog.CreateForTesting(
            "test.generic-callback.invalid-type-name.v1", [import]));
    }

    [Fact]
    public void WindowProcedureAliasRequiresItsCataloguedSignature()
    {
        var source = $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 InvokeCallback(Terrum.Interop.Windows.WNDPROC callback);
            int32 Main() { return 0; }
            """;
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], CreateCatalog(),
            BoundRuntimePolicy.FailFast);

        Assert.Null(compilation.Program);
        Assert.NotEmpty(compilation.Diagnostics);
    }

    [Fact]
    public void CallbackCatalogSupportsWindowHandlesOnlyWhenCallbackSignatureUsesThem()
    {
        Assert.False(CreateCatalog().SupportsParameterType(PrimitiveType.NativeWindowHandle));
        Assert.True(NativeImportCatalog.WindowsX64.SupportsParameterType(PrimitiveType.NativeWindowHandle));
    }

    [Fact]
    public void CallbackSignatureIsSelectedFromEachImportParameter()
    {
        var source = $$"""
            [Terrum.Interop.NativeImport("{{ImportKey}}")]
            [NoThrow]
            extern uint32 InvokeCallback(Terrum.Interop.NativeCallback callback);

            [Terrum.Interop.NativeImport("{{WindowImportKey}}")]
            [NoThrow]
            extern int64 InvokeWindow(Terrum.Interop.NativeCallback callback);

            [NoThrow]
            uint32 Callback(uint32 value) { return value; }

            [NoThrow]
            int64 WindowCallback(Terrum.Interop.Windows.HWND window, uint32 message,
                nuint wParam, nint lParam) { return lParam; }

            int32 Main() {
                InvokeCallback(&Callback);
                InvokeWindow(&WindowCallback);
                return 0;
            }
            """;
        var catalog = NativeImportCatalog.CreateForTesting("test.generic-callback.multiple.v1",
                [CreateImport(), CreateWindowImport(NativeCallbackContract.TypeName)]);
        var compilation = Compilation.CreateWithNativeImports([SyntaxTree.Parse(source)], catalog,
            BoundRuntimePolicy.FailFast);

        Assert.False(compilation.HasErrors, string.Join(Environment.NewLine, compilation.Diagnostics));
        var program = Assert.IsType<BoundProgram>(compilation.Program);
        Assert.NotEmpty(NativeEmitter.EmitWithNativeImportsForTesting(program, NativeRuntimePolicy.FailFast));

        var mismatchedSource = source.Replace("InvokeCallback(&Callback);",
            "InvokeCallback(&WindowCallback);", StringComparison.Ordinal);
        var mismatched = Compilation.CreateWithNativeImports([SyntaxTree.Parse(mismatchedSource)], catalog,
            BoundRuntimePolicy.FailFast);
        Assert.Null(mismatched.Program);
        Assert.Contains(mismatched.Diagnostics, static diagnostic => diagnostic.Code == "TRM3030");
    }
}
