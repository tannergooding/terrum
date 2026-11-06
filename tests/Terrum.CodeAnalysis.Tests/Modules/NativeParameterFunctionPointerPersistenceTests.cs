// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Linq;
using NUnit.Framework;
using Terrum.CodeAnalysis.Modules;
using Terrum.CodeAnalysis.Native;
using Terrum.CodeAnalysis.Semantics;
using Terrum.CodeAnalysis.Syntax;
using Terrum.CodeAnalysis.Tests.Semantics;

namespace Terrum.CodeAnalysis.Tests.Modules;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "NUnit instantiates this fixture through reflection.")]
internal sealed class NativeParameterFunctionPointerPersistenceTests
{
    [Test]
    public void NativeImportExpectationsRemapFunctionPointerPointees()
    {
        var original = CreateProvider(reverse: false, changePointee: false);
        var reordered = CreateProvider(reverse: true, changePointee: false);
        var changed = CreateProvider(reverse: true, changePointee: true);
        var originalReference = NativeModuleCompiler.CreateReference(original);
        var reorderedReference = NativeModuleCompiler.CreateReference(reordered);
        Assert.That(originalReference.NativeFunctionPointerSignatures
            .Select(static signature =>
                $"{signature.ReturnType}:{string.Join(",", signature.ParameterTypes)}"),
            Is.Not.EqualTo(reorderedReference.NativeFunctionPointerSignatures
                .Select(static signature =>
                    $"{signature.ReturnType}:{string.Join(",", signature.ParameterTypes)}")));
        var originalPointeeSignatures = originalReference.NativePointerTypes
            .Select(pointer =>
            {
                var signature = originalReference.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(pointer.PointeeType)];
                return (signature.CallingConvention, signature.ReturnType,
                    Parameters: string.Join(",", signature.ParameterTypes));
            }).ToArray();
        var reorderedPointeeSignatures = reorderedReference.NativePointerTypes
            .Select(pointer =>
            {
                var signature = reorderedReference.NativeFunctionPointerSignatures[
                    NativeFunctionPointerTypes.Index(pointer.PointeeType)];
                return (signature.CallingConvention, signature.ReturnType,
                    Parameters: string.Join(",", signature.ParameterTypes));
            }).ToArray();
        Assert.That(originalPointeeSignatures,
            Is.Not.EqualTo(reorderedPointeeSignatures));

        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"), originalReference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, original, NativeRuntimePolicy.FailFast);

        Assert.DoesNotThrow(() => NativeModuleCompiler.Validate(consumer, reordered));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(consumer, changed));
    }

    [Test]
    public void NativeImportExpectationsRemapFunctionPointerPointeesAcrossSetsAndGraphs()
    {
        var original = CreateProvider(reverse: false, changePointee: false);
        var reordered = CreateProvider(reverse: true, changePointee: false);
        var changed = CreateProvider(reverse: true, changePointee: true);
        var extraCompilation = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Extra() { return 0; }"));
        Assert.That(extraCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, extraCompilation.Diagnostics));
        var extra = NativeModuleCompiler.CreateProducer(
            extraCompilation.Program!, ["Extra"], "ExtraProvider",
            NativeRuntimePolicy.FailFast);
        var providers = new[] { original, extra };
        var reorderedProviders = new[] { reordered, extra };
        var changedProviders = new[] { changed, extra };
        const string consumerSource = "int32 Main() { return 0; }";

        var setCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateReferenceSet(providers));
        Assert.That(setCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, setCompilation.Diagnostics));
        var setConsumer = NativeModuleCompiler.CreateConsumerSet(
            setCompilation.Program!, providers, NativeRuntimePolicy.FailFast);
        Assert.DoesNotThrow(() =>
            NativeModuleCompiler.ValidateConsumerSet(setConsumer,
                reorderedProviders));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateConsumerSet(setConsumer,
                changedProviders));

        var graphCompilation = Compilation.Create(
            SyntaxTree.Parse(consumerSource),
            NativeModuleCompiler.CreateGraphReference(providers));
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graphConsumer = NativeModuleCompiler.CreateGraphConsumer(
            graphCompilation.Program!, providers, NativeRuntimePolicy.FailFast);
        Assert.DoesNotThrow(() =>
            NativeModuleCompiler.ValidateGraph(graphConsumer,
                reorderedProviders));
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.ValidateGraph(graphConsumer,
                changedProviders));
    }

    private static byte[] CreateProvider(bool reverse, bool changePointee)
    {
        var callback32 = CallbackImport("ObserveCallback32",
            changePointee ? "fnptr<int64, int64>" : "fnptr<int32, int32>");
        var callback64 = CallbackImport("ObserveCallback64", "fnptr<int64, int64>");
        var source = reverse ? callback64 + callback32 : callback32 + callback64;
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse(source),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));

        return NativeModuleCompiler.CreateProducer(compilation.Program!,
            ["ObserveCallback32", "ObserveCallback64"], "CallbackProvider",
            NativeRuntimePolicy.FailFast);
    }

    private static string CallbackImport(string name, string callbackType)
        => $$"""
            [Terrum.Interop.NativeImport("callbacks.dll", "{{name}}")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern unsafe int32 {{name}}(
                ptr<{{callbackType}}> callback);
            """;
}
