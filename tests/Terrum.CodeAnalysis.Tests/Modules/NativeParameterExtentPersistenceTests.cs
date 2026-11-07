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
internal sealed class NativeParameterExtentPersistenceTests
{
    [Test]
    public void FullUnsignedExtentSurvivesPrivateModuleRoundTrip()
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("extent.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ObserveValues(ptr<uint32> values);
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        var artifact = ModuleCompiler.CreateProducer(
            WithExtent(source.Program!, uint.MaxValue), ["ObserveValues"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var imported = reference.Functions.Single(static function =>
            function.Name == "ObserveValues").DirectNativeImport!;
        Assert.That(imported.Parameters.Single().Extent, Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void FullUnsignedExtentSurvivesNativeVTablePrivateRoundTrip()
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(3)]
            public extern unsafe uint32 Invoke(ptr<void> receiver, ptr<uint32> values);
            """));
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        var artifact = ModuleCompiler.CreateProducer(
            WithVTableExtent(source.Program!, uint.MaxValue), ["Invoke"]);
        var reference = ModuleCompiler.CreateReference(artifact);
        var method = reference.Functions.Single(static function =>
            function.Name == "Invoke").NativeVTableMethod!;
        Assert.That(method.Parameters.Single().Extent, Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void PrivateModuleProviderReplacementRejectsChangedNativeParameterExtent()
    {
        var provider = CreatePrivateNativeImportProvider(uint.MaxValue);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            unsafe int32 Forward(ptr<uint32> values) {
                unsafe { return ObserveValues(values); }
            }
            int32 Main() { return 0; }
            """), ModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider);
        var importedExpectation = ModuleCompiler
            .ReadConsumerContractSnapshot(consumer).ParsedModule.Functions
            .Single(static function => function.Name == "ObserveValues");
        Assert.That(importedExpectation.ExpectedDirectNativeImport,
            Is.Not.Null);
        Assert.That(importedExpectation.ExpectedDirectNativeImport!.Outputs,
            Is.Empty);
        Assert.That(importedExpectation.ExpectedDirectNativeImport.Parameters
            .Single().Extent, Is.EqualTo(uint.MaxValue));

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, provider));
        var restored = ModuleCompiler.RestoreConsumerAgainstReference(
            consumer, ModuleCompiler.CreateReference(provider));
        var reencoded = ModuleCompiler.CreateConsumer(restored, provider);
        Assert.DoesNotThrow(() => ModuleCompiler.Link(reencoded, provider));
        var replacement = CreatePrivateNativeImportProvider(ushort.MaxValue);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, replacement));
    }

    [Test]
    public void PrivateModuleSetProviderReplacementRejectsChangedNativeParameterExtent()
    {
        var provider = CreatePrivateNativeImportProvider(uint.MaxValue);
        var neutralProvider = CreatePrivateNeutralProvider();
        var providers = new[] { provider, neutralProvider };
        var reference = ModuleCompiler.CreateReferenceSet(providers);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"), reference);
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumerSet(
            consumerCompilation.Program!, providers);

        Assert.DoesNotThrow(() =>
            ModuleCompiler.ValidateConsumerSet(consumer, providers));
        var replacement = CreatePrivateNativeImportProvider(ushort.MaxValue);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.ValidateConsumerSet(
                consumer, [replacement, neutralProvider]));
    }

    [Test]
    public void PrivateGraphLibraryRetainsDirectImportExpectationAcrossReplacement()
    {
        var provider = CreatePrivateNativeImportProvider(uint.MaxValue);
        var providerReference = ModuleCompiler.CreateReference(provider);
        var graphCompilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            public unsafe int32 Forward(ptr<uint32> values) {
                unsafe { return ObserveValues(values); }
            }
            """), providerReference);
        Assert.That(graphCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, graphCompilation.Diagnostics));
        var graph = ModuleCompiler.CreateGraphLibrary(
            graphCompilation.Program!, ["Forward"], providerReference);

        Assert.DoesNotThrow(() => ModuleCompiler.ValidateGraphLibrary(
            graph, ["Forward"], providerReference));
        var replacementReference = ModuleCompiler.CreateReference(
            CreatePrivateNativeImportProvider(ushort.MaxValue));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.ValidateGraphLibrary(
                graph, ["Forward"], replacementReference));
    }

    [Test]
    public void PrivateModuleProviderReplacementRejectsChangedNativeVTableExtent()
    {
        var provider = CreatePrivateNativeVTableProvider(uint.MaxValue);
        var consumerCompilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Forward(ptr<void> receiver, ptr<uint32> values) {
                unsafe {
                    uint32 ignored = Invoke(receiver, values);
                    return int32(ignored);
                }
            }
            int32 Main() { return 0; }
            """), ModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(consumerCompilation.Program!, provider);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, provider));
        var replacement = CreatePrivateNativeVTableProvider(ushort.MaxValue);
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, replacement));
    }

    [Test]
    public void FullUnsignedExtentSurvivesNativeProviderExpectationRoundTrip()
    {
        var provider = CreateNativeProvider(uint.MaxValue);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"),
            NativeModuleCompiler.CreateReference(provider));
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = NativeModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider, NativeRuntimePolicy.FailFast);

        Assert.DoesNotThrow(() => NativeModuleCompiler.Validate(consumer, provider));
        var lowWordProvider = CreateNativeProvider(ushort.MaxValue);
        _ = Assert.Throws<ModuleContractException>(() =>
            NativeModuleCompiler.Validate(consumer, lowWordProvider));
    }

    private static byte[] CreateNativeProvider(uint extent)
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("extent.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ObserveValues(ptr<uint32> values);
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        return NativeModuleCompiler.CreateProducer(
            WithExtent(source.Program!, extent), ["ObserveValues"],
            "ExtentProvider", NativeRuntimePolicy.FailFast);
    }

    private static byte[] CreatePrivateNativeImportProvider(uint extent)
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("extent.dll", "ObserveValues")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 1)]
            public extern int32 ObserveValues(ptr<uint32> values);
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        return ModuleCompiler.CreateProducer(
            WithExtent(source.Program!, extent), ["ObserveValues"]);
    }

    private static byte[] CreatePrivateNativeVTableProvider(uint extent)
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 1)]
            [Terrum.Interop.NativeVTableMethod(3)]
            public extern unsafe uint32 Invoke(ptr<void> receiver, ptr<uint32> value);
            """));
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        return ModuleCompiler.CreateProducer(
            WithVTableExtent(source.Program!, extent), ["Invoke"]);
    }

    private static byte[] CreatePrivateNeutralProvider()
    {
        var source = Compilation.CreateLibrary(SyntaxTree.Parse(
            "public int32 Other() { return int32(1); }"));
        Assert.That(source.HasErrors, Is.False,
            string.Join(Environment.NewLine, source.Diagnostics));

        return ModuleCompiler.CreateProducer(source.Program!, ["Other"]);
    }

    private static BoundProgram WithExtent(BoundProgram program, uint extent)
    {
        var functions = program.Functions.ToBuilder();
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            if (function.Name != "ObserveValues")
            {
                continue;
            }

            var import = function.DirectNativeImport
                ?? throw new AssertionException("Expected a direct NativeImport.");
            var parameters = import.Parameters.ToBuilder();
            parameters[0] = parameters[0] with { Extent = extent };
            functions[index] = function with
            {
                DirectNativeImport = import with
                {
                    Parameters = parameters.MoveToImmutable(),
                },
            };
            return program with { Functions = functions.MoveToImmutable() };
        }

        throw new AssertionException("Expected the bound NativeImport declaration.");
    }

    private static BoundProgram WithVTableExtent(BoundProgram program, uint extent)
    {
        var functions = program.Functions.ToBuilder();
        for (var index = 0; index < functions.Count; index++)
        {
            var function = functions[index];
            if (function.Name != "Invoke")
            {
                continue;
            }

            var method = function.NativeVTableMethod
                ?? throw new AssertionException("Expected a native vtable method.");
            var parameters = method.Parameters.ToBuilder();
            parameters[0] = parameters[0] with { Extent = extent };
            functions[index] = function with
            {
                NativeVTableMethod = method with
                {
                    Parameters = parameters.MoveToImmutable(),
                },
            };
            return program with { Functions = functions.MoveToImmutable() };
        }

        throw new AssertionException("Expected the bound native vtable method.");
    }
}
