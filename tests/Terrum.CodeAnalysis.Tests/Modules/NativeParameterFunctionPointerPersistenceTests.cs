// Copyright © Tanner Gooding and Contributors. Licensed under the MIT License (MIT). See License.md in the repository root for more information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
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

    [Test]
    public void NativeParameterInvocationRowsSurvivePrivatePersistence()
    {
        var program = CreatePrivateInvocationRowProgram();
        var sourceSignature = program
            .NativeFunctionPointerSignatures
            .Single(static candidate =>
                !candidate.NativeParameters.IsDefaultOrEmpty);
        Assert.That(sourceSignature.NativeParameters.Single().Extent,
            Is.EqualTo(4));

        var producer = ModuleCompiler.CreateProducer(program,
            ["Observe", "ObserveWide", "Address"]);
        var reference = ModuleCompiler.CreateReference(producer);
        var persistedExtents = reference.NativeFunctionPointerSignatures
            .Where(static candidate =>
                !candidate.NativeParameters.IsDefaultOrEmpty)
            .SelectMany(static candidate => candidate.NativeParameters)
            .Select(static parameter => parameter.Extent);
        Assert.That(persistedExtents, Is.EquivalentTo([4u, 8u]));
    }

    [Test]
    public void PrivateAddressContractPersistsOutputAndDependencyVectors()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadAndProbe")]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 4)]
            public extern unsafe int32 ReadAndProbe(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.NonZero)]
                write ptr<ptr<int32>> result,
                ptr<uint32> values);

            public int32 Address() {
                unsafe {
                    var readAndProbe = &ReadAndProbe;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var sourceSignature = compilation.Program!
            .NativeFunctionPointerSignatures
            .Single(static signature => !signature.OutputStates.IsEmpty);
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadAndProbe", "Address"]);

        var reference = ModuleCompiler.CreateReference(producer);
        var persistedSignature = reference.NativeFunctionPointerSignatures
            .Single(static signature => !signature.OutputStates.IsEmpty);
        Assert.That(persistedSignature.CallingConvention,
            Is.EqualTo(sourceSignature.CallingConvention));
        Assert.That(persistedSignature.EffectsMask,
            Is.EqualTo(sourceSignature.EffectsMask));
        Assert.That(persistedSignature.Selectors,
            Is.EqualTo(sourceSignature.Selectors));
        Assert.That(persistedSignature.OutputStates,
            Is.EqualTo(sourceSignature.OutputStates));
        Assert.That(persistedSignature.Dependencies,
            Is.EqualTo(sourceSignature.Dependencies));
        Assert.That(persistedSignature.NativeParameters.Select(
            static parameter => (parameter.ParameterIndex,
                parameter.PointeeType, parameter.Access,
                parameter.Retention, parameter.Extent)),
            Is.EqualTo(sourceSignature.NativeParameters.Select(
                static parameter => (parameter.ParameterIndex,
                    parameter.PointeeType, parameter.Access,
                    parameter.Retention, parameter.Extent))));
    }

    [Test]
    public void PrivateBodylessOutputAddressContractRejectsChangedProvider()
    {
        var provider = CreatePrivateOutputDependencyProvider("NonZero");
        var changedProvider = CreatePrivateOutputDependencyProvider(
            "NonNegative");
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            public int32 Main() {
                    unsafe {
                        var readAndProbe = &ReadAndProbe;
                        return 0;
                    }
            }
            """), ModuleCompiler.CreateReference(provider),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!,
            provider);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, provider));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, changedProvider));
    }

    [TestCase("selector-phase")]
    [TestCase("output-state")]
    [TestCase("dependency-source")]
    public void PrivateReaderRejectsMalformedAddressContractVectors(
        string malformedField)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadAndProbe")]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 4)]
            public extern unsafe int32 ReadAndProbe(
                    ptr<int32> source,
                    [Terrum.Interop.NativeOutput(
                        When: Terrum.Interop.NativeOutputCondition.NonZero)]
                    write ptr<ptr<int32>> result,
                    ptr<uint32> values);

            public int32 Address() {
                    unsafe {
                        var readAndProbe = &ReadAndProbe;
                        return 0;
                    }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadAndProbe", "Address"]);
        var (selectorOffset, outputStateOffset, dependencyOffset)
            = FindPrivateAddressContractVectorOffsets(producer);
        var malformed = producer.ToArray();
        switch (malformedField)
        {
            case "selector-phase":
                    malformed[selectorOffset] = 2;
                    break;
            case "output-state":
                    malformed[outputStateOffset + 8] = 2;
                    break;
            case "dependency-source":
                    malformed[dependencyOffset + 1] = 2;
                    break;
            default:
                    throw new InvalidOperationException(
                        "Unsupported private address-contract mutation.");
        }

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void MultipleNativeParameterRowsSurvivePrivatePersistence()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ObservePair")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 8)]
            public extern unsafe int32 ObservePair(
                ptr<uint32> first, ptr<uint64> second);

            public int32 Address() {
                unsafe {
                    var observe = &ObservePair;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["ObservePair", "Address"]);

        var reference = ModuleCompiler.CreateReference(producer);
        var parameters = reference.NativeFunctionPointerSignatures
            .Single(static signature =>
                signature.NativeParameters.Length == 2)
            .NativeParameters;
        Assert.That(parameters.Select(static parameter =>
            parameter.ParameterIndex), Is.EqualTo([0, 1]));
        Assert.That(parameters.Select(static parameter => parameter.Extent),
            Is.EqualTo([4u, 8u]));
    }

    [Test]
    public void PrivateReaderRejectsMalformedNativeParameterRows()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ObservePair")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 8)]
            public extern unsafe int32 ObservePair(
                ptr<uint32> first, ptr<uint64> second);

            public int32 Address() {
                unsafe {
                    var observe = &ObservePair;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["ObservePair", "Address"]);
        var rowOffset = FindPrivateNativeParameterReservedOffset(producer,
            expectedExtent: 8) - 7;
        var mutations = new Action<byte[]>[] {
            malformed => BinaryPrimitives.WriteUInt32LittleEndian(
                malformed.AsSpan(rowOffset, sizeof(uint)), 2),
            malformed => malformed[rowOffset + 4] = 0,
            malformed => malformed[rowOffset + 5] = 1,
            malformed => malformed[rowOffset + 6] = 2,
            malformed => malformed[rowOffset + 6] = 0,
            malformed => {
                malformed[rowOffset + 6] = 1;
                BinaryPrimitives.WriteUInt32LittleEndian(
                    malformed.AsSpan(rowOffset + 8, sizeof(uint)), 0);
            },
        };

        foreach (var mutate in mutations)
        {
            var malformed = producer.ToArray();
            mutate(malformed);

            _ = Assert.Throws<ModuleFormatException>(() =>
                ModuleCompiler.CreateReference(malformed));
        }
    }

    [Test]
    public void PrivateConsumerBindsCompleteNativeParameterAddressContract()
    {
        var provider = CreatePrivateNativeParameterProvider(4);
        var changedProvider = CreatePrivateNativeParameterProvider(8);
        var reference = ModuleCompiler.CreateReference(provider);
        var compilation = Compilation.Create(SyntaxTree.Parse("""
            int32 Main() {
                unsafe {
                    var observe = &Observe;
                    return 0;
                }
            }
            """), reference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(compilation.Program!,
            provider);

        Assert.DoesNotThrow(() => ModuleCompiler.Link(consumer, provider));
        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(consumer, changedProvider));
    }

    [Test]
    public void PrivateReaderRejectsUnknownAddressContractConvention()
    {
        var producer = ModuleCompiler.CreateProducer(
            CreatePrivateInvocationRowProgram(),
            ["Observe", "ObserveWide", "Address"]);
        var malformed = producer.ToArray();
        var conventionOffset =
            FindPrivateNativeFunctionPointerConventionOffset(malformed);
        malformed[conventionOffset] = byte.MaxValue;

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateReaderRejectsRetiredAddressContractEffectBit()
    {
        var producer = ModuleCompiler.CreateProducer(
            CreatePrivateInvocationRowProgram(),
            ["Observe", "ObserveWide", "Address"]);
        var malformed = producer.ToArray();
        var effectsOffset =
            FindPrivateNativeFunctionPointerEffectsOffset(malformed);
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(effectsOffset, sizeof(uint)), 1);

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateReaderRejectsReservedNativeParameterBytes()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
            public extern unsafe int32 Observe(ptr<uint32> value);
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(
            compilation.Program!, ["Observe"]);
        var malformed = producer.ToArray();
        var reservedOffset = FindPrivateNativeParameterReservedOffset(
            malformed, expectedExtent: 4);
        malformed[reservedOffset] = 1;

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateReaderRejectsDuplicateNativeParameterFormals()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "ObservePair")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
            [Terrum.Interop.NativeParameter(2, Read, NoEscape, 8)]
            public extern unsafe int32 ObservePair(
                ptr<uint32> first, ptr<uint64> second);

            public int32 Address() {
                unsafe {
                    var observe = &ObservePair;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["ObservePair", "Address"]);
        var malformed = producer.ToArray();
        var secondParameterReservedOffset
            = FindPrivateNativeParameterReservedOffset(malformed,
                expectedExtent: 8);
        var secondParameterOffset
            = secondParameterReservedOffset - 7;
        BinaryPrimitives.WriteUInt32LittleEndian(
            malformed.AsSpan(secondParameterOffset, sizeof(uint)), 0);

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateReaderRejectsMissingDirectImportAddressContractRoot()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
            public extern unsafe int32 Observe(ptr<uint32> value);

            public int32 Address() {
                unsafe {
                    var observe = &Observe;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        var producer = ModuleCompiler.CreateProducer(compilation.Program!,
            ["Observe", "Address"]);
        var malformed = producer.ToArray();
        var marker = Encoding.ASCII.GetBytes(
            "NativeAddressContract:fnptr#");
        var offset = FindBytes(malformed, marker);
        malformed[offset] = (byte)'n';

        _ = Assert.Throws<ModuleFormatException>(() =>
            ModuleCompiler.CreateReference(malformed));
    }

    [Test]
    public void PrivateBodylessImportRejectsMissingAddressContractRoot()
    {
        var providerCompilation = Compilation.CreateLibrary(
            SyntaxTree.Parse("""
                [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
                [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
                public extern unsafe int32 Observe(ptr<uint32> value);
                """),
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(providerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, providerCompilation.Diagnostics));
        var provider = ModuleCompiler.CreateProducer(
            providerCompilation.Program!, ["Observe"]);
        var reference = ModuleCompiler.CreateReference(provider);
        var consumerCompilation = Compilation.Create(
            SyntaxTree.Parse("int32 Main() { return 0; }"), reference,
            NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(consumerCompilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, consumerCompilation.Diagnostics));
        var consumer = ModuleCompiler.CreateConsumer(
            consumerCompilation.Program!, provider);
        var malformed = consumer.ToArray();
        var marker = Encoding.ASCII.GetBytes(
            "NativeAddressContract:fnptr#");
        var offset = FindBytes(malformed, marker);
        malformed[offset] = (byte)'n';

        _ = Assert.Throws<ModuleContractException>(() =>
            ModuleCompiler.Link(malformed, provider));
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

    private static BoundProgram CreatePrivateInvocationRowProgram()
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse("""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 4)]
            public extern unsafe int32 Observe(ptr<uint32> value);

            [Terrum.Interop.NativeImport("fixture.dll", "ObserveWide")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, 8)]
            public extern unsafe int64 ObserveWide(ptr<uint64> value);

            public int32 Address() {
                unsafe {
                    var observe = &Observe;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return compilation.Program!;
    }

    private static byte[] CreatePrivateNativeParameterProvider(uint extent)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [Terrum.Interop.NativeImport("fixture.dll", "Observe")]
            [Terrum.Interop.NativeParameter(1, Read, NoEscape, {{extent}})]
            public extern unsafe int32 Observe(ptr<uint32> value);

            public int32 Address() {
                unsafe {
                    var observe = &Observe;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!,
            ["Observe", "Address"]);
    }

    private static byte[] CreatePrivateOutputDependencyProvider(
        string outputCondition)
    {
        var compilation = Compilation.CreateLibrary(SyntaxTree.Parse($$"""
            [param(result): DependsOn(nameof(source))]
            [param(result): ForwardsBacking(nameof(source))]
            [Terrum.Interop.NativeImport("synthetic.dll", "ReadAndProbe")]
            [Terrum.Interop.NativeParameter(3, Read, NoEscape, 4)]
            public extern unsafe int32 ReadAndProbe(
                ptr<int32> source,
                [Terrum.Interop.NativeOutput(
                    When: Terrum.Interop.NativeOutputCondition.{{outputCondition}})]
                write ptr<ptr<int32>> result,
                ptr<uint32> values);

            public int32 Address() {
                unsafe {
                    var readAndProbe = &ReadAndProbe;
                    return 0;
                }
            }
            """), NativeCoreRegistrationTests.CreateRegisteredCompilationOptions());
        Assert.That(compilation.HasErrors, Is.False,
            string.Join(Environment.NewLine, compilation.Diagnostics));
        return ModuleCompiler.CreateProducer(compilation.Program!,
            ["ReadAndProbe", "Address"]);
    }

    private static (int Selector, int OutputState, int Dependency)
        FindPrivateAddressContractVectorOffsets(byte[] module)
    {
        var tableOffset = FindPrivateNativeFunctionPointerSignatureTableOffset(
            module);
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8,
            leaveOpen: true);
        stream.Position = tableOffset;
        var signatureCount = reader.ReadInt32();
        for (var signatureIndex = 0;
            signatureIndex < signatureCount; signatureIndex++)
        {
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            var parameterCount = reader.ReadInt32();
            stream.Position += (long)parameterCount * sizeof(int);
            _ = reader.ReadUInt32();
            var selectorCount = reader.ReadInt32();
            var selectorOffset = checked((int)stream.Position);
            stream.Position += (long)selectorCount * 12;
            var outputStateCount = reader.ReadInt32();
            var outputStateOffset = checked((int)stream.Position);
            stream.Position += (long)outputStateCount * 12;
            var dependencyCount = reader.ReadInt32();
            var dependencyOffset = checked((int)stream.Position);
            stream.Position += (long)dependencyCount * 12;
            var nativeParameterCount = reader.ReadInt32();
            stream.Position += (long)nativeParameterCount * 12;
            if (outputStateCount != 0)
            {
                return (selectorOffset, outputStateOffset,
                    dependencyOffset);
            }
        }

        throw new InvalidOperationException(
            "The private signature table has no output-state vector.");
    }

    private static int FindPrivateNativeFunctionPointerConventionOffset(
        byte[] module)
    {
        var tableOffset = FindPrivateNativeFunctionPointerSignatureTableOffset(
            module);
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8,
            leaveOpen: true);
        stream.Position = tableOffset;
        Assert.That(reader.ReadInt32(), Is.GreaterThan(0));
        return checked((int)stream.Position);
    }

    private static int FindPrivateNativeFunctionPointerEffectsOffset(
        byte[] module)
    {
        var tableOffset = FindPrivateNativeFunctionPointerSignatureTableOffset(
            module);
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8,
            leaveOpen: true);
        stream.Position = tableOffset;
        Assert.That(reader.ReadInt32(), Is.GreaterThan(0));
        _ = reader.ReadByte();
        _ = reader.ReadInt32();
        var parameterCount = reader.ReadInt32();
        stream.Position += (long)parameterCount * sizeof(int);
        return checked((int)stream.Position);
    }

    private static int FindPrivateNativeFunctionPointerSignatureTableOffset(
        byte[] module)
    {
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8,
            leaveOpen: true);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.EqualTo(106));
        SkipString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        return checked((int)stream.Position);
    }

    private static int FindPrivateNativeParameterReservedOffset(
        byte[] module, uint expectedExtent)
    {
        using var stream = new MemoryStream(module, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8,
            leaveOpen: true);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.EqualTo(106));
        SkipString(reader);
        _ = reader.ReadBoolean();
        _ = reader.ReadByte();
        _ = reader.ReadBoolean();
        SkipString(reader);
        Assert.That(reader.ReadInt32(), Is.Zero);
        _ = reader.ReadInt32();
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);
        Assert.That(reader.ReadInt32(), Is.Zero);

        var signatureCount = reader.ReadInt32();
        for (var signatureIndex = 0;
            signatureIndex < signatureCount; signatureIndex++)
        {
            _ = reader.ReadByte();
            _ = reader.ReadInt32();
            var parameterCount = reader.ReadInt32();
            stream.Position += (long)parameterCount * sizeof(int);
            _ = reader.ReadUInt32();
            SkipRows(reader, rowSize: 12);
            SkipRows(reader, rowSize: 12);
            SkipRows(reader, rowSize: 12);
            var nativeParameterCount = reader.ReadInt32();
            for (var parameterIndex = 0;
                parameterIndex < nativeParameterCount; parameterIndex++)
            {
                _ = reader.ReadUInt32();
                _ = reader.ReadByte();
                _ = reader.ReadByte();
                _ = reader.ReadByte();
                var reservedOffset = checked((int)stream.Position);
                _ = reader.ReadByte();
                var extent = reader.ReadUInt32();
                if (extent == expectedExtent)
                {
                    return reservedOffset;
                }
            }
        }

        throw new InvalidOperationException(
            "The expected private NativeParameter row was not found.");
    }

    private static int FindBytes(byte[] bytes, byte[] value)
    {
        for (var index = 0; index <= bytes.Length - value.Length; index++)
        {
            if (bytes.AsSpan(index, value.Length).SequenceEqual(value))
            {
                return index;
            }
        }

        throw new InvalidOperationException(
            "The expected private contract token was not found.");
    }

    private static void SkipRows(BinaryReader reader, int rowSize)
    {
        var count = reader.ReadInt32();
        if (count < 0)
        {
            throw new InvalidOperationException(
                "The private native signature row count is negative.");
        }
        reader.BaseStream.Position += (long)count * rowSize;
    }

    private static void SkipString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
        {
            throw new InvalidOperationException(
                "The private module string length is negative.");
        }
        reader.BaseStream.Position += length;
    }
}
